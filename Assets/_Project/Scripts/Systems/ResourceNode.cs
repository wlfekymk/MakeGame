using UnityEngine;
using MakeGame.Data;
using MakeGame.Player;

namespace MakeGame.Systems
{
    /// <summary>
    /// 섬에 배치되는 채집 가능한 자원 하나(나무, 바위, 덤불 등)를 나타낸다.
    /// 상호작용 시 지정된 재료 아이템을 인벤토리에 지급하고 채집 스킬 경험치를 준다.
    /// 채집 가능 횟수가 모두 소진되면 일정 시간 후 다시 채집 가능한 상태로 재생된다.
    /// </summary>
    public class ResourceNode : MonoBehaviour
    {
        [Tooltip("이 노드를 채집했을 때 얻는 재료 아이템")]
        public ItemData yieldItem;

        [Tooltip("1회 채집 시 얻는 재료 개수")]
        public int yieldPerHarvest = 1;

        [Tooltip("재생되기 전까지 채집 가능한 총 횟수")]
        public int maxHarvestCount = 3;

        [Tooltip("현재 남은 채집 가능 횟수")]
        public int remainingHarvestCount = 3;

        [Tooltip("채집 시 지급할 채집(Harvesting) 스킬 경험치")]
        public float harvestExperience = 5f;

        [Tooltip("모두 소진된 뒤 다시 채집 가능해지기까지 걸리는 시간(초)")]
        public float respawnSeconds = 60f;

        [Tooltip("채집에 도구가 필요한지 여부. true면 requiredTool을 인벤토리에 보유해야 채집할 수 있다.")]
        public bool requiresTool = false;

        [Tooltip("채집에 필요한 도구 아이템 (requiresTool이 true일 때만 사용, 예: 손도끼)")]
        public ItemData requiredTool;

        // B3-3: 이 노드를 배치한 섬 번호와, 그 섬 안에서 몇 번째로 생성됐는지(생성 순번). 절차적으로
        // 생성되는 노드라 고유한 프리팹/에셋 식별자가 없으므로, 이 두 값의 조합이 세이브 파일에서 노드
        // 하나를 다시 가리킬 수 있는 유일한 안정적인 키가 된다 - 같은 worldSeed로 재생성하면 항상 같은
        // (islandIndex, spawnOrder)에 같은 노드가 나온다는 전제가 있어야 성립하며, 이 전제는
        // IslandResourceSpawner가 섬별 결정적 System.Random을 쓰도록 바뀐 뒤에야 보장된다. B3-4(자원
        // 노드 채집 상태 저장)에서 이 값을 그대로 세이브 키로 쓴다. -1은 "아직 스포너가 설정하지 않음"을
        // 뜻하는 안전한 기본값(스포너 밖에서 수동으로 생성된 노드가 있어도 크래시하지 않도록).
        [Tooltip("이 노드를 배치한 섬 번호(IslandInstance.islandId). B3-4 세이브 키로 쓰인다.")]
        public int islandIndex = -1;

        [Tooltip("이 섬 안에서 몇 번째로 생성된 노드인지(생성 순번, 0부터). B3-4 세이브 키로 쓰인다.")]
        public int spawnOrder = -1;

        private float respawnTimer = 0f;

        /// <summary>이 노드에 실루엣 보강 파츠를 이미 붙였는지(중복 생성 방지용).</summary>
        private bool silhouetteBuilt = false;

        /// <summary>
        /// 노드 하나가 쓸 수 있는 프리미티브 총 개수 상한(루트 포함). 섬 하나에 자원 노드가 수십 개
        /// 깔리므로(씬 실측 배율 1/2/3/4 × baseCount 합계 20 = 특대 섬 기준 80개 안팎) 노드당 파츠가
        /// 하나 늘 때마다 드로우콜이 80개씩 늘어난다. 아래 실루엣 보강은 항상 이 예산 안에서만 파츠를
        /// 추가하고, 예산이 남지 않으면 아무 것도 하지 않는다.
        /// </summary>
        private const int MaxVisualPrimitives = 4;

        /// <summary>현재 채집이 가능한 상태인지 여부(남은 횟수가 있는지).</summary>
        public bool CanHarvest => remainingHarvestCount > 0;

        /// <summary>
        /// 스포너가 yieldItem을 채워 넣은 뒤(AddComponent 직후 대입되므로 Awake 시점에는 아직 비어 있다)
        /// 실행되는 시각 보강 단계. 게임플레이 값은 일절 건드리지 않는다.
        /// </summary>
        private void Start()
        {
            BuildSilhouetteAccents();
        }

        /// <summary>
        /// 매 프레임 자동으로 재생 타이머를 진행시킨다.
        /// </summary>
        private void Update()
        {
            Tick(Time.deltaTime);
        }

        /// <summary>
        /// 자원 종류별로 "멀리서 실루엣만 보고 무엇인지 알 수 있게" 하는 보강 파츠를 붙인다.
        ///
        /// 왜 필요한가: IslandResourceSpawner.GetNodeShape가 이미 종류별로 프리미티브를 나눠 놓았지만,
        /// 실측해 보면 천조각(0.55×0.05×0.40)·부싯돌(0.32×0.10×0.42)·금속조각(0.50×0.06×0.34)이 전부
        /// "지면에 깔린 납작한 판"이라 20m 밖에서는 같은 형태다. 셋 다 크기도 0.3~0.5m로 비슷하다.
        /// 색(UIBuilder.GetItemCategoryColor)만으로는 (a) 색맹 대응이 안 되고 (b) 야간(nightIntensity
        /// 0.10)에는 색 자체가 읽히지 않는다 - ArtDirection 2장이 "형태 종류 + 배치 패턴"을 유일한
        /// 실루엣 전략으로 못박은 이유가 이것이다. 그래서 여기서는 색이 아니라 "높이"와 "형태 종류"를
        /// 각각 다르게 배분한다: 서 있는 천 / 각진 돌 파편 더미 / 세로 표식 막대.
        ///
        /// 특별 자원(금속조각·부력통·엔진부품)에는 공통으로 밝은 세로 표식 막대를 세운다. 이 셋은
        /// 대형 섬 이상에서만 나오고(금속조각은 손도끼까지 필요) 배 2·3단계와 경비행기 수리의 핵심
        /// 재료라, 못 찾으면 진행이 그대로 막힌다. 반면 실제 크기는 0.3~0.5m로 일반 자원 중에서도
        /// 작은 축이라 지면 잡동사니에 파묻힌다. 세로 0.95m 막대는 이 자원군에만 쓰는 전용 신호이며
        /// (다른 자원에는 절대 붙이지 않는다), 높이 + 밝은 미색이라 색과 무관하게, 밤에도 읽힌다.
        ///
        /// 게임플레이 영향 없음: 여기서 만드는 파츠는 StructureVisualBuilder.CreateVisualPart가
        /// 콜라이더를 즉시 제거한 순수 시각 오브젝트다. 채집 판정은 루트의 콜라이더 하나뿐이라
        /// 상호작용 범위/채집량/리스폰은 1도 바뀌지 않는다.
        /// </summary>
        private void BuildSilhouetteAccents()
        {
            if (silhouetteBuilt || yieldItem == null)
                return;
            silhouetteBuilt = true;

            Vector3 scale = transform.localScale;
            if (scale.x <= 0.0001f || scale.y <= 0.0001f || scale.z <= 0.0001f)
                return;

            // 루트 프리미티브의 바닥이 중심에서 몇 미터 아래인지. 큐브/구는 로컬 반높이가 0.5이고
            // 실린더/캡슐은 1이다(메시 높이가 2단위) - 이 차이를 반영하지 않으면 세워 붙인 파츠가
            // 공중에 뜨거나 땅에 묻힌다(IslandResourceSpawner.GetHalfHeight와 같은 보정).
            float bottom = -RootTopLocalY() * scale.y;
            int used = 1 + transform.childCount; // 루트 + 스포너가 이미 붙여둔 디테일 파츠

            switch (yieldItem.itemName)
            {
                case "금속조각":
                case "부력통":
                case "엔진부품":
                    // 엔진부품은 스포너가 볼트를 3~6개 붙여 이미 예산(4개)을 넘긴 상태다. 볼트는
                    // 5m 안에서만 보이는 근접 디테일이고 표식 막대는 20m 밖 식별용이므로,
                    // ArtDirection 2장 "디테일 밀도 규칙"(폴리곤은 언제나 멀리서 안 보이는 쪽에서 아낀다)
                    // 에 따라 볼트 개수를 줄여 그 예산을 막대에 넘긴다.
                    used = TrimDetailChildren(used, MaxVisualPrimitives - 1);
                    AddSalvageMarker(scale, bottom, ref used);
                    break;

                case "천조각":
                    // 납작한 판 + 위로 선 천자락 → 옆에서 봤을 때 ㄴ자. 같은 "납작한 판" 3종 중
                    // 유일하게 위로 솟은 부드러운 면을 갖는다.
                    AddAccent(ref used, "ClothFlap", PrimitiveType.Cube, scale,
                        new Vector3(-0.12f, bottom + 0.17f, 0.02f), new Vector3(0.30f, 0.34f, 0.03f),
                        StructureVisualBuilder.PalmFiber, "leaf");
                    break;

                case "부싯돌":
                    // 각진 파편이 서로 겹쳐 쌓인 낮은 더미. 천조각(부드럽게 선 면)·금속조각(세로 막대)과
                    // 달리 "낮고 각진 덩어리"로 읽힌다.
                    AddAccent(ref used, "Shard1", PrimitiveType.Cube, scale,
                        new Vector3(0.06f, bottom + 0.07f, 0.04f), new Vector3(0.16f, 0.14f, 0.10f),
                        StructureVisualBuilder.WeatheredStone, "stone");
                    AddAccent(ref used, "Shard2", PrimitiveType.Cube, scale,
                        new Vector3(-0.10f, bottom + 0.045f, -0.05f), new Vector3(0.10f, 0.09f, 0.16f),
                        StructureVisualBuilder.WeatheredStone * 0.8f, "stone");
                    break;

                case "코코넛":
                    // 완전한 구는 돌조각(눌린 구)과 실루엣이 겹친다. 꼭지 하나로 "열매"임을 표시한다.
                    AddAccent(ref used, "Husk", PrimitiveType.Cube, scale,
                        new Vector3(0.04f, -bottom + 0.03f, 0.03f), new Vector3(0.07f, 0.09f, 0.07f),
                        StructureVisualBuilder.Driftwood * 0.8f, "wood");
                    break;

                case "비상식량":
                    // 상자를 두른 결속 밴드 - 자연물에는 없는 "사람이 묶어 포장한 것" 신호.
                    AddAccent(ref used, "Strap", PrimitiveType.Cube, scale,
                        Vector3.zero, new Vector3(scale.x + 0.03f, scale.y + 0.03f, 0.06f),
                        StructureVisualBuilder.SupplyKhaki, "leaf");
                    break;

                case "연료":
                    // 손잡이 - 주둥이(스포너가 붙임)와 짝을 이뤄 제리캔 실루엣을 완성한다.
                    AddAccent(ref used, "Handle", PrimitiveType.Cube, scale,
                        new Vector3(-0.06f, -bottom + 0.02f, 0f), new Vector3(0.10f, 0.05f, 0.20f),
                        StructureVisualBuilder.SalvageMetal, "metal");
                    break;

                // 나뭇가지(가는 막대 다발)·대나무(마디 있는 긴 기둥)·야자잎(부채꼴)·돌조각(눌린 구 무더기)은
                // 이미 서로 다른 형태 원형을 하나씩 차지하고 있고 스포너 파츠만으로 예산이 거의 찼다.
                // 억지로 파츠를 더하지 않는다.
            }
        }

        /// <summary>
        /// 특별 자원(배 제작/비행기 수리 핵심 재료) 공용 표식. 노드 옆에 밝은 미색 세로 막대를 세우고,
        /// 예산이 남으면 가로대를 하나 더해 "누가 꽂아둔 표식"으로 읽히게 한다(자연물에는 없는 형태).
        /// 막대는 루트의 실제 가로폭 바깥에 세워 자원 자체를 가리지 않는다.
        /// </summary>
        private void AddSalvageMarker(Vector3 scale, float bottom, ref int used)
        {
            const float markerHeight = 0.95f;
            float lateral = Mathf.Max(scale.x, scale.z) * 0.5f + 0.1f;

            AddAccent(ref used, "SalvageMarker", PrimitiveType.Cube, scale,
                new Vector3(lateral, bottom + markerHeight * 0.5f, 0.04f),
                new Vector3(0.06f, markerHeight, 0.06f), StructureVisualBuilder.SalvageMarkerWhite, "noise");

            AddAccent(ref used, "SalvageMarkerBar", PrimitiveType.Cube, scale,
                new Vector3(lateral, bottom + markerHeight * 0.86f, 0.04f),
                new Vector3(0.22f, 0.05f, 0.05f), StructureVisualBuilder.SalvageMarkerWhite, "noise");
        }

        /// <summary>
        /// 예산이 남아 있을 때만 시각 파츠 하나를 붙인다. worldOffset/worldSize는 전부 미터 단위로
        /// 적으면 되도록, 루트의 비균일 스케일을 여기서 나눠 보정한다(IslandResourceSpawner.AddPart와
        /// 같은 방식). 회전은 일부러 지원하지 않는다 - 루트가 비균일 스케일(예: 0.5×0.06×0.34)이라
        /// 회전한 자식은 전단(shear)으로 찌그러지기 때문이다(CreatureVisualBuilder.AddBearDetails와 같은 이유).
        /// </summary>
        private void AddAccent(ref int used, string name, PrimitiveType primitive, Vector3 scale,
            Vector3 worldOffset, Vector3 worldSize, Color color, string textureName)
        {
            if (used >= MaxVisualPrimitives)
                return;

            Vector3 localPosition = new Vector3(worldOffset.x / scale.x, worldOffset.y / scale.y, worldOffset.z / scale.z);
            Vector3 localScale = new Vector3(worldSize.x / scale.x, worldSize.y / scale.y, worldSize.z / scale.z);
            StructureVisualBuilder.CreateVisualPart(transform, name, primitive, localPosition, localScale, color, null, textureName);
            used++;
        }

        /// <summary>
        /// 이미 붙어 있는 디테일 파츠를 뒤에서부터 지워 총 프리미티브 수를 keepTotal 이하로 낮추고,
        /// 남은 개수를 반환한다. Destroy는 프레임 끝에 반영되어 transform.childCount가 즉시 줄지 않으므로
        /// 개수는 반환값으로 직접 추적한다.
        /// </summary>
        private int TrimDetailChildren(int used, int keepTotal)
        {
            for (int i = transform.childCount - 1; i >= 0 && used > keepTotal; i--)
            {
                Destroy(transform.GetChild(i).gameObject);
                used--;
            }
            return used;
        }

        /// <summary>
        /// 루트 프리미티브의 로컬 반높이(큐브/구=0.5, 실린더/캡슐=1). 메시 이름으로 판별해,
        /// 나중에 스포너가 어떤 자원의 프리미티브 종류를 바꾸더라도 이 계산이 따라간다.
        /// </summary>
        private float RootTopLocalY()
        {
            var meshFilter = GetComponent<MeshFilter>();
            if (meshFilter != null && meshFilter.sharedMesh != null)
            {
                string meshName = meshFilter.sharedMesh.name;
                if (meshName.StartsWith("Cylinder") || meshName.StartsWith("Capsule"))
                    return 1f;
            }
            return 0.5f;
        }

        /// <summary>
        /// 소진된 노드를 시간 경과에 따라 재생시킨다.
        /// </summary>
        public void Tick(float deltaTime)
        {
            if (CanHarvest)
                return;

            respawnTimer += deltaTime;
            if (respawnTimer >= respawnSeconds)
            {
                remainingHarvestCount = maxHarvestCount;
                respawnTimer = 0f;
            }
        }

        /// <summary>
        /// 이 노드를 채집한다. 도구가 필요한 경우 인벤토리에 도구가 있는지 먼저 확인한다.
        /// 성공 시 인벤토리에 재료를 지급하고 채집 스킬 경험치를 주며, 남은 횟수를 1 줄인다.
        /// 버그 수정: 손도끼처럼 채집에도 쓰이고 전투에도 쓰이는 도구가 채집으로는 전혀 닳지 않던 문제를
        /// 고쳤다 - 요구 도구를 실제로 보유한 InventoryItem 인스턴스 하나를 찾아 내구도를 1 소모시킨다.
        /// </summary>
        public bool Harvest(PlayerInventory inventory, PlayerSkills skills)
        {
            if (!CanHarvest || inventory == null || yieldItem == null)
                return false;

            InventoryItem toolItem = null;
            if (requiresTool && requiredTool != null)
            {
                toolItem = inventory.FindItem(requiredTool);
                if (toolItem == null)
                    return false;
            }

            for (int i = 0; i < yieldPerHarvest; i++)
                inventory.AddItem(yieldItem);

            if (skills != null)
                skills.AddExperience(SkillType.Harvesting, harvestExperience);

            // 도구 내구도 소모: 무제한(IsUnlimited) 도구는 UseItem 내부에서 자동으로 소모되지 않는다.
            if (toolItem != null)
                inventory.UseItem(toolItem);

            remainingHarvestCount--;
            AudioManager.Instance?.PlayPickup(); // 채집 성공 효과음

            // B4-11: 채집이 성공한 그 순간, 노드 위치에 짧은 파티클 팝을 터뜨린다. 지금까지 채집 성공의
            // 유일한 신호가 효과음뿐이라 소리를 껐거나 여러 노드를 연달아 칠 때 "방금 게 먹혔는지"가
            // 보이지 않았다. 입자 색은 EffectBuilder가 노드 표면 색을 그대로 읽어 쓴다.
            EffectBuilder.PlayHarvestPop(gameObject);

            return true;
        }
    }
}
