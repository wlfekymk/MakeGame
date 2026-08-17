using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace MakeGame.Systems
{
    /// <summary>
    /// URP 포스트 프로세싱(톤매핑/블룸/색보정/화이트밸런스/비네트)을 씬 수정 없이 전부 런타임
    /// 코드로 구성한다. 글로벌 Volume + VolumeProfile을 CreateInstance로 만들어 붙이고, 메인
    /// 카메라의 renderPostProcessing을 켠 뒤, SurvivalClock.TimeOfDay01에 맞춰 골든아워/밤의
    /// 색온도·블룸·비네트를 매 프레임 부드럽게 보간한다.
    /// URP가 아니거나 카메라/시계를 못 찾으면 조용히 아무것도 하지 않는다(방어적 설계).
    /// </summary>
    public class AtmospherePostFX : MonoBehaviour
    {
        [Header("블룸")]
        [Tooltip("블룸 임계값. 1 이상이라 HDR 하이라이트(태양/모닥불)만 번진다.")]
        public float bloomThreshold = 1.05f;

        [Tooltip("낮/밤 기본 블룸 강도")]
        public float bloomIntensityDay = 0.25f;

        [Tooltip("골든아워(일출/일몰 정점)의 블룸 강도. 지평선의 해가 크게 번지게 한다.")]
        public float bloomIntensityGolden = 0.45f;

        [Tooltip("블룸 산란(퍼짐) 정도")]
        public float bloomScatter = 0.6f;

        [Header("색 보정 (고정값)")]
        [Tooltip("채도 보정(+8이면 살짝 진한 색감)")]
        public float saturation = 8f;

        [Tooltip("대비 보정")]
        public float contrast = 6f;

        [Header("화이트밸런스 (시간대 연동)")]
        [Tooltip("낮의 색온도(0 = 중립)")]
        public float temperatureDay = 0f;

        [Tooltip("골든아워의 색온도. 양수라 화면 전체가 따뜻한 주황 쪽으로 기운다.")]
        public float temperatureGolden = 12f;

        [Tooltip("밤의 색온도. 음수라 푸른 달빛 쪽으로 기운다.")]
        public float temperatureNight = -12f;

        [Header("비네트 (시간대 연동)")]
        [Tooltip("낮의 비네트 강도")]
        public float vignetteIntensityDay = 0.16f;

        [Tooltip("밤의 비네트 강도. 살짝 더 조여 시야가 좁아진 느낌을 준다.")]
        public float vignetteIntensityNight = 0.22f;

        [Tooltip("비네트 가장자리 부드러움")]
        public float vignetteSmoothness = 0.5f;

        [Header("시간대 연동")]
        [Tooltip("골든아워 폭. TimeOfDay01이 일출(0.25)/일몰(0.75) ±이 값 안이면 골든아워 가중치가 붙는다.")]
        public float goldenHourHalfWidth = 0.045f;

        [Tooltip("현재값이 목표값을 따라가는 속도(클수록 빠르게 수렴). Time.deltaTime 기반이라 " +
            "일시정지(timeScale=0)에서는 값이 그대로 멈춘다.")]
        public float blendSpeed = 2.5f;

        private VolumeProfile profile;
        private Volume volume;
        private Bloom bloom;
        private WhiteBalance whiteBalance;
        private Vignette vignette;

        private SurvivalClock clock;
        private Camera targetCamera;

        /// <summary>현재 카메라에 renderPostProcessing을 이미 켰는지. 카메라가 사라지면 다시 false로 돌린다.</summary>
        private bool cameraPostEnabled;

        // 매 프레임 목표값으로 Lerp되는 현재값(부드러운 전환의 상태).
        private float curBloomIntensity;
        private float curTemperature;
        private float curVignetteIntensity;

        /// <summary>
        /// DayNightCycle.Bootstrap과 같은 패턴: AfterSceneLoad는 "플레이 시작 후 첫 씬 로드"에만
        /// 호출되고 재시작(SceneManager.LoadScene)에는 다시 호출되지 않아, 재시작한 게임에서
        /// 포스트 프로세싱이 조용히 사라진다(DayNightCycle.cs:161 주석에서 라이브 테스트로 확인된
        /// 사실). SubsystemRegistration에서 sceneLoaded를 한 번만 구독해 씬이 로드될 때마다 새로
        /// 만들고, 이미 살아 있는 인스턴스가 있으면(중복 생성 가드) 건너뛴다.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Bootstrap()
        {
            SceneManager.sceneLoaded += (scene, mode) =>
            {
                if (FindAnyObjectByType<AtmospherePostFX>() != null)
                    return;

                var go = new GameObject("AtmospherePostFX");
                go.AddComponent<AtmospherePostFX>();
            };
        }

        /// <summary>
        /// URP일 때만 프로파일/볼륨을 구성한다. URP가 아니면(빌트인 등) 아무것도 만들지 않고
        /// 스스로 꺼진다 - 에러 로그도 내지 않는다.
        /// </summary>
        private void Start()
        {
            if (!(GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset))
            {
                enabled = false;
                return;
            }

            BuildProfile();

            volume = gameObject.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 10f;
            // sharedProfile로 연결한다. volume.profile 게터는 프로파일을 복제해버려서
            // OnDestroy에서 우리가 만든 원본만 지우면 복제본이 새는 구멍이 생긴다.
            volume.sharedProfile = profile;

            curBloomIntensity = bloomIntensityDay;
            curTemperature = temperatureDay;
            curVignetteIntensity = vignetteIntensityDay;

            clock = FindAnyObjectByType<SurvivalClock>();
        }

        /// <summary>
        /// 런타임 전용 VolumeProfile을 만들고 오버라이드 5종을 코드 값으로 구성한다.
        /// profile.Add&lt;T&gt;(true)는 모든 파라미터의 overrideState를 켠 컴포넌트를 추가하고,
        /// 개별 값은 .Override(...)로 명시한다.
        /// </summary>
        private void BuildProfile()
        {
            profile = ScriptableObject.CreateInstance<VolumeProfile>();

            var tonemapping = profile.Add<Tonemapping>(true);
            tonemapping.mode.Override(TonemappingMode.Neutral);

            bloom = profile.Add<Bloom>(true);
            bloom.threshold.Override(bloomThreshold);
            bloom.intensity.Override(bloomIntensityDay);
            bloom.scatter.Override(bloomScatter);

            var colorAdjustments = profile.Add<ColorAdjustments>(true);
            colorAdjustments.saturation.Override(saturation);
            colorAdjustments.contrast.Override(contrast);

            whiteBalance = profile.Add<WhiteBalance>(true);
            whiteBalance.temperature.Override(temperatureDay);

            vignette = profile.Add<Vignette>(true);
            vignette.intensity.Override(vignetteIntensityDay);
            vignette.smoothness.Override(vignetteSmoothness);
        }

        private void Update()
        {
            TryEnableCameraPostProcessing();

            // SurvivalClock 프로브: 못 찾았으면 프레임마다 다시 찾는다(래치 금지 -
            // AirlinerWreck.probeFrame과 같은 규칙. Update는 프레임당 1회라 그 자체가 가드다).
            if (clock == null)
            {
                clock = FindAnyObjectByType<SurvivalClock>();
                if (clock == null)
                    return;
            }

            if (bloom == null || whiteBalance == null || vignette == null)
                return;

            float t = clock.TimeOfDay01;

            // 골든아워 가중치: 일출(0.25)/일몰(0.75)에서 1, ±goldenHourHalfWidth 밖에서 0인 텐트 함수.
            float goldenWeight = Mathf.Max(GoldenWeight(t, 0.25f), GoldenWeight(t, 0.75f));

            // 밤 판정은 SurvivalClock.IsDaytime과 같은 경계(0.25~0.75)를 쓴다. 경계에서 목표값이
            // 계단식으로 바뀌어도, 그 지점은 항상 goldenWeight=1 구간 안이라 골든아워 값이 덮고,
            // 아래 시간 기반 Lerp가 남은 단차를 부드럽게 잇는다.
            bool isNight = t < 0.25f || t > 0.75f;

            float targetBloom = Mathf.Lerp(bloomIntensityDay, bloomIntensityGolden, goldenWeight);
            float targetTemperature = Mathf.Lerp(
                isNight ? temperatureNight : temperatureDay, temperatureGolden, goldenWeight);
            float targetVignette = isNight ? vignetteIntensityNight : vignetteIntensityDay;

            // Time.deltaTime 기반 보간이라 timeScale=0(타이틀/일시정지)에서는 k=0이 되어 값이
            // 그대로 멈춘다(Time.time 애니메이션 금지 규칙 준수).
            float k = Mathf.Clamp01(Time.deltaTime * blendSpeed);
            curBloomIntensity = Mathf.Lerp(curBloomIntensity, targetBloom, k);
            curTemperature = Mathf.Lerp(curTemperature, targetTemperature, k);
            curVignetteIntensity = Mathf.Lerp(curVignetteIntensity, targetVignette, k);

            bloom.intensity.Override(curBloomIntensity);
            whiteBalance.temperature.Override(curTemperature);
            vignette.intensity.Override(curVignetteIntensity);
        }

        /// <summary>기준 시각 center에서의 골든아워 가중치(0~1 텐트).</summary>
        private float GoldenWeight(float t, float center)
        {
            float halfWidth = Mathf.Max(0.0001f, goldenHourHalfWidth);
            return Mathf.Clamp01(1f - Mathf.Abs(t - center) / halfWidth);
        }

        /// <summary>
        /// 메인 카메라를 찾아 URP 포스트 프로세싱을 켠다. 카메라를 못 찾으면 이번 프레임은
        /// 그냥 넘어가고 다음 프레임에 다시 시도한다(래치 금지). 카메라가 파괴되고 새로 생기면
        /// (Unity의 파괴된 오브젝트 == null 규칙으로 감지) 플래그를 되돌려 새 카메라에 다시 켠다.
        /// </summary>
        private void TryEnableCameraPostProcessing()
        {
            if (targetCamera == null)
            {
                cameraPostEnabled = false;
                targetCamera = Camera.main;
                if (targetCamera == null)
                    return;
            }

            if (cameraPostEnabled)
                return;

            var camData = targetCamera.GetUniversalAdditionalCameraData();
            if (camData == null)
                return;

            camData.renderPostProcessing = true;
            cameraPostEnabled = true;
        }

        /// <summary>
        /// 런타임에 CreateInstance로 만든 프로파일과 그 안의 오버라이드 컴포넌트들을 직접 파괴한다.
        /// VolumeProfile을 Destroy해도 안의 VolumeComponent들은 자동으로 파괴되지 않으므로
        /// (별개의 ScriptableObject 인스턴스다) 하나씩 지워야 leak이 없다.
        /// </summary>
        private void OnDestroy()
        {
            if (volume != null)
                volume.sharedProfile = null;

            if (profile == null)
                return;

            var components = profile.components;
            for (int i = 0; i < components.Count; i++)
            {
                if (components[i] != null)
                    Destroy(components[i]);
            }

            Destroy(profile);
            profile = null;
            bloom = null;
            whiteBalance = null;
            vignette = null;
        }
    }
}
