using UnityEngine;
using MakeGame.Data;

namespace MakeGame.Player
{
    /// <summary>
    /// 사망/피해의 원인. 게임 오버 화면에서 원인별로 다른 안내 문구를 보여주기 위해 기록한다
    /// (deathCauseVariety 참고 - 예전에는 원인과 무관하게 "생존하지 못했습니다" 한 문장만 표시했다).
    /// </summary>
    public enum DamageCause
    {
        Unknown,
        Starvation,
        Sunstroke,
        Poison,
        Bleeding,
        Drowning,
        Predator,
        SharkAttack, // 수영/잠수 중 상어 습격 (Predator와 분리 - 육지 포식자와 다른 사망 문구를 보여주기 위함)
    }

    /// <summary>
    /// 플레이어의 생존 수치를 관리한다 (Stranded Deep 기준: 체력/허기/갈증/일사병/중독/출혈/골절).
    /// 허기, 갈증, 일사병은 0~100 범위의 수치이며, 중독/출혈/골절은 발생 여부(플래그)로 관리한다.
    /// </summary>
    public class SurvivalStats : MonoBehaviour
    {
        // B3-11 (2단계 배선, Spec_15 5단계 절차 중 3단계): SurvivalBalanceConfig를 선택적(nullable)
        // 참조로 추가한다. 핵심 원칙(코디네이터 지시) - "씬 직렬화 값이 항상 우선이고, config는 값이
        // 미설정일 때의 단일 소스다" - 이는 GetMultiplier/GetScatterRadius(HazardSpawner/
        // IslandResourceSpawner)에서 이미 쓰던 "필드값이 0 이하면 폴백"과 완전히 동일한 패턴이다.
        // balanceConfig가 비어 있으면(에셋이 아직 없거나 연결 안 됨) ApplyBalanceConfigFallback이
        // 아무 일도 하지 않으므로 기존 동작이 100% 그대로 유지된다.
        [Header("밸런스 config (선택, B3-11)")]
        [Tooltip("연결하면, 아래 각 수치 필드가 0 이하로(미설정) 남아있는 경우에 한해 이 config의 값을 대신 쓴다." +
            " 씬/프리팹에 이미 의미 있는(양수) 값이 직렬화돼 있으면 이 config는 절대 그 값을 덮어쓰지 않는다.")]
        public SurvivalBalanceConfig balanceConfig;

        /// <summary>
        /// Awake 시점에 balanceConfig가 연결돼 있으면, 아직 의미 있게 설정되지 않은(0 이하) 밸런스
        /// 필드에 한해 config 값을 채운다. 씬에 이미 양수 값이 직렬화된 필드는 절대 건드리지 않는다 -
        /// "긴급 정정(#2 회귀 수정)"에서 확립된, 씬 실측값을 최우선하는 원칙을 그대로 따른다.
        /// </summary>
        private void Awake()
        {
            ApplyBalanceConfigFallback();
        }

        /// <summary>
        /// balanceConfig가 있을 때, 0 이하로 남아있는(=미설정) 필드만 골라 config 값으로 채운다.
        /// hungerDecayPerSecond 등은 정상적인 밸런스 값이라면 0이 될 일이 없으므로(0이면 그 효과가
        /// 완전히 꺼지는 것과 같다), 0 이하를 "아직 설정되지 않음"의 안전한 신호로 삼는다 - 이 프로젝트
        /// 전역에서 이미 쓰고 있는 관례(GetMultiplier/GetScatterRadius)와 동일한 판단 기준이다.
        /// </summary>
        private void ApplyBalanceConfigFallback()
        {
            // B4-2: 인스펙터에서 연결되지 않았으면 Resources의 공용 에셋을 자동으로 집는다.
            // 런타임 생성 컴포넌트(WeatherSystem/Campfire/WaterStill 등)는 인스펙터 연결 수단이
            // 아예 없어서, 이 경로가 없으면 balanceConfig가 영원히 null로 남는다.
            if (balanceConfig == null)
                balanceConfig = SurvivalBalanceConfig.Active;
            if (balanceConfig == null)
                return;

            if (hungerDecayPerSecond <= 0f) hungerDecayPerSecond = balanceConfig.hungerDecayPerSecond;
            if (thirstDecayPerSecond <= 0f) thirstDecayPerSecond = balanceConfig.thirstDecayPerSecond;
            if (starvationDamagePerSecond <= 0f) starvationDamagePerSecond = balanceConfig.starvationDamagePerSecond;

            if (sunstrokeGainPerSecond <= 0f) sunstrokeGainPerSecond = balanceConfig.sunstrokeGainPerSecond;
            if (sunstrokeRecoveryPerSecond <= 0f) sunstrokeRecoveryPerSecond = balanceConfig.sunstrokeRecoveryPerSecond;
            if (sunstrokeDamagePerSecond <= 0f) sunstrokeDamagePerSecond = balanceConfig.sunstrokeDamagePerSecond;

            if (poisonDamagePerSecond <= 0f) poisonDamagePerSecond = balanceConfig.poisonDamagePerSecond;
            if (bleedingDamagePerSecond <= 0f) bleedingDamagePerSecond = balanceConfig.bleedingDamagePerSecond;

            if (healthRegenThreshold <= 0f) healthRegenThreshold = balanceConfig.healthRegenThreshold;
            if (healthRegenPerSecond <= 0f) healthRegenPerSecond = balanceConfig.healthRegenPerSecond;

            if (oxygenRecoveryPerSecond <= 0f) oxygenRecoveryPerSecond = balanceConfig.oxygenRecoveryPerSecond;
            if (oxygenDrainPerSecond <= 0f) oxygenDrainPerSecond = balanceConfig.oxygenDrainPerSecond;
            if (drowningDamagePerSecond <= 0f) drowningDamagePerSecond = balanceConfig.drowningDamagePerSecond;

            // [죽은 config 값 배선 (A)] 임계치 0 이하 = "과음이 항상 성립"이라는 뜻이 되어 밸런스 값으로
            // 성립하지 않으므로, 이 파일의 다른 12줄과 똑같이 0 이하를 미설정 신호로 쓴다.
            if (coconutOverdoseThreshold <= 0f) coconutOverdoseThreshold = balanceConfig.coconutOverdoseThreshold;
        }

        // 추가 작업(#10 준비): SurvivalHudUI가 위험 경고(빨간 경고색 깜빡임)를 띄우는 임계값(0.25f/0.2f/0.8f)을
        // UI 쪽에 하드코딩해서 쓰고 있어, 이 시스템의 밸런스(감소/회복 속도 등)가 바뀌어도 UI 경고 시점은
        // 조용히 안 맞게 어긋날 위험이 있었다. 게임 규칙에 속하는 값이므로 SurvivalStats가 단일 소스로
        // 노출하고, UI는 이 값을 참조하도록 한다(UI 쪽 교체는 ui-engineer가 다음 배치에서 진행 - 여기서는
        // 노출만 하며, 값은 SurvivalHudUI가 현재 쓰는 것과 완전히 동일하다).
        // (const 필드는 Unity Inspector에 노출되지 않으므로 [Header]를 붙이지 않는다.)
        /// <summary>체력 비율이 이 값 미만이면 위험 경고를 표시한다 (SurvivalHudUI 기준값과 동일).</summary>
        public const float LowHealthRatio = 0.25f;
        /// <summary>허기 비율이 이 값 미만이면 위험 경고를 표시한다.</summary>
        public const float LowHungerRatio = 0.2f;
        /// <summary>갈증 비율이 이 값 미만이면 위험 경고를 표시한다.</summary>
        public const float LowThirstRatio = 0.2f;
        /// <summary>일사병 비율이 이 값을 초과하면 위험 경고를 표시한다.</summary>
        public const float HighSunstrokeRatio = 0.8f;
        /// <summary>산소 비율이 이 값 미만이면 위험 경고를 표시한다.</summary>
        public const float LowOxygenRatio = 0.25f;

        // 추가 작업(B2-9): SurvivalHudUI가 허기/갈증/일사병/산소의 최대치 100을 UI 쪽에서 직접 알고
        // 있다가 자체적으로 나눠 써서(비율 계산), 이 시스템의 최대치가 바뀌면 UI가 조용히 어긋날 위험이
        // 있었다. LowHealthRatio 등과 동일한 이유로 게임 규칙에 속하는 값이므로 SurvivalStats가 단일
        // 소스로 노출한다(UI 쪽 교체는 ui-engineer가 진행). 아래 허기/갈증/일사병/산소 상한 계산도
        // 이 상수를 참조하도록 함께 정리했다(반환값은 기존과 동일하게 100).
        /// <summary>허기/갈증/일사병/산소 수치의 최대값(공통 상한, 0~100 스케일).</summary>
        public const float MaxStatValue = 100f;

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

        // [죽은 config 값 배선 (A)] SurvivalBalanceConfig.coconutOverdoseThreshold(=40)는 에셋에 값이
        // 있는데 이 프로젝트의 어느 코드도 읽지 않는 죽은 값이었다. 과음 임계치가 ConsumeCoconutWater의
        // 기본 인자(40f)에만 하드코딩돼 있어서, config를 고쳐도 게임은 40으로 굴러갔다.
        // 지금 배선하는 이유: config 값(40)과 실효값(40)이 아직 같아서 회귀 위험이 0이다 - 둘이 갈라진
        // 뒤에 배선하면 그 순간이 곧 밸런스 변경이 된다.
        [Header("코코넛워터 과음")]
        [Tooltip("코코넛 워터를 1회에 이 수치보다 많이 마시면 과음으로 판정되어 설사로 갈증이 절반으로 떨어진다.")]
        public float coconutOverdoseThreshold = 40f;

        /// <summary>현재 사망 상태인지 여부.</summary>
        public bool IsDead => health <= 0f;

        /// <summary>
        /// 체력을 마지막으로 깎은 원인. TakeDamage가 호출될 때마다 갱신되며, 사망 시점에는
        /// "무엇 때문에 죽었는지"를 나타내므로 게임 오버 화면에서 원인별 문구를 고르는 데 쓴다.
        /// </summary>
        public DamageCause lastDamageCause = DamageCause.Unknown;

        /// <summary>
        /// 매 프레임(또는 일정 주기)마다 호출하여 허기/갈증 감소, 상태 이상으로 인한 피해 등
        /// 시간에 따른 생존 수치 변화를 처리한다.
        /// </summary>
        /// <param name="deltaTime">경과 시간(초)</param>
        /// <param name="isInShade">현재 그늘/실내에 있는지 여부 (일사병 회복 여부 판정용)</param>
        /// <param name="isUnderwater">현재 수면 아래(잠수 중)인지 여부 (산소 감소 판정용)</param>
        /// <param name="isDaytime">현재 태양이 떠 있는 낮 시간대인지 여부 (일사병 증가 판정용, 기본값 true는
        /// 밤/낮 주기가 없는 호출부와의 하위 호환을 위함 - 항상 낮인 것처럼 취급해 기존 동작을 유지한다)</param>
        public void Tick(float deltaTime, bool isInShade, bool isUnderwater = false, bool isDaytime = true)
        {
            if (IsDead)
                return;

            UpdateHungerAndThirst(deltaTime);
            UpdateSunstroke(deltaTime, isInShade, isDaytime);
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
                oxygen = Mathf.Min(MaxStatValue, oxygen + oxygenRecoveryPerSecond * deltaTime);

            if (isUnderwater && oxygen <= 0f)
                TakeDamage(drowningDamagePerSecond * deltaTime, DamageCause.Drowning);
        }

        /// <summary>
        /// 허기와 갈증을 시간에 따라 감소시키고, 둘 중 하나라도 0이면 체력을 깎는다.
        /// </summary>
        private void UpdateHungerAndThirst(float deltaTime)
        {
            hunger = Mathf.Max(0f, hunger - hungerDecayPerSecond * deltaTime);
            thirst = Mathf.Max(0f, thirst - thirstDecayPerSecond * deltaTime);

            if (hunger <= 0f || thirst <= 0f)
                TakeDamage(starvationDamagePerSecond * deltaTime, DamageCause.Starvation);
        }

        /// <summary>
        /// 그늘 여부와 낮/밤 시간대에 따라 일사병 수치를 증감시키고, 최대치에 도달하면 체력을 깎는다.
        /// 버그 수정: 예전에는 밤/낮 주기 자체가 없어 그늘 여부만으로 판정했는데, DayNightCycle
        /// 도입 이후에도 이 로직을 그대로 두면 한밤중 뙤약볕 아래 있는 것도 아닌데 일사병이 계속
        /// 오르는 모순이 생긴다. 그늘에 있으면 낮/밤과 무관하게 항상 회복되고, 그늘 밖이라도 밤에는
        /// 햇빛이 없으므로 낮과 같은 속도로(태양 없이 식는다는 의미로) 회복되며, 오직 '그늘 밖 + 낮'
        /// 조합에서만 실제로 증가한다.
        /// </summary>
        private void UpdateSunstroke(float deltaTime, bool isInShade, bool isDaytime)
        {
            if (isInShade || !isDaytime)
                sunstroke = Mathf.Max(0f, sunstroke - sunstrokeRecoveryPerSecond * deltaTime);
            else
                sunstroke = Mathf.Min(MaxStatValue, sunstroke + sunstrokeGainPerSecond * deltaTime);

            if (sunstroke >= MaxStatValue)
                TakeDamage(sunstrokeDamagePerSecond * deltaTime, DamageCause.Sunstroke);
        }

        /// <summary>
        /// 중독/출혈 상태 이상에 의한 지속 피해를 처리한다.
        /// </summary>
        private void UpdateStatusEffectDamage(float deltaTime)
        {
            if (isPoisoned)
                TakeDamage(poisonDamagePerSecond * deltaTime, DamageCause.Poison);

            if (isBleeding)
                TakeDamage(bleedingDamagePerSecond * deltaTime, DamageCause.Bleeding);
        }

        /// <summary>
        /// 지정한 양만큼 체력을 감소시킨다. 0 미만으로는 내려가지 않는다.
        /// cause를 지정하면 lastDamageCause에 기록해, 사망 시 게임 오버 화면이 원인별 문구를 고를 수 있게 한다.
        /// 여러 원인이 같은 프레임에 겹치면(예: 허기 0 + 중독 동시) 가장 마지막에 호출된 TakeDamage의 원인이
        /// 남는다 - 완벽하진 않지만 사망 직전 상황을 대략적으로 알려주는 용도로는 충분하다.
        /// </summary>
        public void TakeDamage(float amount, DamageCause cause = DamageCause.Unknown)
        {
            health = Mathf.Max(0f, health - amount);

            if (amount > 0f && cause != DamageCause.Unknown)
                lastDamageCause = cause;
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
            hunger = Mathf.Min(MaxStatValue, hunger + amount);
        }

        /// <summary>
        /// 물을 섭취하여 갈증 수치를 회복한다. 최대 100을 넘지 않는다.
        /// </summary>
        public void ConsumeWater(float amount)
        {
            thirst = Mathf.Min(MaxStatValue, thirst + amount);
        }

        /// <summary>
        /// 코코넛 워터처럼 과음 시 부작용이 있는 수분 공급원을 섭취한다.
        /// 갈증은 회복되지만, 과음(threshold 초과) 시 설사로 인해 갈증이 급격히 다시 악화된다.
        /// </summary>
        /// <param name="amount">회복시킬 갈증 수치</param>
        /// <param name="overdoseThreshold">과음으로 판정되는 1회 섭취량 기준치.
        /// 음수(기본값 -1)로 두면 이 컴포넌트의 coconutOverdoseThreshold 필드를 쓴다 - 그 필드는
        /// ApplyBalanceConfigFallback을 통해 SurvivalBalanceConfig와 연결돼 있다.
        /// 예전 기본값 40f는 config를 무시하는 하드코딩이었고, 지금은 필드 기본값도 40f라 호출부
        /// (ConsumptionSystem.Consume - 인자를 넘기지 않는 유일한 호출부)의 동작이 완전히 동일하다.</param>
        public void ConsumeCoconutWater(float amount, float overdoseThreshold = -1f)
        {
            ConsumeWater(amount);

            // 0도 유효한 임계치("아무리 조금 마셔도 과음")로 보고, 미지정은 음수로만 판정한다.
            float threshold = overdoseThreshold >= 0f ? overdoseThreshold : coconutOverdoseThreshold;

            if (amount > threshold)
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
