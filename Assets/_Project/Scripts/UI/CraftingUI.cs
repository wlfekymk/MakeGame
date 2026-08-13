using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using MakeGame.Data;
using MakeGame.Systems;

namespace MakeGame.UI
{
    /// <summary>
    /// 제작(크래프팅) UI. V 키로 열고 닫으며, 보유한 레시피 목록과 필요 재료, 제작 버튼을 화면 오른쪽에 표시한다.
    /// 재료가 부족하거나 스킬 레벨이 모자라면 버튼이 비활성화되고 글자가 붉게 표시된다.
    /// 씬에 미리 배치하지 않고 Start()에서 UIBuilder로 캔버스/패널/행을 직접 생성한다.
    /// </summary>
    public class CraftingUI : MonoBehaviour
    {
        [Tooltip("제작을 실제로 처리할 크래프팅 시스템")]
        public CraftingSystem craftingSystem;

        [Tooltip("이 UI에 표시할 전체 레시피 목록 (제작 가능한 모든 레시피를 인스펙터에서 연결)")]
        public List<CraftingRecipe> recipeBook = new List<CraftingRecipe>();

        [Tooltip("제작 창을 여닫는 키")]
        public KeyCode toggleKey = KeyCode.V;

        /// <summary>재료 하나를 표시하는 작은 칩(아이콘 + "이름x개수" 텍스트)을 나타낸다.</summary>
        private class MaterialChip
        {
            public ItemData item;
            public int requiredQuantity;
            public Text label;
        }

        /// <summary>레시피 한 줄을 구성하는 UI 요소와 원본 레시피를 함께 담는다.</summary>
        private class RecipeRow
        {
            public CraftingRecipe recipe;
            public Text nameLabel;
            public Button button;
            public List<MaterialChip> materialChips = new List<MaterialChip>();
        }

        private GameObject panelRoot;
        private RectTransform listContainer;
        private readonly List<RecipeRow> rows = new List<RecipeRow>();

        /// <summary>
        /// 시작 시 제작 UI 계층을 생성하고 기본적으로 닫힌 상태로 둔다.
        /// </summary>
        private void Start()
        {
            BuildUI();
            SetOpen(false);
        }

        /// <summary>
        /// 매 프레임 토글 입력을 감지하고, 창이 열려 있으면 각 레시피의 제작 가능 여부를 최신 상태로 갱신한다.
        /// </summary>
        private void Update()
        {
            if (Input.GetKeyDown(toggleKey))
                SetOpen(!panelRoot.activeSelf);

            if (panelRoot != null && panelRoot.activeSelf)
                RefreshRows();
        }

        /// <summary>
        /// 캔버스와 배경 패널을 만들고, recipeBook에 등록된 레시피마다 한 줄씩 행을 생성한다.
        /// </summary>
        private void BuildUI()
        {
            var canvas = UIBuilder.CreateCanvas("CraftingCanvas", sortOrder: 10);

            var panel = UIBuilder.CreatePanel(
                canvas.transform, "CraftingPanel",
                anchorMin: new Vector2(1f, 0.3f), anchorMax: new Vector2(1f, 1f),
                offsetMin: new Vector2(-380f, -20f), offsetMax: new Vector2(-20f, -20f),
                color: new Color(0f, 0f, 0f, 0.75f));

            panelRoot = panel.gameObject;

            var title = UIBuilder.CreateText(panel, "Title", "제작 (V)", 20, Color.white, TextAnchor.UpperLeft);
            title.rectTransform.anchorMin = new Vector2(0f, 1f);
            title.rectTransform.anchorMax = new Vector2(1f, 1f);
            title.rectTransform.pivot = new Vector2(0.5f, 1f);
            title.rectTransform.anchoredPosition = new Vector2(0f, -8f);
            title.rectTransform.sizeDelta = new Vector2(0f, 28f);

            var listGo = new GameObject("RecipeList", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            listGo.transform.SetParent(panel, false);
            listContainer = listGo.GetComponent<RectTransform>();
            listContainer.anchorMin = new Vector2(0f, 0f);
            listContainer.anchorMax = new Vector2(1f, 1f);
            listContainer.offsetMin = new Vector2(10f, 10f);
            listContainer.offsetMax = new Vector2(-10f, -40f);

            var vlg = listGo.GetComponent<VerticalLayoutGroup>();
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            vlg.spacing = 6f;
            vlg.childAlignment = TextAnchor.UpperLeft;

            var fitter = listGo.GetComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            foreach (var recipe in recipeBook)
            {
                if (recipe == null)
                    continue;
                rows.Add(CreateRow(recipe));
            }
        }

        /// <summary>
        /// 레시피 하나에 대응하는 두 줄짜리 블록(① 결과물 아이콘+이름+제작 버튼, ② 필요 재료 칩 목록)을 생성한다.
        /// </summary>
        private RecipeRow CreateRow(CraftingRecipe recipe)
        {
            var blockGo = new GameObject($"Row_{recipe.recipeName}", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(LayoutElement));
            blockGo.transform.SetParent(listContainer, false);
            blockGo.GetComponent<LayoutElement>().minHeight = 58f;

            var blockVlg = blockGo.GetComponent<VerticalLayoutGroup>();
            blockVlg.childForceExpandWidth = true;
            blockVlg.childForceExpandHeight = false;
            blockVlg.spacing = 2f;
            blockVlg.childAlignment = TextAnchor.UpperLeft;

            // ① 헤더 줄: 결과물 아이콘 + 레시피 이름 + 제작 버튼
            var headerGo = new GameObject("Header", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            headerGo.transform.SetParent(blockGo.transform, false);
            headerGo.GetComponent<LayoutElement>().minHeight = 28f;
            var headerHlg = headerGo.GetComponent<HorizontalLayoutGroup>();
            headerHlg.childForceExpandWidth = false;
            headerHlg.childForceExpandHeight = true;
            headerHlg.spacing = 8f;
            headerHlg.childAlignment = TextAnchor.MiddleLeft;

            string resultLetter = recipe.resultItem != null && !string.IsNullOrEmpty(recipe.resultItem.itemName)
                ? recipe.resultItem.itemName.Substring(0, 1)
                : "?";
            UIBuilder.CreateIcon(headerGo.transform, "ResultIcon", 22f, UIBuilder.GetItemCategoryColor(recipe.resultItem), resultLetter);

            var nameLabel = UIBuilder.CreateText(headerGo.transform, "Name", recipe.recipeName, 15, Color.white, TextAnchor.MiddleLeft);
            nameLabel.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;

            var row = new RecipeRow { recipe = recipe, nameLabel = nameLabel };
            row.button = UIBuilder.CreateButton(headerGo.transform, "CraftButton", "제작", () => craftingSystem?.TryCraft(recipe));
            row.button.gameObject.AddComponent<LayoutElement>().preferredWidth = 60f;

            // ② 재료 줄: 재료마다 작은 아이콘 + "이름x개수" 칩. 보유량이 부족하면 빨간색으로 표시된다.
            var materialsGo = new GameObject("Materials", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            materialsGo.transform.SetParent(blockGo.transform, false);
            materialsGo.GetComponent<LayoutElement>().minHeight = 24f;
            var materialsHlg = materialsGo.GetComponent<HorizontalLayoutGroup>();
            materialsHlg.childForceExpandWidth = false;
            materialsHlg.childForceExpandHeight = true;
            materialsHlg.spacing = 10f;
            materialsHlg.childAlignment = TextAnchor.MiddleLeft;
            materialsHlg.padding = new RectOffset(30, 0, 0, 0); // 위 아이콘과 시작 위치를 맞추기 위한 들여쓰기

            foreach (var req in recipe.requiredMaterials)
            {
                if (req.item == null)
                    continue;

                var chipGo = new GameObject($"Chip_{req.item.itemName}", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
                chipGo.transform.SetParent(materialsGo.transform, false);
                var chipHlg = chipGo.GetComponent<HorizontalLayoutGroup>();
                chipHlg.childForceExpandWidth = false;
                chipHlg.childForceExpandHeight = true;
                chipHlg.spacing = 3f;
                chipHlg.childAlignment = TextAnchor.MiddleLeft;

                UIBuilder.CreateIcon(chipGo.transform, "Icon", 14f, UIBuilder.GetItemCategoryColor(req.item), "");
                var qtyLabel = UIBuilder.CreateText(chipGo.transform, "Qty", $"{req.item.itemName}x{req.quantity}", 12, Color.white, TextAnchor.MiddleLeft);
                qtyLabel.gameObject.AddComponent<LayoutElement>().preferredWidth = 90f;

                row.materialChips.Add(new MaterialChip { item = req.item, requiredQuantity = req.quantity, label = qtyLabel });
            }

            return row;
        }

        /// <summary>
        /// 매 프레임 각 레시피의 제작 가능 여부(재료/스킬 레벨)를 확인해 버튼 활성화와 글자 색을 갱신한다.
        /// 재료 칩은 개별적으로 보유량을 확인해, 부족한 재료만 콕 집어 빨간색으로 표시한다.
        /// </summary>
        private void RefreshRows()
        {
            if (craftingSystem == null || craftingSystem.inventory == null)
                return;

            foreach (var row in rows)
            {
                bool canCraft = craftingSystem.CanCraft(row.recipe);
                row.button.interactable = canCraft;
                row.nameLabel.color = canCraft ? Color.white : new Color(1f, 0.75f, 0.75f, 1f);

                foreach (var chip in row.materialChips)
                {
                    int have = craftingSystem.inventory.GetItemCount(chip.item);
                    bool enough = have >= chip.requiredQuantity;
                    chip.label.color = enough ? new Color(0.6f, 1f, 0.6f, 1f) : new Color(1f, 0.45f, 0.45f, 1f);
                }
            }
        }

        /// <summary>
        /// 패널을 열거나 닫는다.
        /// </summary>
        private void SetOpen(bool open)
        {
            if (panelRoot != null)
                panelRoot.SetActive(open);
        }
    }
}
