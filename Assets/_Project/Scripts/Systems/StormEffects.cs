using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MakeGame.Systems
{
    /// <summary>
    /// 폭풍우 연출의 단일 소유자 — **번개 섬광 · 천둥 소리 · 화면(렌즈) 빗방울** 셋을 담당한다.
    /// WeatherSystem이 "비가 온다"를 정하고, RainWetness가 "젖는다"를 칠하고, DayNightCycle이
    /// "어두워진다/안개가 낀다"를 몰고, 이 컴포넌트가 그 위에 **사건**을 얹는다.
    ///
    /// ── 조명 소유권 충돌을 피한 방법 (가장 중요한 설계 결정) ─────────────────────
    /// 번개를 "전용 Light"로 만들면 안 된다. DayNightCycle.FindDirectionalLight는
    /// **씬의 첫 Directional Light를 태양으로 채택**하는데(DayNightCycle.cs:280-288, 같은 함수를
    /// UnderwaterAmbience도 쓴다), 여기서 Directional을 하나 더 만들면 실행 순서에 따라 그쪽이
    /// 태양으로 잡혀 하루 종일 번개 라이트가 해 노릇을 하는 사고가 난다. Point/Spot으로 피할 수는
    /// 있지만, 그러면 "하늘 전체가 번쩍인다"는 요구를 못 채운다(광원 반경 안만 밝아진다).
    /// 그래서 **광원을 만들지 않고** 이미 화면 전체를 지배하는 세 전역을 순간적으로 밀어 올린다:
    ///   (1) 환경광 3색(RenderSettings.ambient*) — 그림자 속까지 포함해 씬 전체가 밝아진다.
    ///   (2) 스카이박스 _Exposure — 하늘 자체가 하얗게 뜬다("하늘이 번쩍"의 본체).
    ///   (3) 포스트 노출(ColorAdjustments.postExposure) — AtmospherePostFX가 담당한다(그 파일 소유).
    /// (1)(2)는 **LateUpdate에서만** 쓴다. 같은 프레임 안에서 DayNightCycle.Update(환경광/스카이박스
    /// 기록) → 본 LateUpdate(섬광 얹기) 순서가 보장되므로 렌더 직전의 마지막 승자가 여기가 된다.
    /// UnderwaterAmbience가 쓰는 "수중이면 LateUpdate에서 덮어쓴다" 규약과 정확히 같은 관용구이고,
    /// **수중일 때는 이쪽이 통째로 물러난다**(아래 IsCameraUnderwater) — 그래서 두 LateUpdate의
    /// 실행 순서가 어떻든 결과가 갈리지 않는다.
    /// 되돌리기: 섬광이 시작될 때 기준값을 찍어 두고 매 프레임 `기준값 + 증분`을 쓴다. 현재값에
    /// 더하는 방식(+=)은 DayNightCycle이 없는 구성에서 값이 프레임마다 누적되어 폭주하므로 쓰지 않는다.
    ///
    /// ── 결정성(rng 규율) ─────────────────────────────────────────────────────────
    /// 번개 타이밍/거리는 **이 클래스만의 System.Random**으로 뽑는다. 월드 생성 스트림
    /// (SeededRandomExtensions의 섬별 System.Random)도, WeatherSystem이 쓰는 UnityEngine.Random도
    /// 한 번도 소비하지 않으므로 같은 worldSeed의 섬 배치가 한 톨도 밀리지 않는다.
    /// 도메인 리로드에서 System.Random 필드는 null로 돌아오므로(AGENT_BRIEF 4장 2번) 쓰기 직전에
    /// 항상 null 검사로 다시 만든다 — "초기화됐으니 있겠지"를 전제하지 않는다.
    ///
    /// ── 비용 ─────────────────────────────────────────────────────────────────────
    /// 평상시(맑음): Update에서 float 비교 몇 개. 렌더 오브젝트는 꺼져 있어 드로우콜 0.
    /// 우천 시: 화면 물방울 오버레이 쿼드 1장 = **드로우콜 +1**(그게 전부다. 번개는 전역 값만 밀어
    /// 올리므로 드로우콜 0이다). 프레임당 힙 할당 0 — 섬광 스케줄은 미리 잡아 둔 고정 길이 배열에
    /// 덮어쓰고, 천둥 클립은 계층별로 세션당 한 번만 생성해 캐시한다.
    /// </summary>
    [DisallowMultipleComponent]
    public class StormEffects : MonoBehaviour
    {
        // ═══════════════════════════════════════════════════════════════════════════
        //  튜닝 값 (씬에 배치되지 않는 런타임 생성 컴포넌트라 이 코드 기본값이 유일한 진실이다)
        // ═══════════════════════════════════════════════════════════════════════════

        [Header("번개 발생 조건")]
        [Tooltip("이 강우 세기(WeatherSystem.RainIntensity01) 이상일 때만 번개가 친다.\n" +
            "0.6이면 rainFadeSeconds(5초) 페이드 기준으로 비가 시작되고 3초쯤 뒤부터 폭풍우 구간이다.")]
        [Range(0f, 1f)]
        public float stormThreshold = 0.6f;

        [Tooltip("폭풍 문턱(stormThreshold)일 때의 평균 번개 간격(초, 실시간).")]
        public float strikeIntervalAtThreshold = 50f;

        [Tooltip("최대 강우(1.0)일 때의 평균 번개 간격(초, 실시간). 강도에 비례해 잦아진다.")]
        public float strikeIntervalAtPeak = 20f;

        [Tooltip("번개 간격의 흔들림 비율(0.4면 평균의 ±40%). 규칙적인 메트로놈처럼 들리지 않게 한다.")]
        [Range(0f, 0.9f)]
        public float strikeIntervalJitter = 0.4f;

        [Header("섬광")]
        [Tooltip("한 번의 번개에서 연속으로 터지는 스트로크(깜빡임) 최대 횟수. 실제 번개는 같은 통로를" +
            " 여러 번 되친다 — 1~3회 사이가 가장 번개처럼 읽힌다.")]
        [Range(1, 3)]
        public int maxStrokes = 3;

        [Tooltip("스트로크 하나의 최소 지속 시간(초).")]
        public float strokeMinDuration = 0.08f;

        [Tooltip("스트로크 하나의 최대 지속 시간(초).")]
        public float strokeMaxDuration = 0.15f;

        [Tooltip("섬광 색(번개는 푸른빛이 도는 백색이다).")]
        public Color flashColor = new Color(0.72f, 0.80f, 1f, 1f);

        [Tooltip("섬광이 환경광(RenderSettings.ambient*)에 더하는 최대 밝기. 그림자 속까지 밝아진다.")]
        public float flashAmbientBoost = 2.2f;

        [Tooltip("섬광이 스카이박스 _Exposure에 곱하는 최대 증분(1.6이면 최대 2.6배). 하늘이 번쩍인다.")]
        public float flashSkyExposureBoost = 1.6f;

        [Header("천둥")]
        [Tooltip("번개까지의 최소 거리(m). 소리 지연 = 거리 / 음속이므로 340m면 약 1.0초 뒤에 들린다.")]
        public float thunderMinDistance = 340f;

        [Tooltip("번개까지의 최대 거리(m). 3000m면 약 8.7초 뒤에 낮게 우르릉거린다.")]
        public float thunderMaxDistance = 3000f;

        [Tooltip("음속(m/s). 지연 시간 = 거리 / 이 값.")]
        public float soundSpeed = 343f;

        [Tooltip("천둥 최대 음량 배율. 실제 재생 음량은 여기에 AudioManager.sfxVolume과 거리 감쇠가 곱해진다.")]
        [Range(0f, 1f)]
        public float thunderVolume = 0.85f;

        [Header("화면 물방울 (과하면 즉시 짜증 — 보수적으로 잡는다)")]
        [Tooltip("정면을 볼 때의 기본 세기. 최대 강우에서도 이 값이 상한이다(0이면 완전히 끈다).")]
        [Range(0f, 1f)]
        public float screenDropletStrength = 0.14f;

        [Tooltip("바로 위를 올려다볼 때 세기에 곱할 배율. 하늘을 보면 렌즈에 비를 그대로 맞는다.")]
        public float screenDropletLookUpBoost = 2.8f;

        [Tooltip("물방울 굴절 오프셋(화면 폭 대비 비율). 크게 잡으면 화면이 일렁여 멀미가 난다.")]
        [Range(0f, 0.08f)]
        public float screenDropletRefraction = 0.018f;

        [Tooltip("물방울이 화면을 덮는 밀도(1 = 텍스처 한 장이 화면 하나). 텍스처가 중앙을 성기게," +
            " 가장자리를 촘촘하게 그려 두었으므로 1이 설계값이다.")]
        [Range(0.25f, 4f)]
        public float screenDropletTiling = 1f;

        // ═══════════════════════════════════════════════════════════════════════════
        //  공개 상태
        // ═══════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// 지금 이 프레임의 섬광 세기 0~1(0 = 번개 없음). **Update에서 갱신**하므로 다른
        /// 컴포넌트의 LateUpdate가 읽어도 항상 이번 프레임 값이다(AtmospherePostFX가 포스트
        /// 노출/안개에 쓴다 — 두 LateUpdate의 실행 순서에 의존하지 않게 하려는 것이 목적이다).
        /// </summary>
        public static float FlashIntensity01 { get; private set; }

        /// <summary>현재 살아 있는 인스턴스(WeatherSystem.Active와 같은 패턴).</summary>
        public static StormEffects Active { get; private set; }

        // ═══════════════════════════════════════════════════════════════════════════
        //  정적 자원 캐시 (프레임 프로브 + R1 리셋 — UnderwaterVisuals와 같은 규칙)
        // ═══════════════════════════════════════════════════════════════════════════

        private static Shader rainScreenShader;
        private static Texture2D dropletTexture;
        private static float dropletSrgbFix;
        private static int probeFrame = -1;

        /// <summary>화면 전체 오버레이용 쿼드([-0.5,0.5]²) — 셰이더가 정점에서 NDC로 ×2한다.</summary>
        private static Mesh fullscreenQuad;

        /// <summary>
        /// 천둥 클립 캐시(0 = 가까움 / 1 = 중간 / 2 = 멂). **R1 리셋에서 비우지 않는다** —
        /// HideAndDontSave라 플레이 모드를 껐다 켜도 살아남으므로, 비우면 실행마다 새로 만들면서
        /// 이전 클립이 그대로 샌다(RainWetness.fallbackRippleMap과 같은 예외다).
        /// </summary>
        private static readonly AudioClip[] thunderClips = new AudioClip[3];

        /// <summary>
        /// 도메인 리로드를 끈 플레이 모드에서 static 캐시가 이전 실행의 파괴된 자원을 들고
        /// 시작하지 않게 되돌린다(R1 규약 — UnderwaterVisuals.ResetStaticCache와 동일).
        /// thunderClips는 위 주석대로 **의도적으로 제외**한다.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticCache()
        {
            rainScreenShader = null;
            dropletTexture = null;
            dropletSrgbFix = 0f;
            probeFrame = -1;
            fullscreenQuad = null;
            FlashIntensity01 = 0f;
            Active = null;
        }

        // ── 셰이더 프로퍼티 ID (문자열 해시를 매 프레임 다시 계산하지 않는다) ──
        private static readonly int DropletMapId = Shader.PropertyToID("_DropletMap");
        private static readonly int StrengthId = Shader.PropertyToID("_Strength");
        private static readonly int RefractionId = Shader.PropertyToID("_Refraction");
        private static readonly int TilingId = Shader.PropertyToID("_Tiling");
        private static readonly int SrgbFixId = Shader.PropertyToID("_SrgbFix");
        private static readonly int RainScreenTimeId = Shader.PropertyToID("_MG_RainScreenTime");
        private static readonly int ExposureId = Shader.PropertyToID("_Exposure");

        // ═══════════════════════════════════════════════════════════════════════════
        //  인스턴스 상태
        // ═══════════════════════════════════════════════════════════════════════════

        private Camera targetCamera;
        private WorldMapManager worldMap;

        /// <summary>번개 전용 난수 스트림. 월드 생성/날씨 rng와 완전히 분리돼 있다.</summary>
        private System.Random rng;

        /// <summary>다음 번개까지 남은 시간(초, unscaled).</summary>
        private float strikeTimer;

        /// <summary>폭풍우 구간(강우 ≥ stormThreshold)에 들어와 타이머를 걸어 둔 상태인지.</summary>
        private bool stormArmed;

        // ── 섬광 스케줄(고정 길이 배열 — 번개마다 덮어쓴다. 할당 0) ──
        private const int MaxStrokeSlots = 3;
        private readonly float[] strokeStart = new float[MaxStrokeSlots];
        private readonly float[] strokeDuration = new float[MaxStrokeSlots];
        private readonly float[] strokePeak = new float[MaxStrokeSlots];
        private int strokeCount;
        private bool flashRunning;
        private float flashElapsed;
        private float flashTotalSeconds;

        // ── 섬광 적용 상태(기준값 스냅샷 — 누적 폭주 방지) ──
        private bool flashApplied;
        private Color baseAmbientSky;
        private Color baseAmbientEquator;
        private Color baseAmbientGround;
        private Material flashSkybox;
        private float baseSkyExposure;

        // ── 천둥 ──
        private AudioSource thunderSource;
        private bool thunderPending;
        private float thunderPlayTime;
        private int thunderTier;
        private float thunderGain;

        // ── 화면 물방울 ──
        private GameObject dropletObject;
        private Material dropletMaterial;
        private MeshRenderer dropletRenderer;
        private bool dropletVisible;

        /// <summary>
        /// 씬이 로드될 때마다 스스로 생성한다. 프로젝트의 자기 부트스트랩 선례
        /// (WeatherSystem / DayNightCycle / RainWetness / UnderwaterVisuals — 16곳)와 완전히 같은
        /// SubsystemRegistration + sceneLoaded + 중복 가드 패턴이다. AfterSceneLoad는 재시작
        /// (SceneManager.LoadScene)에 다시 불리지 않아 재시작한 게임에서 연출이 조용히 사라진다.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Bootstrap()
        {
            SceneManager.sceneLoaded += (scene, mode) =>
            {
                if (FindAnyObjectByType<StormEffects>() != null)
                    return;

                var go = new GameObject("StormEffects");
                go.AddComponent<StormEffects>();
            };
        }

        /// <summary>정적 참조를 잡고 번개 난수 스트림을 만든다.</summary>
        private void Awake()
        {
            Active = this;
            EnsureRng();
        }

        /// <summary>천둥 재생용 AudioSource를 붙인다(AudioManager는 수정 금지라 자체 소스를 쓴다).</summary>
        private void Start()
        {
            thunderSource = gameObject.AddComponent<AudioSource>();
            thunderSource.playOnAwake = false;
            thunderSource.loop = false;
            // 2D 재생: 번개는 화면 밖 어딘가에서 오는 소리라 3D 감쇠/정위를 걸 지점이 없다.
            thunderSource.spatialBlend = 0f;
            thunderSource.bypassEffects = true;
        }

        /// <summary>씬 재로드로 이 인스턴스가 사라질 때 정적 참조와 런타임 머티리얼을 정리한다.</summary>
        private void OnDestroy()
        {
            if (Active == this)
                Active = null;

            // 섬광 도중에 파괴되면 밀어 올린 값이 그대로 굳는다 — 반드시 기준값으로 되돌린다.
            if (flashApplied)
            {
                RestoreFlashBaseline();
                flashApplied = false;
            }
            FlashIntensity01 = 0f;

            if (dropletMaterial != null)
            {
                Destroy(dropletMaterial);
                dropletMaterial = null;
            }
        }

        /// <summary>
        /// 매 프레임 (1) 번개 스케줄을 굴리고 (2) 섬광 곡선을 진행시키고 (3) 예약된 천둥을 재생한다.
        /// 전부 unscaledDeltaTime이다 — 엔딩/사망으로 timeScale이 0이 되어도 진행 중이던 번개가
        /// 공중에 얼어붙지 않게 한다(AGENT_BRIEF 4장 "연출은 unscaled로").
        /// **섬광 세기를 여기서(Update) 갱신하는 것이 중요하다.** AtmospherePostFX가 LateUpdate에서
        /// 이 값을 읽는데, 양쪽 다 LateUpdate면 실행 순서에 따라 한 프레임 늦은 값을 보게 된다.
        /// </summary>
        private void Update()
        {
            float dt = Time.unscaledDeltaTime;

            WeatherSystem weather = WeatherSystem.Active;
            float rain = weather != null ? Mathf.Clamp01(weather.RainIntensity01) : 0f;

            UpdateStrikeSchedule(rain, dt);
            UpdateFlashCurve(dt);
            UpdateThunder();
        }

        /// <summary>
        /// 화면/전역 연출을 LateUpdate에서 얹는다. DayNightCycle.Update가 환경광·스카이박스를
        /// 기록한 **뒤**에 실행되어야 마지막 승자가 되기 때문이다(클래스 주석의 소유권 설계).
        /// 수중이면 통째로 물러난다 — 그 프레임의 환경광 주인은 UnderwaterAmbience다.
        /// </summary>
        private void LateUpdate()
        {
            // 카메라 캐시는 여기 한 곳에서만 갱신한다(아래 두 함수는 읽기만 한다).
            RefreshCamera();

            bool underwater = IsCameraUnderwater();

            ApplyFlashToWorld(underwater);
            UpdateScreenDroplets(underwater);
        }

        // ═══════════════════════════════════════════════════════════════════════════
        //  1. 번개 — 스케줄
        // ═══════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// 폭풍우 구간(강우 ≥ stormThreshold)에서만 타이머를 굴려 번개를 친다.
        /// 평균 간격은 강도에 선형 비례한다 — 문턱(0.6)에서 50초, 최대 강우(1.0)에서 20초이고
        /// 거기에 ±strikeIntervalJitter(40%)의 흔들림이 곱해진다(실측 범위 12~70초).
        /// 폭풍우를 벗어나면 무장을 풀어, 다음에 다시 들어올 때 즉시 터지지 않게 한다.
        /// </summary>
        private void UpdateStrikeSchedule(float rain, float dt)
        {
            if (rain < stormThreshold)
            {
                stormArmed = false;
                return;
            }

            if (!stormArmed)
            {
                stormArmed = true;
                // 폭풍우에 막 들어온 순간의 첫 번개는 평균의 60% 지점에 걸어 둔다 —
                // "비가 거세지자마자 하늘이 한 번 번쩍인다"가 극적이라서다.
                strikeTimer = NextStrikeDelay(rain) * 0.6f;
                return;
            }

            strikeTimer -= dt;
            if (strikeTimer > 0f)
                return;

            strikeTimer = NextStrikeDelay(rain);
            TriggerStrike(rain);
        }

        /// <summary>다음 번개까지의 대기 시간(초). 강도 비례 평균 + 흔들림.</summary>
        private float NextStrikeDelay(float rain)
        {
            EnsureRng();

            float storm01 = Mathf.Clamp01(Mathf.InverseLerp(stormThreshold, 1f, rain));
            float mean = Mathf.Lerp(
                Mathf.Max(1f, strikeIntervalAtThreshold),
                Mathf.Max(1f, strikeIntervalAtPeak),
                storm01);

            float jitter = Mathf.Clamp01(strikeIntervalJitter);
            float factor = 1f - jitter + (float)rng.NextDouble() * jitter * 2f;
            return Mathf.Max(2f, mean * factor);
        }

        /// <summary>
        /// 번개 하나를 확정한다. 거리를 먼저 뽑고(멀수록 흔하게) 그 거리에서 섬광 밝기와 천둥
        /// 지연/음색이 전부 파생된다 — 가까우면 밝고 곧바로 날카롭게, 멀면 흐릿하고 한참 뒤 낮게.
        /// 스트로크 스케줄은 미리 잡아 둔 고정 길이 배열에 덮어쓰므로 힙 할당이 0이다.
        /// </summary>
        private void TriggerStrike(float rain)
        {
            EnsureRng();

            // 거리: u^0.65는 u보다 항상 크므로 분포가 **먼 쪽으로** 기운다. 가까운 번개(밝고 시끄럽다)가
            // 흔하면 금세 피곤해지므로, 대부분은 멀리 치고 가끔 코앞에서 터지게 만든다.
            float u = Mathf.Pow((float)rng.NextDouble(), 0.65f);
            float minDist = Mathf.Max(50f, thunderMinDistance);
            float maxDist = Mathf.Max(minDist + 50f, thunderMaxDistance);
            float distance = Mathf.Lerp(minDist, maxDist, u);
            float proximity = 1f - Mathf.Clamp01(Mathf.InverseLerp(minDist, maxDist, distance));

            // 섬광 밝기: 가까우면 1.0, 가장 멀면 0.42. 강우가 셀수록 구름이 낮아 조금 더 밝게 번진다.
            float peak = Mathf.Lerp(0.42f, 1f, proximity)
                * Mathf.Lerp(0.85f, 1f, Mathf.Clamp01(Mathf.InverseLerp(stormThreshold, 1f, rain)));

            // ── 스트로크(깜빡임) 구성 ────────────────────────────────────────────
            // 실제 번개는 같은 통로를 여러 번 되쳐서 "번쩍—번쩍" 끊긴다. 1회면 조명 스위치,
            // 2~3회면 번개로 읽힌다. 첫 스트로크가 가장 밝고 뒤로 갈수록 약해진다.
            int slots = Mathf.Clamp(maxStrokes, 1, MaxStrokeSlots);
            strokeCount = 1 + rng.Next(slots);

            float minDur = Mathf.Max(0.02f, strokeMinDuration);
            float maxDur = Mathf.Max(minDur + 0.01f, strokeMaxDuration);

            float cursor = 0f;
            for (int i = 0; i < strokeCount; i++)
            {
                float duration = Mathf.Lerp(minDur, maxDur, (float)rng.NextDouble());
                strokeStart[i] = cursor;
                strokeDuration[i] = duration;
                // 두 번째 이후 스트로크는 첫 번째의 45~85%. 항상 약해지므로 리듬이 읽힌다.
                strokePeak[i] = i == 0
                    ? peak
                    : peak * (0.45f + (float)rng.NextDouble() * 0.40f);

                // 스트로크 사이 간격 40~130ms. 이보다 길면 별개의 번개로 들린다.
                cursor += duration + 0.04f + (float)rng.NextDouble() * 0.09f;
            }

            flashRunning = true;
            flashElapsed = 0f;
            flashTotalSeconds = strokeStart[strokeCount - 1] + strokeDuration[strokeCount - 1];

            // ── 천둥 예약 ────────────────────────────────────────────────────────
            thunderPending = true;
            thunderPlayTime = Time.unscaledTime + distance / Mathf.Max(50f, soundSpeed);
            thunderTier = distance < 900f ? 0 : (distance < 2000f ? 1 : 2);
            // 거리 감쇠: 가장 먼 번개는 절반 음량. 계층별 음색 차이와 합쳐 거리감을 만든다.
            thunderGain = Mathf.Lerp(0.45f, 1f, proximity);
        }

        /// <summary>
        /// 섬광 곡선을 진행시킨다. 스트로크마다 "아주 빠른 상승(12%) → 지수형 소멸"이고,
        /// 겹치는 구간은 max로 합친다(더하면 스트로크가 겹칠 때 1을 넘어 계단이 생긴다).
        /// </summary>
        private void UpdateFlashCurve(float dt)
        {
            if (!flashRunning)
            {
                FlashIntensity01 = 0f;
                return;
            }

            flashElapsed += dt;
            if (flashElapsed > flashTotalSeconds)
            {
                flashRunning = false;
                FlashIntensity01 = 0f;
                return;
            }

            float best = 0f;
            for (int i = 0; i < strokeCount; i++)
            {
                float t = flashElapsed - strokeStart[i];
                if (t < 0f || t > strokeDuration[i])
                    continue;

                float p = t / Mathf.Max(0.0001f, strokeDuration[i]);
                // 상승 12% / 하강 88%. 하강에 2.2제곱을 씌워 초반에 확 꺼지고 잔광이 짧게 남는다.
                float env = p < 0.12f
                    ? p / 0.12f
                    : Mathf.Pow(1f - (p - 0.12f) / 0.88f, 2.2f);

                float value = env * strokePeak[i];
                if (value > best)
                    best = value;
            }

            FlashIntensity01 = Mathf.Clamp01(best);
        }

        // ═══════════════════════════════════════════════════════════════════════════
        //  2. 번개 — 화면에 얹기 (LateUpdate 전용)
        // ═══════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// 섬광을 환경광 3색과 스카이박스 노출에 얹는다. 클래스 주석의 소유권 설계대로
        /// **LateUpdate에서만 · 섬광이 살아 있을 때만 · 수면 위에서만** 쓴다.
        ///
        /// 기준값(baseline)을 섬광 시작 프레임에 한 번 찍고 이후 `기준값 + 증분`을 쓰는 이유:
        /// 현재값에 더하는 방식(+=)은 DayNightCycle이 매 프레임 되써 주는 구성에서는 옳지만,
        /// 그 컴포넌트가 없거나 driveAmbientLight를 끈 구성에서는 프레임마다 누적되어 화면이
        /// 순식간에 하얗게 탄다. 기준값 방식은 두 경우 모두에서 정확히 같은 결과를 낸다.
        /// </summary>
        private void ApplyFlashToWorld(bool underwater)
        {
            bool wantFlash = FlashIntensity01 > 0.001f && !underwater;

            if (wantFlash)
            {
                if (!flashApplied)
                {
                    CaptureFlashBaseline();
                    flashApplied = true;
                }

                float f = FlashIntensity01;
                Color add = flashColor * (f * Mathf.Max(0f, flashAmbientBoost));

                // 하늘 > 수평선 > 지면 순으로 약하게 — 번개는 위에서 오는 빛이다.
                RenderSettings.ambientSkyColor = baseAmbientSky + add;
                RenderSettings.ambientEquatorColor = baseAmbientEquator + add * 0.85f;
                RenderSettings.ambientGroundColor = baseAmbientGround + add * 0.5f;

                if (flashSkybox != null)
                {
                    flashSkybox.SetFloat(ExposureId,
                        baseSkyExposure * (1f + f * Mathf.Max(0f, flashSkyExposureBoost)));
                }
            }
            else if (flashApplied)
            {
                RestoreFlashBaseline();
                flashApplied = false;
            }
        }

        /// <summary>섬광 시작 프레임의 환경광/스카이박스 노출을 찍어 둔다.</summary>
        private void CaptureFlashBaseline()
        {
            baseAmbientSky = RenderSettings.ambientSkyColor;
            baseAmbientEquator = RenderSettings.ambientEquatorColor;
            baseAmbientGround = RenderSettings.ambientGroundColor;

            // DayNightCycle이 런타임에 복제해 꽂아 둔 인스턴스가 여기 잡힌다(원본 에셋이 아니다).
            // _Exposure가 없는 스카이박스(Skybox/Procedural이 아닌 것)면 건드리지 않는다.
            flashSkybox = RenderSettings.skybox;
            if (flashSkybox != null && flashSkybox.HasProperty(ExposureId))
                baseSkyExposure = flashSkybox.GetFloat(ExposureId);
            else
                flashSkybox = null;
        }

        /// <summary>섬광이 끝난 프레임에 기준값을 정확히 되돌린다(DayNightCycle이 있으면 어차피 덮어쓴다).</summary>
        private void RestoreFlashBaseline()
        {
            RenderSettings.ambientSkyColor = baseAmbientSky;
            RenderSettings.ambientEquatorColor = baseAmbientEquator;
            RenderSettings.ambientGroundColor = baseAmbientGround;

            if (flashSkybox != null)
            {
                flashSkybox.SetFloat(ExposureId, baseSkyExposure);
                flashSkybox = null;
            }
        }

        // ═══════════════════════════════════════════════════════════════════════════
        //  3. 천둥
        // ═══════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// 예약된 천둥의 재생 시각(섬광 + 거리/음속)이 되면 소리를 낸다.
        /// timeScale = 0에서도 진행해야 하므로 unscaledTime을 쓴다.
        /// </summary>
        private void UpdateThunder()
        {
            if (!thunderPending || Time.unscaledTime < thunderPlayTime)
                return;

            thunderPending = false;

            if (thunderSource == null)
                return;

            AudioClip clip = GetThunderClip(thunderTier);
            if (clip == null)
                return;

            EnsureRng();

            // 효과음 볼륨은 AudioManager가 PlayerPrefs로 관리한다(읽기만 한다 — 그 파일은 수정 금지).
            float sfx = AudioManager.Instance != null ? AudioManager.Instance.sfxVolume : 0.7f;
            // 같은 클립을 계속 쓰므로 피치를 ±10% 흔들어 "또 그 소리"가 되지 않게 한다.
            thunderSource.pitch = 0.9f + (float)rng.NextDouble() * 0.2f;
            thunderSource.PlayOneShot(clip, Mathf.Clamp01(sfx * thunderVolume * thunderGain));
        }

        /// <summary>
        /// 거리 계층별 천둥 클립을 가져온다(없으면 그 자리에서 절차 생성해 캐시한다).
        /// 생성은 세션당 계층별 1회뿐이고, 첫 번개가 칠 때까지는 아예 일어나지 않는다
        /// (맑은 날만 하다 끝난 세션은 오디오 합성 비용이 0이다).
        /// </summary>
        private static AudioClip GetThunderClip(int tier)
        {
            int index = Mathf.Clamp(tier, 0, thunderClips.Length - 1);
            if (thunderClips[index] != null)
                return thunderClips[index];

            AudioClip clip;
            switch (index)
            {
                // ── 정규화 피크가 계층마다 다른 이유(실측으로 잡은 값이다) ──────────
                // 피크 정규화는 **최댓값**만 맞추므로, 파형 모양이 다르면 체감 음량(RMS)이 뒤집힌다.
                // 처음에 near 0.30 / far 0.55로 두었더니 실측 RMS가 near 0.027 < far 0.073이 되어
                // "가까운 번개가 먼 번개보다 조용한" 정반대 결과가 나왔다 — 균열음이 뾰족해서
                // 피크를 혼자 차지하고 몸통이 눌린 탓이다. 아래 값은 거리 감쇠(thunderGain)까지
                // 곱한 최종 RMS가 near 0.076 > mid 0.053 > far 0.030이 되도록 다시 잡은 것이다.
                case 0:
                    // 가까운 번개: 날카로운 균열음(필터를 거의 안 거친 잡음)이 앞에 붙고 짧게 끝난다.
                    clip = BuildThunderClip("Thunder_Near", 91101, 2.6f, 0.012f, 0.16f, 0.85f, 0.85f);
                    break;
                case 1:
                    clip = BuildThunderClip("Thunder_Mid", 91102, 3.6f, 0.06f, 0.075f, 0.35f, 0.62f);
                    break;
                default:
                    // 먼 번개: 균열음 없이 천천히 부풀었다 오래 끄는 저역 우르릉.
                    clip = BuildThunderClip("Thunder_Far", 91103, 5.0f, 0.55f, 0.030f, 0f, 0.50f);
                    break;
            }

            thunderClips[index] = clip;
            return clip;
        }

        /// <summary>
        /// 천둥 한 발을 PCM으로 합성한다. AudioManager는 수정 금지이고
        /// ProceduralAudioClipGenerator에는 천둥이 없으므로, 그 파일의 관용구(고정 시드
        /// System.Random · 1극 저역통과 다단 · 피크 정규화 · 끝단 페이드)를 그대로 따라 여기서 만든다.
        ///
        /// 구성:
        ///  · 몸통 = 백색 잡음 → 1극 저역통과 3단. 계수가 작을수록 저역만 남아 "우르릉"이 된다
        ///    (가까움 0.16 → 거침/날카로움, 멂 0.030 → 둔탁한 저역).
        ///  · 균열음(crack) = 1단만 거른 밝은 잡음을 앞 0.4초에만 아주 빠른 감쇠로 얹는다.
        ///    가까운 번개에만 있다(먼 번개는 고역이 대기에서 다 흡수되어 도달하지 않는다).
        ///  · 진폭 변조 2중 주기(배수 관계 아님)로 소리가 일정하지 않게 넘실거린다.
        ///  · 되울림(comb) 2탭 — 구름/지형에 반사되어 굴러가는 꼬리를 만든다. 이득 &lt; 1이라 발산하지 않는다.
        /// </summary>
        /// <param name="name">클립 이름(디버깅용).</param>
        /// <param name="seed">고정 시드 — 같은 계층은 매 실행 완전히 같은 파형이다(결정적).</param>
        /// <param name="duration">전체 길이(초).</param>
        /// <param name="attack">최대 음량까지 부풀어 오르는 시간(초). 멀수록 길다.</param>
        /// <param name="lowPass">1극 저역통과 계수(작을수록 저역만 남는다).</param>
        /// <param name="crack">앞머리 균열음의 세기(0이면 없음).</param>
        /// <param name="peak">정규화 목표 피크(0~1).</param>
        private static AudioClip BuildThunderClip(string name, int seed, float duration,
            float attack, float lowPass, float crack, float peak)
        {
            const int SampleRate = 44100;
            int sampleCount = Mathf.Max(1, Mathf.RoundToInt(SampleRate * duration));
            float[] samples = new float[sampleCount];
            var random = new System.Random(seed);

            float attackSeconds = Mathf.Max(0.005f, attack);
            // 끝에서 e^-3.2(≈4%)까지 떨어지는 감쇠율. 길이가 달라도 꼬리 모양이 같게 유지된다.
            float decayRate = 3.2f / Mathf.Max(0.1f, duration - attackSeconds);

            float lp1 = 0f;
            float lp2 = 0f;
            float lp3 = 0f;
            for (int i = 0; i < sampleCount; i++)
            {
                float t = i / (float)SampleRate;
                float white = (float)random.NextDouble() * 2f - 1f;

                lp1 += (white - lp1) * lowPass;
                lp2 += (lp1 - lp2) * lowPass;
                lp3 += (lp2 - lp3) * lowPass;

                // 진폭 변조: 배수 관계가 아닌 두 주기(약 0.6초 / 2.9초)를 섞어 넘실거리게 한다.
                float am = 0.55f + 0.45f
                    * (Mathf.Sin(2f * Mathf.PI * 1.7f * t) * 0.5f + 0.5f)
                    * (Mathf.Sin(2f * Mathf.PI * 0.35f * t + 1.1f) * 0.5f + 0.5f);

                float envelope = t < attackSeconds
                    ? t / attackSeconds
                    : Mathf.Exp(-(t - attackSeconds) * decayRate);

                float body = lp3 * am;

                // 균열음: 1단만 거른 밝은 잡음. 0.4초 안에서 e^-14t로 급소멸한다.
                float crackValue = 0f;
                if (crack > 0f && t < 0.4f)
                    crackValue = lp1 * crack * Mathf.Exp(-t * 14f);

                samples[i] = body * envelope + crackValue;
            }

            // 되울림 2탭(0.19초 ×0.35 / 0.43초 ×0.20). 앞에서 뒤로 한 번만 훑으므로 안정적이다.
            AddCombTap(samples, SampleRate, 0.19f, 0.35f);
            AddCombTap(samples, SampleRate, 0.43f, 0.20f);

            // 피크 정규화 — 계층마다 합성 결과의 절대 크기가 달라서 여기서 맞춰야 음량이 예측 가능해진다.
            float maxAbs = 0f;
            for (int i = 0; i < sampleCount; i++)
            {
                float abs = samples[i] < 0f ? -samples[i] : samples[i];
                if (abs > maxAbs)
                    maxAbs = abs;
            }
            if (maxAbs > 0.0001f)
            {
                float gain = peak / maxAbs;
                for (int i = 0; i < sampleCount; i++)
                    samples[i] *= gain;
            }

            // 끝단 페이드(0.25초) — 클립이 뚝 끊기며 클릭이 나지 않게 한다.
            int fade = Mathf.Min(sampleCount, Mathf.RoundToInt(SampleRate * 0.25f));
            for (int i = 0; i < fade; i++)
            {
                float k = i / (float)fade;
                samples[sampleCount - 1 - i] *= k;
            }
            // 첫 샘플의 클릭 방지(2ms).
            int head = Mathf.Min(sampleCount, Mathf.RoundToInt(SampleRate * 0.002f));
            for (int i = 0; i < head; i++)
                samples[i] *= i / (float)head;

            AudioClip clip = AudioClip.Create(name, sampleCount, 1, SampleRate, false);
            clip.SetData(samples, 0);
            // 플레이 모드를 껐다 켜도 살아남게 한다(위 thunderClips 캐시 주석의 전제).
            clip.hideFlags = HideFlags.HideAndDontSave;
            return clip;
        }

        /// <summary>되울림 한 탭을 제자리에서 더한다(이득 &lt; 1이라 발산하지 않는다).</summary>
        private static void AddCombTap(float[] samples, int sampleRate, float delaySeconds, float gain)
        {
            int delay = Mathf.RoundToInt(sampleRate * delaySeconds);
            if (delay <= 0 || delay >= samples.Length)
                return;

            for (int i = delay; i < samples.Length; i++)
                samples[i] += samples[i - delay] * gain;
        }

        // ═══════════════════════════════════════════════════════════════════════════
        //  4. 화면(렌즈) 물방울
        // ═══════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// 화면 물방울 오버레이를 갱신한다.
        ///
        /// 세기 = 기본값 × 강우 세기 × **실외 계수** × 시선각 배율.
        ///  · 실외 계수는 WeatherSystem.ShelteredFactor01을 그대로 쓴다 — 빗줄기 파티클을 끄는 것과
        ///    **같은 판정 하나**를 재사용하므로 "빗줄기는 멎었는데 화면에는 물방울이 남는" 어긋남이
        ///    원리적으로 생기지 않는다(그쪽이 0.8초 페이드까지 이미 걸어 두었다).
        ///  · 시선각: 카메라 전방의 y성분(=올려다보는 정도)으로 1 → screenDropletLookUpBoost까지 올린다.
        ///    정면을 볼 때가 기본값이고 하늘을 볼 때만 강해진다("과하면 즉시 짜증" 요구의 핵심).
        ///  · 수중이면 0 — 물속 화면은 UnderwaterVisuals의 것이다.
        /// 세기가 0이면 렌더러를 꺼서 드로우콜을 0으로 만든다.
        /// </summary>
        private void UpdateScreenDroplets(bool underwater)
        {
            float strength = 0f;
            if (!underwater && targetCamera != null && screenDropletStrength > 0f)
            {
                WeatherSystem weather = WeatherSystem.Active;
                if (weather != null)
                {
                    float rain = Mathf.Clamp01(weather.RainIntensity01);
                    float outdoor = Mathf.Clamp01(weather.ShelteredFactor01);
                    float up01 = Mathf.Clamp01(targetCamera.transform.forward.y);
                    strength = screenDropletStrength * rain * outdoor
                        * Mathf.Lerp(1f, Mathf.Max(1f, screenDropletLookUpBoost), up01);
                }
            }

            if (strength <= 0.001f)
            {
                if (dropletVisible && dropletRenderer != null)
                {
                    dropletRenderer.enabled = false;
                    dropletVisible = false;
                }
                return;
            }

            EnsureDropletOverlay();
            if (dropletMaterial == null || dropletRenderer == null)
                return;

            dropletMaterial.SetFloat(StrengthId, Mathf.Clamp01(strength));
            dropletMaterial.SetFloat(RefractionId, Mathf.Max(0f, screenDropletRefraction));
            dropletMaterial.SetFloat(TilingId, Mathf.Clamp(screenDropletTiling, 0.25f, 4f));
            // 프로젝트 관례대로 셰이더 시계는 Time.time이다(타이틀/일시정지에서 멈춘다 —
            // MGOcean _MG_WaveTime · MGCaustics _MG_CausticsTime · RainWetness _MG_RainTime과 동일).
            dropletMaterial.SetFloat(RainScreenTimeId, Time.time);

            if (!dropletVisible)
            {
                dropletRenderer.enabled = true;
                dropletVisible = true;
            }
        }

        /// <summary>
        /// 오버레이 쿼드를 만든다(최초 1회). 셰이더나 텍스처가 없으면 아무것도 만들지 않고 조용히
        /// 넘어간다 — 다음 프레임 프로브에서 자원이 잡히면 그때 만들어진다(실패를 영구 래치하지
        /// 않는다는 AGENT_BRIEF 4장 3번 규칙).
        /// 카메라 자식이라 셰이더가 클립 좌표를 직접 내도 항상 화면을 덮고, 메시 바운즈를 10km로
        /// 잡아 어떤 자세에서도 프러스텀 컬링되지 않는다(UnderwaterVisuals.BuildCaustics와 동일).
        /// </summary>
        private void EnsureDropletOverlay()
        {
            if (dropletObject != null && dropletRenderer != null)
                return;

            EnsureResourcesProbed();
            if (rainScreenShader == null || dropletTexture == null || targetCamera == null)
                return;

            dropletObject = new GameObject("RainScreenDroplets");
            dropletObject.transform.SetParent(targetCamera.transform, false);

            var filter = dropletObject.AddComponent<MeshFilter>();
            filter.sharedMesh = GetFullscreenQuad();

            var renderer = dropletObject.AddComponent<MeshRenderer>();
            // 카메라가 파괴돼 자식만 사라진 경우 여기로 다시 들어온다 — 옛 머티리얼을 먼저 지워야
            // 재생성 때마다 한 장씩 쌓이지 않는다.
            if (dropletMaterial != null)
                Destroy(dropletMaterial);

            dropletMaterial = new Material(rainScreenShader);
            dropletMaterial.hideFlags = HideFlags.HideAndDontSave;
            dropletMaterial.SetTexture(DropletMapId, dropletTexture);
            dropletMaterial.SetFloat(SrgbFixId, dropletSrgbFix);
            dropletMaterial.SetFloat(StrengthId, 0f);
            renderer.sharedMaterial = dropletMaterial;

            // 순수 장식 렌더러 — 그림자/프로브를 전부 꺼서 추가 패스를 만들지 않는다.
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            renderer.enabled = false;

            dropletRenderer = renderer;
            dropletVisible = false;
        }

        /// <summary>
        /// 셰이더/텍스처를 프레임당 한 번만 프로브한다(실패를 영구 캐시하지 않는다).
        /// 물방울 텍스처는 **데이터 텍스처**(RG = 노멀)라 sRGB로 임포트되면 값이 밀린다 —
        /// 런타임에 직접 물어보고 셰이더에 보정 스위치를 넘긴다(MGRainScreen.shader 헤더 참고).
        /// </summary>
        private static void EnsureResourcesProbed()
        {
            if (probeFrame == Time.frameCount)
                return;
            probeFrame = Time.frameCount;

            if (rainScreenShader == null)
                rainScreenShader = Resources.Load<Shader>("Shaders/MGRainScreen");

            if (dropletTexture == null)
            {
                dropletTexture = Resources.Load<Texture2D>("Textures/rain_droplet");
                if (dropletTexture != null)
                    dropletSrgbFix = ProbeSrgb(dropletTexture) ? 1f : 0f;
            }
        }

        /// <summary>
        /// 텍스처가 sRGB로 임포트됐는지 조사한다. Texture.isDataSRGB는 비교적 최근 API라
        /// 직접 부르면 없는 버전에서 프로젝트 전체가 컴파일 에러로 멈춘다 — 연출 하나 때문에
        /// 그 위험을 지지 않고 리플렉션으로 조회한다(RainWetness.ProbeSrgb와 완전히 같은 방식).
        /// 조회는 세션당 한 번이라 리플렉션 비용은 논외이고, 실패하면 false(= 보정 없음)다.
        /// </summary>
        private static bool ProbeSrgb(Texture tex)
        {
            if (tex == null)
                return false;

            try
            {
                PropertyInfo info = typeof(Texture).GetProperty("isDataSRGB",
                    BindingFlags.Public | BindingFlags.Instance);
                if (info == null || info.PropertyType != typeof(bool))
                    return false;

                object value = info.GetValue(tex, null);
                return value is bool b && b;
            }
            catch (System.Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// 화면 전체 오버레이용 쿼드. 로컬 좌표가 [-0.5, 0.5]²라 셰이더가 ×2해 NDC로 쓴다.
        /// 바운즈는 10km 상자 — 카메라 자식이므로 어떤 자세에서도 프러스텀에 걸린다.
        /// </summary>
        private static Mesh GetFullscreenQuad()
        {
            if (fullscreenQuad != null)
                return fullscreenQuad;

            var mesh = new Mesh { name = "MGRainScreenQuad" };
            mesh.hideFlags = HideFlags.HideAndDontSave;
            mesh.vertices = new[]
            {
                new Vector3(-0.5f, -0.5f, 0f),
                new Vector3( 0.5f, -0.5f, 0f),
                new Vector3(-0.5f,  0.5f, 0f),
                new Vector3( 0.5f,  0.5f, 0f),
            };
            mesh.triangles = new[] { 0, 2, 1, 2, 3, 1 };
            mesh.bounds = new Bounds(Vector3.zero, new Vector3(10000f, 10000f, 10000f));
            fullscreenQuad = mesh;
            return mesh;
        }

        // ═══════════════════════════════════════════════════════════════════════════
        //  공용 헬퍼
        // ═══════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// 카메라 참조를 유지한다. 카메라가 파괴/재생성되면(파괴된 오브젝트 == null 규칙) 오버레이
        /// 자식도 함께 사라지므로 캐시를 통째로 비워 다음 프레임에 새 카메라 밑에 다시 만들게 한다.
        /// </summary>
        private void RefreshCamera()
        {
            if (targetCamera != null)
                return;

            targetCamera = Camera.main;
            dropletObject = null;
            dropletRenderer = null;
            dropletVisible = false;
        }

        /// <summary>
        /// 카메라가 해수면 아래인지 직접 판정한다. UnderwaterAmbience.IsUnderwater를 읽지 않는
        /// 이유는 UnderwaterVisuals가 적어 둔 것과 같다 — 그 값은 LateUpdate에서 쓰이는데
        /// LateUpdate끼리는 실행 순서가 보장되지 않아 프레임에 따라 한 프레임 늦은 값을 보게 된다.
        /// 같은 입력(Camera.main.y · WorldMapManager.seaLevel)으로 직접 재면 순서 계약이 아예 없다.
        /// </summary>
        private bool IsCameraUnderwater()
        {
            // 못 찾았을 때만 가끔 재시도한다(정상 경로에서는 탐색 비용/할당이 0이다).
            if (worldMap == null && Time.frameCount % 60 == 0)
                worldMap = FindAnyObjectByType<WorldMapManager>();

            if (targetCamera == null || worldMap == null)
                return false;

            return targetCamera.transform.position.y < worldMap.seaLevel;
        }

        /// <summary>
        /// 번개 전용 난수 스트림을 보증한다. 도메인 리로드에서 System.Random 필드는 null로
        /// 돌아오므로(AGENT_BRIEF 4장 2번 — 새끼 곰 AI가 그것 때문에 매 프레임 예외를 던졌다)
        /// 쓰기 직전마다 검사한다. 시드는 실행 시각 기반이라 월드 생성 시드와 무관하다.
        /// </summary>
        private void EnsureRng()
        {
            if (rng != null)
                return;

            rng = new System.Random(unchecked((int)System.DateTime.Now.Ticks));
        }
    }
}
