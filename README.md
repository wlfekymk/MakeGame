# MakeGame — 무인도 생존 (v0.2.51)

Stranded Deep을 참고한 1인용 3D 무인도 생존 게임. Unity 6 (URP) 기반.

비행기 추락으로 무인도에 불시착한 플레이어가 생존 수치를 관리하며 자원을 채집하고,
집을 짓고 정착하거나, 해안에 뗏목을 지어 대양으로 떠나거나, 경비행기를 수리해 탈출한다.

> 프로젝트 방향(2026-08-16~): **탈출만이 정답이 아니다.** 무인도에 집을 잘 지어서
> 힐링하는 것도 방법이다. 압박(허기·갈증·위험)은 처벌이 아니라 리듬으로 유지한다.

![섬 컨셉 아트](Docs/ConceptArt/island_concept.png)

## 1. 실행 방법

1. [Unity Hub](https://unity.com/download)에서 **Unity 6 (6000.5.6f1)** 이상 설치
2. Unity Hub → **Open** → 이 폴더 선택
3. `Assets/Scenes/SampleScene.unity`를 열고 Play
4. 타이틀 화면에서 "시작하기"를 누르면 플레이가 시작된다

## 2. 조작법

| 키 | 기능 |
| --- | --- |
| W/A/S/D · 마우스 | 이동 · 시점 회전 |
| Space / Ctrl | 점프·부상 / 잠수 |
| E | 상호작용 · 공격 · 상자 열기 |
| R / C / G | 조리(모닥불 앞) / 섭취 / 설치형 키트 설치 |
| Tab / V / F | 인벤토리 / 제작 / 인벤 필터 |
| B / Q | 건축 모드 / 조각 회전(+휠) |
| X / T(우클릭) / Z | 회피 구르기 / 창 투척 / 낚시 |
| J / M | 퀘스트 / 지도(`+`/`-` 확대) |
| F5 / F9 | 저장 / 불러오기 (세이브 슬롯 3개) |
| Shift | 커서 해제 · 한 칸 전부 버리기 · 물통에 담기 |
| Esc | 설정 화면 |
| F3 / F4 / F10 / F11 | 디버그 HUD / 재료 지급 / 전체 지도 / 치트 토글 |

전체 키 배정 표는 `Docs/AGENT_BRIEF.md`(키 배정 절) 참고.

## 3. 주요 시스템 (한 줄씩)

- **생존**: 체력/허기/갈증/일사병/산소 + 중독·출혈·골절 상태 이상 (`SurvivalStats`)
- **절차적 월드**: 시드 기반 섬 9개(소/중/대/특대), 실제 지형 메시, 해저·난파선 (`WorldMapManager`, `IslandGenerator`)
- **건축**: 격자 자유 건축(바닥/벽/문/창/계단/지붕/상자), 뗏목 갑판 위 건축 포함 (`BuildingSystem`)
- **뗏목 자유 건조**: 도면 단계 없이 해안에 직접 짓는 Stranded Deep 방식 — 바닥판을 깔고
  돛·키·닻·노·모터를 얹는다 (`RaftStructure`, `RaftSailing`). 구 `BoatConstructionSystem`(도면 3단계)은 삭제됨
- **엔딩 3종** (`EndingChecker`): ① **귀환** — 뗏목 대양 준비(바닥판 6칸+ · 돛+키 또는 모터) + 물자 + 경과 15일
  ② **경비행기 수리** (`AircraftRepairSystem`) ③ **정복** — 수중 보스 3종 트로피 + 탈출 수단 완성
- **보스**: 거대 상어 / 대왕 곰치 / 심해 괴수 — 전부 수중 (`BossSpawner`)
- **전투·위험요소**: 독사/전갈/곰(실물 모델·AI)/벌떼/함정/식인종/상어/대왕 크랩 (`HazardSource`)
- **낚시**: Z키 캐스팅→입질→챔질, 기존 E키 사냥과 별개 (`FishingSystem`)
- **농사**: 밭 키트에 야자 묘목/해조류/약초 재배 (`FarmPlot`)
- **음식 부패**: 신선/상함/부패 3단계, 훈연·비상식량은 안 상함 (`FoodSpoilage`, `Smoker`)
- **제작·설치형 시설 8종**: 모닥불·쉼터·물 증류기·제작대·용광로·베틀·훈연기·밭 (`CraftingSystem`, `CraftStation`)
- **보관 상자 4등급**: 소형 설치 후 중형→대형→특대 업그레이드 (`StorageChest`)
- **아이템 57종**: 도구 내구도, 스킬 레벨링, 난이도별 배율 (`ItemDataRegistry`, `PlayerSkills`)
- **환경**: 밤낮 주기, 날씨(비·실내 차폐), 상어 해역 (`DayNightCycle`, `WeatherSystem`)
- **UI**: 전부 코드 생성(프리팹 UI 없음) — HUD/인벤토리/제작/지도/나침반/퀘스트 (`UIBuilder`)
- **세이브**: JSON + `.bak` 자동 복구, 슬롯 3개 (`SaveLoadController`)

## 4. 폴더 구조

```
Assets/
  Scenes/SampleScene.unity      # 단일 씬
  _Project/
    Scripts/                    # Player / Systems / UI / Managers / Utils / Enemy / Editor
    ScriptableObjects/          # Item_*.asset (아이템 57종)
    Resources/                  # ItemDataRegistry, SurvivalBalanceConfig, Textures, Models, Audio
    Art/                        # 스프라이트(아이콘)
    Prefabs/                    # Campfire · Shelter · WaterStill (프리팹은 3개뿐)
Docs/                           # 설계·스펙·에이전트 문서 (아래 링크)
Tools/                          # check.sh(정적 검증) · bump_version.sh · 에셋 파이프라인
VERSION                         # 현재 버전 (x.x.xx)
```

## 5. 빌드 · 검증 · Git

- 커밋 전 정적 검증: `Tools/check.sh` 1회 (구문 파싱 + 금지 API + 버전 일치, 성공 시 한 줄 출력)
- 버전 범프: `Tools/bump_version.sh` — 커밋마다 patch +1, `main` 브랜치 직접 커밋
- 버전 이력은 git log가 정본이다 (이 README에는 이력을 두지 않는다)

## 6. 문서

| 문서 | 내용 |
| --- | --- |
| `Docs/AGENT_BRIEF.md` | **에이전트 필독 단일 진실 파일** — 실측값·함정·키 배정·비용 규율 |
| `Docs/API_REFERENCE.md` | 코드 심볼 사전 |
| `Docs/MULTI_AGENT_GUIDE.md` | 멀티 에이전트 역할·파이프라인 |
| `Docs/DECISION_LOG.md` | 하지 않기로 한 것과 그 이유 |
| `Docs/ArtDirection.md` | 팔레트·형태 규칙 |
| `Docs/Design_*.md`, `Docs/Spec_*.md` | 기획·밸런스·스펙 (일부는 엔딩 중심의 옛 전제 — AGENT_BRIEF 참고) |

## 7. 코드 컨벤션

- 기능을 구현하는 모든 메서드에 **한글 설명 주석**을 남긴다.
- 새 스크립트는 역할별 하위 폴더(`Player/`, `Systems/`, `UI/`, `Managers/`, `Utils/`)에 배치한다.
- 절차적 시각 요소는 런타임 머티리얼 생성을 우선 검토하되, 프리팹 고정 파츠는 `.mat` 에셋으로 참조한다.
