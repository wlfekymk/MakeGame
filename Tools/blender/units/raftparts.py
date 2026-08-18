#!/usr/bin/env python3
"""
raft_* - Stranded Deep 방식 뗏목 부품 8종 (2026-08-18, 배 시스템 전면 재설계).

    python3 Tools/blender/units/raftparts.py

산출물
  Assets/_Project/Resources/Models/raft_*.obj (+ .mtl)   `o` 오브젝트 2개(머티리얼 단위)
  Tools/blender/_preview/_raft/*.png                     렌더 - 저장소에 넣지 않는다

왜 이 형태인가
  해안가에서 **격자로 바닥판을 이어 붙여** 뗏목을 만들고 그 위에 돛/키/닻/모터를 얹는다.
  격자 1칸 = **2.0m x 2.0m** 다(게임 배치 코드가 이 칸 크기로 놓는다). 그래서 바닥판 3종은
  XZ 가 **정확히 2.0 x 2.0** 이고, 칸 경계에서 무늬가 이어지도록 파츠 피치를 2.0의 약수로 잡았다.
    - raft_base_wood  : 통나무 5개, 피치 0.40m. 칸 경계에서 옆칸 통나무가 같은 피치로 이어진다.
    - raft_base_barrel: 세로 레일이 x=+-0.94(폭 0.12)라 옆칸 레일과 맞닿아 폭 0.24 겹레일이 된다.
                        가로보는 z=+-0.88 에서 x 를 꽉 채워 옆칸 가로보와 한 줄로 이어진다.
    - raft_base_buoy  : 테두리 판 + 결속 노끈이 x/z=+-1.0 까지 꽉 차서 옆칸과 맞물린다.
  가장자리가 정확히 +-1.0 이므로 이어 붙였을 때 **틈도 겹침도 0** 이다(seam_report 로 실측 출력).

`o` 오브젝트 2개 = 런타임 머티리얼 2개 (순서 = 서브메시 순서, wood 가 항상 먼저):
  wood   목재/천 등 밝은 부분
  metal  금속/돌/밧줄 등 어두운 부분
  ** raft_sail 만 예외로 `wood`(마스트/가로대) + `cloth`(천 돛) 다. ** 돛은 천이 주인공이라
  천을 metal 로 부르는 것이 어색하다 - 게임 쪽이 이 이름에 맞춰 색을 준다.
  모든 종이 두 그룹을 반드시 가진다(나무 바닥판의 결속 노끈, 닻의 나무 발톱 등이 그래서 있다).

삼각형: 전 종 small_prop(1500) 예산. 바닥판/바닥재는 한 척에 9~25칸이 깔리므로 800 이하로 더 조인다.
크기/원점: 미터, +Y up, +Z front, 밑면 y=0.
  바닥판 3종 + 바닥재는 **align="bbox"** 다 - 격자 타일이라 바운딩 박스 중심이 곧 칸 중심이어야
  하고, 접지 중심(ground)을 쓰면 통나무 끝 지터 때문에 XZ 가 1~2cm 밀려 이음새가 어긋난다.
  나머지 4종(돛/키/닻/모터)은 신규 에셋 기본값인 **align="ground"** 다.
시드 78001~78008 고정. 같은 시드 = 같은 메시 = 같은 md5(재실행 2회 대조함).
"""

import math
import os
import sys

_UNITS_DIR = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.dirname(_UNITS_DIR))

import mgbuild as mg  # noqa: E402
import bpy  # noqa: E402
import bmesh  # noqa: E402
from mathutils import Matrix, Vector  # noqa: E402

PREVIEW_DIR = os.path.join(mg.PREVIEW_DIR, "_raft")   # 전용 폴더(동시 실행 충돌 방지)

CELL = 2.0            # 격자 한 칸(m) - 게임 배치 코드와 같은 값
HALF = CELL * 0.5

PREVIEW_COLORS = {
    "wood": (0.52, 0.37, 0.21),
    "metal": (0.30, 0.31, 0.33),
    "cloth": (0.80, 0.76, 0.64),
}


# ── 공용 기하 헬퍼 ────────────────────────────────────────────────────────────
def box(bm, center, size, rot_y=0.0, smooth=False):
    """축 정렬 육면체(필요하면 Y 축으로 회전). size 는 전체 변 길이."""
    res = bmesh.ops.create_cube(bm, size=1.0)
    verts = res["verts"]
    m = (Matrix.Translation(Vector(center))
         @ Matrix.Rotation(rot_y, 4, "Y")
         @ Matrix.Diagonal(Vector(size).to_4d()))
    for v in verts:
        v.co = m @ v.co
    faces = list({f for v in verts for f in v.link_faces})
    for f in faces:
        f.smooth = smooth
    bmesh.ops.recalc_face_normals(bm, faces=faces)
    return faces


def poly_tube(bm, pts, radii, sides=8, up=None, smooth=True, cap=True, phase=0.0):
    """임의 축 다각 튜브. **단면이 축에 수직**이라 눕힌 통나무/드럼통에 그대로 쓴다.

    mgbuild.swept_tube 는 단면이 항상 수평(XZ)이라 세로 줄기 전용이다 - 여기서는 통나무를
    Z 축으로, 노끈을 X 축으로 눕혀야 해서 축 자유 버전이 필요했다.

    radii  : 링마다 스칼라 또는 (a, b). a 는 u 기저, b 는 w 기저 방향 반지름 -> **타원 단면**.
             통나무를 폭 0.40 / 높이 0.24 로 눌러 붙여 격자 피치에 딱 맞추는 데 쓴다.
    phase  : 스칼라 또는 링별 리스트. 링마다 단면을 돌리면 **비틀린 날**(프로펠러)이 된다.
    smooth : bool 또는 띠별 리스트.
    프레임(u, w)은 전체 축으로 **한 번만** 잡는다 - 링마다 다시 잡으면 비틀림 아티팩트가 난다.
    """
    pts = [Vector(p) for p in pts]
    if len(pts) < 2:
        raise mg.ContractError("poly_tube: 링이 2개 미만이다")
    axis = (pts[-1] - pts[0]).normalized()
    if up is None:
        up = Vector((0, 1, 0)) if abs(axis.y) < 0.9 else Vector((0, 0, 1))
    u = axis.cross(up)
    if u.length < 1e-6:
        u = Vector((1, 0, 0))
    u.normalize()
    w = axis.cross(u).normalized()

    phases = phase if isinstance(phase, (list, tuple)) else [phase] * len(pts)
    bands = smooth if isinstance(smooth, (list, tuple)) else [smooth] * (len(pts) - 1)

    loops = []
    for p, r, ph in zip(pts, radii, phases):
        a, b = r if isinstance(r, (list, tuple)) else (r, r)
        loop = []
        for i in range(sides):
            t = math.tau * i / sides
            e_u = u * math.cos(ph) + w * math.sin(ph)
            e_w = -u * math.sin(ph) + w * math.cos(ph)
            loop.append(bm.verts.new(p + e_u * (math.cos(t) * a) + e_w * (math.sin(t) * b)))
        loops.append(loop)

    faces = []
    for bi, (lo, hi) in enumerate(zip(loops, loops[1:])):
        for i in range(sides):
            j = (i + 1) % sides
            f = bm.faces.new((lo[i], lo[j], hi[j], hi[i]))
            f.smooth = bands[bi]
            faces.append(f)
    if cap:
        for lp, rev in ((loops[0], False), (loops[-1], True)):
            f = bm.faces.new(tuple(reversed(lp)) if rev else tuple(lp))
            f.smooth = False
            faces.append(f)
    bmesh.ops.recalc_face_normals(bm, faces=faces)
    return loops


def torus(bm, center, ring_r, tube_r, seg=10, sides=5, axis="Y", smooth=True):
    """닫힌 고리(밧줄 고리/드럼 결속 띠/키 쇠테). axis 는 고리면의 법선."""
    center = Vector(center)
    n = {"X": Vector((1, 0, 0)), "Y": Vector((0, 1, 0)), "Z": Vector((0, 0, 1))}[axis.upper()]
    e1 = Vector((0, 1, 0)) if abs(n.y) < 0.9 else Vector((1, 0, 0))
    e1 = (e1 - n * e1.dot(n)).normalized()
    e2 = n.cross(e1).normalized()

    loops = []
    for s in range(seg):
        a = math.tau * s / seg
        radial = e1 * math.cos(a) + e2 * math.sin(a)
        c = center + radial * ring_r
        loop = []
        for i in range(sides):
            t = math.tau * i / sides
            loop.append(bm.verts.new(c + radial * (math.cos(t) * tube_r)
                                     + n * (math.sin(t) * tube_r)))
        loops.append(loop)

    faces = []
    for s in range(seg):
        lo, hi = loops[s], loops[(s + 1) % seg]
        for i in range(sides):
            j = (i + 1) % sides
            f = bm.faces.new((lo[i], lo[j], hi[j], hi[i]))
            f.smooth = smooth
            faces.append(f)
    bmesh.ops.recalc_face_normals(bm, faces=faces)
    return faces


def extrude_poly(bm, pts, normal_axis, half_t, smooth=False):
    """평면 다각형을 법선 방향으로 두께 half_t*2 만큼 밀어 판재를 만든다(키 날/판).

    pts 는 중간면 위의 3D 점 리스트(감김 무시 - recalc_face_normals 가 정리한다).
    """
    n = {"X": Vector((1, 0, 0)), "Y": Vector((0, 1, 0)), "Z": Vector((0, 0, 1))}[normal_axis.upper()]
    front = [bm.verts.new(Vector(p) + n * half_t) for p in pts]
    back = [bm.verts.new(Vector(p) - n * half_t) for p in pts]
    faces = [bm.faces.new(front), bm.faces.new(tuple(reversed(back)))]
    for i in range(len(pts)):
        j = (i + 1) % len(pts)
        faces.append(bm.faces.new((front[i], front[j], back[j], back[i])))
    for f in faces:
        f.smooth = smooth
    bmesh.ops.recalc_face_normals(bm, faces=faces)
    return faces


def lump(bm, rng, center, size, lumps=0.16, subdiv=2, smooth=False):
    """지터 준 아이코스피어 덩어리(부표/닻돌). searock.py 와 같은 문법."""
    res = bmesh.ops.create_icosphere(bm, subdivisions=subdiv, radius=1.0)
    verts = res["verts"]
    m = Matrix.Rotation(rng.uniform(0, math.tau), 4, "Y")
    for v in verts:
        d = v.co.normalized()
        r = 1.0 + rng.uniform(-lumps, lumps)
        v.co = m @ Vector((d.x * size[0] * r * 0.5, d.y * size[1] * r * 0.5,
                           d.z * size[2] * r * 0.5))
        v.co += Vector(center)
    faces = list({f for v in verts for f in v.link_faces})
    for f in faces:
        f.smooth = smooth
    bmesh.ops.recalc_face_normals(bm, faces=faces)
    return faces


def zero_min_jitter(rng, n, amount):
    """[0, amount) 지터를 n 개 뽑되 **최솟값을 0 으로 밀어** 준다.

    통나무/널판 끝을 불규칙하게 하면서도 칸 크기는 정확히 2.0m 로 유지하는 장치다.
    (적어도 하나는 지터 0 = 가장자리에 닿는다 -> 바운딩 박스가 딱 떨어진다.)
    """
    js = [rng.uniform(0.0, amount) for _ in range(n)]
    m = min(js)
    return [j - m for j in js]


def obj_from(name, bm, uv="box", tile=0.6, **kw):
    o = mg.new_object(name, bm)
    if uv == "box":
        mg.box_uv(o, tile=tile)
    elif uv == "planar":
        mg.planar_uv(o, tile=tile, **kw)
    elif uv == "cyl":
        mg.cylinder_uv(o, tile=tile, **kw)
    return o


# ── 1. raft_base_wood - 통나무 바닥판 ─────────────────────────────────────────
def build_base_wood(rng):
    """통나무 5개(피치 0.40m)를 나란히 눕히고 노끈으로 십자 결속한 칸.

    통나무 단면을 **타원(폭 0.40 x 높이 0.24)** 으로 눌러 5개가 x 를 정확히 채우게 했다.
    옆칸 통나무가 같은 피치 0.40 으로 이어지므로 칸 경계에 이음새가 보이지 않는다.
    """
    pitch = CELL / 5.0                       # 0.40 - 2.0의 약수여야 격자가 이어진다
    a = pitch * 0.5                          # 반폭 0.20 -> 5개가 정확히 2.0
    bm_w = bmesh.new()
    front_j = zero_min_jitter(rng, 5, 0.05)  # 통나무 끝이 살짝 들쭉날쭉
    back_j = zero_min_jitter(rng, 5, 0.05)
    logs = []
    for i in range(5):
        cx = -HALF + a + pitch * i
        b = rng.uniform(0.108, 0.120)        # 반높이 -> 굵기 차이
        if i == 2:
            b = 0.120                        # 적어도 하나는 최대 굵기(높이 기준선)
        z0, z1 = -HALF + back_j[i], HALF - front_j[i]
        rings = [(cx, b, z0), (cx, b, z0 + 0.28), (cx, b, z1 - 0.28), (cx, b, z1)]
        radii = [(a, b * 0.86), (a, b), (a, b), (a, b * 0.86)]
        poly_tube(bm_w, [Vector(p) for p in rings], radii, sides=8, up=Vector((0, 1, 0)),
                  smooth=True)
        logs.append((cx, b))
    wood = obj_from("wood", bm_w, uv="box", tile=0.55)

    # 노끈: X 방향 2줄이 통나무 **등을 타고 넘고**(첫 렌더에서 직선 막대가 공중에 떠 보였다),
    # Z 방향 2줄이 통나무 사이 골에 눕는다 - 둘이 겹쳐 십자 결속이 된다.
    bm_m = bmesh.new()
    r_x = 0.023
    path = []
    for i, (cx, b) in enumerate(logs):
        if i == 0:
            path.append((-HALF, b + r_x * 0.55))            # 바깥 통나무 어깨를 감는다
        else:
            pb = logs[i - 1][1]
            hi_b = max(b, pb)
            path.append((cx - a, hi_b * 1.45 + r_x * 0.55))  # 골 - 팽팽해서 끝까지 안 내려간다
        path.append((cx, 2 * b + r_x * 0.55))                # 등마루
    path.append((HALF, logs[-1][1] + r_x * 0.55))
    for z in (-0.56, 0.56):
        poly_tube(bm_m, [(px, py, z) for px, py in path], [r_x] * len(path),
                  sides=6, up=Vector((0, 1, 0)))
    for x, b_pair in ((-0.6, (logs[0][1], logs[1][1])), (0.6, (logs[3][1], logs[4][1]))):
        gy = max(b_pair) * 1.30                              # 골 바닥에 눕는 줄
        poly_tube(bm_m, [(x, gy, -HALF + 0.08), (x, gy + 0.006, 0.0), (x, gy, HALF - 0.08)],
                  [0.020] * 3, sides=6, up=Vector((0, 1, 0)))
    metal = obj_from("metal", bm_m, uv="box", tile=0.25)
    return [wood, metal]


# ── 2. raft_base_barrel - 드럼통 바닥판 ───────────────────────────────────────
def build_base_barrel(rng):
    """금속 드럼통 2개를 눕혀 나무 틀에 묶은 칸. 부력 최고 = 가장 두껍다(0.55m).

    드럼통은 테두리 리브(굴림테)를 플랫 셰이딩 띠로 두어 멀리서도 '드럼통'으로 읽히게 한다
    (README 함정 10 - 굵기 변화만으로는 20m 밖에서 안 보인다).
    """
    bm_w = bmesh.new()
    # 세로 레일 2개 - z 를 꽉 채워 옆칸 레일과 한 줄로 이어진다.
    for x in (-0.94, 0.94):
        box(bm_w, (x, 0.25, 0.0), (0.12, 0.30, CELL))
    # 가로보 3개 - x 를 꽉 채워 옆칸 가로보와 이어지고, 윗면이 이 에셋의 최고점(0.55)이다.
    for z in (-0.88, 0.0, 0.88):
        box(bm_w, (0.0, 0.515, z), (CELL, 0.07, 0.13))
    wood = obj_from("wood", bm_w, uv="box", tile=0.5)

    bm_m = bmesh.new()
    body, rib = 0.225, 0.240
    cy = 0.255                                # 결속 띠(가장 바깥)가 바닥에 닿는다 -> 밑면 y=0
    for x in (-0.48, 0.48):
        # 리브(굴림테)는 **플랫 셰이딩 띠**로 둔다 - 법선이 끊겨 밝기 링이 생기고,
        # 그게 20m 밖에서 '드럼통'을 읽게 하는 유일한 수단이다(README 함정 10).
        # 끝은 평면 뚜껑 + 챠임(테두리 리브), 가운데는 굴림테 2줄. 링 12개로 예산을 맞춘다.
        z_prof = [(-0.92, body), (-0.89, rib), (-0.86, body),
                  (-0.33, body), (-0.30, rib), (-0.27, body),
                  (0.27, body), (0.30, rib), (0.33, body),
                  (0.86, body), (0.89, rib), (0.92, body)]
        pts = [(x, cy, z) for z, _ in z_prof]
        radii = [r for _, r in z_prof]
        smooth = [False, False, True, False, False, True,
                  False, False, True, False, False]
        poly_tube(bm_m, pts, radii, sides=10, up=Vector((0, 1, 0)), smooth=smooth)
        # 결속 띠 - 드럼통을 나무 틀에 묶는 노끈(윗면이 가로보 밑면과 만난다).
        for z in (-0.58, 0.58):
            torus(bm_m, (x, cy, z), 0.241, 0.014, seg=8, sides=4, axis="Z")
    metal = obj_from("metal", bm_m, uv="cyl", tile=0.6, axis="Z")
    return [wood, metal]


# ── 3. raft_base_buoy - 부력통 바닥판 ─────────────────────────────────────────
def build_base_buoy(rng):
    """둥근 부표 4개를 격자 틀에 앉히고 노끈으로 눌러 묶은 칸."""
    bm_w = bmesh.new()
    for z in (-0.94, 0.94):                   # 테두리 판(가로)
        box(bm_w, (0.0, 0.31, z), (CELL, 0.14, 0.12))
    for x in (-0.94, 0.94):                   # 테두리 판(세로)
        box(bm_w, (x, 0.31, 0.0), (0.12, 0.14, CELL - 0.24))
    box(bm_w, (0.0, 0.31, 0.0), (CELL - 0.24, 0.12, 0.11))   # 십자 보강
    box(bm_w, (0.0, 0.31, 0.0), (0.11, 0.12, CELL - 0.24))
    wood = obj_from("wood", bm_w, uv="box", tile=0.5)

    bm_m = bmesh.new()
    for sx in (-1, 1):
        for sz in (-1, 1):
            # 지름 0.60 - 첫 렌더에서 0.46 은 나무 틀에 가려 "밑에 뭔가 있다"로만 읽혔다.
            lump(bm_m, rng, (sx * 0.5, 0.232, sz * 0.5), (0.60, 0.44, 0.60),
                 lumps=0.04, subdiv=2, smooth=True)
    # 결속 노끈 - 부표 위를 넘어가며 아치를 그린다. 끝이 +-1.0 이라 옆칸 줄과 이어진다.
    for z in (-0.5, 0.5):
        poly_tube(bm_m, [(-HALF, 0.38, z), (-0.5, 0.435, z), (0.0, 0.39, z),
                         (0.5, 0.435, z), (HALF, 0.38, z)],
                  [0.020] * 5, sides=6, up=Vector((0, 1, 0)))
    for x in (-0.5, 0.5):
        poly_tube(bm_m, [(x, 0.37, -HALF), (x, 0.418, -0.5), (x, 0.375, 0.0),
                         (x, 0.418, 0.5), (x, 0.37, HALF)],
                  [0.018] * 5, sides=6, up=Vector((0, 1, 0)))
    metal = obj_from("metal", bm_m, uv="box", tile=0.35)
    return [wood, metal]


# ── 4. raft_floor - 갑판 바닥재 ───────────────────────────────────────────────
def build_floor(rng):
    """널판 7장(살짝 어긋난 끝 + 틈)과 못. 바닥판 위에 까는 얇은 갑판."""
    n = 7
    pitch = CELL / n
    gap = 0.016
    bm_w = bmesh.new()
    front_j = zero_min_jitter(rng, n, 0.025)
    back_j = zero_min_jitter(rng, n, 0.025)
    plank_x = []
    for i in range(n):
        x0 = -HALF + pitch * i + (gap * 0.5 if i > 0 else 0.0)
        x1 = -HALF + pitch * (i + 1) - (gap * 0.5 if i < n - 1 else 0.0)
        z0, z1 = -HALF + back_j[i], HALF - front_j[i]
        th = 0.062 + rng.uniform(0.0, 0.004)
        box(bm_w, ((x0 + x1) * 0.5, th * 0.5, (z0 + z1) * 0.5),
            (x1 - x0, th, z1 - z0))
        plank_x.append(((x0 + x1) * 0.5, th))
    # 옹이 - 널 위에 아주 얕게 솟은 원반. 플랫 셰이딩이라 거리에서도 명암으로 읽힌다.
    for _ in range(5):
        cx, th = rng.choice(plank_x)
        kx = cx + rng.uniform(-0.05, 0.05)     # 축은 **수직**이어야 한다 - 양 끝에 다른
        kz = rng.uniform(-0.8, 0.8)            # 지터를 주면 원반이 옆으로 서서 높이가 튄다
        r = rng.uniform(0.026, 0.034)
        poly_tube(bm_w, [(kx, th - 0.01, kz), (kx, th + 0.004, kz)],
                  [r, r * 0.8], sides=8, up=Vector((0, 0, 1)), smooth=False)
    wood = obj_from("wood", bm_w, uv="box", tile=0.5)

    # 못 - 널 끝마다 하나씩. 머리가 이 에셋의 최고점(0.08)이다.
    bm_m = bmesh.new()
    for i in range(n):
        cx, th = plank_x[i]
        for sz in (-1, 1):
            cz = sz * (HALF - 0.10 - (front_j[i] if sz > 0 else back_j[i]))
            poly_tube(bm_m, [(cx, th - 0.02, cz), (cx, 0.08, cz)],
                      [0.020, 0.024], sides=5, up=Vector((0, 0, 1)), smooth=False)
    metal = obj_from("metal", bm_m, uv="box", tile=0.2)
    return [wood, metal]


# ── 5. raft_sail - 돛 ─────────────────────────────────────────────────────────
def build_sail(rng):
    """나무 마스트 + 가로대 + 부푼 천 돛.

    그룹 이름이 wood/**cloth** 인 유일한 종이다(천이 주인공이라 metal 로 부르면 어색하다).
    돛은 두께 없는 단면 메시라 make_double_sided 로 뒷면을 굽는다 - 그 뒤에는 clean_bmesh 를
    부르면 안 된다(README 함정 7).
    """
    mast_h, half_w = 3.2, 0.9
    yard_y, boom_y = 2.92, 0.78

    bm_w = bmesh.new()
    box(bm_w, (0.0, 0.07, 0.0), (0.36, 0.14, 0.36))                  # 마스트 발판
    poly_tube(bm_w, [(0, 0.10, 0), (0, 1.20, 0.01), (0, 2.30, 0.015), (0, mast_h, 0.0)],
              [0.078, 0.070, 0.060, 0.050], sides=8, up=Vector((0, 0, 1)), smooth=True)
    for y, hw, r in ((yard_y, half_w, 0.045), (boom_y, half_w - 0.10, 0.040)):
        poly_tube(bm_w, [(-hw, y, 0.0), (0.0, y - 0.012, 0.02), (hw, y, 0.0)],
                  [r, r, r], sides=6, up=Vector((0, 1, 0)), smooth=True)
    wood = obj_from("wood", bm_w, uv="box", tile=0.5)

    # 천 - 가로 8칸 x 세로 9칸 격자. +Z 쪽으로만 부푼다(바람을 받는 면).
    nu, nv = 8, 9
    bm_c = bmesh.new()
    grid = []
    for j in range(nv + 1):
        v = j / nv
        row = []
        for i in range(nu + 1):
            u = i / nu
            hw = 0.85 + (0.74 - 0.85) * v
            x = (u * 2.0 - 1.0) * hw
            sag = -0.045 * math.sin(math.pi * u) * (v ** 2)          # 아래 자락이 처진다
            y = yard_y + (boom_y - yard_y) * v + sag
            belly = math.sin(math.pi * u) * math.sin(math.pi * min(1.0, 0.13 + v * 0.80))
            z = 0.30 * belly + 0.018 * math.sin(u * math.pi * 3.0) * math.sin(v * math.pi * 2.0)
            row.append(bm_c.verts.new((x, y, z)))
        grid.append(row)
    for j in range(nv):
        for i in range(nu):
            f = bm_c.faces.new((grid[j][i], grid[j][i + 1], grid[j + 1][i + 1], grid[j + 1][i]))
            f.smooth = True
    bmesh.ops.recalc_face_normals(bm_c, faces=bm_c.faces[:])
    mg.make_double_sided(bm_c)
    cloth = obj_from("cloth", bm_c, uv="planar", tile=(1.8, 2.4), axis="Z")
    return [wood, cloth]


# ── 6. raft_rudder - 키 ───────────────────────────────────────────────────────
def build_rudder(rng):
    """나무 틸러(손잡이) + 물속 날. 쇠테/축핀이 metal 그룹이다."""
    bm_w = bmesh.new()
    # 날 - YZ 평면 판재를 X 로 두껍게. 아래가 물속.
    blade = [(0.0, 0.72, -0.02), (0.0, 0.66, 0.42), (0.0, 0.18, 0.44),
             (0.0, 0.0, 0.28), (0.0, 0.02, 0.0)]
    extrude_poly(bm_w, [Vector(p) for p in blade], "X", 0.045)
    poly_tube(bm_w, [(0, 0.52, 0.14), (0, 0.90, 0.10), (0, 1.24, 0.06)],
              [0.058, 0.054, 0.050], sides=8, up=Vector((0, 0, 1)), smooth=True)   # 축(스톡)
    poly_tube(bm_w, [(0, 1.20, 0.03), (0, 1.32, -0.20), (0, 1.40, -0.45)],
              [0.046, 0.040, 0.034], sides=6, up=Vector((1, 0, 0)), smooth=True)   # 틸러
    box(bm_w, (0.0, 1.02, 0.17), (0.50, 0.085, 0.09))                              # 거치대 가로목
    wood = obj_from("wood", bm_w, uv="box", tile=0.45)

    bm_m = bmesh.new()
    for y in (0.62, 1.05):                                     # 쇠테 2개
        torus(bm_m, (0, y, 0.13 - (y - 0.62) * 0.16), 0.068, 0.017, seg=8, sides=4, axis="Y")
    poly_tube(bm_m, [(-0.24, 1.02, 0.17), (0.24, 1.02, 0.17)], [0.024, 0.024],
              sides=6, up=Vector((0, 1, 0)), smooth=True)       # 축핀
    box(bm_m, (0.0, 0.40, 0.24), (0.11, 0.055, 0.30))           # 날 보강 쇠판
    box(bm_m, (0.0, 0.62, 0.20), (0.10, 0.05, 0.22))
    metal = obj_from("metal", bm_m, uv="box", tile=0.3)
    return [wood, metal]


# ── 7. raft_anchor - 닻 ───────────────────────────────────────────────────────
def build_anchor(rng):
    """돌덩이를 노끈 그물로 묶은 원시적 닻 + 밧줄 고리.

    나무 발톱(십자 막대) 2개가 wood 그룹이다 - 돌/노끈만 두면 그룹 하나가 비어 버린다.
    실제로도 돌닻은 나무 가지를 십자로 물려 바닥을 물게 만든다.
    """
    bm_w = bmesh.new()
    poly_tube(bm_w, [(-0.30, 0.045, 0.0), (0.0, 0.055, 0.0), (0.30, 0.045, 0.0)],
              [0.036, 0.040, 0.036], sides=8, up=Vector((0, 1, 0)), smooth=True)
    poly_tube(bm_w, [(0.0, 0.045, -0.30), (0.0, 0.052, 0.0), (0.0, 0.045, 0.30)],
              [0.034, 0.038, 0.034], sides=8, up=Vector((0, 1, 0)), smooth=True)
    wood = obj_from("wood", bm_w, uv="box", tile=0.35)

    bm_m = bmesh.new()
    lump(bm_m, rng, (0.0, 0.33, 0.0), (0.44, 0.48, 0.44), lumps=0.15, subdiv=2, smooth=False)
    # 그물 - 세로 노끈 4가닥이 돌을 감싸고 위에서 모인다.
    for k in range(4):
        a = math.tau * k / 4 + math.pi * 0.25
        dx, dz = math.cos(a), math.sin(a)
        pts = [(dx * 0.20, 0.07, dz * 0.20), (dx * 0.24, 0.22, dz * 0.24),
               (dx * 0.23, 0.40, dz * 0.23), (dx * 0.13, 0.55, dz * 0.13),
               (0.0, 0.62, 0.0)]
        poly_tube(bm_m, pts, [0.018] * 5, sides=5, up=Vector((0, 1, 0)), smooth=True)
    torus(bm_m, (0.0, 0.30, 0.0), 0.235, 0.017, seg=10, sides=4, axis="Y")   # 가로 그물띠
    poly_tube(bm_m, [(0.0, 0.58, 0.0), (0.0, 0.66, 0.0)], [0.026, 0.024],
              sides=6, up=Vector((0, 0, 1)), smooth=True)                    # 밧줄 목
    torus(bm_m, (0.0, 0.72, 0.0), 0.062, 0.019, seg=10, sides=4, axis="Z")   # 밧줄 고리
    metal = obj_from("metal", bm_m, uv="box", tile=0.3)
    return [wood, metal]


# ── 8. raft_motor - 선외기 모터 ───────────────────────────────────────────────
def build_motor(rng):
    """여객기 잔해에서 뜯은 금속 엔진 블록 + 샤프트 + 3날 프로펠러.

    첫 렌더에서 (a) 프로펠러가 기어케이스 뒤에 묻혀 안 보이고 (b) 나무 거치대가 통짜 상자라
    앞을 가렸다. 프로펠러를 뒤로 빼서 지름 0.32로 키우고, 거치대는 **트랜섬 물림쇠**(볼 2개 +
    조임대)로 얇게 바꾼 뒤 남는 자리에 **나무 조종 손잡이(틸러)** 를 달았다 - 선외기 실루엣의
    핵심이 이 손잡이라 이게 있어야 한눈에 "모터"로 읽힌다.
    """
    bm_w = bmesh.new()
    for x in (-0.19, 0.19):                                       # 트랜섬 물림 볼
        box(bm_w, (x, 0.88, 0.25), (0.07, 0.24, 0.15))
    box(bm_w, (0.0, 0.775, 0.29), (0.46, 0.075, 0.09))            # 조임대(가로)
    box(bm_w, (0.0, 0.92, 0.325), (0.40, 0.15, 0.05))             # 트랜섬에 닿는 받침판
    poly_tube(bm_w, [(0, 0.80, 0.16), (0, 0.775, 0.30), (0, 0.745, 0.44)],
              [0.040, 0.037, 0.033], sides=6, up=Vector((0, 1, 0)), smooth=True)  # 나무 틸러
    wood = obj_from("wood", bm_w, uv="box", tile=0.4)

    bm_m = bmesh.new()
    box(bm_m, (0.0, 0.86, 0.0), (0.36, 0.26, 0.30))               # 엔진 블록
    poly_tube(bm_m, [(0, 0.96, 0.0), (0, 1.05, 0.0), (0, 1.10, 0.0)],
              [(0.185, 0.155), (0.175, 0.148), (0.135, 0.115)],
              sides=8, up=Vector((0, 0, 1)), smooth=False)         # 카울(엔진 덮개) - 둥근 윗판
    poly_tube(bm_m, [(-0.19, 0.93, -0.02), (-0.25, 0.93, -0.02)], [0.06, 0.055],
              sides=6, up=Vector((0, 1, 0)), smooth=True)          # 노출된 실린더 헤드
    box(bm_m, (0.205, 0.94, -0.10), (0.03, 0.19, 0.18), rot_y=math.radians(13))   # 찢긴 패널
    poly_tube(bm_m, [(0, 0.22, -0.02), (0, 0.48, -0.02), (0, 0.76, -0.01)],
              [0.075, 0.080, 0.088], sides=8, up=Vector((0, 0, 1)), smooth=True)  # 샤프트 하우징
    box(bm_m, (0.0, 0.305, -0.09), (0.30, 0.025, 0.26))            # 캐비테이션 판
    z_prof = [(-0.24, 0.035), (-0.18, 0.072), (-0.02, 0.082), (0.10, 0.062), (0.15, 0.028)]
    poly_tube(bm_m, [(0, 0.17, z) for z, _ in z_prof], [r for _, r in z_prof],
              sides=8, up=Vector((0, 1, 0)), smooth=True)          # 기어 케이스(어뢰)
    extrude_poly(bm_m, [Vector(p) for p in                          # 스케그(방향 지느러미)
                        ((0, 0.155, 0.10), (0, 0.155, -0.06), (0, 0.02, -0.02),
                         (0, 0.02, 0.06))], "X", 0.016)
    poly_tube(bm_m, [(0, 0.17, -0.35), (0, 0.17, -0.20)], [0.045, 0.052],
              sides=8, up=Vector((0, 1, 0)), smooth=True)          # 프로펠러 허브 - 기어케이스에
    #                                          **겹치게** 뻗는다(떼어 놓으면 프로펠러가 떠 보인다)
    for k in range(3):                                             # 3날 - 링마다 단면을 돌려 비튼다
        ang = math.radians(270.0) + math.tau * k / 3.0
        dx, dy = math.cos(ang), math.sin(ang)
        # 날은 **길고 얇게**. 첫 렌더에서 현(0.14)이 길이(0.16)와 비슷해 3개가 공처럼 보였다.
        pts = [(dx * 0.045, 0.17 + dy * 0.045, -0.300),
               (dx * 0.100, 0.17 + dy * 0.100, -0.308),
               (dx * 0.150, 0.17 + dy * 0.150, -0.316),
               (dx * 0.185, 0.17 + dy * 0.185, -0.322)]
        radii = [(0.030, 0.011), (0.042, 0.009), (0.038, 0.008), (0.016, 0.006)]
        poly_tube(bm_m, pts, radii, sides=6, up=Vector((0, 0, 1)),
                  phase=[0.0, 0.18, 0.38, 0.52], smooth=True)
    metal = obj_from("metal", bm_m, uv="box", tile=0.3)
    return [wood, metal]


# ── 파이프라인 ────────────────────────────────────────────────────────────────
def fit_group(objs, size, axes="xyz"):
    """조립체 전체를 원점 기준으로 축별 스케일해 바운딩 박스를 정확한 치수로 맞춘다.

    mgbuild.fit_size 의 그룹판이다(단일 오브젝트용이라 조립체에 쓰면 파츠끼리 어긋난다).
    모든 파츠에 **같은 행렬**을 먹이므로 조립 관계(상대 위치)는 그대로다.

    axes:
      "xyz" 돛/키/닻/모터 - 실루엣이 자유로워 표의 치수를 그냥 맞춘다(보정 2% 이내).
      "y"   **격자 타일(바닥판/바닥재)** - X/Z 는 손으로 정확히 짠 격자 피치라 절대 건드리지
            않는다. 높이만 표에 맞춘다(밧줄 단면·부표 지터 때문에 mm 단위로 남는 오차 정리).
    """
    lo, hi = mg.union_bbox(objs)
    ext = hi - lo
    want = Vector((size[0] / max(ext.x, mg.EPS), size[1] / max(ext.y, mg.EPS),
                   size[2] / max(ext.z, mg.EPS)))
    s = Vector((want.x if "x" in axes else 1.0,
                want.y if "y" in axes else 1.0,
                want.z if "z" in axes else 1.0))
    for o in objs:
        o.data.transform(Matrix.Diagonal(s.to_4d()))
    return tuple(ext), tuple(s)


# (이름, 시드, 빌더, 기대 크기, align, fit 축, 예산, 메모)
SPECS = [
    ("raft_base_wood", 78001, build_base_wood, (2.0, 0.28, 2.0), "bbox", "y", 800,
     "logs 5 / pitch 0.40"),
    ("raft_base_barrel", 78002, build_base_barrel, (2.0, 0.55, 2.0), "bbox", "y", 800,
     "drums 2 / frame rails"),
    ("raft_base_buoy", 78003, build_base_buoy, (2.0, 0.45, 2.0), "bbox", "y", 800,
     "buoys 4 / lashed"),
    ("raft_floor", 78004, build_floor, (2.0, 0.08, 2.0), "bbox", "y", 800,
     "planks 7 / nails"),
    ("raft_sail", 78005, build_sail, (1.8, 3.2, None), "ground", False, 1500,
     "mast+yard / belly +Z"),
    ("raft_rudder", 78006, build_rudder, (0.5, 1.4, 0.9), "ground", "xyz", 1500,
     "tiller + blade"),
    ("raft_anchor", 78007, build_anchor, (0.6, 0.8, 0.6), "ground", "xyz", 1500,
     "stone + net + loop"),
    ("raft_motor", 78008, build_motor, (0.5, 1.1, 0.8), "ground", "xyz", 1500,
     "block + prop 3"),
]


def build_asset(name, seed, builder, expect, align, fit, budget):
    """씬을 비우고 한 종을 만든 뒤 계약을 강제한다(내보내기 전까지)."""
    mg.reset_scene()
    rng = mg.Rng(seed)
    objs = builder(rng)
    for o in objs:
        mg.triangulate(o)
        mg.assign_material(o, mg.preview_material("pv_" + o.name,
                                                  base_color=PREVIEW_COLORS[o.name]))
    raw = tuple(mg.union_bbox(objs)[1] - mg.union_bbox(objs)[0])
    scale = (1.0, 1.0, 1.0)
    if fit:
        _, scale = fit_group(objs, expect, axes=fit)
    # 돛만 깊이(돛의 부푼 양)를 표에서 정하지 않았다 - 폭/높이는 그대로 엄격히 검사한다.
    lo, hi = mg.union_bbox(objs)
    exp = expect if expect[2] is not None else (expect[0], expect[1], hi.z - lo.z)
    stats = mg.enforce_contract_group(objs, tri_budget=budget, expect_size=exp,
                                      tri_floor=120, name=name, align=align)
    stats["raw_size"] = raw
    stats["fit_scale"] = scale
    return objs, stats


def grid_preview(name, seed, builder, align, fit, expect, budget):
    """바닥판을 3x3(6m x 6m)으로 이어 붙인 가상 배치 - 이음새를 눈으로 본다."""
    objs, _ = build_asset(name, seed, builder, expect, align, fit, budget)
    lo, hi = mg.union_bbox(objs)
    off = Vector((-(lo.x + hi.x) * 0.5, -lo.y, -(lo.z + hi.z) * 0.5))
    for o in objs:
        o.data.transform(Matrix.Translation(off))
    tiles = []
    for gx in (-1, 0, 1):
        for gz in (-1, 0, 1):
            for o in objs:
                me = o.data.copy()
                me.transform(Matrix.Translation(Vector((gx * CELL, 0.0, gz * CELL))))
                dup = bpy.data.objects.new(f"{o.name}_{gx+1}{gz+1}", me)
                bpy.context.collection.objects.link(dup)
                tiles.append(dup)
    for o in objs:
        bpy.data.objects.remove(o, do_unlink=True)
    png = os.path.join(PREVIEW_DIR, f"grid_{name}.png")
    mg.turntable(tiles, png, title=f"{name}  3x3 grid (6m)", px=460, samples=14,
                 notes="seam check / cell 2.0m")
    return png


def edge_profile(path, axis, value, tol=1e-4):
    """OBJ 에서 경계면(x=value 또는 z=value)에 닿은 정점의 (반대축, 높이) 목록."""
    idx = {"x": 0, "z": 2}[axis]
    other = 2 if idx == 0 else 0
    hits = []
    with open(path) as fh:
        for line in fh:
            if line.startswith("v "):
                p = [float(t) for t in line.split()[1:4]]
                if abs(p[idx] - value) <= tol:
                    hits.append((round(p[other], 4), round(p[1], 4)))
    return sorted(set(hits))


def seam_report(names):
    """격자 이음새 실측.

    타일이 성립하려면 두 가지면 된다.
      (1) XZ 크기가 정확히 2.0 x 2.0 이고 바운딩 박스 중심이 원점 -> 옆칸과 **틈도 겹침도 0**.
      (2) 네 경계면에 실제로 정점이 닿아 있고(맞물릴 살이 있고) 마주 보는 두 면의 높이대가
          비슷하다 -> 이어 붙였을 때 단차가 튀지 않는다.
    아래는 그 둘을 파일에서 다시 읽어 잰 값이다.
    """
    print("SEAM_REPORT (cell 2.0m / 3x3 가상 배치 기준)")
    for name in names:
        path = os.path.join(mg.MODELS_DIR, name + ".obj")
        xs, ys, zs = [], [], []
        with open(path) as fh:
            for line in fh:
                if line.startswith("v "):
                    x, y, z = [float(t) for t in line.split()[1:4]]
                    xs.append(x); ys.append(y); zs.append(z)
        w, d = max(xs) - min(xs), max(zs) - min(zs)
        cx, cz = (min(xs) + max(xs)) * 0.5, (min(zs) + max(zs)) * 0.5
        edges = {
            "x-": edge_profile(path, "x", min(xs)), "x+": edge_profile(path, "x", max(xs)),
            "z-": edge_profile(path, "z", min(zs)), "z+": edge_profile(path, "z", max(zs)),
        }
        print(f"  {name:<18} XZ {w:.4f} x {d:.4f} m  (오차 {abs(w - CELL) * 1000:.2f}mm /"
              f" {abs(d - CELL) * 1000:.2f}mm)   bbox 중심 ({cx:+.4f}, {cz:+.4f})")
        line = []
        for k in ("x-", "x+", "z-", "z+"):
            e = edges[k]
            line.append(f"{k} {len(e):>2}개 y {min(v for _, v in e):.3f}~{max(v for _, v in e):.3f}")
        print("      경계 접촉: " + " | ".join(line))
        dx = abs(max(v for _, v in edges["x-"]) - max(v for _, v in edges["x+"]))
        dz = abs(max(v for _, v in edges["z-"]) - max(v for _, v in edges["z+"]))
        print(f"      마주 보는 면 최고점 단차: x {dx * 1000:.1f}mm / z {dz * 1000:.1f}mm")


def main():
    os.makedirs(PREVIEW_DIR, exist_ok=True)
    manifest = []
    for name, seed, builder, expect, align, fit, budget, note in SPECS:
        objs, stats = build_asset(name, seed, builder, expect, align, fit, budget)
        out = os.path.join(mg.MODELS_DIR, name + ".obj")
        mg.export_obj(objs, out)
        stats = mg.verify_obj_file(out, stats)
        mg.inject_usemtl(out)
        mg.turntable(objs, os.path.join(PREVIEW_DIR, name + ".png"), title=name,
                     stats=stats, px=430, samples=16, notes=f"seed {seed} / {note}")
        mg.report(stats)
        manifest.append((name, seed, stats))

    for name, seed, builder, expect, align, fit, budget, _ in SPECS[:4]:
        grid_preview(name, seed, builder, align, fit, expect, budget)

    seam_report([s[0] for s in SPECS[:4]])

    print("RAFT_MANIFEST")
    for name, seed, st in manifest:
        s = st["size"]
        parts = " + ".join(f"{n}:{t}" for n, t in st["parts"])
        print(f"  {name:<18} seed {seed}  tris {st['tris']:>4}/{st['budget']}  "
              f"{s[0]:.2f} x {s[1]:.2f} x {s[2]:.2f} m   [{parts}]")
    print(f"[raftparts] 완료 - {len(manifest)}종")


if __name__ == "__main__":
    main()
