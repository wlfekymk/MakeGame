using UnityEngine;
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
    /// </summary>
    public class MainMenuController : MonoBehaviour
    {
        [Tooltip("타이틀 화면에 표시할 게임 제목")]
        public string gameTitle = "무인도 탈출";

        [Tooltip("타이틀 화면에 표시할 부제목")]
        public string gameSubtitle = "Stranded Deep 스타일 서바이벌";

        [Tooltip("시작 전 비활성화할 이동/시점 컨트롤러")]
        public PlayerController playerController;

        [Tooltip("시작 전 비활성화할 상호작용 컨트롤러")]
        public InteractionController interactionController;

        [Tooltip("메뉴에서 '설정' 버튼을 눌렀을 때 열고 닫을 설정 화면 (비워두면 설정 버튼을 표시하지 않는다)")]
        public SettingsMenuController settingsMenu;

        /// <summary>현재 타이틀 화면이 표시 중인지 여부.</summary>
        public bool isMenuOpen = true;

        /// <summary>
        /// 타이틀 화면 배경 이미지 (Resources/UI/title_background.png, 섬 컨셉 아트).
        /// 그동안 단색 배경만 써서 밋밋했던 문제를 개선한다. 로드 실패 시 기존 단색 배경으로 자동 대체된다.
        /// </summary>
        private Texture2D backgroundTexture;

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

            // 타이틀 배경 이미지를 미리 한 번만 로드해둔다.
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
        /// "시작하기" 버튼을 눌렀을 때 호출한다. 타이틀 화면을 닫고 시간을 다시 흐르게 하며 조작을 활성화한다.
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
        /// 캔버스, 배경(섬 컨셉 아트 + 어두운 오버레이, 또는 이미지가 없으면 단색 배경), 제목/부제목,
        /// 시작/설정/종료 버튼을 생성한다. 설정 화면은 이제 SettingsMenuController가 스스로 그리므로
        /// 여기서는 "설정" 버튼을 누르면 그쪽을 열어주기만 하면 된다.
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

            if (settingsMenu != null)
            {
                var settingsButton = UIBuilder.CreateButton(root, "SettingsButton", "설정", settingsMenu.Open);
                PositionCentered(settingsButton.GetComponent<RectTransform>(), yOffset: -40f, width: 240f, height: 48f);
            }

            var quitButton = UIBuilder.CreateButton(root, "QuitButton", "종료", QuitGame);
            PositionCentered(quitButton.GetComponent<RectTransform>(), yOffset: -100f, width: 240f, height: 48f);
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
