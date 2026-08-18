using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MakeGame.Systems
{
    /// <summary>
    /// 비 소리를 **세 층으로 갈라** 재생하는 드라이버. 전부 절차 생성 클립이고 외부 음원은 없다.
    ///
    /// ── 왜 층을 나누는가 ───────────────────────────────────────────────────────
    /// 기존 빗소리는 AudioManager.rainSource 한 채널에 "쏴아" 루프(CreateRainAmbientLoop) 하나를
    /// 통째로 틀고 끄는 것이 전부였다. 그러면 약한 비와 폭우가 **볼륨만** 다르고, 야자숲 한가운데든
    /// 물가든 지붕 아래든 완전히 같은 소리가 난다. 실제로 비가 "고급스럽게" 들리는 이유는 셋이다.
    ///   층 1 — 강우 자체의 스펙트럼이 세기에 따라 바뀐다(가는 쉬익 ↔ 묵직한 웅웅).
    ///   층 2 — 주변에서 비가 **무엇에 부딪히는가**(잎 / 물 / 지붕)가 계속 섞여 들어온다.
    ///   층 3 — 지붕 아래로 들어가면 고역이 통째로 깎여 먹먹해진다. 이 전환 하나가 "비를 피했다"는
    ///          안도감을 만든다.
    /// 이 셋을 각각 클립/필터/가중치로 나눠 담은 것이 이 파일이다.
    ///
    /// ── 기존 단일 채널과의 관계(중요) ────────────────────────────────────────
    /// WeatherSystem은 비가 시작·종료될 때 AudioManager.StartRainAmbient/StopRainAmbient를 부르고,
    /// 두 파일 모두 이 웨이브에서 **읽기 전용**이라 그 호출을 없앨 수 없다. 그대로 두면 낡은 한 겹이
    /// 아래에 계속 깔려 층 1과 이중으로 울린다. 그래서 이 드라이버는 자기 베드 층이 **실제로 재생을
    /// 시작한 뒤에만** AudioManager.StopRainAmbient()를 저빈도로 불러 낡은 채널을 눌러 둔다
    /// (public API 호출일 뿐 남의 파일을 고치는 것이 아니다). 이 드라이버가 어떤 이유로든 소리를
    /// 못 내면 억제도 하지 않으므로, 최악의 경우 예전 소리가 그대로 남는다(우아한 열화).
    /// → [요청] 디렉터: 다음 웨이브에서 WeatherSystem의 Start/StopRainAmbient 호출과 AudioManager의
    ///   clipRainAmbient(12초 ≈ 2MB)를 제거하면 억제 코드와 낭비 메모리를 함께 걷어낼 수 있다.
    ///
    /// ── 성능 ───────────────────────────────────────────────────────────────────
    /// · AudioSource 4개(베드 1 + 재질 3). 낙수 효과음 소스 1개는 RainDrips가 따로 들고 있다(합계 5).
    /// · 클립은 **세션당 1회** 생성해 정적 캐시(아래 주석 참고). 씬 재로드에도 다시 굽지 않는다.
    /// · 주변 재질 판정은 0.5초에 물리 질의 **1회**(OverlapSphereNonAlloc). 매 프레임 물리 질의 0.
    /// · Update 본문은 산술과 필터 대입뿐이라 프레임당 힙 할당 0이다(재질 분류 결과는 캐시).
    /// · 페이드는 전부 unscaledDeltaTime — 오디오는 timeScale 0에서도 계속 울린다(AudioManager 선례).
    /// </summary>
    public class RainAudio : MonoBehaviour
    {
        // ── 클립 규격 ────────────────────────────────────────────────────────────

        /// <summary>층 1 베드 루프 길이(초). 8초 × 44100Hz = 352,800샘플 ≈ 1.41MB(float 배열 기준).</summary>
        private const float BedLoopSeconds = 8f;

        /// <summary>층 2 재질 루프 길이(초). 5초 × 44100Hz = 220,500샘플 ≈ 0.88MB × 3장.</summary>
        private const float MaterialLoopSeconds = 5f;

        // ── 층 1: 세기에 따른 저역/고역 균형 ────────────────────────────────────

        /// <summary>약한 비(세기 0)의 하이패스 컷오프(Hz). 몸통을 깎아 "쉬쉬하는 고역"만 남긴다.</summary>
        private const float BedHighPassLight = 900f;

        /// <summary>폭우(세기 1)의 하이패스 컷오프(Hz). 사실상 열어 둬 묵직한 저역이 전부 돌아온다.</summary>
        private const float BedHighPassHeavy = 40f;

        // ── 층 3: 실내 감쇠 ──────────────────────────────────────────────────────

        /// <summary>실외의 로우패스 컷오프(Hz). 22000 = 필터가 사실상 없는 상태.</summary>
        private const float OutdoorLowPass = 22000f;

        /// <summary>실내의 로우패스 컷오프(Hz). 620Hz면 "쏴아"의 s 발음이 사라지고 먹먹해진다.</summary>
        private const float IndoorLowPass = 620f;

        /// <summary>
        /// 실내에서 **지붕 타격 층만** 따로 쓰는 로우패스 컷오프(Hz). 내가 들어와 있는 그 지붕을
        /// 때리는 소리는 지붕에 가로막힌 소리가 아니라 **바로 머리 위의 소리**라, 다른 층처럼
        /// 620Hz까지 깎으면 오히려 비현실적이다. 2500Hz면 또렷함은 남기고 실내 톤에는 섞인다.
        /// </summary>
        private const float IndoorRoofLowPass = 2500f;

        /// <summary>실내에서 베드/잎/물 층에 곱할 볼륨.</summary>
        private const float IndoorVolumeScale = 0.45f;

        /// <summary>실내에서 지붕 층에 곱할 볼륨(오히려 커진다 — 머리 위에서 두드리기 때문).</summary>
        private const float IndoorRoofVolumeScale = 1.35f;

        /// <summary>
        /// 실내 전환 계수가 따라가는 최대 속도(초). 실제 전환 시간은 WeatherSystem.indoorFadeSeconds
        /// (기본 0.8초)가 정한다 — 이 값은 그쪽이 0으로 설정돼 톡 튀는 경우를 막는 상한일 뿐이라
        /// 정상 설정에서는 지연을 더하지 않는다(입력 변화율 1.25/s < 상한 1/0.35 = 2.86/s).
        /// 결과적으로 "지붕 아래로 들어가면 0.8초에 걸쳐 먹먹해진다" = 요구 구간 0.5~1.0초 안.
        /// </summary>
        private const float IndoorFadeCapSeconds = 0.35f;

        // ── 볼륨 ─────────────────────────────────────────────────────────────────

        /// <summary>베드 층의 기준 볼륨(bgmVolume에 곱해진다).</summary>
        private const float BedVolume = 0.85f;

        /// <summary>재질 층 3개가 **합쳐서** 가질 수 있는 최대 볼륨(bgmVolume에 곱해진다).</summary>
        private const float MaterialVolume = 0.55f;

        /// <summary>
        /// 세기 → 볼륨 곡선의 지수. 1이면 약한 비가 거의 안 들리고, 0.7이면 세기 0.3에서도
        /// 최대의 43%로 들린다 — 소나기의 시작을 놓치지 않게 하는 값이다.
        /// </summary>
        private const float IntensityVolumeExponent = 0.7f;

        /// <summary>AudioManager를 못 찾았을 때 쓰는 배경음 볼륨(AudioManager.bgmVolume의 기본값과 같다).</summary>
        private const float FallbackBgmVolume = 0.3f;

        // ── 재질 판정 ────────────────────────────────────────────────────────────

        /// <summary>주변 재질을 다시 재는 주기(초). 걷는 속도에서 0.5초면 충분하다.</summary>
        private const float MaterialSampleInterval = 0.5f;

        /// <summary>재질 판정 반경(m). "내 주변에서 나는 소리"이므로 시야가 아니라 청감 거리다.</summary>
        private const float MaterialSampleRadius = 10f;

        /// <summary>재질 판정용 콜라이더 버퍼 크기. 사전 할당이라 질의 자체는 힙 할당 0이다.</summary>
        private const int MaterialBufferSize = 48;

        /// <summary>재질 가중치가 목표로 수렴하는 시간 상수(초). 걸으면서 숲↔물가를 오갈 때 부드럽게 섞인다.</summary>
        private const float MaterialBlendTau = 1.2f;

        /// <summary>이 개수의 야자/초목 콜라이더가 반경 안에 있으면 잎 가중치가 최대가 된다.</summary>
        private const float LeafSaturationCount = 4f;

        /// <summary>이 개수의 건축 조각/쉼터가 반경 안에 있으면 지붕 가중치가 최대가 된다.</summary>
        private const float RoofSaturationCount = 3f;

        /// <summary>해수면 위 이 높이(m)까지는 물 가중치가 최대다(물가/모래톱).</summary>
        private const float WaterFullHeight = 0.5f;

        /// <summary>해수면 위 이 높이(m)를 넘으면 물 가중치가 0이다(섬 안쪽 · terrainMaxHeight 8 기준 절반).</summary>
        private const float WaterZeroHeight = 4f;

        /// <summary>카메라 눈높이(m). 발밑 높이를 얻기 위해 카메라 y에서 뺀다(WeatherSystem 주석의 1.6m와 동일).</summary>
        private const float EyeHeight = 1.6f;

        /// <summary>빗소리 판정에 쓰는 물리 레이어(Default=0만). WeatherSystem.RainCollisionMask와 같은 근거.</summary>
        private const int MaterialLayerMask = 1 << 0;

        // ── 정적 클립 캐시 ───────────────────────────────────────────────────────

        /// <summary>
        /// 절차 생성 클립 4장(베드 + 잎/물/지붕). **세션당 1회만 굽는다** — 4장 합쳐 약 4MB에
        /// 생성 시간이 수십 ms라, 씬을 재로드할 때마다 다시 구우면 로딩이 눈에 띄게 길어진다.
        ///
        /// ⚠️ R1 리셋 훅에서 **이 캐시를 비우지 않는다.** 클립에 HideFlags.HideAndDontSave를 줘서
        /// 씬 언로드/UnloadUnusedAssets에도 살아남게 만들어 두었기 때문에, 캐시를 null로 되돌리면
        /// 살아 있는 클립을 놓쳐 버리고 새로 구운 것이 그 위에 쌓인다(= 순수한 누수).
        /// RainWetness.fallbackRippleMap이 정확히 같은 이유로 R1 리셋의 예외인 것을 그대로 따른다.
        /// 도메인 리로드가 켜져 있으면 어차피 정적 필드와 클립이 함께 사라지므로 문제가 없다.
        /// </summary>
        private static AudioClip bedClip;
        private static AudioClip leafClip;
        private static AudioClip waterClip;
        private static AudioClip roofClip;

        /// <summary>씬에 살아 있는 드라이버(없으면 null). 낙수 쪽이 실내 계수를 공유해 쓸 수 있게 연다.</summary>
        public static RainAudio Active { get; private set; }

        /// <summary>
        /// 현재 실내 계수 0~1(0 = 실외, 1 = 완전히 지붕 아래). 층 3이 실제로 쓰는 값 그대로다.
        /// 드라이버가 없으면 0(실외)으로 읽힌다.
        /// </summary>
        public static float Indoor01 => Active != null ? Active.indoorLevel : 0f;

        // ── 인스턴스 상태 ────────────────────────────────────────────────────────

        private AudioSource bedSource;
        private AudioSource leafSource;
        private AudioSource waterSource;
        private AudioSource roofSource;

        private AudioLowPassFilter bedLowPass;
        private AudioHighPassFilter bedHighPass;
        private AudioLowPassFilter leafLowPass;
        private AudioLowPassFilter waterLowPass;
        private AudioLowPassFilter roofLowPass;

        private WeatherSystem weather;
        private WorldMapManager worldMap;
        private Transform listener;

        private float indoorLevel;
        private float leafWeight;
        private float waterWeight;
        private float roofWeight;
        private float materialTimer;
        private bool playing;

        /// <summary>재질 판정용 사전 할당 콜라이더 버퍼(질의 힙 할당 0).</summary>
        private readonly Collider[] materialBuffer = new Collider[MaterialBufferSize];

        /// <summary>
        /// 콜라이더가 어느 재질인지(0 = 무관, 1 = 초목, 2 = 건축/쉼터) 한 번 판정해 캐시한다.
        /// Transform.name 읽기는 네이티브 → 매니지드 문자열 마샬링이라 호출마다 문자열을 하나 만든다.
        /// 섬의 야자·건축 조각은 자리에서 움직이지 않으므로 처음 본 것만 이름을 읽고 이후에는
        /// 딕셔너리 조회(할당 0)로 끝난다 — 판정 주기가 0.5초여도 굳이 매번 문자열을 만들 이유가 없다.
        /// 섬을 옮겨 다니며 표가 무한정 커지지 않도록 상한을 두고 넘으면 통째로 비운다.
        /// </summary>
        private readonly Dictionary<Transform, byte> materialKindCache = new Dictionary<Transform, byte>(128);

        private const int MaterialKindCacheLimit = 256;

        private const byte KindOther = 0;
        private const byte KindFoliage = 1;
        private const byte KindStructure = 2;

        // ── 수명 주기 ────────────────────────────────────────────────────────────

        /// <summary>
        /// 씬이 로드될 때마다(최초 시작이든 재시작이든) 드라이버를 하나 만든다.
        /// AfterSceneLoad는 재시작(SceneManager.LoadScene)에 다시 불리지 않아 빗소리가 조용히
        /// 사라진다 — UnderwaterAmbience.Bootstrap이 라이브 테스트로 확인한 사실이라 그대로 따른다.
        /// 이미 살아 있는 인스턴스가 있으면 건너뛴다(중복 생성 가드).
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Bootstrap()
        {
            // R1 리셋 훅: 도메인 리로드를 끈 플레이 모드에서 이전 실행의 파괴된 인스턴스를 들고
            // 시작하지 않게 한다. **클립 캐시는 일부러 건드리지 않는다**(필드 주석 참고).
            Active = null;

            SceneManager.sceneLoaded += (scene, mode) =>
            {
                if (FindAnyObjectByType<RainAudio>() != null)
                    return;

                var go = new GameObject("RainAudio");
                go.AddComponent<RainAudio>();
            };
        }

        /// <summary>클립을 준비하고(세션 1회) 층별 소스·필터를 만든다.</summary>
        private void Awake()
        {
            Active = this;
            EnsureClips();
            BuildSources();
        }

        /// <summary>참조 캐시. 못 찾은 것은 Update에서 저빈도로 재시도한다(UnderwaterAmbience와 같은 규칙).</summary>
        private void Start()
        {
            weather = WeatherSystem.Active;
            worldMap = FindAnyObjectByType<WorldMapManager>();
            var cam = Camera.main;
            listener = cam != null ? cam.transform : null;
        }

        /// <summary>정적 참조를 정리한다. 클립은 세션 캐시라 파괴하지 않는다(필드 주석 참고).</summary>
        private void OnDestroy()
        {
            if (Active == this)
                Active = null;

            materialKindCache.Clear();
        }

        /// <summary>
        /// 절차 생성 클립 4장을 세션당 1회만 굽는다. HideAndDontSave를 줘서 씬 전환의
        /// UnloadUnusedAssets에 쓸려나가지 않게 한다(EffectBuilder가 공용 머티리얼에 쓰는 것과 같은 이유).
        /// </summary>
        private static void EnsureClips()
        {
            if (bedClip == null)
            {
                bedClip = ProceduralAudioClipGenerator.CreateRainBedLoop(BedLoopSeconds);
                bedClip.hideFlags = HideFlags.HideAndDontSave;
            }

            if (leafClip == null)
            {
                leafClip = ProceduralAudioClipGenerator.CreateRainLeafLoop(MaterialLoopSeconds);
                leafClip.hideFlags = HideFlags.HideAndDontSave;
            }

            if (waterClip == null)
            {
                waterClip = ProceduralAudioClipGenerator.CreateRainWaterLoop(MaterialLoopSeconds);
                waterClip.hideFlags = HideFlags.HideAndDontSave;
            }

            if (roofClip == null)
            {
                roofClip = ProceduralAudioClipGenerator.CreateRainRoofLoop(MaterialLoopSeconds);
                roofClip.hideFlags = HideFlags.HideAndDontSave;
            }
        }

        /// <summary>
        /// 층별 AudioSource를 **각각 별도의 자식 오브젝트**에 만든다. 한 오브젝트에 소스를 여러 개
        /// 붙이면 같은 오브젝트의 AudioLowPassFilter가 어느 소스에 걸리는지가 정의되지 않기 때문이다
        /// (층 3의 실내 감쇠가 층마다 다른 컷오프를 써야 하므로 이 분리는 선택이 아니라 필수다).
        /// 전부 spatialBlend 0(2D) — 비는 특정 지점이 아니라 사방에서 오는 소리다.
        /// </summary>
        private void BuildSources()
        {
            bedSource = CreateLayer("Layer1_RainBed", bedClip, out bedLowPass);
            bedHighPass = bedSource.gameObject.AddComponent<AudioHighPassFilter>();
            bedHighPass.cutoffFrequency = BedHighPassLight;

            leafSource = CreateLayer("Layer2_OnLeaves", leafClip, out leafLowPass);
            waterSource = CreateLayer("Layer2_OnWater", waterClip, out waterLowPass);
            roofSource = CreateLayer("Layer2_OnRoof", roofClip, out roofLowPass);
        }

        /// <summary>한 층(자식 오브젝트 + 루프 AudioSource + 로우패스)을 만든다.</summary>
        private AudioSource CreateLayer(string name, AudioClip clip, out AudioLowPassFilter lowPass)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);

            var source = go.AddComponent<AudioSource>();
            source.clip = clip;
            source.loop = true;
            source.playOnAwake = false;
            source.spatialBlend = 0f;
            source.dopplerLevel = 0f;
            source.volume = 0f;

            lowPass = go.AddComponent<AudioLowPassFilter>();
            lowPass.cutoffFrequency = OutdoorLowPass;

            return source;
        }

        // ── 구동 ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// 매 프레임 세기·실내 계수·재질 가중치를 반영해 4개 층의 볼륨과 필터 컷오프를 갱신한다.
        /// 비가 완전히 그치면(세기 0) 소스를 정지해 DSP 비용을 0으로 만든다.
        /// </summary>
        private void Update()
        {
            float dt = Time.unscaledDeltaTime;

            // 참조는 못 찾았을 때만 저빈도로 재시도한다(정상 경로 탐색 비용 0).
            if (weather == null)
                weather = WeatherSystem.Active;
            if (listener == null && Time.frameCount % 60 == 0)
            {
                var cam = Camera.main;
                listener = cam != null ? cam.transform : null;
            }
            if (worldMap == null && Time.frameCount % 60 == 0)
                worldMap = FindAnyObjectByType<WorldMapManager>();

            float intensity = weather != null ? Mathf.Clamp01(weather.RainIntensity01) : 0f;

            // 실내 계수: WeatherSystem이 이미 indoorFadeSeconds(0.8초)로 페이드한 값을 그대로 쓰고,
            // 그쪽이 0으로 설정돼 톡 튀는 경우만 아래 상한이 막는다(상수 주석 참고).
            float indoorTarget = weather != null ? 1f - Mathf.Clamp01(weather.ShelteredFactor01) : 0f;
            indoorLevel = Mathf.MoveTowards(indoorLevel, indoorTarget, dt / IndoorFadeCapSeconds);

            // 비가 완전히 그쳤다: 소스를 멈추고 아무것도 하지 않는다.
            if (intensity <= 0.0005f)
            {
                if (playing)
                    StopAll();
                return;
            }

            if (!playing)
                StartAll();

            UpdateMaterialWeights(dt);

            float bgm = AudioManager.Instance != null ? AudioManager.Instance.bgmVolume : FallbackBgmVolume;
            float loud = Mathf.Pow(intensity, IntensityVolumeExponent);
            float indoorScale = Mathf.Lerp(1f, IndoorVolumeScale, indoorLevel);
            float roofIndoorScale = Mathf.Lerp(1f, IndoorRoofVolumeScale, indoorLevel);

            // ── 층 1: 볼륨 + 저역/고역 균형 ──────────────────────────────────────
            bedSource.volume = bgm * BedVolume * loud * indoorScale;
            bedHighPass.cutoffFrequency = LerpHz(BedHighPassLight, BedHighPassHeavy, intensity);

            // ── 층 2: 재질 가중치별 볼륨 ─────────────────────────────────────────
            float matBase = bgm * MaterialVolume * loud;
            leafSource.volume = matBase * leafWeight * indoorScale;
            waterSource.volume = matBase * waterWeight * indoorScale;
            roofSource.volume = matBase * roofWeight * roofIndoorScale;

            // ── 층 3: 실내 감쇠(로우패스 컷오프) ─────────────────────────────────
            float indoorCutoff = LerpHz(OutdoorLowPass, IndoorLowPass, indoorLevel);
            bedLowPass.cutoffFrequency = indoorCutoff;
            leafLowPass.cutoffFrequency = indoorCutoff;
            waterLowPass.cutoffFrequency = indoorCutoff;
            roofLowPass.cutoffFrequency = LerpHz(OutdoorLowPass, IndoorRoofLowPass, indoorLevel);
        }

        /// <summary>
        /// 주파수를 **로그 축**에서 보간한다. 22000 → 620Hz를 선형으로 내리면 앞의 90%가 귀에
        /// 아무 변화도 없고 마지막 순간에 갑자기 먹먹해진다 — 사람의 음높이 감각이 로그이기 때문이다.
        /// 로그 보간이면 t가 균일하게 움직이는 동안 "점점 먹먹해진다"가 균일하게 들린다.
        /// </summary>
        private static float LerpHz(float fromHz, float toHz, float t)
        {
            return Mathf.Exp(Mathf.Lerp(Mathf.Log(Mathf.Max(10f, fromHz)), Mathf.Log(Mathf.Max(10f, toHz)),
                Mathf.Clamp01(t)));
        }

        /// <summary>
        /// 재질 가중치(잎/물/지붕)를 갱신한다. 목표값은 0.5초에 한 번만 다시 재고, 매 프레임에는
        /// 그 목표로 지수 접근만 한다(프레임당 물리 질의 0 · 힙 할당 0).
        ///
        /// 판정 방법과 비용:
        ///  · 잎/지붕 — 반경 10m에 OverlapSphereNonAlloc **1회**(사전 할당 버퍼 48). 잡힌 콜라이더를
        ///    이름 접두사로 분류하되 결과를 Transform별로 캐시하므로, 자리에서 안 움직이는 야자·건축
        ///    조각은 처음 한 번만 문자열을 읽는다.
        ///  · 물 — 물리 질의를 아예 쓰지 않는다. 해수면(WorldMapManager.seaLevel) 대비 발밑 높이만
        ///    보면 된다. 이 게임의 섬은 terrainMaxHeight 8m의 완만한 열대 섬이라, 물가(0.5m 이하)와
        ///    섬 안쪽(4m 이상)을 높이 하나로 충분히 가른다. 잠수 중이면 무조건 최대다.
        ///  · 세 가중치는 합이 1을 넘으면 정규화한다 — 안 하면 야자숲 속 물가에서 재질 층이 겹쳐
        ///    베드보다 커진다.
        /// </summary>
        private void UpdateMaterialWeights(float dt)
        {
            materialTimer -= dt;
            if (materialTimer <= 0f)
            {
                materialTimer = MaterialSampleInterval;
                SampleMaterialTargets();

                // 낡은 단일 채널 억제(클래스 주석 참고). 우리 베드가 실제로 울리고 있을 때만 부른다.
                var audio = AudioManager.Instance;
                if (audio != null && bedSource != null && bedSource.isPlaying)
                    audio.StopRainAmbient();
            }

            // 지수 접근(프레임률과 무관한 곡선). dt가 큰 프레임에서도 오버슈트하지 않는다.
            float k = 1f - Mathf.Exp(-dt / MaterialBlendTau);
            leafWeight += (leafTarget - leafWeight) * k;
            waterWeight += (waterTarget - waterWeight) * k;
            roofWeight += (roofTarget - roofWeight) * k;
        }

        private float leafTarget;
        private float waterTarget;
        private float roofTarget;

        /// <summary>0.5초마다 한 번 주변을 훑어 재질 가중치의 **목표값**을 만든다(보간은 호출자가 한다).</summary>
        private void SampleMaterialTargets()
        {
            leafTarget = 0f;
            waterTarget = 0f;
            roofTarget = 0f;

            if (listener == null)
                return;

            Vector3 probe = listener.position;

            // ── 잎 / 지붕: 물리 질의 1회 ─────────────────────────────────────────
            int hits = Physics.OverlapSphereNonAlloc(probe, MaterialSampleRadius, materialBuffer,
                MaterialLayerMask, QueryTriggerInteraction.Ignore);

            int foliage = 0;
            int structure = 0;
            for (int i = 0; i < hits; i++)
            {
                Collider col = materialBuffer[i];
                if (col == null)
                    continue;

                byte kind = ClassifyMaterial(col.transform);
                if (kind == KindFoliage)
                    foliage++;
                else if (kind == KindStructure)
                    structure++;
            }

            leafTarget = Mathf.Clamp01(foliage / LeafSaturationCount);
            roofTarget = Mathf.Clamp01(structure / RoofSaturationCount);

            // 지붕 아래에 실제로 들어와 있으면 지붕 층은 무조건 최대다(콜라이더가 몇 개 잡혔든
            // "머리 위에 지붕이 있다"가 사실이므로). WeatherSystem의 실내 판정을 그대로 신뢰한다.
            if (weather != null && weather.IsIndoors)
                roofTarget = 1f;

            // ── 물: 물리 질의 없이 높이만으로 ────────────────────────────────────
            if (UnderwaterAmbience.IsUnderwater)
            {
                waterTarget = 1f;
            }
            else if (worldMap != null)
            {
                float footY = probe.y - EyeHeight;
                float above = footY - worldMap.seaLevel;
                waterTarget = 1f - Mathf.Clamp01((above - WaterFullHeight) / (WaterZeroHeight - WaterFullHeight));
            }

            // 실내에서는 잎/물이 거의 들리지 않는다(볼륨 감쇠와 별개로 섞임 자체를 줄인다).
            if (weather != null && weather.IsIndoors)
            {
                leafTarget *= 0.25f;
                waterTarget *= 0.25f;
            }

            // 합이 1을 넘으면 정규화(재질 층 전체가 베드를 덮지 않게).
            float sum = leafTarget + waterTarget + roofTarget;
            if (sum > 1f)
            {
                leafTarget /= sum;
                waterTarget /= sum;
                roofTarget /= sum;
            }
        }

        /// <summary>
        /// 콜라이더 소유 Transform이 초목인지 건축물인지 판정한다(결과는 캐시 — 필드 주석 참고).
        /// 이름 규약: 초목은 IslandMeshGenerator.Vegetation이 붙이는 "Veg_" 접두사(야자 = "Veg_Palm"),
        /// 건축 조각은 BuildPieceVisualBuilder.CreateSolid가 붙이는 "BuildPiece_" 접두사,
        /// 쉼터는 프리팹 이름에 "Shelter"가 들어간다(런타임 인스턴스는 "Shelter(Clone)").
        /// </summary>
        private byte ClassifyMaterial(Transform t)
        {
            if (t == null)
                return KindOther;

            byte cached;
            if (materialKindCache.TryGetValue(t, out cached))
                return cached;

            if (materialKindCache.Count >= MaterialKindCacheLimit)
                materialKindCache.Clear(); // 섬을 옮겨 다녀도 표가 무한정 커지지 않게 한다

            string name = t.name;
            byte kind = KindOther;
            if (name.StartsWith("Veg_"))
                kind = KindFoliage;
            else if (name.StartsWith("BuildPiece_") || name.StartsWith("Shelter"))
                kind = KindStructure;

            materialKindCache[t] = kind;
            return kind;
        }

        /// <summary>네 층을 동시에 재생 시작한다(볼륨은 Update가 곧바로 채운다).</summary>
        private void StartAll()
        {
            playing = true;
            materialTimer = 0f; // 첫 프레임에 재질을 즉시 한 번 재게 한다
            PlayIfReady(bedSource);
            PlayIfReady(leafSource);
            PlayIfReady(waterSource);
            PlayIfReady(roofSource);
        }

        /// <summary>네 층을 정지한다(비가 완전히 그쳤을 때 DSP 비용을 0으로).</summary>
        private void StopAll()
        {
            playing = false;
            StopIfPlaying(bedSource);
            StopIfPlaying(leafSource);
            StopIfPlaying(waterSource);
            StopIfPlaying(roofSource);
        }

        private static void PlayIfReady(AudioSource source)
        {
            if (source != null && source.clip != null && !source.isPlaying)
            {
                source.volume = 0f; // 시작 볼륨 0 - 같은 프레임의 Update가 목표 볼륨을 넣는다
                source.Play();
            }
        }

        private static void StopIfPlaying(AudioSource source)
        {
            if (source != null && source.isPlaying)
                source.Stop();
        }
    }
}
