# Tools/blender — 절차 3D 에셋 파이프라인

규격의 유일한 소스는 **`Docs/AssetPipeline.md`** 다. 이 폴더는 그 계약을 코드로 강제한다.

## 다시 돌리는 법 (한 줄)

```bash
cd /path/to/MakeGame && python3 Tools/blender/units/rock.py
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
- `fit_size(obj, (w,h,d))` — 바운딩 박스를 정확한 미터 크기로
- **`enforce_contract(obj, tri_budget, expect_size=...)`** — 밑면 y=0 / X·Z 중심 정렬을 **적용하고**,
  삼각형화·예산·접지·크기·퇴화면·잔여 변환을 **검사한다. 어기면 `ContractError` 로 죽는다.**
- `export_obj(obj, path)` — 삼각형 + `vn` + `vt`, 머티리얼 없음
- **`verify_obj_file(path, stats)`** — 내보낸 파일을 **다시 읽어** 삼각형 수·크기·접지·중심을 대조
- `turntable(obj, png, ...)` — 정면/측면/후면/3-4 네 컷 + 1m 격자 + y=0 기준선 + 2m 플레이어 막대를
  PNG 한 장으로 합친다

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

## 현재 에셋

| 스크립트 | 산출 | 삼각형 | 크기 (W×H×D m) | UV |
|---|---|---|---|---|
| `units/rock.py` | `rock_a` | 3,366 | 1.85 × 1.20 × 1.60 | box, `rock.png` 1.15m 타일 |
| | `rock_b` | 3,364 | 2.60 × 1.55 × 2.30 | 〃 |
| | `rock_c` | 3,366 | 3.20 × 2.35 × 2.60 | 〃 |

크기 근거와 게임 코드 실측값은 `units/rock.py` 파일 상단 주석에 있다.
