using UnityEngine;
using UnityEngine.SceneManagement;

namespace MakeGame.Systems
{
    /// <summary>
    /// 바다 파도의 **단일 소스**. 파고 함수(높이/기울기)를 C#에서 정의하고, 똑같은 파라미터를
    /// Shader.SetGlobalVector로 MGOcean 셰이더에 밀어넣는다. 한 곳(아래 상수 표)만 고치면
    /// 물리(부력·뗏목 흔들림·수영 수면 판정)와 시각(수면 노멀·화이트캡)이 함께 움직인다.
    ///
    /// ── 파도 모델 ────────────────────────────────────────────────────────────────
    /// 방향이 서로 다른 Gerstner 성분 4개의 합이다. **수평 압축항(steepness Q)은 0으로 고정**했고,
    /// 그 결과 식은 방향성 정현파의 합이 된다:
    ///     h(p, t) = Σ A_i · sin( k_i · dot(D_i, p) + ω_i · t )
    ///     ∂h/∂p   = Σ D_i · (A_i · k_i) · cos( k_i · dot(D_i, p) + ω_i · t )
    /// Q를 0으로 둔 이유는 아래 "정점 변위 미채택" 항목과 같다 - 수평 압축은 **정점을 옮겨야만**
    /// 보이는 효과인데 바다 메시가 그것을 표현할 수 없고, Q > 0이면 C#(높이 역산 반복 필요)과
    /// 셰이더(평면 픽셀 셰이딩) 사이에 위상 불일치만 생긴다. Q = 0이면 두 쪽 식이 문자 그대로 같다.
    ///
    /// 각속도는 심해 분산 관계 ω = sqrt(g·k)에서 유도한다(g = 9.81). 파장 하나만 정하면 주기가
    /// 따라오므로 "긴 파도가 느리게 온다"가 저절로 성립한다(주기 8.7s / 6.6s / 4.9s / 3.7s).
    ///
    /// ── 정점 변위 미채택 근거 (중요) ─────────────────────────────────────────────
    /// 바다 메시는 WorldMapManager.GenerateOceanMesh가 만든 40,000m × 64칸 격자다 → **한 칸 625m**.
    /// 나이퀴스트 한계상 이 격자가 표현할 수 있는 최단 파장은 1,250m이고, 여기서 쓰는 파장은
    /// 21~118m다. 즉 정점 변위를 넣으면 파도가 그려지는 게 아니라 **에일리어싱된 잡음**으로
    /// 625m짜리 삼각형이 통째로 들썩인다. 그래서 셰이더는 정점을 옮기지 않고, 같은 파고 함수의
    /// **해석적 기울기로 픽셀 단위 노멀만** 흔든다(픽셀 단위라 격자 밀도와 무관하다).
    /// 높이 함수는 C# 쪽에서만 실제로 쓰인다(부력·뗏목 흔들림·수영 수면 판정) - 그래서 파도의
    /// "체감"은 전부 살아 있고, 시각은 노멀/스페큘러/화이트캡으로 표현된다.
    /// (WorldMapManager는 이번 작업의 수정 허용 목록 밖이라 격자를 촘촘하게 만들 수 없다.)
    ///
    /// ── 시간 계약 ────────────────────────────────────────────────────────────────
    /// 파도 시계는 Time.time이다. 셰이더 쪽 시간은 예전 그대로 WorldMapManager.Update가 머티리얼에
    /// 넣는 _MG_WaveTime(= Time.time)이고, 이 클래스도 같은 Time.time을 쓴다. Time.time은
    /// Time.timeScale = 0에서 멈추므로 **타이틀 화면에서 바다가 정지하는 계약이 그대로 유지**된다.
    /// (_MG_WaveTime은 머티리얼 프로퍼티라 전역으로 덮어쓸 수 없다 - 그 경로는 손대지 않았다.)
    ///
    /// ── 성능 ─────────────────────────────────────────────────────────────────────
    /// SampleHeight/SampleNormal은 sin/cos 4회짜리 순수 수학이고 힙 할당이 0이다.
    /// 프레임당 호출은 뗏목 4회 + 플레이어 1회 = 5회로 설계했다.
    ///
    /// ── 부트스트랩 ───────────────────────────────────────────────────────────────
    /// 씬에 인스턴스가 없다(프로젝트의 자기 부트스트랩 선례 - CursorLockController/BuildingSystem과
    /// 동일한 SubsystemRegistration + sceneLoaded + 중복 가드). 정적 캐시는 같은 훅에서 리셋한다.
    /// </summary>
    [DisallowMultipleComponent]
    public class OceanWaves : MonoBehaviour
    {
        // ── 파도 파라미터 (단일 소스) ────────────────────────────────────────────
        /// <summary>합성 성분 수. 셰이더 전역이 float4 하나로 성분 4개를 담으므로 4에 고정이다.</summary>
        public const int ComponentCount = 4;

        /// <summary>중력 가속도(m/s²). 심해 분산 관계 ω = sqrt(g·k)에 쓴다.</summary>
        private const float Gravity = 9.81f;

        /// <summary>성분별 파장(m). 긴 것부터. 값을 바꾸면 물리와 시각이 함께 바뀐다.</summary>
        private static readonly float[] Wavelengths = { 118f, 67f, 38f, 21f };

        /// <summary>성분별 진행 방향(+X축 기준 각도, 도). 서로 크게 벌려 격자무늬가 생기지 않게 한다.</summary>
        private static readonly float[] DirectionDegrees = { 24f, 120f, -79f, -40f };

        /// <summary>
        /// 잔잔한 바다(맑음)에서의 성분별 진폭(m). 합계 0.212m = 마루~골 0.42m.
        /// 거친 바다에서는 stormAmplitudeScale이 곱해진다(합계 0.59m).
        /// </summary>
        private static readonly float[] CalmAmplitudes = { 0.090f, 0.060f, 0.040f, 0.022f };

        // ── 날씨 연동 (거친 바다) ────────────────────────────────────────────────
        [Header("날씨 연동")]
        [Tooltip("거칠기 1(비/폭풍)에서 진폭에 곱하는 배율. 1 = 맑음과 동일.")]
        public float stormAmplitudeScale = 2.8f;

        [Tooltip("거칠기 1(비/폭풍)에서 각속도(파도가 지나가는 속도)에 곱하는 배율.")]
        public float stormSpeedScale = 1.45f;

        [Tooltip("거칠기 변화를 따라가는 시간 상수(초). WeatherSystem 쪽 보간에 더해 한 겹 더 완만하게 만든다.")]
        public float roughnessFollowSeconds = 6f;

        // ── 셰이더 전역 프로퍼티 ID ──────────────────────────────────────────────
        // MGOcean.shader는 이 여섯 개를 CBUFFER(UnityPerMaterial) **밖**에서 선언한다.
        // Properties 블록에 넣으면 머티리얼 프로퍼티가 되어 전역 설정이 무시되므로 절대 넣지 않는다.
        private static readonly int AmpProperty = Shader.PropertyToID("_MG_WaveAmp");
        private static readonly int WaveNumberProperty = Shader.PropertyToID("_MG_WaveK");
        private static readonly int OmegaProperty = Shader.PropertyToID("_MG_WaveOmega");
        private static readonly int DirXProperty = Shader.PropertyToID("_MG_WaveDirX");
        private static readonly int DirZProperty = Shader.PropertyToID("_MG_WaveDirZ");
        private static readonly int SeaStateProperty = Shader.PropertyToID("_MG_SeaState");

        // ── 정적 캐시 (샘플러가 읽는 값) ─────────────────────────────────────────
        // 성분 i의 값이 각 Vector4의 i번째 채널에 들어간다(x,y,z,w = 성분 0,1,2,3).
        // 셰이더 전역과 **완전히 같은 배열**이라, C#과 셰이더가 같은 수를 본다.
        private static Vector4 waveAmp;
        private static Vector4 waveK;
        private static Vector4 waveOmega;
        private static Vector4 waveDirX;
        private static Vector4 waveDirZ;

        /// <summary>평균 해수면 y(m). WorldMapManager.seaLevel을 읽어 온다(없으면 0).</summary>
        private static float seaLevelY;

        /// <summary>현재 바다 거칠기 0~1(0 = 잔잔, 1 = 폭풍). 날씨에서 보간되어 들어온다.</summary>
        private static float roughness01;

        /// <summary>씬에 살아 있는 드라이버. 없으면 null(그래도 샘플러는 기본값으로 동작한다).</summary>
        public static OceanWaves Active { get; private set; }

        private WorldMapManager worldMap;
        private WeatherSystem weather;
        private float rescanTimer;

        /// <summary>참조 재탐색 주기(초). 월드/날씨는 런타임 생성이라 첫 프레임에 없을 수 있다.</summary>
        private const float RescanInterval = 1f;

        // ── 공개 샘플러 ──────────────────────────────────────────────────────────

        /// <summary>평균 해수면 y(m). 파도의 0선이다.</summary>
        public static float SeaLevel => seaLevelY;

        /// <summary>현재 바다 거칠기 0~1. 0 = 맑음/잔잔, 1 = 비·폭풍/거칠다.</summary>
        public static float Roughness01 => roughness01;

        /// <summary>
        /// 파도 시계(초). 셰이더에 들어가는 _MG_WaveTime(WorldMapManager.Update가 넣는 Time.time)과
        /// **같은 값**이어야 물리와 시각의 위상이 맞는다. Time.timeScale = 0에서 멈춘다(타이틀 정지 계약).
        /// </summary>
        public static float WaveTime => Time.time;

        /// <summary>
        /// 그 지점의 수면 절대 높이(m). 부력·뗏목 흔들림·수영 수면 판정이 쓰는 값이다.
        /// 힙 할당 없음, sin 4회.
        /// </summary>
        public static float SampleHeight(Vector3 worldPos)
        {
            return seaLevelY + SampleWaveOffset(worldPos.x, worldPos.z, WaveTime);
        }

        /// <summary>지정한 시각의 수면 절대 높이(m). 검증/재현용 오버로드.</summary>
        public static float SampleHeight(Vector3 worldPos, float time)
        {
            return seaLevelY + SampleWaveOffset(worldPos.x, worldPos.z, time);
        }

        /// <summary>
        /// 평균 해수면으로부터의 파도 편차(m). 자기 기준 수면(예: PlayerController.waterLevel)을
        /// 이미 갖고 있는 쪽이 그 값을 유지한 채 파도만 얹을 때 쓴다.
        /// </summary>
        public static float SampleWaveOffset(Vector3 worldPos)
        {
            return SampleWaveOffset(worldPos.x, worldPos.z, WaveTime);
        }

        /// <summary>
        /// 파고 함수 본체. h = Σ A_i · sin(k_i · dot(D_i, p) + ω_i · t).
        /// MGOcean.shader의 MGWaveHeight()와 **문자 그대로 같은 식**이다(같은 전역 파라미터를 읽는다).
        /// </summary>
        public static float SampleWaveOffset(float x, float z, float time)
        {
            float h = 0f;
            h += waveAmp.x * Mathf.Sin(waveK.x * (waveDirX.x * x + waveDirZ.x * z) + waveOmega.x * time);
            h += waveAmp.y * Mathf.Sin(waveK.y * (waveDirX.y * x + waveDirZ.y * z) + waveOmega.y * time);
            h += waveAmp.z * Mathf.Sin(waveK.z * (waveDirX.z * x + waveDirZ.z * z) + waveOmega.z * time);
            h += waveAmp.w * Mathf.Sin(waveK.w * (waveDirX.w * x + waveDirZ.w * z) + waveOmega.w * time);
            return h;
        }

        /// <summary>
        /// 그 지점의 수면 법선(정규화). 뗏목 기울기·물 위 오브젝트 정렬에 쓴다.
        /// MGOcean.shader가 픽셀 노멀을 만드는 식(float3(-slope.x, 1, -slope.y))과 동일하다.
        /// </summary>
        public static Vector3 SampleNormal(Vector3 worldPos)
        {
            return SampleNormal(worldPos, WaveTime);
        }

        /// <summary>지정한 시각의 수면 법선. 검증/재현용 오버로드.</summary>
        public static Vector3 SampleNormal(Vector3 worldPos, float time)
        {
            SampleSlope(worldPos.x, worldPos.z, time, out float sx, out float sz);
            return new Vector3(-sx, 1f, -sz).normalized;
        }

        /// <summary>
        /// 파고 함수의 해석적 기울기 (∂h/∂x, ∂h/∂z). cos 4회.
        /// MGOcean.shader의 MGWaveSlope()와 같은 식이다.
        /// </summary>
        public static void SampleSlope(float x, float z, float time, out float slopeX, out float slopeZ)
        {
            float c;
            slopeX = 0f;
            slopeZ = 0f;

            c = waveAmp.x * waveK.x * Mathf.Cos(waveK.x * (waveDirX.x * x + waveDirZ.x * z) + waveOmega.x * time);
            slopeX += waveDirX.x * c; slopeZ += waveDirZ.x * c;

            c = waveAmp.y * waveK.y * Mathf.Cos(waveK.y * (waveDirX.y * x + waveDirZ.y * z) + waveOmega.y * time);
            slopeX += waveDirX.y * c; slopeZ += waveDirZ.y * c;

            c = waveAmp.z * waveK.z * Mathf.Cos(waveK.z * (waveDirX.z * x + waveDirZ.z * z) + waveOmega.z * time);
            slopeX += waveDirX.z * c; slopeZ += waveDirZ.z * c;

            c = waveAmp.w * waveK.w * Mathf.Cos(waveK.w * (waveDirX.w * x + waveDirZ.w * z) + waveOmega.w * time);
            slopeX += waveDirX.w * c; slopeZ += waveDirZ.w * c;
        }

        // ── 수명 주기 ────────────────────────────────────────────────────────────

        /// <summary>
        /// 씬이 로드될 때마다 드라이버를 하나 만든다(중복 가드 포함). 동시에 정적 캐시를 리셋하고
        /// 기본(잔잔) 파라미터를 즉시 셰이더에 밀어넣는다 - 첫 프레임에 바다가 평평해 보이지 않게 하고,
        /// 드라이버가 없는 테스트 씬에서도 SampleHeight가 유효한 값을 돌려주게 하기 위해서다.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Bootstrap()
        {
            // 도메인 리로드를 끈 플레이 모드에서 이전 실행 값이 남는 것을 막는다(R1 리셋 훅).
            Active = null;
            seaLevelY = 0f;
            roughness01 = 0f;
            RecomputeWaves(0f, 2.8f, 1.45f);
            PushToShader();

            SceneManager.sceneLoaded += (scene, mode) =>
            {
                if (FindAnyObjectByType<OceanWaves>() != null)
                    return;

                var go = new GameObject("OceanWaves");
                go.AddComponent<OceanWaves>();
            };
        }

        private void Awake()
        {
            Active = this;
            ResolveReferences();
            ApplyRoughness(ReadWeatherRoughness());
        }

        private void OnDestroy()
        {
            if (Active == this)
                Active = null;
        }

        /// <summary>
        /// 매 프레임 날씨 거칠기를 따라가며 파라미터를 다시 계산하고 셰이더 전역을 갱신한다.
        /// 갱신 비용은 sqrt 4회 + SetGlobalVector 6회로, 프레임당 무시할 수준이다.
        /// </summary>
        private void Update()
        {
            rescanTimer -= Time.unscaledDeltaTime;
            if (rescanTimer <= 0f)
            {
                rescanTimer = RescanInterval;
                ResolveReferences();
            }

            // 거칠기 보간은 unscaledDeltaTime을 쓴다(엔딩/사망으로 timeScale이 0이 되어도 진행 중이던
            // 보간이 굳지 않게 - 프로젝트 관례, WeatherSystem.Update와 동일한 이유).
            float target = ReadWeatherRoughness();
            float smoothed = roughnessFollowSeconds > 0f
                ? Mathf.MoveTowards(roughness01, target, Time.unscaledDeltaTime / roughnessFollowSeconds)
                : target;

            ApplyRoughness(smoothed);
        }

        /// <summary>WorldMapManager(해수면)와 WeatherSystem(거칠기) 참조를 확보한다. 둘 다 없어도 동작한다.</summary>
        private void ResolveReferences()
        {
            if (worldMap == null)
                worldMap = FindAnyObjectByType<WorldMapManager>();

            if (weather == null)
                weather = WeatherSystem.Active;

            seaLevelY = worldMap != null ? worldMap.seaLevel : 0f;
        }

        /// <summary>날씨가 알려주는 목표 거칠기(0~1). 날씨 시스템이 없으면 잔잔(0)으로 본다.</summary>
        private float ReadWeatherRoughness()
        {
            if (weather == null)
                weather = WeatherSystem.Active;

            return weather != null ? Mathf.Clamp01(weather.SeaRoughness01) : 0f;
        }

        /// <summary>거칠기를 확정하고 파라미터를 다시 계산해 셰이더에 밀어준다.</summary>
        private void ApplyRoughness(float value)
        {
            roughness01 = Mathf.Clamp01(value);
            RecomputeWaves(roughness01,
                Mathf.Max(1f, stormAmplitudeScale),
                Mathf.Max(1f, stormSpeedScale));
            PushToShader();
        }

        /// <summary>
        /// 거칠기에서 성분별 (진폭 · 파수 · 각속도 · 방향)을 유도한다.
        /// 파장/방향은 거칠기와 무관하게 고정이고, 진폭과 각속도만 잔잔↔거침 사이를 선형 보간한다.
        /// </summary>
        private static void RecomputeWaves(float rough, float ampScaleAtStorm, float speedScaleAtStorm)
        {
            float ampScale = Mathf.Lerp(1f, ampScaleAtStorm, rough);
            float speedScale = Mathf.Lerp(1f, speedScaleAtStorm, rough);

            for (int i = 0; i < ComponentCount; i++)
            {
                float k = 2f * Mathf.PI / Wavelengths[i];
                float omega = Mathf.Sqrt(Gravity * k) * speedScale;
                float rad = DirectionDegrees[i] * Mathf.Deg2Rad;

                SetComponent(ref waveAmp, i, CalmAmplitudes[i] * ampScale);
                SetComponent(ref waveK, i, k);
                SetComponent(ref waveOmega, i, omega);
                SetComponent(ref waveDirX, i, Mathf.Cos(rad));
                SetComponent(ref waveDirZ, i, Mathf.Sin(rad));
            }
        }

        /// <summary>Vector4의 i번째 채널에 값을 넣는다(성분 인덱스 → 채널 매핑을 한 곳에 모은다).</summary>
        private static void SetComponent(ref Vector4 target, int index, float value)
        {
            switch (index)
            {
                case 0: target.x = value; break;
                case 1: target.y = value; break;
                case 2: target.z = value; break;
                default: target.w = value; break;
            }
        }

        /// <summary>
        /// 셰이더 전역에 현재 파라미터를 밀어넣는다. MGOcean은 이 값들만 보고 수면 노멀/화이트캡을
        /// 만들므로, 여기서 밀어준 값이 곧 화면에 보이는 파도다(= C#이 계산하는 파도와 같다).
        /// </summary>
        private static void PushToShader()
        {
            Shader.SetGlobalVector(AmpProperty, waveAmp);
            Shader.SetGlobalVector(WaveNumberProperty, waveK);
            Shader.SetGlobalVector(OmegaProperty, waveOmega);
            Shader.SetGlobalVector(DirXProperty, waveDirX);
            Shader.SetGlobalVector(DirZProperty, waveDirZ);
            Shader.SetGlobalVector(SeaStateProperty, new Vector4(roughness01, seaLevelY, 0f, 0f));
        }
    }
}
