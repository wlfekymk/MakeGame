using UnityEngine;
using MakeGame.Data;
using MakeGame.Player;
using MakeGame.Managers;

namespace MakeGame.Systems
{
    /// <summary>
    /// 달성한 엔딩의 종류. Design_Ending.md 2장이 두 엔딩의 연출(배경색/팡파르/통계 공개량)을 서로 다르게
    /// 하도록 설계했는데, 지금까지는 화면 쪽에서 "어느 엔딩인지"를 알 수 있는 수단이 문자열 비교뿐이었다.
    /// 문구가 바뀌면 조용히 깨지는 판정이므로 명시적인 종류 값으로 노출한다.
    /// </summary>
    public enum EndingKind
    {
        None,
        Boat,      // 배를 만들어 떠나는 정공법 경로 - 제목 "귀환"
        Aircraft,  // 경비행기를 수리해 떠나는 속성 경로 - 제목 "탈출"
    }

    /// <summary>
    /// 엔딩 달성 조건을 매 프레임 확인한다. 두 가지 엔딩 경로 중 먼저 달성한 쪽으로 게임을 종료시킨다.
    /// 1) 탈출선(배) 엔딩: 배 3단계 100% 완성 + 상하지 않는 음식/물 30일치 확보 + 연료 확보
    ///    + 최소 경과 일수(requiredElapsedDays, Spec_11 기준 15일) 도달. 여러 단계를 밟아 꾸준히
    ///    자원을 모으는 정공법 경로.
    /// 2) 경비행기 수리 엔딩: 시작 섬의 경비행기 잔해(AircraftWreck)에서 엔진부품 등 희귀 재료를 모아
    ///    한 번에 수리를 완료하는 경로. AircraftRepairSystem.isRepairComplete가 true가 되는 순간 확정된다.
    ///
    /// B4-1 (Spec_15 3단계 배선): SurvivalBalanceConfig를 선택적(nullable) 참조로 받는다.
    /// 폴백으로 읽는 config 필드 — requiredFoodCount ← endingRequiredFoodCount,
    /// requiredWaterCount ← endingRequiredWaterCount, requiredFuelCount ← endingRequiredFuelCount,
    /// requiredElapsedDays ← endingRequiredElapsedDays.
    /// 폴백은 해당 필드가 음수(미설정)일 때만 적용된다 - 네 필드 모두 같은 규칙이다(qa-reviewer 요청으로
    /// "0 이하" → "음수"로 통일했다. 근거는 ApplyBalanceConfigFallback 주석 참고). 씬 실측값
    /// (12/12/1/15, SampleScene.unity:1038-1045)이 전부 양수이므로 현재 씬에서는 폴백이 한 번도
    /// 실행되지 않는다 = 동작 변화 없음.
    /// </summary>
    public class EndingChecker : MonoBehaviour
    {
        [Header("밸런스 config (선택, B4-1)")]
        [Tooltip("연결하면, 아래 requiredFoodCount/requiredWaterCount/requiredFuelCount/" +
            "requiredElapsedDays가 음수로(미설정) 남아있는 경우에 한해 config의 ending* 값을 대신 쓴다." +
            " 0은 '그 조건을 끈다'는 의미 있는 값이라 폴백 대상이 아니다(0을 넣으면 0이 그대로 쓰인다).")]
        public SurvivalBalanceConfig balanceConfig;

        [Tooltip("완성 여부를 확인할 배 제작 시스템")]
        public BoatConstructionSystem boatConstruction;

        [Tooltip("완성 여부를 확인할 경비행기 수리 시스템 (비워두면 경비행기 엔딩을 검사하지 않는다)")]
        public AircraftRepairSystem aircraftRepair;

        [Tooltip("비축 물자를 확인할 인벤토리")]
        public PlayerInventory inventory;

        [Header("배 엔딩 경과 일수 조건 (Spec_11)")]
        [Tooltip("배 엔딩의 경과 일수 조건 판정에 사용할 게임 내 시계. 비워두면 이 조건을 검사할 수 없어" +
            " 경고를 남기고 조건을 만족한 것으로 안전하게 처리한다(HasElapsedRequiredDays 참고).")]
        public SurvivalClock survivalClock;

        [Tooltip("배 엔딩에 필요한 최소 경과 일수 (Spec_11 기준 15일)")]
        public int requiredElapsedDays = 15;

        /// <summary>survivalClock 미연결 경고를 이미 한 번 남겼는지 여부 (매 프레임 로그 스팸 방지용).</summary>
        private bool survivalClockMissingWarned = false;

        [Header("엔딩 연출")]
        [Tooltip("엔딩 달성 시 잠시 비활성화할 이동/시점 컨트롤러")]
        public PlayerController playerController;

        [Tooltip("엔딩 달성 시 잠시 비활성화할 상호작용 컨트롤러")]
        public InteractionController interactionController;

        [Tooltip("엔딩 연출 화면에서 계속 진행하는 키")]
        public KeyCode continueKey = KeyCode.Space;

        // 회귀 방지(B3 배치, GameOverController와 동일한 판단): 레거시 IMGUI(OnGUI)는 Unity 렌더링
        // 순서상 항상 Screen Space-Overlay Canvas보다 나중에, 최상단에 그려져 UI/EndingUI(새 UGUI
        // 엔딩 화면)를 완전히 가려버린다. 배치 2에서 GameOverController.OnGUI를 "검증 전까지 남겨두라"고
        // 했다가 이 문제로 회귀가 났으므로, 이번에는 새 화면(EndingUI)을 만드는 같은 배치에서 곧바로
        // OnGUI()/EnsureStyles()/titleStyle/subStyle과 그 배경 이미지 로딩 전용 필드
        // (backgroundTexture/backgroundLoadAttempted, OnGUI 안에서만 쓰였음)를 전부 제거했다.
        // 화면 표시는 전적으로 UI/EndingUI가 담당하며, 이 클래스는 상태(IsShowingEnding/EndingMessage/
        // EndingTriggered)와 동작(DismissEndingUI)만 노출한다.

        [Header("탈출에 필요한 비축 물자")]
        [Tooltip("상하지 않는 비축 식량 아이템 (없으면 식량 조건을 검사하지 않는다)")]
        public ItemData nonPerishableFoodItem;
        public int requiredFoodCount = 30;

        [Tooltip("비축 식수 아이템 (없으면 식수 조건을 검사하지 않는다)")]
        public ItemData waterSupplyItem;
        public int requiredWaterCount = 30;

        [Tooltip("배 연료 아이템 (없으면 연료 조건을 검사하지 않는다)")]
        public ItemData fuelItem;
        public int requiredFuelCount = 1;

        private bool endingTriggered = false;

        /// <summary>엔딩 연출 화면이 현재 표시 중인지 여부.</summary>
        private bool showEndingUI = false;

        // ── 엔딩 문구 (Design_Ending.md 2장·3장, ui-engineer 요청으로 제목/부제 분리) ──────────────
        //
        // 예전에는 TriggerEnding이 문장 한 줄("배를 타고 섬을 탈출했습니다!")만 받아 그대로 화면에
        // 띄웠다. 설계 문서는 제목을 "탈출"/"귀환" 두 단어로 크게(48pt) 띄우고 마지막 문장을 그 아래
        // 작게 붙이는 2단 구성을 요구하는데, 한 문자열로는 UI가 나눌 방법이 없다.
        //
        // 형식 결정: **문자열 안에 구분자를 넣지 않는다.** 제목과 부제를 별도 프로퍼티로 노출한다.
        // "\n"이나 "|" 같은 구분자를 넣고 UI가 Split하게 하면, 문구에 그 문자가 섞이는 날 조용히
        // 깨지고 두 파일을 동시에 봐야 원인을 알 수 있다. 분리된 값을 분리된 채로 넘기는 편이 싸다.
        // 기존 EndingMessage는 제거하지 않고 두 값을 줄바꿈으로 이어 붙인 값으로 유지한다
        // (UI/EndingUI.cs가 지금 이 프로퍼티를 읽고 있어 제거하면 컴파일이 깨진다).
        //
        // 문구는 Design_Ending.md 3장 페이즈 2/4 표기를 그대로 옮긴 것이다. 느낌표를 쓰지 않는 것이
        // 문서의 명시적 결정이다("150분짜리 생존 게임의 마지막 문장이 감탄사면 무게가 안 맞는다").

        /// <summary>배 엔딩 제목.</summary>
        private const string BoatEndingTitle = "귀환";

        /// <summary>배 엔딩 마지막 문장.</summary>
        private const string BoatEndingSubtitle = "당신은 준비된 채로 떠났다.";

        /// <summary>경비행기 엔딩 제목.</summary>
        private const string AircraftEndingTitle = "탈출";

        /// <summary>경비행기 엔딩 마지막 문장.</summary>
        private const string AircraftEndingSubtitle = "섬은 아직 그 자리에 있다.";

        /// <summary>엔딩 연출 화면에 표시할 메시지.</summary>
        private string endingMessage = "";

        /// <summary>엔딩 제목(두 단어). 아직 엔딩이 없으면 빈 문자열이다.</summary>
        private string endingTitle = "";

        /// <summary>엔딩 마지막 문장. 아직 엔딩이 없으면 빈 문자열이다.</summary>
        private string endingSubtitle = "";

        /// <summary>달성한 엔딩의 종류. 아직 엔딩이 없으면 None이다.</summary>
        private EndingKind achievedEnding = EndingKind.None;

        /// <summary>엔딩이 이미 달성되었는지 여부.</summary>
        public bool EndingTriggered => endingTriggered;

        /// <summary>
        /// 컴파일 차단 해제: UI/EndingUI.cs(새 UGUI 엔딩 화면)가 연출 화면을 표시/유지할지 판단하려면
        /// 이 상태를 직접 읽어야 해서 공개 접근자로 노출했다(GameOverController.isGameOver와 같은 목적).
        /// </summary>
        public bool IsShowingEnding => showEndingUI;

        /// <summary>
        /// 컴파일 차단 해제: UI/EndingUI.cs가 축하 문구를 표시하려면 이 값을 직접 읽어야 해서
        /// 공개 접근자로 노출했다(GameOverController.GetDeathMessage()와 같은 목적).
        /// </summary>
        public string EndingMessage => endingMessage;

        /// <summary>
        /// 엔딩 제목("탈출" 또는 "귀환"). 크게 표시할 두 단어다. 엔딩 전에는 빈 문자열.
        /// </summary>
        public string EndingTitle => endingTitle;

        /// <summary>
        /// 엔딩의 마지막 문장(부제). 제목 아래 작게 표시할 한 줄이다. 엔딩 전에는 빈 문자열.
        /// </summary>
        public string EndingSubtitle => endingSubtitle;

        /// <summary>
        /// 달성한 엔딩의 종류. 연출/통계 공개량을 엔딩별로 다르게 하려면 문구를 비교하지 말고 이 값을 볼 것.
        /// </summary>
        public EndingKind AchievedEnding => achievedEnding;

        /// <summary>
        /// 초기화 시점에 balanceConfig 폴백을 적용한다.
        /// </summary>
        private void Awake()
        {
            ApplyBalanceConfigFallback();
        }

        /// <summary>
        /// balanceConfig가 있을 때, 0 이하로 남아있는(=미설정) 필드만 골라 config 값으로 채운다.
        /// 필요 수량이 0 이하이면 그 조건이 사실상 꺼지는 것과 같으므로(항상 만족), 0 이하를 "아직
        /// 설정되지 않음"의 안전한 신호로 삼는다 - SurvivalStats.ApplyBalanceConfigFallback과 동일한 판단 기준.
        /// balanceConfig가 비어 있으면 아무 것도 하지 않는다(기존 동작 100% 유지, NRE 없음).
        /// </summary>
        private void ApplyBalanceConfigFallback()
        {
            // B4-2: 인스펙터에서 연결되지 않았으면 Resources의 공용 에셋을 자동으로 집는다.
            // 런타임 생성 컴포넌트(WeatherSystem/Campfire/WaterStill 등)는 인스펙터 연결 수단이
            // 아예 없어서, 이 경로가 없으면 balanceConfig가 영원히 null로 남는다.
            if (balanceConfig == null)
                balanceConfig = SurvivalBalanceConfig.Active;
            if (balanceConfig == null)
                return;

            // [qa-reviewer 요청] 네 필드 모두 "미설정 = 음수" 한 가지 규칙으로 통일했다(<=0 → <0).
            //
            // 판단 근거 - <=0 유지 + 주석 대신 <0 통일을 고른 이유:
            //   (1) 0은 세 필드 모두에서 "의미 있는 값"이다. requiredFoodCount 0 = "식량 조건 없음"은
            //       설계자가 실제로 원할 수 있는 설정이고, 지금은 그렇게 넣어도 config의 12로 조용히
            //       되돌아가 인스펙터 표시와 실제 판정이 갈라진다. 조건을 끄는 다른 수단(대응 ItemData
            //       비우기)이 있다는 사실은 이 침묵을 정당화하지 못한다 - 두 수단이 서로 다르게 동작하는
            //       것 자체가 다음 사람이 틀릴 자리다.
            //   (2) 같은 파일 안에서 네 줄 중 한 줄만 규칙이 다르면, 그 차이가 "의도"인지 "고치다 만
            //       흔적"인지 읽는 사람이 판별할 수 없다. requiredElapsedDays만 <0 이었던 직전 상태가
            //       정확히 그 모양이었다.
            //   (3) 회귀 위험 0인 타이밍이다. 씬 실측값이 12/12/1로 전부 양수라(SampleScene.unity
            //       :1038,1040,1042) 이 세 줄은 지금 씬에서 한 번도 실행되지 않는다. 폴백이 필요한
            //       상황을 만들려면 인스펙터에서 값을 음수로 직접 내려야 한다.
            //
            // 주의: 이 규칙 변경으로 "필드를 0으로 두면 config가 채워준다"는 동작이 사라진다. 앞으로
            // config 값을 쓰고 싶으면 해당 필드를 -1로 둘 것.
            if (requiredFoodCount < 0) requiredFoodCount = balanceConfig.endingRequiredFoodCount;
            if (requiredWaterCount < 0) requiredWaterCount = balanceConfig.endingRequiredWaterCount;
            if (requiredFuelCount < 0) requiredFuelCount = balanceConfig.endingRequiredFuelCount;
            if (requiredElapsedDays < 0) requiredElapsedDays = balanceConfig.endingRequiredElapsedDays;
        }

        /// <summary>
        /// 매 프레임 두 엔딩 경로의 조건을 확인하고, 먼저 만족되는 쪽을 트리거한다.
        /// 엔딩 연출 화면이 떠 있는 동안에는 계속하기 입력만 감시한다.
        /// </summary>
        private void Update()
        {
            if (showEndingUI)
            {
                if (Input.GetKeyDown(continueKey))
                    DismissEndingUI();
                return;
            }

            if (endingTriggered)
                return;

            if (CheckBoatEndingConditions())
            {
                TriggerEnding(EndingKind.Boat, BoatEndingTitle, BoatEndingSubtitle);
                return;
            }

            if (aircraftRepair != null && aircraftRepair.isRepairComplete)
            {
                TriggerEnding(EndingKind.Aircraft, AircraftEndingTitle, AircraftEndingSubtitle);
            }
        }

        /// <summary>
        /// 배 엔딩의 모든 조건(배 100% 완성, 식량/식수 30일치, 연료, 최소 경과 일수)을 만족하는지 확인한다.
        /// 경과 일수 조건 추가(B2-2, Spec_11): 배를 지나치게 빨리(초반 몇 시간 만에) 완성해 탈출해버리면
        /// 생존 게임의 긴장감을 충분히 느끼기 전에 끝나버린다는 기획 의도를 반영해, 최소 경과 일수
        /// (requiredElapsedDays) 조건을 추가했다.
        /// </summary>
        private bool CheckBoatEndingConditions()
        {
            if (boatConstruction == null || inventory == null)
                return false;

            bool boatComplete = boatConstruction.currentStage >= BoatConstructionSystem.TotalStages
                && boatConstruction.CanAdvanceStage();

            bool hasEnoughFood = nonPerishableFoodItem == null
                || inventory.GetItemCount(nonPerishableFoodItem) >= requiredFoodCount;

            bool hasEnoughWater = waterSupplyItem == null
                || inventory.GetItemCount(waterSupplyItem) >= requiredWaterCount;

            bool hasEnoughFuel = fuelItem == null
                || inventory.GetItemCount(fuelItem) >= requiredFuelCount;

            bool hasElapsedEnoughDays = HasElapsedRequiredDays();

            return boatComplete && hasEnoughFood && hasEnoughWater && hasEnoughFuel && hasElapsedEnoughDays;
        }

        /// <summary>
        /// 배 엔딩에 필요한 최소 경과 일수(requiredElapsedDays) 조건을 만족했는지 확인한다.
        /// 치명 결함 예방(B2-2): survivalClock이 Inspector에서 아직 연결되지 않은 채로 이 메서드가
        /// 무방비로 참조하면 NullReferenceException이 터져 EndingChecker.Update() 전체가 멈추고
        /// 배/경비행기 두 엔딩 경로 모두 더 이상 확인되지 않는다(IslandGenerator.spawnConfig 미연결
        /// 버그와 동일한 함정). 미연결 상태면 최초 1회만 Debug.LogError로 원인을 남기고, 이 조건 하나
        /// 때문에 배 엔딩이 영원히 막히는 소프트락을 만들지 않도록 조건을 만족한 것으로 안전하게
        /// 처리한다(연결되는 즉시 정상적으로 경과 일수를 검사하게 된다).
        /// </summary>
        private bool HasElapsedRequiredDays()
        {
            if (survivalClock == null)
            {
                if (!survivalClockMissingWarned)
                {
                    Debug.LogError($"[EndingChecker] survivalClock이 연결되지 않았습니다. 배 엔딩의 경과 일수" +
                        $"({requiredElapsedDays}일) 조건을 검사할 수 없어 이 조건을 만족한 것으로 처리합니다. " +
                        "Inspector에서 SurvivalClock을 연결하세요.");
                    survivalClockMissingWarned = true;
                }
                return true;
            }

            return survivalClock.ElapsedDays >= requiredElapsedDays;
        }

        /// <summary>
        /// 엔딩을 확정한다. 어느 경로든 GameManager에 알려 멀티플레이를 개방시키고,
        /// 화면에 승리 연출을 띄운 뒤 이동/상호작용을 잠시 멈춘다.
        /// </summary>
        /// <param name="kind">달성한 엔딩의 종류(연출 분기용).</param>
        /// <param name="title">크게 표시할 제목 두 단어.</param>
        /// <param name="subtitle">제목 아래 작게 표시할 마지막 문장.</param>
        private void TriggerEnding(EndingKind kind, string title, string subtitle)
        {
            endingTriggered = true;
            achievedEnding = kind;
            endingTitle = title;
            endingSubtitle = subtitle;

            // 제목/부제를 분리해 노출하면서도, 아직 두 값을 따로 읽지 않는 호출부(UI/EndingUI.cs)가
            // 예전과 같은 한 덩어리 문자열을 계속 받을 수 있도록 이어 붙인 값을 유지한다.
            endingMessage = $"{title}\n{subtitle}";
            Debug.Log($"[EndingChecker] 엔딩 달성: {kind} - {title} / {subtitle}");
            GameManager.Instance?.CompleteEnding();

            showEndingUI = true;
            if (playerController != null)
                playerController.enabled = false;
            if (interactionController != null)
                interactionController.enabled = false;

            Time.timeScale = 0f;
            AudioManager.Instance?.PlayStageComplete(); // 승리 팡파르 재생
        }

        /// <summary>
        /// 엔딩 연출 화면을 닫고 시간을 다시 흐르게 한 뒤, 이동/상호작용을 되돌려준다.
        /// 첫 엔딩을 본 이후에도 계속 자유롭게 플레이할 수 있도록 허용한다(멀티플레이 개방 규칙과 별개).
        /// 컴파일 차단 해제: UI/EndingUI.cs가 계속하기 버튼에서 이 메서드를 직접 호출해야 해서
        /// 접근제한자만 private→public으로 바꿨다(시그니처/본문은 그대로).
        /// </summary>
        public void DismissEndingUI()
        {
            showEndingUI = false;
            Time.timeScale = 1f;

            if (playerController != null)
                playerController.enabled = true;
            if (interactionController != null)
                interactionController.enabled = true;
        }
    }
}
