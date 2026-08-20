using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace MakeGame.Systems
{
    /// <summary>
    /// 구름 한 벌로 **하늘의 구름과 지면의 구름 그림자를 동시에** 만든다.
    ///
    /// 조사(Docs/RealismPlan.md)에서 날씨 영역의 1순위로 꼽힌 것이 구름 그림자였다. 레이마칭 없이
    /// 얻는 리얼리즘 요소 중 비용 대비 효과가 가장 크다 - 정지해 있던 지형 전체가 "살아 있는 하늘
    /// 아래"에 놓인다. 그런데 그림자만 있고 하늘이 새파랗면 그것대로 이상하므로 둘을 같이 만든다.
    ///
    /// **커버리지 노이즈는 한 벌만 만든다.** 그 한 장에서 두 가지가 나온다.
    ///  · 하늘 구름 - 돔 셰이더가 R 채널을 구름 두께로 읽는다.
    ///  · 그림자   - 같은 값을 1-c로 뒤집어 구운 텍스처를 태양 라이트 쿠키로 붙인다.
    /// 둘이 같은 원본에서 나오고 같은 속도로 흐르므로 "구름은 저리 가는데 그림자는 이리 간다"가 없다.
    ///
    /// ★ 왜 라이트 쿠키인가. URP가 디렉셔널 라이트 쿠키를 네이티브로 지원한다 - 새 렌더 패스도,
    ///   렌더러 에셋 수정도, 지형 셰이더 수정도 필요 없다. 텍스처 한 장을 붙이고 오프셋만 옮기면
    ///   월드 전체(지형·잔디·건물·바다)에 구름 그림자가 한 번에 깔린다.
    ///   URP 구현이 쓰는 식은 `uv = (조명공간 XY − lightCookieOffset) / lightCookieSize`이고,
    ///   **오프셋 단위가 월드 미터**다(사이즈와 같은 단위). 그래서 바람이 만든 미터 단위 이동량을
    ///   조명의 right/up에 투영해 그대로 넣으면 된다.
    ///
    /// 그림자 타일(1400m)을 하늘 구름 타일(2600m)보다 작게 잡은 것도 일부러다. 물리적으로는 구름과
    /// 그 그림자의 크기가 같아야 맞지만, 그렇게 하면 그림자 덩어리가 325m쯤 되어 **플레이어가 보는
    /// 범위(100m 남짓)보다 커진다.** 그러면 "구름 그림자가 지나간다"가 아니라 "화면 전체가 서서히
    /// 어두워졌다 밝아진다"로 읽힌다 - 실제로 첫 판에서 그랬다. 1400m면 덩어리가 175m쯤이라
    /// 그림자 경계가 시야를 가로지르는 것이 보인다. 정확함보다 읽히는 쪽을 택한 것이고,
    /// 이건 조사에서 Subnautica 팀이 남긴 교훈과 같은 판단이다(물리적으로 정확한 값은 예쁘지 않다).
    ///
    /// 하늘 구름과 그림자의 **좌표계도 일부러 다르다.** 구름 돔은 월드 XZ 평면에, 쿠키는 조명 공간에
    /// 투영된다. 완전히 맞추려면 돔도 조명 공간으로 투영해야 하는데, 그러면 해가 낮은 아침·저녁마다
    /// 구름이 지평선 쪽으로 흉하게 늘어난다. 둘이 같은 속도로 흐르는 것으로 충분하다 -
    /// 구름과 그 그림자를 한 화면에서 나란히 놓고 대조할 수 있는 시점은 실제로 없다.
    /// </summary>
    public class SkySystem : MonoBehaviour
    {
        // ── 커버리지 텍스처 ─────────────────────────────────────
        /// <summary>텍스처 한 변(px). 타일링 노이즈라 이 이상 키워도 원경에서 차이가 없다.</summary>
        private const int CoverageResolution = 256;

        /// <summary>
        /// 격자 노이즈의 기본 주기(칸). 이 값이 텍스처 한 변을 나누어떨어져야 이음매 없이 타일링된다
        /// (256 / 8 = 32). 옥타브마다 2배씩 잘게 쪼개도 나누어떨어짐이 유지된다.
        /// </summary>
        private const int BasePeriod = 8;

        private const int Octaves = 4;

        [Header("구름")]
        [Tooltip("구름층 높이(m). 시차의 세기를 정한다 - 낮을수록 머리 위 구름이 빨리 지나간다.")]
        public float cloudHeight = 900f;

        [Tooltip("커버리지 텍스처 한 장이 덮는 월드 크기(m). 클수록 구름 덩어리가 커진다.")]
        public float cloudTileMeters = 2600f;

        [Tooltip("바람 위상 1당 구름이 흐르는 거리(m). 구름은 지상 바람보다 빠르다.")]
        public float driftMetersPerPhase = 14f;

        [Tooltip("맑을 때의 구름 양(0~1)")]
        public float clearCoverage = 0.34f;

        [Tooltip("폭풍일 때의 구름 양(0~1)")]
        public float stormCoverage = 0.82f;

        [Tooltip("구름 양이 목표를 따라가는 시간(초). 하늘은 천천히 흐려진다.")]
        public float coverageFollowSeconds = 40f;

        [Header("구름 그림자")]
        [Tooltip("그림자가 가장 짙은 곳에서 빛이 얼마나 남는가(0.55 = 45% 어두워진다)")]
        public float shadowFloor = 0.55f;

        [Tooltip("쿠키 한 장이 덮는 월드 크기(m). 작을수록 그림자 덩어리가 작아 지나가는 게 눈에 띈다.")]
        public float cookieTileMeters = 1400f;

        /// <summary>지금 살아 있는 인스턴스.</summary>
        public static SkySystem Active { get; private set; }

        /// <summary>지금 구름 양(0~1). 다른 시스템이 "흐린 정도"를 알고 싶을 때.</summary>
        public static float Coverage01 => coverage01;

        private static readonly int ScrollProperty = Shader.PropertyToID("_MG_CloudScroll");
        private static readonly int CoverageProperty = Shader.PropertyToID("_Coverage");
        private static readonly int EdgeProperty = Shader.PropertyToID("_CloudEdge");
        private static readonly int CloudTexProperty = Shader.PropertyToID("_CoverageTex");
        private static readonly int CloudHeightProperty = Shader.PropertyToID("_CloudHeight");
        private static readonly int TileProperty = Shader.PropertyToID("_TileMeters");

        private static float coverage01 = 0.34f;

        // 노이즈 원본. 텍스처를 다시 굽더라도 노이즈는 다시 계산하지 않는다(굽기는 문턱값 매핑뿐).
        private float[] coverageField;

        /// <summary>
        /// "구름 양 c일 때 쓸 문턱값"을 담은 표(인덱스 0~128 = 구름 양 0~1).
        ///
        /// ★ 이 표가 없으면 구름 양이 거짓말을 한다. FBM 노이즈를 0~1로 편다고 값이 고르게 퍼지는
        ///   게 아니다 - 종 모양으로 가운데에 몰린다. 그래서 "0.34를 넘는 곳이 구름"이라고 문턱을
        ///   그냥 1−c로 잡으면 실제로 구름이 되는 면적은 34%가 아니라 5% 남짓이고, 구름 양을 아무리
        ///   올려도 하늘이 한참 맑다(첫 판에서 실제로 그랬다 - 구름이 실낱처럼 희미했다).
        ///
        ///   정렬한 값에서 백분위를 직접 뽑으면 "면적 c만큼이 구름"이 **정확히** 성립한다.
        ///   하늘 구름과 라이트 쿠키가 같은 표를 쓰므로 둘의 구름 양도 저절로 일치한다.
        /// </summary>
        private float[] thresholdTable;

        private Texture2D coverageTexture;   // R = 구름 두께(돔이 읽는다)
        private Texture2D cookieTexture;     // RGB = 남는 빛(라이트 쿠키)
        private Color32[] cookiePixels;

        private Light sunLight;
        private UniversalAdditionalLightData sunLightData;
        private Texture originalCookie;
        private Vector2 originalCookieSize;

        private Transform domeTransform;
        private Mesh domeMesh;

        /// <summary>
        /// 돔 추종용 카메라 캐시. Camera.main은 태그 검색이라 매 프레임 부르면 안 된다
        /// (이 프로젝트의 다른 시스템 전부가 지키는 규약인데 여기만 빠져 있었다 - 성능 감사에서 잡혔다).
        /// </summary>
        private Camera domeCamera;
        private Material domeMaterial;
        private MeshRenderer domeRenderer;

        private float bakedCoverage = -1f;
        private float resolveTimer;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Bootstrap()
        {
            coverage01 = 0.34f;

            SceneManager.sceneLoaded += (scene, mode) =>
            {
                if (FindAnyObjectByType<SkySystem>() != null)
                    return;

                var go = new GameObject("SkySystem");
                go.AddComponent<SkySystem>();
            };
        }

        private void Awake()
        {
            Active = this;
            BuildCoverageField();
            BuildTextures();
            BuildDome();
            ResolveSun();
        }

        private void OnDestroy()
        {
            if (Active == this)
                Active = null;

            // 태양에 붙여 둔 쿠키를 원래대로 돌려놓는다. 씬을 다시 로드하면 이 인스턴스가 파괴되지만
            // Light은 씬에 남아 있을 수 있고, 그러면 파괴된 텍스처를 가리킨 채로 남는다.
            RestoreSunCookie();

            if (coverageTexture != null) Destroy(coverageTexture);
            if (cookieTexture != null) Destroy(cookieTexture);
            if (domeMaterial != null) Destroy(domeMaterial);
            if (domeMesh != null) Destroy(domeMesh);
        }

        private void LateUpdate()
        {
            resolveTimer -= Time.unscaledDeltaTime;
            if (resolveTimer <= 0f)
            {
                resolveTimer = 2f;
                ResolveSun();
            }

            UpdateCoverage();
            UpdateScroll();
            UpdateDome();
        }

        // ────────────────────────────────────────────────────────
        // 노이즈
        // ────────────────────────────────────────────────────────

        /// <summary>
        /// 타일링되는 FBM 값 노이즈를 한 번만 계산해 둔다.
        ///
        /// 타일링의 조건은 하나다: 격자 해시의 인덱스를 **주기로 나눈 나머지**로 잡는 것.
        /// 그래야 x = 0과 x = 주기가 같은 해시를 받아 이음매가 사라진다. 옥타브마다 주기를 2배로
        /// 잘게 쪼개도 256을 계속 나누어떨어지므로 조건이 유지된다.
        /// </summary>
        private void BuildCoverageField()
        {
            coverageField = new float[CoverageResolution * CoverageResolution];

            float min = float.MaxValue;
            float max = float.MinValue;

            for (int y = 0; y < CoverageResolution; y++)
            {
                for (int x = 0; x < CoverageResolution; x++)
                {
                    float u = x / (float)CoverageResolution;
                    float v = y / (float)CoverageResolution;

                    float sum = 0f;
                    float amplitude = 1f;
                    float total = 0f;
                    int period = BasePeriod;

                    for (int o = 0; o < Octaves; o++)
                    {
                        sum += amplitude * TileableValueNoise(u * period, v * period, period);
                        total += amplitude;
                        amplitude *= 0.5f;
                        period *= 2;
                    }

                    float value = sum / Mathf.Max(total, 0.0001f);
                    coverageField[y * CoverageResolution + x] = value;

                    if (value < min) min = value;
                    if (value > max) max = value;
                }
            }

            // 0~1로 펴 준다(문턱 표와는 별개 - 이건 셰이더에서 다루기 쉬운 범위로 맞추는 것뿐이다).
            float range = Mathf.Max(max - min, 0.0001f);
            for (int i = 0; i < coverageField.Length; i++)
                coverageField[i] = (coverageField[i] - min) / range;

            BuildThresholdTable();
        }

        /// <summary>
        /// 값 분포에서 백분위 표를 만든다. thresholdTable[i]는 "이 값을 넘는 픽셀이 전체의 i/128"인 값이다.
        /// 정렬은 시작할 때 한 번뿐이고(65536개, 수 ms), 그 뒤로는 표를 읽기만 한다.
        /// </summary>
        private void BuildThresholdTable()
        {
            var sorted = (float[])coverageField.Clone();
            System.Array.Sort(sorted);

            thresholdTable = new float[129];
            int last = sorted.Length - 1;

            for (int i = 0; i <= 128; i++)
            {
                // 구름 양 c = i/128 → 위에서부터 c만큼 잘라내는 지점.
                float fromTop = i / 128f;
                int index = Mathf.Clamp(Mathf.RoundToInt((1f - fromTop) * last), 0, last);
                thresholdTable[i] = sorted[index];
            }
        }

        /// <summary>구름 양(0~1)에 해당하는 문턱값. 표 사이는 선형 보간한다.</summary>
        private float CloudThreshold(float coverage)
        {
            if (thresholdTable == null)
                return 1f - Mathf.Clamp01(coverage);

            float t = Mathf.Clamp01(coverage) * 128f;
            int i0 = Mathf.FloorToInt(t);
            int i1 = Mathf.Min(i0 + 1, 128);
            return Mathf.Lerp(thresholdTable[i0], thresholdTable[i1], t - i0);
        }

        /// <summary>주기 period로 타일링되는 값 노이즈 한 옥타브.</summary>
        private static float TileableValueNoise(float x, float y, int period)
        {
            int x0 = Mathf.FloorToInt(x);
            int y0 = Mathf.FloorToInt(y);
            float fx = x - x0;
            float fy = y - y0;

            // smoothstep 보간 - 선형이면 격자 방향의 줄무늬가 눈에 보인다.
            fx = fx * fx * (3f - 2f * fx);
            fy = fy * fy * (3f - 2f * fy);

            float a = LatticeHash(x0, y0, period);
            float b = LatticeHash(x0 + 1, y0, period);
            float c = LatticeHash(x0, y0 + 1, period);
            float d = LatticeHash(x0 + 1, y0 + 1, period);

            return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fy);
        }

        private static float LatticeHash(int x, int y, int period)
        {
            // 나머지 연산이 타일링의 전부다. period로 감아 주면 양 끝이 같은 값을 받는다.
            int ix = ((x % period) + period) % period;
            int iy = ((y % period) + period) % period;

            int h = ix * 374761393 + iy * 668265263 + period * 1013904223;
            h = (h ^ (h >> 13)) * 1274126177;
            h ^= h >> 16;
            return (h & 0x7FFFFFF) / (float)0x7FFFFFF;
        }

        // ────────────────────────────────────────────────────────
        // 텍스처
        // ────────────────────────────────────────────────────────

        private void BuildTextures()
        {
            coverageTexture = new Texture2D(CoverageResolution, CoverageResolution, TextureFormat.RGBA32, true, true)
            {
                name = "MG_CloudCoverage",
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.HideAndDontSave
            };

            var pixels = new Color32[coverageField.Length];
            for (int i = 0; i < coverageField.Length; i++)
            {
                byte v = (byte)Mathf.RoundToInt(Mathf.Clamp01(coverageField[i]) * 255f);
                pixels[i] = new Color32(v, v, v, 255);
            }
            coverageTexture.SetPixels32(pixels);
            coverageTexture.Apply(true, false);

            cookieTexture = new Texture2D(CoverageResolution, CoverageResolution, TextureFormat.RGBA32, true, true)
            {
                name = "MG_CloudCookie",
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.HideAndDontSave
            };
            cookiePixels = new Color32[coverageField.Length];

            BakeCookie(coverage01);
        }

        /// <summary>
        /// 지금 구름 양에 맞춰 쿠키를 다시 굽는다. 노이즈는 이미 배열에 있으므로 하는 일은
        /// 문턱값 매핑뿐이다(256² 곱셈). 그래도 매 프레임 할 일은 아니라 구름 양이 눈에 띄게
        /// 달라졌을 때만 부른다 - UpdateCoverage의 0.06 문턱이 그 판정이다.
        /// </summary>
        private void BakeCookie(float coverage)
        {
            float floor = Mathf.Clamp01(shadowFloor);

            // 문턱은 백분위 표에서 온다 - "면적 coverage만큼이 그림자"가 정확히 성립한다.
            // 램프 폭 0.055는 그림자 가장자리가 칼로 자른 듯 서지 않을 만큼만 부드럽게 하는 값이다.
            float edge = CloudThreshold(coverage);

            for (int i = 0; i < coverageField.Length; i++)
            {
                float thick = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(edge, edge + 0.055f, coverageField[i]));
                float lit = Mathf.Lerp(1f, floor, thick);
                byte v = (byte)Mathf.RoundToInt(Mathf.Clamp01(lit) * 255f);
                cookiePixels[i] = new Color32(v, v, v, 255);
            }

            cookieTexture.SetPixels32(cookiePixels);
            cookieTexture.Apply(true, false);
            bakedCoverage = coverage;
        }

        // ────────────────────────────────────────────────────────
        // 태양 쿠키
        // ────────────────────────────────────────────────────────

        private void ResolveSun()
        {
            Light found = DayNightCycle.FindDirectionalLight();
            if (found == sunLight)
                return;

            RestoreSunCookie();

            sunLight = found;
            if (sunLight == null)
            {
                sunLightData = null;
                return;
            }

            sunLightData = sunLight.GetComponent<UniversalAdditionalLightData>();
            if (sunLightData == null)
                sunLightData = sunLight.gameObject.AddComponent<UniversalAdditionalLightData>();

            originalCookie = sunLight.cookie;
            originalCookieSize = sunLightData.lightCookieSize;

            sunLight.cookie = cookieTexture;
            sunLightData.lightCookieSize = new Vector2(cookieTileMeters, cookieTileMeters);
        }

        private void RestoreSunCookie()
        {
            if (sunLight == null)
                return;

            sunLight.cookie = originalCookie;
            if (sunLightData != null)
                sunLightData.lightCookieSize = originalCookieSize;
        }

        // ────────────────────────────────────────────────────────
        // 매 프레임
        // ────────────────────────────────────────────────────────

        private void UpdateCoverage()
        {
            var weather = WeatherSystem.Active;
            float storm01 = weather != null
                ? Mathf.Clamp01(Mathf.Max(weather.SeaRoughness01, weather.RainIntensity01))
                : 0f;

            float target = Mathf.Lerp(clearCoverage, stormCoverage, storm01);

            // 하늘은 천천히 흐려진다. 구름 양이 몇 초 만에 두 배가 되면 "구름이 몰려온다"가 아니라
            // "구름이 켜졌다"로 보인다.
            coverage01 = coverageFollowSeconds > 0f
                ? Mathf.MoveTowards(coverage01, target, Time.unscaledDeltaTime / coverageFollowSeconds)
                : target;

            if (Mathf.Abs(coverage01 - bakedCoverage) > 0.06f)
                BakeCookie(coverage01);

            if (domeMaterial != null)
            {
                domeMaterial.SetFloat(CoverageProperty, coverage01);
                domeMaterial.SetFloat(EdgeProperty, CloudThreshold(coverage01));
            }
        }

        /// <summary>
        /// 구름이 흐른 거리(m)를 하늘과 그림자에 똑같이 먹인다.
        ///
        /// 쿠키 오프셋은 **조명 공간 XY**라, 월드 이동량을 조명의 right/up에 투영해 넣는다.
        /// URP의 식이 `uv = (조명공간 XY − offset) / size`이므로, offset을 바람 방향으로 키우면
        /// 무늬가 바람 방향으로 간다(하늘 구름이 가는 쪽과 같다).
        /// </summary>
        private void UpdateScroll()
        {
            Vector2 drift = WindSystem.Direction * (WindSystem.Phase * driftMetersPerPhase);
            Shader.SetGlobalVector(ScrollProperty, new Vector4(drift.x, drift.y, 0f, 0f));

            if (sunLight == null || sunLightData == null)
                return;

            var driftWorld = new Vector3(drift.x, 0f, drift.y);
            Transform lightTransform = sunLight.transform;

            sunLightData.lightCookieOffset = new Vector2(
                Vector3.Dot(driftWorld, lightTransform.right),
                Vector3.Dot(driftWorld, lightTransform.up));

            // 해가 지평선 아래로 내려가면 구름 그림자는 의미가 없다. 쿠키를 떼서 밤 그림자가
            // 얼룩덜룩해지는 것을 막는다(달빛에 구름 그림자가 지는 그림은 사실도 아니고 지저분하다).
            bool wantCookie = -lightTransform.forward.y > 0.08f;
            Texture desired = wantCookie ? cookieTexture : originalCookie;
            if (sunLight.cookie != desired)
                sunLight.cookie = desired;
        }

        // ────────────────────────────────────────────────────────
        // 구름 돔
        // ────────────────────────────────────────────────────────

        private void BuildDome()
        {
            Shader shader = Resources.Load<Shader>("Shaders/MGCloudDome");
            if (shader == null)
                return;

            domeMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            domeMaterial.SetTexture(CloudTexProperty, coverageTexture);
            domeMaterial.SetFloat(CloudHeightProperty, cloudHeight);
            domeMaterial.SetFloat(TileProperty, Mathf.Max(1f, cloudTileMeters));
            domeMaterial.SetFloat(CoverageProperty, coverage01);
            domeMaterial.SetFloat(EdgeProperty, CloudThreshold(coverage01));

            var go = new GameObject("CloudDome");
            go.transform.SetParent(transform, false);
            domeTransform = go.transform;

            domeMesh = BuildHemisphere(24, 10);
            var filter = go.AddComponent<MeshFilter>();
            filter.sharedMesh = domeMesh;

            domeRenderer = go.AddComponent<MeshRenderer>();
            domeRenderer.sharedMaterial = domeMaterial;
            domeRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            domeRenderer.receiveShadows = false;
            domeRenderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            domeRenderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
        }

        /// <summary>
        /// 반구 메시. UV는 쓰지 않는다 - 셰이더가 시선 방향으로 좌표를 직접 만들기 때문이다
        /// (그래야 구름이 지평선에서 수렴하는 원근이 생긴다). 메시가 하는 일은 하늘을 덮는 것뿐이라
        /// 분할이 거칠어도 된다.
        ///
        /// 반지름은 카메라 far clip(1000m)보다 안쪽인 900m다. 밖으로 나가면 통째로 잘려 사라진다.
        /// </summary>
        private static Mesh BuildHemisphere(int segments, int rings)
        {
            const float radius = 900f;

            var vertices = new Vector3[(segments + 1) * (rings + 1)];
            var triangles = new int[segments * rings * 6];

            for (int r = 0; r <= rings; r++)
            {
                // 완전한 반구가 아니라 지평선 아래 5도까지 살짝 내린다 - 지평선 딱 0도에서 끝나면
                // 카메라가 조금만 기울어도 돔 가장자리가 화면에 들어온다.
                float phi = Mathf.Lerp(-5f, 90f, r / (float)rings) * Mathf.Deg2Rad;
                float y = Mathf.Sin(phi);
                float ringRadius = Mathf.Cos(phi);

                for (int s = 0; s <= segments; s++)
                {
                    float theta = (s / (float)segments) * Mathf.PI * 2f;
                    vertices[r * (segments + 1) + s] = new Vector3(
                        Mathf.Cos(theta) * ringRadius, y, Mathf.Sin(theta) * ringRadius) * radius;
                }
            }

            int t = 0;
            for (int r = 0; r < rings; r++)
            {
                for (int s = 0; s < segments; s++)
                {
                    int i0 = r * (segments + 1) + s;
                    int i1 = i0 + 1;
                    int i2 = i0 + segments + 1;
                    int i3 = i2 + 1;

                    triangles[t++] = i0; triangles[t++] = i2; triangles[t++] = i1;
                    triangles[t++] = i1; triangles[t++] = i2; triangles[t++] = i3;
                }
            }

            var mesh = new Mesh { name = "MG_CloudDome", hideFlags = HideFlags.HideAndDontSave };
            mesh.vertices = vertices;
            mesh.triangles = triangles;
            // 컬링은 셰이더가 Cull Front로 안쪽 면을 그리므로 노멀은 필요 없지만, 바운즈는
            // 직접 넣어 준다 - 돔이 카메라를 따라다니므로 자동 계산된 바운즈로는 컬링이 어긋난다.
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * (radius * 2.2f));
            return mesh;
        }

        private void UpdateDome()
        {
            if (domeTransform == null)
                return;

            if (domeCamera == null)
                domeCamera = Camera.main;

            Camera cam = domeCamera;
            if (cam == null)
            {
                if (domeRenderer != null && domeRenderer.enabled)
                    domeRenderer.enabled = false;
                return;
            }

            // 돔은 카메라를 따라다니되 **회전은 따라가지 않는다**. 회전까지 따라가면 구름이
            // 시선과 함께 돌아 하늘이 통째로 미끄러진다.
            domeTransform.position = cam.transform.position;

            // 수면 아래에서는 끈다. 물 밖 구름이 수중 안개를 뚫고 비쳐 보이면 깊이감이 무너진다.
            float seaLevel = OceanWaves.Active != null ? OceanWaves.SeaLevel : 0f;
            bool visible = cam.transform.position.y > seaLevel - 0.2f;

            if (domeRenderer != null && domeRenderer.enabled != visible)
                domeRenderer.enabled = visible;
        }
    }
}
