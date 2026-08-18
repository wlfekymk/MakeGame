using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using MakeGame.Data;
using MakeGame.Player;

namespace MakeGame.Systems
{
    /// <summary>
    /// 뗏목에 장착할 수 있는 부품. 비트 플래그라 여러 개를 동시에 들고 다닐 수 있고, 세이브에는
    /// 정수 하나로 나간다(SaveData.raftInstalledParts).
    ///
    /// [Stranded Deep 대응] 그 게임의 뗏목은 "바닥판(raft floor)을 이어 붙여 넓히고, 그 위에
    /// 돛(sail) · 키(rudder) · 닻(anchor) · 노(paddle) · 모터(motor)를 얹는" 구조다. 부품 자체가
    /// 진행 단계가 아니라 **기능**이라, 어떤 조합을 갖췄느냐가 곧 "어디까지 나갈 수 있는가"를 정한다.
    /// 그래서 단계(1/2/3) 대신 플래그로 둔다 - 노만 달아 근해를 오가는 뗏목과 돛+키로 대양에 나가는
    /// 뗏목이 서로 다른 상태라는 것을 타입 수준에서 표현할 수 있다.
    /// </summary>
    [System.Flags]
    public enum RaftPart
    {
        None = 0,

        /// <summary>돛. 바람으로 나아간다. 키와 함께 있어야 방향을 잡을 수 있다.</summary>
        Sail = 1 << 0,

        /// <summary>키(방향타). 돛이나 모터의 추진력을 방향으로 바꾼다.</summary>
        Rudder = 1 << 1,

        /// <summary>닻. 정박용. 항해 자체에는 필요 없지만 완성도(진행률)에 들어간다.</summary>
        Anchor = 1 << 2,

        /// <summary>노. 가장 원시적인 추진 수단. 이것만 있어도 근해 이동은 된다.</summary>
        Oar = 1 << 3,

        /// <summary>모터. 돛+키를 한꺼번에 대체하는 최상위 추진 수단.</summary>
        Motor = 1 << 4,
    }

    /// <summary>
    /// 바닥판 한 칸의 종류. Stranded Deep의 raft base(나무 뗏목 바닥 / 부표 / 드럼통)에 대응한다.
    ///
    /// [왜 종류가 필요한가] 그 게임에서 바닥판은 "넓이"만이 아니라 **부력**을 정하는 부품이다.
    /// 같은 한 칸이라도 통나무 바닥은 싸고 약하게, 드럼통 바닥은 비싸고 강하게 뜬다. 종류를 두면
    /// "재료가 없으니 일단 나무로 깔고, 금속조각이 모이면 드럼통으로 넓힌다"는 선택이 생긴다.
    ///
    /// 값은 **세이브에 그대로 나가는 정수**다(SaveData.raftBaseTiles). 순서를 바꾸거나 중간에
    /// 끼워 넣지 말 것 - 옛 세이브의 칸이 다른 종류로 되살아난다. 새 종류는 항상 끝에 붙인다.
    /// </summary>
    public enum RaftBaseTileKind
    {
        /// <summary>칸이 비어 있다(아직 놓지 않았다).</summary>
        None = 0,

        /// <summary>통나무 바닥판. 가장 싸다(나뭇가지+노끈). 부력 1.0.</summary>
        Wood = 1,

        /// <summary>부력통 바닥판. 회수한 부력통을 끼워 넣는다. 부력 1.6.</summary>
        Buoy = 2,

        /// <summary>드럼통 바닥판. 금속조각을 두들겨 만든다. 부력 2.0으로 가장 강하다.</summary>
        Barrel = 3,
    }

    /// <summary>
    /// 해안가에 세우는 뗏목 본체. 월드에 실제로 서 있는 구조물이자, 새 뗏목 시스템의 **상태 소유자**다.
    ///
    /// [이 파일이 하는 일]
    ///  1. 새 뗏목 진행 모델(바닥판 칸 수 + 장착 부품 플래그)을 들고 있고, 그것으로 항해 가능
    ///     (IsSeaworthy) / 탈출 준비(IsOceanReady) / 진행률(GetOverallProgress)을 판정한다.
    ///     섬 이동(IslandTravel) · 엔딩(EndingChecker) · 퀘스트(QuestSystem) · HUD가 전부 이 값을 읽는다.
    ///  2. 시작 섬 물가를 스스로 찾아 자리를 잡고(TryAnchorToShore), 파도에 맞춰 흔들리며
    ///     (UpdateWaveMotion) 갑판에 올라탄 플레이어를 함께 옮긴다(CarryRider).
    ///  3. 건축 시스템(BuildingSystem)에 갑판 계약(Active/DeckRoot/PlacedStructures/DeckSurfaceName/
    ///     HasDeck/DeckLocalSize/DeckTopLocalY/DeckRebuilt)을 제공한다. **이 8개는 이름·시그니처를
    ///     바꾸면 갑판 위 건축이 통째로 죽는다.**
    ///
    /// [예전 3단계 도면-작업대 시스템과의 관계] 없다. BoatConstructionSystem/BoatWorkbench/
    /// BoatBlueprintPickup/BoatBlueprintSpawner는 전부 삭제됐고, 진행도는 이제 이 컴포넌트가 직접
    /// 소유한다. 인스턴스 확보도 남이 해 주지 않는다 - 아래 Bootstrap이 씬 로드마다 스스로 만든다.
    ///
    /// [바닥판 · 부품을 실제로 놓는 API는 다음 웨이브의 몫이다] 여기 있는 AddBaseTile/InstallPart는
    /// 상태를 바꾸고 외형·이벤트까지 정확히 갱신하는 **동작하는 뼈대**이며, "무엇을 소모해서 어떤
    /// 상호작용으로 부르는가"(재료·상호작용·UI)는 아직 아무 데서도 호출하지 않는다.
    ///
    /// [3D 에셋 0개] 전 파츠를 GameObject.CreatePrimitive로 조립한다(StructureVisualBuilder 경유).
    /// 머티리얼은 5개만 만들어 전 파츠가 공유한다 - 상태가 바뀌면 파츠를 통째로 다시 만들기 때문에,
    /// 파츠마다 머티리얼을 만들면 SRP 배처가 죽는다(AGENT_BRIEF 4장).
    ///
    /// [배치] 시작 섬의 물가. TerrainSampler.SnapToGround로 실제 지형 높이를 재서 해안선을 찾는데,
    /// 이 헬퍼는 이름이 "Island_" 로 시작하는 콜라이더만 지형으로 인정한다는 점을 그대로 이용한다 -
    /// 바다 평면(Ocean)과 자원/위험요소에는 절대 스냅되지 않으므로, "레이가 아무것도 못 맞은 지점"
    /// = "섬 메시가 끝난 지점" = 물이다. 이 성질로 해안선을 찾는다(FindShoreDistance 참고).
    /// </summary>
    [DisallowMultipleComponent]
    public class RaftStructure : MonoBehaviour
    {
        // ── 치수 (전부 로컬 좌표. 로컬 +Z = 뱃머리 = 바다 쪽) ───────────────────────────
        /// <summary>
        /// 바닥판 격자의 칸 간격(m, XZ 공통). **실물 바닥판 모델의 발자국과 정확히 같은 값이어야 한다** -
        /// raft_base_wood / raft_base_barrel / raft_base_buoy / raft_floor 넷 다 XZ가 정확히 2.0×2.0이고
        /// 원점이 칸 중심이므로, 이 값이 2.0일 때만 이웃 칸이 틈 없이 맞물린다(0.1mm 오차도 이음매로 보인다).
        /// </summary>
        public const float BaseTilePitch = 2f;

        /// <summary>
        /// 고물~뱃머리 길이(로컬 Z). 바닥판을 전부 깔았을 때의 길이다.
        /// **격자에서 유도한다** - 예전에는 8.0을 직접 박아 두고 칸 길이를 8/4 = 2.0으로 나눠 썼는데,
        /// 그러면 칸 수를 바꾸는 순간 칸 크기가 모델(2.0m)과 어긋난다. 값은 종전과 같은 8.0이다.
        /// </summary>
        public const float DeckLength = BaseGridRows * BaseTilePitch;

        /// <summary>
        /// 좌현~우현 폭(로컬 X). 격자에서 유도한 4.0이다(예전 5.2 → 4.0).
        ///
        /// [왜 5.2에서 줄였나] 옛 값은 칸 폭이 5.2/2 = 2.6이라 2.0m짜리 실물 바닥판 모델을 깔면 열
        /// 사이에 0.6m 구멍이 남는다. 폭을 격자에서 유도해 칸을 정확히 2.0으로 맞추는 쪽을 골랐다.
        /// **갑판 위 건축 격자는 이 변경에 영향받지 않는다**: BuildingSystem의 셀 경계는 원점 기준
        /// 2m 배수라(cellX * 2), 반폭이 2.6이든 2.0이든 온전히 들어가는 열은 cellX ∈ {-1, 0} 두 개로
        /// 같다(IsDeckCellInBounds). 즉 만재 갑판의 건축 가능 칸은 종전과 동일한 2 × 4 = 8칸이다.
        /// </summary>
        public const float DeckWidth = BaseGridColumns * BaseTilePitch;

        /// <summary>
        /// 완성 갑판 윗면 높이(해수면 기준). 플레이어가 서는 면이자 건축 시스템이 집을 올리는 면이다.
        /// **널판 상수에서 유도한다** - 널 높이/두께를 바꿨는데 이 값이 옛날 그대로면 갑판 위 건축물이
        /// 허공에 뜨거나 바닥에 박힌다. 값 자체는 종전과 같은 0.72다(옛 세이브의 갑판 조각이 같은
        /// 높이로 되살아나야 하므로 이 값은 바꾸지 않았다).
        /// </summary>
        public const float DeckSurfaceY = DeckPlankY + DeckPlankThickness * 0.5f;

        /// <summary>선체 통나무 지름과 중심 높이. 지름 0.8 / 중심 0.1 이면 윗면이 정확히 0.5다.</summary>
        private const float LogDiameter = 0.8f;
        private const float LogCenterY = 0.1f;

        /// <summary>가로보(통나무를 가로질러 묶는 각재) 중심 높이. 통나무 윗면(0.5)에 얹힌다.</summary>
        private const float CrossbeamY = 0.56f;

        /// <summary>바닥판 중심 높이/두께. 0.67 ± 0.05 → 윗면이 DeckSurfaceY(0.72)와 일치한다.</summary>
        private const float DeckPlankY = 0.67f;
        private const float DeckPlankThickness = 0.10f;

        /// <summary>갑판 바닥재(raft_floor) 실물 모델의 두께(m). 모델 bbox 실측값이다.</summary>
        private const float FloorModelThickness = 0.08f;

        /// <summary>
        /// 바닥판(골조) 윗면의 로컬 y. 갑판 바닥재를 여기에 얹으면 그 윗면이 정확히 DeckSurfaceY(0.72)다.
        ///
        /// **바닥판 종류가 달라도 이 값은 같다**(나무 0.28 / 부력통 0.45 / 드럼통 0.55는 전부 아래로
        /// 자란다). 두꺼운 바닥판일수록 물에 깊이 잠기고, 사람이 딛는 면은 항상 한 높이라는 뜻이다 -
        /// 종류마다 갑판 높이가 다르면 갑판 위 건축물(DeckTopLocalY 하나를 쓰는)이 통째로 어긋난다.
        /// </summary>
        private const float FrameTopY = DeckSurfaceY - FloorModelThickness;

        // ── 바닥판 격자 (새 뗏목 계약의 뼈대) ────────────────────────────────────────
        /// <summary>바닥판 격자의 좌우 칸 수(좌현/우현). 한 칸 폭은 DeckWidth / 이 값이다.</summary>
        public const int BaseGridColumns = 2;

        /// <summary>바닥판 격자의 앞뒤 칸 수(고물→뱃머리). 한 칸 길이는 DeckLength / 이 값이다.</summary>
        public const int BaseGridRows = 4;

        /// <summary>바닥판을 전부 깔았을 때의 칸 수. 격자 치수의 **단일 출처**다.</summary>
        public const int MaxBaseTiles = BaseGridColumns * BaseGridRows;

        /// <summary>
        /// 항해(IsSeaworthy)에 필요한 최소 바닥판 칸 수. 격자의 절반이다 - 사람 하나와 짐이 올라설
        /// 면적은 되지만 아직 대양에 나갈 크기는 아니라는 선.
        /// </summary>
        public const int SeaworthyTileCount = 4;

        /// <summary>
        /// 탈출(IsOceanReady)에 필요한 최소 바닥판 칸 수. 격자의 3/4이다.
        /// 전 칸(8)을 요구하지 않는 이유: 탈출 조건에는 이미 돛+키(또는 모터) · 비상식량 12 · 생수 12 ·
        /// 연료 1 · 15일 경과가 함께 걸려 있어, 여기서 한 칸까지 완벽을 요구하면 "다 했는데 왜 안 되지"
        /// 형태의 막힘만 늘고 난이도는 거의 오르지 않는다.
        /// </summary>
        public const int OceanReadyTileCount = 6;

        /// <summary>진행률(GetOverallProgress)에서 바닥판이 차지하는 몫. 나머지는 부품 몫이다.</summary>
        private const float BaseTileProgressWeight = 0.5f;

        /// <summary>진행률에 세는 부품. 모터는 돛+키의 **대체재**라 중복으로 세지 않는다.</summary>
        private static readonly RaftPart[] ProgressParts =
        {
            RaftPart.Sail, RaftPart.Rudder, RaftPart.Anchor, RaftPart.Oar,
        };

        /// <summary>건축 전용 컨테이너 이름. 갑판 재생성이 절대로 지우지 않는 유일한 자식이다.</summary>
        public const string PlacedStructuresName = "PlacedStructures";

        /// <summary>
        /// 갑판 윗면을 대표하는 콜라이더의 이름. **DeckRoot의 자식**이어야 한다는 점이 이 오브젝트의
        /// 존재 이유 전부다 - BuildingSystem.IsDeckCollider가 "맞은 콜라이더의 부모를 거슬러 DeckRoot에
        /// 닿는가"로 Deck 공간을 판정하기 때문이다.
        /// </summary>
        public const string DeckSurfaceName = "DeckSurface";

        /// <summary>승선 발판이 고물에서 해변 쪽으로 뻗는 수평 거리.</summary>
        private const float RampRun = 2.2f;

        /// <summary>
        /// 승선 발판 밑동을 실측 해변 높이보다 얼마나 더 아래(모래 속)에 박을지(m).
        /// 잔잔한 날의 상하 흔들림 실측 최대치(0.35m)에 맞췄다 - 근거는 TryAnchorToShore 주석.
        /// </summary>
        private const float RampFootDig = 0.35f;

        [Tooltip("해안선에서 바다 쪽으로 얼마나 밀어낼지(미터). 고물이 물가에 살짝 걸치도록 잡은 값이다.")]
        public float shoreOutwardOffset = 0.2f;

        [Tooltip("상태를 다시 읽어 외형을 맞추는 주기(초). 이벤트를 놓치는 경로(F9 불러오기)의 안전망이다.")]
        public float refreshInterval = 0.2f;

        // ── 파도 흔들림 (OceanWaves 연동) ──────────────────────────────────────────
        [Header("파도 흔들림")]
        [Tooltip("파도에 따라 뗏목이 뜨고 기울지. 끄면 예전처럼 고정된 자리에 가만히 있는다.")]
        public bool waveMotionEnabled = true;

        /// <summary>
        /// ★ 이 값은 PlayerController.oceanWaveFollowScale(씬 값 0.75)과 **반드시 같아야 한다**. ★
        /// 갑판 위에 선 플레이어의 수영 판정 수면은 waterLevel + 파고 × oceanWaveFollowScale이고,
        /// 갑판 높이는 뗏목 상하 이동 = 파고 × waveHeaveScale을 탄다. 두 배율이 같으면 골에서
        /// 갑판이 내려간 만큼 판정 수면도 같이 내려가 **서로 정확히 상쇄**되므로, 진폭을 아무리
        /// 올려도 "갑판 윗면 − 판정 수면"이 갑판 높이 0.72m 근처에서 유지된다.
        /// 이 값만 1.0으로 올리면(= 파도를 더 충실히 따라가면) 상쇄가 깨져 오히려 침수 쪽으로
        /// 0.5m 이상 손해를 본다 - 실제로 시뮬레이션에서 폭풍 최소 여유가 +0.44 → −0.02m로 뒤집혔다.
        /// </summary>
        [Tooltip("파도 높이를 얼마나 따라갈지(1 = 그대로). PlayerController.oceanWaveFollowScale과 같은 값을" +
            " 유지해야 갑판이 침수 판정에 걸리지 않는다(코드 주석 참고).")]
        public float waveHeaveScale = 0.75f;

        [Tooltip("상하 흔들림 상한(m). 실측 최대치(폭풍 1.02m)보다 넉넉해야 한다 - 클램프가 걸리면" +
            " 움직임이 끊길 뿐 아니라 갑판↔판정 수면의 상쇄가 깨져 침수가 생긴다.")]
        public float maxHeaveMeters = 1.2f;

        [Tooltip("기울기 상한(도, 피치/롤 각각). 멀미·조작 불능 방지용 하드 리밋이다.")]
        public float maxTiltDegrees = 9f;

        [Tooltip("흔들림 저역통과 강도(1/초). 클수록 파도를 즉각 따라가고, 작을수록 둔하게 움직인다.")]
        public float waveMotionDamping = 6.5f;

        // ── 새 진행 상태 ──────────────────────────────────────────────────────────
        /// <summary>
        /// 해안에 실제로 놓인 바닥판 칸 수(0 ~ MaxBaseTiles). 0이면 뗏목 자리만 잡혀 있고 아직
        /// 아무것도 놓이지 않은 상태다. **직접 대입하지 말 것** - SetBaseTileCount/AddBaseTile을 쓴다
        /// (외형 재생성과 ProgressChanged 발행이 거기에 묶여 있다).
        /// </summary>
        private int baseTileCount;

        /// <summary>
        /// 칸별 구성. 인덱스 = 격자 순번(고물 좌현부터 BaseGridColumns 단위로 채워진다)이고,
        /// 값은 **세이브에 그대로 나가는 코드**다: 하위 3비트 = RaftBaseTileKind, 8비트 = 갑판 바닥재.
        /// baseTileCount보다 뒤쪽 항목은 항상 0(없는 칸)이다.
        /// </summary>
        private readonly int[] baseTiles = new int[MaxBaseTiles];

        /// <summary>칸 코드에서 "갑판 바닥재가 깔렸다"를 나타내는 비트. 종류 값(1~3)과 겹치지 않는다.</summary>
        private const int FloorBit = 8;

        /// <summary>종류 값을 꺼내는 마스크(하위 3비트 = 0~7).</summary>
        private const int KindMask = 7;

        /// <summary>장착된 부품 플래그. 위와 같은 이유로 InstallPart/RemovePart/ApplySavedState로만 바꾼다.</summary>
        private RaftPart installedParts = RaftPart.None;

        /// <summary>지금 화면에 지어져 있는 상태의 지문. 이것이 그대로면 외형을 다시 만들지 않는다.</summary>
        private int builtSignature = -1;

        /// <summary>물가 위치/방향을 확정했는지. 확정 전에는 파츠를 만들지 않는다(엉뚱한 데 지어지지 않게).</summary>
        private bool anchored;

        private float refreshTimer;

        // ── 파도 흔들림 상태 ──────────────────────────────────────────────────────
        /// <summary>정박이 확정된 시점의 기준 위치/회전. 파도 흔들림은 항상 이 기준에 대한 오프셋이다.</summary>
        private Vector3 anchorPosition;
        private Quaternion anchorRotation = Quaternion.identity;

        /// <summary>저역통과를 거친 현재 오프셋(m, 도, 도).</summary>
        private float smoothedHeave;
        private float smoothedPitchDeg;
        private float smoothedRollDeg;

        /// <summary>갑판에 올라탄 플레이어를 함께 옮기기 위한 캐시. 없으면 주기적으로 다시 찾는다.</summary>
        private CharacterController riderController;
        private float riderRescanTimer;

        private WorldMapManager worldMap;
        private Transform visualRoot;
        private BoxCollider hullCollider;

        /// <summary>
        /// 항해 컴포넌트(같은 GameObject). Awake에서 확보하며, 매 프레임 UpdateWaveMotion **직전에**
        /// TickNavigation()을 부른다. 자기 Update를 두지 않고 여기서 부르는 이유는 순서 때문이다 -
        /// 항해가 기준 자세를 옮긴 뒤에 파도가 그 위에 흔들림을 얹어야 CarryRider가 한 번의 Move로
        /// 두 움직임을 함께 전달한다(순서가 뒤집히면 파도가 한 프레임 낡은 기준으로 계산된다).
        /// </summary>
        private RaftSailing sailing;

        // ── 건축 시스템 계약 ────────────────────────────────────────────────────────
        private static RaftStructure activeInstance;

        /// <summary>씬 로드 훅을 이미 걸었는지. 도메인 리로드를 꺼도 이벤트가 두 번 걸리지 않게 한다.</summary>
        private static bool bootstrapHooked;

        /// <summary>갑판 좌표계의 뿌리. 상태가 바뀌어도 **절대 파괴되지 않는다**.</summary>
        private Transform deckRoot;

        /// <summary>건축 시스템이 소유하는 컨테이너. 여기 있는 것은 갑판 재생성에서 살아남는다.</summary>
        private Transform placedStructures;

        /// <summary>
        /// 갑판 윗면 콜라이더(DeckRoot의 자식). 선체 BoxCollider 안에 완전히 들어가는 얇은 판이라
        /// 물리적으로는 아무것도 바꾸지 않고, 오직 "이 히트는 갑판이다"를 건축 시스템에 알리는 표식이다.
        /// </summary>
        private BoxCollider deckSurfaceCollider;

        /// <summary>
        /// 씬에 살아 있는 뗏목. 없으면 null이다.
        /// 인스턴스 확보는 이 클래스의 Bootstrap/EnsureInstance가 담당한다(중복 방지 포함) -
        /// 여기서는 "먼저 깨어난 쪽이 이긴다"만 지킨다. 늦게 깨어난 중복은 스스로 비활성화한다.
        /// </summary>
        public static RaftStructure Active => activeInstance != null ? activeInstance : null;

        /// <summary>
        /// 갑판 위에 물건을 붙일 부모. 이 밑에 두면 뗏목 좌표계를 따라간다(뗏목이 옮겨져도 같이 간다).
        /// 로컬 원점/회전은 뗏목 본체와 동일하므로, 갑판 윗면은 로컬 y = DeckTopLocalY다.
        /// </summary>
        public Transform DeckRoot
        {
            get
            {
                EnsureDeckRoot();
                return deckRoot;
            }
        }

        /// <summary>
        /// 갑판 위 건축물 전용 컨테이너(DeckRoot의 자식). 뗏목은 이걸 만들어 주기만 하고 절대 비우지 않는다.
        /// 내용물의 수명은 건축 시스템이 관리한다.
        /// </summary>
        public Transform PlacedStructures
        {
            get
            {
                EnsureDeckRoot();
                return placedStructures;
            }
        }

        /// <summary>
        /// 지금 **온전한 갑판이 깔려 있는가**. 통나무만 있거나 바닥판이 성기게 깔린 상태는 false다.
        /// 판정 자체는 DeckLocalSize에서 유도한다(둘이 갈라지지 않게).
        /// </summary>
        public bool HasDeck
        {
            get
            {
                Vector2 size = DeckLocalSize;
                return size.x > 0.01f && size.y > 0.01f;
            }
        }

        /// <summary>갑판 윗면의 로컬 y. 바닥판 상수에서 유도한 값이다.</summary>
        public float DeckTopLocalY => DeckSurfaceY;

        /// <summary>
        /// 실제로 바닥판이 깔린 갑판의 가로(x) x 세로(z), 로컬 미터. 갑판이 없으면 (0,0).
        /// 세로는 **원점 대칭으로 쓸 수 있는 길이**다(건축 시스템이 갑판 중심을 원점으로 보고 셀을 깐다).
        /// </summary>
        public Vector2 DeckLocalSize
        {
            get
            {
                GetDeckedSpan(baseTileCount, out float minZ, out float maxZ);
                float usable = 2f * Mathf.Min(-minZ, maxZ);
                return usable > 0.01f ? new Vector2(DeckWidth, usable) : Vector2.zero;
            }
        }

        /// <summary>
        /// 갑판(뗏목 파츠)이 다시 만들어졌을 때 발생한다.
        /// DeckRoot와 그 밑의 건축 컨테이너는 재생성 대상이 아니므로, 구독자는 보통 아무것도 할 게 없다.
        /// 갑판 높이/크기가 바뀌었을 수 있다는 신호로 쓴다.
        /// </summary>
        public event System.Action DeckRebuilt;

        /// <summary>
        /// 뗏목 진행 상태(바닥판 칸 수 / 장착 부품)가 바뀔 때마다 발생한다.
        /// 퀘스트·HUD·엔딩이 폴링 대신 이걸 구독해도 되도록 열어 둔다.
        /// [주의] SaveLoadController가 복원할 때는 ApplySavedState를 거치므로 이 이벤트가 정상 발행된다.
        /// </summary>
        public event System.Action ProgressChanged;

        // ── 새 뗏목 계약: 읽기 ────────────────────────────────────────────────────

        /// <summary>뗏목이 월드에 실재하는가(해안에 바닥판이 최소 1칸 놓였는가).</summary>
        public bool Exists => baseTileCount > 0;

        /// <summary>지금까지 깔린 바닥판 칸 수(0 ~ MaxBaseTiles).</summary>
        public int BaseTileCount => baseTileCount;

        /// <summary>장착된 부품 플래그 전체.</summary>
        public RaftPart InstalledParts => installedParts;

        // ── 칸별 구성 읽기 (제작 UI · 세이브 · 다음 웨이브의 항해 계산이 쓴다) ──────────

        /// <summary>격자 순번 index의 바닥판 종류. 아직 놓지 않은 칸은 None이다.</summary>
        public RaftBaseTileKind GetBaseTileKind(int index)
        {
            if (index < 0 || index >= baseTileCount)
                return RaftBaseTileKind.None;

            return (RaftBaseTileKind)(baseTiles[index] & KindMask);
        }

        /// <summary>격자 순번 index에 갑판 바닥재(걸어 다닐 판)가 깔려 있는가.</summary>
        public bool HasFloorAt(int index)
        {
            if (index < 0 || index >= baseTileCount)
                return false;

            return (baseTiles[index] & FloorBit) != 0;
        }

        /// <summary>갑판 바닥재가 깔린 칸 수. 제작 UI가 "바닥재 n/m"을 적을 때 쓴다.</summary>
        public int FloorTileCount
        {
            get
            {
                int count = 0;
                for (int i = 0; i < baseTileCount; i++)
                {
                    if ((baseTiles[i] & FloorBit) != 0)
                        count++;
                }
                return count;
            }
        }

        /// <summary>
        /// 바닥재를 다음에 깔 칸의 격자 순번. 전부 깔렸거나 바닥판이 없으면 -1이다.
        /// 제작 UI는 이 값 하나로 "지금 바닥재를 만들 수 있는가"를 판단한다(판정을 UI가 다시 만들지 않게).
        /// </summary>
        public int NextFloorlessTileIndex
        {
            get
            {
                for (int i = 0; i < baseTileCount; i++)
                {
                    if ((baseTiles[i] & FloorBit) == 0)
                        return i;
                }
                return -1;
            }
        }

        /// <summary>
        /// 종류별 부력 계수. 다음 웨이브(항해)가 적재량·속도·파도 안정성을 계산할 때 쓸 단일 출처다.
        /// 나무 1.0 / 부력통 1.6 / 드럼통 2.0 - 재료비(노끈 2 + 나뭇가지 4 / 부력통 1 / 금속조각 4)와
        /// 같은 순서로 오른다.
        /// </summary>
        public static float GetBuoyancy(RaftBaseTileKind kind)
        {
            switch (kind)
            {
                case RaftBaseTileKind.Wood: return 1.0f;
                case RaftBaseTileKind.Buoy: return 1.6f;
                case RaftBaseTileKind.Barrel: return 2.0f;
                default: return 0f;
            }
        }

        /// <summary>
        /// 지금 뗏목의 총 부력(칸별 부력의 합). 나무만 8칸이면 8.0, 드럼통만 8칸이면 16.0이다.
        /// 항해 웨이브가 "적재 한계 / 파도에 견디는 정도"를 여기서 유도하도록 열어 둔다.
        /// </summary>
        public float TotalBuoyancy
        {
            get
            {
                float total = 0f;
                for (int i = 0; i < baseTileCount; i++)
                    total += GetBuoyancy((RaftBaseTileKind)(baseTiles[i] & KindMask));
                return total;
            }
        }

        /// <summary>바닥판 종류 이름(한국어). 제작 UI·프롬프트가 공유하는 단일 출처다.</summary>
        public static string GetBaseTileKindName(RaftBaseTileKind kind)
        {
            switch (kind)
            {
                case RaftBaseTileKind.Wood: return "통나무 바닥판";
                case RaftBaseTileKind.Buoy: return "부력통 바닥판";
                case RaftBaseTileKind.Barrel: return "드럼통 바닥판";
                default: return "바닥판";
            }
        }

        /// <summary>지정한 부품(들)이 전부 장착돼 있는가.</summary>
        public bool HasPart(RaftPart part)
        {
            return part != RaftPart.None && (installedParts & part) == part;
        }

        /// <summary>
        /// 추진 수단을 갖췄는가. 노 하나 / 모터 / 돛+키 셋 중 아무것이나 하나면 된다
        /// (돛만 있고 키가 없으면 방향을 못 잡으므로 추진으로 치지 않는다).
        /// </summary>
        public bool HasPropulsion =>
            HasPart(RaftPart.Oar) || HasPart(RaftPart.Motor) || HasPart(RaftPart.Sail | RaftPart.Rudder);

        /// <summary>
        /// 근해를 항해할 수 있는가. 바닥판 SeaworthyTileCount칸 이상 + 추진 수단 하나.
        /// 섬 이동(IslandTravel)이 해류 우회 판정에 쓰는 값이다.
        /// </summary>
        public bool IsSeaworthy => baseTileCount >= SeaworthyTileCount && HasPropulsion;

        /// <summary>
        /// 대양으로 나갈(= 배 엔딩) 준비가 됐는가. 항해 가능에 더해 **방향을 유지할 수 있는** 추진
        /// (돛+키 또는 모터)과 바닥판 OceanReadyTileCount칸 이상을 요구한다. 노만으로는 안 된다.
        /// </summary>
        public bool IsOceanReady => IsSeaworthy
            && baseTileCount >= OceanReadyTileCount
            && (HasPart(RaftPart.Motor) || HasPart(RaftPart.Sail | RaftPart.Rudder));

        /// <summary>
        /// 뗏목 전체 진행률(0~1). 바닥판 채움 비율과 부품 장착 비율을 반씩 섞는다.
        /// UI(EndingUI 통계 / DebugHud)와 퀘스트 막대가 쓰는 유일한 진행률이다.
        /// </summary>
        public float GetOverallProgress()
        {
            float tileFraction = MaxBaseTiles > 0 ? (float)baseTileCount / MaxBaseTiles : 0f;

            int installed = 0;
            for (int i = 0; i < ProgressParts.Length; i++)
            {
                if (HasPart(ProgressParts[i]))
                    installed++;
            }
            float partFraction = ProgressParts.Length > 0 ? (float)installed / ProgressParts.Length : 0f;

            return Mathf.Clamp01(tileFraction * BaseTileProgressWeight
                + partFraction * (1f - BaseTileProgressWeight));
        }

        /// <summary>부품 이름(한국어). UI/퀘스트가 부족한 부품을 적을 때 쓴다.</summary>
        public static string GetPartName(RaftPart part)
        {
            switch (part)
            {
                case RaftPart.Sail: return "돛";
                case RaftPart.Rudder: return "키";
                case RaftPart.Anchor: return "닻";
                case RaftPart.Oar: return "노";
                case RaftPart.Motor: return "모터";
                default: return "부품";
            }
        }

        /// <summary>
        /// 지금 상태를 한 줄로 요약한다(예: "바닥판 4/8 · 돛 · 키"). HUD/퀘스트/디버그 패널이 공유해
        /// 서로 다른 문장을 만들지 않게 하는 단일 출처다.
        /// </summary>
        public string DescribeState()
        {
            var builder = new System.Text.StringBuilder();
            builder.Append("바닥판 ").Append(baseTileCount).Append('/').Append(MaxBaseTiles);

            // 갑판 바닥재는 아직 다 깔리지 않았을 때만 적는다 - 다 깔린 뗏목에서 "8/8"을 두 번
            // 보여줘 봐야 정보가 없고, 부품 이름이 밀려 한 줄에 안 들어간다.
            int floors = FloorTileCount;
            if (baseTileCount > 0 && floors < baseTileCount)
                builder.Append(" · 바닥재 ").Append(floors).Append('/').Append(baseTileCount);

            AppendPartIfInstalled(builder, RaftPart.Sail);
            AppendPartIfInstalled(builder, RaftPart.Rudder);
            AppendPartIfInstalled(builder, RaftPart.Oar);
            AppendPartIfInstalled(builder, RaftPart.Anchor);
            AppendPartIfInstalled(builder, RaftPart.Motor);

            return builder.ToString();
        }

        private void AppendPartIfInstalled(System.Text.StringBuilder builder, RaftPart part)
        {
            if (HasPart(part))
                builder.Append(" · ").Append(GetPartName(part));
        }

        // ── 새 뗏목 계약: 쓰기 (다음 웨이브가 재료/상호작용을 붙일 지점) ──────────────

        /// <summary>
        /// 바닥판을 한 칸 더 놓는다. 이미 꽉 찼으면 false.
        /// **재료 소모/상호작용은 여기 없다** - 무엇을 얼마나 소모할지는 다음 웨이브가 이 메서드를
        /// 부르는 쪽(제작 UI 또는 상호작용 컨트롤러)에서 정한다.
        /// </summary>
        public bool AddBaseTile()
        {
            return AddBaseTile(RaftBaseTileKind.Wood);
        }

        /// <summary>
        /// 지정한 종류의 바닥판을 한 칸 더 놓는다(격자의 다음 빈 순번). 이미 꽉 찼거나 종류가
        /// None이면 false. **재료 소모는 여기 없다** - 부르는 쪽(RaftBuildUI)이 먼저 소모한다.
        /// </summary>
        public bool AddBaseTile(RaftBaseTileKind kind)
        {
            if (kind == RaftBaseTileKind.None || baseTileCount >= MaxBaseTiles)
                return false;

            baseTiles[baseTileCount] = (int)kind & KindMask;
            baseTileCount++;

            RefreshVisual();
            NotifyProgressChanged();
            return true;
        }

        /// <summary>
        /// 갑판 바닥재를 한 칸 깐다(아직 안 깔린 칸 중 가장 앞 순번). 깔 곳이 없으면 false.
        /// 바닥재는 진행률·항해 판정에 들어가지 않는다 - 걸어 다닐 면을 고르게 만드는 마감재다.
        /// </summary>
        public bool AddFloorTile()
        {
            int index = NextFloorlessTileIndex;
            if (index < 0)
                return false;

            baseTiles[index] |= FloorBit;

            RefreshVisual();
            NotifyProgressChanged();
            return true;
        }

        /// <summary>
        /// 바닥판 칸 수를 직접 지정한다(0 ~ MaxBaseTiles로 잘린다). 값이 실제로 달라졌을 때만
        /// 외형을 다시 만들고 ProgressChanged를 발행한다.
        /// 새로 생기는 칸은 **통나무 바닥판 + 바닥재 있음**으로 채운다 - 이 경로는 칸별 구성을 모르는
        /// 호출부(옛 계약·치트/디버그)만 쓰므로, 갑판이 온전한 쪽으로 채워야 갑판 위 건축물이 뜨지 않는다.
        /// </summary>
        public void SetBaseTileCount(int count)
        {
            int clamped = Mathf.Clamp(count, 0, MaxBaseTiles);
            if (clamped == baseTileCount)
                return;

            for (int i = baseTileCount; i < clamped; i++)
                baseTiles[i] = (int)RaftBaseTileKind.Wood | FloorBit;
            for (int i = clamped; i < MaxBaseTiles; i++)
                baseTiles[i] = 0;

            baseTileCount = clamped;
            RefreshVisual();
            NotifyProgressChanged();
        }

        /// <summary>
        /// 부품을 장착한다. 이미 달려 있으면 false(재료를 두 번 먹지 않게 하는 신호다).
        /// 여러 플래그를 한 번에 넘기면 그중 하나라도 새로 붙는 경우에 true다.
        /// </summary>
        public bool InstallPart(RaftPart part)
        {
            if (part == RaftPart.None || HasPart(part))
                return false;

            installedParts |= part;
            RefreshVisual();
            NotifyProgressChanged();
            return true;
        }

        /// <summary>부품을 뗀다(파손/해체용). 실제로 떼어진 것이 있으면 true.</summary>
        public bool RemovePart(RaftPart part)
        {
            if (part == RaftPart.None || (installedParts & part) == RaftPart.None)
                return false;

            installedParts &= ~part;
            RefreshVisual();
            NotifyProgressChanged();
            return true;
        }

        /// <summary>
        /// 세이브에서 읽은 상태를 통째로 되돌린다(SaveLoadController 전용 경로).
        /// 값 대입 → 외형 재생성 → ProgressChanged 한 번으로 끝나므로, 불러온 그 프레임에 뗏목이
        /// 맞는 모습으로 서 있고 구독자(HUD/퀘스트)도 같은 프레임에 갱신된다.
        /// </summary>
        public void ApplySavedState(int savedBaseTileCount, RaftPart savedParts)
        {
            ApplySavedState(savedBaseTileCount, savedParts, null);
        }

        /// <summary>
        /// 칸별 구성까지 함께 되돌린다(세이브 v2 경로).
        ///
        /// [구버전 호환이 여기 한 곳에 모여 있다] savedTileCodes가 null이거나 savedBaseTileCount보다
        /// 짧으면, 모자란 칸은 **통나무 바닥판 + 갑판 바닥재**로 승격한다. 칸별 데이터가 없던 v1
        /// 세이브(raftBaseTileCount 하나만 있던 파일)가 정확히 이 경우다 - 그때 뗏목은 종류 구분이
        /// 없는 널판 한 종류였고 갑판도 온전했으므로, 그 상태와 가장 가까운 복원이 나무+바닥재다.
        /// 바닥재를 빼면 옛 세이브에서 갑판 위에 지어 둔 집이 8cm 허공에 뜬 것처럼 보인다.
        /// </summary>
        public void ApplySavedState(int savedBaseTileCount, RaftPart savedParts,
            System.Collections.Generic.IList<int> savedTileCodes)
        {
            baseTileCount = Mathf.Clamp(savedBaseTileCount, 0, MaxBaseTiles);
            installedParts = savedParts;

            int provided = savedTileCodes != null ? savedTileCodes.Count : 0;
            for (int i = 0; i < MaxBaseTiles; i++)
            {
                if (i >= baseTileCount)
                {
                    baseTiles[i] = 0;
                    continue;
                }

                int code = i < provided ? savedTileCodes[i] : 0;
                int kind = code & KindMask;

                // 알 수 없는/없는 종류는 통나무로 되돌린다(파일이 손상돼도 칸이 사라지지 않게).
                if (kind <= 0 || kind > (int)RaftBaseTileKind.Barrel)
                {
                    baseTiles[i] = (int)RaftBaseTileKind.Wood | FloorBit;
                    continue;
                }

                baseTiles[i] = kind | (code & FloorBit);
            }

            RefreshVisual();
            NotifyProgressChanged();
        }

        /// <summary>
        /// 지금 칸별 구성을 세이브용 정수 목록으로 옮겨 담는다(기존 내용은 지운다).
        /// 목록 길이 = BaseTileCount이고, 값은 ApplySavedState가 그대로 읽는 코드다.
        /// 새 List를 만들지 않고 호출부의 버퍼를 채우는 형태라 저장 때마다 쓰레기가 생기지 않는다.
        /// </summary>
        public void WriteBaseTileCodes(System.Collections.Generic.List<int> buffer)
        {
            if (buffer == null)
                return;

            buffer.Clear();
            for (int i = 0; i < baseTileCount; i++)
                buffer.Add(baseTiles[i]);
        }

        /// <summary>진행 상태 변화 통지. 외부에서 상태를 되돌린 뒤에도 부를 수 있도록 public이다.</summary>
        public void NotifyProgressChanged()
        {
            ProgressChanged?.Invoke();
        }

        // ── 수명 주기 ─────────────────────────────────────────────────────────────

        /// <summary>
        /// 씬이 로드될 때마다 뗏목 본체를 확보한다. 예전에는 BoatConstructionSystem.EnsureRaftStructure가
        /// 이 일을 했는데 그 허브가 사라졌으므로, 이 프로젝트에 이미 16곳 선례가 있는 자기 완결
        /// 부트스트랩(SubsystemRegistration + sceneLoaded + 중복 가드)으로 옮겼다.
        ///
        /// [씬 루트에 만드는 이유] WorldMapManager.RegenerateWorld(F9 불러오기)가 자기 자식을 전부
        /// 파괴한다. 그 밑에 두면 불러오기 때 뗏목이 함께 지워진다.
        ///
        /// [훅을 한 번만 거는 이유] 도메인 리로드를 끈 플레이 모드에서는 static 구독이 이전 실행에서
        /// 살아남는다. bootstrapHooked를 ResetStatics에서 **되돌리지 않는 것**이 핵심이다 - 리로드가
        /// 켜져 있으면 false로 초기화되어 다시 걸리고, 꺼져 있으면 true로 남아 중복 구독을 막는다.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            activeInstance = null;

            // 모델 캐시도 함께 비운다. 도메인 리로드를 끈 플레이 모드에서 이전 실행의 (이미 언로드된)
            // 메시를 들고 시작하면 파츠가 통째로 빈 채로 만들어진다.
            System.Array.Clear(baseTilePrimary, 0, baseTilePrimary.Length);
            System.Array.Clear(baseTileSecondary, 0, baseTileSecondary.Length);
            floorPrimary = null;
            floorSecondary = null;
            sailPrimary = null;
            sailSecondary = null;
            rudderPrimary = null;
            rudderSecondary = null;
            anchorPrimary = null;
            anchorSecondary = null;
            motorPrimary = null;
            motorSecondary = null;
            modelProbeFrame = -1;

            if (bootstrapHooked)
                return;

            bootstrapHooked = true;
            SceneManager.sceneLoaded += HandleSceneLoaded;
        }

        private static void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            EnsureInstance();
        }

        /// <summary>
        /// 뗏목 본체를 확보한다(이미 있으면 그대로 쓴다). 씬에 손으로 놓아 둔 것이 있으면 그것을 쓴다.
        /// 자리 확정(시작 섬 해안 찾기)은 인스턴스 자신이 한다 - 여기서 섬을 읽으려 하면 WorldMapManager가
        /// 아직 안 돌았을 수 있기 때문이다.
        /// </summary>
        public static RaftStructure EnsureInstance()
        {
            if (activeInstance != null)
                return activeInstance;

            var existing = FindAnyObjectByType<RaftStructure>();
            if (existing != null)
            {
                activeInstance = existing;
                return existing;
            }

            var go = new GameObject("RaftStructure");
            return go.AddComponent<RaftStructure>();
        }

        private void Awake()
        {
            // 중복 방지: 정상 경로(EnsureInstance)는 이미 하나만 만든다. 여기 걸린다면 씬에 손으로 놓은
            // 것 + 런타임 생성이 겹친 경우다. 먼저 깨어난 쪽이 이미 해안을 잡고 파츠를 지었을 수 있으므로,
            // 나중 것을 파괴하지 않고 조용히 재운다(파괴하면 씬 직렬화 값이 사라진다 - AGENT_BRIEF 2장 2번).
            if (activeInstance != null && activeInstance != this)
            {
                Debug.LogWarning($"[RaftStructure] 뗏목이 이미 있다. 중복 인스턴스 '{name}'을 비활성화한다.");
                enabled = false;
                return;
            }

            activeInstance = this;

            EnsureDeckRoot();
            EnsureMaterials();

            // 선체 콜라이더는 항상 존재한다(레이캐스트 대상이자 발판). 크기는 상태마다 갱신한다.
            hullCollider = GetComponent<BoxCollider>();
            if (hullCollider == null)
                hullCollider = gameObject.AddComponent<BoxCollider>();

            // 항해는 뗏목과 같은 오브젝트에 산다. 씬에 손으로 붙여 둘 대상이 아니므로(뗏목 자체가
            // 런타임 생성물이다) 여기서 확보한다.
            sailing = GetComponent<RaftSailing>();
            if (sailing == null)
                sailing = gameObject.AddComponent<RaftSailing>();

            ApplyHullCollider();
        }

        /// <summary>이 뗏목의 항해 컴포넌트. Awake 이후에는 절대 null이 아니다.</summary>
        public RaftSailing Sailing => sailing;

        private void OnEnable()
        {
            if (activeInstance == null)
                activeInstance = this;
        }

        private void OnDestroy()
        {
            if (activeInstance == this)
                activeInstance = null;
        }

        private void Update()
        {
            if (!anchored)
            {
                TryAnchorToShore();
                return;
            }

            // 항해 → 파도 순서가 계약이다(sailing 필드 주석 참고).
            if (sailing != null)
                sailing.TickNavigation();

            UpdateWaveMotion();

            // 외부에서 상태를 직접 되돌리는 경로(불러오기)를 위한 안전망. 상태 지문이 그대로면
            // 아무 일도 하지 않으므로 비용이 없다.
            // Time.timeScale이 0인 엔딩/사망 화면에서도 멈추지 않도록 unscaled를 쓴다.
            refreshTimer -= Time.unscaledDeltaTime;
            if (refreshTimer > 0f)
                return;

            refreshTimer = Mathf.Max(0.05f, refreshInterval);
            RefreshVisual();
        }

        /// <summary>
        /// 지금 상태에 맞는 외형을 확보한다. 상태 지문이 그대로면 아무것도 하지 않으므로 매 프레임
        /// 불러도 비용이 없다. 정박 전에는 자리가 확정되지 않아 짓지 않는다.
        /// </summary>
        public void RefreshVisual()
        {
            if (!anchored)
                return;

            int signature = ComputeStateSignature();
            if (signature == builtSignature)
                return;

            builtSignature = signature;
            RebuildVisual();
        }

        /// <summary>
        /// 바닥판 칸 수 + 칸별 구성 + 부품 플래그를 합친 상태 지문. 이 값이 바뀌면 외형을 다시 만든다.
        /// 칸별 구성을 지문에 넣지 않으면 "칸 수는 그대로인데 바닥재만 깐" 변화가 화면에 나타나지 않는다.
        /// 곱수 33은 홀수 소수라 (종류 0~3 | 바닥재 비트) 조합이 자리마다 섞여 충돌이 나지 않는다.
        /// </summary>
        private int ComputeStateSignature()
        {
            int signature = baseTileCount * 1024 + (int)installedParts;
            for (int i = 0; i < baseTileCount; i++)
                signature = signature * 33 + baseTiles[i];

            return signature;
        }

        // ─────────────────────────────────────────────────────────────────────────
        //  배치
        // ─────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// 시작 섬의 물가를 찾아 뗏목 자리를 잡는다. 섬이 아직 생성되지 않았으면(스크립트 실행 순서상
        /// WorldMapManager.Start가 나중일 수 있다) 아무것도 하지 않고 다음 프레임에 다시 시도한다.
        /// 자리가 잡혀도 바닥판이 0칸이면 아무것도 지어지지 않는다 - "해안에 뗏목을 지을 자리가
        /// 준비된 상태"가 새 시스템의 시작점이다.
        /// </summary>
        private void TryAnchorToShore()
        {
            if (worldMap == null)
                worldMap = FindAnyObjectByType<WorldMapManager>();

            if (worldMap == null)
            {
                // 월드 매니저가 없는 테스트 씬 등: 지금 있는 자리에 그대로 짓는다.
                rampFootLocalY = 0f;
                CaptureWaveAnchor();
                anchored = true;
                RefreshVisual();
                return;
            }

            IslandInstance startIsland = null;
            for (int i = 0; i < worldMap.islands.Count; i++)
            {
                var island = worldMap.islands[i];
                if (island == null)
                    continue;

                if (island.isStartingIsland)
                {
                    startIsland = island;
                    break;
                }

                if (startIsland == null && island.islandId == 0)
                    startIsland = island;
            }

            // 지형 오브젝트까지 실제로 만들어져 있어야 해안선을 잴 수 있다.
            if (startIsland == null || startIsland.placeholderObject == null)
                return;

            // 갓 만든 MeshCollider는 Physics.autoSyncTransforms가 기본 false라 아직 물리 씬에 없을 수
            // 있다. 이걸 빠뜨리면 아래 레이가 지형을 못 맞혀 해안선을 못 찾는다(AGENT_BRIEF 4장).
            Physics.SyncTransforms();

            Vector3 facing = ResolveShoreDirection();
            float radius = IslandSizeMetrics.GetTerrainRadius(startIsland.size);
            float shoreDistance = FindShoreDistance(startIsland.mapPosition, facing, radius);

            // 고물이 물가에 살짝 걸치도록 중심을 잡는다(중심 = 해안선 + 배 길이 절반 + 여유).
            Vector3 center = startIsland.mapPosition + facing * (shoreDistance + DeckLength * 0.5f + shoreOutwardOffset);
            center.y = worldMap.seaLevel;

            transform.SetPositionAndRotation(center, Quaternion.LookRotation(facing, Vector3.up));

            // 승선 발판이 닿을 해변 지점의 실제 높이를 잰다. terrainMaxHeight는 씬 직렬화 값(8)이
            // 코드 기본값(2.5)과 다르므로 상수로 가정하면 안 된다 - 반드시 실측한다.
            //
            // [파도 v5] 잰 높이보다 RampFootDig만큼 **더 아래**를 발판 밑동으로 잡는다(하한도 함께
            // 내렸다). 상하 흔들림 상한이 1.2m라, 발판을 모래 표면에 딱 맞춰 두면 파도 마루마다 발판
            // 끝이 통째로 떠올라 CharacterController.stepOffset(씬 값 0.3)을 넘는 턱이 생기기 때문이다.
            Vector3 rampFoot = center - facing * (DeckLength * 0.5f + RampRun);
            float groundY = SampleTerrainHeight(rampFoot, out bool hitTerrain);
            rampFootLocalY = hitTerrain
                ? Mathf.Clamp(groundY - center.y - RampFootDig, -0.9f, DeckSurfaceY - 0.08f)
                : -RampFootDig;

            CaptureWaveAnchor();
            anchored = true;
            RefreshVisual();
        }

        /// <summary>
        /// 파도 흔들림의 기준(정박 위치/회전)을 기억한다. 흔들림은 매 프레임 이 기준에 오프셋을 얹어
        /// **절대 좌표로 다시 대입**하는 방식이라, 오차가 프레임마다 누적되지 않는다(뗏목이 떠내려가지 않는다).
        ///
        /// 저역통과 상태를 0이 아니라 **지금 파도의 목표값으로 시드한다.** 0에서 출발하면 정박 직후
        /// 약 1초 동안 뗏목이 파도를 "따라잡는" 과도 구간이 생기는데, 진폭이 커진 뒤로는 그 구간의
        /// 갑판 침수 여유가 폭풍에서 −0.14m까지 내려간다(시뮬레이션 실측). 시드해 두면 첫 프레임부터
        /// 정상 상태라 그 구간 자체가 사라진다.
        /// </summary>
        private void CaptureWaveAnchor()
        {
            anchorPosition = transform.position;
            anchorRotation = transform.rotation;
            ComputeWaveTargets(out smoothedHeave, out smoothedPitchDeg, out smoothedRollDeg);
        }

        // ── 항해 계약 (RaftSailing이 쓰는 부분) ──────────────────────────────────────
        //
        // 파도 흔들림은 항상 "기준 자세 + 오프셋"이다. 항해는 그 **기준 자세**만 움직이고,
        // heave/tilt · 승선 이송 · 갑판 콜라이더는 한 글자도 건드리지 않는다.

        /// <summary>물가 자리를 확정했는가(해안선 탐색이 끝났는가).</summary>
        public bool PlacementResolved => anchored;

        /// <summary>파도 오프셋을 걷어낸 뗏목의 기준 위치(= 잔잔한 수면에서의 자리).</summary>
        public Vector3 HullBasePosition => anchorPosition;

        /// <summary>파도 기울기를 걷어낸 뗏목의 기준 회전(순수 방위각).</summary>
        public Quaternion HullBaseRotation => anchorRotation;

        /// <summary>
        /// 기준 자세를 옮긴다(항해 전용). 저역통과 상태는 건드리지 않는다 - 파도는 새 자리에서
        /// 이어서 계산되고, 다음 UpdateWaveMotion이 새 기준 위에 오프셋을 다시 얹는다.
        /// </summary>
        public void SetHullBase(Vector3 position, Quaternion rotation)
        {
            anchorPosition = position;
            anchorRotation = rotation;
        }

        /// <summary>지금 파도로 기울어진 정도(피치/롤 중 큰 쪽, 도). 전복 위험 판정의 단일 출처다.</summary>
        public float CurrentTiltDegrees =>
            Mathf.Max(Mathf.Abs(smoothedPitchDeg), Mathf.Abs(smoothedRollDeg));

        /// <summary>지금 플레이어가 갑판(또는 그 언저리)에 올라타 있는가. 판정은 CarryRider와 같다.</summary>
        public bool IsRiderAboard
        {
            get
            {
                EnsureRider();

                if (riderController == null || !riderController.enabled
                    || !riderController.gameObject.activeInHierarchy)
                    return false;

                return IsOnboardLocal(
                    Quaternion.Inverse(transform.rotation) * (riderController.transform.position - transform.position));
            }
        }

        /// <summary>
        /// 지금 파도에서의 목표 상하 이동/피치/롤(클램프까지 적용). 뱃머리·고물·좌현·우현 4점을
        /// 샘플해 평균으로 상하를, 앞뒤/좌우 차이로 기울기를 만든다. 힙 할당 없음(sin 16회).
        /// </summary>
        private void ComputeWaveTargets(out float heave, out float pitchDeg, out float rollDeg)
        {
            float halfLength = DeckLength * 0.5f;
            float halfWidth = DeckWidth * 0.5f;
            Vector3 forward = anchorRotation * Vector3.forward;
            Vector3 right = anchorRotation * Vector3.right;

            float yBow = OceanWaves.SampleHeight(anchorPosition + forward * halfLength);
            float yStern = OceanWaves.SampleHeight(anchorPosition - forward * halfLength);
            float yStarboard = OceanWaves.SampleHeight(anchorPosition + right * halfWidth);
            float yPort = OceanWaves.SampleHeight(anchorPosition - right * halfWidth);

            float scale = Mathf.Max(0f, waveHeaveScale);
            float heaveLimit = Mathf.Max(0f, maxHeaveMeters);
            float tiltLimit = Mathf.Max(0f, maxTiltDegrees);

            float average = (yBow + yStern + yStarboard + yPort) * 0.25f;
            heave = Mathf.Clamp((average - OceanWaves.SeaLevel) * scale, -heaveLimit, heaveLimit);

            // 부호: Unity에서 로컬 X축 양의 회전은 +Z(뱃머리)를 아래로 내린다 → 뱃머리가 높으면 음수.
            pitchDeg = Mathf.Clamp(
                -Mathf.Atan2((yBow - yStern) * scale, DeckLength) * Mathf.Rad2Deg, -tiltLimit, tiltLimit);
            // 로컬 Z축 양의 회전은 +X(우현)를 위로 올린다 → 우현이 높으면 양수.
            rollDeg = Mathf.Clamp(
                Mathf.Atan2((yStarboard - yPort) * scale, DeckWidth) * Mathf.Rad2Deg, -tiltLimit, tiltLimit);
        }

        /// <summary>
        /// 파도에 맞춰 뗏목을 위아래로 띄우고 살짝 기울인다(OceanWaves.SampleHeight 사용).
        ///
        /// [무엇을 움직이나] **뗏목 루트(transform) 하나만** 움직인다. 갑판(DeckRoot) · 건축 컨테이너
        /// (PlacedStructures / BuildDeckPieces) · 파츠(RaftVisual) · 선체·갑판 콜라이더가 전부 루트의
        /// 자식이고 로컬 좌표로 배치돼 있으므로, 갑판 위에 지은 집·상자는 뗏목과 통째로 같이 움직이며
        /// 1mm도 어긋나지 않는다. 세이브도 갑판 조각을 뗏목 로컬 좌표로 저장하므로 결과가 달라지지 않는다.
        ///
        /// [바닥판 0칸일 때] 아직 뗏목이 실재하지 않으므로 **아무것도 움직이지 않는다.** 자리만 잡힌
        /// 빈 루트가 파도를 타면, 그 위를 지나가는 플레이어를 CarryRider가 붙잡아 끌고 다니게 된다.
        ///
        /// [멀미/조작 방지] 기울기는 maxTiltDegrees(±9°)로, 상하 이동은 maxHeaveMeters(±1.2m)로 하드
        /// 클램프한다. 300초 × 24방위 × 60Hz 시뮬레이션 실측치는 맑음 피치 1.60°/롤 1.69°/상하 0.35m,
        /// 폭풍 피치 4.58°/롤 4.80°/상하 1.01m 로 두 클램프 모두 도달 빈도 0.000%다(순수 안전망).
        ///
        /// [정지 계약] 진행에 Time.deltaTime을 쓰고 파도 시계도 Time.time이므로, timeScale = 0인
        /// 타이틀/일시정지/엔딩에서는 바다와 함께 뗏목도 완전히 멈춘다.
        ///
        /// [갑판 침수] 갑판 윗면은 뗏목 원점 위 0.72m다. 뗏목이 골에 내려앉는 순간에도 그 자리의
        /// 파고와 뗏목 상하 이동이 **같은 배율(0.75)** 로 움직여 서로 상쇄된다(waveHeaveScale 주석).
        /// 갑판 25개 지점 × 300초 × 24방위 시뮬레이션에서 "갑판 윗면 − 플레이어 수영 판정 수면"의
        /// 최소 여유는 맑음 +0.64m · 폭풍 +0.44m다.
        /// </summary>
        private void UpdateWaveMotion()
        {
            // [항해] 파도가 꺼져 있거나 뗏목이 아직 없어도, **기준 자세가 움직였으면 반드시 반영해야
            // 한다**(항해가 옮긴 위치가 여기서 화면에 나가기 때문이다). 아무것도 달라지지 않았을
            // 때만 빠져나간다 - 그 경우가 예전 동작(정지)과 100% 같다.
            bool wavesActive = waveMotionEnabled && baseTileCount > 0;
            if (!wavesActive)
            {
                bool settled = smoothedHeave == 0f && smoothedPitchDeg == 0f && smoothedRollDeg == 0f;
                if (settled && transform.position == anchorPosition && transform.rotation == anchorRotation)
                    return;

                smoothedHeave = 0f;
                smoothedPitchDeg = 0f;
                smoothedRollDeg = 0f;
            }
            else
            {
                ComputeWaveTargets(out float targetHeave, out float targetPitch, out float targetRoll);

                // 지수 저역통과(프레임률 독립). deltaTime이 0이면(일시정지) 계수도 0이라 그대로 멈춘다.
                float blend = waveMotionDamping > 0f
                    ? 1f - Mathf.Exp(-waveMotionDamping * Time.deltaTime)
                    : 1f;
                smoothedHeave = Mathf.Lerp(smoothedHeave, targetHeave, blend);
                smoothedPitchDeg = Mathf.Lerp(smoothedPitchDeg, targetPitch, blend);
                smoothedRollDeg = Mathf.Lerp(smoothedRollDeg, targetRoll, blend);
            }

            Vector3 newPosition = anchorPosition + Vector3.up * smoothedHeave;
            Quaternion newRotation = anchorRotation * Quaternion.Euler(smoothedPitchDeg, 0f, smoothedRollDeg);

            // 플레이어를 **먼저** 옮긴 뒤 뗏목을 옮긴다. 순서를 뒤집으면 갑판 콜라이더가 캡슐을 파고든
            // 상태에서 CharacterController.Move가 호출되어 밀려나거나 끼일 수 있다.
            CarryRider(newPosition, newRotation);

            transform.SetPositionAndRotation(newPosition, newRotation);
        }

        /// <summary>
        /// 갑판에 올라타 있는 플레이어를 뗏목과 같은 양만큼 옮긴다.
        ///
        /// CharacterController는 움직이는 콜라이더에 밀려나지 않으므로(캐릭터 컨트롤러는 스스로
        /// Move한 만큼만 움직인다), 이 처리가 없으면 뗏목만 오르내리고 플레이어는 제자리에 남아
        /// 갑판을 뚫거나 허공에 뜬다. 플레이어의 **뗏목 로컬 좌표를 보존**하는 방식이라, 기울기까지
        /// 반영된 정확한 자리로 따라간다(회전은 건드리지 않는다 - 시야를 억지로 돌리면 조작감이 깨진다).
        ///
        /// 판정은 뗏목 로컬 상자 하나뿐이라 비용이 없고, 승선 중이 아니면 아무 일도 하지 않는다.
        /// </summary>
        private void CarryRider(Vector3 newPosition, Quaternion newRotation)
        {
            EnsureRider();

            if (riderController == null || !riderController.enabled || !riderController.gameObject.activeInHierarchy)
                return;

            Vector3 riderWorld = riderController.transform.position;
            // 강체 변환이므로 스케일과 무관하게 역회전 + 평행이동으로 로컬 좌표를 구한다.
            Vector3 local = Quaternion.Inverse(transform.rotation) * (riderWorld - transform.position);

            if (!IsOnboardLocal(local))
                return;

            Vector3 delta = (newRotation * local + newPosition) - riderWorld;
            if (delta.sqrMagnitude > 1e-10f)
                riderController.Move(delta);
        }

        /// <summary>
        /// 갑판 위 플레이어 참조를 확보한다. 프레임당 전역 검색을 하지 않도록 주기를 둔다.
        /// CarryRider와 IsRiderAboard가 함께 쓴다 - 파도 흔들림을 꺼 둔 구성에서도 승선 판정이
        /// 살아 있어야 조종에 들어갈 수 있기 때문이다.
        /// </summary>
        private void EnsureRider()
        {
            if (riderController != null)
                return;

            riderRescanTimer -= Time.unscaledDeltaTime;
            if (riderRescanTimer > 0f)
                return;

            riderRescanTimer = RiderRescanInterval;
            var player = FindAnyObjectByType<MakeGame.Player.PlayerController>();
            if (player != null)
                riderController = player.GetComponent<CharacterController>();
        }

        /// <summary>
        /// 뗏목 로컬 좌표 하나가 "올라타 있다"에 해당하는가. CarryRider와 IsRiderAboard(항해 진입
        /// 조건)가 **같은 판정**을 쓰도록 한 곳으로 뽑았다 - 둘이 갈라지면 "화면상 갑판에 서 있는데
        /// 조종에 못 들어간다"가 생긴다.
        /// </summary>
        private static bool IsOnboardLocal(Vector3 local)
        {
            return Mathf.Abs(local.x) <= DeckWidth * 0.5f + RiderBoundsMargin
                && Mathf.Abs(local.z) <= DeckLength * 0.5f + RiderBoundsMargin
                && local.y >= RiderMinLocalY
                && local.y <= DeckSurfaceY + RiderHeadroom;
        }

        /// <summary>승선 판정 상자의 여유(m). 난간 바깥에 반쯤 걸친 자세까지 태운다.</summary>
        private const float RiderBoundsMargin = 0.7f;

        /// <summary>
        /// 승선으로 인정하는 최저 로컬 높이(발 기준, m). 갑판 윗면이 0.72, 골조의 선체 윗면이 0.5이므로
        /// 0.25면 "올라타 있다"를 모두 덮으면서, 뗏목 옆에서 헤엄치는 상태(발이 수면 아래)는 배제한다.
        /// </summary>
        private const float RiderMinLocalY = 0.25f;

        /// <summary>승선 판정 상자의 높이 여유(m). 갑판 윗면 기준 - 점프 중에도 판정이 끊기지 않을 정도.</summary>
        private const float RiderHeadroom = 2.6f;

        /// <summary>플레이어 참조를 다시 찾는 주기(초). 프레임당 전역 검색을 하지 않기 위한 값이다.</summary>
        private const float RiderRescanInterval = 1f;

        /// <summary>승선 발판이 닿는 해변 높이(로컬 y). 지형 최대 높이가 씬 값으로 바뀌어도 따라가도록 실측한다.</summary>
        private float rampFootLocalY;

        /// <summary>
        /// 뗏목을 놓을 방향(섬 중심 → 물가). 플레이어가 게임을 시작할 때 바라보는 방향
        /// (WorldMapManager가 여객기 잔해의 정반대로 시선을 잡는다)과 같은 쪽으로 맞춘다.
        /// 잔해 오프셋 하나에서 두 값을 함께 유도하므로, 디렉터가 잔해를 옮겨도 관계가 유지된다.
        /// </summary>
        private Vector3 ResolveShoreDirection()
        {
            Vector3 facing = worldMap != null ? -worldMap.aircraftWreckOffset : Vector3.forward;
            facing.y = 0f;

            if (facing.sqrMagnitude < 0.0001f)
                facing = Vector3.forward;

            return facing.normalized;
        }

        /// <summary>
        /// 섬 중심에서 facing 방향으로 나아가며 "지형이 끝나는 거리"(= 해안선)를 찾는다.
        /// TerrainSampler.SnapToGround가 "Island_" 콜라이더만 인정하는 성질을 그대로 쓴다 -
        /// 지형을 못 맞히면 바다다. 지형 표면이 해수면 근처(0.2m 이하)로 내려가는 지점도 해안으로 본다.
        /// 아무 판정도 안 서면 규모별 지형 반지름을 그대로 쓴다(안전한 기본값).
        /// </summary>
        private float FindShoreDistance(Vector3 islandCenter, Vector3 facing, float radius)
        {
            float seaLevel = worldMap != null ? worldMap.seaLevel : 0f;

            for (float distance = radius * 0.85f; distance <= radius * 1.25f; distance += 0.5f)
            {
                Vector3 probe = islandCenter + facing * distance;
                float groundY = SampleTerrainHeight(probe, out bool hitTerrain);

                if (!hitTerrain)
                    return distance;

                if (groundY - seaLevel <= 0.2f)
                    return distance;
            }

            return radius;
        }

        /// <summary>
        /// 지정 XZ의 섬 지형 높이를 잰다. 지형에 맞지 않으면 hitTerrain이 false다.
        ///
        /// SnapToGround는 지형을 못 맞히면 **넘긴 위치를 그대로 돌려준다**. 그래서 절대 나올 수 없는
        /// 센티넬 y(-1000)를 넣어 보내고, 돌아온 y가 그대로면 "지형 없음"으로 판정한다.
        /// 센티넬을 쓰는 만큼 기본 레이 길이(위 60 / 아래 120)로는 지형까지 닿지 않으므로,
        /// 시작 높이/길이를 명시적으로 크게 넘긴다.
        ///
        /// [항해] public이다. RaftSailing의 좌초 판정이 **같은 규칙**으로 지형 높이를 재야 하기
        /// 때문이다(여기 복사본을 만들면 "Island_ 콜라이더만 지형" 규약이 두 벌이 된다).
        /// 상태를 바꾸지 않으므로 아무 때나 불러도 안전하다.
        /// </summary>
        public float SampleTerrainHeight(Vector3 position, out bool hitTerrain)
        {
            const float Sentinel = -1000f;

            Vector3 probe = new Vector3(position.x, Sentinel, position.z);
            Vector3 result = TerrainSampler.SnapToGround(probe, 1200f, 2400f);

            hitTerrain = result.y > Sentinel + 1f;
            return result.y;
        }

        // ─────────────────────────────────────────────────────────────────────────
        //  갑판 계약 (건축 시스템이 쓰는 부분)
        // ─────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// 갑판 뿌리와 건축 컨테이너를 확보한다. 둘 다 뗏목 파츠(visualRoot)와 **형제**라서
        /// RebuildVisual이 파츠를 통째로 지워도 살아남는다 - 갑판 위에 지은 집이 바닥판 확장에서
        /// 사라지지 않는 이유가 이 부모 관계 하나다.
        /// </summary>
        private void EnsureDeckRoot()
        {
            if (deckRoot == null)
            {
                var rootObject = new GameObject("DeckRoot");
                rootObject.transform.SetParent(transform, false);
                deckRoot = rootObject.transform;
            }

            if (placedStructures == null)
            {
                var container = new GameObject(PlacedStructuresName);
                container.transform.SetParent(deckRoot, false);
                placedStructures = container.transform;
            }

            if (deckSurfaceCollider == null)
            {
                var surface = new GameObject(DeckSurfaceName);
                surface.transform.SetParent(deckRoot, false);
                deckSurfaceCollider = surface.AddComponent<BoxCollider>();
                deckSurfaceCollider.enabled = false; // 바닥판이 깔리기 전에는 갑판이 없다
            }
        }

        /// <summary>
        /// 온전히 채워진 바닥판 **행**의 개수. 한 행(좌현+우현)이 다 차야 그 구간을 갑판으로 인정한다.
        /// 반만 깔린 행을 갑판으로 세면 건축 조각이 빈 칸 위 허공에 놓인다.
        /// GetDeckedSpan(건축 시스템에 알려 주는 크기)과 BuildBaseTiles(실제 파츠 배치)가 공유한다.
        /// </summary>
        private static int GetCompletedRows(int tileCount)
        {
            return Mathf.Clamp(tileCount, 0, MaxBaseTiles) / BaseGridColumns;
        }

        /// <summary>
        /// 바닥판이 실제로 덮은 로컬 z 구간. 바닥판은 고물(-Z)부터 채워지므로 도중 단계에서는 앞쪽이
        /// 비어 있다. 하나도 없으면 빈 구간(0,0)을 준다.
        /// </summary>
        private static void GetDeckedSpan(int tileCount, out float minZ, out float maxZ)
        {
            int rows = GetCompletedRows(tileCount);
            if (rows <= 0)
            {
                minZ = 0f;
                maxZ = 0f;
                return;
            }

            minZ = -DeckLength * 0.5f;
            maxZ = minZ + (DeckLength / BaseGridRows) * rows;
        }

        // ─────────────────────────────────────────────────────────────────────────
        //  외형 조립
        // ─────────────────────────────────────────────────────────────────────────

        // 공유 머티리얼 7개. 파츠 수와 무관하게 이것만 만든다.
        private Material hullWoodMaterial;
        private Material plankWoodMaterial;
        private Material fiberMaterial;
        private Material sailMaterial;
        private Material cargoMaterial;

        /// <summary>실물 모델의 `metal` 그룹(드럼통 테·닻·모터 몸통·바닥재 못)에 쓰는 금속색.</summary>
        private Material metalMaterial;

        /// <summary>"제작 예정지" 유령 칸에 쓰는 반투명 머티리얼(불투명 URP Lit은 알파를 버린다).</summary>
        private Material ghostMaterial;

        // ─────────────────────────────────────────────────────────────────────────
        //  실물 모델 (Resources/Models/raft_*.obj)
        // ─────────────────────────────────────────────────────────────────────────
        //
        // 로드 규칙은 이 프로젝트의 검증된 경로 그대로다(ResourceVisualLibrary.TryLoadTwoPartModel +
        // 프레임당 1회 프로브 가드 + SubsystemRegistration 리셋 훅):
        //  · Resources.Load는 **필드 초기자에서 부르지 않는다**(정적 생성자 시점이라 null이 온다).
        //  · 실패를 영구 래치하지 않는다 - 임포트가 한 프레임 늦어도 다음 프레임에 자연 복구된다.
        //  · 모델이 하나도 없으면 옛 프리미티브 조립으로 폴백해 뗏목이 "안 보이는" 상태가 되지 않는다.
        //
        // 모델 8종 전부 `o` 그룹이 2개다: 첫째 `wood`, 둘째 `metal`(단, raft_sail만 `cloth`).
        // TryLoadTwoPartModel은 이름 규칙(trunk/leaf 등)에 걸리는 것이 없으면 **`o` 등장 순서**로
        // 가르므로 primary = wood, secondary = metal/cloth가 그대로 나온다.
        // Unity 6.5의 실제 임포터는 보통 MeshFilter 1개 + 서브메시 2개로 병합해 오는데, 그때는
        // secondary가 null이고 primary.subMeshCount가 2라 sharedMaterials 두 장으로 칠한다
        // (SeabedFloraSpawner.PlaceCoral / MarineLifeSpawner와 같은 분기).

        /// <summary>바닥판 3종 모델(인덱스 = RaftBaseTileKind - 1). primary = wood 그룹.</summary>
        private static readonly Mesh[] baseTilePrimary = new Mesh[3];
        private static readonly Mesh[] baseTileSecondary = new Mesh[3];

        /// <summary>바닥판 3종의 리소스 경로(위 배열과 같은 순서).</summary>
        private static readonly string[] BaseTileModelPaths =
        {
            "Models/raft_base_wood", "Models/raft_base_buoy", "Models/raft_base_barrel",
        };

        /// <summary>바닥판 3종 모델의 실측 두께(m). 윗면을 FrameTopY에 맞추기 위한 값이다.</summary>
        private static readonly float[] BaseTileModelHeights = { 0.28f, 0.45f, 0.55f };

        private static Mesh floorPrimary;
        private static Mesh floorSecondary;
        private static Mesh sailPrimary;
        private static Mesh sailSecondary;
        private static Mesh rudderPrimary;
        private static Mesh rudderSecondary;
        private static Mesh anchorPrimary;
        private static Mesh anchorSecondary;
        private static Mesh motorPrimary;
        private static Mesh motorSecondary;

        /// <summary>
        /// 프레임당 1회 프로브 가드(SeabedFloraSpawner.probeFrame과 같은 규칙). 같은 프레임에
        /// 뗏목이 여러 번 재조립돼도 Resources.Load는 한 번만 나가고, 실패는 다음 프레임에 다시 시도된다.
        /// </summary>
        private static int modelProbeFrame = -1;

        /// <summary>
        /// 공유 머티리얼을 한 번만 만든다. 색은 전부 StructureVisualBuilder의 팔레트 상수에서 온다
        /// (새 색을 만들지 않는다 - ArtDirection 1장). 갑판 널만 Driftwood를 밝게 민 명도 변형인데,
        /// 색상각을 바꾸지 않으므로 팔레트 밖으로 나가지 않고 "선체 통나무 / 다듬은 널"을 구분해 준다.
        /// </summary>
        private void EnsureMaterials()
        {
            if (hullWoodMaterial != null)
                return;

            hullWoodMaterial = StructureVisualBuilder.CreateColorMaterial(StructureVisualBuilder.Driftwood, "wood");
            plankWoodMaterial = StructureVisualBuilder.CreateColorMaterial(
                Color.Lerp(StructureVisualBuilder.Driftwood, StructureVisualBuilder.SalvageMarkerWhite, 0.22f), "wood");
            fiberMaterial = StructureVisualBuilder.CreateColorMaterial(StructureVisualBuilder.PalmFiber, "leaf");
            sailMaterial = StructureVisualBuilder.CreateColorMaterial(StructureVisualBuilder.SalvageMarkerWhite, "noise");
            cargoMaterial = StructureVisualBuilder.CreateColorMaterial(StructureVisualBuilder.SupplyKhaki, "metal");
            metalMaterial = StructureVisualBuilder.CreateColorMaterial(StructureVisualBuilder.SalvageMetal, "metal");
            ghostMaterial = CreateGhostMaterial(StructureVisualBuilder.SalvageMarkerWhite);
        }

        /// <summary>
        /// URP Lit 머티리얼을 **실제로** 반투명으로 바꾼다. 불투명 패스는 알파를 버리므로 색의 알파만
        /// 낮춰서는 화면에 아무 변화도 없다 - 인스펙터에서 Surface Type을 Transparent로 바꿨을 때 URP가
        /// 내부적으로 하는 일(_Surface/_Blend, 블렌드 모드, ZWrite, 키워드, 렌더 큐, RenderType 태그)을
        /// 코드로 그대로 해 준다. BuildPieceVisualBuilder.CreateGhostMaterial과 같은 절차다
        /// (그쪽은 private이라 부를 수 없어 같은 규칙을 여기에 둔다 - 값이 갈라지지 않게 알파도 같은 0.38).
        /// </summary>
        private static Material CreateGhostMaterial(Color baseColor)
        {
            const float GhostAlpha = 0.38f;

            var material = StructureVisualBuilder.CreateColorMaterial(baseColor, "noise");
            var tinted = new Color(baseColor.r, baseColor.g, baseColor.b, GhostAlpha);

            material.color = tinted;
            if (material.HasProperty("_BaseColor"))
                material.SetColor("_BaseColor", tinted);

            if (material.HasProperty("_Surface"))
                material.SetFloat("_Surface", 1f);      // 0 = Opaque, 1 = Transparent
            if (material.HasProperty("_Blend"))
                material.SetFloat("_Blend", 0f);        // 0 = Alpha blend
            if (material.HasProperty("_AlphaClip"))
                material.SetFloat("_AlphaClip", 0f);
            if (material.HasProperty("_SrcBlend"))
                material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            if (material.HasProperty("_DstBlend"))
                material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            if (material.HasProperty("_ZWrite"))
                material.SetFloat("_ZWrite", 0f);

            material.SetOverrideTag("RenderType", "Transparent");
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.DisableKeyword("_ALPHATEST_ON");
            material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            material.SetShaderPassEnabled("ShadowCaster", false);

            return material;
        }

        /// <summary>
        /// 아직 못 얻은 뗏목 모델을 한 번 프로브한다. 전부 채워졌으면 즉시 돌아오고, 같은 프레임에
        /// 두 번 이상 로드하지 않는다(modelProbeFrame). 실패를 래치하지 않으므로 임포트가 늦어도
        /// 다음 재조립에서 자연 복구된다.
        /// </summary>
        private static void EnsureModelsLoaded()
        {
            bool anyMissing = floorPrimary == null || sailPrimary == null || rudderPrimary == null
                || anchorPrimary == null || motorPrimary == null;
            for (int i = 0; i < baseTilePrimary.Length && !anyMissing; i++)
                anyMissing = baseTilePrimary[i] == null;

            if (!anyMissing || modelProbeFrame == Time.frameCount)
                return;

            modelProbeFrame = Time.frameCount;

            for (int i = 0; i < BaseTileModelPaths.Length; i++)
            {
                if (baseTilePrimary[i] != null)
                    continue;

                if (ResourceVisualLibrary.TryLoadTwoPartModel(BaseTileModelPaths[i],
                        out Mesh wood, out Mesh metal))
                {
                    baseTilePrimary[i] = wood;
                    baseTileSecondary[i] = metal;   // 병합 임포트면 null - 서브메시 분기가 처리한다
                }
            }

            TryLoadPart("Models/raft_floor", ref floorPrimary, ref floorSecondary);
            TryLoadPart("Models/raft_sail", ref sailPrimary, ref sailSecondary);
            TryLoadPart("Models/raft_rudder", ref rudderPrimary, ref rudderSecondary);
            TryLoadPart("Models/raft_anchor", ref anchorPrimary, ref anchorSecondary);
            TryLoadPart("Models/raft_motor", ref motorPrimary, ref motorSecondary);
        }

        /// <summary>모델 하나를 (이미 있으면 건너뛰고) 프로브한다. EnsureModelsLoaded 전용 헬퍼.</summary>
        private static void TryLoadPart(string resourcePath, ref Mesh primary, ref Mesh secondary)
        {
            if (primary != null)
                return;

            if (ResourceVisualLibrary.TryLoadTwoPartModel(resourcePath, out Mesh first, out Mesh second))
            {
                primary = first;
                secondary = second;
            }
        }

        /// <summary>
        /// 두 색짜리 실물 모델 하나를 붙인다. 개별 메시 임포트면 파츠 2개(각각 한 색), 병합 임포트
        /// (서브메시 2)면 파츠 1개 + sharedMaterials 두 장이다. 어느 쪽이든 콜라이더는 생기지 않는다
        /// (StructureVisualBuilder.CreateMeshPart는 프리미티브를 거치지 않는다).
        /// </summary>
        private void CreateModelPart(string name, Mesh primary, Mesh secondary,
            Material primaryMaterial, Material secondaryMaterial,
            Vector3 localPosition, Quaternion localRotation)
        {
            if (primary == null)
                return;

            var part = StructureVisualBuilder.CreateMeshPart(visualRoot, name, primary,
                localPosition, Vector3.one, localRotation, primaryMaterial);

            if (secondary != null)
            {
                StructureVisualBuilder.CreateMeshPart(visualRoot, name + "_B", secondary,
                    localPosition, Vector3.one, localRotation, secondaryMaterial);
                return;
            }

            if (primary.subMeshCount < 2)
                return;

            var renderer = part.GetComponent<MeshRenderer>();
            if (renderer != null)
                renderer.sharedMaterials = new[] { primaryMaterial, secondaryMaterial };
        }

        /// <summary>
        /// 지금 상태(바닥판 칸 수 + 장착 부품)의 뗏목을 통째로 다시 만든다.
        /// 기존 파츠는 Destroy 전에 SetActive(false)를 먼저 부른다 - Destroy는 프레임 끝까지 지연되므로,
        /// 같은 프레임에 새로 만드는 승선 발판 콜라이더와 옛 것이 겹쳐 있는 시간을 없앤다(AGENT_BRIEF 4장).
        /// </summary>
        private void RebuildVisual()
        {
            EnsureMaterials();
            EnsureModelsLoaded();

            // 갑판 뿌리/건축 컨테이너는 여기서 절대 건드리지 않는다. 아래에서 지우는 것은
            // visualRoot(뗏목 자신의 파츠)뿐이고, DeckRoot는 그 형제라 재생성의 영향을 받지 않는다.
            EnsureDeckRoot();

            if (visualRoot != null)
            {
                visualRoot.gameObject.SetActive(false);
                Destroy(visualRoot.gameObject);
                visualRoot = null;
            }

            var rootObject = new GameObject("RaftVisual");
            rootObject.transform.SetParent(transform, false);
            visualRoot = rootObject.transform;

            if (baseTileCount <= 0)
            {
                // 바닥판 0칸: 뗏목이 아직 없다. 대신 **여기서 만들 수 있다**는 표시를 세운다
                // (밧줄로 묶은 말뚝 네 개 + 첫 칸 자리의 반투명 유령 바닥판) - 이게 없으면 해안
                // 어디서도 뗏목 UI를 열 수 없고, 플레이어는 뗏목이라는 기능의 존재조차 알 수 없다.
                BuildSiteMarker();
            }
            else
            {
                BuildHull();
                BuildLashings();
                BuildBaseTiles();
                BuildFloorPlanks();

                // 승선 발판은 바닥판이 한 칸이라도 있으면 놓는다. 선체 윗면이 이미 갑판 높이(0.72)라
                // CharacterController.stepOffset(씬 값 0.3)으로는 올라설 수 없어서, 발판이 없으면
                // "뗏목은 보이는데 탈 수가 없는" 상태가 된다.
                BuildBoardingRamp();

                // 난간과 보급품은 **온전한 갑판이 생긴 뒤**에만 올린다. 반만 깔린 행 위에 세우면
                // 빈 칸 위 허공에 뜬다(판정은 DeckLocalSize 하나에서 유도한다).
                if (HasDeck)
                {
                    BuildRailings();
                    BuildCargo();
                }

                BuildInstalledParts();
            }

            ApplyHullCollider();

            // 갑판 콜라이더가 방금 바뀌었다. 구독자가 이 프레임에 레이캐스트를 쏠 수 있으므로
            // 물리 씬을 먼저 맞춘다(Physics.autoSyncTransforms = false - AGENT_BRIEF 4장).
            Physics.SyncTransforms();

            DeckRebuilt?.Invoke();
        }

        /// <summary>
        /// 부력 통나무. 바닥판이 깔린 길이만큼만 깐다 - 바닥판 2칸짜리 뗏목 밑에 8m 통나무가 있으면
        /// "아직 만드는 중"이 보이지 않는다. 폭은 바닥판이 놓인 열만큼만 넓어진다.
        /// </summary>
        private void BuildHull()
        {
            GetHullExtent(out float minZ, out float length, out float minX, out float width);

            int logCount = Mathf.Max(1, Mathf.RoundToInt(width / (LogDiameter * 1.05f)));
            float spacing = width / logCount;

            for (int i = 0; i < logCount; i++)
            {
                float x = minX + spacing * (i + 0.5f);

                StructureVisualBuilder.CreateVisualPart(visualRoot, $"HullLog{i}", PrimitiveType.Cylinder,
                    new Vector3(x, LogCenterY, minZ + length * 0.5f),
                    new Vector3(LogDiameter, length * 0.5f, LogDiameter),
                    hullWoodMaterial, Quaternion.Euler(90f, 0f, 0f));
            }

            // 통나무를 가로질러 묶는 가로보. 앞뒤 끝에서 조금씩 들어온 자리에 둔다.
            for (int side = -1; side <= 1; side += 2)
            {
                float z = minZ + length * (side < 0 ? 0.15f : 0.85f);
                StructureVisualBuilder.CreateVisualPart(visualRoot, $"Crossbeam{side}", PrimitiveType.Cube,
                    new Vector3(minX + width * 0.5f, CrossbeamY, z),
                    new Vector3(width + 0.3f, 0.12f, 0.32f), hullWoodMaterial);
            }
        }

        /// <summary>
        /// 지금 깔린 바닥판이 차지하는 로컬 범위(z 시작/길이, x 시작/폭). 선체·묶음이 공유한다.
        /// 반만 찬 행도 실물로는 존재하므로 여기서는 **놓인 칸 전부**를 센다(갑판 판정과는 다르다).
        /// </summary>
        private void GetHullExtent(out float minZ, out float length, out float minX, out float width)
        {
            float rowLength = DeckLength / BaseGridRows;
            float columnWidth = DeckWidth / BaseGridColumns;

            int occupiedRows = Mathf.CeilToInt((float)baseTileCount / BaseGridColumns);
            occupiedRows = Mathf.Clamp(occupiedRows, 1, BaseGridRows);

            // 마지막 행이 반만 찼으면 그 행은 한 열만 있다. 폭은 "가장 넓은 행"을 따른다.
            bool anyFullRow = baseTileCount >= BaseGridColumns;
            int widestColumns = anyFullRow ? BaseGridColumns : 1;

            minZ = -DeckLength * 0.5f;
            length = rowLength * occupiedRows;
            minX = -DeckWidth * 0.5f;
            width = columnWidth * widestColumns;
        }

        /// <summary>통나무를 가로로 묶은 밧줄 띠. 통나무 윗면(0.5)을 감싸도록 얹는다.</summary>
        private void BuildLashings()
        {
            GetHullExtent(out float minZ, out float length, out float minX, out float width);

            const int LashingCount = 2;
            for (int i = 0; i < LashingCount; i++)
            {
                float z = minZ + length * (i + 0.5f) / LashingCount;
                StructureVisualBuilder.CreateVisualPart(visualRoot, $"Lashing{i}", PrimitiveType.Cube,
                    new Vector3(minX + width * 0.5f, 0.44f, z),
                    new Vector3(width + 0.12f, 0.14f, 0.18f), fiberMaterial);
            }
        }

        /// <summary>
        /// 바닥판. 격자(BaseGridColumns x BaseGridRows) 순서대로 고물(-Z) 왼쪽부터 채워진다 -
        /// 칸을 하나 놓을 때마다 눈앞에서 판이 하나씩 붙는 것이 보이는 것이 이 배치의 목적이다.
        /// </summary>
        private void BuildBaseTiles()
        {
            for (int i = 0; i < baseTileCount; i++)
            {
                RaftBaseTileKind kind = (RaftBaseTileKind)(baseTiles[i] & KindMask);
                int slot = (int)kind - 1;
                if (slot < 0 || slot >= baseTilePrimary.Length)
                    slot = 0;

                Vector3 center = GetBaseTileCenter(i);
                Mesh model = baseTilePrimary[slot];

                if (model != null)
                {
                    // 실물 모델은 원점이 **칸 중심 + 밑면**이다(bbox y가 0부터 시작한다). 종류마다
                    // 두께가 달라도 윗면이 항상 FrameTopY가 되도록 밑면을 두께만큼 내려 놓는다.
                    CreateModelPart($"BaseTile{i}_{kind}", model, baseTileSecondary[slot],
                        plankWoodMaterial, metalMaterial,
                        new Vector3(center.x, FrameTopY - BaseTileModelHeights[slot], center.z),
                        Quaternion.identity);
                    continue;
                }

                // 폴백(모델 임포트 전/실패): 예전 프리미티브 널판. 뗏목이 "안 보이는" 상태를 만들지 않는다.
                StructureVisualBuilder.CreateVisualPart(visualRoot, $"BaseTile{i}", PrimitiveType.Cube,
                    new Vector3(center.x, DeckPlankY, center.z),
                    new Vector3(BaseTilePitch - 0.06f, DeckPlankThickness, BaseTilePitch - 0.06f),
                    plankWoodMaterial);
            }
        }

        /// <summary>
        /// 격자 순번 i의 칸 중심(로컬 XZ, y는 0). 바닥판·바닥재·유령 표시가 전부 이 하나를 쓴다 -
        /// 좌표 계산이 두 벌이면 바닥재가 바닥판에서 살짝 어긋나 이음매가 벌어진다.
        /// 칸은 고물(-Z) 좌현(-X)부터 열 방향으로 채워진다.
        /// </summary>
        private static Vector3 GetBaseTileCenter(int index)
        {
            int row = index / BaseGridColumns;
            int column = index % BaseGridColumns;

            return new Vector3(
                -DeckWidth * 0.5f + BaseTilePitch * (column + 0.5f),
                0f,
                -DeckLength * 0.5f + BaseTilePitch * (row + 0.5f));
        }

        /// <summary>
        /// 갑판 바닥재(raft_floor). 바닥판 골조 윗면(FrameTopY)에 얹으면 그 윗면이 정확히
        /// DeckSurfaceY(0.72) = 플레이어가 딛는 면이 된다. 바닥재가 없는 칸은 8cm 낮은 골조가 노출된다.
        /// </summary>
        private void BuildFloorPlanks()
        {
            if (floorPrimary == null)
                return;

            for (int i = 0; i < baseTileCount; i++)
            {
                if ((baseTiles[i] & FloorBit) == 0)
                    continue;

                Vector3 center = GetBaseTileCenter(i);
                CreateModelPart($"Floor{i}", floorPrimary, floorSecondary,
                    plankWoodMaterial, metalMaterial,
                    new Vector3(center.x, FrameTopY, center.z), Quaternion.identity);
            }
        }

        /// <summary>
        /// "여기서 뗏목을 만든다" 표시(바닥판 0칸일 때만). 새 모델을 만들지 않는다 -
        /// 말뚝은 프리미티브(StructureVisualBuilder.CreateLashedPost), 유령 칸은 이미 있는
        /// raft_base_wood 메시를 반투명 머티리얼로 한 번 더 그린 것이다.
        ///
        /// 유령 칸은 **첫 칸이 실제로 놓일 자리**(격자 순번 0 = 고물 좌현)에 정확히 겹쳐 둔다.
        /// 이 오브젝트가 곧 상호작용 조준 대상이기도 하다(ApplyHullCollider의 0칸 분기 참고).
        /// </summary>
        private void BuildSiteMarker()
        {
            // 네 귀퉁이의 말뚝. 뗏목이 차지할 넓이를 눈으로 알려 준다(물 위 60cm).
            // 머티리얼은 공유본만 쓴다 - StructureVisualBuilder.CreateLashedPost는 Color 오버로드라
            // 말뚝마다 머티리얼을 새로 만든다(이 클래스가 피하려는 바로 그 비용이다).
            for (int sx = -1; sx <= 1; sx += 2)
            {
                for (int sz = -1; sz <= 1; sz += 2)
                {
                    Vector3 stakeAt = new Vector3(
                        sx * (DeckWidth * 0.5f - 0.12f), 0.3f, sz * (DeckLength * 0.5f - 0.12f));

                    var stake = StructureVisualBuilder.CreateVisualPart(visualRoot, $"SiteStake{sx}_{sz}",
                        PrimitiveType.Cube, stakeAt, new Vector3(0.16f, 1.2f, 0.16f), hullWoodMaterial);

                    StructureVisualBuilder.CreateVisualPart(stake.transform, "Lashing", PrimitiveType.Cube,
                        new Vector3(0f, 0.34f, 0f), new Vector3(1.35f, 0.09f, 1.35f), fiberMaterial);
                }
            }

            // 말뚝을 잇는 밧줄(윗변 네 줄). 말뚝만 있으면 "네 개의 기둥"으로 읽히고, 줄이 있어야
            // "여기가 한 구획"으로 읽힌다.
            float ropeY = 0.78f;
            float halfX = DeckWidth * 0.5f - 0.12f;
            float halfZ = DeckLength * 0.5f - 0.12f;
            for (int side = -1; side <= 1; side += 2)
            {
                StructureVisualBuilder.CreateVisualPart(visualRoot, $"SiteRopeX{side}", PrimitiveType.Cube,
                    new Vector3(side * halfX, ropeY, 0f), new Vector3(0.05f, 0.05f, halfZ * 2f), fiberMaterial);
                StructureVisualBuilder.CreateVisualPart(visualRoot, $"SiteRopeZ{side}", PrimitiveType.Cube,
                    new Vector3(0f, ropeY, side * halfZ), new Vector3(halfX * 2f, 0.05f, 0.05f), fiberMaterial);
            }

            // 첫 칸의 유령. 실물 통나무 바닥판을 **놓일 자리 그대로** 반투명하게 한 번 더 그린다
            // (두 색 그룹 모두 같은 유령 머티리얼 - 병합 임포트에서 서브메시 하나가 빠져 반쪽만
            // 보이는 일이 없게 CreateModelPart를 그대로 쓴다). 모델이 아직 없으면 같은 크기의 큐브다.
            Vector3 center = GetBaseTileCenter(0);
            if (baseTilePrimary[0] != null)
            {
                CreateModelPart("SiteGhostTile", baseTilePrimary[0], baseTileSecondary[0],
                    ghostMaterial, ghostMaterial,
                    new Vector3(center.x, FrameTopY - BaseTileModelHeights[0], center.z),
                    Quaternion.identity);
            }
            else
            {
                StructureVisualBuilder.CreateVisualPart(visualRoot, "SiteGhostTile", PrimitiveType.Cube,
                    new Vector3(center.x, FrameTopY - BaseTileModelHeights[0] * 0.5f, center.z),
                    new Vector3(BaseTilePitch - 0.06f, BaseTileModelHeights[0], BaseTilePitch - 0.06f),
                    ghostMaterial);
            }
        }

        /// <summary>
        /// 승선 발판. CharacterController의 stepOffset은 씬 값 0.3이라 갑판(0.72)에 그냥 올라설 수 없다.
        /// 해변 실측 높이보다 RampFootDig만큼 파묻은 밑동(rampFootLocalY)에서 갑판까지 이어지는
        /// 경사판을 놓고, 여기에만 콜라이더를 남긴다(파묻는 이유는 TryAnchorToShore 주석).
        /// slopeLimit(씬 값 45도)보다 훨씬 완만하므로 걸어서 올라갈 수 있다.
        /// </summary>
        private void BuildBoardingRamp()
        {
            float footY = Mathf.Min(rampFootLocalY, DeckSurfaceY - 0.08f);
            float rise = DeckSurfaceY - footY;
            float length = Mathf.Sqrt(RampRun * RampRun + rise * rise);
            float angle = Mathf.Atan2(rise, RampRun) * Mathf.Rad2Deg;

            var ramp = CreateSolidPart("BoardingRamp",
                new Vector3(0f, (DeckSurfaceY + footY) * 0.5f - 0.05f, -DeckLength * 0.5f - RampRun * 0.5f),
                new Vector3(1.8f, 0.12f, length), plankWoodMaterial,
                Quaternion.Euler(-angle, 0f, 0f));

            // 난간 대신 발판 양옆에 낮은 턱만 둔다(콜라이더 없음 - 시각 표시).
            for (int side = -1; side <= 1; side += 2)
            {
                StructureVisualBuilder.CreateVisualPart(ramp.transform, $"RampEdge{side}", PrimitiveType.Cube,
                    new Vector3(side * 0.46f, 0.6f, 0f), new Vector3(0.06f, 1.2f, 1f), hullWoodMaterial);
            }
        }

        /// <summary>
        /// 장착된 부품을 외형으로 옮긴다. 부품 하나 = 파츠 한 묶음이라, 다음 웨이브가 부품을 추가할 때
        /// 여기 case를 하나 늘리면 된다(진행 단계별 분기가 아니다).
        /// </summary>
        private void BuildInstalledParts()
        {
            if (HasPart(RaftPart.Sail))
                BuildMastAndSail();

            if (HasPart(RaftPart.Rudder))
                BuildRudder();

            if (HasPart(RaftPart.Oar))
                BuildOars();

            if (HasPart(RaftPart.Anchor))
                BuildAnchor();

            if (HasPart(RaftPart.Motor))
                BuildMotor();

            // 조타 자리는 "잡을 것"이 하나라도 달렸을 때 생긴다. 노만 있어도 생기는 것이 중요하다 -
            // Stranded Deep에서 노(paddle)가 곧 조종 수단이기 때문이다.
            if (HasSteeringStation)
                BuildHelmStation();
        }

        /// <summary>고물에서 잡을 것(노·키·모터)이 하나라도 달려 있는가. 조타 자리 생성 조건이다.</summary>
        public bool HasSteeringStation =>
            HasPart(RaftPart.Oar) || HasPart(RaftPart.Rudder) || HasPart(RaftPart.Motor);

        /// <summary>
        /// 조타 자리(고물 뒤편의 트리거 상자 하나). 실물 파츠를 새로 만들지 않는다 - 키·모터·노가
        /// 이미 그 자리에 서 있으므로, 여기서 필요한 것은 "그 자리를 조준했다"를 알리는 콜라이더뿐이다.
        ///
        /// **로컬 z를 고물 끝(-DeckLength/2)보다 뒤로 뺀다.** 갑판 셀은 |z| &lt;= DeckLength/2 안쪽에만
        /// 있으므로, 이렇게 두면 BuildingSystem.CastBuildRay가 이 상자를 "뗏목에 막힘"으로 보는 일이
        /// 갑판 건축을 방해하지 않는다(갑판 칸 위에 두면 그 칸에 집을 못 짓게 된다 - RaftHelm 주석).
        /// 트리거인 이유도 같은 주석에 있다(승선 발판이 이 아래를 지난다).
        /// </summary>
        private void BuildHelmStation()
        {
            var stationObject = new GameObject("HelmStation");
            stationObject.transform.SetParent(visualRoot, false);
            stationObject.transform.localPosition =
                new Vector3(0f, DeckSurfaceY + 0.55f, -DeckLength * 0.5f - 0.45f);

            var box = stationObject.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = new Vector3(2.4f, 1.6f, 0.8f);

            var helm = stationObject.AddComponent<RaftHelm>();
            helm.sailing = sailing != null ? sailing : GetComponent<RaftSailing>();
        }

        /// <summary>
        /// 돛대 + 돛(raft_sail 모델). 모델 원점은 **접지 중심**(밑면 y = 0)이라 갑판 윗면에 그대로
        /// 얹으면 된다. 폭 1.8m라 갑판 폭 4.0m 한가운데 세워도 양옆에 1.1m씩 통로가 남는다.
        /// 앞뒤 지지 밧줄만 프리미티브로 이어 붙인다(모델에 없는 부분 - 돛대와 갑판을 잇는 신호).
        /// 모델이 아직 없으면 예전 프리미티브 조립(BuildMastAndSailPrimitive)으로 폴백한다.
        /// </summary>
        private void BuildMastAndSail()
        {
            const float SailZ = 0.8f;

            if (sailPrimary == null)
            {
                BuildMastAndSailPrimitive();
                return;
            }

            CreateModelPart("Sail", sailPrimary, sailSecondary, hullWoodMaterial, sailMaterial,
                new Vector3(0f, DeckSurfaceY, SailZ), Quaternion.identity);

            StructureVisualBuilder.CreateVisualPart(visualRoot, "MastFootLashing", PrimitiveType.Cube,
                new Vector3(0f, DeckSurfaceY + 0.18f, SailZ),
                new Vector3(0.44f, 0.15f, 0.44f), fiberMaterial);

            // 모델 실측: 돛대 높이 3.2m. 꼭대기에서 고물/뱃머리로 지지줄을 내린다.
            Vector3 mastTop = new Vector3(0f, DeckSurfaceY + 3.1f, SailZ);
            BuildStay("StayAft", mastTop, new Vector3(0f, DeckSurfaceY + 0.1f, -DeckLength * 0.5f + 0.5f));
            BuildStay("StayFore", mastTop, new Vector3(0f, DeckSurfaceY + 0.1f, DeckLength * 0.5f - 0.5f));
        }

        /// <summary>돛 모델을 못 얻었을 때의 폴백(옛 프리미티브 돛대 + 활대 + 돛).</summary>
        private void BuildMastAndSailPrimitive()
        {
            const float MastHeight = 3.6f;
            const float MastZ = 0.6f;

            StructureVisualBuilder.CreateVisualPart(visualRoot, "Mast", PrimitiveType.Cube,
                new Vector3(0f, DeckSurfaceY + MastHeight * 0.5f, MastZ),
                new Vector3(0.26f, MastHeight, 0.26f), hullWoodMaterial);

            StructureVisualBuilder.CreateVisualPart(visualRoot, "MastFootLashing", PrimitiveType.Cube,
                new Vector3(0f, DeckSurfaceY + 0.18f, MastZ),
                new Vector3(0.44f, 0.15f, 0.44f), fiberMaterial);

            float yardY = DeckSurfaceY + MastHeight - 0.25f;

            StructureVisualBuilder.CreateVisualPart(visualRoot, "Yard", PrimitiveType.Cube,
                new Vector3(0f, yardY, MastZ), new Vector3(3.2f, 0.14f, 0.14f), hullWoodMaterial);

            StructureVisualBuilder.CreateVisualPart(visualRoot, "Sail", PrimitiveType.Cube,
                new Vector3(0f, yardY - 1.15f, MastZ + 0.08f),
                new Vector3(3.0f, 2.1f, 0.06f), sailMaterial);

            Vector3 mastTop = new Vector3(0f, DeckSurfaceY + MastHeight - 0.1f, MastZ);
            BuildStay("StayAft", mastTop, new Vector3(0f, DeckSurfaceY + 0.1f, -DeckLength * 0.5f + 0.5f));
            BuildStay("StayFore", mastTop, new Vector3(0f, DeckSurfaceY + 0.1f, DeckLength * 0.5f - 0.5f));
        }

        /// <summary>
        /// 고물 한가운데 붙는 키(raft_rudder 모델). 모델은 원점이 접지 중심이고 위로 1.4m 자란다
        /// (자루 끝 -Z 0.6m). 밑동을 해수면(로컬 y = -0.2)에 두면 날은 물에 잠기고 손잡이는 갑판
        /// 윗면(0.72)보다 위로 올라와 "잡을 수 있는 자루"로 읽힌다.
        /// </summary>
        private void BuildRudder()
        {
            if (rudderPrimary != null)
            {
                CreateModelPart("Rudder", rudderPrimary, rudderSecondary, hullWoodMaterial, metalMaterial,
                    new Vector3(0f, -0.2f, -DeckLength * 0.5f - 0.1f), Quaternion.identity);
                return;
            }

            BuildRudderPrimitive();
        }

        /// <summary>키 모델을 못 얻었을 때의 폴백(옛 프리미티브 방향타).</summary>
        private void BuildRudderPrimitive()
        {
            StructureVisualBuilder.CreateVisualPart(visualRoot, "RudderShaft", PrimitiveType.Cube,
                new Vector3(0.95f, DeckSurfaceY + 0.15f, -DeckLength * 0.5f + 0.25f),
                new Vector3(0.12f, 1.7f, 0.12f), hullWoodMaterial, Quaternion.Euler(38f, 0f, 0f));

            StructureVisualBuilder.CreateVisualPart(visualRoot, "RudderBlade", PrimitiveType.Cube,
                new Vector3(0.95f, -0.2f, -DeckLength * 0.5f - 0.6f),
                new Vector3(0.1f, 0.75f, 0.5f), hullWoodMaterial, Quaternion.Euler(38f, 0f, 0f));
        }

        /// <summary>좌우 뱃전에 걸쳐 둔 노 두 자루.</summary>
        private void BuildOars()
        {
            for (int side = -1; side <= 1; side += 2)
            {
                float x = side * (DeckWidth * 0.5f - 0.25f);

                StructureVisualBuilder.CreateVisualPart(visualRoot, $"OarShaft{side}", PrimitiveType.Cylinder,
                    new Vector3(x, DeckSurfaceY + 0.12f, -0.6f),
                    new Vector3(0.09f, 1.3f, 0.09f), hullWoodMaterial, Quaternion.Euler(90f, 0f, side * 8f));

                StructureVisualBuilder.CreateVisualPart(visualRoot, $"OarBlade{side}", PrimitiveType.Cube,
                    new Vector3(x, DeckSurfaceY + 0.12f, -2.2f),
                    new Vector3(0.28f, 0.05f, 0.7f), plankWoodMaterial, Quaternion.Euler(0f, 0f, side * 8f));
            }
        }

        /// <summary>
        /// 뱃머리 좌현 갑판에 얹은 닻(raft_anchor 모델, 0.6 × 0.8 × 0.6). 원점이 접지 중심이라
        /// 갑판 윗면에 그대로 놓는다. x = -1.2면 갑판 반폭 2.0 안에 여유 0.5m가 남는다.
        /// </summary>
        private void BuildAnchor()
        {
            if (anchorPrimary != null)
            {
                Vector3 anchorAt = new Vector3(-1.2f, DeckSurfaceY, DeckLength * 0.5f - 1.0f);

                CreateModelPart("Anchor", anchorPrimary, anchorSecondary, hullWoodMaterial, metalMaterial,
                    anchorAt, Quaternion.Euler(0f, 24f, 0f));

                StructureVisualBuilder.CreateVisualPart(visualRoot, "AnchorRope", PrimitiveType.Cylinder,
                    anchorAt + new Vector3(0.6f, 0.06f, 0f),
                    new Vector3(0.42f, 0.09f, 0.42f), fiberMaterial);
                return;
            }

            BuildAnchorPrimitive();
        }

        /// <summary>닻 모델을 못 얻었을 때의 폴백(옛 프리미티브 돌닻 + 밧줄 뭉치).</summary>
        private void BuildAnchorPrimitive()
        {
            Vector3 anchorSpot = new Vector3(-1.5f, DeckSurfaceY + 0.22f, DeckLength * 0.5f - 1.0f);

            StructureVisualBuilder.CreateVisualPart(visualRoot, "AnchorStone", PrimitiveType.Cube,
                anchorSpot, new Vector3(0.55f, 0.42f, 0.55f), cargoMaterial, Quaternion.Euler(0f, 24f, 0f));

            StructureVisualBuilder.CreateVisualPart(visualRoot, "AnchorRope", PrimitiveType.Cylinder,
                anchorSpot + new Vector3(0.55f, -0.06f, 0f),
                new Vector3(0.42f, 0.09f, 0.42f), fiberMaterial);
        }

        /// <summary>
        /// 고물 우현 뱃전에 매다는 선외기(raft_motor 모델). 원점이 접지 중심(= 프로펠러 끝)이고 위로
        /// 1.1m 자라며, 조종 손잡이(wood 그룹)가 +Z 쪽으로 0.77m 뻗는다. 밑동을 로컬 y = -0.35에 두면
        /// 프로펠러는 물속, 손잡이는 갑판 높이 근처(0.37~0.65)에 와서 "고물에서 잡는 자루"가 된다.
        /// 키(x = 0)와 자리가 겹치지 않도록 우현(x = +1.0)에 단다.
        /// </summary>
        private void BuildMotor()
        {
            if (motorPrimary != null)
            {
                CreateModelPart("Motor", motorPrimary, motorSecondary, hullWoodMaterial, metalMaterial,
                    new Vector3(1.0f, -0.35f, -DeckLength * 0.5f - 0.1f), Quaternion.identity);
                return;
            }

            BuildMotorPrimitive();
        }

        /// <summary>모터 모델을 못 얻었을 때의 폴백(옛 프리미티브 선외기).</summary>
        private void BuildMotorPrimitive()
        {
            Vector3 motorSpot = new Vector3(-0.95f, DeckSurfaceY + 0.3f, -DeckLength * 0.5f + 0.3f);

            StructureVisualBuilder.CreateVisualPart(visualRoot, "MotorBody", PrimitiveType.Cube,
                motorSpot, new Vector3(0.42f, 0.6f, 0.5f), cargoMaterial);

            StructureVisualBuilder.CreateVisualPart(visualRoot, "MotorShaft", PrimitiveType.Cylinder,
                motorSpot + new Vector3(0f, -0.55f, -0.25f),
                new Vector3(0.14f, 0.45f, 0.14f), hullWoodMaterial, Quaternion.Euler(20f, 0f, 0f));
        }

        /// <summary>두 점을 잇는 가는 밧줄 하나. 큐브의 로컬 +Y를 두 점 방향으로 돌려 세운다.</summary>
        private void BuildStay(string name, Vector3 from, Vector3 to)
        {
            Vector3 delta = to - from;
            float length = delta.magnitude;
            if (length < 0.01f)
                return;

            StructureVisualBuilder.CreateVisualPart(visualRoot, name, PrimitiveType.Cube,
                (from + to) * 0.5f, new Vector3(0.05f, length, 0.05f), fiberMaterial,
                Quaternion.FromToRotation(Vector3.up, delta / length));
        }

        /// <summary>
        /// 난간. 갑판으로 인정된 구간(GetDeckedSpan)에만 세운다.
        /// 콜라이더를 붙이지 않는다 - 붙이면 갑판에 올라간 플레이어가 갇힌다.
        /// </summary>
        private void BuildRailings()
        {
            GetDeckedSpan(baseTileCount, out float deckMinZ, out float deckMaxZ);

            float railY = DeckSurfaceY + 0.45f;
            float halfWidth = DeckWidth * 0.5f - 0.08f;
            float startZ = deckMinZ + 0.3f;
            float endZ = deckMaxZ - 0.3f;
            float length = endZ - startZ;
            if (length <= 0.2f)
                return;

            for (int side = -1; side <= 1; side += 2)
            {
                StructureVisualBuilder.CreateVisualPart(visualRoot, $"RailBar{side}", PrimitiveType.Cube,
                    new Vector3(side * halfWidth, railY, (startZ + endZ) * 0.5f),
                    new Vector3(0.09f, 0.09f, length), plankWoodMaterial);

                const int PostCount = 4;
                for (int i = 0; i < PostCount; i++)
                {
                    float z = startZ + length * i / (PostCount - 1);
                    StructureVisualBuilder.CreateVisualPart(visualRoot, $"RailPost{side}_{i}", PrimitiveType.Cube,
                        new Vector3(side * halfWidth, DeckSurfaceY + 0.22f, z),
                        new Vector3(0.11f, 0.45f, 0.11f), plankWoodMaterial);
                }
            }

            // 바닥판을 끝까지 깔았을 때만 뱃머리 난간이 닫힌다(완성 신호).
            if (baseTileCount >= MaxBaseTiles)
            {
                StructureVisualBuilder.CreateVisualPart(visualRoot, "BowRail", PrimitiveType.Cube,
                    new Vector3(0f, railY, endZ), new Vector3(DeckWidth - 0.16f, 0.09f, 0.09f), plankWoodMaterial);
            }
        }

        /// <summary>갑판 위 보급품. 갑판이 생기면 궤짝이, 바닥판을 다 깔면 물통이 하나 더 놓인다.</summary>
        private void BuildCargo()
        {
            StructureVisualBuilder.CreateVisualPart(visualRoot, "SupplyCrate", PrimitiveType.Cube,
                new Vector3(1.65f, DeckSurfaceY + 0.31f, -2.3f),
                new Vector3(0.62f, 0.62f, 0.62f), plankWoodMaterial, Quaternion.Euler(0f, 18f, 0f));

            if (baseTileCount < MaxBaseTiles)
                return;

            StructureVisualBuilder.CreateVisualPart(visualRoot, "SupplyBarrel", PrimitiveType.Cylinder,
                new Vector3(-1.65f, DeckSurfaceY + 0.35f, -2.6f),
                new Vector3(0.52f, 0.35f, 0.52f), cargoMaterial);
        }

        /// <summary>
        /// 선체 콜라이더를 현재 상태에 맞춘다. 이것이 (1) 플레이어가 올라서는 발판이자
        /// (2) 상호작용 레이캐스트가 맞는 대상이다.
        /// 바닥판이 0칸이면 뗏목이 실재하지 않으므로 콜라이더를 **끈다** - 켜 두면 아무것도 없는
        /// 물 위에 보이지 않는 벽이 선다.
        /// </summary>
        private void ApplyHullCollider()
        {
            if (hullCollider == null)
                return;

            if (baseTileCount <= 0)
            {
                // [제작 예정지] 예전에는 여기서 콜라이더를 껐다("아무것도 없는 물 위의 보이지 않는 벽"
                // 방지). 그런데 뗏목 제작 UI는 뗏목을 **조준해서** 여는 창이라, 0칸에서 콜라이더가
                // 없으면 첫 바닥판을 놓을 방법이 원리적으로 사라진다(닭-달걀).
                // 그래서 유령 첫 칸(BuildSiteMarker) 자리에만 한 칸짜리 상자를 켠다 - 눈에 보이는
                // 표시가 있는 자리라 "보이지 않는 벽"이 아니고, 높이도 갑판 골조 높이(0.64m)뿐이다.
                // 상자 밑면은 유령 판(0.36m)보다 아래인 해수면까지 내린다: 파도에 살짝 잠겨도 조준이
                // 끊기지 않게 하기 위해서다.
                //
                // **자리를 확정하기 전에는 켜지 않는다**(anchored). 정박 전 뗏목 루트는 아직 월드
                // 원점 근처에 있을 수 있어, 그 자리에 상자를 켜면 시작 섬 한복판에 상자가 선다.
                Vector3 ghostCenter = GetBaseTileCenter(0);

                hullCollider.enabled = anchored;
                hullCollider.center = new Vector3(ghostCenter.x, FrameTopY * 0.5f, ghostCenter.z);
                hullCollider.size = new Vector3(BaseTilePitch, FrameTopY, BaseTilePitch);

                ApplyDeckSurfaceCollider();
                return;
            }

            GetHullExtent(out float minZ, out float length, out float minX, out float width);

            hullCollider.enabled = true;
            hullCollider.center = new Vector3(minX + width * 0.5f, DeckSurfaceY * 0.5f, minZ + length * 0.5f);
            hullCollider.size = new Vector3(width, DeckSurfaceY, length);

            ApplyDeckSurfaceCollider();
        }

        /// <summary>
        /// 갑판 윗면 콜라이더를 현재 바닥판 범위에 맞춘다.
        ///
        /// **왜 이게 따로 필요한가:** 건축 시스템은 레이가 맞은 콜라이더의 부모를 거슬러 올라가
        /// DeckRoot에 닿을 때만 BuildSpace.Deck으로 전환한다(BuildingSystem.IsDeckCollider). 그런데
        /// 뗏목의 콜라이더는 (1) 뗏목 **본체**에 붙은 선체 BoxCollider와 (2) RaftVisual 밑의 승선
        /// 발판뿐이고, DeckRoot는 이 둘의 **형제/부모**라 부모 사슬로 절대 닿지 않는다.
        /// DeckRoot 밑에 실제 콜라이더를 하나 두는 것으로 조건을 충족시킨다.
        ///
        /// 이 판은 바닥판과 정확히 같은 자리(중심 y = DeckPlankY, 두께 = 바닥판 두께)에 있고, 선체
        /// 콜라이더의 윗면이 이미 DeckSurfaceY이므로 **선체 상자 안에 완전히 들어간다** - 새로 막히는
        /// 면이 생기지 않아 이동/충돌은 종전과 1mm도 다르지 않다.
        /// </summary>
        private void ApplyDeckSurfaceCollider()
        {
            if (deckSurfaceCollider == null)
                return;

            GetDeckedSpan(baseTileCount, out float minZ, out float maxZ);
            float span = maxZ - minZ;

            if (span <= 0.01f)
            {
                deckSurfaceCollider.enabled = false;
                return;
            }

            // DeckRoot는 뗏목 본체와 로컬 원점/회전이 같으므로(EnsureDeckRoot) 아래 값은 곧 뗏목 로컬이다.
            deckSurfaceCollider.center = new Vector3(0f, DeckPlankY, (minZ + maxZ) * 0.5f);
            deckSurfaceCollider.size = new Vector3(DeckWidth, DeckPlankThickness, span);
            deckSurfaceCollider.enabled = true;
        }

        /// <summary>
        /// 콜라이더를 **남기는** 큐브 파츠. StructureVisualBuilder.CreateVisualPart는 항상 콜라이더를
        /// 지우므로(시각 전용이 원칙), 실제로 밟고 올라가야 하는 승선 발판만 여기서 직접 만든다.
        /// CreatePrimitive가 붙여 주는 BoxCollider를 그대로 쓴다(지웠다가 다시 붙이면 Destroy 지연 때문에
        /// 한 프레임 동안 콜라이더가 2개가 된다).
        /// </summary>
        private GameObject CreateSolidPart(string name, Vector3 localPosition, Vector3 localScale,
            Material material, Quaternion localRotation)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(visualRoot, false);
            go.transform.localPosition = localPosition;
            go.transform.localRotation = localRotation;
            go.transform.localScale = localScale;

            var renderer = go.GetComponent<MeshRenderer>();
            if (renderer != null && material != null)
                renderer.sharedMaterial = material;

            return go;
        }
    }

    /// <summary>
    /// 뗏목 제작에 드는 재료 한 줄. itemName은 <c>ItemData.itemName</c>과 **문자 그대로** 대조된다
    /// (BuildPieceCost와 같은 규약 - 이 프로젝트의 재료 대조는 전부 이름 문자열이다).
    /// </summary>
    public struct RaftBuildCost
    {
        public readonly string itemName;
        public readonly int count;

        public RaftBuildCost(string itemName, int count)
        {
            this.itemName = itemName;
            this.count = count;
        }
    }

    /// <summary>
    /// 제작 목록의 항목 하나. 바닥판 3종 · 갑판 바닥재 · 부품 5종으로 **딱 9개**다.
    /// 순서를 바꾸지 말 것 - 제작 UI의 숫자키(1~9)와 줄 순서가 이 열거 순서 하나로 정해진다.
    /// </summary>
    public enum RaftBuildEntry
    {
        BaseWood = 0,
        BaseBuoy,
        BaseBarrel,
        Floor,
        Oar,
        Sail,
        Rudder,
        Anchor,
        Motor,
    }

    /// <summary>
    /// 뗏목 제작표 - **무엇을 얼마에 만들 수 있는가**의 단일 출처.
    ///
    /// [왜 UI가 아니라 여기 있나] 이 프로젝트에서 여러 번 반복된 사고가 "UI가 판정을 다시 구현해서
    /// 화면과 실제 동작이 갈라지는" 것이다(InteractionPromptUI 클래스 주석). 그래서 제작 UI
    /// (RaftBuildUI)는 이 클래스가 돌려주는 값을 **그리기만** 하고, 재료 대조·소모·설치는 전부
    /// TryBuild 한 곳에서 일어난다. 다음 웨이브(항해)나 퀘스트가 같은 표를 읽어야 할 때도 여기다.
    ///
    /// [재료 이름] 전부 Assets/_Project/ScriptableObjects/Item_*.asset 에 **실제로 존재하는**
    /// itemName이다(나뭇가지 · 노끈 · 부력통 · 금속조각 · 천조각 · 대나무 · 석재 · 엔진부품).
    /// 새 아이템 에셋은 하나도 만들지 않았다.
    ///
    /// [밸런스 근거] 기준선은 이미 게임에 있는 건축 카탈로그(BuildPieceCatalog)다 - 바닥 조각이
    /// 나뭇가지 2, 문이 나뭇가지 3 + 노끈 1, 소형 상자가 나뭇가지 8 + 노끈 3 + 야자잎 4다.
    /// 뗏목 바닥판 한 칸(나뭇가지 4 + 노끈 2)은 그 사이, 즉 "집 바닥 두 장 값"에 놓았다.
    /// 항해 가능(4칸)까지 나뭇가지 16 + 노끈 8, 대양 규격(6칸 + 돛 + 키)까지 대략 나뭇가지 27 +
    /// 노끈 15 + 천조각 3 + 대나무 2 - 상자 하나를 짓고 한두 번 업그레이드하는 정도의 채집량이다.
    /// 노(나뭇가지 2 + 노끈 1)를 가장 싸게 둔 것이 이 표의 핵심이다: 첫 항해까지 걸리는 시간을
    /// 정하는 것은 부품이 아니라 바닥판 4칸이어야 하고, 추진 수단이 비싸면 "칸은 다 깔았는데 못 나간다"
    /// 는 막힘이 생긴다. 반대로 모터(엔진부품 1 + 금속조각 4 + 노끈 1)는 특대 섬 전용 자원을 요구해
    /// 후반 보상으로 남긴다(엔딩 비축이 이미 엔진부품 2 · 금속조각 6을 요구하므로 총량이 과하지 않다).
    /// </summary>
    public static class RaftBuildCatalog
    {
        /// <summary>제작 UI가 그리는 순서(= 숫자키 1~9). 열거 순서와 같다.</summary>
        public static readonly RaftBuildEntry[] Order =
        {
            RaftBuildEntry.BaseWood,
            RaftBuildEntry.BaseBuoy,
            RaftBuildEntry.BaseBarrel,
            RaftBuildEntry.Floor,
            RaftBuildEntry.Oar,
            RaftBuildEntry.Sail,
            RaftBuildEntry.Rudder,
            RaftBuildEntry.Anchor,
            RaftBuildEntry.Motor,
        };

        // ── 재료표 (정적 배열 - 매 프레임 갱신되는 UI가 읽어도 할당이 0이다) ──────────
        private static readonly RaftBuildCost[] CostBaseWood =
        {
            new RaftBuildCost("나뭇가지", 4), new RaftBuildCost("노끈", 2),
        };

        private static readonly RaftBuildCost[] CostBaseBuoy =
        {
            new RaftBuildCost("부력통", 1), new RaftBuildCost("나뭇가지", 2), new RaftBuildCost("노끈", 1),
        };

        private static readonly RaftBuildCost[] CostBaseBarrel =
        {
            new RaftBuildCost("금속조각", 4), new RaftBuildCost("노끈", 2),
        };

        private static readonly RaftBuildCost[] CostFloor =
        {
            new RaftBuildCost("나뭇가지", 2), new RaftBuildCost("노끈", 1),
        };

        private static readonly RaftBuildCost[] CostOar =
        {
            new RaftBuildCost("나뭇가지", 2), new RaftBuildCost("노끈", 1),
        };

        private static readonly RaftBuildCost[] CostSail =
        {
            new RaftBuildCost("천조각", 3), new RaftBuildCost("노끈", 2), new RaftBuildCost("대나무", 2),
        };

        private static readonly RaftBuildCost[] CostRudder =
        {
            new RaftBuildCost("나뭇가지", 3), new RaftBuildCost("노끈", 1),
        };

        private static readonly RaftBuildCost[] CostAnchor =
        {
            new RaftBuildCost("석재", 2), new RaftBuildCost("노끈", 2),
        };

        private static readonly RaftBuildCost[] CostMotor =
        {
            new RaftBuildCost("엔진부품", 1), new RaftBuildCost("금속조각", 4), new RaftBuildCost("노끈", 1),
        };

        private static readonly RaftBuildCost[] EmptyCost = new RaftBuildCost[0];

        /// <summary>항목의 재료 목록. 절대 null이 아니다(없으면 빈 목록).</summary>
        public static IReadOnlyList<RaftBuildCost> GetCost(RaftBuildEntry entry)
        {
            switch (entry)
            {
                case RaftBuildEntry.BaseWood: return CostBaseWood;
                case RaftBuildEntry.BaseBuoy: return CostBaseBuoy;
                case RaftBuildEntry.BaseBarrel: return CostBaseBarrel;
                case RaftBuildEntry.Floor: return CostFloor;
                case RaftBuildEntry.Oar: return CostOar;
                case RaftBuildEntry.Sail: return CostSail;
                case RaftBuildEntry.Rudder: return CostRudder;
                case RaftBuildEntry.Anchor: return CostAnchor;
                case RaftBuildEntry.Motor: return CostMotor;
                default: return EmptyCost;
            }
        }

        /// <summary>항목 이름(한국어). 부품 이름은 RaftStructure의 단일 출처를 그대로 쓴다.</summary>
        public static string GetDisplayName(RaftBuildEntry entry)
        {
            switch (entry)
            {
                case RaftBuildEntry.BaseWood: return RaftStructure.GetBaseTileKindName(RaftBaseTileKind.Wood);
                case RaftBuildEntry.BaseBuoy: return RaftStructure.GetBaseTileKindName(RaftBaseTileKind.Buoy);
                case RaftBuildEntry.BaseBarrel: return RaftStructure.GetBaseTileKindName(RaftBaseTileKind.Barrel);
                case RaftBuildEntry.Floor: return "갑판 바닥재";
                default: return RaftStructure.GetPartName(GetPart(entry));
            }
        }

        /// <summary>한 줄 설명(그 항목이 무엇을 바꾸는지). 제작 UI의 보조 문구다.</summary>
        public static string GetDescription(RaftBuildEntry entry)
        {
            switch (entry)
            {
                case RaftBuildEntry.BaseWood: return "부력 1.0 · 가장 싼 한 칸";
                case RaftBuildEntry.BaseBuoy: return "부력 1.6 · 주운 부력통을 끼운다";
                case RaftBuildEntry.BaseBarrel: return "부력 2.0 · 가장 튼튼하다";
                case RaftBuildEntry.Floor: return "칸 위에 깔아 걸어다닐 면을 만든다";
                case RaftBuildEntry.Oar: return "가장 싼 추진 · 근해까지";
                case RaftBuildEntry.Sail: return "키와 함께 있어야 대양에 나간다";
                case RaftBuildEntry.Rudder: return "방향을 잡는다";
                case RaftBuildEntry.Anchor: return "정박용 · 항해에는 필요 없다";
                case RaftBuildEntry.Motor: return "가장 빠르다 · 돛+키를 대체한다";
                default: return string.Empty;
            }
        }

        /// <summary>이 항목이 놓는 바닥판 종류(부품이면 None).</summary>
        public static RaftBaseTileKind GetBaseTileKind(RaftBuildEntry entry)
        {
            switch (entry)
            {
                case RaftBuildEntry.BaseWood: return RaftBaseTileKind.Wood;
                case RaftBuildEntry.BaseBuoy: return RaftBaseTileKind.Buoy;
                case RaftBuildEntry.BaseBarrel: return RaftBaseTileKind.Barrel;
                default: return RaftBaseTileKind.None;
            }
        }

        /// <summary>이 항목이 장착하는 부품(바닥판/바닥재면 None).</summary>
        public static RaftPart GetPart(RaftBuildEntry entry)
        {
            switch (entry)
            {
                case RaftBuildEntry.Oar: return RaftPart.Oar;
                case RaftBuildEntry.Sail: return RaftPart.Sail;
                case RaftBuildEntry.Rudder: return RaftPart.Rudder;
                case RaftBuildEntry.Anchor: return RaftPart.Anchor;
                case RaftBuildEntry.Motor: return RaftPart.Motor;
                default: return RaftPart.None;
            }
        }

        /// <summary>
        /// 인벤토리에 있는 그 이름의 아이템 개수. BuildingSystem.CountOwned와 **같은 규칙**이다
        /// (한 칸 = 한 개, 이름 문자 그대로 대조) - 두 시스템이 같은 재료를 다르게 세면 안 된다.
        /// </summary>
        public static int CountOwned(PlayerInventory inventory, string itemName)
        {
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

        /// <summary>재료를 전부 들고 있는지(소모하지 않는다).</summary>
        public static bool HasMaterials(PlayerInventory inventory, RaftBuildEntry entry)
        {
            IReadOnlyList<RaftBuildCost> cost = GetCost(entry);
            for (int i = 0; i < cost.Count; i++)
            {
                if (cost[i].count > 0 && CountOwned(inventory, cost[i].itemName) < cost[i].count)
                    return false;
            }
            return true;
        }

        /// <summary>
        /// 지금 이 항목을 만들 수 있는지. 만들 수 없으면 사유를 돌려준다(재료 부족은 여기서 보지
        /// 않는다 - UI가 재료 줄에 수량으로 이미 보여주므로 사유를 두 번 적지 않기 위해서다).
        /// </summary>
        public static bool IsAvailable(RaftStructure raft, RaftBuildEntry entry, out string blockedReason)
        {
            blockedReason = string.Empty;
            if (raft == null)
            {
                blockedReason = "뗏목 자리 없음";
                return false;
            }

            RaftBaseTileKind kind = GetBaseTileKind(entry);
            if (kind != RaftBaseTileKind.None)
            {
                if (raft.BaseTileCount >= RaftStructure.MaxBaseTiles)
                {
                    blockedReason = "가득 참";
                    return false;
                }
                return true;
            }

            if (entry == RaftBuildEntry.Floor)
            {
                if (raft.BaseTileCount <= 0)
                {
                    blockedReason = "바닥판 먼저";
                    return false;
                }
                if (raft.NextFloorlessTileIndex < 0)
                {
                    blockedReason = "전부 깔림";
                    return false;
                }
                return true;
            }

            RaftPart part = GetPart(entry);
            if (part == RaftPart.None)
            {
                blockedReason = "알 수 없는 항목";
                return false;
            }

            if (raft.HasPart(part))
            {
                blockedReason = "장착됨";
                return false;
            }

            // 부품은 딛고 설 바닥이 있어야 단다. 바닥판 0칸짜리 물 위에 돛대를 세울 수는 없다.
            if (raft.BaseTileCount <= 0)
            {
                blockedReason = "바닥판 먼저";
                return false;
            }

            return true;
        }

        /// <summary>
        /// 실제 제작. 재료를 확인하고 → 소모하고 → 뗏목에 반영한다. **이 순서와 판정이 유일하다**
        /// (UI가 같은 검사를 다시 하지 않는다). 실패하면 아무것도 소모하지 않고 사유를 돌려준다.
        ///
        /// 소모 방식은 BuildingSystem.ConsumeCostList와 같다 - 뒤에서부터 같은 이름의 항목을 지우고
        /// 마지막에 NotifyInventoryChanged를 한 번만 부른다(칸마다 이벤트를 쏘면 UI가 n번 다시 그린다).
        /// </summary>
        public static bool TryBuild(RaftStructure raft, PlayerInventory inventory, RaftBuildEntry entry,
            out string failureReason)
        {
            if (!IsAvailable(raft, entry, out failureReason))
                return false;

            if (inventory == null || inventory.items == null)
            {
                failureReason = "소지품을 찾을 수 없다";
                return false;
            }

            IReadOnlyList<RaftBuildCost> cost = GetCost(entry);
            for (int i = 0; i < cost.Count; i++)
            {
                if (cost[i].count > 0 && CountOwned(inventory, cost[i].itemName) < cost[i].count)
                {
                    failureReason = $"재료 부족 - {cost[i].itemName} {cost[i].count}개 필요";
                    return false;
                }
            }

            // 여기서부터는 반드시 성공한다(위에서 전부 확인했다) - 재료만 사라지는 상태가 없다.
            for (int i = 0; i < cost.Count; i++)
            {
                RaftBuildCost line = cost[i];
                if (string.IsNullOrEmpty(line.itemName) || line.count <= 0)
                    continue;

                int remaining = line.count;
                for (int k = inventory.items.Count - 1; k >= 0 && remaining > 0; k--)
                {
                    InventoryItem item = inventory.items[k];
                    if (item == null || item.data == null || item.data.itemName != line.itemName)
                        continue;

                    inventory.items.RemoveAt(k);
                    remaining--;
                }
            }

            inventory.NotifyInventoryChanged();

            RaftBaseTileKind kind = GetBaseTileKind(entry);
            if (kind != RaftBaseTileKind.None)
                return raft.AddBaseTile(kind);

            if (entry == RaftBuildEntry.Floor)
                return raft.AddFloorTile();

            return raft.InstallPart(GetPart(entry));
        }
    }
}
