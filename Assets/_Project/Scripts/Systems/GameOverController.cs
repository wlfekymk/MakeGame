using UnityEngine;
using UnityEngine.SceneManagement;
using MakeGame.Player;
using MakeGame.Managers;

namespace MakeGame.Systems
{
    /// <summary>
    /// 플레이어 사망(체력 0) 상태를 감지해 게임 오버 처리를 담당한다.
    /// 사망 시 이동/상호작용 조작을 멈추고 시간을 정지시킨 뒤, 화면에 게임 오버 안내를 표시하며
    /// 플레이어가 재시작 키를 누르면 씬을 다시 로드해 처음부터 재시작할 수 있게 한다.
    /// </summary>
    public class GameOverController : MonoBehaviour
    {
        [Tooltip("사망 여부를 판정할 생존 수치")]
        public SurvivalStats survivalStats;

        [Tooltip("사망 시 비활성화할 이동/시점 컨트롤러")]
        public PlayerController playerController;

        [Tooltip("사망 시 비활성화할 상호작용 컨트롤러")]
        public InteractionController interactionController;

        [Tooltip("게임 오버 화면에서 재시작하는 키")]
        public KeyCode restartKey = KeyCode.R;

        /// <summary>현재 게임 오버 상태인지 여부.</summary>
        public bool isGameOver = false;

        /// <summary>
        /// 매 프레임 생존 수치의 사망 여부를 감시하다가, 사망을 감지하면 게임 오버 상태로 전환한다.
        /// 이미 게임 오버 상태이면 재시작 입력을 감시한다.
        /// </summary>
        private void Update()
        {
            if (!isGameOver)
            {
                if (survivalStats != null && survivalStats.IsDead)
                    TriggerGameOver();
                return;
            }

            if (Input.GetKeyDown(restartKey))
                RestartGame();
        }

        /// <summary>
        /// 게임 오버 상태로 전환한다. 이동/상호작용 컨트롤러를 비활성화하고 시간을 멈춰
        /// 더 이상 생존 수치가 변하거나 위험요소가 움직이지 않게 한다.
        /// </summary>
        private void TriggerGameOver()
        {
            isGameOver = true;

            if (playerController != null)
                playerController.enabled = false;

            if (interactionController != null)
                interactionController.enabled = false;

            Time.timeScale = 0f;
            AudioManager.Instance?.PlayDamage(); // 사망 알림용 피해음 재생
            Debug.Log("[GameOverController] 플레이어가 사망했습니다. 게임 오버.");
        }

        /// <summary>
        /// 시간을 다시 흐르게 하고 현재 씬을 다시 로드해 게임을 처음부터 재시작한다.
        /// GameManager/AudioManager는 DontDestroyOnLoad로 씬 전환에도 살아남는 싱글턴이라,
        /// 그냥 씬만 다시 로드하면 새 씬의 Managers가 "이미 인스턴스가 있다"고 오인해 스스로 파괴되어
        /// 월드가 새로 생성되지 않는다. 따라서 재시작 시에는 정적 인스턴스 참조를 먼저 비우고
        /// 이 오브젝트(Managers)를 명시적으로 파괴한 뒤 씬을 로드해, 새 씬의 Managers가 정상적으로
        /// 새 싱글턴이 되도록 한다.
        /// </summary>
        private void RestartGame()
        {
            Time.timeScale = 1f;

            GameManager.ClearInstance();
            AudioManager.ClearInstance();
            Destroy(gameObject);

            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        }

        /// <summary>
        /// 게임 오버 상태일 때 화면 전체를 어둡게 덮고 중앙에 안내 문구를 표시한다.
        /// Time.timeScale이 0이어도 OnGUI/Input은 정상 동작하므로 재시작 입력을 받을 수 있다.
        /// </summary>
        private void OnGUI()
        {
            if (!isGameOver)
                return;

            GUI.color = new Color(0f, 0f, 0f, 0.75f);
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.color = Color.white;

            var titleStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 48,
                alignment = TextAnchor.MiddleCenter,
            };
            titleStyle.normal.textColor = Color.red;

            var subStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 20,
                alignment = TextAnchor.MiddleCenter,
            };
            subStyle.normal.textColor = Color.white;

            GUI.Label(new Rect(0, Screen.height / 2f - 80, Screen.width, 60), "게임 오버", titleStyle);
            GUI.Label(new Rect(0, Screen.height / 2f, Screen.width, 40), "무인도에서 생존하지 못했습니다.", subStyle);
            GUI.Label(new Rect(0, Screen.height / 2f + 40, Screen.width, 40), "[R] 키를 눌러 다시 시작", subStyle);
        }
    }
}
