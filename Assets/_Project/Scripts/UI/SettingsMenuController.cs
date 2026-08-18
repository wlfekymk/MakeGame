using System.Text;
using UnityEngine;
using UnityEngine.UI;
using MakeGame.Player;
using MakeGame.Systems;

namespace MakeGame.UI
{
    /// <summary>
    /// 음량(효과음/배경음)·마우스 감도·Y축 반전·난이도·세이브 슬롯을 조절하고, 조작키 목록을 보여주는 화면.
    ///
    /// [설정 확장] 볼륨 2종의 주인은 지금도 AudioManager다(그 파일이 PlayerPrefs에 직접 읽고 쓴다 -
    /// 락 밖이라 건드리지 않았다). 이번에 늘어난 항목은 MakeGame.Systems.GameSettings가 **같은 방식**
    /// (PlayerPrefs + "MakeGame_" 접두 키)으로 따로 보관한다. 두 저장소가 공존하되 키가 겹치지 않는다.
    /// 타이틀 화면(MainMenuController)의 "설정" 버튼으로 열 수도 있고, 플레이 중에는 toggleKey(기본 Esc)로
    /// 직접 열고 닫아 간이 일시정지 메뉴처럼 쓸 수도 있다.
    /// 개선(B2-13): OnGUI(레거시 IMGUI)로 직접 그리던 것을 UIBuilder 기반 UGUI로 옮겼다. IMGUI는
    /// Screen Space Overlay Canvas보다 항상 나중에(최상단에) 그려져 다른 UGUI 화면을 가려버리는 문제가
    /// 있었기 때문에(GameOverController.OnGUI 사례 참고) OnGUI를 완전히 제거했다. UGUI로 바뀌면서
    /// "MainMenuController가 이 컴포넌트의 DrawSettingsPanel()을 대신 호출해서 그려주는" 방식이 더 이상
    /// 필요 없어졌다 - 이 컴포넌트가 스스로 자기 캔버스를 갖고 isOpen에 따라 보이고 숨는다. 설정 캔버스가
    /// 타이틀 캔버스보다 항상 위(sortOrder 높음)에 있고 화면 전체를 덮는 불투명 배경을 깔기 때문에,
    /// 열려 있는 동안은 자연히 타이틀 화면의 버튼 클릭도 가로막는다(레이캐스트가 앞쪽 그래픽에서 멈춤).
    /// </summary>
    public class SettingsMenuController : MonoBehaviour
    {
        [Tooltip("플레이 중 이 화면을 직접 열고 닫는 키. 타이틀 화면에서는 '설정' 버튼으로 연다.")]
        public KeyCode toggleKey = KeyCode.Escape;

        [Tooltip("플레이 중 toggleKey로 직접 열었을 때 시간을 멈출지 여부 (간이 일시정지 역할)")]
        public bool pauseTimeWhenOpenedDuringPlay = true;

        /// <summary>현재 설정 화면이 열려 있는지 여부.</summary>
        public bool isOpen = false;

        /// <summary>타이틀 화면을 거치지 않고 플레이 중 toggleKey로 직접 연 것인지 여부.</summary>
        private bool openedStandalone = false;

        private float timeScaleBeforeOpen = 1f;

        private GameObject panelRoot;
        private Slider sfxSlider;
        private Slider bgmSlider;
        private Text sfxValueLabel;
        private Text bgmValueLabel;

        // ── 조작/게임 설정 (GameSettings가 PlayerPrefs에 저장한다) ────────────────────────────────
        // 볼륨 2종은 여전히 AudioManager가 주인이고(같은 PlayerPrefs 규약), 여기서 늘어난 항목만
        // GameSettings가 맡는다. 두 저장소가 같은 접두("MakeGame_")를 쓰되 키가 겹치지 않는다.
        private Slider sensitivitySlider;
        private Text sensitivityValueLabel;
        private Button invertYButton;
        private Text invertYLabel;
        private Button difficultyButton;
        private Text difficultyLabel;

        // 세이브 슬롯 선택(플레이 중 F5/F9가 어느 슬롯에 걸리는지를 여기서 바꾼다).
        private Button[] slotButtons;
        private Text[] slotButtonLabels;

        private static readonly Color SlotIdleColor = new Color(0.20f, 0.26f, 0.24f, 1f);
        private static readonly Color SlotSelectedColor = new Color(0.25f, 0.55f, 0.3f, 1f);

        // ── 조작키 안내 ────────────────────────────────────────────────────────────────────────
        // [game-designer 지적] 이 게임의 키는 9개가 넘는데(E/R/C/G/Tab/V/M/F/LCtrl/Space/Esc)
        // 화면에서 발견 가능한 것은 사실상 [M](레이더 하단)과 [E](조준 프롬프트)뿐이다. DebugHud에
        // 전체 목록이 있었지만 그건 F3로 여는 QA 도구이고 기본이 꺼짐이라 플레이어의 경로가 아니다.
        //
        // 새 도움말 패널을 만들지 않는다. 이 설정 화면이 (1) 타이틀에서 "설정" 버튼으로,
        // (2) 플레이 중 Esc로 열리는 유일한 상시 메뉴이고, 조작 안내는 관례적으로 여기 있다.
        // 이 화면 자체의 발견 경로는 MinimapUI 레이더 하단 상시 힌트가 담당한다([Esc] 조작키).
        //
        // **키 문자열을 여기에 적어두지 않는다.** 이 프로젝트는 코드/씬 값이 갈라지는 것이 사고의
        // 유일한 원인이고(AGENT_BRIEF 0장), 주석과 값이 어긋난 전력이 여러 번 있다. 각 컴포넌트가
        // 인스펙터/씬에서 실제로 들고 있는 KeyCode 필드를 화면이 열릴 때마다 직접 읽어 조립한다.
        // 씬에서 키를 바꾸면 이 목록도 자동으로 따라간다.
        private Text controlsLabel;
        // 낚시 줄과 슬롯 표기가 늘면서 완성 문자열이 400자를 넘겼다. 초기 용량을 넉넉히 잡아
        // 화면을 열 때마다 내부 버퍼가 재할당되지 않게 한다(창이 닫혀 있으면 어차피 비용 0이다).
        private readonly StringBuilder controlsBuilder = new StringBuilder(640);

        /// <summary>
        /// 시작 시 설정 UI 계층을 생성하고 기본적으로 닫힌 상태로 둔다.
        /// </summary>
        private void Start()
        {
            BuildUI();
            SetPanelActive(false);
        }

        /// <summary>
        /// 플레이 중에는 toggleKey로 직접 열고 닫을 수 있다.
        /// 타이틀/설정 화면을 통해 이미 열려 있는 경우(openedStandalone == false)에는 이 키로 닫지 않는다 -
        /// 그 경우는 각 화면의 자체 닫기 버튼/흐름을 따르게 한다.
        /// </summary>
        private void Update()
        {
            if (!Input.GetKeyDown(toggleKey))
                return;

            if (isOpen && openedStandalone)
                Close();
            else if (!isOpen)
                OpenStandalone();
        }

        /// <summary>
        /// 타이틀 화면 등 다른 화면에서 이 설정 화면을 열 때 호출한다.
        /// 시간 제어는 호출한 쪽(MainMenuController 등)이 이미 담당하므로 여기서는 건드리지 않는다.
        /// </summary>
        public void Open()
        {
            isOpen = true;
            openedStandalone = false;
            ShowPanel();
        }

        /// <summary>
        /// 플레이 도중 toggleKey로 직접 열 때 호출한다. 필요하면 시간을 멈춰 간이 일시정지 메뉴처럼 동작시킨다.
        /// </summary>
        private void OpenStandalone()
        {
            isOpen = true;
            openedStandalone = true;

            if (pauseTimeWhenOpenedDuringPlay)
            {
                timeScaleBeforeOpen = Time.timeScale;
                Time.timeScale = 0f;
            }

            ShowPanel();
        }

        /// <summary>
        /// 설정 화면을 닫는다. toggleKey로 직접 연 경우에만, 멈췄던 시간을 원래대로 되돌린다.
        /// </summary>
        public void Close()
        {
            isOpen = false;

            if (openedStandalone && pauseTimeWhenOpenedDuringPlay)
                Time.timeScale = timeScaleBeforeOpen;

            openedStandalone = false;
            SetPanelActive(false);
        }

        /// <summary>
        /// 캔버스와 반투명 전체화면 배경, 중앙의 설정 박스(제목/효과음·배경음 슬라이더/닫기 버튼)를 생성한다.
        /// Time.timeScale이 0인 상태(타이틀/일시정지)에서도 UGUI 버튼·슬라이더 클릭은 EventSystem이
        /// Time.deltaTime과 무관하게 매 프레임 처리하므로 정상 동작한다(GameOverUI에서 확인된 패턴과 동일).
        /// </summary>
        private void BuildUI()
        {
            // MainMenuCanvas(15)보다 항상 위에 그려져야 타이틀 화면을 가리는 배경 역할을 할 수 있다.
            var canvas = UIBuilder.CreateCanvas("SettingsCanvas", sortOrder: 16);

            var backdrop = UIBuilder.CreatePanel(
                canvas.transform, "SettingsBackdrop",
                anchorMin: Vector2.zero, anchorMax: Vector2.one,
                offsetMin: Vector2.zero, offsetMax: Vector2.zero,
                color: new Color(0.05f, 0.08f, 0.12f, 0.85f));

            panelRoot = backdrop.gameObject;

            // 조작키 안내 6줄이 들어가면서 박스를 420x260 → 520x480으로 키웠다.
            // (내용 합계: 제목 40 + 음량 4줄 92 + 조작 제목 22 + 조작 본문 150 + 닫기 36 = 340,
            //  spacing 10 x 7 = 70, padding 상하 40 → 450. 480 안에 들어간다.)
            //
            // [전투 확장] 회피/투척 한 줄이 늘어 본문이 11줄이 되면서 520x520 / 본문 200으로 키웠다.
            // 본문 라벨은 verticalOverflow=Overflow라 넘쳐도 잘리지는 않지만, 대신 아래 '닫기' 버튼을
            // 파고든다(minHeight는 잘라내는 값이 아니라 자리를 잡아주는 값이다). 실측 기준 11줄 =
            // 12pt × 줄간격 1.25 ≈ 17.4px × 11 ≈ 191px이므로 200이면 여유가 남는다.
            //
            // [설정 확장] 감도 2줄(라벨 22 + 슬라이더 24) · 반전/난이도 줄 34 · 슬롯 줄 34가 늘고,
            // 조작 본문도 낚시 한 줄이 더해져 12줄이 되면서 본문을 235로 키웠다(12줄 ≈ 209px + 여유).
            // 폭도 520 → 560으로 넓혔다 - 건축 줄이 가장 길어(약 45자 × 12pt) 기존 폭에서는 접혔다.
            // (새 합계: 제목 40 + 음량 92 + 감도 46 + 옵션 줄 34 + 슬롯 줄 34 + 조작 제목 22 +
            //  본문 235 + 닫기 36 = 539, spacing 10 × 11 = 110, padding 상하 40 → 689. 700 안에 들어간다.)
            var box = UIBuilder.CreatePanel(
                backdrop, "SettingsBox",
                anchorMin: new Vector2(0.5f, 0.5f), anchorMax: new Vector2(0.5f, 0.5f),
                offsetMin: new Vector2(-280f, -350f), offsetMax: new Vector2(280f, 350f),
                color: new Color(0f, 0f, 0f, 0.85f));

            var vlg = box.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(24, 24, 20, 20);
            vlg.spacing = 10f;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            vlg.childAlignment = TextAnchor.UpperCenter;

            var title = UIBuilder.CreateText(box, "Title", "설정", 26, Color.white, TextAnchor.MiddleCenter);
            title.gameObject.AddComponent<LayoutElement>().minHeight = 40f;

            sfxValueLabel = UIBuilder.CreateText(box, "SfxLabel", "", 16, Color.white, TextAnchor.MiddleLeft);
            sfxValueLabel.gameObject.AddComponent<LayoutElement>().minHeight = 22f;

            sfxSlider = UIBuilder.CreateSlider(box, "SfxSlider", 0f, 1f, 0f,
                trackColor: new Color(1f, 1f, 1f, 0.15f),
                fillColor: new Color(0.35f, 0.65f, 0.4f, 1f),
                handleColor: Color.white);
            sfxSlider.gameObject.AddComponent<LayoutElement>().minHeight = 24f;
            sfxSlider.onValueChanged.AddListener(OnSfxSliderChanged);

            bgmValueLabel = UIBuilder.CreateText(box, "BgmLabel", "", 16, Color.white, TextAnchor.MiddleLeft);
            bgmValueLabel.gameObject.AddComponent<LayoutElement>().minHeight = 22f;

            bgmSlider = UIBuilder.CreateSlider(box, "BgmSlider", 0f, 1f, 0f,
                trackColor: new Color(1f, 1f, 1f, 0.15f),
                fillColor: new Color(0.35f, 0.65f, 0.4f, 1f),
                handleColor: Color.white);
            bgmSlider.gameObject.AddComponent<LayoutElement>().minHeight = 24f;
            bgmSlider.onValueChanged.AddListener(OnBgmSliderChanged);

            // ── 마우스 감도 (0.3 ~ 3.0, 기본 1.0) ────────────────────────────────────────────
            // 값은 PlayerController.lookSensitivity에 **곱해지는 배율**이다. 씬의 감도 값을 갈아치우지
            // 않으므로 1.0이면 예전과 완전히 같은 회전량이 나온다(PlayerController.HandleLook 주석).
            sensitivityValueLabel = UIBuilder.CreateText(box, "SensitivityLabel", "", 16, Color.white, TextAnchor.MiddleLeft);
            sensitivityValueLabel.gameObject.AddComponent<LayoutElement>().minHeight = 22f;

            sensitivitySlider = UIBuilder.CreateSlider(box, "SensitivitySlider",
                GameSettings.MinMouseSensitivity, GameSettings.MaxMouseSensitivity,
                GameSettings.DefaultMouseSensitivity,
                trackColor: new Color(1f, 1f, 1f, 0.15f),
                fillColor: new Color(0.35f, 0.65f, 0.4f, 1f),
                handleColor: Color.white);
            sensitivitySlider.gameObject.AddComponent<LayoutElement>().minHeight = 24f;
            sensitivitySlider.onValueChanged.AddListener(OnSensitivitySliderChanged);

            // ── Y축 반전 · 난이도 (버튼 두 개를 한 줄에) ─────────────────────────────────────
            var optionRow = CreateRow(box, "OptionRow", 34f);

            invertYButton = UIBuilder.CreateButton(optionRow, "InvertYButton", "", ToggleInvertY);
            invertYLabel = invertYButton.GetComponentInChildren<Text>();
            if (invertYLabel != null)
                invertYLabel.fontSize = 14;

            difficultyButton = UIBuilder.CreateButton(optionRow, "DifficultyButton", "", CycleDifficulty);
            difficultyLabel = difficultyButton.GetComponentInChildren<Text>();
            if (difficultyLabel != null)
                difficultyLabel.fontSize = 14;

            // ── 세이브 슬롯 ────────────────────────────────────────────────────────────────
            // 플레이 중에도 바꿀 수 있어야 한다 - F5/F9가 어느 슬롯에 걸리는지가 이 값이다.
            var slotRow = CreateRow(box, "SlotRow", 34f);

            var slotRowTitle = UIBuilder.CreateText(slotRow, "SlotRowTitle", "세이브 슬롯", 14,
                new Color(0.85f, 0.85f, 0.85f, 1f), TextAnchor.MiddleLeft);
            slotRowTitle.raycastTarget = false;

            int slotCount = SaveLoadController.SlotCount;
            slotButtons = new Button[slotCount];
            slotButtonLabels = new Text[slotCount];
            for (int i = 0; i < slotCount; i++)
            {
                // 클로저가 캡처할 변수는 반복문 안에서 새로 만든다(밖의 i를 캡처하면 전부 마지막 값이 된다).
                int slot = i + 1;
                var slotButton = UIBuilder.CreateButton(slotRow, "SlotButton" + slot, "", () => SelectSlot(slot));

                var slotLabel = slotButton.GetComponentInChildren<Text>();
                if (slotLabel != null)
                    slotLabel.fontSize = 12;

                slotButtons[i] = slotButton;
                slotButtonLabels[i] = slotLabel;
            }

            // 조작 안내. 섹션 제목은 항목 라벨(H2 15), 본문은 Body 12(ArtDirection.md 4.3).
            var controlsTitle = UIBuilder.CreateText(box, "ControlsTitle", "조작", 15, Color.white, TextAnchor.MiddleLeft);
            controlsTitle.gameObject.AddComponent<LayoutElement>().minHeight = 22f;

            controlsLabel = UIBuilder.CreateText(box, "ControlsBody", "", 12,
                new Color(0.85f, 0.85f, 0.85f, 1f), TextAnchor.UpperLeft);
            controlsLabel.lineSpacing = 1.25f;
            controlsLabel.gameObject.AddComponent<LayoutElement>().minHeight = 235f;

            var closeButton = UIBuilder.CreateButton(box, "CloseButton", "닫기", Close);
            var closeLayout = closeButton.gameObject.AddComponent<LayoutElement>();
            closeLayout.minHeight = 36f;
            closeLayout.preferredWidth = 160f;
        }

        /// <summary>
        /// 세로 목록(VerticalLayoutGroup) 안에 "가로로 나란히 놓는 한 줄"을 만든다. 배경은 투명이고
        /// 클릭도 가로채지 않으므로(raycastTarget = false) 자리만 잡아주는 껍데기다.
        /// 설정 항목이 늘어날 때마다 세로로만 쌓으면 창이 화면을 넘어가기 때문에 필요하다.
        /// </summary>
        private static RectTransform CreateRow(RectTransform parent, string name, float height)
        {
            var row = UIBuilder.CreatePanel(parent, name,
                anchorMin: Vector2.zero, anchorMax: Vector2.one,
                offsetMin: Vector2.zero, offsetMax: Vector2.zero,
                color: Color.clear);

            var rowImage = row.GetComponent<Image>();
            if (rowImage != null)
                rowImage.raycastTarget = false;

            var hlg = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 8f;
            hlg.childForceExpandWidth = true;
            hlg.childForceExpandHeight = true;
            hlg.childAlignment = TextAnchor.MiddleCenter;

            row.gameObject.AddComponent<LayoutElement>().minHeight = height;
            return row;
        }

        /// <summary>
        /// 마우스 감도 슬라이더를 움직일 때마다 호출된다. 값은 GameSettings가 PlayerPrefs에 즉시 남기고,
        /// PlayerController는 매 프레임 그 값을 직접 읽으므로 시야 회전에 곧바로 반영된다(다시 열 필요 없음).
        /// </summary>
        private void OnSensitivitySliderChanged(float value)
        {
            GameSettings.MouseSensitivity = value;
            UpdateSensitivityLabel(GameSettings.MouseSensitivity);
        }

        /// <summary>감도 라벨을 갱신한다(문자열 조립은 값이 바뀔 때만 - 매 프레임 하지 않는다).</summary>
        private void UpdateSensitivityLabel(float value)
        {
            if (sensitivityValueLabel != null)
                sensitivityValueLabel.text = $"마우스 감도: {value:0.00}배";
        }

        /// <summary>Y축 반전 토글. 상하 시점만 뒤집는다(PlayerController.HandleLook).</summary>
        private void ToggleInvertY()
        {
            GameSettings.InvertLookY = !GameSettings.InvertLookY;
            RefreshOptionLabels();
        }

        /// <summary>난이도를 쉬움 → 보통 → 어려움 → 쉬움 순으로 돌린다.</summary>
        private void CycleDifficulty()
        {
            GameSettings.CycleDifficulty();
            RefreshOptionLabels();
        }

        /// <summary>Y축 반전/난이도 버튼의 글자를 현재 값으로 다시 쓴다.</summary>
        private void RefreshOptionLabels()
        {
            if (invertYLabel != null)
                invertYLabel.text = GameSettings.InvertLookY ? "Y축 반전: 켜짐" : "Y축 반전: 꺼짐";

            if (difficultyLabel != null)
                difficultyLabel.text = "난이도: " + GameSettings.DifficultyLabel(GameSettings.Difficulty);
        }

        /// <summary>
        /// 세이브 슬롯을 고른다. 고른 슬롯은 PlayerPrefs에 남아 F5/F9와 타이틀 "이어하기", 다음 실행까지
        /// 그대로 이어진다. **슬롯 1은 기존 단일 세이브 파일 그 자체다**(SaveLoadController.SlotFilePath).
        /// </summary>
        private void SelectSlot(int slot)
        {
            GameSettings.SaveSlot = slot;
            RefreshSlotButtons();
            RefreshControlsDisplay(); // 조작 목록의 "저장/불러오기 (슬롯 N)" 표기도 같이 따라간다
        }

        /// <summary>
        /// 슬롯 버튼 3개의 글자(슬롯 번호 + 저장 유무)와 선택 표시를 다시 그린다.
        /// 파일 존재 검사만 하므로(요약을 읽지 않는다) 창을 열 때와 슬롯을 바꿀 때만 도는 이 경로에서는 충분히 싸다.
        /// </summary>
        private void RefreshSlotButtons()
        {
            if (slotButtons == null)
                return;

            int currentSlot = GameSettings.SaveSlot;
            for (int i = 0; i < slotButtons.Length; i++)
            {
                int slot = i + 1;
                bool hasSave = SaveLoadController.SlotHasSave(slot);

                if (slotButtonLabels[i] != null)
                    slotButtonLabels[i].text = $"슬롯 {slot}\n{(hasSave ? "저장됨" : "비었음")}";

                var background = slotButtons[i] != null ? slotButtons[i].GetComponent<Image>() : null;
                if (background != null)
                    background.color = slot == currentSlot ? SlotSelectedColor : SlotIdleColor;
            }
        }

        /// <summary>
        /// 패널을 화면에 보이게 하고, AudioManager의 현재 볼륨 값으로 슬라이더/라벨을 동기화한다.
        /// (기존 OnGUI는 매 프레임 GUI.HorizontalSlider를 그리며 현재 값을 다시 읽었지만, 이 값은
        /// 이 UI를 통해서만 바뀌므로 패널이 열리는 시점에 한 번만 동기화해도 결과는 동일하다.)
        /// </summary>
        private void ShowPanel()
        {
            SetPanelActive(true);
            RefreshVolumeDisplay();
            RefreshGameSettingsDisplay();
            RefreshControlsDisplay();
        }

        /// <summary>
        /// 감도 슬라이더·Y축 반전·난이도·세이브 슬롯을 저장된 현재 값으로 맞춘다.
        /// 창이 열리는 순간에만 돌기 때문에(닫혀 있는 동안 비용 0) 여기서 문자열을 조립해도 안전하다.
        /// SetValueWithoutNotify를 쓰는 이유는 볼륨 슬라이더와 같다 - 값을 맞추는 동작 자체가
        /// 콜백을 다시 불러 GameSettings에 쓰지 않게 한다.
        /// </summary>
        private void RefreshGameSettingsDisplay()
        {
            float sensitivity = GameSettings.MouseSensitivity;
            if (sensitivitySlider != null)
                sensitivitySlider.SetValueWithoutNotify(sensitivity);
            UpdateSensitivityLabel(sensitivity);

            RefreshOptionLabels();
            RefreshSlotButtons();
        }

        /// <summary>
        /// 조작키 목록을 씬의 실제 KeyCode 필드에서 읽어 다시 만든다. 화면이 열리는 순간에만 부르므로
        /// (FindAnyObjectByType가 여러 번 돌지만) 비용은 무시할 수 있고, 대신 씬에서 키를 바꾸면
        /// 다음에 이 화면을 열 때 곧바로 반영된다 - 문자열을 여기 박아두면 어긋나는 날이 온다.
        ///
        /// 컴포넌트를 못 찾은 항목은 각 컴포넌트의 코드 기본값으로 대체한다. 이 화면은 타이틀에서도
        /// 열리는데, 그때 플레이 오브젝트가 아직 없을 수 있기 때문이다.
        /// </summary>
        private void RefreshControlsDisplay()
        {
            if (controlsLabel == null)
                return;

            var interaction = FindAnyObjectByType<InteractionController>();
            var inventoryUI = FindAnyObjectByType<InventoryUI>();
            var craftingUI = FindAnyObjectByType<CraftingUI>();
            var minimapUI = FindAnyObjectByType<MinimapUI>();
            var questUI = FindAnyObjectByType<QuestUI>();   // [B24] 퀘스트 창 신설
            var playerController = FindAnyObjectByType<PlayerController>();
            var saveLoad = FindAnyObjectByType<SaveLoadController>();

            KeyCode interact = interaction != null ? interaction.interactKey : KeyCode.E;
            KeyCode cook = interaction != null ? interaction.cookKey : KeyCode.R;
            KeyCode consume = interaction != null ? interaction.consumeKey : KeyCode.C;
            KeyCode place = interaction != null ? interaction.placeKey : KeyCode.G;
            KeyCode inventory = inventoryUI != null ? inventoryUI.toggleKey : KeyCode.Tab;
            KeyCode filter = inventoryUI != null ? inventoryUI.cycleFilterKey : KeyCode.F;
            KeyCode craft = craftingUI != null ? craftingUI.toggleKey : KeyCode.V;
            KeyCode map = minimapUI != null ? minimapUI.toggleKey : KeyCode.M;
            KeyCode quest = questUI != null ? questUI.toggleKey : KeyCode.J;
            KeyCode build = BuildingSystem.Instance != null ? BuildingSystem.Instance.toggleKey : KeyCode.B;
            KeyCode dive = playerController != null ? playerController.diveKey : KeyCode.LeftControl;
            // [전투 확장] 회피 구르기 / 창 투척. 다른 줄과 같은 규약으로 **씬의 실제 필드에서** 읽는다
            // (PlayerController.dodgeKey = X, throwKey = T, throwMouseButton = 1(우클릭)이 코드 기본값).
            KeyCode dodge = playerController != null ? playerController.dodgeKey : KeyCode.X;
            KeyCode throwAlt = playerController != null ? playerController.throwKey : KeyCode.T;
            int throwMouse = playerController != null ? playerController.throwMouseButton : 1;
            // [낚시] 같은 규약 - 씬의 실제 필드에서 읽는다(PlayerController.fishingKey = Z가 코드 기본값).
            KeyCode fishing = playerController != null ? playerController.fishingKey : KeyCode.Z;

            controlsBuilder.Clear();
            controlsBuilder.Append("이동 WASD · 시점 마우스 · 점프 Space\n");
            controlsBuilder.Append('[').Append(interact.ToString()).Append("] 상호작용 / 공격(무기 필요)\n");
            // [전투 확장] 회피와 투척은 상호작용/공격 바로 아래에 둔다 - 셋 다 "곰을 만났을 때 쓰는 것"이라
            // 한 덩어리로 읽혀야 한다. 투척은 마우스 버튼이 주력이고 T는 보조라 마우스를 먼저 적는다
            // (throwMouseButton이 음수면 마우스 투척이 꺼진 상태이므로 보조 키만 적는다).
            controlsBuilder.Append('[').Append(dodge.ToString()).Append("] 회피 구르기    ");
            if (throwMouse >= 0)
                controlsBuilder.Append('[').Append(DescribeMouseButton(throwMouse)).Append("]/");
            controlsBuilder.Append('[').Append(throwAlt.ToString()).Append("] 창 투척(창 필요)\n");
            controlsBuilder.Append('[').Append(consume.ToString()).Append("] 섭취 · 치료    [")
                .Append(cook.ToString()).Append("] 조리    [").Append(place.ToString()).Append("] 설치\n");
            // [낚시] 한 키로 "던지기 → 입질 때 챔질" 두 단계를 다 하므로 그 사실까지 한 줄에 적는다.
            // (키 판정과 단계 구분은 전부 FishingSystem이 한다 - 여기는 안내만.)
            controlsBuilder.Append('[').Append(fishing.ToString()).Append("] 낚시 (물가에서 던지고, 입질이 오면 다시 [")
                .Append(fishing.ToString()).Append("])\n");
            controlsBuilder.Append('[').Append(inventory.ToString()).Append("] 인벤토리 (열린 상태에서 [")
                .Append(filter.ToString()).Append("] 분류 전환)\n");
            controlsBuilder.Append('[').Append(craft.ToString()).Append("] 제작    [").Append(map.ToString()).Append("] 세계 지도 / 이동\n");
            // [B24] 퀘스트 창. 지도 설명도 함께 고쳤다 - 배치 21에서 섬 목록 패널이 세계 지도로 흡수돼
            // "섬 목록 / 이동"은 더 이상 존재하지 않는 창을 가리키고 있었다.
            controlsBuilder.Append('[').Append(quest.ToString()).Append("] 퀘스트 (할 일)    [")
                .Append(build.ToString()).Append("] 건축\n");
            // [B25] 건축 모드는 조작이 따로 놀아서 한 줄을 더 준다.
            controlsBuilder.Append("건축 중 — 1~4 부품 · 휠/[Q] 회전 · 좌클릭 설치 · 우클릭 철거\n");
            // 수영은 지상 조작과 키가 겹쳐(Space) 따로 묶어주지 않으면 오해를 산다.
            // [0.2.22] 보조 잠수 키 병기 - "잠수 키가 안 된다" 보고의 견고책(PlayerController 주석).
            KeyCode diveAlt = playerController != null ? playerController.diveKeyAlt : KeyCode.LeftShift;
            controlsBuilder.Append("수영 중 — Space 떠오르기 · [").Append(dive.ToString())
                .Append("]/[").Append(diveAlt.ToString()).Append("] 잠수\n");

            // [슬롯 3개] 저장/불러오기가 **어느 슬롯에 걸리는지**까지 같이 적는다 - 슬롯이 생기고 나서
            // 가장 위험한 오해가 "F5를 눌렀는데 다른 판에 덮어썼다"이기 때문이다.
            if (saveLoad != null)
                controlsBuilder.Append('[').Append(saveLoad.saveKey.ToString()).Append("] 저장    [")
                    .Append(saveLoad.loadKey.ToString()).Append("] 불러오기    (슬롯 ")
                    .Append(SaveLoadController.CurrentSlot).Append(")\n");

            controlsBuilder.Append('[').Append(toggleKey.ToString()).Append("] 이 화면 열기 / 닫기");

            controlsLabel.text = controlsBuilder.ToString();
        }

        /// <summary>
        /// 마우스 버튼 번호(Input.GetMouseButton 규약: 0 = 좌, 1 = 우, 2 = 휠)를 한글 표기로 바꾼다.
        /// 키보드 키가 KeyCode.ToString()으로 저절로 읽히는 것과 달리 마우스 버튼은 그냥 정수라
        /// 이름을 여기서 붙여줘야 한다. 알 수 없는 번호는 번호 그대로 보여준다(거짓 이름보다 낫다).
        /// </summary>
        private static string DescribeMouseButton(int button)
        {
            switch (button)
            {
                case 0: return "좌클릭";
                case 1: return "우클릭";
                case 2: return "휠클릭";
                default: return "마우스" + button.ToString();
            }
        }

        /// <summary>
        /// 패널을 활성/비활성화한다.
        /// </summary>
        private void SetPanelActive(bool active)
        {
            if (panelRoot != null)
                panelRoot.SetActive(active);
        }

        /// <summary>
        /// AudioManager.Instance의 현재 효과음/배경음 볼륨을 읽어 슬라이더와 라벨에 반영한다.
        /// SetValueWithoutNotify를 써서, 여기서 값을 맞추는 동작 자체가 OnSfxSliderChanged 등을 다시
        /// 불러 AudioManager.SetXxxVolume을 불필요하게 재호출하지 않게 한다.
        /// </summary>
        private void RefreshVolumeDisplay()
        {
            var audio = AudioManager.Instance;
            float currentSfx = audio != null ? audio.sfxVolume : 0f;
            float currentBgm = audio != null ? audio.bgmVolume : 0f;

            sfxSlider.SetValueWithoutNotify(currentSfx);
            bgmSlider.SetValueWithoutNotify(currentBgm);
            sfxValueLabel.text = $"효과음 볼륨: {currentSfx:P0}";
            bgmValueLabel.text = $"배경음 볼륨: {currentBgm:P0}";
        }

        /// <summary>
        /// 효과음 슬라이더를 드래그할 때마다 호출된다. AudioManager에 즉시 반영하고 라벨을 갱신한다.
        /// </summary>
        private void OnSfxSliderChanged(float value)
        {
            AudioManager.Instance?.SetSfxVolume(value);
            sfxValueLabel.text = $"효과음 볼륨: {value:P0}";
        }

        /// <summary>
        /// 배경음 슬라이더를 드래그할 때마다 호출된다. AudioManager에 즉시 반영하고 라벨을 갱신한다.
        /// </summary>
        private void OnBgmSliderChanged(float value)
        {
            AudioManager.Instance?.SetBgmVolume(value);
            bgmValueLabel.text = $"배경음 볼륨: {value:P0}";
        }
    }
}
