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
        /// 고무보트처럼 해류 제약이 있는 아이템은 특대 섬으로는 가져갈 수 없다.
        /// 치명적 버그 수정: 예전에는 대형(대) 섬까지도 막았는데, 배 제작 1~2단계 도면은
        /// BoatBlueprintSpawner가 오직 대형 섬에만 배치한다(BoatBlueprintSpawner.cs 참고). 그런데
        /// IslandTravel.TryTravelTo는 "배 1단계를 완성해야만" 대형/특대 섬 해류를 뚫을 수 있게 했으니,
        /// 1단계를 완성하려면 대형 섬의 도면이 필요하고, 대형 섬에 가려면 이미 1단계를 완성했어야 하는
        /// 순환 잠금(soft-lock)이었다 - 배 엔딩 경로 전체가 처음부터 영원히 도달 불가능했다.
        /// 대형 섬은 처음부터 갈 수 있게 하고, 정말로 강한 해류가 필요한 특대 섬(최종 3단계 도면)만
        /// 진행도 요건으로 막아 두면 잠금 없이 원래 의도한 난이도 곡선(대형→특대 순으로 더 강해지는 해류)이 유지된다.
        /// </summary>
        public bool CanCarryToIsland(ItemData itemData, IslandSize destinationSize)
        {
            if (!itemData.blockedFromLargeIslandsByCurrent)
                return true;

            return destinationSize != IslandSize.ExtraLarge;
        }

        /// <summary>
        /// 채집 등으로 재료 아이템 하나를 인벤토리에 새로 추가한다 (제작 재료처럼 개수로 관리되는 아이템에 사용).
        /// </summary>
        public void AddItem(ItemData itemData)
        {
            items.Add(new InventoryItem(itemData));
        }

        /// <summary>
        /// 지정한 종류의 아이템을 실제로 소지 중인 개별 InventoryItem 인스턴스 하나를 찾는다.
        /// 무기/도구의 내구도(remainingUses)를 실제로 소모시키려면 ItemData(원본 정의)가 아니라
        /// 이 개별 인스턴스가 필요하다 (같은 "손도끼"라도 인스턴스마다 남은 사용 횟수가 다를 수 있음).
        /// 없으면 null을 반환한다.
        /// </summary>
        public InventoryItem FindItem(ItemData itemData)
        {
            foreach (var item in items)
            {
                if (item.data == itemData)
                    return item;
            }
            return null;
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
