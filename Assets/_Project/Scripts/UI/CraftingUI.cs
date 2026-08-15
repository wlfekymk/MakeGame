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

        [Tooltip("탈출 목표(배 제작) 진행 상황을 읽어올 시스템. 비워두면 씬에서 자동으로 찾는다.")]
        public BoatConstructionSystem boatConstruction;

        [Tooltip("탈출 목표(경비행기 수리) 진행 상황을 읽어올 시스템. 비워두면 씬에서 자동으로 찾는다.")]
        public AircraftRepairSystem aircraftRepair;

        /// <summary>재료 하나를 표시하는 작은 칩(아이콘 + "이름 보유/필요" 텍스트)을 나타낸다.</summary>
        private class MaterialChip
        {
            public ItemData item;
            public int requiredQuantity;
            public Text label;

            // 성능: 표시 결과가 실제로 달라진 프레임에만 문자열을 다시 만든다(보유 수량이 그대로면 같은 문자열).
            public int cachedHave = -1;
        }

        // 팔레트(ArtDirection.md 1.1/1.3): 부족한 재료는 Danger Red #CC3333, 충족된 재료는 Medic Green #4FA87A 계열.
        private static readonly Color ShortageColor = new Color(0.8f, 0.2f, 0.2f, 1f);
        private static readonly Color SatisfiedColor = new Color(0.55f, 0.85f, 0.7f, 1f);
        private static readonly Color BodyGrayColor = new Color(0.75f, 0.75f, 0.75f, 1f);

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

        // 탈출 목표(배 3단계 / 경비행기 수리) 표시용. 요구 재료는 절대 하드코딩하지 않고
        // BoatConstructionSystem/AircraftRepairSystem이 들고 있는 실제 설계값을 매번 읽어서 그린다.
        private RectTransform goalContainer;
        private readonly List<Text> goalRowPool = new List<Text>();
        private float goalRefreshTimer = 0f;

        /// <summary>
        /// 시작 시 제작 UI 계층을 생성하고 기본적으로 닫힌 상태로 둔다.
        /// </summary>
        private void Start()
        {
            // 인스펙터에서 연결되지 않았을 때를 대비한 자동 탐색(SurvivalHudUI와 동일한 방식).
            // 씬 파일을 고칠 수 없는 상황에서도 탈출 목표 섹션이 동작하게 하기 위함이다.
            if (boatConstruction == null)
                boatConstruction = FindAnyObjectByType<BoatConstructionSystem>();
            if (aircraftRepair == null)
                aircraftRepair = FindAnyObjectByType<AircraftRepairSystem>();

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
            {
                RefreshRows();

                // 탈출 목표는 초 단위로 천천히 변하는 정보라 매 프레임 다시 만들 이유가 없다.
                goalRefreshTimer -= Time.unscaledDeltaTime;
                if (goalRefreshTimer <= 0f)
                {
                    goalRefreshTimer = 0.25f;
                    RefreshGoals();
                }
            }
        }

        /// <summary>
        /// 캔버스와 배경 패널을 만들고, recipeBook에 등록된 레시피마다 한 줄씩 행을 생성한다.
        /// </summary>
        private void BuildUI()
        {
            var canvas = UIBuilder.CreateCanvas("CraftingCanvas", sortOrder: 10);

            // 개선(B4-14, ArtDirection.md 4.3): 카드형 패널임을 알려주는 상단 테두리(2px, 흰색 알파 12%)를 추가.
            var panel = UIBuilder.CreatePanel(
                canvas.transform, "CraftingPanel",
                anchorMin: new Vector2(1f, 0.3f), anchorMax: new Vector2(1f, 1f),
                offsetMin: new Vector2(-380f, -20f), offsetMax: new Vector2(-20f, -20f),
                color: new Color(0f, 0f, 0f, 0.75f),
                addTopBorder: true);

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

            BuildGoalSection();
        }

        /// <summary>
        /// 레시피 목록 아래에 "탈출 목표" 섹션(배 제작 단계 / 경비행기 수리)을 만든다.
        /// 실제 문구와 필요 재료는 RefreshGoals()가 시스템에서 읽어 채우므로 여기서는 빈 틀만 만든다.
        /// </summary>
        private void BuildGoalSection()
        {
            var goalGo = new GameObject("EscapeGoals", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(LayoutElement));
            goalGo.transform.SetParent(listContainer, false);
            goalContainer = goalGo.GetComponent<RectTransform>();
            goalGo.GetComponent<LayoutElement>().minHeight = 24f;

            var goalVlg = goalGo.GetComponent<VerticalLayoutGroup>();
            goalVlg.childForceExpandWidth = true;
            goalVlg.childForceExpandHeight = false;
            goalVlg.spacing = 2f;
            goalVlg.padding = new RectOffset(0, 0, 8, 0);
            goalVlg.childAlignment = TextAnchor.UpperLeft;

            // 섹션 제목은 항목명/강조 라벨(H2 15)로, 개별 목표 줄은 본문(Body 12)으로 표시한다.
            var header = UIBuilder.CreateText(goalGo.transform, "GoalHeader", "탈출 목표", 15, Color.white, TextAnchor.MiddleLeft);
            header.gameObject.AddComponent<LayoutElement>().minHeight = 20f;
        }

        /// <summary>
        /// 레시피 하나에 대응하는 두 줄짜리 블록(① 결과물 아이콘+이름+제작 버튼, ② 필요 재료 칩 목록)을 생성한다.
        /// </summary>
        private RecipeRow CreateRow(CraftingRecipe recipe)
        {
            var blockGo = new GameObject($"Row_{recipe.recipeName}", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(LayoutElement));
            blockGo.transform.SetParent(listContainer, false);
            bool hasDescription = !string.IsNullOrEmpty(recipe.description);
            blockGo.GetComponent<LayoutElement>().minHeight = hasDescription ? 74f : 58f;

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
            var resultIconRt = UIBuilder.CreateIcon(headerGo.transform, "ResultIcon", 22f, UIBuilder.GetItemCategoryColor(recipe.resultItem), resultLetter);
            // 결과 아이템에 아이콘 스프라이트가 있으면 실제 그림으로, 없으면 기존 문자 placeholder로 표시한다.
            UIBuilder.ApplyItemIcon(resultIconRt, recipe.resultItem);

            var nameLabel = UIBuilder.CreateText(headerGo.transform, "Name", recipe.recipeName, 15, Color.white, TextAnchor.MiddleLeft);
            nameLabel.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;

            var row = new RecipeRow { recipe = recipe, nameLabel = nameLabel };
            row.button = UIBuilder.CreateButton(headerGo.transform, "CraftButton", "제작", () => craftingSystem?.TryCraft(recipe));
            row.button.gameObject.AddComponent<LayoutElement>().preferredWidth = 60f;

            // 버그 수정: CraftingRecipe.description이 그동안 어떤 UI에도 표시되지 않는 죽은 데이터였다.
            // 헤더(이름+버튼) 아래, 재료 칩 위에 작은 회색 글씨로 레시피 설명을 한 줄 보여준다.
            // 설명이 없는 레시피는 이 줄 자체를 만들지 않아 불필요한 빈 공간이 생기지 않게 한다.
            // 재료 줄(materialsHlg)과 동일하게, 패딩을 주는 HorizontalLayoutGroup으로 감싸 위 아이콘과
            // 시작 위치를 맞춘다 (VerticalLayoutGroup 자식은 RectTransform을 직접 만지면 레이아웃이
            // 다시 계산될 때 덮어써지므로, 패딩은 반드시 LayoutGroup의 padding으로 줘야 한다).
            if (hasDescription)
            {
                var descRowGo = new GameObject("DescriptionRow", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
                descRowGo.transform.SetParent(blockGo.transform, false);
                descRowGo.GetComponent<LayoutElement>().minHeight = 14f;
                var descHlg = descRowGo.GetComponent<HorizontalLayoutGroup>();
                descHlg.childForceExpandWidth = true;
                descHlg.childForceExpandHeight = true;
                descHlg.padding = new RectOffset(30, 0, 0, 0);

                UIBuilder.CreateText(descRowGo.transform, "Description", recipe.description, 11,
                    new Color(0.75f, 0.75f, 0.75f, 1f), TextAnchor.UpperLeft);
            }

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

                var chipIconRt = UIBuilder.CreateIcon(chipGo.transform, "Icon", 14f, UIBuilder.GetItemCategoryColor(req.item), "");
                UIBuilder.ApplyItemIcon(chipIconRt, req.item);
                // 개선: 예전에는 "대나무x4"처럼 필요 수량만 보여줘서, 지금 몇 개 들고 있는지 확인하려면
                // 인벤토리를 따로 열어봐야 했다. 이제 "보유/필요"를 나란히 적어 한 줄에서 바로 비교된다.
                var qtyLabel = UIBuilder.CreateText(chipGo.transform, "Qty", $"{req.item.itemName} 0/{req.quantity}", 12, Color.white, TextAnchor.MiddleLeft);
                qtyLabel.gameObject.AddComponent<LayoutElement>().preferredWidth = 110f;

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

                    // 부족한 재료만 Danger Red(#CC3333)로 콕 집어 보여준다(ArtDirection.md 1.1/1.3).
                    chip.label.color = enough ? SatisfiedColor : ShortageColor;

                    // 보유 수량이 실제로 바뀐 프레임에만 "이름 보유/필요" 문자열을 다시 만든다.
                    if (chip.cachedHave != have)
                    {
                        chip.label.text = $"{chip.item.itemName} {have}/{chip.requiredQuantity}";
                        chip.cachedHave = have;
                    }
                }
            }
        }

        /// <summary>
        /// 탈출 목표 섹션을 최신 상태로 다시 그린다. 배 제작 단계별 재료와 경비행기 수리 재료는
        /// 문서(Balance_SceneSnapshot.md)에 실측값이 있지만 **절대 하드코딩하지 않는다** - 씬/프리팹
        /// 직렬화 값이 코드 기본값과 다른 프로젝트라, 화면에 적힌 숫자와 실제 판정이 어긋나면
        /// 그 자체가 버그가 되기 때문이다. 항상 시스템이 들고 있는 설계값을 그대로 읽는다.
        /// 표시 형식: "대나무  투입 2/6 (소지 3)" - 이미 작업대에 투입한 양과 지금 들고 있는 양을 함께 보여준다.
        /// </summary>
        private void RefreshGoals()
        {
            if (goalContainer == null)
                return;

            var inventory = craftingSystem != null ? craftingSystem.inventory : null;
            int usedRows = 0;

            if (boatConstruction != null)
            {
                string stageText = $"배: {boatConstruction.currentStage}/{BoatConstructionSystem.TotalStages}단계"
                    + (boatConstruction.isFullyComplete ? " (완성)"
                        : boatConstruction.hasCurrentStageBlueprint ? "" : " - 도면 필요");
                SetGoalRow(usedRows++, stageText, Color.white);

                if (!boatConstruction.isFullyComplete)
                {
                    foreach (var req in boatConstruction.GetCurrentStageRequirements())
                    {
                        if (req == null || req.item == null)
                            continue;

                        int contributed = boatConstruction.GetCollectedQuantity(req.item);
                        int owned = inventory != null ? inventory.GetItemCount(req.item) : 0;
                        bool enough = contributed + owned >= req.quantity;
                        SetGoalRow(usedRows++,
                            $"   {req.item.itemName}  투입 {contributed}/{req.quantity} (소지 {owned})",
                            enough ? SatisfiedColor : ShortageColor);
                    }
                }
            }

            if (aircraftRepair != null)
            {
                int percent = Mathf.RoundToInt(aircraftRepair.GetOverallProgress() * 100f);
                SetGoalRow(usedRows++,
                    $"경비행기: {percent}%" + (aircraftRepair.isRepairComplete ? " (수리 완료)" : ""),
                    Color.white);

                if (!aircraftRepair.isRepairComplete)
                {
                    foreach (var req in aircraftRepair.requiredMaterials)
                    {
                        if (req == null || req.item == null)
                            continue;

                        int contributed = aircraftRepair.GetCollectedQuantity(req.item);
                        int owned = inventory != null ? inventory.GetItemCount(req.item) : 0;
                        bool enough = contributed + owned >= req.quantity;
                        SetGoalRow(usedRows++,
                            $"   {req.item.itemName}  투입 {contributed}/{req.quantity} (소지 {owned})",
                            enough ? SatisfiedColor : ShortageColor);
                    }
                }
            }

            if (usedRows == 0)
                SetGoalRow(usedRows++, "탈출 진행 정보 없음", BodyGrayColor);

            for (int i = usedRows; i < goalRowPool.Count; i++)
                goalRowPool[i].gameObject.SetActive(false);
        }

        /// <summary>
        /// 탈출 목표 섹션의 index번째 줄에 문구와 색을 채운다. 줄이 모자라면 새로 만들어 재사용 풀에 넣는다.
        /// </summary>
        private void SetGoalRow(int index, string text, Color color)
        {
            while (goalRowPool.Count <= index)
            {
                var line = UIBuilder.CreateText(goalContainer, $"Goal{goalRowPool.Count}", "", 12, Color.white, TextAnchor.MiddleLeft);
                line.gameObject.AddComponent<LayoutElement>().minHeight = 16f;
                goalRowPool.Add(line);
            }

            var row = goalRowPool[index];
            if (!row.gameObject.activeSelf)
                row.gameObject.SetActive(true);
            if (row.text != text)
                row.text = text;
            row.color = color;
        }

        /// <summary>
        /// 패널을 열거나 닫는다. 열리는 순간에는 탈출 목표를 곧바로 다시 계산하도록 타이머를 0으로 만든다.
        /// </summary>
        private void SetOpen(bool open)
        {
            if (panelRoot != null)
                panelRoot.SetActive(open);

            if (open)
                goalRefreshTimer = 0f;
        }
    }
}
