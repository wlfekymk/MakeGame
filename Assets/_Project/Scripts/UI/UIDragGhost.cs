using UnityEngine;
using UnityEngine.UI;

namespace MakeGame.UI
{
    /// <summary>
    /// 드래그로 집어 든 아이템을 커서에 붙여 보여 주는 "고스트". 화면에 하나뿐이다
    /// (동시에 두 개를 끌 수 없으므로 인스턴스를 여럿 둘 이유가 없다).
    ///
    /// 구조는 ui-toolkit-pt4의 ItemDragManipulator.InitGhost를 그대로 옮긴 것이다
    /// (Docs/Attribution.md). 원본이 정한 두 가지를 지킨다.
    ///  · 고스트는 **커서 중앙**에 온다(원본은 56px 고스트를 -28만큼 민다).
    ///  · 고스트는 클릭 판정을 절대 받지 않는다. 받으면 자기 밑의 칸이 가려져
    ///    "지금 어느 칸 위인가"를 영영 알 수 없게 된다.
    ///
    /// 별도 캔버스(sortOrder 14)를 쓰는 이유는 툴팁(13)과 같다. 고스트는 자기를 띄운 창보다
    /// 반드시 위에 그려져야 하고, 툴팁보다도 위여야 한다 - 다만 원본을 따라 드래그가 시작되면
    /// 툴팁은 스스로 숨으므로 실제로 둘이 겹치는 프레임은 없다.
    /// </summary>
    public class UIDragGhost : MonoBehaviour
    {
        private static UIDragGhost instance;

        private RectTransform canvasRect;
        private RectTransform ghostRt;
        private Image iconImage;
        private Text letterLabel;
        private bool visible;
        private bool targetValid = true;
        private Color contentColor = Color.white;

        /// <summary>지금 무언가를 끌고 있는가. 툴팁이 이 값을 보고 스스로 숨는다(원본과 같은 규칙).</summary>
        public static bool IsDragging => instance != null && instance.visible;

        private static UIDragGhost GetOrCreate()
        {
            if (instance != null)
                return instance;

            var canvas = UIBuilder.CreateCanvas("DragGhostCanvas", sortOrder: 14);

            // 고스트는 순수 장식이다. 레이캐스터가 살아 있으면 커서 밑의 칸을 자기가 가로채
            // 드롭 대상 판정이 통째로 무너진다.
            var raycaster = canvas.GetComponent<GraphicRaycaster>();
            if (raycaster != null)
                raycaster.enabled = false;

            var created = canvas.gameObject.AddComponent<UIDragGhost>();
            created.Build(canvas);
            instance = created;
            return instance;
        }

        private void Build(Canvas canvas)
        {
            canvasRect = canvas.GetComponent<RectTransform>();

            var go = new GameObject("Ghost", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(canvas.transform, false);
            ghostRt = go.GetComponent<RectTransform>();
            ghostRt.anchorMin = new Vector2(0.5f, 0.5f);
            ghostRt.anchorMax = new Vector2(0.5f, 0.5f);
            ghostRt.pivot = new Vector2(0.5f, 0.5f);   // 커서 중앙(원본의 -size/2와 같다)

            iconImage = go.GetComponent<Image>();
            iconImage.raycastTarget = false;
            iconImage.preserveAspect = true;
            iconImage.enabled = false;

            // 아이콘 스프라이트가 없는 아이템을 위한 폴백. 격자 칸이 쓰는 것과 같은 규칙
            // (이름 첫 글자)이라 끌기 전과 끄는 중의 모습이 어긋나지 않는다.
            letterLabel = UIBuilder.CreateText(ghostRt, "Letter", "", 24, Color.white, TextAnchor.MiddleCenter);
            letterLabel.raycastTarget = false;
            var letterRt = letterLabel.rectTransform;
            letterRt.anchorMin = Vector2.zero;
            letterRt.anchorMax = Vector2.one;
            letterRt.offsetMin = Vector2.zero;
            letterRt.offsetMax = Vector2.zero;
            letterLabel.gameObject.SetActive(false);

            ghostRt.gameObject.SetActive(false);
        }

        /// <summary>고스트를 띄운다. icon이 없으면 fallbackLetter를 대신 그린다.</summary>
        public static void Show(Sprite icon, string fallbackLetter, Color tint, float size)
        {
            var ghost = GetOrCreate();
            ghost.ghostRt.sizeDelta = new Vector2(size, size);

            bool hasIcon = icon != null;
            ghost.iconImage.enabled = hasIcon;
            if (hasIcon)
                ghost.iconImage.sprite = icon;

            ghost.letterLabel.gameObject.SetActive(!hasIcon);
            if (!hasIcon)
                ghost.letterLabel.text = string.IsNullOrEmpty(fallbackLetter) ? "?" : fallbackLetter;

            // 아이콘이 있으면 원래 그림 그대로(흰색 곱), 없으면 카테고리색 글자로 그린다.
            ghost.contentColor = hasIcon ? Color.white : tint;
            ghost.targetValid = true;
            ghost.ApplyTint();

            ghost.ghostRt.gameObject.SetActive(true);
            ghost.visible = true;
            ghost.Follow();
        }

        /// <summary>
        /// 지금 커서 아래에 놓을 수 있는 자리가 있는지 알린다. 놓을 수 없으면 고스트를 붉게 물들인다
        /// (원본 USS의 .drop-target--invalid 색). 매 프레임 불려도 안전하다 - 값이 바뀔 때만 칠한다.
        /// </summary>
        public static void SetTargetValid(bool valid)
        {
            if (instance == null || !instance.visible || instance.targetValid == valid)
                return;

            instance.targetValid = valid;
            instance.ApplyTint();
        }

        /// <summary>고스트를 치운다. 이미 없으면 아무 일도 하지 않는다.</summary>
        public static void Hide()
        {
            if (instance == null || !instance.visible)
                return;

            instance.visible = false;
            if (instance.ghostRt != null)
                instance.ghostRt.gameObject.SetActive(false);

            // 스프라이트 참조를 놓아 준다(숨긴 고스트가 마지막 아이콘을 붙들고 있지 않게).
            if (instance.iconImage != null)
                instance.iconImage.sprite = null;
        }

        /// <summary>
        /// 커서를 따라간다. 창이 떠 있는 동안 Time.timeScale이 0일 수 있으므로 시간에 기대는
        /// 보간을 쓰지 않는다 - 매 프레임 커서 좌표에서 위치를 직접 만든다.
        /// </summary>
        private void LateUpdate()
        {
            if (visible)
                Follow();
        }

        /// <summary>지금 상태(놓을 수 있는가)에 맞는 색을 아이콘·폴백 글자에 칠한다.</summary>
        private void ApplyTint()
        {
            Color color = targetValid ? contentColor : UITheme.DropTargetInvalid;
            color.a = UITheme.DragGhostAlpha;

            if (iconImage != null)
                iconImage.color = color;

            if (letterLabel != null)
                letterLabel.color = color;
        }

        private void Follow()
        {
            if (canvasRect == null || ghostRt == null)
                return;

            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, Input.mousePosition, null, out var cursor))
                ghostRt.anchoredPosition = cursor;
        }
    }
}
