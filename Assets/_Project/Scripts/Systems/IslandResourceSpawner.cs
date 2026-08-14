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

        [Header("섬 규모별 배치 배율")]
        public float smallMultiplier = 1f;
        public float mediumMultiplier = 2f;
        public float largeMultiplier = 3f;
        public float extraLargeMultiplier = 4f;

        [Tooltip("자원 노드를 흩뿌릴 반경 (섬 플레이스홀더 크기에 맞춰 조절)")]
        public float scatterRadius = 8f;

        /// <summary>
        /// 지정한 섬 인스턴스 위에 규모에 맞는 개수만큼 자원 노드를 생성한다.
        /// 각 노드는 섬 위치를 중심으로 scatterRadius 반경 안에 무작위 배치된다.
        /// </summary>
        public List<ResourceNode> SpawnResourcesForIsland(IslandInstance island, Transform parent)
        {
            var spawned = new List<ResourceNode>();
            if (island == null)
                return spawned;

            float multiplier = GetMultiplier(island.size);

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
                    Vector2 offset = Random.insideUnitCircle * scatterRadius;
                    Vector3 position = island.mapPosition + new Vector3(offset.x, 0f, offset.y);
                    position = TerrainSampler.SnapToGround(position);
                    spawned.Add(SpawnSingleNode(entry, position, parent));
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
        private ResourceNode SpawnSingleNode(ResourceEntry entry, Vector3 position, Transform parent)
        {
            ItemData yieldItem = entry.yieldItem;
            string itemName = yieldItem.itemName;

            GetNodeShape(itemName, out PrimitiveType primitive, out Vector3 scale, out Quaternion rotation);

            GameObject go = GameObject.CreatePrimitive(primitive);
            go.transform.SetParent(parent);
            go.transform.localScale = scale;
            go.transform.rotation = rotation;
            go.transform.position = position + Vector3.up * GetHalfHeight(primitive, scale); // 프리미티브 종류별 반높이만큼 띄워 지형 위에 놓이게 한다
            go.name = $"Resource_{itemName}";

            // 아이템 종류(무기/음식/음료/설치형/이동수단/일반 재료)에 맞는 색을 입혀 카테고리 단위로 구분한다.
            Color color = UIBuilder.GetItemCategoryColor(yieldItem);
            string textureName = GetSurfaceTextureName(itemName);

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

            AddResourceDetailParts(go, itemName, scale, color, textureName);

            var node = go.AddComponent<ResourceNode>();
            node.yieldItem = yieldItem;
            node.remainingHarvestCount = node.maxHarvestCount;
            node.requiresTool = entry.requiresTool;
            node.requiredTool = entry.requiredTool;
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
        private void AddResourceDetailParts(GameObject go, string itemName, Vector3 parentScale, Color color, string textureName)
        {
            switch (itemName)
            {
                case "나뭇가지":
                    AddPart(go, "Twig2", PrimitiveType.Cylinder, new Vector3(0.05f, 0.05f, 0.02f), new Vector3(0.07f, 0.3f, 0.07f), parentScale, Quaternion.Euler(15f, 0f, 55f), color, textureName);
                    AddPart(go, "Twig3", PrimitiveType.Cylinder, new Vector3(-0.05f, 0.02f, -0.03f), new Vector3(0.07f, 0.25f, 0.07f), parentScale, Quaternion.Euler(-10f, 0f, -50f), color, textureName);
                    break;

                case "대나무":
                    AddPart(go, "Joint0", PrimitiveType.Cylinder, new Vector3(0f, -0.7f, 0f), new Vector3(1.15f, 0.02f, 1.15f), parentScale, Quaternion.identity, color * 0.75f, textureName);
                    AddPart(go, "Joint1", PrimitiveType.Cylinder, new Vector3(0f, -0.1f, 0f), new Vector3(1.15f, 0.02f, 1.15f), parentScale, Quaternion.identity, color * 0.75f, textureName);
                    AddPart(go, "Joint2", PrimitiveType.Cylinder, new Vector3(0f, 0.55f, 0f), new Vector3(1.15f, 0.02f, 1.15f), parentScale, Quaternion.identity, color * 0.75f, textureName);
                    break;

                case "돌조각":
                    AddPart(go, "Rock2", PrimitiveType.Sphere, new Vector3(0.35f, -0.15f, 0.1f), new Vector3(0.28f, 0.2f, 0.28f), parentScale, Quaternion.identity, color, textureName);
                    AddPart(go, "Rock3", PrimitiveType.Sphere, new Vector3(-0.3f, -0.18f, -0.15f), new Vector3(0.22f, 0.16f, 0.22f), parentScale, Quaternion.identity, color, textureName);
                    break;

                case "코코넛":
                    AddPart(go, "Coconut2", PrimitiveType.Sphere, new Vector3(0.4f, -0.05f, 0.1f), new Vector3(0.38f, 0.38f, 0.38f), parentScale, Quaternion.identity, color, textureName);
                    break;

                case "천조각":
                    AddPart(go, "Fold", PrimitiveType.Cube, new Vector3(0.05f, 0.3f, -0.05f), new Vector3(0.4f, 0.05f, 0.3f), parentScale, Quaternion.Euler(0f, 18f, 3f), color * 0.92f, textureName);
                    break;

                case "야자잎":
                    {
                        // 줄기 기준으로 5장의 잎사귀를 부채꼴로 퍼뜨린다. 각 잎은 줄기 쪽 끝이 중심에 붙도록
                        // 회전 방향으로 절반 길이만큼 이동시키고, 살짝 위로 들려 보이게 X축을 기울인다.
                        float[] fanAngles = { -60f, -30f, 0f, 30f, 60f };
                        for (int i = 0; i < fanAngles.Length; i++)
                        {
                            float rad = fanAngles[i] * Mathf.Deg2Rad;
                            Vector3 localPos = new Vector3(Mathf.Sin(rad) * 0.26f, 0.1f, Mathf.Cos(rad) * 0.26f);
                            Quaternion rot = Quaternion.Euler(-20f, fanAngles[i], 0f);
                            AddPart(go, $"Leaf{i}", PrimitiveType.Cube, localPos, new Vector3(0.05f, 0.02f, 0.55f), parentScale, rot, color, textureName);
                        }
                        break;
                    }

                case "금속조각":
                    AddPart(go, "Bend", PrimitiveType.Cube, new Vector3(-0.05f, 0.4f, 0.05f), new Vector3(0.32f, 0.06f, 0.22f), parentScale, Quaternion.Euler(0f, -35f, 8f), color * 0.85f, textureName);
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
                    for (int i = 0; i < 4; i++)
                    {
                        float rad = i * 90f * Mathf.Deg2Rad;
                        Vector3 localPos = new Vector3(Mathf.Cos(rad) * 0.24f, 0.05f, Mathf.Sin(rad) * 0.24f);
                        AddPart(go, $"Bolt{i}", PrimitiveType.Cube, localPos, new Vector3(0.06f, 0.06f, 0.06f), parentScale, Quaternion.identity, color * 0.8f, textureName);
                    }
                    break;
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
        /// 아이템 이름을 보고 어떤 표면 질감 텍스처(Resources/Textures/*)를 씌울지 결정한다.
        /// 처음에는 wood/stone/noise 3종뿐이었는데, 금속과 잎/식물류가 돌·나무와 뭉뚱그려져 있어
        /// leaf(잎맥 얼룩)와 metal(브러시드 메탈 스크래치)을 추가로 분리했다.
        /// </summary>
        private string GetSurfaceTextureName(string itemName)
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
        /// </summary>
        private float GetMultiplier(IslandSize size)
        {
            switch (size)
            {
                case IslandSize.Small: return smallMultiplier;
                case IslandSize.Medium: return mediumMultiplier;
                case IslandSize.Large: return largeMultiplier;
                case IslandSize.ExtraLarge: return extraLargeMultiplier;
                default: return smallMultiplier;
            }
        }
    }
}
