using UnityEngine;
using UnityEngine.EventSystems;

namespace MakeGame.UI
{
    /// <summary>
    /// 창(패널)을 마우스로 끌어 옮기는 손잡이. 제목 표시줄 같은 "잡는 곳"에 붙이고, 실제로 움직일
    /// 창(target)과 화면 경계로 쓸 캔버스(bounds)를 지정한다.
    ///
    /// EventTrigger가 아니라 인터페이스 구현을 쓰는 이유: EventTrigger는 붙은 오브젝트에서 이벤트를
    /// 가로채 상위로 올리지 않는다. 제목 표시줄 안에 닫기(X) 버튼이 들어 있으므로, 버튼 위에서 시작한
    /// 드래그도 창 이동으로 이어져야 자연스럽다. Button은 IPointerDown은 먹지만 IBeginDrag/IDrag는
    /// 구현하지 않으므로, 이 손잡이가 상위에서 드래그만 받아 처리하면 두 기능이 충돌 없이 공존한다.
    /// (그래서 잡은 지점 보정은 OnPointerDown이 아니라 OnBeginDrag를 기준으로 삼는다 - 버튼 위에서
    /// 시작하면 OnPointerDown이 이 컴포넌트까지 오지 않기 때문이다.)
    ///
    /// 전제: target은 앵커가 한 점(anchorMin == anchorMax)이고 피벗이 (0.5, 1)인 고정 크기 창이다.
    /// InventoryUI가 그렇게 만든다. 클램프 계산이 이 전제를 그대로 쓴다.
    /// </summary>
    public class UIDragHandle : MonoBehaviour, IPointerDownHandler, IBeginDragHandler, IDragHandler
    {
        /// <summary>실제로 움직일 창.</summary>
        public RectTransform target;

        /// <summary>화면 경계로 쓸 캔버스의 RectTransform.</summary>
        public RectTransform bounds;

        /// <summary>제목 표시줄 높이. 세로 클램프는 "이 높이만큼은 화면 안에 남는다"를 보장한다.</summary>
        public float handleHeight = 34f;

        /// <summary>가로로 화면 안에 반드시 남겨둘 최소 폭. 창을 완전히 화면 밖으로 밀어낼 수 없게 한다.</summary>
        public float minVisibleWidth = 96f;

        /// <summary>창이 실제로 움직인 뒤 호출된다(위치 기억용).</summary>
        public System.Action<Vector2> onMoved;

        private Vector2 grabOffset;

        public void OnPointerDown(PointerEventData eventData)
        {
            CaptureGrabOffset(eventData);
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            CaptureGrabOffset(eventData);
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (target == null || bounds == null)
                return;

            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(bounds, eventData.position, eventData.pressEventCamera, out var local))
                return;

            target.anchoredPosition = Clamp(local + grabOffset);
            onMoved?.Invoke(target.anchoredPosition);
        }

        /// <summary>지금 위치를 화면 안으로 다시 밀어 넣는다(해상도가 바뀌었거나 창을 다시 열 때).</summary>
        public void ClampNow()
        {
            if (target == null || bounds == null)
                return;

            target.anchoredPosition = Clamp(target.anchoredPosition);
            onMoved?.Invoke(target.anchoredPosition);
        }

        private void CaptureGrabOffset(PointerEventData eventData)
        {
            if (target == null || bounds == null)
                return;

            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(bounds, eventData.position, eventData.pressEventCamera, out var local))
                grabOffset = target.anchoredPosition - local;
        }

        /// <summary>
        /// 창의 위치를 "제목 표시줄이 항상 화면 안에 남는" 범위로 자른다. 한 번 화면 밖으로 완전히
        /// 나가면 다시 잡을 방법이 없기 때문에, 되돌릴 수 없는 상태 자체를 만들지 않는다.
        /// </summary>
        public Vector2 Clamp(Vector2 position)
        {
            if (target == null || bounds == null)
                return position;

            Vector2 canvasSize = bounds.rect.size;
            Vector2 windowSize = target.rect.size;
            float halfWindow = windowSize.x * 0.5f;
            float visible = Mathf.Min(minVisibleWidth, windowSize.x);

            // 피벗이 (0.5, 1)이므로 position.y는 창의 위쪽 모서리, position.x는 가로 중앙이다.
            float minY = -canvasSize.y * 0.5f + handleHeight; // 제목 표시줄 아래끝이 화면 아래를 넘지 않게
            float maxY = canvasSize.y * 0.5f;                 // 위쪽 모서리가 화면 위를 넘지 않게
            float minX = -canvasSize.x * 0.5f + visible - halfWindow;
            float maxX = canvasSize.x * 0.5f - visible + halfWindow;

            position.x = Mathf.Clamp(position.x, minX, maxX);
            position.y = Mathf.Clamp(position.y, minY, maxY);
            return position;
        }
    }
}
