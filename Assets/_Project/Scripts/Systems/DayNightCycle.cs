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
        public float nightIntensity = 0.10f;

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

        [Tooltip("한낮의 환경광(하늘 반사광). 씬 기본 스카이박스 환경광과 비슷한 밝기로 맞춰둔 값.")]
        public Color dayAmbient = new Color(0.45f, 0.48f, 0.52f);

        [Tooltip("일출/일몰 무렵의 환경광(따뜻하고 채도가 낮은 색)")]
        public Color duskDawnAmbient = new Color(0.38f, 0.29f, 0.25f);

        [Tooltip("한밤중의 환경광. 이 값이 밤의 '최소 가시성 바닥'이다 - 0에 가까우면 실루엣조차 안 보인다.")]
        public Color nightAmbient = new Color(0.10f, 0.12f, 0.18f);

        [Tooltip("켜면 하늘색과 같은 색의 옅은 거리 안개를 깔아 수평선에서 바다와 하늘이 이어지게 한다.")]
        public bool enableAtmosphericFog = true;

        [Tooltip("맑은 날의 거리 안개 밀도(Exponential). 카메라 far clip이 1000이라 이 정도면 " +
            "가까운 곳은 선명하고 수평선만 옅게 흐려진다. WeatherSystem의 rainFogDensity와는 별개 값이다.")]
        public float clearFogDensity = 0.0012f;

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
            sunLight.transform.rotation = Quaternion.Euler(lightPitch, 170f, 0f);

            float rainMultiplier = (weather != null && weather.IsRaining) ? weather.rainDimFactor : 1f;
            sunLight.intensity = Mathf.Lerp(nightIntensity, dayIntensity, dayFactor) * rainMultiplier;

            // 일출/일몰(골든아워) 가중치. 예전에는 |dayFactor-0.5|의 선형 텐트라 폭이 너무 넓어서
            // 늦은 오전/이른 오후까지 30% 가까이 주황빛이 섞였고, 정작 노을 순간의 색은 밋밋했다.
            // 지수를 씌워 구간을 좁히고 대신 최대 혼합량을 0.6 -> 0.8로 올려, "낮은 중성 백색광 →
            // 짧고 진한 주황 노을 → 푸른 달빛"으로 색온도가 뚜렷하게 넘어가게 만든다.
            float duskDawnBlend = 1f - Mathf.Abs(dayFactor - 0.5f) * 2f; // dayFactor=0.5(지평선)에서 1
            float goldenHour = Mathf.Pow(Mathf.Clamp01(duskDawnBlend), 2.5f);

            // 색상: 낮에는 백색광, 일출/일몰에는 노을빛, 밤에는 푸른 달빛으로 보간한다.
            Color baseColor = Color.Lerp(nightColor, dayColor, dayFactor);
            sunLight.color = Color.Lerp(baseColor, duskDawnColor, goldenHour * 0.8f);

            // 하늘도 태양광과 같은 dayFactor/goldenHour 리듬으로 색조와 노출을 보간한다.
            Color baseSky = Color.Lerp(nightSkyTint, daySkyTint, dayFactor);
            Color sky = Color.Lerp(baseSky, duskDawnSkyTint, goldenHour * 0.8f);

            if (skyboxInstance != null)
            {
                skyboxInstance.SetColor(SkyTintId, sky);

                if (skyboxInstance.HasProperty(ExposureId))
                {
                    float exposure = Mathf.Lerp(nightSkyExposure, daySkyExposure, dayFactor);
                    skyboxInstance.SetFloat(ExposureId, exposure);
                }
            }

            UpdateAmbientLight(dayFactor, goldenHour, rainMultiplier);
            UpdateAtmosphericFog(sky, dayFactor);
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
        /// </summary>
        private void UpdateAmbientLight(float dayFactor, float goldenHour, float rainMultiplier)
        {
            if (!driveAmbientLight)
                return;

            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;

            Color ambient = Color.Lerp(nightAmbient, dayAmbient, dayFactor);
            ambient = Color.Lerp(ambient, duskDawnAmbient, goldenHour * 0.6f);

            // 비가 오면 태양광과 같은 비율로 환경광도 함께 죽인다. 다만 환경광까지 rainDimFactor를
            // 그대로 곱하면 낮인데도 시야가 지나치게 어두워지므로 절반만 적용한다.
            float ambientRainMultiplier = Mathf.Lerp(1f, rainMultiplier, 0.5f);
            // Color * float는 알파까지 곱해버린다. 환경광 색의 알파는 1로 고정해 둔다.
            RenderSettings.ambientLight = new Color(
                ambient.r * ambientRainMultiplier,
                ambient.g * ambientRainMultiplier,
                ambient.b * ambientRainMultiplier,
                1f);
        }

        /// <summary>
        /// 하늘색과 같은 색의 옅은 거리 안개를 깔아, 40000 크기의 단색 바다 평면이 수평선에서 하늘과
        /// 자연스럽게 이어지게 한다(바다 표현 개선: 거리에 따른 수면 색 변화를 셰이더 없이 만드는 방법).
        /// 비가 오는 동안은 WeatherSystem이 자기 안개(rainFogColor/rainFogDensity)를 쓰므로 손대지 않는다.
        /// </summary>
        private void UpdateAtmosphericFog(Color skyColor, float dayFactor)
        {
            if (!enableAtmosphericFog)
                return;

            if (weather != null && weather.IsRaining)
                return;

            // 스카이박스는 천정보다 지평선이 항상 더 밝고 옅다. 하늘 색조를 그대로 안개색으로 쓰면
            // 수평선 부근이 하늘보다 어두워져 오히려 검은 띠가 생기므로, 낮일수록 더 하얗게 섞는다.
            Color fogColor = Color.Lerp(skyColor, Color.white, Mathf.Lerp(0.1f, 0.45f, dayFactor));

            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Exponential;
            RenderSettings.fogColor = fogColor;
            RenderSettings.fogDensity = Mathf.Max(0f, clearFogDensity);
        }
    }
}
