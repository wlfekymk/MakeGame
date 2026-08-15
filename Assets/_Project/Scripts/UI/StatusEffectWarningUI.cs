using UnityEngine;
using UnityEngine.UI;
using MakeGame.Player;
using MakeGame.Systems;

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

        [Tooltip("상태 이상이 새로 시작된 직후 강하게 강조하는 시간(초). 이 시간이 지나면 조용한 상시 표시로 물러난다.")]
        public float onsetEmphasisSeconds = 1.8f;

        // 상태 이상별 색(ArtDirection.md 1.1/1.2 팔레트 그대로).
        private static readonly Color BleedingColor = new Color(0.8f, 0.2f, 0.2f, 1f);   // Danger Red #CC3333
        private static readonly Color PoisonColor = new Color(0.5f, 0.85f, 0.2f, 1f);    // Toxic Green #80D933
        private static readonly Color BrokenBoneColor = new Color(0.8f, 0.8f, 0.8f, 1f); // Neutral Gray #CCCCCC
        private static readonly Color SunstrokeColor = new Color(0.9f, 0.75f, 0.2f, 1f); // Sunstroke Gold #E6BF33
        private static readonly Color DrowningColor = new Color(0.3f, 0.85f, 0.8f, 1f);  // Oxygen Cyan #4CD9CC

        private GameObject panelRoot;
        private CanvasGroup canvasGroup;
        private Image backgroundImage;
        private Text messageLabel;

        // 마지막으로 배너에 반영한 상태 이상 조합. 이 값들이 실제로 바뀐 프레임에만 BuildWarningMessage()로
        // 새 문자열을 만들어 대입한다(#7/#8과 동일한 캐싱 패턴) - 기존 OnGUI 코드는 배너가 떠 있는 동안
        // 매 프레임 List<string> 할당 + string.Join을 다시 했는데, 그보다 개선된 방식이다.
        // 개선(B4, ArtDirection.md 4.2): 일사병/익사(산소 고갈)도 "시작 순간"을 알려야 하는 상태 이상이라
        // 같은 배너에서 함께 다룬다. 판정 임계값은 SurvivalStats가 단일 소스로 노출한 public const를 쓴다.
        private bool lastBleeding;
        private bool lastPoisoned;
        private bool lastBrokenBone;
        private bool lastSunstroke;
        private bool lastDrowning;
        private bool everBuilt = false;

        // 시작 순간 강조 타이머. 0보다 크면 "방금 시작됐다" 구간이라 빠르게 깜빡이고, 0이 되면
        // 조용한 상시 표시(고정 알파, 깜빡임 없음)로 물러난다 - 지속 중 계속 깜빡이면 눈이 피로해진다.
        private float onsetTimer = 0f;
        private Color onsetColor = Color.white;

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
            // 시작 순간에는 배경색을 그 상태 이상의 색으로 물들여("중독이 시작됐다"를 색으로도 구분),
            // 강조 구간이 끝나면 다시 기본 warningColor로 되돌린다.
            backgroundImage = panelRoot.GetComponent<Image>();

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
            // 일사병/익사는 플래그가 아니라 수치라, SurvivalHudUI의 Tier 3 위험 판정과 정확히 같은
            // 임계값(SurvivalStats의 public const)을 써서 "위험 구간 진입"을 상태 이상처럼 취급한다.
            bool isSunstroke = survivalStats.sunstroke / SurvivalStats.MaxStatValue > SurvivalStats.HighSunstrokeRatio;
            bool isDrowning = survivalStats.oxygen / SurvivalStats.MaxStatValue < SurvivalStats.LowOxygenRatio;
            bool anyWarning = isBleeding || isPoisoned || hasBrokenBone || isSunstroke || isDrowning;

            if (!anyWarning)
            {
                SetOpen(false);
                everBuilt = false; // 다음에 다시 경고가 뜰 때 반드시 새로 문구를 만들도록 초기화
                onsetTimer = 0f;
                lastBleeding = false;
                lastPoisoned = false;
                lastBrokenBone = false;
                lastSunstroke = false;
                lastDrowning = false;
                return;
            }

            SetOpen(true);

            // 새로 시작된(false→true) 상태 이상이 있으면 그 순간에만 강조를 건다. 지속 중에는 다시 걸지 않는다.
            bool startedNow = (isBleeding && !lastBleeding) || (isPoisoned && !lastPoisoned)
                || (hasBrokenBone && !lastBrokenBone) || (isSunstroke && !lastSunstroke) || (isDrowning && !lastDrowning);

            if (startedNow)
            {
                // 여러 개가 동시에 시작되는 일은 드물지만, 겹치면 더 급한 것(출혈>중독>익사>골절>일사병) 색을 쓴다.
                onsetColor = (isBleeding && !lastBleeding) ? BleedingColor
                    : (isPoisoned && !lastPoisoned) ? PoisonColor
                    : (isDrowning && !lastDrowning) ? DrowningColor
                    : (hasBrokenBone && !lastBrokenBone) ? BrokenBoneColor
                    : SunstrokeColor;

                onsetTimer = Mathf.Max(0f, onsetEmphasisSeconds);

                // 화면 가장자리 플래시 1회(C단계). "시작된 순간"에만 호출하므로 상시 피해에 플래시를
                // 거는 것이 아니다 - ArtDirection.md 4.2 규칙을 지킨다.
                CombatFeedbackUI.Instance?.TriggerStatusOnset(onsetColor);

                // 같은 순간에 경고음도 한 번. 화면 아래쪽을 보고 있거나 정면 전투에 시선이 묶여 있으면
                // 상단 배너와 가장자리 플래시를 둘 다 놓칠 수 있어, 시선과 무관한 채널이 하나 필요하다.
                // 이 호출은 startedNow(false→true) 게이트 안에 있으므로 상태 이상이 지속되는 동안
                // 반복 재생되지 않는다. 동시에 여러 개가 시작돼도 AudioManager가 최소 간격(0.3초)으로
                // 한 번으로 합쳐 준다.
                AudioManager.Instance?.PlayStatusOnset();
            }

            if (!everBuilt || isBleeding != lastBleeding || isPoisoned != lastPoisoned || hasBrokenBone != lastBrokenBone
                || isSunstroke != lastSunstroke || isDrowning != lastDrowning)
            {
                messageLabel.text = BuildWarningMessage(isSunstroke, isDrowning);
                lastBleeding = isBleeding;
                lastPoisoned = isPoisoned;
                lastBrokenBone = hasBrokenBone;
                lastSunstroke = isSunstroke;
                lastDrowning = isDrowning;
                everBuilt = true;
            }

            // 3단계 피드백(ArtDirection.md 4.2)의 "시작은 강하게, 이후는 물러나게":
            // - 시작 직후(onsetTimer > 0): 배경을 그 상태 이상 색으로 물들이고 빠르게 깜빡여 시선을 뺏는다.
            // - 그 이후: 깜빡임을 멈추고 고정 알파로 조용히 남는다. 지속 내내 깜빡이면 눈이 피로해지고,
            //   정작 "새로 하나 더 걸린 순간"을 구분할 수 없게 된다.
            if (onsetTimer > 0f)
            {
                onsetTimer -= Time.unscaledDeltaTime;

                float pulse = Mathf.PingPong(Time.unscaledTime * pulseSpeed * 2f, 1f);
                canvasGroup.alpha = Mathf.Lerp(0.75f, 1f, pulse);

                if (backgroundImage != null)
                {
                    // 배경은 상태 이상 색을 어둡게(0.55배) 깐다. 중독(연두)/골절(회색)처럼 밝은 색을
                    // 그대로 깔면 흰 글씨가 묻혀 정작 읽어야 할 대처법이 안 보이기 때문이다.
                    Color emphasized = onsetColor * 0.55f;
                    emphasized.a = warningColor.a;
                    backgroundImage.color = emphasized;
                }
            }
            else
            {
                canvasGroup.alpha = 0.6f;
                if (backgroundImage != null)
                    backgroundImage.color = warningColor;
            }
        }

        /// <summary>
        /// 현재 활성화된 상태 이상들을 조합해 하나의 경고 문구로 만든다.
        /// 붕대/해독제/부목처럼 어떤 아이템으로 치료 가능한지, 일사병/익사는 무엇을 해야 하는지도 함께 안내한다.
        /// (일사병/익사는 SurvivalStats에 불리언 플래그가 없어 호출부가 판정 결과를 넘겨준다 - 임계값을
        /// 두 곳에서 각자 계산하지 않기 위함이다.)
        /// </summary>
        private string BuildWarningMessage(bool isSunstroke, bool isDrowning)
        {
            var parts = new System.Collections.Generic.List<string>();

            if (survivalStats.isBleeding)
                parts.Add("⚠ 출혈 중! 붕대로 지혈하세요 (C)");
            if (survivalStats.isPoisoned)
                parts.Add("⚠ 중독 상태! 해독제가 필요합니다 (C)");
            if (survivalStats.hasBrokenBone)
                parts.Add("⚠ 골절 상태! 부목으로 치료하세요 (C)");
            if (isDrowning)
                parts.Add("⚠ 산소 부족! 수면으로 올라가세요");
            if (isSunstroke)
                parts.Add("⚠ 일사병! 그늘로 피하세요");

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
