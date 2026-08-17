#!/usr/bin/env python3
"""
airliner_wreck_a - 시작 섬 해안의 폭발한 여객기 잔해 (2026-08-17).

    python3 Tools/blender/units/airliner.py

산출물
  Assets/_Project/Resources/Models/airliner_wreck_a.obj   `o` 오브젝트 5개(머티리얼 단위)
  Tools/blender/_preview/airliner_wreck_a.png             렌더 - 저장소에 넣지 않는다

왜 이 형태인가 (사용자 요청: "내가 타고 온 여객기의 잔해 - 폭발한 여객기를 해안에")
  타이틀 아트(추락한 협동체 여객기가 해안을 지배하는 그림)와 게임 시작점을 일치시킨다.
  "추락"을 말하는 신호는 경비행기 잔해(AircraftWreck.cs [B29])와 같은 문법을 쓴다:
    (1) 동체가 날개 뒤에서 **두 동강** 나 있고, 후방 동체는 축이 어긋나(요 26도) 누워 있다.
    (2) 전방 동체는 기수를 낮춘 자세(추락 진행 방향), 후방 절단면 주위에 그을음.
    (3) 왼쪽 날개는 뜯겨 앞쪽 모래에 누워 있고, 엔진 하나는 절단부 옆에 나뒹군다.
    (4) 오른쪽 날개는 붙은 채 위로 들려 있다 - 콜라이더를 얹으면 올라갈 수 있는 경사로다.
    (5) 승객 창 띠 + 빨간 리버리 줄무늬가 "여객기"를 읽게 한다(경비행기와 구분되는 신호).

  오브젝트 5개 = 런타임 머티리얼 5개 (계약 4장 - 머티리얼은 코드가 만든다):
    airliner_hull    흰 동체·날개·꼬리      -> SalvageMetal 밝은 회백 "metal"
    airliner_dark    엔진·파편·절단면 안쪽  -> 어두운 금속 "metal"
    airliner_stripe  리버리 줄무늬·꼬리 띠   -> DangerRed 계열 "metal"
    airliner_window  조종석·승객 창 띠      -> 아주 어두운 유리색 "noise"
    airliner_soot    지면 그을음 자국        -> 거의 검정 "noise"

크기: 실기(협동체 38m)의 약 6할 - 전장 약 26m, 꼬리 높이 약 7m.
  시작 섬(Small)의 해안을 압도하되 섬을 잡아먹지 않는 선. 파츠 수 5(드로우콜 5).

시드 77031 고정. 같은 시드 = 같은 메시 = 같은 md5.
콜라이더 명세는 스크립트 끝에서 JSON 으로 출력한다(배치 코드가 박스 콜라이더로 옮겨 적는다).
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

SEED = 77031
FUSE_R = 1.32          # 동체 반지름(m)
SIDES = 16             # 동체 단면 변 수

# 후방 동체의 어긋남 (요 각도와 평행이동) - 콜라이더 명세와 공유한다.
REAR_YAW_DEG = 26.0
REAR_SHIFT = Vector((-3.2, 0.0, 0.0))
REAR_ROLL_DEG = -7.0


# ── 공용 소도구 ────────────────────────────────────────────────────────────────
def tube_along_z(bm, rings, sides=SIDES, cap_lo=True, cap_hi=True, smooth=True):
    """Z축 방향 튜브. rings = [(center Vector, 반지름 float 또는 측면별 리스트)] (z 오름차순).

    mg.swept_tube 는 단면이 수평(XZ)이라 세워진 줄기 전용이다 - 동체는 축이 Z 라서
    단면을 XY 평면에 깐 변형을 여기 둔다(감김은 recalc 로 일괄 정리, 같은 원칙).
    """
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
    """4개 평면 꼭짓점(평면상 시계/반시계 무관)으로 두께 있는 판을 만든다. 날개/꼬리/파편용."""
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
    """중심·크기·요/피치의 직육면체. 창 띠/줄무늬/파편용."""
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
    """지면 그을음: 위만 보이는 불규칙 부채꼴 원판(단면 - 위에서만 보므로 단면으로 충분)."""
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
    # 위를 향하게 - recalc 는 닫힌 몸체용이라 단면 부채꼴엔 손대지 않고 직접 확인한다.
    for f in faces:
        if f.normal.y < 0:
            f.normal_flip()


def transform_last(bm, before_count, matrix):
    """직전에 추가된 정점들(인덱스 before_count 이후)에만 행렬을 굽는다."""
    bm.verts.ensure_lookup_table()
    for v in bm.verts[before_count:]:
        v.co = matrix @ v.co


def jagged(rng, base, count, amount):
    """절단면용 측면별 반지름 - 찢긴 금속의 들쭉날쭉함."""
    return [base + rng.uniform(-amount, amount) for _ in range(count)]


# ── 파츠 빌더 (전부 잔해 로컬 미터 좌표 - 원점은 조립 후 접지 중심으로 보정) ──
def build_hull(rng):
    """흰 동체 계열: 전방 동체 + 후방 동체(+꼬리) + 날개 2장. 한 오브젝트."""
    bm = bmesh.new()

    # 전방 동체 - 기수를 +Z 로, 절단부는 z=1.6. 몸통은 배가 땅에 닿게 수평으로 눕고,
    # 기수 원뿔 구간에서 중심선이 급히 내려가 "코를 떨군" 자세가 된다.
    # 접지 계약: 모든 링에서 (중심 y - 그 링 반지름 - 롤 여유 0.10) >= 0 이어야 한다 -
    # 처음 설계는 중심선을 직선으로 내려 z=10 부근 배가 y=-0.49 까지 파고들었고,
    # 정렬이 전체를 들어올려 그을음 원판이 공중에 뜨는 사고가 났다(실측 alignOffset y 0.48).
    front_rings = [
        (Vector((0.0, 1.50, 1.6)), jagged(rng, FUSE_R, SIDES, 0.26)),
        (Vector((0.0, 1.44, 2.1)), FUSE_R),
        (Vector((0.0, 1.44, 6.0)), FUSE_R),
        (Vector((0.0, 1.44, 9.5)), FUSE_R),
        (Vector((0.0, 1.42, 11.2)), FUSE_R * 0.97),
        (Vector((0.0, 1.20, 12.4)), FUSE_R * 0.80),
        (Vector((0.0, 0.83, 13.2)), FUSE_R * 0.52),
        (Vector((0.0, 0.35, 13.8)), FUSE_R * 0.16),
    ]
    n0 = len(bm.verts)
    tube_along_z(bm, front_rings)
    transform_last(bm, n0, Matrix.Rotation(math.radians(4.0), 4, "Z"))  # 살짝 롤

    # 후방 동체 - 로컬로 z=-0.6(절단) ~ z=-10.4(꼬리 원뿔 끝)에 만들고 요/롤/이동을 굽는다.
    rear_rings = [
        (Vector((0.0, 1.52, -0.6)), jagged(rng, FUSE_R, SIDES, 0.26)),
        (Vector((0.0, 1.52, -1.1)), FUSE_R),
        (Vector((0.0, 1.52, -5.2)), FUSE_R),
        (Vector((0.0, 1.56, -7.4)), FUSE_R * 0.92),
        (Vector((0.0, 1.76, -9.2)), FUSE_R * 0.62),
        (Vector((0.0, 2.00, -10.4)), FUSE_R * 0.26),
    ]
    rear_m = (Matrix.Translation(REAR_SHIFT)
              @ Matrix.Rotation(math.radians(REAR_YAW_DEG), 4, "Y")
              @ Matrix.Rotation(math.radians(REAR_ROLL_DEG), 4, "Z"))
    n0 = len(bm.verts)
    tube_along_z(bm, rear_rings)

    # 수직 꼬리날개(후방 동체 로컬) - 위로 갈수록 뒤로 쓸린 판.
    slab(bm, [(0.0, 2.4, -7.6), (0.0, 2.4, -10.2),
              (0.0, 7.0, -11.0), (0.0, 7.0, -9.6)], 0.30, up=Vector((1, 0, 0)))
    # 수평 안정판 2장(후방 동체 로컬).
    slab(bm, [(0.2, 2.30, -8.6), (4.4, 2.55, -9.9),
              (4.4, 2.55, -10.8), (0.2, 2.30, -10.0)], 0.18)
    slab(bm, [(-0.2, 2.30, -8.6), (-4.4, 2.55, -9.9),
              (-4.4, 2.55, -10.8), (-0.2, 2.30, -10.0)], 0.18)
    transform_last(bm, n0, rear_m)

    # 오른쪽 날개(붙은 채 위로 들림) - 루트 z 5.7, 뒤로 25도 쓸림, 끝이 들려 경사로가 된다.
    slab(bm, [(1.0, 1.02, 7.3), (10.6, 1.95, 4.6),
              (10.6, 1.95, 2.9), (1.0, 1.02, 3.3)], 0.32)
    # 왼쪽 날개(뜯겨 나감) - 앞왼쪽 모래에 거의 평평하게 누움.
    n0 = len(bm.verts)
    slab(bm, [(0.0, 0.30, 1.8), (7.6, 0.16, 0.2),
              (7.6, 0.16, -1.4), (0.0, 0.30, -1.6)], 0.28)
    transform_last(bm, n0, Matrix.Translation(Vector((-8.6, 0.0, 8.0)))
                   @ Matrix.Rotation(math.radians(148.0), 4, "Y"))

    obj = mg.new_object("airliner_hull", bm)
    mg.box_uv(obj, tile=2.2)
    return obj


def build_dark(rng):
    """어두운 금속 계열: 엔진 나셀 2개 + 절단면 안쪽 원판 2장 + 파편 판 7장."""
    bm = bmesh.new()

    def nacelle(matrix):
        n0 = len(bm.verts)
        rings = [
            (Vector((0.0, 0.0, -1.3)), 0.58),
            (Vector((0.0, 0.0, -0.9)), 0.66),
            (Vector((0.0, 0.0, 0.7)), 0.66),
            (Vector((0.0, 0.0, 1.3)), 0.52),
        ]
        tube_along_z(bm, rings, sides=12)
        transform_last(bm, n0, matrix)

    # 오른쪽 날개 아래 엔진(붙어 있음).
    nacelle(Matrix.Translation(Vector((5.4, 0.72, 4.9))))
    # 뜯겨 나간 엔진 - 절단부 왼쪽 모래 위, 비스듬히.
    nacelle(Matrix.Translation(Vector((-2.6, 0.95, 3.4)))
            @ Matrix.Rotation(math.radians(55.0), 4, "Y")
            @ Matrix.Rotation(math.radians(8.0), 4, "Z"))

    # 절단면 안쪽(타 버린 내부) - 절단 링보다 살짝 안쪽의 원판.
    def burn_disc(center, normal_yaw_deg, r):
        n0 = len(bm.verts)
        hub = bm.verts.new((0.0, 0.0, 0.0))
        rim = [bm.verts.new((math.cos(math.tau * i / 12) * r,
                             math.sin(math.tau * i / 12) * r, 0.0)) for i in range(12)]
        faces = [bm.faces.new((hub, rim[i], rim[(i + 1) % 12])) for i in range(12)]
        for f in faces:
            f.smooth = False
        m = (Matrix.Translation(Vector(center))
             @ Matrix.Rotation(math.radians(normal_yaw_deg), 4, "Y"))
        transform_last(bm, n0, m)

    burn_disc((0.0, 1.50, 1.45), 0.0, FUSE_R * 0.95)          # 전방 절단면(뒤를 본다)
    rear_break_world = (Matrix.Translation(REAR_SHIFT)
                        @ Matrix.Rotation(math.radians(REAR_YAW_DEG), 4, "Y")) @ Vector((0.0, 1.52, -0.45))
    burn_disc(tuple(rear_break_world), REAR_YAW_DEG, FUSE_R * 0.95)  # 후방 절단면(앞을 본다)

    # 흩어진 파편 판 7장 - 절단부 주변 타원 안에 시드 고정 산포.
    for _ in range(7):
        cx = rng.uniform(-4.5, 3.5)
        cz = rng.uniform(-2.5, 4.5)
        w = rng.uniform(0.5, 1.3)
        d = rng.uniform(0.4, 1.0)
        box(bm, (cx, 0.09, cz), (w, 0.07, d), yaw_deg=rng.uniform(0, 180))

    obj = mg.new_object("airliner_dark", bm)
    mg.box_uv(obj, tile=1.4)
    return obj


def build_stripe():
    """리버리: 동체 옆 빨간 줄무늬(창 띠 아래) + 꼬리날개 사선 띠."""
    bm = bmesh.new()

    # 전방 동체 좌우 줄무늬 - 몸통 구간(z 2.5~11) 중심선이 수평이라 띠도 수평이다.
    for sx in (+1, -1):
        box(bm, (sx * FUSE_R * 0.99, 1.14, 6.6), (0.10, 0.30, 8.6))
    # 후방 동체 좌우 줄무늬(후방 변환을 굽는다).
    rear_m = (Matrix.Translation(REAR_SHIFT)
              @ Matrix.Rotation(math.radians(REAR_YAW_DEG), 4, "Y")
              @ Matrix.Rotation(math.radians(REAR_ROLL_DEG), 4, "Z"))
    for sx in (+1, -1):
        n0 = len(bm.verts)
        box(bm, (sx * FUSE_R * 0.99, 1.12, -4.0), (0.10, 0.30, 6.4))
        transform_last(bm, n0, rear_m)
    # 꼬리날개 사선 띠.
    n0 = len(bm.verts)
    slab(bm, [(0.0, 3.2, -9.1), (0.0, 3.2, -10.1),
              (0.0, 6.8, -10.85), (0.0, 6.8, -10.15)], 0.34, up=Vector((1, 0, 0)))
    transform_last(bm, n0, rear_m)

    obj = mg.new_object("airliner_stripe", bm)
    mg.box_uv(obj, tile=1.0)
    return obj


def build_window():
    """창: 조종석 2면 + 전방/후방 승객 창 띠(어두운 가로 띠 - 저폴리 문법)."""
    bm = bmesh.new()

    # 전방 승객 창 띠(좌우) - 몸통 구간이 수평이라 띠도 수평이다.
    for sx in (+1, -1):
        box(bm, (sx * FUSE_R * 0.97, 1.90, 6.4), (0.10, 0.17, 8.2))
    # 조종석 창 - 기수 위쪽에 좌우로 꺾인 판 2장.
    for sx in (+1, -1):
        box(bm, (sx * 0.52, 1.55, 12.35), (0.06, 0.34, 1.05),
            yaw_deg=sx * 28.0, pitch_deg=-14.0)
    # 후방 승객 창 띠(좌우, 후방 변환).
    rear_m = (Matrix.Translation(REAR_SHIFT)
              @ Matrix.Rotation(math.radians(REAR_YAW_DEG), 4, "Y")
              @ Matrix.Rotation(math.radians(REAR_ROLL_DEG), 4, "Z"))
    for sx in (+1, -1):
        n0 = len(bm.verts)
        box(bm, (sx * FUSE_R * 0.97, 2.00, -3.9), (0.10, 0.17, 5.8))
        transform_last(bm, n0, rear_m)

    obj = mg.new_object("airliner_window", bm)
    mg.box_uv(obj, tile=1.0)
    return obj


def build_soot(rng):
    """지면 그을음 3곳: 절단부 큰 자국 + 뜯긴 엔진 옆 + 후방 절단부 아래."""
    bm = bmesh.new()
    ground_patch(bm, rng, (0.2, 0.0, 1.2), 3.1)
    ground_patch(bm, rng, (-2.7, 0.0, 3.3), 1.5, y=0.012)
    ground_patch(bm, rng, (-3.4, 0.0, -0.9), 2.1, y=0.018)
    obj = mg.new_object("airliner_soot", bm)
    mg.planar_uv(obj, axis="Y", tile=2.0)
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

    # 프리뷰 색(렌더 검수용 - 런타임 머티리얼은 코드가 만든다, 계약 4장).
    preview_colors = {
        "airliner_hull": (0.80, 0.81, 0.82),
        "airliner_dark": (0.26, 0.28, 0.30),
        "airliner_stripe": (0.62, 0.15, 0.13),
        "airliner_window": (0.09, 0.11, 0.14),
        "airliner_soot": (0.05, 0.05, 0.05),
    }
    for o in objs:
        mg.assign_material(o, mg.preview_material("pv_" + o.name,
                                                  base_color=preview_colors[o.name]))

    lo_before, _ = mg.union_bbox(objs)
    stats = mg.enforce_contract_group(objs, tri_budget=mg.TRI_BUDGET["large_structure"],
                                      tri_floor=800, name="airliner_wreck_a",
                                      align="ground")
    lo_after, _ = mg.union_bbox(objs)
    align_offset = [round(lo_after[i] - lo_before[i], 4) for i in range(3)]

    out = os.path.join(mg.MODELS_DIR, "airliner_wreck_a.obj")
    mg.export_obj(objs, out)
    mg.verify_obj_file(out, stats)

    png = os.path.join(mg.PREVIEW_DIR, "airliner_wreck_a.png")
    mg.turntable(objs, png, title="airliner_wreck_a", stats=stats,
                 notes="seed %d / rear yaw %.0f / shift %.1f" % (SEED, REAR_YAW_DEG, REAR_SHIFT.x))

    # 콜라이더 명세 - 배치 코드(AirlinerWreck.cs)가 박스 콜라이더로 옮겨 적는다.
    # 좌표는 **정렬(접지 중심 보정) 후** 기준이어야 하므로, 정렬 오프셋을 출력에 반영한다.
    lo, hi = mg.union_bbox(objs)
    manifest = {
        "size": [round(v, 3) for v in stats["size"]],
        "parts_tris": stats["parts"],
        "colliders": [
            {"name": "fuse_front", "center": [0.0, 1.44, 7.6], "size": [2.7, 2.75, 12.4], "yawDeg": 0.0},
            {"name": "fuse_rear", "center": [-4.6, 1.52, -5.0], "size": [2.7, 2.75, 9.8], "yawDeg": REAR_YAW_DEG},
            {"name": "tail_fin", "center": [-7.5, 4.6, -9.4], "size": [0.5, 5.0, 2.6], "yawDeg": REAR_YAW_DEG},
            {"name": "wing_right_ramp", "center": [5.8, 1.42, 4.9], "size": [9.6, 0.4, 3.6], "yawDeg": 0.0,
             "note": "경사로 - 루트 y1.02 끝 y1.95, 뒤쓸림 25도. 회전 대신 두툼한 박스."},
            {"name": "wing_left_torn", "center": [-11.6, 0.22, 6.2], "size": [7.4, 0.5, 3.2], "yawDeg": -32.0},
            {"name": "engine_attached", "center": [5.4, 0.72, 4.9], "size": [1.4, 1.4, 2.8], "yawDeg": 0.0},
            {"name": "engine_torn", "center": [-2.6, 0.95, 3.4], "size": [1.4, 1.4, 2.8], "yawDeg": 55.0},
        ],
        "align_note": "colliders 좌표는 정렬 전 로컬 기준 - alignOffset 을 center 에 더하면 최종 메시 좌표다.",
        "alignOffset": align_offset,
        "post_align_bbox": {"lo": [round(v, 3) for v in lo], "hi": [round(v, 3) for v in hi]},
    }
    print("MANIFEST_JSON=" + json.dumps(manifest, ensure_ascii=False))
    mg.report(stats)


if __name__ == "__main__":
    main()
