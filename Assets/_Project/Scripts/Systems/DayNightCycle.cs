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

        [Tooltip("밤 동안의 최소 조명 강도 (완전한 암흑은 아니고 은은하게 남겨둔다)")]
        public float nightIntensity = 0.05f;

        [Tooltip("한낮의 조명 색상 (밝은 백색광)")]
        public Color dayColor = new Color(1f, 0.98f, 0.92f);

        [Tooltip("일출/일몰 무렵의 조명 색상 (붉은 노을빛)")]
        public Color duskDawnColor = new Color(1f, 0.6f, 0.35f);

        [Tooltip("한밤중의 조명 색상 (푸르스름한 달빛)")]
        public Color nightColor = new Color(0.4f, 0.5f, 0.7f);

        [Header("하늘(스카이박스) 색조")]
        [Tooltip("낮 하늘의 색조 (Skybox/Procedural의 _SkyTint)")]
        public Color daySkyTint = new Color(0.45f, 0.65f, 0.85f);

        [Tooltip("노을 무렵 하늘의 색조")]
        public Color duskDawnSkyTint = new Color(0.85f, 0.5f, 0.35f);

        [Tooltip("밤하늘의 색조 (짙은 남색)")]
        public Color nightSkyTint = new Color(0.05f, 0.06f, 0.12f);

        [Tooltip("낮 하늘의 노출(밝기)")]
        public float daySkyExposure = 1.15f;

        [Tooltip("밤하늘의 노출(밝기) - 별이 반짝일 정도로 어둡게")]
        public float nightSkyExposure = 0.15f;

        private Light sunLight;
        private SurvivalClock clock;
        private WeatherSystem weather;

        /// <summary>
        /// 런타임에만 색을 바꾸기 위해 원본(Default-Skybox 등 공유 에셋)을 복제한 인스턴스.
        /// 공유 머티리얼을 직접 건드리면 다른 씬/에디터 상태에도 영향을 줄 수 있어 항상 복제본을 쓴다.
        /// </summary>
        private Material skyboxInstance;
        private static readonly int SkyTintId = Shader.PropertyToID("_SkyTint");
        private static readonly int ExposureId = Shader.PropertyToID("_Exposure");

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
        /// 씬의 모든 Light 중 타입이 Directional인 첫 번째 광원을 찾는다.
        /// </summary>
        private Light FindDirectionalLight()
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

            float t = clock.TimeOfDay01;

            // 태양 각도: t=0.5(정오)일 때 머리 위(90도)에 가깝고, t=0/1(자정)일 때 지평선 아래로 내려가도록
            // 360도를 한 바퀴 회전시킨다. -90도 오프셋을 주면 t=0.25(일출)에 지평선 근처(0도)에서 시작한다.
            float sunAngle = (t * 360f) - 90f;
            sunLight.transform.rotation = Quaternion.Euler(sunAngle, 170f, 0f);

            // 낮 강도(0~1): 정오에 1, 자정에 0이 되는 코사인 곡선. 태양이 지평선 아래일 때는 0으로 클램프.
            float dayFactor = Mathf.Clamp01(Mathf.Cos((t - 0.5f) * Mathf.PI * 2f) * 0.5f + 0.5f);
            float rainMultiplier = (weather != null && weather.IsRaining) ? weather.rainDimFactor : 1f;
            sunLight.intensity = Mathf.Lerp(nightIntensity, dayIntensity, dayFactor) * rainMultiplier;

            // 색상: 낮에는 백색광, 일출/일몰 무렵(dayFactor가 중간값)에는 노을빛, 밤에는 푸른 달빛으로 보간한다.
            Color baseColor = Color.Lerp(nightColor, dayColor, dayFactor);
            float duskDawnBlend = 1f - Mathf.Abs(dayFactor - 0.5f) * 2f; // dayFactor=0.5 부근(여명/노을)에서 1에 가까워짐
            sunLight.color = Color.Lerp(baseColor, duskDawnColor, Mathf.Clamp01(duskDawnBlend) * 0.6f);

            // 하늘도 태양광과 같은 dayFactor/duskDawnBlend 리듬으로 색조와 노출을 보간한다.
            if (skyboxInstance != null)
            {
                Color baseSky = Color.Lerp(nightSkyTint, daySkyTint, dayFactor);
                Color sky = Color.Lerp(baseSky, duskDawnSkyTint, Mathf.Clamp01(duskDawnBlend) * 0.6f);
                skyboxInstance.SetColor(SkyTintId, sky);

                if (skyboxInstance.HasProperty(ExposureId))
                {
                    float exposure = Mathf.Lerp(nightSkyExposure, daySkyExposure, dayFactor);
                    skyboxInstance.SetFloat(ExposureId, exposure);
                }
            }
        }
    }
}
