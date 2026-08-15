# SampleScene 실측 스냅샷 + GUID 확정 매핑표

> 작성: 2026-08-15 · 디렉터(unity-operator 역할 겸임)
> 방법: **Unity 화면이 아니라 `Assets/Scenes/SampleScene.unity` YAML과 `*.asset.meta` 를 직접 파싱**했다.
> 화면 판독보다 정확하고 재현 가능하며, Unity를 점유하지 않는다. 앞으로 씬 값 확인은 이 방법을 우선한다.

---

## 0. 왜 이 문서가 필요한가

이 프로젝트는 **코드 기본값과 씬/프리팹 직렬화 값이 다르다.** 코드만 보고 밸런스를 논하면 틀린다.
실제로 2026-08-15 배치 1에서 스포너 public 필드를 제거했다가 씬 값이 날아갈 뻔한 사고가 있었다(커밋 직전 발견).

**규칙: 밸런스 수치를 바꿀 때는 반드시 (1) 코드 기본값 (2) 씬 직렬화 값 (3) 프리팹 오버라이드 세 곳을 모두 확인한다.**

---

## 1. GUID → 아이템명 확정 매핑표 (31개 전수)

`Item_*.asset.meta` 에서 직접 추출했다. **추론이 아니라 확정값이다.**

| GUID | 아이템 |
|---|---|
| `000803e45e0b49fba685b6d469cde39c` | 구운생선 |
| `08fe47e3a4d3407ba4ea6f1a3a13e6e7` | 금속조각 |
| `0b631157c4864eb3adb1b0bf99ba09da` | 코코넛 |
| `1a4a40095456c6449952232cb9a583d2` | 라이터 |
| `1bc80ca53542bae4ab84ec7762ebb50d` | 생수 |
| `25fad7a6c7d84ed9809d1dc8458dc0d9` | 물증류기키트 |
| `358830a65ae94744b1565a47670653c6` | 생선 |
| `3e5f88728e114bd689d243715c2450b0` | 붕대 |
| `3f55ff4a55f7414b891a323c5e7c303a` | 야자잎 |
| `4427ef709e804d3487fb24440b9995f7` | 물통 |
| `4a241ab5170140758aae80e111c89ccf` | 해독제 |
| `4dfa08c680734e5abc2e6ed6904ac0c1` | 노끈 |
| `519258073502428085c5b0ec1011eb9d` | 나뭇가지 |
| `53f26fad42714f3eb765334ed469db71` | 창 |
| `57c8b75b1a78d9b45ae04aaa7355d08a` | 칼 |
| `5d951f1b7640476989632c5784092378` | 돌조각 |
| `61bf8d7fb52249649499a0ad8b8c3faf` | 생고기 |
| `664c5c68df1543368e20c8fd2c52f8b9` | 부력통 |
| `77b4c958c2364779bb0e0e1362486b95` | 비상식량 |
| `7a3d5e91c4b64f2e9d0a1b8c6e4f2a90` | 모닥불키트 |
| `7fb050de192f45438680c437c1a1e88e` | 연료 |
| `8883e182994d44b4bde936ddb9ff29f7` | 파이어스타터 |
| `8d1c7553b9312d84ca35db5829795373` | 고무보트 |
| `91d3649162b446f4bd856b5d6f3ac4d0` | 부목 |
| `b538f6f80f3342c99094b7acef7b4779` | 쉼터키트 |
| `b86d36da6d5b45bba13e7d0c24e6a66e` | 천조각 |
| `cc208e17df7a4c4fa1353f74216a27f3` | 엔진부품 |
| `dce46f1400c042da94b13073a17e1b11` | 대나무 |
| `e6d70fced200407eb31b08731cd6db82` | 부싯돌 |
| `e78c2cccce274d2fac0d814667672602` | 손도끼 |
| `f83e11fa1f604ceabd6de3ff30e93819` | 구운고기 |

> tech-artist가 레시피 설명文 교차대조로 20개를 역추산했고 **전부 맞았다.** 나머지 11개는 참조가 없어 확정 불가였는데, `.asset.meta` 직접 추출로 채웠다.

---

## 2. game-designer [확인요청] 5건에 대한 답

### (1) WaterStill 오버라이드 — ⚠️ **프리팹에 있다**
`Assets/_Project/Prefabs/WaterStill.prefab`:
```
waterPerSecond: 0.3
maxStorage: 20
storedWater: 0
```
씬에는 WaterStill 인스턴스가 없다(플레이어가 설치하는 배치형이라 런타임 생성).
→ **`Spec_13` 의 0.10/12 적용 시 `WaterStill.cs` 기본값만 고치면 안 된다. 프리팹도 함께 수정해야 실제로 반영된다.** 이걸 놓치면 "고쳤는데 안 바뀐다".

### (2) EndingChecker의 SurvivalClock 참조 — **없다 (필드 자체가 없음)**
씬의 EndingChecker 직렬화 필드 전체:
```
boatConstruction / aircraftRepair / inventory / playerController / interactionController  → 전부 연결됨
continueKey: 32 (Space)
nonPerishableFoodItem: 비상식량,  requiredFoodCount: 30
waterSupplyItem:       생수,      requiredWaterCount: 30
fuelItem:              연료,      requiredFuelCount: 1
```
→ `survivalClock` 필드가 **아예 존재하지 않는다.** `Spec_11`(15일 경과 조건)을 구현하려면 필드 신설 + 씬 연결이 둘 다 필요하다. 코드만 고치면 인스펙터에서 비어 있어 NRE 위험 — **`IslandGenerator` 때와 똑같은 함정이니 null 가드를 반드시 넣을 것.**

### (3) PlayerInventory.startingItemPool — **4개, 붕대 없음**
```
고무보트 · 생수 · 라이터 · 칼
```
→ `Spec_14`(시작 인벤에 붕대 1개) 근거 확정. 붕대(`3e5f8872…`)를 추가하면 된다.

### (4) HazardSpawner.hazardEntries — **빈 case 2개는 목록에 없다**
```
type 1 (VenomousSnake) 0.25
type 2 (Scorpion)      0.20
type 3 (Bear)          0.15
type 4 (BeeSwarm)      0.20
type 5 (Trap)          0.20
type 6 (Cannibal)      0.10
```
→ `FoodShortage(0)` · `Dehydration(7)` 미포함. **`Spec_16`의 "의도된 설계, 코드 변경 없음" 결론이 실측으로 검증됐다.** 스포너 목록 오염 없음. Shark(8)는 `SharkSpawner`가 별도 처리.

### (5) 관련 컴포넌트 씬 실측값 → 아래 3장

---

## 3. 씬 실측 밸런스 값

### SurvivalStats (씬 = 코드 기본값과 일치)
| 항목 | 씬 값 |
|---|---|
| health / maxHealth | 100 / 100 |
| hunger / thirst 시작값 | 100 / 100 |
| hungerDecayPerSecond | 0.05 |
| thirstDecayPerSecond | 0.08 |
| starvationDamagePerSecond | 1 |
| sunstrokeGain / Recovery / Damage | 0.1 / 0.2 / 0.5 |
| poisonDamagePerSecond | 0.8 |
| bleedingDamagePerSecond | 1.2 |
| healthRegenThreshold / PerSecond | 50 / 0.5 |
| oxygenRecovery / Drain / drowningDamage | 25 / 5 / 3 |

### SurvivalClock
`secondsPerDay: 600` (실시간 10분 = 게임 1일) → `Spec_11`의 15일 = 실시간 150분.

### 스포너 — ⚠️ **코드 기본값과 다름**
| 컴포넌트 | 배율 (S/M/L/XL) | scatterRadius | 코드 기본값 |
|---|---|---|---|
| IslandResourceSpawner | **1 / 2 / 3 / 4** | **80** | 1/3.24/7.84/16, 40·72·112·160 |
| HazardSpawner | 1 / 1.5 / 2 / 2.5 | **100** | 동일 배율, 40·72·112·160 |
| CreatureSpawner | 1 / 1.5 / 2 / 2.5 | **90** | 동일 배율, 40·72·112·160 |

**자원 스포너 배율이 코드와 완전히 다르다**(선형 1/2/3/4 vs 면적비례 1/3.24/7.84/16). 반경도 셋 다 규모별이 아니라 단일값이다.
→ `IslandSizeMetrics` 는 폴백으로만 동작하므로 현재 게임은 **위 씬 값으로 돌아간다.**

### WorldMapManager
```
baseDistanceStep 1200 · distanceJitter 400 · minSpacingBetweenIslands 500
initialIslandCount 8 · oceanSize 40000 · terrainMaxHeight 2.5 · worldSeed 0
```

### BoatBlueprintSpawner
```
largeIslandSpawnChance 0.9 · extraLargeIslandSpawnChance 0.95 · placementOffset 4
```
→ 특대 섬(지형 반지름 200)에서 도면이 중심 4m에 몰리는 문제 실측 확인.

### 배 제작 3단계 재료
| 단계 | 재료 |
|---|---|
| 1 | 대나무 4 · 노끈 3 · 나뭇가지 3 |
| 2 | 대나무 6 · 노끈 5 · 금속조각 2 |
| 3 | 금속조각 4 · 부력통 2 · 노끈 6 |

현재 `currentStage: 1`, `hasCurrentStageBlueprint: 0`.

### 경비행기 수리 재료
엔진부품 2 · 금속조각 6 · 연료 3 · 노끈 4

### 자원 노드 배치 (baseCount)
나뭇가지 4 · 돌조각 3 · 야자잎 3 · 코코넛 2 · 대나무 2 · 천조각 1 · 부싯돌 1 · 비상식량 1 · 연료 1
금속조각 2(대형+, **손도끼 필요**) · 부력통 2(대형+) · 엔진부품 2(대형+)

### 사냥/낚시
| 대상 | 필요 도구 | baseCount | 성공률 | 리스폰 |
|---|---|---|---|---|
| 생고기 | 창 | 2 | 0.7 | 90초 |
| 생선 | 없음 | 2 | 0.6 | 60초 (해안) |

---

## 4. 이 스냅샷에서 나온 새 발견

1. **`Spec_13` 구현 시 프리팹 수정 필수** — 코드만 고치면 무효. 위 (1) 참고.
2. **`Spec_11` 구현 시 `survivalClock` 필드 신설 + 씬 연결 + null 가드 3종 세트 필요.**
3. **자원 스포너 배율이 코드와 씬이 완전히 다르다.** 어느 쪽이 의도인지 game-designer 판단 필요 — 씬 값(1/2/3/4)이 현재 실제 동작이다.
4. 금속조각은 **대형 섬 이상 + 손도끼 보유** 시에만 채집 가능하다. 배 2단계·경비행기 수리의 핵심 재료이므로, 손도끼를 못 만들면 진행이 막힌다. 손도끼 레시피 접근성 점검 필요.
