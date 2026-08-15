using UnityEngine;
using MakeGame.Data;
using MakeGame.Player;
using MakeGame.Managers;

namespace MakeGame.Systems
{
    /// <summary>
    /// 엔딩 달성 조건을 매 프레임 확인한다. 두 가지 엔딩 경로 중 먼저 달성한 쪽으로 게임을 종료시킨다.
    /// 1) 탈출선(배) 엔딩: 배 3단계 100% 완성 + 상하지 않는 음식/물 30일치 확보 + 연료 확보
    ///    + 최소 경과 일수(requiredElapsedDays, Spec_11 기준 15일) 도달. 여러 단계를 밟아 꾸준히
    ///    자원을 모으는 정공법 경로.
    /// 2) 경비행기 수리 엔딩: 시작 섬의 경비행기 잔해(AircraftWreck)에서 엔진부품 등 희귀 재료를 모아
    ///    한 번에 수리를 완료하는 경로. AircraftRepairSystem.isRepairComplete가 true가 되는 순간 확정된다.
    /// </summary>
    public class EndingChecker : MonoBehaviour
    {
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

        /// <summary>엔딩 연출 화면에 표시할 메시지.</summary>
        private string endingMessage = "";

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
                TriggerEnding("배를 타고 섬을 탈출했습니다!");
                return;
            }

            if (aircraftRepair != null && aircraftRepair.isRepairComplete)
            {
                TriggerEnding("경비행기를 수리해 하늘로 섬을 탈출했습니다!");
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
        private void TriggerEnding(string message)
        {
            endingTriggered = true;
            endingMessage = message;
            Debug.Log(message);
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
