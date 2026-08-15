using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using MakeGame.Systems;

namespace MakeGame.UI
{
    /// <summary>
    /// 정식 UGUI 기반 엔딩(승리) 연출 화면.
    /// 개선(B3-1): EndingChecker(Systems 소유, 두 엔딩 경로 - 배 탈출 / 경비행기 수리 - 의 달성 판정만
    /// 담당해야 한다)가 지금까지 OnGUI로 화면까지 직접 그려온 것은 GameOverController와 동일한 역할
    /// 경계 위반이었다. 이 클래스가 화면 렌더링을 전담하고, EndingChecker는 상태(IsShowingEnding,
    /// EndingMessage)와 동작(DismissEndingUI)만 노출한다.
    /// GameOverUI와 동일하게 씬에 미리 배치하지 않고 스스로 생성된다: sceneLoaded 이벤트를 구독해
    /// 씬이 다시 로드되더라도(재시작 등) 그때마다 새 EndingUI가 새 EndingChecker 참조로 생성되게 한다.
    /// 주의(코디네이터 지시): 이번에는 EndingChecker.OnGUI 제거를 "나중에" 미루지 않는다 - 이전에
    /// GameOverController.OnGUI를 잠시 남겨뒀다가 IMGUI가 Screen Space Overlay Canvas를 전부 가려버리는
    /// 회귀가 실제로 발생했던 사례(GameOverUI 규정)와 같은 문제를 여기서도 반복하지 않기 위함이다.
    /// </summary>
    public class EndingUI : MonoBehaviour
    {
        private EndingChecker endingChecker;

        private GameObject panelRoot;
        private Text messageLabel;
        private Text hintLabel;

        // EndingChecker.showEndingUI(→IsShowingEnding)가 false→true로 바뀐 프레임에만 메시지를 다시
        // 채우고 패널을 연다(#7/#8/GameOverUI와 동일한, 바뀌지 않는 상태에서 매 프레임 다시 그리지
        // 않는 캐싱 패턴). 엔딩 화면은 Dismiss 후에도 자유 플레이가 계속되므로(EndingChecker 주석 참고)
        // true→false 전환도 감지해 다시 닫아준다.
        private bool lastShowing = false;

        /// <summary>
        /// 씬이 로드될 때마다(최초 시작이든 재시작이든) 새 EndingUI를 생성한다.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Bootstrap()
        {
            SceneManager.sceneLoaded += (scene, mode) =>
            {
                var go = new GameObject("EndingUI");
                go.AddComponent<EndingUI>();
            };
        }

        /// <summary>
        /// 씬에서 EndingChecker를 찾아 참조를 캐시하고 UI를 생성한다. 기본적으로는 닫힌 상태로 둔다.
        /// </summary>
        private void Start()
        {
            endingChecker = FindAnyObjectByType<EndingChecker>();
            BuildUI();
            SetOpen(false);
        }

        /// <summary>
        /// 캔버스와 배경(섬 컨셉 아트 + 금색 톤 오버레이, 또는 이미지가 없으면 어두운 단색 배경),
        /// 제목/엔딩 메시지/계속하기 안내 텍스트, 계속하기 버튼을 생성한다. 기존 EndingChecker.OnGUI가
        /// 그리던 배경(title_background + 금색 오버레이 또는 단색 폴백)·글자 크기·색을 그대로 옮겼다.
        /// </summary>
        private void BuildUI()
        {
            // GameOverCanvas(20)보다 위에 있어야 한다 - 엔딩은 게임 오버와 동시에 일어날 수 없는
            // 배타적 결말이지만, 다른 모든 HUD/메뉴 캔버스보다는 항상 위에 그려져야 하므로 그보다도
            // 높은 sortOrder를 준다.
            var canvas = UIBuilder.CreateCanvas("EndingCanvas", sortOrder: 21);

            var root = UIBuilder.CreatePanel(
                canvas.transform, "EndingPanel",
                anchorMin: Vector2.zero, anchorMax: Vector2.one,
                offsetMin: Vector2.zero, offsetMax: Vector2.zero,
                color: Color.clear);
            panelRoot = root.gameObject;

            var backgroundTexture = Resources.Load<Texture2D>("UI/title_background");
            if (backgroundTexture != null)
            {
                // 탈출에 성공했으니 떠나온 섬을 배경으로 보여주고, 금색 톤 오버레이로 축하 분위기를
                // 낸다(MainMenuController.BuildUI와 동일한 AspectRatioFitter.EnvelopeParent 기법으로
                // 원본 GUI.DrawTexture(..., ScaleMode.ScaleAndCrop)를 재현).
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

                UIBuilder.CreatePanel(root, "BackgroundOverlay", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero,
                    new Color(0.25f, 0.15f, 0f, 0.6f));
            }
            else
            {
                // 배경 이미지를 못 불러온 경우(임포트 실패 등) 기존처럼 짙은 단색 배경으로 대체한다.
                UIBuilder.CreatePanel(root, "BackgroundOverlay", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero,
                    new Color(0f, 0f, 0f, 0.75f));
            }

            var title = UIBuilder.CreateText(root, "Title", "탈출 성공!", 48, new Color(1f, 0.85f, 0.2f), TextAnchor.MiddleCenter);
            PositionCentered(title.rectTransform, yOffset: 80f, width: 0f, height: 60f);

            messageLabel = UIBuilder.CreateText(root, "Message", "", 20, Color.white, TextAnchor.MiddleCenter);
            PositionCentered(messageLabel.rectTransform, yOffset: 20f, width: 0f, height: 40f);

            string keyLabel = endingChecker != null ? endingChecker.continueKey.ToString() : "Space";
            hintLabel = UIBuilder.CreateText(root, "Hint", $"[{keyLabel}] 키를 눌러 계속하기", 20, Color.white, TextAnchor.MiddleCenter);
            PositionCentered(hintLabel.rectTransform, yOffset: -20f, width: 0f, height: 40f);

            // 기존 키 입력(continueKey, 기본 Space)은 EndingChecker.Update()가 그대로 처리하므로 여기서는
            // 화면 표시만 담당하지만, 키보드가 없는 입력 방식(터치/게임패드 커서 등)도 지원하기 위해
            // 클릭으로도 계속할 수 있는 버튼을 추가로 둔다(GameOverUI의 재시작 버튼과 동일한 이유).
            var continueButton = UIBuilder.CreateButton(root, "ContinueButton", $"계속하기 ({keyLabel})", OnContinueClicked);
            PositionCentered(continueButton.GetComponent<RectTransform>(), yOffset: -80f, width: 240f, height: 48f);
        }

        /// <summary>
        /// 화면 가로 중앙(width가 0이면 좌우로 꽉 채움) 기준, 수직 중심에서 yOffset만큼 위/아래로
        /// 떨어진 위치에 지정한 크기로 배치한다(MainMenuController.PositionCentered와 동일한 헬퍼).
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

        /// <summary>
        /// EndingChecker.IsShowingEnding이 false→true 혹은 true→false로 바뀐 프레임을 감지해 그때만
        /// 패널을 열거나 닫고, 여는 순간에만 엔딩 메시지 문구를 다시 채운다. Space 키 입력 자체는
        /// EndingChecker.Update()가 이미 직접 처리하고 있으므로(이 UI가 없어도 Dismiss됨) 여기서는
        /// 화면 표시만 담당한다.
        /// </summary>
        private void Update()
        {
            if (endingChecker == null)
                return;

            bool showing = endingChecker.IsShowingEnding;
            if (showing == lastShowing)
                return;

            if (showing)
            {
                messageLabel.text = endingChecker.EndingMessage;
                SetOpen(true);
            }
            else
            {
                SetOpen(false);
            }

            lastShowing = showing;
        }

        /// <summary>
        /// 계속하기 버튼 클릭 시 호출한다. Time.timeScale이 0인 상태에서도 UGUI 버튼 클릭(EventSystem의
        /// 포인터 입력 처리)은 Time.deltaTime과 무관하게 매 프레임 정상 동작하므로 눌린다(GameOverUI와
        /// 동일한 이유). EndingChecker.DismissEndingUI()는 상태 플래그와 Time.timeScale, 컨트롤러
        /// 활성화 여부만 되돌리는 멱등(idempotent) 동작이라(두 번 불려도 안전) 별도 재진입 방지 처리는
        /// 필요 없다 - GameOverController.RestartGame()이 씬 리로드 같은 파괴적 동작을 하기 때문에
        /// isRestarting 가드가 필요했던 것과는 성격이 다르다.
        /// </summary>
        private void OnContinueClicked()
        {
            endingChecker?.DismissEndingUI();
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
