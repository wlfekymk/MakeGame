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
