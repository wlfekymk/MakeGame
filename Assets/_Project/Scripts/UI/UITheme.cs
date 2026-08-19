using UnityEngine;

namespace MakeGame.UI
{
    /// <summary>
    /// 창 스킨 기준값을 한곳에 모은 표. 값 자체는 새로 지어낸 것이 아니라
    /// Docs/ArtDirection.md 4.3(폰트 4단계, 패널 테두리 흰색 알파 0.12, 강조색 남발 금지)과
    /// 기존 UIBuilder 색을 그대로 수치화한 것이다.
    ///
    /// 구조(헤더 / 구분선 / 좌우 2단 본문)는 UI Toolkit 튜토리얼
    /// gamedev-resources/ui-toolkit-pt2-inventory-design 의 레이아웃을 uGUI로 옮긴 것이다.
    /// 그 저장소의 파일·스프라이트는 하나도 가져오지 않았다(LICENSE 파일이 없고
    /// 아이콘은 제3자 KayKit 출처라 그대로 쓸 수 없다). 가져온 것은 "배치 규칙"뿐이다.
    ///
    /// 원본의 주황 강조색(#FF7C00)은 쓰지 않는다. ArtDirection 4.3이 "패널마다 다른 강조색을
    /// 넣지 않는다"로 못박았고, 우리 팔레트에서 주황(#D98C33)은 이미 "음식"을 뜻하기 때문이다.
    /// 대신 원본이 강조색으로 하던 일(칸을 색으로 구분하기)은 이미 게임에 있는
    /// UIBuilder.GetItemCategoryColor(카테고리색)로 대신한다 — 판타지 등급보다 생존 게임에
    /// 정보량이 크고, 팔레트를 하나도 늘리지 않는다.
    /// </summary>
    public static class UITheme
    {
        // ── 창 구조 ─────────────────────────────────────────────
        /// <summary>제목 줄 높이. 제목(20pt) 한 줄이 여백 포함해 들어가는 최소치.</summary>
        public const float HeaderHeight = 44f;

        /// <summary>헤더와 본문을 가르는 선 두께. 1px 이상은 두꺼워 보인다.</summary>
        public const float SeparatorThickness = 1f;

        /// <summary>본문 안쪽 여백(원본의 body padding 10에 대응, 우리 창은 조금 더 넉넉하게).</summary>
        public const float BodyPadding = 12f;

        /// <summary>오른쪽 상세 패널 폭. 아이템 이름(16pt)이 두 줄 안에 들어가는 값.</summary>
        public const float DetailPaneWidth = 232f;

        /// <summary>격자와 상세 패널 사이 간격.</summary>
        public const float PaneGap = 12f;

        /// <summary>창 테두리와 본문 사이 여백. 창 6개가 각자 14f를 박아 두던 것을 여기로 모았다.</summary>
        public const float WindowPadding = 14f;

        /// <summary>
        /// 창 위쪽이 본문에 앞서 잡아먹는 높이(헤더 + 구분선 + 본문 윗여백) = 57.
        /// 창 크기를 `const`로 계산하는 곳이 많아 메서드가 아니라 상수로 둔다.
        /// </summary>
        public const float ChromeTop = HeaderHeight + SeparatorThickness + BodyPadding;

        /// <summary>창 아래쪽 여백.</summary>
        public const float ChromeBottom = WindowPadding;

        /// <summary>창 좌우 여백 합.</summary>
        public const float ChromeWidth = WindowPadding * 2f;

        // ── 슬롯 ───────────────────────────────────────────────
        /// <summary>
        /// 칸 테두리 두께. 안쪽 면(Body)을 이만큼 밀어 테두리를 드러낸다.
        /// 링 스프라이트(ui_slot_frame)의 실제 두께가 2px이라 그보다 얇게 밀면 링이 반쯤 가려진다.
        /// </summary>
        public const float SlotFrameThickness = 2f;

        /// <summary>마우스를 올렸을 때 칸이 커지는 배율(원본 USS transition 대응).</summary>
        public const float SlotHoverScale = 1.07f;

        /// <summary>호버 스케일 보간 속도. 값이 클수록 즉각적이다(0.1초 안에 도달).</summary>
        public const float SlotHoverSpeed = 16f;

        // ── 폰트 크기 4단계 (ArtDirection 4.3 고정) ──────────────
        public const int FontTitle = 20;
        public const int FontHeading = 16;
        public const int FontBody = 12;
        public const int FontButton = 16;

        // ── 색 ────────────────────────────────────────────────
        /// <summary>패널 구분선. ArtDirection 4.3이 지정한 흰색 알파 0.12 그대로.</summary>
        public static readonly Color Separator = new Color(1f, 1f, 1f, 0.12f);

        /// <summary>헤더 배경(제목 줄). 기존 UIBuilder.WindowTitleBarColor와 같은 값.</summary>
        public static readonly Color HeaderBackground = new Color(1f, 1f, 1f, 0.07f);

        /// <summary>상세 패널 배경. 본문보다 한 단계만 밝게 해 "카드 안의 카드"로 읽히게 한다.</summary>
        public static readonly Color PaneBackground = new Color(1f, 1f, 1f, 0.035f);

        /// <summary>빈 칸 테두리. 있는지 없는지 겨우 보이는 정도.</summary>
        public static readonly Color SlotFrameIdle = new Color(1f, 1f, 1f, 0.10f);

        /// <summary>마우스를 올린 칸의 테두리.</summary>
        public static readonly Color SlotFrameHover = new Color(1f, 1f, 1f, 0.55f);

        /// <summary>본문 글자(=Neutral Gray #CCCCCC).</summary>
        public static readonly Color TextPrimary = new Color(0.8f, 0.8f, 0.8f, 1f);

        /// <summary>부가 설명 글자.</summary>
        public static readonly Color TextDim = new Color(0.55f, 0.55f, 0.55f, 1f);

        /// <summary>
        /// 칸 테두리 색을 상태로부터 정한다. 색상(hue)은 카테고리색 하나만 쓰고
        /// 상태 구분은 오직 **알파(밝기)**로 한다 — 색맹 대응과 야간 가독성을 위해
        /// 기존 슬롯 배경 4단계가 쓰던 규칙을 테두리에도 그대로 적용한 것이다.
        /// </summary>
        public static Color SlotFrame(Color categoryColor, bool filled, bool hovered, bool selected)
        {
            if (!filled) return hovered ? SlotFrameHover : SlotFrameIdle;

            float alpha = selected ? 1f : (hovered ? 0.85f : 0.45f);
            return new Color(categoryColor.r, categoryColor.g, categoryColor.b, alpha);
        }
    }
}
