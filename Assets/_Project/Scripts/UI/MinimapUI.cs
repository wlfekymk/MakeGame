using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using MakeGame.Data;
using MakeGame.Player;
using MakeGame.Systems;

namespace MakeGame.UI
{
    /// <summary>
    /// 미니맵(레이더)과 발견한 섬 목록 UI.
    /// 화면 우상단에 항상 떠 있는 작은 레이더는 플레이어 주변 섬들을 점으로 표시해 방향을 가늠할 수 있게 하고,
    /// [M] 키를 누르면 전체 섬 목록 패널이 열려 각 섬의 규모/거리/발견 여부를 확인하고
    /// 고무보트로 빠르게 이동(IslandTravel.TryTravelTo)할 수 있다.
    /// 참고: IslandTravel.TryTravelTo는 기존에 호출하는 곳이 이 UI가 생기기 전까지 전혀 없어서
    /// 실제로는 플레이어가 사용할 방법이 없는 "죽은" 시스템이었다. 이 UI가 그 유일한 진입점이다.
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

        [Tooltip("섬 목록 패널을 여닫는 키")]
        public KeyCode toggleKey = KeyCode.M;

        [Header("레이더 설정")]
        [Tooltip("레이더에 표시할 실제 월드 반경(미터). 이보다 먼 섬은 가장자리에 붙어서 표시된다. " +
            "0 이하로 두면(기본값) WorldMapManager의 섬 배치 설정(baseDistanceStep 등)에서 자동으로 유도한다. " +
            "특정 값을 강제하고 싶으면 여기 직접 양수를 입력하면 그 값이 항상 우선한다.")]
        // 개선(B2-10): 예전엔 4000이라는 값을 이 필드 하나에 하드코딩해뒀는데, WorldMapManager.baseDistanceStep
        // (섬 배치 간격)이 바뀌면 이 값과 조용히 어긋나 섬이 레이더 밖으로 나가거나 반대로 너무 촘촘해 보일
        // 위험이 있었다. 배치 1에서 확립한 패턴대로, 필드 자체는 남겨 씬/Inspector에서 명시적으로 override할
        // 수 있게 하되(0보다 크면 그 값이 항상 우선), 기본값은 "미설정"을 뜻하는 0으로 낮추고 실제 반경은
        // ResolveRadarWorldRadius()가 WorldMapManager 배치 설정에서 매번 유도하도록 했다.
        public float radarWorldRadius = 0f;

        [Tooltip("레이더 패널의 한 변 크기(픽셀)")]
        public float radarPanelSize = 160f;

        // worldMapManager 참조가 없어(Inspector 미할당 등) 유도 계산 자체가 불가능할 때만 쓰는 최후 안전값.
        // 필드 기본값이 아니라 "정말 아무 정보도 없을 때"의 방어적 fallback이라 별도 상수로 분리했다.
        private const float FallbackRadarWorldRadius = 4000f;

        // BuildRadar()에서 한 번 계산해 캐시해두는 실제 사용 반경(radarWorldRadius를 그대로 쓰거나,
        // 0 이하면 WorldMapManager 설정에서 유도한 값). worldMapManager 참조는 Start() 이전에 이미
        // Inspector에서 확정되므로 매 프레임 다시 계산할 필요가 없다.
        private float effectiveRadarWorldRadius;

        /// <summary>섬 하나에 대응하는 레이더 위 점(dot) UI.
        /// 퀄리티 개선: 예전에는 sprite 없는 사각형 Image 하나뿐이라 배경 대비가 약하고 사각형으로 보였다.
        /// 이제 원형 스프라이트(radar_dot)를 쓰고, 그 아래 흰 테두리 링(radar_ring)을 겹쳐 어떤 색 점이든
        /// 배경과 또렷하게 구분되게 한다.</summary>
        private class RadarDot
        {
            public RectTransform rt;
            public Image image;
            public RectTransform borderRt;
        }

        /// <summary>섬 목록 패널의 한 줄(정보 텍스트 + 이동 버튼)을 구성하는 UI 요소.</summary>
        private class IslandRow
        {
            public GameObject rowGo;
            public Text infoLabel;
            public Button travelButton;
        }

        private RectTransform radarDotsLayer;
        private readonly List<RadarDot> radarDotPool = new List<RadarDot>();

        /// <summary>플레이어 자신을 나타내는 레이더 중심 화살표. 시선(Y축 회전)에 맞춰 매 프레임 돌아간다.</summary>
        private RectTransform playerArrowRt;

        private GameObject listPanelRoot;
        private RectTransform listContainer;
        private Text statusLabel;
        private readonly List<IslandRow> islandRows = new List<IslandRow>();

        private string lastTravelStatus = "";

        /// <summary>시작 시 레이더와 섬 목록 패널을 만들고, 목록 패널은 기본적으로 닫아 둔다.</summary>
        private void Start()
        {
            effectiveRadarWorldRadius = ResolveRadarWorldRadius();
            BuildRadar();
            BuildListPanel();
            SetListOpen(false);
        }

        /// <summary>
        /// 레이더가 실제로 표시할 월드 반경을 결정한다. radarWorldRadius가 Inspector/씬에서 명시적으로
        /// 양수로 설정돼 있으면(예: 디렉터가 씬에서 직접 override) 그 값을 그대로 쓴다. 그렇지 않으면(0
        /// 이하, 즉 "미설정") WorldMapManager의 실제 섬 배치 공식(FindValidPosition의
        /// baseDistanceStep * islands.Count + distanceJitter)을 그대로 따라, 초기 생성되는 마지막 섬이
        /// 배치될 수 있는 최대 거리를 계산해 반경으로 삼는다. 이러면 baseDistanceStep/initialIslandCount/
        /// distanceJitter 중 하나가 바뀌어도 레이더 범위가 자동으로 맞춰진다.
        /// </summary>
        private float ResolveRadarWorldRadius()
        {
            if (radarWorldRadius > 0f)
                return radarWorldRadius;

            if (worldMapManager == null)
                return FallbackRadarWorldRadius;

            return worldMapManager.baseDistanceStep * worldMapManager.initialIslandCount + worldMapManager.distanceJitter;
        }

        /// <summary>매 프레임 레이더를 갱신하고, 목록 패널이 열려 있으면 목록도 함께 갱신한다.</summary>
        private void Update()
        {
            if (listPanelRoot != null && Input.GetKeyDown(toggleKey))
                SetListOpen(!listPanelRoot.activeSelf);

            RefreshRadar();

            if (listPanelRoot != null && listPanelRoot.activeSelf)
                RefreshList();
        }

        /// <summary>화면 우상단에 항상 표시되는 원형 배경의 레이더 패널을 만든다.</summary>
        private void BuildRadar()
        {
            var canvas = UIBuilder.CreateCanvas("MinimapCanvas", sortOrder: 9);

            var panel = UIBuilder.CreatePanel(
                canvas.transform, "RadarPanel",
                anchorMin: new Vector2(1f, 1f), anchorMax: new Vector2(1f, 1f),
                offsetMin: new Vector2(-radarPanelSize - 20f, -radarPanelSize - 20f),
                offsetMax: new Vector2(-20f, -20f),
                color: new Color(0f, 0f, 0f, 0.5f), addTopBorder: true);

            var border = UIBuilder.CreateText(panel, "Hint", "[M] 지도", 12, new Color(1f, 1f, 1f, 0.7f), TextAnchor.LowerCenter);
            border.rectTransform.anchorMin = new Vector2(0f, 0f);
            border.rectTransform.anchorMax = new Vector2(1f, 0f);
            border.rectTransform.pivot = new Vector2(0.5f, 0f);
            border.rectTransform.anchoredPosition = new Vector2(0f, 2f);
            border.rectTransform.sizeDelta = new Vector2(0f, 16f);

            var dotsLayerGo = new GameObject("DotsLayer", typeof(RectTransform));
            dotsLayerGo.transform.SetParent(panel, false);
            radarDotsLayer = dotsLayerGo.GetComponent<RectTransform>();
            radarDotsLayer.anchorMin = new Vector2(0.5f, 0.5f);
            radarDotsLayer.anchorMax = new Vector2(0.5f, 0.5f);
            radarDotsLayer.anchoredPosition = Vector2.zero;
            radarDotsLayer.sizeDelta = Vector2.zero;

            // 레이더 중심의 플레이어 표시.
            // 퀄리티 개선: 예전엔 방향 정보가 전혀 없는 흰 사각형이라 "어느 쪽을 보고 있는지" 알 수 없었다.
            // 삼각 화살표 스프라이트(player_arrow)로 바꾸고, RefreshRadar에서 플레이어 Y축 회전에 맞춰
            // 매 프레임 돌려서 현재 시선/이동 방향을 레이더에서도 바로 파악할 수 있게 한다.
            var playerDot = UIBuilder.CreateIcon(radarDotsLayer, "PlayerDot", 14f, Color.white, "");
            playerDot.anchorMin = new Vector2(0.5f, 0.5f);
            playerDot.anchorMax = new Vector2(0.5f, 0.5f);
            playerDot.anchoredPosition = Vector2.zero;
            var arrowSprite = Resources.Load<Sprite>("Sprites/player_arrow");
            var playerImage = playerDot.GetComponent<Image>();
            if (arrowSprite != null && playerImage != null)
            {
                playerImage.sprite = arrowSprite;
                playerImage.type = Image.Type.Simple;
                playerImage.preserveAspect = true;
            }
            playerArrowRt = playerDot;
        }

        /// <summary>
        /// 레이더에 표시할 섬 점(dot)들의 위치와 색을 갱신한다.
        /// 플레이어 기준 상대 좌표를 레이더 반경 비율로 축소해 배치하고, 범위를 벗어나면 가장자리에 붙인다.
        /// </summary>
        private void RefreshRadar()
        {
            if (worldMapManager == null || player == null || radarDotsLayer == null)
                return;

            // 플레이어 화살표를 실제 시선 방향(Y축 회전)에 맞춰 돌린다.
            // UI의 RectTransform Z축 회전은 반시계 방향이 양수라, 시계 방향으로 도는 Y축 오일러각과
            // 부호가 반대라서 -player.eulerAngles.y를 넣어야 화면상 방향이 실제 회전과 일치한다.
            if (playerArrowRt != null)
                playerArrowRt.localEulerAngles = new Vector3(0f, 0f, -player.eulerAngles.y);

            var islands = worldMapManager.islands;
            EnsureRadarDotCount(islands.Count);

            // effectiveRadarWorldRadius는 Start()에서 ResolveRadarWorldRadius()로 한 번만 계산해둔 값이다
            // (radarWorldRadius를 그대로 쓰거나, 0 이하면 WorldMapManager 배치 설정에서 유도).
            // qa 지적: 유도값이 0 이하가 되는 극단적인 경우(예: worldMapManager 설정이 전부 0으로 잘못
            // 들어간 경우) scale이 Infinity가 되고, 플레이어와 좌표가 겹친 섬(rel=(0,0))에서 0*Infinity=NaN이
            // 나와 RectTransform이 깨질 수 있다. 스포너들(IslandResourceSpawner 등)이 쓰는 것과 동일한
            // Mathf.Max(0.0001f, ...) 패턴으로 분모가 0 이하로 내려가지 않게 방어한다.
            float scale = (radarPanelSize / 2f) / Mathf.Max(0.0001f, effectiveRadarWorldRadius);
            float maxOffset = radarPanelSize / 2f - 6f;

            for (int i = 0; i < islands.Count; i++)
            {
                var island = islands[i];
                var dot = radarDotPool[i];

                Vector3 rel = island.mapPosition - player.position;
                Vector2 offset = new Vector2(rel.x, rel.z) * scale;
                if (offset.magnitude > maxOffset)
                    offset = offset.normalized * maxOffset;

                dot.rt.anchoredPosition = offset;
                dot.rt.gameObject.SetActive(true);
                dot.image.color = GetIslandDotColor(island);

                // 테두리 링은 점과 같은 위치를 따라가되 항상 흰색으로 고정해, 어떤 점 색이든
                // 어두운 레이더 배경과 대비되도록 한다.
                if (dot.borderRt != null)
                {
                    dot.borderRt.anchoredPosition = offset;
                    dot.borderRt.gameObject.SetActive(true);
                }
            }

            for (int i = islands.Count; i < radarDotPool.Count; i++)
            {
                radarDotPool[i].rt.gameObject.SetActive(false);
                if (radarDotPool[i].borderRt != null)
                    radarDotPool[i].borderRt.gameObject.SetActive(false);
            }
        }

        /// <summary>레이더 점 풀의 개수가 부족하면 필요한 만큼 새로 만든다.
        /// 퀄리티 개선: 예전엔 sprite 없는 사각형 Image라 배경과 잘 구분되지 않았다. 이제 원형 스프라이트
        /// (radar_dot)로 점을 그리고, 그 뒤에 한 단계 더 큰 흰 테두리 링(radar_ring)을 깔아 항상 또렷하게
        /// 보이게 한다.</summary>
        private void EnsureRadarDotCount(int count)
        {
            var dotSprite = Resources.Load<Sprite>("Sprites/radar_dot");
            var ringSprite = Resources.Load<Sprite>("Sprites/radar_ring");

            while (radarDotPool.Count < count)
            {
                // 테두리 링을 점보다 먼저 만들어 형제 순서상 뒤에 깔리게 한다(점이 그 위에 그려짐).
                var borderRt = UIBuilder.CreateIcon(radarDotsLayer, $"DotBorder{radarDotPool.Count}", 9f, new Color(1f, 1f, 1f, 0.9f), "");
                borderRt.anchorMin = new Vector2(0.5f, 0.5f);
                borderRt.anchorMax = new Vector2(0.5f, 0.5f);
                var borderImage = borderRt.GetComponent<Image>();
                if (ringSprite != null && borderImage != null)
                {
                    borderImage.sprite = ringSprite;
                    borderImage.type = Image.Type.Simple;
                    borderImage.preserveAspect = true;
                }

                var iconRt = UIBuilder.CreateIcon(radarDotsLayer, $"Dot{radarDotPool.Count}", 6f, Color.gray, "");
                iconRt.anchorMin = new Vector2(0.5f, 0.5f);
                iconRt.anchorMax = new Vector2(0.5f, 0.5f);
                var dotImage = iconRt.GetComponent<Image>();
                if (dotSprite != null && dotImage != null)
                {
                    dotImage.sprite = dotSprite;
                    dotImage.type = Image.Type.Simple;
                    dotImage.preserveAspect = true;
                }

                radarDotPool.Add(new RadarDot { rt = iconRt, image = dotImage, borderRt = borderRt });
            }
        }

        /// <summary>
        /// 섬 상태에 따라 레이더 점 색을 정한다. 시작 섬/발견한 섬은 초록, 아직 발견하지 못한 섬은 회색(약한 신호)으로 표시한다.
        /// </summary>
        private Color GetIslandDotColor(IslandInstance island)
        {
            if (island.isStartingIsland)
                return new Color(1f, 0.85f, 0.2f, 1f); // 금색: 시작 섬

            return island.isDiscovered
                ? new Color(0.3f, 0.9f, 0.4f, 1f)   // 초록: 발견한 섬
                : new Color(0.6f, 0.6f, 0.6f, 0.6f); // 회색: 아직 발견하지 못한 섬(먼 발견되지 않은 지형)
        }

        /// <summary>화면 중앙에 섬 목록 패널(제목, 안내, 섬 행 목록, 상태 메시지)을 만든다.</summary>
        private void BuildListPanel()
        {
            var canvas = UIBuilder.CreateCanvas("MinimapListCanvas", sortOrder: 11);

            var panel = UIBuilder.CreatePanel(
                canvas.transform, "IslandListPanel",
                anchorMin: new Vector2(0.5f, 0.5f), anchorMax: new Vector2(0.5f, 0.5f),
                offsetMin: new Vector2(-260f, -220f), offsetMax: new Vector2(260f, 220f),
                color: new Color(0f, 0f, 0f, 0.85f), addTopBorder: true);

            listPanelRoot = panel.gameObject;

            var title = UIBuilder.CreateText(panel, "Title", "섬 목록 (M)", 20, Color.white, TextAnchor.UpperLeft);
            title.rectTransform.anchorMin = new Vector2(0f, 1f);
            title.rectTransform.anchorMax = new Vector2(1f, 1f);
            title.rectTransform.pivot = new Vector2(0.5f, 1f);
            title.rectTransform.anchoredPosition = new Vector2(0f, -8f);
            title.rectTransform.sizeDelta = new Vector2(0f, 28f);

            statusLabel = UIBuilder.CreateText(panel, "Status", "", 13, new Color(1f, 0.9f, 0.4f, 1f), TextAnchor.LowerLeft);
            statusLabel.rectTransform.anchorMin = new Vector2(0f, 0f);
            statusLabel.rectTransform.anchorMax = new Vector2(1f, 0f);
            statusLabel.rectTransform.pivot = new Vector2(0.5f, 0f);
            statusLabel.rectTransform.anchoredPosition = new Vector2(0f, 6f);
            statusLabel.rectTransform.sizeDelta = new Vector2(-20f, 22f);

            var listGo = new GameObject("IslandList", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            listGo.transform.SetParent(panel, false);
            listContainer = listGo.GetComponent<RectTransform>();
            listContainer.anchorMin = new Vector2(0f, 0f);
            listContainer.anchorMax = new Vector2(1f, 1f);
            listContainer.offsetMin = new Vector2(14f, 32f);
            listContainer.offsetMax = new Vector2(-14f, -40f);

            var vlg = listGo.GetComponent<VerticalLayoutGroup>();
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            vlg.spacing = 4f;
            vlg.childAlignment = TextAnchor.UpperLeft;

            var fitter = listGo.GetComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        }

        /// <summary>
        /// worldMapManager.islands 목록을 기준으로 섬 목록 행들을 갱신한다(부족하면 새로 만들고, 남으면 숨긴다).
        /// 각 행에는 규모/거리/발견 여부와, 발견한 섬으로 이동할 수 있는 버튼이 표시된다.
        /// </summary>
        private void RefreshList()
        {
            if (worldMapManager == null || player == null)
                return;

            var islands = worldMapManager.islands;
            EnsureRowCount(islands.Count);

            for (int i = 0; i < islands.Count; i++)
            {
                var island = islands[i];
                var row = islandRows[i];
                row.rowGo.SetActive(true);
                row.infoLabel.text = BuildIslandInfo(island);
                row.infoLabel.color = island.isDiscovered ? Color.white : new Color(0.6f, 0.6f, 0.6f, 1f);

                // 치명적 버그 수정: island.isDiscovered는 IslandTravel.TryTravelTo가 "도착에 성공한 뒤"에만
                // true로 바뀌는데, 예전 코드는 그 isDiscovered를 이동 버튼의 활성화 조건으로도 썼다.
                // 즉 도착해야 발견되고, 발견돼야 이동 버튼이 눌리는 순환 잠금(soft-lock)이라 시작 섬 밖의
                // 어떤 섬도 영원히 갈 수 없었다. 미발견 섬으로 "처음 항해해서 발견하는 것"이 원래 의도이므로,
                // 이동 가능 여부는 시작 섬 여부로만 판단하고 isDiscovered는 목록 표시(글자 색/상태 문구)에만 쓴다.
                row.travelButton.interactable = !island.isStartingIsland;

                int islandId = island.islandId; // 클로저 캡처용 로컬 변수
                row.travelButton.onClick.RemoveAllListeners();
                row.travelButton.onClick.AddListener(() => TryTravel(islandId));
            }

            for (int i = islands.Count; i < islandRows.Count; i++)
                islandRows[i].rowGo.SetActive(false);

            statusLabel.text = lastTravelStatus;
        }

        /// <summary>섬 행 풀의 개수가 부족하면 필요한 만큼 새로 만든다.</summary>
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

            var infoLabel = UIBuilder.CreateText(rowGo.transform, "Info", "", 14, Color.white, TextAnchor.MiddleLeft);
            infoLabel.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;

            var travelButton = UIBuilder.CreateButton(rowGo.transform, "TravelButton", "이동", null);
            travelButton.gameObject.AddComponent<LayoutElement>().preferredWidth = 70f;

            return new IslandRow { rowGo = rowGo, infoLabel = infoLabel, travelButton = travelButton };
        }

        /// <summary>
        /// 섬 하나의 표시용 정보 문자열("섬 3 - 대형 - 128m - 미발견")을 만든다.
        /// 버그 수정: 예전에는 미발견 섬에도 실제 규모(소/중/대/특대)를 그대로 보여줘서, 방문하지 않고도
        /// 어느 먼 섬이 대형/특대(도면·희귀 재료가 나오는 섬)인지 목록만 보고 미리 알 수 있었다. 이는
        /// "완전히 확정 짓지 않아 약간의 탐험 긴장감을 남긴다"는 배 도면 확률 설계 의도와 어긋나는
        /// 정보 노출이라, 시작 섬이 아니고 아직 발견하지 못한 섬은 규모를 "미확인"으로 가려서
        /// 실제로 배를 타고 가봐야만 규모를 알 수 있게 했다.
        /// </summary>
        private string BuildIslandInfo(IslandInstance island)
        {
            float distance = Vector3.Distance(player.position, island.mapPosition);
            bool revealSize = island.isStartingIsland || island.isDiscovered;
            string sizeText = revealSize ? GetSizeKoreanName(island.size) : "미확인";
            string statusText = island.isStartingIsland ? "시작 섬" : (island.isDiscovered ? "발견함" : "미발견");
            return $"섬 {island.islandId}  -  {sizeText}  -  {distance:F0}m  -  {statusText}";
        }

        /// <summary>IslandSize를 한글 표시명으로 바꾼다.</summary>
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
        /// IslandTravel.TryTravelTo(고무보트 보유/해류 제약 확인)를 실제로 호출하는 유일한 진입점이다.
        /// </summary>
        private void TryTravel(int islandId)
        {
            if (islandTravel == null || inventory == null)
                return;

            bool success = islandTravel.TryTravelTo(islandId, inventory);
            lastTravelStatus = success
                ? $"섬 {islandId}(으)로 이동했습니다."
                : "이동 실패: 고무보트가 필요하거나, 해류가 강해 이 섬까지는 아직 갈 수 없습니다.";
        }

        /// <summary>섬 목록 패널을 열거나 닫는다.</summary>
        private void SetListOpen(bool open)
        {
            if (listPanelRoot != null)
                listPanelRoot.SetActive(open);
        }
    }
}
