using UnityEngine;
using MakeGame.Data;
using MakeGame.Player;

namespace MakeGame.Systems
{
    /// <summary>
    /// HazardSource의 곰 AI partial 분할 파일. [B35] 성체 곰 추격 AI와 [B37] 새끼 곰 AI에 속하는
    /// 필드·상수·메서드를 HazardSource.cs에서 **내용 수정 없이 그대로** 옮겨 왔다(순수 이동 리팩토링).
    /// 위험요소 공통 로직(전투·재등장·접촉 판정·activeHazards 목록 관리)은 HazardSource.cs에 남아 있다.
    /// </summary>
    public partial class HazardSource : MonoBehaviour
    {
        // ═════════════════════════════════════════════════════════════════════════════
        //  [B35] 곰 추격 AI - 배회 → 경계 → 추격 → 공격 → 복귀
        //
        //  ── 왜 여기(HazardSource)인가 ─────────────────────────────────────────────
        //  CreatureMotion은 **루트를 절대 건드리지 않는다**는 것이 그 파일 설계의 전부다(자식의
        //  localPosition만 쓴다). 즉 루트 이동은 구조적으로 그쪽 일이 아니다. 루트(=트리거 콜라이더가
        //  붙은 오브젝트)를 소유한 것은 이 컴포넌트이므로 이동/회전도 여기서 한다. 두 파일의 채널이
        //  겹치지 않는다: 여기는 **루트의 position/rotation**, 저기는 **자식의 localPosition**,
        //  숨쉬기는 **자식의 localScale**. 세 채널이 서로 덮어쓰지 않는다.
        //
        //  ── NavMesh를 쓰지 않는 이유 ───────────────────────────────────────────────
        //  이 프로젝트의 씬 NavMesh Surface는 과거에 파괴된 이력이 있고 재구축이 보장되지 않는다.
        //  대신 매 프레임 목표 방향으로 직접 옮기고 지면은 TerrainSampler로 스냅한다. 장애물 회피는
        //  없고(대신 아래 지형 프로브가 절벽/물을 막는다) 지형 위를 걷는 것까지가 이 AI의 약속이다.
        //
        //  ── 회전은 y축 요(yaw)만 ──────────────────────────────────────────────────
        //  곰 루트의 localScale은 (0.86, 1.80, 2.56)으로 비균등이라, x/z로 기울이면 자식 월드 행렬이
        //  S·R·S 꼴이 되어 전단(shear)으로 찌그러진다. 그래서 회전을 Quaternion.RotateTowards 같은
        //  자유 회전으로 다루지 않고 **float 하나(bearYaw)로만** 들고 다니며 Quaternion.Euler(0, yaw, 0)로
        //  통째로 덮어쓴다 - 어떤 경로로도 x/z 성분이 생길 수 없다.
        //  (루트 회전 자체는 안전하다. 전단은 '비균등 스케일 **아래**에서 회전한 자식'에서만 생긴다.)
        // ═════════════════════════════════════════════════════════════════════════════

        /// <summary>곰의 행동 상태. 배회(Idle/Wander) → 경계 → 추격 → 공격 → 복귀.</summary>
        private enum BearState
        {
            Idle,    // 제자리에 서서 다음 배회 지점을 기다린다
            Wander,  // 처음 자리 주변을 어슬렁거린다
            Alert,   // 발견 직후. 포효하며 플레이어 쪽으로 몸을 돌린다(아직 안 움직인다)
            Chase,   // 달려간다
            Attack,  // 사거리 안. 멈춰서 앞발을 내려친다
            Return   // 놓쳤거나 너무 멀리 왔다. 처음 자리로 돌아간다
        }

        // ── 하드코딩 상수(인스펙터에 노출할 이유가 없는 내부 값) ──────────────────────

        /// <summary>플레이어 이동 속도 대비 추격 속도 배율. 1.06 = "아주 조금 빠르다"(플레이어 5.0 → 곰 5.3).</summary>
        private const float BearChaseSpeedRatio = 1.06f;

        /// <summary>배회 반경(m). 처음 자리에서 이 안으로만 돌아다닌다.</summary>
        private const float BearWanderRadius = 13f;

        /// <summary>경계(포효) 상태로 버티는 시간(초). 이 사이에 플레이어는 도망칠 여유를 얻는다.</summary>
        private const float BearAlertSeconds = 0.9f;

        /// <summary>추격 중 돌진 연출을 다시 재생하기까지의 간격(초).</summary>
        private const float BearChargeIntervalSeconds = 4.5f;

        /// <summary>공격 상태에서 앞발을 내려치는 간격(초).</summary>
        private const float BearSlamIntervalSeconds = 1.6f;

        /// <summary>이탈 반경 밖에 이만큼(초) 계속 있으면 추격을 포기한다. 잠깐 스치는 것으로는 안 놓친다.</summary>
        private const float BearLoseSeconds = 2.2f;

        /// <summary>복귀 중 다시 달려들 수 있는 앵커 거리 비율. 이탈 경계에서 다시 덜덜 떠는 것을 막는다.</summary>
        private const float BearReaggroLeashFraction = 0.7f;

        /// <summary>진행 방향 앞을 얼마나 내다볼지(m). 곰 몸 길이의 절반쯤이라 벼랑에 발을 딛기 전에 멈춘다.</summary>
        private const float BearProbeDistance = 1.3f;

        /// <summary>프로브 거리 안에서 오를 수 있는 최대 높이차(m). 1.3m에 0.9m ≈ 35도.</summary>
        private const float BearMaxClimbMeters = 0.9f;

        /// <summary>프로브 거리 안에서 내려갈 수 있는 최대 낙차(m). 오르는 것보다는 관대하다.</summary>
        private const float BearMaxDropMeters = 1.6f;

        /// <summary>해수면에서 이만큼(m) 위까지가 "물가"다. 지면 높이가 이보다 낮으면 발을 딛지 않는다 - 곰은 바다로 걸어 들어가지 않는다.</summary>
        private const float BearShoreMarginY = 0.55f;

        /// <summary>지면을 따라 오르내릴 때의 수직 속도 상한(m/s). 프로브가 순간적으로 튀어도 곰이 순간이동하지 않는다.</summary>
        private const float BearMaxVerticalSpeed = 12f;

        /// <summary>목표 지점에 "도착했다"고 볼 거리(m).</summary>
        private const float BearArriveDistance = 1.8f;

        /// <summary>공격 중 플레이어에게 이만큼(m)까지는 계속 밀고 들어간다. 곰 몸 앞뒤 반폭보다 짧아야 트리거가 실제로 닿는다.</summary>
        private const float BearPressDistance = 1.4f;

        /// <summary>진행 방향과 몸 방향이 이만큼 어긋나면 속도를 깎는다(내적). 무거운 짐승은 옆으로 미끄러지지 않는다.</summary>
        private const float BearAlignFullSpeedDot = 0.75f;

        /// <summary>몸이 완전히 반대를 볼 때 남는 속도 비율. 제자리에서 도는 동안 앞으로 새지 않는다.</summary>
        private const float BearMisalignedSpeedFactor = 0.15f;

        /// <summary>막혔을 때 시도할 우회 각도(도). 0(정면)부터 좌우로 넓혀 간다.</summary>
        private static readonly float[] BearSteerAngles = { 0f, 32f, -32f, 64f, -64f, 105f, -105f };

        // ── 인스턴스 상태 ─────────────────────────────────────────────────────────────
        private bool bearAiReady;             // 곰에서만 true. 다른 위험 요소는 AI 코드를 한 줄도 밟지 않는다
        private BearState bearState = BearState.Idle;
        private Vector3 bearHome;             // 처음 서 있던 자리(리쉬 앵커 · 복귀 지점)
        private Vector3 bearWanderTarget;
        private float bearYaw;                // 유일한 회전 성분. x/z는 존재하지 않는다
        private float bearSpeed;              // 현재 속력(m/s). 가속/감속으로만 변한다
        private float bearStateTimer;
        private float bearLostTimer;
        private float bearSlamTimer;
        private float bearChargeTimer;
        private float bearGroundY;            // 마지막으로 성공한 지면 높이. 프로브 기준 높이로도 쓴다
        private bool bearGroundValid;
        // [B44] **필드 초기자에서 CreatureVisualBuilder를 부르면 안 된다.** 필드 초기자는
        // MonoBehaviour 생성자에서 도는데, BearGroundOffset은 Resources.Load로 모델 유무를 살핀다.
        // Unity는 생성자에서의 Load를 금지하고 UnityException을 찍은 뒤 null을 돌려주므로,
        // 프로브가 실패로 확정되어 이 세션 내내 모델 곰이 안 나온다(실제로 Hazard_GiantCrab에서 발생).
        // 실제 값은 InitBearAI가 접지 실측으로 채운다. 여기서는 상수만 둔다.
        private float bearHoverOffset = 0.61f; // 지면에서 루트 중심까지의 높이(InitBearAI가 덮어쓴다)
        private float bearSeaLevel;
        private System.Random bearRng;        // 배회 지점 추첨용. UnityEngine.Random 금지(재현성 규칙)
        private bool bearRngRebuiltWarned;    // 지연 재생성 경고를 개체당 딱 한 번만 남기기 위한 표시

        // ─────────────────────────────────────────────────────────────────────────────
        //  ★ bearAiReady == true 인데 bearRng == null 이던 매 프레임 NRE의 원인과 그 수정 ★
        //
        //  ── 코드로 증명되는 사실 ─────────────────────────────────────────────────────
        //  · bearAiReady를 true로 만드는 곳은 InitBearAI의 **마지막 줄** 하나뿐이고, 바로 그 직전 줄이
        //    BearRng.NextDouble()을 부른다(아래 InitBearAI 참고). 즉 그 대입이 실행된 순간에는
        //    bearRng이 반드시 non-null이었다 - null이었다면 한 줄 앞에서 예외가 나 bearAiReady는
        //    false로 남는다. 그리고 이 파일 어디에도 bearRng에 null을 넣는 코드가 없다(유일한 대입은
        //    InitBearAI의 new System.Random 한 줄).
        //  · HazardSource를 만드는 곳은 HazardSpawner.SpawnSingleHazard 한 곳뿐이고, 거기서
        //    hazardType = Bear / isBearCub을 **Start보다 먼저** 세운다(HazardSpawner.cs:490-496).
        //    새끼도 hazardType은 Bear라 Start의 조기 반환에 걸리지 않고 InitBearAI를 반드시 탄다.
        //    씬(SampleScene.unity)에는 HazardSource가 단 하나도 직렬화돼 있지 않다.
        //  → 따라서 "한 도메인 안에서" 이 상태에 도달하는 C# 경로는 존재하지 않는다.
        //
        //  ── 그래서 실제로 무엇이 일어났나 ────────────────────────────────────────────
        //  **Play 중 스크립트 재컴파일(도메인 리로드)** 이다. 이 프로젝트의 표준 작업 절차가
        //  "외부에서 .cs 수정 → Unity에서 Assets > Refresh"인데(CLAUDE.md:7,
        //  MULTI_AGENT_GUIDE.md:103/107 - Play 중 Refresh를 걸지 말라는 경고가 이미 적혀 있다),
        //  Play 중에 이걸 하면 Unity가 MonoBehaviour 상태를 백업/복원한다. 이때 살아남는 것은
        //  **Unity가 직렬화할 수 있는 타입의 필드뿐**이다:
        //    · bool bearAiReady → 살아남는다(true 그대로)
        //    · float/Vector3/enum/Transform[]/CreatureMotion(bearHome · bearGroundY · bearSeaLevel ·
        //      bearHoverOffset · bearState · breathParts · bearMotion …) → 전부 살아남는다
        //    · **System.Random bearRng → Unity가 직렬화할 수 없다 → null로 되돌아온다**
        //  이 클래스에서 Unity가 직렬화하지 못하는 인스턴스 필드는 bearRng **하나뿐**이라, 리로드
        //  직후의 곰은 정확히 "bearAiReady = true, bearRng = null" 상태가 되고 다음 배회 추첨에서
        //  매 프레임 NRE를 뱉는다(성체도 EnterBearState의 Idle/Wander에서 똑같이 터진다 -
        //  새끼가 먼저 눈에 띈 것은 새끼가 배회/Idle에 훨씬 자주 들어가기 때문이다).
        //
        //  ── 원인 수정 ────────────────────────────────────────────────────────────────
        //  "직렬화되는 플래그(bearAiReady)가 직렬화되지 않는 상태(bearRng)의 존재를 보증한다"는
        //  구조 자체를 없앤다. bearRng을 **(islandIndex, spawnOrder)에서 언제든 다시 만들 수 있는
        //  캐시**로 강등하고, 모든 추첨을 아래 BearRng 프로퍼티 하나로만 통과시킨다. 시드 식은
        //  BearRngSeed 한 곳에만 적어 InitBearAI와 지연 생성이 절대 어긋날 수 없게 한다
        //  (시드가 달라지면 배회 패턴 재현성이 깨진다).
        // ─────────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// 배회 난수의 시드. InitBearAI와 지연 재생성이 **같은 식**을 쓰도록 한 곳에만 적는다.
        /// 값 자체는 예전 그대로다(islandIndex * 7919 + spawnOrder * 104729 + 31).
        /// </summary>
        private int BearRngSeed
        {
            get { return islandIndex * 7919 + spawnOrder * 104729 + 31; }
        }

        /// <summary>
        /// 배회 추첨용 난수. 어떤 이유로든 비어 있으면 **같은 시드로 그 자리에서** 다시 만든다.
        /// UnityEngine.Random은 쓰지 않는다(재현성 규칙). 경고는 개체당 한 번만 남긴다 -
        /// 매 프레임 찍으면 콘솔 도배가 예외에서 경고로 바뀔 뿐이다.
        /// </summary>
        private System.Random BearRng
        {
            get
            {
                if (bearRng == null)
                {
                    bearRng = new System.Random(BearRngSeed);

                    if (!bearRngRebuiltWarned)
                    {
                        bearRngRebuiltWarned = true;
                        Debug.LogWarning(
                            "[HazardSource] 곰 배회 난수(bearRng)가 비어 있어 같은 시드로 다시 만들었다 " +
                            "(island " + islandIndex + " / spawn " + spawnOrder +
                            (isBearCub ? " / 새끼" : " / 성체") +
                            "). Play 중 스크립트 재컴파일(도메인 리로드)로 System.Random이 복원되지 " +
                            "않은 경우다 - 배회 패턴만 시드 처음으로 되감긴다.", this);
                    }
                }

                return bearRng;
            }
        }

        // 플레이어는 씬에 하나뿐이라 곰마다 따로 찾을 이유가 없다. 파괴된 참조는 Unity의 null 비교로 걸러진다.
        private static SurvivalStats cachedPlayerStats;
        private static float cachedPlayerStatsTime = -999f;
        private const float PlayerCacheSeconds = 2f;

        // Physics.autoSyncTransforms가 false다. 곰이 루트를 옮긴 뒤 지형 레이를 쏘기 전에 한 번은 동기화해야
        // 한다. 전역 호출이라 곰이 몇 마리든 프레임당 한 번이면 충분하다.
        private static int bearPhysicsSyncFrame = -1;

        /// <summary>
        /// 곰 AI를 초기화한다. Start의 곰 분기에서만 불린다.
        /// 여기서 정하는 것: 리쉬 앵커(처음 자리) · 지면에서 띄울 높이 · 해수면 · 추격 속도 · 배회 난수.
        /// </summary>
        private void InitBearAI()
        {
            bearHome = transform.position;
            bearWanderTarget = bearHome;
            bearYaw = transform.eulerAngles.y;   // 스포너가 준 yaw 지터를 그대로 이어받는다
            bearSpeed = 0f;
            bearState = BearState.Idle;

            // 개체마다 다른(그러나 재실행해도 같은) 배회 패턴. UnityEngine.Random을 쓰지 않는다.
            // 시드 식은 BearRngSeed 한 곳에만 적는다 - 지연 재생성(BearRng)과 어긋나면 재현성이 깨진다.
            bearRng = new System.Random(BearRngSeed);

            // 해수면. 못 찾으면 0(WorldMapManager.seaLevel 기본값 · PlayerController.waterLevel과 같은 값).
            WorldMapManager world = FindAnyObjectByType<WorldMapManager>();
            bearSeaLevel = world != null ? world.seaLevel : 0f;

            // 추격 속도는 **플레이어에게서 읽어** 정한다. 숫자를 여기 다시 적으면 플레이어 속도를 고친
            // 순간 곰이 조용히 못 따라오거나 순식간에 덮치는 존재가 된다(이 프로젝트가 반복해서 낸 사고
            // 유형이라, 곰 몸 크기를 CreatureVisualBuilder 상수로 참조하는 것과 같은 방식을 쓴다).
            // PlayerController는 **읽기만** 한다.
            PlayerController controller = FindAnyObjectByType<PlayerController>();
            if (controller != null && controller.moveSpeed > 0.1f)
                bearChaseSpeed = Mathf.Clamp(controller.moveSpeed * BearChaseSpeedRatio, 1.5f, 8f);

            // 지면에서 루트 중심까지의 높이를 실측해 둔다. 스포너가 넣은 groundOffset과 같아야 하지만,
            // 실측해 두면 개체 크기 지터나 스포너 변경과 무관하게 항상 접지가 유지된다.
            float groundY = SampleGroundY(bearHome.x, bearHome.z, bearHome.y, out bool hit, 40f, 60f);
            bearGroundValid = hit;
            if (hit)
            {
                bearGroundY = groundY;
                bearHoverOffset = Mathf.Clamp(bearHome.y - groundY, 0.1f, 4f);
            }
            else
            {
                // 지형을 못 찾았다(아직 생성 전이거나 재생성 중). 스포너가 쓴 규격값을 그대로 쓰고,
                // 지면을 다시 찾기 전까지는 아래 UpdateBearAI가 y를 한 번도 건드리지 않는다.
                // [B37] 새끼는 피벗 높이가 다르다 - 성체 값을 쓰면 새끼가 지면에 반쯤 묻힌다.
                bearHoverOffset = isBearCub
                    ? CreatureVisualBuilder.BearCubGroundOffset
                    : CreatureVisualBuilder.BearGroundOffset;
                bearGroundY = bearHome.y - bearHoverOffset;
            }

            bearStateTimer = 1.5f + (float)BearRng.NextDouble() * 3f;
            bearAiReady = true;
        }

        /// <summary>
        /// 곰을 처음 자리로 되돌리고 상태를 초기화한다. 처치 후 재등장/세이브 복원에서 부른다.
        /// </summary>
        /// <param name="teleportHome">처음 자리로 순간이동시킬지 여부(재등장 경로에서만 true).</param>
        private void ResetBearAI(bool teleportHome)
        {
            if (!bearAiReady)
                return;

            bearState = BearState.Idle;
            bearSpeed = 0f;
            bearStateTimer = 2f;
            bearLostTimer = 0f;
            bearSlamTimer = 0f;
            bearChargeTimer = 0f;
            bearWanderTarget = bearHome;

            if (!teleportHome)
                return;

            transform.position = bearHome;
            transform.rotation = Quaternion.Euler(0f, bearYaw, 0f);

            float groundY = SampleGroundY(bearHome.x, bearHome.z, bearHome.y, out bool hit, 40f, 60f);
            bearGroundValid = hit;
            if (hit)
            {
                bearGroundY = groundY;
                transform.position = new Vector3(bearHome.x, groundY + bearHoverOffset, bearHome.z);
            }
        }

        /// <summary>
        /// 한 프레임의 상태 갱신 + 이동. Update에서 timeScale/처치 여부를 이미 걸러 준 뒤에만 불린다.
        /// </summary>
        private void UpdateBearAI(float dt)
        {
            if (dt <= 0f)
                return;

            // 방금 옮긴 트랜스폼에 레이캐스트하기 전 동기화(Physics.autoSyncTransforms가 false다).
            // 전역 상태라 프레임당 한 번이면 곰이 몇 마리든 충분하다.
            if (bearPhysicsSyncFrame != Time.frameCount)
            {
                Physics.SyncTransforms();
                bearPhysicsSyncFrame = Time.frameCount;
            }

            Transform player = ResolvePlayerTransform();
            Vector3 self = transform.position;

            float distance = float.MaxValue;
            bool inDetectCone = false;
            if (player != null)
            {
                Vector3 flat = player.position - self;
                flat.y = 0f;
                distance = flat.magnitude;
                inDetectCone = IsPlayerInDetectCone(flat, distance);
            }

            bool inLoseRange = distance <= bearLoseRadius;

            Vector3 fromHome = self - bearHome;
            fromHome.y = 0f;
            float homeDistance = fromHome.magnitude;

            bearStateTimer -= dt;
            bearSlamTimer = Mathf.Max(0f, bearSlamTimer - dt);
            bearChargeTimer = Mathf.Max(0f, bearChargeTimer - dt);

            switch (bearState)
            {
                case BearState.Idle:
                    if (inDetectCone)
                    {
                        EnterBearState(BearState.Alert);
                        break;
                    }
                    DriveBear(self, 0f, bearWanderTurnSpeed, dt);
                    if (bearStateTimer <= 0f)
                    {
                        PickBearWanderTarget();
                        EnterBearState(BearState.Wander);
                    }
                    break;

                case BearState.Wander:
                {
                    if (inDetectCone)
                    {
                        EnterBearState(BearState.Alert);
                        break;
                    }

                    Vector3 toTarget = bearWanderTarget - self;
                    toTarget.y = 0f;
                    bool arrived = toTarget.magnitude <= BearArriveDistance;
                    bool moved = DriveBear(bearWanderTarget, bearWanderSpeed, bearWanderTurnSpeed, dt);

                    // 도착했거나 시간이 다 됐거나 사방이 막혔으면(물가/절벽에 코를 박았다) 쉬었다 다시 고른다.
                    if (arrived || bearStateTimer <= 0f || !moved)
                        EnterBearState(BearState.Idle);
                    break;
                }

                case BearState.Alert:
                    // 몸만 돌린다. 발은 아직 떼지 않는다 - 포효가 곧 경고이고, 이 0.9초가 도망칠 여유다.
                    DriveBear(player != null ? player.position : self, 0f, bearChaseTurnSpeed, dt);

                    if (!inLoseRange)
                    {
                        EnterBearState(BearState.Return);
                        break;
                    }
                    if (bearStateTimer <= 0f)
                        EnterBearState(BearState.Chase);
                    break;

                case BearState.Chase:
                    if (player == null)
                    {
                        EnterBearState(BearState.Return);
                        break;
                    }

                    if (distance <= bearAttackRange)
                    {
                        EnterBearState(BearState.Attack);
                        break;
                    }

                    // 이탈 판정에 히스테리시스를 준다: 발견 반경(18)보다 넓은 이탈 반경(27) **밖에**
                    // BearLoseSeconds 동안 계속 있어야 놓친다. 경계선을 오가는 것만으로는 안 풀린다.
                    bearLostTimer = inLoseRange ? 0f : bearLostTimer + dt;
                    if (bearLostTimer >= BearLoseSeconds || homeDistance > bearLeashRadius)
                    {
                        EnterBearState(BearState.Return);
                        break;
                    }

                    // 달리는 동안 돌진 연출을 되풀이한다. 이미 시퀀스 중이면 CreatureMotion이 무시하지만,
                    // 여기서도 물어보고 넘어가야 쿨다운이 헛돌지 않는다.
                    if (bearChargeTimer <= 0f && bearMotion != null && !bearMotion.IsPlayingSequence)
                    {
                        bearMotion.PlayCharge();
                        bearChargeTimer = BearChargeIntervalSeconds;
                    }

                    DriveBear(player.position, bearChaseSpeed, bearChaseTurnSpeed, dt);
                    break;

                case BearState.Attack:
                    if (player == null || !inLoseRange)
                    {
                        EnterBearState(BearState.Return);
                        break;
                    }

                    // 때리면서도 몸은 계속 붙인다. 공격 사거리(2.8m)에서 그냥 서 버리면 곰의 트리거
                    // 콜라이더(몸 길이 2.56m → 앞뒤 반폭 약 1.15~1.47m + 플레이어 반지름)가 플레이어에
                    // 닿지 않아서, 곰이 코앞에서 앞발만 휘두르고 실제로는 한 대도 못 때린다.
                    // 그래서 BearPressDistance(1.4m)까지는 느리게 밀고 들어가고, 그 안에서만 선다.
                    // (피해 자체는 예전과 똑같이 트리거 접촉 경로 하나만 담당한다 - 여기서 새로 주지 않는다.)
                    float pressSpeed = distance > BearPressDistance ? bearWanderSpeed * 1.4f : 0f;
                    DriveBear(player.position, pressSpeed, bearChaseTurnSpeed, dt);

                    // 사거리를 조금 넉넉히(1.15배) 보고 판정한다 - 딱 경계에서 공격/추격이 떨지 않게.
                    if (distance > bearAttackRange * 1.15f)
                    {
                        EnterBearState(BearState.Chase);
                        break;
                    }

                    if (bearSlamTimer <= 0f && bearMotion != null)
                    {
                        bearMotion.PlaySlam();
                        bearSlamTimer = BearSlamIntervalSeconds;
                    }
                    break;

                case BearState.Return:
                {
                    // 돌아가는 길에도 다시 달려들 수 있다. 다만 리쉬 경계에서 추격/복귀가 덜덜 떨지 않도록
                    // 앵커에서 충분히 안쪽(70%)에 있을 때만 다시 붙는다.
                    if (inDetectCone && homeDistance <= bearLeashRadius * BearReaggroLeashFraction)
                    {
                        EnterBearState(BearState.Alert);
                        break;
                    }

                    DriveBear(bearHome, bearWanderSpeed * 1.5f, bearWanderTurnSpeed, dt);
                    if (homeDistance <= BearArriveDistance)
                        EnterBearState(BearState.Idle);
                    break;
                }
            }
        }

        /// <summary>
        /// 상태를 바꾸고 그 상태의 진입 동작(연출 · 타이머)을 한 번만 실행한다.
        /// </summary>
        private void EnterBearState(BearState next)
        {
            bearState = next;
            bearLostTimer = 0f;

            switch (next)
            {
                case BearState.Idle:
                    // 다음 배회까지 2~6초 쉰다.
                    bearStateTimer = 2f + (float)BearRng.NextDouble() * 4f;
                    break;

                case BearState.Wander:
                    // 한 번 정한 목표를 최대 14초까지만 쫓는다(도중에 막히면 Idle로 빠진다).
                    bearStateTimer = 8f + (float)BearRng.NextDouble() * 6f;
                    break;

                case BearState.Alert:
                    // 발견하는 **순간** 포효한다. 상태 기계가 생기기 전에는 이 순간이 없었다.
                    bearMotion?.PlayRoar();
                    bearStateTimer = BearAlertSeconds;
                    break;

                case BearState.Chase:
                    // 추격을 시작하는 **순간** 돌진한다. 예전에는 이 호출이 "첫 접촉"에 임시로 걸려 있었다
                    // (TryApplyContactDamage 주석 참고) - 그 자리에서 여기로 옮긴 것이 이 배치의 핵심이다.
                    bearMotion?.PlayCharge();
                    bearChargeTimer = BearChargeIntervalSeconds;
                    bearStateTimer = 0f;
                    break;

                case BearState.Attack:
                    bearMotion?.PlaySlam();
                    bearSlamTimer = BearSlamIntervalSeconds;
                    bearStateTimer = 0f;
                    break;

                case BearState.Return:
                    bearStateTimer = 0f;
                    break;
            }
        }

        /// <summary>
        /// 플레이어가 지금 "보이는지". 거리 + 시야각이고, 아주 가까우면 각도를 무시한다(기척).
        /// 이미 붙은 상태(Alert/Chase/Attack)에서 놓치는 판정은 여기가 아니라 이탈 반경(bearLoseRadius)이
        /// 담당한다 - 두 반경이 다른 것이 히스테리시스의 전부다.
        /// </summary>
        private bool IsPlayerInDetectCone(Vector3 flatToPlayer, float distance)
        {
            if (distance > bearDetectRadius)
                return false;

            if (distance <= bearCloseSenseRadius)
                return true;   // 코앞이면 뒤에 있어도 안다

            if (distance <= 0.0001f)
                return true;

            Vector3 direction = flatToPlayer / distance;
            return Vector3.Angle(BearForward(), direction) <= bearViewHalfAngle;
        }

        /// <summary>bearYaw가 가리키는 수평 방향. transform.forward를 쓰지 않는 이유는 회전을 float 하나로만 들고 다니기 때문이다.</summary>
        private Vector3 BearForward()
        {
            float rad = bearYaw * Mathf.Deg2Rad;
            return new Vector3(Mathf.Sin(rad), 0f, Mathf.Cos(rad));
        }

        /// <summary>
        /// 이번 프레임의 회전 + 이동을 실제로 수행한다.
        /// </summary>
        /// <param name="targetPoint">바라보고 다가갈 지점(월드).</param>
        /// <param name="targetSpeed">목표 속력(m/s). 0이면 제자리에서 방향만 튼다.</param>
        /// <returns>실제로 앞으로 나아갔는지(false면 사방이 막혔거나 지면을 못 찾았다는 뜻).</returns>
        private bool DriveBear(Vector3 targetPoint, float targetSpeed, float turnSpeed, float dt)
        {
            Vector3 self = transform.position;
            Vector3 toTarget = targetPoint - self;
            toTarget.y = 0f;

            Vector3 desired = toTarget.sqrMagnitude > 0.0001f ? toTarget.normalized : BearForward();

            // 갈 수 있는 방향을 고른다. 정면이 물/절벽/지형 밖이면 좌우로 넓혀 가며 우회로를 찾는다.
            bool blocked = false;
            if (targetSpeed > 0.01f)
            {
                if (TryPickBearStep(desired, out Vector3 steered))
                {
                    desired = steered;
                }
                else
                {
                    // 사방이 막혔다. 방향은 목표 쪽으로 계속 틀되 발은 떼지 않는다.
                    blocked = true;
                    targetSpeed = 0f;
                }
            }

            // ── 회전: y축 요만. float 하나로 들고 다니다 통째로 덮어쓰므로 x/z가 생길 수 없다. ──
            float desiredYaw = Mathf.Atan2(desired.x, desired.z) * Mathf.Rad2Deg;
            bearYaw = Mathf.MoveTowardsAngle(bearYaw, desiredYaw, turnSpeed * dt);
            transform.rotation = Quaternion.Euler(0f, bearYaw, 0f);

            Vector3 forward = BearForward();

            // ── 속도: 즉시 최고속이 아니라 가속/감속을 거친다. 곰의 "무게"는 여기서 나온다. ──
            // 몸이 진행 방향을 안 볼 때는 목표 속도를 깎는다 - 옆으로 미끄러지는 짐승은 가벼워 보인다.
            float align = Vector3.Dot(forward, desired);
            float alignFactor = Mathf.Lerp(BearMisalignedSpeedFactor, 1f,
                Mathf.InverseLerp(-1f, BearAlignFullSpeedDot, align));
            float cappedTarget = targetSpeed * alignFactor;

            float rate = cappedTarget > bearSpeed ? bearAcceleration : bearDeceleration;
            bearSpeed = Mathf.MoveTowards(bearSpeed, cappedTarget, rate * dt);

            // ── 이동 + 접지 ────────────────────────────────────────────────────────
            float step = bearSpeed * dt;
            Vector3 nextXZ = step > 0.0001f ? self + forward * step : self;

            // 목표 지점을 지나치지 않는다(제자리에서 앞뒤로 튀는 것을 막는다).
            if (targetSpeed > 0.01f && step > 0.0001f && toTarget.magnitude < step)
                nextXZ = new Vector3(targetPoint.x, self.y, targetPoint.z);

            // ── [B52] 장애물 통과 방지: 바위 convex 헐·절벽 박스·야자수 줄기·건축물 등 기본 레이어의
            // 정적 콜라이더를 곰이 그냥 뚫고 걷던 문제. 이동분을 몸통 반경 스피어로 캐스트해서
            // 막히면 표면 접선으로 미끄러지고, 그마저 막히면 이동을 취소한다(물가 방어와 같은 처리 -
            // false를 돌려주면 배회 중에는 기존 경로 그대로 Idle로 빠져 목적지가 무효화된다).
            // 검사는 CreatureMotion의 공용 정적 유틸이 한다 - **순수 질의**라 그 파일의 "루트를
            // 건드리지 않는다" 계약은 그대로고, 이동 코드가 생기는 다른 생물도 같은 검사를 쓸 수 있다.
            // 캐스트는 실제로 움직일 때만 프레임당 1~2회이고, 여기서는 bearRng를 한 번도 뽑지 않는다
            // (추첨 횟수 불변 - 도메인 리로드 재구축 계약(BearRng 주석)이 깨지지 않는다).
            Vector3 horizontalMove = nextXZ - self;
            horizontalMove.y = 0f;
            if (horizontalMove.sqrMagnitude > 0.00000001f)
            {
                // 몸통 반경: 루트 localScale.x가 곧 몸 폭(m)이다(BearBodyScale.x 0.86 × sizeJitter,
                // 새끼는 그 0.58배) - 성체 약 0.43m / 새끼 약 0.25m. 캐스트 중심은 루트 중심
                // (지면 위 bearHoverOffset)이라 몸통 높이의 장애물만 보고, 발밑 지형은 걸리지 않는다.
                float bodyRadius = Mathf.Clamp(transform.localScale.x * 0.5f, 0.15f, 0.6f);
                bool obstacleBlocked;
                Vector3 allowedMove = CreatureMotion.ResolveObstacleMotion(
                    transform, self, horizontalMove, bodyRadius, out obstacleBlocked);
                if (obstacleBlocked)
                {
                    bearSpeed = 0f;
                    return false;
                }

                nextXZ = self + allowedMove;
            }

            // 지난 프레임에 지면을 못 찾았다면(월드 재생성 등) 마지막 지면 높이 자체를 믿을 수 없으므로
            // 프로브 범위를 크게 벌려 다시 잡는다. 좁은 범위(±8/12)로만 계속 찾으면 지형이 다른 높이로
            // 다시 생성됐을 때 곰이 영원히 얼어붙는다.
            float probeAbove = bearGroundValid ? 8f : 60f;
            float probeBelow = bearGroundValid ? 12f : 90f;
            float groundY = SampleGroundY(nextXZ.x, nextXZ.z, bearGroundY, out bool hit, probeAbove, probeBelow);

            // ★ SnapToGround 실패 방어 ★
            // TerrainSampler.SnapToGround는 "Island_" 콜라이더를 못 맞히면 **넘긴 좌표를 그대로**
            // 돌려주고 호출자는 실패를 알 수 없다. 그래서 절대 나올 수 없는 센티넬 y를 넣어 보내고
            // (SampleGroundY 참고) 그 값이 그대로 오면 실패로 판정한다. 실패했을 때는:
            //   · **y를 한 번도 건드리지 않는다** → 허공으로 떨어지지도, 지형에 파묻히지도 않는다.
            //   · **수평 이동도 취소한다** → 지형이 확인되지 않은 칸으로는 한 발도 딛지 않는다.
            // (프로젝트에 이미 있는 RaftStructure.SampleTerrainHeight의 센티넬 판정과 같은 방식이다.)
            if (!hit)
            {
                bearGroundValid = false;
                bearSpeed = 0f;
                return false;
            }

            // 물가 방어: 지면 자체가 해수면 근처면 그쪽으로는 발을 딛지 않는다. 곰은 바다로 걸어
            // 들어가지 않는다(상어와 달리 곰은 물 밖 생물이고, 물에 들어가면 접지 계산도 무의미해진다).
            //
            // [B53 감사(監査) - "곰이 물에 있다" 침수 조사에서 이 파일을 전수 확인한 결과]
            // 이동 경로의 물 가드는 이미 3중이다(이번 조사에서 새로 뚫을 구멍이 없음을 확인):
            //  (1) 이동 **결과**: 바로 이 판정. 위 장애물 캐스트(B52)가 이동분을 접선으로 미끄러뜨린
            //      **뒤의 최종 nextXZ**에 대해 TerrainSampler 레이 1회(SampleGroundY)로 지면 높이를
            //      재므로, 우회·미끄러짐·목표 스냅 어느 경로로도 수면 아래 지면에는 발을 딛지 못한다.
            //  (2) 조향 프로브: IsBearStepAllowed가 후보 방향의 착지점을 같은 기준으로 거른다.
            //  (3) 이동 **목적지**: PickBearWanderTarget이 물가(y < 해수면+0.55) 후보를 버린다.
            // 즉 물속 곰의 실제 원인은 이동이 아니라 **스폰 위치**였다(마스크형 섬의 만 위에 스폰) -
            // HazardSpawner의 B53 물 스폰 가드가 그 뿌리를 막는다. 이 세 판정 모두 bearRng를 한 번도
            // 뽑지 않으므로(순수 레이 질의) 배회 추첨 횟수·순서는 예전과 완전히 동일하다.
            if (groundY < bearSeaLevel + BearShoreMarginY)
            {
                bearSpeed = 0f;
                return false;
            }

            bearGroundValid = true;
            bearGroundY = groundY;

            // 수직은 상한을 두고 따라간다. 지면이 순간적으로 튀어도 곰이 순간이동하지 않는다.
            float targetY = groundY + bearHoverOffset;
            float newY = Mathf.MoveTowards(self.y, targetY, BearMaxVerticalSpeed * dt);
            transform.position = new Vector3(nextXZ.x, newY, nextXZ.z);

            // step 크기로 판정하지 않는다: 가속 첫 프레임의 이동량은 프레임률이 높을수록 작아져서
            // (240fps에서는 0.05mm) "못 갔다"로 잘못 읽히고, 그러면 곰이 배회를 시작하자마자 포기한다.
            // "갈 곳이 있었는지"만 돌려준다.
            return !blocked;
        }

        /// <summary>
        /// 원하는 방향부터 좌우로 넓혀 가며 실제로 딛을 수 있는 방향을 찾는다. 전부 막히면 false.
        /// </summary>
        private bool TryPickBearStep(Vector3 desired, out Vector3 chosen)
        {
            for (int i = 0; i < BearSteerAngles.Length; i++)
            {
                Vector3 candidate = Quaternion.Euler(0f, BearSteerAngles[i], 0f) * desired;
                if (IsBearStepAllowed(candidate))
                {
                    chosen = candidate;
                    return true;
                }
            }

            chosen = desired;
            return false;
        }

        /// <summary>
        /// 그 방향으로 BearProbeDistance만큼 나아간 지점이 딛을 만한지 본다.
        /// 거부하는 세 가지: 지형이 없다(섬 밖) · 물가다 · 경사가 너무 급하다.
        /// </summary>
        private bool IsBearStepAllowed(Vector3 direction)
        {
            Vector3 probe = transform.position + direction * BearProbeDistance;
            float y = SampleGroundY(probe.x, probe.z, bearGroundY, out bool hit);

            if (!hit)
                return false;                                   // 섬 밖 - 허공으로 나가지 않는다
            if (y < bearSeaLevel + BearShoreMarginY)
                return false;                                   // 물가 - 바다로 들어가지 않는다

            float rise = y - bearGroundY;
            if (rise > BearMaxClimbMeters)
                return false;                                   // 절벽을 기어오르지 않는다
            if (rise < -BearMaxDropMeters)
                return false;                                   // 절벽에서 뛰어내리지 않는다

            return true;
        }

        /// <summary>
        /// 지정 XZ의 섬 지형 높이를 잰다. 지형을 못 맞히면 hit이 false이고 referenceY를 그대로 돌려준다.
        ///
        /// TerrainSampler.SnapToGround는 실패해도 넘긴 좌표를 그대로 돌려줘서 호출자가 실패를 알 수 없다.
        /// 그래서 절대 나올 수 없는 y(referenceY - 100)를 센티넬로 넣어 보내고, 돌아온 y가 그대로면
        /// "지형 없음"으로 판정한다. 레이는 센티넬이 아니라 **referenceY 기준**으로 above 위에서 시작해
        /// below 아래까지만 훑으므로(길이 above+below), 센티넬을 쓰면서도 레이가 길어지지 않는다.
        /// </summary>
        private float SampleGroundY(float x, float z, float referenceY, out bool hit,
            float above = 8f, float below = 12f)
        {
            const float SentinelDrop = 100f;

            float sentinel = referenceY - SentinelDrop;
            Vector3 result = TerrainSampler.SnapToGround(
                new Vector3(x, sentinel, z), SentinelDrop + above, above + below);

            // 실패하면 y가 센티넬 그대로다. 실제 지형이 referenceY - 100까지 내려갈 수는 없다
            // (레이 자체가 referenceY - below까지만 닿고 below는 100보다 훨씬 작다).
            hit = result.y > sentinel + 1f;
            return hit ? result.y : referenceY;
        }

        /// <summary>
        /// 플레이어(SurvivalStats를 가진 오브젝트)를 찾는다. 씬에 하나뿐이라 곰마다 찾지 않고 공유 캐시를 쓴다.
        /// Unity 6.5: FindObjectsByType의 2인자/3인자 오버로드는 CS0618이라 여기서는 아예 쓰지 않는다.
        /// </summary>
        private static Transform ResolvePlayerTransform()
        {
            if (cachedPlayerStats != null && Time.unscaledTime - cachedPlayerStatsTime < PlayerCacheSeconds)
                return cachedPlayerStats.transform;

            cachedPlayerStats = FindAnyObjectByType<SurvivalStats>();
            cachedPlayerStatsTime = Time.unscaledTime;
            return cachedPlayerStats != null ? cachedPlayerStats.transform : null;
        }

        /// <summary>
        /// 배회 목표를 처음 자리 주변에서 하나 고른다. 지형이 없거나 물가인 지점은 버리고 다시 뽑는다.
        /// 여섯 번 안에 못 고르면 처음 자리로 돌아가는 것을 목표로 삼는다(항상 유효하다).
        /// </summary>
        private void PickBearWanderTarget()
        {
            for (int i = 0; i < 6; i++)
            {
                double angle = BearRng.NextDouble() * 6.2831853;
                double radius = BearWanderRadius * (0.35 + 0.65 * BearRng.NextDouble());
                float x = bearHome.x + (float)(System.Math.Cos(angle) * radius);
                float z = bearHome.z + (float)(System.Math.Sin(angle) * radius);

                // 배회 목표는 최대 13m 떨어져 있어 높이차가 클 수 있다 - 프로브 범위를 넉넉히 잡는다.
                float y = SampleGroundY(x, z, bearGroundY, out bool hit, 40f, 60f);
                if (!hit || y < bearSeaLevel + BearShoreMarginY)
                    continue;

                bearWanderTarget = new Vector3(x, y + bearHoverOffset, z);
                return;
            }

            bearWanderTarget = bearHome;
        }

        // ═════════════════════════════════════════════════════════════════════════════
        //  [B37] 새끼 곰 AI - 배회 → (플레이어 접근) 도망 → 복귀 + 어미 호출
        //
        //  ── 성체와 무엇을 공유하고 무엇이 다른가 ──────────────────────────────────────
        //  공유: 이동/접지/지형 프로브(DriveBear · TryPickBearStep · SampleGroundY · PickBearWanderTarget)와
        //        상태 열거형. 즉 물가·절벽 회피, 센티넬 실패 판정, y축 요만 쓰는 회전 규칙이 전부 그대로다.
        //  다름: 플레이어를 **쫓지 않는다**. 감지 시야각도 쓰지 않는다(새끼는 겁이 많아 사방을 다 본다).
        //        포효/돌진/슬램을 한 번도 재생하지 않는다.
        //
        //  ── 상태를 새로 만들지 않았다 ────────────────────────────────────────────────
        //  BearState.Chase를 새끼에서는 **"도망"**으로 읽는다(성체의 추격과 같은 "전속력으로 달리는"
        //  상태라 타이머/전이 구조가 그대로 맞는다). 열거형에 값을 더하지 않았으므로 성체 상태 기계와
        //  세이브/직렬화 어디에도 영향이 없다.
        //
        //  ── 핵심 재미 요소: 어미 각성 ────────────────────────────────────────────────
        //  플레이어가 CubAlarmRadius 안에 들어오거나 새끼를 때리면, 반경 CubMotherAlertRadius 안의
        //  성체 곰이 **시야각과 무관하게** 추격으로 넘어간다. 새 상태를 만들지 않고 성체 AI의 기존
        //  진입점 EnterBearState(BearState.Chase)를 그대로 부른다 - 돌진 연출·타이머·리쉬 판정이
        //  전부 원래 코드대로 돈다.
        // ═════════════════════════════════════════════════════════════════════════════

        /// <summary>플레이어가 이 거리(m) 안에 들어오면 새끼가 달아나기 시작한다.</summary>
        private const float CubFleeRadius = 12f;

        /// <summary>플레이어가 이 거리(m) 안에 들어오면 어미를 부른다(디렉터 지시 "10m 정도").</summary>
        private const float CubAlarmRadius = 10f;

        /// <summary>어미를 불러오는 반경(m). 이 안의 성체 곰은 시야각과 무관하게 즉시 달려온다.</summary>
        private const float CubMotherAlertRadius = 30f;

        /// <summary>도망 속도(m/s). 성체 추격(약 5.3)보다 느려 따라잡을 수는 있지만, 쫓는 동안 어미가 온다.</summary>
        private const float CubFleeSpeed = 4.2f;

        /// <summary>배회 속도 배율(성체 배회 속도 대비). 작고 가벼워 성체보다 총총거린다.</summary>
        private const float CubWanderSpeedRatio = 1.25f;

        /// <summary>도망 중 방향 전환 속도(도/초). 몸이 가벼워 성체 추격 회전(130)보다 빠르다.</summary>
        private const float CubFleeTurnSpeed = 210f;

        /// <summary>위협이 사라진 뒤에도 이만큼(초) 더 달린다. 한 발 벗어나자마자 멈추면 겁이 안 읽힌다.</summary>
        private const float CubFleeHoldSeconds = 3.5f;

        /// <summary>어미 호출 쿨다운(초). 플레이어가 옆에 서 있어도 매 프레임 목록을 훑지 않게 한다.</summary>
        private const float CubAlarmCooldownSeconds = 4f;

        /// <summary>도망 목표를 몇 미터 앞에 두는지. DriveBear는 지점을 향해 걷는 함수라 방향을 지점으로 바꾼다.</summary>
        private const float CubFleeTargetDistance = 8f;

        private float cubAlarmTimer;

        /// <summary>
        /// 새끼 한 마리의 한 프레임. Update가 timeScale/처치 여부를 이미 걸러 준 뒤에만 불린다.
        /// </summary>
        private void UpdateBearCubAI(float dt)
        {
            if (dt <= 0f)
                return;

            // 성체와 같은 이유의 프레임당 1회 물리 동기화(Physics.autoSyncTransforms가 false다).
            if (bearPhysicsSyncFrame != Time.frameCount)
            {
                Physics.SyncTransforms();
                bearPhysicsSyncFrame = Time.frameCount;
            }

            cubAlarmTimer = Mathf.Max(0f, cubAlarmTimer - dt);
            bearStateTimer -= dt;

            Transform player = ResolvePlayerTransform();
            Vector3 self = transform.position;

            float distance = float.MaxValue;
            Vector3 awayDirection = BearForward();
            if (player != null)
            {
                Vector3 flat = player.position - self;
                flat.y = 0f;
                distance = flat.magnitude;
                if (distance > 0.0001f)
                    awayDirection = -flat / distance;   // 플레이어 반대쪽
            }

            Vector3 fromHome = self - bearHome;
            fromHome.y = 0f;
            float homeDistance = fromHome.magnitude;

            bool threatened = distance <= CubFleeRadius;

            // 어미 호출. 시야각을 보지 않는다 - 새끼는 등 뒤로 다가와도 놀란다.
            if (distance <= CubAlarmRadius)
                AlarmNearbyAdults();

            switch (bearState)
            {
                case BearState.Chase:   // 새끼에게 이 상태는 "도망"이다(위 주석 참고)
                {
                    if (threatened)
                        bearStateTimer = CubFleeHoldSeconds;   // 쫓아오는 동안에는 계속 달린다

                    // 너무 멀리 달아나면(리쉬 밖) 도망 방향을 집 쪽으로 반씩 섞는다. 그대로 두면
                    // 플레이어가 계속 미는 것만으로 새끼가 섬 끝 물가에 처박혀 갇힌다.
                    Vector3 fleeDirection = awayDirection;
                    if (homeDistance > bearLeashRadius && homeDistance > 0.0001f)
                    {
                        Vector3 homeward = -fromHome / homeDistance;
                        Vector3 blended = awayDirection + homeward;
                        if (blended.sqrMagnitude > 0.0001f)
                            fleeDirection = blended.normalized;
                    }

                    DriveBear(self + fleeDirection * CubFleeTargetDistance, CubFleeSpeed, CubFleeTurnSpeed, dt);

                    if (!threatened && bearStateTimer <= 0f)
                        EnterBearCubState(homeDistance > BearArriveDistance ? BearState.Return : BearState.Idle);
                    break;
                }

                case BearState.Return:
                    if (threatened)
                    {
                        EnterBearCubState(BearState.Chase);
                        break;
                    }

                    DriveBear(bearHome, bearWanderSpeed * CubWanderSpeedRatio, bearWanderTurnSpeed, dt);
                    if (homeDistance <= BearArriveDistance)
                        EnterBearCubState(BearState.Idle);
                    break;

                case BearState.Wander:
                {
                    if (threatened)
                    {
                        EnterBearCubState(BearState.Chase);
                        break;
                    }

                    Vector3 toTarget = bearWanderTarget - self;
                    toTarget.y = 0f;
                    bool arrived = toTarget.magnitude <= BearArriveDistance;
                    bool moved = DriveBear(bearWanderTarget, bearWanderSpeed * CubWanderSpeedRatio, bearWanderTurnSpeed, dt);

                    if (arrived || bearStateTimer <= 0f || !moved)
                        EnterBearCubState(BearState.Idle);
                    break;
                }

                default:
                    // Idle. Alert/Attack에는 새끼가 절대 들어가지 않으므로 여기로 합류시킨다
                    // (세이브 복원 등으로 이상한 상태가 남아도 다음 프레임에 배회로 되돌아온다).
                    if (threatened)
                    {
                        EnterBearCubState(BearState.Chase);
                        break;
                    }

                    DriveBear(self, 0f, bearWanderTurnSpeed, dt);
                    if (bearStateTimer <= 0f)
                    {
                        PickBearWanderTarget();
                        EnterBearCubState(BearState.Wander);
                    }
                    break;
            }
        }

        /// <summary>
        /// 새끼의 상태 전환. 성체의 EnterBearState와 **일부러 분리했다** - 그쪽은 진입할 때마다
        /// 포효/돌진/슬램을 재생하는데 새끼는 그 셋 중 어느 것도 하지 않기 때문이다.
        /// 성체 코드는 이 메서드 때문에 한 줄도 바뀌지 않는다.
        /// </summary>
        private void EnterBearCubState(BearState next)
        {
            if (!bearAiReady)
                return;   // Start 전(세이브 복원 등) - bearHome/접지 기준값이 아직 없다

            bearState = next;
            bearLostTimer = 0f;

            switch (next)
            {
                case BearState.Idle:
                    bearStateTimer = 1.5f + (float)BearRng.NextDouble() * 3f;
                    break;
                case BearState.Wander:
                    bearStateTimer = 6f + (float)BearRng.NextDouble() * 5f;
                    break;
                case BearState.Chase:
                    bearStateTimer = CubFleeHoldSeconds;
                    break;
                default:
                    bearStateTimer = 0f;
                    break;
            }
        }

        /// <summary>
        /// 반경 CubMotherAlertRadius 안의 **성체** 곰을 전부 추격 상태로 밀어 넣는다(어미가 새끼를 지킨다).
        /// 목록은 OnEnable/OnDisable이 관리하는 static 목록이라 평상시에는 FindObjectsByType을 부르지 않는다.
        ///
        /// [bearRng NRE와 같은 뿌리의 두 번째 구멍] activeHazards는 **static**이라 Play 중 재컴파일
        /// (도메인 리로드)에서 통째로 비워지는데, 이미 활성인 오브젝트의 OnEnable은 다시 불리지 않는다.
        /// 그러면 목록이 영원히 빈 채로 남아 "새끼를 건드리면 어미가 온다"는 이 기능이 예외 하나 없이
        /// 조용히 죽는다(bearRng과 달리 크래시가 아니라 무반응이라 더 늦게 발견된다). 그래서 목록이
        /// 건강한지 딱 하나로 확인한다 - **자기 자신이 목록에 들어 있는가**. 정상이라면 자기 OnEnable이
        /// 넣어 뒀으므로 반드시 들어 있고, 없다면 목록을 믿을 수 없다는 뜻이라 그 자리에서 다시 만든다.
        /// </summary>
        private void AlarmNearbyAdults()
        {
            if (cubAlarmTimer > 0f)
                return;

            cubAlarmTimer = CubAlarmCooldownSeconds;

            if (!activeHazards.Contains(this))
                RebuildActiveHazards();

            Vector3 self = transform.position;
            float sqrRadius = CubMotherAlertRadius * CubMotherAlertRadius;

            for (int i = activeHazards.Count - 1; i >= 0; i--)
            {
                HazardSource other = activeHazards[i];
                if (other == null)
                {
                    activeHazards.RemoveAt(i);   // 씬 전환 등으로 파괴된 항목 청소
                    continue;
                }

                if (other == this || other.isBearCub || other.hazardType != HazardType.Bear)
                    continue;
                if (!other.IsActive || !other.bearAiReady)
                    continue;

                Vector3 delta = other.transform.position - self;
                delta.y = 0f;
                if (delta.sqrMagnitude > sqrRadius)
                    continue;

                other.WakeAsProtectiveMother();
            }
        }

        /// <summary>
        /// 새끼의 비명을 듣고 달려나가는 성체 쪽 처리. **성체 AI의 기존 진입점을 그대로 쓴다** -
        /// 시야각/발견 반경 판정만 건너뛸 뿐, 그 뒤의 추격·이탈·리쉬·복귀는 전부 원래 코드가 돈다.
        /// 이미 붙어 있는(경계/추격/공격) 곰은 건드리지 않는다 - 다시 부르면 돌진 연출이 되감긴다.
        /// </summary>
        private void WakeAsProtectiveMother()
        {
            if (bearState == BearState.Alert || bearState == BearState.Chase || bearState == BearState.Attack)
                return;

            EnterBearState(BearState.Chase);
        }
    }
}
