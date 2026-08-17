#!/usr/bin/env python3
"""
풀포기 2종 - grass_a / grass_b (2026-08-17 신규).

    python3 Tools/blender/units/grass.py

산출물 (이 두 파일만 건드린다)
  Assets/_Project/Resources/Models/grass_a.obj   (+ b)
  Tools/blender/_preview/grass_a.png             (+ b)  ← 저장소에 넣지 않는다

────────────────────────────────────────────────────────────────────────────────
크기의 근거 (게임 코드 실측 - 이번 배치에서 코드는 못 고치므로 여기에 맞춘다)

  IslandMeshGenerator.Vegetation.cs:810 CreateGrassTuft
      width = 0.32 ~ 0.62 m,  height = 0.26 ~ 0.46 m,  depth = width × 0.30
      (depth 30% 는 "눌린 구" 시절의 부채꼴 납작화다 - 실물 포기는 방사형이 맞고,
       연결 코드는 균등 배율 하나로 쓰면 된다. 그림자 캐스팅은 코드가 끈다: :822)
  IslandMeshGenerator.MeshLibrary.cs:572 GetGrassBladeMesh (현행 절차 메시, 40삼각형)
      잎 5장 × 2마디 × 양면. 규격 [-0.5,0.5]^3 중심 원점 - **이 모델은 접지 중심**으로
      바꾼다(현행 호출부는 ground + height×0.35 로 중심을 띄워 보정하고 있다: :818).
  색  IslandMeshGenerator.Vegetation.cs:177  Shade(MeadowGreen, 0.86~0.98) × "leaf"
      ("leaf" 는 런타임 절차 텍스처 - PNG 없음. 방향성 없는 노이즈.)
  개수  IslandMeshGenerator.Vegetation.cs:231  tuftCount = clamp(radius × 0.78, 20, 156)

형태 규칙 (현행 5장 부채꼴과 갈라지는 이유)
  1. 잎날 7~10장을 **방사형**으로 심는다(부채꼴 한 장은 옆에서 보면 판때기다).
  2. 잎날마다 3마디로 휘어 끝이 바깥·아래로 처진다 - 직선 잎은 칼날로 읽힌다.
  3. 잎은 두께 없는 단면 + 양면(mg.make_double_sided). 개수가 제일 많은 에셋이라
     잎 하나 = 12삼각형, 포기 전체 ≤ 150 을 지킨다(특대 섬 156포기 × 150 = 23,400).
  4. 잎날 폭 3~4.5cm - 포기 폭 0.5m 에 잎 10장이면 이게 "풀"로 읽히는 최소 비례다.

원점 규약: **접지 중심**(align="ground"). 밑동이 원점 - 배치 코드가 띄울 보정값이 없다.
파일 구조: OBJ 1개 = `o` 1개(단색 "leaf" 틴트 1장).
"""

import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

import mgbuild as mg  # noqa: E402  (bpy 를 먼저 끌어온다 - bmesh 가 그 뒤에야 import 된다)
import bmesh  # noqa: E402
from mathutils import Matrix, Vector  # noqa: E402

# 시드는 여기 박아 둔다. 같은 시드 = 같은 메시(계약 5장).
VARIANTS = [
    # 이름,      시드,   (W, H, D) 미터,       잎수
    ("grass_a", 33107, (0.46, 0.34, 0.42), 7),
    ("grass_b", 47521, (0.62, 0.45, 0.56), 10),
]

TRI_BUDGET = 150        # 특대 섬 156포기 - 개수 × 삼각형 상한(디렉터 지시)
TRI_FLOOR = 60
GRASS_GREEN = (0.465, 0.567, 0.267)   # Shade(MeadowGreen, 0.86) - 게임 틴트 1번


def _blade(rng):
    """잎날 1장. 평면(폭 ±X, 길이 +Z)에서 만들어 UV 를 뜬 **뒤에** 휜다(mgbuild 함정 8번).

    3마디 스트립(쿼드 3장 = 6삼각형, 양면 12). 밑동 → 중간 → 끝으로 좁아지고,
    휨은 마디별 접선 각도를 누적해서 만든다 - 적분이라 마디가 꺾인 관절로 안 보인다.
    """
    # 2차 수정: 길이 0.30~0.44 는 잎끝 높이가 고르게 나와 윗변이 둥근 부케가 됐다.
    # 범위를 0.24~0.46 으로 벌려 짧은 속잎과 긴 겉잎이 섞이게 한다.
    length = rng.uniform(0.24, 0.46)
    half_w = rng.uniform(0.015, 0.023)
    stations = [0.0, 0.42, 0.76, 1.0]
    widths = [1.0, 0.72, 0.45, 0.14]

    bm = bmesh.new()
    rows = []
    for s, wf in zip(stations, widths):
        w = half_w * wf
        rows.append((bm.verts.new((-w, 0.0, s * length)),
                     bm.verts.new((w, 0.0, s * length))))
    for (a0, a1), (b0, b1) in zip(rows, rows[1:]):
        bm.faces.new((a0, a1, b1, b0))
    bmesh.ops.triangulate(bm, faces=bm.faces[:])
    mg.make_double_sided(bm)

    leaf = mg.new_object("blade", bm)
    mg.shade_flat(leaf)
    mg.planar_uv(leaf, axis="Y", tile=(half_w * 6.0, length), offset=(0.5, 0.0))

    # 휨: 밑동은 수직에서 launch 만큼 기울어 시작하고, 끝으로 갈수록 bend 만큼 더 눕는다.
    launch = math.radians(rng.uniform(10.0, 34.0))     # 수직에서 벌어진 각
    bend = math.radians(rng.uniform(28.0, 75.0))       # 끝까지 누적되는 추가 휨
    prev_s = 0.0
    pos = Vector((0.0, 0.0, 0.0))
    spine = {0.0: (Vector((0.0, 0.0, 0.0)), launch)}
    for s in stations[1:]:
        mid = (prev_s + s) * 0.5
        ang = launch + bend * (mid ** 1.6)
        seg = (s - prev_s) * length
        pos = pos + Vector((0.0, math.cos(ang), math.sin(ang))) * seg
        spine[s] = (pos.copy(), launch + bend * (s ** 1.6))
        prev_s = s
    for vert in leaf.data.vertices:
        s = round(vert.co.z / length, 6)
        key = min(spine.keys(), key=lambda k: abs(k - s))
        p, ang = spine[key]
        vert.co = Vector((vert.co.x, p.y, p.z))
    return leaf


def build_tuft(seed, blade_count):
    rng = mg.Rng(seed)
    blades = []
    for i in range(blade_count):
        yaw = 360.0 * i / blade_count + rng.uniform(-24.0, 24.0)
        # 2차 수정: 밑동 산포 ±0.04 는 잎이 한 점에서 묶여 "다발 꽃다발"로 보였다 - ±0.07 로.
        hub = Vector((rng.uniform(-0.07, 0.07), 0.0, rng.uniform(-0.07, 0.07)))
        leaf = _blade(rng.sub(i))
        leaf.matrix_world = (Matrix.Translation(hub)
                             @ Matrix.Rotation(math.radians(yaw), 4, "Y"))
        blades.append(leaf)
    return mg.join_objects(blades, name="tuft")
    # ★ join 뒤 clean_bmesh 금지 - 양면 잎의 복제 정점이 녹아 뒷면이 사라진다.


def main():
    print("[grass] 풀포기 2종 생성")
    all_stats = []
    for name, seed, size, blade_count in VARIANTS:
        mg.reset_scene()
        tuft = build_tuft(seed, blade_count)
        tuft.name = name          # OBJ 의 `o` 이름이 파일명과 일치해야 한다(다른 종과 통일)

        mg.fit_size(tuft, size)
        stats = mg.enforce_contract(tuft, tri_budget=TRI_BUDGET, tri_floor=TRI_FLOOR,
                                    expect_size=size, name=name, align="ground",
                                    ground_band=0.02)

        obj_path = os.path.join(mg.MODELS_DIR, f"{name}.obj")
        mg.export_obj(tuft, obj_path)
        stats = mg.verify_obj_file(obj_path, stats)

        mg.assign_material(tuft, mg.preview_material(
            f"prev_{name}", texture_name=None, base_color=GRASS_GREEN, roughness=0.75))
        mg.turntable(tuft, os.path.join(mg.PREVIEW_DIR, f"{name}.png"),
                     title=f"{name}   seed {seed}   blades {blade_count}",
                     stats=stats,
                     notes='tint Shade(MeadowGreen,.86) / runtime tex "leaf" / planar UV')

        mg.report(stats)
        all_stats.append(stats)

    print("[grass] 완료 - 렌더: Tools/blender/_preview/grass_*.png")
    return all_stats


if __name__ == "__main__":
    main()
