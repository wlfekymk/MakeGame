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

        [Tooltip("최대 세기일 때의 빗줄기 초당 방출 개수. 수명 1.5초라 동시 생존 입자는 이 값의 1.5배다" +
            "(maxParticles 1500 이내로 유지할 것).")]
        public float rainEmissionRate = 700f;

        [Tooltip("빗줄기가 비스듬히 내리게 만드는 수평 바람 속도(m/s, 월드 XZ). Stretched 빌보드가" +
            " 속도 방향으로 늘어나므로 이 값이 곧 빗줄기의 기울기가 된다.")]
        public Vector2 rainWind = new Vector2(2.6f, 1.4f);

        [Tooltip("빗방울이 땅/수면에 부딪히는 물튀김 파티클을 켤지 여부.")]
        public bool enableRainSplashes = true;

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
        private ParticleSystem rainSplashes;
        private Transform followTarget;

        // 물튀김 파티클을 놓을 지면 높이. 매 프레임 레이캐스트하면 낭비라 주기적으로만 갱신한다.
        private float splashGroundY;
        private float splashProbeTimer;

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
            main.maxParticles = 1500;
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

            var renderer = rainParticles.GetComponent<ParticleSystemRenderer>();
            if (renderer != null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
                if (shader == null)
                    shader = Shader.Find("Sprites/Default"); // URP에서도 안전하게 동작하는 대체 셰이더
                if (shader != null)
                    renderer.material = new Material(shader);

                // 퀄리티 개선: 예전엔 둥근 점(Billboard)이라 정지된 빗방울처럼 보였다.
                // Stretched Billboard로 바꾸면 낙하 속도에 비례해 입자가 세로로 길게 늘어나
                // 실제 빗줄기처럼 보인다. lengthScale을 키워 속도감을 더 강조했다.
                renderer.renderMode = ParticleSystemRenderMode.Stretch;
                renderer.velocityScale = 0.12f;
                renderer.lengthScale = 3.5f;
            }

            rainParticles.Stop();

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

            UpdateRainVisuals();

            if (IsRaining)
                ApplyRainGameplayEffects(Time.deltaTime);
        }

        /// <summary>
        /// [B22] 비 연출(에미터 위치 · 방출량 · 물튀김)을 RainIntensity01에 맞춰 갱신한다.
        /// 게임플레이 수치는 전혀 건드리지 않는다.
        /// </summary>
        private void UpdateRainVisuals()
        {
            // 페이드 아웃 중에도 남은 빗줄기가 플레이어를 따라와야 한다(예전에는 IsRaining이 꺼지는
            // 순간 에미터가 그 자리에 멈춰, 걸어가면 등 뒤에 비 기둥이 서 있었다).
            if (followTarget == null)
            {
                var cam = Camera.main;
                followTarget = cam != null ? cam.transform : null;
            }

            if (RainIntensity01 <= 0f)
            {
                if (rainParticles != null && rainParticles.isPlaying)
                    rainParticles.Stop();
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
                emission.rateOverTime = Mathf.Max(0f, rainEmissionRate) * RainIntensity01;
                if (!rainParticles.isPlaying)
                    rainParticles.Play();
            }

            UpdateRainSplashes();
        }

        /// <summary>
        /// [B22] 물튀김 파티클을 플레이어 발밑 지면(없으면 해수면)에 붙여 둔다.
        /// 지면 높이는 TerrainSampler.SnapToGround로 찾는다 - 이 헬퍼는 이름이 "Island_"로 시작하는
        /// 콜라이더만 지형으로 인정하므로 플레이어 자신의 캡슐이나 자원 노드에 맞지 않는다(그 함정을
        /// 이미 한 번 겪고 만들어진 API다). 지형을 못 찾으면 입력 위치를 그대로 돌려주므로,
        /// 그때는 바다 위로 보고 해수면(0)에 깐다.
        /// </summary>
        private void UpdateRainSplashes()
        {
            if (rainSplashes == null || followTarget == null)
                return;

            splashProbeTimer -= Time.unscaledDeltaTime;
            if (splashProbeTimer <= 0f)
            {
                splashProbeTimer = SplashProbeInterval;

                Vector3 probe = followTarget.position;
                Vector3 snapped = TerrainSampler.SnapToGround(probe);
                // SnapToGround는 지형을 못 찾으면 인자를 그대로 반환한다(y가 비트 단위로 같다).
                splashGroundY = snapped.y < probe.y ? snapped.y : SeaLevelFallbackY;
            }

            rainSplashes.transform.position = new Vector3(
                followTarget.position.x, splashGroundY + 0.05f, followTarget.position.z);

            var splashEmission = rainSplashes.emission;
            splashEmission.rateOverTime = SplashRateAtFullRain * RainIntensity01;
            if (!rainSplashes.isPlaying)
                rainSplashes.Play();
        }

        /// <summary>지형을 못 찾았을 때 물튀김을 깔 높이(=해수면). WorldMapManager.seaLevel 기본값과 같다.</summary>
        private const float SeaLevelFallbackY = 0f;

        /// <summary>최대 강우일 때 물튀김 초당 방출 개수. 수명 0.45초라 동시 생존은 약 20개다.</summary>
        private const float SplashRateAtFullRain = 45f;

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
