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

        // ── [B33] 폭풍우 연출 (강우 시야 안개 · 번개 화면 반응) ────────────────────
        // 안개(RenderSettings)의 단독 소유자는 DayNightCycle.Update다. 맑은 날 기준값을 계산하고
        // WeatherSystem의 rainFogColor/rainFogDensity까지 RainIntensity01로 보간해 매 프레임
        // 기록한다(DayNightCycle.cs:376-387). 여기서는 그 계약을 **한 줄도 건드리지 않고**,
        // UnderwaterAmbience가 쓰는 것과 같은 LateUpdate 순서 규약으로 그 위에 한 겹만 얹는다:
        //   DayNightCycle.Update(기준 안개 기록) → 본 LateUpdate(폭풍 헤이즈 가산) → 렌더
        // **수중이면 통째로 물러난다** — 그 프레임 안개의 주인은 UnderwaterAmbience이고,
        // 두 LateUpdate의 실행 순서는 보장되지 않으므로 겹치는 순간 결과가 갈리기 때문이다.
        //
        // 밀도 수치 근거(FogMode.ExponentialSquared, 잔여 시야 = exp(-(밀도·거리)²)):
        //   DayNightCycle이 만드는 최대 강우 기준값 0.006 → 100m 70% · 200m 24% · 300m 4%
        //   여기서 최대 +0.0015를 더한 0.0075   → 100m 57% · 150m 29% · 200m 11% · 300m 0.6%
        // 즉 근거리는 여전히 선명하고 **원경만** 뚜렷하게 뿌옇게 잠긴다. 더 올리면 답답해진다.
        [Header("폭풍우 (B33 — 강우 시야 안개 · 번개)")]
        [Tooltip("최대 강우에서 DayNightCycle의 기준 안개 밀도에 **더할** 값. 0이면 이 기능이 꺼진다.")]
        public float stormFogExtraDensity = 0.0015f;

        [Tooltip("폭풍 헤이즈가 붙기 시작하는 강우 세기. 이 아래로는 DayNightCycle 기준값 그대로다.")]
        [Range(0f, 1f)]
        public float stormFogRainThreshold = 0.5f;

        [Tooltip("폭풍 안개가 밀리는 회청색. 과하면 화면이 납빛이 되므로 혼합량을 낮게 유지한다.")]
        public Color stormFogTint = new Color(0.50f, 0.55f, 0.60f, 1f);

        [Tooltip("최대 강우에서 안개색을 stormFogTint 쪽으로 미는 비율(0~1). 0.3이면 30%만 섞는다.")]
        [Range(0f, 1f)]
        public float stormFogTintAmount = 0.3f;

        [Tooltip("번개 섬광이 만드는 최대 포스트 노출(EV). 하늘이 안 보이는 실내/숲에서도 화면 전체가" +
            " 번쩍이게 하는 것이 이 항목의 역할이다(환경광/스카이박스는 StormEffects가 담당한다).")]
        public float flashPostExposure = 0.7f;

        [Tooltip("번개 섬광이 안개색을 섬광색 쪽으로 미는 최대 비율. 원경의 빗줄기 벽이 함께 번쩍인다.")]
        [Range(0f, 1f)]
        public float flashFogBrighten = 0.5f;

        [Tooltip("최대 강우에서 비네트에 더할 값. 비 오는 날 시야가 조금 더 조여든다.")]
        [Range(0f, 0.3f)]
        public float rainVignetteBoost = 0.06f;

        [Tooltip("최대 강우에서 채도에서 뺄 값. 색이 빠져 눅눅해 보인다(기본 채도 +8 기준 -9면 -1).")]
        public float rainSaturationDrop = 9f;

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

        /// <summary>[B33] 강우 채도 저하 · 번개 포스트 노출에 쓴다(예전에는 지역 변수라 참조가 없었다).</summary>
        private ColorAdjustments colorAdjustments;

        private SurvivalClock clock;
        private Camera targetCamera;

        /// <summary>[B33] 수중 판정용. 못 찾았을 때만 저빈도로 재시도한다(정상 경로 탐색 비용 0).</summary>
        private WorldMapManager worldMap;

        // [B33] 안개 누적 폭주 방지용 스냅샷.
        // DayNightCycle이 매 프레임 안개를 되써 주는 정상 구성에서는 "현재값 + 증분"이 옳지만,
        // 그 컴포넌트가 없거나 enableAtmosphericFog를 끈 구성에서는 WeatherSystem이 단계 전환 때만
        // 안개를 쓰므로 증분이 프레임마다 쌓여 순식간에 눈앞이 하얘진다. 그래서 **직전에 우리가 쓴
        // 값**을 기억해 두고, 지금 값이 그것과 똑같으면(= 아무도 되쓰지 않았다) 우리가 기억한
        // 기준값을 다시 쓴다. 두 구성 모두에서 결과가 정확히 같아진다.
        private bool stormFogWritten;
        private float lastWrittenFogDensity;
        private float lastBaseFogDensity;
        private Color lastWrittenFogColor;
        private Color lastBaseFogColor;

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

            colorAdjustments = profile.Add<ColorAdjustments>(true);
            colorAdjustments.saturation.Override(saturation);
            colorAdjustments.contrast.Override(contrast);
            // [B33] 번개 섬광이 밀어 올릴 자리. 평소에는 0(중립)이다.
            colorAdjustments.postExposure.Override(0f);

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

        /// <summary>
        /// [B33] 폭풍우 연출을 화면에 얹는다. **LateUpdate인 이유가 전부**다 —
        /// 안개/환경광의 단독 소유자인 DayNightCycle.Update가 이번 프레임 값을 다 기록한 뒤에
        /// 실행되어야 렌더 직전의 마지막 승자가 될 수 있고, 그래야 DayNightCycle 코드를 한 줄도
        /// 건드리지 않고 그 위에 한 겹만 얹을 수 있다(UnderwaterAmbience 클래스 주석의 규약과 동일).
        ///
        /// 두 갈래로 나눈 이유:
        ///  · 포스트 프로세싱(노출/비네트/채도)은 이 컴포넌트가 만든 볼륨이라 **수중이든 아니든**
        ///    소유권 다툼이 없다. 다만 수중에서는 번개가 안 보이는 것이 맞으므로 섬광만 죽인다.
        ///  · RenderSettings 안개는 수중이면 통째로 물러난다. 그 프레임의 주인은 UnderwaterAmbience고,
        ///    LateUpdate끼리는 실행 순서가 보장되지 않아 둘이 겹치는 순간 결과가 갈리기 때문이다.
        /// </summary>
        private void LateUpdate()
        {
            WeatherSystem weather = WeatherSystem.Active;
            float rain = weather != null ? Mathf.Clamp01(weather.RainIntensity01) : 0f;

            bool underwater = IsCameraUnderwater();
            // 섬광 세기는 StormEffects가 **Update에서** 갱신하므로 여기서 읽어도 이번 프레임 값이다.
            float flash = underwater ? 0f : Mathf.Clamp01(StormEffects.FlashIntensity01);

            ApplyStormPostProcessing(rain, flash);

            if (!underwater)
                ApplyStormFog(rain, flash);
        }

        /// <summary>
        /// [B33] 강우/섬광을 포스트 프로세싱에 반영한다.
        ///  · 포스트 노출: 섬광 세기에 정비례. **보간하지 않는다** — Update의 blendSpeed(2.5) 보간을
        ///    태우면 0.1초짜리 섬광이 통째로 뭉개져 사라진다.
        ///  · 비네트/채도: 강우에 정비례. Update가 매 프레임 자기 값을 다시 쓰므로, 여기서 더한 값은
        ///    다음 프레임 Update에서 자동으로 원복된다(래치되는 상태가 없다).
        /// </summary>
        private void ApplyStormPostProcessing(float rain, float flash)
        {
            if (colorAdjustments != null)
            {
                colorAdjustments.postExposure.Override(flash * flashPostExposure);
                colorAdjustments.saturation.Override(saturation - rainSaturationDrop * rain);
            }

            if (vignette != null && rain > 0f)
                vignette.intensity.Override(curVignetteIntensity + rainVignetteBoost * rain);
        }

        /// <summary>
        /// [B33] 강우 시야 안개와 섬광의 안개 반응.
        ///
        /// 밀도는 DayNightCycle이 이번 프레임에 기록한 기준값 위에 stormFogExtraDensity를 더하고,
        /// 색은 회청색(stormFogTint)과 섬광색 쪽으로 민다. 헤이즈는 stormFogRainThreshold(0.5)부터
        /// SmoothStep으로 붙으므로 약한 비에서는 예전과 완전히 같다.
        ///
        /// **RenderSettings.fog가 꺼져 있으면 아무것도 하지 않는다.** 안개를 켜고 끄는 결정은
        /// DayNightCycle/WeatherSystem의 몫이고, 여기서 켜 버리면 그쪽 계약을 침범한다.
        /// 누적 폭주 방지는 위 stormFogWritten 스냅샷 주석 참고.
        /// </summary>
        private void ApplyStormFog(float rain, float flash)
        {
            float haze = Mathf.SmoothStep(0f, 1f,
                Mathf.InverseLerp(Mathf.Clamp01(stormFogRainThreshold), 1f, rain));
            float extraDensity = Mathf.Max(0f, stormFogExtraDensity) * haze;
            float tintAmount = Mathf.Clamp01(stormFogTintAmount) * rain;
            float flashAmount = Mathf.Clamp01(flashFogBrighten) * flash;

            if (!RenderSettings.fog || (extraDensity <= 0f && tintAmount <= 0f && flashAmount <= 0f))
            {
                stormFogWritten = false;
                return;
            }

            // 아무도 되쓰지 않았으면(우리가 쓴 값이 그대로 남아 있으면) 우리가 기억한 기준값을 쓴다.
            float baseDensity = stormFogWritten
                && Mathf.Approximately(RenderSettings.fogDensity, lastWrittenFogDensity)
                ? lastBaseFogDensity
                : RenderSettings.fogDensity;

            Color currentColor = RenderSettings.fogColor;
            Color baseColor = stormFogWritten && ApproximatelyColor(currentColor, lastWrittenFogColor)
                ? lastBaseFogColor
                : currentColor;

            float density = baseDensity + extraDensity;
            Color color = Color.Lerp(baseColor, stormFogTint, tintAmount);
            // 섬광은 안개까지 하얗게 띄운다 — 빗줄기로 채워진 원경이 통째로 번쩍이는 느낌이 여기서 난다.
            if (flashAmount > 0f && StormEffects.Active != null)
                color = Color.Lerp(color, StormEffects.Active.flashColor, flashAmount);

            RenderSettings.fogDensity = density;
            RenderSettings.fogColor = color;

            lastBaseFogDensity = baseDensity;
            lastWrittenFogDensity = density;
            lastBaseFogColor = baseColor;
            lastWrittenFogColor = color;
            stormFogWritten = true;
        }

        /// <summary>안개색 스냅샷 비교용(Color에는 Mathf.Approximately가 없다).</summary>
        private static bool ApproximatelyColor(Color a, Color b)
        {
            return Mathf.Approximately(a.r, b.r)
                && Mathf.Approximately(a.g, b.g)
                && Mathf.Approximately(a.b, b.b);
        }

        /// <summary>
        /// [B33] 카메라가 해수면 아래인지 직접 판정한다. UnderwaterAmbience.IsUnderwater를 읽지 않는
        /// 이유는 UnderwaterVisuals가 적어 둔 것과 같다 — 그 값도 LateUpdate에서 쓰이는데
        /// LateUpdate끼리는 실행 순서가 보장되지 않아 프레임에 따라 한 프레임 늦은 값을 보게 된다.
        /// </summary>
        private bool IsCameraUnderwater()
        {
            // targetCamera 필드에 **대입하지 않는다.** TryEnableCameraPostProcessing이 "필드가
            // null이 되는 순간"을 카메라 교체 신호로 삼아 cameraPostEnabled를 되돌리기 때문에,
            // 여기서 몰래 채워 넣으면 새 카메라에 포스트 프로세싱이 영영 안 켜진다.
            Camera cam = targetCamera != null ? targetCamera : Camera.main;

            if (worldMap == null && Time.frameCount % 60 == 0)
                worldMap = FindAnyObjectByType<WorldMapManager>();

            if (cam == null || worldMap == null)
                return false;

            return cam.transform.position.y < worldMap.seaLevel;
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
            colorAdjustments = null;
        }
    }
}
