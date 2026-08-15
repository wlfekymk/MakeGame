using UnityEngine;
using MakeGame.Player;
using MakeGame.Systems;

namespace MakeGame.UI
{
    /// <summary>
    /// 게임 시작 시 표시되는 타이틀(메인 메뉴) 화면을 담당한다.
    /// 씬은 바로 로드되어 섬/월드가 생성되지만, 플레이어가 "시작하기"를 누르기 전까지는
    /// 시간을 멈추고 이동/상호작용 조작을 막아 준비되지 않은 상태에서 조작이 들어가지 않게 한다.
    /// GameOverController/EndingChecker와 동일하게 OnGUI + Time.timeScale 패턴을 사용한다.
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

        // 성능 개선(#6): OnGUI는 매 프레임 호출되는데, 그때마다 new GUIStyle(...)로 스타일 객체를
        // 새로 만들면 타이틀 화면이 오래 켜져 있을 때 불필요한 GC 부담이 누적된다. StatusEffectWarningUI.
        // EnsureStyles()와 동일하게 최초 1회만 만들고 이후에는 캐시된 스타일을 재사용한다.
        private GUIStyle titleStyle;
        private GUIStyle subStyle;
        private GUIStyle buttonStyle;

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

            // 타이틀 배경 이미지를 미리 한 번만 로드해둔다 (OnGUI에서 매 프레임 Resources.Load하지 않도록).
            backgroundTexture = Resources.Load<Texture2D>("UI/title_background");
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
        /// 타이틀 화면이 열려 있는 동안 화면 중앙에 제목/부제목과 시작/설정/종료 버튼을 그린다.
        /// 설정 화면이 열려 있으면 타이틀 버튼 대신 설정 화면을 그리도록 SettingsMenuController에 위임한다.
        /// </summary>
        private void OnGUI()
        {
            if (!isMenuOpen)
                return;

            var fullScreen = new Rect(0, 0, Screen.width, Screen.height);
            if (backgroundTexture != null)
            {
                // 섬 컨셉 아트를 화면 전체에 꽉 채워 그리고(ScaleAndCrop, 비율 유지하며 잘라내기),
                // 그 위에 반투명 어두운 오버레이를 덮어 글자 가독성을 확보한다.
                GUI.DrawTexture(fullScreen, backgroundTexture, ScaleMode.ScaleAndCrop);
                GUI.color = new Color(0.05f, 0.08f, 0.12f, 0.55f);
                GUI.DrawTexture(fullScreen, Texture2D.whiteTexture);
                GUI.color = Color.white;
            }
            else
            {
                // 배경 이미지를 못 불러온 경우(임포트 실패 등) 기존처럼 단색 배경으로 대체한다.
                GUI.color = new Color(0.05f, 0.08f, 0.12f, 0.92f);
                GUI.DrawTexture(fullScreen, Texture2D.whiteTexture);
                GUI.color = Color.white;
            }

            if (settingsMenu != null && settingsMenu.isOpen)
            {
                settingsMenu.DrawSettingsPanel();
                return;
            }

            EnsureStyles();

            float centerX = Screen.width / 2f;
            float centerY = Screen.height / 2f;

            GUI.Label(new Rect(0, centerY - 180, Screen.width, 70), gameTitle, titleStyle);
            GUI.Label(new Rect(0, centerY - 120, Screen.width, 30), gameSubtitle, subStyle);

            float buttonWidth = 240f;
            float buttonHeight = 48f;
            float buttonX = centerX - buttonWidth / 2f;

            if (GUI.Button(new Rect(buttonX, centerY - 40, buttonWidth, buttonHeight), "시작하기", buttonStyle))
                StartGame();

            if (settingsMenu != null)
            {
                if (GUI.Button(new Rect(buttonX, centerY + 20, buttonWidth, buttonHeight), "설정", buttonStyle))
                    settingsMenu.Open();
            }

            if (GUI.Button(new Rect(buttonX, centerY + 80, buttonWidth, buttonHeight), "종료", buttonStyle))
                QuitGame();
        }

        /// <summary>
        /// GUIStyle은 OnGUI 컨텍스트 안에서만 새로 만들 수 있으므로, 최초 호출 시점에 지연 생성해
        /// 필드에 캐시해두고 이후에는 재사용한다(StatusEffectWarningUI.EnsureStyles와 동일한 패턴).
        /// </summary>
        private void EnsureStyles()
        {
            if (titleStyle != null)
                return;

            titleStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 52,
                alignment = TextAnchor.MiddleCenter,
            };
            titleStyle.normal.textColor = new Color(0.95f, 0.85f, 0.55f);

            subStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 18,
                alignment = TextAnchor.MiddleCenter,
            };
            subStyle.normal.textColor = new Color(0.8f, 0.8f, 0.8f);

            buttonStyle = new GUIStyle(GUI.skin.button) { fontSize = 22 };
        }
    }
}
