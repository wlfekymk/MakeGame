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

        [Header("일몰 예고 (Design_Onboarding 6장)")]
        [Tooltip("일몰 예고를 발생시키는 시계. 비워두면 씬에서 자동으로 찾는다.")]
        public SurvivalClock survivalClock;

        [Tooltip("일몰 예고 배너가 화면에 완전히 보이는 시간(초). 이 시간이 지나면 아래 fadeSeconds 동안 사라진다.")]
        public float sunsetNoticeSeconds = 6f;

        [Tooltip("일몰 예고 배너가 사라질 때의 페이드 시간(초).")]
        public float sunsetFadeSeconds = 1.2f;

        // 상태 이상별 색(ArtDirection.md 1.1/1.2 팔레트 그대로).
        private static readonly Color BleedingColor = new Color(0.8f, 0.2f, 0.2f, 1f);   // Danger Red #CC3333
        private static readonly Color PoisonColor = new Color(0.5f, 0.85f, 0.2f, 1f);    // Toxic Green #80D933
        private static readonly Color BrokenBoneColor = new Color(0.8f, 0.8f, 0.8f, 1f); // Neutral Gray #CCCCCC
        private static readonly Color SunstrokeColor = new Color(0.9f, 0.75f, 0.2f, 1f); // Sunstroke Gold #E6BF33
        private static readonly Color DrowningColor = new Color(0.3f, 0.85f, 0.8f, 1f);  // Oxygen Cyan #4CD9CC

        // 일몰 예고 문구는 Docs/Design_Onboarding.md 6장 확정본을 그대로 쓴다(임의로 바꾸지 말 것).
        private const string SunsetNoticeText = "곧 밤이 됩니다 — 불을 피우거나 안전한 곳으로";

        // 안내 문구 색. 새 색을 만들지 않는다(ArtDirection.md 1장) - SurvivalHudUI의 목표 1줄,
        // MinimapUI.statusLabel이 이미 "주목시키는 안내 문구"에 쓰고 있는 옅은 금색 그대로다.
        private static readonly Color SunsetNoticeColor = new Color(1f, 0.9f, 0.4f, 1f);

        private GameObject panelRoot;
        private CanvasGroup canvasGroup;
        private Image backgroundImage;
        private Text messageLabel;

        // 일몰 예고 배너. 상태 이상 배너와 같은 캔버스를 쓰고(새 캔버스를 만들지 않는다) 바로 아래
        // 줄에 놓여, 둘이 동시에 떠도 서로 가리지 않는다. 표시/숨김은 상태 이상 쪽과 완전히 독립이다
        // (상태 이상이 없다고 예고가 꺼지면 안 되고, 그 반대도 안 된다).
        private GameObject sunsetPanelRoot;
        private CanvasGroup sunsetCanvasGroup;

        // 남은 표시 시간(초). 0 이하이면 배너가 닫혀 있다는 뜻이다.
        private float sunsetRemaining = 0f;

        // 실제로 구독한 시계. OnDestroy에서 반드시 같은 인스턴스에서 해제해야 하므로 따로 들고 있는다
        // (survivalClock 공개 필드는 인스펙터에서 도중에 바뀔 수 있어 해제 대상 기준으로 쓸 수 없다).
        private SurvivalClock subscribedClock;

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

        // 치료 키. 예전에는 문구에 "(C)"가 박혀 있었는데, 실제 키는 InteractionController.consumeKey가
        // 정한다(씬에서 바뀔 수 있다). 값이 갈라지면 화면이 거짓말을 하므로 실제 필드를 읽어 캐시한다.
        // 표기는 조준 프롬프트/인벤토리 사용법과 같은 "[키] 동작" 형식으로 통일했다 - 지금 눌러야 할
        // 키를 알려주는 문장이라 명사 뒤 괄호 표기(제작(V) 같은 서술형)와 성격이 다르다(보고서 [결정] 1).
        private KeyCode consumeKey = KeyCode.C;

        /// <summary>
        /// 시작 시 배너 UI 계층을 생성하고 기본적으로 닫힌 상태로 둔다.
        /// </summary>
        private void Start()
        {
            var interaction = FindAnyObjectByType<InteractionController>();
            if (interaction != null)
                consumeKey = interaction.consumeKey;

            BuildUI();
            SetOpen(false);
            SetSunsetOpen(false);

            SubscribeSunsetWarning();
        }

        /// <summary>
        /// 일몰 예고 이벤트를 구독한다. 이미 발생한 뒤에 이 UI가 만들어졌을 수도 있으므로
        /// (SurvivalClock 주석이 명시한 폴링 경로) 그 경우는 남은 시간을 계산해 이어서 띄운다.
        ///
        /// SunsetWarningFired만 보면 안 된다 - 시계는 "예고할 날이 이미 지나갔다"(불러오기로 5일차
        /// 시작 등)일 때도 이벤트 없이 Fired만 true로 소진한다. 실제 발생 여부는 SunsetWarningTime이
        /// 0 이상인지로 구분해야 한다.
        /// </summary>
        private void SubscribeSunsetWarning()
        {
            if (survivalClock == null)
                survivalClock = FindAnyObjectByType<SurvivalClock>();

            if (survivalClock == null)
                return;

            subscribedClock = survivalClock;
            subscribedClock.SunsetWarningRaised += OnSunsetWarningRaised;

            // 구독 전에 이미 발생했다면 놓친 만큼을 빼고 남은 시간만 표시한다.
            if (subscribedClock.SunsetWarningFired && subscribedClock.SunsetWarningTime >= 0f)
            {
                float elapsed = Time.time - subscribedClock.SunsetWarningTime;
                float total = Mathf.Max(0f, sunsetNoticeSeconds) + Mathf.Max(0f, sunsetFadeSeconds);
                float remaining = total - elapsed;
                if (remaining > 0f)
                {
                    sunsetRemaining = remaining;
                    SetSunsetOpen(true);
                }
            }
        }

        /// <summary>
        /// 구독을 반드시 해제한다. SurvivalClock은 씬 오브젝트라 씬 리로드 시 함께 파괴되지만,
        /// 이 UI가 먼저 파괴되는 경우(오브젝트 비활성/파괴)에는 죽은 델리게이트가 시계에 남는다.
        /// </summary>
        private void OnDestroy()
        {
            if (subscribedClock != null)
            {
                subscribedClock.SunsetWarningRaised -= OnSunsetWarningRaised;
                subscribedClock = null;
            }
        }

        /// <summary>
        /// 일몰 예고가 발생한 순간 호출된다(세션당 1회 - 1회성 보장은 SurvivalClock의 책임이므로
        /// 여기서 별도 게이트를 두지 않는다).
        /// </summary>
        private void OnSunsetWarningRaised()
        {
            // 경고음은 붙이지 않는다. PlayStatusOnset은 "출혈/중독이 시작됐다"는 위급 신호에 배정된
            // 소리라(ArtDirection.md 4.2의 3단계 피드백), 아직 아무 피해도 없는 안내에 같은 소리를
            // 쓰면 위급 신호의 의미가 희석된다.
            sunsetRemaining = Mathf.Max(0f, sunsetNoticeSeconds) + Mathf.Max(0f, sunsetFadeSeconds);
            SetSunsetOpen(true);
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

            BuildSunsetBanner(canvas.transform);
        }

        /// <summary>
        /// 일몰 예고 배너를 상태 이상 배너와 같은 캔버스 아래에 만든다(새 캔버스를 만들지 않는다).
        /// 배너 연출(반투명 배경 패널 + 그림자 있는 굵은 중앙 정렬 텍스트 + CanvasGroup 알파 제어)은
        /// 위 BuildUI와 동일한 구성 그대로다. 위치만 상태 이상 배너 바로 아래 줄로 내려, 출혈 중에
        /// 밤이 와도 두 문구가 겹치지 않는다.
        /// </summary>
        private void BuildSunsetBanner(Transform canvasTransform)
        {
            // 배경색도 새로 만들지 않고, 이미 있는 일사병 색(#E6BF33)을 어둡게 깐다 - 위 onsetColor를
            // 0.55배로 깔던 것과 같은 방식이다. 밝은 금색을 그대로 깔면 흰 글씨가 묻힌다.
            Color background = SunstrokeColor * 0.3f;
            background.a = 0.8f;

            var panel = UIBuilder.CreatePanel(
                canvasTransform, "SunsetNoticeBanner",
                anchorMin: new Vector2(0.5f, 1f), anchorMax: new Vector2(0.5f, 1f),
                offsetMin: new Vector2(-450f, -112f), offsetMax: new Vector2(450f, -66f),
                color: background);

            sunsetPanelRoot = panel.gameObject;
            sunsetCanvasGroup = sunsetPanelRoot.AddComponent<CanvasGroup>();

            var label = UIBuilder.CreateText(panel, "Message", SunsetNoticeText, 16, SunsetNoticeColor, TextAnchor.MiddleCenter);
            label.fontStyle = FontStyle.Bold;
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            var labelRt = label.rectTransform;
            labelRt.anchorMin = Vector2.zero;
            labelRt.anchorMax = Vector2.one;
            labelRt.offsetMin = Vector2.zero;
            labelRt.offsetMax = Vector2.zero;

            var labelShadow = label.gameObject.AddComponent<Shadow>();
            labelShadow.effectColor = new Color(0f, 0f, 0f, 0.6f);
            labelShadow.effectDistance = new Vector2(1.5f, -1.5f);
        }

        /// <summary>
        /// 일몰 예고 배너의 남은 시간을 줄이고, 마지막 sunsetFadeSeconds 구간에서 서서히 사라지게 한다.
        /// Time.timeScale이 0인 화면(설정/게임오버/엔딩) 위에서 타이머가 멈춰 배너가 영원히 남지
        /// 않도록 unscaledDeltaTime을 쓴다(AGENT_BRIEF 4장 함정).
        /// </summary>
        private void UpdateSunsetNotice()
        {
            if (sunsetRemaining <= 0f)
                return;

            sunsetRemaining -= Time.unscaledDeltaTime;
            if (sunsetRemaining <= 0f)
            {
                sunsetRemaining = 0f;
                SetSunsetOpen(false);
                return;
            }

            if (sunsetCanvasGroup != null)
            {
                float fade = Mathf.Max(0.0001f, sunsetFadeSeconds);
                sunsetCanvasGroup.alpha = Mathf.Clamp01(sunsetRemaining / fade);
            }
        }

        /// <summary>
        /// 일몰 예고 배너를 열거나 닫는다. 열 때는 알파를 1로 되돌린다.
        /// </summary>
        private void SetSunsetOpen(bool open)
        {
            if (sunsetPanelRoot == null)
                return;

            sunsetPanelRoot.SetActive(open);
            if (open && sunsetCanvasGroup != null)
                sunsetCanvasGroup.alpha = 1f;
        }

        /// <summary>
        /// 매 프레임 현재 활성화된 상태 이상을 확인해 배너를 열고 닫고, 열려 있는 동안은 펄스(깜빡임)
        /// 알파를 갱신한다. 표시할 문구 자체는 상태 이상 조합이 실제로 바뀐 프레임에만 다시 만든다.
        /// </summary>
        private void Update()
        {
            // 일몰 예고는 상태 이상과 완전히 독립이다. survivalStats가 연결되지 않은 씬에서도(아래
            // early return 경로) 예고는 정상적으로 뜨고 사라져야 하므로 반드시 먼저 처리한다.
            UpdateSunsetNotice();

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

            string cure = $"[{consumeKey}]";

            if (survivalStats.isBleeding)
                parts.Add($"⚠ 출혈 중! {cure} 붕대로 지혈");
            if (survivalStats.isPoisoned)
                parts.Add($"⚠ 중독 상태! {cure} 해독제 사용");
            if (survivalStats.hasBrokenBone)
                parts.Add($"⚠ 골절 상태! {cure} 부목으로 치료");
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
