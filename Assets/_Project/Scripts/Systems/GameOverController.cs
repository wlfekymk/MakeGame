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

        // 회귀 수정(qa-reviewer 지적): 레거시 IMGUI(OnGUI)는 Unity 렌더링 순서상 항상 Screen Space-Overlay
        // Canvas보다 나중에, 최상단에 그려진다(sortOrder로도 못 바꾼다). 예전 OnGUI()가 화면 전체를 다시
        // 덮고 자체 문구를 그렸기 때문에, ui-engineer가 새로 만든 UI/GameOverUI(UGUI)의 제목/사망 메시지/
        // 재시작 버튼이 화면에 전혀 보이지 않는 문제가 있었다. "검증 전까지 과도기 안전장치로 남겨두라"는
        // 이전 지시가 틀렸다는 판단에 따라 OnGUI()/titleStyle/subStyle/EnsureStyles()를 전부 제거했다.
        // 새 화면 표시는 전적으로 UI/GameOverUI가 담당하며, 이 클래스는 isGameOver/GetDeathMessage()/
        // RestartGame()만 노출한다.

        /// <summary>
        /// RestartGame()이 같은 프레임 안에서 두 번 실행되는 것을 막는 1회성 가드.
        /// qa-reviewer 지적: R키 입력(이 클래스의 Update)과 UI/GameOverUI의 재시작 버튼 클릭이 서로
        /// 다른 경로로 RestartGame()을 호출할 수 있어, 겹치면 GameManager.ClearInstance/
        /// AudioManager.ClearInstance/Destroy(gameObject)/SceneManager.LoadScene이 중복 실행될 위험이 있었다.
        /// </summary>
        private bool isRestarting = false;

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
            EnsureDeathCauseRecorded();

            if (playerController != null)
                playerController.enabled = false;

            if (interactionController != null)
                interactionController.enabled = false;

            Time.timeScale = 0f;
            AudioManager.Instance?.PlayDamage(); // 사망 알림용 피해음 재생
            Debug.Log("[GameOverController] 플레이어가 사망했습니다. 게임 오버.");
        }

        /// <summary>
        /// [ui-engineer 요청 - Design_Ending.md 5장 (3)] 사망 화면의 사인별 회피 힌트는 정확도가
        /// 전적으로 lastDamageCause에 달려 있다(사인이 Unknown이면 "허기와 갈증을 먼저 관리하라"는
        /// 일반 문구로 떨어져, 실제로는 상어에게 죽은 플레이어에게 엉뚱한 힌트를 준다).
        ///
        /// 전수 조사 결과: SurvivalStats.TakeDamage 호출부 8곳(굶주림/일사병/중독/출혈/익사 5곳 +
        /// HazardSource의 Predator 2곳·SharkAttack 1곳)은 전부 cause를 명시적으로 넘기고 있어,
        /// **체력을 깎아서 죽는 경로에는 빈 곳이 없다.** 남은 빈 경로는 체력을 깎지 않고 죽는 하나뿐이다:
        /// SaveLoadController가 저장된 체력을 survivalStats.health에 직접 대입한다(TakeDamage를 거치지
        /// 않는다). 체력이 매우 낮은 상태로 저장된 판을 불러오면 lastDamageCause는 초기값 Unknown인
        /// 채로 사망 판정에 도달할 수 있다.
        ///
        /// 그래서 여기서는 "사인이 아직 비어 있을 때에 한해" 현재 생존 수치에서 사인을 역추정해 채운다.
        /// 이미 기록된 사인은 절대 덮어쓰지 않으므로(정상 경로 100% 무변화), 이 메서드가 값을 바꾸는
        /// 경우는 원래 Unknown이었을 때뿐이다 - 즉 나빠질 수 있는 표시가 없다.
        /// </summary>
        private void EnsureDeathCauseRecorded()
        {
            if (survivalStats == null || survivalStats.lastDamageCause != DamageCause.Unknown)
                return;

            // 순서는 "지금 실제로 체력을 깎고 있는 효과" 중 초당 피해가 큰 쪽부터다
            // (익사 3.0 > 출혈 1.2 > 굶주림/탈수 1.0 > 중독 0.8 > 일사병 0.5, SurvivalStats 기본값 기준).
            // 여러 조건이 동시에 참이면 가장 빨리 죽였을 쪽을 사인으로 본다.
            if (survivalStats.oxygen <= 0f)
                survivalStats.lastDamageCause = DamageCause.Drowning;
            else if (survivalStats.isBleeding)
                survivalStats.lastDamageCause = DamageCause.Bleeding;
            else if (survivalStats.hunger <= 0f || survivalStats.thirst <= 0f)
                survivalStats.lastDamageCause = DamageCause.Starvation;
            else if (survivalStats.isPoisoned)
                survivalStats.lastDamageCause = DamageCause.Poison;
            else if (survivalStats.sunstroke >= SurvivalStats.MaxStatValue)
                survivalStats.lastDamageCause = DamageCause.Sunstroke;
            // 어느 것도 아니면 Unknown 그대로 둔다 - 근거 없이 아무 사인이나 찍으면 힌트가 함정이 된다
            // (Design_Ending.md 5장: "힌트가 틀리면 힌트가 아니라 함정이 된다").
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
        /// qa-reviewer 지적: R키(Update)와 UI/GameOverUI의 버튼 클릭 두 경로가 같은 프레임에 겹치면
        /// 아래 정리/씬 로드 로직이 중복 실행될 수 있어, 진입부에 1회성 가드(isRestarting)를 추가했다.
        /// </summary>
        public void RestartGame()
        {
            if (isRestarting)
                return;
            isRestarting = true;

            Time.timeScale = 1f;

            GameManager.ClearInstance();
            AudioManager.ClearInstance();
            Destroy(gameObject);

            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
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
    }
}
