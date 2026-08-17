#!/usr/bin/env python3
"""
덤불 2종 - bush_a / bush_b (2026-08-17 신규).

    python3 Tools/blender/units/bush.py

산출물 (이 두 파일만 건드린다)
  Assets/_Project/Resources/Models/bush_a.obj   (+ b)
  Tools/blender/_preview/bush_a.png             (+ b)  ← 저장소에 넣지 않는다

────────────────────────────────────────────────────────────────────────────────
크기의 근거 (게임 코드 실측 - 이번 배치에서 코드는 못 고치므로 여기에 맞춘다)

  IslandMeshGenerator.Vegetation.cs:768 CreateBush
      width  = 1.3 ~ 2.2 m,  height = 0.6 ~ 1.0 m,  depth = width × 0.9
      기울기 tiltX ±7.7도 / tiltZ ±10도, yaw 무작위 - 자세는 인스턴스가 준다.
  IslandMeshGenerator.MeshLibrary.cs:108 GetBushClumpMesh (현행 절차 메시, 92삼각형)
      규격 x·z ∈ [-0.5,0.5], y ∈ [0,1], **원점이 밑동** - 이 모델도 같은 규약(접지 중심)이다.
  색  IslandMeshGenerator.Vegetation.cs:171  Shade(FrondGreen, 0.82) 등 × "leaf" 텍스처
      ("leaf" 는 런타임 절차 텍스처다 - Textures/ 폴더에 PNG 가 없다. 방향성 없는 노이즈라
       box UV 로 어떤 타일 값을 줘도 이음새가 안 보인다.)
  개수  IslandMeshGenerator.Vegetation.cs:230  bushCount = clamp(radius × 0.24, 12, 48)

  두 변종의 (W,H,D)는 게임 범위의 중간·상단이다. 연결 코드는 목표 폭/모델 폭 균등 배율
  (0.8~1.1 근처)로 쓰면 된다.

형태 규칙 (현행 92삼각형 절차 메시와 갈라지는 이유)
  1. 로브가 3개가 아니라 5~6개이고, 각 로브가 방향 노이즈로 울퉁불퉁하다(매끈한 정이십면체
     3개는 "돌 세 개"로도 읽혔다).
  2. **삐져나온 잎끝**을 8장 → 14~18장으로 늘리고 로브 윤곽선 위로 확실히 내보낸다.
     이게 20m 밖에서 덤불과 바위를 가르는 유일한 신호다.
  3. 밑면을 평면 절단해 접지면을 만든다 - 둥근 밑바닥은 땅에 점 하나로 닿아 굴러온 공처럼
     보인다(현행 메시도 같은 이유로 y ∈ [0,1] 규격이다).
  4. 잎끝은 두께 없는 양면 사각면이다(mg.make_double_sided - 계약 4장, 알파 컷아웃 없음).
     ★ 그래서 join 뒤에 clean_bmesh/remove_doubles 를 부르면 안 된다(뒷면이 녹는다).

원점 규약: **접지 중심**(align="ground"). 파일 구조: OBJ 1개 = `o` 1개(단색 - "leaf" 틴트 1장).
"""

import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

import mgbuild as mg  # noqa: E402  (bpy 를 먼저 끌어온다 - bmesh 가 그 뒤에야 import 된다)
import bmesh  # noqa: E402
from mathutils import Vector  # noqa: E402

# 시드는 여기 박아 둔다. 같은 시드 = 같은 메시(계약 5장).
VARIANTS = [
    # 이름,     시드,   (W, H, D) 미터,       로브, 잎끝
    ("bush_a", 14243, (1.60, 0.75, 1.45), 5, 14),
    ("bush_b", 28901, (2.10, 0.95, 1.90), 6, 18),
]

# 특대 섬에 48포기가 깔린다(위 실측). 개수 × 삼각형으로 잡은 상한이 800 이다(디렉터 지시).
TRI_BUDGET = 800
TRI_FLOOR = 250
UV_TILE = 0.60          # "leaf" 노이즈 텍스처가 0.6m 마다 반복 - 방향성이 없어 이음새 무해
BUSH_GREEN = (0.344, 0.540, 0.203)   # Shade(FrondGreen, 0.82) - 게임 틴트 1번


def _lobe_noise(direction, salt):
    """로브 표면 요철. 방향만의 함수라 이웃 삼각형에 틈이 안 생긴다(rock._surface_radius 방식)."""
    s = salt * 0.6180339
    r = 1.0
    r += 0.10 * math.sin(direction.x * 4.1 + s)
    r += 0.09 * math.sin(direction.z * 4.9 + s * 1.7)
    r += 0.07 * math.sin(direction.y * 3.6 + s * 2.3)
    r += 0.05 * math.sin((direction.x + direction.z) * 7.3 + s * 3.1)
    return r


def build_lobes(rng, lobe_count):
    """로브 덩어리(단위 크기, 나중에 fit_size). 밑면은 평면 절단으로 접지면을 만든다."""
    bm = bmesh.new()
    for li in range(lobe_count):
        # 첫 로브는 중앙의 주 덩어리, 나머지는 주위로 돌려 가며 붙인다.
        if li == 0:
            center = Vector((0.0, 0.40, 0.0))
            rx, ry, rz = 0.52, 0.42, 0.48
        else:
            a = math.tau * (li - 1) / (lobe_count - 1) + rng.uniform(-0.4, 0.4)
            d = rng.uniform(0.24, 0.40)
            center = Vector((math.cos(a) * d, rng.uniform(0.34, 0.52), math.sin(a) * d))
            rx = rng.uniform(0.26, 0.38)
            ry = rx * rng.uniform(0.72, 0.95)
            rz = rng.uniform(0.26, 0.38)
        salt = rng.uniform(0.0, 100.0)
        res = bmesh.ops.create_icosphere(bm, subdivisions=2, radius=1.0)
        for v in res["verts"]:
            d = v.co.normalized()
            n = _lobe_noise(d, salt)
            v.co = center + Vector((d.x * rx, d.y * ry, d.z * rz)) * n

    # 밑면 평면 절단. 로브가 y<0 까지 내려와 있으므로 0 에서 자르면 접지 디스크가 생긴다.
    geom = bm.verts[:] + bm.edges[:] + bm.faces[:]
    res = bmesh.ops.bisect_plane(bm, geom=geom, dist=1e-6,
                                 plane_co=Vector((0.0, 0.0, 0.0)),
                                 plane_no=Vector((0.0, -1.0, 0.0)),
                                 clear_outer=True)
    edges = [e for e in res["geom_cut"] if isinstance(e, bmesh.types.BMEdge)]
    if edges:
        bmesh.ops.edgenet_fill(bm, edges=edges)
    mg.clean_bmesh(bm, dist=1e-4)        # 잎끝을 합치기 **전**이라 remove_doubles 가 안전하다
    return mg.new_object("lobes", bm)


def build_blades(rng, blade_count):
    """삐져나온 잎끝. 두께 없는 양면 사각면 - 로브 윤곽선 밖으로 확실히 나가야 덤불로 읽힌다.

    1차 렌더에서 끝을 0.18~0.30 배로 좁히고 위로 세운 잎끝이 **가시**로 읽혔다(고슴도치).
    잎끝은 (a) 끝 폭을 밑폭의 절반 정도로만 좁히고 (b) 위가 아니라 **바깥**으로 눕히고
    (c) 잎면을 축 둘레로 굴려(roll) 제각각 기울어야 "삐져나온 가지 끝 잎"으로 읽힌다.
    """
    blades = []
    for i in range(blade_count):
        a = math.tau * i / blade_count + rng.uniform(-0.25, 0.25)
        out = Vector((math.cos(a), 0.0, math.sin(a)))
        crown = (i % 3 == 0)          # 1/3 은 위로 솟는 왕관 잎, 2/3 은 옆으로 뻗는 잎
        if crown:
            lift = rng.uniform(0.55, 0.72)
            base = out * rng.uniform(0.10, 0.22) + Vector((0.0, lift, 0.0))
            tip = (out * rng.uniform(0.26, 0.40)
                   + Vector((0.0, lift + rng.uniform(0.24, 0.36), 0.0)))
        else:
            lift = rng.uniform(0.30, 0.58)
            base = out * rng.uniform(0.26, 0.36) + Vector((0.0, lift, 0.0))
            # 3차 수정: 2차의 out 0.52~0.68 은 로브 표면(≈0.5+요철)에 묻혀 안 보였다.
            # 로브 밖으로 0.15~0.3 확실히 내보내고 끝을 살짝 든다.
            tip = (out * rng.uniform(0.64, 0.80)
                   + Vector((0.0, lift + rng.uniform(0.10, 0.26), 0.0)))
        wb = rng.uniform(0.075, 0.105)
        wt = wb * rng.uniform(0.40, 0.60)

        axis = (tip - base).normalized()
        side = axis.cross(Vector((0.0, 1.0, 0.0)))
        if side.length < 1e-4:
            side = axis.cross(out)
        side.normalize()
        roll = rng.uniform(-0.7, 0.7)              # 잎면을 굴려 전부 수직판이 되지 않게
        side = side * math.cos(roll) + axis.cross(side) * math.sin(roll)

        bm = bmesh.new()
        b0 = bm.verts.new(base - side * wb)
        b1 = bm.verts.new(base + side * wb)
        t1 = bm.verts.new(tip + side * wt)
        t0 = bm.verts.new(tip - side * wt)
        bm.faces.new((b0, b1, t1, t0))
        bmesh.ops.triangulate(bm, faces=bm.faces[:])
        mg.make_double_sided(bm)
        leaf = mg.new_object(f"tip{i}", bm)
        blades.append(leaf)
    return blades


def main():
    print("[bush] 덤불 2종 생성")
    all_stats = []
    for name, seed, size, lobe_count, blade_count in VARIANTS:
        mg.reset_scene()
        rng = mg.Rng(seed)
        lobes = build_lobes(rng, lobe_count)
        blades = build_blades(rng.sub(9), blade_count)
        bush = mg.join_objects([lobes] + blades, name=name)
        # ★ join 뒤 clean_bmesh 금지 - 양면 잎끝의 복제 정점이 녹아 뒷면이 사라진다.

        mg.fit_size(bush, size)
        mg.shade_flat(bush)                 # 각진 면이 "매끈한 돌덩이"와 덤불을 가른다
        mg.box_uv(bush, tile=UV_TILE)

        stats = mg.enforce_contract(bush, tri_budget=TRI_BUDGET, tri_floor=TRI_FLOOR,
                                    expect_size=size, name=name, align="ground")

        obj_path = os.path.join(mg.MODELS_DIR, f"{name}.obj")
        mg.export_obj(bush, obj_path)
        stats = mg.verify_obj_file(obj_path, stats)

        mg.assign_material(bush, mg.preview_material(
            f"prev_{name}", texture_name=None, base_color=BUSH_GREEN, roughness=0.80))
        mg.turntable(bush, os.path.join(mg.PREVIEW_DIR, f"{name}.png"),
                     title=f"{name}   seed {seed}   lobes {lobe_count} tips {blade_count}",
                     stats=stats,
                     notes='tint Shade(FrondGreen,.82) / runtime tex "leaf" / box UV %.2fm' % UV_TILE)

        mg.report(stats)
        all_stats.append(stats)

    print("[bush] 완료 - 렌더: Tools/blender/_preview/bush_*.png")
    return all_stats


if __name__ == "__main__":
    main()
