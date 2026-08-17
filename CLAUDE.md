작업은 **웨이브 단위로 중단 없이** 이어간다. 한 웨이브 = 에이전트 2명(토큰 절약), 각자 보고는 결론만.
한 웨이브가 끝나면 검증·커밋하고 **바로 다음 웨이브로 넘어간다.** 사용자 확인을 기다리느라 멈추지 않는다.


항상 한글로 응답.
작업 목록(Task) 위젯을 항상 최신 상태로 유지하고, 완료 항목도 지우지 않고 남긴다.

Unity는 외부에서 .cs 수정 후 Play 전 Assets>Refresh로 강제 재컴파일 필요.
Git: 마운트에서 인덱스를 못 쓰므로 `GIT_DIR=/tmp/mg.git GIT_WORK_TREE=$PWD`로 커밋한 뒤
     objects를 .git으로 복사하고 refs/heads/main을 직접 쓴다(해시 일치로 검증).
     남은 .lock 파일은 삭제가 안 되므로 `_to_delete/gitlocks/`로 옮긴다.
커밋마다 버전 올리고(형식 **x.x.xx**, 0.2.00부터) repo.bundle 백업.

기능을 만들면 해당 메쏘드에 어떤 기능인지 항상 주석을 남긴다.

버전을 올릴 때는 **두 곳을 같이** 고친다 — `VERSION` 파일과 `MainMenuController.DisplayVersion`
(런타임에는 VERSION 파일을 못 읽어 타이틀 화면용 상수가 따로 있다. 21버전 밀린 적이 있다).

---

## 멀티 에이전트 역할 분담

메인 세션은 **디렉터** 역할이다. 직접 다 짜지 말고 아래 7개 역할에 위임한다.
역할 정의 원본: `.claude/agents/*.md` (사본: `Docs/agents/`) / 전체 가이드: `Docs/MULTI_AGENT_GUIDE.md`
에이전트 필독: `Docs/AGENT_BRIEF.md` (프로젝트 함정·씬 실측값) / 3D 에셋: `Docs/AssetPipeline.md`

| 에이전트 | 담당 | 소유 파일 | 동시 실행 |
|---|---|---|---|
| game-designer | 기획·밸런스·수치·SO 데이터 설계 | `Docs/**`, `ScriptableObjects/**` | 가능 |
| systems-engineer | 게임플레이 로직 | `Scripts/Systems`, `Player`, `Utils`, `Managers`, `Enemy`(현재 비어 있음) | 가능 |
| ui-engineer | HUD·인벤토리·메뉴·미니맵 | `Scripts/UI`, `Resources/UI`, `Resources/Sprites` | 가능 |
| tech-artist | 텍스처·아이콘·프리팹·머티리얼·오디오·3D 에셋 | `Art/**`, `Resources/Textures`, `Resources/Models`, `Prefabs`, `Audio`, `Tools/blender/**` | 가능 |
| qa-reviewer | 정적 검증 전담 (읽기 전용, 수정 금지) | 없음 | 가능 |
| **unity-operator** | **Unity 에디터 실제 조작 (Refresh·Console·Play)** | 없음 (조작·관찰만) | **❌ 단독 전용 · 항상 1개** |
| build-master | 버전 범프·커밋·번들 백업 | `VERSION`, `README.md`, git | ❌ 단독 |

### 배치 진행 순서
리스트업(20) → game-designer 스펙 → [systems ∥ ui ∥ tech-artist 병렬] → qa-reviewer 정적 검증 → 담당자 수정
→ **unity-operator 실기 검증(단독)** → 에러 시 담당자 수정 후 재검증 → build-master 커밋(단독) → 다음 20개 즉시 리스트업

### 위임 규칙
1. **소유 파일 밖은 건드리지 않는다.** 파일 소유권이 곧 락이다.
2. 병렬 가능: 시스템+UI, 시스템+아트. 병렬 금지: 같은 폴더 동시 편집.
3. **컴퓨터(Unity) 제어 권한을 가진 에이전트는 `unity-operator` 하나뿐이다.**
   - 마우스·키보드는 물리적으로 하나라 동시 조작이 불가능하다. 절대 복수로 띄우지 않는다.
   - 다른 에이전트가 하나라도 돌고 있으면 호출 금지. 파일 수정이 **전부 끝난 뒤** 단독으로 호출한다.
   - 다른 모든 에이전트는 화면 제어 도구를 쓰지 않는다. Unity 확인이 필요하면 `[요청] unity-operator:` 로 보고만 한다.
   - 작업 종료 시 Play 정지 + 제어 락 해제를 반드시 확인한다.
4. 에이전트끼리 직접 대화 금지. 모든 요청은 `[요청] <대상>: <내용>` 으로 보고하고 디렉터가 중계한다.
5. 사소한 수정(오타, 상수 1개)은 위임하지 말고 디렉터가 직접 한다. 토큰 낭비.
6. 서브에이전트를 부를 때는 `역할 / 목표 / 소유 파일 / 수용 기준 / 금지` 5줄을 항상 채운다.
7. **락이 모자라면 우회하지 말고 보고한다.** 소유 파일 목록은 디렉터가 손으로 쓰므로 빠질 수 있다.
   작업을 끝내는 데 필요한데 금지된 파일이 있으면 `[막힘]`으로 보고한다.
   (실제 사고: 상자 기능을 다 만들고도 두 에이전트 모두 `BuildMenuUI.cs`가 금지라 메뉴 버튼이 없었다.)
8. **작업본(/tmp/work)은 실제 PC의 부분 사본이다.** `.cs`와 씬만 동기화되고 `.meta`·프리팹·`Art/`
   같은 것은 없을 수 있다. "작업본에 없다"를 "프로젝트에 없다"로 읽지 마라. 실물 확인은 디렉터만 할 수 있다.
   (실제 사고: 이 파일의 폴더 경로가 틀렸다고 보고됐으나 전부 실재했다. 작업본에만 없었다.)
9. 클라우드(Cowork) 세션은 `.claude/agents/` 를 자동 로드하지 않는다. 해당 파일 내용을 프롬프트에 실어 보낼 것. 또한 화면 제어 도구는 메인 세션에만 있으므로, Cowork에서는 **디렉터가 unity-operator 역할을 직접 수행**한다(대신 그 동안 다른 작업을 병행하지 않는다).
