using System.Collections.Generic;
using UnityEngine;
using MakeGame.Data;

namespace MakeGame.Systems
{
    /// <summary>
    /// 설치형 제작 시설의 종류. 값을 바꾸거나 중간에 끼워 넣지 말 것 - 세이브에 들어가지는 않지만
    /// (0.2.51에서 저장 배선 완료 — StructureType.Workbench/Furnace/Loom + SaveLoadController) 씬에 직렬화된
    /// CraftStation.kind가 정수로 저장되므로 순서가 바뀌면 이미 놓인 제작대의 종류가 뒤바뀐다.
    /// </summary>
    public enum CraftStationKind
    {
        Workbench = 0,  // 제작대
        Furnace = 1,    // 용광로
        Loom = 2        // 베틀
    }

    /// <summary>
    /// 플레이어가 설치하는 제작 시설(제작대 / 용광로 / 베틀) 하나.
    ///
    /// [기존 설치형 3종과 같은 규약을 따른다 - Campfire / WaterStill / Shelter]
    ///  · 설치 경로: 인벤토리의 키트 아이템(ItemData.isPlaceable) + InteractionController.placeKey(G).
    ///    G는 인벤토리에서 첫 설치형 아이템을 찾아 placementPrefab을 플레이어 앞 3m에 Instantiate한다.
    ///  · 활성 목록: Campfire.Active / WaterStill.Active와 완전히 같은 방식의 정적 목록을 둔다
    ///    (매 프레임 FindObjectsByType으로 씬을 훑지 않기 위한 것).
    ///  · 외형: 신규 모델/프리팹 없이 StructureVisualBuilder의 프리미티브 조합으로 Awake에서 조립한다
    ///    (WaterStill.BuildVisual과 같은 방식).
    ///
    /// [상호작용은 "조준 + E"가 아니라 "근처에 있기"다]
    /// 분기 순서 규약을 가진 InteractionController.cs가 이 작업의 락 밖이라 새 E 분기를 넣을 수 없다.
    /// 대신 제작 창(V)이 열릴 때 CraftingSystem이 <see cref="IsNear"/>로 반경 안에 해당 시설이 있는지만
    /// 본다 - 시설 앞에 서서 V를 누르면 그 시설의 고급 제작법이 풀린다. 새 키도, 새 입력 분기도 없다.
    /// 조준 프롬프트는 0.2.51에서 InteractionPromptUI에 배선됐다(이름 + [V] 제작 안내).
    ///
    /// [세이브] (0.2.51 완료) StructureType 3/4/5로 저장된다. 종류 목록(Data.StructureType)과
    /// 저장/복원 경로(SaveLoadController)가 둘 다 이 작업의 락 밖이다. 불러오기를 하면 놓아둔 제작대가
    /// 사라지므로, 저장 배선은 다음 담당에게 넘긴다(보고서의 [요청] 항목 참고).
    /// </summary>
    public class CraftStation : MonoBehaviour
    {
        // ── 이름 규약 ────────────────────────────────────────────────────────────
        //
        // 아래 문자열은 전부 **실제 에셋의 itemName 실측값**이다(Item_제작대키트.asset 등).
        // 키트 이름에는 공백이 없고(모닥불키트/물증류기키트/쉼터키트와 같은 기존 규약),
        // 화면 표시 이름에는 공백이 없는 단어 하나를 쓴다.

        /// <summary>제작대 키트 아이템의 itemName(Item_제작대키트.asset 실측값).</summary>
        public const string WorkbenchKitItemName = "제작대키트";

        /// <summary>용광로 키트 아이템의 itemName(Item_용광로키트.asset 실측값).</summary>
        public const string FurnaceKitItemName = "용광로키트";

        /// <summary>베틀 키트 아이템의 itemName(Item_베틀키트.asset 실측값).</summary>
        public const string LoomKitItemName = "베틀키트";

        /// <summary>화면에 보여줄 제작대 이름.</summary>
        public const string WorkbenchDisplayName = "제작대";

        /// <summary>화면에 보여줄 용광로 이름.</summary>
        public const string FurnaceDisplayName = "용광로";

        /// <summary>화면에 보여줄 베틀 이름.</summary>
        public const string LoomDisplayName = "베틀";

        /// <summary>
        /// 이 시설을 "쓸 수 있다"고 보는 기본 반경(미터). InteractionController.interactionDistance(4)와
        /// 같은 값이다 - 조준해서 쓰는 다른 시설과 체감 거리가 어긋나지 않게 한다.
        /// </summary>
        public const float DefaultUseRadius = 4f;

        [Tooltip("이 시설의 종류(제작대/용광로/베틀). 설치 시 키트 아이템 이름으로 정해진다.")]
        public CraftStationKind kind = CraftStationKind.Workbench;

        [Tooltip("이 시설을 쓸 수 있는 반경(미터). 0 이하이면 기본값 4m를 쓴다.")]
        public float useRadius = DefaultUseRadius;

        /// <summary>
        /// 현재 씬에 살아 있는 제작 시설 목록. Campfire.activeCampfires / WaterStill.activeStills와
        /// 완전히 같은 방식이다(정적 캐시 → 아래 ResetStaticCache에서 R1 리셋한다).
        /// </summary>
        private static readonly List<CraftStation> activeStations = new List<CraftStation>();

        /// <summary>현재 씬에 살아 있는 제작 시설 목록(읽기 전용).</summary>
        public static IReadOnlyList<CraftStation> Active => activeStations;

        /// <summary>
        /// 설치 원본(템플릿) 3종을 담아 두는 루트. DontDestroyOnLoad라 씬을 다시 로드해도 살아남는다.
        /// 월드에서 한참 아래(<see cref="TemplateParkY"/>)에 세워 두므로 플레이에 절대 닿지 않는다.
        /// </summary>
        private static GameObject templateRoot;

        /// <summary>템플릿을 세워 두는 y 좌표. 지형(y ≈ 0~30)에서 충분히 떨어진 값이면 무엇이든 좋다.</summary>
        private const float TemplateParkY = -5000f;

        /// <summary>이 시설의 시각 파츠를 이미 조립했는지(설치 원본을 복제한 사본은 다시 만들지 않는다).</summary>
        private bool visualBuilt;

        /// <summary>
        /// 이 인스턴스가 "설치 원본"인지. 원본은 활성 목록에 등록하지 않고 지면 스냅도 하지 않는다.
        /// 부모가 templateRoot인지로만 판정하므로 별도의 플래그 직렬화가 필요 없다
        /// (Instantiate로 만든 사본은 부모가 없으므로 언제나 false다).
        /// </summary>
        private bool IsPlacementTemplate =>
            templateRoot != null && transform.parent == templateRoot.transform;

        // ── 활성 목록 ────────────────────────────────────────────────────────────

        private void OnEnable()
        {
            if (IsPlacementTemplate)
                return;

            if (!activeStations.Contains(this))
                activeStations.Add(this);
        }

        private void OnDisable()
        {
            activeStations.Remove(this);
        }

        /// <summary>
        /// 설치 직후 시각 파츠를 조립하고 지면에 내려놓는다. InteractionController.PlaceFirstPlaceableItem은
        /// "플레이어 위치 + 앞 3m"에 그대로 놓기 때문에(플레이어 피벗이 지면보다 높다) 보정하지 않으면
        /// 시설이 공중에 뜬다. TerrainSampler는 섬 지형("Island_" 접두어)에만 스냅하고 못 찾으면 원래
        /// 위치를 그대로 돌려주므로, 바다 위나 지형이 아직 없는 상황에서도 안전하다.
        /// </summary>
        private void Awake()
        {
            EnsureCollider();
            BuildVisual();

            if (!IsPlacementTemplate)
                transform.position = TerrainSampler.SnapToGround(transform.position);
        }

        // ── 조회 ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// 지정한 위치의 반경 안에 해당 종류의 제작 시설이 있는지. 제작대 요구 판정의 **유일한 소스**다
        /// (CraftingSystem.HasRequiredStation이 이 값만 본다 - 같은 판정을 UI가 따로 구현하면 화면과
        /// 실제 제작 결과가 갈라진다). 상태를 바꾸지 않으므로 매 프레임 호출해도 안전하다.
        /// 거리 비교는 제곱 거리로 해 제곱근을 쓰지 않는다.
        /// </summary>
        public static bool IsNear(Vector3 worldPosition, CraftStationKind kind)
        {
            for (int i = 0; i < activeStations.Count; i++)
            {
                CraftStation station = activeStations[i];
                if (station == null || station.kind != kind)
                    continue;

                float radius = station.useRadius > 0f ? station.useRadius : DefaultUseRadius;
                if ((station.transform.position - worldPosition).sqrMagnitude <= radius * radius)
                    return true;
            }

            return false;
        }

        /// <summary>화면에 표시할 시설 이름("제작대" / "용광로" / "베틀").</summary>
        public static string GetDisplayName(CraftStationKind kind)
        {
            switch (kind)
            {
                case CraftStationKind.Furnace: return FurnaceDisplayName;
                case CraftStationKind.Loom: return LoomDisplayName;
                default: return WorkbenchDisplayName;
            }
        }

        /// <summary>
        /// 키트 아이템 이름이 어떤 시설을 세우는지 알려준다. 키트가 아니면 false.
        /// 판정은 itemName 문자열 하나뿐이다 - 이 프로젝트가 이미 쓰는 방식이며
        /// (PlayerController.SwimFinsItemName / CombatSystem.RefinedPrefix), ItemData에 새 필드를
        /// 추가하지 않으므로 기존 에셋 직렬화를 전혀 건드리지 않는다.
        /// </summary>
        public static bool TryGetKindForKitItem(string itemName, out CraftStationKind kind)
        {
            switch (itemName)
            {
                case WorkbenchKitItemName:
                    kind = CraftStationKind.Workbench;
                    return true;
                case FurnaceKitItemName:
                    kind = CraftStationKind.Furnace;
                    return true;
                case LoomKitItemName:
                    kind = CraftStationKind.Loom;
                    return true;
                default:
                    kind = CraftStationKind.Workbench;
                    return false;
            }
        }

        // ── 설치 원본(placementPrefab) 공급 ──────────────────────────────────────
        //
        // [왜 코드가 원본을 만드는가]
        // G 설치 경로(InteractionController.PlaceFirstPlaceableItem)는 isPlaceable 뿐 아니라
        // **placementPrefab != null** 인 아이템만 놓는다. 그런데 새로 만들어진 세 키트 에셋은
        // isPlaceable = 1 인데 placementPrefab = {fileID: 0}(비어 있음)이다 - 실측 확인했다.
        // 프리팹 에셋 생성(.prefab/.meta)과 ItemData 에셋 편집은 둘 다 이 작업의 락 밖이라,
        // 그대로 두면 세 키트는 영영 설치할 수 없고 그에 딸린 고급 제작법 6종이 통째로 막힌다.
        //
        // 그래서 시작 시 1회, **비어 있을 때만** 런타임 원본 오브젝트를 만들어 끼워 넣는다.
        //  · 이미 진짜 프리팹이 들어 있으면 건드리지 않는다(나중에 에셋이 채워지면 이 경로는 자동으로 꺼진다).
        //  · 에셋 파일은 바뀌지 않는다. 에셋 필드에 씬 오브젝트 참조는 직렬화될 수 없어 저장 시
        //    다시 {fileID: 0}으로 기록된다 - 즉 지금 파일 내용과 동일하다.
        //  · 원본은 DontDestroyOnLoad + 월드 한참 아래에 세워 두고 활성 목록에도 넣지 않는다.

        /// <summary>
        /// [R1 규약] 도메인 리로드를 끈 플레이 모드에서 이전 세션의 정적 상태가 새지 않게 비운다.
        /// 목록에 남은 파괴된 컴포넌트와, 이미 파괴된 템플릿 루트 참조가 그 대상이다.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticCache()
        {
            activeStations.Clear();
            templateRoot = null;
        }

        /// <summary>
        /// 세 키트 아이템의 placementPrefab이 비어 있으면 런타임 원본을 만들어 채운다.
        /// ItemDataRegistry(Resources)를 통해 아이템을 찾으므로 씬이 참조하지 않는 에셋도 잡힌다.
        /// 레지스트리가 없거나 키트가 등록돼 있지 않으면 아무 일도 하지 않는다(예전 동작 그대로).
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void EnsureKitPlacementTemplates()
        {
            var registry = ItemDataRegistry.LoadFromResources();
            if (registry == null || registry.allItems == null)
                return;

            for (int i = 0; i < registry.allItems.Count; i++)
            {
                ItemData item = registry.allItems[i];
                if (item == null || !item.isPlaceable || item.placementPrefab != null)
                    continue;

                if (!TryGetKindForKitItem(item.itemName, out CraftStationKind kind))
                    continue;

                item.placementPrefab = GetOrCreateTemplate(kind);
            }
        }

        /// <summary>
        /// 지정한 종류의 설치 원본을 만들어(또는 이미 있으면 그대로) 돌려준다.
        ///
        /// 생성 순서가 중요하다: 비활성 상태로 만들어 컴포넌트를 붙이고 kind를 채운 **다음** 활성화한다.
        /// 활성 오브젝트에 AddComponent를 하면 그 자리에서 Awake가 돌아 kind가 기본값(제작대)인 채로
        /// 외형이 조립돼 버린다. 원본이 활성이어야 하는 이유는 Instantiate의 사본이 원본의 활성 상태를
        /// 그대로 물려받기 때문이다(비활성 원본을 복제하면 사본도 비활성이라 아무 일도 하지 않는다).
        /// </summary>
        private static GameObject GetOrCreateTemplate(CraftStationKind kind)
        {
            if (templateRoot == null)
            {
                templateRoot = new GameObject("CraftStationTemplates");
                templateRoot.transform.position = new Vector3(0f, TemplateParkY, 0f);
                DontDestroyOnLoad(templateRoot);
            }

            string templateName = "CraftStationTemplate_" + kind;
            Transform existing = templateRoot.transform.Find(templateName);
            if (existing != null)
                return existing.gameObject;

            var go = new GameObject(templateName);
            go.SetActive(false);
            go.transform.SetParent(templateRoot.transform, false);

            var station = go.AddComponent<CraftStation>();
            station.kind = kind;
            station.useRadius = DefaultUseRadius;

            go.SetActive(true);
            return go;
        }

        // ── 외형 ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// 상호작용/충돌용 몸통 콜라이더를 보장한다. 시각 파츠는 StructureVisualBuilder가 콜라이더를
        /// 지운 순수 시각 오브젝트라, 이 루트 콜라이더 하나가 시설의 물리적 실체 전부다.
        /// 사본은 원본에서 콜라이더를 그대로 물려받으므로 두 번 붙지 않는다.
        /// </summary>
        private void EnsureCollider()
        {
            if (GetComponent<Collider>() != null)
                return;

            var box = gameObject.AddComponent<BoxCollider>();
            switch (kind)
            {
                case CraftStationKind.Furnace:
                    box.center = new Vector3(0f, 0.70f, 0f);
                    box.size = new Vector3(1.20f, 1.40f, 1.20f);
                    break;
                case CraftStationKind.Loom:
                    box.center = new Vector3(0f, 0.62f, 0f);
                    box.size = new Vector3(1.30f, 1.25f, 0.50f);
                    break;
                default:
                    box.center = new Vector3(0f, 0.45f, 0f);
                    box.size = new Vector3(1.40f, 0.90f, 0.90f);
                    break;
            }
        }

        /// <summary>
        /// 종류별 외형을 프리미티브로 조립한다(신규 모델/프리팹 없음 - WaterStill.BuildVisual과 같은 방식).
        /// 이미 자식 파츠가 있으면(= 설치 원본을 복제한 사본) 다시 만들지 않는다.
        ///
        /// 형태 언어는 ArtDirection 2장 규칙을 따른다: 인공물은 각진 사각 기둥 + 밧줄 결속으로 읽히게
        /// 하고(CreateLashedPost), 자연물과 실루엣이 겹치지 않도록 종류마다 다른 높이/윤곽을 준다.
        ///   · 제작대 = 낮고 넓은 상판(가로로 긴 직육면체)
        ///   · 용광로 = 굴뚝이 솟은 원통 덩어리(유일하게 둥근 실루엣 + 붉은 불구멍)
        ///   · 베틀   = 세로로 선 사각 틀(속이 빈 프레임 + 세로 실)
        /// </summary>
        private void BuildVisual()
        {
            if (visualBuilt || transform.childCount > 0)
            {
                visualBuilt = true;
                return;
            }
            visualBuilt = true;

            switch (kind)
            {
                case CraftStationKind.Furnace:
                    BuildFurnaceVisual();
                    break;
                case CraftStationKind.Loom:
                    BuildLoomVisual();
                    break;
                default:
                    BuildWorkbenchVisual();
                    break;
            }
        }

        /// <summary>제작대: 묶어 세운 다리 넷 + 두꺼운 상판 + 금속 모루 + 공구 걸이.</summary>
        private void BuildWorkbenchVisual()
        {
            Color wood = StructureVisualBuilder.Driftwood;

            StructureVisualBuilder.CreateLashedPost(transform, "LegFL", new Vector3(-0.55f, 0.35f, 0.30f), 0.70f, 0.09f, wood);
            StructureVisualBuilder.CreateLashedPost(transform, "LegFR", new Vector3(0.55f, 0.35f, 0.30f), 0.70f, 0.09f, wood);
            StructureVisualBuilder.CreateLashedPost(transform, "LegBL", new Vector3(-0.55f, 0.35f, -0.30f), 0.70f, 0.09f, wood);
            StructureVisualBuilder.CreateLashedPost(transform, "LegBR", new Vector3(0.55f, 0.35f, -0.30f), 0.70f, 0.09f, wood);

            StructureVisualBuilder.CreateVisualPart(transform, "Top", PrimitiveType.Cube,
                new Vector3(0f, 0.76f, 0f), new Vector3(1.35f, 0.12f, 0.80f), wood, null, "driftwood");

            // 모루: 이 시설이 "무기를 다시 벼리는 곳"임을 알리는 유일한 금속 파츠.
            StructureVisualBuilder.CreateVisualPart(transform, "Anvil", PrimitiveType.Cube,
                new Vector3(0.38f, 0.90f, 0.06f), new Vector3(0.34f, 0.16f, 0.24f),
                StructureVisualBuilder.SalvageMetal, null, "metal");

            // 공구 걸이(세로 막대 + 가로대) - 위로 솟은 부분이 있어야 20m 밖에서 탁자와 구분된다.
            StructureVisualBuilder.CreateVisualPart(transform, "RackPost", PrimitiveType.Cube,
                new Vector3(-0.58f, 1.02f, -0.30f), new Vector3(0.07f, 0.52f, 0.07f), wood, null, "driftwood");
            StructureVisualBuilder.CreateVisualPart(transform, "RackBar", PrimitiveType.Cube,
                new Vector3(-0.20f, 1.24f, -0.30f), new Vector3(0.80f, 0.06f, 0.06f), wood, null, "driftwood");
        }

        /// <summary>용광로: 돌을 쌓아 만든 원통 몸통 + 쇠테 + 굴뚝 + 붉게 달아오른 불구멍.</summary>
        private void BuildFurnaceVisual()
        {
            Color stone = StructureVisualBuilder.WeatheredStone;

            // 실린더 메시는 높이가 2단위라 scale.y에 "실제 높이 ÷ 2"를 넣는다(WaterStill.BuildVisual과 같은 보정).
            StructureVisualBuilder.CreateVisualPart(transform, "Base", PrimitiveType.Cylinder,
                new Vector3(0f, 0.14f, 0f), new Vector3(1.15f, 0.14f, 1.15f), stone, null, "rock");

            StructureVisualBuilder.CreateVisualPart(transform, "Body", PrimitiveType.Cylinder,
                new Vector3(0f, 0.62f, 0f), new Vector3(0.92f, 0.34f, 0.92f), stone, null, "rock");

            StructureVisualBuilder.CreateVisualPart(transform, "IronBand", PrimitiveType.Cube,
                new Vector3(0f, 0.92f, 0f), new Vector3(0.96f, 0.07f, 0.96f),
                StructureVisualBuilder.SalvageMetal, null, "metal");

            StructureVisualBuilder.CreateVisualPart(transform, "Chimney", PrimitiveType.Cylinder,
                new Vector3(0f, 1.22f, 0f), new Vector3(0.34f, 0.26f, 0.34f), stone, null, "rock");

            // 불구멍: 어두운 아궁이 안쪽 + 그 앞의 붉은 불빛. 야간에도 읽히는 유일한 신호다.
            StructureVisualBuilder.CreateVisualPart(transform, "Mouth", PrimitiveType.Cube,
                new Vector3(0f, 0.52f, 0.44f), new Vector3(0.42f, 0.34f, 0.10f),
                new Color(0.12f, 0.10f, 0.09f), null, "rock");
            StructureVisualBuilder.CreateVisualPart(transform, "Ember", PrimitiveType.Cube,
                new Vector3(0f, 0.50f, 0.49f), new Vector3(0.30f, 0.22f, 0.04f),
                new Color(0.90f, 0.42f, 0.12f), null, "noise");
        }

        /// <summary>베틀: 세로로 선 사각 틀 + 위아래 도투마리 + 세로 날실 + 짜다 만 천.</summary>
        private void BuildLoomVisual()
        {
            Color wood = StructureVisualBuilder.Driftwood;

            StructureVisualBuilder.CreateLashedPost(transform, "PostL", new Vector3(-0.58f, 0.62f, 0f), 1.24f, 0.09f, wood);
            StructureVisualBuilder.CreateLashedPost(transform, "PostR", new Vector3(0.58f, 0.62f, 0f), 1.24f, 0.09f, wood);

            StructureVisualBuilder.CreateVisualPart(transform, "BeamTop", PrimitiveType.Cube,
                new Vector3(0f, 1.18f, 0f), new Vector3(1.30f, 0.10f, 0.10f), wood, null, "driftwood");
            StructureVisualBuilder.CreateVisualPart(transform, "BeamBottom", PrimitiveType.Cube,
                new Vector3(0f, 0.30f, 0f), new Vector3(1.30f, 0.10f, 0.10f), wood, null, "driftwood");

            // 날실 3가닥. 파츠를 늘리지 않으려고 3가닥으로 줄였다 - 실루엣에서 "속이 빈 틀에 세로줄"만
            // 읽히면 충분하고, 그 이상은 5m 안에서만 보이는 디테일이다.
            Color fiber = StructureVisualBuilder.PalmFiber;
            StructureVisualBuilder.CreateVisualPart(transform, "Warp1", PrimitiveType.Cube,
                new Vector3(-0.30f, 0.74f, 0f), new Vector3(0.035f, 0.80f, 0.035f), fiber, null, "leaf");
            StructureVisualBuilder.CreateVisualPart(transform, "Warp2", PrimitiveType.Cube,
                new Vector3(0f, 0.74f, 0f), new Vector3(0.035f, 0.80f, 0.035f), fiber, null, "leaf");
            StructureVisualBuilder.CreateVisualPart(transform, "Warp3", PrimitiveType.Cube,
                new Vector3(0.30f, 0.74f, 0f), new Vector3(0.035f, 0.80f, 0.035f), fiber, null, "leaf");

            // 짜다 만 천 - 아래쪽에 몰아 두면 "짜 내려온다"는 방향이 생긴다.
            StructureVisualBuilder.CreateVisualPart(transform, "Cloth", PrimitiveType.Cube,
                new Vector3(0f, 0.48f, 0.04f), new Vector3(0.86f, 0.28f, 0.03f),
                StructureVisualBuilder.SupplyKhaki, null, "thatch");
        }
    }
}
