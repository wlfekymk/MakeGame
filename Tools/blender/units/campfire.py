#!/usr/bin/env python3
"""
campfire_a - 모닥불 실물 모델 (2026-08-17, 그래픽 로드맵 2번).

    python3 Tools/blender/units/campfire.py

산출물
  Assets/_Project/Resources/Models/campfire_a.obj   `o` 오브젝트 3개(머티리얼 단위)
  Tools/blender/_preview/campfire_a.png             렌더 - 저장소에 넣지 않는다

왜 이 형태인가
  현재 모닥불은 프리팹의 프리미티브 조합(원기둥/큐브)이라 20m 밖에서 "갈색 원통 무더기"다.
  실물 문법으로 교체한다: (1) 돌 둘레 9개 - 불가에 두른 돌이 "관리되는 불"을 읽게 한다,
  (2) 티피(원뿔형)로 기대 세운 장작 5개 - 불붙는 모닥불의 상징 실루엣,
  (3) 바닥 숯/재 원판 + 타다 만 장작 2개 - 사용감. 불꽃/연기는 기존 CampfireEffect가 얹는다.

오브젝트 3개 = 런타임 머티리얼 3개 (o 순서 = 서브메시 순서):
  campfire_stone  돌 둘레          campfire_wood  장작(티피+타다 만 것)
  campfire_char   숯/재 바닥

크기: 지름 1.25m, 높이 0.62m(티피 꼭대기). 원점 접지 중심.
usemtl 주입은 airliner.py와 같은 이유(Unity 6.5 임포터의 머티리얼 단위 서브메시 병합).
시드 55117 고정. 같은 시드 = 같은 메시 = 같은 md5.
"""

import math
import os
import sys

_UNITS_DIR = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.dirname(_UNITS_DIR))

import mgbuild as mg  # noqa: E402
import bmesh  # noqa: E402
from mathutils import Matrix, Vector  # noqa: E402

SEED = 55117


def rock_lump(bm, rng, center, size):
    """돌 하나: 육면체를 무작위로 깎은 낮은 덩어리(rock.py 문법의 초저가판)."""
    result = bmesh.ops.create_cube(bm, size=1.0)
    verts = result["verts"]
    for v in verts:
        v.co.x *= size[0] * (1.0 + rng.uniform(-0.18, 0.18))
        v.co.y *= size[1] * (1.0 + rng.uniform(-0.15, 0.15))
        v.co.z *= size[2] * (1.0 + rng.uniform(-0.18, 0.18))
    m = (Matrix.Translation(Vector(center))
         @ Matrix.Rotation(math.radians(rng.uniform(0, 180)), 4, "Y"))
    for v in verts:
        v.co = m @ v.co
    for f in bm.faces:
        f.smooth = False
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])


def log_stick(bm, rng, tail, tip, radius, sides=7):
    """장작 하나: 꼬리->끝으로 가늘어지는 다각 막대."""
    tail = Vector(tail)
    tip = Vector(tip)
    axis = (tip - tail).normalized()
    # 축에 수직인 기저 2개.
    up = Vector((0, 1, 0)) if abs(axis.y) < 0.9 else Vector((1, 0, 0))
    u = axis.cross(up).normalized()
    w = axis.cross(u).normalized()

    loops = []
    for t, r in ((0.0, radius), (1.0, radius * 0.72)):
        center = tail.lerp(tip, t)
        loop = []
        for i in range(sides):
            a = math.tau * i / sides
            rr = r * (1.0 + rng.uniform(-0.10, 0.10))
            loop.append(bm.verts.new(center + u * math.cos(a) * rr + w * math.sin(a) * rr))
        loops.append(loop)

    faces = []
    lo, hi = loops
    for i in range(sides):
        j = (i + 1) % sides
        f = bm.faces.new((lo[i], lo[j], hi[j], hi[i]))
        f.smooth = True
        faces.append(f)
    caps = [bm.faces.new(lo), bm.faces.new(list(reversed(hi)))]
    for f in caps:
        f.smooth = False
    bmesh.ops.recalc_face_normals(bm, faces=faces + caps)


def build_stone(rng):
    """돌 둘레 9개 - 반지름 0.55m 원 위에 크기 지터를 줘 배치."""
    bm = bmesh.new()
    count = 9
    for i in range(count):
        a = math.tau * i / count + rng.uniform(-0.12, 0.12)
        r = 0.55 * (1.0 + rng.uniform(-0.06, 0.06))
        s = rng.uniform(0.14, 0.20)
        h = rng.uniform(0.10, 0.16)
        rock_lump(bm, rng, (math.cos(a) * r, h * 0.42, math.sin(a) * r), (s, h, s * 0.85))
    obj = mg.new_object("campfire_stone", bm)
    mg.box_uv(obj, tile=0.6)
    return obj


def build_wood(rng):
    """장작: 티피로 기대 세운 5개 + 바닥에 타다 만 2개."""
    bm = bmesh.new()
    count = 5
    apex_h = 0.58
    for i in range(count):
        a = math.tau * i / count + rng.uniform(-0.15, 0.15)
        foot_r = 0.34 * (1.0 + rng.uniform(-0.08, 0.08))
        tip_off = 0.06
        log_stick(bm, rng,
                  (math.cos(a) * foot_r, 0.05, math.sin(a) * foot_r),
                  (math.cos(a) * -tip_off, apex_h, math.sin(a) * -tip_off),
                  rng.uniform(0.035, 0.045))
    # 타다 만 장작 2개 - 재 위에 눕는다.
    log_stick(bm, rng, (-0.30, 0.05, 0.10), (0.24, 0.07, -0.14), 0.040)
    log_stick(bm, rng, (0.06, 0.05, 0.26), (-0.12, 0.06, -0.28), 0.036)
    obj = mg.new_object("campfire_wood", bm)
    mg.cylinder_uv(obj, tile=0.5)
    return obj


def build_char(rng):
    """숯/재 바닥: 불규칙 원판(위만 보인다) - 지름 0.9m."""
    bm = bmesh.new()
    spokes = 14
    hub = bm.verts.new((0.0, 0.02, 0.0))
    rim = []
    for i in range(spokes):
        a = math.tau * i / spokes
        r = 0.45 * (1.0 + rng.uniform(-0.18, 0.18))
        rim.append(bm.verts.new((math.cos(a) * r, 0.018, math.sin(a) * r)))
    faces = [bm.faces.new((hub, rim[i], rim[(i + 1) % spokes])) for i in range(spokes)]
    for f in faces:
        f.smooth = False
        if f.normal.y < 0:
            f.normal_flip()
    obj = mg.new_object("campfire_char", bm)
    mg.planar_uv(obj, axis="Y", tile=0.8)
    return obj


def inject_usemtl(path):
    """각 `o <이름>` 줄 뒤에 `usemtl <이름>`을 넣고, 같은 이름의 .mtl 파일을 함께 쓴다.

    [실사고 2건의 종착지] Unity 6.5 OBJ 임포터는 서브메시를 머티리얼 단위로 만드는데,
    (1) usemtl이 아예 없으면 서브메시 1개로 병합되고(0.2.13 "색상이 회색"),
    (2) usemtl이 있어도 mtllib가 가리키는 실제 .mtl에서 해석되지 않으면 무시되어
        여전히 서브메시 1개다(0.2.21 검증에서 "병합 메시 sub1" 로그로 발각).
    그래서 mtllib 선언 + newmtl 목록이 든 최소 .mtl을 동봉한다. 계약 3장("외부 .mtl 의존
    금지")의 취지는 "머티리얼은 런타임 코드가 만든다"이고, 이 .mtl은 색을 정의하는 파일이
    아니라 서브메시 구분자다(런타임이 어차피 MG~ 머티리얼로 갈아끼운다).
    """
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
            name = line[2:].strip()
            names.append(name)
            out.append("usemtl " + name + chr(10))
    with open(path, "w") as fh:
        fh.writelines(out)
    mtl_path = os.path.join(os.path.dirname(path), mtl_name)
    with open(mtl_path, "w") as fh:
        for n in names:
            fh.write("newmtl " + n + chr(10) + "Kd 0.8 0.8 0.8" + chr(10) + chr(10))


def main():
    mg.reset_scene()
    rng = mg.Rng(SEED)

    objs = [
        build_stone(rng.sub(1)),
        build_wood(rng.sub(2)),
        build_char(rng.sub(3)),
    ]
    for o in objs:
        mg.triangulate(o)

    preview_colors = {
        "campfire_stone": (0.46, 0.45, 0.43),
        "campfire_wood": (0.38, 0.26, 0.16),
        "campfire_char": (0.07, 0.06, 0.06),
    }
    for o in objs:
        mg.assign_material(o, mg.preview_material("pv_" + o.name,
                                                  base_color=preview_colors[o.name]))

    stats = mg.enforce_contract_group(objs, tri_budget=mg.TRI_BUDGET["small_prop"],
                                      tri_floor=200, name="campfire_a", align="ground")

    out = os.path.join(mg.MODELS_DIR, "campfire_a.obj")
    mg.export_obj(objs, out)
    mg.verify_obj_file(out, stats)
    inject_usemtl(out)

    png = os.path.join(mg.PREVIEW_DIR, "campfire_a.png")
    mg.turntable(objs, png, title="campfire_a", stats=stats,
                 notes="seed %d / stones 9 / teepee 5" % SEED)
    mg.report(stats)


if __name__ == "__main__":
    main()
