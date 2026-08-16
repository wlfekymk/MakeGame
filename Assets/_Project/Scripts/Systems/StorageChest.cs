using System.Collections.Generic;
using UnityEngine;
using MakeGame.Data;
using MakeGame.Player;

namespace MakeGame.Systems
{
    /// <summary>
    /// 보관 상자 하나의 **내용물과 등급**. GameObject와 수명을 분리해 두는 그릇이다.
    ///
    /// 왜 컴포넌트가 직접 들고 있지 않은가: 뗏목 갑판이 다시 만들어지면 갑판 밑 컨테이너가 통째로
    /// 파괴될 수 있고(BuildingSystem.RestoreDeckPiecesAfterRebuild), 그때 상자 실물도 함께 사라진다.
    /// 상태가 컴포넌트 안에만 있으면 그 순간 내용물이 조용히 증발한다. BuildingSystem이 이 그릇을
    /// 조각 기록(PlacedPiece)에 들고 있다가 새 실물에 다시 물려주므로, 실물이 몇 번 다시 만들어져도
    /// 내용물과 등급은 그대로 남는다.
    ///
    /// 내부 표현은 <see cref="PlayerInventory.items"/>와 **완전히 같은 규약**이다:
    /// 1개 = 1항목인 평면 리스트이고, 칸 수는 종류별로 묶어 스택 기준으로 센다.
    /// </summary>
    public class StorageChestState
    {
        /// <summary>0=소 1=중 2=대 3=특대.</summary>
        public int tier;

        /// <summary>보관 중인 아이템. **1개 = 1항목**이다(PlayerInventory.items와 같은 규약).</summary>
        public readonly List<InventoryItem> items = new List<InventoryItem>();
    }

    /// <summary>
    /// 플레이어가 아이템을 넣고 꺼낼 수 있는 보관 상자. 건축 부품(<see cref="BuildPieceType.Chest"/>)으로
    /// 설치되며, 실물 GameObject의 루트에 BuildingSystem이 붙인다.
    ///
    /// 등급은 소·중·대·특대(0~3)이고 칸 수는 50 / 100 / 150 / 200이다. **상위 등급은 직접 지을 수 없다** -
    /// 소형만 설치할 수 있고 나머지는 <see cref="TryUpgrade"/>로만 도달한다. 업그레이드는 내용물을
    /// 건드리지 않는다(재료만 소모하고 Tier를 1 올린다).
    ///
    /// 내부 저장 규약은 PlayerInventory와 같다 - **1개 = 1항목인 평면 리스트**이고, 칸 수는 그 리스트를
    /// 종류별로 묶어 MaxStackSize로 나눈 값이다. 이 프로젝트에는 items를 평면으로 순회하며 개수를 세는
    /// 코드가 이미 여러 곳(Shelter.CountByName 등) 있어 "1항목 = N개"로 바꾸면 조용히 어긋난다.
    /// </summary>
    public class StorageChest : MonoBehaviour
    {
        // ────────────────────────────────────────────────────────────────────────
        // 조준 중인 상자 (UI가 이 값 하나만 보고 창을 연다)
        // ────────────────────────────────────────────────────────────────────────

        /// <summary>플레이어가 지금 조준하고 있는 상자(없으면 null). BuildingSystem이 매 프레임 갱신한다.</summary>
        public static StorageChest Focused { get; private set; }

        /// <summary>Focused가 실제로 **바뀐 프레임에만** 발행된다(매 프레임 발행하지 않는다).</summary>
        public static event System.Action FocusChanged;

        /// <summary>
        /// 어떤 상자든 등급이 올랐을 때 발행된다. BuildingSystem이 이 신호를 받아 실물의 겉모습만
        /// 새 등급으로 갈아 끼운다(루트 GameObject는 그대로 두므로 UI가 들고 있는 참조는 끊기지 않는다).
        /// </summary>
        public static event System.Action<StorageChest> TierChanged;

        /// <summary>
        /// 도메인 리로드가 꺼져 있어도 정적 상태가 이전 플레이 세션에서 넘어오지 않게 초기화한다
        /// (BuildingSystem.Bootstrap과 같은 시점).
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticState()
        {
            Focused = null;
            FocusChanged = null;
            TierChanged = null;
        }

        /// <summary>
        /// 조준 중인 상자를 바꾼다. 값이 그대로면 아무 일도 하지 않는다(이벤트도 나가지 않는다).
        /// 파괴된 상자는 Unity의 == 규약상 null과 같으므로 null로 정규화해서 들고 있는다.
        /// </summary>
        public static void SetFocused(StorageChest chest)
        {
            // Unity의 == 는 파괴된 인스턴스를 null로 취급한다. 여기서 진짜 null로 정규화해 두면
            // 아래 ReferenceEquals 비교가 "파괴된 상자를 계속 조준 중"으로 오판하지 않는다.
            if (chest == null)
                chest = null;

            if (ReferenceEquals(Focused, chest))
                return;

            Focused = chest;
            FocusChanged?.Invoke();
        }

        // ────────────────────────────────────────────────────────────────────────
        // 상태
        // ────────────────────────────────────────────────────────────────────────

        /// <summary>내용물과 등급의 실제 그릇. BuildingSystem이 물려주며, 없으면 스스로 하나 만든다.</summary>
        private StorageChestState state;

        /// <summary>내용물 또는 등급이 바뀔 때마다 발행된다.</summary>
        public event System.Action Changed;

        private PlayerInventory cachedInventory;

        // 칸 수는 내용물이 바뀔 때만 다시 센다. 특대 상자는 최대 4000개 항목(200칸 × 20개)이 될 수 있어
        // UI가 매 프레임 UsedSlots를 읽으면 그때마다 전수 순회가 일어난다.
        private bool usedSlotsDirty = true;
        private int usedSlotsCache;

        // 칸 수 계산용 재사용 버퍼(PlayerInventory.CountUsedSlots와 같은 방식 - 호출마다 List를 만들지 않는다).
        private readonly List<ItemData> kindBuffer = new List<ItemData>();
        private readonly List<int> kindCountBuffer = new List<int>();

        private void Awake()
        {
            if (state == null)
                state = new StorageChestState();
        }

        private void OnDestroy()
        {
            if (ReferenceEquals(Focused, this))
            {
                Focused = null;
                FocusChanged?.Invoke();
            }
        }

        /// <summary>
        /// 내용물/등급 그릇을 물려받는다. **BuildingSystem 전용이다** - 실물이 다시 만들어져도 같은
        /// 그릇을 다시 물려주면 상자의 내용이 그대로 이어진다. null을 넘기면 무시한다.
        /// </summary>
        public void Bind(StorageChestState boundState)
        {
            if (boundState == null)
                return;

            state = boundState;
            usedSlotsDirty = true;
            Changed?.Invoke();
        }

        /// <summary>이 상자가 쓰고 있는 그릇(세이브가 직접 읽는다). 항상 null이 아니다.</summary>
        public StorageChestState State
        {
            get
            {
                if (state == null)
                    state = new StorageChestState();
                return state;
            }
        }

        // ────────────────────────────────────────────────────────────────────────
        // 조회 (상태를 바꾸지 않는다 - UI가 매 프레임 불러도 안전하다)
        // ────────────────────────────────────────────────────────────────────────

        /// <summary>0=소 1=중 2=대 3=특대.</summary>
        public int Tier => BuildPieceCatalog.ClampChestTier(State.tier);

        /// <summary>"소형 상자" / "중형 상자" / "대형 상자" / "특대 상자".</summary>
        public string TierDisplayName => BuildPieceCatalog.GetChestTierDisplayName(Tier);

        /// <summary>이 등급의 칸 수(50 / 100 / 150 / 200).</summary>
        public int SlotCapacity => BuildPieceCatalog.GetChestCapacity(Tier);

        /// <summary>지금 쓰고 있는 칸 수. GetStacks()가 돌려주는 스택 개수와 항상 같다.</summary>
        public int UsedSlots
        {
            get
            {
                if (usedSlotsDirty)
                {
                    usedSlotsCache = CountUsedSlots();
                    usedSlotsDirty = false;
                }
                return usedSlotsCache;
            }
        }

        /// <summary>남은 빈 칸 수(세이브 복원으로 상한을 넘긴 경우에도 음수가 되지 않는다).</summary>
        public int FreeSlots => Mathf.Max(0, SlotCapacity - UsedSlots);

        /// <summary>비어 있는지 여부. 철거 판정이 이 값을 본다(내용물이 남아 있으면 부술 수 없다).</summary>
        public bool IsEmpty => State.items.Count == 0;

        /// <summary>더 올릴 등급이 남아 있는지(Tier &lt; 3).</summary>
        public bool CanUpgrade => Tier < BuildPieceCatalog.ChestTierCount - 1;

        /// <summary>다음 등급으로 올리는 데 드는 재료. **최고 등급이면 빈 목록**이다(null이 아니다).</summary>
        public IReadOnlyList<BuildPieceCost> UpgradeCost => BuildPieceCatalog.GetChestUpgradeCost(Tier);

        /// <summary>다음 등급의 칸 수(최고 등급이면 지금 칸 수). UI가 "50 → 100"을 띄울 때 쓴다.</summary>
        public int NextTierCapacity => BuildPieceCatalog.GetChestCapacity(Tier + 1);

        /// <summary>다음 등급의 이름(최고 등급이면 지금 이름).</summary>
        public string NextTierDisplayName => BuildPieceCatalog.GetChestTierDisplayName(Tier + 1);

        /// <summary>
        /// 내용물을 칸 단위로 묶은 뷰를 새 리스트에 담아 돌려준다.
        /// **PlayerInventory.GetStacks()와 완전히 같은 규약**이다(같은 종류는 MaxStackSize 단위로 나뉘고,
        /// 내구도가 있는 도구는 인스턴스마다 별도 스택이 된다. 순서는 items에 처음 나타난 순서다).
        /// </summary>
        public List<InventoryStack> GetStacks()
        {
            var result = new List<InventoryStack>();
            GetStacks(result);
            return result;
        }

        /// <summary>GetStacks()와 같지만 호출부가 넘긴 버퍼를 재사용한다. 버퍼는 내부에서 Clear된다.</summary>
        public void GetStacks(List<InventoryStack> buffer)
        {
            if (buffer == null)
                return;

            buffer.Clear();

            List<InventoryItem> items = State.items;
            for (int i = 0; i < items.Count; i++)
            {
                InventoryItem item = items[i];
                if (item == null || item.data == null)
                    continue;

                int max = item.data.MaxStackSize;
                if (max <= 1)
                {
                    buffer.Add(new InventoryStack(item.data, 1, item));
                    continue;
                }

                InventoryStack target = null;
                for (int s = buffer.Count - 1; s >= 0; s--)
                {
                    if (buffer[s].data != item.data)
                        continue;
                    if (buffer[s].count < max)
                        target = buffer[s];
                    break;
                }

                if (target != null)
                    target.count++;
                else
                    buffer.Add(new InventoryStack(item.data, 1, item));
            }
        }

        /// <summary>
        /// 이 상자가 지금 이 아이템을 count개 더 받을 수 있는지. 상태를 전혀 바꾸지 않는다.
        /// 이미 담긴 스택에 빈자리가 있으면 새 칸이 필요 없으므로, 칸이 가득 차 있어도 true일 수 있다
        /// (PlayerInventory.CanAccept와 완전히 같은 판정이다).
        /// </summary>
        public bool CanAccept(ItemData itemData, int count = 1)
        {
            if (itemData == null || count <= 0)
                return false;

            int max = itemData.MaxStackSize;
            int need;
            if (max <= 1)
            {
                need = count;
            }
            else
            {
                int have = GetItemCount(itemData);
                need = SlotsFor(have + count, max) - SlotsFor(have, max);
            }

            return UsedSlots + need <= SlotCapacity;
        }

        /// <summary>이 상자에 그 종류가 몇 개 들어 있는지.</summary>
        public int GetItemCount(ItemData itemData)
        {
            if (itemData == null)
                return 0;

            List<InventoryItem> items = State.items;
            int count = 0;
            for (int i = 0; i < items.Count; i++)
            {
                if (items[i] != null && items[i].data == itemData)
                    count++;
            }
            return count;
        }

        // ────────────────────────────────────────────────────────────────────────
        // 넣기 / 꺼내기
        // ────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// 플레이어 인벤토리에서 상자로 옮긴다. **전부 옮길 수 있을 때만 옮긴다** -
        /// 자리가 모자라거나 플레이어가 그만큼 갖고 있지 않으면 아무것도 바꾸지 않고 false다.
        /// 성공하면 플레이어 쪽에서 지우는 것까지 끝난 상태로 true를 돌려준다.
        /// 내구도(remainingUses)는 옮긴 인스턴스의 값을 그대로 가져간다(반쯤 닳은 손도끼가 새것이 되지 않는다).
        /// </summary>
        public bool TryDeposit(ItemData itemData, int count)
        {
            if (itemData == null || count <= 0)
                return false;

            PlayerInventory inventory = ResolveInventory();
            if (inventory == null || inventory.items == null)
                return false;

            if (inventory.GetItemCount(itemData) < count)
                return false;

            if (!CanAccept(itemData, count))
                return false;

            List<InventoryItem> items = State.items;
            int moved = 0;
            for (int i = inventory.items.Count - 1; i >= 0 && moved < count; i--)
            {
                InventoryItem item = inventory.items[i];
                if (item == null || item.data != itemData)
                    continue;

                inventory.items.RemoveAt(i);
                items.Add(new InventoryItem(itemData) { remainingUses = item.remainingUses });
                moved++;
            }

            if (moved <= 0)
                return false;

            inventory.NotifyInventoryChanged();
            MarkChanged();
            return true;
        }

        /// <summary>
        /// 상자에서 플레이어 인벤토리로 옮긴다. 플레이어가 가득 차면 **넣을 수 있는 만큼만** 넣고
        /// 실제로 옮긴 개수를 돌려준다(0일 수 있다). 상자 쪽도 딱 그만큼만 줄어든다 - 넘친 몫은
        /// 상자에 그대로 남는다(아이템이 사라지는 경로를 만들지 않는다).
        /// </summary>
        public int Withdraw(ItemData itemData, int count)
        {
            if (itemData == null || count <= 0)
                return 0;

            PlayerInventory inventory = ResolveInventory();
            if (inventory == null)
                return 0;

            List<InventoryItem> items = State.items;
            int moved = 0;

            for (int i = items.Count - 1; i >= 0 && moved < count; i--)
            {
                InventoryItem item = items[i];
                if (item == null || item.data != itemData)
                    continue;

                // 한 개씩 확인한다 - 넣을 때마다 남은 칸이 줄어들기 때문이다.
                if (!inventory.CanAccept(itemData, 1))
                    break;

                items.RemoveAt(i);
                // 용량 검사는 방금 CanAccept로 끝냈다. 내구도를 지정해 넣을 수 있는 경로가 이것뿐이라
                // AddItemIgnoringCapacity를 쓴다(TryAddItem은 remainingUses를 새것으로 되돌린다).
                inventory.AddItemIgnoringCapacity(itemData, item.remainingUses);
                moved++;
            }

            if (moved > 0)
                MarkChanged();

            return moved;
        }

        // ────────────────────────────────────────────────────────────────────────
        // 업그레이드
        // ────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// 다음 등급으로 올린다. **내용물은 그대로 둔다** - 칸 수만 늘어난다.
        /// 재료가 모자라거나 최고 등급이면 아무것도 바꾸지 않고 false + 한국어 사유를 돌려준다.
        /// 재료는 <see cref="BuildingSystem.TryPlace"/>와 같은 규칙으로 **전부 있는지 먼저 확인한 뒤에**
        /// 지운다(중간에 모자라면 이미 지운 재료를 되돌릴 방법이 없다).
        /// </summary>
        public bool TryUpgrade(out string failReason)
        {
            if (!CanUpgrade)
            {
                failReason = "이미 가장 큰 상자다";
                return false;
            }

            PlayerInventory inventory = ResolveInventory();
            if (inventory == null || inventory.items == null)
            {
                failReason = "인벤토리를 찾지 못했다";
                return false;
            }

            IReadOnlyList<BuildPieceCost> cost = UpgradeCost;
            for (int i = 0; i < cost.Count; i++)
            {
                if (CountOwnedByName(inventory, cost[i].itemName) < cost[i].count)
                {
                    failReason = "재료가 부족하다";
                    return false;
                }
            }

            for (int i = 0; i < cost.Count; i++)
            {
                BuildPieceCost entry = cost[i];
                if (string.IsNullOrEmpty(entry.itemName) || entry.count <= 0)
                    continue;

                int remaining = entry.count;
                for (int k = inventory.items.Count - 1; k >= 0 && remaining > 0; k--)
                {
                    InventoryItem item = inventory.items[k];
                    if (item == null || item.data == null || item.data.itemName != entry.itemName)
                        continue;

                    inventory.items.RemoveAt(k);
                    remaining--;
                }
            }

            inventory.NotifyInventoryChanged();

            State.tier = BuildPieceCatalog.ClampChestTier(State.tier + 1);
            usedSlotsDirty = true;

            AudioManager.Instance?.PlayCraftSuccess();

            // 겉모습 교체가 먼저다. UI(Changed 구독자)가 그리는 시점에는 이미 새 등급의 실물이 서 있다.
            TierChanged?.Invoke(this);
            Changed?.Invoke();

            failReason = null;
            return true;
        }

        // ────────────────────────────────────────────────────────────────────────
        // 세이브 전용 (BuildingSystem이 부른다 - 이벤트를 거치지 않고 그릇을 직접 채운다)
        // ────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// 세이브 복원용으로 아이템 하나를 용량 검사 없이 넣는다.
        /// 검사하지 않는 이유는 PlayerInventory.AddItemIgnoringCapacity와 같다 - 상한을 넘긴 옛 기록에서
        /// 넘치는 만큼을 버리면 플레이어의 물건이 사라진다. 넘친 채로 복원하고 이후 투입만 막는다.
        /// </summary>
        public void AddItemIgnoringCapacity(ItemData itemData, int remainingUses)
        {
            if (itemData == null)
                return;

            State.items.Add(new InventoryItem(itemData) { remainingUses = remainingUses });
            usedSlotsDirty = true;
        }

        /// <summary>세이브 복원이 끝난 뒤 한 번 불러 UI에 알린다(항목마다 이벤트를 쏘지 않기 위해 분리했다).</summary>
        public void NotifyChanged()
        {
            MarkChanged();
        }

        // ────────────────────────────────────────────────────────────────────────
        // 내부
        // ────────────────────────────────────────────────────────────────────────

        private void MarkChanged()
        {
            usedSlotsDirty = true;
            Changed?.Invoke();
        }

        /// <summary>플레이어 인벤토리(없으면 null). 매 호출 씬 전체를 훑지 않도록 캐시한다.</summary>
        private PlayerInventory ResolveInventory()
        {
            if (cachedInventory == null)
                cachedInventory = FindAnyObjectByType<PlayerInventory>();
            return cachedInventory;
        }

        /// <summary>인벤토리에 같은 **이름**의 아이템이 몇 개 있는지(재료표가 이름만 들고 있어서 필요하다).</summary>
        private static int CountOwnedByName(PlayerInventory inventory, string itemName)
        {
            if (inventory == null || inventory.items == null || string.IsNullOrEmpty(itemName))
                return 0;

            int count = 0;
            for (int i = 0; i < inventory.items.Count; i++)
            {
                InventoryItem item = inventory.items[i];
                if (item != null && item.data != null && item.data.itemName == itemName)
                    count++;
            }
            return count;
        }

        /// <summary>
        /// 지금 쓰고 있는 칸 수를 센다. **PlayerInventory.CountUsedSlots와 같은 식**이다 -
        /// 종류별 개수를 MaxStackSize로 나눠 올린 값의 합이고, 겹칠 수 없는 도구는 1개당 1칸이다.
        /// </summary>
        private int CountUsedSlots()
        {
            int slots = 0;
            kindBuffer.Clear();
            kindCountBuffer.Clear();

            List<InventoryItem> items = State.items;
            for (int i = 0; i < items.Count; i++)
            {
                InventoryItem item = items[i];
                if (item == null || item.data == null)
                    continue;

                if (item.data.MaxStackSize <= 1)
                {
                    slots++;
                    continue;
                }

                int index = kindBuffer.IndexOf(item.data);
                if (index < 0)
                {
                    kindBuffer.Add(item.data);
                    kindCountBuffer.Add(1);
                }
                else
                {
                    kindCountBuffer[index]++;
                }
            }

            for (int i = 0; i < kindBuffer.Count; i++)
                slots += SlotsFor(kindCountBuffer[i], kindBuffer[i].MaxStackSize);

            return slots;
        }

        /// <summary>개수 count를 한 칸 max개짜리 스택으로 담을 때 필요한 칸 수(PlayerInventory와 같은 식).</summary>
        private static int SlotsFor(int count, int max)
        {
            if (count <= 0)
                return 0;
            if (max <= 1)
                return count;
            return (count + max - 1) / max;
        }
    }
}
