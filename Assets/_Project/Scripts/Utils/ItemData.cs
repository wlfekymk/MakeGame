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

        [Tooltip("인벤토리/제작 UI에 표시할 아이템 아이콘 스프라이트. " +
            "비어 있으면 UI가 기존처럼 카테고리 색상 + 이름 첫 글자 placeholder로 대체 표시한다.")]
        public Sprite icon;

        [Tooltip("최대 사용 횟수. -1이면 무제한 사용 (예: 고무보트)")]
        public int maxUses = 1;

        [Tooltip("특대 섬은 해류가 너무 강해 이 아이템을 들고 갈 수 없는지 여부 (고무보트 전용 제약).\n" +
            "대형(대) 섬은 배 도면(1~2단계)을 구할 수 있는 유일한 장소라 처음부터 갈 수 있어야 하므로 제약에서 " +
            "제외했다 (자세한 내용은 PlayerInventory.CanCarryToIsland 참고).")]
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

        [Header("설치(빌드) 효과 (해당하는 경우에만 사용)")]
        [Tooltip("월드에 설치(건설)할 수 있는 키트 아이템인지 여부 (예: 물 증류기 키트, 쉼터 키트)")]
        public bool isPlaceable = false;

        [Tooltip("설치 시 월드에 생성할 프리팹 (isPlaceable이 true일 때만 사용)")]
        public GameObject placementPrefab;

        [Header("무기 효과 (해당하는 경우에만 사용)")]
        [Tooltip("위험 요소(맹수/식인종 등)를 상대로 전투에 사용할 수 있는 무기인지 여부 (예: 칼, 손도끼, 창)")]
        public bool isWeapon = false;

        [Tooltip("이 무기로 위험 요소를 공격했을 때 입히는 피해량 (isWeapon이 true일 때만 사용)")]
        public float weaponDamage = 10f;

        [Header("치료 효과 (해당하는 경우에만 사용)")]
        [Tooltip("사용 시 출혈 상태 이상을 치료하는지 여부 (예: 붕대)")]
        public bool curesBleeding = false;

        [Tooltip("사용 시 중독 상태 이상을 치료하는지 여부 (예: 해독제)")]
        public bool curesPoison = false;

        [Tooltip("사용 시 골절 상태 이상을 치료하는지 여부 (예: 부목)")]
        public bool curesBrokenBone = false;

        /// <summary>
        /// 이 아이템이 사용 횟수 무제한인지 여부를 반환한다.
        /// </summary>
        public bool IsUnlimited => maxUses < 0;

        /// <summary>
        /// 이 아이템이 섭취/사용 가능한지 여부를 반환한다 (허기·갈증 회복 효과 또는 상태 이상 치료 효과가 있는 경우).
        /// 치료 효과 필드가 추가되기 전에는 붕대처럼 치료만 하는 아이템이 IsConsumable=false로 판정되어
        /// C(섭취) 키로 아예 사용할 방법이 없는 죽은 아이템이 되는 버그가 있었다.
        /// </summary>
        public bool IsConsumable => hungerRestoreAmount > 0f || thirstRestoreAmount > 0f
            || curesBleeding || curesPoison || curesBrokenBone;
    }
}
