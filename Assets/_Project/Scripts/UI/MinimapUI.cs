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
        [Tooltip("레이더에 표시할 실제 월드 반경(미터). 이보다 먼 섬은 가장자리에 붙어서 표시된다.")]
        public float radarWorldRadius = 400f;

        [Tooltip("레이더 패널의 한 변 크기(픽셀)")]
        public float radarPanelSize = 160f;

        /// <summary>섬 하나에 대응하는 레이더 위 점(dot) UI.</summary>
        private class RadarDot
        {
            public RectTransform rt;
            public Image image;
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

        private GameObject listPanelRoot;
        private RectTransform listContainer;
        private Text statusLabel;
        private readonly List<IslandRow> islandRows = new List<IslandRow>();

        private string lastTravelStatus = "";

        /// <summary>시작 시 레이더와 섬 목록 패널을 만들고, 목록 패널은 기본적으로 닫아 둔다.</summary>
        private void Start()
        {
            BuildRadar();
            BuildListPanel();
            SetListOpen(false);
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
                color: new Color(0f, 0f, 0f, 0.5f));

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

            // 레이더 중심의 플레이어 표시(고정 점).
            var playerDot = UIBuilder.CreateIcon(radarDotsLayer, "PlayerDot", 8f, Color.white, "");
            playerDot.anchorMin = new Vector2(0.5f, 0.5f);
            playerDot.anchorMax = new Vector2(0.5f, 0.5f);
            playerDot.anchoredPosition = Vector2.zero;
        }

        /// <summary>
        /// 레이더에 표시할 섬 점(dot)들의 위치와 색을 갱신한다.
        /// 플레이어 기준 상대 좌표를 레이더 반경 비율로 축소해 배치하고, 범위를 벗어나면 가장자리에 붙인다.
        /// </summary>
        private void RefreshRadar()
        {
            if (worldMapManager == null || player == null || radarDotsLayer == null)
                return;

            var islands = worldMapManager.islands;
            EnsureRadarDotCount(islands.Count);

            float scale = (radarPanelSize / 2f) / radarWorldRadius;
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
            }

            for (int i = islands.Count; i < radarDotPool.Count; i++)
                radarDotPool[i].rt.gameObject.SetActive(false);
        }

        /// <summary>레이더 점 풀의 개수가 부족하면 필요한 만큼 새로 만든다.</summary>
        private void EnsureRadarDotCount(int count)
        {
            while (radarDotPool.Count < count)
            {
                var iconRt = UIBuilder.CreateIcon(radarDotsLayer, $"Dot{radarDotPool.Count}", 6f, Color.gray, "");
                iconRt.anchorMin = new Vector2(0.5f, 0.5f);
                iconRt.anchorMax = new Vector2(0.5f, 0.5f);
                radarDotPool.Add(new RadarDot { rt = iconRt, image = iconRt.GetComponent<Image>() });
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
                color: new Color(0f, 0f, 0f, 0.85f));

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
