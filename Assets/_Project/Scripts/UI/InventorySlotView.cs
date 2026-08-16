using UnityEngine;
using UnityEngine.EventSystems;

namespace MakeGame.UI
{
    /// <summary>
    /// 인벤토리 격자의 칸 하나가 받는 마우스 입력(들어옴/나감/좌클릭/우클릭)을 소유자(InventoryUI)에게
    /// 슬롯 번호와 함께 넘겨주는 얇은 어댑터. 칸마다 델리게이트를 새로 만들지 않도록 소유자가 슬롯을
    /// 만들 때 콜백을 한 번만 연결하고, 어떤 칸인지는 index로 구분한다.
    ///
    /// 자식(아이콘·개수·내구도 막대)은 별도 핸들러를 갖지 않는다. uGUI가 포인터 이벤트를 조상으로
    /// 올려 보내므로 칸 안 어디를 가리켜도 이 컴포넌트가 받는다 - 자식 위로 커서가 옮겨갔다고
    /// PointerExit이 잘못 발생하지도 않는다.
    /// </summary>
    public class InventorySlotView : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
    {
        public int index = -1;

        public System.Action<int> onEnter;
        public System.Action<int> onExit;
        public System.Action<int> onLeftClick;
        public System.Action<int> onRightClick;

        public void OnPointerEnter(PointerEventData eventData)
        {
            onEnter?.Invoke(index);
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            onExit?.Invoke(index);
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData.button == PointerEventData.InputButton.Right)
                onRightClick?.Invoke(index);
            else if (eventData.button == PointerEventData.InputButton.Left)
                onLeftClick?.Invoke(index);
        }
    }
}
