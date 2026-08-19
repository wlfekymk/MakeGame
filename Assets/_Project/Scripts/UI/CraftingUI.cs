using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using MakeGame.Data;
using MakeGame.Systems;

namespace MakeGame.UI
{
    /// <summary>
    /// 제작(크래프팅) UI. V 키로 열고 닫으며, 제작법을 **결과물 아이콘 격자**로 보여준다.
    /// 씬에 미리 배치하지 않고 Start()에서 UIBuilder로 캔버스/창/격자를 직접 생성한다.
    ///
    /// 목록형 → 격자형 재작성(B24). 인벤토리(B19)가 세운 창 UI 표준을 그대로 따른다:
    ///   1) 알파 0.93 어두운 패널(UIBuilder.WindowBackgroundColor) - 뒤의 HUD 글자가 비치면 안 된다
    ///   2) 제목 표시줄 + 마우스로 누르는 빨간 X 닫기 버튼
    ///   3) UIDragHandle로 창을 마우스로 옮길 수 있다
    ///   4) 제작법은 결과물 아이콘으로 보여주고, 한 번에 2개 이상 나오면 우하단에 숫자
    ///   5) 아이콘에 마우스를 올리면 ItemTooltipUI로 필요 재료와 보유 수량이 뜬다
    ///
    /// 예전 형식(레시피 한 줄 = 이름 + 재료 칩 + 제작 버튼)이 손해였던 이유:
    /// · 화면 오른쪽에 못 박혀 있어 옮길 수도, 마우스로 닫을 수도 없었다.
    /// · 알파 0.75라 뒤의 월드/HUD가 재료 글자 사이로 비쳤다.
    /// · 만들 수 있는 것과 없는 것이 **정렬로 구분되지 않아** 매번 11줄을 다 읽어야 했다.
    /// · 결과물 아이콘이 22px 장식이었고, 실제 판독은 전부 글자로 했다.
    ///
    /// 지금 구조:
    /// · 칸 하나 = 제작법 하나. 결과물 아이콘을 크게 그리고, **만들 수 있으면 밝게 / 재료나 기술이
    ///   모자라면 어둡게** 칠한다(형태가 아니라 밝기로 구분 - 색맹 대응과 야간 가독성).
    /// · **제작 가능한 것이 항상 위로 온다.** 그다음 재료 부족, 마지막이 기술 레벨 부족이다.
    /// · 필요 재료와 보유 수량은 툴팁에서 본다(부족분은 붉게 + 몇 개 부족한지까지).
    /// · 카테고리 필터는 넣지 않았다 - 제작법이 11개(ScriptableObjects/Recipes)라 전부 두 줄에
    ///   들어온다. 필터는 한 화면에 안 들어올 때 값을 하는 장치이고, 여기서는 조작만 늘린다.
    /// · **제작 창은 제작만 한다.** 예전에 이 창 아래에 있던 "탈출 목표"(배 단계 / 경비행기 수리)
    ///   섹션은 제거했다 - QuestUI(J)가 같은 정보를 진행도 막대와 함께 관리하게 되어 두 창에 같은
    ///   내용이 나란히 뜨는 중복이었다. 안내줄에 퀘스트 창 단축키만 남겨 길을 알려준다.
    /// </summary>
    public class CraftingUI : MonoBehaviour
    {
        [Tooltip("제작을 실제로 처리할 크래프팅 시스템")]
        public CraftingSystem craftingSystem;

        [Tooltip("이 UI에 표시할 전체 레시피 목록 (제작 가능한 모든 레시피를 인스펙터에서 연결)")]
        public List<CraftingRecipe> recipeBook = new List<CraftingRecipe>();

        [Tooltip("제작 창을 여닫는 키")]
        public KeyCode toggleKey = KeyCode.V;

        /// <summary>
        /// 갱신 주기(초). 인벤토리와 같은 이유로 **이벤트 + 저주파 폴링**을 함께 쓴다:
        /// PlayerInventory.InventoryChanged는 재료가 늘고 주는 순간을 즉시 알려주지만,
        /// 스킬 레벨업(PlayerSkills.AddExperience)에는 이벤트가 없어 폴링이 없으면 기술 부족으로
        /// 잠긴 제작법이 레벨을 올려도 계속 잠겨 보인다. 폴링은 창이 열려 있는 동안에만 돈다.
        /// </summary>
        private const float RefreshInterval = 0.2f;

        // 격자 치수는 인벤토리와 같은 값을 쓴다. 두 창의 칸 크기가 다르면 나란히 띄웠을 때
        // 같은 아이콘이 다른 크기로 보여 "다른 물건"처럼 읽힌다.
        private const int Columns = 6;
        private const float SlotSize = 62f;
        private const float SlotSpacing = 6f;
        private const float InfoRowHeight = 26f;
        private const float FooterButtonHeight = 28f;
        private const float HintHeight = 16f;
        private const float SkillChipWidth = 150f;

        /// <summary>왼쪽 격자 단의 폭(402). 인벤토리와 같은 6열 62px이라 두 창의 칸이 같은 크기로 보인다.</summary>
        private const float GridWidth = Columns * SlotSize + (Columns - 1) * SlotSpacing;

        /// <summary>본문 폭(402 + 12 + 232 = 646). 창 전체 폭은 골격이 좌우 여백을 더해 674 - 인벤토리와 같다.</summary>
        private const float BodyWidth = GridWidth + UITheme.PaneGap + UITheme.DetailPaneWidth;

        /// <summary>
        /// 격자 위쪽(정보 줄)이 차지하는 높이. **본문 좌표계**라 제목줄 높이도 창 여백도 들어가지 않는다 -
        /// 그 둘은 공용 골격(UIBuilder.CreateSkinnedWindow)이 이미 빼고 본문을 넘겨준다.
        /// </summary>
        private const float GridTopOffset = InfoRowHeight + 10f;

        /// <summary>상세 단 안쪽 여백. 인벤토리 상세 패널과 같은 값이어야 두 창이 같은 창으로 읽힌다.</summary>
        private const float DetailPadding = 12f;

        /// <summary>상세 단 맨 위 선택 문구가 쓰는 높이(232px 폭에서 세 줄까지 접힌다).</summary>
        private const float SelectionHeight = 54f;

        /// <summary>재료 한 줄의 높이와, 그 줄 오른쪽 "보유 / 필요" 칸의 폭.</summary>
        private const float IngredientRowHeight = 18f;
        private const float IngredientCountWidth = 72f;

        /// <summary>필요 재료 제목줄의 위치(선택 문구 아래 구분선 바로 밑).</summary>
        private const float IngredientHeadingTop = DetailPadding + SelectionHeight + 6f + UITheme.SeparatorThickness + 8f;

        /// <summary>제작 버튼은 상세 단 폭을 꽉 채운다 - 이 창에서 유일하게 무언가를 소비하는 조작이라 숨기지 않는다.</summary>
        private const float CraftButtonWidth = UITheme.DetailPaneWidth - DetailPadding * 2f;

        /// <summary>
        /// 본문 최소 높이. 창 높이는 제작법 수(격자 줄 수)가 정하는데, 목록이 짧으면 오른쪽 상세 단의
        /// 재료 줄과 제작 버튼이 잘린다. 상세 단이 항상 다 보이는 높이를 바닥으로 깐다.
        /// </summary>
        private const float MinBodyHeight = 300f;

        // 색: ArtDirection.md 팔레트 안에서만 쓴다(새 색을 만들지 않는다).
        // 본문 글자색(#CCCCCC)·보조 글자색·Danger Red는 UITheme/UIBuilder에 같은 값이 이미 있다.
        // 같은 색이 파일마다 복사돼 있으면 창 스킨을 한 번에 손볼 수가 없어 여기서는 지웠다.
        private static readonly Color SunstrokeGold = new Color(0.902f, 0.749f, 0.2f, 1f);  // #E6BF33
        private static readonly Color SatisfiedColor = new Color(0.55f, 0.85f, 0.7f, 1f);   // Medic Green 계열

        /// <summary>제작 가능한 칸의 아이콘 색(그대로 밝게).</summary>
        private static readonly Color IconReadyColor = Color.white;

        /// <summary>재료/기술이 모자란 칸의 아이콘 색. 회색으로 눌러 밝기만으로 구분되게 한다.</summary>
        private static readonly Color IconLockedColor = new Color(0.4f, 0.42f, 0.44f, 0.7f);

        private static readonly Color SlotSelectedOutline = new Color(0.31f, 0.659f, 0.478f, 1f);

        /// <summary>
        /// 창 위치를 세션 동안 기억한다. static인 이유는 인벤토리와 같다 - 이 컴포넌트는 씬에 배치돼
        /// 있어 씬을 다시 로드하면 통째로 새로 생성되고, 인스턴스 필드에 두면 "새 게임/불러오기 후
        /// 창이 처음 자리로 돌아간다". 세이브 파일에는 쓰지 않는다.
        /// </summary>
        private static bool hasSavedWindowPosition;
        private static Vector2 savedWindowPosition;

        /// <summary>제작법 하나의 표시 상태. 화면에 그릴 값과 정렬 키를 함께 들고 있다.</summary>
        private class RecipeEntry
        {
            public CraftingRecipe recipe;
            public ItemData result;
            public string displayName;
            public UIBuilder.ItemCategory category;

            // 갱신마다 다시 계산되는 값
            public bool canCraft;
            public bool skillLocked;
            public int skillLevel;

            /// <summary>
            /// 이 제작법이 설치형 제작 시설(제작대/용광로/베틀)을 요구하는데 지금 그 근처가 아닌지.
            /// 판정은 CraftingSystem이 하고(IsMissingRequiredStation) 여기서는 결과만 들고 있는다 -
            /// UI가 반경 판정을 따로 구현하면 화면과 실제 제작 결과가 갈라진다.
            /// </summary>
            public bool stationMissing;

            /// <summary>stationMissing일 때 필요한 시설 종류(문구에 쓸 이름을 여기서 얻는다).</summary>
            public CraftStationKind requiredStation;

            /// <summary>정렬 키. 0 = 지금 만들 수 있다 · 1 = 재료 부족 · 2 = 기술 레벨 부족 · 3 = 제작대 없음.</summary>
            public int sortKey;
        }

        /// <summary>격자 칸 하나. 화면 부품(UIBuilder가 만든다)과 "지금 무엇을 보여주는지" 캐시.</summary>
        private class SlotBinding
        {
            public UIBuilder.SlotVisual visual;
            public RecipeEntry entry;

            /// <summary>화면에 실제로 반영된 상태 비트(잠김/hover/선택). -1은 아직 그린 적 없음.</summary>
            public int appliedVisual = -1;
        }

        private RectTransform canvasRect;
        private GameObject panelRoot;
        private RectTransform windowRt;
        private UIDragHandle dragHandle;
        private RectTransform gridContainer;
        private Text readyLabel;
        private Text skillLabel;
        private Text selectionLabel;
        private Text hintLabel;
        private Text questHintLabel;
        private Button craftButton;
        private RectTransform detailPane;
        private RectTransform ingredientList;
        private Text ingredientHeading;
        private ItemTooltipUI tooltip;

        /// <summary>재료 한 줄(이름 왼쪽 · 보유/필요 오른쪽). 제작법마다 줄 수가 달라 만들어 두고 재사용한다.</summary>
        private class IngredientRow
        {
            public GameObject go;
            public Text label;
            public Text count;
        }

        private readonly List<IngredientRow> ingredientRows = new List<IngredientRow>();

        private readonly List<RecipeEntry> entries = new List<RecipeEntry>();
        private readonly List<RecipeEntry> displayOrder = new List<RecipeEntry>();
        private readonly List<SlotBinding> slots = new List<SlotBinding>();

        private MakeGame.Player.PlayerInventory subscribedInventory;

        private float refreshTimer;

        /// <summary>
        /// 안내줄의 퀘스트 키 문구를 이미 만들었는지. QuestUI는 런타임 생성이고 Instance를 **Start에서**
        /// 잡기 때문에(QuestUI.cs:121-123), 씬 컴포넌트인 이 창의 Start에서 읽으면 실행 순서에 따라
        /// null일 수 있다(AGENT_BRIEF 4장). 그래서 창을 처음 열 때 한 번만 만든다 - 그 시점이면
        /// 두 Start가 모두 끝나 있다.
        /// </summary>
        private bool questHintBuilt;

        private int hoverIndex = -1;
        private CraftingRecipe selectedRecipe;
        private int selectedIndex = -1;

        // "마지막으로 문자열을 만들었을 때의 표시 조건". 초기값을 있을 수 없는 값으로 둬서 첫 갱신은
        // 반드시 실행되게 한다. 값이 바뀔 때만 문자열을 다시 만든다(폴링마다 조립하지 않는다).
        private int lastReadyCount = -1;
        private int lastEntryCount = -1;
        private int lastSkillLevel = int.MinValue;
        private CraftingRecipe lastFooterRecipe;
        private int lastFooterState = -1;

        private CraftingRecipe lastIngredientRecipe;
        private int lastIngredientSignature = int.MinValue;


        /// <summary>
        /// 정렬 순서: 제작 가능 → 재료 부족 → 기술 부족, 그 안에서는 카테고리 → 이름순.
        /// 캡처가 없는 정적 람다라 컴파일러가 한 번만 만들어 캐시한다(정렬마다 델리게이트가 생기지 않는다).
        /// 이름까지 비교하는 이유: List.Sort는 불안정 정렬이라 동점을 남겨두면 갱신마다 칸 순서가
        /// 뒤바뀌어, 커서를 올려둔 칸이 저절로 다른 제작법으로 바뀐다.
        /// </summary>
        private static readonly Comparison<RecipeEntry> EntryOrder = (a, b) =>
        {
            if (a.sortKey != b.sortKey)
                return a.sortKey.CompareTo(b.sortKey);

            int categoryCompare = a.category.CompareTo(b.category);
            if (categoryCompare != 0)
                return categoryCompare;

            return string.Compare(a.displayName, b.displayName, StringComparison.CurrentCulture);
        };

        private bool IsOpen => panelRoot != null && panelRoot.activeSelf;

        /// <summary>시작 시 제작 UI 계층을 생성하고 기본적으로 닫힌 상태로 둔다.</summary>
        private void Start()
        {
            tooltip = ItemTooltipUI.GetOrCreate();

            // Resources/Recipes의 제작법을 recipeBook에 합친 **다음** BuildEntries를 부른다 -
            // 격자 칸 수(BuildUI의 slots)와 창 높이(ApplyWindowLayout)가 entries.Count로 정해지므로,
            // 등재가 늦으면 새 제작법이 칸 없이 잘려 나간다. Resources.LoadAll은 런타임 메서드에서만
            // 부른다(필드 초기화식/생성자에서 Unity API를 부르면 로드 순서에 따라 터진다).
            AppendResourceRecipes();
            BuildEntries();
            BuildUI();
            SubscribeInventory();
            SetOpen(false);
        }

        /// <summary>
        /// 구독한 이벤트를 반드시 해제한다. 씬 재로드 시 죽은 UI가 이벤트에 남아 있으면
        /// 다음 인벤토리 변화에서 파괴된 오브젝트를 건드리게 된다.
        /// </summary>
        private void OnDestroy()
        {
            if (subscribedInventory != null)
            {
                subscribedInventory.InventoryChanged -= OnInventoryChanged;
                subscribedInventory = null;
            }

            if (tooltip != null)
                tooltip.Hide();
        }

        private void SubscribeInventory()
        {
            var inventory = craftingSystem != null ? craftingSystem.inventory : null;
            if (inventory == null || inventory == subscribedInventory)
                return;

            if (subscribedInventory != null)
                subscribedInventory.InventoryChanged -= OnInventoryChanged;

            subscribedInventory = inventory;
            subscribedInventory.InventoryChanged += OnInventoryChanged;
        }

        /// <summary>재료가 늘거나 줄면 격자를 즉시 다시 그린다(폴링 0.2초를 기다리지 않는다).</summary>
        private void OnInventoryChanged()
        {
            if (IsOpen)
                RefreshAll();
        }

        /// <summary>매 프레임 토글 입력을 감지하고, 창이 열려 있으면 저주파로 갱신한다.</summary>
        private void Update()
        {
            if (panelRoot == null)
                return;

            if (Input.GetKeyDown(toggleKey))
                SetOpen(!panelRoot.activeSelf);

            if (!panelRoot.activeSelf)
                return;

            // Time.timeScale이 0인 화면 위에서도 멈추지 않도록 unscaled를 쓴다(프로젝트 공통 규칙).
            refreshTimer -= Time.unscaledDeltaTime;
            if (refreshTimer <= 0f)
            {
                refreshTimer = RefreshInterval;
                RefreshAll();
            }
        }

        // ────────────────────────────────────────────────────────────────────────
        // 생성
        // ────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Resources/Recipes 폴더의 제작법을 recipeBook에 자동 등재한다. recipeBook은 씬에 직렬화된
        /// 리스트라 새 제작법 에셋을 만들 때마다 씬을 고칠 수는 없다 - 폴더에 넣으면 여기서 줍는다.
        /// 씬 리스트에 이미 연결된 것과의 중복은 **참조 비교**로 거른다(같은 에셋은 같은 인스턴스로
        /// 로드되므로 이름 비교가 필요 없고, GetInstanceID 같은 우회도 쓰지 않는다).
        /// </summary>
        private void AppendResourceRecipes()
        {
            var loaded = Resources.LoadAll<CraftingRecipe>("Recipes");
            for (int i = 0; i < loaded.Length; i++)
            {
                var recipe = loaded[i];
                if (recipe == null)
                    continue;

                bool alreadyListed = false;
                for (int j = 0; j < recipeBook.Count; j++)
                {
                    if (ReferenceEquals(recipeBook[j], recipe))
                    {
                        alreadyListed = true;
                        break;
                    }
                }

                if (!alreadyListed)
                    recipeBook.Add(recipe);
            }
        }

        /// <summary>
        /// recipeBook을 표시용 항목으로 한 번만 변환한다. 이름·카테고리처럼 절대 변하지 않는 값은
        /// 여기서 계산해 두고, 갱신 때는 제작 가능 여부만 다시 본다.
        /// </summary>
        private void BuildEntries()
        {
            entries.Clear();
            displayOrder.Clear();

            for (int i = 0; i < recipeBook.Count; i++)
            {
                var recipe = recipeBook[i];
                if (recipe == null)
                    continue;

                var entry = new RecipeEntry
                {
                    recipe = recipe,
                    result = recipe.resultItem,
                    displayName = !string.IsNullOrEmpty(recipe.recipeName)
                        ? recipe.recipeName
                        : (recipe.resultItem != null ? recipe.resultItem.itemName : "?"),
                    category = UIBuilder.GetItemCategory(recipe.resultItem),
                    sortKey = 1,
                };

                entries.Add(entry);
                displayOrder.Add(entry);
            }
        }

        /// <summary>캔버스 · 창(제목 표시줄/닫기/정보 줄) · 격자 · 하단 조작줄과 안내줄을 만든다.</summary>
        private void BuildUI()
        {
            var canvas = UIBuilder.CreateCanvas("CraftingCanvas", sortOrder: 10);
            canvasRect = canvas.GetComponent<RectTransform>();

            // 제목줄 높이·여백·닫기 버튼 자리·드래그 손잡이는 전부 공용 골격이 정한다. 창마다 따로
            // 박아 두면 제작 창과 인벤토리를 나란히 띄웠을 때 서로 다른 게임의 UI처럼 보인다.
            // 넘기는 값은 **본문 안쪽 크기**다(창 전체 크기는 골격이 여백을 더해 정한다).
            var frame = UIBuilder.CreateSkinnedWindow(canvas.transform, "CraftingWindow",
                BodyWidth, ComputeBodyHeight(), $"제작 ({toggleKey})", canvasRect, () => SetOpen(false));

            windowRt = frame.window;
            dragHandle = frame.drag;
            panelRoot = windowRt.gameObject;

            // "지금 만들 수 있는 것 N"은 제목 옆 보조 정보 자리다 - 인벤토리의 용량 표시와 같은 자리라야
            // 두 창을 같이 띄웠을 때 눈이 같은 곳에서 요약을 찾는다.
            readyLabel = frame.status;

            BuildInfoRow(frame.body);

            gridContainer = UIBuilder.CreateSlotGrid(frame.body, "RecipeGrid", Columns, SlotSize, SlotSpacing, GridTopOffset);

            // CreateSlotGrid는 부모 한가운데를 기준으로 붙는다. 본문이 좌우 2단이 되었으므로 격자는
            // **왼쪽 단 기준**으로 다시 앵커한다(가운데에 두면 상세 단 밑으로 절반이 들어간다).
            gridContainer.anchorMin = new Vector2(0f, 1f);
            gridContainer.anchorMax = new Vector2(0f, 1f);
            gridContainer.pivot = new Vector2(0f, 1f);
            gridContainer.anchoredPosition = new Vector2(0f, -GridTopOffset);

            for (int i = 0; i < entries.Count; i++)
                slots.Add(CreateSlot(i));

            BuildDetailPane(frame.body);
            BuildFooter(frame.body);
            ApplyWindowLayout();

            // 위치 기억은 **레이아웃을 다 잡은 뒤에** 연결한다. ApplyWindowLayout이 부르는 ClampNow도
            // onMoved를 발행하므로, 먼저 연결해 두면 아직 아무 데도 놓지 않은 (0,0)이 "사용자가 옮겨둔
            // 자리"로 기록돼 처음 열 때 DefaultWindowPosition이 통째로 무시된다.
            dragHandle.onMoved = position =>
            {
                savedWindowPosition = position;
                hasSavedWindowPosition = true;
            };
        }

        /// <summary>왼쪽 단 첫 줄: 제작 기술 레벨(제작 가능 개수는 제목 옆 보조 정보로 옮겼다).</summary>
        private void BuildInfoRow(RectTransform body)
        {
            // 제작 기술 레벨을 상시 노출하는 이유: 재료를 다 모았는데도 잠긴 제작법(물 증류기 키트는
            // Lv2가 필요하다)을 만났을 때, 왜 안 되는지가 툴팁을 열기 전에도 화면에 있어야 한다.
            skillLabel = UIBuilder.CreateText(body, "SkillLevel", "", UITheme.FontBody, UITheme.TextDim, TextAnchor.MiddleRight);
            skillLabel.raycastTarget = false;
            skillLabel.horizontalOverflow = HorizontalWrapMode.Overflow;
            var skillRt = skillLabel.rectTransform;
            skillRt.anchorMin = new Vector2(0f, 1f);
            skillRt.anchorMax = new Vector2(0f, 1f);
            skillRt.pivot = new Vector2(1f, 1f);
            skillRt.sizeDelta = new Vector2(SkillChipWidth, InfoRowHeight);
            // 격자 단의 오른쪽 끝에 맞춘다 - 창 오른쪽 끝은 이제 상세 단이 쓰고 있다.
            skillRt.anchoredPosition = new Vector2(GridWidth, 0f);
        }

        /// <summary>
        /// 본문 오른쪽 단: 선택한 제작법. 위에서 아래로 이름·상태 → 필요 재료 목록이고,
        /// **제작 버튼은 단 맨 아래에 고정**한다. 재료 줄 수에 따라 버튼이 오르내리면 제작법마다
        /// 버튼 자리가 달라져 오폭을 부른다(인벤토리의 버리기 버튼과 같은 규칙).
        /// </summary>
        private void BuildDetailPane(RectTransform body)
        {
            // 오른쪽 단도 골격이 만든다 - 폭·재질·시작선을 창마다 따로 정하면 인벤토리와 어긋난다.
            detailPane = UIBuilder.CreateSidePane(body, "DetailPane", UITheme.DetailPaneWidth);

            selectionLabel = UIBuilder.CreateText(detailPane, "Selection", "", UITheme.FontBody, UITheme.TextPrimary, TextAnchor.UpperLeft);
            selectionLabel.raycastTarget = false;
            selectionLabel.horizontalOverflow = HorizontalWrapMode.Wrap;
            selectionLabel.verticalOverflow = VerticalWrapMode.Overflow;
            var selectionRt = selectionLabel.rectTransform;
            selectionRt.anchorMin = new Vector2(0f, 1f);
            selectionRt.anchorMax = new Vector2(1f, 1f);
            selectionRt.pivot = new Vector2(0.5f, 1f);
            selectionRt.offsetMin = new Vector2(DetailPadding, -DetailPadding - SelectionHeight);
            selectionRt.offsetMax = new Vector2(-DetailPadding, -DetailPadding);

            // 제작법의 "정체"와 "필요 재료"를 선으로 가른다 - 창 헤더가 하는 일을 단 안에서 반복한다.
            UIBuilder.CreateSeparator(detailPane, "DetailSeparator", DetailPadding + SelectionHeight + 6f, DetailPadding);

            ingredientHeading = UIBuilder.CreateText(detailPane, "IngredientHeading", "필요 재료", UITheme.FontBody, UITheme.TextDim, TextAnchor.UpperLeft);
            ingredientHeading.raycastTarget = false;
            var headingRt = ingredientHeading.rectTransform;
            headingRt.anchorMin = new Vector2(0f, 1f);
            headingRt.anchorMax = new Vector2(1f, 1f);
            headingRt.pivot = new Vector2(0.5f, 1f);
            headingRt.offsetMin = new Vector2(DetailPadding, -IngredientHeadingTop - HintHeight);
            headingRt.offsetMax = new Vector2(-DetailPadding, -IngredientHeadingTop);

            // 재료 줄은 개수가 제작법마다 달라 위에서부터 쌓는다(고정 자리를 주면 줄이 적은 제작법에서
            // 목록 아래에 구멍이 생긴다). 버튼만은 계속 단 바닥 고정이다.
            var listGo = new GameObject("Ingredients", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            listGo.transform.SetParent(detailPane, false);
            ingredientList = listGo.GetComponent<RectTransform>();
            ingredientList.anchorMin = new Vector2(0f, 1f);
            ingredientList.anchorMax = new Vector2(1f, 1f);
            ingredientList.pivot = new Vector2(0.5f, 1f);
            ingredientList.sizeDelta = new Vector2(-DetailPadding * 2f, 0f);
            ingredientList.anchoredPosition = new Vector2(0f, -(IngredientHeadingTop + HintHeight + 4f));

            var listLayout = listGo.GetComponent<VerticalLayoutGroup>();
            listLayout.spacing = 4f;
            listLayout.childControlHeight = true;
            listLayout.childControlWidth = true;
            listLayout.childForceExpandHeight = false;
            listLayout.childForceExpandWidth = true;

            listGo.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            craftButton = UIBuilder.CreateButton(detailPane, "Craft", "제작", CraftSelected);
            var craftRt = craftButton.GetComponent<RectTransform>();
            craftRt.anchorMin = new Vector2(0.5f, 0f);
            craftRt.anchorMax = new Vector2(0.5f, 0f);
            craftRt.pivot = new Vector2(0.5f, 0f);
            craftRt.sizeDelta = new Vector2(CraftButtonWidth, FooterButtonHeight);
            craftRt.anchoredPosition = new Vector2(0f, DetailPadding);
        }

        /// <summary>
        /// 재료 줄 하나를 얻는다(모자라면 만들어 목록에 넣는다). 제작법을 바꿀 때마다 오브젝트를 새로
        /// 만들지 않는다 - 창을 열어둔 채 목록을 훑으면 그때마다 계층이 늘어난다.
        /// </summary>
        private IngredientRow GetIngredientRow(int index)
        {
            while (ingredientRows.Count <= index)
            {
                var rowGo = new GameObject($"Ingredient{ingredientRows.Count}", typeof(RectTransform), typeof(LayoutElement));
                rowGo.transform.SetParent(ingredientList, false);
                rowGo.GetComponent<LayoutElement>().preferredHeight = IngredientRowHeight;

                var label = UIBuilder.CreateText(rowGo.transform, "Name", "", UITheme.FontBody, UITheme.TextPrimary, TextAnchor.MiddleLeft);
                label.raycastTarget = false;
                label.horizontalOverflow = HorizontalWrapMode.Overflow;
                var labelRt = label.rectTransform;
                labelRt.anchorMin = Vector2.zero;
                labelRt.anchorMax = Vector2.one;
                labelRt.offsetMin = Vector2.zero;
                labelRt.offsetMax = new Vector2(-IngredientCountWidth, 0f);

                // 보유/필요는 오른쪽 끝에 맞춘다 - 줄마다 같은 x에 있어야 "몇 개 모자란가"를 세로로 훑는다.
                var count = UIBuilder.CreateText(rowGo.transform, "Count", "", UITheme.FontBody, UITheme.TextPrimary, TextAnchor.MiddleRight);
                count.raycastTarget = false;
                count.horizontalOverflow = HorizontalWrapMode.Overflow;
                var countRt = count.rectTransform;
                countRt.anchorMin = new Vector2(1f, 0f);
                countRt.anchorMax = new Vector2(1f, 1f);
                countRt.pivot = new Vector2(1f, 0.5f);
                countRt.sizeDelta = new Vector2(IngredientCountWidth, 0f);
                countRt.anchoredPosition = Vector2.zero;

                ingredientRows.Add(new IngredientRow { go = rowGo, label = label, count = count });
            }

            return ingredientRows[index];
        }

        /// <summary>격자 아래 안내 두 줄. 실제 y 위치는 ApplyWindowLayout이 정한다.</summary>
        private void BuildFooter(RectTransform body)
        {
            hintLabel = UIBuilder.CreateText(body, "Hint", "", UITheme.FontBody, UITheme.TextDim, TextAnchor.MiddleLeft);
            hintLabel.raycastTarget = false;
            hintLabel.horizontalOverflow = HorizontalWrapMode.Overflow;
            var hintRt = hintLabel.rectTransform;
            hintRt.anchorMin = new Vector2(0f, 1f);
            hintRt.anchorMax = new Vector2(0f, 1f);
            hintRt.pivot = new Vector2(0f, 1f);
            hintRt.sizeDelta = new Vector2(GridWidth, HintHeight);

            // 조작 안내는 조건에 따라 바뀌지 않으므로 한 번만 만든다. 한 줄에 다 넣으면 왼쪽 격자
            // 단 폭(402px)을 넘어 상세 단을 덮으므로 "창 조작"과 "다른 창 안내"를 두 줄로 나눈다.
            hintLabel.text = "제목 표시줄 드래그로 창 이동 · 클릭 선택 · 우클릭 즉시 제작";

            // 두 번째 줄: 예전에 이 창 아래에 있던 "탈출 목표"가 어디로 갔는지 알려주는 자리다.
            // 그 정보는 이제 퀘스트 창이 진행도 막대와 함께 관리한다(제작 창은 제작만 한다).
            // 실제 문구는 QuestUI 단축키를 읽어야 해서 창을 처음 열 때 만든다(UpdateQuestHint).
            questHintLabel = UIBuilder.CreateText(body, "QuestHint", "", UITheme.FontBody, UITheme.TextDim, TextAnchor.MiddleLeft);
            questHintLabel.raycastTarget = false;
            questHintLabel.horizontalOverflow = HorizontalWrapMode.Overflow;
            var questHintRt = questHintLabel.rectTransform;
            questHintRt.anchorMin = new Vector2(0f, 1f);
            questHintRt.anchorMax = new Vector2(0f, 1f);
            questHintRt.pivot = new Vector2(0f, 1f);
            questHintRt.sizeDelta = new Vector2(GridWidth, HintHeight);
        }

        /// <summary>
        /// 퀘스트 창 안내 문구를 한 번만 만든다. 단축키는 실제 값(QuestUI.toggleKey)을 읽고, 아직
        /// 인스턴스가 없으면 코드 기본값 J로 폴백한다 - SurvivalHudUI.cs:561과 같은 방식이다.
        /// </summary>
        private void UpdateQuestHint()
        {
            if (questHintBuilt || questHintLabel == null)
                return;

            questHintBuilt = true;

            KeyCode questKey = QuestUI.Instance != null ? QuestUI.Instance.toggleKey : KeyCode.J;
            questHintLabel.text = $"[{questKey}] 퀘스트 - 배 건조·경비행기 수리 진행은 퀘스트 창에서 본다";
        }

        /// <summary>
        /// 창 안의 각 구역(격자 아래 조작줄 · 안내줄 두 줄)의 y 위치와 창 전체 높이를 정한다.
        /// 창 높이는 제작법 수만으로 정해지는 고정값이라 한 번만 부르면 된다 - 창 높이가 갱신마다
        /// 흔들리면 드래그 클램프도 함께 흔들려서 창이 저절로 조금씩 움직인다.
        /// </summary>
        private void ApplyWindowLayout()
        {
            if (windowRt == null)
                return;

            float gridHeight = ComputeGridHeight();

            if (gridContainer != null)
                gridContainer.sizeDelta = new Vector2(gridContainer.sizeDelta.x, gridHeight);

            // 안내 두 줄은 왼쪽 격자 단 아래다(선택 줄과 제작 버튼은 오른쪽 상세 단으로 갔다).
            float hintTop = GridTopOffset + gridHeight + 10f;
            float questHintTop = hintTop + HintHeight + 2f;

            if (hintLabel != null)
                hintLabel.rectTransform.anchoredPosition = new Vector2(0f, -hintTop);

            if (questHintLabel != null)
                questHintLabel.rectTransform.anchoredPosition = new Vector2(0f, -questHintTop);

            // 창 높이는 **본문 높이 + 골격이 쓰는 위아래 여백**이다. 본문은 창에 스트레치로 붙어 있어
            // 여기서 창만 키우면 격자·상세 단이 함께 따라온다.
            windowRt.sizeDelta = new Vector2(windowRt.sizeDelta.x,
                ComputeBodyHeight() + UITheme.ChromeTop + UITheme.ChromeBottom);

            if (dragHandle != null)
                dragHandle.ClampNow();
        }

        /// <summary>제작법 수가 정하는 격자 높이.</summary>
        private float ComputeGridHeight()
        {
            int rows = Mathf.CeilToInt(entries.Count / (float)Columns);
            return rows > 0 ? rows * SlotSize + (rows - 1) * SlotSpacing : 0f;
        }

        /// <summary>본문 높이(격자 + 안내 두 줄). 상세 단이 잘리지 않게 최소 높이를 바닥으로 깐다.</summary>
        private float ComputeBodyHeight()
        {
            float used = GridTopOffset + ComputeGridHeight() + 10f + HintHeight + 2f + HintHeight;
            return Mathf.Max(used, MinBodyHeight);
        }

        /// <summary>
        /// 격자 칸 하나를 만든다. 계층 구조는 UIBuilder.CreateItemSlot(인벤토리와 같은 부품)이 만들고,
        /// 여기서는 입력 콜백만 연결한다. 내구도 막대는 제작 창에서 의미가 없어 만들지 않는다.
        /// </summary>
        private SlotBinding CreateSlot(int index)
        {
            var visual = UIBuilder.CreateItemSlot(gridContainer, $"Recipe{index}");
            visual.input.index = index;
            visual.input.onEnter = OnSlotEnter;
            visual.input.onExit = OnSlotExit;
            visual.input.onLeftClick = OnSlotLeftClick;
            visual.input.onRightClick = OnSlotRightClick;

            return new SlotBinding { visual = visual };
        }

        // ────────────────────────────────────────────────────────────────────────
        // 열기 / 닫기
        // ────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// 창을 열거나 닫는다. 여는 순간 곧바로 한 번 그려서 첫 프레임에 빈 격자가 보이지 않게 한다.
        /// 창 밖 클릭으로 닫지 않는 것도 인벤토리와 같다 - 커서를 잠그지 않는 게임이라 창 밖 클릭은
        /// 곧 월드 조작이고, 인벤토리와 나란히 띄워 쓰는 흐름에서 다른 창을 만지는 순간 닫혀 버린다.
        /// </summary>
        private void SetOpen(bool open)
        {
            if (panelRoot == null)
                return;

            panelRoot.SetActive(open);

            if (!open)
            {
                hoverIndex = -1;
                HideTooltip();
                return;
            }

            // 옮겨둔 자리를 그대로 복원하고, 해상도가 바뀌었을 경우를 대비해 화면 안으로 다시 맞춘다.
            if (hasSavedWindowPosition)
                windowRt.anchoredPosition = savedWindowPosition;
            else
                windowRt.anchoredPosition = DefaultWindowPosition();

            if (dragHandle != null)
                dragHandle.ClampNow();

            // 씬 로드 순서에 따라 Start 시점에 craftingSystem.inventory가 아직 비어 있을 수 있다.
            SubscribeInventory();
            UpdateQuestHint();

            refreshTimer = RefreshInterval;
            RefreshAll();
        }

        /// <summary>
        /// 처음 열 때의 기본 자리: 화면 오른쪽, 미니맵(우상단 160px + 여백 20) 아래.
        /// 인벤토리는 왼쪽에 열리므로 두 창을 함께 띄워도 겹치지 않는다.
        /// </summary>
        private Vector2 DefaultWindowPosition()
        {
            if (canvasRect == null)
                return Vector2.zero;

            float halfCanvasWidth = canvasRect.rect.width * 0.5f;
            float halfCanvasHeight = canvasRect.rect.height * 0.5f;

            // [골격] 창 폭이 430 → 674(인벤토리와 같은 폭)로 늘었지만 자리 계산은 rect.width에서
            // 나오므로 값은 그대로 둔다. 오른쪽 붙임을 유지해도 왼쪽에 열리는 인벤토리와 겹치지 않는다.
            const float minimapBottomMargin = 200f; // MinimapUI.radarPanelSize 160 + 위아래 여백 40
            return new Vector2(halfCanvasWidth - 24f - windowRt.rect.width * 0.5f,
                halfCanvasHeight - minimapBottomMargin);
        }

        // ────────────────────────────────────────────────────────────────────────
        // 갱신
        // ────────────────────────────────────────────────────────────────────────

        /// <summary>제작 가능 여부를 다시 판정하고, 정렬 → 칸 반영 → 하단 줄 순으로 갱신한다.</summary>
        private void RefreshAll()
        {
            if (gridContainer == null)
                return;

            var skills = craftingSystem != null ? craftingSystem.skills : null;
            int readyCount = 0;

            for (int i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];

                // 스킬 판정은 CraftingSystem.CanCraft와 **같은 규칙**을 쓴다(skills가 없으면 통과).
                // 여기서 규칙을 다시 쓰면 화면과 실제 판정이 갈리고, 그게 이 프로젝트의 사고 1위다.
                entry.skillLevel = skills != null ? skills.GetLevel(entry.recipe.requiredSkill) : entry.recipe.requiredSkillLevel;
                entry.skillLocked = skills != null && entry.skillLevel < entry.recipe.requiredSkillLevel;
                entry.canCraft = craftingSystem != null && craftingSystem.CanCraft(entry.recipe);

                // 제작대 요구도 같은 규칙이다: 판정은 CraftingSystem 하나가 소유하고 UI는 물어보기만 한다.
                // 이 값은 플레이어가 걸어 다니면 바뀌므로 매 갱신(0.2초 폴링)마다 다시 본다.
                entry.stationMissing = craftingSystem != null
                    && craftingSystem.IsMissingRequiredStation(entry.recipe, out entry.requiredStation);

                // 제작대가 없어서 잠긴 칸은 목록의 **맨 끝**에 둔다(3). 기존 세 값(0/1/2)의 의미와
                // 상대 순서는 한 글자도 바꾸지 않으므로 기존 제작법의 배치는 예전 그대로다.
                entry.sortKey = entry.canCraft ? 0 : (entry.stationMissing ? 3 : (entry.skillLocked ? 2 : 1));
                if (entry.canCraft)
                    readyCount++;
            }

            displayOrder.Sort(EntryOrder);

            selectedIndex = -1;
            for (int i = 0; i < slots.Count; i++)
            {
                var entry = i < displayOrder.Count ? displayOrder[i] : null;
                if (entry != null && entry.recipe == selectedRecipe)
                    selectedIndex = i;

                BindSlot(slots[i], entry);
            }

            // 선택한 제작법이 목록에서 사라질 수는 없지만(제작법은 늘 같은 11개), 방어적으로 정리한다.
            if (selectedIndex < 0)
                selectedRecipe = null;

            RefreshSlotStates();
            UpdateInfoRow(readyCount);
            UpdateFooter();
            UpdateHoveredTooltip();
        }

        /// <summary>칸에 제작법을 붙인다. 붙어 있던 것과 같으면 아무것도 만지지 않는다.</summary>
        private void BindSlot(SlotBinding slot, RecipeEntry entry)
        {
            if (slot.entry == entry)
                return;

            slot.entry = entry;
            slot.appliedVisual = -1; // 내용이 바뀌었으니 상태색도 다시 칠하게 한다

            var visual = slot.visual;

            if (entry == null)
            {
                visual.go.SetActive(false);
                return;
            }

            visual.go.SetActive(true);
            visual.categoryStrip.color = UIBuilder.GetItemCategoryColor(entry.result);

            // 아이콘 31종이 전부 배선돼 있지만(ItemData.icon), 아이콘 없는 아이템이 결과물인 제작법이
            // 추가될 수 있으므로 이름 첫 글자 폴백을 남겨둔다.
            if (entry.result != null && entry.result.icon != null)
            {
                visual.icon.enabled = true;
                visual.icon.sprite = entry.result.icon;
                visual.letterLabel.gameObject.SetActive(false);
            }
            else
            {
                visual.icon.enabled = true;
                visual.icon.sprite = null;
                visual.letterLabel.gameObject.SetActive(true);
                visual.letterLabel.text = string.IsNullOrEmpty(entry.displayName) ? "?" : entry.displayName.Substring(0, 1);
            }

            // 한 번에 2개 이상 나오는 제작법만 숫자를 찍는다. "x1"은 정보가 0이면서 아이콘만 가린다.
            int resultQuantity = entry.recipe.resultQuantity;
            if (resultQuantity > 1)
            {
                visual.countLabel.gameObject.SetActive(true);
                visual.countLabel.text = resultQuantity.ToString();
            }
            else
            {
                visual.countLabel.gameObject.SetActive(false);
            }
        }

        /// <summary>
        /// 칸의 상태(제작 가능/잠김 · hover · 선택)를 색으로 반영한다. 상태 비트가 지난번과 같으면
        /// 색을 다시 대입하지 않는다 - uGUI는 color를 넣을 때마다 그래픽을 더럽혀 리빌드를 예약한다.
        /// </summary>
        private void ApplySlotState(SlotBinding slot, int index)
        {
            var entry = slot.entry;
            if (entry == null)
                return;

            bool ready = entry.canCraft;
            int visualState = (ready ? 1 : 0)
                | (index == hoverIndex ? 2 : 0)
                | (index == selectedIndex ? 4 : 0);

            if (slot.appliedVisual == visualState)
                return;

            slot.appliedVisual = visualState;

            var visual = slot.visual;

            visual.background.color = index == hoverIndex
                ? UIBuilder.SlotHoverColor
                : (ready ? UIBuilder.SlotFilledColor : UIBuilder.SlotEmptyColor);

            visual.outline.enabled = index == selectedIndex;
            visual.outline.effectColor = SlotSelectedOutline;

            // 아이콘은 스프라이트가 있으면 흰색(원본 색)/회색(눌러 어둡게), 폴백 글자 칸이면
            // 카테고리 색을 같은 비율로 눌러 같은 밝기 차이를 준다.
            Color categoryColor = UIBuilder.GetItemCategoryColor(entry.result);
            bool usesSprite = visual.icon.sprite != null;

            if (ready)
            {
                visual.icon.color = usesSprite ? IconReadyColor : categoryColor;
                visual.categoryStrip.color = categoryColor;
                visual.countLabel.color = Color.white;
                visual.letterLabel.color = Color.white;
            }
            else
            {
                visual.icon.color = usesSprite
                    ? IconLockedColor
                    : new Color(categoryColor.r * 0.4f, categoryColor.g * 0.4f, categoryColor.b * 0.4f, 0.7f);

                Color dimStrip = categoryColor;
                dimStrip.a = 0.3f;
                visual.categoryStrip.color = dimStrip;
                visual.countLabel.color = UITheme.TextDim;
                visual.letterLabel.color = UITheme.TextDim;
            }
        }

        private void RefreshSlotStates()
        {
            for (int i = 0; i < slots.Count; i++)
                ApplySlotState(slots[i], i);
        }

        /// <summary>제작 가능 개수 + 제작 기술 레벨. 실제로 바뀐 갱신에서만 문자열을 새로 만든다.</summary>
        private void UpdateInfoRow(int readyCount)
        {
            if (readyLabel != null && (readyCount != lastReadyCount || entries.Count != lastEntryCount))
            {
                lastReadyCount = readyCount;
                lastEntryCount = entries.Count;

                readyLabel.text = $"지금 만들 수 있는 것 {readyCount}/{entries.Count}";
                readyLabel.color = readyCount > 0 ? SatisfiedColor : UITheme.TextDim;
            }

            if (skillLabel == null)
                return;

            var skills = craftingSystem != null ? craftingSystem.skills : null;
            int level = skills != null ? skills.GetLevel(SkillType.Craftsmanship) : -1;
            if (level == lastSkillLevel)
                return;

            lastSkillLevel = level;

            if (level < 0)
            {
                skillLabel.text = "";
                return;
            }

            skillLabel.text = $"제작 기술 Lv{level}";
        }

        /// <summary>
        /// 선택 줄 + 제작 버튼 상태를 맞춘다. 폴링마다 문자열을 새로 만들지 않도록, 실제로 표시가
        /// 달라지는 조합(대상/가능 여부/잠긴 이유)일 때만 다시 쓴다.
        /// </summary>
        private void UpdateFooter()
        {
            // 상세 단은 **선택 > 호버** 순으로 채운다(인벤토리와 같은 규칙). 오른쪽 단이 생긴 뒤로
            // 떠다니는 툴팁은 같은 내용을 두 번 띄우면서 다른 제작법 칸을 가리기만 해서 뺐다.
            bool showingSelection = selectedIndex >= 0 && selectedIndex < slots.Count;
            int detailIndex = showingSelection ? selectedIndex : hoverIndex;
            var entry = detailIndex >= 0 && detailIndex < slots.Count ? slots[detailIndex].entry : null;
            int state = entry == null
                ? 0
                : (entry.canCraft ? 1 : (entry.stationMissing ? 4 : (entry.skillLocked ? 2 : 3)));

            // 버튼만은 **선택**에만 반응한다. 커서를 스치는 것만으로 살아나면, 다른 칸을 훑어보다
            // 누른 것이 엉뚱한 물건을 만든다.
            var chosen = showingSelection ? slots[selectedIndex].entry : null;
            if (craftButton != null)
                craftButton.interactable = chosen != null && chosen.canCraft;

            // 재료 줄은 아래 캐시 검사보다 **먼저** 갱신한다. 보유량이 1/3 → 2/3으로 늘어도 선택 상태
            // (재료 부족)는 그대로라, 여기서 걸러지면 숫자가 굳은 채로 남는다.
            UpdateIngredients(entry);

            // 캐시 키에 "선택인가 미리보기인가"를 섞는다. 같은 제작법을 훑어보다 클릭해 선택으로
            // 바뀌면 앞머리 문구가 달라져야 하는데, recipe와 state만 보면 같은 값이라 통째로 걸러진다.
            int keyedState = state | (showingSelection ? 0x100 : 0);
            if (entry?.recipe == lastFooterRecipe && keyedState == lastFooterState)
                return;

            lastFooterRecipe = entry != null ? entry.recipe : null;
            lastFooterState = keyedState;

            // 커서만 얹은 상태를 "선택"이라고 부르면 거짓말이 된다.
            string lead = showingSelection ? "선택" : "미리보기";

            if (selectionLabel == null)
                return;

            switch (state)
            {
                case 1:
                    selectionLabel.text = $"{lead}: {entry.displayName} - 만들 수 있다";
                    selectionLabel.color = SatisfiedColor;
                    break;

                case 2:
                    selectionLabel.text = $"{lead}: {entry.displayName} - 제작 기술 Lv{entry.recipe.requiredSkillLevel} 필요";
                    selectionLabel.color = SunstrokeGold;
                    break;

                case 3:
                    selectionLabel.text = $"{lead}: {entry.displayName} - 재료가 부족하다";
                    selectionLabel.color = UIBuilder.DangerRed;
                    break;

                // 제작대 부족은 기술 레벨 부족과 같은 성격이다: 재료를 다 모아도 지금은 못 만들지만
                // 조건을 채우면 풀린다. 그래서 색도 같은 SunstrokeGold를 쓴다(재료 부족의 붉은색은
                // "더 모아라"라는 다른 지시다). 문구 형식은 기존 세 줄과 완전히 같다.
                case 4:
                    selectionLabel.text = $"{lead}: {entry.displayName} - {CraftStation.GetDisplayName(entry.requiredStation)} 필요";
                    selectionLabel.color = SunstrokeGold;
                    break;

                default:
                    selectionLabel.text = "칸을 클릭해 제작법 선택";
                    selectionLabel.color = UITheme.TextDim;
                    break;
            }
        }

        /// <summary>
        /// 선택한 제작법의 재료 줄(이름 · 보유 / 필요)을 채운다. 부족한 줄만 붉게 칠해 "무엇이 몇 개
        /// 모자란가"를 툴팁을 열지 않고도 읽게 한다. 보유량이 그대로면 문자열을 다시 만들지 않는다.
        /// </summary>
        private void UpdateIngredients(RecipeEntry entry)
        {
            if (ingredientList == null)
                return;

            var inventory = craftingSystem != null ? craftingSystem.inventory : null;

            // 툴팁과 같은 서명을 쓴다 - 재료 보유량·스킬·제작대가 그대로면 다시 그릴 것이 없다.
            int signature = entry != null ? ComputeRecipeTooltipSignature(entry, inventory) : 0;
            if ((entry != null ? entry.recipe : null) == lastIngredientRecipe && signature == lastIngredientSignature)
                return;

            lastIngredientRecipe = entry != null ? entry.recipe : null;
            lastIngredientSignature = signature;

            if (ingredientHeading != null)
                ingredientHeading.gameObject.SetActive(entry != null);

            var materials = entry != null && entry.recipe != null ? entry.recipe.requiredMaterials : null;

            int used = 0;
            for (int i = 0; materials != null && i < materials.Count; i++)
            {
                var requirement = materials[i];
                if (requirement == null || requirement.item == null)
                    continue;

                int have = inventory != null ? inventory.GetItemCount(requirement.item) : 0;
                bool enough = have >= requirement.quantity;

                var row = GetIngredientRow(used++);
                row.go.SetActive(true);
                row.label.text = requirement.item.itemName;
                row.count.text = $"{have} / {requirement.quantity}";

                // 이름과 숫자를 같은 색으로 칠한다 - 한 줄이 통째로 "모자란 줄"로 읽혀야 훑기 쉽다.
                Color rowColor = enough ? UITheme.TextPrimary : UIBuilder.DangerRed;
                row.label.color = rowColor;
                row.count.color = rowColor;
            }

            for (int i = used; i < ingredientRows.Count; i++)
                ingredientRows[i].go.SetActive(false);
        }

        // ────────────────────────────────────────────────────────────────────────
        // 입력
        // ────────────────────────────────────────────────────────────────────────

        private void OnSlotEnter(int index)
        {
            hoverIndex = index;
            RefreshSlotStates();
            UpdateFooter();
        }

        private void OnSlotExit(int index)
        {
            if (hoverIndex != index)
                return;

            hoverIndex = -1;
            RefreshSlotStates();
            UpdateFooter();
        }

        private void OnSlotLeftClick(int index)
        {
            var entry = index >= 0 && index < slots.Count ? slots[index].entry : null;

            // 같은 칸을 다시 누르면 선택 해제. 선택을 못 푸는 UI는 제작 버튼이 계속 무장돼 있는
            // 것처럼 보여 불안하다.
            if (entry == null || (selectedIndex == index && selectedRecipe == entry.recipe))
            {
                selectedRecipe = null;
                selectedIndex = -1;
            }
            else
            {
                selectedRecipe = entry.recipe;
                selectedIndex = index;
            }

            RefreshSlotStates();
            UpdateFooter();
        }

        /// <summary>
        /// 우클릭 = 그 칸을 즉시 제작. 격자에는 칸마다 버튼을 놓을 자리가 없어 조작을 옮겼고, 대신
        /// (1) 하단 안내줄, (2) 선택 후 하단 제작 버튼 두 경로로 함께 노출한다 - 우클릭 하나만
        /// 남기면 마우스로 발견할 방법이 없다.
        /// </summary>
        private void OnSlotRightClick(int index)
        {
            var entry = index >= 0 && index < slots.Count ? slots[index].entry : null;
            if (entry == null)
                return;

            selectedRecipe = entry.recipe;
            selectedIndex = index;

            Craft(entry.recipe);
        }

        private void CraftSelected()
        {
            Craft(selectedRecipe);
        }

        /// <summary>
        /// 실제 제작. 판정과 재료 소모는 전부 CraftingSystem이 한다(UI는 결과만 다시 그린다).
        /// 성공 효과음도 TryCraft가 낸다 - 여기서 또 내면 두 번 울린다.
        /// 실패는 조용히 넘긴다: 버튼은 애초에 비활성이고, 우클릭으로 잠긴 칸을 눌렀을 때는
        /// 그 칸이 어둡다는 사실 자체가 이미 이유를 말하고 있다.
        /// </summary>
        private void Craft(CraftingRecipe recipe)
        {
            if (recipe == null || craftingSystem == null)
                return;

            if (!craftingSystem.TryCraft(recipe))
            {
                RefreshSlotStates();
                UpdateFooter();
                return;
            }

            RefreshAll();
        }

        // ────────────────────────────────────────────────────────────────────────
        // 툴팁
        // ────────────────────────────────────────────────────────────────────────

        /// <summary>커서가 얹혀 있는 칸의 재료 보유량이 바뀌었을 수 있으므로 다시 채운다.</summary>
        private void UpdateHoveredTooltip()
        {
            if (hoverIndex >= 0)
                UpdateFooter();
        }

        /// <summary>툴팁에 그려질 값(재료 보유량·스킬 레벨)을 정수 하나로 접는다.</summary>
        private static int ComputeRecipeTooltipSignature(RecipeEntry entry, MakeGame.Player.PlayerInventory inventory)
        {
            unchecked
            {
                // 제작대 유무는 재료가 하나도 안 변해도 플레이어가 걸어가면 바뀌는 값이라, 서명에
                // 넣지 않으면 시설 앞에 서도 툴팁 마지막 줄이 "제작대 필요"인 채로 굳는다.
                int signature = entry.skillLevel * 31 + (entry.canCraft ? 1 : 0) + (entry.stationMissing ? 2 : 0);

                var materials = entry.recipe.requiredMaterials;
                for (int i = 0; materials != null && i < materials.Count; i++)
                {
                    var requirement = materials[i];
                    if (requirement == null || requirement.item == null)
                        continue;

                    signature = signature * 31 + (inventory != null ? inventory.GetItemCount(requirement.item) : 0);
                }

                return signature;
            }
        }

        /// <summary>툴팁 맨 아래 줄. 지금 이 제작법으로 무엇을 할 수 있는지.</summary>
        private static string GetActionHint(RecipeEntry entry)
        {
            if (entry.canCraft)
                return "우클릭 = 즉시 제작 · 클릭 = 선택";

            // 제작대 부족을 기술 부족보다 먼저 말한다. 기술이 모자란 동시에 제작대도 없을 때, 기술은
            // 시간이 지나면 저절로 오르지만 제작대는 플레이어가 만들어 놓지 않으면 영영 생기지 않는다.
            if (entry.stationMissing)
                return $"{CraftStation.GetDisplayName(entry.requiredStation)} 근처에서만 만들 수 있다";

            if (entry.skillLocked)
                return "제작 기술 레벨이 오르면 풀린다";

            return "재료를 더 모아야 한다";
        }

        /// <summary>
        /// 이 창은 더 이상 떠다니는 툴팁을 띄우지 않는다(오른쪽 상세 단이 대신한다). 그래도 창을
        /// 닫을 때 한 번은 숨겨 준다 - 툴팁은 상자/인벤토리와 공유하는 단일 인스턴스라, 예전 세션에서
        /// 떠 있던 것이 남아 있을 수 있다.
        /// </summary>
        private void HideTooltip()
        {
            if (tooltip != null)
                tooltip.Hide();
        }
    }
}
