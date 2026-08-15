# #16 FoodShortage / Dehydration 해저드 빈 case 처리

## 목적
`HazardSource.ApplyHazardEffect`의 `HazardType.FoodShortage`/`HazardType.Dehydration` case가
비어 있다(HazardSource.cs:154-157). 스폰 목록에 포함된 상태로 플레이어와 접촉해도 아무 효과가
없는 "죽은 엔트리"가 될 수 있는지 확인한다.

## 결정: 의도된 설계로 문서화 (코드 변경 없음)

### 근거
- `HazardType.cs`의 enum 주석에 이미 "Shark만 예외적으로 섬이 아니라 섬 사이의 깊은 바다에
  배치된다"는 배치 규칙이 명시돼 있고, `HazardSource.cs:154-157`의 빈 case 옆에는 시스템
  담당자가 남긴 주석("음식 부족/탈수는 SurvivalStats의 허기/갈증 감소 로직에서 이미 처리되므로
  별도 효과 없음")이 이미 존재한다. 즉 이 두 값은 "접촉 시 효과를 주는 오브젝트형 위험요소"가
  아니라, **"허기/갈증 감소 자체가 위험이다"라는 것을 표현하기 위한 분류용 enum 값**으로
  의도된 것이다. `HazardSpawner`가 오브젝트를 실제로 만들어 배치하는 대상이 아니라, 기획
  문서·UI 등에서 "이 섬의 위험 요소 종류"를 나열할 때 쓰는 개념적 카테고리에 가깝다.
- 새로 효과를 부여하면(예: 접촉 시 허기/갈증 추가 감소) `SurvivalStats`의 상시 감소 로직과
  중복 페널티가 되어 오히려 밸런스가 꼬인다. enum에서 제거하는 것도 과하다 — UI/스토리
  텍스트에서 "이 섬은 물이 부족하다" 같은 경고 표시용으로 향후 쓰일 수 있는 여지를 남겨둔다.

### 위험 요소
`HazardSpawner.hazardEntries`(씬/프리팹 Inspector 목록)에 실수로 FoodShortage/Dehydration이
포함돼 있다면, 그 확률 슬롯은 스폰만 되고 접촉해도 무효과인 채로 낭비된다(다른 위험요소들과
확률 계산에 혼동을 줄 수 있음).

**[확인요청] unity-operator / systems-engineer**: `HazardSpawner` 인스턴스(씬/프리팹)의
`hazardEntries` 목록에 `FoodShortage`/`Dehydration`이 포함돼 있는지 Inspector에서 확인 요청.
이번 스테이징 범위에는 씬 파일이 없어 직접 확인하지 못했다. 포함돼 있다면 목록에서 제거해야
"죽은 스폰"이 실제로 발생하지 않는다.

## 수정 대상
- 코드 변경 없음.
- 문서화: 본 스펙 문서.
- **[요청] systems-engineer**: `HazardType.cs`의 `FoodShortage`/`Dehydration` 항목 주석에
  "HazardSpawner.hazardEntries 스폰 목록에는 넣지 말 것 — 접촉 효과 없음, 분류용 값"이라는
  한 줄을 보강해달라. 순수 주석 추가라 로직 변경은 아니지만 `.cs` 파일이라 game-designer가
  직접 수정할 수 없어 요청으로 남긴다.

## 수용 기준
1. `HazardSource.ApplyHazardEffect`의 두 case는 빈 상태로 유지(변경 없음).
2. 확인 결과 `HazardSpawner.hazardEntries`에 두 타입이 포함돼 있지 않음(또는 확인 후 제거 완료).
3. `HazardType.cs`에 "스폰 목록에 넣지 말 것" 주석이 추가됨.

## 담당 제안
- 결정 및 문서화: game-designer (완료, 본 문서)
- 씬 실측: unity-operator
- 주석 보강 및 필요시 hazardEntries 정리: systems-engineer
