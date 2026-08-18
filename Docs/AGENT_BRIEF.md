# 에이전트 필독 브리핑 (단일 진실 파일)

> 갱신: **v0.2.00**  (예전 표기 0.01.128까지가 이 문서의 3장 기준이다) · **커밋할 때마다 디렉터가 갱신한다.**
> 이 파일 하나만 읽고 시작하면 된다. 다른 문서보다 이 파일이 항상 최신이다.
> 여기 적힌 값과 다른 것을 발견하면 **먼저 이 파일을 의심하지 말고 소스/씬 YAML을 확인한 뒤**
> 보고서에 `[요청] 디렉터: AGENT_BRIEF 갱신` 으로 올려라.
> 수치 옆의 `파일:줄` / `씬` 은 그 값을 실제로 읽어 온 곳이다. 낡았는지 의심되면 거기부터 봐라.

---

## ★ 현재 방향 (2026-08-16 전환 — 이전 문서들과 다르다)

**이 게임은 더 이상 "탈출 게임"이 아니다.** 사용자 지시:

> "꼭 탈출이 정답은 아니야. **무인도에 집을 잘 지어서 힐링 하는 것도 방법**이거든."

- **엔딩(배 탈출 / 경비행기 수리)에 새 작업을 넣지 마라.** 구현된 것은 그대로 둔다.
- 우선순위: **생존 리듬 → 집 짓기 → 배 만들기 → 배 위에 집 짓기**
- 기획 판단 기준: **"이걸 하면 플레이어가 섬을 떠나고 싶어지는가, 머물고 싶어지는가?"**
  → 머물고 싶어져야 한다.
- 압박(허기·갈증·위험)은 유지하되 **처벌이 아니라 리듬**으로. 힐링이 가능해야 한다.
- `Docs/Design_Progression.md` / `Design_Ending.md` / `Design_BalancePass.md` 등 **엔딩 중심으로
  쓰인 기존 설계 문서는 전제가 바뀌었다.** 수치는 참고하되 목표 설정은 따르지 마라.

### 보고 형식 (토큰 절약 — 사용자 지시)
**결론만 써라.** 판단 결과 + 한 줄 근거. 계산 과정·검산 표·대안 비교는 **요구받지 않는 한 쓰지 마라.**
근거가 필요하면 파일:줄만 대라.

---

## 0. 이 프로젝트에서 사고가 나는 단 하나의 이유

**코드 기본값과 씬/프리팹 직렬화 값이 다르고, 씬 값이 이긴다.**

여기서 파생된 실제 사고 목록이다. 전부 한 번씩 일어났다.

| 사고 | 무슨 일이 있었나 |
|---|---|
| 스포너 public 필드 제거 | "씬이 없으니 안전하다"고 판단 → 씬 직렬화 값이 날아갈 뻔했다 |
| 상어 `depthBelowSeaLevel` | 코드 0.6 / 씬 2 로 갈라져 씬이 이겼다 → 지느러미 작업 2개 배치가 통째로 무효 |
| 위험요소 배율 | 코드만 올리고 씬을 놓쳤다 → 상향이 반영된 적이 없었다 |
| `scatterRadius` | 주석은 "규모별로 나눴다"인데 네 값이 전부 같았다 → 소형 섬 자원이 바다로 나갔다 |
| 자원 배율 | 주석은 "면적비로 올렸다"인데 값은 선형 그대로였다 |
| WaterStill 프리팹 | 코드/config는 0.1/12인데 프리팹이 0.3/20 → "고쳤는데 안 바뀐다" |
| `slotCapacity` | 씬 텍스트에 키가 없어 "씬이 안 덮어쓴다"고 판단 → 실제로는 인스펙터에 30이 박혀 있었다 (4장 4번) |

**규칙: 수치를 논할 때는 (1) 코드 기본값 (2) 씬 직렬화 값 (3) 프리팹 오버라이드 세 곳을 모두 확인한다.**
**규칙: 주석이 "고쳤다"고 주장하면 값을 직접 확인한다. 이 프로젝트의 주석은 여러 번 거짓말했다.**
**규칙: "씬 텍스트에 키가 없다"는 "씬이 안 덮어쓴다"가 아니다. 4장 4번을 반드시 읽어라.**

---

## 1. 프로젝트 형태

- Unity 6.5 (6000.5.6f1), URP, 단일 씬(`Assets/Scenes/SampleScene.unity`)
- **3D 모델 에셋은 곰 2개뿐이다** — `Resources/Models/bear_adult.obj`(12,000삼각형) / `bear_cub.obj`(6,999).
  나머지는 여전히 `GameObject.CreatePrimitive` / 내장 메시 조합으로 런타임 생성한다.
  로우폴리·스타일라이즈드가 유일하게 가능한 아트 방향이다.
- 오디오는 `ProceduralAudioClipGenerator` 절차 생성이 기본이고, **외부 트랙이 하나 있다** —
  `Resources/Audio/Music/BGM_StayHerePiano.mp3`(`BackgroundMusicPlayer.cs:23,95` 가 먼저 찾고 없으면 폴백).
- **텍스처는 있다**(`Resources/Textures/`). 작업본에서 확인된 것은 `bamboo / bark / bearfur / bearpad /
  bear_albedo / bear_normal / driftwood / frond / rock / thatch` 이고, 코드는 그 밖에
  `water`(`WorldMapManager.cs:412`) · `leaf`(`:933`) · `UI/title_background`(`MainMenuController.cs:119`)도
  로드한다. **작업본에 안 보여도 실제 디스크에는 있다** — 스크립트·씬·VERSION 외 에셋은 부분 스테이징이다.
- **지형 본체 색은 모래가 아니라 Meadow Green**(#8AA84F, `StructureVisualBuilder.cs:45`). 모래는
  `DrySandCap` / `WetSandCap` 덮개로만 존재하고, 세 캡이 정확한 여집합이라 본체가 노출되지 않는다.
- UI도 전부 코드 생성(`UIBuilder.cs` 공통 팩토리). 프리팹 UI 없음.
- **프리팹은 3개뿐이다**: `Prefabs/Campfire` · `Shelter` · `WaterStill`. 셋 다 씬에 인스턴스가 없고
  런타임에 설치되므로 **프리팹 직렬화 값이 진실**이다(코드 기본값이 아니다).
- `.asmdef` 없음 → 전부 `Assembly-CSharp` 단일 어셈블리(순환 참조 걱정 불필요).

---

### 파일 분할 지도 (0.2.03~0.2.06 리팩토링)
거대 파일이 partial로 갈라졌다. **락을 걸 때 파일 단위로 정확히 지정해라** — "HazardSource"라고만 쓰면 어느 파일인지 모호하다.

| 클래스 | 파일 | 내용 |
|---|---|---|
| HazardSource | HazardSource.cs | 공통 위험요소·전투·재등장 (719줄) |
| 〃 | HazardSource.BearAI.cs | 성체/새끼 곰 AI (991줄) |
| IslandMeshGenerator | IslandMeshGenerator.cs | 지형 메시·프로파일 8종·모래 캡 (1,256줄) |
| 〃 | IslandMeshGenerator.Vegetation.cs | 초목·소품 배치 (828줄) |
| 〃 | IslandMeshGenerator.MeshLibrary.cs | 메시 공급·모델 로더 (682줄) |
| CreatureVisualBuilder | CreatureVisualBuilder.cs | 공통·곰 외 생물 (608줄) |
| 〃 | CreatureVisualBuilder.Bear.cs | 곰 규격·모델·빌더 (550줄) |
| CreatureMeshLibrary | CreatureVisualBuilder.MeshLibrary.cs | 생물 메시 (1,820줄) |
| BuildingSystem | BuildingSystem.cs | 격자·배치·지지·실내 판정 (2,682줄) |
| 〃 | BuildingSystem.Chest.cs | 상자 런타임 거동 (134줄) |
| 〃 | BuildingSystem.Persistence.cs | 저장/복원 (387줄) |
| IslandResourceSpawner | IslandResourceSpawner.cs | 배치·stableKey·증량 (505줄) |
| 〃 | IslandResourceSpawner.Visuals.cs | 노드 겉모습 (671줄) |
| ResourceVisualLibrary | IslandResourceSpawner.MeshLibrary.cs | 자원 메시·OBJ 로더 (684줄) |

## 2. 절대 금지 (전 역할 공통)

1. **소유 파일 밖 편집 금지.** 매 웨이브 3~4명이 병렬 편집한다. 남의 파일을 건드리면 덮어쓰기 사고다.
   → 단, **작업을 끝내려면 필요한데 금지된 파일이 있으면 조용히 우회하지 말고 보고해라**(4장 5번).
2. **public 필드 제거·개명·타입변경 금지.** 씬 직렬화 값이 조용히 사라진다.
3. **`.unity` / `.prefab` / `.asset` / `.meta` 편집 금지.** 디렉터가 직렬로 처리한다.
   → `[요청] 디렉터:` 로 **파일 · 필드(YAML 키 정확히) · 현재값 → 새값** 을 올려라.
4. **`OnGUI`(IMGUI) 추가 금지.** sortOrder와 무관하게 Canvas 위에 덮어 그려진다.
   실제로 이것 때문에 GameOverUI가 통째로 안 보인 적이 있다. 현재 프로젝트 전체 `OnGUI` = 0개.
5. **아직 없는 API를 호출하지 마라.** 컴파일이 깨지면 전원이 막힌다.
   필요하면 `[요청] <대상>:` 으로 정확한 시그니처를 적어라.
6. **월드 생성·배치에 `UnityEngine.Random` 금지.** 섬별 시드 `System.Random` 스트림을 쓴다
   (`SeededRandomExtensions.CreateForIsland`, 재현성). 날씨·조리 확률 등 비생성 계열은 예외다.
7. **`Cursor.lockState` 를 직접 만지지 마라.** `CursorLockController` 가 단독으로 결정한다(3장).
8. **에이전트끼리 직접 대화 금지.** `[요청] <대상>: <내용>` 으로 보고서에 적으면 디렉터가 중계한다.
9. **"스테이징에 없다 ≠ 프로젝트에 없다."** 파일이 안 보여도 없다고 단정하지 말고
   `[확인요청]` 으로 올려라. 새로 만들어 덮어쓰지 마라.
10. **컴파일 가능한 코드만.** 세미콜론 2개가 빠져 커밋 2개가 빌드 깨진 채 올라간 전력이 있다.
    특히 `UIBuilder.CreatePanel(...)` 같은 다중 줄 호출의 닫는 괄호·세미콜론을 눈으로 재확인해라.

---

## 3. 씬 실측값 (v0.01.128 = 0.2.00 직전 기준)

씬 `Assets/Scenes/SampleScene.unity` = **`--- !u!` 앵커 53개** (MonoBehaviour 34 · GameObject 5 ·
Transform 5 · 나머지는 카메라/조명/렌더 설정). MonoBehaviour 34개 중 3개는 URP 내장이다.

### 생존 수치 (SurvivalStats — 전부 씬)
health/maxHealth 100 · hunger/thirst 시작 100
`hungerDecayPerSecond 0.05` · `thirstDecayPerSecond 0.08` · `starvationDamagePerSecond 1`
`sunstrokeGainPerSecond 0.25`(← 0.1로는 일사병 100 도달이 수학적으로 불가능했다)
`sunstrokeRecoveryPerSecond 0.2` · `sunstrokeDamagePerSecond 1.0`(← 0.5는 회복 0.5와 정확히 상쇄됐다)
`poisonDamagePerSecond 0.8` · `bleedingDamagePerSecond 1.2`
`healthRegenThreshold 50` · `healthRegenPerSecond 0.5`
`oxygenRecoveryPerSecond 25` · `oxygenDrainPerSecond 5` · `drowningDamagePerSecond 3`
`coconutOverdoseThreshold 100` · `crisisGraceSeconds 8`

### ⚠️ 씬에 **없는** 컴포넌트 (이 절에서 가장 자주 오판하는 지점)
씬은 방금 재직렬화됐지만, **아래 목록은 여전히 씬에 인스턴스가 없다**(전부 `RuntimeInitializeOnLoadMethod`
자기 부트스트랩 — 각 파일에서 그 속성을 직접 확인했다):
`DayNightCycle` · `WeatherSystem` · `BuildingSystem` · `QuestSystem` · `CursorLockController` ·
`BackgroundMusicPlayer` · `SurvivalHudUI` · `QuestUI` · `BuildMenuUI` · `ChestUI` · `EndingUI` ·
`GameOverUI` · `CombatFeedbackUI` · `InteractionPromptUI`
→ 이들은 **코드 기본값이 유일한 소스**다. 씬을 뒤져도 없다.

**`Campfire` / `WaterStill` / `Shelter` 는 이 목록이 아니다.** 셋은 부트스트랩되지 않고 **프리팹**으로
설치된다(`SaveLoadController` 의 `campfirePrefab`/`shelterPrefab`/`waterStillPrefab`) →
**프리팹 값이 이긴다.** (0장의 WaterStill 사고가 정확히 이것이다. 현재 프리팹 = `waterPerSecond 0.1`
/ `maxStorage 12` / `waterPerBottle 6` 로 코드와 일치한다.)

### 시간 / 월드
`SurvivalClock.secondsPerDay 600`(씬) — 실시간 10분 = 게임 1일. `sunsetWarningTimeOfDay 0.65`
새 게임은 `DayNightCycle.newGameStartTimeOfDay 0.3`(아침)에 시작한다 — **씬 아님**(`DayNightCycle.cs:95`).
**0(자정)이면 게임 시작 2분 30초가 새까맣다** — 예전 "검은 하늘 버그"가 이것이다.
`WorldMapManager`(씬): `baseDistanceStep 1200` · `oceanSize 40000` · `terrainMaxHeight 8` · `worldSeed 0`
`initialIslandCount 8` (+ 시작 섬 1 = **총 9개**)
플레이어 시작 위치 `(0, 14, 0)`(씬) — terrainMaxHeight 8보다 높아야 한다. **5로 두면 지형 안에서 스폰된다.**
CharacterController(씬): 높이 2 · 반지름 0.5 · `stepOffset 0.3` · `skinWidth 0.08` · center (0,1,0).
→ 계단·턱 관련 수치를 손대기 전에 이 네 값을 먼저 봐라(`BuildingSystem.cs:145` 주석이 이걸 전제한다).

### 섬 규모 (`IslandSizeMetrics.cs:25`, 코드가 단일 소스 — 씬 값 없음)
지형 반지름 Small 50 / Medium 90 / Large 140 / ExtraLarge 200 · `IslandSize` enum = 0 / 1 / 2 / 3

### 스포너 (전부 씬)
| 컴포넌트 | 배율 (S/M/L/XL) | 산포 반경 |
|---|---|---|
| IslandResourceSpawner | 1 / 2 / 3 / 4 (선형 — 의도된 것) | 40 / 72 / 112 / 160 |
| HazardSpawner | 1 / 1.75 / 2.5 / 3.25 | 40 / 72 / 112 / 160 |
| CreatureSpawner | 1 / 1.5 / 2 / 2.5 | 40 / 72 / 112 / 160 |

> ⚠️ **HazardSpawner에서 "배율 = 마릿수"가 아니다.** 마릿수는 면적 밀도로 정해진다
> (`HazardSpawner.ComputeHazardCount`):
> `round(hazardsPerTenThousandSquareMeters(2.0) × π·r²/10,000 × 규모트림)`, 최소 1,
> 상한 `maxHazardsPerIsland`(씬 20). **규모 트림은 현재 네 규모 모두 정확히 1.0**이라 배율 필드는
> 마릿수에 영향을 주지 않고 종류 가중치에만 쓰인다.
> 실측 마릿수 — 소형 1 / 중형 3 / 대형 8 / 특대 16. 시작 섬은 0(면제).

산포 반경은 지형 반지름의 80%다. **씬의 낡은 `scatterRadius` 단일 키는 제거됐다.**
자원 배율을 면적비(1:3.24:7.84:16)로 올리자는 주석이 있는데 **일부러 선형을 유지한다** —
면적비면 특대 섬 노드가 96 → 380개가 된다. 큰 섬은 "빽빽한 곳"이 아니라 "특별한 자원이 있는 곳"이다.

### 위험 요소 (씬 `hazardEntries`, 7종)
| type | 종류 | baseChance | minIslandSize | guaranteedCount |
|---|---|---|---|---|
| 1 | 독사 | 0.25 | 0 | 0 |
| 2 | 전갈 | 0.20 | 0 | 0 |
| 3 | **곰** | 0.15 | **1 (중형+)** | **1** |
| 4 | 벌떼 | 0.20 | 0 | 0 |
| 5 | 함정 | 0.20 | 0 | 0 |
| 6 | 식인종 | 0.10 | 0 | 0 |
| 9 | **대왕 크랩** | 0.12 | 0 | 0 |

`HazardType`(`HazardType.cs`) = FoodShortage 0 / VenomousSnake 1 / Scorpion 2 / Bear 3 / BeeSwarm 4 /
Trap 5 / Cannibal 6 / Dehydration 7 / Shark 8 / **GiantCrab 9**.
**추가는 반드시 맨 끝에.** 씬이 정수로 들고 있어 중간 삽입하면 섬의 위험 요소가 통째로 뒤바뀐다.
0/7(음식부족·탈수)은 `SurvivalStats` 가 담당하는 개념적 값이라 `hazardEntries` 에 넣지 마라(효과가 없다).

보장 배치는 마릿수를 늘리지 않고 **앞자리를 덮어쓴다**(`HazardSpawner.cs:243-262`). rng는 자리마다
정확히 1회 소비하므로 같은 worldSeed의 기존 월드가 밀리지 않는다.

체력/접촉피해(`HazardSource.ConfigureForType`): 대왕 크랩 60/8 · 곰 50/10(기본값) · 식인종 35/10 ·
상어 25/18 · 벌떼 12/10 · **새끼 곰 14/3**. 독사·전갈·함정은 전투 대상이 아니다(체력 없음).

### 곰 (v0.01.093 이후 추가 — 문서에 없던 시스템)
- **실물 모델 2벌.** `Resources/Models/bear_adult.obj`(12,000삼각형) / `bear_cub.obj`(6,999).
  좌표 계약 = **미터 · +Y 위 · +Z 정면 · 발바닥 y=0 · X/Z 중심 정렬**. 새 모델도 이 계약을 지켜라.
- **규격이 두 벌이고 모델 유무로 갈린다**(`CreatureVisualBuilder.cs:112-138`). 한 세션에서 섞이지 않는다.
  모델 곰 `(0.86, 1.22, 2.56)`/`0.61`(실측 0.981×1.219×2.562) · 절차 폴백 `(0.86, 1.80, 2.56)`/`0.90` ·
  새끼 `(0.45, 0.65, 1.73)`/`0.325`(실측 0.452×0.644×1.734). **x(0.86)·z(2.56)는 바꾸지 마라** —
  추격 AI 접촉 사거리가 이 부피 전제다. 규격/groundOffset은 **함께만** 의미가 있다(따로 바꾸면 발이 뜬다).
- **성체 AI**(`HazardSource.cs:66-99`, 전부 코드 기본값): `bearDetectRadius 18` · `bearLoseRadius 27` ·
  `bearViewHalfAngle 65°` · `bearCloseSenseRadius 6` · `bearAttackRange 2.8` · `bearLeashRadius 42` ·
  `bearChaseSpeed 5.3` · `bearWanderSpeed 1.2` · `bearAcceleration 3` · `bearDeceleration 6.5` ·
  회전 추격 130°/s · 배회 65°/s. 추격 속도는 런타임에 `clamp(moveSpeed × 1.06, 1.5, 8)` 로 다시 잡힌다.
- **새끼**(`HazardSource.cs:1461-1485`): 플레이어를 **쫓지 않는다**. 12m에서 도망(속도 4.2 · 회전 210°/s ·
  이탈 후 3.5초 더 달림), **10m에 들어오거나 맞으면 반경 30m 성체를 시야각 무관하게 즉시 추격 상태로
  깨운다**(쿨다운 4초). 출혈은 걸지 않는다.
- **새끼가 될 확률 40%** — `(islandIndex, stableKey) — **v0.2.05부터 안정 해시**(SaveData.StableSpawnKey). 예전 spawnOrder 러닝 카운터 아님` 해시(`HazardSpawner.cs:303,318`).
  **보장 배치분(중형+ 섬의 곰 1마리)은 언제나 성체다.**

### 자원 노드 (씬 `resourceEntries`, 13종 · 순서가 곧 세이브 키다)
나뭇가지 4 · 돌조각 3 · 야자잎 3 · 코코넛 2 · 대나무 2 · 천조각 1 · 부싯돌 1 ·
금속조각 2(**대형+ & 손도끼 필요**) · 부력통 2(대형+) · 비상식량 1 · 연료 1 ·
엔진부품 2(**특대 전용**) · 생수 1(중형+)
야자잎에 `bonusTool: 칼` / `bonusYieldPerHarvest: 1` (요구가 아니라 가산 — 칼이 없어도 채집된다)

> ⚠️ **`resourceEntries` 순서를 바꾸거나 중간에 끼워 넣지 마라.** `spawnOrder` 는 엔트리 인덱스가
> 아니라 **실제로 스폰된 노드의 러닝 카운터**이고(규모 미달 엔트리는 통째로 건너뛴다),
> `SaveLoadController` 가 `(islandIndex, stableKey) — **v0.2.05부터 안정 해시**(SaveData.StableSpawnKey). 예전 spawnOrder 러닝 카운터 아님` 를 세이브 키로 쓴다. 중간 삽입 = 그 뒤 전부의
> 번호가 밀림 = 기존 세이브의 채집 상태가 엉뚱한 노드에 복원된다. **추가는 반드시 맨 끝에.**
> 시작 섬 착륙 원 3노드도 같은 이유로 루프 뒤에 붙어 있다.
> `hazardEntries` · `HazardType` · `BuildPieceType` 도 정확히 같은 제약이다.

### 사냥감 (씬 `creatureEntries`, **3개**)
1. 생고기 — 요구 도구 있음 · baseCount 2 · 성공률 0.7 · 리스폰 90초
2. 생선 — 도구 불필요 · baseCount 2 · 성공률 0.6 · 리스폰 60초 · `preferShoreline 1`
3. **생선(게, `isCrab: 1`)** — 도구 불필요 · baseCount 2 · 성공률 0.65 · 리스폰 45초 · `preferShoreline 1`
전부 `nightSuccessBonus 0.2` / `nightYieldBonus 1`.

### 건축 (v0.01.093 이후 추가 — 문서에 없던 시스템)
`BuildingSystem`(씬에 없음 · 자기 부트스트랩) + `BuildPieceCatalog`(규격·재료) +
`BuildPieceVisualBuilder`(형상). 토글 **B** · 회전 **Q**(+휠) · 사거리 8m · 부품 선택 숫자키 **1~7**.

`BuildPieceType`(`BuildPieceCatalog.cs:10`) = **Floor 0 / Wall 1 / Doorway 2 / Window 3 / Stair 4 /
Roof 5 / Chest 6.** 값이 세이브에 정수로 들어간다 — **추가는 맨 끝에만.**
격자: `CellSize 2m` · `LevelHeight 2.5m`(`BuildPieceCatalog.cs:73,76`). 격자 원점 = 월드 (0,0,0).
좌표 공간 `BuildSpace` = **Ground 0 / Deck 1**(`BuildingSystem.cs:44`). 갑판 조각은 뗏목 로컬 좌표로
저장돼 뗏목이 움직여도 집이 따라간다. 옛 세이브는 이 필드가 없어 0으로 읽히고, 그게 옛 동작이다.

재료(`BuildPieceCatalog.cs:82-137`, 이름은 `ItemData.itemName` 과 **문자 그대로** 대조된다):
| 부품 | 재료 |
|---|---|
| 바닥 | 나뭇가지 4 (**노끈 없음** — 첫 바닥은 제작대 없이 놓을 수 있어야 한다) |
| 벽 | 나뭇가지 4 · 노끈 1 |
| 문 | 나뭇가지 5 · 노끈 2 |
| 창문 | 나뭇가지 4 · 대나무 2 · 노끈 1 |
| 계단 | 나뭇가지 6 · 대나무 2 · 노끈 2 |
| 지붕 | 나뭇가지 3 · 야자잎 3 · 노끈 1 |
| 보관 상자(소형) | 나뭇가지 8 · 노끈 3 · 야자잎 4 |
철거 반환은 **재료의 절반(내림)** 이고, 인벤토리가 가득 차면 철거 자체를 취소한다(아이템 증발 금지).

**★ 되돌리면 안 되는 규칙 (전부 사고를 겪고 들어온 것이다 · 줄번호는 `BuildingSystem.cs`)**
1. **바닥이 없어도 아래 벽이 있으면 벽 한 층을 더 올린다** — `TryGetWallSupport`(`:1178`).
   "벽은 바닥이 있어야 한다"로 되돌리지 마라.
2. **철거는 위층부터.** 위에 바닥·지붕·계단·상자·벽이 얹혀 있으면 거부한다(`:2087-2112`).
3. **지붕 위에는 아무것도 쌓지 않는다.** 지붕은 `roofByKey`(`:218`)에 따로 들어가고 딛는 면 조회
   (`TryGetFloorTopY`)·천장 조회·실내 판정에 섞이지 않는다.
4. **상자는 지지 근거가 아니다.** `chestByKey`(`:227`)에만 들어간다 — 상자 위에 벽·지붕이 서면 안 된다.
   **내용물이 남은 상자는 철거 거부**(`:2087`). 순서는 "비우기 → 상자 → 바닥".
5. **뗏목 관통 차단** `BlockedByRaft`(`:37`). 판정 여유 `RaftBlockBias 0.05`(`:172`)가 없으면
   반올림 오차 한 번에 갑판 건축 전체가 "막힘"으로 뒤집힌다.
6. **계단 앞칸 통행 여유** `StairFrontClearance 0.25`(`:145`) — 0으로 되돌리면 6단에서 막혀 못 올라간다
   (부족분이 정확히 0.037m였다). 씬의 CharacterController 값을 전제로 계산된 상수다.

### 보관 상자
`StorageChest` + `ChestUI`. 4등급 = **소형 50 / 중형 100 / 대형 150 / 특대 200칸**
(`BuildPieceCatalog.cs:168`). **설치는 소형만 가능하고 상위는 이미 놓인 상자를 업그레이드해야 도달한다.**
승급 재료(`BuildPieceCatalog.cs:139-160`):
- 소→중: 나뭇가지 10 · 대나무 6 · 노끈 4
- 중→대: 대나무 12 · 금속조각 4 · 노끈 6 · 천조각 3
- 대→특대: 대나무 16 · 금속조각 10 · 노끈 8 · 천조각 6

금속조각은 대형+ 섬 & 손도끼 필요라 대형 이상 상자는 확실히 중반 이후 목표다.
상자는 **바닥/갑판 위에만** 놓이고, 조준 4m 안(`ChestFocusDistance`)에서 **E**로 연다.

### 인벤토리 / 상자 UI
`PlayerInventory.slotCapacity` = **100**(씬에 직렬화됨). 코드 상수 `DefaultSlotCapacity` = 100
(`PlayerInventory.cs:44`). `SlotCapacity` 는 `slotCapacity > 0 ? slotCapacity : 100`.
두 창 모두 공용 `VirtualSlotGrid`(`InventorySlotView.cs:69`) — ScrollRect + 칸 뷰 풀링을 쓴다.
**창 높이는 고정이고 칸 뷰 개수에 상한이 있다**(`(visibleRows + SpareRows) × columns`, `:161`):
인벤토리 6열×보이는 7줄 → **칸 뷰 최대 54개**, 상자 6열×보이는 6줄 → **최대 48개**.
용량을 늘려도 **콘텐츠 높이만 바뀐다.** 칸을 전부 그리도록 되돌리지 마라 — 200칸이면 GameObject
1,200개가 넘고 등급을 올릴 때마다 프레임이 튄다.

### 커서 — `CursorLockController` 가 단독 결정한다
게임 중에는 커서를 화면 중앙에 잠근다. **다른 어떤 파일도 `Cursor.lockState` 를 직접 만지면 안 된다.**
푸는 조건(하나라도 성립하면): `Time.timeScale <= 0` / 타이틀·설정 화면 열림 / **활성 상태인
`UIDragHandle` 이 하나라도 있음**(= 창이 열려 있음) / `LeftShift`(또는 RightShift)를 누름.
새 창은 제목 표시줄에 `UIDragHandle` 을 붙이고 닫을 때 루트를 `SetActive(false)` 하는 것만으로
커서가 알아서 풀리고 잠긴다. **건축 핫바(BuildMenuUI)만 의도적 예외** — 그걸 "열린 창"으로 세면
건축 모드 내내 시야가 얼어붙는다.

### 그 밖에 v0.01.093 이후 들어온 것
- **뗏목 갑판 콜라이더**(`RaftStructure.cs`): 선체 `BoxCollider` 하나 + 갑판 윗면 전용 얇은 판
  `DeckSurface`(`:67`). `DeckLength 8` · `DeckWidth 5.2` · `DeckSurfaceY 0.72` · `TotalBuildLevels 6`.
  두 콜라이더 윗면이 **같은 평면**이라 위 건축 규칙 5번의 여유가 필요하다.
- **날씨 실내 차폐**(`WeatherSystem.cs:86-100`): 지붕 아래에서 빗줄기·물튀김만 0으로 줄인다
  (`indoorCheckInterval 0.25` · `indoorFadeSeconds 0.8`). **광량·안개와 `IsRaining` 은 실내에서도
  그대로다** — 연출만이다. 판정은 `BuildingSystem.IsInsideEnclosedStructure`(`:2925`) + `Shelter.IsInsideHome`.
- **타이틀 화면**(`MainMenuController.cs`): `Resources/UI/MainMenu_Background` 를 aspect-fill로 깔고
  제목("무인도 탈출")·부제를 얹는다. 시작 시 `isMenuOpen 1`(씬).
- **퀘스트 창 분리**: 판정은 `QuestSystem`(폴링 0.5초 · unscaled), 표시는 `QuestUI`(**J**). 묶음 순서는
  **생존 → 정착 → 항해**(항해가 맨 위에 오지 않는다 — 현재 방향). 정착·항해만 완료 래치가 있다.

### 키 배정 (전수)
`E` 상호작용/상자 열기 · `R` 조리(+게임오버 재시작) · `C` 섭취 · `G` 설치 · `F` 인벤 필터 ·
`Tab` 인벤토리 · `V` 제작 · `J` 퀘스트 · `M` 지도(`+`/`-` 확대) · `B` 건축 · `Q` 조각 회전 ·
`Space` 엔딩 계속 · `Esc` 설정 · `LeftCtrl` 잠수 · `Shift` 커서 해제/한 칸 전부 버리기/물통에 담기 ·
`F3` 디버그 HUD · `F4` 재료 지급 · `F5` 저장 · `F6/F7/F8` 엔딩·게임오버 미리보기 · `F9` 불러오기.

**0.2.2x~0.2.50에서 추가된 키 (전수 재대조 완료 — 이제 왼손 글자 키는 전부 찼다)**
- `X` **회피 구르기** (`PlayerController.dodgeKey`, 코드 기본값 · 씬 아님). 쿨다운 1.8초 · 속도 13m/s ·
  0.28초 → 약 3.6m. 무적 프레임은 **없고** 대신 직후 0.22초 경직(속도 ×0.35)이 대가다.
  허기 1.5 / 갈증 2 소모(RaftSailing 노 젓기와 같은 대가 규약).
- `T` **또는 마우스 우클릭** — **창 투척** (`throwKey` / `throwMouseButton 1`, 둘 다 코드 기본값).
  쿨다운 0.8초 · 초속 26m/s · 수중이면 ×0.5. `throwMouseButton`을 음수로 두면 마우스 투척만 꺼진다.
- `Z` **낚시** (`fishingKey`). 캐스팅 → 입질 대기 → 챔질을 이 키 하나로 진행한다.
  전수 대조 결과 **왼손이 닿는 빈 글자 키는 Z가 마지막이었다**(`PlayerController.cs:70-77`의 대조표).
- `F10` 전체 지도 토글 · `F11` 치트 일괄 토글 (`DebugHud.cs:88,114`, 디버그 계열).

**새 키를 만들기 전에 이 표를 확인해라.** 남는 키가 거의 없다.
→ 위 세 키(X·T·Z)는 전부 **씬이 아니라 코드 기본값**이다. 씬에 직렬화되지 않았으므로 0장의
"씬 값이 이긴다"가 적용되지 않는다 — 코드를 고치면 그대로 반영된다.

### 엔딩
배: 3단계 누적 + 도면 3장 + 비상식량 12 · 생수 12 · 연료 1 + **경과 15일**(씬)
비행기: 엔진부품 2 · 금속조각 6 · 연료 3 · 노끈 4 + **경과 8일**(씬 `aircraftRequiredElapsedDays 8`)
동시 성립 시 **배 엔딩이 이긴다(확정, 의도)** — `GetEndingPriority()`(Boat 2 / Aircraft 1).
두 검사의 코드 순서를 뒤바꿔도 결과가 안 뒤집힌다.

### 도구 내구도 (`ScriptableObjects/Item_*.asset`)
칼 **-1(무한)** · 물통 -1 · 파이어스타터 -1 · 창 15 · 손도끼 20 · 라이터 5 · 나머지 전부 1
`ItemData.IsUnlimited => maxUses < 0`

### 밸런스 SO
`Assets/_Project/Resources/SurvivalBalanceConfig.asset` (guid `80e0187dae8f4309843ba9454f977b28`)
**코드상 8개**가 참조하지만 **씬에서 실제로 배선된 것은 4개뿐**이다.
- 씬 배선됨: `SurvivalStats` / `HazardSpawner` / `SurvivalClock` / `EndingChecker`
- 미배선: `WeatherSystem` / `Campfire` / `WaterStill` — 씬에 인스턴스가 없어 `Active`(Resources 자동 로드)로 받는다
- **`ConsumptionSystem` 은 씬에서 `balanceConfig: {fileID: 0}` 이고 `useConfigPoisonChance: 0` 이다.
  폴백이 `Active` 를 집어 오더라도 이 토글이 false면 config의 `rawFoodPoisonChance` 는 읽히지 않는다**
  → 실제로 쓰이는 값은 씬의 `rawFoodPoisonChance 0.15` 다.

> **"config만 고치면 반영된다"고 가정하지 마라.** 이 프로젝트 사고의 대표 유형이다(0장 표 참고).

폴백 규칙: 필드가 미설정일 때만 config 값으로 채운다(씬 값이 이긴다).
0이 유효값인 필드(`requiredElapsedDays` 등)는 `<=0` 이 아니라 **`<0`** 으로 판정한다.

### 세이브
파일은 `makegame_save.json` + **`.bak`(직전 저장본)** 2개다. F5는 `.tmp` 에 쓰고 원자적으로 교체하며
직전 저장본을 `.bak` 으로 남긴다. F9는 본 파일이 깨져 있으면 **`.bak` 으로 자동 폴백**한다.
`.bak`/`.tmp` 를 잔여물로 오해해서 지우는 코드를 넣지 마라 — 세이브 복구 경로다.
`JsonUtility` 는 없는 필드를 기본값으로 채우므로 **`SaveData` 필드 추가는 안전하지만 제거·개명은 파괴적**이다.
건축은 `buildStructureJson`(문자열) + `storageChests`(`ChestSaveEntry`: space/cell/level/pos/yaw/tier/items).

**세이브 슬롯 3개**(0.2.4x~): 저장 위치가 슬롯 1~3으로 늘었다. **슬롯 1 = 기존 `makegame_save.json`
그 자체**라 마이그레이션 코드가 없고 옛 진행이 그대로 이어진다. 슬롯 2·3만 `makegame_save_slot{N}.json`.
현재 슬롯의 주인은 `GameSettings.SaveSlot`(`SaveLoadController.SlotCount = GameSettings.SaveSlotCount`)
이고 F5/F9·타이틀 "이어하기"는 **언제나 현재 슬롯**에 작용한다. `.bak`/`.tmp` 규약은 슬롯마다 그대로다.
**파일 내용 형식(SaveData 스키마)은 한 글자도 바뀌지 않았다.**

### 아이템 수 (레지스트리 전수 = 단일 소스)
**현재 57종.** 근거: `Assets/_Project/Resources/ItemDataRegistry.asset` 의 `allItems` 항목 57개 =
`Assets/_Project/ScriptableObjects/Item_*.asset` 파일 57개(두 수가 일치 = 미등록 누락 0).

> ⚠️ **소스 주석의 아이템 수는 낡았다.** `ItemDataRegistry.cs`(31개) · `ItemData.cs`(32개 .asset) ·
> `CompassUI.cs`(51종)가 각각 다른 옛 숫자를 들고 있다. **수를 셀 때는 주석이 아니라 레지스트리
> 에셋을 세라**(0장 "주석이 고쳤다고 주장하면 값을 직접 확인한다"의 전형이다).

### 설치형 시설 (설치 키트, `ItemData.isPlaceable`)
`isPlaceable: 1` 인 키트 아이템은 **8종**이다 — 모닥불 · 쉼터 · 물 증류기 · **제작대 · 용광로 ·
베틀 · 훈연기 · 밭**. `G` 키로 설치한다.

| 시설 | 세이브 경로 |
|---|---|
| 모닥불 / 쉼터 / 물 증류기 | `StructureType` 0 / 1 / 2 (기존) |
| **제작대 / 용광로 / 베틀** | `StructureType` **3 / 4 / 5** (`CraftStationKind` ↔ 변환, 상태 없음 = type만 저장) |
| **훈연기** | `StructureType`을 늘리지 않고 **`SaveData.smokers` 별도 목록**(`SmokerSaveEntry`) |
| **밭** | 같은 이유로 **`SaveData.farmPlots` 별도 목록**(`FarmPlotSaveEntry`: 작물·성장도) |

> ⚠️ 훈연기는 **Campfire 컴포넌트를 함께 달고 있다.** `SaveLoadController`가 모닥불을 훑을 때
> `GetComponent<Smoker>() != null` 로 걸러내지 않으면 한 대가 모닥불로도 훈연기로도 두 번 저장돼
> 불러올 때마다 유령이 늘어난다(`SaveLoadController.cs:754,926`). 이 검사를 지우지 마라.
> 훈연기·밭 목록은 `Smoker.Active` / `FarmPlot.Active` 로 모은다 — `FindObjectsByType`으로
> 되돌리면 **DontDestroyOnLoad 설치 템플릿까지 잡혀** 저장 때마다 한 대씩 늘어난다(`:1150-1158`).

### 0.2.2x~0.2.50 신규 시스템 (한 줄씩)
- **엔드게임 보스 3종 + 트로피 엔딩** — `BossKind` = 거대 상어 0 / 대왕 곰치 1 / 심해 괴수 2
  (`BossSpawner` 배치, 전부 수중). 보스는 **자체 체력계를 만들지 않고 `HazardSource`를 얹는다** —
  이 게임에서 때릴 수 있는 것은 `HazardSource`뿐이기 때문이다(E키 근접 · 투척 창 둘 다 그걸 본다).
  트로피 3종(상어 이빨 · 곰치 턱뼈 · 촉수 표본)을 다 모으고 **배/비행기 탈출 중 하나까지 완성**하면
  `EndingKind.Trophy`(제목 "정복"). **`EndingKind`는 맨 끝에만 추가한다.**
- **음식 부패** — `FoodSpoilage`(정적 단일 판정처). 3단계 = 신선 / 상하기 시작(신선도 <0.5) /
  부패(<0.15). 허기 회복 배율 **1.0 / 0.6 / 0.25**, 식중독 확률 **가산** +0.10 / +0.50.
  기본 수명은 생음식 1일 · 익힌 음식 3일이고 **비상식량과 "훈제"로 시작하는 것은 안 상한다**
  (`ItemData.spoilDays`: 0 = 자동 규칙 / 양수 = 그 값 / **음수 = 절대 안 상함**).
  섭취는 같은 종류 중 **가장 상한 것부터(FIFO)** 소모된다 — 칸 표시와 실제가 갈리지 않게 하기 위함.
- **낚시** — `FishingSystem`(Z키). 캐스팅 → 입질 대기 → 챔질. 낚싯대 내구도 1 + 미끼 1 소모,
  성급한 챔질은 실패. 성공 시 사냥 스킬 경험치. **기존 E키 물고기 사냥(`HuntableCreature`)은
  한 줄도 바뀌지 않았다** — 낚시는 대체가 아니라 추가 선택지다.
  **씬에 없다** — `PlayerController.Awake`가 런타임에 `AddComponent`한다 → **코드 기본값이 곧 실동작값.**
- **농사** — `FarmPlot`(밭키트 설치). `FarmCropKind` = 야자 묘목 0(3일) / 해조류 1(1.5일) / 약초 2(1일).
  **정수 그대로 세이브에 들어간다 — 순서 변경·중간 삽입 금지.**
- **난파선** — `ShipwreckSpawner`. 섬마다 0~2척이 해저 스커트 수심 5~16m에 흩어지고, 한 척에
  수거 지점 2~4곳. `SeabedGenerator.Build`가 해저 스포너 3종 **직후** 같은 동기 흐름에서 부른다.
  신규 모델 0개(기존 뗏목/화물 메시 재조합). **전용 rng 스트림**(salt `0xB0A7`)만 소비하므로
  같은 worldSeed의 기존 월드 배치가 밀리지 않는다.
- **나침반** — `CompassUI`. 화면 상단 띠형(N/E/S/W + 도수 + 발견한 섬 방향 표식), sortOrder 7.
  **아이템을 요구하지 않는다**(Item_나침반 에셋이 없어서 내린 판단). 설정에서 끌 수 있다.
- **지도 표식(카토그래피)** — `MinimapUI.IslandMark` = None 0 / 고갈됨 1 / 자원 있음 2 / 위험 3.
  전체 지도의 섬 줄에서 버튼으로 순환한다. **정수 그대로 세이브(`IslandMarkSaveEntry`)에 들어간다 —
  맨 끝에만 추가.** 저장소가 `static`인 이유는 `RegenerateWorld`가 섬 인스턴스를 통째로 갈기 때문이다.

### 스킬·난이도 효과 배선 (0.2.50 — "노출만 돼 있던" 3건을 닫았다)
수치의 단일 소스는 `PlayerSkills` / `GameSettings`이고, **적용만** 각 파일에서 한다.
- **요리** `GetCookingRestoreMultiplier()`(Lv1 1.0 → Lv10 1.27) → `ConsumptionSystem.Consume`.
  **익힌 음식(`isRawFood == false`)에만** 곱하고, 부패 배율과 **곱셈으로 합성**한다(축이 달라 이중
  적용이 아니다). 갈증(음료)에는 적용하지 않는다.
- **신체(이동)** `GetPhysicalMoveSpeedMultiplier()`(Lv10 1.09) → `PlayerController.HandleMove`의
  지상 기본 속도. 골절 감속·구르기 경직·달리기 배율이 그 위에서 계산된다. **수영·구르기 대시는 제외.**
- **신체(산소)** `GetPhysicalOxygenDrainMultiplier()`(Lv10 0.82, 하한 0.5) → `SurvivalStats.
  oxygenDrainMultiplier`. **그 필드의 단일 소스는 여전히 `PlayerController`다** — 컨트롤러가
  `산소통 배율 × 신체 배율`로 합성해 밀어 넣는다(덮어쓰면 산소통 효과가 사라진다).
  **`PlayerSkills`에는 레벨업 이벤트가 없어** 인벤토리 변경뿐 아니라 `Update`에서도 다시 민다.
- **난이도 위협 피해** `GameSettings.ThreatDamageMultiplier`(쉬움 0.7 / 보통 1.0 / 어려움 1.4) →
  `SurvivalStats.TakeDamage`. **전투 원인(`Predator` · `SharkAttack`)에만** 건다. 굶주림·갈증·일사병·
  중독·출혈·익사에는 걸지 않는다 — 굶주림/갈증은 이미 `HungerDrainMultiplier`/`ThirstDrainMultiplier`로
  난이도가 걸려 있어 **같은 손잡이가 두 번 먹는다**. `Unknown`(기본 인자)도 1.0이다.
  디버그 무적(`debugInfiniteHealth`)보다 **뒤**에 둔다 — 무적 중에는 사인·위기 통계가 오염되면 안 된다.

> **세 배선 모두 Lv1 / 보통 난이도에서 배율이 정확히 `1f`라 예전 식과 비트 단위로 같다(회귀 0).**
> 배율을 새로 만들 때 이 성질(레벨 1 = 1.0)을 깨지 마라.

---

## 4. 알려진 함정

### ★ 오늘(v0.01.125 직전) 실제로 사고가 난 5가지 — 소스만 봐서는 유도할 수 없다 ★

1. **Unity 6.5 API.**
   - `GetInstanceID()` 는 **CS0619(에러)** 다. 쓰지 마라(`CameraShake.cs:90` 참고).
   - `FindObjectsByType` 은 **1인자 형태만** 쓴다 — `FindObjectsByType<T>(FindObjectsInactive.Include)`.
     `FindObjectsSortMode` 를 받는 오버로드는 **CS0618**이다. **2인자도 안 된다. 오늘 실제로 걸렸다.**

2. **도메인 리로드는 직렬화 가능한 필드만 복원한다.**
   플레이 중 스크립트가 재컴파일되면 `bool` 같은 플래그는 살아남지만 `System.Random` 처럼 직렬화
   불가능한 필드는 **null로 돌아온다.** "플래그가 true면 이 참조는 반드시 있다"는 전제가 그대로 깨진다.
   정적 검사로는 절대 안 잡힌다 — 실제로 새끼 곰 AI가 매 프레임 예외를 던져 콘솔이 999개를 넘겼다.
   **초기화 플래그로 참조의 존재를 보증하지 마라. 언제든 다시 만들 수 있게 짜라.**
   `static` 목록도 리로드에서 비워지는데 **이미 활성인 오브젝트의 `OnEnable` 은 다시 불리지 않는다.**

3. **MonoBehaviour 필드 초기자에서 `Resources.Load` 금지.**
   필드 초기자는 생성자에서 돌고 Unity는 그 시점의 `Load` 를 막고 null을 돌려준다. 그 null을
   "에셋 없음"으로 확정하면 **세션 내내 에셋이 안 쓰인다**(실제로 곰 모델이 그렇게 죽었다).
   **프로브 실패를 영구히 캐시하지 마라.** 현재 해법은 프레임당 한 번씩 다시 살피는 것이다
   (`CreatureVisualBuilder.cs:153-172`).

4. **★ 씬 텍스트에 키가 없어도 Unity는 값을 들고 있다 ★ — 오늘 최대 교훈이다.**
   `SampleScene.unity` 를 grep해서 `slotCapacity` 가 없길래 "씬이 안 덮어쓴다"고 판단했는데,
   **Inspector에는 30이 떠 있었고** 코드 상수 100이 영원히 무시되고 있었다. 스크립트에 필드가 추가된 뒤
   **씬을 저장하기 전까지는 텍스트에 안 적히지만 Unity는 값을 갖고 있다.**
   → **"씬 텍스트에 없음"을 "씬이 안 덮어씀"으로 읽지 마라.**
   확실히 알려면 Inspector를 봐야 하고, **그건 디렉터만 할 수 있다.**
   씬 직렬화가 걸린 값은 에이전트가 단정하지 말고 `[확인요청]` 으로 디렉터에게 확인을 요청해라.

5. **파일 소유권 락은 디렉터가 손으로 쓴다 — 틀릴 수 있다.**
   오늘 상자 작업에서 두 에이전트 모두에게 `BuildMenuUI.cs` 를 금지시켰고, 그래서 아무도 메뉴 슬롯을
   추가하지 않았다. **기능은 전부 됐는데 버튼만 없었다.**
   → 네 락 목록에 **"이 작업을 끝내려면 필요한데 금지된 파일"** 이 있으면 **조용히 우회하지 말고 보고해라.**

### ★ 0.2.13~0.2.26에서 추가로 사고가 난 3가지 ★

6. **Unity 6.5 OBJ 임포터는 서브메시를 `o`가 아니라 머티리얼 단위로 만든다.**
   `o` 오브젝트가 5개라도 usemtl이 없으면 **"default" 메시 1장·서브메시 1개**로 병합된다
   (여객기가 통째로 회색이던 사고). usemtl을 넣어도 **mtllib가 가리키는 실제 .mtl에서
   해석되지 않으면 무시**된다(sub1 재발 사고). → 파이프라인은 mtllib+최소 .mtl을 동봉한다
   (`Tools/blender/units/coral.py inject_usemtl` 이 표준). 로더는 병합(서브메시 N +
   `sharedMaterials` 배열)과 개별 메시 양쪽을 지원해야 한다(`AirlinerWreck.cs` 참고).

7. **`Resources.LoadAll<Mesh>(모델 경로)` 는 이 프로젝트 모델에서 빈 배열이다.**
   메시를 꺼내는 검증된 경로는 `Resources.Load<GameObject>` + `GetComponentsInChildren<MeshFilter>`
   뿐이다(`ResourceVisualLibrary.TryLoadTwoPartModel`). 다른 API로 "안 로드된다"고 결론내리지 마라.

8. **(디렉터 전용) 마운트 git 우회에서 `cp -r .git` 직후의 인덱스는 낡아 있다.**
   `git add -A` 는 전체 재구축이라 가려지지만, **pathspec add 후 커밋하면 낡은 인덱스가
   그대로 기록돼 대량 삭제 커밋이 된다**(ff26f69 사고 - soft reset으로 폐기).
   → pathspec add 전에 반드시 `git read-tree HEAD`.

### 그 밖의 상시 함정

- **`Time.timeScale = 0`**: `EndingChecker.TriggerEnding` 과 `GameOverController` 가 즉시 건다.
  엔딩·사망 화면의 모든 연출은 **`unscaledDeltaTime` / `WaitForSecondsRealtime`** 이어야 한다.
  `Time.deltaTime` 을 하나라도 쓰면 연출 전체가 첫 프레임에서 멈춘다(실제 버그였다).
  `InvokeRepeating` 도 timeScale 0에서 죽는다(볼륨 슬라이더 전례).
- **눕힌 몸통의 로컬 축**: 일부 생물은 `rotationEuler(0,0,90)` 으로 누워 있어 **로컬 +X가 월드 위쪽**이다.
  이 착각으로 상어 지느러미·독사 혀·전갈 집게·함정 가시·식인종 창이 전부 엉뚱한 곳에 붙어 있었다.
  파츠를 붙이기 전에 부모 회전을 확인하고 `CreateUprightPivot()` 을 써라.
- **`Physics.autoSyncTransforms` 는 기본 false**: 방금 만든 콜라이더에 바로 레이캐스트하면 안 맞는다.
  `Physics.SyncTransforms()` 를 먼저 불러라(초목이 전부 해수면에 깔린 원인이었다).
- **`TerrainSampler.SnapToGround` 는 이름이 `"Island_"` 로 시작하는 콜라이더만 지형으로 인정한다**
  (`BuildingSystem` 도 같은 규칙). 초목·장식물에 콜라이더를 붙이지 마라 — 배치 높이 계산이 깨진다.
- **`Destroy()` 는 프레임 끝까지 지연된다.** 즉시 물리에서 빼려면 `SetActive(false)` 를 먼저 불러라.
- **`GameObject.CreatePrimitive` 는 콜라이더를 자동으로 붙인다.** 시각 전용 파츠는 제거해야 한다.
- **스크립트 실행 순서가 지정돼 있지 않다.** 씬 컴포넌트와 런타임 생성 컴포넌트가 같은 프레임에 상태를
  주고받으면 실행마다 결과가 갈린다(엔딩 건너뛰기 통지가 한 프레임 지연으로 해결됐다).
- **머티리얼을 파츠마다 만들지 마라.** 섬 하나에 400개가 넘으면 SRP 배처가 죽는다. 공유해라.

---

## 5. 아트 기준

`Docs/ArtDirection.md` 가 원본. 요약:
- 팔레트 10색. **Medic Green(#4FA87A)은 UI/아이콘 전용**, **Frond Green(#6BA83F) / Meadow Green(#8AA84F)은 월드 3D 표면 전용.** 용도를 섞지 마라.
- 형태로 구분한다. 색만 바꾸는 건 색맹 대응도 안 되고 야간엔 무의미하다.
- 3단계 HUD 정보 위계 / 3단계 피드백 강도 / UI 스타일 규칙.

---

## 6. 보고서 형식

```
[완료] 항목: 한두 줄 (수치·판단 근거 포함)
[발견] 예상과 달랐던 것 (근거: 파일:줄)
[요청] <대상>: <내용>          ← 시그니처·YAML 키는 정확히
[확인요청] <내용>              ← 근거가 약한 것, 씬 직렬화가 걸린 값은 전부 여기로
[막힘] <파일>: <왜 필요한가>   ← 작업 완료에 필요한데 락에 걸린 파일 (필수 · 4장 5번)
```

**결론만 써라. 유도 과정·대안 비교표 금지.** 코드 전문을 붙여넣지 마라. 짧게.
**추측을 "발견"으로 올리지 마라.** 코드를 읽고 근거를 댈 수 있는 것만.
**작업을 끝내려면 필요한데 금지된 파일이 있으면 `[막힘]` 으로 반드시 보고해라.** 조용히 우회하면
"기능은 다 됐는데 버튼이 없는" 사고가 그대로 반복된다.

---

## 에이전트 비용 규율 (0.2.52 개정 — 모든 작업 에이전트 지시문에 적용)

**왜**: 실측 결과 도구 결과로 컨텍스트에 들어간 원본은 7.2M 토큰인데, 그것이 이후 턴에서
**502M 토큰으로 다시 읽혔다(평균 70배)**. 비용 = (결과 크기) x (그 뒤 남은 턴 수) 이다.
한 번 컨텍스트에 들어간 것은 에이전트가 죽을 때까지 매 턴 다시 처리된다.
그러니 **들어가는 양**과 **머무는 시간** 둘 다 줄여야 한다.

### 1. Bash는 증거가 아니라 답만 출력한다 (효과 최대 — 재읽기의 58%가 Bash다)

성공을 확인하는 명령이 성공 내용을 통째로 쏟아내면 그 쓰레기가 남은 모든 턴에 얹힌다.

- 검사류는 결과만: `mcs --parse ... 2>&1 | tail -3`, `... | wc -l`, `... && echo OK || echo FAIL`
- grep은 개수나 첫 몇 줄만: `grep -c`, `grep -m3`, `head -20`
- 목록은 세어서 보고: `ls ... | wc -l` (전체 목록이 실제로 필요할 때만 나열)
- 이미 아는 것을 다시 출력하지 않는다(방금 쓴 파일을 cat 하지 말 것)

### 2. 파일을 통째로 읽지 않는다 (재읽기의 39%가 Read다)

Read 1회 평균 8,951자 = 약 2,200토큰이 영구 잔류한다. 실제로 필요한 건 대개 함수 하나다.

- 구간만: `sed -n '400,460p' 파일`
- 심볼 주변만: `grep -n -A6 -B2 "메서드명" 파일`
- Read는 **Edit 직전 필수인 경우에만**, 그때도 고칠 구간만
- 편집한 파일을 검증하려고 다시 읽지 않는다(Edit이 실패하면 오류를 낸다)

### 3. API 확인은 grep으로 (Read 금지)

`Docs/API_REFERENCE.md`는 610줄 약 9K 토큰이다. **통째로 읽으면 아끼려던 것보다 비싸다.**

- `grep -n -A4 "PlayerInventory.AddItem" Docs/API_REFERENCE.md`
- 파일 절 전체가 필요하면 `sed -n '/^## Systems\/CraftStation/,/^## /p'`
- 사전에 없는 심볼만 원본에서 grep하고, 쓰게 된 심볼은 사전에 **추가**한다

### 4. 큰 파일 훑기는 하위 에이전트에 맡긴다 (컨텍스트 격리)

**적용 조건**: 800줄 넘는 파일을 이해해야 하거나, 구조 파악을 위해 큰 파일 3개 이상을 봐야 할 때.
(작은 확인까지 위임하면 지연만 늘어난다 — 그 경우는 grep으로 직접 해결한다.)

- 하위 에이전트에게 "이 파일에서 X를 찾아 결론 3~5줄로 보고"를 시킨다
- 원본은 하위 에이전트와 함께 사라지고, 부모는 결론만 떠안는다
- 2,225줄짜리 파일 하나 기준 약 15배 차이가 난다

### 5. 독립적인 호출은 한 턴에 몰아친다

턴이 하나 줄면 그 시점까지의 컨텍스트 전체를 한 번 덜 읽는다.
서로 결과에 의존하지 않는 bash/grep/read는 **한 메시지에 병렬로** 낸다.
턴 25% 감소 = 비용 약 40% 감소(제곱으로 준다).

### 6. 과업 분할

에이전트 하나는 30~40턴 안에 끝나는 크기로 자른다. 파일 5개 이상을 고치는 과업은 쪼개서 순차 투입.
길어질 것 같으면 중간 결과를 파일에 적고, **다음 단계는 새 에이전트가 그 파일만 읽고** 이어받는다.

### 7. 검증은 `Tools/check.sh` 한 번 (성공하면 출력 30자)

과업이 끝났을 때 **`Tools/check.sh` 딱 한 번**. 정적 게이트 + 구문 파싱 + 금지 API + 버전 일치를
한 번에 돌고, 성공하면 `CHECK OK (133 files, v0.2.51)` 한 줄만 나온다(실패해야 원인이 나온다).
- 파일마다 `mcs --parse`를 돌리지 마라. 게이트·grep을 따로 돌리지도 마라 — 4턴이 1턴이 된다.
- 전수 적대적 검증은 디렉터가 별도 웨이브로 돌린다.
- 보고는 지시문이 요구한 항목만. 과정 서술·표 남발 금지.

### 8. 기계적 변환은 스크립트 1회 실행 (효과 최대 — 총 턴 수 직격)

실측: 도구 호출 9,700회에 턴이 16,657회, 그중 Edit이 2,054회였다. 고정 프리픽스(시스템 프롬프트 +
도구 스키마 24.4K)가 **매 턴** 다시 읽히므로, 재읽기의 45%가 순전히 턴 수 때문에 발생한다.
**턴을 줄이는 것이 컨텍스트를 줄이는 것보다 크다.**

같은 패턴을 여러 파일에 적용하는 일 — 훅 삽입, 일괄 치환, 표 추출, 에셋 생성, 배열 확장 —
은 파일마다 read→edit로 돌지 말고 **파이썬/awk 스크립트를 한 번 써서 한 번 돌려라**.

- 파일 14개에 리셋 훅 추가: read→edit 30~50턴 → **스크립트 3~4턴**
- 모델이 파일 내용을 볼 필요조차 없는 경우가 많다(grep으로 앵커만 확인하고 치환)
- 스크립트는 반드시 **치환 건수를 세어 출력**하고(`assert s.count(old)==1`), 0건이면 실패시켜라 —
  조용히 안 먹은 치환이 이 프로젝트 최악의 사고 유형이다
- 실적: API_REFERENCE.md(610줄, 31파일 480심볼)를 awk 1패스로 **8턴**에 만들었다(중앙값 78턴)

지연도 함께 줄어든다 — 턴이 곧 왕복이다. 스크립트 작성이 부담되는 **일회성 편집에만** 직접 Edit을 쓴다.
