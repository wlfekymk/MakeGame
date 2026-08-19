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
    public partial class MinimapUI : MonoBehaviour
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

        /// <summary>미니맵 바닥. 상시 표시 HUD라 알파 0.55(ArtDirection 4.3).</summary>
        private static readonly Color MinimapBackground = new Color(0.018f, 0.030f, 0.040f, 0.55f);

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

        // ────────────────────────────────────────────────────────────────────────
        // 카토그래피 (지도 표식)
        // ────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// 섬에 남길 수 있는 표식. 값은 **세이브 파일에 정수로 그대로 들어가므로**(IslandMarkSaveEntry)
        /// 기존 값의 숫자를 바꾸거나 중간에 삽입하면 옛 세이브의 표식이 다른 뜻으로 바뀐다.
        /// 새 표식은 반드시 **맨 뒤에만** 추가한다(BossKind와 같은 규칙).
        /// </summary>
        public enum IslandMark
        {
            None = 0,
            Depleted = 1,      // 고갈됨 - 다 캤으니 다시 오지 않아도 된다
            HasResources = 2,  // 자원 있음 - 남은 것이 있다
            Danger = 3,        // 위험 - 상어/보스/맹수
        }

        /// <summary>표식 순환 순서(버튼을 누를 때마다 다음 값). 마지막에서 다시 None으로 돌아온다.</summary>
        private const int IslandMarkCount = 4;

        /// <summary>
        /// 섬별 표식 저장소. **static인 이유**: 섬(IslandInstance)은 RegenerateWorld마다 통째로
        /// 새로 만들어지므로 거기에 필드를 달면 불러오기 한 번에 표식이 전부 날아간다
        /// (isDiscovered를 SaveData.discoveredIslandIds로 따로 들고 있는 것과 완전히 같은 이유).
        /// islandId를 키로 UI 계층에 들고 있다가 SaveLoadController가 저장/복원한다.
        /// </summary>
        private static readonly Dictionary<int, int> islandMarkStore = new Dictionary<int, int>();

        /// <summary>
        /// 표식 저장소가 어느 월드의 것인지. 한 플레이 세션 안에서 새 게임/불러오기로 worldSeed가
        /// 바뀌면(= 완전히 다른 섬 배치) 옛 표식이 엉뚱한 섬에 붙으므로 통째로 비운다.
        /// static은 씬을 다시 로드해도 살아남기 때문에 이 가드가 없으면 표식이 세계를 넘어 샌다.
        /// </summary>
        private static int markStoreWorldSeed;
        private static bool markStoreSeedKnown;

        /// <summary>
        /// 도메인 리로드를 끈 플레이 모드에서 static 표식 저장소가 이전 실행의 값을 들고 시작하지
        /// 않게 초기 상태로 되돌린다(R1 규칙).
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticCache()
        {
            islandMarkStore.Clear();
            markStoreWorldSeed = 0;
            markStoreSeedKnown = false;
            spritesLoaded = false;
            dotSprite = null;
            ringSprite = null;
            arrowSprite = null;
            hasSavedMapPosition = false;
            savedMapPosition = Vector2.zero;
        }

        /// <summary>
        /// 표식 저장소가 지금 월드의 것인지 확인하고, 다른 월드면 비운다. 매 프레임 호출되지만
        /// 정상 경로에서는 int 비교 한 번이라 비용이 없다(할당 0).
        /// </summary>
        public static void SyncMarkWorld(int worldSeed)
        {
            if (markStoreSeedKnown && markStoreWorldSeed == worldSeed)
                return;

            if (markStoreSeedKnown)
                islandMarkStore.Clear(); // 다른 월드로 갈아탔다 - 옛 표식은 의미가 없다

            markStoreWorldSeed = worldSeed;
            markStoreSeedKnown = true;
        }

        /// <summary>섬에 남긴 표식(없으면 None). 나침반 띠(CompassUI)도 이것을 읽어 표식 색을 맞춘다.</summary>
        public static IslandMark GetIslandMark(int islandId)
        {
            if (islandMarkStore.TryGetValue(islandId, out int value)
                && value > 0 && value < IslandMarkCount)
                return (IslandMark)value;
            return IslandMark.None;
        }

        /// <summary>표식을 지정한다. None이면 항목을 아예 지운다(세이브에도 실리지 않는다).</summary>
        public static void SetIslandMark(int islandId, IslandMark mark)
        {
            if (mark == IslandMark.None)
                islandMarkStore.Remove(islandId);
            else
                islandMarkStore[islandId] = (int)mark;
        }

        /// <summary>표식을 다음 값으로 돌린다(없음 → 고갈됨 → 자원 있음 → 위험 → 없음).</summary>
        public static IslandMark CycleIslandMark(int islandId)
        {
            var next = (IslandMark)(((int)GetIslandMark(islandId) + 1) % IslandMarkCount);
            SetIslandMark(islandId, next);
            return next;
        }

        /// <summary>
        /// 저장용: 표식이 있는 섬만 목록에 싣는다(SaveLoadController.Save가 부른다).
        /// 넘어온 목록은 먼저 비운다 - 두 번 저장해도 항목이 겹쳐 쌓이지 않는다.
        /// </summary>
        public static void WriteMarksTo(List<IslandMarkSaveEntry> target)
        {
            if (target == null)
                return;

            target.Clear();
            foreach (var pair in islandMarkStore)
            {
                if (pair.Value <= 0 || pair.Value >= IslandMarkCount)
                    continue;
                target.Add(new IslandMarkSaveEntry { islandId = pair.Key, mark = pair.Value });
            }
        }

        /// <summary>
        /// 복원용: 저장된 표식으로 저장소를 통째로 교체한다(SaveLoadController.Load가 부른다).
        /// **RegenerateWorld 뒤에 부른다** - 그래야 여기서 세운 worldSeed가 실제로 만들어진 월드와
        /// 일치한다. 목록이 null이거나 비어 있으면(표식 필드가 없는 옛 세이브) 그냥 빈 상태가 되고,
        /// 그것이 정확히 "그 세이브에는 표식이 없었다"는 뜻이라 마이그레이션이 필요 없다.
        /// 모르는 값(미래 버전 파일)은 조용히 버린다.
        /// </summary>
        public static void ReadMarksFrom(List<IslandMarkSaveEntry> source, int worldSeed)
        {
            islandMarkStore.Clear();
            markStoreWorldSeed = worldSeed;
            markStoreSeedKnown = true;

            if (source == null)
                return;

            for (int i = 0; i < source.Count; i++)
            {
                var entry = source[i];
                if (entry == null || entry.mark <= 0 || entry.mark >= IslandMarkCount)
                    continue;
                islandMarkStore[entry.islandId] = entry.mark;
            }
        }

        /// <summary>표식의 짧은 한글 이름(목록 버튼용). None은 버튼의 기본 문구다.</summary>
        private static string GetMarkButtonLabel(IslandMark mark)
        {
            switch (mark)
            {
                case IslandMark.Depleted: return "고갈";
                case IslandMark.HasResources: return "자원";
                case IslandMark.Danger: return "위험";
                default: return "표식";
            }
        }

        /// <summary>정보 줄에 덧붙일 표식 꼬리표. 표식이 없으면 빈 문자열이다.</summary>
        private static string GetMarkTag(IslandMark mark)
        {
            switch (mark)
            {
                case IslandMark.Depleted: return "  ·  [고갈됨]";
                case IslandMark.HasResources: return "  ·  [자원 있음]";
                case IslandMark.Danger: return "  ·  [위험]";
                default: return "";
            }
        }

        /// <summary>
        /// 표식 색. 새 색을 만들지 않는다(ArtDirection 1장) - 이미 이 파일이 쓰고 있는 세 색이다.
        /// 고갈됨 = NeutralGray(다 끝난 것), 자원 있음 = 안내 금색(MinimapUI.statusLabel과 같은 값),
        /// 위험 = DangerRed. **선택 표시(MedicGreen)와 겹치지 않는 세 색**이라 링 하나로 둘을
        /// 동시에 구분할 수 있다(선택이 표식보다 우선).
        /// </summary>
        public static Color GetIslandMarkColor(IslandMark mark)
        {
            switch (mark)
            {
                case IslandMark.Depleted: return NeutralGray;
                case IslandMark.HasResources: return new Color(1f, 0.9f, 0.4f, 1f);
                case IslandMark.Danger: return DangerRed;
                default: return NeutralGray;
            }
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
        private float mapRefreshTimer = 0f;

        /// <summary>창을 옮긴 자리를 세션 동안 기억한다. 인벤토리와 같은 방식(static).</summary>
        private static bool hasSavedMapPosition;
        private static Vector2 savedMapPosition;

        private int selectedIslandId = -1;

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

            // [카토그래피] 표식 저장소가 지금 월드의 것인지 확인한다(int 비교 1회 - 할당 0).
            // 한 세션 안에서 새 게임/불러오기로 월드가 갈리면 옛 표식이 엉뚱한 섬에 붙으므로 비운다.
            SyncMarkWorld(worldMapManager.worldSeed);

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

            // [카토그래피] 표식이 있는 섬은 **윤곽 링의 색**으로 알린다 - 미니맵과 전체 지도가 같은
            // ApplyMarkerVisual을 지나므로 두 화면에 한 번에 반영되고, 오브젝트가 하나도 늘지 않는다.
            // 미탐사 섬에는 칠하지 않는다(표식을 남기려면 먼저 가 봐야 한다 - 정보 누출 금지).
            IslandMark mark = revealed ? GetIslandMark(island.islandId) : IslandMark.None;
            if (mark != IslandMark.None)
                ringColor = GetIslandMarkColor(mark);

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
                    // [카토그래피] Shift를 누른 채 클릭하면 표식을 순환시킨다(없음 → 고갈됨 →
                    // 자원 있음 → 위험 → 없음). 맨 클릭의 뜻(목적지 선택)은 한 글자도 바꾸지 않는다 -
                    // 이 창의 클릭은 이미 "이동 목적지 고르기"라는 의미가 굳어 있어서, 거기에 표식을
                    // 얹으면 지도를 짚어보는 것만으로 표식이 뒤바뀐다. 목록의 [표식] 버튼도 같은
                    // 함수를 부르므로 조작 경로가 둘이어도 규칙은 하나다.
                    if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift))
                    {
                        if (worldMapManager != null && IsRevealed(worldMapManager.GetIsland(marker.islandId)))
                            CycleIslandMark(marker.islandId);
                    }

                    selectedIslandId = marker.islandId;
                    mapRefreshTimer = 0f; // 표식 변화를 다음 프레임에 바로 반영한다
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
    }
}
