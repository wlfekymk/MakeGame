using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using MakeGame.Data;
using MakeGame.Player;

namespace MakeGame.UI
{
    /// <summary>
    /// 인벤토리 UI. Tab 키로 열고 닫으며, 현재 소지품을 **칸(스택) 단위**로 화면 왼쪽에 표시한다.
    /// 씬에 미리 배치하지 않고 Start()에서 UIBuilder로 캔버스/패널/목록을 직접 생성한다.
    ///
    /// 스택 뷰(정착 배치 3): 예전에는 PlayerInventory.items("1개 = 1항목" 평면 리스트)를 그대로 훑어
    /// 종류별 개수를 직접 세서 한 줄로 합쳤다. 그 방식은 (1) 칸 상한을 표현할 수 없고(야자잎 42개가
    /// "x42" 한 줄로 보여 실제로 3칸을 먹고 있다는 사실이 화면에 없다), (2) 내구도가 서로 다른 도구
    /// 여러 개를 한 줄로 뭉개 "가장 적게 남은 값"만 보여줬다. 이제 PlayerInventory.GetStacks(buffer)가
    /// 돌려주는 칸 뷰를 그대로 한 줄씩 그린다 - 화면의 줄 수 = 실제로 쓰고 있는 칸 수다.
    /// </summary>
    public class InventoryUI : MonoBehaviour
    {
        [Tooltip("표시할 대상 인벤토리")]
        public PlayerInventory inventory;

        [Tooltip("인벤토리 창을 여닫는 키")]
        public KeyCode toggleKey = KeyCode.Tab;

        [Tooltip("인벤토리가 열려 있을 때 카테고리 필터를 순환시키는 키")]
        public KeyCode cycleFilterKey = KeyCode.F;

        [Tooltip("한 칸 전부를 버릴 때 버리기 버튼과 함께 누르는 키")]
        public KeyCode dropWholeStackModifier = KeyCode.LeftShift;

        private static readonly string[] CategoryFilterNames =
        {
            "전체", "무기", "치료", "음식", "음료", "설치형", "이동수단", "재료"
        };

        /// <summary>
        /// 목록 갱신 주기(초). **매 프레임 갱신하지 않는 이유와, 그렇다고 이벤트만 쓰지 않는 이유**:
        ///
        /// · 매 프레임 GetStacks(buffer)를 부르면 List 재할당은 없지만 InventoryStack은 class라
        ///   (PlayerInventory.GetStacks에서 스택마다 new) 칸 수만큼 새 객체가 매 프레임 쌓인다.
        ///   20~30칸이면 초당 1200~1800개다 - "GC 없음"이 아니다.
        /// · 그렇다고 InventoryChanged 이벤트만 구독하면 **내구도 표시가 굳는다**. PlayerInventory.UseItem은
        ///   아이템이 완전히 소진된 순간에만 InventoryChanged를 발행하고(PlayerInventory.cs UseItem),
        ///   remainingUses가 20→19로 줄어드는 평범한 사용에는 이벤트가 없다. 창을 열어둔 채 공격/채집하면
        ///   "20/20회 남음"이 그대로 멈춰 있게 된다.
        ///
        /// 그래서 이벤트(추가/제거/버리기 - 즉시 반응)와 저주파 폴링(내구도 - 0.2초 안에 따라잡음)을
        /// 함께 쓴다. 폴링은 창이 열려 있는 동안에만 돈다.
        /// </summary>
        private const float RefreshInterval = 0.2f;

        /// <summary>버리기 버튼을 무장한 뒤 이 시간(초) 안에 다시 누르지 않으면 확인이 취소된다.</summary>
        private const float DropConfirmWindow = 3f;

        // 필터 인덱스 0은 "전체"를 뜻하고, 1부터는 ItemCategory 값 + 1에 대응한다.
        private int currentFilterIndex = 0;

        // 성능 개선(#8): 제목 텍스트는 currentFilterIndex가 바뀔 때만 실제로 달라지므로, 마지막으로
        // 반영한 필터 인덱스를 기억해두고 그 값이 그대로면 UpdateTitle()에서 문자열을 다시 만들지 않는다.
        private int lastDisplayedFilterIndex = -1;

        // 용량 줄(사용 칸/전체 칸)도 값이 실제로 바뀐 갱신에서만 문자열을 다시 만든다.
        private int lastDisplayedUsedSlots = -1;
        private int lastDisplayedSlotCapacity = -1;

        private float refreshTimer = 0f;

        /// <summary>
        /// 칸 하나(스택 하나)를 표시하는 한 묶음. 구성:
        /// (1) 카테고리 헤더 텍스트 - 그 카테고리의 첫 줄에서만 보인다(도구·재료 사이 구분선 역할),
        /// (2) 카테고리 색 띠 + 아이콘 + 이름/개수(+도구는 내구도 막대) + 설명 + 사용법 힌트 + 버리기 버튼.
        /// </summary>
        private class ItemRow
        {
            public GameObject rowGo;          // 헤더 + 항목 줄을 함께 담는 바깥 컨테이너
            public LayoutElement entryLayout; // 헤더 표시 여부에 따라 높이를 늘렸다 줄인다
            public Text categoryHeader;
            public Image categoryStrip;       // 왼쪽 세로 색 띠(무기=#CC3333 등 카테고리 색)
            public Image icon;
            public Text letterLabel;
            public Text nameCountLabel;
            public Text descLabel;
            public Text usageLabel;           // "[C] 섭취" 처럼 지금 이 아이템을 어떻게 쓰는지
            public GameObject duraBarGo;      // 내구도 막대(스택되지 않는 도구 줄에서만 보인다)
            public Image duraFill;
            public Button dropButton;
            public Text dropLabel;

            // 카테고리 헤더는 이웃한 행과의 관계(정렬 순서)로 결정되므로 아이템 캐시와 별도로 기억한다.
            public string cachedHeaderText = null;

            // 성능 개선(#8): 이 행이 마지막으로 문자열을 다시 만들었을 때의 표시 대상 값들을 캐시해둔다.
            // 같은 위치(row)가 같은 칸(같은 종류·같은 개수·같은 잔여 사용횟수)을 표시하는 경우에는
            // 문자열 보간을 다시 하지 않고 건너뛴다.
            public ItemData cachedData;
            public int cachedCount = -1;
            public int cachedRemaining = int.MinValue;

            // 이 줄이 지금 가리키고 있는 실제 인스턴스. 내구도가 제각각인 도구를 "이 줄에 보이는 그것"으로
            // 정확히 버리기 위해 필요하다(같은 손도끼라도 12/20짜리와 20/20짜리는 다른 인스턴스다).
            public InventoryItem representative;

            // 버리기 확인 대기 상태. 무장한 대상(데이터/인스턴스)이 바뀌면 즉시 해제한다 - 목록이 다시
            // 정렬돼 같은 자리에 다른 물건이 들어왔는데 "확인" 클릭이 그대로 먹히면 그게 바로 오폭이다.
            public ItemData pendingData;
            public InventoryItem pendingRepresentative;
            public bool pendingWhole;
            public float pendingUntil = -1f;
            public bool dropLabelArmed = false;
        }

        // 사용법 힌트에 쓰는 실제 키. 예전에는 "[C] 섭취"처럼 문자열에 키가 박혀 있었는데, 실제 키는
        // InteractionController가 정하고 씬에서 바뀔 수 있다(이 프로젝트에서 코드/씬 값이 갈라지는 것이
        // 사고의 유일한 원인이다 - AGENT_BRIEF 0장). Start()에서 실제 필드를 읽어 캐시한다.
        private KeyCode interactKey = KeyCode.E;
        private KeyCode cookKey = KeyCode.R;
        private KeyCode consumeKey = KeyCode.C;
        private KeyCode placeKey = KeyCode.G;

        private GameObject panelRoot;
        private RectTransform listContainer;
        private Text titleLabel;
        private Text capacityLabel;
        private readonly List<ItemRow> rowPool = new List<ItemRow>();

        // 칸 뷰 버퍼. PlayerInventory.GetStacks(buffer)가 내부에서 Clear하고 다시 채운다.
        private readonly List<InventoryStack> stackBuffer = new List<InventoryStack>();
        // 필터/정렬을 거친 표시용 목록(원본 순서를 망가뜨리지 않도록 따로 둔다).
        private readonly List<InventoryStack> displayBuffer = new List<InventoryStack>();

        // 캡처가 없는 정적 람다는 컴파일러가 한 번만 만들어 캐시하므로 정렬마다 델리게이트가 새로 생기지 않는다.
        private static readonly Comparison<InventoryStack> StackOrder = (a, b) =>
        {
            int categoryCompare = GetCategory(a.data).CompareTo(GetCategory(b.data));
            if (categoryCompare != 0)
                return categoryCompare;

            int nameCompare = string.Compare(a.data.itemName, b.data.itemName, StringComparison.CurrentCulture);
            if (nameCompare != 0)
                return nameCompare;

            // 같은 종류의 칸이 여러 개일 때(야자잎 20/20/2) 가득 찬 칸을 위로 모아 순서가 흔들리지 않게 한다.
            // List.Sort는 불안정 정렬이라 동점을 남겨두면 갱신마다 줄 순서가 뒤바뀔 수 있다.
            return b.count.CompareTo(a.count);
        };

        // 색: 새로 만들지 않고 이미 이 파일/프로젝트에서 쓰던 값을 그대로 쓴다(ArtDirection.md 1장).
        private static readonly Color BodyGray = new Color(0.75f, 0.75f, 0.75f, 1f);
        private static readonly Color CautionAmber = new Color(1f, 0.85f, 0.3f, 1f);
        private static readonly Color DangerRed = new Color(0.8f, 0.2f, 0.2f, 1f);
        private static readonly Color CriticalText = new Color(1f, 0.35f, 0.3f, 1f);

        /// <summary>
        /// 시작 시 인벤토리 UI 계층을 생성하고 기본적으로 닫힌 상태로 둔다.
        /// </summary>
        private void Start()
        {
            var interaction = FindAnyObjectByType<MakeGame.Systems.InteractionController>();
            if (interaction != null)
            {
                interactKey = interaction.interactKey;
                cookKey = interaction.cookKey;
                consumeKey = interaction.consumeKey;
                placeKey = interaction.placeKey;
            }

            // 씬에서 연결돼 있으면 그 값이 이긴다. null일 때만 찾아 채운다(CraftingUI와 같은 방식).
            if (inventory == null)
                inventory = FindAnyObjectByType<PlayerInventory>();

            if (inventory != null)
                inventory.InventoryChanged += OnInventoryChanged;

            BuildUI();
            SetOpen(false);
        }

        /// <summary>
        /// 구독한 이벤트를 반드시 해제한다. 씬 재로드 시 죽은 UI가 이벤트에 남아 있으면
        /// 다음 인벤토리 변화에서 파괴된 오브젝트를 건드리게 된다.
        /// </summary>
        private void OnDestroy()
        {
            if (inventory != null)
                inventory.InventoryChanged -= OnInventoryChanged;
        }

        /// <summary>
        /// 인벤토리에 실제 변화(추가/제거/버리기/복원)가 생긴 순간 목록을 즉시 다시 그린다.
        /// 창이 닫혀 있으면 아무 일도 하지 않는다.
        /// </summary>
        private void OnInventoryChanged()
        {
            if (panelRoot != null && panelRoot.activeSelf)
                RefreshList();
        }

        /// <summary>
        /// 매 프레임 토글 입력을 감지하고, 창이 열려 있으면 목록을 저주파로 갱신한다(RefreshInterval 참고).
        /// </summary>
        private void Update()
        {
            if (Input.GetKeyDown(toggleKey))
                SetOpen(!panelRoot.activeSelf);

            if (panelRoot != null && panelRoot.activeSelf)
            {
                // 인벤토리가 열려 있을 때만 필터 순환 키를 받는다 (닫혀 있을 때 실수로 필터가 바뀌는 것을 방지).
                if (Input.GetKeyDown(cycleFilterKey))
                {
                    CycleFilter();
                    RefreshList();
                }

                // Time.timeScale이 0인 화면 위에서도 멈추지 않도록 unscaled를 쓴다(프로젝트 공통 규칙).
                refreshTimer -= Time.unscaledDeltaTime;
                if (refreshTimer <= 0f)
                {
                    refreshTimer = RefreshInterval;
                    RefreshList();
                }
            }
        }

        /// <summary>
        /// 카테고리 필터를 다음 순서로 넘긴다 (전체 → 무기 → 치료 → 음식 → 음료 → 설치형 → 이동수단 → 재료 → 전체...).
        /// </summary>
        private void CycleFilter()
        {
            currentFilterIndex = (currentFilterIndex + 1) % CategoryFilterNames.Length;
        }

        /// <summary>
        /// 제목 텍스트에 현재 적용 중인 카테고리 필터를 함께 표시한다 (예: "인벤토리 (Tab)  [필터: 무기, F로 전환]").
        /// 성능 개선(#8): 필터가 실제로 바뀐 갱신에서만 문자열을 새로 만든다.
        /// </summary>
        private void UpdateTitle()
        {
            if (titleLabel == null)
                return;

            if (currentFilterIndex == lastDisplayedFilterIndex)
                return;

            // 키는 이 컴포넌트가 직접 들고 있는 값(씬에서 바뀔 수 있다)을 쓴다. 창 제목처럼 명사에
            // 붙는 자리는 "이름 (키)" 표기다(지금 눌러야 할 동작을 가리키는 "[키] 동작"과 구분).
            titleLabel.text = $"인벤토리 ({toggleKey})  [필터: {CategoryFilterNames[currentFilterIndex]}, {cycleFilterKey}로 전환]";
            lastDisplayedFilterIndex = currentFilterIndex;
        }

        /// <summary>
        /// 사용 칸/전체 칸을 제목 아래 한 줄로 보여준다. **가득 차기 전에** 읽혀야 의미가 있으므로
        /// 상시 표시하고, 80% 이상이면 노란색, 꽉 차면 Danger Red로 바꿔 한눈에 잡히게 한다.
        /// (필터를 걸어도 이 값은 인벤토리 전체 기준이다 - 필터는 보기일 뿐 용량과 무관하다.)
        /// </summary>
        private void UpdateCapacity()
        {
            if (capacityLabel == null || inventory == null)
                return;

            int used = inventory.UsedSlots;
            int capacity = inventory.SlotCapacity;
            if (used == lastDisplayedUsedSlots && capacity == lastDisplayedSlotCapacity)
                return;

            capacityLabel.text = $"칸 {used}/{capacity}" + (used >= capacity ? "  (가득 참)" : "");
            capacityLabel.color = used >= capacity ? DangerRed
                : (capacity > 0 && (float)used / capacity >= 0.8f ? CautionAmber : BodyGray);

            lastDisplayedUsedSlots = used;
            lastDisplayedSlotCapacity = capacity;
        }

        /// <summary>
        /// 아이템 하나의 분류 카테고리를 판정한다. 정렬 순서와 필터링에 함께 사용한다.
        /// 판정은 단일 소스인 UIBuilder.GetItemCategory에 위임하고, 이 메서드는 얇게 감싸기만 한다.
        /// </summary>
        private static UIBuilder.ItemCategory GetCategory(ItemData item)
        {
            return UIBuilder.GetItemCategory(item);
        }

        /// <summary>
        /// 이 아이템을 버릴 때 확인 절차를 요구해야 하는지 판정한다. **되돌릴 방법이 없는 손실만** 막는다.
        /// · maxUses != 1 → 도구/장비다. 무제한(칼·물통·고무보트·파이어스타터, maxUses -1)과 내구도
        ///   도구(창 15·손도끼 20·라이터 5)가 여기 해당한다. 잃으면 다시 제작하거나 영영 못 구한다.
        /// · isPlaceable → 키트(쉼터·모닥불·물증류기). 재료를 모아 만든 결과물이라 손실이 크다.
        /// · 한 칸 통째로 버리기(whole) → 개수가 큰 만큼 오폭의 대가도 크다.
        /// 나머지(maxUses == 1인 재료·음식·치료제 1개)는 다시 주우면 되므로 한 번 클릭으로 즉시 버린다 -
        /// 모든 버리기에 확인을 붙이면 야자잎 20개 정리에 40번을 클릭하게 되고, 그러면 아무도 안 쓴다.
        /// </summary>
        private static bool RequiresDropConfirm(ItemData data, bool whole)
        {
            if (data == null)
                return true;

            return whole || data.maxUses != 1 || data.isPlaceable;
        }

        /// <summary>
        /// 캔버스와 배경 패널, 아이템 목록을 담을 세로 레이아웃 컨테이너를 생성한다.
        /// </summary>
        private void BuildUI()
        {
            var canvas = UIBuilder.CreateCanvas("InventoryCanvas", sortOrder: 10);

            // 개선(B4-14, ArtDirection.md 4.3): 카드형 패널임을 알려주는 상단 테두리(2px, 흰색 알파 12%)를 추가.
            // 폭을 320 → 360으로 넓혔다: 한 줄에 [색 띠·아이콘·이름/개수·사용법·버리기 버튼]이 들어가야
            // 하는데 기존 폭에서는 "물증류기키트" 같은 긴 이름이 두 줄로 접혀 줄 높이를 넘겼다.
            var panel = UIBuilder.CreatePanel(
                canvas.transform, "InventoryPanel",
                anchorMin: new Vector2(0f, 0.3f), anchorMax: new Vector2(0f, 1f),
                offsetMin: new Vector2(20f, -20f), offsetMax: new Vector2(380f, -20f),
                color: new Color(0f, 0f, 0f, 0.75f),
                addTopBorder: true);

            panelRoot = panel.gameObject;

            var title = UIBuilder.CreateText(panel, "Title", $"인벤토리 ({toggleKey})", 20, Color.white, TextAnchor.UpperLeft);
            title.rectTransform.anchorMin = new Vector2(0f, 1f);
            title.rectTransform.anchorMax = new Vector2(1f, 1f);
            title.rectTransform.pivot = new Vector2(0.5f, 1f);
            title.rectTransform.anchoredPosition = new Vector2(0f, -8f);
            title.rectTransform.sizeDelta = new Vector2(0f, 28f);
            titleLabel = title;

            // 용량 줄(제목 바로 아래). 폰트 12 = ArtDirection.md 4.3 Body 등급.
            capacityLabel = UIBuilder.CreateText(panel, "Capacity", "", 12, BodyGray, TextAnchor.UpperLeft);
            capacityLabel.rectTransform.anchorMin = new Vector2(0f, 1f);
            capacityLabel.rectTransform.anchorMax = new Vector2(1f, 1f);
            capacityLabel.rectTransform.pivot = new Vector2(0.5f, 1f);
            capacityLabel.rectTransform.anchoredPosition = new Vector2(0f, -36f);
            capacityLabel.rectTransform.sizeDelta = new Vector2(-20f, 16f);

            // 버리기 조작 안내. 버튼만 있고 "한 칸 전부"를 알릴 방법이 없으면 아무도 그 기능을 못 찾는다.
            var dropHint = UIBuilder.CreateText(panel, "DropHint",
                $"버리기: 클릭=1개 · {dropWholeStackModifier}+클릭=한 칸",
                11, new Color(0.6f, 0.6f, 0.6f, 1f), TextAnchor.UpperLeft);
            dropHint.horizontalOverflow = HorizontalWrapMode.Overflow;
            dropHint.rectTransform.anchorMin = new Vector2(0f, 1f);
            dropHint.rectTransform.anchorMax = new Vector2(1f, 1f);
            dropHint.rectTransform.pivot = new Vector2(0.5f, 1f);
            dropHint.rectTransform.anchoredPosition = new Vector2(0f, -54f);
            dropHint.rectTransform.sizeDelta = new Vector2(-20f, 14f);

            var listGo = new GameObject("ItemList", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            listGo.transform.SetParent(panel, false);
            listContainer = listGo.GetComponent<RectTransform>();
            listContainer.anchorMin = new Vector2(0f, 0f);
            listContainer.anchorMax = new Vector2(1f, 1f);
            listContainer.offsetMin = new Vector2(10f, 10f);
            listContainer.offsetMax = new Vector2(-10f, -72f);

            var vlg = listGo.GetComponent<VerticalLayoutGroup>();
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            vlg.spacing = 4f;
            vlg.childAlignment = TextAnchor.UpperLeft;

            var fitter = listGo.GetComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        }

        /// <summary>
        /// 패널을 열거나 닫는다. 여는 순간 곧바로 한 번 그려서 첫 프레임에 빈 목록이 보이지 않게 한다.
        /// </summary>
        private void SetOpen(bool open)
        {
            if (panelRoot == null)
                return;

            panelRoot.SetActive(open);

            if (!open)
            {
                // 창을 닫는 순간 무장된 확인은 전부 취소한다. 다음에 열었을 때 남아 있던 "확실?"이
                // 그대로 눌리는 상황을 만들지 않는다.
                for (int i = 0; i < rowPool.Count; i++)
                    ClearPendingDrop(rowPool[i]);
                return;
            }

            refreshTimer = RefreshInterval;
            RefreshList();
        }

        /// <summary>
        /// 인벤토리를 **칸 단위**로 목록에 표시한다. 매 갱신마다 오브젝트를 새로 만들지 않고 행 풀을 재사용한다.
        /// </summary>
        private void RefreshList()
        {
            if (inventory == null || listContainer == null)
                return;

            inventory.GetStacks(stackBuffer);

            // 필터 인덱스 0은 "전체"이고, 1 이상이면 해당 카테고리(인덱스-1)만 통과시킨다.
            bool filterActive = currentFilterIndex > 0;
            UIBuilder.ItemCategory activeCategory = filterActive ? (UIBuilder.ItemCategory)(currentFilterIndex - 1) : default;

            displayBuffer.Clear();
            for (int i = 0; i < stackBuffer.Count; i++)
            {
                var stack = stackBuffer[i];
                if (stack.data == null)
                    continue;

                if (filterActive && GetCategory(stack.data) != activeCategory)
                    continue;

                displayBuffer.Add(stack);
            }

            // 카테고리별로 묶이도록 정렬하고, 같은 카테고리 안에서는 이름순으로 정렬해 항목을 찾기 쉽게 한다.
            displayBuffer.Sort(StackOrder);

            UpdateTitle();
            UpdateCapacity();

            int rowsNeeded = Mathf.Max(displayBuffer.Count, 1);
            EnsureRowCount(rowsNeeded);

            if (displayBuffer.Count == 0)
            {
                var empty = rowPool[0];
                ClearPendingDrop(empty);
                empty.icon.gameObject.SetActive(false);
                empty.nameCountLabel.text = filterActive ? "(이 카테고리에 해당하는 아이템 없음)" : "(비어 있음)";
                empty.nameCountLabel.color = new Color(0.7f, 0.7f, 0.7f, 1f);
                empty.descLabel.text = "";
                empty.usageLabel.text = "";
                empty.duraBarGo.SetActive(false);
                empty.dropButton.gameObject.SetActive(false);
                empty.categoryStrip.color = Color.clear;
                empty.categoryHeader.gameObject.SetActive(false);
                empty.cachedHeaderText = null;
                empty.entryLayout.minHeight = 48f;
                empty.rowGo.SetActive(true);
                // 이 분기는 캐시를 거치지 않고 rowPool[0]에 직접 "비어 있음" 문구를 써버리므로,
                // 캐시된 값(cachedData 등)을 그대로 두면 다음에 다시 실제 아이템이 이 행에 들어왔을 때
                // "이전에 봤던 값과 같다"고 오판해 갱신을 건너뛰는 버그가 생긴다. 그래서 여기서 캐시를
                // 강제로 무효화해, 다음 번엔 반드시 실제 아이템 표시 로직이 다시 실행되게 한다.
                empty.cachedData = null;
                empty.cachedCount = -1;
                empty.cachedRemaining = int.MinValue;
                empty.representative = null;
                for (int i = 1; i < rowPool.Count; i++)
                {
                    ClearPendingDrop(rowPool[i]);
                    rowPool[i].rowGo.SetActive(false);
                }
                return;
            }

            for (int i = 0; i < displayBuffer.Count; i++)
            {
                var stack = displayBuffer[i];
                var data = stack.data;
                var row = rowPool[i];
                int count = stack.count;
                int remaining = stack.RemainingUses;

                // 성능 개선(#8): 이 행이 지난 갱신과 같은 칸(종류/개수/잔여 사용횟수)을 표시하는 중이면
                // 화면에 보이는 결과가 동일하므로 문자열을 다시 만들지 않고 건너뛴다.
                bool needsRefresh = row.cachedData != data || row.cachedCount != count || row.cachedRemaining != remaining;

                row.representative = stack.representative;

                // 이 줄이 가리키는 대상이 바뀌었으면(정렬 변경·소모·버리기) 무장된 확인은 즉시 해제한다.
                if (row.pendingUntil > 0f && (row.pendingData != data || row.pendingRepresentative != stack.representative))
                    ClearPendingDrop(row);

                // 카테고리 헤더: 목록이 카테고리순으로 정렬돼 있으므로, 바로 위 행과 카테고리가 다른
                // 지점(그리고 첫 행)에서만 헤더를 보여준다.
                UIBuilder.ItemCategory category = GetCategory(data);
                bool showHeader = i == 0 || GetCategory(displayBuffer[i - 1].data) != category;
                string headerText = showHeader ? GetCategoryDisplayName(category) : null;
                if (row.cachedHeaderText != headerText)
                {
                    row.categoryHeader.gameObject.SetActive(showHeader);
                    if (showHeader)
                        row.categoryHeader.text = headerText;
                    row.entryLayout.minHeight = showHeader ? 66f : 48f;
                    row.cachedHeaderText = headerText;
                }

                if (needsRefresh)
                    ApplyStackToRow(row, data, count, remaining);

                UpdateDropButton(row);

                row.rowGo.SetActive(true);
            }

            for (int i = displayBuffer.Count; i < rowPool.Count; i++)
            {
                ClearPendingDrop(rowPool[i]);
                rowPool[i].rowGo.SetActive(false);
            }
        }

        /// <summary>
        /// 칸 하나의 내용을 한 줄에 그린다. 스택된 칸과 스택되지 않는 도구를 **다른 형식**으로 쓴다:
        /// · 스택 칸: "야자잎  x20/20" - 분모가 항상 붙어 있어 이 칸이 상한에 걸렸다는 사실이 그대로 읽힌다.
        /// · 도구(maxUses > 1: 창 15·손도끼 20·라이터 5): 개수 표기가 아예 없고 "12/20회 남음" + 이름 아래
        ///   내구도 막대가 뜬다. 개수가 아니라 막대가 붙어 있다는 것 자체가 "이건 겹쳐지지 않는 물건"이라는 신호다.
        /// </summary>
        private void ApplyStackToRow(ItemRow row, ItemData data, int count, int remaining)
        {
            row.icon.gameObject.SetActive(true);
            // 아이콘 스프라이트 유무와 무관하게 왼쪽 색 띠는 항상 카테고리 색을 유지한다
            // (무기는 Danger Red #CC3333 - ArtDirection.md 1.1).
            row.categoryStrip.color = UIBuilder.GetItemCategoryColor(data);
            row.usageLabel.text = GetUsageHint(data);

            // 아이템별 아이콘 스프라이트가 있으면 실제 그림을 보여주고, 없으면 기존처럼
            // 카테고리 색상 배경 + 이름 첫 글자 placeholder로 대체 표시한다(하위 호환).
            if (data.icon != null)
            {
                row.icon.sprite = data.icon;
                row.icon.color = Color.white;
                row.icon.type = Image.Type.Simple;
                row.icon.preserveAspect = true;
                if (row.letterLabel != null)
                    row.letterLabel.gameObject.SetActive(false);
            }
            else
            {
                row.icon.sprite = null;
                row.icon.color = UIBuilder.GetItemCategoryColor(data);
                if (row.letterLabel != null)
                {
                    row.letterLabel.gameObject.SetActive(true);
                    row.letterLabel.text = string.IsNullOrEmpty(data.itemName) ? "?" : data.itemName.Substring(0, 1);
                }
            }

            int maxStack = data.MaxStackSize;
            bool isDurableTool = maxStack <= 1 && !data.IsUnlimited && data.maxUses > 1;

            if (isDurableTool)
            {
                float remainRatio = data.maxUses > 0 ? Mathf.Clamp01((float)remaining / data.maxUses) : 0f;
                Color wearColor = remainRatio <= 0.2f ? CriticalText
                    : remainRatio <= 0.4f ? CautionAmber
                    : Color.white;

                row.nameCountLabel.text = $"{data.itemName}  {remaining}/{data.maxUses}회 남음";
                row.nameCountLabel.color = wearColor;

                row.duraBarGo.SetActive(true);
                row.duraFill.fillAmount = remainRatio;
                row.duraFill.color = wearColor;
            }
            else
            {
                // 스택 칸. 분모(칸 상한)를 항상 함께 적어 "이 칸은 더 안 들어간다"가 색 없이도 읽히게 한다.
                row.nameCountLabel.text = maxStack > 1
                    ? $"{data.itemName}  x{count}/{maxStack}"
                    : $"{data.itemName}  x{count}";
                row.nameCountLabel.color = Color.white;
                row.duraBarGo.SetActive(false);
            }

            // ItemData.description을 이름/개수 아래 작은 회색 글씨로 함께 보여준다.
            row.descLabel.text = string.IsNullOrEmpty(data.description) ? "" : data.description;

            row.cachedData = data;
            row.cachedCount = count;
            row.cachedRemaining = remaining;
        }

        /// <summary>
        /// 버리기 버튼의 라벨/색을 지금 상태(평상시 / 확인 대기)에 맞춘다. 확인 대기는 DropConfirmWindow가
        /// 지나면 저절로 풀리며, 갱신 주기(0.2초) 안에 라벨이 원래대로 돌아온다.
        /// </summary>
        private void UpdateDropButton(ItemRow row)
        {
            row.dropButton.gameObject.SetActive(true);

            bool armed = row.pendingUntil > 0f && Time.unscaledTime <= row.pendingUntil;
            if (!armed && row.pendingUntil > 0f)
                ClearPendingDrop(row);

            if (armed == row.dropLabelArmed)
                return;

            row.dropLabelArmed = armed;
            if (row.dropLabel != null)
            {
                row.dropLabel.text = armed ? "확실?" : "버리기";
                row.dropLabel.color = armed ? CautionAmber : Color.white;
            }
            var image = row.dropButton.GetComponent<Image>();
            if (image != null)
            {
                // 무장 상태에서는 버튼 자체를 Danger Red로 바꿔, 다음 클릭이 되돌릴 수 없는 행동임을 알린다.
                image.color = armed ? DangerRed : new Color(0.25f, 0.55f, 0.3f, 1f);
            }
        }

        /// <summary>버리기 확인 대기 상태를 해제한다(무장 취소).</summary>
        private void ClearPendingDrop(ItemRow row)
        {
            row.pendingData = null;
            row.pendingRepresentative = null;
            row.pendingWhole = false;
            row.pendingUntil = -1f;
        }

        /// <summary>
        /// 버리기 버튼을 눌렀을 때. Shift를 함께 누르면 그 칸 전부, 아니면 1개다.
        /// 되돌릴 수 없는 손실(도구/키트/한 칸 전부)은 곧바로 버리지 않고 한 번 더 누르게 한다
        /// (RequiresDropConfirm 참고) - 이 게임에는 버린 물건을 되찾을 수단이 없다.
        /// </summary>
        private void OnDropClicked(ItemRow row)
        {
            if (inventory == null || row.cachedData == null)
                return;

            var data = row.cachedData;
            bool whole = Input.GetKey(dropWholeStackModifier)
                || (dropWholeStackModifier == KeyCode.LeftShift && Input.GetKey(KeyCode.RightShift));

            if (RequiresDropConfirm(data, whole))
            {
                bool armedForThis = row.pendingUntil > 0f
                    && Time.unscaledTime <= row.pendingUntil
                    && row.pendingData == data
                    && row.pendingRepresentative == row.representative
                    && row.pendingWhole == whole;

                if (!armedForThis)
                {
                    row.pendingData = data;
                    row.pendingRepresentative = row.representative;
                    row.pendingWhole = whole;
                    row.pendingUntil = Time.unscaledTime + DropConfirmWindow;
                    UpdateDropButton(row);
                    return;
                }
            }

            ClearPendingDrop(row);
            ExecuteDrop(row, data, whole);
        }

        /// <summary>
        /// 실제로 버린다. 인벤토리 쪽 공개 경로만 쓴다:
        /// · 스택되지 않는 도구 1개 → 이 줄이 가리키는 그 인스턴스를 items에서 직접 지우고
        ///   NotifyInventoryChanged로 알린다(PlayerInventory가 "items를 직접 건드린 코드"용으로 열어둔 통로).
        ///   RemoveItems(data, 1)를 쓰면 내구도가 다른 동일 종류 중 목록 끝의 것이 지워져,
        ///   화면에서 고른 것과 실제로 사라지는 것이 어긋난다.
        /// · 그 외(겹쳐지는 재료·음식·무제한 도구) → RemoveItems. 같은 칸 안의 개체는 서로 완전히 동일하다.
        /// </summary>
        private void ExecuteDrop(ItemRow row, ItemData data, bool whole)
        {
            int maxStack = data.MaxStackSize;
            bool removed;

            if (maxStack <= 1)
            {
                removed = row.representative != null && inventory.items.Remove(row.representative);
                if (removed)
                    inventory.NotifyInventoryChanged();
            }
            else
            {
                int amount = whole ? Mathf.Max(1, row.cachedCount) : 1;
                removed = inventory.RemoveItems(data, amount);
            }

            if (!removed)
            {
                // 이미 사라진 대상을 눌렀을 뿐이므로 목록만 다시 그린다(소리도 내지 않는다).
                RefreshList();
                return;
            }

            // ArtDirection.md 4.2 A단계(일상): 화면 이펙트 없이 짧은 효과음 하나만.
            MakeGame.Systems.AudioManager.Instance?.PlayPickup();
            RefreshList();
        }

        /// <summary>
        /// 행(카테고리 헤더 + 항목 줄) 풀의 개수가 부족하면 필요한 만큼 새로 만든다.
        /// </summary>
        private void EnsureRowCount(int count)
        {
            while (rowPool.Count < count)
                rowPool.Add(CreateRow(rowPool.Count));
        }

        /// <summary>
        /// 카테고리 헤더(선택적) + [카테고리 색 띠 | 아이콘 | 이름·개수(+내구도 막대)/설명 | 사용법 | 버리기] 한 줄을 생성한다.
        /// 짝수/홀수 행마다 배경을 살짝 다르게 칠해 가독성을 높인다(줄무늬 배경).
        /// </summary>
        private ItemRow CreateRow(int index)
        {
            var row = new ItemRow();

            // 바깥 컨테이너: 카테고리 헤더(위) + 실제 항목 줄(아래).
            var entryGo = new GameObject($"Entry{index}", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(LayoutElement));
            entryGo.transform.SetParent(listContainer, false);
            var entryLayout = entryGo.GetComponent<LayoutElement>();
            entryLayout.minHeight = 48f;

            var entryVlg = entryGo.GetComponent<VerticalLayoutGroup>();
            entryVlg.childForceExpandWidth = true;
            entryVlg.childForceExpandHeight = false;
            entryVlg.spacing = 2f;
            entryVlg.childAlignment = TextAnchor.UpperLeft;

            var categoryHeader = UIBuilder.CreateText(entryGo.transform, "CategoryHeader", "", 12,
                new Color(0.7f, 0.7f, 0.7f, 1f), TextAnchor.MiddleLeft);
            categoryHeader.gameObject.AddComponent<LayoutElement>().minHeight = 16f;
            categoryHeader.gameObject.SetActive(false);

            var rowGo = new GameObject($"Row{index}", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            rowGo.transform.SetParent(entryGo.transform, false);
            rowGo.GetComponent<LayoutElement>().minHeight = 46f;
            rowGo.GetComponent<Image>().color = index % 2 == 0
                ? new Color(1f, 1f, 1f, 0.04f)
                : new Color(1f, 1f, 1f, 0f);

            var hlg = rowGo.GetComponent<HorizontalLayoutGroup>();
            hlg.childForceExpandWidth = false;
            hlg.childForceExpandHeight = true;
            hlg.spacing = 8f;
            hlg.childAlignment = TextAnchor.MiddleLeft;
            hlg.padding = new RectOffset(4, 4, 2, 2);

            // 카테고리 색 띠: 폭 4px, 줄 높이를 그대로 채운다.
            var stripGo = new GameObject("CategoryStrip", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(LayoutElement));
            stripGo.transform.SetParent(rowGo.transform, false);
            var stripLayout = stripGo.GetComponent<LayoutElement>();
            stripLayout.minWidth = 4f;
            stripLayout.preferredWidth = 4f;
            var categoryStrip = stripGo.GetComponent<Image>();
            categoryStrip.color = Color.gray;

            var iconRt = UIBuilder.CreateIcon(rowGo.transform, "Icon", 22f, Color.gray, "?");
            var icon = iconRt.GetComponent<Image>();
            var letterLabel = iconRt.Find("Letter").GetComponent<Text>();

            // 이름/개수(위) + 내구도 막대 + 설명(아래)을 세로로 쌓는 컨테이너.
            var textColGo = new GameObject("TextColumn", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(LayoutElement));
            textColGo.transform.SetParent(rowGo.transform, false);
            textColGo.GetComponent<LayoutElement>().flexibleWidth = 1f;
            var vlg = textColGo.GetComponent<VerticalLayoutGroup>();
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            vlg.spacing = 1f;
            vlg.childAlignment = TextAnchor.MiddleLeft;

            // 폰트 15 = ArtDirection.md 4.3 H2 등급(15~16). 16에서 15로 내린 이유는 한 줄에 버리기 버튼이
            // 추가돼 이름이 접힐 여지를 없애기 위해서다(등급 안에서의 조정이라 위계는 그대로다).
            var nameCountLabel = UIBuilder.CreateText(textColGo.transform, "NameCount", "", 15, Color.white, TextAnchor.MiddleLeft);

            // 내구도 막대: 스택되지 않는 도구 줄에서만 켠다. "개수 대신 막대"가 스택 줄과의 시각적 구분이다.
            var duraFill = UIBuilder.CreateProgressBar(textColGo.transform, "Durability",
                new Color(1f, 1f, 1f, 0.15f), Color.white);
            var duraBarGo = duraFill.transform.parent.gameObject;
            var duraLayout = duraBarGo.AddComponent<LayoutElement>();
            duraLayout.minHeight = 4f;
            duraLayout.preferredHeight = 4f;
            duraLayout.flexibleWidth = 1f;
            duraBarGo.SetActive(false);

            var descLabel = UIBuilder.CreateText(textColGo.transform, "Desc", "", 11, BodyGray, TextAnchor.MiddleLeft);

            // 사용법 힌트: 이 아이템을 지금 어떤 키로 쓸 수 있는지("[C] 섭취" 등)를 오른쪽에 표시한다.
            var usageLabel = UIBuilder.CreateText(rowGo.transform, "Usage", "", 11, new Color(0.7f, 0.7f, 0.7f, 1f), TextAnchor.MiddleRight);
            usageLabel.gameObject.AddComponent<LayoutElement>().preferredWidth = 64f;

            // 버리기 버튼: 용량 상한이 생긴 이상 버릴 수단이 없으면 인벤토리가 막힌다.
            var dropButton = UIBuilder.CreateButton(rowGo.transform, "Drop", "버리기", () => OnDropClicked(row));
            var dropLayout = dropButton.gameObject.AddComponent<LayoutElement>();
            dropLayout.minWidth = 56f;
            dropLayout.preferredWidth = 56f;
            dropLayout.preferredHeight = 26f;
            var dropLabel = dropButton.GetComponentInChildren<Text>();
            if (dropLabel != null)
                dropLabel.fontSize = 12;

            row.rowGo = entryGo;
            row.entryLayout = entryLayout;
            row.categoryHeader = categoryHeader;
            row.categoryStrip = categoryStrip;
            row.icon = icon;
            row.letterLabel = letterLabel;
            row.nameCountLabel = nameCountLabel;
            row.descLabel = descLabel;
            row.usageLabel = usageLabel;
            row.duraBarGo = duraBarGo;
            row.duraFill = duraFill;
            row.dropButton = dropButton;
            row.dropLabel = dropLabel;
            return row;
        }

        /// <summary>
        /// 카테고리별 표시 이름. 필터 이름 배열(CategoryFilterNames)의 인덱스 0("전체") 다음부터가
        /// ItemCategory 값 순서와 1:1로 대응하므로 그 배열을 그대로 재사용한다(이름 정의를 두 곳에 두지 않는다).
        /// </summary>
        private static string GetCategoryDisplayName(UIBuilder.ItemCategory category)
        {
            int index = (int)category + 1;
            return index >= 0 && index < CategoryFilterNames.Length ? CategoryFilterNames[index] : "기타";
        }

        /// <summary>
        /// 이 아이템을 지금 어떤 키로 쓸 수 있는지 짧은 힌트를 만든다. 키 자체는 InteractionController가
        /// 정하는 값이라(C=섭취/R=조리/G=설치) 여기서는 그 기본 키에 맞춘 표시 문자열만 담당한다.
        /// </summary>
        private string GetUsageHint(ItemData data)
        {
            if (data == null)
                return "";

            if (data.isRawFood && data.cookedResult != null)
                return $"[{cookKey}] 굽기";

            if (data.isPlaceable && data.placementPrefab != null)
                return $"[{placeKey}] 설치";

            if (data.curesBleeding || data.curesPoison || data.curesBrokenBone)
                return $"[{consumeKey}] 치료";

            if (data.IsConsumable)
                return $"[{consumeKey}] 섭취";

            if (data.isWeapon)
                return $"[{interactKey}] 공격";

            return "재료";
        }
    }
}
