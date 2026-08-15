using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using MakeGame.Player;
using MakeGame.Systems;

namespace MakeGame.UI
{
    /// <summary>
    /// 정식 UGUI 기반 생존 HUD. DebugHud(OnGUI로 숫자를 텍스트로 나열하던 "임시" 화면)를 대체하는
    /// 플레이어용 화면으로, 체력/허기/갈증/일사병/산소를 색상 막대 바로, 중독/출혈/골절 상태 이상을
    /// 활성화됐을 때만 나타나는 아이콘으로, 경과 일수와 배/경비행기 진행도를 짧은 텍스트로 보여준다.
    /// 화면 좌상단에 항상 표시되며, 씬에 미리 배치할 필요 없이 스스로 생성된다.
    /// 버그 관련: DontDestroyOnLoad 싱글턴(CombatFeedbackUI 방식)이 아니라 DayNightCycle과 동일한
    /// RuntimeInitializeLoadType.SubsystemRegistration + SceneManager.sceneLoaded 패턴을 쓴다.
    /// 이 HUD가 참조하는 SurvivalStats 등은 Player 오브젝트에 있는데, 사망 후 재시작(SceneManager.LoadScene)
    /// 시 씬 전체가 새로 만들어져 Player도 새 인스턴스가 되므로, AfterSceneLoad(최초 1회만 호출)로 만들면
    /// 재시작 후 HUD가 죽은 참조를 들고 있거나 아예 사라지는 문제가 생긴다. sceneLoaded 이벤트를 구독해
    /// 씬이 몇 번을 다시 로드되더라도 그때마다 새 HUD가 새 참조로 생성되게 한다.
    /// </summary>
    public class SurvivalHudUI : MonoBehaviour
    {
        private SurvivalStats survivalStats;
        private SurvivalClock survivalClock;
        private BoatConstructionSystem boatConstruction;
        private AircraftRepairSystem aircraftRepair;

        private Image healthFill;
        private Image hungerFill;
        private Image thirstFill;
        private Image sunstrokeFill;
        private Image oxygenFill;

        // 각 막대의 평소 색상(경고 상태가 아닐 때로 되돌아갈 기준값). CreateStatBar에서 채워진다.
        private Color healthBaseColor;
        private Color hungerBaseColor;
        private Color thirstBaseColor;
        private Color sunstrokeBaseColor;
        private Color oxygenBaseColor;

        // 위험 수준일 때 막대가 이 색으로 깜빡인다 (선명한 경고색).
        private static readonly Color WarningColor = new Color(1f, 0.15f, 0.15f, 1f);

        private Text dayLabel;
        private Text boatLabel;
        private Text aircraftLabel;

        private GameObject poisonIcon;
        private GameObject bleedingIcon;
        private GameObject brokenBoneIcon;

        // 성능 개선(#7): Update()가 매 프레임 값 변화와 무관하게 $"..." 문자열 보간으로 .text를 다시 만들면
        // 불필요한 GC 할당이 누적된다. 화면에 실제로 표시되는 값(정수 일수/단계/퍼센트, 불리언 플래그)을
        // 캐시해두고, 그 표시용 값이 실제로 바뀐 프레임에만 문자열을 새로 만들어 대입한다.
        // day/boat 단계/aircraft 퍼센트는 정수라 float 오차 없이 "==" 비교가 안전하다. 다만 최초 1회는
        // 반드시 갱신돼야 하므로 각 값이 절대 나올 수 없는 범위(-1 등)를 "아직 표시한 적 없음" 센티널로 둔다.
        private int lastDisplayedDay = -1;
        private int lastDisplayedBoatStage = -1;
        private bool lastDisplayedBoatHasBlueprint;
        private bool boatLabelDisplayed = false;
        private int lastDisplayedAircraftPercent = -1;
        private bool lastDisplayedAircraftComplete;
        private bool aircraftLabelDisplayed = false;

        /// <summary>
        /// 씬이 로드될 때마다(최초 시작이든 재시작이든) 새 SurvivalHudUI를 생성한다.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Bootstrap()
        {
            SceneManager.sceneLoaded += (scene, mode) =>
            {
                var go = new GameObject("SurvivalHudUI");
                go.AddComponent<SurvivalHudUI>();
            };
        }

        /// <summary>
        /// 씬에서 표시할 대상 시스템들을 찾아 참조를 캐시하고 UI를 생성한다.
        /// </summary>
        private void Start()
        {
            survivalStats = FindAnyObjectByType<SurvivalStats>();
            survivalClock = FindAnyObjectByType<SurvivalClock>();
            boatConstruction = FindAnyObjectByType<BoatConstructionSystem>();
            aircraftRepair = FindAnyObjectByType<AircraftRepairSystem>();

            BuildUI();
        }

        /// <summary>
        /// 캔버스와 패널, 5개 수치 막대, 상태 이상 아이콘 3종, 배/비행기 진행도 텍스트를 생성한다.
        /// </summary>
        private void BuildUI()
        {
            var canvas = UIBuilder.CreateCanvas("SurvivalHudCanvas", sortOrder: 5);

            var panel = UIBuilder.CreatePanel(
                canvas.transform, "SurvivalHudPanel",
                anchorMin: new Vector2(0f, 1f), anchorMax: new Vector2(0f, 1f),
                offsetMin: new Vector2(20f, -272f), offsetMax: new Vector2(300f, -20f),
                color: new Color(0f, 0f, 0f, 0.55f));

            var vlg = panel.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(12, 12, 10, 10);
            vlg.spacing = 6f;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            vlg.childAlignment = TextAnchor.UpperLeft;

            dayLabel = UIBuilder.CreateText(panel, "DayLabel", "1일차", 16, Color.white, TextAnchor.MiddleLeft);
            dayLabel.gameObject.AddComponent<LayoutElement>().minHeight = 22f;

            healthBaseColor = new Color(0.85f, 0.2f, 0.2f, 1f);
            hungerBaseColor = new Color(0.85f, 0.55f, 0.2f, 1f);
            thirstBaseColor = new Color(0.25f, 0.55f, 0.85f, 1f);
            sunstrokeBaseColor = new Color(0.9f, 0.75f, 0.2f, 1f);
            oxygenBaseColor = new Color(0.3f, 0.85f, 0.8f, 1f);

            healthFill = CreateStatBar(panel, "체력", healthBaseColor, "stat_health");
            hungerFill = CreateStatBar(panel, "허기", hungerBaseColor, "stat_hunger");
            thirstFill = CreateStatBar(panel, "갈증", thirstBaseColor, "stat_thirst");
            sunstrokeFill = CreateStatBar(panel, "일사병", sunstrokeBaseColor, "stat_sunstroke");
            oxygenFill = CreateStatBar(panel, "산소", oxygenBaseColor, "stat_oxygen");

            // 상태 이상 아이콘 줄: 평소엔 숨겨져 있다가 중독/출혈/골절 상태일 때만 나타난다.
            var statusRowGo = new GameObject("StatusRow", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            statusRowGo.transform.SetParent(panel, false);
            statusRowGo.GetComponent<LayoutElement>().minHeight = 22f;
            var statusHlg = statusRowGo.GetComponent<HorizontalLayoutGroup>();
            statusHlg.spacing = 6f;
            statusHlg.childForceExpandWidth = false;
            statusHlg.childForceExpandHeight = true;
            statusHlg.childAlignment = TextAnchor.MiddleLeft;

            poisonIcon = CreateStatusIcon(statusRowGo.transform, "중독", new Color(0.5f, 0.85f, 0.2f, 1f), "status_poison");
            bleedingIcon = CreateStatusIcon(statusRowGo.transform, "출혈", new Color(0.8f, 0.1f, 0.1f, 1f), "status_bleeding");
            brokenBoneIcon = CreateStatusIcon(statusRowGo.transform, "골절", new Color(0.8f, 0.8f, 0.8f, 1f), "status_broken_bone");

            boatLabel = UIBuilder.CreateText(panel, "BoatLabel", "", 13, new Color(0.85f, 0.85f, 0.85f, 1f), TextAnchor.MiddleLeft);
            boatLabel.gameObject.AddComponent<LayoutElement>().minHeight = 18f;

            aircraftLabel = UIBuilder.CreateText(panel, "AircraftLabel", "", 13, new Color(0.85f, 0.85f, 0.85f, 1f), TextAnchor.MiddleLeft);
            aircraftLabel.gameObject.AddComponent<LayoutElement>().minHeight = 18f;
        }

        /// <summary>
        /// "글리프 아이콘 + 라벨 + 가로 막대"로 구성된 수치 한 줄을 만들고, 매 프레임 갱신할 Fill Image를 반환한다.
        /// 퀄리티 개선: 예전엔 텍스트 라벨만 있어 한눈에 어떤 수치인지 알아보려면 글자를 읽어야 했다.
        /// Resources/Sprites의 글리프 아이콘(스프라이트 이름은 iconSpriteName)을 막대 색으로 틴트해
        /// 라벨 왼쪽에 붙이면, 색+아이콘 조합만으로도 바로 구분된다.
        /// </summary>
        private Image CreateStatBar(Transform parent, string label, Color fillColor, string iconSpriteName)
        {
            var rowGo = new GameObject($"Row_{label}", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            rowGo.transform.SetParent(parent, false);
            rowGo.GetComponent<LayoutElement>().minHeight = 20f;
            var hlg = rowGo.GetComponent<HorizontalLayoutGroup>();
            hlg.spacing = 6f;
            hlg.childForceExpandWidth = false;
            hlg.childForceExpandHeight = true;
            hlg.childAlignment = TextAnchor.MiddleLeft;

            var iconRt = UIBuilder.CreateIcon(rowGo.transform, "Icon", 16f, Color.clear, "");
            var iconImage = iconRt.GetComponent<Image>();
            var iconSprite = Resources.Load<Sprite>($"Sprites/{iconSpriteName}");
            if (iconSprite != null && iconImage != null)
            {
                iconImage.sprite = iconSprite;
                iconImage.color = fillColor;
                iconImage.type = Image.Type.Simple;
                iconImage.preserveAspect = true;
            }

            var labelText = UIBuilder.CreateText(rowGo.transform, "Label", label, 12, Color.white, TextAnchor.MiddleLeft);
            labelText.gameObject.AddComponent<LayoutElement>().preferredWidth = 40f;

            var barFill = UIBuilder.CreateProgressBar(rowGo.transform, "Bar", new Color(1f, 1f, 1f, 0.15f), fillColor);
            var barLayout = barFill.transform.parent.gameObject.AddComponent<LayoutElement>();
            barLayout.flexibleWidth = 1f;
            barLayout.minHeight = 14f;

            return barFill;
        }

        /// <summary>
        /// 막대의 색을 위험 여부에 따라 갱신한다. 위험하지 않으면 평소 색으로, 위험하면 평소 색과
        /// 경고색(WarningColor) 사이를 Mathf.PingPong으로 오가며 깜빡이는 색으로 바꾼다.
        /// </summary>
        private void ApplyWarningPulse(Image fill, Color baseColor, bool isDanger)
        {
            if (!isDanger)
            {
                fill.color = baseColor;
                return;
            }

            float pulse = Mathf.PingPong(Time.unscaledTime * 2.5f, 1f);
            fill.color = Color.Lerp(baseColor, WarningColor, pulse);
        }

        /// <summary>
        /// 상태 이상 하나를 나타내는 작은 아이콘을 만들되, 기본적으로는 비활성화(숨김) 상태로 둔다.
        /// 퀄리티 개선: 예전엔 색 배경 + 첫 글자(중/출/골) 조합이라 작은 크기에서 글자가 뭉개져 잘
        /// 안 읽혔다. 해골/핏방울/뼈 글리프로 바꿔 글자 없이도 상태를 구분할 수 있게 했다.
        /// </summary>
        private GameObject CreateStatusIcon(Transform parent, string label, Color color, string iconSpriteName)
        {
            var icon = UIBuilder.CreateIcon(parent, $"Status_{label}", 18f, color, "");
            var iconSprite = Resources.Load<Sprite>($"Sprites/{iconSpriteName}");
            var image = icon.GetComponent<Image>();
            if (iconSprite != null && image != null)
            {
                // 알파가 있는 글리프라 배경은 자연히 투명해지고, color는 그대로 카테고리 색으로 유지해
                // 해골(중독)/핏방울(출혈)/뼈(골절)가 각자 지정된 색으로 그려지게 한다.
                image.sprite = iconSprite;
                image.type = Image.Type.Simple;
                image.preserveAspect = true;
            }
            icon.gameObject.SetActive(false);
            return icon.gameObject;
        }

        /// <summary>
        /// 매 프레임 생존 수치/상태 이상/진행도를 최신 값으로 갱신한다.
        /// </summary>
        private void Update()
        {
            if (survivalClock != null)
            {
                int day = survivalClock.ElapsedDays + 1;
                // 정수값이 실제로 바뀐 프레임에만 새 문자열을 만들어 대입한다 (그 외 프레임은 이전 프레임과
                // 화면에 보이는 결과가 완전히 동일하므로 매번 다시 만들 필요가 없다).
                if (day != lastDisplayedDay)
                {
                    dayLabel.text = $"{day}일차";
                    lastDisplayedDay = day;
                }
            }

            if (survivalStats != null)
            {
                float healthRatio = survivalStats.maxHealth > 0f
                    ? Mathf.Clamp01(survivalStats.health / survivalStats.maxHealth)
                    : 0f;
                // 개선(B2-9): 허기/갈증/일사병/산소의 최대치 100을 UI가 직접 알고 나누던 것을,
                // SurvivalStats가 단일 소스로 노출한 MaxStatValue 참조로 바꿨다(값은 100f로 동일).
                // health/maxHealth는 원래도 survivalStats.maxHealth를 실제로 읽고 있어 대상이 아니다.
                float hungerRatio = Mathf.Clamp01(survivalStats.hunger / SurvivalStats.MaxStatValue);
                float thirstRatio = Mathf.Clamp01(survivalStats.thirst / SurvivalStats.MaxStatValue);
                float sunstrokeRatio = Mathf.Clamp01(survivalStats.sunstroke / SurvivalStats.MaxStatValue);
                float oxygenRatio = Mathf.Clamp01(survivalStats.oxygen / SurvivalStats.MaxStatValue);

                healthFill.fillAmount = healthRatio;
                hungerFill.fillAmount = hungerRatio;
                thirstFill.fillAmount = thirstRatio;
                sunstrokeFill.fillAmount = sunstrokeRatio;
                oxygenFill.fillAmount = oxygenRatio;

                // 위험 수준(체력/허기/갈증/산소는 낮을 때, 일사병은 반대로 높을 때)일 때 막대 색을
                // 평소 색과 경고색 사이로 깜빡이게 해 raw 숫자를 보지 않아도 한눈에 위험을 알 수 있게 한다.
                // 개선(#10): 위험 임계값(0.25f/0.2f/0.8f)을 UI에 하드코딩해두면 SurvivalStats의 밸런스가
                // 바뀌어도 조용히 어긋날 수 있었다. SurvivalStats가 단일 소스로 노출한 public const를
                // 그대로 참조해, 게임 규칙 값 자체는 항상 시스템 쪽에서만 정의되게 했다(값은 기존과 동일).
                ApplyWarningPulse(healthFill, healthBaseColor, healthRatio < SurvivalStats.LowHealthRatio);
                ApplyWarningPulse(hungerFill, hungerBaseColor, hungerRatio < SurvivalStats.LowHungerRatio);
                ApplyWarningPulse(thirstFill, thirstBaseColor, thirstRatio < SurvivalStats.LowThirstRatio);
                ApplyWarningPulse(sunstrokeFill, sunstrokeBaseColor, sunstrokeRatio > SurvivalStats.HighSunstrokeRatio);
                ApplyWarningPulse(oxygenFill, oxygenBaseColor, oxygenRatio < SurvivalStats.LowOxygenRatio);

                poisonIcon.SetActive(survivalStats.isPoisoned);
                bleedingIcon.SetActive(survivalStats.isBleeding);
                brokenBoneIcon.SetActive(survivalStats.hasBrokenBone);
            }

            if (boatConstruction != null)
            {
                int boatStage = boatConstruction.currentStage;
                bool hasBlueprint = boatConstruction.hasCurrentStageBlueprint;
                // 단계 숫자 또는 도면 보유 여부(문구에 "(도면 필요)"가 붙는지) 둘 중 하나라도 바뀌었을 때만
                // 갱신한다. 두 값 다 정수/불리언이라 프레임마다 흔들리는 float 오차 걱정 없이 "==" 비교로 충분하다.
                if (!boatLabelDisplayed || boatStage != lastDisplayedBoatStage || hasBlueprint != lastDisplayedBoatHasBlueprint)
                {
                    boatLabel.text = $"배: {boatStage}/{BoatConstructionSystem.TotalStages}단계"
                        + (hasBlueprint ? "" : " (도면 필요)");
                    lastDisplayedBoatStage = boatStage;
                    lastDisplayedBoatHasBlueprint = hasBlueprint;
                    boatLabelDisplayed = true;
                }
            }

            if (aircraftRepair != null)
            {
                // 화면에는 F0(반올림된 정수 %)로 표시되므로, 원본 float가 미세하게 흔들려도 반올림 결과가
                // 같으면 화면상 결과는 동일하다. 그래서 float를 직접 비교하지 않고, 표시에 쓰는 것과 동일한
                // 반올림 정수값으로 변환한 뒤 비교한다 - 이러면 "보이지 않는 변화"로 인한 불필요한 갱신도 막는다.
                int aircraftPercent = Mathf.RoundToInt(aircraftRepair.GetOverallProgress() * 100f);
                bool isComplete = aircraftRepair.isRepairComplete;
                if (!aircraftLabelDisplayed || aircraftPercent != lastDisplayedAircraftPercent || isComplete != lastDisplayedAircraftComplete)
                {
                    aircraftLabel.text = $"경비행기: {aircraftPercent}%"
                        + (isComplete ? " (완료)" : "");
                    lastDisplayedAircraftPercent = aircraftPercent;
                    lastDisplayedAircraftComplete = isComplete;
                    aircraftLabelDisplayed = true;
                }
            }
        }
    }
}
