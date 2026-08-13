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

            GUI.color = new Color(0.05f, 0.08f, 0.12f, 0.92f);
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.color = Color.white;

            if (settingsMenu != null && settingsMenu.isOpen)
            {
                settingsMenu.DrawSettingsPanel();
                return;
            }

            var titleStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 52,
                alignment = TextAnchor.MiddleCenter,
            };
            titleStyle.normal.textColor = new Color(0.95f, 0.85f, 0.55f);

            var subStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 18,
                alignment = TextAnchor.MiddleCenter,
            };
            subStyle.normal.textColor = new Color(0.8f, 0.8f, 0.8f);

            float centerX = Screen.width / 2f;
            float centerY = Screen.height / 2f;

            GUI.Label(new Rect(0, centerY - 180, Screen.width, 70), gameTitle, titleStyle);
            GUI.Label(new Rect(0, centerY - 120, Screen.width, 30), gameSubtitle, subStyle);

            var buttonStyle = new GUIStyle(GUI.skin.button) { fontSize = 22 };
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
    }
}
