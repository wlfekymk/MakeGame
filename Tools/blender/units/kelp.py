#!/usr/bin/env python3
"""
kelp_* - 해초(미역/켈프) 10변종 (2026-08-17 해저 배치, 사용자 요청 "미역 10타입 이상").

    python3 Tools/blender/units/kelp.py

산출물: Assets/_Project/Resources/Models/kelp_a~j.obj (+.mtl)
        Tools/blender/_preview/kelp_*.png

설계
  세 형태 축: (1) 리본 켈프 - 긴 물결 리본 몇 가닥(1.8~4.2m), (2) 블레이드 군집 -
  짧은 잎 여러 장이 방석처럼(0.5~1.0m), (3) 갈래 미역 - 넓은 잎이 중간에서 갈라짐.
  잎은 전부 단면 + make_double_sided(URP 백페이스 컬링 - 계약 주석 참고).
  o 오브젝트 1개(kelp_x_blade) = 머티리얼 1장. 색은 런타임(어두운 갈조~녹조 팔레트).
  물결(사인 굽이)은 정점에 굽는다 - 셰이더 흔들림은 추후 과제.
"""

import math
import os
import sys

_UNITS_DIR = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.dirname(_UNITS_DIR))

import mgbuild as mg  # noqa: E402
import bmesh  # noqa: E402
from mathutils import Vector  # noqa: E402


def ribbon(bm, rng, base, height, width, waves, segs=7):
    """물결치는 수직 리본 한 가닥(단면 - 뒤는 make_double_sided가 만든다)."""
    yaw = rng.uniform(0, math.tau)
    ux, uz = math.cos(yaw), math.sin(yaw)          # 리본 폭 방향
    px, pz = -uz, ux                                # 물결 방향(폭과 수직)
    phase = rng.uniform(0, math.tau)
    lean = rng.uniform(-0.12, 0.12)

    left = []
    right = []
    for s in range(segs + 1):
        t = s / segs
        y = height * t
        sway = math.sin(phase + t * math.pi * waves) * width * 1.4 * t
        w = width * (1.0 - 0.55 * t)               # 위로 갈수록 좁아진다
        cx = base[0] + px * sway + ux * 0 + lean * y * px
        cz = base[2] + pz * sway + lean * y * pz
        left.append(bm.verts.new((cx - ux * w * 0.5, y, cz - uz * w * 0.5)))
        right.append(bm.verts.new((cx + ux * w * 0.5, y, cz + uz * w * 0.5)))
    faces = []
    for s in range(segs):
        f = bm.faces.new((left[s], right[s], right[s + 1], left[s + 1]))
        f.smooth = True
        faces.append(f)
    bmesh.ops.recalc_face_normals(bm, faces=faces)


def blade(bm, rng, base, length, width, droop):
    """짧은 잎 한 장: 비스듬히 뻗는 4분할 스트립, 끝이 처진다."""
    yaw = rng.uniform(0, math.tau)
    d = Vector((math.cos(yaw), 0, math.sin(yaw)))
    u = Vector((-d.z, 0, d.x))
    segs = 4
    left = []
    right = []
    rise = rng.uniform(0.5, 0.9)
    for s in range(segs + 1):
        t = s / segs
        p = Vector(base) + d * (length * t)
        p.y = base[1] + length * rise * math.sin(min(1.0, t * 1.15) * math.pi * 0.55) - droop * t * t * length
        w = width * (1.0 - 0.7 * t)
        left.append(bm.verts.new(p - u * w * 0.5))
        right.append(bm.verts.new(p + u * w * 0.5))
    faces = []
    for s in range(segs):
        f = bm.faces.new((left[s], right[s], right[s + 1], left[s + 1]))
        f.smooth = True
        faces.append(f)
    bmesh.ops.recalc_face_normals(bm, faces=faces)


def build_kelp(name, seed, kind, height, strands):
    rng = mg.Rng(seed)
    bm = bmesh.new()

    if kind == "ribbon":
        for _ in range(strands):
            base = (rng.uniform(-0.12, 0.12), 0.0, rng.uniform(-0.12, 0.12))
            ribbon(bm, rng, base, height * rng.uniform(0.75, 1.0),
                   rng.uniform(0.10, 0.17), rng.uniform(1.2, 2.4))
    elif kind == "cluster":
        for _ in range(strands):
            base = (rng.uniform(-0.10, 0.10), 0.0, rng.uniform(-0.10, 0.10))
            blade(bm, rng, base, height * rng.uniform(0.6, 1.0),
                  rng.uniform(0.10, 0.16), rng.uniform(0.15, 0.45))
    else:  # "split" - 넓은 잎 리본 + 위쪽 갈래 블레이드
        for _ in range(max(1, strands // 2)):
            base = (rng.uniform(-0.08, 0.08), 0.0, rng.uniform(-0.08, 0.08))
            ribbon(bm, rng, base, height * rng.uniform(0.8, 1.0),
                   rng.uniform(0.16, 0.24), rng.uniform(0.8, 1.5))
        for _ in range(strands):
            base = (rng.uniform(-0.10, 0.10), height * rng.uniform(0.25, 0.5),
                    rng.uniform(-0.10, 0.10))
            blade(bm, rng, base, height * rng.uniform(0.3, 0.5),
                  rng.uniform(0.08, 0.13), rng.uniform(0.3, 0.6))

    mg.make_double_sided(bm)
    obj = mg.new_object(name + "_blade", bm)
    mg.planar_uv(obj, axis="Z", tile=0.6)
    return obj


# (이름, 시드, 형태, 높이, 가닥 수)
SPECS = [
    ("kelp_a", 71001, "ribbon", 2.6, 4),
    ("kelp_b", 71013, "ribbon", 3.8, 5),
    ("kelp_c", 71027, "ribbon", 1.8, 3),
    ("kelp_d", 71039, "ribbon", 4.2, 6),
    ("kelp_e", 71051, "cluster", 0.7, 8),
    ("kelp_f", 71063, "cluster", 0.5, 6),
    ("kelp_g", 71077, "cluster", 1.0, 10),
    ("kelp_h", 71089, "split", 2.2, 5),
    ("kelp_i", 71101, "split", 3.0, 6),
    ("kelp_j", 71113, "split", 1.5, 4),
]


def main():
    manifest = []
    for name, seed, kind, h, n in SPECS:
        mg.reset_scene()
        obj = build_kelp(name, seed, kind, h, n)
        mg.triangulate(obj)
        mg.assign_material(obj, mg.preview_material("pv_" + name, base_color=(0.20, 0.42, 0.22)))
        stats = mg.enforce_contract_group([obj], tri_budget=mg.TRI_BUDGET["small_prop"],
                                          tri_floor=20, name=name, align="ground")
        out = os.path.join(mg.MODELS_DIR, name + ".obj")
        mg.export_obj(obj, out)
        mg.verify_obj_file(out, stats)
        mg.inject_usemtl(out)
        mg.turntable(obj, os.path.join(mg.PREVIEW_DIR, name + ".png"),
                     title=name, stats=stats, px=330, samples=16)
        manifest.append((name, stats["tris"], tuple(round(v, 2) for v in stats["size"])))

    print("KELP_MANIFEST")
    for name, tris, size in manifest:
        print(f"  {name}  {tris}tri  {size[0]}x{size[1]}x{size[2]}m")
    print(f"[kelp] 완료 - {len(manifest)}종")


if __name__ == "__main__":
    main()
