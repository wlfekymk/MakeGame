using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using MakeGame.Data;

namespace MakeGame.UI
{
    /// <summary>
    /// 아이콘 위에 커서를 올렸을 때 뜨는 아이템 정보 패널. 커서를 따라다니되 화면 밖으로 나가지 않게
    /// 잘리는 쪽에서 반대 방향으로 뒤집는다.
    ///
    /// 별도 캔버스(sortOrder 13)를 쓰는 이유: 툴팁은 자기를 띄운 창(인벤토리 sortOrder 10, 제작 10,
    /// 미니맵 목록 11, 전투 피드백 12)보다 반드시 위에 그려져야 하는데, 같은 캔버스 안에서 형제 순서로
    /// 올리면 창 밖으로 삐져나온 부분이 다른 캔버스에 덮인다. 단 모달(설정 16 / 게임오버 20 / 엔딩 21)
    /// 보다는 아래에 둔다 - 그 화면들이 떠 있는 동안 툴팁이 그 위에 남으면 안 된다.
    ///
    /// 아이템 정보는 "그 아이템이 실제로 가진 속성"만 줄로 만든다. 회복량 0인 항목까지 전부 나열하면
    /// 줄 수만 늘고 정작 중요한 한 줄(무기 피해량, 식중독 위험)이 묻힌다.
    /// </summary>
    public class ItemTooltipUI : MonoBehaviour
    {
        /// <summary>툴팁 패널의 고정 폭(px, 1920 기준). 설명이 길어도 이 폭 안에서 줄바꿈된다.</summary>
        private const float PanelWidth = 300f;

        /// <summary>커서와 툴팁 모서리 사이 간격.</summary>
        private const float CursorGap = 18f;

        // 색은 ArtDirection.md 팔레트 안에서만 쓴다(새 색을 만들지 않는다).
        private static readonly Color NeutralGray = new Color(0.8f, 0.8f, 0.8f, 1f);      // #CCCCCC
        private static readonly Color DimGray = new Color(0.55f, 0.55f, 0.55f, 1f);       // #CCCCCC 어둡게(알파 아님, 보조 문구)
        private static readonly Color SunstrokeGold = new Color(0.902f, 0.749f, 0.2f, 1f); // #E6BF33 경고
        private static readonly Color DangerRed = new Color(0.8f, 0.2f, 0.2f, 1f);         // #CC3333
        private static readonly Color MedicGreen = new Color(0.31f, 0.659f, 0.478f, 1f);   // #4FA87A

        private static ItemTooltipUI instance;

        private RectTransform canvasRect;
        private RectTransform panelRt;

        // 머리줄(아이콘 + 이름/분류). ui-toolkit-pt4의 ItemTooltip.uxml 구조를 uGUI로 옮긴 것이다
        // (Docs/Attribution.md). 예전에는 이름·분류가 그냥 첫 두 줄의 글자였는데, 그러면 툴팁이
        // "글자 목록"으로만 읽혀 무엇에 대한 설명인지가 본문과 같은 무게로 묻힌다.
        private RectTransform headerRow;
        private Image headerIcon;
        private Text headerLetter;      // 아이콘 스프라이트가 없을 때의 폴백(격자 칸과 같은 규칙)
        private Text headerName;
        private Text headerCategory;
        private RectTransform headerRule;   // 머리줄과 본문을 가르는 선(.tooltip-description border-top)

        private readonly List<Text> linePool = new List<Text>();
        private int usedLines;
        private bool visible;

        /// <summary>
        /// 툴팁 인스턴스를 가져온다. 없으면(첫 호출이거나 씬이 다시 로드됐으면) 캔버스째로 만든다.
        /// </summary>
        public static ItemTooltipUI GetOrCreate()
        {
            if (instance != null)
                return instance;

            var canvas = UIBuilder.CreateCanvas("ItemTooltipCanvas", sortOrder: 13);

            // 툴팁은 커서를 따라다니기만 하면 되고 클릭 대상이 아니다. 레이캐스터가 살아 있으면
            // 툴팁이 커서 아래 칸을 가려 PointerExit이 즉시 발생하고 툴팁이 깜빡인다(껐다 켜짐 반복).
            var raycaster = canvas.GetComponent<GraphicRaycaster>();
            if (raycaster != null)
                raycaster.enabled = false;

            var created = canvas.gameObject.AddComponent<ItemTooltipUI>();
            created.Build(canvas);
            instance = created;
            return instance;
        }

        private void Build(Canvas canvas)
        {
            canvasRect = canvas.GetComponent<RectTransform>();

            // 모달 패널 알파 규칙(ArtDirection.md 4.3)보다 조금 더 진하게 간다 - 툴팁은 아이콘 격자 위에
            // 겹쳐 뜨기 때문에 0.75로는 뒤의 아이콘이 글자에 비쳐 읽히지 않는다.
            panelRt = UIBuilder.CreatePanel(
                canvas.transform, "TooltipPanel",
                anchorMin: new Vector2(0.5f, 0.5f), anchorMax: new Vector2(0.5f, 0.5f),
                offsetMin: Vector2.zero, offsetMax: Vector2.zero,
                color: UITheme.TooltipBackground,
                addTopBorder: true);

            // 원본은 툴팁을 1px 흰색 알파 0.15 테두리로 두른다. Outline은 그 테두리를 만드는
            // 가장 싼 방법이고(사각 이미지 복사본 4장), useGraphicAlpha를 꺼야 배경 알파가
            // 곱해져 사라지지 않는다 - 격자 칸에서 쓴 것과 같은 수법이다.
            var panelOutline = panelRt.gameObject.AddComponent<Outline>();
            panelOutline.effectColor = UITheme.TooltipBorder;
            panelOutline.effectDistance = new Vector2(1f, 1f);
            panelOutline.useGraphicAlpha = false;

            panelRt.pivot = new Vector2(0f, 1f);

            // CreatePanel이 붙여준 상단 테두리는 앵커로 직접 배치된 장식이다. 아래에서 붙이는
            // VerticalLayoutGroup이 이걸 "첫 번째 줄"로 오해해 높이 0으로 눌러버리지 않게 레이아웃에서 뺀다.
            var border = panelRt.Find("TopBorder");
            if (border != null)
                border.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;

            var layout = panelRt.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(
                UITheme.TooltipPaddingX, UITheme.TooltipPaddingX,
                UITheme.TooltipPaddingY, UITheme.TooltipPaddingY);
            layout.spacing = 3f;
            layout.childAlignment = TextAnchor.UpperLeft;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            // 폭은 LayoutElement로 고정하고 높이만 내용에 맞춰 늘린다(설명이 길면 줄바꿈으로 높아진다).
            var element = panelRt.gameObject.AddComponent<LayoutElement>();
            element.preferredWidth = PanelWidth;

            var fitter = panelRt.gameObject.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            BuildHeader();

            panelRt.gameObject.SetActive(false);
        }

        /// <summary>
        /// 머리줄을 만든다: [48px 아이콘][이름 / 분류]. 원본 ItemTooltip.uss의 수치를 그대로 쓴다
        /// (아이콘 48px · 간격 8px · 이름 14pt · 분류 11pt · 아이콘 배경 검정 알파 0.3 + 1px 테두리).
        /// 세로 배치가 아니라 가로 배치인 것이 핵심이다 - 아이콘과 이름이 한 줄에 붙어 있어야
        /// "이 아이템"이라는 덩어리가 아래 설명 줄들과 다른 무게로 읽힌다.
        /// </summary>
        private void BuildHeader()
        {
            var rowGo = new GameObject("Header", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            rowGo.transform.SetParent(panelRt, false);
            headerRow = rowGo.GetComponent<RectTransform>();

            var rowLayout = rowGo.GetComponent<HorizontalLayoutGroup>();
            rowLayout.spacing = UITheme.TooltipHeaderGap;
            rowLayout.childAlignment = TextAnchor.MiddleLeft;
            rowLayout.childControlWidth = true;
            rowLayout.childControlHeight = true;
            rowLayout.childForceExpandWidth = false;
            rowLayout.childForceExpandHeight = false;

            rowGo.GetComponent<LayoutElement>().minHeight = UITheme.TooltipIconSize;

            // 아이콘 칸: 배경(어두운 판) + 테두리 + 그 안에 비율을 지킨 아이콘.
            var frameGo = new GameObject("IconFrame", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image),
                typeof(Outline), typeof(LayoutElement));
            frameGo.transform.SetParent(headerRow, false);

            var frameImage = frameGo.GetComponent<Image>();
            frameImage.color = new Color(0f, 0f, 0f, 0.3f);
            frameImage.raycastTarget = false;

            var frameOutline = frameGo.GetComponent<Outline>();
            frameOutline.effectColor = UITheme.TooltipBorder;
            frameOutline.effectDistance = new Vector2(1f, 1f);
            frameOutline.useGraphicAlpha = false;

            var frameElement = frameGo.GetComponent<LayoutElement>();
            frameElement.preferredWidth = UITheme.TooltipIconSize;
            frameElement.preferredHeight = UITheme.TooltipIconSize;
            frameElement.flexibleWidth = 0f;

            var iconGo = new GameObject("Icon", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            iconGo.transform.SetParent(frameGo.transform, false);
            var iconRt = iconGo.GetComponent<RectTransform>();
            iconRt.anchorMin = Vector2.zero;
            iconRt.anchorMax = Vector2.one;
            iconRt.offsetMin = new Vector2(4f, 4f);
            iconRt.offsetMax = new Vector2(-4f, -4f);
            headerIcon = iconGo.GetComponent<Image>();
            headerIcon.raycastTarget = false;
            headerIcon.preserveAspect = true;
            headerIcon.enabled = false;

            headerLetter = UIBuilder.CreateText(frameGo.transform, "Letter", "", 22, NeutralGray, TextAnchor.MiddleCenter);
            headerLetter.raycastTarget = false;
            var letterRt = headerLetter.rectTransform;
            letterRt.anchorMin = Vector2.zero;
            letterRt.anchorMax = Vector2.one;
            letterRt.offsetMin = Vector2.zero;
            letterRt.offsetMax = Vector2.zero;
            headerLetter.gameObject.SetActive(false);

            // 글자 칸: 이름(굵게 큰 글씨) 위에 분류(작고 흐리게).
            var textGo = new GameObject("HeaderText", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(LayoutElement));
            textGo.transform.SetParent(headerRow, false);

            var textLayout = textGo.GetComponent<VerticalLayoutGroup>();
            textLayout.spacing = 2f;   // .tooltip-name margin-bottom: 2px
            textLayout.childAlignment = TextAnchor.MiddleLeft;
            textLayout.childControlWidth = true;
            textLayout.childControlHeight = true;
            textLayout.childForceExpandWidth = true;
            textLayout.childForceExpandHeight = false;

            var textElement = textGo.GetComponent<LayoutElement>();
            textElement.flexibleWidth = 1f;
            textElement.preferredWidth = PanelWidth - UITheme.TooltipIconSize - UITheme.TooltipHeaderGap - UITheme.TooltipPaddingX * 2;

            headerName = UIBuilder.CreateText(textGo.transform, "Name", "", UITheme.FontHeading, NeutralGray, TextAnchor.LowerLeft);
            headerName.raycastTarget = false;
            headerName.fontStyle = FontStyle.Bold;

            headerCategory = UIBuilder.CreateText(textGo.transform, "Category", "", 11, DimGray, TextAnchor.UpperLeft);
            headerCategory.raycastTarget = false;

            // 머리줄과 본문을 가르는 선. 원본은 설명 문단의 border-top으로 그리지만, 우리는
            // 설명이 없는 아이템(돌조각 등)이 있어 선을 별도 요소로 두고 그럴 때 끈다.
            headerRule = UIBuilder.CreatePanel(panelRt, "HeaderRule",
                anchorMin: Vector2.zero, anchorMax: Vector2.one,
                offsetMin: Vector2.zero, offsetMax: Vector2.zero,
                color: UITheme.TooltipBorder);

            var ruleElement = headerRule.gameObject.AddComponent<LayoutElement>();
            ruleElement.minHeight = 1f;
            ruleElement.preferredHeight = 1f;
            ruleElement.flexibleHeight = 0f;

            var ruleImage = headerRule.GetComponent<Image>();
            if (ruleImage != null)
                ruleImage.raycastTarget = false;
        }

        /// <summary>
        /// 머리줄을 채운다. 아이콘이 없으면 격자 칸과 같은 폴백(이름 첫 글자)을 쓴다 -
        /// 두 곳이 다른 폴백을 쓰면 같은 아이템이 창마다 다르게 보인다.
        /// </summary>
        private void SetHeader(Sprite icon, string name, string category, Color nameColor)
        {
            headerRow.gameObject.SetActive(true);

            bool hasIcon = icon != null;
            headerIcon.enabled = hasIcon;
            if (hasIcon)
            {
                headerIcon.sprite = icon;
                headerIcon.color = Color.white;
            }

            headerLetter.gameObject.SetActive(!hasIcon);
            if (!hasIcon)
            {
                headerLetter.text = string.IsNullOrEmpty(name) ? "?" : name.Substring(0, 1);
                headerLetter.color = nameColor;
            }

            headerName.text = name;
            headerName.color = nameColor;

            headerCategory.text = category;
            headerCategory.gameObject.SetActive(!string.IsNullOrEmpty(category));
        }

        /// <summary>
        /// 아이템 정보를 채우고 툴팁을 띄운다. count/remaining은 인벤토리의 그 칸이 지금 들고 있는 값이다
        /// (내구도가 제각각인 도구를 같은 종류로 뭉뚱그리지 않기 위해 칸 단위로 받는다).
        /// usageLine에는 호출부가 실제 키(InteractionController가 정한다)로 만든 사용법 힌트를 넘긴다.
        /// </summary>
        public void Show(ItemData data, int count, int remaining, string usageLine, string dropLine)
        {
            // 무언가를 끌고 있는 동안에는 툴팁을 띄우지 않는다(원본 ItemTooltipManipulator와 같은 규칙).
            // 고스트가 이미 커서에 붙어 있는데 툴팁까지 뜨면 정작 놓을 자리가 가려진다.
            if (data == null || UIDragGhost.IsDragging)
            {
                Hide();
                return;
            }

            BeginLines();

            // 머리줄: 아이콘 + 이름 + 분류. 이름은 카테고리 색으로 칠해 격자에서 본 색 띠와 같은
            // 정보를 반복해 주고, 분류 이름은 필터가 가리키는 묶음과 같은 말이라 필터 학습에도 쓰인다.
            SetHeader(data.icon, data.itemName,
                InventoryUI.GetCategoryDisplayName(UIBuilder.GetItemCategory(data)),
                UIBuilder.GetItemCategoryColor(data));

            if (!string.IsNullOrEmpty(data.description))
                AddLine(data.description, 12, NeutralGray);

            // 3) 실제로 가진 속성만. 순서는 "이 아이템을 지금 어떻게 볼 것인가" 기준:
            //    수량/내구도 → 전투 → 회복 → 치료 → 위험/제약.
            // 내구도 판정 순서 주의: ItemData.IsStackable은 `maxUses <= 1`이라 **무제한 도구(칼·물통,
            // maxUses -1)도 true**다. IsUnlimited를 먼저 걸러내지 않으면 칼에 "이 칸 1/20개"가 붙는다.
            if (data.IsUnlimited)
            {
                AddLine("내구도 무제한", 12, MedicGreen);
            }
            else if (!data.IsStackable && data.maxUses > 1)
            {
                float ratio = Mathf.Clamp01((float)remaining / data.maxUses);
                Color wear = ratio <= 0.2f ? DangerRed : ratio <= 0.4f ? SunstrokeGold : NeutralGray;
                AddLine($"남은 사용 {remaining}/{data.maxUses}회", 12, wear);
            }

            // 개수 줄은 실제로 겹쳐 담긴 칸에서만 쓴다. 1개짜리에 "이 칸 1/20개"를 붙이면 줄만 늘어난다.
            if (count > 1 && data.MaxStackSize > 1)
                AddLine($"이 칸 {count}/{data.MaxStackSize}개", 12, count >= data.MaxStackSize ? SunstrokeGold : NeutralGray);

            if (data.isWeapon)
                AddLine($"피해량 {data.weaponDamage:0.#}", 12, NeutralGray);

            if (data.hungerRestoreAmount > 0f)
                AddLine($"허기 +{data.hungerRestoreAmount:0.#}", 12, NeutralGray);

            if (data.thirstRestoreAmount > 0f)
                AddLine($"갈증 +{data.thirstRestoreAmount:0.#}", 12, NeutralGray);

            if (data.curesBleeding)
                AddLine("출혈 치료", 12, MedicGreen);

            if (data.curesPoison)
                AddLine("중독 치료", 12, MedicGreen);

            if (data.curesBrokenBone)
                AddLine("골절 치료", 12, MedicGreen);

            if (data.isPlaceable)
                AddLine("설치형 - 월드에 지을 수 있다", 12, NeutralGray);

            // [식량 루프] 상하지 않는 음식임을 못 박아 준다(훈제육 · 훈제생선 · 비상식량).
            //
            // **지금 이 칸이 얼마나 신선한가**는 여기서 알 수 없다 - 이 메서드는 ItemData만 받고
            // 신선도는 칸(InventoryStack.oldest)의 값이기 때문이다. 그래서 그쪽은 호출부가 사용법
            // 줄(usageLine)에 실어 보내고(InventoryUI.BuildFreshnessText), 여기서는 **종류만 보면
            // 알 수 있는 사실** 하나만 적는다. 두 곳이 같은 판정을 두 벌로 갖지 않게 하려는 것이고,
            // 판정도 새로 만들지 않고 FoodSpoilage.CanSpoil 하나만 부른다.
            //
            // 조건에 hungerRestoreAmount를 함께 거는 이유: CanSpoil은 재료·키트·음료에서도 false라,
            // 이 검사가 없으면 돌조각 툴팁에까지 "상하지 않는다"가 붙는다(의미 없는 줄이다).
            if (data.hungerRestoreAmount > 0f && !MakeGame.Systems.FoodSpoilage.CanSpoil(data))
                AddLine("상하지 않는다 - 오래 보관할 수 있다", 12, MedicGreen);

            // 경고 계열은 마지막에 모아 눈에 걸리게 한다.
            if (data.isRawFood)
                AddLine("생식품 - 익히지 않고 먹으면 식중독 위험", 12, SunstrokeGold);

            if (data.isCoconutWaterSource)
                AddLine("과음하면 설사로 갈증이 더 나빠진다", 12, SunstrokeGold);

            if (data.blockedFromLargeIslandsByCurrent)
                AddLine("특대 섬 해류는 모터 단 대양 규격 뗏목이라야 뚫는다", 12, SunstrokeGold);

            if (!string.IsNullOrEmpty(usageLine))
                AddLine(usageLine, 12, MedicGreen);

            if (!string.IsNullOrEmpty(dropLine))
                AddLine(dropLine, 11, DimGray);

            EndLines();
            Present();
        }

        /// <summary>
        /// 제작법 하나를 설명하는 툴팁을 띄운다. Show(아이템)와 **같은 패널·같은 줄 풀·같은 위치
        /// 추적**을 쓰고, 다른 것은 어떤 줄을 채우느냐뿐이다(제작 창을 위해 툴팁을 따로 만들지 않는다).
        ///
        /// 재료 줄 형식은 "이름 보유/필요"로, 프로젝트의 다른 표시(제작 재료 칩, 탈출 목표 줄)와 같다.
        /// 부족한 재료는 Danger Red로 칠하고 **부족한 개수까지 적는다** - "2/4"만으로는 어느 쪽이
        /// 보유고 어느 쪽이 필요인지 읽는 사람마다 갈리기 때문이다.
        ///
        /// 보유 수량은 inventory에서 직접 읽는다. 호출부가 미리 계산해 넘기면 "제작 창이 계산한 값"과
        /// "CraftingSystem.CanCraft가 보는 값"이 갈릴 수 있고, 그러면 툴팁은 충분하다는데 버튼이
        /// 안 눌리는 상태가 만들어진다.
        /// </summary>
        public void ShowRecipe(CraftingRecipe recipe, MakeGame.Player.PlayerInventory inventory, int skillLevel, string actionLine)
        {
            if (recipe == null)
            {
                Hide();
                return;
            }

            BeginLines();

            var result = recipe.resultItem;

            // 머리줄은 아이템 툴팁과 **같은 것**을 쓴다(제작법용 머리줄을 따로 만들지 않는다).
            // 이름은 결과물 카테고리 색, 아래 작은 줄은 결과물 분류 + 한 번에 몇 개가 나오는지다
            // (1개면 적지 않는다 - 정보가 없다).
            string headline = !string.IsNullOrEmpty(recipe.recipeName)
                ? recipe.recipeName
                : (result != null ? result.itemName : "이름 없는 제작법");

            string categoryLine = "";
            if (result != null)
            {
                categoryLine = InventoryUI.GetCategoryDisplayName(UIBuilder.GetItemCategory(result));
                if (recipe.resultQuantity > 1)
                    categoryLine += $" · 한 번에 {recipe.resultQuantity}개";
            }

            SetHeader(result != null ? result.icon : null, headline, categoryLine,
                result != null ? UIBuilder.GetItemCategoryColor(result) : NeutralGray);

            if (!string.IsNullOrEmpty(recipe.description))
                AddLine(recipe.description, 12, NeutralGray);

            AddLine("필요 재료", 11, DimGray);

            bool anyMaterial = false;
            var materials = recipe.requiredMaterials;
            for (int i = 0; materials != null && i < materials.Count; i++)
            {
                var requirement = materials[i];
                if (requirement == null || requirement.item == null)
                    continue;

                anyMaterial = true;
                int have = inventory != null ? inventory.GetItemCount(requirement.item) : 0;

                if (have >= requirement.quantity)
                {
                    AddLine($"{requirement.item.itemName} {have}/{requirement.quantity}", 12, MedicGreen);
                }
                else
                {
                    AddLine($"{requirement.item.itemName} {have}/{requirement.quantity} ({requirement.quantity - have}개 부족)", 12, DangerRed);
                }
            }

            if (!anyMaterial)
                AddLine("재료가 필요 없다", 12, MedicGreen);

            // 스킬 부족은 재료와 성격이 다른 잠금이라(모아도 안 풀린다) 재료 아래에 따로 적는다.
            if (recipe.requiredSkillLevel > skillLevel)
                AddLine($"제작 기술 Lv{recipe.requiredSkillLevel} 필요 (지금 Lv{skillLevel})", 12, SunstrokeGold);

            if (!string.IsNullOrEmpty(actionLine))
                AddLine(actionLine, 11, DimGray);

            EndLines();
            Present();
        }

        /// <summary>
        /// 줄을 다 채운 뒤 공통으로 하는 일: 켜고, 이번 프레임의 실제 크기를 확정시키고, 커서 위치에
        /// 맞춘다. 위치를 잡으려면 크기가 먼저 확정돼야 해서 레이아웃을 즉시 다시 만든다.
        /// </summary>
        private void Present()
        {
            panelRt.gameObject.SetActive(true);
            visible = true;

            LayoutRebuilder.ForceRebuildLayoutImmediate(panelRt);
            FollowCursor();
        }

        /// <summary>툴팁을 숨긴다. 이미 숨겨져 있으면 아무 일도 하지 않는다.</summary>
        public void Hide()
        {
            if (!visible)
                return;

            visible = false;
            if (panelRt != null)
                panelRt.gameObject.SetActive(false);
        }

        /// <summary>
        /// 커서를 따라간다. Time.timeScale이 0인 화면 위에서도 멈추면 안 되므로 시간에 의존하는 계산은
        /// 쓰지 않는다(위치는 매 프레임 커서 좌표에서 직접 만든다).
        /// </summary>
        private void LateUpdate()
        {
            if (visible)
                FollowCursor();
        }

        private void FollowCursor()
        {
            if (canvasRect == null || panelRt == null)
                return;

            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, Input.mousePosition, null, out var cursor))
                return;

            Vector2 size = panelRt.rect.size;
            float halfW = canvasRect.rect.width * 0.5f;
            float halfH = canvasRect.rect.height * 0.5f;

            // 기본은 커서 오른쪽 아래. 그 방향이 화면을 벗어나면 반대쪽으로 뒤집고, 그래도 남으면 자른다
            // (뒤집기를 먼저 하는 이유: 그냥 자르면 툴팁이 커서 밑에 깔려 아이콘을 가린다).
            Vector2 position = new Vector2(cursor.x + CursorGap, cursor.y - CursorGap);

            if (position.x + size.x > halfW)
                position.x = cursor.x - CursorGap - size.x;

            if (position.y - size.y < -halfH)
                position.y = cursor.y + CursorGap + size.y;

            position.x = Mathf.Clamp(position.x, -halfW, Mathf.Max(-halfW, halfW - size.x));
            position.y = Mathf.Clamp(position.y, Mathf.Min(halfH, -halfH + size.y), halfH);

            panelRt.anchoredPosition = position;
        }

        private void BeginLines()
        {
            usedLines = 0;
        }

        /// <summary>
        /// 한 줄을 추가한다. 갱신마다 Text 오브젝트를 새로 만들지 않고 풀에서 재사용한다
        /// (툴팁은 커서가 칸을 옮길 때마다 다시 채워지므로 생성 비용이 그대로 노출된다).
        /// </summary>
        private void AddLine(string content, int fontSize, Color color)
        {
            Text line;
            if (usedLines < linePool.Count)
            {
                line = linePool[usedLines];
            }
            else
            {
                line = UIBuilder.CreateText(panelRt, $"Line{linePool.Count}", "", fontSize, color, TextAnchor.UpperLeft);
                line.raycastTarget = false;
                linePool.Add(line);
            }

            line.gameObject.SetActive(true);
            line.text = content;
            line.fontSize = fontSize;
            line.color = color;
            usedLines++;
        }

        private void EndLines()
        {
            for (int i = usedLines; i < linePool.Count; i++)
                linePool[i].gameObject.SetActive(false);

            // 본문이 한 줄도 없으면 구분선을 끈다. 아무것도 가르지 않는 선은 그냥 얼룩이다.
            if (headerRule != null)
                headerRule.gameObject.SetActive(usedLines > 0);
        }
    }
}
