using UnityEngine;

namespace MakeGame.Systems
{
    /// <summary>
    /// WorldMapManager의 지오메트리 팩토리 파트 (0.2.34 감사 분리, 순수 이동).
    /// 바다 평면 메시, 절차적 섬 지형/해안선 띠 메시, 해안선 그라데이션 텍스처·머티리얼 생성을 담당한다.
    /// 공유 필드(oceanSize, seaLevel, terrainMaxHeight, shorelineBand* 등)는 본체(WorldMapManager.cs)에 있다.
    /// </summary>
    public partial class WorldMapManager
    {
        /// <summary>모든 섬이 공유하는 해안선 띠 머티리얼(색/텍스처가 같으므로 하나면 충분하다).</summary>
        private Material shorelineBandMaterial;

        /// <summary>
        /// 한 변이 size인 수평 격자 평면 메시를 만든다(y = 0, 위를 향한 법선).
        ///
        /// UV는 내장 Plane과 동일하게 평면 전체에 0~1로 정규화한다 - CreateOceanMaterial의
        /// mainTextureScale(oceanSize/10)과 Update의 스크롤 오프셋 의미가 한 글자도 바뀌지 않게 하기
        /// 위해서다("1타일 = 월드 10미터"라는 인스펙터 툴팁이 계속 사실이어야 한다).
        ///
        /// 격자 위상: 정점이 x = -size/2 + (i + 0.37)·cell, z = -size/2 + (j + 0.23)·cell 에 놓인다.
        /// x/z에 서로 다른 비율을 쓰는 것이 핵심이다 - 같은 비율이면 칸의 대각선이 z = x 직선이 되어
        /// 월드 원점(플레이어 시작 지점)을 그대로 지나간다. 두 값이 다르면 원점은 정점도 모서리도
        /// 대각선도 아닌 칸 내부의 한 점이 된다(CreateOcean의 흰 세로선 주석 참고).
        /// </summary>
        private static Mesh GenerateOceanMesh(float size, int cells)
        {
            cells = Mathf.Clamp(cells, 2, 200);
            float cell = size / cells;
            float half = size * 0.5f;
            const float phaseX = 0.37f;
            const float phaseZ = 0.23f;

            int lineCount = cells + 1;
            var vertices = new Vector3[lineCount * lineCount];
            var normals = new Vector3[vertices.Length];
            var uvs = new Vector2[vertices.Length];
            var triangles = new int[cells * cells * 6];

            for (int j = 0; j < lineCount; j++)
            {
                float z = -half + (j + phaseZ) * cell;
                for (int i = 0; i < lineCount; i++)
                {
                    float x = -half + (i + phaseX) * cell;
                    int index = j * lineCount + i;
                    vertices[index] = new Vector3(x, 0f, z);
                    normals[index] = Vector3.up;
                    uvs[index] = new Vector2(x / size + 0.5f, z / size + 0.5f);
                }
            }

            int t = 0;
            for (int j = 0; j < cells; j++)
            {
                for (int i = 0; i < cells; i++)
                {
                    int a = j * lineCount + i;          // (i,   j)
                    int b = a + 1;                      // (i+1, j)
                    int c = a + lineCount;              // (i,   j+1)
                    int d = c + 1;                      // (i+1, j+1)

                    // 왼손 좌표계에서 위를 향한 면의 감김. 반대로 감으면 바다가 통째로 사라진다
                    // (IslandMeshGenerator / GenerateShorelineBandMesh와 같은 함정).
                    triangles[t++] = a;
                    triangles[t++] = c;
                    triangles[t++] = b;

                    triangles[t++] = b;
                    triangles[t++] = c;
                    triangles[t++] = d;
                }
            }

            var mesh = new Mesh();
            mesh.name = "OceanGrid";
            // 65,535 정점을 넘길 일은 없지만(64칸 = 4,225), cells 상한이 바뀌어도 안전하게 32비트로 둔다.
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.vertices = vertices;
            mesh.normals = normals;
            mesh.uv = uvs;
            mesh.triangles = triangles;
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>
        /// 지정한 반지름/위치에 절차적 섬 지형 메시를 생성한다.
        /// MeshFilter/MeshRenderer/MeshCollider를 직접 붙여 플레이어가 실제로 걸어다닐 수 있게 한다.
        ///
        /// [B46] islandId를 함께 받는다. 이 값이 없으면 지형 노이즈가 섬 로컬 좌표에만 의존해서
        /// **반지름이 같은 섬은 지형 메시가 비트 단위로 동일**했다(정점은 원점 대칭으로 굽고 섬의 월드
        /// 위치는 오브젝트 트랜스폼에만 들어가므로, 위치가 달라도 메시는 같다). islandId는 지형 노이즈
        /// **오프셋 유도에만** 쓰이고 난수 스트림을 하나도 만들지 않는다 - 자원/위험요소의 추첨 순서와
        /// 세이브 키(v2부터 (islandIndex, stableKey) 안정 해시)도 영향을 받지 않는다.
        /// </summary>
        private GameObject CreateProceduralIslandTerrain(float radius, Vector3 position, int islandId)
        {
            var go = new GameObject("IslandTerrain");
            go.transform.SetParent(transform);
            go.transform.position = position;

            // 퀄리티 개선: 섬 반지름이 10배로 커진 뒤 예전 고정 해상도(링6/세그먼트24)를 그대로 쓰면
            // 삼각형 하나하나가 너무 커져 각진 저해상도 지형처럼 보인다. 반지름에 비례해 링/세그먼트
            // 수를 늘려 단위 면적당 디테일 밀도를 비슷하게 유지하되, 정점 수가 지나치게 많아지지
            // 않도록 상한선을 둔다.
            int ringCount = Mathf.Clamp(Mathf.RoundToInt(radius / 5f), 6, 40);
            int radialSegments = Mathf.Clamp(Mathf.RoundToInt(radius * 1.5f), 24, 90);
            // [B46] 노이즈 오프셋 시드. worldSeed와 islandId만 입력으로 받는 순수 해시라
            // (IslandMeshGenerator.ComputeNoiseSeed) 난수를 한 번도 소비하지 않는다.
            //
            // [B47] 여기에 **지형 프로파일 번호**가 더해졌다. 프로파일은 섬의 형태 언어 자체를 바꾼다
            // (완만한 초원 / 단봉 / 쌍봉 / 초승달 / 가운데 수로 / 석호 / 길쭉한 능선 / 고원+절벽).
            //  · SelectShapeProfile도 (worldSeed, islandId) 순수 해시다 - 난수 스트림을 만들지 않으므로
            //    자원·위험요소의 추첨 순서(월드 배치 재현성)가 한 칸도 밀리지 않는다.
            //    (세이브 키는 v2부터 안정 해시라 추첨 순서와 무관하다 - SaveData.StableSpawnKey 참고.)
            //  · islandId 0(시작 섬)은 그 함수가 항상 0번(가장 완만한 프로파일)을 돌려준다. 튜토리얼
            //    구간이고, 경비행기 잔해(+6,-4)·배 작업대(-6,-3)가 중심 근처에 고정 배치되며,
            //    사용자가 여기서 처음 집을 짓는다.
            //  · terrainMaxHeight(씬 직렬화 값 8)는 그대로 넘긴다. 섬별 높이 차이는 프로파일의
            //    heightScale(0.16~0.36)이 이 값에 곱해져 만들어진다 - 씬을 고치지 않고 높이를 가르는 경로다.
            // [B50] 실측 윤곽 주입. null이면(실측 배치 꺼짐) 기존 하모닉 마스크 경로 그대로다.
            // 시작 섬(0번)도 실측 윤곽을 쓰되, SelectShapeProfile이 id 0 → 0번(완만한 초원)을 고정
            // 반환하므로 높이 특성(heightScale 0.30, plateauPow 0.40 등)은 그대로 유지된다 - 윤곽만 실측.
            var mesh = IslandMeshGenerator.GenerateIslandMesh(
                radius, terrainMaxHeight, ringCount, radialSegments,
                noiseSeed: IslandMeshGenerator.ComputeNoiseSeed(worldSeed, islandId),
                shapeProfile: IslandMeshGenerator.SelectShapeProfile(worldSeed, islandId),
                radialMask: GetMaldivesRadialMask(islandId));

            var meshFilter = go.AddComponent<MeshFilter>();
            meshFilter.sharedMesh = mesh;

            var meshRenderer = go.AddComponent<MeshRenderer>();
            meshRenderer.sharedMaterial = terrainMaterial != null ? terrainMaterial : CreateDefaultTerrainMaterial(radius);

            var meshCollider = go.AddComponent<MeshCollider>();
            meshCollider.sharedMesh = mesh;

            CreateShorelineBand(go.transform, radius, radialSegments);

            return go;
        }

        /// <summary>
        /// 퀄리티 개선(바다): 섬을 두르는 얕은 물 띠(고리 메시)를 해수면 바로 위에 깐다.
        /// 그동안 바다는 수평선까지 완전한 단색 평면이라, 물이 얕은 해안과 깊은 바다가 시각적으로
        /// 전혀 구분되지 않았다(어디까지 걸어 들어가도 되는지 색으로 알 수 없었다).
        /// 셰이더를 새로 만들 수 없으므로, 절차적 고리 메시 + 코드로 생성한 반경 방향 알파 그라데이션
        /// 텍스처 조합으로 "안쪽은 밝고 바깥으로 갈수록 스르르 사라지는 띠"를 만든다.
        /// 콜라이더가 없는 순수 시각 요소이고, 해수면(seaLevel) 판정은 PlayerController가 y좌표만으로
        /// 하므로 수영/잠수 판정에는 아무 영향이 없다.
        ///
        /// [바다 v3] MGOcean 셰이더가 살아 있으면(oceanCustomShaderActive) 이 띠를 만들지 않는다 -
        /// 셰이더의 깊이 기반 해안 거품/얕은 물 흡수색이 "여기부터 얕다"를 실제 지형 깊이 그대로
        /// 표현하므로 원형 고리 띠는 중복이고, 반투명 바다와 반투명 띠가 겹치면 정렬(소트) 문제도
        /// 생긴다. 셰이더 로드 실패 폴백(URP Lit, 불투명)에서는 기존대로 띠를 만든다 - 폴백 경로 보존.
        /// </summary>
        private void CreateShorelineBand(Transform islandTransform, float radius, int radialSegments)
        {
            if (oceanCustomShaderActive)
                return;

            if (shorelineBandOuterScale <= 1f || radius <= 0f)
                return;

            var go = new GameObject("ShorelineBand");
            go.transform.SetParent(islandTransform, false);
            // 섬 중심의 XZ는 그대로 두고 높이만 해수면 바로 위로 올린다(바다 평면과의 z-파이팅 회피).
            // localPosition이 아니라 월드 position으로 지정해, 부모 트랜스폼에 어떤 값이 들어 있어도 항상 해수면에 맞는다.
            go.transform.position = new Vector3(
                islandTransform.position.x, seaLevel + shorelineBandHeight, islandTransform.position.z);

            var meshFilter = go.AddComponent<MeshFilter>();
            meshFilter.sharedMesh = GenerateShorelineBandMesh(
                radius * 0.95f, radius * shorelineBandOuterScale, radialSegments);

            var meshRenderer = go.AddComponent<MeshRenderer>();
            meshRenderer.sharedMaterial = GetShorelineBandMaterial();
            // 얕은 물 띠가 그림자를 드리우거나 받으면 평평한 판때기가 도드라져 보인다.
            meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            meshRenderer.receiveShadows = false;
        }

        /// <summary>
        /// 안쪽 반지름 innerRadius, 바깥 반지름 outerRadius인 납작한 고리(annulus) 메시를 만든다.
        /// UV는 u = 반경 방향 진행도(0=안쪽, 1=바깥), v = 각도로 잡아, 알파 그라데이션 텍스처 한 장을
        /// 반경 방향으로 그대로 입힐 수 있게 한다.
        /// </summary>
        private static Mesh GenerateShorelineBandMesh(float innerRadius, float outerRadius, int radialSegments)
        {
            radialSegments = Mathf.Clamp(radialSegments, 12, 120);

            var mesh = new Mesh();
            mesh.name = "ShorelineBand";

            var vertices = new Vector3[radialSegments * 2];
            var uvs = new Vector2[radialSegments * 2];
            var triangles = new int[radialSegments * 6];

            for (int seg = 0; seg < radialSegments; seg++)
            {
                float angle = (float)seg / radialSegments * Mathf.PI * 2f;
                float cos = Mathf.Cos(angle);
                float sin = Mathf.Sin(angle);
                float v = (float)seg / radialSegments;

                int inner = seg * 2;
                int outer = inner + 1;

                vertices[inner] = new Vector3(cos * innerRadius, 0f, sin * innerRadius);
                vertices[outer] = new Vector3(cos * outerRadius, 0f, sin * outerRadius);
                uvs[inner] = new Vector2(0f, v);
                uvs[outer] = new Vector2(1f, v);
            }

            for (int seg = 0; seg < radialSegments; seg++)
            {
                int inner = seg * 2;
                int outer = inner + 1;
                int nextInner = ((seg + 1) % radialSegments) * 2;
                int nextOuter = nextInner + 1;

                int t = seg * 6;
                // IslandMeshGenerator와 같은 이유로 감는 방향에 주의한다 - 반대로 감으면 위에서 봤을 때
                // 뒷면 컬링으로 띠가 통째로 사라진다.
                triangles[t + 0] = inner;
                triangles[t + 1] = nextOuter;
                triangles[t + 2] = outer;

                triangles[t + 3] = inner;
                triangles[t + 4] = nextInner;
                triangles[t + 5] = nextOuter;
            }

            mesh.vertices = vertices;
            mesh.uv = uvs;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            return mesh;
        }

        /// <summary>
        /// 해안선 띠용 반투명 머티리얼을 만들어 캐시한다. 모든 섬이 같은 색/텍스처를 쓰므로 하나만 만든다.
        /// URP Lit은 기본이 Opaque라 알파가 무시되므로, EffectBuilder.GetParticleMaterial()이 실측으로
        /// 검증해 둔 것과 같은 순서로 투명 모드 프로퍼티/키워드를 직접 세팅한다.
        /// </summary>
        private Material GetShorelineBandMaterial()
        {
            if (shorelineBandMaterial != null)
                return shorelineBandMaterial;

            var shader = Shader.Find("Universal Render Pipeline/Lit");
            var material = new Material(shader != null ? shader : Shader.Find("Standard"));
            material.color = shorelineBandColor;
            material.mainTexture = CreateShorelineGradientTexture();

            if (material.HasProperty("_Smoothness"))
                material.SetFloat("_Smoothness", 0.6f);
            if (material.HasProperty("_Metallic"))
                material.SetFloat("_Metallic", 0f);

            if (material.HasProperty("_Surface"))
                material.SetFloat("_Surface", 1f); // 0=Opaque, 1=Transparent
            if (material.HasProperty("_Blend"))
                material.SetFloat("_Blend", 0f); // Alpha 블렌드
            if (material.HasProperty("_SrcBlend"))
                material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            if (material.HasProperty("_DstBlend"))
                material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            if (material.HasProperty("_ZWrite"))
                material.SetFloat("_ZWrite", 0f);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.DisableKeyword("_ALPHATEST_ON");
            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;

            shorelineBandMaterial = material;
            return shorelineBandMaterial;
        }

        /// <summary>
        /// 가로(u) 방향으로만 알파가 변하는 그라데이션 텍스처를 코드로 생성한다.
        /// u=0(해안 쪽)에서 가장 진하고 u=1(먼바다 쪽)에서 완전히 투명해지는 2차 감쇠 곡선이라,
        /// 띠의 바깥 경계가 선으로 보이지 않고 깊은 바다색에 자연스럽게 녹아든다.
        /// 세로(v) 방향으로는 변화가 없으므로 높이 2픽셀이면 충분하다.
        /// </summary>
        private static Texture2D CreateShorelineGradientTexture()
        {
            const int width = 64;
            const int height = 2;
            const float peakAlpha = 0.55f;

            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            texture.name = "ShorelineGradient";
            texture.wrapMode = TextureWrapMode.Clamp; // 반복시키면 u=1(투명)과 u=0(불투명)이 맞닿아 경계선이 생긴다.
            texture.filterMode = FilterMode.Bilinear;
            texture.hideFlags = HideFlags.HideAndDontSave;

            var pixels = new Color32[width * height];
            for (int x = 0; x < width; x++)
            {
                float u = (float)x / (width - 1);
                float fade = (1f - u) * (1f - u);
                byte alpha = (byte)Mathf.RoundToInt(Mathf.Clamp01(peakAlpha * fade) * 255f);
                var pixel = new Color32(255, 255, 255, alpha);

                for (int y = 0; y < height; y++)
                    pixels[y * width + x] = pixel;
            }

            texture.SetPixels32(pixels);
            texture.Apply();
            return texture;
        }

        /// <summary>
        /// 섬 지형용 머티리얼이 지정되지 않았을 때 사용할 기본 URP Lit 머티리얼을 만든다.
        /// radius: 이 섬의 실제 반지름(미터). UV가 0~1로 정규화돼 있어(IslandMeshGenerator),
        /// 타일 반복 횟수를 고정값으로 두면 섬이 커질수록 텍스처 한 칸이 늘어나 흐릿해 보인다.
        /// 반지름에 비례해서 반복 횟수를 늘려 무늬 한 칸의 실제 크기가 섬 크기와 무관하게 일정하도록 한다.
        ///
        /// [B11] 기본색이 모래(0.76, 0.7, 0.5)에서 **Meadow Green(= IslandMeshGenerator.GrassCap 기준색)**
        /// 으로 바뀌었다. 실기 "밝은 황갈색 각진 얼룩" 신고의 정체가 **지면 캡이 덮지 않은 자리로 비치는
        /// 이 모래색 본체**였기 때문이다(값과 배제 근거는 IslandMeshGenerator.BuildGroundCaps 본문 주석).
        /// 모래는 이제 전부 덮개(DrySandCap/WetSandCap)가 그리므로 지형 본체가 모래일 이유가 없고,
        /// 본체를 풀밭과 같은 색으로 두면 **덮개에 어떤 구멍·틈·이음매가 생겨도 드러나는 것이 같은 초록**
        /// 이라 같은 종류의 사고가 원리적으로 재발하지 않는다.
        /// 텍스처/타일링도 GrassCap과 같은 규격("leaf", radius×0.75)으로 맞춘다 - 나중에
        /// Resources/Textures가 실제로 추가되면 본체와 덮개의 무늬가 어긋나면 안 되기 때문이다
        /// (현재 프로젝트에 Resources/Textures 폴더 자체가 없어 두 경로 모두 null 폴백이다).
        /// </summary>
        private Material CreateDefaultTerrainMaterial(float radius)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            var material = new Material(shader != null ? shader : Shader.Find("Standard"));
            material.color = StructureVisualBuilder.MeadowGreen;

            var surfaceTexture = Resources.Load<Texture2D>("Textures/leaf");
            if (surfaceTexture != null)
            {
                material.mainTexture = surfaceTexture;
                float tiling = radius * 0.75f; // GrassCap과 동일한 배수
                material.mainTextureScale = new Vector2(tiling, tiling);
            }
            return material;
        }
    }
}
