# B2-5 금속조각 병목 — 데드락 판정

## 목적
금속조각(`minimumIslandSize: Large`, `requiresTool: 손도끼`)은 배 2단계(2개)·3단계(4개)·경비행기
수리(6개)의 핵심 재료다. 손도끼 없이는 채집 불가 → 손도끼 재료 확보 → 대형 섬 도달 경로 전체를
추적해 진행 불가(데드락) 여부를 판정한다.

## 판정: **데드락 없음.**

### 근거 (경로 전체 추적)

1. **손도끼 제작 재료는 시작 섬(소형)에서 전부 구해진다.**
   `Recipe_손도끼.asset`: 나뭇가지×1 + 돌조각×2, `requiredSkillLevel: 1`.
   `Balance_SceneSnapshot.md` 자원 노드 배치에 따르면 나뭇가지(baseCount 4)·돌조각(baseCount 3)은
   `minimumIslandSize` 제한이 없는 공통 자원이다(금속조각/부력통/엔진부품만 "대형+" 제한이
   명시됨) — 즉 소형 섬에도 존재한다. `ResourceNode.maxHarvestCount=3`이므로 소형 섬 나뭇가지
   노드 4개만으로도 최대 12회 채집 가능해 1개가 필요한 손도끼 재료는 충분하다.
2. **Craftsmanship 스킬은 시작부터 Lv1이다.**
   `PlayerSkills.cs`의 `SkillProgress.level` 기본값이 `1`이다(`PlayerSkills.cs:18`). 손도끼
   레시피의 `requiredSkillLevel: 1`을 시작부터 충족하므로 별도 레벨업 그라인딩이 필요 없다.
3. **대형 섬은 배 1단계 완성 없이 처음부터 갈 수 있다.**
   `IslandTravel.TryTravelTo`(IslandTravel.cs:53-56)는 `stageRequiredToBypassCurrent`(=1) 조건을
   **특대 섬에만** 적용한다: `PlayerInventory.CanCarryToIsland`(PlayerInventory.cs:68-74)가
   `destinationSize != IslandSize.ExtraLarge`이면 무조건 `true`를 반환하므로, 대형 섬은 배
   진행도와 무관하게 시작 아이템 고무보트만으로 즉시 이동 가능하다. 이 설계는 이미 과거 순환
   잠금 버그(대형 섬 접근에 배 1단계가 필요 → 배 1단계 도면은 대형 섬에만 있음 → 영원히 불가능)를
   고치며 명시적으로 반영된 것으로, `PlayerInventory.cs:60-66` 주석에 그 수정 이력이 남아 있다.
4. **시작 인벤토리에 고무보트가 이미 포함돼 있다.**
   `Balance_SceneSnapshot.md` 확인: `startingItemPool = 고무보트·생수·라이터·칼`. 별도 제작
   없이 게임 시작 즉시 대형 섬으로 이동 가능하다.
5. **종합 경로**: 시작(소형 섬) → 나뭇가지1+돌조각2 채집(수 분 내) → 손도끼 제작(즉시, Lv1 충족)
   → 고무보트로 대형 섬 이동(제약 없음) → 대형 섬에서 손도끼로 금속조각 채집. 이 경로에 순환
   의존이나 막힘 지점이 없다.

## 잔여 리스크 (데드락은 아니지만 페이싱 관점에서 참고)
- 배 2단계(금속조각 2)·3단계(금속조각 4)·경비행기 수리(금속조각 6) 총 **12개**가 필요하다.
  금속조각 baseCount=2, 대형 섬 배율은 B2-4 결정에 따라 7.84(면적비례 채택 시) 또는 3(현재 씬
  선형값) — 노드 1개당 3회 채집(`maxHarvestCount=3`) 가능하므로, 대형 섬 1곳만 방문해도
  `2 × 7.84 ≈ 16개 노드 × 3회 = 최대 48회 채집분`으로 12개 확보에는 절대량이 부족하지 않다.
  다만 재생 시간(`respawnSeconds`, ResourceNode 기준 60초)을 감안하면 한 자리에서 몰아 채집하는
  것이 아니라 여러 번 방문/대기가 필요할 수 있어 "병목"이라기보다는 "시간 투자"에 가깝다 —
  데드락 판정과는 별개 사안이라 이번 문서에서 수치 조정을 제안하지 않는다.
- 위 경로가 성립하려면 대형 섬에 `BoatBlueprintSpawner`뿐 아니라 `IslandResourceSpawner`의
  금속조각 엔트리(`minimumIslandSize: Large`)가 실제로 씬에 정확히 그렇게 직렬화돼 있어야
  한다 — `Balance_SceneSnapshot.md`에 이미 실측 확인되어 있으므로 추가 확인 불필요.

## 수정 대상
없음(코드/씬 변경 불필요). 데드락이 아니라는 결론이므로 조치 대상 자체가 없다.

## 수용 기준
- 신규 게임 시작 후 데드락 없이 "손도끼 제작 → 대형 섬 이동 → 금속조각 채집"까지 도달 가능함을
  QA/unity-operator가 실제 플레이 스루로 재확인(선택, 이미 코드 근거로 판정 완료했으므로 필수는
  아님).

## 담당 제안
- 판정 완료: game-designer(본 문서)
- 실제 플레이 스루 재확인(선택): unity-operator
