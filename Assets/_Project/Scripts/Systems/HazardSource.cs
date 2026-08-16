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
        /// 매 프레임 접촉 쿨다운과 물리친 뒤의 재등장 타이머를 진행시킨다.
        /// </summary>
        private void Update()
        {
            if (contactCooldownTimer > 0f)
                contactCooldownTimer -= Time.deltaTime;

            // 벌떼가 살아 있는 동안에만 무리가 술렁인다(물리친 뒤에는 보이지도 않는다).
            if (hazardType == HazardType.BeeSwarm && !isDefeated)
                transform.Rotate(BeeSwarmSpinAxis, BeeSwarmSpinDegreesPerSecond * Time.unscaledDeltaTime, Space.Self);

            if (!isDefeated)
                return;

            respawnTimer += Time.deltaTime;
            if (respawnTimer >= respawnSeconds)
            {
                isDefeated = false;
                currentHealth = maxHealth;
                respawnTimer = 0f;
                SetVisualActive(true);
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

            // 내구도 소모: 무제한(IsUnlimited) 무기는 자동으로 소모되지 않는다. 사용 횟수가 다하면
            // UseItem이 인벤토리에서 자동으로 제거해 "무기가 파손되었다"를 자연스럽게 표현한다.
            inventory.UseItem(bestWeaponItem);

            if (currentHealth <= 0f)
            {
                isDefeated = true;
                respawnTimer = 0f;
                SetVisualActive(false);

                if (skills != null)
                    skills.AddExperience(SkillType.Physical, defeatExperience);
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
        }
    }
}
