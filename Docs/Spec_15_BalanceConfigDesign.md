# #15 중앙 밸런스 ScriptableObject 설계 (SurvivalBalanceConfig)

## 목적
모든 밸런스 수치가 각 MonoBehaviour의 public 필드 기본값으로 흩어져 있어(SurvivalStats,
WaterStill, Campfire, WeatherSystem, HazardSpawner, EndingChecker, SurvivalClock 등), 값 하나를
조정하려면 스크립트 기본값과 씬/프리팹 오버라이드를 모두 찾아 다녀야 한다. 이번 배치에서 실제로
그 문제(#13 WaterStill 씬 직렬화 의심)를 겪었다. 이번 항목은 **설계만** 한다 — 구현은 다음
배치에서 systems-engineer가 진행한다.

## 스키마 설계: `SurvivalBalanceConfig`

```csharp
namespace MakeGame.Data
{
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
```

## 매핑표 (원본 필드 → 대체 대상)

| Config 필드 | 원본 스크립트.필드 | 현재 기본값 |
|---|---|---|
| hungerDecayPerSecond | SurvivalStats.hungerDecayPerSecond | 0.05 |
| thirstDecayPerSecond | SurvivalStats.thirstDecayPerSecond | 0.08 |
| starvationDamagePerSecond | SurvivalStats.starvationDamagePerSecond | 1.0 |
| sunstrokeGainPerSecond | SurvivalStats.sunstrokeGainPerSecond | 0.1 |
| sunstrokeRecoveryPerSecond | SurvivalStats.sunstrokeRecoveryPerSecond | 0.2 |
| sunstrokeDamagePerSecond | SurvivalStats.sunstrokeDamagePerSecond | 0.5 |
| poisonDamagePerSecond | SurvivalStats.poisonDamagePerSecond | 0.8 |
| bleedingDamagePerSecond | SurvivalStats.bleedingDamagePerSecond | 1.2 |
| healthRegenThreshold | SurvivalStats.healthRegenThreshold | 50 |
| healthRegenPerSecond | SurvivalStats.healthRegenPerSecond | 0.5 |
| oxygenRecoveryPerSecond | SurvivalStats.oxygenRecoveryPerSecond | 25 |
| oxygenDrainPerSecond | SurvivalStats.oxygenDrainPerSecond | 5 |
| drowningDamagePerSecond | SurvivalStats.drowningDamagePerSecond | 3 |
| coconutOverdoseThreshold | SurvivalStats.ConsumeCoconutWater(overdoseThreshold 파라미터 기본값) | 40 |
| rawFoodPoisonChance | ConsumptionSystem.rawFoodPoisonChance | 0.3 |
| campfireSecondsPerFuel | Campfire.secondsPerFuel | 30 |
| campfireCookingExperience | Campfire.cookingExperience | 8 |
| waterStillPerSecond | WaterStill.waterPerSecond | 0.3 → **0.10(#13)** |
| waterStillMaxStorage | WaterStill.maxStorage | 20 → **12(#13)** |
| secondsPerDay | SurvivalClock.secondsPerDay | 600 |
| weatherMin/MaxClearSeconds | WeatherSystem.minClearSeconds/maxClearSeconds | 90/240 |
| weatherMin/MaxRainSeconds | WeatherSystem.minRainSeconds/maxRainSeconds | 40/100 |
| rainDimFactor | WeatherSystem.rainDimFactor | 0.55 |
| hazard*Multiplier | HazardSpawner.small/medium/large/extraLargeMultiplier | 1/1.5/2/2.5 |
| endingRequired* | EndingChecker.requiredFoodCount/requiredWaterCount/requiredFuelCount | 30/30/1 |
| endingRequiredElapsedDays | EndingChecker.requiredElapsedDays(신규, #11) | 15 |

의도적으로 config에 넣지 않은 것: `weaponDamage`/`maxUses`(ItemData 자체에 이미 있음),
`HazardSource.directDamage`/`maxHealth`(위험요소별 고유값, 종류마다 달라 config 단일 필드로
묶기 부적절 — 필요하면 별도 `HazardBalanceConfig`로 분리 검토), 위험요소 산포 반경
(`HazardSpawner.*ScatterRadius`, 밸런스보다는 월드 지형 스케일에 종속된 값이라 별도 유지).

## 마이그레이션 위험 및 안전 전환 순서

**위험**: 씬/프리팹에 개별적으로 오버라이드된 값(예: #13에서 의심한 WaterStill 인스턴스,
디렉터가 확인한 스포너 배율/반경 값)이 있는 상태에서 스크립트가 config를 참조하도록 바뀌는
순간, 그 오버라이드 값이 조용히 무시되거나 Inspector 상에서 사라질 수 있다. 코드 기본값을
config 기본값으로 그대로 옮기면, 씬에서만 조정해뒀던 실제 운영 값을 놓치게 된다.

**안전 전환 순서 (5단계)**:
1. **씬/프리팹 실측 스냅샷 우선.** unity-operator가 관련 컴포넌트(SurvivalStats, WaterStill,
   Campfire, WeatherSystem, HazardSpawner, EndingChecker, SurvivalClock) 전부의 현재 씬/프리팹
   Inspector 값을 캡처해 `Docs/Balance_SceneSnapshot.md`로 남긴다. 코드 기본값과 다른 필드는
   반드시 표시.
2. `SurvivalBalanceConfig_Default.asset`의 초기값은 "코드 기본값"이 아니라 **1번에서 캡처한
   씬 실측값**으로 채운다(씬 값이 실제로 지금 반영되고 있는 값이므로 우선).
3. 각 스크립트에 `public SurvivalBalanceConfig balanceConfig`를 **선택적(nullable) 참조**로
   추가하고, `balanceConfig`가 있으면 그 값을 우선 사용, 없으면 기존 public 필드 값을 그대로
   쓰는 폴백 구조로 만든다(`SurvivalTickDriver`가 `clock == null`일 때 폴백하는 기존 패턴과
   동일). **한 번에 강제 치환하지 않는다** — 기존 씬이 깨지지 않도록.
4. qa-reviewer가 "config 적용 전/후 실제 수치 차이 없음"을 확인한 뒤에만, 다음 배치에서 개별
   컴포넌트의 public 필드를 config 참조로 완전히 전환한다(2단계로 나눠 진행).
5. 전환 완료 후에도 개별 필드 자체는 인스펙터에 남겨두되, 툴팁에 "config가 있으면 이 필드는
   무시됨"을 명시해 혼동을 막는다.

## 수정 대상 (다음 배치, 이번엔 설계만)
- 신규: `Assets/_Project/Scripts/Utils/SurvivalBalanceConfig.cs` (systems-engineer)
- 신규: `Assets/_Project/ScriptableObjects/SurvivalBalanceConfig_Default.asset`
  (game-designer, 1~2단계 완료 후 값 채움)
- 신규: `Docs/Balance_SceneSnapshot.md` (unity-operator 실측 결과)

## 수용 기준
- 본 문서에 필드 목록/기본값/매핑표/마이그레이션 절차가 숫자와 함께 명시됨(완료).
- 이번 배치에서는 `.cs` 변경 없음 — 설계 문서만 산출물.
- 다음 배치 착수 전 `Docs/Balance_SceneSnapshot.md` 작성이 **선행 조건**으로 명시됨.

## 담당 제안
- 설계: game-designer (완료, 본 문서)
- 씬 실측(선행 필수): unity-operator
- 구현: systems-engineer (다음 배치, 씬 실측 이후 착수)
- 검증: qa-reviewer (config 적용 전/후 수치 무결성)
