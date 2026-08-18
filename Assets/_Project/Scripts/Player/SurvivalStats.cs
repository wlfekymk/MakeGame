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

        /// <summary>
        /// 산소 감소 속도에 곱해지는 런타임 배율. 산소통(소지 패시브 장비)을 들고 있으면
        /// PlayerController가 인벤토리 변경 이벤트 시점에 0.5로 낮춰 산소 지속시간을 2배로 만든다
        /// (최대치는 MaxStatValue가 허기/갈증/일사병과 공유하는 공통 상한이라 최대치 2배 쪽은
        /// 택하지 않았다 - HUD 비율 계산까지 전부 이 상수를 참조한다).
        /// 직렬화하지 않는 이유: 이 값은 밸런스 설정이 아니라 "지금 산소통을 들고 있는가"라는
        /// 파생 상태다. 세이브는 인벤토리(이름 키)만 저장하면 복원 시 InventoryChanged 경로로
        /// 자연히 다시 계산되므로, 씬/세이브 포맷 모두 건드리지 않는다. 배율 값 자체(0.5)는
        /// PlayerController.oxygenTankDrainMultiplier가 단일 소스다.
        /// 0 이하는 이 파일의 다른 필드와 같은 관례로 '미설정'으로 보고 1로 취급한다.
        /// </summary>
        [System.NonSerialized]
        public float oxygenDrainMultiplier = 1f;

        /// <summary>
        /// 플레이어의 머리(카메라 위치)가 에어포켓(AirPocketZone) 안에 있는가. oxygenDrainMultiplier와
        /// 같은 패턴으로 PlayerController가 매 프레임 밀어주는 파생 상태다 — SurvivalStats는 존의
        /// 존재를 모른 채 bool 하나만 읽는다. 직렬화하지 않는 이유도 위와 동일: 매 프레임 다시
        /// 계산되는 순간 상태라 씬/세이브 포맷 어느 쪽에도 실을 필요가 없다.
        /// </summary>
        [System.NonSerialized]
        public bool isHeadInAirPocket = false;

        [Tooltip("에어포켓 안에서 잠수 중일 때 초당 산소 회복량 = 산소 감소량(oxygenDrainPerSecond) x 이 배율.\n" +
            "수면 위 회복(oxygenRecoveryPerSecond)보다는 느리게, 감소보다는 확실히 빠르게 차오른다.")]
        public float airPocketRecoveryMultiplier = 3f;
        [Tooltip("산소가 고갈된 채로 잠수 중일 때 초당 입는 익사 피해량")]
        public float drowningDamagePerSecond = 3f;

        // [죽은 config 값 배선 (A)] SurvivalBalanceConfig.coconutOverdoseThreshold(=40)는 에셋에 값이
        // 있는데 이 프로젝트의 어느 코드도 읽지 않는 죽은 값이었다. 과음 임계치가 ConsumeCoconutWater의
        // 기본 인자(40f)에만 하드코딩돼 있어서, config를 고쳐도 게임은 40으로 굴러갔다.
        // 지금 배선하는 이유: config 값(40)과 실효값(40)이 아직 같아서 회귀 위험이 0이다 - 둘이 갈라진
        // 뒤에 배선하면 그 순간이 곧 밸런스 변경이 된다.
        // ⚠ 위 문단은 규칙 재설계 이전의 기록이다. **지금은 config의 40이 옛 의미(1회 섭취량)를
        //   담고 있어 새 의미(마신 뒤 갈증 합계)와 맞지 않는다.** 아래 필드 기본값이 100(양수)이라
        //   폴백(<=0일 때만)이 실행되지 않아 당장은 아무 영향이 없지만, 에셋 값을 100으로 고쳐야
        //   한다(디렉터 요청 올림 - .asset은 이 역할이 편집할 수 없다).
        // [규칙 재설계 - game-designer 실측] 위 배선은 값만 이었고 **규칙 자체가 죽어 있었다.**
        // 판정이 `amount > coconutOverdoseThreshold`(= 1회 섭취량)인데 코코넛의 1회 섭취량은
        // Item_코코넛.asset의 thirstRestoreAmount 30으로 고정이고 임계치는 40이라 30 > 40은 영원히
        // 거짓이다. 코코넛 워터 아이템은 이 하나뿐이므로(isCoconutWaterSource: 1인 에셋 전수 = 1개)
        // 값을 바꿔도 살아나지 않는다 - 임계치를 30 미만으로 내리면 "모든 섭취가 과음"이 되어
        // 규칙이 반대편으로 죽는다.
        //
        // 그래서 "얼마나 많이 마셨나"가 아니라 **"배부른데 또 마셨나"**로 판정을 바꿨다. 이 필드는
        // 이제 1회 섭취량이 아니라 **마신 뒤의 갈증 합계**와 비교되며, 기본값도 그 의미에 맞는
        // MaxStatValue(100)다. 필드/타입/이름은 그대로라 씬·프리팹 직렬화에는 영향이 없다
        // (SampleScene의 SurvivalStats 블록 :684-708에 이 키 자체가 없고, SurvivalStats를 가진
        // 프리팹도 0개 - 전수 확인함. 따라서 실효값은 코드 기본값 100이 된다).
        //
        // 왜 이 안이 가장 온건한가: (1) 페널티가 연속적이다. 임계치 바로 아래에서 초과분 0으로
        // 시작하므로 예전의 "갈증 절반" 같은 절벽이 없다. (2) 갈증 70 이하(= 100 - 30)에서 마시면
        // 페널티가 **아예 걸리지 않는다** - 정상적으로 목마를 때 마시는 플레이는 지금과 100% 동일하다.
        // (3) 손익분기가 85다(85에서 마시면 100 - 15 = 85로 제자리). 즉 손해를 보려면 갈증 85 이상,
        // 사실상 "마실 이유가 없을 때" 마셔야 한다. 최대 손실은 초과분 상한인 30(갈증 100에서 섭취).
        [Header("코코넛워터 과음")]
        [Tooltip("코코넛 워터를 마신 뒤의 갈증 합계가 이 수치를 넘으면 과음으로 판정되어, 넘친 만큼\n" +
            "설사로 갈증을 다시 잃는다(초과분 = 마신 뒤 합계 - 이 수치). 기본값 100 = 최대치를\n" +
            "넘겨 마실 때만 손해. 0 이하는 이 파일의 다른 필드와 마찬가지로 '미설정'으로 보고\n" +
            "config 값으로 채운다 - 조건을 세게 걸고 싶으면 0이 아니라 작은 양수를 넣을 것.")]
        public float coconutOverdoseThreshold = MaxStatValue;

        // ── QA 치트 플래그 (개발 빌드 전용) ──────────────────────────────────────────────────
        //
        // 감독 요청: "디버그 모드에 생명, 일사병, 공기 무제한 설정을 넣어줘. 테스트를 하려니깐 힘드네"
        // = 테스트 중 죽지 않고 자유롭게 돌아다니기 위한 도구다.
        //
        // **출시 빌드 격리 방식(#if로 필드와 검사 코드를 통째로 뺀다).** DebugHud의 결말 미리보기/
        // 재료 지급 키가 쓰는 것과 같은 심볼이다. "플래그는 항상 두고 분기만 남긴다"도 가능했지만
        // 이쪽을 택한 이유:
        //   1. 출시 빌드의 IL이 이 작업 전과 **완전히 동일**해진다(분기 한 줄도 남지 않는다).
        //      "치트 플래그가 꺼진 상태에서 기존 동작이 비트 단위로 동일한가"를 증명할 필요조차 없어진다.
        //   2. 나중에 누가 #if 밖에서 이 플래그를 읽으면 **릴리스 빌드가 컴파일 에러로 시끄럽게
        //      실패한다.** 조용히 치트 경로가 출시본에 실려 나가는 것보다 훨씬 낫다.
        //   3. 이 파일과 DebugHud.cs는 같은 Assembly-CSharp이라 두 곳의 #if 결과가 항상 일치한다.
        // 플래그를 true로 만드는 경로는 DebugHud(같은 #if 안)에만 있다. 이 파일은 읽기만 한다.
        //
        // [System.NonSerialized]인 이유: oxygenDrainMultiplier/isHeadInAirPocket과 같다. 세이브 포맷
        // (SaveData)에도, 씬/프리팹 직렬화에도 절대 남기지 않는다 - 치트가 세이브에 굳어 버리면
        // "왜 안 죽지"가 다음 세션까지 따라간다.
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        /// <summary>체력이 줄지 않는다(항상 maxHealth). TakeDamage 전 경로 + Tick 고정으로 막는다.</summary>
        [System.NonSerialized] public bool debugInfiniteHealth = false;

        /// <summary>일사병 수치가 오르지 않는다(항상 0).</summary>
        [System.NonSerialized] public bool debugNoHeatstroke = false;

        /// <summary>산소가 줄지 않는다(항상 최대치, 익사 피해 없음).</summary>
        [System.NonSerialized] public bool debugInfiniteOxygen = false;

        /// <summary>허기·갈증이 줄지 않는다(현재 값에서 정지). 감독이 명시 요청하진 않았지만 같은 기구다.</summary>
        [System.NonSerialized] public bool debugNoHungerThirst = false;

        /// <summary>치트가 하나라도 켜져 있는지. DebugHud의 상태 표기용.</summary>
        public bool AnyDebugCheatActive =>
            debugInfiniteHealth || debugNoHeatstroke || debugInfiniteOxygen || debugNoHungerThirst;

        /// <summary>
        /// 켜져 있는 치트가 요구하는 "고정값"을 지금 값에 반영한다. 각 Update* 메서드가 감소/증가
        /// 지점 자체를 건너뛰지만, **플래그를 켠 순간 이미 깎여 있던 값**은 그 분기만으로는 돌아오지
        /// 않는다(예: 체력 12에서 무적을 켜면 12에 멈춘 채 "무제한"이 된다). 여기서 한 번 끌어올린다.
        ///
        /// 허기·갈증은 일부러 끌어올리지 않는다. 요구는 "줄지 않음"(정지)이고, 최대치로 밀어 버리면
        /// 저허기 HUD 경고처럼 **테스터가 일부러 만들어 둔 상태를 지워 버린다.** 대신 굶주림 피해는
        /// UpdateHungerAndThirst에서 함께 막아, 허기 0에서 켰을 때 "줄지도 않는데 계속 아픈" 모순이
        /// 생기지 않게 했다.
        /// </summary>
        private void ApplyDebugCheatClamp()
        {
            if (debugInfiniteHealth && health < maxHealth)
                health = maxHealth;

            if (debugNoHeatstroke && sunstroke > 0f)
                sunstroke = 0f;

            if (debugInfiniteOxygen && oxygen < MaxStatValue)
                oxygen = MaxStatValue;
        }
#endif

        /// <summary>현재 사망 상태인지 여부.</summary>
        public bool IsDead => health <= 0f;

        /// <summary>
        /// 체력을 마지막으로 깎은 원인. TakeDamage가 호출될 때마다 갱신되며, 사망 시점에는
        /// "무엇 때문에 죽었는지"를 나타내므로 게임 오버 화면에서 원인별 문구를 고르는 데 쓴다.
        /// </summary>
        public DamageCause lastDamageCause = DamageCause.Unknown;

        // ── 겪은 위기 횟수 (Design_Ending.md 4장, 엔딩/사망 화면 통계용) ──────────────────────────
        //
        // 설계 문서의 최초 제안은 "lastDamageCause가 Unknown → 유효값으로 바뀌는 전환을 센다"였는데,
        // 실제 코드를 읽고 그 정의를 쓰지 않기로 했다. lastDamageCause는 한 번 유효값이 되면 다시
        // Unknown으로 돌아가는 코드가 어디에도 없다(TakeDamage에서 단조적으로 덮어쓸 뿐이다).
        // 그 정의를 그대로 구현하면 이 카운터는 한 세션에서 **영원히 1**이 된다 - 통계 칸이 항상
        // "1회"로 고정되므로 없느니만 못하다.
        //
        // 대안으로 "원인이 바뀔 때마다 +1"도 검토했다가 버렸다. 중독과 출혈이 동시에 걸려 있으면
        // UpdateStatusEffectDamage가 한 프레임 안에 Poison→Bleeding을 번갈아 호출해서, 초당 120회씩
        // 카운터가 오른다.
        //
        // 채택한 정의: **위기 = 원인별 "에피소드"**. 어떤 원인의 피해가 graceSeconds 이상 끊긴 뒤
        // 다시 들어오면 그때 1회로 센다. 원인마다 마지막 피격 시각을 따로 들고 있으므로
        //   · 같은 곰에게 연속으로 맞으면 → **1회**. (질문에 대한 답: 타격 수가 아니라 사건 수를 센다.
        //     "곰에게 습격당했다"는 한 번의 위기이지 3번의 위기가 아니다. 지속 피해(굶주림/중독)는
        //     매 프레임 TakeDamage가 불리므로 타격 수를 세는 정의는 애초에 성립하지도 않는다.)
        //   · 곰에게 맞다가 중독까지 되면 → 2회 (서로 다른 원인 = 서로 다른 위기).
        //   · 굶주림으로 깎이다가 밥 먹고 회복한 뒤 나중에 또 굶으면 → 2회.
        // 세이브에는 넣지 않는다(세이브 포맷 불변). 엔딩/사망은 한 세션 안에서 완결되므로 충분하다.

        [Tooltip("같은 원인의 피해가 이 시간(초) 이상 끊겼다가 다시 들어오면 새로운 위기 1회로 센다.\n" +
            "짧게 잡으면 지속 피해 한 번이 여러 번으로 쪼개지고, 길게 잡으면 별개의 습격이 하나로 합쳐진다.")]
        public float crisisGraceSeconds = 8f;

        /// <summary>DamageCause 항목 수. 원인별 마지막 피격 시각 배열의 크기로 쓴다(enum이 늘어나도 자동으로 따라간다).</summary>
        private static readonly int DamageCauseCount = System.Enum.GetValues(typeof(DamageCause)).Length;

        /// <summary>원인별 마지막 피격 시각(Time.time). NegativeInfinity로 시작해 첫 피해가 항상 새 위기로 잡히게 한다.</summary>
        private float[] lastCrisisTimeByCause;

        /// <summary>지금까지 겪은 위기 횟수(위 주석의 "에피소드" 정의).</summary>
        private int crisisCount = 0;

        /// <summary>
        /// 지금까지 겪은 위기 횟수. Design_Ending.md 4장 통계 6번 항목의 데이터 출처다.
        /// 읽기 전용이며, 세이브에 저장되지 않으므로 불러오기를 하면 이번 세션 값이 그대로 이어진다.
        /// </summary>
        public int CrisisCount => crisisCount;

        /// <summary>
        /// 원인별 마지막 피격 시각 배열을 준비한다. Awake보다 먼저 TakeDamage가 불릴 가능성
        /// (다른 컴포넌트의 Awake에서 피해를 주는 경우 등)까지 감안해 TakeDamage에서도 한 번 더 확인한다.
        /// </summary>
        private void EnsureCrisisBuffer()
        {
            if (lastCrisisTimeByCause != null && lastCrisisTimeByCause.Length >= DamageCauseCount)
                return;

            lastCrisisTimeByCause = new float[DamageCauseCount];
            for (int i = 0; i < lastCrisisTimeByCause.Length; i++)
                lastCrisisTimeByCause[i] = float.NegativeInfinity;
        }

        /// <summary>
        /// 이번 피해가 "새 위기의 시작"인지 판정하고, 맞으면 카운터를 올린다.
        /// 원인별로 마지막 피격 시각을 따로 들고 있어, 동시에 진행 중인 두 상태 이상이 서로를
        /// 새 위기로 만들어 버리는 문제가 생기지 않는다(위 주석 참고).
        /// </summary>
        private void RecordCrisis(DamageCause cause)
        {
            EnsureCrisisBuffer();

            int index = (int)cause;
            if (index < 0 || index >= lastCrisisTimeByCause.Length)
                return;

            float now = Time.time;
            if (now - lastCrisisTimeByCause[index] > Mathf.Max(0f, crisisGraceSeconds))
                crisisCount++;

            lastCrisisTimeByCause[index] = now;
        }

        /// <summary>
        /// 매 프레임(또는 일정 주기)마다 호출하여 허기/갈증 감소, 상태 이상으로 인한 피해 등
        /// 시간에 따른 생존 수치 변화를 처리한다.
        /// </summary>
        /// <param name="deltaTime">경과 시간(초)</param>
        /// <param name="isInShade">현재 그늘/실내에 있는지 여부 (일사병 회복 여부 판정용)</param>
        /// <param name="isUnderwater">현재 수면 아래(잠수 중)인지 여부 (산소 감소 판정용)</param>
        /// <param name="isDaytime">현재 태양이 떠 있는 낮 시간대인지 여부 (일사병 증가 판정용, 기본값 true는
        /// 밤/낮 주기가 없는 호출부와의 하위 호환을 위함 - 항상 낮인 것처럼 취급해 기존 동작을 유지한다)</param>
        /// <param name="isRaining">비가 오는 중인지 (일사병 정지용). 기본값 false는 날씨를 모르는
        /// 호출부와의 하위 호환 - 비가 안 오는 것처럼 취급해 기존 동작을 유지한다.</param>
        public void Tick(float deltaTime, bool isInShade, bool isUnderwater = false, bool isDaytime = true,
            bool isRaining = false)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            // IsDead 검사보다 **먼저** 부른다. 이미 죽은 뒤에 무적을 켜면 체력이 최대치로 돌아오고
            // IsDead가 다시 false가 되므로, "죽어서 아무것도 못 하는 상태"에서도 빠져나올 수 있다.
            // 치트가 전부 꺼져 있으면 이 호출은 값을 하나도 건드리지 않는다(전부 조건부 대입).
            ApplyDebugCheatClamp();
#endif

            if (IsDead)
                return;

            UpdateHungerAndThirst(deltaTime);
            UpdateSunstroke(deltaTime, isInShade, isDaytime, isRaining);
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
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            // [QA 치트] 산소 무제한. 감소 지점 자체를 건너뛴다 - 아래 익사 TakeDamage 호출에
            // 애초에 도달하지 않으므로, 무적(debugInfiniteHealth)과 무관하게 익사 피해가 사라진다.
            if (debugInfiniteOxygen)
            {
                oxygen = MaxStatValue;
                return;
            }
#endif

            // 에어포켓(수중 동굴 천장의 공기 주머니)에 머리를 넣고 있으면 잠수 중에도 산소가
            // **회복**되고, 익사 진행(아래 TakeDamage)도 여기서 함께 멈춘다. 회복 속도는 감소
            // 속도의 airPocketRecoveryMultiplier(기본 3)배 — 감소/회복이 상호 배타가 되도록
            // 이 분기에서 바로 반환한다.
            if (isUnderwater && isHeadInAirPocket)
            {
                float recoveryMultiplier = airPocketRecoveryMultiplier > 0f ? airPocketRecoveryMultiplier : 3f;
                oxygen = Mathf.Min(MaxStatValue, oxygen + oxygenDrainPerSecond * recoveryMultiplier * deltaTime);
                return;
            }

            // 산소통 소지 패시브(oxygenDrainMultiplier=0.5) 반영. 0 이하는 미설정으로 보고 1로 취급한다.
            float drainMultiplier = oxygenDrainMultiplier > 0f ? oxygenDrainMultiplier : 1f;

            if (isUnderwater)
                oxygen = Mathf.Max(0f, oxygen - oxygenDrainPerSecond * drainMultiplier * deltaTime);
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
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            // [QA 치트] 허기·갈증 정지. 감소와 굶주림 피해를 **함께** 건너뛴다(둘을 나누면
            // 허기 0에서 켰을 때 값은 안 줄면서 피해만 계속 들어온다). 값은 현재 상태로 정지시키고
            // 최대치로 밀지 않는 이유는 ApplyDebugCheatClamp 주석 참고.
            if (debugNoHungerThirst)
                return;
#endif

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
        private void UpdateSunstroke(float deltaTime, bool isInShade, bool isDaytime, bool isRaining)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            // [QA 치트] 일사병 면역. 증가 지점 자체를 건너뛰고 0으로 고정하므로, 아래
            // `sunstroke >= MaxStatValue` 피해 조건에 영원히 도달하지 않는다.
            if (debugNoHeatstroke)
            {
                sunstroke = 0f;
                return;
            }
#endif

            // [B13] 비가 오는 동안에는 증가도 회복도 하지 않는다. 회복까지 주면 비가 공짜 안전지대가
            // 되어, 우천이 "불이 꺼지고 시야가 나빠지는 불리한 이벤트"라는 설계와 정반대가 된다.
            // 멈추기만 해도 플레이어에게는 "지금은 밖에서 일할 수 있는 시간"으로 읽힌다.
            if (isRaining)
                return;

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
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            // [QA 치트] 생명 무제한. **이 메서드가 체력을 깎는 유일한 통로다**(전수 확인:
            // 굶주림/일사병/중독/출혈/익사 5곳 + HazardSource의 곰·식인종·대왕크랩·벌떼 Predator 4곳 +
            // 상어 SharkAttack 1곳. 그 외에 health를 쓰는 곳은 SaveLoadController의 불러오기 대입뿐이다).
            // 따라서 여기 한 곳만 막으면 즉사·게임오버를 포함한 모든 피해 경로가 닫힌다.
            //
            // 상태 이상(중독/출혈/골절) 자체는 **지우지 않는다.** ApplyPoison/ApplyBleeding/
            // ApplyBrokenBone은 그대로 걸리고 HUD·디버그 패널에도 O로 표시되므로, "독사에 물리면
            // 중독이 붙는가", "골절 시 이동 속도가 느려지는가" 같은 것을 무적 상태에서도 그대로
            // 테스트할 수 있다. 막는 것은 그 상태가 체력을 깎는 부분(여기)뿐이다.
            //
            // lastDamageCause/RecordCrisis보다 먼저 반환한다: 맞지 않은 것으로 치므로 사인도 위기
            // 횟수도 오염되지 않는다(치트를 끈 뒤의 통계가 그대로 유효하다).
            if (debugInfiniteHealth)
            {
                if (health < maxHealth)
                    health = maxHealth;
                return;
            }
#endif

            health = Mathf.Max(0f, health - amount);

            if (amount > 0f && cause != DamageCause.Unknown)
            {
                lastDamageCause = cause;
                RecordCrisis(cause); // 겪은 위기 횟수(CrisisCount) 집계 - 판정 기준은 필드 선언부 주석 참고.
            }
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
        /// 갈증은 회복되지만, **이미 배가 부른 상태에서 더 마시면**(마신 뒤 합계가 임계치 초과)
        /// 설사로 초과분만큼 갈증을 다시 잃는다. 규칙을 이렇게 바꾼 이유는 coconutOverdoseThreshold
        /// 선언부 주석 참고 - 예전 판정(1회 섭취량 &gt; 임계치)은 수학적으로 성립할 수 없는 죽은 코드였다.
        /// </summary>
        /// <param name="amount">회복시킬 갈증 수치</param>
        /// <param name="overdoseThreshold">과음 판정 기준치. **마신 뒤의 갈증 합계**와 비교한다.
        /// 음수(기본값 -1)로 두면 이 컴포넌트의 coconutOverdoseThreshold 필드를 쓴다 - 그 필드는
        /// ApplyBalanceConfigFallback을 통해 SurvivalBalanceConfig와 연결돼 있다.
        /// 유일한 호출부(ConsumptionSystem.Consume)는 이 인자를 넘기지 않는다.</param>
        public void ConsumeCoconutWater(float amount, float overdoseThreshold = -1f)
        {
            float thirstBefore = thirst;
            ConsumeWater(amount);

            // 인자로 0을 넘기는 것은 "합계가 0만 넘어도 과음" 이라는 유효한 지정으로 보고, 미지정은
            // 음수로만 판정한다(필드 쪽 0 이하는 ApplyBalanceConfigFallback이 미설정으로 다룬다).
            float threshold = overdoseThreshold >= 0f ? overdoseThreshold : coconutOverdoseThreshold;

            // 마시지 않은(amount 0 이하) 호출은 과음 판정에서 아예 제외한다 - 마시지도 않았는데
            // 갈증이 높다는 이유로 배탈이 나면 안 된다(임계치가 낮게 설정된 경우에 실제로 그럴 수 있다).
            float totalAfterDrink = thirstBefore + amount;
            if (amount > 0f && totalAfterDrink > threshold)
            {
                // 넘긴 만큼(초과분) 설사로 다시 잃는다. ConsumeWater가 이미 최대치로 잘라냈으므로
                // 여기서 빼는 값은 "버려진 양"이 아니라 그 위에 얹히는 실제 손해다.
                float overdoseAmount = totalAfterDrink - threshold;
                thirst = Mathf.Max(0f, thirst - overdoseAmount);
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
