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
        // 전체 지도 창 치수. 제목줄 높이·좌우 여백은 공용 골격이 정하므로 여기 값은 전부
        // **본문 안쪽** 좌표다 - 본문의 (0,0)이 곧 지도 그림의 왼쪽 위다.
        private const float MapViewSize = 470f;
        private const float ListColumnWidth = 328f;
        private const float ListColumnGap = 14f;
        private const float ListHeaderHeight = 20f;
        private const float ChecklistHeight = 34f;
        private const float StatusHeight = 22f;

        private const float MapBodyWidth = MapViewSize + ListColumnGap + ListColumnWidth;
        private const float MapBodyHeight = MapViewSize + 8f + ChecklistHeight + 4f + StatusHeight;

        /// <summary>기본 자리 계산에 쓰는 창 전체 높이(골격이 더하는 위·아래 여백 포함).</summary>
        private const float MapWindowHeight = MapBodyHeight + UITheme.ChromeTop + UITheme.ChromeBottom;

        /// <summary>
        /// 전체 지도에서 "탐사해서 밝아지는" 원의 반경(미터).
        /// 근거: 이웃 섬까지 중앙값이 1,143m인 배치에서 두 탐사 섬의 밝은 구역이 서로 이어져 항로처럼
        /// 읽히려면 그보다 넉넉히 커야 한다. 4,000m면 이웃 무리를 통째로 덮고, 27km짜리 바다에서도
        /// 밝은 구역이 점이 아니라 띠로 보인다.
        /// </summary>
        private const float ExploredRadiusMeters = 4000f;

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
        private UIDragHandle mapDragHandle;
        private RectTransform mapMarkerLayer;
        private RectTransform mapFogLayer;
        private RectTransform playerPinRt;
        private RectTransform scaleBarRt;
        private Text scaleLabel;
        private readonly List<IslandMarker> mapMarkers = new List<IslandMarker>();
        private readonly List<Image> exploredHalos = new List<Image>();
        private float mapPixelsPerMeter = 0.02f;

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
                MapBodyWidth, MapBodyHeight, $"세계 지도 ({toggleKey})", canvasRect, () => SetMapOpen(false));

            mapWindowRt = frame.window;
            mapBodyRt = frame.body;
            mapWindowRoot = mapWindowRt.gameObject;

            // 탐사율 한 줄은 제목 **옆**이다. 이동 결과 문구와 체크리스트는 본문에 그대로 둔다.
            mapSummaryLabel = frame.status;

            mapDragHandle = frame.drag;
            if (mapDragHandle != null)
            {
                mapDragHandle.onMoved = position =>
                {
                    savedMapPosition = position;
                    hasSavedMapPosition = true;
                };
            }

            BuildMapView();
            BuildIslandListColumn();
            BuildMapFooter();
        }

        /// <summary>지도 그림 영역(왼쪽 열): 검은 바다 → 탐사 원반 → 섬 표식 → 플레이어 → 축척자.</summary>
        private void BuildMapView()
        {
            var view = UIBuilder.CreatePanel(
                mapBodyRt, "MapView",
                anchorMin: new Vector2(0f, 1f), anchorMax: new Vector2(0f, 1f),
                offsetMin: Vector2.zero, offsetMax: Vector2.zero,
                color: UnexploredSea);

            view.pivot = new Vector2(0f, 1f);
            view.anchoredPosition = Vector2.zero;
            view.sizeDelta = new Vector2(MapViewSize, MapViewSize);
            view.gameObject.AddComponent<RectMask2D>();

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
            float columnX = MapViewSize + ListColumnGap;

            var header = UIBuilder.CreateText(mapBodyRt, "ListHeader", "섬 목록 · [표식]으로 고갈/자원/위험 표시 (지도에선 Shift+클릭)", UITheme.FontBody, NeutralGray, TextAnchor.MiddleLeft);
            header.raycastTarget = false;
            header.horizontalOverflow = HorizontalWrapMode.Overflow;
            header.rectTransform.anchorMin = new Vector2(0f, 1f);
            header.rectTransform.anchorMax = new Vector2(0f, 1f);
            header.rectTransform.pivot = new Vector2(0f, 1f);
            header.rectTransform.anchoredPosition = new Vector2(columnX, 0f);
            header.rectTransform.sizeDelta = new Vector2(ListColumnWidth, ListHeaderHeight);

            // [B52] 50섬 대응: 행이 50개면 목록 전체 높이가 약 1,500px(행 26 + 간격 4)로 열 높이
            // (약 446px)의 3배가 넘는다. 예전처럼 컨테이너를 창에 직접 붙이면 마스크가 없어 행들이
            // 창 아래 체크리스트/상태 줄을 덮고 화면 밖까지 그대로 그려진다. InventorySlotView와 같은
            // 조립(ScrollRect + RectMask2D 뷰포트 + ContentSizeFitter 콘텐츠)으로 세로 스크롤을 붙인다.
            var scrollGo = new GameObject("IslandListScroll", typeof(RectTransform), typeof(ScrollRect));
            scrollGo.transform.SetParent(mapBodyRt, false);
            var scrollRt = scrollGo.GetComponent<RectTransform>();
            scrollRt.anchorMin = new Vector2(0f, 1f);
            scrollRt.anchorMax = new Vector2(0f, 1f);
            scrollRt.pivot = new Vector2(0f, 1f);
            scrollRt.anchoredPosition = new Vector2(columnX, -(ListHeaderHeight + 4f));
            scrollRt.sizeDelta = new Vector2(ListColumnWidth, MapViewSize - ListHeaderHeight - 4f);

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
        /// 전체 지도 창을 열거나 닫는다. 옮겨둔 자리를 복원하고, 해상도가 바뀌었을 경우를 대비해
        /// 화면 안으로 다시 맞춘다(인벤토리 SetOpen과 같은 순서).
        /// </summary>
        private void SetMapOpen(bool open)
        {
            if (mapWindowRoot == null)
                return;

            mapWindowRoot.SetActive(open);

            if (!open)
                return;

            if (hasSavedMapPosition)
                mapWindowRt.anchoredPosition = savedMapPosition;
            else
                mapWindowRt.anchoredPosition = DefaultMapWindowPosition();

            if (mapDragHandle != null)
                mapDragHandle.ClampNow();

            RecalculateMapScale();
            mapRefreshTimer = 0f;
            RefreshMapWindow();
        }

        /// <summary>처음 열 때의 기본 자리: 화면 한가운데(창이 커서 어느 쪽에 붙여도 HUD를 가린다).</summary>
        private Vector2 DefaultMapWindowPosition()
        {
            return new Vector2(0f, MapWindowHeight * 0.5f);
        }

        /// <summary>
        /// 지도 축척을 정한다. **oceanSize(40000m)가 아니라 실제 섬이 퍼져 있는 범위에 맞춘다.**
        /// 섬은 baseDistanceStep(1200) × 섬 번호 + jitter 규칙으로 배치되므로 가장 먼 섬도 원점에서
        /// 약 10km 안쪽이다. 40km 기준으로 그리면 지도의 94%가 빈 바다가 되고 섬 9개가 가운데
        /// 한 줌으로 뭉친다(픽셀을 4배 낭비한다).
        /// </summary>
        private void RecalculateMapScale()
        {
            float extent = 0f;

            if (worldMapManager != null && worldMapManager.islands != null)
            {
                for (int i = 0; i < worldMapManager.islands.Count; i++)
                {
                    var island = worldMapManager.islands[i];
                    if (island == null)
                        continue;

                    float distance = new Vector2(island.mapPosition.x, island.mapPosition.z).magnitude;
                    extent = Mathf.Max(extent, distance + IslandSizeMetrics.GetTerrainRadius(island.size));
                }
            }

            if (player != null)
                extent = Mathf.Max(extent, new Vector2(player.position.x, player.position.z).magnitude);

            // 탐사 원반이 지도 밖으로 잘리지 않게 여유를 준다.
            extent = Mathf.Max(extent + ExploredRadiusMeters * 0.5f, 1000f);

            mapPixelsPerMeter = (MapViewSize * 0.5f - 12f) / extent;

            UpdateScaleBar();
        }

        /// <summary>축척자 길이를 60~170px에 들어오는 "깔끔한" 거리로 맞춘다.</summary>
        private void UpdateScaleBar()
        {
            if (scaleBarRt == null || scaleLabel == null)
                return;

            float[] candidates = { 100f, 200f, 500f, 1000f, 2000f, 5000f, 10000f };
            float chosen = candidates[candidates.Length - 1];

            for (int i = 0; i < candidates.Length; i++)
            {
                float pixels = candidates[i] * mapPixelsPerMeter;
                if (pixels >= 60f && pixels <= 170f)
                {
                    chosen = candidates[i];
                    break;
                }
            }

            scaleBarRt.sizeDelta = new Vector2(Mathf.Max(20f, chosen * mapPixelsPerMeter), 2f);
            scaleLabel.text = $"{chosen:F0}m";
        }

        /// <summary>월드 좌표(X,Z)를 지도 위 픽셀 좌표로 바꾼다. 지도 중심은 월드 원점이다.</summary>
        private Vector2 WorldToMap(Vector3 worldPosition)
        {
            return new Vector2(worldPosition.x, worldPosition.z) * mapPixelsPerMeter;
        }

        /// <summary>
        /// 전체 지도를 갱신한다. 플레이어 표시는 매 프레임, 나머지(문자열을 만드는 목록·이름표)는
        /// 0.2초 간격으로 갱신해 매 프레임 문자열을 새로 만들지 않는다.
        /// </summary>
        private void RefreshMapWindow()
        {
            if (worldMapManager == null || worldMapManager.islands == null)
                return;

            if (player != null && playerPinRt != null)
            {
                playerPinRt.anchoredPosition = WorldToMap(player.position);
                playerPinRt.localEulerAngles = new Vector3(0f, 0f, -player.eulerAngles.y);
            }

            mapRefreshTimer -= Time.unscaledDeltaTime;
            if (mapRefreshTimer > 0f)
                return;

            mapRefreshTimer = MapRefreshInterval;

            RefreshExploredArea();
            RefreshMapMarkers();
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

            int used = 0;
            for (int i = 0; i < islands.Count; i++)
            {
                if (!IsRevealed(islands[i]))
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

        /// <summary>전체 지도의 섬 표식을 갱신한다. 크기는 실제 지형 반지름 비례, 이름표는 탐사한 섬만.</summary>
        private void RefreshMapMarkers()
        {
            var islands = worldMapManager.islands;
            EnsureMarkerCount(mapMarkers, mapMarkerLayer, islands.Count, true);

            for (int i = 0; i < islands.Count; i++)
            {
                var island = islands[i];
                var marker = mapMarkers[i];
                bool revealed = IsRevealed(island);

                marker.islandId = island.islandId;
                marker.root.gameObject.SetActive(true);
                marker.root.anchoredPosition = WorldToMap(island.mapPosition);

                float diameter = MarkerDiameter(island, revealed, mapPixelsPerMeter, MapViewSize);
                ApplyMarkerVisual(marker, island, revealed, diameter, false);

                if (marker.label != null)
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
