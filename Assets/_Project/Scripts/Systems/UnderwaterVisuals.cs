using UnityEngine;
using UnityEngine.SceneManagement;

namespace MakeGame.Systems
{
    /// <summary>
    /// 수중 시각 연출 3종(카우스틱 · 갓레이 · 마린 스노우)의 단일 소유자.
    ///
    /// UnderwaterAmbience가 "물속의 색/안개"를 맡는다면 이쪽은 "물속의 빛과 부유물"을 맡는다.
    /// 안개·환경광은 RenderSettings 전역이라 소유권 다툼이 있었지만(UnderwaterAmbience 클래스
    /// 주석), 여기서 만드는 것은 전부 **이 컴포넌트가 새로 만든 자기 오브젝트**뿐이라 기존 시스템과
    /// 겹치는 전역 상태가 하나도 없다. 씬·세이브·rng를 전혀 건드리지 않는다.
    ///
    /// ── 구성 ────────────────────────────────────────────────────────────────────
    ///  (1) 카우스틱(MGCaustics): 카메라 자식의 **화면 크기 오버레이 쿼드 1장**. 프래그먼트가
    ///      _CameraDepthTexture를 읽어 화면의 각 픽셀이 실제로 어느 월드 좌표인지 되짚고,
    ///      그 XZ로 카우스틱 텍스처를 샘플해 가산 합성한다. 기존 해저/바위/산호 머티리얼은
    ///      한 줄도 바뀌지 않는다(RockCap/CrashSoilOverlay의 "기존 재질 불변 + 오버레이" 관용구를
    ///      평면 데칼 대신 화면 오버레이로 구현한 것 - 근거는 MGCaustics.shader 헤더).
    ///  (2) 갓레이(MGGodRay): 빛기둥 7개를 **메시 한 장**에 구워 셰이더가 정점에서 빌보드를 편다.
    ///      루트는 매 프레임 카메라 XZ · 해수면 y로 옮겨 플레이어 주변에만 존재한다(비용 상수).
    ///  (3) 마린 스노우: ParticleSystem 1개. 월드 시뮬레이션 + 카메라를 따라오는 상자 볼륨.
    ///      **회전은 절대 주지 않는다** - 기포가 카메라 자식이라 고개를 숙이면 옆으로 발사됐던
    ///      사고(UnderwaterAmbience.UpdateDiveBubbles 주석)의 교훈이라, 아예 회전 없는 부모
    ///      (이 컴포넌트의 GameObject)의 자식으로 두고 **위치만** 옮긴다.
    ///
    /// ── 물 밖 비용 ──────────────────────────────────────────────────────────────
    /// LateUpdate 상단에서 카메라 y와 해수면만 비교하고 즉시 return한다(전환된 그 프레임에만
    /// SetActive(false) 3회). 렌더러/파티클이 전부 꺼져 있으므로 드로우콜 0 · 할당 0이다.
    ///
    /// ── 수중 판정을 UnderwaterAmbience.IsUnderwater로 하지 않는 이유 ─────────────
    /// 그 값은 UnderwaterAmbience.LateUpdate가 쓰는데, 같은 LateUpdate끼리는 실행 순서가
    /// 보장되지 않는다(Script Execution Order 미설정). IsUnderwater를 읽으면 프레임에 따라
    /// 한 프레임 늦은 값을 보게 되므로, 여기서는 같은 입력(Camera.main.y, WorldMapManager.seaLevel)
    /// 으로 직접 판정한다 - 세 줄이고, 두 컴포넌트의 실행 순서 계약이 아예 생기지 않는다.
    ///
    /// ── 폴백 ────────────────────────────────────────────────────────────────────
    /// 셰이더/텍스처 로드가 실패하면 해당 연출만 조용히 생략한다(MGOcean 계열의 폴백 계약).
    /// Resources.Load는 필드 초기자가 아니라 **프레임당 1회 프로브**로 부른다 - 실패를 영구
    /// 래치하지 않으므로 임포트가 한 프레임 늦어도 다음 프레임에 자연 복구된다(AGENT_BRIEF 함정 3).
    /// </summary>
    public class UnderwaterVisuals : MonoBehaviour
    {
        // ═══════════════════════════════════════════════════════════════════════════
        //  튜닝 값 (씬에 배치되지 않는 런타임 생성 컴포넌트라 이 코드 기본값이 유일한 진실이다)
        // ═══════════════════════════════════════════════════════════════════════════

        [Header("카우스틱")]
        [Tooltip("카우스틱 가산 세기. 0이면 꺼진다.")]
        public float causticsIntensity = 0.85f;

        [Tooltip("카우스틱 텍스처 1장이 덮는 월드 폭(m). 작을수록 무늬가 잘다.")]
        public float causticsTileSize = 7f;

        [Tooltip("빛무늬가 완전히 사라지는 지면 수심(m). 이보다 얕은 바닥만 빛난다.")]
        public float causticsFadeDepth = 26f;

        [Tooltip("카우스틱을 그리는 최대 거리(m). 이 너머는 수중 안개에 맡긴다.")]
        public float causticsMaxDistance = 45f;

        [Header("갓레이")]
        [Tooltip("빛기둥 가산 세기. 과하면 안 되는 연출이라 낮게 잡는다(0이면 꺼진다).")]
        public float godRayIntensity = 0.30f;

        [Tooltip("빛기둥이 약해지기 시작하는 카메라 수심(m). 깊이 내려갈수록 수면 빛이 안 닿는다.")]
        public float godRayDepthFade = 40f;

        [Header("마린 스노우")]
        [Tooltip("부유물 방출량(개/초). 수명 14초와 곱해진 값이 화면 볼륨 안의 평균 개수다.")]
        public float marineSnowRate = 16f;

        [Tooltip("부유물이 떠다니는 상자 볼륨의 한 변(m). 카메라를 따라다닌다.")]
        public float marineSnowVolume = 22f;

        [Header("공통")]
        [Tooltip("이 값보다 태양이 약해지면(밤) 카우스틱/갓레이가 꺼진다. DayNightCycle.dayIntensity 대비 비율.")]
        public float nightCutoff = 0.18f;

        // ═══════════════════════════════════════════════════════════════════════════
        //  정적 자원 캐시 (프레임 프로브 + R1 리셋 - SeabedFloraSpawner/UnderwaterCaveSpawner와 같은 규칙)
        // ═══════════════════════════════════════════════════════════════════════════

        private static Shader causticsShader;
        private static Shader godRayShader;
        private static Texture2D causticsTexture;

        /// <summary>프레임당 1회 프로브 가드. 실패를 영구 래치하지 않는다(AGENT_BRIEF 함정 3).</summary>
        private static int probeFrame = -1;

        /// <summary>화면 전체 오버레이용 쿼드([-0.5,0.5]²) - 셰이더가 정점에서 NDC로 ×2한다.</summary>
        private static Mesh fullscreenQuad;

        /// <summary>빛기둥 7개를 한 장에 구운 메시(정점 28 · 삼각형 14).</summary>
        private static Mesh godRayMesh;

        /// <summary>
        /// 도메인 리로드를 끈 플레이 모드에서 static 캐시가 이전 실행의 파괴된 자원을 들고
        /// 시작하지 않게 초기 상태로 되돌린다(R1 규약 - SeabedGenerator.ResetStaticCache와 동일).
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticCache()
        {
            causticsShader = null;
            godRayShader = null;
            causticsTexture = null;
            probeFrame = -1;
            fullscreenQuad = null;
            godRayMesh = null;
        }

        // ── 셰이더 프로퍼티 ID (문자열 해시를 매 프레임 다시 계산하지 않는다) ──
        private static readonly int CausticsMapId = Shader.PropertyToID("_CausticsMap");
        private static readonly int CausticsIntensityId = Shader.PropertyToID("_Intensity");
        private static readonly int CausticsTileId = Shader.PropertyToID("_TileSize");
        private static readonly int CausticsFadeDepthId = Shader.PropertyToID("_FadeDepth");
        private static readonly int CausticsMaxDistId = Shader.PropertyToID("_MaxDistance");
        private static readonly int SeaLevelId = Shader.PropertyToID("_SeaLevel");
        private static readonly int SunFactorId = Shader.PropertyToID("_SunFactor");
        private static readonly int CausticsTimeId = Shader.PropertyToID("_MG_CausticsTime");
        private static readonly int GodRayIntensityId = Shader.PropertyToID("_Intensity");
        private static readonly int SunDirId = Shader.PropertyToID("_MG_SunDir");
        private static readonly int GodRayStrengthId = Shader.PropertyToID("_MG_GodRayStrength");
        private static readonly int GodRayTimeId = Shader.PropertyToID("_MG_GodRayTime");

        // ═══════════════════════════════════════════════════════════════════════════
        //  빛기둥 배치표 (rng 소비 0 - 고정 상수다. 값이 흩어져 보이지만 전부 손으로 고른 값이다)
        // ═══════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// 빛기둥 7개 = { 중심 오프셋 x, z(m), 반폭(m), 길이(m), 밝기(0~1), 위상(0~1) }.
        /// 오프셋은 카메라 기준 6~19m 반경에 방위를 흩어 놓았고, 굵기/길이/밝기를 전부 다르게 줘
        /// 규칙적인 울타리처럼 보이지 않게 했다. UnityEngine.Random·System.Random을 쓰지 않으므로
        /// 어떤 추첨 순서도 밀리지 않는다(rng 불변 계약).
        /// </summary>
        private static readonly float[] ShaftTable =
        {
              8.5f,  -3.0f, 1.6f, 22f, 0.95f, 0.00f,
             -6.0f,   9.5f, 2.2f, 26f, 0.80f, 0.31f,
             14.0f,  11.0f, 1.3f, 18f, 0.70f, 0.62f,
            -13.5f,  -8.0f, 1.9f, 24f, 0.85f, 0.14f,
              3.0f,  17.5f, 1.1f, 16f, 0.60f, 0.77f,
            -18.0f,   2.5f, 2.5f, 28f, 0.75f, 0.45f,
              6.5f, -15.0f, 1.5f, 20f, 0.65f, 0.88f,
        };

        private const int ShaftStride = 6;

        // ── 인스턴스 상태 ────────────────────────────────────────────────────────
        private Camera targetCamera;
        private WorldMapManager worldMap;
        private DayNightCycle dayNight;
        private Light sunLight;

        private GameObject causticsObject;
        private Material causticsMaterial;

        private GameObject godRayObject;
        private Material godRayMaterial;

        private ParticleSystem marineSnow;

        /// <summary>직전 프레임의 활성 상태. 전환된 프레임에만 SetActive를 부른다(매 프레임 호출 낭비 방지).</summary>
        private bool visualsActive;

        /// <summary>
        /// 씬 로드마다 스스로 생성한다. UnderwaterAmbience.Bootstrap과 완전히 같은 패턴이다
        /// (SubsystemRegistration에서 sceneLoaded를 한 번만 구독 - AfterSceneLoad는 재시작 때
        /// 다시 불리지 않아 재시작한 게임에서 연출이 조용히 사라진다). 이미 살아 있는 인스턴스가
        /// 있으면 건너뛴다(중복 생성 가드).
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Bootstrap()
        {
            SceneManager.sceneLoaded += (scene, mode) =>
            {
                if (FindAnyObjectByType<UnderwaterVisuals>() != null)
                    return;

                var go = new GameObject("UnderwaterVisuals");
                go.AddComponent<UnderwaterVisuals>();
            };
        }

        /// <summary>참조를 한 번 캐시한다. 못 찾은 것은 LateUpdate에서 저빈도로 재시도한다
        /// (UnderwaterAmbience.Start와 같은 규칙 - 정상 경로 탐색 비용 0).</summary>
        private void Start()
        {
            targetCamera = Camera.main;
            worldMap = FindAnyObjectByType<WorldMapManager>();
            dayNight = FindAnyObjectByType<DayNightCycle>();
            sunLight = DayNightCycle.FindDirectionalLight();
        }

        /// <summary>
        /// 런타임에 만든 머티리얼은 GameObject가 파괴돼도 함께 사라지지 않으므로 직접 지운다
        /// (씬 재로드마다 머티리얼이 한 장씩 쌓이는 누수 방지). 메시는 정적 공유 캐시라 남긴다.
        /// </summary>
        private void OnDestroy()
        {
            if (causticsMaterial != null)
                Destroy(causticsMaterial);
            if (godRayMaterial != null)
                Destroy(godRayMaterial);
        }

        /// <summary>
        /// 매 프레임 수중 판정 후, 수중일 때만 연출을 켜고 갱신한다. LateUpdate인 이유는
        /// 카메라 이동(Update)이 끝난 뒤의 최종 위치로 오버레이/볼륨을 옮겨야 한 프레임 늦지
        /// 않기 때문이다(GrassFieldDriver와 같은 자리). 물 밖에서는 즉시 return하므로
        /// 타이틀 화면(timeScale = 0)에서도 무해하다.
        /// </summary>
        private void LateUpdate()
        {
            // 카메라는 파괴/재생성될 수 있으므로(파괴된 오브젝트 == null 규칙) null이면 다시 집는다.
            if (targetCamera == null)
                targetCamera = Camera.main;

            // WorldMapManager는 씬 로드 직후 생성 순서에 따라 늦게 나타날 수 있다. 못 찾았을 때만
            // 60프레임마다 재시도한다(UnderwaterAmbience와 같은 규칙 - 정상 경로 할당 0).
            if (worldMap == null && Time.frameCount % 60 == 0)
                worldMap = FindAnyObjectByType<WorldMapManager>();

            if (targetCamera == null || worldMap == null)
            {
                SetVisualsActive(false);
                return;
            }

            Vector3 camPos = targetCamera.transform.position;
            float seaLevel = worldMap.seaLevel;
            float camDepth = seaLevel - camPos.y;

            // 물 밖: 전부 끄고 끝(전환된 그 프레임에만 SetActive가 나간다).
            if (camDepth <= 0f)
            {
                SetVisualsActive(false);
                return;
            }

            if (dayNight == null && Time.frameCount % 60 == 0)
                dayNight = FindAnyObjectByType<DayNightCycle>();
            if (sunLight == null && Time.frameCount % 60 == 0)
                sunLight = DayNightCycle.FindDirectionalLight();

            EnsureResourcesProbed();
            EnsureBuilt();
            SetVisualsActive(true);

            // 현재 태양 강도(0~1). UnderwaterAmbience와 같은 계산 - DayNightCycle.Update가 이번
            // 프레임 sunLight.intensity에 기록한 최종값이라 밤/비가 이미 반영돼 있다.
            float lightFactor = 1f;
            if (sunLight != null && dayNight != null && dayNight.dayIntensity > 0f)
                lightFactor = Mathf.Clamp01(sunLight.intensity / dayNight.dayIntensity);

            // 밤에는 끈다: nightCutoff 아래 0 → 그 위로 부드럽게 1까지. 태양이 지평선 아래로
            // 내려가면 lightFactor 자체가 0에 수렴하므로 별도 고도 판정이 필요 없다.
            float sunFactor = Mathf.SmoothStep(0f, 1f,
                Mathf.InverseLerp(Mathf.Max(0.01f, nightCutoff), 0.65f, lightFactor));

            float now = Time.time;

            UpdateCaustics(seaLevel, sunFactor, now);
            UpdateGodRays(camPos, seaLevel, camDepth, sunFactor, now);
            UpdateMarineSnow(camPos);
        }

        /// <summary>
        /// 카우스틱 오버레이 갱신. 오브젝트는 카메라 자식이고 셰이더가 오브젝트 변환을 무시하므로
        /// 트랜스폼을 손댈 일이 없다 - 프로퍼티만 밀어 넣는다(SetFloat 6회, 할당 0).
        /// </summary>
        private void UpdateCaustics(float seaLevel, float sunFactor, float now)
        {
            if (causticsMaterial == null)
                return;

            causticsMaterial.SetFloat(SeaLevelId, seaLevel);
            causticsMaterial.SetFloat(CausticsIntensityId, Mathf.Max(0f, causticsIntensity));
            causticsMaterial.SetFloat(CausticsTileId, Mathf.Max(0.5f, causticsTileSize));
            causticsMaterial.SetFloat(CausticsFadeDepthId, Mathf.Max(1f, causticsFadeDepth));
            causticsMaterial.SetFloat(CausticsMaxDistId, Mathf.Max(5f, causticsMaxDistance));
            // 밤에도 완전히 0으로 죽이지는 않는다(달빛). 0.04는 거의 보이지 않는 최소치다.
            causticsMaterial.SetFloat(SunFactorId, Mathf.Max(0.04f, sunFactor));
            causticsMaterial.SetFloat(CausticsTimeId, now);
        }

        /// <summary>
        /// 갓레이 갱신. 루트를 카메라 XZ · 해수면 y로 옮겨 빛기둥의 머리가 항상 수면에 닿게 하고
        /// (회전은 절대 주지 않는다 - 셰이더가 태양 방향으로 축을 잡는다), 태양 방향/세기를 넣는다.
        /// 세기 = 태양 계수 × 수심 감쇠 - 깊이 내려갈수록 수면 빛이 닿지 않아 자연히 사라진다.
        /// </summary>
        private void UpdateGodRays(Vector3 camPos, float seaLevel, float camDepth, float sunFactor, float now)
        {
            if (godRayObject == null || godRayMaterial == null)
                return;

            godRayObject.transform.position = new Vector3(camPos.x, seaLevel, camPos.z);

            Vector3 sunDir = sunLight != null ? sunLight.transform.forward : Vector3.down;

            float depthFade = Mathf.Clamp01(1f - camDepth / Mathf.Max(1f, godRayDepthFade));
            float strength = sunFactor * depthFade;

            godRayMaterial.SetVector(SunDirId, new Vector4(sunDir.x, sunDir.y, sunDir.z, 0f));
            godRayMaterial.SetFloat(GodRayIntensityId, Mathf.Max(0f, godRayIntensity));
            godRayMaterial.SetFloat(GodRayStrengthId, strength);
            godRayMaterial.SetFloat(GodRayTimeId, now);
        }

        /// <summary>
        /// 마린 스노우 갱신. 상자 볼륨을 카메라 위치로 옮기기만 한다(월드 시뮬레이션이라 이미
        /// 뱉은 입자는 제자리에 남아 시차가 생긴다). **회전은 건드리지 않는다** - 부모가 회전 없는
        /// 이 컴포넌트의 GameObject라 원리적으로 기울 수 없다(기포 사고의 근본 해법).
        /// </summary>
        private void UpdateMarineSnow(Vector3 camPos)
        {
            if (marineSnow == null)
                return;

            marineSnow.transform.position = camPos;

            if (!marineSnow.isEmitting)
                marineSnow.Play();
        }

        /// <summary>
        /// 연출 3종을 한꺼번에 켜고 끈다. 상태가 바뀐 프레임에만 실제 호출이 나가고, 물 밖에서는
        /// 파티클을 Stop(StopEmitting)해 이미 뜬 부유물이 수명만큼 자연스럽게 사라지게 한다
        /// (UnderwaterAmbience의 기포와 같은 처리 - Clear는 하지 않는다).
        /// </summary>
        private void SetVisualsActive(bool active)
        {
            if (visualsActive == active)
                return;
            visualsActive = active;

            if (causticsObject != null)
                causticsObject.SetActive(active);
            if (godRayObject != null)
                godRayObject.SetActive(active);

            if (marineSnow != null)
            {
                if (active)
                    marineSnow.Play();
                else
                    marineSnow.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            }
        }

        /// <summary>
        /// 셰이더/텍스처를 프레임당 한 번만 프로브한다. 필드 초기자에서 Resources.Load를 부르면
        /// null이 돌아오고 그 null을 확정하면 세션 내내 에셋이 안 쓰인다(AGENT_BRIEF 함정 3) -
        /// 실패를 영구 래치하지 않는 이 패턴이 프로젝트 표준이다.
        /// </summary>
        private static void EnsureResourcesProbed()
        {
            if (probeFrame == Time.frameCount)
                return;
            probeFrame = Time.frameCount;

            if (causticsShader == null)
                causticsShader = Resources.Load<Shader>("Shaders/MGCaustics");
            if (godRayShader == null)
                godRayShader = Resources.Load<Shader>("Shaders/MGGodRay");
            if (causticsTexture == null)
                causticsTexture = Resources.Load<Texture2D>("Textures/caustics");
        }

        /// <summary>
        /// 아직 없는 연출 오브젝트를 만든다. 최초 잠수 때 1회만 실제 생성이 일어나고 이후에는
        /// null 검사 3번으로 끝난다. 셰이더/텍스처가 없으면 그 연출만 건너뛰므로(폴백 계약)
        /// 다음 프레임 프로브에서 자원이 잡히면 그때 만들어진다.
        /// </summary>
        private void EnsureBuilt()
        {
            if (causticsObject == null && causticsShader != null && causticsTexture != null
                && targetCamera != null)
                BuildCaustics();

            if (godRayObject == null && godRayShader != null)
                BuildGodRays();

            if (marineSnow == null)
                BuildMarineSnow();
        }

        /// <summary>
        /// 카우스틱 오버레이 생성: 카메라 자식의 쿼드 1장. 셰이더가 클립 좌표를 직접 내므로
        /// 트랜스폼은 의미가 없지만, **바운즈**는 프러스텀 컬링에 쓰이므로 아주 크게 잡아
        /// 어느 각도에서도 컬링되지 않게 한다(카메라 자식이라 바운즈도 함께 따라다닌다).
        /// </summary>
        private void BuildCaustics()
        {
            causticsObject = new GameObject("UnderwaterCaustics");
            causticsObject.transform.SetParent(targetCamera.transform, false);

            var filter = causticsObject.AddComponent<MeshFilter>();
            filter.sharedMesh = GetFullscreenQuad();

            var renderer = causticsObject.AddComponent<MeshRenderer>();
            // 카메라가 파괴돼 자식 오브젝트만 사라진 경우 여기로 다시 들어온다 - 옛 머티리얼을
            // 먼저 지워야 재생성 때마다 한 장씩 쌓이지 않는다(OnDestroy와 같은 이유).
            if (causticsMaterial != null)
                Destroy(causticsMaterial);
            causticsMaterial = new Material(causticsShader);
            causticsMaterial.hideFlags = HideFlags.HideAndDontSave;
            causticsMaterial.SetTexture(CausticsMapId, causticsTexture);
            renderer.sharedMaterial = causticsMaterial;
            ApplyDecorRendererSettings(renderer);

            causticsObject.SetActive(visualsActive);
        }

        /// <summary>
        /// 갓레이 생성: 빛기둥 7개를 한 장에 구운 메시 + 머티리얼 1장 = 드로우콜 1.
        /// 이 컴포넌트의 GameObject 자식이라 회전이 원리적으로 0이다(셰이더가 축을 잡는다).
        /// </summary>
        private void BuildGodRays()
        {
            godRayObject = new GameObject("UnderwaterGodRays");
            godRayObject.transform.SetParent(transform, false);

            var filter = godRayObject.AddComponent<MeshFilter>();
            filter.sharedMesh = GetGodRayMesh();

            var renderer = godRayObject.AddComponent<MeshRenderer>();
            if (godRayMaterial != null)
                Destroy(godRayMaterial);
            godRayMaterial = new Material(godRayShader);
            godRayMaterial.hideFlags = HideFlags.HideAndDontSave;
            renderer.sharedMaterial = godRayMaterial;
            ApplyDecorRendererSettings(renderer);

            godRayObject.SetActive(visualsActive);
        }

        /// <summary>
        /// 순수 장식 렌더러 공통 설정. 그림자 캐스팅/수신·프로브를 전부 꺼서 추가 패스와
        /// 프로브 보간 비용을 없앤다(SeabedFloraSpawner의 장식 렌더러와 같은 규칙).
        /// </summary>
        private static void ApplyDecorRendererSettings(Renderer renderer)
        {
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
        }

        /// <summary>
        /// 화면 전체 오버레이용 쿼드. 로컬 좌표가 [-0.5, 0.5]²라 셰이더가 ×2해 NDC로 쓴다.
        /// 바운즈는 10km 짜리 상자 - 카메라 자식이므로 어떤 자세에서도 프러스텀에 걸린다.
        /// </summary>
        private static Mesh GetFullscreenQuad()
        {
            if (fullscreenQuad != null)
                return fullscreenQuad;

            var mesh = new Mesh { name = "MGFullscreenQuad" };
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
            return fullscreenQuad;
        }

        /// <summary>
        /// 빛기둥 메시를 굽는다(ShaftTable과 MGGodRay.shader의 기하 계약이 한 쌍이다).
        /// 정점은 전부 그 기둥의 **중심 오프셋**이고, 실제 사각형 전개는 셰이더 정점 단계가
        /// 카메라 방향을 봐서 편다(빌보드). 그래서 어느 각도에서도 메시 갱신이 필요 없다.
        ///  · uv0 = (좌 0 / 우 1, 위 0 / 아래 1)
        ///  · uv1 = (반폭 m, 길이 m)  · uv2 = (밝기, 위상)
        /// 정점 색이 아니라 UV 채널을 쓰는 이유는 셰이더 헤더 주석 참고(Color32 8비트 절단).
        /// </summary>
        private static Mesh GetGodRayMesh()
        {
            if (godRayMesh != null)
                return godRayMesh;

            int shaftCount = ShaftTable.Length / ShaftStride;
            var vertices = new Vector3[shaftCount * 4];
            var uv0 = new Vector2[shaftCount * 4];
            var uv1 = new Vector2[shaftCount * 4];
            var uv2 = new Vector2[shaftCount * 4];
            var triangles = new int[shaftCount * 6];

            float maxLength = 0f;
            float maxSpread = 0f;

            for (int s = 0; s < shaftCount; s++)
            {
                int t = s * ShaftStride;
                float ox = ShaftTable[t];
                float oz = ShaftTable[t + 1];
                float halfWidth = ShaftTable[t + 2];
                float shaftLength = ShaftTable[t + 3];
                float brightness = ShaftTable[t + 4];
                float phase = ShaftTable[t + 5];

                maxLength = Mathf.Max(maxLength, shaftLength);
                maxSpread = Mathf.Max(maxSpread, Mathf.Abs(ox) + halfWidth);
                maxSpread = Mathf.Max(maxSpread, Mathf.Abs(oz) + halfWidth);

                int v = s * 4;
                var center = new Vector3(ox, 0f, oz);
                var size = new Vector2(halfWidth, shaftLength);
                var variation = new Vector2(brightness, phase);

                for (int k = 0; k < 4; k++)
                {
                    vertices[v + k] = center;
                    uv1[v + k] = size;
                    uv2[v + k] = variation;
                }

                uv0[v + 0] = new Vector2(0f, 0f); // 좌·위(수면)
                uv0[v + 1] = new Vector2(1f, 0f); // 우·위
                uv0[v + 2] = new Vector2(0f, 1f); // 좌·아래
                uv0[v + 3] = new Vector2(1f, 1f); // 우·아래

                int i = s * 6;
                triangles[i + 0] = v + 0;
                triangles[i + 1] = v + 2;
                triangles[i + 2] = v + 1;
                triangles[i + 3] = v + 2;
                triangles[i + 4] = v + 3;
                triangles[i + 5] = v + 1;
            }

            var mesh = new Mesh { name = "MGGodRayShafts" };
            mesh.hideFlags = HideFlags.HideAndDontSave;
            mesh.vertices = vertices;
            mesh.uv = uv0;
            mesh.uv2 = uv1;
            mesh.uv3 = uv2;
            mesh.triangles = triangles;

            // 정점이 전부 원점 근처(기둥 중심)라 자동 바운즈는 실제 그려지는 범위보다 훨씬 작다.
            // 셰이더가 아래로 최대 maxLength만큼, 옆으로 maxSpread만큼 펴므로 직접 잡아 준다
            // (안 잡으면 기둥이 화면 가장자리에서 통째로 컬링돼 사라진다).
            // 여유 12m: 셰이더가 축을 태양 방위로 최대 _SunTilt(0.25) × 길이(28m) = 7m까지
            // 기울이므로, 정점 오프셋 + 반폭만으로 잡으면 기운 만큼이 바운즈 밖으로 나간다.
            float half = maxSpread + 12f;
            mesh.bounds = new Bounds(new Vector3(0f, -maxLength * 0.5f, 0f),
                new Vector3(half * 2f, maxLength + 8f, half * 2f));

            godRayMesh = mesh;
            return godRayMesh;
        }

        /// <summary>
        /// 마린 스노우 파티클 생성. EffectBuilder의 파티클 규약을 그대로 따르되(공유 머티리얼
        /// GetParticleMaterial · 빌보드 · View 정렬 · playOnAwake false), 부유물 전용 값으로 짠다:
        ///  · 월드 시뮬레이션 - 상자 볼륨이 카메라를 따라와도 이미 뜬 입자는 제자리에 남아
        ///    헤엄칠 때 시차가 생긴다(로컬이면 입자가 통째로 따라와 "정지한 눈"이 된다).
        ///  · 아주 느린 하강 + 저주파 노이즈 - 물속 부유물의 표류를 흉내낸다.
        ///  · 수명 14초 × 방출 16/초 ≈ 화면 볼륨 안 평균 220개. maxParticles 260으로 상한을 건다.
        ///  · Time.timeScale에 묶인 기본(스케일) 시간을 쓴다 - 타이틀 화면 정지 관례.
        ///    (기포는 useUnscaledTime = true지만, 그건 잠수 피드백이라 목적이 다르다.)
        /// </summary>
        private void BuildMarineSnow()
        {
            var go = new GameObject("MarineSnow");
            go.transform.SetParent(transform, false);

            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.playOnAwake = false;
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            main.loop = true;
            main.duration = 5f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(10f, 18f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.01f, 0.06f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.012f, 0.05f);
            // 흰색~아주 옅은 청백. 알파는 아래 colorOverLifetime이 단독으로 정하게 1로 둔다
            // (colorOverLifetime은 startColor를 "곱한다" - EffectBuilder의 함정 주석과 동일).
            main.startColor = new ParticleSystem.MinMaxGradient(
                Color.white, new Color(0.86f, 0.94f, 1f, 1f));
            // 거의 뜨지도 가라앉지도 않는다(중성 부력). 0.0012 → 14초 뒤 약 0.16m/s.
            main.gravityModifier = 0.0012f;
            main.maxParticles = 260;
            main.simulationSpace = ParticleSystemSimulationSpace.World;

            var emission = ps.emission;
            emission.rateOverTime = Mathf.Max(0f, marineSnowRate);

            // 카메라를 감싸는 상자에서 고르게 뿜는다. 볼륨이 카메라를 따라오므로 어느 방향을 봐도
            // 부유물이 있고, 볼륨 밖으로 흘러간 입자는 수명이 다해 사라진다.
            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Box;
            float v = Mathf.Max(4f, marineSnowVolume);
            shape.scale = new Vector3(v, v * 0.65f, v);
            shape.position = Vector3.zero;

            // 저주파·저강도 노이즈 - 물살에 실려 아주 느리게 표류한다.
            var noise = ps.noise;
            noise.enabled = true;
            noise.strength = 0.05f;
            noise.frequency = 0.09f;
            noise.quality = ParticleSystemNoiseQuality.Low;

            // 수명 양 끝에서 페이드 - 볼륨 경계에서 입자가 툭 나타나거나 사라지지 않게 한다.
            var gradient = new Gradient();
            gradient.SetKeys(
                new[]
                {
                    new GradientColorKey(Color.white, 0f),
                    new GradientColorKey(Color.white, 1f),
                },
                new[]
                {
                    new GradientAlphaKey(0f, 0f),
                    new GradientAlphaKey(0.5f, 0.18f),
                    new GradientAlphaKey(0.5f, 0.82f),
                    new GradientAlphaKey(0f, 1f),
                });
            var colorOverLifetime = ps.colorOverLifetime;
            colorOverLifetime.enabled = true;
            colorOverLifetime.color = new ParticleSystem.MinMaxGradient(gradient);

            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            if (renderer != null)
            {
                renderer.sharedMaterial = EffectBuilder.GetParticleMaterial();
                renderer.renderMode = ParticleSystemRenderMode.Billboard;
                renderer.alignment = ParticleSystemRenderSpace.View;
                ApplyDecorRendererSettings(renderer);
            }

            marineSnow = ps;
        }
    }
}
