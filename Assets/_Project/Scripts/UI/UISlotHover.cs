using UnityEngine;
using UnityEngine.EventSystems;

namespace MakeGame.UI
{
    /// <summary>
    /// 격자 칸에 마우스를 올렸을 때 살짝 커지는 연출. UI Toolkit 원본이 USS transition으로
    /// 하던 일을 uGUI에서 대신한다.
    ///
    /// 왜 소유자(InventoryUI 등)가 아니라 칸 자신이 갖고 있나:
    /// · 같은 GameObject에 IPointerEnterHandler가 여럿 있어도 Unity는 전부 호출한다.
    ///   그래서 기존 InventorySlotView의 콜백 배선을 하나도 건드리지 않고 붙을 수 있다.
    /// · 덕분에 인벤토리뿐 아니라 제작/상자/건축 격자도 CreateItemSlot 한 번의 수정으로 같이 살아난다.
    ///
    /// **색은 절대 만지지 않는다.** 색은 소유자가 상태(선택/버리기 무장/사용 불가)에 따라 정하는데,
    /// 여기서 같이 건드리면 두 곳이 매 프레임 서로 덮어쓰게 된다. 이 컴포넌트는 스케일만 담당한다.
    ///
    /// GridLayoutGroup은 localScale을 보지 않으므로 칸이 커져도 배치는 흔들리지 않는다.
    /// </summary>
    [DisallowMultipleComponent]
    public class UISlotHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        private bool hovered;
        private float current = 1f;

        /// <summary>마우스가 올라가 있는가(소유자가 참고할 수 있게 공개).</summary>
        public bool IsHovered => hovered;

        public void OnPointerEnter(PointerEventData eventData) { hovered = true; }

        public void OnPointerExit(PointerEventData eventData) { hovered = false; }

        private void OnDisable()
        {
            // 창을 닫을 때 커진 채로 굳으면 다음에 열 때 한 칸만 큰 상태로 보인다.
            hovered = false;
            current = 1f;
            transform.localScale = Vector3.one;
        }

        private void Update()
        {
            float target = hovered ? UITheme.SlotHoverScale : 1f;
            if (Mathf.Approximately(current, target)) return;

            // unscaledDeltaTime: 인벤토리는 시간이 멈춘 상태에서도 열리는 창이다.
            current = Mathf.MoveTowards(current, target,
                Mathf.Abs(UITheme.SlotHoverScale - 1f) * UITheme.SlotHoverSpeed * Time.unscaledDeltaTime);
            transform.localScale = new Vector3(current, current, 1f);
        }
    }
}
