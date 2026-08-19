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
    /// · 헤더 드래그로 창 이동(UIDragHandle), 우상단 X로 닫기.
    /// · 창은 **헤더 / 구분선 / 좌우 2단 본문**이다(왼쪽 = 필터 + 격자, 오른쪽 = 아이템 상세).
    ///   떠다니는 툴팁을 쓰지 않는 이유는 RefreshDetail 주석에 적어 뒀다.
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

        // 격자 치수는 목록형에서 그대로 이어받았다(6열 62px). 이 값을 건드리면 저장된 창 위치와
        // 스크롤 감각이 한꺼번에 달라지므로, 창 구조를 바꾸는 이번 작업에서는 손대지 않았다.
        private const int Columns = 6;
        private const float SlotSize = 62f;
        private const float SlotSpacing = 6f;
        private const float WindowPadding = 14f;
        private const float InfoRowHeight = 26f;
        private const float FooterButtonHeight = 28f;
        private const float HintHeight = 16f;
        private const float FilterChipWidth = 180f;

        /// <summary>상세 패널 안쪽 여백. 패널 폭이 232px이라 이보다 키우면 아이템 이름이 세 줄로 깨진다.</summary>
        private const float DetailPadding = 12f;

        /// <summary>버리기 버튼은 상세 패널 폭을 꽉 채운다 - 이 창에서 유일하게 되돌릴 수 없는 조작이라 숨기지 않는다.</summary>
        private const float DropButtonWidth = UITheme.DetailPaneWidth - DetailPadding * 2f;

        /// <summary>
        /// 한 화면에 보이는 줄 수. 창 높이는 용량과 무관한 고정값이고, 남는 칸은 스크롤로 본다
        /// (용량에 비례해 늘리던 예전 방식은 100칸에서 1200px을 넘어 화면 밖으로 나간다).
        /// </summary>
        private const int VisibleRows = 7;

        private const float GridWidth = Columns * SlotSize + (Columns - 1) * SlotSpacing;
        private const float GridHeight = VisibleRows * SlotSize + (VisibleRows - 1) * SlotSpacing;

        /// <summary>
        /// 본문이 시작되는 y(헤더 + 구분선 + 본문 여백). 왼쪽 격자 단과 오른쪽 상세 패널이 **같은 선**에서
        /// 출발해야 두 단이 하나의 본문으로 읽힌다 - 이번 재구성의 핵심이라 상수로 못박는다.
        /// </summary>
        private const float BodyTop = UITheme.HeaderHeight + UITheme.SeparatorThickness + UITheme.BodyPadding;

        /// <summary>격자 위쪽(헤더 + 필터 줄)이 차지하는 높이.</summary>
        private const float GridTopOffset = BodyTop + InfoRowHeight + 8f;

        /// <summary>창 높이(44 + 1 + 12 + 26 + 8 + 470 + 14 = 575px 고정).</summary>
        private const float WindowHeight = GridTopOffset + GridHeight + WindowPadding;

        /// <summary>창 폭(14 + 402 + 12 + 232 + 14 = 674px). 격자와 상세가 한 창 안에 있어야 시선이 창 밖으로 나가지 않는다.</summary>
        private const float WindowWidth = WindowPadding + GridWidth + UITheme.PaneGap + UITheme.DetailPaneWidth + WindowPadding;

        /// <summary>상세 패널의 왼쪽 x와 높이(본문 시작선부터 창 아래 여백까지).</summary>
        private const float DetailPaneX = WindowPadding + GridWidth + UITheme.PaneGap;
        private const float DetailPaneHeight = WindowHeight - BodyTop - WindowPadding;

        // 색: ArtDirection.md 팔레트 안에서만 쓴다(새 색을 만들지 않는다).
        // 본문 글자색(#CCCCCC)과 보조 글자색은 UITheme.TextPrimary / TextDim으로 옮겼다 - 같은 값이
        // 파일마다 복사돼 있으면 창 스킨을 한 번에 손볼 수가 없다.
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
        private Text hintLabel;
        private Button dropButton;
        private Text dropButtonLabel;
        private ItemTooltipUI tooltip;

        // 오른쪽 상세 패널. 떠다니는 툴팁이 하던 일을 고정된 자리에서 대신한다.
        private RectTransform detailPane;
        private Image detailIcon;
        private Text detailName;
        private Text detailCategory;
        private RectTransform detailSeparator;
        private RectTransform detailBlock;
        private Text detailDescription;
        private Text detailStats;
        private Text detailUsage;
        private Text detailEmptyLabel;
        private Text confirmLabel;

        /// <summary>상세 패널이 지금 "빈 상태"인가. 내용 캐시만으로는 빈 상태를 구분할 수 없어 따로 든다.</summary>
        private bool detailEmpty;

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

        // 상세 패널이 마지막으로 채운 내용. 커서/선택이 같은 칸에 머무는 동안 0.2초마다 문자열을
        // 다시 만들지 않는다.
        private ItemData lastDetailData;
        private int lastDetailCount = -1;
        private int lastDetailRemaining = int.MinValue;

        // [식량 루프] 신선도도 캐시 키에 넣는다. 넣지 않으면 칸의 내용(종류·개수·내구도)이 그대로인 채
        // 신선도만 "신선 → 상하기 시작"으로 넘어갔을 때 조기 반환에 걸려 표시가 영영 굳는다.
        private string lastDetailFreshness;

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

            // 인벤토리는 이제 툴팁을 **띄우지 않는다**(상세 패널이 그 일을 한다). 그래도 인스턴스를
            // 잡아두는 이유는 이것이 창과 무관한 단일 인스턴스라 상자/제작 창이 같은 것을 재사용하고,
            // 창을 닫을 때 남아 있는 툴팁을 걷어낼 손잡이가 필요하기 때문이다.
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
            windowRt.sizeDelta = new Vector2(WindowWidth, WindowHeight);
            panelRoot = windowRt.gameObject;

            BuildHeader();

            // 헤더와 본문을 가르는 1px 선. 창 안에서 "여기부터 내용"이라는 신호를 배경색 차이 대신
            // 선 하나로 준다(배경을 한 단계 더 밝히면 뒤의 월드가 다시 비친다).
            UIBuilder.CreateSeparator(windowRt, "HeaderSeparator", UITheme.HeaderHeight);

            BuildFilterRow();

            // 격자: 스크롤 + 칸 뷰 재사용은 공용 VirtualSlotGrid가 담당한다(보관 상자 창과 같은 구현).
            grid.Build(windowRt, "SlotGrid",
                GridWidth, GridHeight,
                Columns, SlotSize, SlotSpacing, durabilityBars: true);
            grid.Root.anchoredPosition = new Vector2(WindowPadding, -GridTopOffset);
            grid.onEnter = OnSlotEnter;
            grid.onExit = OnSlotExit;
            grid.onLeftClick = OnSlotLeftClick;
            grid.onRightClick = OnSlotRightClick;
            grid.onStyle = ApplyCellStyle;
            grid.onRowsChanged = OnGridScrolled;

            BuildDetailPane();
        }

        /// <summary>헤더(제목 · 용량 · 닫기). 창을 끄는 손잡이도 여기 붙는다.</summary>
        private void BuildHeader()
        {
            var header = UIBuilder.CreatePanel(
                windowRt, "Header",
                anchorMin: new Vector2(0f, 1f), anchorMax: new Vector2(1f, 1f),
                offsetMin: new Vector2(0f, -UITheme.HeaderHeight), offsetMax: Vector2.zero,
                color: UITheme.HeaderBackground);

            var title = UIBuilder.CreateText(header, "Title", "인벤토리", UITheme.FontTitle, UITheme.TextPrimary, TextAnchor.MiddleLeft);
            title.raycastTarget = false; // 제목 글자가 드래그 입력을 가로채지 않게(입력은 헤더가 받는다)
            title.horizontalOverflow = HorizontalWrapMode.Overflow;
            title.rectTransform.anchorMin = Vector2.zero;
            title.rectTransform.anchorMax = Vector2.one;
            title.rectTransform.offsetMin = new Vector2(WindowPadding, 0f);
            title.rectTransform.offsetMax = new Vector2(-180f, 0f);

            // 용량은 격자 위가 아니라 **창 이름 옆**에 둔다. 그래야 "이 창이 얼마나 찼는가"로 읽히고,
            // 본문 첫 줄은 필터 하나만 남아 왼쪽 단이 조용해진다.
            capacityLabel = UIBuilder.CreateText(header, "Capacity", "", UITheme.FontBody, UITheme.TextDim, TextAnchor.MiddleRight);
            capacityLabel.raycastTarget = false;
            capacityLabel.horizontalOverflow = HorizontalWrapMode.Overflow;
            var capacityRt = capacityLabel.rectTransform;
            capacityRt.anchorMin = new Vector2(1f, 0f);
            capacityRt.anchorMax = new Vector2(1f, 1f);
            capacityRt.pivot = new Vector2(1f, 0.5f);
            capacityRt.sizeDelta = new Vector2(200f, 0f);
            capacityRt.anchoredPosition = new Vector2(-46f, 0f);

            // 닫기(X): 마우스만으로 창을 닫는 유일한 확실한 수단이라 항상 같은 자리(우상단)에 둔다.
            // Danger Red는 "되돌릴 수 없는 행동"이 아니라 창 닫기라는 관습적 의미로 쓴다 - 팔레트 안이고,
            // 이 화면에서 빨강을 쓰는 다른 요소(가득 참 경고/확인 대기 테두리)와 형태·위치가 완전히 다르다.
            var close = UIBuilder.CreateButton(header, "Close", "X", () => SetOpen(false));
            var closeRt = close.GetComponent<RectTransform>();
            closeRt.anchorMin = new Vector2(1f, 1f);
            closeRt.anchorMax = new Vector2(1f, 1f);
            closeRt.pivot = new Vector2(1f, 1f);
            closeRt.sizeDelta = new Vector2(30f, 24f);
            closeRt.anchoredPosition = new Vector2(-8f, -10f);

            var closeImage = close.GetComponent<Image>();
            if (closeImage != null)
            {
                Color closeColor = DangerRed;
                closeColor.a = 0.75f;
                closeImage.color = closeColor;
            }

            // 헤더 자체가 드래그 손잡이다. 창 전체를 잡게 만들지 않은 이유: 격자 칸을 클릭·우클릭
            // 하는 조작과 드래그가 같은 영역에서 겹치면, 버리려고 우클릭하다 창이 딸려 움직인다.
            dragHandle = UIBuilder.AttachDragHandle(header, windowRt, canvasRect, UITheme.HeaderHeight);
            dragHandle.onMoved = position =>
            {
                savedWindowPosition = position;
                hasSavedWindowPosition = true;
            };
        }

        /// <summary>본문 왼쪽 단의 첫 줄: 카테고리 필터 칩.</summary>
        private void BuildFilterRow()
        {
            // 필터를 F키로만 돌릴 수 있으면 마우스만 쓰는 사람에게는 없는 기능이나 마찬가지다.
            // 칩을 누르면 같은 순환이 돌고, 라벨에 키를 함께 적어 키 조작도 계속 노출한다.
            var filterButton = UIBuilder.CreateButton(windowRt, "FilterChip", "", CycleFilter);
            var filterRt = filterButton.GetComponent<RectTransform>();
            filterRt.anchorMin = new Vector2(0f, 1f);
            filterRt.anchorMax = new Vector2(0f, 1f);
            filterRt.pivot = new Vector2(0f, 1f);
            filterRt.sizeDelta = new Vector2(FilterChipWidth, InfoRowHeight);
            filterRt.anchoredPosition = new Vector2(WindowPadding, -BodyTop);

            // 기본 버튼색(초록)을 그대로 두면 같은 창의 '버리기'와 같은 무게로 보인다. 필터는 아무 때나
            // 눌러도 되는 조회 조작이고 버리기는 되돌릴 수 없는 조작이라, 둘의 시각적 무게가 같으면 안 된다.
            // 색을 새로 만들지 않고 흰색 알파만 낮춰 '칸과 같은 재질의 칩'으로 보이게 한다.
            var chipImage = filterButton.GetComponent<Image>();
            if (chipImage != null)
            {
                chipImage.color = new Color(1f, 1f, 1f, 0.35f);
                var chipColors = filterButton.colors;
                chipColors.normalColor = new Color(1f, 1f, 1f, 0.30f);      // 실효 알파 0.105
                chipColors.highlightedColor = new Color(1f, 1f, 1f, 0.55f); // 0.19
                chipColors.pressedColor = new Color(1f, 1f, 1f, 0.75f);     // 0.26
                chipColors.selectedColor = chipColors.normalColor;
                chipColors.disabledColor = new Color(1f, 1f, 1f, 0.15f);
                filterButton.colors = chipColors;
            }

            filterLabel = filterButton.GetComponentInChildren<Text>();
            if (filterLabel != null)
            {
                filterLabel.fontSize = UITheme.FontBody;
                filterLabel.color = UITheme.TextPrimary;
                filterLabel.horizontalOverflow = HorizontalWrapMode.Overflow;
            }
        }

        /// <summary>
        /// 본문 오른쪽 단: 아이템 상세. 위에서 아래로 아이콘 → 이름 → 분류 → 설명 → 수치 → 사용법이고,
        /// **버리기 버튼과 조작 안내는 패널 맨 아래에 고정**한다. 설명 길이에 따라 버튼이 오르내리면
        /// 되돌릴 수 없는 조작의 위치가 아이템마다 달라져 오폭을 부른다.
        /// </summary>
        private void BuildDetailPane()
        {
            detailPane = UIBuilder.CreatePanel(
                windowRt, "DetailPane",
                anchorMin: new Vector2(0f, 1f), anchorMax: new Vector2(0f, 1f),
                offsetMin: Vector2.zero, offsetMax: Vector2.zero,
                color: UITheme.PaneBackground);
            detailPane.pivot = new Vector2(0f, 1f);
            detailPane.sizeDelta = new Vector2(UITheme.DetailPaneWidth, DetailPaneHeight);
            detailPane.anchoredPosition = new Vector2(DetailPaneX, -BodyTop);

            // 아이콘은 격자 칸(62px)보다 커야 "확대해서 보는 자리"로 읽힌다.
            var iconGo = new GameObject("DetailIcon", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            iconGo.transform.SetParent(detailPane, false);
            var iconRt = iconGo.GetComponent<RectTransform>();
            iconRt.anchorMin = new Vector2(0.5f, 1f);
            iconRt.anchorMax = new Vector2(0.5f, 1f);
            iconRt.pivot = new Vector2(0.5f, 1f);
            iconRt.sizeDelta = new Vector2(72f, 72f);
            iconRt.anchoredPosition = new Vector2(0f, -DetailPadding);
            detailIcon = iconGo.GetComponent<Image>();
            detailIcon.raycastTarget = false;
            detailIcon.preserveAspect = true;

            detailName = CreateDetailText("DetailName", UITheme.FontHeading, UITheme.TextPrimary, TextAnchor.UpperCenter, 92f, 44f);
            detailCategory = CreateDetailText("DetailCategory", UITheme.FontBody, UITheme.TextDim, TextAnchor.UpperCenter, 140f, 18f);

            // 아이템의 "정체"와 "설명"을 선으로 가른다 - 창 헤더가 하는 일과 같은 규칙을 패널 안에서 반복한다.
            detailSeparator = UIBuilder.CreateSeparator(detailPane, "DetailSeparator", 164f, DetailPadding);

            // 설명·수치·사용법은 자리를 고정하지 않고 **위에서부터 붙여 쌓는다**. 고정 높이를 주면
            // 설명이 세 줄인 아이템에서 다음 줄까지 70px 넘는 구멍이 생겨 패널이 비어 보였다.
            // (버리기 버튼만은 계속 패널 바닥 고정이다 - 되돌릴 수 없는 조작의 자리는 움직이면 안 된다.)
            var block = new GameObject("DetailBlock", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            block.transform.SetParent(detailPane, false);
            detailBlock = block.GetComponent<RectTransform>();
            detailBlock.anchorMin = new Vector2(0f, 1f);
            detailBlock.anchorMax = new Vector2(1f, 1f);
            detailBlock.pivot = new Vector2(0.5f, 1f);
            // 폭은 패널 폭에서 좌우 여백만 뺀 값, 높이는 ContentSizeFitter가 내용에 맞춰 덮어쓴다.
            detailBlock.sizeDelta = new Vector2(-DetailPadding * 2f, 0f);
            detailBlock.anchoredPosition = new Vector2(0f, -172f);

            var blockLayout = block.GetComponent<VerticalLayoutGroup>();
            blockLayout.spacing = 10f;
            blockLayout.childControlHeight = true;
            blockLayout.childControlWidth = true;
            blockLayout.childForceExpandHeight = false;
            blockLayout.childForceExpandWidth = true;

            var blockFitter = block.GetComponent<ContentSizeFitter>();
            blockFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            detailDescription = CreateBlockText("DetailDescription", UITheme.TextDim);
            detailStats = CreateBlockText("DetailStats", UITheme.TextPrimary);
            detailUsage = CreateBlockText("DetailUsage", UITheme.TextDim);

            // 빈 상태 문구는 패널 한가운데다. 위쪽(아이콘 자리)에 두면 아이템이 있을 때와 없을 때
            // 시선이 같은 곳에 머물러, 패널이 비었다는 사실이 늦게 읽힌다.
            detailEmptyLabel = UIBuilder.CreateText(detailPane, "DetailEmpty", "아이템을 선택하세요", UITheme.FontBody, UITheme.TextDim, TextAnchor.MiddleCenter);
            detailEmptyLabel.raycastTarget = false;
            var emptyRt = detailEmptyLabel.rectTransform;
            emptyRt.anchorMin = Vector2.zero;
            emptyRt.anchorMax = Vector2.one;
            emptyRt.offsetMin = new Vector2(DetailPadding, 0f);
            emptyRt.offsetMax = new Vector2(-DetailPadding, 0f);

            // 확인 대기 문구는 버튼 **바로 위**다. 버튼 라벨("확실?")만으로는 무엇을 몇 개 버리는지 알 수 없다.
            confirmLabel = UIBuilder.CreateText(detailPane, "Confirm", "", UITheme.FontBody, SunstrokeGold, TextAnchor.LowerLeft);
            confirmLabel.raycastTarget = false;
            var confirmRt = confirmLabel.rectTransform;
            confirmRt.anchorMin = new Vector2(0f, 0f);
            confirmRt.anchorMax = new Vector2(1f, 0f);
            confirmRt.pivot = new Vector2(0.5f, 0f);
            confirmRt.offsetMin = new Vector2(DetailPadding, HintHeight + FooterButtonHeight + 22f);
            confirmRt.offsetMax = new Vector2(-DetailPadding, HintHeight + FooterButtonHeight + 22f + 34f);

            dropButton = UIBuilder.CreateButton(detailPane, "Drop", "버리기", OnDropButtonClicked);
            var dropRt = dropButton.GetComponent<RectTransform>();
            dropRt.anchorMin = new Vector2(0.5f, 0f);
            dropRt.anchorMax = new Vector2(0.5f, 0f);
            dropRt.pivot = new Vector2(0.5f, 0f);
            dropRt.sizeDelta = new Vector2(DropButtonWidth, FooterButtonHeight);
            dropRt.anchoredPosition = new Vector2(0f, HintHeight + 6f);
            dropButtonLabel = dropButton.GetComponentInChildren<Text>();

            hintLabel = UIBuilder.CreateText(detailPane, "Hint", "", UITheme.FontBody, UITheme.TextDim, TextAnchor.MiddleCenter);
            hintLabel.raycastTarget = false;
            var hintRt = hintLabel.rectTransform;
            hintRt.anchorMin = new Vector2(0f, 0f);
            hintRt.anchorMax = new Vector2(1f, 0f);
            hintRt.pivot = new Vector2(0.5f, 0f);
            hintRt.offsetMin = new Vector2(DetailPadding, DetailPadding - HintHeight * 0.5f);
            hintRt.offsetMax = new Vector2(-DetailPadding, DetailPadding + HintHeight * 0.5f);

            ShowDetailEmpty();
        }

        /// <summary>
        /// 세로 레이아웃 블록에 들어가는 글줄. 높이를 지정하지 않는다 - VerticalLayoutGroup이
        /// Text의 preferredHeight(=실제 줄 수)를 읽어 스스로 잡는다.
        /// </summary>
        private Text CreateBlockText(string name, Color color)
        {
            var text = UIBuilder.CreateText(detailBlock, name, "", UITheme.FontBody, color, TextAnchor.UpperLeft);
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            return text;
        }

        /// <summary>
        /// 상세 패널 안의 글줄 하나. 위(top)에서부터의 거리로 자리를 정한다 - 세로 레이아웃 그룹을 쓰면
        /// 글자 길이에 따라 아래 요소가 밀려, 아이템마다 버튼 위치가 달라진다.
        /// </summary>
        private Text CreateDetailText(string name, int fontSize, Color color, TextAnchor alignment, float top, float height)
        {
            var text = UIBuilder.CreateText(detailPane, name, "", fontSize, color, alignment);
            text.raycastTarget = false;

            var rt = text.rectTransform;
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.offsetMin = new Vector2(DetailPadding, -top - height);
            rt.offsetMax = new Vector2(-DetailPadding, -top);
            return text;
        }

        /// <summary>
        /// 스크롤로 보이는 줄이 갈리면 hover 강조를 정리한다. 커서는 그대로인데 칸이 미끄러져
        /// 나갔으므로, 안 그러면 엉뚱한 칸이 밝게 남고 상세 패널이 옛 물건을 계속 설명한다.
        /// **선택은 건드리지 않는다** - 선택은 칸 위치가 아니라 물건(대표 인스턴스)에 걸려 있어서,
        /// 화면 밖으로 스크롤해도 살아 있어야 버리기 버튼이 그대로 동작한다.
        /// </summary>
        private void OnGridScrolled()
        {
            hoverIndex = -1;
            RefreshDetail();
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

            bool hovered = cell.index >= 0 && cell.index == hoverIndex;

            if (armed)
                cell.visual.background.color = SlotArmedColor;
            else if (hovered)
                cell.visual.background.color = SlotHoverColor;
            else
                cell.visual.background.color = cell.data != null ? SlotFilledColor : SlotEmptyColor;

            // 테두리는 배경과 **다른 축**을 쓴다: 배경은 밝기로 상태를, 테두리는 색상으로 분류를 말한다.
            // 그래서 한 칸만 봐도 "무슨 종류인가 / 지금 무슨 상태인가"가 동시에 읽힌다(칸의 기본값은
            // VirtualSlotGrid.Apply가 넣고, 이 창에만 있는 hover·선택을 여기서 덮어쓴다).
            cell.visual.frame.color = UITheme.SlotFrame(
                cell.data != null ? UIBuilder.GetItemCategoryColor(cell.data) : Color.white,
                cell.data != null, hovered, selected);

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
                RefreshDetail();

                // 인벤토리는 툴팁을 띄우지 않지만, 다른 창이 띄워 둔 것이 남아 있으면 여기서 걷어낸다.
                if (tooltip != null)
                    tooltip.Hide();
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
            // [상세 패널] 창이 674x575로 넓어졌지만 자리 계산은 rect.width/height에서 나오므로 값은
            // 그대로 둔다. 왼쪽 붙임을 유지해도 674 + 24는 1920 화면 안이고, HUD 아래 여백도 그대로다.
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
            RefreshDetail();
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

            capacityLabel.text = $"{used} / {capacity}" + (used >= capacity ? "  (가득 참)" : "");
            capacityLabel.color = used >= capacity ? DangerRed
                : (capacity > 0 && (float)used / capacity >= 0.8f ? SunstrokeGold : UITheme.TextPrimary);

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

            // 무엇을 선택했는지는 상세 패널이 이미 크게 보여주므로, 이 줄은 **확인 대기일 때만** 쓴다.
            // 평상시에도 글자가 차 있으면 정작 위험한 순간의 문구가 눈에 띄지 않는다.
            if (confirmLabel != null)
            {
                if (armed && data != null)
                {
                    confirmLabel.text = pendingWhole
                        ? $"{data.itemName} {count}개를 전부 버린다\n한 번 더 누르면 되돌릴 수 없다"
                        : $"{data.itemName}을(를) 버린다\n한 번 더 누르면 되돌릴 수 없다";
                }
                else
                {
                    confirmLabel.text = "";
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

            // 문구가 짧아진 이유: 이 줄은 이제 폭 208px 패널 안에 있다. 버리기 조작의 자세한 설명
            // (Shift=한 칸 전부, 확인 절차 유무)은 상세 패널의 사용법 줄이 아이템마다 이미 적어 준다.
            if (warning)
            {
                hintLabel.text = string.IsNullOrEmpty(rejectedName)
                    ? "칸이 가득 차 줍지 못했다"
                    : $"가득 참: {rejectedName} 못 주움";
                hintLabel.color = DangerRed;
                return;
            }

            // 칸이 한 화면을 넘으면(100칸 = 17줄) 스크롤이 있다는 사실을 적어 준다 - 스크롤 막대가
            // 따로 없는 격자라 안내가 없으면 아래쪽 칸의 존재를 모른 채로 "가득 찼다"고 오해한다.
            hintLabel.text = grid.IsScrollable
                ? $"휠 스크롤 · [{toggleKey}] 닫기"
                : $"클릭 선택 · [{toggleKey}] 닫기";
            hintLabel.color = UITheme.TextDim;
        }

        // ────────────────────────────────────────────────────────────────────────
        // 입력
        // ────────────────────────────────────────────────────────────────────────

        private void OnSlotEnter(int index)
        {
            hoverIndex = index;
            RefreshSlotStates();
            RefreshDetail();
        }

        private void OnSlotExit(int index)
        {
            if (hoverIndex != index)
                return;

            hoverIndex = -1;
            RefreshSlotStates();
            RefreshDetail();
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

            // 선택이 바뀌면 상세 패널이 가리키는 대상도 바뀐다(선택 > 호버).
            RefreshDetail();
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
        // 상세 패널
        // ────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// 상세 패널이 보여줄 칸. **선택이 호버를 이긴다** - 선택은 사용자가 의도적으로 고정한 대상이라,
        /// 커서가 지나가는 칸마다 내용이 갈리면 버리기 버튼과 설명이 서로 다른 물건을 가리키게 된다.
        /// </summary>
        private int DetailIndex => selectedIndex >= 0 ? selectedIndex : hoverIndex;

        /// <summary>
        /// 상세 패널을 채운다. 예전에는 이 정보를 커서를 따라다니는 툴팁이 보여줬는데, 툴팁은
        /// (1) 커서 아래 칸을 가려 격자 판독을 방해하고 (2) 창 밖으로 삐져나가며 (3) 선택한 물건을
        /// 계속 보고 있을 수단이 못 된다. 자리를 고정하면 세 문제가 한 번에 사라진다.
        /// </summary>
        private void RefreshDetail()
        {
            if (detailPane == null)
                return;

            InventoryStack stack = grid.GetStack(DetailIndex);
            ItemData data = stack != null ? stack.data : null;

            if (data == null)
            {
                if (!detailEmpty)
                    ShowDetailEmpty();
                return;
            }

            // 대표 인스턴스가 없으면 내구도를 "모름"으로 둔다. 0으로 믿으면 넣어둔 손도끼가 전부
            // 다 닳은 것으로 보인다(격자 칸의 막대와 같은 규칙).
            int remaining = stack.representative != null ? stack.RemainingUses : int.MinValue;
            string freshness = BuildFreshnessText(stack);

            if (!detailEmpty && data == lastDetailData && stack.count == lastDetailCount
                && remaining == lastDetailRemaining && freshness == lastDetailFreshness)
                return;

            detailEmpty = false;
            lastDetailData = data;
            lastDetailCount = stack.count;
            lastDetailRemaining = remaining;
            lastDetailFreshness = freshness;

            SetDetailContentActive(true);
            if (detailEmptyLabel != null)
                detailEmptyLabel.gameObject.SetActive(false);

            Color categoryColor = UIBuilder.GetItemCategoryColor(data);

            // 아이콘 없이 추가되는 아이템이 있을 수 있으므로 격자 칸과 같은 폴백(카테고리색 면)을 쓴다.
            detailIcon.sprite = data.icon;
            detailIcon.color = data.icon != null ? Color.white : categoryColor;

            detailName.text = data.itemName;
            detailCategory.text = GetCategoryDisplayName(GetCategory(data));
            detailCategory.color = categoryColor;
            detailDescription.text = data.description;
            detailStats.text = CombineUsageLine(BuildAmountText(data, stack.count, remaining), freshness);

            // 사용법과 버리기 안내는 두 줄로 나눈다 - 한 줄로 이으면 폭 208px 안에서 어디까지가
            // "쓰는 법"이고 어디부터가 "버리는 법"인지 구분되지 않는다.
            string usage = GetUsageHint(data);
            detailUsage.text = string.IsNullOrEmpty(usage) ? GetDropHint(data) : usage + "\n" + GetDropHint(data);

            // 내용이 빈 줄은 아예 접는다. 세로 레이아웃은 글자가 없는 칸에도 간격 10px를 넣기 때문에,
            // 설명이 비어 있는 아이템에서 이유 없는 틈이 생긴다.
            detailDescription.gameObject.SetActive(!string.IsNullOrEmpty(detailDescription.text));
            detailStats.gameObject.SetActive(!string.IsNullOrEmpty(detailStats.text));
            detailUsage.gameObject.SetActive(!string.IsNullOrEmpty(detailUsage.text));
        }

        /// <summary>
        /// 선택도 호버도 없는 상태. 옛 내용을 남겨두면 지금 버리기 버튼이 무엇을 겨누고 있는지
        /// 착각하게 된다 - 그래서 문구 하나만 남기고 전부 끈다.
        /// </summary>
        private void ShowDetailEmpty()
        {
            detailEmpty = true;
            lastDetailData = null;
            lastDetailCount = -1;
            lastDetailRemaining = int.MinValue;
            lastDetailFreshness = null;

            SetDetailContentActive(false);
            if (detailEmptyLabel != null)
                detailEmptyLabel.gameObject.SetActive(true);
        }

        /// <summary>상세 내용 부품을 한꺼번에 켜고 끈다(버리기 버튼·조작 안내는 항상 켜져 있다).</summary>
        private void SetDetailContentActive(bool active)
        {
            if (detailIcon != null)
                detailIcon.gameObject.SetActive(active);
            if (detailName != null)
                detailName.gameObject.SetActive(active);
            if (detailCategory != null)
                detailCategory.gameObject.SetActive(active);
            if (detailSeparator != null)
                detailSeparator.gameObject.SetActive(active);
            if (detailDescription != null)
                detailDescription.gameObject.SetActive(active);
            if (detailStats != null)
                detailStats.gameObject.SetActive(active);
            if (detailUsage != null)
                detailUsage.gameObject.SetActive(active);
        }

        /// <summary>
        /// 수치 줄(개수 · 내구도). 개수 1은 적지 않는다 - "x1"은 정보가 없고 줄만 차지한다.
        /// 내구도는 유한한 도구에만 뜬다(무제한 칼·물통은 남은 횟수라는 개념이 없다).
        /// </summary>
        private static string BuildAmountText(ItemData data, int count, int remaining)
        {
            string amount = count > 1 ? "개수 " + count : "";

            if (data != null && !data.IsUnlimited && data.maxUses > 1 && remaining != int.MinValue)
            {
                string durability = "내구도 " + remaining + "/" + data.maxUses;
                return string.IsNullOrEmpty(amount) ? durability : amount + " · " + durability;
            }

            return amount;
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

        /// <summary>
        /// [식량 루프] 이 칸의 신선도 문구("신선 82%" 같은 것). 신선도를 표시할 종류가 아니면 빈 문자열이라
        /// 음식이 아닌 칸의 툴팁은 한 글자도 달라지지 않는다.
        ///
        /// 판정을 여기서 새로 만들지 않는다 - 표시 여부(ShowsFreshness) · 단계 문구(FreshnessLabel) ·
        /// 비율(Freshness01)은 전부 InventoryStack이 이미 공개한 값이고, 그 값들은 다시 FoodSpoilage
        /// 하나에서 나온다(칸의 신선도는 **그 칸에서 가장 오래된 것**을 따른다 - InventoryStack.oldest).
        /// </summary>
        private static string BuildFreshnessText(InventoryStack stack)
        {
            if (stack == null || !stack.ShowsFreshness)
                return "";

            string label = stack.FreshnessLabel;
            if (string.IsNullOrEmpty(label))
                return "";

            return $"{label} {Mathf.RoundToInt(stack.Freshness01 * 100f)}%";
        }

        /// <summary>
        /// 두 조각을 " · "로 잇되 비어 있는 쪽은 건너뛴다(둘 다 없으면 빈 문자열).
        /// 상세 패널의 수치 줄(개수·내구도 + 신선도)이 이 규칙을 그대로 쓴다.
        /// </summary>
        private static string CombineUsageLine(string usageHint, string freshness)
        {
            if (string.IsNullOrEmpty(freshness))
                return usageHint;

            if (string.IsNullOrEmpty(usageHint))
                return freshness;

            return $"{usageHint} · {freshness}";
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
