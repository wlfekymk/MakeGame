> 작성: game-designer · 근거: SampleScene.unity 실측 + Recipe/Item .asset 실측

# Design_BalancePass.md — 3건 확정 밸런스 패스

`Design_Progression.md` 4·5장과 `Design_Ending.md` 2장에서 제기만 하고 닫지 못한 3건을
**"어느 파일 어느 필드를 얼마로"** 까지 내린다.

표기 규칙: **[실측]** = 파일·줄 번호를 댄 값. **[추정]** = 내가 계산·가정한 값.
`Balance_SceneSnapshot.md` 는 인용하지 않았다 — 전부 원본을 다시 읽었다. 줄 번호는 별도 표기가 없으면 `Assets/Scenes/SampleScene.unity` 기준.

---
## 0. 먼저 — 배 엔딩은 지금 **달성 불가능**하다

3건을 논하기 전에 이 발견이 전제를 바꾼다. `EndingChecker.waterSupplyItem` = **생수**,
`requiredWaterCount: 30` **[실측 SampleScene.unity:1034-1035]**. 생수를 인벤토리에 넣는 경로를 전수 조사했다:

| 경로 | 코드 | 생수 지급? |
|---|---|---|
| 자원 노드 | `ResourceNode.cs:331` `AddItem(yieldItem)` | ✗ — `resourceEntries` 12종에 생수 없음 **[실측 :887-947]** |
| 제작 | `CraftingSystem.cs:56` `AddItem(recipe.resultItem)` | ✗ — 레시피 11종 전수 확인, 생수 산출 0 |
| 조리 | `Campfire.cs:182` `AddItem(cookedResult)` | ✗ — 생고기→구운고기, 생선→구운생선뿐 |
| 사냥/낚시 | `HuntableCreature.cs:151` | ✗ — 생고기·생선뿐 |
| 물 증류기 | `WaterStill.cs:118` `CollectInto` | ✗ — **`ConsumeWater()` 로 갈증만 회복. 아이템을 주지 않는다** |
| 시작 지급 | `PlayerInventory.GrantStartingLoadout` | ✓ **1개** **[실측 :666-670]** |

`AddItem(` 호출부는 코드 전체에 5곳뿐이고 **그중 어느 것도 생수를 만들지 않는다.** 생수 GUID(`1bc80ca5…`)는
프로젝트 전체에서 3곳 — 시작 풀, 엔딩 조건, 아이템 레지스트리 — 에만 등장한다.

→ **플레이어가 가질 수 있는 생수 최대치는 1개다. 조건은 30이다. 배 엔딩은 수학적으로 도달 불가.**
`IslandTravel` 주석에 기록된 순환 잠금 사고와 같은 종류의 결함이지만 이번엔 조용하다 — 30개를 못 모으는 것이
"어렵다"로 읽히지 "불가능하다"로 읽히지 않기 때문이다. 두 엔딩의 실제 비대칭은 4배가 아니라 **∞ 대 1** 이다.

---

## 1. 엔딩 비대칭 — **안 (c) 성격 분리** 를 고른다

### 왜 (a)/(b)가 아닌가
- **(a) 비행기를 어렵게**: 지금 유일하게 완주 가능한 엔딩이다. 배의 블로커가 한 배치라도 늦어지면 도달 가능한
  엔딩이 0개가 된다. 부서진 쪽을 고치기 전에 성한 쪽을 부수지 않는다.
- **(b) 배를 쉽게**: 물자·도면·단계 제작을 덜어내면 배는 "비행기 + 절차"가 되고, 배 경로에 투입된 시스템
  3개(도면 스폰·단계 제작·비축)의 존재 이유가 사라진다.
- **(c)** 는 두 경로의 *종류* 차이를 유지하되 *크기* 차이를 줄인다. 아래 4개 조치로 실현한다.

### C-1 (블로커 해제) — 생수 획득처 신설
`SampleScene.unity` `resourceEntries`(:887 배열)에 항목 1개 추가 —
`yieldItem: 생수(guid 1bc80ca53542bae4ab84ec7762ebb50d)` / `baseCount: 2` / `minimumIslandSize: 0` / `requiresTool: 0`.

공급 **[추정]**: 노드 수 = 2 × 배율(1/2/3/4) → S 2 / M 4 / L 6 / XL 8개. 노드당 `maxHarvestCount 3` ×
`yieldPerHarvest 1` **[실측 ResourceNode.cs:21,18]** → 첫 스윕 S 6 / M 12 / L 18 / XL 24개. 서사 정합성: 비상식량이
"난파선 잔해에서 발견되는", 생수가 "불시착 시 챙길 수 있는" **[실측 Item_*.asset]** — 같은 잔해 픽션이다.

> 더 나은 안(코드 필요): `WaterStill` 이 갈증 회복 대신/외에 생수 **아이템**을 지급.
> 증류기의 존재 이유가 살고 "설치해두고 다른 일 하기"가 성립한다. → 5장 [요청].
> C-1은 코드 없이 씬만으로 블로커를 푸는 최소 조치다. 둘은 배타적이지 않다.

### C-2 — 비축 요구치 30 → 12
| 파일 | 필드 | 현재 | 새 값 |
|---|---|---|---|
| `SampleScene.unity:1033` | `requiredFoodCount` | 30 | **12** |
| `SampleScene.unity:1035` | `requiredWaterCount` | 30 | **12** |
| `Resources/SurvivalBalanceConfig.asset:44` | `endingRequiredFoodCount` | 30 | **12** |
| `Resources/SurvivalBalanceConfig.asset:45` | `endingRequiredWaterCount` | 30 | **12** |

씬·config를 함께 고쳐야 하는 이유: 폴백이 `<= 0` 이라 씬 값이 지워지면 config가 이긴다
**[실측 EndingChecker.cs:130-132]**. 근거는 2장.

### C-3 — `requiredElapsedDays: 15` **유지**
바꾸지 않는다. 이유는 2장 끝에서 따로 논증한다.

### C-4 — 비행기에 "속도의 대가"를 지운다 (재료 증량이 아니라 **동선**으로)
지금 비행기 재료 15개는 **대형 섬 한 곳으로 전부 해결된다** **[추정, 실측 근거로 계산]**:
대형 섬의 엔진부품 노드 2×3=6개(=18개 산출), 금속조각 6개(=18개 산출). 요구치는 각 2·6이다.
즉 비행기는 "원정 1회짜리 심부름"이다. 재료를 늘리면 같은 섬을 더 오래 돌 뿐이다.

| 파일 | 필드 | 현재 | 새 값 |
|---|---|---|---|
|  `SampleScene.unity:945` | 엔진부품 엔트리 `minimumIslandSize` | 2 (대형↑) | **3 (특대 전용)** |
| `SampleScene.unity:1073` | `BoatBlueprintSpawner.largeIslandSpawnChance` | 0.9 | **1.0** |

효과의 사슬 **[실측 연결]**: 엔진부품이 특대 전용 → 특대 진입에 `IslandTravel.stageRequiredToBypassCurrent: 1`
**[실측 :998]** → **비행기 경로가 배 1단계를 반드시 통과한다.**

이것이 "속도 vs 완성도"의 실제 구현이다. 비행기는 **배를 짓기 시작했다가 끝까지 가지 않고 날아가는 사람**의 엔딩이 된다.
두 엔딩이 병렬 선택지에서 **하나의 줄기와 그 분기**로 바뀐다 — `Design_Ending.md` 2장의
`탈출`/`귀환` 대비가 연출 장치가 아니라 구조적 사실이 된다.

**잠금 반증**: 배 1단계 재료 = 대나무 4 · 노끈 3 · 나뭇가지 3 **[실측 :530-536]**, 셋 다 `minimumIslandSize: 0`
= 시작 섬에서 조달된다. 1단계 도면은 대형 섬 **[실측 BoatConstructionSystem.cs:59-60]**, 대형은 자유 진입.
→ 시작 섬 → 대형 → 1단계 완성 → 특대 → 시작 섬. 순환 없음.
`largeIslandSpawnChance` 를 1.0으로 올리는 것은 이 사슬의 유일한 확률 구멍(대형 2곳 모두 도면 실패 = 1%)을
막기 위해서다. C-4 이전에는 그 1%가 배 경로만 막았지만, C-4 이후에는 **두 엔딩을 동시에** 막는다.

### 결과 **[추정]**
| | 비행기 전 → 후 | 배 전 → 후 |
|---|---|---|
| 실물 재료 | 15 → 15 | 35 + 물자 61 → 35 + 물자 25 |
| 필수 섬 등급 | 대형 1 → 대형 1 + **특대 1** | 대형 2 + 특대 1 (동일) |
| 시간 게이트 | 없음 (동일) | 15일 (동일) |
| 총량비 | 1 | **4.3배 → 2.3배** |

**반대 의견**: C-4는 비행기 경로에 배 시스템 의존을 새로 만든다. 배 1단계 도면 스폰이 실패하는 어떤 경우
(시드 이상, 향후 스폰 규칙 변경)에도 이제 비행기까지 함께 죽는다. 지금은 비행기가 도면과 완전히 독립이라 이 위험이
0이다. `largeIslandSpawnChance: 1.0` 으로 되돌릴 수 있다고 보지만, **위험을 0에서 "0으로 되돌려진 것"으로 바꾸는 거래**임은 사실이다.

---

## 2. 비상식량 30 — **요구치를 12로 낮춘다**

### 실제 획득 곡선 (계산)
**[실측]** 비상식량 엔트리 `baseCount 1 / minimumIslandSize 0 / requiresTool 0` (:933-937, `nonPerishableFoodItem`
GUID `77b4c958…` 와 일치 :1032) · 노드 규격 `maxHarvestCount 3` / `yieldPerHarvest 1` / `respawnSeconds 60`
(ResourceNode.cs:21,18,30 — 스포너가 덮어쓰지 않는다, IslandResourceSpawner.cs:190) · 자원 배율 1/2/3/4 (:948-951).

→ 섬당 비상식량 노드 = S 1 / M 2 / L 3 / XL 4개, 첫 스윕 수확 = **S 3 / M 6 / L 9 / XL 12개**
(이후 60초마다 같은 양이 다시 찬다).

**[추정]** 섬 구성 S4·M2·L2·XL1 (시작 소형 1 + 생성 8, 가중치 50/30/15/5 **[실측 IslandSpawnConfig_Default.asset]**,
대형 ≥2·특대 ≥1 강제 **[실측 :565-566]**) → 세계 전체 18노드, 첫 스윕 총 **54개**.

**직전 문서의 "~60분+" 는 오류였다. 정정한다.** 리스폰을 아이템 단위로 잡았는데 실제로는 노드 단위다.
소형 섬 노드 1개 앞에서 대기하면 60초당 3개 → **30개 = 10분**. 최단 경로는 대형+대형+특대 완전 탐색
= 9+9+12 = **정확히 30개**, 대기 0.

즉 30개는 "대기 60분"이 아니라 **"세계의 절반을 훑어야 하는 수집 할당량"** 이다. 진짜 비용은 탐색이다: 특대 섬
총 노드 = baseCount 합 24 × 4 = 96개를 반경 160m **[실측 IslandSizeMetrics.cs:41-47 — 씬에 scatterRadius 필드가
없어 이 폴백이 실제로 쓰인다]** 에 흩뿌린 중 비상식량 4개(4.2%)를 찾는 일이다. 이동속도 5m/s **[실측 :805]**.

### 결정과 근거
**요구치 30 → 12** (조치는 C-2와 동일).
- 12 = 대형 섬 1곳 첫 스윕(9) + 귀로의 소형·중형 보충(3~6). **원정 1회로 닫힌다.**
  30은 원정이 아니라 순회이고, 그 순회에서 얻는 것은 같은 아이템 30개뿐이다.
- **획득처를 늘리지 않는다(반대)**: `baseCount` 를 올리면 "난파선 잔해"의 희소성 서사가 죽고,
  무엇보다 **초반 굶주림 압력이 사라진다** — 비상식량은 도구 없이 먹는 즉효 식량(`hungerRestoreAmount: 20`)이라
  초반 난이도의 일부다.
- **다른 음식으로 대체 허용(반대)**: 합산은 코드 변경이고(`EndingChecker` 는 단일 `ItemData` 만 센다),
  구운고기는 사냥 반복으로 **무한 생산**된다(`respawnSeconds: 90`, 성공률 0.7 **[실측 :1236-1238]**).
  합산하면 요구치가 "시간만 쓰면 반드시 채워지는 수"가 되어 조건이기를 그만둔다.

### 15일 vs 30개 — 어느 쪽이 게이트여야 하는가
**15일을 게이트로 남긴다.** 근거는 하나다: **15일에는 플레이어 실력 레버가 붙어 있고, 반복 수집에는 없다.**

`Shelter.TrySleep` **[실측 Shelter.cs:76-84]** 은 밤에 쉼터에서 상호작용하면 시계를
`(ElapsedDays + 1 + 0.25) × secondsPerDay` 로 **점프**시킨다. 건너뛴 시간 동안 허기·갈증은 소모되지 않는다.
새 게임은 `newGameStartTimeOfDay = 0.3` 에서 시작한다 **[실측 DayNightCycle.cs:67,161]**.

| 플레이 | 15일까지 실시간 |
|---|---|
| 취침을 모름 (`secondsPerDay 600` **[실측 :1011]** 그대로) | (15 − 0.3) × 600 = **147분** |
| 매일 일몰 직후 취침 (일출 0.25 → 일몰 0.75만 실시간) | 270 + 14×300 = 4470초 = **74.5분** |

**15일 조건은 150분이 아니라, 아는 사람에게는 75분이다.** 2배 차이가 순수하게 "쉼터를 지었는가"에서 나온다
(쉼터 키트 = 대나무 4 · 야자잎 6 · 노끈 3, 채집 스킬 2 **[실측 Recipe_쉼터키트.asset]**).
이건 게이트가 아니라 **보상받는 준비**다 — 정확히 배 엔딩이 표방하는 "완성도"의 의미다.
반면 비상식량 30개에는 그런 레버가 없다. 빠르게 모으는 방법과 느리게 모으는 방법이 같다.
**같은 시간을 요구하더라도, 실력으로 줄일 수 있는 시간이 더 나은 게임이다.**

**반대 의견**: 이 취침 단축은 어디에도 안내되지 않는다. 지금 상태로는 "15일 = 147분"을 그대로 겪는
플레이어가 다수일 것이다. C-3(15일 유지)의 정당성은 취침이 **발견 가능해진다**는 전제에 걸려 있고,
그 전제는 아직 참이 아니다. → 5장 [요청].

---

## 3. 칼 — **채집 보너스 도구(요구가 아니라 가산)**

### 진단 정정: 칼의 용도는 0이 아니다. **지배당했다.**
**[실측 Item_칼.asset]** `isWeapon: 1`, `weaponDamage: 8`, `maxUses: 10`. 그런데
**[실측 HazardSource.cs:235-248]** `FindBestWeapon` 은 **피해량이 가장 높은 무기 하나만** 고른다 — 플레이어는 선택할 수 없다.

창 18 / 손도끼 14 / **칼 8** **[실측 Item_*.asset]**. 창·손도끼는 나뭇가지 1 + 돌조각 2로 만들어지고
둘 다 `minimumIslandSize: 0` 이라 **[실측 :888-897]** 시작 섬에서 2분이면 나온다 **[추정]**.

→ **칼은 게임 시작 약 2분 뒤부터 영구히 선택되지 않는다.** 진단이 "필드가 비었다"가 아니라
"필드는 찼는데 항상 진다"이므로, 처방은 새 용도를 붙이는 게 아니라 **경쟁하지 않는 축을 주는 것**이다.

### 고른 안
**`bonusTool` — 보유하면 수확량이 늘고, 없어도 채집은 성공한다.**

| 파일 | 조치 |
|---|---|
| `IslandResourceSpawner.cs` `ResourceEntry` + `ResourceNode.cs:18` 부근 | 필드 2개씩 추가: `public ItemData bonusTool;` `public int bonusYieldPerHarvest = 0;` (`IslandResourceSpawner.cs:190` 에서 `node.requiredTool` 과 같은 자리에 전달) |
| `ResourceNode.cs:330` | `for (i < yieldPerHarvest)` → `for (i < yieldPerHarvest + (bonusTool != null && inventory.FindItem(bonusTool) != null ? bonusYieldPerHarvest : 0))` |
| `SampleScene.unity:898-902` (야자잎 엔트리) | `bonusTool: Item_칼`, `bonusYieldPerHarvest: 1` |
| `Item_칼.asset` | `maxUses: 10` → **-1** |
| `InteractionPromptUI.cs:227` | `1회당 {yieldPerHarvest}개` 가 보너스 반영값을 표시하도록 |

**효과 수치 [실측 기반 계산]**: 노끈 14개 = 야자잎 42개 **[실측 Recipe_노끈: 야자잎 3 → 노끈 1]** = 채집 42회
→ **칼 보유 시 21회.** 배 경로 최대의 반복 노동이 정확히 절반이 된다. `GetHarvestFailure` 는 손대지 않는다.

**`maxUses: -1` 인 이유**: 레시피 11종 전수 확인 결과 **칼을 산출하는 레시피는 없다.** 재획득 불가능한 아이템에
붙은 용도는 용도가 아니라 유예다. 무제한이면 `UseItem` 이 소모하지 않아 **[실측 PlayerInventory.cs:38-44 주석]**
전투로도 채집으로도 닳지 않는다.

### 잠금 반증 — "칼을 잃었을 때 게임이 어떻게 되는가"
1. **잃는 경로가 없다.** `maxUses: -1` 이면 `UseItem` 이 제거하지 않는다. 드롭·버리기 기능은 코드에 없다.
2. **설령 잃어도 채집은 성공한다.** `bonusYieldPerHarvest` 는 **가산항**이고, 도구 판정은
   `requiresTool && requiredTool != null` 분기 **[실측 ResourceNode.cs:371]** 안에만 있다.
   야자잎 엔트리의 `requiresTool` 은 0으로 그대로 둔다 → `HarvestFailure.MissingTool` 이
   **발생할 수 있는 코드 경로가 존재하지 않는다.**
3. **최악의 결과 = 야자잎 채집 21회가 42회로 돌아간다. 시간 손해이지 경로 소멸이 아니다.** 보류된 직전 안
   (야자잎에 `requiredTool` 부착)의 실패값은 "채집 불가 → 노끈 전멸 → 배·비행기 동시 사망"이었다. 이 안의
   실패값은 "느려짐"이다. 두 안의 차이는 그것이 전부다.
4. **순환 검증**: 칼 → 야자잎 수확량 → 노끈 → 배/비행기. 칼은 어떤 재료로도 만들어지지 않으므로 이 사슬에
   되돌아오는 간선이 없다. 순환 잠금이 성립할 위상 자체가 아니다.

**반대 의견 (약점 3개)**
- 3건 중 **유일하게 코드 변경이 필요하다.**
- **보이지 않으면 없는 기능이다.** `InteractionPromptUI.cs:227` 의 `1회당 N개` 가 1→2로 바뀌는 것이
  유일한 발견 경로다. 그 한 줄이 빠지면 이 설계는 있으나 마나다.
- **칼이 무제한 무기가 된다.** 곰(체력 50 **[실측 HazardSource.cs:69]**)에 7타 — 처벌적 안전망이고
  `FindBestWeapon` 이 최고 피해를 먼저 태우므로 창·손도끼의 소모 긴장은 유지되지만,
  "무기가 전부 사라지는 상태"가 게임에서 없어지는 것은 사실이다.

---

## 4. [요청] 디렉터 — 씬/에셋 변경 목록

| # | 파일 | 필드 | 현재값 | 새값 | 근거 |
|---|---|---|---|---|---|
| 1 | `SampleScene.unity` `resourceEntries` | (신규 항목) 생수 `baseCount 2 / minimumIslandSize 0 / requiresTool 0` | 없음 | 추가 | **0장 — 없으면 배 엔딩 불가** |
| 2 | `SampleScene.unity:1033` | `requiredFoodCount` | 30 | 12 | 2장 |
| 3 | `SampleScene.unity:1035` | `requiredWaterCount` | 30 | 12 | 2장 |
| 4 | `SurvivalBalanceConfig.asset:44` | `endingRequiredFoodCount` | 30 | 12 | 폴백 3자 일치 |
| 5 | `SurvivalBalanceConfig.asset:45` | `endingRequiredWaterCount` | 30 | 12 | 폴백 3자 일치 |
| 6 |  `SampleScene.unity:945` | 엔진부품 `minimumIslandSize` | 2 | 3 | C-4 |
| 7 | `SampleScene.unity:1073` | `largeIslandSpawnChance` | 0.9 | 1.0 | C-4 위험 상쇄 |
| 8 | `SampleScene.unity:898-902` | 야자잎 엔트리 `bonusTool`/`bonusYieldPerHarvest` | 없음 | 칼 / 1 | 3장 (코드 선행) |
| 9 | `Item_칼.asset` | `maxUses` | 10 | -1 | 3장 |
| 10 | `SampleScene.unity:1040` | `requiredElapsedDays` | 15 | **유지** | 2장 |
| 11 | `SampleScene.unity:892` | 나뭇가지 엔트리 `requiredTool` | 손도끼 | `{fileID: 0}` | `requiresTool: 0` 이라 읽히지 않는 죽은 값. 오독 유발 |

## 5. [요청] 다른 에이전트

- **[요청] systems-engineer**: `ResourceEntry`/`ResourceNode` 에 `bonusTool` + `bonusYieldPerHarvest` 추가,
  `ResourceNode.Harvest:330` 루프 상한에 가산. **`GetHarvestFailure` 는 건드리지 말 것** — 판정에 들어가는 순간 3장의 잠금 반증이 무효가 된다.
- **[요청] systems-engineer**: `EndingChecker.ApplyBalanceConfigFallback:130-132` 의 `<= 0` 을 `< 0` 으로.
  지금은 `requiredFoodCount`/`WaterCount`/`FuelCount` 에 0을 넣어도 config 값으로 조용히 되돌아가 "조건 끄기"가
  불가능하다. `requiredElapsedDays` 만 이미 고쳐져 있다(:136).
- **[요청] systems-engineer**: `WaterStill` 이 생수 **아이템**을 지급하는 경로(지금 `CollectInto` 는 갈증만
  회복시킨다, WaterStill.cs:113-125). 이게 들어오면 위 표 1번(씬 노드)은 철회해도 된다.
- **[요청] ui-engineer**: `InteractionPromptUI.cs:227` 의 `1회당 N개` 가 보너스 반영값을 표시하도록 — 이 한 줄이
  3장 설계의 유일한 발견 경로다.
- **[요청] ui-engineer**: 밤에 쉼터를 조준했을 때 "취침 — 아침까지 건너뛴다" 프롬프트. 15일 조건을 147분에서
  74.5분으로 줄이는 유일한 수단이 현재 완전히 비공개다(2장).

## 6. [확인요청]

- **[확인요청] 디렉터**: `.meta` 파일이 스테이징에 없어 GUID→에셋 매핑을 역참조로 복원했다. 대부분 교차
  검증됐지만 **엔진부품(`cc208e17…`)/부력통(`664c5c68…`) 구분은 추정**이다(전자는 `AircraftRepairSystem`,
  후자는 배 3단계에만 등장 → 역할로 판별). 표 6번을 적용하기 전에 인스펙터에서 이름을 눈으로 확인해 달라.
- **[확인요청] 디렉터**: 0장의 생수 결함이 이미 다른 에이전트의 작업 목록에 있는지. 중복 수정으로 씬이 충돌하면 손해가 크다.
- **[확인요청] 디렉터**: `Shelter.TrySleep` 이 건너뛴 시간 동안 허기·갈증을 소모하지 않는 것이 의도인지
  (주석은 "의도된 단순화"라고 적고 있다). 의도가 아니라면 15일의 실제 부담이 147분으로 돌아가고 C-3을 재검토해야 한다.
