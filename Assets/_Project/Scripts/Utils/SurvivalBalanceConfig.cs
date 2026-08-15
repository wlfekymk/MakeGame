using UnityEngine;

namespace MakeGame.Data
{
    /// <summary>
    /// B2-6 / Docs/Spec_15_BalanceConfigDesign.md 스키마를 그대로 구현한 중앙 밸런스 ScriptableObject.
    /// 목적: 지금은 밸런스 수치가 SurvivalStats/WaterStill/Campfire/WeatherSystem/HazardSpawner/
    /// EndingChecker/SurvivalClock 등 각 MonoBehaviour의 public 필드 기본값에 흩어져 있어, 값 하나를
    /// 조정하려면 스크립트 기본값과 씬/프리팹 오버라이드를 모두 찾아 다녀야 한다(#13 WaterStill
    /// 사고가 실제 사례).
    ///
    /// 1단계(이번 배치) 범위: 이 클래스와 기본값만 만든다. 각 스크립트를 이 config를 참조하도록
    /// 바꾸는 배선 작업은 하지 않는다 — Spec_15의 "안전 전환 순서" 3~5단계(선택적 참조 추가 →
    /// qa-reviewer 검증 → 완전 전환)는 이후 배치에서 진행한다. 기존 MonoBehaviour의 public 필드는
    /// 단 하나도 제거/치환하지 않았다.
    ///
    /// 기본값 출처: Docs/Balance_SceneSnapshot.md(씬/프리팹 실측값). 코드 기본값이 아니라 실측값을
    /// 우선했다 — 예를 들어 hazard*Multiplier(1/1.5/2/2.5)는 HazardSpawner의 씬 실측값과 일치하고,
    /// waterStillPerSecond/waterStillMaxStorage(0.10/12)는 Spec_13에서 확정된 교정값이며,
    /// endingRequiredFoodCount/WaterCount/FuelCount(30/30/1)는 EndingChecker 씬 실측값과 일치한다.
    /// 스냅샷에 실측되지 않은 항목(코코넛워터 과음 임계치, 식중독 확률, 모닥불 연료 소모/경험치,
    /// 날씨 타이머)은 Spec_15 문서에 명시된 설계값을 그대로 썼다.
    /// </summary>
    [CreateAssetMenu(fileName = "SurvivalBalanceConfig", menuName = "MakeGame/Survival Balance Config")]
    public class SurvivalBalanceConfig : ScriptableObject
    {
        [Header("허기/갈증")]
        public float hungerDecayPerSecond = 0.05f;
        public float thirstDecayPerSecond = 0.08f;
        public float starvationDamagePerSecond = 1f;

        [Header("일사병")]
        public float sunstrokeGainPerSecond = 0.1f;
        public float sunstrokeRecoveryPerSecond = 0.2f;
        public float sunstrokeDamagePerSecond = 0.5f;

        [Header("상태이상 피해")]
        public float poisonDamagePerSecond = 0.8f;
        public float bleedingDamagePerSecond = 1.2f;

        [Header("체력 자연회복")]
        public float healthRegenThreshold = 50f;
        public float healthRegenPerSecond = 0.5f;

        [Header("산소")]
        public float oxygenRecoveryPerSecond = 25f;
        public float oxygenDrainPerSecond = 5f;
        public float drowningDamagePerSecond = 3f;

        [Header("코코넛워터 과음")]
        public float coconutOverdoseThreshold = 40f;

        [Header("식중독")]
        [Range(0f, 1f)] public float rawFoodPoisonChance = 0.3f;

        [Header("모닥불")]
        public float campfireSecondsPerFuel = 30f;
        public float campfireCookingExperience = 8f;

        [Header("물 증류기 (#13 반영)")]
        public float waterStillPerSecond = 0.10f;
        public float waterStillMaxStorage = 12f;

        [Header("낮/밤")]
        public float secondsPerDay = 600f;

        [Header("날씨")]
        public float weatherMinClearSeconds = 90f;
        public float weatherMaxClearSeconds = 240f;
        public float weatherMinRainSeconds = 40f;
        public float weatherMaxRainSeconds = 100f;
        [Range(0f, 1f)] public float rainDimFactor = 0.55f;

        [Header("위험요소 규모별 배율")]
        public float hazardSmallMultiplier = 1f;
        public float hazardMediumMultiplier = 1.5f;
        public float hazardLargeMultiplier = 2f;
        public float hazardExtraLargeMultiplier = 2.5f;

        [Header("배 엔딩 (#11 반영)")]
        public int endingRequiredFoodCount = 30;
        public int endingRequiredWaterCount = 30;
        public int endingRequiredFuelCount = 1;
        public int endingRequiredElapsedDays = 15;
    }
}
