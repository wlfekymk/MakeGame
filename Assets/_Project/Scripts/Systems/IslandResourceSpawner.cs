using System.Collections.Generic;
using UnityEngine;
using MakeGame.Data;
using MakeGame.UI;

namespace MakeGame.Systems
{
    /// <summary>
    /// 섬 하나에 채집 가능한 자원 노드들을 배치하는 스포너.
    /// 섬 규모가 클수록 더 많은 자원 노드를 생성한다 (Stranded Deep 기준: 큰 섬일수록 자원이 풍부).
    /// 실제 3D 모델 에셋이 없으므로, 자원 종류별로 다르게 조합한 프리미티브(GetNodeShape/
    /// AddResourceDetailParts 참고)에 ResourceNode를 붙여 시각화한다. 처음엔 전부 동일한 큐브였는데,
    /// 비스듬한 각도에서 보면 큐브 옆면 3개가 보여 죄다 육각형 상자처럼 보인다는 사용자 피드백을 받고
    /// 자원별 프리미티브 조합으로 실루엣을 다르게 만들었다.
    /// 버그 수정: 예전에는 모든 자원 노드가 색 지정 없이 기본 회색 큐브로만 나와, 나뭇가지/돌조각/코코넛/
    /// 금속조각 등 종류가 전혀 구분되지 않았다. 인벤토리/제작 UI에서 이미 쓰던
    /// UIBuilder.GetItemCategoryColor를 재사용해 최소한 음식/음료/일반 재료 정도는 색으로 구분되게 했다.
    /// 퀄리티 개선: "같은 종류 자원이라도 보여지는 모양이 다양했으면 좋겠다"는 추가 피드백을 받아,
    /// SpawnSingleNode에서 스폰마다 축별 크기 배율과 Y축 회전을 무작위로 주고, AddResourceDetailParts의
    /// 곁가지/마디/잎사귀/볼트 등 반복 파츠 개수도 무작위 범위로 바꿔 같은 자원이라도 인스턴스마다
    /// 조금씩 다르게 생기도록 했다(완전한 클론처럼 보이지 않게).
    /// </summary>
    public class IslandResourceSpawner : MonoBehaviour
    {
        [System.Serializable]
        public class ResourceEntry
        {
            [Tooltip("이 자원 노드를 채집했을 때 얻는 아이템")]
            public ItemData yieldItem;

            [Tooltip("소형 섬 기준 기본 배치 개수 (규모가 커질수록 배율이 곱해진다)")]
            public int baseCount = 3;

            [Tooltip("이 자원이 등장할 수 있는 최소 섬 규모. 예를 들어 Large로 설정하면 대형/특대 섬에만 등장하고" +
                " 소형/중형 섬에는 전혀 등장하지 않는다. 희귀 재료(금속조각/부력통/엔진부품 등)의 등장 위치를 제한할 때 사용한다.")]
            public IslandSize minimumIslandSize = IslandSize.Small;

            [Tooltip("이 자원을 채집하는 데 도구가 필요한지 여부. true면 requiredTool을 인벤토리에 보유해야 채집할 수 있고,\n" +
                "채집할 때마다 그 도구의 내구도(ItemData.maxUses)가 1씩 소모된다 (예: 나뭇가지 채집에 손도끼 필요).")]
            public bool requiresTool = false;

            [Tooltip("채집에 필요한 도구 아이템 (requiresTool이 true일 때만 사용, 예: 손도끼)")]
            public ItemData requiredTool;
        }

        [Tooltip("섬에 배치할 자원 종류와 기본 개수 목록")]
        public List<ResourceEntry> resourceEntries = new List<ResourceEntry>();

        // 긴급 정정(#2 회귀 수정): 이 필드들을 한 차례 제거하고 IslandSizeMetrics 직접 호출로 바꿨었는데,
        // 실제 배포된 SampleScene.unity에 이 컴포넌트가 배치되어 있고 이 필드들에 코드 기본값과 다른
        // 값(디자이너가 조정한 실제 밸런스 값)이 직렬화되어 있다는 사실이 뒤늦게 확인되었다. 필드를
        // 제거하면 Unity가 그 직렬화 값을 잃어버리고 조용히 코드 기본값으로 되돌아간다 - "스테이징 범위에
        // 씬 파일이 없다"는 것이 "프로젝트에 씬 파일이 없다"는 뜻이 아니었다. 필드명/타입/기본값을
        // 원래(리팩터링 이전) 그대로 복원해 씬 직렬화 값이 다시 정상적으로 바인딩되도록 되돌렸다.
        // IslandSizeMetrics는 삭제하지 않고, 이 필드가 의미 있게 설정되지 않았을 때(0 이하)만 쓰는
        // "폴백 단일 소스"로 역할을 낮췄다 (GetMultiplier/GetScatterRadius 참고).
        [Header("섬 규모별 배치 배율")]
        // 버그 수정 (#1003/#1006 - 섬 크기별 자원 밀도 공식 정립): 예전에는 이 배율이 섬 반지름과 같은
        // "선형" 비율(1/2/3/4)로만 늘어났다. 그런데 자원이 실제로 흩뿌려지는 면적은 반지름의 "제곱"에
        // 비례해서 커진다. WorldMapManager.GetSizeScale의 지형 반지름이 50/90/140/200이므로 면적비는
        // 1 : 3.24 : 7.84 : 16이 된다. 배율을 반지름과 같은 선형 비율로만 올리면 섬이 커질수록 단위
        // 면적당 자원 밀도가 오히려 옅어지는 문제가 있었다(맵 스케일을 10배로 키운 배치 이후 발견).
        // 아래 배율을 면적비에 맞춰 올려서 섬 크기와 무관하게 체감 밀도가 비슷하게 유지되도록 했다.
        public float smallMultiplier = 1f;
        public float mediumMultiplier = 2f;
        public float largeMultiplier = 3f;
        public float extraLargeMultiplier = 4f;

        [Header("섬 규모별 산포 반경")]
        // 버그 수정 (#1003/#1006): 예전에는 scatterRadius가 섬 규모와 무관하게 값 하나(80f)뿐이었다.
        // WorldMapManager.GetSizeScale의 지형 반지름(50/90/140/200)과 전혀 맞지 않아서, 소형 섬은
        // 지형 밖(바다)까지 자원이 튀어나가고 특대 섬은 중심 근처에만 자원이 몰려 대부분의 면적이 텅
        // 비어 보이는 문제가 있었다. 각 섬 지형 반지름의 80%(해안에 자원이 물에 잠기지 않도록 여백 확보)
        // 에 맞춰 규모별 반경을 따로 뒀다.
        // [B5 디렉터 수정] 위 주석은 "규모별로 따로 뒀다"고 적혀 있었지만 실제로는 네 값이 전부 같았다.
        // 즉 주석이 고쳤다고 주장하는 버그가 그대로 살아 있었다(qa-reviewer 지적). 소형 섬(지형 반지름 50)에
        // 반경 80로 흩뿌리면 배치물이 바다로 나가고, 특대 섬(200)은 중심 근처에만 몰린다.
        // IslandSizeMetrics.GetTerrainRadius(50/90/140/200)의 80%로 실제로 분리했다.
        // 씬의 낡은 `scatterRadius` 단일 키는 코드에 대응 필드가 없는 죽은 키라 함께 제거했다.
        public float smallScatterRadius = 40f;
        public float mediumScatterRadius = 72f;
        public float largeScatterRadius = 112f;
        public float extraLargeScatterRadius = 160f;

        /// <summary>
        /// 지정한 섬 인스턴스 위에 규모에 맞는 개수만큼 자원 노드를 생성한다.
        /// 각 노드는 섬 위치를 중심으로 scatterRadius 반경 안에 무작위 배치된다.
        /// B3-3: worldSeed를 추가로 받아, 이 섬(island.islandId) 전용 결정적 System.Random 스트림을
        /// 만들어 쓴다. 같은 worldSeed로 다시 호출하면(WorldMapManager.RegenerateWorld) 이 섬에서
        /// 생성되는 노드의 위치·모양(스케일/회전 지터 포함)·개수·순서가 정확히 그대로 재현된다 -
        /// 다른 섬이 그 사이에 몇 개를 뽑았는지와 완전히 무관하다(섬마다 독립된 스트림이므로).
        /// 각 노드에는 (island.islandId, spawnOrder) 쌍으로 이뤄진 안정적인 식별자를 부여한다
        /// (ResourceNode.islandIndex/spawnOrder 참고) - B3-4에서 이 쌍을 세이브 키로 그대로 쓴다.
        /// </summary>
        public List<ResourceNode> SpawnResourcesForIsland(IslandInstance island, Transform parent, int worldSeed)
        {
            var spawned = new List<ResourceNode>();
            if (island == null)
                return spawned;

            System.Random rng = SeededRandomExtensions.CreateForIsland(worldSeed, island.islandId);
            int spawnOrder = 0;

            float multiplier = GetMultiplier(island.size);
            float radius = GetScatterRadius(island.size);

            foreach (var entry in resourceEntries)
            {
                if (entry.yieldItem == null)
                    continue;

                // 최소 섬 규모 미만이면 이 자원은 아예 등장하지 않는다 (희귀 재료 위치 제한용).
                if (island.size < entry.minimumIslandSize)
                    continue;

                int count = Mathf.RoundToInt(entry.baseCount * multiplier);
                for (int i = 0; i < count; i++)
                {
                    Vector2 offset = rng.NextInsideUnitCircle() * radius;
                    Vector3 position = island.mapPosition + new Vector3(offset.x, 0f, offset.y);
                    position = TerrainSampler.SnapToGround(position);
                    spawned.Add(SpawnSingleNode(entry, position, parent, rng, island.islandId, spawnOrder));
                    spawnOrder++;
                }
            }

            return spawned;
        }

        /// <summary>
        /// 자원 노드 하나를 실제로 생성한다. ResourceNode 컴포넌트를 붙인다.
        /// 버그 수정: requiresTool/requiredTool을 ResourceNode에 전달하지 않아, ResourceNode.Harvest에
        /// 도구 요구 로직이 있어도 실제로 생성되는 노드는 전부 도구 없이 채집 가능했던 문제를 고쳤다
        /// (ResourceEntry에 requiresTool/requiredTool 필드가 아예 없어 절차적으로 설정할 방법 자체가 없었음).
        /// 퀄리티 개선: 예전엔 모든 자원 종류가 동일한 큐브(1x1.5x1)라 카메라를 비스듬히 보면 다 똑같은
        /// "육각형 상자"로 보여 색상만으로 뭘 채집할 수 있는지 구분해야 했다(사용자 피드백으로 발견).
        /// GetNodeShape로 자원 종류별 실제 프리미티브/크기를 다르게 하고, AddResourceDetailParts로
        /// 보조 파츠(대나무 마디, 야자잎 부채꼴 등)를 덧붙여 실루엣만 보고도 구분되게 했다.
        /// </summary>
        private ResourceNode SpawnSingleNode(ResourceEntry entry, Vector3 position, Transform parent, System.Random rng, int islandIndex, int spawnOrder)
        {
            ItemData yieldItem = entry.yieldItem;
            string itemName = yieldItem.itemName;

            GetNodeShape(itemName, out PrimitiveType primitive, out Vector3 scale, out Quaternion rotation);

            // 퀄리티 개선: 사용자 피드백("같은 종류 자원이라도 보여지는 모양이 다양했으면 좋겠다")을 반영해,
            // 자원 하나하나가 완전히 같은 크기/방향으로 찍히지 않도록 스폰마다 축별로 살짝 다른 배율을 곱하고
            // Y축(위아래 축) 기준으로 무작위 회전을 더한다.
            // B3-3: 시드 없는 UnityEngine.Random 대신 이 섬 전용 rng(System.Random)를 쓰도록 바꿔, 같은
            // worldSeed면 이 스케일/회전 지터까지도 정확히 재현되게 했다(SpawnResourcesForIsland 주석 참고).
            Vector3 scaleJitter = new Vector3(
                rng.NextFloat(0.85f, 1.18f),
                rng.NextFloat(0.85f, 1.25f),
                rng.NextFloat(0.85f, 1.18f));
            scale = Vector3.Scale(scale, scaleJitter);
            rotation = rotation * Quaternion.Euler(0f, rng.NextFloat(0f, 360f), 0f);

            GameObject go = GameObject.CreatePrimitive(primitive);
            go.transform.SetParent(parent);
            go.transform.localScale = scale;
            go.transform.rotation = rotation;
            go.transform.position = position + Vector3.up * GetHalfHeight(primitive, scale); // 프리미티브 종류별 반높이만큼 띄워 지형 위에 놓이게 한다
            go.name = $"Resource_{itemName}";

            // 아이템 종류(무기/음식/음료/설치형/이동수단/일반 재료)에 맞는 색을 입혀 카테고리 단위로 구분한다.
            Color color = UIBuilder.GetItemCategoryColor(yieldItem);
            string textureName = GetSurfaceTextureName(yieldItem);

            var renderer = go.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.material.color = color;

                // 단색이 밋밋해 보이는 문제 개선: 아이템 종류에 맞는 흑백 그레인 텍스처를 곱해 씌워
                // 나무 재질(세로 결)/돌 재질(반점)/그 외(부드러운 얼룩) 표면 디테일을 준다. 색상 구분은
                // 여전히 위 material.color가 담당하고, 텍스처는 표면 질감만 추가한다.
                var surfaceTexture = Resources.Load<Texture2D>($"Textures/{textureName}");
                if (surfaceTexture != null)
                {
                    renderer.material.mainTexture = surfaceTexture;
                    renderer.material.mainTextureScale = new Vector2(1.5f, 2f);
                }
            }

            AddResourceDetailParts(go, itemName, scale, color, textureName, rng);

            var node = go.AddComponent<ResourceNode>();
            node.yieldItem = yieldItem;
            node.remainingHarvestCount = node.maxHarvestCount;
            node.requiresTool = entry.requiresTool;
            node.requiredTool = entry.requiredTool;
            node.islandIndex = islandIndex;
            node.spawnOrder = spawnOrder;
            return node;
        }

        /// <summary>
        /// 자원 종류별로 실제 사용할 프리미티브 형태/크기/기울기를 정한다. 예전엔 전부 큐브(1x1.5x1)
        /// 하나뿐이었던 것을, 원기둥(대나무/부력통/연료 스파우트 등)·구(돌/코코넛)·납작한 큐브(천/금속조각)
        /// 등으로 나눠 실루엣만 봐도 어떤 자원인지 구분할 수 있게 했다.
        /// </summary>
        private void GetNodeShape(string itemName, out PrimitiveType primitive, out Vector3 scale, out Quaternion rotation)
        {
            rotation = Quaternion.identity;

            switch (itemName)
            {
                case "나뭇가지": // 짧고 가는 나뭇가지 다발 (아래 AddResourceDetailParts에서 곁가지 2개 추가)
                    primitive = PrimitiveType.Cylinder;
                    scale = new Vector3(0.09f, 0.32f, 0.09f);
                    break;
                case "대나무": // 키가 큰 얇은 기둥 (마디는 AddResourceDetailParts에서 추가)
                    primitive = PrimitiveType.Cylinder;
                    scale = new Vector3(0.14f, 1.05f, 0.14f);
                    break;
                case "돌조각": // 납작하게 눌린 바위 무더기
                    primitive = PrimitiveType.Sphere;
                    scale = new Vector3(0.5f, 0.32f, 0.5f);
                    break;
                case "부싯돌": // 얇고 각진 파편 - 살짝 비스듬히 기울여 둠
                    primitive = PrimitiveType.Cube;
                    scale = new Vector3(0.32f, 0.1f, 0.42f);
                    rotation = Quaternion.Euler(8f, 25f, -5f);
                    break;
                case "코코넛": // 둥근 열매 (여분 하나는 AddResourceDetailParts에서 추가)
                    primitive = PrimitiveType.Sphere;
                    scale = new Vector3(0.42f, 0.42f, 0.42f);
                    break;
                case "천조각": // 얇고 넓은 천 조각
                    primitive = PrimitiveType.Cube;
                    scale = new Vector3(0.55f, 0.05f, 0.4f);
                    break;
                case "야자잎": // 짧은 줄기 위에 잎사귀들이 부채꼴로 퍼짐 (AddResourceDetailParts에서 추가)
                    primitive = PrimitiveType.Cylinder;
                    scale = new Vector3(0.05f, 0.08f, 0.05f);
                    break;
                case "금속조각": // 찌그러진 얇은 금속판
                    primitive = PrimitiveType.Cube;
                    scale = new Vector3(0.5f, 0.06f, 0.34f);
                    rotation = Quaternion.Euler(6f, 20f, 0f);
                    break;
                case "부력통": // 짧고 통통한 드럼통 형태
                    primitive = PrimitiveType.Cylinder;
                    scale = new Vector3(0.42f, 0.42f, 0.42f);
                    break;
                case "비상식량": // 작은 배급 상자
                    primitive = PrimitiveType.Cube;
                    scale = new Vector3(0.34f, 0.22f, 0.26f);
                    break;
                case "연료": // 각진 연료통 몸체 (주둥이는 AddResourceDetailParts에서 추가)
                    primitive = PrimitiveType.Cube;
                    scale = new Vector3(0.28f, 0.4f, 0.22f);
                    break;
                case "엔진부품": // 짧은 원판형 부품 (볼트는 AddResourceDetailParts에서 추가)
                    primitive = PrimitiveType.Cylinder;
                    scale = new Vector3(0.3f, 0.22f, 0.3f);
                    break;
                default: // 목록에 없는 새 자원이 추가되면 기존 큐브로 안전하게 폴백
                    primitive = PrimitiveType.Cube;
                    scale = new Vector3(1f, 1.5f, 1f);
                    break;
            }
        }

        /// <summary>
        /// 프리미티브 종류별 로컬 단위 형태 차이를 감안해, 지정한 스케일일 때 피벗(중심)을 지면 위
        /// 몇 미터에 둬야 바닥이 정확히 지면에 닿는지 계산한다. 큐브/구는 반높이가 scale.y*0.5인데
        /// 실린더는 기본 높이가 2(로컬 -1~+1)라서 반높이가 scale.y*1이다 - 이 차이를 반영하지 않으면
        /// 프리미티브 종류에 따라 절반이 땅에 묻히거나 붕 떠 보인다.
        /// </summary>
        private float GetHalfHeight(PrimitiveType primitive, Vector3 scale)
        {
            return primitive == PrimitiveType.Cylinder ? scale.y * 1f : scale.y * 0.5f;
        }

        /// <summary>
        /// 자원 종류별 보조 파츠를 덧붙여 기본 프리미티브만으로는 부족한 디테일(마디/부채꼴 잎/볼트 등)을
        /// 더한다. 파츠는 순수 시각용이라 콜라이더를 만들지 않고(AddPart에서 제거), 부모의 상호작용용
        /// 콜라이더와 절대 간섭하지 않는다.
        /// </summary>
        private void AddResourceDetailParts(GameObject go, string itemName, Vector3 parentScale, Color color, string textureName, System.Random rng)
        {
            switch (itemName)
            {
                case "나뭇가지":
                    // 퀄리티 개선: 곁가지 개수(1~3개)와 각도를 스폰마다 무작위로 바꿔, 같은 나뭇가지라도
                    // 어떤 건 가지가 많고 어떤 건 홑가지처럼 보이게 해서 클론처럼 보이지 않게 했다.
                    {
                        int twigCount = rng.NextInt(1, 4);
                        for (int i = 0; i < twigCount; i++)
                        {
                            float baseAngle = 55f + i * 40f;
                            float jitter = rng.NextFloat(-15f, 15f);
                            Vector3 pos = new Vector3(rng.NextFloat(-0.06f, 0.06f), rng.NextFloat(-0.02f, 0.05f), rng.NextFloat(-0.04f, 0.04f));
                            AddPart(go, $"Twig{i}", PrimitiveType.Cylinder, pos, new Vector3(0.07f, rng.NextFloat(0.22f, 0.32f), 0.07f), parentScale, Quaternion.Euler(15f, 0f, (i % 2 == 0 ? 1f : -1f) * baseAngle + jitter), color, textureName);
                        }
                        break;
                    }

                case "대나무":
                    // 퀄리티 개선: 마디 개수(2~4개)를 무작위로 바꿔 대나무 길이감이 다양해 보이게 했다.
                    {
                        int jointCount = rng.NextInt(2, 5);
                        for (int i = 0; i < jointCount; i++)
                        {
                            float t = (float)i / Mathf.Max(1, jointCount - 1); // 0~1
                            float y = Mathf.Lerp(-0.7f, 0.55f, t);
                            AddPart(go, $"Joint{i}", PrimitiveType.Cylinder, new Vector3(0f, y, 0f), new Vector3(1.15f, 0.02f, 1.15f), parentScale, Quaternion.identity, color * 0.75f, textureName);
                        }
                        break;
                    }

                case "돌조각":
                    // 퀄리티 개선: 곁돌 개수(0~3개)를 무작위로 바꿔 돌무더기 크기가 다양해 보이게 했다.
                    {
                        int rockCount = rng.NextInt(0, 4);
                        Vector3[] offsets = { new Vector3(0.35f, -0.15f, 0.1f), new Vector3(-0.3f, -0.18f, -0.15f), new Vector3(0.1f, -0.12f, -0.32f) };
                        for (int i = 0; i < rockCount && i < offsets.Length; i++)
                        {
                            float size = rng.NextFloat(0.16f, 0.28f);
                            AddPart(go, $"Rock{i + 2}", PrimitiveType.Sphere, offsets[i], new Vector3(size, size * 0.75f, size), parentScale, Quaternion.identity, color, textureName);
                        }
                        break;
                    }

                case "코코넛":
                    // 퀄리티 개선: 열매가 1개짜리 노드도, 3개까지 뭉친 노드도 나오게 해서 다발 크기가 다양해 보이게 했다.
                    {
                        int extraCount = rng.NextInt(0, 3);
                        Vector3[] offsets = { new Vector3(0.4f, -0.05f, 0.1f), new Vector3(-0.35f, -0.08f, 0.25f) };
                        for (int i = 0; i < extraCount && i < offsets.Length; i++)
                            AddPart(go, $"Coconut{i + 2}", PrimitiveType.Sphere, offsets[i], new Vector3(0.38f, 0.38f, 0.38f), parentScale, Quaternion.identity, color, textureName);
                        break;
                    }

                case "천조각":
                    // 퀄리티 개선: 접힌 주름이 있을 때도(70% 확률) 없을 때도 있게 해 밋밋한 조각과 구겨진 조각이 섞여 보이게 했다.
                    if (rng.NextValue01() < 0.7f)
                        AddPart(go, "Fold", PrimitiveType.Cube, new Vector3(0.05f, 0.3f, -0.05f), new Vector3(0.4f, 0.05f, 0.3f), parentScale, Quaternion.Euler(0f, rng.NextFloat(0f, 36f), 3f), color * 0.92f, textureName);
                    break;

                case "야자잎":
                    {
                        // 퀄리티 개선: 잎사귀 개수(4~6장)와 부채꼴 각도 간격, 개별 잎 길이를 무작위로 바꿔
                        // 같은 야자잎이라도 풍성해 보이는 것과 성긴 것이 섞여 보이게 했다.
                        int leafCount = rng.NextInt(4, 7);
                        float spread = rng.NextFloat(100f, 140f); // 부채꼴 전체 펼침 각도
                        for (int i = 0; i < leafCount; i++)
                        {
                            float angle = -spread * 0.5f + spread * i / Mathf.Max(1, leafCount - 1);
                            float rad = angle * Mathf.Deg2Rad;
                            float leafLength = rng.NextFloat(0.45f, 0.62f);
                            Vector3 localPos = new Vector3(Mathf.Sin(rad) * 0.26f, 0.1f, Mathf.Cos(rad) * 0.26f);
                            Quaternion rot = Quaternion.Euler(-20f, angle, 0f);
                            AddPart(go, $"Leaf{i}", PrimitiveType.Cube, localPos, new Vector3(0.05f, 0.02f, leafLength), parentScale, rot, color, textureName);
                        }
                        break;
                    }

                case "금속조각":
                    // 퀄리티 개선: 구부러진 정도(각도)를 무작위로 바꿔 찌그러진 모양이 조금씩 다르게 보이게 했다.
                    AddPart(go, "Bend", PrimitiveType.Cube, new Vector3(-0.05f, 0.4f, 0.05f), new Vector3(0.32f, 0.06f, 0.22f), parentScale, Quaternion.Euler(0f, rng.NextFloat(-50f, -20f), 8f), color * 0.85f, textureName);
                    break;

                case "부력통":
                    AddPart(go, "Rim", PrimitiveType.Cylinder, new Vector3(0f, 0.85f, 0f), new Vector3(1.08f, 0.04f, 1.08f), parentScale, Quaternion.identity, color * 0.8f, textureName);
                    break;

                case "비상식량":
                    AddPart(go, "Label", PrimitiveType.Cube, new Vector3(0f, 0.1f, 0.51f), new Vector3(0.26f, 0.1f, 0.02f), parentScale, Quaternion.identity, Color.white * 0.9f, "noise");
                    break;

                case "연료":
                    AddPart(go, "Spout", PrimitiveType.Cylinder, new Vector3(0.08f, 0.62f, 0f), new Vector3(0.14f, 0.12f, 0.14f), parentScale, Quaternion.identity, color * 0.85f, textureName);
                    break;

                case "엔진부품":
                    // 퀄리티 개선: 볼트 개수(3~6개)를 무작위로 바꿔 부품마다 조립 상태가 달라 보이게 했다.
                    {
                        int boltCount = rng.NextInt(3, 7);
                        for (int i = 0; i < boltCount; i++)
                        {
                            float rad = i * (360f / boltCount) * Mathf.Deg2Rad;
                            Vector3 localPos = new Vector3(Mathf.Cos(rad) * 0.24f, 0.05f, Mathf.Sin(rad) * 0.24f);
                            AddPart(go, $"Bolt{i}", PrimitiveType.Cube, localPos, new Vector3(0.06f, 0.06f, 0.06f), parentScale, Quaternion.identity, color * 0.8f, textureName);
                        }
                        break;
                    }
            }
        }

        /// <summary>
        /// 순수 시각용 보조 파츠 하나를 만들어 parent의 자식으로 붙인다. worldSize를 parentScale로 나눠
        /// 자식의 localScale로 지정하면, 부모가 비균일 스케일(예: 얇고 넓은 큐브)이어도 파츠가 찌그러지지
        /// 않고 의도한 크기로 보인다(CreatureSpawner.AddCompensated와 동일한 보정 방식).
        /// 자동으로 붙는 콜라이더는 즉시 제거해 부모의 상호작용용 콜라이더와 간섭하지 않게 한다.
        /// </summary>
        private void AddPart(GameObject parent, string name, PrimitiveType primitive, Vector3 localPosition,
            Vector3 worldSize, Vector3 parentScale, Quaternion localRotation, Color color, string textureName)
        {
            var part = GameObject.CreatePrimitive(primitive);
            part.name = name;
            part.transform.SetParent(parent.transform, false);
            part.transform.localPosition = localPosition;
            part.transform.localRotation = localRotation;
            part.transform.localScale = new Vector3(
                worldSize.x / Mathf.Max(0.0001f, parentScale.x),
                worldSize.y / Mathf.Max(0.0001f, parentScale.y),
                worldSize.z / Mathf.Max(0.0001f, parentScale.z));

            var collider = part.GetComponent<Collider>();
            if (collider != null)
                Object.Destroy(collider);

            var renderer = part.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.material.color = color;
                var tex = Resources.Load<Texture2D>($"Textures/{textureName}");
                if (tex != null)
                {
                    renderer.material.mainTexture = tex;
                    renderer.material.mainTextureScale = new Vector2(1.5f, 2f);
                }
            }
        }

        /// <summary>
        /// 아이템이 어떤 표면 질감 텍스처(Resources/Textures/*)를 씌울지 결정한다.
        /// B3-9: game-designer의 Spec_B2_11_MaterialFamilyField.md 권장 매핑에 따라, ItemData.materialFamily
        /// 필드가 설정돼 있으면(None이 아니면) 그 값을 우선 참조한다. 필드가 아직 None인 경우(43개의
        /// 기존 .asset이 이 필드가 추가되기 전부터 있었으므로 game-designer가 값을 채우기 전까지는 전부
        /// None이다) 예전과 동일한 itemName 문자열 추론 로직(GetSurfaceTextureNameFromName)으로 폴백해,
        /// .asset 값이 채워지기 전까지는 동작이 전혀 바뀌지 않는다.
        /// </summary>
        private string GetSurfaceTextureName(ItemData item)
        {
            if (item == null)
                return "noise";

            switch (item.materialFamily)
            {
                case MaterialFamily.Wood: return "wood";
                case MaterialFamily.Stone: return "stone";
                case MaterialFamily.Metal: return "metal";
                case MaterialFamily.Fiber: return "leaf";
                case MaterialFamily.Fruit: return "noise";
                case MaterialFamily.Supply: return "noise";
                case MaterialFamily.None:
                default:
                    return GetSurfaceTextureNameFromName(item.itemName);
            }
        }

        /// <summary>
        /// (B3-9 이전 로직, materialFamily가 None일 때의 폴백) 아이템 이름을 보고 어떤 표면 질감
        /// 텍스처(Resources/Textures/*)를 씌울지 추론한다. 처음에는 wood/stone/noise 3종뿐이었는데,
        /// 금속과 잎/식물류가 돌·나무와 뭉뚱그려져 있어 leaf(잎맥 얼룩)와 metal(브러시드 메탈 스크래치)을
        /// 추가로 분리했다.
        /// </summary>
        private string GetSurfaceTextureNameFromName(string itemName)
        {
            if (string.IsNullOrEmpty(itemName))
                return "noise";

            if (itemName.Contains("금속조각"))
                return "metal";

            if (itemName.Contains("야자잎"))
                return "leaf";

            if (itemName.Contains("나뭇가지") || itemName.Contains("대나무"))
                return "wood";

            if (itemName.Contains("돌조각") || itemName.Contains("부싯돌"))
                return "stone";

            return "noise";
        }

        /// <summary>
        /// 섬 규모에 대응하는 자원 개수 배율을 반환한다.
        /// 긴급 정정(#2 회귀 수정): 인스펙터(씬 직렬화)에 설정된 필드 값을 항상 우선한다. 필드가 0
        /// 이하로 남아있어(설정 실수/아직 배치 안 된 새 컴포넌트 등) 의미 있게 설정되지 않은 경우에만
        /// IslandSizeMetrics.GetAreaProportionalMultiplier를 안전한 기본값 폴백으로 사용한다.
        /// </summary>
        private float GetMultiplier(IslandSize size)
        {
            switch (size)
            {
                case IslandSize.Small: return smallMultiplier > 0f ? smallMultiplier : IslandSizeMetrics.GetAreaProportionalMultiplier(size);
                case IslandSize.Medium: return mediumMultiplier > 0f ? mediumMultiplier : IslandSizeMetrics.GetAreaProportionalMultiplier(size);
                case IslandSize.Large: return largeMultiplier > 0f ? largeMultiplier : IslandSizeMetrics.GetAreaProportionalMultiplier(size);
                case IslandSize.ExtraLarge: return extraLargeMultiplier > 0f ? extraLargeMultiplier : IslandSizeMetrics.GetAreaProportionalMultiplier(size);
                default: return smallMultiplier > 0f ? smallMultiplier : IslandSizeMetrics.GetAreaProportionalMultiplier(size);
            }
        }

        /// <summary>
        /// 섬 규모에 대응하는 자원 산포 반경을 반환한다.
        /// 긴급 정정(#2 회귀 수정): 인스펙터(씬 직렬화)에 설정된 필드 값을 항상 우선한다. 필드가 0 이하로
        /// 남아있을 때만 IslandSizeMetrics.GetScatterRadius를 안전한 기본값 폴백으로 사용한다.
        /// </summary>
        private float GetScatterRadius(IslandSize size)
        {
            switch (size)
            {
                case IslandSize.Small: return smallScatterRadius > 0f ? smallScatterRadius : IslandSizeMetrics.GetScatterRadius(size);
                case IslandSize.Medium: return mediumScatterRadius > 0f ? mediumScatterRadius : IslandSizeMetrics.GetScatterRadius(size);
                case IslandSize.Large: return largeScatterRadius > 0f ? largeScatterRadius : IslandSizeMetrics.GetScatterRadius(size);
                case IslandSize.ExtraLarge: return extraLargeScatterRadius > 0f ? extraLargeScatterRadius : IslandSizeMetrics.GetScatterRadius(size);
                default: return smallScatterRadius > 0f ? smallScatterRadius : IslandSizeMetrics.GetScatterRadius(size);
            }
        }
    }
}
