using System.Collections.Generic;
using UnityEngine;
using MakeGame.Data;
using MakeGame.Player;

namespace MakeGame.Systems
{
    /// <summary>
    /// 탈출선(배) 제작 엔딩의 진행 상태를 관리하는 시스템.
    /// 총 3단계로 구성되며, 각 단계는 "도면 습득 → 재료 확보 → 제작 완료" 순서로 진행된다.
    /// 1~2단계 도면은 대형(대) 섬에서, 3단계(최종) 도면은 특대 섬에서만 습득할 수 있다.
    /// 단계별 필요 재료는 ItemData 기반으로 관리되며, PlayerInventory에서 실제로 재료를 소모해 투입한다.
    /// </summary>
    public class BoatConstructionSystem : MonoBehaviour
    {
        /// <summary>배 제작 전체 단계 수.</summary>
        public const int TotalStages = 3;

        [Tooltip("현재 진행 중인 단계 (1~3)")]
        public int currentStage = 1;

        [Tooltip("현재 단계의 도면을 습득했는지 여부")]
        public bool hasCurrentStageBlueprint = false;

        [Tooltip("지금까지 완료한 배 제작 최고 단계 (0이면 아직 한 단계도 완료 못함). 뗏목 진행도에 따른 이동 범위 확장 판정에 사용한다.")]
        public int highestCompletedStage = 0;

        [Tooltip("최종 단계까지 전부 완성했는지 여부. TryAdvanceStage가 마지막 단계에서 성공하면 true가 된다.")]
        public bool isFullyComplete = false;

        /// <summary>재료 하나와 필요 수량을 나타낸다 (CraftingRecipe.MaterialRequirement와 동일한 구조).</summary>
        [System.Serializable]
        public class MaterialRequirement
        {
            public ItemData item;
            [Min(1)]
            public int quantity = 1;
        }

        /// <summary>한 단계에서 필요한 재료 목록을 감싸는 래퍼 (Inspector에서 단계별로 리스트를 구성하기 위함).</summary>
        [System.Serializable]
        public class StageRequirements
        {
            public List<MaterialRequirement> materials = new List<MaterialRequirement>();
        }

        [Tooltip("단계별(1~3단계) 필요 재료 설계. 인덱스 0이 1단계, 인덱스 2가 3단계에 대응한다.")]
        public List<StageRequirements> stageMaterialRequirements = new List<StageRequirements>();

        [Tooltip("현재 단계에서 확보(투입)한 재료 수량 목록")]
        public List<MaterialRequirement> collectedMaterialsForCurrentStage = new List<MaterialRequirement>();

        [Header("실체 뗏목")]
        // 새로 추가하는 필드다. 씬에는 이 키가 없으므로 코드 기본값(false)이 그대로 쓰인다.
        // 기본값을 일부러 false(= 켜짐)로 잡았다 - 만약 어떤 이유로든 직렬화가 기본값을 못 살리고
        // default(bool)로 떨어져도 뗏목이 조용히 사라지지 않는다.
        [Tooltip("켜면 월드에 실체 뗏목(RaftStructure)을 만들지 않는다. 진행도 카운터만 남는 예전 동작.")]
        public bool disableRaftStructure = false;

        /// <summary>
        /// 진행도(단계/도면/투입 재료)가 바뀔 때마다 발생한다. RaftStructure가 이걸 받아 외형을 즉시 갱신한다.
        /// [주의] SaveLoadController.Load는 이 컴포넌트의 public 필드를 직접 대입해 복원하므로 이 이벤트가
        /// 발생하지 않는다. 그래서 RaftStructure는 이벤트에만 의존하지 않고 주기적으로 진행도를 다시 읽는다
        /// (불러오기 직후 외형이 옛 단계로 남는 것을 막는 안전망). SaveLoadController 쪽에서 복원 후
        /// NotifyProgressChanged()를 한 번 불러 주면 그 폴링 지연(0.2초)도 사라진다.
        /// </summary>
        public event System.Action ProgressChanged;

        /// <summary>이 세션에서 만들어진 실체 뗏목. 씬 직렬화 대상이 아니도록 필드가 아니라 프로퍼티로 둔다.</summary>
        public RaftStructure Raft { get; private set; }

        /// <summary>
        /// 진행도 변화 통지. 외부(예: 세이브 복원)에서 필드를 직접 바꾼 뒤에도 부를 수 있도록 public이다.
        /// </summary>
        public void NotifyProgressChanged()
        {
            ProgressChanged?.Invoke();
        }

        /// <summary>
        /// 배가 "카운터"에서 "월드에 실제로 서 있는 구조물"이 되도록, 플레이 시작 시 뗏목 본체를 만든다.
        /// 위치 확정(시작 섬 해안 찾기)은 RaftStructure가 스스로 한다 - 이 컴포넌트와 WorldMapManager는
        /// 실행 순서가 보장되지 않아서, 여기서 섬을 읽으려 하면 아직 섬이 없을 수 있기 때문이다.
        /// </summary>
        private void Start()
        {
            EnsureRaftStructure();
        }

        /// <summary>
        /// 실체 뗏목 오브젝트를 확보한다(이미 있으면 그대로 쓴다).
        /// 씬 루트에 만든다 - WorldMapManager.RegenerateWorld(F9 불러오기)가 자기 자식을 전부 파괴하는데,
        /// 이 컴포넌트가 WorldMapManager와 같은 Managers 오브젝트에 붙어 있을 수 있어 transform 아래에
        /// 두면 불러오기 때 뗏목이 함께 지워진다.
        /// </summary>
        public RaftStructure EnsureRaftStructure()
        {
            if (disableRaftStructure)
                return null;

            if (Raft != null)
                return Raft;

            var existing = FindAnyObjectByType<RaftStructure>();
            if (existing != null)
            {
                Raft = existing;
                Raft.boatConstruction = this;
                return Raft;
            }

            // 비활성 상태로 만들어 컴포넌트를 붙이고 참조를 채운 뒤 켠다. AddComponent는 Awake를 즉시
            // 실행하므로, 켜기 전에 배선을 끝내야 RaftStructure가 Awake/OnEnable에서 null을 보지 않는다.
            var go = new GameObject("RaftStructure");
            go.SetActive(false);
            Raft = go.AddComponent<RaftStructure>();
            Raft.boatConstruction = this;
            go.SetActive(true);
            return Raft;
        }

        /// <summary>
        /// 지정한 규모의 섬에서 현재 단계 도면을 습득할 수 있는지 확인한다.
        /// 1~2단계는 대형 섬, 3단계(최종)는 특대 섬에서만 습득 가능하다.
        /// </summary>
        public bool CanFindBlueprintOnIsland(IslandSize islandSize)
        {
            if (currentStage <= 2)
                return islandSize == IslandSize.Large;

            return islandSize == IslandSize.ExtraLarge;
        }

        /// <summary>
        /// 도면을 습득했을 때 호출한다. 현재 단계에 맞는 섬이 아니면 무시한다.
        /// </summary>
        public void ObtainBlueprint(IslandSize islandSize)
        {
            if (!CanFindBlueprintOnIsland(islandSize))
                return;

            hasCurrentStageBlueprint = true;
            NotifyProgressChanged();
        }

        /// <summary>
        /// 현재 단계(currentStage)에 필요한 재료 목록을 반환한다. 설계가 없으면 빈 목록을 반환한다.
        /// </summary>
        public List<MaterialRequirement> GetCurrentStageRequirements()
        {
            int index = currentStage - 1;
            if (index < 0 || index >= stageMaterialRequirements.Count)
                return new List<MaterialRequirement>();

            return stageMaterialRequirements[index].materials;
        }

        /// <summary>
        /// 인벤토리에서 재료를 실제로 소모하여 현재 단계 제작에 투입한다.
        /// 인벤토리에 충분한 수량이 없으면 아무것도 소모하지 않고 실패한다.
        /// </summary>
        public bool ContributeMaterial(PlayerInventory inventory, ItemData item, int quantity)
        {
            if (inventory == null || item == null || quantity <= 0)
                return false;

            if (!inventory.RemoveItems(item, quantity))
                return false;

            AddCollected(item, quantity);
            NotifyProgressChanged(); // 뗏목 외형이 재료를 넣은 그 프레임에 자란다.
            return true;
        }

        /// <summary>
        /// 투입된 재료 수량을 collectedMaterialsForCurrentStage에 누적 기록한다.
        /// </summary>
        private void AddCollected(ItemData item, int quantity)
        {
            foreach (var entry in collectedMaterialsForCurrentStage)
            {
                if (entry.item == item)
                {
                    entry.quantity += quantity;
                    return;
                }
            }

            collectedMaterialsForCurrentStage.Add(new MaterialRequirement { item = item, quantity = quantity });
        }

        /// <summary>
        /// 현재 단계에서 특정 재료를 몇 개나 확보(투입)했는지 반환한다.
        /// </summary>
        public int GetCollectedQuantity(ItemData item)
        {
            foreach (var entry in collectedMaterialsForCurrentStage)
            {
                if (entry.item == item)
                    return entry.quantity;
            }
            return 0;
        }

        /// <summary>
        /// 현재 단계를 완료하고 다음 단계로 넘어갈 수 있는지 확인한다.
        /// 도면을 보유하고, 필요한 재료를 모두 확보했어야 한다.
        /// </summary>
        public bool CanAdvanceStage()
        {
            if (!hasCurrentStageBlueprint)
                return false;

            foreach (var required in GetCurrentStageRequirements())
            {
                if (GetCollectedQuantity(required.item) < required.quantity)
                    return false;
            }

            return true;
        }

        /// <summary>
        /// 조건을 만족하면 다음 단계로 진행한다.
        /// 이미 마지막 단계(3단계)에서 조건을 만족하면 배가 100% 완성된 것으로 보고 true를 반환한다.
        /// </summary>
        public bool TryAdvanceStage()
        {
            if (!CanAdvanceStage())
                return false;

            highestCompletedStage = Mathf.Max(highestCompletedStage, currentStage);
            AudioManager.Instance?.PlayStageComplete(); // 단계 완료 축하 효과음

            if (currentStage >= TotalStages)
            {
                isFullyComplete = true;
                NotifyProgressChanged();
                return true; // 3단계까지 모두 완료 - 배 100% 완성
            }

            currentStage++;
            hasCurrentStageBlueprint = false;
            collectedMaterialsForCurrentStage.Clear();
            NotifyProgressChanged();
            return false;
        }

        /// <summary>
        /// 지정한 단계까지 배(뗏목) 제작을 완료했는지 확인한다.
        /// 뗏목이 일정 단계 이상 완성되면 고무보트의 해류 제약(대형/특대 섬 접근 불가)을 뚫을 수 있을 만큼
        /// 튼튼해진 것으로 간주해 IslandTravel의 이동 범위 확장 판정에 사용한다.
        /// </summary>
        public bool HasCompletedStage(int stage)
        {
            return highestCompletedStage >= stage;
        }

        /// <summary>
        /// 배 제작 전체 진행률(0~1)을 대략적으로 반환한다. 완료된 단계 수 기준이며 단계 내 세부 진행은 반영하지 않는다.
        /// 예전 계산식(currentStage-1)/TotalStages은 3단계를 완성해도 currentStage가 더 이상 증가하지 않아
        /// 진행률이 2/3에서 멈추는 버그가 있었다. isFullyComplete를 우선 확인해 100%를 정확히 반환하도록 고쳤다.
        /// </summary>
        public float GetOverallProgress()
        {
            if (isFullyComplete)
                return 1f;

            return (float)(currentStage - 1) / TotalStages;
        }

        /// <summary>
        /// 현재 단계 안에서 필요한 재료를 얼마나 채웠는지(0~1). 재료 종류별 충족률의 평균이다.
        /// 설계가 비어 있는 단계(요구 재료 0개)에서는 도면 보유 여부만으로 0 또는 0.5를 돌려준다 -
        /// 그래야 진행이 0에 붙박이지 않는다.
        /// </summary>
        public float GetCurrentStageMaterialFraction()
        {
            var requirements = GetCurrentStageRequirements();

            float sum = 0f;
            int counted = 0;
            for (int i = 0; i < requirements.Count; i++)
            {
                var required = requirements[i];
                if (required == null || required.item == null || required.quantity <= 0)
                    continue;

                sum += Mathf.Clamp01((float)GetCollectedQuantity(required.item) / required.quantity);
                counted++;
            }

            if (counted == 0)
                return hasCurrentStageBlueprint ? 0.5f : 0f;

            return sum / counted;
        }

        /// <summary>
        /// 뗏목 외형이 쓰는 "세밀한" 진행률(0~1). GetOverallProgress는 완료된 단계 수만 세기 때문에
        /// 3단계 게임에서 값이 0 / 0.33 / 0.67 / 1 네 개뿐이고, 그러면 재료를 넣어도 눈에 보이는 변화가
        /// 없다. 여기서는 단계 안의 재료 충족률까지 섞어 훨씬 촘촘한 값을 만든다.
        /// 씬 설계(단계별 재료 3종)에서는 1/9 단위로 움직인다.
        ///
        /// 완성 전에는 0.97로 상한을 둔다. 마지막 단계의 재료를 다 넣은 순간(아직 작업대에서 완성
        /// 확정을 안 누른 상태)에 뗏목이 먼저 100% 모습이 되어버리면 완성 연출이 사라지기 때문이다.
        /// GetOverallProgress는 UI/엔딩이 이미 쓰고 있으므로 건드리지 않는다.
        /// </summary>
        public float GetDetailedProgress()
        {
            if (isFullyComplete)
                return 1f;

            int completedStages = Mathf.Clamp(currentStage - 1, 0, TotalStages);
            float progress = (completedStages + GetCurrentStageMaterialFraction()) / TotalStages;
            return Mathf.Clamp(progress, 0f, 0.97f);
        }
    }
}
