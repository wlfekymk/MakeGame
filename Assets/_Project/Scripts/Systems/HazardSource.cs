using UnityEngine;
using MakeGame.Data;
using MakeGame.Player;

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

        private bool isDefeated = false;
        private float respawnTimer = 0f;
        private float contactCooldownTimer = 0f;

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
                    // 곰: 맷집이 가장 세다.
                    isCombatTarget = true;
                    maxHealth = 50f;
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

            switch (hazardType)
            {
                case HazardType.VenomousSnake:
                case HazardType.Scorpion:
                    // 독사/전갈: 중독 상태로 만든다.
                    target.ApplyPoison();
                    break;

                case HazardType.Bear:
                case HazardType.Cannibal:
                    // 곰/식인종: 직접 피해 + 출혈을 유발한다.
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

                case HazardType.FoodShortage:
                case HazardType.Dehydration:
                    // 음식 부족/탈수는 SurvivalStats의 허기/갈증 감소 로직에서 이미 처리되므로 별도 효과 없음.
                    break;
            }
        }

        /// <summary>
        /// 인벤토리에서 가장 피해량이 높은 무기(isWeapon)를 찾아 이 위험 요소를 공격한다.
        /// 무기가 없으면 공격할 수 없다. 체력이 0이 되면 물리쳐서 일정 시간 동안 비활성화된다.
        /// </summary>
        public bool TryAttack(PlayerInventory inventory, PlayerSkills skills)
        {
            if (!isCombatTarget || isDefeated || inventory == null)
                return false;

            ItemData bestWeapon = FindBestWeapon(inventory);
            if (bestWeapon == null)
                return false;

            currentHealth = Mathf.Max(0f, currentHealth - bestWeapon.weaponDamage);
            AudioManager.Instance?.PlayHit(); // 공격 적중 효과음

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
        /// 인벤토리에 보유한 무기(isWeapon) 중 피해량이 가장 높은 아이템을 찾는다. 없으면 null.
        /// </summary>
        private ItemData FindBestWeapon(PlayerInventory inventory)
        {
            ItemData best = null;
            foreach (var item in inventory.items)
            {
                if (item.data == null || !item.data.isWeapon)
                    continue;

                if (best == null || item.data.weaponDamage > best.weaponDamage)
                    best = item.data;
            }
            return best;
        }

        /// <summary>
        /// 물리쳐서 비활성화하거나 재등장시킬 때, 시각적으로도 보이지 않도록/보이도록 전환한다.
        /// 콜라이더가 있다면 함께 꺼서 접촉 판정도 멈춘다.
        /// </summary>
        private void SetVisualActive(bool active)
        {
            var renderer = GetComponent<Renderer>();
            if (renderer != null)
                renderer.enabled = active;

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
