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
        /// 게임 시작 시 자동으로 시작 아이템 풀을 지급한다 (불시착 직후 상황 재현).
        /// </summary>
        private void Start()
        {
            GrantStartingLoadout();
        }

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

        /// <summary>
        /// 채집 등으로 재료 아이템 하나를 인벤토리에 새로 추가한다 (제작 재료처럼 개수로 관리되는 아이템에 사용).
        /// </summary>
        public void AddItem(ItemData itemData)
        {
            items.Add(new InventoryItem(itemData));
        }

        /// <summary>
        /// 지정한 아이템을 인벤토리에 몇 개 가지고 있는지 센다 (제작 재료 확인 등에 사용).
        /// </summary>
        public int GetItemCount(ItemData itemData)
        {
            int count = 0;
            foreach (var item in items)
            {
                if (item.data == itemData)
                    count++;
            }
            return count;
        }

        /// <summary>
        /// 지정한 아이템을 count개 만큼 인벤토리에서 제거한다.
        /// 보유 수량이 부족하면 아무것도 제거하지 않고 false를 반환한다 (제작 시 재료 소모 등에 사용).
        /// </summary>
        public bool RemoveItems(ItemData itemData, int count)
        {
            if (GetItemCount(itemData) < count)
                return false;

            int removed = 0;
            for (int i = items.Count - 1; i >= 0 && removed < count; i--)
            {
                if (items[i].data == itemData)
                {
                    items.RemoveAt(i);
                    removed++;
                }
            }
            return true;
        }
    }
}
