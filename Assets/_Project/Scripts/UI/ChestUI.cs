using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using MakeGame.Data;
using MakeGame.Player;
using MakeGame.Systems;

namespace MakeGame.UI
{
    /// <summary>
    /// 보관 상자 창. 왼쪽에 상자 칸, 오른쪽에 플레이어 소지품 칸을 나란히 놓고 좌/우클릭으로 물건을
    /// 주고받는다. 상단에 등급 이름과 사용 칸 수, 하단에 업그레이드 버튼과 재료표가 붙는다.
    ///
    /// **여는 경로는 하나뿐이다.** 전용 토글 키를 만들지 않고 <see cref="InteractionController"/>의
    /// 기존 상호작용 키(기본 E)에 얹었다 - 상자를 조준하고 E를 누르면 열리고, 열려 있는 동안 E를 다시
    /// 누르면 닫힌다(InteractionController.Update 맨 앞의 CloseIfOpen 분기). 새 키를 만들면
    /// Tab/V/J/M/B/Esc가 이미 차 있는 키 표에 하나를 더 얹는 것이고, 상자는 조준했을 때만 의미가
    /// 있는 창이라 전역 토글 키를 줄 이유가 없다.
    ///
    /// **200칸 문제(이 창의 핵심).** 특대 상자는 200칸이고, 칸 뷰 하나가 GameObject 6개(배경/색 띠/
    /// 아이콘/글자/내구도 막대/개수)라 전부 만들면 1200개가 넘는다. 등급을 올릴 때마다 그걸 새로
    /// 만들면 프레임이 그대로 튄다. 그래서 격자를 GridLayoutGroup으로 깔지 않고 **화면에 실제로
    /// 보이는 줄 + 여유 2줄만큼만 칸 뷰를 만들어 스크롤에 따라 재사용(가상화)** 한다
    /// (<see cref="VirtualSlotPanel"/>). 만들어지는 칸 뷰는 상자 쪽 최대 48개로 고정이고,
    /// 50 → 100 → 150 → 200 어느 등급으로 올라가도 **칸 뷰는 단 하나도 새로 만들지 않는다**
    /// (콘텐츠 높이만 바뀐다).
    ///
    /// 이 프로젝트의 창 규칙을 그대로 따른다:
    /// · 프리팹 없이 100% 코드 생성. 창/제목 표시줄/닫기 버튼/격자 칸은 전부 <see cref="UIBuilder"/> 팩토리.
    /// · <see cref="Time.timeScale"/>을 건드리지 않는다. 인벤토리·제작·퀘스트와 같은 "플레이 중 창"이고,
    ///   timeScale 0은 타이틀/설정/엔딩/사망 화면만 쓴다. 그래도 이 파일의 모든 타이머는 unscaled로 센다.
    /// · <see cref="Cursor"/>를 직접 만지지 않는다. 커서 잠금은 <see cref="CursorLockController"/>가 단독으로
    ///   결정하고, 그 판정 기준은 "활성 상태인 <see cref="UIDragHandle"/>이 있는가"다. 그래서 제목 표시줄에
    ///   드래그 손잡이를 붙이고 닫을 때 창 루트를 SetActive(false) 하는 것만으로 커서가 알아서 풀리고 잠긴다.
    /// · 씬에 인스턴스가 없다. RuntimeInitializeOnLoadMethod로 스스로 생성되므로 **코드 기본값이 유일한 진실**이다.
    /// </summary>
    public class ChestUI : MonoBehaviour
    {
        /// <summary>이 씬의 보관 상자 창(씬 리로드마다 새 인스턴스로 교체된다).</summary>
        public static ChestUI Instance { get; private set; }

        // ── 치수 (인벤토리 창과 같은 칸 크기·간격을 쓴다) ──────────────────────────
        private const int Columns = 6;
        private const float SlotSize = 62f;
        private const float SlotSpacing = 6f;

        /// <summary>한 줄이 차지하는 세로 길이(칸 + 간격). 가상화 계산의 기준 단위다.</summary>
        private const float RowStride = SlotSize + SlotSpacing;

        /// <summary>격자 한 쪽의 가로 폭(6열).</summary>
        private const float PanelWidth = Columns * SlotSize + (Columns - 1) * SlotSpacing;

        /// <summary>한 화면에 보이는 줄 수. 6줄 = 36칸이 항상 보이고 나머지는 스크롤한다.</summary>
        private const int VisibleRows = 6;

        /// <summary>스크롤 도중 위아래로 반쯤 걸치는 줄을 덮기 위한 여유 줄 수.</summary>
        private const int SpareRows = 2;

        private const float ViewportHeight = VisibleRows * SlotSize + (VisibleRows - 1) * SlotSpacing;

        private const float WindowPadding = 14f;
        private const float PanelGap = 16f;
        private const float TitleBarHeight = 34f;
        private const float HeaderHeight = 22f;
        private const float CaptionHeight = 18f;
        private const float UpgradeRowHeight = 30f;
        private const float MessageHeight = 18f;
        private const float HintHeight = 16f;
        private const float UpgradeButtonWidth = 150f;

        private const float WindowWidth = WindowPadding * 2f + PanelWidth * 2f + PanelGap;

        // 세로 배치(위에서부터 쌓아 내려간 값). 창 높이는 이 합으로 결정된다.
        private const float HeaderTop = TitleBarHeight + 6f;
        private const float CaptionTop = HeaderTop + HeaderHeight + 4f;
        private const float GridTop = CaptionTop + CaptionHeight + 4f;
        private const float UpgradeTop = GridTop + ViewportHeight + 10f;
        private const float MessageTop = UpgradeTop + UpgradeRowHeight + 4f;
        private const float HintTop = MessageTop + MessageHeight + 4f;
        private const float WindowHeight = HintTop + HintHeight + 12f;

        /// <summary>
        /// 플레이어 소지품 쪽 저주파 갱신 주기(초). 인벤토리 창과 같은 이유다 - 내구도(remainingUses)가
        /// 1씩 줄어드는 평범한 사용에는 InventoryChanged가 발행되지 않아 이벤트만으로는 막대가 굳는다.
        /// **상자 쪽은 이 폴링을 타지 않는다.** 상자 안의 물건은 스스로 닳지 않으므로 StorageChest.Changed
        /// 하나면 충분하고, 200칸짜리 스택 목록을 0.2초마다 다시 만들 이유가 없다.
        /// </summary>
        private const float RefreshInterval = 0.2f;

        /// <summary>실패 사유/안내 문구를 띄워 두는 시간(초).</summary>
        private const float MessageDuration = 4f;

        /// <summary>플레이어가 이만큼 멀어지면 창이 저절로 닫힌다(상호작용 거리에 더하는 여유, m).</summary>
        private const float AutoCloseSlack = 2.5f;

        // 색: ArtDirection.md 팔레트 안에서만 쓴다(새 색을 만들지 않는다).
        private static readonly Color NeutralGray = new Color(0.8f, 0.8f, 0.8f, 1f);        // #CCCCCC
        private static readonly Color DimGray = new Color(0.55f, 0.55f, 0.55f, 1f);
        private static readonly Color SunstrokeGold = new Color(0.902f, 0.749f, 0.2f, 1f);  // #E6BF33
        private static readonly Color DangerRed = new Color(0.8f, 0.2f, 0.2f, 1f);          // #CC3333
        private static readonly Color MedicGreen = new Color(0.31f, 0.659f, 0.478f, 1f);    // #4FA87A

        // 재료표 rich text에 쓰는 색 문자열(Text.supportRichText). 위 Color 값과 같은 색이다.
        private const string HexOk = "CCCCCC";
        private const string HexShort = "CC3333";

        /// <summary>
        /// 창 위치를 세션 동안 기억한다. static인 이유는 인벤토리 창과 같다 - 이 컴포넌트는 씬 로드마다
        /// 통째로 새로 생성되므로 인스턴스 필드에 두면 씬을 다시 불러올 때마다 처음 자리로 돌아간다.
        /// </summary>
        private static bool hasSavedWindowPosition;
        private static Vector2 savedWindowPosition;

        // ── 대상 ────────────────────────────────────────────────────────────────
        private StorageChest chest;
        private bool chestSubscribed;

        private PlayerInventory inventory;
        private bool inventorySubscribed;

        private InteractionController interaction;
        private KeyCode interactKey = KeyCode.E;

        // ── 화면 부품 ───────────────────────────────────────────────────────────
        private RectTransform canvasRect;
        private GameObject panelRoot;
        private RectTransform windowRt;
        private UIDragHandle dragHandle;
        private Text titleLabel;
        private Text headerLabel;
        private Text chestCaption;
        private Text playerCaption;
        private Button upgradeButton;
        private Text upgradeButtonLabel;
        private Text upgradeCostLabel;
        private Text messageLabel;
        private Text hintLabel;
        private ItemTooltipUI tooltip;

        private VirtualSlotPanel chestPanel;
        private VirtualSlotPanel playerPanel;

        private float refreshTimer;
        private float messageUntil = -1f;

        // 마지막으로 문자열을 다시 만들었을 때의 표시 조건. 실제로 바뀐 갱신에서만 다시 쓴다.
        private int lastHeaderUsed = -1;
        private int lastHeaderCapacity = -1;
        private string lastHeaderTier;
        private string lastCostText;
        private bool lastCanUpgrade;

        private readonly StringBuilder costBuilder = new StringBuilder(96);

        /// <summary>커서가 얹혀 있는 칸(툴팁·강조용). 어느 쪽 격자인지까지 함께 들고 있어야 한다.</summary>
        private VirtualSlotPanel hoverPanel;
        private int hoverIndex = -1;

        // 툴팁이 마지막으로 채워진 내용. 커서가 같은 칸에 머무는 동안 다시 만들지 않는다.
        private ItemData lastTooltipData;
        private int lastTooltipCount = -1;
        private int lastTooltipRemaining = int.MinValue;

        /// <summary>지금 창이 열려 있는지.</summary>
        public bool IsWindowOpen => panelRoot != null && panelRoot.activeSelf;

        /// <summary>지금 이 씬에서 보관 상자 창이 열려 있는지(다른 시스템이 입력을 양보할 때 쓴다).</summary>
        public static bool IsOpen => Instance != null && Instance.IsWindowOpen;

        // ────────────────────────────────────────────────────────────────────────
        // 부트스트랩 / 수명
        // ────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// 씬이 로드될 때마다 스스로 생성된다(QuestUI·CursorLockController와 같은 자기 완결 패턴).
        /// 씬 파일을 고칠 수 없으므로 이 방식이 아니면 이 창은 존재할 수 없다.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Bootstrap()
        {
            SceneManager.sceneLoaded += (scene, mode) =>
            {
                // 중복 생성 방지: 창이 두 개면 상자 하나에 두 창이 붙어 서로 다른 내용을 그린다.
                if (FindAnyObjectByType<ChestUI>() != null)
                    return;

                var go = new GameObject("ChestUI");
                go.AddComponent<ChestUI>();
            };
        }

        /// <summary>
        /// 인스턴스를 가져온다. 없으면(부트스트랩보다 먼저 상호작용이 일어난 경우) 그 자리에서 만든다.
        /// ItemTooltipUI.GetOrCreate와 같은 안전장치다.
        /// </summary>
        public static ChestUI GetOrCreate()
        {
            if (Instance != null)
                return Instance;

            var existing = FindAnyObjectByType<ChestUI>();
            if (existing != null)
                return existing;

            var go = new GameObject("ChestUI");
            return go.AddComponent<ChestUI>();
        }

        /// <summary>
        /// UI 계층을 만들고 닫힌 상태로 둔다. Start가 아니라 Awake인 이유: 같은 프레임에
        /// InteractionController가 OpenFor를 부를 수 있어(실행 순서는 보장되지 않는다) 그때 창이
        /// 이미 만들어져 있어야 한다.
        /// </summary>
        private void Awake()
        {
            Instance = this;

            BuildUI();
            SetOpen(false);
        }

        /// <summary>
        /// **static 이벤트 누수 방지.** 이 컴포넌트가 다시 활성화되는 경로(씬 리로드 사이의 재사용 등)
        /// 에서도 구독이 정확히 한 번만 살아 있게 OnEnable에서 다시 걸고 OnDisable에서 뗀다.
        /// 실제 구독 시점은 대부분 창을 여는 순간이지만, 여기 없으면 "비활성 → 활성" 왕복에서 죽은
        /// 구독이 남는다.
        /// </summary>
        private void OnEnable()
        {
            SubscribeChest();
            SubscribeInventory();
        }

        private void OnDisable()
        {
            UnsubscribeChest();
            UnsubscribeInventory();
        }

        private void OnDestroy()
        {
            UnsubscribeChest();
            UnsubscribeInventory();

            if (tooltip != null)
                tooltip.Hide();

            if (Instance == this)
                Instance = null;
        }

        // ────────────────────────────────────────────────────────────────────────
        // 열기 / 닫기 (InteractionController가 부르는 진입점)
        // ────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// 지정한 상자의 보관 창을 연다. 이미 다른 상자가 열려 있으면 대상만 갈아 끼운다
        /// (창을 닫았다 다시 여는 것보다 눈에 덜 튀고, 칸 뷰도 그대로 재사용된다).
        /// </summary>
        public static void OpenFor(StorageChest target)
        {
            if (target == null)
                return;

            GetOrCreate().Open(target);
        }

        /// <summary>
        /// 창이 열려 있으면 닫고 true를 반환한다. InteractionController가 상호작용 키를 소비할지
        /// 판단하는 데 쓴다 - true를 돌려받은 프레임에는 월드 상호작용을 하지 않는다.
        /// </summary>
        public static bool CloseIfOpen()
        {
            if (!IsOpen)
                return false;

            Instance.SetOpen(false);
            return true;
        }

        /// <summary>상자를 대상으로 잡고 창을 연다.</summary>
        public void Open(StorageChest target)
        {
            if (target == null)
                return;

            if (chest != target)
            {
                UnsubscribeChest();
                chest = target;
                SubscribeChest();

                // 대상이 바뀌면 이전 상자의 스크롤 위치·선택 잔상이 남지 않게 맨 위로 되돌린다.
                if (chestPanel != null)
                    chestPanel.ResetScroll();
            }

            EnsureInventory();
            EnsureInteraction();

            ClearMessage();
            SetOpen(true);
        }

        /// <summary>
        /// 창을 열거나 닫는다. **timeScale을 건드리지 않는다**(인벤토리·제작 창과 같은 정책).
        /// 커서도 직접 만지지 않는다 - 창 루트가 켜지면 그 안의 UIDragHandle이 활성 상태가 되고,
        /// CursorLockController가 그것을 보고 커서를 푼다. 닫으면 반대로 다시 잠긴다.
        /// </summary>
        public void SetOpen(bool open)
        {
            if (panelRoot == null)
                return;

            panelRoot.SetActive(open);

            if (!open)
            {
                hoverPanel = null;
                hoverIndex = -1;
                HideTooltip();
                return;
            }

            // 옮겨둔 자리를 복원하고, 해상도가 바뀌었을 경우를 대비해 화면 안으로 다시 맞춘다.
            windowRt.anchoredPosition = hasSavedWindowPosition ? savedWindowPosition : DefaultWindowPosition();
            if (dragHandle != null)
                dragHandle.ClampNow();

            refreshTimer = RefreshInterval;
            RefreshAll();
        }

        /// <summary>
        /// 처음 열 때의 기본 자리: 화면 한가운데(창이 848px로 넓어 좌우 어느 쪽에도 붙일 수 없다).
        /// 피벗이 (0.5, 1)이라 y는 창의 위쪽 모서리다 - 높이의 절반만큼 올리면 세로 중앙에 온다.
        /// </summary>
        private static Vector2 DefaultWindowPosition()
        {
            return new Vector2(0f, WindowHeight * 0.5f);
        }

        // ────────────────────────────────────────────────────────────────────────
        // 구독
        // ────────────────────────────────────────────────────────────────────────

        /// <summary>상자 내용 변화 신호를 구독한다. 이미 걸려 있으면 아무 일도 하지 않는다(이중 구독 금지).</summary>
        private void SubscribeChest()
        {
            if (chest == null || chestSubscribed)
                return;

            chest.Changed += OnChestChanged;
            chestSubscribed = true;
        }

        private void UnsubscribeChest()
        {
            if (chest == null || !chestSubscribed)
            {
                chestSubscribed = false;
                return;
            }

            chest.Changed -= OnChestChanged;
            chestSubscribed = false;
        }

        private void SubscribeInventory()
        {
            if (inventory == null || inventorySubscribed)
                return;

            inventory.InventoryChanged += OnInventoryChanged;
            inventorySubscribed = true;
        }

        private void UnsubscribeInventory()
        {
            if (inventory == null || !inventorySubscribed)
            {
                inventorySubscribed = false;
                return;
            }

            inventory.InventoryChanged -= OnInventoryChanged;
            inventorySubscribed = false;
        }

        /// <summary>플레이어 인벤토리 참조를 확보하고 구독을 건다(창을 처음 열 때 한 번이면 충분하다).</summary>
        private void EnsureInventory()
        {
            if (inventory != null)
                return;

            inventory = FindAnyObjectByType<PlayerInventory>();
            SubscribeInventory();
        }

        /// <summary>상호작용 컨트롤러에서 실제 키와 플레이어 위치를 읽어 온다(키 문자열을 박아두지 않는다).</summary>
        private void EnsureInteraction()
        {
            if (interaction == null)
                interaction = FindAnyObjectByType<InteractionController>();

            if (interaction != null)
                interactKey = interaction.interactKey;
        }

        /// <summary>상자 내용/등급이 바뀐 순간 다시 그린다(매 프레임 폴링하지 않는 이유가 이것이다).</summary>
        private void OnChestChanged()
        {
            if (!IsWindowOpen)
                return;

            RefreshChestSide();
            UpdateUpgradeRow();
        }

        private void OnInventoryChanged()
        {
            if (!IsWindowOpen)
                return;

            RefreshPlayerSide();
            UpdateUpgradeRow();
        }

        // ────────────────────────────────────────────────────────────────────────
        // 매 프레임
        // ────────────────────────────────────────────────────────────────────────

        private void Update()
        {
            if (!IsWindowOpen)
                return;

            // 상자가 사라졌거나(철거·씬 정리) 플레이어가 멀어지면 창을 붙들고 있을 이유가 없다.
            if (chest == null || IsPlayerTooFar())
            {
                SetOpen(false);
                return;
            }

            // timeScale 0 화면 위에서도 멈추지 않도록 unscaled로 센다(프로젝트 공통 규칙).
            refreshTimer -= Time.unscaledDeltaTime;
            if (refreshTimer <= 0f)
            {
                refreshTimer = RefreshInterval;

                // 폴링은 **소지품 쪽만** 한다. 상자 쪽은 Changed 이벤트로 충분하다(위 주석 참고).
                RefreshPlayerSide();
                UpdateHeader();
                UpdateUpgradeRow();
                UpdateHoveredTooltip();
            }

            if (messageUntil > 0f && Time.unscaledTime > messageUntil)
                ClearMessage();
        }

        /// <summary>플레이어가 상자에서 너무 멀어졌는지. 상호작용 거리 + 여유를 넘으면 참이다.</summary>
        private bool IsPlayerTooFar()
        {
            if (interaction == null || chest == null)
                return false;

            float limit = interaction.interactionDistance + AutoCloseSlack;
            return (interaction.transform.position - chest.transform.position).sqrMagnitude > limit * limit;
        }

        // ────────────────────────────────────────────────────────────────────────
        // 생성
        // ────────────────────────────────────────────────────────────────────────

        /// <summary>캔버스 · 창 · 두 격자 · 업그레이드 줄 · 안내줄을 만든다.</summary>
        private void BuildUI()
        {
            // 인벤토리/제작 창과 같은 층(10). 툴팁(13)·설정(16)·게임오버(20)보다는 아래여야 한다.
            var canvas = UIBuilder.CreateCanvas("ChestCanvas", sortOrder: 10);
            canvasRect = canvas.GetComponent<RectTransform>();

            windowRt = UIBuilder.CreateWindow(canvas.transform, "ChestWindow", WindowWidth, WindowHeight);
            panelRoot = windowRt.gameObject;

            var titleBar = UIBuilder.CreateTitleBar(windowRt, "보관 상자", TitleBarHeight);
            titleLabel = titleBar.GetComponentInChildren<Text>();
            UIBuilder.CreateCloseButton(titleBar, () => SetOpen(false));

            // 제목 표시줄이 곧 드래그 손잡이이자, CursorLockController가 "창이 열렸다"를 판정하는 근거다.
            dragHandle = UIBuilder.AttachDragHandle(titleBar, windowRt, canvasRect, TitleBarHeight);
            dragHandle.onMoved = position =>
            {
                savedWindowPosition = position;
                hasSavedWindowPosition = true;
            };

            headerLabel = CreateRow("Header", HeaderTop, HeaderHeight, WindowWidth - WindowPadding * 2f, 14, NeutralGray);
            chestCaption = CreateColumnCaption("ChestCaption", WindowPadding, "상자");
            playerCaption = CreateColumnCaption("PlayerCaption", WindowPadding + PanelWidth + PanelGap, "내 소지품");

            chestPanel = new VirtualSlotPanel();
            chestPanel.Build(this, windowRt, "ChestGrid", WindowPadding, -GridTop, PanelWidth, ViewportHeight);

            playerPanel = new VirtualSlotPanel();
            playerPanel.Build(this, windowRt, "PlayerGrid", WindowPadding + PanelWidth + PanelGap, -GridTop, PanelWidth, ViewportHeight);

            BuildUpgradeRow();

            messageLabel = CreateRow("Message", MessageTop, MessageHeight, WindowWidth - WindowPadding * 2f, 12, SunstrokeGold);
            hintLabel = CreateRow("Hint", HintTop, HintHeight, WindowWidth - WindowPadding * 2f, 11, DimGray);

            tooltip = ItemTooltipUI.GetOrCreate();
        }

        /// <summary>창 폭을 가득 채우는 한 줄짜리 글자를 만든다(머리글/메시지/안내줄 공통).</summary>
        private Text CreateRow(string name, float top, float height, float width, int fontSize, Color color)
        {
            var text = UIBuilder.CreateText(windowRt, name, "", fontSize, color, TextAnchor.MiddleLeft);
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;

            var rt = text.rectTransform;
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.sizeDelta = new Vector2(width, height);
            rt.anchoredPosition = new Vector2(WindowPadding, -top);
            return text;
        }

        /// <summary>격자 위에 붙는 열 제목(상자 / 내 소지품).</summary>
        private Text CreateColumnCaption(string name, float x, string label)
        {
            var text = UIBuilder.CreateText(windowRt, name, label, 12, DimGray, TextAnchor.MiddleLeft);
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;

            var rt = text.rectTransform;
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.sizeDelta = new Vector2(PanelWidth, CaptionHeight);
            rt.anchoredPosition = new Vector2(x, -CaptionTop);
            return text;
        }

        /// <summary>업그레이드 버튼 + 재료표 한 줄.</summary>
        private void BuildUpgradeRow()
        {
            upgradeButton = UIBuilder.CreateButton(windowRt, "Upgrade", "등급 올리기", OnUpgradeClicked);
            var rt = upgradeButton.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.sizeDelta = new Vector2(UpgradeButtonWidth, UpgradeRowHeight);
            rt.anchoredPosition = new Vector2(WindowPadding, -UpgradeTop);
            upgradeButtonLabel = upgradeButton.GetComponentInChildren<Text>();

            upgradeCostLabel = UIBuilder.CreateText(windowRt, "UpgradeCost", "", 12, NeutralGray, TextAnchor.MiddleLeft);
            upgradeCostLabel.raycastTarget = false;
            upgradeCostLabel.horizontalOverflow = HorizontalWrapMode.Overflow;
            upgradeCostLabel.supportRichText = true; // 모자란 재료만 빨갛게 칠하기 위해(줄을 쪼개지 않는다)

            var costRt = upgradeCostLabel.rectTransform;
            costRt.anchorMin = new Vector2(0f, 1f);
            costRt.anchorMax = new Vector2(0f, 1f);
            costRt.pivot = new Vector2(0f, 1f);
            costRt.sizeDelta = new Vector2(WindowWidth - WindowPadding * 2f - UpgradeButtonWidth - 10f, UpgradeRowHeight);
            costRt.anchoredPosition = new Vector2(WindowPadding + UpgradeButtonWidth + 10f, -UpgradeTop);
        }

        // ────────────────────────────────────────────────────────────────────────
        // 갱신
        // ────────────────────────────────────────────────────────────────────────

        private void RefreshAll()
        {
            RefreshChestSide();
            RefreshPlayerSide();
            UpdateHeader();
            UpdateUpgradeRow();
            UpdateHint();
        }

        /// <summary>
        /// 상자 쪽 격자를 다시 그린다. 등급이 올라 칸 수가 바뀌어도 **칸 뷰를 새로 만들지 않고**
        /// 콘텐츠 높이와 스크롤 범위만 바꾼다(VirtualSlotPanel.SetCapacity).
        /// </summary>
        private void RefreshChestSide()
        {
            if (chestPanel == null || chest == null)
                return;

            chest.GetStacks(chestPanel.Buffer);
            chestPanel.SetCapacity(chest.SlotCapacity);
            chestPanel.Rebind(true);
        }

        private void RefreshPlayerSide()
        {
            if (playerPanel == null)
                return;

            EnsureInventory();
            if (inventory == null)
                return;

            inventory.GetStacks(playerPanel.Buffer);
            playerPanel.SetCapacity(inventory.SlotCapacity);
            playerPanel.Rebind(true);

            if (playerCaption != null)
            {
                int used = playerPanel.Buffer.Count;
                int capacity = inventory.SlotCapacity;
                playerCaption.text = $"내 소지품 {used}/{capacity}칸";
                playerCaption.color = used >= capacity ? DangerRed : DimGray;
            }
        }

        /// <summary>등급 이름 + 사용 칸 수. 실제로 값이 바뀐 갱신에서만 문자열을 다시 만든다.</summary>
        private void UpdateHeader()
        {
            if (headerLabel == null || chest == null)
                return;

            int used = chest.UsedSlots;
            int capacity = chest.SlotCapacity;
            string tier = chest.TierDisplayName;

            if (used == lastHeaderUsed && capacity == lastHeaderCapacity && tier == lastHeaderTier)
                return;

            lastHeaderUsed = used;
            lastHeaderCapacity = capacity;
            lastHeaderTier = tier;

            headerLabel.text = $"{tier}   ·   칸 {used}/{capacity}" + (used >= capacity ? "  (가득 참)" : "");
            headerLabel.color = used >= capacity ? DangerRed
                : (capacity > 0 && (float)used / capacity >= 0.8f ? SunstrokeGold : NeutralGray);

            if (titleLabel != null)
                titleLabel.text = $"보관 상자 - {tier}";

            if (chestCaption != null)
                chestCaption.text = $"상자 {capacity}칸";
        }

        /// <summary>
        /// 업그레이드 버튼 상태와 재료표를 맞춘다. 버튼은 <c>CanUpgrade</c>가 true일 때만 눌린다
        /// (UIBuilder.CreateButton이 targetGraphic을 채워 두므로 비활성 틴트가 실제로 보인다).
        /// 재료 보유량은 **플레이어 소지품 기준**이다 - 건축/쉼터 승급이 전부 그 기준이고, 상자 안의
        /// 물건까지 세면 화면과 실제 판정(TryUpgrade)이 갈릴 수 있다.
        /// </summary>
        private void UpdateUpgradeRow()
        {
            if (upgradeButton == null || chest == null)
                return;

            bool canUpgrade = chest.CanUpgrade;
            IReadOnlyList<BuildPieceCost> cost = chest.UpgradeCost;

            string costText = BuildCostText(cost, canUpgrade);
            if (costText == lastCostText && canUpgrade == lastCanUpgrade)
                return;

            lastCostText = costText;
            lastCanUpgrade = canUpgrade;

            upgradeButton.interactable = canUpgrade;
            upgradeCostLabel.text = costText;

            if (upgradeButtonLabel != null)
            {
                // 재료가 모자라 CanUpgrade가 false인 것과 **더 올릴 등급이 없는** 것은 다른 상태다.
                // 재료표가 남아 있으면 "최고 등급"이라고 적지 않는다(거짓말이 되고, 옆의 빨간 재료줄과 어긋난다).
                bool hasNextTier = canUpgrade || (cost != null && cost.Count > 0);
                upgradeButtonLabel.text = hasNextTier ? "등급 올리기" : "최고 등급";
                upgradeButtonLabel.color = canUpgrade ? Color.white : DimGray;
            }
        }

        /// <summary>
        /// "나뭇가지 4/10 · 대나무 6/6" 형태의 재료 줄을 만든다. 모자란 항목만 Danger Red로 칠하고
        /// 부족한 개수까지 적는다 - "4/10"만으로는 어느 쪽이 보유인지 읽는 사람마다 갈린다.
        /// </summary>
        private string BuildCostText(IReadOnlyList<BuildPieceCost> cost, bool canUpgrade)
        {
            if (!canUpgrade && (cost == null || cost.Count == 0))
                return "더 올릴 등급이 없다";

            if (cost == null || cost.Count == 0)
                return "재료 없이 올릴 수 있다";

            costBuilder.Length = 0;
            costBuilder.Append("재료: ");

            for (int i = 0; i < cost.Count; i++)
            {
                BuildPieceCost entry = cost[i];
                if (string.IsNullOrEmpty(entry.itemName))
                    continue;

                int have = CountOwned(entry.itemName);
                bool enough = have >= entry.count;

                if (i > 0)
                    costBuilder.Append("  ·  ");

                costBuilder.Append("<color=#").Append(enough ? HexOk : HexShort).Append('>');
                costBuilder.Append(entry.itemName).Append(' ').Append(have).Append('/').Append(entry.count);
                if (!enough)
                    costBuilder.Append(" (").Append(entry.count - have).Append("개 부족)");
                costBuilder.Append("</color>");
            }

            return costBuilder.ToString();
        }

        /// <summary>
        /// 플레이어가 그 이름의 재료를 몇 개 가지고 있는지 센다. BuildPieceCost.itemName은
        /// ItemData.itemName과 문자 그대로 대조된다(Shelter.CountByName과 완전히 같은 규칙).
        /// </summary>
        private int CountOwned(string itemName)
        {
            if (inventory == null || string.IsNullOrEmpty(itemName))
                return 0;

            int count = 0;
            var items = inventory.items;
            for (int i = 0; i < items.Count; i++)
            {
                var item = items[i];
                if (item != null && item.data != null && item.data.itemName == itemName)
                    count++;
            }

            return count;
        }

        private void UpdateHint()
        {
            if (hintLabel == null)
                return;

            hintLabel.text = $"좌클릭 1개 옮기기 · 우클릭/Shift+좌클릭 한 칸 전부 · 제목 표시줄을 끌어 창 이동 · [{interactKey}] 닫기";
            hintLabel.color = DimGray;
        }

        // ────────────────────────────────────────────────────────────────────────
        // 조작
        // ────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// 칸을 클릭했을 때 물건을 옮긴다. 어느 쪽 격자인지에 따라 방향이 정해진다:
        /// 상자 칸 → 꺼내기(Withdraw), 소지품 칸 → 넣기(TryDeposit).
        /// **인벤토리와 상자 양쪽의 실제 이동은 전부 StorageChest가 한다** - UI는 개수만 정해 넘기고
        /// 결과(성공 여부 / 실제로 옮긴 개수)를 화면에 옮겨 적을 뿐이다.
        /// </summary>
        private void OnSlotActivated(VirtualSlotPanel panel, int index, bool whole)
        {
            if (chest == null)
                return;

            InventoryStack stack = panel.GetStack(index);
            if (stack == null || stack.data == null)
                return;

            int want = whole ? Mathf.Max(1, stack.count) : 1;

            if (panel == chestPanel)
                WithdrawFromChest(stack.data, want);
            else
                DepositToChest(stack.data, want);
        }

        /// <summary>상자 → 플레이어. 실제로 옮겨진 개수가 요청보다 적으면 그 사실을 그대로 알린다.</summary>
        private void WithdrawFromChest(ItemData data, int want)
        {
            int moved = chest.Withdraw(data, want);

            if (moved <= 0)
            {
                ShowMessage($"소지품 칸이 모자라 {data.itemName}을(를) 꺼내지 못했다", DangerRed);
                AudioManager.Instance?.PlayActionFail();
                return;
            }

            if (moved < want)
                ShowMessage($"{data.itemName} {moved}개만 꺼냈다 - 소지품 칸이 모자라다", SunstrokeGold);
            else
                ClearMessage();

            AudioManager.Instance?.PlayPickup();
        }

        /// <summary>플레이어 → 상자.</summary>
        private void DepositToChest(ItemData data, int want)
        {
            // 미리 물어볼 수 있는 것은 미리 물어본다(CanAccept는 상태를 바꾸지 않는다). 한 칸 전부가
            // 들어가지 않는 상황과 아예 못 넣는 상황을 다른 문구로 갈라 주기 위한 것이다.
            if (!chest.CanAccept(data, want))
            {
                bool anyRoom = want > 1 && chest.CanAccept(data, 1);
                ShowMessage(anyRoom
                    ? $"상자에 {data.itemName} {want}개가 다 들어가지 않는다 - 좌클릭으로 1개씩 넣어라"
                    : $"상자가 가득 차 {data.itemName}을(를) 넣지 못했다", DangerRed);
                AudioManager.Instance?.PlayActionFail();
                return;
            }

            if (!chest.TryDeposit(data, want))
            {
                ShowMessage($"{data.itemName}을(를) 상자에 넣지 못했다", DangerRed);
                AudioManager.Instance?.PlayActionFail();
                return;
            }

            ClearMessage();
            AudioManager.Instance?.PlayPickup();
        }

        /// <summary>업그레이드 버튼. 실패하면 상자가 돌려준 사유를 그대로 창에 띄운다(문구를 지어내지 않는다).</summary>
        private void OnUpgradeClicked()
        {
            if (chest == null)
                return;

            if (chest.TryUpgrade(out string failReason))
            {
                ShowMessage($"{chest.TierDisplayName}(으)로 올렸다 - {chest.SlotCapacity}칸", MedicGreen);
                AudioManager.Instance?.PlayCraftSuccess();

                // 칸 수가 바뀌었으니 곧바로 반영한다(Changed도 오지만 순서를 기다리지 않는다).
                RefreshChestSide();
                UpdateHeader();
                UpdateUpgradeRow();
                return;
            }

            ShowMessage(string.IsNullOrEmpty(failReason) ? "지금은 등급을 올릴 수 없다" : failReason, DangerRed);
            AudioManager.Instance?.PlayActionFail();
        }

        private void ShowMessage(string text, Color color)
        {
            if (messageLabel == null)
                return;

            messageLabel.text = text;
            messageLabel.color = color;
            messageUntil = Time.unscaledTime + MessageDuration;
        }

        private void ClearMessage()
        {
            if (messageLabel != null)
                messageLabel.text = "";

            messageUntil = -1f;
        }

        // ────────────────────────────────────────────────────────────────────────
        // hover / 툴팁
        // ────────────────────────────────────────────────────────────────────────

        private void OnSlotEnter(VirtualSlotPanel panel, int index)
        {
            hoverPanel = panel;
            hoverIndex = index;

            chestPanel.RefreshColors();
            playerPanel.RefreshColors();
            ShowTooltipFor(panel, index);
        }

        private void OnSlotExit(VirtualSlotPanel panel, int index)
        {
            if (hoverPanel != panel || hoverIndex != index)
                return;

            hoverPanel = null;
            hoverIndex = -1;

            chestPanel.RefreshColors();
            playerPanel.RefreshColors();
            HideTooltip();
        }

        /// <summary>커서가 얹혀 있는 칸의 내용이 바뀌었을 수 있으므로(개수 변화) 다시 채운다.</summary>
        private void UpdateHoveredTooltip()
        {
            if (hoverPanel != null && hoverIndex >= 0)
                ShowTooltipFor(hoverPanel, hoverIndex);
        }

        private void ShowTooltipFor(VirtualSlotPanel panel, int index)
        {
            if (tooltip == null)
                return;

            InventoryStack stack = panel != null ? panel.GetStack(index) : null;
            if (stack == null || stack.data == null)
            {
                HideTooltip();
                return;
            }

            // 대표 인스턴스가 없는 칸(상자가 개수만 보관하는 구현)은 남은 사용 횟수를 알 수 없다.
            // 0을 넘기면 툴팁에 "남은 사용 0/20"이 찍히므로, 그럴 때는 최대치를 넘겨 오해를 만들지 않는다.
            int remaining = stack.representative != null
                ? stack.RemainingUses
                : (stack.data != null ? stack.data.maxUses : 0);

            if (stack.data == lastTooltipData && stack.count == lastTooltipCount && remaining == lastTooltipRemaining)
                return;

            lastTooltipData = stack.data;
            lastTooltipCount = stack.count;
            lastTooltipRemaining = remaining;

            string action = panel == chestPanel ? "좌클릭 = 1개 꺼내기" : "좌클릭 = 1개 넣기";
            tooltip.Show(stack.data, stack.count, remaining, action, "우클릭 / Shift+좌클릭 = 한 칸 전부");
        }

        private void HideTooltip()
        {
            lastTooltipData = null;
            lastTooltipCount = -1;
            lastTooltipRemaining = int.MinValue;

            if (tooltip != null)
                tooltip.Hide();
        }

        /// <summary>"한 칸 전부" 수식 키. 인벤토리 창의 버리기 수식 키와 같은 규칙(좌우 Shift 모두 인정)이다.</summary>
        private static bool IsWholeStackModifierHeld()
        {
            return Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
        }

        // ────────────────────────────────────────────────────────────────────────
        // 가상화 격자
        // ────────────────────────────────────────────────────────────────────────

        /// <summary>칸 뷰 하나와 "지금 이 뷰가 무엇을 보여주고 있는지" 캐시.</summary>
        private class SlotBinding
        {
            public UIBuilder.SlotVisual visual;

            public int dataIndex = -1;
            public ItemData data;
            public int count = -1;
            public int remaining = int.MinValue;
            public bool shown;
        }

        /// <summary>
        /// **스크롤 + 칸 뷰 재사용(가상화) 격자.** 이 창이 200칸을 감당하는 방법이다.
        ///
        /// GridLayoutGroup으로 200칸을 깔면 칸 뷰 200개(자식까지 1200개가 넘는다)가 실제로 존재하게
        /// 되고, 등급을 올릴 때마다 50개씩 새로 만들면서 프레임이 튄다. 여기서는 콘텐츠(전체 격자)의
        /// **높이만** 칸 수에 맞춰 늘리고, 칸 뷰는 화면에 보이는 줄 + 여유 2줄만큼만 만들어 스크롤
        /// 위치에 따라 다른 인덱스로 다시 묶는다(재사용). 그래서:
        ///   · 만들어지는 칸 뷰 수 = 최대 (VisibleRows + SpareRows) × Columns = 48개로 고정.
        ///   · 50 → 200칸으로 올라가도 새로 만들어지는 오브젝트가 **0개**다.
        ///   · 스크롤은 위치 계산 + 내용 갱신뿐이라 오브젝트 생성/파괴가 전혀 없다.
        ///
        /// 배치는 GridLayoutGroup 없이 직접 한다 - 레이아웃 그룹은 자식 전부를 대상으로 재계산하므로
        /// 가상화와 같이 쓰면 이득이 사라진다.
        /// </summary>
        private class VirtualSlotPanel
        {
            private ChestUI owner;

            private RectTransform root;
            private RectTransform viewport;
            private RectTransform content;
            private ScrollRect scroll;

            private readonly List<SlotBinding> slots = new List<SlotBinding>();

            /// <summary>소유자가 GetStacks(buffer)로 직접 채우는 표시용 목록(리스트를 새로 만들지 않는다).</summary>
            private readonly List<InventoryStack> stacks = new List<InventoryStack>();

            private int capacity;
            private int firstRow = -1;
            private float viewportHeight;

            public List<InventoryStack> Buffer => stacks;

            /// <summary>이 격자가 만들 수 있는 칸 뷰의 절대 상한(=화면에 보이는 줄 + 여유 줄).</summary>
            private static int MaxPooledSlots => (VisibleRows + SpareRows) * Columns;

            /// <summary>스크롤 영역 · 콘텐츠 · 칸 뷰 풀을 만든다.</summary>
            public void Build(ChestUI ownerUI, Transform parent, string name, float x, float yTop, float width, float height)
            {
                owner = ownerUI;
                viewportHeight = height;

                var rootGo = new GameObject(name, typeof(RectTransform), typeof(ScrollRect));
                rootGo.transform.SetParent(parent, false);
                root = rootGo.GetComponent<RectTransform>();
                root.anchorMin = new Vector2(0f, 1f);
                root.anchorMax = new Vector2(0f, 1f);
                root.pivot = new Vector2(0f, 1f);
                root.sizeDelta = new Vector2(width, height);
                root.anchoredPosition = new Vector2(x, yTop);

                // 뷰포트: RectMask2D가 영역 밖으로 나간 칸을 잘라낸다(MinimapUI가 쓰는 것과 같은 방식).
                // 아주 옅은 배경을 깔아 두는 이유는 두 가지다 - (1) 격자 영역의 경계가 눈에 보이고,
                // (2) raycastTarget이 켜져 있어야 빈 자리에서 끌어 스크롤하는 조작이 먹는다.
                viewport = UIBuilder.CreatePanel(root, "Viewport",
                    anchorMin: Vector2.zero, anchorMax: Vector2.one,
                    offsetMin: Vector2.zero, offsetMax: Vector2.zero,
                    color: new Color(1f, 1f, 1f, 0.02f));
                viewport.gameObject.AddComponent<RectMask2D>();

                var contentGo = new GameObject("Content", typeof(RectTransform));
                contentGo.transform.SetParent(viewport, false);
                content = contentGo.GetComponent<RectTransform>();
                content.anchorMin = new Vector2(0f, 1f);
                content.anchorMax = new Vector2(1f, 1f);
                content.pivot = new Vector2(0.5f, 1f);
                content.anchoredPosition = Vector2.zero;
                content.sizeDelta = new Vector2(0f, height);

                scroll = rootGo.GetComponent<ScrollRect>();
                scroll.viewport = viewport;
                scroll.content = content;
                scroll.horizontal = false;
                scroll.vertical = true;
                scroll.movementType = ScrollRect.MovementType.Clamped;
                scroll.scrollSensitivity = RowStride; // 휠 한 칸 = 한 줄
                scroll.onValueChanged.AddListener(OnScrolled);
            }

            private void OnScrolled(Vector2 _)
            {
                Rebind(false);
            }

            /// <summary>스크롤을 맨 위로 되돌린다(다른 상자를 열었을 때).</summary>
            public void ResetScroll()
            {
                if (content == null)
                    return;

                content.anchoredPosition = new Vector2(content.anchoredPosition.x, 0f);
                firstRow = -1;
            }

            /// <summary>
            /// 칸 수를 바꾼다. **칸 뷰를 새로 만들지 않는다** - 콘텐츠 높이(=스크롤 가능 범위)만 바꾸고,
            /// 아직 풀에 없는 만큼만(그것도 48개를 넘지 않게) 칸 뷰를 채운다. 그래서 특대(200칸)로
            /// 올려도 이미 48개가 만들어져 있으면 생성 비용이 0이다.
            /// </summary>
            public void SetCapacity(int newCapacity)
            {
                newCapacity = Mathf.Max(0, newCapacity);

                EnsurePool(Mathf.Min(MaxPooledSlots, newCapacity));

                if (newCapacity == capacity)
                    return;

                capacity = newCapacity;

                int rows = Mathf.Max(1, Mathf.CeilToInt(capacity / (float)Columns));
                float contentHeight = Mathf.Max(viewportHeight, rows * SlotSize + (rows - 1) * SlotSpacing);
                content.sizeDelta = new Vector2(0f, contentHeight);

                // 칸이 줄어드는 경우(있을 수 없지만 방어) 스크롤이 빈 영역에 남지 않게 되돌린다.
                float maxScroll = Mathf.Max(0f, contentHeight - viewportHeight);
                if (content.anchoredPosition.y > maxScroll)
                    content.anchoredPosition = new Vector2(content.anchoredPosition.x, maxScroll);

                firstRow = -1; // 다음 Rebind가 반드시 전부 다시 묶게 한다
            }

            /// <summary>필요한 만큼만 칸 뷰를 만든다(한 번 만든 뷰는 파괴하지 않고 계속 재사용한다).</summary>
            private void EnsurePool(int wanted)
            {
                while (slots.Count < wanted)
                {
                    int poolIndex = slots.Count;

                    var binding = new SlotBinding();
                    binding.visual = UIBuilder.CreateItemSlot(content, $"Slot{poolIndex}", withDurabilityBar: true);

                    RectTransform rt = binding.visual.rect;
                    rt.anchorMin = new Vector2(0f, 1f);
                    rt.anchorMax = new Vector2(0f, 1f);
                    rt.pivot = new Vector2(0f, 1f);
                    rt.sizeDelta = new Vector2(SlotSize, SlotSize);

                    // 콜백은 만들 때 한 번만 연결한다. 어떤 칸인지는 Rebind가 index에 다시 써 넣는다
                    // (같은 뷰가 스크롤에 따라 다른 칸을 맡기 때문이다).
                    var input = binding.visual.input;
                    input.index = -1;
                    input.onEnter = OnSlotEnter;
                    input.onExit = OnSlotExit;
                    input.onLeftClick = OnSlotLeftClick;
                    input.onRightClick = OnSlotRightClick;

                    binding.shown = true;
                    slots.Add(binding);
                }
            }

            private void OnSlotEnter(int dataIndex)
            {
                owner.OnSlotEnter(this, dataIndex);
            }

            private void OnSlotExit(int dataIndex)
            {
                owner.OnSlotExit(this, dataIndex);
            }

            private void OnSlotLeftClick(int dataIndex)
            {
                owner.OnSlotActivated(this, dataIndex, IsWholeStackModifierHeld());
            }

            private void OnSlotRightClick(int dataIndex)
            {
                owner.OnSlotActivated(this, dataIndex, true);
            }

            /// <summary>표시 목록에서 그 칸이 담고 있는 스택을 얻는다(빈 칸이면 null).</summary>
            public InventoryStack GetStack(int dataIndex)
            {
                if (dataIndex < 0 || dataIndex >= stacks.Count)
                    return null;

                return stacks[dataIndex];
            }

            /// <summary>
            /// 지금 스크롤 위치에 맞춰 칸 뷰를 데이터에 다시 묶는다. force가 아니면 스크롤이 한 줄도
            /// 움직이지 않은 프레임에서는 아무 일도 하지 않는다(휠 한 번에 수십 번 불려도 안전하다).
            /// </summary>
            public void Rebind(bool force)
            {
                if (content == null || slots.Count == 0)
                    return;

                int totalRows = Mathf.Max(1, Mathf.CeilToInt(capacity / (float)Columns));
                int poolRows = Mathf.Max(1, slots.Count / Columns);
                int maxFirstRow = Mathf.Max(0, totalRows - poolRows);

                int newFirstRow = Mathf.Clamp(Mathf.FloorToInt(content.anchoredPosition.y / RowStride), 0, maxFirstRow);
                if (!force && newFirstRow == firstRow)
                    return;

                firstRow = newFirstRow;

                for (int i = 0; i < slots.Count; i++)
                {
                    SlotBinding binding = slots[i];

                    int row = firstRow + i / Columns;
                    int column = i % Columns;
                    int dataIndex = row * Columns + column;

                    if (dataIndex >= capacity)
                    {
                        Hide(binding);
                        continue;
                    }

                    if (!binding.shown)
                    {
                        binding.visual.go.SetActive(true);
                        binding.shown = true;
                    }

                    binding.visual.rect.anchoredPosition = new Vector2(column * RowStride, -row * RowStride);
                    binding.visual.input.index = dataIndex;

                    Apply(binding, dataIndex);
                }
            }

            /// <summary>hover 상태만 다시 칠한다(내용은 그대로라 문자열을 만들지 않는다).</summary>
            public void RefreshColors()
            {
                for (int i = 0; i < slots.Count; i++)
                {
                    SlotBinding binding = slots[i];
                    if (binding.shown)
                        ApplyColor(binding);
                }
            }

            private void Hide(SlotBinding binding)
            {
                if (!binding.shown)
                    return;

                binding.shown = false;
                binding.dataIndex = -1;
                binding.data = null;
                binding.count = -1;
                binding.remaining = int.MinValue;
                binding.visual.input.index = -1;
                binding.visual.go.SetActive(false);
            }

            /// <summary>칸 하나의 내용을 그린다. 지난번과 같은 칸·같은 내용이면 문자열을 다시 만들지 않는다.</summary>
            private void Apply(SlotBinding binding, int dataIndex)
            {
                InventoryStack stack = dataIndex < stacks.Count ? stacks[dataIndex] : null;
                ItemData data = stack != null ? stack.data : null;
                int count = stack != null ? stack.count : 0;

                // **대표 인스턴스가 없을 수 있다.** InventoryStack.RemainingUses는 대표가 null이면 0을
                // 돌려주는데(InventoryItem.cs), 상자가 개수만 세어 보관하는 구현이라면 모든 칸이 그렇다.
                // 그 0을 내구도로 믿으면 상자에 넣어둔 손도끼가 전부 "다 닳음(빨간 막대)"으로 보인다.
                // 그래서 대표가 없을 때는 int.MinValue = "모름"으로 두고 막대 자체를 그리지 않는다.
                int remaining = (stack != null && stack.representative != null) ? stack.RemainingUses : int.MinValue;

                if (binding.dataIndex == dataIndex && binding.data == data && binding.count == count && binding.remaining == remaining)
                {
                    ApplyColor(binding);
                    return;
                }

                binding.dataIndex = dataIndex;
                binding.data = data;
                binding.count = count;
                binding.remaining = remaining;

                UIBuilder.SlotVisual visual = binding.visual;

                if (data == null)
                {
                    visual.icon.enabled = false;
                    visual.icon.sprite = null;
                    visual.categoryStrip.color = Color.clear;
                    visual.letterLabel.gameObject.SetActive(false);
                    visual.countLabel.gameObject.SetActive(false);
                    if (visual.durabilityBarGo != null)
                        visual.durabilityBarGo.SetActive(false);

                    ApplyColor(binding);
                    return;
                }

                visual.categoryStrip.color = UIBuilder.GetItemCategoryColor(data);

                // 아이콘은 인벤토리 창과 **같은 경로**로 얻는다(ItemData.icon, 없으면 이름 첫 글자 폴백).
                visual.icon.enabled = true;
                if (data.icon != null)
                {
                    visual.icon.sprite = data.icon;
                    visual.icon.color = Color.white;
                    visual.letterLabel.gameObject.SetActive(false);
                }
                else
                {
                    visual.icon.sprite = null;
                    visual.icon.color = UIBuilder.GetItemCategoryColor(data);
                    visual.letterLabel.gameObject.SetActive(true);
                    visual.letterLabel.text = string.IsNullOrEmpty(data.itemName) ? "?" : data.itemName.Substring(0, 1);
                }

                // 개수 1은 찍지 않는다("x1"은 정보가 없고 아이콘만 가린다 - 격자 UI의 표준).
                if (count > 1)
                {
                    visual.countLabel.gameObject.SetActive(true);
                    visual.countLabel.text = count.ToString();
                    visual.countLabel.color = count >= data.MaxStackSize ? SunstrokeGold : Color.white;
                }
                else
                {
                    visual.countLabel.gameObject.SetActive(false);
                }

                bool durableTool = !data.IsStackable && !data.IsUnlimited && data.maxUses > 1 && remaining != int.MinValue;
                if (visual.durabilityBarGo != null)
                {
                    if (durableTool)
                    {
                        float ratio = Mathf.Clamp01((float)remaining / data.maxUses);
                        visual.durabilityBarGo.SetActive(true);
                        visual.durabilityFill.fillAmount = ratio;
                        visual.durabilityFill.color = ratio <= 0.2f ? DangerRed : ratio <= 0.4f ? SunstrokeGold : MedicGreen;
                    }
                    else
                    {
                        visual.durabilityBarGo.SetActive(false);
                    }
                }

                ApplyColor(binding);
            }

            /// <summary>칸 배경색(빈칸/채움/hover). 색이 실제로 달라질 때만 Image에 쓴다.</summary>
            private void ApplyColor(SlotBinding binding)
            {
                bool hovered = owner.hoverPanel == this && owner.hoverIndex == binding.dataIndex && binding.dataIndex >= 0;
                Color target = hovered ? UIBuilder.SlotHoverColor
                    : (binding.data != null ? UIBuilder.SlotFilledColor : UIBuilder.SlotEmptyColor);

                if (binding.visual.background.color != target)
                    binding.visual.background.color = target;
            }
        }
    }
}
