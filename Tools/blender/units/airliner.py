#!/usr/bin/env python3
"""
airliner_wreck_a v2 - 시작 섬 해안의 폭발한 여객기 잔해, 실물급·내부 진입형 (2026-08-17).

    python3 Tools/blender/units/airliner.py

v1(전장 26m, Ø2.6m, 닫힌 동체) -> v2 개정 사유 (사용자 피드백 3건):
  1. "생각보다 작아 / 사람이 들어가 움직일 수 있어야" -> 실물급으로 확대:
     동체 지름 3.9m(내부 유효 약 3.3m), 전장 38m, 꼬리 높이 9.6m.
     동체는 **양면 셸**이고 절단부가 뚫려 있어 안으로 걸어 들어갈 수 있다.
     객실 바닥판(내부 y 0.95)이 있어 서 있는 높이(약 3m)가 나온다.
  2. "색상이 회색" -> 근본 원인은 OBJ에 usemtl이 없어 Unity 6.5 임포터가 서브메시를
     **1개로 합쳐** 첫 머티리얼(회색)만 칠해진 것. 내보낸 뒤 각 `o` 블록에 usemtl을
     주입해 서브메시 5개(파츠 순서 그대로)를 보장한다. 배색 자체도 흰 동체로 밝힌다
     (런타임 색은 AirlinerWreck.cs - 이 파일은 프리뷰 색만 갖는다).
  3. "상호작용" -> 콜라이더 명세를 내부 보행용(바닥/벽/천장 분해)으로 바꾸고,
     상호작용 자체는 AirlinerWreck.cs / InteractionController.cs가 담당한다.

오브젝트 5개 = 런타임 머티리얼 5개 (o 순서 = 서브메시 순서 = 머티리얼 배열 순서):
  airliner_hull    흰 동체·날개·꼬리      airliner_dark    엔진·파편·내부 바닥
  airliner_stripe  빨간 리버리·꼬리 띠     airliner_window  조종석·승객 창 띠
  airliner_soot    지면 그을음

시드 78211 고정. 같은 시드 = 같은 메시 = 같은 md5.
콜라이더/연기 명세는 끝에서 JSON으로 출력한다(후방부는 요/이동을 미리 계산한 최종 좌표).
"""

import json
import math
import os
import sys

_UNITS_DIR = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.dirname(_UNITS_DIR))

import mgbuild as mg  # noqa: E402
import bmesh  # noqa: E402
import bpy  # noqa: E402
from mathutils import Matrix, Vector  # noqa: E402

SEED = 78211
FUSE_R = 1.95          # 동체 반지름(m) - 내부 유효 지름 약 3.3m
SIDES = 18
CY = 2.05              # 몸통 구간 중심선 높이(배가 땅에 닿는 값: 2.05-1.95=0.10 - 롤 2도 여유)
FLOOR_Y = 0.95         # 객실 바닥판 윗면(내부 보행 기준면)

REAR_YAW_DEG = 24.0
REAR_SHIFT = Vector((-4.2, 0.0, 0.0))
REAR_ROLL_DEG = -4.0

FRONT_BREAK_Z = 2.0    # 전방 동체 절단부(열린 끝)
NOSE_TIP_Z = 21.0
REAR_BREAK_Z = -1.0    # 후방 동체 절단부(로컬, 열린 끝)
TAIL_TIP_Z = -17.2


# ── 공용 소도구 ────────────────────────────────────────────────────────────────
def tube_along_z(bm, rings, sides=SIDES, cap_lo=True, cap_hi=True, smooth=True):
    """Z축 방향 튜브(단면은 XY 원). mg.swept_tube의 축만 바꾼 변형 - v1과 동일."""
    loops = []
    for center, radius in rings:
        radii = radius if isinstance(radius, (list, tuple)) else [radius] * sides
        loop = []
        for i in range(sides):
            a = math.tau * i / sides
            loop.append(bm.verts.new((center.x + math.cos(a) * radii[i],
                                      center.y + math.sin(a) * radii[i],
                                      center.z)))
        loops.append(loop)

    faces = []
    for lo, hi in zip(loops, loops[1:]):
        for i in range(sides):
            j = (i + 1) % sides
            f = bm.faces.new((lo[i], lo[j], hi[j], hi[i]))
            f.smooth = smooth
            faces.append(f)
    caps = []
    if cap_lo:
        caps.append(bm.faces.new(loops[0]))
    if cap_hi:
        caps.append(bm.faces.new(loops[-1]))
    for f in caps:
        f.smooth = False
    bmesh.ops.recalc_face_normals(bm, faces=faces + caps)
    return loops


def slab(bm, corners4, thickness, up=Vector((0, 1, 0)), smooth=False):
    """4개 꼭짓점으로 두께 있는 판. 날개/꼬리/바닥판/파편용."""
    half = up.normalized() * (thickness * 0.5)
    top = [bm.verts.new(Vector(c) + half) for c in corners4]
    bot = [bm.verts.new(Vector(c) - half) for c in corners4]
    faces = [bm.faces.new(top), bm.faces.new(list(reversed(bot)))]
    for i in range(4):
        j = (i + 1) % 4
        faces.append(bm.faces.new((top[i], top[j], bot[j], bot[i])))
    for f in faces:
        f.smooth = smooth
    bmesh.ops.recalc_face_normals(bm, faces=faces)


def box(bm, center, size, yaw_deg=0.0, pitch_deg=0.0):
    """중심·크기·요/피치의 직육면체(자기 중심 기준 회전). 창 띠/줄무늬/파편용."""
    result = bmesh.ops.create_cube(bm, size=1.0)
    verts = result["verts"]
    m = (Matrix.Translation(Vector(center))
         @ Matrix.Rotation(math.radians(yaw_deg), 4, "Y")
         @ Matrix.Rotation(math.radians(pitch_deg), 4, "X")
         @ Matrix.Diagonal(Vector(size).to_4d()))
    for v in verts:
        v.co = m @ v.co
    for f in bm.faces:
        f.smooth = False
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])


def ground_patch(bm, rng, center, radius, wobble=0.35, spokes=12, y=0.015):
    """지면 그을음: 위만 보이는 불규칙 부채꼴 원판."""
    hub = bm.verts.new((center[0], y, center[2]))
    rim = []
    for i in range(spokes):
        a = math.tau * i / spokes
        r = radius * (1.0 + rng.uniform(-wobble, wobble))
        rim.append(bm.verts.new((center[0] + math.cos(a) * r, y,
                                 center[2] + math.sin(a) * r)))
    faces = []
    for i in range(spokes):
        j = (i + 1) % spokes
        faces.append(bm.faces.new((hub, rim[i], rim[j])))
    for f in faces:
        f.smooth = False
        if f.normal.y < 0:
            f.normal_flip()


def transform_last(bm, before_count, matrix):
    bm.verts.ensure_lookup_table()
    for v in bm.verts[before_count:]:
        v.co = matrix @ v.co


def jagged(rng, base, count, amount):
    return [base + rng.uniform(-amount, amount) for _ in range(count)]


def rear_matrix():
    return (Matrix.Translation(REAR_SHIFT)
            @ Matrix.Rotation(math.radians(REAR_YAW_DEG), 4, "Y")
            @ Matrix.Rotation(math.radians(REAR_ROLL_DEG), 4, "Z"))


def yaw_point(p, yaw_deg, shift):
    """콜라이더 명세용: 로컬 점에 요+이동만 적용(롤은 콜라이더 박스에 반영하지 않는다)."""
    t = math.radians(yaw_deg)
    x, y, z = p
    return [round(x * math.cos(t) + z * math.sin(t) + shift[0], 2),
            round(y, 2),
            round(z * math.cos(t) - x * math.sin(t) + shift[2], 2)]


# ── 파츠 빌더 ─────────────────────────────────────────────────────────────────
def build_hull(rng):
    """흰 동체 계열: 전방/후방 동체(양면 셸, 절단부 개방) + 꼬리 + 날개 2장."""
    bm = bmesh.new()

    # 전방 동체 - 몸통은 수평(배가 접지), 기수 원뿔에서 중심선이 떨어진다(코 떨굼).
    front_rings = [
        (Vector((0.0, CY, FRONT_BREAK_Z)), jagged(rng, FUSE_R, SIDES, 0.30)),
        (Vector((0.0, CY, FRONT_BREAK_Z + 0.7)), FUSE_R),
        (Vector((0.0, CY, 9.0)), FUSE_R),
        (Vector((0.0, CY, 14.5)), FUSE_R),
        (Vector((0.0, CY - 0.02, 16.5)), FUSE_R * 0.97),
        (Vector((0.0, 1.75, 18.5)), FUSE_R * 0.80),
        (Vector((0.0, 1.22, 19.9)), FUSE_R * 0.52),
        (Vector((0.0, 0.52, NOSE_TIP_Z)), FUSE_R * 0.16),
    ]
    n0 = len(bm.verts)
    tube_along_z(bm, front_rings, cap_lo=False, cap_hi=True)  # 절단부 개방 = 걸어 들어간다
    transform_last(bm, n0, Matrix.Rotation(math.radians(2.0), 4, "Z"))

    # 후방 동체(로컬 z 오름차순: 꼬리 끝 -> 절단부) - 절단부 개방.
    rear_rings = [
        (Vector((0.0, 2.75, TAIL_TIP_Z)), FUSE_R * 0.15),
        (Vector((0.0, 2.35, -15.4)), FUSE_R * 0.38),
        (Vector((0.0, 2.15, -13.6)), FUSE_R * 0.69),
        (Vector((0.0, CY, -10.0)), FUSE_R * 0.97),
        (Vector((0.0, CY, -5.5)), FUSE_R),
        (Vector((0.0, CY, REAR_BREAK_Z)), jagged(rng, FUSE_R, SIDES, 0.30)),
    ]
    rm = rear_matrix()
    n0 = len(bm.verts)
    tube_along_z(bm, rear_rings, cap_lo=True, cap_hi=False)

    # 수직 꼬리날개(후방 로컬).
    slab(bm, [(0.0, 3.6, -12.6), (0.0, 3.6, -16.4),
              (0.0, 9.6, -17.6), (0.0, 9.6, -15.5)], 0.42, up=Vector((1, 0, 0)))
    # 수평 안정판 2장.
    slab(bm, [(0.3, 3.35, -14.0), (6.4, 3.75, -15.9),
              (6.4, 3.75, -17.2), (0.3, 3.35, -16.0)], 0.26)
    slab(bm, [(-0.3, 3.35, -14.0), (-6.4, 3.75, -15.9),
              (-6.4, 3.75, -17.2), (-0.3, 3.35, -16.0)], 0.26)
    transform_last(bm, n0, rm)

    # 오른쪽 날개(붙은 채 끝이 들림 - 콜라이더 경사로).
    slab(bm, [(1.5, 1.68, 11.4), (15.0, 2.85, 6.7),
              (15.0, 2.85, 4.3), (1.5, 1.68, 5.6)], 0.46)
    # 왼쪽 날개(뜯겨 나가 앞왼쪽 모래에 누움).
    n0 = len(bm.verts)
    slab(bm, [(0.0, 0.42, 2.6), (10.6, 0.20, 0.4),
              (10.6, 0.20, -1.9), (0.0, 0.42, -2.4)], 0.40)
    transform_last(bm, n0, Matrix.Translation(Vector((-12.4, 0.0, 11.6)))
                   @ Matrix.Rotation(math.radians(146.0), 4, "Y"))

    # 양면 셸 - 열린 절단부로 들여다본 내부가 컬링으로 사라지지 않게 한다.
    # (주의: 이후 remove_doubles 계열 호출 금지 - make_double_sided 주석.)
    mg.make_double_sided(bm)

    obj = mg.new_object("airliner_hull", bm)
    mg.box_uv(obj, tile=3.0)
    return obj


def build_dark(rng):
    """어두운 금속 계열: 엔진 2기 + 객실 바닥판 2장 + 파편 8장."""
    bm = bmesh.new()

    def nacelle(matrix):
        n0 = len(bm.verts)
        rings = [
            (Vector((0.0, 0.0, -1.8)), 0.80),
            (Vector((0.0, 0.0, -1.2)), 0.92),
            (Vector((0.0, 0.0, 1.0)), 0.92),
            (Vector((0.0, 0.0, 1.8)), 0.72),
        ]
        tube_along_z(bm, rings, sides=12)
        transform_last(bm, n0, matrix)

    nacelle(Matrix.Translation(Vector((7.6, 1.02, 6.6))))          # 오른날개 아래
    nacelle(Matrix.Translation(Vector((-3.4, 0.95, 4.6)))          # 뜯겨 나간 엔진
            @ Matrix.Rotation(math.radians(50.0), 4, "Y")
            @ Matrix.Rotation(math.radians(8.0), 4, "Z"))

    # 객실 바닥판 - 절단부로 걸어 들어와 서는 면. 윗면 y = FLOOR_Y.
    slab(bm, [(-1.55, FLOOR_Y - 0.08, FRONT_BREAK_Z + 0.2), (1.55, FLOOR_Y - 0.08, FRONT_BREAK_Z + 0.2),
              (1.55, FLOOR_Y - 0.08, 16.6), (-1.55, FLOOR_Y - 0.08, 16.6)], 0.16)
    n0 = len(bm.verts)
    slab(bm, [(-1.55, FLOOR_Y - 0.08, -12.2), (1.55, FLOOR_Y - 0.08, -12.2),
              (1.55, FLOOR_Y - 0.08, REAR_BREAK_Z - 0.2), (-1.55, FLOOR_Y - 0.08, REAR_BREAK_Z - 0.2)], 0.16)
    transform_last(bm, n0, rear_matrix())

    # 흩어진 파편 판 8장 - 절단부 주위.
    for _ in range(8):
        cx = rng.uniform(-6.5, 5.0)
        cz = rng.uniform(-3.5, 6.0)
        w = rng.uniform(0.7, 1.9)
        d = rng.uniform(0.6, 1.5)
        box(bm, (cx, 0.12, cz), (w, 0.10, d), yaw_deg=rng.uniform(0, 180))

    obj = mg.new_object("airliner_dark", bm)
    mg.box_uv(obj, tile=1.8)
    return obj


def build_stripe():
    """리버리: 동체 옆 빨간 줄무늬(창 띠 아래, 높이 0.5m) + 꼬리날개 사선 띠."""
    bm = bmesh.new()

    for sx in (+1, -1):
        box(bm, (sx * FUSE_R * 0.99, CY - 0.55, 9.4), (0.12, 0.50, 12.6))
    rm = rear_matrix()
    for sx in (+1, -1):
        n0 = len(bm.verts)
        box(bm, (sx * FUSE_R * 0.99, CY - 0.55, -6.4), (0.12, 0.50, 10.4))
        transform_last(bm, n0, rm)
    n0 = len(bm.verts)
    slab(bm, [(0.0, 4.6, -15.0), (0.0, 4.6, -16.6),
              (0.0, 9.3, -17.5), (0.0, 9.3, -16.4)], 0.50, up=Vector((1, 0, 0)))
    transform_last(bm, n0, rm)

    obj = mg.new_object("airliner_stripe", bm)
    mg.box_uv(obj, tile=1.2)
    return obj


def build_window():
    """창: 조종석 2면 + 전방/후방 승객 창 띠."""
    bm = bmesh.new()

    for sx in (+1, -1):
        box(bm, (sx * FUSE_R * 0.97, CY + 0.72, 9.0), (0.12, 0.28, 11.6))
    for sx in (+1, -1):
        box(bm, (sx * 0.78, 2.35, 18.4), (0.08, 0.52, 1.55),
            yaw_deg=sx * 28.0, pitch_deg=-14.0)
    rm = rear_matrix()
    for sx in (+1, -1):
        n0 = len(bm.verts)
        box(bm, (sx * FUSE_R * 0.97, CY + 0.72, -6.0), (0.12, 0.28, 9.4))
        transform_last(bm, n0, rm)

    obj = mg.new_object("airliner_window", bm)
    mg.box_uv(obj, tile=1.2)
    return obj


def build_soot(rng):
    """지면 그을음 3곳."""
    bm = bmesh.new()
    ground_patch(bm, rng, (0.4, 0.0, 1.6), 4.4)
    ground_patch(bm, rng, (-3.6, 0.0, 4.6), 2.2, y=0.012)
    ground_patch(bm, rng, (-5.2, 0.0, -1.4), 3.0, y=0.018)
    obj = mg.new_object("airliner_soot", bm)
    mg.planar_uv(obj, axis="Y", tile=2.6)
    return obj


# ── 조립·계약·출력 ────────────────────────────────────────────────────────────
def main():
    mg.reset_scene()
    rng = mg.Rng(SEED)

    objs = [
        build_hull(rng.sub(1)),
        build_dark(rng.sub(2)),
        build_stripe(),
        build_window(),
        build_soot(rng.sub(3)),
    ]
    for o in objs:
        mg.triangulate(o)

    preview_colors = {
        "airliner_hull": (0.92, 0.93, 0.94),
        "airliner_dark": (0.24, 0.26, 0.28),
        "airliner_stripe": (0.68, 0.13, 0.11),
        "airliner_window": (0.08, 0.10, 0.13),
        "airliner_soot": (0.05, 0.05, 0.05),
    }
    for o in objs:
        mg.assign_material(o, mg.preview_material("pv_" + o.name,
                                                  base_color=preview_colors[o.name]))

    lo_before, _ = mg.union_bbox(objs)
    stats = mg.enforce_contract_group(objs, tri_budget=mg.TRI_BUDGET["hero"],
                                      tri_floor=1500, name="airliner_wreck_a",
                                      align="ground")
    lo_after, _ = mg.union_bbox(objs)
    off = [lo_after[i] - lo_before[i] for i in range(3)]
    align_offset = [round(v, 4) for v in off]

    out = os.path.join(mg.MODELS_DIR, "airliner_wreck_a.obj")
    # 검증 먼저, usemtl 주입은 그 다음이다. verify_obj_file은 머티리얼 참조를 계약 위반으로
    # 거절하는데(외부 .mtl 의존 금지가 취지), 여기서 넣는 usemtl은 mtllib 없는 **서브메시
    # 구분자**라 파일 의존이 없다 - 지오메트리 검증을 통과한 파일에 구분자만 덧붙인다.
    mg.export_obj(objs, out)
    mg.verify_obj_file(out, stats)
    mg.inject_usemtl(out)

    png = os.path.join(mg.PREVIEW_DIR, "airliner_wreck_a.png")
    mg.turntable(objs, png, title="airliner_wreck_a v2", stats=stats,
                 notes="seed %d / R %.2f / interior open" % (SEED, FUSE_R))

    # ── 콜라이더 명세 v2 (내부 보행용) ──
    # 좌표는 정렬 전 로컬. alignOffset을 center에 더하면 최종 메시 좌표.
    # 전방 객실: 바닥(윗면 FLOOR_Y) + 좌우 벽 + 천장 + 기수 막음. 절단부는 열어 둔다.
    ry, rs = REAR_YAW_DEG, (REAR_SHIFT.x, 0.0, REAR_SHIFT.z)
    colliders = [
        {"name": "cabin_floor_front", "center": [0.0, 0.435, 9.4], "size": [3.2, 1.03, 14.4], "yawDeg": 0.0,
         "note": "윗면 y=0.95(FLOOR_Y) - 내부 보행면"},
        {"name": "cabin_wall_front_L", "center": [-1.82, 2.4, 9.4], "size": [0.4, 2.9, 14.4], "yawDeg": 0.0},
        {"name": "cabin_wall_front_R", "center": [1.82, 2.4, 9.4], "size": [0.4, 2.9, 14.4], "yawDeg": 0.0},
        {"name": "cabin_ceiling_front", "center": [0.0, 4.05, 9.4], "size": [3.2, 0.5, 14.4], "yawDeg": 0.0},
        {"name": "nose_block", "center": [0.0, 1.35, 19.3], "size": [2.6, 2.6, 3.6], "yawDeg": 0.0},
        {"name": "cabin_floor_rear", "center": yaw_point((0.0, 0.435, -6.7), ry, rs),
         "size": [3.2, 1.03, 11.4], "yawDeg": ry, "note": "윗면 y=0.95 - 후방 객실 보행면"},
        {"name": "cabin_wall_rear_L", "center": yaw_point((-1.82, 2.4, -6.7), ry, rs),
         "size": [0.4, 2.9, 11.4], "yawDeg": ry},
        {"name": "cabin_wall_rear_R", "center": yaw_point((1.82, 2.4, -6.7), ry, rs),
         "size": [0.4, 2.9, 11.4], "yawDeg": ry},
        {"name": "cabin_ceiling_rear", "center": yaw_point((0.0, 4.05, -6.7), ry, rs),
         "size": [3.2, 0.5, 11.4], "yawDeg": ry},
        {"name": "tail_block", "center": yaw_point((0.0, 2.6, -14.6), ry, rs),
         "size": [2.4, 2.4, 4.6], "yawDeg": ry},
        {"name": "tail_fin", "center": yaw_point((0.0, 6.6, -16.0), ry, rs),
         "size": [0.6, 6.2, 3.2], "yawDeg": ry},
        {"name": "wing_right_ramp", "center": [8.2, 2.28, 7.0], "size": [13.6, 0.5, 4.6], "yawDeg": -19.0,
         "rollDeg": -4.9, "note": "루트 y1.68 -> 끝 y2.85 경사로(+X로 오름). 자식 회전 z -4.9도"},
        {"name": "wing_left_torn", "center": [-16.8, 0.31, 10.4], "size": [10.4, 0.7, 4.4], "yawDeg": -34.0},
        {"name": "engine_attached", "center": [7.6, 1.02, 6.6], "size": [1.9, 1.9, 3.8], "yawDeg": 0.0},
        {"name": "engine_torn", "center": [-3.4, 0.95, 4.6], "size": [1.9, 1.9, 3.8], "yawDeg": 50.0},
    ]
    smoke_points = [
        [0.6, 2.3, 1.2],                      # 전방 절단부
        yaw_point((0.3, 2.3, -0.7), ry, rs),  # 후방 절단부
        [-3.4, 1.7, 4.6],                     # 뜯긴 엔진
    ]
    manifest = {
        "size": [round(v, 3) for v in stats["size"]],
        "parts_tris": stats["parts"],
        "alignOffset": align_offset,
        "floorY": FLOOR_Y,
        "colliders": colliders,
        "smoke": smoke_points,
        "align_note": "colliders/smoke 좌표는 정렬 전 로컬 - alignOffset을 더하면 최종 메시 좌표.",
    }
    print("MANIFEST_JSON=" + json.dumps(manifest, ensure_ascii=False))
    mg.report(stats)


if __name__ == "__main__":
    main()
