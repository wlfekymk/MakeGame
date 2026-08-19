using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using MakeGame.Data;
using MakeGame.Player;
using MakeGame.Systems;

namespace MakeGame.UI
{
    /// <summary>
    /// MinimapUI의 전체 지도 창([M]) partial 분할 파일. 창 조립(BuildMapWindow와 그 보조들)·열닫기·
    /// 드래그 위치·축척 막대·탐사 안개·지도 표식 배치·섬 목록/이동·출발 전 체크리스트를
    /// MinimapUI.cs에서 **내용 수정 없이 그대로** 옮겨 왔다(순수 이동 리팩토링).
    /// 양쪽(미니맵/전체 지도)이 함께 쓰는 헬퍼(IsRevealed·MarkerDiameter·ApplyMarkerVisual·
    /// CreateMarker·CreateCircle, 표식 저장소와 세이브 연동 WriteMarksTo/ReadMarksFrom,
    /// GetIslandMarkColor, 스프라이트 static 캐시)는 MinimapUI.cs에 그대로 있다.
    /// </summary>
    public partial class MinimapUI : MonoBehaviour
    {
        // 전체 지도 창은 화면을 꽉 채운다. 그래서 고정 치수로 남는 것은 **오른쪽 목록 단의 폭**과
        // **아래 두 줄**뿐이고, 지도 그림은 남는 자리를 전부 가져간다.
        private const float ListColumnWidth = 328f;
        private const float ListColumnGap = 14f;
        private const float ListHeaderHeight = 20f;
        private const float ChecklistHeight = 34f;
        private const float StatusHeight = 22f;

        /// <summary>본문 아래에 비워 두는 두 줄(체크리스트 + 이동 결과)의 높이.</summary>
        private const float MapFooterHeight = ChecklistHeight + 4f + StatusHeight;

        // 공용 골격(CreateSkinnedWindow)은 본문 크기를 받아야 하지만, 만들자마자 캔버스에
        // stretch시키므로 이 값은 첫 프레임의 씨앗일 뿐이다(기준 해상도 1920×1080).
        private const float SeedBodyWidth = 1920f - UITheme.ChromeWidth;
        private const float SeedBodyHeight = 1080f - UITheme.ChromeTop - UITheme.ChromeBottom;

        // ── 줌 사다리: 아래 7단계는 실루엣(원 마커), 위 7단계는 상세(실제 해안선). 합쳐서 14단계다.
        private const int SilhouetteZoomSteps = 7;
        private const int DetailZoomSteps = 7;
        private const int MapZoomStepCount = SilhouetteZoomSteps + DetailZoomSteps;

        /// <summary>
        /// 최대 줌(13단계)에서 **소형 섬(지형 반지름 50m)**의 반지름이 지도 뷰 짧은 변의 몇 배가 되는가.
        /// 화면 크기에서 유도하는 이유: px를 못 박으면 4K에서는 섬이 손톱만 해지고 720p에서는 화면을
        /// 넘는다. 0.125면 1920×1080에서 뷰 높이 약 946px → 반지름 약 118px로, 64각형 해안선의
        /// 굴곡이 눈으로 읽히는 크기다(요구 범위 100~140px의 한가운데).
        /// </summary>
        private const float DetailSmallIslandFraction = 0.125f;

        /// <summary>
        /// 전체 지도에서 "탐사해서 밝아지는" 원의 반경(미터).
        /// 근거: 이웃 섬까지 중앙값이 1,143m인 배치에서 두 탐사 섬의 밝은 구역이 서로 이어져 항로처럼
        /// 읽히려면 그보다 넉넉히 커야 한다. 4,000m면 이웃 무리를 통째로 덮고, 27km짜리 바다에서도
        /// 밝은 구역이 점이 아니라 띠로 보인다.
        /// </summary>
        private const float ExploredRadiusMeters = 2600f;

        /// <summary>목록·라벨 문자열을 다시 만드는 간격(초). 매 프레임 문자열을 새로 만들지 않기 위함.</summary>
        private const float MapRefreshInterval = 0.2f;

        /// <summary>전체 지도의 기본 바닥 = 아직 아무것도 모르는 바다.</summary>
        private static readonly Color UnexploredSea = new Color(0.018f, 0.024f, 0.030f, 1f);

        // 체크리스트 O/X 색. ✓/✗ 글리프는 LegacyRuntime.ttf에서 보장되지 않아 ASCII O/X를 쓴다.
        private static readonly Color ReadyColor = MedicGreen;
        private static readonly Color MissingColor = DangerRed;

        /// <summary>섬 목록의 한 줄(정보 텍스트 + 표식 버튼 + 이동 버튼).</summary>
        private class IslandRow
        {
            public GameObject rowGo;
            public Text infoLabel;
            public Button travelButton;
            public Button markButton;
            public Text markLabel;
            public int islandId = -1;
            public int shownMark = -1; // 마지막으로 라벨에 반영한 표식(바뀔 때만 문자열을 갈아 끼운다)
        }

        // ── 전체 지도 창
        private RectTransform canvasRect;
        private GameObject mapWindowRoot;
        private RectTransform mapWindowRt;
        private RectTransform mapBodyRt;
        private Text mapSummaryLabel;
        private RectTransform mapMarkerLayer;
        private RectTransform mapFogLayer;
        private RectTransform playerPinRt;
        private RectTransform scaleBarRt;
        private Text scaleLabel;
        private RectTransform mapViewRt;
        private readonly List<IslandMarker> mapMarkers = new List<IslandMarker>();
        private readonly List<Image> exploredHalos = new List<Image>();
        private float mapPixelsPerMeter = 0.02f;

        /// <summary>지도 한가운데에 오는 월드 좌표. 팬(드래그)으로 움직인다.</summary>
        private Vector2 mapCenter = Vector2.zero;

        // ── 줌/팬 상태. 사다리는 창 크기에 따라 달라지므로 **쓰는 자리에서** 다시 잡는다
        // (Start에서 한 번만 채워 두면 아직 레이아웃이 안 돈 크기가 그대로 굳는다).
        private int mapZoomLevel = 0;
        private float mapFitPixelsPerMeter = 0.02f;   // 레벨 0 = 전부 보이는 배율
        private float mapZoomStepRatio = 1.4f;        // 단계당 배율(등비수열)
        private Vector2 mapFitCenter = Vector2.zero;  // 섬 경계 상자의 중심
        private Vector2 mapBoundsMin = Vector2.zero;  // 팬 한계(섬 경계 상자 + 여유)
        private Vector2 mapBoundsMax = Vector2.zero;
        private Vector2 lastMapViewSize = Vector2.zero;
        private bool mapViewDirty = true;

        /// <summary>섬별 해안선 배열 캐시. 인스턴스 필드라 씬을 다시 로드하면 저절로 사라진다.</summary>
        private readonly Dictionary<int, float[]> islandRadialMasks = new Dictionary<int, float[]>();

        // ── 섬 목록(전체 지도 창 오른쪽 열로 흡수됨)
        private RectTransform listContainer;
        private Text statusLabel;
        private Text checklistLabel;
        private readonly List<IslandRow> islandRows = new List<IslandRow>();

        private string lastTravelStatus = "";
        private string lastDisplayedChecklist = null;
        private string lastDisplayedMapSummary = null;

        private bool IsMapOpen => mapWindowRoot != null && mapWindowRoot.activeSelf;
        // ────────────────────────────────────────────────────────────────────────
        // B. 전체 지도 창 ([M])
        // ────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// 전체 지도 창을 만든다. 제목 표시줄·닫기 버튼·드래그·여백은 공용 골격
        /// (UIBuilder.CreateSkinnedWindow)이 맡는다 - 창마다 다른 규격이 생기지 않게 하기 위함이다.
        /// 왼쪽이 지도, 오른쪽이 섬 목록이다 - 예전에 따로 뜨던 목록 패널은 여기로 흡수됐다.
        /// </summary>
        private void BuildMapWindow()
        {
            var canvas = UIBuilder.CreateCanvas("WorldMapCanvas", sortOrder: 11);
            canvasRect = canvas.GetComponent<RectTransform>();

            // 창 6개가 공유하는 골격. 호출자는 **본문 크기만** 말하고 제목줄·여백은 골격이 정한다.
            var frame = UIBuilder.CreateSkinnedWindow(canvas.transform, "WorldMapWindow",
                SeedBodyWidth, SeedBodyHeight, $"전체 지도 ({toggleKey})", canvasRect, () => SetMapOpen(false));

            mapWindowRt = frame.window;
            mapBodyRt = frame.body;
            mapWindowRoot = mapWindowRt.gameObject;

            // 게임 화면 전체 크기. 골격이 준 고정 크기를 버리고 캔버스에 stretch시킨다 - 본문(frame.body)이
            // 앵커 기반이라 창을 늘리면 본문도 그대로 따라 늘어나고, 해상도가 바뀌어도 다시 맞출 것이 없다.
            mapWindowRt.anchorMin = Vector2.zero;
            mapWindowRt.anchorMax = Vector2.one;
            mapWindowRt.pivot = new Vector2(0.5f, 0.5f);
            mapWindowRt.offsetMin = Vector2.zero;
            mapWindowRt.offsetMax = Vector2.zero;

            // 탐사율 한 줄은 제목 **옆**이다. 이동 결과 문구와 체크리스트는 본문에 그대로 둔다.
            mapSummaryLabel = frame.status;

            // 전체 화면 창은 옮길 이유가 없고, 제목줄 드래그가 살아 있으면 stretch 앵커에
            // anchoredPosition을 써 넣어 창이 화면 밖으로 밀려난다. 그래서 **헤더의 클릭 판정을 끈다**
            // (자식인 닫기 버튼은 자기 판정을 따로 가지므로 그대로 눌린다).
            // 손잡이 컴포넌트 자체는 남긴다 - CursorLockController가 "활성 UIDragHandle이 있는가"로
            // 창 열림을 판정하므로, 떼어내면 지도를 열어도 커서가 잠긴 채라 아무것도 누를 수 없다.
            var headerImage = frame.header != null ? frame.header.GetComponent<Image>() : null;
            if (headerImage != null)
                headerImage.raycastTarget = false;

            BuildMapView();
            BuildIslandListColumn();
            BuildMapFooter();
        }

        /// <summary>지도 그림 영역(왼쪽 열): 검은 바다 → 탐사 원반 → 섬 표식 → 플레이어 → 축척자.</summary>
        private void BuildMapView()
        {
            // 왼쪽 = 지도(남는 자리 전부), 오른쪽 = 목록 단(폭 고정), 아래 두 줄 = 체크리스트/상태.
            // 네 변을 모두 앵커로 묶었으므로 해상도가 바뀌면 지도만 알아서 넓어진다.
            var view = UIBuilder.CreatePanel(
                mapBodyRt, "MapView",
                anchorMin: Vector2.zero, anchorMax: Vector2.one,
                offsetMin: new Vector2(0f, MapFooterHeight),
                offsetMax: new Vector2(-(ListColumnWidth + ListColumnGap), 0f),
                color: UnexploredSea);

            view.pivot = new Vector2(0.5f, 0.5f);
            view.offsetMin = new Vector2(0f, MapFooterHeight);
            view.offsetMax = new Vector2(-(ListColumnWidth + ListColumnGap), 0f);
            mapViewRt = view;
            view.gameObject.AddComponent<RectMask2D>();

            // 드래그(팬)와 휠(줌)은 **이 사각형 안에서만** 받는다. Input.GetAxis로 휠을 읽으면 지도
            // 밖 어디서 굴려도 줌이 되고, 오른쪽 목록의 세로 스크롤과 같은 입력을 놓고 싸운다.
            var panZoom = view.gameObject.AddComponent<MapPanZoomInput>();
            panZoom.onPan = OnMapPan;
            panZoom.onZoom = OnMapZoom;

            // 탐사로 밝아진 구역. 섬 표식보다 아래에 깔려야 하므로 먼저 만든다.
            var fogGo = new GameObject("ExploredLayer", typeof(RectTransform));
            fogGo.transform.SetParent(view, false);
            mapFogLayer = fogGo.GetComponent<RectTransform>();
            mapFogLayer.anchorMin = new Vector2(0.5f, 0.5f);
            mapFogLayer.anchorMax = new Vector2(0.5f, 0.5f);
            mapFogLayer.anchoredPosition = Vector2.zero;
            mapFogLayer.sizeDelta = Vector2.zero;

            var markerGo = new GameObject("MarkerLayer", typeof(RectTransform));
            markerGo.transform.SetParent(view, false);
            mapMarkerLayer = markerGo.GetComponent<RectTransform>();
            mapMarkerLayer.anchorMin = new Vector2(0.5f, 0.5f);
            mapMarkerLayer.anchorMax = new Vector2(0.5f, 0.5f);
            mapMarkerLayer.anchoredPosition = Vector2.zero;
            mapMarkerLayer.sizeDelta = Vector2.zero;

            var pinGo = new GameObject("PlayerPin", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            pinGo.transform.SetParent(view, false);
            playerPinRt = pinGo.GetComponent<RectTransform>();
            playerPinRt.anchorMin = new Vector2(0.5f, 0.5f);
            playerPinRt.anchorMax = new Vector2(0.5f, 0.5f);
            playerPinRt.pivot = new Vector2(0.5f, 0.5f);
            playerPinRt.sizeDelta = new Vector2(16f, 16f);
            var pinImage = pinGo.GetComponent<Image>();
            pinImage.color = Color.white;
            pinImage.raycastTarget = false;
            if (arrowSprite != null)
            {
                pinImage.sprite = arrowSprite;
                pinImage.type = Image.Type.Simple;
                pinImage.preserveAspect = true;
            }

            // 방위 표시(북쪽 고정).
            var north = UIBuilder.CreateText(view, "North", "N", UITheme.FontHeading, new Color(1f, 1f, 1f, 0.8f), TextAnchor.UpperCenter);
            north.raycastTarget = false;
            north.rectTransform.anchorMin = new Vector2(0.5f, 1f);
            north.rectTransform.anchorMax = new Vector2(0.5f, 1f);
            north.rectTransform.pivot = new Vector2(0.5f, 1f);
            north.rectTransform.anchoredPosition = new Vector2(0f, -4f);
            north.rectTransform.sizeDelta = new Vector2(28f, 20f);

            // 축척자: 길이가 곧 거리다. 지도 축척이 섬 배치에 맞춰 자동으로 정해지므로, 숫자만 적고
            // 막대를 안 그리면 "이 지도에서 1200m가 얼마나 되는지"를 눈으로 잴 방법이 없어진다.
            var barGo = new GameObject("ScaleBar", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            barGo.transform.SetParent(view, false);
            scaleBarRt = barGo.GetComponent<RectTransform>();
            scaleBarRt.anchorMin = new Vector2(0f, 0f);
            scaleBarRt.anchorMax = new Vector2(0f, 0f);
            scaleBarRt.pivot = new Vector2(0f, 0f);
            scaleBarRt.anchoredPosition = new Vector2(10f, 12f);
            scaleBarRt.sizeDelta = new Vector2(80f, 2f);
            var barImage = barGo.GetComponent<Image>();
            barImage.color = new Color(1f, 1f, 1f, 0.7f);
            barImage.raycastTarget = false;

            // 조작 안내. 드래그/휠은 눌러 보기 전에는 알 수 없는 조작이라 지도 위에 상시로 적어 둔다.
            var hint = UIBuilder.CreateText(view, "MapHint", "드래그: 지도 이동 · 휠 또는 [+]/[-]: 줌",
                UITheme.FontBody, new Color(1f, 1f, 1f, 0.55f), TextAnchor.LowerLeft);
            hint.raycastTarget = false;
            hint.horizontalOverflow = HorizontalWrapMode.Overflow;
            hint.rectTransform.anchorMin = new Vector2(0f, 0f);
            hint.rectTransform.anchorMax = new Vector2(0f, 0f);
            hint.rectTransform.pivot = new Vector2(0f, 0f);
            hint.rectTransform.anchoredPosition = new Vector2(10f, 32f);
            hint.rectTransform.sizeDelta = new Vector2(320f, 14f);

            scaleLabel = UIBuilder.CreateText(view, "ScaleLabel", "", UITheme.FontBody, new Color(1f, 1f, 1f, 0.7f), TextAnchor.LowerLeft);
            scaleLabel.raycastTarget = false;
            scaleLabel.horizontalOverflow = HorizontalWrapMode.Overflow;
            scaleLabel.rectTransform.anchorMin = new Vector2(0f, 0f);
            scaleLabel.rectTransform.anchorMax = new Vector2(0f, 0f);
            scaleLabel.rectTransform.pivot = new Vector2(0f, 0f);
            scaleLabel.rectTransform.anchoredPosition = new Vector2(10f, 16f);
            scaleLabel.rectTransform.sizeDelta = new Vector2(160f, 14f);
        }

        /// <summary>섬 목록 열(오른쪽). 예전의 독립 "섬 목록" 패널이 이 열로 흡수됐다.</summary>
        private void BuildIslandListColumn()
        {
            // 목록 단은 창의 **오른쪽 끝**에 붙인다(폭 328 유지). 왼쪽 기준 오프셋으로 두면 창이
            // 화면 크기를 따라가는 순간 해상도마다 지도 위로 겹치거나 화면 밖으로 나간다.
            var header = UIBuilder.CreateText(mapBodyRt, "ListHeader", "섬 목록 · [표식]으로 고갈/자원/위험 표시 (지도에선 Shift+클릭)", UITheme.FontBody, NeutralGray, TextAnchor.MiddleLeft);
            header.raycastTarget = false;
            header.horizontalOverflow = HorizontalWrapMode.Overflow;
            header.rectTransform.anchorMin = new Vector2(1f, 1f);
            header.rectTransform.anchorMax = new Vector2(1f, 1f);
            header.rectTransform.pivot = new Vector2(1f, 1f);
            header.rectTransform.anchoredPosition = Vector2.zero;
            header.rectTransform.sizeDelta = new Vector2(ListColumnWidth, ListHeaderHeight);

            // [B52] 50섬 대응: 행이 50개면 목록 전체 높이가 약 1,500px(행 26 + 간격 4)로 열 높이
            // (약 446px)의 3배가 넘는다. 예전처럼 컨테이너를 창에 직접 붙이면 마스크가 없어 행들이
            // 창 아래 체크리스트/상태 줄을 덮고 화면 밖까지 그대로 그려진다. InventorySlotView와 같은
            // 조립(ScrollRect + RectMask2D 뷰포트 + ContentSizeFitter 콘텐츠)으로 세로 스크롤을 붙인다.
            var scrollGo = new GameObject("IslandListScroll", typeof(RectTransform), typeof(ScrollRect));
            scrollGo.transform.SetParent(mapBodyRt, false);
            var scrollRt = scrollGo.GetComponent<RectTransform>();
            scrollRt.anchorMin = new Vector2(1f, 0f);
            scrollRt.anchorMax = new Vector2(1f, 1f);
            scrollRt.pivot = new Vector2(1f, 1f);
            scrollRt.offsetMin = new Vector2(-ListColumnWidth, MapFooterHeight);
            scrollRt.offsetMax = new Vector2(0f, -(ListHeaderHeight + 4f));

            // 뷰포트. 아주 옅은 배경을 까는 이유는 InventorySlotView와 같다 - (1) 목록 영역 경계가
            // 보이고, (2) raycastTarget이 켜져 있어야 행 사이 빈 자리에서 끌어 스크롤하는 조작이 먹는다.
            var viewport = UIBuilder.CreatePanel(scrollRt, "Viewport",
                anchorMin: Vector2.zero, anchorMax: Vector2.one,
                offsetMin: Vector2.zero, offsetMax: Vector2.zero,
                color: new Color(1f, 1f, 1f, 0.02f));
            viewport.gameObject.AddComponent<RectMask2D>();

            var listGo = new GameObject("IslandList", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            listGo.transform.SetParent(viewport, false);
            listContainer = listGo.GetComponent<RectTransform>();
            listContainer.anchorMin = new Vector2(0f, 1f);
            listContainer.anchorMax = new Vector2(1f, 1f);
            listContainer.pivot = new Vector2(0.5f, 1f);
            listContainer.anchoredPosition = Vector2.zero;
            listContainer.sizeDelta = new Vector2(0f, 0f);

            var vlg = listGo.GetComponent<VerticalLayoutGroup>();
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            vlg.spacing = 4f;
            vlg.childAlignment = TextAnchor.UpperLeft;

            var fitter = listGo.GetComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var scroll = scrollGo.GetComponent<ScrollRect>();
            scroll.viewport = viewport;
            scroll.content = listContainer;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 30f; // 휠 한 칸 = 한 행(26px + 간격 4px)
        }

        /// <summary>창 아래쪽 두 줄: 출발 전 준비 체크리스트와 이동 결과 문구.</summary>
        private void BuildMapFooter()
        {
            // 출발 전 준비 체크리스트(Design_Progression.md 3장 단계 3 (b)).
            // **이동을 막지 않는다** - 이 줄은 어떤 버튼의 interactable도 건드리지 않는다.
            checklistLabel = UIBuilder.CreateText(mapBodyRt, "Checklist", "", UITheme.FontBody, new Color(0.85f, 0.85f, 0.85f, 1f), TextAnchor.LowerLeft);
            checklistLabel.raycastTarget = false;
            checklistLabel.rectTransform.anchorMin = new Vector2(0f, 0f);
            checklistLabel.rectTransform.anchorMax = new Vector2(1f, 0f);
            checklistLabel.rectTransform.pivot = new Vector2(0.5f, 0f);
            checklistLabel.rectTransform.anchoredPosition = new Vector2(0f, StatusHeight + 4f);
            // 두 줄 자리를 유지한다. 한 줄로 두면 문구가 폭을 넘는 순간 둘째 줄이 Truncate로 통째로
            // 사라지고, 정작 중요한 도구 안내만 조용히 잘려나간다(힌트가 반만 보이면 함정이 된다).
            checklistLabel.rectTransform.sizeDelta = new Vector2(0f, ChecklistHeight);

            statusLabel = UIBuilder.CreateText(mapBodyRt, "Status", "", UITheme.FontBody, new Color(1f, 0.9f, 0.4f, 1f), TextAnchor.LowerLeft);
            statusLabel.raycastTarget = false;
            statusLabel.horizontalOverflow = HorizontalWrapMode.Overflow;
            statusLabel.rectTransform.anchorMin = new Vector2(0f, 0f);
            statusLabel.rectTransform.anchorMax = new Vector2(1f, 0f);
            statusLabel.rectTransform.pivot = new Vector2(0.5f, 0f);
            statusLabel.rectTransform.anchoredPosition = Vector2.zero;
            statusLabel.rectTransform.sizeDelta = new Vector2(0f, StatusHeight);
        }

        /// <summary>
        /// 전체 지도 창을 열거나 닫는다. 창이 캔버스에 stretch돼 있어 자리를 기억하거나 화면 안으로
        /// 밀어 넣을 것이 없다. 대신 축척 사다리는 **열 때마다** 다시 잡는다 - 창 크기가 해상도를
        /// 따라가므로 지난번에 잡아둔 값이 지금 창에 맞는다는 보장이 없다.
        /// </summary>
        private void SetMapOpen(bool open)
        {
            if (mapWindowRoot == null)
                return;

            mapWindowRoot.SetActive(open);

            if (!open)
                return;

            // 열 때는 항상 레벨 0(= 전부 보이는 배율)에서 시작한다. 지난번에 확대해 둔 자리에서
            // 열리면 "지도를 열었는데 바다만 보인다"가 된다.
            RecalculateMapScale(true);
            mapRefreshTimer = 0f;
            RefreshMapWindow();
        }

        /// <summary>
        /// 지도 축척을 정한다. **oceanSize(40000m)가 아니라 실제 섬이 퍼져 있는 범위에 맞춘다.**
        /// 섬은 baseDistanceStep(1200) × 섬 번호 + jitter 규칙으로 배치되므로 가장 먼 섬도 원점에서
        /// 약 10km 안쪽이다. 40km 기준으로 그리면 지도의 94%가 빈 바다가 되고 섬 9개가 가운데
        /// 한 줌으로 뭉친다(픽셀을 4배 낭비한다).
        ///
        /// 여기서 정하는 것은 **사다리의 두 끝**이다: 레벨 0 = 경계 상자가 통째로 들어오는 배율,
        /// 레벨 13 = 소형 섬이 상세하게 읽히는 배율. 그 사이는 등비수열로 채운다.
        /// resetView가 true면 보던 자리와 줌 단계도 처음으로 되돌린다(창을 새로 열 때).
        /// </summary>
        private void RecalculateMapScale(bool resetView)
        {
            // **원점 기준 반지름이 아니라 섬 전체의 경계 상자에 맞춘다.**
            // 예전 방식(원점에서 가장 먼 섬까지의 거리를 반지름으로)은 배치가 원점을 중심으로
            // 고르게 퍼져 있을 때만 맞는다. 2026-08-19 배치처럼 시작 섬이 구석에 있고 월드가
            // 사각형이면, 모서리까지의 거리(반폭 × 1.41)가 반지름이 되어 지도의 절반이 빈 바다로
            // 낭비된다(실기에서 섬 뭉치가 화면의 40%만 차지했다).
            float minX = float.MaxValue, maxX = float.MinValue;
            float minZ = float.MaxValue, maxZ = float.MinValue;
            bool any = false;

            if (worldMapManager != null && worldMapManager.islands != null)
            {
                for (int i = 0; i < worldMapManager.islands.Count; i++)
                {
                    var island = worldMapManager.islands[i];
                    if (island == null)
                        continue;

                    float pad = IslandSizeMetrics.GetTerrainRadius(island.size);
                    minX = Mathf.Min(minX, island.mapPosition.x - pad);
                    maxX = Mathf.Max(maxX, island.mapPosition.x + pad);
                    minZ = Mathf.Min(minZ, island.mapPosition.z - pad);
                    maxZ = Mathf.Max(maxZ, island.mapPosition.z + pad);
                    any = true;
                }
            }

            // 플레이어가 섬 밖(항해 중)에 있어도 화면에 남아야 한다.
            if (player != null)
            {
                minX = any ? Mathf.Min(minX, player.position.x) : player.position.x;
                maxX = any ? Mathf.Max(maxX, player.position.x) : player.position.x;
                minZ = any ? Mathf.Min(minZ, player.position.z) : player.position.z;
                maxZ = any ? Mathf.Max(maxZ, player.position.z) : player.position.z;
                any = true;
            }

            Vector2 viewSize = GetMapViewSize();
            float viewMin = Mathf.Min(viewSize.x, viewSize.y);

            if (!any)
            {
                mapFitCenter = Vector2.zero;
                mapBoundsMin = new Vector2(-1000f, -1000f);
                mapBoundsMax = new Vector2(1000f, 1000f);
                mapFitPixelsPerMeter = (viewMin * 0.5f - 12f) / 1000f;
            }
            else
            {
                mapFitCenter = new Vector2((minX + maxX) * 0.5f, (minZ + maxZ) * 0.5f);

                // 팬 한계는 경계 상자에 탐사 원반 반경의 절반을 덧댄 사각형이다 - 가장자리 섬의 밝은
                // 구역까지는 따라갈 수 있어야 하고, 그 밖의 빈 바다로는 못 나가야 한다.
                float pad = ExploredRadiusMeters * 0.5f;
                mapBoundsMin = new Vector2(minX - pad, minZ - pad);
                mapBoundsMax = new Vector2(maxX + pad, maxZ + pad);

                // 레벨 0은 뷰의 **짧은 변**에 가로·세로 중 큰 쪽을 맞춘다(한쪽이 잘리면 섬이 사라진다).
                float halfSpan = Mathf.Max(maxX - minX, maxZ - minZ) * 0.5f + pad;
                halfSpan = Mathf.Max(halfSpan, 1000f);
                mapFitPixelsPerMeter = (viewMin * 0.5f - 12f) / halfSpan;
            }

            mapFitPixelsPerMeter = Mathf.Max(mapFitPixelsPerMeter, 0.000001f);

            // 레벨 13의 배율은 "소형 섬 반지름이 뷰 짧은 변의 1/8"에서 거꾸로 푼다. 최소한 레벨 0의
            // 2배는 되게 눌러 둔다 - 섬이 한 개뿐인 이상한 월드에서 사다리가 뒤집히지 않게 하는 바닥이다.
            float smallRadius = Mathf.Max(1f, IslandSizeMetrics.GetTerrainRadius(IslandSize.Small));
            float detailPixelsPerMeter = Mathf.Max(
                viewMin * DetailSmallIslandFraction / smallRadius,
                mapFitPixelsPerMeter * 2f);

            // 단계당 배율이 일정해야 휠 한 칸의 체감이 어디서나 같다(등비수열).
            mapZoomStepRatio = Mathf.Pow(detailPixelsPerMeter / mapFitPixelsPerMeter, 1f / (MapZoomStepCount - 1));

            if (resetView)
            {
                mapZoomLevel = 0;
                mapCenter = mapFitCenter;
            }

            lastMapViewSize = viewSize;
            ApplyMapZoom();
        }

        /// <summary>
        /// 지도 뷰의 지금 크기(px). 아직 레이아웃이 한 번도 돌지 않았으면 기준 해상도에서 어림한다 -
        /// 0으로 나눈 축척이 한 프레임이라도 생기면 표식 50개가 전부 원점에 뭉친다.
        /// </summary>
        private Vector2 GetMapViewSize()
        {
            if (mapViewRt != null)
            {
                Vector2 size = mapViewRt.rect.size;
                if (size.x > 1f && size.y > 1f)
                    return size;
            }

            return new Vector2(
                Mathf.Max(320f, 1920f - UITheme.ChromeWidth - ListColumnWidth - ListColumnGap),
                Mathf.Max(240f, 1080f - UITheme.ChromeTop - UITheme.ChromeBottom - MapFooterHeight));
        }

        /// <summary>지금 줌 단계의 배율을 실제 값에 반영하고, 팬 위치를 다시 가둔다.</summary>
        private void ApplyMapZoom()
        {
            mapZoomLevel = Mathf.Clamp(mapZoomLevel, 0, MapZoomStepCount - 1);
            mapPixelsPerMeter = mapFitPixelsPerMeter * Mathf.Pow(mapZoomStepRatio, mapZoomLevel);
            ClampMapCenter();
            UpdateScaleBar();
            mapViewDirty = true;
        }

        /// <summary>위 7단계에서만 실제 해안선을 그린다(아래 7단계는 지금까지처럼 단순한 원 마커).</summary>
        private bool IsDetailZoom => mapZoomLevel >= SilhouetteZoomSteps;

        /// <summary>
        /// 줌 단계를 바꾼다. focusLocal(뷰 중심 기준 px)에 있던 월드 좌표가 **그 자리에 그대로
        /// 남도록** 중심을 다시 잡는다 - 이게 없으면 확대할수록 보던 곳이 화면 밖으로 달아난다.
        /// </summary>
        private void SetMapZoomLevel(int level, Vector2 focusLocal)
        {
            int clamped = Mathf.Clamp(level, 0, MapZoomStepCount - 1);
            if (clamped == mapZoomLevel)
                return;

            Vector2 anchorWorld = mapCenter + focusLocal / Mathf.Max(0.000001f, mapPixelsPerMeter);

            mapZoomLevel = clamped;
            mapPixelsPerMeter = mapFitPixelsPerMeter * Mathf.Pow(mapZoomStepRatio, mapZoomLevel);
            mapCenter = anchorWorld - focusLocal / Mathf.Max(0.000001f, mapPixelsPerMeter);

            ClampMapCenter();
            UpdateScaleBar();
            mapViewDirty = true;
            mapRefreshTimer = 0f;   // 실루엣↔상세 전환과 이름표를 다음 프레임에 바로 반영한다
        }

        /// <summary>키보드 줌의 기준점. 커서가 지도 위에 있으면 커서, 아니면 뷰 한가운데다.</summary>
        private Vector2 MapZoomFocusLocal()
        {
            if (mapViewRt != null
                && RectTransformUtility.ScreenPointToLocalPointInRectangle(mapViewRt, Input.mousePosition, null, out var local)
                && mapViewRt.rect.Contains(local))
                return local;

            return Vector2.zero;
        }

        /// <summary>드래그한 픽셀만큼 지도를 민다(끈 만큼 월드가 따라온다).</summary>
        private void OnMapPan(Vector2 deltaPixels)
        {
            if (mapPixelsPerMeter <= 0.000001f)
                return;

            mapCenter -= deltaPixels / mapPixelsPerMeter;
            ClampMapCenter();
            mapViewDirty = true;
        }

        /// <summary>휠 한 칸 = 줌 한 단계. 커서 아래 지점을 기준으로 확대·축소한다.</summary>
        private void OnMapZoom(float scroll, Vector2 focusLocal)
        {
            if (Mathf.Abs(scroll) < 0.01f)
                return;

            SetMapZoomLevel(mapZoomLevel + (scroll > 0f ? 1 : -1), focusLocal);
        }

        /// <summary>
        /// 지도를 아무 데나 끌고 가지 못하게 막는다. 보이는 사각형이 섬 경계 상자 **안에** 머물도록
        /// 가두고, 뷰가 상자보다 넓은 축에서는 가운데에 고정한다. 레벨 0은 두 축 모두 그 경우라
        /// 팬이 저절로 잠긴다(다 보이는데 끌 수 있게 두면 화면이 빈 바다로 미끄러진다).
        /// </summary>
        private void ClampMapCenter()
        {
            Vector2 viewSize = GetMapViewSize();
            float ppm = Mathf.Max(0.000001f, mapPixelsPerMeter);

            mapCenter.x = ClampMapAxis(mapCenter.x, mapBoundsMin.x, mapBoundsMax.x, viewSize.x * 0.5f / ppm);
            mapCenter.y = ClampMapAxis(mapCenter.y, mapBoundsMin.y, mapBoundsMax.y, viewSize.y * 0.5f / ppm);
        }

        private static float ClampMapAxis(float center, float min, float max, float halfExtent)
        {
            if (halfExtent * 2f >= max - min)
                return (min + max) * 0.5f;

            return Mathf.Clamp(center, min + halfExtent, max - halfExtent);
        }

        /// <summary>월드 좌표가 지금 보이는 사각형(여유 포함) 안에 걸리는지. 화면 밖 물체를 끄는 데 쓴다.</summary>
        private bool IsWithinMapView(Vector3 worldPosition, float marginX, float marginZ)
        {
            return Mathf.Abs(worldPosition.x - mapCenter.x) <= marginX
                && Mathf.Abs(worldPosition.z - mapCenter.y) <= marginZ;
        }

        /// <summary>
        /// 상세 단계에서 쓸 섬 윤곽. **지형에 주입되는 것과 같은 배열**을 얻으려고
        /// WorldMapManager.GetMaldivesRadialMask를 그대로 부른다 - MaldivesLayout.Islands[i].mask를
        /// 직접 읽으면 회전된 사본을 쓰는 시작 섬(0번)만 모양이 돌아간다.
        /// 섬마다 한 번만 받아 들고 있는 이유: 0번은 부를 때마다 새 배열을 만드는데,
        /// MapIslandShape.Configure는 **배열 참조가 같을 때만** 메시 재굽기를 건너뛴다.
        /// </summary>
        private float[] GetIslandRadialMask(int islandId)
        {
            if (islandRadialMasks.TryGetValue(islandId, out float[] cached))
                return cached;

            float[] mask = worldMapManager != null ? worldMapManager.GetMaldivesRadialMask(islandId) : null;
            islandRadialMasks[islandId] = mask;   // null도 담는다(실측 배치가 꺼진 월드에서 매번 되묻지 않게)
            return mask;
        }

        /// <summary>
        /// 축척자 길이를 60~170px에 들어오는 "깔끔한" 거리로 맞춘다.
        /// 후보를 10m까지 내린 이유: 14단계 사다리는 양 끝의 배율이 80배 가까이 차이 나서, 예전
        /// 후보(100m~)만 두면 최대 줌에서 어느 것도 띠에 못 들어와 막대가 화면을 몇 배 넘어갔다.
        /// </summary>
        private void UpdateScaleBar()
        {
            if (scaleBarRt == null || scaleLabel == null)
                return;

            float[] candidates = { 10f, 25f, 50f, 100f, 200f, 500f, 1000f, 2000f, 5000f, 10000f };
            float chosen = candidates[0];
            float bestGap = float.MaxValue;

            for (int i = 0; i < candidates.Length; i++)
            {
                float pixels = candidates[i] * mapPixelsPerMeter;
                if (pixels >= 60f && pixels <= 170f)
                {
                    chosen = candidates[i];
                    bestGap = 0f;
                    break;
                }

                // 어느 후보도 띠에 못 들어오면 115px에 가장 가까운 것을 쓴다(막대가 사라지거나
                // 화면을 넘는 쪽이 눈금이 어중간한 쪽보다 훨씬 나쁘다).
                float gap = Mathf.Abs(pixels - 115f);
                if (gap < bestGap)
                {
                    bestGap = gap;
                    chosen = candidates[i];
                }
            }

            scaleBarRt.sizeDelta = new Vector2(Mathf.Clamp(chosen * mapPixelsPerMeter, 20f, 400f), 2f);
            scaleLabel.text = $"{chosen:F0}m · 줌 {mapZoomLevel + 1}/{MapZoomStepCount} ({(IsDetailZoom ? "상세" : "실루엣")})";
        }

        /// <summary>월드 좌표(X,Z)를 지도 위 픽셀 좌표로 바꾼다. 지도 중심은 섬 경계 상자의 중심(mapCenter)이다.</summary>
        private Vector2 WorldToMap(Vector3 worldPosition)
        {
            return (new Vector2(worldPosition.x, worldPosition.z) - mapCenter) * mapPixelsPerMeter;
        }

        /// <summary>
        /// 전체 지도를 갱신한다. 플레이어 표시는 매 프레임, 나머지(문자열을 만드는 목록·이름표)는
        /// 0.2초 간격으로 갱신해 매 프레임 문자열을 새로 만들지 않는다.
        /// </summary>
        private void RefreshMapWindow()
        {
            if (worldMapManager == null || worldMapManager.islands == null)
                return;

            // 해상도(창 크기)가 바뀌면 뷰가 넓어진 만큼 사다리를 다시 잡는다. 보던 자리와 줌 단계는
            // 그대로 둔다. **여기서 보장하는 이유**: 축척을 Start나 창 조립 시점에만 잡아 두면 아직
            // 레이아웃이 돌지 않은 크기가 그대로 굳는다.
            Vector2 viewSize = GetMapViewSize();
            if (Mathf.Abs(viewSize.x - lastMapViewSize.x) > 1f || Mathf.Abs(viewSize.y - lastMapViewSize.y) > 1f)
                RecalculateMapScale(false);

            if (player != null && playerPinRt != null)
            {
                playerPinRt.anchoredPosition = WorldToMap(player.position);
                playerPinRt.localEulerAngles = new Vector3(0f, 0f, -player.eulerAngles.y);
            }

            mapRefreshTimer -= Time.unscaledDeltaTime;
            if (mapRefreshTimer > 0f)
            {
                // 끄는 동안에는 **자리만** 옮긴다. 이름표까지 매 프레임 다시 만들면 50개 문자열이
                // 프레임마다 새로 할당돼 드래그 내내 GC가 달아오른다.
                if (mapViewDirty)
                {
                    mapViewDirty = false;
                    RefreshExploredArea();
                    RefreshMapMarkers(false);
                }

                return;
            }

            mapRefreshTimer = MapRefreshInterval;
            mapViewDirty = false;

            RefreshExploredArea();
            RefreshMapMarkers(true);
            RefreshList();
        }

        /// <summary>
        /// "가본 곳만 밝다"를 그린다. 바탕이 검은 바다이고, 탐사한 섬(+ 지금 서 있는 자리) 주변에만
        /// 반경 ExploredRadiusMeters(4,000m)짜리 밝은 원반을 깐다. 원반은 풀링하며 갱신마다 새로 만들지 않는다.
        /// </summary>
        private void RefreshExploredArea()
        {
            var islands = worldMapManager.islands;

            int needed = 0;
            for (int i = 0; i < islands.Count; i++)
            {
                if (IsRevealed(islands[i]))
                    needed++;
            }

            if (player != null)
                needed++; // 지금 서 있는 자리도 밝다(막 도착해 아직 isDiscovered가 아닐 수 있다)

            while (exploredHalos.Count < needed)
                exploredHalos.Add(CreateCircle(mapFogLayer, $"Explored{exploredHalos.Count}", 10f, DeepOcean, dotSprite));

            float diameter = ExploredRadiusMeters * 2f * mapPixelsPerMeter;
            Color haloColor = DeepOcean;
            haloColor.a = 0.5f;

            // 최대 줌에서는 원반 하나가 1만 px을 넘는다. 화면에 걸리지도 않는 원반까지 켜 두면
            // 50장이 통째로 그려지며 오버드로가 화면을 몇 겹씩 덮는다.
            Vector2 viewSize = GetMapViewSize();
            float ppm = Mathf.Max(0.000001f, mapPixelsPerMeter);
            float haloMarginX = viewSize.x * 0.5f / ppm + ExploredRadiusMeters;
            float haloMarginZ = viewSize.y * 0.5f / ppm + ExploredRadiusMeters;

            int used = 0;
            for (int i = 0; i < islands.Count; i++)
            {
                if (!IsRevealed(islands[i]))
                    continue;

                if (!IsWithinMapView(islands[i].mapPosition, haloMarginX, haloMarginZ))
                    continue;

                var halo = exploredHalos[used++];
                halo.rectTransform.anchoredPosition = WorldToMap(islands[i].mapPosition);
                halo.rectTransform.sizeDelta = new Vector2(diameter, diameter);
                halo.color = haloColor;
                halo.gameObject.SetActive(true);
            }

            if (player != null)
            {
                var halo = exploredHalos[used++];
                halo.rectTransform.anchoredPosition = WorldToMap(player.position);
                halo.rectTransform.sizeDelta = new Vector2(diameter, diameter);
                halo.color = haloColor;
                halo.gameObject.SetActive(true);
            }

            for (int i = used; i < exploredHalos.Count; i++)
                exploredHalos[i].gameObject.SetActive(false);
        }

        /// <summary>
        /// 전체 지도의 섬 표식을 갱신한다. 크기는 실제 지형 반지름 비례, 이름표는 탐사한 섬만.
        /// rebuildLabels가 false면 자리·크기·모양만 손대고 이름표 문자열은 그대로 둔다(팬 중).
        /// </summary>
        private void RefreshMapMarkers(bool rebuildLabels)
        {
            var islands = worldMapManager.islands;
            EnsureMarkerCount(mapMarkers, mapMarkerLayer, islands.Count, true);

            Vector2 viewSize = GetMapViewSize();
            float viewMin = Mathf.Min(viewSize.x, viewSize.y);
            float ppm = Mathf.Max(0.000001f, mapPixelsPerMeter);
            bool detail = IsDetailZoom;

            for (int i = 0; i < islands.Count; i++)
            {
                var island = islands[i];
                var marker = mapMarkers[i];
                bool revealed = IsRevealed(island);

                // 화면 밖 섬은 통째로 끈다. 여유는 섬 반지름 + 이름표가 삐져나오는 폭만큼이다.
                float margin = IslandSizeMetrics.GetTerrainRadius(island.size) + 120f / ppm;
                if (!IsWithinMapView(island.mapPosition,
                        viewSize.x * 0.5f / ppm + margin,
                        viewSize.y * 0.5f / ppm + margin))
                {
                    marker.root.gameObject.SetActive(false);
                    continue;
                }

                marker.islandId = island.islandId;
                marker.root.gameObject.SetActive(true);
                marker.root.anchoredPosition = WorldToMap(island.mapPosition);

                float diameter = MarkerDiameter(island, revealed, mapPixelsPerMeter, viewMin);
                ApplyMarkerVisual(marker, island, revealed, diameter, false);

                // 실루엣 ↔ 상세 전환. **탐사한 섬만** 실제 해안선으로 바꾼다 - 미탐사 섬은 지금까지처럼
                // 크기를 숨긴 고정 크기 점이어야 한다(모양까지 보여주면 가보지 않고도 규모를 알아낸다).
                float[] shapeMask = detail && revealed ? GetIslandRadialMask(island.islandId) : null;
                bool useShape = shapeMask != null;
                if (marker.shape != null)
                {
                    if (useShape)
                    {
                        marker.shapeRt.sizeDelta = new Vector2(diameter, diameter);
                        marker.shape.color = marker.fill.color;
                        marker.shape.Configure(shapeMask, diameter * 0.5f);
                    }

                    if (marker.shape.gameObject.activeSelf != useShape)
                        marker.shape.gameObject.SetActive(useShape);
                }

                // 둘 다 켜 두면 원과 폴리곤이 겹쳐 보인다.
                if (marker.fill.enabled == useShape)
                    marker.fill.enabled = !useShape;

                // 클릭 판정도 표식 크기를 따라간다. 상세 단계에서 섬이 화면의 1/4을 차지하는데
                // 판정만 24px에 머물면 **눈에 보이는 섬을 눌러도 아무 일이 없다**.
                if (marker.hitButton != null && marker.hitButton.transform is RectTransform hitRt)
                {
                    float hit = Mathf.Max(24f, diameter);
                    hitRt.sizeDelta = new Vector2(hit, hit);
                }

                if (rebuildLabels && marker.label != null)
                {
                    bool isCurrent = islandTravel != null && island.islandId == islandTravel.currentIslandId;
                    // [B52] 미탐사 이름표는 그리지 않는다(예전에는 "?"). 50섬 실측 배치는 환초라 섬이
                    // 최근접 786m·중앙값 1.1km 간격 무리를 이루는데, 27km를 한 화면에 담는 축척에서는
                    // 표식이 몇 px 간격이 되므로 무리마다 "?" 수십 장이 같은 자리에 겹쳐 잉크 얼룩이
                    // 된다. 미탐사 섬의 존재는 검은 원 표식이 이미 알리고 있어 정보 손실이 없다.
                    // [카토그래피] 표식이 있으면 이름표에도 꼬리표를 붙인다(링 색과 이중으로 알린다 -
                    // 색만으로는 야간·색맹 조건에서 세 표식이 갈리지 않는다).
                    marker.label.text = revealed
                        ? $"섬 {island.islandId} · {GetSizeKoreanName(island.size)}{(isCurrent ? " (현재 위치)" : "")}{GetMarkTag(GetIslandMark(island.islandId))}"
                        : "";
                }
            }

            for (int i = islands.Count; i < mapMarkers.Count; i++)
                mapMarkers[i].root.gameObject.SetActive(false);
        }

        /// <summary>
        /// 지도 뷰가 직접 받는 마우스 조작(드래그 = 팬, 휠 = 줌).
        /// **컴포넌트로 받는 이유**: Input.GetAxis("Mouse ScrollWheel")로 읽으면 지도 밖 어디서
        /// 굴려도 줌이 되고, 오른쪽 섬 목록의 세로 스크롤과 같은 입력을 놓고 싸운다.
        /// 델타를 화면 px이 아니라 이 사각형의 지역 좌표로 환산하는 이유는 CanvasScaler다 -
        /// 해상도가 기준(1920×1080)과 다르면 화면 px과 캔버스 px의 배율이 다르다.
        /// </summary>
        private sealed class MapPanZoomInput : MonoBehaviour, IPointerDownHandler, IDragHandler, IScrollHandler
        {
            public System.Action<Vector2> onPan;
            public System.Action<float, Vector2> onZoom;

            private RectTransform rt;

            private RectTransform SelfRect => rt != null ? rt : (rt = transform as RectTransform);

            public void OnPointerDown(PointerEventData eventData)
            {
                // 받아만 둔다. EventSystem은 **누른 대상**에서 위로 올라가며 드래그 처리기를 찾으므로,
                // 이 인터페이스가 없으면 빈 바다를 눌러 끌기 시작할 수 없다.
            }

            public void OnDrag(PointerEventData eventData)
            {
                if (onPan == null || SelfRect == null)
                    return;

                if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                        SelfRect, eventData.position, eventData.pressEventCamera, out Vector2 now))
                    return;

                if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                        SelfRect, eventData.position - eventData.delta, eventData.pressEventCamera, out Vector2 before))
                    return;

                onPan(now - before);
            }

            public void OnScroll(PointerEventData eventData)
            {
                if (onZoom == null || SelfRect == null)
                    return;

                RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    SelfRect, eventData.position, eventData.pressEventCamera, out Vector2 local);
                onZoom(eventData.scrollDelta.y, local);
            }
        }

        // ────────────────────────────────────────────────────────────────────────
        // 섬 목록 / 이동
        // ────────────────────────────────────────────────────────────────────────

        /// <summary>섬 목록 행들을 갱신한다(부족하면 새로 만들고, 남으면 숨긴다).</summary>
        private void RefreshList()
        {
            if (player == null || listContainer == null)
                return;

            var islands = worldMapManager.islands;
            EnsureRowCount(islands.Count);

            int revealedCount = 0;

            for (int i = 0; i < islands.Count; i++)
            {
                var island = islands[i];
                var row = islandRows[i];
                bool revealed = IsRevealed(island);
                if (revealed)
                    revealedCount++;

                row.rowGo.SetActive(true);
                row.infoLabel.text = BuildIslandInfo(island);

                // 상태 구분은 색이 아니라 **밝기**로 한다(지도 표식의 밝기 사다리와 같은 규칙).
                row.infoLabel.color = island.islandId == selectedIslandId
                    ? Color.white
                    : (revealed ? new Color(0.88f, 0.88f, 0.88f, 1f) : new Color(0.5f, 0.5f, 0.5f, 1f));

                // 치명적 버그 방지: island.isDiscovered는 "도착에 성공한 뒤"에만 true가 되므로 이동
                // 버튼의 활성 조건으로 쓰면 순환 잠금(도착해야 발견 → 발견해야 이동)이 된다.
                // 이동 가능 여부는 "지금 있는 섬이 아닌가"로만 판단하고, 실제 조건(고무보트·해류)은
                // IslandTravel.TryTravelTo가 판정한다. UI가 조건을 다시 구현하지 않는다.
                bool isCurrent = islandTravel != null
                    ? island.islandId == islandTravel.currentIslandId
                    : island.isStartingIsland;
                row.travelButton.interactable = !isCurrent;

                // [B52] 콜백은 CreateIslandRow에서 한 번만 등록한다(row.islandId를 이벤트 시점에
                // 읽는다 - 호버 콜백과 같은 방식). 예전처럼 여기서 RemoveAll/Add를 반복하면 50행 ×
                // 0.2초 주기 = 초당 클로저 250개 할당이라 지도를 열어두는 내내 GC를 데운다.
                row.islandId = island.islandId;

                // [카토그래피] 표식은 가본 섬에만 남길 수 있다. 라벨 문자열은 표식이 실제로
                // 바뀐 행에서만 갈아 끼운다(0.2초 주기 × 50행에서 매번 새 문자열을 만들지 않는다).
                if (row.markButton != null)
                    row.markButton.interactable = revealed;
                RefreshMarkButton(row);
            }

            for (int i = islands.Count; i < islandRows.Count; i++)
                islandRows[i].rowGo.SetActive(false);

            if (statusLabel != null)
                statusLabel.text = lastTravelStatus;

            // 탐사율은 실제로 달라졌을 때만 문자열을 다시 만든다(0.2초 주기로 도는 자리다).
            if (mapSummaryLabel != null)
            {
                string summary = $"탐사 {revealedCount} / {islands.Count}";
                if (lastDisplayedMapSummary != summary)
                {
                    lastDisplayedMapSummary = summary;
                    mapSummaryLabel.text = summary;
                }
            }

            RefreshChecklist();
        }

        /// <summary>
        /// 표식 버튼의 글자/색을 지금 표식에 맞춘다. **표식이 실제로 바뀐 행에서만** 문자열을
        /// 갈아 끼우므로(shownMark 캐시) 목록 갱신 주기(0.2초)에 새 문자열이 생기지 않는다.
        /// </summary>
        private void RefreshMarkButton(IslandRow row)
        {
            if (row == null || row.markLabel == null)
                return;

            IslandMark mark = GetIslandMark(row.islandId);
            if (row.shownMark == (int)mark)
                return;

            row.shownMark = (int)mark;
            row.markLabel.text = GetMarkButtonLabel(mark);
            row.markLabel.color = mark == IslandMark.None
                ? new Color(1f, 1f, 1f, 0.75f)
                : GetIslandMarkColor(mark);
        }

        private void EnsureRowCount(int count)
        {
            while (islandRows.Count < count)
                islandRows.Add(CreateIslandRow());
        }

        /// <summary>섬 정보 텍스트 + "이동" 버튼으로 구성된 한 줄을 생성한다.</summary>
        private IslandRow CreateIslandRow()
        {
            var rowGo = new GameObject("IslandRow", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            rowGo.transform.SetParent(listContainer, false);
            rowGo.GetComponent<LayoutElement>().minHeight = 26f;

            var hlg = rowGo.GetComponent<HorizontalLayoutGroup>();
            hlg.childForceExpandWidth = false;
            hlg.childForceExpandHeight = true;
            hlg.spacing = 8f;
            hlg.childAlignment = TextAnchor.MiddleLeft;

            var infoLabel = UIBuilder.CreateText(rowGo.transform, "Info", "", UITheme.FontBody, Color.white, TextAnchor.MiddleLeft);
            infoLabel.horizontalOverflow = HorizontalWrapMode.Overflow;
            infoLabel.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;

            // [카토그래피] 표식 버튼. 이동 버튼 **왼쪽**에 둔다 - 목록에서 가장 자주 누르는 것은
            // 여전히 이동이라 그쪽을 오른쪽 끝(누르던 자리)에 그대로 남긴다.
            var markButton = UIBuilder.CreateButton(rowGo.transform, "MarkButton", "표식", null);
            markButton.gameObject.AddComponent<LayoutElement>().preferredWidth = 52f;
            Text markLabel = markButton.GetComponentInChildren<Text>();
            if (markLabel != null)
                markLabel.fontSize = UITheme.FontBody;

            var travelButton = UIBuilder.CreateButton(rowGo.transform, "TravelButton", "이동", null);
            travelButton.gameObject.AddComponent<LayoutElement>().preferredWidth = 70f;

            var row = new IslandRow
            {
                rowGo = rowGo,
                infoLabel = infoLabel,
                travelButton = travelButton,
                markButton = markButton,
                markLabel = markLabel,
            };

            // 표식 콜백도 행을 만들 때 한 번만 등록한다(이동 버튼과 같은 규칙 - 매 갱신마다
            // RemoveAll/Add를 반복하면 50행 × 0.2초 주기로 클로저가 초당 수백 개 생긴다).
            // 미발견 섬에는 표식을 남길 수 없다(버튼도 비활성) - 가보지 않은 섬에 "고갈됨"을
            // 붙이는 것은 뜻이 없고, 표식 색이 미탐사 링을 덮어 정보 누출이 된다.
            row.markButton.onClick.AddListener(() =>
            {
                CycleIslandMark(row.islandId);
                row.shownMark = -1;  // 라벨을 즉시 다시 쓰게 만든다
                mapRefreshTimer = 0f; // 지도 표식 색도 다음 프레임에 바로 반영한다
                RefreshMarkButton(row);
            });

            // [B52] 이동 콜백도 행을 만들 때 한 번만 등록한다. row.islandId는 RefreshList가 매 갱신
            // 최신으로 채워 두므로, 클릭 시점에 읽으면 항상 그 행이 지금 표시 중인 섬이다.
            row.travelButton.onClick.AddListener(() => TryTravel(row.islandId));

            // "목적지 선택" = 이동 버튼에 포인터를 올리는 것. 이 프로젝트는 커서를 잠그지 않으므로
            // 마우스 오버가 그대로 동작한다. 콜백은 행을 만들 때 한 번만 등록하고 row.islandId를
            // 이벤트 시점에 읽는다(매 갱신마다 Remove/Add를 반복하지 않기 위함).
            var trigger = travelButton.gameObject.AddComponent<EventTrigger>();
            var enterEntry = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
            enterEntry.callback.AddListener(_ =>
            {
                selectedIslandId = row.islandId;
                RefreshChecklist();
            });
            trigger.triggers.Add(enterEntry);

            return row;
        }

        /// <summary>
        /// 섬 하나의 표시용 정보 문자열을 만든다.
        /// 미발견 섬의 규모는 "미확인"으로 가린다 - 방문하지 않고도 어느 섬이 대형/특대(도면·희귀 재료가
        /// 나오는 섬)인지 목록만 보고 알아내면 탐험의 의미가 사라진다. 지도 표식이 미탐사 섬을 고정
        /// 크기로 그리는 것과 같은 이유이자 같은 규칙이다.
        /// </summary>
        private string BuildIslandInfo(IslandInstance island)
        {
            float distance = Vector3.Distance(player.position, island.mapPosition);
            bool revealed = IsRevealed(island);
            string sizeText = revealed ? GetSizeKoreanName(island.size) : "미확인";
            string statusText = island.isStartingIsland ? "시작 섬" : (island.isDiscovered ? "발견함" : "미발견");
            // [카토그래피] 표식은 가본 섬에만 붙는다 - 미발견 섬에 꼬리표가 뜨면 그 자체가 정보 누출이다.
            string markTag = revealed ? GetMarkTag(GetIslandMark(island.islandId)) : "";
            return $"섬 {island.islandId}  ·  {sizeText}  ·  {distance:F0}m  ·  {statusText}{markTag}";
        }

        private string GetSizeKoreanName(IslandSize size)
        {
            switch (size)
            {
                case IslandSize.Small: return "소형";
                case IslandSize.Medium: return "중형";
                case IslandSize.Large: return "대형";
                case IslandSize.ExtraLarge: return "특대";
                default: return size.ToString();
            }
        }

        /// <summary>
        /// 지정한 섬으로 이동을 시도하고, 결과 메시지를 상태 라벨에 남긴다.
        /// IslandTravel.TryTravelTo(고무보트 보유/해류 제약 확인)를 호출하는 유일한 진입점이다.
        /// </summary>
        private void TryTravel(int islandId)
        {
            selectedIslandId = islandId; // 클릭도 "목적지 선택"으로 친다

            if (islandTravel == null || inventory == null)
                return;

            bool success = islandTravel.TryTravelTo(islandId, inventory);
            lastTravelStatus = success
                ? $"섬 {islandId}(으)로 이동했습니다."
                : DescribeTravelFailure(islandId);

            // 이동에 성공하면 새 섬이 탐사 구역으로 밝아진다. 다음 프레임을 기다리지 않고 바로 반영한다.
            mapRefreshTimer = 0f;
        }

        /// <summary>
        /// 이동이 왜 막혔는지 이름을 붙여 준다. "해류가 강하다"만으로는 무엇을 더 만들어야 하는지 알 수
        /// 없어, 뗏목을 4칸에서 멈춰 세운 채 특대 섬 앞에서 헤매게 된다. 판정 순서는 TryTravelTo와 같다
        /// (고무보트 → 규모별 해류). 요구 등급은 IslandTravel이 들고 있으므로 여기서 다시 정하지 않는다.
        /// </summary>
        private string DescribeTravelFailure(int islandId)
        {
            var boat = islandTravel.rubberBoatItem;
            if (boat != null && inventory.GetItemCount(boat) <= 0)
                return "이동 실패: 고무보트가 없습니다.";

            var island = worldMapManager != null ? worldMapManager.GetIsland(islandId) : null;
            if (island == null)
                return "이동 실패: 그런 섬이 없습니다.";

            if (island.size != IslandSize.ExtraLarge)
                return "이동 실패: 이 섬까지는 아직 갈 수 없습니다.";

            var raft = RaftStructure.Active;
            string state = raft == null ? "해안에 뗏목이 아직 없습니다" : raft.DescribeState();
            return "이동 실패: 특대 섬 해류는 모터를 단 대양 규격 뗏목"
                + $"(바닥판 {RaftStructure.OceanReadyTileCount}칸 + 돛·키 또는 모터, 거기에 모터까지)이라야 뚫습니다."
                + $" 현재 뗏목: {state}.";
        }

        // ────────────────────────────────────────────────────────────────────────
        // 출발 전 준비 체크리스트
        // ────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// 선택된 목적지가 대형/특대 섬일 때만 준비 체크리스트 한 줄을 채운다.
        /// 규칙 둘을 반드시 지킨다.
        /// 1) **이동을 막지 않는다.** 어떤 버튼의 interactable도 건드리지 않는다(순환 잠금 재발 방지).
        /// 2) **규모를 몰래 알려주지 않는다.** 규모가 공개된 섬에 대해서만 띄운다 - 미발견 섬에
        ///    체크리스트가 뜨면 그 자체가 "저건 대형이다"라는 정보 누출이다.
        /// </summary>
        private void RefreshChecklist()
        {
            if (checklistLabel == null)
                return;

            string text = BuildChecklistText();
            if (text == lastDisplayedChecklist)
                return;

            checklistLabel.text = text;
            lastDisplayedChecklist = text;
        }

        private string BuildChecklistText()
        {
            if (selectedIslandId < 0 || worldMapManager == null)
                return "";

            var island = worldMapManager.GetIsland(selectedIslandId);
            if (island == null)
                return "";

            if (!IsRevealed(island))
                return "";

            if (island.size != IslandSize.Large && island.size != IslandSize.ExtraLarge)
                return "";

            bool hasAxe = HasItemNamed("손도끼");
            bool hasWater = HasItem(item => item.thirstRestoreAmount > 0f || item.isCoconutWaterSource);
            bool hasFood = HasItem(item => item.hungerRestoreAmount > 0f);
            bool hasWeapon = HasItem(item => item.isWeapon);

            string note = GetToolNote(island.size, hasAxe);

            return $"{GetSizeKoreanName(island.size)} 섬 준비: "
                + $"{Mark("손도끼", hasAxe)}  {Mark("물", hasWater)}  {Mark("음식", hasFood)}  {Mark("무기", hasWeapon)}"
                + (string.IsNullOrEmpty(note) ? "" : $"   ({note})");
        }

        /// <summary>
        /// 규모별 도구 안내. 특대 섬의 핵심 자원인 엔진부품은 requiresTool이 꺼져 있어 손도끼가 필요
        /// 없다(실측: minimumIslandSize 3 / requiresTool 0). 손도끼가 없다는 이유로 특대 섬 여행을
        /// 미루게 만드는 문구는 힌트가 아니라 함정이라, "없어도 이 섬의 목적은 달성된다"를 먼저 읽힌다.
        /// </summary>
        private string GetToolNote(IslandSize size, bool hasAxe)
        {
            if (hasAxe)
                return "";

            if (size == IslandSize.ExtraLarge)
                return "엔진부품은 맨손으로 캔다, 손도끼는 금속조각용";

            return "금속조각에는 손도끼, 부력통은 맨손";
        }

        /// <summary>체크리스트 항목 하나를 "이름 O"/"이름 X" 형태의 색 있는 조각으로 만든다.</summary>
        private string Mark(string label, bool ready)
        {
            Color color = ready ? ReadyColor : MissingColor;
            return $"<color=#{ColorUtility.ToHtmlStringRGBA(color)}>{label} {(ready ? "O" : "X")}</color>";
        }

        /// <summary>인벤토리에 조건을 만족하는 아이템이 하나라도 있는지 확인한다(표시 전용 조회).</summary>
        private bool HasItem(System.Func<ItemData, bool> predicate)
        {
            if (inventory == null || inventory.items == null)
                return false;

            for (int i = 0; i < inventory.items.Count; i++)
            {
                var entry = inventory.items[i];
                if (entry != null && entry.data != null && predicate(entry.data))
                    return true;
            }

            return false;
        }

        /// <summary>인벤토리에 이름에 특정 단어가 들어간 아이템이 있는지 확인한다(표시 전용 조회).</summary>
        private bool HasItemNamed(string keyword)
        {
            return HasItem(item => !string.IsNullOrEmpty(item.itemName) && item.itemName.Contains(keyword));
        }
    }
}
