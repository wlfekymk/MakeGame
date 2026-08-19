using System.Text;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using MakeGame.Player;
using MakeGame.Systems;

namespace MakeGame.UI
{
    /// <summary>
    /// 해안 뗏목 제작 창. 뗏목(또는 바닥판 0칸일 때의 "제작 예정지")을 조준하고 E를 누르면 열린다.
    /// 목록은 바닥판 3종 · 갑판 바닥재 · 노 · 돛 · 키 · 닻 · 모터의 **9줄 고정**이고, 줄마다 필요한
    /// 재료와 지금 들고 있는 수량을 함께 보여준다.
    ///
    /// **판정은 이 창이 하지 않는다.** 무엇을 얼마에 만들 수 있는지 · 지금 만들 수 있는지 · 실제 제작과
    /// 재료 소모는 전부 <see cref="RaftBuildCatalog"/>(Systems)가 정하고, 이 파일은 그 값을 문장과 색으로
    /// 옮기고 클릭·숫자키를 넘기기만 한다. 이 프로젝트에서 반복된 사고가 "UI가 같은 판정을 다시 구현해
    /// 화면과 실제 동작이 조용히 갈라지는" 것이라, 제작표를 UI 쪽에 복사해 두지 말 것.
    ///
    /// 이 프로젝트의 창 규칙을 그대로 따른다(ChestUI 클래스 주석과 같은 목록):
    /// · 프리팹 없이 100% 코드 생성. 창/제목 표시줄/닫기 버튼/버튼은 전부 <see cref="UIBuilder"/> 팩토리.
    /// · 캔버스 sortOrder는 10 - 인벤토리·제작·상자·퀘스트 창과 같은 층이다(툴팁 13 · 설정 16 · 사망 20 아래).
    /// · <see cref="Time.timeScale"/>을 건드리지 않는다. "플레이 중 창"이라 게임은 계속 돈다.
    ///   그래도 이 파일의 타이머는 전부 unscaled로 센다(프로젝트 공통 규칙).
    /// · <see cref="Cursor"/>를 직접 만지지 않는다. 제목 표시줄에 <see cref="UIDragHandle"/>을 붙이고
    ///   닫을 때 창 루트를 SetActive(false) 하는 것만으로 <see cref="CursorLockController"/>가 알아서
    ///   커서를 풀고 다시 잠근다. 커서가 풀리면 시야 회전이 멈추고 이동은 그대로 되는데, 이는
    ///   인벤토리·제작·상자 창이 열렸을 때와 **완전히 같은 상태**다(PlayerController.HandleLook).
    /// · 씬에 인스턴스가 없다. 씬 로드마다 스스로 생성되므로 **코드 기본값이 유일한 진실**이다.
    /// · OnGUI(IMGUI)는 절대 쓰지 않는다 - sortingOrder를 무시하고 다른 화면을 통째로 덮는다.
    ///
    /// [프레임당 할당] 닫혀 있으면 Update가 첫 줄에서 돌아간다. 열려 있어도 문자열은 refreshInterval
    /// (0.2초)마다, 그것도 **표시 내용이 실제로 달라진 줄만** 다시 만든다.
    /// </summary>
    public class RaftBuildUI : MonoBehaviour
    {
        /// <summary>이 씬의 뗏목 제작 창(씬 리로드마다 새 인스턴스로 교체된다).</summary>
        public static RaftBuildUI Instance { get; private set; }

        // ── 치수 ────────────────────────────────────────────────────────────────
        // 제목줄 높이·좌우 여백은 공용 골격(UIBuilder.CreateSkinnedWindow)이 정한다.
        // 여기 값은 전부 **본문 안쪽** 좌표다 - 본문의 (0,0)이 곧 목록 첫 줄의 왼쪽 위다.
        private const float RowHeight = 38f;
        private const float RowSpacing = 4f;
        private const float MessageHeight = 18f;
        private const float HintHeight = 16f;

        private const float BodyWidth = 532f;

        private const float NameWidth = 190f;
        private const float CostWidth = 250f;
        private const float ButtonWidth = 76f;
        private const float ButtonHeight = 26f;

        /// <summary>줄 수는 제작표가 정한다(카탈로그에 항목을 추가하면 창이 저절로 길어진다).</summary>
        private static readonly int RowCount = RaftBuildCatalog.Order.Length;

        private static readonly float RowsHeight = RowCount * RowHeight + (RowCount - 1) * RowSpacing;
        private static readonly float MessageTop = RowsHeight + 8f;
        private static readonly float HintTop = MessageTop + MessageHeight + 4f;
        private static readonly float BodyHeight = HintTop + HintHeight;

        /// <summary>바깥에서 쓰는 창 전체 높이(기본 자리 계산용). 골격이 더하는 여백을 포함한다.</summary>
        private static readonly float WindowHeight = BodyHeight + UITheme.ChromeTop + UITheme.ChromeBottom;

        // ── 색 (ArtDirection 팔레트 안에서만 - 새 색을 만들지 않는다) ────────────
        private static readonly Color SunstrokeGold = new Color(0.902f, 0.749f, 0.2f, 1f);
        private static readonly Color RowBackground = new Color(1f, 1f, 1f, 0.05f);
        private static readonly Color RowBackgroundBlocked = new Color(1f, 1f, 1f, 0.02f);

        // 재료 줄 rich text 색(위 Color와 같은 값). 충분하면 회색, 모자라면 Danger Red.
        private const string HexOk = "CCCCCC";
        private const string HexShort = "CC3333";

        /// <summary>실패/안내 문구를 띄워 두는 시간(초).</summary>
        private const float MessageDuration = 3.5f;

        /// <summary>표시 갱신 주기(초, unscaled). 열려 있을 때만 돈다.</summary>
        private const float RefreshInterval = 0.2f;

        /// <summary>플레이어가 이만큼 멀어지면 창이 저절로 닫힌다(상호작용 거리에 더하는 여유, m).</summary>
        private const float AutoCloseSlack = 3f;

        /// <summary>
        /// 창 위치를 세션 동안 기억한다. static인 이유는 다른 창들과 같다 - 이 컴포넌트는 씬 로드마다
        /// 통째로 새로 생성되므로 인스턴스 필드에 두면 씬을 다시 불러올 때마다 처음 자리로 돌아간다.
        /// </summary>
        private static bool hasSavedWindowPosition;
        private static Vector2 savedWindowPosition;

        /// <summary>목록 한 줄이 들고 있는 화면 부품 + 마지막으로 그린 내용(다시 만들지 판단하는 캐시).</summary>
        private class BuildRow
        {
            public RaftBuildEntry entry;
            public Image background;
            public Text nameLabel;
            public Text descLabel;
            public Text costLabel;
            public Button button;
            public Text buttonLabel;

            public string shownCost;
            public string shownDesc;
            public bool shownInteractable = true;
            public bool shownBlocked;
        }

        private readonly BuildRow[] rows = new BuildRow[RaftBuildCatalog.Order.Length];

        /// <summary>재료 줄 문자열 조립용. 갱신마다 새 StringBuilder를 만들지 않는다.</summary>
        private readonly StringBuilder costBuilder = new StringBuilder(160);

        // ── 대상 ────────────────────────────────────────────────────────────────
        private RaftStructure raft;
        private bool raftSubscribed;

        private PlayerInventory inventory;
        private bool inventorySubscribed;

        private InteractionController interaction;
        private KeyCode interactKey = KeyCode.E;

        // ── 화면 부품 ───────────────────────────────────────────────────────────
        private RectTransform canvasRect;
        private RectTransform windowRt;
        private RectTransform bodyRt;
        private GameObject panelRoot;
        private UIDragHandle dragHandle;
        private Text headerLabel;
        private Text messageLabel;

        private float refreshTimer;
        private float messageUntil = -1f;
        private string shownHeader;

        /// <summary>지금 창이 열려 있는지.</summary>
        public bool IsWindowOpen => panelRoot != null && panelRoot.activeSelf;

        /// <summary>지금 이 씬에서 뗏목 제작 창이 열려 있는지(다른 시스템이 입력을 양보할 때 쓴다).</summary>
        public static bool IsOpen => Instance != null && Instance.IsWindowOpen;

        // ────────────────────────────────────────────────────────────────────────
        // 부트스트랩 / 수명
        // ────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// 씬이 로드될 때마다 스스로 생성된다(ChestUI·QuestUI와 같은 자기 완결 패턴).
        /// 씬 파일을 고칠 수 없으므로 이 방식이 아니면 이 창은 존재할 수 없다.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Bootstrap()
        {
            SceneManager.sceneLoaded += (scene, mode) =>
            {
                // 중복 생성 방지: 창이 두 개면 같은 뗏목에 두 창이 붙어 서로 다른 내용을 그린다.
                if (FindAnyObjectByType<RaftBuildUI>() != null)
                    return;

                var go = new GameObject("RaftBuildUI");
                go.AddComponent<RaftBuildUI>();
            };
        }

        /// <summary>
        /// 인스턴스를 가져온다. 없으면(부트스트랩보다 먼저 상호작용이 일어난 경우) 그 자리에서 만든다.
        /// ChestUI.GetOrCreate와 같은 안전장치다.
        /// </summary>
        public static RaftBuildUI GetOrCreate()
        {
            if (Instance != null)
                return Instance;

            var existing = FindAnyObjectByType<RaftBuildUI>();
            if (existing != null)
                return existing;

            var go = new GameObject("RaftBuildUI");
            return go.AddComponent<RaftBuildUI>();
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

        private void OnEnable()
        {
            SubscribeRaft();
            SubscribeInventory();
        }

        private void OnDisable()
        {
            UnsubscribeRaft();
            UnsubscribeInventory();
        }

        private void OnDestroy()
        {
            UnsubscribeRaft();
            UnsubscribeInventory();

            if (Instance == this)
                Instance = null;
        }

        // ────────────────────────────────────────────────────────────────────────
        // 열기 / 닫기 (InteractionController가 부르는 진입점)
        // ────────────────────────────────────────────────────────────────────────

        /// <summary>지정한 뗏목의 제작 창을 연다. 대상이 바뀌면 갈아 끼우기만 한다.</summary>
        public static void OpenFor(RaftStructure target)
        {
            if (target == null)
                return;

            GetOrCreate().Open(target);
        }

        /// <summary>
        /// 창이 열려 있으면 닫고 true를 반환한다. InteractionController가 상호작용 키를 소비할지
        /// 판단하는 데 쓴다 - true를 돌려받은 프레임에는 월드 상호작용을 하지 않는다(ChestUI와 같은 규약).
        /// </summary>
        public static bool CloseIfOpen()
        {
            if (!IsOpen)
                return false;

            Instance.SetOpen(false);
            return true;
        }

        /// <summary>뗏목을 대상으로 잡고 창을 연다.</summary>
        public void Open(RaftStructure target)
        {
            if (target == null)
                return;

            if (raft != target)
            {
                UnsubscribeRaft();
                raft = target;
                SubscribeRaft();
            }

            EnsureInventory();
            EnsureInteraction();

            ClearMessage();
            SetOpen(true);
        }

        /// <summary>
        /// 창을 열거나 닫는다. **timeScale을 건드리지 않고 커서도 직접 만지지 않는다** - 창 루트가
        /// 켜지면 그 안의 UIDragHandle이 활성 상태가 되고 CursorLockController가 커서를 푼다.
        /// </summary>
        public void SetOpen(bool open)
        {
            if (panelRoot == null)
                return;

            panelRoot.SetActive(open);

            if (!open)
                return;

            // 옮겨둔 자리를 복원하고, 해상도가 바뀌었을 경우를 대비해 화면 안으로 다시 맞춘다.
            windowRt.anchoredPosition = hasSavedWindowPosition ? savedWindowPosition : DefaultWindowPosition();
            if (dragHandle != null)
                dragHandle.ClampNow();

            refreshTimer = RefreshInterval;
            RefreshAll();
        }

        /// <summary>
        /// 처음 열 때의 기본 자리: 화면 한가운데. 피벗이 (0.5, 1)이라 y는 창의 위쪽 모서리다 -
        /// 높이의 절반만큼 올리면 세로 중앙에 온다(ChestUI.DefaultWindowPosition과 같은 계산).
        /// </summary>
        private static Vector2 DefaultWindowPosition()
        {
            return new Vector2(0f, WindowHeight * 0.5f);
        }

        // ────────────────────────────────────────────────────────────────────────
        // 구독
        // ────────────────────────────────────────────────────────────────────────

        /// <summary>뗏목 진행 변화를 구독한다(제작 직후 같은 프레임에 목록이 갱신되도록).</summary>
        private void SubscribeRaft()
        {
            if (raft == null || raftSubscribed)
                return;

            raft.ProgressChanged += OnRaftChanged;
            raftSubscribed = true;
        }

        private void UnsubscribeRaft()
        {
            if (raft == null || !raftSubscribed)
            {
                raftSubscribed = false;
                return;
            }

            raft.ProgressChanged -= OnRaftChanged;
            raftSubscribed = false;
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

        private void OnRaftChanged()
        {
            if (IsWindowOpen)
                RefreshAll();
        }

        private void OnInventoryChanged()
        {
            if (IsWindowOpen)
                RefreshAll();
        }

        // ────────────────────────────────────────────────────────────────────────
        // 매 프레임
        // ────────────────────────────────────────────────────────────────────────

        private void Update()
        {
            // 닫혀 있으면 여기서 끝난다 - 창이 닫힌 동안 이 파일은 문자열을 한 글자도 만들지 않는다.
            if (!IsWindowOpen)
                return;

            // 뗏목이 사라졌거나(씬 정리) 플레이어가 멀어지면 창을 붙들고 있을 이유가 없다.
            if (raft == null || IsPlayerTooFar())
            {
                SetOpen(false);
                return;
            }

            HandleNumberKeys();

            // timeScale 0 화면 위에서도 멈추지 않도록 unscaled로 센다(프로젝트 공통 규칙).
            refreshTimer -= Time.unscaledDeltaTime;
            if (refreshTimer <= 0f)
            {
                refreshTimer = RefreshInterval;
                RefreshAll();
            }

            if (messageUntil > 0f && Time.unscaledTime > messageUntil)
                ClearMessage();
        }

        /// <summary>
        /// 숫자키 1~9로 같은 순번의 줄을 제작한다. 마우스를 쓰지 않고도 목록을 다룰 수 있어야 한다는
        /// 규칙은 건축 핫바(BuildMenuUI.SelectKeys)와 같다. 줄 순서 = RaftBuildCatalog.Order 순서다.
        /// </summary>
        private void HandleNumberKeys()
        {
            // [건축 핫바와의 겹침] BuildMenuUI도 1~7을 쓰지만 그쪽은 **건축 모드가 켜져 있을 때만**
            // 입력을 읽는다. 두 창이 동시에 떠 있으면 부품 선택이 함께 바뀌는데, 선택은 재료를 쓰지
            // 않는 무해한 상태 변화라 굳이 막지 않는다(막으려면 두 파일 중 하나가 다른 쪽을 알아야 한다).
            for (int i = 0; i < rows.Length && i < 9; i++)
            {
                if (Input.GetKeyDown(KeyCode.Alpha1 + i))
                {
                    TryBuild(rows[i].entry);
                    return;
                }
            }
        }

        /// <summary>플레이어가 뗏목에서 너무 멀어졌는지. 상호작용 거리 + 여유를 넘으면 참이다.</summary>
        private bool IsPlayerTooFar()
        {
            if (interaction == null || raft == null)
                return false;

            float limit = interaction.interactionDistance + AutoCloseSlack;
            return (interaction.transform.position - raft.transform.position).sqrMagnitude > limit * limit;
        }

        // ────────────────────────────────────────────────────────────────────────
        // 제작
        // ────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// 한 줄을 제작한다. 판정·재료 소모·설치는 전부 <see cref="RaftBuildCatalog.TryBuild"/> 한 곳에서
        /// 일어나고, 여기서는 결과를 소리와 문구로 옮기기만 한다.
        /// </summary>
        private void TryBuild(RaftBuildEntry entry)
        {
            if (raft == null)
                return;

            EnsureInventory();

            if (RaftBuildCatalog.TryBuild(raft, inventory, entry, out string failure))
            {
                ShowMessage($"{RaftBuildCatalog.GetDisplayName(entry)} 완성 — {raft.DescribeState()}");
                AudioManager.Instance?.PlayCraftSuccess();
            }
            else
            {
                ShowMessage(failure);
                AudioManager.Instance?.PlayActionFail();
            }

            RefreshAll();
        }

        // ────────────────────────────────────────────────────────────────────────
        // 생성
        // ────────────────────────────────────────────────────────────────────────

        /// <summary>캔버스 · 창 · 요약 줄 · 목록 9줄 · 안내줄을 만든다.</summary>
        private void BuildUI()
        {
            // 인벤토리(10)·제작(10)·상자(10)와 같은 층. 툴팁(13)·설정(16)·사망(20)보다는 아래여야 한다.
            var canvas = UIBuilder.CreateCanvas("RaftBuildCanvas", sortOrder: 10);
            canvasRect = canvas.GetComponent<RectTransform>();

            // 창 6개가 공유하는 골격. 호출자는 **본문 크기만** 말하고 제목줄·여백은 골격이 정한다.
            var frame = UIBuilder.CreateSkinnedWindow(canvas.transform, "RaftBuildWindow",
                BodyWidth, BodyHeight, "뗏목 제작", canvasRect, () => SetOpen(false));

            windowRt = frame.window;
            bodyRt = frame.body;
            panelRoot = windowRt.gameObject;

            // 제목 표시줄이 곧 드래그 손잡이이자, CursorLockController가 "창이 열렸다"를 판정하는 근거다.
            dragHandle = frame.drag;
            if (dragHandle != null)
            {
                dragHandle.onMoved = position =>
                {
                    savedWindowPosition = position;
                    hasSavedWindowPosition = true;
                };
            }

            // 단계 요약은 제목 **옆**이다. 본문 첫 줄에 두면 창마다 요약이 다른 자리에 놓인다.
            headerLabel = frame.status;

            for (int i = 0; i < rows.Length; i++)
                rows[i] = CreateRow(i);

            messageLabel = CreateLine("Message", MessageTop, MessageHeight, UITheme.FontBody, SunstrokeGold);

            var hint = CreateLine("Hint", HintTop, HintHeight, UITheme.FontBody, UITheme.TextDim);
            hint.text = "숫자키 1~9 또는 [만들기] 클릭 · 제목 표시줄 드래그로 창 이동 · E 또는 X로 닫기";
        }

        /// <summary>본문 폭을 가득 채우는 한 줄짜리 글자(문구/안내). ChestUI.CreateRow와 같은 배치다.</summary>
        private Text CreateLine(string name, float top, float height, int fontSize, Color color)
        {
            var text = UIBuilder.CreateText(bodyRt, name, "", fontSize, color, TextAnchor.MiddleLeft);
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;

            var rt = text.rectTransform;
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.sizeDelta = new Vector2(BodyWidth, height);
            rt.anchoredPosition = new Vector2(0f, -top);
            return text;
        }

        /// <summary>
        /// 목록 한 줄: [이름 + 한 줄 설명] [재료 필요/보유] [만들기 버튼].
        /// 버튼만 클릭을 받는다 - 줄 전체를 버튼으로 만들면 창을 드래그하려다 제작이 눌린다.
        /// </summary>
        private BuildRow CreateRow(int index)
        {
            RaftBuildEntry entry = RaftBuildCatalog.Order[index];

            var row = new BuildRow { entry = entry };

            var panel = UIBuilder.CreatePanel(bodyRt, $"Row{index}_{entry}",
                anchorMin: new Vector2(0f, 1f), anchorMax: new Vector2(0f, 1f),
                offsetMin: Vector2.zero, offsetMax: Vector2.zero,
                color: RowBackground);
            panel.pivot = new Vector2(0f, 1f);
            panel.sizeDelta = new Vector2(BodyWidth, RowHeight);
            panel.anchoredPosition = new Vector2(0f, -(index * (RowHeight + RowSpacing)));

            row.background = panel.GetComponent<Image>();

            // 이름(숫자키 번호를 앞에 붙여 둔다 - 목록과 단축키가 같은 순서임을 화면에서 바로 읽게).
            row.nameLabel = CreateCell(panel, "Name", $"{index + 1}. {RaftBuildCatalog.GetDisplayName(entry)}",
                UITheme.FontHeading, UITheme.TextPrimary, new Vector2(8f, -2f), new Vector2(NameWidth, 20f));

            row.descLabel = CreateCell(panel, "Desc", RaftBuildCatalog.GetDescription(entry),
                UITheme.FontBody, UITheme.TextDim, new Vector2(8f, -21f), new Vector2(NameWidth, 16f));

            row.costLabel = CreateCell(panel, "Cost", "",
                UITheme.FontBody, UITheme.TextPrimary, new Vector2(8f + NameWidth + 8f, -11f), new Vector2(CostWidth, 18f));

            row.button = UIBuilder.CreateButton(panel, "Build", "만들기", () => TryBuild(entry));
            var buttonRt = row.button.GetComponent<RectTransform>();
            buttonRt.anchorMin = new Vector2(1f, 1f);
            buttonRt.anchorMax = new Vector2(1f, 1f);
            buttonRt.pivot = new Vector2(1f, 1f);
            buttonRt.sizeDelta = new Vector2(ButtonWidth, ButtonHeight);
            buttonRt.anchoredPosition = new Vector2(-8f, -(RowHeight - ButtonHeight) * 0.5f);

            row.buttonLabel = row.button.GetComponentInChildren<Text>();
            if (row.buttonLabel != null)
                row.buttonLabel.fontSize = UITheme.FontButton;

            return row;
        }

        /// <summary>줄 안쪽의 글자 하나. 좌상단 기준 오프셋 + 고정 크기로 놓는다.</summary>
        private Text CreateCell(RectTransform parent, string name, string content, int fontSize,
            Color color, Vector2 anchoredPosition, Vector2 size)
        {
            var text = UIBuilder.CreateText(parent, name, content, fontSize, color, TextAnchor.MiddleLeft);
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;

            var rt = text.rectTransform;
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.sizeDelta = size;
            rt.anchoredPosition = anchoredPosition;
            return text;
        }

        // ────────────────────────────────────────────────────────────────────────
        // 갱신
        // ────────────────────────────────────────────────────────────────────────

        /// <summary>요약 줄과 목록 전체를 지금 상태에 맞춘다(열려 있을 때만 불린다).</summary>
        private void RefreshAll()
        {
            EnsureInventory();
            UpdateHeader();

            for (int i = 0; i < rows.Length; i++)
                UpdateRow(rows[i]);
        }

        /// <summary>
        /// 요약 줄. 문장은 <see cref="RaftStructure.DescribeState"/> 하나만 쓴다 - 프롬프트·퀘스트·
        /// 디버그 패널과 같은 문장을 보여주기 위해서다(창마다 다른 요약을 만들지 않는다).
        /// </summary>
        private void UpdateHeader()
        {
            if (headerLabel == null)
                return;

            string state = raft != null ? raft.DescribeState() : "뗏목 없음";
            if (shownHeader == state)
                return;

            shownHeader = state;
            headerLabel.text = state;
        }

        /// <summary>
        /// 한 줄을 갱신한다. **실제로 달라진 것만** 다시 쓴다(문자열 비교 후 대입) - 0.2초마다
        /// 9줄 × 3개 라벨을 무조건 다시 쓰면 그것만으로 가비지가 쌓인다.
        /// </summary>
        private void UpdateRow(BuildRow row)
        {
            if (row == null)
                return;

            bool available = RaftBuildCatalog.IsAvailable(raft, row.entry, out string blockedReason);
            bool hasMaterials = RaftBuildCatalog.HasMaterials(inventory, row.entry);
            bool canBuild = available && hasMaterials;

            // 재료 줄: "나뭇가지 4/12 · 노끈 2/1" 형태(필요/보유). 모자란 항목만 붉게 물들인다.
            string cost = BuildCostText(row.entry);
            if (row.shownCost != cost)
            {
                row.shownCost = cost;
                row.costLabel.text = cost;
            }

            // 설명 줄이 곧 상태 줄이다. 만들 수 없을 때는 설명 대신 사유("장착됨" / "가득 참" /
            // "전부 깔림" / "바닥판 먼저")를 그 자리에 쓴다.
            // **버튼 문구에 사유를 넣지 않는 이유**: 버튼 폭이 76px이라 "바닥판 먼저"가 두 줄로 접혀
            // 26px 높이 안에서 잘린다. 사유는 폭이 190px인 이 줄에 쓰는 것이 안전하다.
            string desc = available ? RaftBuildCatalog.GetDescription(row.entry) : blockedReason;
            if (row.shownDesc != desc)
            {
                row.shownDesc = desc;
                row.descLabel.text = desc;
                row.descLabel.color = available ? UITheme.TextDim : SunstrokeGold;
            }

            if (row.shownInteractable != canBuild)
            {
                row.shownInteractable = canBuild;
                row.button.interactable = canBuild;
            }

            // 만들 수 없는 줄은 배경과 글자를 함께 낮춘다(회색 처리). 위험 경고색은 쓰지 않는다 -
            // "지금은 못 하지만 조건만 갖추면 된다"는 신호이기 때문이다(InteractionPromptUI와 같은 규칙).
            bool blocked = !canBuild;
            if (row.shownBlocked != blocked)
            {
                row.shownBlocked = blocked;
                row.background.color = blocked ? RowBackgroundBlocked : RowBackground;
                row.nameLabel.color = blocked ? UITheme.TextDim : UITheme.TextPrimary;
            }
        }

        /// <summary>
        /// 재료 한 줄을 만든다. 각 재료는 "이름 필요/보유"로 적고, 보유가 모자라면 그 항목만 붉다.
        /// StringBuilder를 재사용하므로 호출마다 생기는 할당은 최종 ToString 하나뿐이고,
        /// 그 결과가 이전과 같으면 호출부가 라벨에 대입조차 하지 않는다.
        /// </summary>
        private string BuildCostText(RaftBuildEntry entry)
        {
            costBuilder.Length = 0;

            var cost = RaftBuildCatalog.GetCost(entry);
            for (int i = 0; i < cost.Count; i++)
            {
                if (string.IsNullOrEmpty(cost[i].itemName) || cost[i].count <= 0)
                    continue;

                if (costBuilder.Length > 0)
                    costBuilder.Append("  ");

                int owned = RaftBuildCatalog.CountOwned(inventory, cost[i].itemName);
                bool enough = owned >= cost[i].count;

                costBuilder.Append("<color=#").Append(enough ? HexOk : HexShort).Append('>')
                    .Append(cost[i].itemName).Append(' ')
                    .Append(cost[i].count).Append('/').Append(owned)
                    .Append("</color>");
            }

            return costBuilder.ToString();
        }

        // ────────────────────────────────────────────────────────────────────────
        // 안내 문구
        // ────────────────────────────────────────────────────────────────────────

        /// <summary>제작 결과/실패 사유를 잠깐 띄운다.</summary>
        private void ShowMessage(string text)
        {
            if (messageLabel == null)
                return;

            messageLabel.text = text ?? string.Empty;
            messageUntil = Time.unscaledTime + MessageDuration;
        }

        private void ClearMessage()
        {
            if (messageLabel != null && messageLabel.text.Length > 0)
                messageLabel.text = string.Empty;

            messageUntil = -1f;
        }
    }
}
