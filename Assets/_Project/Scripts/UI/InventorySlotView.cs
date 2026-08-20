using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using MakeGame.Data;
using MakeGame.Player;

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
    public class InventorySlotView : MonoBehaviour,
        IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler,
        IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        public int index = -1;

        public System.Action<int> onEnter;
        public System.Action<int> onExit;
        public System.Action<int> onLeftClick;
        public System.Action<int> onRightClick;

        /// <summary>
        /// 드래그 3종. 소유자가 "이 칸을 끌 수 있는가"를 판단하고 true를 돌려주면 이 칸이 드래그를
        /// 가져가고, false면 **그대로 조상에게 넘긴다**(= 격자 스크롤이 계속 먹는다).
        ///
        /// 이 되돌림이 필요한 이유: IBeginDragHandler를 붙이는 순간 uGUI는 이 오브젝트에서
        /// 이벤트를 멈춘다. 아무 처리도 안 하고 삼키면 빈 칸을 잡고 끌어 스크롤하던 조작이
        /// 조용히 죽는다(100칸짜리 격자에서 그건 기능 하나가 사라지는 것과 같다).
        /// </summary>
        public System.Func<int, bool> onDragBegin;
        public System.Action<int> onDragMove;
        public System.Action<int> onDragEnd;

        /// <summary>지금 이 칸이 드래그를 가져갔는가(false면 스크롤에 넘긴 상태).</summary>
        private bool draggingSelf;

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
            // 끌었다 놓은 직후에는 클릭으로 치지 않는다. uGUI는 드래그가 끝난 포인터에도
            // PointerClick을 보내므로, 이 검사가 없으면 칸을 옮길 때마다 그 칸이 선택된다.
            if (eventData.dragging)
                return;

            if (eventData.button == PointerEventData.InputButton.Right)
                onRightClick?.Invoke(index);
            else if (eventData.button == PointerEventData.InputButton.Left)
                onLeftClick?.Invoke(index);
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            draggingSelf = eventData.button == PointerEventData.InputButton.Left
                           && onDragBegin != null
                           && onDragBegin.Invoke(index);

            if (!draggingSelf && transform.parent != null)
                ExecuteEvents.ExecuteHierarchy(transform.parent.gameObject, eventData, ExecuteEvents.beginDragHandler);
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (draggingSelf)
                onDragMove?.Invoke(index);
            else if (transform.parent != null)
                ExecuteEvents.ExecuteHierarchy(transform.parent.gameObject, eventData, ExecuteEvents.dragHandler);
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            if (draggingSelf)
            {
                draggingSelf = false;
                onDragEnd?.Invoke(index);
            }
            else if (transform.parent != null)
            {
                ExecuteEvents.ExecuteHierarchy(transform.parent.gameObject, eventData, ExecuteEvents.endDragHandler);
            }
        }
    }

    /// <summary>
    /// **스크롤 + 칸 뷰 재사용(가상화) 격자.** 칸이 100개, 200개로 늘어나도 감당하는 유일한 방법이고,
    /// 인벤토리 창(100칸)과 보관 상자 창(50~200칸)이 **같은 구현 하나**를 공유한다.
    ///
    /// 왜 GridLayoutGroup을 안 쓰는가:
    /// · 칸 뷰 하나가 GameObject 6개(배경/색 띠/아이콘/글자/내구도 막대/개수)다. 200칸을 전부 만들면
    ///   1200개가 넘고, 인벤토리 100칸도 600개다. 창을 열 때마다 그만큼이 레이아웃 계산에 들어간다.
    /// · 용량이 바뀔 때(상자 등급 승급) 칸을 새로 만들면 그 프레임이 그대로 튄다.
    ///
    /// 대신 여기서는 콘텐츠(전체 격자)의 **높이만** 칸 수에 맞춰 늘리고, 칸 뷰는 화면에 보이는 줄 +
    /// 여유 2줄만큼만 만들어 스크롤 위치에 따라 다른 인덱스로 다시 묶는다. 그래서:
    ///   · 만들어지는 칸 뷰 수 = (보이는 줄 + 2) × 열 수. 6열 7줄이면 54개가 상한이다.
    ///   · 50 → 200칸으로 늘어나도 새로 만들어지는 오브젝트는 **0개**다(콘텐츠 높이만 바뀐다).
    ///   · 스크롤은 위치 계산과 내용 갱신뿐이라 생성/파괴가 전혀 없다.
    ///
    /// 배치는 레이아웃 그룹 없이 직접 한다 - 레이아웃 그룹은 자식 전부를 대상으로 재계산하므로
    /// 가상화와 같이 쓰면 이득이 사라진다.
    ///
    /// **칸의 배경색·선택 테두리는 이 클래스가 정하지 않는다.** 창마다 상태가 다르기 때문이다
    /// (인벤토리는 선택/버리기 확인 대기가 있고, 상자 창은 hover만 있다). 소유자가 <see cref="onStyle"/>
    /// 훅에서 칠한다.
    /// </summary>
    public class VirtualSlotGrid
    {
        /// <summary>스크롤 도중 위아래로 반쯤 걸치는 줄을 덮기 위한 여유 줄 수.</summary>
        private const int SpareRows = 2;

        /// <summary>개수 상한에 닿은 칸을 표시하는 색 (#E6BF33 Sunstroke Gold).</summary>
        private static readonly Color SunstrokeGold = new Color(0.902f, 0.749f, 0.2f, 1f);

        /// <summary>칸 뷰 하나와 "지금 이 뷰가 무엇을 보여주고 있는지" 캐시.</summary>
        public class Cell
        {
            public UIBuilder.SlotVisual visual;

            /// <summary>이 뷰가 지금 맡고 있는 **데이터 인덱스**(격자에서 몇 번째 칸인가). 숨김이면 -1.</summary>
            public int index = -1;

            /// <summary>이 칸이 지금 보여주고 있는 내용(문자열을 다시 만들지 판단하는 캐시).</summary>
            public ItemData data;
            public int count;
            public int remaining = int.MinValue;
            public InventoryItem representative;
            public bool shown = true;
        }

        // 소유자가 받는 신호. 인자는 전부 **데이터 인덱스**다(풀 안의 위치가 아니다).
        public System.Action<int> onEnter;
        public System.Action<int> onExit;
        public System.Action<int> onLeftClick;
        public System.Action<int> onRightClick;

        // 드래그(칸 옮기기). 격자는 판단하지 않고 그대로 소유자에게 넘긴다 - "이 칸을 끌 수 있는가"는
        // 창마다 다르기 때문이다(인벤토리는 수동 정렬일 때만 허용, 상자 창은 아직 허용하지 않는다).
        // onDragBegin이 null이거나 false를 돌려주면 그 드래그는 격자 스크롤로 넘어간다.
        public System.Func<int, bool> onDragBegin;
        public System.Action<int> onDragMove;
        public System.Action<int> onDragEnd;

        /// <summary>칸 하나의 상태색/테두리를 소유자가 칠하는 훅(내용을 새로 그린 뒤와 RefreshStyles에서 불린다).</summary>
        public System.Action<Cell> onStyle;

        /// <summary>
        /// 스크롤로 **보이는 줄이 실제로 바뀐** 순간 불린다(같은 줄에 머무는 스크롤에서는 불리지 않는다).
        /// 커서 아래의 칸이 다른 물건으로 갈리는 순간이라, 소유자는 여기서 hover 강조와 툴팁을 정리한다 -
        /// uGUI는 "칸이 커서 밑에서 미끄러져 나간" 경우에 PointerExit를 발행하지 않기 때문에, 이게
        /// 없으면 스크롤 뒤에도 엉뚱한 칸이 밝게 남고 툴팁이 옛 물건을 계속 설명한다.
        /// </summary>
        public System.Action onRowsChanged;

        private readonly List<Cell> cells = new List<Cell>();

        /// <summary>소유자가 GetStacks(buffer)로 직접 채우는 표시용 목록(리스트를 새로 만들지 않는다).</summary>
        private readonly List<InventoryStack> stacks = new List<InventoryStack>();

        private RectTransform root;
        private RectTransform viewport;
        private RectTransform content;
        private ScrollRect scroll;

        private int columns = 6;
        private float slotSize = 62f;
        private float spacing = 6f;
        private float rowStride = 68f;
        private float viewportHeight;
        private int maxPooledCells;
        private bool withDurabilityBar;

        private int capacity;
        private int firstRow = -1;

        /// <summary>표시할 스택 목록. 소유자가 이 버퍼를 그대로 GetStacks(buffer)에 넘긴다.</summary>
        public List<InventoryStack> Buffer => stacks;

        /// <summary>지금 격자가 그리고 있는 칸 수(빈 칸 포함).</summary>
        public int Capacity => capacity;

        /// <summary>지금 존재하는 칸 뷰들(소유자가 상태색을 다시 칠할 때 훑는다).</summary>
        public IReadOnlyList<Cell> Cells => cells;

        /// <summary>격자 루트. 소유자가 창 안에서의 위치(anchoredPosition)를 정한다.</summary>
        public RectTransform Root => root;

        /// <summary>세로로 실제 스크롤이 필요한 상태인지(안내 문구를 바꿀 때 쓴다).</summary>
        public bool IsScrollable => content != null && content.sizeDelta.y > viewportHeight + 0.5f;

        /// <summary>
        /// 스크롤 영역 · 콘텐츠 · 칸 뷰 풀을 만든다. 루트는 부모의 **좌상단 기준**으로 붙으므로,
        /// 소유자는 Root.anchoredPosition만 정하면 된다.
        /// </summary>
        public void Build(Transform parent, string name, float width, float height,
            int columnCount, float cellSize, float cellSpacing, bool durabilityBars)
        {
            columns = Mathf.Max(1, columnCount);
            slotSize = cellSize;
            spacing = cellSpacing;
            rowStride = slotSize + spacing;
            viewportHeight = height;
            withDurabilityBar = durabilityBars;

            int visibleRows = Mathf.Max(1, Mathf.CeilToInt(height / rowStride));
            maxPooledCells = (visibleRows + SpareRows) * columns;

            var rootGo = new GameObject(name, typeof(RectTransform), typeof(ScrollRect));
            rootGo.transform.SetParent(parent, false);
            root = rootGo.GetComponent<RectTransform>();
            root.anchorMin = new Vector2(0f, 1f);
            root.anchorMax = new Vector2(0f, 1f);
            root.pivot = new Vector2(0f, 1f);
            root.sizeDelta = new Vector2(width, height);

            // 뷰포트: RectMask2D가 영역 밖으로 나간 칸을 잘라낸다(MinimapUI가 쓰는 것과 같은 방식).
            // 아주 옅은 배경을 깔아 두는 이유는 두 가지다 - (1) 격자 영역의 경계가 눈에 보이고,
            // (2) raycastTarget이 켜져 있어야 빈 자리에서 끌어 스크롤하는 조작이 먹는다.
            viewport = UIBuilder.CreatePanel(root, "Viewport",
                anchorMin: Vector2.zero, anchorMax: Vector2.one,
                offsetMin: Vector2.zero, offsetMax: Vector2.zero,
                color: new Color(1f, 1f, 1f, 0.02f));
            viewport.gameObject.AddComponent<RectMask2D>();

            var contentGo = new GameObject("Content", typeof(RectTransform));
            contentGo.transform.SetParent(viewport, false);
            content = contentGo.GetComponent<RectTransform>();
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            content.anchoredPosition = Vector2.zero;
            content.sizeDelta = new Vector2(0f, height);

            scroll = rootGo.GetComponent<ScrollRect>();
            scroll.viewport = viewport;
            scroll.content = content;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = rowStride; // 휠 한 칸 = 한 줄
            scroll.onValueChanged.AddListener(OnScrolled);
        }

        private void OnScrolled(Vector2 _)
        {
            Rebind(false);
        }

        /// <summary>스크롤을 맨 위로 되돌린다(다른 대상을 열었거나 필터가 바뀌어 목록이 통째로 달라졌을 때).</summary>
        public void ResetScroll()
        {
            if (content == null)
                return;

            content.anchoredPosition = new Vector2(content.anchoredPosition.x, 0f);
            firstRow = -1;
        }

        /// <summary>
        /// 칸 수를 바꾼다. **칸 뷰를 새로 만들지 않는다** - 콘텐츠 높이(=스크롤 가능 범위)만 바꾸고,
        /// 아직 풀에 없는 만큼만(그것도 상한을 넘지 않게) 칸 뷰를 채운다.
        /// </summary>
        public void SetCapacity(int newCapacity)
        {
            newCapacity = Mathf.Max(0, newCapacity);

            EnsurePool(Mathf.Min(maxPooledCells, newCapacity));

            if (newCapacity == capacity)
                return;

            capacity = newCapacity;

            int rows = Mathf.Max(1, Mathf.CeilToInt(capacity / (float)columns));
            float contentHeight = Mathf.Max(viewportHeight, rows * slotSize + (rows - 1) * spacing);
            content.sizeDelta = new Vector2(0f, contentHeight);

            // 칸이 줄어드는 경우 스크롤이 빈 영역에 남지 않게 되돌린다.
            float maxScroll = Mathf.Max(0f, contentHeight - viewportHeight);
            if (content.anchoredPosition.y > maxScroll)
                content.anchoredPosition = new Vector2(content.anchoredPosition.x, maxScroll);

            firstRow = -1; // 다음 Rebind가 반드시 전부 다시 묶게 한다
        }

        /// <summary>필요한 만큼만 칸 뷰를 만든다(한 번 만든 뷰는 파괴하지 않고 계속 재사용한다).</summary>
        private void EnsurePool(int wanted)
        {
            while (cells.Count < wanted)
            {
                var cell = new Cell();
                cell.visual = UIBuilder.CreateItemSlot(content, $"Slot{cells.Count}", withDurabilityBar);

                RectTransform rt = cell.visual.rect;
                rt.anchorMin = new Vector2(0f, 1f);
                rt.anchorMax = new Vector2(0f, 1f);
                rt.pivot = new Vector2(0f, 1f);
                rt.sizeDelta = new Vector2(slotSize, slotSize);

                // 콜백은 만들 때 한 번만 연결한다. 어떤 칸을 맡고 있는지는 Rebind가 index에 다시 써 넣는다
                // (같은 뷰가 스크롤에 따라 다른 칸을 맡기 때문이다).
                InventorySlotView input = cell.visual.input;
                input.index = -1;
                input.onEnter = RaiseEnter;
                input.onExit = RaiseExit;
                input.onLeftClick = RaiseLeftClick;
                input.onRightClick = RaiseRightClick;
                input.onDragBegin = RaiseDragBegin;
                input.onDragMove = RaiseDragMove;
                input.onDragEnd = RaiseDragEnd;

                cells.Add(cell);
            }
        }

        private void RaiseEnter(int dataIndex) => onEnter?.Invoke(dataIndex);
        private void RaiseExit(int dataIndex) => onExit?.Invoke(dataIndex);
        private void RaiseLeftClick(int dataIndex) => onLeftClick?.Invoke(dataIndex);
        private void RaiseRightClick(int dataIndex) => onRightClick?.Invoke(dataIndex);
        private bool RaiseDragBegin(int dataIndex) => onDragBegin != null && onDragBegin.Invoke(dataIndex);
        private void RaiseDragMove(int dataIndex) => onDragMove?.Invoke(dataIndex);
        private void RaiseDragEnd(int dataIndex) => onDragEnd?.Invoke(dataIndex);

        /// <summary>
        /// 화면 좌표 아래에 있는 칸의 데이터 인덱스를 찾는다. 없으면 -1.
        /// 드래그를 놓을 대상을 정할 때 쓴다(원본 ItemDragManipulator.FindSlotUnderPointer에 대응).
        ///
        /// 레이캐스트를 쓰지 않는 이유: 고스트·툴팁 캔버스가 위에 떠 있고, 무엇보다 칸 뷰는
        /// 스크롤 밖으로 밀려나도 **파괴되지 않고 남아 있어** 마스크에 잘린 채로도 레이캐스트에
        /// 걸린다. 그래서 뷰포트 안인지 먼저 보고, 그 안에서만 칸 사각형을 검사한다.
        /// </summary>
        public int IndexAtScreenPoint(Vector2 screenPoint)
        {
            if (viewport == null)
                return -1;

            if (!RectTransformUtility.RectangleContainsScreenPoint(viewport, screenPoint, null))
                return -1;

            for (int i = 0; i < cells.Count; i++)
            {
                Cell cell = cells[i];
                if (!cell.shown || cell.index < 0 || cell.visual == null || cell.visual.rect == null)
                    continue;

                if (RectTransformUtility.RectangleContainsScreenPoint(cell.visual.rect, screenPoint, null))
                    return cell.index;
            }

            return -1;
        }

        /// <summary>표시 목록에서 그 칸이 담고 있는 스택을 얻는다(빈 칸이면 null).</summary>
        public InventoryStack GetStack(int dataIndex)
        {
            if (dataIndex < 0 || dataIndex >= stacks.Count)
                return null;

            return stacks[dataIndex];
        }

        /// <summary>
        /// 지금 스크롤 위치에 맞춰 칸 뷰를 데이터에 다시 묶는다. force가 아니면 스크롤이 한 줄도
        /// 움직이지 않은 프레임에서는 아무 일도 하지 않는다(휠 한 번에 수십 번 불려도 안전하다).
        /// </summary>
        public void Rebind(bool force)
        {
            if (content == null || cells.Count == 0)
                return;

            int totalRows = Mathf.Max(1, Mathf.CeilToInt(capacity / (float)columns));
            int poolRows = Mathf.Max(1, cells.Count / columns);
            int maxFirstRow = Mathf.Max(0, totalRows - poolRows);

            int newFirstRow = Mathf.Clamp(Mathf.FloorToInt(content.anchoredPosition.y / rowStride), 0, maxFirstRow);
            if (!force && newFirstRow == firstRow)
                return;

            // 첫 묶기(firstRow == -1)는 스크롤이 아니다. 실제로 줄이 갈린 경우에만 알린다.
            bool rowsChanged = firstRow >= 0 && newFirstRow != firstRow;
            firstRow = newFirstRow;

            if (rowsChanged)
                onRowsChanged?.Invoke();

            for (int i = 0; i < cells.Count; i++)
            {
                Cell cell = cells[i];

                int row = firstRow + i / columns;
                int column = i % columns;
                int dataIndex = row * columns + column;

                if (dataIndex >= capacity)
                {
                    Hide(cell);
                    continue;
                }

                if (!cell.shown)
                {
                    cell.visual.go.SetActive(true);
                    cell.shown = true;
                }

                cell.visual.rect.anchoredPosition = new Vector2(column * rowStride, -row * rowStride);
                cell.visual.input.index = dataIndex;

                Apply(cell, dataIndex);
            }
        }

        /// <summary>상태색만 다시 칠한다(내용은 그대로라 문자열을 만들지 않는다).</summary>
        public void RefreshStyles()
        {
            for (int i = 0; i < cells.Count; i++)
            {
                if (cells[i].shown)
                    onStyle?.Invoke(cells[i]);
            }
        }

        private void Hide(Cell cell)
        {
            if (!cell.shown)
                return;

            cell.shown = false;
            cell.index = -1;
            cell.data = null;
            cell.representative = null;
            cell.count = 0;
            cell.remaining = int.MinValue;
            cell.visual.input.index = -1;
            cell.visual.go.SetActive(false);
        }

        /// <summary>칸 하나의 내용을 그린다. 지난번과 같은 칸·같은 내용이면 문자열을 다시 만들지 않는다.</summary>
        private void Apply(Cell cell, int dataIndex)
        {
            InventoryStack stack = dataIndex < stacks.Count ? stacks[dataIndex] : null;
            ItemData data = stack != null ? stack.data : null;
            int count = stack != null ? stack.count : 0;

            // **대표 인스턴스가 없을 수 있다.** InventoryStack.RemainingUses는 대표가 null이면 0을
            // 돌려주는데(InventoryItem.cs), 개수만 세어 보관하는 구현이라면 모든 칸이 그렇다. 그 0을
            // 내구도로 믿으면 넣어둔 손도끼가 전부 "다 닳음(빨간 막대)"으로 보인다. 그래서 대표가
            // 없을 때는 int.MinValue = "모름"으로 두고 막대 자체를 그리지 않는다.
            int remaining = (stack != null && stack.representative != null) ? stack.RemainingUses : int.MinValue;

            cell.index = dataIndex;
            cell.representative = stack != null ? stack.representative : null;

            // 막대(내구도 / 신선도)는 **내용 캐시보다 앞에서** 갱신한다. 신선도는 칸의 내용
            // (종류·개수·내구도)이 하나도 바뀌지 않아도 시간이 흐르면 변하기 때문에, 아래 조기 반환
            // 뒤에 두면 게이지가 그 자리에 얼어붙는다(툴팁 캐시에 신선도를 넣은 것과 같은 이유).
            UpdateSlotBar(cell, stack, data, remaining);

            if (cell.data == data && cell.count == count && cell.remaining == remaining)
            {
                onStyle?.Invoke(cell);
                return;
            }

            cell.data = data;
            cell.count = count;
            cell.remaining = remaining;

            UIBuilder.SlotVisual visual = cell.visual;

            if (data == null)
            {
                visual.icon.enabled = false;
                visual.icon.sprite = null;
                visual.categoryStrip.color = Color.clear;
                visual.frame.color = UITheme.SlotFrame(Color.white, filled: false, hovered: false, selected: false);
                visual.letterLabel.gameObject.SetActive(false);
                visual.countLabel.gameObject.SetActive(false);
                if (visual.durabilityBarGo != null)
                    visual.durabilityBarGo.SetActive(false);

                onStyle?.Invoke(cell);
                return;
            }

            Color categoryColor = UIBuilder.GetItemCategoryColor(data);
            visual.categoryStrip.color = categoryColor;

            // 칸 테두리도 같은 카테고리색을 쓴다. 여기서 칠하는 것은 "내용이 있다"까지고, hover·선택은
            // 소유자의 onStyle이 덮어쓴다 - 창마다 선택 규칙이 달라(상자 창엔 선택이 없다) 여기서 정할 수 없다.
            visual.frame.color = UITheme.SlotFrame(categoryColor, filled: true, hovered: false, selected: false);

            // 아이콘 31종이 전부 배선돼 있지만(ItemData.icon) 새 아이템이 아이콘 없이 추가될 수 있으므로
            // 이름 첫 글자 폴백을 남겨둔다. 폴백일 때는 카테고리 색을 배경으로 깔아 최소한의 구분을 준다.
            visual.icon.enabled = true;
            if (data.icon != null)
            {
                visual.icon.sprite = data.icon;
                visual.icon.color = Color.white;
                visual.letterLabel.gameObject.SetActive(false);
            }
            else
            {
                visual.icon.sprite = null;
                visual.icon.color = categoryColor;
                visual.letterLabel.gameObject.SetActive(true);
                visual.letterLabel.text = string.IsNullOrEmpty(data.itemName) ? "?" : data.itemName.Substring(0, 1);
            }

            // 개수 1은 찍지 않는다("x1"은 정보가 없고 아이콘만 가린다 - 격자 UI의 표준).
            if (count > 1)
            {
                visual.countLabel.gameObject.SetActive(true);
                visual.countLabel.text = count.ToString();
                visual.countLabel.color = count >= data.MaxStackSize ? SunstrokeGold : Color.white;
            }
            else
            {
                visual.countLabel.gameObject.SetActive(false);
            }

            // 막대(내구도 / 신선도)는 이 메서드 위쪽에서 UpdateSlotBar가 이미 그렸다. 여기서 다시
            // 손대면 조기 반환 경로와 전체 갱신 경로가 서로 다른 막대를 그리게 된다.

            onStyle?.Invoke(cell);
        }

        /// <summary>
        /// 칸 아래 가로 막대 하나를 갱신한다. 이 막대는 **둘 중 하나**를 보여준다:
        ///  · 내구도 - 겹쳐지지 않는 유한 내구도 도구(창 15 · 손도끼 20 · 라이터 5).
        ///  · [식량 루프] 신선도 - 부패하는 음식(InventoryStack.ShowsFreshness).
        ///
        /// 하나를 나눠 쓰는 것이 안전한 이유: 두 조건은 **동시에 참이 될 수 없다.** 내구도 막대는
        /// !IsStackable(= maxUses &gt; 1)일 때만 뜨는데 음식은 전부 maxUses == 1이라 IsStackable이다.
        /// (칸에 막대를 하나 더 만들려면 UIBuilder.CreateItemSlot을 고쳐야 하는데 그 파일은 이 작업의
        /// 락 밖이다 - 그래서 새 오브젝트를 만들지 않는 이 방법을 골랐다. 표시도 그만큼 조용하다.)
        ///
        /// 신선도 색의 경계값은 <see cref="MakeGame.Systems.FoodSpoilage"/>의 단계 경계를 **그대로**
        /// 쓴다. 여기에 별도의 숫자를 적으면 막대는 노란데 툴팁에는 "신선"이라고 적히는 일이 생긴다.
        /// </summary>
        private void UpdateSlotBar(Cell cell, InventoryStack stack, ItemData data, int remaining)
        {
            GameObject barGo = cell.visual.durabilityBarGo;
            if (barGo == null)
                return;

            if (data != null && !data.IsStackable && !data.IsUnlimited && data.maxUses > 1
                && remaining != int.MinValue)
            {
                float ratio = Mathf.Clamp01((float)remaining / data.maxUses);
                barGo.SetActive(true);
                cell.visual.durabilityFill.fillAmount = ratio;
                cell.visual.durabilityFill.color = ratio <= 0.2f ? UIBuilder.DangerRed
                    : ratio <= 0.4f ? SunstrokeGold : UIBuilder.MedicGreen;
                return;
            }

            if (stack != null && stack.ShowsFreshness)
            {
                float freshness = Mathf.Clamp01(stack.Freshness01);
                barGo.SetActive(true);
                cell.visual.durabilityFill.fillAmount = freshness;
                cell.visual.durabilityFill.color =
                    freshness < MakeGame.Systems.FoodSpoilage.RottenThreshold01 ? UIBuilder.DangerRed
                    : freshness < MakeGame.Systems.FoodSpoilage.SpoilingThreshold01 ? SunstrokeGold
                    : UIBuilder.MedicGreen;
                return;
            }

            barGo.SetActive(false);
        }
    }
}
