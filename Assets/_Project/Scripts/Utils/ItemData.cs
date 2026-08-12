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

        [Header("음식/음료 효과 (해당하는 경우에만 사용)")]
        [Tooltip("섭취 시 회복되는 허기 수치. 0이면 음식이 아니다.")]
        public float hungerRestoreAmount = 0f;

        [Tooltip("섭취 시 회복되는 갈증 수치. 0이면 음료가 아니다.")]
        public float thirstRestoreAmount = 0f;

        [Tooltip("익히지 않은 음식인지 여부. true면 생으로 섭취 시 식중독(중독) 위험이 있다.")]
        public bool isRawFood = false;

        [Tooltip("모닥불에서 조리했을 때 변환되는 결과 아이템 (isRawFood가 true일 때만 사용)")]
        public ItemData cookedResult;

        [Tooltip("코코넛 워터처럼 과음 시 설사로 갈증이 급격히 악화되는 수분 공급원인지 여부")]
        public bool isCoconutWaterSource = false;

        /// <summary>
        /// 이 아이템이 사용 횟수 무제한인지 여부를 반환한다.
        /// </summary>
        public bool IsUnlimited => maxUses < 0;

        /// <summary>
        /// 이 아이템이 섭취 가능한 음식/음료인지 여부를 반환한다 (허기 또는 갈증 회복 효과가 있는 경우).
        /// </summary>
        public bool IsConsumable => hungerRestoreAmount > 0f || thirstRestoreAmount > 0f;
    }
}
