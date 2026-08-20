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
    /// ── 두 개의 "수면" (v5에서 갈라졌다) ──────────────────────────────────────────
    ///  · SampleHeight / SampleWaveOffset(x, z, t) = **원 파형**. 셰이더가 그리는 것과 같다.
    ///    부유체(뗏목)가 타고 오르내리는 면이다.
    ///  · SampleWaveOffset(Vector3)          = 원 파형 × 얕은 물 쇄파 감쇠. "이 점이 물에 잠겼는가"
    ///    를 판정하는 면이다(PlayerController의 수영/보행 전환 전용). 지면이 해수면 위면 감쇠가 0이라
    ///    마른 모래 위에서 수영 모드로 뒤집히지 않는다. 자세한 근거는 그 함수 주석에 있다.
    ///
    /// ── 성능 ─────────────────────────────────────────────────────────────────────
    /// SampleHeight/SampleNormal은 sin/cos 4회짜리 순수 수학이고 힙 할당이 0이다.
    /// 프레임당 호출은 뗏목 4회 + 플레이어 1회 = 5회로 설계했다.
    /// SampleWaveOffset(Vector3)만 수심 프로브(지형 레이 1회)를 타는데, 지형이 정적이라 결과를
    /// 슬롯 8개에 0.4초/0.75m 허용으로 캐시한다 - 실제 레이는 초당 한 자릿수고 배열은 전부 정적
    /// 사전 할당 + RaycastNonAlloc이라 프레임당 힙 할당은 여전히 0이다.
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
        /// 위 네 방향의 산술평균(= (24+120-79-40)/4). 전역 바람 방위를 파도에 전할 때의 **기준점**이다.
        /// 바람이 이 방위일 때 파도는 예전과 정확히 같고, 바람이 돌면 네 성분이 서로의 각도 관계를
        /// 유지한 채 통째로 돈다(성분끼리의 각도 차이는 파도 모양을 정하는 값이라 건드리면 안 된다).
        /// </summary>
        private const float BaseBearingDegrees = 6.25f;

        /// <summary>
        /// 잔잔한 바다(맑음)에서의 성분별 진폭(m). 합계 0.500m = 마루~골 1.00m.
        /// 거친 바다에서는 stormAmplitudeScale이 곱해진다(합계 1.45m = 마루~골 2.90m).
        ///
        /// ── [파도 v5] 진폭 상향 배분 근거 ("파도가 좀 높이 쳐야 할 거 같아") ──────────
        /// 종전 0.212m(잔잔)/0.594m(폭풍)은 8m짜리 뗏목이 2° 남짓 기우는 정도라 "잔잔하다"로
        /// 읽혔다. 합계를 2.36배(0.212 → 0.500) 올리되 **성분마다 배율을 다르게** 줬다:
        ///     118m ×2.56 · 67m ×2.25 · 38m ×2.25 · 21m ×2.05
        /// 가장 긴 성분을 가장 많이 키운 이유는 두 가지다.
        ///  (1) "높이 친다"의 체감은 주기 8.7초짜리 **너울**이 만든다 - 짧은 성분은 아무리 키워도
        ///      배가 그 위를 평균해 버려(뗏목 8×5.2m > 파장 21m의 절반) 흔들림으로 나오지 않는다.
        ///  (2) 급경사(첨점) 위험은 파장이 짧은 쪽에서 먼저 온다. A/λ를 보면
        ///      0.00195 / 0.00201 / 0.00237 / 0.00214로 네 성분이 거의 같은 급경사도에 머문다
        ///      (종전 0.00076~0.00105의 약 2.3배). 짧은 성분만 덜 올린 것이 이 균형을 만든다.
        /// 실측(랜덤 40만 점): 최대 |dh/dx| 잔잔 0.042 · 폭풍 0.123(이론 상한 0.053/0.154),
        /// rms 0.019/0.055. MGOcean의 잔물결 2겹이 만드는 기울기(최대 0.36 / rms 약 0.19)가
        /// 여전히 지배적이라, 큰 파도 노멀이 과격해져 수면이 금속처럼 번들거리는 구간은 없다.
        /// </summary>
        private static readonly float[] CalmAmplitudes = { 0.230f, 0.135f, 0.090f, 0.045f };

        // ── 날씨 연동 (거친 바다) ────────────────────────────────────────────────
        [Header("날씨 연동")]
        [Tooltip("거칠기 1(비/폭풍)에서 진폭에 곱하는 배율. 1 = 맑음과 동일.")]
        public float stormAmplitudeScale = 2.9f;

        [Tooltip("거칠기 1(비/폭풍)에서 각속도(파도가 지나가는 속도)에 곱하는 배율.")]
        public float stormSpeedScale = 1.45f;

        [Tooltip("거칠기 변화를 따라가는 시간 상수(초). WeatherSystem 쪽 보간에 더해 한 겹 더 완만하게 만든다.")]
        public float roughnessFollowSeconds = 6f;

        /// <summary>
        /// 파도 방향이 바람 방위를 따라가는 속도(초당 도).
        ///
        /// **바람보다 느리게 따라가야 한다.** 이유가 둘이다.
        ///  · 실제 바다가 그렇다. 바람이 방향을 바꿔도 이미 만들어진 너울은 한참 그대로 간다.
        ///  · 뗏목 부력이 이 파도를 딛고 서 있다. 파도 방향이 홱 돌면 뗏목이 이유 없이 출렁인다.
        /// 기본값 1.2도/초는 90도 전환에 75초가 걸리는 속도다.
        /// </summary>
        public float bearingFollowDegreesPerSecond = 1.2f;

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

        /// <summary>지금 파도가 향하는 방위(도). 바람 방위를 느리게 따라간다.</summary>
        private static float waveBearing = BaseBearingDegrees;

        /// <summary>
        /// 파도 마루가 온전히 설 수 있는 최소 수심(m) = 지금 파라미터의 진폭 합(= 최대 마루 높이).
        /// RecomputeWaves가 갱신하므로 진폭을 올리면 자동으로 따라온다(잔잔 0.50m · 폭풍 1.45m).
        ///
        /// 기준을 "진폭 합"으로 잡은 근거: 마루가 그 자리 정수면 수심보다 높으면 반대편 골에서 바닥이
        /// 드러난다는 뜻이라 그 파도는 이미 부서진 상태다. 유의파고 + 쇄파계수(H_b = 0.78·d) 같은 더
        /// 강한 기준도 검토했지만(그러면 폭풍 기준 수심이 3m가 된다), 그건 정박한 뗏목이 있는 수심
        /// 2m 지점의 판정 수면까지 눌러서 **갑판 침수 여유를 0.44 → 0.25m로 깎았다**(시뮬레이션 실측).
        /// 감쇠의 목적은 "마른 모래 위에서 수영 모드가 되는 것"을 막는 것 하나뿐이고 그 목적은
        /// 수심 ≤ 0에서 감쇠 = 0인 것만으로 100% 달성되므로, 물속에서는 되도록 빨리 1로 복귀시키는
        /// 지금 기준이 부작용이 가장 적다.
        /// </summary>
        private static float crestLimitDepth = 1f;

        /// <summary>지형/해저를 못 찾았을 때 쓰는 "충분히 깊다" 수심(m). 감쇠가 정확히 1이 된다.</summary>
        private const float DeepWaterDepth = 100f;

        // ── 수심 프로브 캐시 (잠김 판정 감쇠용) ─────────────────────────────────
        // 지형 높이는 정적이라 매 호출 레이를 쏠 이유가 없다. 슬롯 8개면 뗏목 4점 + 플레이어 1점을
        // 전부 덮는다. 배열은 전부 정적 사전 할당이고 RaycastNonAlloc을 쓰므로 프레임당 힙 할당이 0이다.
        private const int DepthCacheSlots = 8;
        private const float DepthCacheSeconds = 0.4f;
        private const float DepthCacheRadius = 0.75f;

        /// <summary>레이 시작점으로 쓰는 "절대 나올 수 없는" y. TerrainSampler가 히트 실패 시 입력을 그대로 돌려주는 성질을 이용한다.</summary>
        private const float DepthProbeSentinelY = -400f;

        private static readonly float[] depthCacheX = new float[DepthCacheSlots];
        private static readonly float[] depthCacheZ = new float[DepthCacheSlots];
        private static readonly float[] depthCacheDepth = new float[DepthCacheSlots];
        private static readonly float[] depthCacheStamp = new float[DepthCacheSlots];
        private static int depthCacheCursor;

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
        /// 그 지점에서 **부유체가 타고 오르내리는** 수면의 절대 높이(m). 뗏목 흔들림이 쓰는 값이다.
        /// 쇄파 감쇠를 걸지 않은 원 파형이라 셰이더가 그리는 파도와 문자 그대로 같다.
        /// 힙 할당 없음, sin 4회.
        ///
        /// **SampleWaveOffset(Vector3)와 값이 다를 수 있다(의도된 비대칭 - 아래 그쪽 주석 참고).**
        /// 쇄파대의 부유체는 파고가 깎여도 그 쇄파에 얹혀 오히려 더 요동친다. 반대로 "이 점이
        /// 물에 잠겼는가"는 그 자리 물기둥의 문제라 수심을 넘어설 수 없다. 두 질문의 답이 달라서
        /// 두 함수가 갈린 것이지, 한쪽이 다른 쪽의 근사가 아니다.
        /// </summary>
        public static float SampleHeight(Vector3 worldPos)
        {
            return seaLevelY + SampleWaveOffset(worldPos.x, worldPos.z, WaveTime);
        }

        /// <summary>지정한 시각의 수면 절대 높이(m, 감쇠 없음). 검증/재현용 오버로드.</summary>
        public static float SampleHeight(Vector3 worldPos, float time)
        {
            return seaLevelY + SampleWaveOffset(worldPos.x, worldPos.z, time);
        }

        /// <summary>
        /// 평균 해수면으로부터의 파도 편차(m), **얕은 물 쇄파 감쇠 포함**. 자기 기준 수면
        /// (예: PlayerController.waterLevel)을 이미 갖고 있는 쪽이 그 값을 유지한 채 파도만 얹을 때 쓴다.
        /// 현재 유일한 호출자는 PlayerController.CurrentWaterSurfaceY(수영/보행 전환 판정)다.
        ///
        /// ── [파도 v5] 왜 여기에만 감쇠를 거는가 ──────────────────────────────────────
        /// 진폭을 2.4배 올리면서 생긴 유일한 회귀가 이것이다: 폭풍에서 파도 편차가 ±1.45m가 되고
        /// 판정 배율 0.75를 곱해도 ±1.09m라, **해수면보다 1m 높은 마른 모래 위에 서 있어도**
        /// 수면이 발을 넘어 수영 모드로 뒤집힌다(그것도 파주기마다 깜빡인다).
        /// 실제 파도는 수심이 얕아지면 부서지며 낮아지므로, 그 물리를 그대로 판정에 넣는다:
        ///     감쇠 = min(1, 수심 / 마루한계수심),   마루한계수심 = 진폭 합 (crestLimitDepth)
        /// 지면이 해수면 위(수심 ≤ 0)면 감쇠가 정확히 0이라 파도가 걷기 판정에 **아예 관여하지
        /// 않는다** - 즉 뭍에서는 followOceanWaves를 끈 것과 100% 같은 동작이 되고, 수영 경계는
        /// 파도를 넣기 전과 똑같이 "해수면 등고선"이 된다. 수심이 마루한계수심 이상이면(폭풍 1.45m,
        /// 잔잔 0.50m) 감쇠가 정확히 1이라 종전과 1비트도 다르지 않다 - 뗏목이 떠 있는 자리(실측
        /// 수심 약 2m)와 외해가 전부 여기 들어간다. 바뀌는 곳은 오직 그 사이, 발목~허리 깊이의
        /// 물가뿐이고 거기는 애초에 (평평한 waterLevel 기준으로도) 수영 모드인 구간이다.
        ///
        /// 부작용이 없다는 근거를 뒤집어 말하면: 이 감쇠는 "파도 때문에 수영 모드가 되는 일"만
        /// 없애고, "물에 들어가서 수영 모드가 되는 일"은 한 글자도 바꾸지 않는다.
        ///
        /// ※ 셰이더(MGOcean)는 이 감쇠를 반영하지 않는다. 감쇠는 **파형을 바꾸는 것이 아니라
        ///   "잠김 판정"을 보정하는 것**이고, 셰이더 쪽에 넣으려면 화면공간 깊이(뷰 방향 물기둥)에
        ///   의존해야 해서 C#의 수직 수심과 원리적으로 다른 값이 된다. C#↔셰이더 단일 소스 계약은
        ///   **파형 자체**(아래 SampleWaveOffset(x,z,t) / SampleSlope)에 걸려 있고, 그쪽은 그대로다.
        /// </summary>
        public static float SampleWaveOffset(Vector3 worldPos)
        {
            return SampleWaveOffset(worldPos.x, worldPos.z, WaveTime) * SubmergenceScale(worldPos);
        }

        /// <summary>
        /// 그 지점의 얕은 물 쇄파 감쇠 계수(0~1). 1 = 깊은 바다(원 파형 그대로), 0 = 해수면 위 지면.
        /// 지형 높이는 정적이라 결과를 짧게 캐시하고, 레이는 RaycastNonAlloc이라 힙 할당이 0이다.
        /// </summary>
        public static float SubmergenceScale(Vector3 worldPos)
        {
            float depth = SampleWaterDepth(worldPos.x, worldPos.z);
            if (depth <= 0f)
                return 0f;

            return crestLimitDepth > 0.001f ? Mathf.Min(1f, depth / crestLimitDepth) : 1f;
        }

        /// <summary>
        /// 그 XZ의 수심(m, 해수면 기준. 지면이 해수면 위면 음수). 캐시 적중이면 계산이 없다.
        /// </summary>
        private static float SampleWaterDepth(float x, float z)
        {
            float now = Time.unscaledTime;

            for (int i = 0; i < DepthCacheSlots; i++)
            {
                if (now - depthCacheStamp[i] > DepthCacheSeconds)
                    continue;

                float dx = depthCacheX[i] - x;
                float dz = depthCacheZ[i] - z;
                if (dx * dx + dz * dz <= DepthCacheRadius * DepthCacheRadius)
                    return depthCacheDepth[i];
            }

            float depth = ProbeWaterDepth(x, z);

            int slot = depthCacheCursor;
            depthCacheCursor = (depthCacheCursor + 1) % DepthCacheSlots;
            depthCacheX[slot] = x;
            depthCacheZ[slot] = z;
            depthCacheDepth[slot] = depth;
            depthCacheStamp[slot] = now;
            return depth;
        }

        /// <summary>
        /// 수심 실측. 두 경로를 이어 붙여 물가부터 외해까지 **끊김 없는** 수심장을 만든다:
        ///  (1) 섬 메시 반지름 R 안쪽 - TerrainSampler.SnapToGround("Island_" 콜라이더만 인정).
        ///      섬 메시는 해안선 밖(q &gt; 1) 구간도 해수면 아래로 잠긴 지형이므로(IslandMeshGenerator
        ///      (12) 해안 잠수) 물가~R 사이의 얕은 여울 수심이 여기서 그대로 나온다.
        ///  (2) R ~ R+스커트 폭 - SeabedGenerator.TrySampleSeabed(생성 수식과 같은 해석식, 레이 없음).
        ///  (3) 둘 다 아니면 외해로 본다(감쇠 = 1). 섬/해저가 아직 생성되지 않은 프레임과 테스트 씬도
        ///      이 갈래로 떨어져 **종전 동작(감쇠 없음)** 이 되므로, 참조가 없어도 안전하다.
        /// </summary>
        private static float ProbeWaterDepth(float x, float z)
        {
            // SnapToGround는 지형을 못 맞히면 넘긴 위치를 그대로 돌려준다 → 센티넬 y로 히트 여부를 판별한다
            // (RaftStructure.SampleTerrainHeight와 같은 수법). 레이는 y = +100에서 아래로 1000m 간다.
            Vector3 probe = new Vector3(x, DepthProbeSentinelY, z);
            Vector3 ground = TerrainSampler.SnapToGround(probe, 500f, 1000f);
            if (ground.y > DepthProbeSentinelY + 1f)
                return seaLevelY - ground.y;

            if (SeabedGenerator.TrySampleSeabed(new Vector3(x, seaLevelY, z), out float seabedY))
                return seaLevelY - seabedY;

            return DeepWaterDepth;
        }

        /// <summary>
        /// 파고 함수 본체(**쇄파 감쇠 없는 원 파형**). h = Σ A_i · sin(k_i · dot(D_i, p) + ω_i · t).
        /// MGOcean.shader의 MGWaveHeight()와 **문자 그대로 같은 식**이다(같은 전역 파라미터를 읽는다).
        /// C#↔셰이더 단일 소스 계약이 걸려 있는 지점이므로, 여기에는 어떤 보정도 얹지 않는다.
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
            waveBearing = BaseBearingDegrees;
            depthCacheCursor = 0;
            for (int i = 0; i < DepthCacheSlots; i++)
            {
                depthCacheX[i] = 0f;
                depthCacheZ[i] = 0f;
                depthCacheDepth[i] = DeepWaterDepth;
                depthCacheStamp[i] = float.NegativeInfinity; // 전부 만료 상태로 시작한다
            }
            RecomputeWaves(0f, 2.9f, 1.45f, 0f);
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

            // 파도 방향은 전역 바람(WindSystem)을 느리게 따라간다. 바람 시스템이 없으면 기준 방위에
            // 머무르므로, 이 게임의 예전 바다와 정확히 같은 파도가 나온다.
            float targetBearing = WindSystem.Active != null
                ? WindSystem.BearingDegrees
                : BaseBearingDegrees;

            waveBearing = Mathf.MoveTowardsAngle(waveBearing, targetBearing,
                Mathf.Max(0f, bearingFollowDegreesPerSecond) * Time.unscaledDeltaTime);

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
                Mathf.Max(1f, stormSpeedScale),
                Mathf.DeltaAngle(BaseBearingDegrees, waveBearing));
            PushToShader();
        }

        /// <summary>
        /// 거칠기에서 성분별 (진폭 · 파수 · 각속도 · 방향)을 유도한다.
        /// 파장은 거칠기와 무관하게 고정이고, 진폭과 각속도만 잔잔↔거침 사이를 선형 보간한다.
        /// 방향은 네 성분이 각도 관계를 유지한 채 bearingOffset만큼 통째로 회전한다(전역 바람).
        ///
        /// C#(부력)과 셰이더(그림)가 **같은 값**을 쓰는 것이 이 클래스의 전제이므로, 회전도
        /// 여기 한 곳에서만 일어나고 PushToShader가 그 결과를 그대로 내보낸다 - 둘이 갈릴 자리가 없다.
        /// </summary>
        private static void RecomputeWaves(float rough, float ampScaleAtStorm, float speedScaleAtStorm,
            float bearingOffsetDegrees)
        {
            float ampScale = Mathf.Lerp(1f, ampScaleAtStorm, rough);
            float speedScale = Mathf.Lerp(1f, speedScaleAtStorm, rough);

            // 마루 한계 수심 = 진폭 합(= 최대 마루 높이). crestLimitDepth 주석에 근거가 있다.
            float ampSum = 0f;

            for (int i = 0; i < ComponentCount; i++)
            {
                float k = 2f * Mathf.PI / Wavelengths[i];
                float omega = Mathf.Sqrt(Gravity * k) * speedScale;
                float rad = (DirectionDegrees[i] + bearingOffsetDegrees) * Mathf.Deg2Rad;
                float amp = CalmAmplitudes[i] * ampScale;

                ampSum += amp;

                SetComponent(ref waveAmp, i, amp);
                SetComponent(ref waveK, i, k);
                SetComponent(ref waveOmega, i, omega);
                SetComponent(ref waveDirX, i, Mathf.Cos(rad));
                SetComponent(ref waveDirZ, i, Mathf.Sin(rad));
            }

            crestLimitDepth = ampSum;
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
