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

    /// <summary>
    /// 인벤토리 한 칸(슬롯)에 담긴 내용을 나타내는 **읽기 전용 뷰**. PlayerInventory.GetStacks()가 만든다.
    ///
    /// 왜 뷰인가: PlayerInventory.items는 지금까지처럼 "1개 = 1항목"인 평면 리스트로 남는다.
    /// 프로젝트 전역에서 items를 직접 순회하며 항목 수를 개수로 세거나(Shelter.CountByName,
    /// WorldMapManager) 항목 하나를 1개로 보고 지우는 코드(Shelter.ConsumeMaterials)가 이미 있어,
    /// 내부 표현을 "1항목 = N개"로 바꾸면 그쪽이 조용히 어긋난다(제작 재료가 통째로 사라진다).
    /// 그래서 스택은 저장 구조가 아니라 파생 뷰로 만들고, 칸 수 계산·UI 표시·세이브 압축만 이 뷰를 쓴다.
    ///
    /// 이 객체를 들고 있지 마라. 인벤토리가 바뀌면 낡은 값이 된다. 표시할 때마다 새로 받아라.
    /// </summary>
    public class InventoryStack
    {
        /// <summary>이 칸에 담긴 아이템 종류.</summary>
        public ItemData data;

        /// <summary>이 칸에 담긴 개수(1 이상, data.MaxStackSize 이하).</summary>
        public int count;

        /// <summary>
        /// 이 칸을 대표하는 실제 인스턴스. 내구도 표시나 UseItem 호출에 쓴다.
        /// 스택 가능한 아이템은 같은 칸 안의 remainingUses가 모두 같으므로 대표 하나로 충분하다.
        /// </summary>
        public InventoryItem representative;

        /// <summary>대표 인스턴스의 남은 사용 횟수(무제한이면 -1). 대표가 없으면 0.</summary>
        public int RemainingUses => representative != null ? representative.remainingUses : 0;

        public InventoryStack(ItemData data, int count, InventoryItem representative)
        {
            this.data = data;
            this.count = count;
            this.representative = representative;
        }
    }
}
