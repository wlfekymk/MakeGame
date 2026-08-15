using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using MakeGame.Systems;

namespace MakeGame.UI
{
    /// <summary>
    /// 정식 UGUI 기반 게임 오버 화면.
    /// 개선(B2-12): GameOverController(Systems 소유, 사망 판정/재시작 같은 게임 규칙만 담당해야 한다)가
    /// 지금까지 OnGUI로 화면까지 직접 그려온 것은 역할 경계 위반이었다. 이 클래스가 화면 렌더링을
    /// 전담하고, GameOverController는 상태(isGameOver)와 동작(GetDeathMessage, RestartGame)만 노출한다.
    /// 씬에 미리 배치하지 않고 SurvivalHudUI와 동일한 방식으로 스스로 생성된다: 사망 후 재시작은
    /// GameOverController.RestartGame()이 SceneManager.LoadScene으로 씬 전체를 다시 로드하므로,
    /// AfterSceneLoad(최초 1회만 호출)로 만들면 재시작 후 이 UI가 죽은 참조를 들고 있거나 사라지는
    /// 문제가 생긴다. sceneLoaded 이벤트를 구독해 씬이 몇 번을 다시 로드되더라도 그때마다 새
    /// GameOverUI가 새 GameOverController 참조로 생성되게 한다.
    /// </summary>
    public class GameOverUI : MonoBehaviour
    {
        private GameOverController gameOverController;

        private GameObject panelRoot;
        private Text messageLabel;

        // 게임 오버 상태는 한 씬 인스턴스 안에서 false→true로 딱 한 번만 바뀌고 다시 false로 돌아가지
        // 않는다(재시작하면 씬 전체가 새로 로드되어 이 컴포넌트 자체가 새로 만들어진다). 그래서 "이미
        // 한 번 화면을 띄웠는지"만 기억해두면 충분하고, 그 전환 프레임에만 패널을 열고 사망 메시지를
        // 한 번 그려 넣는다(#7/#8과 동일하게, 바뀌지 않는 상태에서 매 프레임 다시 그리지 않기 위함).
        private bool shown = false;

        /// <summary>
        /// 씬이 로드될 때마다(최초 시작이든 재시작이든) 새 GameOverUI를 생성한다.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Bootstrap()
        {
            SceneManager.sceneLoaded += (scene, mode) =>
            {
                var go = new GameObject("GameOverUI");
                go.AddComponent<GameOverUI>();
            };
        }

        /// <summary>
        /// 씬에서 GameOverController를 찾아 참조를 캐시하고 UI를 생성한다. 기본적으로는 닫힌 상태로 둔다.
        /// </summary>
        private void Start()
        {
            gameOverController = FindAnyObjectByType<GameOverController>();
            BuildUI();
            SetOpen(false);
        }

        /// <summary>
        /// 캔버스와 화면 전체를 덮는 어두운 배경, 제목/사망 원인 안내 텍스트, 재시작 버튼을 생성한다.
        /// 기존 GameOverController.OnGUI가 그리던 배경색(짙은 핏빛 오버레이)·글자 크기·색을 그대로 옮겼다.
        /// </summary>
        private void BuildUI()
        {
            // 다른 UI 캔버스(SurvivalHud=5, Minimap=9, Inventory/Crafting=10, MinimapList=11)보다
            // 항상 위에 그려져야 하므로 그보다 높은 sortOrder를 준다.
            var canvas = UIBuilder.CreateCanvas("GameOverCanvas", sortOrder: 20);

            var panel = UIBuilder.CreatePanel(
                canvas.transform, "GameOverPanel",
                anchorMin: Vector2.zero, anchorMax: Vector2.one,
                offsetMin: Vector2.zero, offsetMax: Vector2.zero,
                color: new Color(0.12f, 0.02f, 0.02f, 0.85f));

            panelRoot = panel.gameObject;

            var title = UIBuilder.CreateText(panel, "Title", "게임 오버", 48, Color.red, TextAnchor.MiddleCenter);
            title.rectTransform.anchorMin = new Vector2(0f, 0.5f);
            title.rectTransform.anchorMax = new Vector2(1f, 0.5f);
            title.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            title.rectTransform.anchoredPosition = new Vector2(0f, 90f);
            title.rectTransform.sizeDelta = new Vector2(0f, 70f);

            messageLabel = UIBuilder.CreateText(panel, "Message", "", 20, Color.white, TextAnchor.MiddleCenter);
            messageLabel.rectTransform.anchorMin = new Vector2(0f, 0.5f);
            messageLabel.rectTransform.anchorMax = new Vector2(1f, 0.5f);
            messageLabel.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            messageLabel.rectTransform.anchoredPosition = new Vector2(0f, 20f);
            messageLabel.rectTransform.sizeDelta = new Vector2(0f, 40f);

            // R 키로도 여전히 재시작할 수 있지만(아래 참고), 클릭으로도 재시작할 수 있도록 버튼을 둔다.
            var restartButton = UIBuilder.CreateButton(panel, "RestartButton", "다시 시작 (R)", OnRestartClicked);
            var buttonRt = restartButton.GetComponent<RectTransform>();
            buttonRt.anchorMin = new Vector2(0.5f, 0.5f);
            buttonRt.anchorMax = new Vector2(0.5f, 0.5f);
            buttonRt.pivot = new Vector2(0.5f, 0.5f);
            buttonRt.anchoredPosition = new Vector2(0f, -50f);
            buttonRt.sizeDelta = new Vector2(240f, 48f);
        }

        /// <summary>
        /// GameOverController.isGameOver가 false→true로 바뀐 프레임을 감지해 그 한 번만 패널을 열고
        /// 사망 원인 안내 문구를 채운다. R 키 입력 자체는 GameOverController.Update()가 이미 직접
        /// 처리하고 있으므로(이 UI가 없어도 재시작됨) 여기서는 화면 표시만 담당한다.
        /// </summary>
        private void Update()
        {
            if (gameOverController == null || shown)
                return;

            if (!gameOverController.isGameOver)
                return;

            messageLabel.text = gameOverController.GetDeathMessage();
            SetOpen(true);
            shown = true;
        }

        /// <summary>
        /// 재시작 버튼 클릭 시 호출한다. Time.timeScale이 0인 상태에서도 UGUI 버튼 클릭(EventSystem의
        /// 포인터 입력 처리)은 Time.deltaTime과 무관하게 매 프레임 정상 동작하므로 눌린다 - 기존
        /// GameOverController.OnGUI 주석("Time.timeScale이 0이어도 OnGUI/Input은 정상 동작")과 같은
        /// 이유로 UGUI 쪽도 동일하게 동작한다.
        /// </summary>
        private void OnRestartClicked()
        {
            gameOverController?.RestartGame();
        }

        /// <summary>
        /// 패널을 열거나 닫는다.
        /// </summary>
        private void SetOpen(bool open)
        {
            if (panelRoot != null)
                panelRoot.SetActive(open);
        }
    }
}
