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
    public partial class RaftStructure : MonoBehaviour
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
        /// 승선 발판이 고물 뒤로 뻗는 길이(m). 건축 고스트가 발판을 같은 크기로 그리기 위해 공개한다 -
        /// 그림과 판정이 다른 값을 쓰면 "고스트에는 닿는데 실제로는 안 닿는" 자리가 생긴다.
        /// </summary>
        public const float RampRunPublic = RampRun;

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

        // ── 명부 ────────────────────────────────────────────────────────────────────
        //
        // 예전에는 static 싱글턴 하나였다(activeInstance). 뗏목을 아무 물가에나, 여러 대 지을 수
        // 있게 되면서 "월드에 뗏목은 하나"라는 전제 자체가 사라졌다.
        //
        // 대신 두 가지 질문에 답한다. 부르는 쪽이 무엇을 묻는지가 서로 다르기 때문이다.
        //  · **Best** — "탈출할 배가 준비됐는가?" 진행도·엔딩·퀘스트·섬 이동 게이트가 이걸 본다.
        //    여러 대가 있으면 가장 완성된 것 하나가 그 질문의 답이다.
        //  · **Nearest(pos)** — "지금 내가 만지고 있는 뗏목은 어느 것인가?" 갑판 건축과 항해가
        //    이걸 본다. 여기서 Best를 쓰면 저쪽 섬에 있는 더 좋은 뗏목의 갑판에 집을 짓게 된다.
        private static readonly List<RaftStructure> all = new List<RaftStructure>();

        /// <summary>월드에 살아 있는 모든 뗏목. 순서는 만들어진 순서다.</summary>
        public static IReadOnlyList<RaftStructure> All => all;

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
        /// **가장 완성된 뗏목.** 없으면 null이다.
        ///
        /// "탈출할 배가 준비됐는가"를 묻는 쪽(진행도 표시·엔딩 판정·퀘스트·섬 이동 게이트)이 쓴다.
        /// 이름이 Active인 것은 예전 싱글턴 시절의 호출부를 그대로 두기 위해서이고, 뜻은
        /// "지금 활성인 하나"가 아니라 "지금 이 월드를 대표하는 하나"다.
        ///
        /// ★ 이 값은 **뗏목을 지을수록 바뀐다.** 예전에는 인스턴스가 하나뿐이라 Start에서 한 번
        ///   잡아 두면 그만이었지만, 이제는 캐시해 두면 낡는다. 호출부는 매 갱신마다 다시 읽는다
        ///   (static 프로퍼티 읽기라 전역 검색 비용이 없다는 기존 주석의 전제는 그대로다).
        /// </summary>
        public static RaftStructure Active => Best;

        /// <summary>가장 완성된 뗏목. 점수 = 바닥판 칸 수 + 장착 부품 수 × 4(부품이 더 귀하다).</summary>
        public static RaftStructure Best
        {
            get
            {
                RaftStructure best = null;
                int bestScore = int.MinValue;

                for (int i = 0; i < all.Count; i++)
                {
                    RaftStructure raft = all[i];
                    if (raft == null)
                        continue;

                    int score = raft.BaseTileCount + CountBits((int)raft.InstalledParts) * 4;
                    if (score > bestScore)
                    {
                        bestScore = score;
                        best = raft;
                    }
                }

                return best;
            }
        }

        /// <summary>
        /// 주어진 위치에서 가장 가까운 뗏목. 없으면 null.
        /// "지금 내가 만지고 있는 뗏목"을 묻는 쪽(갑판 건축·항해)이 쓴다.
        /// </summary>
        public static RaftStructure Nearest(Vector3 worldPosition)
        {
            RaftStructure nearest = null;
            float bestSqr = float.MaxValue;

            for (int i = 0; i < all.Count; i++)
            {
                RaftStructure raft = all[i];
                if (raft == null)
                    continue;

                float sqr = (raft.transform.position - worldPosition).sqrMagnitude;
                if (sqr < bestSqr)
                {
                    bestSqr = sqr;
                    nearest = raft;
                }
            }

            return nearest;
        }

        /// <summary>세워진 뗏목 수.</summary>
        public static int Count => all.Count;

        // ── 뗏목 식별자 ─────────────────────────────────────────────────────────
        //
        // [왜 필요한가] 갑판 위에 지은 건축 조각이 "어느 뗏목 소속인지"를 적어 두려면 뗏목마다
        // 변하지 않는 이름표가 있어야 한다.
        //
        // [왜 명부 인덱스가 아닌가] all은 DestroyAll·씬 리로드·불러오기로 순서가 갈리고, 저장은
        // 아직 자리를 못 잡은 뗏목을 건너뛰므로(SaveLoadController) 저장 인덱스와 런타임 인덱스가
        // 이미 어긋난다. [왜 앵커 좌표가 아닌가] 항해하면 바뀌고, 두 뗏목이 가까울 때 부동소수
        // 비교로 소속을 가리면 조각이 섞인다.

        [SerializeField] private string raftId;

        /// <summary>이 뗏목의 불변 식별자. 세이브에 실려 나가고 불러오기 때 그대로 돌아온다.</summary>
        public string RaftId
        {
            get
            {
                if (string.IsNullOrEmpty(raftId))
                    raftId = System.Guid.NewGuid().ToString("N");
                return raftId;
            }
        }

        /// <summary>
        /// 세이브에서 읽은 식별자를 물린다. **PlaceAt/ApplySavedState보다 먼저** 불러야 한다 -
        /// 건축 조각 복원이 이 값으로 소속 뗏목을 찾기 때문이다.
        /// 빈 값이면 아무것도 하지 않는다(스스로 새로 발급한 것을 덮어쓰지 않는다).
        /// </summary>
        public void AssignId(string id)
        {
            if (!string.IsNullOrEmpty(id))
                raftId = id;
        }

        /// <summary>식별자로 살아 있는 뗏목을 찾는다. 없으면 null.</summary>
        public static RaftStructure FindById(string id)
        {
            if (string.IsNullOrEmpty(id))
                return null;

            for (int i = 0; i < all.Count; i++)
            {
                RaftStructure raft = all[i];
                if (raft != null && raft.RaftId == id)
                    return raft;
            }

            return null;
        }

        /// <summary>격자 키에 접어 넣을 수 있는 가장 큰 뗏목 번호(6비트).</summary>
        public const int MaxKeySlot = 63;

        private int keySlot = -1;

        /// <summary>
        /// 건축 격자 키에 접어 넣는 짧은 번호(0~63). GUID를 그대로 키에 넣을 수는 없어서 둔 값이다.
        /// **살아 있는 뗏목끼리만 겹치지 않으면 된다** - 세이브에 나가지 않고, 불러오기 때 새로
        /// 발급된다(조각의 번호도 그때 다시 매겨진다).
        /// </summary>
        public int KeySlot
        {
            get
            {
                if (keySlot < 0)
                    keySlot = AllocateKeySlot();
                return keySlot;
            }
        }

        /// <summary>
        /// 번호 점유표. **명부(all)를 훑지 않는 이유**: 명부는 OnDisable에서 빠졌다가 OnEnable에서
        /// 돌아온다. 그 사이에 다른 뗏목이 같은 번호를 받으면, 돌아온 뗏목과 번호가 겹쳐 두 뗏목의
        /// 갑판 칸이 같은 격자 키를 쓰게 된다(이미 등록된 조각의 번호는 그대로라 되돌릴 수도 없다).
        /// 점유는 파괴될 때까지 유지한다.
        /// </summary>
        private static readonly bool[] keySlotTaken = new bool[MaxKeySlot + 1];

        /// <summary>아무도 쓰지 않는 가장 작은 번호를 잡는다.</summary>
        private static int AllocateKeySlot()
        {
            for (int slot = 0; slot <= MaxKeySlot; slot++)
            {
                if (keySlotTaken[slot])
                    continue;

                keySlotTaken[slot] = true;
                return slot;
            }

            Debug.LogWarning($"[RaftStructure] 뗏목 번호를 {MaxKeySlot + 1}개 다 썼다." +
                " 갑판 칸 점유 판정이 뗏목 사이에서 섞일 수 있다.");
            return 0;
        }

        /// <summary>파괴될 때 번호를 돌려놓는다.</summary>
        private void ReleaseKeySlot()
        {
            if (keySlot >= 0 && keySlot <= MaxKeySlot)
                keySlotTaken[keySlot] = false;

            keySlot = -1;
        }

        /// <summary>
        /// 파도 흔들림을 뺀 기준 위치. **저장에는 반드시 이 값을 쓴다** - transform.position에는
        /// 그 프레임의 상하 흔들림이 섞여 있어, 그대로 저장하면 불러올 때마다 뗏목이 조금씩 위아래로
        /// 옮겨 앉는다.
        /// </summary>
        public Vector3 AnchorPosition => anchored ? anchorPosition : transform.position;

        /// <summary>기준 뱃머리 방향(도).</summary>
        public float AnchorYaw => (anchored ? anchorRotation : transform.rotation).eulerAngles.y;

        /// <summary>자리를 확정했는가.</summary>
        public bool IsPlaced => anchored;

        /// <summary>
        /// 월드의 뗏목을 전부 없앤다. 불러오기가 세이브대로 다시 세우기 전에 부른다.
        ///
        /// 명부에서 **즉시** 빼는 것이 핵심이다. Destroy는 프레임 끝에 처리되므로, 명부를 비우지
        /// 않으면 바로 뒤에 세우는 새 뗏목이 "이미 뗏목이 있다"고 판정되어 배치가 막힌다.
        /// </summary>
        /// <summary>
        /// 뗏목 한 대를 없앤다. 배치를 되돌릴 때 쓴다.
        ///
        /// **명부에서 즉시 뺀다** - Destroy는 프레임 끝까지 지연되므로, 그때까지 명부에 남겨 두면
        /// 같은 프레임의 IsValidSite가 방금 지운 뗏목을 보고 "다른 뗏목과 너무 가깝다"로 막는다.
        /// </summary>
        public static void DestroyRaft(RaftStructure raft)
        {
            if (raft == null)
                return;

            all.Remove(raft);
            Destroy(raft.gameObject);
        }

        public static void DestroyAll()
        {
            for (int i = all.Count - 1; i >= 0; i--)
            {
                RaftStructure raft = all[i];
                all.RemoveAt(i);

                if (raft != null)
                    Destroy(raft.gameObject);
            }
        }

        private static int CountBits(int value)
        {
            int count = 0;
            while (value != 0)
            {
                value &= value - 1;
                count++;
            }
            return count;
        }

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
        /// 살아남는다. 그래서 구독은 "한 번만"이 아니라 **매번 멱등하게**(-= 뒤 +=) 건다 -
        /// 자세한 이유는 아래 구독 지점 주석에 있다.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            all.Clear();

            // 격자 번호 점유표. OnDestroy가 안 불린 채 도메인이 리로드되면 표가 다 찬 채로 남아,
            // 다음 실행의 뗏목이 전부 0번을 쓰게 된다(갑판 칸이 섞인다).
            System.Array.Clear(keySlotTaken, 0, keySlotTaken.Length);

            // 씬을 다시 열면 월드 매니저도 새 인스턴스다.
            siteWorldMap = null;

            // 모델 캐시도 함께 비운다. 도메인 리로드를 끈 플레이 모드에서 이전 실행의 (이미 언로드된)
            // 메시를 들고 시작하면 파츠가 통째로 빈 채로 만들어진다.
            System.Array.Clear(baseTilePrimary, 0, baseTilePrimary.Length);
            System.Array.Clear(baseTileSecondary, 0, baseTileSecondary.Length);
            // 공유 머티리얼도 함께 비운다(static이 된 뒤로는 모델 캐시와 수명이 같다).
            hullWoodMaterial = null;
            plankWoodMaterial = null;
            fiberMaterial = null;
            sailMaterial = null;
            cargoMaterial = null;
            metalMaterial = null;
            ghostMaterial = null;

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

            // ★★ 구독은 **매번, 멱등하게** 건다. 예전에는 bootstrapHooked 플래그로 "한 번만" 걸었는데,
            //     그 방식은 조건이 어긋나는 순간 뗏목이 통째로 안 생긴다:
            //     플래그는 static이라 도메인 리로드를 끈 플레이 모드에서 살아남는 반면,
            //     sceneLoaded 구독은 플레이 모드를 빠져나올 때 정리될 수 있다. 그러면
            //     "이미 걸었다"고 믿는 플래그만 남고 실제 구독은 없는 상태가 된다.
            //     실기에서 정확히 그 증상이 나왔다 - 재컴파일 직후 첫 플레이에서만 뗏목이 생기고
            //     그 뒤로는 안 생겼다.
            //
            //     -= 뒤에 += 는 구독이 없어도 안전하고(없는 것을 빼도 예외가 아니다) 중복도 막는다.
            //     이 프로젝트의 다른 자기 부트스트랩 시스템(WindSystem·SkySystem 등)이 매번 거는 것과
            //     같은 결과이면서, 중복 구독까지 원리적으로 불가능하다.
            SceneManager.sceneLoaded -= HandleSceneLoaded;
            SceneManager.sceneLoaded += HandleSceneLoaded;
        }

        private static void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            // ★★ mode로 거르지 말 것. Additive 로드를 걸러내려고 `if (mode != LoadSceneMode.Single) return;`
            //     을 넣었더니 **시작 뗏목이 아예 안 생겼다.** 진단 로그로 실측한 결과 에디터에서
            //     플레이 모드에 들어갈 때 이 콜백이 받는 mode 값은 **4**다 - LoadSceneMode의
            //     Single(0)도 Additive(1)도 아니다. 문서에 없는 값이라 코드로 짐작하면 반드시 틀린다.
            //
            //     이 프로젝트에서 씬을 Additive로 여는 경로는 없고, 중복은 아래 `all.Count == 0`
            //     하나로 이미 막힌다. 그 조건만으로 충분하다.
            all.RemoveAll(raft => raft == null);

            // 시작 뗏목 한 대를 시작 섬 물가에 세운다.
            //
            // ★ 자유 배치가 들어온 뒤에도 이 한 대를 남기는 이유(임시): 지금은 이 말뚝 표시가
            //   뗏목이라는 기능의 **유일한 발견 경로**다. 건축 메뉴에서 원하는 물가에 세우는 경로가
            //   들어오면 이 자동 생성은 지운다 - 자동으로 한 대가 서 있으면 "내가 놓은 것"이라는
            //   감각이 처음부터 사라지기 때문이다.
            //
            //   불러오기는 이것과 무관하다. SaveLoadController가 DestroyAll로 전부 지우고
            //   세이브에 적힌 자리에 다시 세운다.
            if (all.Count == 0)
                Create();
        }

        /// <summary>
        /// 뗏목 본체를 확보한다(이미 있으면 그대로 쓴다). 씬에 손으로 놓아 둔 것이 있으면 그것을 쓴다.
        /// 자리 확정(시작 섬 해안 찾기)은 인스턴스 자신이 한다 - 여기서 섬을 읽으려 하면 WorldMapManager가
        /// 아직 안 돌았을 수 있기 때문이다.
        /// </summary>
        public static RaftStructure EnsureInstance()
        {
            RaftStructure best = Best;
            if (best != null)
                return best;

            var existing = FindAnyObjectByType<RaftStructure>();
            if (existing != null)
                return existing;

            return Create();
        }

        /// <summary>
        /// 새 뗏목을 하나 만든다. 자리는 아직 정하지 않은 상태로 나오므로, 부르는 쪽이 곧바로
        /// <see cref="PlaceAt"/>로 자리를 주거나, 주지 않으면 예전처럼 시작 섬 물가를 스스로 찾는다.
        ///
        /// **씬 루트에 만든다.** WorldMapManager.RegenerateWorld(불러오기)가 자기 자식을 전부
        /// 파괴하므로 그 밑에 두면 불러오기 때 뗏목이 함께 지워진다(예전 주석의 이유 그대로).
        /// </summary>
        public static RaftStructure Create()
        {
            var go = new GameObject(all.Count == 0 ? "RaftStructure" : $"RaftStructure_{all.Count}");
            return go.AddComponent<RaftStructure>();
        }

        private void Awake()
        {
            // 명부에 오른다. 예전에는 여기서 "이미 뗏목이 있으면 나를 재운다"고 했지만, 이제 뗏목은
            // 여러 대가 정상이다. 등록이 Awake인 이유: 자리를 잡기 전에도 명부에 있어야 배치 코드가
            // "다른 뗏목과 겹치는가"를 물어볼 수 있다.
            if (!all.Contains(this))
                all.Add(this);

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
            // 비활성화됐다 다시 켜지는 경로(섬 재생성 등)에서 명부로 돌아온다.
            if (!all.Contains(this))
                all.Add(this);
        }

        private void OnDisable()
        {
            // 명부를 떠나는 경로가 이것뿐이다. 없으면 SetActive(false)된 뗏목이 계속 Best/Nearest에
            // 잡히고, "뗏목이 한 대도 없으면 한 대 세운다"는 규칙도 영영 발동하지 않는다.
            all.Remove(this);
        }

        private void OnDestroy()
        {
            all.Remove(this);

            // 격자 번호는 **파괴될 때만** 돌려놓는다(비활성화로는 놓지 않는다 - AllocateKeySlot 주석).
            ReleaseKeySlot();
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

            PlaceAt(center, Quaternion.LookRotation(facing, Vector3.up));
        }

        /// <summary>
        /// 이 뗏목을 주어진 자리에 세운다. 자동 해안 탐색(TryAnchorToShore)과 건축 메뉴 배치가
        /// **같은 이 메서드**를 통과하므로, 두 경로가 서로 다른 상태로 끝날 여지가 없다.
        ///
        /// center는 선체 중심의 월드 좌표다(y는 해수면으로 맞춰진다). rotation의 forward가
        /// 뱃머리 방향이고, 승선 발판은 그 반대쪽(고물)에 달린다.
        /// </summary>
        public void PlaceAt(Vector3 center, Quaternion rotation)
        {
            if (worldMap == null)
                worldMap = FindAnyObjectByType<WorldMapManager>();

            if (worldMap != null)
                center.y = worldMap.seaLevel;

            // 갓 만든 MeshCollider는 Physics.autoSyncTransforms가 기본 false라 아직 물리 씬에 없을 수
            // 있다. 아래 발판 높이 실측이 그걸 그대로 밟는다 - 빠뜨리면 레이가 지형을 못 맞혀
            // 발판이 해변에 닿지 않고, 그러면 뗏목에 올라탈 방법이 사라진다(stepOffset 0.3).
            // TryAnchorToShore에 있던 이 한 줄이 측정과 함께 여기로 와야 했다.
            Physics.SyncTransforms();

            transform.SetPositionAndRotation(center, rotation);

            // 승선 발판이 닿을 해변 지점의 실제 높이를 잰다. terrainMaxHeight는 씬 직렬화 값(8)이
            // 코드 기본값(2.5)과 다르므로 상수로 가정하면 안 된다 - 반드시 실측한다.
            //
            // [파도 v5] 잰 높이보다 RampFootDig만큼 **더 아래**를 발판 밑동으로 잡는다(하한도 함께
            // 내렸다). 상하 흔들림 상한이 1.2m라, 발판을 모래 표면에 딱 맞춰 두면 파도 마루마다 발판
            // 끝이 통째로 떠올라 CharacterController.stepOffset(씬 값 0.3)을 넘는 턱이 생기기 때문이다.
            Vector3 facing = rotation * Vector3.forward;
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
        /// 이 뗏목이 차지하는 수평 반경(m). 배치할 때 다른 뗏목과 겹치는지 볼 때 쓴다.
        /// 선체 대각선의 절반에 여유를 조금 더한 값이다.
        /// </summary>
        public static float FootprintRadius =>
            Mathf.Sqrt(DeckLength * DeckLength + DeckWidth * DeckWidth) * 0.5f + 0.6f;

        /// <summary>
        /// 여기에 뗏목을 세울 수 있는가. 건축 메뉴의 고스트가 매 프레임 묻는다.
        ///
        /// 세 가지를 본다.
        ///  (1) **물 위인가.** 해수면 아래로 최소 MinSiteDepth만큼 파여 있어야 한다 - 모래 위에 배를
        ///      지어 놓으면 띄울 수가 없다.
        ///  (2) **물가에서 너무 멀지 않은가.** 뗏목은 해안 건조물이다. 먼바다 한복판에 지을 수 있으면
        ///      승선 발판이 닿을 땅이 없어 올라탈 방법이 사라진다.
        ///  (3) **다른 뗏목과 겹치지 않는가.**
        ///
        /// reason에는 안 되는 이유를 그대로 담는다(고스트 옆에 그대로 띄우기 위해서다 - "왜 안 되는지
        /// 모르겠는 빨간 고스트"가 이 프로젝트에서 반복해서 나온 UX 실패다).
        /// </summary>
        public static bool IsValidSite(Vector3 worldPoint, float yaw, RaftStructure ignore, out string reason)
        {
            // 매 프레임 씬을 뒤지지 않는다 - 이 함수는 건축 고스트가 조준하는 동안 계속 불린다.
            if (siteWorldMap == null)
                siteWorldMap = FindAnyObjectByType<WorldMapManager>();

            float seaLevel = siteWorldMap != null ? siteWorldMap.seaLevel : 0f;

            // (1) 물 위인가. **중심 한 점이 아니라 선체 네 귀퉁이까지** 잰다.
            //
            // ★ 이게 왜 중요한가: 첫 바닥판은 선체 중심이 아니라 **고물-좌현 모서리**에 놓인다
            //   (GetBaseTileCenter(0) = 로컬 (-1, 0, -3)). 중심만 보면 수심 0.6m를 통과해도 실제로
            //   생기는 판자는 3m 뒤 모래밭에 박힐 수 있다. 고스트는 4x8 테두리를 그리므로 플레이어
            //   눈에는 "초록 사각형 한복판을 겨눴는데 판자가 구석에 나온" 것으로 보인다.
            if (!IsWaterDeepEnough(worldPoint, seaLevel))
            {
                reason = "물이 얕다 - 뗏목이 뜨지 않는다";
                return false;
            }

            Quaternion rotation = Quaternion.Euler(0f, yaw, 0f);
            float halfWidth = DeckWidth * 0.5f - 0.3f;
            float halfLength = DeckLength * 0.5f - 0.3f;

            for (int corner = 0; corner < 4; corner++)
            {
                float localX = (corner % 2 == 0 ? -1f : 1f) * halfWidth;
                float localZ = (corner < 2 ? -1f : 1f) * halfLength;
                Vector3 probe = worldPoint + rotation * new Vector3(localX, 0f, localZ);

                if (!IsWaterDeepEnough(probe, seaLevel))
                {
                    reason = "선체가 걸린다 - 네 귀퉁이가 다 물에 잠겨야 한다";
                    return false;
                }
            }

            // (2) **승선 발판이 뭍에 닿는가.**
            //
            // 예전에는 "반경 14m 안에 뭍이 있는가"를 여덟 방향으로 훑었다. 그런데 발판은 고물(-Z)
            // 한 방향에 고정이라, 물가가 옆이나 앞에만 있으면 검사는 통과하고 발판은 허공에 뜬다
            // (수영으로는 올라탈 수 있지만 "왜 안 올라가지"가 된다). 그래서 두루뭉술한 반경 대신
            // **실제로 발판이 놓일 지점 한 곳**을 잰다 - PlaceAt이 발판 높이를 재는 바로 그 점이다.
            //
            // 덤으로 싸다. 여덟 방향 탐침은 최대 56회 레이캐스트였고 이건 1회다.
            Vector3 facing = rotation * Vector3.forward;
            Vector3 rampFoot = worldPoint - facing * (DeckLength * 0.5f + RampRun);
            float rampGroundY = SampleTerrainHeightStatic(rampFoot, out bool rampHitTerrain);

            // PlaceAt이 발판 밑동을 Clamp(groundY - center.y - RampFootDig, -0.9f, ...)로 자른다.
            // 그 하한에 걸리는 깊이보다 더 파여 있으면 발판이 바닥에 닿지 않는다.
            if (!rampHitTerrain || rampGroundY < seaLevel - (0.9f - RampFootDig))
            {
                reason = "승선 발판이 뭍에 닿지 않는다 - 뱃머리를 물 쪽으로 돌려라";
                return false;
            }

            // (3) 다른 뗏목과 겹치지 않는가.
            float minGap = FootprintRadius * 2f;
            for (int i = 0; i < all.Count; i++)
            {
                RaftStructure other = all[i];
                if (other == null || other == ignore || !other.anchored)
                    continue;

                Vector3 delta = other.transform.position - worldPoint;
                delta.y = 0f;
                if (delta.sqrMagnitude < minGap * minGap)
                {
                    reason = "다른 뗏목과 너무 가깝다";
                    return false;
                }
            }

            reason = string.Empty;
            return true;
        }

        /// <summary>
        /// 뱃머리 방향을 모르는 호출자를 위한 판(정북을 향한다고 본다).
        /// 자유 배치는 반드시 yaw를 넘겨야 한다 - 선체가 4x8이라 방향에 따라 답이 달라진다.
        /// </summary>
        public static bool IsValidSite(Vector3 worldPoint, RaftStructure ignore, out string reason)
        {
            return IsValidSite(worldPoint, 0f, ignore, out reason);
        }

        /// <summary>그 지점이 뗏목을 띄울 만큼 파여 있는가.</summary>
        private static bool IsWaterDeepEnough(Vector3 worldPoint, float seaLevel)
        {
            float groundY = SampleTerrainHeightStatic(worldPoint, out bool hitTerrain);
            float depth = hitTerrain ? seaLevel - groundY : DeepWaterAssumedDepth;
            return depth >= MinSiteDepth;
        }

        /// <summary>IsValidSite가 해수면을 물어볼 월드 매니저(씬당 하나). ResetStatics에서 비운다.</summary>
        private static WorldMapManager siteWorldMap;

        /// <summary>지형을 못 맞혔을 때 가정하는 수심(m). 먼바다는 충분히 깊다고 본다.</summary>
        private const float DeepWaterAssumedDepth = 50f;

        /// <summary>뗏목을 띄우려면 최소한 이만큼은 파여 있어야 한다(m).</summary>
        private const float MinSiteDepth = 0.55f;

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
            return SampleTerrainHeightStatic(position, out hitTerrain);
        }

        /// <summary>
        /// 위와 같은 계산의 static 판. 배치 판정(IsValidSite)은 인스턴스가 아직 없는 상태에서도
        /// 지형 높이를 물어야 하므로 필요하다. 인스턴스 메서드는 이걸 그대로 부른다 -
        /// "Island_ 콜라이더만 지형" 규약의 복사본이 생기지 않는다.
        /// </summary>
        public static float SampleTerrainHeightStatic(Vector3 position, out bool hitTerrain)
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

    }

}
