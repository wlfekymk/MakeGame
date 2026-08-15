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
    /// 2단계(B3-11) 진행 상황: SurvivalStats가 이 config를 선택적(nullable) 참조로 배선했다(필드가
    /// 0 이하로 미설정일 때만 config 값을 채우는 폴백 구조, SurvivalStats.ApplyBalanceConfigFallback
    /// 참고). WaterStill/Campfire/WeatherSystem/HazardSpawner/EndingChecker/SurvivalClock은 아직
    /// 배선하지 않았다. 기존 MonoBehaviour의 public 필드는 단 하나도 제거/치환하지 않았다.
    ///
    /// 기본값 출처: Docs/Balance_SceneSnapshot.md(씬/프리팹 실측값). 코드 기본값이 아니라 실측값을
    /// 우선했다 — 예를 들어 hazard*Multiplier는 HazardSpawner의 현재 기본값(1/1.75/2.5/3.25, B3-7
    /// 상향 반영 - qa가 지적한 stale 값을 이 배치에서 갱신했다)과 일치하고, waterStillPerSecond/
    /// waterStillMaxStorage(0.10/12)는 Spec_13에서 확정된 교정값이며, endingRequiredFoodCount/
    /// WaterCount/FuelCount(30/30/1)는 EndingChecker 씬 실측값과 일치한다. 스냅샷에 실측되지 않은
    /// 항목(코코넛워터 과음 임계치, 식중독 확률, 모닥불 연료 소모/경험치, 날씨 타이머)은 Spec_15
    /// 문서에 명시된 설계값을 그대로 썼다.
    /// [주의] config 필드를 수정할 때는 반드시 대응하는 스크립트의 현재 기본값과 대조할 것 - 이번
    /// hazard*Multiplier처럼 원본 스크립트 기본값이 나중에 바뀌면 이 config만 조용히 stale해질 수 있다.
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

        // qa 지적(B3-11-1 stale 값): B3-7에서 HazardSpawner의 실제 기본값이 1/1.5/2/2.5 → 1/1.75/2.5/3.25로
        // 올랐는데, 이 config는 갱신되지 않은 채 구값으로 남아 있었다. 지금은 SurvivalStats만 config를
        // 참조해서(B3-11 1차 배선) 이 값을 아무도 읽지 않아 무해했지만, 다음 배치에서 HazardSpawner를
        // 배선하는 순간 씬 실측값(1/1.75/2.5/3.25, Balance_SceneSnapshot.md)과 어긋나는 회귀가 될 뻔했다.
        // HazardSpawner.cs의 현재 기본값과 정확히 일치시켰다.
        [Header("위험요소 규모별 배율")]
        public float hazardSmallMultiplier = 1f;
        public float hazardMediumMultiplier = 1.75f;
        public float hazardLargeMultiplier = 2.5f;
        public float hazardExtraLargeMultiplier = 3.25f;

        [Header("배 엔딩 (#11 반영)")]
        public int endingRequiredFoodCount = 30;
        public int endingRequiredWaterCount = 30;
        public int endingRequiredFuelCount = 1;
        public int endingRequiredElapsedDays = 15;

        /// <summary>
        /// B4-2: Resources 폴더의 공용 인스턴스. 인스펙터 연결이 불가능한 런타임 생성 컴포넌트
        /// (WeatherSystem은 Bootstrap이 new GameObject로 만들고, Campfire/WaterStill은 플레이어가
        /// 설치할 때 생성된다)가 balanceConfig를 얻을 수 있는 유일한 경로다. ItemDataRegistry의
        /// LoadFromResources와 같은 방식이며, 에셋이 없으면 조용히 null을 반환한다 - 이 경우 각
        /// 컴포넌트는 기존 필드 기본값으로 100% 동일하게 동작한다(NRE 없음).
        /// 씬/프리팹에서 명시적으로 연결한 config가 항상 이 공용 인스턴스보다 우선한다.
        /// </summary>
        public static SurvivalBalanceConfig Active
        {
            get
            {
                if (cachedActive == null)
                    cachedActive = Resources.Load<SurvivalBalanceConfig>(ResourceName);

                return cachedActive;
            }
        }

        /// <summary>Resources 폴더 기준 에셋 이름(확장자 없음).</summary>
        public const string ResourceName = "SurvivalBalanceConfig";

        // Resources.Load는 내부 캐시가 있지만 매 컴포넌트 Awake마다 호출하면 불필요한 조회가 쌓인다.
        // 에셋이 없을 때(null)는 캐시가 성립하지 않아 매번 재시도하는데, 그 편이 안전하다 -
        // 에디터에서 에셋을 새로 만든 직후 Play를 눌러도 곧바로 잡힌다.
        private static SurvivalBalanceConfig cachedActive;
    }
}
