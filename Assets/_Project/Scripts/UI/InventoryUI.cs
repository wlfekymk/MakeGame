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

        /// <summary>아이템 한 종류를 표시하는 한 줄(아이콘 + 이름/개수 텍스트)을 구성하는 UI 요소 묶음.</summary>
        private class ItemRow
        {
            public GameObject rowGo;
            public Image icon;
            public Text letterLabel;
            public Text nameCountLabel;
        }

        private GameObject panelRoot;
        private RectTransform listContainer;
        private readonly List<ItemRow> rowPool = new List<ItemRow>();
        private readonly List<ItemData> orderBuffer = new List<ItemData>();
        private readonly Dictionary<ItemData, int> countBuffer = new Dictionary<ItemData, int>();

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
                RefreshList();
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

            foreach (var item in inventory.items)
            {
                if (item.data == null)
                    continue;

                if (!countBuffer.ContainsKey(item.data))
                {
                    countBuffer[item.data] = 0;
                    orderBuffer.Add(item.data);
                }
                countBuffer[item.data]++;
            }

            int rowsNeeded = Mathf.Max(orderBuffer.Count, 1);
            EnsureRowCount(rowsNeeded);

            if (orderBuffer.Count == 0)
            {
                rowPool[0].icon.gameObject.SetActive(false);
                rowPool[0].nameCountLabel.text = "(비어 있음)";
                rowPool[0].nameCountLabel.color = new Color(0.7f, 0.7f, 0.7f, 1f);
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
                string usesInfo = data.IsUnlimited ? "" : $" (최대 {data.maxUses}회 사용)";
                row.nameCountLabel.text = $"{data.itemName}  x{count}{usesInfo}";
                row.nameCountLabel.color = Color.white;
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
        /// 아이콘(카테고리 색상 + 이름 첫 글자) + "이름 x개수" 텍스트로 구성된 한 줄을 생성한다.
        /// 짝수/홀수 행마다 배경을 살짝 다르게 칠해 가독성을 높인다(줄무늬 배경).
        /// </summary>
        private ItemRow CreateRow(int index)
        {
            var rowGo = new GameObject($"Row{index}", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            rowGo.transform.SetParent(listContainer, false);
            rowGo.GetComponent<LayoutElement>().minHeight = 28f;
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

            var nameCountLabel = UIBuilder.CreateText(rowGo.transform, "NameCount", "", 16, Color.white, TextAnchor.MiddleLeft);
            nameCountLabel.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;

            return new ItemRow { rowGo = rowGo, icon = icon, letterLabel = letterLabel, nameCountLabel = nameCountLabel };
        }
    }
}
