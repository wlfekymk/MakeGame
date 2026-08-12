using UnityEngine;
using MakeGame.Data;
using MakeGame.Player;
using MakeGame.Managers;

namespace MakeGame.Systems
{
    /// <summary>
    /// 엔딩 달성 조건을 매 프레임 확인한다.
    /// 탈출선(배) 엔딩: 배 3단계 100% 완성 + 상하지 않는 음식/물 30일치 확보 + 연료 확보.
    /// (경비행기 수리 엔딩은 story_dictionary.json 기준 아직 설계 보류 상태라 여기서 다루지 않는다.)
    /// </summary>
    public class EndingChecker : MonoBehaviour
    {
        [Tooltip("완성 여부를 확인할 배 제작 시스템")]
        public BoatConstructionSystem boatConstruction;

        [Tooltip("비축 물자를 확인할 인벤토리")]
        public PlayerInventory inventory;

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

        /// <summary>엔딩이 이미 달성되었는지 여부.</summary>
        public bool EndingTriggered => endingTriggered;

        /// <summary>
        /// 매 프레임 엔딩 조건을 확인하고, 조건을 모두 만족하면 배 엔딩을 트리거한다.
        /// </summary>
        private void Update()
        {
            if (endingTriggered)
                return;

            if (CheckBoatEndingConditions())
                TriggerBoatEnding();
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
        /// 배 엔딩을 확정한다. GameManager에 알려 멀티플레이를 개방시킨다.
        /// </summary>
        private void TriggerBoatEnding()
        {
            endingTriggered = true;
            Debug.Log("배 엔딩 달성! 섬을 탈출했습니다.");
            GameManager.Instance?.CompleteEnding();
        }
    }
}
