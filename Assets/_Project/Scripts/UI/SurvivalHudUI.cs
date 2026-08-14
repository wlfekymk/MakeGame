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

            healthFill = CreateStatBar(panel, "체력", healthBaseColor);
            hungerFill = CreateStatBar(panel, "허기", hungerBaseColor);
            thirstFill = CreateStatBar(panel, "갈증", thirstBaseColor);
            sunstrokeFill = CreateStatBar(panel, "일사병", sunstrokeBaseColor);
            oxygenFill = CreateStatBar(panel, "산소", oxygenBaseColor);

            // 상태 이상 아이콘 줄: 평소엔 숨겨져 있다가 중독/출혈/골절 상태일 때만 나타난다.
            var statusRowGo = new GameObject("StatusRow", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            statusRowGo.transform.SetParent(panel, false);
            statusRowGo.GetComponent<LayoutElement>().minHeight = 22f;
            var statusHlg = statusRowGo.GetComponent<HorizontalLayoutGroup>();
            statusHlg.spacing = 6f;
            statusHlg.childForceExpandWidth = false;
            statusHlg.childForceExpandHeight = true;
            statusHlg.childAlignment = TextAnchor.MiddleLeft;

            poisonIcon = CreateStatusIcon(statusRowGo.transform, "중독", new Color(0.5f, 0.85f, 0.2f, 1f));
            bleedingIcon = CreateStatusIcon(statusRowGo.transform, "출혈", new Color(0.8f, 0.1f, 0.1f, 1f));
            brokenBoneIcon = CreateStatusIcon(statusRowGo.transform, "골절", new Color(0.8f, 0.8f, 0.8f, 1f));

            boatLabel = UIBuilder.CreateText(panel, "BoatLabel", "", 13, new Color(0.85f, 0.85f, 0.85f, 1f), TextAnchor.MiddleLeft);
            boatLabel.gameObject.AddComponent<LayoutElement>().minHeight = 18f;

            aircraftLabel = UIBuilder.CreateText(panel, "AircraftLabel", "", 13, new Color(0.85f, 0.85f, 0.85f, 1f), TextAnchor.MiddleLeft);
            aircraftLabel.gameObject.AddComponent<LayoutElement>().minHeight = 18f;
        }

        /// <summary>
        /// "라벨 + 가로 막대"로 구성된 수치 한 줄을 만들고, 매 프레임 갱신할 Fill Image를 반환한다.
        /// </summary>
        private Image CreateStatBar(Transform parent, string label, Color fillColor)
        {
            var rowGo = new GameObject($"Row_{label}", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            rowGo.transform.SetParent(parent, false);
            rowGo.GetComponent<LayoutElement>().minHeight = 20f;
            var hlg = rowGo.GetComponent<HorizontalLayoutGroup>();
            hlg.spacing = 6f;
            hlg.childForceExpandWidth = false;
            hlg.childForceExpandHeight = true;
            hlg.childAlignment = TextAnchor.MiddleLeft;

            var labelText = UIBuilder.CreateText(rowGo.transform, "Label", label, 12, Color.white, TextAnchor.MiddleLeft);
            labelText.gameObject.AddComponent<LayoutElement>().preferredWidth = 44f;

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
        /// </summary>
        private GameObject CreateStatusIcon(Transform parent, string label, Color color)
        {
            var icon = UIBuilder.CreateIcon(parent, $"Status_{label}", 18f, color, label.Substring(0, 1));
            icon.gameObject.SetActive(false);
            return icon.gameObject;
        }

        /// <summary>
        /// 매 프레임 생존 수치/상태 이상/진행도를 최신 값으로 갱신한다.
        /// </summary>
        private void Update()
        {
            if (survivalClock != null)
                dayLabel.text = $"{survivalClock.ElapsedDays + 1}일차";

            if (survivalStats != null)
            {
                float healthRatio = survivalStats.maxHealth > 0f
                    ? Mathf.Clamp01(survivalStats.health / survivalStats.maxHealth)
                    : 0f;
                float hungerRatio = Mathf.Clamp01(survivalStats.hunger / 100f);
                float thirstRatio = Mathf.Clamp01(survivalStats.thirst / 100f);
                float sunstrokeRatio = Mathf.Clamp01(survivalStats.sunstroke / 100f);
                float oxygenRatio = Mathf.Clamp01(survivalStats.oxygen / 100f);

                healthFill.fillAmount = healthRatio;
                hungerFill.fillAmount = hungerRatio;
                thirstFill.fillAmount = thirstRatio;
                sunstrokeFill.fillAmount = sunstrokeRatio;
                oxygenFill.fillAmount = oxygenRatio;

                // 위험 수준(체력/허기/갈증/산소는 낮을 때, 일사병은 반대로 높을 때)일 때 막대 색을
                // 평소 색과 경고색 사이로 깜빡이게 해 raw 숫자를 보지 않아도 한눈에 위험을 알 수 있게 한다.
                ApplyWarningPulse(healthFill, healthBaseColor, healthRatio < 0.25f);
                ApplyWarningPulse(hungerFill, hungerBaseColor, hungerRatio < 0.2f);
                ApplyWarningPulse(thirstFill, thirstBaseColor, thirstRatio < 0.2f);
                ApplyWarningPulse(sunstrokeFill, sunstrokeBaseColor, sunstrokeRatio > 0.8f);
                ApplyWarningPulse(oxygenFill, oxygenBaseColor, oxygenRatio < 0.25f);

                poisonIcon.SetActive(survivalStats.isPoisoned);
                bleedingIcon.SetActive(survivalStats.isBleeding);
                brokenBoneIcon.SetActive(survivalStats.hasBrokenBone);
            }

            if (boatConstruction != null)
            {
                boatLabel.text = $"배: {boatConstruction.currentStage}/{BoatConstructionSystem.TotalStages}단계"
                    + (boatConstruction.hasCurrentStageBlueprint ? "" : " (도면 필요)");
            }

            if (aircraftRepair != null)
            {
                aircraftLabel.text = $"경비행기: {(aircraftRepair.GetOverallProgress() * 100f):F0}%"
                    + (aircraftRepair.isRepairComplete ? " (완료)" : "");
            }
        }
    }
}
