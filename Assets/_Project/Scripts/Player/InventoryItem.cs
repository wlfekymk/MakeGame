using UnityEngine;
using MakeGame.Data;
using MakeGame.Systems;

namespace MakeGame.Player
{
    /// <summary>
    /// 플레이어가 실제로 소지한 아이템 1개의 상태(원본 데이터 + 남은 사용 횟수 + 부패 경과)를 나타낸다.
    /// </summary>
    [System.Serializable]
    public class InventoryItem
    {
        public ItemData data;
        public int remainingUses;

        // ── 부패 (식량 루프) ──────────────────────────────────────────────────────────────
        //
        // **인스턴스별 상태를 remainingUses 옆에 하나 더 두는 방식이다.** 내구도 자리에 얹지 않은
        // 이유는 둘의 의미가 정면으로 충돌하기 때문이다: 음식은 전부 maxUses = 1이라
        // remainingUses가 인벤토리에 있는 동안 항상 1이고(ItemData.IsStackable 주석), 거기에 신선도를
        // 넣으면 "남은 사용 횟수 <= 0이면 제거"(InventoryItem.Use)에 걸려 상한 음식이 조용히 증발한다.
        //
        // 값은 **남은 신선도가 아니라 누적 경과 시간(초)** 이다. 이 방향이 세이브/복원에 강하다 -
        // 신선도(0~1)로 저장하면 나중에 부패 속도표가 바뀔 때 옛 세이브의 음식 나이가 통째로
        // 뒤바뀌지만, 경과 시간은 속도표와 무관한 사실이라 그대로 다시 해석하면 된다.
        // 옛 세이브/씬 직렬화에는 이 키가 없어 0(= 갓 얻은 것)으로 읽히므로 전부 "신선"으로 복원된다.
        //
        // 시간을 앞으로 감는 곳은 FoodSpoilage 하나뿐이다(실시간 Time.deltaTime 기준 - 취침으로
        // 건너뛴 시간에는 허기/갈증도 흐르지 않으므로 부패도 흐르지 않는다. Shelter.TrySleep 주석 참고).

        [Tooltip("이 아이템이 만들어진 뒤 흐른 부패 경과 시간(초). 0이면 갓 얻은 상태다.\n" +
            "부패하지 않는 아이템(재료·음료·비상식량·훈제품)에서는 값이 늘어나도 무시된다.")]
        public float spoilAgeSeconds;

        /// <summary>이 아이템이 부패 대상인지(FoodSpoilage의 규칙 하나만 본다).</summary>
        public bool CanSpoil => FoodSpoilage.CanSpoil(data);

        /// <summary>
        /// 남은 신선도(1 = 갓 만든 것, 0 = 완전히 부패). **부패하지 않는 아이템은 항상 1이다** -
        /// UI가 종류를 따로 구분하지 않고도 이 값 하나로 게이지를 그릴 수 있게 하기 위한 것이다
        /// (구분이 필요하면 CanSpoil을 함께 봐라).
        /// </summary>
        public float Freshness01 => FoodSpoilage.GetFreshness01(this);

        /// <summary>지금의 부패 단계(없음/신선/상하기 시작/부패).</summary>
        public FoodSpoilStage SpoilStage => FoodSpoilage.GetStage(this);

        /// <summary>화면에 그대로 쓸 수 있는 부패 단계 문구("신선" / "상하기 시작" / "부패"). 대상이 아니면 빈 문자열.</summary>
        public string FreshnessLabel => FoodSpoilage.GetStageLabel(SpoilStage);

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

        // ── 부패 (식량 루프) ──────────────────────────────────────────────────────────────
        //
        // **한 칸의 신선도는 그 칸에서 가장 오래된 것을 따른다(평균이 아니다).**
        // 근거는 두 가지다.
        //  (1) 평균은 거짓말을 한다. 다 상한 고기 19개에 갓 잡은 것 1개를 얹으면 평균은 올라가지만
        //      플레이어가 다음에 먹을 한 개는 여전히 상한 것이다. "표시된 값"과 "실제로 입에 들어가는
        //      것"이 갈리는 순간 그 표시는 신뢰를 잃는다.
        //  (2) 이 프로젝트의 섭취/저장 경로가 이미 "가장 오래된 것부터"에 맞출 수 있다.
        //      ConsumptionSystem은 같은 종류 중 가장 상한 인스턴스를 골라 먹이고(FIFO),
        //      세이브는 스택을 한 줄로 접을 때 이 값을 적는다 - 셋이 전부 같은 기준이라 어긋나지 않는다.
        // 대가는 "복원하면 한 칸이 통째로 가장 오래된 것의 나이가 된다"는 것뿐이고, 그 방향은
        // 플레이어에게 불리한 쪽(= 공짜 신선도를 주지 않는 쪽)이라 안전하다.

        /// <summary>
        /// 이 칸에서 **가장 많이 상한** 인스턴스. 스택되지 않는 아이템은 representative와 같다.
        /// PlayerInventory.GetStacks가 칸을 합칠 때마다 갱신한다.
        /// </summary>
        public InventoryItem oldest;

        /// <summary>이 칸이 신선도를 표시해야 하는 종류인지(음식이 아니면 false).</summary>
        public bool ShowsFreshness => oldest != null && oldest.CanSpoil;

        /// <summary>이 칸의 신선도(가장 오래된 것 기준, 1 = 신선). 부패 대상이 아니면 항상 1.</summary>
        public float Freshness01 => oldest != null ? oldest.Freshness01 : 1f;

        /// <summary>이 칸의 부패 단계(가장 오래된 것 기준).</summary>
        public FoodSpoilStage SpoilStage => oldest != null ? oldest.SpoilStage : FoodSpoilStage.None;

        /// <summary>이 칸에 붙일 부패 단계 문구("신선" / "상하기 시작" / "부패"). 대상이 아니면 빈 문자열.</summary>
        public string FreshnessLabel => oldest != null ? oldest.FreshnessLabel : "";

        public InventoryStack(ItemData data, int count, InventoryItem representative)
        {
            this.data = data;
            this.count = count;
            this.representative = representative;

            // 합쳐지기 전에는 칸 안에 대표 하나뿐이므로 그것이 곧 가장 오래된 것이다.
            // 이 기본값 덕분에 이 생성자를 그대로 쓰는 다른 호출부(StorageChest.GetStacks)도
            // oldest가 null이 되지 않는다(= 상자 UI가 신선도를 읽어도 NRE가 없다).
            this.oldest = representative;
        }
    }
}
