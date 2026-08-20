# 외부 참조 출처

이 파일은 **어디에서 무엇을 보고 만들었는지**를 남긴다. 규칙은 두 줄이다.

1. 참조한 출처는 전부 여기에 적는다.
2. **그대로 복사한 것**과 **보고 다시 구현한 것**을 반드시 구분해 적는다.

라이선스 파일이 없는 저장소도 참조한다(2026-08-20 결정). 다만 그렇기 때문에
"무엇을 그대로 가져왔는가"를 더 정확히 적어 둘 필요가 있다 — 나중에 배포 전에
걸러내야 할 것이 있다면 이 파일 한 장만 보면 되도록.

---

## 1. Unity UI Toolkit 인벤토리 시리즈 (Part 1~6)

| | |
|---|---|
| 출처 | `ui-toolkit-pt1-reusable-window-system` ~ `ui-toolkit-pt6-equipment-interactions` (GitHub, 영상 시리즈 부속 저장소) |
| 영상 | YouTube 재생목록 `PLUQd-0PkiOI5_msWheOHo-XnyEvQPLpbR` |
| 라이선스 | 저장소에 LICENSE 파일 없음 |
| 기술 스택 | UI Toolkit (UXML/USS) — **우리 프로젝트는 legacy uGUI라 파일을 그대로 쓸 수 없다** |

### 그대로 복사한 것

**없다.** UXML·USS·C#·스프라이트·폰트·아이콘 중 프로젝트에 들어간 파일은 하나도 없다.
기술 스택이 달라서(UI Toolkit ↔ uGUI) 애초에 복사가 성립하지 않는다.

### 수치만 옮긴 것 (원본 USS에 적힌 값을 단위만 바꿔 그대로 사용)

`Assets/_Project/Scripts/UI/UITheme.cs`의 "튜토리얼 레퍼런스에서 그대로 옮긴 값" 구획.

| 우리 상수 | 원본 위치 | 원본 값 |
|---|---|---|
| `DragSourceAlpha` | `Inventory/InventoryWindow.uss` `.drag-active` | `opacity: 0.4` |
| `DropTargetValid` | `Shared/ItemSlot.uss` `.drop-target` | `border-color: rgb(255,224,130)` |
| `DropTargetInvalid` | `Shared/ItemSlot.uss` `.drop-target--invalid` | `border-color: rgb(220,70,70)` |
| `TooltipBackground` | `Shared/ItemTooltip.uss` `#ItemTooltip` | `rgba(24,22,30,0.95)` |
| `TooltipBorder` | 같은 파일 | `rgba(255,255,255,0.15)` |
| `TooltipIconSize` | 같은 파일 `.tooltip-icon` | `48px` |
| `TooltipHeaderGap` | 같은 파일 `.tooltip-header-text` | `margin-left: 8px` |
| `TooltipPaddingX/Y` | 같은 파일 `#ItemTooltip` | `padding: 8px 10px` |

원본의 색 중 **가져오지 않은 것**: 등급색 3종(`--rarity-uncommon/rare/epic`),
제목 색(`--color-h1` 갈색, `--color-h2` 크림색), 폰트(LilitaOne / Sniglet).
우리 게임에는 아이템 등급이 없고, 색은 ArtDirection.md 팔레트 밖으로 나가지 않는다.

### 보고 다시 구현한 것 (구조·규칙만 참고, 코드는 새로 씀)

| 우리 파일 | 참고한 원본 | 무엇을 참고했나 |
|---|---|---|
| `UI/UITheme.cs`, `UI/UIBuilder.CreateSkinnedWindow` | pt1 `Shared/GameWindow.uss/.uxml` | 창을 "제목줄 / 구분선 / 본문"으로 나누고 창마다 다시 짜지 않는 골격 |
| `UI/InventoryUI.cs` (격자 + 상세 2단) | pt2 `Inventory/InventoryWindow.uss` | 칸 격자를 감싸는 컨테이너 구성과 여백 |
| `UI/UISlotHover.cs` | pt2 `Shared/ItemSlot.uss` `#ItemSlot:hover` | 마우스를 올리면 칸이 살짝 커지는 연출(원본 `scale 1.05`, `0.15s ease-in-out`) |
| `UI/UIDragGhost.cs` | pt4 `ItemDragManipulator.InitGhost` | 끌고 다니는 고스트를 최상위에 하나만 두고, 커서 중앙에 두고, 클릭 판정을 주지 않는다 |
| `UI/InventoryUI.cs` 드래그 4종 | pt4 `ItemDragManipulator` | ① 집어 든 칸을 흐리게 ② 커서 아래 칸 테두리를 물들임 ③ Esc 취소 ④ 드래그 중 툴팁 숨김 |
| `UI/ItemTooltipUI.cs` 머리줄 | pt4 `Shared/ItemTooltip.uxml/.uss` | 툴팁 맨 위를 [아이콘][이름/분류] 가로 2단으로 두고 그 아래를 선으로 가름 |

### 원본과 일부러 다르게 한 것

- **맞바꿈이 아니라 끼워 넣기.** 원본은 칸이 곧 저장 위치라 두 칸을 swap하지만, 우리 격자는
  `PlayerInventory.items`에서 파생된 뷰라 빈 칸이 존재하지 않는다. A를 B 자리로 끌면
  나머지가 한 칸씩 밀린다(`PlayerInventory.ApplyStackOrder`).
- **등급 대신 카테고리.** 원본의 희귀도 테두리 발광(`#RarityOverlay`) 자리에 우리는
  카테고리 색을 쓴다. 생존 게임에서는 "이게 음식인가 재료인가"가 등급보다 정보량이 크다.
- **떠다니는 툴팁 대신 고정 상세 패널.** 인벤토리 창은 오른쪽에 상세 패널을 고정으로 둔다
  (이유는 `InventoryUI.RefreshDetail` 주석). 떠다니는 툴팁은 제작 창·보관 상자 창이 쓴다.
- **필터 중에는 드래그 금지.** 원본에는 필터가 없다. 화면의 n번째 칸이 전체 목록의 n번째가
  아니게 되는 순간 "놓은 자리"와 "들어가는 자리"가 갈리므로 아예 막았다.

---

## 2. 그 밖의 참조

- `Docs/QualityPlan.md`에 적힌 후보 에셋·저장소는 **아직 프로젝트에 들어오지 않았다.**
  실제로 가져오는 시점에 이 파일에 항목을 추가한다.
