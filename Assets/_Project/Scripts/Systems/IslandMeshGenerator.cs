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
        /// 개까지 늘어나 프레임이 죽는다. 규모별 개수 공식(BuildIslandSurface)이 커봐야 정확히 이 값에
        /// 닿도록 잡혀 있고, 공식이 나중에 바뀌더라도 이 상한이 항상 마지막에 한 번 더 강제된다.
        /// 렌더러 개수 기준으로는 최대 406개(야자수 16×13 + 덤불 40×3 + 풀 78×1)다 - 형태 품질을 올리면서
        /// (B8: 굽은 줄기 3단 + 2단 꺾인 잎) 그루 수를 줄여 예전(42×5+60×2+78×1=408)과 같은 예산에 맞췄다.
        /// </summary>
        public const int MaxVegetationInstancesPerIsland = 180;

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
        /// (GetPrimitiveMesh + 빈 GameObject + MeshFilter/MeshRenderer)로 만든다.
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
            // 추가하고(디렉터 승인), 줄기는 Driftwood의 어두운 단계로 낮춰 명도 대비를 1.75배로 벌린다.
            // (Palm Fiber는 "수확한 마른 섬유" 아이템 색으로 의미를 유지한다 - 기존 8색의 뜻은 그대로다.)
            Material trunkMaterial = StructureVisualBuilder.CreateColorMaterial(Shade(StructureVisualBuilder.Driftwood, 0.78f), "wood");
            Material frondMaterial = StructureVisualBuilder.CreateColorMaterial(StructureVisualBuilder.FrondGreen, "leaf");
            Material bushMaterial = StructureVisualBuilder.CreateColorMaterial(Shade(StructureVisualBuilder.FrondGreen, 0.82f), "leaf");
            Material tuftMaterial = StructureVisualBuilder.CreateColorMaterial(Shade(StructureVisualBuilder.MeadowGreen, 0.86f), "leaf");

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
            //     덤불도 렌더러 2 → 3이라 상한을 60 → 40으로 낮췄다.
            int palmCount = Mathf.Clamp(Mathf.RoundToInt(radius * 0.12f), 4, 16);
            int bushCount = Mathf.Clamp(Mathf.RoundToInt(radius * 0.28f), 8, 40);
            int tuftCount = Mathf.Clamp(Mathf.RoundToInt(radius * 0.55f), 14, 78);

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
                CreatePalm(root.transform, TerrainSampler.SnapToGround(spot), rng, trunkMaterial, frondMaterial);
            }

            for (int i = 0; i < bushCount; i++)
            {
                Vector3 spot = SampleOnIsland(islandObject.transform.position, rng, innerClearRadius * 0.8f, radius * 0.50f);
                CreateBush(root.transform, TerrainSampler.SnapToGround(spot), rng, bushMaterial);
            }

            for (int i = 0; i < tuftCount; i++)
            {
                // 풀포기만 풀밭 경계 밖(모래)까지 나갈 수 있게 둔다 - 해안가에 듬성듬성 난 풀처럼 보여
                // 풀밭과 모래의 경계선이 자로 그은 원처럼 보이지 않게 하는 역할이다.
                Vector3 spot = SampleOnIsland(islandObject.transform.position, rng, innerClearRadius * 0.5f, radius * 0.70f);
                CreateGrassTuft(root.transform, TerrainSampler.SnapToGround(spot), rng, tuftMaterial);
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

            // 내륙 풀밭. 예전 색은 Shade(PalmFiber, 0.82) = #79733E로, Island Sand(#C2B280)와 색상각이
            // 각각 54°/45°로 9°밖에 차이 나지 않는 같은 황토 계열에 휘도만 1.58배 낮은 값이었다.
            // 그래서 실기에서 "풀밭"이 아니라 "그늘진 모래"로 읽혀 캡이 있는지조차 확인되지 않았다.
            // Meadow Green(#8AA84F, 색상각 80°)으로 바꿔 색상 자체로 구분되게 한다.
            BuildCapLayer(surfaceRoot, source, radius, "GrassCap", StructureVisualBuilder.MeadowGreen,
                capOffset, radius * 0.75f, "leaf",
                (centroid, distance, angle) => distance <= GrassBoundaryRadius(angle, radius, phaseA, phaseB));

            // 정상부의 밝은 풀. 반지름이 아니라 고도로 잘라내, 지형 굴곡(펄린 노이즈로 생긴 등성이)이
            // 그대로 색 경계가 된다 - 원형으로 잘라내면 또 하나의 완벽한 동심원이 생겨 인공적으로 보인다.
            // 0.86은 코사인 지형에서 대략 0.34R 안쪽(정상부)에 해당한다(cos(0.34·π/2) ≈ 0.86).
            BuildCapLayer(surfaceRoot, source, radius, "HighlandCap", Shade(StructureVisualBuilder.MeadowGreen, 1.18f),
                capOffset + 0.06f, radius * 0.75f, "leaf",
                (centroid, distance, angle) => centroid.y >= peakHeight * 0.86f
                    && distance <= GrassBoundaryRadius(angle, radius, phaseA, phaseB));

            // 해안의 젖은 모래. 바깥 한계 0.955R은 ShorelineBand(0.95R부터 시작하는 반투명 물 띠)와
            // 겹치는 폭을 최소화하기 위한 값이다.
            BuildCapLayer(surfaceRoot, source, radius, "WetSandCap", Shade(StructureVisualBuilder.IslandSand, 0.80f),
                capOffset * 0.5f, radius * 1.5f, "sand",
                (centroid, distance, angle) => distance >= radius * 0.84f && distance <= radius * 0.955f);
        }

        /// <summary>
        /// 지형 메시에서 조건에 맞는 삼각형만 골라내 덮개 메시 1장을 만든다.
        ///
        /// 지형 메시의 정점을 그대로 복사해 쓰기 때문에 굴곡이 100% 일치한다(링/세그먼트 개수 계산식을
        /// WorldMapManager와 중복 정의할 필요가 없다 - 그 계산식이 나중에 바뀌어도 자동으로 따라간다).
        /// 콜라이더는 붙이지 않는다(플레이어는 원래 지형 콜라이더 위를 걷는다).
        /// </summary>
        /// <param name="selector">(삼각형 무게중심, 중심축까지의 XZ 거리, 각도) → 이 삼각형을 포함할지.</param>
        private static void BuildCapLayer(Transform surfaceRoot, Mesh source, float radius, string name,
            Color color, float yOffset, float textureTiling, string textureName,
            System.Func<Vector3, float, float, bool> selector)
        {
            Vector3[] sourceVertices = source.vertices;
            int[] sourceTriangles = source.triangles;
            Vector2[] sourceUvs = source.uv;
            bool hasSourceUv = sourceUvs != null && sourceUvs.Length == sourceVertices.Length;

            var remap = new Dictionary<int, int>(sourceVertices.Length);
            var vertices = new List<Vector3>();
            var uvs = new List<Vector2>();
            var triangles = new List<int>();

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

                triangles.Add(RemapVertex(i0, remap, sourceVertices, sourceUvs, hasSourceUv, radius, vertices, uvs));
                triangles.Add(RemapVertex(i1, remap, sourceVertices, sourceUvs, hasSourceUv, radius, vertices, uvs));
                triangles.Add(RemapVertex(i2, remap, sourceVertices, sourceUvs, hasSourceUv, radius, vertices, uvs));
            }

            if (triangles.Count == 0)
                return;

            var mesh = new Mesh();
            mesh.name = $"Island{name}";
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            var go = new GameObject(name);
            go.transform.SetParent(surfaceRoot, false);
            // 지형과 정확히 같은 높이면 z-파이팅으로 지글거린다. 캡마다 다른 오프셋을 줘서 캡끼리도
            // 겹치는 구간(HighlandCap ⊂ GrassCap)에서 깊이 충돌이 나지 않게 한다.
            go.transform.localPosition = new Vector3(0f, yOffset, 0f);

            go.AddComponent<MeshFilter>().sharedMesh = mesh;

            var renderer = go.AddComponent<MeshRenderer>();
            var material = StructureVisualBuilder.CreateColorMaterial(color, textureName);
            // UV가 섬 전체에 0~1로 정규화돼 있어(GenerateIslandMesh) 타일 반복을 반지름에 비례시키지
            // 않으면 큰 섬에서 잎 무늬 한 칸이 수십 미터로 늘어나 흐릿한 단색이 된다.
            // WorldMapManager.CreateDefaultTerrainMaterial의 모래 타일링과 같은 계산 방식이다.
            material.mainTextureScale = new Vector2(textureTiling, textureTiling);
            renderer.sharedMaterial = material;

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

        /// <summary>야자수 1그루의 잎 장수. 잎 1장은 안쪽/바깥쪽 2마디로 꺾여 아래로 늘어진다.</summary>
        private const int PalmFrondCount = 5;

        /// <summary>
        /// 야자수 한 그루(줄기 원기둥 3 + 잎 박스 5×2 = 렌더러 13개)를 만든다.
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
            float height = rng.NextFloat(4.6f, 7.6f);
            float baseRadius = rng.NextFloat(0.26f, 0.38f);
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

                // 원기둥 프리미티브는 높이 2단위라 localScale.y에 "마디 길이의 절반"을 넣어야 한다.
                // 마디 사이가 벌어져 보이지 않게 길이를 6% 겹쳐 쌓는다.
                CreatePart(palm.transform, $"Veg_PalmTrunk{i}", PrimitiveType.Cylinder,
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

            CreatePart(bush.transform, "Veg_BushMain", PrimitiveType.Sphere,
                new Vector3(0f, height * 0.42f, 0f), new Vector3(width, height, width * 0.9f),
                Quaternion.Euler(0f, 0f, rng.NextFloat(-10f, 10f)), material);

            for (int i = 0; i < 2; i++)
            {
                Vector2 offset = rng.NextInsideUnitCircle() * (width * 0.34f);
                float lobeScale = rng.NextFloat(0.50f, 0.76f);
                // 로브를 본체보다 살짝 위로 올려 윤곽선 위쪽에 혹이 생기게 한다(돌은 이런 혹이 없다).
                CreatePart(bush.transform, $"Veg_BushLobe{i}", PrimitiveType.Sphere,
                    new Vector3(offset.x, height * rng.NextFloat(0.55f, 0.80f), offset.y),
                    new Vector3(width * lobeScale, height * lobeScale * 1.15f, width * lobeScale),
                    Quaternion.Euler(rng.NextFloat(-22f, 22f), 0f, rng.NextFloat(-22f, 22f)), material);
            }
        }

        /// <summary>
        /// 풀포기 한 개(얇게 눌린 구체 1개). 가장 저렴한 파츠라 개수가 제일 많아 렌더러 1개를 유지한다.
        /// [B8] 두께를 폭의 80% → 30%로 줄이고 좌우로 살짝 눕혀, 위에서 봐도 "납작한 덩어리"가 아니라
        /// 풀잎 다발이 서 있는 것처럼 보이게 한다.
        /// </summary>
        private static void CreateGrassTuft(Transform parent, Vector3 groundPosition, System.Random rng, Material material)
        {
            float width = rng.NextFloat(0.7f, 1.5f);
            float height = rng.NextFloat(0.30f, 0.62f);
            float yaw = rng.NextFloat(0f, 360f);
            float lean = rng.NextFloat(-14f, 14f);

            var tuft = CreatePart(parent, "Veg_GrassTuft", PrimitiveType.Sphere,
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
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            go.transform.localRotation = localRotation;
            go.transform.localScale = localScale;

            Mesh mesh = GetPrimitiveMesh(primitiveType);
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
    }
}
