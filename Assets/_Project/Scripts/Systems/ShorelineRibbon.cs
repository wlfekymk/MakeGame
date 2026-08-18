using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace MakeGame.Systems
{
    /// <summary>
    /// 물가(y = 0 등고선) 선분 모음. IslandMeshGenerator.BakeShoreField가 **이미 뽑아 둔** 마칭
    /// 트라이앵글 결과를 그대로 담아 넘기는 그릇이다(리본이 등고선을 다시 계산하지 않는 근거).
    ///
    /// 선분 i는 (ax[i], az[i]) - (bx[i], bz[i])이고 좌표는 **섬 로컬 XZ(m)**다.
    /// keyA/keyB는 그 끝점이 놓인 **지형 메시 변의 식별자**(정점 인덱스 쌍을 하나의 long으로 접은 값)다.
    /// 이 키가 있어서 조각 잇기를 좌표 비교(부동소수 오차 허용치가 필요하다) 대신 **위상(topology)
    /// 비교**로 할 수 있다 - 같은 변을 공유하는 두 삼각형은 같은 키를 내므로 오차가 원리적으로 없다.
    /// downX/downZ는 그 선분이 나온 지형 삼각형의 **내리막 단위 방향(XZ)** = 바다 쪽이다.
    /// (반지름 방향을 쓰면 석호·수로 안쪽 물가에서 바다 방향이 뒤집힌다 - 그래서 높이 기울기를 쓴다.)
    /// </summary>
    public sealed class ShoreContour
    {
        public int count;
        public float[] ax;
        public float[] az;
        public float[] bx;
        public float[] bz;
        public long[] keyA;
        public long[] keyB;
        public float[] downX;
        public float[] downZ;
    }

    /// <summary>
    /// [해변 파도 3단계] 부서지는 파도 마루 리본.
    ///
    /// 물가를 따라가는 얇은 띠 메시를 섬당 하나 만들고, MG/ShoreBreak 셰이더가 그 위에서
    /// "바다에서 마루가 밀려와 → 솟았다가 → 부서져 사라지는" 한 주기를 정점 변위로 그린다.
    ///
    /// ── 기준선을 다시 계산하지 않는다 ──────────────────────────────────────────
    /// BakeShoreField(1단계)가 모래 캡 UV2를 구우려고 이미 지형 삼각형에서 y = 0 등고선을 마칭
    /// 트라이앵글로 뽑는다. **그 선분 집합이 곧 해안선 폴리라인**이므로 리본은 그것을 넘겨받아
    /// (ShoreContour) 잇기만 한다. 등고선 추출을 두 곳이 각자 하면 값이 갈라질 수 있고, 무엇보다
    /// 이미 낸 비용을 두 번 내는 일이다.
    ///
    /// ── 조각 잇기 ──────────────────────────────────────────────────────────────
    /// 마칭 트라이앵글이 내놓는 선분은 순서가 없다. 이웃한 두 삼각형은 **같은 변**을 가로지르므로
    /// 그 변의 식별자(정점 인덱스 쌍)를 키로 끝점을 짝지으면 폴리라인이 복원된다. 좌표 비교가
    /// 아니라 위상 비교라 허용치(epsilon)가 필요 없다. 만·수로·석호로 등고선이 여러 조각으로
    /// 끊긴 섬에서는 조각이 여러 개 나오고, **둘레 3m 미만 조각은 버린다**(파편 잡음).
    ///
    /// ── 정점에 싣는 리본 로컬 좌표(셰이더 계약. MGShoreBreak.shader 헤더와 단일 소스) ──
    ///   uv (TEXCOORD0) : x = 해안선을 따라간 거리 u(m), y = 물가로부터의 바다쪽 진행도 v(0~1)
    ///   uv2(TEXCOORD1) : x = 부호 있는 물가 거리(m, 리본은 전부 바다 쪽이라 **음수**) - 지형 UV2의
    ///                    부호 규약(+ 내륙 / - 물속)과 같다. y = 리본 폭(m).
    ///   uv3(TEXCOORD2) : 물가 → 바다 단위 방향(XZ). 마루 말림(수평 변위)과 노멀 재구성에 쓴다.
    ///
    /// ── 위상은 스와시와 같은 계약 ──────────────────────────────────────────────
    /// 셰이더가 tLocal = _MG_ShoreTime + s/celerity(= MGShoreline의 t - d/celerity를 d &lt; 0으로
    /// 연장한 것)를 쓴다. 그래서 마루가 물가에 닿는 순간과 스와시 전선이 물가에서 출발하는 순간이
    /// **같은 시각**이다. 이 클래스의 CrestSeawardDistance / SwashInlandDistance가 그 두 위치를
    /// C#에서 같은 식으로 계산해 주므로 수치로 대조할 수 있다(연속성 검증용, 게임플레이에서도 쓸 수 있다).
    ///
    /// ── 결정성·세이브 ──────────────────────────────────────────────────────────
    /// System.Random / UnityEngine.Random을 만들지도 소비하지도 않는다(**rng 소비 0**). 입력은
    /// 이미 확정된 등고선 선분뿐인 순수 기하다. 세이브 포맷·기존 배치·밀도·지형 정점 데이터
    /// (BakeShoreField의 UV2 계약 포함)에 한 비트도 손대지 않는다. 리본 오브젝트 이름은
    /// "ShoreRibbon_" 접두사라 TerrainSampler류의 "Island_" 필터에 구조적으로 걸리지 않고,
    /// 콜라이더도 없어서 어떤 레이캐스트에도 잡히지 않는다.
    ///
    /// ── 비용 ───────────────────────────────────────────────────────────────────
    ///  · 섬당 드로우콜 +1. 리본은 서브메시 1개 + **월드 공유 머티리얼 1장**이라 SRP Batcher가 묶는다.
    ///  · 정점은 섬당 3000개 상한. 넘칠 것 같으면 정거장 간격을 늘려(폴리라인 단순화) 맞춘다.
    ///  · 프레임당 할당 0. 드라이버는 미리 만든 레지스트리를 for로 훑고 renderer.enabled만 토글한다.
    ///  · 카메라에서 섬 테두리까지 300m를 넘으면 렌더를 끈다(GrassFieldSystem.MaxRenderDistance 관례).
    ///  · 물보라는 **월드 전체에 파티클 시스템 1개**뿐이다(가장 가까운 섬의 부서지는 지점 3곳에만,
    ///    주기당 한 번 얇게). EffectBuilder의 공유 파티클 머티리얼을 쓰므로 머티리얼도 늘지 않는다.
    /// </summary>
    public static class ShorelineRibbon
    {
        // ── 이름/규격 상수 ─────────────────────────────────────────────────────────

        /// <summary>리본 루트 이름 접두사. "Island_"로 시작하지 않는 것이 TerrainSampler 안전의 전제다.</summary>
        public const string RibbonNamePrefix = "ShoreRibbon_";

        /// <summary>물가에서 바다 쪽으로 뻗는 리본 폭(m). 마루는 물 위에서 부서지므로 전부 바다 쪽이다.</summary>
        private const float RibbonWidth = 6f;

        /// <summary>가로(연안 직교) 줄 수(요구 범위 4~6의 상단).</summary>
        private const int RowCount = 6;

        /// <summary>
        /// 줄의 v 위치. **등간격이 아니다** - 마루가 실제로 솟고 부서지는 구간에 줄을 몰아 준다.
        /// 셰이더 기준값으로 그 구간은 v = 0.16(_BreakV, 무너지는 자리) ~ 0.30(_ShoalPeakV, 최고점)이고,
        /// 마루 앞면 폭은 1.4m = v로 0.23이다. 등간격 6줄이면 줄 간격이 1.2m라 앞면(1.4m)이 겨우 한 칸에
        /// 걸려 마루가 삼각형 하나로 접힌 것처럼 보인다. 이 배치는 그 구간의 줄 간격이 0.78~1.08m라
        /// 앞면이 두 칸에 걸치고, 대신 바깥(v &gt; 0.7 - 아직 평평하거나 이미 알파가 0인 구간)이 성겨진다.
        /// 정점 수는 그대로다.
        ///   v =   0.00  0.13  0.28  0.46  0.70  1.00
        ///   m  =   0.00  0.78  1.68  2.76  4.20  6.00 (폭 6m 기준)
        /// </summary>
        private static readonly float[] RowProgress = { 0f, 0.13f, 0.28f, 0.46f, 0.70f, 1f };

        /// <summary>기본 정거장 간격(m). 정점 상한에 걸리면 이 값이 커진다(폴리라인 단순화).</summary>
        private const float BaseStationSpacing = 2f;

        /// <summary>섬당 정점 상한.</summary>
        private const int MaxVerticesPerIsland = 3000;

        /// <summary>정점 상한에서 역산한 정거장 상한.</summary>
        private const int MaxStationsPerIsland = MaxVerticesPerIsland / RowCount;

        /// <summary>이보다 짧은 등고선 조각은 버린다(m). 만·수로 경계의 파편 잡음 제거.</summary>
        private const float MinChainLength = 3f;

        /// <summary>
        /// 리본을 해수면보다 살짝 띄우는 높이(m). 물가에서는 지형·모래 캡과 같은 평면이라
        /// z-파이팅이 날 수 있어서 준다. 모래 캡은 지형 위 0.08m라 물가 안쪽 12cm 남짓만
        /// 캡에 가려지는데, 그 구간은 애초에 MGShoreline의 스와시 거품 담당이다.
        /// </summary>
        private const float RibbonLift = 0.05f;

        /// <summary>카메라 → 섬 테두리 거리가 이보다 멀면 리본을 그리지 않는다(m).</summary>
        private const float MaxRenderDistance = 300f;

        // ── 물보라 ────────────────────────────────────────────────────────────────

        /// <summary>물보라를 뿌리는 최대 거리(m). 이 밖에서는 파티클이 화면에서 몇 픽셀도 안 된다.</summary>
        private const float SprayDistance = 70f;

        /// <summary>섬당 물보라 지점 수. "부서지는 지점 몇 곳에만 얇게"의 몇 곳.</summary>
        private const int SprayAnchorCount = 3;

        /// <summary>한 지점에서 한 주기에 뿜는 입자 수.</summary>
        private const int SprayPerBurst = 4;

        /// <summary>물보라 지점의 바다쪽 거리(m) = 셰이더 _BreakV(0.16) × 리본 폭 - 부서지는 자리다.</summary>
        private const float SprayAnchorSeaward = 0.16f * RibbonWidth;

        /// <summary>
        /// 입자 사출 방향 표(리본 로컬: x = 연안, y = 위, z = 바다쪽). 무작위를 쓰지 않는 이유는
        /// UnityEngine.Random이 전역 상태라 매 프레임 소비하면 다른 소비자의 수열이 밀리기 때문이다.
        /// 방향이 4개로 고정돼도 사출 시각과 지점이 파도마다 달라 반복으로 읽히지 않는다.
        /// </summary>
        private static readonly Vector3[] SprayBurstDirections =
        {
            new Vector3(-0.34f, 0.92f, -0.20f),
            new Vector3(0.12f, 0.98f, -0.16f),
            new Vector3(0.41f, 0.88f, -0.24f),
            new Vector3(-0.06f, 0.99f, -0.12f),
        };

        /// <summary>
        /// 물보라 사출 속력(m/s). 거칠기로 0.6~1.35배 된다(UpdateSpray). 중력 배율 0.85에서
        /// 최고 상승 높이가 (v·1.35)²/(2·9.81·0.85) ≈ 1.1m라, 폭풍 마루(현재 진폭 표에서 2.6m)의
        /// 절반쯤까지만 튄다 - 물보라가 마루보다 높이 솟으면 파도가 아니라 분수처럼 보인다.
        /// </summary>
        private const float SpraySpeed = 3.2f;

        // ── 셰이더 계약 상수 (MGShoreBreak.shader / MGShoreline.shader와 같은 값) ──
        // ★ 셰이더 쪽 기본값을 바꾸면 여기도 같이 바꿔야 한다 ★ - 아래 위상 계산이 셰이더와
        // 같은 수를 내야 물보라가 실제로 마루가 부서지는 순간에 튄다.

        /// <summary>연안 방향 도달 시각 변주 계수(= 두 셰이더의 _AlongshoreWobble 기본값).</summary>
        public const float AlongshoreWobble = 0.18f;

        // ── 공유 에셋 ─────────────────────────────────────────────────────────────

        private static Material ribbonMaterial;
        private static int shaderProbeFrame = -1;
        private static Texture2D foamTexture;
        private static int foamProbeFrame = -1;
        private static bool foamApplied;

        /// <summary>MGShoreBreak의 거품 텍스처 슬롯(문자열 조회를 섬마다 반복하지 않게 ID로 굳힌다).</summary>
        private static readonly int FoamMapProperty = Shader.PropertyToID("_FoamMap");

        // ── 레지스트리 ────────────────────────────────────────────────────────────

        private sealed class RibbonRecord
        {
            public Transform root;             // 파괴/비활성 감지
            public MeshRenderer renderer;      // 거리 컷 토글 대상
            public Vector3 center;             // 섬 중심(월드)
            public float radius;               // 섬 지형 반지름 R(m)
            public Vector3[] sprayAnchors;     // 부서지는 지점(월드). 없으면 길이 0
            public Vector3[] sprayForward;     // 그 지점의 바다 방향(월드, 단위)
            public Vector3[] sprayRight;       // 그 지점의 연안 방향(월드, 단위)
            public int[] sprayCycle;           // 마지막으로 뿜은 파도 주기 번호
        }

        private static readonly List<RibbonRecord> registry = new List<RibbonRecord>();

        // ── 진단 카운터(로그 1줄용) ────────────────────────────────────────────────
        private static int pendingVertexTotal;
        private static int pendingRibbonCount;
        private static float pendingLengthTotal;
        private static bool loggerScheduled;

        /// <summary>
        /// 도메인 리로드를 끈 플레이 모드에서 static 캐시가 이전 실행의 파괴된 머티리얼/레코드를
        /// 들고 시작하지 않게 초기 상태로 되돌린다(R1 리셋 훅 - 프로젝트 관례).
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticCache()
        {
            registry.Clear();
            ribbonMaterial = null;
            shaderProbeFrame = -1;
            foamTexture = null;
            foamProbeFrame = -1;
            foamApplied = false;
            pendingVertexTotal = 0;
            pendingRibbonCount = 0;
            pendingLengthTotal = 0f;
            loggerScheduled = false;
        }

        // ═══════════════════════════════════════════════════════════════════════════
        //  생성 (섬당 1회, 월드 생성 시점의 동기 흐름)
        // ═══════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// 섬 하나의 마루 리본을 만든다. IslandMeshGenerator.BuildGroundCaps가 지형 메시를 확보한
        /// 직후(= BakeShoreField가 등고선을 뽑아 둔 뒤) 1회 호출한다.
        ///
        /// 다음 경우에는 **조용히 아무것도 하지 않는다**(경고도 남기지 않는다):
        ///   · 셰이더(Shaders/MGShoreBreak)를 못 읽었다 → 폴백 렌더 없음(URP Lit으로 그리면 바다 위에
        ///     불투명 판이 깔린다). MGGrass의 "셰이더 없으면 생략" 계약과 같다.
        ///   · 등고선이 비었다(섬 전체가 물 위이거나 물속 - BakeShoreField가 segmentCount 0으로 돌려준다).
        ///   · 이을 수 있는 조각이 전부 3m 미만이다.
        /// </summary>
        /// <param name="islandObject">섬 지형 오브젝트("Island_{id}_{size}"). 리본은 이 자식으로 붙는다.</param>
        /// <param name="contour">BakeShoreField가 넘겨준 y=0 등고선 선분(섬 로컬 XZ).</param>
        /// <param name="radius">섬 지형 반지름 R(m). 거리 컷 계산에만 쓴다.</param>
        public static void Build(GameObject islandObject, ShoreContour contour, float radius)
        {
            if (islandObject == null || contour == null || contour.count <= 0 || radius <= 0f)
                return;

            // 같은 섬에 두 번 불려도(방어) 리본이 겹으로 깔리지 않게 한다.
            string objectName = RibbonNamePrefix + islandObject.name;
            if (islandObject.transform.Find(objectName) != null)
                return;

            Material material = GetRibbonMaterial();
            if (material == null)
                return; // 셰이더 부재 - 조용한 생략(계약)

            List<ShoreChain> chains = AssembleChains(contour);
            if (chains == null || chains.Count == 0)
                return;

            Mesh mesh = BuildRibbonMesh(chains, out int vertexCount, out float contourLength);
            if (mesh == null)
                return;

            var go = new GameObject(objectName);
            go.transform.SetParent(islandObject.transform, false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;

            go.AddComponent<MeshFilter>().sharedMesh = mesh;

            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            // 반투명 물이라 그림자를 만들 수도(줄무늬 그림자가 모래에 찍힌다) 받을 수도 없다.
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            // 콜라이더는 붙이지 않는다 - 순수 시각 요소이고, 레이캐스트에 잡히면 배치·건축 판정이 바뀐다.

            var record = new RibbonRecord
            {
                root = go.transform,
                renderer = renderer,
                center = islandObject.transform.position,
                radius = radius,
                sprayCycle = new int[SprayAnchorCount],
            };
            CollectSprayAnchors(chains, islandObject.transform.position, record);
            registry.Add(record);

            pendingVertexTotal += vertexCount;
            pendingLengthTotal += contourLength;
            pendingRibbonCount++;
            if (!loggerScheduled)
            {
                loggerScheduled = true;
                go.AddComponent<RibbonBatchLogger>();
            }
        }

        // ═══════════════════════════════════════════════════════════════════════════
        //  폴리라인 복원
        // ═══════════════════════════════════════════════════════════════════════════

        /// <summary>이어 붙인 등고선 조각 하나. 좌표는 섬 로컬 XZ(m)다.</summary>
        private sealed class ShoreChain
        {
            public List<Vector2> points = new List<Vector2>();  // 폴리라인 꼭짓점
            public List<Vector2> seaward = new List<Vector2>(); // 꼭짓점별 바다 방향(단위)
            public bool closed;
            public float length;
        }

        /// <summary>
        /// 선분 집합을 폴리라인 조각으로 잇는다. 끝점 짝짓기는 좌표가 아니라 **지형 메시 변 식별자**로
        /// 한다(클래스 주석). 열린 조각을 먼저 처리하고 남은 것(닫힌 고리)을 훑는다.
        /// </summary>
        private static List<ShoreChain> AssembleChains(ShoreContour contour)
        {
            int n = contour.count;
            var nbrA = new int[n];
            var nbrB = new int[n];
            for (int i = 0; i < n; i++)
            {
                nbrA[i] = -1;
                nbrB[i] = -1;
            }

            // 변 키 → 아직 짝을 못 찾은 끝점(세그먼트 << 1 | 끝번호). 짝이 맞으면 사전에서 뺀다.
            // 셋 이상이 같은 키를 공유하는 경우(정점 y가 정확히 0인 퇴화)에도 앞의 둘만 이어지고
            // 나머지는 새 조각의 시작이 된다 - 무한 루프가 생길 수 없는 구조다.
            var pending = new Dictionary<long, int>(n * 2);
            for (int i = 0; i < n; i++)
            {
                LinkEnd(pending, nbrA, nbrB, contour.keyA[i], i, 0);
                LinkEnd(pending, nbrA, nbrB, contour.keyB[i], i, 1);
            }

            var visited = new bool[n];
            var chains = new List<ShoreChain>();

            // (1) 열린 조각: 한쪽 끝이 비어 있는 세그먼트에서 출발한다.
            for (int i = 0; i < n; i++)
            {
                if (visited[i])
                    continue;
                if (nbrA[i] < 0)
                    AddChain(chains, WalkChain(contour, nbrA, nbrB, visited, i, 0));
                else if (nbrB[i] < 0)
                    AddChain(chains, WalkChain(contour, nbrA, nbrB, visited, i, 1));
            }

            // (2) 남은 것은 전부 닫힌 고리다.
            for (int i = 0; i < n; i++)
            {
                if (visited[i])
                    continue;
                AddChain(chains, WalkChain(contour, nbrA, nbrB, visited, i, 0));
            }

            return chains;
        }

        private static void LinkEnd(Dictionary<long, int> pending, int[] nbrA, int[] nbrB,
            long key, int segment, int end)
        {
            if (pending.TryGetValue(key, out int packed))
            {
                pending.Remove(key);
                int other = packed >> 1;
                int otherEnd = packed & 1;
                if (other == segment)
                    return; // 같은 세그먼트의 두 끝이 같은 키 - 퇴화. 잇지 않는다.
                SetNeighbour(nbrA, nbrB, segment, end, other);
                SetNeighbour(nbrA, nbrB, other, otherEnd, segment);
                return;
            }

            pending[key] = (segment << 1) | end;
        }

        private static void SetNeighbour(int[] nbrA, int[] nbrB, int segment, int end, int other)
        {
            if (end == 0)
                nbrA[segment] = other;
            else
                nbrB[segment] = other;
        }

        private static void AddChain(List<ShoreChain> chains, ShoreChain chain)
        {
            if (chain != null && chain.length >= MinChainLength && chain.points.Count >= 2)
                chains.Add(chain);
        }

        /// <summary>
        /// startSegment의 startEnd 쪽 끝에서 출발해 이웃을 따라가며 폴리라인을 만든다.
        /// 꼭짓점별 바다 방향은 그 점을 공유하는 앞뒤 삼각형의 내리막 방향 평균이다.
        /// 마지막에 폴리라인 전체의 감김을 확인해, 필요하면 뒤집어서 **항상 "진행 방향의 오른쪽이
        /// 바다"** 가 되게 맞춘다(리본 삼각형 감김이 한 방향으로 고정되는 근거).
        /// </summary>
        private static ShoreChain WalkChain(ShoreContour contour, int[] nbrA, int[] nbrB, bool[] visited,
            int startSegment, int startEnd)
        {
            var chain = new ShoreChain();
            var chainSegments = new List<int>();

            int current = startSegment;
            int inEnd = startEnd;
            int guard = 0;

            while (true)
            {
                visited[current] = true;
                chainSegments.Add(current);

                Vector2 head = EndPoint(contour, current, inEnd);
                Vector2 tail = EndPoint(contour, current, 1 - inEnd);
                if (chain.points.Count == 0)
                    chain.points.Add(head);
                chain.points.Add(tail);

                int next = (1 - inEnd) == 0 ? nbrA[current] : nbrB[current];
                if (next < 0)
                    break;
                if (visited[next])
                {
                    // 출발점으로 되돌아왔다 = 닫힌 고리. 마지막 점을 시작점에 정확히 맞춘다.
                    if (next == startSegment)
                    {
                        chain.closed = true;
                        chain.points[chain.points.Count - 1] = chain.points[0];
                    }
                    break;
                }

                inEnd = nbrA[next] == current ? 0 : 1;
                current = next;

                if (++guard > contour.count)
                    break; // 방어(위상이 깨진 입력)
            }

            // 꼭짓점별 바다 방향: 그 점에 닿은 세그먼트들의 내리막 방향 평균.
            int pointCount = chain.points.Count;
            for (int p = 0; p < pointCount; p++)
            {
                Vector2 sum = Vector2.zero;
                if (p - 1 >= 0 && p - 1 < chainSegments.Count)
                    sum += Down(contour, chainSegments[p - 1]);
                if (p < chainSegments.Count)
                    sum += Down(contour, chainSegments[p]);
                if (chain.closed)
                {
                    if (p == 0)
                        sum += Down(contour, chainSegments[chainSegments.Count - 1]);
                    else if (p == pointCount - 1)
                        sum += Down(contour, chainSegments[0]);
                }

                chain.seaward.Add(sum.sqrMagnitude > 1e-8f ? sum.normalized : Vector2.zero);
            }

            // 길이 + 감김 확인. orientation은 Σ 길이 × dot(오른쪽 법선, 바다 방향)이다.
            float length = 0f;
            float orientation = 0f;
            for (int p = 0; p + 1 < pointCount; p++)
            {
                Vector2 edge = chain.points[p + 1] - chain.points[p];
                float segLength = edge.magnitude;
                length += segLength;
                if (segLength < 1e-6f)
                    continue;

                Vector2 tangent = edge / segLength;
                Vector2 right = new Vector2(tangent.y, -tangent.x); // cross(T, right) = +Y
                Vector2 hint = chain.seaward[p] + chain.seaward[p + 1];
                orientation += segLength * Vector2.Dot(right, hint);
            }

            chain.length = length;

            if (orientation < 0f)
            {
                chain.points.Reverse();
                chain.seaward.Reverse();
            }

            return chain;
        }

        private static Vector2 EndPoint(ShoreContour contour, int segment, int end)
        {
            return end == 0
                ? new Vector2(contour.ax[segment], contour.az[segment])
                : new Vector2(contour.bx[segment], contour.bz[segment]);
        }

        private static Vector2 Down(ShoreContour contour, int segment)
        {
            return new Vector2(contour.downX[segment], contour.downZ[segment]);
        }

        // ═══════════════════════════════════════════════════════════════════════════
        //  리본 메시
        // ═══════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// 조각들을 정거장 간격으로 재표본해 격자 띠를 만든다. 정점 상한(3000)을 넘길 것 같으면
        /// **간격을 늘려**(폴리라인 단순화) 맞춘다 - 조각을 버리는 것보다 형태 손실이 작다.
        /// 그래도 남는 예산이 없으면 짧은 조각부터 버린다(긴 조각 우선).
        /// </summary>
        private static Mesh BuildRibbonMesh(List<ShoreChain> chains, out int vertexCount, out float contourLength)
        {
            vertexCount = 0;
            contourLength = 0f;

            float totalLength = 0f;
            for (int i = 0; i < chains.Count; i++)
                totalLength += chains[i].length;
            if (totalLength <= 0f)
                return null;

            // 조각 하나가 쓰는 정거장 수 ≈ length/spacing + 1. 총합이 상한을 넘지 않는 간격을 역산한다.
            float budget = Mathf.Max(1f, MaxStationsPerIsland - chains.Count);
            float spacing = Mathf.Max(BaseStationSpacing, totalLength / budget);

            // 긴 조각이 먼저 예산을 가져가게 정렬한다(같은 길이면 원래 순서 - 삽입 정렬이라 안정적).
            for (int i = 1; i < chains.Count; i++)
            {
                ShoreChain key = chains[i];
                int j = i - 1;
                while (j >= 0 && chains[j].length < key.length)
                {
                    chains[j + 1] = chains[j];
                    j--;
                }
                chains[j + 1] = key;
            }

            var vertices = new List<Vector3>(MaxVerticesPerIsland);
            var normals = new List<Vector3>(MaxVerticesPerIsland);
            var uv0 = new List<Vector2>(MaxVerticesPerIsland);
            var uv1 = new List<Vector2>(MaxVerticesPerIsland);
            var uv2 = new List<Vector2>(MaxVerticesPerIsland);
            var triangles = new List<int>(MaxVerticesPerIsland * 6);

            var stationPos = new List<Vector2>();
            var stationSea = new List<Vector2>();
            var stationU = new List<float>();
            var stationNormal = new List<Vector2>();
            var stationWidth = new List<float>();

            int stationsUsed = 0;

            for (int c = 0; c < chains.Count; c++)
            {
                ShoreChain chain = chains[c];
                int stationCount = Mathf.Max(2, Mathf.FloorToInt(chain.length / spacing) + 1);
                if (stationsUsed + stationCount > MaxStationsPerIsland)
                    continue; // 남은 예산으로 감당 못 하는 조각은 버린다(짧은 것부터 걸린다)

                stationPos.Clear();
                stationSea.Clear();
                stationU.Clear();
                Resample(chain, stationCount, stationPos, stationSea, stationU);
                if (stationPos.Count < 2)
                    continue;

                stationsUsed += stationPos.Count;
                contourLength += chain.length;

                int baseIndex = vertices.Count;
                int stations = stationPos.Count;

                // 정거장별 바다 방향과 **안전 폭**을 먼저 확정한다. 굽이가 급한 곳(곡률 반지름이
                // 리본 폭보다 작은 곶/후미)에서 바깥 줄이 서로를 넘어가면 삼각형이 뒤집혀 아래를
                // 보고 컬링된다 - 그 지점만 띠를 좁혀 접히지 않게 한다(SafeWidth).
                stationNormal.Clear();
                stationWidth.Clear();
                for (int i = 0; i < stations; i++)
                    stationNormal.Add(NormalAt(stationPos, stationSea, chain.closed, i));
                for (int i = 0; i < stations; i++)
                    stationWidth.Add(SafeWidth(stationPos, chain.closed, i));
                RelaxFolds(stationPos, stationNormal, stationWidth);

                for (int i = 0; i < stations; i++)
                {
                    Vector2 p = stationPos[i];
                    Vector2 sea = stationNormal[i];
                    float width = stationWidth[i];
                    float u = stationU[i];

                    for (int j = 0; j < RowCount; j++)
                    {
                        float v = RowProgress[j];
                        float offset = v * width;
                        vertices.Add(new Vector3(p.x + sea.x * offset, RibbonLift, p.y + sea.y * offset));
                        normals.Add(Vector3.up);
                        uv0.Add(new Vector2(u, v));
                        // x: 부호 있는 물가 거리(바다 쪽이라 음수 - 지형 UV2와 같은 부호 규약)
                        // y: **그 정거장의** 리본 폭(m). 셰이더가 v ↔ 미터 환산을 상수로 가정하면
                        //    좁혀진 정거장에서 노멀 유한차분이 어긋나므로 정점마다 실어 보낸다.
                        uv1.Add(new Vector2(-offset, width));
                        uv2.Add(sea);
                    }
                }

                // 감김은 IslandMeshGenerator/SeabedGenerator의 링-링 루프(a,b,d)(a,d,c)와 같은 순서다.
                // a = (정거장 i, 줄 j), b = (i+1, j), c = (i, j+1), d = (i+1, j+1).
                // WalkChain이 "진행 방향의 오른쪽이 바다"로 감김을 맞춰 두므로 정거장 증가 =
                // 원판의 seg 증가, 줄 증가 = 링 증가와 정확히 대응한다 → 법선이 위를 향한다.
                for (int i = 0; i + 1 < stations; i++)
                {
                    for (int j = 0; j + 1 < RowCount; j++)
                    {
                        int a = baseIndex + i * RowCount + j;
                        int b = baseIndex + (i + 1) * RowCount + j;
                        int cc = a + 1;
                        int d = b + 1;

                        triangles.Add(a);
                        triangles.Add(b);
                        triangles.Add(d);

                        triangles.Add(a);
                        triangles.Add(d);
                        triangles.Add(cc);
                    }
                }
            }

            if (vertices.Count == 0 || triangles.Count == 0)
                return null;

            var mesh = new Mesh { name = "ShoreBreakRibbon" };
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uv0);
            mesh.SetUVs(1, uv1);
            mesh.SetUVs(2, uv2);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();

            // 정점 변위(셰이더 _CrestMaxHeight 상한 4.0m + 물가 쪽 말림)를 담을 여유를 바운즈에
            // 더한다. 그러지 않으면 마루가 솟은 순간에도 평평한 띠 기준으로 컬링돼 화면 가장자리에서
            // 리본이 통째로 사라진다(바운즈는 컬링에만 쓰이므로 넉넉해도 비용이 없다).
            Bounds bounds = mesh.bounds;
            bounds.Expand(new Vector3(4f, 10f, 4f));
            mesh.bounds = bounds;

            vertexCount = vertices.Count;
            return mesh;
        }

        /// <summary>폴리라인을 등간격 정거장으로 재표본한다(위치·바다 방향·연안 누적거리 u).</summary>
        private static void Resample(ShoreChain chain, int stationCount,
            List<Vector2> outPos, List<Vector2> outSea, List<float> outU)
        {
            float step = chain.length / (stationCount - 1);
            int cursor = 0;
            float consumed = 0f; // cursor 세그먼트 시작점까지의 누적 길이

            for (int k = 0; k < stationCount; k++)
            {
                float target = k * step;

                while (cursor + 1 < chain.points.Count - 1)
                {
                    float segLength = (chain.points[cursor + 1] - chain.points[cursor]).magnitude;
                    // 길이 0인 마디(정점 y가 정확히 0이면 같은 점이 두 번 나온다)는 건너뛴다.
                    // 여기서 break하면 그 뒤 정거장이 전부 같은 자리에 겹친다.
                    if (segLength > 1e-6f && consumed + segLength >= target)
                        break;
                    consumed += segLength;
                    cursor++;
                }

                Vector2 p0 = chain.points[cursor];
                Vector2 p1 = chain.points[cursor + 1];
                float length = (p1 - p0).magnitude;
                float t = length > 1e-6f ? Mathf.Clamp01((target - consumed) / length) : 0f;

                outPos.Add(Vector2.Lerp(p0, p1, t));
                Vector2 sea = Vector2.Lerp(chain.seaward[cursor], chain.seaward[cursor + 1], t);
                outSea.Add(sea.sqrMagnitude > 1e-8f ? sea.normalized : chain.seaward[cursor]);
                outU.Add(target);
            }

            // 닫힌 고리는 마지막 정거장을 시작점에 정확히 붙여 이음매를 없앤다(u는 둘레 그대로 둔다).
            if (chain.closed && outPos.Count >= 2)
            {
                outPos[outPos.Count - 1] = outPos[0];
                outSea[outSea.Count - 1] = outSea[0];
            }
        }

        /// <summary>
        /// 정거장의 바다 방향 = 접선의 **오른쪽 법선**(cross(T, N) = +Y). 부호를 여기서 다시 뒤집지
        /// 않는 것이 중요하다 - WalkChain이 조각 전체의 감김을 이미 "진행 방향의 오른쪽이 바다"로
        /// 맞춰 놓았고, 정거장 하나만 반대로 뒤집으면 그 칸의 삼각형이 아래를 보고 컬링된다.
        /// 접선이 퇴화한 경우(정거장이 겹친 경우)에만 등고선 내리막 힌트를 쓴다.
        /// </summary>
        private static Vector2 NormalAt(List<Vector2> pos, List<Vector2> hint, bool closed, int index)
        {
            int last = pos.Count - 1;
            Vector2 previous;
            Vector2 next;

            if (closed)
            {
                previous = index > 0 ? pos[index - 1] : pos[last - 1];
                next = index < last ? pos[index + 1] : pos[1];
            }
            else
            {
                previous = pos[Mathf.Max(index - 1, 0)];
                next = pos[Mathf.Min(index + 1, last)];
            }

            Vector2 tangent = next - previous;
            if (tangent.sqrMagnitude < 1e-10f)
                return hint[index].sqrMagnitude > 1e-8f ? hint[index].normalized : Vector2.right;

            tangent.Normalize();
            return new Vector2(tangent.y, -tangent.x);
        }

        /// <summary>
        /// 그 정거장에서 띠를 얼마나 넓게 뽑아도 되는가(m). 폴리라인을 w만큼 옆으로 밀어낸 곡선은
        /// **오목한 쪽의 곡률 반지름보다 w가 크면 스스로를 관통한다**(offset curve의 고전적 성질).
        /// 관통하면 그 칸의 삼각형이 뒤집혀 아래를 보고 컬링되므로, 굽이가 급한 지점만 폭을 줄인다.
        ///
        /// 바다 방향은 진행 방향의 오른쪽이므로(NormalAt), **오른쪽으로 꺾이는 굽이**(2D 외적 &lt; 0)만
        /// 위험하다 - 왼쪽으로 꺾이면 바깥 줄은 벌어질 뿐이다. 곡률 반지름은 (평균 마디 길이 / 꺾인
        /// 각도)로 근사하고 0.85배 여유를 둔다. 실측(프로파일 8종 · 섬 7개)에서 이 규칙을 넣기 전
        /// 초승달 섬 R=200에 뒤집힌 칸이 2개 있었고, 넣은 뒤 전 섬에서 0개다. 폭이 줄어드는 정거장은
        /// 전체의 0.2~4%뿐이라 띠 모양은 사실상 그대로다.
        /// </summary>
        private static float SafeWidth(List<Vector2> pos, bool closed, int index)
        {
            int last = pos.Count - 1;
            Vector2 previous;
            Vector2 next;

            if (closed)
            {
                previous = index > 0 ? pos[index - 1] : pos[last - 1];
                next = index < last ? pos[index + 1] : pos[1];
            }
            else
            {
                if (index == 0 || index == last)
                    return RibbonWidth; // 열린 조각의 끝은 이웃이 한쪽뿐이라 접힐 수 없다
                previous = pos[index - 1];
                next = pos[index + 1];
            }

            Vector2 incoming = pos[index] - previous;
            Vector2 outgoing = next - pos[index];
            float inLength = incoming.magnitude;
            float outLength = outgoing.magnitude;
            if (inLength < 1e-5f || outLength < 1e-5f)
                return RibbonWidth;

            incoming /= inLength;
            outgoing /= outLength;

            float cross = incoming.x * outgoing.y - incoming.y * outgoing.x;
            if (cross >= 0f)
                return RibbonWidth; // 바다 반대쪽으로 꺾인다 - 안전

            float turn = Mathf.Abs(Mathf.Atan2(cross, Vector2.Dot(incoming, outgoing)));
            if (turn < 1e-4f)
                return RibbonWidth;

            float curvatureRadius = (inLength + outLength) * 0.5f / turn;
            return Mathf.Clamp(curvatureRadius * 0.85f, RibbonWidth * 0.2f, RibbonWidth);
        }

        /// <summary>
        /// SafeWidth의 곡률 근사가 놓친 접힘을 **실제 삼각형 부호로** 확인해 마저 없앤다.
        /// 곡률 근사는 정거장 간격으로 이산화된 값이라 아주 급한 굽이에서 반지름을 과대평가할 수 있다
        /// (실측: 초승달 섬 R=200에서 칸 하나가 남았다). 여기서는 각 마디의 사변형 두 삼각형이 정말
        /// 위를 보는지 직접 재고, 아니면 양 끝 폭을 20%씩 줄여 다시 잰다. 월드 생성 1회 비용이고
        /// (정거장 600 × 줄 5 × 최대 8회 = 2.4만 번의 곱셈), 수렴하지 않으면 하한에서 멈춘다.
        /// </summary>
        private static void RelaxFolds(List<Vector2> pos, List<Vector2> normal, List<float> width)
        {
            float floor = RibbonWidth * 0.12f;

            for (int pass = 0; pass < 8; pass++)
            {
                bool folded = false;
                for (int i = 0; i + 1 < pos.Count; i++)
                {
                    if (EdgeFaceUp(pos[i], normal[i], width[i], pos[i + 1], normal[i + 1], width[i + 1]))
                        continue;
                    if (width[i] <= floor && width[i + 1] <= floor)
                        continue; // 더 줄여도 소용없는 퇴화(정거장이 겹친 경우) - 그대로 둔다

                    folded = true;
                    width[i] = Mathf.Max(width[i] * 0.8f, floor);
                    width[i + 1] = Mathf.Max(width[i + 1] * 0.8f, floor);
                }

                if (!folded)
                    return;
            }
        }

        /// <summary>마디 하나의 사변형(줄 RowCount-1개 × 삼각형 2개)이 전부 위를 보는가.</summary>
        private static bool EdgeFaceUp(Vector2 p0, Vector2 n0, float w0, Vector2 p1, Vector2 n1, float w1)
        {
            for (int j = 0; j + 1 < RowCount; j++)
            {
                Vector2 a = p0 + n0 * (RowProgress[j] * w0);
                Vector2 b = p1 + n1 * (RowProgress[j] * w1);
                Vector2 c = p0 + n0 * (RowProgress[j + 1] * w0);
                Vector2 d = p1 + n1 * (RowProgress[j + 1] * w1);

                // 삼각형 (a, b, d) · (a, d, c) - 메시에 넣는 감김 그대로다.
                // 법선의 y성분 ∝ u.y·v.x − u.x·v.y (Vector2는 (x, z)를 담는다).
                if (UpSign(b - a, d - a) <= 0f || UpSign(d - a, c - a) <= 0f)
                    return false;
            }

            return true;
        }

        private static float UpSign(Vector2 u, Vector2 v)
        {
            return u.y * v.x - u.x * v.y;
        }

        /// <summary>
        /// 물보라 지점을 고른다. 가장 긴 조각에서 등간격으로 최대 3곳, 물가에서 바다쪽으로
        /// SprayAnchorSeaward(0.96m) 떨어진 자리(= 셰이더가 마루를 무너뜨리는 지점)다.
        /// </summary>
        private static void CollectSprayAnchors(List<ShoreChain> chains, Vector3 islandCenter, RibbonRecord record)
        {
            record.sprayAnchors = new Vector3[0];
            record.sprayForward = new Vector3[0];
            record.sprayRight = new Vector3[0];

            ShoreChain best = null;
            for (int i = 0; i < chains.Count; i++)
            {
                if (best == null || chains[i].length > best.length)
                    best = chains[i];
            }

            if (best == null || best.points.Count < 2)
                return;

            var anchors = new Vector3[SprayAnchorCount];
            var forwards = new Vector3[SprayAnchorCount];
            var rights = new Vector3[SprayAnchorCount];

            for (int k = 0; k < SprayAnchorCount; k++)
            {
                // 조각을 SprayAnchorCount + 1 등분한 지점(양 끝을 피한다).
                int index = Mathf.Clamp(
                    Mathf.RoundToInt((k + 1f) / (SprayAnchorCount + 1f) * (best.points.Count - 1)),
                    0, best.points.Count - 1);

                Vector2 p = best.points[index];
                Vector2 sea = best.seaward[index];
                if (sea.sqrMagnitude < 1e-8f)
                    sea = Vector2.right;
                sea = sea.normalized;

                Vector2 anchorXZ = p + sea * SprayAnchorSeaward;
                anchors[k] = islandCenter + new Vector3(anchorXZ.x, RibbonLift, anchorXZ.y);
                forwards[k] = new Vector3(sea.x, 0f, sea.y);
                rights[k] = new Vector3(-sea.y, 0f, sea.x);
            }

            record.sprayAnchors = anchors;
            record.sprayForward = forwards;
            record.sprayRight = rights;
        }

        // ═══════════════════════════════════════════════════════════════════════════
        //  에셋 프로브
        // ═══════════════════════════════════════════════════════════════════════════

        // [로드 규칙] Resources.Load는 정적 필드 초기자에서 부르지 않는다(Unity 6.5 - 초기자는 Load가
        // 막힌 시점에 돌 수 있다). 그리고 **실패를 영구히 캐시하지 않는다** - 거품 텍스처는 다른
        // 작업으로 나중에 들어올 수 있고, 그 null을 굳히면 텍스처가 와도 세션 내내 반영이 안 된다.
        // 프레임당 한 번만 다시 살핀다(IslandMeshGenerator.MeshLibrary의 프로브와 같은 규칙).

        /// <summary>
        /// 월드 전체가 공유하는 리본 머티리얼 1장. 셰이더가 없으면 null이고 리본은 생기지 않는다.
        /// </summary>
        private static Material GetRibbonMaterial()
        {
            if (ribbonMaterial == null)
            {
                if (shaderProbeFrame == Time.frameCount)
                    return null;
                shaderProbeFrame = Time.frameCount;

                Shader shader = Resources.Load<Shader>("Shaders/MGShoreBreak");
                if (shader == null)
                    return null;

                ribbonMaterial = new Material(shader) { name = "MGShoreBreakMaterial" };
                foamApplied = false;
            }

            ApplyFoamTexture();
            return ribbonMaterial;
        }

        /// <summary>
        /// 거품 텍스처를 붙인다(MGShoreline과 같은 파일·같은 채널 계약). 없으면 _FoamMap 기본값
        /// "black"이 남고 셰이더가 단색 폴백으로 간다 - 경고 없이 조용히.
        /// </summary>
        private static void ApplyFoamTexture()
        {
            if (foamApplied || ribbonMaterial == null)
                return;
            if (foamTexture == null)
            {
                if (foamProbeFrame == Time.frameCount)
                    return;
                foamProbeFrame = Time.frameCount;
                foamTexture = Resources.Load<Texture2D>("Textures/shore_foam");
                if (foamTexture == null)
                    return;
            }

            ribbonMaterial.SetTexture(FoamMapProperty, foamTexture);
            foamApplied = true;
        }

        // ═══════════════════════════════════════════════════════════════════════════
        //  위상 조회 (셰이더와 같은 식 - 스와시 ↔ 마루 연속성 검증/물보라 타이밍용)
        // ═══════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// MGShoreBreak/MGShoreline의 MGShoreWobble과 **같은 식**. 연안 방향으로 도달 시각을 흔들어
        /// 섬 전체가 한 순간에 똑같이 젖는 인공적인 동기화를 부순다(파장 63m/41m).
        /// </summary>
        public static float ShoreWobble(float worldX, float worldZ)
        {
            return 0.6f * Mathf.Sin(worldX * 0.099f + worldZ * 0.061f)
                 + 0.4f * Mathf.Sin(worldX * -0.043f + worldZ * 0.152f + 2.1f);
        }

        /// <summary>
        /// 시각 time에 (worldX, worldZ) 근방의 마루가 물가에서 **바다 쪽으로** 몇 m에 있는지.
        /// 0이면 지금 막 물가에 닿아 부서지는 순간이고, 그 직후 스와시가 물가에서 출발한다.
        /// 값의 범위는 0 ~ celerity × period(현재 13.5m)이며, 리본 폭(6m)을 넘는 동안에는
        /// 마루가 아직 리본 밖이라 화면에 아무것도 없다.
        /// </summary>
        public static float CrestSeawardDistance(float time, float worldX, float worldZ)
        {
            float period = Mathf.Max(ShorelineWaves.Period, 0.5f);
            float celerity = Mathf.Max(ShorelineWaves.FrontSpeed, 0.2f);
            float tLocal = time + ShoreWobble(worldX, worldZ) * period * AlongshoreWobble;
            float phase = tLocal / period;
            phase -= Mathf.Floor(phase);
            return (1f - phase) * period * celerity;
        }

        /// <summary>
        /// 같은 시각의 **스와시 전선**이 물가에서 내륙으로 몇 m 올라가 있는지(MGShoreline과 같은 식).
        /// CrestSeawardDistance가 celerity × period로 되감기는 바로 그 순간 이 값이 0에서 출발한다 -
        /// 두 전선이 물가에서 한 점으로 이어지는 것을 수치로 확인하는 창구다.
        /// </summary>
        public static float SwashInlandDistance(float time, float worldX, float worldZ)
        {
            float period = Mathf.Max(ShorelineWaves.Period, 0.5f);
            float celerity = Mathf.Max(ShorelineWaves.FrontSpeed, 0.2f);
            float tLocal = time + ShoreWobble(worldX, worldZ) * period * AlongshoreWobble;
            float phase = tLocal / period;
            phase -= Mathf.Floor(phase);
            return phase * period * celerity;
        }

        // ═══════════════════════════════════════════════════════════════════════════
        //  렌더 드라이버 (거리 컷 + 물보라)
        // ═══════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// 씬마다 자동 생성되는 드라이버. GrassFieldDriver/UnderwaterAmbience와 같은
        /// SubsystemRegistration + sceneLoaded 부트스트랩이다(씬 수정 없음, 재시작 안전).
        ///
        /// 하는 일은 둘뿐이다.
        ///   (1) 섬당 1회 거리 비교로 renderer.enabled 토글(먼 섬 렌더 생략).
        ///   (2) 가장 가까운 섬의 부서지는 지점 3곳에 주기당 한 번 물보라를 뿜는다.
        /// 셰이더 전역(_MG_ShoreTime 등)은 ShorelineWaves/OceanWaves가 이미 밀고 있으므로
        /// 여기서 다시 밀지 않는다 - 같은 전역을 두 곳이 쓰면 경합이 생긴다.
        /// </summary>
        private sealed class ShoreRibbonDriver : MonoBehaviour
        {
            private Camera targetCamera;
            private ParticleSystem spray;
            private bool sprayFailed;

            [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
            private static void Bootstrap()
            {
                SceneManager.sceneLoaded += (scene, mode) =>
                {
                    if (FindAnyObjectByType<ShoreRibbonDriver>() != null)
                        return;

                    var go = new GameObject("ShoreRibbonDriver");
                    go.AddComponent<ShoreRibbonDriver>();
                };
            }

            /// <summary>
            /// LateUpdate인 이유: 카메라 이동(Update)이 끝난 최종 위치로 거리 컷을 판단해야
            /// 리본이 한 프레임 늦게 켜지지 않는다(GrassFieldDriver와 같은 자리).
            /// </summary>
            private void LateUpdate()
            {
                if (registry.Count == 0)
                    return;

                if (targetCamera == null)
                {
                    targetCamera = Camera.main;
                    if (targetCamera == null)
                        return;
                }

                // 텍스처가 늦게 들어오는 경우를 위해 계속 살핀다(성공하면 foamApplied가 즉시 끊는다).
                ApplyFoamTexture();

                Vector3 camPos = targetCamera.transform.position;
                RibbonRecord nearest = null;
                float nearestDistance = float.MaxValue;

                for (int i = registry.Count - 1; i >= 0; i--)
                {
                    RibbonRecord record = registry[i];

                    // 월드 재생성으로 파괴된 섬의 레코드는 조회 시 버린다(SeabedGenerator와 같은 정리 규칙).
                    if (record.root == null || record.renderer == null)
                    {
                        registry.RemoveAt(i);
                        continue;
                    }

                    if (!record.root.gameObject.activeInHierarchy)
                        continue;

                    float edgeDistance = Vector3.Distance(camPos, record.center) - record.radius;
                    bool visible = edgeDistance <= MaxRenderDistance;
                    if (record.renderer.enabled != visible)
                        record.renderer.enabled = visible;

                    if (visible && edgeDistance < nearestDistance)
                    {
                        nearestDistance = edgeDistance;
                        nearest = record;
                    }
                }

                if (nearest != null && nearestDistance <= SprayDistance)
                    UpdateSpray(nearest);
            }

            /// <summary>
            /// 부서지는 지점 3곳에 **주기당 한 번**만 얇게 뿜는다. 파도 위상은 셰이더와 같은 식으로
            /// 계산하므로(CrestSeawardDistance와 단일 소스인 ShoreWobble 사용) 마루가 실제로
            /// 무너지는 순간과 어긋나지 않는다. 프레임당 할당 0(EmitParams는 구조체다).
            /// </summary>
            private void UpdateSpray(RibbonRecord record)
            {
                if (sprayFailed || record.sprayAnchors == null || record.sprayAnchors.Length == 0)
                    return;

                if (spray == null && !EnsureSpray())
                    return;

                float period = Mathf.Max(ShorelineWaves.Period, 0.5f);
                float celerity = Mathf.Max(ShorelineWaves.FrontSpeed, 0.2f);
                float now = Time.time;

                for (int k = 0; k < record.sprayAnchors.Length; k++)
                {
                    Vector3 anchor = record.sprayAnchors[k];
                    // 셰이더의 tLocal = _MG_ShoreTime + s/celerity + wobble·period·계수 와 같은 식.
                    float tLocal = now + SprayAnchorSeaward / celerity
                        + ShoreWobble(anchor.x, anchor.z) * period * AlongshoreWobble;
                    int cycle = Mathf.FloorToInt(tLocal / period);
                    if (cycle == record.sprayCycle[k])
                        continue;

                    record.sprayCycle[k] = cycle;

                    // 거칠수록 세게 튄다. 잔잔하면 아예 생략한다(잔잔한 날엔 마루도 거의 평평하다).
                    float rough = Mathf.Clamp01(OceanWaves.Roughness01);
                    if (rough < 0.12f)
                        continue;

                    float speed = SpraySpeed * Mathf.Lerp(0.6f, 1.35f, rough);
                    Vector3 forward = record.sprayForward[k];
                    Vector3 right = record.sprayRight[k];

                    for (int q = 0; q < SprayPerBurst; q++)
                    {
                        Vector3 local = SprayBurstDirections[q];
                        var emit = new ParticleSystem.EmitParams
                        {
                            position = anchor,
                            velocity = (right * local.x + Vector3.up * local.y + forward * local.z) * speed,
                        };
                        spray.Emit(emit, 1);
                    }
                }
            }

            /// <summary>
            /// 물보라 파티클 시스템 1개(월드 전체 공유)를 만든다. 머티리얼은 EffectBuilder의 공유
            /// 파티클 머티리얼이라 머티리얼/드로우콜이 늘지 않는다(다른 이펙트와 같은 배치에 들어간다).
            /// </summary>
            private bool EnsureSpray()
            {
                var go = new GameObject("ShoreSprayFX");
                go.transform.SetParent(transform, false);

                var ps = go.AddComponent<ParticleSystem>();
                if (ps == null)
                {
                    sprayFailed = true;
                    return false;
                }

                var main = ps.main;
                main.playOnAwake = false;
                // [함정] AddComponent<ParticleSystem>() 직후의 시스템은 이미 재생 중이라,
                // 재생 상태에서 main.duration을 대입하면 Unity가 에러를 던진다("Setting the
                // duration while system is still playing is not supported"). 반드시 먼저 완전 정지
                // 시킨다(UnderwaterVisuals.BuildMarineSnow와 같은 순서 - 실사고 0.2.43).
                ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                main.loop = true;
                main.duration = 5f;
                main.simulationSpace = ParticleSystemSimulationSpace.World; // 월드 좌표로 뿜는다
                main.startLifetime = new ParticleSystem.MinMaxCurve(0.55f, 1.05f);
                main.startSize = new ParticleSystem.MinMaxCurve(0.18f, 0.42f);
                main.startSpeed = new ParticleSystem.MinMaxCurve(0f); // 속도는 EmitParams가 준다
                main.startColor = new ParticleSystem.MinMaxGradient(
                    new Color(0.94f, 0.97f, 0.98f, 0.55f),
                    new Color(0.86f, 0.94f, 0.96f, 0.35f));
                main.gravityModifier = new ParticleSystem.MinMaxCurve(0.85f);
                main.maxParticles = SprayAnchorCount * SprayPerBurst * 4;

                var emission = ps.emission;
                emission.enabled = false; // 사출은 전부 Emit() 호출로만 한다

                var shape = ps.shape;
                shape.enabled = false;

                var colorOverLifetime = ps.colorOverLifetime;
                colorOverLifetime.enabled = true;
                var gradient = new Gradient();
                gradient.SetKeys(
                    new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                    new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.85f, 0.35f), new GradientAlphaKey(0f, 1f) });
                colorOverLifetime.color = new ParticleSystem.MinMaxGradient(gradient);

                var renderer = ps.GetComponent<ParticleSystemRenderer>();
                if (renderer != null)
                {
                    renderer.sharedMaterial = EffectBuilder.GetParticleMaterial();
                    renderer.renderMode = ParticleSystemRenderMode.Billboard;
                    renderer.alignment = ParticleSystemRenderSpace.View;
                    renderer.shadowCastingMode = ShadowCastingMode.Off;
                    renderer.receiveShadows = false;
                }

                ps.Play();
                spray = ps;
                return true;
            }
        }

        /// <summary>
        /// 정점/둘레 총합을 프레임당 1줄로 남기는 일회용 로거(SeabedGenerator.SeabedBatchLogger와
        /// 같은 방식 - 첫 리본에 붙여 두면 그 프레임의 섬 생성 루프가 끝난 뒤 Start가 돈다).
        /// </summary>
        private sealed class RibbonBatchLogger : MonoBehaviour
        {
            private void Start()
            {
                if (pendingRibbonCount > 0)
                {
                    Debug.Log($"[ShorelineRibbon] 리본 {pendingRibbonCount}개 / 정점 {pendingVertexTotal}개 / " +
                              $"해안선 총 길이 {pendingLengthTotal:F0}m");
                }

                pendingVertexTotal = 0;
                pendingRibbonCount = 0;
                pendingLengthTotal = 0f;
                loggerScheduled = false;
                Destroy(this);
            }
        }
    }
}
