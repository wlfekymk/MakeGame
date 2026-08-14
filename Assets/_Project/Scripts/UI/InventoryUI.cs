using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using MakeGame.Data;
using MakeGame.Player;

namespace MakeGame.UI
{
    /// <summary>
    /// 인벤토리 UI. Tab 키로 열고 닫으며, 현재 소지한 아이템을 종류별로 묶어 이름과 개수를 화면 왼쪽에 표시한다.
    /// 씬에 미리 배치하지 않고 Start()에서 UIBuilder로 캔버스/패널/목록을 직접 생성한다.
    /// </summary>
    public class InventoryUI : MonoBehaviour
    {
        [Tooltip("표시할 대상 인벤토리")]
        public PlayerInventory inventory;

        [Tooltip("인벤토리 창을 여닫는 키")]
        public KeyCode toggleKey = KeyCode.Tab;

        [Tooltip("인벤토리가 열려 있을 때 카테고리 필터를 순환시키는 키")]
        public KeyCode cycleFilterKey = KeyCode.F;

        /// <summary>인벤토리 아이템 분류 카테고리. 정렬 순서와 필터 순환 순서로 함께 사용한다.</summary>
        private enum ItemCategory
        {
            Weapon = 0,
            Cure = 1,
            Food = 2,
            Drink = 3,
            Placeable = 4,
            Vehicle = 5,
            Material = 6,
        }

        private static readonly string[] CategoryFilterNames =
        {
            "전체", "무기", "치료", "음식", "음료", "설치형", "이동수단", "재료"
        };

        // 필터 인덱스 0은 "전체"를 뜻하고, 1부터는 ItemCategory 값 + 1에 대응한다.
        private int currentFilterIndex = 0;

        /// <summary>아이템 한 종류를 표시하는 한 줄(아이콘 + 이름/개수 텍스트 + 설명 텍스트)을 구성하는 UI 요소 묶음.</summary>
        private class ItemRow
        {
            public GameObject rowGo;
            public Image icon;
            public Text letterLabel;
            public Text nameCountLabel;
            public Text descLabel;
        }

        private GameObject panelRoot;
        private RectTransform listContainer;
        private Text titleLabel;
        private readonly List<ItemRow> rowPool = new List<ItemRow>();
        private readonly List<ItemData> orderBuffer = new List<ItemData>();
        private readonly Dictionary<ItemData, int> countBuffer = new Dictionary<ItemData, int>();
        // 버그 수정: 인벤토리 목록이 도구/무기의 "최대 사용 횟수"만 보여주고 실제로 지금 몇 번 남았는지는
        // 전혀 표시하지 않아, 손도끼가 거의 다 닳았는지 플레이어가 알 방법이 없었다. 같은 종류가 여러 개
        // 쌓여 있을 수 있으므로(remainingUses가 서로 다를 수 있음) 그중 가장 적게 남은 값을 대표로 보여준다.
        private readonly Dictionary<ItemData, int> minRemainingBuffer = new Dictionary<ItemData, int>();

        /// <summary>
        /// 시작 시 인벤토리 UI 계층을 생성하고 기본적으로 닫힌 상태로 둔다.
        /// </summary>
        private void Start()
        {
            BuildUI();
            SetOpen(false);
        }

        /// <summary>
        /// 매 프레임 토글 입력을 감지하고, 창이 열려 있으면 목록을 최신 상태로 갱신한다.
        /// </summary>
        private void Update()
        {
            if (Input.GetKeyDown(toggleKey))
                SetOpen(!panelRoot.activeSelf);

            if (panelRoot != null && panelRoot.activeSelf)
            {
                // 인벤토리가 열려 있을 때만 필터 순환 키를 받는다 (닫혀 있을 때 실수로 필터가 바뀌는 것을 방지).
                if (Input.GetKeyDown(cycleFilterKey))
                    CycleFilter();

                RefreshList();
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
        /// </summary>
        private void UpdateTitle()
        {
            if (titleLabel == null)
                return;

            titleLabel.text = $"인벤토리 (Tab)  [필터: {CategoryFilterNames[currentFilterIndex]}, F로 전환]";
        }

        /// <summary>
        /// 아이템 하나의 분류 카테고리를 판정한다. 정렬 순서와 필터링에 함께 사용한다.
        /// UIBuilder.GetItemCategoryColor와 동일한 우선순위 규칙을 따른다.
        /// </summary>
        private static ItemCategory GetCategory(ItemData item)
        {
            if (item.isWeapon)
                return ItemCategory.Weapon;

            if (item.curesBleeding || item.curesPoison || item.curesBrokenBone)
                return ItemCategory.Cure;

            if (item.hungerRestoreAmount > 0f)
                return ItemCategory.Food;

            if (item.thirstRestoreAmount > 0f)
                return ItemCategory.Drink;

            if (item.isPlaceable)
                return ItemCategory.Placeable;

            if (item.blockedFromLargeIslandsByCurrent)
                return ItemCategory.Vehicle;

            return ItemCategory.Material;
        }

        /// <summary>
        /// 캔버스와 배경 패널, 아이템 목록을 담을 세로 레이아웃 컨테이너를 생성한다.
        /// </summary>
        private void BuildUI()
        {
            var canvas = UIBuilder.CreateCanvas("InventoryCanvas", sortOrder: 10);

            var panel = UIBuilder.CreatePanel(
                canvas.transform, "InventoryPanel",
                anchorMin: new Vector2(0f, 0.3f), anchorMax: new Vector2(0f, 1f),
                offsetMin: new Vector2(20f, -20f), offsetMax: new Vector2(340f, -20f),
                color: new Color(0f, 0f, 0f, 0.75f));

            panelRoot = panel.gameObject;

            var title = UIBuilder.CreateText(panel, "Title", "인벤토리 (Tab)", 20, Color.white, TextAnchor.UpperLeft);
            title.rectTransform.anchorMin = new Vector2(0f, 1f);
            title.rectTransform.anchorMax = new Vector2(1f, 1f);
            title.rectTransform.pivot = new Vector2(0.5f, 1f);
            title.rectTransform.anchoredPosition = new Vector2(0f, -8f);
            title.rectTransform.sizeDelta = new Vector2(0f, 28f);
            titleLabel = title;

            var listGo = new GameObject("ItemList", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            listGo.transform.SetParent(panel, false);
            listContainer = listGo.GetComponent<RectTransform>();
            listContainer.anchorMin = new Vector2(0f, 0f);
            listContainer.anchorMax = new Vector2(1f, 1f);
            listContainer.offsetMin = new Vector2(10f, 10f);
            listContainer.offsetMax = new Vector2(-10f, -40f);

            var vlg = listGo.GetComponent<VerticalLayoutGroup>();
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            vlg.spacing = 4f;
            vlg.childAlignment = TextAnchor.UpperLeft;

            var fitter = listGo.GetComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        }

        /// <summary>
        /// 패널을 열거나 닫는다.
        /// </summary>
        private void SetOpen(bool open)
        {
            if (panelRoot != null)
                panelRoot.SetActive(open);
        }

        /// <summary>
        /// 인벤토리 아이템을 종류별로 묶어 "이름 x개수" 형태로 목록에 표시한다.
        /// 매 프레임 새로 만들지 않고 텍스트 오브젝트 풀을 재사용한다.
        /// </summary>
        private void RefreshList()
        {
            if (inventory == null || listContainer == null)
                return;

            orderBuffer.Clear();
            countBuffer.Clear();
            minRemainingBuffer.Clear();

            // 필터 인덱스 0은 "전체"이고, 1 이상이면 해당 카테고리(인덱스-1)만 통과시킨다.
            bool filterActive = currentFilterIndex > 0;
            ItemCategory activeCategory = filterActive ? (ItemCategory)(currentFilterIndex - 1) : default;

            foreach (var item in inventory.items)
            {
                if (item.data == null)
                    continue;

                if (filterActive && GetCategory(item.data) != activeCategory)
                    continue;

                if (!countBuffer.ContainsKey(item.data))
                {
                    countBuffer[item.data] = 0;
                    orderBuffer.Add(item.data);
                    minRemainingBuffer[item.data] = item.remainingUses;
                }
                countBuffer[item.data]++;

                // 무제한(remainingUses < 0) 항목은 비교 대상에서 제외하고, 그 외에는 가장 적게 남은 값을 추적한다.
                if (item.remainingUses >= 0 && item.remainingUses < minRemainingBuffer[item.data])
                    minRemainingBuffer[item.data] = item.remainingUses;
            }

            // 카테고리별로 묶이도록 정렬하고, 같은 카테고리 안에서는 이름순으로 정렬해 항목을 찾기 쉽게 한다.
            orderBuffer.Sort((a, b) =>
            {
                int categoryCompare = GetCategory(a).CompareTo(GetCategory(b));
                if (categoryCompare != 0)
                    return categoryCompare;
                return string.Compare(a.itemName, b.itemName, System.StringComparison.CurrentCulture);
            });

            UpdateTitle();

            int rowsNeeded = Mathf.Max(orderBuffer.Count, 1);
            EnsureRowCount(rowsNeeded);

            if (orderBuffer.Count == 0)
            {
                rowPool[0].icon.gameObject.SetActive(false);
                rowPool[0].nameCountLabel.text = filterActive ? "(이 카테고리에 해당하는 아이템 없음)" : "(비어 있음)";
                rowPool[0].nameCountLabel.color = new Color(0.7f, 0.7f, 0.7f, 1f);
                rowPool[0].descLabel.text = "";
                rowPool[0].rowGo.SetActive(true);
                for (int i = 1; i < rowPool.Count; i++)
                    rowPool[i].rowGo.SetActive(false);
                return;
            }

            for (int i = 0; i < orderBuffer.Count; i++)
            {
                var data = orderBuffer[i];
                var row = rowPool[i];

                row.icon.gameObject.SetActive(true);
                row.icon.color = UIBuilder.GetItemCategoryColor(data);
                row.letterLabel.text = string.IsNullOrEmpty(data.itemName) ? "?" : data.itemName.Substring(0, 1);

                int count = countBuffer[data];
                // 최대 사용 횟수뿐 아니라 실제로 몇 번 남았는지(같은 종류 중 가장 적게 남은 값)도 함께 보여준다.
                string usesInfo = data.IsUnlimited ? "" : $" ({minRemainingBuffer[data]}/{data.maxUses}회 남음)";
                row.nameCountLabel.text = $"{data.itemName}  x{count}{usesInfo}";
                // 내구도가 얼마 남지 않은 도구/무기는 노란색(경고)~빨간색(위급)으로 강조해 눈에 띄게 한다.
                if (!data.IsUnlimited && data.maxUses > 0)
                {
                    float remainRatio = (float)minRemainingBuffer[data] / data.maxUses;
                    row.nameCountLabel.color = remainRatio <= 0.2f ? new Color(1f, 0.35f, 0.3f, 1f)
                        : remainRatio <= 0.4f ? new Color(1f, 0.85f, 0.3f, 1f)
                        : Color.white;
                }
                else
                {
                    row.nameCountLabel.color = Color.white;
                }

                // 버그 수정: ItemData.description이 그동안 어떤 UI에도 표시되지 않는 죽은 데이터였다.
                // 이름/개수 아래에 작은 회색 글씨로 설명을 함께 보여줘, 이미 작성해둔 아이템 설명이
                // 실제로 플레이어에게 전달되게 한다.
                row.descLabel.text = string.IsNullOrEmpty(data.description) ? "" : data.description;
                row.rowGo.SetActive(true);
            }
            for (int i = orderBuffer.Count; i < rowPool.Count; i++)
                rowPool[i].rowGo.SetActive(false);
        }

        /// <summary>
        /// 행(아이콘 + 이름/개수 텍스트) 풀의 개수가 부족하면 필요한 만큼 새로 만든다.
        /// </summary>
        private void EnsureRowCount(int count)
        {
            while (rowPool.Count < count)
                rowPool.Add(CreateRow(rowPool.Count));
        }

        /// <summary>
        /// 아이콘(카테고리 색상 + 이름 첫 글자) + "이름 x개수" 텍스트 + 설명 텍스트(작은 회색 글씨)로
        /// 구성된 한 줄을 생성한다. 짝수/홀수 행마다 배경을 살짝 다르게 칠해 가독성을 높인다(줄무늬 배경).
        /// 이름/개수와 설명을 세로로 쌓기 위해, 아이콘 옆에 수직 레이아웃 컨테이너를 하나 더 둔다.
        /// </summary>
        private ItemRow CreateRow(int index)
        {
            var rowGo = new GameObject($"Row{index}", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            rowGo.transform.SetParent(listContainer, false);
            rowGo.GetComponent<LayoutElement>().minHeight = 42f;
            rowGo.GetComponent<Image>().color = index % 2 == 0
                ? new Color(1f, 1f, 1f, 0.04f)
                : new Color(1f, 1f, 1f, 0f);

            var hlg = rowGo.GetComponent<HorizontalLayoutGroup>();
            hlg.childForceExpandWidth = false;
            hlg.childForceExpandHeight = true;
            hlg.spacing = 8f;
            hlg.childAlignment = TextAnchor.MiddleLeft;
            hlg.padding = new RectOffset(4, 4, 2, 2);

            var iconRt = UIBuilder.CreateIcon(rowGo.transform, "Icon", 22f, Color.gray, "?");
            var icon = iconRt.GetComponent<Image>();
            var letterLabel = iconRt.Find("Letter").GetComponent<Text>();

            // 이름/개수(위) + 설명(아래)을 세로로 쌓는 컨테이너.
            var textColGo = new GameObject("TextColumn", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(LayoutElement));
            textColGo.transform.SetParent(rowGo.transform, false);
            textColGo.GetComponent<LayoutElement>().flexibleWidth = 1f;
            var vlg = textColGo.GetComponent<VerticalLayoutGroup>();
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            vlg.spacing = 1f;
            vlg.childAlignment = TextAnchor.MiddleLeft;

            var nameCountLabel = UIBuilder.CreateText(textColGo.transform, "NameCount", "", 16, Color.white, TextAnchor.MiddleLeft);

            // 버그 수정: ItemData.description이 지금까지 어떤 UI에도 노출되지 않던 죽은 데이터였다.
            // 이름 아래에 작은 회색 글씨로 설명을 표시해, 이미 작성된 아이템 설명을 실제로 보여준다.
            var descLabel = UIBuilder.CreateText(textColGo.transform, "Desc", "", 11, new Color(0.75f, 0.75f, 0.75f, 1f), TextAnchor.MiddleLeft);

            return new ItemRow { rowGo = rowGo, icon = icon, letterLabel = letterLabel, nameCountLabel = nameCountLabel, descLabel = descLabel };
        }
    }
}
