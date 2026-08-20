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
        Boat,      // 뗏목을 지어 떠나는 정공법 경로 - 제목 "귀환"
        Aircraft,  // 경비행기를 수리해 떠나는 속성 경로 - 제목 "탈출"

        // [엔드게임 보스] 위 두 탈출 중 하나를 완성한 상태에서 **보스 3종의 트로피까지** 모은 경우.
        // 값을 중간에 끼워 넣지 않고 맨 뒤에 붙였다 - 이 enum은 세이브에 들어가지 않지만
        // (SaveData에 엔딩 종류 필드가 없다) 씬/프리팹에 int로 직렬화될 수 있는 형태라 규칙은 같다.
        Trophy,    // 두 탈출 경로 중 하나 + 트로피 3개 - 제목 "정복"
    }

    /// <summary>
    /// 엔딩 달성 조건을 매 프레임 확인한다. 두 가지 엔딩 경로 중 먼저 달성한 쪽으로 게임을 종료시킨다.
    /// 1) 탈출선(뗏목) 엔딩: 뗏목이 대양 항해 준비 완료(RaftStructure.IsOceanReady = 바닥판 6칸 이상 +
    ///    돛+키 또는 모터) + 상하지 않는 음식/물 확보 + 연료 확보
    ///    + 최소 경과 일수(requiredElapsedDays, Spec_11 기준 15일) 도달. 여러 단계를 밟아 꾸준히
    ///    자원을 모으는 정공법 경로.
    /// 2) 경비행기 수리 엔딩: 특대(XL) 섬의 경비행기 잔해(AircraftWreck)에서 엔진부품 등 희귀 재료를 모아
    ///    한 번에 수리를 완료하는 경로. AircraftRepairSystem.isRepairComplete + 최소 경과 일수
    ///    (aircraftRequiredElapsedDays, Design_MidGame 8장 기준 8일)를 함께 요구한다 - 예전에는
    ///    isRepairComplete 하나뿐이라 시간 조건이 0이었다(HasElapsedAircraftRequiredDays 주석 참고).
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

        [Tooltip("탈출 준비 여부(IsOceanReady)를 확인할 뗏목. 비워두면 씬에서 살아 있는 뗏목" +
            "(RaftStructure.Active)을 자동으로 쓴다 - 뗏목은 씬에 배선하는 오브젝트가 아니라" +
            " 런타임에 스스로 생기므로, 정상 경로에서는 이 칸이 비어 있는 것이 맞다.")]
        public RaftStructure raftStructure;

        /// <summary>
        /// 지금 판정에 쓸 뗏목. 인스펙터 연결이 있으면 그것을, 없으면 RaftStructure.Active를 쓴다.
        /// EndingUI가 같은 인스턴스를 재사용할 수 있도록 public이다(값을 캐시하지 않는다 - 뗏목은
        /// 씬 리로드마다 새 인스턴스가 되므로 죽은 참조를 들고 있으면 엔딩이 통째로 막힌다).
        /// </summary>
        public RaftStructure Raft => raftStructure != null ? raftStructure : RaftStructure.Active;

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

        [Tooltip("경비행기 엔딩에 필요한 최소 경과 일수 (Design_MidGame 8장 기준 8일). 0이면 이 조건을 끈다.")]
        public int aircraftRequiredElapsedDays = 8;

        /// <summary>survivalClock 미연결 경고를 이미 한 번 남겼는지 여부 (매 프레임 로그 스팸 방지용).</summary>
        private bool survivalClockMissingWarned = false;

        /// <summary>경비행기 경로에서 survivalClock 미연결 경고를 이미 남겼는지 여부(로그 스팸 방지).</summary>
        private bool aircraftSurvivalClockMissingWarned = false;

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

        /// <summary>엔딩 연출(암전→제목→통계→마지막 문장)이 끝났는지 여부. UI가 알려준다.</summary>
        private bool endingPresentationFinished = false;

        /// <summary>엔딩 화면이 열린 실시간 시각(Time.unscaledTime). 아래 안전장치 계산용.</summary>
        private float endingShownAtUnscaledTime = 0f;

        /// <summary>
        /// 연출 완료 통지가 오지 않아도 이 시간(실시간 초)이 지나면 continueKey를 받아준다.
        ///
        /// 안전장치인 이유: 지금 씬(SampleScene.unity)에는 EndingUI가 **아직 배선돼 있지 않다**
        /// (씬 YAML 전수 검색 결과 EndingUI 컴포넌트 0개). UI가 없는 상태에서 엔딩이 나면 화면에는
        /// 아무것도 뜨지 않고 Time.timeScale만 0으로 멈추므로, 연출 완료 통지를 무한정 기다리면
        /// 플레이어가 빠져나올 수단이 사라진다(복구 불가능한 정지 화면). 설계상 연출 총 길이는 약 7초
        /// (Design_Ending.md 3장)이므로 10초면 정상 연출을 자르지 않으면서 최악을 막는다.
        /// Time.timeScale이 0이라 반드시 unscaled 시간으로 재야 한다.
        /// </summary>
        private const float ContinueKeyFallbackSeconds = 10f;

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

        /// <summary>보스 트로피 엔딩 제목(세 번째 엔딩).</summary>
        private const string TrophyEndingTitle = "정복";

        /// <summary>보스 트로피 엔딩 마지막 문장. 두 단어 제목 + 감탄사 없는 한 줄 규칙은 그대로다.</summary>
        private const string TrophyEndingSubtitle = "바다는 더 이상 당신을 붙잡지 못했다.";

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
        /// [ui-engineer 요청] 엔딩 연출이 끝나 "계속하기"를 받아도 되는 상태인지 여부.
        /// UI가 마지막 페이즈(마지막 문장 + 계속하기 버튼)까지 도달했을 때
        /// <see cref="MarkEndingPresentationFinished"/>로 true가 된다. 엔딩이 새로 확정될 때마다
        /// false로 초기화된다.
        /// </summary>
        public bool EndingPresentationFinished => endingPresentationFinished;

        /// <summary>
        /// [ui-engineer 요청] "연출이 끝났다"를 알리는 지점. UI(EndingUI)가 마지막 페이즈에 도달했을 때
        /// — 정상 재생으로 끝났든 건너뛰기로 마지막 상태로 점프했든 — 한 번 부르면 된다.
        /// 이때부터 continueKey(기본 Space)가 엔딩 화면을 닫는다.
        ///
        /// 여러 번 불러도 안전하다(단순 대입). 엔딩 화면이 떠 있지 않을 때 부른 값은
        /// 다음 TriggerEnding에서 false로 되돌아가므로 다음 엔딩에 새지 않는다.
        /// </summary>
        public void MarkEndingPresentationFinished()
        {
            endingPresentationFinished = true;
        }

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

            // [감사] 이 줄이 오래 비어 있었다. 예전 주석은 "SurvivalBalanceConfig에 대응 필드가 아직
            // 없다"고 적혀 있었는데, 그 필드(endingAircraftRequiredElapsedDays)는 그 뒤에 추가됐고
            // 주석만 낡은 채 남아 배선이 마저 안 됐다. 지금은 코드 기본값(8)과 config 값(8)이 우연히
            // 같아 겉으로 티가 나지 않지만, 밸런스 파일에서 이 값만 바꾸면 아무 일도 일어나지 않는
            // 상태였다 - "밸런스는 파일 하나만 고치면 된다"는 이 시스템의 계약이 깨져 있었다.
            if (aircraftRequiredElapsedDays < 0)
                aircraftRequiredElapsedDays = balanceConfig.endingAircraftRequiredElapsedDays;
        }

        /// <summary>
        /// 매 프레임 두 엔딩 경로의 조건을 확인하고, 성립한 것 중 우선순위가 높은 쪽을 트리거한다
        /// (동시 성립 규칙은 ResolveAchievableEnding 주석 참고 - 검사 "순서"가 아니라 명시적 우선순위다).
        /// 엔딩 연출 화면이 떠 있는 동안에는 계속하기 입력만 감시한다.
        /// </summary>
        private void Update()
        {
            if (showEndingUI)
            {
                // [수정] 연출이 끝나기 전의 continueKey는 무시한다. 예전에는 이 키가 곧바로
                // DismissEndingUI를 불러서, 연출 1~3페이즈(암전/제목/통계 공개) 중에 Space를 누르면
                // "건너뛰기"가 아니라 엔딩 화면 자체가 닫혀 통계를 한 번도 못 보고 게임으로 돌아갔다.
                // 건너뛰기는 UI(EndingUI)가 담당하고, 이 키는 연출이 끝난 뒤의 "화면 닫기"만 맡는다.
                // 게임 규칙(엔딩 조건/결과)은 전혀 바뀌지 않는다 - 입력을 받는 시점만 늦춘다.
                if (Input.GetKeyDown(continueKey) && CanAcceptContinueKey())
                    DismissEndingUI();
                return;
            }

            if (endingTriggered)
                return;

            EndingKind kind = ResolveAchievableEnding();
            if (kind != EndingKind.None)
                TriggerEnding(kind);
        }

        /// <summary>
        /// 이번 프레임에 성립한 엔딩 중 **우선순위가 가장 높은 것**을 고른다. 아무것도 성립하지 않으면 None.
        ///
        /// [디렉터 결정 - 배 우선은 의도다] 두 조건이 같은 프레임에 동시에 성립할 수 있다(배 조건이
        /// 15일 경과로 오래 대기하는 동안 경비행기 수리를 끝내두면 실제로 일어난다). 예전에는 Update가
        /// 배를 먼저 검사하고 return 하는 **코드 순서의 부산물**로 배가 이겼고, 그것이 판단인지 우연인지
        /// 코드에서 읽을 수 없었다(qa-reviewer 지적).
        ///
        /// 확정: **동시 성립 시 배 엔딩("귀환")을 보여준다.** 뗏목은 바닥판 6칸 + 돛·키 + 비축 물자 +
        /// 경과 15일을 요구하는 훨씬 긴 경로다(Design_Progression.md 4장: 경비행기가 재료 4배·시간
        /// 3~5배 싸다). 둘 다 달성했다면 더 어려운 쪽의 결말을 보여주는 것이 플레이어가 실제로 한 일에
        /// 대한 정확한 응답이다.
        ///
        /// 구현도 순서에 의존하지 않게 바꿨다 - 아래 두 if의 위치를 뒤바꿔도 결과가 뒤집히지 않는다.
        /// 우선순위는 GetEndingPriority 한 곳에만 있다.
        /// </summary>
        private EndingKind ResolveAchievableEnding()
        {
            EndingKind best = EndingKind.None;

            // 두 조건 모두 읽기 전용 판정이라(인벤토리 개수 조회 / bool 필드 읽기) 둘 다 평가해도
            // 부작용이 없다. 먼저 성립한 쪽에서 빠져나가지 않는 이유가 이것이다.
            bool boatReady = CheckBoatEndingConditions();
            if (boatReady)
                best = HigherPriority(best, EndingKind.Boat);

            bool aircraftReady = aircraftRepair != null && aircraftRepair.isRepairComplete
                && HasElapsedAircraftRequiredDays();
            if (aircraftReady)
                best = HigherPriority(best, EndingKind.Aircraft);

            // [엔드게임 보스] 세 번째 엔딩. **기존 두 엔딩의 조건을 한 글자도 건드리지 않는다** -
            // 탈출 수단(뗏목 또는 경비행기)이 이미 성립한 그 프레임에, 트로피 3개까지 있으면
            // 결말만 더 높은 것으로 바뀐다. 그래서 보스를 한 마리도 잡지 않은 플레이는 예전과
            // 100% 같고, 보스를 잡았다고 해서 엔딩이 **더 일찍** 나는 일도 없다.
            if ((boatReady || aircraftReady) && BossCreature.AllTrophiesCollected)
                best = HigherPriority(best, EndingKind.Trophy);

            return best;
        }

        /// <summary>두 엔딩 종류 중 우선순위가 높은 쪽을 돌려준다(동점이면 a).</summary>
        private static EndingKind HigherPriority(EndingKind a, EndingKind b)
        {
            return GetEndingPriority(b) > GetEndingPriority(a) ? b : a;
        }

        /// <summary>
        /// 동시 성립 시의 우선순위. **값이 클수록 먼저 보여준다.** 이 게임의 엔딩 우선순위를 정의하는
        /// 유일한 지점이므로, 순서를 바꾸고 싶으면 여기 숫자만 고치면 된다(Update의 검사 순서가 아니라).
        /// </summary>
        private static int GetEndingPriority(EndingKind kind)
        {
            switch (kind)
            {
                // [엔드게임 보스] 트로피 엔딩은 "탈출 조건 + 보스 3종"이라 두 경로 어느 쪽보다도
                // 엄격하게 위에 얹힌 조건이다. 동시 성립은 정의상 항상 일어나므로(트로피 엔딩은
                // 다른 하나가 성립해야만 성립한다) 여기가 가장 높아야 세 번째 결말이 실제로 보인다.
                case EndingKind.Trophy: return 3;
                case EndingKind.Boat: return 2;      // 더 긴 경로 - 동시 성립 시 이쪽을 보여준다
                case EndingKind.Aircraft: return 1;
                default: return 0;                   // None
            }
        }

        /// <summary>
        /// 지금 continueKey를 받아 엔딩 화면을 닫아도 되는지 판단한다.
        /// (1) UI가 연출 종료를 알렸거나, (2) 통지가 오지 않은 채 ContinueKeyFallbackSeconds가 지났으면 받는다.
        /// (2)는 UI가 씬에 없거나 연출 코루틴이 죽었을 때 정지 화면에 갇히지 않기 위한 안전장치다
        /// (ContinueKeyFallbackSeconds 주석 참고).
        /// </summary>
        private bool CanAcceptContinueKey()
        {
            if (endingPresentationFinished)
                return true;

            return Time.unscaledTime - endingShownAtUnscaledTime >= ContinueKeyFallbackSeconds;
        }

        /// <summary>
        /// 배 엔딩의 모든 조건(뗏목 대양 준비 완료, 식량/식수, 연료, 최소 경과 일수)을 만족하는지 확인한다.
        /// 경과 일수 조건 추가(B2-2, Spec_11): 배를 지나치게 빨리(초반 몇 시간 만에) 완성해 탈출해버리면
        /// 생존 게임의 긴장감을 충분히 느끼기 전에 끝나버린다는 기획 의도를 반영해, 최소 경과 일수
        /// (requiredElapsedDays) 조건을 추가했다.
        /// </summary>
        private bool CheckBoatEndingConditions()
        {
            var raft = Raft;
            if (raft == null || inventory == null)
                return false;

            // [뗏목 재배선] 예전 조건은 "3단계 도면+재료를 다 넣었는가"(BoatConstructionSystem)였다.
            // 이제는 해안에 실제로 지은 뗏목이 **대양에 나갈 수 있는 모양인가**를 본다:
            // 바닥판 OceanReadyTileCount칸 이상 + 방향을 유지할 수 있는 추진(돛+키 또는 모터).
            // 보급품(비상식량 12 / 생수 12 / 연료 1)과 최소 경과 15일 조건은 한 글자도 바뀌지 않았다.
            bool boatComplete = raft.IsOceanReady;

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
            return HasElapsedDaysOrWarn(requiredElapsedDays, "배", ref survivalClockMissingWarned);
        }

        /// <summary>
        /// 경비행기 엔딩의 최소 경과 일수(aircraftRequiredElapsedDays) 조건을 만족했는지 확인한다.
        ///
        /// [추가 근거 - Design_MidGame.md 8장] 이 조건이 붙기 전까지 경비행기 판정은
        /// isRepairComplete 하나뿐이었다. 그런데 엔진부품(특대 전용)에 도달하는 시점에 그 섬에 이미
        /// 재료가 넘치게 있어서 25~30분이면 게임이 끝난다 - 배 경로(74.5분)의 절반도 안 되고, 그
        /// 결과 30분 이후의 모든 콘텐츠가 "아무도 보지 않는" 선택 사항이 된다.
        /// 8일 근거: 일몰 취침 루프 기준 39.5분(270 + 300×7초)이라 재료 확보 완료 추정(25~30분)보다
        /// 10분 뒤이고, 배 엔딩(15일)의 약 절반이라 두 엔딩의 길이 차이는 그대로 유지된다.
        ///
        /// survivalClock 미연결 시의 처리(경고 1회 + 조건 통과)는 배 쪽과 완전히 동일하다 -
        /// 미연결 때문에 엔딩이 영구히 막히는 편이 조건이 헐거워지는 것보다 나쁘다는 같은 판단이다.
        /// </summary>
        private bool HasElapsedAircraftRequiredDays()
        {
            return HasElapsedDaysOrWarn(aircraftRequiredElapsedDays, "경비행기",
                ref aircraftSurvivalClockMissingWarned);
        }

        /// <summary>
        /// 경과 일수 조건 판정의 공통 본체. survivalClock이 없으면 최초 1회만 Debug.LogError를 남기고
        /// 조건을 만족한 것으로 처리한다(소프트락 방지 - HasElapsedRequiredDays 주석 참고).
        /// 경로마다 경고 플래그를 따로 넘기므로 배/경비행기 경고가 서로를 잡아먹지 않는다.
        /// </summary>
        /// <param name="requiredDays">그 경로가 요구하는 최소 경과 일수(0 이하면 사실상 조건 없음).</param>
        /// <param name="endingName">로그에 표시할 경로 이름.</param>
        /// <param name="warnedFlag">그 경로의 "경고를 이미 남겼는지" 플래그.</param>
        private bool HasElapsedDaysOrWarn(int requiredDays, string endingName, ref bool warnedFlag)
        {
            if (survivalClock == null)
            {
                if (!warnedFlag)
                {
                    Debug.LogError($"[EndingChecker] survivalClock이 연결되지 않았습니다. {endingName} 엔딩의 경과 일수" +
                        $"({requiredDays}일) 조건을 검사할 수 없어 이 조건을 만족한 것으로 처리합니다. " +
                        "Inspector에서 SurvivalClock을 연결하세요.");
                    warnedFlag = true;
                }
                return true;
            }

            return survivalClock.ElapsedDays >= requiredDays;
        }

        /// <summary>
        /// 엔딩 종류별 제목(크게 띄우는 두 단어). 문구 표는 이 클래스의 상수 한 곳에만 있다.
        /// [엔드게임 보스] 세 번째 엔딩이 붙으면서 삼항 연산자를 switch로 바꿨다 - 종류가 셋 이상이면
        /// "경비행기가 아니면 배"라는 판정이 조용히 틀린 답을 준다.
        /// </summary>
        private static string GetEndingTitle(EndingKind kind)
        {
            switch (kind)
            {
                case EndingKind.Aircraft: return AircraftEndingTitle;
                case EndingKind.Trophy: return TrophyEndingTitle;
                default: return BoatEndingTitle;
            }
        }

        /// <summary>엔딩 종류별 마지막 문장. 분기 기준은 <see cref="GetEndingTitle"/>과 완전히 같다.</summary>
        private static string GetEndingSubtitle(EndingKind kind)
        {
            switch (kind)
            {
                case EndingKind.Aircraft: return AircraftEndingSubtitle;
                case EndingKind.Trophy: return TrophyEndingSubtitle;
                default: return BoatEndingSubtitle;
            }
        }

        /// <summary>
        /// 엔딩을 확정한다. 어느 경로든 GameManager에 알려 멀티플레이를 개방시키고,
        /// 화면에 승리 연출을 띄운 뒤 이동/상호작용을 잠시 멈춘다.
        /// </summary>
        /// <param name="kind">달성한 엔딩의 종류(연출 분기용). 제목/부제는 이 값에서 결정된다.</param>
        private void TriggerEnding(EndingKind kind)
        {
            string title = GetEndingTitle(kind);
            string subtitle = GetEndingSubtitle(kind);

            endingTriggered = true;
            achievedEnding = kind;
            endingTitle = title;
            endingSubtitle = subtitle;

            // 제목/부제를 분리해 노출하면서도, 아직 두 값을 따로 읽지 않는 호출부(UI/EndingUI.cs)가
            // 예전과 같은 한 덩어리 문자열을 계속 받을 수 있도록 이어 붙인 값을 유지한다.
            endingMessage = $"{title}\n{subtitle}";
            Debug.Log($"[EndingChecker] 엔딩 달성: {kind} - {title} / {subtitle}");
            GameManager.Instance?.CompleteEnding();

            // 연출은 이제 막 시작한다. UI가 끝냈다고 알려줄 때까지 continueKey를 받지 않는다.
            endingPresentationFinished = false;
            endingShownAtUnscaledTime = Time.unscaledTime;

            showEndingUI = true;
            if (playerController != null)
                playerController.enabled = false;
            if (interactionController != null)
                interactionController.enabled = false;

            Time.timeScale = 0f;

            // [수정] 팡파르를 여기서 울리지 않는다. 연출은 암전 1초로 시작하므로(Design_Ending.md 3장
            // 페이즈 1) 여기서 소리를 내면 화면에 아무것도 뜨기 전에 소리부터 난다 - 그림과 소리가
            // 어긋난다. 페이즈 2(배경 + 제목 등장)에서 UI가 AudioManager.PlayEndingFanfare()를 부른다.
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
            endingPresentationFinished = false; // 다음 엔딩은 다시 연출부터 시작한다
            Time.timeScale = 1f;

            if (playerController != null)
                playerController.enabled = true;
            if (interactionController != null)
                interactionController.enabled = true;
        }
    }
}
