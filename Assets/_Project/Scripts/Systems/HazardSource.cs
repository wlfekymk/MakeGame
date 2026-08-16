using System.Collections.Generic;
using UnityEngine;
using MakeGame.Data;
using MakeGame.Player;
using MakeGame.UI;

namespace MakeGame.Systems
{
    /// <summary>
    /// 섬에 배치되는 위험 요소(독사, 전갈, 곰, 벌떼, 함정, 식인종 등) 하나를 나타낸다.
    /// 플레이어와 접촉하면 위험 요소 종류에 맞는 상태 이상/피해를 SurvivalStats에 적용한다.
    /// 음식 부족/탈수는 개별 오브젝트가 아니라 SurvivalStats의 허기/갈증 감소 로직으로 이미 처리되므로 여기서는 다루지 않는다.
    /// </summary>
    public class HazardSource : MonoBehaviour
    {
        [Tooltip("이 위험 요소의 종류")]
        public HazardType hazardType;

        [Tooltip("접촉 시 즉시 입히는 피해량 (곰, 식인종처럼 직접 공격하는 유형에 사용)")]
        public float directDamage = 10f;

        [Header("전투(맹수/식인종만 해당)")]
        [Tooltip("이 위험 요소가 전투로 물리칠 수 있는 대상인지 여부. true면 체력을 깎아 물리칠 수 있다\n(독사/전갈/벌떼/함정처럼 스치기만 하는 위험 요소는 false로 둔다).")]
        public bool isCombatTarget = false;

        [Tooltip("전투 대상일 때의 최대 체력")]
        public float maxHealth = 30f;

        [Tooltip("현재 체력 (전투 대상일 때만 의미 있음)")]
        public float currentHealth = 30f;

        [Tooltip("물리쳤을 때 지급할 전투 경험치 (Physical 스킬)")]
        public float defeatExperience = 15f;

        [Tooltip("물리친 뒤 다시 나타나기까지 걸리는 시간(초)")]
        public float respawnSeconds = 120f;

        [Tooltip("접촉 상태를 유지할 때 재피격 사이의 최소 간격(초). 붙어 있다고 매 프레임 피해를 입지 않게 한다.")]
        public float contactDamageCooldown = 1.5f;

        // B3-3: ResourceNode와 동일한 목적의 안정적 식별자(생성 섬 번호 + 섬 안에서의 생성 순번).
        // HazardSpawner.SpawnHazardsForIsland가 섬별 결정적 System.Random을 쓰게 되어, 같은 worldSeed면
        // 항상 같은 (islandIndex, spawnOrder)에 같은 위험 요소가 나온다는 전제가 성립한다. 섬에 속하지
        // 않는 스폰(SharkSpawner가 배치하는 상어)은 islandIndex를 -1로 둬 "섬에 속하지 않음"을 표시한다.
        [Tooltip("이 위험 요소를 배치한 섬 번호(IslandInstance.islandId). 섬에 속하지 않으면(예: 상어) -1.")]
        public int islandIndex = -1;

        [Tooltip("이 섬(또는 스폰 그룹) 안에서 몇 번째로 생성됐는지(생성 순번, 0부터).")]
        public int spawnOrder = -1;

        // [B37] 새끼 곰 표시. hazardType은 그대로 Bear다 - 열거형에 값을 추가하면 씬 편집이 필요하고
        // (hazardEntries가 씬에 int로 직렬화돼 있다) 새 엔트리는 spawnOrder를 밀어 기존 세이브를 깨뜨린다.
        // 그래서 "같은 자리에 있던 곰 한 마리의 성격"으로만 갈라 두고, 스포너가 AddComponent 직후
        // ConfigureForType보다 **먼저** 세운다. 필드 추가일 뿐이라 세이브(JsonUtility)에도 영향이 없다.
        [Tooltip("이 곰이 새끼인지 여부(곰에만 의미가 있다). 새끼는 스스로 사냥하지 않고 도망치며," +
            " 주변 성체 곰을 어미로 불러온다.")]
        public bool isBearCub = false;

        // ─────────────────────────────────────────────────────────────────────────────
        //  [B35] 곰 추격 AI 튜닝 값. 곰(HazardType.Bear)에만 쓰이고 다른 위험 요소는 읽지도 않는다.
        //  (다른 종류는 Start에서 AI를 초기화하지 않으므로 비용이 정확히 0이다.)
        // ─────────────────────────────────────────────────────────────────────────────

        [Header("곰 추격 AI (곰 전용)")]
        [Tooltip("플레이어를 처음 발견하는 반경(m). 이 안 + 시야각 안이어야 경계로 넘어간다.")]
        public float bearDetectRadius = 18f;

        [Tooltip("한 번 붙은 뒤 놓치는 반경(m). 반드시 발견 반경보다 커야 한다(히스테리시스 - 경계선에서 상태가 떠는 것을 막는다).")]
        public float bearLoseRadius = 27f;

        [Tooltip("정면 기준 시야 반각(도). 이보다 옆/뒤에 있으면 발견 반경 안이어도 못 본다.")]
        public float bearViewHalfAngle = 65f;

        [Tooltip("시야각을 무시하고 기척만으로 알아채는 근접 반경(m). 뒤로 몰래 붙어도 이 거리에서는 들킨다.")]
        public float bearCloseSenseRadius = 6f;

        [Tooltip("앞발을 내려칠 수 있는 거리(m). 이 안에 들어오면 공격 상태로 넘어간다.")]
        public float bearAttackRange = 2.8f;

        [Tooltip("처음 서 있던 자리에서 이 거리(m) 이상 벌어지면 추격을 포기하고 복귀한다.")]
        public float bearLeashRadius = 42f;

        [Tooltip("추격 최고 속도(m/s). Start에서 PlayerController.moveSpeed를 읽어 그보다 아주 조금 빠르게 덮어쓴다.")]
        public float bearChaseSpeed = 5.3f;

        [Tooltip("배회/복귀할 때의 속도(m/s). 무거운 짐승의 어슬렁거림이라 걷기보다 느리다.")]
        public float bearWanderSpeed = 1.2f;

        [Tooltip("가속도(m/s²). 곰은 즉시 최고속에 닿지 않는다 - 이 값이 '무겁다'의 대부분이다.")]
        public float bearAcceleration = 3f;

        [Tooltip("감속도(m/s²). 가속보다 빠르지만 즉시 멈추지는 않는다.")]
        public float bearDeceleration = 6.5f;

        [Tooltip("추격/공격 중 방향을 트는 속도(도/초).")]
        public float bearChaseTurnSpeed = 130f;

        [Tooltip("배회/복귀 중 방향을 트는 속도(도/초).")]
        public float bearWanderTurnSpeed = 65f;

        private bool isDefeated = false;
        private float respawnTimer = 0f;
        private float contactCooldownTimer = 0f;

        /// <summary>
        /// [B29] 벌떼 전용 연출. 벌떼의 실루엣은 개체가 아니라 "무리"이고, 무리를 무리로 보이게 하는
        /// 마지막 요소가 움직임이다(CreatureVisualBuilder.AddBeeSwarmDetails 주석 참고).
        /// 몸통 메시(벌 18마리가 흩어진 구름)를 천천히 돌리기만 한다:
        ///  - **회전만** 준다. 위치를 흔들면 트리거 콜라이더가 함께 움직여 접촉 판정이 바뀐다.
        ///    벌떼의 콜라이더는 구체(SphereCollider)라 회전에 완전히 불변이므로 판정이 1도 안 바뀐다.
        ///  - 축을 살짝 기울여(Y가 아니라 (0.15, 1, 0.08)) 위에서 봐도 정면에서 봐도 움직임이 읽히게 한다.
        ///  - **unscaledDeltaTime**을 쓴다. 엔딩/사망 화면은 Time.timeScale = 0을 걸므로 deltaTime을
        ///    쓰면 그 화면에서 연출이 첫 프레임에 얼어붙는다(AGENT_BRIEF 4장의 실제 사고 사례).
        /// </summary>
        private static readonly Vector3 BeeSwarmSpinAxis = new Vector3(0.15f, 1f, 0.08f);
        private const float BeeSwarmSpinDegreesPerSecond = 34f;

        /// <summary>
        /// [B33] 곰 전용 숨쉬기 연출. 리그·애니메이터·AI 상태기계가 없는 프로젝트에서 "살아 있다"를
        /// 살 수 있는 유일하게 값싼 수단이라, 흉곽이 부풀었다 꺼지는 스케일 펄스만 넣는다.
        ///  - **자식 파츠만** 늘였다 줄인다. 콜라이더(BoxCollider)는 루트에 붙어 있고 루트 스케일은
        ///    한 번도 건드리지 않으므로 전투/접촉 판정이 1mm도 변하지 않는다.
        ///  - **x와 z만** 곱한다. y를 건드리면 지면에 닿아 있던 발이 뜨거나 파묻힌다.
        ///    곰의 발 메시는 애초에 루트(콜라이더가 있는 오브젝트)에 있어서 이 펄스의 영향을 받지 않는다.
        ///  - **unscaledDeltaTime**을 쓴다. 엔딩/사망 화면은 Time.timeScale = 0을 걸므로 deltaTime을
        ///    쓰면 그 화면에서 곰이 첫 프레임에 얼어붙는다(AGENT_BRIEF 4장의 실제 사고 사례).
        ///  - 위상을 (islandIndex, spawnOrder)로 어긋내 같은 섬의 곰 여러 마리가 한 몸처럼 숨쉬지 않게 한다.
        /// 진폭 1.6%는 가슴 반폭 0.386m 기준 약 6mm다 - 서 있는 짐승의 호흡으로 읽히되, 배 껍질을
        /// 몸통에서 띄워 둔 여유(14mm) 안이라 어떤 위상에서도 껍질과 몸통이 파고들지 않는다.
        /// </summary>
        private const float BearBreathRadiansPerSecond = 1.4f; // 주기 약 4.5초 = 분당 13회
        private const float BearBreathWidthAmplitude = 0.016f;
        private const float BearBreathDepthAmplitude = 0.010f;

        private Transform[] breathParts;
        private Vector3[] breathBaseScales;
        private float breathPhase;

        /// <summary>
        /// [B34] 곰 전용 프로시저럴 모션(걸음 바운스 · 어깨 혹 관성 · 포효/돌진/슬램). 곰이 아니면 null이다.
        ///
        /// 위 숨쉬기와 채널이 겹치지 않는다: 숨쉬기는 자식의 **localScale**만, 모션은 자식의
        /// **localPosition**만 쓴다. 둘 다 루트(콜라이더)는 절대 건드리지 않으므로 전투/접촉 판정은
        /// 어느 쪽에도 영향을 받지 않는다. 숨쉬기를 CreatureMotion으로 옮기지 않은 이유도 여기 있다 -
        /// 숨쉬기는 timeScale = 0인 엔딩/사망 화면에서도 계속 돌아야 해서 unscaledDeltaTime을 쓰지만,
        /// 모션은 반대로 그 화면에서 완전히 멈춰야 한다(CreatureMotion 클래스 주석의 제약 (3)).
        /// </summary>
        private CreatureMotion bearMotion;

        /// <summary>현재 이 위험 요소가 활성 상태(물리쳐지지 않음)인지 여부.</summary>
        public bool IsActive => !isDefeated;

        /// <summary>
        /// 위험 요소 종류에 맞춰 전투 가능 여부와 체력을 설정한다.
        /// 절차적으로 생성되는 위험 요소는 프리팹이 따로 없으므로, 스포너가 hazardType을 지정한 직후
        /// 이 메서드를 호출해 종류별 전투 설계를 반영해야 한다.
        /// </summary>
        public void ConfigureForType()
        {
            switch (hazardType)
            {
                case HazardType.Bear:
                    if (isBearCub)
                    {
                        // [B37] 새끼 곰: 위협 자체는 거의 없다. 진짜 위험은 이 녀석이 부르는 어미다.
                        //  - 체력 14 = 성체 50의 28%. 무기 한두 방이면 쓰러지지만, 때리는 순간
                        //    주변 30m 성체가 전부 달려온다(AlarmNearbyAdults) - 그게 진짜 대가다.
                        //  - 접촉 피해 3 = 성체 10의 30%. 목록에서 가장 낮다(대왕 크랩 8 아래).
                        //    출혈도 걸지 않는다(ApplyHazardEffect의 새끼 분기) - 새끼가 스치는 것으로
                        //    성체와 같은 상태 이상이 걸리면 "훨씬 낮게"라는 설계가 무의미해진다.
                        isCombatTarget = true;
                        maxHealth = 14f;
                        directDamage = 3f;
                        break;
                    }

                    // 곰: 살로 된 맹수 중에서는 맷집이 가장 세다(갑각을 두른 대왕 크랩만 이보다 단단하다).
                    isCombatTarget = true;
                    maxHealth = 50f;
                    break;
                case HazardType.GiantCrab:
                    // [B30] 대왕 크랩: "느리지만 단단하다"를 기존 두 축(체력 / 접촉 피해)으로만 표현한다.
                    //  - 체력 60: 기존 최대였던 곰 50보다 20% 위. 갑각이 이 종의 유일한 정체성이라
                    //    맷집만큼은 목록 최상단에 두되, 새 수치대(예: 100)를 열지 않는다.
                    //    무기 피해량 기준으로 곰보다 공격 1~2회가 더 든다는 정도의 차이다.
                    //  - 접촉 피해 8: 지시대로 곰(10)보다 낮다. 벌떼/곰이 쓰는 코드 기본값 10과
                    //    상어 18 사이의 기존 스케일 안에서 가장 아래를 잡은 값이고, 대신 곰/식인종과
                    //    같은 출혈(ApplyBleeding)을 건다 - 집게에 베이는 상처가 게다운 위협 방식이다.
                    //    "느리다"는 이동/추격 코드가 없는 정적 위험요소라는 기존 전제로 이미 표현된다
                    //    (HazardSpawner의 마릿수 주석 참고) - 새 이동 파라미터를 만들지 않는다.
                    isCombatTarget = true;
                    maxHealth = 60f;
                    directDamage = 8f;
                    break;
                case HazardType.Cannibal:
                    // 식인종: 곰보다는 약하지만 여전히 위협적이다.
                    isCombatTarget = true;
                    maxHealth = 35f;
                    break;
                case HazardType.BeeSwarm:
                    // 벌떼: 무기로 쉽게 쫓아낼 수 있다.
                    isCombatTarget = true;
                    maxHealth = 12f;
                    break;
                case HazardType.Shark:
                    // 상어: 체력 자체는 곰보다 낮지만, 물속에서는 무기를 안정적으로 쓰기 어렵다는 전제로
                    // 직접 피해량(directDamage)을 다른 위험 요소보다 높게 잡아 위협적으로 만든다.
                    isCombatTarget = true;
                    maxHealth = 25f;
                    directDamage = 18f;
                    break;
                default:
                    // 독사/전갈/함정: 전투 대상이 아니라 피하거나 감수해야 하는 위험 요소다.
                    isCombatTarget = false;
                    break;
            }

            currentHealth = maxHealth;
        }

        /// <summary>
        /// [B33] 곰의 숨쉬기 펄스가 곱해질 자식 파츠와 그 원본 스케일을 한 번만 캐시한다.
        /// Start인 이유: 이 컴포넌트는 HazardSpawner.SpawnSingleHazard가 시각 파츠를 **다 만든 뒤**
        /// AddComponent로 붙이지만, 그때 실행되는 것은 Awake다. 파츠 정리(RemoveLegacyPart)의 Destroy가
        /// 프레임 끝까지 지연되므로 한 프레임 뒤인 Start에서 잡아야 목록이 확정된다.
        /// 곰이 아니면 배열을 만들지 않으므로 다른 위험 요소에는 비용이 0이다.
        /// </summary>
        // ═════════════════════════════════════════════════════════════════════════════
        //  [B37] 활성 위험 요소 목록(새끼 곰 → 어미 호출용)
        //
        //  왜 static 목록인가: 새끼가 어미를 부를 때마다 FindObjectsByType을 부르면 섬마다 수십 개인
        //  위험 요소 전체를 매번 훑는다. 대신 자기 자신을 OnEnable에 등록하고 OnDisable에서 뺀다
        //  (OnDisable은 오브젝트가 파괴될 때도 불리므로 씬을 다시 열어도 죽은 참조가 남지 않는다).
        //
        //  ⚠️ 종류로 걸러서 등록하지 않는 이유: OnEnable은 AddComponent **그 순간** 실행되는데,
        //  스포너는 그 다음 줄에서야 hazardType/isBearCub을 세운다. 즉 등록 시점에는 두 값이 아직
        //  기본값이라 믿을 수 없다. 그래서 전부 등록하고 **쓰는 쪽에서** 걸러낸다(호출은 새끼가
        //  플레이어를 감지하거나 맞은 순간뿐이고 쿨다운도 있어 비용이 문제 되지 않는다).
        //  안전망으로 목록을 훑을 때 null 항목(씬 전환 중 파괴)도 함께 걷어낸다.
        // ═════════════════════════════════════════════════════════════════════════════
        private static readonly List<HazardSource> activeHazards = new List<HazardSource>();

        private void OnEnable()
        {
            if (!activeHazards.Contains(this))
                activeHazards.Add(this);
        }

        private void OnDisable()
        {
            activeHazards.Remove(this);
        }

        /// <summary>목록 재구축 경고를 (개체가 아니라) 목록 단위로 한 번만 남기기 위한 표시.</summary>
        private static bool activeHazardsRebuiltWarned;

        /// <summary>
        /// activeHazards를 씬 실물에서 다시 만든다. 평상시에는 절대 불리지 않는다 - OnEnable/OnDisable이
        /// 목록을 정확히 관리하기 때문이다. 도메인 리로드로 static 목록만 비워졌을 때의 복구 경로다.
        /// Unity 6.5: FindObjectsByType은 **1인자 형태만** 쓴다(FindObjectsSortMode 오버로드는 CS0618).
        /// SaveLoadController.RestoreHazardsAndCreatures와 같은 호출 형태다.
        /// </summary>
        private static void RebuildActiveHazards()
        {
            activeHazards.Clear();

            HazardSource[] found = FindObjectsByType<HazardSource>(FindObjectsInactive.Exclude);
            for (int i = 0; i < found.Length; i++)
            {
                if (found[i] != null)
                    activeHazards.Add(found[i]);
            }

            if (!activeHazardsRebuiltWarned)
            {
                activeHazardsRebuiltWarned = true;
                Debug.LogWarning("[HazardSource] 활성 위험 요소 목록(static)이 비어 있어 씬에서 다시 만들었다(" +
                    activeHazards.Count + "개). Play 중 스크립트 재컴파일(도메인 리로드) 직후에만 일어난다.");
            }
        }

        private void Start()
        {
            if (hazardType != HazardType.Bear)
                return;

            // [B35] 추격 AI는 시각 파츠 유무와 무관하게 붙인다(아래 숨쉬기/모션은 자식이 있어야 의미가 있다).
            InitBearAI();

            int count = transform.childCount;
            if (count <= 0)
                return;

            var parts = new Transform[count];
            var scales = new Vector3[count];
            int kept = 0;
            for (int i = 0; i < count; i++)
            {
                Transform child = transform.GetChild(i);
                if (child == null)
                    continue;

                // [B34] 발톱만 흉곽 펄스에서 뺀다. 발톱 뿌리는 발바닥 안에 1.5cm밖에 안 묻혀 있는데
                // (CreatureMeshLibrary.BearClawsMeters 참고) 발은 루트에 있어 절대 스케일되지 않으므로,
                // 발톱만 1.6% 부풀면 숨 쉴 때마다 발 끝에서 최대 7mm씩 떠올랐다 가라앉는다.
                // 지금까지도 그랬지만(발톱이 Underside 안에 있었다) 이제 발톱은 "지면 고정" 파츠라
                // CreatureMotion도 건드리지 않는다 - 두 연출의 약속을 여기서도 맞춘다.
                if (child.name == "Claws")
                    continue;

                parts[kept] = child;
                scales[kept] = child.localScale;
                kept++;
            }

            breathParts = new Transform[kept];
            breathBaseScales = new Vector3[kept];
            System.Array.Copy(parts, breathParts, kept);
            System.Array.Copy(scales, breathBaseScales, kept);

            // 개체마다 다른 위상. UnityEngine.Random을 쓰지 않는다(재현성 규칙 - AGENT_BRIEF 2장 6번).
            breathPhase = Mathf.Repeat(islandIndex * 1.31f + spawnOrder * 0.77f, 6.2831853f);

            // [B34] 프로시저럴 모션을 붙인다. 여기(Start)에서 붙이는 이유는 위 숨쉬기와 같다 -
            // 시각 파츠가 전부 만들어지고 예전 파츠 정리(Destroy)까지 끝난 뒤라야 자식 목록이 확정된다.
            // 위상 씨앗도 숨쉬기와 같은 결정적 식별자에서 뽑되, 두 연출이 한 박자로 겹쳐 보이지 않도록
            // 계수를 다르게 준다.
            // [B37] 새끼는 같은 통짜 메시 경로를 쓰되 진폭만 몸 크기에 비례해 줄인 판으로 붙는다.
            float motionPhase = Mathf.Repeat(islandIndex * 2.07f + spawnOrder * 1.43f, 6.2831853f);
            bearMotion = isBearCub
                ? CreatureMotion.AttachBearCub(gameObject, motionPhase)
                : CreatureMotion.AttachBear(gameObject, motionPhase);

            if (bearMotion != null && isDefeated)
                bearMotion.enabled = false;
        }

        /// <summary>
        /// 매 프레임 접촉 쿨다운과 물리친 뒤의 재등장 타이머를 진행시킨다.
        /// </summary>
        private void Update()
        {
            if (contactCooldownTimer > 0f)
                contactCooldownTimer -= Time.deltaTime;

            // 벌떼가 살아 있는 동안에만 무리가 술렁인다(물리친 뒤에는 보이지도 않는다).
            if (hazardType == HazardType.BeeSwarm && !isDefeated)
                transform.Rotate(BeeSwarmSpinAxis, BeeSwarmSpinDegreesPerSecond * Time.unscaledDeltaTime, Space.Self);

            // 곰이 살아 있는 동안에만 숨을 쉰다.
            if (breathParts != null && !isDefeated)
                UpdateBearBreathing();

            // [B35] 곰 추격 AI. 세 가지 조건에서 **아예 돌지 않는다**:
            //  · timeScale <= 0 (타이틀/설정/엔딩/사망 화면) - 이동도 상태 갱신도 전부 정지한다.
            //    숨쉬기(unscaledDeltaTime)와 달리 AI는 게임 시간에 묶여야 한다.
            //  · isDefeated (SetVisualActive(false) 구간) - 안 보이는 곰이 돌아다니면 안 된다.
            //  · 곰이 아닌 위험 요소 - bearAiReady가 곰에서만 true다.
            //  [B37] 새끼는 같은 세 조건 아래에서 **자기 몫의 AI**로 갈라진다(성체 경로는 손대지 않는다).
            if (bearAiReady && !isDefeated && Time.timeScale > 0f)
            {
                if (isBearCub)
                    UpdateBearCubAI(Time.deltaTime);
                else
                    UpdateBearAI(Time.deltaTime);
            }

            if (!isDefeated)
                return;

            respawnTimer += Time.deltaTime;
            if (respawnTimer >= respawnSeconds)
            {
                isDefeated = false;
                currentHealth = maxHealth;
                respawnTimer = 0f;
                SetVisualActive(true);

                // [B34] 다시 나타났으니 모션도 되살린다(처치 시 원자세로 되돌린 뒤 꺼 뒀다).
                if (bearMotion != null)
                    bearMotion.enabled = true;

                // [B35] 재등장은 쓰러진 자리가 아니라 처음 서 있던 자리에서 한다. 쓰러진 자리는
                // 플레이어가 방금 싸운 곳(대개 자기 거점 앞)이라, 그 자리에 그대로 되살아나면
                // 120초마다 거점 안에서 곰이 솟는다.
                ResetBearAI(true);
            }
        }

        /// <summary>
        /// [B33] 곰의 흉곽을 좌우/앞뒤로만 부풀렸다 꺼뜨린다. 루트(콜라이더)와 y축은 손대지 않는다.
        /// 자식이 파괴된 경우(예전 방식 파츠 정리)를 대비해 매번 null을 확인한다.
        /// </summary>
        private void UpdateBearBreathing()
        {
            breathPhase += Time.unscaledDeltaTime * BearBreathRadiansPerSecond;
            float pulse = Mathf.Sin(breathPhase);
            float width = 1f + pulse * BearBreathWidthAmplitude;
            float depth = 1f + pulse * BearBreathDepthAmplitude;

            for (int i = 0; i < breathParts.Length; i++)
            {
                Transform part = breathParts[i];
                if (part == null)
                    continue;

                Vector3 baseScale = breathBaseScales[i];
                part.localScale = new Vector3(baseScale.x * width, baseScale.y, baseScale.z * depth);
            }
        }

        /// <summary>
        /// 지정한 대상에게 이 위험 요소의 효과를 적용한다.
        /// 위험 요소 종류에 따라 중독/출혈/골절/직접 피해 중 알맞은 효과를 준다.
        /// </summary>
        public void ApplyHazardEffect(SurvivalStats target)
        {
            if (target == null)
                return;

            // 전투/접촉 시각 피드백: 위험요소와 "접촉한 이 순간"에만 화면 테두리를 붉게 번쩍인다.
            // SurvivalStats.TakeDamage 안에 걸면 굶주림/일사병 등 상시 피해에도 매번 발동해 버리므로,
            // 반드시 이 접촉 진입점에서만 트리거해야 한다 (CombatFeedbackUI 클래스 주석 참고).
            // [B7 디렉터] 피격 세기 3단계 연결 완료. GetContactDamage()가 곰 10 / 상어 18 / 벌떼 10 /
            // 대왕 크랩 8(B30) / 독사·전갈·함정 0을 돌려주고, CombatFeedbackUI가 이를 약/중/강으로 나눠 번쩍인다.
            // 0을 "피격 아님"으로 버리지 않는다 - 독사·전갈·함정은 체력이 안 깎일 뿐 접촉은 일어났고,
            // 무반응은 이 프로젝트가 반복해서 낸 실패 패턴이다(가장 약한 단계로 반드시 표시된다).
            CombatFeedbackUI.Instance?.TriggerHit(GetContactDamage());

            // B4-11: 화면 테두리 플래시(2D)는 "맞았다"만 알려줄 뿐 어디서 맞았는지는 알려주지 못한다.
            // 접촉 지점 근처(플레이어 가슴 높이)에 Danger Red 입자를 짧게 튀겨 위치 정보를 더한다.
            // 화면 전체 이펙트가 아니라 월드 공간 국소 이펙트이고, 상시 피해(굶주림/일사병)가 아니라
            // 이 접촉 진입점에서만 호출되므로 위 TriggerHit과 정확히 같은 조건에서만 발동한다.
            // [B5 qa 지적] 원래 target(플레이어) 위치에서 터뜨려서, 곰이든 상어든 함정이든 항상 내 가슴에서만
                // 입자가 튀었다. 화면 테두리 플래시와 정보량이 똑같아 이펙트의 존재 이유가 없었다.
                // 위험요소와 플레이어의 중간점으로 옮겨 "어느 쪽에서 맞았는지"가 읽히게 한다.
                Vector3 hitPoint = Vector3.Lerp(transform.position, target.transform.position, 0.5f) + Vector3.up * 1f;
                EffectBuilder.PlayHitBurst(hitPoint);

            switch (hazardType)
            {
                case HazardType.VenomousSnake:
                case HazardType.Scorpion:
                    // 독사/전갈: 중독 상태로 만든다.
                    target.ApplyPoison();
                    break;

                case HazardType.Bear:
                    // [B37] 새끼 곰만 출혈 없이 낮은 직접 피해만 준다(몸으로 밀치는 정도).
                    if (isBearCub)
                    {
                        target.TakeDamage(directDamage, DamageCause.Predator);
                        break;
                    }
                    goto case HazardType.Cannibal;

                case HazardType.Cannibal:
                case HazardType.GiantCrab:
                    // 곰/식인종/대왕 크랩: 직접 피해 + 출혈을 유발한다.
                    // 대왕 크랩의 피해량은 곰보다 낮게(8) ConfigureForType에서 정해지고, 여기서는
                    // 분류만 공유한다 - 이 파일은 밸런스 수치를 두 곳에 적지 않는다.
                    target.TakeDamage(directDamage, DamageCause.Predator);
                    target.ApplyBleeding();
                    break;

                case HazardType.BeeSwarm:
                    // 벌떼: 직접 피해를 입힌다 (중독/출혈은 없음).
                    target.TakeDamage(directDamage, DamageCause.Predator);
                    break;

                case HazardType.Trap:
                    // 함정: 골절 상태로 만든다.
                    target.ApplyBrokenBone();
                    break;

                case HazardType.Shark:
                    // 상어: 직접 피해 + 출혈. 사망 원인은 Predator와 구분되는 SharkAttack으로 기록해
                    // 게임 오버 화면에 "바닷속에서 상어의 습격" 같은 정확한 문구가 뜨게 한다.
                    target.TakeDamage(directDamage, DamageCause.SharkAttack);
                    target.ApplyBleeding();
                    break;

                case HazardType.FoodShortage:
                case HazardType.Dehydration:
                    // 음식 부족/탈수는 SurvivalStats의 허기/갈증 감소 로직에서 이미 처리되므로 별도 효과 없음.
                    break;
            }
        }

        /// <summary>
        /// [ui-engineer 요청 / 피격 세기 3단계] 이 위험 요소와 한 번 접촉했을 때 실제로 체력에서 깎이는
        /// 즉시 피해량을 반환한다. ApplyHazardEffect의 switch와 **같은 분류**를 쓰므로, 종류별 효과가
        /// 바뀌면 반드시 두 곳을 함께 고쳐야 한다(값 자체는 directDamage 필드를 그대로 읽으므로
        /// 밸런스 수치를 여기서 새로 정의하지 않는다 - 씬/ConfigureForType이 정한 값이 그대로 나온다).
        ///
        /// 0을 반환하는 경우가 정상적으로 존재한다: 독사/전갈(중독)·함정(골절)은 접촉 순간에 체력을
        /// 깎지 않고 상태 이상만 건다. 화면 연출에서 이 0을 "피격 아님"으로 취급해 아무 것도 보여주지
        /// 않으면 예전의 무반응 문제가 그대로 돌아오므로, **0도 가장 약한 단계로 반드시 표시**할 것.
        /// (상태 이상이 걸렸다는 사실 자체는 StatusEffectWarningUI + AudioManager.PlayStatusOnset이 알린다.)
        /// </summary>
        public float GetContactDamage()
        {
            switch (hazardType)
            {
                case HazardType.Bear:
                case HazardType.Cannibal:
                case HazardType.BeeSwarm:
                case HazardType.Shark:
                case HazardType.GiantCrab:
                    return directDamage;

                default:
                    // 독사/전갈/함정/음식부족/탈수: 접촉 순간의 직접 피해는 없다.
                    return 0f;
            }
        }

        /// <summary>
        /// 인벤토리에서 가장 피해량이 높은 무기(isWeapon)를 찾아 이 위험 요소를 공격한다.
        /// 무기가 없으면 공격할 수 없다. 체력이 0이 되면 물리쳐서 일정 시간 동안 비활성화된다.
        /// 버그 수정: 손도끼(20회)/창(15회)/칼(10회)처럼 무기 ItemData에 이미 maxUses(최대 사용 횟수)가
        /// 설정되어 있고 세이브/로드·인벤토리 UI 표시("최대 N회 사용")까지 다 준비되어 있었는데,
        /// 정작 전투에서는 무기를 소모하는 코드가 없어 무기가 절대 닳지 않던 문제를 고쳤다.
        /// 공격이 성공할 때마다 PlayerInventory.UseItem으로 실제 내구도를 1 소모시킨다.
        /// </summary>
        public bool TryAttack(PlayerInventory inventory, PlayerSkills skills)
        {
            if (!isCombatTarget || isDefeated || inventory == null)
                return false;

            InventoryItem bestWeaponItem = FindBestWeapon(inventory);
            if (bestWeaponItem == null)
                return false;

            currentHealth = Mathf.Max(0f, currentHealth - bestWeaponItem.data.weaponDamage);
            AudioManager.Instance?.PlayHit(); // 공격 적중 효과음

            // [B37] 새끼를 때리면 죽든 살든 **즉시** 어미가 온다. 쿨다운을 0으로 밀어 확실히 통과시킨다
            // (플레이어가 접근만 해서 이미 한 번 불렀더라도, 때린 것은 별개의 사건이다).
            if (isBearCub)
            {
                cubAlarmTimer = 0f;
                AlarmNearbyAdults();
            }

            // 내구도 소모: 무제한(IsUnlimited) 무기는 자동으로 소모되지 않는다. 사용 횟수가 다하면
            // UseItem이 인벤토리에서 자동으로 제거해 "무기가 파손되었다"를 자연스럽게 표현한다.
            inventory.UseItem(bestWeaponItem);

            if (currentHealth <= 0f)
            {
                isDefeated = true;
                respawnTimer = 0f;
                SetVisualActive(false);

                // [B34 패배] 모션을 끊고 파츠를 원자세로 되돌린 뒤 정지시킨다. 되돌리지 않으면
                // 재등장(respawnSeconds 뒤)했을 때 몸통이 마지막 프레임 자세로 어긋난 채 나타난다.
                if (bearMotion != null)
                {
                    bearMotion.StopAndReset();
                    bearMotion.enabled = false;
                }

                if (skills != null)
                    skills.AddExperience(SkillType.Physical, defeatExperience);
            }
            else if (isBearCub)
            {
                // [B37] 맞고 살아남은 새끼는 포효가 아니라 **도망**으로 반응한다(포효/돌진/슬램 없음).
                EnterBearCubState(BearState.Chase);
            }
            else if (bearMotion != null)
            {
                // [B34 피격 후 생존] 맞고도 버틴 곰은 포효로 반응한다. 지금 이 프로젝트에 상태 기계가
                // 없으므로, "공격받음"을 알 수 있는 유일한 지점이 여기다. 시퀀스가 이미 재생 중이면
                // CreatureMotion이 알아서 무시하므로 연타로 눌러도 동작이 되감기지 않는다.
                bearMotion.PlayRoar();
            }

            return true;
        }

        /// <summary>
        /// B3-5: 세이브 파일에서 읽어온 처치 상태를 그대로 되돌린다. TryAttack과 달리 무기/인벤토리를
        /// 전혀 거치지 않고 isDefeated/체력/시각 표시만 직접 맞춘다 - 저장 시점에 "처치됨"이었던 위험
        /// 요소를 불러온 뒤에도 다시 처치된 채로 보이게 하기 위함이다. 재등장까지 남은 시간은 저장하지
        /// 않으므로(SaveData.defeatedHazards 주석 참고) respawnTimer는 항상 0부터 다시 시작한다 -
        /// 즉 불러온 직후부터 respawnSeconds가 다시 꽉 채워 흘러야 재등장한다(오프라인 경과 시간 미반영,
        /// SaveLoadController.RestoreHazardsAndCreatures 주석 참고).
        /// </summary>
        public void RestoreDefeatedState(bool defeated)
        {
            isDefeated = defeated;
            respawnTimer = 0f;
            currentHealth = defeated ? 0f : maxHealth;
            SetVisualActive(!defeated);

            // [B34] 처치 상태로 복원되면 모션도 원자세로 되돌린 뒤 멈춘다(TryAttack의 처치 경로와 동일).
            // 이 메서드는 Start보다 먼저 불릴 수 있으므로(세이브 복원 순서) bearMotion이 아직 null일 수
            // 있고, 그 경우는 Start가 isDefeated를 보고 처음부터 꺼진 채로 붙인다.
            if (bearMotion != null)
            {
                if (defeated)
                {
                    bearMotion.StopAndReset();
                    bearMotion.enabled = false;
                }
                else
                {
                    bearMotion.enabled = true;
                }
            }

            // [B35] 추격 상태도 함께 되돌린다. 세이브에는 곰의 위치가 들어 있지 않고(스포너가 같은
            // 시드로 같은 자리에 다시 놓는다) 이 메서드는 Start보다 먼저 불릴 수 있으므로,
            // bearAiReady가 아직 false면 ResetBearAI가 알아서 아무 일도 하지 않는다.
            ResetBearAI(!defeated);
        }

        /// <summary>
        /// 인벤토리에 보유한 무기(isWeapon) 중 피해량이 가장 높은 InventoryItem 인스턴스를 찾는다. 없으면 null.
        /// ItemData가 아니라 InventoryItem을 반환해야 TryAttack에서 그 무기 하나의 내구도를 실제로 소모시킬 수 있다.
        /// </summary>
        private InventoryItem FindBestWeapon(PlayerInventory inventory)
        {
            InventoryItem best = null;
            foreach (var item in inventory.items)
            {
                if (item.data == null || !item.data.isWeapon)
                    continue;

                if (best == null || item.data.weaponDamage > best.data.weaponDamage)
                    best = item;
            }
            return best;
        }

        /// <summary>
        /// 물리쳐서 비활성화하거나 재등장시킬 때, 시각적으로도 보이지 않도록/보이도록 전환한다.
        /// 콜라이더가 있다면 함께 꺼서 접촉 판정도 멈춘다.
        ///
        /// [B29 버그 수정 - 물리친 위험 요소의 파츠가 공중에 그대로 남아 있었다]
        /// 예전 코드는 `GetComponent<Renderer>()`로 **루트 하나만** 껐다. 그런데 위험 요소는 전부
        /// 자식 파츠를 갖는다(곰: 눈 2 + 코, 식인종: 눈 2 + 창 + 돌촉, 상어: 눈 2 + 등지느러미,
        /// 벌떼: 벌 5). 즉 곰을 쓰러뜨리면 몸통만 사라지고 눈 두 개와 코가 허공에 뜬 채로
        /// respawnSeconds(120초) 동안 남았고, 식인종은 창이 그대로 서 있었다. 세이브에서 처치 상태를
        /// 복원할 때(RestoreDefeatedState)도 같은 경로라 같은 증상이 나온다.
        /// 자식 렌더러까지 모두 끈다. 콜라이더는 예전 그대로 루트 것만 다룬다 - 시각 파츠에는
        /// 콜라이더가 없어야 하고(CreateVisualPart가 즉시 제거한다) 실제로 없기 때문이다.
        /// </summary>
        private void SetVisualActive(bool active)
        {
            var renderers = GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] != null)
                    renderers[i].enabled = active;
            }

            var collider = GetComponent<Collider>();
            if (collider != null)
                collider.enabled = active;
        }

        /// <summary>
        /// 플레이어의 콜라이더와 접촉을 시작했을 때 즉시 위험 요소 효과를 적용한다.
        /// 플레이어 오브젝트에는 SurvivalStats 컴포넌트가 붙어 있어야 한다.
        /// </summary>
        private void OnTriggerEnter(Collider other)
        {
            TryApplyContactDamage(other);
        }

        /// <summary>
        /// 접촉이 계속 유지되는 동안(예: 도망치지 않고 맹수 옆에 서 있는 경우) contactDamageCooldown
        /// 간격으로 반복 피해를 입혀, 전투/도주 없이 버티기만 하는 것을 막는다.
        /// </summary>
        private void OnTriggerStay(Collider other)
        {
            if (contactCooldownTimer <= 0f)
                TryApplyContactDamage(other);
        }

        /// <summary>
        /// 물리쳐지지 않은 상태일 때만 접촉 대상에게 위험 효과를 적용하고, 쿨다운을 초기화한다.
        ///
        /// [B35] 예전에 있던 firstContact 분기(첫 접촉 = PlayCharge)를 제거했다. 그건 추격 AI가 없던
        /// 시절 "달려온다"를 표현할 자리가 접촉뿐이라 임시로 걸어 둔 것이고(B34 주석), 이제 돌진은
        /// 실제 추격이 시작되는 지점(EnterBearState의 Chase 진입)에서 재생된다.
        /// 접촉 순간에 남는 연출은 앞발 내려치기 하나뿐이다 - 실제로 맞은 순간이 그것이다.
        /// 피해 계산은 예전과 1도 달라지지 않았다(두 진입 경로가 원래부터 완전히 같았다).
        /// </summary>
        private void TryApplyContactDamage(Collider other)
        {
            if (isDefeated)
                return;

            SurvivalStats stats = other.GetComponent<SurvivalStats>();
            if (stats == null)
                return;

            ApplyHazardEffect(stats);
            contactCooldownTimer = contactDamageCooldown;
            AudioManager.Instance?.PlayDamage(); // 피해를 입었을 때 경고 효과음

            // [B37] 새끼는 앞발을 내려치지 않는다(때리는 동작 자체가 없다 - 도망만 친다).
            // 대신 이만큼 붙었다는 것은 어미를 부르고도 남을 거리라, 여기서도 한 번 부른다.
            if (isBearCub)
            {
                AlarmNearbyAdults();
                return;
            }

            if (bearMotion != null)
                bearMotion.PlaySlam(); // 앞발을 내려친다(이미 시퀀스 중이면 CreatureMotion이 무시한다)
        }

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
        private float bearHoverOffset = CreatureVisualBuilder.BearGroundOffset; // 지면에서 루트 중심까지의 높이
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
