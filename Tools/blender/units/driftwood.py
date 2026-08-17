#!/usr/bin/env python3
"""
표류물 3종 - crate_a / barrel_a / plankpile_a (2026-08-17 신규).

    python3 Tools/blender/units/driftwood.py

산출물 (이 세 파일만 건드린다)
  Assets/_Project/Resources/Models/crate_a.obj / barrel_a.obj / plankpile_a.obj
  Tools/blender/_preview/crate_a.png (+ barrel_a, plankpile_a)  ← 저장소에 넣지 않는다

────────────────────────────────────────────────────────────────────────────────
크기의 근거 (게임 코드 실측 - 이번 배치에서 코드는 못 고치므로 여기에 맞춘다)

  IslandMeshGenerator.Vegetation.cs:445 CreateDriftItem  (스케일 지터 0.85~1.25 공통)
      :462  궤짝     0.82 × 0.66 × 0.74 m
      :467  통       0.60 × 0.86 × 0.60 m
      :472  널판더미 2.10 × 0.22 × 0.86 m
      자세(파묻힘 15~35%·기울기·yaw)는 전부 인스턴스가 준다 - 모델은 똑바로 세워 굽는다.
  현행 절차 메시: IslandMeshGenerator.MeshLibrary.cs:366/393/419 (84/272/36삼각형, 단위 구격)
  색·텍스처: :193  Shade(Driftwood,0.88) / SupplyKhaki × **driftwood.png**
  개수  IslandMeshGenerator.Vegetation.cs:316  driftCount = clamp(radius×0.05, 2, 9)

  세 모델 다 위 실측 크기 그대로 미터로 굽는다(현행 단위 구격과 달리, 연결 코드는
  스케일 지터만 곱하면 된다 - "메시만 바꾸고 호출부 스케일을 그대로 둔" 사고 방지 주석 참고).

형태 규칙 (프리미티브 상자 조합과 갈라지는 이유)
  crate_a     널판 사이 **진짜 틈**(판마다 분리된 상자 + 1.2~2cm 간격)과 모서리 보강대,
              밑깔개 2개. 판마다 두께·길이를 지터해 "공장 새 상자"가 아니라 표류물로.
  barrel_a    10각 스웨이브 몸통 + 볼록한 배 + **돌출 링 3줄**(플랫 셰이딩 - 멀리서 밝기
              링으로 읽힌다, 대나무 마디와 같은 수법) + 한쪽 옆구리 찌그러짐.
  plankpile_a 길이·폭이 제각각인 널판 7장이 어긋나게 겹친다(3+2+1 더미 + 기대 선 1장).
              현행 3장 정렬 더미는 "쌓아 둔 것"으로 읽혔다 - 흐트러져야 "밀려온 것"이다.

원점 규약: **접지 중심**(align="ground" - 셋 다 밑면이 넓어 bbox 와 사실상 같지만
  파이프라인 신규 에셋 기본을 따른다). 파일 구조: OBJ 1개 = `o` 1개(driftwood 틴트 1장).
"""

import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

import mgbuild as mg  # noqa: E402  (bpy 를 먼저 끌어온다 - bmesh 가 그 뒤에야 import 된다)
import bmesh  # noqa: E402
from mathutils import Matrix, Vector  # noqa: E402

TRI_BUDGET = 600        # 섬당 최대 9개 - 개수 × 삼각형 상한(디렉터 지시)
UV_TILE = 0.55          # driftwood.png 한 장이 0.55m 를 덮는다
DRIFT_TINT = (0.483, 0.352, 0.221)   # Shade(Driftwood, 0.88) - 게임 틴트 1번

CRATE_SEED = 61417
BARREL_SEED = 72923
PLANK_SEED = 83641


def _box(bm, center, size, yaw=0.0, pitch=0.0, roll=0.0):
    """상자 하나를 bm 에 누적한다. size 는 전체 치수(미터), 회전은 도 단위."""
    mat = (Matrix.Translation(Vector(center))
           @ Matrix.Rotation(math.radians(yaw), 4, "Y")
           @ Matrix.Rotation(math.radians(pitch), 4, "X")
           @ Matrix.Rotation(math.radians(roll), 4, "Z")
           @ Matrix.Diagonal(Vector((size[0] * 0.5, size[1] * 0.5, size[2] * 0.5)).to_4d()))
    bmesh.ops.create_cube(bm, size=2.0, matrix=mat)


# ── 궤짝 ──────────────────────────────────────────────────────────────────────
def build_crate(seed):
    """0.82 × 0.66 × 0.74. 판자 틈과 모서리 보강이 실루엣이 아니라 **그림자**로 읽히게
    판마다 상자를 분리해 굽는다(현행 메시는 홈이 없어 민짜 큐브로 보였다)."""
    rng = mg.Rng(seed)
    W, H, D = 0.82, 0.66, 0.74
    post = 0.085                      # 모서리 기둥 굵기
    bm = bmesh.new()

    # 모서리 기둥 4개 (높이 전체)
    for sx in (-1, 1):
        for sz in (-1, 1):
            _box(bm, (sx * (W / 2 - post / 2), H / 2, sz * (D / 2 - post / 2)),
                 (post, H, post), yaw=rng.uniform(-1.0, 1.0))

    # 옆판: 앞뒤(±Z) / 좌우(±X) 각 3장, 판 사이 틈. 판마다 두께·길이·기울기 지터.
    rows = 3
    plank_h = (H - 0.10) / rows * 0.82
    for row in range(rows):
        y = 0.07 + (H - 0.12) * (row + 0.5) / rows
        for sz in (-1, 1):            # 앞뒤 판 (X 방향으로 길다)
            t = rng.uniform(0.030, 0.042)
            _box(bm, (rng.uniform(-0.012, 0.012), y, sz * (D / 2 - t / 2)),
                 (W - post * 0.7, plank_h * rng.uniform(0.92, 1.05), t),
                 roll=rng.uniform(-1.6, 1.6))
        for sx in (-1, 1):            # 좌우 판 (Z 방향으로 길다)
            t = rng.uniform(0.030, 0.042)
            _box(bm, (sx * (W / 2 - t / 2), y, rng.uniform(-0.012, 0.012)),
                 (t, plank_h * rng.uniform(0.92, 1.05), D - post * 0.7),
                 pitch=rng.uniform(-1.6, 1.6))

    # 뚜껑 판 3장(하나는 살짝 들려 어긋난다 - 밀봉된 새 상자가 아니라 표류물)
    lid_w = W / 3 * 0.94
    for k in range(3):
        x = -W / 3 + k * W / 3
        lift = 0.012 if k == 1 else 0.0
        _box(bm, (x + rng.uniform(-0.008, 0.008), H - 0.024 + lift, rng.uniform(-0.01, 0.01)),
             (lid_w, 0.045, D * 0.97), yaw=rng.uniform(-2.2, 2.2))

    # 밑깔개 2개 (스키드 - 바닥과의 접지선을 만들고 상자를 살짝 띄운다)
    for sz in (-1, 1):
        _box(bm, (0.0, 0.028, sz * (D / 2 - 0.10)), (W * 0.98, 0.055, 0.09))

    bmesh.ops.triangulate(bm, faces=bm.faces[:])
    return mg.new_object("crate_a", bm), (W, H, D)


# ── 드럼통(나무통) ────────────────────────────────────────────────────────────
def build_barrel(seed):
    """0.60 지름 × 0.86 높이. 배 부른 10각 몸통 + 돌출 링 3줄 + 옆구리 찌그러짐."""
    rng = mg.Rng(seed)
    R, H = 0.30, 0.86
    sides = 12    # 1차 렌더의 10각 + 통널 요철 0.012 는 그루터기처럼 각졌다 - 12각, 요철 절반

    # 세로 프로파일: 통널의 배(sin 곡선) 위에 링 3줄을 얹는다. 링은 (아래/칼라/위)
    # 3링 묶음 - 칼라 띠를 플랫 셰이딩으로 남겨 법선이 끊긴 밝기 링을 만든다(bamboo 수법).
    hoop_ys = (0.13, 0.43, 0.73)
    hoop_half = 0.020                          # 1차 0.030 은 링이 벨트처럼 넓었다
    ts = [0.0, 0.05]
    for hy in hoop_ys:
        ts += [hy - hoop_half, hy - 0.009, hy + 0.009, hy + hoop_half]
    ts += [0.95, 1.0]
    ts = sorted(ts)

    def belly(t):
        return R * (0.74 + 0.26 * math.sin(math.pi * min(1.0, max(0.0, t))) ** 0.9)

    rings, bands = [], []
    for k, t in enumerate(ts):
        r = belly(t)
        in_hoop = any(abs(t - hy) <= 0.0095 for hy in hoop_ys)
        if in_hoop:
            r *= 1.055                        # 돌출 링
        radii = []
        for i in range(sides):
            a = math.tau * i / sides
            stave = 1.0 + 0.006 * math.cos(6.0 * a + 1.3)   # 통널 미세 요철
            radii.append(r * stave)
        rings.append((Vector((0.0, t * H, 0.0)), radii))
    for k in range(len(ts) - 1):
        mid = (ts[k] + ts[k + 1]) * 0.5
        bands.append(not any(abs(mid - hy) <= hoop_half for hy in hoop_ys))  # 링 띠만 플랫

    bm = bmesh.new()
    mg.swept_tube(bm, rings, sides=sides, cap_bottom=True, cap_top=True, smooth=bands)

    # 찌그러짐: 한 방위(위쪽 1/3)를 안으로 누른다. 완전한 회전체는 공산품 새 통으로 보인다.
    # 1차 깊이 0.055 는 통이 녹은 것처럼 보였다 - 0.032 로 얕게, 구간도 좁게.
    dent_a = rng.uniform(0.0, math.tau)
    dent_dir = Vector((math.cos(dent_a), 0.0, math.sin(dent_a)))
    for v in bm.verts:
        t = v.co.y / H
        radial = Vector((v.co.x, 0.0, v.co.z))
        if radial.length < 1e-5:
            continue
        d = radial.normalized().dot(dent_dir)
        if d > 0.4 and 0.55 < t < 0.92:
            depth = 0.032 * ((d - 0.4) / 0.6) ** 1.5 * math.sin(math.pi * (t - 0.55) / 0.37)
            v.co -= radial.normalized() * depth

    bmesh.ops.triangulate(bm, faces=bm.faces[:])
    return mg.new_object("barrel_a", bm), (0.60, H, 0.60)


# ── 널판 더미 ─────────────────────────────────────────────────────────────────
def build_plankpile(seed):
    """2.10 × 0.22 × 0.86. 널판 7장 - 아래 3, 가운데 2, 위 1, 기대 선 1. 전부 어긋난다."""
    rng = mg.Rng(seed)
    bm = bmesh.new()

    def plank(cx, cy, cz, ln, wd, th, yaw, pitch=0.0, roll=0.0):
        _box(bm, (cx, cy, cz), (ln, th, wd), yaw=yaw, pitch=pitch, roll=roll)
        return th

    # 아래층 3장 (긴 판이 나란하되 어긋난 각도)
    t0 = 0.050
    plank(-0.05, t0 / 2, -0.28, rng.uniform(1.7, 2.0), 0.27, t0, rng.uniform(3, 8))
    plank(0.10, t0 / 2, 0.02, rng.uniform(1.8, 2.05), 0.30, t0, rng.uniform(-7, -3))
    plank(-0.12, t0 / 2, 0.30, rng.uniform(1.5, 1.8), 0.25, t0, rng.uniform(-2, 3))
    # 가운데층 2장 (교차 각을 키운다)
    t1 = 0.048
    plank(0.06, t0 + t1 / 2, -0.10, rng.uniform(1.5, 1.8), 0.28, t1, rng.uniform(9, 16))
    plank(-0.10, t0 + t1 / 2 + 0.004, 0.18, rng.uniform(1.3, 1.6), 0.24, t1, rng.uniform(-16, -9))
    # 위층 1장 (짧고 크게 돌아가 있다)
    t2 = 0.045
    plank(0.02, t0 + t1 + t2 / 2 + 0.006, 0.04, rng.uniform(1.1, 1.4), 0.26, t2,
          rng.uniform(18, 26), roll=rng.uniform(2, 5))
    # 기대 선 1장: 한쪽 끝이 더미 위에 걸쳐 들려 있다 - 더미 실루엣을 깨는 사선.
    plank(0.55, 0.115, -0.18, 1.35, 0.22, 0.045, rng.uniform(-30, -22),
          pitch=rng.uniform(-2, 2), roll=rng.uniform(-10.5, -8.5))

    bmesh.ops.triangulate(bm, faces=bm.faces[:])
    return mg.new_object("plankpile_a", bm), (2.10, 0.22, 0.86)


def main():
    print("[driftwood] 표류물 3종 생성")
    builds = [
        ("crate_a", CRATE_SEED, build_crate),
        ("barrel_a", BARREL_SEED, build_barrel),
        ("plankpile_a", PLANK_SEED, build_plankpile),
    ]
    all_stats = []
    for name, seed, fn in builds:
        mg.reset_scene()
        obj, size = fn(seed)
        mg.fit_size(obj, size)               # 지터로 어긋난 바운딩 박스를 실측 계약치에 고정
        if name == "barrel_a":
            mg.cylinder_uv(obj, tile=UV_TILE, wraps=2.0)   # 통은 smooth 밴드(링) 유지
        else:
            mg.shade_flat(obj)
            mg.box_uv(obj, tile=UV_TILE)

        stats = mg.enforce_contract(obj, tri_budget=TRI_BUDGET, tri_floor=30,
                                    expect_size=size, name=name, align="ground")

        obj_path = os.path.join(mg.MODELS_DIR, f"{name}.obj")
        mg.export_obj(obj, obj_path)
        stats = mg.verify_obj_file(obj_path, stats)

        mg.assign_material(obj, mg.preview_material(
            f"prev_{name}", texture_name="driftwood", base_color=DRIFT_TINT, roughness=0.85))
        mg.turntable(obj, os.path.join(mg.PREVIEW_DIR, f"{name}.png"),
                     title=f"{name}   seed {seed}",
                     stats=stats,
                     notes="tex driftwood.png / tile %.2fm" % UV_TILE)

        mg.report(stats)
        all_stats.append(stats)

    print("[driftwood] 완료 - 렌더: Tools/blender/_preview/{crate,barrel,plankpile}_a.png")
    return all_stats


if __name__ == "__main__":
    main()
