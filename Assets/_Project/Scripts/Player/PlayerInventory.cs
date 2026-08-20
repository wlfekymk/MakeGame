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
        /// 대형 섬은 처음부터 갈 수 있게 하고, 정말로 강한 해류가 필요한 특대 섬만 진행도 요건으로 막아 두면
        /// 잠금 없이 원래 의도한 난이도 곡선(대형→특대 순으로 더 강해지는 해류)이 유지된다.
        /// 그 진행도 요건은 이제 뗏목 "대양 규격 + 모터"다(IslandTravel.CurrentBypass.OceanReadyWithMotor) -
        /// 시작 섬에서 특대 섬이 19 km라 모터(6.0 m/s) 없이는 실제로 몰고 갈 수 없기 때문이다.
        /// 이 메서드는 그 요건을 알지 못한다 - 여기는 목적지 규모만 보고 막고, 뚫는 판정은 IslandTravel이 한다.
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
            return AddItemIgnoringCapacity(itemData, remainingUses, 0f);
        }

        /// <summary>
        /// [식량 루프] 위와 같지만 부패 경과 시간까지 지정해 넣는다(세이브 복원 전용).
        /// 옛 세이브에는 이 값이 없어 0(= 신선)이 들어오므로, 예전 세이브의 음식은 전부 갓 만든
        /// 상태로 되살아난다 - 기존 세이브 호환의 핵심이다.
        /// </summary>
        public InventoryItem AddItemIgnoringCapacity(ItemData itemData, int remainingUses, float spoilAgeSeconds)
        {
            if (itemData == null)
                return null;

            var item = new InventoryItem(itemData)
            {
                remainingUses = remainingUses,
                spoilAgeSeconds = Mathf.Max(0f, spoilAgeSeconds),
            };
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
                {
                    target.count++;

                    // [식량 루프] 한 칸의 신선도는 그 칸에서 **가장 오래된 것**을 따른다
                    // (근거는 InventoryStack.oldest 주석). 합칠 때마다 더 상한 쪽으로 갱신한다.
                    // 부패 대상이 아닌 아이템도 경과 시간은 전부 0이라 이 비교가 대표를 바꾸지 않는다.
                    if (target.oldest == null || item.spoilAgeSeconds > target.oldest.spoilAgeSeconds)
                        target.oldest = item;
                }
                else
                {
                    buffer.Add(new InventoryStack(item.data, 1, item));
                }
            }
        }

        /// <summary>
        /// 칸 순서를 바꾼다. UI가 지금 보고 있는 칸 목록을 **원하는 순서로 재배열해 통째로** 넘기면,
        /// items를 그 순서대로 다시 깐다. 인벤토리 창의 드래그 이동이 쓰는 유일한 통로다.
        ///
        /// 왜 "from/to 두 정수"가 아니라 목록을 통째로 받는가: 칸은 items에서 파생된 뷰라
        /// (<see cref="GetStacks(List{InventoryStack})"/>) 칸 번호만으로는 어느 인스턴스를 옮길지
        /// 정할 수 없다. 대표 인스턴스(representative)를 열쇠로 쓰면 그 모호함이 사라진다.
        ///
        /// 넘긴 목록에 없는 칸(필터로 가려진 것 등)은 **원래 순서를 지킨 채 뒤에 붙는다** - 필터를
        /// 켠 상태로 순서를 바꿔도 보이지 않던 물건이 사라지거나 뒤섞이지 않게 하려는 것이다.
        ///
        /// 개수/내구도/신선도는 하나도 건드리지 않는다. 바뀌는 것은 items의 나열 순서뿐이다.
        /// </summary>
        /// <returns>실제로 순서가 달라졌으면 true.</returns>
        public bool ApplyStackOrder(List<InventoryStack> orderedStacks)
        {
            if (orderedStacks == null || orderedStacks.Count == 0)
                return false;

            // 1) 지금 items를 GetStacks와 **같은 규칙**으로 묶는다. 규칙이 갈리면 대표 인스턴스가
            //    가리키는 칸이 달라져 엉뚱한 뭉치가 옮겨진다.
            var groups = new List<List<InventoryItem>>();

            for (int i = 0; i < items.Count; i++)
            {
                InventoryItem item = items[i];
                if (item == null || item.data == null)
                    continue;

                int max = item.data.MaxStackSize;
                if (max <= 1)
                {
                    groups.Add(new List<InventoryItem> { item });
                    continue;
                }

                List<InventoryItem> target = null;
                for (int g = groups.Count - 1; g >= 0; g--)
                {
                    if (groups[g][0].data != item.data)
                        continue;
                    if (groups[g].Count < max)
                        target = groups[g];
                    break;
                }

                if (target != null)
                    target.Add(item);
                else
                    groups.Add(new List<InventoryItem> { item });
            }

            if (groups.Count == 0)
                return false;

            // 2) 넘어온 순서대로 그룹을 꺼낸다. 대표가 없거나 이미 꺼낸 그룹이면 건너뛴다.
            var reordered = new List<List<InventoryItem>>(groups.Count);
            var taken = new bool[groups.Count];

            for (int i = 0; i < orderedStacks.Count; i++)
            {
                InventoryStack stack = orderedStacks[i];
                if (stack == null || stack.representative == null)
                    continue;

                for (int g = 0; g < groups.Count; g++)
                {
                    if (taken[g] || groups[g][0] != stack.representative)
                        continue;

                    taken[g] = true;
                    reordered.Add(groups[g]);
                    break;
                }
            }

            // 3) 목록에 없던 그룹은 원래 순서대로 뒤에 붙인다.
            for (int g = 0; g < groups.Count; g++)
            {
                if (!taken[g])
                    reordered.Add(groups[g]);
            }

            // 4) 실제로 달라졌는지 확인한다. 같으면 items를 다시 깔지 않는다
            //    (드래그를 놓는 곳마다 UI 전체 갱신이 도는 것을 막는다).
            bool changed = false;
            int cursor = 0;
            for (int g = 0; g < reordered.Count && !changed; g++)
            {
                var group = reordered[g];
                for (int m = 0; m < group.Count; m++, cursor++)
                {
                    if (cursor >= items.Count || !ReferenceEquals(items[cursor], group[m]))
                    {
                        changed = true;
                        break;
                    }
                }
            }

            if (!changed)
                return false;

            // 5) 새 순서로 items를 다시 깐다. data가 null인 유령 항목은 이 과정에서 함께 걷힌다.
            items.Clear();
            for (int g = 0; g < reordered.Count; g++)
                items.AddRange(reordered[g]);

            InventoryChanged?.Invoke();
            return true;
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
        /// [식량 루프] 지정한 종류 중 **가장 많이 상한(가장 오래된) 인스턴스**를 찾는다. 없으면 null.
        ///
        /// 왜 필요한가: 인벤토리 표시(InventoryStack.oldest)는 칸에서 가장 오래된 것을 보여주는데,
        /// 정작 먹을 때 목록 앞쪽의 신선한 것이 소모되면 화면과 실제가 갈린다. 섭취(ConsumptionSystem)가
        /// 이 메서드를 거쳐 항상 오래된 것부터 먹게 하면 세 곳(표시·섭취·세이브)이 같은 기준이 된다.
        /// 부패하지 않는 아이템은 경과 시간이 전부 0이라 목록에서 처음 찾은 것이 그대로 나온다
        /// (= 예전 FindItem과 같은 결과).
        /// </summary>
        public InventoryItem FindMostSpoiled(ItemData itemData)
        {
            if (itemData == null)
                return null;

            InventoryItem best = null;
            for (int i = 0; i < items.Count; i++)
            {
                InventoryItem item = items[i];
                if (item == null || item.data != itemData)
                    continue;

                if (best == null || item.spoilAgeSeconds > best.spoilAgeSeconds)
                    best = item;
            }

            return best;
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
