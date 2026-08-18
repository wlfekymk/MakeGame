using System.Collections.Generic;
using UnityEngine;
using MakeGame.Data;
using MakeGame.Player;

namespace MakeGame.Systems
{
    /// <summary>
    /// 밭에 심을 수 있는 작물의 종류.
    ///
    /// **값을 바꾸거나 중간에 끼워 넣지 말 것.** 이 값이 정수 그대로 세이브
    /// (<see cref="FarmPlotSaveEntry.cropKind"/>)에 들어간다 - 순서가 바뀌면 옛 세이브의 야자 묘목이
    /// 해조류로 자란다. HazardType / BuildPieceType / CraftStationKind와 완전히 같은 제약이다
    /// (AGENT_BRIEF 3장 "추가는 반드시 맨 끝에").
    /// </summary>
    public enum FarmCropKind
    {
        /// <summary>야자 묘목 - 코코넛을 맺는다. 가장 느리다(3일).</summary>
        PalmSapling = 0,

        /// <summary>해조류 밭 - 조간대 흙에 담수를 대어 미역을 기른다(1.5일).</summary>
        Seaweed = 1,

        /// <summary>약초 - 해독 재료가 되는 잎을 낸다. 가장 빠르다(1일).</summary>
        Herb = 2
    }

    /// <summary>
    /// 플레이어가 설치하는 밭 한 칸. 씨앗을 심어 두면 시간이 지나 자라고, 다 자라면 수확한다.
    /// Stranded Deep의 감자·유카 재배에 해당하는 자리이며, 이 게임은 열대 무인도라 작물을
    /// **야자 묘목 · 해조류 · 약초** 세 가지로 잡았다.
    ///
    /// [설치·저장 규약은 CraftStation / Smoker와 같다]
    ///  · 설치: 인벤토리의 키트 아이템(<see cref="FarmPlotKitItemName"/>, ItemData.isPlaceable) + G.
    ///    키트 에셋의 placementPrefab이 비어 있으면 시작 시 런타임 원본(템플릿)을 만들어 끼워 넣는다
    ///    (<see cref="EnsureKitPlacementTemplate"/> - Smoker.EnsureKitPlacementTemplate과 같은 절차.
    ///    프리팹 에셋 생성은 이 작업의 락 밖이다).
    ///  · 활성 목록: Campfire.Active / CraftStation.Active / Smoker.Active와 같은 방식의 정적 목록
    ///    (R1 리셋 훅 포함). 설치 원본(템플릿)은 이 목록에 들어가지 않으므로 저장할 때마다 지하
    ///    -5000m에 유령 밭이 하나씩 기록되는 사고가 구조적으로 일어나지 않는다.
    ///  · 외형: 신규 모델 없이 StructureVisualBuilder의 프리미티브 조합으로 만든다
    ///    (흙 두둑 + 나무 테두리, 그 위에 자란 만큼의 포기).
    ///
    /// [상호작용은 이 컴포넌트가 직접 E를 읽는다 - 근거]
    /// InteractionController.cs가 이 작업의 락 밖이라 InteractWithTarget에 새 분기를 넣을 수 없다.
    /// 다른 시설이 쓴 두 가지 우회는 밭에 쓸 수 없었다:
    ///  · Smoker의 수법(기존 컴포넌트를 얹어 그 분기를 물려받기)은 Campfire를 붙여야 성립하는데,
    ///    밭에 모닥불을 얹으면 E가 "불 지피기"가 되고 연료(나뭇가지)까지 태운다.
    ///  · ResourceNode를 얹는 방법은 한 키에 심기/물주기/수확 **세 가지**를 태울 수 없다.
    ///    ResourceNode.Harvest는 virtual이 아니고 분기 훅도 없으며, 실패 경로(HarvestFailed)는
    ///    훅이 불리기 **전에** 이미 PlayActionFail을 울린다(ResourceNode.ReportHarvestFailure) -
    ///    심기가 매번 "실패음 + 성공음"으로 들린다.
    /// 그래서 조준 판정만 InteractionController에서 빌려 오고(TryGetLookTarget - 그 메서드는
    /// "조준의 유일한 소스"로 public 공개돼 있다) 키 처리는 이 클래스가 한다. 선례는
    /// WaterStill.bottleModifierKey다(그쪽도 락 밖의 입력 분기를 못 늘려 컴포넌트가 Input을 직접 읽는다).
    ///
    /// **E가 두 번 처리될 수 없다:** 밭을 조준한 프레임에 InteractionController.InteractWithTarget은
    /// 자기 분기를 하나도 맞히지 못하고(밭에는 StorageChest/ResourceNode/Campfire/Shelter/건축조각/
    /// 뗏목 컴포넌트가 없다) 맨 끝의 StorageChest.Focused 폴백만 남는데, 그 값은 같은 프레임에
    /// BuildingSystem.UpdateChestFocus가 **같은 규칙의 레이**로 갱신하면서 상자가 아니면 null로
    /// 지운다(BuildingSystem.Chest.cs:49-56). 즉 밭을 겨눈 E는 언제나 이 클래스에만 도달한다.
    ///
    /// [에셋이 없으면 조용히 비활성이다]
    /// 씨앗/수확물 ItemData가 아직 없으면 심기가 재료를 소모하지 않고 실패만 하고, 밭 키트 ItemData가
    /// 없으면 설치 자체가 존재하지 않는다. 어느 쪽도 예외를 던지지 않는다(Smoker와 같은 규약).
    /// </summary>
    public class FarmPlot : MonoBehaviour
    {
        // ── 이름 규약 ────────────────────────────────────────────────────────────
        //
        // 아래 문자열은 ItemData.itemName과 **문자 그대로** 대조된다(CraftStation/Smoker의 키트 이름
        // 규약과 같다 - 키트 이름에는 공백이 없다).

        /// <summary>밭 키트 아이템의 itemName.</summary>
        public const string FarmPlotKitItemName = "밭키트";

        /// <summary>화면에 보여줄 시설 이름.</summary>
        public const string DisplayName = "밭";

        /// <summary>물주기에 소모하는 아이템(Item_생수.asset 실측값).</summary>
        public const string WaterItemName = "생수";

        // ── 밸런스 상수 ──────────────────────────────────────────────────────────
        //
        // 1일 = SurvivalClock.secondsPerDay = 600초(씬 실측값). 성장 시간을 넉넉히 잡은 이유는 하나다 -
        // 농사가 채집을 대체하면 섬을 돌아다닐 이유가 사라진다. 가장 빠른 약초조차 1일이고, 그 사이
        // 플레이어는 여전히 나가서 주워 와야 한다. 밭은 "돌아왔을 때 뭔가 늘어나 있는 것"이지
        // "나가지 않아도 되는 것"이 아니다.

        /// <summary>게임 내 1일의 초. SurvivalClock.secondsPerDay(씬 600)와 같은 값이다.</summary>
        private const float SecondsPerDay = 600f;

        /// <summary>비가 올 때 성장 속도에 더해지는 배수(합산). 1.0이면 맑을 때의 2배로 자란다.</summary>
        public const float RainGrowthBonus = 1.0f;

        /// <summary>물을 준 상태에서 성장 속도에 더해지는 배수(합산).</summary>
        public const float WateredGrowthBonus = 0.6f;

        /// <summary>물주기 한 번이 유지되는 시간(게임 내 초). 생수 1개 = 게임 내 5분.</summary>
        public const float WaterDurationSeconds = 300f;

        /// <summary>수확할 때 씨앗 1개를 되돌려 받을 확률.</summary>
        private const float SeedReturnChance = 0.5f;

        /// <summary>심기로 주는 채집(Harvesting) 경험치.</summary>
        private const float PlantExperience = 4f;

        /// <summary>물주기로 주는 채집 경험치.</summary>
        private const float WaterExperience = 2f;

        /// <summary>
        /// 한 번의 정산에서 반영할 수 있는 최대 시간(초). 취침(Shelter.TrySleep)이 시계를 다음 아침까지
        /// 점프시키므로 상한이 필요하다 - Shelter.maxSettleSeconds(1800)와 같은 값을 쓴다.
        /// </summary>
        private const float MaxSettleSeconds = 1800f;

        /// <summary>성장 정산 주기(실시간 초). 매 프레임 시계를 읽을 이유가 없다.</summary>
        private const float SettleInterval = 0.5f;

        // ── 작물 표 ──────────────────────────────────────────────────────────────

        /// <summary>
        /// 작물 하나의 고정 수치. 코드가 유일한 소스다(밭은 씬에도 프리팹에도 인스턴스가 없다 -
        /// 런타임 설치형이라 인스펙터 오버라이드 경로 자체가 없다).
        /// </summary>
        private sealed class CropDefinition
        {
            public readonly FarmCropKind Kind;
            public readonly string DisplayName;
            public readonly string SeedItemName;
            public readonly string HarvestItemName;

            /// <summary>다 자라는 데 필요한 성장 시간(게임 내 초, 배수 1.0 기준).</summary>
            public readonly float GrowSeconds;

            /// <summary>1회 수확 수량의 하한/상한(둘 다 포함).</summary>
            public readonly int MinYield;
            public readonly int MaxYield;

            /// <summary>밭 한 칸에 서는 포기 수(1~4). 수확량이 아니라 **외형 밀도**를 정한다.</summary>
            public readonly int PlantCount;

            /// <summary>수확 시 주는 채집(Harvesting) 경험치.</summary>
            public readonly float HarvestExperience;

            /// <summary>다 자란 포기의 높이(m). 실루엣으로 세 작물이 갈리게 하는 값이다.</summary>
            public readonly float MatureHeight;

            /// <summary>줄기/잎 색.</summary>
            public readonly Color FoliageColor;

            /// <summary>다 자랐을 때 맺히는 열매/꽃 색.</summary>
            public readonly Color FruitColor;

            public CropDefinition(FarmCropKind kind, string displayName, string seedItemName,
                string harvestItemName, float growSeconds, int minYield, int maxYield, int plantCount,
                float harvestExperience, float matureHeight, Color foliageColor, Color fruitColor)
            {
                Kind = kind;
                DisplayName = displayName;
                SeedItemName = seedItemName;
                HarvestItemName = harvestItemName;
                GrowSeconds = growSeconds;
                MinYield = minYield;
                MaxYield = maxYield;
                PlantCount = plantCount;
                HarvestExperience = harvestExperience;
                MatureHeight = matureHeight;
                FoliageColor = foliageColor;
                FruitColor = fruitColor;
            }
        }

        /// <summary>
        /// 작물 3종의 수치표. **인덱스가 (int)FarmCropKind와 같아야 한다**(TryGetDefinition이 그 전제로
        /// 직접 색인한다). readonly 정적 배열이라 한 번만 만들어지고 프레임당 할당이 없다.
        ///
        /// 성장 시간 근거: 야자 3일(1800) / 해조류 1.5일(900) / 약초 1일(600). 수확량은 야자가 가장
        /// 적고(2~3) 가장 느리지만 코코넛은 갈증 30짜리라 단가가 제일 높고, 약초는 가장 빠르지만
        /// 그 자체로는 먹을 수 없는 재료다 - 세 작물이 "빠름 ↔ 가치"로 갈리게 배치했다.
        /// </summary>
        private static readonly CropDefinition[] Crops =
        {
            new CropDefinition(FarmCropKind.PalmSapling, "야자 묘목", "야자씨앗", "코코넛",
                SecondsPerDay * 3f, 2, 3, 1, 14f, 1.15f,
                StructureVisualBuilder.FrondGreen, new Color(0.42f, 0.30f, 0.20f)),

            new CropDefinition(FarmCropKind.Seaweed, "해조류", "해조류씨앗", "해조류",
                SecondsPerDay * 1.5f, 3, 4, 4, 10f, 0.55f,
                new Color(0.22f, 0.45f, 0.33f), new Color(0.16f, 0.32f, 0.26f)),

            new CropDefinition(FarmCropKind.Herb, "약초", "약초씨앗", "약초",
                SecondsPerDay * 1f, 2, 3, 3, 8f, 0.42f,
                StructureVisualBuilder.MeadowGreen, new Color(0.88f, 0.87f, 0.72f)),
        };

        /// <summary>
        /// 포기를 세우는 밭 안쪽 위치(로컬 XZ). 앞에서부터 PlantCount개만 쓴다 - 1포기면 한가운데,
        /// 4포기면 네 귀퉁이가 되도록 배치 순서를 잡았다.
        /// </summary>
        private static readonly Vector3[] PlantOffsets =
        {
            new Vector3(0f, 0f, 0f),
            new Vector3(-0.42f, 0f, -0.42f),
            new Vector3(0.42f, 0f, 0.42f),
            new Vector3(0.42f, 0f, -0.42f),
        };

        // ── 활성 목록 / 템플릿 ───────────────────────────────────────────────────

        private static readonly List<FarmPlot> activePlots = new List<FarmPlot>();

        /// <summary>현재 씬에 살아 있는 밭 목록(읽기 전용). 설치 원본(템플릿)은 포함되지 않는다.</summary>
        public static IReadOnlyList<FarmPlot> Active => activePlots;

        /// <summary>설치 원본을 담아 두는 루트(DontDestroyOnLoad). CraftStation.templateRoot와 같은 방식이다.</summary>
        private static GameObject templateRoot;

        /// <summary>템플릿을 세워 두는 y 좌표. 지형(y ≈ 0~30)에서 충분히 떨어져 있으면 된다.</summary>
        private const float TemplateParkY = -5000f;

        /// <summary>이름 → ItemData 조회 캐시(R1 리셋 대상). 씨앗 에셋이 없으면 비어 있는 채로 남는다.</summary>
        private static Dictionary<string, ItemData> itemsByName;

        /// <summary>조준 판정과 키 설정을 빌려 오는 상호작용 컨트롤러(R1 리셋 대상).</summary>
        private static InteractionController cachedController;

        /// <summary>성장 정산의 기준이 되는 게임 내 시계(R1 리셋 대상).</summary>
        private static SurvivalClock cachedClock;

        /// <summary>
        /// 이번 프레임의 E를 이미 한 밭이 소비했는지 판정하는 프레임 번호.
        /// 밭이 여러 개여도 E 한 번에 레이캐스트는 정확히 한 번만 나간다.
        /// </summary>
        private static int lastInteractFrame = -1;

        /// <summary>
        /// 이 인스턴스가 설치 원본인지. 부모가 templateRoot인지로만 판정하므로 별도 플래그가 필요 없다
        /// (Instantiate로 만든 사본은 부모가 없어 언제나 false다 - CraftStation과 같은 규칙).
        /// </summary>
        private bool IsPlacementTemplate =>
            templateRoot != null && transform.parent == templateRoot.transform;

        // ── 상태 (세이브 대상) ────────────────────────────────────────────────────

        /// <summary>지금 심겨 있는 작물이 있는지.</summary>
        private bool hasCrop;

        /// <summary>심겨 있는 작물의 종류(hasCrop이 false면 의미 없음).</summary>
        private FarmCropKind cropKind;

        /// <summary>지금까지 쌓인 성장 시간(게임 내 초 · 배수가 곱해진 값).</summary>
        private float growthSeconds;

        /// <summary>물을 준 효과가 남아 있는 시간(게임 내 초).</summary>
        private float wateredSecondsRemaining;

        // ── 내부 상태 (저장하지 않음) ─────────────────────────────────────────────

        /// <summary>마지막 정산 시각(게임 내 초). **저장하지 않는다** - Shelter.lastSettleSeconds와 같은 이유다.</summary>
        private float lastSettleSeconds;
        private bool settleInitialized;

        /// <summary>정산 주기 타이머(실시간 초).</summary>
        private float settleTimer;

        /// <summary>흙 두둑/테두리를 이미 만들었는지(설치 원본을 복제한 사본은 다시 만들지 않는다).</summary>
        private bool bedBuilt;

        /// <summary>지금 화면에 서 있는 포기들의 부모. 단계가 바뀔 때 통째로 갈아 끼운다.</summary>
        private Transform cropRoot;

        /// <summary>cropRoot에 반영돼 있는 단계. 이 값이 바뀔 때만 포기를 다시 만든다(프레임당 할당 0).</summary>
        private int builtStage = -1;

        /// <summary>cropRoot에 반영돼 있는 작물 종류.</summary>
        private FarmCropKind builtKind = FarmCropKind.PalmSapling;

        /// <summary>경험치를 줄 대상(마지막으로 이 밭을 다룬 플레이어).</summary>
        private PlayerSkills cachedSkills;

        // ── 읽기 전용 공개값 (세이브 · 프롬프트 UI 전용) ──────────────────────────

        /// <summary>지금 작물이 심겨 있는지.</summary>
        public bool HasCrop => hasCrop;

        /// <summary>심겨 있는 작물의 종류. HasCrop이 false면 의미가 없다.</summary>
        public FarmCropKind CropKind => cropKind;

        /// <summary>쌓인 성장 시간(게임 내 초 - 세이브용).</summary>
        public float GrowthSeconds => growthSeconds;

        /// <summary>남은 물주기 효과 시간(게임 내 초 - 세이브용).</summary>
        public float WateredSecondsRemaining => wateredSecondsRemaining;

        /// <summary>지금 물이 대어져 있는지.</summary>
        public bool IsWatered => wateredSecondsRemaining > 0f;

        /// <summary>성장 진행도 0~1. 작물이 없으면 0이다.</summary>
        public float Progress01
        {
            get
            {
                if (!hasCrop || !TryGetDefinition(cropKind, out CropDefinition def) || def.GrowSeconds <= 0f)
                    return 0f;

                return Mathf.Clamp01(growthSeconds / def.GrowSeconds);
            }
        }

        /// <summary>다 자라 수확할 수 있는 상태인지.</summary>
        public bool IsRipe => hasCrop && Progress01 >= 1f;

        /// <summary>심겨 있는 작물의 표시 이름. 작물이 없으면 빈 문자열.</summary>
        public string CropDisplayName =>
            hasCrop && TryGetDefinition(cropKind, out CropDefinition def) ? def.DisplayName : "";

        /// <summary>심겨 있는 작물의 수확물 ItemData. 없거나 에셋이 없으면 null.</summary>
        public ItemData HarvestItem =>
            hasCrop && TryGetDefinition(cropKind, out CropDefinition def)
                ? FindItemByName(def.HarvestItemName)
                : null;

        /// <summary>
        /// 심겨 있는 작물의 수확 수량 범위를 알려준다(프롬프트의 "1회당 N개" 표시용).
        /// 상태를 바꾸지 않으므로 매 프레임 호출해도 안전하다.
        /// </summary>
        /// <returns>작물이 심겨 있으면 true.</returns>
        public bool TryGetYieldRange(out int minYield, out int maxYield)
        {
            minYield = 0;
            maxYield = 0;

            if (!hasCrop || !TryGetDefinition(cropKind, out CropDefinition def))
                return false;

            minYield = def.MinYield;
            maxYield = def.MaxYield;
            return true;
        }

        /// <summary>
        /// 이 인벤토리로 지금 심을 수 있는 첫 씨앗을 찾는다(인벤토리 순서 그대로 - 다른 "첫 아이템"
        /// 규약과 같다). 상태를 바꾸지 않으므로 프롬프트가 매 프레임 호출해도 안전하다.
        /// </summary>
        /// <returns>심을 수 있는 씨앗이 있으면 true.</returns>
        public static bool TryFindPlantableSeed(PlayerInventory inventory, out ItemData seed, out FarmCropKind kind)
        {
            seed = null;
            kind = FarmCropKind.PalmSapling;

            if (inventory == null)
                return false;

            var items = inventory.items;
            for (int i = 0; i < items.Count; i++)
            {
                InventoryItem item = items[i];
                if (item == null || item.data == null)
                    continue;

                if (!TryGetKindForSeedItem(item.data.itemName, out FarmCropKind found))
                    continue;

                seed = item.data;
                kind = found;
                return true;
            }

            return false;
        }

        /// <summary>씨앗 아이템 이름 → 작물 종류. 씨앗이 아니면 false.</summary>
        public static bool TryGetKindForSeedItem(string itemName, out FarmCropKind kind)
        {
            kind = FarmCropKind.PalmSapling;
            if (string.IsNullOrEmpty(itemName))
                return false;

            for (int i = 0; i < Crops.Length; i++)
            {
                if (Crops[i].SeedItemName == itemName)
                {
                    kind = Crops[i].Kind;
                    return true;
                }
            }

            return false;
        }

        /// <summary>작물 종류의 표시 이름(프롬프트용). 모르는 값이면 빈 문자열.</summary>
        public static string GetCropDisplayName(FarmCropKind kind)
        {
            return TryGetDefinition(kind, out CropDefinition def) ? def.DisplayName : "";
        }

        /// <summary>
        /// 작물 종류가 다 자라는 데 걸리는 시간(게임 내 일). 배수 1.0(맑음 · 물 없음) 기준이다.
        /// 프롬프트가 "며칠 걸리는가"를 안내할 때 쓴다.
        /// </summary>
        public static float GetGrowDays(FarmCropKind kind)
        {
            return TryGetDefinition(kind, out CropDefinition def) ? def.GrowSeconds / SecondsPerDay : 0f;
        }

        /// <summary>
        /// 지금 비 보정이 걸려 있는지. 프롬프트가 "지금 빨리 자라는 중"을 알릴 때 쓴다.
        /// <see cref="AdvanceGrowth"/>가 실제로 보는 것과 **같은 판정**이다(UI가 날씨 규칙을 따로 만들지 않는다).
        /// </summary>
        public static bool IsRainBoostActive => IsRainingNow();

        /// <summary>
        /// 지금 이 인벤토리로 물을 줄 수 있는지. <see cref="TryWater"/>가 실제로 쓰는 조건 그대로이며
        /// 상태를 바꾸지 않으므로 프롬프트가 매 프레임 호출해도 안전하다(소리도 나지 않는다).
        /// </summary>
        public bool CanWater(PlayerInventory inventory)
        {
            if (inventory == null || wateredSecondsRemaining > 0f)
                return false;

            ItemData water = FindItemByName(WaterItemName);
            return water != null && inventory.FindItem(water) != null;
        }

        /// <summary>
        /// 지금 이 인벤토리로 수확할 수 있는지. <see cref="TryHarvest"/>가 실제로 쓰는 조건과 같다
        /// (수량은 가방에 들어가는 만큼 줄어들므로, 최소 1개가 들어가면 수확은 성립한다).
        /// 상태를 바꾸지 않으므로 프롬프트가 매 프레임 호출해도 안전하다.
        /// </summary>
        public bool CanHarvest(PlayerInventory inventory)
        {
            if (!IsRipe || inventory == null || !TryGetDefinition(cropKind, out CropDefinition def))
                return false;

            ItemData harvest = FindItemByName(def.HarvestItemName);
            return harvest != null && inventory.CanAccept(harvest, 1);
        }

        // ── 수명 주기 ────────────────────────────────────────────────────────────

        /// <summary>
        /// [R1 규약] 도메인 리로드를 끈 플레이 모드에서 이전 세션의 정적 상태가 새지 않게 비운다
        /// (CraftStation.ResetStaticCache / Smoker.ResetStaticCache와 같은 이유).
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticCache()
        {
            activePlots.Clear();
            templateRoot = null;
            itemsByName = null;
            cachedController = null;
            cachedClock = null;
            lastInteractFrame = -1;
        }

        private void OnEnable()
        {
            if (IsPlacementTemplate)
                return;

            if (!activePlots.Contains(this))
                activePlots.Add(this);
        }

        private void OnDisable()
        {
            activePlots.Remove(this);
        }

        /// <summary>
        /// 콜라이더와 흙 두둑/테두리를 만들고 지면에 내려놓는다. 설치 원본은 지면 스냅을 하지 않는다
        /// (원본은 y = -5000에 세워 두는 것이 규약이다 - CraftStation.Awake와 같다).
        /// </summary>
        private void Awake()
        {
            EnsureCollider();
            BuildBedVisual();

            if (IsPlacementTemplate)
                return;

            transform.position = TerrainSampler.SnapToGround(transform.position);
        }

        /// <summary>
        /// 성장 정산과 E 입력 처리. 둘 다 프레임당 할당이 없다(정산은 값 계산뿐이고, 입력은
        /// 키가 눌린 프레임에만 조준 판정을 한 번 한다).
        /// </summary>
        private void Update()
        {
            if (IsPlacementTemplate)
                return;

            settleTimer -= Time.deltaTime;
            if (settleTimer <= 0f)
            {
                settleTimer = SettleInterval;
                SettleElapsedTime();
            }

            TryHandleInteractKey();
        }

        // ── 성장 ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// **마지막 정산 시각과의 차이만큼** 성장을 진행시킨다.
        ///
        /// 왜 Time.deltaTime이 아니라 시계인가: Shelter.TrySleep이 시계를 다음 아침으로 점프시키는데
        /// (WaterStill/Campfire처럼 실시간으로 재면) 취침으로 건너뛴 밤이 통째로 사라져, 야자 3일짜리
        /// 작물을 자면서 기를 수 없게 된다. 시계 차이로 재면 취침 점프분이 그대로 들어온다.
        /// 성장은 다른 어떤 시스템도 진행시키지 않으므로 이 방식으로 이중 계산이 발생할 수 없다
        /// (Shelter.SettleElapsedTime과 완전히 같은 분담이다).
        /// </summary>
        private void SettleElapsedTime()
        {
            SurvivalClock clock = ResolveClock();
            if (clock == null)
            {
                // 시계가 없는 씬(단독 프리팹 테스트 등)에서도 밭이 멈춰 있지 않도록 실시간으로 떨어진다.
                AdvanceGrowth(SettleInterval);
                return;
            }

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

            AdvanceGrowth(Mathf.Min(delta, MaxSettleSeconds));
        }

        /// <summary>
        /// 흐른 시간만큼 성장과 물주기 효과를 진행시킨다.
        ///
        /// 성장 배수는 **합산**이다: 기본 1.0 + 비(RainGrowthBonus 1.0) + 물주기(WateredGrowthBonus 0.6).
        /// 최대 2.6배이며 **0이 되는 경우가 없다** - 물을 주지 않아도 자란다. 굶기면 멈추는 설계로
        /// 하지 않은 이유는 밸런스가 아니라 리듬이다(AGENT_BRIEF ★ "압박은 처벌이 아니라 리듬으로").
        /// 며칠 걸리는 작물이 물 한 번 놓쳤다고 통째로 멈추면 밭은 관리 부담이지 힐링이 아니다.
        ///
        /// 비 판정은 <see cref="WeatherSystem.IsRaining"/> 하나만 본다. RainIntensity01은 연출 세기라
        /// 게임플레이 수치가 읽지 않는다는 것이 이 프로젝트의 규약이다(WeatherSystem.cs:138-148 -
        /// 증류기/모닥불도 IsRaining만 본다). 여기서 그 규약을 깨면 날씨 수치 소유권이 두 갈래가 된다.
        /// </summary>
        private void AdvanceGrowth(float seconds)
        {
            if (seconds <= 0f)
                return;

            // 물주기 효과는 작물이 없어도 마른다(빈 밭에 물을 부어 두고 나중에 심는 악용 방지).
            if (wateredSecondsRemaining > 0f)
                wateredSecondsRemaining = Mathf.Max(0f, wateredSecondsRemaining - seconds);

            if (!hasCrop || !TryGetDefinition(cropKind, out CropDefinition def))
                return;

            if (growthSeconds >= def.GrowSeconds)
            {
                // 이미 다 자랐다. 더 쌓아 두지 않는다 - 쌓아 두면 수확 직후 다시 심은 작물이
                // 즉시 완성돼 버린다(Smoker.AdvanceSmoking이 진행도를 0으로 되돌리는 것과 같은 이유).
                growthSeconds = def.GrowSeconds;
                RefreshCropVisual();
                return;
            }

            float multiplier = 1f;
            if (IsRainingNow())
                multiplier += RainGrowthBonus;
            if (wateredSecondsRemaining > 0f)
                multiplier += WateredGrowthBonus;

            growthSeconds = Mathf.Min(def.GrowSeconds, growthSeconds + seconds * multiplier);
            RefreshCropVisual();
        }

        /// <summary>
        /// 지금 비가 오는지. WeatherSystem은 씬에 인스턴스가 없고 스스로 부트스트랩되므로
        /// (AGENT_BRIEF 3장) 정적 참조로만 접근한다. 아직 없으면 "맑음"으로 본다.
        /// </summary>
        private static bool IsRainingNow()
        {
            WeatherSystem weather = WeatherSystem.Active;
            return weather != null && weather.IsRaining;
        }

        // ── 상호작용 ─────────────────────────────────────────────────────────────

        /// <summary>
        /// 이번 프레임의 E가 이 밭을 겨눈 것이면 처리한다. 클래스 주석의 "E가 두 번 처리될 수 없다"
        /// 근거가 이 메서드의 전제다.
        ///
        /// 조준 판정은 InteractionController.TryGetLookTarget **그대로** 쓴다 - UI가 레이캐스트 사본을
        /// 만들었다가 화면과 실제가 갈라진 전례가 있어 그 메서드가 "조준의 유일한 소스"로 공개돼 있다.
        /// 키도 컨트롤러의 interactKey를 읽으므로 나중에 키가 바뀌면 밭도 따라간다.
        /// </summary>
        private void TryHandleInteractKey()
        {
            // 엔딩/사망/설정 화면은 timeScale 0으로 멈춘다. 그 뒤에서 밭이 자라거나 수확되면 안 된다.
            if (Time.timeScale <= 0f)
                return;

            // **E를 삼키는 규칙을 InteractionController.Update와 한 줄도 다르지 않게 맞춘다.**
            // 그쪽에서 이 세 경우의 E는 "창 닫기 / 조종 그만두기"이고 그 프레임의 월드 상호작용은
            // 아예 일어나지 않는다. 여기서 같은 조건을 걸지 않으면 상자 창을 닫는 E 한 번이
            // (조준이 밭에 걸려 있을 때) 창도 닫고 작물도 심는 이중 처리가 된다.
            if (MakeGame.UI.ChestUI.IsOpen || MakeGame.UI.RaftBuildUI.IsOpen)
                return;

            var sailing = RaftSailing.Active;
            if (sailing != null && sailing.IsSteering)
                return;

            // 이번 프레임에 다른 밭이 이미 E를 소비했다면 레이캐스트를 다시 쏘지 않는다.
            if (lastInteractFrame == Time.frameCount)
                return;

            InteractionController controller = ResolveController();
            if (controller == null)
                return;

            if (!Input.GetKeyDown(controller.interactKey))
                return;

            // 여기서 프레임을 찍는다 - 아래에서 무엇을 하든(대상이 밭이 아니어도) 이번 프레임의
            // 조준 판정은 끝난 것이므로, 밭이 10개여도 레이는 정확히 한 번만 나간다.
            lastInteractFrame = Time.frameCount;

            if (!controller.TryGetLookTarget(out GameObject target))
                return;

            // 시각 파츠에는 콜라이더가 없어 지금은 루트만 맞지만, 나중에 파츠에 콜라이더가 붙어도
            // 안전하도록 부모를 거슬러 찾는다(InteractionController가 상자/뗏목에 쓰는 규약과 같다).
            FarmPlot plot = target.GetComponentInParent<FarmPlot>();
            if (plot == null || plot.IsPlacementTemplate)
                return;

            plot.Interact(controller.inventory, controller.skills);
        }

        /// <summary>
        /// 밭에 대한 E 한 번을 상태에 따라 갈라 처리한다.
        ///  · 비어 있다 → 씨앗 심기
        ///  · 다 자랐다 → 수확
        ///  · 자라는 중 → 물주기(생수 1 소모)
        /// 세 갈래가 한 키를 나눠 쓰지만 겹치지 않는다 - 밭의 상태가 곧 배타적인 분기이기 때문이다
        /// (Shelter가 밤에는 취침, 낮에는 건축으로 갈리는 것과 같은 구조).
        /// </summary>
        public void Interact(PlayerInventory inventory, PlayerSkills skills)
        {
            if (skills != null)
                cachedSkills = skills;

            if (!hasCrop)
            {
                TryPlant(inventory, skills);
                return;
            }

            if (IsRipe)
            {
                TryHarvest(inventory, skills);
                return;
            }

            TryWater(inventory, skills);
        }

        /// <summary>
        /// 인벤토리의 첫 씨앗을 심는다. 실패하면 씨앗을 **절대 소모하지 않는다**.
        /// 실패하는 경우: 이미 심겨 있다 / 씨앗이 없다 / 씨앗 ItemData 에셋이 아직 없다.
        /// </summary>
        /// <returns>실제로 심었으면 true.</returns>
        public bool TryPlant(PlayerInventory inventory, PlayerSkills skills)
        {
            if (hasCrop || inventory == null)
                return false;

            if (!TryFindPlantableSeed(inventory, out ItemData seed, out FarmCropKind kind))
            {
                AudioManager.Instance?.PlayActionFail();
                Debug.Log("[FarmPlot] 심을 씨앗이 없다 (야자씨앗 / 해조류씨앗 / 약초씨앗).");
                return false;
            }

            if (!inventory.RemoveItems(seed, 1))
                return false;

            hasCrop = true;
            cropKind = kind;
            growthSeconds = 0f;

            // 심는 순간의 진행 기준점을 다시 잡는다 - 오래 방치된 밭이 심자마자 성장분을 몰아
            // 받는 것을 막는다(Shelter.RestoreChestState가 settleInitialized를 되돌리는 것과 같은 이유).
            settleInitialized = false;

            RefreshCropVisual();

            if (skills != null && PlantExperience > 0f)
                skills.AddExperience(SkillType.Harvesting, PlantExperience);

            AudioManager.Instance?.PlayCraftSuccess();
            return true;
        }

        /// <summary>
        /// 생수 1개를 부어 성장을 가속한다(<see cref="WateredGrowthBonus"/>, <see cref="WaterDurationSeconds"/>).
        /// 이미 물이 대어져 있으면 소모하지 않고 거절한다 - 생수는 배 엔딩 재료이기도 해서
        /// 실수로 연타해 몇 병이 사라지는 경로를 만들지 않는다.
        /// </summary>
        /// <returns>실제로 물을 줬으면 true.</returns>
        public bool TryWater(PlayerInventory inventory, PlayerSkills skills)
        {
            if (inventory == null)
                return false;

            if (wateredSecondsRemaining > 0f)
            {
                AudioManager.Instance?.PlayActionFail();
                Debug.Log("[FarmPlot] 이미 물이 충분하다.");
                return false;
            }

            ItemData water = FindItemByName(WaterItemName);
            if (water == null || !inventory.RemoveItems(water, 1))
            {
                AudioManager.Instance?.PlayActionFail();
                Debug.Log($"[FarmPlot] 물을 주려면 {WaterItemName} 1개가 필요하다.");
                return false;
            }

            wateredSecondsRemaining = WaterDurationSeconds;

            if (skills != null && WaterExperience > 0f)
                skills.AddExperience(SkillType.Harvesting, WaterExperience);

            AudioManager.Instance?.PlayDrink();
            return true;
        }

        /// <summary>
        /// 다 자란 작물을 거둔다. 수확물 + 확률로 씨앗 1개를 돌려주고 밭을 다시 빈 상태로 되돌린다
        /// (바로 다시 심을 수 있다).
        ///
        /// **아이템이 증발하는 경로를 만들지 않는다:** 가방에 들어갈 수 있는 만큼으로 수량을 먼저
        /// 줄이고(최소 1개도 못 넣으면 아예 수확하지 않는다), 그 뒤에야 상태를 비운다.
        /// </summary>
        /// <returns>실제로 수확했으면 true.</returns>
        public bool TryHarvest(PlayerInventory inventory, PlayerSkills skills)
        {
            if (!IsRipe || inventory == null || !TryGetDefinition(cropKind, out CropDefinition def))
                return false;

            ItemData harvest = FindItemByName(def.HarvestItemName);
            if (harvest == null)
            {
                AudioManager.Instance?.PlayActionFail();
                Debug.LogWarning($"[FarmPlot] '{def.HarvestItemName}' ItemData를 찾지 못해 수확할 수 없다.");
                return false;
            }

            // rng는 UnityEngine.Random을 쓴다. 월드 생성 스트림(섬 시드 System.Random)은 재현성이
            // 걸려 있어 건드리면 안 되지만, 이 주사위는 플레이어 입력에만 반응하므로 섬 배치를 밀지
            // 않는다(ResourceNode.RollHarvestingSkillBonus / ConsumptionSystem의 식중독 판정과 같은 근거).
            int count = Random.Range(def.MinYield, def.MaxYield + 1);
            while (count > 0 && !inventory.CanAccept(harvest, count))
                count--;

            if (count <= 0)
            {
                AudioManager.Instance?.PlayActionFail();
                Debug.Log("[FarmPlot] 가방이 가득 차 수확할 수 없다.");
                return false;
            }

            for (int i = 0; i < count; i++)
                inventory.AddItem(harvest);

            // 씨앗 일부 회수. 넣을 자리가 없으면 굴리지 않는다(AddItem이 거부하면서 성공한 수확이
            // 실패음으로 들리는 것을 막는다 - ResourceNode가 스킬 보너스에서 겪은 것과 같은 함정).
            ItemData seed = FindItemByName(def.SeedItemName);
            if (seed != null && Random.value < SeedReturnChance && inventory.CanAccept(seed, 1))
                inventory.AddItem(seed);

            if (skills != null && def.HarvestExperience > 0f)
                skills.AddExperience(SkillType.Harvesting, def.HarvestExperience);

            hasCrop = false;
            growthSeconds = 0f;
            RefreshCropVisual();

            AudioManager.Instance?.PlayPickup();
            EffectBuilder.PlayHarvestPop(gameObject);
            return true;
        }

        // ── 세이브 복원 ──────────────────────────────────────────────────────────

        /// <summary>
        /// 저장된 상태(작물 종류 · 성장 진행도 · 물주기 잔여)를 그대로 되돌린다.
        /// 마지막 정산 시각은 복원하지 않는다 - 불러오면 시계도 저장 시점으로 돌아가므로 다음 정산에서
        /// 현재 시계로 기준점을 다시 잡는 쪽이 정확하다(Shelter.RestoreChestState와 같은 판단).
        /// 모르는 작물 종류(미래 버전 세이브)는 "빈 밭"으로 떨어진다 - 예외를 던지지 않는다.
        /// </summary>
        public void ApplySavedState(bool savedHasCrop, int savedCropKind, float savedGrowthSeconds,
            float savedWateredSeconds)
        {
            hasCrop = savedHasCrop && TryGetDefinition((FarmCropKind)savedCropKind, out CropDefinition _);
            cropKind = hasCrop ? (FarmCropKind)savedCropKind : FarmCropKind.PalmSapling;
            growthSeconds = Mathf.Max(0f, savedGrowthSeconds);
            wateredSecondsRemaining = Mathf.Max(0f, savedWateredSeconds);

            settleInitialized = false;
            RefreshCropVisual();
        }

        // ── 조회 ─────────────────────────────────────────────────────────────────

        /// <summary>작물 종류 → 수치표. 모르는 값이면 false(예외를 던지지 않는다).</summary>
        private static bool TryGetDefinition(FarmCropKind kind, out CropDefinition definition)
        {
            int index = (int)kind;
            if (index < 0 || index >= Crops.Length)
            {
                definition = null;
                return false;
            }

            definition = Crops[index];
            return definition != null;
        }

        /// <summary>
        /// 이름으로 ItemData를 찾는다(최초 1회만 표를 만든다). 레지스트리 에셋이 없으면
        /// Smoker.FindItemByName과 같은 폴백(현재 로드된 ItemData 전수 조회)을 쓴다.
        /// </summary>
        private static ItemData FindItemByName(string itemName)
        {
            if (string.IsNullOrEmpty(itemName))
                return null;

            if (itemsByName == null)
            {
                itemsByName = new Dictionary<string, ItemData>();

                ItemDataRegistry registry = ItemDataRegistry.LoadFromResources();
                if (registry != null && registry.allItems != null)
                {
                    for (int i = 0; i < registry.allItems.Count; i++)
                        AddToNameCache(registry.allItems[i]);
                }
                else
                {
                    var all = Resources.FindObjectsOfTypeAll<ItemData>();
                    for (int i = 0; i < all.Length; i++)
                        AddToNameCache(all[i]);
                }
            }

            itemsByName.TryGetValue(itemName, out ItemData found);
            return found;
        }

        private static void AddToNameCache(ItemData item)
        {
            if (item == null || string.IsNullOrEmpty(item.itemName))
                return;

            if (!itemsByName.ContainsKey(item.itemName))
                itemsByName.Add(item.itemName, item);
        }

        /// <summary>조준 판정과 키 설정을 빌려 올 상호작용 컨트롤러. 한 번 찾아 정적으로 캐시한다.</summary>
        private static InteractionController ResolveController()
        {
            if (cachedController == null)
                cachedController = FindAnyObjectByType<InteractionController>();

            return cachedController;
        }

        /// <summary>성장 정산의 기준이 되는 게임 내 시계. Shelter.ResolveClock과 같은 방식이다.</summary>
        private static SurvivalClock ResolveClock()
        {
            if (cachedClock == null)
                cachedClock = FindAnyObjectByType<SurvivalClock>();

            return cachedClock;
        }

        // ── 설치 원본(placementPrefab) 공급 ─────────────────────────────────────
        //
        // Smoker.EnsureKitPlacementTemplate과 완전히 같은 이유·같은 절차다: G 설치 경로
        // (InteractionController.PlaceFirstPlaceableItem)는 placementPrefab != null 인 아이템만 놓는데,
        // 새로 만들 밭키트 에셋에는 그 필드를 채울 프리팹 자체가 없다(프리팹 3개뿐 - AGENT_BRIEF 1장).
        // **비어 있을 때만** 런타임 원본을 만들어 끼우므로, 나중에 진짜 프리팹이 배선되면 저절로 꺼진다.

        /// <summary>
        /// 밭 키트의 placementPrefab이 비어 있으면 런타임 원본을 만들어 채운다.
        /// 키트 에셋이 아직 없으면 아무 일도 하지 않는다(= 밭은 존재하지 않는 상태로 남는다).
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void EnsureKitPlacementTemplate()
        {
            ItemData kit = FindItemByName(FarmPlotKitItemName);
            if (kit == null || !kit.isPlaceable || kit.placementPrefab != null)
                return;

            kit.placementPrefab = GetOrCreateTemplate();
        }

        /// <summary>
        /// 설치 원본을 만들어(또는 이미 있으면 그대로) 돌려준다.
        /// 비활성으로 만들어 붙인 **다음** 활성화한다 - 활성 오브젝트에 AddComponent를 하면 그 자리에서
        /// Awake가 돌아 아직 원점인 위치에서 지면 스냅이 일어난다(CraftStation.GetOrCreateTemplate와 같은 이유).
        /// </summary>
        private static GameObject GetOrCreateTemplate()
        {
            if (templateRoot == null)
            {
                templateRoot = new GameObject("FarmPlotTemplates");
                templateRoot.transform.position = new Vector3(0f, TemplateParkY, 0f);
                DontDestroyOnLoad(templateRoot);
            }

            const string templateName = "FarmPlotTemplate";
            Transform existing = templateRoot.transform.Find(templateName);
            if (existing != null)
                return existing.gameObject;

            var go = new GameObject(templateName);
            go.SetActive(false);
            go.transform.SetParent(templateRoot.transform, false);
            go.AddComponent<FarmPlot>();
            go.SetActive(true);
            return go;
        }

        // ── 외형 ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// 상호작용/충돌용 몸통 콜라이더를 보장한다(시각 파츠는 콜라이더가 없는 순수 시각 오브젝트라
        /// 이 하나가 밭의 물리적 실체 전부다). 조준 판정도 이 콜라이더로 이뤄진다.
        /// 두둑 자체는 낮지만(0.30m) 콜라이더는 0.6m로 세워 둔다 - 밭 앞에 선 플레이어의 시선(1.6m)이
        /// 4m 밖에서 지면을 겨눴을 때도 걸리게 하기 위한 여유다.
        /// </summary>
        private void EnsureCollider()
        {
            if (GetComponent<Collider>() != null)
                return;

            var box = gameObject.AddComponent<BoxCollider>();
            box.center = new Vector3(0f, 0.30f, 0f);
            box.size = new Vector3(2.00f, 0.60f, 2.00f);
        }

        /// <summary>
        /// 밭의 고정 외형(갈아 놓은 흙 두둑 + 나무 테두리)을 프리미티브로 조립한다.
        /// 신규 모델을 만들지 않는다 - CraftStation.BuildVisual / Smoker.BuildVisual과 같은 방식이다.
        ///
        /// 실루엣 규칙(ArtDirection 2장): 밭은 이 게임에서 **유일하게 지면에 납작한 인공물**이다.
        /// 다른 시설(제작대 0.9 / 용광로 1.4 / 훈연기 1.5)은 전부 서 있는 형태라 높이만으로 갈린다.
        /// 테두리는 각진 사각 재목 + 밧줄 결속(CreateLashedPost)이라 "자연물이 아니라 내가 만든 것"으로 읽힌다.
        /// 이미 자식 파츠가 있으면(설치 원본을 복제한 사본) 다시 만들지 않는다.
        /// </summary>
        private void BuildBedVisual()
        {
            if (bedBuilt || transform.childCount > 0)
            {
                bedBuilt = true;
                CacheExistingCropRoot();
                return;
            }
            bedBuilt = true;

            Color wood = StructureVisualBuilder.Driftwood;

            // 갈아 놓은 흙. 지형(Meadow Green)·모래(IslandSand)와 확실히 갈리는 어두운 갈색이라
            // "여기는 손을 댄 땅이다"가 색만으로도 읽힌다.
            Color soil = new Color(0.32f, 0.24f, 0.17f);
            StructureVisualBuilder.CreateVisualPart(transform, "Soil", PrimitiveType.Cube,
                new Vector3(0f, 0.13f, 0f), new Vector3(1.80f, 0.26f, 1.80f), soil, null, "noise");

            // 두둑 세 줄(고랑). 납작한 밭이 단색 판 하나로 보이지 않게 하는 유일한 형태 신호다.
            for (int i = 0; i < 3; i++)
            {
                float z = -0.56f + i * 0.56f;
                StructureVisualBuilder.CreateVisualPart(transform, "Ridge" + i, PrimitiveType.Cube,
                    new Vector3(0f, 0.29f, z), new Vector3(1.70f, 0.08f, 0.30f),
                    new Color(0.38f, 0.29f, 0.20f), null, "noise");
            }

            // 나무 테두리 네 변. CreateLashedPost는 큐브 기둥 + 밧줄 띠라 눕혀 쓰면 그대로 재목이 된다.
            StructureVisualBuilder.CreateVisualPart(transform, "RailNorth", PrimitiveType.Cube,
                new Vector3(0f, 0.20f, 0.95f), new Vector3(2.00f, 0.16f, 0.10f), wood, null, "wood");
            StructureVisualBuilder.CreateVisualPart(transform, "RailSouth", PrimitiveType.Cube,
                new Vector3(0f, 0.20f, -0.95f), new Vector3(2.00f, 0.16f, 0.10f), wood, null, "wood");
            StructureVisualBuilder.CreateVisualPart(transform, "RailEast", PrimitiveType.Cube,
                new Vector3(0.95f, 0.20f, 0f), new Vector3(0.10f, 0.16f, 2.00f), wood, null, "wood");
            StructureVisualBuilder.CreateVisualPart(transform, "RailWest", PrimitiveType.Cube,
                new Vector3(-0.95f, 0.20f, 0f), new Vector3(0.10f, 0.16f, 2.00f), wood, null, "wood");

            // 네 귀퉁이 말뚝 - 테두리가 "쌓아 둔 나무"가 아니라 "박아 세운 틀"로 읽히게 한다.
            StructureVisualBuilder.CreateLashedPost(transform, "PegNE", new Vector3(0.95f, 0.22f, 0.95f), 0.44f, 0.11f, wood);
            StructureVisualBuilder.CreateLashedPost(transform, "PegNW", new Vector3(-0.95f, 0.22f, 0.95f), 0.44f, 0.11f, wood);
            StructureVisualBuilder.CreateLashedPost(transform, "PegSE", new Vector3(0.95f, 0.22f, -0.95f), 0.44f, 0.11f, wood);
            StructureVisualBuilder.CreateLashedPost(transform, "PegSW", new Vector3(-0.95f, 0.22f, -0.95f), 0.44f, 0.11f, wood);
        }

        /// <summary>
        /// 설치 원본을 복제한 사본은 자식 파츠를 그대로 물려받는다. 그 안에 포기 묶음(CropRoot)이
        /// 섞여 있으면 두 벌이 되므로 참조만 되찾아 둔다(원본은 언제나 빈 밭이라 실제로는 없다).
        /// </summary>
        private void CacheExistingCropRoot()
        {
            if (cropRoot == null)
                cropRoot = transform.Find(CropRootName);
        }

        private const string CropRootName = "CropRoot";

        /// <summary>
        /// 지금 단계에 맞는 포기를 세운다. **단계나 작물이 바뀌었을 때만** 다시 만들므로
        /// 프레임당 할당이 0이다(성장 중에는 한 작물당 네 번만 재조립된다).
        /// </summary>
        private void RefreshCropVisual()
        {
            int stage = GetStage();
            if (stage == builtStage && (!hasCrop || cropKind == builtKind))
                return;

            builtStage = stage;
            builtKind = cropKind;

            // Destroy는 프레임 끝까지 지연되므로 먼저 꺼서 옛 포기와 새 포기가 한 프레임 겹치지 않게
            // 한다(AGENT_BRIEF 4장 "Destroy()는 지연된다 - 즉시 빼려면 SetActive(false)").
            if (cropRoot != null)
            {
                cropRoot.gameObject.SetActive(false);
                Destroy(cropRoot.gameObject);
                cropRoot = null;
            }

            if (stage <= 0 || !TryGetDefinition(cropKind, out CropDefinition def))
                return;

            var rootGo = new GameObject(CropRootName);
            rootGo.transform.SetParent(transform, false);
            rootGo.transform.localPosition = Vector3.zero;
            cropRoot = rootGo.transform;

            BuildPlants(cropRoot, def, stage);
        }

        /// <summary>
        /// 현재 단계. 0 = 빈 밭 · 1 = 씨앗 · 2 = 새싹 · 3 = 성장 · 4 = 수확 가능.
        /// 프롬프트 문구와 외형이 **같은 값**을 보게 하려고 하나로 모아 두었다.
        /// </summary>
        private int GetStage()
        {
            if (!hasCrop)
                return 0;

            float progress = Progress01;
            if (progress >= 1f)
                return 4;
            if (progress >= 0.55f)
                return 3;
            if (progress >= 0.25f)
                return 2;
            return 1;
        }

        /// <summary>
        /// 단계에 따라 포기를 세운다. 씨앗 단계는 흙에 박아 둔 표식(작은 무더기)이고, 그 뒤로는
        /// 같은 형태가 키만 자라다가 마지막에 열매/꽃이 붙는다 - "무엇이 달라졌는지"를 한 눈에
        /// 읽으려면 형태가 계속 바뀌는 것보다 하나가 커지는 편이 낫다.
        /// 머티리얼은 파츠마다 만들지 않고 색이 같은 것끼리 공유한다(AGENT_BRIEF 4장).
        /// </summary>
        private static void BuildPlants(Transform parent, CropDefinition def, int stage)
        {
            int count = Mathf.Clamp(def.PlantCount, 1, PlantOffsets.Length);

            if (stage <= 1)
            {
                // 씨앗: 갓 덮은 흙무더기. 아직 초록이 하나도 없다는 것이 이 단계의 신호다.
                Material moundMaterial = StructureVisualBuilder.CreateColorMaterial(
                    new Color(0.44f, 0.34f, 0.24f), "noise");

                for (int i = 0; i < count; i++)
                {
                    Vector3 offset = PlantOffsets[i];
                    StructureVisualBuilder.CreateVisualPart(parent, "Mound" + i, PrimitiveType.Sphere,
                        new Vector3(offset.x, 0.32f, offset.z), new Vector3(0.22f, 0.10f, 0.22f),
                        moundMaterial);
                }
                return;
            }

            // 단계별 키. 새싹 0.30배 → 성장 0.65배 → 수확 가능 1.0배.
            float heightScale = stage >= 4 ? 1f : (stage == 3 ? 0.65f : 0.30f);
            float height = Mathf.Max(0.08f, def.MatureHeight * heightScale);

            Material stemMaterial = StructureVisualBuilder.CreateColorMaterial(def.FoliageColor, "leaf");
            Material fruitMaterial = stage >= 4
                ? StructureVisualBuilder.CreateColorMaterial(def.FruitColor, "noise")
                : null;

            for (int i = 0; i < count; i++)
            {
                Vector3 offset = PlantOffsets[i];

                // 줄기: 실린더 메시는 높이가 2단위라 실제 높이의 절반을 scale.y에 넣는다
                // (WaterStill의 원기둥 지지대가 겪었던 것과 같은 함정이라 여기 적어 둔다).
                float stemBottom = 0.26f;
                StructureVisualBuilder.CreateVisualPart(parent, "Stem" + i, PrimitiveType.Cylinder,
                    new Vector3(offset.x, stemBottom + height * 0.5f, offset.z),
                    new Vector3(0.09f, height * 0.5f, 0.09f), stemMaterial);

                // 잎: 줄기 꼭대기에 얹은 납작한 갓. 위에서 내려다보는 시점(밭은 발밑에 있다)에서
                // 실제로 보이는 것은 거의 이것뿐이라 줄기보다 넓게 잡았다.
                StructureVisualBuilder.CreateVisualPart(parent, "Leaf" + i, PrimitiveType.Cube,
                    new Vector3(offset.x, stemBottom + height * 0.92f, offset.z),
                    new Vector3(0.34f, 0.05f, 0.34f), stemMaterial);

                if (fruitMaterial != null)
                {
                    StructureVisualBuilder.CreateVisualPart(parent, "Fruit" + i, PrimitiveType.Sphere,
                        new Vector3(offset.x, stemBottom + height * 0.80f, offset.z),
                        new Vector3(0.18f, 0.18f, 0.18f), fruitMaterial);
                }
            }
        }
    }
}
