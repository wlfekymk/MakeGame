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
                color: new Color(0.04f, 0.04f, 0.04f, 0.94f),
                addTopBorder: true);

            panelRt.pivot = new Vector2(0f, 1f);

            // CreatePanel이 붙여준 상단 테두리는 앵커로 직접 배치된 장식이다. 아래에서 붙이는
            // VerticalLayoutGroup이 이걸 "첫 번째 줄"로 오해해 높이 0으로 눌러버리지 않게 레이아웃에서 뺀다.
            var border = panelRt.Find("TopBorder");
            if (border != null)
                border.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;

            var layout = panelRt.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(12, 12, 10, 10);
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

            panelRt.gameObject.SetActive(false);
        }

        /// <summary>
        /// 아이템 정보를 채우고 툴팁을 띄운다. count/remaining은 인벤토리의 그 칸이 지금 들고 있는 값이다
        /// (내구도가 제각각인 도구를 같은 종류로 뭉뚱그리지 않기 위해 칸 단위로 받는다).
        /// usageLine에는 호출부가 실제 키(InteractionController가 정한다)로 만든 사용법 힌트를 넘긴다.
        /// </summary>
        public void Show(ItemData data, int count, int remaining, string usageLine, string dropLine)
        {
            if (data == null)
            {
                Hide();
                return;
            }

            BeginLines();

            // 1) 이름 - 카테고리 색으로 칠해 격자에서 본 색 띠와 같은 정보를 반복해 준다.
            AddLine(data.itemName, 16, UIBuilder.GetItemCategoryColor(data));

            // 2) 분류 이름(작게). 필터가 어떤 묶음을 가리키는지와 같은 말이라 필터 학습에도 도움이 된다.
            AddLine(InventoryUI.GetCategoryDisplayName(UIBuilder.GetItemCategory(data)), 11, DimGray);

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

            // 경고 계열은 마지막에 모아 눈에 걸리게 한다.
            if (data.isRawFood)
                AddLine("생식품 - 익히지 않고 먹으면 식중독 위험", 12, SunstrokeGold);

            if (data.isCoconutWaterSource)
                AddLine("과음하면 설사로 갈증이 더 나빠진다", 12, SunstrokeGold);

            if (data.blockedFromLargeIslandsByCurrent)
                AddLine("해류가 강한 큰 섬에는 들고 갈 수 없다", 12, SunstrokeGold);

            if (!string.IsNullOrEmpty(usageLine))
                AddLine(usageLine, 12, MedicGreen);

            if (!string.IsNullOrEmpty(dropLine))
                AddLine(dropLine, 11, DimGray);

            EndLines();

            panelRt.gameObject.SetActive(true);
            visible = true;

            // 위치를 잡으려면 이번 프레임의 실제 크기가 필요하다. 레이아웃을 즉시 확정시킨다.
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
        }
    }
}
