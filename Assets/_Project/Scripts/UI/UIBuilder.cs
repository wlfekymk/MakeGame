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
        /// </summary>
        public static RectTransform CreatePanel(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(parent, false);

            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.offsetMin = offsetMin;
            rt.offsetMax = offsetMax;

            go.GetComponent<Image>().color = color;
            return rt;
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
        /// 아이템의 종류(무기/음식/음료/설치형/일반 재료)에 따라 아이콘 배경색을 정한다.
        /// 실제 아이콘 이미지가 없는 상태에서 최소한의 시각적 구분을 주기 위한 임시 규칙이다.
        /// </summary>
        public static Color GetItemCategoryColor(MakeGame.Data.ItemData item)
        {
            if (item == null)
                return new Color(0.5f, 0.5f, 0.5f, 1f);

            if (item.isWeapon)
                return new Color(0.8f, 0.25f, 0.25f, 1f); // 빨강: 무기

            if (item.hungerRestoreAmount > 0f)
                return new Color(0.85f, 0.55f, 0.2f, 1f); // 주황: 음식

            if (item.thirstRestoreAmount > 0f)
                return new Color(0.25f, 0.55f, 0.85f, 1f); // 파랑: 음료

            if (item.isPlaceable)
                return new Color(0.3f, 0.7f, 0.6f, 1f); // 청록: 설치형(빌드) 아이템

            if (item.blockedFromLargeIslandsByCurrent)
                return new Color(0.6f, 0.5f, 0.85f, 1f); // 보라: 이동 수단(고무보트 등)

            return new Color(0.55f, 0.45f, 0.35f, 1f); // 갈색: 일반 채집 재료
        }

        /// <summary>
        /// 체력/허기/갈증처럼 0~1 비율로 변하는 수치를 가로 막대(배경 + 채움 이미지)로 표시하는
        /// 프로그레스 바를 생성한다. 반환되는 Fill Image의 fillAmount(0~1)를 매 프레임 갱신하면
        /// 막대가 늘고 줄어드는 것처럼 보인다. HUD처럼 raw 숫자보다 한눈에 들어오는 시각 표시가
        /// 필요한 곳에서 쓴다.
        /// </summary>
        public static Image CreateProgressBar(Transform parent, string name, Color backgroundColor, Color fillColor)
        {
            var bgGo = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            bgGo.transform.SetParent(parent, false);
            bgGo.GetComponent<Image>().color = backgroundColor;

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
        /// </summary>
        public static Button CreateButton(Transform parent, string name, string label, UnityEngine.Events.UnityAction onClick)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);

            go.GetComponent<Image>().color = new Color(0.25f, 0.55f, 0.3f, 1f);

            var button = go.GetComponent<Button>();
            if (onClick != null)
                button.onClick.AddListener(onClick);

            var labelText = CreateText(go.transform, "Label", label, 16, Color.white, TextAnchor.MiddleCenter);
            var labelRt = labelText.rectTransform;
            labelRt.anchorMin = Vector2.zero;
            labelRt.anchorMax = Vector2.one;
            labelRt.offsetMin = Vector2.zero;
            labelRt.offsetMax = Vector2.zero;

            return button;
        }
    }
}
