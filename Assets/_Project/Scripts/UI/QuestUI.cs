using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using MakeGame.Systems;

namespace MakeGame.UI
{
    /// <summary>
    /// 퀘스트("할 일") 창. **판정은 하지 않는다** - Systems/QuestSystem.cs가 판정한 목록을 그대로 그린다.
    ///
    /// 창 규격은 인벤토리(Tab) · 세계 지도(M)와 완전히 같다(최근 배치에서 확립한 창 UI 표준):
    /// · 알파 0.93 어두운 패널 + 상단 테두리
    /// · 제목 표시줄 + 우상단 빨간 X(마우스로 닫기)
    /// · 제목 표시줄을 UIDragHandle로 끌어 창 이동, 화면 밖으로 나가지 않게 클램프
    /// · 하단 단축키 힌트 줄
    /// 공용 부품(UIBuilder / UIDragHandle)만 재사용하고 새 부품을 만들지 않는다.
    ///
    /// 씬에 인스턴스가 없다(씬 파일을 편집할 수 없다). SurvivalHudUI와 같은
    /// RuntimeInitializeOnLoadMethod + sceneLoaded 패턴으로 씬이 로드될 때마다 스스로 생성된다.
    /// 따라서 **코드 기본값이 유일한 진실**이다.
    /// </summary>
    public class QuestUI : MonoBehaviour
    {
        /// <summary>
        /// 퀘스트 창 단축키. J는 이 프로젝트에서 비어 있고(전수 grep: C E F G M R V Tab Space Escape
        /// F3 F5 F6 F7 F8 F9 +/- Shift Ctrl만 사용 중), RPG 퀘스트 로그의 관례 키다.
        /// </summary>
        public KeyCode toggleKey = KeyCode.J;

        /// <summary>이 씬의 퀘스트 창(씬 리로드마다 새 인스턴스로 교체된다).</summary>
        public static QuestUI Instance { get; private set; }

        // ── 치수 (인벤토리/지도 창과 같은 값) ────────────────────────────────────
        private const float WindowWidth = 440f;
        private const float TitleBarHeight = 34f;
        private const float WindowPadding = 14f;
        private const float HintHeight = 16f;
        private const float HeaderHeight = 24f;
        private const float RowHeight = 46f;
        private const float RowSpacing = 4f;
        private const float GroupSpacing = 10f;
        private const float ContentTop = TitleBarHeight + 8f;
        private const float ContentBottom = 12f + HintHeight + 8f;

        // ── 색 (새로 만들지 않는다 - 이미 프로젝트에서 쓰는 값 그대로) ───────────
        private static readonly Color WindowBackground = new Color(0.04f, 0.05f, 0.06f, 0.93f);
        private static readonly Color TitleBarColor = new Color(1f, 1f, 1f, 0.07f);
        private static readonly Color DangerRed = new Color(0.8f, 0.2f, 0.2f, 1f);        // #CC3333
        private static readonly Color MedicGreen = new Color(0.31f, 0.659f, 0.478f, 1f);  // #4FA87A
        private static readonly Color NeutralGray = new Color(0.8f, 0.8f, 0.8f, 1f);      // #CCCCCC
        private static readonly Color DimGray = new Color(0.55f, 0.55f, 0.55f, 1f);
        private static readonly Color LockedGray = new Color(0.4f, 0.4f, 0.4f, 1f);
        private static readonly Color ObjectiveGold = new Color(1f, 0.9f, 0.4f, 1f);      // 목표 문구와 같은 옅은 금색

        private static readonly Color RowActiveBackground = new Color(1f, 1f, 1f, 0.07f);
        private static readonly Color RowDoneBackground = new Color(1f, 1f, 1f, 0.02f);
        private static readonly Color BarTrack = new Color(1f, 1f, 1f, 0.12f);

        /// <summary>묶음 표시 이름. QuestCategory의 정수값 순서와 반드시 일치해야 한다.</summary>
        private static readonly string[] CategoryNames = { "생존", "정착", "항해" };

        /// <summary>
        /// 창 위치를 세션 동안 기억한다. static인 이유: 이 컴포넌트는 씬 로드마다 새로 생성되므로
        /// 인스턴스 필드에 두면 "새 게임/불러오기 후 창이 처음 자리로 돌아간다"(인벤토리와 같은 처리).
        /// </summary>
        private static bool hasSavedWindowPosition;
        private static Vector2 savedWindowPosition;

        private QuestSystem questSystem;

        private RectTransform canvasRect;
        private RectTransform windowRt;
        private GameObject panelRoot;
        private RectTransform contentRt;
        private UIDragHandle dragHandle;
        private Text titleLabel;
        private Text hintLabel;

        private string lastDisplayedTitle;
        private bool subscribed;

        /// <summary>퀘스트 한 줄의 화면 부품 묶음. 갱신마다 오브젝트를 새로 만들지 않는다.</summary>
        private class QuestRow
        {
            public GameObject go;
            public Image background;
            public Image checkBox;
            public Text title;
            public Text progress;
            public Text status;
            public Image barFill;

            // 지금 이 줄이 표시 중인 내용(문자열을 다시 만들지 판단하는 캐시).
            public string shownTitle;
            public string shownProgress;
            public string shownStatus;
            public int shownPercent = -1;
            public int shownState = -1; // 0 완료 · 1 진행 중 · 2 대기
        }

        private readonly List<QuestRow> rows = new List<QuestRow>();
        private readonly List<Text> headers = new List<Text>();

        /// <summary>지금 화면에 조립돼 있는 항목의 id 순서. 이것이 달라졌을 때만 배치를 다시 계산한다.</summary>
        private readonly List<string> builtIds = new List<string>();

        private bool IsOpen => panelRoot != null && panelRoot.activeSelf;

        /// <summary>씬이 로드될 때마다 새 QuestUI를 만든다(SurvivalHudUI와 같은 패턴).</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Bootstrap()
        {
            SceneManager.sceneLoaded += (scene, mode) =>
            {
                var go = new GameObject("QuestUI");
                go.AddComponent<QuestUI>();
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
            if (questSystem != null && subscribed)
            {
                questSystem.Changed -= OnQuestsChanged;
                subscribed = false;
            }

            if (Instance == this)
                Instance = null;
        }

        /// <summary>
        /// 토글 입력만 매 프레임 본다. 목록 갱신은 QuestSystem.Changed(값이 실제로 바뀐 순간)에만
        /// 일어나므로 여기서 폴링하지 않는다 - 매 프레임 문자열을 다시 만들지 않기 위함이다.
        /// </summary>
        private void Update()
        {
            // QuestSystem도 런타임 생성이라 어느 쪽이 먼저 Start를 도는지 보장되지 않는다
            // (AGENT_BRIEF 4장: 실행 순서 미지정). 잡힐 때까지 매 프레임 싸게 확인만 한다.
            if (questSystem == null)
            {
                questSystem = QuestSystem.Instance;
                if (questSystem != null && !subscribed)
                {
                    questSystem.Changed += OnQuestsChanged;
                    subscribed = true;
                    if (IsOpen)
                        Refresh();
                }
            }

            if (panelRoot == null)
                return;

            if (Input.GetKeyDown(toggleKey))
                SetOpen(!panelRoot.activeSelf);
        }

        private void OnQuestsChanged()
        {
            if (IsOpen)
                Refresh();
        }

        /// <summary>
        /// 창을 열거나 닫는다. 인벤토리(Tab)·지도(M)와 같은 규칙이다: 옮겨둔 자리를 복원하고,
        /// 해상도가 바뀌었을 경우를 대비해 화면 안으로 다시 맞춘 뒤 즉시 한 번 그린다.
        /// **창 밖 클릭으로 닫지 않는다** - 이 프로젝트는 커서를 잠그지 않아 창 밖 클릭이 곧 월드
        /// 조작이고, 다른 창과 나란히 띄워 쓰는 흐름에서 퀘스트 창만 사라진다(InventoryUI.SetOpen 참고).
        /// </summary>
        public void SetOpen(bool open)
        {
            if (panelRoot == null)
                return;

            panelRoot.SetActive(open);
            if (!open)
                return;

            if (hasSavedWindowPosition)
                windowRt.anchoredPosition = savedWindowPosition;
            else
                windowRt.anchoredPosition = DefaultWindowPosition();

            if (dragHandle != null)
                dragHandle.ClampNow();

            // 창을 여는 순간의 값이 최신이어야 한다(폴링 주기 0.5초를 기다리지 않는다).
            if (questSystem != null)
                questSystem.RefreshNow();

            Refresh();
        }

        /// <summary>처음 열 때의 자리: 화면 가운데 위. 좌측은 생존 HUD·인벤토리, 우측은 제작 창이 쓴다.</summary>
        private Vector2 DefaultWindowPosition()
        {
            if (canvasRect == null)
                return Vector2.zero;

            return new Vector2(0f, canvasRect.rect.height * 0.5f - 60f);
        }

        // ────────────────────────────────────────────────────────────────────────
        // 생성
        // ────────────────────────────────────────────────────────────────────────

        private void BuildUI()
        {
            var canvas = UIBuilder.CreateCanvas("QuestCanvas", sortOrder: 10);
            canvasRect = canvas.GetComponent<RectTransform>();

            windowRt = UIBuilder.CreatePanel(
                canvas.transform, "QuestWindow",
                anchorMin: new Vector2(0.5f, 0.5f), anchorMax: new Vector2(0.5f, 0.5f),
                offsetMin: Vector2.zero, offsetMax: Vector2.zero,
                color: WindowBackground,
                addTopBorder: true);

            windowRt.pivot = new Vector2(0.5f, 1f);
            windowRt.sizeDelta = new Vector2(WindowWidth, 400f); // 실제 높이는 Layout()이 항목 수에 맞춰 정한다
            panelRoot = windowRt.gameObject;

            BuildTitleBar();

            var contentGo = new GameObject("Content", typeof(RectTransform));
            contentGo.transform.SetParent(windowRt, false);
            contentRt = contentGo.GetComponent<RectTransform>();
            contentRt.anchorMin = new Vector2(0f, 1f);
            contentRt.anchorMax = new Vector2(0f, 1f);
            contentRt.pivot = new Vector2(0f, 1f);
            contentRt.anchoredPosition = new Vector2(WindowPadding, -ContentTop);
            contentRt.sizeDelta = new Vector2(WindowWidth - WindowPadding * 2f, 0f);

            BuildHint();
        }

        /// <summary>제목 표시줄(드래그 손잡이 + 빨간 X). 인벤토리/지도와 완전히 같은 조립이다.</summary>
        private void BuildTitleBar()
        {
            var titleBar = UIBuilder.CreatePanel(
                windowRt, "TitleBar",
                anchorMin: new Vector2(0f, 1f), anchorMax: new Vector2(1f, 1f),
                offsetMin: new Vector2(0f, -TitleBarHeight), offsetMax: Vector2.zero,
                color: TitleBarColor);

            titleLabel = UIBuilder.CreateText(titleBar, "Title", $"퀘스트 ({toggleKey})", 20, Color.white, TextAnchor.MiddleLeft);
            titleLabel.raycastTarget = false; // 제목 글자가 드래그 입력을 가로채지 않게 한다
            titleLabel.horizontalOverflow = HorizontalWrapMode.Overflow;
            titleLabel.rectTransform.anchorMin = Vector2.zero;
            titleLabel.rectTransform.anchorMax = Vector2.one;
            titleLabel.rectTransform.offsetMin = new Vector2(12f, 0f);
            titleLabel.rectTransform.offsetMax = new Vector2(-40f, 0f);

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

        /// <summary>하단 단축키 힌트 줄(미니맵의 "[M] 지도 · [+/-] 줌"과 같은 형식).</summary>
        private void BuildHint()
        {
            hintLabel = UIBuilder.CreateText(windowRt, "Hint",
                $"[{toggleKey}] 퀘스트 · 제목 표시줄을 끌어 창 이동 · [X] 닫기", 11,
                new Color(1f, 1f, 1f, 0.62f), TextAnchor.MiddleLeft);
            hintLabel.raycastTarget = false;
            hintLabel.horizontalOverflow = HorizontalWrapMode.Overflow;

            var hintRt = hintLabel.rectTransform;
            hintRt.anchorMin = Vector2.zero;
            hintRt.anchorMax = Vector2.zero;
            hintRt.pivot = Vector2.zero;
            hintRt.sizeDelta = new Vector2(WindowWidth - WindowPadding * 2f, HintHeight);
            hintRt.anchoredPosition = new Vector2(WindowPadding, 12f);
        }

        /// <summary>묶음 제목 줄 하나를 만든다(생존 / 정착 / 항해).</summary>
        private Text CreateHeader()
        {
            var header = UIBuilder.CreateText(contentRt, "GroupHeader", "", 13, ObjectiveGold, TextAnchor.LowerLeft);
            header.raycastTarget = false;
            header.horizontalOverflow = HorizontalWrapMode.Overflow;

            var rt = header.rectTransform;
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.sizeDelta = new Vector2(contentRt.sizeDelta.x, HeaderHeight);
            return header;
        }

        /// <summary>퀘스트 한 줄(체크 상자 + 제목 + 상태 칩 + 진행도 문구 + 막대)을 만든다.</summary>
        private QuestRow CreateRow()
        {
            float contentWidth = contentRt.sizeDelta.x;

            var row = new QuestRow();
            var bg = UIBuilder.CreatePanel(
                contentRt, "QuestRow",
                anchorMin: new Vector2(0f, 1f), anchorMax: new Vector2(0f, 1f),
                offsetMin: Vector2.zero, offsetMax: Vector2.zero,
                color: RowActiveBackground);
            bg.pivot = new Vector2(0f, 1f);
            bg.sizeDelta = new Vector2(contentWidth, RowHeight);

            row.go = bg.gameObject;
            row.background = bg.GetComponent<Image>();

            // 체크 상자: 완료면 꽉 찬 초록 사각형, 진행 중이면 금색, 대기면 흐린 회색.
            // 형태(채움 여부)가 아니라 색만으로 구분하지 않도록 완료일 때만 안쪽에 밝은 점이 생긴다.
            var boxRt = UIBuilder.CreatePanel(
                bg, "Check",
                anchorMin: new Vector2(0f, 1f), anchorMax: new Vector2(0f, 1f),
                offsetMin: Vector2.zero, offsetMax: Vector2.zero,
                color: LockedGray);
            boxRt.pivot = new Vector2(0f, 1f);
            boxRt.sizeDelta = new Vector2(14f, 14f);
            boxRt.anchoredPosition = new Vector2(2f, -8f);
            row.checkBox = boxRt.GetComponent<Image>();

            row.title = UIBuilder.CreateText(bg, "Title", "", 14, Color.white, TextAnchor.UpperLeft);
            row.title.raycastTarget = false;
            row.title.horizontalOverflow = HorizontalWrapMode.Overflow;
            var titleRt = row.title.rectTransform;
            titleRt.anchorMin = new Vector2(0f, 1f);
            titleRt.anchorMax = new Vector2(0f, 1f);
            titleRt.pivot = new Vector2(0f, 1f);
            titleRt.sizeDelta = new Vector2(contentWidth - 24f - 74f, 18f);
            titleRt.anchoredPosition = new Vector2(24f, -5f);

            row.status = UIBuilder.CreateText(bg, "Status", "", 11, DimGray, TextAnchor.UpperRight);
            row.status.raycastTarget = false;
            row.status.horizontalOverflow = HorizontalWrapMode.Overflow;
            var statusRt = row.status.rectTransform;
            statusRt.anchorMin = new Vector2(1f, 1f);
            statusRt.anchorMax = new Vector2(1f, 1f);
            statusRt.pivot = new Vector2(1f, 1f);
            statusRt.sizeDelta = new Vector2(70f, 16f);
            statusRt.anchoredPosition = new Vector2(-6f, -6f);

            row.progress = UIBuilder.CreateText(bg, "Progress", "", 11, NeutralGray, TextAnchor.UpperLeft);
            row.progress.raycastTarget = false;
            row.progress.horizontalOverflow = HorizontalWrapMode.Overflow;
            var progressRt = row.progress.rectTransform;
            progressRt.anchorMin = new Vector2(0f, 1f);
            progressRt.anchorMax = new Vector2(0f, 1f);
            progressRt.pivot = new Vector2(0f, 1f);
            progressRt.sizeDelta = new Vector2(contentWidth - 24f - 6f, 16f);
            progressRt.anchoredPosition = new Vector2(24f, -24f);

            row.barFill = UIBuilder.CreateProgressBar(bg, "Bar", BarTrack, MedicGreen);
            var barBgRt = row.barFill.transform.parent.GetComponent<RectTransform>();
            barBgRt.anchorMin = new Vector2(0f, 1f);
            barBgRt.anchorMax = new Vector2(0f, 1f);
            barBgRt.pivot = new Vector2(0f, 1f);
            barBgRt.sizeDelta = new Vector2(contentWidth - 24f - 6f, 5f);
            barBgRt.anchoredPosition = new Vector2(24f, -40f);

            return row;
        }

        // ────────────────────────────────────────────────────────────────────────
        // 갱신
        // ────────────────────────────────────────────────────────────────────────

        /// <summary>QuestSystem의 목록을 화면에 반영한다. 창이 열려 있을 때만 불린다.</summary>
        private void Refresh()
        {
            if (questSystem == null || contentRt == null)
                return;

            var quests = questSystem.Quests;
            EnsureRows(quests);
            Layout(quests);

            for (int i = 0; i < quests.Count; i++)
                ApplyRow(rows[i], quests[i]);

            string windowTitle = $"퀘스트 ({toggleKey})   {questSystem.CompletedCount}/{quests.Count}";
            if (titleLabel != null && windowTitle != lastDisplayedTitle)
            {
                titleLabel.text = windowTitle;
                lastDisplayedTitle = windowTitle;
            }
        }

        /// <summary>
        /// 항목 수/순서가 화면에 조립된 것과 다를 때만 줄과 묶음 제목을 늘린다.
        /// 실제로는 항목 구성이 고정이라 첫 갱신에서 한 번만 만들어진다.
        /// </summary>
        private void EnsureRows(IReadOnlyList<QuestEntry> quests)
        {
            while (rows.Count < quests.Count)
                rows.Add(CreateRow());

            for (int i = quests.Count; i < rows.Count; i++)
            {
                if (rows[i].go.activeSelf)
                    rows[i].go.SetActive(false);
            }

            // 묶음 제목은 목록에 실제로 등장하는 묶음 수만큼만 필요하다(최대 3개).
            while (headers.Count < CategoryNames.Length)
                headers.Add(CreateHeader());
        }

        /// <summary>
        /// 묶음 제목과 줄을 위에서 아래로 배치하고, 창 높이를 내용에 맞춘다.
        /// 항목 구성이 그대로면 아무것도 다시 계산하지 않는다(builtIds 비교).
        /// </summary>
        private void Layout(IReadOnlyList<QuestEntry> quests)
        {
            bool sameLayout = builtIds.Count == quests.Count;
            if (sameLayout)
            {
                for (int i = 0; i < quests.Count; i++)
                {
                    if (builtIds[i] != quests[i].id)
                    {
                        sameLayout = false;
                        break;
                    }
                }
            }

            if (sameLayout)
                return;

            builtIds.Clear();
            for (int i = 0; i < quests.Count; i++)
                builtIds.Add(quests[i].id);

            float y = 0f;
            int headerIndex = 0;
            int previousCategory = -1;

            for (int i = 0; i < quests.Count; i++)
            {
                var quest = quests[i];
                int category = (int)quest.category;

                if (category != previousCategory && headerIndex < headers.Count)
                {
                    if (previousCategory >= 0)
                        y -= GroupSpacing;

                    var header = headers[headerIndex];
                    header.gameObject.SetActive(true);
                    header.text = category >= 0 && category < CategoryNames.Length
                        ? CategoryNames[category]
                        : quest.category.ToString();
                    header.rectTransform.anchoredPosition = new Vector2(0f, y);
                    y -= HeaderHeight;

                    headerIndex++;
                    previousCategory = category;
                }

                var row = rows[i];
                if (!row.go.activeSelf)
                    row.go.SetActive(true);
                row.go.GetComponent<RectTransform>().anchoredPosition = new Vector2(0f, y);
                y -= RowHeight + RowSpacing;
            }

            for (int i = headerIndex; i < headers.Count; i++)
            {
                if (headers[i].gameObject.activeSelf)
                    headers[i].gameObject.SetActive(false);
            }

            float contentHeight = -y;
            contentRt.sizeDelta = new Vector2(WindowWidth - WindowPadding * 2f, contentHeight);
            windowRt.sizeDelta = new Vector2(WindowWidth, ContentTop + contentHeight + ContentBottom);

            // 높이가 바뀌면 화면 아래로 삐져나올 수 있으므로 다시 클램프한다.
            if (dragHandle != null)
                dragHandle.ClampNow();
        }

        /// <summary>
        /// 줄 하나의 내용을 반영한다. 표시되는 값이 실제로 바뀐 것만 대입한다(문자열 재생성 절약).
        /// 상태 3단: 0 완료(체크 + 흐리게) · 1 진행 중(강조) · 2 대기(앞 단계 미완료).
        /// </summary>
        private void ApplyRow(QuestRow row, QuestEntry quest)
        {
            int state = quest.completed ? 0 : (quest.locked ? 2 : 1);

            if (row.shownTitle != quest.title)
            {
                row.title.text = quest.title;
                row.shownTitle = quest.title;
            }

            string progressText = quest.progress ?? "";
            if (row.shownProgress != progressText)
            {
                row.progress.text = progressText;
                row.shownProgress = progressText;
            }

            string statusText = state == 0 ? "완료" : (state == 2 ? "대기" : "진행 중");
            if (row.shownStatus != statusText)
            {
                row.status.text = statusText;
                row.shownStatus = statusText;
            }

            int percent = Mathf.RoundToInt(Mathf.Clamp01(quest.fraction) * 100f);
            if (row.shownPercent != percent)
            {
                row.barFill.fillAmount = percent / 100f;
                row.shownPercent = percent;
            }

            if (row.shownState == state)
                return;
            row.shownState = state;

            switch (state)
            {
                case 0: // 완료 - 체크(꽉 찬 초록) + 전체를 흐리게
                    row.background.color = RowDoneBackground;
                    row.checkBox.color = MedicGreen;
                    row.title.color = DimGray;
                    row.progress.color = DimGray;
                    row.status.color = MedicGreen;
                    row.barFill.color = MedicGreen;
                    break;

                case 2: // 대기 - 앞 단계가 안 끝났다. 강조하지 않는다.
                    row.background.color = RowDoneBackground;
                    row.checkBox.color = new Color(1f, 1f, 1f, 0.12f);
                    row.title.color = LockedGray;
                    row.progress.color = LockedGray;
                    row.status.color = LockedGray;
                    row.barFill.color = LockedGray;
                    break;

                default: // 진행 중 - 지금 할 일. 제목은 흰색, 진행도는 금색으로 눈에 먼저 들어오게 한다.
                    row.background.color = RowActiveBackground;
                    row.checkBox.color = ObjectiveGold;
                    row.title.color = Color.white;
                    row.progress.color = ObjectiveGold;
                    row.status.color = ObjectiveGold;
                    row.barFill.color = ObjectiveGold;
                    break;
            }
        }
    }
}
