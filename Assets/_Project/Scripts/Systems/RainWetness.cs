using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MakeGame.Systems
{
    /// <summary>
    /// 비에 **세상이 젖는** 연출의 단일 소스 드라이버.
    ///
    /// 진단: 예전에는 비가 빗줄기 파티클 + 광량/안개뿐이라, 비가 그친 직후의 세상이 비가 오기 전과
    /// 픽셀 하나 다르지 않았다. 빗방울이 실제로 남기는 흔적(젖은 표면 · 수면 파문)이 없으면
    /// 아무리 파티클을 늘려도 "화면 위에 얹힌 선"으로만 읽힌다. 이 클래스가 그 흔적의 입력을 만든다.
    ///
    /// ── 밀어 주는 셰이더 전역 ────────────────────────────────────────────────────
    ///  · _MG_Wetness       (float)  : 젖음 0~1. **비대칭 곡선**(아래) - 빨리 젖고 아주 천천히 마른다.
    ///  · _MG_RainIntensity (float)  : 지금 내리는 비의 세기 0~1 = WeatherSystem.RainIntensity01 그대로.
    ///                                 빗방울 파문의 밀도/세기에 쓴다(젖음과 달리 즉시 따라간다).
    ///  · _MG_RainTime      (float)  : 파문 시계(초) = Time.time.
    ///  · _MG_RippleParams  (float4) : x = 파문 타일링(1/m), y = 파문 속도(회/초),
    ///                                 z = sRGB 보정 스위치(0/1 - 아래 "sRGB 방어"),
    ///                                 w = 파문 노멀 세기 배율.
    ///  · _MG_RippleMap     (Texture): 빗방울 파문 텍스처(RG 노멀 xy / B 위상 / A 마스크).
    ///
    /// 셰이더 두 곳(MGShoreline = 모래 캡, MGOcean = 바다)이 이 전역만 읽는다. **머티리얼에는
    /// 아무 것도 넣지 않는다** - 바다 머티리얼은 WorldMapManager가 만들어서 이 락 밖이고,
    /// 전역이면 두 셰이더가 같은 값을 공짜로 본다(SetGlobal 호출은 프레임당 3회 남짓).
    ///
    /// ── 타이틀 화면 정지 계약 ────────────────────────────────────────────────────
    /// 시계는 Time.time이라 Time.timeScale = 0에서 멈춘다(_MG_WaveTime/_MG_ShoreTime/_MG_WindTime과
    /// 정확히 같은 계약). 셰이더는 내장 _Time을 쓰지 않는다.
    /// **단, 젖음값 자체는 unscaledDeltaTime으로 진행한다** - 입력인 WeatherSystem.RainIntensity01이
    /// 이미 unscaled로 페이드되므로(그쪽 [B22] 주석), 여기만 scaled로 두면 timeScale = 0 구간에서
    /// 목표만 움직이고 젖음이 굳어 두 값이 갈라진다.
    ///
    /// ── 젖음 곡선(비대칭) ────────────────────────────────────────────────────────
    /// 사실감의 핵심은 "빨리 젖고 아주 천천히 마른다"는 비대칭 하나다. 대칭으로 만들면 비가 그치는
    /// 순간 세상이 같이 마르면서 비가 **연출이 아니라 스위치**로 보인다.
    ///   젖을 때 : 목표(RainIntensity01)로 시간상수 wetSeconds(2.5s)의 지수 접근.
    ///   마를 때 : 시간상수 drySeconds(36s) 지수 감쇠 **+ 선형 바닥항**(dryFloorSeconds 140s).
    /// 선형 바닥항을 더한 이유: 순수 지수는 꼬리가 영원히 0에 닿지 않아, 비가 그친 지 5분이 지나도
    /// _MG_Wetness가 0.01~0.02로 남는다. 그 값은 화면에서 안 보이지만 "완전히 마른 상태"라는 계약이
    /// 사라져 디버깅이 어려워진다. 바닥항이 있으면 유한 시간에 정확히 목표에 닿는다.
    /// 실측 곡선(WeatherSystem.rainFadeSeconds = 5초 페이드를 그대로 먹인 60fps 수치 시뮬레이션):
    ///   젖음 0 → 0.5 : 4.6초 / → 0.9 : 8.7초 / → 0.99 : 14.4초
    ///   마름 1 → 0.5 : 20.3초 / → 0.25 : 34.7초 / → 0.1 : 47.3초 / → 0.0 : 59.2초
    /// 즉 **마르는 데 젖는 데의 약 6.8배**가 걸린다. 맑음 단계가 최소 90초라(WeatherSystem)
    /// 다음 비가 오기 전에는 반드시 완전히 마른다 - 젖음이 누적되어 굳는 실패 모드가 없다.
    /// 최소 우천 시간이 40초라 어떤 소나기도 젖음을 1.0까지 올린다(위 시뮬레이션 확인).
    ///
    /// ── sRGB 방어 (Textures/rain_ripple) ─────────────────────────────────────────
    /// 파문 텍스처는 **데이터 텍스처**다(RG = 접선공간 노멀 xy, 0.5 = 평평). 임포트 설정에서
    /// sRGB 체크가 켜져 있으면 셰이더가 읽는 값이 감마 해제를 거쳐 0.5 → 0.214로 밀리고,
    /// (rg×2-1)이 0이 아니라 -0.57이 되어 **파문 영역 전체가 한쪽으로 기운 판**이 된다.
    /// .meta는 이 작업의 편집 범위 밖이라 설정을 여기서 고칠 수 없다. 그래서 런타임에 텍스처가
    /// sRGB로 읽히는지 **직접 물어보고**(Texture.isDataSRGB - 리플렉션으로 조회해 API가 없는
    /// 버전에서도 컴파일/실행이 깨지지 않게 한다) 셰이더에 보정 스위치를 넘긴다. 셰이더는 스위치가
    /// 1일 때 pow(c, 1/2.2)로 저장값을 되돌린다(0.214 → 0.496 ≈ 0.5).
    /// 조회는 부트스트랩 때 딱 한 번이고, 실패하면 0(보정 없음 = Linear 임포트 가정)이다.
    ///
    /// ── 텍스처가 없을 때 ─────────────────────────────────────────────────────────
    /// 로드 실패면 **RG = 0.5(평평한 노멀) · A = 0(마스크 0)** 인 1×1 텍스처를 대신 넣는다.
    /// 셰이더의 파문 진폭이 A에 비례하므로 A = 0 → 진폭 0이 되어, 분기도 경고도 없이 파문만
    /// 조용히 빠지고 젖음(_MG_Wetness)은 그대로 동작한다(_FoamMap과 같은 우아한 열화 계약).
    /// ※ Texture2D.blackTexture를 쓰면 안 된다 - 그쪽은 **불투명** 검정이라 A = 1로 마스크가 살아
    ///   있고 RG = 0이라 (rg×2-1) = -1이 된다. 즉 지면·수면 전체에 최대로 기운 노멀이 깔린다.
    ///   "검정이니 안전하겠지"가 정확히 틀리는 자리라 1×1을 직접 만든다.
    ///
    /// ── 부트스트랩 ───────────────────────────────────────────────────────────────
    /// 씬에 인스턴스가 없다. 프로젝트의 자기 부트스트랩 선례(ShorelineWaves / OceanWaves /
    /// GrassFieldDriver - 16곳)와 동일한 SubsystemRegistration + sceneLoaded + 중복 가드다.
    /// 정적 캐시는 같은 훅에서 리셋한다(R1 규약 - 도메인 리로드를 끈 플레이 모드 대비).
    ///
    /// ── 비용 ─────────────────────────────────────────────────────────────────────
    /// 프레임당 SetGlobalFloat 3회(값이 바뀐 프레임에만 SetGlobalVector 1회). 힙 할당 0
    /// (Vector4는 구조체이고 WeatherSystem.Active는 캐시된 정적 참조다). 렌더 오브젝트를 만들지
    /// 않으므로 드로우콜 증가 0 - 젖음/파문은 전부 기존 머티리얼의 셰이더 안에서 일어난다.
    /// </summary>
    [DisallowMultipleComponent]
    public class RainWetness : MonoBehaviour
    {
        // ── 젖음 곡선 파라미터 ───────────────────────────────────────────────────

        [Header("젖음 곡선 (비대칭 - 이 비대칭이 사실감의 핵심이다)")]
        [Tooltip("젖는 시간상수(초). 목표 세기까지 지수 접근한다. 2.5면 약 8.7초에 0.9에 닿는다.")]
        public float wetSeconds = 2.5f;

        [Tooltip("마르는 시간상수(초). 크게 잡을수록 비가 그친 뒤에도 오래 축축하게 남는다.")]
        public float drySeconds = 36f;

        [Tooltip("마름의 선형 바닥항(초). 남은 젖음을 이 시간에 걸쳐 추가로 깎아 **유한 시간에** 0으로 만든다.\n" +
            "0이면 지수 꼬리가 영원히 남는다(0.01~0.02에서 굳는다).")]
        public float dryFloorSeconds = 140f;

        // ── 파문 파라미터 ────────────────────────────────────────────────────────

        [Header("빗방울 파문 (MGShoreline / MGOcean 공용)")]
        [Tooltip("파문 텍스처 타일링(1/m). 0.32 = 한 타일이 월드 3.1m.")]
        public float rippleTiling = 0.32f;

        [Tooltip("파문 한 번이 나고 사라지는 속도(회/초). 텍스처의 B 채널이 파문마다 위상을 어긋내 준다.")]
        public float rippleSpeed = 1.6f;

        [Tooltip("파문 노멀의 전체 세기 배율. 0이면 파문이 완전히 꺼진다(젖음은 그대로).")]
        [Range(0f, 3f)]
        public float rippleStrength = 1f;

        // ── 셰이더 전역 프로퍼티 ID ──────────────────────────────────────────────
        // 셰이더 쪽은 이 넷을 Properties 블록 **밖**·CBUFFER(UnityPerMaterial) **밖**에 선언한다.
        // Properties에 넣으면 머티리얼 프로퍼티가 되어 전역 설정이 무시되고(머티리얼 값이 이긴다),
        // CBUFFER 안에 넣으면 SRP Batcher 레이아웃과 충돌한다(MGOcean/MGShoreline과 같은 규약).
        private static readonly int WetnessProperty = Shader.PropertyToID("_MG_Wetness");
        private static readonly int RainIntensityProperty = Shader.PropertyToID("_MG_RainIntensity");
        private static readonly int RainTimeProperty = Shader.PropertyToID("_MG_RainTime");
        private static readonly int RippleParamsProperty = Shader.PropertyToID("_MG_RippleParams");
        private static readonly int RippleMapProperty = Shader.PropertyToID("_MG_RippleMap");

        /// <summary>마지막으로 밀어 넣은 파문 파라미터. 값이 바뀐 프레임에만 다시 민다.</summary>
        private static Vector4 pushedRippleParams;

        /// <summary>현재 젖음값 0~1. 정적으로 두는 이유는 아래 Wetness 프로퍼티 주석 참고.</summary>
        private static float wetness;

        /// <summary>파문 텍스처가 sRGB로 읽히는지(1 = 보정 필요). 부트스트랩에서 한 번만 조사한다.</summary>
        private static float rippleSrgbFix;

        /// <summary>파문 텍스처를 이미 전역에 밀어 넣었는지. 텍스처는 세션 내내 바뀌지 않는다.</summary>
        private static bool rippleMapPushed;

        /// <summary>
        /// 파문 텍스처가 없을 때 대신 넣는 1×1(RG 0.5 = 평평 · A 0 = 마스크 0).
        /// HideAndDontSave라 플레이 모드를 껐다 켜도 살아남으므로 **부트스트랩에서 null로 되돌리지
        /// 않는다** - 되돌리면 매 실행마다 새로 만들면서 이전 것이 그대로 새는다(R1 리셋의 예외).
        /// </summary>
        private static Texture2D fallbackRippleMap;

        /// <summary>씬에 살아 있는 드라이버. 없으면 null(그래도 전역 기본값은 부트스트랩이 밀어 둔다).</summary>
        public static RainWetness Active { get; private set; }

        /// <summary>
        /// 현재 젖음 0~1(셰이더의 _MG_Wetness와 같은 값). 게임플레이 수치에는 관여하지 않는
        /// 순수 연출값이며, 다른 시스템(예: 발자국·미끄러짐 연출)이 같은 수를 보고 싶을 때를 위해 연다.
        /// 인스턴스가 아니라 정적으로 두는 이유는 WeatherSystem.RainIntensity01과 달리 이 값을
        /// 읽는 쪽이 드라이버 생성 순서를 신경 쓰지 않아도 되게 하기 위해서다(없으면 0 = 마름).
        /// </summary>
        public static float Wetness => wetness;

        /// <summary>파문 시계(초). 셰이더의 _MG_RainTime과 같은 값. timeScale = 0에서 멈춘다.</summary>
        public static float RainTime => Time.time;

        // ── 수명 주기 ────────────────────────────────────────────────────────────

        /// <summary>
        /// 씬이 로드될 때마다 드라이버를 하나 만든다(중복 가드 포함). 동시에 정적 캐시를 리셋하고
        /// 전역 기본값(마름 · 비 없음 · 파문 텍스처)을 즉시 밀어 넣는다 - 드라이버의 첫 Update보다
        /// 먼저 그려지는 프레임에서 파문 텍스처가 미바인딩으로 남는 것을 막기 위해서다.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Bootstrap()
        {
            // 도메인 리로드를 끈 플레이 모드에서 이전 실행 값이 남는 것을 막는다(R1 리셋 훅).
            Active = null;
            wetness = 0f;
            rippleSrgbFix = 0f;
            rippleMapPushed = false;
            pushedRippleParams = Vector4.zero;

            Shader.SetGlobalFloat(WetnessProperty, 0f);
            Shader.SetGlobalFloat(RainIntensityProperty, 0f);
            Shader.SetGlobalFloat(RainTimeProperty, 0f);
            PushRippleMap();
            PushRippleParams(0.32f, 1.6f, 1f);

            SceneManager.sceneLoaded += (scene, mode) =>
            {
                if (FindAnyObjectByType<RainWetness>() != null)
                    return;

                var go = new GameObject("RainWetness");
                go.AddComponent<RainWetness>();
            };
        }

        private void Awake()
        {
            Active = this;
            PushRippleMap();
            PushRippleParams(rippleTiling, rippleSpeed, rippleStrength);
        }

        private void OnDestroy()
        {
            if (Active == this)
                Active = null;
        }

        /// <summary>
        /// 매 프레임 젖음을 적분해 셰이더 전역에 민다.
        /// WeatherSystem이 아직 없으면(부트스트랩 순서/타이틀 화면) 목표를 0으로 보고 그대로 마른다 -
        /// 예외도 로그도 없다.
        /// </summary>
        private void Update()
        {
            var weather = WeatherSystem.Active;
            float target = weather != null ? Mathf.Clamp01(weather.RainIntensity01) : 0f;

            // 입력(RainIntensity01)이 unscaled로 페이드되므로 여기도 unscaled로 맞춘다.
            // 그러지 않으면 timeScale = 0(엔딩/사망 화면)에서 목표만 움직이고 젖음이 굳는다.
            wetness = StepWetness(wetness, target, Time.unscaledDeltaTime,
                wetSeconds, drySeconds, dryFloorSeconds);

            Shader.SetGlobalFloat(WetnessProperty, wetness);

            // [실사감 E1] 셰이더 전역만으로는 URP Lit을 쓰는 구조물·바위·통나무가 젖지 않는다.
            // 그쪽은 머티리얼 프로퍼티로 직접 젖힌다. 실제로 칠하는 것은 젖음이 눈에 띄게
            // 달라졌을 때뿐이라(SurfaceWetness.ApplyThreshold) 매 프레임 불러도 된다.
            SurfaceWetness.Apply(wetness);
            Shader.SetGlobalFloat(RainIntensityProperty, target);
            Shader.SetGlobalFloat(RainTimeProperty, Time.time);
            PushRippleParams(rippleTiling, rippleSpeed, rippleStrength);
        }

        // ── 젖음 적분 ────────────────────────────────────────────────────────────

        /// <summary>
        /// 젖음값을 한 스텝 진행한다(순수 함수 - 상태를 읽지도 쓰지도 않아 그대로 시뮬레이션/검산할 수 있다).
        ///
        /// 올라갈 때와 내려갈 때 **다른 식**을 쓰는 것이 이 함수의 전부다.
        ///   · 목표가 위면 : 시간상수 wetTau의 지수 접근. 소나기가 시작되면 수 초 만에 젖는다.
        ///   · 목표가 아래면 : 시간상수 dryTau의 지수 감쇠 + 선형 바닥항(1/floorSeconds per second).
        ///     선형 항이 지수 꼬리를 잘라 유한 시간에 정확히 목표에 닿게 한다.
        /// dt에 대해 지수를 쓰므로 프레임률이 달라져도 곡선이 같다(MoveTowards처럼 dt 선형이 아니다).
        /// </summary>
        public static float StepWetness(float current, float target, float dt,
            float wetTau, float dryTau, float floorSeconds)
        {
            if (dt <= 0f)
                return Mathf.Clamp01(current);

            current = Mathf.Clamp01(current);
            target = Mathf.Clamp01(target);

            if (target >= current)
            {
                float tau = Mathf.Max(wetTau, 0.01f);
                current += (target - current) * (1f - Mathf.Exp(-dt / tau));
            }
            else
            {
                float tau = Mathf.Max(dryTau, 0.01f);
                float gap = (current - target) * Mathf.Exp(-dt / tau);
                if (floorSeconds > 0.01f)
                    gap -= dt / floorSeconds;
                current = target + Mathf.Max(gap, 0f);
            }

            return Mathf.Clamp01(current);
        }

        // ── 전역 밀어 넣기 ───────────────────────────────────────────────────────

        /// <summary>
        /// 파문 파라미터를 정리해 전역에 넣는다. 인스펙터에서 만지지 않는 한 값이 바뀌지 않으므로
        /// 실제로 달라진 프레임에만 SetGlobalVector를 부른다.
        /// </summary>
        private static void PushRippleParams(float tiling, float speed, float strength)
        {
            var next = new Vector4(
                Mathf.Max(tiling, 0.001f),
                Mathf.Max(speed, 0f),
                rippleSrgbFix,
                Mathf.Max(strength, 0f));

            if (next == pushedRippleParams)
                return;

            pushedRippleParams = next;
            Shader.SetGlobalVector(RippleParamsProperty, next);
        }

        /// <summary>
        /// 파문 텍스처를 전역 슬롯에 넣는다(세션당 한 번). 로드에 실패하면 검은 텍스처를 넣는다 -
        /// 셰이더의 파문 진폭이 A 채널에 비례하므로 A = 0 → 파문 0이 되어 젖음만 남는다.
        /// **전역 텍스처를 아예 안 넣는 선택지는 없다.** 미바인딩 슬롯은 플랫폼마다 읽히는 값이
        /// 달라(검정이 아닐 수 있다) RG 채널이 0.5가 아닌 값으로 읽히면 지면 전체에 기운 노멀이 깔린다.
        /// </summary>
        private static void PushRippleMap()
        {
            if (rippleMapPushed)
                return;

            // [로드 규칙] Resources.Load는 정적 필드 초기자에서 부르지 않는다(초기자는 Unity가
            // Load를 막는 시점에 돌 수 있다 - IslandMeshGenerator.MeshLibrary의 같은 주석 참고).
            var tex = Resources.Load<Texture2D>("Textures/rain_ripple");
            if (tex == null)
            {
                Shader.SetGlobalTexture(RippleMapProperty, GetFallbackRippleMap());
                // 실패를 굳히지 않는다 - 텍스처가 나중에 들어오면 다음 씬 로드에서 다시 살핀다.
                return;
            }

            rippleSrgbFix = ProbeSrgb(tex) ? 1f : 0f;
            Shader.SetGlobalTexture(RippleMapProperty, tex);
            rippleMapPushed = true;
        }

        /// <summary>
        /// 파문 텍스처가 없을 때 쓰는 1×1 대체 텍스처(RG 0.5 = 평평한 노멀 · A 0 = 세기 마스크 0).
        /// A가 0이라 셰이더의 파문 진폭이 정확히 0이 되어, 파문만 빠지고 젖음은 그대로 남는다.
        /// linear = true로 만들어 임포터의 감마 설정과 무관하게 0.5가 0.5로 읽히게 한다
        /// (어차피 A = 0이라 값이 쓰이지는 않지만, 계약을 지키는 쪽이 나중에 덜 헷갈린다).
        /// </summary>
        private static Texture2D GetFallbackRippleMap()
        {
            if (fallbackRippleMap != null)
                return fallbackRippleMap;

            fallbackRippleMap = new Texture2D(1, 1, TextureFormat.RGBA32, false, true);
            fallbackRippleMap.name = "MG_RippleFallback";
            fallbackRippleMap.hideFlags = HideFlags.HideAndDontSave;
            fallbackRippleMap.SetPixel(0, 0, new Color(0.5f, 0.5f, 0.5f, 0f));
            // 두 번째 인자 true = CPU 사본 해제(읽을 일이 없다).
            fallbackRippleMap.Apply(false, true);
            return fallbackRippleMap;
        }

        /// <summary>
        /// 텍스처가 sRGB로 임포트됐는지 조사한다(클래스 헤더의 "sRGB 방어" 참고).
        ///
        /// Texture.isDataSRGB를 **리플렉션으로** 읽는 이유: 이 프로퍼티는 비교적 최근에 추가된
        /// API라, 직접 부르면 없는 버전에서 프로젝트 전체가 컴파일 에러로 멈춘다. 파문 하나 때문에
        /// 그 위험을 지지 않는다. 조회는 부트스트랩에서 딱 한 번이라 리플렉션 비용도 논외다.
        /// 프로퍼티가 없으면 false(= Linear 임포트 가정 = 보정 없음)를 돌려준다.
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
                // 조회 자체가 실패해도 연출 하나 때문에 예외를 올리지 않는다(보정 없음으로 떨어진다).
                return false;
            }
        }
    }
}
