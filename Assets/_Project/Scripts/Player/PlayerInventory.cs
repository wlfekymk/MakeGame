using System.Collections.Generic;
using UnityEngine;
using MakeGame.Data;
using MakeGame.Systems;

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

        [Tooltip("현재 플레이어가 소지 중인 아이템 목록.\n" +
            "**표현은 예전 그대로 '1개 = 1항목'인 평면 리스트다.** 스택은 이 리스트를 바꾸지 않고" +
            " GetStacks()가 만들어 주는 파생 뷰이며(InventoryStack 주석 참고), 용량도 이 리스트를" +
            " 종류별로 묶어 계산한 '칸 수'로 판정한다. 항목 수(items.Count)는 칸 수가 아니라 개수다.")]
        public List<InventoryItem> items = new List<InventoryItem>();

        /// <summary>
        /// slotCapacity가 설정되지 않은(0 이하) 경우에 쓰는 코드 기본 칸 수.
        /// 씬의 PlayerInventory에는 이 키가 없으므로(SampleScene.unity, startingItemPool/items만 직렬화됨)
        /// 현재는 이 값이 유일한 소스다.
        ///
        /// **100칸(사용자 요청).** 예전 값은 30이었고 근거는 아래 계산이었다:
        /// · 한 종류가 가장 많이 쌓이는 경우가 야자잎이다. 노끈 1개 = 야자잎 3개(Recipe_노끈)이고,
        ///   쉼터 3단계까지 + 경비행기까지 필요한 노끈이 14개 수준이라 야자잎 42개 = 3칸(20개 스택).
        /// · 엔딩 비축은 비상식량 12 · 생수 12 · 연료 3 · 금속조각 6 · 노끈 4 · 엔진부품 2 = 6칸.
        /// · 도구는 겹쳐지지 않아 1개당 1칸이다(칼·물통·라이터·손도끼·창·파이어스타터 = 6칸).
        /// 즉 정상적인 진행은 20칸 안팎이라 30으로도 소프트락은 없었지만, 13종 자원을 상한까지 쓸어
        /// 담는 플레이에서는 계속 넘쳤다. 100칸이면 그 플레이까지 한 번에 들고 다닐 수 있다.
        ///
        /// **용량을 올릴 때 같이 봐야 하는 곳**(칸 수에 비례해 커지는 것들):
        /// · InventoryUI - 창 높이를 칸 수에 비례해 늘리던 방식이었다. 100칸이면 17줄 = 1200px이라
        ///   화면 밖으로 나가서, 7줄만 보이고 나머지는 스크롤하는 가상화 격자로 바꿨다(VirtualSlotGrid).
        /// · ChestUI의 소지품 쪽 격자 - 같은 가상화 격자를 쓰므로 SlotCapacity만 따라가면 된다.
        /// · SurvivalHudUI의 "가방 24/30" 칩 - 80% 비율로 판정하므로 값에 무관하게 그대로 맞는다.
        /// 비례하지 않는 것: 세이브(SaveData는 아이템 평면 목록이라 칸 수를 저장하지 않는다),
        /// CanCarryToIsland(해류 제약이라 칸 수와 무관), BuildingSystem의 FreeSlots 검사(상대값).
        /// </summary>
        public const int DefaultSlotCapacity = 100;

        [Tooltip("인벤토리 칸 수 상한. 0 이하이면 코드 기본값(DefaultSlotCapacity=100)을 쓴다.\n" +
            "한 칸에는 같은 종류를 ItemData.MaxStackSize개까지 겹쳐 담을 수 있고, 내구도가 있는 도구는" +
            " 1개당 1칸을 쓴다.")]
        public int slotCapacity = DefaultSlotCapacity;

        /// <summary>
        /// 인벤토리에 변화(추가/제거/복원)가 생겼을 때 발행된다. UI가 매 프레임 다시 그리지 않고
        /// 이 신호에만 반응해 갱신할 수 있도록 둔 것이다.
        /// </summary>
        public event System.Action InventoryChanged;

        /// <summary>
        /// 용량이 가득 차서 아이템을 받지 못했을 때, 거부된 아이템 종류와 함께 발행된다.
        /// **"채집이 소리도 텍스트도 없이 무시되는" 사고를 반복하지 않기 위한 통로다.**
        /// PlayerInventory 자체도 거부 시 실패음(PlayActionFail)과 경고 로그를 남기므로, 이 이벤트를
        /// 아무도 구독하지 않아도 무반응이 되지는 않는다. 화면 문구는 UI가 이 이벤트로 붙이면 된다.
        /// </summary>
        public event System.Action<ItemData> AddRejected;

        // 칸 수 계산용 재사용 버퍼. AddItem마다(즉 채집 1개마다) 호출되므로 매번 List를 새로 만들지 않는다.
        private readonly List<ItemData> kindBuffer = new List<ItemData>();
        private readonly List<int> kindCountBuffer = new List<int>();

        /// <summary>실제로 적용되는 칸 수 상한(slotCapacity가 0 이하이면 코드 기본값).</summary>
        public int SlotCapacity => slotCapacity > 0 ? slotCapacity : DefaultSlotCapacity;

        /// <summary>지금 쓰고 있는 칸 수. GetStacks()가 돌려주는 스택 개수와 항상 같다.</summary>
        public int UsedSlots => CountUsedSlots();

        /// <summary>남은 빈 칸 수(세이브 복원으로 상한을 넘긴 경우에도 음수가 되지 않는다).</summary>
        public int FreeSlots => Mathf.Max(0, SlotCapacity - UsedSlots);

        /// <summary>빈 칸이 없는지 여부. 단, 이미 담긴 스택에 빈자리가 있으면 그 종류는 여전히 받을 수 있다.</summary>
        public bool IsFull => UsedSlots >= SlotCapacity;

        /// <summary>
        /// 게임 시작 시 자동으로 시작 아이템 풀을 지급한다 (불시착 직후 상황 재현).
        /// </summary>
        private void Start()
        {
            GrantStartingLoadout();
        }

        /// <summary>
        /// 시작 아이템 풀에 있는 아이템들을 인벤토리에 지급한다. 불시착 직후 최초 1회 호출한다.
        /// 용량 검사를 거치지 않는다 - 시작 지급이 용량에 막히면 플레이어가 손쓸 방법이 없다.
        /// </summary>
        public void GrantStartingLoadout()
        {
            foreach (var itemData in startingItemPool)
            {
                AddItemIgnoringCapacity(itemData);
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
            {
                items.Remove(item);
                // 버그 수정: 내구도가 다 닳아 아이템이 인벤토리에서 조용히 사라져도 아무 피드백이 없어
                // 방금 손도끼/창/칼이 파손된 것을 플레이어가 알아채기 어려웠다. 파손 전용 효과음 재생.
                AudioManager.Instance?.PlayBreak();
                InventoryChanged?.Invoke();
            }
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
        /// **시그니처는 예전 그대로다**(호출부가 채집·제작·조리·사냥·증류기 등 전역에 흩어져 있다).
        /// 달라진 것은 칸이 모자라면 넣지 않고 거부한다는 점뿐이며, 거부는 조용히 일어나지 않는다
        /// (실패음 + AddRejected 이벤트 + 경고 로그). 성공 여부가 필요하면 TryAddItem을 써라.
        /// </summary>
        public void AddItem(ItemData itemData)
        {
            TryAddItem(itemData);
        }

        /// <summary>
        /// AddItem과 완전히 같은 동작을 하되 성공 여부를 돌려준다(오버로드가 아니라 별도 이름인 이유는,
        /// 반환형만 다른 오버로드는 C#에서 만들 수 없기 때문이다).
        /// 넣기 전에 미리 알아보려면 상태를 바꾸지 않는 CanAccept를 써라.
        /// </summary>
        /// <returns>실제로 넣었으면 true, 칸이 모자라거나 itemData가 null이면 false.</returns>
        public bool TryAddItem(ItemData itemData)
        {
            if (itemData == null)
                return false;

            if (!CanAccept(itemData, 1))
            {
                // 사유가 밖으로 나가야 한다. 소리는 ResourceNode.ReportHarvestFailure와 같은
                // PlayActionFail로 통일했다 - 플레이어가 새 소리를 외우게 만들 이유가 없고,
                // 여기서 소리가 맡는 역할은 "무반응이 아니다"를 알리는 것 하나다.
                AudioManager.Instance?.PlayActionFail();
                AddRejected?.Invoke(itemData);
                Debug.LogWarning($"[PlayerInventory] 인벤토리가 가득 차 '{itemData.itemName}'을(를) 넣지 못했다" +
                    $" ({UsedSlots}/{SlotCapacity}칸).");
                return false;
            }

            items.Add(new InventoryItem(itemData));
            InventoryChanged?.Invoke();
            return true;
        }

        /// <summary>
        /// 용량 검사를 건너뛰고 아이템을 넣는다. **시작 지급과 세이브 복원 전용이다.**
        /// 게임 플레이 중 획득 경로에서는 쓰지 마라 - 용량이 의미를 잃는다.
        /// 세이브 복원에서 검사하지 않는 이유: 상한을 넘긴 옛 세이브를 불러올 때 넘치는 만큼을 버리면
        /// 플레이어의 물건이 사라진다. 넘친 채로 복원하고(경고만 남긴다) 이후 획득만 막는 쪽이 안전하다.
        /// </summary>
        public InventoryItem AddItemIgnoringCapacity(ItemData itemData)
        {
            return AddItemIgnoringCapacity(itemData, itemData != null ? itemData.maxUses : 0);
        }

        /// <summary>
        /// 남은 사용 횟수를 지정해 용량 검사 없이 넣는다(세이브 복원 전용 - 내구도가 닳은 도구 복원).
        /// </summary>
        public InventoryItem AddItemIgnoringCapacity(ItemData itemData, int remainingUses)
        {
            if (itemData == null)
                return null;

            var item = new InventoryItem(itemData) { remainingUses = remainingUses };
            items.Add(item);
            InventoryChanged?.Invoke();
            return item;
        }

        /// <summary>
        /// 지금 이 아이템을 count개 더 받을 수 있는지 알려준다. 상태를 전혀 바꾸지 않으므로
        /// 조준 프롬프트가 매 프레임 호출해도 안전하다(소리도 나지 않는다).
        /// 이미 담긴 스택에 빈자리가 있으면 새 칸이 필요 없으므로, 칸이 가득 차 있어도 true일 수 있다.
        /// </summary>
        public bool CanAccept(ItemData itemData, int count = 1)
        {
            if (itemData == null || count <= 0)
                return false;

            int max = itemData.MaxStackSize;
            int need;
            if (max <= 1)
            {
                // 겹칠 수 없는 도구는 1개당 1칸이다.
                need = count;
            }
            else
            {
                int have = GetItemCount(itemData);
                need = SlotsFor(have + count, max) - SlotsFor(have, max);
            }

            return UsedSlots + need <= SlotCapacity;
        }

        /// <summary>
        /// 인벤토리를 칸 단위로 묶은 뷰를 만들어 돌려준다(UI 표시·세이브 압축용).
        /// 같은 종류는 MaxStackSize 단위로 나뉘므로, 야자잎 42개는 20/20/2 세 스택이 된다.
        /// 내구도가 있는 도구는 인스턴스마다 별도의 스택(count=1)이 되어 남은 내구도가 섞이지 않는다.
        /// 순서는 items에 처음 나타난 순서를 따른다(호출할 때마다 뒤바뀌지 않는다).
        /// </summary>
        public List<InventoryStack> GetStacks()
        {
            var result = new List<InventoryStack>();
            GetStacks(result);
            return result;
        }

        /// <summary>
        /// GetStacks()와 같지만 호출부가 넘긴 버퍼를 재사용한다(매 프레임 갱신하는 UI용 - 리스트 재할당 없음).
        /// 버퍼는 내부에서 Clear된다.
        /// </summary>
        public void GetStacks(List<InventoryStack> buffer)
        {
            if (buffer == null)
                return;

            buffer.Clear();

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

                // 같은 종류의 마지막 스택에 자리가 남아 있으면 거기에 채운다.
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
        /// items를 직접 건드린 코드(세이브 복원 등)가 UI에 변화를 알리기 위한 통로.
        /// 평소에는 AddItem/RemoveItems/UseItem이 알아서 발행하므로 부를 필요가 없다.
        /// </summary>
        public void NotifyInventoryChanged()
        {
            InventoryChanged?.Invoke();
        }

        /// <summary>
        /// 지금 쓰고 있는 칸 수를 센다. 종류별로 개수를 모은 뒤 MaxStackSize로 나눠 올린 값의 합이며,
        /// 겹칠 수 없는 도구는 1개당 1칸이다. GetStacks().Count와 항상 같은 값이 나온다.
        /// </summary>
        private int CountUsedSlots()
        {
            return CountUsedSlots(items, kindBuffer, kindCountBuffer);
        }

        /// <summary>
        /// [B5] 위 계산의 공용 구현. StorageChest.CountUsedSlots가 같은 식을 복붙하고 있었어서
        /// (표현이 둘 다 '1개 = 1항목' 평면 리스트라 식이 완전히 같다) 여기로 합쳤다.
        /// 버퍼 두 개는 호출마다 List를 새로 만들지 않기 위한 재사용 스크래치로, Clear 후 채워 쓴다.
        /// </summary>
        internal static int CountUsedSlots(List<InventoryItem> items, List<ItemData> kindBuffer, List<int> kindCountBuffer)
        {
            int slots = 0;
            kindBuffer.Clear();
            kindCountBuffer.Clear();

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

        /// <summary>
        /// 개수 count를 한 칸 max개짜리 스택으로 담을 때 필요한 칸 수(정수 올림).
        /// [B5] StorageChest/BuildingSystem이 같은 식을 복붙하고 있었어서 여기 것을 공용으로 승격했다.
        /// </summary>
        internal static int SlotsFor(int count, int max)
        {
            if (count <= 0)
                return 0;
            if (max <= 1)
                return count;
            return (count + max - 1) / max;
        }

        /// <summary>
        /// 특정 인스턴스 하나를 목록에서 제거한다. **어느 것을 지울지 호출자가 고를 수 있는 유일한 경로다.**
        /// [B18] RemoveItems(data, 1)은 목록 끝을 지우므로, UI에서 "12/20 남은 손도끼"를 버리려 해도
        /// 새 손도끼(20/20)가 사라진다. UI가 items를 직접 건드리지 않아도 되게 열어 둔다.
        /// </summary>
        /// <returns>실제로 제거했으면 true.</returns>
        public bool RemoveItem(InventoryItem item)
        {
            if (item == null || !items.Remove(item))
                return false;

            NotifyInventoryChanged();
            return true;
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

            if (removed > 0)
                InventoryChanged?.Invoke();

            return true;
        }
    }
}
