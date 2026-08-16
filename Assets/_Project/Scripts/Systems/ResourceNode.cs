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

        // [game-designer 요청 - Design_BalancePass 3장] 보너스 도구(가산 방식). requiredTool과 목적이
        // 정반대라는 점이 이 두 필드의 전부다:
        //   requiredTool = 없으면 채집이 "거부"된다(GetHarvestFailure.MissingTool).
        //   bonusTool    = 없어도 채집은 100% 성공하고, 있으면 수확량만 늘어난다.
        // 그래서 bonusTool은 GetHarvestFailure에 절대 들어가지 않는다 - 판정에 한 번이라도 들어가는
        // 순간 "도구를 잃으면 그 자원 경로가 통째로 죽는다"는 잠금 위험이 되살아나고, 3장의 잠금 반증
        // (최악의 결과가 '느려짐'이지 '경로 소멸'이 아니다)이 무효가 된다.
        //
        // 기본값이 곧 "보너스 없음"이다(bonusTool = null, bonusYieldPerHarvest = 0). 두 조건을 AND로
        // 묶어 검사하므로 둘 중 어느 쪽이 비어 있어도 가산은 0이다 - 이 필드들이 추가되기 전부터
        // 존재하던 씬/세이브의 노드는 역직렬화 시 어느 쪽으로 채워지든(초기화식 값이든 0/null이든)
        // 결과가 같아서, 씬을 고치기 전까지 기존 동작이 1도 바뀌지 않는다.
        [Tooltip("보유하고 있으면 이 노드의 채집량이 늘어나는 보너스 도구 (예: 야자잎에 칼).\n" +
            "requiredTool과 달리 없어도 채집은 정상적으로 성공한다 - 수확량 가산에만 관여한다.")]
        public ItemData bonusTool;

        [Tooltip("bonusTool을 보유했을 때 1회 채집당 추가로 얻는 개수. 0이면 보너스가 없다(기본값).")]
        public int bonusYieldPerHarvest = 0;

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
        /// [game-designer 최우선 요청] 채집 시도가 거부된 이유. Harvest()가 그냥 false를 반환하던 시절에는
        /// "왜 안 되는지"를 아무도 알 수 없었고(소리도 문구도 없었다) 플레이어는 그것을 버그로 읽었다.
        ///
        /// None을 제외한 값은 전부 "지금은 안 되지만 조건만 갖추면 된다"는 뜻이며, 밸런스 조건 자체는
        /// 예전과 100% 동일하다 - 이 enum은 이미 존재하던 판정의 결과를 이름 붙여 밖으로 내보낼 뿐이다.
        /// </summary>
        public enum HarvestFailure
        {
            /// <summary>실패가 아니다 - 지금 채집할 수 있다.</summary>
            None = 0,

            /// <summary>남은 채집 횟수가 0이다. respawnSeconds가 지나면 저절로 다시 찬다.</summary>
            Depleted = 1,

            /// <summary>requiredTool(예: 손도끼)을 인벤토리에 갖고 있지 않다.</summary>
            MissingTool = 2,

            /// <summary>인벤토리 참조가 없다(호출부 배선 오류). 플레이어가 해결할 수 있는 사유가 아니다.</summary>
            NoInventory = 3,

            /// <summary>이 노드에 지급할 yieldItem이 설정되지 않았다(스포너/데이터 오류). 역시 플레이어 잘못이 아니다.</summary>
            NoYieldItem = 4,

            /// <summary>
            /// 인벤토리에 빈 칸이 없어 수확물을 받을 수 없다.
            /// [B18] 용량 도입과 함께 생겼다. 이 검사가 없으면 Harvest()가 remainingHarvestCount를
            /// 깎은 뒤 AddItem이 조용히 거부해 **수확물이 증발한다** - 이 프로젝트가 이미 낸
            /// "채집이 반응 없이 무시된다" 사고와 같은 유형이다.
            /// </summary>
            InventoryFull = 5,
        }

        /// <summary>
        /// 채집 시도가 실패한 순간 발생한다. 첫 인자는 실패한 노드, 둘째는 그 사유다.
        /// 화면 표시(조준 시 사유 문구)는 InteractionPromptUI가 이미 담당하므로 이 이벤트는
        /// "E를 실제로 눌렀는데 거부당한 순간"에만 발생한다 - 조준만 하고 있을 때는 발생하지 않는다.
        /// 튜토리얼 힌트·실패 로그·통계 수집처럼 나중에 붙는 시스템이 구독할 수 있게 열어 둔다.
        ///
        /// 주의(static 이벤트): 씬을 다시 로드해도 구독은 자동으로 끊기지 않는다. 씬과 함께 생성되는
        /// MonoBehaviour가 구독한다면 반드시 OnDisable/OnDestroy에서 -= 로 해제할 것. 해제하지 않으면
        /// 파괴된 오브젝트를 가리키는 델리게이트가 남아 사망 후 재시작 시 MissingReferenceException이 난다.
        /// </summary>
        public static event System.Action<ResourceNode, HarvestFailure> HarvestFailed;

        /// <summary>
        /// 채집이 성공한 순간 발생한다(인자는 성공한 노드). 성공 효과음/파티클은 Harvest 안에서 이미
        /// 처리하므로 이 이벤트는 부가 시스템(퀘스트 진행도 등)을 위한 것이다.
        /// static 이벤트 해제 주의사항은 HarvestFailed와 동일하다.
        /// </summary>
        public static event System.Action<ResourceNode> Harvested;

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
            // [game-designer 최우선 요청] 조건 판정을 GetHarvestFailure 한 곳으로 모았다. 예전 코드와
            // 검사 항목·순서·조건이 완전히 동일하며(고갈 -> 인벤토리 -> yieldItem -> 도구), 달라진 것은
            // "왜 실패했는지"를 알 수 있게 된 것뿐이다. 채집 가능 조건 자체는 1도 건드리지 않았다.
            HarvestFailure failure = GetHarvestFailure(inventory);
            if (failure != HarvestFailure.None)
            {
                ReportHarvestFailure(failure);
                return false;
            }

            InventoryItem toolItem = null;
            if (requiresTool && requiredTool != null)
                toolItem = inventory.FindItem(requiredTool);

            // [game-designer 요청 - 3장] 보너스 도구 가산. 이 시점에는 이미 GetHarvestFailure가 None을
            // 돌려준 뒤이므로, 보너스는 "성공한 채집이 몇 개를 주는가"에만 영향을 준다.
            int totalYield = GetEffectiveYield(inventory);
            for (int i = 0; i < totalYield; i++)
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

            Harvested?.Invoke(this);
            return true;
        }

        /// <summary>
        /// 지금 이 인벤토리로 채집을 시도하면 1회 채집에 실제로 몇 개를 받게 되는지 알려준다
        /// (기본 수확량 + 보너스 도구 가산). Harvest()가 실제로 쓰는 바로 그 계산이며, 상태를 전혀
        /// 바꾸지 않으므로 조준 프롬프트가 매 프레임 호출해도 안전하다.
        ///
        /// [ui-engineer 요청 - Design_BalancePass 3장] "1회당 N개" 표시가 이 값을 쓰지 않으면
        /// 보너스 도구 설계 전체가 플레이어에게 보이지 않는 기능이 된다 - 발견 경로가 이 숫자 하나뿐이다.
        /// inventory가 null이면 보너스 없이 기본 수확량만 돌려준다.
        /// </summary>
        public int GetEffectiveYield(PlayerInventory inventory)
        {
            int bonus = 0;
            if (bonusTool != null && bonusYieldPerHarvest > 0 && inventory != null
                && inventory.FindItem(bonusTool) != null)
            {
                bonus = bonusYieldPerHarvest;
            }

            // yieldPerHarvest 자체는 손대지 않는다 - 이 메서드가 하는 일은 "기존 수확량에 가산항을
            // 더하는 것" 하나뿐이며, 보너스가 없을 때의 반환값은 예전 for 루프의 상한과 완전히 동일하다.
            return yieldPerHarvest + bonus;
        }

        /// <summary>
        /// 지금 이 인벤토리로 채집을 시도하면 어떤 사유로 거부되는지 미리 알려준다(None이면 채집 가능).
        /// Harvest()가 실제로 쓰는 바로 그 판정이며, 유일한 판정 소스다 - UI가 같은 조건을 따로 구현하면
        /// 언젠가 화면 표시와 실제 동작이 갈라지므로 반드시 이 메서드를 통해 물어볼 것.
        /// 상태를 전혀 바꾸지 않으므로 매 프레임 호출해도 안전하다(소리도 나지 않는다).
        /// </summary>
        public HarvestFailure GetHarvestFailure(PlayerInventory inventory)
        {
            // 아래 순서는 예전 Harvest()의 조건 순서를 그대로 옮긴 것이다. 순서를 바꾸면 고갈된 노드에
            // 도구가 없을 때 표시되는 사유가 달라지므로 임의로 재배치하지 말 것.
            if (!CanHarvest)
                return HarvestFailure.Depleted;

            if (inventory == null)
                return HarvestFailure.NoInventory;

            if (yieldItem == null)
                return HarvestFailure.NoYieldItem;

            if (requiresTool && requiredTool != null && inventory.FindItem(requiredTool) == null)
                return HarvestFailure.MissingTool;

            // [B18] 도구 검사 **뒤**에 둔다. 도구도 없고 가방도 찼을 때는 도구 쪽을 먼저 알려주는 것이
            // 맞다(도구가 없으면 어차피 못 캔다). CanAccept는 상태를 바꾸지 않아 매 프레임 호출해도 안전하다.
            if (!inventory.CanAccept(yieldItem, GetEffectiveYield(inventory)))
                return HarvestFailure.InventoryFull;

            return HarvestFailure.None;
        }

        /// <summary>
        /// 실패 사유를 소리와 이벤트로 밖에 알린다. 화면 문구는 InteractionPromptUI가 조준 단계에서 이미
        /// 보여주므로 여기서 UI를 직접 만들지 않는다(UI 소유권 분리). 이 메서드가 하는 일은 두 가지뿐이다:
        /// (1) "입력은 분명히 처리됐고 다만 거부됐다"를 알리는 실패음 재생,
        /// (2) 다른 시스템이 붙을 수 있는 HarvestFailed 이벤트 발행.
        /// </summary>
        private void ReportHarvestFailure(HarvestFailure failure)
        {
            // 사유가 무엇이든 소리는 하나로 통일한다. 사유별로 음을 나누면 플레이어가 네 가지 소리를
            // 새로 외워야 하는데, 정확한 사유는 이미 조준 프롬프트에 글자로 떠 있으므로 이득이 없다.
            // 여기서 소리가 맡는 역할은 "무반응이 아니다"를 0.16초 안에 확실히 알리는 것 하나다.
            AudioManager.Instance?.PlayActionFail();

            HarvestFailed?.Invoke(this, failure);
        }
    }
}
