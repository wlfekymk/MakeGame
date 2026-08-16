using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

namespace MakeGame.UI
{
    /// <summary>
    /// 런타임에 코드로 uGUI 요소(캔버스/패널/텍스트/버튼)를 만들어내는 공용 헬퍼.
    /// 이 게임의 다른 시스템들(스포너 등)처럼 씬에 미리 배치하지 않고 필요한 UI를 스스로 생성하는 방식을 위해 사용한다.
    /// </summary>
    public static class UIBuilder
    {
        // ────────────────────────────────────────────────────────────────────────
        // 창(윈도) 표준 팔레트. InventoryUI(B19)가 확립한 격자 창 표준의 색을 여기 한 곳에
        // 모아 두고, 창을 만드는 모든 화면이 같은 값을 참조한다. 창마다 색을 다시 적으면
        // 한쪽만 고쳐졌을 때 "인벤토리는 안 비치는데 제작 창은 비친다" 같은 어긋남이 생긴다.
        // ────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// 창 배경색. 알파 0.93은 **뒤의 HUD 글자가 비쳐 읽히지 않게** 하기 위한 값이다
        /// (ArtDirection 4.3의 0.75는 짧게 뜨는 알림/확인 패널 기준이고, 정보 밀도가 높은
        /// 창에는 부족하다 - 실기에서 격자 사이로 HUD 막대가 그대로 읽혔다).
        /// </summary>
        public static readonly Color WindowBackgroundColor = new Color(0.04f, 0.05f, 0.06f, 0.93f);

        /// <summary>제목 표시줄 배경(창 배경 위에 아주 옅게 얹는 띠).</summary>
        public static readonly Color WindowTitleBarColor = new Color(1f, 1f, 1f, 0.07f);

        // 격자 칸 배경 3단계. 색상이 아니라 **밝기**로만 구분해 색맹 대응과 야간 가독성을 함께 지킨다.
        public static readonly Color SlotEmptyColor = new Color(1f, 1f, 1f, 0.04f);
        public static readonly Color SlotFilledColor = new Color(1f, 1f, 1f, 0.09f);
        public static readonly Color SlotHoverColor = new Color(1f, 1f, 1f, 0.2f);

        /// <summary>Danger Red #CC3333 (ArtDirection.md 1.3).</summary>
        public static readonly Color DangerRed = new Color(0.8f, 0.2f, 0.2f, 1f);

        /// <summary>Medic Green #4FA87A (ArtDirection.md 1.1 - UI/아이콘 전용).</summary>
        public static readonly Color MedicGreen = new Color(0.31f, 0.659f, 0.478f, 1f);

        /// <summary>
        /// 화면 전체를 덮는 Screen Space Overlay 캔버스를 새로 생성한다.
        /// 씬에 EventSystem이 없으면(버튼 클릭 등 UI 입력 처리에 필요) 함께 생성한다.
        /// </summary>
        public static Canvas CreateCanvas(string name, int sortOrder = 0)
        {
            var go = new GameObject(name, typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = sortOrder;

            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);

            EnsureEventSystem();
            return canvas;
        }

        /// <summary>
        /// 씬에 EventSystem이 하나도 없으면 새로 생성한다. 버튼 클릭 등 uGUI 입력 처리에 반드시 필요하다.
        /// </summary>
        public static void EnsureEventSystem()
        {
            if (Object.FindAnyObjectByType<EventSystem>() != null)
                return;

            new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
        }

        /// <summary>
        /// 지정한 앵커/오프셋 범위에 단색 배경 패널(Image)을 생성한다.
        /// 개선(B4-14, ArtDirection.md 4.3): addTopBorder를 true로 주면 패널 상단에 두께 2px, 흰색
        /// 알파 12%인 얇은 선을 추가해 "이것은 카드형 패널이다"라는 시각적 신호를 준다. 기본값은
        /// false로 두어(하위 호환), 슬라이더 트랙/핸들처럼 CreatePanel을 내부 부품 조립에 재사용하는
        /// 곳이나 화면 전체를 덮는 투명/반투명 배경 오버레이(카드가 아닌 배경 그 자체)에는 실수로
        /// 테두리가 그려지지 않게 했다 - 실제 "카드형" 패널(HUD/레이더/목록/인벤토리/제작 패널 등)을
        /// 만드는 호출부에서만 명시적으로 true를 넘긴다.
        /// </summary>
        public static RectTransform CreatePanel(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax, Color color, bool addTopBorder = false)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(parent, false);

            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.offsetMin = offsetMin;
            rt.offsetMax = offsetMax;

            go.GetComponent<Image>().color = color;

            if (addTopBorder)
                CreateTopBorder(rt);

            return rt;
        }

        /// <summary>
        /// 패널 상단에 두께 2px, 색 #FFFFFF 알파 12%인 얇은 선을 하나 붙인다(ArtDirection.md 4.3).
        /// 패널마다 다른 강조색을 넣지 않고 이 값 하나로 고정해, 화면 전체의 "카드형 패널" 신호를 통일한다.
        /// </summary>
        private static void CreateTopBorder(RectTransform panelRt)
        {
            var borderGo = new GameObject("TopBorder", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            borderGo.transform.SetParent(panelRt, false);

            var borderRt = borderGo.GetComponent<RectTransform>();
            borderRt.anchorMin = new Vector2(0f, 1f);
            borderRt.anchorMax = new Vector2(1f, 1f);
            borderRt.pivot = new Vector2(0.5f, 1f);
            borderRt.sizeDelta = new Vector2(0f, 2f);
            borderRt.anchoredPosition = Vector2.zero;

            borderGo.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.12f);
        }

        /// <summary>
        /// 텍스트 오브젝트를 생성한다. 부모의 레이아웃 그룹 안에서 쓸 수도, 절대 위치로 배치할 수도 있다.
        /// </summary>
        public static Text CreateText(Transform parent, string name, string content, int fontSize, Color color, TextAnchor alignment)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            go.transform.SetParent(parent, false);

            var text = go.GetComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.text = content;
            text.fontSize = fontSize;
            text.color = color;
            text.alignment = alignment;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            return text;
        }

        /// <summary>
        /// 아이템을 시각적으로 구분하기 위한 작은 정사각형 "아이콘"을 생성한다.
        /// 실제 아이콘 이미지 에셋이 없으므로, 카테고리별 색상 배경 + 아이템 이름 첫 글자로 대체 표시한다.
        /// LayoutElement가 붙어 있어 HorizontalLayoutGroup 등 레이아웃 그룹 안에서 고정 크기로 배치된다.
        /// </summary>
        public static RectTransform CreateIcon(Transform parent, string name, float size, Color backgroundColor, string letter)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(LayoutElement));
            go.transform.SetParent(parent, false);

            go.GetComponent<Image>().color = backgroundColor;

            var layoutElement = go.GetComponent<LayoutElement>();
            layoutElement.preferredWidth = size;
            layoutElement.preferredHeight = size;
            layoutElement.minWidth = size;
            layoutElement.minHeight = size;

            if (!string.IsNullOrEmpty(letter))
            {
                var label = CreateText(go.transform, "Letter", letter, Mathf.RoundToInt(size * 0.55f), Color.white, TextAnchor.MiddleCenter);
                var labelRt = label.rectTransform;
                labelRt.anchorMin = Vector2.zero;
                labelRt.anchorMax = Vector2.one;
                labelRt.offsetMin = Vector2.zero;
                labelRt.offsetMax = Vector2.zero;
            }

            return go.GetComponent<RectTransform>();
        }

        /// <summary>
        /// CreateIcon으로 만든 아이콘에 ItemData.icon 스프라이트를 적용한다.
        /// 아이템에 아이콘 이미지가 있으면 실제 그림으로 바꾸고(색은 흰색, 문자 placeholder는 숨김),
        /// 없으면 아무것도 하지 않아 CreateIcon이 만들어둔 카테고리 색상 + 문자 placeholder가 그대로 남는다.
        /// (CraftingUI처럼 한 번만 만들고 다시 갱신하지 않는 정적인 아이콘에서 사용)
        /// </summary>
        public static void ApplyItemIcon(RectTransform iconRt, MakeGame.Data.ItemData item)
        {
            if (iconRt == null || item == null || item.icon == null)
                return;

            var image = iconRt.GetComponent<Image>();
            if (image != null)
            {
                image.sprite = item.icon;
                image.color = Color.white;
                image.type = Image.Type.Simple;
                image.preserveAspect = true;
            }

            var letter = iconRt.Find("Letter");
            if (letter != null)
                letter.gameObject.SetActive(false);
        }

        /// <summary>
        /// 아이템 분류 카테고리. 인벤토리 UI의 정렬/필터 순환 순서와 아이콘 배경색 규칙이 함께 이 값을 기준으로 삼는다.
        /// 개선(#9): 이전에는 InventoryUI.GetCategory와 UIBuilder.GetItemCategoryColor가 동일한 우선순위 판정
        /// 로직을 각자 구현하고 있어, 한쪽만 고치면 조용히 어긋날 위험이 있었다. 이 열거형과 GetItemCategory를
        /// 단일 소스로 두고 양쪽 모두 이걸 참조하게 통합했다. 필터 인덱스 계산(InventoryUI의
        /// (ItemCategory)(currentFilterIndex - 1))이 정수값에 의존하므로, 각 값의 순서/번호는 절대 바꾸면 안 된다.
        /// </summary>
        public enum ItemCategory
        {
            Weapon = 0,
            Cure = 1,
            Food = 2,
            Drink = 3,
            Placeable = 4,
            Vehicle = 5,
            Material = 6,
        }

        /// <summary>
        /// 아이템 하나의 분류 카테고리를 판정한다. GetItemCategoryColor(색상 규칙)와 InventoryUI(정렬/필터)가
        /// 공통으로 사용하는 단일 판정 로직이다. item이 null인 호출은 이 프로젝트 안에서 나오지 않으므로
        /// (호출부가 미리 null 체크를 하거나 null이 아님을 보장) 방어적으로만 Material로 폴백한다.
        /// </summary>
        public static ItemCategory GetItemCategory(MakeGame.Data.ItemData item)
        {
            if (item == null)
                return ItemCategory.Material;

            if (item.isWeapon)
                return ItemCategory.Weapon;

            // 버그 수정: 붕대/부목/해독제 같은 치료 아이템이 별도 분류가 없어 전부 맨 아래
            // "일반 채집 재료"로 취급됐다. 별도 카테고리로 분리해 인벤토리 UI 카테고리 점과
            // 아이콘 배경 색이 일치하게 했다.
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
        /// 아이템의 종류(무기/음식/음료/설치형/일반 재료)에 따라 아이콘 배경색을 정한다.
        /// 실제 아이콘 이미지가 없는 상태에서 최소한의 시각적 구분을 주기 위한 임시 규칙이다.
        /// 분류 판정 자체는 GetItemCategory에 위임하고, 여기서는 카테고리별 색상만 담당한다.
        /// </summary>
        public static Color GetItemCategoryColor(MakeGame.Data.ItemData item)
        {
            if (item == null)
                return new Color(0.5f, 0.5f, 0.5f, 1f);

            switch (GetItemCategory(item))
            {
                case ItemCategory.Weapon:
                    // 개선(ArtDirection.md 1.3): "위급/전투"를 뜻하는 빨강이 코드 곳곳에서 4종
                    // (#CC4040/#D93333/#CC1A1A/#FF2626)으로 흩어져 있던 것을 Danger Red #CC3333로 통일.
                    return new Color(0.8f, 0.2f, 0.2f, 1f); // Danger Red #CC3333: 무기

                case ItemCategory.Cure:
                    return new Color(0.31f, 0.66f, 0.48f, 1f); // 초록: 의료 아이템

                case ItemCategory.Food:
                    return new Color(0.85f, 0.55f, 0.2f, 1f); // 주황: 음식

                case ItemCategory.Drink:
                    return new Color(0.25f, 0.55f, 0.85f, 1f); // 파랑: 음료

                case ItemCategory.Placeable:
                    return new Color(0.3f, 0.7f, 0.6f, 1f); // 청록: 설치형(빌드) 아이템

                case ItemCategory.Vehicle:
                    return new Color(0.6f, 0.5f, 0.85f, 1f); // 보라: 이동 수단(고무보트 등)

                default:
                    // 품질 개선(#327): 나뭇가지~엔진부품까지 8종이 넘는 "일반 재료"가 전부 똑같은 갈색
                    // 한 가지로만 표시돼, 인벤토리/제작 UI에서 실루엣 아이콘이 아니면 재료 계열을 구분할
                    // 방법이 없었다. 재질 계열(나무/돌/금속/식물·천/기계부품)별로 색조를 살짝 나눠서
                    // IslandResourceSpawner가 월드에 심는 실제 오브젝트 색상과 동일한 기준으로 일치시켰다.
                    return GetMaterialSubCategoryColor(item.itemName);
            }
        }

        /// <summary>
        /// "일반 재료" 대분류 안에서 재질 계열별로 세분화된 색상을 반환한다. 이름에 재질 키워드가
        /// 없는 새 재료가 추가되면 기존 갈색으로 안전하게 폴백한다. IslandResourceSpawner의
        /// GetSurfaceTextureName과 동일한 재질 그룹 기준(나무/돌/금속/식물)을 그대로 따라가되
        /// 여기서는 색상만 담당한다(질감 텍스처는 스포너 쪽 책임).
        /// </summary>
        private static Color GetMaterialSubCategoryColor(string itemName)
        {
            if (string.IsNullOrEmpty(itemName))
                return new Color(0.55f, 0.45f, 0.35f, 1f); // 갈색: 기본값

            if (itemName.Contains("나뭇가지") || itemName.Contains("대나무"))
                return new Color(0.55f, 0.4f, 0.25f, 1f); // 짙은 갈색: 목재 계열

            if (itemName.Contains("돌조각") || itemName.Contains("부싯돌"))
                return new Color(0.5f, 0.5f, 0.52f, 1f); // 회색: 석재 계열

            if (itemName.Contains("금속조각") || itemName.Contains("엔진부품"))
                return new Color(0.45f, 0.5f, 0.58f, 1f); // 강청회색: 금속/기계 부품 계열

            if (itemName.Contains("야자잎") || itemName.Contains("천조각"))
                return new Color(0.58f, 0.55f, 0.3f, 1f); // 올리브: 식물/섬유 계열

            if (itemName.Contains("코코넛"))
                return new Color(0.5f, 0.38f, 0.22f, 1f); // 갈색-크림: 열매 계열

            if (itemName.Contains("부력통") || itemName.Contains("비상식량") || itemName.Contains("연료"))
                return new Color(0.35f, 0.45f, 0.4f, 1f); // 군용 카키그린: 표류 보급품 계열

            return new Color(0.55f, 0.45f, 0.35f, 1f); // 갈색: 그 외 미분류 재료 기본값
        }

        /// <summary>
        /// 체력/허기/갈증처럼 0~1 비율로 변하는 수치를 가로 막대(배경 + 채움 이미지)로 표시하는
        /// 프로그레스 바를 생성한다. 반환되는 Fill Image의 fillAmount(0~1)를 매 프레임 갱신하면
        /// 막대가 늘고 줄어드는 것처럼 보인다. HUD처럼 raw 숫자보다 한눈에 들어오는 시각 표시가
        /// 필요한 곳에서 쓴다.
        /// 퀄리티 개선: 예전엔 각진 사각형 막대라 밋밋했다. 배경에 둥근 캡슐 9-slice 스프라이트
        /// (bar_rounded)를 씌우고 Mask 컴포넌트를 달아, 안쪽 Fill 이미지가 그 캡슐 모양 그대로
        /// 잘려서 보이게 했다(Fill 자체는 Filled 타입이라 9-slice를 직접 지원하지 않기 때문).
        /// </summary>
        public static Image CreateProgressBar(Transform parent, string name, Color backgroundColor, Color fillColor)
        {
            var bgGo = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Mask));
            bgGo.transform.SetParent(parent, false);
            var bgImage = bgGo.GetComponent<Image>();
            bgImage.color = backgroundColor;

            var barSprite = Resources.Load<Sprite>("Sprites/bar_rounded");
            if (barSprite != null)
            {
                bgImage.sprite = barSprite;
                bgImage.type = Image.Type.Sliced;
            }

            var mask = bgGo.GetComponent<Mask>();
            mask.showMaskGraphic = true; // 배경(트랙)도 그대로 보이면서, 자식 Fill만 이 캡슐 모양으로 클리핑한다

            var fillGo = new GameObject("Fill", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            fillGo.transform.SetParent(bgGo.transform, false);
            var fillRt = fillGo.GetComponent<RectTransform>();
            fillRt.anchorMin = Vector2.zero;
            fillRt.anchorMax = Vector2.one;
            fillRt.offsetMin = Vector2.zero;
            fillRt.offsetMax = Vector2.zero;

            var fillImage = fillGo.GetComponent<Image>();
            fillImage.color = fillColor;
            fillImage.type = Image.Type.Filled;
            fillImage.fillMethod = Image.FillMethod.Horizontal;
            fillImage.fillOrigin = (int)Image.OriginHorizontal.Left;
            fillImage.fillAmount = 1f;

            return fillImage;
        }

        /// <summary>
        /// 클릭 시 콜백을 실행하는 버튼을 생성한다 (배경 + 가운데 정렬 라벨 텍스트 포함).
        /// 개선(B4-14, ArtDirection.md 4.3): interactable=false일 때 Unity 기본 ColorBlock이 회색
        /// disabledColor로 자동 전환하던 것을, 우리 팔레트 안에 머물도록 버튼 기본색의 알파 40%로
        /// 명시 설정했다. normal/highlighted/pressed 등 나머지 상태색은 Button 기본값(흰 배경 곱연산)을
        /// 그대로 두고 disabledColor만 바꾼다.
        /// </summary>
        public static Button CreateButton(Transform parent, string name, string label, UnityEngine.Events.UnityAction onClick)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);

            Color baseColor = new Color(0.25f, 0.55f, 0.3f, 1f);
            go.GetComponent<Image>().color = baseColor;

            var button = go.GetComponent<Button>();
            if (onClick != null)
                button.onClick.AddListener(onClick);

            var colors = button.colors;
            Color disabledColor = baseColor;
            disabledColor.a = 0.4f;
            colors.disabledColor = disabledColor;
            button.colors = colors;

            var labelText = CreateText(go.transform, "Label", label, 16, Color.white, TextAnchor.MiddleCenter);
            var labelRt = labelText.rectTransform;
            labelRt.anchorMin = Vector2.zero;
            labelRt.anchorMax = Vector2.one;
            labelRt.offsetMin = Vector2.zero;
            labelRt.offsetMax = Vector2.zero;

            return button;
        }

        /// <summary>
        /// 가로형 슬라이더(배경 트랙 + 채움 + 핸들)를 생성한다. 개선(B2-13): SettingsMenuController가
        /// OnGUI의 GUI.HorizontalSlider 대신 쓸 정식 UGUI Slider가 필요해 추가했다. Unity 기본 UI 프리팹의
        /// Slider 구조(Background/Fill Area/Fill, Handle Slide Area/Handle)를 코드로 그대로 재현한다.
        /// 반환된 Slider의 value를 그대로 읽거나 onValueChanged를 구독해서 쓴다.
        /// </summary>
        public static Slider CreateSlider(Transform parent, string name, float minValue, float maxValue, float value,
            Color trackColor, Color fillColor, Color handleColor)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Slider));
            go.transform.SetParent(parent, false);

            var slider = go.GetComponent<Slider>();
            slider.minValue = minValue;
            slider.maxValue = maxValue;
            slider.wholeNumbers = false;
            slider.direction = Slider.Direction.LeftToRight;

            // 배경(트랙): 슬라이더 세로 중앙에 얇게 깔린다.
            var bgRt = CreatePanel(go.transform, "Background",
                new Vector2(0f, 0.25f), new Vector2(1f, 0.75f), Vector2.zero, Vector2.zero, trackColor);

            // Fill Area: 안쪽 여백을 살짝 두고, 그 안의 Fill을 Slider 컴포넌트가 값에 따라 자동으로 늘였다 줄인다.
            var fillAreaGo = new GameObject("Fill Area", typeof(RectTransform));
            fillAreaGo.transform.SetParent(go.transform, false);
            var fillAreaRt = fillAreaGo.GetComponent<RectTransform>();
            fillAreaRt.anchorMin = new Vector2(0f, 0.25f);
            fillAreaRt.anchorMax = new Vector2(1f, 0.75f);
            fillAreaRt.offsetMin = new Vector2(5f, 0f);
            fillAreaRt.offsetMax = new Vector2(-5f, 0f);

            var fillRt = CreatePanel(fillAreaRt, "Fill", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, fillColor);

            // Handle Slide Area: 핸들이 좌우로 오갈 수 있는 전체 폭. Handle은 고정 너비로 세로만 꽉 채운다.
            var handleAreaGo = new GameObject("Handle Slide Area", typeof(RectTransform));
            handleAreaGo.transform.SetParent(go.transform, false);
            var handleAreaRt = handleAreaGo.GetComponent<RectTransform>();
            handleAreaRt.anchorMin = Vector2.zero;
            handleAreaRt.anchorMax = Vector2.one;
            handleAreaRt.offsetMin = new Vector2(10f, 0f);
            handleAreaRt.offsetMax = new Vector2(-10f, 0f);

            var handleRt = CreatePanel(handleAreaGo.transform, "Handle",
                new Vector2(0f, 0f), new Vector2(0f, 1f), Vector2.zero, Vector2.zero, handleColor);
            handleRt.sizeDelta = new Vector2(16f, 0f);
            handleRt.pivot = new Vector2(0.5f, 0.5f);

            slider.fillRect = fillRt;
            slider.handleRect = handleRt;
            slider.targetGraphic = handleRt.GetComponent<Image>();

            // value는 fillRect/handleRect를 다 연결한 뒤에 설정해야 슬라이더가 시각적으로도 올바른 위치에서 시작한다.
            slider.value = value;

            return slider;
        }

        // ────────────────────────────────────────────────────────────────────────
        // 창(윈도) 표준 부품
        //
        // InventoryUI(B19)가 세운 "창 UI 표준"을 다른 창(제작·퀘스트 등)이 **코드를 복사하지 않고**
        // 그대로 쓰도록 공용 팩토리로 뽑아낸 것이다. 표준의 다섯 요소 중 네 개가 여기 있다:
        //   1) 알파 0.93 어두운 패널        → CreateWindow
        //   2) 제목 표시줄 + 빨간 X 닫기    → CreateTitleBar / CreateCloseButton
        //   3) 마우스로 창 이동             → AttachDragHandle (UIDragHandle)
        //   4) 아이콘 + 우하단 개수 격자 칸 → CreateItemSlot (InventorySlotView)
        // 나머지 하나(hover 툴팁)는 ItemTooltipUI가 이미 창과 무관한 단일 인스턴스라 그대로 쓰면 된다.
        //
        // 기존 호출부에 영향이 없도록 **추가만** 했다. 기존 시그니처는 하나도 건드리지 않았다.
        // ────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// 표준 창 패널을 만든다. 화면 한쪽에 못 박지 않고 **한 점 앵커 + 고정 크기**로 만들기 때문에
        /// 드래그가 anchoredPosition 하나만 움직이면 되고, UIDragHandle의 클램프 계산도 그 전제를 쓴다
        /// (pivot (0.5, 1) = position.y가 창의 위쪽 모서리).
        /// </summary>
        public static RectTransform CreateWindow(Transform parent, string name, float width, float height)
        {
            var rt = CreatePanel(parent, name,
                anchorMin: new Vector2(0.5f, 0.5f), anchorMax: new Vector2(0.5f, 0.5f),
                offsetMin: Vector2.zero, offsetMax: Vector2.zero,
                color: WindowBackgroundColor,
                addTopBorder: true);

            rt.pivot = new Vector2(0.5f, 1f);
            rt.sizeDelta = new Vector2(width, height);
            return rt;
        }

        /// <summary>
        /// 창 위쪽에 제목 표시줄을 붙이고 그 RectTransform을 돌려준다. 제목 글자는 raycastTarget을
        /// 꺼서 드래그 입력을 가로채지 않게 한다(입력은 표시줄 자체가 받는다). 오른쪽 40px은
        /// 닫기(X) 버튼 자리로 비워 둔다.
        /// </summary>
        public static RectTransform CreateTitleBar(RectTransform window, string title, float height)
        {
            var bar = CreatePanel(window, "TitleBar",
                anchorMin: new Vector2(0f, 1f), anchorMax: new Vector2(1f, 1f),
                offsetMin: new Vector2(0f, -height), offsetMax: Vector2.zero,
                color: WindowTitleBarColor);

            var text = CreateText(bar, "Title", title, 20, Color.white, TextAnchor.MiddleLeft);
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;

            var textRt = text.rectTransform;
            textRt.anchorMin = Vector2.zero;
            textRt.anchorMax = Vector2.one;
            textRt.offsetMin = new Vector2(12f, 0f);
            textRt.offsetMax = new Vector2(-40f, 0f);

            return bar;
        }

        /// <summary>
        /// 제목 표시줄 우상단에 빨간 X 닫기 버튼을 붙인다. 마우스만으로 창을 닫는 유일한 확실한
        /// 수단이라 모든 창에서 같은 자리·같은 색이어야 한다. Danger Red는 "되돌릴 수 없는 행동"이
        /// 아니라 창 닫기라는 관습적 의미로 쓴다.
        /// </summary>
        public static Button CreateCloseButton(RectTransform titleBar, UnityEngine.Events.UnityAction onClick)
        {
            var close = CreateButton(titleBar, "Close", "X", onClick);

            var rt = close.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(1f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(1f, 1f);
            rt.sizeDelta = new Vector2(30f, 24f);
            rt.anchoredPosition = new Vector2(-5f, -5f);

            var image = close.GetComponent<Image>();
            if (image != null)
            {
                Color closeColor = DangerRed;
                closeColor.a = 0.75f;
                image.color = closeColor;
            }

            return close;
        }

        /// <summary>
        /// 제목 표시줄을 드래그 손잡이로 만든다. 창 전체를 잡게 하지 않는 이유: 격자 칸을 클릭·우클릭
        /// 하는 조작과 드래그가 같은 영역에서 겹치면, 조작하려다 창이 딸려 움직인다.
        /// </summary>
        public static UIDragHandle AttachDragHandle(RectTransform titleBar, RectTransform window, RectTransform canvasRect, float handleHeight)
        {
            var handle = titleBar.gameObject.AddComponent<UIDragHandle>();
            handle.target = window;
            handle.bounds = canvasRect;
            handle.handleHeight = handleHeight;
            return handle;
        }

        /// <summary>
        /// 격자 칸 하나가 들고 있는 화면 부품 묶음. 소유자(InventoryUI/CraftingUI 등)는 여기 담긴
        /// 참조만 갱신하면 되고, 칸을 그리는 계층 구조 자체는 다시 만들지 않는다.
        /// </summary>
        public class SlotVisual
        {
            public GameObject go;
            public RectTransform rect;
            public Image background;
            public Outline outline;          // 선택 테두리(꺼둔 상태로 시작)
            public Image categoryStrip;      // 왼쪽 세로 색 띠
            public Image icon;
            public Text letterLabel;         // 아이콘 스프라이트가 없을 때의 폴백(이름 첫 글자)
            public Text countLabel;          // 우하단 개수
            public GameObject durabilityBarGo;
            public Image durabilityFill;
            public InventorySlotView input;  // 들어옴/나감/좌클릭/우클릭 어댑터
        }

        /// <summary>
        /// 표준 격자 칸을 만든다. 구성(아래→위): 배경 → 카테고리 색 띠 → 아이콘 → 폴백 글자 →
        /// (선택) 내구도 막대 → 개수. 개수와 내구도 막대는 둘 다 칸 아래쪽이지만 y가 겹치지 않게
        /// 띄워 둔다(막대 3~7px, 개수 8px부터).
        ///
        /// 크기는 부모 GridLayoutGroup의 cellSize가 정한다(여기서 sizeDelta를 만지지 않는다).
        /// withDurabilityBar는 도구 내구도를 보여줄 격자에서만 true로 준다 - 제작 창처럼 내구도가
        /// 의미 없는 격자에서 막대를 만들어 두면 꺼져 있어도 오브젝트만 늘어난다.
        /// </summary>
        public static SlotVisual CreateItemSlot(Transform parent, string name, bool withDurabilityBar = false)
        {
            var slot = new SlotVisual();

            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Outline), typeof(InventorySlotView));
            go.transform.SetParent(parent, false);
            slot.go = go;
            slot.rect = go.GetComponent<RectTransform>();

            slot.background = go.GetComponent<Image>();
            slot.background.color = SlotEmptyColor;

            // 스프라이트 9-slice 없이 테두리를 만들려면 Outline이 가장 싸다(사각 이미지 복사본 4장을
            // 바깥으로 민다). useGraphicAlpha를 끄지 않으면 배경 알파 0.04가 곱해져 사실상 안 보인다.
            slot.outline = go.GetComponent<Outline>();
            slot.outline.effectColor = MedicGreen;
            slot.outline.effectDistance = new Vector2(2f, 2f);
            slot.outline.useGraphicAlpha = false;
            slot.outline.enabled = false;

            var stripGo = new GameObject("CategoryStrip", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            stripGo.transform.SetParent(go.transform, false);
            var stripRt = stripGo.GetComponent<RectTransform>();
            stripRt.anchorMin = new Vector2(0f, 0f);
            stripRt.anchorMax = new Vector2(0f, 1f);
            stripRt.pivot = new Vector2(0f, 0.5f);
            stripRt.sizeDelta = new Vector2(3f, 0f);
            stripRt.anchoredPosition = Vector2.zero;
            slot.categoryStrip = stripGo.GetComponent<Image>();
            slot.categoryStrip.raycastTarget = false;
            slot.categoryStrip.color = Color.clear;

            var iconGo = new GameObject("Icon", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            iconGo.transform.SetParent(go.transform, false);
            var iconRt = iconGo.GetComponent<RectTransform>();
            iconRt.anchorMin = Vector2.zero;
            iconRt.anchorMax = Vector2.one;
            iconRt.offsetMin = new Vector2(8f, 10f); // 왼쪽은 색 띠, 아래쪽은 막대·개수 자리
            iconRt.offsetMax = new Vector2(-6f, -6f);
            slot.icon = iconGo.GetComponent<Image>();
            slot.icon.raycastTarget = false;
            slot.icon.preserveAspect = true;
            slot.icon.enabled = false;

            slot.letterLabel = CreateText(go.transform, "Letter", "", 20, Color.white, TextAnchor.MiddleCenter);
            slot.letterLabel.raycastTarget = false;
            var letterRt = slot.letterLabel.rectTransform;
            letterRt.anchorMin = Vector2.zero;
            letterRt.anchorMax = Vector2.one;
            letterRt.offsetMin = Vector2.zero;
            letterRt.offsetMax = Vector2.zero;
            slot.letterLabel.gameObject.SetActive(false);

            if (withDurabilityBar)
            {
                slot.durabilityFill = CreateProgressBar(go.transform, "Durability",
                    new Color(1f, 1f, 1f, 0.15f), Color.white);
                var barRt = (RectTransform)slot.durabilityFill.transform.parent;
                barRt.anchorMin = new Vector2(0f, 0f);
                barRt.anchorMax = new Vector2(1f, 0f);
                barRt.pivot = new Vector2(0.5f, 0f);
                barRt.sizeDelta = new Vector2(-10f, 4f);
                barRt.anchoredPosition = new Vector2(0f, 3f);
                slot.durabilityBarGo = barRt.gameObject;
                slot.durabilityBarGo.SetActive(false);
            }

            // 개수: 우하단. 밝은 아이콘 위에서도 읽히도록 그림자를 깐다(색을 하나 더 만들지 않는 방법).
            slot.countLabel = CreateText(go.transform, "Count", "", 12, Color.white, TextAnchor.LowerRight);
            slot.countLabel.raycastTarget = false;
            slot.countLabel.horizontalOverflow = HorizontalWrapMode.Overflow;
            var countRt = slot.countLabel.rectTransform;
            countRt.anchorMin = new Vector2(1f, 0f);
            countRt.anchorMax = new Vector2(1f, 0f);
            countRt.pivot = new Vector2(1f, 0f);
            countRt.sizeDelta = new Vector2(50f, 18f);
            countRt.anchoredPosition = new Vector2(-5f, 8f);
            var countShadow = slot.countLabel.gameObject.AddComponent<Shadow>();
            countShadow.effectColor = new Color(0f, 0f, 0f, 0.9f);
            countShadow.effectDistance = new Vector2(1f, -1f);
            slot.countLabel.gameObject.SetActive(false);

            slot.input = go.GetComponent<InventorySlotView>();

            return slot;
        }

        /// <summary>
        /// 고정 열 수 격자 컨테이너를 만든다. 창 위쪽에서 topOffset 만큼 내려온 자리에 붙고,
        /// 높이는 ResizeGrid가 실제 칸 수에 맞춰 정한다.
        /// </summary>
        public static RectTransform CreateSlotGrid(RectTransform window, string name, int columns, float slotSize, float spacing, float topOffset)
        {
            var gridGo = new GameObject(name, typeof(RectTransform), typeof(GridLayoutGroup));
            gridGo.transform.SetParent(window, false);

            var rt = gridGo.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 1f);
            rt.anchorMax = new Vector2(0.5f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.anchoredPosition = new Vector2(0f, -topOffset);
            rt.sizeDelta = new Vector2(columns * slotSize + (columns - 1) * spacing, slotSize);

            var grid = gridGo.GetComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(slotSize, slotSize);
            grid.spacing = new Vector2(spacing, spacing);
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = columns;
            grid.childAlignment = TextAnchor.UpperLeft;

            return rt;
        }
    }
}
