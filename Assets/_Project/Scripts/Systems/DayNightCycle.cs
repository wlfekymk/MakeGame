using UnityEngine;
using UnityEngine.SceneManagement;

namespace MakeGame.Systems
{
    /// <summary>
    /// SurvivalClock의 하루 진행률(TimeOfDay01)에 맞춰 Directional Light의 각도/밝기/색온도를
    /// 서서히 바꿔 밤/낮 주기를 표현한다. 예전에는 조명이 고정값이라 게임 내 시간이 아무리 흘러도
    /// 낮과 밤의 시각적 차이가 전혀 없었다(밤/낮 주기 부재 이슈).
    /// 씬에 미리 배치할 필요 없이 RuntimeInitializeOnLoadMethod로 씬 로드 시 자동 생성되며,
    /// Directional Light와 SurvivalClock을 씬에서 스스로 찾아 연결한다.
    /// </summary>
    public class DayNightCycle : MonoBehaviour
    {
        [Tooltip("낮 동안(정오)의 최대 조명 강도")]
        public float dayIntensity = 1.2f;

        // B4(ArtDirection 3장): 0.05 → 0.10으로 올린다. 0.05는 사실상 완전 암흑이라, 곰/식인종처럼
        // 색과 실루엣으로 미리 알아볼 수 있게 만들어 둔 위험 요소조차 밤에는 형체가 보이지 않았다.
        // "위험은 사전에 식별 가능해야 한다"(B3-6)는 원칙과 조명 값이 정면으로 모순되던 상태다.
        // 0.10은 "밤은 여전히 어둡고 위험하지만 실루엣은 최소한 읽히는" 수준을 목표로 한 값이다.
        // 이 값은 씬에 직렬화돼 있지 않다 - DayNightCycle은 RuntimeInitializeOnLoadMethod로 매번 새로
        // 생성되므로(아래 Bootstrap 참고) 이 코드 기본값이 곧 실제 게임에 적용되는 값이다.
        [Tooltip("밤 동안의 최소 조명 강도 (완전한 암흑은 아니고 은은하게 남겨둔다)")]
        public float nightIntensity = 0.18f;

        // [B23] 열대 바다 톤 미세조정: 종전 (1, 0.98, 0.92)는 온대 지방의 따뜻한 백색이었다.
        // 산호초 바다의 한낮은 물/하늘 산란광이 섞여 살짝 청록이 도는 밝은 백색에 가깝다.
        // 채도를 크게 올리면 피부·모래 색이 병들어 보이므로 녹/청 채널만 아주 조금 올렸다.
        [Tooltip("한낮의 조명 색상 (살짝 청록이 도는 밝은 백색광)")]
        public Color dayColor = new Color(0.96f, 1f, 0.98f);

        [Tooltip("일몰(황혼) 무렵의 조명 색상 (붉은 노을빛). [B22] 새벽은 아래 dawnColor를 따로 쓴다.")]
        public Color duskDawnColor = new Color(1f, 0.52f, 0.26f);

        // [B22] 새벽과 황혼을 같은 색으로 칠하면 하루가 좌우대칭이 되어 "지금이 아침인지 저녁인지"를
        // 화면만 보고 알 수 없다. 실제 대기에서도 아침은 밤새 가라앉은 공기 때문에 더 차갑고 분홍/보라
        // 쪽이고, 저녁은 낮 동안 달궈진 먼지 때문에 더 진한 주황/빨강 쪽이다. 색을 갈라두면 시간 감각이
        // 생기고(디렉터 방향 "머물고 싶은 섬"의 리듬), 같은 골든아워 계산식을 그대로 재사용하므로 비용은 0이다.
        [Tooltip("새벽(일출) 무렵의 조명 색상. 황혼보다 차갑고 분홍빛이 돈다.")]
        public Color dawnColor = new Color(1f, 0.74f, 0.66f);

        [Tooltip("한밤중의 조명 색상 (푸르스름한 달빛). [B22] 채도를 올려 '어두운 회색'이 아니라 '달빛 파랑'으로 읽히게 했다.")]
        public Color nightColor = new Color(0.40f, 0.55f, 0.88f);

        [Header("하늘(스카이박스) 색조")]
        [Tooltip("낮 하늘의 색조 (Skybox/Procedural의 _SkyTint)")]
        public Color daySkyTint = new Color(0.45f, 0.65f, 0.85f);

        [Tooltip("황혼(일몰) 무렵 하늘의 색조")]
        public Color duskDawnSkyTint = new Color(0.88f, 0.44f, 0.30f);

        [Tooltip("새벽(일출) 무렵 하늘의 색조. 황혼보다 분홍/보라 쪽이다.")]
        public Color dawnSkyTint = new Color(0.80f, 0.55f, 0.66f);

        [Tooltip("밤하늘의 색조 (짙은 남색). [B22] 완전한 검정에 가깝지 않게 파랑을 조금 남긴다.")]
        public Color nightSkyTint = new Color(0.06f, 0.09f, 0.20f);

        [Tooltip("낮 하늘의 노출(밝기)")]
        public float daySkyExposure = 1.15f;

        [Tooltip("밤하늘의 노출(밝기) - 별이 반짝일 정도로 어둡게")]
        public float nightSkyExposure = 0.22f;

        // [B22] Skybox/Procedural(기본 스카이박스)의 나머지 프로퍼티도 시간대에 맞춰 몬다.
        // _AtmosphereThickness는 "대기를 얼마나 두껍게 통과해서 보는가"라 값이 커질수록 짧은 파장이
        // 더 많이 산란돼 하늘 전체가 주황/빨강으로 물든다 - 노을이 예뻐지는 실제 물리 파라미터다.
        // _SunSize는 태양 원반의 크기다. 지평선 근처에서 키우면 "크게 걸린 해"가 되어 노을이 극적으로 보인다.
        // 둘 다 HasProperty로 걸러 쓰므로, 스카이박스가 Procedural이 아니어도 조용히 건너뛴다.
        [Header("스카이박스 대기 (Skybox/Procedural 전용, 없으면 무시)")]
        [Tooltip("한낮/한밤의 대기 두께. 1이 셰이더 기본값이다.")]
        public float dayAtmosphereThickness = 1.0f;

        [Tooltip("일출/일몰 정점의 대기 두께. 클수록 하늘이 붉게 타오른다.")]
        public float goldenAtmosphereThickness = 2.1f;

        [Tooltip("평소 태양 원반 크기(_SunSize). 셰이더 기본값 0.04.")]
        public float daySunSize = 0.045f;

        [Tooltip("일출/일몰 정점의 태양 원반 크기. 지평선에 크게 걸린 해를 만든다.")]
        public float goldenSunSize = 0.10f;

        [Header("게임 시작 시각")]
        // [검은 하늘 원인규명] SurvivalClock.elapsedSeconds는 코드 기본값도 0이고 씬 직렬화 값도 0이라
        // (SampleScene.unity의 SurvivalClock, elapsedSeconds: 0) 새 게임은 TimeOfDay01 == 0,
        // 즉 "정확히 자정"에서 시작한다. 그러면 아래 Update의 dayFactor가 0이 되어 하늘이 곧바로
        // nightSkyTint(#0D0F1F) + nightSkyExposure(0.15)로 칠해진다 - 이것이 게임을 시작하자마자
        // 수평선 위가 새까맣게 보이던 현상의 실체다(콘솔 에러가 0건이었던 이유이기도 하다. 버그가
        // 아니라 "밤에 시작하는" 정상 동작이었다). secondsPerDay가 600이므로 일출(0.25)까지
        // 실시간 2분 30초 동안 이 암흑이 계속된다.
        // 조치: 시계가 아직 0(=새 게임)일 때만 이 값만큼 하루를 앞당겨 아침에서 시작하게 한다.
        // DayNightCycle의 시각 계산만 오프셋하면 SurvivalClock.IsDaytime(일사병 판정)/Shelter.TrySleep과
        // 어긋나므로, 반드시 시계 자체를 옮겨 모든 시스템이 같은 시각을 보게 한다.
        // 0.25는 Shelter.TrySleep이 취침 후 이동시키는 "다음 날 일출" 시각과 같은 기준값이다.
        [Tooltip("새 게임(경과 시간 0)일 때 하루의 어느 시점에서 시작할지(0=자정, 0.25=일출, 0.5=정오)." +
            " 0 이하로 두면 이 보정을 하지 않고 기존처럼 자정에서 시작한다.")]
        [Range(0f, 1f)]
        public float newGameStartTimeOfDay = 0.3f;

        [Header("환경광 / 대기 안개")]
        [Tooltip("켜면 시간대에 맞춰 환경광(Ambient)을 직접 제어한다. 끄면 씬의 조명 설정을 그대로 둔다.")]
        public bool driveAmbientLight = true;

        // [B23] 단색(Flat) 환경광을 3색(Trilight)으로 확장하면서, 기존 day/duskDawn/nightAmbient
        // 필드는 트라이라이트의 '하늘' 색으로 재활용한다(필드 이름을 유지해 씬/인스펙터 호환을 지킨다).
        // 하늘/수평선/지면 3색이 갈라지면 윗면·옆면·아랫면이 서로 다른 반사광을 받아, 단색일 때의
        // "플라스틱 같은 균일한 그늘"이 사라지고 형태가 공짜로 입체적으로 읽힌다.
        [Tooltip("한낮 환경광의 '하늘' 색(Trilight Sky). 열대의 밝은 하늘색 반사광.")]
        public Color dayAmbient = new Color(0.47f, 0.57f, 0.66f);

        [Tooltip("한낮 환경광의 '수평선' 색(Trilight Equator). 바다 위 대기 산란으로 하늘보다 따뜻하다.")]
        public Color dayAmbientEquator = new Color(0.56f, 0.53f, 0.46f);

        [Tooltip("한낮 환경광의 '지면' 색(Trilight Ground). 모래·식생에 튕겨 아랫면을 데우는 반사색.")]
        public Color dayAmbientGround = new Color(0.40f, 0.36f, 0.28f);

        [Tooltip("일출/일몰 무렵 환경광의 '하늘' 색(따뜻하고 채도가 낮은 색)")]
        public Color duskDawnAmbient = new Color(0.38f, 0.29f, 0.25f);

        [Tooltip("골든아워 환경광의 '수평선' 색(진한 주황). 노을이 수평선 쪽 반사광을 물들인다.")]
        public Color goldenAmbientEquator = new Color(0.66f, 0.33f, 0.16f);

        [Tooltip("골든아워 환경광의 '지면' 색. 해가 낮게 깔리면 지면 반사는 급격히 어두워진다.")]
        public Color goldenAmbientGround = new Color(0.15f, 0.11f, 0.09f);

        // [B22] 밝기 총량은 예전(0.16/0.18/0.26, 상대휘도 ≈0.176)과 거의 같게 두고 색상만 파랑 쪽으로
        // 민다(0.12/0.16/0.30, 상대휘도 ≈0.166). "밤 = 안 보임"이 아니라 "밤 = 푸른 어둠"이라는
        // 디렉터 지시를 지키면서, 실루엣 가독성(B4에서 확보한 것)은 그대로 유지하기 위한 값이다.
        // [B23] Trilight에서 이 값은 '하늘' 색이 된다. 실루엣은 주로 하늘을 등지고 읽히므로
        // "최소 가시성 바닥" 역할은 그대로 이 값이 진다(수평선/지면은 이보다 어두워도 된다).
        [Tooltip("한밤중 환경광의 '하늘' 색(달빛 남색). 이 값이 밤의 '최소 가시성 바닥'이다 - 0에 가까우면 실루엣조차 안 보인다.")]
        public Color nightAmbient = new Color(0.12f, 0.16f, 0.30f);

        [Tooltip("한밤 환경광의 '수평선' 색(어두운 남색).")]
        public Color nightAmbientEquator = new Color(0.06f, 0.08f, 0.16f);

        [Tooltip("한밤 환경광의 '지면' 색(거의 검정). 달빛은 지면 반사광을 거의 만들지 못한다.")]
        public Color nightAmbientGround = new Color(0.02f, 0.03f, 0.05f);

        [Tooltip("비가 올 때 환경광이 섞여 들어갈 색(채도 빠진 차가운 회색). 젖은 날의 흐린 빛을 만든다.")]
        public Color rainAmbientTint = new Color(0.38f, 0.42f, 0.46f);

        // [B23] 그림자의 어둡기는 "하늘 전체에서 오는 환경광이 그림자 속을 얼마나 채우는가"의 표현이다.
        // 종전에는 씬 기본값(1.0) 고정이라 밤 달빛 그림자까지 한낮처럼 새까만 구멍으로 보였다.
        // 한낮의 열대 태양은 짙은 그림자(1.0), 골든아워는 대기 산란으로 조금 풀리고(0.8),
        // 밤은 하늘 전체가 약한 면광원이라 훨씬 옅다(0.5).
        [Header("그림자 강도 주기")]
        [Tooltip("한낮의 그림자 강도(1 = 완전 불투명 그림자)")]
        [Range(0f, 1f)] public float dayShadowStrength = 1f;

        [Tooltip("골든아워(일출/일몰)의 그림자 강도")]
        [Range(0f, 1f)] public float goldenShadowStrength = 0.8f;

        [Tooltip("밤(달빛)의 그림자 강도. 낮출수록 그림자 속에도 환경광이 남아 새까맣지 않다.")]
        [Range(0f, 1f)] public float nightShadowStrength = 0.5f;

        // [B23] 밤 광원 방향. 종전의 '궤도 접기'(Update 참고)는 전환 연속성은 지켰지만 밤의 대부분을
        // 빛이 지평선 근처에 낮게 걸린 채 보내서, 그림자가 옆으로 길게 눕는 부자연스러움이 있었다.
        // 밤에는 태양이 진 반대편 하늘의 고정 고도에 뜬 '달'로 부드럽게 전환한다.
        [Header("달빛")]
        [Tooltip("밤에 달이 떠 있는 고정 고도(도). 40~55도 권장 - 너무 낮으면 그림자가 눕고, 너무 높으면 정오처럼 보인다.")]
        [Range(20f, 80f)]
        public float moonAltitudeDeg = 48f;

        [Tooltip("켜면 하늘색과 같은 색의 옅은 거리 안개를 깔아 수평선에서 바다와 하늘이 이어지게 한다.")]
        public bool enableAtmosphericFog = true;

        // [B22] FogMode.Exponential → ExponentialSquared로 바꿨다. Exponential은 밀도를 올리면
        // 발밑부터 같이 뿌예져서 "가까운 건 선명하고 먼 것만 안개"가 되지 않는다(지수함수의 기울기가
        // 거리 0에서 최대다). ExponentialSquared는 거리 제곱이라 근거리 감쇠가 거의 없고 먼 거리에서
        // 급격히 짙어진다 - 값싼 원근감(과제 4번 "먼 풍경")을 얻는 정확한 도구다.
        // 0.0016 기준 실제 값: 200m 96% 선명 · 500m 67% · 800m 36% · 1000m 8%.
        // far clip이 1000이라 바다 평면이 잘리는 지점에서는 이미 거의 안개색 = 하늘색이라 이음매가 안 보인다.
        [Tooltip("맑은 날의 거리 안개 밀도(ExponentialSquared). 카메라 far clip이 1000이라 이 정도면 " +
            "가까운 곳은 선명하고 수평선만 짙게 흐려진다. WeatherSystem의 rainFogDensity와는 별개 값이다.")]
        public float clearFogDensity = 0.0016f;

        [Tooltip("새벽 안개 배수. 일출 정점에서 안개 밀도가 이만큼 배로 짙어져 아침 물안개가 깔린다.")]
        public float dawnFogDensityMultiplier = 2.3f;

        // ── 고도에 따른 안개(가벼운 aerial perspective) ─────────────────────────────
        //
        // 실제 대기에서 수증기와 먼지는 아래쪽에 깔린다. 그래서 언덕에 올라가면 시야가 트이고,
        // 해변에 내려오면 수평선이 다시 뿌예진다. 지금까지 우리 안개는 **거리만** 봤기 때문에
        // 섬 꼭대기에서나 물가에서나 보이는 거리가 똑같았다 - 섬 게임에서 가장 아까운 손실이다
        // (Docs/RealismPlan.md C2).
        //
        // 진짜 aerial perspective(픽셀마다 높이·태양 방향을 보는 산란 안개)는 셰이더가 필요하고,
        // 우리 지형·야자수는 URP Lit을 쓰므로 우리가 손댈 수 없다. 대신 **카메라 높이**로 전역
        // 밀도를 낮추면, 셰이더를 하나도 건드리지 않고 "올라가면 트인다"는 체감의 대부분을 얻는다.
        // 값싼 근사임을 알고 쓰는 것이고, 나중에 커스텀 산란 안개를 넣으면 이 블록이 대체된다.

        [Tooltip("해수면 위 이 높이(m)에 도달하면 안개 밀도가 fogDensityAtHeight 배까지 옅어진다.")]
        public float fogClearHeight = 85f;

        [Tooltip("높은 곳에서 남는 안개 밀도 비율(1 = 고도 무관, 낮을수록 정상에서 시야가 트인다).")]
        [Range(0.2f, 1f)] public float fogDensityAtHeight = 0.45f;

        /// <summary>
        /// [B22] 지금 시각/날씨에서 계산된 "맑은 날 기준" 안개 색. WeatherSystem이 비 안개로 서서히
        /// 넘어갈 때 출발점으로 읽는다(둘이 매 프레임 RenderSettings를 서로 덮어쓰지 않게 하는 장치).
        /// </summary>
        public Color ClearFogColor { get; private set; } = Color.gray;

        /// <summary>[B22] 지금 시각에서 계산된 "맑은 날 기준" 안개 밀도. WeatherSystem이 보간 출발점으로 읽는다.</summary>
        public float ClearFogDensity { get; private set; }

        private Light sunLight;
        private SurvivalClock clock;
        private WeatherSystem weather;

        /// <summary>고도 안개 계산용 카메라 캐시(Camera.main은 태그 검색이라 매 프레임 부르면 안 된다).</summary>
        private Camera fogCamera;

        /// <summary>
        /// 런타임에만 색을 바꾸기 위해 원본(Default-Skybox 등 공유 에셋)을 복제한 인스턴스.
        /// 공유 머티리얼을 직접 건드리면 다른 씬/에디터 상태에도 영향을 줄 수 있어 항상 복제본을 쓴다.
        /// </summary>
        private Material skyboxInstance;
        private static readonly int SkyTintId = Shader.PropertyToID("_SkyTint");
        private static readonly int ExposureId = Shader.PropertyToID("_Exposure");

        // [B22] Skybox/Procedural에만 있는 프로퍼티들. 다른 스카이박스면 HasProperty가 false라 건너뛴다.
        // (URP에서 Built-in 전용 프로퍼티를 쓰면 조용히 무시되지만, Skybox/Procedural 셰이더 자체는
        //  파이프라인 중립이라 URP에서도 그대로 동작한다 - 이 파일이 이미 _SkyTint/_Exposure로 검증한 경로다.)
        private static readonly int AtmosphereThicknessId = Shader.PropertyToID("_AtmosphereThickness");
        private static readonly int SunSizeId = Shader.PropertyToID("_SunSize");
        private static readonly int GroundColorId = Shader.PropertyToID("_GroundColor");

        /// <summary>
        /// 버그 수정: 처음에는 RuntimeInitializeLoadType.AfterSceneLoad로 한 번만 생성했는데, 이
        /// 훅은 "플레이 시작 후 첫 씬 로드"에만 호출되고 이후 SceneManager.LoadScene으로 씬을
        /// 다시 불러올 때(예: GameOverController.RestartGame으로 사망 후 재시작)는 다시 호출되지
        /// 않는다는 것을 재시작 라이브 테스트에서 확인했다. DayNightCycle은 DontDestroyOnLoad가
        /// 아니므로 재시작 시 기존 인스턴스가 씬과 함께 파괴된 뒤 새로 생성되지 않아, 재시작한
        /// 게임에서는 밤/낮 주기가 조용히 사라지는 문제가 있었다. SubsystemRegistration 시점에
        /// SceneManager.sceneLoaded 이벤트를 한 번만 구독해두면, 씬이 몇 번을 다시 로드되더라도
        /// (최초 시작이든 재시작이든) 그때마다 새 씬에 맞는 DayNightCycle이 매번 새로 생성된다.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Bootstrap()
        {
            SceneManager.sceneLoaded += (scene, mode) =>
            {
                if (FindAnyObjectByType<DayNightCycle>() != null)
                    return;

                var go = new GameObject("DayNightCycle");
                go.AddComponent<DayNightCycle>();
            };
        }

        /// <summary>
        /// 씬에서 Directional Light와 SurvivalClock을 찾아 참조를 캐시해둔다.
        /// 둘 중 하나라도 없으면 이 컴포넌트는 아무 동작도 하지 않는다(방어적 설계).
        /// </summary>
        private void Start()
        {
            sunLight = FindDirectionalLight();
            clock = FindAnyObjectByType<SurvivalClock>();
            weather = FindAnyObjectByType<WeatherSystem>();

            ApplyNewGameStartTime();

            // 버그 수정: 그동안 태양광 색상/밝기만 낮밤에 맞춰 바뀌고 하늘(스카이박스)은 항상 기본값
            // 그대로라, 조명은 붉게 물드는데 하늘은 계속 낮처럼 파랗게 보이는 어색함이 있었다.
            // RenderSettings.skybox(기본 Skybox/Procedural)를 복제해 _SkyTint/_Exposure를 매 프레임
            // 태양광과 같은 리듬으로 보간하면 노을/밤하늘까지 자연스럽게 이어진다.
            if (RenderSettings.skybox != null)
            {
                skyboxInstance = new Material(RenderSettings.skybox);
                if (skyboxInstance.HasProperty(SkyTintId))
                    RenderSettings.skybox = skyboxInstance;
                else
                    skyboxInstance = null; // 지원하지 않는 셰이더면 건드리지 않고 그대로 둔다
            }
        }

        /// <summary>
        /// 새 게임일 때만 시계를 아침으로 앞당긴다.
        /// elapsedSeconds가 0보다 크면 이미 진행 중인 게임(또는 불러오기로 복원된 시각)이므로 절대 건드리지 않는다.
        /// ElapsedDays는 floor(elapsedSeconds / secondsPerDay)이고 newGameStartTimeOfDay는 1 미만이므로,
        /// 이 보정을 해도 여전히 0일차(HUD 표기 "1일차")로 시작한다 - 경과 일수 표기는 바뀌지 않는다.
        /// </summary>
        private void ApplyNewGameStartTime()
        {
            if (clock == null || newGameStartTimeOfDay <= 0f)
                return;

            if (clock.elapsedSeconds > 0f || clock.secondsPerDay <= 0f)
                return;

            clock.elapsedSeconds = Mathf.Clamp01(newGameStartTimeOfDay) * clock.secondsPerDay;
        }

        /// <summary>
        /// 씬의 모든 Light 중 타입이 Directional인 첫 번째 광원을 찾는다.
        /// UnderwaterAmbience도 같은 판정이 필요해 여기 정본을 공용으로 쓴다(internal static).
        /// </summary>
        internal static Light FindDirectionalLight()
        {
            var lights = FindObjectsByType<Light>(FindObjectsInactive.Exclude);
            foreach (var light in lights)
            {
                if (light.type == LightType.Directional)
                    return light;
            }
            return null;
        }

        /// <summary>
        /// 매 프레임 하루 진행률에 맞춰 태양의 각도와 밝기/색상을 갱신한다.
        /// t=0(자정)~0.25(일출)~0.5(정오)~0.75(일몰)~1(다시 자정) 순으로 순환한다.
        /// </summary>
        private void Update()
        {
            if (sunLight == null || clock == null)
                return;

            // WeatherSystem도 DayNightCycle과 같은 sceneLoaded 콜백에서 생성되므로, Start() 시점에
            // 아직 존재하지 않아 null로 남을 수 있었다(그러면 비가 와도 rainDimFactor가 적용되지 않고
            // 아래 안개 제어가 WeatherSystem의 비 안개와 서로 덮어쓴다). 못 찾았을 때만 가끔 다시 찾는다.
            if (weather == null && Time.frameCount % 60 == 0)
                weather = FindAnyObjectByType<WeatherSystem>();

            float t = clock.TimeOfDay01;

            // 태양 각도: t=0.25(일출)에 지평선(0도), t=0.5(정오)에 머리 위(90도), t=0.75(일몰)에 다시
            // 지평선 반대편(180도)에 오도록 -90도 오프셋을 준 값.
            float sunAngle = (t * 360f) - 90f;

            // 낮 강도(0~1): 정오에 1, 자정에 0이 되는 코사인 곡선. dayFactor > 0.5가 곧 "태양이 지평선 위".
            float dayFactor = Mathf.Clamp01(Mathf.Cos((t - 0.5f) * Mathf.PI * 2f) * 0.5f + 0.5f);

            // 버그 수정: 예전에는 이 각도를 그대로 썼는데, 밤(180~360도) 구간에서는 Directional Light가
            // 지면 "아래에서 위로" 비추는 방향이 되어 지형/오브젝트의 윗면에 빛이 전혀 닿지 않았다.
            // 그래서 nightIntensity를 0.05에서 0.10으로 올려도 화면 밝기가 사실상 그대로였다(수치가
            // 아니라 각도 문제였다). 지평선 아래 구간을 180도 기준으로 접어 올려, 해가 진 뒤에는 같은
            // 궤도를 "달"이 이어서 지나가게 한다. 접는 지점(=일몰, 정확히 지평선)에서 각도가 연속이라
            // 전환 순간에 조명이 튀지 않고, 낮 구간(0~180도)의 태양 궤도는 예전과 100% 동일하다.
            float lightPitch = Mathf.Repeat(sunAngle, 360f);
            if (lightPitch > 180f)
                lightPitch = 360f - lightPitch;

            // [B23 달빛 방향] 위의 접기는 "지평선 아래에서 위로 비추는 빛"은 없앴지만, 밤 궤도가
            // 일몰 지점에서 다시 떠올라 일출 지점으로 지는 모양이라 자정 전후를 빼면 달빛이 지평선에
            // 낮게 걸려 그림자가 옆으로 길게 눕는 부자연스러움이 남아 있었다. 이제 태양 고도가
            // 지평선 아래로 내려가면(sinAlt < 0) 태양이 진 쪽(반대 방위, yaw 350) 하늘의 고정 고도에
            // 뜬 '달' 방향으로 Slerp 전환한다. sinAlt는 실제 태양 고도의 사인값(+1 정오, 0 지평선,
            // -1 자정)이라 전환 창을 지평선 근처의 좁은 구간으로 정확히 잡을 수 있고, SmoothStep +
            // Slerp라 전환 양 끝에서 각속도가 0으로 붙어 조명이 튀지 않는다. 일몰 쪽은 해가 진 바로
            // 그 방위에서 달이 떠오르는 그림이 되고, 일출 쪽은 달빛이 하늘을 부드럽게 가로질러
            // 떠오르는 해에게 자리를 넘긴다(이 구간은 조도가 최저라 스윕이 거의 눈에 띄지 않는다).
            // 낮 구간(0~180도)의 태양 궤도는 여전히 예전과 100% 동일하다.
            float sinAlt = Mathf.Sin(sunAngle * Mathf.Deg2Rad);
            float moonBlend = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.03f, -0.22f, sinAlt));
            Quaternion sunRotation = Quaternion.Euler(lightPitch, 170f, 0f);
            Quaternion moonRotation = Quaternion.Euler(moonAltitudeDeg, 350f, 0f);
            sunLight.transform.rotation = Quaternion.Slerp(sunRotation, moonRotation, moonBlend);

            // [B22] 예전에는 IsRaining이 켜지는 프레임에 조도가 55%로 **한 프레임 만에 툭 떨어졌다**.
            // WeatherSystem이 0~1로 서서히 오르내리는 RainIntensity01을 공개하므로 그 값으로 보간해
            // 구름이 몰려오듯 어두워지게 한다. (게임플레이 효과는 여전히 IsRaining 불리언으로만
            // 판정되고 이 값은 순수 연출이다 - 우천 수치는 한 개도 바뀌지 않는다.)
            float rainIntensity = weather != null ? weather.RainIntensity01 : 0f;
            float rainMultiplier = Mathf.Lerp(1f, weather != null ? weather.rainDimFactor : 1f, rainIntensity);
            sunLight.intensity = Mathf.Lerp(nightIntensity, dayIntensity, dayFactor) * rainMultiplier;

            // 일출/일몰(골든아워) 가중치. 예전에는 |dayFactor-0.5|의 선형 텐트라 폭이 너무 넓어서
            // 늦은 오전/이른 오후까지 30% 가까이 주황빛이 섞였고, 정작 노을 순간의 색은 밋밋했다.
            // 지수를 씌워 구간을 좁히고 대신 최대 혼합량을 0.6 -> 0.8로 올려, "낮은 중성 백색광 →
            // 짧고 진한 주황 노을 → 푸른 달빛"으로 색온도가 뚜렷하게 넘어가게 만든다.
            float duskDawnBlend = 1f - Mathf.Abs(dayFactor - 0.5f) * 2f; // dayFactor=0.5(지평선)에서 1
            float goldenHour = Mathf.Pow(Mathf.Clamp01(duskDawnBlend), 2.5f);

            // [B22] 오전(t<0.5)이면 새벽 색, 오후면 황혼 색을 고른다. goldenHour는 하루에 두 번
            // 똑같이 솟는 값이라 이 분기 하나만으로 아침/저녁이 다른 색이 된다(추가 계산 0회).
            bool isMorning = t < 0.5f;
            Color goldenLight = isMorning ? dawnColor : duskDawnColor;
            Color goldenSky = isMorning ? dawnSkyTint : duskDawnSkyTint;

            // [B23] 그림자 강도 주기: 낮 1.0 → 골든아워 0.8 → 밤 0.5. dayFactor로 낮/밤을 잇고
            // goldenHour로 일출/일몰 정점에서만 잠깐 0.8로 풀리게 한다(필드 주석 참고).
            float shadowStrength = Mathf.Lerp(nightShadowStrength, dayShadowStrength, dayFactor);
            sunLight.shadowStrength = Mathf.Lerp(shadowStrength, goldenShadowStrength, goldenHour);

            // 색상: 낮에는 백색광, 일출/일몰에는 노을빛, 밤에는 푸른 달빛으로 보간한다.
            Color baseColor = Color.Lerp(nightColor, dayColor, dayFactor);
            sunLight.color = Color.Lerp(baseColor, goldenLight, goldenHour * 0.8f);

            // 하늘도 태양광과 같은 dayFactor/goldenHour 리듬으로 색조와 노출을 보간한다.
            Color baseSky = Color.Lerp(nightSkyTint, daySkyTint, dayFactor);
            Color sky = Color.Lerp(baseSky, goldenSky, goldenHour * 0.8f);

            // 맑은 날 기준 안개. 새벽에만 밀도를 올려 아침 물안개를 깐다(황혼에는 올리지 않는다 -
            // 밤새 식은 공기에서 생기는 현상이라 저녁에 같은 안개가 끼면 오히려 어색하다).
            ClearFogColor = ResolveClearFogColor(sky, dayFactor, goldenHour, goldenLight);

            // 고도 배율은 이번 프레임에 한 번만 구해 맑은 안개와 비 안개 **양쪽에** 쓴다.
            // 맑은 쪽에만 곱하면, 비가 최고조일 때 보간 결과가 weather.rainFogDensity(고정 상수)로
            // 완전히 수렴해 고도 효과가 정확히 가장 필요한 순간에 사라진다(검수에서 잡힌 것).
            float heightFog = ResolveHeightFogFactor();

            ClearFogDensity = Mathf.Max(0f, clearFogDensity)
                * Mathf.Lerp(1f, Mathf.Max(1f, dawnFogDensityMultiplier), isMorning ? goldenHour : 0f)
                * heightFog;

            // 비 안개까지 반영한 "이번 프레임에 실제로 적용할" 값. 스카이박스 지평선 색과 RenderSettings가
            // 같은 값을 써야 수평선에 이음매가 생기지 않으므로 여기서 한 번만 계산한다.
            Color fogColor = ClearFogColor;
            float fogDensity = ClearFogDensity;
            if (weather != null && rainIntensity > 0f)
            {
                fogColor = Color.Lerp(fogColor, weather.rainFogColor, rainIntensity);
                fogDensity = Mathf.Lerp(fogDensity,
                    Mathf.Max(0f, weather.rainFogDensity) * heightFog, rainIntensity);
            }

            if (skyboxInstance != null)
            {
                skyboxInstance.SetColor(SkyTintId, sky);

                if (skyboxInstance.HasProperty(ExposureId))
                {
                    float exposure = Mathf.Lerp(nightSkyExposure, daySkyExposure, dayFactor);
                    skyboxInstance.SetFloat(ExposureId, exposure);
                }

                if (skyboxInstance.HasProperty(AtmosphereThicknessId))
                {
                    skyboxInstance.SetFloat(AtmosphereThicknessId,
                        Mathf.Lerp(dayAtmosphereThickness, goldenAtmosphereThickness, goldenHour));
                }

                if (skyboxInstance.HasProperty(SunSizeId))
                    skyboxInstance.SetFloat(SunSizeId, Mathf.Lerp(daySunSize, goldenSunSize, goldenHour));

                // [B22 수평선] 스카이박스는 안개의 영향을 받지 않는다. 그래서 far clip(1000m)에서
                // 바다 평면이 잘리면 그 바깥은 **생 스카이박스의 지평선 아래 색(_GroundColor, 기본 회갈색)**
                // 이 그대로 드러나, 안개색으로 사라져가던 바다가 갑자기 회색 띠로 바뀌는 경계선이 생긴다.
                // 지평선 아래 색을 지금 프레임의 안개색과 똑같이 맞추면 그 경계가 원리적으로 안 보인다.
                if (skyboxInstance.HasProperty(GroundColorId))
                    skyboxInstance.SetColor(GroundColorId, fogColor);
            }

            UpdateAmbientLight(dayFactor, goldenHour, rainMultiplier);
            UpdateAtmosphericFog(fogColor, fogDensity);
        }

        /// <summary>
        /// 시간대에 맞춰 환경광을 직접 제어한다.
        /// 씬의 Ambient Mode는 Skybox(m_AmbientMode: 0)인데, 스카이박스를 런타임에 교체/변경해도
        /// DynamicGI.UpdateEnvironment()를 부르지 않으면 환경광 구면조화(SH)는 씬 로드 시점 값에
        /// 그대로 얼어붙는다. 즉 하늘은 밤이 되는데 환경광만 씬 로드 시점(기본 스카이박스)의 밝기로
        /// 남아, 그림자면/역광면의 밝기가 낮과 밤에 거의 차이가 없었다.
        /// DynamicGI.UpdateEnvironment()를 매번 호출하는 방법도 있지만 SH 재계산 비용이 크고,
        /// 무엇보다 밤 스카이박스(#0D0F1F x 노출 0.15)에서 뽑은 환경광은 거의 0이라 "밤에 완전히
        /// 안 보인다"가 되어버린다. 그래서 Flat 모드로 바꾸고 밤 바닥값(nightAmbient)을 명시적으로
        /// 보장하는 쪽을 택했다 - ArtDirection 3장의 "밤은 어둡지만 실루엣은 읽혀야 한다"는 기준을
        /// 수치로 강제할 수 있는 유일한 방법이다.
        ///
        /// [B23] Flat(단색) → Trilight(하늘/수평선/지면 3색)로 확장. "SH 재계산 없이 코드가 환경광
        /// 바닥값을 명시적으로 보장한다"는 위 결론은 그대로이고, 단색 하나 대신 3색을 보장한다는
        /// 점만 다르다(Trilight도 Flat처럼 스카이박스와 무관하게 즉시 반영된다). 3색이 갈라지면
        /// 윗면·옆면·아랫면이 서로 다른 반사광을 받아 단색 환경광 특유의 밋밋한 그늘이 사라진다.
        /// </summary>
        private void UpdateAmbientLight(float dayFactor, float goldenHour, float rainMultiplier)
        {
            if (!driveAmbientLight)
                return;

            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;

            // 하늘/수평선/지면을 각각 밤→낮으로 잇고, 골든아워 정점에서 따뜻한 색을 덮는다.
            // 수평선/지면은 노을의 영향(0.85)을 하늘(0.6)보다 세게 받는다 - 실제로도 노을은
            // 천정보다 지평선 쪽을 훨씬 진하게 물들인다.
            Color sky = Color.Lerp(nightAmbient, dayAmbient, dayFactor);
            sky = Color.Lerp(sky, duskDawnAmbient, goldenHour * 0.6f);
            Color equator = Color.Lerp(nightAmbientEquator, dayAmbientEquator, dayFactor);
            equator = Color.Lerp(equator, goldenAmbientEquator, goldenHour * 0.85f);
            Color ground = Color.Lerp(nightAmbientGround, dayAmbientGround, dayFactor);
            ground = Color.Lerp(ground, goldenAmbientGround, goldenHour * 0.85f);

            // [B22] 비가 오면 색까지 채도가 빠진 차가운 회색 쪽으로 민다. 예전에는 밝기만 곱해서
            // "그냥 어두운 맑은 날"이었는데, 흐린 날의 실제 특징은 어둡다는 것보다 **색이 빠진다**는 것이다.
            // 젖은 표면을 만들 셰이더가 없는 이 파이프라인에서 "비 오는 날처럼 보이게" 만드는 가장 싼 수단이다.
            // [B23] Trilight에서는 3색 모두 같은 회색으로 수렴시킨다 - 흐린 날은 하늘/수평선/지면의
            // 색 차이 자체가 사라지는 것이 실제 모습이기도 하다.
            float rainBlend = Mathf.Clamp01(Mathf.Clamp01(1f - rainMultiplier) * 1.6f) * 0.5f;
            sky = Color.Lerp(sky, rainAmbientTint, rainBlend);
            equator = Color.Lerp(equator, rainAmbientTint, rainBlend);
            ground = Color.Lerp(ground, rainAmbientTint, rainBlend);

            // 비가 오면 태양광과 같은 비율로 환경광도 함께 죽인다. 다만 환경광까지 rainDimFactor를
            // 그대로 곱하면 낮인데도 시야가 지나치게 어두워지므로 절반만 적용한다.
            float ambientRainMultiplier = Mathf.Lerp(1f, rainMultiplier, 0.5f);
            RenderSettings.ambientSkyColor = ScaleRgb(sky, ambientRainMultiplier);
            RenderSettings.ambientEquatorColor = ScaleRgb(equator, ambientRainMultiplier);
            RenderSettings.ambientGroundColor = ScaleRgb(ground, ambientRainMultiplier);
        }

        /// <summary>
        /// 카메라 높이에 따른 안개 밀도 배율. 해수면에서 1, fogClearHeight 이상에서 fogDensityAtHeight.
        ///
        /// 비 안개에도 그대로 곱해진다(ClearFogDensity를 출발점으로 보간하기 때문이다). 그게 맞다 -
        /// 비구름 위로 올라가는 게 아니라 같은 비를 높은 데서 보는 것이니 조금 트이는 편이 자연스럽다.
        ///
        /// 카메라는 매 프레임 찾지 않고 캐시한다. Camera.main은 태그 검색이라 공짜가 아니다.
        /// </summary>
        private float ResolveHeightFogFactor()
        {
            if (fogCamera == null)
            {
                fogCamera = Camera.main;
                if (fogCamera == null)
                    return 1f;
            }

            float height = fogCamera.transform.position.y - OceanWaves.SeaLevel;
            float t = Mathf.Clamp01(height / Mathf.Max(1f, fogClearHeight));
            return Mathf.Lerp(1f, Mathf.Clamp(fogDensityAtHeight, 0.2f, 1f), t);
        }

        /// <summary>RGB에만 배율을 곱한다. Color * float는 알파까지 곱해버리므로 알파는 1로 고정한다.</summary>
        private static Color ScaleRgb(Color c, float multiplier)
        {
            return new Color(c.r * multiplier, c.g * multiplier, c.b * multiplier, 1f);
        }

        /// <summary>
        /// [B22] 맑은 날 기준 안개색을 만든다. 세 겹으로 쌓는다:
        ///   (1) 하늘색을 낮일수록 하얗게 — 스카이박스는 천정보다 지평선이 항상 밝고 옅다.
        ///   (2) 노을에는 태양색을 섞어 수평선 자체가 물들게 — 값싼 "노을 지는 바다".
        ///   (3) 밤에는 달빛 파랑을 섞어 수평선이 새까만 벽이 되지 않게("달빛 푸른 어둠", 디렉터 지시).
        /// </summary>
        private Color ResolveClearFogColor(Color skyColor, float dayFactor, float goldenHour, Color goldenLight)
        {
            Color fog = Color.Lerp(skyColor, Color.white, Mathf.Lerp(0.06f, 0.45f, dayFactor));
            // [B23] 태양색 혼합 0.45 → 0.55. goldenLight는 새벽(dawnColor)/황혼(duskDawnColor)이
            // 이미 갈라져 있으므로, 이 한 줄로 아침 안개는 분홍빛·저녁 안개는 주황빛으로 물든다.
            // 골든아워 정점의 수평선이 "살짝 데워진" 수준을 넘어 "노을에 잠긴" 수준으로 읽히게 했다.
            fog = Color.Lerp(fog, goldenLight, goldenHour * 0.55f);

            // Color * float는 알파까지 곱하므로 채널을 직접 만든다.
            var moonFog = new Color(nightColor.r * 0.42f, nightColor.g * 0.42f, nightColor.b * 0.42f, 1f);
            fog = Color.Lerp(fog, moonFog, (1f - dayFactor) * 0.35f);

            fog.a = 1f;
            return fog;
        }

        /// <summary>
        /// 하늘색과 같은 색의 거리 안개를 깔아, 40000 크기의 단색 바다 평면이 수평선에서 하늘과
        /// 자연스럽게 이어지게 한다(셰이더 없이 원근감을 만드는 유일한 수단).
        ///
        /// [B22] 예전에는 비가 오는 동안 통째로 return해서 WeatherSystem이 따로 안개를 썼고, 그래서
        /// 비가 시작/종료되는 프레임에 안개가 **툭** 바뀌었다(두 스크립트가 같은 전역 상태를 번갈아
        /// 덮어쓰는, 이 프로젝트가 반복해서 사고를 낸 형태이기도 하다). 이제 안개를 쓰는 곳은 여기
        /// 한 곳뿐이고, 비 안개는 WeatherSystem이 공개하는 값을 읽어 RainIntensity01로 보간한다.
        /// </summary>
        private void UpdateAtmosphericFog(Color fogColor, float fogDensity)
        {
            if (!enableAtmosphericFog)
                return;

            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = fogColor;
            RenderSettings.fogDensity = fogDensity;
        }
    }
}
