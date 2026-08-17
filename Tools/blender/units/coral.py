#!/usr/bin/env python3
"""
coral_* - 산호 5계열 20변종 (2026-08-17 해저 배치, 사용자 요청 "산호초 20타입 이상").

    python3 Tools/blender/units/coral.py

산출물 (전부 신규)
  Assets/_Project/Resources/Models/coral_branch_a~f.obj  가지 산호(사슴뿔) 6종
  Assets/_Project/Resources/Models/coral_table_a~d.obj   테이블 산호 4종
  Assets/_Project/Resources/Models/coral_brain_a~c.obj   뇌 산호 3종
  Assets/_Project/Resources/Models/coral_fan_a~d.obj     부채 산호(고르고니안) 4종
  Assets/_Project/Resources/Models/coral_tube_a~c.obj    관 산호 3종
  (+ 각 .mtl - mtllib 없이는 Unity 6.5가 서브메시를 안 가른다: airliner.py 실사고)
  Tools/blender/_preview/coral_*.png                      렌더 - 저장소에 넣지 않는다

설계
  각 모델은 `o` 오브젝트 2개 = 서브메시 2개:
    <이름>_body  본체     <이름>_tip  가지 끝/폴립 강조 (밝은 색으로 산호 특유의 팁 발광감)
  색은 런타임 코드(SeabedFloraSpawner 계열)가 변종 인덱스로 팔레트에서 입힌다 - 여기선
  프리뷰 색만. 크기 0.3~1.6m, 밑면 y=0 접지 중심(수중 바닥에 그대로 놓는다).
  삼각형 예산: small_prop(1500) 안에서 종당 120~700.

시드는 표에 박아 둔다. 같은 시드 = 같은 메시 = 같은 md5.
"""

import math
import os
import sys

_UNITS_DIR = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.dirname(_UNITS_DIR))

import mgbuild as mg  # noqa: E402
import bmesh  # noqa: E402
from mathutils import Matrix, Vector  # noqa: E402


# ── 공용 소도구 ────────────────────────────────────────────────────────────────
def taper_tube(bm, tail, tip, r0, r1, sides=6, smooth=True):
    """두 점 사이 가늘어지는 다각 튜브(캡 포함). 산호 가지/줄기의 기본 단위."""
    tail = Vector(tail)
    tip = Vector(tip)
    axis = (tip - tail)
    if axis.length < 1e-5:
        return
    axis = axis.normalized()
    up = Vector((0, 1, 0)) if abs(axis.y) < 0.95 else Vector((1, 0, 0))
    u = axis.cross(up).normalized()
    w = axis.cross(u).normalized()

    loops = []
    for t, r in ((0.0, r0), (1.0, r1)):
        c = tail.lerp(tip, t)
        loop = [bm.verts.new(c + u * math.cos(math.tau * i / sides) * r
                             + w * math.sin(math.tau * i / sides) * r)
                for i in range(sides)]
        loops.append(loop)
    lo, hi = loops
    faces = []
    for i in range(sides):
        j = (i + 1) % sides
        f = bm.faces.new((lo[i], lo[j], hi[j], hi[i]))
        f.smooth = smooth
        faces.append(f)
    caps = [bm.faces.new(lo), bm.faces.new(list(reversed(hi)))]
    for f in caps:
        f.smooth = False
    bmesh.ops.recalc_face_normals(bm, faces=faces + caps)


def lump_sphere(bm, rng, center, radius, lumps=0.18, subdiv=1, squash=1.0):
    """울퉁불퉁한 구(아이코스피어 지터). 뇌 산호/덩어리용."""
    result = bmesh.ops.create_icosphere(bm, subdivisions=subdiv, radius=1.0)
    verts = result["verts"]
    for v in verts:
        n = v.co.normalized()
        r = radius * (1.0 + rng.uniform(-lumps, lumps))
        v.co = Vector((n.x * r, n.y * r * squash, n.z * r))
        v.co += Vector(center)
    for f in bm.faces:
        f.smooth = True
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
    return verts


def export_two_part(name, body_obj, tip_obj, tri_budget, tri_floor, preview_body, preview_tip):
    """body/tip 2오브젝트를 계약 검증 → OBJ+MTL 내보내기 → 턴테이블 렌더."""
    objs = [o for o in (body_obj, tip_obj) if o is not None]
    for o in objs:
        mg.triangulate(o)
    mg.assign_material(objs[0], mg.preview_material("pv_" + name + "_body", base_color=preview_body))
    if len(objs) > 1:
        mg.assign_material(objs[1], mg.preview_material("pv_" + name + "_tip", base_color=preview_tip))

    stats = mg.enforce_contract_group(objs, tri_budget=tri_budget, tri_floor=tri_floor,
                                      name=name, align="ground")
    out = os.path.join(mg.MODELS_DIR, name + ".obj")
    mg.export_obj(objs, out)
    mg.verify_obj_file(out, stats)
    inject_usemtl(out)
    png = os.path.join(mg.PREVIEW_DIR, name + ".png")
    mg.turntable(objs, png, title=name, stats=stats, px=330, samples=16)
    return stats


def inject_usemtl(path):
    """airliner.py와 동일: mtllib + usemtl + 최소 .mtl 동봉(서브메시 구분자)."""
    with open(path, "r") as fh:
        lines = fh.readlines()
    base = os.path.basename(path)
    mtl_name = base[:-4] + ".mtl"
    names = []
    out = []
    header_done = False
    for line in lines:
        out.append(line)
        if not header_done and not line.startswith("#"):
            out.insert(len(out) - 1, "mtllib " + mtl_name + chr(10))
            header_done = True
        if line.startswith("o "):
            n = line[2:].strip()
            names.append(n)
            out.append("usemtl " + n + chr(10))
    with open(path, "w") as fh:
        fh.writelines(out)
    with open(os.path.join(os.path.dirname(path), mtl_name), "w") as fh:
        for n in names:
            fh.write("newmtl " + n + chr(10) + "Kd 0.8 0.8 0.8" + chr(10) + chr(10))


# ── 계열 1: 가지 산호 (사슴뿔) ────────────────────────────────────────────────
def build_branch(name, seed, height, spread, branches):
    rng = mg.Rng(seed)
    bm_body = bmesh.new()
    bm_tip = bmesh.new()

    def grow(base, direction, length, radius, depth):
        tip = base + direction * length
        taper_tube(bm_body, base, tip, radius, radius * 0.62, sides=5)
        if depth >= 2:
            # 끝마디는 팁 오브젝트에 - 밝은 색 강조.
            end_dir = (direction + Vector((rng.uniform(-0.5, 0.5), rng.uniform(0.1, 0.5),
                                           rng.uniform(-0.5, 0.5)))).normalized()
            taper_tube(bm_tip, tip, tip + end_dir * length * 0.35,
                       radius * 0.62, radius * 0.25, sides=5)
            return
        kids = rng.randint(2, 3)
        for _ in range(kids):
            kd = (direction + Vector((rng.uniform(-spread, spread),
                                      rng.uniform(0.15, 0.55),
                                      rng.uniform(-spread, spread)))).normalized()
            grow(tip, kd, length * rng.uniform(0.6, 0.8), radius * 0.72, depth + 1)

    for b in range(branches):
        a = math.tau * b / branches + rng.uniform(-0.3, 0.3)
        d = Vector((math.cos(a) * spread, 1.0, math.sin(a) * spread)).normalized()
        grow(Vector((rng.uniform(-0.06, 0.06), 0.0, rng.uniform(-0.06, 0.06))),
             d, height * rng.uniform(0.32, 0.42), height * 0.055, 0)

    body = mg.new_object(name + "_body", bm_body)
    tip = mg.new_object(name + "_tip", bm_tip)
    mg.box_uv(body, tile=0.5)
    mg.box_uv(tip, tile=0.5)
    return body, tip


# ── 계열 2: 테이블 산호 ───────────────────────────────────────────────────────
def build_table(name, seed, width, height):
    rng = mg.Rng(seed)
    bm_body = bmesh.new()
    bm_tip = bmesh.new()

    # 줄기(짧고 굵다).
    taper_tube(bm_body, (0, 0, 0), (rng.uniform(-0.05, 0.05), height * 0.55, rng.uniform(-0.05, 0.05)),
               width * 0.09, width * 0.07, sides=6)
    # 상판: 울퉁불퉁한 원판(부채꼴 팬) - 가장자리 지터.
    spokes = 14
    cy = height * 0.58
    hub = bm_tip.verts.new((0, cy, 0))
    rim = []
    for i in range(spokes):
        a = math.tau * i / spokes
        r = width * 0.5 * (1.0 + rng.uniform(-0.16, 0.16))
        rim.append(bm_tip.verts.new((math.cos(a) * r, cy + rng.uniform(-0.03, 0.06), math.sin(a) * r)))
    faces = []
    for i in range(spokes):
        j = (i + 1) % spokes
        faces.append(bm_tip.faces.new((hub, rim[i], rim[j])))
    for f in faces:
        f.smooth = True
    mg.make_double_sided(bm_tip)

    body = mg.new_object(name + "_body", bm_body)
    tip = mg.new_object(name + "_tip", bm_tip)
    mg.box_uv(body, tile=0.5)
    mg.planar_uv(tip, axis="Y", tile=0.6)
    return body, tip


# ── 계열 3: 뇌 산호 ───────────────────────────────────────────────────────────
def build_brain(name, seed, width):
    rng = mg.Rng(seed)
    bm_body = bmesh.new()
    bm_tip = bmesh.new()

    # 본체: 눌린 혹 구.
    lump_sphere(bm_body, rng, (0, width * 0.30, 0), width * 0.5, lumps=0.12, subdiv=2, squash=0.62)
    # 주름 강조: 짧은 융기 튜브 몇 가닥을 표면 위에 얹는다(팁 색).
    ridges = rng.randint(4, 6)
    for _ in range(ridges):
        a = rng.uniform(0, math.tau)
        r = width * rng.uniform(0.12, 0.30)
        x, z = math.cos(a) * r, math.sin(a) * r
        y = width * 0.30 + math.sqrt(max(0.01, (width * 0.5) ** 2 - r * r)) * 0.55
        d = Vector((rng.uniform(-1, 1), rng.uniform(-0.2, 0.2), rng.uniform(-1, 1)))
        if d.length < 0.1:
            d = Vector((1, 0, 0))
        d = d.normalized()
        ln = width * rng.uniform(0.18, 0.34)
        taper_tube(bm_tip, (x - d.x * ln / 2, y, z - d.z * ln / 2),
                   (x + d.x * ln / 2, y + rng.uniform(-0.02, 0.02), z + d.z * ln / 2),
                   width * 0.035, width * 0.03, sides=4)

    body = mg.new_object(name + "_body", bm_body)
    tip = mg.new_object(name + "_tip", bm_tip)
    mg.box_uv(body, tile=0.4)
    mg.box_uv(tip, tile=0.4)
    return body, tip


# ── 계열 4: 부채 산호 ─────────────────────────────────────────────────────────
def build_fan(name, seed, width, height):
    rng = mg.Rng(seed)
    bm_body = bmesh.new()
    bm_tip = bmesh.new()

    # 부채 본판: 수직 평면의 부채꼴(리브 방사) - 단면 + 양면.
    spokes = 9
    base = Vector((0, 0.02, 0))
    rim_pts = []
    for i in range(spokes):
        a = math.pi * (0.12 + 0.76 * i / (spokes - 1))  # 부채 각도 범위
        r = height * (0.95 + rng.uniform(-0.12, 0.12))
        rim_pts.append(base + Vector((math.cos(a) * width * 0.5 * (r / height),
                                      math.sin(a) * r, rng.uniform(-0.015, 0.015))))
    hub = bm_body.verts.new(base)
    rim = [bm_body.verts.new(p) for p in rim_pts]
    faces = []
    for i in range(spokes - 1):
        faces.append(bm_body.faces.new((hub, rim[i], rim[i + 1])))
    for f in faces:
        f.smooth = True
    mg.make_double_sided(bm_body)
    # 리브(팁 색): 허브에서 가장자리로 가는 가는 튜브들.
    for i in range(0, spokes, 2):
        taper_tube(bm_tip, base, rim_pts[i], height * 0.02, height * 0.008, sides=4)
    # 밑동 줄기.
    taper_tube(bm_tip, (0, 0, 0), (0, 0.10, 0), height * 0.035, height * 0.03, sides=5)

    body = mg.new_object(name + "_body", bm_body)
    tip = mg.new_object(name + "_tip", bm_tip)
    mg.planar_uv(body, axis="Z", tile=0.5)
    mg.box_uv(tip, tile=0.5)
    return body, tip


# ── 계열 5: 관 산호 ───────────────────────────────────────────────────────────
def build_tube(name, seed, height, count):
    rng = mg.Rng(seed)
    bm_body = bmesh.new()
    bm_tip = bmesh.new()

    for i in range(count):
        a = math.tau * i / count + rng.uniform(-0.4, 0.4)
        r = height * rng.uniform(0.10, 0.28)
        x, z = math.cos(a) * r, math.sin(a) * r
        h = height * rng.uniform(0.55, 1.0)
        lean = Vector((rng.uniform(-0.15, 0.15), 1.0, rng.uniform(-0.15, 0.15))).normalized()
        top = Vector((x, 0, z)) + lean * h
        taper_tube(bm_body, (x, 0, z), top, height * rng.uniform(0.07, 0.10), height * 0.06, sides=6)
        # 관 입구(팁 색): 꼭대기에 짧은 고리 튜브.
        taper_tube(bm_tip, top, top + lean * (height * 0.06), height * 0.062, height * 0.052, sides=6)

    body = mg.new_object(name + "_body", bm_body)
    tip = mg.new_object(name + "_tip", bm_tip)
    mg.box_uv(body, tile=0.4)
    mg.box_uv(tip, tile=0.4)
    return body, tip


# ── 명세표: 이름, 시드, 파라미터 ──────────────────────────────────────────────
BRANCH = [  # (이름, 시드, 높이, 퍼짐, 뿌리가지 수)
    ("coral_branch_a", 61001, 0.85, 0.55, 4),
    ("coral_branch_b", 61013, 1.15, 0.45, 5),
    ("coral_branch_c", 61027, 0.60, 0.75, 3),
    ("coral_branch_d", 61039, 1.45, 0.38, 5),
    ("coral_branch_e", 61051, 0.95, 0.62, 4),
    ("coral_branch_f", 61063, 1.30, 0.55, 6),
]
TABLE = [  # (이름, 시드, 상판 폭, 높이)
    ("coral_table_a", 62001, 1.10, 0.55),
    ("coral_table_b", 62017, 1.60, 0.70),
    ("coral_table_c", 62029, 0.80, 0.42),
    ("coral_table_d", 62041, 1.35, 0.60),
]
BRAIN = [  # (이름, 시드, 폭)
    ("coral_brain_a", 63001, 0.70),
    ("coral_brain_b", 63019, 1.05),
    ("coral_brain_c", 63031, 0.48),
]
FAN = [  # (이름, 시드, 폭, 높이)
    ("coral_fan_a", 64001, 0.90, 0.95),
    ("coral_fan_b", 64013, 1.30, 1.25),
    ("coral_fan_c", 64027, 0.65, 0.70),
    ("coral_fan_d", 64039, 1.05, 1.50),
]
TUBE = [  # (이름, 시드, 높이, 관 수)
    ("coral_tube_a", 65001, 0.60, 4),
    ("coral_tube_b", 65017, 0.90, 6),
    ("coral_tube_c", 65029, 0.45, 3),
]

PREVIEW = {  # 계열별 프리뷰 색 (본체, 팁) - 런타임 색은 코드가 입힌다.
    "branch": ((0.85, 0.45, 0.50), (0.95, 0.75, 0.72)),
    "table": ((0.80, 0.60, 0.35), (0.92, 0.80, 0.55)),
    "brain": ((0.72, 0.62, 0.42), (0.85, 0.78, 0.55)),
    "fan": ((0.75, 0.35, 0.55), (0.92, 0.62, 0.75)),
    "tube": ((0.55, 0.45, 0.75), (0.80, 0.72, 0.92)),
}


def main():
    total = 0
    manifest = []
    for name, seed, h, s, b in BRANCH:
        mg.reset_scene()
        body, tip = build_branch(name, seed, h, s, b)
        st = export_two_part(name, body, tip, 1500, 100, *PREVIEW["branch"])
        manifest.append((name, st["tris"], tuple(round(v, 2) for v in st["size"])))
        total += 1
    for name, seed, w, h in TABLE:
        mg.reset_scene()
        body, tip = build_table(name, seed, w, h)
        st = export_two_part(name, body, tip, 1500, 40, *PREVIEW["table"])
        manifest.append((name, st["tris"], tuple(round(v, 2) for v in st["size"])))
        total += 1
    for name, seed, w in BRAIN:
        mg.reset_scene()
        body, tip = build_brain(name, seed, w)
        st = export_two_part(name, body, tip, 1500, 100, *PREVIEW["brain"])
        manifest.append((name, st["tris"], tuple(round(v, 2) for v in st["size"])))
        total += 1
    for name, seed, w, h in FAN:
        mg.reset_scene()
        body, tip = build_fan(name, seed, w, h)
        st = export_two_part(name, body, tip, 1500, 40, *PREVIEW["fan"])
        manifest.append((name, st["tris"], tuple(round(v, 2) for v in st["size"])))
        total += 1
    for name, seed, h, c in TUBE:
        mg.reset_scene()
        body, tip = build_tube(name, seed, h, c)
        st = export_two_part(name, body, tip, 1500, 60, *PREVIEW["tube"])
        manifest.append((name, st["tris"], tuple(round(v, 2) for v in st["size"])))
        total += 1

    print("CORAL_MANIFEST")
    for name, tris, size in manifest:
        print(f"  {name}  {tris}tri  {size[0]}x{size[1]}x{size[2]}m")
    print(f"[coral] 완료 - {total}종")


if __name__ == "__main__":
    main()
