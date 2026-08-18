#!/usr/bin/env python3
"""
rockform_a ~ rockform_l - 육상 바위 **형태 축** 12종 (2026-08-18).

    python3 Tools/blender/units/rockform.py

산출물 (전부 신규 파일 - 기존 rock_* / searock_* 는 한 바이트도 건드리지 않는다)
  Assets/_Project/Resources/Models/rockform_a.obj (+.mtl)  자연 아치
  …                              rockform_l.obj (+.mtl)   낮은 노두 판
  Tools/blender/_preview/_rockform/rockform_*.png          턴테이블 - 저장소에 넣지 않는다

  ★ 렌더를 _preview 바로 밑이 아니라 **전용 하위 폴더**에 쓴다. mgbuild.turntable 은 타일을
    `os.path.dirname(out_png)/_tiles/tile0..3.png` 에 쓰고 합성 후 지우는데, 같은 시각에 다른
    unit 스크립트가 돌면 **두 프로세스가 같은 타일 파일을 덮어쓴다**. 실제로 1차 렌더에서
    rockform_a 의 정면/측면/후면 컷에 다른 에셋(해저 첨탑)이 찍혀 나왔다 - 3/4 컷만 제 것이었다.
    출력 폴더를 나누면 타일 폴더도 갈라져 충돌이 사라진다.

왜 12종인가 (사용자: "바위 다양성이 부족하다")
  기존 육상 바위는 rock_a~e(표석) / rock_cliff_a·b(절벽) / rock_mega_a·b(거대) /
  rock_rubble_a·b(자갈) / rock_stack_a(층상) 11종인데, **실루엣 축은 사실상 셋**이다
  (둥근 덩어리 / 납작한 판 / 벽). 시드를 바꿔도 멀리서 보면 같은 물건이다.
  이 12종은 전부 **한눈에 갈리는 형태 축**을 하나씩 연다. 형태가 겹치면 존재 이유가 없다:

    a 자연 아치      뚫려 있다 (실측 개구: 바닥폭 2.50m / 높이 2.0m 지점 폭 2.06m /
                     천장 3.48m / Z 관통 확인 - 플레이어 2m 가 그대로 지나간다)
    b 첨탑/오벨리스크 가늘고 높고 기울었다 (H 7.0m / 밑동 지름 1.5m / 꼭대기가 1.4m 밀림)
    c 주상절리 다발  육각 기둥 7개가 높이차를 두고 선다
    d 버섯 바위      아랫부분이 잘록하고 갓이 덮는다 (갓 3.5m / 허리 0.9m)
    e 층리 슬랩 스택 수평 판 5장이 어긋나게 쌓인다
    f 균열 거석      큰 덩어리에 위로 벌어지는 쐐기 균열 2줄
    g 표석 군집      크고 작은 둥근 바위 6개 (폭 6m, 한 메시)
    h 기울어진 판석  큰 평판이 68도로 비스듬히 박혀 있다
    i 벌집 풍화암    표면에 얕은 구멍 22개 (타포니)
    j 계단식 노두    자연 계단 4단 (실측 최대 단높이 0.52m - 올라갈 수 있다)
    k 쐐기 바위      바위 하나가 두 바위 사이에 끼었다 (실측 개구: 바닥폭 1.70m /
                     천장 1.92m / Z 관통 확인)
    l 낮은 노두 판   지면에서 살짝 솟은 넓고 낮은 암반 (7.0 x 1.2m - 바위섬 지면 피복)

계약 (Docs/AssetPipeline.md / mgbuild.py)
  미터 / +Y up / +Z front / 밑면 y=0 / 원점 = 접지 중심 / OBJ + vn + vt / 머티리얼 없음.
  export -> verify_obj_file -> **mg.inject_usemtl** 순서를 지킨다(로컬 inject 금지 - R2 승격분).
  `o` 오브젝트 1개(`rockform_x_rock`) = 런타임 머티리얼 1장(WeatheredStone 계열).
  시드 73001~73012 을 아래 표에 박아 둔다. 같은 시드 = 같은 md5 (2회 실행 대조함).

삼각형 예산
  large_structure(8000): a / f / g / j / l - 5m 급이라 면이 성기면 각이 보인다.
  small_prop(1500)     : 나머지 7종.

형태 만드는 법 (프리미티브와 갈리는 이유)
  - 아치·첨탑·버섯·주상절리는 **링 스윕**이다(_ring_tube). 구를 깎아서는 구멍이 안 뚫린다.
    아치는 반타원 경로를 따라 단면을 쓸어 **실제로 뚫린 고리**를 만든다 - 개구를 아래에서
    레이 패리티로 실측해 보고한다(measure_opening).
  - 균열 거석은 같은 덩어리를 **세 번 만들어** 서로 다른 평면으로 자른 뒤, 균열 법선 방향으로
    높이에 비례한 변위를 준다. 밑동은 붙어 있고 위로 갈수록 벌어지는 **쐐기 균열**이 된다.
    조각을 통째로 평행이동하면 밑동까지 벌어져 "바위 세 개"가 되어 버린다(1차 시도의 실패).
  - 벌집 풍화는 방향 기준 오목 변위 22개다. 불리언을 쓰면 비결정적이고 예산이 폭발한다.
"""

import math
import os
import sys

_UNITS_DIR = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.dirname(_UNITS_DIR))
sys.path.insert(0, _UNITS_DIR)

import mgbuild as mg  # noqa: E402  (bpy 를 먼저 끌어온다 - bmesh 가 그 뒤에야 import 된다)
import rock  # noqa: E402  (형태 헬퍼 _surface_radius/_slab_cut/_cut 과 색·UV 상수만 쓴다)
import bmesh  # noqa: E402
from mathutils import Matrix, Vector  # noqa: E402
from mathutils.bvhtree import BVHTree  # noqa: E402

UV_TILE = rock.UV_TILE          # 1.15m - 기존 바위와 같은 rock.png 박스 UV
STONE = rock.STONE              # WeatheredStone - 기존 바위와 같은 색 계열

# 렌더 전용 폴더 - mgbuild.turntable 의 _tiles 가 다른 unit 스크립트와 겹치지 않게 한다.
PREVIEW_SUB = os.path.join(mg.PREVIEW_DIR, "_rockform")

BIG = mg.TRI_BUDGET["large_structure"]   # 8000
SMALL = mg.TRI_BUDGET["small_prop"]      # 1500

# 이름, 시드, (W, H, D) 미터, 예산, 정렬, 한 줄 설명
SPECS = [
    ("rockform_a", 73001, (6.20, 4.30, 2.10), BIG,   "ground", "natural arch"),
    ("rockform_b", 73002, (2.20, 7.00, 1.85), SMALL, "ground", "leaning spire"),
    ("rockform_c", 73003, (4.00, 5.00, 3.40), SMALL, "ground", "columnar basalt x7"),
    ("rockform_d", 73004, (3.50, 3.10, 3.30), SMALL, "ground", "mushroom rock"),
    ("rockform_e", 73005, (3.90, 3.00, 3.15), SMALL, "ground", "bedded slab stack x5"),
    ("rockform_f", 73006, (3.20, 4.50, 2.90), BIG,   "ground", "fractured megalith"),
    ("rockform_g", 73007, (6.00, 2.10, 4.40), BIG,   "ground", "boulder cluster x6"),
    ("rockform_h", 73008, (2.60, 3.50, 2.30), SMALL, "ground", "tilted flagstone"),
    ("rockform_i", 73009, (2.50, 2.10, 2.45), SMALL, "ground", "honeycomb weathered"),
    ("rockform_j", 73010, (5.00, 2.00, 4.20), BIG,   "ground", "stepped outcrop x4"),
    ("rockform_k", 73011, (4.00, 2.90, 1.90), SMALL, "ground", "wedged rock"),
    ("rockform_l", 73012, (7.00, 1.20, 5.50), BIG,   "ground", "low pavement"),
]

# 개구가 있는 종만 실측한다(a 아치 / k 쐐기). z = 0 단면에서 레이 패리티로 잰다.
OPENING = {"rockform_a", "rockform_k"}


# ────────────────────────────────────────────────────────────────────────────
# 공통 형태 헬퍼
# ────────────────────────────────────────────────────────────────────────────
def _wob(x, y, z, salt, amp):
    """결정적 저진폭 요철. 난수 표집이 아니라 **좌표의 함수**라 이웃 면에 틈이 안 생긴다."""
    return amp * (math.sin(x * 3.7 + salt) * 0.55
                  + math.sin(y * 4.9 + salt * 1.7) * 0.30
                  + math.sin(z * 6.1 + salt * 2.3) * 0.35
                  + math.sin((x + z) * 8.3 + salt * 3.1) * 0.20)


def _ring_tube(bm, rings, cap_start=True, cap_end=True):
    """링(같은 개수의 점 리스트) 목록을 튜브로 잇는다. 캡을 붙여 **닫힌 껍질**로 만든다.

    mgbuild.swept_tube 는 단면이 항상 **수평 XZ 원**이라 아치처럼 방향이 도는 스윕에는
    못 쓴다(크라운에서 단면이 눕는다). 여기서는 링 좌표를 호출부가 직접 준다.
    감김은 신경 쓰지 않는다 - clean_bmesh 의 recalc_face_normals 가 한 번에 바깥으로 돌린다.
    """
    loops = []
    for ring in rings:
        loops.append([bm.verts.new((p[0], p[1], p[2])) for p in ring])
    for lo, hi in zip(loops, loops[1:]):
        n = len(lo)
        for i in range(n):
            j = (i + 1) % n
            bm.faces.new((lo[i], lo[j], hi[j], hi[i]))
    if cap_start:
        bm.faces.new(loops[0])
    if cap_end:
        bm.faces.new(list(reversed(loops[-1])))
    return loops


def _poly_xz(rng, sides, rx, rz, rough=0.15, phase=0.0):
    """불규칙 n각형(XZ 평면). 판석·계단·노두의 발자국을 만든다."""
    pts = []
    for i in range(sides):
        a = math.tau * (i + rng.uniform(-0.22, 0.22)) / sides + phase
        rr = 1.0 + rng.uniform(-rough, rough)
        pts.append((math.cos(a) * rx * rr, math.sin(a) * rz * rr))
    return pts


def _prism(bm, poly, y_bot, y_top, rng, taper=0.94, cx=0.0, cz=0.0,
           tilt=(0.0, 0.0), salt=0.0, mid_bulge=0.06):
    """XZ 다각형을 y_bot~y_top 으로 세운 각기둥. 옆면 중간에 요철 링을 하나 넣어
    프리미티브 실린더로 안 읽히게 한다. tilt=(dy/dx, dy/dz) 로 윗면을 기울인다."""
    rings = []
    for k, t in enumerate((0.0, 0.5, 1.0)):
        ring = []
        s = 1.0 + (mid_bulge if k == 1 else 0.0)
        for (px, pz) in poly:
            f = (1.0 - t) + taper * t
            x = cx + px * f * s
            z = cz + pz * f * s
            y = y_bot + (y_top - y_bot) * t
            if t > 0.99:
                y += tilt[0] * (x - cx) + tilt[1] * (z - cz)
            y += _wob(x, y, z, salt, (y_top - y_bot) * 0.04)
            x += _wob(z, y * 1.7, x, salt + 3.0, (abs(px) + abs(pz)) * 0.035)
            z += _wob(x, y * 2.1, z, salt + 7.0, (abs(px) + abs(pz)) * 0.035)
            ring.append((x, y, z))
        rings.append(ring)
    return _ring_tube(bm, rings)


def _lump(seed, rng, subdiv=4, lobe_n=4, amp=(0.18, 0.40), y_damp=0.45):
    """rock.py 의 로브 어휘를 쓴 둥근 덩어리(반지름 ~1). 빌더는 호출하지 않는다."""
    bm = bmesh.new()
    bmesh.ops.create_icosphere(bm, subdivisions=subdiv, radius=1.0)
    lobes = []
    for _ in range(lobe_n):
        axis = rng.unit_vector()
        axis.y *= y_damp
        axis.normalize()
        lobes.append((axis, rng.uniform(*amp), rng.uniform(1.1, 2.1)))
    for v in bm.verts:
        d = v.co.normalized()
        v.co = d * rock._surface_radius(d, lobes, seed)
    return bm


def _flatten_bottom(bm, frac=0.12):
    ys = [v.co.y for v in bm.verts]
    lo, hi = min(ys), max(ys)
    rock._cut(bm, Vector((0.0, lo + (hi - lo) * frac, 0.0)), Vector((0.0, -1.0, 0.0)))


def _place(bm, scale, yaw, tilt_axis, tilt, px, pz, base_y):
    """덩어리를 스케일 -> 회전 -> (밑면을 base_y 에 맞춰) 이동. rockforms._place_shard 와 같은 규약."""
    m = (Matrix.Rotation(yaw, 4, "Y") @
         Matrix.Rotation(tilt, 4, tilt_axis) @
         Matrix.Diagonal(Vector((scale[0], scale[1], scale[2], 1.0))))
    bmesh.ops.transform(bm, matrix=m, verts=bm.verts[:])
    ymin = min(v.co.y for v in bm.verts)
    bmesh.ops.transform(bm, matrix=Matrix.Translation(Vector((px, base_y - ymin, pz))),
                        verts=bm.verts[:])
    return bm


def _fit_bm(bm, size, cx=0.0, cz=0.0, base_y=0.0):
    """bmesh 를 **정확한 미터 치수**로 맞추고 (cx, base_y, cz) 에 앉힌다.

    _place(스케일 배수)는 로브 때문에 실제 반지름이 1.0~1.4 로 흔들려서 조각 사이 간격을
    설계값대로 못 잡는다(쐐기 바위 1차 시도에서 기둥 간격이 1.95m 로 벌어져 쐐기가
    오른쪽 기둥에 닿지 않았고, 개구가 위로 새어 실측이 실패했다). 여기서는 bbox 로 잰다.
    """
    co = [v.co for v in bm.verts]
    lo = Vector((min(c.x for c in co), min(c.y for c in co), min(c.z for c in co)))
    hi = Vector((max(c.x for c in co), max(c.y for c in co), max(c.z for c in co)))
    ext = hi - lo
    s = Vector((size[0] / max(ext.x, 1e-6), size[1] / max(ext.y, 1e-6),
                size[2] / max(ext.z, 1e-6)))
    bmesh.ops.transform(bm, matrix=Matrix.Diagonal(s.to_4d()), verts=bm.verts[:])
    co = [v.co for v in bm.verts]
    lo = Vector((min(c.x for c in co), min(c.y for c in co), min(c.z for c in co)))
    hi = Vector((max(c.x for c in co), max(c.y for c in co), max(c.z for c in co)))
    bmesh.ops.transform(bm, matrix=Matrix.Translation(Vector((
        cx - (lo.x + hi.x) * 0.5, base_y - lo.y, cz - (lo.z + hi.z) * 0.5))),
        verts=bm.verts[:])
    return bm


def _merge(bms, name):
    """bmesh 여러 개를 오브젝트 하나로. join_objects 가 UV/스무스를 보존하지만
    여기서는 UV 를 나중에 한 번에 펴므로 단순 누적으로 충분하다."""
    objs = [mg.new_object(f"{name}_p{i}", b) for i, b in enumerate(bms)]
    return mg.join_objects(objs, name)


# ────────────────────────────────────────────────────────────────────────────
# a - 자연 아치. 반타원 경로를 따라 단면을 쓸어 **실제로 뚫린 고리**를 만든다.
# ────────────────────────────────────────────────────────────────────────────
def build_arch(seed):
    rng = mg.Rng(seed)
    bm = bmesh.new()
    sides, n = 14, 54
    xr, yr = 2.60, 4.00
    salt = seed * 0.31
    # 다리를 y<0 까지 내렸다가 y=0 에서 잘라 **평평한 발**을 만든다.
    s0, s1 = -0.085, 1.085
    # 다리마다 두께를 다르게 준다(좌우 대칭이면 콘크리트 육교로 읽힌다).
    leg_bias = rng.uniform(0.10, 0.18)
    side_r = [1.0 + rng.uniform(-0.13, 0.13) for _ in range(sides)]

    rings = []
    for i in range(n + 1):
        s = s0 + (s1 - s0) * i / n
        ang = math.pi * s
        c = Vector((-xr * math.cos(ang), yr * math.sin(ang), 0.0))
        t = Vector((xr * math.pi * math.sin(ang), yr * math.pi * math.cos(ang), 0.0))
        t.normalize()
        nrm = Vector((-t.y, t.x, 0.0))          # 아치 평면 안쪽 법선
        bino = Vector((0.0, 0.0, 1.0))          # 깊이 방향
        u = min(1.0, abs(s - 0.5) * 2.0)        # 0 = 크라운, 1 = 발
        rn = 0.48 + 0.62 * u ** 1.55            # 아치 띠의 두께(개구 폭·높이를 정한다)
        rb = 0.74 + 0.46 * u ** 1.35            # 깊이 반두께
        rn *= 1.0 + leg_bias * (1.0 if s > 0.5 else -1.0) * u
        ring = []
        for k in range(sides):
            a = math.tau * k / sides
            j = side_r[k] * (1.0 + 0.09 * math.sin(s * 11.0 + k * 2.3 + salt))
            p = (c + nrm * (math.cos(a) * rn * j) + bino * (math.sin(a) * rb * j))
            p.x += _wob(p.x, p.y, p.z, salt, 0.075)
            p.y += _wob(p.z, p.x, p.y, salt + 2.0, 0.065)
            p.z += _wob(p.y, p.z, p.x, salt + 5.0, 0.070)
            ring.append((p.x, p.y, p.z))
        rings.append(ring)
    _ring_tube(bm, rings)

    # 발 밑동을 넓힌다(땅에 박힌 자세) - y<0.9 구간을 바깥/앞뒤로 부풀린다.
    for v in bm.verts:
        if v.co.y < 0.9:
            f = 1.0 + 0.24 * ((0.9 - v.co.y) / 0.9) ** 1.3
            v.co.x *= 1.0 + (f - 1.0) * (1.0 if abs(v.co.x) > 1.0 else 0.0)
            v.co.z *= f

    rock._cut(bm, Vector((0.0, 0.0, 0.0)), Vector((0.0, -1.0, 0.0)))
    # 크라운 윗면을 얕게 쳐서 둥근 파이프가 아니라 풍화된 암반으로 읽히게 한다.
    top_n = Vector((rng.uniform(-0.18, 0.18), 1.0, rng.uniform(-0.14, 0.14))).normalized()
    rock._slab_cut(bm, top_n, rng.uniform(0.93, 0.96))
    for _ in range(rng.randint(3, 5)):
        nv = rng.unit_vector()
        nv.y = rng.uniform(-0.15, 0.35)
        nv.normalize()
        rock._slab_cut(bm, nv, rng.uniform(0.93, 0.97))
    mg.clean_bmesh(bm, dist=2e-4)
    return mg.new_object("arch", bm)


# ────────────────────────────────────────────────────────────────────────────
# b - 첨탑/오벨리스크. 위로 갈수록 급히 좁아지고 통째로 기운다.
# ────────────────────────────────────────────────────────────────────────────
def build_spire(seed):
    rng = mg.Rng(seed)
    bm = bmesh.new()
    sides, n = 11, 22
    height = 7.0
    lean = rng.uniform(0.19, 0.23)            # 꼭대기 수평 이동 = lean * H
    lean_dir = rng.uniform(0.0, math.tau)
    salt = seed * 0.27
    side_r = [1.0 + rng.uniform(-0.16, 0.16) for _ in range(sides)]
    twist = rng.uniform(0.10, 0.22)

    rings = []
    for i in range(n + 1):
        t = i / n
        y = height * t
        # 1차 렌더에서 (1-t)^1.30 + 0.055 는 위쪽이 너무 빨리 사라져 **상어 지느러미**로
        # 보였다(오벨리스크가 아니라 칼날). 지수를 1.0 으로 낮추고 최소 반지름을 0.14 로
        # 올려 꼭대기에 두께를 남긴다 - 밑동 지름 1.5m / 중간 0.9m / 꼭대기 0.3m.
        r = 0.62 * (1.0 - t) + 0.14
        if t < 0.18:                           # 밑동 플레어
            r *= 1.0 + 0.36 * ((0.18 - t) / 0.18) ** 1.25
        ring = []
        for k in range(sides):
            a = math.tau * k / sides + twist * t
            rr = r * side_r[k] * (1.0 + 0.10 * math.sin(t * 9.0 + k * 2.7 + salt))
            x = math.cos(a) * rr + math.cos(lean_dir) * lean * y
            z = math.sin(a) * rr * 0.88 + math.sin(lean_dir) * lean * y
            yy = y + _wob(x, y, z, salt, 0.055)
            ring.append((x, yy, z))
        rings.append(ring)
    _ring_tube(bm, rings)

    # 세로 벽개면 - 오벨리스크의 각을 세운다.
    for _ in range(rng.randint(4, 6)):
        nv = rng.unit_vector()
        nv.y = rng.uniform(-0.10, 0.10)
        nv.normalize()
        rock._slab_cut(bm, nv, rng.uniform(0.90, 0.95))
    # 꼭대기는 부러진 사면이다(뾰족한 원뿔 끝은 자연 첨탑이 아니라 콘 프리미티브로 읽힌다).
    brk = Vector((rng.uniform(-0.55, 0.55), 1.0, rng.uniform(-0.55, 0.55))).normalized()
    rock._slab_cut(bm, brk, rng.uniform(0.95, 0.975))
    rock._cut(bm, Vector((0.0, 0.0, 0.0)), Vector((0.0, -1.0, 0.0)))
    mg.clean_bmesh(bm, dist=2e-4)
    return mg.new_object("spire", bm)


# ────────────────────────────────────────────────────────────────────────────
# c - 주상절리 다발. 육각 기둥 7개가 높이차를 두고 선다.
# ────────────────────────────────────────────────────────────────────────────
def build_columns(seed):
    rng = mg.Rng(seed)
    bm = bmesh.new()
    salt = seed * 0.19
    # 육각 격자 배치(중심 + 6방향) - 실제 주상절리처럼 기둥끼리 면을 맞댄다.
    step = 0.66
    cells = [(0.0, 0.0)]
    for k in range(6):
        a = math.tau * k / 6
        cells.append((math.cos(a) * step * 1.72, math.sin(a) * step * 1.72))
    heights = [5.00, 3.15, 4.35, 2.05, 3.70, 2.60, 4.75]

    for idx, ((cx, cz), h) in enumerate(zip(cells, heights)):
        srng = rng.sub(idx)
        r = step * srng.uniform(0.86, 1.02)
        phase = srng.uniform(0.0, math.tau / 6)
        # 불규칙 육각 단면(정육각형이면 인공물이다).
        hexa = []
        for k in range(6):
            a = math.tau * k / 6 + phase + srng.uniform(-0.10, 0.10)
            rr = r * (1.0 + srng.uniform(-0.10, 0.10))
            hexa.append((math.cos(a) * rr, math.sin(a) * rr))
        # 기둥은 살짝 기울고, 꼭대기는 부러진 사면이다.
        lean_x = srng.uniform(-0.035, 0.035)
        lean_z = srng.uniform(-0.035, 0.035)
        tilt = (srng.uniform(-0.30, 0.30), srng.uniform(-0.30, 0.30))
        rings = []
        n = 4
        for i in range(n + 1):
            t = i / n
            y = h * t
            ring = []
            for (px, pz) in hexa:
                f = 1.0 - 0.07 * t
                x = cx + px * f + lean_x * y
                z = cz + pz * f + lean_z * y
                yy = y
                if i == n:
                    yy += tilt[0] * (x - cx) + tilt[1] * (z - cz)
                # 가로 절리(수평 균열선) - 기둥 옆면에 마디를 남긴다.
                x += _wob(x * 0.4, y * 4.1, z * 0.4, salt + idx, r * 0.055)
                z += _wob(z * 0.4, y * 4.1, x * 0.4, salt + idx + 3.0, r * 0.055)
                ring.append((x, yy, z))
            rings.append(ring)
        _ring_tube(bm, rings)

    mg.clean_bmesh(bm, dist=2e-4)
    return mg.new_object("columns", bm)


# ────────────────────────────────────────────────────────────────────────────
# d - 버섯 바위. 풍화로 허리가 잘록하고 갓이 크게 덮는다(언더컷이 정체성).
# ────────────────────────────────────────────────────────────────────────────
def build_mushroom(seed):
    rng = mg.Rng(seed)
    bm = bmesh.new()
    sides, n = 18, 22
    salt = seed * 0.23
    side_r = [1.0 + rng.uniform(-0.09, 0.09) for _ in range(sides)]
    # (높이비, 반지름) - 허리 0.45 / 갓 1.75 = 언더컷 비 3.9
    profile = [(0.00, 1.05), (0.10, 0.78), (0.26, 0.55), (0.42, 0.46),
               (0.52, 0.52), (0.60, 0.92), (0.66, 1.45), (0.72, 1.72),
               (0.82, 1.75), (0.90, 1.55), (0.96, 1.05), (1.00, 0.30)]
    height = 3.10

    def radius(t):
        for (t0, r0), (t1, r1) in zip(profile, profile[1:]):
            if t <= t1 or (t1, r1) == profile[-1]:
                k = (t - t0) / max(1e-6, t1 - t0)
                k = min(1.0, max(0.0, k))
                k = k * k * (3.0 - 2.0 * k)         # smoothstep - 프로파일 꺾임을 부드럽게
                return r0 + (r1 - r0) * k
        return profile[-1][1]

    rings = []
    for i in range(n + 1):
        t = i / n
        y = height * t
        r = radius(t)
        ring = []
        for k in range(sides):
            a = math.tau * k / sides
            rr = r * side_r[k] * (1.0 + 0.07 * math.sin(t * 7.0 + k * 1.9 + salt))
            x = math.cos(a) * rr
            z = math.sin(a) * rr * 0.94
            yy = y + _wob(x, y * 2.0, z, salt, 0.05)
            ring.append((x, yy, z))
        rings.append(ring)
    _ring_tube(bm, rings)

    # 갓 위를 얕게 쳐서 둥근 우산이 아니라 부서진 암반으로 읽히게 한다.
    top_n = Vector((rng.uniform(-0.22, 0.22), 1.0, rng.uniform(-0.22, 0.22))).normalized()
    rock._slab_cut(bm, top_n, rng.uniform(0.94, 0.97))
    for _ in range(rng.randint(2, 4)):
        nv = rng.unit_vector()
        nv.y = rng.uniform(0.05, 0.30)
        nv.normalize()
        rock._slab_cut(bm, nv, rng.uniform(0.92, 0.96))
    rock._cut(bm, Vector((0.0, 0.0, 0.0)), Vector((0.0, -1.0, 0.0)))
    mg.clean_bmesh(bm, dist=2e-4)
    return mg.new_object("mushroom", bm)


# ────────────────────────────────────────────────────────────────────────────
# e - 층리 슬랩 스택. 수평 판 5장이 어긋나게 쌓인다.
# ────────────────────────────────────────────────────────────────────────────
def build_slabstack(seed):
    rng = mg.Rng(seed)
    bm = bmesh.new()
    salt = seed * 0.17
    y = 0.0
    cx = cz = 0.0
    thick = [0.75, 0.58, 0.66, 0.48, 0.53]
    rad = [1.75, 1.62, 1.48, 1.30, 1.02]
    for i in range(5):
        srng = rng.sub(i)
        poly = _poly_xz(srng, srng.randint(7, 9), rad[i], rad[i] * srng.uniform(0.78, 0.95),
                        rough=0.16, phase=srng.uniform(0.0, math.tau))
        # 판마다 한쪽으로 밀려 나온다 - 어긋남이 "쌓인 층"의 신호다.
        a = srng.uniform(0.0, math.tau)
        d = srng.uniform(0.20, 0.42)
        cx += math.cos(a) * d
        cz += math.sin(a) * d
        tilt = (srng.uniform(-0.11, 0.11), srng.uniform(-0.11, 0.11))
        _prism(bm, poly, y, y + thick[i], srng, taper=srng.uniform(0.93, 1.02),
               cx=cx, cz=cz, tilt=tilt, salt=salt + i * 3.0, mid_bulge=0.05)
        y += thick[i] - 0.045          # 판끼리 살짝 겹쳐 틈이 벌어지지 않게
    mg.clean_bmesh(bm, dist=2e-4)
    return mg.new_object("slabstack", bm)


# ────────────────────────────────────────────────────────────────────────────
# f - 균열 거석. 같은 덩어리를 세 번 만들어 다른 평면으로 자르고,
#     **높이에 비례한 변위**로 위가 벌어지는 쐐기 균열을 낸다(밑동은 붙어 있다).
# ────────────────────────────────────────────────────────────────────────────
def _megalith_base(seed):
    """같은 시드면 몇 번을 불러도 같은 덩어리(조각 3개가 원래 한 몸이어야 한다)."""
    rng = mg.Rng(seed)
    bm = _lump(seed, rng, subdiv=5, lobe_n=4, amp=(0.12, 0.26), y_damp=0.35)
    ys = [v.co.y for v in bm.verts]
    ymin, ymax = min(ys), max(ys)
    height = ymax - ymin
    twist = rng.uniform(0.0, math.tau)
    strata_dir = rng.uniform(0.0, math.tau)
    for v in bm.verts:
        t = (v.co.y - ymin) / height
        around = math.atan2(v.co.z, v.co.x)
        strata = 0.20 + 0.80 * (0.5 + 0.5 * math.cos(around - strata_dir)) ** 1.5
        phase = (t * 4.0 + 0.12 * math.sin(t * 7.3 + twist)) % 1.0
        lead = min(1.0, phase / 0.12)
        f = 1.0 + 0.10 * strata * lead * (1.0 - phase) ** 0.45
        f *= 1.0 - 0.20 * t
        if t < 0.30:
            f *= 1.0 + 0.30 * ((0.30 - t) / 0.30) ** 1.25
        v.co.x *= f
        v.co.z *= f
        v.co.y *= 1.75                       # 세로로 선 거석
    ys = [v.co.y for v in bm.verts]
    ymin, ymax = min(ys), max(ys)
    rock._cut(bm, Vector((0.0, ymin + (ymax - ymin) * 0.10, 0.0)), Vector((0.0, -1.0, 0.0)))
    for _ in range(rng.randint(5, 7)):
        nv = rng.unit_vector()
        nv.y = rng.uniform(-0.20, 0.35)
        nv.normalize()
        rock._slab_cut(bm, nv, rng.uniform(0.84, 0.92))
    top_n = Vector((rng.uniform(-0.18, 0.18), 1.0, rng.uniform(-0.18, 0.18))).normalized()
    rock._slab_cut(bm, top_n, rng.uniform(0.84, 0.90))
    return bm


def build_fractured(seed):
    rng = mg.Rng(seed)
    base = _megalith_base(seed)
    ys = [v.co.y for v in base.verts]
    y0, y1 = min(ys), max(ys)
    base.free()

    # 균열 평면 두 장 - 거의 수직, 방위가 조금 다르다.
    a1 = rng.uniform(0.0, math.tau)
    a2 = a1 + rng.uniform(0.28, 0.52)
    n1 = Vector((math.cos(a1), rng.uniform(-0.10, 0.10), math.sin(a1))).normalized()
    n2 = Vector((math.cos(a2), rng.uniform(-0.10, 0.10), math.sin(a2))).normalized()
    d1 = rng.uniform(-0.42, -0.22)
    d2 = rng.uniform(0.18, 0.40)
    gap1 = rng.uniform(0.085, 0.115)
    gap2 = rng.uniform(0.070, 0.100)
    # 균열이 시작되는 높이(그 아래는 붙어 있다).
    yc1 = y0 + (y1 - y0) * 0.14
    yc2 = y0 + (y1 - y0) * 0.30

    def wedge(bm, normal, sign, y_start, gap):
        for v in bm.verts:
            t = (v.co.y - y_start) / max(1e-6, y1 - y_start)
            t = min(1.0, max(0.0, t))
            t = t * t * (3.0 - 2.0 * t)
            v.co += normal * (sign * gap * t)

    pieces = []
    # A: n1 평면의 음의 쪽
    a = _megalith_base(seed)
    rock._cut(a, n1 * d1, n1)
    wedge(a, n1, -1.0, yc1, gap1)
    pieces.append(a)
    # B: 두 평면 사이
    b = _megalith_base(seed)
    rock._cut(b, n1 * d1, -n1)
    rock._cut(b, n2 * d2, n2)
    pieces.append(b)
    # C: n2 평면의 양의 쪽
    c = _megalith_base(seed)
    rock._cut(c, n2 * d2, -n2)
    wedge(c, n2, 1.0, yc2, gap2)
    pieces.append(c)

    for p in pieces:
        mg.clean_bmesh(p, dist=2e-4)
    return _merge(pieces, "fractured")


# ────────────────────────────────────────────────────────────────────────────
# g - 표석 군집. 크고 작은 둥근 바위 6개를 한 메시로.
# ────────────────────────────────────────────────────────────────────────────
def build_cluster(seed):
    rng = mg.Rng(seed)
    # (반지름, x, z, 밑면 y) - 큰 것 하나 + 중간 둘 + 작은 셋. 서로 살짝 맞닿는다.
    layout = [
        (1.20, -0.55, -0.20, -0.05),
        (0.86, 1.25, 0.62, -0.03),
        (0.74, -1.92, 0.78, -0.04),
        (0.52, 0.72, -1.28, -0.02),
        (0.44, 2.28, -0.55, -0.05),
        (0.38, -1.30, -1.42, -0.01),
    ]
    bms = []
    for i, (r, px, pz, by) in enumerate(layout):
        srng = rng.sub(i)
        bm = _lump(seed + i * 11, srng, subdiv=4, lobe_n=4, amp=(0.14, 0.30), y_damp=0.55)
        _flatten_bottom(bm, frac=0.14)
        for _ in range(srng.randint(2, 4)):    # 표석은 절단이 얕다(침식으로 둥글다)
            nv = srng.unit_vector()
            nv.y = srng.uniform(-0.25, 0.35)
            nv.normalize()
            rock._slab_cut(bm, nv, srng.uniform(0.90, 0.96))
        s = (r, r * srng.uniform(0.62, 0.80), r * srng.uniform(0.85, 1.10))
        _place(bm, s, srng.uniform(0.0, math.tau), "X", srng.uniform(-0.10, 0.10),
               px, pz, by)
        mg.clean_bmesh(bm, dist=1e-4)
        bms.append(bm)
    return _merge(bms, "cluster")


# ────────────────────────────────────────────────────────────────────────────
# h - 기울어진 판석. 큰 평판이 68도로 비스듬히 박혀 있다.
# ────────────────────────────────────────────────────────────────────────────
def build_flagstone(seed):
    rng = mg.Rng(seed)
    bms = []

    bm = bmesh.new()
    salt = seed * 0.29
    poly = _poly_xz(rng, 9, 1.95, 1.55, rough=0.13, phase=rng.uniform(0.0, math.tau))
    _prism(bm, poly, -0.30, 0.30, rng, taper=0.97, salt=salt, mid_bulge=0.03)
    # 판을 세운다: Z 축 둘레 68도 -> 판의 법선이 Y 에서 X 쪽으로 눕는다.
    ang = math.radians(68.0)
    bmesh.ops.transform(bm, matrix=(Matrix.Rotation(rng.uniform(-0.25, 0.25), 4, "Y") @
                                    Matrix.Rotation(ang, 4, "Z")), verts=bm.verts[:])
    # 아래쪽을 땅에 박는다(잘라 낼 몫만큼 내린다).
    ymin = min(v.co.y for v in bm.verts)
    bmesh.ops.transform(bm, matrix=Matrix.Translation(Vector((0.0, -ymin - 0.55, 0.0))),
                        verts=bm.verts[:])
    rock._cut(bm, Vector((0.0, 0.0, 0.0)), Vector((0.0, -1.0, 0.0)))
    for _ in range(rng.randint(2, 4)):
        nv = rng.unit_vector()
        nv.y = rng.uniform(0.0, 0.35)
        nv.normalize()
        rock._slab_cut(bm, nv, rng.uniform(0.90, 0.96))
    mg.clean_bmesh(bm, dist=2e-4)
    bms.append(bm)

    # 밑동에 받침돌 둘 - "박혀 있다"를 읽게 하는 최소한의 신호.
    for i, (px, pz, r) in enumerate(((0.62, 0.42, 0.40), (-0.48, -0.52, 0.30))):
        srng = rng.sub(50 + i)
        chock = _lump(seed + 40 + i * 7, srng, subdiv=3, lobe_n=3,
                      amp=(0.16, 0.34), y_damp=0.6)
        _flatten_bottom(chock, frac=0.16)
        _place(chock, (r, r * 0.70, r * 0.90), srng.uniform(0.0, math.tau), "Z",
               srng.uniform(-0.12, 0.12), px, pz, 0.0)
        mg.clean_bmesh(chock, dist=1e-4)
        bms.append(chock)

    return _merge(bms, "flagstone")


# ────────────────────────────────────────────────────────────────────────────
# i - 벌집 풍화암(타포니). 표면에 얕은 오목 구멍 22개.
# ────────────────────────────────────────────────────────────────────────────
def build_honeycomb(seed):
    rng = mg.Rng(seed)
    bm = _lump(seed, rng, subdiv=5, lobe_n=5, amp=(0.14, 0.30), y_damp=0.55)
    for v in bm.verts:                       # 살짝 눌러 앉은 덩어리로
        v.co.y *= 0.90

    pits = []
    for _ in range(22):
        d = rng.unit_vector()
        d.y = abs(d.y) * 0.85 + 0.05         # 밑면에는 구멍을 내지 않는다(안 보인다)
        d.normalize()
        pits.append((d, rng.uniform(0.15, 0.24), rng.uniform(0.11, 0.19)))

    for v in bm.verts:
        d = v.co.normalized()
        r = v.co.length
        for (pc, ang_r, depth) in pits:
            cutoff = math.cos(ang_r)
            c = d.dot(pc)
            if c > cutoff:
                f = (c - cutoff) / (1.0 - cutoff)
                r -= depth * math.sqrt(f)     # 그릇 모양 - 가장자리에서 각이 선다
        v.co = d * max(0.25, r)

    _flatten_bottom(bm, frac=0.16)
    mg.clean_bmesh(bm, dist=1e-4)
    return mg.new_object("honeycomb", bm)


# ────────────────────────────────────────────────────────────────────────────
# j - 계단식 노두. 자연 계단 4단. 단높이 0.55m 이하 = 플레이어가 올라갈 수 있다.
# ────────────────────────────────────────────────────────────────────────────
def build_steps(seed):
    rng = mg.Rng(seed)
    bm = bmesh.new()
    salt = seed * 0.13
    # 단높이는 **최종 치수 기준 0.6m 이하**여야 플레이어가 올라간다(요구사항).
    # 목표 H 2.00m / 4단 = 명목 0.50m 이고, 윗면 기울기·요철까지 더한 실측 최대는 0.53m 다
    # (아래 STEP 실측 참고). 목표 H 2.20m 로 두었을 때는 최대 0.586m 로 여유가 없었다.
    rise = 0.50
    # 단이 올라갈수록 -Z 로 물러나고 좁아진다 -> 옆에서 보면 계단, 위에서 보면 반원 노두.
    steps = [
        (2.50, 2.10, 0.00, 0.00),
        (2.05, 1.72, 0.00, -0.52),
        (1.58, 1.30, 0.06, -1.00),
        (1.05, 0.86, 0.10, -1.36),
    ]
    for i, (rx, rz, cx, cz) in enumerate(steps):
        srng = rng.sub(i)
        poly = _poly_xz(srng, srng.randint(9, 11), rx, rz, rough=0.09,
                        phase=srng.uniform(0.0, math.tau))
        _prism(bm, poly, 0.0, rise * (i + 1), srng, taper=0.985, cx=cx, cz=cz,
               tilt=(srng.uniform(-0.03, 0.03), srng.uniform(-0.04, 0.015)),
               salt=salt + i * 5.0, mid_bulge=0.02)
    mg.clean_bmesh(bm, dist=2e-4)
    return mg.new_object("steps", bm)


# ────────────────────────────────────────────────────────────────────────────
# k - 쐐기 바위. 바위 하나가 두 바위 사이에 끼어 있다(아래가 뚫린다).
# ────────────────────────────────────────────────────────────────────────────
def build_wedged(seed):
    rng = mg.Rng(seed)
    bms = []
    # 좌우 기둥 둘 - **실치수로** 앉힌다(간격 1.45m 가 곧 개구 폭이다).
    #   왼쪽 x -2.00 ~ -0.75 / 오른쪽 x 0.70 ~ 2.00
    for i, (x_lo, x_hi, hh, dd) in enumerate(((-2.00, -0.75, 2.85, 1.55),
                                              (0.70, 2.00, 2.60, 1.70))):
        srng = rng.sub(i)
        bm = _lump(seed + i * 13, srng, subdiv=4, lobe_n=4, amp=(0.10, 0.22), y_damp=0.30)
        _flatten_bottom(bm, frac=0.12)
        for _ in range(srng.randint(3, 5)):
            nv = srng.unit_vector()
            nv.y = srng.uniform(-0.12, 0.22)
            nv.normalize()
            rock._slab_cut(bm, nv, srng.uniform(0.84, 0.92))
        # 안쪽 면을 평평하게 깎는다 - 쐐기가 걸리는 벽이자 개구의 옆벽이다.
        inner = Vector((1.0 if i == 0 else -1.0, 0.10, 0.0)).normalized()
        rock._slab_cut(bm, inner, 0.86)
        bmesh.ops.transform(bm, matrix=Matrix.Rotation(srng.uniform(0.0, math.tau), 4, "Y"),
                            verts=bm.verts[:])
        _fit_bm(bm, (x_hi - x_lo, hh, dd), cx=(x_lo + x_hi) * 0.5, cz=srng.uniform(-0.1, 0.1))
        mg.clean_bmesh(bm, dist=1e-4)
        bms.append(bm)

    # 끼인 쐐기 - 폭 2.55m 로 간격(1.45m)보다 넓어 양쪽 기둥에 0.55m 씩 물린다.
    # (1차 시도 2.15m 는 z=0 단면에서 왼쪽 기둥과 2cm 슬릿이 벌어져 개구가 위로 새었다 -
    #  실측이 "개구 없음"으로 나왔고, 보기에도 끼인 게 아니라 걸친 것으로 읽혔다.)
    srng = rng.sub(9)
    wedge = _lump(seed + 77, srng, subdiv=4, lobe_n=4, amp=(0.12, 0.28), y_damp=0.6)
    for _ in range(4):
        nv = srng.unit_vector()
        nv.y = srng.uniform(-0.20, 0.30)
        nv.normalize()
        rock._slab_cut(wedge, nv, srng.uniform(0.78, 0.88))
    bmesh.ops.transform(wedge, matrix=(Matrix.Rotation(srng.uniform(0.0, math.tau), 4, "Y") @
                                       Matrix.Rotation(srng.uniform(0.12, 0.20), 4, "Z")),
                        verts=wedge.verts[:])
    _fit_bm(wedge, (2.55, 1.18, 1.30), cx=-0.05, cz=srng.uniform(-0.12, 0.12), base_y=1.55)
    mg.clean_bmesh(wedge, dist=1e-4)
    bms.append(wedge)

    return _merge(bms, "wedged")


# ────────────────────────────────────────────────────────────────────────────
# l - 낮은 노두 판. 지면에서 살짝 솟은 넓고 낮은 암반(바위섬 지면 피복).
#     방사 격자로 윗면을 만들고 치마를 y=0 까지 내린 뒤 밑면을 덮는다.
# ────────────────────────────────────────────────────────────────────────────
def build_pavement(seed):
    rng = mg.Rng(seed)
    bm = bmesh.new()
    salt = seed * 0.11
    n_ang, n_rad = 24, 6
    rx, rz = 3.50, 2.75
    edge = [1.0 + rng.uniform(-0.13, 0.13) for _ in range(n_ang)]
    # 단 2개 + 완만한 경사 - "암반"으로 읽히게 하는 최소한의 구조.
    tier_dir = rng.uniform(0.0, math.tau)

    def top_y(x, z, u):
        base = 1.05 * (1.0 - 0.42 * u ** 2.4)
        s = (math.cos(tier_dir) * x + math.sin(tier_dir) * z) / rx
        base -= 0.16 * (1.0 if s > 0.18 else 0.0)
        base -= 0.14 * (1.0 if s > 0.62 else 0.0)
        base += _wob(x, z * 1.6, x + z, salt, 0.055)
        return max(0.30, base)

    # 정점 격자
    grid = []
    center = bm.verts.new((0.0, top_y(0.0, 0.0, 0.0), 0.0))
    for j in range(1, n_rad + 1):
        u = j / n_rad
        row = []
        for k in range(n_ang):
            a = math.tau * k / n_ang
            e = edge[k] * (1.0 + 0.05 * math.sin(a * 3.0 + salt))
            x = math.cos(a) * rx * u * e
            z = math.sin(a) * rz * u * e
            row.append(bm.verts.new((x, top_y(x, z, u), z)))
        grid.append(row)

    for k in range(n_ang):                       # 중심 팬
        bm.faces.new((center, grid[0][k], grid[0][(k + 1) % n_ang]))
    for j in range(n_rad - 1):                   # 링 사이
        for k in range(n_ang):
            k2 = (k + 1) % n_ang
            bm.faces.new((grid[j][k], grid[j + 1][k], grid[j + 1][k2], grid[j][k2]))

    outer = grid[-1]
    skirt = [bm.verts.new((v.co.x * 1.02, 0.0, v.co.z * 1.02)) for v in outer]
    for k in range(n_ang):                       # 옆 치마
        k2 = (k + 1) % n_ang
        bm.faces.new((outer[k], skirt[k], skirt[k2], outer[k2]))
    bm.faces.new(skirt)                          # 밑면

    mg.clean_bmesh(bm, dist=2e-4)
    return mg.new_object("pavement", bm)


BUILDERS = {
    "rockform_a": build_arch,
    "rockform_b": build_spire,
    "rockform_c": build_columns,
    "rockform_d": build_mushroom,
    "rockform_e": build_slabstack,
    "rockform_f": build_fractured,
    "rockform_g": build_cluster,
    "rockform_h": build_flagstone,
    "rockform_i": build_honeycomb,
    "rockform_j": build_steps,
    "rockform_k": build_wedged,
    "rockform_l": build_pavement,
}


# ────────────────────────────────────────────────────────────────────────────
# 개구 실측 - **내보낸 OBJ 를 다시 읽어** z=0 단면에서 레이 패리티로 잰다.
# (눈으로 "뚫린 것 같다"는 검증이 아니다. 아치가 실제로 통과 가능한지는 숫자로만 안다.)
# ────────────────────────────────────────────────────────────────────────────
def _read_obj(path):
    verts, tris = [], []
    with open(path, "r") as fh:
        for line in fh:
            if line.startswith("v "):
                p = line.split()
                verts.append((float(p[1]), float(p[2]), float(p[3])))
            elif line.startswith("f "):
                idx = [int(t.split("/")[0]) - 1 for t in line.split()[1:]]
                tris.append(tuple(idx[:3]))
    return verts, tris


def _inside(bvh, p, direction=Vector((1.0, 0.0, 0.0))):
    """닫힌 껍질의 합집합에 대한 내부 판정 - 레이 교차 **패리티**.
    껍질이 여러 개 겹쳐 있어도(군집·쐐기) 바깥 점은 항상 짝수 번 교차한다."""
    origin = Vector(p)
    count = 0
    for _ in range(128):
        hit = bvh.ray_cast(origin, direction, 1e4)
        if hit[0] is None:
            break
        count += 1
        origin = hit[0] + direction * 1e-4
    return count % 2 == 1


def measure_opening(path, z_slice=0.0, step=0.02):
    """z=z_slice 단면에서 **바닥에서 시작해 사방이 막힌 빈 영역**(= 개구)을 찾아 잰다.

    반환: dict(width_min, width_at, height, through) 또는 None(개구 없음).
      clearance   - 바닥~천장 80% 구간의 **최소 폭**(플레이어가 지나갈 수 있는 실효 폭)
      height      - 개구 천장 높이
      floor_width - 지면에서의 폭
      profile     - 높이별 폭
      through     - 개구 중심에서 Z 축을 따라 앞뒤로 실제로 뚫려 있는가
    """
    verts, tris = _read_obj(path)
    bvh = BVHTree.FromPolygons(verts, tris, all_triangles=True)
    xs = [v[0] for v in verts]
    ys = [v[1] for v in verts]
    zs = [v[2] for v in verts]
    x0, x1 = min(xs), max(xs)
    y1 = max(ys)
    nx = int((x1 - x0) / step) + 1
    ny = int(y1 / step) + 1

    free = [[not _inside(bvh, (x0 + i * step, 0.004 + j * step, z_slice))
             for j in range(ny)] for i in range(nx)]

    # 바닥 중앙에서 flood fill. 좌우 끝(x0/x1)에 닿으면 그건 바깥 공간이라 개구가 아니다.
    ci = nx // 2
    if not free[ci][0]:
        for off in range(1, nx // 2):
            if free[min(nx - 1, ci + off)][0]:
                ci = ci + off
                break
            if free[max(0, ci - off)][0]:
                ci = ci - off
                break
    if not free[ci][0]:
        return None
    seen = [[False] * ny for _ in range(nx)]
    stack = [(ci, 0)]
    seen[ci][0] = True
    cells = []
    escaped = False
    while stack:
        i, j = stack.pop()
        cells.append((i, j))
        if i == 0 or i == nx - 1 or j == ny - 1:
            escaped = True
        for di, dj in ((1, 0), (-1, 0), (0, 1), (0, -1)):
            a, b = i + di, j + dj
            if 0 <= a < nx and 0 <= b < ny and not seen[a][b] and free[a][b]:
                seen[a][b] = True
                stack.append((a, b))
    if escaped or not cells:
        return None

    rows = {}
    for i, j in cells:
        rows.setdefault(j, []).append(i)
    height = (max(rows) * step) + 0.004

    def run_width(j):
        """그 높이에서 **중앙을 포함하는 연속 빈 구간**의 폭(끊긴 조각은 세지 않는다)."""
        if j not in rows:
            return 0.0
        idx = sorted(rows[j])
        best = run = 1
        for k in range(1, len(idx)):
            run = run + 1 if idx[k] == idx[k - 1] + 1 else 1
            best = max(best, run)
        return best * step

    # 유효 통과 폭: 바닥부터 개구 높이의 80% 까지의 최소 폭.
    # (천장 바로 밑은 아치·쐐기 모두 필연적으로 좁아진다 - 거기까지 세면 항상 0 이 나온다.)
    lim = int(height * 0.80 / step)
    clearance = min(run_width(j) for j in range(0, max(1, lim) + 1))
    profile = {}
    for h in (0.2, 0.5, 1.0, 1.5, 2.0, 2.5, 3.0):
        if h < height:
            profile[h] = round(run_width(int((h - 0.004) / step)), 3)

    # 관통 여부: 개구 중심에서 Z 를 따라 훑는다.
    mid_j = max(rows) // 2
    mid_i = sum(rows[mid_j]) / len(rows[mid_j])
    px = x0 + mid_i * step
    py = 0.004 + mid_j * step
    zlo, zhi = min(zs) - 0.5, max(zs) + 0.5
    n_z = int((zhi - zlo) / 0.05) + 1
    through = all(not _inside(bvh, (px, py, zlo + t * 0.05)) for t in range(n_z))
    return {"clearance": clearance, "height": height, "through": through,
            "floor_width": run_width(0), "profile": profile}



# ────────────────────────────────────────────────────────────────────────────
def _finish(obj, name, seed, size, budget, align, note):
    """감면 -> 크기 -> UV -> 계약 -> 내보내기 -> 검증 -> usemtl -> 렌더."""
    obj.name = name + "_rock"
    mg.decimate_to_budget(obj, int(budget * 0.92))
    mg.fit_size(obj, size)
    mg.shade_flat(obj)
    mg.box_uv(obj, tile=UV_TILE)
    stats = mg.enforce_contract(obj, tri_budget=budget, tri_floor=150,
                                expect_size=size, name=name, align=align,
                                ground_band=0.06)
    out = os.path.join(mg.MODELS_DIR, name + ".obj")
    mg.export_obj(obj, out)
    stats = mg.verify_obj_file(out, stats)
    mg.inject_usemtl(out)          # export -> verify -> inject 순서는 계약이다(mgbuild 주석)
    mg.assign_material(obj, mg.preview_material(
        "pv_" + name, texture_name="rock", base_color=STONE, roughness=0.82))
    mg.turntable(obj, os.path.join(PREVIEW_SUB, name + ".png"),
                 title=f"{name}   seed {seed}", stats=stats, notes=note,
                 px=380, samples=20)
    mg.report(stats)
    return stats


def main():
    print("[rockform] 형태 축 12종 생성")
    os.makedirs(PREVIEW_SUB, exist_ok=True)
    manifest = []
    for name, seed, size, budget, align, note in SPECS:
        mg.reset_scene()
        obj = BUILDERS[name](seed)
        obj = _finish(obj, name, seed, size, budget, align,
                      f"{note} / box UV {UV_TILE:.2f}m")
        info = None
        if name in OPENING:
            info = measure_opening(obj["path"])
            if info is None:
                print(f"    !! {name}: 개구를 찾지 못했다 - 뚫려 있지 않다")
            else:
                print(f"    개구 {name}: 유효폭 {info['clearance']:.2f}m x 높이 "
                      f"{info['height']:.2f}m  바닥폭 {info['floor_width']:.2f}m  "
                      f"관통 {info['through']}  폭프로파일 {info['profile']}")
        manifest.append((name, obj, info))

    print("ROCKFORM_MANIFEST")
    for name, st, info in manifest:
        s = st["size"]
        extra = ""
        if info:
            extra = f"  개구 {info['clearance']:.2f}x{info['height']:.2f}m"
        print(f"  {name}  {st['tris']}tri/{st['budget']}  "
              f"{s[0]:.2f}x{s[1]:.2f}x{s[2]:.2f}m{extra}")
    print(f"[rockform] 완료 - {len(manifest)}종")
    return manifest


if __name__ == "__main__":
    main()
