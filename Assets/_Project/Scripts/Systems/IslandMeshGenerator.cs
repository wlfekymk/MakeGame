using System.Collections.Generic;
using UnityEngine;
using MakeGame.Data;

namespace MakeGame.Systems
{
    /// <summary>
    /// 섬 지형용 절차적 메시를 생성하는 유틸리티.
    /// 밋밋한 원기둥 플레이스홀더 대신, 중심이 살짝 높고 가장자리로 갈수록 완만하게 낮아지며
    /// 약간의 굴곡(펄린 노이즈)이 있는, 실제로 걸어다닐 수 있는 낮은 언덕 모양의 지형을 만든다.
    ///
    /// [B5 확장 - "민둥산" 해소] 지형 메시 생성에 더해, 그 위에 얹는 지면 구분(해안 모래 / 내륙 풀)과
    /// 초목(야자수·덤불·풀포기) 배치도 이 클래스가 담당한다. ArtDirection.md 0장이 "정점을 직접 찍는
    /// 절차적 메시(IslandMeshGenerator)가 야자수·바위처럼 프리미티브 조합보다 한 단계 복잡한 형태를
    /// 만들 수 있는 유일한 검증된 경로"라고 명시하고 있어, 섬 표면 시각 요소의 단일 소스를 여기로 모은다.
    /// </summary>
    public static class IslandMeshGenerator
    {
        /// <summary>
        /// 지정한 반지름과 최대 높이를 가진 둥근 언덕 모양의 섬 지형 메시를 생성한다.
        /// 중심에서 가장자리로 갈수록 코사인 곡선으로 완만하게 낮아지고,
        /// 펄린 노이즈로 자연스러운 굴곡을 더하되 가장자리에서는 노이즈를 줄여 매끄럽게 물과 맞닿게 한다.
        /// </summary>
        public static Mesh GenerateIslandMesh(float radius, float maxHeight, int ringCount = 6, int radialSegments = 24, float noiseScale = 0.05f, float noiseAmplitude = 2.0f)
        {
            var mesh = new Mesh();
            mesh.name = "IslandTerrain";

            int vertexCount = 1 + ringCount * radialSegments;
            var vertices = new Vector3[vertexCount];
            var uvs = new Vector2[vertexCount];

            // 중심점 (인덱스 0)
            vertices[0] = new Vector3(0f, maxHeight, 0f);
            uvs[0] = new Vector2(0.5f, 0.5f);

            int index = 1;
            for (int ring = 1; ring <= ringCount; ring++)
            {
                float t = (float)ring / ringCount; // 0(중심)~1(가장자리)
                float r = t * radius;
                float baseHeight = maxHeight * Mathf.Cos(t * Mathf.PI * 0.5f);

                for (int seg = 0; seg < radialSegments; seg++)
                {
                    float angle = (float)seg / radialSegments * Mathf.PI * 2f;
                    float x = Mathf.Cos(angle) * r;
                    float z = Mathf.Sin(angle) * r;

                    float noise = (Mathf.PerlinNoise(x * noiseScale + 1000f, z * noiseScale + 1000f) - 0.5f) * noiseAmplitude * (1f - t);
                    float y = Mathf.Max(0f, baseHeight + noise);

                    vertices[index] = new Vector3(x, y, z);
                    uvs[index] = new Vector2(x / radius * 0.5f + 0.5f, z / radius * 0.5f + 0.5f);
                    index++;
                }
            }

            var triangles = new List<int>();

            // 중심 -> 첫 번째 링 (부채꼴)
            // 주의: c, b 순서로 추가해야 위쪽을 향하는 법선이 나온다 (b, c 순서면 아래를 향해 컬링되어
            // 지형 중앙에 구멍이 뚫린 것처럼 보이고, 콜라이더도 그 구멍으로 플레이어가 빠지는 버그가 있었다).
            for (int seg = 0; seg < radialSegments; seg++)
            {
                int b = 1 + seg;
                int c = 1 + (seg + 1) % radialSegments;
                triangles.Add(0);
                triangles.Add(c);
                triangles.Add(b);
            }

            // 링과 링 사이 (사각형을 삼각형 2개로)
            for (int ring = 1; ring < ringCount; ring++)
            {
                int ringStart = 1 + (ring - 1) * radialSegments;
                int nextRingStart = 1 + ring * radialSegments;

                for (int seg = 0; seg < radialSegments; seg++)
                {
                    int a = ringStart + seg;
                    int b = ringStart + (seg + 1) % radialSegments;
                    int c = nextRingStart + seg;
                    int d = nextRingStart + (seg + 1) % radialSegments;

                    triangles.Add(a);
                    triangles.Add(b);
                    triangles.Add(d);

                    triangles.Add(a);
                    triangles.Add(d);
                    triangles.Add(c);
                }
            }

            mesh.vertices = vertices;
            mesh.uv = uvs;
            mesh.triangles = triangles.ToArray();
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            return mesh;
        }

        // ─────────────────────────────────────────────────────────────────────────
        //  섬 표면(지면 구분 + 초목)  —  "민둥산" 해소
        // ─────────────────────────────────────────────────────────────────────────

        /// <summary>초목/잔디 캡을 담는 루트 자식의 이름. 중복 생성 방지 판정에도 쓴다.</summary>
        public const string SurfaceRootName = "IslandSurface";

        /// <summary>
        /// 섬 하나에 배치할 수 있는 초목 인스턴스(야자수 1그루 / 덤불 1개 / 풀포기 1개를 각각 1로 센다)의
        /// 절대 상한. 특대 섬(반지름 200m)의 면적은 12만 m²가 넘어서, 밀도만 보고 배치하면 초목이 수천
        /// 개까지 늘어나 프레임이 죽는다. 공식이 나중에 바뀌더라도 이 상한이 항상 마지막에 한 번 더 강제된다.
        ///
        /// [B9 정정 — 이 주석은 거짓이었다] 직전 값은 180이었고 "규모별 개수 공식이 커봐야 정확히 이 값에
        /// 닿도록 잡혀 있다"고 적혀 있었는데 **사실이 아니었다**. 당시 공식의 최대 요청치는
        /// palm 16 + bush 40 + tuft 78 = 134라서 상한 180에 46 모자랐고, 아래 트림 블록은 **단 한 번도
        /// 발동한 적이 없는 도달 불가 코드**였다. 이 프로젝트는 틀린 주석이 실제 사고를 만든 전력이 있어
        /// (scatterRadius / 자원 배율) 주석을 사실에 맞추는 대신 **값을 주석에 맞춘다** — 아래 상한과
        /// 규모별 상한의 합을 정확히 일치시켜, 트림 블록이 실제로 살아 있는 가드가 되게 한다.
        ///
        /// 현재 값 220 = 야자수 16 + 덤불 48 + 풀포기 156 (전부 특대 섬 R=200에서의 상한).
        /// 즉 특대 섬은 정확히 이 값에 닿고, 누군가 공식을 조금이라도 올리는 순간 트림이 발동한다.
        ///
        /// 예산 근거(특대 섬 실측, B10 줄기 프리즘 교체 후):
        ///   삼각형 8,016 (야자수 3,264 + 덤불 2,880 + 풀 1,872) — B9 10,512에서 **-24%**,
        ///   저폴리 교체 전 157,824 대비 **-95%**
        ///   렌더러 508 (16×13 + 48×3 + 156×1) — 프리즘 교체로 **변하지 않았다**(줄기 파츠 수 동일).
        ///
        /// [B10 그루 수를 올리지 않는 이유] 야자수 1그루가 360 → 204삼각형이 됐지만 그루 수 16은
        /// 그대로 둔다. 근거 두 가지다.
        ///   (1) 16을 정한 제약은 삼각형이 아니라 **렌더러 수**다(B8, 디렉터). 그루당 렌더러 13개는
        ///       프리즘 교체로 1개도 줄지 않았으므로 16을 올릴 근거가 새로 생기지 않았다.
        ///   (2) 16 + 48 + 156 = 220 = 이 상한과 정확히 같다. 야자수만 올리면 아래 트림 블록이 발동해
        ///       **덤불·풀이 대신 깎인다** - "야자수를 늘렸더니 숲이 성겨졌다"는 조용한 회귀가 된다.
        ///       그루 수를 올리려면 이 상한과 렌더러 예산을 함께 올려야 하고, 그것은 디렉터 결정이다.
        /// </summary>
        public const int MaxVegetationInstancesPerIsland = 220;

        /// <summary>
        /// 섬 지형 오브젝트 위에 (1) 내륙 풀밭 캡 메시와 (2) 초목(야자수/덤불/풀포기)을 배치한다.
        ///
        /// 왜 필요했나: 지형은 단색 모래(#C2B280)로 칠한 메시 하나뿐이고 초목을 만드는 코드는 프로젝트
        /// 어디에도 없었다(WorldMapManager.CreateDefaultTerrainMaterial / CreateProceduralIslandTerrain).
        /// 그래서 실제 게임에 들어가면 반지름 50~200m짜리 모래색 평지만 보였다.
        ///
        /// [콜라이더 절대 금지] 여기서 만드는 오브젝트에는 콜라이더를 단 하나도 붙이지 않는다.
        /// TerrainSampler.SnapToGround가 이름이 "Island_"로 시작하는 콜라이더만 지형으로 인정하는데,
        /// 초목에 콜라이더가 붙으면 (a) 이름 규칙상 지형으로 인정되지는 않더라도 물리 씬에 불필요한
        /// 콜라이더가 수천 개 늘어나고, (b) 이후 누군가 판정 규칙을 손대는 순간 "불러오기 후 모든
        /// 아이템이 하늘로 떠오르는" 사고가 재발한다. 그래서 프리미티브를 만들고 콜라이더를 지우는
        /// (Destroy가 프레임 끝까지 지연되는) 방식조차 쓰지 않고, 아예 콜라이더가 생기지 않는 경로
        /// (공유 메시 + 빈 GameObject + MeshFilter/MeshRenderer)로 만든다. 공유 메시는 내장 프리미티브
        /// (GetPrimitiveMesh)이거나 이 클래스가 만든 저폴리 메시(GetLowPolyLobeMesh/GetGrassBladeMesh)이며,
        /// 후자는 프리미티브를 거치지 않으므로 콜라이더가 한 프레임도 존재하지 않는다.
        ///
        /// [결정성] 배치에 UnityEngine.Random을 일절 쓰지 않는다. 호출자가 넘긴 섬별 System.Random
        /// 스트림만 소비하며, 소비 횟수도 (반지름 → 개수)가 정해지면 고정이라 같은 worldSeed면 항상
        /// 같은 숲이 나온다(SeededRandomExtensions 상단 주석의 재현성 전제를 그대로 따른다).
        /// </summary>
        /// <param name="islandObject">WorldMapManager가 만든 섬 지형 오브젝트("Island_{id}_{size}").</param>
        /// <param name="radius">이 섬의 지형 반지름(m). IslandSizeMetrics.GetTerrainRadius 값.</param>
        /// <param name="rng">이 섬 전용 결정적 난수 스트림. 다른 스포너의 스트림과 반드시 분리돼 있어야 한다.</param>
        public static void BuildIslandSurface(GameObject islandObject, float radius, System.Random rng)
        {
            if (islandObject == null || rng == null || radius <= 0f)
                return;

            // 같은 섬에 두 번 호출돼도 숲이 겹쳐 두 배로 자라지 않게 한다.
            if (islandObject.transform.Find(SurfaceRootName) != null)
                return;

            var root = new GameObject(SurfaceRootName);
            root.transform.SetParent(islandObject.transform, false);
            root.transform.localPosition = Vector3.zero;
            root.transform.localRotation = Quaternion.identity;
            root.transform.localScale = Vector3.one;

            // 머티리얼은 섬당 4개만 만들어 그 섬의 모든 초목 파츠가 공유한다. StructureVisualBuilder.
            // CreateColorMaterial은 호출할 때마다 새 Material을 만들기 때문에, 파츠마다 부르면 섬 하나에
            // 400개가 넘는 고유 머티리얼이 생겨 SRP 배처가 전혀 묶지 못한다(자원 노드는 개수가 수십 개
            // 수준이라 문제되지 않았지만 초목은 자릿수가 다르다).
            //
            // [B8 색 교체] 이전에는 잎/덤불/풀을 전부 Palm Fiber(#948C4C, 올리브)의 명도 변주로 칠했는데,
            // 실기에서 야자수가 통째로 마른 나무처럼 보였다. 근거: Palm Fiber의 상대휘도는 137, 줄기에 쓰던
            // Driftwood(#8C6640)는 107 - 차이가 1.28배뿐인 데다 색상각도 55°/29°로 둘 다 노랑~주황 계열이라
            // 줄기와 잎이 한 덩어리로 뭉쳤다. ArtDirection 1.1에 초목 전용 Frond Green/Meadow Green을
            // 추가하고(디렉터 승인), 줄기는 Driftwood를 어둡게+진하게 눌러 명도 대비를 벌린다.
            // (Palm Fiber는 "수확한 마른 섬유" 아이템 색으로 의미를 유지한다 - 기존 8색의 뜻은 그대로다.)
            //
            // [B9 줄기 색 재조정] 직전 값 Shade(Driftwood, 0.78) = #6D5032 는 명도 대비(1.75배)는 얻었지만
            // 하늘을 배경으로 실루엣이 잡히면 거의 검은 막대로 보였다. 원인은 명도가 아니라 채도다 -
            // Shade()는 세 채널을 같은 비율로 곱하므로 HSV 채도(0.54)는 그대로 두고 명도만 0.549→0.427로
            // 깎는다. 그 결과 유채색량(chroma = max-min)이 76 → 59로 줄어, 밝은 배경 앞에서 색상 정보가
            // 남지 않는 "검은 실루엣"이 됐다. 그래서 이번에는 명도를 조금만 되돌리고(×0.93) 채도를
            // 20% 올려(#82582D) 어두운 채로도 "갈색"이 읽히게 한다.
            //   명도 V 0.427 → 0.510(+19%) · 채도 S 0.541 → 0.654 · chroma 59 → 85(+44%)
            //   상대휘도 84 → 94, 잎(Frond Green 147)과의 대비 1.75배 → 1.57배
            //   (실루엣이 뭉쳤던 예전 조합은 1.28배, 순정 Driftwood라도 1.37배뿐이다 - 1.57배는 그 위다.
            //    게다가 줄기 색상각 30° / 잎 95°로 65° 벌어져 있어 대비가 명도 단독에 기대지 않는다.)
            //   하늘(daySkyTint #73A6D9, 색상각 210°) 앞에서는 거의 보색이라 실루엣이 색으로 분리되고,
            //   지면(Meadow Green 155 / Island Sand 178) 앞에서는 여전히 1.65~1.89배 어두워 분리된다.
            Material trunkMaterial = StructureVisualBuilder.CreateColorMaterial(PalmBarkColor, "wood");

            // 잎/덤불/풀을 각각 단색 한 장으로 칠하면 같은 초록이 반지름 200m를 덮어 "한 톤"으로 읽힌다.
            // 프리미티브(=렌더러) 개수를 늘리지 않고 톤만 늘리는 유일한 방법이 머티리얼 장수를 늘려
            // 인스턴스마다 돌려 쓰는 것이다. SRP 배처는 머티리얼이 아니라 셰이더 변형 단위로 묶으므로
            // 머티리얼이 4장 → 8장이 되어도 배칭은 깨지지 않는다(파츠마다 새로 만들면 400장이 되어
            // 깨지는 것과는 자릿수가 다르다).
            // 변주는 "명도"가 아니라 "색상"으로 준다 - 명도를 깎으면 위에서 확보한 줄기-잎 대비가
            // 같이 무너지기 때문이다. Frond Green ↔ Meadow Green 사이를 조금 섞어 황록/청록 쪽으로만
            // 흔들고 상대휘도는 147~150으로 유지한다.
            var frondMaterials = new[]
            {
                StructureVisualBuilder.CreateColorMaterial(StructureVisualBuilder.FrondGreen, "leaf"),
                StructureVisualBuilder.CreateColorMaterial(
                    Color.Lerp(StructureVisualBuilder.FrondGreen, StructureVisualBuilder.MeadowGreen, 0.35f), "leaf"),
            };
            var bushMaterials = new[]
            {
                StructureVisualBuilder.CreateColorMaterial(Shade(StructureVisualBuilder.FrondGreen, 0.82f), "leaf"),
                StructureVisualBuilder.CreateColorMaterial(
                    Shade(Color.Lerp(StructureVisualBuilder.FrondGreen, StructureVisualBuilder.MeadowGreen, 0.40f), 0.90f), "leaf"),
            };
            var tuftMaterials = new[]
            {
                StructureVisualBuilder.CreateColorMaterial(Shade(StructureVisualBuilder.MeadowGreen, 0.86f), "leaf"),
                StructureVisualBuilder.CreateColorMaterial(Shade(StructureVisualBuilder.MeadowGreen, 0.98f), "leaf"),
                StructureVisualBuilder.CreateColorMaterial(
                    Shade(Color.Lerp(StructureVisualBuilder.MeadowGreen, StructureVisualBuilder.FrondGreen, 0.35f), 0.90f), "leaf"),
            };

            // (1) 지면 색 구분: 정상부 밝은 풀 / 내륙 풀 / 모래(지형 원색) / 해안 젖은 모래의 4단.
            //     난수 소비 2회(풀밭 경계 위상 2개)로 고정.
            float boundaryPhaseA = rng.NextFloat(0f, Mathf.PI * 2f);
            float boundaryPhaseB = rng.NextFloat(0f, Mathf.PI * 2f);
            BuildGroundCaps(root.transform, islandObject, radius, boundaryPhaseA, boundaryPhaseB);

            // (2) 초목 개수: 반지름에 선형 비례시키되 규모별 상한을 두고, 마지막에 섬 전체 상한을 강제한다.
            //     면적 비례(반지름의 제곱)로 잡으면 특대 섬에서 곧바로 수천 개가 되어 쓸 수 없다.
            //     [B8] 야자수 1그루가 렌더러 5개(줄기1+잎4)에서 13개(줄기3+잎5×2)로 늘었다. 렌더러 총량을
            //     예전과 같은 수준(약 400)으로 묶어두기 위해 그루 수 상한을 42 → 16으로 내려 상쇄한다
            //     (디렉터 지시: "잎 1장당 프리미티브를 늘리려면 나무 수를 줄여서 상쇄해라").
            //     [B9] 덤불 로브와 풀포기를 내장 Sphere(768삼각형)에서 저폴리 메시(20 / 12삼각형)로
            //     교체해 삼각형이 15배 남았다. 남은 예산은 **저폴리가 된 쪽에만** 쓴다 -
            //     덤불 40 → 48, 풀포기 78 → 156. 야자수 16은 그대로다(교체 대상이 아니라 여전히
            //     그루당 204삼각형(B10 프리즘 교체 후) / 렌더러 13개로 여전히 가장 비싸고, 16은
            //     삼각형이 아니라 렌더러 예산을 보고 정한 값이다).
            //     세 상한의 합 16+48+156 = 220 = MaxVegetationInstancesPerIsland로 정확히 맞춰,
            //     아래 트림 블록이 도달 불가 코드가 아니라 살아 있는 가드가 되게 했다.
            //     하한(4/12/20)은 IslandSizeMetrics의 최소 반지름이 50이라 현재 어떤 섬에서도 발동하지
            //     않는다 - 반지름 공식이 바뀔 때를 대비한 방어값이라는 뜻이며, 상한과 달리 "닿는" 값이
            //     아니다(주석이 사실과 어긋나지 않도록 명시해 둔다).
            int palmCount = Mathf.Clamp(Mathf.RoundToInt(radius * 0.12f), 4, 16);
            int bushCount = Mathf.Clamp(Mathf.RoundToInt(radius * 0.24f), 12, 48);
            int tuftCount = Mathf.Clamp(Mathf.RoundToInt(radius * 0.78f), 20, 156);

            int requested = palmCount + bushCount + tuftCount;
            if (requested > MaxVegetationInstancesPerIsland)
            {
                float trim = (float)MaxVegetationInstancesPerIsland / requested;
                palmCount = Mathf.Max(1, Mathf.FloorToInt(palmCount * trim));
                bushCount = Mathf.Max(1, Mathf.FloorToInt(bushCount * trim));
                tuftCount = Mathf.Max(1, Mathf.FloorToInt(tuftCount * trim));
            }

            // 중심부는 비워 둔다. 시작 섬의 경비행기 잔해(+6,-4)/배 작업대(-6,-3)가 중심 근처에 고정
            // 배치되므로, 여기에 야자수가 서면 상호작용 대상이 나무에 파묻혀 보이지 않는다.
            float innerClearRadius = Mathf.Max(12f, radius * 0.12f);

            // 야자수는 균등 산포 대신 "숲(grove)" 단위로 뭉친다. 같은 개수라도 뭉쳐 있으면 밀도가
            // 훨씬 높게 읽히고, 뻥 뚫린 개활지와 그늘진 숲이 생겨 지형이 밋밋하게 보이지 않는다.
            int groveCount = Mathf.Max(2, Mathf.RoundToInt(palmCount / 4f));
            var groveCenters = new Vector3[groveCount];
            for (int i = 0; i < groveCount; i++)
                groveCenters[i] = SampleOnIsland(islandObject.transform.position, rng, innerClearRadius, radius * 0.45f);

            for (int i = 0; i < palmCount; i++)
            {
                // 야자수/덤불의 바깥 한계(둘 다 0.50R)는 풀밭 경계가 가장 안쪽으로 들어왔을 때의
                // 값(GrassBoundaryRadius 최솟값 ≈ 0.51R)에 맞춰 잡았다 - 야자수가 모래 위에 홀로 서는
                // 어색한 그림을 피하기 위해서다(풀포기만 의도적으로 경계를 넘어간다).
                Vector3 center = groveCenters[i % groveCount];
                Vector2 jitter = rng.NextInsideUnitCircle() * 11f;
                Vector3 spot = center + new Vector3(jitter.x, 0f, jitter.y);
                spot = ClampToIslandRing(spot, islandObject.transform.position, innerClearRadius, radius * 0.50f);
                // 머티리얼 선택은 인덱스로만 한다 - rng를 한 번이라도 더 소비하면 같은 worldSeed에서
                // 숲 배치가 통째로 밀려 재현성이 깨진다(파일 상단 [결정성] 주석).
                CreatePalm(root.transform, TerrainSampler.SnapToGround(spot), rng, trunkMaterial,
                    frondMaterials[i % frondMaterials.Length]);
            }

            for (int i = 0; i < bushCount; i++)
            {
                Vector3 spot = SampleOnIsland(islandObject.transform.position, rng, innerClearRadius * 0.8f, radius * 0.50f);
                CreateBush(root.transform, TerrainSampler.SnapToGround(spot), rng, bushMaterials[i % bushMaterials.Length]);
            }

            for (int i = 0; i < tuftCount; i++)
            {
                // 풀포기만 풀밭 경계 밖(모래)까지 나갈 수 있게 둔다 - 해안가에 듬성듬성 난 풀처럼 보여
                // 풀밭과 모래의 경계선이 자로 그은 원처럼 보이지 않게 하는 역할이다.
                Vector3 spot = SampleOnIsland(islandObject.transform.position, rng, innerClearRadius * 0.5f, radius * 0.70f);
                CreateGrassTuft(root.transform, TerrainSampler.SnapToGround(spot), rng, tuftMaterials[i % tuftMaterials.Length]);
            }
        }

        /// <summary>
        /// 지형 메시를 잘라내 만든 "지면 캡" 3장을 지형 바로 위에 덮어, 단색 모래였던 지면에 색 변화를 준다.
        ///
        /// 왜 머티리얼 교체가 아니라 덮개 메시인가: 지형 머티리얼은 WorldMapManager가 만들고(이 배치의
        /// 편집 범위 밖) 섬 전체에 하나만 적용되므로, 그것만으로는 해안과 내륙을 나눌 수 없다. 셰이더를
        /// 새로 만들 수도 없다. 그래서 WorldMapManager가 얕은 물 띠(ShorelineBand)를 별도 고리 메시로
        /// 해결한 것과 정확히 같은 방식 - 별도 메시 + 별도 머티리얼 - 을 그대로 따른다.
        ///
        /// 3장의 구성(안쪽 → 바깥쪽):
        ///   HighlandCap  : 섬 정상부의 밝은 풀. 고도(정점 y)로 잘라내 능선이 색으로 드러나게 한다.
        ///   GrassCap     : 내륙 풀밭. 경계는 각도에 따라 출렁여 자로 그은 원이 되지 않는다.
        ///   WetSandCap   : 해안의 젖은 모래 띠. 모래 → 젖은 모래 → 얕은 물 띠(ShorelineBand)로 이어진다.
        /// 이 3장 사이에 노출되는 원래 지형색(Island Sand)이 네 번째 톤 역할을 해, 지면이 총 4단으로 읽힌다.
        /// </summary>
        private static void BuildGroundCaps(Transform surfaceRoot, GameObject islandObject, float radius,
            float phaseA, float phaseB)
        {
            var sourceFilter = islandObject.GetComponent<MeshFilter>();
            if (sourceFilter == null || sourceFilter.sharedMesh == null)
                return; // islandPlaceholderPrefab을 쓰는 구성이면 지형 메시를 알 수 없으므로 조용히 건너뛴다.

            Mesh source = sourceFilter.sharedMesh;

            // 지형 최대 높이는 WorldMapManager.terrainMaxHeight(인스펙터 값, 실기에서 2.5 → 8로 상향)라
            // 코드 상수로 가정하면 안 된다. 메시 바운즈에서 읽어 항상 실제 지형에 맞춘다.
            float peakHeight = Mathf.Max(0.01f, source.bounds.max.y);

            // 캡을 띄우는 높이. 8cm 고정이었는데, terrainMaxHeight가 3배 넘게 커지면 같은 8cm도 상대적으로
            // 얇아져 원거리에서 깊이 정밀도에 눌릴 수 있다. 지형 기복에 비례시키되 하한 8cm를 유지한다.
            // (주의: 지형은 y=f(x,z) 단일값 높이장이므로 캡을 +Y로 평행이동하면 경사가 아무리 급해도
            //  절대 지형에 파묻히지 않는다 - 경사면에서 수직 간격이 cos(경사각)배로 줄 뿐이다.
            //  실기에서 풀밭이 안 보였던 원인은 이 오프셋이 아니라 캡 색이었다. 아래 GrassCap 주석 참고.)
            float capOffset = Mathf.Max(0.08f, peakHeight * 0.02f);

            // [B9 이음매 사고 원인] 실기에서 "지면 한가운데에 직선 경계의 사각형 얼룩"이 보고됐다.
            // 코드로 특정한 원인은 아래 HighlandCap의 고도 컷이다. 근거(값은 terrainMaxHeight=8 기준
            // 실제 메시를 재현해 계산):
            //   · 지형 높이 = maxHeight·cos(t·π/2) + perlin(x·0.05, z·0.05)·2·(1-t)
            //   · 정상부(고도 컷이 걸리는 t≤0.33 구간)에서 코사인 항의 낙차는 0.92~1.07m인데
            //     펄린 항의 진폭은 1.33~1.38m다 → 컷 등고선을 결정하는 것은 반지름이 아니라 펄린이다.
            //   · 그 펄린은 noiseScale 0.05, 즉 격자 한 칸이 정확히 20m인 축 정렬(axis-aligned) 격자다.
            //     시작 섬(반지름 50)에서 캡 지름은 33m = 격자 1.7칸 → 캡이 사실상 펄린 격자 한 칸이 되어
            //     경계가 X/Z축에 나란한 직선으로 잘린다. 게다가 캡 중심은 항상 섬 중심 = 플레이어
            //     시작 지점이라 그 직선이 화면 정중앙 지면에 온다. 신고 내용과 정확히 일치한다.
            // 배제한 후보(추측이 아니라 값으로 확인):
            //   (a) z-파이팅: 캡 오프셋은 Grass 0.165 / Highland 0.225 / WetSand 0.082m로 6~8cm 벌어져
            //       있고, 겹치는 쌍은 Grass↔Highland 하나뿐이다. reversed-Z 깊이에서 6cm는 100m
            //       거리에서도 밀리미터 단위 여유가 있어 지글거림이 나올 수 없다. 실제 증상도 "지글"이
            //       아니라 "고정된 직선 경계"였다.
            //   (c) 텍스처 타일링 경계: GrassCap과 HighlandCap은 UV 소스(지형 메시)와 타일 배수
            //       (radius×0.75)가 완전히 동일해 서로 어긋날 수 없고, 타일 경계라면 얼룩 하나가 아니라
            //       섬 전체에 2.7m 간격으로 반복돼야 한다.
            // 조치: 고도 컷을 "삼각형 단위 디더"로 흩뜨려 연속된 직선 경계가 아예 생길 수 없게 하고,
            //       밝기 단차도 1.18배 → 1.10배로 낮춘다. 원형 경계(GrassCap/WetSandCap)에도 같은
            //       디더를 얇게 걸어 네 캡의 경계 처리를 한 방식으로 통일한다.

            // [B10 "각진 삼각형 얼룩" 후속] 직선 이음매는 위 디더로 사라졌지만, 실기에 **옅은 각진
            // 삼각형 얼룩**이 남았다. 남은 원인은 경계가 아니라 **톤 자체**다. 값으로 특정한 근거:
            //   · ToneIndex의 삼각형 단위 해시 디더 진폭이 0.55(=±0.275)였는데 톤 한 칸의 폭은
            //     1/toneCount = 0.333이다. 즉 디더가 칸 폭의 165%라, 저주파 펄린이 만들려던 "넓은
            //     얼룩"이 완전히 묻히고 **모든 삼각형이 사실상 무작위로 3톤에 배정**됐다.
            //     경계만 점묘가 되는 것이 아니라 캡 전체가 소금·후추 노이즈가 된 것이다.
            //   · 그 3톤의 차이가 명도(±6%)라 이웃 삼각형 사이 최대 단차가 1.06/0.94 = **12.8%**다.
            //     넓고 평평하게 조명된 면에서 12.8% 명도 단차는 육안 식별 한계(수 %)를 크게 넘는다.
            //     삼각형 하나하나가 도드라져 보이는 이유가 이것이고, 평면 셰이딩이 아니라 톤 배정이
            //     범인이다(캡 메시는 RemapVertex가 정점을 공유해 스무스 셰이딩된다 - 위 주석 참고).
            // 정점 색 검토(디렉터 요청): **불가능하다.** URP Lit 셰이더는 정점 색 입력 자체가 없고
            //   (Attributes에 color 시맨틱이 없다), 이 프로젝트는 셰이더/Shader Graph 에셋이 0개라
            //   (AGENT_BRIEF 1장) 정점 색을 읽는 셰이더를 만들 수단이 없다. 그래서 "서브메시를 늘리지
            //   않고 정점 색으로 부드러운 그라데이션"은 이 파이프라인에서 성립하지 않는다.
            // 조치(서브메시 개수는 그대로 3/1/2, 드로우콜 변화 0):
            //   (1) 톤 변주를 명도가 아니라 **색상**으로 준다. ToneVariant가 상대휘도를 기준색에 정확히
            //       고정하므로 톤 사이 명도 단차가 **0%**가 된다 - 삼각형이 밝기 얼룩으로 보일 수가 없다.
            //       사람 눈은 색차의 공간 해상도가 명도보다 훨씬 낮아(크로마 서브샘플링이 성립하는 이유)
            //       같은 크기의 변주라도 삼각형 단위에서는 거의 보이지 않고 넓은 패치에서만 읽힌다.
            //       이 파일이 잎 머티리얼에 이미 쓰고 있는 규칙("변주는 명도가 아니라 색상으로")과 같다.
            //   (2) ToneIndex의 삼각형 디더를 0.55 → 0.20으로 낮춰, 톤 배정이 저주파 펄린(격자 ≈29m)에
            //       지배되게 되돌린다. 패치 경계 근처 삼각형만 섞이므로 원래 의도였던 "경계만 점묘"가 된다.
            //   (3) HighlandCap의 명도 단차 1.10배(=10%)도 같은 이유로 1.05배로 낮추고, 부족해진 구분은
            //       색상(Frond Green 쪽으로 0.55) 으로 채운다. 능선은 조명 자체가 달라 5%면 충분히 읽힌다.

            // 캡 경계를 흩뜨리는 폭. 삼각형 하나(2~5m)보다 넓은 띠에 걸쳐 포함/제외가 섞이게 만들어
            // 경계가 선이 아니라 점묘(stipple)로 읽히게 하는 것이 목적이다.
            float highlandDither = Mathf.Max(0.4f, peakHeight * 0.13f);   // 고도 기준 ±0.55m ≈ 평면상 ±6m
            float radialDither = radius * 0.05f;                          // 반지름 기준 ±2.5%R

            // 내륙 풀밭. 예전 색은 Shade(PalmFiber, 0.82) = #79733E로, Island Sand(#C2B280)와 색상각이
            // 각각 54°/45°로 9°밖에 차이 나지 않는 같은 황토 계열에 휘도만 1.58배 낮은 값이었다.
            // 그래서 실기에서 "풀밭"이 아니라 "그늘진 모래"로 읽혀 캡이 있는지조차 확인되지 않았다.
            // Meadow Green(#8AA84F, 색상각 80°)으로 바꿔 색상 자체로 구분되게 한다.
            // toneCount 3: 같은 초록 한 장이 섬을 덮던 문제(아래 BuildCapLayer 주석) 해소용.
            BuildCapLayer(surfaceRoot, source, radius, "GrassCap", StructureVisualBuilder.MeadowGreen,
                capOffset, radius * 0.75f, "leaf",
                (centroid, distance, angle) => distance <=
                    GrassBoundaryRadius(angle, radius, phaseA, phaseB) + (Hash01(centroid) - 0.5f) * radialDither,
                // 3톤 × 최대 0.50 혼합 = Meadow Green(색상각 80°) → 약 88° 사이의 색조 변주.
                // 상대휘도는 세 톤 모두 0.609로 동일하다(ToneVariant) → 명도 단차 0%.
                3, 0.50f, StructureVisualBuilder.FrondGreen);

            // 정상부의 밝은 풀. 반지름이 아니라 고도로 잘라내, 지형 굴곡(펄린 노이즈로 생긴 등성이)이
            // 그대로 색 경계가 된다 - 원형으로 잘라내면 또 하나의 완벽한 동심원이 생겨 인공적으로 보인다.
            // 0.86은 코사인 지형에서 대략 0.34R 안쪽(정상부)에 해당한다(cos(0.34·π/2) ≈ 0.86).
            // 디더 항이 이 배치의 핵심 수정이다 - 없으면 컷이 펄린 격자(20m 축 정렬)를 그대로 따라간다.
            BuildCapLayer(surfaceRoot, source, radius, "HighlandCap",
                Shade(ToneVariant(StructureVisualBuilder.MeadowGreen, StructureVisualBuilder.FrondGreen, 0.55f), 1.05f),
                capOffset + 0.06f, radius * 0.75f, "leaf",
                (centroid, distance, angle) =>
                    centroid.y >= peakHeight * 0.86f + (Hash01(centroid) - 0.5f) * highlandDither
                    && distance <= GrassBoundaryRadius(angle, radius, phaseA, phaseB));

            // 해안의 젖은 모래. 바깥 한계 0.955R은 ShorelineBand(0.95R부터 시작하는 반투명 물 띠)와
            // 겹치는 폭을 최소화하기 위한 값이라 그대로 두고(디더를 걸면 물 띠와 어긋난다),
            // 안쪽(마른 모래와 만나는) 경계에만 디더를 건다.
            BuildCapLayer(surfaceRoot, source, radius, "WetSandCap", Shade(StructureVisualBuilder.IslandSand, 0.80f),
                capOffset * 0.5f, radius * 1.5f, "sand",
                (centroid, distance, angle) =>
                    distance >= radius * 0.84f + (Hash01(centroid) - 0.5f) * radialDither * 0.8f
                    && distance <= radius * 0.955f,
                2, 0.22f, StructureVisualBuilder.WeatheredStone);
        }

        /// <summary>
        /// 지형 메시에서 조건에 맞는 삼각형만 골라내 덮개 메시 1장을 만든다.
        ///
        /// 지형 메시의 정점을 그대로 복사해 쓰기 때문에 굴곡이 100% 일치한다(링/세그먼트 개수 계산식을
        /// WorldMapManager와 중복 정의할 필요가 없다 - 그 계산식이 나중에 바뀌어도 자동으로 따라간다).
        /// 콜라이더는 붙이지 않는다(플레이어는 원래 지형 콜라이더 위를 걷는다).
        /// </summary>
        /// <param name="selector">(삼각형 무게중심, 중심축까지의 XZ 거리, 각도) → 이 삼각형을 포함할지.</param>
        /// <param name="toneCount">
        /// 캡 하나를 서브메시 몇 장으로 쪼개 서로 다른 밝기로 칠할지(1이면 기존과 100% 동일한 단색).
        ///
        /// [B9 "지형 색이 한 톤"] 캡 1장 = 단색 1개라, 같은 초록이 반지름 200m를 통째로 덮어 실기에서
        /// 평평한 색판으로 보였다. 정점 색(URP Lit은 정점 색을 읽지 않는다)도, 캡 메시 추가(드로우콜과
        /// 오브젝트가 캡마다 늘어난다)도 쓰지 않고 톤을 늘릴 수 있는 방법은 하나뿐이다 - 이미 만든 캡
        /// 메시를 서브메시로 쪼개 머티리얼 슬롯만 늘리는 것. GameObject·MeshFilter·정점은 그대로 1벌이고
        /// 늘어나는 것은 드로우콜 (toneCount-1)개뿐이다(섬당 총 +3). 초목 프리미티브 상한 180과는
        /// 무관하다 - 프리미티브를 하나도 추가하지 않는다.
        /// </param>
        /// <param name="toneSpread">
        /// 마지막 톤이 toneShift 쪽으로 얼마나 섞이는지(0~1). [B10] 예전에는 "밝기 폭(±비율)"이었는데,
        /// 명도 변주가 삼각형 단위 얼룩의 직접 원인이라 **색상 혼합 비율**로 의미를 바꿨다.
        /// 상대휘도는 ToneVariant가 기준색에 고정하므로 이 값이 아무리 커도 명도 단차는 0이다.
        /// </param>
        /// <param name="toneShift">톤이 섞여 들어갈 상대 색. 비우면 변주 없음(단색)과 같다.</param>
        private static void BuildCapLayer(Transform surfaceRoot, Mesh source, float radius, string name,
            Color color, float yOffset, float textureTiling, string textureName,
            System.Func<Vector3, float, float, bool> selector, int toneCount = 1, float toneSpread = 0.30f,
            Color? toneShift = null)
        {
            Vector3[] sourceVertices = source.vertices;
            int[] sourceTriangles = source.triangles;
            Vector2[] sourceUvs = source.uv;
            bool hasSourceUv = sourceUvs != null && sourceUvs.Length == sourceVertices.Length;

            toneCount = Mathf.Clamp(toneCount, 1, 4);

            var remap = new Dictionary<int, int>(sourceVertices.Length);
            var vertices = new List<Vector3>();
            var uvs = new List<Vector2>();
            var toneTriangles = new List<int>[toneCount];
            for (int i = 0; i < toneCount; i++)
                toneTriangles[i] = new List<int>();

            int selectedCount = 0;
            for (int t = 0; t + 2 < sourceTriangles.Length; t += 3)
            {
                int i0 = sourceTriangles[t];
                int i1 = sourceTriangles[t + 1];
                int i2 = sourceTriangles[t + 2];

                Vector3 centroid = (sourceVertices[i0] + sourceVertices[i1] + sourceVertices[i2]) / 3f;
                float distance = new Vector2(centroid.x, centroid.z).magnitude;
                float angle = Mathf.Atan2(centroid.z, centroid.x);
                if (!selector(centroid, distance, angle))
                    continue;

                var bucket = toneTriangles[ToneIndex(centroid, toneCount)];
                bucket.Add(RemapVertex(i0, remap, sourceVertices, sourceUvs, hasSourceUv, radius, vertices, uvs));
                bucket.Add(RemapVertex(i1, remap, sourceVertices, sourceUvs, hasSourceUv, radius, vertices, uvs));
                bucket.Add(RemapVertex(i2, remap, sourceVertices, sourceUvs, hasSourceUv, radius, vertices, uvs));
                selectedCount++;
            }

            if (selectedCount == 0)
                return;

            // 비어 있는 톤은 서브메시를 만들지 않는다(빈 서브메시는 드로우콜만 소모한다).
            var usedTones = new List<int>(toneCount);
            for (int i = 0; i < toneCount; i++)
            {
                if (toneTriangles[i].Count > 0)
                    usedTones.Add(i);
            }

            var mesh = new Mesh();
            mesh.name = $"Island{name}";
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uvs);
            mesh.subMeshCount = usedTones.Count;
            for (int s = 0; s < usedTones.Count; s++)
                mesh.SetTriangles(toneTriangles[usedTones[s]], s);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            var go = new GameObject(name);
            go.transform.SetParent(surfaceRoot, false);
            // 지형과 정확히 같은 높이면 z-파이팅으로 지글거린다. 캡마다 다른 오프셋을 줘서 캡끼리도
            // 겹치는 구간(HighlandCap ⊂ GrassCap)에서 깊이 충돌이 나지 않게 한다.
            go.transform.localPosition = new Vector3(0f, yOffset, 0f);

            go.AddComponent<MeshFilter>().sharedMesh = mesh;

            var renderer = go.AddComponent<MeshRenderer>();
            var materials = new Material[usedTones.Count];
            for (int s = 0; s < usedTones.Count; s++)
            {
                // 톤 0 → 기준색 그대로, 마지막 톤 → toneShift 쪽으로 toneSpread만큼(toneCount 1이면 0).
                // 상대휘도는 전 톤이 동일하다(ToneVariant) - 이웃 삼각형 사이 명도 단차 0%.
                float mix = toneCount <= 1
                    ? 0f
                    : usedTones[s] / (float)(toneCount - 1) * toneSpread;
                var material = StructureVisualBuilder.CreateColorMaterial(
                    ToneVariant(color, toneShift ?? color, mix), textureName);
                // UV가 섬 전체에 0~1로 정규화돼 있어(GenerateIslandMesh) 타일 반복을 반지름에 비례시키지
                // 않으면 큰 섬에서 잎 무늬 한 칸이 수십 미터로 늘어나 흐릿한 단색이 된다.
                // WorldMapManager.CreateDefaultTerrainMaterial의 모래 타일링과 같은 계산 방식이다.
                material.mainTextureScale = new Vector2(textureTiling, textureTiling);
                // 톤마다 타일 위상을 어긋나게 해, 같은 그레인 무늬가 톤 경계에서 이어지며
                // "색만 다른 같은 얼룩"으로 보이지 않게 한다.
                material.mainTextureOffset = new Vector2(usedTones[s] * 0.37f, usedTones[s] * 0.19f);
                materials[s] = material;
            }
            renderer.sharedMaterials = materials;

            // 지면에 몇 cm 떠 있는 덮개라 그림자를 드리우면 자기 그림자로 얼룩진다. 받기만 한다
            // (야자수 그림자는 풀밭 위에 정상적으로 떨어져야 한다).
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = true;
        }

        /// <summary>
        /// 원본 지형 정점을 캡 메시로 옮기고(중복 없이) 새 인덱스를 돌려준다.
        /// </summary>
        private static int RemapVertex(int sourceIndex, Dictionary<int, int> remap, Vector3[] sourceVertices,
            Vector2[] sourceUvs, bool hasSourceUv, float radius, List<Vector3> vertices, List<Vector2> uvs)
        {
            if (remap.TryGetValue(sourceIndex, out int existing))
                return existing;

            Vector3 v = sourceVertices[sourceIndex];
            int newIndex = vertices.Count;
            vertices.Add(v);
            uvs.Add(hasSourceUv
                ? sourceUvs[sourceIndex]
                : new Vector2(v.x / radius * 0.5f + 0.5f, v.z / radius * 0.5f + 0.5f));
            remap[sourceIndex] = newIndex;
            return newIndex;
        }

        /// <summary>
        /// 풀밭(내륙)과 모래(해안)의 경계 반지름. 기본 0.62R에 서로 다른 주기의 사인 둘을 더해
        /// 각도에 따라 대략 0.51R ~ 0.73R 사이에서 출렁이게 한다(완전한 원 경계 회피).
        /// </summary>
        private static float GrassBoundaryRadius(float angle, float radius, float phaseA, float phaseB)
        {
            float ratio = 0.62f
                + 0.07f * Mathf.Sin(angle * 3f + phaseA)
                + 0.04f * Mathf.Sin(angle * 5f + phaseB);
            return radius * ratio;
        }

        /// <summary>
        /// 섬 중심 기준 [minRadius, maxRadius] 고리 안의 한 점을 뽑는다(면적 균등).
        /// 난수 소비는 호출당 항상 2회(NextInsideUnitCircle)로 고정된다.
        /// </summary>
        private static Vector3 SampleOnIsland(Vector3 islandCenter, System.Random rng, float minRadius, float maxRadius)
        {
            Vector2 unit = rng.NextInsideUnitCircle();
            float length = Mathf.Max(0.0001f, unit.magnitude);
            float distance = Mathf.Lerp(minRadius, maxRadius, length);
            Vector2 direction = unit / length;
            return islandCenter + new Vector3(direction.x * distance, 0f, direction.y * distance);
        }

        /// <summary>지정한 점을 섬 중심 기준 [minRadius, maxRadius] 고리 안으로 밀어 넣는다.</summary>
        private static Vector3 ClampToIslandRing(Vector3 point, Vector3 islandCenter, float minRadius, float maxRadius)
        {
            Vector3 offset = point - islandCenter;
            offset.y = 0f;
            float distance = offset.magnitude;
            if (distance < 0.0001f)
                return islandCenter + new Vector3(minRadius, 0f, 0f);

            float clamped = Mathf.Clamp(distance, minRadius, maxRadius);
            return islandCenter + offset / distance * clamped;
        }

        /// <summary>야자수 1그루를 이루는 줄기 마디 수. 마디마다 기울기를 조금씩 더해 휜 기둥을 만든다.</summary>
        private const int PalmTrunkSegments = 3;

        /// <summary>
        /// 야자수 줄기 프리즘의 각 수. 내장 Cylinder(20각, 마디당 80삼각형)를 대체한다.
        ///
        /// [B10] 6각(마디당 20)이 아니라 **8각(마디당 28)** 으로 정했다. 직전 배치에서 스스로 올린 우려
        /// ("줄기는 5m 이내 근접 관찰 대상이라 각이 눈에 띌 수 있다")를 값으로 검증한 결과다.
        ///   · 실루엣 오차: 정n각형의 평균 폭은 Cauchy 공식으로 2nR·sin(π/n)/π다. 원(2R) 대비
        ///     20각 99.6% / 8각 97.5% / 6각 95.5% — 즉 회전에 따라 굵기가 출렁이는 폭이
        ///     8각 7.6% vs 6각 13.4%다. 굵기 인지 한계(약 5%)를 8각은 거의 넘지 않고 6각은 확실히 넘는다.
        ///   · 능선 꺾임각: 6각은 면 사이 법선이 60° 꺾이고 8각은 45°다. 지향성 광원 하나뿐인
        ///     이 씬에서 60° 꺾임은 이웃 면 사이 밝기가 최대 2배 가까이 벌어져, 지금 지면에서 고치고 있는
        ///     "각진 얼룩"과 같은 실패를 굵기 0.3m짜리 근접 오브젝트에서 재현하게 된다.
        ///   · 비용 차이는 그루당 24삼각형(마디 3개 × 8), 특대 섬 16그루 기준 384삼각형 = 교체 전
        ///     총량의 3.7%뿐이다. 가장 자주 근접 관찰되는 오브젝트의 리스크를 그 값에 사는 것이 맞다.
        /// 옆면은 **스무스 셰이딩**(법선을 반경 방향으로 직접 지정)이라 내장 Cylinder와 음영이 사실상
        /// 같다. 덤불/풀의 평면 셰이딩과 달리 여기서 각을 세우지 않는 이유는 위 능선 꺾임각 근거와 같다.
        /// </summary>
        private const int PalmTrunkSides = 8;

        /// <summary>야자수 1그루의 잎 장수. 잎 1장은 안쪽/바깥쪽 2마디로 꺾여 아래로 늘어진다.</summary>
        private const int PalmFrondCount = 5;

        /// <summary>
        /// 야자수 한 그루(줄기 8각 프리즘 3 + 잎 박스 5×2 = 렌더러 13개 / 204삼각형)를 만든다.
        ///
        /// [B8 형태 개선] 이전 형태는 곧은 원기둥 1개 + 방사형으로 뻗은 평평한 판자 4개라서, 실기에서
        /// "가는 장대에 판자를 붙인 것"으로 보이고 야자수로 읽히지 않았다. 진짜 야자수의 실루엣을 만드는
        /// 요소는 두 가지뿐인데 둘 다 없었다:
        ///   (a) 기둥이 곧지 않고 위로 갈수록 한쪽으로 휘며 가늘어진다  → 마디 3개를 각도를 누적시켜 쌓는다.
        ///   (b) 잎이 밑동에서 위로 뻗다가 중간에서 꺾여 아래로 늘어진다 → 잎 1장을 2마디로 꺾는다.
        /// 통짜 기울기(예전 방식: 뿌리 오브젝트 자체를 tilt)로는 (a)가 안 된다 - 기둥 전체가 그대로
        /// 기울어져 밑동이 지면에서 뜨기만 한다. 그래서 뿌리에는 yaw만 주고 휨은 마디 누적으로 만든다.
        ///
        /// 뿌리 오브젝트의 스케일은 항상 1(균등)로 두고 회전만 준다 - 부모 스케일이 비균일한 상태에서
        /// 회전한 자식을 두면 전단(shear)으로 찌그러진다(CreatureVisualBuilder/StructureVisualBuilder
        /// 주석에 반복해서 나오는 이 프로젝트의 기존 함정).
        /// </summary>
        private static void CreatePalm(Transform parent, Vector3 groundPosition, System.Random rng,
            Material trunkMaterial, Material frondMaterial)
        {
            // 굵기: 예전 0.16~0.26m는 5~7m 높이에 대해 너무 가늘어 장대로 보였다. 밑동을 0.26~0.38m로
            // 올리고 위로 갈수록 62%까지 가늘어지게 해서 "굵은 밑동 → 가는 목"의 야자수 비례를 만든다.
            //
            // [B10 호출부 스케일 재검토 — 형태 교체와 함께 반드시 본다는 규칙]
            // 여기 값은 **외접 반지름**(정점이 놓이는 반지름)이다. 내장 Cylinder도 정점이 반지름 0.5에
            // 놓이므로 스케일의 의미 자체는 그대로지만, 화면에 보이는 굵기는 외접 반지름이 아니라
            // **평균 폭**(Cauchy: 2nR·sin(π/n)/π)이다. 20각 0.996·2R → 8각 0.9745·2R 이므로 같은
            // baseRadius를 그대로 넣으면 줄기가 **2.2% 가늘어 보인다**. 그래서 범위를 0.9958/0.9745
            // = 1.0219배 한 0.266~0.388로 올려 교체 전후 평균 굵기를 일치시킨다.
            // (참고: 6각이었다면 보정이 4.3%로 인지 한계에 걸린다 - 8각을 고른 또 하나의 이유다.)
            // 난수 소비는 그대로 1회다. NextFloat(min,max)는 범위와 무관하게 스트림을 한 번만 당기므로
            // 상·하한을 바꿔도 같은 worldSeed에서 이후 배치가 밀리지 않는다(파일 상단 [결정성] 전제 유지).
            float height = rng.NextFloat(4.6f, 7.6f);
            float baseRadius = rng.NextFloat(0.266f, 0.388f);
            float leanDirection = rng.NextFloat(0f, 360f);   // 어느 쪽으로 휘는가
            float leanStart = rng.NextFloat(1f, 5f);         // 밑동 마디의 기울기(거의 수직)
            float leanStep = rng.NextFloat(4f, 9f);          // 마디마다 더해지는 기울기
            float frondLength = rng.NextFloat(2.2f, 3.4f);
            float baseYaw = rng.NextFloat(0f, 360f);

            var palm = new GameObject("Veg_Palm");
            palm.transform.SetParent(parent, false);
            palm.transform.position = groundPosition;
            // 뿌리는 yaw만. 휨은 아래 마디 누적이 만들기 때문에 밑동은 항상 지면에 수직으로 박힌다.
            palm.transform.rotation = Quaternion.Euler(0f, leanDirection, 0f);

            float segmentLength = height / PalmTrunkSegments;
            Vector3 cursor = Vector3.zero;      // 지금까지 쌓아 올린 줄기 끝(로컬)
            float lean = 0f;

            for (int i = 0; i < PalmTrunkSegments; i++)
            {
                lean = leanStart + i * leanStep;
                // X축 회전 a는 원기둥의 축(+Y)을 (0, cos a, sin a)로 눕힌다. 마디를 그 방향으로 쌓는다.
                Quaternion rotation = Quaternion.Euler(lean, 0f, 0f);
                Vector3 direction = rotation * Vector3.up;
                float t = (i + 0.5f) / PalmTrunkSegments;
                float segmentRadius = Mathf.Lerp(baseRadius, baseRadius * 0.62f, t);

                // 프리즘 메시는 내장 Cylinder와 동일한 로컬 규격(반지름 0.5·높이 2)이라 아래 스케일 식이
                // 그대로 유효하다. localScale.y에 "마디 길이의 절반"을 넣고, 마디 사이가 벌어져 보이지
                // 않게 길이를 6% 겹쳐 쌓는다.
                CreatePart(palm.transform, $"Veg_PalmTrunk{i}", GetPalmTrunkPrismMesh(),
                    cursor + direction * (segmentLength * 0.5f),
                    new Vector3(segmentRadius * 2f, segmentLength * 0.53f, segmentRadius * 2f),
                    rotation, trunkMaterial);

                cursor += direction * segmentLength;
            }

            // 잎은 줄기 끝(cursor)에서 뻗는다. 줄기가 휜 만큼 왕관도 따라 기울어져 있어야 자연스럽다.
            Quaternion crownTilt = Quaternion.Euler(lean * 0.6f, 0f, 0f);

            for (int i = 0; i < PalmFrondCount; i++)
            {
                float yaw = baseYaw + i * (360f / PalmFrondCount) + rng.NextFloat(-14f, 14f);
                // 안쪽 마디: 살짝 위로 솟았다가(음수 피치 = 위쪽) 수평 근처까지.
                float innerPitch = rng.NextFloat(-16f, 4f);
                // 바깥 마디: 안쪽에서 40~68° 더 꺾여 아래로 늘어진다. 이 꺾임이 야자수 실루엣의 핵심이다.
                float outerPitch = innerPitch + rng.NextFloat(40f, 68f);

                float innerLength = frondLength * 0.44f;
                float outerLength = frondLength * 0.64f;

                Quaternion yawRotation = Quaternion.Euler(0f, yaw, 0f);
                Quaternion innerRotation = crownTilt * yawRotation * Quaternion.Euler(innerPitch, 0f, 0f);
                Quaternion outerRotation = crownTilt * yawRotation * Quaternion.Euler(outerPitch, 0f, 0f);

                // 잎 박스의 로컬 +Z가 잎 길이 방향이다. 회전시킨 방향으로 길이의 절반만큼 밀어
                // 밑동이 줄기 꼭대기(또는 앞 마디 끝)에 붙게 한다.
                Vector3 innerCenter = cursor + innerRotation * new Vector3(0f, 0f, innerLength * 0.5f);
                Vector3 joint = cursor + innerRotation * new Vector3(0f, 0f, innerLength);
                Vector3 outerCenter = joint + outerRotation * new Vector3(0f, 0f, outerLength * 0.5f);

                CreatePart(palm.transform, $"Veg_PalmFrond{i}A", PrimitiveType.Cube,
                    innerCenter, new Vector3(0.42f, 0.07f, innerLength), innerRotation, frondMaterial);
                // 바깥 마디는 폭/두께를 줄여 끝으로 갈수록 가늘어지게 한다(잎 끝이 뭉툭하면 판자로 보인다).
                CreatePart(palm.transform, $"Veg_PalmFrond{i}B", PrimitiveType.Cube,
                    outerCenter, new Vector3(0.28f, 0.05f, outerLength), outerRotation, frondMaterial);
            }
        }

        /// <summary>
        /// 덤불 한 개(눌린 구체 3개, 렌더러 3개). 야자수보다 낮아 시야를 막지 않는 높이로 유지한다.
        ///
        /// [B8] 예전에는 매끈한 타원 2개가 거의 동심으로 겹쳐 있어 실루엣이 하나의 매끈한 돌덩이였고,
        /// 돌조각 자원 노드와 구분되지 않았다. 자연물 중 "덤불"만 가진 신호는 (a) 위쪽이 울퉁불퉁하게
        /// 튀어나온 여러 덩이, (b) 폭이 높이보다 확실히 큰 납작한 비례 두 가지다. 로브를 3개로 늘리고
        /// 각 로브를 서로 다른 방향으로 기울여 윤곽선이 매끈한 곡선이 되지 않게 만든다.
        /// (돌은 기울지 않은 단일 덩어리다 - 색이 초록으로 바뀐 것과 합쳐 20m 밖에서도 갈린다.)
        ///
        /// [B9 저폴리 교체] 로브를 내장 Sphere(768삼각형)에서 정이십면체(20삼각형)로 바꿨다. 위 B8 실루엣
        /// 규칙 - 기울인 로브 3개 · 폭 &gt;&gt; 높이 - 은 하나도 바꾸지 않는다(스케일·회전·오프셋 그대로).
        /// 오히려 각진 면이 생겨 "매끈한 돌덩이"와의 구분이 강해진다. 난수 소비 순서·횟수도 그대로다.
        /// </summary>
        private static void CreateBush(Transform parent, Vector3 groundPosition, System.Random rng, Material material)
        {
            float width = rng.NextFloat(1.3f, 2.2f);
            float height = rng.NextFloat(0.6f, 1.0f); // 폭 대비 확실히 낮게 - 납작한 비례가 돌과의 1차 구분
            float yaw = rng.NextFloat(0f, 360f);

            var bush = new GameObject("Veg_Bush");
            bush.transform.SetParent(parent, false);
            bush.transform.position = groundPosition;
            bush.transform.rotation = Quaternion.Euler(0f, yaw, 0f);

            Mesh lobeMesh = GetLowPolyLobeMesh();
            CreatePart(bush.transform, "Veg_BushMain", lobeMesh,
                new Vector3(0f, height * 0.42f, 0f), new Vector3(width, height, width * 0.9f),
                Quaternion.Euler(0f, 0f, rng.NextFloat(-10f, 10f)), material);

            for (int i = 0; i < 2; i++)
            {
                Vector2 offset = rng.NextInsideUnitCircle() * (width * 0.34f);
                float lobeScale = rng.NextFloat(0.50f, 0.76f);
                // 로브를 본체보다 살짝 위로 올려 윤곽선 위쪽에 혹이 생기게 한다(돌은 이런 혹이 없다).
                CreatePart(bush.transform, $"Veg_BushLobe{i}", lobeMesh,
                    new Vector3(offset.x, height * rng.NextFloat(0.55f, 0.80f), offset.y),
                    new Vector3(width * lobeScale, height * lobeScale * 1.15f, width * lobeScale),
                    Quaternion.Euler(rng.NextFloat(-22f, 22f), 0f, rng.NextFloat(-22f, 22f)), material);
            }
        }

        /// <summary>
        /// 풀포기 한 개(잎 3장 부채꼴, 12삼각형). 가장 저렴한 파츠라 개수가 제일 많아 렌더러 1개를 유지한다.
        /// [B8] 두께를 폭의 80% → 30%로 줄이고 좌우로 살짝 눕혀, 위에서 봐도 "납작한 덩어리"가 아니라
        /// 풀잎 다발이 서 있는 것처럼 보이게 한다.
        /// [B9] 그 "눌린 구"(768삼각형)를 같은 규격의 잎 부채꼴 메시(12삼각형)로 교체했다. 눌린 구가
        /// 화면에서 실제로 하던 일이 "위로 솟은 납작한 잎 다발"이라 실루엣은 사실상 동일하고, 끝이
        /// 뾰족해져 오히려 풀로 더 잘 읽힌다. 스케일·회전·위치 계산과 난수 소비는 한 줄도 바뀌지 않았다.
        /// </summary>
        private static void CreateGrassTuft(Transform parent, Vector3 groundPosition, System.Random rng, Material material)
        {
            // [B9 디렉터 수정] 폭 0.7~1.5m 는 풀포기가 아니라 관목 크기였다(플레이어 몸통보다 넓다).
            // 이 값은 이전에 "눌린 구"였을 때 잡은 것인데, 잎 판으로 바뀌면서 그 크기가 그대로 벽이 됐다.
            // 실제 풀포기 비례로 되돌린다.
            float width = rng.NextFloat(0.32f, 0.62f);
            float height = rng.NextFloat(0.26f, 0.46f);
            float yaw = rng.NextFloat(0f, 360f);
            float lean = rng.NextFloat(-14f, 14f);

            var tuft = CreatePart(parent, "Veg_GrassTuft", GetGrassBladeMesh(),
                Vector3.zero, new Vector3(width, height, width * 0.30f),
                Quaternion.Euler(0f, yaw, lean), material);
            tuft.transform.position = groundPosition + Vector3.up * (height * 0.35f);

            // 풀포기는 5m 밖에서 그림자가 보이지 않는데 개수만 많아, 그림자 드리우기를 끈다
            // (ArtDirection 2장 "폴리곤을 아낄 곳은 5m 밖에서 안 보이는 디테일").
            var renderer = tuft.GetComponent<MeshRenderer>();
            if (renderer != null)
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        /// <summary>
        /// 콜라이더가 전혀 붙지 않는 시각 전용 파츠를 만든다.
        /// StructureVisualBuilder.CreateVisualPart는 GameObject.CreatePrimitive로 만든 뒤 콜라이더를
        /// Object.Destroy하는데, Destroy는 프레임 끝까지 지연되므로 그 사이에 실행되는 다른 스포너의
        /// SnapToGround 레이가 초목 콜라이더를 스칠 수 있다. 초목은 개수가 수백 개라 그 위험을 감수할
        /// 이유가 없어, 콜라이더가 애초에 생기지 않는 경로로 따로 만든다.
        /// </summary>
        private static GameObject CreatePart(Transform parent, string name, PrimitiveType primitiveType,
            Vector3 localPosition, Vector3 localScale, Quaternion localRotation, Material material)
        {
            return CreatePart(parent, name, GetPrimitiveMesh(primitiveType),
                localPosition, localScale, localRotation, material);
        }

        /// <summary>
        /// 위와 같지만 내장 프리미티브 대신 이 클래스가 만든 저폴리 메시를 쓴다(B9).
        /// 메시는 반드시 캐시된 공유 메시를 넘겨라 - 파츠마다 새 Mesh를 만들면 수백 개가 쌓인다.
        /// </summary>
        private static GameObject CreatePart(Transform parent, string name, Mesh mesh,
            Vector3 localPosition, Vector3 localScale, Quaternion localRotation, Material material)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            go.transform.localRotation = localRotation;
            go.transform.localScale = localScale;

            if (mesh != null)
                go.AddComponent<MeshFilter>().sharedMesh = mesh;

            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            return go;
        }

        /// <summary>
        /// 프리미티브 종류별 내장 메시를 한 번만 뽑아 캐시한다.
        /// 임시 프리미티브에 자동으로 붙는 콜라이더가 물리 씬에 한 프레임도 남지 않도록, 지연 파괴
        /// (Object.Destroy)에 앞서 즉시 SetActive(false)로 비활성화한다(비활성화는 즉시 반영된다).
        /// 반환하는 메시는 Unity 내장 공유 메시라 임시 오브젝트를 파괴해도 사라지지 않는다.
        /// </summary>
        private static Mesh GetPrimitiveMesh(PrimitiveType primitiveType)
        {
            if (primitiveMeshCache.TryGetValue(primitiveType, out Mesh cached) && cached != null)
                return cached;

            var temporary = GameObject.CreatePrimitive(primitiveType);
            temporary.SetActive(false);
            var filter = temporary.GetComponent<MeshFilter>();
            Mesh mesh = filter != null ? filter.sharedMesh : null;
            Object.Destroy(temporary);

            primitiveMeshCache[primitiveType] = mesh;
            return mesh;
        }

        private static readonly Dictionary<PrimitiveType, Mesh> primitiveMeshCache = new Dictionary<PrimitiveType, Mesh>();

        // ─────────────────────────────────────────────────────────────────────────
        //  저폴리 초목 메시 (B9)  —  "가장 안 보이는 파츠가 삼각형 예산의 96%를 쓰던" 문제
        // ─────────────────────────────────────────────────────────────────────────
        //
        // 실측(특대 섬, 교체 전): 풀포기 78×768 = 59,904 · 덤불 로브 120×768 = 92,160 ·
        // 야자수 전체 5,760 → 합계 157,824. 즉 덤불+풀이 96%였다. 반면 야자수는 그루당 360삼각형
        // (내장 Cylinder 3×80 + 큐브 10×12)뿐이라 애초에 쌌다 - B8에서 "그루 수 42 → 16으로 상쇄했다"고
        // 한 것은 렌더러 예산에는 맞았지만 삼각형 관점에서는 잘못된 곳을 줄인 것이었다.
        //
        // 내장 Sphere는 768삼각형짜리 UV 구다. 덤불 로브와 풀포기는 둘 다 비균일 스케일로 납작하게
        // 눌러 쓰는 데다 대부분 5m 밖에서 보이므로, 그 정밀도가 화면에 도달하지 않는다
        // (ArtDirection 2장 "폴리곤을 아낄 곳은 언제나 5m 밖에서 안 보이는 디테일").
        //
        // 두 메시 모두 **내장 Sphere와 동일한 로컬 규격**(지름 1, 중심 원점, [-0.5,0.5]^3)으로 만든다.
        // 그래야 호출부의 스케일·회전·오프셋을 한 줄도 고치지 않고 그대로 쓸 수 있고, B8에서 확정한
        // 실루엣 규칙(덤불: 기울인 로브 3개·폭>>높이 / 풀: 두께가 폭의 30%)이 그대로 보존된다.

        /// <summary>
        /// 덤불 로브용 저폴리 덩어리 = 정이십면체(20삼각형). 내장 Sphere 768삼각형의 1/38.
        ///
        /// 왜 정이십면체인가: 20삼각형만으로 실루엣이 거의 원에 가깝고(면이 균일해 어느 각도에서 봐도
        /// 윤곽이 무너지지 않는다), 평면 셰이딩된 각진 면이 오히려 "매끈한 돌덩이와 덤불을 가른다"는
        /// B8의 목표를 강화한다. 로우폴리/스타일라이즈드 방향(ArtDirection 0장)과도 정확히 맞는다.
        ///
        /// 평면 셰이딩을 위해 정점을 면마다 분리한다(60정점) - 정점 수는 삼각형과 달리 이 프로젝트의
        /// 병목이 아니고, 공유 정점으로 부드럽게 셰이딩하면 20면짜리 저폴리가 찌그러진 구로 보인다.
        /// </summary>
        private static Mesh GetLowPolyLobeMesh()
        {
            if (lowPolyLobeMesh != null)
                return lowPolyLobeMesh;

            const float phi = 1.6180340f;
            var basePoints = new[]
            {
                new Vector3(-1f, phi, 0f), new Vector3(1f, phi, 0f),
                new Vector3(-1f, -phi, 0f), new Vector3(1f, -phi, 0f),
                new Vector3(0f, -1f, phi), new Vector3(0f, 1f, phi),
                new Vector3(0f, -1f, -phi), new Vector3(0f, 1f, -phi),
                new Vector3(phi, 0f, -1f), new Vector3(phi, 0f, 1f),
                new Vector3(-phi, 0f, -1f), new Vector3(-phi, 0f, 1f),
            };
            // 반지름 0.5 = 지름 1. 내장 Sphere와 같은 규격이라 호출부 스케일 의미가 바뀌지 않는다.
            for (int i = 0; i < basePoints.Length; i++)
                basePoints[i] = basePoints[i].normalized * 0.5f;

            int[] faces =
            {
                0, 11, 5,  0, 5, 1,   0, 1, 7,   0, 7, 10,  0, 10, 11,
                1, 5, 9,   5, 11, 4,  11, 10, 2, 10, 7, 6,  7, 1, 8,
                3, 9, 4,   3, 4, 2,   3, 2, 6,   3, 6, 8,   3, 8, 9,
                4, 9, 5,   2, 4, 11,  6, 2, 10,  8, 6, 7,   9, 8, 1,
            };

            lowPolyLobeMesh = BuildFlatShadedMesh("Veg_LobeIcosa", basePoints, faces, true);
            return lowPolyLobeMesh;
        }

        /// <summary>
        /// 야자수 줄기 마디용 저폴리 프리즘(8각, 28삼각형). 내장 Cylinder 80삼각형의 35%.
        ///
        /// 규격은 **내장 Cylinder와 완전히 동일**하다 - 정점이 반지름 0.5 원 위에 놓이고 높이는 y = -1~+1.
        /// 그래야 CreatePalm의 스케일 식 (segmentRadius*2, segmentLength*0.53, segmentRadius*2)의 의미가
        /// 한 글자도 바뀌지 않는다(굵기 보정은 스케일 식이 아니라 baseRadius 범위에서 한다 - 그쪽 주석 참고).
        ///
        /// 삼각형 내역: 옆면 8×2 = 16, 캡 2×(8-2) = 12 → 28. 캡은 중심 정점 없이 모서리에서 부채꼴로
        /// 감아(=n-2개) 삼각형을 아낀다. 캡을 아예 빼면 12삼각형까지 내려가지만, 마디가 6% 겹쳐 있다는
        /// 전제가 깨지는 순간(기울기 누적이 커지면 이음매가 벌어질 수 있다) 줄기 속이 뚫려 보이므로 남긴다.
        ///
        /// 옆면 법선은 반경 방향으로 **직접 지정**한다. RecalculateNormals에 맡기면 정점을 면마다 나눈
        /// 구조가 아니어도 면 법선이 평균돼 결과가 파이프라인 버전에 의존하고, 무엇보다 평면 셰이딩이
        /// 되면 이웃 면 사이 45° 법선 단차가 그대로 밝기 단차로 나온다(PalmTrunkSides 주석의 근거).
        /// 캡은 정점을 따로 두고 ±Y 법선을 줘 옆면과 섞이지 않게 한다.
        /// </summary>
        private static Mesh GetPalmTrunkPrismMesh()
        {
            if (palmTrunkPrismMesh != null)
                return palmTrunkPrismMesh;

            const int sides = PalmTrunkSides;
            const float radius = 0.5f;

            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector2>();

            // 옆면: 이음매 정점을 한 번 더 둬서 UV가 한 바퀴 돌 때 되감기지 않게 한다.
            int sideStart = vertices.Count;
            for (int i = 0; i <= sides; i++)
            {
                float angle = (float)i / sides * Mathf.PI * 2f;
                var radial = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                Vector3 p = radial * radius;
                float u = (float)i / sides;

                vertices.Add(new Vector3(p.x, -1f, p.z));
                normals.Add(radial);
                uvs.Add(new Vector2(u, 0f));

                vertices.Add(new Vector3(p.x, 1f, p.z));
                normals.Add(radial);
                uvs.Add(new Vector2(u, 1f));
            }

            int topStart = vertices.Count;
            for (int i = 0; i < sides; i++)
            {
                float angle = (float)i / sides * Mathf.PI * 2f;
                var p = new Vector3(Mathf.Cos(angle) * radius, 1f, Mathf.Sin(angle) * radius);
                vertices.Add(p);
                normals.Add(Vector3.up);
                uvs.Add(new Vector2(p.x + 0.5f, p.z + 0.5f));
            }

            int bottomStart = vertices.Count;
            for (int i = 0; i < sides; i++)
            {
                float angle = (float)i / sides * Mathf.PI * 2f;
                var p = new Vector3(Mathf.Cos(angle) * radius, -1f, Mathf.Sin(angle) * radius);
                vertices.Add(p);
                normals.Add(Vector3.down);
                uvs.Add(new Vector2(p.x + 0.5f, p.z + 0.5f));
            }

            Vector3[] positions = vertices.ToArray();
            var triangles = new List<int>();

            for (int i = 0; i < sides; i++)
            {
                int b0 = sideStart + i * 2;
                int t0 = b0 + 1;
                int b1 = b0 + 2;
                int t1 = b0 + 3;

                float mid = ((float)i + 0.5f) / sides * Mathf.PI * 2f;
                var outward = new Vector3(Mathf.Cos(mid), 0f, Mathf.Sin(mid));
                AddOrientedTriangle(triangles, positions, b0, t0, t1, outward);
                AddOrientedTriangle(triangles, positions, b0, t1, b1, outward);
            }

            for (int i = 1; i < sides - 1; i++)
            {
                AddOrientedTriangle(triangles, positions, topStart, topStart + i, topStart + i + 1, Vector3.up);
                AddOrientedTriangle(triangles, positions, bottomStart, bottomStart + i, bottomStart + i + 1, Vector3.down);
            }

            var mesh = new Mesh { name = "Veg_PalmTrunkPrism" };
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds(); // 법선은 위에서 직접 넣었으므로 RecalculateNormals를 부르면 안 된다.

            palmTrunkPrismMesh = mesh;
            return palmTrunkPrismMesh;
        }

        /// <summary>
        /// 삼각형 하나를 감김 방향까지 맞춰 넣는다. 기하 법선이 reference와 반대면 감김을 뒤집는다.
        /// 이 프로젝트는 왼손 좌표계라 표준 인덱스 표를 그대로 옮기면 통째로 안쪽을 향해 컬링되는
        /// 사고가 반복됐다(BuildFlatShadedMesh 주석). 표를 믿지 않고 계산으로 확정하는 방식을 그대로 쓴다.
        /// </summary>
        private static void AddOrientedTriangle(List<int> triangles, Vector3[] positions,
            int i0, int i1, int i2, Vector3 reference)
        {
            Vector3 geometric = Vector3.Cross(positions[i1] - positions[i0], positions[i2] - positions[i0]);
            if (Vector3.Dot(geometric, reference) < 0f)
            {
                int swap = i1;
                i1 = i2;
                i2 = swap;
            }

            triangles.Add(i0);
            triangles.Add(i1);
            triangles.Add(i2);
        }

        /// <summary>
        /// 풀포기용 저폴리 잎다발 = 부채꼴로 벌린 잎 3장(양면이라 12삼각형). 내장 Sphere의 1/64.
        ///
        /// 풀포기는 이미 "두께를 폭의 30%로 눌러 좌우로 눕힌" 형태라(B8), 눌린 구가 실제로 화면에서
        /// 하던 일은 "위로 솟은 납작한 잎 다발"이었다. 그 실루엣은 평면 조합으로 그대로 재현되고,
        /// 오히려 끝이 뾰족한 잎이 생겨 풀로 더 잘 읽힌다.
        ///
        /// 잎은 단면이라 뒷면에서 보이지 않으므로 감김을 뒤집은 사본을 함께 넣어 양면으로 만든다
        /// (양면 셰이더나 두께가 있는 상자를 쓰는 것보다 싸다). 규격은 Sphere와 같은
        /// [-0.5,0.5]^3 이라 호출부의 (width, height, width*0.30) 스케일 의미가 그대로 유지된다.
        /// </summary>
        private static Mesh GetGrassBladeMesh()
        {
            if (grassBladeMesh != null)
                return grassBladeMesh;

            var points = new List<Vector3>();
            var faces = new List<int>();

            // [B9 디렉터 수정] 잎 3장 × 폭 0.30 은 실기에서 "풀"이 아니라 **반투명 판때기**로 보였다.
            // 원인은 지오메트리가 아니라 비례다 - 호출부 스케일(폭 0.7~1.5m)에 잎이 3장뿐이라
            // 한 장이 0.5m 폭짜리 벽이 됐다. 잎을 늘리고 각각을 가늘게 해야 풀로 읽힌다.
            // 잎 5장을 0°/40°/78°/118°/155°로 벌린다. 호출부가 z를 30%로 누르므로 결과는 부채꼴이 된다.
            float[] yaws = { 0f, 40f, 78f, 118f, 155f };
            float[] tipHeights = { 0.50f, 0.34f, 0.44f, 0.30f, 0.40f };   // 끝 높이를 다르게 해 윗변이 평평해지지 않게 한다
            float[] tipOuts = { 0.30f, 0.18f, 0.38f, 0.16f, 0.26f };      // 바깥으로 벌어지는 정도

            for (int i = 0; i < yaws.Length; i++)
            {
                float rad = yaws[i] * Mathf.Deg2Rad;
                Vector3 outward = new Vector3(Mathf.Sin(rad), 0f, Mathf.Cos(rad));
                Vector3 side = new Vector3(Mathf.Cos(rad), 0f, -Mathf.Sin(rad));

                // 폭: 밑동 0.30 → 0.10, 끝 0.10 → 0.03. 잎이 "칼날"이 아니라 "풀잎"으로 읽히는 최소 비례다
                // (높이 1.0 대비 폭 0.10 = 10:1). 이전 0.30은 3.3:1이라 판때기였다.
                Vector3 b0 = side * -0.05f + outward * -0.03f + Vector3.down * 0.5f;
                Vector3 b1 = side * 0.05f + outward * -0.03f + Vector3.down * 0.5f;
                Vector3 t0 = side * -0.015f + outward * tipOuts[i] + Vector3.up * tipHeights[i];
                Vector3 t1 = side * 0.015f + outward * tipOuts[i] + Vector3.up * tipHeights[i];

                int b = points.Count;
                points.Add(b0); points.Add(b1); points.Add(t1); points.Add(t0);
                faces.Add(b); faces.Add(b + 1); faces.Add(b + 2);
                faces.Add(b); faces.Add(b + 2); faces.Add(b + 3);
                // 뒷면(감김 반대). 법선도 반대로 나오므로 양쪽에서 정상적으로 조명을 받는다.
                faces.Add(b); faces.Add(b + 2); faces.Add(b + 1);
                faces.Add(b); faces.Add(b + 3); faces.Add(b + 2);
            }

            // 잎은 닫힌 볼륨이 아니라 중심 기준 바깥 판정(ensureOutward)을 쓸 수 없다 - 감김을 그대로 둔다.
            grassBladeMesh = BuildFlatShadedMesh("Veg_GrassBlades", points.ToArray(), faces.ToArray(), false);
            return grassBladeMesh;
        }

        /// <summary>
        /// 면마다 정점을 분리한 평면 셰이딩 메시를 만든다.
        /// ensureOutward가 켜져 있으면 각 삼각형의 법선이 원점 바깥을 향하도록 감김을 바로잡는다
        /// (닫힌 볼록 다면체에만 유효하다 - 이 프로젝트는 왼손 좌표계라 표준 인덱스 표를 그대로
        /// 옮기면 안쪽을 향해 통째로 컬링되는 사고가 나기 쉬워, 표를 믿지 않고 계산으로 확정한다).
        /// UV는 XY 평면 투영이다 - 표면 그레인 텍스처를 곱하는 용도라 정밀한 전개가 필요 없다.
        /// </summary>
        private static Mesh BuildFlatShadedMesh(string meshName, Vector3[] points, int[] faces, bool ensureOutward)
        {
            var vertices = new Vector3[faces.Length];
            var uvs = new Vector2[faces.Length];
            var triangles = new int[faces.Length];

            for (int f = 0; f + 2 < faces.Length; f += 3)
            {
                Vector3 p0 = points[faces[f]];
                Vector3 p1 = points[faces[f + 1]];
                Vector3 p2 = points[faces[f + 2]];

                if (ensureOutward && Vector3.Dot(Vector3.Cross(p1 - p0, p2 - p0), (p0 + p1 + p2) / 3f) < 0f)
                {
                    Vector3 swap = p1;
                    p1 = p2;
                    p2 = swap;
                }

                vertices[f] = p0;
                vertices[f + 1] = p1;
                vertices[f + 2] = p2;
                uvs[f] = new Vector2(p0.x + 0.5f, p0.y + 0.5f);
                uvs[f + 1] = new Vector2(p1.x + 0.5f, p1.y + 0.5f);
                uvs[f + 2] = new Vector2(p2.x + 0.5f, p2.y + 0.5f);
                triangles[f] = f;
                triangles[f + 1] = f + 1;
                triangles[f + 2] = f + 2;
            }

            var mesh = new Mesh { name = meshName };
            mesh.vertices = vertices;
            mesh.uv = uvs;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static Mesh lowPolyLobeMesh;
        private static Mesh grassBladeMesh;
        private static Mesh palmTrunkPrismMesh;

        /// <summary>
        /// 팔레트 색의 명도만 바꾼 변주를 만든다(알파는 항상 1로 유지 - URP Lit Opaque에서 알파가
        /// 딸려 어두워지는 실수를 막는다). 새 색을 만드는 것이 아니라 같은 색의 밝기 단계다.
        /// factor > 1(밝게)도 허용하되 채널을 0~1로 잘라, 밝히는 쪽에서 색이 흰색으로 튀거나
        /// URP Lit이 HDR 범위를 받아 과노출되는 것을 막는다.
        /// </summary>
        private static Color Shade(Color color, float factor)
        {
            return new Color(
                Mathf.Clamp01(color.r * factor),
                Mathf.Clamp01(color.g * factor),
                Mathf.Clamp01(color.b * factor),
                1f);
        }

        /// <summary>
        /// 명도(HSV의 V)와 색상각은 그대로 두고 채도(S)만 배율로 바꾼다.
        ///
        /// 왜 Shade로는 안 되는가: Shade는 세 채널에 같은 수를 곱하므로 HSV 채도가 정확히 보존된다.
        /// 즉 "어둡게" 하면 명도만 떨어지고 유채색량(chroma = max-min)이 같이 줄어, 밝은 배경(하늘)
        /// 앞에서 색상 정보가 남지 않는 검은 실루엣이 된다 - 야자수 줄기에서 실제로 일어난 일이다.
        /// 채도를 따로 올릴 수단이 필요해서 짝이 되는 헬퍼를 둔다(새 팔레트 색을 만드는 것이 아니라
        /// 같은 색상각 위의 변주라는 점은 Shade와 같다).
        /// </summary>
        private static Color Saturate(Color color, float factor)
        {
            float max = Mathf.Max(color.r, Mathf.Max(color.g, color.b));
            float min = Mathf.Min(color.r, Mathf.Min(color.g, color.b));
            if (max <= 0.0001f || max - min <= 0.0001f)
                return new Color(color.r, color.g, color.b, 1f); // 무채색은 채도를 곱해도 무채색이다.

            float saturation = Mathf.Clamp01((max - min) / max * factor);
            float newMin = max * (1f - saturation);
            float scale = (max - newMin) / (max - min);
            return new Color(
                Mathf.Clamp01(newMin + (color.r - min) * scale),
                Mathf.Clamp01(newMin + (color.g - min) * scale),
                Mathf.Clamp01(newMin + (color.b - min) * scale),
                1f);
        }

        /// <summary>Rec.709 상대휘도. 톤 변주가 명도를 건드리지 않았는지 판정하는 기준이다.</summary>
        private static float Luma(Color color)
        {
            return 0.2126f * color.r + 0.7152f * color.g + 0.0722f * color.b;
        }

        /// <summary>
        /// 기준색과 같은 상대휘도를 유지한 채 색상만 shiftTarget 쪽으로 amount만큼 민 변주를 만든다.
        ///
        /// 왜 Shade가 아니라 이것인가(B10): 지면 캡의 톤 변주를 명도로 주면 이웃 삼각형 사이에 명도
        /// 단차가 생기고, 넓고 평평하게 조명된 지면에서 그 단차는 몇 %만 되어도 "각진 삼각형 얼룩"으로
        /// 읽힌다(실기 보고). 색상 변주는 같은 크기라도 삼각형 단위에서는 거의 보이지 않는다 - 사람 눈의
        /// 색차 공간 해상도가 명도보다 훨씬 낮기 때문이다. 여기서 휘도를 강제로 되맞추므로 명도 단차는
        /// 정확히 0이 되고, 남는 것은 넓은 패치에서만 읽히는 색조 변화뿐이다.
        /// </summary>
        private static Color ToneVariant(Color baseColor, Color shiftTarget, float amount)
        {
            if (amount <= 0.0001f)
                return new Color(baseColor.r, baseColor.g, baseColor.b, 1f);

            Color mixed = Color.Lerp(baseColor, shiftTarget, Mathf.Clamp01(amount));
            float mixedLuma = Luma(mixed);
            if (mixedLuma <= 0.0001f)
                return new Color(baseColor.r, baseColor.g, baseColor.b, 1f);

            return Shade(mixed, Luma(baseColor) / mixedLuma);
        }

        /// <summary>
        /// 야자수 줄기(나무껍질) 색. Driftwood(#8C6640)의 명도를 0.93배로 낮추고 채도를 1.20배로 올린
        /// 변주 = #82582D. 팔레트에 새 색을 추가한 것이 아니라 Driftwood의 한 단계다("목재" 의미 유지).
        /// 수치 근거는 BuildIslandSurface의 머티리얼 생성 지점 주석에 있다.
        /// </summary>
        private static readonly Color PalmBarkColor =
            Saturate(Shade(StructureVisualBuilder.Driftwood, 0.93f), 1.20f);

        /// <summary>
        /// 위치만으로 결정되는 0~1 해시. 난수 스트림을 소비하지 않으므로 재현성(같은 worldSeed = 같은
        /// 숲/지형)에 아무 영향이 없고, 같은 지형 메시면 항상 같은 결과가 나온다.
        /// 입력은 항상 섬 로컬 좌표(|x|,|z| ≤ radius ≤ 200)라 float 정밀도 문제가 생기지 않는다.
        /// </summary>
        private static float Hash01(Vector3 p)
        {
            float h = Mathf.Sin(p.x * 12.9898f + p.z * 78.233f + p.y * 37.719f) * 43758.5453f;
            return h - Mathf.Floor(h);
        }

        /// <summary>
        /// 캡 삼각형 하나를 어느 톤(서브메시)에 넣을지 고른다.
        ///
        /// 저주파 펄린(격자 ≈29m)으로 넓은 얼룩을 만들되, 삼각형 단위 해시를 섞어 얼룩의 경계를
        /// 점묘로 흩뜨린다. 이 디더가 없으면 펄린 격자가 축 정렬(axis-aligned)이라는 사실이 그대로
        /// 드러나 직선 경계의 사각 얼룩이 생긴다 - HighlandCap에서 실제로 났던 사고와 같은 원인이다.
        ///
        /// [B10] 디더 진폭을 0.55 → 0.20으로 낮췄다. 톤 한 칸의 폭이 1/toneCount = 0.333인데 0.55는
        /// ±0.275, 즉 칸 폭의 165%라 펄린 패치가 통째로 묻히고 **모든** 삼각형이 무작위 배정됐다
        /// (= 캡 전체가 소금·후추 노이즈). 0.20은 ±0.10 = 칸 폭의 30%라, 패치 경계 근처 삼각형만
        /// 섞이고 패치 안쪽은 한 톤으로 남는다 - 원래 의도했던 "경계만 점묘"가 된다.
        /// </summary>
        private static int ToneIndex(Vector3 centroid, int toneCount)
        {
            if (toneCount <= 1)
                return 0;

            // 펄린은 실제로 0~1을 다 쓰지 않고 대략 0.25~0.75에 몰려 있어, 그대로 나누면 양 끝 톤이
            // 거의 안 쓰인다. 1.6배로 펴서 세 톤이 고르게 나오게 한다.
            float patch = (Mathf.PerlinNoise(centroid.x * 0.035f + 517f, centroid.z * 0.035f + 517f) - 0.5f) * 1.6f + 0.5f;
            float dithered = Mathf.Clamp01(patch + (Hash01(centroid) - 0.5f) * 0.20f);
            return Mathf.Clamp(Mathf.FloorToInt(dithered * toneCount), 0, toneCount - 1);
        }
    }
}
