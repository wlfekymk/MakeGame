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
    /// 미니맵(우상단 상시 표시)과 전체 지도 창([M])을 담당한다.
    ///
    /// [B20 개편] 예전에는 (1) 크기 구분이 전혀 없는 흰 점 레이더와 (2) 그와 아무 관계가 없는
    /// 텍스트 "섬 목록" 패널이 따로 놀았다. 실기에서 흰 원(점 + 테두리 링)이 겹쳐 보여 무엇이
    /// 무엇인지 읽히지 않았고, 소형 섬과 특대 섬이 똑같은 6px 점이었다. 이번 개편의 축은 셋이다.
    ///   A. 미니맵: 섬을 지형 반지름(IslandSizeMetrics.GetTerrainRadius)에 **비례한 원**으로 그리고,
    ///      5단계 줌([+]/[-])으로 축척을 바꾼다. 플레이어는 항상 중심, 지도는 **북쪽 고정**이다.
    ///   B. 전체 지도: [M]으로 여는 창 하나에 지도와 섬 목록을 **함께** 넣는다(목록만 따로 뜨던
    ///      패널은 이 창의 오른쪽 열로 흡수됐다). 인벤토리와 같은 규격 - 제목 표시줄 드래그로 이동,
    ///      우상단 X로 닫기, 옮긴 자리는 static으로 기억, 패널 알파 0.93.
    ///   C. 미탐사 표현: 아직 가보지 않은 섬은 **검은 원 + 흐린 윤곽**이고 크기는 고정값이다.
    ///      전체 지도의 바다는 기본이 검정이고, 탐사한 섬 주변만 원형으로 밝다.
    ///
    /// 이동(IslandTravel.TryTravelTo)을 호출하는 유일한 진입점이라는 성격은 그대로다. 이동 조건
    /// (고무보트 보유·해류 제약)은 IslandTravel이 판정하며 이 UI는 그것을 다시 구현하지 않는다.
    /// </summary>
    public class MinimapUI : MonoBehaviour
    {
        [Tooltip("섬 목록/배치 정보를 가진 월드맵 매니저")]
        public WorldMapManager worldMapManager;

        [Tooltip("레이더 중심 기준이 되는 플레이어 위치")]
        public Transform player;

        [Tooltip("섬 목록에서 '이동' 버튼을 눌렀을 때 실제 이동을 처리할 시스템")]
        public IslandTravel islandTravel;

        [Tooltip("이동에 필요한 고무보트 등을 확인할 인벤토리")]
        public PlayerInventory inventory;

        [Tooltip("전체 지도 창을 여닫는 키")]
        public KeyCode toggleKey = KeyCode.M;

        [Header("미니맵 설정")]
        [Tooltip("미니맵 줌 **최대 단계(5단계)**가 보여줄 월드 반경(미터). 아래 단계는 여기서 절반씩 " +
            "내려간다(5단계=이 값, 4단계=1/2, 3단계=1/4, 2단계=1/8, 1단계=1/16). " +
            "0 이하로 두면 WorldMapManager의 섬 배치 설정(baseDistanceStep 등)에서 자동으로 유도한다. " +
            "씬 값 4000 기준 사다리는 250 / 500 / 1000 / 2000 / 4000m다.")]
        // [B20] 의미를 "레이더가 보여주는 유일한 반경"에서 "줌 사다리의 최대 단계"로 넓혔다. 필드 이름과
        // 타입은 그대로 두었으므로 씬 직렬화 값(4000)은 그대로 살아 있고, 그 값이 사다리 꼭대기가 된다.
        // 즉 씬을 건드리지 않아도 최대 배율의 체감은 개편 전과 동일하다.
        public float radarWorldRadius = 0f;

        [Tooltip("미니맵 패널의 한 변 크기(픽셀)")]
        public float radarPanelSize = 160f;

        [Tooltip("미니맵을 한 단계 확대(가까이)하는 키. 키패드 +와 Shift+= 도 함께 받는다.")]
        public KeyCode zoomInKey = KeyCode.Equals;

        [Tooltip("미니맵을 한 단계 축소(멀리)하는 키. 키패드 - 도 함께 받는다.")]
        public KeyCode zoomOutKey = KeyCode.Minus;

        // worldMapManager 참조가 없어(Inspector 미할당 등) 유도 계산 자체가 불가능할 때만 쓰는 최후 안전값.
        private const float FallbackRadarWorldRadius = 4000f;

        // ────────────────────────────────────────────────────────────────────────
        // 상수
        // ────────────────────────────────────────────────────────────────────────

        /// <summary>줌 단계 수. 사다리는 "최대 반경 / 2^(4-단계)"로 만든다.</summary>
        private const int ZoomStepCount = 5;

        /// <summary>
        /// 시작 줌 단계(0부터). 2단계 = 최대 반경의 1/4 = 씬 값 기준 1000m.
        /// 근거: 섬 간격(baseDistanceStep)이 1200m라, 1000m는 "이웃 섬이 아직 안 보이는" 축척이다.
        /// 내 섬과 주변 바다는 넉넉히 담기고, 다음 섬을 찾으려면 한 단계 넓혀야 한다는 신호가 된다.
        /// </summary>
        private const int DefaultZoomIndex = 2;

        /// <summary>미니맵 가장자리 여백(px). 표식이 테두리에 물리지 않게 한다.</summary>
        private const float MinimapEdgePadding = 7f;

        /// <summary>미니맵 하단 정보 띠 높이(px). 줌 단계와 키 안내가 여기 들어간다.</summary>
        private const float MinimapCaptionHeight = 32f;

        /// <summary>미탐사 섬 표식의 고정 지름(px). 규모를 노출하지 않기 위해 항상 이 값이다.</summary>
        private const float UnknownMarkerDiameter = 7f;

        /// <summary>시야 밖 섬을 가장자리에 붙일 때 쓰는 지름(px).</summary>
        private const float EdgeMarkerDiameter = 8f;

        // 전체 지도 창 치수. 인벤토리 창과 같은 조립 규칙(한 점 앵커 + 고정 크기 + 피벗 (0.5,1))을 쓴다.
        private const float TitleBarHeight = 34f;
        private const float WindowPadding = 14f;
        private const float MapViewSize = 470f;
        private const float ListColumnWidth = 328f;
        private const float ListHeaderHeight = 20f;
        private const float ChecklistHeight = 34f;
        private const float StatusHeight = 22f;
        private const float MapViewTop = TitleBarHeight + 6f;
        private const float MapWindowWidth = WindowPadding + MapViewSize + 14f + ListColumnWidth + WindowPadding;
        private const float MapWindowHeight = MapViewTop + MapViewSize + 8f + ChecklistHeight + 4f + StatusHeight + 8f;

        /// <summary>
        /// 전체 지도에서 "탐사해서 밝아지는" 원의 반경(미터).
        /// 근거: 섬 간격 1200m보다 크게 잡아야 이웃한 두 탐사 섬의 밝은 구역이 서로 이어져 항로처럼
        /// 읽힌다. 1500m면 1200m 간격을 덮고도 300m 여유가 남는다.
        /// </summary>
        private const float ExploredRadiusMeters = 1500f;

        /// <summary>목록·라벨 문자열을 다시 만드는 간격(초). 매 프레임 문자열을 새로 만들지 않기 위함.</summary>
        private const float MapRefreshInterval = 0.2f;

        // ────────────────────────────────────────────────────────────────────────
        // 색 (ArtDirection 팔레트 안에서만. 상태 구분은 색상이 아니라 **밝기 단계**로 한다)
        // ────────────────────────────────────────────────────────────────────────

        /// <summary>Island Sand #C2B280 - 지도 위의 "육지"는 전부 이 한 가지 색이다.</summary>
        private static readonly Color IslandSand = new Color(0.761f, 0.698f, 0.502f, 1f);

        /// <summary>Deep Ocean #1A598C - 탐사해서 밝아진 바다.</summary>
        private static readonly Color DeepOcean = new Color(0.102f, 0.349f, 0.549f, 1f);

        private static readonly Color DangerRed = new Color(0.8f, 0.2f, 0.2f, 1f);
        private static readonly Color MedicGreen = new Color(0.31f, 0.659f, 0.478f, 1f);
        private static readonly Color NeutralGray = new Color(0.8f, 0.8f, 0.8f, 1f);

        // 육지 밝기 사다리(같은 색, 밝기만 다르다):
        //   현재 있는 섬 1.00 → 탐사한 섬 0.72 → 미탐사 섬 0.06(사실상 검정)
        // 색맹·야간 대응을 위해 상태를 색상으로 나누지 않는다. 채도까지 같이 죽으면 어두운 배경 앞에서
        // 검게 뭉치므로(ArtDirection 1.1b의 Shade 주의), 미탐사는 아예 검정으로 확정하고 윤곽선으로만
        // 존재를 알린다.
        private static readonly Color LandCurrent = IslandSand;
        private static readonly Color LandKnown = new Color(0.548f, 0.503f, 0.361f, 1f);
        private static readonly Color LandUnknown = new Color(0.046f, 0.042f, 0.030f, 1f);

        private static readonly Color RingCurrent = new Color(1f, 1f, 1f, 0.95f);
        private static readonly Color RingKnown = new Color(0.761f, 0.698f, 0.502f, 0.55f);
        private static readonly Color RingUnknown = new Color(0.761f, 0.698f, 0.502f, 0.26f);

        /// <summary>전체 지도의 기본 바닥 = 아직 아무것도 모르는 바다.</summary>
        private static readonly Color UnexploredSea = new Color(0.018f, 0.024f, 0.030f, 1f);

        /// <summary>미니맵 바닥. 상시 표시 HUD라 알파 0.55(ArtDirection 4.3).</summary>
        private static readonly Color MinimapBackground = new Color(0.018f, 0.030f, 0.040f, 0.55f);

        /// <summary>전체 지도 창 바탕. 인벤토리와 같은 알파 0.93(0.75는 뒤가 비쳐 못 읽는다).</summary>
        private static readonly Color WindowBackground = new Color(0.04f, 0.05f, 0.06f, 0.93f);

        // 체크리스트 O/X 색. ✓/✗ 글리프는 LegacyRuntime.ttf에서 보장되지 않아 ASCII O/X를 쓴다.
        private static readonly Color ReadyColor = MedicGreen;
        private static readonly Color MissingColor = DangerRed;

        // ────────────────────────────────────────────────────────────────────────
        // 내부 자료구조
        // ────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// 섬 하나를 그리는 표식. 채움 원(육지) + 윤곽 링 + (전체 지도에서만) 이름표와 클릭 판정.
        /// **풀링한다.** 갱신마다 새로 만들지 않는다.
        /// </summary>
        private class IslandMarker
        {
            public RectTransform root;
            public Image fill;
            public RectTransform fillRt;
            public Image ring;
            public RectTransform ringRt;
            public Text label;
            public Button hitButton;
            public int islandId = -1;
        }

        /// <summary>섬 목록의 한 줄(정보 텍스트 + 이동 버튼).</summary>
        private class IslandRow
        {
            public GameObject rowGo;
            public Text infoLabel;
            public Button travelButton;
            public int islandId = -1;
        }

        // 스프라이트는 한 번만 읽어 캐시한다(매 표식 생성마다 Resources.Load를 부르지 않는다).
        private static Sprite dotSprite;
        private static Sprite ringSprite;
        private static Sprite arrowSprite;
        private static bool spritesLoaded;

        // ── 미니맵
        private RectTransform radarContent;
        private RectTransform playerArrowRt;
        private Text zoomLabel;
        private readonly List<IslandMarker> radarMarkers = new List<IslandMarker>();
        private readonly float[] zoomRadii = new float[ZoomStepCount];
        private int zoomIndex = DefaultZoomIndex;
        private int lastDisplayedZoomIndex = -1;

        // ── 전체 지도 창
        private RectTransform canvasRect;
        private GameObject mapWindowRoot;
        private RectTransform mapWindowRt;
        private UIDragHandle mapDragHandle;
        private RectTransform mapMarkerLayer;
        private RectTransform mapFogLayer;
        private RectTransform playerPinRt;
        private RectTransform scaleBarRt;
        private Text scaleLabel;
        private readonly List<IslandMarker> mapMarkers = new List<IslandMarker>();
        private readonly List<Image> exploredHalos = new List<Image>();
        private float mapPixelsPerMeter = 0.02f;
        private float mapRefreshTimer = 0f;

        /// <summary>창을 옮긴 자리를 세션 동안 기억한다. 인벤토리와 같은 방식(static).</summary>
        private static bool hasSavedMapPosition;
        private static Vector2 savedMapPosition;

        // ── 섬 목록(전체 지도 창 오른쪽 열로 흡수됨)
        private RectTransform listContainer;
        private Text statusLabel;
        private Text checklistLabel;
        private readonly List<IslandRow> islandRows = new List<IslandRow>();

        private string lastTravelStatus = "";
        private int selectedIslandId = -1;
        private string lastDisplayedChecklist = null;

        private bool IsMapOpen => mapWindowRoot != null && mapWindowRoot.activeSelf;

        // ────────────────────────────────────────────────────────────────────────
        // 수명 주기
        // ────────────────────────────────────────────────────────────────────────

        private void Start()
        {
            EnsureSprites();
            BuildZoomLadder();
            BuildMinimap();
            BuildMapWindow();
            SetMapOpen(false);
        }

        private void Update()
        {
            if (Input.GetKeyDown(toggleKey))
                SetMapOpen(!IsMapOpen);

            if (IsZoomInPressed())
                SetZoom(zoomIndex - 1);
            else if (IsZoomOutPressed())
                SetZoom(zoomIndex + 1);

            RefreshMinimap();

            if (IsMapOpen)
                RefreshMapWindow();
        }

        /// <summary>
        /// 확대(가까이) 입력. 대부분의 자판에서 '+'는 Shift+'='라 KeyCode.Equals가 실제로 눌리는 키이고,
        /// KeyCode.Plus/KeypadPlus는 자판·입력 설정에 따라 따로 들어온다. 셋 다 받는다.
        /// </summary>
        private bool IsZoomInPressed()
        {
            return Input.GetKeyDown(zoomInKey)
                || Input.GetKeyDown(KeyCode.Plus)
                || Input.GetKeyDown(KeyCode.KeypadPlus);
        }

        /// <summary>축소(멀리) 입력. 본 키와 키패드 '-'를 함께 받는다.</summary>
        private bool IsZoomOutPressed()
        {
            return Input.GetKeyDown(zoomOutKey)
                || Input.GetKeyDown(KeyCode.KeypadMinus);
        }

        /// <summary>한 번만 읽어 캐시하는 UI 스프라이트. 없으면 null이고, 그때는 사각형으로 그려진다.</summary>
        private static void EnsureSprites()
        {
            if (spritesLoaded)
                return;

            spritesLoaded = true;
            dotSprite = Resources.Load<Sprite>("Sprites/radar_dot");
            ringSprite = Resources.Load<Sprite>("Sprites/radar_ring");
            arrowSprite = Resources.Load<Sprite>("Sprites/player_arrow");
        }

        // ────────────────────────────────────────────────────────────────────────
        // 줌
        // ────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// 줌 사다리를 만든다. 최대 단계는 radarWorldRadius(씬 값 4000), 아래로 절반씩 내려간다.
        ///   1단계 250m  - 특대 섬 지형 반지름 200m가 통째로 들어가는 최소 배율(섬 안에서 길 찾기용)
        ///   2단계 500m  - 어떤 규모의 섬이든 섬 전체 + 둘러싼 얕은 바다
        ///   3단계 1000m - 기본값. 섬 간격 1200m의 83%라 "이웃 섬은 아직 안 보인다"
        ///   4단계 2000m - 간격 1200m를 넘어서므로 이웃 섬 1~2개가 들어온다
        ///   5단계 4000m - 섬 간격 3.3개분. 광역 항해용
        /// 가장 먼 섬은 원점에서 약 9600m(=baseDistanceStep×8)까지 나가므로 전체 조망은 미니맵이 아니라
        /// [M] 전체 지도의 몫이다. 미니맵을 그 축척까지 넓히면 섬이 전부 3px 점으로 뭉쳐 개편의 목적
        /// (크기 구분)이 사라진다.
        /// </summary>
        private void BuildZoomLadder()
        {
            float widest = ResolveRadarWorldRadius();

            for (int i = 0; i < ZoomStepCount; i++)
                zoomRadii[i] = widest / Mathf.Pow(2f, ZoomStepCount - 1 - i);

            zoomIndex = Mathf.Clamp(DefaultZoomIndex, 0, ZoomStepCount - 1);
        }

        /// <summary>
        /// 줌 사다리의 최대 반경을 결정한다. radarWorldRadius가 양수로 설정돼 있으면(씬 값 4000) 그것을
        /// 쓰고, 0 이하("미설정")면 WorldMapManager의 실제 배치 공식에서 유도한다.
        /// </summary>
        private float ResolveRadarWorldRadius()
        {
            if (radarWorldRadius > 0f)
                return radarWorldRadius;

            if (worldMapManager == null)
                return FallbackRadarWorldRadius;

            float derived = worldMapManager.baseDistanceStep * worldMapManager.initialIslandCount
                + worldMapManager.distanceJitter;

            return derived > 0f ? derived : FallbackRadarWorldRadius;
        }

        /// <summary>줌 단계를 바꾼다(범위 밖이면 무시). 라벨은 값이 바뀔 때만 다시 쓴다.</summary>
        private void SetZoom(int index)
        {
            int clamped = Mathf.Clamp(index, 0, ZoomStepCount - 1);
            if (clamped == zoomIndex)
                return;

            zoomIndex = clamped;
            UpdateZoomLabel();
        }

        private void UpdateZoomLabel()
        {
            if (zoomLabel == null || zoomIndex == lastDisplayedZoomIndex)
                return;

            lastDisplayedZoomIndex = zoomIndex;
            zoomLabel.text = $"줌 {zoomIndex + 1}/{ZoomStepCount} · 반경 {zoomRadii[zoomIndex]:F0}m";
        }

        // ────────────────────────────────────────────────────────────────────────
        // A. 미니맵 (우상단 상시 표시)
        // ────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// 우상단 사각 미니맵을 만든다.
        ///
        /// **사각인 이유**: RectMask2D가 사각 영역으로 정확히 잘라주기 때문에, 축척을 키웠을 때 섬 원이
        /// 패널 밖으로 새어 나가는 문제를 부품 하나로 끝낼 수 있다. 원형으로 만들면 마스크는 사각인데
        /// 배경만 원이라 네 모서리에서 표식이 배경 밖에 떠 보인다.
        ///
        /// **북쪽 고정(회전하지 않는다)인 이유**: (1) [M] 전체 지도도 북쪽 고정이라, 미니맵만 돌면 두
        /// 화면의 섬 배치가 서로 어긋나 머릿속 지도를 두 번 만들어야 한다. (2) 이 게임의 이동은
        /// "섬 목록에서 목적지를 고르는" 방식이라 절대 방위(북동쪽 섬)로 기억하는 편이 쓸모 있다.
        /// 대신 중앙의 플레이어 화살표가 시선 방향으로 돌아 방향 정보를 준다.
        /// </summary>
        private void BuildMinimap()
        {
            var canvas = UIBuilder.CreateCanvas("MinimapCanvas", sortOrder: 9);

            var panel = UIBuilder.CreatePanel(
                canvas.transform, "MinimapPanel",
                anchorMin: new Vector2(1f, 1f), anchorMax: new Vector2(1f, 1f),
                offsetMin: new Vector2(-radarPanelSize - 20f, -radarPanelSize - 20f),
                offsetMax: new Vector2(-20f, -20f),
                color: MinimapBackground, addTopBorder: true);

            // 지도 그림 영역. RectMask2D가 영역 밖으로 나가는 표식을 잘라낸다.
            // 아래쪽 MinimapCaptionHeight 만큼은 정보 띠 자리라 지도에서 뺀다 - 겹쳐 두면 정보 띠가
            // 표식을 가리고, 그렇다고 패널을 아래로 더 키우면 DebugHud(우상단 y -200부터)와 겹친다.
            var viewGo = new GameObject("MapView", typeof(RectTransform), typeof(RectMask2D));
            viewGo.transform.SetParent(panel, false);
            var viewRt = viewGo.GetComponent<RectTransform>();
            viewRt.anchorMin = Vector2.zero;
            viewRt.anchorMax = Vector2.one;
            viewRt.offsetMin = new Vector2(0f, MinimapCaptionHeight);
            viewRt.offsetMax = Vector2.zero;

            var contentGo = new GameObject("Content", typeof(RectTransform));
            contentGo.transform.SetParent(viewRt, false);
            radarContent = contentGo.GetComponent<RectTransform>();
            radarContent.anchorMin = new Vector2(0.5f, 0.5f);
            radarContent.anchorMax = new Vector2(0.5f, 0.5f);
            radarContent.anchoredPosition = Vector2.zero;
            radarContent.sizeDelta = Vector2.zero;

            // 방위 표시. 지도가 고정이므로 N은 영원히 위쪽에 있다 - 그것이 이 표시의 의미다.
            var north = UIBuilder.CreateText(viewRt, "North", "N", 12, new Color(1f, 1f, 1f, 0.75f), TextAnchor.UpperCenter);
            north.raycastTarget = false;
            north.rectTransform.anchorMin = new Vector2(0.5f, 1f);
            north.rectTransform.anchorMax = new Vector2(0.5f, 1f);
            north.rectTransform.pivot = new Vector2(0.5f, 1f);
            north.rectTransform.anchoredPosition = new Vector2(0f, -2f);
            north.rectTransform.sizeDelta = new Vector2(24f, 16f);

            // 플레이어 화살표는 표식보다 항상 위에 있어야 한다(마지막에 만든다).
            var arrowGo = new GameObject("PlayerArrow", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            arrowGo.transform.SetParent(viewRt, false);
            playerArrowRt = arrowGo.GetComponent<RectTransform>();
            playerArrowRt.anchorMin = new Vector2(0.5f, 0.5f);
            playerArrowRt.anchorMax = new Vector2(0.5f, 0.5f);
            playerArrowRt.pivot = new Vector2(0.5f, 0.5f);
            playerArrowRt.anchoredPosition = Vector2.zero;
            playerArrowRt.sizeDelta = new Vector2(14f, 14f);
            var arrowImage = arrowGo.GetComponent<Image>();
            arrowImage.color = Color.white;
            arrowImage.raycastTarget = false;
            if (arrowSprite != null)
            {
                arrowImage.sprite = arrowSprite;
                arrowImage.type = Image.Type.Simple;
                arrowImage.preserveAspect = true;
            }

            // 하단 정보 띠: 줌 단계와 키 안내. 패널 **안**에 넣어 미니맵의 전체 차지 높이를 개편 전과
            // 똑같이 유지한다(DebugHud가 우상단 y -200부터 시작하므로 아래로 더 자라면 겹친다).
            var caption = UIBuilder.CreatePanel(
                panel, "Caption",
                anchorMin: new Vector2(0f, 0f), anchorMax: new Vector2(1f, 0f),
                offsetMin: new Vector2(0f, 0f), offsetMax: new Vector2(0f, MinimapCaptionHeight),
                color: new Color(0f, 0f, 0f, 0.35f));

            zoomLabel = UIBuilder.CreateText(caption, "Zoom", "", 11, NeutralGray, TextAnchor.MiddleCenter);
            zoomLabel.raycastTarget = false;
            zoomLabel.horizontalOverflow = HorizontalWrapMode.Overflow;
            zoomLabel.rectTransform.anchorMin = new Vector2(0f, 1f);
            zoomLabel.rectTransform.anchorMax = new Vector2(1f, 1f);
            zoomLabel.rectTransform.pivot = new Vector2(0.5f, 1f);
            zoomLabel.rectTransform.anchoredPosition = new Vector2(0f, -1f);
            zoomLabel.rectTransform.sizeDelta = new Vector2(0f, 15f);

            // 이 줄은 게임에서 **항상 화면에 떠 있는 유일한 키 안내**다. Esc(조작 설정) 입구도 여기 얹는다.
            // Esc 키 값은 박아두지 않고 SettingsMenuController가 실제로 들고 있는 값을 읽는다.
            var settingsMenu = FindAnyObjectByType<SettingsMenuController>();
            KeyCode settingsKey = settingsMenu != null ? settingsMenu.toggleKey : KeyCode.Escape;

            var hint = UIBuilder.CreateText(caption, "Hint",
                $"[{toggleKey}] 지도 · [+/-] 줌 · [{settingsKey}] 조작", 11,
                new Color(1f, 1f, 1f, 0.62f), TextAnchor.MiddleCenter);
            hint.raycastTarget = false;
            // 한 줄에 다 들어가지 않아도 두 번째 줄은 verticalOverflow 기본값 Truncate로 통째로 사라진다.
            // 가로로 넘치게 두는 편이 안전하다(우상단이라 가운데 정렬 기준 좌우로 몇 px 삐져나올 뿐이다).
            hint.horizontalOverflow = HorizontalWrapMode.Overflow;
            hint.rectTransform.anchorMin = new Vector2(0f, 0f);
            hint.rectTransform.anchorMax = new Vector2(1f, 0f);
            hint.rectTransform.pivot = new Vector2(0.5f, 0f);
            hint.rectTransform.anchoredPosition = new Vector2(0f, 1f);
            hint.rectTransform.sizeDelta = new Vector2(0f, 15f);

            UpdateZoomLabel();
        }

        /// <summary>
        /// 미니맵을 갱신한다. 플레이어가 항상 중심이고, 섬은 지형 반지름에 비례한 원으로 그린다.
        /// 오브젝트를 새로 만들지 않는다(EnsureMarkerCount가 부족할 때만 늘린다).
        /// </summary>
        private void RefreshMinimap()
        {
            if (worldMapManager == null || player == null || radarContent == null)
                return;

            // UI의 Z축 회전은 반시계가 양수라, 시계 방향으로 도는 Y축 오일러각과 부호가 반대다.
            if (playerArrowRt != null)
                playerArrowRt.localEulerAngles = new Vector3(0f, 0f, -player.eulerAngles.y);

            var islands = worldMapManager.islands;
            if (islands == null)
                return;

            EnsureMarkerCount(radarMarkers, radarContent, islands.Count, false);

            // 지도 영역은 정보 띠를 뺀 만큼 세로가 짧다. "반경 Xm"이 **모든 방향으로** 보장되도록
            // 축척은 짧은 쪽(세로)을 기준으로 잡는다 - 가로로는 그보다 더 멀리까지 보이게 된다.
            float limitX = radarPanelSize * 0.5f - MinimapEdgePadding;
            float limitY = (radarPanelSize - MinimapCaptionHeight) * 0.5f - MinimapEdgePadding;
            float pixelsPerMeter = Mathf.Min(limitX, limitY) / Mathf.Max(0.0001f, zoomRadii[zoomIndex]);
            float maxDiameter = radarPanelSize * 1.6f;

            for (int i = 0; i < islands.Count; i++)
            {
                var island = islands[i];
                var marker = radarMarkers[i];

                Vector3 rel = island.mapPosition - player.position;
                Vector2 point = new Vector2(rel.x, rel.z) * pixelsPerMeter;

                bool revealed = IsRevealed(island);
                bool offscreen = Mathf.Abs(point.x) > limitX || Mathf.Abs(point.y) > limitY;
                if (offscreen)
                {
                    point.x = Mathf.Clamp(point.x, -limitX, limitX);
                    point.y = Mathf.Clamp(point.y, -limitY, limitY);
                }

                float diameter = offscreen
                    ? EdgeMarkerDiameter
                    : MarkerDiameter(island, revealed, pixelsPerMeter, maxDiameter);

                marker.root.gameObject.SetActive(true);
                marker.root.anchoredPosition = point;
                ApplyMarkerVisual(marker, island, revealed, diameter, offscreen);
            }

            for (int i = islands.Count; i < radarMarkers.Count; i++)
                radarMarkers[i].root.gameObject.SetActive(false);
        }

        // ────────────────────────────────────────────────────────────────────────
        // 표식 (미니맵 / 전체 지도 공용)
        // ────────────────────────────────────────────────────────────────────────

        /// <summary>시작 섬은 도착 판정 없이도 아는 섬이다. isDiscovered와 반드시 OR로 묶는다.</summary>
        private static bool IsRevealed(IslandInstance island)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            // [디버그 전체 지도] 미니맵/전체 지도/섬 목록/체크리스트의 발견 표시는 전부 이 메서드
            // 하나를 지나므로, 여기 한 곳만 우회하면 "모든 섬이 발견된 것처럼" 보인다.
            // **표시 계층 우회일 뿐 island.isDiscovered에는 아무것도 쓰지 않는다** - 세이브는
            // isDiscovered만 기록하므로(SaveLoadController), 토글을 껐다 켜도 실제 발견 상태가
            // 오염되지 않는다(목록의 "발견함/미발견" 상태 문구는 일부러 실제 값을 그대로 보여준다).
            // 가드는 DebugHud의 관례와 동일: #if + Debug.isDebugBuild(DebugRevealAllActive 내부).
            if (island != null && IslandTravel.DebugRevealAllActive)
                return true;
#endif
            return island != null && (island.isDiscovered || island.isStartingIsland);
        }

        /// <summary>
        /// 축척이 넓어졌을 때도 규모가 읽히도록 보장하는 **최소 지름**(px).
        /// 실제 반지름 비(50:90:140:200 = 1:1.8:2.8:4)를 최소치에 그대로 쓰면 소형이 3px 아래로 내려가
        /// 사라진다. 그래서 최소치 구간에서만 비를 압축(6:8:11:14 = 1:1.33:1.83:2.33)하고, 줌인해서
        /// 실제 크기가 이 값을 넘어서면 **실제 비례가 이긴다**.
        /// </summary>
        private static float SizeFloorDiameter(IslandSize size)
        {
            switch (size)
            {
                case IslandSize.Small: return 6f;
                case IslandSize.Medium: return 8f;
                case IslandSize.Large: return 11f;
                case IslandSize.ExtraLarge: return 14f;
                default: return 6f;
            }
        }

        /// <summary>
        /// 섬 표식의 지름(px). 탐사한 섬은 지형 반지름에 비례하고, 미탐사 섬은 **항상 고정 크기**다
        /// (크기를 그대로 그리면 가보지 않고도 대형/특대를 알아내는 정보 누출이 된다).
        /// </summary>
        private static float MarkerDiameter(IslandInstance island, bool revealed, float pixelsPerMeter, float maxDiameter)
        {
            if (!revealed)
                return UnknownMarkerDiameter;

            float trueDiameter = IslandSizeMetrics.GetTerrainRadius(island.size) * 2f * pixelsPerMeter;
            float floor = SizeFloorDiameter(island.size);
            return Mathf.Clamp(Mathf.Max(trueDiameter, floor), 4f, maxDiameter);
        }

        /// <summary>표식의 크기·색·윤곽을 상태에 맞춰 적용한다(오브젝트를 만들지 않는다).</summary>
        private void ApplyMarkerVisual(IslandMarker marker, IslandInstance island, bool revealed, float diameter, bool offscreen)
        {
            bool isCurrent = islandTravel != null && island.islandId == islandTravel.currentIslandId;
            bool isSelected = island.islandId == selectedIslandId;

            Color fillColor = !revealed ? LandUnknown : (isCurrent ? LandCurrent : LandKnown);
            Color ringColor = !revealed ? RingUnknown : (isCurrent ? RingCurrent : RingKnown);

            if (isSelected)
                ringColor = MedicGreen; // 선택 표시는 인벤토리 선택 테두리와 같은 색·같은 의미로 통일

            // 시야 밖 섬은 "속이 빈 작은 링"으로 형태를 바꾼다. 색이 아니라 형태로 구분하므로
            // 야간·색맹 조건에서도 "저건 방향 표시지 섬 크기가 아니다"가 읽힌다.
            if (offscreen)
                fillColor.a = 0f;

            marker.fillRt.sizeDelta = new Vector2(diameter, diameter);
            marker.fill.color = fillColor;

            float ringDiameter = diameter + (offscreen ? 3f : 4f);
            marker.ringRt.sizeDelta = new Vector2(ringDiameter, ringDiameter);
            marker.ring.color = ringColor;

            if (marker.label != null)
            {
                marker.label.rectTransform.anchoredPosition = new Vector2(0f, -(diameter * 0.5f + 2f));
                marker.label.color = revealed ? new Color(1f, 1f, 1f, 0.9f) : new Color(1f, 1f, 1f, 0.4f);
            }
        }

        /// <summary>표식 풀이 모자라면 필요한 만큼만 새로 만든다.</summary>
        private void EnsureMarkerCount(List<IslandMarker> pool, RectTransform layer, int count, bool interactive)
        {
            while (pool.Count < count)
                pool.Add(CreateMarker(layer, interactive));
        }

        /// <summary>
        /// 표식 하나를 만든다. 구성(아래→위): 윤곽 링 → 채움 원 → 이름표 → 클릭 판정.
        /// 전체 지도용(interactive)만 이름표와 클릭 판정을 갖는다.
        /// </summary>
        private IslandMarker CreateMarker(RectTransform layer, bool interactive)
        {
            var marker = new IslandMarker();

            var rootGo = new GameObject("IslandMarker", typeof(RectTransform));
            rootGo.transform.SetParent(layer, false);
            marker.root = rootGo.GetComponent<RectTransform>();
            marker.root.anchorMin = new Vector2(0.5f, 0.5f);
            marker.root.anchorMax = new Vector2(0.5f, 0.5f);
            marker.root.pivot = new Vector2(0.5f, 0.5f);
            marker.root.sizeDelta = Vector2.zero;

            marker.ring = CreateCircle(marker.root, "Ring", 10f, RingKnown, ringSprite);
            marker.ringRt = marker.ring.rectTransform;

            marker.fill = CreateCircle(marker.root, "Fill", 6f, LandKnown, dotSprite);
            marker.fillRt = marker.fill.rectTransform;

            if (interactive)
            {
                marker.label = UIBuilder.CreateText(marker.root, "Label", "", 11, Color.white, TextAnchor.UpperCenter);
                marker.label.raycastTarget = false;
                marker.label.horizontalOverflow = HorizontalWrapMode.Overflow;
                marker.label.rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
                marker.label.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
                marker.label.rectTransform.pivot = new Vector2(0.5f, 1f);
                marker.label.rectTransform.sizeDelta = new Vector2(150f, 16f);
                var labelShadow = marker.label.gameObject.AddComponent<Shadow>();
                labelShadow.effectColor = new Color(0f, 0f, 0f, 0.9f);
                labelShadow.effectDistance = new Vector2(1f, -1f);

                // 표식이 6px까지 작아질 수 있으므로 클릭 판정은 눈에 보이는 원이 아니라 고정 크기의
                // 투명 사각형이 받는다(알파 0이어도 raycastTarget이 켜져 있으면 클릭은 잡힌다).
                var hitGo = new GameObject("Hit", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
                hitGo.transform.SetParent(marker.root, false);
                var hitRt = hitGo.GetComponent<RectTransform>();
                hitRt.anchorMin = new Vector2(0.5f, 0.5f);
                hitRt.anchorMax = new Vector2(0.5f, 0.5f);
                hitRt.pivot = new Vector2(0.5f, 0.5f);
                hitRt.sizeDelta = new Vector2(24f, 24f);
                var hitImage = hitGo.GetComponent<Image>();
                hitImage.color = Color.clear;
                hitImage.raycastTarget = true;

                marker.hitButton = hitGo.GetComponent<Button>();
                // 콜백은 만들 때 한 번만 등록하고 marker.islandId를 이벤트 시점에 읽는다
                // (매 갱신마다 Remove/Add를 반복하지 않기 위함).
                marker.hitButton.onClick.AddListener(() =>
                {
                    selectedIslandId = marker.islandId;
                    RefreshChecklist();
                });
            }

            return marker;
        }

        /// <summary>원형 스프라이트 Image 하나를 만든다. 스프라이트가 없으면 사각형으로 그려진다.</summary>
        private static Image CreateCircle(Transform parent, string name, float diameter, Color color, Sprite sprite)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(parent, false);

            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = new Vector2(diameter, diameter);

            var image = go.GetComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            if (sprite != null)
            {
                image.sprite = sprite;
                image.type = Image.Type.Simple;
                image.preserveAspect = true;
            }

            return image;
        }

        // ────────────────────────────────────────────────────────────────────────
        // B. 전체 지도 창 ([M])
        // ────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// 전체 지도 창을 만든다. 인벤토리 창과 같은 규격이다: 제목 표시줄 + 우상단 X, 제목 표시줄을
        /// 끌어 이동, 화면 밖으로 못 나가게 클램프(UIDragHandle 재사용), 알파 0.93.
        /// 왼쪽이 지도, 오른쪽이 섬 목록이다 - 예전에 따로 뜨던 목록 패널은 여기로 흡수됐다.
        /// </summary>
        private void BuildMapWindow()
        {
            var canvas = UIBuilder.CreateCanvas("WorldMapCanvas", sortOrder: 11);
            canvasRect = canvas.GetComponent<RectTransform>();

            mapWindowRt = UIBuilder.CreatePanel(
                canvas.transform, "WorldMapWindow",
                anchorMin: new Vector2(0.5f, 0.5f), anchorMax: new Vector2(0.5f, 0.5f),
                offsetMin: Vector2.zero, offsetMax: Vector2.zero,
                color: WindowBackground, addTopBorder: true);

            mapWindowRt.pivot = new Vector2(0.5f, 1f);
            mapWindowRt.sizeDelta = new Vector2(MapWindowWidth, MapWindowHeight);
            mapWindowRoot = mapWindowRt.gameObject;

            BuildMapTitleBar();
            BuildMapView();
            BuildIslandListColumn();
            BuildMapFooter();
        }

        /// <summary>제목 표시줄(드래그 손잡이 + 닫기 버튼). 인벤토리와 완전히 같은 조립이다.</summary>
        private void BuildMapTitleBar()
        {
            var titleBar = UIBuilder.CreatePanel(
                mapWindowRt, "TitleBar",
                anchorMin: new Vector2(0f, 1f), anchorMax: new Vector2(1f, 1f),
                offsetMin: new Vector2(0f, -TitleBarHeight), offsetMax: Vector2.zero,
                color: new Color(1f, 1f, 1f, 0.07f));

            var title = UIBuilder.CreateText(titleBar, "Title", $"세계 지도 ({toggleKey})", 20, Color.white, TextAnchor.MiddleLeft);
            title.raycastTarget = false; // 제목 글자가 드래그 입력을 가로채지 않게 한다
            title.rectTransform.anchorMin = Vector2.zero;
            title.rectTransform.anchorMax = Vector2.one;
            title.rectTransform.offsetMin = new Vector2(12f, 0f);
            title.rectTransform.offsetMax = new Vector2(-40f, 0f);

            var close = UIBuilder.CreateButton(titleBar, "Close", "X", () => SetMapOpen(false));
            var closeRt = close.GetComponent<RectTransform>();
            closeRt.anchorMin = new Vector2(1f, 1f);
            closeRt.anchorMax = new Vector2(1f, 1f);
            closeRt.pivot = new Vector2(1f, 1f);
            closeRt.sizeDelta = new Vector2(30f, 24f);
            closeRt.anchoredPosition = new Vector2(-5f, -5f);

            var closeImage = close.GetComponent<Image>();
            if (closeImage != null)
            {
                Color closeColor = DangerRed;
                closeColor.a = 0.75f;
                closeImage.color = closeColor;
            }

            mapDragHandle = titleBar.gameObject.AddComponent<UIDragHandle>();
            mapDragHandle.target = mapWindowRt;
            mapDragHandle.bounds = canvasRect;
            mapDragHandle.handleHeight = TitleBarHeight;
            mapDragHandle.onMoved = position =>
            {
                savedMapPosition = position;
                hasSavedMapPosition = true;
            };
        }

        /// <summary>지도 그림 영역(왼쪽 열): 검은 바다 → 탐사 원반 → 섬 표식 → 플레이어 → 축척자.</summary>
        private void BuildMapView()
        {
            var view = UIBuilder.CreatePanel(
                mapWindowRt, "MapView",
                anchorMin: new Vector2(0f, 1f), anchorMax: new Vector2(0f, 1f),
                offsetMin: Vector2.zero, offsetMax: Vector2.zero,
                color: UnexploredSea);

            view.pivot = new Vector2(0f, 1f);
            view.anchoredPosition = new Vector2(WindowPadding, -MapViewTop);
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
            var north = UIBuilder.CreateText(view, "North", "N", 14, new Color(1f, 1f, 1f, 0.8f), TextAnchor.UpperCenter);
            north.raycastTarget = false;
            north.rectTransform.anchorMin = new Vector2(0.5f, 1f);
            north.rectTransform.anchorMax = new Vector2(0.5f, 1f);
            north.rectTransform.pivot = new Vector2(0.5f, 1f);
            north.rectTransform.anchoredPosition = new Vector2(0f, -4f);
            north.rectTransform.sizeDelta = new Vector2(28f, 18f);

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

            scaleLabel = UIBuilder.CreateText(view, "ScaleLabel", "", 11, new Color(1f, 1f, 1f, 0.7f), TextAnchor.LowerLeft);
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
            float columnX = WindowPadding + MapViewSize + 14f;

            var header = UIBuilder.CreateText(mapWindowRt, "ListHeader", "섬 목록 · 클릭하면 지도에서 찾아준다", 12, NeutralGray, TextAnchor.MiddleLeft);
            header.raycastTarget = false;
            header.horizontalOverflow = HorizontalWrapMode.Overflow;
            header.rectTransform.anchorMin = new Vector2(0f, 1f);
            header.rectTransform.anchorMax = new Vector2(0f, 1f);
            header.rectTransform.pivot = new Vector2(0f, 1f);
            header.rectTransform.anchoredPosition = new Vector2(columnX, -MapViewTop);
            header.rectTransform.sizeDelta = new Vector2(ListColumnWidth, ListHeaderHeight);

            // [B52] 50섬 대응: 행이 50개면 목록 전체 높이가 약 1,500px(행 26 + 간격 4)로 열 높이
            // (약 446px)의 3배가 넘는다. 예전처럼 컨테이너를 창에 직접 붙이면 마스크가 없어 행들이
            // 창 아래 체크리스트/상태 줄을 덮고 화면 밖까지 그대로 그려진다. InventorySlotView와 같은
            // 조립(ScrollRect + RectMask2D 뷰포트 + ContentSizeFitter 콘텐츠)으로 세로 스크롤을 붙인다.
            var scrollGo = new GameObject("IslandListScroll", typeof(RectTransform), typeof(ScrollRect));
            scrollGo.transform.SetParent(mapWindowRt, false);
            var scrollRt = scrollGo.GetComponent<RectTransform>();
            scrollRt.anchorMin = new Vector2(0f, 1f);
            scrollRt.anchorMax = new Vector2(0f, 1f);
            scrollRt.pivot = new Vector2(0f, 1f);
            scrollRt.anchoredPosition = new Vector2(columnX, -(MapViewTop + ListHeaderHeight + 4f));
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
            checklistLabel = UIBuilder.CreateText(mapWindowRt, "Checklist", "", 12, new Color(0.85f, 0.85f, 0.85f, 1f), TextAnchor.LowerLeft);
            checklistLabel.raycastTarget = false;
            checklistLabel.rectTransform.anchorMin = new Vector2(0f, 0f);
            checklistLabel.rectTransform.anchorMax = new Vector2(1f, 0f);
            checklistLabel.rectTransform.pivot = new Vector2(0.5f, 0f);
            checklistLabel.rectTransform.anchoredPosition = new Vector2(0f, StatusHeight + 8f + 4f);
            // 두 줄 자리를 유지한다. 한 줄로 두면 문구가 폭을 넘는 순간 둘째 줄이 Truncate로 통째로
            // 사라지고, 정작 중요한 도구 안내만 조용히 잘려나간다(힌트가 반만 보이면 함정이 된다).
            checklistLabel.rectTransform.sizeDelta = new Vector2(-WindowPadding * 2f, ChecklistHeight);

            statusLabel = UIBuilder.CreateText(mapWindowRt, "Status", "", 13, new Color(1f, 0.9f, 0.4f, 1f), TextAnchor.LowerLeft);
            statusLabel.raycastTarget = false;
            statusLabel.horizontalOverflow = HorizontalWrapMode.Overflow;
            statusLabel.rectTransform.anchorMin = new Vector2(0f, 0f);
            statusLabel.rectTransform.anchorMax = new Vector2(1f, 0f);
            statusLabel.rectTransform.pivot = new Vector2(0.5f, 0f);
            statusLabel.rectTransform.anchoredPosition = new Vector2(0f, 8f);
            statusLabel.rectTransform.sizeDelta = new Vector2(-WindowPadding * 2f, StatusHeight);
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
        /// 반경 1500m짜리 밝은 원반을 깐다. 원반은 풀링하며 갱신마다 새로 만들지 않는다.
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
                    // 340m 간격 무리를 이루는데, 지도 축척(약 0.02px/m)에서 표식이 7px 간격이 되므로
                    // 무리마다 "?" 수십 장이 같은 자리에 겹쳐 잉크 얼룩이 된다. 미탐사 섬의 존재는
                    // 검은 원 표식이 이미 알리고 있어 정보 손실이 없다.
                    marker.label.text = revealed
                        ? $"섬 {island.islandId} · {GetSizeKoreanName(island.size)}{(isCurrent ? " (현재 위치)" : "")}"
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

            for (int i = 0; i < islands.Count; i++)
            {
                var island = islands[i];
                var row = islandRows[i];
                bool revealed = IsRevealed(island);

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
            }

            for (int i = islands.Count; i < islandRows.Count; i++)
                islandRows[i].rowGo.SetActive(false);

            if (statusLabel != null)
                statusLabel.text = lastTravelStatus;

            RefreshChecklist();
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

            var infoLabel = UIBuilder.CreateText(rowGo.transform, "Info", "", 13, Color.white, TextAnchor.MiddleLeft);
            infoLabel.horizontalOverflow = HorizontalWrapMode.Overflow;
            infoLabel.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;

            var travelButton = UIBuilder.CreateButton(rowGo.transform, "TravelButton", "이동", null);
            travelButton.gameObject.AddComponent<LayoutElement>().preferredWidth = 70f;

            var row = new IslandRow { rowGo = rowGo, infoLabel = infoLabel, travelButton = travelButton };

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
            return $"섬 {island.islandId}  ·  {sizeText}  ·  {distance:F0}m  ·  {statusText}";
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
                : "이동 실패: 고무보트가 필요하거나, 해류가 강해 이 섬까지는 아직 갈 수 없습니다.";

            // 이동에 성공하면 새 섬이 탐사 구역으로 밝아진다. 다음 프레임을 기다리지 않고 바로 반영한다.
            mapRefreshTimer = 0f;
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
