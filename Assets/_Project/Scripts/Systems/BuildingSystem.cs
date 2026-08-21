using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using MakeGame.Data;
using MakeGame.Player;

namespace MakeGame.Systems
{
    /// <summary>
    /// 조각을 지금 놓을 수 없는 이유. 매 프레임 문자열을 만들지 않기 위해 열거형으로 들고 있다가,
    /// UI가 화면에 띄울 때만 DescribeBlockReason으로 미리 만들어 둔 상수 문자열로 바꾼다.
    /// **정수값은 바꾸지 마라** - UI가 이 값으로만 갱신 여부를 판단한다.
    /// </summary>
    public enum BuildBlockReason
    {
        None = 0,
        /// <summary>레이가 지형에도 조각에도 갑판에도 닿지 않았다(허공/너무 멀다).</summary>
        NoTarget = 1,
        /// <summary>그 칸/모서리에 이미 조각이 있다.</summary>
        Occupied = 2,
        /// <summary>벽·문·창문·계단을 받쳐 줄 바닥이 없다.</summary>
        NoSupportingFloor = 3,
        /// <summary>지형이 조각을 뚫고 올라오거나 너무 가파르다.</summary>
        GroundTooUneven = 4,
        /// <summary>땅이 아니다(바다 위 등). 지면 건축은 땅 위에만 한다.</summary>
        NotOnGround = 5,
        /// <summary>재료가 모자란다.</summary>
        NotEnoughMaterials = 6,
        /// <summary>뗏목 갑판 밖이다(배치 26 - 갑판 위 건축).</summary>
        OffDeck = 7,
        /// <summary>계단이 지나가는 자리라 바닥을 덮을 수 없다.</summary>
        StairInTheWay = 8,
        /// <summary>
        /// 뗏목 본체(선체 옆구리·난간·돛대 등 갑판 윗면이 아닌 부분)가 앞을 막고 있다.
        /// 레이가 뗏목을 투과해 뒤쪽 지형에 자리를 잡던 것을 막은 결과다. **새 값은 끝에만 붙인다.**
        /// </summary>
        BlockedByRaft = 9,
    }

    /// <summary>
    /// 건축 모드가 지금 무엇을 놓으려 하는가.
    ///
    /// [왜 BuildPieceType에 뗏목을 붙이지 않았나] 조각은 전부 격자 위의 물건이다 - 셀·층·모서리 축이
    /// 있고, 다섯 개의 격자 표(floor/wall/stair/roof/chest) 중 하나에 반드시 등록된다. 뗏목은 4x8m
    /// 물 위 구조물이라 셀도 층도 축도 없다. 열거형에 값을 하나 붙이면 RegisterPiece·ResolveTarget·
    /// GetYawFor·CreatePieceObject의 default가 전부 "벽"이라, 뗏목이 벽 표에 앉아 실내 판정과 철거
    /// 순서를 오염시키고 세이브 복원의 종류 범위 검사(Floor~Roof)에서 조용히 버려진다.
    /// 그래서 격자를 한 줄도 건드리지 않는 별도 모드로 두고, 갈라지는 지점을 세 곳으로 못 박았다:
    /// ResolveTarget · UpdateGhost(EnsureGhost) · TryPlace.
    /// </summary>
    public enum BuildPlacementMode
    {
        /// <summary>격자 위의 건축 부품(바닥·벽·문·창문·계단·지붕·상자).</summary>
        Piece = 0,
        /// <summary>물 위에 새 뗏목을 세운다. 격자를 쓰지 않는다.</summary>
        Raft = 1,
    }

    /// <summary>
    /// 건축물이 서 있는 좌표 공간. **정수값이 세이브에 그대로 들어간다 - 바꾸지 마라.**
    /// 옛 세이브에는 이 필드가 없어 0(Ground)으로 읽히는데, 그게 정확히 옛 동작이다.
    /// </summary>
    public enum BuildSpace
    {
        /// <summary>지면. 격자 원점 = 월드 (0,0,0)이고 조각의 좌표는 곧 월드 좌표다.</summary>
        Ground = 0,
        /// <summary>뗏목 갑판. 격자 원점 = 갑판 윗면 중심이고 좌표는 전부 뗏목 로컬이다.</summary>
        Deck = 1,
    }

    /// <summary>
    /// 건축 모드의 배치·스냅·미리보기·철거를 담당하는 코어 시스템.
    ///
    /// 역할 분담: **부품의 겉모습과 재료표는 이 파일이 갖고 있지 않다.** 형상은
    /// <see cref="BuildPieceVisualBuilder"/>, 재료·표시 이름은 <see cref="BuildPieceCatalog"/>가 단일
    /// 소스이고, 이 파일은 "어디에 놓을 수 있는가 / 놓아도 되는가 / 무엇을 돌려주는가"만 판단한다.
    ///
    /// 격자 규약(고정):
    /// · 셀 한 변 <see cref="BuildPieceCatalog.CellSize"/>(2m), 한 층 <see cref="BuildPieceCatalog.LevelHeight"/>(2.5m).
    /// · 셀 (cx,cz)는 그 공간 좌표계의 X [cx*2, cx*2+2), Z [cz*2, cz*2+2) 구간이고 중심은 ((cx+0.5)*2, (cz+0.5)*2).
    /// · 바닥은 셀 중심에 놓이며 **로컬 원점이 윗면**이라 position.y가 곧 사람이 딛는 높이다.
    /// · 벽/문/창문은 셀 모서리에 놓인다. 모서리는 (ex, ez, axis)로 canonical하게 표기한다 -
    ///   axis 0 = z가 일정한(= X축을 따라 뻗은) 모서리, axis 1 = x가 일정한 모서리.
    /// · 지붕은 바닥과 같은 (셀, 층) 자리를 쓰지만 **딴 표(roofByKey)** 에 들어간다 - 천장이지 딛고 설
    ///   바닥이 아니다. 로컬 원점이 처마 밑면이라 position.y가 곧 그 층의 천장 높이(= 벽 꼭대기)이고,
    ///   회전이 곧 경사 방향이다. **지붕 위에는 아무것도 쌓지 않는다**(계속 위로 올리려면 바닥을 쓴다).
    /// · 계단은 셀 하나를 통째로 차지하고, **로컬 원점이 밑단의 앞 모서리**다(로컬 +Z로 2m 나아가며
    ///   2.5m 올라간다). 그래서 위치 = 셀 중심에서 바라보는 방향의 반대쪽 모서리 중점이다.
    ///
    /// 좌표 공간이 둘이다(배치 26): 지면(<see cref="BuildSpace.Ground"/>)과 뗏목 갑판
    /// (<see cref="BuildSpace.Deck"/>). 갑판 위 조각은 **뗏목 로컬 좌표**로 계산·저장하고 갑판 밑
    /// 전용 컨테이너에 매달아서, 뗏목이 움직여도 집이 통째로 따라간다.
    ///
    /// 씬에 인스턴스가 없다(씬 파일을 편집할 수 없다). DayNightCycle·QuestUI와 같은
    /// RuntimeInitializeOnLoadMethod + sceneLoaded 패턴으로 씬 로드마다 스스로 생성되므로
    /// **여기 적힌 코드 기본값이 유일한 진실**이다(AGENT_BRIEF 3장의 "씬에 없는 컴포넌트" 규칙).
    /// </summary>
    public partial class BuildingSystem : MonoBehaviour
    {
        // ────────────────────────────────────────────────────────────────────────
        // 설정 (코드 기본값이 유일한 소스)
        // ────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// 건축 모드 토글 키. B는 이 프로젝트에서 비어 있다(전수 grep 결과 사용 중인 키는
        /// C E F G J M R V Tab Space Escape F3 F5 F6 F7 F8 F9 +/- Shift Ctrl 뿐이다).
        /// </summary>
        public KeyCode toggleKey = KeyCode.B;

        /// <summary>
        /// 90도 회전 키. **감독 지시의 R은 쓸 수 없다** - InteractionController.cookKey(조리)가
        /// 이미 R이고, 건축 중에도 조리 입력은 살아 있으므로 같은 프레임에 두 동작이 함께 난다.
        /// Q는 전수 grep에서 어디에도 쓰이지 않는다. 마우스 휠도 함께 받는다(휠은 프로젝트 전체에서
        /// 아무도 쓰지 않는다 - MinimapUI의 확대/축소는 +/- 키다).
        /// </summary>
        public KeyCode rotateKey = KeyCode.Q;

        /// <summary>조각을 놓을 수 있는 최대 거리(m). 상호작용 거리(4m)보다 넉넉해야 벽 한 장 너머가 잡힌다.</summary>
        public float buildDistance = 8f;

        /// <summary>
        /// 뗏목을 세울 수 있는 최대 거리(m). 조각(buildDistance)보다 넉넉하다 - 뗏목은 4x8m라
        /// 팔 닿는 거리에 두면 발밑밖에 안 보이고, 물가에 서서 앞바다에 놓는 것이 정상적인 자세다.
        /// </summary>
        public float raftPlaceDistance = 14f;

        // ── 격자 상수 ───────────────────────────────────────────────────────────
        private const float CellSize = BuildPieceCatalog.CellSize;
        private const float LevelHeight = BuildPieceCatalog.LevelHeight;

        /// <summary>지형으로 인정하는 콜라이더 이름 접두사. TerrainSampler와 같은 규칙이다.</summary>
        private const string TerrainNamePrefix = "Island_";

        /// <summary>
        /// 바닥 윗면을 조준했을 때 "가장자리 띠"로 보는 비율(셀 반폭 기준). 안쪽을 보면 위로 쌓고,
        /// 이 띠를 보면 옆 칸으로 잇는다. 이 규칙이 있어야 2층 바닥을 옆으로 넓힐 수 있다(2층 옆
        /// 허공에는 레이가 맞을 콜라이더가 없어서, 조준점만으로는 층을 알 수 없기 때문이다).
        /// </summary>
        private const float EdgeBand = 0.7f;

        /// <summary>지형이 바닥 윗면보다 이만큼 넘게 솟아 있으면 "파묻힘"으로 보고 막는다(m).</summary>
        private const float BuriedTolerance = 0.45f;

        /// <summary>
        /// 지면에 처음 놓는 바닥이 허용하는 네 모서리 높이차 상한(m). 2m 폭에서 0.6m면 약 17도 경사이고,
        /// 그보다 가파른 곳에는 못 짓는다(한쪽이 땅에 처박히거나 반대쪽이 허공에 뜬다).
        /// </summary>
        private const float GroundFlatnessTolerance = 0.6f;

        /// <summary>지면에 놓는 바닥을 지면보다 살짝 띄우는 값(m). Z-파이팅 방지용.</summary>
        private const float GroundClearance = 0.02f;

        /// <summary>실내 판정 탐색 상한(칸). 이보다 넓은 영역은 "방"이 아니라 야외 데크로 본다.</summary>
        private const int MaxEnclosureCells = 64;

        /// <summary>
        /// 계단을 올라가는 방향의 **반대쪽으로 물려 놓는 거리(m)**. 계단이 셀에 딱 맞게 놓이면
        /// 꼭대기 단의 끝과 참(landing) 바닥의 앞모서리가 정확히 맞닿는데, 그러면 계단을 올라갈 수 없다.
        ///
        /// 왜 그런가(씬 실측 CharacterController: 반지름 0.5 / 높이 2.0 / stepOffset 0.3 / skinWidth 0.08):
        /// · 참 바닥은 윗면이 base+2.5이고 두께 0.20이 아래로 가므로 **밑면이 base+2.30**이다.
        /// · 그 밑면~윗면(2.30~2.50) 구간은 플레이어 기준 "타고 오를 수 없는 벽"이다 - 6번째 단
        ///   (발밑 base+1.944)에서 보면 벽 꼭대기가 0.476m 위라 stepOffset 0.3을 넘는다.
        /// · 그 벽에 몸통이 먼저 닿아서, 캡슐 중심이 셀 앞모서리에서 0.58m 앞까지밖에 못 간다.
        ///   PhysX가 단을 오를 때 쓰는 "0.3 올리고 → 앞으로 → 내리기" 경로에서도 앞으로 갈 수 있는
        ///   한계가 1.519m인데 7번째 단은 1.556m에서 시작한다. **0.037m가 모자라서 6단에서 막힌다.**
        /// 계단을 0.25m 물려 놓으면 그 0.037m가 0.287m 여유로 바뀐다(약 7배). 물린 만큼 꼭대기와 참
        /// 사이에 0.25m 틈이 생기지만 캡슐 지름이 1.0m라 빠지지 않고, 밑단이 뒷칸으로 0.25m 나온다.
        /// </summary>
        private const float StairFrontClearance = 0.25f;

        /// <summary>
        /// 갑판 윗면으로 인정하는 로컬 y 오차(m). 뗏목의 콜라이더는 선체 전체를 덮는 상자 하나라
        /// (RaftStructure.ApplyHullCollider) 옆면도 같은 콜라이더에 맞는다 - 높이와 법선을 함께 본다.
        /// </summary>
        private const float DeckSurfaceTolerance = 0.5f;

        /// <summary>
        /// 레이가 아무것도 맞히지 못했을 때 쓰는 가상 조준 거리(m). 2층 이상에서 정면을 보면 8m 안에
        /// 지형도 조각도 없는 것이 정상이라, 이 값이 없으면 위층에서는 아무것도 지을 수 없다.
        /// 팔 길이보다 조금 긴 4m로 잡아, 겨눈 것이 없을 때 조각이 발밑 근처에만 생기게 한다.
        /// </summary>
        private const float NoHitAimDistance = 4f;

        /// <summary>
        /// 상자를 "조준 중"으로 인정하는 거리(m). InteractionController.interactionDistance와 같은 4m다 -
        /// 이 값이 그쪽보다 길면 E가 닿지 않는 상자에 UI만 뜨고, 짧으면 그 반대가 된다.
        /// </summary>
        private const float ChestFocusDistance = 4f;

        /// <summary>
        /// 뗏목 본체에 막혔다고 인정하려면 채택한 히트보다 이만큼(m)은 확실히 앞서야 한다.
        /// 갑판 윗면 콜라이더(DeckSurface)의 윗면과 선체 상자의 윗면은 **같은 평면**이라
        /// (RaftStructure: 둘 다 DeckSurfaceY) 갑판을 내려다보면 두 거리가 사실상 같게 나온다.
        /// 이 여유가 없으면 반올림 오차 한 번에 갑판 건축 전체가 "막힘"으로 뒤집힌다.
        /// </summary>
        private const float RaftBlockBias = 0.05f;

        // ────────────────────────────────────────────────────────────────────────
        // 상태
        // ────────────────────────────────────────────────────────────────────────

        /// <summary>이 씬의 건축 시스템(씬 리로드마다 새 인스턴스로 교체된다).</summary>
        public static BuildingSystem Instance { get; private set; }

        /// <summary>모드/선택/조각 수가 바뀌었을 때 발행된다. UI가 매 프레임 다시 그리지 않게 하는 통로다.</summary>
        public event System.Action Changed;

        /// <summary>놓여 있는 조각 하나의 등록 정보. position/yaw는 **그 조각이 속한 공간의 로컬 값**이다.</summary>
        private class PlacedPiece
        {
            public BuildPieceType type;
            public BuildSpace space;
            public GameObject go;
            public Transform root;
            public int cellX;      // 바닥/계단: 셀 좌표 · 벽류: 모서리 좌표
            public int cellZ;
            public int level;      // 양자화된 층 번호(= LevelOf(position.y))
            public int axis;       // 벽류 전용(0 = X축을 따라 뻗은 모서리, 1 = Z축)
            public Vector3 position;
            public float yaw;

            /// <summary>
            /// [건축 4티어] 부품 티어(1=나무 2=돌 3=강철 4=대리석). 신축은 언제나 1이고
            /// TryUpgradePiece로만 오른다. **상자는 이 값을 쓰지 않는다** - 상자 등급은 chestState.tier가
            /// 따로 들고 있다(두 승급 축이 한 필드에 겹치면 세이브 해석이 갈라진다).
            /// </summary>
            public int tier = 1;

            /// <summary>
            /// 보관 상자 전용. 내용물과 등급은 **실물이 아니라 이 기록이 들고 있다** - 갑판 재생성으로
            /// 실물이 파괴돼도(RestoreDeckPieces) 새 실물에 같은 그릇을 다시 물려주면
            /// 상자 안의 물건이 그대로 이어진다. 상자가 아닌 조각에서는 항상 null이다.
            /// </summary>
            public StorageChestState chestState;

            /// <summary>보관 상자 전용. 지금 그 그릇을 물고 있는 컴포넌트(실물이 없으면 null).</summary>
            public StorageChest chest;

            /// <summary>
            /// [뗏목 v4] 갑판 조각의 **소속 뗏목**. 지면 조각은 빈 문자열이다.
            /// 이 값이 없던 시절에는 갑판 조각이 "지금 결속된 컨테이너"에 매여 있어서, 다른 뗏목으로
            /// 걸어가는 순간 구조물 전체가 그쪽으로 재부모화됐다(뗏목 사이를 순간이동했다).
            /// </summary>
            public string raftId = string.Empty;

            /// <summary>
            /// [뗏목 v4] 격자 키에 접어 넣는 소속 뗏목 번호(지면은 0). 등록할 때 정해지며 세이브에
            /// 나가지 않는다 - 불러오기 때 뗏목이 번호를 새로 받고 조각도 그때 다시 등록된다.
            /// </summary>
            public int keySlot;
        }

        private readonly List<PlacedPiece> pieces = new List<PlacedPiece>();
        private readonly Dictionary<long, PlacedPiece> floorByKey = new Dictionary<long, PlacedPiece>();
        private readonly Dictionary<long, PlacedPiece> wallByKey = new Dictionary<long, PlacedPiece>();
        private readonly Dictionary<long, PlacedPiece> stairByKey = new Dictionary<long, PlacedPiece>();

        /// <summary>
        /// [배치 38] 지붕. 바닥과 **같은 격자 자리(셀 + 층)** 를 쓰지만 딴 표에 둔다 - 지붕은 천장이지
        /// 딛고 설 바닥이 아니어서, 바닥 조회(TryGetFloorTopY)에 섞이면 지붕 위에 벽·계단이 서 버린다.
        /// </summary>
        private readonly Dictionary<long, PlacedPiece> roofByKey = new Dictionary<long, PlacedPiece>();

        /// <summary>
        /// [배치 39] 보관 상자. 지붕을 roofByKey로 뺀 것과 **완전히 같은 이유로** 딴 표에 둔다 -
        /// 상자는 가구지 구조물이 아니어서, 바닥 조회(TryGetFloorTopY)나 천장 조회(HasCeilingAt)에
        /// 섞이면 상자 위에 벽·창문·계단·지붕이 서고 실내 판정(IsInsideEnclosedStructure)까지 흔들린다.
        /// 이 표는 **한 칸에 상자 하나**라는 규칙과 철거 순서 판정에만 쓴다.
        /// 키는 바닥·계단과 같은 (공간, 셀, 층, NonWallAxis)이며, 층은 상자가 딛고 선 바닥의 층이다.
        /// </summary>
        private readonly Dictionary<long, PlacedPiece> chestByKey = new Dictionary<long, PlacedPiece>();

        /// <summary>레이가 맞은 콜라이더에서 조각 본체를 거슬러 찾기 위한 표(루트 Transform → 조각).</summary>
        private readonly Dictionary<Transform, PlacedPiece> pieceByRoot = new Dictionary<Transform, PlacedPiece>();

        private Transform piecesRoot;
        private Transform ghostRoot;

        private GameObject ghost;
        private BuildPieceType ghostType;
        private bool ghostValid = true;

        private bool buildMode;
        private BuildPieceType selectedType = BuildPieceType.Floor;
        private int rotationSteps;

        /// <summary>지금 조각을 놓는 중인가, 뗏목을 세우는 중인가.</summary>
        private BuildPlacementMode placementMode = BuildPlacementMode.Piece;

        /// <summary>지금 떠 있는 고스트가 뗏목 발자국인가(부품 고스트와 재사용 판정이 다르다).</summary>
        private bool ghostIsRaft;

        /// <summary>
        /// BuildBlockReason으로 표현할 수 없는 불가 사유(뗏목 자리 판정이 돌려주는 문장).
        /// 비어 있으면 UI는 BlockReason을 쓴다. IsValidSite가 상수 문자열만 돌려주므로 할당이 없다.
        /// </summary>
        private string extraBlockReason = string.Empty;

        // 뗏목 자리 판정 캐시. IsValidSite는 레이를 100번 넘게 쏘는데 고스트는 매 프레임 묻는다.
        // 조준점이 실제로 움직였을 때만 다시 묻는다(가만히 서 있으면 레이가 한 번도 안 나간다).
        private Vector3 raftSiteQueryPoint = new Vector3(float.MaxValue, 0f, 0f);
        private float raftSiteQueryYaw = float.MaxValue;
        private int raftSiteQueryRaftCount = -1;
        private bool raftSiteQueryValid;
        private string raftSiteQueryReason = string.Empty;

        /// <summary>해수면 높이를 물어볼 월드 매니저. 매 프레임 찾지 않도록 잡아 둔다.</summary>
        private WorldMapManager cachedWorldMap;

        // 이번 프레임의 조준 결과(위치/회전은 targetSpace의 로컬 값이다).
        private bool hasTarget;
        private bool targetValid;
        private BuildBlockReason blockReason = BuildBlockReason.NoTarget;
        private BuildSpace targetSpace = BuildSpace.Ground;
        private Vector3 targetPosition;
        private float targetYaw;
        private int targetCellX;
        private int targetCellZ;
        private int targetLevel;
        private int targetAxis;

        // 계단을 놓을 때 함께 깔아 주는 참(landing) 바닥. 계단만 놓고 끝내면 꼭대기에서 한 발 내딛는
        // 순간 아래층으로 떨어진다 - "시스템이 스스로 못 쓰는 상태를 만들지 않는다"는 원칙에 걸린다.
        private bool targetNeedsLanding;
        private int targetLandingCellX;
        private int targetLandingCellZ;
        private Vector3 targetLandingPosition;

        private Camera cachedCamera;
        private PlayerInventory cachedInventory;

        // ── 뗏목 갑판 결속 ──────────────────────────────────────────────────────
        private RaftStructure boundRaft;
        private Transform boundDeckRoot;

        /// <summary>
        /// 갑판 건축 컨테이너의 이름. 뗏목마다 DeckRoot 밑에 하나씩 있다.
        /// **공개인 이유**: RaftSailing이 뗏목별 적재량을 잴 때 이 이름으로 컨테이너를 찾는다.
        /// 문자열이 양쪽에 따로 박혀 있으면 한쪽만 바뀌었을 때 적재량이 조용히 0이 된다.
        /// </summary>
        public const string DeckContainerName = "BuildDeckPieces";

        /// <summary>
        /// **지금 결속된 뗏목의** 갑판 컨테이너 캐시. DeckRoot 밑에 있고 로컬 원점이 갑판 윗면
        /// 중심이라, 이 컨테이너의 로컬 좌표가 곧 Deck 공간 좌표다(별도 변환식이 필요 없다).
        ///
        /// ★ 이 필드는 **컨테이너의 주인이 아니다.** 진짜 컨테이너는 뗏목마다 하나씩 DeckRoot 밑에
        ///   살아 있고(EnsureDeckContainer), 여기 담기는 것은 그중 지금 밟고 선 뗏목 것뿐이다.
        ///   예전에는 이 필드가 유일한 컨테이너라, 뗏목을 갈아탈 때마다 갑판 조각 전체가 딸려 왔다.
        /// </summary>
        private Transform deckContainer;

        /// <summary>갑판이 아직 없을 때 복원된 갑판 조각을 잠시 담아 두는 대기열(뗏목이 생기면 세운다).</summary>
        private readonly List<BuildPieceSaveEntry> pendingDeckEntries = new List<BuildPieceSaveEntry>();

        /// <summary>갑판이 아직 없을 때 복원된 갑판 위 상자의 대기열(조각 대기열과 같은 규칙).</summary>
        private readonly List<ChestSaveEntry> pendingDeckChests = new List<ChestSaveEntry>();

        // 대기열을 비우는 동안 원본을 그대로 순회하면, 아직도 세울 수 없는 항목이 스스로 다시
        // 대기열에 들어가면서 목록이 자라 무한히 돈다. 복사본을 돌리고 원본은 먼저 비운다.
        private readonly List<BuildPieceSaveEntry> flushEntryBuffer = new List<BuildPieceSaveEntry>();
        private readonly List<ChestSaveEntry> flushChestBuffer = new List<ChestSaveEntry>();

        /// <summary>대기열을 마지막으로 시도했을 때의 뗏목 상황. 같은 상황이면 다시 시도하지 않는다.</summary>
        private int lastPendingFlushSignature = -1;

        /// <summary>조각이 놓이거나 부서질 때마다 올라간다. 실내 판정 캐시를 통째로 버리는 기준이다.</summary>
        private int structureVersion;

        // 재사용 버퍼(매 프레임 new 금지).
        private static readonly RaycastHit[] rayBuffer = new RaycastHit[32];
        private static readonly RaycastHit[] groundBuffer = new RaycastHit[32];
        private readonly List<BuildPieceCost> placementCostBuffer = new List<BuildPieceCost>();

        /// <summary>[건축 4티어] 승급 비용을 모아 소모할 때만 쓰는 버퍼(E 키를 눌렀을 때만 돈다).</summary>
        private readonly List<BuildPieceCost> upgradeCostBuffer = new List<BuildPieceCost>();

        // ResolveWallTarget이 후보를 고르는 동안 쓰는 작업 필드. 호출이 중첩되지 않으므로 지역 변수를
        // 여러 개 ref로 넘기는 대신 여기 둔다(할당 0). '빈 자리'와 '이미 찬 자리'를 따로 들고 있다가
        // 빈 자리를 우선 고른다 - 아래 벽을 겨눴을 때 그 벽 자신이 이겨 버리면 위층을 못 올린다.
        private bool wallPickFound;
        private float wallPickSqr;
        private int wallPickLevel;
        private float wallPickY;
        private int wallPickEdgeX;
        private int wallPickEdgeZ;
        private int wallPickAxis;
        private bool wallPickTakenFound;
        private float wallPickTakenSqr;
        private int wallPickTakenLevel;
        private float wallPickTakenY;
        private int wallPickTakenEdgeX;
        private int wallPickTakenEdgeZ;
        private int wallPickTakenAxis;
        private readonly List<PlacedPiece> rebuildBuffer = new List<PlacedPiece>();

        /// <summary>
        /// 바닥/계단 키에 쓰는 axis 자리값. 벽류(0/1)와 섞이지 않게 별도 딕셔너리를 쓰므로 값 자체는
        /// 의미가 없지만, 세 종류가 같은 함수로 키를 만들게 0으로 고정해 둔다.
        /// </summary>
        private const int NonWallAxis = 0;

        // ────────────────────────────────────────────────────────────────────────
        // 공개 조회 (UI 전용 - 상태를 바꾸지 않는다)
        // ────────────────────────────────────────────────────────────────────────

        public bool IsBuildModeOn => buildMode;
        public BuildPieceType SelectedType => selectedType;

        /// <summary>지금 조각을 놓는 중인가, 뗏목을 세우는 중인가.</summary>
        public BuildPlacementMode PlacementMode => placementMode;

        /// <summary>건축 메뉴에서 "뗏목"이 골라져 있는가.</summary>
        public bool IsRaftPlacementSelected => placementMode == BuildPlacementMode.Raft;

        /// <summary>BuildBlockReason으로 못 담는 불가 사유. 비어 있으면 BlockReason을 쓴다.</summary>
        public string ExtraBlockReason => extraBlockReason;

        /// <summary>뗏목 한 대를 세울 재료(첫 바닥판)를 들고 있는지.</summary>
        public bool HasMaterialsForRaft()
        {
            return RaftBuildCatalog.HasMaterials(Inventory, RaftBuildEntry.BaseWood);
        }
        public BuildBlockReason BlockReason => blockReason;
        public bool CanPlaceNow => hasTarget && targetValid;
        public int PieceCount => pieces.Count;

        /// <summary>지금 조준하고 있는 좌표 공간(UI가 "갑판 위"를 알려 줄 때 쓴다).</summary>
        public BuildSpace TargetSpace => targetSpace;

        /// <summary>
        /// 지금 계단을 놓으면 참(landing) 바닥이 함께 깔리고 그 재료까지 소모되는지.
        /// UI가 "계단 + 참"이라고 미리 알려 주기 위한 값이다(재료가 조용히 더 나가면 안 된다).
        /// </summary>
        public bool TargetIncludesLanding =>
            placementMode == BuildPlacementMode.Piece && targetNeedsLanding && selectedType == BuildPieceType.Stair;

        /// <summary>
        /// 실제로 지을 수 있는 부품인지. **배치 26에서 계단이 해금됐다** - 이제 잠긴 부품은 없지만,
        /// 앞으로 부품이 추가될 때 다시 쓸 수 있게 판정 자리를 남겨 둔다(UI가 이 값만 본다).
        /// </summary>
        public static bool IsTypeUnlocked(BuildPieceType type)
        {
            switch (type)
            {
                case BuildPieceType.Floor:
                case BuildPieceType.Wall:
                case BuildPieceType.Doorway:
                case BuildPieceType.Window:
                case BuildPieceType.Stair:
                case BuildPieceType.Roof:
                case BuildPieceType.Chest:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>플레이어 인벤토리(없으면 null). 매 호출 씬 전체를 훑지 않도록 캐시한다.</summary>
        public PlayerInventory Inventory
        {
            get
            {
                if (cachedInventory == null)
                    cachedInventory = FindAnyObjectByType<PlayerInventory>();
                return cachedInventory;
            }
        }

        /// <summary>
        /// 인벤토리에 같은 이름의 아이템이 몇 개 있는지 센다(ItemData 참조가 아니라 이름으로 대조).
        /// 재료표가 이름만 들고 있어서(BuildPieceCost.itemName) 이름 대조가 유일한 경로다.
        /// </summary>
        public int CountOwned(string itemName)
        {
            PlayerInventory inventory = Inventory;
            if (inventory == null || inventory.items == null || string.IsNullOrEmpty(itemName))
                return 0;

            int count = 0;
            for (int i = 0; i < inventory.items.Count; i++)
            {
                InventoryItem item = inventory.items[i];
                if (item != null && item.data != null && item.data.itemName == itemName)
                    count++;
            }
            return count;
        }

        /// <summary>이 부품의 재료를 전부 들고 있는지 확인한다(소모하지 않는다).</summary>
        public bool HasMaterialsFor(BuildPieceType type)
        {
            IReadOnlyList<BuildPieceCost> cost = BuildPieceCatalog.GetCost(type);
            if (cost == null)
                return true;

            for (int i = 0; i < cost.Count; i++)
            {
                BuildPieceCost entry = cost[i];
                if (string.IsNullOrEmpty(entry.itemName) || entry.count <= 0)
                    continue;
                if (CountOwned(entry.itemName) < entry.count)
                    return false;
            }
            return true;
        }

        /// <summary>UI에 띄울 사유 문구. 미리 만들어 둔 상수만 돌려주므로 매 프레임 불러도 안전하다.</summary>
        public static string DescribeBlockReason(BuildBlockReason reason)
        {
            switch (reason)
            {
                case BuildBlockReason.None: return "설치 가능";
                case BuildBlockReason.Occupied: return "이미 조각이 있다";
                case BuildBlockReason.NoSupportingFloor: return "받쳐 줄 바닥이나 아래 벽이 없다";
                case BuildBlockReason.GroundTooUneven: return "지면이 고르지 않다";
                case BuildBlockReason.NotOnGround: return "땅 위가 아니다";
                case BuildBlockReason.NotEnoughMaterials: return "재료가 모자란다";
                case BuildBlockReason.OffDeck: return "갑판 밖이다";
                case BuildBlockReason.StairInTheWay: return "계단이 지나가는 자리다";
                case BuildBlockReason.BlockedByRaft: return "뗏목이 가로막았다 - 갑판 위를 겨눠라";
                default: return "놓을 자리를 찾는 중";
            }
        }

        // ────────────────────────────────────────────────────────────────────────
        // 수명 주기
        // ────────────────────────────────────────────────────────────────────────

        /// <summary>씬이 로드될 때마다 새 BuildingSystem을 만든다(DayNightCycle·QuestUI와 같은 패턴).</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Bootstrap()
        {
            SceneManager.sceneLoaded += (scene, mode) =>
            {
                var go = new GameObject("BuildingSystem");
                go.AddComponent<BuildingSystem>();
            };
        }

        private void Awake()
        {
            // Start가 아니라 Awake에서 잡는다 - BuildMenuUI도 런타임 생성이라 실행 순서가 보장되지
            // 않는데(AGENT_BRIEF 4장), 그쪽 Start가 먼저 돌아도 Instance가 이미 있어야 한다.
            Instance = this;

            var piecesGo = new GameObject("Pieces");
            piecesGo.transform.SetParent(transform, false);
            piecesRoot = piecesGo.transform;

            var ghostGo = new GameObject("Ghost");
            ghostGo.transform.SetParent(transform, false);
            ghostRoot = ghostGo.transform;

            // 상자의 등급이 오르면 실물 겉모습을 그 자리에서 새 등급으로 갈아 끼운다. 루트는 그대로
            // 두므로(BuildPieceVisualBuilder.RebuildChest) UI가 들고 있는 StorageChest 참조는 살아 있다.
            StorageChest.TierChanged += OnChestTierChanged;
        }

        private void OnDestroy()
        {
            StorageChest.TierChanged -= OnChestTierChanged;
            StorageChest.SetFocused(null);

            UnbindRaft();

            if (Instance == this)
                Instance = null;
        }

        private void Update()
        {
            // 뗏목은 건조 단계에 따라 생겼다 없어졌다 하므로 매 프레임 싸게(정적 프로퍼티 읽기 하나)
            // 확인한다. 갑판이 다시 만들어지면서 컨테이너가 통째로 날아갔을 수도 있어 null도 함께 본다.
            SyncRaftBinding();

            // 조준 중인 상자는 **입력과 무관하게** 갱신한다. 상자 UI가 열려 있는 동안 게임이 멈춰도
            // (timeScale 0) 방금 연 상자를 계속 가리키고 있어야 하고, 건축 모드가 아닐 때도 조준은 살아 있다.
            UpdateChestFocus();

            // 엔딩/사망 화면은 Time.timeScale을 0으로 세운다(AGENT_BRIEF 4장). 그 동안에는 입력을
            // 아예 받지 않는다 - 죽은 화면 뒤에서 집이 지어지면 안 된다.
            if (Time.timeScale <= 0f)
                return;

            if (Input.GetKeyDown(toggleKey))
                SetBuildMode(!buildMode);

            if (!buildMode)
                return;

            HandleRotationInput();

            ResolveTarget();
            UpdateGhost();

            if (Input.GetMouseButtonDown(0) && !IsPointerOverUI())
                TryPlace();
            else if (Input.GetMouseButtonDown(1) && !IsPointerOverUI())
                TryDemolish();
        }

        /// <summary>휠/회전키로 90도씩 돌린다. 회전의 의미는 부품 종류마다 다르다(GetYawFor 참고).</summary>
        private void HandleRotationInput()
        {
            // Input.mouseScrollDelta는 InputManager의 축 정의에 의존하지 않는다(GetAxis("Mouse
            // ScrollWheel")과 달리 축이 지워져 있어도 예외가 나지 않는다). ProjectSettings는 이번
            // 스테이징에 없어 축 정의를 확인할 수 없으므로 확인이 필요 없는 쪽을 쓴다.
            float wheel = Input.mouseScrollDelta.y;
            if (wheel > 0.01f)
                Rotate(1);
            else if (wheel < -0.01f)
                Rotate(-1);
            else if (Input.GetKeyDown(rotateKey))
                Rotate(1);
        }

        private void Rotate(int steps)
        {
            rotationSteps = ((rotationSteps + steps) % 4 + 4) % 4;
        }

        // ────────────────────────────────────────────────────────────────────────
        // 모드 / 선택
        // ────────────────────────────────────────────────────────────────────────

        public void SetBuildMode(bool on)
        {
            if (buildMode == on)
                return;

            buildMode = on;
            if (!on)
            {
                DestroyGhost();
                hasTarget = false;
                targetValid = false;
                blockReason = BuildBlockReason.NoTarget;

                // 사유 문자열도 함께 지운다. 이것만 남으면 창을 다시 열었을 때 한 프레임 동안
                // 옛 뗏목 사유가 떠 있고, 시간이 멈춘 상태(사망/엔딩)에서는 계속 남는다.
                extraBlockReason = string.Empty;
            }

            // 창을 여닫는 사이에 뗏목이 늘거나 줄었을 수 있다.
            InvalidateRaftSiteCache();

            Changed?.Invoke();
        }

        /// <summary>부품을 고른다. 잠긴 부품은 무시한다(현재 잠긴 부품은 없다).</summary>
        public void SelectType(BuildPieceType type)
        {
            if (!IsTypeUnlocked(type))
                return;

            // ★ "이미 그 부품이다"만 보고 일찍 돌아가면, 뗏목 모드에서 같은 부품을 다시 눌렀을 때
            //   모드가 안 빠져나온다. 모드까지 함께 봐야 한다.
            if (placementMode == BuildPlacementMode.Piece && selectedType == type)
                return;

            placementMode = BuildPlacementMode.Piece;
            selectedType = type;
            rotationSteps = 0;
            Changed?.Invoke();
        }

        /// <summary>건축 메뉴의 "뗏목" 항목. 격자를 쓰지 않는 배치 모드로 넘어간다.</summary>
        public void SelectRaftPlacement()
        {
            if (placementMode == BuildPlacementMode.Raft)
                return;

            placementMode = BuildPlacementMode.Raft;
            rotationSteps = 0;
            InvalidateRaftSiteCache();
            Changed?.Invoke();
        }

        // ────────────────────────────────────────────────────────────────────────
        // 뗏목 갑판 결속
        // ────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// **플레이어가 지금 있는 곳의 뗏목**을 따라가며 갑판 컨테이너를 유지한다.
        /// 뗏목이 바뀌었거나(다른 인스턴스) 컨테이너가 파괴됐으면 다시 만들고 갑판 조각을 되세운다.
        ///
        /// ★ 여기서 RaftStructure.Active(= 가장 완성된 뗏목)를 쓰면 안 된다. 뗏목이 여러 대가 된
        ///   지금, 그러면 저쪽 섬에 있는 더 좋은 뗏목의 갑판에 집을 짓게 된다. 이 질문의 답은
        ///   "지금 내가 밟고 있는 뗏목"이므로 플레이어 위치에서 가장 가까운 것을 쓴다.
        /// </summary>
        private void SyncRaftBinding()
        {
            RaftStructure active = ResolveNearbyRaft();

            if (active != boundRaft)
            {
                UnbindRaft();
                boundRaft = active;
                if (boundRaft != null)
                    boundRaft.DeckRebuilt += OnDeckRebuilt;
            }

            if (boundRaft == null || !boundRaft.HasDeck)
            {
                deckContainer = null;
                return;
            }

            Transform deckRoot = boundRaft.DeckRoot;
            if (deckRoot == null)
            {
                deckContainer = null;
                return;
            }

            boundDeckRoot = deckRoot;

            // 컨테이너는 **뗏목이 들고 있다.** 여기서는 지금 밟고 선 뗏목 것을 집어 올 뿐이라,
            // 물가를 오가거나 뗏목을 갈아타도 남의 조각을 끌어오지 않는다.
            //
            // 캐시가 이미 이 뗏목 것이면 이름 탐색을 건너뛴다(이 함수는 매 프레임 돈다).
            bool created = false;
            if (deckContainer == null || deckContainer.parent != deckRoot)
                deckContainer = EnsureDeckContainer(boundRaft, out created);
            else
                AlignDeckContainer(deckContainer, boundRaft.DeckTopLocalY);

            // 컨테이너를 새로 만들었다 = 이 뗏목의 갑판 조각 실물이 사라졌을 수 있다.
            // **그 뗏목 것만** 되세운다.
            if (created)
                RestoreDeckPieces(boundRaft);

            TryFlushPendingDeckEntries();
        }

        /// <summary>
        /// 대기 중인 갑판 조각을 세워 본다. **뗏목 상황이 지난 시도 때와 같으면 아무것도 하지 않는다.**
        ///
        /// [왜 이 빗장이 필요한가] 세우지 못한 항목은 스스로 다시 대기열에 들어간다. 그런데 이 경로가
        /// 매 프레임 도는 SyncRaftBinding에 있어서, 영영 못 세우는 항목이 하나라도 있으면 프레임마다
        /// 전부 다시 시도하며 경고까지 쏟는다. 실제로 그럴 수 있다 - 세이브에 뗏목이 하나도 없으면
        /// 바닥판 0칸짜리 뗏목이 한 대 서는데, 갑판이 없으니 갑판 조각은 언제까지고 대기열에 남는다.
        ///
        /// 결과를 바꿀 수 있는 것은 "뗏목이 늘거나 줄었다" 또는 "갑판이 깔린 뗏목 수가 달라졌다"뿐이다.
        /// 그 둘을 숫자 하나로 접어 두고, 값이 달라졌을 때만 다시 시도한다.
        /// </summary>
        private void TryFlushPendingDeckEntries()
        {
            if (pendingDeckEntries.Count == 0 && pendingDeckChests.Count == 0)
                return;

            int signature = PendingFlushSignature();
            if (signature == lastPendingFlushSignature)
                return;

            lastPendingFlushSignature = signature;
            FlushPendingDeckEntries();
        }

        /// <summary>대기열 재시도의 판단 근거: (뗏목 수, 갑판이 깔린 뗏목 수).</summary>
        private static int PendingFlushSignature()
        {
            var rafts = RaftStructure.All;
            int decked = 0;

            for (int i = 0; i < rafts.Count; i++)
            {
                RaftStructure raft = rafts[i];
                if (raft != null && raft.HasDeck)
                    decked++;
            }

            return rafts.Count * 1024 + decked;
        }

        /// <summary>
        /// 그 뗏목의 갑판 건축 컨테이너를 확보한다(없으면 만든다). 로컬 원점을 갑판 윗면 중심에
        /// 맞춰 두므로 컨테이너의 로컬 좌표가 곧 그 뗏목의 Deck 공간 좌표가 된다.
        ///
        /// 이름으로 찾는다(<see cref="DeckContainerName"/>). 캐시 필드를 두지 않는 이유는, 캐시가
        /// 하나뿐이면 결국 "지금 결속된 뗏목"에 묶여 예전 버그로 되돌아가기 때문이다. DeckRoot 밑의
        /// 자식은 한 줌이라 Find 비용은 무시할 수 있다.
        /// </summary>
        private Transform EnsureDeckContainer(RaftStructure raft, out bool created)
        {
            created = false;

            if (raft == null)
                return null;

            Transform deckRoot = raft.DeckRoot;
            if (deckRoot == null)
                return null;

            Transform container = deckRoot.Find(DeckContainerName);
            if (container == null)
            {
                var go = new GameObject(DeckContainerName);
                container = go.transform;
                container.SetParent(deckRoot, false);
                created = true;
            }

            AlignDeckContainer(container, raft.DeckTopLocalY);
            return container;
        }

        /// <summary>
        /// 컨테이너의 로컬 원점을 갑판 윗면 중심에 맞춘다. 지금은 DeckTopLocalY가 상수라 첫 정렬
        /// 이후로는 아무 일도 하지 않지만, 값이 상태에 따라 달라지게 바뀌더라도 여기 한 줄이
        /// 그 밑에 지은 집을 통째로 데려간다(조각 좌표를 하나도 안 건드린다).
        /// </summary>
        private static void AlignDeckContainer(Transform container, float topY)
        {
            if (container == null)
                return;

            Vector3 local = container.localPosition;
            if (!Mathf.Approximately(local.y, topY) || local.x != 0f || local.z != 0f)
                container.localPosition = new Vector3(0f, topY, 0f);

            if (container.localRotation != Quaternion.identity)
                container.localRotation = Quaternion.identity;
        }

        /// <summary>그 뗏목의 갑판 컨테이너. 없으면 만들지 않고 null을 돌려준다.</summary>
        private static Transform FindDeckContainer(RaftStructure raft)
        {
            if (raft == null)
                return null;

            Transform deckRoot = raft.DeckRoot;
            return deckRoot != null ? deckRoot.Find(DeckContainerName) : null;
        }

        /// <summary>
        /// 플레이어(없으면 이 컴포넌트) 근처의 뗏목. 너무 멀면 null을 돌려 갑판 결속을 푼다 -
        /// 뭍에 서 있는데 저 멀리 뗏목이 결속돼 있으면 지상 건축이 갑판 좌표계로 새어 든다.
        /// </summary>
        private RaftStructure ResolveNearbyRaft()
        {
            // 기준점은 플레이어 카메라다. 이 컴포넌트 자신의 위치는 씬 어디에 놓였는지에 따라
            // 달라지므로(매니저 오브젝트) 기준으로 쓸 수 없다.
            Camera cam = GetCamera();
            Vector3 origin = cam != null ? cam.transform.position : transform.position;

            // ★ **밟고 선 뗏목이 언제나 이긴다.**
            //
            //   예전에는 중심까지의 거리로만 골랐다. 뗏목 사이 최소 간격이 8.9m인데 갑판 위에서
            //   중심으로부터 4.5m까지 떨어져 설 수 있으니, A의 갑판 끝에 서서 B를 바로 옆에 세우면
            //   B가 더 가까워진다. 그러면 결속이 B로 넘어가고, B는 아직 갑판이 없어 A 위에 지으려던
            //   조각이 통째로 막힌다("뗏목에 막힘"). 자유 배치가 들어오면서 손쉽게 재현되는 상황이
            //   됐으므로, 발밑을 먼저 본다.
            RaftStructure standingOn = FindRaftUnderfoot(origin);
            if (standingOn != null)
                return standingOn;

            RaftStructure nearest = RaftStructure.Nearest(origin);
            if (nearest == null)
                return null;

            // 선체 반경 + 건축 사거리만큼은 넉넉히 잡는다(갑판 가장자리에 서서 짓는 경우).
            float reach = RaftStructure.FootprintRadius + buildDistance + 2f;
            Vector3 delta = nearest.transform.position - origin;
            return delta.sqrMagnitude <= reach * reach ? nearest : null;
        }

        /// <summary>
        /// 그 지점이 **어느 뗏목의 갑판 위인가**(수평으로 선체 안, 높이는 갑판 언저리).
        /// 두 뗏목이 겹칠 수 없으므로(IsValidSite) 답은 많아야 하나다.
        /// </summary>
        private static RaftStructure FindRaftUnderfoot(Vector3 worldPoint)
        {
            // 눈높이에서 갑판까지의 여유. 서 있으면 1.6m 안팎, 앉거나 점프해도 이 안이다.
            const float VerticalReach = 3.5f;

            var rafts = RaftStructure.All;
            for (int i = 0; i < rafts.Count; i++)
            {
                RaftStructure raft = rafts[i];
                if (raft == null || !raft.HasDeck)
                    continue;

                Transform deckRoot = raft.DeckRoot;
                if (deckRoot == null)
                    continue;

                // 뗏목 로컬로 옮겨 사각형 안인지 본다(원이 아니라 사각형이라 4x8의 긴 쪽이 정확하다).
                Vector3 local = deckRoot.InverseTransformPoint(worldPoint);
                Vector2 size = raft.DeckLocalSize;

                if (Mathf.Abs(local.x) > size.x * 0.5f || Mathf.Abs(local.z) > size.y * 0.5f)
                    continue;

                float height = local.y - raft.DeckTopLocalY;
                if (height < -0.5f || height > VerticalReach)
                    continue;

                return raft;
            }

            return null;
        }

        private void UnbindRaft()
        {
            if (boundRaft != null)
                boundRaft.DeckRebuilt -= OnDeckRebuilt;

            boundRaft = null;
            boundDeckRoot = null;
        }

        /// <summary>
        /// 갑판 메시가 다시 만들어졌다. 컨테이너가 살아남았으면 아무것도 할 필요가 없고, 갑판 재생성이
        /// 컨테이너까지 지워 버렸으면(부모 오브젝트를 통째로 Destroy하는 구현) 조각 실물이 함께 사라진다.
        /// 그 경우를 대비해 **기록만 보고 다시 세운다** - 조각의 좌표는 전부 갑판 로컬이라 그대로 복원된다.
        /// </summary>
        private void OnDeckRebuilt()
        {
            if (boundRaft == null)
                return;

            // 컨테이너는 DeckRoot의 자식이고 DeckRoot는 파츠 재생성 대상이 아니라 보통 살아남는다.
            // 그래도 **항상** 되세운다 - 실물만 사라진 경우(상자 주석 참고)를 여기서 흡수한다.
            // 살아 있는 조각에는 같은 부모·같은 로컬 좌표를 다시 대입할 뿐이라 사실상 무해하고,
            // 이 이벤트 자체가 드물게(바닥판을 넓힐 때만) 발생한다.
            deckContainer = EnsureDeckContainer(boundRaft, out _);
            RestoreDeckPieces(boundRaft);
        }

        /// <summary>
        /// 갑판 조각의 실물이 사라졌으면 기록을 보고 다시 만든다. 살아 있으면 새 컨테이너로 옮기기만 한다.
        /// </summary>
        private void RestoreDeckPieces(RaftStructure raft)
        {
            if (raft == null)
                return;

            Transform container = FindDeckContainer(raft);
            if (container == null)
                return;

            // ★ 소속으로 거른다. 예전에는 "space == Deck" 하나만 보고 **모든 뗏목의** 갑판 조각을
            //   여기로 끌어왔다. 그게 구조물이 뗏목 사이를 순간이동하던 원인이다.
            string ownerId = raft.RaftId;

            rebuildBuffer.Clear();
            for (int i = 0; i < pieces.Count; i++)
            {
                PlacedPiece candidate = pieces[i];
                if (candidate.space == BuildSpace.Deck && candidate.raftId == ownerId)
                    rebuildBuffer.Add(candidate);
            }

            if (rebuildBuffer.Count == 0)
                return;

            for (int i = 0; i < rebuildBuffer.Count; i++)
            {
                PlacedPiece piece = rebuildBuffer[i];

                if (piece.go != null)
                {
                    piece.go.transform.SetParent(container, false);
                    ApplyDeckLocalTransform(piece.go.transform, piece.position, piece.yaw);
                    continue;
                }

                // 상자는 상자 등급, 그 외 부품은 부품 티어를 넘긴다(CreatePieceObject 주석 참고).
                int tier = piece.type == BuildPieceType.Chest
                    ? (piece.chestState != null ? piece.chestState.tier : 0)
                    : piece.tier;
                GameObject go = CreatePieceObject(piece.type, container, tier);
                if (go == null)
                {
                    Debug.LogWarning("[BuildingSystem] 갑판 재생성 후 조각 실물을 다시 만들지 못했다.");
                    continue;
                }

                if (!ReferenceEquals(piece.root, null))
                    pieceByRoot.Remove(piece.root);

                piece.go = go;
                piece.root = go.transform;
                pieceByRoot[piece.root] = piece;
                ApplyDeckLocalTransform(piece.root, piece.position, piece.yaw);

                // 상자는 새 실물에 **같은 그릇**을 다시 물린다 - 내용물과 등급이 그대로 이어진다.
                AttachChest(piece);
            }

            rebuildBuffer.Clear();
            Physics.SyncTransforms();
        }

        /// <summary>대기 중이던 갑판 조각·상자(갑판이 없을 때 불러온 세이브)를 실제로 세운다.</summary>
        private void FlushPendingDeckEntries()
        {
            // ★ 복사본을 돌리고 원본은 먼저 비운다. 아직 소속 뗏목이 안 선 항목은 CreatePieceFromEntry가
            //   **스스로 다시 대기열에 넣는다** - 원본을 그대로 순회하면 목록이 자라며 무한히 돈다.
            // 실제로 하나라도 세웠을 때만 물리 동기화와 UI 갱신을 한다. 해소되지 않는 항목이
            // 남아 있으면 이 함수는 매 프레임 불리는데, 그때마다 UI를 다시 그리면 게임 내내
            // 건축 창이 깜빡인다.
            int before = pieces.Count;

            if (pendingDeckEntries.Count > 0)
            {
                flushEntryBuffer.Clear();
                flushEntryBuffer.AddRange(pendingDeckEntries);
                pendingDeckEntries.Clear();

                for (int i = 0; i < flushEntryBuffer.Count; i++)
                    CreatePieceFromEntry(flushEntryBuffer[i]);

                flushEntryBuffer.Clear();
            }

            if (pendingDeckChests.Count > 0)
            {
                flushChestBuffer.Clear();
                flushChestBuffer.AddRange(pendingDeckChests);
                pendingDeckChests.Clear();

                for (int i = 0; i < flushChestBuffer.Count; i++)
                    CreateChestFromEntry(flushChestBuffer[i]);

                flushChestBuffer.Clear();
            }

            if (pieces.Count == before)
                return;

            Physics.SyncTransforms();
            Changed?.Invoke();
        }

        private static void ApplyDeckLocalTransform(Transform t, Vector3 localPosition, float yaw)
        {
            t.localPosition = localPosition;
            t.localRotation = Quaternion.Euler(0f, yaw, 0f);
        }

        /// <summary>갑판에 조각을 놓을 수 있는 상태인가(뗏목이 있고, 갑판 단계이고, 컨테이너가 살아 있다).</summary>
        private bool IsDeckReady => deckContainer != null && boundRaft != null && boundRaft.HasDeck;

        /// <summary>그 갑판 조각이 **지금 결속된 뗏목** 것인가. 조준·건축은 이 뗏목 위에서만 일어난다.</summary>
        private bool IsBoundRaftPiece(PlacedPiece piece)
        {
            if (piece == null || piece.space != BuildSpace.Deck)
                return true;

            return boundRaft != null && piece.raftId == boundRaft.RaftId;
        }

        /// <summary>그 셀이 갑판 안에 **온전히** 들어가는지. 한 귀퉁이라도 밖이면 못 짓는다.</summary>
        private bool IsDeckCellInBounds(int cellX, int cellZ)
        {
            return IsDeckCellInBounds(boundRaft, cellX, cellZ);
        }

        /// <summary>
        /// 그 셀이 **그 뗏목의** 갑판 안에 온전히 들어가는지. 철거 판정처럼 "조각의 뗏목"이 따로
        /// 정해진 자리에서는 결속된 뗏목이 아니라 이쪽을 써야 한다.
        /// </summary>
        private static bool IsDeckCellInBounds(RaftStructure raft, int cellX, int cellZ)
        {
            if (raft == null || !raft.HasDeck)
                return false;

            Vector2 size = raft.DeckLocalSize;
            float halfX = size.x * 0.5f;
            float halfZ = size.y * 0.5f;

            const float epsilon = 0.01f; // 부동소수 오차로 딱 맞는 칸이 탈락하지 않게
            float minX = cellX * CellSize;
            float minZ = cellZ * CellSize;

            return minX >= -halfX - epsilon
                && minX + CellSize <= halfX + epsilon
                && minZ >= -halfZ - epsilon
                && minZ + CellSize <= halfZ + epsilon;
        }

        // ── 공간 ↔ 월드 변환 ────────────────────────────────────────────────────

        private Vector3 SpaceToWorld(BuildSpace space, Vector3 local)
        {
            if (space == BuildSpace.Ground || deckContainer == null)
                return local;
            return deckContainer.TransformPoint(local);
        }

        private Vector3 WorldToSpace(BuildSpace space, Vector3 world)
        {
            if (space == BuildSpace.Ground || deckContainer == null)
                return world;
            return deckContainer.InverseTransformPoint(world);
        }

        private Vector3 WorldToSpaceDirection(BuildSpace space, Vector3 worldDirection)
        {
            if (space == BuildSpace.Ground || deckContainer == null)
                return worldDirection;
            return deckContainer.InverseTransformDirection(worldDirection);
        }

        private Quaternion SpaceToWorldRotation(BuildSpace space, float yaw)
        {
            Quaternion local = Quaternion.Euler(0f, yaw, 0f);
            if (space == BuildSpace.Ground || deckContainer == null)
                return local;
            return deckContainer.rotation * local;
        }

        // ────────────────────────────────────────────────────────────────────────
        // 조준 / 스냅
        // ────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// 뗏목을 세울 자리를 조준한다. 조각과 달리 **격자에 스냅하지 않는다** - 조준선을 해수면
        /// 평면에 떨어뜨린 지점이 곧 자리다(물 위에는 붙일 격자가 없다).
        ///
        /// 판정 순서는 "자리 → 재료"다. 재료를 먼저 보면, 물이 얕아서 못 짓는 자리에 서 있는데
        /// "재료 부족"이라고 뜬다.
        /// </summary>
        private void ResolveRaftTarget()
        {
            targetAxis = NonWallAxis;
            targetCellX = 0;
            targetCellZ = 0;
            targetLevel = 0;

            Camera cam = GetCamera();
            if (cam == null)
                return;

            Transform camTransform = cam.transform;
            Vector3 origin = camTransform.position;
            Vector3 direction = camTransform.forward;

            // 수평선 위를 보고 있으면 평면과 만나지 않거나 지평선 너머의 엉뚱한 지점이 나온다.
            if (direction.y > -0.05f)
                return;

            float seaLevel = ResolveSeaLevel();
            float travel = (seaLevel - origin.y) / direction.y;
            if (travel <= 0.5f || travel > raftPlaceDistance)
                return;

            Vector3 point = origin + direction * travel;
            point.y = seaLevel;

            hasTarget = true;
            targetPosition = point;
            targetYaw = ResolveRaftYaw(camTransform);

            // 조준선이 무엇에 가로막혀 있지는 않은가. 부품 경로는 실제로 맞은 콜라이더로 자리를
            // 정하지만(CastBuildRay) 뗏목은 평면 교점이라, 이 검사가 없으면 바위나 남의 뗏목
            // **너머** 물에 뗏목이 선다.
            if (IsRaftSiteBlocked(origin, point))
            {
                blockReason = BuildBlockReason.BlockedByRaft;
                return;
            }

            if (!QueryRaftSite(point, targetYaw, out string reason))
            {
                // IsValidSite가 돌려주는 문장을 그대로 띄운다. "왜 안 되는지 모르겠는 빨간 고스트"가
                // 이 프로젝트에서 반복해서 나온 UX 실패라, 사유를 요약하지 않고 그대로 넘긴다.
                extraBlockReason = reason;
                return;
            }

            if (!HasMaterialsForRaft())
            {
                blockReason = BuildBlockReason.NotEnoughMaterials;
                return;
            }

            targetValid = true;
            blockReason = BuildBlockReason.None;
        }

        /// <summary>뱃머리 방향. 바라보는 쪽을 기준으로 삼고 휠/Q로 90도씩 돌린다.</summary>
        private float ResolveRaftYaw(Transform camTransform)
        {
            Vector3 flat = camTransform.forward;
            flat.y = 0f;

            float baseYaw = flat.sqrMagnitude > 0.0001f
                ? Quaternion.LookRotation(flat).eulerAngles.y
                : 0f;

            return Mathf.Repeat(baseYaw + rotationSteps * 90f, 360f);
        }

        /// <summary>해수면 높이. 매 프레임 씬을 뒤지지 않도록 월드 매니저를 잡아 둔다.</summary>
        private float ResolveSeaLevel()
        {
            if (cachedWorldMap == null)
                cachedWorldMap = FindAnyObjectByType<WorldMapManager>();

            return cachedWorldMap != null ? cachedWorldMap.seaLevel : 0f;
        }

        /// <summary>
        /// 그 자리에 뗏목을 세울 수 있는지. **조준점이 실제로 움직였을 때만** 진짜로 묻는다.
        ///
        /// IsValidSite는 수심 다섯 번(중심 + 네 귀퉁이) + 물가 탐침 여덟 방향(최대 56번)의
        /// 레이캐스트를 쏘는데, 고스트는 매 프레임 같은 질문을 한다. 가만히 서서 조준만 하고 있으면
        /// 레이가 한 번도 안 나가야 한다.
        ///
        /// ★ 캐시 반경 0.25m는 **정확하지 않다.** 뗏목 자리의 경계는 격자가 아니라 수심 등고선·물가
        ///   거리·뗏목 간 원이라 전부 연속이고, 0.25m 안쪽에서는 실제로 틀린 답이 나올 수 있다.
        ///   그래서 확정(TryPlaceRaft)은 캐시를 거치지 않고 다시 묻는다 - 고스트의 색이 한 프레임
        ///   늦는 것은 감수하고, 실제로 서는 자리만은 반드시 옳게 한다.
        /// </summary>
        private bool QueryRaftSite(Vector3 point, float yaw, out string reason)
        {
            const float CacheRadius = 0.25f;
            const float CacheYawDegrees = 3f;

            // 뗏목 수가 달라졌으면(세우거나 부쉈거나 불러왔다) 답이 통째로 달라진다.
            // 항해로 남의 뗏목이 움직이는 경우까지는 못 잡지만, 그건 0.25m만 조준을 움직이면 풀린다.
            int raftCount = RaftStructure.Count;

            if (raftCount == raftSiteQueryRaftCount
                && Mathf.Abs(Mathf.DeltaAngle(yaw, raftSiteQueryYaw)) < CacheYawDegrees
                && (point - raftSiteQueryPoint).sqrMagnitude < CacheRadius * CacheRadius)
            {
                reason = raftSiteQueryReason;
                return raftSiteQueryValid;
            }

            raftSiteQueryValid = RaftStructure.IsValidSite(point, yaw, null, out raftSiteQueryReason);
            raftSiteQueryPoint = point;
            raftSiteQueryYaw = yaw;
            raftSiteQueryRaftCount = raftCount;

            reason = raftSiteQueryReason;
            return raftSiteQueryValid;
        }

        /// <summary>
        /// 눈에서 그 자리까지 가는 길이 막혀 있는가. **지형과 뗏목만** 막는 것으로 친다 -
        /// 풀·물결·건축 조각은 조준선을 가려도 그 자리에 뗏목을 못 세울 이유가 되지 않는다.
        /// </summary>
        private bool IsRaftSiteBlocked(Vector3 origin, Vector3 point)
        {
            Vector3 delta = point - origin;
            float distance = delta.magnitude;
            if (distance <= 0.5f)
                return false;

            // 끝을 조금 남긴다 - 목표점 바로 아래의 해저에 스치는 것까지 "막힘"으로 세면
            // 얕은 물가에서는 어디를 겨눠도 빨간 고스트가 된다.
            float reach = distance - 0.35f;
            int count = Physics.RaycastNonAlloc(new Ray(origin, delta / distance), rayBuffer, reach);

            for (int i = 0; i < count; i++)
            {
                Collider collider = rayBuffer[i].collider;
                if (collider == null)
                    continue;

                if (collider.gameObject.name.StartsWith(TerrainNamePrefix, System.StringComparison.Ordinal))
                    return true;

                if (IsRaftCollider(collider.transform))
                    return true;
            }

            return false;
        }

        /// <summary>자리 판정 캐시를 버린다(뗏목을 세운 직후처럼 답이 달라졌을 때).</summary>
        private void InvalidateRaftSiteCache()
        {
            raftSiteQueryPoint = new Vector3(float.MaxValue, 0f, 0f);
            raftSiteQueryYaw = float.MaxValue;
            raftSiteQueryRaftCount = -1;
            raftSiteQueryValid = false;
            raftSiteQueryReason = string.Empty;
        }

        private void ResolveTarget()
        {
            hasTarget = false;
            targetValid = false;
            targetNeedsLanding = false;
            blockReason = BuildBlockReason.NoTarget;
            targetSpace = BuildSpace.Ground;
            extraBlockReason = string.Empty;

            // 뗏목은 격자에 붙지 않으므로 조준 방식 자체가 다르다. 여기서 갈라진다.
            if (placementMode == BuildPlacementMode.Raft)
            {
                ResolveRaftTarget();
                return;
            }

            Camera cam = GetCamera();
            if (cam == null)
                return;

            Transform camTransform = cam.transform;
            Ray ray = new Ray(camTransform.position, camTransform.forward);

            if (!CastBuildRay(ray, out Vector3 worldPoint, out Vector3 worldNormal, out PlacedPiece piece,
                    out BuildSpace space, out bool deckSurface, out bool blockedByRaft))
            {
                // 허공은 CastBuildRay가 가상 조준점으로 되살리므로 여기 오지 않는다. 여기 오는 것은
                // "뗏목이 가로막았다" 하나뿐이라, 그 사유를 그대로 띄운다.
                if (blockedByRaft)
                    blockReason = BuildBlockReason.BlockedByRaft;
                return;
            }

            targetSpace = space;
            Vector3 point = WorldToSpace(space, worldPoint);
            Vector3 normal = WorldToSpaceDirection(space, worldNormal);

            switch (selectedType)
            {
                case BuildPieceType.Floor:
                    ResolveFloorTarget(space, piece, deckSurface, point, normal);
                    break;

                case BuildPieceType.Stair:
                    ResolveStairTarget(space, point);
                    break;

                case BuildPieceType.Roof:
                    ResolveRoofTarget(space, point);
                    break;

                case BuildPieceType.Chest:
                    ResolveChestTarget(space, point);
                    break;

                default:
                    ResolveWallTarget(space, point);
                    break;
            }

            // 재료 검사는 자리 판정이 끝난 뒤에 한 번만 한다(자리가 없으면 재료를 셀 필요도 없다).
            // 계단은 참 바닥을 함께 깔 수 있으므로 **두 부품의 재료를 합산해서** 본다 - 같은 재료
            // (나뭇가지)를 둘 다 쓰기 때문에 각각 따로 검사하면 모자란데도 통과한다.
            if (hasTarget && targetValid)
            {
                BuildPlacementCost(placementCostBuffer);
                if (!HasMaterialsForList(placementCostBuffer))
                {
                    targetValid = false;
                    blockReason = BuildBlockReason.NotEnoughMaterials;
                }
            }
        }

        /// <summary>이번 설치로 실제 소모될 재료를 모은다(선택 부품 + 필요하면 참 바닥). 할당 없음.</summary>
        private void BuildPlacementCost(List<BuildPieceCost> buffer)
        {
            buffer.Clear();
            AccumulateCost(buffer, BuildPieceCatalog.GetCost(selectedType));

            if (selectedType == BuildPieceType.Stair && targetNeedsLanding)
                AccumulateCost(buffer, BuildPieceCatalog.GetCost(BuildPieceType.Floor));
        }

        /// <summary>재료표 하나를 buffer에 더한다. 같은 이름은 개수를 합친다.</summary>
        private static void AccumulateCost(List<BuildPieceCost> buffer, IReadOnlyList<BuildPieceCost> cost)
        {
            if (cost == null)
                return;

            for (int i = 0; i < cost.Count; i++)
            {
                BuildPieceCost entry = cost[i];
                if (string.IsNullOrEmpty(entry.itemName) || entry.count <= 0)
                    continue;

                bool merged = false;
                for (int k = 0; k < buffer.Count; k++)
                {
                    if (buffer[k].itemName != entry.itemName)
                        continue;

                    buffer[k] = new BuildPieceCost(entry.itemName, buffer[k].count + entry.count);
                    merged = true;
                    break;
                }

                if (!merged)
                    buffer.Add(entry);
            }
        }

        private bool HasMaterialsForList(List<BuildPieceCost> cost)
        {
            for (int i = 0; i < cost.Count; i++)
            {
                if (CountOwned(cost[i].itemName) < cost[i].count)
                    return false;
            }
            return true;
        }

        /// <summary>바닥 조각의 놓을 자리를 정한다(지면 스냅 · 옆으로 잇기 · 위로 쌓기 · 계단참).</summary>
        private void ResolveFloorTarget(BuildSpace space, PlacedPiece piece, bool deckSurface, Vector3 point, Vector3 normal)
        {
            int cellX;
            int cellZ;
            float topY;
            bool placingOnGround = false;

            if (piece != null && piece.type == BuildPieceType.Floor && normal.y > 0.5f)
            {
                // 이미 있는 바닥의 윗면을 보고 있다. 안쪽이면 위층, 가장자리면 옆 칸.
                float dx = point.x - CellCenterCoord(piece.cellX);
                float dz = point.z - CellCenterCoord(piece.cellZ);
                cellX = piece.cellX;
                cellZ = piece.cellZ;

                if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dz)) > CellSize * 0.5f * EdgeBand)
                {
                    if (Mathf.Abs(dx) >= Mathf.Abs(dz))
                        cellX += dx > 0f ? 1 : -1;
                    else
                        cellZ += dz > 0f ? 1 : -1;

                    topY = piece.position.y;          // 같은 층을 옆으로 잇는다
                }
                else
                {
                    topY = piece.position.y + LevelHeight; // 위로 한 층 쌓는다
                }
            }
            else if (piece != null && piece.type == BuildPieceType.Stair)
            {
                // 계단을 조준하면 **계단이 올라가 닿는 칸**에 참(landing)을 깐다. 계단을 먼저 놓고
                // 그 위에 바닥을 까는 순서가 자연스러워야 한다는 감독 지시를 이 규칙 하나로 만족시킨다.
                GetStairLandingCell(piece, out cellX, out cellZ);
                topY = piece.position.y + LevelHeight;
            }
            else if (deckSurface)
            {
                // 갑판 자체가 0층 바닥이다. 갑판을 조준하면 그 위층(2층 바닥 = 지붕)을 놓는다.
                cellX = CellIndexOf(point.x);
                cellZ = CellIndexOf(point.z);
                topY = LevelHeight;
            }
            else
            {
                cellX = CellIndexOf(point.x);
                cellZ = CellIndexOf(point.z);

                // 옆 칸에 이미 바닥이 있으면 그 높이에 맞춘다 - 지면이 울퉁불퉁해도 데크가 평평해진다.
                SupportRef neighbor = FindNeighborFloorNear(space, cellX, cellZ, point.y);
                if (neighbor.valid)
                {
                    topY = neighbor.y;
                }
                else if (piece == null && space == BuildSpace.Ground)
                {
                    topY = point.y;               // 지형에 직접 놓는다
                    placingOnGround = true;
                }
                else
                {
                    // 벽 옆면 등을 봤는데 붙일 바닥도 없다.
                    blockReason = BuildBlockReason.NoTarget;
                    return;
                }
            }

            bool groundFound = false;
            float maxGround = 0f;
            float minGround = 0f;

            if (space == BuildSpace.Ground)
            {
                // 지형 검사는 자리를 확정하기 **전에** 한다. 지면에 처음 놓는 바닥은 네 모서리 중 가장 높은
                // 지면에 윗면을 맞춰야 어느 구석도 땅에 처박히지 않는다(그래서 topY가 여기서 바뀔 수 있다).
                groundFound = TryGetCellGround(cellX, cellZ, topY, out maxGround, out minGround);
                if (groundFound && placingOnGround)
                    topY = maxGround + GroundClearance;
            }

            hasTarget = true;
            targetCellX = cellX;
            targetCellZ = cellZ;
            targetAxis = NonWallAxis;
            targetLevel = LevelOf(topY);
            targetYaw = GetYawFor(BuildPieceType.Floor, NonWallAxis);
            targetPosition = new Vector3(CellCenterCoord(cellX), topY, CellCenterCoord(cellZ));

            if (space == BuildSpace.Deck)
            {
                if (!IsDeckCellInBounds(cellX, cellZ))
                {
                    blockReason = BuildBlockReason.OffDeck;
                    return;
                }
            }
            else if (!groundFound)
            {
                // 지면 건축은 땅 위에만 한다. 이 검사가 없으면 바닥을 한 칸씩 이어 붙여 바다 위로
                // 다리를 놓을 수 있다(바다 위에 짓고 싶으면 뗏목 갑판을 쓴다).
                blockReason = BuildBlockReason.NotOnGround;
                return;
            }

            // 지붕도 그 자리의 천장이다 - 지붕을 덮은 칸 위에 바닥을 다시 얹지 못한다(배치 38).
            if (HasCeilingAt(space, cellX, cellZ, targetLevel))
            {
                blockReason = BuildBlockReason.Occupied;
                return;
            }

            // 계단이 이 칸을 통과해 올라오고 있으면 그 위를 덮을 수 없다(머리가 천장에 박힌다).
            if (stairByKey.ContainsKey(PieceKey(space, cellX, cellZ, targetLevel - 1, NonWallAxis)))
            {
                blockReason = BuildBlockReason.StairInTheWay;
                return;
            }

            if (space == BuildSpace.Ground)
            {
                if (placingOnGround && maxGround - minGround > GroundFlatnessTolerance)
                {
                    blockReason = BuildBlockReason.GroundTooUneven;
                    return;
                }

                if (!placingOnGround && maxGround > topY + BuriedTolerance)
                {
                    blockReason = BuildBlockReason.GroundTooUneven;
                    return;
                }
            }

            targetValid = true;
            blockReason = BuildBlockReason.None;
        }

        /// <summary>
        /// 벽/문/창문의 놓을 자리를 정한다. 지지는 **둘 중 하나만 있으면 된다**:
        ///  (a) 그 층에 받쳐 줄 바닥(갑판 포함)이 있다 - 예전부터의 규칙.
        ///  (b) **같은 모서리의 바로 아래층에 벽류가 있다**(감독 지시, 배치 37). 벽은 원래 아래 벽이
        ///      받치는 물건이고, 이 규칙이 있어야 계단으로 올라간 자리처럼 **바닥 조각이 없는 층**에서도
        ///      벽을 올릴 수 있다. 한 번에 한 층씩만 올라가므로 연쇄로 쌓으면 탑이 된다(허용).
        ///
        /// 후보는 **빈 자리를 우선**한다. 아래 벽을 겨누면 그 벽 자신(이미 찬 자리)이 거리상 항상 더
        /// 가까워서, 우선순위가 없으면 "이미 조각이 있다"만 뜨고 위층을 영영 못 올린다.
        /// </summary>
        private void ResolveWallTarget(BuildSpace space, Vector3 point)
        {
            int centerX = CellIndexOf(point.x);
            int centerZ = CellIndexOf(point.z);

            wallPickFound = false;
            wallPickSqr = float.MaxValue;
            wallPickTakenFound = false;
            wallPickTakenSqr = float.MaxValue;

            // 조준점 주변 3x3 칸의 모서리를 훑는다. 조준점이 벽 위라 셀 경계에 딱 걸려도(어느 쪽 셀로
            // 반올림될지 모르는 상황) 옆 칸이 후보에 들어오므로 "벽을 보고 있는데 붙일 데가 없다"가 되지 않는다.
            for (int ox = -1; ox <= 1; ox++)
            {
                for (int oz = -1; oz <= 1; oz++)
                {
                    int cx = centerX + ox;
                    int cz = centerZ + oz;
                    SupportRef support = FindSupportNear(space, cx, cz, point.y);

                    for (int side = 0; side < 4; side++)
                    {
                        GetEdgeOfCell(cx, cz, side, out int ex, out int ez, out int axis);

                        if (support.valid)
                            ConsiderWallEdge(space, point, ex, ez, axis, support.level, support.y);

                        if (TryGetWallSupport(space, ex, ez, axis, point.y, out int stackLevel, out float stackY))
                            ConsiderWallEdge(space, point, ex, ez, axis, stackLevel, stackY);
                    }
                }
            }

            bool useTaken = !wallPickFound;
            if (useTaken && !wallPickTakenFound)
            {
                blockReason = BuildBlockReason.NoSupportingFloor;
                return;
            }

            int bestLevel = useTaken ? wallPickTakenLevel : wallPickLevel;
            float bestY = useTaken ? wallPickTakenY : wallPickY;
            int bestEdgeX = useTaken ? wallPickTakenEdgeX : wallPickEdgeX;
            int bestEdgeZ = useTaken ? wallPickTakenEdgeZ : wallPickEdgeZ;
            int bestAxis = useTaken ? wallPickTakenAxis : wallPickAxis;

            hasTarget = true;
            targetCellX = bestEdgeX;
            targetCellZ = bestEdgeZ;
            targetLevel = bestLevel;
            targetAxis = bestAxis;
            targetPosition = EdgeMidpoint(bestEdgeX, bestEdgeZ, bestAxis, bestY);
            targetYaw = GetYawFor(selectedType, bestAxis);

            if (useTaken)
            {
                blockReason = BuildBlockReason.Occupied;
                return;
            }

            targetValid = true;
            blockReason = BuildBlockReason.None;
        }

        /// <summary>
        /// 벽 후보 하나를 점수 매겨 최선값에 반영한다. 빈 자리와 이미 찬 자리를 따로 들고 있다가
        /// ResolveWallTarget이 빈 자리를 우선 고른다. 매 프레임 도는 경로라 델리게이트/할당을 쓰지 않고
        /// 필드에 직접 쓴다(호출이 중첩되지 않으므로 안전하다).
        /// </summary>
        private void ConsiderWallEdge(BuildSpace space, Vector3 point, int edgeX, int edgeZ, int axis, int level, float baseY)
        {
            Vector3 mid = EdgeMidpoint(edgeX, edgeZ, axis, baseY);

            // 벽 높이의 절반쯤을 기준으로 재야 "벽 위쪽을 겨눴을 때"도 그 모서리가 이긴다.
            mid.y += LevelHeight * 0.5f;
            float sqr = (mid - point).sqrMagnitude;

            if (wallByKey.ContainsKey(PieceKey(space, edgeX, edgeZ, level, axis)))
            {
                if (sqr >= wallPickTakenSqr)
                    return;

                wallPickTakenSqr = sqr;
                wallPickTakenLevel = level;
                wallPickTakenY = baseY;
                wallPickTakenEdgeX = edgeX;
                wallPickTakenEdgeZ = edgeZ;
                wallPickTakenAxis = axis;
                wallPickTakenFound = true;
                return;
            }

            if (sqr >= wallPickSqr)
                return;

            wallPickSqr = sqr;
            wallPickLevel = level;
            wallPickY = baseY;
            wallPickEdgeX = edgeX;
            wallPickEdgeZ = edgeZ;
            wallPickAxis = axis;
            wallPickFound = true;
        }

        /// <summary>
        /// 이 모서리에서 조준점 아래로 가장 가까운 벽류를 찾아, 그 **꼭대기**를 새 벽의 밑면으로 돌려준다.
        /// 바닥 지지(FindSupportNear)와 같은 기준으로 "조준점보다 위에 있는 것은 딛는 것이 아니다"를 적용한다.
        /// </summary>
        private bool TryGetWallSupport(BuildSpace space, int edgeX, int edgeZ, int axis, float y,
            out int level, out float baseY)
        {
            level = 0;
            baseY = 0f;

            int start = LevelOf(y);
            bool found = false;
            float bestDelta = float.MaxValue;

            // 아래 벽은 밑면이 y보다 최대 한 층 아래에 있을 수 있어 두 층 아래까지 본다.
            for (int d = -2; d <= 1; d++)
            {
                int candidate = start + d;
                if (!wallByKey.TryGetValue(PieceKey(space, edgeX, edgeZ, candidate, axis), out PlacedPiece wall))
                    continue;

                // 아래 벽의 꼭대기가 조준점보다 한참 위면 그 벽 위에 올리려는 것이 아니다.
                // 여유를 벽 높이의 60%로 두어, 벽의 중간쯤을 겨눠도 "그 위"가 후보로 잡히게 한다
                // (꼭대기 모서리를 정확히 겨누게 만들면 조작이 너무 까다롭다).
                float topY = wall.position.y + LevelHeight;
                if (topY > y + LevelHeight * 0.6f)
                    continue;

                float delta = Mathf.Abs(topY - y);
                if (delta >= bestDelta)
                    continue;

                bestDelta = delta;
                level = candidate + 1;
                baseY = topY;
                found = true;
            }

            return found;
        }

        /// <summary>
        /// 계단의 놓을 자리를 정한다. 계단은 **바닥 칸 하나를 통째로 차지**하고, 그 칸의 바닥 위에서
        /// 시작해 바라보는 방향으로 2m 나아가며 한 층(2.5m) 올라간다.
        /// 위층 바닥(계단 칸의 천장)은 **없어야** 하고, 계단이 닿는 앞칸의 참(landing) 바닥은 없으면
        /// 계단과 함께 깔아 준다(StairFrontClearance / TryPlace 주석 참고).
        /// </summary>
        private void ResolveStairTarget(BuildSpace space, Vector3 point)
        {
            targetNeedsLanding = false;

            int cellX = CellIndexOf(point.x);
            int cellZ = CellIndexOf(point.z);

            SupportRef support = FindSupportNear(space, cellX, cellZ, point.y);
            if (!support.valid)
            {
                blockReason = BuildBlockReason.NoSupportingFloor;
                return;
            }

            float yaw = GetYawFor(BuildPieceType.Stair, NonWallAxis);
            Vector3 forward = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;

            hasTarget = true;
            targetCellX = cellX;
            targetCellZ = cellZ;
            targetLevel = support.level;
            targetAxis = NonWallAxis;
            targetYaw = yaw;

            // 계단의 로컬 원점은 밑단의 **앞** 모서리다(BuildPieceVisualBuilder.BuildStair: 로컬 z 0→2).
            // 셀 중심에서 바라보는 방향의 반대쪽으로 반 칸 + 통행 여유(StairFrontClearance)만큼 물린다.
            targetPosition = new Vector3(CellCenterCoord(cellX), support.y, CellCenterCoord(cellZ))
                - forward * (CellSize * 0.5f + StairFrontClearance);

            int landingLevel = support.level + 1;
            GetStepCell(cellX, cellZ, yaw, out int landingX, out int landingZ);
            targetLandingCellX = landingX;
            targetLandingCellZ = landingZ;
            targetLandingPosition = new Vector3(CellCenterCoord(landingX), support.y + LevelHeight, CellCenterCoord(landingZ));

            if (space == BuildSpace.Deck && !IsDeckCellInBounds(cellX, cellZ))
            {
                blockReason = BuildBlockReason.OffDeck;
                return;
            }

            if (stairByKey.ContainsKey(PieceKey(space, cellX, cellZ, support.level, NonWallAxis)))
            {
                blockReason = BuildBlockReason.Occupied;
                return;
            }

            // 계단이 뚫고 올라가야 할 위층 바닥(= 계단 칸의 천장)이 이미 덮여 있으면 못 놓는다.
            // 반대 방향(바닥 먼저)은 ResolveFloorTarget이 막는다 - 양방향 대칭이다.
            if (HasCeilingAt(space, cellX, cellZ, landingLevel))
            {
                blockReason = BuildBlockReason.StairInTheWay;
                return;
            }

            // 참 자리를 지붕이 이미 덮고 있으면 그 위에 바닥을 깔 수 없다 - 지붕을 먼저 걷어야 한다.
            // (지붕은 바닥 표에 없으므로, 이 검사가 없으면 아래 targetNeedsLanding이 참이 되어
            //  지붕과 같은 자리에 참 바닥이 자동으로 겹쳐 깔린다.)
            if (HasRoofAt(space, landingX, landingZ, landingLevel))
            {
                blockReason = BuildBlockReason.Occupied;
                return;
            }

            // 참이 없으면 계단과 함께 깔아 준다. 깔 수 없는 자리(갑판 밖 / 바다 위)면 계단 자체를 막는다 -
            // 내려설 곳이 없는 계단은 "못 쓰는 상태"이고, 그것을 만들 수 있게 두지 않는다.
            targetNeedsLanding = !HasFloorAt(space, landingX, landingZ, landingLevel);
            if (targetNeedsLanding)
            {
                // 참이 **다른 계단의 통행 경로**를 덮으면 안 된다. 이 검사를 빼면 자동 배치가
                // ResolveFloorTarget의 금지 규칙을 우회해서, 손으로는 못 놓는 자리에 바닥이 생긴다.
                if (stairByKey.ContainsKey(PieceKey(space, landingX, landingZ, landingLevel - 1, NonWallAxis)))
                {
                    blockReason = BuildBlockReason.StairInTheWay;
                    return;
                }

                if (!CanPlaceLandingFloor(space, landingX, landingZ, targetLandingPosition.y))
                {
                    blockReason = space == BuildSpace.Deck ? BuildBlockReason.OffDeck : BuildBlockReason.NotOnGround;
                    return;
                }
            }

            targetValid = true;
            blockReason = BuildBlockReason.None;
        }

        /// <summary>
        /// 지붕의 놓을 자리를 정한다. 지붕은 바닥처럼 **셀 하나**를 덮고, 로컬 원점이 처마 밑면이라
        /// position.y가 곧 그 층의 천장 높이(= 벽 꼭대기 / 위층 바닥 윗면과 같은 평면)다.
        ///
        /// **지지 판정을 새로 만들지 않는다.** 이미 있는 두 규칙을 그대로 빌린다:
        ///  (a) 이 칸에 딛고 설 바닥(갑판 포함)이 있으면 그 바닥이 이루는 층의 천장 = 바닥층 + 1.
        ///  (b) 이 칸을 두른 네 모서리에 아래 벽이 있으면 그 벽의 꼭대기(<see cref="TryGetWallSupport"/>).
        ///      배치 37의 "바닥이 없어도 아래 벽이 있으면 한 층"과 같은 근거이므로, 계단으로 올라간
        ///      바닥 없는 층에도 지붕을 덮을 수 있다.
        /// 둘 다 나오면 **더 높은 쪽**을 고른다. 지붕은 그 층에서 가장 위에 얹히는 물건이고, 벽 중간을
        /// 겨눴을 때 TryGetWallSupport가 밑동과 꼭대기 사이에서 동률이 되는 것도 이걸로 풀린다.
        /// </summary>
        private void ResolveRoofTarget(BuildSpace space, Vector3 point)
        {
            int cellX = CellIndexOf(point.x);
            int cellZ = CellIndexOf(point.z);

            bool found = false;
            int bestLevel = 0;
            float bestY = 0f;

            // (a) 바닥 지지 - 벽·계단이 쓰는 것과 완전히 같은 조회다.
            SupportRef floorSupport = FindSupportNear(space, cellX, cellZ, point.y);
            if (floorSupport.valid)
            {
                found = true;
                bestLevel = floorSupport.level + 1;
                bestY = floorSupport.y + LevelHeight;
            }

            // (b) 벽 지지. 조준점을 반 층 남짓 올려서 본다 - 벽 한가운데를 겨누면 그 벽의 밑동과
            // 꼭대기가 정확히 같은 거리라, 원래 기준(가장 가까운 것)으로는 밑동(= 이 층의 바닥면)이
            // 뽑힌다. 지붕은 벽 **위**에 얹히는 물건이라 그 동률을 위쪽으로 기울여야 한다.
            float wallProbeY = point.y + LevelHeight * 0.6f;
            for (int side = 0; side < 4; side++)
            {
                GetEdgeOfCell(cellX, cellZ, side, out int ex, out int ez, out int axis);
                if (!TryGetWallSupport(space, ex, ez, axis, wallProbeY, out int level, out float y))
                    continue;

                if (found && level <= bestLevel)
                    continue;

                found = true;
                bestLevel = level;
                bestY = y;
            }

            if (!found)
            {
                blockReason = BuildBlockReason.NoSupportingFloor;
                return;
            }

            hasTarget = true;
            targetCellX = cellX;
            targetCellZ = cellZ;
            targetLevel = bestLevel;
            targetAxis = NonWallAxis;
            targetYaw = GetYawFor(BuildPieceType.Roof, NonWallAxis);
            targetPosition = new Vector3(CellCenterCoord(cellX), bestY, CellCenterCoord(cellZ));

            if (space == BuildSpace.Deck && !IsDeckCellInBounds(cellX, cellZ))
            {
                blockReason = BuildBlockReason.OffDeck;
                return;
            }

            // 그 자리의 천장은 하나뿐이다(바닥이 이미 덮고 있으면 지붕을 겹쳐 놓지 않는다).
            if (HasCeilingAt(space, cellX, cellZ, bestLevel))
            {
                blockReason = BuildBlockReason.Occupied;
                return;
            }

            targetValid = true;
            blockReason = BuildBlockReason.None;
        }

        /// <summary>참(landing) 바닥을 그 칸에 깔 수 있는지. 바닥 배치와 같은 기준을 쓴다.</summary>
        private bool CanPlaceLandingFloor(BuildSpace space, int cellX, int cellZ, float topY)
        {
            if (space == BuildSpace.Deck)
                return IsDeckCellInBounds(cellX, cellZ);

            if (!TryGetCellGround(cellX, cellZ, topY, out float maxGround, out float _))
                return false;

            return maxGround <= topY + BuriedTolerance;
        }

        /// <summary>계단이 올라가 닿는 칸(참을 까는 자리).</summary>
        private static void GetStairLandingCell(PlacedPiece stair, out int cellX, out int cellZ)
        {
            GetStepCell(stair.cellX, stair.cellZ, stair.yaw, out cellX, out cellZ);
        }

        /// <summary>셀 (x,z)에서 yaw 방향으로 한 칸 나아간 셀.</summary>
        private static void GetStepCell(int x, int z, float yaw, out int cellX, out int cellZ)
        {
            int step = ((Mathf.RoundToInt(yaw / 90f) % 4) + 4) % 4;
            cellX = x;
            cellZ = z;

            switch (step)
            {
                case 0: cellZ += 1; break;   // 로컬 +Z
                case 1: cellX += 1; break;   // 로컬 +X
                case 2: cellZ -= 1; break;
                default: cellX -= 1; break;
            }
        }

        /// <summary>
        /// 부품의 최종 y 회전. 벽류는 모서리 방향이 회전을 결정하므로(그래야 모서리에 정확히 맞는다)
        /// 회전 입력은 앞뒤 뒤집기(180도)로만 쓴다. 바닥은 정사각형이라 90도 회전이 겉모습 문제이고,
        /// **계단은 회전이 곧 올라가는 방향**이고, **지붕은 회전이 곧 경사 방향**이라
        /// 둘 다 네 방향이 전부 의미가 다르다.
        /// </summary>
        private float GetYawFor(BuildPieceType type, int axis)
        {
            if (type == BuildPieceType.Floor || type == BuildPieceType.Stair || type == BuildPieceType.Roof
                || type == BuildPieceType.Chest)
                return rotationSteps * 90f;

            float baseYaw = axis == 0 ? 0f : 90f;
            return baseYaw + (rotationSteps % 2) * 180f;
        }

        /// <summary>
        /// 지형/조각/갑판만 걸러서 가장 가까운 히트를 돌려준다. 초목·자원 노드·사냥감·플레이어는
        /// 통과시킨다(TerrainSampler가 "Island_" 접두사만 지형으로 인정하는 것과 같은 이유 -
        /// 콜라이더가 붙은 장식물에 조준이 걸리면 배치 높이가 통째로 틀어진다).
        ///
        /// **아무것도 안 맞아도 실패로 끝내지 않는다.** 2층에 올라가 정면을 보면 8m 안에 지형도 조각도
        /// 없어서 예전에는 그대로 "놓을 자리 없음"이 됐다 - 계단으로 올라간 자리에서 벽이 안 서던
        /// 실제 원인이 이것이다. 히트가 없으면 시선 앞 NoHitAimDistance 지점을 조준점으로 삼아,
        /// 그 주변의 바닥·벽을 근거로 자리를 잡는다(실물이 없으므로 조각 참조는 null이고 철거는 그대로 실패한다).
        ///
        /// **"허공(히트 없음)"과 "뗏목에 막힘"은 다른 결과다.** 앞의 것만 가상 조준점 폴백을 탄다.
        /// 뗏목 선체 옆구리·난간을 겨눈 경우는 blockedByRaft로 실패를 확정하고 폴백도 타지 않는다 -
        /// 폴백을 태우면 옆구리를 겨눴을 때 조각이 허공에 뜨고, 그냥 통과시키면 뒤쪽 섬 지형에 잡힌다.
        /// </summary>
        private bool CastBuildRay(Ray ray, out Vector3 hitPoint, out Vector3 hitNormal, out PlacedPiece bestPiece,
            out BuildSpace space, out bool deckSurface, out bool blockedByRaft)
        {
            hitPoint = ray.origin;
            hitNormal = -ray.direction;
            bestPiece = null;
            space = BuildSpace.Ground;
            deckSurface = false;
            blockedByRaft = false;

            int count = Physics.RaycastNonAlloc(ray, rayBuffer, buildDistance);
            bool found = false;
            float bestDistance = float.MaxValue;

            // 뗏목 본체에 막힌 가장 가까운 지점. 채택한 히트보다 앞서면 조준 실패로 확정한다.
            float raftBlockDistance = float.MaxValue;

            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = rayBuffer[i];
                Collider collider = hit.collider;
                if (collider == null)
                    continue;

                PlacedPiece piece = FindPieceOf(collider.transform);
                bool onDeck = false;

                // ★ 남의 뗏목에 지은 조각은 조준 대상이 아니다. 지금 밟고 선 뗏목(boundRaft)의
                //   좌표계로 그 조각을 풀면, A의 조각을 겨눠 B의 엉뚱한 칸에 짓게 된다.
                //   뗏목 본체와 똑같이 "막힘"으로 처리한다(허공이 아니라 실제로 뭔가 있으므로).
                if (piece != null && piece.space == BuildSpace.Deck && !IsBoundRaftPiece(piece))
                {
                    if (hit.distance < raftBlockDistance)
                        raftBlockDistance = hit.distance;
                    continue;
                }

                if (piece == null)
                {
                    bool isTerrain = collider.gameObject.name.StartsWith(TerrainNamePrefix, System.StringComparison.Ordinal);
                    if (!isTerrain)
                    {
                        if (!IsDeckCollider(collider.transform))
                        {
                            // 뗏목 본체(선체 상자·난간·돛대·승선 발판)에 맞은 히트. 예전에는 여기서 그냥
                            // 다음 히트로 넘어가 레이가 뗏목을 투과했다 - 옆구리를 겨누면 뒤쪽 섬 지형에
                            // 조각이 잡혔다. 거리만 기억해 두고 루프가 끝난 뒤 채택 히트와 견준다.
                            if (hit.distance < raftBlockDistance && IsRaftCollider(collider.transform))
                                raftBlockDistance = hit.distance;
                            continue;
                        }
                        onDeck = true;
                    }
                }

                if (hit.distance >= bestDistance)
                    continue;

                bestDistance = hit.distance;
                hitPoint = hit.point;
                hitNormal = hit.normal;
                bestPiece = piece;
                space = piece != null ? piece.space : (onDeck ? BuildSpace.Deck : BuildSpace.Ground);
                deckSurface = onDeck;
                found = true;
            }

            if (found && deckSurface)
            {
                // 갑판 윗면인지 선체 옆구리인지 가른다. 뗏목의 콜라이더는 선체 전체를 덮는 상자 하나라
                // (RaftStructure.ApplyHullCollider) 옆면도 같은 콜라이더에 맞는다. 높이와 법선을 둘 다
                // 봐야 "물에 잠긴 옆구리에 집이 붙는" 일이 없다.
                Vector3 localPoint = WorldToSpace(BuildSpace.Deck, hitPoint);
                Vector3 localNormal = WorldToSpaceDirection(BuildSpace.Deck, hitNormal);
                if (Mathf.Abs(localPoint.y) > DeckSurfaceTolerance || localNormal.y < 0.5f)
                {
                    // 갑판 콜라이더의 옆면·아랫면을 긁은 것도 "뗏목에 막힘"이다(허공이 아니다).
                    found = false;
                    if (bestDistance < raftBlockDistance)
                        raftBlockDistance = bestDistance;
                }
            }

            // 뗏목이 앞을 가로막았으면 그 뒤에서 찾은 자리는 버리고, 폴백도 타지 않고 실패로 끝낸다.
            // RaftBlockBias만큼 확실히 앞설 때만 막힌 것으로 본다 - 갑판 윗면과 선체 윗면은 같은 평면이라
            // 갑판을 내려다볼 때 두 거리가 같게 나오고, 그 경우는 갑판이 이겨야 한다.
            if (raftBlockDistance < (found ? bestDistance : float.MaxValue) - RaftBlockBias)
            {
                blockedByRaft = true;
                hitPoint = ray.origin + ray.direction * raftBlockDistance;
                hitNormal = -ray.direction;
                bestPiece = null;
                space = BuildSpace.Ground;
                deckSurface = false;
                return false;
            }

            if (found)
                return true;

            // 히트 없음(=허공) - 시선 앞 한 지점을 가상의 조준점으로 삼는다.
            hitPoint = ray.origin + ray.direction * NoHitAimDistance;
            hitNormal = -ray.direction;
            bestPiece = null;
            deckSurface = false;
            space = IsDeckAimPoint(hitPoint) ? BuildSpace.Deck : BuildSpace.Ground;
            return true;
        }

        /// <summary>실물에 맞지 않은 조준점이 갑판 격자 안쪽인지(갑판 위 허공을 겨눈 경우).</summary>
        private bool IsDeckAimPoint(Vector3 worldPoint)
        {
            if (!IsDeckReady)
                return false;

            Vector3 local = WorldToSpace(BuildSpace.Deck, worldPoint);
            if (local.y < -DeckSurfaceTolerance || local.y > LevelHeight * 3f)
                return false;

            return IsDeckCellInBounds(CellIndexOf(local.x), CellIndexOf(local.z));
        }

        /// <summary>이 콜라이더가 뗏목 갑판(또는 그 부속)인지. 부모를 거슬러 DeckRoot에 닿으면 참이다.</summary>
        private bool IsDeckCollider(Transform t)
        {
            if (!IsDeckReady || boundDeckRoot == null)
                return false;

            while (t != null)
            {
                if (t == boundDeckRoot)
                    return true;
                t = t.parent;
            }
            return false;
        }

        /// <summary>
        /// 이 콜라이더가 지금 결속된 뗏목의 일부인지(선체 상자·난간·돛대·승선 발판 등, 갑판 윗면이
        /// 아닌 부속까지 포함). 갑판 윗면 판정은 IsDeckCollider가 따로 하므로 여기서는 손대지 않는다.
        /// </summary>
        private bool IsRaftCollider(Transform t)
        {
            // 기준점은 ResolveNearbyRaft와 같아야 한다 - 이 컴포넌트 자신의 위치는 매니저 오브젝트라
            // 월드 어디에 놓였는지에 따라 달라져 기준으로 쓸 수 없다.
            Camera raftCam = GetCamera();
            Vector3 raftOrigin = raftCam != null ? raftCam.transform.position : transform.position;
            RaftStructure raft = boundRaft != null ? boundRaft : RaftStructure.Nearest(raftOrigin);
            if (raft == null)
                return false;

            Transform raftRoot = raft.transform;
            while (t != null)
            {
                if (t == raftRoot)
                    return true;
                t = t.parent;
            }
            return false;
        }

        /// <summary>콜라이더가 붙은 자식에서 조각 본체까지 부모를 거슬러 올라간다(할당 없음).</summary>
        private PlacedPiece FindPieceOf(Transform t)
        {
            while (t != null)
            {
                if (pieceByRoot.TryGetValue(t, out PlacedPiece piece))
                    return piece;
                t = t.parent;
            }
            return null;
        }

        // ── 바닥 조회 (갑판 = 0층 가상 바닥) ────────────────────────────────────

        /// <summary>딛고 설 수 있는 바닥 하나를 가리키는 값. 갑판처럼 실물 조각이 없는 바닥도 표현한다.</summary>
        private struct SupportRef
        {
            public bool valid;
            public int level;
            public float y;
        }

        /// <summary>
        /// 그 칸 그 층에 딛고 설 바닥이 있는지. **갑판은 0층에 실물 없는 바닥이 깔려 있는 것으로 친다** -
        /// 그래야 갑판 위에 바닥 조각을 먼저 깔지 않고도 벽을 세울 수 있다(갑판이 이미 바닥이다).
        /// </summary>
        private bool TryGetFloorTopY(BuildSpace space, int cellX, int cellZ, int level, out float y)
        {
            if (floorByKey.TryGetValue(PieceKey(space, cellX, cellZ, level, NonWallAxis), out PlacedPiece floor))
            {
                y = floor.position.y;
                return true;
            }

            if (space == BuildSpace.Deck && level == 0 && IsDeckCellInBounds(cellX, cellZ))
            {
                y = 0f; // Deck 공간의 원점이 곧 갑판 윗면이다
                return true;
            }

            y = 0f;
            return false;
        }

        private bool HasFloorAt(BuildSpace space, int cellX, int cellZ, int level)
        {
            return TryGetFloorTopY(space, cellX, cellZ, level, out float _);
        }

        /// <summary>그 칸 그 층에 지붕이 있는지. **딛고 설 수 있는 면이 아니다**(바닥 조회와 섞지 마라).</summary>
        private bool HasRoofAt(BuildSpace space, int cellX, int cellZ, int level)
        {
            return roofByKey.ContainsKey(PieceKey(space, cellX, cellZ, level, NonWallAxis));
        }

        // ── 소속 맥락 판(철거 전용) ────────────────────────────────────────────
        //
        // 위의 조회들은 전부 "지금 조준 중인 맥락"(= 결속된 뗏목)으로 키를 만든다. 그런데 철거는
        // **부수려는 조각의 뗏목**이 기준이어야 한다 - 뗏목 두 대가 가까이 붙어 ResolveNearbyRaft가
        // 옆 뗏목을 집으면, 지붕이 얹힌 벽이 "위에 아무것도 없다"로 판정돼 부서지고 지붕이 공중에 뜬다.

        /// <summary>그 조각이 속한 뗏목 기준으로 그 칸 그 층에 지붕이 있는지.</summary>
        private bool HasRoofAt(PlacedPiece context, int cellX, int cellZ, int level)
        {
            return roofByKey.ContainsKey(
                PieceKey(context.space, context.keySlot, cellX, cellZ, level, NonWallAxis));
        }

        /// <summary>그 조각이 속한 뗏목 기준으로 그 칸 그 층에 딛고 설 바닥이 있는지.</summary>
        private bool HasFloorAt(PlacedPiece context, int cellX, int cellZ, int level)
        {
            if (floorByKey.ContainsKey(
                    PieceKey(context.space, context.keySlot, cellX, cellZ, level, NonWallAxis)))
                return true;

            // 갑판 0층은 실물 없는 바닥이다(TryGetFloorTopY 주석 참고). **그 조각의 뗏목** 크기로 본다.
            if (context.space == BuildSpace.Deck && level == 0)
                return IsDeckCellInBounds(RaftStructure.FindById(context.raftId), cellX, cellZ);

            return false;
        }

        /// <summary>
        /// 그 칸 그 층을 이미 무언가가 덮고 있는지(바닥 또는 지붕). 한 자리의 천장은 하나뿐이라는
        /// 규칙을 한 곳에 모아 둔 것이다 - 바닥과 지붕은 서로의 자리를 빼앗지 않는다.
        /// </summary>
        private bool HasCeilingAt(BuildSpace space, int cellX, int cellZ, int level)
        {
            return HasFloorAt(space, cellX, cellZ, level) || HasRoofAt(space, cellX, cellZ, level);
        }

        /// <summary>
        /// 이 칸에서 y에 가장 가까운 바닥을 찾는다. 지면에 놓인 바닥은 y가 정수배가 아니므로
        /// 양자화 층 번호가 한 칸 어긋날 수 있어 L-1 · L · L+1 세 층을 본다.
        /// </summary>
        private SupportRef FindSupportNear(BuildSpace space, int cellX, int cellZ, float y)
        {
            SupportRef best = default;
            float bestDelta = float.MaxValue;
            int level = LevelOf(y);

            for (int d = -1; d <= 1; d++)
            {
                int candidate = level + d;
                if (!TryGetFloorTopY(space, cellX, cellZ, candidate, out float floorY))
                    continue;

                // 조준점보다 위에 있는 바닥은 "딛고 선 바닥"이 아니다(위층에 붙이려면 위층을 조준한다).
                if (floorY > y + 0.6f)
                    continue;

                float delta = Mathf.Abs(floorY - y);
                if (delta >= bestDelta)
                    continue;

                bestDelta = delta;
                best.valid = true;
                best.level = candidate;
                best.y = floorY;
            }

            return best;
        }

        /// <summary>맞닿은 네 칸 중 이 높이에 가장 가까운 바닥(데크 평탄화용).</summary>
        private SupportRef FindNeighborFloorNear(BuildSpace space, int cellX, int cellZ, float y)
        {
            SupportRef best = default;
            float bestDelta = float.MaxValue;
            int level = LevelOf(y);

            for (int side = 0; side < 4; side++)
            {
                GetNeighborCell(cellX, cellZ, side, out int nx, out int nz);

                for (int d = -1; d <= 1; d++)
                {
                    int candidate = level + d;
                    if (!TryGetFloorTopY(space, nx, nz, candidate, out float floorY))
                        continue;

                    float delta = Mathf.Abs(floorY - y);
                    if (delta > LevelHeight * 0.8f || delta >= bestDelta)
                        continue;

                    bestDelta = delta;
                    best.valid = true;
                    best.level = candidate;
                    best.y = floorY;
                }
            }

            return best;
        }

        /// <summary>
        /// 한 칸의 네 모서리 지면 높이를 재서 가장 높은 곳과 낮은 곳을 돌려준다(레이 4번).
        /// **네 모서리가 전부** 지형에 닿아야 한다 - 하나라도 비면 그 칸은 물가 밖으로 절반쯤 걸쳐 있다.
        /// </summary>
        private static bool TryGetCellGround(int cellX, int cellZ, float aroundY, out float maxGround, out float minGround)
        {
            float half = CellSize * 0.5f * 0.9f; // 셀 안쪽으로 조금 당겨서 잰다(바로 옆 절벽에 걸리지 않게)
            float centerX = CellCenterCoord(cellX);
            float centerZ = CellCenterCoord(cellZ);

            int found = 0;
            maxGround = float.MinValue;
            minGround = float.MaxValue;

            for (int i = 0; i < 4; i++)
            {
                float sx = centerX + ((i & 1) == 0 ? -half : half);
                float sz = centerZ + ((i & 2) == 0 ? -half : half);
                if (!TryGetGroundHeight(sx, sz, aroundY, out float groundY))
                    continue;

                found++;
                if (groundY > maxGround) maxGround = groundY;
                if (groundY < minGround) minGround = groundY;
            }

            if (found == 4)
                return true;

            maxGround = 0f;
            minGround = 0f;
            return false;
        }

        /// <summary>
        /// 그 XZ의 지형 높이를 잰다. TerrainSampler.SnapToGround와 **같은 규칙**(이름이 "Island_"로
        /// 시작하는 콜라이더만 지형)이지만, 그쪽은 못 찾았을 때 입력 위치를 그대로 돌려줘서 성공/실패를
        /// 구분할 수 없다. 바다 위 배치를 막으려면 실패 여부가 필요해 여기서 따로 쏜다.
        /// </summary>
        private static bool TryGetGroundHeight(float x, float z, float aroundY, out float groundY)
        {
            groundY = 0f;
            Vector3 origin = new Vector3(x, aroundY + 60f, z);
            int count = Physics.RaycastNonAlloc(origin, Vector3.down, groundBuffer, 120f);

            bool found = false;
            float closest = float.MaxValue;

            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = groundBuffer[i];
                if (hit.collider == null)
                    continue;
                if (!hit.collider.gameObject.name.StartsWith(TerrainNamePrefix, System.StringComparison.Ordinal))
                    continue;
                if (hit.distance >= closest)
                    continue;

                closest = hit.distance;
                groundY = hit.point.y;
                found = true;
            }

            return found;
        }

        // ────────────────────────────────────────────────────────────────────────
        // 미리보기(고스트)
        // ────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// 고스트는 **타입이 바뀔 때만** 다시 만든다. 위치/회전은 옮기기만 하고, 유효/무효는
        /// SetGhostValid로 색만 바꾼다(매 프레임 오브젝트를 만들면 GC와 SRP 배처가 함께 죽는다).
        /// 갑판 위 조준일 때는 갑판 로컬 좌표를 월드로 바꿔서 놓는다 - 고스트를 뗏목에 매달지 않는
        /// 이유는 매 프레임 위치를 새로 계산하므로 부모를 바꿀 필요가 없고, 타입이 그대로인데
        /// 부모만 바뀌어 고스트를 다시 만드는 일도 없어야 하기 때문이다.
        /// </summary>
        private void UpdateGhost()
        {
            if (!hasTarget)
            {
                if (ghost != null && ghost.activeSelf)
                    ghost.SetActive(false);
                return;
            }

            EnsureGhost();
            if (ghost == null)
                return;

            if (!ghost.activeSelf)
                ghost.SetActive(true);

            ghost.transform.SetPositionAndRotation(
                SpaceToWorld(targetSpace, targetPosition),
                SpaceToWorldRotation(targetSpace, targetYaw));

            if (ghostValid != targetValid)
            {
                BuildPieceVisualBuilder.SetGhostValid(ghost, targetValid);
                ghostValid = targetValid;
            }
        }

        private void EnsureGhost()
        {
            if (placementMode == BuildPlacementMode.Raft)
            {
                if (ghost != null && ghostIsRaft)
                    return;

                DestroyGhost();

                ghost = BuildPieceVisualBuilder.CreateRaftSiteGhost(ghostRoot, targetValid);
                ghostIsRaft = true;
                ghostValid = targetValid;
                return;
            }

            // ★ ghostIsRaft도 함께 본다. 뗏목 고스트가 떠 있는 상태에서 부품으로 돌아왔을 때
            //   ghostType이 우연히 같으면 뗏목 발자국이 그대로 남는다.
            if (ghost != null && !ghostIsRaft && ghostType == selectedType)
                return;

            DestroyGhost();

            ghost = BuildPieceVisualBuilder.CreateGhost(selectedType, ghostRoot, targetValid);
            ghostType = selectedType;
            ghostIsRaft = false;
            ghostValid = targetValid;
        }

        private void DestroyGhost()
        {
            if (ghost == null)
                return;

            // Destroy는 프레임 끝까지 지연된다(AGENT_BRIEF 4장). 먼저 꺼서 이번 프레임에 새로 만드는
            // 고스트와 겹쳐 보이지 않게 한다.
            ghost.SetActive(false);
            Destroy(ghost);
            ghost = null;
            ghostIsRaft = false;
        }

        // ────────────────────────────────────────────────────────────────────────
        // 설치
        // ────────────────────────────────────────────────────────────────────────

        /// <summary>지금 조준한 자리에 조각을 세운다. 유효하지 않으면 아무것도 하지 않는다.</summary>
        /// <summary>
        /// 겨눈 자리에 새 뗏목을 세운다.
        ///
        /// **순서가 곧 안전장치다.** 뗏목을 먼저 세우고, 재료는 첫 바닥판이 실제로 놓였을 때만
        /// 나간다(RaftBuildCatalog.TryBuild가 검사·소모·설치를 한 곳에서 한다). 실패하면 방금 만든
        /// 뗏목을 도로 지운다 - "재료만 사라졌다"도 "빈 뗏목이 남았다"도 생기지 않는다.
        /// (이 프로젝트에서 아이템이 증발한 사고가 네 번 있었고 전부 반대 순서였다.)
        /// </summary>
        private void TryPlaceRaft()
        {
            if (!hasTarget || !targetValid)
            {
                AudioManager.Instance?.PlayActionFail();
                return;
            }

            // 확정 직전 재검사. 고스트 판정은 0.25m 캐시를 타므로 그 사이에 다른 뗏목이 섰을 수 있다.
            // 여기서는 캐시를 거치지 않고 직접 묻는다(클릭 한 번에 레이 한 묶음은 싸다).
            if (!RaftStructure.IsValidSite(targetPosition, targetYaw, null, out string reason))
            {
                // 상태줄도 이번 프레임에 바로 바꾼다. 사유 없이 실패음만 나면 "왜 안 되는지 모르겠는
                // 빨간 고스트"가 되고, 그건 이 프로젝트에서 반복해서 나온 UX 실패다.
                extraBlockReason = reason;
                targetValid = false;

                Debug.LogWarning($"[BuildingSystem] 뗏목을 세울 수 없다: {reason}");
                InvalidateRaftSiteCache();
                AudioManager.Instance?.PlayActionFail();
                return;
            }

            RaftStructure raft = RaftStructure.Create();
            raft.PlaceAt(targetPosition, Quaternion.Euler(0f, targetYaw, 0f));

            if (!RaftBuildCatalog.TryBuild(raft, Inventory, RaftBuildEntry.BaseWood, out string failure))
            {
                RaftStructure.DestroyRaft(raft);
                Debug.LogWarning($"[BuildingSystem] 뗏목 첫 바닥판을 놓지 못해 배치를 취소했다: {failure}");
                AudioManager.Instance?.PlayActionFail();
                return;
            }

            // 방금 선 뗏목 때문에 주변 자리 판정이 달라졌다.
            InvalidateRaftSiteCache();

            AudioManager.Instance?.PlayCraftSuccess();
            Changed?.Invoke();
        }

        public void TryPlace()
        {
            if (placementMode == BuildPlacementMode.Raft)
            {
                TryPlaceRaft();
                return;
            }

            if (!hasTarget || !targetValid)
            {
                AudioManager.Instance?.PlayActionFail();
                return;
            }

            Transform parent = targetSpace == BuildSpace.Deck ? deckContainer : piecesRoot;
            if (parent == null)
            {
                AudioManager.Instance?.PlayActionFail();
                return;
            }

            // 계단은 참(landing) 바닥과 **한 묶음으로** 세운다. 둘 중 하나라도 실패하면 둘 다 되돌리고
            // 재료는 한 톨도 건드리지 않는다.
            bool needsLanding = selectedType == BuildPieceType.Stair && targetNeedsLanding;

            // **순서가 곧 안전장치다.** 실물을 먼저 만들고, 성공한 뒤에야 재료를 지운다.
            // (이 프로젝트에서 아이템이 증발한 사고가 네 번 있었고 전부 반대 순서였다.)
            // 새로 놓는 상자는 언제나 소형(등급 0)이다 - 상위 등급은 업그레이드로만 도달한다.
            GameObject go = CreatePieceObject(selectedType, parent, 0);
            if (go == null)
            {
                Debug.LogWarning($"[BuildingSystem] '{BuildPieceCatalog.GetDisplayName(selectedType)}' 실물 생성에 " +
                    "실패해 설치를 취소했다. 재료는 소모하지 않았다.");
                AudioManager.Instance?.PlayActionFail();
                return;
            }

            ApplyPieceTransform(go.transform, targetSpace, targetPosition, targetYaw);

            GameObject landingGo = null;
            if (needsLanding)
            {
                landingGo = BuildPieceVisualBuilder.CreateSolid(BuildPieceType.Floor, parent);
                if (landingGo == null)
                {
                    DiscardNewPiece(go);
                    Debug.LogWarning("[BuildingSystem] 계단 참(바닥) 실물 생성에 실패해 계단 설치를 취소했다. 재료는 소모하지 않았다.");
                    AudioManager.Instance?.PlayActionFail();
                    return;
                }

                ApplyPieceTransform(landingGo.transform, targetSpace, targetLandingPosition, 0f);
            }

            BuildPlacementCost(placementCostBuffer);
            if (!ConsumeCostList(placementCostBuffer))
            {
                // 여기까지 오면 안 된다(ResolveTarget에서 이미 걸렀다). 그래도 왔다면 방금 만든 실물을
                // 되돌려서 "재료는 남고 조각도 없다"는 안전한 상태로 끝낸다.
                DiscardNewPiece(go);
                DiscardNewPiece(landingGo);
                Debug.LogWarning("[BuildingSystem] 재료 소모에 실패해 방금 만든 조각을 되돌렸다.");
                AudioManager.Instance?.PlayActionFail();
                return;
            }

            PlacedPiece placed = RegisterPiece(selectedType, targetSpace, go, targetCellX, targetCellZ, targetLevel,
                targetAxis, targetPosition, targetYaw);

            if (selectedType == BuildPieceType.Chest)
                AttachChest(placed);

            if (landingGo != null)
            {
                RegisterPiece(BuildPieceType.Floor, targetSpace, landingGo, targetLandingCellX, targetLandingCellZ,
                    targetLevel + 1, NonWallAxis, targetLandingPosition, 0f);
            }

            // Physics.autoSyncTransforms는 꺼져 있다(AGENT_BRIEF 4장). 방금 만든 콜라이더에 다음
            // 프레임 레이캐스트가 맞으려면 여기서 물리 씬에 반영해야 한다.
            Physics.SyncTransforms();

            AudioManager.Instance?.PlayCraftSuccess();
            Changed?.Invoke();
        }

        /// <summary>
        /// 아직 등록하지 않은 실물을 되돌린다. Destroy는 프레임 끝까지 지연되므로 먼저 꺼서 이번
        /// 프레임의 레이캐스트/물리에서 즉시 빠지게 한다.
        /// </summary>
        private void DiscardNewPiece(GameObject go)
        {
            if (go == null)
                return;

            go.SetActive(false);
            Destroy(go);
        }

        /// <summary>조각을 제 공간의 좌표에 놓는다. 갑판 조각은 로컬, 지면 조각은 월드다.</summary>
        private void ApplyPieceTransform(Transform t, BuildSpace space, Vector3 position, float yaw)
        {
            if (space == BuildSpace.Deck)
                ApplyDeckLocalTransform(t, position, yaw);
            else
                t.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
        }

        /// <summary>
        /// 재료를 실제로 소모한다. **전부 있는지 먼저 확인한 뒤에 지운다** - 중간에 모자라면 이미 지운
        /// 재료를 되돌릴 방법이 없다(Shelter.ConsumeMaterials와 같은 규칙).
        /// </summary>
        private bool ConsumeCostList(List<BuildPieceCost> cost)
        {
            if (cost == null || cost.Count == 0)
                return true;

            PlayerInventory inventory = Inventory;
            if (inventory == null || inventory.items == null)
                return false;

            for (int i = 0; i < cost.Count; i++)
            {
                if (CountOwned(cost[i].itemName) < cost[i].count)
                    return false;
            }

            for (int i = 0; i < cost.Count; i++)
            {
                BuildPieceCost entry = cost[i];
                if (string.IsNullOrEmpty(entry.itemName) || entry.count <= 0)
                    continue;

                int remaining = entry.count;
                for (int k = inventory.items.Count - 1; k >= 0 && remaining > 0; k--)
                {
                    InventoryItem item = inventory.items[k];
                    if (item == null || item.data == null || item.data.itemName != entry.itemName)
                        continue;

                    inventory.items.RemoveAt(k);
                    remaining--;
                }
            }

            inventory.NotifyInventoryChanged();
            return true;
        }

        // ────────────────────────────────────────────────────────────────────────
        // 부품 티어 승급 (건축 4티어 - 상자 등급 승급과 같은 "제자리 업그레이드" 패턴)
        // ────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// 조준한 콜라이더가 티어 승급 대상 부품(바닥/벽/문/창/계단/지붕)인지 확인하고 현재 티어를
        /// 돌려준다. **격자 역조회(pieceByRoot → FindPieceOf)를 그대로 재사용한다** - UI가 부품 식별을
        /// 따로 구현하면 화면에 뜨는 대상과 E가 잡는 대상이 갈라진다. 상자는 자체 등급 경로(ChestUI)가
        /// 있으므로 여기서 항상 false다. 상태를 바꾸지 않으므로 매 프레임 불러도 안전하다.
        /// </summary>
        public bool TryGetPieceTier(Transform colliderTransform, out BuildPieceType type, out int tier)
        {
            type = BuildPieceType.Floor;
            tier = 1;

            if (colliderTransform == null)
                return false;

            PlacedPiece piece = FindPieceOf(colliderTransform);
            if (piece == null || !BuildPieceCatalog.IsTierUpgradable(piece.type))
                return false;

            type = piece.type;
            tier = BuildPieceCatalog.ClampPieceTier(piece.tier);
            return true;
        }

        /// <summary>
        /// 조준한 부품을 다음 티어로 승급한다(최대 4티어 = 대리석). 카탈로그의 승급비
        /// (BuildPieceCatalog.GetPieceUpgradeCost)를 소모하며, **재료를 지우기 전에 전부 있는지 먼저
        /// 확인한다**(ConsumeCostList의 기존 규칙). 성공하면 해당 부품의 렌더러 재질만 새 티어로
        /// 갈아 끼운다 - 지오메트리·콜라이더·격자 표는 그대로다(자리·지지 판정이 변하지 않는다).
        /// 실패(대상 아님/최고 티어/재료 부족)는 false를 돌려주고, 대상이 맞는데 못 올린 경우만
        /// 실패음을 낸다(대상이 아닐 때는 호출부의 다른 분기가 처리할 몫이다).
        /// </summary>
        public bool TryUpgradePiece(Transform colliderTransform, PlayerInventory inventory)
        {
            if (colliderTransform == null)
                return false;

            PlacedPiece piece = FindPieceOf(colliderTransform);
            if (piece == null || !BuildPieceCatalog.IsTierUpgradable(piece.type))
                return false;

            // 호출부가 인벤토리를 손에 들고 있으면 그것을 캐시로 쓴다(씬 전수 조회 생략).
            if (inventory != null)
                cachedInventory = inventory;

            int tier = BuildPieceCatalog.ClampPieceTier(piece.tier);
            if (tier >= BuildPieceCatalog.PieceTierCount)
            {
                AudioManager.Instance?.PlayActionFail();
                return false;
            }

            upgradeCostBuffer.Clear();
            AccumulateCost(upgradeCostBuffer, BuildPieceCatalog.GetPieceUpgradeCost(piece.type, tier));

            if (!ConsumeCostList(upgradeCostBuffer))
            {
                AudioManager.Instance?.PlayActionFail();
                return false;
            }

            piece.tier = tier + 1;
            BuildPieceVisualBuilder.ApplyTier(piece.go, piece.tier);

            AudioManager.Instance?.PlayCraftSuccess();
            Changed?.Invoke();
            return true;
        }

        // ────────────────────────────────────────────────────────────────────────
        // 등록 / 해제
        // ────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// 조각 하나를 표에 올린다. **만들어진 기록을 돌려준다** - 보관 상자처럼 등록 직후에 부가 상태
        /// (내용물 그릇)를 물려야 하는 부품이 있어서, 호출부가 그 기록을 손에 쥘 수 있어야 한다.
        /// </summary>
        /// <param name="owner">
        /// 갑판 조각의 소속 뗏목. null이면 지금 결속된 뗏목으로 본다(직접 짓는 경우가 그렇다).
        /// **세이브 복원에서는 반드시 명시해야 한다** - 뭍에 서서 불러오면 결속된 뗏목이 없거나
        /// 엉뚱한 뗏목이라, 조각이 남의 갑판에 등록된다.
        /// </param>
        private PlacedPiece RegisterPiece(BuildPieceType type, BuildSpace space, GameObject go, int cellX, int cellZ,
            int level, int axis, Vector3 position, float yaw, RaftStructure owner = null)
        {
            RaftStructure raft = space == BuildSpace.Deck
                ? (owner != null ? owner : boundRaft)
                : null;

            var piece = new PlacedPiece
            {
                type = type,
                space = space,
                go = go,
                root = go.transform,
                cellX = cellX,
                cellZ = cellZ,
                level = level,
                axis = axis,
                position = position,
                yaw = yaw,
                raftId = raft != null ? raft.RaftId : string.Empty,
                keySlot = raft != null ? raft.KeySlot : 0,
            };

            pieces.Add(piece);
            pieceByRoot[piece.root] = piece;

            switch (type)
            {
                case BuildPieceType.Floor:
                    floorByKey[PieceKey(space, piece.keySlot, cellX, cellZ, level, NonWallAxis)] = piece;
                    break;

                case BuildPieceType.Stair:
                    stairByKey[PieceKey(space, piece.keySlot, cellX, cellZ, level, NonWallAxis)] = piece;
                    break;

                case BuildPieceType.Roof:
                    roofByKey[PieceKey(space, piece.keySlot, cellX, cellZ, level, NonWallAxis)] = piece;
                    break;

                // 상자는 **딴 표**다. 바닥/천장 조회에 절대 들어가지 않는다(BuildPieceType.Chest 주석 참고).
                case BuildPieceType.Chest:
                    chestByKey[PieceKey(space, piece.keySlot, cellX, cellZ, level, NonWallAxis)] = piece;
                    break;

                default:
                    wallByKey[PieceKey(space, piece.keySlot, cellX, cellZ, level, axis)] = piece;
                    break;
            }

            structureVersion++;
            return piece;
        }

        /// <summary>
        /// 부품 실물을 만든다. tier의 의미가 종류에 따라 다르다 - 상자면 상자 등급(0~3, 크기가 다르다),
        /// 그 외 부품이면 부품 티어(1~4, 재질만 다르다 · 0/1은 나무 그대로).
        /// </summary>
        private static GameObject CreatePieceObject(BuildPieceType type, Transform parent, int tier)
        {
            if (type == BuildPieceType.Chest)
                return BuildPieceVisualBuilder.CreateChestSolid(parent, tier);

            GameObject go = BuildPieceVisualBuilder.CreateSolid(type, parent);
            if (go != null && tier > 1)
                BuildPieceVisualBuilder.ApplyTier(go, tier);
            return go;
        }

        private void UnregisterPiece(PlacedPiece piece)
        {
            pieces.Remove(piece);

            // Unity의 == 는 파괴된 오브젝트를 null로 취급하므로 `piece.root != null`로 거르면 이미
            // 파괴된 조각의 표 항목이 영원히 남는다. 참조 동일성으로만 검사한다.
            if (!ReferenceEquals(piece.root, null))
                pieceByRoot.Remove(piece.root);

            switch (piece.type)
            {
                case BuildPieceType.Floor:
                    floorByKey.Remove(PieceKey(piece.space, piece.keySlot, piece.cellX, piece.cellZ, piece.level, NonWallAxis));
                    break;

                case BuildPieceType.Stair:
                    stairByKey.Remove(PieceKey(piece.space, piece.keySlot, piece.cellX, piece.cellZ, piece.level, NonWallAxis));
                    break;

                case BuildPieceType.Roof:
                    roofByKey.Remove(PieceKey(piece.space, piece.keySlot, piece.cellX, piece.cellZ, piece.level, NonWallAxis));
                    break;

                case BuildPieceType.Chest:
                    chestByKey.Remove(PieceKey(piece.space, piece.keySlot, piece.cellX, piece.cellZ, piece.level, NonWallAxis));
                    break;

                default:
                    wallByKey.Remove(PieceKey(piece.space, piece.keySlot, piece.cellX, piece.cellZ, piece.level, piece.axis));
                    break;
            }

            structureVersion++;
        }

        private void ClearAllPieces()
        {
            for (int i = 0; i < pieces.Count; i++)
            {
                GameObject go = pieces[i].go;
                if (go == null)
                    continue;

                go.SetActive(false);
                Destroy(go);
            }

            pieces.Clear();
            pieceByRoot.Clear();
            floorByKey.Clear();
            wallByKey.Clear();
            stairByKey.Clear();
            roofByKey.Clear();
            chestByKey.Clear();
            pendingDeckEntries.Clear();
            pendingDeckChests.Clear();

            // 빗장을 푼다. 새로 불러온 세이브의 대기열은 뗏목 상황이 그대로여도 반드시 한 번은 시도해야 한다.
            lastPendingFlushSignature = -1;

            StorageChest.SetFocused(null);
            structureVersion++;
        }

        // ────────────────────────────────────────────────────────────────────────
        // 잡동사니
        // ────────────────────────────────────────────────────────────────────────

        private Camera GetCamera()
        {
            if (cachedCamera == null)
                cachedCamera = Camera.main;
            return cachedCamera;
        }

        private static bool IsPointerOverUI()
        {
            var eventSystem = UnityEngine.EventSystems.EventSystem.current;
            return eventSystem != null && eventSystem.IsPointerOverGameObject();
        }
    }

    /// <summary>
    /// 조각 하나의 저장 항목. **필드 제거·개명은 세이브를 깬다 - 추가만 하라**(AGENT_BRIEF 3장).
    /// 좌표를 격자 키와 실제 위치 양쪽으로 저장하는 이유: 격자 키는 점유 판정과 실내 판정에 필요하고,
    /// 실제 위치는 지형 높이에 맞춰 놓은 1층 바닥의 y를 원래대로 되살리는 데 필요하다.
    /// </summary>
    [System.Serializable]
    public class BuildPieceSaveEntry
    {
        public int type;
        public int cellX;
        public int cellZ;
        public int level;
        public int axis;
        public float posX;
        public float posY;
        public float posZ;
        public float yaw;

        /// <summary>
        /// [배치 26 추가] 0 = 지면(좌표가 곧 월드) · 1 = 뗏목 갑판(좌표가 갑판 로컬).
        /// 이 필드가 없는 옛 세이브는 JsonUtility가 0으로 채우고, 그게 정확히 옛 동작이다.
        /// </summary>
        public int space;

        /// <summary>
        /// [건축 4티어 추가 - **맨 끝에 추가만 했다**] 부품 티어(1=나무 2=돌 3=강철 4=대리석).
        /// 이 필드가 없는 옛 세이브는 JsonUtility가 0으로 채우는데, 복원(CreatePieceFromEntry)이
        /// BuildPieceCatalog.ClampPieceTier로 **0을 1(나무)로 해석**하므로 옛 세이브의 부품은 전부
        /// 1티어로 그대로 되살아난다(파괴되지 않는다).
        /// </summary>
        public int tier;

        /// <summary>
        /// [뗏목 v4 추가 - **맨 끝에 추가만 했다**] 갑판 조각의 소속 뗏목 식별자(RaftStructure.RaftId).
        /// 지면 조각은 ""다. 이 필드가 없는 옛 세이브도 ""로 읽히는데, 그 시절에는 뗏목이 한 대뿐이라
        /// 복원이 대표 뗏목에 귀속시키는 것이 정확히 옛 동작이다(ResolveSavedRaft).
        /// </summary>
        public string raftId;
    }

    /// <summary>
    /// 건축물 전체 저장 데이터. JsonUtility는 최상위 리스트를 직렬화하지 못해 감싸는 클래스가 필요하다.
    /// version은 앞으로 형식이 바뀔 때 분기하기 위한 자리다(지금은 읽기만 하고 쓰지 않는다).
    /// </summary>
    [System.Serializable]
    public class BuildStructureSaveData
    {
        public int version = 1;
        public List<BuildPieceSaveEntry> pieces = new List<BuildPieceSaveEntry>();
    }
}
