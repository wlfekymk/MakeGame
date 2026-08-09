using System.Collections.Generic;
using UnityEngine;
using MakeGame.Data;

namespace MakeGame.Player
{
    /// <summary>
    /// 플레이어의 인벤토리를 관리한다.
    /// 불시착 직후 시작 아이템 지급, 아이템 사용, 섬 이동 시 휴대 가능 여부 판정을 담당한다.
    /// </summary>
    public class PlayerInventory : MonoBehaviour
    {
        [Tooltip("불시착 직후 챙길 수 있는 시작 아이템 목록 (고무보트, 생수, 라이터, 칼 등)")]
        public List<ItemData> startingItemPool = new List<ItemData>();

        [Tooltip("현재 플레이어가 소지 중인 아이템 목록")]
        public List<InventoryItem> items = new List<InventoryItem>();

        /// <summary>
        /// 시작 아이템 풀에 있는 아이템들을 인벤토리에 지급한다. 불시착 직후 최초 1회 호출한다.
        /// </summary>
        public void GrantStartingLoadout()
        {
            foreach (var itemData in startingItemPool)
            {
                items.Add(new InventoryItem(itemData));
            }
        }

        /// <summary>
        /// 지정한 아이템을 한 번 사용한다. 사용 횟수가 모두 소진되면 인벤토리에서 제거한다.
        /// </summary>
        public void UseItem(InventoryItem item)
        {
            if (!items.Contains(item))
                return;

            bool exhausted = item.Use();
            if (exhausted)
                items.Remove(item);
        }

        /// <summary>
        /// 지정한 아이템을 목적지 섬까지 들고 갈 수 있는지 확인한다.
        /// 고무보트처럼 해류 제약이 있는 아이템은 대형(대)/특대 섬으로는 가져갈 수 없다.
        /// </summary>
        public bool CanCarryToIsland(ItemData itemData, IslandSize destinationSize)
        {
            if (!itemData.blockedFromLargeIslandsByCurrent)
                return true;

            return destinationSize != IslandSize.Large && destinationSize != IslandSize.ExtraLarge;
        }
    }
}
