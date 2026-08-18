using System.Collections.Generic;
using UnityEngine;

using MakeGame.Data;
using MakeGame.Player;

namespace MakeGame.Systems
{
    /// <summary>
    /// 조타 자리 표식. 뗏목 고물 **뒤쪽**(갑판 격자 밖)에 서 있는 트리거 상자 하나에만 붙는다.
    ///
    /// [왜 별도 컴포넌트인가] 상호작용 컨트롤러는 조준 대상의 컴포넌트 종류로 행동을 고른다. 뗏목
    /// 본체(선체·갑판·승선 발판)는 전부 "제작 창 열기"이고 조타 자리만 "항해 시작/종료"여야 하는데,
    /// 둘 다 RaftStructure의 자손이라 GetComponentInParent<RaftStructure> 하나로는 구분할 수 없다.
    /// 이 표식이 붙은 콜라이더를 조준했을 때만 조종 분기가 열린다.
    ///
    /// [왜 갑판 격자 밖인가] BuildingSystem.CastBuildRay는 갑판 윗면(DeckRoot의 자손)이 아닌 뗏목
    /// 콜라이더에 먼저 맞으면 그 지점을 "뗏목에 막힘"으로 확정한다. 조타 상자를 갑판 칸 위에 두면
    /// 그 칸에 집을 지을 수 없게 된다. 그래서 로컬 z를 고물 끝(-DeckLength/2)보다 더 뒤로 빼
    /// 어떤 갑판 셀과도 겹치지 않게 한다(갑판 셀은 |z| &lt;= DeckLength/2 안쪽에만 있다).
    ///
    /// [왜 트리거인가] 승선 발판이 이 상자 아래를 지난다. 솔리드로 두면 배에 오르는 길이 막힌다.
    /// Physics.queriesHitTriggers 기본값이 true라 상호작용 레이캐스트에는 그대로 잡히고,
    /// BuildingSystem의 상자 조준 레이는 QueryTriggerInteraction.Ignore라 영향을 받지 않는다.
    /// </summary>
    [DisallowMultipleComponent]
    public class RaftHelm : MonoBehaviour
    {
        /// <summary>이 조타 자리가 조종하는 항해 컴포넌트. RaftStructure가 만들 때 넣어 준다.</summary>
        public RaftSailing sailing;
    }

    /// <summary>
    /// 뗏목을 실제로 타고 나가는 항해. RaftStructure와 **같은 GameObject**에 붙고, 뗏목의 "기준 자세"
    /// (파도 흔들림이 오프셋을 얹는 그 기준)를 매 프레임 갱신한다.
    ///
    /// ── 역할 분담 (이 파일이 만든 유일한 계약) ──────────────────────────────────────
    ///  · RaftStructure : 상태(바닥판/부품) · 외형 · 파도 흔들림 · 승선 이송 · 갑판 건축 계약.
    ///  · RaftSailing   : 조종 모드 · 추진(노/돛/모터) · 바람 · 적재/부력 · 전복 위험 · 닻 · 좌초.
    /// RaftStructure.Update가 UpdateWaveMotion **직전에** TickNavigation()을 부른다. 이 순서가
    /// 계약이다 - 스크립트 실행 순서에 기대지 않으려고 일부러 자체 Update를 두지 않았다.
    /// 항해가 기준 자세를 옮기면 → 파도가 그 위에 heave/tilt를 얹고 → CarryRider가 그 합을 한 번에
    /// 플레이어에게 전달한다. 즉 항해 중에도 갑판에 선 플레이어는 한 번만 Move된다.
    ///
    /// ── 갑판 건축물 동반 (확인 완료) ────────────────────────────────────────────────
    /// 갑판 위 건축물은 DeckRoot/BuildDeckPieces, 상자·설치물은 DeckRoot/PlacedStructures 밑에 있고
    /// 둘 다 뗏목 루트의 자손이다. 여기서 옮기는 것은 **뗏목 루트 하나뿐**이므로 갑판 위의 모든 것이
    /// 1mm도 어긋나지 않고 함께 간다. BuildingSystem의 좌표 변환(WorldToSpace/SpaceToWorldRotation)도
    /// deckContainer의 **현재** 트랜스폼에서 매번 유도하므로, 움직이는 뗏목 위에서도 배치가 맞는다.
    ///
    /// ── 결정성 · 세이브 ────────────────────────────────────────────────────────────
    /// rng 소비 0이다. 바람도 해류도 Time.time의 순수 함수다(아래 WindAngleDegrees / DriftAngleDegrees).
    /// 항해 상태(위치·속도·닻)는 **저장하지 않는다**: SaveData는 수정 금지 대상이라 새 필드를 넣을 수
    /// 없고, 기존 필드에 억지로 끼워 넣으면 포맷이 조용히 바뀐다. 대신 불러오기 직후에는 뗏목이 다시
    /// 해안 정박 자세로 돌아온다(RaftStructure.TryAnchorToShore) - "항해는 세션 안의 행동"이라는 선.
    /// </summary>
    [DisallowMultipleComponent]
    public class RaftSailing : MonoBehaviour
    {
        // ── 추진 수치 (Stranded Deep 대응) ────────────────────────────────────────
        /// <summary>노 최고 속도(m/s). 바람과 무관하게 항상 낼 수 있는 최저 보장 속도다.</summary>
        public const float OarSpeed = 1.2f;

        /// <summary>돛 최고 속도(m/s). 순풍 + 거친 바다에서만 실제로 나온다(아래 SailFactor).</summary>
        public const float SailMaxSpeed = 4f;

        /// <summary>모터 최고 속도(m/s). 노의 5배 - 연료를 태우는 값어치가 있어야 한다.</summary>
        public const float MotorSpeed = 6f;

        /// <summary>후진 속도 배율. 뗏목은 뒤로 잘 가지 않는다(자리 고쳐 대기용).</summary>
        private const float ReverseFactor = 0.3f;

        /// <summary>가속/감속(m/s^2). 8m짜리 통나무 뗏목이라 관성이 크게 느껴지도록 낮게 둔다.</summary>
        private const float Acceleration = 0.7f;
        private const float Deceleration = 1.1f;

        /// <summary>키가 있을 때 / 없을 때의 선회 속도(도/초). 키가 있으면 180도에 8초.</summary>
        private const float TurnRateWithRudder = 22f;
        private const float TurnRateWithoutRudder = 4.5f;

        /// <summary>연료(Item_연료) 한 개로 모터가 도는 시간(초). 6m/s x 90s = 540m로 이웃 섬까지 왕복이 된다.</summary>
        public const float MotorSecondsPerFuel = 90f;

        /// <summary>인벤토리에서 연료를 찾을 때 쓰는 이름. 재료 대조는 이 프로젝트 관례대로 문자열이다.</summary>
        public const string FuelItemName = "연료";

        /// <summary>표류 속도(m/s). 조종을 그만두고 닻이 없을 때만 걸린다.</summary>
        private const float DriftSpeed = 0.35f;

        // ── 바람 모델 ────────────────────────────────────────────────────────────
        //
        // WeatherSystem에는 바람 개념이 없다(rainWind는 빗줄기 파티클을 기울이는 시각 값이고, 방향도
        // 고정 Vector2다). OceanWaves도 파도 성분 방향만 들고 있고 "지금 부는 바람"을 노출하지 않는다.
        // 그래서 바람은 여기가 소유한다 - 대신 **세기**는 이미 있는 거칠기(_MG_SeaState = 날씨 연동)에
        // 묶어, 폭풍이 오면 바람도 함께 세지도록 한다(두 개의 날씨가 생기지 않게).

        /// <summary>바람이 한 바퀴 도는 데 걸리는 시간(초). 0.6도/초 = 10분에 한 바퀴.</summary>
        private const float WindTurnDegreesPerSecond = 0.6f;

        /// <summary>바람 방향의 흔들림 진폭(도)과 주기(초). 일정하게만 돌면 기계처럼 읽힌다.</summary>
        private const float WindWobbleDegrees = 25f;
        private const float WindWobblePeriod = 170f;

        /// <summary>잔잔할 때 / 거칠 때의 바람 세기(m/s). 표시용이자 추력 배율의 근거다.</summary>
        private const float WindSpeedCalm = 3.5f;
        private const float WindSpeedStorm = 10f;

        // ── 부력 · 적재 ──────────────────────────────────────────────────────────
        /// <summary>갑판 위 건축 조각 하나의 적재 무게(부력 단위). 바닥판 한 칸(나무 1.0)의 절반 아래다.</summary>
        private const float PieceLoadUnits = 0.45f;

        /// <summary>보관 상자 본체 / 내용물 한 칸의 적재 무게.</summary>
        private const float ChestLoadUnits = 0.4f;
        private const float ChestItemLoadUnits = 0.06f;

        /// <summary>올라탄 플레이어의 적재 무게. Stranded Deep도 사람을 짐으로 센다.</summary>
        private const float RiderLoadUnits = 0.9f;

        /// <summary>이 비율까지는 아무 손해가 없다(적재 여유 구간).</summary>
        private const float LoadFreeRatio = 0.8f;

        /// <summary>
        /// 과적일 때 뗏목이 더 잠기는 최대 깊이(m). **0.25를 넘기지 않는다.**
        /// 갑판 윗면과 플레이어 수영 판정 수면의 최소 여유가 폭풍에서 +0.44m라(RaftStructure의
        /// waveHeaveScale 주석에 있는 실측치), 0.25m까지는 잠겨도 갑판 위에서 수영 모드로 넘어가지
        /// 않는다. 여기를 더 키우면 "짐을 실었더니 갑판에서 헤엄치기 시작한다"가 된다.
        /// </summary>
        private const float MaxOverloadSinkMeters = 0.25f;

        // ── 전복(완화판) ─────────────────────────────────────────────────────────
        //
        // [판단: 진짜 전복을 넣지 않는다 - 근거]
        //  1. 뗏목은 Rigidbody 없이 트랜스폼으로 구동된다(프로젝트 규칙). 뒤집힘은 자세만 바꾸면
        //     끝나는 일이 아니라 "뒤집힌 동안 갑판이 아래를 향한다"는 상태를 승선 판정 · 건축 공간 ·
        //     복원 절차가 전부 새로 다뤄야 한다. CarryRider의 승선 상자는 로컬 y >= 0.25를 "올라타
        //     있다"로 보므로, 뒤집힌 순간 플레이어는 승선 판정에서 빠져 지오메트리 사이로 떨어진다.
        //  2. 갑판 위 건축물은 DeckRoot의 자손이라 뗏목과 함께 뒤집힌다. BuildingSystem의 갑판 격자는
        //     "갑판 평면이 위를 향한다"를 전제로 셀을 깐다(DeckTopLocalY · IsDeckCellInBounds) -
        //     뒤집힌 상태에서 건축 입력이 들어오면 조용히 어긋난 자리에 조각이 놓인다.
        //  3. 세이브 포맷이 불변이라 "뒤집힌 상태"를 저장할 수 없다. 불러오면 멀쩡히 서 있는 뗏목이
        //     되므로, 전복은 원리적으로 F9 한 번에 사라지는 벌이다.
        //  4. 이 게임에는 Stranded Deep의 "뗏목이 근처에 떠 있고 헤엄쳐 돌아간다"가 없다. 대양에서
        //     뗏목을 잃으면 회수 수단이 아예 없어, 같은 사고의 체감 벌이 원작보다 훨씬 무겁다.
        // → 대신 **기울기 경고 + 속도 급감 + 지속되면 적재물 유실**로 간다. 유실은 갑판 상자의
        //    내용물에서만 일어난다(지은 집을 부수지 않는다 - 되돌릴 수 없는 손실을 만들지 않기 위해).

        /// <summary>안정적인 뗏목이 견디는 기울기(도). 폭풍 실측 최대치(4.8도)보다 위라 절대 안 걸린다.</summary>
        private const float DangerTiltStable = 8f;

        /// <summary>가장 불안정한 뗏목이 견디는 기울기(도). 폭풍(4.8도)에서는 걸리고 맑음(1.7도)에서는 안 걸린다.</summary>
        private const float DangerTiltUnstable = 3.2f;

        /// <summary>위험 상태가 이만큼 누적될 때마다 적재물 한 칸을 잃는다(초).</summary>
        private const float CargoLossSeconds = 8f;

        /// <summary>위험이 풀렸을 때 누적이 식는 속도 배율.</summary>
        private const float DangerCoolRate = 0.5f;

        /// <summary>위험 상태의 속도 배율.</summary>
        private const float DangerSpeedFactor = 0.4f;

        // ── 좌초 · 상륙 ──────────────────────────────────────────────────────────
        /// <summary>이보다 얕은 물(해수면 기준 깊이, m)은 좌초로 본다.</summary>
        private const float GroundingDepth = 0.9f;

        /// <summary>진행 방향으로 이만큼 앞을 미리 재서 얕은 물에 들어가는 것을 막는다(m).</summary>
        private const float LookAheadMeters = 3f;

        /// <summary>지형 탐침 주기(초). 6m/s로 달려도 한 주기에 0.6m라 앞보기 3m 안에 들어온다.</summary>
        private const float GroundProbeInterval = 0.1f;

        /// <summary>섬 반지름에서 이만큼 안쪽으로 들어와야 지형 탐침을 켠다(m). 먼바다에서는 레이 0발이다.</summary>
        private const float ShoreProbeMargin = 45f;

        // ── 노 젓기 대가 (스태미나가 없는 프로젝트라 허기/갈증으로 대신한다) ────────
        //
        // SurvivalStats에는 스태미나가 없다(체력/허기/갈증/일사병/산소가 전부다). 새 수치를 만들면
        // HUD · 세이브 · 밸런스 표가 전부 따라와야 하므로, 이미 있는 소모 축을 쓴다. 기본 감소가
        // 허기 0.05/s · 갈증 0.08/s이므로 노를 저으면 대략 두 배가 된다.
        private const float RowingHungerPerSecond = 0.05f;
        private const float RowingThirstPerSecond = 0.07f;

        /// <summary>적재/무게중심을 다시 재는 주기(초). 갑판이 바뀌면 즉시 한 번 더 잰다.</summary>
        private const float LoadRescanInterval = 2f;

        // ── 상태 ─────────────────────────────────────────────────────────────────
        private static RaftSailing activeInstance;

        /// <summary>씬에 살아 있는 항해 컴포넌트. 없으면 null.</summary>
        public static RaftSailing Active => activeInstance != null ? activeInstance : null;

        private RaftStructure raft;
        private WorldMapManager worldMap;
        private PlayerController player;
        private CharacterController playerController;
        private SurvivalStats playerStats;
        private PlayerInventory playerInventory;
        private float playerRescanTimer;

        /// <summary>연료를 찾지 못한 직후 다시 뒤지기까지 기다리는 시간(초). 매 프레임 소지품을 훑지 않기 위한 값.</summary>
        private float fuelSearchCooldown;

        /// <summary>조종 모드인가.</summary>
        public bool IsSteering { get; private set; }

        /// <summary>닻을 내렸는가. 닻 부품이 없으면 항상 false다.</summary>
        public bool AnchorDown { get; private set; }

        /// <summary>한 번이라도 조종을 시작했는가. 표류는 이 뒤에만 일어난다(제작 중 뗏목이 떠내려가지 않게).</summary>
        public bool HasLaunched { get; private set; }

        /// <summary>지금 좌초(해변에 얹힘) 상태인가.</summary>
        public bool IsBeached { get; private set; }

        /// <summary>지금 속도(m/s, 부호 있음 - 음수는 후진).</summary>
        public float Speed { get; private set; }

        /// <summary>지금 실제로 뗏목을 미는 수단. 없으면 RaftPart.None.</summary>
        public RaftPart ActivePropulsion { get; private set; }

        /// <summary>모터에 남은 연료 시간(초). 0이면 다음 가동에서 인벤토리 연료 1개를 태운다.</summary>
        public float FuelSeconds { get; private set; }

        /// <summary>적재량 / 부력(1을 넘으면 과적).</summary>
        public float LoadRatio { get; private set; }

        /// <summary>지금 적재량과 부력 한도(부력 단위).</summary>
        public float LoadUnits { get; private set; }
        public float LoadCapacity { get; private set; }

        /// <summary>기울기 위험(전복 위험) 상태인가.</summary>
        public bool StabilityWarning { get; private set; }

        /// <summary>가장 가까운 섬까지의 거리(m). 없으면 float.MaxValue.</summary>
        public float NearestIslandDistance { get; private set; }

        private float headingDegrees;
        private bool hasHeading;
        private float baseSeaLevelY;
        private bool hasBaseSeaLevel;

        private float loadRescanTimer;
        private float cachedCargoUnits;
        private float comHeight;
        private float dangerSeconds;

        private float groundProbeTimer;
        private bool blockedAhead;
        private Vector3 lastProbeDirection;

        private IslandInstance nearestIsland;

        private Transform deckPiecesContainer;
        private readonly List<StorageChest> chestBuffer = new List<StorageChest>();

        // ─────────────────────────────────────────────────────────────────────────
        //  바람 · 해류 (전부 Time.time의 순수 함수 - rng 소비 0)
        // ─────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// 지금 바람이 **불어 가는** 방향(도, 0 = +Z(북), 시계 방향). 천천히 한 방향으로 돌면서
        /// 느린 흔들림을 얹는다. 파도 시계와 같은 Time.time을 쓰므로 timeScale = 0에서 함께 멈춘다.
        /// </summary>
        public static float WindAngleDegrees
        {
            get
            {
                float t = Time.time;
                return 37f + t * WindTurnDegreesPerSecond
                    + WindWobbleDegrees * Mathf.Sin(t * (2f * Mathf.PI / WindWobblePeriod));
            }
        }

        /// <summary>지금 바람 세기(m/s). 바다 거칠기(_MG_SeaState = 날씨 연동)에 묶여 있다.</summary>
        public static float WindSpeed
        {
            get { return Mathf.Lerp(WindSpeedCalm, WindSpeedStorm, Mathf.Clamp01(OceanWaves.Roughness01)); }
        }

        /// <summary>지금 바람이 불어 가는 방향의 수평 단위벡터.</summary>
        public static Vector3 WindDirection
        {
            get
            {
                float rad = WindAngleDegrees * Mathf.Deg2Rad;
                return new Vector3(Mathf.Sin(rad), 0f, Mathf.Cos(rad));
            }
        }

        /// <summary>해류(표류) 방향(도). 바람보다 훨씬 느리게 돈다 - 사실상 고정된 한 방향이다.</summary>
        public static float DriftAngleDegrees
        {
            get { return 118f + Time.time * 0.09f; }
        }

        /// <summary>해류가 흐르는 방향의 수평 단위벡터.</summary>
        public static Vector3 DriftDirection
        {
            get
            {
                float rad = DriftAngleDegrees * Mathf.Deg2Rad;
                return new Vector3(Mathf.Sin(rad), 0f, Mathf.Cos(rad));
            }
        }

        // ─────────────────────────────────────────────────────────────────────────
        //  수명 주기
        // ─────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// 도메인 리로드를 끈 플레이 모드에서 이전 실행의 인스턴스를 들고 시작하지 않게 한다
        /// (프로젝트 공통 R1 리셋 훅 - RaftStructure.ResetStatics와 같은 규약).
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            activeInstance = null;
        }

        private void Awake()
        {
            raft = GetComponent<RaftStructure>();
            activeInstance = this;
        }

        private void OnEnable()
        {
            if (activeInstance == null)
                activeInstance = this;
        }

        private void OnDisable()
        {
            // 조종 잠금을 들고 죽으면 플레이어가 영영 못 움직인다. 어떤 경로로 꺼지든 반드시 푼다.
            ReleasePlayerLock();
            IsSteering = false;
        }

        private void OnDestroy()
        {
            if (activeInstance == this)
                activeInstance = null;
        }

        // ─────────────────────────────────────────────────────────────────────────
        //  조종 진입 / 이탈
        // ─────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// 지금 조타 자리에 들어갈 수 있는가. 못 들어가면 사유를 돌려준다(프롬프트가 그대로 적는다).
        ///
        /// [Stranded Deep 대응] 그 게임은 **노(paddle)를 달아야 조종 위치에 앉을 수 있다.** 여기서도
        /// 추진 수단(RaftStructure.HasPropulsion = 노 / 모터 / 돛+키) 없이는 조종에 들어갈 수 없다.
        /// 조타 자리 자체는 노·키·모터 중 하나만 있어도 생기므로(RaftStructure가 만든다), "자리는
        /// 보이는데 왜 못 타지"에는 항상 아래 사유가 붙는다.
        /// </summary>
        public bool CanEnterSteering(out string reason)
        {
            reason = string.Empty;

            if (raft == null || raft.BaseTileCount <= 0)
            {
                reason = "뗏목이 없다";
                return false;
            }

            if (raft.BaseTileCount < RaftStructure.SeaworthyTileCount)
            {
                reason = $"바닥판 {RaftStructure.SeaworthyTileCount}칸 이상 필요";
                return false;
            }

            if (!raft.HasPropulsion)
            {
                reason = "추진 수단 필요 - 노 · 모터 · 돛+키 중 하나";
                return false;
            }

            if (!raft.IsRiderAboard)
            {
                reason = "뗏목에 올라타야 한다";
                return false;
            }

            return true;
        }

        /// <summary>조종 모드에 들어간다. 조건이 안 맞으면 사유를 돌려주고 아무것도 바꾸지 않는다.</summary>
        public bool TryEnterSteering(out string reason)
        {
            if (IsSteering)
            {
                reason = string.Empty;
                return true;
            }

            if (!CanEnterSteering(out reason))
            {
                AudioManager.Instance?.PlayActionFail();
                return false;
            }

            EnsurePlayer(true);
            if (player == null)
            {
                // 이동 잠금을 걸 대상이 없으면 조종에 들어가면 안 된다 - 배는 도는데 플레이어는
                // 갑판 위를 걸어 나가는 상태가 된다(승선 판정을 통과했으니 정상 경로에서는 안 온다).
                reason = "플레이어를 찾을 수 없다";
                AudioManager.Instance?.PlayActionFail();
                return false;
            }

            IsSteering = true;
            HasLaunched = true;

            // 조타 자리(고물 한가운데)로 옮겨 세운다. 여기서만 순간이동을 쓰고, 이후에는 CarryRider가
            // 뗏목 로컬 좌표를 보존하며 옮긴다(회전은 건드리지 않는다 - 시야를 뺏지 않기 위해서).
            SnapPlayerToHelm();
            ApplyPlayerLock();

            AudioManager.Instance?.PlayCraftSuccess();
            return true;
        }

        /// <summary>조종 모드에서 나온다. 플레이어 이동 잠금을 반드시 푼다.</summary>
        public void ExitSteering()
        {
            if (!IsSteering)
                return;

            IsSteering = false;
            ReleasePlayerLock();
            AudioManager.Instance?.PlayPickup();
        }

        /// <summary>닻을 올리거나 내린다. 닻 부품이 없으면 아무 일도 하지 않는다.</summary>
        public bool ToggleAnchor()
        {
            if (raft == null || !raft.HasPart(RaftPart.Anchor))
            {
                AudioManager.Instance?.PlayActionFail();
                return false;
            }

            AnchorDown = !AnchorDown;
            if (AnchorDown)
                Speed = 0f;

            AudioManager.Instance?.PlayHit();
            return true;
        }

        // ─────────────────────────────────────────────────────────────────────────
        //  매 프레임 (RaftStructure.Update가 UpdateWaveMotion 직전에 부른다)
        // ─────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// 조종 입력 → 추진 → 선회 → 좌초 판정 → 뗏목 기준 자세 갱신. 프레임당 할당 0이다.
        ///
        /// **호출 지점은 RaftStructure.Update 하나뿐이다.** 자체 Update를 두면 파도 흔들림과의 순서가
        /// 스크립트 실행 순서에 좌우되어, 어떤 프레임에는 파도가 옛 기준으로 계산된다.
        /// </summary>
        public void TickNavigation()
        {
            if (raft == null)
                return;

            // 타이틀/일시정지/엔딩(timeScale = 0)에서는 바다와 함께 완전히 멈춘다. 입력도 받지 않는다.
            if (Time.timeScale <= 0f)
                return;

            float dt = Time.deltaTime;
            if (dt <= 0f)
                return;

            EnsureReferences();
            fuelSearchCooldown = Mathf.Max(0f, fuelSearchCooldown - dt);

            if (!hasHeading)
            {
                headingDegrees = raft.HullBaseRotation.eulerAngles.y;
                hasHeading = true;
            }

            if (!hasBaseSeaLevel)
            {
                baseSeaLevelY = raft.HullBasePosition.y;
                hasBaseSeaLevel = true;
            }

            RefreshLoad(dt);
            UpdateStability(dt);

            // 뗏목이 없으면(제작 예정지) 항해 자체가 성립하지 않는다. 조종 중이었다면 내보낸다.
            if (raft.BaseTileCount <= 0)
            {
                if (IsSteering)
                    ExitSteering();
                Speed = 0f;
                ActivePropulsion = RaftPart.None;
                return;
            }

            float steerInput = 0f;
            float throttleInput = 0f;

            if (IsSteering)
            {
                ReadSteeringInput(out throttleInput, out steerInput);
                HandleAnchorKey();
            }

            UpdateHeading(steerInput, dt);
            UpdateSpeed(throttleInput, dt);

            Vector3 basePosition = raft.HullBasePosition;
            Vector3 forward = Quaternion.Euler(0f, headingDegrees, 0f) * Vector3.forward;

            Vector3 step = forward * (Speed * dt);
            step += ComputeDriftStep(dt);

            if (step.sqrMagnitude > 1e-8f)
            {
                if (IsBlockedTowards(basePosition, step))
                {
                    // 좌초. 앞으로는 못 가고 속도만 죽는다(뒤로는 갈 수 있다 - 아래 IsBlockedTowards가
                    // 진행 방향만 보므로, 후진 입력은 다시 깊은 물 쪽을 재게 되어 통과한다).
                    Speed = 0f;
                    step = Vector3.zero;
                    NotifyLandfall();
                }
                else
                {
                    IsBeached = false;
                }
            }

            basePosition += step;
            basePosition.y = baseSeaLevelY - ComputeOverloadSink();

            raft.SetHullBase(basePosition, Quaternion.Euler(0f, headingDegrees, 0f));

            ConsumeRowingStamina(throttleInput, dt);
        }

        // ─────────────────────────────────────────────────────────────────────────
        //  입력
        // ─────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// 조종 중의 WASD. 플레이어 이동은 PlayerController.MovementSuspended로 꺼져 있으므로
        /// 같은 키가 두 곳에서 소비되지 않는다(시점 회전 HandleLook은 그대로 살아 있다).
        ///
        /// GetAxisRaw를 쓰는 이유: GetAxis의 부드럽기(smoothing)는 사람 걸음에 맞춘 값이라, 관성이
        /// 큰 배에 겹치면 입력을 놓은 뒤에도 한참 더 도는 것처럼 느껴진다. 관성은 여기서 직접 준다.
        /// </summary>
        private void ReadSteeringInput(out float throttle, out float steer)
        {
            throttle = Mathf.Clamp(Input.GetAxisRaw("Vertical"), -1f, 1f);
            steer = Mathf.Clamp(Input.GetAxisRaw("Horizontal"), -1f, 1f);
        }

        /// <summary>
        /// 닻 토글 키(F).
        ///
        /// [키 선택 근거] 감독이 제안한 두 후보를 실제 배치와 대조했다.
        ///  · Space = PlayerController의 점프이자 수영 상승 키다(Input "Jump"). 조종 중에는 이동이
        ///    꺼져 있어 당장은 안 겹치지만, 조종을 그만두는 순간 같은 키가 점프로 돌아온다 - 토글
        ///    키로 쓰면 "내리자마자 뛴다"가 된다. 탈락.
        ///  · F = InventoryUI.cycleFilterKey. 다만 그쪽은 **인벤토리 창이 열려 있을 때만** 받는다.
        ///    창이 열리면 CursorLockController가 커서를 풀므로, 여기서 "커서가 잠겨 있을 때만"이라는
        ///    조건 하나로 두 소비가 완전히 갈린다. 채택.
        /// </summary>
        private void HandleAnchorKey()
        {
            if (Cursor.lockState != CursorLockMode.Locked)
                return;

            if (Input.GetKeyDown(KeyCode.F))
                ToggleAnchor();
        }

        // ─────────────────────────────────────────────────────────────────────────
        //  추진
        // ─────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// 돛의 추력 배율(0~1). 바람이 불어 가는 방향과 뱃머리의 각도만으로 정한다.
        ///  · 정확한 순풍(각 0도)  → 1.0
        ///  · 옆바람(각 90도)      → 0.375
        ///  · 맞바람(각 126도 이상) → 0.0  (돛이 죽는 구간 - 지그재그로 돌아가야 한다)
        /// 실제 범선의 극곡선(옆바람이 가장 빠르다)과는 다르지만, 화면에 각도계가 없는 게임에서는
        /// "바람을 등지면 빠르다"가 즉시 읽히는 쪽이 옳다.
        /// </summary>
        public static float SailFactor(Vector3 raftForward, Vector3 windDirection)
        {
            float alignment = Vector3.Dot(raftForward, windDirection);
            return Mathf.Clamp01((alignment + 0.6f) / 1.6f);
        }

        /// <summary>
        /// 지금 낼 수 있는 최고 속도와 그 수단. 세 수단 중 **가장 빠른 것**이 자동으로 뽑힌다 -
        /// 모드 전환 키를 따로 두지 않는 이유는, 연료가 떨어졌을 때 돛으로, 바람이 죽었을 때 노로
        /// 자연히 내려앉는 것이 플레이어가 기대하는 동작이기 때문이다.
        /// </summary>
        private float ResolveTopSpeed(Vector3 forward, float dt, bool wantsThrust)
        {
            float best = 0f;
            RaftPart bestPart = RaftPart.None;

            if (raft.HasPart(RaftPart.Oar))
            {
                best = OarSpeed;
                bestPart = RaftPart.Oar;
            }

            if (raft.HasPart(RaftPart.Sail))
            {
                // 키가 없는 돛은 방향을 못 잡아 절반밖에 못 쓴다(HasPropulsion도 돛만으로는 인정하지 않는다).
                float rudderFactor = raft.HasPart(RaftPart.Rudder) ? 1f : 0.5f;
                float windFactor = Mathf.Lerp(0.7f, 1f, Mathf.Clamp01(OceanWaves.Roughness01));
                float sail = SailMaxSpeed * SailFactor(forward, WindDirection) * windFactor * rudderFactor;
                if (sail > best)
                {
                    best = sail;
                    bestPart = RaftPart.Sail;
                }
            }

            if (raft.HasPart(RaftPart.Motor) && MotorSpeed > best)
            {
                // 연료가 남아 있거나, 지금 인벤토리에서 한 개를 태울 수 있으면 모터가 이긴다.
                if (FuelSeconds > 0f || (wantsThrust && TryBurnFuelUnit()))
                {
                    best = MotorSpeed;
                    bestPart = RaftPart.Motor;
                }
            }

            // 모터를 실제로 쓰는 동안에만 연료가 준다(정박·표류 중에는 타지 않는다).
            if (bestPart == RaftPart.Motor && wantsThrust)
                FuelSeconds = Mathf.Max(0f, FuelSeconds - dt);

            ActivePropulsion = bestPart;
            return best;
        }

        /// <summary>
        /// 인벤토리에서 연료 1개를 소모해 모터 가동 시간을 채운다. 없으면 false.
        /// 소모 방식은 RaftBuildCatalog.TryBuild와 같다(뒤에서부터 이름 대조로 한 칸을 지우고
        /// 마지막에 NotifyInventoryChanged를 한 번만 부른다) - 재료 소모 규약이 두 벌이 되지 않게.
        ///
        /// 연료가 없을 때 매 프레임 소지품을 훑지 않도록 짧은 쿨다운을 둔다(최대 100칸 x 60Hz 방지).
        /// </summary>
        private bool TryBurnFuelUnit()
        {
            if (fuelSearchCooldown > 0f)
                return false;

            if (playerInventory == null || playerInventory.items == null)
            {
                fuelSearchCooldown = 1f;
                return false;
            }

            for (int i = playerInventory.items.Count - 1; i >= 0; i--)
            {
                InventoryItem item = playerInventory.items[i];
                if (item == null || item.data == null || item.data.itemName != FuelItemName)
                    continue;

                playerInventory.items.RemoveAt(i);
                playerInventory.NotifyInventoryChanged();
                FuelSeconds = MotorSecondsPerFuel;
                return true;
            }

            fuelSearchCooldown = 1f;
            return false;
        }

        /// <summary>목표 속도로 서서히 붙는다. 닻을 내렸거나 조종 중이 아니면 목표는 0이다.</summary>
        private void UpdateSpeed(float throttle, float dt)
        {
            Vector3 forward = Quaternion.Euler(0f, headingDegrees, 0f) * Vector3.forward;

            float target = 0f;
            if (IsSteering && !AnchorDown && Mathf.Abs(throttle) > 0.01f)
            {
                float top = ResolveTopSpeed(forward, dt, throttle > 0f);
                target = throttle > 0f
                    ? top * throttle                        // 전진: 입력 크기만큼
                    : top * ReverseFactor * throttle;       // 후진: 부호는 throttle이 이미 음수다
            }
            else
            {
                // 입력이 없어도 지금 쓸 수 있는 수단은 계속 갱신한다(프롬프트가 그 값을 읽는다).
                ResolveTopSpeed(forward, dt, false);
            }

            target *= LoadSpeedFactor();
            if (StabilityWarning)
                target *= DangerSpeedFactor;

            float rate = Mathf.Abs(target) > Mathf.Abs(Speed) ? Acceleration : Deceleration;
            Speed = Mathf.MoveTowards(Speed, target, rate * dt);
        }

        /// <summary>
        /// 선회. 키가 있으면 빠르고, 없으면 거의 직진만 된다(노 하나로 방향을 트는 감각).
        /// 속도가 0이면 뗏목은 제자리에서 돌지 않는다 - 물살을 받아야 방향이 바뀐다.
        /// </summary>
        private void UpdateHeading(float steer, float dt)
        {
            if (Mathf.Abs(steer) < 0.01f || AnchorDown)
                return;

            float rate = raft.HasPart(RaftPart.Rudder) ? TurnRateWithRudder : TurnRateWithoutRudder;

            // 정지 상태에서도 노/장대로 뱃머리를 조금은 돌릴 수 있게 최소 30%를 남긴다.
            float speedFactor = Mathf.Lerp(0.3f, 1f, Mathf.Clamp01(Mathf.Abs(Speed) / OarSpeed));
            headingDegrees += steer * rate * speedFactor * dt;

            if (headingDegrees > 360f)
                headingDegrees -= 360f;
            else if (headingDegrees < 0f)
                headingDegrees += 360f;
        }

        /// <summary>
        /// 표류. 닻이 없으면 조종을 그만둔 순간부터 해류를 타고 천천히 떠내려간다.
        /// **첫 조종 전에는 절대 흐르지 않는다**(HasLaunched) - 해안에서 뗏목을 만드는 동안 제작
        /// 예정지가 떠내려가면 바닥판을 놓을 자리가 사라진다.
        /// </summary>
        private Vector3 ComputeDriftStep(float dt)
        {
            if (IsSteering || AnchorDown || !HasLaunched || IsBeached)
                return Vector3.zero;

            return DriftDirection * (DriftSpeed * dt);
        }

        /// <summary>노를 젓는 동안의 허기/갈증 추가 소모. 노가 실제 추진 수단일 때만 붙는다.</summary>
        private void ConsumeRowingStamina(float throttle, float dt)
        {
            if (!IsSteering || ActivePropulsion != RaftPart.Oar || Mathf.Abs(throttle) < 0.01f)
                return;

            if (playerStats == null || playerStats.IsDead)
                return;

            playerStats.hunger = Mathf.Max(0f, playerStats.hunger - RowingHungerPerSecond * dt);
            playerStats.thirst = Mathf.Max(0f, playerStats.thirst - RowingThirstPerSecond * dt);
        }

        // ─────────────────────────────────────────────────────────────────────────
        //  부력 · 적재 · 안정성
        // ─────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// 갑판 위의 짐 무게와 무게중심 높이를 다시 잰다. 갑판 구성은 자주 바뀌지 않으므로
        /// LoadRescanInterval마다만 훑는다(GetComponentsInChildren은 List 오버로드라 할당이 없다).
        /// </summary>
        private void RefreshLoad(float dt)
        {
            LoadCapacity = raft.TotalBuoyancy;

            loadRescanTimer -= dt;
            bool riderAboard = raft.IsRiderAboard;

            if (loadRescanTimer > 0f)
            {
                // 사람이 타고 내리는 것만 즉시 반영한다(짐 스캔은 다음 주기에).
                LoadUnits = cachedCargoUnits + (riderAboard ? RiderLoadUnits : 0f);
                LoadRatio = LoadCapacity > 0.01f ? LoadUnits / LoadCapacity : 0f;
                return;
            }

            loadRescanTimer = LoadRescanInterval;

            Transform deckRoot = raft.DeckRoot;
            float cargo = 0f;
            float weightedHeight = 0f;

            if (deckRoot != null)
            {
                if (deckPiecesContainer == null)
                    deckPiecesContainer = deckRoot.Find("BuildDeckPieces");

                cargo += AccumulatePieces(raft.PlacedStructures, deckRoot, ref weightedHeight);
                cargo += AccumulatePieces(deckPiecesContainer, deckRoot, ref weightedHeight);

                // 보관 상자는 내용물까지 센다. Stranded Deep에서 뗏목이 뒤집히는 가장 흔한 이유가
                // "상자를 잔뜩 쌓았다"이므로, 내용물 무게를 빼면 이 시스템의 핵심이 사라진다.
                chestBuffer.Clear();
                deckRoot.GetComponentsInChildren(true, chestBuffer);
                for (int i = 0; i < chestBuffer.Count; i++)
                {
                    StorageChest chest = chestBuffer[i];
                    if (chest == null)
                        continue;

                    float units = ChestLoadUnits + ChestItemLoadUnits * chest.State.items.Count;
                    cargo += units;
                    weightedHeight += units * Mathf.Max(0f,
                        deckRoot.InverseTransformPoint(chest.transform.position).y);
                }
            }

            cachedCargoUnits = cargo;
            comHeight = cargo > 0.01f ? weightedHeight / cargo : 0f;

            LoadUnits = cargo + (riderAboard ? RiderLoadUnits : 0f);
            LoadRatio = LoadCapacity > 0.01f ? LoadUnits / LoadCapacity : 0f;
        }

        /// <summary>컨테이너의 직계 자식을 건축 조각 하나씩으로 세고, 무게중심 높이에 누적한다.</summary>
        private float AccumulatePieces(Transform container, Transform deckRoot, ref float weightedHeight)
        {
            if (container == null)
                return 0f;

            float total = 0f;
            for (int i = 0; i < container.childCount; i++)
            {
                Transform child = container.GetChild(i);
                if (child == null || !child.gameObject.activeSelf)
                    continue;

                total += PieceLoadUnits;
                weightedHeight += PieceLoadUnits * Mathf.Max(0f,
                    deckRoot.InverseTransformPoint(child.position).y);
            }
            return total;
        }

        /// <summary>과적일 때의 속도 배율. 여유 구간(0.8)까지는 손해가 없다.</summary>
        private float LoadSpeedFactor()
        {
            if (LoadRatio <= LoadFreeRatio)
                return 1f;

            if (LoadRatio <= 1f)
                return Mathf.Lerp(1f, 0.85f, (LoadRatio - LoadFreeRatio) / (1f - LoadFreeRatio));

            return Mathf.Lerp(0.85f, 0.35f, Mathf.Clamp01((LoadRatio - 1f) / 0.6f));
        }

        /// <summary>과적으로 뗏목이 더 잠기는 깊이(m). 상한은 MaxOverloadSinkMeters 주석 참고.</summary>
        private float ComputeOverloadSink()
        {
            float t = Mathf.Clamp01((LoadRatio - 0.7f) / 0.9f);
            return t * MaxOverloadSinkMeters;
        }

        /// <summary>
        /// 무게중심 높이 + 적재율 + 지금 파도의 기울기로 "전복 위험"을 판정한다.
        /// 진짜로 뒤집지는 않는다 - 판단 근거는 이 파일 위쪽 [판단: 진짜 전복을 넣지 않는다] 참고.
        /// </summary>
        private void UpdateStability(float dt)
        {
            // 무게중심 4m를 완전히 위태로운 상태로 본다(갑판 위 2층 집 + 지붕 정도).
            float topHeavy = Mathf.Clamp01(comHeight / 4f);
            float instability = Mathf.Clamp01(0.6f * topHeavy + 0.4f * Mathf.Clamp01(LoadRatio));
            float dangerTilt = Mathf.Lerp(DangerTiltStable, DangerTiltUnstable, instability);

            bool danger = raft.BaseTileCount > 0 && raft.CurrentTiltDegrees > dangerTilt;
            StabilityWarning = danger;

            if (danger)
            {
                dangerSeconds += dt;
                if (dangerSeconds >= CargoLossSeconds)
                {
                    dangerSeconds = 0f;
                    LoseOneCargoItem();
                }
            }
            else
            {
                dangerSeconds = Mathf.Max(0f, dangerSeconds - dt * DangerCoolRate);
            }
        }

        /// <summary>
        /// 갑판 상자에서 짐 한 칸을 잃는다(바다에 쓸려 나갔다). **지은 건축물은 절대 부수지 않는다** -
        /// 되돌릴 수 없는 손실은 이 완화안의 취지에 어긋난다.
        /// </summary>
        private void LoseOneCargoItem()
        {
            for (int i = chestBuffer.Count - 1; i >= 0; i--)
            {
                StorageChest chest = chestBuffer[i];
                if (chest == null || chest.State.items.Count <= 0)
                    continue;

                chest.State.items.RemoveAt(chest.State.items.Count - 1);
                chest.NotifyChanged();
                AudioManager.Instance?.PlayBreak();
                return;
            }
        }

        // ─────────────────────────────────────────────────────────────────────────
        //  좌초 · 상륙
        // ─────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// 이 방향으로 더 가면 얕은 물(좌초)인가.
        ///
        /// [비용] 먼바다에서는 **레이를 한 발도 쏘지 않는다.** 가장 가까운 섬까지의 거리를 먼저
        /// 재고(섬 50개 float 비교 - 할당 0), 섬 반지름 + ShoreProbeMargin 안에 들어왔을 때만
        /// GroundProbeInterval(0.1초)마다 아래로 레이를 한 발 쏜다. 6m/s로 달려도 한 주기에 0.6m라
        /// 앞보기 3m 안에 충분히 들어온다.
        /// </summary>
        private bool IsBlockedTowards(Vector3 basePosition, Vector3 step)
        {
            Vector3 direction = step.normalized;

            // 진행 방향이 크게 바뀌면(예: 좌초한 뒤 후진) 캐시를 버리고 즉시 다시 잰다. 이게 없으면
            // 앞이 막혔다는 판정이 최대 한 주기 동안 뒤로 빼는 것까지 막아 "얹혀서 못 빠져나온다"가 된다.
            bool directionChanged = Vector3.Dot(direction, lastProbeDirection) < 0.85f;

            groundProbeTimer -= Time.deltaTime;
            if (groundProbeTimer > 0f && !directionChanged)
                return blockedAhead;

            groundProbeTimer = GroundProbeInterval;
            lastProbeDirection = direction;

            UpdateNearestIsland(basePosition);
            if (nearestIsland == null)
            {
                blockedAhead = false;
                return false;
            }

            float shoreRadius = IslandSizeMetrics.GetTerrainRadius(nearestIsland.size) + ShoreProbeMargin;
            if (NearestIslandDistance > shoreRadius)
            {
                blockedAhead = false;
                return false;
            }

            Vector3 probeAt = basePosition
                + direction * (RaftStructure.DeckLength * 0.5f + LookAheadMeters);

            float aheadY = raft.SampleTerrainHeight(probeAt, out bool aheadHit);
            if (!aheadHit || aheadY <= baseSeaLevelY - GroundingDepth)
            {
                // 앞이 충분히 깊다(또는 지형이 아예 없다 = 열린 바다).
                blockedAhead = false;
                return false;
            }

            // 앞이 얕다. 그래도 **지금 있는 자리보다 깊어지는 방향이면 통과시킨다.**
            // 이게 없으면 두 가지가 통째로 막힌다:
            //  · 시작 해안. 뗏목은 태생부터 물가에 걸쳐 있으므로(TryAnchorToShore), 얕다는 이유만으로
            //    막으면 첫 항해를 영영 시작할 수 없다.
            //  · 좌초 탈출. 얹힌 뗏목은 사방이 얕으니 후진도 함께 막혀 영구히 갇힌다.
            // 즉 판정의 실제 의미는 "얕은 곳"이 아니라 "**지금보다 더 얕아지는 쪽**"이다.
            float hereY = raft.SampleTerrainHeight(basePosition, out bool hereHit);
            float hereGround = hereHit ? hereY : float.MinValue;

            blockedAhead = aheadY >= hereGround - 0.02f;
            return blockedAhead;
        }

        /// <summary>가장 가까운 섬을 찾는다. 리스트 인덱스 순회라 할당이 0이다.</summary>
        private void UpdateNearestIsland(Vector3 position)
        {
            nearestIsland = null;
            NearestIslandDistance = float.MaxValue;

            if (worldMap == null || worldMap.islands == null)
                return;

            for (int i = 0; i < worldMap.islands.Count; i++)
            {
                IslandInstance island = worldMap.islands[i];
                if (island == null)
                    continue;

                float dx = island.mapPosition.x - position.x;
                float dz = island.mapPosition.z - position.z;
                float distance = Mathf.Sqrt(dx * dx + dz * dz);
                if (distance < NearestIslandDistance)
                {
                    NearestIslandDistance = distance;
                    nearestIsland = island;
                }
            }
        }

        /// <summary>
        /// 좌초했다. 뗏목은 그 자리에 멈추고, 닿은 섬은 지도에 드러난다(WorldMapManager.DiscoverIsland).
        /// 지도(M) 순간이동(IslandTravel)은 건드리지 않는다 - 실제 항해는 그것과 **별개의 선택지**다.
        /// </summary>
        private void NotifyLandfall()
        {
            if (IsBeached)
                return;

            IsBeached = true;

            if (worldMap != null && nearestIsland != null && !nearestIsland.isDiscovered)
                worldMap.DiscoverIsland(nearestIsland.islandId);
        }

        // ─────────────────────────────────────────────────────────────────────────
        //  플레이어 결속
        // ─────────────────────────────────────────────────────────────────────────

        private void EnsureReferences()
        {
            if (worldMap == null)
                worldMap = FindAnyObjectByType<WorldMapManager>();

            EnsurePlayer();
        }

        /// <summary>
        /// 플레이어 참조를 확보한다. 프레임당 전역 검색을 하지 않도록 주기를 둔다.
        /// <paramref name="force"/>가 참이면 주기를 무시하고 즉시 찾는다 - 조종에 들어가는 순간에는
        /// "아직 못 찾았다"로 이동 잠금이 빠지면 배와 플레이어가 따로 논다.
        /// </summary>
        private void EnsurePlayer(bool force = false)
        {
            if (player != null)
                return;

            if (!force)
            {
                playerRescanTimer -= Time.unscaledDeltaTime;
                if (playerRescanTimer > 0f)
                    return;
            }

            playerRescanTimer = 1f;
            player = FindAnyObjectByType<PlayerController>();
            if (player == null)
                return;

            playerController = player.GetComponent<CharacterController>();
            playerInventory = player.GetComponent<PlayerInventory>();
            if (playerInventory == null)
                playerInventory = FindAnyObjectByType<PlayerInventory>();

            playerStats = player.survivalStats != null
                ? player.survivalStats
                : player.GetComponent<SurvivalStats>();
        }

        private void ApplyPlayerLock()
        {
            if (player != null)
                player.MovementSuspended = true;
        }

        private void ReleasePlayerLock()
        {
            if (player != null)
                player.MovementSuspended = false;
        }

        /// <summary>
        /// 플레이어를 조타 자리(고물 한가운데 갑판 위)에 세운다. CharacterController는 트랜스폼을
        /// 직접 옮기면 내부 위치가 어긋나므로, 이 프로젝트의 검증된 순간이동 절차(껐다 켜기 +
        /// SyncTransforms)를 그대로 쓴다(SaveLoadController.TeleportPlayer와 같은 규약).
        /// 회전은 건드리지 않는다 - 조종 중에도 시점은 온전히 플레이어의 것이다.
        /// </summary>
        private void SnapPlayerToHelm()
        {
            if (player == null)
                return;

            Vector3 target = raft.transform.TransformPoint(new Vector3(
                0f,
                RaftStructure.DeckSurfaceY + 0.05f,
                -RaftStructure.DeckLength * 0.5f + 0.9f));

            bool wasEnabled = playerController != null && playerController.enabled;
            if (wasEnabled)
                playerController.enabled = false;

            player.transform.position = target;

            if (wasEnabled)
                playerController.enabled = true;

            Physics.SyncTransforms();
        }

        // ─────────────────────────────────────────────────────────────────────────
        //  표시 문구 (InteractionPromptUI가 그대로 쓰는 단일 출처)
        // ─────────────────────────────────────────────────────────────────────────

        /// <summary>조타 자리를 조준했을 때의 주 문구.</summary>
        public string GetHelmHeadline(string key)
        {
            return IsSteering ? $"{key} 조종 그만두기" : $"{key} 조종 시작";
        }

        /// <summary>
        /// 조타 자리 조준 시의 보조 문구. 못 들어가면 사유가, 들어갈 수 있으면 지금 쓸 수 있는
        /// 추진 수단과 바람이 나온다.
        /// </summary>
        public string GetHelmDetail(out bool blocked)
        {
            blocked = false;

            if (IsSteering)
                return GetSteeringDetail();

            if (!CanEnterSteering(out string reason))
            {
                blocked = true;
                return reason;
            }

            return $"{DescribePropulsion()} · {DescribeWind()}";
        }

        /// <summary>조종 중 화면에 항상 떠 있는 주 문구(속도 + 조작 안내).</summary>
        public string GetSteeringHeadline(string key)
        {
            if (IsBeached)
                return $"상륙 - [{key}] 내려서 섬으로";

            if (AnchorDown)
                return $"정박 중 · [F] 닻 올리기 · [{key}] 조종 그만두기";

            return $"항해 {Mathf.Abs(Speed):F1} m/s · [WASD] 조종 · [{key}] 그만두기";
        }

        /// <summary>
        /// 뱃머리가 향한 수평 방향. **파도 기울기를 걷어낸 순수 방위각**이라, 추력 계산과 화면 문구가
        /// 같은 값을 본다(raft.transform.forward를 쓰면 기울기만큼 y 성분이 섞여 둘이 미세하게 갈린다).
        /// </summary>
        public Vector3 HullForward => Quaternion.Euler(0f, headingDegrees, 0f) * Vector3.forward;

        /// <summary>조종 중의 보조 문구(추진 수단 · 바람 · 적재 · 경고).</summary>
        public string GetSteeringDetail()
        {
            if (IsBeached)
            {
                string islandName = nearestIsland != null ? $"{nearestIsland.islandId}번 섬" : "섬";
                return $"{islandName} 해안에 얹혔다 - 뗏목에서 내려 뭍으로 갈 수 있다 · [S] 후진";
            }

            if (StabilityWarning)
                return $"[경고] 기울기 위험 - 짐을 줄이거나 잔잔한 곳으로 · 적재 {LoadRatio * 100f:F0}%";

            if (LoadRatio > 1f)
                return $"[과적] 적재 {LoadRatio * 100f:F0}% - 뗏목이 잠기고 느려진다 · {DescribeWind()}";

            string anchorHint = raft != null && raft.HasPart(RaftPart.Anchor)
                ? "[F] 닻 내리기"
                : "닻 없음 - 내리면 표류한다";

            return $"{DescribePropulsion()} · {DescribeWind()} · 적재 {LoadRatio * 100f:F0}% · {anchorHint}";
        }

        /// <summary>지금 추진 수단과 그 성능을 한 조각으로 적는다.</summary>
        public string DescribePropulsion()
        {
            if (raft == null)
                return "추진 없음";

            switch (ActivePropulsion)
            {
                case RaftPart.Motor:
                    return $"모터 {MotorSpeed:F0} m/s · 연료 {FuelSeconds:F0}초";
                case RaftPart.Sail:
                    return $"돛 {SailMaxSpeed * SailFactor(HullForward, WindDirection):F1} m/s";
                case RaftPart.Oar:
                    return $"노 {OarSpeed:F1} m/s";
                default:
                    break;
            }

            // 아직 아무것도 못 밀고 있는 상태 - 왜인지 알려 준다.
            if (raft.HasPart(RaftPart.Motor) && FuelSeconds <= 0f)
                return $"모터 연료 없음 - {FuelItemName} 필요";

            return raft.HasPropulsion ? "대기" : "추진 수단 없음";
        }

        /// <summary>바람 방향(8방위) · 세기 · 뱃머리 기준 상대 방향.</summary>
        public string DescribeWind()
        {
            float relative = SailFactor(HullForward, WindDirection);

            string bearing;
            if (relative >= 0.8f)
                bearing = "순풍";
            else if (relative >= 0.35f)
                bearing = "옆바람";
            else if (relative > 0.01f)
                bearing = "빗겨부는 바람";
            else
                bearing = "맞바람";

            return $"바람 {CompassName(WindAngleDegrees)} {WindSpeed:F1} m/s({bearing})";
        }

        /// <summary>각도(0 = +Z)를 8방위 한국어 이름으로. 순수 표시용이라 여기 두는 것이 맞다.</summary>
        public static string CompassName(float angleDegrees)
        {
            float a = Mathf.Repeat(angleDegrees, 360f);
            int sector = Mathf.RoundToInt(a / 45f) % 8;
            switch (sector)
            {
                case 0: return "북";
                case 1: return "북동";
                case 2: return "동";
                case 3: return "남동";
                case 4: return "남";
                case 5: return "남서";
                case 6: return "서";
                default: return "북서";
            }
        }
    }
}
