using System.Collections.Generic;
using UnityEngine;
using MakeGame.Data;
using MakeGame.Player;

namespace MakeGame.Systems
{
    /// <summary>
    /// 엔드게임 보스 3종의 식별자. **값을 중간에 끼워 넣지 마라** - 세이브(SaveData의 보스 처치/트로피
    /// 플래그)가 이 순서를 배열 인덱스로 그대로 쓴다. 새 보스는 반드시 맨 뒤에 붙인다.
    /// </summary>
    public enum BossKind
    {
        GiantShark = 0,   // boss_a - 거대 상어(전장 12m). 외해 심해를 지킨다.
        GiantMoray = 1,   // boss_b - 대왕 곰치(전장 7m). 수중 동굴 앞을 지킨다.
        AbyssHorror = 2,  // boss_c - 심해 괴수(폭 9m). 가장 깊은 곳을 지킨다.
    }

    /// <summary>
    /// 보스 개체 하나. 체력·페이즈·수중 AI·트로피 드랍을 담당한다(배치는 BossSpawner).
    ///
    /// ── 왜 HazardSource에 얹는가 (설계의 핵심) ───────────────────────────────────
    /// 플레이어의 공격 경로는 두 개이고 **둘 다 이 작업의 락 밖 파일**이 소유한다:
    ///   · 근접 E키 → InteractionController.HandleInteract가 `GetComponent&lt;HazardSource&gt;()`를 본다.
    ///   · 투척 창 → ThrownWeapon.TryFindImpact가 `GetComponentInParent&lt;HazardSource&gt;()`만 대상으로 친다.
    /// 즉 **HazardSource가 아닌 것은 이 게임에서 때릴 수 없다.** 그래서 보스는 새 체력 시스템을 만드는
    /// 대신 자기 오브젝트에 HazardSource(전투 대상, 큰 체력)를 얹고, 이 컴포넌트는 그 체력을 **읽어서**
    /// 페이즈와 이동을 굴린다. 덤으로 피격 반응(경직/이펙트)·적중 표식(CombatFeedbackUI.TriggerAttackConfirm)·
    /// 접촉 피해 + 출혈(ApplyHazardEffect의 Shark 분기)·물리침 처리가 전부 검증된 기존 경로로 들어온다.
    ///
    /// 얹을 때 기존 위험 요소와 다르게 세우는 값은 네 가지뿐이다:
    ///   (1) spawnOrder = -1 → SaveLoadController의 위험 요소 세이브에서 **구조적으로 제외**된다
    ///       (SaveHazardsAndCreatures가 `spawnOrder &lt; 0`을 건너뛴다). 보스 진행도는 이 클래스의
    ///       static 플래그가 들고 SaveData의 전용 필드로 저장된다 - 두 벌로 갈라지지 않는다.
    ///   (2) respawnSeconds = 매우 큰 값 → 보스는 재등장하지 않는다(처치 즉시 이 컴포넌트가
    ///       오브젝트를 파괴하므로 실제로는 타이머가 돌 기회조차 없다. 방어선일 뿐이다).
    ///   (3) hitKnockbackDistance = 0 → 넉백이 좌표를 밀면 이 클래스의 수학 이동과 서로 덮어쓴다.
    ///       맞았다는 표현은 이 클래스가 자체 경직(피격 프레임에 AI 정지)으로 낸다.
    ///   (4) hazardType = Shark 고정 → ApplyHazardEffect에서 "직접 피해 + 출혈 + 사인 SharkAttack"이
    ///       나온다. 셋 다 수중 보스에 맞는 유일한 조합이다(곰/식인종 계열은 육상 사인이 찍힌다).
    ///       [한계] 그 대가로 조준 프롬프트 이름이 세 보스 모두 "상어"로 나온다
    ///       (InteractionPromptUI.GetHazardDisplayName은 hazardType만 본다 - 그 파일은 락 밖).
    ///
    /// ── AI (물리 없음) ──────────────────────────────────────────────────────────
    /// Rigidbody도 NavMesh도 쓰지 않는다. MarineLifeSpawner/HazardSource.BearAI와 같은 프로젝트 관례로
    /// 순수 수학 이동이다: 순회(제자리 궤도) → 감지 → 추격 → (3페이즈)돌진 → 이탈/후퇴.
    /// **플레이어가 물 밖이면 추격을 포기한다**(수중 전용 보스). 전진은 항상 자기 정면(+Z)이라
    /// 선회 반경이 생기고, 그래서 회피(X)와 뭍으로 도망이 실제 대응 수단이 된다.
    ///
    /// ── 성능 ────────────────────────────────────────────────────────────────────
    /// 프레임당 할당 0(문자열 조립·LINQ·new 없음). 플레이어가 <see cref="CullDistance"/>(300m)보다
    /// 멀면 모델 컨테이너를 통째로 SetActive(false)하고 AI도 건너뛴다(MarineLifeSpawner 200~300m 관례).
    /// 월드 전체에 보스는 3마리뿐이다.
    /// </summary>
    public class BossCreature : MonoBehaviour
    {
        /// <summary>보스 종류 수(BossKind의 값 개수). 세이브 배열 길이와 같다.</summary>
        public const int KindCount = 3;

        // ══════════════════════════════════════════════════════════════════════════
        //  종류별 스펙 표 (밸런스 수치의 단일 소스 - 여기 말고 다른 곳에 적지 마라)
        // ══════════════════════════════════════════════════════════════════════════

        /// <summary>표시 이름(로그·트로피 프롬프트용).</summary>
        private static readonly string[] DisplayNames = { "거대 상어", "대왕 곰치", "심해 괴수" };

        /// <summary>보스 본체 모델(Resources 경로 - 확장자를 붙이면 항상 null이다).</summary>
        private static readonly string[] ModelPaths = { "Models/boss_a", "Models/boss_b", "Models/boss_c" };

        /// <summary>트로피 소품 모델. 접지(밑면 y=0) 소품이라 피벗 보정이 없다.</summary>
        private static readonly string[] TrophyModelPaths =
            { "Models/boss_trophy_a", "Models/boss_trophy_b", "Models/boss_trophy_c" };

        /// <summary>트로피 표시 이름(수거 지점 프롬프트에 그대로 나온다).</summary>
        private static readonly string[] TrophyNames = { "상어 이빨", "곰치 턱뼈", "촉수 표본" };

        /// <summary>
        /// 본체 피벗 보정값. mgbuild가 밑면 y=0을 강제하므로 모델 파츠에 `localPosition = -pivot`을 주어
        /// **몸통(body 그룹)의 중심**을 루트 원점에 올린다. 그래야 루트를 돌릴 때 제자리에서 돈다.
        /// 값은 OBJ 실측(body 그룹 바운딩 박스 중심)이며 tech-artist 제공표와 일치하는 것을 확인했다.
        /// </summary>
        private static readonly Vector3[] BodyPivots =
        {
            new Vector3(0.0004f, 1.4123f, 0.5630f),
            new Vector3(0f, 0.5361f, 0f),
            new Vector3(0.0806f, 2.3193f, -1.3759f),
        };

        /// <summary>
        /// 접촉/조준 판정 상자의 크기(m). **fin(지느러미·촉수)이 아니라 body 그룹 실측 크기**를 쓴다 -
        /// 상어 꼬리 끝이나 괴수의 촉수 끝에 스쳤다고 30 피해가 들어오면 회피할 여지가 사라진다.
        /// 상자 중심은 루트 원점(= body 중심)이라 center = 0이다(위 피벗 보정의 직접적 결과).
        /// 실측 body 크기: a (2.468, 2.825, 10.874) / b (1.989, 0.990, 7.000) / c (2.649, 2.297, 4.665).
        /// </summary>
        private static readonly Vector3[] HitboxSizes =
        {
            new Vector3(2.5f, 2.8f, 8.7f),   // 상어: 꼬리 쪽 20%는 판정에서 뺐다
            new Vector3(2.0f, 1.0f, 5.6f),   // 곰치: 같은 규칙(뱀 같은 몸이라 꼬리가 길다)
            new Vector3(2.7f, 2.3f, 4.7f),   // 괴수: 몸통 전체(촉수 9m는 판정 밖 - 장식이다)
        };

        /// <summary>최대 체력. 위협 8종 최고치(대왕 크랩 60)의 4~7배 - 무기 한 자루로는 끝나지 않는다.</summary>
        private static readonly float[] MaxHealths = { 300f, 220f, 400f };

        /// <summary>
        /// 접촉 1회 피해량. 기존 최고치는 상어 18이고 플레이어 최대 체력은 100이다(SurvivalStats.maxHealth).
        /// 22~30이면 **네 방까지는 버틴다** - 즉사가 아니면서 "회피하지 않으면 죽는다"가 성립하는 구간이다.
        /// 여기에 출혈까지 겹치므로 실효 위협은 더 크다(ApplyHazardEffect의 Shark 분기).
        /// </summary>
        private static readonly float[] ContactDamages = { 26f, 22f, 30f };

        /// <summary>물리쳤을 때 Physical 스킬에 주는 경험치(곰 15의 3~5배).</summary>
        private static readonly float[] DefeatExperiences = { 60f, 50f, 80f };

        /// <summary>1페이즈의 접촉 재피격 간격(초). 페이즈가 오르면 <see cref="PhaseCooldownScale"/>로 짧아진다.</summary>
        private static readonly float[] BaseAttackCooldowns = { 2.0f, 1.6f, 2.4f };

        /// <summary>순회(비전투) 유영 속도(m/s).</summary>
        private static readonly float[] PatrolSpeeds = { 2.4f, 1.6f, 1.2f };

        /// <summary>추격 속도(m/s). 플레이어 수영 3.0(오리발 4.2)보다 빠르다 - 헤엄쳐서는 못 따돌린다.</summary>
        private static readonly float[] ChaseSpeeds = { 5.2f, 4.6f, 3.8f };

        /// <summary>3페이즈 돌진 속도(m/s). 짧게만 유지된다(<see cref="ChargeSeconds"/>).</summary>
        private static readonly float[] ChargeSpeeds = { 8f, 7f, 6.5f };

        /// <summary>플레이어를 처음 알아채는 거리(m).</summary>
        private static readonly float[] DetectRadii = { 45f, 30f, 40f };

        /// <summary>한 번 붙은 뒤 놓치는 거리(m). 반드시 감지 반경보다 커야 한다(히스테리시스).</summary>
        private static readonly float[] LoseRadii = { 70f, 50f, 65f };

        /// <summary>자기 자리에서 이만큼(m) 벌어지면 추격을 포기하고 돌아간다. 보스는 자기 영역을 지킨다.</summary>
        private static readonly float[] LeashRadii = { 130f, 70f, 100f };

        /// <summary>순회 궤도 반지름(m).</summary>
        private static readonly float[] PatrolRadii = { 35f, 18f, 26f };

        /// <summary>선회 속도(도/초). 느릴수록 몸집이 크게 읽히고, 플레이어에게 옆으로 빠질 틈이 생긴다.</summary>
        private static readonly float[] TurnSpeeds = { 70f, 110f, 55f };

        /// <summary>페이즈별 공격 간격 배수(1/2/3페이즈). 체력이 깎일수록 더 자주 문다.</summary>
        private static readonly float[] PhaseCooldownScale = { 1f, 0.7f, 0.55f };

        /// <summary>페이즈별 이동 속도 배수(1/2/3페이즈).</summary>
        private static readonly float[] PhaseSpeedScale = { 1f, 1.1f, 1.15f };

        /// <summary>2페이즈로 넘어가는 체력 비율.</summary>
        private const float Phase2HealthRatio = 0.7f;

        /// <summary>3페이즈(돌진)로 넘어가는 체력 비율.</summary>
        private const float Phase3HealthRatio = 0.3f;

        /// <summary>돌진 1회의 지속 시간(초).</summary>
        private const float ChargeSeconds = 1.2f;

        /// <summary>돌진과 돌진 사이의 최소 간격(초).</summary>
        private const float ChargeCooldownSeconds = 6f;

        /// <summary>돌진을 시작할 수 있는 최소/최대 거리(m). 너무 붙었거나 너무 멀면 시작하지 않는다.</summary>
        private const float ChargeMinRange = 8f;
        private const float ChargeMaxRange = 32f;

        /// <summary>맞은 프레임에 거는 경직 시간(초). 이 동안 AI가 멈춘다(HazardSource의 곰 경직 0.4와 같은 규모).</summary>
        private const float StaggerSeconds = 0.35f;

        /// <summary>플레이어가 이보다 멀면 모델을 끄고 AI도 돌리지 않는다(m).</summary>
        private const float CullDistance = 300f;

        /// <summary>플레이어의 발 높이가 해수면보다 이만큼(m) 위면 "물 밖"으로 본다 - 추격을 포기한다.</summary>
        private const float OutOfWaterMargin = 0.2f;

        /// <summary>몸통 위쪽이 수면 아래로 유지해야 할 여유(m). 보스는 수면 위로 뛰어오르지 않는다.</summary>
        private const float SurfaceClearance = 0.8f;

        /// <summary>해저에서 띄워 둘 여유(m). 지형을 뚫고 들어가지 않게 한다.</summary>
        private const float SeabedClearance = 0.6f;

        /// <summary>해저 샘플이 없는 외해에서 자기 자리 기준으로 내려갈 수 있는 최대 깊이(m).</summary>
        private const float FreeWaterDiveDepth = 14f;

        // ══════════════════════════════════════════════════════════════════════════
        //  진행 상태(static) - 세이브가 읽고 쓰는 단일 소스
        // ══════════════════════════════════════════════════════════════════════════

        private static readonly bool[] defeatedFlags = new bool[KindCount];
        private static readonly bool[] trophyFlags = new bool[KindCount];

        // 공유 메시 캐시(종류당 body/fin 2장). Resources.Load는 필드 초기자에서 부르지 않는다.
        private static readonly Mesh[] bodyMeshes = new Mesh[KindCount];
        private static readonly Mesh[] finMeshes = new Mesh[KindCount];
        private static readonly Mesh[] trophyMeshes = new Mesh[KindCount];
        private static readonly Mesh[] trophyFinMeshes = new Mesh[KindCount];
        private static int probeFrame = -1;

        // 플레이어 공유 캐시(MarineLifeSpawner.EnsurePlayer와 같은 저빈도 재탐색 규칙).
        private static Transform playerTransform;
        private static int playerProbeFrame = -1;

        /// <summary>
        /// 도메인 리로드를 끈 플레이 모드에서 static이 이전 실행의 값을 들고 시작하지 않게 되돌린다
        /// (AGENT_BRIEF 4장 2번의 R1 리셋 훅). 진행 플래그까지 여기서 비우는 것이 중요하다 -
        /// 안 비우면 새 게임을 시작해도 지난 판에서 잡은 보스가 죽어 있는 채로 시작한다.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            for (int i = 0; i < KindCount; i++)
            {
                defeatedFlags[i] = false;
                trophyFlags[i] = false;
                bodyMeshes[i] = null;
                finMeshes[i] = null;
                trophyMeshes[i] = null;
                trophyFinMeshes[i] = null;
            }

            probeFrame = -1;
            playerTransform = null;
            playerProbeFrame = -1;
        }

        /// <summary>이 종류의 보스를 이미 처치했는지(세이브 대상).</summary>
        public static bool IsDefeated(int kind)
        {
            return kind >= 0 && kind < KindCount && defeatedFlags[kind];
        }

        /// <summary>이 종류의 트로피를 이미 수거했는지(세이브 대상).</summary>
        public static bool HasTrophy(int kind)
        {
            return kind >= 0 && kind < KindCount && trophyFlags[kind];
        }

        /// <summary>지금까지 수거한 트로피 개수(0~3). 퀘스트/엔딩이 읽는다.</summary>
        public static int TrophyCount
        {
            get
            {
                int count = 0;
                for (int i = 0; i < KindCount; i++)
                {
                    if (trophyFlags[i])
                        count++;
                }
                return count;
            }
        }

        /// <summary>지금까지 처치한 보스 수(0~3). 퀘스트 체크리스트가 읽는다.</summary>
        public static int DefeatedCount
        {
            get
            {
                int count = 0;
                for (int i = 0; i < KindCount; i++)
                {
                    if (defeatedFlags[i])
                        count++;
                }
                return count;
            }
        }

        /// <summary>트로피 3개를 모두 모았는가(세 번째 엔딩의 유일한 추가 조건).</summary>
        public static bool AllTrophiesCollected => TrophyCount >= KindCount;

        /// <summary>보스 표시 이름(퀘스트 체크리스트가 쓴다). 범위를 벗어나면 빈 문자열.</summary>
        public static string GetDisplayName(int kind)
        {
            return kind >= 0 && kind < KindCount ? DisplayNames[kind] : string.Empty;
        }

        /// <summary>트로피 표시 이름(퀘스트/수거 프롬프트가 쓴다). 범위를 벗어나면 빈 문자열.</summary>
        public static string GetTrophyName(int kind)
        {
            return kind >= 0 && kind < KindCount ? TrophyNames[kind] : string.Empty;
        }

        /// <summary>
        /// 세이브에서 읽은 진행도를 그대로 밀어 넣는다(SaveLoadController 전용).
        /// 목록이 짧거나 null이어도 안전하다 - 옛 세이브에는 이 필드 자체가 없어 빈 목록이 온다
        /// (= 아직 한 마리도 잡지 않았다. 정확히 옛 세이브의 진실이다).
        /// </summary>
        public static void ApplySavedProgress(IList<bool> defeated, IList<bool> trophies)
        {
            for (int i = 0; i < KindCount; i++)
            {
                defeatedFlags[i] = defeated != null && i < defeated.Count && defeated[i];
                trophyFlags[i] = trophies != null && i < trophies.Count && trophies[i];

                // 트로피를 가졌다면 그 보스는 반드시 처치된 상태다(옛 세이브 손상 방어).
                if (trophyFlags[i])
                    defeatedFlags[i] = true;
            }
        }

        // ══════════════════════════════════════════════════════════════════════════
        //  생성
        // ══════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// 모델 메시가 준비됐는지 확인하고, 아직이면 이번 프레임에 한 번만 프로브한다.
        /// (프레임당 1회 가드는 SeabedFloraSpawner.probeFrame과 같은 규칙 - 실패를 영구 캐시하지 않는다.)
        /// </summary>
        public static bool TryEnsureMeshes(int kind)
        {
            if (kind < 0 || kind >= KindCount)
                return false;
            if (bodyMeshes[kind] != null && trophyMeshes[kind] != null)
                return true;
            if (probeFrame == Time.frameCount)
                return false;

            probeFrame = Time.frameCount;

            for (int i = 0; i < KindCount; i++)
            {
                if (bodyMeshes[i] == null
                    && ResourceVisualLibrary.TryLoadTwoPartModel(ModelPaths[i], out Mesh body, out Mesh fin))
                {
                    bodyMeshes[i] = body;
                    finMeshes[i] = fin;   // 병합 임포트면 null - 아래 서브메시 분기가 처리한다
                }

                if (trophyMeshes[i] == null
                    && ResourceVisualLibrary.TryLoadTwoPartModel(TrophyModelPaths[i],
                        out Mesh trophyBody, out Mesh trophyFin))
                {
                    trophyMeshes[i] = trophyBody;
                    trophyFinMeshes[i] = trophyFin;
                }
            }

            return bodyMeshes[kind] != null && trophyMeshes[kind] != null;
        }

        /// <summary>
        /// 보스 한 마리를 실제로 만든다(BossSpawner 전용 진입점). 메시가 아직 로드되지 않았으면 null을
        /// 돌려주고 아무것도 만들지 않는다 - 보이지 않는 전투 대상은 만들지 않는다는 프로젝트 규칙이다.
        /// </summary>
        /// <param name="kind">보스 종류(BossKind의 정수값).</param>
        /// <param name="position">몸통 중심이 놓일 월드 좌표.</param>
        /// <param name="yawDegrees">초기 방향(도). 순회 궤도의 시작 위상이기도 하다.</param>
        /// <param name="parent">부모(월드 재생성에 함께 파괴되도록 WorldMapManager 아래).</param>
        /// <param name="seaLevel">해수면 높이. 수면 돌파 방지와 "물 밖" 판정에 쓴다.</param>
        public static BossCreature Spawn(int kind, Vector3 position, float yawDegrees,
            Transform parent, float seaLevel)
        {
            if (kind < 0 || kind >= KindCount || !TryEnsureMeshes(kind))
                return null;

            // 이름은 "Island_" 비접두라 TerrainSampler.SnapToGround류 지형 판정에서 구조적으로 제외된다.
            var go = new GameObject("Boss_" + (BossKind)kind);
            go.transform.SetParent(parent, false);
            go.transform.position = position;
            go.transform.rotation = Quaternion.Euler(0f, yawDegrees, 0f);

            // 몸통 판정 상자(트리거). 근접 E키 레이·투척 창 SphereCast·접촉 피해가 전부 이 하나를 쓴다.
            var box = go.AddComponent<BoxCollider>();
            box.center = Vector3.zero;
            box.size = HitboxSizes[kind];
            box.isTrigger = true;

            var boss = go.AddComponent<BossCreature>();
            boss.kind = kind;
            boss.home = position;
            boss.seaLevel = seaLevel;
            boss.orbitAngle = yawDegrees * Mathf.Deg2Rad;

            // 시각: 피벗 보정 컨테이너 아래에 모델 파츠 하나. 원거리 컬링은 이 컨테이너를 끈다.
            var modelRoot = new GameObject("BossModel");
            modelRoot.transform.SetParent(go.transform, false);
            modelRoot.transform.localPosition = Vector3.zero;
            boss.modelRoot = modelRoot;

            Material bodyMaterial = ResourceVisualLibrary.GetMaterial(BodyColors[kind], "noise");
            Material finMaterial = ResourceVisualLibrary.GetMaterial(
                ResourceVisualLibrary.Shade(BodyColors[kind], 1.35f), "noise");

            CreatePart(modelRoot.transform, "Body_" + (BossKind)kind, bodyMeshes[kind], finMeshes[kind],
                bodyMaterial, finMaterial, -BodyPivots[kind]);

            // 전투 배선. 이 컴포넌트가 체력을 읽고, 플레이어의 두 공격 경로는 이것을 때린다.
            var hazard = go.AddComponent<HazardSource>();
            hazard.hazardType = HazardType.Shark;   // 수중 피해 + 출혈 + SharkAttack 사인
            hazard.isCombatTarget = true;
            hazard.maxHealth = MaxHealths[kind];
            hazard.currentHealth = MaxHealths[kind];
            hazard.directDamage = ContactDamages[kind];
            hazard.defeatExperience = DefeatExperiences[kind];
            hazard.contactDamageCooldown = BaseAttackCooldowns[kind];
            hazard.hitKnockbackDistance = 0f;       // 넉백이 이 클래스의 수학 이동과 싸우지 않게 한다
            hazard.respawnSeconds = 1e9f;           // 보스는 재등장하지 않는다
            hazard.islandIndex = -1;
            hazard.spawnOrder = -1;                 // 위험 요소 세이브에서 구조적으로 제외
            boss.hazard = hazard;
            boss.lastHealth = MaxHealths[kind];

            return boss;
        }

        /// <summary>몸통 기본색(지느러미/촉수는 이 색의 Shade 1.35).</summary>
        private static readonly Color[] BodyColors =
        {
            new Color(0.30f, 0.34f, 0.40f),   // 상어: 젖은 강철빛 회청색
            new Color(0.26f, 0.30f, 0.18f),   // 곰치: 어두운 올리브
            new Color(0.20f, 0.16f, 0.26f),   // 심해 괴수: 검보라
        };

        /// <summary>
        /// 모델 파츠 하나를 만든다. 임포터가 `o` 2개를 개별 메시로 주면 파츠 2개, 병합해서 주면
        /// 렌더러 하나에 sharedMaterials 2장이다(PlaceCoral/PlaceClam과 같은 분기 - 서브메시 순서는
        /// OBJ `o` 등장 순서 = body → fin이 계약이다). 수중이라 그림자는 캐스팅/수신 모두 끈다.
        /// </summary>
        private static void CreatePart(Transform parent, string name, Mesh body, Mesh fin,
            Material bodyMaterial, Material finMaterial, Vector3 localPosition)
        {
            var part = StructureVisualBuilder.CreateMeshPart(parent, name, body,
                localPosition, Vector3.one, Quaternion.identity, bodyMaterial);

            var renderer = part.GetComponent<MeshRenderer>();
            if (fin != null)
            {
                var finPart = StructureVisualBuilder.CreateMeshPart(parent, name + "_fin", fin,
                    localPosition, Vector3.one, Quaternion.identity, finMaterial);
                var finRenderer = finPart.GetComponent<MeshRenderer>();
                if (finRenderer != null)
                {
                    finRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    finRenderer.receiveShadows = false;
                }
            }
            else if (renderer != null && body != null && body.subMeshCount >= 2)
            {
                renderer.sharedMaterials = new[] { bodyMaterial, finMaterial };
            }

            if (renderer != null)
            {
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }
        }

        // ══════════════════════════════════════════════════════════════════════════
        //  개체 상태
        // ══════════════════════════════════════════════════════════════════════════

        /// <summary>이 보스의 종류(BossKind의 정수값). Spawn이 세운다.</summary>
        private int kind;

        /// <summary>지키는 자리(순회 궤도의 중심이자 추격 이탈 기준점).</summary>
        private Vector3 home;

        private float seaLevel;
        private GameObject modelRoot;
        private HazardSource hazard;

        private float lastHealth;
        private int phase = 1;
        private float staggerTimer;
        private float orbitAngle;
        private float chargeTimer;
        private float chargeCooldownTimer;
        private Vector3 chargeDirection;
        private bool chasing;
        private bool modelVisible = true;
        private bool defeatHandled;

        /// <summary>이 보스가 지키는 자리(BossSpawner가 트로피 재배치 위치로 쓴다).</summary>
        public Vector3 Home => home;

        /// <summary>
        /// 매 프레임 갱신. 순서가 중요하다: 처치 판정 → 컬링 → 페이즈 → 경직 → 상태 전이 → 이동.
        /// **Time.deltaTime을 쓴다** - 엔딩/사망 화면(timeScale 0)에서는 보스도 멈춰야 한다
        /// (곰 AI·던진 창과 같은 규칙. 상시 연출만 unscaled를 쓴다).
        /// </summary>
        private void Update()
        {
            if (hazard == null)
            {
                enabled = false;
                return;
            }

            if (!hazard.IsActive)
            {
                HandleDefeat();
                return;
            }

            EnsurePlayer();

            Vector3 position = transform.position;
            Vector3 viewer = playerTransform != null ? playerTransform.position : position;
            float dx = viewer.x - position.x;
            float dy = viewer.y - position.y;
            float dz = viewer.z - position.z;
            float distanceSq = dx * dx + dy * dy + dz * dz;

            bool nearby = playerTransform != null && distanceSq <= CullDistance * CullDistance;
            if (nearby != modelVisible)
            {
                modelVisible = nearby;
                if (modelRoot != null)
                    modelRoot.SetActive(nearby);
            }

            if (!nearby || Time.timeScale <= 0f)
                return;

            float dt = Time.deltaTime;
            UpdatePhase();

            // 맞은 프레임을 체력 감소로 알아낸다(HazardSource의 피격 진입점은 private이다).
            if (hazard.currentHealth < lastHealth)
            {
                staggerTimer = StaggerSeconds;
                lastHealth = hazard.currentHealth;
            }

            if (staggerTimer > 0f)
            {
                staggerTimer -= dt;
                return;   // 맞은 직후에는 움직이지 않는다 - 그 정지가 "맞았다"의 표현이다
            }

            UpdateAI(dt, viewer, Mathf.Sqrt(distanceSq));
        }

        /// <summary>
        /// 체력 비율로 페이즈를 갱신하고, 바뀐 순간에만 공격 간격을 다시 세운다.
        /// 1페이즈(&gt;70%) 기본 / 2페이즈(≤70%) 공격 빈도·속도 상승 / 3페이즈(≤30%) 돌진 개방.
        /// </summary>
        private void UpdatePhase()
        {
            float ratio = hazard.maxHealth > 0f ? hazard.currentHealth / hazard.maxHealth : 1f;
            int next = ratio > Phase2HealthRatio ? 1 : (ratio > Phase3HealthRatio ? 2 : 3);
            if (next == phase)
                return;

            phase = next;
            hazard.contactDamageCooldown = BaseAttackCooldowns[kind] * PhaseCooldownScale[phase - 1];
            Debug.Log("[BossCreature] " + DisplayNames[kind] + " " + phase + "페이즈 진입 (체력 "
                + Mathf.RoundToInt(hazard.currentHealth) + "/" + Mathf.RoundToInt(hazard.maxHealth) + ")");
        }

        /// <summary>
        /// 상태 전이 + 이동 한 걸음. 상태는 셋뿐이다:
        ///  · 순회 - 자기 자리 주위를 도는 궤도. 플레이어를 감지하면 추격으로 넘어간다.
        ///  · 추격 - 플레이어를 향해 전진(3페이즈에서는 주기적으로 돌진). 접촉 피해는 HazardSource가 낸다.
        ///  · 후퇴 - 놓쳤거나(거리·물 밖) 자기 영역을 너무 벗어났으면 자리로 돌아간다.
        /// 감지/이탈 반경이 다른 것은 경계선에서 상태가 떠는 것을 막기 위한 히스테리시스다(곰 AI와 같다).
        /// </summary>
        private void UpdateAI(float dt, Vector3 playerPosition, float playerDistance)
        {
            bool playerInWater = playerPosition.y < seaLevel - OutOfWaterMargin;
            float homeDistance = HorizontalDistance(transform.position, home);

            if (chasing)
            {
                // 물 밖으로 나갔거나, 너무 멀어졌거나, 내 영역을 벗어났으면 포기한다.
                if (!playerInWater || playerDistance > LoseRadii[kind] || homeDistance > LeashRadii[kind])
                {
                    chasing = false;
                    chargeTimer = 0f;
                }
            }
            else if (playerInWater && playerDistance <= DetectRadii[kind] && homeDistance <= LeashRadii[kind])
            {
                chasing = true;
                chargeCooldownTimer = ChargeCooldownSeconds * 0.5f;   // 붙자마자 돌진하지는 않는다
            }

            float speed;
            Vector3 target;

            if (chasing)
            {
                UpdateCharge(dt, playerPosition, playerDistance);
                if (chargeTimer > 0f)
                {
                    // 돌진 중에는 조준을 갱신하지 않는다 - 그래서 옆으로 피할 수 있다(회피 X의 존재 이유).
                    target = transform.position + chargeDirection * 10f;
                    speed = ChargeSpeeds[kind];
                }
                else
                {
                    target = playerPosition + Vector3.up * 0.8f;   // 발이 아니라 몸통을 노린다
                    speed = ChaseSpeeds[kind] * PhaseSpeedScale[phase - 1];
                }
            }
            else if (homeDistance > PatrolRadii[kind] * 1.5f)
            {
                target = home;                       // 후퇴: 자리로 돌아간다
                speed = ChaseSpeeds[kind] * 0.6f;
            }
            else
            {
                orbitAngle += PatrolSpeeds[kind] / Mathf.Max(1f, PatrolRadii[kind]) * dt;
                if (orbitAngle > Mathf.PI * 2f)
                    orbitAngle -= Mathf.PI * 2f;

                target = new Vector3(
                    home.x + Mathf.Cos(orbitAngle) * PatrolRadii[kind],
                    home.y + Mathf.Sin(orbitAngle * 2f) * 1.5f,
                    home.z + Mathf.Sin(orbitAngle) * PatrolRadii[kind]);
                speed = PatrolSpeeds[kind];
            }

            Steer(dt, target, speed);
        }

        /// <summary>
        /// 3페이즈 전용 돌진. 쿨다운이 지났고 사거리대에 있으면 그 순간의 방향을 **고정**해 짧게 가속한다.
        /// 방향을 고정하는 것이 핵심이다 - 유도 돌진이면 회피(X)로 피할 방법이 없다.
        /// </summary>
        private void UpdateCharge(float dt, Vector3 playerPosition, float playerDistance)
        {
            if (chargeTimer > 0f)
            {
                chargeTimer -= dt;
                if (chargeTimer <= 0f)
                    chargeCooldownTimer = ChargeCooldownSeconds;
                return;
            }

            if (chargeCooldownTimer > 0f)
            {
                chargeCooldownTimer -= dt;
                return;
            }

            if (phase < 3 || playerDistance < ChargeMinRange || playerDistance > ChargeMaxRange)
                return;

            Vector3 direction = playerPosition + Vector3.up * 0.8f - transform.position;
            if (direction.sqrMagnitude < 0.0001f)
                return;

            chargeDirection = direction.normalized;
            chargeTimer = ChargeSeconds;
        }

        /// <summary>
        /// 목표 쪽으로 선회하며 **정면으로만** 전진한다(게걸음 금지 - 몸집이 큰 짐승의 이동이다).
        /// 이동 뒤에 깊이를 제한한다: 수면을 뚫지 않고, 해저(스커트 샘플이 있으면 그 높이)도 뚫지 않는다.
        /// </summary>
        private void Steer(float dt, Vector3 target, float speed)
        {
            Vector3 toTarget = target - transform.position;
            if (toTarget.sqrMagnitude > 0.0001f)
            {
                Quaternion want = SafeLook(toTarget.normalized);
                transform.rotation = Quaternion.RotateTowards(transform.rotation, want, TurnSpeeds[kind] * dt);
            }

            Vector3 position = transform.position + transform.forward * (speed * dt);

            float halfHeight = HitboxSizes[kind].y * 0.5f;
            float ceiling = seaLevel - halfHeight - SurfaceClearance;
            if (position.y > ceiling)
                position.y = ceiling;

            float floor = home.y - FreeWaterDiveDepth;
            if (SeabedGenerator.TrySampleSeabed(position, out float seabedY))
                floor = Mathf.Max(floor, seabedY + halfHeight + SeabedClearance);
            if (position.y < floor)
                position.y = floor;

            transform.position = position;
        }

        /// <summary>
        /// 처치된 뒤 한 번만 도는 마무리. 트로피 수거 지점을 그 자리에 놓고 보스 오브젝트는 지운다
        /// (HazardSource는 처치돼도 오브젝트가 남아 재등장을 기다리는데, 보스는 재등장하지 않는다).
        /// </summary>
        private void HandleDefeat()
        {
            if (defeatHandled)
                return;

            defeatHandled = true;
            defeatedFlags[kind] = true;

            Vector3 dropPosition = transform.position;
            EffectBuilder.PlayHitBurst(dropPosition);
            Debug.Log("[BossCreature] " + DisplayNames[kind] + " 처치 - 전리품 '" + TrophyNames[kind] + "'을(를) 남겼다.");

            if (!trophyFlags[kind])
                SpawnTrophy(kind, dropPosition, transform.parent, seaLevel);

            Destroy(gameObject);
        }

        /// <summary>
        /// 트로피 수거 지점을 만든다. 처치 직후(이 클래스)와 불러오기 직후(BossSpawner - 이미 잡았지만
        /// 아직 안 주운 트로피 복원)에서 같은 함수를 쓴다.
        ///
        /// 수거는 **AirlinerSalvagePoint 그대로**다(범용 1회 수거 지점 - InteractionController가
        /// GetComponentInParent로 잡아 TryCollect를 부르고, InteractionPromptUI가 HasLoot으로 문구를
        /// 만든다). 여기서 새 상호작용을 만들지 않는 이유는 그 두 파일이 이 작업의 락 밖이라
        /// 새 분기를 넣을 수 없기 때문이기도 하다.
        ///
        /// [트로피 아이템 에셋] 트로피 3종의 ItemData 에셋은 아직 없다(ScriptableObjects 전수 확인).
        /// 그래서 지급표는 **레지스트리에 실제로 있을 때만** 채운다 - 없는 이름을 넣으면 수거할 때마다
        /// 경고가 찍히기 때문이다. 에셋이 생기는 순간 코드 수정 없이 가방에도 들어온다.
        /// 진행도(트로피 획득)는 아이템과 **무관하게** 수거 상호작용 자체로 기록되므로, 에셋이 없어도
        /// 엔딩·퀘스트는 지금 그대로 동작한다.
        /// </summary>
        public static GameObject SpawnTrophy(int kind, Vector3 position, Transform parent, float seaLevel)
        {
            if (kind < 0 || kind >= KindCount || !TryEnsureMeshes(kind))
                return null;

            var go = new GameObject("BossTrophy_" + (BossKind)kind);
            go.transform.SetParent(parent, false);

            // 수면 위에 떠 있는 전리품은 없다. 죽은 자리가 수면 근처면 조금 내려 잠긴 상태로 둔다.
            float y = Mathf.Min(position.y, seaLevel - 1.5f);
            go.transform.position = new Vector3(position.x, y, position.z);

            Material bodyMaterial = ResourceVisualLibrary.GetMaterial(TrophyColors[kind], "noise");
            Material detailMaterial = ResourceVisualLibrary.GetMaterial(
                ResourceVisualLibrary.Shade(TrophyColors[kind], 1.3f), "noise");

            // 트로피는 접지 소품이라 피벗 보정이 없다(밑면 y=0을 그대로 쓴다).
            CreatePart(go.transform, "Trophy_" + (BossKind)kind, trophyMeshes[kind], trophyFinMeshes[kind],
                bodyMaterial, detailMaterial, Vector3.zero);

            // 조준용 콜라이더. 트리거로 두어 헤엄치는 플레이어를 막지도, 던진 창이 꽂히지도 않게 한다.
            var box = go.AddComponent<BoxCollider>();
            box.center = new Vector3(0f, 0.45f, 0f);
            box.size = new Vector3(1.1f, 0.9f, 1.1f);
            box.isTrigger = true;

            var salvage = go.AddComponent<AirlinerSalvagePoint>();
            salvage.displayName = TrophyNames[kind];
            salvage.loot = BuildTrophyLoot(kind);

            var pickup = go.AddComponent<BossTrophyPickup>();
            pickup.Setup(kind, salvage);
            return go;
        }

        /// <summary>트로피 소품의 기본색(뼈·이빨은 상아색, 촉수 표본은 자줏빛).</summary>
        private static readonly Color[] TrophyColors =
        {
            new Color(0.90f, 0.88f, 0.80f),
            new Color(0.84f, 0.80f, 0.70f),
            new Color(0.45f, 0.35f, 0.55f),
        };

        /// <summary>
        /// 지급표를 만든다. 같은 이름의 ItemData가 레지스트리에 **실제로 있을 때만** 한 줄을 넣는다
        /// (없으면 빈 표 - AirlinerSalvagePoint.BuildLootList의 "못 찾음" 경고를 애초에 만들지 않는다).
        /// </summary>
        private static AirlinerSalvagePoint.LootEntry[] BuildTrophyLoot(int kind)
        {
            var registry = ItemDataRegistry.LoadFromResources();
            if (registry == null || registry.allItems == null)
                return new AirlinerSalvagePoint.LootEntry[0];

            for (int i = 0; i < registry.allItems.Count; i++)
            {
                var item = registry.allItems[i];
                if (item != null && item.itemName == TrophyNames[kind])
                    return new[] { new AirlinerSalvagePoint.LootEntry(TrophyNames[kind], 1) };
            }

            return new AirlinerSalvagePoint.LootEntry[0];
        }

        // ══════════════════════════════════════════════════════════════════════════
        //  유틸
        // ══════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// 플레이어 Transform 공유 캐시. 못 찾았을 때만, 그것도 60프레임에 한 번, 프레임당 최대 1회
        /// 재탐색한다(MarineLifeSpawner.EnsurePlayer와 같은 규칙 - 정상 경로에서 탐색 비용 0).
        /// </summary>
        private static void EnsurePlayer()
        {
            if (playerTransform != null)
                return;
            if (playerProbeFrame == Time.frameCount || Time.frameCount % 60 != 0)
                return;

            playerProbeFrame = Time.frameCount;
            var stats = Object.FindAnyObjectByType<SurvivalStats>();
            if (stats != null)
                playerTransform = stats.transform;
        }

        /// <summary>수평 거리(y 무시). 깊이 차이 때문에 영역 판정이 흔들리지 않게 한다.</summary>
        private static float HorizontalDistance(Vector3 a, Vector3 b)
        {
            float dx = a.x - b.x;
            float dz = a.z - b.z;
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        /// <summary>
        /// 진행 방향을 바라보는 회전. 정확히 수직인 방향을 그대로 넣으면 Quaternion.LookRotation이
        /// 에러를 뱉고 회전을 포기하므로 그 구간만 기준 축을 바꾼다(ThrownWeapon.SafeLook과 같은 처리).
        /// </summary>
        private static Quaternion SafeLook(Vector3 direction)
        {
            Vector3 up = Mathf.Abs(direction.y) > 0.999f ? Vector3.forward : Vector3.up;
            return Quaternion.LookRotation(direction, up);
        }

        /// <summary>
        /// 트로피 수거 지점에 함께 붙는 감시자. AirlinerSalvagePoint는 "수거됐다"를 알려 주는 콜백이
        /// 없고 그 파일은 이 작업의 락 밖이라, 여기서 <see cref="AirlinerSalvagePoint.HasLoot"/>가
        /// false로 넘어가는 순간을 저빈도 폴링(0.25초)으로 잡아 진행도에 기록한다.
        ///
        /// 가방이 꽉 차서 일부만 들어간 경우에는 HasLoot이 계속 true라 기록되지 않는다 - 정확히
        /// 원하는 동작이다("주웠다"는 실제로 다 주웠을 때만 참이다).
        /// </summary>
        private sealed class BossTrophyPickup : MonoBehaviour
        {
            private const float PollInterval = 0.25f;

            private int kind;
            private AirlinerSalvagePoint salvage;
            private float timer;

            /// <summary>생성 직후 BossCreature.SpawnTrophy가 부른다(인스펙터 배선 없음).</summary>
            public void Setup(int trophyKind, AirlinerSalvagePoint point)
            {
                kind = trophyKind;
                salvage = point;
            }

            private void Update()
            {
                if (salvage == null)
                    return;

                timer -= Time.unscaledDeltaTime;
                if (timer > 0f)
                    return;
                timer = PollInterval;

                if (salvage.HasLoot)
                    return;

                trophyFlags[kind] = true;
                Debug.Log("[BossCreature] 전리품 '" + TrophyNames[kind] + "' 수거 (트로피 "
                    + TrophyCount + "/" + KindCount + ")");
                Destroy(gameObject);
            }
        }
    }
}
