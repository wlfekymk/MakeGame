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

        /// <summary>
        /// maxStackSize가 설정되지 않은(0 이하) 아이템이 쓰는 코드 기본 스택 상한.
        /// 32개 .asset 중 어느 것도 아직 이 키를 갖고 있지 않으므로, 당분간 모든 아이템이 이 값을 쓴다.
        /// </summary>
        public const int DefaultMaxStackSize = 20;

        [Header("스택 (정착 배치 3)")]
        [Tooltip("한 칸에 겹쳐 담을 수 있는 최대 개수. 0 이하이면 코드 기본값(DefaultMaxStackSize=20)을 쓴다.\n" +
            "기존 .asset에는 이 키가 없어 역직렬화 시 0으로 읽힐 수 있으므로, 값을 직접 읽지 말고 반드시" +
            " MaxStackSize 프로퍼티를 거쳐라 - 0을 기본값으로 되돌리는 폴백이 거기에 있다.\n" +
            "개별 상태(남은 내구도)가 있는 도구(maxUses > 1: 창/손도끼/라이터)는 이 값과 무관하게 항상 1칸이다.")]
        public int maxStackSize = DefaultMaxStackSize;

        [Tooltip("특대 섬은 해류가 너무 강해 이 아이템을 들고 갈 수 없는지 여부 (고무보트 전용 제약).\n" +
            "대형(대) 섬은 배 도면(1~2단계)을 구할 수 있는 유일한 장소라 처음부터 갈 수 있어야 하므로 제약에서 " +
            "제외했다 (자세한 내용은 PlayerInventory.CanCarryToIsland 참고).")]
        public bool blockedFromLargeIslandsByCurrent = false;

        [Header("재질 계열 (B3-9)")]
        [Tooltip("이 아이템의 재질 계열. IslandResourceSpawner 등이 표면 텍스처/분류를 결정할 때 itemName" +
            " 문자열 추론 대신 우선 참조하는 필드다.\n" +
            "기본값은 반드시 None이어야 한다 - 이 필드가 추가되기 전부터 존재하던 43개의 기존 .asset이" +
            " 전부 역직렬화 시 0(None)으로 채워지기 때문이다. None일 때는 기존 문자열 추론 로직으로" +
            " 자동 폴백하므로, game-designer가 이후 배치(B3-10)에서 .asset 값을 채우기 전까지는 동작이" +
            " 전혀 바뀌지 않는다.")]
        public MaterialFamily materialFamily = MaterialFamily.None;

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

        // ── 부패 / 훈연 (식량 루프) ────────────────────────────────────────────────────────
        // **추가만 했다.** 기존 필드는 하나도 지우거나 이름을 바꾸지 않았다. 32개 .asset 중 어느 것도
        // 아직 이 두 키를 갖고 있지 않으므로 전부 역직렬화 시 0 / null로 읽히고, 그 두 값이 정확히
        // "규칙으로 알아서 정해라"를 뜻하도록 기본값을 골랐다(materialFamily가 None을 기본으로 둔 것과
        // 같은 판단). 즉 game-designer가 값을 채우기 전까지 동작이 전혀 바뀌지 않는다.

        [Header("부패 (식량 루프)")]
        [Tooltip("이 음식이 완전히 부패하기까지의 게임 내 일수(1일 = SurvivalClock.secondsPerDay = 600초).\n" +
            "· 0(기본값·미설정) = FoodSpoilage의 규칙으로 자동 판정한다. 허기 회복이 없는 것(재료/음료/" +
            "키트)과 비상식량·훈제품은 부패 없음, 생음식(isRawFood)은 1일, 나머지 익힌 음식은 3일이다.\n" +
            "· 양수 = 그 일수를 그대로 쓴다(자동 판정보다 우선).\n" +
            "· 음수(-1 권장) = **절대 상하지 않는다**는 명시적 표시. 훈제육/훈제생선처럼 이름 규칙에" +
            " 기대지 않고 못 박고 싶을 때 쓴다.")]
        public float spoilDays = 0f;

        [Tooltip("훈연기(Smoker)에 넣었을 때 나오는 결과 아이템. 비워 두면 Smoker가 이름 규칙" +
            "(생고기→훈제육 · 생선→훈제생선)으로 찾아본다. 그마저 없으면 그 재료는 훈연할 수 없다.")]
        public ItemData smokedResult;

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
        /// 이 아이템을 한 칸에 겹쳐 담을 수 있는지 여부.
        /// 판정 기준은 "개별 상태가 있는가" 하나다 - InventoryItem이 인스턴스별로 들고 있는 가변 상태는
        /// remainingUses뿐이므로, 그 값이 인스턴스마다 달라질 수 있는 아이템만 스택에서 제외한다.
        /// · maxUses &gt; 1 (창 15 / 손도끼 20 / 라이터 5) → 반쯤 닳은 것과 새 것을 한 칸에 합치면
        ///   남은 내구도 정보가 사라진다. 스택 불가.
        /// · maxUses &lt; 0 (칼 / 물통 / 고무보트 / 파이어스타터) → IsUnlimited라 Use()가 값을 건드리지
        ///   않는다. remainingUses는 영원히 -1이다. 개별 상태가 없으므로 스택 가능.
        /// · maxUses == 1 (나머지 26종: 자원·음식·키트·치료제) → 1회 쓰면 인벤토리에서 사라지므로
        ///   인벤토리 안에 있는 동안 remainingUses는 항상 1이다. 개별 상태가 없으므로 스택 가능.
        /// </summary>
        public bool IsStackable => maxUses <= 1;

        /// <summary>
        /// 실제로 적용되는 한 칸당 최대 개수. 스택 불가 아이템은 1, 그 외에는 maxStackSize(0 이하면
        /// 코드 기본값)를 돌려준다. **maxStackSize 필드를 직접 읽지 말고 항상 이 프로퍼티를 써라.**
        /// </summary>
        public int MaxStackSize => IsStackable ? (maxStackSize > 0 ? maxStackSize : DefaultMaxStackSize) : 1;

        /// <summary>
        /// 이 아이템이 섭취/사용 가능한지 여부를 반환한다 (허기·갈증 회복 효과 또는 상태 이상 치료 효과가 있는 경우).
        /// 치료 효과 필드가 추가되기 전에는 붕대처럼 치료만 하는 아이템이 IsConsumable=false로 판정되어
        /// C(섭취) 키로 아예 사용할 방법이 없는 죽은 아이템이 되는 버그가 있었다.
        /// </summary>
        public bool IsConsumable => hungerRestoreAmount > 0f || thirstRestoreAmount > 0f
            || curesBleeding || curesPoison || curesBrokenBone;
    }
}
