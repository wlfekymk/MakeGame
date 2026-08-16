using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using MakeGame.Data;
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
        private Text objectiveLabel;
        private Text boatLabel;
        private Text aircraftLabel;

        // 목표 1줄(Design_Progression.md 3장 5단계 체인)에 쓰는 상태.
        // 원칙: 이 UI는 "무엇이 목표인지"를 판정하지 않는다. 외부(systems)가 SetObjective로 넣어준
        // 문자열을 그대로 표시하는 것이 정상 경로이고, 아직 판정 API가 없는 동안에만 아래
        // ResolveFallbackObjective()가 "지금 있는 public 시그니처만" 읽어 대략적인 문구를 만든다.
        // 한 번이라도 SetObjective가 불리면 그 뒤로는 폴백을 완전히 끈다(외부 판정이 항상 이긴다).
        private bool objectiveInjected = false;
        private string lastDisplayedObjective = null;
        private float objectiveRefreshTimer = 0f;
        private PlayerInventory playerInventory;

        // 목표 문구 갱신 주기(초). 매 프레임 인벤토리를 훑으면 낭비라서 간격을 둔다.
        private const float ObjectiveRefreshInterval = 0.5f;

        /// <summary>
        /// 이 씬의 HUD 인스턴스. 진행 단계 판정 시스템이 매번 FindAnyObjectByType를 하지 않고
        /// 목표 문구를 넣을 수 있도록 노출한다(씬 리로드마다 새 인스턴스로 교체된다).
        /// </summary>
        public static SurvivalHudUI Instance { get; private set; }

        private GameObject poisonIcon;
        private GameObject bleedingIcon;
        private GameObject brokenBoneIcon;

        // 가방(인벤토리 칸) 상태 칩. 상태 이상 아이콘과 같은 줄에 살고, 상태 이상과 같은 규칙으로
        // **필요할 때만 나타난다**(ArtDirection.md 4.1의 "발생 시에만 나타남" = Tier 0 취급).
        private Text inventoryChip;
        private float capacityRefreshTimer = 0f;
        private string lastDisplayedCapacityText = null;
        private Color lastDisplayedCapacityColor;

        // 칸이 차오르는 속도는 초 단위로 느리므로 매 프레임 UsedSlots(소지품 전수 순회)를 셀 이유가 없다.
        private const float CapacityRefreshInterval = 0.25f;

        // 이 비율 이상 차면 칩이 나타난다. **가득 찬 뒤에 알리면 이미 늦다** - 채집 도중에 한 칸씩
        // 줄어드는 것을 보고 정리할 시간을 주는 것이 이 칩의 존재 이유다.
        private const float CapacityWarnRatio = 0.8f;

        // 거부 문구를 화면에 남겨두는 시간(초).
        private const float RejectMessageDuration = 3f;
        private string rejectedItemName = null;
        private float rejectMessageUntil = -1f;

        // 색은 새로 만들지 않는다(ArtDirection.md 1장). 이 파일에 이미 있는 두 값을 그대로 쓴다.
        private static readonly Color CapacityWarnColor = new Color(1f, 0.9f, 0.4f, 1f);   // 목표 줄과 같은 옅은 금색
        private static readonly Color CapacityFullColor = new Color(0.8f, 0.2f, 0.2f, 1f); // Danger Red #CC3333

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
            Instance = this;

            survivalStats = FindAnyObjectByType<SurvivalStats>();
            survivalClock = FindAnyObjectByType<SurvivalClock>();
            boatConstruction = FindAnyObjectByType<BoatConstructionSystem>();
            aircraftRepair = FindAnyObjectByType<AircraftRepairSystem>();
            playerInventory = FindAnyObjectByType<PlayerInventory>();

            BuildUI();

            // 용량 때문에 채집이 거부된 사실은 반드시 화면에 나가야 한다. 이 프로젝트는 이미
            // "채집이 소리도 텍스트도 없이 무시되는" 사고를 냈고, PlayerInventory가 그 통로로
            // AddRejected를 열어두었다(PlayerInventory.cs AddRejected 주석).
            if (playerInventory != null)
                playerInventory.AddRejected += OnInventoryAddRejected;
        }

        /// <summary>
        /// 씬이 다시 로드되면 이 인스턴스는 파괴되고 새 HUD가 생성되므로, 정적 참조가 죽은
        /// 오브젝트를 계속 가리키지 않게 정리한다(CombatFeedbackUI와 동일한 패턴).
        /// </summary>
        private void OnDestroy()
        {
            if (playerInventory != null)
                playerInventory.AddRejected -= OnInventoryAddRejected;

            if (Instance == this)
                Instance = null;
        }

        /// <summary>
        /// 인벤토리가 가득 차 아이템을 받지 못했을 때 호출된다(PlayerInventory.AddRejected).
        /// 새 패널을 만들지 않고, 가방 칩의 문구를 몇 초 동안 "무엇이 거부됐는지"로 덮어쓴다 -
        /// 용량 정보가 원래 살던 자리에서 그대로 사유를 말하게 하는 것이 가장 읽히는 배치다.
        /// </summary>
        private void OnInventoryAddRejected(ItemData itemData)
        {
            rejectedItemName = itemData != null && !string.IsNullOrEmpty(itemData.itemName)
                ? itemData.itemName
                : "아이템";
            rejectMessageUntil = Time.unscaledTime + RejectMessageDuration;

            // 다음 프레임에 곧바로 반영되도록 폴링 대기를 취소한다(0.25초 늦게 뜨면 채집한 순간과 어긋난다).
            capacityRefreshTimer = 0f;
        }

        /// <summary>
        /// 현재 목표 1줄을 외부에서 주입한다(Design_Progression.md 6장 [요청] ui-engineer).
        /// 진행 단계 판정은 전적으로 호출부의 책임이고, 이 UI는 받은 문자열을 그대로 한 줄로 표시만 한다.
        /// 한 번이라도 호출되면 내부 폴백 판정은 영구히 꺼진다 - 외부 판정과 폴백이 매 0.5초마다
        /// 서로 문구를 덮어쓰며 깜빡이는 상황을 원천 차단하기 위함이다.
        /// null이나 빈 문자열을 넣으면 목표 줄이 비워진다(주입 우선 상태는 그대로 유지).
        /// </summary>
        public void SetObjective(string objective)
        {
            objectiveInjected = true;
            ApplyObjectiveText(objective ?? "");
        }

        /// <summary>
        /// 캔버스와 패널, 5개 수치 막대, 상태 이상 아이콘 3종, 배/비행기 진행도 텍스트를 생성한다.
        /// </summary>
        private void BuildUI()
        {
            var canvas = UIBuilder.CreateCanvas("SurvivalHudCanvas", sortOrder: 5);

            // 개선(B4-14, ArtDirection.md 4.3): 카드형 패널임을 알려주는 상단 테두리(2px, 흰색 알파 12%)를 추가.
            var panel = UIBuilder.CreatePanel(
                canvas.transform, "SurvivalHudPanel",
                anchorMin: new Vector2(0f, 1f), anchorMax: new Vector2(0f, 1f),
                // 높이 -272 → -296: 아래 목표 1줄 라벨(18) + 레이아웃 간격(6)만큼 패널을 늘렸다.
                // 새 패널을 만들지 않고 기존 HUD 패널 안에 한 줄만 추가하라는 지시(Design_Progression.md
                // 3장 단계 1 (b) "새 패널을 만들지 않는다")를 그대로 따른 결과다.
                offsetMin: new Vector2(20f, -296f), offsetMax: new Vector2(300f, -20f),
                color: new Color(0f, 0f, 0f, 0.55f),
                addTopBorder: true);

            var vlg = panel.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(12, 12, 10, 10);
            vlg.spacing = 6f;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            vlg.childAlignment = TextAnchor.UpperLeft;

            dayLabel = UIBuilder.CreateText(panel, "DayLabel", "1일차", 16, Color.white, TextAnchor.MiddleLeft);
            dayLabel.gameObject.AddComponent<LayoutElement>().minHeight = 22f;

            // 현재 목표 1줄(Design_Progression.md 3장). "N일차" 바로 아래, 폰트 12 = ArtDirection.md 4.3
            // Body 등급. 진행 단계 판정 결과를 받아 표시만 하는 라벨이고, 패널을 새로 만들지 않는다.
            // 색은 새로 만들지 않는다(ArtDirection.md 1장 원칙). 이미 코드에서 "주목시키는 안내 문구"에
            // 쓰고 있는 값(MinimapUI.statusLabel과 동일한 옅은 금색)을 그대로 재사용해, 회색 보조 진행도
            // 문구(배/경비행기)와는 구분되고 흰색 본문보다는 눈에 먼저 들어오게 한다.
            objectiveLabel = UIBuilder.CreateText(panel, "ObjectiveLabel", "", 12, new Color(1f, 0.9f, 0.4f, 1f), TextAnchor.MiddleLeft);
            objectiveLabel.gameObject.AddComponent<LayoutElement>().minHeight = 18f;

            // 개선(ArtDirection.md 1.3/1.1): "위급/전투" 의미의 빨강이 #CC4040/#D93333/#CC1A1A/#FF2626로
            // 흩어져 있던 것 중, 체력 평상시 색을 Danger Red #CC3333로 통일한다. WarningColor(펄스
            // 목표값, #FF2626)만 의도적으로 더 밝게 유지해 "평상시"와 "지금 위험" 깜빡임을 밝기로 구분한다.
            healthBaseColor = new Color(0.8f, 0.2f, 0.2f, 1f); // Danger Red #CC3333
            hungerBaseColor = new Color(0.85f, 0.55f, 0.2f, 1f);
            thirstBaseColor = new Color(0.25f, 0.55f, 0.85f, 1f);
            sunstrokeBaseColor = new Color(0.9f, 0.75f, 0.2f, 1f);
            oxygenBaseColor = new Color(0.3f, 0.85f, 0.8f, 1f);

            // 개선(B4-12, ArtDirection.md 4.1): 정보 위계 3단.
            // Tier 1(체력, 상시 강조): 바 높이 1.4배(14→20), 라벨 폰트 14 - 0이 되면 사망하는 유일한
            // 최종 지표라 항상 가장 크게 보여준다.
            healthFill = CreateStatBar(panel, "체력", healthBaseColor, "stat_health", barHeight: 20f, labelFontSize: 14);
            // Tier 2(허기·갈증, 상시 표시): 기존 크기(14/12) 그대로, 항상 완전 불투명.
            hungerFill = CreateStatBar(panel, "허기", hungerBaseColor, "stat_hunger");
            thirstFill = CreateStatBar(panel, "갈증", thirstBaseColor, "stat_thirst");
            // Tier 3(일사병·산소, 조건부 흐림): 바 자체 크기는 Tier 2와 동일하고, 대신 Update()에서
            // 안전 구간일 때 알파 0.4로 흐리게 하고 위험 구간 진입 시 1.0 + 경고 펄스로 전환한다.
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
            // 개선(ArtDirection.md 1.3): 출혈 아이콘 색도 체력 바와 동일하게 Danger Red #CC3333로 통일.
            bleedingIcon = CreateStatusIcon(statusRowGo.transform, "출혈", new Color(0.8f, 0.2f, 0.2f, 1f), "status_bleeding");
            brokenBoneIcon = CreateStatusIcon(statusRowGo.transform, "골절", new Color(0.8f, 0.8f, 0.8f, 1f), "status_broken_bone");

            // 가방 칩. 상태 이상 아이콘과 같은 줄을 쓰는 이유는 (1) 그 줄이 이미 "조건부로만 나타나는 것들"의
            // 자리이고, (2) HUD 패널 높이를 늘리지 않아도 되기 때문이다 - 이 줄은 어차피 22px를 늘 비워두고
            // 있어서 새 정보를 넣어도 다른 수치가 밀리지 않는다. HUD에 상시 한 줄을 더하는 것은
            // ArtDirection.md 4.1(정보 위계)에 반한다: 가방 칸은 체력·허기·갈증과 같은 급이 아니다.
            inventoryChip = UIBuilder.CreateText(statusRowGo.transform, "InventoryCapacity", "", 12,
                CapacityWarnColor, TextAnchor.MiddleLeft);
            // 좁은 HUD 폭(280)에서 문구가 두 줄로 접히면 22px 줄 높이를 넘어 아래 진행도 문구를 덮는다.
            inventoryChip.horizontalOverflow = HorizontalWrapMode.Overflow;
            var chipLayout = inventoryChip.gameObject.AddComponent<LayoutElement>();
            chipLayout.minHeight = 18f;
            chipLayout.preferredWidth = 170f;
            inventoryChip.gameObject.SetActive(false);

            // 개선(B4-14, ArtDirection.md 4.3): 폰트 4단계(20/15~16/11~12/16) 밖이었던 13pt를
            // Body 단계(11~12)로 스냅했다(보조 진행도 문구라 본문 취급이 맞다).
            boatLabel = UIBuilder.CreateText(panel, "BoatLabel", "", 12, new Color(0.85f, 0.85f, 0.85f, 1f), TextAnchor.MiddleLeft);
            boatLabel.gameObject.AddComponent<LayoutElement>().minHeight = 18f;

            aircraftLabel = UIBuilder.CreateText(panel, "AircraftLabel", "", 12, new Color(0.85f, 0.85f, 0.85f, 1f), TextAnchor.MiddleLeft);
            aircraftLabel.gameObject.AddComponent<LayoutElement>().minHeight = 18f;
        }

        /// <summary>
        /// "글리프 아이콘 + 라벨 + 가로 막대"로 구성된 수치 한 줄을 만들고, 매 프레임 갱신할 Fill Image를 반환한다.
        /// 퀄리티 개선: 예전엔 텍스트 라벨만 있어 한눈에 어떤 수치인지 알아보려면 글자를 읽어야 했다.
        /// Resources/Sprites의 글리프 아이콘(스프라이트 이름은 iconSpriteName)을 막대 색으로 틴트해
        /// 라벨 왼쪽에 붙이면, 색+아이콘 조합만으로도 바로 구분된다.
        /// 개선(B4-12, ArtDirection.md 4.1): barHeight/labelFontSize를 매개변수화해 Tier별로 크기를
        /// 다르게 줄 수 있게 했다. 기본값(14/12)은 기존 Tier 2/3 크기와 동일해, 호출부를 안 바꾸면
        /// 기존과 완전히 같은 결과가 나온다(Tier 1인 체력만 호출부에서 20/14를 명시로 넘긴다).
        /// </summary>
        private Image CreateStatBar(Transform parent, string label, Color fillColor, string iconSpriteName, float barHeight = 14f, int labelFontSize = 12)
        {
            var rowGo = new GameObject($"Row_{label}", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            rowGo.transform.SetParent(parent, false);
            // 바 높이가 기존 기본 행 높이(20)보다 커질 수 있으므로(Tier 1), 행 자체의 최소 높이도
            // 바 높이에 맞춰 늘려 아이콘/라벨/바가 서로 겹치지 않게 한다.
            rowGo.GetComponent<LayoutElement>().minHeight = Mathf.Max(20f, barHeight);
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

            var labelText = UIBuilder.CreateText(rowGo.transform, "Label", label, labelFontSize, Color.white, TextAnchor.MiddleLeft);
            labelText.gameObject.AddComponent<LayoutElement>().preferredWidth = 40f;

            var barFill = UIBuilder.CreateProgressBar(rowGo.transform, "Bar", new Color(1f, 1f, 1f, 0.15f), fillColor);
            var barLayout = barFill.transform.parent.gameObject.AddComponent<LayoutElement>();
            barLayout.flexibleWidth = 1f;
            barLayout.minHeight = barHeight;

            return barFill;
        }

        /// <summary>
        /// 막대의 색을 위험 여부에 따라 갱신한다. 위험하지 않으면 평소 색으로, 위험하면 평소 색과
        /// 경고색(WarningColor) 사이를 Mathf.PingPong으로 오가며 깜빡이는 색으로 바꾼다.
        /// 개선(B4-12, ArtDirection.md 4.1): safeAlpha 매개변수를 추가했다. Tier 3(일사병/산소)처럼
        /// "안전할 때는 흐리게" 표시해야 하는 막대는 safeAlpha를 0.4로 넘긴다 - 기본값 1f는 기존
        /// Tier 1/2 호출부(체력/허기/갈증)의 동작을 그대로 유지한다. 위험 상태 진입 시(isDanger=true)는
        /// Tier와 무관하게 항상 알파 1.0으로 펄스한다 - 안전 구간의 흐림과 위험 경고가 같은 임계값에서
        /// 전환되므로(Update() 참고), 흐림 때문에 위험 신호를 놓치는 프레임이 생기지 않는다.
        /// </summary>
        private void ApplyWarningPulse(Image fill, Color baseColor, bool isDanger, float safeAlpha = 1f)
        {
            if (!isDanger)
            {
                Color dimmed = baseColor;
                dimmed.a = safeAlpha;
                fill.color = dimmed;
                return;
            }

            float pulse = Mathf.PingPong(Time.unscaledTime * 2.5f, 1f);
            Color pulsedColor = Color.Lerp(baseColor, WarningColor, pulse);
            pulsedColor.a = 1f;
            fill.color = pulsedColor;
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
                // 개선(B4-12, ArtDirection.md 4.1): Tier 3(일사병/산소)는 danger 판정과 같은 프레임에
                // safeAlpha(0.4)→1.0 전환이 함께 일어나도록 동일한 bool을 그대로 재사용한다(아래 참고).
                bool healthDanger = healthRatio < SurvivalStats.LowHealthRatio;
                bool hungerDanger = hungerRatio < SurvivalStats.LowHungerRatio;
                bool thirstDanger = thirstRatio < SurvivalStats.LowThirstRatio;
                bool sunstrokeDanger = sunstrokeRatio > SurvivalStats.HighSunstrokeRatio;
                bool oxygenDanger = oxygenRatio < SurvivalStats.LowOxygenRatio;

                ApplyWarningPulse(healthFill, healthBaseColor, healthDanger);
                ApplyWarningPulse(hungerFill, hungerBaseColor, hungerDanger);
                ApplyWarningPulse(thirstFill, thirstBaseColor, thirstDanger);
                // Tier 3: 안전 구간(danger가 아닐 때)엔 알파 0.4로 흐리게, 위험 구간 진입 시 알파 1.0 +
                // 경고 펄스로 전환한다. safeAlpha와 danger 판정 임계값을 SurvivalStats의 기존 위험 상수
                // (HighSunstrokeRatio/LowOxygenRatio)로 완전히 일치시켜, "흐린 상태에서 위험해지는
                // 순간"과 "펄스가 시작되는 순간"이 정확히 같은 프레임에 겹치게 했다 - 흐림 때문에
                // 위험 진입을 한 프레임이라도 놓칠 여지를 원천 차단한다(코디네이터 지시 사항).
                ApplyWarningPulse(sunstrokeFill, sunstrokeBaseColor, sunstrokeDanger, safeAlpha: 0.4f);
                ApplyWarningPulse(oxygenFill, oxygenBaseColor, oxygenDanger, safeAlpha: 0.4f);

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

            UpdateInventoryCapacityChip();
            UpdateObjectiveFallback();
        }

        /// <summary>
        /// 가방 칩을 갱신한다. 평소에는 아예 보이지 않고, 다음 두 경우에만 나타난다:
        /// · 칸이 CapacityWarnRatio(80%) 이상 찼다 → "가방 24/30"(금색), 꽉 차면 Danger Red.
        /// · 방금 용량 때문에 채집이 거부됐다 → 몇 초 동안 무엇이 거부됐는지를 그 자리에 표시.
        /// 창(Tab)을 열어야만 알 수 있으면 채집하다 가득 찬 것을 모른 채 계속 줍게 되므로 HUD에도 둔다.
        /// </summary>
        private void UpdateInventoryCapacityChip()
        {
            if (inventoryChip == null || playerInventory == null)
                return;

            capacityRefreshTimer -= Time.unscaledDeltaTime;
            if (capacityRefreshTimer > 0f)
                return;
            capacityRefreshTimer = CapacityRefreshInterval;

            int used = playerInventory.UsedSlots;
            int capacity = playerInventory.SlotCapacity;
            bool isFull = used >= capacity;
            bool isNearFull = capacity > 0 && (float)used / capacity >= CapacityWarnRatio;
            bool showReject = Time.unscaledTime <= rejectMessageUntil && !string.IsNullOrEmpty(rejectedItemName);

            bool show = showReject || isNearFull;
            if (inventoryChip.gameObject.activeSelf != show)
                inventoryChip.gameObject.SetActive(show);

            if (!show)
                return;

            string text = showReject
                ? $"가방 가득 참 · {rejectedItemName} 못 챙김"
                : $"가방 {used}/{capacity}";
            Color color = (showReject || isFull) ? CapacityFullColor : CapacityWarnColor;

            if (text != lastDisplayedCapacityText)
            {
                inventoryChip.text = text;
                lastDisplayedCapacityText = text;
            }

            if (color != lastDisplayedCapacityColor)
            {
                inventoryChip.color = color;
                lastDisplayedCapacityColor = color;
            }
        }

        /// <summary>
        /// 진행 단계 판정 API가 아직 없는 동안만 도는 최소 폴백. SetObjective가 한 번이라도 불렸으면
        /// 즉시 아무 일도 하지 않는다. Time.timeScale이 0인 화면(타이틀/설정/게임오버/엔딩) 위에서도
        /// 타이머가 멈추지 않도록 unscaledDeltaTime을 쓴다(Design_Ending.md 1장 제약 A).
        /// </summary>
        private void UpdateObjectiveFallback()
        {
            if (objectiveInjected || objectiveLabel == null)
                return;

            objectiveRefreshTimer -= Time.unscaledDeltaTime;
            if (objectiveRefreshTimer > 0f)
                return;

            objectiveRefreshTimer = ObjectiveRefreshInterval;
            ApplyObjectiveText(ResolveFallbackObjective());
        }

        /// <summary>
        /// 지금 이 코드베이스에 실제로 존재하는 public 시그니처(BoatConstructionSystem.currentStage,
        /// AircraftRepairSystem.GetOverallProgress, PlayerInventory.items, SurvivalStats)만으로 만든
        /// 아주 단순한 단계 판정이다. Design_Progression.md 3장의 정식 진입 신호(도면 습득/금속조각
        /// 최초 획득 등)는 여기서 알 수 없으므로 근사치이며, 정식 판정 API가 들어오면
        /// SetObjective 주입이 이 폴백을 통째로 대체한다.
        /// </summary>
        private string ResolveFallbackObjective()
        {
            // [B6 디렉터] 이 메서드는 원래 판정 API가 없던 동안 쓰는 임시 폴백이었다. 이제
            // ProgressionTracker(systems-engineer-B, WorldMapManager.cs)가 들어왔으므로 그쪽으로 위임한다.
            //
            // 임시 폴백에 실제 버그가 있었다: `boatConstruction.currentStage >= 1` 로 4단계를 판정했는데
            // currentStage의 초기값이 1이다(= "1단계를 아직 안 지었다"는 뜻이지 "1단계를 지었다"가 아니다).
            // 그래서 게임 시작 0초에 곧바로 "탈출 준비" 목표가 떴다. 실기 확인에서 잡혔다.
            // ProgressionTracker.Evaluate는 hasCurrentStageBlueprint / highestCompletedStage / 진행률로
            // 판정하므로 같은 함정에 빠지지 않는다.
            //
            // 인자는 전부 null 허용이다. islandTravel 참조는 이 UI가 들고 있지 않아 null을 넘기는데,
            // 그러면 "시작 섬을 떠났다" 신호만 빠지고 손도끼 보유 신호로 3단계 판정이 유지된다.
            return ProgressionTracker.GetObjectiveText(
                playerInventory, survivalStats, boatConstruction, aircraftRepair, null);
        }

        /// <summary>
        /// 목표 문구가 실제로 바뀐 경우에만 .text에 대입한다(#7과 동일한 GC 절약 패턴).
        /// </summary>
        private void ApplyObjectiveText(string objective)
        {
            if (objectiveLabel == null || objective == lastDisplayedObjective)
                return;

            objectiveLabel.text = objective;
            lastDisplayedObjective = objective;
        }
    }
}
