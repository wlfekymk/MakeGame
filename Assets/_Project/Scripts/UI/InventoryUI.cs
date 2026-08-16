using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using MakeGame.Data;
using MakeGame.Player;

namespace MakeGame.UI
{
    /// <summary>
    /// 인벤토리 UI. Tab 키로 열고 닫으며, 소지품을 **정사각 슬롯 격자**로 보여준다.
    /// 씬에 미리 배치하지 않고 Start()에서 UIBuilder로 캔버스/창/격자를 직접 생성한다.
    ///
    /// 목록형 → 격자형 재작성(B19). 예전에는 칸 하나가 화면의 한 "줄"이었고, 줄마다 이름·설명·사용법·
    /// 버리기 버튼이 글자로 나열돼 있었다. 아이콘 31종이 전부 배선된 지금은 그 형식이 손해다:
    /// · 글자를 읽어야 무엇을 가졌는지 알 수 있다(아이콘이 22px 장식으로만 쓰였다).
    /// · **빈 칸이 화면에 없다.** 24/30이라는 숫자는 있었지만 "앞으로 6칸 남았다"가 눈에 보이지 않았다.
    /// · 창이 화면 왼쪽에 못 박혀 있어 옮길 수도, 마우스로 닫을 수도 없었다.
    ///
    /// 지금 구조:
    /// · 슬롯 = 스택 1개. SlotCapacity 만큼 **빈 칸까지 전부** 그린다(용량이 형태로 읽힌다).
    /// · 용량이 30 → 100칸이 되면서 격자를 **스크롤 + 칸 뷰 재사용(가상화)** 으로 바꿨다
    ///   (<see cref="VirtualSlotGrid"/> - 보관 상자 창과 같은 구현을 공유한다). 100칸을 한 화면에
    ///   전부 그리면 17줄 = 창 높이 1200px이 넘어 1080 화면 밖으로 나가고, 칸 뷰 100개(자식까지
    ///   600개가 넘는다)를 창을 열 때마다 레이아웃에 태우게 된다. 지금은 7줄(42칸)이 보이고
    ///   나머지는 스크롤이며, 실제로 만들어지는 칸 뷰는 54개가 상한이다.
    /// · 슬롯 안 = 아이콘(꽉 차게) + 우하단 개수 + (도구면) 하단 내구도 막대 + 좌측 카테고리 색 띠.
    /// · 개수 1은 숫자를 찍지 않는다 - "x1"은 정보가 없고 아이콘만 가린다.
    /// · 제목 표시줄 드래그로 창 이동(UIDragHandle), 우상단 X로 닫기, 아이콘 hover로 툴팁(ItemTooltipUI).
    /// · 카테고리 필터(F키 + 마우스 칩)·정렬·용량 표시·버리기 2단계 확인은 목록형에서 그대로 계승했다.
    /// </summary>
    public class InventoryUI : MonoBehaviour
    {
        [Tooltip("표시할 대상 인벤토리")]
        public PlayerInventory inventory;

        [Tooltip("인벤토리 창을 여닫는 키")]
        public KeyCode toggleKey = KeyCode.Tab;

        [Tooltip("인벤토리가 열려 있을 때 카테고리 필터를 순환시키는 키")]
        public KeyCode cycleFilterKey = KeyCode.F;

        [Tooltip("한 칸 전부를 버릴 때 버리기 조작과 함께 누르는 키")]
        public KeyCode dropWholeStackModifier = KeyCode.LeftShift;

        private static readonly string[] CategoryFilterNames =
        {
            "전체", "무기", "치료", "음식", "음료", "설치형", "이동수단", "재료"
        };

        /// <summary>
        /// 목록 갱신 주기(초). **매 프레임 갱신하지 않는 이유와, 그렇다고 이벤트만 쓰지 않는 이유**:
        ///
        /// · 매 프레임 GetStacks(buffer)를 부르면 List 재할당은 없지만 InventoryStack은 class라
        ///   (PlayerInventory.GetStacks에서 스택마다 new) 칸 수만큼 새 객체가 매 프레임 쌓인다.
        /// · 그렇다고 InventoryChanged 이벤트만 구독하면 **내구도 표시가 굳는다**. PlayerInventory.UseItem은
        ///   아이템이 완전히 소진된 순간에만 InventoryChanged를 발행하고, remainingUses가 20→19로 줄어드는
        ///   평범한 사용에는 이벤트가 없다. 창을 열어둔 채 채집하면 막대가 멈춰 있게 된다.
        ///
        /// 그래서 이벤트(추가/제거/버리기 - 즉시)와 저주파 폴링(내구도 - 0.2초 안에 따라잡음)을 함께 쓴다.
        /// 폴링은 창이 열려 있는 동안에만 돈다.
        /// </summary>
        private const float RefreshInterval = 0.2f;

        /// <summary>버리기를 무장한 뒤 이 시간(초) 안에 다시 누르지 않으면 확인이 취소된다.</summary>
        private const float DropConfirmWindow = 3f;

        /// <summary>주울 수 없었다는 경고를 띄워두는 시간(초).</summary>
        private const float RejectWarningDuration = 3f;

        // 격자 치수. 1920x1080 기준 폭 430 - 6열이면 창을 옮겨도 시야를 크게 가리지 않는다.
        private const int Columns = 6;
        private const float SlotSize = 62f;
        private const float SlotSpacing = 6f;
        private const float WindowPadding = 14f;
        private const float TitleBarHeight = 34f;
        private const float InfoRowHeight = 26f;
        private const float FooterButtonHeight = 28f;
        private const float HintHeight = 16f;
        private const float FilterChipWidth = 180f;
        private const float DropButtonWidth = 100f;

        /// <summary>
        /// 한 화면에 보이는 줄 수. **창 높이는 이제 용량과 무관한 고정값**이고, 남는 칸은 스크롤로 본다.
        /// 7줄 = 42칸이 한눈에 들어오고 창 높이는 618px이라 1080 화면에 여유 있게 들어간다
        /// (용량에 비례해 늘리던 예전 방식은 100칸에서 1200px을 넘어 화면 밖으로 나간다).
        /// </summary>
        private const int VisibleRows = 7;

        /// <summary>격자(뷰포트) 높이. 보이는 줄 수로만 정해진다.</summary>
        private const float GridHeight = VisibleRows * SlotSize + (VisibleRows - 1) * SlotSpacing;

        /// <summary>격자 위쪽(제목 표시줄 + 용량/필터 줄)이 차지하는 높이.</summary>
        private const float GridTopOffset = TitleBarHeight + 6f + InfoRowHeight + 10f;

        /// <summary>격자 아래쪽(선택 줄 + 버리기 버튼 + 조작 안내)이 차지하는 높이.</summary>
        private const float GridBottomOffset = 10f + FooterButtonHeight + 6f + HintHeight + 12f;

        /// <summary>창 전체 높이(76 + 470 + 72 = 618px 고정).</summary>
        private const float WindowHeight = GridTopOffset + GridHeight + GridBottomOffset;

        // 색: ArtDirection.md 팔레트 안에서만 쓴다(새 색을 만들지 않는다).
        private static readonly Color NeutralGray = new Color(0.8f, 0.8f, 0.8f, 1f);        // #CCCCCC
        private static readonly Color DimGray = new Color(0.55f, 0.55f, 0.55f, 1f);
        private static readonly Color SunstrokeGold = new Color(0.902f, 0.749f, 0.2f, 1f);  // #E6BF33
        private static readonly Color DangerRed = new Color(0.8f, 0.2f, 0.2f, 1f);          // #CC3333
        private static readonly Color MedicGreen = new Color(0.31f, 0.659f, 0.478f, 1f);    // #4FA87A

        // 슬롯 배경 4단계. 색상이 아니라 **밝기**로만 구분해 색맹 대응과 야간 가독성을 둘 다 지킨다.
        private static readonly Color SlotEmptyColor = new Color(1f, 1f, 1f, 0.04f);
        private static readonly Color SlotFilledColor = new Color(1f, 1f, 1f, 0.09f);
        private static readonly Color SlotHoverColor = new Color(1f, 1f, 1f, 0.2f);
        private static readonly Color SlotArmedColor = new Color(0.8f, 0.2f, 0.2f, 0.28f);

        /// <summary>
        /// 창 위치를 세션 동안 기억한다. static인 이유: 이 컴포넌트는 씬에 배치돼 있어 씬을 다시 로드하면
        /// 통째로 새로 생성된다. 인스턴스 필드에 두면 "새 게임/불러오기 후 창이 처음 자리로 돌아간다".
        /// 세이브까지 갈 필요는 없다는 요구라 파일에는 쓰지 않는다.
        /// </summary>
        private static bool hasSavedWindowPosition;
        private static Vector2 savedWindowPosition;

        // 필터 인덱스 0은 "전체"를 뜻하고, 1부터는 ItemCategory 값 + 1에 대응한다.
        private int currentFilterIndex = 0;
        private int lastDisplayedFilterIndex = -1;
        private int lastDisplayedUsedSlots = -1;
        private int lastDisplayedSlotCapacity = -1;

        private float refreshTimer = 0f;

        // 사용법 힌트에 쓰는 실제 키. 실제 키는 InteractionController가 정하고 씬에서 바뀔 수 있다.
        private KeyCode interactKey = KeyCode.E;
        private KeyCode cookKey = KeyCode.R;
        private KeyCode consumeKey = KeyCode.C;
        private KeyCode placeKey = KeyCode.G;

        private RectTransform canvasRect;
        private GameObject panelRoot;
        private RectTransform windowRt;
        private UIDragHandle dragHandle;
        private Text capacityLabel;
        private Text filterLabel;
        private Text selectionLabel;
        private Text hintLabel;
        private Button dropButton;
        private Text dropButtonLabel;
        private ItemTooltipUI tooltip;

        /// <summary>
        /// 스크롤 + 칸 뷰 재사용 격자. 표시 목록(필터/정렬을 거친 스택들)은 이 격자의 Buffer가 그대로
        /// 들고 있으므로, 화면의 n번째 칸 = Buffer[n]이다(클릭·툴팁·선택이 전부 이 인덱스를 쓴다).
        /// </summary>
        private readonly VirtualSlotGrid grid = new VirtualSlotGrid();

        // 칸 뷰 버퍼. PlayerInventory.GetStacks(buffer)가 내부에서 Clear하고 다시 채운다.
        private readonly List<InventoryStack> stackBuffer = new List<InventoryStack>();

        private int hoverIndex = -1;
        private int selectedIndex = -1;
        private ItemData selectedData;
        private InventoryItem selectedRepresentative;

        private ItemData pendingData;
        private InventoryItem pendingRepresentative;
        private bool pendingWhole;
        private float pendingUntil = -1f;
        private bool dropLabelArmed = false;

        private string rejectedName;
        private float rejectedUntil = -1f;

        // 하단 줄/안내줄이 마지막으로 문자열을 다시 만들었을 때의 표시 조건. 초기값을 "있을 수 없는 값"
        // (개수 -1)으로 둬서 첫 갱신은 반드시 실행되게 한다.
        private ItemData lastFooterData;
        private int lastFooterCount = -1;
        private bool lastFooterArmed;
        private bool lastFooterWhole;
        private int lastHintState = -1; // 0 = 조작 안내, 1 = 가득 참 경고
        private string lastHintRejectedName;

        // 툴팁이 마지막으로 채워진 내용. 커서가 같은 칸에 머무는 동안 0.2초마다 다시 만들지 않는다.
        private ItemData lastTooltipData;
        private int lastTooltipCount = -1;
        private int lastTooltipRemaining = int.MinValue;

        // 캡처가 없는 정적 람다는 컴파일러가 한 번만 만들어 캐시하므로 정렬마다 델리게이트가 새로 생기지 않는다.
        private static readonly Comparison<InventoryStack> StackOrder = (a, b) =>
        {
            int categoryCompare = GetCategory(a.data).CompareTo(GetCategory(b.data));
            if (categoryCompare != 0)
                return categoryCompare;

            int nameCompare = string.Compare(a.data.itemName, b.data.itemName, StringComparison.CurrentCulture);
            if (nameCompare != 0)
                return nameCompare;

            // 같은 종류의 칸이 여러 개일 때(야자잎 20/20/2) 가득 찬 칸을 앞으로 모아 순서가 흔들리지 않게 한다.
            // List.Sort는 불안정 정렬이라 동점을 남겨두면 갱신마다 칸 순서가 뒤바뀔 수 있다.
            return b.count.CompareTo(a.count);
        };

        private bool IsOpen => panelRoot != null && panelRoot.activeSelf;

        /// <summary>시작 시 인벤토리 UI 계층을 생성하고 기본적으로 닫힌 상태로 둔다.</summary>
        private void Start()
        {
            var interaction = FindAnyObjectByType<MakeGame.Systems.InteractionController>();
            if (interaction != null)
            {
                interactKey = interaction.interactKey;
                cookKey = interaction.cookKey;
                consumeKey = interaction.consumeKey;
                placeKey = interaction.placeKey;
            }

            // 씬에서 연결돼 있으면 그 값이 이긴다. null일 때만 찾아 채운다(CraftingUI와 같은 방식).
            if (inventory == null)
                inventory = FindAnyObjectByType<PlayerInventory>();

            if (inventory != null)
            {
                inventory.InventoryChanged += OnInventoryChanged;
                inventory.AddRejected += OnAddRejected;
            }

            tooltip = ItemTooltipUI.GetOrCreate();

            BuildUI();
            SetOpen(false);
        }

        /// <summary>
        /// 구독한 이벤트를 반드시 해제한다. 씬 재로드 시 죽은 UI가 이벤트에 남아 있으면
        /// 다음 인벤토리 변화에서 파괴된 오브젝트를 건드리게 된다.
        /// </summary>
        private void OnDestroy()
        {
            if (inventory != null)
            {
                inventory.InventoryChanged -= OnInventoryChanged;
                inventory.AddRejected -= OnAddRejected;
            }

            if (tooltip != null)
                tooltip.Hide();
        }

        /// <summary>인벤토리에 실제 변화(추가/제거/버리기/복원)가 생긴 순간 격자를 즉시 다시 그린다.</summary>
        private void OnInventoryChanged()
        {
            if (IsOpen)
                RefreshGrid();
        }

        /// <summary>
        /// 용량이 꽉 차 줍지 못한 순간을 창 안에 남긴다. 이 알림은 보통 창이 닫힌 채로 발생하므로
        /// (줍는 순간에 인벤토리를 열어두는 사람은 드물다) 몇 초 동안 상태로 들고 있다가, 그 사이에
        /// 창을 열면 "왜 안 주워졌는지"를 알려준다.
        /// </summary>
        private void OnAddRejected(ItemData data)
        {
            rejectedName = data != null ? data.itemName : null;
            rejectedUntil = Time.unscaledTime + RejectWarningDuration;

            if (IsOpen)
                UpdateHint();
        }

        /// <summary>매 프레임 토글 입력을 감지하고, 창이 열려 있으면 격자를 저주파로 갱신한다.</summary>
        private void Update()
        {
            if (panelRoot == null)
                return;

            if (Input.GetKeyDown(toggleKey))
                SetOpen(!panelRoot.activeSelf);

            if (!panelRoot.activeSelf)
                return;

            // 인벤토리가 열려 있을 때만 필터 순환 키를 받는다(닫혀 있을 때 실수로 바뀌는 것을 방지).
            if (Input.GetKeyDown(cycleFilterKey))
                CycleFilter();

            // Time.timeScale이 0인 화면 위에서도 멈추지 않도록 unscaled를 쓴다(프로젝트 공통 규칙).
            refreshTimer -= Time.unscaledDeltaTime;
            if (refreshTimer <= 0f)
            {
                refreshTimer = RefreshInterval;
                RefreshGrid();
            }
        }

        /// <summary>카테고리 필터를 다음 순서로 넘긴다(전체 → 무기 → … → 재료 → 전체).</summary>
        private void CycleFilter()
        {
            currentFilterIndex = (currentFilterIndex + 1) % CategoryFilterNames.Length;

            // 필터를 바꾸면 목록이 통째로 달라진다. 스크롤을 그대로 두면 "아무것도 없는 아래쪽"을
            // 보고 있게 되므로(예: 재료 40칸에서 스크롤을 내린 채 '치료' 필터로 넘어가면 빈 화면),
            // 맨 위로 되돌린다.
            grid.ResetScroll();

            // 필터가 바뀌면 화면에서 사라질 수도 있는 대상을 선택한 채로 두지 않는다.
            ClearSelection();
            RefreshGrid();
        }

        /// <summary>
        /// 카테고리별 표시 이름. 필터 이름 배열의 인덱스 0("전체") 다음부터가 ItemCategory 값 순서와
        /// 1:1로 대응하므로 그 배열을 그대로 재사용한다(이름 정의를 두 곳에 두지 않는다).
        /// 툴팁(ItemTooltipUI)도 같은 이름을 써야 해서 public이다.
        /// </summary>
        public static string GetCategoryDisplayName(UIBuilder.ItemCategory category)
        {
            int index = (int)category + 1;
            return index >= 0 && index < CategoryFilterNames.Length ? CategoryFilterNames[index] : "기타";
        }

        /// <summary>
        /// 아이템 하나의 분류 카테고리를 판정한다. 정렬 순서와 필터링에 함께 사용한다.
        /// 판정은 단일 소스인 UIBuilder.GetItemCategory에 위임하고, 이 메서드는 얇게 감싸기만 한다.
        /// </summary>
        private static UIBuilder.ItemCategory GetCategory(ItemData item)
        {
            return UIBuilder.GetItemCategory(item);
        }

        /// <summary>
        /// 이 아이템을 버릴 때 확인 절차를 요구해야 하는지 판정한다. **되돌릴 방법이 없는 손실만** 막는다.
        /// · maxUses != 1 → 도구/장비(무제한 칼·물통, 내구도 창 15·손도끼 20·라이터 5). 잃으면 다시 제작해야 한다.
        /// · isPlaceable → 키트(쉼터·모닥불·물증류기). 재료를 모아 만든 결과물이라 손실이 크다.
        /// · 한 칸 통째로 버리기 → 개수가 큰 만큼 오폭의 대가도 크다.
        /// 나머지(maxUses == 1인 재료·음식·치료제 1개)는 다시 주우면 되므로 즉시 버린다 - 모든 버리기에
        /// 확인을 붙이면 야자잎 20개 정리에 40번을 누르게 되고, 그러면 아무도 안 쓴다.
        /// </summary>
        private static bool RequiresDropConfirm(ItemData data, bool whole)
        {
            if (data == null)
                return true;

            return whole || data.maxUses != 1 || data.isPlaceable;
        }

        // ────────────────────────────────────────────────────────────────────────
        // 생성
        // ────────────────────────────────────────────────────────────────────────

        /// <summary>캔버스 · 창(제목 표시줄/닫기/용량/필터) · 격자 · 하단 조작줄을 만든다.</summary>
        private void BuildUI()
        {
            var canvas = UIBuilder.CreateCanvas("InventoryCanvas", sortOrder: 10);
            canvasRect = canvas.GetComponent<RectTransform>();

            float windowWidth = Columns * SlotSize + (Columns - 1) * SlotSpacing + WindowPadding * 2f;

            // 창은 화면 한쪽에 못 박지 않고 **한 점 앵커 + 고정 크기**로 만든다. 그래야 드래그가
            // anchoredPosition 하나만 움직이면 되고, 클램프 계산도 한 가지 좌표계로 끝난다.
            windowRt = UIBuilder.CreatePanel(
                canvas.transform, "InventoryWindow",
                anchorMin: new Vector2(0.5f, 0.5f), anchorMax: new Vector2(0.5f, 0.5f),
                offsetMin: Vector2.zero, offsetMax: Vector2.zero,
                // [B19 디렉터] 알파 0.75 → 0.93. 실기에서 뒤의 HUD 막대·글자가 격자 사이로 그대로
                // 읽혀 아이콘 판독을 방해했다. 인벤토리는 정보 밀도가 높은 창이라 배경이 비치면 안 된다
                // (ArtDirection 4.3의 0.75는 짧게 뜨는 알림·확인 패널 기준이다).
                color: new Color(0.04f, 0.05f, 0.06f, 0.93f),
                addTopBorder: true);

            windowRt.pivot = new Vector2(0.5f, 1f);
            // 창 높이는 **용량과 무관한 고정값**이다. 예전에는 칸 수에 비례해 늘렸는데(30칸=5줄), 그
            // 방식은 100칸에서 17줄 = 1200px이 넘어 화면 밖으로 나간다. 지금은 7줄만 보이고 나머지는
            // 격자 안에서 스크롤한다.
            windowRt.sizeDelta = new Vector2(windowWidth, WindowHeight);
            panelRoot = windowRt.gameObject;

            BuildTitleBar();
            BuildInfoRow(windowWidth);

            // 격자: 스크롤 + 칸 뷰 재사용은 공용 VirtualSlotGrid가 담당한다(보관 상자 창과 같은 구현).
            grid.Build(windowRt, "SlotGrid",
                Columns * SlotSize + (Columns - 1) * SlotSpacing, GridHeight,
                Columns, SlotSize, SlotSpacing, durabilityBars: true);
            grid.Root.anchoredPosition = new Vector2(WindowPadding, -GridTopOffset);
            grid.onEnter = OnSlotEnter;
            grid.onExit = OnSlotExit;
            grid.onLeftClick = OnSlotLeftClick;
            grid.onRightClick = OnSlotRightClick;
            grid.onStyle = ApplyCellStyle;
            grid.onRowsChanged = OnGridScrolled;

            BuildFooter(windowWidth);
        }

        /// <summary>제목 표시줄(드래그 손잡이 + 닫기 버튼).</summary>
        private void BuildTitleBar()
        {
            var titleBar = UIBuilder.CreatePanel(
                windowRt, "TitleBar",
                anchorMin: new Vector2(0f, 1f), anchorMax: new Vector2(1f, 1f),
                offsetMin: new Vector2(0f, -TitleBarHeight), offsetMax: Vector2.zero,
                color: new Color(1f, 1f, 1f, 0.07f));

            var title = UIBuilder.CreateText(titleBar, "Title", $"인벤토리 ({toggleKey})", 20, Color.white, TextAnchor.MiddleLeft);
            title.raycastTarget = false; // 제목 글자가 드래그 입력을 가로채지 않게(입력은 제목 표시줄이 받는다)
            title.rectTransform.anchorMin = Vector2.zero;
            title.rectTransform.anchorMax = Vector2.one;
            title.rectTransform.offsetMin = new Vector2(12f, 0f);
            title.rectTransform.offsetMax = new Vector2(-40f, 0f);

            // 닫기(X): 마우스만으로 창을 닫는 유일한 확실한 수단이라 항상 같은 자리(우상단)에 둔다.
            // Danger Red는 "되돌릴 수 없는 행동"이 아니라 창 닫기라는 관습적 의미로 쓴다 - 팔레트 안이고,
            // 이 화면에서 빨강을 쓰는 다른 요소(가득 참 경고/확인 대기 테두리)와 형태·위치가 완전히 다르다.
            var close = UIBuilder.CreateButton(titleBar, "Close", "X", () => SetOpen(false));
            var closeRt = close.GetComponent<RectTransform>();
            closeRt.anchorMin = new Vector2(1f, 1f);
            closeRt.anchorMax = new Vector2(1f, 1f);
            closeRt.pivot = new Vector2(1f, 1f);
            closeRt.sizeDelta = new Vector2(30f, 24f);
            closeRt.anchoredPosition = new Vector2(-5f, -5f);

            var closeImage = close.GetComponent<Image>();
            if (closeImage != null)
            {
                Color closeColor = DangerRed;
                closeColor.a = 0.75f;
                closeImage.color = closeColor;
            }

            // 제목 표시줄 자체가 드래그 손잡이다. 창 전체를 잡게 만들지 않은 이유: 격자 칸을 클릭·우클릭
            // 하는 조작과 드래그가 같은 영역에서 겹치면, 버리려고 우클릭하다 창이 딸려 움직인다.
            dragHandle = titleBar.gameObject.AddComponent<UIDragHandle>();
            dragHandle.target = windowRt;
            dragHandle.bounds = canvasRect;
            dragHandle.handleHeight = TitleBarHeight;
            dragHandle.onMoved = position =>
            {
                savedWindowPosition = position;
                hasSavedWindowPosition = true;
            };
        }

        /// <summary>용량 표시 + 카테고리 필터 칩.</summary>
        private void BuildInfoRow(float windowWidth)
        {
            capacityLabel = UIBuilder.CreateText(windowRt, "Capacity", "", 12, NeutralGray, TextAnchor.MiddleLeft);
            capacityLabel.raycastTarget = false;
            capacityLabel.horizontalOverflow = HorizontalWrapMode.Overflow;
            var capacityRt = capacityLabel.rectTransform;
            capacityRt.anchorMin = new Vector2(0f, 1f);
            capacityRt.anchorMax = new Vector2(0f, 1f);
            capacityRt.pivot = new Vector2(0f, 1f);
            capacityRt.sizeDelta = new Vector2(windowWidth - WindowPadding * 2f - FilterChipWidth - 8f, InfoRowHeight);
            capacityRt.anchoredPosition = new Vector2(WindowPadding, -(TitleBarHeight + 6f));

            // 필터를 F키로만 돌릴 수 있으면 마우스만 쓰는 사람에게는 없는 기능이나 마찬가지다.
            // 칩을 누르면 같은 순환이 돌고, 라벨에 키를 함께 적어 키 조작도 계속 노출한다.
            var filterButton = UIBuilder.CreateButton(windowRt, "FilterChip", "", CycleFilter);
            var filterRt = filterButton.GetComponent<RectTransform>();
            filterRt.anchorMin = new Vector2(1f, 1f);
            filterRt.anchorMax = new Vector2(1f, 1f);
            filterRt.pivot = new Vector2(1f, 1f);
            filterRt.sizeDelta = new Vector2(FilterChipWidth, InfoRowHeight);
            filterRt.anchoredPosition = new Vector2(-WindowPadding, -(TitleBarHeight + 6f));

            filterLabel = filterButton.GetComponentInChildren<Text>();
            if (filterLabel != null)
                filterLabel.horizontalOverflow = HorizontalWrapMode.Overflow;
        }

        /// <summary>선택 표시 + 버리기 버튼 + 조작 안내.</summary>
        private void BuildFooter(float windowWidth)
        {
            selectionLabel = UIBuilder.CreateText(windowRt, "Selection", "", 12, NeutralGray, TextAnchor.MiddleLeft);
            selectionLabel.raycastTarget = false;
            selectionLabel.horizontalOverflow = HorizontalWrapMode.Overflow;
            var selectionRt = selectionLabel.rectTransform;
            selectionRt.anchorMin = Vector2.zero;
            selectionRt.anchorMax = Vector2.zero;
            selectionRt.pivot = Vector2.zero;
            selectionRt.sizeDelta = new Vector2(windowWidth - WindowPadding * 2f - DropButtonWidth - 8f, FooterButtonHeight);
            selectionRt.anchoredPosition = new Vector2(WindowPadding, HintHeight + 12f + 6f);

            dropButton = UIBuilder.CreateButton(windowRt, "Drop", "버리기", OnDropButtonClicked);
            var dropRt = dropButton.GetComponent<RectTransform>();
            dropRt.anchorMin = new Vector2(1f, 0f);
            dropRt.anchorMax = new Vector2(1f, 0f);
            dropRt.pivot = new Vector2(1f, 0f);
            dropRt.sizeDelta = new Vector2(DropButtonWidth, FooterButtonHeight);
            dropRt.anchoredPosition = new Vector2(-WindowPadding, HintHeight + 12f + 6f);
            dropButtonLabel = dropButton.GetComponentInChildren<Text>();

            hintLabel = UIBuilder.CreateText(windowRt, "Hint", "", 11, DimGray, TextAnchor.MiddleLeft);
            hintLabel.raycastTarget = false;
            hintLabel.horizontalOverflow = HorizontalWrapMode.Overflow;
            var hintRt = hintLabel.rectTransform;
            hintRt.anchorMin = Vector2.zero;
            hintRt.anchorMax = Vector2.zero;
            hintRt.pivot = Vector2.zero;
            hintRt.sizeDelta = new Vector2(windowWidth - WindowPadding * 2f, HintHeight);
            hintRt.anchoredPosition = new Vector2(WindowPadding, 12f);
        }

        /// <summary>
        /// 스크롤로 보이는 줄이 갈리면 hover 강조와 툴팁을 정리한다. 커서는 그대로인데 칸이 미끄러져
        /// 나갔으므로, 안 그러면 엉뚱한 칸이 밝게 남고 툴팁이 옛 물건을 계속 설명한다.
        /// **선택은 건드리지 않는다** - 선택은 칸 위치가 아니라 물건(대표 인스턴스)에 걸려 있어서,
        /// 화면 밖으로 스크롤해도 살아 있어야 버리기 버튼이 그대로 동작한다.
        /// </summary>
        private void OnGridScrolled()
        {
            hoverIndex = -1;
            HideTooltip();
        }

        /// <summary>
        /// 칸 하나의 상태색과 선택 테두리를 칠한다(빈칸/채움/hover/선택/확인 대기).
        /// 격자가 칸 내용을 새로 그린 뒤와 RefreshSlotStates에서 칸마다 불린다 - **칸의 내용을 그리는
        /// 일은 공용 VirtualSlotGrid가 하고, 이 창에만 있는 상태(선택·버리기 확인 대기)만 여기서 얹는다.**
        /// </summary>
        private void ApplyCellStyle(VirtualSlotGrid.Cell cell)
        {
            bool selected = cell.index >= 0 && cell.index == selectedIndex;
            bool armed = selected && IsDropArmed();

            if (armed)
                cell.visual.background.color = SlotArmedColor;
            else if (cell.index >= 0 && cell.index == hoverIndex)
                cell.visual.background.color = SlotHoverColor;
            else
                cell.visual.background.color = cell.data != null ? SlotFilledColor : SlotEmptyColor;

            cell.visual.outline.enabled = selected;
            if (selected)
                cell.visual.outline.effectColor = armed ? DangerRed : MedicGreen;
        }

        // ────────────────────────────────────────────────────────────────────────
        // 열기 / 닫기
        // ────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// 창을 열거나 닫는다. 여는 순간 곧바로 한 번 그려서 첫 프레임에 빈 격자가 보이지 않게 한다.
        ///
        /// **창 밖 클릭으로 닫지 않는다(판단).** 이 프로젝트는 커서를 잠그지 않으므로(Cursor.lockState를
        /// 건드리는 코드가 한 곳도 없다 - MinimapUI.cs:561) 창 밖 클릭은 곧 월드 조작·공격 클릭이고,
        /// 제작 창(V)·섬 목록(M)과 나란히 띄워 쓰는 흐름에서 다른 창을 만지는 순간 인벤토리가 닫힌다.
        /// 닫는 수단은 이미 두 개(X 버튼 · Tab)가 있고 둘 다 화면에 적혀 있으므로 모호한 세 번째를
        /// 추가하지 않는다.
        /// </summary>
        public void SetOpen(bool open)
        {
            if (panelRoot == null)
                return;

            panelRoot.SetActive(open);

            if (!open)
            {
                // 닫는 순간 무장된 확인·hover·툴팁을 전부 정리한다. 다음에 열었을 때 남아 있던
                // "확실?"이 그대로 눌리는 상황을 만들지 않는다.
                ClearPendingDrop();
                hoverIndex = -1;
                HideTooltip();
                return;
            }

            // 옮겨둔 자리를 그대로 복원하고, 해상도가 바뀌었을 경우를 대비해 화면 안으로 다시 맞춘다.
            if (hasSavedWindowPosition)
                windowRt.anchoredPosition = savedWindowPosition;
            else
                windowRt.anchoredPosition = DefaultWindowPosition();

            if (dragHandle != null)
                dragHandle.ClampNow();

            // 다시 열 때는 항상 첫 줄부터 보여준다(닫기 전에 내려둔 스크롤이 남아 있으면,
            // 그 사이 물건이 줄어든 경우 빈 칸만 있는 화면으로 열린다).
            grid.ResetScroll();

            refreshTimer = RefreshInterval;
            RefreshGrid();
        }

        /// <summary>처음 열 때의 기본 자리: 화면 왼쪽 위(제작 창이 오른쪽에 있으므로 겹치지 않는다).</summary>
        private Vector2 DefaultWindowPosition()
        {
            if (canvasRect == null)
                return Vector2.zero;

            float halfCanvasWidth = canvasRect.rect.width * 0.5f;
            float halfCanvasHeight = canvasRect.rect.height * 0.5f;

            // [B19 디렉터] 좌상단은 **생존 HUD가 이미 쓰고 있다**(SurvivalHudUI 패널이 (20,-20)에서
            // 296px 높이로 내려온다). 실기에서 인벤토리가 그 위에 정확히 겹쳐 둘 다 읽기 어려웠다.
            // HUD 아래로 내리고 살짝 오른쪽으로 민다. 사용자가 드래그로 옮기면 그 위치가 이긴다.
            // [B24] 316 → 284. HUD에서 배/경비행기 두 줄을 퀘스트 창으로 옮기면서 패널 높이가
            // 296 → 264로 줄었다. 값을 안 고치면 겹치지는 않지만 여백만 52px 벌어진다.
            const float hudBottomMargin = 284f;   // HUD 패널 높이 264 + 여백 20
            return new Vector2(-halfCanvasWidth + 24f + windowRt.rect.width * 0.5f,
                halfCanvasHeight - hudBottomMargin);
        }

        // ────────────────────────────────────────────────────────────────────────
        // 갱신
        // ────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// 인벤토리를 **칸 단위**로 격자에 그린다. 갱신마다 오브젝트를 새로 만들지 않는다.
        /// 100칸이어도 실제로 다시 묶이는 칸 뷰는 화면에 보이는 54개뿐이다(VirtualSlotGrid).
        /// </summary>
        private void RefreshGrid()
        {
            if (inventory == null || grid.Root == null)
                return;

            inventory.GetStacks(stackBuffer);

            // 필터 인덱스 0은 "전체"이고, 1 이상이면 해당 카테고리(인덱스-1)만 통과시킨다.
            bool filterActive = currentFilterIndex > 0;
            UIBuilder.ItemCategory activeCategory = filterActive ? (UIBuilder.ItemCategory)(currentFilterIndex - 1) : default;

            // 표시 목록은 격자의 버퍼를 그대로 쓴다(리스트를 두 벌 들고 복사하지 않는다).
            // 화면의 n번째 칸 = display[n] 이라는 관계는 예전과 완전히 같다.
            List<InventoryStack> display = grid.Buffer;
            display.Clear();
            for (int i = 0; i < stackBuffer.Count; i++)
            {
                var stack = stackBuffer[i];
                if (stack.data == null)
                    continue;

                if (filterActive && GetCategory(stack.data) != activeCategory)
                    continue;

                display.Add(stack);
            }

            // 카테고리별로 묶이도록 정렬하고, 같은 카테고리 안에서는 이름순으로 정렬해 찾기 쉽게 한다.
            display.Sort(StackOrder);

            grid.SetCapacity(inventory.SlotCapacity);
            ResolveSelection();
            grid.Rebind(true);

            UpdateCapacity();
            UpdateFilterLabel();
            UpdateFooter();
            UpdateHint();
            UpdateHoveredTooltip();
        }

        /// <summary>
        /// 선택 대상을 다시 찾는다. 격자는 갱신마다 정렬되므로 "몇 번째 칸"으로 선택을 들고 있으면
        /// 물건이 늘거나 줄었을 때 엉뚱한 칸이 선택된 것처럼 보이고, 그 상태로 버리기를 누르면 그게
        /// 바로 오폭이다. 그래서 인스턴스(도구) → 종류(재료) 순으로 다시 찾는다.
        /// </summary>
        private void ResolveSelection()
        {
            if (selectedData == null && selectedRepresentative == null)
            {
                selectedIndex = -1;
                return;
            }

            selectedIndex = -1;

            if (selectedRepresentative != null)
            {
                for (int i = 0; i < grid.Buffer.Count && i < grid.Capacity; i++)
                {
                    if (grid.Buffer[i].representative == selectedRepresentative)
                    {
                        selectedIndex = i;
                        break;
                    }
                }
            }

            // 대표 인스턴스가 사라졌어도(스택에서 1개를 버리면 목록 끝의 인스턴스가 지워진다) 같은 종류가
            // 남아 있으면 선택을 유지한다 - 재료를 한 개씩 버릴 때마다 선택이 풀리면 쓸 수 없다.
            if (selectedIndex < 0 && selectedData != null)
            {
                for (int i = 0; i < grid.Buffer.Count && i < grid.Capacity; i++)
                {
                    if (grid.Buffer[i].data == selectedData)
                    {
                        selectedIndex = i;
                        selectedRepresentative = grid.Buffer[i].representative;
                        break;
                    }
                }
            }

            if (selectedIndex < 0)
                ClearSelection();
        }

        /// <summary>
        /// 사용 칸/전체 칸. **가득 차기 전에** 읽혀야 의미가 있으므로 상시 표시하고, 80% 이상이면
        /// Sunstroke Gold, 꽉 차면 Danger Red로 바꾼다. 필터를 걸어도 이 값은 인벤토리 전체 기준이다.
        /// </summary>
        private void UpdateCapacity()
        {
            if (capacityLabel == null || inventory == null)
                return;

            int used = inventory.UsedSlots;
            int capacity = inventory.SlotCapacity;
            if (used == lastDisplayedUsedSlots && capacity == lastDisplayedSlotCapacity)
                return;

            capacityLabel.text = $"칸 {used}/{capacity}" + (used >= capacity ? "  (가득 참)" : "");
            capacityLabel.color = used >= capacity ? DangerRed
                : (capacity > 0 && (float)used / capacity >= 0.8f ? SunstrokeGold : NeutralGray);

            lastDisplayedUsedSlots = used;
            lastDisplayedSlotCapacity = capacity;
        }

        /// <summary>필터 칩 라벨. 실제로 바뀐 갱신에서만 문자열을 새로 만든다.</summary>
        private void UpdateFilterLabel()
        {
            if (filterLabel == null || currentFilterIndex == lastDisplayedFilterIndex)
                return;

            filterLabel.text = $"분류: {CategoryFilterNames[currentFilterIndex]} [{cycleFilterKey}]";
            lastDisplayedFilterIndex = currentFilterIndex;
        }

        /// <summary>
        /// 선택 줄 + 버리기 버튼 상태(평상시 / 확인 대기)를 맞춘다. 0.2초마다 도는 폴링에서 매번
        /// 문자열을 새로 만들지 않도록, 실제로 표시가 달라지는 조합(대상/개수/무장 여부)일 때만 다시 쓴다.
        /// </summary>
        private void UpdateFooter()
        {
            bool armed = IsDropArmed();
            if (!armed && pendingUntil > 0f)
                ClearPendingDrop();

            // 선택된 칸의 내용은 **표시 목록**에서 읽는다. 칸 뷰는 스크롤에 따라 재사용되므로
            // "몇 번째 칸 뷰"가 아니라 "몇 번째 데이터"가 선택의 근거다(선택한 칸이 화면 밖으로
            // 스크롤돼도 선택과 버리기 버튼이 그대로 살아 있어야 한다).
            InventoryStack selected = grid.GetStack(selectedIndex);
            var data = selected != null ? selected.data : null;
            int count = selected != null ? selected.count : 0;

            bool unchanged = data == lastFooterData
                && count == lastFooterCount
                && armed == lastFooterArmed
                && (!armed || pendingWhole == lastFooterWhole);

            lastFooterData = data;
            lastFooterCount = count;
            lastFooterArmed = armed;
            lastFooterWhole = pendingWhole;

            if (unchanged)
                return;

            if (selectionLabel != null)
            {
                if (data == null)
                {
                    selectionLabel.text = "칸을 클릭해 선택";
                    selectionLabel.color = DimGray;
                }
                else if (armed)
                {
                    selectionLabel.text = pendingWhole
                        ? $"{data.itemName} {count}개를 전부 버린다 - 한 번 더 누르면 되돌릴 수 없다"
                        : $"{data.itemName}을(를) 버린다 - 한 번 더 누르면 되돌릴 수 없다";
                    selectionLabel.color = SunstrokeGold;
                }
                else
                {
                    selectionLabel.text = $"선택: {data.itemName}" + (count > 1 ? $" x{count}" : "");
                    selectionLabel.color = NeutralGray;
                }
            }

            if (dropButton != null)
            {
                dropButton.interactable = data != null;

                if (armed != dropLabelArmed)
                {
                    dropLabelArmed = armed;
                    if (dropButtonLabel != null)
                    {
                        dropButtonLabel.text = armed ? "확실?" : "버리기";
                        dropButtonLabel.color = armed ? SunstrokeGold : Color.white;
                    }

                    var image = dropButton.GetComponent<Image>();
                    if (image != null)
                        image.color = armed ? DangerRed : new Color(0.25f, 0.55f, 0.3f, 1f);
                }
            }
        }

        /// <summary>하단 조작 안내. 최근에 "가득 차서 못 주웠다"가 있었으면 그 경고가 먼저다.</summary>
        private void UpdateHint()
        {
            if (hintLabel == null)
                return;

            bool warning = rejectedUntil > Time.unscaledTime;
            int state = warning ? 1 : 0;
            if (state == lastHintState && (!warning || rejectedName == lastHintRejectedName))
                return;

            lastHintState = state;
            lastHintRejectedName = rejectedName;

            if (warning)
            {
                hintLabel.text = string.IsNullOrEmpty(rejectedName)
                    ? "칸이 가득 차 줍지 못했다 - 무언가를 버려야 한다"
                    : $"칸이 가득 차 {rejectedName}을(를) 줍지 못했다";
                hintLabel.color = DangerRed;
                return;
            }

            // 칸이 한 화면을 넘으면(100칸 = 17줄) 스크롤이 있다는 사실을 적어 준다 - 스크롤 막대가
            // 따로 없는 격자라 안내가 없으면 아래쪽 칸의 존재를 모른 채로 "가득 찼다"고 오해한다.
            hintLabel.text = grid.IsScrollable
                ? $"휠로 스크롤 · 클릭 선택 · 우클릭 버리기({dropWholeStackModifier}=한 칸 전부) · 제목 표시줄을 끌어 창 이동"
                : $"제목 표시줄을 끌어 창 이동 · 클릭 선택 · 우클릭 버리기({dropWholeStackModifier}=한 칸 전부)";
            hintLabel.color = DimGray;
        }

        // ────────────────────────────────────────────────────────────────────────
        // 입력
        // ────────────────────────────────────────────────────────────────────────

        private void OnSlotEnter(int index)
        {
            hoverIndex = index;
            RefreshSlotStates();
            ShowTooltipFor(index);
        }

        private void OnSlotExit(int index)
        {
            if (hoverIndex != index)
                return;

            hoverIndex = -1;
            RefreshSlotStates();
            HideTooltip();
        }

        private void OnSlotLeftClick(int index)
        {
            InventoryStack stack = grid.GetStack(index);
            if (stack == null || stack.data == null)
            {
                ClearSelection();
            }
            else if (selectedIndex == index)
            {
                // 같은 칸을 다시 누르면 선택 해제. 선택을 못 푸는 UI는 버리기 버튼이 계속 무장돼 있는
                // 것처럼 보여 불안하다.
                ClearSelection();
            }
            else
            {
                selectedIndex = index;
                selectedData = stack.data;
                selectedRepresentative = stack.representative;
                ClearPendingDrop();
            }

            RefreshSlotStates();
            UpdateFooter();
        }

        /// <summary>
        /// 우클릭 = 그 칸 버리기. 격자에는 줄마다 버튼을 놓을 자리가 없어서 조작을 옮겼고, 대신
        /// (1) 하단 안내줄, (2) 툴팁 마지막 줄, (3) 선택 후 하단 버리기 버튼 세 경로로 노출한다 -
        /// 우클릭 하나만 남기면 마우스로 발견할 방법이 없다.
        /// </summary>
        private void OnSlotRightClick(int index)
        {
            InventoryStack stack = grid.GetStack(index);
            if (stack == null || stack.data == null)
                return;

            if (selectedIndex != index)
            {
                selectedIndex = index;
                selectedData = stack.data;
                selectedRepresentative = stack.representative;
                ClearPendingDrop();
                RefreshSlotStates();
            }

            RequestDrop();
        }

        private void OnDropButtonClicked()
        {
            RequestDrop();
        }

        /// <summary>
        /// 버리기 요청. Shift를 함께 누르면 그 칸 전부, 아니면 1개다. 되돌릴 수 없는 손실(도구/키트/
        /// 한 칸 전부)은 곧바로 버리지 않고 한 번 더 누르게 한다 - 이 게임에는 버린 물건을 되찾을 수단이 없다.
        /// </summary>
        private void RequestDrop()
        {
            if (inventory == null || selectedIndex < 0)
                return;

            InventoryStack stack = grid.GetStack(selectedIndex);
            var data = stack != null ? stack.data : null;
            if (data == null)
                return;

            bool whole = Input.GetKey(dropWholeStackModifier)
                || (dropWholeStackModifier == KeyCode.LeftShift && Input.GetKey(KeyCode.RightShift));

            if (RequiresDropConfirm(data, whole))
            {
                bool armedForThis = IsDropArmed()
                    && pendingData == data
                    && pendingRepresentative == stack.representative
                    && pendingWhole == whole;

                if (!armedForThis)
                {
                    pendingData = data;
                    pendingRepresentative = stack.representative;
                    pendingWhole = whole;
                    pendingUntil = Time.unscaledTime + DropConfirmWindow;
                    RefreshSlotStates();
                    UpdateFooter();
                    return;
                }
            }

            ClearPendingDrop();
            ExecuteDrop(stack, data, whole);
        }

        /// <summary>
        /// 실제로 버린다. 인벤토리 쪽 공개 경로만 쓴다:
        /// · 겹쳐지지 않는 도구 → RemoveItem(대표 인스턴스). RemoveItems(data, 1)를 쓰면 내구도가 다른
        ///   동일 종류 중 목록 끝의 것이 지워져, 화면에서 고른 것과 실제로 사라지는 것이 어긋난다.
        /// · 그 외(겹쳐지는 재료·음식·무제한 도구) → RemoveItems. 같은 칸 안의 개체는 서로 완전히 동일하다.
        /// </summary>
        private void ExecuteDrop(InventoryStack stack, ItemData data, bool whole)
        {
            bool removed;

            if (!data.IsStackable)
            {
                removed = stack.representative != null && inventory.RemoveItem(stack.representative);
            }
            else if (!whole)
            {
                // [B19 디렉터] 1개 버리기도 **대표 인스턴스**를 지운다. RemoveItems(data, 1)은 목록 끝을
                // 지우므로, 같은 재료가 여러 칸(야자잎 20/20/2)일 때 20칸을 골라도 2칸이 줄어든다.
                // 개체가 서로 동일해 최종 상태는 같지만, "고른 칸이 줄어드는" 것이 눈에 보이는 계약이다.
                removed = stack.representative != null
                    ? inventory.RemoveItem(stack.representative)
                    : inventory.RemoveItems(data, 1);
            }
            else
            {
                // 한 칸 전부는 개수 단위가 맞다 - 어느 인스턴스가 지워지든 그 칸이 통째로 비워진다.
                removed = inventory.RemoveItems(data, Mathf.Max(1, stack.count));
            }

            if (!removed)
            {
                // 이미 사라진 대상을 눌렀을 뿐이므로 격자만 다시 그린다(소리도 내지 않는다).
                RefreshGrid();
                return;
            }

            // ArtDirection.md 4.2 A단계(일상): 화면 이펙트 없이 짧은 효과음 하나만.
            MakeGame.Systems.AudioManager.Instance?.PlayPickup();
            RefreshGrid();
        }

        private bool IsDropArmed()
        {
            return pendingUntil > 0f && Time.unscaledTime <= pendingUntil;
        }

        private void ClearPendingDrop()
        {
            pendingData = null;
            pendingRepresentative = null;
            pendingWhole = false;
            pendingUntil = -1f;
        }

        private void ClearSelection()
        {
            selectedIndex = -1;
            selectedData = null;
            selectedRepresentative = null;
            ClearPendingDrop();
        }

        /// <summary>보이는 칸들의 상태색만 다시 칠한다(내용은 건드리지 않는다).</summary>
        private void RefreshSlotStates()
        {
            grid.RefreshStyles();
        }

        // ────────────────────────────────────────────────────────────────────────
        // 툴팁
        // ────────────────────────────────────────────────────────────────────────

        /// <summary>커서가 얹혀 있는 칸의 내용이 바뀌었을 수 있으므로(내구도 감소·소모) 다시 채운다.</summary>
        private void UpdateHoveredTooltip()
        {
            if (hoverIndex >= 0)
                ShowTooltipFor(hoverIndex);
        }

        private void ShowTooltipFor(int index)
        {
            if (tooltip == null)
                return;

            InventoryStack stack = grid.GetStack(index);
            if (stack == null || stack.data == null)
            {
                HideTooltip();
                return;
            }

            int remaining = stack.RemainingUses;

            // 커서가 같은 칸에 머무는 동안(0.2초 폴링) 같은 내용을 다시 만들지 않는다. 위치 추적은
            // 툴팁 쪽 LateUpdate가 알아서 하므로 내용이 그대로면 아무것도 할 일이 없다.
            if (stack.data == lastTooltipData && stack.count == lastTooltipCount && remaining == lastTooltipRemaining)
                return;

            lastTooltipData = stack.data;
            lastTooltipCount = stack.count;
            lastTooltipRemaining = remaining;

            tooltip.Show(stack.data, stack.count, remaining, GetUsageHint(stack.data), GetDropHint(stack.data));
        }

        /// <summary>툴팁을 숨기고 "마지막으로 보여준 내용" 캐시를 비운다(같은 칸에 다시 들어와도 다시 뜨게).</summary>
        private void HideTooltip()
        {
            lastTooltipData = null;
            lastTooltipCount = -1;
            lastTooltipRemaining = int.MinValue;

            if (tooltip != null)
                tooltip.Hide();
        }

        /// <summary>
        /// 이 아이템을 지금 어떤 키로 쓸 수 있는지 짧은 힌트를 만든다. 키 자체는 InteractionController가
        /// 정하는 값이라(C=섭취/R=조리/G=설치) Start()에서 읽어둔 실제 키를 쓴다.
        /// </summary>
        private string GetUsageHint(ItemData data)
        {
            if (data == null)
                return "";

            if (data.isRawFood && data.cookedResult != null)
                return $"[{cookKey}] 굽기";

            if (data.isPlaceable && data.placementPrefab != null)
                return $"[{placeKey}] 설치";

            if (data.curesBleeding || data.curesPoison || data.curesBrokenBone)
                return $"[{consumeKey}] 치료";

            if (data.IsConsumable)
                return $"[{consumeKey}] 섭취";

            if (data.isWeapon)
                return $"[{interactKey}] 공격";

            return "";
        }

        /// <summary>툴팁 맨 아래 줄. 되돌릴 수 없는 물건이면 확인 절차가 있다는 사실까지 알려준다.</summary>
        private string GetDropHint(ItemData data)
        {
            if (data != null && RequiresDropConfirm(data, false))
                return "우클릭 버리기 (되돌릴 수 없어 한 번 더 확인한다)";

            return $"우클릭 = 1개 버리기 · {dropWholeStackModifier}+우클릭 = 한 칸 전부";
        }
    }
}
