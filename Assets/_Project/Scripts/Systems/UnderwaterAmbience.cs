using UnityEngine;
using UnityEngine.SceneManagement;

namespace MakeGame.Systems
{
    /// <summary>
    /// 카메라가 해수면(WorldMapManager.seaLevel) 아래로 내려가면 화면을 "물속"으로 보이게 하는
    /// 수중 분위기 전환. 바다가 반투명이 되면서 잠수가 시각적으로 의미 있어졌는데, 물속에 들어가도
    /// 안개/환경광이 물 밖과 똑같아 "수면 아래로 카메라만 통과한" 느낌이었다 - 그 반쪽을 채운다.
    ///
    /// ── 안개/환경광 소유권과의 충돌 회피 설계 ─────────────────────────────────────
    /// 이 프로젝트에서 RenderSettings 안개의 단독 소유자는 DayNightCycle.Update다(맑은 날 기준값
    /// ClearFogColor/ClearFogDensity를 계산하고, WeatherSystem의 비 안개까지 RainIntensity01로
    /// 보간해 매 프레임 기록한다 - DayNightCycle.UpdateAtmosphericFog 주석 참고). 환경광(Trilight
    /// 3색)도 마찬가지로 DayNightCycle.UpdateAmbientLight가 매 프레임 기록한다. 두 스크립트가 같은
    /// 전역 상태를 번갈아 덮어쓰는 형태는 이 프로젝트가 반복해서 사고를 낸 패턴이므로(WeatherSystem
    /// 클래스 주석 참고), 여기서는 그 계약을 전혀 건드리지 않는다:
    ///  · 이 컴포넌트는 **LateUpdate**에서만, **수중일 때만** RenderSettings를 덮어쓴다.
    ///    같은 프레임 안에서 DayNightCycle.Update(안개/환경광 기록) → 본 LateUpdate(수중이면
    ///    덮어쓰기) 순서가 보장되므로, 렌더링 직전의 "마지막 승자"가 항상 이 컴포넌트다.
    ///    DayNightCycle 쪽 코드는 한 줄도 바뀌지 않고, ClearFogColor/ClearFogDensity 계약도
    ///    그대로 살아 있다(수면 위로 나오는 즉시 그 값이 다시 화면을 지배한다).
    ///  · 물 밖에서는 **아무것도 하지 않는다**. 복원 코드가 아예 없으므로 "되돌리기 전에
    ///    씬 저장/재로드가 끼면 값이 굳는" 류의 사고가 원리적으로 불가능하다 - DayNightCycle이
    ///    어차피 다음 프레임 Update에서 안개/환경광을 처음부터 다시 기록한다.
    ///  · 타이틀 화면(Time.timeScale=0)에서도 LateUpdate는 돌지만, 카메라가 물 위에 있는 한
    ///    위 원칙대로 즉시 return하므로 무해하다.
    ///
    /// 씬에 미리 배치할 필요 없이 DayNightCycle/AtmospherePostFX와 같은
    /// RuntimeInitializeOnLoadMethod 패턴으로 씬 로드 시 자동 생성된다(씬 수정 없음).
    /// 성능: 프레임당 몇 줄의 산술 연산뿐이고 정상 경로 할당은 0이다(참조 탐색은 캐시 후
    /// null일 때만 저빈도로 재시도).
    /// </summary>
    public class UnderwaterAmbience : MonoBehaviour
    {
        [Header("수중 안개")]
        // [설계] 주간 만수면 기준의 깊은 청록. 열대 바다의 물속은 붉은 파장이 수 미터 안에 흡수되어
        // 청록만 남는다 - 채도를 과하게 올리지 않아도 이 색 하나로 "물속"이 즉시 읽힌다.
        [Tooltip("주간 · 수심 0m 기준의 수중 안개 색(깊은 청록). 실제 색은 현재 조명 강도와 수심에 비례해 어두워진다.")]
        public Color underwaterFogColor = new Color(0.07f, 0.25f, 0.33f);

        // ExponentialSquared 0.055 기준 잔여 시야: 10m 74% · 15m 51% · 20m 30% · 30m 6.5%.
        // "약 15~20m 앞까지 형체가 읽히고 그 너머는 물빛에 잠긴다"는 목표 수치다.
        // DayNightCycle의 clearFogDensity(0.0016)와는 자릿수가 다른 별개 값이라 헷갈릴 일이 없다.
        [Tooltip("수심 0m 기준의 수중 안개 밀도(ExponentialSquared). 시야 약 15~20m.")]
        public float underwaterFogDensity = 0.055f;

        [Header("수중 환경광 (Trilight 3색)")]
        // DayNightCycle이 Trilight로 몰고 있으므로 같은 모드의 3색을 전부 덮는다(하나라도 남기면
        // 물 밖의 하늘/노을 반사광이 물속 오브젝트에 그대로 얹혀 어색하다). 물속의 빛은 전부
        // 수면에서 내려오므로 하늘>수평선>지면 순으로 급격히 어두워지는 것이 실제 모습이다.
        [Tooltip("수중 환경광 '하늘' 색(수면 쪽에서 내려오는 빛). 주간 · 수심 0m 기준.")]
        public Color underwaterAmbientSky = new Color(0.16f, 0.38f, 0.46f);

        [Tooltip("수중 환경광 '수평선' 색. 주간 · 수심 0m 기준.")]
        public Color underwaterAmbientEquator = new Color(0.07f, 0.20f, 0.27f);

        [Tooltip("수중 환경광 '지면' 색(아래는 빛이 거의 닿지 않는다). 주간 · 수심 0m 기준.")]
        public Color underwaterAmbientGround = new Color(0.02f, 0.07f, 0.10f);

        [Header("깊이 감쇠")]
        [Tooltip("깊이 감쇠가 최대치에 도달하는 수심(m).")]
        public float maxAttenuationDepth = 20f;

        [Tooltip("최대 수심에서 안개 밀도에 곱할 배율(깊을수록 시야가 좁아진다).")]
        public float depthDensityMultiplier = 1.6f;

        [Tooltip("최대 수심에서 밝기(안개색/환경광)에 곱할 배율(깊을수록 어두워진다).")]
        public float depthBrightnessMultiplier = 0.4f;

        /// <summary>
        /// 이번 프레임 메인 카메라가 해수면 아래에 있는지. 다음 웨이브의 해저 연출/수중 사운드
        /// 작업이 읽는 공개 계약이다. 카메라나 WorldMapManager를 못 찾으면 항상 false.
        /// </summary>
        public static bool IsUnderwater { get; private set; }

        private Camera targetCamera;
        private WorldMapManager worldMap;
        private DayNightCycle dayNight;
        private Light sunLight;

        /// <summary>
        /// DayNightCycle.Bootstrap과 같은 패턴: AfterSceneLoad는 "플레이 시작 후 첫 씬 로드"에만
        /// 호출되고 재시작(SceneManager.LoadScene)에는 다시 호출되지 않아, 재시작한 게임에서
        /// 수중 연출이 조용히 사라진다(DayNightCycle.Bootstrap 주석에서 라이브 테스트로 확인된
        /// 사실). SubsystemRegistration에서 sceneLoaded를 한 번만 구독해 씬이 로드될 때마다 새로
        /// 만들고, 이미 살아 있는 인스턴스가 있으면(중복 생성 가드, AtmospherePostFX와 동일) 건너뛴다.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Bootstrap()
        {
            SceneManager.sceneLoaded += (scene, mode) =>
            {
                if (FindAnyObjectByType<UnderwaterAmbience>() != null)
                    return;

                var go = new GameObject("UnderwaterAmbience");
                go.AddComponent<UnderwaterAmbience>();
            };
        }

        /// <summary>
        /// 참조를 한 번 캐시한다. WorldMapManager/DayNightCycle은 같은 sceneLoaded 콜백에서
        /// 생성/초기화되는 순서에 따라 Start 시점에 아직 없을 수 있으므로, 못 찾은 것은
        /// LateUpdate에서 저빈도로 재시도한다(DayNightCycle이 WeatherSystem을 찾는 것과 같은 규칙).
        /// </summary>
        private void Start()
        {
            targetCamera = Camera.main;
            worldMap = FindAnyObjectByType<WorldMapManager>();
            dayNight = FindAnyObjectByType<DayNightCycle>();
            sunLight = FindDirectionalLight();
        }

        /// <summary>
        /// 씬 재로드로 파괴될 때 정적 상태를 정리한다. 다음 씬에서 카메라가 물 위에서 시작하는데
        /// IsUnderwater가 true로 굳어 있으면 다음 웨이브의 수중 사운드가 한 프레임 잘못 판정한다.
        /// </summary>
        private void OnDestroy()
        {
            IsUnderwater = false;
        }

        /// <summary>씬의 Light 중 Directional 타입 첫 번째를 찾는다(DayNightCycle과 동일한 방식).</summary>
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
        /// 매 프레임 수중 판정 후, 수중일 때만 안개/환경광을 덮어쓴다. LateUpdate인 이유는
        /// 클래스 주석의 소유권 설계 참고 - DayNightCycle.Update가 기록을 끝낸 뒤의 "마지막 승자"
        /// 자리를 차지하기 위해서다. 물 밖에서는 즉시 return하므로 타이틀 화면(timeScale=0)에서도 무해하다.
        /// </summary>
        private void LateUpdate()
        {
            // 카메라는 파괴/재생성될 수 있으므로(파괴된 오브젝트 == null 규칙) null이면 다시 집는다.
            if (targetCamera == null)
                targetCamera = Camera.main;

            // WorldMapManager는 씬 로드 직후 생성 순서에 따라 늦게 나타날 수 있다. 못 찾았을 때만
            // 가끔 재시도한다(DayNightCycle이 WeatherSystem을 60프레임마다 재탐색하는 것과 같은 규칙 -
            // 정상 경로에서는 탐색 비용/할당이 0이다).
            if (worldMap == null && Time.frameCount % 60 == 0)
                worldMap = FindAnyObjectByType<WorldMapManager>();

            if (targetCamera == null || worldMap == null)
            {
                IsUnderwater = false;
                return;
            }

            float depth = worldMap.seaLevel - targetCamera.transform.position.y;
            IsUnderwater = depth > 0f;

            // 물 밖: 아무것도 하지 않는다. DayNightCycle이 이번 프레임 Update에서 이미 안개/환경광을
            // 기록했고 다음 프레임에도 계속 기록하므로, 복원은 자연히 그쪽 몫이다(클래스 주석 참고).
            if (!IsUnderwater)
                return;

            // 조명/시간대 참조도 같은 저빈도 재시도 규칙을 따른다(수중에서만 필요하므로 여기서 재시도).
            if (dayNight == null && Time.frameCount % 60 == 0)
                dayNight = FindAnyObjectByType<DayNightCycle>();
            if (sunLight == null && Time.frameCount % 60 == 0)
                sunLight = FindDirectionalLight();

            // 현재 조명 강도(0~1). DayNightCycle.Update가 이번 프레임 sunLight.intensity에 기록한
            // 값을 그대로 읽어 dayIntensity로 정규화한다 - 밤/비까지 이미 반영된 최종값이라
            // 별도 시간 계산 없이 "밤 잠수는 거의 암흑, 비 오는 낮은 침침"이 공짜로 성립한다.
            float lightFactor = 1f;
            if (sunLight != null && dayNight != null && dayNight.dayIntensity > 0f)
                lightFactor = Mathf.Clamp01(sunLight.intensity / dayNight.dayIntensity);

            // 깊이 감쇠: 수심 0m → maxAttenuationDepth(20m)에서 밀도 1.6배 · 밝기 0.4배.
            float depth01 = Mathf.Clamp01(depth / Mathf.Max(1f, maxAttenuationDepth));
            float density = Mathf.Max(0f, underwaterFogDensity)
                * Mathf.Lerp(1f, depthDensityMultiplier, depth01);
            float brightness = lightFactor * Mathf.Lerp(1f, depthBrightnessMultiplier, depth01);

            // 수중 안개. ExponentialSquared는 근거리 감쇠가 거의 없어 손/장비는 선명하고 먼 곳만
            // 물빛에 잠긴다(DayNightCycle이 같은 모드를 쓰는 이유와 동일).
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = ScaleRgb(underwaterFogColor, brightness);
            RenderSettings.fogDensity = density;

            // 수중 환경광. DayNightCycle과 같은 Trilight 모드로 3색 전부 덮는다(필드 주석 참고).
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = ScaleRgb(underwaterAmbientSky, brightness);
            RenderSettings.ambientEquatorColor = ScaleRgb(underwaterAmbientEquator, brightness);
            RenderSettings.ambientGroundColor = ScaleRgb(underwaterAmbientGround, brightness);
        }

        /// <summary>RGB에만 배율을 곱한다. Color * float는 알파까지 곱하므로 알파는 1로 고정한다(DayNightCycle과 동일).</summary>
        private static Color ScaleRgb(Color c, float multiplier)
        {
            return new Color(c.r * multiplier, c.g * multiplier, c.b * multiplier, 1f);
        }
    }
}
