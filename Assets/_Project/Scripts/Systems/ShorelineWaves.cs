using UnityEngine;
using UnityEngine.SceneManagement;

namespace MakeGame.Systems
{
    /// <summary>
    /// 해변 스와시(파도가 밀려왔다 빠지는 연출)의 **단일 소스 드라이버**.
    /// MGShoreline 셰이더(Resources/Shaders/MGShoreline, 모래 캡 3장이 쓴다)가 읽는 전역
    /// 두 개를 매 프레임 밀어 넣는다.
    ///
    /// ── 밀어 주는 전역 ───────────────────────────────────────────────────────────
    ///  · _MG_ShoreTime   (float)  : 파도 시계(초) = Time.time.
    ///  · _MG_ShoreParams (float4) : (주기 s, 스와시 전선 속도 m/s, 도달거리@잔잔 m, 도달거리@거침 m)
    ///
    /// 바다 거칠기(_MG_SeaState.x)는 **OceanWaves.cs가 이미 밀고 있는 전역**을 셰이더가 그대로
    /// 읽는다. 여기서 다시 밀지 않는다 - 두 곳이 같은 전역을 쓰면 마지막에 쓴 쪽이 이기는
    /// 경합이 생기고, 파도 세기의 단일 소스는 OceanWaves다(그쪽 상수 표가 물리도 함께 정한다).
    ///
    /// ── 타이틀 화면 정지 계약 ────────────────────────────────────────────────────
    /// 시계는 Time.time이다. Time.timeScale = 0에서 멈추므로 타이틀에서 파도가 정지한다
    /// (MGOcean의 _MG_WaveTime / MGGrass의 _MG_WindTime과 정확히 같은 계약).
    /// 셰이더 내장 _Time을 쓰면 이 정지가 깨지므로 셰이더는 _Time을 쓰지 않는다.
    ///
    /// ── 부트스트랩 ───────────────────────────────────────────────────────────────
    /// 씬에 인스턴스가 없다. 프로젝트의 자기 부트스트랩 선례(OceanWaves / GrassFieldDriver /
    /// CursorLockController - 16곳)와 동일한 SubsystemRegistration + sceneLoaded + 중복 가드다.
    /// 정적 캐시는 같은 훅에서 리셋한다(R1 규약 - 도메인 리로드를 끈 플레이 모드 대비).
    ///
    /// ── 비용 ─────────────────────────────────────────────────────────────────────
    /// 프레임당 SetGlobalFloat 1회(+ 값이 바뀐 프레임에만 SetGlobalVector 1회). 힙 할당 0
    /// (Vector4는 구조체다). 드로우콜 증가 0 - 이 클래스는 렌더 오브젝트를 만들지 않는다.
    /// </summary>
    [DisallowMultipleComponent]
    public class ShorelineWaves : MonoBehaviour
    {
        // ── 파도 파라미터 (단일 소스) ────────────────────────────────────────────
        // 값의 근거는 실측 지형이다. 물가(y=0 등고선)에서 모래 3단 경계까지의 수평 거리를
        // IslandMeshGenerator의 거리장과 같은 식으로 재 보면(Tools/terrain/preview.py 기반 검산):
        //   시작 섬 R=50, 프로파일 0 : WetTop(0.30m)까지 0.43m / DampTop(0.75m)까지 1.30m /
        //                              1.30m 등고선까지 4.4m / 잔디선(1.93m)까지 15.3m
        //   R=90  프로파일 4         : 0.70m / 2.30m / 11.7m
        //   R=200 프로파일 3         : 2.48m / 7.20m / 18.8m
        // 즉 물가 근방 해변 경사는 0.23~0.69(m/m)다. 도달거리를 1.3m로 잡으면 잔잔한 날의
        // 스와시가 딱 "젖은 모래 띠 + 축축한 모래 띠"를 덮고, 2.8m면 거친 날에 마른 모래
        // 아래쪽까지 올라온다 - 바다 거칠기가 눈으로 읽히는 폭이다.
        //
        // ── [파도 v5] 도달거리 상향 (1.3/2.8 → 2.0/4.5) ─────────────────────────────
        // OceanWaves의 진폭이 0.212 → 0.500m(잔잔) / 0.594 → 1.45m(폭풍)로 올라갔다. 스와시
        // 도달거리는 파고에 비례하므로 같은 비율(약 1.55/1.60배)로 올린다. 해변 경사 0.23~0.69로
        // 환산하면 잔잔 2.0m는 물가에서 높이 0.46~1.38m까지, 폭풍 4.5m는 1.04~3.1m까지 젖는다 -
        // 잔잔한 날은 여전히 젖은/축축한 모래 띠(경계 0.30/0.75m) 안쪽에 머물고, 폭풍에는 마른
        // 모래를 확실히 넘어 잔디선(1.93m) 근처까지 올라온다.
        // ※ 전역 계약(_MG_ShoreParams의 채널 의미, _MG_SeaState를 여기서 쓰지 않는 규칙)은 그대로다 -
        //   리본 메시 쪽이 _MG_SeaState에 비례해 높이를 잡는 계약도 건드리지 않았다.

        [Header("스와시 파형")]
        [Tooltip("파도 한 번의 주기(초). 바다 파도 성분의 주기(8.7/6.6/4.9/3.7s) 중 지배 성분에 맞춘 값.")]
        public float wavePeriod = 7.5f;

        [Tooltip("스와시 전선이 모래를 타고 올라가는 속도(m/s). 물가에서 d미터 안쪽은 d/이 값 초만큼 늦게 젖는다.")]
        public float frontSpeed = 1.8f;

        [Tooltip("잔잔한 바다(거칠기 0)에서 파도가 물가보다 안쪽으로 올라오는 최대 거리(m).")]
        public float runupCalm = 2.0f;

        [Tooltip("거친 바다(거칠기 1)에서의 최대 도달 거리(m). 클수록 폭풍에 파도가 해변 깊숙이 친다.")]
        public float runupStorm = 4.5f;

        // ── 셰이더 전역 프로퍼티 ID ──────────────────────────────────────────────
        // MGShoreline.shader는 이 둘을 Properties 블록 **밖**·CBUFFER(UnityPerMaterial) **밖**에서
        // 선언한다. Properties에 넣으면 머티리얼 프로퍼티가 되어 전역 설정이 무시되고,
        // CBUFFER 안에 넣으면 SRP Batcher 레이아웃과 충돌한다(MGOcean과 같은 규약).
        private static readonly int ShoreTimeProperty = Shader.PropertyToID("_MG_ShoreTime");
        private static readonly int ShoreParamsProperty = Shader.PropertyToID("_MG_ShoreParams");

        /// <summary>마지막으로 셰이더에 밀어 넣은 파라미터. 값이 바뀐 프레임에만 다시 민다.</summary>
        private static Vector4 pushedParams;

        /// <summary>씬에 살아 있는 드라이버. 없으면 null(그래도 전역 기본값은 부트스트랩이 밀어 둔다).</summary>
        public static ShorelineWaves Active { get; private set; }

        // ── 공개 조회 ────────────────────────────────────────────────────────────

        /// <summary>현재 셰이더에 들어가 있는 스와시 주기(초).</summary>
        public static float Period => pushedParams.x;

        /// <summary>현재 셰이더에 들어가 있는 스와시 전선 속도(m/s).</summary>
        public static float FrontSpeed => pushedParams.y;

        /// <summary>
        /// 현재 바다 거칠기에서의 최대 도달 거리(m). 셰이더가 하는 것과 같은 보간이며,
        /// 필요하면 게임플레이 쪽(예: 물가 발자국 판정)이 같은 수를 볼 수 있게 열어 둔다.
        /// </summary>
        public static float CurrentRunup =>
            Mathf.Lerp(pushedParams.z, pushedParams.w, Mathf.Clamp01(OceanWaves.Roughness01));

        /// <summary>
        /// 파도 시계(초). 셰이더의 _MG_ShoreTime과 같은 값이다.
        /// Time.timeScale = 0에서 멈춘다(타이틀 정지 계약).
        /// </summary>
        public static float ShoreTime => Time.time;

        // ── 수명 주기 ────────────────────────────────────────────────────────────

        /// <summary>
        /// 씬이 로드될 때마다 드라이버를 하나 만든다(중복 가드 포함). 동시에 정적 캐시를 리셋하고
        /// 기본 파라미터를 즉시 셰이더에 밀어 넣는다 - 드라이버의 첫 Update보다 먼저 그려지는
        /// 프레임에서도 해변이 "도달거리 0"으로 보이지 않게 하기 위해서다.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Bootstrap()
        {
            // 도메인 리로드를 끈 플레이 모드에서 이전 실행 값이 남는 것을 막는다(R1 리셋 훅).
            Active = null;
            pushedParams = Vector4.zero;
            PushParams(7.5f, 1.8f, 2.0f, 4.5f);
            Shader.SetGlobalFloat(ShoreTimeProperty, 0f);

            SceneManager.sceneLoaded += (scene, mode) =>
            {
                if (FindAnyObjectByType<ShorelineWaves>() != null)
                    return;

                var go = new GameObject("ShorelineWaves");
                go.AddComponent<ShorelineWaves>();
            };
        }

        private void Awake()
        {
            Active = this;
            PushParams(wavePeriod, frontSpeed, runupCalm, runupStorm);
        }

        private void OnDestroy()
        {
            if (Active == this)
                Active = null;
        }

        /// <summary>
        /// 매 프레임 파도 시계를 갱신한다. 파라미터는 인스펙터에서 만지지 않는 한 바뀌지 않으므로
        /// 값이 실제로 달라진 프레임에만 다시 민다(SetGlobal 호출을 1회로 유지).
        /// </summary>
        private void Update()
        {
            Shader.SetGlobalFloat(ShoreTimeProperty, Time.time);
            PushParams(wavePeriod, frontSpeed, runupCalm, runupStorm);
        }

        /// <summary>
        /// 파라미터를 정리(하한 클램프)해 셰이더 전역에 밀어 넣는다. 셰이더도 같은 하한을 다시
        /// 걸지만(0 나눗셈 방지), 여기서 먼저 막아 두면 조회 프로퍼티도 항상 유효한 값을 준다.
        /// </summary>
        private static void PushParams(float period, float speed, float calm, float storm)
        {
            var next = new Vector4(
                Mathf.Max(period, 0.5f),
                Mathf.Max(speed, 0.2f),
                Mathf.Max(calm, 0f),
                Mathf.Max(storm, 0f));

            if (next == pushedParams)
                return;

            pushedParams = next;
            Shader.SetGlobalVector(ShoreParamsProperty, next);
        }
    }
}
