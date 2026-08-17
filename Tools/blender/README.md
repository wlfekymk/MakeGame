# Tools/blender — 절차 3D 에셋 파이프라인

규격의 유일한 소스는 **`Docs/AssetPipeline.md`** 다. 이 폴더는 그 계약을 코드로 강제한다.

## 다시 돌리는 법 (한 줄)

```bash
cd /path/to/MakeGame && python3 Tools/blender/units/rock.py       # rock_a~e
cd /path/to/MakeGame && python3 Tools/blender/units/palm.py       # palm_a~f
cd /path/to/MakeGame && python3 Tools/blender/units/bamboo.py     # bamboo_a~f
cd /path/to/MakeGame && python3 Tools/blender/units/bush.py       # bush_a/b
cd /path/to/MakeGame && python3 Tools/blender/units/grass.py      # grass_a/b
cd /path/to/MakeGame && python3 Tools/blender/units/driftwood.py  # crate_a/barrel_a/plankpile_a
```

- Blender GUI 가 필요 없다. `bpy` 가 pip 모듈로 설치돼 있다(Blender 5.0.1 / Python 3.11).
- 어디서 실행하든 경로는 스크립트가 저장소 루트 기준으로 잡는다(`mgbuild.PROJECT_ROOT`).
- 시드가 스크립트에 박혀 있어 **몇 번을 돌려도 바이트 단위로 같은 OBJ** 가 나온다(검증함).
- 한 번 도는 데 약 40초(대부분 Cycles 턴테이블 렌더 12컷).

새 에셋을 추가할 때는 `units/` 아래에 파일을 하나 더 만든다. `mgbuild` 는 손대지 않는 것이 기본이고,
공통으로 쓸 것이 생겼을 때만 옮긴다.

## 산출물

| 무엇 | 어디 | 저장소에 넣나 |
|---|---|---|
| 메시 | `Assets/_Project/Resources/Models/<이름>.obj` | 넣는다 |
| 턴테이블 렌더 | `Tools/blender/_preview/<이름>.png` | **넣지 않는다**(`_preview/.gitignore`) |
| `.blend` | 만들지 않는다 | — |

Unity 임포트 설정(디렉터): **Scale Factor 1 / Convert Units 꺼짐 / Generate Colliders 꺼짐.**
머티리얼·콜라이더는 에셋이 아니라 런타임 코드가 만든다(계약 4장).

## `mgbuild.py` 가 하는 일

- `reset_scene()` — 빈 씬에서 시작(이전 실행 잔여물이 재현성을 깬다)
- `Rng(seed)` — 시드 고정 난수. `unit_vector()` 포함
- `clean_bmesh(bm)` — 겹친 정점 용접 + 면적 0 삼각형 제거(절단·불리언 뒤에는 항상)
- `decimate_to_budget(obj, n)` — 삼각형 예산 맞추기. **UV 를 펴기 전에** 부른다
- `box_uv(obj, tile)` — 박스(삼면) 투영 UV. 타일링 노이즈 텍스처 전용, 완전히 결정적
- `cylinder_uv(obj, tile, wraps)` — 둘레 → U / 높이 → V. 줄기(bark·bamboo)용. 이음새를 면 단위로 푼다
- `planar_uv(obj, axis, tile, offset)` — 평면 투영. `tile` 에 (U m, V m) 쌍을 줄 수 있다. **잎 전용**
- `make_double_sided(bm)` — 두께 없이 뒷면을 굽는다(단면 잎이 백페이스 컬링에 사라지는 것을 막는다)
- `swept_tube(bm, rings, sides)` — 줄기용 다각 튜브. 링마다 측면별 반지름을 줄 수 있다(비원형 단면)
- `join_objects(objs, name)` — bmesh 누적으로 메시 한 장으로 합친다(UV·smooth 플래그 보존)
- `enforce_contract_group(objs, ...)` / `export_obj([o1, o2], path)` — **조립체**(한 파일 · `o` 여럿)
- `fit_size(obj, (w,h,d))` — 바운딩 박스를 정확한 미터 크기로
- `ground_center(objs, band)` — **접지 중심**(밑동의 XZ 중심). 아래 원점 규약 참고
- **`enforce_contract(obj, tri_budget, expect_size=..., align=...)`** — 밑면 y=0 / X·Z 정렬을 **적용하고**,
  삼각형화·예산·접지·크기·퇴화면·잔여 변환을 **검사한다. 어기면 `ContractError` 로 죽는다.**
- `export_obj(obj, path)` — 삼각형 + `vn` + `vt`, 머티리얼 없음
- **`verify_obj_file(path, stats)`** — 내보낸 파일을 **다시 읽어** 삼각형 수·크기·접지·중심을 대조
- `turntable(obj, png, ...)` — 정면/측면/후면/3-4 네 컷 + 1m 격자 + y=0 기준선 + 2m 플레이어 막대를
  PNG 한 장으로 합친다

## 원점 규약 — 접지 중심 (2026-08-17 변경)

`enforce_contract` / `enforce_contract_group` 의 `align` 이 정한다.

| 값 | 뜻 | 쓰는 곳 |
|---|---|---|
| `"bbox"` | 바운딩 박스 중심 (`enforce_contract` 기본값) | 대칭 물체 — `rock_a/b/c` |
| `"ground"` | **접지 중심** = 밑면에서 `ground_band` 안에 있는 정점의 XZ 중심 (`enforce_contract_group` 기본값) | 위가 비대칭인 물체 — 야자수·대나무 |

계약 1장의 "X/Z 중심 정렬"을 오래 바운딩 박스 중심으로 읽어 왔는데, **비대칭 물체에서는 틀렸다.**
휜 야자수는 크라운이 bbox 를 지배해서 줄기 밑동이 원점에서 **최대 0.74m 밀려났다**(실측 palm_c).
지면에 닿는 것은 밑동이므로 밑동 중심이 원점이어야 한다. 바운딩 박스는 비대칭이어도 된다.

`verify_obj_file` 도 같은 규약을 **파일에서 다시 계산해** 검사한다(`stats["align"]` 를 따라간다).
`enforce_contract` 의 기본값이 여전히 `"bbox"` 인 이유는 하나뿐이다 — 이미 배포된 `rock_*.obj` 의
바이트가 바뀌면 안 된다(재실행 md5 동일 확인함). **새 에셋은 대칭이 확실하지 않으면 `"ground"` 를 쓴다.**

## 이 파이프라인을 만들면서 실제로 걸린 함정 (지우지 마라)

1. **`import bmesh` 는 `import bpy` 뒤에 와야 한다.** 알파벳 순으로 정렬하면 `ModuleNotFoundError` 다.
2. **OBJ 내보내기의 "축 변환 없음"은 `forward_axis='Y', up_axis='Z'` 다.** 직관과 반대다.
   기본값(`-Z`/`Y`)은 Blender Z-up → OBJ Y-up 변환이라, 이미 Y-up 으로 만든 메시의 **Y 와 Z 가 맞바뀐다**
   (실측: 높이 1.20m 가 파일에서 1.60m 로 나왔다). `verify_obj_file` 이 이걸 잡았다.
3. **`Vector.to_track_quat('-Z','Y')` 로 카메라를 겨누지 마라.** 그 함수는 카메라의 위쪽을
   **Blender 월드 Z** 에 맞춘다. 이 파이프라인은 Unity 좌표(Y-up)로 작업하므로 측면 컷이 화면상
   90도 돌아 나오고, 그러면 접지 검수가 통째로 무의미해진다. `turntable` 은 카메라 기저를 직접 만든다.
4. **`bmesh.ops.create_icosphere(subdivisions=n)` 은 1부터 센다**: 1=20면 / 4=1,280 / 7=81,920.
5. **평면 절단은 표면을 크게 깎는다.** 원점 기준 support 비율로 자르면 무게중심이 밀린 뒤
   덩어리 한복판이 잘려 나간다 — 그 **방향의 두께(hi−lo) 비율**로 잘라야 한다.
6. **수평 정사영 카메라에서 바닥판은 한 픽셀도 안 나온다**(정확히 옆으로 선다).
   접지를 눈으로 보려면 `turntable` 처럼 y=0 선을 계산해서 직접 그어야 한다.
   (1m 큐브로 픽셀 대응을 교정해 확인했다: 예측 361.9행 / 실제 362행)
7. **`make_double_sided` 를 거친 메시에 `clean_bmesh`(remove_doubles)를 부르면 안 된다.**
   겹쳐 놓은 복제 정점이 다시 녹아 뒷면이 통째로 사라진다.
8. **잎 UV 는 휘기 전 평면 좌표로 뜬다.** 3D 로 휜 뒤 투영하면 늘어진 소엽이 위에서 봤을 때
   납작해져 UV 가 뭉개진다. `_flat_frond` → `planar_uv` → `_bend_frond` 순서가 그 이유다.
9. **한 오브젝트 = 한 머티리얼 = 한 색**이다(계약 4장: 머티리얼은 런타임 코드가 만든다).
   줄기 갈색과 잎 초록을 같이 내려면 파일 하나에 `o` 오브젝트를 둘 둔다. 파일을 둘로 쪼개면
   왕관 OBJ 가 제 밑면을 y=0 으로 맞추면서 조립 오프셋을 잃는다.
10. **멀리서 안 보이는 디테일은 없는 것과 같다.** 대나무 마디를 굵기 변화로만 만들면 20m 밖에서
   한 픽셀도 안 남는다. `swept_tube(smooth=[...])` 로 마디 칼라 띠만 **플랫 셰이딩**으로 두면
   법선이 끊겨 밝기 링이 생기고, 그게 거리에서 마디를 읽게 하는 유일한 수단이다.
11. **잎은 "식물학"이 아니라 "가독성"으로 크기를 정한다.** 실제 대나무 잎(폭 1~3cm)을 그대로
   만들면 포기가 맨 막대로 보인다. 게임 코드도 같은 이유로 대나무 잎다발에 야자잎 메시를 쓴다.

## 현재 에셋

2026-08-17 확장: 기존 종의 변종을 늘리고(rock +2 / palm +3 / bamboo +3) 신규 3세트를
추가했다(bush / grass / driftwood). **기존 a/b/c 9파일은 바이트 단위로 그대로다**(재실행
md5 대조함) - 새 변종은 별도 빌더(rock) 또는 기본값이 기존 상수와 같은 style 오버라이드
(palm/bamboo)로 만들어 기존 경로의 난수 소비를 건드리지 않는다.

| 스크립트 | 산출 | 삼각형 | 크기 (W×H×D m) | UV |
|---|---|---|---|---|
| `units/rock.py` | `rock_a` | 3,366 | 1.85 × 1.20 × 1.60 | box, `rock.png` 1.15m 타일 |
| | `rock_b` | 3,364 | 2.60 × 1.55 × 2.30 | 〃 |
| | `rock_c` | 3,366 | 3.20 × 2.35 × 2.60 | 〃 |
| | `rock_d` 판석 | 3,366 | 2.95 × 0.95 × 2.45 | 〃 |
| | `rock_e` 첨탑 | 3,366 | 2.15 × 3.20 × 1.90 | 〃 |
| `units/palm.py` | `palm_a` | 1,388 | 3.52 × 5.30 × 3.82 | 줄기 cylinder `bark.png` 0.55m · 잎 planar `frond.png` |
| | `palm_b` | 1,652 | 4.39 × 6.79 × 4.75 | 〃 |
| | `palm_c` | 1,784 | 5.55 × 7.95 × 5.30 | 〃 |
| | `palm_d` 어린 나무 | 1,004 | 3.25 × 3.29 × 3.08 | 〃 |
| | `palm_e` 폭풍 노목 | 2,024 | 5.10 × 7.41 × 4.57 | 〃 |
| | `palm_f` 곧은 장년목 | 1,424 | 5.01 × 6.52 × 5.11 | 〃 |
| `units/bamboo.py` | `bamboo_a` | 1,356 | 3.06 × 3.35 × 2.18 | 줄기 cylinder `bamboo.png` 0.30m ×2 · 잎 planar `frond.png` |
| | `bamboo_b` | 1,748 | 2.75 × 3.88 × 2.91 | 〃 |
| | `bamboo_c` | 1,768 | 2.38 × 4.46 × 2.72 | 〃 |
| | `bamboo_d` 어린 포기 | 508 | 1.92 × 2.04 × 2.35 | 〃 |
| | `bamboo_e` 꽉 찬 포기 | 1,668 | 2.57 × 5.07 × 2.88 | 〃 |
| | `bamboo_f` 바람 포기 | 996 | 1.89 × 4.25 × 2.40 | 〃 |
| `units/bush.py` | `bush_a` | 470 | 1.60 × 0.75 × 1.45 | box 0.60m · 런타임 절차 텍스처 `"leaf"` |
| | `bush_b` | 566 | 2.10 × 0.95 × 1.90 | 〃 |
| `units/grass.py` | `grass_a` | 84 | 0.46 × 0.34 × 0.42 | planar · `"leaf"` |
| | `grass_b` | 120 | 0.62 × 0.45 × 0.56 | 〃 |
| `units/driftwood.py` | `crate_a` | 252 | 0.82 × 0.66 × 0.74 | box 0.55m `driftwood.png` |
| | `barrel_a` | 380 | 0.60 × 0.86 × 0.60 | cylinder 0.55m ×2 `driftwood.png` |
| | `plankpile_a` | 84 | 2.10 × 0.22 × 0.86 | box 0.55m `driftwood.png` |

신규/확장 에셋의 원점은 **전부 접지 중심**(`align="ground"`)이다. 예외는 rock 5종
(`"bbox"` - 대칭이라 두 기준이 같고, a/b/c 의 바이트 보존이 우선이다).
bush / grass / driftwood 는 `o` 오브젝트 1개(단색 - 런타임 틴트 1장)이고,
palm / bamboo 는 기존처럼 `o` 2개(줄기/잎)다.

야자수·대나무는 **삼각형 예산을 계약표보다 보수적으로** 잡았다(야자수 2,500 / 대나무 1,800).
섬당 야자수 4~16그루 · 대나무 포기 수 개가 깔리므로 개수 × 삼각형으로 봐야 하기 때문이다.

야자수/대나무 OBJ 는 **파일 1개 안에 `o` 오브젝트 2개**다(줄기 / 잎). Unity 임포터가 자식
GameObject 로 만들어 상대 위치를 보존하므로 파츠마다 다른 런타임 머티리얼을 물릴 수 있다.
렌더러는 야자수 13 → 2, 대나무 최대 8 → 2 로 줄어든다.
원점은 둘 다 **접지 중심**이다(위 "원점 규약"). 배치 코드가 보정할 오프셋이 없다.

대나무 줄기 굵기는 게임의 "보이는 지름 13.2cm"가 아니라 **채집 콜라이더 지름 30cm** 쪽에 맞췄다
(굵은 줄기 16~20cm). 세장비 32는 갈대로 읽힌다. 콜라이더 안에 들어가므로 조준 판정은 안 변한다.
줄기가 어두운 것은 UV 가 아니라 런타임 틴트(Driftwood)다 — 제안 색과 비교 렌더는
`units/bamboo.py` 헤더의 [색] 절과 `_preview/bamboo_tint_proposal.png` 참고.

크기 근거와 게임 코드 실측값은 각 `units/*.py` 파일 상단 주석에 있다.
