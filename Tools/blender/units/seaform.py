#!/usr/bin/env python3
"""
seaform_a~h - 해저 지형 바위 8종 (2026-08-18, 사용자 요청 "수중 바위 다양성").

    python3 Tools/blender/units/seaform.py

산출물: Assets/_Project/Resources/Models/seaform_a~h.obj (+.mtl)
        Tools/blender/_preview/seaform_*.png

왜 새 계열인가: 기존 searock_a~t(20종)는 전부 **한 덩어리 소품**이다 - 표석/판형/첨탑/군집
넷 다 "돌 하나를 모래에 얹는" 실루엣이라 아무리 늘려도 해저가 단조롭다. seaform 은
**지형(landform)** 계열로, 플레이어가 통과하거나 밑에 들어가거나 타고 오르는 구조물이다.
실루엣이 searock 과 겹치지 않게 8종 전부 "구멍/단차/오버행/통로" 중 하나를 갖는다.

  a 해저 아치      폭 6m, 개구 2.5m+ - 잠수해 통과하는 랜드마크          large_structure
  b 기둥 군집      가는 암주 6~9개 숲, 높이 3~5m, 총 폭 5m               small_prop
  c 계단식 리지    층층 암반 능선, 길이 8m 높이 2.5m - 타고 오른다        large_structure
  d 협곡 블록 쌍   두 벽 사이 통로 1.5m+, 각 높이 4m                     small_prop
  e 오버행 바위    위가 튀어나온 그늘 - 물고기 은신처, 폭 4.5m           small_prop
  f 균열 암반 판   넓고 낮은 암반 + 깊은 틈, 폭 7m 높이 1.0m - 피복용     large_structure
  g 탑 바위        단독 첨탑 6m - 깊은 곳 수직 랜드마크                   small_prop
  h 잔해 더미      각진 파편이 무너진 더미, 폭 5m 높이 2m                small_prop

계약: 미터 / +Y up / +Z front / 밑면 y=0 / o 오브젝트 1개(seaform_x_rock) = 런타임 머티리얼
1장 / 머티리얼 없이 export -> verify -> inject_usemtl(서브메시 구분자) 순서. 시드 74001~74008
고정이라 재실행해도 md5 가 같다.

표면 규약: searock 의 "침식으로 둥글다"를 그대로 따르되(뭍 바위보다 매끈), 크기가 커진 만큼
지터 대신 **다옥타브 펄린 변위**(cave.py 와 같은 방식, mathutils.noise 는 순열표가 고정이라
완전히 결정적)로 굴곡을 준다. 변위는 2층이다 - 축 반경에 **비례하는** 저주파(rel: 실루엣을
깬다)와 **미터 단위** 고주파(amp: 표면 잔굴곡). 미터 단위 한 층만 쓰면 7~8m 짜리 판/능선이
"베개"로 렌더된다(실측 후 고침).

프리미티브 2종:
  rocky_blob  아이코스피어 -> 초타원체. 둥근 덩어리(a 버트리스·b 둔덕·e 밑동·g 선반·h 바닥).
  rocky_slab  큐브 격자. 면이 평면으로 남고 모서리에 정점 줄이 있어 **각이 선다** - 층리(c)·
              협곡 벽(d)·암반 판(f)·오버행 갓(e)이 "돌"이 아니라 "암반"으로 읽히려면 필요하다.
셰이딩: a/b/e 스무스(둥근 표석 계열), c/d/f/g/h 플랫(암반·첨탑·파편 - searock 규약과 같다).

접지 규약: 모든 종을 y=0 아래까지 만들어 두고 `ground_cut` 으로 잘라 **평평한 접지면**을
만든다. searock 처럼 둥근 밑면을 그대로 두면 7m 짜리 판(f)이 가운데 한 점으로 서서
가장자리가 24cm 뜬다(실측). 잘라 낸 단면은 holes_fill 로 막아 밑에서 뚫려 보이지 않게 한다.

실측 검증: 개구/통로가 있는 a/d/e 는 BVH 레이캐스트로 실제 뚫려 있는지와 치수를 재고,
기준(개구 2.5m / 통로 1.5m / 오버행 그늘 깊이 0.8m)에 못 미치면 ContractError 로 죽는다.
"""

import math
import os
import shutil
import sys

_UNITS_DIR = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.dirname(_UNITS_DIR))

import mgbuild as mg  # noqa: E402
import bmesh  # noqa: E402
import bpy  # noqa: E402
from mathutils import Matrix, Vector, noise  # noqa: E402
from mathutils.bvhtree import BVHTree  # noqa: E402


# ── 노이즈 ────────────────────────────────────────────────────────────────────
def fbm(p, off, freq=1.0, octaves=4, gain=0.5, lac=2.07):
    """다옥타브 펄린. mathutils.noise 는 순열표가 고정이라 완전히 결정적이다(cave.py 와 동일)."""
    total, amp, f = 0.0, 1.0, freq
    for _ in range(octaves):
        total += amp * noise.noise((p + off) * f)
        amp *= gain
        f *= lac
    return total


def _pw(t, e):
    """t**e 를 음수 t 에서도 안전하게. sweep 이 접선을 구하려고 t-1e-4 를 넣는데
    음수의 실수 거듭제곱은 파이썬에서 complex 가 되어 Vector 생성이 죽는다(실제로 걸렸다)."""
    return max(t, 0.0) ** e


def _noise_off(rng):
    return Vector((rng.uniform(0.0, 40.0), rng.uniform(0.0, 40.0), rng.uniform(0.0, 40.0)))


# ── 형상 소도구 ───────────────────────────────────────────────────────────────
def rocky_blob(bm, rng, center, semi, rel=0.11, amp=0.05, freq=1.5, detail_freq=5.5,
               subdiv=3, exponent=2.0, yaw=0.0, tilt=0.0):
    """노이즈 변위 준 **초타원체** 덩어리를 bm 에 덧붙인다(닫힌 솔리드).

    exponent 가 실루엣을 정한다. 2.0 이면 보통 타원체(둥근 표석), 4~8 로 올리면 모서리가
    서서 **판상 암반 블록**이 된다 - 계단식 리지(c)·협곡 벽(d)·암반 판(f)이 "돌"이 아니라
    "암반"으로 읽히려면 이 각이 필요하다. 아이코스피어를 방향별로 초타원체 표면까지 밀어
    내는 방식이라 극점 특이점이 없고 전부 삼각형이다.

    변위는 **2층**이다. 처음엔 미터 단위 한 층만 줬더니 8m 짜리 리지와 7m 짜리 판이
    "베개"로 렌더됐다(실루엣이 그냥 타원). 층을 나눈 이유:
      rel  축 반경에 **비례하는** 저주파 변위 - 실루엣을 깬다. 비례라서 얇은 판(두께 1m)의
           두께는 안 건드리면서 긴 축(7m)만 크게 흔든다. 미터 단위 한 값으로는 이게 안 된다.
      amp  미터 단위 고주파 변위 - 침식된 표면 잔굴곡(searock 의 lumps 규약에 대응).
    """
    res = bmesh.ops.create_icosphere(bm, subdivisions=subdiv, radius=1.0)
    off_lo, off_hi = _noise_off(rng), _noise_off(rng)
    m = Matrix.Rotation(yaw, 4, "Y") @ Matrix.Rotation(tilt, 4, "X")
    e = float(exponent)
    for v in res["verts"]:
        n = v.co.normalized()
        sc = (abs(n.x) ** e + abs(n.y) ** e + abs(n.z) ** e) ** (-1.0 / e)
        p = Vector((n.x * sc * semi[0], n.y * sc * semi[1], n.z * sc * semi[2]))
        p *= 1.0 + rel * fbm(n, off_lo, freq)
        p += n * (amp * fbm(n, off_hi, detail_freq, octaves=2))
        v.co = m @ p + Vector(center)
    return res["verts"]


def _merge(dst, src):
    """임시 bmesh 를 누적 bmesh 에 덧붙인다(메시 왕복).

    파츠 하나에 bmesh **전체 오퍼레이터**(subdivide_edges 등)를 쓰려면 이 방식이 필요하다.
    누적 bm 에 직접 만들면 subdivide 가 반환 정점을 무효화해 죽는다(ReferenceError 로 걸렸다).
    """
    me = bpy.data.meshes.new("_mgtmp")
    src.to_mesh(me)
    src.free()
    dst.from_mesh(me)
    bpy.data.meshes.remove(me, do_unlink=True)


def rocky_slab(bm, rng, center, semi, rel=0.10, amp=0.05, freq=1.6, detail_freq=5.0,
               cuts=4, round_amt=0.20, yaw=0.0, tilt=0.0):
    """격자 분할한 정육면체를 노이즈로 민 **판상 암반 블록**.

    rocky_blob(아이코스피어)은 exponent 를 아무리 올려도 '베개'로 렌더된다 - 정점이
    모서리에 모이지 않아 각이 서지 않는다(계단식 리지 c 가 팬케이크 더미로, 암반 판 f 가
    매트리스로 나왔다). 층리·벽·판은 큐브 격자가 맞다: 면이 평면으로 남고 모서리에 정점
    줄이 있어 각이 살아 있으며, 같은 삼각형 수로 훨씬 '암반'처럼 읽힌다.

    round_amt  모서리만 구면 쪽으로 당겨 **침식된 둥근 각**을 만든다(0 이면 날 선 큐브).
    rel / amp  rocky_blob 과 같은 2층 변위(비례 저주파 + 미터 고주파).
    """
    tb = bmesh.new()
    bmesh.ops.create_cube(tb, size=2.0)
    bmesh.ops.subdivide_edges(tb, edges=tb.edges[:], cuts=cuts, use_grid_fill=True)
    off_lo, off_hi = _noise_off(rng), _noise_off(rng)
    m = Matrix.Rotation(yaw, 4, "Y") @ Matrix.Rotation(tilt, 4, "X")
    for v in tb.verts:
        c = v.co.copy()
        n = c.normalized()
        q = c * (1.0 - round_amt) + n * round_amt
        p = Vector((q.x * semi[0], q.y * semi[1], q.z * semi[2]))
        p *= 1.0 + rel * fbm(n, off_lo, freq)
        p += n * (amp * fbm(n, off_hi, detail_freq, octaves=2))
        v.co = m @ p + Vector(center)
    bmesh.ops.recalc_face_normals(tb, faces=tb.faces[:])
    _merge(bm, tb)


def rocky_shard(bm, rng, center, size, jitter=0.10, subdiv=1, exponent=5.0):
    """각진 파편(잔해 더미 h 전용). 초타원체(exponent 5 = 거의 상자)에 정점 지터를 준 뒤
    임의 회전한다. blob 과 달리 **모서리가 살아 있어야** '부서진 조각'으로 읽히므로
    노이즈 변위(부드러운 굴곡) 대신 지터를 쓰고 셰이딩도 플랫으로 간다.

    (create_cube + subdivide_edges 로 만들다가 subdivide 가 원본 정점을 지워
    ReferenceError 로 죽었다 - 아이코스피어 경로는 반환된 정점이 그대로 살아 있다.)
    """
    res = bmesh.ops.create_icosphere(bm, subdivisions=subdiv, radius=1.0)
    m = (Matrix.Rotation(rng.uniform(0, math.tau), 4, "Y")
         @ Matrix.Rotation(rng.uniform(-0.55, 0.55), 4, "X")
         @ Matrix.Rotation(rng.uniform(-0.55, 0.55), 4, "Z"))
    e = float(exponent)
    for v in res["verts"]:
        n = v.co.normalized()
        sc = (abs(n.x) ** e + abs(n.y) ** e + abs(n.z) ** e) ** (-1.0 / e)
        p = Vector((n.x * sc * size[0], n.y * sc * size[1], n.z * sc * size[2]))
        p += Vector((rng.uniform(-1, 1), rng.uniform(-1, 1), rng.uniform(-1, 1))) * jitter
        v.co = m @ p + Vector(center)
    return res["verts"]


def sweep(bm, rng, path_fn, t0, t1, rings, sides, radius_fn, side_vec=(0.0, 0.0, 1.0),
          amp=0.12, freq=1.2):
    """경로를 따라 쓸어낸 닫힌 타원 단면 튜브(양끝 캡 = 닫힌 솔리드).

    프레임을 **고정 기준축 side_vec 으로** 잡는다. 접선에서 프레임을 만드는(Frenet) 방식은
    아치(a)처럼 접선이 수직이 되는 구간에서 외적이 0 으로 무너진다 - 아치는 XY 평면 안에서만
    휘므로 side_vec=+Z 로 고정하면 프레임이 끝까지 안정적이다. 첨탑(g)도 같은 이유로 쓴다.

    radius_fn(t) -> (r_side, r_up): side_vec 방향 반경 / 그와 접선의 외적 방향 반경.
    """
    off = _noise_off(rng)
    side0 = Vector(side_vec).normalized()
    loops = []
    for k in range(rings):
        t = t0 + (t1 - t0) * k / (rings - 1)
        c = path_fn(t)
        tan = (path_fn(t + 1e-4) - path_fn(t - 1e-4)).normalized()
        up = side0.cross(tan).normalized()
        r_s, r_u = radius_fn(t)
        loop = []
        for i in range(sides):
            a = math.tau * i / sides
            radial = (side0 * math.cos(a) + up * math.sin(a)).normalized()
            p = c + side0 * (r_s * math.cos(a)) + up * (r_u * math.sin(a))
            p += radial * (amp * fbm(p * 0.30, off, freq))
            p += radial * (amp * 0.45 * fbm(p * 1.20, off, freq * 2.5, octaves=2))
            loop.append(bm.verts.new(p))
        loops.append(loop)
    faces = []
    for lo, hi in zip(loops, loops[1:]):
        for i in range(sides):
            j = (i + 1) % sides
            faces.append(bm.faces.new((lo[i], lo[j], hi[j], hi[i])))
    faces.append(bm.faces.new(loops[0]))
    faces.append(bm.faces.new(loops[-1]))
    bmesh.ops.recalc_face_normals(bm, faces=faces)
    return loops


def bm_to_obj(name, bm, smooth=True):
    """bmesh 를 정리하고(용접·퇴화면 제거·삼각형화·법선) 오브젝트로 굽는다."""
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
    mg.clean_bmesh(bm)
    for f in bm.faces:
        f.smooth = smooth
    return mg.new_object(name, bm)


def ground_cut(obj, y=0.0):
    """y 평면 아래를 잘라 내고 단면을 막아 **평평한 접지면**을 만든다.

    해저 바위는 모래에 얹히므로 밑면이 평평해야 가장자리가 뜨지 않는다(f 처럼 넓고 낮은
    판은 둥근 밑면이면 가장자리가 24cm 뜬다 - 실측). 잘라 낸 자리는 holes_fill 로 막는다.
    껍질이 여러 개(기둥 군집 b, 협곡 벽 d, 파편 h)여도 경계 루프마다 각각 막힌다.
    """
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    bmesh.ops.bisect_plane(bm, geom=bm.verts[:] + bm.edges[:] + bm.faces[:],
                           plane_co=(0.0, y, 0.0), plane_no=(0.0, 1.0, 0.0),
                           clear_inner=True, clear_outer=False)
    boundary = [e for e in bm.edges if e.is_boundary]
    if boundary:
        bmesh.ops.holes_fill(bm, edges=boundary)
    bmesh.ops.triangulate(bm, faces=bm.faces[:])
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
    mg.clean_bmesh(bm)
    bm.to_mesh(obj.data)
    bm.free()


def boolean_diff(target, cutter):
    """target -= cutter (EXACT). 커터는 소멸한다(cave.py 와 동일 규약)."""
    mod = target.modifiers.new("MG_Bool", "BOOLEAN")
    mod.operation = "DIFFERENCE"
    mod.solver = "EXACT"
    mod.object = cutter
    bpy.ops.object.select_all(action="DESELECT")
    target.select_set(True)
    bpy.context.view_layer.objects.active = target
    bpy.ops.object.modifier_apply(modifier=mod.name)
    mesh = cutter.data
    bpy.data.objects.remove(cutter, do_unlink=True)
    bpy.data.meshes.remove(mesh, do_unlink=True)


# ── a: 해저 아치 ──────────────────────────────────────────────────────────────
# 아치 중심선 반경. 개구 폭 = 2*(AX - r_up), 바깥 폭 = 2*(AX + r_up) 이므로
# AX=2.20 / 다리 r_up=0.70 -> 개구 3.00m, 다리 바깥 5.80m (밑동 덩어리로 6.0m 로 채운다).
AX_A, AY_A = 2.20, 3.50


def _path_a(t):
    a = math.pi * t
    # 마루를 +X 쪽으로 조금 밀어 좌우 비대칭으로 만든다(대칭 아치는 콘크리트 구조물로 보인다).
    return Vector((-AX_A * math.cos(a) + 0.18 * math.sin(a), AY_A * math.sin(a),
                   0.28 * math.sin(a * 1.7)))


def _radius_a(t):
    k = math.sin(math.pi * min(max(t, 0.0), 1.0))     # 0 = 다리 밑, 1 = 마루
    # 굵기를 t 의 결정적 파형으로 흔든다 - 일정한 굵기면 '돌 아치'가 아니라 '파이프'로 보인다.
    wob = 1.0 + 0.17 * math.sin(4.7 * t + 0.9) + 0.09 * math.sin(9.3 * t + 2.1)
    r_up = (0.70 - 0.09 * k) * wob
    r_z = (1.15 + 0.12 * (1.0 - k)) * (1.0 + 0.19 * math.sin(3.1 * t + 2.0))
    return r_z, r_up


def build_a(rng):
    bm = bmesh.new()
    # 다리를 y<0 까지 늘려 두고 나중에 ground_cut 으로 평평하게 자른다.
    sweep(bm, rng.sub(1), _path_a, -0.055, 1.055, rings=48, sides=20,
          radius_fn=_radius_a, side_vec=(0, 0, 1), amp=0.20, freq=1.1)
    # 다리 밑동 덩어리(버트리스). yaw 를 주지 않는다 - Z 반경(1.30)이 X 로 돌아 들어오면
    # 폭이 6m 를 넘는다(7.06m 로 걸렸다).
    for sx in (-1.0, 1.0):
        rocky_blob(bm, rng.sub(2 if sx < 0 else 3),
                   (sx * 2.26, 0.42, rng.uniform(-0.25, 0.25)),
                   (0.58, 0.88, 1.28), rel=0.13, amp=0.07, freq=1.7,
                   subdiv=3, exponent=2.8)
    # 한쪽 어깨에만 붙은 혹 - 좌우 대칭을 확실히 깬다.
    rocky_blob(bm, rng.sub(4), (1.55, 2.55, 0.35), (0.55, 0.70, 0.80),
               rel=0.16, amp=0.06, freq=2.0, subdiv=2, exponent=2.4,
               yaw=rng.uniform(0, math.tau))
    return bm_to_obj("seaform_a_rock", bm, smooth=True)


# ── b: 기둥 군집 ──────────────────────────────────────────────────────────────
def build_b(rng):
    bm = bmesh.new()
    count = rng.randint(6, 9)
    # 기둥 밑동을 링 위에 흩어 놓는다(정면에서 겹쳐 보이지 않게 각도에 지터).
    for i in range(count):
        a = math.tau * i / count + rng.uniform(-0.35, 0.35)
        rad = rng.uniform(1.25, 2.35)   # 하한을 올려야 총 폭 5m 가 나온다(4.3m 로 걸렸다)
        bx, bz = math.cos(a) * rad, math.sin(a) * rad * 0.85
        h = rng.uniform(3.0, 5.0)
        lean_x, lean_z = rng.uniform(-0.30, 0.30), rng.uniform(-0.26, 0.26)
        r0 = rng.uniform(0.30, 0.44)
        ph = rng.uniform(0.0, math.tau)

        def path(t, bx=bx, bz=bz, h=h, lean_x=lean_x, lean_z=lean_z):
            return Vector((bx + lean_x * _pw(t, 1.7), -0.25 + (h + 0.25) * t,
                           bz + lean_z * _pw(t, 1.7)))

        def rad_fn(t, r0=r0, ph=ph):
            # 위로 갈수록 **조금만** 가늘어진다. 뾰족하게 좁히면 침엽수 가시처럼 보인다
            # (첫 렌더가 그랬다) - 주상절리는 끝까지 기둥이고 꼭대기가 부러져 뭉툭하다.
            r = r0 * (1.0 - 0.34 * _pw(t, 1.3))
            r *= 1.0 + 0.15 * math.sin(6.2 * t + ph) + 0.08 * math.sin(11.4 * t + ph * 2)
            return r, r * (1.0 + 0.12 * math.sin(ph))

        sweep(bm, rng.sub(10 + i), path, 0.0, 1.0, rings=7, sides=8,
              radius_fn=rad_fn, side_vec=(0, 0, 1), amp=0.055, freq=2.2)
        # 부러진 꼭대기 - 기둥 절반에만 얹어 실루엣을 들쭉날쭉하게.
        if i % 2 == 0:
            rocky_blob(bm, rng.sub(60 + i),
                       (bx + lean_x, h - 0.12, bz + lean_z),
                       (r0 * 0.95, r0 * 0.55, r0 * 0.9), rel=0.20, amp=0.04,
                       freq=2.4, subdiv=1, exponent=2.6, yaw=rng.uniform(0, math.tau))
    # 밑동을 묶는 낮은 암반 둔덕 - 기둥이 모래에 그냥 꽂힌 것처럼 보이지 않게.
    rocky_blob(bm, rng.sub(2), (0.0, 0.10, 0.0), (1.95, 0.34, 1.70),
               rel=0.16, amp=0.06, freq=1.9, subdiv=2, exponent=3.2,
               yaw=rng.uniform(0, math.tau))
    return bm_to_obj("seaform_b_rock", bm, smooth=True)


# ── c: 계단식 리지 ────────────────────────────────────────────────────────────
def build_c(rng):
    bm = bmesh.new()
    # 층이 -Z 로 물러나며 쌓인다 = +Z 에서 보면 계단. 층마다 X 중심·길이를 어긋나게 잡아
    # 동심 팬케이크 더미가 아니라 **능선**이 되게 한다(아이코스피어판이 팬케이크였다).
    tiers = [
        ((0.00, 0.26, 0.10), (3.95, 0.36, 1.75), 5),
        ((-0.55, 0.80, -0.32), (3.25, 0.33, 1.35), 5),
        ((0.62, 1.32, -0.60), (2.45, 0.31, 1.05), 5),
        ((-0.32, 1.78, -0.82), (1.80, 0.29, 0.80), 4),
        ((0.58, 2.20, -0.98), (1.05, 0.26, 0.58), 4),
    ]
    for i, (c, sz, cu) in enumerate(tiers):
        rocky_slab(bm, rng.sub(1 + i), c, sz, rel=0.10, amp=0.05, freq=1.7,
                   cuts=cu, round_amt=0.22, yaw=rng.uniform(-0.13, 0.13),
                   tilt=rng.uniform(-0.04, 0.04))
    # 층 앞자락에 굴러 내린 잔석 - 계단 코가 너무 깨끗하지 않게.
    for i in range(4):
        rocky_blob(bm, rng.sub(20 + i),
                   (rng.uniform(-3.2, 3.2), 0.22, rng.uniform(1.1, 1.7)),
                   (0.48, 0.28, 0.40), rel=0.20, amp=0.05, freq=2.4,
                   subdiv=2, exponent=3.0, yaw=rng.uniform(0, math.tau))
    return bm_to_obj("seaform_c_rock", bm, smooth=False)


# ── d: 협곡 블록 쌍 ───────────────────────────────────────────────────────────
def build_d(rng):
    bm = bmesh.new()
    # 안쪽 면 공칭 ∓0.87 -> 통로 1.74m(실측 1.6m 대 = 스펙 "좁은 통로 1.5m" 에 여유 0.1m). 실측은 1.7m 대로 떨어진다: rel 변위(±0.06)보다
    # **yaw 회전**이 더 크게 먹는다(벽의 Z 길이 2.05m 가 0.09rad 돌면 안쪽으로 0.18m 들어온다).
    # 그래서 yaw 를 0.045 로 줄이고 벽을 더 벌렸다(∓0.92/yaw0.09 조합은 실측 1.50m 로
    # 기준선 1.5m 에 딱 붙었다).
    # 큐브 격자라 벽면이 평면으로 남는다 - 통로가 '두 바위 사이'가 아니라 '갈라진 암벽
    # 사이'로 읽힌다.
    rocky_slab(bm, rng.sub(1), (-1.65, 1.98, -0.10), (0.78, 2.08, 2.05),
               rel=0.07, amp=0.05, freq=1.7, cuts=5, round_amt=0.16, yaw=0.045)
    rocky_slab(bm, rng.sub(2), (1.71, 2.02, 0.12), (0.84, 2.12, 1.95),
               rel=0.07, amp=0.05, freq=1.7, cuts=5, round_amt=0.16, yaw=-0.05)
    # 벽 바깥 어깨 - 두 블록이 갈라진 한 암반이었다는 느낌.
    for sx, salt in ((-1.0, 3), (1.0, 4)):
        rocky_blob(bm, rng.sub(salt), (sx * 2.40, 0.55, sx * 1.35),
                   (0.55, 0.62, 0.72), rel=0.18, amp=0.05, freq=2.0,
                   subdiv=2, exponent=3.0, yaw=rng.uniform(0, math.tau))
    # 한쪽 벽에만 얹힌 표석 - 쌍둥이로 보이지 않게 높이를 어긋나게.
    rocky_blob(bm, rng.sub(5), (-1.72, 3.88, 0.35), (0.58, 0.40, 0.72),
               rel=0.18, amp=0.05, freq=2.2, subdiv=2, exponent=3.4,
               yaw=rng.uniform(0, math.tau))
    return bm_to_obj("seaform_d_rock", bm, smooth=False)


# ── e: 오버행 바위 ────────────────────────────────────────────────────────────
def build_e(rng):
    bm = bmesh.new()
    # 밑동은 -Z 쪽으로 물러나 있고 갓이 +Z 로 튀어나온다 -> +Z 정면에 그늘이 생긴다.
    rocky_blob(bm, rng.sub(1), (0.05, 0.85, -0.62), (1.80, 0.95, 0.80),
               rel=0.12, amp=0.06, freq=1.7, subdiv=3, exponent=4.5, yaw=0.22)
    # 갓은 큐브 격자 슬랩 - 아래 모서리가 각져야 그늘 경계가 보인다.
    rocky_slab(bm, rng.sub(2), (0.00, 2.02, 0.34), (2.18, 0.62, 1.42),
               rel=0.09, amp=0.05, freq=1.6, cuts=5, round_amt=0.26,
               yaw=-0.10, tilt=-0.07)
    # 옆 버팀돌 하나 - 버섯처럼 보이지 않게 한쪽만 받친다.
    rocky_blob(bm, rng.sub(3), (-1.52, 0.70, 0.05), (0.58, 0.80, 0.62),
               rel=0.16, amp=0.05, freq=2.0, subdiv=2, exponent=3.0,
               yaw=rng.uniform(0, math.tau))
    return bm_to_obj("seaform_e_rock", bm, smooth=True)


# ── f: 균열 암반 판 ───────────────────────────────────────────────────────────
def _crack_path(t):
    """판을 가로지르는 구불구불한 균열 중심선(XZ 평면)."""
    return Vector((-4.2 + 8.4 * t, 0.72, 0.95 * math.sin(t * 5.2 + 0.6) - 0.15))


def build_f(rng):
    bm = bmesh.new()
    # 큐브 격자 판. rel 0.13 이 7m 짜리 긴 축만 크게 흔들어 윤곽이 타원(아이코스피어판의
    # '매트리스')이 되지 않게 한다 - 비례 변위라 두께 1m 는 안 흔들린다.
    rocky_slab(bm, rng.sub(1), (0.0, 0.40, 0.0), (3.42, 0.46, 2.40),
               rel=0.16, amp=0.05, freq=1.5, detail_freq=6.5,
               cuts=8, round_amt=0.24, yaw=0.06)
    slab = bm_to_obj("seaform_f_rock", bm, smooth=False)

    # 균열 커터: 세로로 서고 수평으로 얇은 블레이드. side_vec=+Y 라 r_side 가 세로 반경,
    # r_up 이 수평 반두께가 된다. 아래 끝을 y=0.17 에 두어 판을 완전히 쪼개지 않는다
    # (완전히 쪼개면 두 조각이 되어 '균열'이 아니라 '판 두 장'이 된다).
    cb = bmesh.new()
    sweep(cb, rng.sub(2), _crack_path, 0.0, 1.0, rings=28, sides=10,
          radius_fn=lambda t: (0.55, 0.19 + 0.07 * math.sin(t * 9.1)),
          side_vec=(0, 1, 0), amp=0.05, freq=2.6)
    blade = bm_to_obj("f_crack", cb, smooth=False)
    boolean_diff(slab, blade)

    # 판 위 낮은 암맥 융기 - 넓은 판이 밋밋해 보이지 않게. X 로 길게 누운 능선이라
    # 층리 방향이 읽힌다(둥근 혹은 '단추'처럼 보였다).
    ab = bmesh.new()
    for i in range(4):
        rocky_slab(ab, rng.sub(30 + i),
                   (rng.uniform(-2.3, 2.3), 0.66, rng.uniform(-1.6, 1.6)),
                   (rng.uniform(0.85, 1.30), rng.uniform(0.26, 0.34),
                    rng.uniform(0.22, 0.38)),
                   rel=0.16, amp=0.04, freq=2.4, cuts=3, round_amt=0.28,
                   yaw=rng.uniform(-0.5, 0.5))
    extra = bm_to_obj("f_extra", ab, smooth=False)
    return mg.join_objects([slab, extra], name="seaform_f_rock")


# ── g: 탑 바위 ────────────────────────────────────────────────────────────────
def build_g(rng):
    bm = bmesh.new()
    lean_x, lean_z = rng.uniform(-0.45, 0.45), rng.uniform(-0.4, 0.4)

    def path(t):
        # 살짝 S 자로 휘어 오르는 첨탑. 아래 끝은 y<0 (ground_cut 으로 자른다).
        s = math.sin(t * 2.4)
        return Vector((lean_x * _pw(t, 1.8) + 0.22 * s, -0.30 + 6.25 * t,
                       lean_z * _pw(t, 1.8) + 0.16 * s))

    def rad_fn(t):
        # 단조 감소하는 매끈한 원뿔은 '마녀 모자'로 보인다(첫 렌더). 파형을 얹어 굵기가
        # 오르내리게 하고 꼭대기를 뭉툭하게(0.15m) 남겨 부서진 첨탑으로 읽히게 한다.
        r = 0.92 * _pw(1.0 - t, 0.72) + 0.15
        r *= 1.0 + 0.21 * math.sin(7.1 * t + 1.3) + 0.11 * math.sin(13.7 * t + 0.4)
        return r * 0.87, r

    sweep(bm, rng.sub(1), path, 0.0, 1.0, rings=24, sides=12,
          radius_fn=rad_fn, side_vec=(0, 0, 1), amp=0.13, freq=1.7)
    # 중턱 선반 3개 + 밑동 치마 - 매끈한 원뿔이 아니라 부서진 첨탑으로 읽히게.
    for i, (y, r) in enumerate(((1.55, 0.62), (2.95, 0.46), (4.15, 0.32))):
        a = rng.uniform(0, math.tau)
        rocky_blob(bm, rng.sub(10 + i),
                   (math.cos(a) * r * 1.0, y, math.sin(a) * r * 1.0),
                   (r * 1.15, r * 0.50, r * 1.0), rel=0.20, amp=0.05, freq=2.4,
                   subdiv=2, exponent=3.2, yaw=a)
    rocky_blob(bm, rng.sub(3), (0.0, 0.22, 0.0), (1.05, 0.45, 0.95),
               rel=0.18, amp=0.06, freq=2.0, subdiv=2, exponent=3.0,
               yaw=rng.uniform(0, math.tau))
    return bm_to_obj("seaform_g_rock", bm, smooth=False)


# ── h: 잔해 더미 ──────────────────────────────────────────────────────────────
def build_h(rng):
    bm = bmesh.new()
    n = 15
    for i in range(n):
        u = i / (n - 1.0)                       # 0 = 바깥 바닥, 1 = 꼭대기
        # 완전 난수 각도는 한쪽에 뭉쳐 폭이 3.1m 로 줄었다(실측). 황금각으로 고르게 흩는다.
        ang = math.tau * ((i * 0.618034) % 1.0) + rng.uniform(-0.22, 0.22)
        rad = 2.15 * _pw(1.0 - u, 0.55) * rng.uniform(0.62, 1.05)
        y = 0.30 + 1.30 * u + rng.uniform(-0.14, 0.14)
        sc = (1.0 - 0.38 * u) * rng.uniform(0.80, 1.30)
        rocky_shard(bm, rng.sub(40 + i),
                    (math.cos(ang) * rad, y, math.sin(ang) * rad * 0.78),
                    (0.72 * sc, 0.42 * sc, 0.58 * sc), jitter=0.11)
    # 무너져 깔린 바닥 파편 - 더미 밑이 비어 보이지 않게. 낮고 좁게 둬서 '접시 위의 돌'로
    # 보이지 않게 한다(첫 렌더가 그랬다 - 받침이 파편보다 컸다).
    rocky_blob(bm, rng.sub(2), (0.0, 0.06, 0.0), (1.55, 0.26, 1.35),
               rel=0.22, amp=0.06, freq=2.1, subdiv=2, exponent=3.4,
               yaw=rng.uniform(0, math.tau))
    return bm_to_obj("seaform_h_rock", bm, smooth=False)


# ── 실측 (BVH 레이캐스트) ─────────────────────────────────────────────────────
def bvh_of(obj):
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    tree = BVHTree.FromBMesh(bm)
    bm.free()
    return tree


def _runs(flags):
    """True(빈 칸) 연속 구간 [(시작 index, 길이)] 목록."""
    out, s = [], None
    for i, f in enumerate(flags + [False]):
        if f and s is None:
            s = i
        elif not f and s is not None:
            out.append((s, i - s))
            s = None
    return out


def scan_gap_x(tree, y, z, x0, x1, step=0.02):
    """(y, z) 높이에서 +X 로 레이를 쏘아 **양쪽이 막힌 빈 구간**(= 통로/개구)의 폭을 잰다.

    바위 바깥(왼쪽/오른쪽 허공)도 빈 칸이라 그냥 최대 빈 구간을 쓰면 안 된다 - 양끝이
    실제 암체로 막힌 안쪽 구간만 개구로 인정한다.
    """
    n = int((x1 - x0) / step) + 1
    empty = []
    for i in range(n):
        x = x0 + i * step
        hit = tree.ray_cast(Vector((x, y, z)), Vector((0.0, 0.0, -1.0)))
        empty.append(hit[0] is None)
    runs = _runs(empty)
    inner = [r for r in runs if r[0] > 0 and r[0] + r[1] < n]
    if not inner:
        return 0.0, None
    s, ln = max(inner, key=lambda r: r[1])
    return ln * step, x0 + (s + ln * 0.5) * step


def scan_open_height(tree, x, z0, ymax=6.0, step=0.02):
    """x 열에서 바닥(y=0)부터 위로 올라가며 -Z 레이가 뚫리는 높이 = 개구 높이."""
    n = int(ymax / step)
    h = 0.0
    for i in range(n):
        y = 0.02 + i * step
        hit = tree.ray_cast(Vector((x, y, z0)), Vector((0.0, 0.0, -1.0)))
        if hit[0] is not None:
            break
        h = y
    return h


def vertex_gap(obj, y_lo, y_hi):
    """정점 좌표만으로 재는 개구 폭 - 레이캐스트와 서로 대조하기 위한 독립 측정."""
    left = [v.co.x for v in obj.data.vertices if y_lo <= v.co.y <= y_hi and v.co.x < 0]
    right = [v.co.x for v in obj.data.vertices if y_lo <= v.co.y <= y_hi and v.co.x > 0]
    if not left or not right:
        return None
    return min(right) - max(left)


def measure_arch(obj, tree, name):
    lo, hi = mg.bbox(obj)
    z0 = hi.z + 1.0
    w, cx = scan_gap_x(tree, y=1.20, z=z0, x0=lo.x - 0.3, x1=hi.x + 0.3)
    h = scan_open_height(tree, x=(cx if cx is not None else 0.0), z0=z0)
    vg = vertex_gap(obj, 1.05, 1.35)
    # 통과 확인: 개구 중심에서 -Z 로 쏜 레이가 반대편까지 아무것도 안 맞아야 한다.
    thru = tree.ray_cast(Vector((cx or 0.0, 1.2, z0)), Vector((0, 0, -1)))[0] is None
    print(f"[{name}] 개구 실측: 폭(y=1.20m) {w:.2f}m / 높이(x={cx:.2f}) {h:.2f}m / "
          f"정점 실측 폭 {vg:.2f}m / 관통 {thru}")
    if not thru or w < 2.5 or h < 2.5:
        raise mg.ContractError(f"{name}: 아치 개구가 기준(2.5m) 미달 (w {w:.2f}, h {h:.2f}, 관통 {thru})")
    return {"opening_w": w, "opening_h": h, "vertex_w": vg, "through": thru}


def measure_passage(obj, tree, name):
    lo, hi = mg.bbox(obj)
    z0 = hi.z + 1.0
    samples = []
    for y in (0.4, 1.0, 1.6, 2.2, 2.8, 3.4):
        w, cx = scan_gap_x(tree, y=y, z=z0, x0=lo.x - 0.3, x1=hi.x + 0.3)
        samples.append((y, w, cx))
    widths = [w for _, w, _ in samples if w > 0]
    vg = vertex_gap(obj, 0.5, 3.5)
    thru = tree.ray_cast(Vector((0.0, 1.5, z0)), Vector((0, 0, -1)))[0] is None
    detail = " ".join(f"y{y:.1f}={w:.2f}" for y, w, _ in samples)
    print(f"[{name}] 통로 실측: {detail} / 최소 {min(widths):.2f}m / "
          f"정점 실측 폭 {vg:.2f}m / 관통 {thru}")
    if not thru or min(widths) < 1.5:
        raise mg.ContractError(f"{name}: 통로가 1.5m 미만이거나 막혔다 (min {min(widths):.2f}, 관통 {thru})")
    return {"passage_min": min(widths), "passage_max": max(widths),
            "vertex_w": vg, "through": thru}


def measure_overhang(obj, tree, name, step=0.06):
    """위로 쏜 레이가 **아래를 보는 면**(천장)에 맞으면 그 바닥점은 '그늘'이다.

    그늘 칸의 z 범위 = 오버행 깊이, 천장 높이 = 물고기가 들어갈 수 있는 여유.
    """
    lo, hi = mg.bbox(obj)
    cells = []
    x = lo.x
    while x <= hi.x:
        z = lo.z
        while z <= hi.z:
            up = tree.ray_cast(Vector((x, 0.02, z)), Vector((0, 1, 0)))
            if up[0] is not None and up[1].y < -0.2:
                cells.append((x, z, up[0].y))
            z += step
        x += step
    if not cells:
        raise mg.ContractError(f"{name}: 오버행 아래에 그늘 공간이 없다")
    zs = [c[1] for c in cells]
    depth = max(zs) - min(zs)
    front = max(zs)
    ceil_front = max(c[2] for c in cells if abs(c[1] - front) < step * 1.5)
    area = len(cells) * step * step
    # 정면(+Z) 언더컷: 갓 앞끝(hi.z)에서 밑동 앞면까지의 수평 거리 = 물고기가 파고드는 깊이.
    fb = tree.ray_cast(Vector((0.0, 0.35, hi.z + 1.0)), Vector((0, 0, -1)))
    base_z = fb[0].z if fb[0] is not None else lo.z
    undercut = hi.z - base_z
    print(f"[{name}] 오버행 실측: 그늘 면적 {area:.2f}m^2 / 그늘 Z 범위 {depth:.2f}m / "
          f"정면 언더컷 {undercut:.2f}m / 바깥끝 천장고 {ceil_front:.2f}m / 갓 앞끝 z {hi.z:.2f}m")
    if depth < 0.8 or ceil_front < 0.6:
        raise mg.ContractError(f"{name}: 오버행 그늘이 얕다 (깊이 {depth:.2f}, 천장고 {ceil_front:.2f})")
    return {"shade_area": area, "shade_depth": depth, "undercut": undercut,
            "ceiling": ceil_front}


# (이름, 시드, 빌더, 예산키, UV 타일, 실측 함수, 한 줄 설명)
SPECS = [
    ("seaform_a", 74001, build_a, "large_structure", 2.2, measure_arch, "arch 6m span"),
    ("seaform_b", 74002, build_b, "small_prop", 1.4, None, "pillar grove"),
    ("seaform_c", 74003, build_c, "large_structure", 2.0, None, "stepped ridge 8m"),
    ("seaform_d", 74004, build_d, "small_prop", 1.8, measure_passage, "canyon pair"),
    ("seaform_e", 74005, build_e, "small_prop", 1.8, measure_overhang, "overhang shelter"),
    ("seaform_f", 74006, build_f, "large_structure", 2.2, None, "cracked bedrock 7m"),
    ("seaform_g", 74007, build_g, "small_prop", 1.6, None, "tower spire 6m"),
    ("seaform_h", 74008, build_h, "small_prop", 1.2, None, "rubble pile"),
]


def produce(name, seed, builder, budget_key, uv_tile, measure, note):
    mg.reset_scene()
    rng = mg.Rng(seed)
    obj = builder(rng)
    obj.name = name + "_rock"
    obj.data.name = name + "_rock"
    ground_cut(obj)
    mg.triangulate(obj)
    budget = mg.TRI_BUDGET[budget_key]
    mg.decimate_to_budget(obj, budget)          # UV 를 펴기 전에(감면이 UV 를 흔들지 않게)
    mg.box_uv(obj, tile=uv_tile)
    mg.assign_material(obj, mg.preview_material("pv_" + name, base_color=(0.30, 0.33, 0.31)))
    stats = mg.enforce_contract_group([obj], tri_budget=budget, tri_floor=200,
                                      name=name, align="ground")

    out = os.path.join(mg.MODELS_DIR, name + ".obj")
    mg.export_obj(obj, out)
    stats = mg.verify_obj_file(out, stats)
    mg.inject_usemtl(out)                       # export -> verify 통과 후에만(호출 순서 계약)

    extra = {}
    if measure is not None:
        extra = measure(obj, bvh_of(obj), name)

    # 미리보기 렌더는 **전용 폴더**에서 굽고 결과 PNG 만 _preview 로 옮긴다.
    # mgbuild.turntable 은 out_png 와 같은 폴더에 `_tiles` 를 만들어 4컷을 굽고 지우는데,
    # 다른 unit 스크립트를 동시에 돌리면 그 폴더가 겹쳐 서로의 타일을 지운다
    # (실제로 rockform.py 와 동시 실행 중 tile2.png 가 사라져 합성이 죽었다).
    stage = os.path.join(mg.PREVIEW_DIR, "_seaform_render")
    os.makedirs(stage, exist_ok=True)
    tmp_png = os.path.join(stage, name + ".png")
    mg.turntable(obj, tmp_png, title=name, stats=stats, px=380, samples=18,
                 notes=f"seed {seed} / {note}")
    shutil.move(tmp_png, os.path.join(mg.PREVIEW_DIR, name + ".png"))
    if not os.listdir(stage):          # turntable 이 _tiles 를 지운 뒤라 보통 비어 있다
        os.rmdir(stage)
    mg.report(stats)
    return stats, extra


def main():
    rows = []
    for name, seed, builder, budget_key, uv_tile, measure, note in SPECS:
        stats, extra = produce(name, seed, builder, budget_key, uv_tile, measure, note)
        rows.append((name, seed, stats, extra))

    print("SEAFORM_MANIFEST")
    for name, seed, stats, extra in rows:
        s = stats["size"]
        print(f"  {name}  {s[0]:.2f} x {s[1]:.2f} x {s[2]:.2f} m  "
              f"{stats['tris']}/{stats['budget']}tri  seed {seed}"
              + (("  " + " ".join(f"{k}={v if not isinstance(v, float) else round(v, 2)}"
                                  for k, v in extra.items())) if extra else ""))
    print(f"[seaform] 완료 - {len(rows)}종")


if __name__ == "__main__":
    main()
