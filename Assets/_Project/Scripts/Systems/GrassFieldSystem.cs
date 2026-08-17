using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using MakeGame.Player;

namespace MakeGame.Systems
{
    /// <summary>
    /// 초지(내륙 풀밭) 잔디 필드 v2. 섬 지형 위에 수만 개의 잔디 카드를 GPU 인스턴싱으로 깐다.
    ///
    /// ── 구조 (SeabedGenerator의 "생성 훅 + 정적 레지스트리" 패턴을 그대로 따른다) ──
    ///  · 배치: 섬 메시 생성 직후 IslandMeshGenerator.BuildGroundCaps가 Build()를 1회 호출한다
    ///    (SeabedGenerator.Build 호출 바로 다음 줄 - 같은 동기 흐름). 섬별 인스턴스 행렬 배열을
    ///    한 번 구워 레지스트리에 담아 두고, 이후에는 절대 재계산하지 않는다.
    ///  · 렌더: 씬마다 자동 생성되는 GrassFieldDriver(UnderwaterAmbience/AtmospherePostFX와 같은
    ///    RuntimeInitializeOnLoadMethod + sceneLoaded 부트스트랩)가 LateUpdate에서
    ///    Graphics.RenderMeshInstanced로 그린다. GameObject/MeshRenderer가 인스턴스마다 생기지
    ///    않으므로 씬 오브젝트 수는 드라이버 1개뿐이다.
    ///
    /// ── v2: 알파 컷아웃 카드 텍스처 + 군락감 + 꽃 무리 ──
    ///  · 텍스처: Resources.Load&lt;Texture2D&gt;("Textures/grass_card") - 2×2 아틀라스
    ///    ((0,0) 촘촘한 초록 / (1,0) 성긴+이삭 / (0,1) 마른 풀 / (1,1) 분홍 꽃 스파이크).
    ///    카드 한 장에 풀잎 수십 가닥이 그려져 있어 같은 인스턴스 수로 밀도감이 크게 오른다.
    ///  · 머티리얼 2장(같은 MG/Grass 셰이더): 잔디(_CellOverride -1 = 원점 해시로 잔디 3셀 택1,
    ///    틴트 온) + 꽃(_CellOverride 3 = (1,1) 고정, _TintStrength 0 = 텍스처 원색).
    ///  · 폴백: 텍스처가 null이면 v1 방식으로 돌아간다 - 좁은 blade 메시 + 틴트 그라데이션만
    ///    (셰이더 _BaseMap 기본값 white가 곧 v1 렌더), 꽃 배치는 생략. 셰이더 자체가 없으면
    ///    기존 계약대로 잔디 전체 생략(폴백 렌더 없음 - 조용히 무동작, 행렬 메모리도 안 쓴다).
    ///  · 군락감(패치니스): 채택 확률에 파장 ~7m 저주파 격자 해시 노이즈(0.35~1.0 배율)를 곱해
    ///    잔디가 균일 카펫이 아니라 무성한 곳/성긴 곳으로 갈린다. 1차 통과가 배율 합계를 세므로
    ///    총 개수는 여전히 목표치 근처다.
    ///  · 꽃 무리: 파장 ~11m의 별도 해시 노이즈가 문턱(상위 ~12%)을 넘는 지대에서만 잔디의
    ///    8~15%를 꽃 배치로 전환 - 꽃이 드문드문 '무리 지어' 핀다. 꽃 인스턴스는 별도 행렬
    ///    리스트로 모아 꽃 머티리얼 배치로 렌더한다(LOD A/B 분할은 잔디와 동일 규칙).
    ///
    /// ── 셰이더 계약 (같은 웨이브의 MG/Grass, Resources/Shaders/MGGrass) ──
    ///  · _MG_WindTime(Time.time) / _MG_PlayerPos(플레이어 월드 위치)를 매 프레임 주입한다
    ///    (잔디 머티리얼 - 꽃 머티리얼에도 같이 넣는다). 바람/밟힘 애니메이션은 전부 셰이더
    ///    정점 단계 - C#은 행렬을 다시 만지지 않는다.
    ///  · 카드 메시는 계약 규격(교차 쿼드 2장, 폭 0.55m·높이 0.65m·피벗 밑동·UV.y 0뿌리~1끝)대로
    ///    이 클래스가 코드로 생성한다(텍스처 폴백 시에는 v1 blade 규격 0.14m×1m).
    ///    Cull Off 셰이더라 단면 지오메트리면 충분하다.
    ///
    /// ── 초지 판정 (IslandMeshGenerator의 실제 색 경계 규칙 재사용) ──
    /// B47부터 지면 캡 경계는 반경이 아니라 **해수면 기준 높이**다: DryTop(기준 높이 + BandWobble(angle)
    /// ± 디더 0.18m) 위가 지형 본체 = Meadow Green 초원, 아래는 모래 캡 3단이다(BuildGroundCaps).
    /// [B52] 그 기준 높이는 전 섬 공통 1.30m가 아니라 **섬별 grassLine**(1.15~3.70m,
    /// IslandMeshGenerator.GrassLineHeight - 캡의 DryTop과 단일 소스)이다. 잔디는
    /// y ≥ grassLine + BandWobble(angle) + 0.18(디더 반폭)만 채택해 **어떤 디더 값에서도 모래
    /// 삼각형 위에 서지 않는다.** BandWobble의 phaseA/phaseB는 훅에서 그대로 넘겨받으므로 경계가
    /// 실제 모래 경계와 정확히 같은 위상으로 출렁인다. 수면 근처는 이 높이 조건이 자동으로 배제하고
    /// (해수면 0m ≪ 1.33m+), 바위 절벽 지대(P7 메사 등)는 경사 30도 초과 제외가 걸러낸다.
    ///
    /// ── [B52] 섬별 잔디량 스펙트럼 ("잔디가 너무 많다" 대응) ──
    /// 목표 개수 = 기존 목표 × 전역 0.65 × 섬별 lerp(1.0, 0.45, t). t는 grassLine과 **같은**
    /// 섬 해시(IslandMeshGenerator.ComputeGrassLineT)라, 경계선이 높은 모래 섬일수록 남은 초지의
    /// 잔디도 성기다. t가 균등 분포이므로 섬 평균 밀도 계수는 0.65 × 0.725 ≈ 0.47 - 경계 상승으로
    /// 초지 면적 자체도 줄어 체감 잔디량은 현재 대비 약 45~50% 이하다. 꽃 무리·패치니스·LOD
    /// 로직은 전부 불변이고 모수(목표 개수)만 줄어든다.
    ///
    /// ── 높이 샘플: 레이캐스트 0회 ──
    /// 섬 메시는 런타임 생성이라 readable이고, 정점 배열이 결정적 극좌표 격자다
    /// (index 0 = 중심, ring k(1..ringCount)의 seg s = 1+(k-1)*segments+s, 각도 = s/segments·2π,
    /// 반경 = k/ringCount·R - IslandMeshGenerator.GenerateIslandMesh). 그 격자를 (ring, seg) 이중
    /// 선형 보간으로 직접 읽는다. 후보 수만 개 × 섬 50개를 Physics.Raycast로 하면 수백만 캐스트라
    /// 애초에 성립하지 않는 비용이고, 정점 보간은 곱셈 몇 번이다.
    ///
    /// ── rng 불변 (SeabedGenerator와 같은 근거) ──
    /// System.Random/UnityEngine.Random을 만들지도 소비하지도 않는다. 지터·선별·회전·스케일 변주·
    /// 패치 노이즈·꽃 전환까지 전부 (격자 정수 좌표, 섬 월드 위치 유래 salt)만 입력으로 받는 순수
    /// 해시다(IslandMeshGenerator.ComputeNoiseSeed / SeabedGenerator.LatticeHash01과 같은 finalizer
    /// 계열). 따라서 자원/위험요소/초목의 추첨 순서는 한 칸도 밀리지 않는다.
    ///
    /// ── 성능 가드 ──
    ///  · 행렬 배열은 섬당 1회 생성 후 재사용(프레임당 할당 0). RenderParams는 스택 구조체다.
    ///  · 간이 LOD: 배치 시 위치 해시로 A/B 두 그룹(각 절반)으로 미리 갈라 두고, 카메라-섬 테두리
    ///    거리 60m 이내면 A+B(전체), 60~300m면 A만(절반 밀도), 300m 밖이면 스킵한다. 꽃도 같은
    ///    규칙으로 A/B를 가른다. 프레임당 인스턴스 단위 재계산은 없다 - 거리 비교는 섬당 1회다.
    ///  · 비활성 섬(RegenerateWorld의 SetActive(false) → Destroy 흐름)은 그리지 않고, 파괴된 섬의
    ///    레코드는 조회 시 걸러 제거한다(SeabedGenerator.TrySampleSeabed와 같은 정리 규칙).
    /// </summary>
    public static class GrassFieldSystem
    {
        // ── 밀도/배치 상수 ─────────────────────────────────────────────────────────

        /// <summary>전체 잔디 밀도 배율. 성능 튜닝은 이 값 하나로 한다(0.5 = 절반, 0 = 잔디 끔).</summary>
        public const float DensityMultiplier = 1f;

        /// <summary>후보 격자 간격(m). 지터가 ±0.45×이 값이라 격자무늬가 눈에 남지 않는다.</summary>
        private const float CellSpacing = 0.55f;

        /// <summary>
        /// 초지 경계 디더 반폭(m) = BuildGroundCaps heightDither 0.36의 절반. [B52] 기준 높이 자체는
        /// 상수 1.30이 아니라 섬별 grassLine(IslandMeshGenerator.GrassLineHeight - 캡과 단일 소스)이고,
        /// Build가 "grassLine + 이 반폭"을 계산해 후보 판정에 넘긴다. 여기에 BandWobble(angle)이
        /// 더해져 실제 모래 경계와 같은 위상으로 출렁이는 것은 B47 그대로다.
        /// </summary>
        private const float GrassBoundaryDitherHalf = 0.18f;

        /// <summary>[B52] 전역 잔디 감소 계수. 전 섬 공통으로 목표 개수에 곱한다.</summary>
        private const float GlobalDensityScale = 0.65f;

        /// <summary>[B52] 섬별 밀도 계수 하한. t=0 섬은 1.0, t=1 섬(모래 섬)은 이 값까지 성기다.</summary>
        private const float IslandDensityMin = 0.45f;

        /// <summary>경사 상한 tan(30°). 정점 격자 기울기가 이보다 크면 바위 절벽 지대로 보고 제외한다.</summary>
        private const float MaxSlopeTan = 0.57735f;

        /// <summary>경사 측정용 유한 차분 간격(m). 메시 삼각형(2~5m)보다 작아 국소 경사를 잡는다.</summary>
        private const float SlopeProbeStep = 0.6f;

        /// <summary>카드 밑동을 지면에 살짝 심는 깊이(m). 경사면에서 밑동이 뜨는 것을 가린다.</summary>
        private const float RootSinkDepth = 0.04f;

        // ── [v3] 이봉(bimodal) 스케일 변주 상수 (다발 tuft - 순수 위치 해시, rng 소비 0) ──
        // 실제 초지는 낮은 하층 풀 사이로 솟은 다발이 드문드문 섞인다. 위치 해시로 80%는
        // 짧은 하층(0.5~0.9), 20%는 솟은 다발(1.0~1.5, xz도 1.0~1.25로 살짝 넓게)로 가른다.
        // 배치 개수·판정·LOD 로직은 불변 - 행렬 스케일 계산만 이 상수를 쓴다.

        /// <summary>솟은 다발(tuft)로 뽑히는 비율.</summary>
        private const float TuftFraction = 0.2f;

        /// <summary>하층 짧은 풀의 높이 스케일 범위.</summary>
        private const float UnderScaleYMin = 0.5f;
        private const float UnderScaleYMax = 0.9f;

        /// <summary>하층 짧은 풀의 폭(xz) 스케일 범위(v2와 동일).</summary>
        private const float UnderScaleXZMin = 0.8f;
        private const float UnderScaleXZMax = 1.1f;

        /// <summary>솟은 다발의 높이 스케일 범위.</summary>
        private const float TuftScaleYMin = 1.0f;
        private const float TuftScaleYMax = 1.5f;

        /// <summary>솟은 다발의 폭(xz) 스케일 범위 - 키만 크면 빗자루 같아서 살짝 넓게.</summary>
        private const float TuftScaleXZMin = 1.0f;
        private const float TuftScaleXZMax = 1.25f;

        // ── 군락(패치니스)/꽃 무리 상수 (전부 순수 해시 노이즈 - rng 소비 0) ────────────

        /// <summary>패치 노이즈 파장(m). 이 스케일로 무성한 곳/성긴 곳이 갈린다.</summary>
        private const float PatchWavelength = 7f;

        /// <summary>패치 노이즈 최저 배율. 성긴 곳도 완전히 비지는 않는다(0.35~1.0).</summary>
        private const float PatchFloor = 0.35f;

        /// <summary>꽃 무리 노이즈 파장(m). 패치보다 넓어 꽃밭이 더 큰 덩어리로 뭉친다.</summary>
        private const float FlowerWavelength = 11f;

        /// <summary>
        /// 꽃 지대 문턱. 보간된 격자 해시 노이즈는 0.5 근처로 몰리므로 0.76 초과는 면적 기준
        /// 상위 ~12% - 레퍼런스처럼 꽃이 드문드문 무리 지어 핀다.
        /// </summary>
        private const float FlowerZoneThreshold = 0.76f;

        /// <summary>꽃 지대 안에서 잔디→꽃 전환 비율의 하한/상한(문턱 초과 정도로 보간).</summary>
        private const float FlowerRatioMin = 0.08f;
        private const float FlowerRatioMax = 0.15f;

        // ── 렌더/LOD 상수 ──────────────────────────────────────────────────────────

        /// <summary>이 거리(카메라→섬 테두리) 이내면 A+B 전체를 그린다.</summary>
        private const float FullDetailDistance = 60f;

        /// <summary>이 거리(카메라→섬 테두리) 밖이면 섬을 통째로 스킵한다.</summary>
        private const float MaxRenderDistance = 300f;

        /// <summary>Graphics.RenderMeshInstanced 1회 호출당 인스턴스 상한(계약: 1023개 단위 배치).</summary>
        private const int InstancesPerBatch = 1023;

        // ── 레지스트리 (SeabedGenerator.SkirtRecord와 같은 생명주기 규칙) ──────────────

        private sealed class GrassRecord
        {
            public Transform root;         // 섬 지형 오브젝트. 파괴(RegenerateWorld) 감지용.
            public Vector3 center;         // 섬 중심(월드)
            public float radius;           // 섬 지형 반지름 R
            public Matrix4x4[] groupA;     // 잔디 LOD 그룹 A(약 절반). 원거리에서는 이것만 그린다.
            public Matrix4x4[] groupB;     // 잔디 LOD 그룹 B(나머지 절반). 근거리에서만 추가.
            public Matrix4x4[] flowerA;    // 꽃 LOD 그룹 A(꽃 머티리얼로 렌더. 폴백 시 null).
            public Matrix4x4[] flowerB;    // 꽃 LOD 그룹 B.
            public Bounds bounds;          // RenderParams.worldBounds(섬 단위)
        }

        private static readonly List<GrassRecord> registry = new List<GrassRecord>();

        private static Mesh bladeMesh;
        private static Material grassMaterial;
        private static Material flowerMaterial;  // 카드 텍스처 폴백 시 null - 꽃 배치 자체가 없다.
        private static bool shaderMissing;       // 한 번 실패하면 이후 전부 조용히 무동작(계약)
        private static bool hasCardTexture;      // grass_card 로드 성공 여부(꽃/카드 규격 스위치)
        private static int windTimeId;
        private static int playerPosId;

        // ═══════════════════════════════════════════════════════════════════════════
        //  배치 (섬당 1회, 결정적)
        // ═══════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// 섬 하나의 잔디/꽃 인스턴스 배열을 굽는다. IslandMeshGenerator.BuildGroundCaps에서
        /// 섬 메시 확보 직후 같은 동기 흐름으로 호출된다(SeabedGenerator.Build와 같은 훅 지점).
        /// </summary>
        /// <param name="islandObject">섬 지형 오브젝트("Island_{id}_{size}").</param>
        /// <param name="islandMesh">그 섬의 지형 메시(정점 높이를 직접 읽는 소스).</param>
        /// <param name="radius">섬 지형 반지름 R(m). 메시 XZ 반경과 같다.</param>
        /// <param name="phaseA">모래 경계 BandWobble 위상 A(BuildGroundCaps와 같은 값).</param>
        /// <param name="phaseB">모래 경계 BandWobble 위상 B(BuildGroundCaps와 같은 값).</param>
        public static void Build(GameObject islandObject, Mesh islandMesh, float radius,
            float phaseA, float phaseB)
        {
            if (islandObject == null || islandMesh == null || radius <= 0f || DensityMultiplier <= 0f)
                return;

            // 셰이더가 없으면 배치 자체를 생략한다(폴백 렌더 없음 - 계약). 행렬 메모리도 아낀다.
            if (!EnsureGrassAssets())
                return;

            Transform islandTransform = islandObject.transform;
            for (int i = 0; i < registry.Count; i++)
            {
                if (registry[i].root == islandTransform)
                    return; // 같은 섬에 두 번 불려도(방어) 잔디가 겹으로 깔리지 않게 한다.
            }

            // ── 메시 정점 격자 복원 ────────────────────────────────────────────────
            // 정점 순서는 GenerateIslandMesh가 결정적으로 보장한다(클래스 주석). 최외곽 링의
            // 정점 수를 세어 radialSegments를 얻고(SeabedGenerator.ExtractRimHeights와 같은
            // 문턱 R-1m - 링 간격이 R/ringCount ≥ 5m라 오분류가 없다) 총 정점 수로 검산한다.
            Vector3[] verts = islandMesh.vertices;
            float rimThreshold = radius - 1f;
            int segments = 0;
            for (int i = 0; i < verts.Length; i++)
            {
                Vector3 v = verts[i];
                if (Mathf.Sqrt(v.x * v.x + v.z * v.z) >= rimThreshold)
                    segments++;
            }
            if (segments < 8 || (verts.Length - 1) % segments != 0)
                return; // 절차적 지형이 아닌 구성(placeholder 프리팹 등)이면 조용히 건너뛴다.
            int rings = (verts.Length - 1) / segments;
            if (rings < 2)
                return;

            Vector3 center = islandTransform.position;

            // 섬별 해시 salt. 섬 월드 위치(worldSeed에 결정적)만 입력으로 받는 순수 해시라
            // 같은 시드면 항상 같은 잔디가 나오고, 반지름이 같은 다른 섬과는 패턴이 갈린다.
            uint islandSalt;
            unchecked
            {
                islandSalt = Mix32((uint)(Mathf.RoundToInt(center.x * 4f) * 73856093)
                    ^ (uint)(Mathf.RoundToInt(center.z * 4f) * 19349663) ^ 0x3D4A2C15u);
            }

            // [B52] 섬별 잔디 경계선(단일 소스: IslandMeshGenerator - 캡의 DrySandCap 상한(DryTop)과
            // 정확히 같은 값이다. 어긋나면 모래 위 잔디/잔디 없는 초원이 생긴다). t는 밀도에도 재사용한다.
            // ComputeGrassLineT는 섬 월드 위치만 입력인 순수 해시라 rng 소비 0, 세이브 로드 후에도 동일하다.
            float grassLineT = IslandMeshGenerator.ComputeGrassLineT(islandObject);
            float grassMinHeightBase =
                IslandMeshGenerator.GrassLineHeightFromT(grassLineT) + GrassBoundaryDitherHalf;

            // 섬 크기별 목표 개수: Small(R50) ~12k / Medium(R90) ~20k / Large(R140) ~30k / XL(R200) ~40k.
            // 반지름 선형 보간이 위 네 점을 ±4% 안으로 지나므로 별도 테이블이 필요 없다.
            // [B52] 여기에 전역 0.65 × 섬별 lerp(1.0, 0.45, t)를 곱한다 - 경계선이 높은 모래 섬(t 큼)
            // 일수록 남은 초지의 잔디도 성기다. 목표 개수만 줄고 이후 로직(패치·꽃·LOD)은 불변이다.
            float targetCount = DensityMultiplier
                * GlobalDensityScale
                * Mathf.Lerp(1f, IslandDensityMin, grassLineT)
                * Mathf.Lerp(12000f, 40000f, Mathf.InverseLerp(50f, 200f, radius));
            if (targetCount < 1f)
                return;

            int cellRange = Mathf.CeilToInt(radius / CellSpacing);
            float maxPlaceRadiusSq = radius * 0.98f * radius * 0.98f;

            // ── 1차 통과: 초지 조건을 만족하는 후보 셀의 패치 배율 합을 구한다(저장 없음) ──
            // v1은 개수만 셌지만 v2는 채택 확률에 패치 노이즈 배율(0.35~1.0)이 곱해지므로,
            // 배율 합계로 나눠야 총 개수가 목표치 근처에 남는다. 경사 검사는 여기서 하지
            // 않는다(비싼 검사라 채택된 소수에만 건다 - 목표는 "~" 근사치라 몇 % 미달은 허용).
            float eligibleWeight = 0f;
            for (int iz = -cellRange; iz <= cellRange; iz++)
            {
                for (int ix = -cellRange; ix <= cellRange; ix++)
                {
                    float x, z, y;
                    if (TryGetGrassCandidate(ix, iz, islandSalt, verts, rings, segments, radius,
                            maxPlaceRadiusSq, phaseA, phaseB, grassMinHeightBase, out x, out z, out y))
                        eligibleWeight += PatchDensity(x, z, islandSalt);
                }
            }
            if (eligibleWeight <= 0f)
                return;

            // 패치 배율이 곱해진 채택 판정에서 총 기대 개수 = keepProbability × 배율 합.
            // 1을 넘으면(작은 초지) 무성한 셀이 전부 채택될 뿐이라 클램프가 필요 없다.
            float keepProbability = targetCount / eligibleWeight;

            // ── 2차 통과: 해시 선별(×패치 배율) + 경사 검사 + 행렬 생성 + 꽃 전환 ──────────
            int expected = Mathf.CeilToInt(targetCount * 0.55f) + 16;
            var listA = new List<Matrix4x4>(expected);
            var listB = new List<Matrix4x4>(expected);
            List<Matrix4x4> flowerListA = null;
            List<Matrix4x4> flowerListB = null;
            if (flowerMaterial != null)
            {
                // 꽃은 전체의 최대 ~2%(면적 12% × 전환 15%) 수준 - 소용량으로 시작해도 충분하다.
                flowerListA = new List<Matrix4x4>(256);
                flowerListB = new List<Matrix4x4>(256);
            }
            float minY = float.MaxValue;
            float maxY = float.MinValue;

            for (int iz = -cellRange; iz <= cellRange; iz++)
            {
                for (int ix = -cellRange; ix <= cellRange; ix++)
                {
                    float x, z, y;
                    if (!TryGetGrassCandidate(ix, iz, islandSalt, verts, rings, segments, radius,
                            maxPlaceRadiusSq, phaseA, phaseB, grassMinHeightBase, out x, out z, out y))
                        continue;

                    // 군락감: 채택 확률에 파장 ~7m 저주파 노이즈 배율(0.35~1.0)을 곱한다 -
                    // 균일 카펫이 아니라 무성한 곳/성긴 곳으로 갈린다. 1차 통과와 같은 식이라
                    // 총 개수는 목표치 근처를 유지한다.
                    float patch = PatchDensity(x, z, islandSalt);
                    if (Hash01(ix, iz, islandSalt ^ 0x9E3779B9u) > keepProbability * patch)
                        continue;

                    // 경사 30도 초과 제외(유한 차분). 절벽(P7 메사)·수로 둑 상단 급경사를 걸러낸다.
                    float gx = (SampleHeight(verts, rings, segments, radius, x + SlopeProbeStep, z)
                              - SampleHeight(verts, rings, segments, radius, x - SlopeProbeStep, z))
                              / (2f * SlopeProbeStep);
                    float gz = (SampleHeight(verts, rings, segments, radius, x, z + SlopeProbeStep)
                              - SampleHeight(verts, rings, segments, radius, x, z - SlopeProbeStep))
                              / (2f * SlopeProbeStep);
                    if (gx * gx + gz * gz > MaxSlopeTan * MaxSlopeTan)
                        continue;

                    // 인스턴스 변주(전부 위치 해시): yaw 0~360° + [v3] 이봉 스케일 -
                    // 80%는 하층 짧은 풀(y 0.5~0.9), 20%는 솟은 다발(y 1.0~1.5, xz 1.0~1.25).
                    // 균일 단봉 분포의 "고른 카펫"이 하층+다발 두 층으로 갈라진다.
                    float yaw = Hash01(ix, iz, islandSalt ^ 0x85EBCA6Bu) * 360f;
                    bool isTuft = Hash01(ix, iz, islandSalt ^ 0x94D049BBu) < TuftFraction;
                    float hY = Hash01(ix, iz, islandSalt ^ 0xC2B2AE35u);
                    float hXZ = Hash01(ix, iz, islandSalt ^ 0x27D4EB2Fu);
                    float scaleY = isTuft
                        ? Mathf.Lerp(TuftScaleYMin, TuftScaleYMax, hY)
                        : Mathf.Lerp(UnderScaleYMin, UnderScaleYMax, hY);
                    float scaleXZ = isTuft
                        ? Mathf.Lerp(TuftScaleXZMin, TuftScaleXZMax, hXZ)
                        : Mathf.Lerp(UnderScaleXZMin, UnderScaleXZMax, hXZ);

                    var worldPos = new Vector3(center.x + x, center.y + y - RootSinkDepth, center.z + z);
                    var matrix = Matrix4x4.TRS(worldPos, Quaternion.Euler(0f, yaw, 0f),
                        new Vector3(scaleXZ, scaleY, scaleXZ));

                    // 꽃 무리: 파장 ~11m 노이즈가 문턱(상위 ~12%)을 넘는 지대에서만, 채택된 잔디의
                    // 8~15%(문턱 초과 정도로 보간)를 꽃 배치로 전환한다. 텍스처 폴백 시에는
                    // flowerList가 null이라 이 분기 전체가 죽는다(v1과 같은 잔디-only).
                    bool isFlower = false;
                    if (flowerListA != null)
                    {
                        float zone = LatticeNoise(x, z, FlowerWavelength, islandSalt ^ 0x68E31DA4u);
                        if (zone > FlowerZoneThreshold)
                        {
                            float ratio = Mathf.Lerp(FlowerRatioMin, FlowerRatioMax,
                                (zone - FlowerZoneThreshold) / (1f - FlowerZoneThreshold));
                            isFlower = Hash01(ix, iz, islandSalt ^ 0xB5297A4Du) < ratio;
                        }
                    }

                    // LOD 그룹 배정도 위치 해시(프레임에서 절대 재계산하지 않는다). 꽃도 같은 규칙.
                    bool inGroupA = Hash01(ix, iz, islandSalt ^ 0x165667B1u) < 0.5f;
                    if (isFlower)
                        (inGroupA ? flowerListA : flowerListB).Add(matrix);
                    else
                        (inGroupA ? listA : listB).Add(matrix);

                    if (y < minY) minY = y;
                    if (y > maxY) maxY = y;
                }
            }

            int total = listA.Count + listB.Count
                + (flowerListA != null ? flowerListA.Count + flowerListB.Count : 0);
            if (total == 0)
                return;

            // worldBounds는 섬 단위 하나. 높이는 최대 신장 + 바람 진폭 여유 - [v3] 다발 스케일
            // 상한 1.5 × 폴백 blade 높이 1m = 1.5m까지 덮도록 여유를 1.65로 올렸다(카드 0.65m는
            // 0.975m라 원래도 여유. 컬링 바운즈만 커질 뿐 배치·판정은 불변).
            float boundsMinY = center.y + minY - RootSinkDepth - 0.2f;
            float boundsMaxY = center.y + maxY + 1.65f;
            var bounds = new Bounds(
                new Vector3(center.x, (boundsMinY + boundsMaxY) * 0.5f, center.z),
                new Vector3(radius * 2f + 2f, boundsMaxY - boundsMinY, radius * 2f + 2f));

            registry.Add(new GrassRecord
            {
                root = islandTransform,
                center = center,
                radius = radius,
                groupA = listA.ToArray(),
                groupB = listB.ToArray(),
                flowerA = flowerListA != null && flowerListA.Count > 0 ? flowerListA.ToArray() : null,
                flowerB = flowerListB != null && flowerListB.Count > 0 ? flowerListB.ToArray() : null,
                bounds = bounds,
            });
        }

        /// <summary>
        /// 격자 셀 (ix, iz) 하나를 초지 후보로 평가한다. 지터 위치가 산포 원 안이고 지형 높이가
        /// 초지 경계(섬별 grassLine + 디더 반폭 = grassMinHeightBase) 위면 true와 함께
        /// 섬 로컬 (x, z, y)를 돌려준다.
        /// 1차(계수)와 2차(생성) 통과가 반드시 같은 판정을 내려야 하므로 한 함수로 공유한다.
        /// </summary>
        /// <param name="grassMinHeightBase">[B52] IslandMeshGenerator.GrassLineHeightFromT(t) +
        /// GrassBoundaryDitherHalf. Build가 섬당 1회 계산해 넘긴다(캡 경계와 단일 소스).</param>
        private static bool TryGetGrassCandidate(int ix, int iz, uint islandSalt, Vector3[] verts,
            int rings, int segments, float radius, float maxPlaceRadiusSq, float phaseA, float phaseB,
            float grassMinHeightBase, out float x, out float z, out float y)
        {
            x = ix * CellSpacing + (Hash01(ix, iz, islandSalt ^ 0x51ED270Bu) - 0.5f) * 0.9f * CellSpacing;
            z = iz * CellSpacing + (Hash01(ix, iz, islandSalt ^ 0x1B873593u) - 0.5f) * 0.9f * CellSpacing;
            y = 0f;

            float rSq = x * x + z * z;
            if (rSq > maxPlaceRadiusSq)
                return false;

            float angle = Mathf.Atan2(z, x);
            y = SampleHeightPolar(verts, rings, segments, radius, Mathf.Sqrt(rSq), angle);

            // 초지 경계: BuildGroundCaps의 DryTop과 같은 식(섬별 grassLine + BandWobble) + 디더 반폭 0.18.
            float minHeight = grassMinHeightBase
                + 0.22f * Mathf.Sin(angle * 2f + phaseA)
                + 0.12f * Mathf.Sin(angle * 3f + phaseB);
            return y >= minHeight;
        }

        // ── 저주파 격자 해시 노이즈 (군락/꽃 무리 - rng 소비 0) ────────────────────────

        /// <summary>패치 밀도 배율 0.35~1.0. 파장 ~7m 노이즈로 무성한 곳/성긴 곳을 가른다.</summary>
        private static float PatchDensity(float x, float z, uint islandSalt)
        {
            return Mathf.Lerp(PatchFloor, 1f,
                LatticeNoise(x, z, PatchWavelength, islandSalt ^ 0x7F4A7C15u));
        }

        /// <summary>
        /// 파장 wavelength의 값 노이즈 [0,1]: 격자점 해시(Hash01)를 smoothstep 이중 선형 보간.
        /// 입력이 (섬 로컬 좌표, salt)뿐인 순수 함수라 1차/2차 통과가 항상 같은 값을 본다.
        /// </summary>
        private static float LatticeNoise(float x, float z, float wavelength, uint salt)
        {
            float fx = x / wavelength;
            float fz = z / wavelength;
            int x0 = Mathf.FloorToInt(fx);
            int z0 = Mathf.FloorToInt(fz);
            float tx = fx - x0;
            float tz = fz - z0;
            tx = tx * tx * (3f - 2f * tx);
            tz = tz * tz * (3f - 2f * tz);
            float h00 = Hash01(x0, z0, salt);
            float h10 = Hash01(x0 + 1, z0, salt);
            float h01 = Hash01(x0, z0 + 1, salt);
            float h11 = Hash01(x0 + 1, z0 + 1, salt);
            return Mathf.Lerp(Mathf.Lerp(h00, h10, tx), Mathf.Lerp(h01, h11, tx), tz);
        }

        // ── 메시 정점 격자 높이 샘플러 ────────────────────────────────────────────────

        /// <summary>섬 로컬 (x, z)의 지형 높이. 극좌표로 바꿔 정점 격자를 이중 선형 보간한다.</summary>
        private static float SampleHeight(Vector3[] verts, int rings, int segments, float radius,
            float x, float z)
        {
            return SampleHeightPolar(verts, rings, segments, radius,
                Mathf.Sqrt(x * x + z * z), Mathf.Atan2(z, x));
        }

        /// <summary>
        /// (반경 r, 각도 angle)의 지형 높이. ring 0은 중심 정점(index 0), ring k(1..rings)의
        /// seg s는 verts[1+(k-1)*segments+s]다(GenerateIslandMesh의 결정적 정점 순서).
        /// </summary>
        private static float SampleHeightPolar(Vector3[] verts, int rings, int segments, float radius,
            float r, float angle)
        {
            float ringF = Mathf.Clamp(r / radius * rings, 0f, rings - 0.0001f);
            int ring0 = (int)ringF;
            float tRing = ringF - ring0;

            float twoPi = Mathf.PI * 2f;
            float a = angle - Mathf.Floor(angle / twoPi) * twoPi; // [0, 2π)
            float segF = a / twoPi * segments;
            int seg0 = (int)segF;
            if (seg0 >= segments) seg0 = 0;
            int seg1 = (seg0 + 1) % segments;
            float tSeg = segF - (int)segF;

            float h0 = RingHeight(verts, segments, ring0, seg0, seg1, tSeg);
            float h1 = RingHeight(verts, segments, ring0 + 1, seg0, seg1, tSeg);
            return Mathf.Lerp(h0, h1, tRing);
        }

        /// <summary>ring(0 = 중심 정점) 위 각도 보간 높이.</summary>
        private static float RingHeight(Vector3[] verts, int segments, int ring, int seg0, int seg1, float tSeg)
        {
            if (ring <= 0)
                return verts[0].y;
            int baseIndex = 1 + (ring - 1) * segments;
            return Mathf.Lerp(verts[baseIndex + seg0].y, verts[baseIndex + seg1].y, tSeg);
        }

        // ── 결정적 해시 (rng 소비 0 - SeabedGenerator.LatticeHash01과 같은 finalizer 계열) ──

        /// <summary>격자 정수 좌표 + salt → [0,1). System.Random/UnityEngine.Random을 일절 쓰지 않는다.</summary>
        private static float Hash01(int xi, int zi, uint salt)
        {
            unchecked
            {
                uint h = (uint)(xi * 73856093) ^ (uint)(zi * 19349663) ^ salt;
                h ^= h >> 16;
                h *= 0x7FEB352Du;
                h ^= h >> 15;
                h *= 0x846CA68Bu;
                h ^= h >> 16;
                return (h & 0xFFFFFFu) / (float)0x1000000u;
            }
        }

        /// <summary>uint 하나를 섞는다(IslandMeshGenerator.Mix32와 같은 finalizer).</summary>
        private static uint Mix32(uint h)
        {
            unchecked
            {
                h ^= h >> 16;
                h *= 0x7FEB352Du;
                h ^= h >> 15;
                h *= 0x846CA68Bu;
                h ^= h >> 16;
                return h;
            }
        }

        // ── 공유 에셋 (카드 메시 1개 + 머티리얼 2장, 월드 전체 공유) ─────────────────────

        /// <summary>
        /// 셰이더/텍스처 로드와 머티리얼/카드 메시를 준비한다. 셰이더가 없으면 false를 래치하고
        /// 이후 모든 경로가 조용히 무동작한다(계약: 폴백 렌더 없음). 텍스처(grass_card)가 없으면
        /// v1 방식(좁은 blade + 틴트만, 꽃 없음)으로 폴백한다. 필드 초기화식이 아니라 호출 시
        /// 로드한다(Unity 6.5: 필드 초기화식에서 Resources.Load 금지).
        /// </summary>
        private static bool EnsureGrassAssets()
        {
            if (shaderMissing)
                return false;
            if (grassMaterial != null && bladeMesh != null)
                return true;

            if (grassMaterial == null)
            {
                Shader shader = Resources.Load<Shader>("Shaders/MGGrass");
                if (shader == null)
                {
                    shaderMissing = true;
                    return false;
                }

                // 카드 텍스처(2×2 아틀라스). null이어도 계속 간다 - 셰이더 _BaseMap 기본값
                // white가 곧 v1 틴트 그라데이션 렌더라 잔디는 그대로 나온다(꽃만 생략).
                Texture2D cardTexture = Resources.Load<Texture2D>("Textures/grass_card");
                hasCardTexture = cardTexture != null;

                // 잔디 머티리얼: 해시 셀 선택(_CellOverride 기본 -1), 틴트 온(톤 조절용 완화 승수).
                grassMaterial = new Material(shader) { name = "MGGrassFieldMaterial" };
                grassMaterial.enableInstancing = true;
                // 틴트 기본값(계약): 뿌리/끝/마른 풀. 나머지(_WindStrength/_TrampleRadius)는 셰이더
                // Properties 기본값을 그대로 쓴다.
                grassMaterial.SetColor("_RootColor", new Color(0.16f, 0.30f, 0.14f, 1f));
                grassMaterial.SetColor("_TipColor", new Color(0.45f, 0.62f, 0.28f, 1f));
                grassMaterial.SetColor("_DryTint", new Color(0.55f, 0.52f, 0.30f, 1f));
                // [v3] 음영: 풀끝 스페큘러 시트 + 역광 투과(셰이더 기본값과 같지만 명시 세팅이 계약).
                grassMaterial.SetFloat("_SheenStrength", 0.35f);
                grassMaterial.SetFloat("_TranslucencyStrength", 0.4f);
                if (hasCardTexture)
                {
                    grassMaterial.SetTexture("_BaseMap", cardTexture);
                    grassMaterial.SetFloat("_TintStrength", 0.65f); // 텍스처가 이미 뿌리→끝 색을 가진다
                }
                else
                {
                    grassMaterial.SetFloat("_TintStrength", 1f);    // v1 방식: 틴트가 곧 알베도
                }

                // 꽃 머티리얼: (1,1) 분홍 꽃 스파이크 셀 고정 + 틴트 오프(텍스처 원색).
                // 텍스처 폴백 시에는 만들지 않는다 - 꽃 배치 분기 자체가 죽는다.
                if (hasCardTexture)
                {
                    flowerMaterial = new Material(shader) { name = "MGGrassFlowerMaterial" };
                    flowerMaterial.enableInstancing = true;
                    flowerMaterial.SetTexture("_BaseMap", cardTexture);
                    flowerMaterial.SetFloat("_CellOverride", 3f);
                    flowerMaterial.SetFloat("_TintStrength", 0f);
                    // [v3] 꽃은 시트를 약하게(꽃잎이 번들거리면 플라스틱 느낌), 투과는 더 세게
                    // (얇은 꽃잎이 역광에서 더 잘 비친다).
                    flowerMaterial.SetFloat("_SheenStrength", 0.2f);
                    flowerMaterial.SetFloat("_TranslucencyStrength", 0.5f);
                }

                windTimeId = Shader.PropertyToID("_MG_WindTime");
                playerPosId = Shader.PropertyToID("_MG_PlayerPos");
            }

            if (bladeMesh == null)
                bladeMesh = hasCardTexture ? CreateCardMesh() : CreateBladeMesh();
            return true;
        }

        /// <summary>
        /// 카드 메시(v2 계약 규격): 교차 쿼드 2장 = 정점 8개·삼각형 4개. 폭 0.55m·높이 0.65m·
        /// 피벗 밑동, UV.y 0(뿌리)~1(끝). 카드 한 장에 텍스처 풀잎 수십 가닥이 실리므로 v1 blade보다
        /// 넓고 낮다. Cull Off 셰이더라 단면이면 충분하고, 높이 변주는 인스턴스 행렬 스케일 몫이다.
        /// </summary>
        private static Mesh CreateCardMesh()
        {
            return CreateCrossQuadMesh("GrassCard", 0.275f, 0.65f);
        }

        /// <summary>
        /// v1 blade 메시(텍스처 폴백 규격): 폭 0.14m·높이 1m. grass_card가 없을 때 틴트
        /// 그라데이션만으로 그리던 v1 모양을 그대로 유지한다.
        /// </summary>
        private static Mesh CreateBladeMesh()
        {
            return CreateCrossQuadMesh("GrassBlade", 0.07f, 1f);
        }

        /// <summary>교차 쿼드 2장 메시 공용 생성기. 피벗 밑동, UV.y 0(뿌리)~1(끝).</summary>
        private static Mesh CreateCrossQuadMesh(string name, float halfWidth, float height)
        {
            var mesh = new Mesh { name = name };

            mesh.vertices = new[]
            {
                // 쿼드 1: XY 평면(법선 +Z)
                new Vector3(-halfWidth, 0f, 0f), new Vector3(halfWidth, 0f, 0f),
                new Vector3(-halfWidth, height, 0f), new Vector3(halfWidth, height, 0f),
                // 쿼드 2: ZY 평면(법선 +X) - 90도 교차
                new Vector3(0f, 0f, -halfWidth), new Vector3(0f, 0f, halfWidth),
                new Vector3(0f, height, -halfWidth), new Vector3(0f, height, halfWidth),
            };
            mesh.uv = new[]
            {
                new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f),
            };
            mesh.normals = new[]
            {
                Vector3.forward, Vector3.forward, Vector3.forward, Vector3.forward,
                Vector3.right, Vector3.right, Vector3.right, Vector3.right,
            };
            mesh.triangles = new[]
            {
                0, 2, 3, 0, 3, 1,
                4, 6, 7, 4, 7, 5,
            };
            // 바람 진폭(0.12m)·스케일 상한(1.15)을 덮는 여유 바운즈. 컬링은 어차피
            // RenderParams.worldBounds(섬 단위)가 담당하므로 넉넉해도 비용이 없다.
            mesh.bounds = new Bounds(new Vector3(0f, height * 0.6f, 0f),
                new Vector3(halfWidth * 2f + 0.5f, height * 1.5f, halfWidth * 2f + 0.5f));
            return mesh;
        }

        // ═══════════════════════════════════════════════════════════════════════════
        //  렌더 드라이버 (매 프레임)
        // ═══════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// 씬마다 자동 생성되는 렌더 드라이버. UnderwaterAmbience/AtmospherePostFX와 같은
        /// SubsystemRegistration + sceneLoaded 부트스트랩 패턴이다(씬 수정 없음, 재시작 안전).
        /// 렌더 상태(레지스트리/메시/머티리얼)는 정적이므로 이 컴포넌트는 순수한 프레임 펌프다.
        /// SeabedGenerator.SeabedBatchLogger처럼 중첩 private MonoBehaviour로 둔다(AddComponent 전용).
        /// </summary>
        private sealed class GrassFieldDriver : MonoBehaviour
        {
            private Camera targetCamera;
            private PlayerController player;

            [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
            private static void Bootstrap()
            {
                SceneManager.sceneLoaded += (scene, mode) =>
                {
                    if (FindAnyObjectByType<GrassFieldDriver>() != null)
                        return;

                    var go = new GameObject("GrassFieldDriver");
                    go.AddComponent<GrassFieldDriver>();
                };
            }

            /// <summary>
            /// LateUpdate인 이유: 플레이어/카메라 이동(Update)이 끝난 뒤의 최종 위치로
            /// _MG_PlayerPos와 거리 컷을 계산해야 잔디 밟힘이 한 프레임 늦지 않는다
            /// (UnderwaterAmbience의 "마지막 승자" 관례와 같은 자리).
            /// </summary>
            private void LateUpdate()
            {
                if (registry.Count == 0 || grassMaterial == null || bladeMesh == null)
                    return; // 셰이더 실패 포함 - 조용히 무동작(계약)

                // 카메라는 파괴/재생성될 수 있으므로 null이면 다시 집는다(파괴된 오브젝트 == null 규칙).
                if (targetCamera == null)
                {
                    targetCamera = Camera.main;
                    if (targetCamera == null)
                        return;
                }

                // 플레이어 1회 캐시·null 저빈도 재시도(UnderwaterAmbience가 WorldMapManager를 찾는
                // 것과 같은 규칙 - 정상 경로에서 탐색 비용/할당 0).
                if (player == null && Time.frameCount % 60 == 0)
                    player = FindAnyObjectByType<PlayerController>();

                // 매 프레임 주입(계약). 잔디/꽃 머티리얼 둘 다 - 꽃도 같은 바람/밟힘을 받는다.
                // 플레이어를 아직 못 찾았으면 _MG_PlayerPos는 셰이더 기본값(지하 -10000)에 남아
                // 밟힘이 없다 - 올바른 무동작이다.
                float now = Time.time;
                grassMaterial.SetFloat(windTimeId, now);
                if (flowerMaterial != null)
                    flowerMaterial.SetFloat(windTimeId, now);
                if (player != null)
                {
                    Vector3 p = player.transform.position;
                    var packed = new Vector4(p.x, p.y, p.z, 0f);
                    grassMaterial.SetVector(playerPosId, packed);
                    if (flowerMaterial != null)
                        flowerMaterial.SetVector(playerPosId, packed);
                }

                Vector3 camPos = targetCamera.transform.position;

                for (int i = registry.Count - 1; i >= 0; i--)
                {
                    GrassRecord record = registry[i];

                    // 섬 파괴(RegenerateWorld) 시 정리: 유니티 == 오버로드로 감지해 레코드를 버린다
                    // (행렬 배열은 관리 메모리라 이 제거로 GC 대상이 된다 - SeabedGenerator와 같은 규칙).
                    if (record.root == null)
                    {
                        registry.RemoveAt(i);
                        continue;
                    }

                    // RegenerateWorld는 Destroy 전에 SetActive(false)를 먼저 건다(WorldMapManager).
                    // 비활성 섬의 잔디를 이번 프레임에 그리면 유령 잔디가 남으므로 함께 쉰다.
                    if (!record.root.gameObject.activeInHierarchy)
                        continue;

                    // 거리 컷은 섬당 1회: 카메라 → 섬 테두리(중심 거리 - R).
                    float edgeDistance = Vector3.Distance(camPos, record.center) - record.radius;
                    if (edgeDistance > MaxRenderDistance)
                        continue;

                    bool fullDetail = edgeDistance <= FullDetailDistance;

                    var rparams = new RenderParams(grassMaterial)
                    {
                        worldBounds = record.bounds,
                        shadowCastingMode = ShadowCastingMode.Off,
                        receiveShadows = true,
                    };

                    // 간이 LOD: 미리 갈라 둔 그룹을 거리로 선택만 한다(인스턴스 단위 재계산 없음).
                    RenderGroup(rparams, record.groupA);
                    if (fullDetail)
                        RenderGroup(rparams, record.groupB);

                    // 꽃 배치: 별도 머티리얼((1,1) 셀 고정·틴트 오프), LOD 분할은 잔디와 동일 규칙.
                    if (flowerMaterial != null && (record.flowerA != null || record.flowerB != null))
                    {
                        var flowerParams = new RenderParams(flowerMaterial)
                        {
                            worldBounds = record.bounds,
                            shadowCastingMode = ShadowCastingMode.Off,
                            receiveShadows = true,
                        };
                        RenderGroup(flowerParams, record.flowerA);
                        if (fullDetail)
                            RenderGroup(flowerParams, record.flowerB);
                    }
                }
            }

            /// <summary>행렬 배열을 1023개 단위로 잘라 그린다. 배열 재사용 - 프레임당 할당 0.</summary>
            private static void RenderGroup(in RenderParams rparams, Matrix4x4[] matrices)
            {
                if (matrices == null)
                    return;
                for (int start = 0; start < matrices.Length; start += InstancesPerBatch)
                {
                    Graphics.RenderMeshInstanced(rparams, bladeMesh, 0, matrices,
                        Mathf.Min(InstancesPerBatch, matrices.Length - start), start);
                }
            }
        }
    }
}
