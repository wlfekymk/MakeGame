using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using MakeGame.Data;
using MakeGame.Player;
using MakeGame.Systems;

namespace MakeGame.UI
{
    /// <summary>
    /// 건축 모드일 때만 화면 아래에 뜨는 부품 핫바. **판정은 하지 않는다** -
    /// <see cref="BuildingSystem"/>이 정한 선택/유효성/사유를 그대로 그리고, 클릭·숫자키 입력만 넘긴다.
    ///
    /// 창 규격은 인벤토리(Tab)·제작(V)·퀘스트(J)와 같다. 다만 그 셋은 UIBuilder에 공용 팩토리가
    /// 생기기 전에 만들어져 조립 코드를 각자 갖고 있는데, 이 창은 처음부터 공용 팩토리만 쓴다
    /// (CreateWindow / CreateTitleBar / CreateCloseButton / AttachDragHandle / CreateSlotGrid /
    /// CreateItemSlot + 표준 색 상수). UIBuilder는 읽기만 하고 고치지 않았다.
    ///
    /// 씬에 인스턴스가 없다(씬 파일을 편집할 수 없다). QuestUI와 같은 RuntimeInitializeOnLoadMethod +
    /// sceneLoaded 패턴으로 씬 로드마다 스스로 생성되므로 **코드 기본값이 유일한 진실**이다.
    /// </summary>
    public class BuildMenuUI : MonoBehaviour
    {
        /// <summary>이 씬의 건축 핫바(씬 리로드마다 새 인스턴스로 교체된다).</summary>
        public static BuildMenuUI Instance { get; private set; }

        /// <summary>
        /// 핫바에 늘어놓을 부품 순서. **배치 26에서 계단이 해금됐다** - 다섯 칸 전부 실제로 지을 수 있다.
        /// </summary>
        private static readonly BuildPieceType[] SlotTypes =
        {
            BuildPieceType.Floor,
            BuildPieceType.Wall,
            BuildPieceType.Doorway,
            BuildPieceType.Window,
            BuildPieceType.Stair,
        };

        /// <summary>숫자키 선택. SlotTypes와 같은 순서다(계단 = 5).</summary>
        private static readonly KeyCode[] SelectKeys =
        {
            KeyCode.Alpha1,
            KeyCode.Alpha2,
            KeyCode.Alpha3,
            KeyCode.Alpha4,
            KeyCode.Alpha5,
        };

        // ── 치수 ────────────────────────────────────────────────────────────────
        private const float SlotSize = 88f;
        private const float SlotSpacing = 8f;
        private const float WindowPadding = 14f;
        private const float TitleBarHeight = 34f;
        private const float GridTop = TitleBarHeight + 8f;
        private const float WindowWidth = 5f * SlotSize + 4f * SlotSpacing + WindowPadding * 2f; // 500
        private const float WindowHeight = GridTop + SlotSize + 62f;                             // 192

        // ── 색 (UIBuilder 표준 상수를 그대로 쓴다 - 새로 만들지 않는다) ─────────
        private static readonly Color DimGray = new Color(0.5f, 0.5f, 0.5f, 1f);
        private static readonly Color NeutralGray = new Color(0.82f, 0.82f, 0.82f, 1f);
        private static readonly Color HintGray = new Color(1f, 1f, 1f, 0.62f);

        /// <summary>창 위치를 세션 동안 기억한다(씬 로드마다 새로 생성되므로 static이어야 한다).</summary>
        private static bool hasSavedWindowPosition;
        private static Vector2 savedWindowPosition;

        /// <summary>핫바 칸 하나가 들고 있는 화면 부품.</summary>
        private class BuildSlot
        {
            public UIBuilder.SlotVisual visual;
            public Text costLabel;
            public bool locked;
            public bool hovered;

            // 지금 이 칸이 표시 중인 내용(문자열을 다시 만들지 판단하는 캐시).
            public string shownCost;
            public bool shownAffordable = true;
        }

        private readonly List<BuildSlot> slots = new List<BuildSlot>();

        private BuildingSystem building;
        private PlayerInventory subscribedInventory;
        private bool subscribedToBuilding;

        private RectTransform canvasRect;
        private RectTransform windowRt;
        private GameObject panelRoot;
        private UIDragHandle dragHandle;
        private Text statusLabel;

        private BuildPieceType shownSelected = (BuildPieceType)(-1); // 첫 갱신을 강제하기 위한 초기값
        private BuildBlockReason shownReason = (BuildBlockReason)(-1);
        private bool shownCanPlace;
        private BuildSpace shownSpace = (BuildSpace)(-1);

        private bool IsOpen => panelRoot != null && panelRoot.activeSelf;

        /// <summary>
        /// 이 창의 드래그 손잡이(읽기 전용). MakeGame.Systems.CursorLockController가 "열린 창" 목록에서
        /// 이 핫바만 골라내기 위해 참조한다 - 이 창은 플레이어가 여는 창이 아니라 건축 모드 동안 계속
        /// 떠 있는 핫바이고, 조준은 커서가 아니라 카메라 정면 레이(BuildingSystem.ResolveTarget)이므로
        /// 이 창 때문에 커서를 풀면 건축 모드 내내 시야가 얼어붙는다. 읽기 전용이라 창 조립에는 영향이 없다.
        /// </summary>
        public UIDragHandle WindowDragHandle => dragHandle;

        /// <summary>지금 건축 핫바가 떠 있는지(읽기 전용). 내부 IsOpen을 그대로 노출만 한다.</summary>
        public bool IsWindowOpen => IsOpen;

        /// <summary>씬이 로드될 때마다 새 BuildMenuUI를 만든다(QuestUI와 같은 패턴).</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Bootstrap()
        {
            SceneManager.sceneLoaded += (scene, mode) =>
            {
                var go = new GameObject("BuildMenuUI");
                go.AddComponent<BuildMenuUI>();
            };
        }

        private void Start()
        {
            Instance = this;

            BuildUI();
            SetOpen(false);
        }

        private void OnDestroy()
        {
            if (subscribedInventory != null)
            {
                subscribedInventory.InventoryChanged -= OnInventoryChanged;
                subscribedInventory = null;
            }

            if (building != null && subscribedToBuilding)
            {
                building.Changed -= OnBuildingChanged;
                subscribedToBuilding = false;
            }

            if (Instance == this)
                Instance = null;
        }

        private void Update()
        {
            // BuildingSystem도 런타임 생성이라 어느 쪽이 먼저 도는지 보장되지 않는다
            // (AGENT_BRIEF 4장: 실행 순서 미지정). 잡힐 때까지 매 프레임 싸게 확인만 한다.
            if (building == null)
            {
                building = BuildingSystem.Instance;
                if (building != null && !subscribedToBuilding)
                {
                    building.Changed += OnBuildingChanged;
                    subscribedToBuilding = true;
                    OnBuildingChanged();
                }
            }

            if (building == null || panelRoot == null)
                return;

            if (subscribedInventory == null)
            {
                PlayerInventory inventory = building.Inventory;
                if (inventory != null)
                {
                    subscribedInventory = inventory;
                    subscribedInventory.InventoryChanged += OnInventoryChanged;
                    RefreshSlots();
                }
            }

            bool shouldBeOpen = building.IsBuildModeOn;
            if (shouldBeOpen != IsOpen)
                SetOpen(shouldBeOpen);

            if (!shouldBeOpen)
                return;

            HandleSelectKeys();

            if (shownSelected != building.SelectedType)
            {
                RefreshSlots();
                RefreshStatus();  // 상태 줄에도 부품 이름이 들어간다
            }

            // 사유는 enum 비교로만 확인하고, 실제로 바뀐 순간에만 문자열을 만든다
            // (매 프레임 문자열 조립 금지 - AGENT_BRIEF 2장).
            else if (shownReason != building.BlockReason
                || shownCanPlace != building.CanPlaceNow
                || shownSpace != building.TargetSpace)
                RefreshStatus();
        }

        private void HandleSelectKeys()
        {
            for (int i = 0; i < SelectKeys.Length && i < SlotTypes.Length; i++)
            {
                if (Input.GetKeyDown(SelectKeys[i]))
                {
                    building.SelectType(SlotTypes[i]);
                    return;
                }
            }
        }

        private void OnBuildingChanged()
        {
            if (building == null)
                return;

            bool shouldBeOpen = building.IsBuildModeOn;
            if (shouldBeOpen != IsOpen)
                SetOpen(shouldBeOpen);

            if (IsOpen)
            {
                RefreshSlots();
                RefreshStatus();
            }
        }

        private void OnInventoryChanged()
        {
            if (IsOpen)
                RefreshSlots();
        }

        /// <summary>
        /// 창을 열거나 닫는다. 인벤토리·퀘스트 창과 같은 규칙이다: 옮겨둔 자리를 복원하고, 해상도가
        /// 바뀌었을 경우를 대비해 화면 안으로 다시 맞춘 뒤 즉시 한 번 그린다.
        /// </summary>
        public void SetOpen(bool open)
        {
            if (panelRoot == null)
                return;

            panelRoot.SetActive(open);
            if (!open)
                return;

            windowRt.anchoredPosition = hasSavedWindowPosition ? savedWindowPosition : DefaultWindowPosition();
            dragHandle?.ClampNow();

            RefreshSlots();
            RefreshStatus();
        }

        /// <summary>처음 열 때의 자리: 화면 아래 가운데(핫바의 관례 위치). 좌우는 HUD가 쓴다.</summary>
        private Vector2 DefaultWindowPosition()
        {
            if (canvasRect == null)
                return Vector2.zero;

            // 창의 pivot이 (0.5, 1)이라 anchoredPosition.y는 창의 위쪽 모서리다.
            return new Vector2(0f, -canvasRect.rect.height * 0.5f + WindowHeight + 16f);
        }

        // ────────────────────────────────────────────────────────────────────────
        // 생성
        // ────────────────────────────────────────────────────────────────────────

        private void BuildUI()
        {
            // sortOrder 8: 생존 HUD(5)보다 위, 인벤토리/제작/퀘스트 창(10)보다 아래에 둔다.
            // 건축 중에도 인벤토리를 열 수 있고, 그때 핫바가 창을 덮으면 안 된다.
            var canvas = UIBuilder.CreateCanvas("BuildMenuCanvas", sortOrder: 8);
            canvasRect = canvas.GetComponent<RectTransform>();

            windowRt = UIBuilder.CreateWindow(canvas.transform, "BuildMenuWindow", WindowWidth, WindowHeight);
            panelRoot = windowRt.gameObject;

            // 제목에 단축키를 함께 적는 것은 인벤토리/퀘스트 창과 같은 규칙이다("퀘스트 (J)").
            KeyCode titleKey = BuildingSystem.Instance != null ? BuildingSystem.Instance.toggleKey : KeyCode.B;
            RectTransform titleBar = UIBuilder.CreateTitleBar(windowRt, $"건축 ({titleKey})", TitleBarHeight);
            UIBuilder.CreateCloseButton(titleBar, CloseBuildMode);

            dragHandle = UIBuilder.AttachDragHandle(titleBar, windowRt, canvasRect, TitleBarHeight);
            dragHandle.onMoved = position =>
            {
                savedWindowPosition = position;
                hasSavedWindowPosition = true;
            };

            RectTransform grid = UIBuilder.CreateSlotGrid(windowRt, "PieceGrid", SlotTypes.Length, SlotSize, SlotSpacing, GridTop);

            for (int i = 0; i < SlotTypes.Length; i++)
                slots.Add(CreateSlot(grid, i, SlotTypes[i]));

            BuildStatusLine();
            BuildHint();
        }

        /// <summary>부품 칸 하나. 공용 CreateItemSlot 위에 이름/재료 글자만 얹는다.</summary>
        private BuildSlot CreateSlot(RectTransform grid, int index, BuildPieceType type)
        {
            var slot = new BuildSlot();
            slot.locked = !BuildingSystem.IsTypeUnlocked(type);
            slot.visual = UIBuilder.CreateItemSlot(grid, $"Slot_{index}");

            UIBuilder.SlotVisual visual = slot.visual;

            // 이름: 칸 위쪽.
            visual.letterLabel.gameObject.SetActive(true);
            visual.letterLabel.text = slot.locked
                ? $"{BuildPieceCatalog.GetDisplayName(type)} (잠김)"
                : BuildPieceCatalog.GetDisplayName(type);
            visual.letterLabel.fontSize = 13;
            visual.letterLabel.alignment = TextAnchor.UpperCenter;
            visual.letterLabel.color = slot.locked ? DimGray : Color.white;
            RectTransform nameRt = visual.letterLabel.rectTransform;
            nameRt.offsetMin = new Vector2(2f, 66f);
            nameRt.offsetMax = new Vector2(-2f, -6f);

            // 아이콘: 가운데 좁은 띠. 카탈로그가 지정한 ItemData에 스프라이트가 있을 때만 켠다.
            RectTransform iconRt = visual.icon.rectTransform;
            iconRt.offsetMin = new Vector2(28f, 40f);
            iconRt.offsetMax = new Vector2(-28f, -24f);
            ApplyPieceIcon(slot, type);

            // 숫자키: 우상단(기본 자리인 우하단은 재료 글자와 겹친다).
            visual.countLabel.gameObject.SetActive(true);
            visual.countLabel.text = slot.locked ? "-" : (index + 1).ToString();
            visual.countLabel.alignment = TextAnchor.UpperRight;
            visual.countLabel.color = slot.locked ? DimGray : HintGray;
            RectTransform countRt = visual.countLabel.rectTransform;
            countRt.anchorMin = new Vector2(1f, 1f);
            countRt.anchorMax = new Vector2(1f, 1f);
            countRt.pivot = new Vector2(1f, 1f);
            countRt.anchoredPosition = new Vector2(-5f, -4f);

            // 재료: 칸 아래쪽. "이름 보유/필요" 한 줄씩.
            slot.costLabel = UIBuilder.CreateText(visual.go.transform, "Cost", "", 10, NeutralGray, TextAnchor.LowerCenter);
            slot.costLabel.raycastTarget = false;
            RectTransform costRt = slot.costLabel.rectTransform;
            costRt.anchorMin = Vector2.zero;
            costRt.anchorMax = Vector2.one;
            costRt.offsetMin = new Vector2(4f, 4f);
            costRt.offsetMax = new Vector2(-4f, -50f);

            visual.input.index = index;
            if (!slot.locked)
            {
                // 칸마다 델리게이트를 새로 만들지 않도록 메서드 그룹을 그대로 넘긴다(InventorySlotView 주석 참고).
                visual.input.onLeftClick = OnSlotLeftClick;
                visual.input.onEnter = OnSlotEnter;
                visual.input.onExit = OnSlotExit;
            }

            return slot;
        }

        /// <summary>카탈로그가 아이콘으로 쓰라고 지정한 ItemData의 스프라이트/색을 칸에 적용한다.</summary>
        private void ApplyPieceIcon(BuildSlot slot, BuildPieceType type)
        {
            string iconItemName = BuildPieceCatalog.GetIconItemName(type);
            if (string.IsNullOrEmpty(iconItemName))
                return;

            ItemData item = FindItemData(iconItemName);
            if (item == null)
                return;

            // 색 띠는 스프라이트가 없어도 계열을 알려 준다(인벤토리·제작 창과 같은 기준).
            Color stripColor = UIBuilder.GetItemCategoryColor(item);
            if (slot.locked)
                stripColor.a = 0.35f;
            slot.visual.categoryStrip.color = stripColor;

            if (item.icon == null)
                return;

            slot.visual.icon.sprite = item.icon;
            slot.visual.icon.color = slot.locked ? new Color(1f, 1f, 1f, 0.35f) : Color.white;
            slot.visual.icon.enabled = true;
        }

        /// <summary>
        /// 아이콘용 ItemData를 이름으로 찾는다. 창을 만들 때 한 번만 부르므로 캐시를 두지 않는다.
        /// 인벤토리에 들고 있는 것 → 레지스트리 → 메모리 전수 조회 순으로 본다.
        /// </summary>
        private ItemData FindItemData(string itemName)
        {
            PlayerInventory inventory = building != null ? building.Inventory : FindAnyObjectByType<PlayerInventory>();
            if (inventory != null && inventory.items != null)
            {
                for (int i = 0; i < inventory.items.Count; i++)
                {
                    InventoryItem item = inventory.items[i];
                    if (item != null && item.data != null && item.data.itemName == itemName)
                        return item.data;
                }
            }

            ItemDataRegistry registry = ItemDataRegistry.LoadFromResources();
            if (registry != null && registry.allItems != null)
            {
                for (int i = 0; i < registry.allItems.Count; i++)
                {
                    ItemData data = registry.allItems[i];
                    if (data != null && data.itemName == itemName)
                        return data;
                }
            }

            var all = Resources.FindObjectsOfTypeAll<ItemData>();
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] != null && all[i].itemName == itemName)
                    return all[i];
            }

            return null;
        }

        /// <summary>선택한 부품과 지금 설치할 수 없는 사유를 알려 주는 한 줄.</summary>
        private void BuildStatusLine()
        {
            statusLabel = UIBuilder.CreateText(windowRt, "Status", "", 13, NeutralGray, TextAnchor.MiddleCenter);
            statusLabel.raycastTarget = false;
            statusLabel.horizontalOverflow = HorizontalWrapMode.Overflow;

            // 좌우로 늘어나고 높이는 고정인 rect: sizeDelta.x는 부모 폭 대비 여백(음수), y는 높이다.
            RectTransform rt = statusLabel.rectTransform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = new Vector2(1f, 0f);
            rt.pivot = new Vector2(0.5f, 0f);
            rt.sizeDelta = new Vector2(-WindowPadding * 2f, 18f);
            rt.anchoredPosition = new Vector2(0f, 32f);
        }

        /// <summary>하단 조작 안내 한 줄(다른 창의 단축키 힌트 줄과 같은 형식).</summary>
        private void BuildHint()
        {
            KeyCode exitKey = BuildingSystem.Instance != null ? BuildingSystem.Instance.toggleKey : KeyCode.B;
            KeyCode rotate = BuildingSystem.Instance != null ? BuildingSystem.Instance.rotateKey : KeyCode.Q;

            var hint = UIBuilder.CreateText(windowRt, "Hint",
                $"[좌클릭] 설치 · [우클릭] 철거(재료 절반 반환) · [휠/{rotate}] 90도 회전 · [1~5] 부품 · [{exitKey}] 나가기",
                11, HintGray, TextAnchor.MiddleCenter);
            hint.raycastTarget = false;
            hint.horizontalOverflow = HorizontalWrapMode.Overflow;

            RectTransform rt = hint.rectTransform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = new Vector2(1f, 0f);
            rt.pivot = new Vector2(0.5f, 0f);
            rt.sizeDelta = new Vector2(-WindowPadding * 2f, 16f);
            rt.anchoredPosition = new Vector2(0f, 12f);
        }

        // ────────────────────────────────────────────────────────────────────────
        // 갱신
        // ────────────────────────────────────────────────────────────────────────

        private void RefreshSlots()
        {
            if (building == null)
                return;

            shownSelected = building.SelectedType;

            for (int i = 0; i < slots.Count && i < SlotTypes.Length; i++)
                RefreshSlot(slots[i], SlotTypes[i]);
        }

        private void RefreshSlot(BuildSlot slot, BuildPieceType type)
        {
            if (building == null || slot == null || slot.visual == null)
                return;

            bool selected = !slot.locked && type == shownSelected;
            bool affordable = slot.locked || building.HasMaterialsFor(type);

            slot.visual.outline.enabled = selected;
            slot.visual.background.color = slot.locked
                ? UIBuilder.SlotEmptyColor
                : (slot.hovered ? UIBuilder.SlotHoverColor : (selected ? UIBuilder.SlotFilledColor : UIBuilder.SlotEmptyColor));

            if (slot.locked)
            {
                if (slot.shownCost != "다음 배치")
                {
                    slot.shownCost = "다음 배치";
                    slot.costLabel.text = slot.shownCost;
                    slot.costLabel.color = DimGray;
                }
                return;
            }

            string costText = BuildCostText(type);
            if (costText != slot.shownCost || affordable != slot.shownAffordable)
            {
                slot.shownCost = costText;
                slot.shownAffordable = affordable;
                slot.costLabel.text = costText;
                slot.costLabel.color = affordable ? NeutralGray : UIBuilder.DangerRed;
            }
        }

        /// <summary>"나뭇가지 4/6" 형태로 재료 줄을 만든다. 값이 실제로 바뀐 갱신에서만 불린다.</summary>
        private string BuildCostText(BuildPieceType type)
        {
            IReadOnlyList<BuildPieceCost> cost = BuildPieceCatalog.GetCost(type);
            if (cost == null || cost.Count == 0)
                return "재료 없음";

            costBuilder.Length = 0;
            for (int i = 0; i < cost.Count; i++)
            {
                BuildPieceCost entry = cost[i];
                if (string.IsNullOrEmpty(entry.itemName) || entry.count <= 0)
                    continue;

                if (costBuilder.Length > 0)
                    costBuilder.Append('\n');

                costBuilder.Append(entry.itemName);
                costBuilder.Append(' ');
                costBuilder.Append(building.CountOwned(entry.itemName));
                costBuilder.Append('/');
                costBuilder.Append(entry.count);
            }

            return costBuilder.Length == 0 ? "재료 없음" : costBuilder.ToString();
        }

        private readonly System.Text.StringBuilder costBuilder = new System.Text.StringBuilder(64);

        private void RefreshStatus()
        {
            if (building == null || statusLabel == null)
                return;

            shownReason = building.BlockReason;
            shownCanPlace = building.CanPlaceNow;
            shownSpace = building.TargetSpace;

            // 갑판 위를 겨누고 있으면 그렇다고 알려 준다 - 같은 부품이 뗏목에 붙는지 땅에 박히는지는
            // 화면만 봐서는 구별이 어렵고, 배가 떠난 뒤에야 알게 되면 늦다.
            string where = shownSpace == BuildSpace.Deck ? "갑판 · " : "";

            // 구분자는 프로젝트의 다른 창들이 이미 쓰는 "·"를 그대로 쓴다(내장 LegacyRuntime 폰트에
            // 없을 수 있는 글자를 새로 들이지 않는다).
            statusLabel.text = shownCanPlace
                ? $"{where}{BuildPieceCatalog.GetDisplayName(building.SelectedType)} · 설치 가능"
                : $"{where}{BuildPieceCatalog.GetDisplayName(building.SelectedType)} · {BuildingSystem.DescribeBlockReason(shownReason)}";

            statusLabel.color = shownCanPlace ? UIBuilder.MedicGreen : UIBuilder.DangerRed;
        }

        // ────────────────────────────────────────────────────────────────────────
        // 입력
        // ────────────────────────────────────────────────────────────────────────

        private void OnSlotLeftClick(int index)
        {
            if (building == null || index < 0 || index >= SlotTypes.Length)
                return;

            building.SelectType(SlotTypes[index]);
            RefreshSlots();
        }

        private void OnSlotEnter(int index)
        {
            if (index < 0 || index >= slots.Count)
                return;

            slots[index].hovered = true;
            RefreshSlot(slots[index], SlotTypes[index]);
        }

        private void OnSlotExit(int index)
        {
            if (index < 0 || index >= slots.Count)
                return;

            slots[index].hovered = false;
            RefreshSlot(slots[index], SlotTypes[index]);
        }

        private void CloseBuildMode()
        {
            building?.SetBuildMode(false);
        }
    }
}
