# #11 배 엔딩 "30일치 비축" 조건 재설계

## 목적
`EndingChecker.CheckBoatEndingConditions()`가 `SurvivalClock.ElapsedDays`를 전혀 참조하지 않고
아이템 개수(비상식량≥30, 생수 계열≥30, 연료≥1)만 확인한다. `Item_비상식량.asset`의 설명文에는
"배 엔딩(탈출)에 필요한 30일치 비축 식량으로 쓰인다"라고 명시돼 있어, 텍스트가 약속하는 것과
실제 판정 로직이 어긋난다. 초반에 30개를 몰아 모으면 즉시 클리어되는 것이 현재 상태다.

## 결정: (A) 실제 일수 경과 조건 추가

### 근거
- 아이템 자체 설명(`Item_비상식량.asset`)이 "30일치"를 명시하고 있어, 문구를 "30개 비축"으로
  낮추는 (B)안은 이미 배포된 아이템 서사와 재차 어긋난다. 아이템 텍스트까지 함께 고치는 것보다
  판정 로직을 실제 의도에 맞게 보강하는 편이 수정 범위가 작다.
- 이 게임의 정체성은 "생존 시뮬레이션"이며, 초반 러시로 엔딩을 즉시 볼 수 있는 현재 구조는
  핵심 재미(자원을 오래 관리하는 것)를 무력화한다.
- 단, 원안 그대로 "30일 경과"를 요구하면 `secondsPerDay=600`(실시간 10분/일) 기준
  30일 = 18,000초 = **300분(5시간) 논스톱 플레이**를 강제하게 되어 비현실적이다. 세션 하나로
  완주하기엔 과하므로 요구 일수를 낮춘다.

## 수치
- **요구 일수: 15일** (18,000초 → 9,000초 = **150분(2.5시간)**). 상업 서바이벌 장르 기준으로도
  1~3시간 내 첫 엔딩 도달이 합리적인 범위라 판단, 30일 원안 대비 정확히 절반으로 낮춘다.
- 아이템 개수 조건(식량 30 / 식수 30 / 연료 1)은 그대로 유지한다 — "30일치 분량"이라는 스토리
  텍스트는 아이템 자체의 서사이므로 개수 자체를 바꿀 필요는 없고, 게이트에만 일수 조건을 더한다.
- 판정 방식: **스냅샷 방식 유지.** 15일차 이후 조건 확인 시점에 아이템 30/30/1을 "보유하고 있으면"
  즉시 통과. 상시 유지(예: 15일 내내 계속 30개를 채우고 있어야 함) 같은 별도 추적 로직은
  구현 비용 대비 이득이 낮아 요구하지 않는다.
- 하위 호환: `SurvivalClock`이 씬에 없는 테스트 환경에서는 일수 조건을 무시(항상 통과)한다 —
  `SurvivalTickDriver`가 `clock == null`일 때 "항상 낮"으로 폴백하는 기존 패턴과 동일하게 맞춘다.

## 수정 대상 파일
- `Assets/_Project/Scripts/Systems/EndingChecker.cs`
  - `CheckBoatEndingConditions()`에 아래 조건을 AND로 추가:
    ```
    bool hasEnoughDays = survivalClock == null || survivalClock.ElapsedDays >= requiredElapsedDays;
    ```
  - 신규 public 필드 2개 노출 필요: `public SurvivalClock survivalClock;`,
    `public int requiredElapsedDays = 15;`
- (선택) `Assets/_Project/Scripts/UI/SurvivalHudUI.cs` 또는 EndingChecker 자체 OnGUI에
  "며칠째 / 15일" 진행률 노출 — ui-engineer 판단에 맡김.

## 수용 기준
1. `requiredFoodCount`/`requiredWaterCount`/`requiredFuelCount` 조건을 모두 만족해도
   `ElapsedDays < 15`이면 배 엔딩이 트리거되지 않는다.
2. `ElapsedDays >= 15` + 배 100% 완성 + 물자 조건 충족 시 정상적으로 엔딩이 트리거된다.
3. `survivalClock` 필드가 비어있는 씬에서는 기존과 동일하게(일수 조건 없이) 동작한다.
4. 경비행기 수리 엔딩(`AircraftRepairSystem`)에는 이 조건을 적용하지 않는다 — 설계상 "희귀 재료
   러시" 경로로 의도된 대체 엔딩이므로 그대로 둔다(재확인 완료, 변경 불필요).

## 담당 제안
- 로직 구현: systems-engineer (`EndingChecker.cs`)
- 진행률 UI(선택): ui-engineer
- 씬에 `SurvivalClock`/`EndingChecker` 인스턴스가 실제로 배치돼 있고 필드가 정상 연결되는지는
  이번 스테이징 범위에 씬 파일이 없어 확인 못 함.
  **[확인요청] unity-operator: 씬의 EndingChecker 컴포넌트에 SurvivalClock 참조가 비어있지
  않은지 확인 필요.**
