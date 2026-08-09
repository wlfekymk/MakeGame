using UnityEngine;

namespace MakeGame.Data
{
    /// <summary>
    /// 아이템 하나의 고정 데이터(원본 정의)를 담는 ScriptableObject.
    /// 플레이어가 실제로 소지한 개별 아이템의 "남은 사용 횟수" 같은 가변 상태는
    /// MakeGame.Player.InventoryItem에서 별도로 관리한다.
    /// </summary>
    [CreateAssetMenu(fileName = "NewItemData", menuName = "MakeGame/Item Data", order = 0)]
    public class ItemData : ScriptableObject
    {
        [Tooltip("아이템 이름 (예: 고무보트, 생수, 라이터, 칼)")]
        public string itemName;

        [TextArea]
        [Tooltip("아이템 설명")]
        public string description;

        [Tooltip("최대 사용 횟수. -1이면 무제한 사용 (예: 고무보트)")]
        public int maxUses = 1;

        [Tooltip("대형(대) 섬부터 해류가 강해져서 이 아이템을 들고 갈 수 없는지 여부 (고무보트 전용 제약)")]
        public bool blockedFromLargeIslandsByCurrent = false;

        /// <summary>
        /// 이 아이템이 사용 횟수 무제한인지 여부를 반환한다.
        /// </summary>
        public bool IsUnlimited => maxUses < 0;
    }
}
