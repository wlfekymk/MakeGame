using UnityEngine;
using MakeGame.Player;

namespace MakeGame.Systems
{
    /// <summary>
    /// 음식/음료 아이템을 섭취하는 처리를 담당한다.
    /// 허기/갈증을 회복시키고, 익히지 않은 음식(isRawFood)을 먹으면 확률적으로 식중독(중독)에 걸린다.
    /// </summary>
    public class ConsumptionSystem : MonoBehaviour
    {
        [Tooltip("아이템을 소모할 대상 인벤토리")]
        public PlayerInventory inventory;

        [Tooltip("허기/갈증을 회복시킬 대상 생존 수치")]
        public SurvivalStats survivalStats;

        [Tooltip("익히지 않은 음식을 먹었을 때 식중독(중독)에 걸릴 확률 (0~1)")]
        [Range(0f, 1f)]
        public float rawFoodPoisonChance = 0.3f;

        /// <summary>
        /// 지정한 인벤토리 아이템을 섭취한다.
        /// 음식/음료 효과가 있는 아이템만 섭취할 수 있으며, 성공 시 사용 횟수를 1 소모한다(소진되면 인벤토리에서 제거).
        /// </summary>
        public bool Consume(InventoryItem item)
        {
            if (item == null || item.data == null || inventory == null || survivalStats == null)
                return false;

            if (!item.data.IsConsumable)
                return false;

            if (item.data.hungerRestoreAmount > 0f)
            {
                survivalStats.ConsumeFood(item.data.hungerRestoreAmount);
                AudioManager.Instance?.PlayEat(); // 음식 섭취 효과음
            }

            if (item.data.thirstRestoreAmount > 0f)
            {
                if (item.data.isCoconutWaterSource)
                    survivalStats.ConsumeCoconutWater(item.data.thirstRestoreAmount);
                else
                    survivalStats.ConsumeWater(item.data.thirstRestoreAmount);

                AudioManager.Instance?.PlayDrink(); // 음료 섭취 효과음
            }

            if (item.data.isRawFood && Random.value < rawFoodPoisonChance)
                survivalStats.ApplyPoison();

            inventory.UseItem(item);
            return true;
        }
    }
}
