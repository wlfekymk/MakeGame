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
            if (CanFindBlueprintOnIsland(islandSize))
                hasCurrentStageBlueprint = true;
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

            if (currentStage >= TotalStages)
                return true; // 3단계까지 모두 완료 - 배 100% 완성

            currentStage++;
            hasCurrentStageBlueprint = false;
            collectedMaterialsForCurrentStage.Clear();
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
        /// </summary>
        public float GetOverallProgress()
        {
            return (float)(currentStage - 1) / TotalStages;
        }
    }
}
