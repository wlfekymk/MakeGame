using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using MakeGame.Data;
using MakeGame.Player;
using MakeGame.Systems;

namespace MakeGame.UI
{
    /// <summary>
    /// 화면 상단 중앙의 **띠형 나침반**(N/E/S/W + 도수 + 발견한 섬 방향 표식).
    ///
    /// ── 왜 필요한가 ───────────────────────────────────────────────────────────────
    /// 이 게임에는 방위를 읽을 수단이 없었다. 미니맵은 **북쪽 고정**이라 지도 위 섬의 방향은 알 수
    /// 있어도, 실제로 지금 내가 어느 쪽을 보고 있는지는 중앙의 작은 화살표를 눈대중으로 읽어야 했다.
    /// 뗏목을 타고 바다로 나가면 참조물이 사라져 방향을 잃는다. 띠형 나침반은 그 한 줄을 채운다.
    ///
    /// ── 아이템 요구 여부 판단(요구하지 않는다) ────────────────────────────────────
    /// "Item_나침반이 있어야 보이게" 하는 쪽이 서바이벌 문법에는 더 맞지만, **그 아이템 에셋이
    /// 아직 없고 신규 에셋 제작은 이 작업의 락 밖**이다(ScriptableObjects/Item_*.asset 51종에
    /// 나침반이 없다 - 실측). 없는 아이템을 조건으로 걸면 나침반은 영원히 화면에 뜨지 않는
    /// 죽은 기능이 된다. 그래서 **처음부터 표시**하되, 나중에 아이템 게이트를 붙일 때 지우지 않고
    /// 이어 쓸 수 있도록 두 개의 스위치를 미리 열어 둔다:
    ///   · <see cref="showCompass"/> - 인스펙터/코드에서 끄면 띠가 통째로 숨는다(설정 메뉴 연결용).
    ///   · <see cref="requiredItemName"/> - 비워 두면(기본) 항상 표시, 아이템 이름을 채워 넣으면
    ///     그 아이템을 가방에 들고 있을 때만 표시한다. 즉 Item_나침반 에셋이 생기는 날
    ///     **이 문자열 한 줄만 채우면** 아이템 게이트가 켜진다(코드 변경 0줄).
    ///
    /// ── 배치 근거(기존 HUD와 겹치지 않는다) ───────────────────────────────────────
    /// 화면 상단 중앙 y 0 ~ -30px. 같은 자리를 쓰는 것은 StatusEffectWarningUI의 배너 두 줄
    /// (y -14~-60, -66~-112, 폭 900px)뿐인데, 그 배너는 상태 이상/일몰에만 잠깐 뜨고 **글자는
    /// 46px 띠의 세로 중앙**(대략 y -29 ~ -45)에 있다. 나침반 띠는 -30에서 끝나므로 배너의 위쪽
    /// 여백만 스치고 글자를 가리지 않는다. 좌상단(SurvivalHud)·우상단(미니맵)·하단(상호작용
    /// 프롬프트)과는 아예 영역이 다르다.
    /// 캔버스 sortOrder는 **7** - 생존 HUD(5)/상태이상 배너(6)보다 위, 건축 메뉴(8)·미니맵(9)·
    /// 모달 창(10~11)·피격 플래시(12)·툴팁(13)·설정(16)·사망(20)·엔딩(21)보다 아래다.
    /// 7은 기존 어느 캔버스도 쓰지 않던 빈 층이다(전수 확인).
    ///
    /// ── 그리는 방식(프레임당 할당 0) ──────────────────────────────────────────────
    /// OnGUI(IMGUI)는 절대 쓰지 않는다 - sortingOrder를 무시하고 다른 화면을 통째로 덮는다
    /// (이 프로젝트의 실제 사고 - InteractionPromptUI 상단 주석).
    /// 눈금 24개(15°마다) + 방위 글자 8개는 **Start에서 한 번만** 만들고, 매 프레임 하는 일은
    /// 각 조각의 anchoredPosition을 Mathf.DeltaAngle(시선, 눈금각) × 픽셀/도로 다시 찍는 것뿐이다
    /// (구조체 대입 32번 - 힙 할당 0, 문자열 조립 0, LINQ 0). 띠를 3벌 복제해 밀어내는 흔한 방식
    /// 대신 DeltaAngle을 쓰므로 오브젝트가 1/3이고 경계에서 튀지 않는다.
    /// 도수 문자열은 0~359를 **미리 구운 static string[360]**에서 꺼내므로 회전 중에도 새 문자열이
    /// 생기지 않고, 그마저도 정수 도수가 실제로 바뀐 프레임에만 대입한다.
    /// 섬 표식은 최대 6개를 풀링하고, 대상 선정(가까운 순)은 0.5초에 한 번 **미리 잡아 둔 배열
    /// 안에서만** 갱신한다(정렬·리스트 생성 없음).
    /// </summary>
    public class CompassUI : MonoBehaviour
    {
        // ────────────────────────────────────────────────────────────────────────
        // 설정
        // ────────────────────────────────────────────────────────────────────────

        [Tooltip("나침반 띠를 표시할지. 끄면 띠가 통째로 숨는다(설정 메뉴에서 끌 수 있게 남겨 둔 스위치).")]
        public bool showCompass = true;

        [Tooltip("나침반을 보려면 가방에 있어야 하는 아이템 이름. **비워 두면 항상 표시**한다.\n" +
            "Item_나침반 에셋이 생기면 여기에 \"나침반\"만 넣으면 아이템 게이트가 켜진다(코드 변경 없음).")]
        public string requiredItemName = "";

        [Tooltip("나침반 띠에 방향 표식을 찍을 섬의 최대 거리(m). 이보다 먼 섬은 표식을 찍지 않는다.")]
        public float islandMarkerRange = 6000f;

        // ────────────────────────────────────────────────────────────────────────
        // 치수 (전부 1920×1080 기준 px - UIBuilder.CreateCanvas의 referenceResolution)
        // ────────────────────────────────────────────────────────────────────────

        /// <summary>띠 전체 폭.</summary>
        private const float StripWidth = 480f;

        /// <summary>띠 높이. "얇은 띠"이면서 12~13pt 글자가 들어가는 최소값이다.</summary>
        private const float StripHeight = 30f;

        /// <summary>띠 좌우 안쪽 여백(마스크 영역이 이만큼 좁다).</summary>
        private const float StripPadding = 5f;

        /// <summary>띠에 한 번에 보이는 방위각 폭(도). 120°면 정면 기준 좌우 60°씩 - 인접 방위
        /// 글자(45° 간격)가 항상 최소 하나는 함께 보여 "지금 어디쯤"이 읽힌다.</summary>
        private const float VisibleSpanDegrees = 120f;

        /// <summary>눈금 간격(도). 15°마다 하나, 그중 45°의 배수는 긴 눈금 + 방위 글자다.</summary>
        private const int TickStepDegrees = 15;

        /// <summary>눈금 개수(360/15).</summary>
        private const int TickCount = 360 / TickStepDegrees;

        /// <summary>방위 글자 수(N/NE/E/SE/S/SW/W/NW).</summary>
        private const int CardinalCount = 8;

        /// <summary>띠에 동시에 찍을 수 있는 섬 표식 수(가까운 순).</summary>
        private const int MaxIslandPips = 6;

        /// <summary>섬 표식 대상을 다시 고르는 간격(초). 매 프레임 거리 정렬을 하지 않기 위함.</summary>
        private const float PipRetargetInterval = 0.5f;

        /// <summary>플레이어/월드맵 참조를 다시 찾아보는 간격(초). 정상 경로에서는 한 번이면 끝난다.</summary>
        private const float ReferenceProbeInterval = 1f;

        // ────────────────────────────────────────────────────────────────────────
        // 색 (ArtDirection 팔레트 안에서만 - 새 색을 만들지 않는다)
        // ────────────────────────────────────────────────────────────────────────

        /// <summary>띠 바닥. 미니맵 바닥(MinimapUI.MinimapBackground)과 같은 값이라 상시 HUD 두 개의
        /// 바탕 밝기가 정확히 일치한다.</summary>
        private static readonly Color StripBackground = new Color(0.018f, 0.030f, 0.040f, 0.55f);

        /// <summary>Island Sand #C2B280 - 방위 글자/눈금의 기본색(지도의 "육지"와 같은 색 계열).</summary>
        private static readonly Color TickColor = new Color(0.761f, 0.698f, 0.502f, 0.75f);

        /// <summary>주 방위(N/E/S/W)는 더 밝게. 부 방위(NE/SE/SW/NW)는 TickColor 그대로다.</summary>
        private static readonly Color MajorCardinalColor = new Color(0.95f, 0.92f, 0.86f, 1f);

        /// <summary>정면 지시선. 미니맵의 "현재 섬" 링과 같은 따뜻한 흰색이다.</summary>
        private static readonly Color IndexColor = new Color(1f, 1f, 1f, 0.95f);

        /// <summary>섬 표식 기본색(발견한 섬). 지도의 "탐사한 육지"와 같은 값.</summary>
        private static readonly Color IslandPipColor = new Color(0.548f, 0.503f, 0.361f, 1f);

        /// <summary>지금 서 있는 섬의 표식색(지도의 "현재 섬" 육지색).</summary>
        private static readonly Color CurrentIslandPipColor = new Color(0.761f, 0.698f, 0.502f, 1f);

        // ────────────────────────────────────────────────────────────────────────
        // 상태
        // ────────────────────────────────────────────────────────────────────────

        /// <summary>도수 라벨 문자열 0~359를 미리 구워 둔 표. 회전 중에도 새 문자열이 생기지 않는다.</summary>
        private static string[] degreeStrings;

        private WorldMapManager worldMapManager;
        private Transform player;
        private PlayerInventory inventory;
        private float referenceProbeTimer;

        private GameObject stripRoot;
        private RectTransform maskArea;
        private Text headingLabel;

        private readonly RectTransform[] tickRects = new RectTransform[TickCount];
        private readonly RectTransform[] cardinalRects = new RectTransform[CardinalCount];
        private readonly RectTransform[] pipRects = new RectTransform[MaxIslandPips];
        private readonly Image[] pipImages = new Image[MaxIslandPips];

        /// <summary>표식을 찍을 섬의 월드 XZ 위치와 종류. 0.5초마다만 다시 고른다(할당 없음).</summary>
        private readonly Vector3[] pipPositions = new Vector3[MaxIslandPips];
        private readonly int[] pipIslandIds = new int[MaxIslandPips];
        private int pipActiveCount;
        private float pipRetargetTimer;

        private float pixelsPerDegree = 4f;
        private float halfSpanPixels = 240f;
        private float lastHeading = float.NaN;
        private int lastShownDegree = -1;
        private bool lastVisible = true;

        /// <summary>방위 글자. 인덱스 = 45° 단위(0=N, 1=NE, ... 7=NW).</summary>
        private static readonly string[] CardinalLabels = { "N", "NE", "E", "SE", "S", "SW", "W", "NW" };

        // ────────────────────────────────────────────────────────────────────────
        // 부트스트랩 (씬 파일을 고칠 수 없으므로 스스로 생긴다 - 프로젝트에 16곳 선례)
        // ────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// 씬이 로드될 때마다 스스로 생성된다(RaftBuildUI·ChestUI·QuestUI와 같은 자기 완결 패턴).
        /// 중복 생성 방지 가드가 있다 - 나침반이 두 개면 같은 자리에 띠가 겹쳐 글자가 두 겹으로 보인다.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Bootstrap()
        {
            SceneManager.sceneLoaded += (scene, mode) =>
            {
                if (FindAnyObjectByType<CompassUI>() != null)
                    return;

                var go = new GameObject("CompassUI");
                go.AddComponent<CompassUI>();
            };
        }

        // ────────────────────────────────────────────────────────────────────────
        // 수명 주기
        // ────────────────────────────────────────────────────────────────────────

        private void Start()
        {
            EnsureDegreeStrings();
            AcquireReferences();
            BuildUI();
        }

        private void Update()
        {
            // 참조는 못 찾았을 때만, 1초에 한 번 다시 찾는다(정상 경로에서 탐색 비용 0).
            if (player == null || worldMapManager == null || inventory == null)
            {
                referenceProbeTimer -= Time.unscaledDeltaTime;
                if (referenceProbeTimer <= 0f)
                {
                    referenceProbeTimer = ReferenceProbeInterval;
                    AcquireReferences();
                }
            }

            bool visible = showCompass && player != null && HasRequiredItem();
            if (visible != lastVisible)
            {
                lastVisible = visible;
                if (stripRoot != null)
                    stripRoot.SetActive(visible);
            }

            if (!visible)
                return;

            pipRetargetTimer -= Time.deltaTime;
            if (pipRetargetTimer <= 0f)
            {
                pipRetargetTimer = PipRetargetInterval;
                RetargetIslandPips();
            }

            RefreshStrip();
        }

        /// <summary>
        /// 씬에서 필요한 참조(플레이어 트랜스폼 · 월드맵 · 인벤토리)를 찾는다.
        /// 방위의 기준은 **플레이어 트랜스폼의 yaw**다 - 미니맵의 플레이어 화살표
        /// (MinimapUI: player.eulerAngles.y)와 같은 값을 써야 두 HUD가 같은 방향을 가리킨다.
        /// </summary>
        private void AcquireReferences()
        {
            if (player == null)
            {
                // 미니맵이 이미 들고 있는 플레이어 트랜스폼을 **최우선으로** 재사용한다. 두 HUD가
                // 서로 다른 트랜스폼의 yaw를 읽으면(예: 몸통 vs 카메라 피벗) 미니맵 화살표와
                // 나침반이 다른 방향을 가리키는, 가장 알아채기 어려운 종류의 버그가 된다.
                var minimap = FindAnyObjectByType<MinimapUI>();
                if (minimap != null && minimap.player != null)
                    player = minimap.player;
            }

            if (player == null)
            {
                var stats = FindAnyObjectByType<SurvivalStats>();
                if (stats != null)
                    player = stats.transform;
            }

            if (worldMapManager == null)
                worldMapManager = FindAnyObjectByType<WorldMapManager>();

            if (inventory == null)
                inventory = FindAnyObjectByType<PlayerInventory>();
        }

        /// <summary>
        /// 표시 조건: requiredItemName이 비어 있으면 항상 true(현재 기본값 - 클래스 주석의 판단 근거).
        /// 이름이 채워져 있으면 그 아이템을 가방에 들고 있을 때만 true다.
        ///
        /// PlayerInventory에는 **이름으로 개수를 세는 API가 없어**(GetItemCount는 ItemData를 받는다)
        /// 공개 items 목록을 직접 훑는다. 기본값이 빈 문자열이라 평소에는 첫 줄에서 바로 빠져나가고,
        /// 게이트를 켜도 목록 순회 한 번(할당 0)이라 프레임 비용이 사실상 없다.
        /// </summary>
        private bool HasRequiredItem()
        {
            if (string.IsNullOrEmpty(requiredItemName))
                return true;
            if (inventory == null || inventory.items == null)
                return false;

            for (int i = 0; i < inventory.items.Count; i++)
            {
                InventoryItem entry = inventory.items[i];
                if (entry != null && entry.data != null && entry.data.itemName == requiredItemName)
                    return true;
            }
            return false;
        }

        // ────────────────────────────────────────────────────────────────────────
        // 조립 (Start에서 한 번만)
        // ────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// 띠 · 마스크 · 눈금 24개 · 방위 글자 8개 · 정면 지시선 · 도수 라벨 · 섬 표식 6개를
        /// 한 번에 만든다. 이후 매 프레임 하는 일은 위치/글자 대입뿐이라 오브젝트가 늘거나 줄지 않는다.
        /// </summary>
        private void BuildUI()
        {
            var canvas = UIBuilder.CreateCanvas("CompassCanvas", sortOrder: 7);

            var strip = UIBuilder.CreatePanel(
                canvas.transform, "CompassStrip",
                anchorMin: new Vector2(0.5f, 1f), anchorMax: new Vector2(0.5f, 1f),
                offsetMin: new Vector2(-StripWidth * 0.5f, -StripHeight),
                offsetMax: new Vector2(StripWidth * 0.5f, 0f),
                color: StripBackground);

            stripRoot = strip.gameObject;

            // 마스크 영역: 띠 밖으로 나가는 눈금/글자를 잘라낸다(미니맵 레이더와 같은 RectMask2D 방식).
            var maskGo = new GameObject("CompassMask", typeof(RectTransform), typeof(RectMask2D));
            maskGo.transform.SetParent(strip, false);
            maskArea = maskGo.GetComponent<RectTransform>();
            maskArea.anchorMin = Vector2.zero;
            maskArea.anchorMax = Vector2.one;
            maskArea.offsetMin = new Vector2(StripPadding, 0f);
            maskArea.offsetMax = new Vector2(-StripPadding, 0f);

            float usableWidth = StripWidth - StripPadding * 2f;
            pixelsPerDegree = usableWidth / VisibleSpanDegrees;
            halfSpanPixels = usableWidth * 0.5f;

            BuildTicks();
            BuildCardinals();
            BuildIslandPips();
            BuildIndexAndHeading(strip);

            stripRoot.SetActive(showCompass);
            lastVisible = showCompass;
        }

        /// <summary>15°마다 짧은 눈금 하나. 45°의 배수 자리는 방위 글자가 대신 서므로 눈금을 짧게 둔다.</summary>
        private void BuildTicks()
        {
            for (int i = 0; i < TickCount; i++)
            {
                int degree = i * TickStepDegrees;
                bool major = degree % 45 == 0;

                var rt = UIBuilder.CreatePanel(
                    maskArea, "Tick_" + degree,
                    anchorMin: new Vector2(0.5f, 1f), anchorMax: new Vector2(0.5f, 1f),
                    offsetMin: Vector2.zero, offsetMax: Vector2.zero,
                    color: major ? MajorCardinalColor : TickColor);

                rt.pivot = new Vector2(0.5f, 1f);
                rt.sizeDelta = new Vector2(major ? 2f : 1f, major ? 8f : 5f);
                rt.anchoredPosition = new Vector2(0f, -3f);
                tickRects[i] = rt;
            }
        }

        /// <summary>45°마다 방위 글자 하나(N/NE/E/SE/S/SW/W/NW). 주 방위는 굵고 밝게 쓴다.</summary>
        private void BuildCardinals()
        {
            for (int i = 0; i < CardinalCount; i++)
            {
                bool major = i % 2 == 0; // N/E/S/W
                var text = UIBuilder.CreateText(maskArea, "Cardinal_" + CardinalLabels[i],
                    CardinalLabels[i], major ? 14 : 11,
                    major ? MajorCardinalColor : TickColor, TextAnchor.MiddleCenter);
                text.fontStyle = major ? FontStyle.Bold : FontStyle.Normal;
                text.horizontalOverflow = HorizontalWrapMode.Overflow;

                var rt = text.rectTransform;
                rt.anchorMin = new Vector2(0.5f, 0f);
                rt.anchorMax = new Vector2(0.5f, 0f);
                rt.pivot = new Vector2(0.5f, 0f);
                rt.sizeDelta = new Vector2(40f, 18f);
                rt.anchoredPosition = new Vector2(0f, 1f);
                cardinalRects[i] = rt;
            }
        }

        /// <summary>
        /// 발견한 섬의 방향 표식(작은 마름모 대신 3×7 세로 막대 - 눈금과 구분되게 아래쪽 줄에 선다).
        /// 6개를 풀링하고, 남는 것은 비활성으로 둔다(매번 만들고 지우지 않는다).
        /// </summary>
        private void BuildIslandPips()
        {
            for (int i = 0; i < MaxIslandPips; i++)
            {
                var rt = UIBuilder.CreatePanel(
                    maskArea, "IslandPip_" + i,
                    anchorMin: new Vector2(0.5f, 1f), anchorMax: new Vector2(0.5f, 1f),
                    offsetMin: Vector2.zero, offsetMax: Vector2.zero,
                    color: IslandPipColor);

                rt.pivot = new Vector2(0.5f, 1f);
                rt.sizeDelta = new Vector2(3f, 7f);
                rt.anchoredPosition = new Vector2(0f, -2f);

                pipRects[i] = rt;
                pipImages[i] = rt.GetComponent<Image>();
                rt.gameObject.SetActive(false);
            }
        }

        /// <summary>
        /// 정면 지시선(띠 세로 전체를 가로지르는 얇은 선)과 그 위의 도수 라벨.
        /// 지시선/라벨은 마스크 밖(띠 직계 자식)이라 절대 잘리지 않는다.
        /// </summary>
        private void BuildIndexAndHeading(RectTransform strip)
        {
            var index = UIBuilder.CreatePanel(
                strip, "IndexLine",
                anchorMin: new Vector2(0.5f, 0f), anchorMax: new Vector2(0.5f, 1f),
                offsetMin: Vector2.zero, offsetMax: Vector2.zero,
                color: IndexColor);
            index.sizeDelta = new Vector2(2f, 0f);
            index.anchoredPosition = Vector2.zero;

            // 도수는 지시선 바로 위(띠 안 상단)에 얹는다 - 띠 밖으로 한 줄을 더 내면 상태이상 배너
            // 글자와 정면으로 겹친다(클래스 주석의 배치 근거). 눈금이 숫자 뒤로 지나가면 읽기가
            // 나빠지므로 숫자 자리만 한 겹 어둡게 깐다(마스크 밖이라 눈금 위에 그려진다).
            var plate = UIBuilder.CreatePanel(
                strip, "HeadingPlate",
                anchorMin: new Vector2(0.5f, 1f), anchorMax: new Vector2(0.5f, 1f),
                offsetMin: new Vector2(-24f, -16f), offsetMax: new Vector2(24f, 0f),
                color: new Color(0.018f, 0.030f, 0.040f, 0.85f));
            plate.gameObject.name = "HeadingPlate";

            headingLabel = UIBuilder.CreateText(strip, "Heading", "000", 12, IndexColor, TextAnchor.UpperCenter);
            headingLabel.fontStyle = FontStyle.Bold;
            headingLabel.horizontalOverflow = HorizontalWrapMode.Overflow;

            var rt = headingLabel.rectTransform;
            rt.anchorMin = new Vector2(0.5f, 1f);
            rt.anchorMax = new Vector2(0.5f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.sizeDelta = new Vector2(50f, 14f);
            rt.anchoredPosition = new Vector2(0f, -1f);

            var shadow = headingLabel.gameObject.AddComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0f, 0f, 0.8f);
            shadow.effectDistance = new Vector2(1f, -1f);
        }

        // ────────────────────────────────────────────────────────────────────────
        // 갱신 (프레임당 할당 0)
        // ────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// 시선 방위로 눈금·글자·섬 표식의 x를 다시 찍는다. 각 조각의 x는
        /// Mathf.DeltaAngle(시선, 그 조각의 절대 방위) × 픽셀/도라, 띠를 복제해 밀어내지 않아도
        /// 경계(359°→0°)에서 자연스럽게 이어진다. 시선이 사실상 안 움직인 프레임은 통째로 건너뛴다.
        /// </summary>
        private void RefreshStrip()
        {
            float heading = player.eulerAngles.y;
            if (!float.IsNaN(lastHeading) && Mathf.Abs(Mathf.DeltaAngle(lastHeading, heading)) < 0.05f)
            {
                // 시선이 안 변해도 섬 표식은 **플레이어가 이동**하면 각이 변하므로 그쪽만 갱신한다.
                RefreshIslandPips(heading);
                return;
            }
            lastHeading = heading;

            for (int i = 0; i < TickCount; i++)
            {
                RectTransform rt = tickRects[i];
                if (rt == null)
                    continue;
                float delta = Mathf.DeltaAngle(heading, i * TickStepDegrees);
                rt.anchoredPosition = new Vector2(delta * pixelsPerDegree, -3f);
            }

            for (int i = 0; i < CardinalCount; i++)
            {
                RectTransform rt = cardinalRects[i];
                if (rt == null)
                    continue;
                float delta = Mathf.DeltaAngle(heading, i * 45f);
                rt.anchoredPosition = new Vector2(delta * pixelsPerDegree, 1f);
            }

            RefreshIslandPips(heading);

            // 도수 라벨: 정수 도수가 실제로 바뀐 프레임에만, 그것도 미리 구운 문자열 표에서 꺼내 대입한다.
            int degree = Mathf.RoundToInt(heading);
            degree -= Mathf.FloorToInt(degree / 360f) * 360;
            if (degree < 0)
                degree += 360;
            if (degree != lastShownDegree && headingLabel != null)
            {
                lastShownDegree = degree;
                headingLabel.text = degreeStrings[degree];
            }
        }

        /// <summary>섬 표식 6개의 x를 다시 찍는다(띠 밖으로 나간 것은 비활성).</summary>
        private void RefreshIslandPips(float heading)
        {
            if (player == null)
                return;

            Vector3 origin = player.position;
            for (int i = 0; i < MaxIslandPips; i++)
            {
                RectTransform rt = pipRects[i];
                if (rt == null)
                    continue;

                if (i >= pipActiveCount)
                {
                    if (rt.gameObject.activeSelf)
                        rt.gameObject.SetActive(false);
                    continue;
                }

                float dx = pipPositions[i].x - origin.x;
                float dz = pipPositions[i].z - origin.z;
                float bearing = Mathf.Atan2(dx, dz) * Mathf.Rad2Deg;
                float x = Mathf.DeltaAngle(heading, bearing) * pixelsPerDegree;

                bool inside = Mathf.Abs(x) <= halfSpanPixels;
                if (rt.gameObject.activeSelf != inside)
                    rt.gameObject.SetActive(inside);
                if (inside)
                    rt.anchoredPosition = new Vector2(x, -2f);
            }
        }

        /// <summary>
        /// 표식을 찍을 섬을 다시 고른다(0.5초에 한 번). **발견한 섬만** 대상이다 - 미발견 섬의
        /// 방향까지 알려주면 탐험이 사라진다(미니맵의 IsRevealed와 같은 규약: isDiscovered 또는
        /// 시작 섬). 가까운 순 상위 6개를 미리 잡아 둔 배열 안에서 삽입 정렬로 고르므로
        /// 리스트 생성·정렬 할당이 0이다.
        /// </summary>
        private void RetargetIslandPips()
        {
            pipActiveCount = 0;
            if (worldMapManager == null || worldMapManager.islands == null || player == null)
                return;

            Vector3 origin = player.position;
            float rangeSq = islandMarkerRange * islandMarkerRange;
            var islands = worldMapManager.islands;

            for (int i = 0; i < islands.Count; i++)
            {
                IslandInstance island = islands[i];
                if (island == null || !(island.isDiscovered || island.isStartingIsland))
                    continue;

                float dx = island.mapPosition.x - origin.x;
                float dz = island.mapPosition.z - origin.z;
                float distSq = dx * dx + dz * dz;
                if (distSq > rangeSq)
                    continue;

                InsertPip(island, distSq);
            }

            ApplyPipColors();
        }

        /// <summary>가까운 순 정렬을 유지하며 표식 후보를 끼워 넣는다(고정 배열 안에서만 - 할당 0).</summary>
        private void InsertPip(IslandInstance island, float distSq)
        {
            int slot = pipActiveCount;
            for (int i = 0; i < pipActiveCount; i++)
            {
                if (distSq < pipDistances[i])
                {
                    slot = i;
                    break;
                }
            }

            if (slot >= MaxIslandPips)
                return;

            int last = Mathf.Min(pipActiveCount, MaxIslandPips - 1);
            for (int i = last; i > slot; i--)
            {
                pipDistances[i] = pipDistances[i - 1];
                pipPositions[i] = pipPositions[i - 1];
                pipIslandIds[i] = pipIslandIds[i - 1];
            }

            pipDistances[slot] = distSq;
            pipPositions[slot] = island.mapPosition;
            pipIslandIds[slot] = island.islandId;
            if (pipActiveCount < MaxIslandPips)
                pipActiveCount++;
        }

        /// <summary>가까운 순 거리(제곱). InsertPip 전용 - 매 프레임 쓰이지 않는다.</summary>
        private readonly float[] pipDistances = new float[MaxIslandPips];

        /// <summary>
        /// 표식 색을 다시 칠한다(대상이 바뀐 0.5초 주기에만 - 프레임당 비용 0).
        /// [카토그래피 연동] 전체 지도([M])에서 그 섬에 남긴 표식이 있으면 **지도와 같은 색**으로
        /// 칠한다(MinimapUI.GetIslandMark - 색 정의도 그쪽 한 곳뿐이라 두 화면이 어긋날 수 없다).
        /// 표식이 없으면 가장 가까운 섬만 밝게, 나머지는 "탐사한 육지"색이다.
        /// </summary>
        private void ApplyPipColors()
        {
            for (int i = 0; i < pipActiveCount; i++)
            {
                Image image = pipImages[i];
                if (image == null)
                    continue;

                MinimapUI.IslandMark mark = MinimapUI.GetIslandMark(pipIslandIds[i]);
                if (mark != MinimapUI.IslandMark.None)
                    image.color = MinimapUI.GetIslandMarkColor(mark);
                else
                    image.color = i == 0 ? CurrentIslandPipColor : IslandPipColor;
            }
        }

        /// <summary>
        /// 도수 문자열 0~359를 한 번만 구워 둔다. 필드 초기화식이 아니라 Start에서 채운다
        /// (필드 초기화식 금지 규칙과 통일 - 여기서는 Unity API를 부르지 않지만 규칙을 맞춘다).
        /// </summary>
        private static void EnsureDegreeStrings()
        {
            if (degreeStrings != null)
                return;

            degreeStrings = new string[360];
            for (int i = 0; i < 360; i++)
                degreeStrings[i] = i.ToString("000") + "°";
        }

        /// <summary>
        /// 도메인 리로드를 끈 플레이 모드에서 static 캐시가 이전 실행의 값을 들고 시작하지 않게
        /// 초기 상태로 되돌린다(R1 규칙). 문자열 표는 순수 값이라 남아도 무해하지만 규칙을 통일한다.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticCache()
        {
            degreeStrings = null;
        }
    }
}
