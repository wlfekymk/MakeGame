using UnityEngine;

namespace MakeGame.Player
{
    /// <summary>
    /// 플레이어의 생존 수치를 관리한다 (Stranded Deep 기준: 체력/허기/갈증/일사병/중독/출혈/골절).
    /// 허기, 갈증, 일사병은 0~100 범위의 수치이며, 중독/출혈/골절은 발생 여부(플래그)로 관리한다.
    /// </summary>
    public class SurvivalStats : MonoBehaviour
    {
        [Header("체력")]
        [Tooltip("현재 체력 (0이 되면 사망)")]
        public float health = 100f;
        public float maxHealth = 100f;

        [Header("허기 / 갈증")]
        [Tooltip("현재 허기 수치 (0이 되면 체력이 깎이기 시작)")]
        public float hunger = 100f;
        [Tooltip("현재 갈증(수분) 수치 (0이 되면 체력이 깎이기 시작)")]
        public float thirst = 100f;

        [Tooltip("초당 허기 감소량")]
        public float hungerDecayPerSecond = 0.05f;
        [Tooltip("초당 갈증 감소량 (허기보다 빠르게 감소)")]
        public float thirstDecayPerSecond = 0.08f;
        [Tooltip("허기 또는 갈증이 0일 때 초당 입는 피해량")]
        public float starvationDamagePerSecond = 1f;

        [Header("일사병(더위)")]
        [Tooltip("현재 일사병 수치 (100에 도달하면 체력이 깎이기 시작)")]
        public float sunstroke = 0f;
        [Tooltip("햇빛에 노출됐을 때 초당 일사병 증가량")]
        public float sunstrokeGainPerSecond = 0.1f;
        [Tooltip("그늘에 있을 때 초당 일사병 감소량")]
        public float sunstrokeRecoveryPerSecond = 0.2f;
        [Tooltip("일사병이 최대치일 때 초당 입는 피해량")]
        public float sunstrokeDamagePerSecond = 0.5f;

        [Header("상태 이상")]
        [Tooltip("중독 상태 여부 (바다뱀/성게 등). 치료 전까지 체력이 지속적으로 깎인다.")]
        public bool isPoisoned = false;
        [Tooltip("출혈 상태 여부 (상어 등). 붕대 처리 전까지 체력이 지속적으로 깎인다.")]
        public bool isBleeding = false;
        [Tooltip("골절 상태 여부. 치료 전까지 이동 속도가 감소한다.")]
        public bool hasBrokenBone = false;

        [Tooltip("중독 상태일 때 초당 입는 피해량")]
        public float poisonDamagePerSecond = 0.8f;
        [Tooltip("출혈 상태일 때 초당 입는 피해량")]
        public float bleedingDamagePerSecond = 1.2f;

        [Header("체력 자연 회복")]
        [Tooltip("허기/갈증이 모두 이 수치 이상이고 출혈/중독 상태가 아닐 때 체력이 서서히 자연 회복된다.")]
        public float healthRegenThreshold = 50f;
        [Tooltip("자연 회복 조건을 만족할 때 초당 회복되는 체력량")]
        public float healthRegenPerSecond = 0.5f;

        [Header("산소(잠수)")]
        [Tooltip("현재 산소 수치 (잠수 중 감소하며, 0이 되면 익사 피해를 입는다)")]
        public float oxygen = 100f;
        [Tooltip("수면 위/육지에 있을 때 초당 산소 회복량")]
        public float oxygenRecoveryPerSecond = 25f;
        [Tooltip("잠수(수면 아래) 중 초당 산소 감소량")]
        public float oxygenDrainPerSecond = 5f;
        [Tooltip("산소가 고갈된 채로 잠수 중일 때 초당 입는 익사 피해량")]
        public float drowningDamagePerSecond = 3f;

        /// <summary>현재 사망 상태인지 여부.</summary>
        public bool IsDead => health <= 0f;

        /// <summary>
        /// 매 프레임(또는 일정 주기)마다 호출하여 허기/갈증 감소, 상태 이상으로 인한 피해 등
        /// 시간에 따른 생존 수치 변화를 처리한다.
        /// </summary>
        /// <param name="deltaTime">경과 시간(초)</param>
        /// <param name="isInShade">현재 그늘/실내에 있는지 여부 (일사병 회복 여부 판정용)</param>
        /// <param name="isUnderwater">현재 수면 아래(잠수 중)인지 여부 (산소 감소 판정용)</param>
        public void Tick(float deltaTime, bool isInShade, bool isUnderwater = false)
        {
            if (IsDead)
                return;

            UpdateHungerAndThirst(deltaTime);
            UpdateSunstroke(deltaTime, isInShade);
            UpdateStatusEffectDamage(deltaTime);
            UpdateOxygen(deltaTime, isUnderwater);
            UpdateHealthRegen(deltaTime);
        }

        /// <summary>
        /// 허기/갈증이 충분히 채워져 있고 출혈/중독처럼 지속 피해를 주는 상태 이상이 없을 때
        /// 체력을 서서히 자연 회복시킨다 (Stranded Deep처럼 생존 수치를 잘 관리하면 서서히 회복).
        /// 골절은 체력에 직접 피해를 주지 않으므로 회복 조건에서는 제외한다.
        /// </summary>
        private void UpdateHealthRegen(float deltaTime)
        {
            bool needsMet = hunger >= healthRegenThreshold && thirst >= healthRegenThreshold;
            bool freeOfDamagingEffects = !isPoisoned && !isBleeding;

            if (needsMet && freeOfDamagingEffects)
                Heal(healthRegenPerSecond * deltaTime);
        }

        /// <summary>
        /// 잠수 여부에 따라 산소 수치를 증감시키고, 산소가 고갈된 채로 잠수 중이면 익사 피해를 입힌다.
        /// </summary>
        private void UpdateOxygen(float deltaTime, bool isUnderwater)
        {
            if (isUnderwater)
                oxygen = Mathf.Max(0f, oxygen - oxygenDrainPerSecond * deltaTime);
            else
                oxygen = Mathf.Min(100f, oxygen + oxygenRecoveryPerSecond * deltaTime);

            if (isUnderwater && oxygen <= 0f)
                TakeDamage(drowningDamagePerSecond * deltaTime);
        }

        /// <summary>
        /// 허기와 갈증을 시간에 따라 감소시키고, 둘 중 하나라도 0이면 체력을 깎는다.
        /// </summary>
        private void UpdateHungerAndThirst(float deltaTime)
        {
            hunger = Mathf.Max(0f, hunger - hungerDecayPerSecond * deltaTime);
            thirst = Mathf.Max(0f, thirst - thirstDecayPerSecond * deltaTime);

            if (hunger <= 0f || thirst <= 0f)
                TakeDamage(starvationDamagePerSecond * deltaTime);
        }

        /// <summary>
        /// 그늘 여부에 따라 일사병 수치를 증감시키고, 최대치에 도달하면 체력을 깎는다.
        /// </summary>
        private void UpdateSunstroke(float deltaTime, bool isInShade)
        {
            if (isInShade)
                sunstroke = Mathf.Max(0f, sunstroke - sunstrokeRecoveryPerSecond * deltaTime);
            else
                sunstroke = Mathf.Min(100f, sunstroke + sunstrokeGainPerSecond * deltaTime);

            if (sunstroke >= 100f)
                TakeDamage(sunstrokeDamagePerSecond * deltaTime);
        }

        /// <summary>
        /// 중독/출혈 상태 이상에 의한 지속 피해를 처리한다.
        /// </summary>
        private void UpdateStatusEffectDamage(float deltaTime)
        {
            if (isPoisoned)
                TakeDamage(poisonDamagePerSecond * deltaTime);

            if (isBleeding)
                TakeDamage(bleedingDamagePerSecond * deltaTime);
        }

        /// <summary>
        /// 지정한 양만큼 체력을 감소시킨다. 0 미만으로는 내려가지 않는다.
        /// </summary>
        public void TakeDamage(float amount)
        {
            health = Mathf.Max(0f, health - amount);
        }

        /// <summary>
        /// 지정한 양만큼 체력을 회복시킨다. 최대 체력을 넘지 않는다.
        /// </summary>
        public void Heal(float amount)
        {
            health = Mathf.Min(maxHealth, health + amount);
        }

        /// <summary>
        /// 음식을 섭취하여 허기 수치를 회복한다. 최대 100을 넘지 않는다.
        /// </summary>
        public void ConsumeFood(float amount)
        {
            hunger = Mathf.Min(100f, hunger + amount);
        }

        /// <summary>
        /// 물을 섭취하여 갈증 수치를 회복한다. 최대 100을 넘지 않는다.
        /// </summary>
        public void ConsumeWater(float amount)
        {
            thirst = Mathf.Min(100f, thirst + amount);
        }

        /// <summary>
        /// 코코넛 워터처럼 과음 시 부작용이 있는 수분 공급원을 섭취한다.
        /// 갈증은 회복되지만, 과음(threshold 초과) 시 설사로 인해 갈증이 급격히 다시 악화된다.
        /// </summary>
        /// <param name="amount">회복시킬 갈증 수치</param>
        /// <param name="overdoseThreshold">과음으로 판정되는 1회 섭취량 기준치</param>
        public void ConsumeCoconutWater(float amount, float overdoseThreshold = 40f)
        {
            ConsumeWater(amount);

            if (amount > overdoseThreshold)
            {
                // 과음 시 설사로 인해 갈증이 절반 수준으로 급격히 악화된다.
                thirst = Mathf.Max(0f, thirst * 0.5f);
            }
        }

        /// <summary>
        /// 중독 상태로 만든다 (바다뱀/성게 등에게 공격받았을 때 호출).
        /// </summary>
        public void ApplyPoison()
        {
            isPoisoned = true;
        }

        /// <summary>
        /// 중독을 치료한다.
        /// </summary>
        public void CurePoison()
        {
            isPoisoned = false;
        }

        /// <summary>
        /// 출혈 상태로 만든다 (상어 등에게 공격받았을 때 호출).
        /// </summary>
        public void ApplyBleeding()
        {
            isBleeding = true;
        }

        /// <summary>
        /// 붕대를 감아 출혈을 멈춘다.
        /// </summary>
        public void BandageBleeding()
        {
            isBleeding = false;
        }

        /// <summary>
        /// 골절 상태로 만든다 (낙상 등으로 인한 부상 발생 시 호출).
        /// </summary>
        public void ApplyBrokenBone()
        {
            hasBrokenBone = true;
        }

        /// <summary>
        /// 부목/치료를 통해 골절을 치료한다.
        /// </summary>
        public void HealBrokenBone()
        {
            hasBrokenBone = false;
        }
    }
}
