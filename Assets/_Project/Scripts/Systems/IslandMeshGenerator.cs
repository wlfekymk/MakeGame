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
        public static Mesh GenerateIslandMesh(float radius, float maxHeight, int ringCount = 6, int radialSegments = 24, float noiseScale = 0.15f, float noiseAmplitude = 0.6f)
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
        /// 렌더러 개수 기준으로는 최대 약 408개(야자수 42×5 + 덤불 60×2 + 풀 78×1)다.
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
            // 색은 ArtDirection 1장 팔레트 안에서만 고른다 - 줄기는 Driftwood, 잎/풀은 Palm Fiber와
            // 그 명도 변주(새 색을 만들지 않는다).
            Material trunkMaterial = StructureVisualBuilder.CreateColorMaterial(StructureVisualBuilder.Driftwood, "wood");
            Material frondMaterial = StructureVisualBuilder.CreateColorMaterial(StructureVisualBuilder.PalmFiber, "leaf");
            Material bushMaterial = StructureVisualBuilder.CreateColorMaterial(Shade(StructureVisualBuilder.PalmFiber, 0.72f), "leaf");
            Material tuftMaterial = StructureVisualBuilder.CreateColorMaterial(Shade(StructureVisualBuilder.PalmFiber, 0.88f), "leaf");

            // (1) 지면 구분: 해안(모래) → 내륙(풀). 난수 소비 2회(경계 위상 2개)로 고정.
            float boundaryPhaseA = rng.NextFloat(0f, Mathf.PI * 2f);
            float boundaryPhaseB = rng.NextFloat(0f, Mathf.PI * 2f);
            BuildGrassCap(root.transform, islandObject, radius, boundaryPhaseA, boundaryPhaseB);

            // (2) 초목 개수: 반지름에 선형 비례시키되 규모별 상한을 두고, 마지막에 섬 전체 상한을 강제한다.
            //     면적 비례(반지름의 제곱)로 잡으면 특대 섬에서 곧바로 수천 개가 되어 쓸 수 없다.
            int palmCount = Mathf.Clamp(Mathf.RoundToInt(radius * 0.30f), 6, 42);
            int bushCount = Mathf.Clamp(Mathf.RoundToInt(radius * 0.42f), 10, 60);
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
        /// 지형 메시에서 내륙 부분만 잘라낸 "풀밭 캡" 메시를 만들어 지면 바로 위에 덮는다.
        ///
        /// 왜 머티리얼 교체가 아니라 덮개 메시인가: 지형 머티리얼은 WorldMapManager가 만들고(이 배치의
        /// 편집 범위 밖) 섬 전체에 하나만 적용되므로, 그것만으로는 해안과 내륙을 나눌 수 없다. 셰이더를
        /// 새로 만들 수도 없다. 그래서 WorldMapManager가 얕은 물 띠(ShorelineBand)를 별도 고리 메시로
        /// 해결한 것과 정확히 같은 방식 - 별도 메시 + 별도 머티리얼 - 을 그대로 따른다.
        ///
        /// 지형 메시의 정점을 그대로 복사해 쓰기 때문에 굴곡이 100% 일치한다(링/세그먼트 개수 계산식을
        /// WorldMapManager와 중복 정의할 필요가 없다 - 그 계산식이 나중에 바뀌어도 자동으로 따라간다).
        /// 경계는 완전한 원이 아니라 각도에 따라 출렁이게 해서 자로 그은 듯한 원형 경계를 피한다.
        /// 콜라이더는 붙이지 않는다(플레이어는 원래 지형 콜라이더 위를 걷는다).
        /// </summary>
        private static void BuildGrassCap(Transform surfaceRoot, GameObject islandObject, float radius,
            float phaseA, float phaseB)
        {
            var sourceFilter = islandObject.GetComponent<MeshFilter>();
            if (sourceFilter == null || sourceFilter.sharedMesh == null)
                return; // islandPlaceholderPrefab을 쓰는 구성이면 지형 메시를 알 수 없으므로 조용히 건너뛴다.

            Mesh source = sourceFilter.sharedMesh;
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
                if (distance > GrassBoundaryRadius(angle, radius, phaseA, phaseB))
                    continue;

                triangles.Add(RemapVertex(i0, remap, sourceVertices, sourceUvs, hasSourceUv, radius, vertices, uvs));
                triangles.Add(RemapVertex(i1, remap, sourceVertices, sourceUvs, hasSourceUv, radius, vertices, uvs));
                triangles.Add(RemapVertex(i2, remap, sourceVertices, sourceUvs, hasSourceUv, radius, vertices, uvs));
            }

            if (triangles.Count == 0)
                return;

            var mesh = new Mesh();
            mesh.name = "IslandGrassCap";
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            var go = new GameObject("GrassCap");
            go.transform.SetParent(surfaceRoot, false);
            // 지형과 정확히 같은 높이면 z-파이팅으로 지글거린다. 지형 최대 높이가 2.5m뿐이라
            // 8cm면 눈에 띄지 않으면서도 깊이 충돌을 확실히 피한다.
            go.transform.localPosition = new Vector3(0f, 0.08f, 0f);

            go.AddComponent<MeshFilter>().sharedMesh = mesh;

            var renderer = go.AddComponent<MeshRenderer>();
            var material = StructureVisualBuilder.CreateColorMaterial(Shade(StructureVisualBuilder.PalmFiber, 0.82f), "leaf");
            // UV가 섬 전체에 0~1로 정규화돼 있어(GenerateIslandMesh) 타일 반복을 반지름에 비례시키지
            // 않으면 큰 섬에서 잎 무늬 한 칸이 수십 미터로 늘어나 흐릿한 단색이 된다.
            // WorldMapManager.CreateDefaultTerrainMaterial의 모래 타일링과 같은 계산 방식이다.
            material.mainTextureScale = new Vector2(radius * 0.75f, radius * 0.75f);
            renderer.sharedMaterial = material;

            // 지면에 8cm 떠 있는 덮개라 그림자를 드리우면 자기 그림자로 얼룩진다. 받기만 한다
            // (야자수 그림자는 풀밭 위에 정상적으로 떨어져야 한다).
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = true;
        }

        /// <summary>
        /// 원본 지형 정점을 풀밭 캡 메시로 옮기고(중복 없이) 새 인덱스를 돌려준다.
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

        /// <summary>
        /// 야자수 한 그루(줄기 원기둥 1 + 잎 박스 4)를 만든다.
        /// 뿌리 오브젝트의 스케일은 항상 1(균등)로 두고 기울기만 준다 - 부모 스케일이 비균일한 상태에서
        /// 회전한 자식을 두면 전단(shear)으로 찌그러진다(CreatureVisualBuilder/StructureVisualBuilder
        /// 주석에 반복해서 나오는 이 프로젝트의 기존 함정).
        /// </summary>
        private static void CreatePalm(Transform parent, Vector3 groundPosition, System.Random rng,
            Material trunkMaterial, Material frondMaterial)
        {
            float height = rng.NextFloat(4.2f, 7.4f);
            float trunkRadius = rng.NextFloat(0.16f, 0.26f);
            float tilt = rng.NextFloat(3f, 13f);
            float tiltDirection = rng.NextFloat(0f, 360f);
            float frondLength = rng.NextFloat(1.7f, 2.7f);
            float baseYaw = rng.NextFloat(0f, 360f);

            var palm = new GameObject("Veg_Palm");
            palm.transform.SetParent(parent, false);
            palm.transform.position = groundPosition;
            palm.transform.rotation = Quaternion.Euler(0f, tiltDirection, 0f) * Quaternion.Euler(tilt, 0f, 0f);

            // 원기둥 프리미티브는 높이 2단위라 localScale.y에 "전체 높이의 절반"을 넣어야 한다.
            CreatePart(palm.transform, "Veg_PalmTrunk", PrimitiveType.Cylinder,
                new Vector3(0f, height * 0.5f, 0f),
                new Vector3(trunkRadius * 2f, height * 0.5f, trunkRadius * 2f),
                Quaternion.identity, trunkMaterial);

            const int frondCount = 4;
            for (int i = 0; i < frondCount; i++)
            {
                float yaw = baseYaw + i * (360f / frondCount) + rng.NextFloat(-12f, 12f);
                float droop = rng.NextFloat(18f, 42f); // 잎이 아래로 처지는 각도
                Quaternion rotation = Quaternion.Euler(0f, yaw, 0f) * Quaternion.Euler(droop, 0f, 0f);
                // 잎 박스의 로컬 +Z가 잎 길이 방향이므로, 회전시킨 방향으로 길이의 절반만큼 밀어
                // 밑동이 줄기 꼭대기에 붙게 한다.
                Vector3 localPosition = new Vector3(0f, height, 0f) + rotation * new Vector3(0f, 0f, frondLength * 0.5f);
                CreatePart(palm.transform, $"Veg_PalmFrond{i}", PrimitiveType.Cube,
                    localPosition, new Vector3(0.36f, 0.06f, frondLength), rotation, frondMaterial);
            }
        }

        /// <summary>덤불 한 개(눌린 구체 2개). 야자수보다 낮아 시야를 막지 않는 높이로 유지한다.</summary>
        private static void CreateBush(Transform parent, Vector3 groundPosition, System.Random rng, Material material)
        {
            float width = rng.NextFloat(1.1f, 2.0f);
            float height = rng.NextFloat(0.7f, 1.2f);
            float yaw = rng.NextFloat(0f, 360f);
            Vector2 lobeOffset = rng.NextInsideUnitCircle() * (width * 0.35f);

            var bush = new GameObject("Veg_Bush");
            bush.transform.SetParent(parent, false);
            bush.transform.position = groundPosition;
            bush.transform.rotation = Quaternion.Euler(0f, yaw, 0f);

            CreatePart(bush.transform, "Veg_BushMain", PrimitiveType.Sphere,
                new Vector3(0f, height * 0.45f, 0f), new Vector3(width, height, width),
                Quaternion.identity, material);
            CreatePart(bush.transform, "Veg_BushLobe", PrimitiveType.Sphere,
                new Vector3(lobeOffset.x, height * 0.32f, lobeOffset.y),
                new Vector3(width * 0.65f, height * 0.75f, width * 0.65f),
                Quaternion.identity, material);
        }

        /// <summary>풀포기 한 개(납작하게 눌린 구체 1개). 가장 저렴한 파츠라 개수가 제일 많다.</summary>
        private static void CreateGrassTuft(Transform parent, Vector3 groundPosition, System.Random rng, Material material)
        {
            float width = rng.NextFloat(0.7f, 1.5f);
            float height = rng.NextFloat(0.22f, 0.45f);
            float yaw = rng.NextFloat(0f, 360f);

            var tuft = CreatePart(parent, "Veg_GrassTuft", PrimitiveType.Sphere,
                Vector3.zero, new Vector3(width, height, width * 0.8f),
                Quaternion.Euler(0f, yaw, 0f), material);
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
        /// </summary>
        private static Color Shade(Color color, float factor)
        {
            return new Color(color.r * factor, color.g * factor, color.b * factor, 1f);
        }
    }
}
