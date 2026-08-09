using UnityEngine;
using MakeGame.Data;

namespace MakeGame.Player
{
    /// <summary>
    /// 플레이어가 실제로 소지한 아이템 1개의 상태(원본 데이터 + 남은 사용 횟수)를 나타낸다.
    /// </summary>
    [System.Serializable]
    public class InventoryItem
    {
        public ItemData data;
        public int remainingUses;

        /// <summary>
        /// 아이템 데이터를 기반으로 새 인벤토리 아이템을 생성한다.
        /// 남은 사용 횟수는 데이터의 최대 사용 횟수로 초기화된다.
        /// </summary>
        public InventoryItem(ItemData itemData)
        {
            data = itemData;
            remainingUses = itemData.maxUses;
        }

        /// <summary>
        /// 아이템을 한 번 사용한다. 무제한 아이템(예: 고무보트)이면 횟수를 소모하지 않는다.
        /// 사용 후 더 이상 남은 횟수가 없으면 true를 반환하여 인벤토리에서 제거해야 함을 알린다.
        /// </summary>
        public bool Use()
        {
            if (data.IsUnlimited)
                return false;

            remainingUses = Mathf.Max(0, remainingUses - 1);
            return remainingUses <= 0;
        }
    }
}
