using UnityEngine;
using UnityEngine.UI;
using MakeGame.Player;

namespace MakeGame.UI
{
    /// <summary>
    /// 출혈/중독/골절처럼 즉각적인 대응이 필요한 상태 이상이 발생했을 때
    /// 화면 상단 중앙에 눈에 띄는 경고 배너를 띄운다.
    /// 기존 DebugHud의 작은 O/X 표시만으로는 상태 이상 발생을 놓치기 쉬워서
    /// 별도의 큰 경고 UI로 보완한다.
    /// 개선(B2-14): OnGUI(레거시 IMGUI)로 직접 그리던 것을 UIBuilder 기반 UGUI로 옮겼다. IMGUI는
    /// Screen Space Overlay Canvas보다 항상 나중에(최상단에) 그려져 GameOverUI 등 다른 UGUI 화면을
    /// 가려버리는 문제가 있었기 때문에(GameOverController.OnGUI 사례 참고), OnGUI를 완전히 제거했다.
    /// </summary>
    public class StatusEffectWarningUI : MonoBehaviour
    {
        [Tooltip("경고 상태를 판단할 대상 생존 수치")]
        public SurvivalStats survivalStats;

        [Tooltip("경고 배너 배경색 (반투명 빨강)")]
        public Color warningColor = new Color(0.6f, 0f, 0f, 0.75f);

        [Tooltip("경고 텍스트가 깜빡이는 속도")]
        public float pulseSpeed = 2.5f;

        private GameObject panelRoot;
        private CanvasGroup canvasGroup;
        private Text messageLabel;

        // 마지막으로 배너에 반영한 상태 이상 조합. 이 세 값이 실제로 바뀐 프레임에만 BuildWarningMessage()로
        // 새 문자열을 만들어 대입한다(#7/#8과 동일한 캐싱 패턴) - 기존 OnGUI 코드는 배너가 떠 있는 동안
        // 매 프레임 List<string> 할당 + string.Join을 다시 했는데, 그보다 개선된 방식이다.
        private bool lastBleeding;
        private bool lastPoisoned;
        private bool lastBrokenBone;
        private bool everBuilt = false;

        /// <summary>
        /// 시작 시 배너 UI 계층을 생성하고 기본적으로 닫힌 상태로 둔다.
        /// </summary>
        private void Start()
        {
            BuildUI();
            SetOpen(false);
        }

        /// <summary>
        /// 캔버스와 배너 패널(배경 + 그림자가 있는 텍스트)을 화면 상단 중앙에 생성한다.
        /// 펄스(깜빡임) 애니메이션은 CanvasGroup.alpha로 배경/텍스트/그림자를 한꺼번에 제어한다.
        /// </summary>
        private void BuildUI()
        {
            var canvas = UIBuilder.CreateCanvas("StatusEffectWarningCanvas", sortOrder: 6);

            // 원본 OnGUI는 배경 알파를 warningColor.a * pulse로, 텍스트는 1 * pulse로 따로 계산해
            // 배경은 항상 반투명(최대 0.75)에 머물고 텍스트만 완전히 불투명해질 수 있었다. 배경/텍스트/
            // 그림자 각각의 "기본" 알파를 원본과 동일하게 설정해두고, 그 위에 CanvasGroup.alpha로
            // pulse(0.55~1)를 곱하면 최종 결과가 원본의 (base alpha * pulse) 계산과 정확히 일치한다.
            var panel = UIBuilder.CreatePanel(
                canvas.transform, "WarningBanner",
                anchorMin: new Vector2(0.5f, 1f), anchorMax: new Vector2(0.5f, 1f),
                offsetMin: new Vector2(-450f, -60f), offsetMax: new Vector2(450f, -14f),
                color: warningColor);

            panelRoot = panel.gameObject;
            canvasGroup = panelRoot.AddComponent<CanvasGroup>();

            messageLabel = UIBuilder.CreateText(panel, "Message", "", 16, Color.white, TextAnchor.MiddleCenter);
            messageLabel.fontStyle = FontStyle.Bold;
            messageLabel.horizontalOverflow = HorizontalWrapMode.Wrap;
            var labelRt = messageLabel.rectTransform;
            labelRt.anchorMin = Vector2.zero;
            labelRt.anchorMax = Vector2.one;
            labelRt.offsetMin = Vector2.zero;
            labelRt.offsetMax = Vector2.zero;

            // 원본 OnGUI는 검은 그림자 텍스트를 1.5px 오프셋으로 먼저 그린 뒤 흰 텍스트를 덧그려
            // 밝은 배경 위에서도 잘 읽히게 했다. UGUI의 내장 Shadow 이펙트 컴포넌트가 동일한 역할을 한다.
            var shadow = messageLabel.gameObject.AddComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0f, 0f, 0.6f);
            shadow.effectDistance = new Vector2(1.5f, -1.5f);
        }

        /// <summary>
        /// 매 프레임 현재 활성화된 상태 이상을 확인해 배너를 열고 닫고, 열려 있는 동안은 펄스(깜빡임)
        /// 알파를 갱신한다. 표시할 문구 자체는 상태 이상 조합이 실제로 바뀐 프레임에만 다시 만든다.
        /// </summary>
        private void Update()
        {
            if (survivalStats == null)
            {
                SetOpen(false);
                return;
            }

            bool isBleeding = survivalStats.isBleeding;
            bool isPoisoned = survivalStats.isPoisoned;
            bool hasBrokenBone = survivalStats.hasBrokenBone;
            bool anyWarning = isBleeding || isPoisoned || hasBrokenBone;

            if (!anyWarning)
            {
                SetOpen(false);
                everBuilt = false; // 다음에 다시 경고가 뜰 때 반드시 새로 문구를 만들도록 초기화
                return;
            }

            SetOpen(true);

            if (!everBuilt || isBleeding != lastBleeding || isPoisoned != lastPoisoned || hasBrokenBone != lastBrokenBone)
            {
                messageLabel.text = BuildWarningMessage();
                lastBleeding = isBleeding;
                lastPoisoned = isPoisoned;
                lastBrokenBone = hasBrokenBone;
                everBuilt = true;
            }

            // 시간에 따라 알파를 진동시켜(pulse) 시선을 끄는 효과를 준다. CanvasGroup.alpha 하나로
            // 배경(WarningBanner Image)과 텍스트, 그림자까지 한꺼번에 곱해져 원본과 동일한 최종 알파가 된다.
            float pulse = Mathf.PingPong(Time.time * pulseSpeed, 1f);
            canvasGroup.alpha = Mathf.Lerp(0.55f, 1f, pulse);
        }

        /// <summary>
        /// 현재 활성화된 상태 이상들을 조합해 하나의 경고 문구로 만든다.
        /// 붕대/해독제/부목처럼 어떤 아이템으로 치료 가능한지도 함께 안내한다.
        /// </summary>
        private string BuildWarningMessage()
        {
            var parts = new System.Collections.Generic.List<string>();

            if (survivalStats.isBleeding)
                parts.Add("⚠ 출혈 중! 붕대로 지혈하세요 (C)");
            if (survivalStats.isPoisoned)
                parts.Add("⚠ 중독 상태! 해독제가 필요합니다 (C)");
            if (survivalStats.hasBrokenBone)
                parts.Add("⚠ 골절 상태! 부목으로 치료하세요 (C)");

            return string.Join("   /   ", parts);
        }

        /// <summary>
        /// 경고 배너를 열거나 닫는다.
        /// </summary>
        private void SetOpen(bool open)
        {
            if (panelRoot != null)
                panelRoot.SetActive(open);
        }
    }
}
