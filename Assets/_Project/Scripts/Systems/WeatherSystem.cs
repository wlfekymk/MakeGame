using UnityEngine;
using UnityEngine.SceneManagement;
using MakeGame.Data;

namespace MakeGame.Systems
{
    /// <summary>
    /// 맑음/비를 오가는 간단한 날씨 연출을 담당한다. 비가 오는 동안은 플레이어 머리 위에서 비 파티클이
    /// 내리고, 빗소리 배경음이 재생되며, DayNightCycle이 계산하는 태양광 밝기를 이 클래스의
    /// IsRaining/rainDimFactor 값을 참고해 추가로 어둡게 만든다(직접 sunLight.intensity를 건드리지
    /// 않는 이유는 DayNightCycle.Update()와 같은 필드를 서로 다른 스크립트가 매 프레임 덮어쓰면
    /// 실행 순서에 따라 값이 튀거나 서로 상쇄될 수 있기 때문 - 대신 DayNightCycle이 "비가 오는지"를
    /// 읽어서 자기 계산 결과에 곱하는 단방향 의존 구조로 설계했다).
    /// DayNightCycle과 동일하게 SubsystemRegistration + SceneManager.sceneLoaded 패턴으로
    /// 재시작 시에도 매번 새로 생성된다.
    ///
    /// B4-1 (Spec_15 3단계 배선): SurvivalBalanceConfig를 선택적(nullable) 참조로 받는다.
    /// 폴백으로 읽는 config 필드 — minClearSeconds ← weatherMinClearSeconds,
    /// maxClearSeconds ← weatherMaxClearSeconds, minRainSeconds ← weatherMinRainSeconds,
    /// maxRainSeconds ← weatherMaxRainSeconds, rainDimFactor ← rainDimFactor.
    /// 폴백은 해당 필드가 0 이하(미설정)일 때만 적용되므로 인스펙터 값이 항상 이긴다.
    /// [B5 정정 - 이전 주석이 사실과 달랐다(qa-reviewer 지적)] 여기에는 "Bootstrap()이 런타임에
    /// new GameObject로 생성하므로 balanceConfig는 항상 null이고, 따라서 폴백은 씬에 직접 배치한
    /// 경우에만 의미가 있다"고 적혀 있었으나, 그 뒤 B4-2에서 ApplyBalanceConfigFallback에
    /// `balanceConfig ??= SurvivalBalanceConfig.Active`(Resources 자동 로드) 경로가 추가되어 더 이상
    /// 사실이 아니다. 런타임 생성 인스턴스도 Resources에 공용 에셋만 있으면 config를 확보하므로,
    /// 폴백은 항상 살아 있는 경로다. 즉 아래 필드의 코드 기본값을 0 이하로 바꾸면 그 순간부터
    /// config 값이 실제 게임 동작을 지배한다 - "어차피 null이니 상관없다"고 판단하면 안 된다.
    /// </summary>
    public class WeatherSystem : MonoBehaviour
    {
        [Header("밸런스 config (선택, B4-1)")]
        [Tooltip("연결하면, 아래 날씨 타이머/우천 감광 계수가 0 이하로(미설정) 남아있는 경우에 한해" +
            " config의 weather*Seconds / rainDimFactor 값을 대신 쓴다. 인스펙터에 이미 의미 있는" +
            "(양수) 값이 들어 있으면 절대 덮어쓰지 않는다.")]
        public SurvivalBalanceConfig balanceConfig;

        [Tooltip("맑은 날씨가 지속되는 최소 시간(초, 실시간)")]
        public float minClearSeconds = 90f;

        [Tooltip("맑은 날씨가 지속되는 최대 시간(초, 실시간)")]
        public float maxClearSeconds = 240f;

        [Tooltip("비가 지속되는 최소 시간(초, 실시간)")]
        public float minRainSeconds = 40f;

        [Tooltip("비가 지속되는 최대 시간(초, 실시간)")]
        public float maxRainSeconds = 100f;

        [Tooltip("비가 올 때 태양광 밝기에 곱할 배율(0~1). 낮을수록 더 어두워진다")]
        [Range(0f, 1f)]
        public float rainDimFactor = 0.55f;

        [Tooltip("비 파티클을 플레이어(카메라) 머리 위 얼마나 높은 곳에 띄울지")]
        public float rainHeightAboveTarget = 15f;

        // ── [B22] 비의 존재감 ────────────────────────────────────────────────────
        [Header("비 연출 (B22 — 게임플레이 수치와 무관)")]
        [Tooltip("비가 시작/종료될 때 세기가 0↔1로 오르내리는 데 걸리는 시간(초, 실시간).\n" +
            "0이면 예전처럼 한 프레임에 딱 켜지고 꺼진다.")]
        public float rainFadeSeconds = 5f;

        [Tooltip("최대 세기일 때의 빗줄기 초당 방출 개수.\n" +
            "[B29] 충돌(collision)을 켜면서 700 → 460으로 내렸다. 빗방울은 에미터(머리 위 15m)에서" +
            " 약 0.94초 만에 지면에 닿아 사라지므로(2t + 14.7t² = 15) 동시 생존은 약 430개이고," +
            " 그만큼의 스윕 레이캐스트가 매 프레임 돈다. 700이면 650개가 되어 섬 9개가 깔린 씬에서" +
            " 이득 없이 비싸다(maxParticles 900 이내로 유지할 것).")]
        public float rainEmissionRate = 460f;

        [Tooltip("빗줄기가 비스듬히 내리게 만드는 수평 바람 속도(m/s, 월드 XZ). Stretched 빌보드가" +
            " 속도 방향으로 늘어나므로 이 값이 곧 빗줄기의 기울기가 된다.")]
        public Vector2 rainWind = new Vector2(2.6f, 1.4f);

        [Tooltip("빗방울이 땅/수면에 부딪히는 물튀김 파티클을 켤지 여부.")]
        public bool enableRainSplashes = true;

        // ── [B33] 빗줄기 텍스처 + 근/원 2겹 ─────────────────────────────────────
        // (a) Resources/Textures/rain_streak.png를 빗줄기 머티리얼에 얹는다. 이 텍스처는 512²
        //     **시트**다 — 세로로 무이음이고 옅은 청백 줄기 약 20가닥이 가로로 흩어져 있다.
        //     한 파티클이 시트 전체를 보면 거의 투명한 얼룩이 되므로, textureSheetAnimation으로
        //     가로 16칸(32px)으로 잘라 파티클마다 **한 칸(=줄기 하나)** 을 고정으로 물린다.
        //     16칸 각각의 알파 최대값이 83~203(실측)이라 빈 칸이 하나도 없다 — 어떤 파티클도
        //     투명하게 사라지지 않는다.
        // (b) 텍스처 알파가 시트 평균 기준으로 옅기 때문에, 텍스처를 적용할 때만 startColor를
        //     흰색·알파 0.95로 올리고 폭을 넓힌다(색조는 텍스처의 청백이 낸다 — 기존 startColor
        //     색을 그대로 곱하면 두 번 어두워진다). 텍스처가 없으면 전부 예전 값 그대로다.
        // (c) 근경 레이어를 한 겹 더 얹어 깊이감을 만든다. 예산은 **maxParticles 합계 900 유지** —
        //     원경 700 + 근경 200. 원경 실제 동시 생존이 약 430개라(rainEmissionRate 주석) 700도
        //     1.6배 여유가 있고, 남긴 200을 근경에 넘겨 총량을 늘리지 않았다.
        [Header("빗줄기 텍스처 · 근경 레이어 (B33 — 연출만)")]
        [Tooltip("빗줄기 시트를 가로로 몇 칸으로 자를지. 파티클마다 한 칸(줄기 하나)을 무작위로 물린다.")]
        public int rainStreakTilesX = 16;

        [Tooltip("근경 빗줄기 레이어를 켤지 여부. 끄면 예전처럼 원경 한 겹만 내린다.")]
        public bool enableNearRainLayer = true;

        [Tooltip("근경 빗줄기 에미터를 카메라 머리 위 얼마나 높은 곳에 둘지(m). 원경(15m)보다 낮아야" +
            " 화면을 빠르게 스쳐 지나가며 시차가 생긴다.")]
        public float nearRainHeightAboveTarget = 5.5f;

        [Tooltip("최대 강우일 때 근경 레이어의 초당 방출 개수. 수명 0.8초라 동시 생존은 약 75개다.")]
        public float nearRainEmissionRate = 95f;

        [Tooltip("근경 빗줄기의 알파(0~1). 카메라 코앞을 지나므로 원경보다 옅어야 시야를 가리지 않는다.")]
        [Range(0f, 1f)]
        public float nearRainAlpha = 0.4f;

        // ── [B29] "비가 2층 바닥을 뚫고 실내에 내린다" 수정 ──────────────────────
        // 감독 실기 보고. 원인은 단순하다 - 빗줄기는 플레이어 머리 위 15m에 떠 있는 Box 에미터에서
        // 쏟아지는데 충돌 모듈이 없어서 무엇이든 그대로 통과했다. 두 층위로 고친다.
        //  (a) 실내에서는 방출량·물튀김을 0으로 줄인다(주 수정). 플레이어가 지붕 아래에 있으면
        //      "빗줄기가 지붕에 막혀 안 보인다"가 아니라 아예 만들지 않는 것이 가장 싸고 확실하다.
        //  (b) 파티클 충돌을 켠다(보조). 밖에서 자기 집을 볼 때 지붕 위에 비가 부딪혀 멈춰야
        //      건물이 비를 막고 있다는 것이 읽힌다.
        // **광량·안개는 실내에서도 그대로 둔다.** 창밖은 여전히 흐린 것이 맞고, 광량/안개는
        // DayNightCycle이 RainIntensity01을 읽어 몰고 있어서 여기서 손대면 그쪽과 싸운다.
        [Header("실내 차단 (B29 — 연출만. 게임플레이 수치는 IsRaining 그대로다)")]
        [Tooltip("실내(지붕 덮인 건축물 안)에서 빗줄기·물튀김을 끌지 여부. 끄면 예전처럼 관통한다.")]
        public bool stopRainIndoors = true;

        [Tooltip("쉼터(Shelter)의 홈 반경도 실내로 칠지 여부(Lv1 0m / Lv2 8m / Lv3 14m).\n" +
            "이 반경은 형상이 아니라 **거리** 판정이라, 켜 두면 쉼터 바로 옆 '바깥'에서도 비가 멎는다." +
            " 건축물 실내 판정(BuildingSystem)은 이 토글과 무관하게 항상 본다.")]
        public bool shelterRadiusCountsAsIndoors = true;

        [Tooltip("실내 여부를 다시 재는 주기(초, 실시간). 걷는 속도에서 이 정도면 충분해 매 프레임 부르지 않는다.")]
        public float indoorCheckInterval = 0.25f;

        [Tooltip("실내↔실외가 뒤집힐 때 빗줄기·물튀김이 0↔1로 오가는 데 걸리는 시간(초, 실시간).\n" +
            "문턱에 서 있을 때 판정이 오가도 깜빡이지 않게 하는 것이 목적이다.")]
        public float indoorFadeSeconds = 0.8f;

        [Tooltip("빗줄기가 지붕·지형에 부딪혀 사라지게 할지 여부(파티클 collision 모듈).")]
        public bool enableRainCollision = true;

        /// <summary>
        /// 현재 비가 오고 있는지 여부(단계 전환 즉시 뒤집히는 논리값).
        /// **게임플레이 효과(증류기/모닥불)는 지금도 오직 이 값만 본다** — 아래 RainIntensity01은
        /// 순수 연출용이며 우천의 게임 수치에는 한 톨도 관여하지 않는다.
        /// </summary>
        public bool IsRaining { get; private set; }

        /// <summary>
        /// [B22] 비 연출의 세기(0=완전 맑음, 1=최대 강우). IsRaining이 뒤집힌 뒤 rainFadeSeconds에 걸쳐
        /// 서서히 따라간다. DayNightCycle이 광량/안개 보간에, 이 클래스가 파티클 방출량에 쓴다.
        /// Time.timeScale이 0이 되는 순간(엔딩/사망)에도 연출이 멈추지 않도록 unscaledDeltaTime으로 진행한다.
        /// </summary>
        public float RainIntensity01 { get; private set; }

        // ── 바다 거칠기 (OceanWaves 연동) ────────────────────────────────────────
        // 이 시스템의 논리 상태는 예전 그대로 **맑음/비 두 개뿐**이다. 폭풍 상태를 새로 만들지 않았다 —
        // 상태를 늘리면 전환 확률/지속시간 표(minClear~maxRain)와 그것을 읽는 balanceConfig 폴백,
        // 그리고 DayNightCycle의 IsRaining/RainIntensity01 계약까지 함께 흔들리기 때문이다.
        // 대신 "거친 바다"를 아래 세 구간의 **연속값 하나**로 유도한다(전환 확률·세이브 포맷 불변, rng 소비 0):
        //   맑음(잔잔)  : 맑음 단계 전반           → 0
        //   흐림(보통)  : 맑음 단계 끝 preStormSeconds → 0에서 preStormRoughness까지 상승
        //   비(거칠다)  : 비 단계                  → preStormRoughness에서 1까지 상승
        // "흐림"은 비가 오기 직전 예고 구간을 그대로 쓴 것이라 phaseTimer/phaseDuration만 읽고,
        // 단계 길이도 난수도 전혀 건드리지 않는다.

        [Header("바다 거칠기 (OceanWaves가 읽는다)")]
        [Tooltip("비가 오기 전 '흐림' 예고 구간의 길이(초). 이 구간 동안 바다가 서서히 거칠어진다.")]
        public float preStormSeconds = 35f;

        [Tooltip("'흐림'(비 직전) 구간에서 도달하는 거칠기 0~1. 비가 시작되면 여기서 1까지 이어서 오른다.")]
        public float preStormRoughness = 0.45f;

        [Tooltip("거칠기 변화의 최소 소요 시간(초). 급변을 막는 안전 보간이다(0이면 즉시 반영).")]
        public float seaRoughnessFadeSeconds = 8f;

        /// <summary>
        /// 바다 거칠기 0~1(0 = 맑고 잔잔, 1 = 비/폭풍으로 거칠다). OceanWaves가 이 값 하나로
        /// 파도 진폭·속도를 보간한다. **게임플레이 수치에는 전혀 관여하지 않는다**(RainIntensity01과 같은 위치).
        /// </summary>
        public float SeaRoughness01 { get; private set; }

        [Header("퀄리티 개선: 비 오는 동안의 안개")]
        [Tooltip("비가 올 때 켤 안개 색(축축하고 뿌연 느낌)")]
        public Color rainFogColor = new Color(0.55f, 0.6f, 0.65f, 1f);

        // [B22] 안개 모드가 Exponential → ExponentialSquared로 바뀌었다(DayNightCycle 주석 참고).
        // 같은 밀도라도 exp2는 먼 거리에서 훨씬 급격히 짙어지므로 0.012를 그대로 두면 200m 앞이
        // 통째로 벽이 된다(exp(-(0.012·200)²) = 0.3% 잔여). 0.006이면 100m 70% · 200m 24% · 300m 4%로,
        // "비 오는 날 시야가 좁아진다"는 느낌은 그대로면서 길을 잃을 정도는 아니다.
        [Tooltip("비가 올 때 안개 밀도(ExponentialSquared). 너무 높으면 시야가 답답해지므로 낮게 유지")]
        public float rainFogDensity = 0.006f;

        // ── [B9] 우천의 게임플레이 효과 ──────────────────────────────────────────
        // 문제: 비는 실시간의 약 30%를 차지하는데(평균 주기 165초 맑음 + 70초 비 = 29.8%,
        // Docs/Design_MidGameContent.md 1-2장) IsRaining을 읽는 곳이 광량/안개/빗소리뿐이라
        // 게임 상태에 아무 영향이 없었다. 90~240초마다 오는 이벤트가 순수 장식이었다.
        //
        // 설계는 같은 문서 4장 "안 3"을 그대로 따른다(효과 3개 중 2개를 여기서 구현):
        //   (1) 증류기 생산 3배 — 비 = 물 수급 기회. 우천 중 원정/제작을 나갈 이유가 생긴다.
        //   (2) 모닥불 연료 소모 1.5배 — 비 = 불을 유지하기 나쁜 시간. 야간 사냥(안 2)과 정면 충돌시켜
        //       "비 오는 밤은 사냥하기 나쁜 밤"이라는 판단을 매일 밤 다시 하게 만든다.
        //   (3) 일사병 정지 — SurvivalStats/SurvivalTickDriver 소유가 아니라 여기서 못 한다. [요청]으로 올렸다.
        //
        // 구현 방식이 "필드를 곱하지 않고 Tick을 한 번 더 부르는 것"인 이유:
        // WaterStill.waterPerSecond나 Campfire.secondsPerFuel을 직접 곱해두고 비가 그칠 때 되돌리는
        // 방식은 이 프로젝트가 반복해서 사고를 낸 형태다 — 되돌리기 전에 씬 저장/재로드/파괴가 끼면
        // 곱해진 값이 그대로 굳는다(AGENT_BRIEF 0장의 "코드 값과 직렬화 값이 갈라진다"). 대신
        // 두 컴포넌트의 공개 API인 Tick(float)을 배수만큼의 추가 델타로 한 번 더 호출한다.
        // 두 Tick 모두 델타에 선형인 순수 누적/차감이고(WaterStill.cs:150, Campfire.cs:92),
        // 각자 자기 Update에서 이미 1배로 돌고 있으므로 여기서 (배수-1)배를 더하면 정확히 배수가 된다.
        // 설정 필드는 하나도 건드리지 않으므로 비가 그치면 되돌릴 상태 자체가 없다.
        [Header("우천 게임플레이 효과 (B9)")]
        [Tooltip("비가 오는 동안 물 증류기(WaterStill) 생산 속도에 곱할 배율. 1이면 효과 없음")]
        public float rainWaterStillMultiplier = 3f;

        [Tooltip("비가 오는 동안 모닥불(Campfire) 연료 소모 속도에 곱할 배율. 1이면 효과 없음")]
        public float rainCampfireFuelMultiplier = 1.5f;

        [Tooltip("우천 효과 대상(증류기/모닥불) 목록을 다시 훑는 주기(초). 플레이어가 비 도중에 새로" +
            " 설치한 구조물을 잡아내기 위한 것이라 짧을 필요가 없다")]
        public float rainTargetRescanSeconds = 3f;

        /// <summary>
        /// 현재 살아 있는 WeatherSystem 인스턴스. Bootstrap이 런타임에 만들기 때문에 인스펙터로
        /// 참조를 연결할 수단이 없어서, 다른 시스템이 IsRaining을 읽으려면 매번 FindAnyObjectByType을
        /// 해야 했다. 이 정적 참조는 그 비용을 없애기 위한 것이다(AudioManager.Instance와 같은 패턴).
        /// 씬이 다시 로드되면 새 인스턴스의 Awake가 덮어쓴다.
        /// </summary>
        public static WeatherSystem Active { get; private set; }

        // 우천 효과 대상 캐시. 매 프레임 FindObjectsByType을 돌리면 비 오는 30% 구간 내내 GC/스캔
        // 비용이 붙으므로 rainTargetRescanSeconds 주기로만 갱신한다.
        private WaterStill[] rainWaterStills = System.Array.Empty<WaterStill>();
        private Campfire[] rainCampfires = System.Array.Empty<Campfire>();
        private float rainRescanTimer;

        private float phaseTimer;
        private float phaseDuration;
        private ParticleSystem rainParticles;
        private ParticleSystem nearRainParticles;
        private ParticleSystem rainSplashes;
        private Transform followTarget;

        /// <summary>
        /// [B33] 원경/근경 빗줄기가 **공유하는** 머티리얼 1장. 두 시스템이 같은 머티리얼을 쓰면
        /// Unity가 파티클 배치를 합칠 여지가 생기고(최악의 경우에도 드로우콜 +1로 끝난다),
        /// 무엇보다 텍스처/셰이더 설정을 한 곳에서만 관리하면 된다.
        /// renderer.material(자동 인스턴스화)이 아니라 sharedMaterial로 꽂고 여기서 소유·파괴한다 —
        /// 예전 코드는 renderer.material에 new Material을 넣어 인스턴스가 두 겹 생기고 있었다.
        /// </summary>
        private Material rainMaterial;

        /// <summary>[B33] 빗줄기 텍스처를 이미 머티리얼에 얹었는지. 실패는 래치하지 않고 다시 시도한다.</summary>
        private bool rainStreakApplied;

        // 물튀김 파티클을 놓을 지면 높이. 매 프레임 레이캐스트하면 낭비라 주기적으로만 갱신한다.
        private float splashGroundY;
        private float splashProbeTimer;

        // [B29] 실내 차단 상태. shelteredFactor는 1=실외(비 그대로) / 0=실내(빗줄기·물튀김 정지)이며,
        // RainIntensity01과 **곱해서만** 쓴다 - 광량/안개를 몰고 있는 RainIntensity01 자체는 건드리지 않는다.
        private bool indoorNow;
        private float indoorCheckTimer;
        private float shelteredFactor = 1f;

        /// <summary>
        /// [B33] 실외 계수 0~1(1 = 실외로 비를 그대로 맞는다, 0 = 실내라 비가 닿지 않는다).
        /// 빗줄기·물튀김에 곱하는 것과 **완전히 같은 값**이라, 이 값을 읽는 다른 연출(StormEffects의
        /// 화면 물방울)은 판정을 새로 짜지 않고도 빗줄기와 정확히 같은 타이밍에 켜지고 꺼진다.
        /// 게임플레이 수치에는 관여하지 않는다(IsRaining이 여전히 단독 소유자다).
        /// </summary>
        public float ShelteredFactor01 => shelteredFactor;

        /// <summary>[B33] 현재 실내(지붕 덮인 건축물/쉼터 반경 안)로 판정됐는지. 연출 전용 읽기값.</summary>
        public bool IsIndoors => indoorNow;

        /// <summary>[B29] 빗줄기가 부딪힐 레이어(Default=0만). 근거는 BuildRainParticles의 collision 주석.</summary>
        private const int RainCollisionMask = 1 << 0;

        /// <summary>물튀김 지면 높이를 다시 재는 주기(초). 걸어다니는 속도에서 이 정도면 충분히 따라온다.</summary>
        private const float SplashProbeInterval = 0.3f;

        // 맑은 날씨로 돌아왔을 때 원래 안개 설정을 복원하기 위한 캐시.
        private bool originalFogEnabled;
        private Color originalFogColor;
        private float originalFogDensity;

        // 맑은 날의 대기 안개(하늘색과 같은 색의 옅은 거리 안개)는 DayNightCycle이 매 프레임 시간대에
        // 맞춰 직접 관리한다. 그쪽이 살아 있는데 여기서 "원래 설정"으로 되돌리면 비가 그치는 순간
        // 한 프레임 동안 안개가 툭 꺼졌다 다시 켜지므로, 그럴 때는 복원을 건너뛰고 넘겨준다.
        private DayNightCycle dayNight;

        /// <summary>
        /// 씬이 로드될 때마다(최초 시작이든 재시작이든) 새 WeatherSystem을 생성한다.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Bootstrap()
        {
            SceneManager.sceneLoaded += (scene, mode) =>
            {
                var go = new GameObject("WeatherSystem");
                go.AddComponent<WeatherSystem>();
            };
        }

        /// <summary>
        /// Start()에서 첫 맑음 단계 길이를 뽑기 전에 balanceConfig 폴백을 적용한다.
        /// </summary>
        private void Awake()
        {
            Active = this;
            ApplyBalanceConfigFallback();
        }

        /// <summary>
        /// 씬 재로드로 이 인스턴스가 사라질 때 정적 참조를 정리한다. 새 인스턴스의 Awake가 먼저
        /// 실행된 뒤 옛 인스턴스가 파괴되는 순서도 가능하므로, 반드시 "아직 나인 경우"에만 비운다.
        /// </summary>
        private void OnDestroy()
        {
            if (Active == this)
                Active = null;

            // [B33] 공유 빗줄기 머티리얼은 이 컴포넌트가 소유한다(HideAndDontSave라 씬 전환으로
            // 자동 파괴되지 않는다). 직접 지우지 않으면 씬을 다시 로드할 때마다 한 장씩 샌다.
            if (rainMaterial != null)
            {
                Destroy(rainMaterial);
                rainMaterial = null;
            }
        }

        /// <summary>
        /// balanceConfig가 있을 때, 0 이하로 남아있는(=미설정) 필드만 골라 config 값으로 채운다.
        /// 날씨 지속 시간이 0이면 맑음/비가 매 프레임 뒤집히고 rainDimFactor가 0이면 비 올 때 화면이
        /// 완전히 캄캄해지므로, 0 이하를 "아직 설정되지 않음"의 안전한 신호로 삼는다.
        /// balanceConfig가 비어 있으면 아무 것도 하지 않는다(기존 동작 100% 유지, NRE 없음).
        /// </summary>
        private void ApplyBalanceConfigFallback()
        {
            // B4-2: 인스펙터에서 연결되지 않았으면 Resources의 공용 에셋을 자동으로 집는다.
            // 런타임 생성 컴포넌트(WeatherSystem/Campfire/WaterStill 등)는 인스펙터 연결 수단이
            // 아예 없어서, 이 경로가 없으면 balanceConfig가 영원히 null로 남는다.
            if (balanceConfig == null)
                balanceConfig = SurvivalBalanceConfig.Active;
            if (balanceConfig == null)
                return;

            if (minClearSeconds <= 0f) minClearSeconds = balanceConfig.weatherMinClearSeconds;
            if (maxClearSeconds <= 0f) maxClearSeconds = balanceConfig.weatherMaxClearSeconds;
            if (minRainSeconds <= 0f) minRainSeconds = balanceConfig.weatherMinRainSeconds;
            if (maxRainSeconds <= 0f) maxRainSeconds = balanceConfig.weatherMaxRainSeconds;
            if (rainDimFactor <= 0f) rainDimFactor = balanceConfig.rainDimFactor;
        }

        /// <summary>비 파티클을 만들고, 항상 맑은 날씨로 시작한다(플레이어가 스폰되자마자 비를 맞지 않도록).</summary>
        private void Start()
        {
            var cam = Camera.main;
            followTarget = cam != null ? cam.transform : null;

            // 비가 그친 뒤 원래 안개 상태로 정확히 되돌리기 위해, 게임 시작 시점의 안개 설정을 기억해 둔다.
            originalFogEnabled = RenderSettings.fog;
            originalFogColor = RenderSettings.fogColor;
            originalFogDensity = RenderSettings.fogDensity;

            BuildRainParticles();
            StartClearPhase();
        }

        /// <summary>
        /// 코드로 비 파티클 시스템을 만든다. gravityModifier로 월드 아래 방향으로 자연스럽게
        /// 떨어지게 한다(에미터 회전과 무관하게 항상 아래로 떨어짐).
        /// 버그 수정: 처음에는 셰이더를 지정하지 않고 파티클 시스템 기본(내장 렌더 파이프라인용)
        /// 머티리얼을 그대로 뒀는데, 실제 라이브 플레이테스트에서 비가 올 때 화면에 마젠타색
        /// 점들이 흩뿌려지는 것을 확인했다 - URP 프로젝트에서는 내장 렌더 파이프라인 전용 셰이더를
        /// 인식하지 못해 "셰이더 없음"을 뜻하는 마젠타로 표시된 것. Universal Render
        /// Pipeline/Particles/Unlit 셰이더를 명시적으로 찾아 머티리얼에 지정해 해결했다
        /// (혹시 그 셰이더도 없는 환경을 대비해 URP에서도 안전하게 동작하는 Sprites/Default를
        /// 대체 셰이더로 둔다).
        /// </summary>
        private void BuildRainParticles()
        {
            var go = new GameObject("RainParticles");
            go.transform.SetParent(transform, false);
            rainParticles = go.AddComponent<ParticleSystem>();

            var main = rainParticles.main;
            main.loop = true;
            main.playOnAwake = false;
            main.startLifetime = 1.5f;
            main.startSpeed = 2f;
            main.gravityModifier = 3f;
            // 퀄리티 개선: 입자 크기를 고정값 하나가 아니라 범위(Min-Max Curve)로 줘서
            // 굵은 빗줄기/가는 빗줄기가 섞여 보이게 해 단조로움을 줄인다.
            main.startSize = new ParticleSystem.MinMaxCurve(0.03f, 0.09f);
            main.startColor = new Color(0.75f, 0.82f, 0.92f, 0.55f);
            // [B29] 1500 → 900. 충돌을 켜면 빗방울 하나하나가 매 프레임 스윕 검사를 받으므로 상한이
            // 곧 최악의 경우 비용이다. 실제 동시 생존은 약 430개(rainEmissionRate 주석)다.
            // [B33] 900 → 700. **총 예산 900은 그대로 두고** 남긴 200을 근경 레이어에 넘겼다.
            // 700도 실제 동시 생존(430)의 1.6배라 잘릴 일이 없고, 스윕 검사를 받는 입자 수의
            // 최악값은 오히려 줄었다(근경 레이어는 충돌을 켜지 않는다).
            main.maxParticles = 700;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            // [B22] Time.timeScale = 0이 되는 순간(엔딩/사망 화면)에 비가 얼어붙어 **공중에 멈춘
            // 빗줄기 벽**이 되는 것을 막는다. AGENT_BRIEF 4장의 "연출은 unscaled로"와 같은 취지다.
            main.useUnscaledTime = true;

            var emission = rainParticles.emission;
            emission.rateOverTime = 0f; // 세기(RainIntensity01)에 따라 Update에서 채운다

            var shape = rainParticles.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(28f, 0.5f, 28f);

            // [B22] 수평 바람. 빗줄기가 수직으로만 떨어지면 "화면에 붙은 선"처럼 보이는데, 조금만
            // 기울여도 대기의 움직임이 읽힌다. Stretch 렌더 모드가 속도 벡터 방향으로 늘이므로
            // 기울기와 길이가 자동으로 일치한다.
            var velocity = rainParticles.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.World;
            velocity.x = new ParticleSystem.MinMaxCurve(rainWind.x * 0.8f, rainWind.x * 1.2f);
            velocity.z = new ParticleSystem.MinMaxCurve(rainWind.y * 0.8f, rainWind.y * 1.2f);
            // [B25 감독 수정] y를 빼먹으면 안 된다. velocityOverLifetime의 x/y/z는 **셋 다 같은 커브
            // 모드**여야 하는데, x/z만 TwoConstants로 바꾸면 y는 기본값인 Constant로 남아 모드가
            // 갈린다. 그러면 비가 내리는 동안 매 프레임 "Particle Velocity curves must all be in the
            // same mode" 에러가 쏟아진다(실기에서 750개까지 확인했다). 낙하는 startSpeed와 중력이
            // 맡으므로 값 자체는 0이면 되지만, **2인자 생성자**를 써서 모드를 맞추는 게 핵심이다.
            velocity.y = new ParticleSystem.MinMaxCurve(0f, 0f);

            // [B29] (b) 빗줄기가 지붕/지형에 부딪혀 소멸한다. 실내 차단(a)이 주 수정이고 이쪽은
            // **밖에서 건물을 볼 때** 지붕이 비를 받아내는 것을 보여 주는 보조 수정이다.
            //
            // quality = High인 이유: Medium/Low는 정적 콜라이더를 복셀 격자에 평면으로 캐시해 근사하는
            // 방식이라 **얇은 판**에서 뚫림이 생긴다. 이 프로젝트의 지붕은 두께 0.2m급 건축 조각
            // (BuildPieceVisualBuilder가 붙이는 BoxCollider) 하나뿐이고, 그것도 플레이어가 세우면
            // 바로 그 프레임부터 유효해야 한다(복셀 캐시는 즉시 따라오지 않는다). High는 입자의
            // 이전→현재 위치를 잇는 스윕 검사라 얇은 바닥도, 방금 지은 조각도 그대로 잡는다.
            // 대신 비용이 붙으므로 방출량 상한을 460/900으로 내려 상쇄했다(위 주석).
            //
            // collidesWith = Default(레이어 0)만: 씬의 모든 오브젝트가 m_Layer 0이고, 코드에서 레이어를
            // 바꾸는 곳은 WorldMapManager:300의 바다 평면("Water") 하나뿐이다(그 평면은 같은 함수에서
            // 콜라이더를 아예 Destroy한다). ProjectSettings/TagManager.asset이 스테이징에 없어 커스텀
            // 레이어 정의를 확인할 수 없으므로, "확신이 없으면 Default만"이라는 보수적 선택을 따랐다 -
            // 이러면 트리거/바다에 부딪혀 빗줄기가 공중에 멈추는 사고가 원리적으로 불가능하다.
            //
            // enableDynamicColliders = false: 리지드바디가 달린(=움직이는) 콜라이더는 무시한다.
            // 플레이어 CharacterController가 대표적인데, 카메라가 그 캡슐 안에 있어서 빗방울이
            // 어깨 높이에서 사라지는 고리가 화면 가장자리에 생긴다. 지형·건축 조각·자원 노드는
            // 전부 리지드바디가 없는 정적 콜라이더라 이 설정과 무관하게 그대로 막는다.
            var collision = rainParticles.collision;
            collision.enabled = enableRainCollision;
            collision.type = ParticleSystemCollisionType.World;
            collision.mode = ParticleSystemCollisionMode.Collision3D;
            collision.quality = ParticleSystemCollisionQuality.High;
            collision.collidesWith = RainCollisionMask;
            collision.enableDynamicColliders = false;
            collision.lifetimeLoss = 1f;   // 닿는 즉시 소멸(빗방울은 튀지 않는다)
            collision.dampen = 1f;
            collision.bounce = 0f;
            collision.radiusScale = 1f;
            collision.sendCollisionMessages = false; // 콜백을 받을 곳이 없다 - 켜면 값비싼 이벤트만 쌓인다
            collision.colliderForce = 0f;            // 빗방울이 물리 오브젝트를 밀지 않는다

            var renderer = rainParticles.GetComponent<ParticleSystemRenderer>();
            if (renderer != null)
            {
                // 퀄리티 개선: 예전엔 둥근 점(Billboard)이라 정지된 빗방울처럼 보였다.
                // Stretched Billboard로 바꾸면 낙하 속도에 비례해 입자가 세로로 길게 늘어나
                // 실제 빗줄기처럼 보인다. lengthScale을 키워 속도감을 더 강조했다.
                // [B33] 텍스처의 줄기가 **세로**(v축)이고 Stretch는 속도 방향으로 v축을 늘리므로
                // 방향이 정확히 일치한다 — 별도 회전 보정이 필요 없다.
                renderer.renderMode = ParticleSystemRenderMode.Stretch;
                renderer.velocityScale = 0.12f;
                renderer.lengthScale = 3.5f;

                // 머티리얼을 못 만들었으면(두 셰이더가 다 없는 환경) **건드리지 않는다** —
                // null을 넣으면 파티클이 통째로 안 보인다(예전 코드도 같은 이유로 조건부였다).
                Material shared = EnsureRainMaterial();
                if (shared != null)
                    renderer.sharedMaterial = shared;
            }

            rainParticles.Stop();

            // [B33] 근경 레이어를 **먼저** 만든다. 아래 텍스처 적용이 두 레이어의 색/크기/시트 분할을
            // 한 번에 손보기 때문에, 순서를 뒤집으면 근경만 보정을 못 받는다.
            if (enableNearRainLayer)
                BuildNearRainParticles();

            // [B33] 빗줄기 텍스처. 실패해도 조용히 예전 모습(기본 파티클)으로 남는다.
            TryApplyRainStreakTexture();

            // [B22] 땅/수면에 부딪히는 물튀김. 빗줄기만 있으면 비가 "카메라 앞에 떠 있는 레이어"로
            // 보이고 월드에 닿지 않는다 - 지면에 닿는 신호가 하나 있어야 비가 세계 안에 있게 된다.
            // 파티클 상한 60개짜리 시스템 하나뿐이라(섬 개수와 무관한 단일 인스턴스) 비용은 무시할 수준이다.
            if (enableRainSplashes)
            {
                rainSplashes = EffectBuilder.CreateRainSplashes(transform);
                if (rainSplashes != null)
                    rainSplashes.Stop();
            }
        }

        /// <summary>
        /// [B33] 원경/근경 빗줄기가 공유하는 머티리얼을 만든다(최초 1회).
        /// URP 프로젝트에서 파티클 기본 머티리얼(빌트인 전용)을 그대로 두면 마젠타로 뜨는 사고가
        /// 이미 있었으므로(BuildRainParticles 주석), URP Particles/Unlit → Sprites/Default 순으로
        /// 찾는 기존 폴백 사슬을 그대로 유지한다. 둘 다 없으면 null을 돌려주고 렌더러는 손대지 않는다.
        /// </summary>
        private Material EnsureRainMaterial()
        {
            if (rainMaterial != null)
                return rainMaterial;

            Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (shader == null)
                shader = Shader.Find("Sprites/Default"); // URP에서도 안전하게 동작하는 대체 셰이더
            if (shader == null)
                return null;

            rainMaterial = new Material(shader);
            rainMaterial.name = "MG_RainStreak";
            rainMaterial.hideFlags = HideFlags.HideAndDontSave;
            ConfigureTransparentBlending(rainMaterial);
            return rainMaterial;
        }

        /// <summary>
        /// [B33] 파티클 머티리얼을 **알파 블렌딩**으로 바꾼다.
        ///
        /// 이게 없으면 빗줄기 텍스처가 통째로 망가진다: `new Material(URP Particles/Unlit)`의
        /// 기본 Surface는 **Opaque**라 알파 채널이 무시된다. 지금까지는 텍스처 없이 단색 쿼드를
        /// 그렸기 때문에 "옅은 청백 막대"로 그럭저럭 보였지만(그래서 아무도 눈치채지 못했다),
        /// 알파에 줄기 모양이 들어 있는 rain_streak을 얹는 순간 **줄기 대신 불투명한 직사각형**이
        /// 화면을 가득 채운다. startColor의 알파(0.55)가 여태 실제로는 무시되고 있었다는 뜻이기도 하다.
        ///
        /// 프로퍼티/키워드 이름은 URP Particles/Unlit의 것이고, 전부 HasProperty로 감싸서
        /// 폴백 셰이더(Sprites/Default — 이미 알파 블렌딩이라 손댈 것이 없다)에서는 조용히 건너뛴다.
        /// </summary>
        private static void ConfigureTransparentBlending(Material material)
        {
            if (material == null)
                return;

            // _Surface: 0 = Opaque, 1 = Transparent. _Blend: 0 = Alpha, 1 = Premultiply, 2 = Additive.
            if (material.HasProperty("_Surface"))
                material.SetFloat("_Surface", 1f);
            if (material.HasProperty("_Blend"))
                material.SetFloat("_Blend", 0f);
            if (material.HasProperty("_SrcBlend"))
                material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            if (material.HasProperty("_DstBlend"))
                material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            if (material.HasProperty("_ZWrite"))
                material.SetFloat("_ZWrite", 0f);
            if (material.HasProperty("_AlphaClip"))
                material.SetFloat("_AlphaClip", 0f);

            // 셰이더 변형 키워드도 같이 맞춰야 한다(프로퍼티만 바꾸면 컴파일된 변형은 그대로다).
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.DisableKeyword("_ALPHATEST_ON");
            material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            material.SetOverrideTag("RenderType", "Transparent");
            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        }

        /// <summary>
        /// [B33] 빗줄기 텍스처(Textures/rain_streak)를 공유 머티리얼에 얹고, 두 파티클 시스템의
        /// 시트 분할·색·크기를 텍스처에 맞게 보정한다.
        ///
        /// 텍스처가 없으면 **아무것도 바꾸지 않고** 조용히 빠진다(기존 모습 그대로). 실패를 영구
        /// 래치하지 않으므로(AGENT_BRIEF 4장 3번) 비가 시작될 때마다 한 번 더 시도한다 —
        /// 임포트가 늦어 첫 Start에서 못 잡혀도 다음 강우에 자연히 복구된다.
        ///
        /// 색/크기 보정의 근거: 시트의 알파 평균이 낮아(줄기가 가늘다) 기존 startColor
        /// (청백 0.75/0.82/0.92 · 알파 0.55)를 그대로 곱하면 색은 두 번 어두워지고 알파는 절반이
        /// 되어 비가 거의 안 보인다. 색조는 텍스처(옅은 청백)가 이미 갖고 있으므로 startColor는
        /// **흰색**으로 두고 알파만 0.95로 올린다. 폭은 줄기가 칸 안에서 8% 남짓만 차지하므로
        /// (0.03~0.09 → 0.05~0.14) 넓혀서 화면상 줄기 굵기를 예전 수준으로 맞춘다.
        /// </summary>
        private void TryApplyRainStreakTexture()
        {
            if (rainStreakApplied)
                return;

            Material material = EnsureRainMaterial();
            if (material == null)
                return;

            // [로드 규칙] 필드 초기자가 아니라 메서드 안에서 부른다(초기자는 Unity가 Load를 막는
            // 시점에 돌 수 있다 — AGENT_BRIEF 4장 3번).
            var streak = Resources.Load<Texture2D>("Textures/rain_streak");
            if (streak == null)
                return;

            material.mainTexture = streak;
            rainStreakApplied = true;

            ApplyStreakLook(rainParticles, new Color(1f, 1f, 1f, 0.95f), 0.05f, 0.14f);
            ApplyStreakLook(nearRainParticles, new Color(1f, 1f, 1f, Mathf.Clamp01(nearRainAlpha)),
                0.09f, 0.20f);
        }

        /// <summary>
        /// [B33] 한 파티클 시스템에 시트 분할(textureSheetAnimation)과 텍스처용 색/크기를 건다.
        ///
        /// 시트 분할 설정의 의미: 가로 rainStreakTilesX칸 × 세로 1칸으로 자르고,
        /// frameOverTime을 상수 0으로 고정한 뒤 startFrame을 0~칸수 사이의 난수로 준다.
        /// 즉 파티클마다 **수명 내내 바뀌지 않는 한 칸**(= 줄기 하나)을 물게 된다. 프레임이 흐르면
        /// 낙하 중에 줄기 모양이 바뀌어 깜빡이는 것처럼 보이므로 일부러 정지시킨 것이다.
        /// 이 난수는 파티클 시스템 내부 난수라 UnityEngine.Random / 월드 생성 스트림을 소비하지 않는다.
        /// </summary>
        private void ApplyStreakLook(ParticleSystem ps, Color startColor, float sizeMin, float sizeMax)
        {
            if (ps == null)
                return;

            var main = ps.main;
            main.startColor = startColor;
            main.startSize = new ParticleSystem.MinMaxCurve(sizeMin, sizeMax);

            int tiles = Mathf.Clamp(rainStreakTilesX, 1, 64);
            var sheet = ps.textureSheetAnimation;
            sheet.enabled = tiles > 1;
            if (tiles <= 1)
                return;

            sheet.mode = ParticleSystemAnimationMode.Grid;
            sheet.numTilesX = tiles;
            sheet.numTilesY = 1;
            sheet.animation = ParticleSystemAnimationType.WholeSheet;
            sheet.frameOverTime = new ParticleSystem.MinMaxCurve(0f);
            sheet.startFrame = new ParticleSystem.MinMaxCurve(0f, tiles - 0.001f);
            sheet.cycleCount = 1;
        }

        /// <summary>
        /// [B33] 근경 빗줄기 레이어를 만든다. 원경(머리 위 15m · 얇고 촘촘)과 달리 머리 위 5.5m에서
        /// 굵고 빠르게 떨어져 카메라를 스쳐 지나간다 — 두 레이어의 낙하 속도 차이가 그대로 시차가
        /// 되어 "비에 깊이가 있다"로 읽힌다.
        ///
        /// 원경과 다른 점(그리고 그 이유):
        ///  · 충돌 없음 — 실내 판정(shelteredFactor)이 이미 지붕 아래에서 이 레이어를 끄므로,
        ///    코앞 입자에 매 프레임 스윕 검사를 돌릴 이유가 없다. 예산 절약이 곧 이 레이어의 비용이다.
        ///  · 알파 낮음(nearRainAlpha 0.4) — 카메라 코앞을 지나므로 원경과 같은 진하기면 시야를 가린다.
        ///  · maxParticles 200 · 방출 95/s · 수명 0.8s → 동시 생존 약 75개(상한의 38%).
        ///  · velocityOverLifetime의 x/y/z를 **셋 다 TwoConstants로** 준다. 하나라도 모드가 갈리면
        ///    "Particle Velocity curves must all be in the same mode" 에러가 매 프레임 쏟아진다(B25).
        /// </summary>
        private void BuildNearRainParticles()
        {
            var go = new GameObject("NearRainParticles");
            go.transform.SetParent(transform, false);
            nearRainParticles = go.AddComponent<ParticleSystem>();

            var main = nearRainParticles.main;
            main.loop = true;
            main.playOnAwake = false;
            main.startLifetime = 0.8f;
            main.startSpeed = 5f;
            main.gravityModifier = 4f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.07f, 0.16f);
            main.startColor = new Color(0.78f, 0.85f, 0.95f, Mathf.Clamp01(nearRainAlpha));
            main.maxParticles = 200;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.useUnscaledTime = true;

            var emission = nearRainParticles.emission;
            emission.rateOverTime = 0f; // 세기에 따라 UpdateRainVisuals가 채운다

            var shape = nearRainParticles.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(11f, 0.4f, 11f);

            var velocity = nearRainParticles.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.World;
            velocity.x = new ParticleSystem.MinMaxCurve(rainWind.x * 1.0f, rainWind.x * 1.5f);
            velocity.y = new ParticleSystem.MinMaxCurve(0f, 0f);
            velocity.z = new ParticleSystem.MinMaxCurve(rainWind.y * 1.0f, rainWind.y * 1.5f);

            var renderer = nearRainParticles.GetComponent<ParticleSystemRenderer>();
            if (renderer != null)
            {
                renderer.renderMode = ParticleSystemRenderMode.Stretch;
                renderer.velocityScale = 0.14f;
                renderer.lengthScale = 4.5f;
                Material shared = EnsureRainMaterial();
                if (shared != null)
                    renderer.sharedMaterial = shared;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }

            nearRainParticles.Stop();
        }

        /// <summary>매 프레임 날씨 단계(맑음/비) 타이머를 진행시키고, 비가 올 때 파티클을 따라오게 한다.</summary>
        private void Update()
        {
            phaseTimer += Time.deltaTime;
            if (phaseTimer >= phaseDuration)
            {
                if (IsRaining)
                    StartClearPhase();
                else
                    StartRainPhase();
            }

            // [B22] 연출 세기는 논리 상태(IsRaining)를 rainFadeSeconds에 걸쳐 따라간다.
            // unscaledDeltaTime을 쓰는 이유는 AGENT_BRIEF 4장 그대로 — 엔딩/사망으로 timeScale이 0이
            // 되어도 진행 중이던 페이드가 첫 프레임에서 굳어버리지 않게 하기 위해서다.
            float target = IsRaining ? 1f : 0f;
            if (rainFadeSeconds <= 0f)
            {
                RainIntensity01 = target;
            }
            else
            {
                RainIntensity01 = Mathf.MoveTowards(
                    RainIntensity01, target, Time.unscaledDeltaTime / rainFadeSeconds);
            }

            UpdateSeaRoughness();

            RefreshFollowTarget();
            UpdateShelterFactor();
            UpdateRainVisuals();

            if (IsRaining)
                ApplyRainGameplayEffects(Time.deltaTime);
        }

        /// <summary>
        /// 바다 거칠기(SeaRoughness01)를 갱신한다. OceanWaves가 이 값 하나로 파도 진폭·속도를 정한다.
        ///
        /// 목표값 = max(비 세기, 사전 예고 바닥값)이다.
        ///  * 비 단계에서는 바닥값을 preStormRoughness로 **유지**한다. 그러지 않으면 비가 시작되는
        ///    순간 예고값(0.45)이 사라지고 RainIntensity01(0에서 출발)이 목표가 되어 거칠기가 한 번
        ///    푹 꺼졌다가 다시 오르는 부자연스러운 골이 생긴다.
        ///  * 맑음 단계에서는 남은 시간이 preStormSeconds 안으로 들어왔을 때만 0 → preStormRoughness로
        ///    선형 상승한다. phaseTimer/phaseDuration을 읽기만 하므로 단계 길이도 난수도 바뀌지 않는다.
        /// 마지막으로 MoveTowards로 한 겹 더 완만하게 만든다(급변 금지 요구). 진행은 unscaledDeltaTime —
        /// RainIntensity01과 같은 이유로, timeScale이 0이 되어도 진행 중이던 보간이 굳지 않게 한다.
        /// </summary>
        private void UpdateSeaRoughness()
        {
            float floorValue;
            if (IsRaining)
            {
                floorValue = Mathf.Clamp01(preStormRoughness);
            }
            else if (preStormSeconds > 0f && phaseDuration > 0f)
            {
                float remaining = phaseDuration - phaseTimer;
                floorValue = remaining >= preStormSeconds
                    ? 0f
                    : Mathf.Clamp01(1f - remaining / preStormSeconds) * Mathf.Clamp01(preStormRoughness);
            }
            else
            {
                floorValue = 0f;
            }

            float target = Mathf.Max(RainIntensity01, floorValue);

            if (seaRoughnessFadeSeconds <= 0f)
                SeaRoughness01 = target;
            else
                SeaRoughness01 = Mathf.MoveTowards(
                    SeaRoughness01, target, Time.unscaledDeltaTime / seaRoughnessFadeSeconds);
        }

        /// <summary>
        /// [B22] 비 연출(에미터 위치 · 방출량 · 물튀김)을 RainIntensity01에 맞춰 갱신한다.
        /// 게임플레이 수치는 전혀 건드리지 않는다.
        /// </summary>
        private void UpdateRainVisuals()
        {
            // [B29] 실내 계수를 곱한 "실제로 보이는 세기". RainIntensity01 자체는 손대지 않으므로
            // DayNightCycle이 읽는 광량/안개는 실내에서도 그대로다(창밖은 여전히 흐리다).
            float visibleIntensity = RainIntensity01 * shelteredFactor;

            if (visibleIntensity <= 0f)
            {
                if (rainParticles != null && rainParticles.isPlaying)
                    rainParticles.Stop();
                if (nearRainParticles != null && nearRainParticles.isPlaying)
                    nearRainParticles.Stop();
                if (rainSplashes != null && rainSplashes.isPlaying)
                    rainSplashes.Stop();
                return;
            }

            if (followTarget != null)
            {
                transform.position = new Vector3(
                    followTarget.position.x,
                    followTarget.position.y + rainHeightAboveTarget,
                    followTarget.position.z);
            }

            if (rainParticles != null)
            {
                var emission = rainParticles.emission;
                emission.rateOverTime = Mathf.Max(0f, rainEmissionRate) * visibleIntensity;
                if (!rainParticles.isPlaying)
                    rainParticles.Play();
            }

            // [B33] 근경 레이어. 이 GameObject가 머리 위 rainHeightAboveTarget에 있으므로,
            // 로컬 y 오프셋으로 nearRainHeightAboveTarget 높이에 내려 둔다(인스펙터에서 두 높이를
            // 바꿔도 매 프레임 따라온다 — 값을 굳혀 두면 "고쳤는데 안 바뀐다"가 된다).
            if (nearRainParticles != null)
            {
                nearRainParticles.transform.localPosition = new Vector3(
                    0f, nearRainHeightAboveTarget - rainHeightAboveTarget, 0f);

                var nearEmission = nearRainParticles.emission;
                nearEmission.rateOverTime = Mathf.Max(0f, nearRainEmissionRate) * visibleIntensity;
                if (!nearRainParticles.isPlaying)
                    nearRainParticles.Play();
            }

            UpdateRainSplashes(visibleIntensity);
        }

        /// <summary>
        /// [B22] 페이드 아웃 중에도 남은 빗줄기가 플레이어를 따라와야 한다(예전에는 IsRaining이 꺼지는
        /// 순간 에미터가 그 자리에 멈춰, 걸어가면 등 뒤에 비 기둥이 서 있었다).
        /// [B29] 실내 판정도 이 대상의 위치를 쓰므로 Update 맨 앞으로 끌어올렸다.
        /// </summary>
        private void RefreshFollowTarget()
        {
            if (followTarget != null)
                return;

            var cam = Camera.main;
            followTarget = cam != null ? cam.transform : null;
        }

        /// <summary>
        /// [B29] 플레이어가 지붕 아래(실내)에 있는지 보고 빗줄기·물튀김에 곱할 계수를 0↔1로 옮긴다.
        ///
        /// 판정 함수는 둘 다 **남의 파일 소유**라 여기서는 읽기만 한다.
        ///  · BuildingSystem.IsInsideEnclosedStructure(Vector3) — 벽으로 둘러싸이고 **위층 바닥(=지붕)이
        ///    덮인** 칸일 때만 true. 결과를 (공간·칸·층) 단위로 캐시하고 조각 버전이 바뀔 때만 버리므로
        ///    매 프레임 불러도 싸지만, 굳이 그럴 이유가 없어 indoorCheckInterval(0.25초)마다만 부른다.
        ///  · Shelter.IsInsideHome(Vector3) — 위 판정을 이미 OR로 품고 있고, 거기에 쉼터 홈 반경
        ///    (Lv2 8m / Lv3 14m)을 더한다. 반경은 형상이 아니라 거리라서 토글로 분리해 뒀다.
        ///
        /// 계수는 unscaledDeltaTime으로 페이드한다(AGENT_BRIEF 4장: timeScale 0 구간이 있다).
        /// 문턱에 서서 판정이 오갈 때 방출량이 한 프레임에 0↔최대로 튀지 않게 하는 것이 목적이다.
        /// </summary>
        private void UpdateShelterFactor()
        {
            float target = 1f;

            if (stopRainIndoors)
            {
                indoorCheckTimer -= Time.unscaledDeltaTime;
                if (indoorCheckTimer <= 0f && followTarget != null)
                {
                    indoorCheckTimer = Mathf.Max(0.05f, indoorCheckInterval);

                    // 카메라(눈높이 = 발밑 +1.6m)를 그대로 쓴다. IsInsideEnclosedStructure는 발밑 바닥에서
                    // -0.3m ~ LevelHeight(2.5m) 안쪽을 "그 바닥을 딛고 있다"로 보므로 1.6m는 안전하게 들어간다.
                    Vector3 probe = followTarget.position;
                    indoorNow = BuildingSystem.IsInsideEnclosedStructure(probe)
                        || (shelterRadiusCountsAsIndoors && Shelter.IsInsideHome(probe));
                }

                target = indoorNow ? 0f : 1f;
            }
            else
            {
                indoorNow = false;
            }

            shelteredFactor = indoorFadeSeconds <= 0f
                ? target
                : Mathf.MoveTowards(shelteredFactor, target, Time.unscaledDeltaTime / indoorFadeSeconds);
        }

        /// <summary>
        /// [B22] 물튀김 파티클을 플레이어 발밑 지면(없으면 해수면)에 붙여 둔다.
        /// 지면 높이는 TerrainSampler.SnapToGround로 찾는다 - 이 헬퍼는 이름이 "Island_"로 시작하는
        /// 콜라이더만 지형으로 인정하므로 플레이어 자신의 캡슐이나 자원 노드에 맞지 않는다(그 함정을
        /// 이미 한 번 겪고 만들어진 API다). 지형을 못 찾으면 입력 위치를 그대로 돌려주므로,
        /// 그때는 바다 위로 보고 해수면(0)에 깐다.
        /// </summary>
        private void UpdateRainSplashes(float visibleIntensity)
        {
            if (rainSplashes == null || followTarget == null)
                return;

            splashProbeTimer -= Time.unscaledDeltaTime;
            if (splashProbeTimer <= 0f)
            {
                splashProbeTimer = SplashProbeInterval;
                splashGroundY = ProbeSplashSurfaceY(followTarget.position);
            }

            rainSplashes.transform.position = new Vector3(
                followTarget.position.x, splashGroundY + 0.05f, followTarget.position.z);

            var splashEmission = rainSplashes.emission;
            splashEmission.rateOverTime = SplashRateAtFullRain * visibleIntensity;
            if (!rainSplashes.isPlaying)
                rainSplashes.Play();
        }

        /// <summary>
        /// [B29] 물튀김을 깔 표면 높이. 기본은 예전과 같은 지형(SnapToGround)이고, 그 위에 **건축 조각**
        /// 바닥이 있으면 그쪽을 쓴다.
        ///
        /// 필요한 이유: 지붕 없는 2층 데크에 서 있으면 SnapToGround가 데크를 못 보고(이름이 "Island_"가
        /// 아니다) 파문을 발밑이 아니라 **한 층 아래 지형**에 깔았다. 실내는 위 shelteredFactor가 이미
        /// 물튀김을 끄므로, 이 경로가 실제로 도는 것은 "지붕/데크 위에 서서 비를 맞는" 경우뿐이다.
        /// 레이는 Default 레이어 + 이름이 "BuildPiece_"인 콜라이더만 채택한다 - 자원 노드/사냥감 위에
        /// 파문이 얹히는 예전 함정(TerrainSampler 주석)을 되풀이하지 않기 위해서다.
        /// </summary>
        private float ProbeSplashSurfaceY(Vector3 probe)
        {
            Vector3 snapped = TerrainSampler.SnapToGround(probe);
            // SnapToGround는 지형을 못 찾으면 인자를 그대로 반환한다(y가 비트 단위로 같다).
            float surfaceY = snapped.y < probe.y ? snapped.y : SeaLevelFallbackY;

            float rayTop = probe.y + 0.2f;
            int hitCount = Physics.RaycastNonAlloc(
                new Vector3(probe.x, rayTop, probe.z), Vector3.down, splashHitBuffer,
                SplashProbeRayLength, RainCollisionMask, QueryTriggerInteraction.Ignore);

            for (int i = 0; i < hitCount; i++)
            {
                Collider collider = splashHitBuffer[i].collider;
                if (collider == null || !collider.gameObject.name.StartsWith(BuildPieceNamePrefix))
                    continue;

                float y = splashHitBuffer[i].point.y;
                if (y > surfaceY && y <= rayTop)
                    surfaceY = y;
            }

            return surfaceY;
        }

        /// <summary>지형을 못 찾았을 때 물튀김을 깔 높이(=해수면). WorldMapManager.seaLevel 기본값과 같다.</summary>
        private const float SeaLevelFallbackY = 0f;

        /// <summary>최대 강우일 때 물튀김 초당 방출 개수. 수명 0.45초라 동시 생존은 약 20개다.</summary>
        private const float SplashRateAtFullRain = 45f;

        /// <summary>[B29] 물튀김 표면 탐색용 아래 방향 레이 길이(m). 눈높이 위 0.2m에서 쏴 발밑 1.4m까지 본다.</summary>
        private const float SplashProbeRayLength = 3.2f;

        /// <summary>[B29] 건축 조각 루트 이름 접두어(BuildPieceVisualBuilder:72 `BuildPiece_{type}`).</summary>
        private const string BuildPieceNamePrefix = "BuildPiece_";

        /// <summary>[B29] 물튀김 표면 탐색 레이 결과 버퍼(재사용 - 0.3초에 한 번 도는 경로라 4개면 넉넉하다).</summary>
        private static readonly RaycastHit[] splashHitBuffer = new RaycastHit[8];

        /// <summary>
        /// [B9] 비가 오는 동안만 매 프레임 실행되는 게임플레이 효과. 위 필드 주석의 설계 근거대로
        /// 대상 컴포넌트의 설정 필드는 건드리지 않고 공개 Tick(float)을 추가 델타로 한 번 더 부른다.
        /// 배율이 1 이하면 추가 델타가 0 이하가 되어 그 효과는 통째로 건너뛴다(효과 끄기 = 배율 1).
        /// 대상이 하나도 없으면(증류기·모닥불 미설치) 아무 일도 일어나지 않는다 — 초반 플레이는 그대로다.
        /// </summary>
        private void ApplyRainGameplayEffects(float deltaTime)
        {
            if (deltaTime <= 0f)
                return;

            rainRescanTimer -= deltaTime;
            if (rainRescanTimer <= 0f)
            {
                rainRescanTimer = Mathf.Max(0.5f, rainTargetRescanSeconds);
                // 비활성 오브젝트는 제외한다(꺼져 있는 모닥불/증류기가 비를 맞을 이유가 없다).
                // 정렬은 필요 없으므로 SortMode.None — SaveLoadController가 쓰는 것과 같은 API다.
                // [B13] FindObjectsSortMode를 받는 오버로드는 Unity 6에서 obsolete다(CS0618).
                // 프로젝트의 다른 호출부(DayNightCycle:169, SaveLoadController 전부)와 같은
                // 1인자 형태로 맞춘다 - 정렬이 필요 없는 호출이라 동작도 동일하다.
                rainWaterStills = FindObjectsByType<WaterStill>(FindObjectsInactive.Exclude);
                rainCampfires = FindObjectsByType<Campfire>(FindObjectsInactive.Exclude);
            }

            // 증류기: 빗물을 받아 생산이 빨라진다. WaterStill.Tick은 maxStorage로 클램프하므로
            // 배수를 걸어도 저장 상한을 넘지 않는다(넘칠 걱정 없이 안전하게 가속만 된다).
            float extraStillSeconds = (rainWaterStillMultiplier - 1f) * deltaTime;
            if (extraStillSeconds > 0f)
            {
                for (int i = 0; i < rainWaterStills.Length; i++)
                {
                    WaterStill still = rainWaterStills[i];
                    if (still != null)
                        still.Tick(extraStillSeconds);
                }
            }

            // 모닥불: 빗속에서 연료가 빨리 탄다. Campfire.Tick은 꺼져 있으면 즉시 return하고,
            // 연료가 0이 되면 스스로 isLit을 내린다 — 즉 "비가 오래 오면 불이 꺼진다"가 자동으로 성립한다.
            float extraFuelSeconds = (rainCampfireFuelMultiplier - 1f) * deltaTime;
            if (extraFuelSeconds > 0f)
            {
                for (int i = 0; i < rainCampfires.Length; i++)
                {
                    Campfire fire = rainCampfires[i];
                    if (fire != null)
                        fire.Tick(extraFuelSeconds);
                }
            }
        }

        /// <summary>맑은 날씨로 전환한다. 비 파티클과 빗소리를 멈춘다.</summary>
        private void StartClearPhase()
        {
            IsRaining = false;
            phaseTimer = 0f;
            phaseDuration = Random.Range(minClearSeconds, maxClearSeconds);

            // [B22] 파티클 정지는 UpdateRainVisuals가 RainIntensity01이 0에 닿았을 때 처리한다.
            // 여기서 즉시 Stop()하면 페이드 아웃(rainFadeSeconds)이 성립하지 않는다.

            // 퀄리티 개선: 비가 그치면 게임 시작 시점의 원래 안개 설정으로 정확히 되돌린다.
            // 단, DayNightCycle이 맑은 날 대기 안개를 직접 몰고 있으면 그쪽에 맡긴다(위 dayNight 주석 참고).
            // [B22] 이제 DayNightCycle이 비 안개까지 RainIntensity01로 보간해서 몰기 때문에, 그쪽이
            // 살아 있으면 여기서 손댈 것이 아무것도 없다. 아래 복원은 DayNightCycle이 없거나 대기 안개를
            // 끈 구성(테스트 씬 등)에서만 도는 폴백이다.
            if (dayNight == null)
                dayNight = FindAnyObjectByType<DayNightCycle>();

            if (dayNight == null || !dayNight.enableAtmosphericFog)
            {
                RenderSettings.fog = originalFogEnabled;
                RenderSettings.fogColor = originalFogColor;
                RenderSettings.fogDensity = originalFogDensity;
            }

            AudioManager.Instance?.StopRainAmbient();
        }

        /// <summary>비 오는 날씨로 전환한다. 비 파티클과 빗소리를 시작한다.</summary>
        private void StartRainPhase()
        {
            IsRaining = true;
            phaseTimer = 0f;
            phaseDuration = Random.Range(minRainSeconds, maxRainSeconds);

            // [B9] 비가 시작되는 프레임에 대상 목록을 즉시 훑도록 타이머를 0으로 만든다.
            // (맑은 동안 설치/철거된 증류기·모닥불이 캐시에 반영돼 있지 않기 때문)
            rainRescanTimer = 0f;

            // [B33] Start() 시점에 텍스처를 못 잡았으면(임포트 지연 등) 여기서 한 번 더 시도한다.
            // 이미 얹었으면 즉시 return하므로 비용이 없다(실패를 영구 래치하지 않는 규칙).
            TryApplyRainStreakTexture();

            if (followTarget != null)
                transform.position = followTarget.position + Vector3.up * rainHeightAboveTarget;

            // [B22] 파티클 Play/방출량과 안개는 세기(RainIntensity01)를 따라 UpdateRainVisuals /
            // DayNightCycle이 서서히 올린다. 여기서 한 프레임에 최대치로 세팅하면 페이드가 무의미해진다.
            // 폴백(DayNightCycle이 없거나 대기 안개를 끈 구성)일 때만 예전처럼 즉시 비 안개를 건다.
            if (dayNight == null)
                dayNight = FindAnyObjectByType<DayNightCycle>();

            if (dayNight == null || !dayNight.enableAtmosphericFog)
            {
                RenderSettings.fog = true;
                RenderSettings.fogMode = FogMode.ExponentialSquared;
                RenderSettings.fogColor = rainFogColor;
                RenderSettings.fogDensity = rainFogDensity;
            }

            AudioManager.Instance?.StartRainAmbient();
        }
    }
}
