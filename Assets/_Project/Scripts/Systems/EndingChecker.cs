using UnityEngine;
using MakeGame.Data;
using MakeGame.Player;
using MakeGame.Managers;

namespace MakeGame.Systems
{
    /// <summary>
    /// 엔딩 달성 조건을 매 프레임 확인한다. 두 가지 엔딩 경로 중 먼저 달성한 쪽으로 게임을 종료시킨다.
    /// 1) 탈출선(배) 엔딩: 배 3단계 100% 완성 + 상하지 않는 음식/물 30일치 확보 + 연료 확보.
    ///    여러 단계를 밟아 꾸준히 자원을 모으는 정공법 경로.
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

        [Header("엔딩 연출")]
        [Tooltip("엔딩 달성 시 잠시 비활성화할 이동/시점 컨트롤러")]
        public PlayerController playerController;

        [Tooltip("엔딩 달성 시 잠시 비활성화할 상호작용 컨트롤러")]
        public InteractionController interactionController;

        [Tooltip("엔딩 연출 화면에서 계속 진행하는 키")]
        public KeyCode continueKey = KeyCode.Space;

        /// <summary>
        /// 승리 화면 배경 이미지 (타이틀 화면과 동일한 섬 컨셉 아트, Resources/UI/title_background.png).
        /// OnGUI에서 최초 1회만 로드해 캐싱한다(널이면 아직 안 불렀거나 로드 실패 - 이 경우 기존 단색 배경 유지).
        /// </summary>
        private Texture2D backgroundTexture;
        private bool backgroundLoadAttempted;

        // 성능 개선(#5): OnGUI가 매 프레임 호출될 때마다 new GUIStyle(...)로 새 인스턴스를 만들고 있었다.
        // UI/StatusEffectWarningUI.EnsureStyles()와 동일한 지연 캐싱 패턴을 적용해, 최초 1회만 만들고
        // 이후에는 캐시된 스타일을 재사용한다.
        private GUIStyle titleStyle;
        private GUIStyle subStyle;

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
        /// 배 엔딩의 모든 조건(배 100% 완성, 식량/식수 30일치, 연료)을 만족하는지 확인한다.
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

            return boatComplete && hasEnoughFood && hasEnoughWater && hasEnoughFuel;
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
        /// </summary>
        private void DismissEndingUI()
        {
            showEndingUI = false;
            Time.timeScale = 1f;

            if (playerController != null)
                playerController.enabled = true;
            if (interactionController != null)
                interactionController.enabled = true;
        }

        /// <summary>
        /// 엔딩 연출 화면일 때 화면 전체를 어둡게 덮고 중앙에 축하 문구를 표시한다.
        /// </summary>
        private void OnGUI()
        {
            if (!showEndingUI)
                return;

            if (!backgroundLoadAttempted)
            {
                backgroundTexture = Resources.Load<Texture2D>("UI/title_background");
                backgroundLoadAttempted = true;
            }

            var fullScreen = new Rect(0, 0, Screen.width, Screen.height);
            if (backgroundTexture != null)
            {
                // 탈출에 성공했으니 떠나온 섬을 배경으로 보여주고, 금색 톤 오버레이로 축하 분위기를 낸다.
                GUI.DrawTexture(fullScreen, backgroundTexture, ScaleMode.ScaleAndCrop);
                GUI.color = new Color(0.25f, 0.15f, 0f, 0.6f);
                GUI.DrawTexture(fullScreen, Texture2D.whiteTexture);
                GUI.color = Color.white;
            }
            else
            {
                GUI.color = new Color(0f, 0f, 0f, 0.75f);
                GUI.DrawTexture(fullScreen, Texture2D.whiteTexture);
                GUI.color = Color.white;
            }

            EnsureStyles();

            GUI.Label(new Rect(0, Screen.height / 2f - 80, Screen.width, 60), "탈출 성공!", titleStyle);
            GUI.Label(new Rect(0, Screen.height / 2f, Screen.width, 40), endingMessage, subStyle);
            GUI.Label(new Rect(0, Screen.height / 2f + 40, Screen.width, 40), $"[{continueKey}] 키를 눌러 계속하기", subStyle);
        }

        /// <summary>
        /// GUIStyle은 OnGUI 컨텍스트 안에서만 새로 만들 수 있으므로, 최초 호출 시점에 지연 생성해
        /// 캐시해둔다(UI/StatusEffectWarningUI.EnsureStyles와 동일한 패턴).
        /// </summary>
        private void EnsureStyles()
        {
            if (titleStyle != null)
                return;

            titleStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 48,
                alignment = TextAnchor.MiddleCenter,
            };
            titleStyle.normal.textColor = new Color(1f, 0.85f, 0.2f); // 금색: 승리 강조

            subStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 20,
                alignment = TextAnchor.MiddleCenter,
            };
            subStyle.normal.textColor = Color.white;
        }
    }
}
