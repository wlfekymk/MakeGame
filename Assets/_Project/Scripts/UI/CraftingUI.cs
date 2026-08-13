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

        /// <summary>레시피 한 줄을 구성하는 UI 요소와 원본 레시피를 함께 담는다.</summary>
        private class RecipeRow
        {
            public CraftingRecipe recipe;
            public Text label;
            public Button button;
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
        /// 레시피 하나에 대응하는 "이름+필요재료" 텍스트와 "제작" 버튼으로 구성된 한 줄을 생성한다.
        /// </summary>
        private RecipeRow CreateRow(CraftingRecipe recipe)
        {
            var rowGo = new GameObject($"Row_{recipe.recipeName}", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            rowGo.transform.SetParent(listContainer, false);
            rowGo.GetComponent<LayoutElement>().minHeight = 40f;

            var hlg = rowGo.GetComponent<HorizontalLayoutGroup>();
            hlg.childForceExpandWidth = false;
            hlg.childForceExpandHeight = true;
            hlg.spacing = 8f;
            hlg.childAlignment = TextAnchor.MiddleLeft;

            var label = UIBuilder.CreateText(rowGo.transform, "Label", BuildRecipeLabel(recipe), 14, Color.white, TextAnchor.MiddleLeft);
            label.gameObject.AddComponent<LayoutElement>().preferredWidth = 260f;

            var row = new RecipeRow { recipe = recipe, label = label };
            row.button = UIBuilder.CreateButton(rowGo.transform, "CraftButton", "제작", () => craftingSystem?.TryCraft(recipe));
            row.button.gameObject.AddComponent<LayoutElement>().preferredWidth = 60f;

            return row;
        }

        /// <summary>
        /// 레시피 이름과 필요 재료 목록을 두 줄짜리 표시용 문자열로 만든다.
        /// </summary>
        private string BuildRecipeLabel(CraftingRecipe recipe)
        {
            var parts = new List<string>();
            foreach (var req in recipe.requiredMaterials)
            {
                if (req.item != null)
                    parts.Add($"{req.item.itemName}x{req.quantity}");
            }
            return $"{recipe.recipeName}\n({string.Join(", ", parts)})";
        }

        /// <summary>
        /// 매 프레임 각 레시피의 제작 가능 여부(재료/스킬 레벨)를 확인해 버튼 활성화와 글자 색을 갱신한다.
        /// </summary>
        private void RefreshRows()
        {
            if (craftingSystem == null)
                return;

            foreach (var row in rows)
            {
                bool canCraft = craftingSystem.CanCraft(row.recipe);
                row.button.interactable = canCraft;
                row.label.color = canCraft ? Color.white : new Color(1f, 0.4f, 0.4f, 1f);
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
