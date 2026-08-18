#!/usr/bin/env python3
"""
searock_* - 해저 바위 20변종 (2026-08-17 해저 배치, 사용자 요청 "바다 안 바위 20종류 이상").

    python3 Tools/blender/units/searock.py

산출물: Assets/_Project/Resources/Models/searock_a~t.obj (+.mtl)
        Tools/blender/_preview/searock_*.png

설계 - 4형태 × 5변종 = 20:
  boulder  둥근 표석(침식으로 둥글다 - 뭍 바위보다 매끈)   5종 (a~e)
  slab     낮게 누운 판형(모래에 반쯤 묻힌 암반)            5종 (f~j)
  spire    세로 첨탑(물속 기둥 - 잠수 랜드마크)             5종 (k~o)
  cluster  잔바위 군집(한 메시, 드로우콜 1)                 5종 (p~t)
  o 오브젝트 1개(searock_x_rock) = 머티리얼 1장. 색은 런타임(어두운 현무암~해조 낀 회록).
  크기 0.4~3.2m, 밑면 y=0. 콜라이더는 배치 코드가 박스로 얹는다(작은 것은 생략).
"""

import math
import os
import sys

_UNITS_DIR = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.dirname(_UNITS_DIR))

import mgbuild as mg  # noqa: E402
import bmesh  # noqa: E402
from mathutils import Matrix, Vector  # noqa: E402


def lump(bm, rng, center, size, lumps, subdiv=2, smooth=True):
    """지터 준 아이코스피어 덩어리. size=(sx,sy,sz)."""
    result = bmesh.ops.create_icosphere(bm, subdivisions=subdiv, radius=1.0)
    verts = result["verts"]
    yaw = rng.uniform(0, math.tau)
    m = Matrix.Rotation(yaw, 4, "Y")
    for v in verts:
        n = v.co.normalized()
        r = 1.0 + rng.uniform(-lumps, lumps)
        v.co = Vector((n.x * size[0] * r * 0.5, n.y * size[1] * r * 0.5, n.z * size[2] * r * 0.5))
        v.co = m @ v.co
        v.co += Vector(center)
    for f in bm.faces:
        f.smooth = smooth
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])


def build_searock(name, seed, kind, w, h, d):
    rng = mg.Rng(seed)
    bm = bmesh.new()

    if kind == "boulder":
        lump(bm, rng, (0, h * 0.42, 0), (w, h, d), lumps=0.10, subdiv=2)
    elif kind == "slab":
        lump(bm, rng, (0, h * 0.35, 0), (w, h, d), lumps=0.14, subdiv=1, smooth=False)
        # 곁판 하나 - 층리 느낌.
        lump(bm, rng, (w * rng.uniform(-0.25, 0.25), h * 0.22, d * rng.uniform(-0.25, 0.25)),
             (w * 0.6, h * 0.5, d * 0.6), lumps=0.16, subdiv=1, smooth=False)
    elif kind == "spire":
        # 세로로 쌓인 덩어리 3개 - 위로 갈수록 가늘다.
        y = 0.0
        for i, k in enumerate((1.0, 0.72, 0.45)):
            hh = h * (0.42 - 0.06 * i)
            lump(bm, rng, (rng.uniform(-0.06, 0.06) * w, y + hh * 0.5, rng.uniform(-0.06, 0.06) * w),
                 (w * k, hh * 1.25, d * k), lumps=0.12, subdiv=1, smooth=False)
            y += hh * 0.78
    else:  # cluster
        count = rng.randint(4, 6)
        for _ in range(count):
            cw = w * rng.uniform(0.22, 0.45)
            ch = h * rng.uniform(0.4, 1.0)
            lump(bm, rng, (rng.uniform(-0.5, 0.5) * (w - cw) * 0.9, ch * 0.35,
                           rng.uniform(-0.5, 0.5) * (d - cw) * 0.9),
                 (cw, ch, cw * rng.uniform(0.8, 1.2)), lumps=0.13,
                 subdiv=1, smooth=False)

    obj = mg.new_object(name + "_rock", bm)
    mg.box_uv(obj, tile=1.0)
    return obj


# (이름, 시드, 형태, W, H, D)
SPECS = [
    ("searock_a", 81001, "boulder", 0.9, 0.7, 0.85),
    ("searock_b", 81013, "boulder", 1.5, 1.1, 1.4),
    ("searock_c", 81027, "boulder", 0.55, 0.45, 0.5),
    ("searock_d", 81039, "boulder", 2.1, 1.5, 1.9),
    ("searock_e", 81051, "boulder", 1.2, 0.85, 1.05),
    ("searock_f", 81063, "slab", 1.6, 0.45, 1.3),
    ("searock_g", 81077, "slab", 2.4, 0.6, 1.8),
    ("searock_h", 81089, "slab", 1.1, 0.35, 0.9),
    ("searock_i", 81101, "slab", 3.0, 0.75, 2.2),
    ("searock_j", 81113, "slab", 1.9, 0.5, 1.6),
    ("searock_k", 81127, "spire", 0.8, 1.8, 0.7),
    ("searock_l", 81139, "spire", 1.1, 2.6, 0.95),
    ("searock_m", 81151, "spire", 0.6, 1.3, 0.55),
    ("searock_n", 81163, "spire", 1.4, 3.2, 1.2),
    ("searock_o", 81177, "spire", 0.9, 2.1, 0.8),
    ("searock_p", 81189, "cluster", 1.4, 0.5, 1.2),
    ("searock_q", 81201, "cluster", 2.0, 0.7, 1.7),
    ("searock_r", 81213, "cluster", 1.0, 0.4, 0.9),
    ("searock_s", 81227, "cluster", 2.6, 0.9, 2.2),
    ("searock_t", 81239, "cluster", 1.7, 0.6, 1.5),
]


def main():
    manifest = []
    for name, seed, kind, w, h, d in SPECS:
        mg.reset_scene()
        obj = build_searock(name, seed, kind, w, h, d)
        mg.triangulate(obj)
        mg.assign_material(obj, mg.preview_material("pv_" + name, base_color=(0.30, 0.33, 0.31)))
        stats = mg.enforce_contract_group([obj], tri_budget=mg.TRI_BUDGET["small_prop"],
                                          tri_floor=30, name=name, align="ground")
        out = os.path.join(mg.MODELS_DIR, name + ".obj")
        mg.export_obj(obj, out)
        mg.verify_obj_file(out, stats)
        mg.inject_usemtl(out)
        mg.turntable(obj, os.path.join(mg.PREVIEW_DIR, name + ".png"),
                     title=name, stats=stats, px=330, samples=16)
        manifest.append((name, stats["tris"], tuple(round(v, 2) for v in stats["size"])))

    print("SEAROCK_MANIFEST")
    for name, tris, size in manifest:
        print(f"  {name}  {tris}tri  {size[0]}x{size[1]}x{size[2]}m")
    print(f"[searock] 완료 - {len(manifest)}종")


if __name__ == "__main__":
    main()
