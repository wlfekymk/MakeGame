using System.Collections.Generic;
using UnityEngine;
using MakeGame.Data;
using MakeGame.Player;

namespace MakeGame.Systems
{
    /// <summary>
    /// 던진 창 한 자루. 날아가는 동안의 궤적·명중 판정과, 빗나간 뒤 땅에 꽂혀 회수되기까지를 담당한다.
    ///
    /// **Rigidbody를 쓰지 않는다**(프로젝트 관례 - RaftSailing.cs:133 "뗏목은 Rigidbody 없이 트랜스폼으로
    /// 구동된다", MarineLifeSpawner "위치는 전부 순수 수학"). 궤적은 속도 + 중력 적분이고, 명중 판정은
    /// 이 프레임에 지나갈 선분에 대한 SphereCastNonAlloc 1회다. 즉 프레임당 물리 질의 1회 · 할당 0.
    ///
    /// **날아가는 동안에는 콜라이더가 아예 없다.** 꽂힌 뒤에만 조준 프롬프트용 작은 트리거 구체를 단다
    /// (InteractionPromptUI가 조준 대상을 알아내려면 콜라이더가 필요하다). 트리거라서
    /// CreatureMotion의 장애물 캐스트(QueryTriggerInteraction.Ignore)에 걸리지 않고, BuildingSystem의
    /// 건축 레이는 "지형도 조각도 아닌 히트"를 건너뛰므로(CastBuildRay) 건축을 막지도 않는다.
    ///
    /// **회수는 E키가 아니라 근접 자동 회수다.** 상호작용 분기의 주인인 InteractionController.cs가 이
    /// 작업의 락 밖이라 E에 새 분기를 넣을 수 없다(보고서의 [막힘] 항목). 대신 플레이어가
    /// <see cref="recoverRadius"/> 안으로 걸어 들어오면 자동으로 가방에 돌아가고, 조준하면
    /// InteractionPromptUI가 그 사실을 알려 준다.
    ///
    /// 동시에 존재할 수 있는 개수는 <see cref="MaxActive"/>(5)로 묶는다. 초과분은 가장 오래된 것부터
    /// 사라진다(회수되지 않고 소실된다 - 무한정 쌓여 물리 질의와 오브젝트가 늘어나는 것을 막는다).
    /// </summary>
    public class ThrownWeapon : MonoBehaviour
    {
        /// <summary>월드에 동시에 존재할 수 있는 던진 무기의 최대 개수(성능 상한, 감독 지시).</summary>
        public const int MaxActive = 5;

        /// <summary>비행 중 판정에 쓰는 구체 반경(m). 창 자루 굵기보다 조금 넉넉하게 잡아 스치는 명중을 인정한다.</summary>
        private const float CastRadius = 0.18f;

        /// <summary>캐스트 거리에 더하는 여유(m). 프레임 이동량이 아주 작을 때도 접촉을 놓치지 않게 한다.</summary>
        private const float CastSkin = 0.05f;

        /// <summary>궤적에 적용하는 중력 가속도(m/s²). PlayerController.gravity(-20)보다 조금 약해 더 멀리 난다.</summary>
        private const float Gravity = -18f;

        /// <summary>아무것도 맞히지 못한 창이 스스로 땅을 찾아 꽂히는 최대 비행 시간(초).</summary>
        private const float MaxFlightSeconds = 6f;

        /// <summary>수면 아래에서 초당 잃는 속도 비율. 물속에서 사거리가 급격히 짧아지는 이유가 이것이다.</summary>
        private const float UnderwaterDragPerSecond = 2.2f;

        /// <summary>꽂힌 창이 아무도 줍지 않을 때 사라지기까지의 시간(초). 섬에 창이 영원히 쌓이지 않게 한다.</summary>
        private const float DespawnSeconds = 300f;

        /// <summary>회수 판정 주기(초). 매 프레임 거리 계산을 하지 않기 위한 값이다.</summary>
        private const float RecoverCheckInterval = 0.2f;

        [Tooltip("플레이어가 이 거리(m) 안으로 들어오면 자동으로 가방에 회수된다.")]
        public float recoverRadius = 2.2f;

        // ── 활성 목록(정적) ───────────────────────────────────────────────────────
        //
        // OnEnable/OnDisable이 아니라 Launch/Despawn에서 직접 넣고 뺀다. 이 오브젝트는 런타임에만
        // 만들어지고 비활성화되는 경로가 없어서, 수명 = 목록 체류 시간이 정확히 일치한다.
        private static readonly List<ThrownWeapon> active = new List<ThrownWeapon>();

        // 파츠 머티리얼은 던질 때마다 새로 만들지 않고 공유한다(AGENT_BRIEF 4장 "머티리얼을 파츠마다
        // 만들지 마라"). 씬 오브젝트가 아니라 씬 전환으로 파괴되지 않으므로 리셋 훅에서도 비우지 않는다
        // (비우면 도메인 리로드를 끈 플레이 모드에서 매번 새 머티리얼이 생겨 오히려 샌다).
        private static Material shaftMaterial;
        private static Material tipMaterial;

        // 프레임마다 재사용하는 캐스트 결과 버퍼(할당 0). 한 선분에 겹칠 수 있는 콜라이더 수는
        // 지형 + 생물 몇 개면 충분하다.
        private static readonly RaycastHit[] castHits = new RaycastHit[16];

        /// <summary>
        /// 도메인 리로드를 끈 플레이 모드에서 static 목록이 이전 실행의 죽은 참조를 들고 시작하지
        /// 않게 비운다(AGENT_BRIEF 4장 2번). 머티리얼 캐시는 일부러 남긴다(위 주석 참고).
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            active.Clear();
        }

        /// <summary>지금 월드에 떠 있거나 꽂혀 있는 던진 무기 수(디버그·상한 판정용).</summary>
        public static int ActiveCount => active.Count;

        // ── 개별 상태 ────────────────────────────────────────────────────────────

        private ItemData weaponData;
        private int remainingUses;
        private bool recoverable;
        private float damage;

        private Vector3 velocity;
        private float flightTimer;
        private float waterSurfaceY;

        private bool stuck;
        private float stuckTimer;
        private float recoverCheckTimer;

        private PlayerInventory ownerInventory;
        private PlayerSkills ownerSkills;
        private Transform thrower;

        /// <summary>회수하면 돌아올 아이템 종류(프롬프트 표시·용량 판정용).</summary>
        public ItemData WeaponData => weaponData;

        /// <summary>회수하면 복원될 남은 사용 횟수(무제한이면 -1).</summary>
        public int RemainingUses => remainingUses;

        /// <summary>회수할 수 있는 상태인지(부러진 창은 꽂히지 않고 그대로 사라진다).</summary>
        public bool IsRecoverable => recoverable && stuck;

        /// <summary>땅에 꽂혀 멈춘 상태인지.</summary>
        public bool IsStuck => stuck;

        /// <summary>프롬프트에 쓸 이름. 아이템 데이터가 사라진 경우에도 문장이 비지 않게 폴백을 둔다.</summary>
        public string DisplayName => weaponData != null && !string.IsNullOrEmpty(weaponData.itemName)
            ? weaponData.itemName
            : "던진 무기";

        /// <summary>
        /// 창 한 자루를 던진다. 호출자(PlayerController)가 **이미 인벤토리에서 빼고 내구도를 소모한 뒤**
        /// 부르는 것을 전제로 한다 - 이 클래스는 인벤토리를 줄이지 않는다(회수할 때만 되돌린다).
        /// </summary>
        /// <param name="weaponData">던진 무기의 원본 데이터(회수 시 이 종류로 돌아온다).</param>
        /// <param name="remainingUses">던진 시점의 남은 사용 횟수(회수하면 그대로 복원된다).</param>
        /// <param name="recoverable">회수 가능한지. 이번 투척으로 부러졌으면 false(맞은 뒤 사라진다).</param>
        /// <param name="origin">발사 지점(보통 카메라 앞).</param>
        /// <param name="direction">발사 방향(정규화되지 않아도 된다).</param>
        /// <param name="speed">초기 속도(m/s).</param>
        /// <param name="damage">명중 시 입힐 피해량(CombatSystem.GetThrowDamage).</param>
        /// <param name="waterSurfaceY">지금 발밑의 수면 높이. 이보다 낮은 구간에서는 저항이 걸린다.</param>
        /// <param name="inventory">회수 대상 인벤토리. null이면 회수되지 않는다.</param>
        /// <param name="skills">명중 시 경험치를 받을 스킬(없어도 된다).</param>
        /// <param name="thrower">자기 몸에 맞지 않도록 제외할 트랜스폼(플레이어).</param>
        /// <returns>생성된 비행체. 데이터가 없으면 null.</returns>
        public static ThrownWeapon Launch(ItemData weaponData, int remainingUses, bool recoverable,
            Vector3 origin, Vector3 direction, float speed, float damage, float waterSurfaceY,
            PlayerInventory inventory, PlayerSkills skills, Transform thrower)
        {
            if (weaponData == null)
                return null;

            EnforceActiveLimit();

            var go = new GameObject("ThrownWeapon_" + weaponData.itemName);
            go.transform.position = origin;

            var weapon = go.AddComponent<ThrownWeapon>();
            weapon.weaponData = weaponData;
            weapon.remainingUses = remainingUses;
            weapon.recoverable = recoverable;
            weapon.damage = damage;
            weapon.waterSurfaceY = waterSurfaceY;
            weapon.ownerInventory = inventory;
            weapon.ownerSkills = skills;
            weapon.thrower = thrower;

            Vector3 dir = direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector3.forward;
            weapon.velocity = dir * Mathf.Max(1f, speed);
            go.transform.rotation = SafeLook(dir);

            weapon.BuildVisual();
            active.Add(weapon);
            return weapon;
        }

        /// <summary>
        /// 상한(<see cref="MaxActive"/>)에 걸리면 자리를 하나 비운다. 이미 꽂혀 멈춘 것을 먼저 지우고
        /// (아직 날아가는 창을 없애면 방금 던진 것이 허공에서 사라진 것처럼 보인다), 없으면 가장 오래된
        /// 것을 지운다. 목록에 섞여 있을 수 있는 죽은 참조도 이 김에 걷어낸다.
        /// </summary>
        private static void EnforceActiveLimit()
        {
            for (int i = active.Count - 1; i >= 0; i--)
            {
                if (active[i] == null)
                    active.RemoveAt(i);
            }

            while (active.Count >= MaxActive)
            {
                int victim = 0;
                for (int i = 0; i < active.Count; i++)
                {
                    if (active[i] != null && active[i].stuck)
                    {
                        victim = i;
                        break;
                    }
                }

                ThrownWeapon target = active[victim];
                active.RemoveAt(victim);
                if (target != null)
                    target.DestroySelf();
            }
        }

        /// <summary>
        /// 자루(원기둥)와 돌촉(정육면체)만으로 창 모양을 만든다. 프리팹이 없는 프로젝트라
        /// 모든 시각 파츠가 이 방식이다(StructureVisualBuilder.CreateVisualPart가 콜라이더를 떼 준다).
        /// 원기둥의 기본 축은 로컬 +Y이므로 X로 90도 눕혀 자루가 이 오브젝트의 **정면(+Z)**을 향하게 한다
        /// - 그래야 비행 중 transform.rotation = LookRotation(velocity)이 창끝을 진행 방향으로 맞춘다.
        /// </summary>
        private void BuildVisual()
        {
            if (shaftMaterial == null)
                shaftMaterial = StructureVisualBuilder.CreateColorMaterial(StructureVisualBuilder.Driftwood, "driftwood");
            if (tipMaterial == null)
                tipMaterial = StructureVisualBuilder.CreateColorMaterial(StructureVisualBuilder.WeatheredStone, "rock");

            Quaternion layDown = Quaternion.Euler(90f, 0f, 0f);

            // 자루: 길이 1.7m(원기둥 기본 높이 2 × 스케일 0.85), 굵기 9cm.
            StructureVisualBuilder.CreateVisualPart(transform, "Shaft", PrimitiveType.Cylinder,
                new Vector3(0f, 0f, -0.15f), new Vector3(0.045f, 0.85f, 0.045f), shaftMaterial, layDown);

            // 돌촉: 자루 앞끝에 얹는 작은 쐐기.
            StructureVisualBuilder.CreateVisualPart(transform, "Tip", PrimitiveType.Cube,
                new Vector3(0f, 0f, 0.78f), new Vector3(0.07f, 0.07f, 0.24f), tipMaterial);
        }

        /// <summary>
        /// 매 프레임 비행하거나(아직 안 꽂혔으면) 회수를 기다린다.
        /// **Time.deltaTime을 쓴다** - 엔딩/사망 화면(timeScale 0)에서는 창도 함께 멈춰야 한다
        /// (곰 AI와 같은 규칙. 숨쉬기 같은 상시 연출만 unscaled를 쓴다 - AGENT_BRIEF 4장).
        /// </summary>
        private void Update()
        {
            if (Time.timeScale <= 0f)
                return;

            float dt = Time.deltaTime;
            if (stuck)
                UpdateStuck(dt);
            else
                UpdateFlight(dt);
        }

        /// <summary>
        /// 포물선 한 걸음을 전진시키고, 그 걸음 안에서 무엇을 맞혔는지 판정한다.
        /// 수면 아래에서는 속도가 빠르게 죽어 사거리가 눈에 띄게 짧아진다(감독 지시 "물속에서는 사거리·속도 감소").
        /// </summary>
        private void UpdateFlight(float dt)
        {
            flightTimer += dt;
            if (flightTimer >= MaxFlightSeconds)
            {
                StickToGround(transform.position);
                return;
            }

            if (transform.position.y < waterSurfaceY)
                velocity *= Mathf.Clamp01(1f - UnderwaterDragPerSecond * dt);

            velocity += Vector3.up * (Gravity * dt);

            Vector3 step = velocity * dt;
            float stepLength = step.magnitude;
            if (stepLength < 0.0001f)
            {
                StickToGround(transform.position);
                return;
            }

            Vector3 direction = step / stepLength;
            if (TryFindImpact(direction, stepLength, out RaycastHit hit, out HazardSource hazard, out HuntableCreature creature))
            {
                ResolveImpact(hit, direction, hazard, creature);
                return;
            }

            transform.position += step;
            transform.rotation = SafeLook(direction);
        }

        /// <summary>
        /// 진행 방향을 바라보는 회전. **위/아래로 정확히 수직인 방향을 그대로 넣지 않는다** -
        /// Quaternion.LookRotation은 forward와 upwards가 평행하면 콘솔에 에러를 뱉고 회전을 포기한다
        /// (창을 하늘로 던졌다가 수직으로 떨어지는 순간이 정확히 그 경우다). 그 구간에서만 기준 축을 바꾼다.
        /// </summary>
        private static Quaternion SafeLook(Vector3 forward)
        {
            if (forward.sqrMagnitude < 0.0001f)
                return Quaternion.identity;

            Vector3 dir = forward.normalized;
            Vector3 up = Mathf.Abs(dir.y) > 0.999f ? Vector3.forward : Vector3.up;
            return Quaternion.LookRotation(dir, up);
        }

        /// <summary>
        /// 이번 걸음이 지나갈 선분에서 가장 가까운 "의미 있는" 충돌을 찾는다.
        ///
        /// 통과시키는 것: 던진 사람 자신, 트리거 구역(에어포켓·조타 자리 등 판정용 상자),
        ///               시작 시점에 이미 겹쳐 있던 콜라이더(distance 0 - 스폰 겹침 표준 처리).
        /// 명중으로 치는 것: 살아 있는 HazardSource / 아직 잡히지 않은 HuntableCreature.
        /// 그 밖의 단단한 콜라이더(지형·바위·건축물)는 꽂힐 자리다.
        ///
        /// QueryTriggerInteraction.Collide인 이유: 위험 요소의 콜라이더가 **트리거**다
        /// (HazardSource.OnTriggerEnter로 접촉 피해를 준다). Ignore로 두면 창이 곰을 그대로 통과한다.
        /// </summary>
        private bool TryFindImpact(Vector3 direction, float distance,
            out RaycastHit best, out HazardSource hazard, out HuntableCreature creature)
        {
            best = default(RaycastHit);
            hazard = null;
            creature = null;

            int count = Physics.SphereCastNonAlloc(transform.position, CastRadius, direction, castHits,
                distance + CastSkin, ~0, QueryTriggerInteraction.Collide);

            bool found = false;
            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = castHits[i];
                Collider collider = hit.collider;
                if (collider == null || hit.distance <= 0f)
                    continue;

                Transform hitTransform = collider.transform;
                if (hitTransform == transform || hitTransform.IsChildOf(transform))
                    continue;   // 자기 몸(시각 파츠에는 콜라이더가 없지만 안전하게)

                if (thrower != null && (hitTransform == thrower || hitTransform.IsChildOf(thrower)))
                    continue;   // 던진 사람

                HazardSource hitHazard = collider.GetComponentInParent<HazardSource>();
                HuntableCreature hitCreature = hitHazard == null ? collider.GetComponentInParent<HuntableCreature>() : null;

                bool isTarget = (hitHazard != null && hitHazard.IsActive)
                    || (hitCreature != null && hitCreature.IsAvailable);

                if (!isTarget)
                {
                    // 이미 물리친 위험 요소/잡힌 사냥감은 껍데기다. 트리거 구역도 통과시킨다.
                    if (hitHazard != null || hitCreature != null || collider.isTrigger)
                        continue;
                }

                if (found && hit.distance >= best.distance)
                    continue;

                best = hit;
                hazard = isTarget ? hitHazard : null;
                creature = isTarget ? hitCreature : null;
                found = true;
            }

            return found;
        }

        /// <summary>
        /// 충돌 결과를 적용한다. 대상에 맞았으면 피해를 주고, 어느 쪽이든 창은 그 자리 지면에 꽂힌다
        /// (대상 안에 창이 박힌 채 떠 있으면 회수하러 갈 수가 없다).
        /// 부러진 창(recoverable == false)은 꽂히지 않고 그대로 사라진다.
        /// </summary>
        private void ResolveImpact(RaycastHit hit, Vector3 direction, HazardSource hazard, HuntableCreature creature)
        {
            bool hitTarget = false;

            if (hazard != null)
            {
                hazard.TakeProjectileHit(damage, transform.position, ownerSkills);
                hitTarget = true;
            }
            else if (creature != null)
            {
                creature.TakeProjectileHit(damage, transform.position, ownerInventory, ownerSkills);
                hitTarget = true;
            }

            if (hitTarget)
            {
                // 맞은 대상 발밑으로 떨어뜨린다. SnapToGround는 이름이 "Island_"로 시작하는 지형
                // 콜라이더만 인정하므로(TerrainSampler 주석) 대상의 몸 위에 얹히지 않는다.
                StickToGround(hit.point);
                return;
            }

            // 단단한 것에 꽂혔다. 표면에서 살짝 물러난 지점에 진행 방향 그대로 박힌다.
            transform.position = hit.point - direction * 0.12f;
            transform.rotation = SafeLook(direction);
            EnterStuckState();
        }

        /// <summary>지정 위치의 지면으로 내려 꽂는다(빗나감·시간 초과·대상 명중 공통 경로).</summary>
        private void StickToGround(Vector3 around)
        {
            Vector3 ground = TerrainSampler.SnapToGround(around);
            transform.position = ground + Vector3.up * 0.35f;

            // 땅에 비스듬히 꽂힌 모양. 수평 방향은 날아온 방향을 유지한다.
            Vector3 flat = new Vector3(velocity.x, 0f, velocity.z);
            if (flat.sqrMagnitude < 0.0001f)
                flat = transform.forward;
            flat.y = 0f;
            if (flat.sqrMagnitude < 0.0001f)
                flat = Vector3.forward;

            transform.rotation = SafeLook(flat.normalized + Vector3.down * 1.4f);
            EnterStuckState();
        }

        /// <summary>
        /// 비행을 끝내고 회수 대기 상태로 넘어간다. 회수 불가(부러진 창)면 여기서 그대로 사라진다.
        /// 조준 프롬프트가 이 창을 알아볼 수 있도록 작은 트리거 구체를 이때 붙인다 - 비행 중에는
        /// 콜라이더가 없어야 다른 레이캐스트(조준·건축·지형 스냅)를 한 번도 방해하지 않는다.
        /// </summary>
        private void EnterStuckState()
        {
            velocity = Vector3.zero;
            stuck = true;
            stuckTimer = 0f;
            recoverCheckTimer = 0f;

            if (!recoverable)
            {
                DestroySelf();
                return;
            }

            var probe = gameObject.AddComponent<SphereCollider>();
            probe.isTrigger = true;
            probe.radius = 0.22f;
            probe.center = Vector3.zero;

            AudioManager.Instance?.PlayHit();
        }

        /// <summary>
        /// 꽂힌 뒤의 처리. 주기적으로 플레이어가 가까이 왔는지 보고, 아무도 줍지 않으면 결국 사라진다.
        /// </summary>
        private void UpdateStuck(float dt)
        {
            stuckTimer += dt;
            if (stuckTimer >= DespawnSeconds)
            {
                DestroySelf();
                return;
            }

            recoverCheckTimer -= dt;
            if (recoverCheckTimer > 0f)
                return;
            recoverCheckTimer = RecoverCheckInterval;

            TryRecover();
        }

        /// <summary>
        /// 플레이어가 회수 반경 안에 있으면 가방으로 되돌린다.
        ///
        /// 용량은 <see cref="PlayerInventory.CanAccept"/>로 **먼저** 확인한 뒤
        /// AddItemIgnoringCapacity를 쓴다. 후자는 세이브 복원 전용 API지만, 남은 내구도를 지정해
        /// 넣을 수 있는 유일한 경로다(TryAddItem은 항상 새 것으로 만든다 - InventoryItem 생성자).
        /// 앞에서 용량을 확인하므로 용량 규칙은 그대로 지켜진다.
        /// </summary>
        private bool TryRecover()
        {
            if (!recoverable || weaponData == null || ownerInventory == null)
                return false;

            Vector3 playerPos = ownerInventory.transform.position;
            float sqrDistance = (playerPos - transform.position).sqrMagnitude;
            float radius = Mathf.Max(0.5f, recoverRadius);
            if (sqrDistance > radius * radius)
                return false;

            if (!ownerInventory.CanAccept(weaponData, 1))
                return false;   // 가방이 가득 찼다 - 창은 그 자리에 그대로 남는다

            ownerInventory.AddItemIgnoringCapacity(weaponData, remainingUses);
            AudioManager.Instance?.PlayPickup();
            DestroySelf();
            return true;
        }

        /// <summary>
        /// 이 비행체를 없앤다. Destroy는 프레임 끝까지 지연되므로(AGENT_BRIEF 4장) 물리/조준에서
        /// 즉시 빠지도록 SetActive(false)를 먼저 부른다.
        /// </summary>
        private void DestroySelf()
        {
            active.Remove(this);
            gameObject.SetActive(false);
            Destroy(gameObject);
        }

        /// <summary>파괴 경로가 무엇이든(씬 전환 포함) 정적 목록에 죽은 참조가 남지 않게 한다.</summary>
        private void OnDestroy()
        {
            active.Remove(this);
        }
    }
}
