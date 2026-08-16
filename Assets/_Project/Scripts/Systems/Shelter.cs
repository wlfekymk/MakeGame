using System.Collections.Generic;
using UnityEngine;
using MakeGame.Data;
using MakeGame.Player;

namespace MakeGame.Systems
{
    /// <summary>
    /// 플레이어가 제작해 설치한 쉼터(Shelter). Stranded Deep 기준: 비/햇빛을 막아주고 휴식 지점이 된다.
    /// 그늘 판정(지붕 콜라이더가 "Shade" 레이어) 외에도, 밤에 상호작용(E)하면 취침해 아침까지
    /// 시간을 건너뛰고 소량의 체력/일사병을 회복하는 능동적 기능도 제공한다 (TrySleep).
    ///
    /// [정착 배치 1 - Docs/Design_Settlement.md 2장] 쉼터는 이제 **통짜 레벨(Lv1→Lv3) + 인덱스 슬롯**을
    /// 가진다. 조각 자유배치는 설계에서 기각됐다(프리미티브뿐이라 자유도의 산출물이 없고, 세이브가
    /// 좌표 엔트리 수백 개로 불어난다). 구현 형태는 다음 세 가지뿐이다:
    ///  · level 1~3 (통짜 승급, 재료를 더 넣어 올린다)
    ///  · slotMask (슬롯 3개를 **정수 비트마스크 하나**로 표현 - 세이브가 int 1개로 끝난다)
    ///  · HomeRadius (Lv2 이상에서 집 주변 반경. 밖보다 압박이 **줄지만 0이 되지는 않는다**)
    /// 세 값 모두 StructureSaveEntry.level / slotMask 로 저장된다(SaveLoadController 참고).
    /// </summary>
    public class Shelter : MonoBehaviour
    {
        [Tooltip("이 쉼터가 제공하는 그늘 판정 반경(참고용 수치, 실제 판정은 레이캐스트로 이뤄진다)")]
        public float shadeRadius = 3f;

        [Tooltip("취침 시 즉시 회복되는 체력량")]
        public float sleepHealAmount = 15f;

        [Tooltip("지붕이 바닥으로부터 떠 있어야 하는 높이. 설치 시 Instantiate가 루트 위치를 바닥(설치 지점)에\n맞춰버리므로, 이 값만큼 스스로 들어올려 지붕이 바닥에 깔리지 않게 한다.")]
        public float roofHeight = 2.2f;

        // ── 레벨 (정착 배치 1) ────────────────────────────────────────────────────────────────
        // 기존 public 필드는 하나도 건드리지 않았다(프리팹 직렬화값 존재). 아래는 전부 신규 추가이므로
        // 프리팹/씬 YAML에 키가 없고, 따라서 여기 적힌 코드 기본값이 그대로 유일한 소스다.

        [Header("레벨 (1=쉼터 / 2=오두막 / 3=집)")]
        [Tooltip("현재 쉼터 단계. 1이 기존 쉼터와 완전히 동일한 상태다.")]
        public int level = 1;

        [Tooltip("설치된 슬롯 비트마스크. bit0=문 / bit1=침상 / bit2=저장궤. 0이면 전부 비어 있다.")]
        public int slotMask = 0;

        [Tooltip("이 쉼터가 올라갈 수 있는 최대 단계")]
        public int maxLevel = 3;

        [Header("홈 반경 (압박 감소 - 0으로 만들지 않는다)")]
        [Tooltip("Lv2(오두막)의 홈 반경(m). 이 안에서는 그늘 레이캐스트 없이도 일사병이 회복된다.\n" +
            "허기·갈증·위험요소는 그대로 진행된다 - 힐링은 '압박이 사라지는 것'이 아니라 '관리하지 않아도 되는 시간'이다.")]
        public float homeRadiusLevel2 = 8f;

        [Tooltip("Lv3(집)의 홈 반경(m).")]
        public float homeRadiusLevel3 = 14f;

        [Header("취침 회복 차등")]
        [Tooltip("Lv2에서의 취침 회복량. Lv1은 위의 sleepHealAmount를 그대로 쓴다.")]
        public float sleepHealLevel2 = 30f;

        [Tooltip("Lv3에서의 취침 회복량.")]
        public float sleepHealLevel3 = 45f;

        [Tooltip("침상 슬롯이 설치돼 있을 때 추가로 회복되는 체력량.")]
        public float bedSlotSleepHealBonus = 10f;

        // ── 저장궤 = 자동 공급원 (정착 배치 2) ────────────────────────────────────────────────
        //
        // 왜 "보관"이 아니라 "공급"인가: 인벤토리에 용량 제한이 없어서 창고형 저장은 게임적 가치가
        // 정확히 0이다(Design_Settlement 0장 실측 - PlayerInventory에 maxSlots가 없다). 그래서
        // 저장궤는 넣어둔 것을 **홈 반경 안의 모닥불·증류기가 스스로 가져다 쓰는 공급원**이고,
        // 동시에 **밤사이 결과물을 모아두는 곳**이다(2-3 3번 / 4장 아침 수거).
        //
        // 세 갈래로만 동작한다.
        //  1) 연료  : 홈 반경 안의 모닥불이 꺼지기 직전에 저장궤에서 나뭇가지를 스스로 가져간다.
        //  2) 물통  : 홈 반경 안의 증류기가 찬 물을 저장궤가 생수로 걷어 둔다(물통은 소모되지 않는다 -
        //             수동 병입 규칙 HasBottleContainer와 완전히 같다).
        //  3) 훈연  : 저장궤에 넣어둔 생식품이 시간이 지나면 익힌 것(ItemData.cookedResult)으로 바뀐다.
        //             **새 자원을 만들지 않는다**(0장 경고) - 이미 있는 조리 결과 참조를 그대로 쓴다.
        //
        // 1·2는 실시간(각자 Update)으로 진행되는 것을 대신 걷어오는 것이라 이중 계산이 없고,
        // 3은 오직 여기서만 진행되므로 **게임 내 시계 경과분**으로 누적한다(취침 스킵 포함, 아래 참고).

        [Header("저장궤 자동 공급 (정착 배치 2)")]
        [Tooltip("저장궤가 보관할 수 있는 연료(모닥불 fuelItem) 최대 개수.\n" +
            "상한을 두는 이유: 보급은 '지을 게 없을 때'의 마지막 행동이라, 상한이 없으면 들고 있던 연료를 " +
            "통째로 삼켜 나중에 슬롯 재료로 쓸 나뭇가지가 사라진다.")]
        public int chestFuelCapacity = 10;

        [Tooltip("저장궤가 보관할 수 있는 물통(증류기 requiredContainerItem) 최대 개수.\n" +
            "물통은 병입에 소모되지 않으므로(WaterStill.HasBottleContainer) 1개만 넣어두면 영구히 동작한다.")]
        public int chestContainerCapacity = 1;

        [Tooltip("저장궤가 보관할 수 있는 생식품(isRawFood + cookedResult 보유) 최대 개수.")]
        public int chestRawFoodCapacity = 6;

        [Tooltip("생식품 1개를 익힌 것으로 바꾸는 데 필요한 **게임 내** 시간(초).\n" +
            "secondsPerDay 600 기준 120초 = 0.2일. 밤 하나(0.5일 = 300초)를 통째로 자면 2개가 마르므로, " +
            "가득 채운 저장궤(6개)를 비우는 데 사흘 밤이 걸린다 - 하루에 한 번 들르는 리듬에 맞춘 값이다.")]
        public float dryingSecondsPerItem = 120f;

        [Tooltip("모닥불의 남은 연료가 이 시간(초) 이하로 떨어지면 저장궤가 연료를 한 개 넣어준다.")]
        public float campfireRefuelThresholdSeconds = 5f;

        [Tooltip("자동 공급/정산을 실행하는 간격(실제 초). 매 프레임 돌 필요가 전혀 없는 처리다.")]
        public float autoSupplyInterval = 0.5f;

        [Tooltip("한 번의 정산이 인정하는 최대 게임 내 경과 시간(초). 비정상적으로 큰 시계 점프가" +
            " 저장궤를 통째로 익혀버리는 것을 막는 안전장치다(600 = 하루, 기본 1800 = 사흘).")]
        public float maxSettleSeconds = 1800f;

        // ── 승급/슬롯 재료 ────────────────────────────────────────────────────────────────────
        // ItemData 직접 참조 대신 **아이템 이름**으로 요구한다: 이 컴포넌트는 프리팹에만 붙어 있고
        // 프리팹 편집은 디렉터 전용이라(AGENT_BRIEF 2장 3번) 인스펙터로 ItemData를 배선할 방법이 없다.
        // 이름은 인벤토리에 실제로 들어있는 ItemData.itemName과 대조하므로 레지스트리 로드도 필요 없다.
        // 재료는 전부 소형 섬에서 조달 가능한 기존 13종 안에서만 고른다(Design_Settlement 2-4:
        // 새 자원 노드를 만들지 않는다 / 대형+ 전용 재료를 필수로 넣지 않는다).

        [Header("재료 (이름 기반 - 프리팹 배선 불가라 ItemData 참조를 쓰지 않는다)")]
        [Tooltip("Lv1 → Lv2(오두막) 승급 재료")]
        public List<ShelterMaterialRequirement> level2Requirements = new List<ShelterMaterialRequirement>
        {
            new ShelterMaterialRequirement("나뭇가지", 6),
            new ShelterMaterialRequirement("야자잎", 4),
            new ShelterMaterialRequirement("노끈", 3),
            new ShelterMaterialRequirement("천조각", 2),
        };

        [Tooltip("Lv2 → Lv3(집) 승급 재료")]
        public List<ShelterMaterialRequirement> level3Requirements = new List<ShelterMaterialRequirement>
        {
            new ShelterMaterialRequirement("대나무", 8),
            new ShelterMaterialRequirement("나뭇가지", 8),
            new ShelterMaterialRequirement("노끈", 4),
            new ShelterMaterialRequirement("돌조각", 4),
        };

        [Tooltip("문 슬롯 재료")]
        public List<ShelterMaterialRequirement> doorSlotRequirements = new List<ShelterMaterialRequirement>
        {
            new ShelterMaterialRequirement("나뭇가지", 3),
            new ShelterMaterialRequirement("노끈", 2),
        };

        [Tooltip("침상 슬롯 재료")]
        public List<ShelterMaterialRequirement> bedSlotRequirements = new List<ShelterMaterialRequirement>
        {
            new ShelterMaterialRequirement("야자잎", 4),
            new ShelterMaterialRequirement("천조각", 2),
        };

        [Tooltip("저장궤 슬롯 재료")]
        public List<ShelterMaterialRequirement> chestSlotRequirements = new List<ShelterMaterialRequirement>
        {
            new ShelterMaterialRequirement("대나무", 4),
            new ShelterMaterialRequirement("노끈", 2),
        };

        // ── 슬롯 인덱스 ───────────────────────────────────────────────────────────────────────

        /// <summary>문 슬롯 인덱스(비트 0).</summary>
        public const int SlotDoor = 0;

        /// <summary>침상 슬롯 인덱스(비트 1).</summary>
        public const int SlotBed = 1;

        /// <summary>저장궤 슬롯 인덱스(비트 2).</summary>
        public const int SlotChest = 2;

        /// <summary>슬롯 개수. 늘릴 때는 **끝에만** 추가할 것 - 비트 위치가 세이브 값이다.</summary>
        public const int SlotCount = 3;

        /// <summary>슬롯 표시 이름(로그/프롬프트용). 인덱스 순서는 위 상수와 같다.</summary>
        public static readonly string[] SlotNames = { "문", "침상", "저장궤" };

        /// <summary>슬롯이 열리는 최소 레벨. 셋 다 Lv2(오두막)에서 열린다(Design_Settlement 2-2 표).</summary>
        public const int SlotUnlockLevel = 2;

        // ── 활성 쉼터 목록 (홈 반경 조회용) ───────────────────────────────────────────────────

        private static readonly List<Shelter> activeShelters = new List<Shelter>();

        /// <summary>현재 씬에 살아 있는 쉼터 목록(읽기 전용). 홈 반경 판정 외 용도로도 쓸 수 있다.</summary>
        public static IReadOnlyList<Shelter> ActiveShelters => activeShelters;

        private void OnEnable()
        {
            if (!activeShelters.Contains(this))
                activeShelters.Add(this);
        }

        private void OnDisable()
        {
            activeShelters.Remove(this);
        }

        /// <summary>
        /// 현재 레벨의 홈 반경(m). Lv1은 0이다 - **쉼터는 집이 아니다.** 홈 반경은 Lv2부터 생긴다.
        /// </summary>
        public float HomeRadius
        {
            get
            {
                if (level >= 3) return homeRadiusLevel3;
                if (level >= 2) return homeRadiusLevel2;
                return 0f;
            }
        }

        /// <summary>
        /// 주어진 월드 좌표를 홈 반경에 품고 있는 집을 찾는다. 없으면 null.
        /// 단순 거리 비교다 - 이 프로젝트에는 "실내"라는 개념이 아예 없고(그늘 판정이 머리 위 레이캐스트
        /// 1회뿐, SurvivalTickDriver.IsCurrentlyInShade) 벽 콜라이더/트리거를 새로 만드는 것은
        /// 배치 1이 필요로 하는 것보다 훨씬 비싸다.
        /// 파괴된 쉼터가 목록에 남아 있으면 여기서 함께 정리한다(Destroy는 프레임 끝까지 지연된다).
        /// </summary>
        public static Shelter FindHomeContaining(Vector3 worldPosition)
        {
            for (int i = activeShelters.Count - 1; i >= 0; i--)
            {
                Shelter shelter = activeShelters[i];
                if (shelter == null)
                {
                    activeShelters.RemoveAt(i);
                    continue;
                }

                float radius = shelter.HomeRadius;
                if (radius <= 0f)
                    continue;

                if ((shelter.transform.position - worldPosition).sqrMagnitude <= radius * radius)
                    return shelter;
            }

            return null;
        }

        /// <summary>
        /// 주어진 좌표가 "내 집" 안인지 여부. SurvivalTickDriver가 이 값을 쓴다.
        ///
        /// 판정은 **두 갈래의 OR**다(감독 결정: "건축물이 집 판정을 이어받는다").
        ///  (1) 기존 그대로 - Lv2 이상 쉼터의 홈 반경 안(Lv1 0m / Lv2 8m / Lv3 14m).
        ///  (2) 새로 추가 - 자유 건축으로 지은 **막힌 실내**(BuildingSystem이 판정한다).
        /// (1)은 한 줄도 바뀌지 않았다. 밤사이 증류기 수확·모닥불 유지(SettleHomesForSkippedTime)는
        /// 저장궤·침상이 달린 "집 객체" 자체가 필요해서 여전히 FindHomeContaining을 거치므로 이 OR의
        /// 영향을 전혀 받지 않는다. 실제로 넓어지는 것은 IsInsideHome을 쓰는 곳 - 일사병 그늘 판정
        /// (SurvivalTickDriver.IsCurrentlyShaded) 하나뿐이다. 즉 지붕까지 덮은 건축물 실내에서
        /// 일사병이 회복되고, 허기·갈증은 집 안에서도 그대로 진행된다(압박을 0으로 만들지 않는다).
        ///
        /// 왜 반경이 아니라 실내인가: 쉼터는 반경 하나로 집을 흉내 냈지만, 건축물은 벽이 실제로 존재하므로
        /// "안에 있다"를 형상으로 판정할 수 있다. 벽만 몇 장 세워 놓고 집 대접을 받는 일이 없어야 해서
        /// 반경이 아니라 **둘러싸였는지**를 묻는다.
        /// </summary>
        public static bool IsInsideHome(Vector3 worldPosition)
        {
            if (FindHomeContaining(worldPosition) != null)
                return true;

            return BuildingSystem.IsInsideEnclosedStructure(worldPosition);
        }

        /// <summary>지정한 슬롯이 설치돼 있는지 여부.</summary>
        public bool HasSlot(int slotIndex)
        {
            if (slotIndex < 0 || slotIndex >= SlotCount)
                return false;

            return (slotMask & (1 << slotIndex)) != 0;
        }

        /// <summary>현재 레벨에서 열려 있는 슬롯 중 아직 비어 있는 첫 번째 인덱스. 없으면 -1.</summary>
        public int FindFirstEmptySlot()
        {
            if (level < SlotUnlockLevel)
                return -1;

            for (int i = 0; i < SlotCount; i++)
            {
                if (!HasSlot(i))
                    return i;
            }

            return -1;
        }

        /// <summary>
        /// 현재 취침 회복량. 레벨별 기본값 + 침상 슬롯 보너스다.
        /// [요청] ui-engineer: InteractionPromptUI.BuildShelterPrompt가 지금 shelter.sleepHealAmount를
        /// 그대로 표시하는데(InteractionPromptUI.cs:326), Lv2 이상에서는 실제 회복량과 갈라진다.
        /// 그 자리를 shelter.CurrentSleepHealAmount 로 바꿔 달라.
        /// </summary>
        public float CurrentSleepHealAmount
        {
            get
            {
                float baseHeal = sleepHealAmount;
                if (level >= 3) baseHeal = sleepHealLevel3;
                else if (level >= 2) baseHeal = sleepHealLevel2;

                return baseHeal + (HasSlot(SlotBed) ? bedSlotSleepHealBonus : 0f);
            }
        }

        // ── 저장궤 상태 ───────────────────────────────────────────────────────────────────────
        //
        // 씬/프리팹에 직렬화하지 않는 **순수 런타임 상태**다(public 필드로 노출하면 프리팹에 죽은 키가
        // 생긴다). 저장은 SaveData.StructureSaveEntry.chestItems/chestYield/chestDryingProgress로
        // 가고, 복원은 RestoreChestState 하나로 들어온다.
        //
        // 이름이 아니라 ItemData 참조로 들고 있는 이유: 저장궤에 들어오는 경로가 (1) 플레이어 인벤토리
        // (2) ItemData.cookedResult (3) WaterStill.bottledWaterItem 셋뿐이고 셋 다 이미 참조다.
        // 이름 대조가 필요한 곳은 세이브 복원 한 곳뿐이라, 그 해석은 이름→ItemData 캐시를 이미 들고 있는
        // SaveLoadController에 맡긴다(중복 캐시를 만들지 않는다).

        private readonly List<ShelterItemStack> chestStock = new List<ShelterItemStack>();
        private readonly List<ShelterItemStack> chestYield = new List<ShelterItemStack>();
        private float dryingProgressSeconds;

        private float autoSupplyTimer;
        private float lastSettleSeconds;
        private bool settleInitialized;

        private static SurvivalClock cachedClock;

        /// <summary>저장궤에 보관 중인 재료(연료/물통/생식품). 세이브 전용 읽기 창구다.</summary>
        public IReadOnlyList<ShelterItemStack> ChestStock => chestStock;

        /// <summary>수거를 기다리는 결과물(생수/익힌 음식). 세이브 전용 읽기 창구다.</summary>
        public IReadOnlyList<ShelterItemStack> ChestYield => chestYield;

        /// <summary>현재 훈연 진행도(게임 내 초). 세이브 전용 읽기 창구다.</summary>
        public float DryingProgressSeconds => dryingProgressSeconds;

        /// <summary>저장궤 기능이 실제로 켜져 있는지(= Lv2 이상 + 저장궤 슬롯 설치됨).</summary>
        public bool ChestActive => level >= SlotUnlockLevel && HasSlot(SlotChest);

        /// <summary>수거할 결과물이 하나라도 있는지. 프롬프트 UI가 매 프레임 물어봐도 안전하다.</summary>
        public bool HasYield => chestYield.Count > 0;

        /// <summary>
        /// 세이브에서 읽은 저장궤 상태를 되돌린다(SaveLoadController.RestoreShelter 전용).
        /// lastSettleSeconds는 **저장하지 않는다** - 불러오면 시계도 저장 시점으로 함께 되돌아가므로
        /// 다음 정산에서 현재 시계로 다시 초기화하면 경과가 0이 되어 정확하다. 저장했다가 잘못 복원하면
        /// 오히려 없던 시간이 통째로 익혀지는 쪽이 위험하다.
        /// </summary>
        public void RestoreChestState(List<ShelterItemStack> savedStock, List<ShelterItemStack> savedYield, float savedDryingProgress)
        {
            chestStock.Clear();
            chestYield.Clear();

            if (savedStock != null)
            {
                foreach (var stack in savedStock)
                    AddToStacks(chestStock, stack != null ? stack.data : null, stack != null ? stack.count : 0);
            }

            if (savedYield != null)
            {
                foreach (var stack in savedYield)
                    AddToStacks(chestYield, stack != null ? stack.data : null, stack != null ? stack.count : 0);
            }

            dryingProgressSeconds = Mathf.Max(0f, savedDryingProgress);
            settleInitialized = false; // 다음 정산에서 복원된 시계 값으로 다시 기준점을 잡는다.
        }

        // ── 정산 루프 ─────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// 저장궤가 켜져 있을 때만 도는 주기 처리. 프레임마다 돌 필요가 없어 autoSupplyInterval로 묶는다.
        /// </summary>
        private void Update()
        {
            if (!ChestActive)
                return;

            autoSupplyTimer += Time.deltaTime;
            if (autoSupplyTimer < autoSupplyInterval)
                return;

            autoSupplyTimer = 0f;

            SettleElapsedTime();
            RunAutoSupply();
        }

        /// <summary>
        /// **마지막 정산 시각과의 차이만큼** 훈연을 진행시킨다.
        ///
        /// 왜 Time.deltaTime이 아니라 시계인가: TrySleep이 시계를 다음 아침으로 점프시키는데
        /// (Shelter.TrySleep), 실시간 기준으로 누적하면 취침으로 건너뛴 밤이 통째로 사라져 "밤사이
        /// 집이 모아 둔다"가 성립하지 않는다. 시계 차이로 재면 취침 점프분이 그대로 들어온다.
        /// 훈연은 다른 어떤 시스템도 진행시키지 않으므로 이 방식으로 이중 계산이 발생할 수 없다
        /// (증류기/모닥불은 각자 실시간으로 Tick하므로 시계 차이를 쓰면 이중 계산이 된다 - 그쪽은
        /// SettleHomesForSkippedTime에서 **건너뛴 시간에 대해서만** 따로 보정한다).
        /// </summary>
        private void SettleElapsedTime()
        {
            SurvivalClock clock = ResolveClock();
            if (clock == null)
                return;

            if (!settleInitialized)
            {
                lastSettleSeconds = clock.elapsedSeconds;
                settleInitialized = true;
                return;
            }

            float delta = clock.elapsedSeconds - lastSettleSeconds;
            lastSettleSeconds = clock.elapsedSeconds;
            if (delta <= 0f)
                return;

            AdvanceDrying(Mathf.Min(delta, Mathf.Max(0f, maxSettleSeconds)));
        }

        /// <summary>
        /// 저장궤 안의 생식품을 익힌 것으로 바꿔 결과물 칸으로 옮긴다. 넣어둔 생식품이 없으면 진행도를
        /// 0으로 되돌린다 - 빈 저장궤가 시간을 쌓아두었다가 넣자마자 즉시 익히는 것을 막는다.
        /// </summary>
        private void AdvanceDrying(float seconds)
        {
            if (dryingSecondsPerItem <= 0f)
                return;

            if (FindDryableFood() == null)
            {
                dryingProgressSeconds = 0f;
                return;
            }

            dryingProgressSeconds += seconds;

            // guard: dryingSecondsPerItem이 아주 작게 설정돼도 프레임을 잡아먹지 않게 상한을 둔다.
            for (int guard = 0; guard < 64 && dryingProgressSeconds >= dryingSecondsPerItem; guard++)
            {
                ItemData raw = FindDryableFood();
                if (raw == null)
                    break;

                dryingProgressSeconds -= dryingSecondsPerItem;
                RemoveFromStacks(chestStock, raw, 1);
                AddToStacks(chestYield, raw.cookedResult, 1);
            }

            if (FindDryableFood() == null)
                dryingProgressSeconds = 0f;
        }

        /// <summary>저장궤 안에서 익힐 수 있는 생식품 한 종류를 찾는다. 없으면 null.</summary>
        private ItemData FindDryableFood()
        {
            foreach (var stack in chestStock)
            {
                if (stack != null && stack.count > 0 && IsDryableFood(stack.data))
                    return stack.data;
            }

            return null;
        }

        /// <summary>익힌 결과가 정의된 생음식인지. 새 아이템을 만들지 않고 기존 cookedResult만 쓴다.</summary>
        private static bool IsDryableFood(ItemData data)
        {
            return data != null && data.isRawFood && data.cookedResult != null;
        }

        // ── 자동 공급 ─────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// 홈 반경 안의 모닥불에 연료를 넣어주고, 증류기의 찬 물을 생수로 걷어 저장궤에 쌓는다.
        /// 둘 다 실시간으로 진행되는 것을 대신 처리하는 것이라 추가 생산이 아니다 - 플레이어가 매번
        /// 손으로 넣고 걷으러 다니지 않아도 되는 것이 저장궤의 보상이다(Design_Settlement 2-3 3번).
        /// </summary>
        private void RunAutoSupply()
        {
            float radius = HomeRadius;
            if (radius <= 0f)
                return;

            float radiusSqr = radius * radius;
            Vector3 origin = transform.position;

            var fires = Campfire.Active;
            for (int i = 0; i < fires.Count; i++)
            {
                Campfire fire = fires[i];
                if (fire == null || (fire.transform.position - origin).sqrMagnitude > radiusSqr)
                    continue;

                TryRefuelCampfire(fire, false);
            }

            var stills = WaterStill.Active;
            for (int i = 0; i < stills.Count; i++)
            {
                WaterStill still = stills[i];
                if (still == null || (still.transform.position - origin).sqrMagnitude > radiusSqr)
                    continue;

                TryBottleFromStill(still);
            }
        }

        /// <summary>
        /// 모닥불이 꺼지기 직전이면 저장궤에서 연료 하나를 꺼내 넣어준다.
        /// allowRelight가 false이면 **이미 켜져 있는 불만** 먹여 살린다 - 차갑게 식은 모닥불을 저장궤가
        /// 스스로 점화하면 발화 도구(라이터/파이어스타터) 규칙을 우회하게 된다. true는 취침으로
        /// 건너뛴 시간을 정산하는 도중, 방금 전까지 타고 있던 불이 그 안에서 꺼진 경우에만 쓴다.
        /// </summary>
        private bool TryRefuelCampfire(Campfire fire, bool allowRelight)
        {
            if (fire == null || fire.fuelItem == null)
                return false;

            if (!fire.isLit && !allowRelight)
                return false;

            if (fire.isLit && fire.remainingFuelSeconds > campfireRefuelThresholdSeconds)
                return false;

            if (!RemoveFromStacks(chestStock, fire.fuelItem, 1))
                return false;

            fire.AddFuelFromStorage(1);
            return true;
        }

        /// <summary>
        /// 증류기에 병 하나 분량 이상의 물이 찼고 저장궤에 물통이 있으면, 생수로 걷어 저장궤에 쌓는다.
        /// 물통은 소모하지 않는다(수동 병입 규칙 WaterStill.HasBottleContainer와 동일).
        /// </summary>
        private int TryBottleFromStill(WaterStill still)
        {
            if (still == null || still.bottledWaterItem == null)
                return 0;

            if (still.requiredContainerItem != null && CountInChest(still.requiredContainerItem) <= 0)
                return 0;

            int bottles = still.ExtractBottles(int.MaxValue);
            if (bottles > 0)
                AddToStacks(chestYield, still.bottledWaterItem, bottles);

            return bottles;
        }

        /// <summary>
        /// 취침으로 **건너뛴** 시간만큼 홈 반경 안의 증류기/모닥불을 앞으로 감는다.
        ///
        /// 왜 홈 반경 안만인가: 밖에 세워둔 증류기는 예전 그대로(취침 중 정지)다. 집 반경 안에서만
        /// 밤이 실제로 흐르게 두면 "집에 시설을 모아두는 것"이 곧 밤사이 생산이 되고, 그것이 아침에
        /// 수거할 것을 만든다(Design_Settlement 4장). 대칭으로 모닥불도 그 시간만큼 타 들어가므로
        /// 취침이 연료를 공짜로 아껴주는 일은 없다 - 저장궤에 연료를 넣어둔 집만 아침에 불이 살아 있다.
        /// 날씨 배율(WeatherSystem의 증류기 ×3 / 연료 ×1.5)은 건너뛴 시간에 적용하지 않는다. 건너뛴
        /// 구간의 날씨는 애초에 정해진 적이 없고, 자는 동안의 날씨를 사후에 지어내는 것은 "비 오는 낮에
        /// 나갈 것인가"라는 선택(4장)을 흐릴 뿐이다 - 비의 이득은 깨어 있을 때만 받는다.
        /// </summary>
        /// <param name="seconds">시계가 앞으로 점프한 게임 내 시간(초).</param>
        public static void SettleHomesForSkippedTime(float seconds)
        {
            if (seconds <= 0f)
                return;

            var stills = WaterStill.Active;
            for (int i = 0; i < stills.Count; i++)
            {
                WaterStill still = stills[i];
                if (still == null)
                    continue;

                Shelter home = FindHomeContaining(still.transform.position);
                if (home == null)
                    continue;

                still.Tick(seconds);
                if (home.ChestActive)
                    home.TryBottleFromStill(still);
            }

            var fires = Campfire.Active;
            for (int i = 0; i < fires.Count; i++)
            {
                Campfire fire = fires[i];
                if (fire == null)
                    continue;

                Shelter home = FindHomeContaining(fire.transform.position);
                if (home == null)
                    continue;

                home.BurnCampfireThroughSkippedTime(fire, seconds);
            }
        }

        /// <summary>
        /// 모닥불을 건너뛴 시간만큼 태운다. 도중에 연료가 떨어지면 저장궤에서 한 개씩 이어 넣어,
        /// 연료가 충분한 집이라면 아침에도 불이 켜져 있게 만든다. 저장궤가 비면 거기서 꺼진다.
        /// </summary>
        private void BurnCampfireThroughSkippedTime(Campfire fire, float seconds)
        {
            if (fire == null || !fire.isLit)
                return;

            // guard: 연료 상한(chestFuelCapacity)이 있어 반복은 그 개수 + 1을 넘지 않는다.
            for (int guard = 0; guard < 128 && seconds > 0f; guard++)
            {
                float step = Mathf.Min(seconds, Mathf.Max(fire.remainingFuelSeconds, 0.01f));
                fire.Tick(step);
                seconds -= step;

                if (fire.isLit)
                    continue;

                if (seconds <= 0f)
                    break;

                if (!ChestActive || !TryRefuelCampfire(fire, true))
                    break;
            }
        }

        // ── 보급 / 수거 ───────────────────────────────────────────────────────────────────────

        /// <summary>
        /// 저장궤가 밤사이 모아둔 결과물을 한 번에 전부 인벤토리로 옮긴다. **아침에 할 일**이 이것이다.
        /// 시간대 제한은 두지 않는다 - 강제하면 힐링이 아니라 출근이 된다(Design_Settlement 4장 마지막 줄).
        /// </summary>
        /// <returns>실제로 옮긴 개수(0이면 수거할 것이 없었다).</returns>
        public int TryCollectYield(PlayerInventory inventory)
        {
            if (inventory == null || chestYield.Count == 0)
                return 0;

            int moved = 0;
            int leftBehind = 0;
            var summary = new List<string>();

            // [B18] 예전에는 AddItem 성공 여부와 무관하게 아래에서 chestYield.Clear()를 불렀다.
            // 용량이 도입된 지금 그대로 두면 **가방이 가득 찬 상태로 수거하는 순간 결과물이
            // 저장궤에서도 인벤토리에서도 사라진다.** 들어간 만큼만 지우고 나머지는 남긴다.
            foreach (var stack in chestYield)
            {
                if (stack == null || stack.data == null || stack.count <= 0)
                    continue;

                int accepted = 0;
                for (int i = 0; i < stack.count; i++)
                {
                    if (!inventory.TryAddItem(stack.data))
                        break;

                    accepted++;
                }

                if (accepted > 0)
                {
                    summary.Add($"{stack.data.itemName} {accepted}");
                    moved += accepted;
                }

                leftBehind += stack.count - accepted;
                stack.count -= accepted;
            }

            // 다 비운 칸만 목록에서 뺀다.
            chestYield.RemoveAll(stack => stack == null || stack.data == null || stack.count <= 0);

            if (leftBehind > 0)
                Debug.Log($"[Shelter] 가방이 가득 차 {leftBehind}개는 저장궤에 그대로 뒀다");

            if (moved <= 0)
                return 0;

            AudioManager.Instance?.PlayPickup();
            Debug.Log($"[Shelter] 저장궤 수거 - {string.Join(" · ", summary)}");
            return moved;
        }

        /// <summary>
        /// 인벤토리에서 저장궤가 실제로 쓸 수 있는 것(홈 반경 안 모닥불의 연료 / 증류기의 물통 /
        /// 익힐 수 있는 생식품)만 골라 상한까지 넣는다. **쓸 곳이 없는 물건은 받지 않는다** - 창고가
        /// 아니기 때문이고, 동시에 모닥불이 없는 집이 슬롯 재료용 나뭇가지를 삼키는 사고도 막힌다.
        /// </summary>
        /// <returns>실제로 넣은 개수(0이면 넣을 것이 없었다).</returns>
        public int TryStockChest(PlayerInventory inventory)
        {
            if (inventory == null || !ChestActive)
                return 0;

            int stocked = 0;
            var summary = new List<string>();

            // 인벤토리를 직접 순회하며 지우면 인덱스가 흔들리므로, 넣을 종류를 먼저 모은다.
            var kinds = new List<ItemData>();
            foreach (var item in inventory.items)
            {
                if (item == null || item.data == null || kinds.Contains(item.data))
                    continue;

                if (GetChestCapacity(item.data) > 0)
                    kinds.Add(item.data);
            }

            foreach (var data in kinds)
            {
                int take = GetStockableCount(inventory, data);
                if (take <= 0 || !inventory.RemoveItems(data, take))
                    continue;

                AddToStacks(chestStock, data, take);
                summary.Add($"{data.itemName} {take}");
                stocked += take;
            }

            if (stocked <= 0)
                return 0;

            AudioManager.Instance?.PlayCraftSuccess();
            Debug.Log($"[Shelter] 저장궤 보급 - {string.Join(" · ", summary)}");
            return stocked;
        }

        /// <summary>
        /// 이 아이템을 저장궤가 몇 개까지 받아주는지. 0이면 받지 않는다(= 쓸 곳이 없다).
        /// 연료/물통은 **홈 반경 안에 그것을 쓰는 시설이 실제로 있을 때만** 받는다.
        /// </summary>
        public int GetChestCapacity(ItemData data)
        {
            if (data == null || !ChestActive)
                return 0;

            float radius = HomeRadius;
            if (radius > 0f)
            {
                float radiusSqr = radius * radius;
                Vector3 origin = transform.position;

                var fires = Campfire.Active;
                for (int i = 0; i < fires.Count; i++)
                {
                    Campfire fire = fires[i];
                    if (fire != null && fire.fuelItem == data &&
                        (fire.transform.position - origin).sqrMagnitude <= radiusSqr)
                        return chestFuelCapacity;
                }

                var stills = WaterStill.Active;
                for (int i = 0; i < stills.Count; i++)
                {
                    WaterStill still = stills[i];
                    if (still != null && still.requiredContainerItem == data &&
                        (still.transform.position - origin).sqrMagnitude <= radiusSqr)
                        return chestContainerCapacity;
                }
            }

            return IsDryableFood(data) ? chestRawFoodCapacity : 0;
        }

        /// <summary>
        /// 지금 이 아이템을 저장궤에 몇 개까지 넣을 수 있는지(빈 자리와 소지량 중 작은 쪽).
        ///
        /// **마지막 한 개는 절대 가져가지 않는다.** 이 규칙이 없으면 물통 하나를 넣는 순간 원정지의
        /// 증류기에서 손으로 병입할 수단이 사라지고(WaterStill.HasBottleContainer), 밤에 들고 있던
        /// 마지막 생고기가 저장궤로 들어가 그날 먹을 것이 없어진다. 물통은 재제작이 가능하므로
        /// (Recipe_물통: 대나무+노끈) 저장궤 전용 물통을 하나 더 만들면 되고, 그 비용은 "집이 알아서
        /// 걷어둔다"의 대가로 타당하다. 힐링 게임에서 자동화가 손해로 느껴지면 아무도 안 쓴다.
        /// </summary>
        public int GetStockableCount(PlayerInventory inventory, ItemData data)
        {
            if (inventory == null || data == null)
                return 0;

            int room = GetChestCapacity(data) - CountInChest(data);
            if (room <= 0)
                return 0;

            int spare = inventory.GetItemCount(data) - 1;
            return Mathf.Max(0, Mathf.Min(room, spare));
        }

        /// <summary>수거 대기 중인 결과물을 "생수 2 · 구운고기 1" 형태의 한 줄로 만든다.</summary>
        public string DescribeYield()
        {
            var parts = new List<string>();
            foreach (var stack in chestYield)
            {
                if (stack != null && stack.data != null && stack.count > 0)
                    parts.Add($"{stack.data.itemName} {stack.count}");
            }

            return parts.Count == 0 ? string.Empty : string.Join(" · ", parts);
        }

        /// <summary>지금 저장궤에 넣을 수 있는 것을 한 줄로 만든다. 없으면 null.</summary>
        public string DescribeStockable(PlayerInventory inventory)
        {
            if (inventory == null || !ChestActive)
                return null;

            var parts = new List<string>();
            var seen = new List<ItemData>();

            foreach (var item in inventory.items)
            {
                if (item == null || item.data == null || seen.Contains(item.data))
                    continue;

                seen.Add(item.data);

                int take = GetStockableCount(inventory, item.data);
                if (take > 0)
                    parts.Add($"{item.data.itemName} {take}");
            }

            return parts.Count == 0 ? null : string.Join(" · ", parts);
        }

        // ── 스택 유틸 ─────────────────────────────────────────────────────────────────────────

        /// <summary>저장궤에 이 아이템이 몇 개 들어 있는지.</summary>
        public int CountInChest(ItemData data)
        {
            if (data == null)
                return 0;

            foreach (var stack in chestStock)
            {
                if (stack != null && stack.data == data)
                    return stack.count;
            }

            return 0;
        }

        private static void AddToStacks(List<ShelterItemStack> stacks, ItemData data, int count)
        {
            if (data == null || count <= 0)
                return;

            foreach (var stack in stacks)
            {
                if (stack != null && stack.data == data)
                {
                    stack.count += count;
                    return;
                }
            }

            stacks.Add(new ShelterItemStack(data, count));
        }

        private static bool RemoveFromStacks(List<ShelterItemStack> stacks, ItemData data, int count)
        {
            if (data == null || count <= 0)
                return false;

            for (int i = 0; i < stacks.Count; i++)
            {
                ShelterItemStack stack = stacks[i];
                if (stack == null || stack.data != data || stack.count < count)
                    continue;

                stack.count -= count;
                if (stack.count <= 0)
                    stacks.RemoveAt(i);

                return true;
            }

            return false;
        }

        /// <summary>씬의 게임 내 시계. 정산의 기준이므로 한 번 찾아 정적으로 캐시한다.</summary>
        private static SurvivalClock ResolveClock()
        {
            if (cachedClock == null)
                cachedClock = FindAnyObjectByType<SurvivalClock>();

            return cachedClock;
        }

        /// <summary>
        /// 설치 직후 루트(지붕)를 roofHeight만큼 들어올리고, 바닥까지 닿는 기둥 4개를 절차적으로 붙여
        /// 판자 한 장뿐이던 플레이스홀더를 실제 쉼터처럼 보이게 만든다.
        /// </summary>
        private void Awake()
        {
            transform.position += Vector3.up * roofHeight;
            BuildVisual();
        }

        /// <summary>
        /// 지붕 색을 이엉(초가) 색으로 바꾸고, 스케일이 비균일한 루트(지붕 Plane) 아래에
        /// 스케일 영향을 받지 않는 보정용 빈 오브젝트를 하나 만들어 그 밑에 기둥 4개를 붙인다.
        ///
        /// [정착 배치 1] Awake 전용이 아니라 **언제든 다시 부를 수 있는 재빌드 메서드**다
        /// (Design_Settlement 2-2: 승급은 비주얼 재빌드 분리가 전제). 여러 번 불러도 안전하도록
        /// 이전에 만든 VisualParts를 먼저 지운다. Destroy는 프레임 끝까지 지연되므로
        /// SetActive(false)로 즉시 화면/물리에서 빼고 참조를 끊는다(AGENT_BRIEF 4장).
        /// 루트 위치는 여기서 건드리지 않는다 - 위치 보정은 Awake 1회뿐이다(재빌드마다 또 올리면
        /// 지붕이 계속 떠오른다. SaveLoadController.RestoreShelter의 roofHeight 보정과 같은 함정).
        /// </summary>
        public void BuildVisual()
        {
            if (visualParts != null)
            {
                visualParts.SetActive(false);
                Destroy(visualParts);
                visualParts = null;
            }

            var rootRenderer = GetComponent<MeshRenderer>();
            if (rootRenderer != null)
                rootRenderer.sharedMaterial = StructureVisualBuilder.CreateColorMaterial(RoofColorForLevel());

            // 루트가 지붕용으로 (4, 0.3, 4) 비균일 스케일되어 있어, 그 스케일을 상쇄하는 빈 부모를 만든다.
            visualParts = new GameObject("VisualParts");
            visualParts.transform.SetParent(transform, false);
            Vector3 parentScale = transform.localScale;
            visualParts.transform.localScale = new Vector3(
                parentScale.x != 0f ? 1f / parentScale.x : 1f,
                parentScale.y != 0f ? 1f / parentScale.y : 1f,
                parentScale.z != 0f ? 1f / parentScale.z : 1f);

            Vector3[] legOffsets =
            {
                new Vector3(1.6f, 0f, 1.6f),
                new Vector3(-1.6f, 0f, 1.6f),
                new Vector3(1.6f, 0f, -1.6f),
                new Vector3(-1.6f, 0f, -1.6f),
            };

            // [tech-artist-B 요청 - 인공물 시각 언어] 매끈한 원기둥 다리는 야자수 줄기/대나무 자원과 같은
            // 형태 언어라 "내가 지은 것"으로 읽히지 않는다(ArtDirection 2장 4번). 각진 사각 기둥 + 밧줄
            // 결속으로 바꾼다. 높이 인자가 원기둥과 다르다는 점에 주의: 원기둥 메시는 높이가 2단위라
            // scale.y에 절반(roofHeight * 0.5)을 넣어야 했지만, CreateLashedPost는 큐브라 실제 높이를
            // 그대로 받는다 - 그래서 roofHeight를 넘긴다(중심은 지붕 아래 roofHeight/2로 동일하므로
            // 기둥이 바닥~지붕을 정확히 잇는 결과도 이전과 완전히 같다).
            foreach (var offset in legOffsets)
            {
                StructureVisualBuilder.CreateLashedPost(visualParts.transform, "Leg",
                    offset + Vector3.down * (roofHeight * 0.5f), roofHeight, 0.12f, new Color(0.35f, 0.22f, 0.1f));
            }

            if (level >= 2)
                BuildWalls();

            if (level >= 3)
                BuildUpperDeck();

            if (level >= SlotUnlockLevel)
                BuildSlotVisuals();
        }

        /// <summary>승급으로 새로 만들어진 비주얼 파츠들의 부모. 재빌드 시 통째로 지운다.</summary>
        private GameObject visualParts;

        /// <summary>레벨이 올라갈수록 지붕이 더 잘 손질된 색으로 바뀐다(형태 변화는 벽/데크가 담당).</summary>
        private Color RoofColorForLevel()
        {
            if (level >= 3) return new Color(0.62f, 0.47f, 0.26f);
            if (level >= 2) return new Color(0.58f, 0.44f, 0.24f);
            return new Color(0.55f, 0.42f, 0.22f);
        }

        /// <summary>
        /// Lv2 이상의 벽 3면. 앞면(z+)은 문 슬롯 자리라 비워 두고 BuildSlotVisuals가 채운다.
        /// 콜라이더는 붙지 않는다(StructureVisualBuilder.CreateVisualPart가 제거한다) - 시각 신호만이
        /// 목적이고, 벽으로 플레이어를 가두면 "실내"가 아니라 버그가 된다.
        /// </summary>
        private void BuildWalls()
        {
            float wallHeight = roofHeight * 0.9f;
            float wallCenterY = -roofHeight * 0.55f;
            Color wallColor = new Color(0.47f, 0.36f, 0.2f);

            StructureVisualBuilder.CreateVisualPart(visualParts.transform, "Wall_Back", PrimitiveType.Cube,
                new Vector3(0f, wallCenterY, -1.6f), new Vector3(3.2f, wallHeight, 0.12f), wallColor, null, "wood");
            StructureVisualBuilder.CreateVisualPart(visualParts.transform, "Wall_Left", PrimitiveType.Cube,
                new Vector3(-1.6f, wallCenterY, 0f), new Vector3(0.12f, wallHeight, 3.2f), wallColor, null, "wood");
            StructureVisualBuilder.CreateVisualPart(visualParts.transform, "Wall_Right", PrimitiveType.Cube,
                new Vector3(1.6f, wallCenterY, 0f), new Vector3(0.12f, wallHeight, 3.2f), wallColor, null, "wood");
        }

        /// <summary>Lv3의 2층 전망 데크. 지붕 위 평상 + 난간 2개로 최소 파츠만 쓴다.</summary>
        private void BuildUpperDeck()
        {
            Color deckColor = new Color(0.52f, 0.4f, 0.22f);

            StructureVisualBuilder.CreateVisualPart(visualParts.transform, "UpperDeck", PrimitiveType.Cube,
                new Vector3(0f, 0.35f, 0f), new Vector3(2.6f, 0.12f, 2.6f), deckColor, null, "wood");
            StructureVisualBuilder.CreateVisualPart(visualParts.transform, "DeckRail_Back", PrimitiveType.Cube,
                new Vector3(0f, 0.62f, -1.25f), new Vector3(2.6f, 0.42f, 0.08f), deckColor, null, "wood");
            StructureVisualBuilder.CreateVisualPart(visualParts.transform, "DeckRail_Left", PrimitiveType.Cube,
                new Vector3(-1.25f, 0.62f, 0f), new Vector3(0.08f, 0.42f, 2.6f), deckColor, null, "wood");
        }

        /// <summary>
        /// 슬롯 3개의 비주얼. **비어 있는 슬롯도 반드시 그린다** - 빈 슬롯이 곧 할 일 목록이라는 것이
        /// Design_Settlement 1장의 축이다(퀘스트 시스템 0줄로 "할 일이 하나 더 있다"가 성립한다).
        /// 비어 있으면 어두운 자리 표시(빈 문틀 / 빈 바닥 자국), 설치되면 실제 가구가 된다.
        /// </summary>
        private void BuildSlotVisuals()
        {
            float floorY = -roofHeight;
            Color emptyColor = new Color(0.28f, 0.24f, 0.2f);

            // 문(앞면 z+): 비었으면 문틀만, 설치되면 문짝이 채워진다.
            float doorHeight = roofHeight * 0.9f;
            float doorCenterY = -roofHeight * 0.55f;
            StructureVisualBuilder.CreateVisualPart(visualParts.transform, "DoorFrame_L", PrimitiveType.Cube,
                new Vector3(-0.75f, doorCenterY, 1.6f), new Vector3(0.14f, doorHeight, 0.14f),
                new Color(0.42f, 0.32f, 0.18f), null, "wood");
            StructureVisualBuilder.CreateVisualPart(visualParts.transform, "DoorFrame_R", PrimitiveType.Cube,
                new Vector3(0.75f, doorCenterY, 1.6f), new Vector3(0.14f, doorHeight, 0.14f),
                new Color(0.42f, 0.32f, 0.18f), null, "wood");
            StructureVisualBuilder.CreateVisualPart(visualParts.transform, "DoorLintel", PrimitiveType.Cube,
                new Vector3(0f, doorCenterY + doorHeight * 0.5f, 1.6f), new Vector3(1.7f, 0.12f, 0.14f),
                new Color(0.42f, 0.32f, 0.18f), null, "wood");

            if (HasSlot(SlotDoor))
            {
                StructureVisualBuilder.CreateVisualPart(visualParts.transform, "Door", PrimitiveType.Cube,
                    new Vector3(0f, doorCenterY, 1.6f), new Vector3(1.4f, doorHeight * 0.92f, 0.08f),
                    new Color(0.5f, 0.38f, 0.2f), null, "wood");
            }

            // 침상(안쪽 왼편).
            if (HasSlot(SlotBed))
            {
                StructureVisualBuilder.CreateVisualPart(visualParts.transform, "Bed_Frame", PrimitiveType.Cube,
                    new Vector3(-0.85f, floorY + 0.18f, -0.85f), new Vector3(1.5f, 0.24f, 0.9f),
                    new Color(0.45f, 0.34f, 0.19f), null, "wood");
                StructureVisualBuilder.CreateVisualPart(visualParts.transform, "Bed_Mat", PrimitiveType.Cube,
                    new Vector3(-0.85f, floorY + 0.33f, -0.85f), new Vector3(1.4f, 0.1f, 0.82f),
                    StructureVisualBuilder.PalmFiber, null, "leaf");
            }
            else
            {
                StructureVisualBuilder.CreateVisualPart(visualParts.transform, "Bed_EmptySlot", PrimitiveType.Cube,
                    new Vector3(-0.85f, floorY + 0.03f, -0.85f), new Vector3(1.5f, 0.05f, 0.9f),
                    emptyColor, null, "sand");
            }

            // 저장궤(안쪽 오른편).
            if (HasSlot(SlotChest))
            {
                StructureVisualBuilder.CreateVisualPart(visualParts.transform, "Chest_Body", PrimitiveType.Cube,
                    new Vector3(0.9f, floorY + 0.28f, -0.9f), new Vector3(0.9f, 0.55f, 0.7f),
                    new Color(0.48f, 0.36f, 0.2f), null, "wood");
                StructureVisualBuilder.CreateVisualPart(visualParts.transform, "Chest_Lid", PrimitiveType.Cube,
                    new Vector3(0.9f, floorY + 0.59f, -0.9f), new Vector3(0.95f, 0.1f, 0.75f),
                    StructureVisualBuilder.PalmFiber, null, "leaf");
            }
            else
            {
                StructureVisualBuilder.CreateVisualPart(visualParts.transform, "Chest_EmptySlot", PrimitiveType.Cube,
                    new Vector3(0.9f, floorY + 0.03f, -0.9f), new Vector3(0.9f, 0.05f, 0.7f),
                    emptyColor, null, "sand");
            }
        }

        /// <summary>
        /// 밤에 쉼터에서 상호작용하면 취침해 **이 밤이 끝나는 아침**(TimeOfDay01 0.25)까지 시간을 건너뛰고
        /// 소량의 체력을 회복하며 일사병 수치를 완전히 초기화한다. 신규 기능: 예전에는 밤이 되어도
        /// 그냥 지켜보거나 돌아다니는 것 외에 할 수 있는 게 없었는데, Stranded Deep처럼 쉼터를 지은
        /// 보람이 있도록 "밤을 건너뛰는 능동적 행동"을 추가했다. 낮에는 건너뛸 밤이 없으므로 실패한다.
        /// 세이브/로드나 SurvivalStats.Tick과 별개로 시계만 앞으로 이동시키므로, 건너뛴 시간 동안
        /// 허기/갈증이 소모되지 않는다 - 이는 "쉼터에서 안전하게 밤을 보낸다"는 컨셉을 살리기 위한
        /// 의도된 단순화다(허기/갈증까지 실시간 시뮬레이션하려면 별도의 대규모 시간가속 처리가 필요).
        /// [정착 배치 1] 회복량은 레벨/침상 슬롯에 따라 달라진다(CurrentSleepHealAmount).
        /// </summary>
        public bool TrySleep(SurvivalClock clock, SurvivalStats survivalStats)
        {
            if (clock == null || clock.IsDaytime || clock.secondsPerDay <= 0f)
                return false;

            // "이 밤이 끝나는 아침"(TimeOfDay01 == 0.25)으로 이동시킨다. GetWakeDay 주석 참고 -
            // 예전의 ElapsedDays + 1은 자정을 넘긴 뒤에 자면 하루를 통째로 더 건너뛰었다.
            float wakeSeconds = GetWakeSeconds(clock);
            float skippedSeconds = Mathf.Max(0f, wakeSeconds - clock.elapsedSeconds);
            clock.elapsedSeconds = wakeSeconds;

            // [정착 배치 2] 건너뛴 밤을 홈 반경 안의 증류기/모닥불에도 그대로 흘려보낸다.
            // 이 한 줄이 없으면 "자면 밤이 통째로 사라져서 아침에 걷을 것이 없다"가 된다
            // (SettleHomesForSkippedTime 주석 참고). 저장궤 안 훈연은 시계 차이로 자동 반영되므로
            // 여기서 따로 부르지 않는다 - 부르면 이중 계산이 된다.
            SettleHomesForSkippedTime(skippedSeconds);

            if (survivalStats != null)
            {
                survivalStats.Heal(CurrentSleepHealAmount);
                survivalStats.sunstroke = 0f; // 밤새 푹 쉬어 더위(일사병)가 완전히 가라앉는다.
            }

            // 연결(B-2): tech-artist가 AudioManager에 만들어 둔 전용 취침 성공음으로 교체.
            // 예전에는 PlayCraftSuccess()를 재사용해 "제작"과 "취침"이 같은 소리로 구분이 안 됐다.
            AudioManager.Instance?.PlaySleepSuccess();
            return true;
        }

        // ── 승급 / 슬롯 설치 ──────────────────────────────────────────────────────────────────

        /// <summary>
        /// 지금 E를 누르면 무엇이 지어지는지(또는 무엇이 모자란지) 한 줄로 설명한다.
        /// **순수 조회다 - 아무것도 소모하거나 바꾸지 않는다.** 프롬프트 UI가 매 프레임 부른다.
        /// TryBuildNext와 같은 우선순위(빈 슬롯 → 승급)를 쓰므로 표시와 실제 동작이 갈릴 수 없다.
        /// </summary>
        public string DescribeNextBuildAction(PlayerInventory inventory)
        {
            // [정착 배치 2] 수거가 최우선이다 - 재료를 쓰지 않고, 아침에 집에 들르는 이유 그 자체다.
            if (HasYield)
                return $"수거: {DescribeYield()}";

            // 지을 것이 막혔을 때만 보급이 실제 동작이 되므로, 안내도 그때만 덧붙인다(TryBuildNext와 동일 순서).
            string stockSuffix = null;

            int emptySlot = FindFirstEmptySlot();
            if (emptySlot >= 0)
            {
                var slotReqs = GetSlotRequirements(emptySlot);
                if (HasAllMaterials(inventory, slotReqs))
                    return $"{SlotNames[emptySlot]} 설치 가능";

                stockSuffix = DescribeStockSuffix(inventory);
                return $"{SlotNames[emptySlot]}: {DescribeMissing(inventory, slotReqs)}{stockSuffix}";
            }

            if (level >= maxLevel)
            {
                stockSuffix = DescribeStockable(inventory);
                return stockSuffix != null
                    ? $"저장궤 보급: {stockSuffix}"
                    : "이미 최고 단계다 - 밤에 오면 취침할 수 있다";
            }

            var levelReqs = GetLevelRequirements(level + 1);
            if (HasAllMaterials(inventory, levelReqs))
                return $"Lv{level + 1} 승급 가능";

            stockSuffix = DescribeStockSuffix(inventory);
            return $"Lv{level + 1} 승급: {DescribeMissing(inventory, levelReqs)}{stockSuffix}";
        }

        /// <summary>보급 안내를 뒤에 붙일 꼬리표. 넣을 것이 없으면 빈 문자열이라 문장이 그대로 남는다.</summary>
        private string DescribeStockSuffix(PlayerInventory inventory)
        {
            string stockable = DescribeStockable(inventory);
            return stockable == null ? string.Empty : $" · 저장궤 보급: {stockable}";
        }

        /// <summary>
        /// 이 쉼터에서 지금 할 수 있는 건축 행동 하나를 실행한다(InteractionController가 낮에 E로 부른다).
        /// 순서: **비어 있는 슬롯 먼저, 그 다음 승급.** 빈 슬롯이 곧 눈에 보이는 할 일이므로 그것을
        /// 먼저 채우게 두고, 재료가 없어 슬롯을 못 채우면 승급을 시도한다(둘 다 막혀 있으면 실패 로그).
        /// </summary>
        /// <returns>실제로 무언가 지었으면 true.</returns>
        public bool TryBuildNext(PlayerInventory inventory)
        {
            // [정착 배치 2] 아침 수거가 최우선이다. 재료를 전혀 쓰지 않고, 이것이 "아침에 할 일"이다.
            if (TryCollectYield(inventory) > 0)
                return true;

            int emptySlot = FindFirstEmptySlot();
            if (emptySlot >= 0 && TryInstallSlot(inventory, emptySlot))
                return true;

            if (TryUpgrade(inventory))
                return true;

            // [정착 배치 2] 지을 수 있는 것이 아무것도 없을 때만 저장궤 보급으로 떨어진다.
            // 순서가 중요하다 - 보급이 먼저면 슬롯 재료로 쓸 나뭇가지를 저장궤가 먼저 삼켜버린다.
            if (TryStockChest(inventory) > 0)
                return true;

            // 실패 이유는 남아 있는 첫 후보 기준으로 한 줄만 남긴다.
            string reason;
            if (emptySlot >= 0)
                reason = $"{SlotNames[emptySlot]} 슬롯: {DescribeMissing(inventory, GetSlotRequirements(emptySlot))}";
            else if (level >= maxLevel)
                reason = "이미 최고 단계다";
            else
                reason = $"Lv{level + 1} 승급: {DescribeMissing(inventory, GetLevelRequirements(level + 1))}";

            Debug.Log($"[Shelter] 지을 수 없다 - {reason}");
            AudioManager.Instance?.PlayActionFail();
            return false;
        }

        /// <summary>
        /// 다음 레벨로 승급한다. 재료가 모자라거나 최고 단계면 아무것도 소모하지 않고 false.
        /// 성공하면 비주얼을 통째로 다시 그린다(BuildVisual).
        /// </summary>
        public bool TryUpgrade(PlayerInventory inventory)
        {
            if (inventory == null || level >= maxLevel)
                return false;

            int nextLevel = level + 1;
            List<ShelterMaterialRequirement> requirements = GetLevelRequirements(nextLevel);
            if (!HasAllMaterials(inventory, requirements) || !ConsumeMaterials(inventory, requirements))
                return false;

            level = nextLevel;
            BuildVisual();

            AudioManager.Instance?.PlayStageComplete();
            Debug.Log($"[Shelter] Lv{level}로 승급했다. 홈 반경 {HomeRadius:F0}m · 취침 회복 {CurrentSleepHealAmount:F0}");
            return true;
        }

        /// <summary>
        /// 지정한 슬롯을 설치한다. 이미 설치돼 있거나 레벨이 모자라거나 재료가 없으면 false.
        /// 성공하면 slotMask의 해당 비트를 켜고 비주얼을 다시 그린다.
        /// </summary>
        public bool TryInstallSlot(PlayerInventory inventory, int slotIndex)
        {
            if (inventory == null || slotIndex < 0 || slotIndex >= SlotCount)
                return false;

            if (level < SlotUnlockLevel || HasSlot(slotIndex))
                return false;

            List<ShelterMaterialRequirement> requirements = GetSlotRequirements(slotIndex);
            if (!HasAllMaterials(inventory, requirements) || !ConsumeMaterials(inventory, requirements))
                return false;

            slotMask |= 1 << slotIndex;
            BuildVisual();

            AudioManager.Instance?.PlayCraftSuccess();
            Debug.Log($"[Shelter] {SlotNames[slotIndex]} 슬롯을 설치했다.");
            return true;
        }

        /// <summary>
        /// 세이브에서 읽은 레벨/슬롯 상태를 그대로 되돌린다(SaveLoadController.RestoreShelter 전용).
        /// 재료를 소모하지 않고, 비주얼만 다시 그린다. 저장값 0은 옛 세이브의 "필드 없음"이므로 Lv1로 본다.
        /// </summary>
        public void ApplySavedState(int savedLevel, int savedSlotMask)
        {
            level = Mathf.Clamp(savedLevel <= 0 ? 1 : savedLevel, 1, maxLevel);

            int validMask = (1 << SlotCount) - 1;
            slotMask = savedSlotMask & validMask;

            BuildVisual();
        }

        /// <summary>지정한 레벨로 올라가는 데 필요한 재료 목록. 정의가 없으면 빈 목록(= 승급 불가 아님, 무료).</summary>
        public List<ShelterMaterialRequirement> GetLevelRequirements(int targetLevel)
        {
            if (targetLevel == 2) return level2Requirements;
            if (targetLevel == 3) return level3Requirements;
            return null;
        }

        /// <summary>지정한 슬롯의 재료 목록.</summary>
        public List<ShelterMaterialRequirement> GetSlotRequirements(int slotIndex)
        {
            switch (slotIndex)
            {
                case SlotDoor: return doorSlotRequirements;
                case SlotBed: return bedSlotRequirements;
                case SlotChest: return chestSlotRequirements;
                default: return null;
            }
        }

        /// <summary>요구 재료를 전부 들고 있는지 확인한다(소모하지 않는다). UI에서도 그대로 쓸 수 있다.</summary>
        public static bool HasAllMaterials(PlayerInventory inventory, List<ShelterMaterialRequirement> requirements)
        {
            if (inventory == null)
                return false;

            if (requirements == null)
                return true;

            foreach (var requirement in requirements)
            {
                if (requirement == null || string.IsNullOrEmpty(requirement.itemName) || requirement.count <= 0)
                    continue;

                if (CountByName(inventory, requirement.itemName) < requirement.count)
                    return false;
            }

            return true;
        }

        /// <summary>
        /// 요구 재료를 실제로 소모한다. **반드시 HasAllMaterials로 전부 확인한 뒤에 부를 것** -
        /// 중간에 모자라면 이미 지운 재료를 되돌릴 방법이 없다(그래서 호출부가 항상 먼저 검사한다).
        /// </summary>
        private static bool ConsumeMaterials(PlayerInventory inventory, List<ShelterMaterialRequirement> requirements)
        {
            if (inventory == null)
                return false;

            if (requirements == null)
                return true;

            foreach (var requirement in requirements)
            {
                if (requirement == null || string.IsNullOrEmpty(requirement.itemName) || requirement.count <= 0)
                    continue;

                int remaining = requirement.count;
                for (int i = inventory.items.Count - 1; i >= 0 && remaining > 0; i--)
                {
                    InventoryItem item = inventory.items[i];
                    if (item == null || item.data == null || item.data.itemName != requirement.itemName)
                        continue;

                    inventory.items.RemoveAt(i);
                    remaining--;
                }

                if (remaining > 0)
                    return false;
            }

            return true;
        }

        /// <summary>인벤토리에 같은 이름의 아이템이 몇 개 있는지 센다(ItemData 참조가 아니라 이름으로 대조).</summary>
        public static int CountByName(PlayerInventory inventory, string itemName)
        {
            if (inventory == null || string.IsNullOrEmpty(itemName))
                return 0;

            int count = 0;
            foreach (var item in inventory.items)
            {
                if (item != null && item.data != null && item.data.itemName == itemName)
                    count++;
            }

            return count;
        }

        /// <summary>모자란 재료를 "나뭇가지 2/6" 형태의 한 줄 문자열로 만든다(로그/프롬프트용).</summary>
        public static string DescribeMissing(PlayerInventory inventory, List<ShelterMaterialRequirement> requirements)
        {
            if (requirements == null || requirements.Count == 0)
                return "재료 없음";

            var parts = new List<string>();
            foreach (var requirement in requirements)
            {
                if (requirement == null || string.IsNullOrEmpty(requirement.itemName) || requirement.count <= 0)
                    continue;

                int owned = CountByName(inventory, requirement.itemName);
                if (owned < requirement.count)
                    parts.Add($"{requirement.itemName} {owned}/{requirement.count}");
            }

            return parts.Count == 0 ? "재료는 충분하다" : string.Join(" · ", parts);
        }

        // ── 취침 목적지 계산 (game-designer 지적: 자정 이후 취침이 1.25일을 건너뛴다) ──────────────
        //
        // 무엇이 틀렸었나: 예전 계산은 `ElapsedDays + 1`이었다. 밤은 하루의 끝(TimeOfDay01 0.75~1.0)과
        // **다음 날의 시작**(0~0.25) 양쪽에 걸쳐 있는데, 자정을 넘긴 시각에는 ElapsedDays가 이미 +1 된
        // 상태다. 거기에 또 +1을 하면 의도(그날 아침까지 0.25일)의 5배인 1.25일을 건너뛴다.
        // 그 결과 배 엔딩의 15일 조건 도달이 74.5분 → 59.5분으로 20% 짧아졌다(Design_MidGame 7장).
        //
        // 고친 방법: 시각을 0.75일(= 밤의 시작) 앞으로 밀어 놓고 날짜를 내림한다. 그러면 일몰 직후와
        // 자정 직후가 **같은 날 번호**로 접히므로, 한 밤 안의 어느 시각에 자든 도착지가 하나로 정해진다.
        //   · 일몰 직후 t=0.76 (day D) → floor(D+0.76+0.75) = D+1 → 도착 (D+1.25)일 = 0.49일 건너뜀
        //   · 자정 직후 t=0.01 (day D+1, 같은 밤) → floor(D+1.01+0.75) = D+1 → 같은 도착지, 0.24일 건너뜀
        //   · 일출 직전 t=0.24 (day D+1) → floor(D+1.24+0.75) = D+1 → 같은 도착지, 0.01일 건너뜀
        // 시계가 뒤로 가는 경우는 없다: n = floor(x + 0.75) > x - 0.25 이므로 항상 n + 0.25 > x다.

        /// <summary>밤의 시작(= 낮의 끝) 시각. SurvivalClock.IsDaytime의 상한과 같은 기준이다.</summary>
        private const float NightStartTimeOfDay = 0.75f;

        /// <summary>일출(아침) 시각. SurvivalClock.IsDaytime의 하한/DayNightCycle과 같은 기준이다.</summary>
        private const float MorningTimeOfDay = 0.25f;

        /// <summary>
        /// 지금 취침하면 눈을 뜨는 날(ElapsedDays 기준, 0 = 1일차). 위 주석의 계산이다.
        /// 시계가 없거나 하루 길이가 0 이하면(0 나누기) 0을 돌려주므로, 호출부는 시계를 먼저 확인할 것.
        /// [ui-engineer] InteractionPromptUI가 같은 식을 따로 들고 있었는데, 그쪽이 이 메서드를 부르면
        /// 프롬프트의 "N일차 아침" 표기가 실제 도착지와 갈라지지 않는다.
        /// </summary>
        public static int GetWakeDay(SurvivalClock clock)
        {
            if (clock == null || clock.secondsPerDay <= 0f)
                return 0;

            return Mathf.FloorToInt(clock.elapsedSeconds / clock.secondsPerDay + NightStartTimeOfDay);
        }

        /// <summary>
        /// 지금 취침하면 도달하는 게임 내 경과 시간(초). 곧 다음 아침(TimeOfDay01 == 0.25)이다.
        /// 건너뛰는 시간을 표시하려면 이 값에서 clock.elapsedSeconds를 빼면 된다.
        /// </summary>
        public static float GetWakeSeconds(SurvivalClock clock)
        {
            if (clock == null || clock.secondsPerDay <= 0f)
                return 0f;

            return (GetWakeDay(clock) + MorningTimeOfDay) * clock.secondsPerDay;
        }
    }

    /// <summary>
    /// 쉼터 승급/슬롯 설치에 필요한 재료 한 줄(아이템 이름 + 개수).
    /// ItemData 참조가 아니라 **이름**인 이유: 이 컴포넌트는 프리팹에만 붙어 있고 프리팹 편집은
    /// 디렉터 전용이라 인스펙터에서 ItemData를 배선할 수 없다(AGENT_BRIEF 2장 3번).
    /// 이름은 인벤토리 안 ItemData.itemName과 대조하므로 레지스트리 로드가 필요 없다.
    /// </summary>
    [System.Serializable]
    public class ShelterMaterialRequirement
    {
        [Tooltip("ItemData.itemName과 정확히 같아야 한다 (예: 나뭇가지 / 야자잎 / 노끈 / 천조각 / 대나무 / 돌조각)")]
        public string itemName;

        [Tooltip("필요한 개수")]
        public int count = 1;

        public ShelterMaterialRequirement() { }

        public ShelterMaterialRequirement(string itemName, int count)
        {
            this.itemName = itemName;
            this.count = count;
        }
    }

    /// <summary>
    /// 저장궤 안의 아이템 한 묶음(ItemData + 개수). 정착 배치 2.
    /// ShelterMaterialRequirement가 **이름**을 쓰는 것과 반대로 여기는 **참조**를 쓴다: 이 묶음은
    /// 인스펙터에 노출되는 설정값이 아니라 런타임 상태이고, 들어오는 경로(플레이어 인벤토리 /
    /// ItemData.cookedResult / WaterStill.bottledWaterItem)가 전부 이미 ItemData 참조이기 때문이다.
    /// 세이브는 이름으로 기록되며, 이름→ItemData 해석은 그 캐시를 이미 들고 있는 SaveLoadController가 한다.
    /// </summary>
    [System.Serializable]
    public class ShelterItemStack
    {
        public ItemData data;
        public int count;

        public ShelterItemStack() { }

        public ShelterItemStack(ItemData data, int count)
        {
            this.data = data;
            this.count = count;
        }
    }
}
