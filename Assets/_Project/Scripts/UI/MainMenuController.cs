using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using MakeGame.Player;
using MakeGame.Systems;

namespace MakeGame.UI
{
    /// <summary>
    /// 게임 시작 시 표시되는 타이틀(메인 메뉴) 화면을 담당한다.
    /// 씬은 바로 로드되어 섬/월드가 생성되지만, 플레이어가 "시작하기"를 누르기 전까지는
    /// 시간을 멈추고 이동/상호작용 조작을 막아 준비되지 않은 상태에서 조작이 들어가지 않게 한다.
    /// 개선(B2-13): OnGUI(레거시 IMGUI)로 직접 그리던 것을 UIBuilder 기반 UGUI로 옮겼다. IMGUI는
    /// Screen Space Overlay Canvas보다 항상 나중에(최상단에) 그려져 다른 UGUI 화면을 가려버리는 문제가
    /// 있었기 때문에(GameOverController.OnGUI 사례 참고) OnGUI를 완전히 제거했다. Time.timeScale = 0인
    /// 동안에도 UGUI 버튼 클릭은 EventSystem이 Time.deltaTime과 무관하게 처리하므로 정상 동작한다
    /// (GameOverUI에서 이미 검증된 패턴과 동일).
    /// SettingsMenuController도 이제 스스로 자기 캔버스를 갖고 있어, 예전처럼 이 클래스가 설정 화면을
    /// 대신 그려줄 필요가 없다 - "설정" 버튼은 그냥 settingsMenu.Open()만 호출한다.
    ///
    /// 배치 27: 타이틀 아트(Resources/UI/MainMenu_Background)를 화면 전체에 aspect-fill로 깔고,
    /// 그림에 그려져 있는 우측 버튼 4개 자리에 **실제로 동작하는 불투명 버튼**을 정확히 겹쳐 놓는다.
    /// 제목/부제목 글자는 그림에 이미 박혀 있으므로 코드로 다시 그리지 않는다. 아트를 못 불러오면
    /// (임포트 실패 등) 예전의 단색 배경 + 세로 버튼 화면으로 조용히 되돌아간다(BuildFallbackMenu).
    /// </summary>
    public class MainMenuController : MonoBehaviour
    {
        [Tooltip("타이틀 화면에 표시할 게임 제목 (타이틀 아트를 못 불러왔을 때의 폴백 화면에서만 쓰인다)")]
        public string gameTitle = "무인도 탈출";

        [Tooltip("타이틀 화면에 표시할 부제목 (타이틀 아트를 못 불러왔을 때의 폴백 화면에서만 쓰인다)")]
        public string gameSubtitle = "Stranded Deep 스타일 서바이벌";

        [Tooltip("시작 전 비활성화할 이동/시점 컨트롤러")]
        public PlayerController playerController;

        [Tooltip("시작 전 비활성화할 상호작용 컨트롤러")]
        public InteractionController interactionController;

        [Tooltip("메뉴에서 '설정' 버튼을 눌렀을 때 열고 닫을 설정 화면 (비워두면 설정 버튼을 표시하지 않는다)")]
        public SettingsMenuController settingsMenu;

        [Tooltip("'이어하기'가 호출할 불러오기 시스템. 비워두면 시작할 때 씬에서 자동으로 찾는다.")]
        public SaveLoadController saveLoadController;

        /// <summary>현재 타이틀 화면이 표시 중인지 여부.</summary>
        public bool isMenuOpen = true;

        /// <summary>
        /// 우측 하단에 표시할 버전 문자열. 프로젝트 루트의 VERSION 파일이 진실이지만 런타임에는 읽을 수
        /// 없으므로 여기 한 곳에만 둔다(VERSION이 올라가면 이 값도 같이 올린다). PlayerSettings의
        /// bundleVersion이 VERSION과 동기화되어 있다면 Application.version으로 바꿔도 된다.
        /// </summary>
        public const string DisplayVersion = "0.2.66";

        // [슬롯 3개] 예전에는 저장 파일 이름("makegame_save.json")을 여기에 복사해 두고 직접 File.Exists로
        // 확인했다. 같은 문자열이 두 파일에 살아 있는 것은 이 프로젝트가 반복해서 낸 사고 유형이라,
        // 슬롯이 생기면서 판정을 파일의 주인(SaveLoadController.SlotHasSave)에게 넘겼다.
        // 이제 이 파일에는 저장 경로 지식이 한 글자도 없다.

        /// <summary>세이브 슬롯 버튼들(길이 = SaveLoadController.SlotCount). 아트/폴백 화면 양쪽에서 쓴다.</summary>
        private Button[] slotButtons;
        private Text[] slotButtonLabels;

        // 슬롯을 바꾸면 "이어하기" 버튼의 활성/잠금 표시도 같이 바뀌어야 하므로 참조를 들고 있는다.
        private Button continueButton;
        private Image continueBorder;
        private Text continueKoreanLabel;
        private Text continueEnglishLabel;
        private GameObject continueLockIcon;

        /// <summary>슬롯 버튼의 평소 색과 "지금 쓰는 슬롯" 색.</summary>
        private static readonly Color SlotIdleColor = new Color(0.20f, 0.26f, 0.24f, 1f);
        private static readonly Color SlotSelectedColor = new Color(0.25f, 0.55f, 0.3f, 1f);

        // ── 타이틀 아트에 그려져 있는 버튼 4개의 자리(정규화 좌표, 원점 좌하단). 픽셀이 아니라 비율이므로
        //    화면 비율이 바뀌어 아트가 잘려도 우리 버튼은 그림 속 버튼과 함께 움직인다.
        private const float ButtonXMin = 0.730f;
        private const float ButtonXMax = 0.945f;
        private const float NewGameYMin = 0.538f;
        private const float NewGameYMax = 0.629f;
        private const float ContinueYMin = 0.411f;
        private const float ContinueYMax = 0.501f;
        private const float SettingsYMin = 0.285f;
        private const float SettingsYMax = 0.374f;
        private const float QuitYMin = 0.158f;
        private const float QuitYMax = 0.246f;

        // ── 녹슨 금속 버튼 팔레트. 그림 속 버튼을 완전히 덮어야 하므로 알파는 전부 1이다.
        private static readonly Color MetalNormalColor = new Color(0.27f, 0.23f, 0.19f, 1f);
        private static readonly Color MetalHoverColor = new Color(0.46f, 0.39f, 0.31f, 1f);
        private static readonly Color MetalPressedColor = new Color(0.19f, 0.16f, 0.13f, 1f);
        private static readonly Color MetalLockedColor = new Color(0.17f, 0.16f, 0.15f, 1f);
        private static readonly Color BorderIdleColor = new Color(0.60f, 0.53f, 0.40f, 1f);
        private static readonly Color BorderHoverColor = new Color(0.96f, 0.86f, 0.60f, 1f);
        private static readonly Color BorderLockedColor = new Color(0.30f, 0.28f, 0.25f, 1f);
        private static readonly Color LabelKoreanColor = new Color(0.96f, 0.93f, 0.86f, 1f);
        private static readonly Color LabelEnglishColor = new Color(0.79f, 0.75f, 0.66f, 0.85f);
        private static readonly Color LabelKoreanLockedColor = new Color(0.62f, 0.60f, 0.56f, 1f);
        private static readonly Color LabelEnglishLockedColor = new Color(0.50f, 0.49f, 0.46f, 0.85f);

        /// <summary>
        /// 타이틀 화면 배경 이미지 (Resources/UI/title_background.png, 섬 컨셉 아트).
        /// 그동안 단색 배경만 써서 밋밋했던 문제를 개선한다. 로드 실패 시 기존 단색 배경으로 자동 대체된다.
        /// </summary>
        private Texture2D backgroundTexture;

        /// <summary>
        /// 감독이 그린 타이틀 아트(Resources/UI/MainMenu_Background, 1376x768). 제목·저작권 줄·버튼 그림이
        /// 전부 이 한 장에 들어 있다. null이면 예전 화면으로 폴백한다.
        /// </summary>
        private Sprite backgroundSprite;

        private GameObject panelRoot;

        /// <summary>
        /// 시작하자마자 조작을 막고 시간을 멈춰 타이틀 화면 상태로 진입한다.
        /// 섬/월드 생성(WorldMapManager 등)은 Time.timeScale과 무관하게 Awake/Start에서 그대로 진행되므로,
        /// 플레이어가 "시작하기"를 누르는 순간 이미 준비된 월드로 바로 들어갈 수 있다.
        /// </summary>
        private void Start()
        {
            if (!isMenuOpen)
                return;

            SetGameplayEnabled(false);
            Time.timeScale = 0f;

            // 타이틀 아트/배경 이미지를 미리 한 번만 로드해둔다.
            backgroundSprite = Resources.Load<Sprite>("UI/MainMenu_Background");
            backgroundTexture = Resources.Load<Texture2D>("UI/title_background");

            BuildUI();
        }

        /// <summary>
        /// 이동/상호작용 컨트롤러를 한꺼번에 켜거나 끈다.
        /// </summary>
        private void SetGameplayEnabled(bool enabled)
        {
            if (playerController != null)
                playerController.enabled = enabled;

            if (interactionController != null)
                interactionController.enabled = enabled;
        }

        /// <summary>
        /// "시작하기"("새 게임") 버튼을 눌렀을 때 호출한다. 타이틀 화면을 닫고 시간을 다시 흐르게 하며
        /// 조작을 활성화한다. 이어하기도 이 흐름을 그대로 통과한 뒤 불러오기만 덧붙인다.
        /// </summary>
        private void StartGame()
        {
            isMenuOpen = false;
            Time.timeScale = 1f;
            SetGameplayEnabled(true);

            if (panelRoot != null)
                panelRoot.SetActive(false);
        }

        /// <summary>
        /// "이어하기" 버튼. 새 게임과 똑같이 게임을 시작한 뒤(시간·조작 복구) 곧바로 저장 파일을 불러온다.
        /// SaveLoadController.Load()는 Time.timeScale에 의존하지 않지만, 불러온 뒤 바로 조작이 가능해야
        /// 하므로 순서는 반드시 "시작 → 불러오기"다.
        /// </summary>
        private void ContinueGame()
        {
            StartGame();

            var loader = ResolveSaveLoadController();
            if (loader != null)
                loader.Load();
            else
                Debug.LogWarning("[MainMenuController] SaveLoadController를 찾지 못해 이어하기를 처리하지 못했습니다.");
        }

        /// <summary>
        /// "종료" 버튼을 눌렀을 때 호출한다. 에디터에서는 Play 모드를 종료하고, 빌드에서는 애플리케이션을 종료한다.
        /// </summary>
        private void QuitGame()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        /// <summary>
        /// 이어하기가 쓸 SaveLoadController를 구한다. 인스펙터 연결이 있으면 그것을, 없으면 씬에서 한 번만
        /// 찾아 캐시한다(씬에 1개 존재 - SampleScene.unity의 SaveLoadController 컴포넌트).
        /// </summary>
        private SaveLoadController ResolveSaveLoadController()
        {
            if (saveLoadController == null)
                saveLoadController = Object.FindAnyObjectByType<SaveLoadController>();

            return saveLoadController;
        }

        /// <summary>
        /// 지금 고른 슬롯에 이어할 저장이 있는지 판정한다. 판정은 파일의 주인인 SaveLoadController가 한다
        /// (본 파일이 깨졌을 때 .bak으로 폴백하는 Load의 규칙까지 그쪽 한 곳에 모여 있다).
        /// </summary>
        private static bool HasSaveFile()
        {
            return SaveLoadController.SlotHasSave(GameSettings.SaveSlot);
        }

        /// <summary>
        /// [슬롯 3개] 슬롯을 고른다. 고른 슬롯은 PlayerPrefs에 남아 F5/F9와 다음 실행까지 그대로 이어진다.
        /// </summary>
        private void SelectSlot(int slot)
        {
            GameSettings.SaveSlot = slot;
            RefreshSlotButtons();
        }

        /// <summary>
        /// [슬롯 3개] 슬롯 버튼 3개의 라벨(요약)과 선택 표시, 그리고 "이어하기" 버튼 상태를 다시 그린다.
        /// **화면을 만들 때와 슬롯을 바꿀 때만 호출한다** - 안에서 세이브 파일을 읽으므로 매 프레임 호출 금지다.
        /// </summary>
        private void RefreshSlotButtons()
        {
            if (slotButtons == null)
                return;

            // 타이틀이 이미 닫힌 뒤(플레이 중 설정 화면에서 슬롯을 바꾼 경우)에는 다시 그릴 화면이 없다.
            // 여기서 막지 않으면 슬롯을 바꿀 때마다 보이지도 않는 패널 때문에 세이브 파일 3개를 다시 읽는다.
            if (!isMenuOpen)
                return;

            var loader = ResolveSaveLoadController();
            int currentSlot = GameSettings.SaveSlot;

            for (int i = 0; i < slotButtons.Length; i++)
            {
                int slot = i + 1;
                bool hasSave;
                string summary = null;

                // 요약(경과 일수·발견한 섬·저장 시각)은 세이브를 읽을 수 있는 쪽이 만든다. 씬에
                // SaveLoadController가 없으면(있어야 정상) 존재 여부만이라도 알려준다.
                if (loader != null)
                    hasSave = loader.TryGetSlotSummary(slot, out summary);
                else
                    hasSave = SaveLoadController.SlotHasSave(slot);

                string body = hasSave
                    ? (string.IsNullOrEmpty(summary) ? "저장됨" : summary)
                    : "비어 있음";

                if (slotButtonLabels[i] != null)
                    slotButtonLabels[i].text = slot == currentSlot
                        ? $"슬롯 {slot} (사용 중)\n{body}"
                        : $"슬롯 {slot}\n{body}";

                var background = slotButtons[i] != null ? slotButtons[i].GetComponent<Image>() : null;
                if (background != null)
                    background.color = slot == currentSlot ? SlotSelectedColor : SlotIdleColor;
            }

            RefreshContinueButton();
        }

        /// <summary>
        /// [슬롯 3개] 고른 슬롯이 비어 있으면 "이어하기"를 잠근다(그림과 같은 어두운 바탕 + 자물쇠).
        /// 예전에는 화면을 만들 때 한 번만 판정했지만, 이제 슬롯을 바꿀 때마다 다시 판정해야 한다.
        /// </summary>
        private void RefreshContinueButton()
        {
            if (continueButton == null)
                return;

            bool hasSave = HasSaveFile();
            continueButton.interactable = hasSave;

            if (continueBorder != null)
                continueBorder.color = hasSave ? BorderIdleColor : BorderLockedColor;

            if (continueKoreanLabel != null)
                continueKoreanLabel.color = hasSave ? LabelKoreanColor : LabelKoreanLockedColor;

            if (continueEnglishLabel != null)
                continueEnglishLabel.color = hasSave ? LabelEnglishColor : LabelEnglishLockedColor;

            if (continueLockIcon != null)
                continueLockIcon.SetActive(!hasSave);
        }

        /// <summary>
        /// [슬롯 3개] 화면 좌하단에 슬롯 선택 패널을 만든다.
        ///
        /// **타이틀 아트(artRt)가 아니라 화면 전체 패널(root)에 붙인다.** 아트는 화면 비율에 따라 좌우가
        /// 잘리도록(EnvelopeParent) 늘어나므로, 아트를 부모로 삼으면 좁은 화면에서 이 패널이 화면 밖으로
        /// 밀려난다. 그림 속 버튼 자리에 겹쳐야 하는 오른쪽 버튼 4개와 달리, 이 패널은 아트에 대응하는
        /// 그림이 없는 새 UI라 화면 기준으로 두는 것이 맞다.
        /// </summary>
        private void BuildSlotSelector(RectTransform root)
        {
            var panel = UIBuilder.CreatePanel(root, "SlotSelector",
                anchorMin: new Vector2(0.030f, 0.070f), anchorMax: new Vector2(0.345f, 0.360f),
                offsetMin: Vector2.zero, offsetMax: Vector2.zero,
                color: new Color(0.05f, 0.08f, 0.12f, 0.85f), addTopBorder: true);

            var vlg = panel.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(14, 14, 12, 12);
            vlg.spacing = 6f;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            vlg.childAlignment = TextAnchor.UpperCenter;

            var title = UIBuilder.CreateText(panel, "SlotTitle", "세이브 슬롯 (F5 저장 / F9 불러오기)", 15,
                new Color(0.90f, 0.88f, 0.82f, 1f), TextAnchor.MiddleLeft);
            title.gameObject.AddComponent<LayoutElement>().minHeight = 24f;

            int slotCount = SaveLoadController.SlotCount;
            slotButtons = new Button[slotCount];
            slotButtonLabels = new Text[slotCount];

            for (int i = 0; i < slotCount; i++)
            {
                // 클로저가 캡처할 지역 변수는 반복문 안에서 새로 만든다(밖의 i를 캡처하면 전부 마지막 값이 된다).
                int slot = i + 1;
                var button = UIBuilder.CreateButton(panel, "SlotButton" + slot, "", () => SelectSlot(slot));
                button.gameObject.AddComponent<LayoutElement>().minHeight = 44f;

                var label = button.GetComponentInChildren<Text>();
                if (label != null)
                {
                    label.fontSize = 14;
                    label.alignment = TextAnchor.MiddleCenter;
                }

                slotButtons[i] = button;
                slotButtonLabels[i] = label;
            }

            RefreshSlotButtons();
        }

        /// <summary>
        /// 설정 화면에서도 슬롯을 바꿀 수 있으므로(플레이 중 F5 대상 변경), 그쪽에서 바뀐 값이 타이틀
        /// 화면에도 즉시 반영되도록 구독한다. 설정을 아무도 만지지 않으면 한 번도 발행되지 않아 비용이 0이다.
        /// </summary>
        private void OnEnable()
        {
            GameSettings.Changed += RefreshSlotButtons;
        }

        /// <summary>정적 이벤트 구독을 반드시 푼다(파괴된 컴포넌트가 계속 불려 나오는 것을 막는다).</summary>
        private void OnDisable()
        {
            GameSettings.Changed -= RefreshSlotButtons;
        }

        /// <summary>
        /// 캔버스와 타이틀 화면을 만든다. 타이틀 아트가 있으면 아트 화면(BuildTitleArtMenu),
        /// 없으면 예전 단색 화면(BuildFallbackMenu)으로 간다.
        /// </summary>
        private void BuildUI()
        {
            // SettingsCanvas(16)보다 아래에 있어야, 설정 화면이 열렸을 때 그 뒤로 자연스럽게 가려진다.
            var canvas = UIBuilder.CreateCanvas("MainMenuCanvas", sortOrder: 15);

            var root = UIBuilder.CreatePanel(
                canvas.transform, "MainMenuPanel",
                anchorMin: Vector2.zero, anchorMax: Vector2.one,
                offsetMin: Vector2.zero, offsetMax: Vector2.zero,
                color: Color.clear);
            panelRoot = root.gameObject;

            if (backgroundSprite != null)
                BuildTitleArtMenu(root);
            else
                BuildFallbackMenu(root);
        }

        /// <summary>
        /// 감독이 그린 타이틀 아트를 화면에 꽉 채우고(비율 유지 + 넘치는 쪽은 잘림), 그림 속 버튼 자리에
        /// 진짜 버튼을 겹쳐 놓는다. 버튼들은 **배경 이미지의 RectTransform을 부모로** 삼고 정규화 앵커만
        /// 쓰기 때문에, 화면 비율이 달라져 아트가 잘려도 그림 속 버튼과 항상 같은 자리에 붙어 있다.
        /// </summary>
        private void BuildTitleArtMenu(RectTransform root)
        {
            var artGo = new GameObject("TitleArt", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(AspectRatioFitter));
            artGo.transform.SetParent(root, false);

            var artRt = artGo.GetComponent<RectTransform>();
            artRt.anchorMin = Vector2.zero;
            artRt.anchorMax = Vector2.one;
            artRt.offsetMin = Vector2.zero;
            artRt.offsetMax = Vector2.zero;

            var artImage = artGo.GetComponent<Image>();
            artImage.sprite = backgroundSprite;
            artImage.color = Color.white;
            artImage.type = Image.Type.Simple;
            artImage.raycastTarget = false; // 배경이 클릭을 먹지 않도록(버튼만 받게)

            // EnvelopeParent = "부모를 덮되 비율은 유지" (넘치는 쪽이 잘린다). AspectRatioFitter가 앵커와
            // sizeDelta를 스스로 다시 세팅하므로, 위에서 준 스트레치 앵커는 시작값일 뿐이다.
            var fitter = artGo.GetComponent<AspectRatioFitter>();
            fitter.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
            float artHeight = backgroundSprite.rect.height;
            fitter.aspectRatio = artHeight > 0f ? backgroundSprite.rect.width / artHeight : 16f / 9f;

            bool hasSave = HasSaveFile();

            CreateMenuButton(artRt, "NewGameButton", "새 게임", "NEW GAME",
                NewGameYMin, NewGameYMax, true, StartGame);

            continueButton = CreateMenuButton(artRt, "ContinueButton", "이어하기", "CONTINUE",
                ContinueYMin, ContinueYMax, hasSave, ContinueGame);

            // [슬롯 3개] 슬롯을 바꾸면 이 버튼의 잠금 상태가 그때그때 달라지므로, 갱신에 필요한
            // 부품(테두리·라벨 2줄·자물쇠)의 참조를 들고 있는다. 자물쇠는 "없을 때만 만들던 것"을
            // **항상 만들어 두고 보였다 숨겼다** 하는 방식으로 바꿨다 - 슬롯을 바꿀 때마다 오브젝트를
            // 만들고 지우면 그때부터 자물쇠가 두 개 겹치거나 사라지는 경로가 생긴다.
            continueBorder = continueButton.GetComponent<Image>();

            var continueTexts = continueButton.GetComponentsInChildren<Text>(true);
            for (int i = 0; i < continueTexts.Length; i++)
            {
                if (continueTexts[i].gameObject.name == "LabelEn")
                    continueEnglishLabel = continueTexts[i];
                else if (continueKoreanLabel == null)
                    continueKoreanLabel = continueTexts[i];
            }

            continueLockIcon = CreateLockIcon(continueButton.GetComponent<RectTransform>());
            continueLockIcon.SetActive(!hasSave);

            // settingsMenu가 비어 있으면 그림 속 설정 버튼을 눌러도 열 것이 없으므로 잠긴 상태로 덮는다.
            bool hasSettings = settingsMenu != null;
            UnityEngine.Events.UnityAction openSettings = null;
            if (hasSettings)
                openSettings = settingsMenu.Open;

            CreateMenuButton(artRt, "SettingsButton", "설정", "SETTINGS",
                SettingsYMin, SettingsYMax, hasSettings, openSettings);

            CreateMenuButton(artRt, "QuitButton", "나가기", "QUIT",
                QuitYMin, QuitYMax, true, QuitGame);

            CreateVersionLabel(artRt);

            // [슬롯 3개] 왼쪽 아래에 슬롯 선택 패널. 그림 속 버튼 열(오른쪽)과 겹치지 않는 빈 자리다.
            BuildSlotSelector(root);
        }

        /// <summary>
        /// 그림 속 버튼 한 칸을 완전히 덮는 불투명 금속 버튼을 만든다. 구조는
        /// [바깥 Image = 밝은 테두리] → [Fill = 녹슨 금속 바탕(Button의 targetGraphic)] → [한글/영문 2줄 라벨].
        /// 테두리를 Outline 컴포넌트가 아니라 별도의 바깥 Image로 만든 이유: Outline은 같은 그래픽의 메시에
        /// 얹히기 때문에 Button의 ColorTint(어두운 금속색)가 테두리까지 어둡게 곱해버린다.
        /// </summary>
        private Button CreateMenuButton(RectTransform parent, string name, string korean, string english,
            float yMin, float yMax, bool interactable, UnityEngine.Events.UnityAction onClick)
        {
            var button = UIBuilder.CreateButton(parent, name, korean, onClick);

            var rt = button.GetComponent<RectTransform>();
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchorMin = new Vector2(ButtonXMin, yMin);
            rt.anchorMax = new Vector2(ButtonXMax, yMax);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            // 바깥 Image = 테두리. 알파 1이라 그림 속 버튼이 테두리 틈으로 비치지 않는다.
            var border = button.GetComponent<Image>();
            border.color = interactable ? BorderIdleColor : BorderLockedColor;

            // 안쪽 바탕. 2px 안쪽으로 들어가 있어 바깥 Image가 테두리처럼 보인다.
            var fill = UIBuilder.CreatePanel(rt, "Fill",
                anchorMin: Vector2.zero, anchorMax: Vector2.one,
                offsetMin: new Vector2(2f, 2f), offsetMax: new Vector2(-2f, -2f),
                color: Color.white);
            fill.SetAsFirstSibling(); // 라벨보다 먼저 그려져야 글자를 덮지 않는다

            var fillImage = fill.GetComponent<Image>();
            fillImage.raycastTarget = false;

            // ColorTint가 fill의 흰색에 곱해지므로, 실제 금속색은 전부 ColorBlock 쪽에 있다.
            // targetGraphic은 코드로 AddComponent한 Button에는 자동으로 채워지지 않으므로 반드시 직접 지정한다.
            button.transition = Selectable.Transition.ColorTint;
            button.targetGraphic = fillImage;

            var colors = button.colors;
            colors.normalColor = MetalNormalColor;
            colors.highlightedColor = MetalHoverColor;
            colors.pressedColor = MetalPressedColor;
            colors.selectedColor = MetalNormalColor; // 클릭 후 계속 밝게 남아 있는 것 방지
            colors.disabledColor = MetalLockedColor;
            colors.colorMultiplier = 1f;
            colors.fadeDuration = 0.08f;
            button.colors = colors;
            button.interactable = interactable;

            // UIBuilder가 만들어 둔 라벨을 그대로 한글 줄로 쓰고, 영문 줄만 하나 더 붙인다.
            // 이 시점에 버튼 아래 Text는 CreateButton이 만든 "Label" 하나뿐이다(영문 줄은 아래에서 추가한다).
            var koreanLabel = button.GetComponentInChildren<Text>();
            koreanLabel.fontSize = 30;
            koreanLabel.color = interactable ? LabelKoreanColor : LabelKoreanLockedColor;
            koreanLabel.raycastTarget = false;
            koreanLabel.horizontalOverflow = HorizontalWrapMode.Overflow;
            var koreanRt = koreanLabel.rectTransform;
            koreanRt.anchorMin = new Vector2(0f, 0.40f);
            koreanRt.anchorMax = new Vector2(1f, 0.94f);
            koreanRt.offsetMin = Vector2.zero;
            koreanRt.offsetMax = Vector2.zero;
            AddTextShadow(koreanLabel);

            var englishLabel = UIBuilder.CreateText(rt, "LabelEn", english, 15,
                interactable ? LabelEnglishColor : LabelEnglishLockedColor, TextAnchor.MiddleCenter);
            englishLabel.raycastTarget = false;
            englishLabel.horizontalOverflow = HorizontalWrapMode.Overflow;
            var englishRt = englishLabel.rectTransform;
            englishRt.anchorMin = new Vector2(0f, 0.06f);
            englishRt.anchorMax = new Vector2(1f, 0.42f);
            englishRt.offsetMin = Vector2.zero;
            englishRt.offsetMax = Vector2.zero;
            AddTextShadow(englishLabel);

            if (interactable)
                AttachBorderGlow(button, border);

            return button;
        }

        /// <summary>
        /// 마우스를 올리면 테두리가 은은하게 밝아지게 한다(그림의 "새 게임" 버튼과 같은 느낌).
        /// 바탕색은 Button의 ColorTint가 처리하고, 여기서는 ColorTint가 건드리지 않는 바깥 테두리만 바꾼다.
        /// EventTrigger는 Unity 내장이라 새 MonoBehaviour를 만들지 않아도 되고, 콜백은 화면을 만들 때
        /// 한 번만 등록되므로 매 프레임 할당이 없다.
        /// </summary>
        private void AttachBorderGlow(Button button, Image border)
        {
            var trigger = button.gameObject.AddComponent<EventTrigger>();

            var enter = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
            enter.callback.AddListener(_ =>
            {
                if (button.interactable)
                    border.color = BorderHoverColor;
            });
            trigger.triggers.Add(enter);

            var exit = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit };
            exit.callback.AddListener(_ =>
            {
                border.color = button.interactable ? BorderIdleColor : BorderLockedColor;
            });
            trigger.triggers.Add(exit);
        }

        /// <summary>
        /// 어두운 금속 바탕 위에서도 글자가 뭉개지지 않게 얇은 그림자를 깐다(색을 하나 더 만들지 않는 방법).
        /// </summary>
        private void AddTextShadow(Text text)
        {
            var shadow = text.gameObject.AddComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0f, 0f, 0.75f);
            shadow.effectDistance = new Vector2(1f, -1f);
        }

        /// <summary>
        /// 잠긴 "이어하기" 버튼 오른쪽에 그림과 같은 자물쇠 표시를 그린다. 아이콘 에셋이 없으므로
        /// 몸통 1개 + 고리 3개(좌/상/우)의 작은 사각형 조합으로 만든다. 전부 raycastTarget=false다.
        /// [슬롯 3개] 만든 오브젝트를 돌려준다 - 호출부가 슬롯 상태에 따라 보였다 숨겼다 한다.
        /// </summary>
        private GameObject CreateLockIcon(RectTransform buttonRt)
        {
            var lockRoot = new GameObject("LockIcon", typeof(RectTransform));
            lockRoot.transform.SetParent(buttonRt, false);

            var lockRt = lockRoot.GetComponent<RectTransform>();
            lockRt.anchorMin = new Vector2(0.88f, 0.5f);
            lockRt.anchorMax = new Vector2(0.88f, 0.5f);
            lockRt.pivot = new Vector2(0.5f, 0.5f);
            lockRt.sizeDelta = new Vector2(22f, 28f);
            lockRt.anchoredPosition = Vector2.zero;

            Color lockColor = new Color(0.72f, 0.68f, 0.60f, 0.9f);

            CreateLockPart(lockRt, "Body", new Vector2(0f, 0f), new Vector2(1f, 0.55f), lockColor);
            CreateLockPart(lockRt, "ShackleLeft", new Vector2(0.22f, 0.55f), new Vector2(0.36f, 0.9f), lockColor);
            CreateLockPart(lockRt, "ShackleRight", new Vector2(0.64f, 0.55f), new Vector2(0.78f, 0.9f), lockColor);
            CreateLockPart(lockRt, "ShackleTop", new Vector2(0.22f, 0.86f), new Vector2(0.78f, 1f), lockColor);

            return lockRoot;
        }

        /// <summary>자물쇠를 이루는 작은 사각형 하나.</summary>
        private void CreateLockPart(RectTransform parent, string name, Vector2 anchorMin, Vector2 anchorMax, Color color)
        {
            var part = UIBuilder.CreatePanel(parent, name, anchorMin, anchorMax, Vector2.zero, Vector2.zero, color);
            var image = part.GetComponent<Image>();
            image.raycastTarget = false;
        }

        /// <summary>
        /// 그림 우측 하단에 박혀 있는 "Version 0.1.2" 자리를 우리 실제 버전으로 덮는다.
        /// 저작권 줄은 그림 그대로 두고, 버전 한 줄만 가리도록 아주 작은 불투명 띠를 깐다.
        /// </summary>
        private void CreateVersionLabel(RectTransform artRt)
        {
            var plate = UIBuilder.CreatePanel(artRt, "VersionPlate",
                // 그림의 버전 줄은 세로로 y 0.003~0.026 구간에 있고 그 위 0.034부터가 저작권 줄이다.
                // 저작권 줄을 건드리지 않도록 위 경계를 0.030에서 끊는다.
                anchorMin: new Vector2(0.885f, 0.000f), anchorMax: new Vector2(0.998f, 0.030f),
                offsetMin: Vector2.zero, offsetMax: Vector2.zero,
                color: new Color(0.05f, 0.055f, 0.06f, 0.92f));

            var plateImage = plate.GetComponent<Image>();
            plateImage.raycastTarget = false;

            // 문자열은 화면을 만들 때 한 번만 조립한다(매 프레임 조립 금지).
            var versionText = UIBuilder.CreateText(plate, "VersionText", "Version " + DisplayVersion,
                15, new Color(0.82f, 0.80f, 0.75f, 1f), TextAnchor.MiddleRight);
            versionText.raycastTarget = false;
            versionText.horizontalOverflow = HorizontalWrapMode.Overflow;

            var textRt = versionText.rectTransform;
            textRt.anchorMin = Vector2.zero;
            textRt.anchorMax = Vector2.one;
            textRt.offsetMin = Vector2.zero;
            textRt.offsetMax = new Vector2(-6f, 0f);
        }

        /// <summary>
        /// 타이틀 아트를 못 불러왔을 때의 예전 화면. 배경(섬 컨셉 아트 + 어두운 오버레이, 또는 이미지가
        /// 없으면 단색 배경), 제목/부제목, 시작/설정/종료 버튼을 생성한다. 설정 화면은 SettingsMenuController가
        /// 스스로 그리므로 여기서는 "설정" 버튼을 누르면 그쪽을 열어주기만 하면 된다.
        /// </summary>
        private void BuildFallbackMenu(RectTransform root)
        {
            if (backgroundTexture != null)
            {
                // 섬 컨셉 아트를 화면 전체에 꽉 채워 보여준다. AspectRatioFitter의 EnvelopeParent 모드가
                // 원본 GUI.DrawTexture(..., ScaleMode.ScaleAndCrop)와 동일하게 "비율을 유지한 채 잘라내며
                // 채우기"를 해준다.
                var bgGo = new GameObject("Background", typeof(RectTransform), typeof(RawImage), typeof(AspectRatioFitter));
                bgGo.transform.SetParent(root, false);
                var bgRt = bgGo.GetComponent<RectTransform>();
                bgRt.anchorMin = Vector2.zero;
                bgRt.anchorMax = Vector2.one;
                bgRt.offsetMin = Vector2.zero;
                bgRt.offsetMax = Vector2.zero;

                var rawImage = bgGo.GetComponent<RawImage>();
                rawImage.texture = backgroundTexture;

                var fitter = bgGo.GetComponent<AspectRatioFitter>();
                fitter.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
                fitter.aspectRatio = (float)backgroundTexture.width / backgroundTexture.height;

                // 배경 위에 반투명 어두운 오버레이를 덮어 글자 가독성을 확보한다.
                UIBuilder.CreatePanel(root, "BackgroundOverlay", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero,
                    new Color(0.05f, 0.08f, 0.12f, 0.55f));
            }
            else
            {
                // 배경 이미지를 못 불러온 경우(임포트 실패 등) 기존처럼 단색 배경으로 대체한다.
                UIBuilder.CreatePanel(root, "BackgroundOverlay", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero,
                    new Color(0.05f, 0.08f, 0.12f, 0.92f));
            }

            var title = UIBuilder.CreateText(root, "Title", gameTitle, 52, new Color(0.95f, 0.85f, 0.55f), TextAnchor.MiddleCenter);
            PositionCentered(title.rectTransform, yOffset: 150f, width: 0f, height: 70f);

            var subtitle = UIBuilder.CreateText(root, "Subtitle", gameSubtitle, 18, new Color(0.8f, 0.8f, 0.8f), TextAnchor.MiddleCenter);
            PositionCentered(subtitle.rectTransform, yOffset: 95f, width: 0f, height: 30f);

            var startButton = UIBuilder.CreateButton(root, "StartButton", "시작하기", StartGame);
            PositionCentered(startButton.GetComponent<RectTransform>(), yOffset: 20f, width: 240f, height: 48f);

            // 폴백 화면에도 이어하기를 둔다. 고른 슬롯이 비어 있으면 눌리지 않는 상태로 남는다.
            continueButton = UIBuilder.CreateButton(root, "ContinueButton", "이어하기", ContinueGame);
            PositionCentered(continueButton.GetComponent<RectTransform>(), yOffset: -40f, width: 240f, height: 48f);
            continueButton.targetGraphic = continueButton.GetComponent<Image>();
            continueButton.interactable = HasSaveFile();

            // 폴백 화면에는 아트 화면의 테두리/영문 라벨/자물쇠가 없다. 참조를 비워 두면
            // RefreshContinueButton이 interactable만 갱신한다(null 검사로 나머지를 건너뛴다).
            continueBorder = null;
            continueKoreanLabel = null;
            continueEnglishLabel = null;
            continueLockIcon = null;

            if (settingsMenu != null)
            {
                var settingsButton = UIBuilder.CreateButton(root, "SettingsButton", "설정", settingsMenu.Open);
                PositionCentered(settingsButton.GetComponent<RectTransform>(), yOffset: -100f, width: 240f, height: 48f);
            }

            var quitButton = UIBuilder.CreateButton(root, "QuitButton", "종료", QuitGame);
            PositionCentered(quitButton.GetComponent<RectTransform>(), yOffset: -160f, width: 240f, height: 48f);

            // [슬롯 3개] 아트 화면과 같은 자리(좌하단)에 같은 패널을 둔다.
            BuildSlotSelector(root);
        }

        /// <summary>
        /// 화면 가로 중앙(width가 0이면 좌우로 꽉 채움) 기준, 수직 중심에서 yOffset만큼 위/아래로
        /// 떨어진 위치에 지정한 크기로 배치한다. 타이틀/버튼들을 세로로 나란히 쌓기 위한 공통 헬퍼.
        /// </summary>
        private void PositionCentered(RectTransform rt, float yOffset, float width, float height)
        {
            if (width <= 0f)
            {
                rt.anchorMin = new Vector2(0f, 0.5f);
                rt.anchorMax = new Vector2(1f, 0.5f);
                rt.sizeDelta = new Vector2(0f, height);
            }
            else
            {
                rt.anchorMin = new Vector2(0.5f, 0.5f);
                rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.sizeDelta = new Vector2(width, height);
            }

            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2(0f, yOffset);
        }
    }
}
