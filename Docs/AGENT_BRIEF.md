# 에이전트 필독 브리핑 (단일 진실 파일)

> 갱신: v0.01.093 · **커밋할 때마다 디렉터가 갱신한다.**
> 이 파일 하나만 읽고 시작하면 된다. 다른 문서보다 이 파일이 항상 최신이다.
> 여기 적힌 값과 다른 것을 발견하면 **먼저 이 파일을 의심하지 말고 씬 YAML을 파싱해서 확인한 뒤**
> 보고서에 `[요청] 디렉터: AGENT_BRIEF 갱신` 으로 올려라.

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

**규칙: 수치를 논할 때는 (1) 코드 기본값 (2) 씬 직렬화 값 (3) 프리팹 오버라이드 세 곳을 모두 확인한다.**
**규칙: 주석이 "고쳤다"고 주장하면 값을 직접 확인한다. 이 프로젝트의 주석은 여러 번 거짓말했다.**

---

## 1. 프로젝트 형태

- Unity 6 (6000.5.6f1), URP, 단일 씬(`Assets/Scenes/SampleScene.unity`)
- **3D 모델 에셋 0개.** 전부 `GameObject.CreatePrimitive` / 내장 메시 조합으로 런타임 생성한다.
  로우폴리·스타일라이즈드가 유일하게 가능한 아트 방향이다.
- 오디오도 `ProceduralAudioClipGenerator` 절차 생성. 외부 오디오 파일 없음.
- UI도 전부 코드 생성(`UIBuilder.cs` 공통 팩토리). 프리팹 UI 없음.
- `.asmdef` 없음 → 전부 `Assembly-CSharp` 단일 어셈블리(순환 참조 걱정 불필요).

---

## 2. 절대 금지 (전 역할 공통)

1. **소유 파일 밖 편집 금지.** 매 웨이브 3~4명이 병렬 편집한다. 남의 파일을 건드리면 덮어쓰기 사고다.
2. **public 필드 제거·개명·타입변경 금지.** 씬 직렬화 값이 조용히 사라진다.
3. **`.unity` / `.prefab` / `.asset` / `.meta` 편집 금지.** 디렉터가 직렬로 처리한다.
   → `[요청] 디렉터:` 로 **파일 · 필드(YAML 키 정확히) · 현재값 → 새값** 을 올려라.
4. **`OnGUI`(IMGUI) 추가 금지.** sortOrder와 무관하게 Canvas 위에 덮어 그려진다.
   실제로 이것 때문에 GameOverUI가 통째로 안 보인 적이 있다. 현재 프로젝트 전체 `OnGUI` = 0개.
5. **아직 없는 API를 호출하지 마라.** 컴파일이 깨지면 전원이 막힌다.
   필요하면 `[요청] <대상>:` 으로 정확한 시그니처를 적어라.
6. **`UnityEngine.Random` 전역 사용 금지.** 섬별 시드 `System.Random` 스트림을 쓴다(재현성).
7. **에이전트끼리 직접 대화 금지.** `[요청] <대상>: <내용>` 으로 보고서에 적으면 디렉터가 중계한다.
8. **"스테이징에 없다 ≠ 프로젝트에 없다."** 파일이 안 보여도 없다고 단정하지 말고
   `[확인요청]` 으로 올려라. 새로 만들어 덮어쓰지 마라.
9. **컴파일 가능한 코드만.** 세미콜론 2개가 빠져 커밋 2개가 빌드 깨진 채 올라간 전력이 있다.
   특히 `UIBuilder.CreatePanel(...)` 같은 다중 줄 호출의 닫는 괄호·세미콜론을 눈으로 재확인해라.

---

## 3. 씬 실측값 (v0.01.093 기준)

### 생존 수치 (SurvivalStats)
health/maxHealth 100 · hunger/thirst 시작 100
`hungerDecayPerSecond 0.05` · `thirstDecayPerSecond 0.08` · `starvationDamagePerSecond 1`
`sunstrokeGainPerSecond 0.25`(← 0.1에서 상향, 이전 값으로는 일사병 100 도달이 수학적으로 불가능했다)
`sunstrokeRecovery 0.2` · `sunstrokeDamage 1.0`(← 0.5는 자연회복 0.5와 정확히 상쇄돼 일사병이 위협이 아니었다) · `poisonDamage 0.8` · `bleedingDamage 1.2`
`healthRegenThreshold 50` · `healthRegenPerSecond 0.5`
`oxygenRecovery 25` · `oxygenDrain 5` · `drowningDamage 3`

### ⚠️ 씬에 **없는** 컴포넌트 (이 절에서 가장 자주 오판하는 지점)
`RuntimeInitializeOnLoadMethod` 로 매 씬 로드마다 스스로 생성되는 것들은 **씬에 인스턴스가 없다.**
즉 **코드 기본값이 유일한 소스**이고, "씬 값이 이긴다"는 대원칙이 적용되지 않는다.
`DayNightCycle` · `WeatherSystem` · `Campfire` · `WaterStill` · `SurvivalHudUI` · `EndingUI` ·
`GameOverUI` · `CombatFeedbackUI` · `InteractionPromptUI`
→ 이들의 값을 바꾸려면 **코드를 고쳐야 한다. 씬을 뒤져도 없다.**

### 시간 / 월드
`SurvivalClock.secondsPerDay 600` (실시간 10분 = 게임 1일)
새 게임은 `DayNightCycle.newGameStartTimeOfDay 0.3`(아침)에 시작한다. **(씬 아님 — 코드 기본값)**
**0(자정)이면 게임 시작 2분 30초가 새까맣다** — 예전에 "검은 하늘 버그"로 신고됐던 것이 이것이다.
`WorldMapManager`: `baseDistanceStep 1200` · `oceanSize 40000` · `terrainMaxHeight 8` · `worldSeed 0`
`initialIslandCount 8` (+ 시작 섬 1 = **총 9개**)
플레이어 시작 위치 `(0, 14, 0)` — terrainMaxHeight 8보다 높아야 한다. **5로 두면 지형 안에서 스폰돼 바다로 떨어진다.**

### 섬 규모 (`IslandSizeMetrics`, 코드가 단일 소스 — 씬 값 없음)
지형 반지름 Small 50 / Medium 90 / Large 140 / ExtraLarge 200
`IslandSize` enum = 0 / 1 / 2 / 3

### 스포너
| 컴포넌트 | 배율 (S/M/L/XL) | 산포 반경 |
|---|---|---|
| IslandResourceSpawner | 1 / 2 / 3 / 4 (선형 — 의도된 것) | 40 / 72 / 112 / 160 |
| HazardSpawner | 1 / 1.75 / 2.5 / 3.25 | 40 / 72 / 112 / 160 |
| CreatureSpawner | 1 / 1.5 / 2 / 2.5 | 40 / 72 / 112 / 160 |

> ⚠️ **HazardSpawner에서 "배율 = 마릿수"가 아니다.** 마릿수는 면적 밀도로 정해진다:
> `마릿수 = round(hazardsPerTenThousandSquareMeters(2.0) × π·r²/10,000 × 규모트림)`, 최소 1,
> 상한 `maxHazardsPerIsland`(20, 0 이하면 상수 20). 배율은 종류 가중치 트림에 쓰인다.
> 실측 마릿수 — 소형 1 / 중형 3 / 대형 8 / 특대 16.
> 예전에는 엔트리 6종의 확률을 한 번씩 굴려 **섬당 최대 6마리**였고, 그래서 큰 섬일수록
> 밀도가 옅어졌다(특대가 소형의 1/5). 지금은 실현 밀도가 규모와 무관하게 약 2.0으로 일정하다.

산포 반경은 지형 반지름의 80%다. **씬의 낡은 `scatterRadius` 단일 키는 제거됐다.**
자원 배율을 면적비(1:3.24:7.84:16)로 올리자는 주석이 있는데 **일부러 선형을 유지한다** —
면적비면 특대 섬 노드가 96 → 380개가 된다. 큰 섬은 "빽빽한 곳"이 아니라 "특별한 자원이 있는 곳"이다.

### 자원 노드 (씬 `resourceEntries`, 13종)
나뭇가지 4 · 돌조각 3 · 야자잎 3 · 코코넛 2 · 대나무 2 · 천조각 1 · 부싯돌 1 ·
금속조각 2(**대형+ & 손도끼 필요**) · 부력통 2(대형+) · 비상식량 1 · 연료 1 ·
엔진부품 2(**특대 전용**) · 생수 1(중형+)
야자잎에 `bonusTool: 칼` / `bonusYieldPerHarvest: 1` (요구가 아니라 가산 — 칼이 없어도 채집된다)

> ⚠️ **`resourceEntries` 순서를 바꾸거나 중간에 끼워 넣지 마라.**
> `spawnOrder` 는 **엔트리 인덱스가 아니라 실제로 스폰된 노드의 러닝 카운터**다.
> `island.size < minimumIslandSize` 인 엔트리는 통째로 건너뛰므로 섬 규모마다 매핑이 다르다
> (즉 "N번 엔트리 = spawnOrder N" 이 아니다 — 이걸 오해하면 세이브를 깬다).
> `SaveLoadController` 가 `(islandIndex, spawnOrder)` 를 세이브 키로 쓰기 때문에,
> 중간 삽입 = 그 뒤 엔트리 전부의 번호가 밀림 = 기존 세이브의 채집 상태가 엉뚱한 노드에 복원된다.
> 추가는 **반드시 맨 끝에.** 시작 섬 착륙 원 3노드도 같은 이유로 루프 뒤에 붙어 있다.

### 엔딩
배: 3단계 누적 + 도면 3장 + 비상식량 12 · 생수 12 · 연료 1 + **경과 15일**
비행기: 엔진부품 2 · 금속조각 6 · 연료 3 · 노끈 4 + **경과 8일**
  (엔진부품이 특대 전용이라 배 1단계를 반드시 거친다. 8일 조건이 없던 시절에는 실제 플레이 길이가
   30분이 되어 30~90분 구간이 통째로 선택 사항이었다 — `aircraftRequiredElapsedDays = 8`, 코드 기본값.)
`EndingChecker.survivalClock` 연결됨 · `requiredElapsedDays 15` 동작 중
동시 성립 시 **배 엔딩이 이긴다(확정, 의도)** — `ResolveAchievableEnding()` + `GetEndingPriority()`(Boat 2 / Aircraft 1).
두 검사의 코드 순서를 뒤바꿔도 결과가 안 뒤집힌다. 배가 15일 + 3단계 + 비축까지 요구하는 더 긴 경로라 그쪽을 보여준다.

### 도구 내구도
칼 **-1(무한)** · 물통 -1 · 창 15 · 손도끼 20 · 라이터 5
`ItemData.IsUnlimited => maxUses < 0`

### 밸런스 SO
`Assets/_Project/Resources/SurvivalBalanceConfig.asset` (guid `80e0187dae8f4309843ba9454f977b28`)
**코드상 8개**가 참조하지만 **씬에서 실제로 배선된 것은 4개뿐**이다.
- 씬 배선됨: `SurvivalStats` / `HazardSpawner` / `SurvivalClock` / `EndingChecker`
- 미배선: `WeatherSystem` / `Campfire` / `WaterStill` — 씬에 인스턴스가 없어 `Active`(Resources 자동 로드)로 받는다
- **`ConsumptionSystem` 은 `balanceConfig` 가 비어 있고, 폴백도 `<=0` 이 아니라 `useConfigPoisonChance`
  명시 토글(기본 false)이다 → config의 `rawFoodPoisonChance` 는 현재 씬에서 한 번도 읽히지 않는다.**

> **"config만 고치면 반영된다"고 가정하지 마라.** 이 프로젝트 사고의 대표 유형이다(0장 표 참고).

폴백 규칙: 필드가 미설정일 때만 config 값으로 채운다(씬 값이 이긴다).
0이 유효값인 필드(`requiredElapsedDays` 등)는 `<=0` 이 아니라 **`<0`** 으로 판정한다.
런타임 생성 컴포넌트(WeatherSystem/Campfire/WaterStill)는 인스펙터 연결이 불가능해서
`SurvivalBalanceConfig.Active` (Resources 자동 로드)로 받는다.

### 세이브
파일은 `makegame_save.json` + **`.bak`(직전 저장본)** 2개다. F5는 `.tmp` 에 쓰고 원자적으로 교체하며
직전 저장본을 `.bak` 으로 남긴다. F9는 본 파일이 깨져 있으면 **`.bak` 으로 자동 폴백**한다.
`.bak`/`.tmp` 를 잔여물로 오해해서 지우는 코드를 넣지 마라 — 세이브 복구 경로다.
`JsonUtility` 는 없는 필드를 기본값으로 채우므로 **`SaveData` 필드 추가는 안전하지만 제거·개명은 파괴적**이다.

---

## 4. 알려진 함정

- **`Time.timeScale = 0`**: `EndingChecker.TriggerEnding` 과 `GameOverController` 가 즉시 건다.
  엔딩·사망 화면의 모든 연출은 **`unscaledDeltaTime` / `WaitForSecondsRealtime`** 이어야 한다.
  `Time.deltaTime` 을 하나라도 쓰면 연출 전체가 첫 프레임에서 멈춘다(실제 버그였다).
- **눕힌 몸통의 로컬 축**: 일부 생물은 `rotationEuler(0,0,90)` 으로 누워 있어 **로컬 +X가 월드 위쪽**이다.
  이 착각으로 상어 지느러미·독사 혀·전갈 집게·함정 가시·식인종 창이 전부 엉뚱한 곳에 붙어 있었다.
  파츠를 붙이기 전에 부모 회전을 반드시 확인하고, `CreateUprightPivot()` 을 써라.
- **`Physics.autoSyncTransforms` 는 기본 false**: 방금 만든 콜라이더에 바로 레이캐스트하면 안 맞는다.
  `Physics.SyncTransforms()` 를 먼저 불러라(초목이 전부 해수면에 깔린 원인이었다).
- **`TerrainSampler.SnapToGround` 는 이름이 `"Island_"` 로 시작하는 콜라이더만 지형으로 인정한다.**
  초목·장식물에 콜라이더를 붙이면 배치 높이 계산이 깨진다. 붙이지 마라.
- **`Destroy()` 는 프레임 끝까지 지연된다.** 즉시 물리에서 빼려면 `SetActive(false)` 를 먼저 불러라.
- **`GameObject.CreatePrimitive` 는 콜라이더를 자동으로 붙인다.** 시각 전용 파츠는 제거해야 한다.
- **`Time.deltaTime` vs 스크립트 실행 순서**: 씬 컴포넌트와 런타임 생성 컴포넌트는 실행 순서가
  지정돼 있지 않다. 같은 프레임에 상태를 주고받으면 실행마다 결과가 갈린다
  (엔딩 건너뛰기 통지가 실제로 이 함정에 걸려 한 프레임 지연으로 해결했다).
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
[확인요청] <내용>              ← 근거가 약한 것은 발견이 아니라 여기로
```

**코드 전문을 붙여넣지 마라. 짧게.**
**추측을 "발견"으로 올리지 마라.** 코드를 읽고 근거를 댈 수 있는 것만.
