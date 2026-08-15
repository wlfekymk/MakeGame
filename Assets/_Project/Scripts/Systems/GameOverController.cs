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

        // 성능 개선(#5): OnGUI가 매 프레임 호출될 때마다 new GUIStyle(...)로 새 인스턴스를 만들고 있었다.
        // UI/StatusEffectWarningUI.EnsureStyles()와 동일한 지연 캐싱 패턴을 적용해, 최초 1회만 만들고
        // 이후에는 캐시된 스타일을 재사용한다.
        private GUIStyle titleStyle;
        private GUIStyle subStyle;

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
        /// 컴파일 차단 해제: UI/GameOverUI.cs(새 UGUI 게임오버 화면)가 재시작 버튼에서 이 메서드를
        /// 직접 호출해야 해서 접근제한자만 private→public으로 바꿨다(시그니처/본문은 그대로).
        /// </summary>
        public void RestartGame()
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

            // 배경 아트(밝은 섬 사진)는 일부러 넣지 않는다 - 사망 화면은 어둡고 무거운 톤이 맞다고 판단해
            // 순수 검정 대신 아주 어두운 핏빛을 살짝 섞어 분위기만 보강했다.
            GUI.color = new Color(0.12f, 0.02f, 0.02f, 0.85f);
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.color = Color.white;

            EnsureStyles();

            GUI.Label(new Rect(0, Screen.height / 2f - 80, Screen.width, 60), "게임 오버", titleStyle);
            GUI.Label(new Rect(0, Screen.height / 2f, Screen.width, 40), GetDeathMessage(), subStyle);
            GUI.Label(new Rect(0, Screen.height / 2f + 40, Screen.width, 40), "[R] 키를 눌러 다시 시작", subStyle);
        }

        /// <summary>
        /// 버그 수정: 예전에는 사망 원인과 무관하게 "무인도에서 생존하지 못했습니다." 한 문장만 표시했다.
        /// SurvivalStats.lastDamageCause(체력을 마지막으로 깎은 원인)를 읽어, 굶주림/탈수/일사병/중독/출혈/
        /// 익사/맹수 중 실제로 죽음에 이른 원인에 맞는 문구를 보여준다. 원인을 알 수 없으면(예: 초기값 그대로
        /// 죽은 경우) 기존 문구로 대체한다.
        /// 컴파일 차단 해제: UI/GameOverUI.cs가 사망 문구를 표시하려면 이 메서드를 직접 호출해야 해서
        /// 접근제한자만 private→public으로 바꿨다(시그니처/본문은 그대로).
        /// </summary>
        public string GetDeathMessage()
        {
            if (survivalStats == null)
                return "무인도에서 생존하지 못했습니다.";

            switch (survivalStats.lastDamageCause)
            {
                case DamageCause.Starvation:
                    return "굶주림과 갈증을 이기지 못하고 쓰러졌습니다.";
                case DamageCause.Sunstroke:
                    return "뜨거운 햇빛 아래 일사병으로 쓰러졌습니다.";
                case DamageCause.Poison:
                    return "독을 이겨내지 못하고 쓰러졌습니다.";
                case DamageCause.Bleeding:
                    return "출혈을 멈추지 못하고 쓰러졌습니다.";
                case DamageCause.Drowning:
                    return "물속에서 숨을 쉬지 못하고 익사했습니다.";
                case DamageCause.Predator:
                    return "섬의 포식자에게 목숨을 잃었습니다.";
                case DamageCause.SharkAttack:
                    return "바닷속에서 상어의 습격을 받아 목숨을 잃었습니다.";
                default:
                    return "무인도에서 생존하지 못했습니다.";
            }
        }

        /// <summary>
        /// GUIStyle은 OnGUI 컨텍스트 안에서만 새로 만들 수 있으므로, 최초 호출 시점에 지연 생성해
        /// 캐시해둔다(UI/StatusEffectWarningUI.EnsureStyles와 동일한 패턴).
        /// </summary>
        private void EnsureStyles()
        {
            if (titleStyle != null)
                return;

            titleStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 48,
                alignment = TextAnchor.MiddleCenter,
            };
            titleStyle.normal.textColor = Color.red;

            subStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 20,
                alignment = TextAnchor.MiddleCenter,
            };
            subStyle.normal.textColor = Color.white;
        }
    }
}
