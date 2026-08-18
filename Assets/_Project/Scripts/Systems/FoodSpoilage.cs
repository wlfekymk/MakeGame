using UnityEngine;
using UnityEngine.SceneManagement;
using MakeGame.Data;
using MakeGame.Player;

namespace MakeGame.Systems
{
    /// <summary>
    /// 음식 하나의 부패 단계. **값은 맨 끝에만 추가한다** - 세이브에는 들어가지 않지만
    /// (저장하는 것은 경과 시간이지 단계가 아니다) UI가 정수로 비교할 수 있어서다.
    /// </summary>
    public enum FoodSpoilStage
    {
        /// <summary>부패 대상이 아니다(재료·음료·비상식량·훈제품).</summary>
        None = 0,
        /// <summary>신선하다. 회복량도 위험도 원래 값 그대로다.</summary>
        Fresh = 1,
        /// <summary>상하기 시작했다. 회복량이 줄고 약한 식중독 위험이 붙는다.</summary>
        Spoiling = 2,
        /// <summary>부패했다. 회복량이 거의 없고 식중독 위험이 크다(먹는 것 자체는 막지 않는다).</summary>
        Rotten = 3
    }

    /// <summary>
    /// 음식의 부패(신선도)를 관리하는 단일 시스템.
    ///
    /// [무엇을 소유하는가]
    ///  · 부패 속도표(어떤 음식이 며칠 만에 상하는가)와 단계 판정 - 전부 이 파일의 static 함수다.
    ///    ConsumptionSystem·InventoryItem·UI가 전부 여기 것을 부르므로 판정이 두 벌로 갈라지지 않는다.
    ///  · 시간 진행 - 인벤토리 안의 음식 나이(InventoryItem.spoilAgeSeconds)를 앞으로 감는 유일한 곳.
    ///
    /// [씬에 인스턴스가 없다] BuildingSystem과 같은 자기 부트스트랩(RuntimeInitializeOnLoadMethod +
    /// sceneLoaded)이라 **이 파일의 코드 기본값이 유일한 진실**이다(AGENT_BRIEF 3장).
    ///
    /// [실시간 기준이다 - 시계 차이가 아니다]
    /// 나이는 Time.deltaTime으로 누적한다. 게임 내 시계(SurvivalClock)는 **단위**로만 쓴다
    /// (1일 = secondsPerDay = 실측 600초). 시계 차이로 재지 않는 이유:
    ///  · 취침(Shelter.TrySleep)은 시계를 아침으로 점프시키면서도 허기/갈증은 전혀 소모시키지 않는다.
    ///    부패만 시계를 따라가면 "자고 일어나면 배는 안 고픈데 식량고가 썩어 있다"가 되어, 압박을
    ///    리듬이 아니라 처벌로 만든다(AGENT_BRIEF ★현재 방향).
    ///  · 불러오기는 시계를 저장 시점으로 되돌린다. 시계 차이를 쓰면 그 순간 음이 되거나(무시) 이후
    ///    재계산이 어긋난다. 나이는 절대값이라 그런 경로가 아예 없다.
    /// 결과적으로 **게임을 꺼 둔 시간(오프라인)에는 상하지 않는다.** 이 프로젝트의 다른 시간 기반
    /// 시스템(모닥불 연료·증류기)이 전부 그렇고, "껐다 켜니 식량이 전멸했다"는 되돌릴 방법이 없는
    /// 손실이라 보수적인 쪽을 골랐다.
    /// </summary>
    public class FoodSpoilage : MonoBehaviour
    {
        // ── 속도표 (코드 기본값이 유일한 소스) ────────────────────────────────────────────
        //
        // 1일 = 600초(실시간 10분)다. 표는 ItemData.spoilDays가 비어 있는(0) 아이템에만 적용되며,
        // game-designer가 .asset에 값을 채우면 그쪽이 이긴다.
        //
        //  생고기 / 생선            → 1일  (600초)  가장 빠르다. "구워서 쟁여 둬라"가 이 표의 전부다.
        //  구운고기 / 구운생선 / 해조류 → 3일 (1800초)
        //  비상식량 / 생수 / 코코넛 / 훈제육 / 훈제생선 → 부패 없음
        // 음료(허기 회복 0)와 재료·키트·도구는 애초에 대상이 아니다.

        /// <summary>생음식(ItemData.isRawFood)이 완전히 부패하기까지의 게임 내 일수.</summary>
        public const float RawFoodSpoilDays = 1f;

        /// <summary>익힌 음식이 완전히 부패하기까지의 게임 내 일수.</summary>
        public const float CookedFoodSpoilDays = 3f;

        /// <summary>SurvivalClock을 찾지 못했을 때 쓰는 하루 길이(초). 씬 실측값과 같다.</summary>
        public const float FallbackSecondsPerDay = 600f;

        /// <summary>이 신선도 아래로 내려가면 "상하기 시작"이다(남은 신선도 기준).</summary>
        public const float SpoilingThreshold01 = 0.5f;

        /// <summary>이 신선도 아래로 내려가면 "부패"다.</summary>
        public const float RottenThreshold01 = 0.15f;

        /// <summary>"상하기 시작" 단계의 허기 회복 배율.</summary>
        public const float SpoilingRestoreMultiplier = 0.6f;

        /// <summary>"부패" 단계의 허기 회복 배율. 0으로 두지 않는 이유는 굶어 죽기 직전의 마지막 수단을 남기기 위해서다.</summary>
        public const float RottenRestoreMultiplier = 0.25f;

        /// <summary>"상하기 시작"한 음식을 먹었을 때 **더해지는** 식중독 확률.</summary>
        public const float SpoilingExtraPoisonChance = 0.10f;

        /// <summary>"부패"한 음식을 먹었을 때 **더해지는** 식중독 확률.</summary>
        public const float RottenExtraPoisonChance = 0.50f;

        // ── 이름 규약 ─────────────────────────────────────────────────────────────────────
        //
        // ItemData에 "절대 안 상함" 전용 bool을 새로 만들지 않는 이유: 기존 32개 .asset에는 그 키가
        // 없어 전부 false로 읽히므로, 비상식량이 상하기 시작한다. 이름 대조는 이 프로젝트가 이미
        // 쓰는 방식이고(CraftStation.WorkbenchKitItemName / CombatSystem.RefinedPrefix), .asset을
        // 한 글자도 건드리지 않고 예외를 못 박을 수 있는 유일한 수단이다.
        // 명시적으로 못 박고 싶으면 game-designer가 ItemData.spoilDays = -1을 넣으면 된다(그쪽이 이긴다).

        /// <summary>절대 상하지 않는 비상식량의 itemName(Item_비상식량.asset 실측값).</summary>
        public const string EmergencyRationItemName = "비상식량";

        /// <summary>훈연기 결과물의 이름 접두어. "훈제육"/"훈제생선"이 전부 여기에 걸린다.</summary>
        public const string SmokedItemNamePrefix = "훈제";

        // ── 인스턴스 설정 ─────────────────────────────────────────────────────────────────

        [Tooltip("부패 진행을 통째로 끄는 안전장치. 밸런스가 무너지면 이 값 하나로 예전 동작(절대 안 상함)으로 되돌아간다.")]
        public bool spoilageEnabled = true;

        [Tooltip("부패 속도 배율. 1이면 위 속도표 그대로, 0.5면 두 배 오래 간다. 0 이하이면 진행하지 않는다.")]
        public float spoilRateMultiplier = 1f;

        [Tooltip("인벤토리를 훑어 나이를 더하는 주기(초). 매 프레임 돌 이유가 없어 묶어서 처리한다.")]
        public float tickInterval = 1f;

        /// <summary>이 씬의 부패 시스템. 없을 수도 있으므로 호출부는 항상 null을 확인한다.</summary>
        public static FoodSpoilage Instance { get; private set; }

        private static SurvivalClock cachedClock;
        private static PlayerInventory cachedInventory;

        private float tickTimer;

        // ── 부트스트랩 ────────────────────────────────────────────────────────────────────

        /// <summary>
        /// [R1 규약] 도메인 리로드를 끈 플레이 모드에서 이전 세션의 정적 상태가 새지 않게 비운다.
        /// 파괴된 컴포넌트를 가리키는 캐시가 그대로 남아 있으면 이후 조회가 전부 "있는데 죽은" 참조가 된다.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticCache()
        {
            Instance = null;
            cachedClock = null;
            cachedInventory = null;
        }

        /// <summary>
        /// 씬 로드마다 스스로 생긴다(BuildingSystem.Bootstrap과 완전히 같은 방식 - 씬에 컴포넌트를
        /// 추가하려면 .unity 편집이 필요한데 그것은 디렉터만 할 수 있다).
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Bootstrap()
        {
            SceneManager.sceneLoaded += (scene, mode) =>
            {
                var go = new GameObject("FoodSpoilage");
                go.AddComponent<FoodSpoilage>();
            };
        }

        /// <summary>
        /// 중복 생성을 막는다. 씬 로드 훅이 도메인 리로드로 두 번 붙는 경우에도 실제로 도는 인스턴스는
        /// 하나뿐이어야 한다 - 둘이 살아 있으면 나이가 두 배 속도로 늘어난다.
        /// </summary>
        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        /// <summary>
        /// 주기마다 인벤토리 안의 음식 나이를 실시간 경과만큼 앞으로 감는다.
        /// Time.timeScale이 0인 동안(엔딩/사망 화면)에는 deltaTime이 0이라 자동으로 멈춘다.
        /// </summary>
        private void Update()
        {
            if (!spoilageEnabled || spoilRateMultiplier <= 0f)
                return;

            tickTimer += Time.deltaTime;

            float interval = tickInterval > 0f ? tickInterval : 1f;
            if (tickTimer < interval)
                return;

            float elapsed = tickTimer;
            tickTimer = 0f;

            AdvanceInventory(elapsed * spoilRateMultiplier);
        }

        /// <summary>
        /// 플레이어 인벤토리의 모든 음식 나이를 seconds만큼 더한다.
        /// 나이는 그 음식의 수명에서 멈춘다(계속 커져도 신선도는 이미 0이라 의미가 없고, 세이브에
        /// 끝없이 커지는 수가 들어가는 것을 막는다).
        /// </summary>
        private void AdvanceInventory(float seconds)
        {
            if (seconds <= 0f)
                return;

            PlayerInventory inventory = ResolveInventory();
            if (inventory == null)
                return;

            var items = inventory.items;
            for (int i = 0; i < items.Count; i++)
            {
                InventoryItem item = items[i];
                if (item == null)
                    continue;

                float lifetime = GetSpoilLifetimeSeconds(item.data);
                if (lifetime <= 0f)
                    continue;

                item.spoilAgeSeconds = Mathf.Min(item.spoilAgeSeconds + seconds, lifetime);
            }
        }

        /// <summary>플레이어 인벤토리를 한 번만 찾아 캐시한다(파괴되면 다시 찾는다).</summary>
        private static PlayerInventory ResolveInventory()
        {
            if (cachedInventory == null)
                cachedInventory = FindAnyObjectByType<PlayerInventory>();

            return cachedInventory;
        }

        /// <summary>게임 내 시계를 한 번만 찾아 캐시한다(Shelter.ResolveClock과 같은 방식).</summary>
        private static SurvivalClock ResolveClock()
        {
            if (cachedClock == null)
                cachedClock = FindAnyObjectByType<SurvivalClock>();

            return cachedClock;
        }

        /// <summary>
        /// 하루의 길이(초). 시계가 없거나 값이 망가져 있으면 실측 기본값(600)을 쓴다 -
        /// 이 값은 나눗셈 분모라 0이 들어오면 신선도가 전부 NaN이 된다.
        /// </summary>
        public static float SecondsPerDay
        {
            get
            {
                SurvivalClock clock = ResolveClock();
                if (clock != null && clock.secondsPerDay > 0f)
                    return clock.secondsPerDay;

                return FallbackSecondsPerDay;
            }
        }

        // ── 속도표 판정 (단일 소스) ───────────────────────────────────────────────────────

        /// <summary>
        /// 이 아이템이 완전히 부패하기까지의 게임 내 일수. 0이면 **절대 상하지 않는다**.
        ///
        /// 우선순위:
        ///  1. ItemData.spoilDays &lt; 0  → 부패 없음(명시적 표시)
        ///  2. ItemData.spoilDays &gt; 0  → 그 값 그대로
        ///  3. 그 외(0 = 미설정)          → 아래 자동 규칙
        ///     · 허기를 회복시키지 않는 것(재료·도구·키트·물/코코넛 같은 음료) → 부패 없음
        ///       (사용자 규칙: 비상식량·생수는 절대 안 상한다)
        ///     · 비상식량 / 이름이 "훈제"로 시작하는 것 → 부패 없음
        ///     · 생음식(isRawFood) → 1일
        ///     · 나머지 음식 → 3일
        /// </summary>
        public static float GetSpoilDays(ItemData data)
        {
            if (data == null)
                return 0f;

            if (data.spoilDays < 0f)
                return 0f;

            if (data.spoilDays > 0f)
                return data.spoilDays;

            if (data.hungerRestoreAmount <= 0f)
                return 0f;

            if (data.itemName == EmergencyRationItemName)
                return 0f;

            if (!string.IsNullOrEmpty(data.itemName) && data.itemName.StartsWith(SmokedItemNamePrefix))
                return 0f;

            return data.isRawFood ? RawFoodSpoilDays : CookedFoodSpoilDays;
        }

        /// <summary>이 아이템이 완전히 부패하기까지의 실제 초. 0 이하이면 부패 대상이 아니다.</summary>
        public static float GetSpoilLifetimeSeconds(ItemData data)
        {
            float days = GetSpoilDays(data);
            if (days <= 0f)
                return 0f;

            return days * SecondsPerDay;
        }

        /// <summary>이 아이템이 부패 대상인지 여부.</summary>
        public static bool CanSpoil(ItemData data)
        {
            return GetSpoilLifetimeSeconds(data) > 0f;
        }

        /// <summary>
        /// 남은 신선도(1 = 갓 만든 것, 0 = 완전 부패). **부패 대상이 아니면 항상 1이다** -
        /// UI가 종류를 가리지 않고 이 값 하나로 게이지를 그릴 수 있게 하기 위한 것이다.
        /// </summary>
        public static float GetFreshness01(InventoryItem item)
        {
            if (item == null)
                return 1f;

            float lifetime = GetSpoilLifetimeSeconds(item.data);
            if (lifetime <= 0f)
                return 1f;

            return Mathf.Clamp01(1f - (item.spoilAgeSeconds / lifetime));
        }

        /// <summary>이 인스턴스의 부패 단계.</summary>
        public static FoodSpoilStage GetStage(InventoryItem item)
        {
            if (item == null || !CanSpoil(item.data))
                return FoodSpoilStage.None;

            return GetStageForFreshness(GetFreshness01(item));
        }

        /// <summary>
        /// 신선도 값 하나를 단계로 바꾼다. UI가 이미 신선도를 들고 있을 때(스택 뷰 등) 쓰라고 열어 둔다.
        /// **부패 대상 여부는 판정하지 않는다** - 대상이 아닌 아이템의 신선도는 항상 1이라 Fresh가 나온다.
        /// </summary>
        public static FoodSpoilStage GetStageForFreshness(float freshness01)
        {
            if (freshness01 < RottenThreshold01)
                return FoodSpoilStage.Rotten;

            if (freshness01 < SpoilingThreshold01)
                return FoodSpoilStage.Spoiling;

            return FoodSpoilStage.Fresh;
        }

        /// <summary>단계별 허기 회복 배율(갈증 회복에는 적용하지 않는다 - 음료는 부패 대상이 아니다).</summary>
        public static float GetRestoreMultiplier(FoodSpoilStage stage)
        {
            switch (stage)
            {
                case FoodSpoilStage.Spoiling: return SpoilingRestoreMultiplier;
                case FoodSpoilStage.Rotten: return RottenRestoreMultiplier;
                default: return 1f;
            }
        }

        /// <summary>
        /// 단계별로 **더해지는** 식중독 확률. 익힌 음식이라도 부패하면 위험해진다는 뜻이며,
        /// 생음식의 기존 확률(ConsumptionSystem.rawFoodPoisonChance)에 더한 뒤 0~1로 자른다.
        /// </summary>
        public static float GetExtraPoisonChance(FoodSpoilStage stage)
        {
            switch (stage)
            {
                case FoodSpoilStage.Spoiling: return SpoilingExtraPoisonChance;
                case FoodSpoilStage.Rotten: return RottenExtraPoisonChance;
                default: return 0f;
            }
        }

        /// <summary>화면에 그대로 쓸 수 있는 단계 문구. 부패 대상이 아니면 빈 문자열이다.</summary>
        public static string GetStageLabel(FoodSpoilStage stage)
        {
            switch (stage)
            {
                case FoodSpoilStage.Fresh: return "신선";
                case FoodSpoilStage.Spoiling: return "상하기 시작";
                case FoodSpoilStage.Rotten: return "부패";
                default: return "";
            }
        }
    }
}
