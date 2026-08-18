#!/usr/bin/env python3
"""
cave_a/cave_b - 수중 동굴 바위 2종 (2026-08-18 열대 해저, 수심 8~15m 배치).

    python3 Tools/blender/units/cave.py

산출물: Assets/_Project/Resources/Models/cave_a.obj / cave_b.obj (+.mtl)
        Tools/blender/_preview/cave_a.png / cave_a_interior.png / cave_b.png / cave_b_interior.png

설계:
  cave_a  돔형. 외경 ~11x6x10m 불규칙 바위 돔, 내부 공동 ~8x4.5x7m, +Z 입구 아치 1개
          (폭 ~3m x 높이 ~2.6m 이상 - 여유 있게). 내부 천장 상부에 에어포켓 돔
          (공동 천장보다 ~0.9m 더 위로 파인다). 시드 72001.
  cave_b  터널형. 반타원 아크(footprint ~14x9m)를 따라 굽은 관통 터널 길이 ~16m,
          입구 2개 모두 +Z 를 본다(주 입구 방향 규약). 내부 단면 ~3.5x3m,
          중간(t=0.5)에 폭 ~6m 방. 시드 72002.

두께(핵심): 솔리디파이 대신 **닫힌 외피 솔리드 - 내부 공동 솔리드 불리언(EXACT) 차집합**.
  결과는 안쪽 면/바깥 면이 모두 있는 진짜 두께 셸이고 불리언이 노멀을 바깥(=공동 안쪽에서는
  공동 쪽)으로 유지한다. 벽 두께는 외피/공동 치수 차로 정해진다(측벽 ~0.6-1.6m, 천장
  최소부 ~0.5-0.8m - 스크립트가 레이캐스트로 실측해 출력한다). 안팎 셸은 0.5m 이상
  떨어져 있어 remove_doubles(1e-4)가 붙일 수 없다.

바닥: 불리언 뒤 y=0 평면 바이섹트로 아래를 **버리고 열어 둔다**(바닥면 없음 - 해저 모래가
  바닥). 입구 개구부 아랫변도 y=0 이라 턱이 없다.

검증(스크립트가 전부 자동으로 한다 - 렌더만 믿지 않는다):
  1. BVH 레이캐스트로 내부 공동 실측: 공동 중앙에서 위(천장 높이·노멀 방향), 아래(개방 확인).
  2. 입구 개구부 실측: 입구 앞에서 -Z 평행 레이 그리드를 쏘아 "통과" 셀 집합의 폭/높이.
  3. 셸 두께 샘플링: 내부에서 방사 레이 2회 캐스트(안쪽면 -> 바깥면 거리) min/중앙값.
  4. 렌더: mg.turntable(외형 4컷) + 입구 정면/내부/단면 3컷 합성(_interior.png).

o 오브젝트 1개(shell) = 런타임 머티리얼 1장. 시드 고정 = 같은 md5.
"""

import math
import os
import sys

_UNITS_DIR = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.dirname(_UNITS_DIR))

import mgbuild as mg  # noqa: E402
import bmesh  # noqa: E402
import bpy  # noqa: E402
from mathutils import Matrix, Vector, noise  # noqa: E402
from mathutils.bvhtree import BVHTree  # noqa: E402

SEED_A = 72001
SEED_B = 72002


# ── 노이즈/지오메트리 소도구 ──────────────────────────────────────────────────
def fbm(p, off, freq=1.0, octaves=4, gain=0.5, lac=2.07):
    """다옥타브 펄린. mathutils.noise 는 순열표가 고정이라 완전히 결정적이다."""
    total, amp, f = 0.0, 1.0, freq
    for _ in range(octaves):
        total += amp * noise.noise((p + off) * f)
        amp *= gain
        f *= lac
    return total


def rocky_ellipsoid(name, rng, center, semi, amp, freq, subdiv=3):
    """노이즈 변위 준 아이코스피어 타원체(닫힌 솔리드). amp 는 미터 단위 변위 진폭."""
    bm = bmesh.new()
    res = bmesh.ops.create_icosphere(bm, subdivisions=subdiv, radius=1.0)
    off = Vector((rng.uniform(0.0, 40.0), rng.uniform(0.0, 40.0), rng.uniform(0.0, 40.0)))
    for v in res["verts"]:
        n = v.co.normalized()
        d = amp * fbm(n, off, freq)
        v.co = Vector((n.x * (semi[0] + d), n.y * (semi[1] + d), n.z * (semi[2] + d)))
        v.co += Vector(center)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
    return mg.new_object(name, bm)


def swept_solid(name, rng, path_fn, t0, t1, rings, sides, half_w, cy, semi_v, amp, freq):
    """경로를 따라 쓸어낸 닫힌 타원 단면 튜브(양끝 캡 = 닫힌 솔리드).

    half_w(t)  단면 반폭(경로 수직 수평 방향)
    cy(t)      단면 중심 높이 / semi_v(t) 세로 반경 - 바닥은 y=0 아래로 내려 보낸다
               (나중에 바이섹트로 잘라 열기 위해).
    amp        노이즈 변위 진폭(미터, 단면 방사 방향).
    """
    bm = bmesh.new()
    off = Vector((rng.uniform(0.0, 40.0), rng.uniform(0.0, 40.0), rng.uniform(0.0, 40.0)))
    loops = []
    for k in range(rings):
        t = t0 + (t1 - t0) * k / (rings - 1)
        c = path_fn(t)
        tan = (path_fn(t + 1e-4) - path_fn(t - 1e-4)).normalized()
        side = Vector((tan.z, 0.0, -tan.x)).normalized()
        w, h, y0 = half_w(t), semi_v(t), cy(t)
        loop = []
        for i in range(sides):
            a = math.tau * i / sides
            radial = side * math.cos(a) + Vector((0, 1, 0)) * math.sin(a)
            p = c + side * (w * math.cos(a)) + Vector((0, y0 + h * math.sin(a), 0))
            p += radial * (amp * fbm(p * 0.22, off, freq))
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
    return mg.new_object(name, bm)


def arch_cutter(name, rng, half_w, half_h, center, z0, z1, flare=1.12, segs=18):
    """입구 아치 커터: Z 방향 타원 기둥. 바깥 끝(z1)이 flare 배 넓다(침식 느낌).
    타원 하단이 y<0 까지 내려가 바이섹트 후 개구부 아랫변이 정확히 y=0 이 된다."""
    bm = bmesh.new()
    off = Vector((rng.uniform(0.0, 40.0), rng.uniform(0.0, 40.0), rng.uniform(0.0, 40.0)))
    loops = []
    for z, s in ((z0, 1.0), (z0 + (z1 - z0) * 0.55, 1.0 + (flare - 1.0) * 0.4), (z1, flare)):
        loop = []
        for i in range(segs):
            a = math.tau * i / segs
            x = math.cos(a) * half_w * s
            y = math.sin(a) * half_h * s
            p = Vector((center[0] + x, center[1] + y, z))
            n = Vector((math.cos(a), math.sin(a), 0.0))
            p += n * (0.14 * fbm(p * 0.5, off, 1.6, octaves=3))
            loop.append(bm.verts.new(p))
        loops.append(loop)
    faces = []
    for lo, hi in zip(loops, loops[1:]):
        for i in range(segs):
            j = (i + 1) % segs
            faces.append(bm.faces.new((lo[i], lo[j], hi[j], hi[i])))
    faces.append(bm.faces.new(loops[0]))
    faces.append(bm.faces.new(loops[-1]))
    bmesh.ops.recalc_face_normals(bm, faces=faces)
    return mg.new_object(name, bm)


def boolean_diff(target, cutter):
    """target -= cutter (EXACT). 커터는 소멸한다."""
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


def open_bottom(obj, y=0.0):
    """y 평면 아래를 잘라 버린다(채우지 않음 = 바닥 개방). 노멀은 건드리지 않는다."""
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    bmesh.ops.bisect_plane(bm, geom=bm.verts[:] + bm.edges[:] + bm.faces[:],
                           plane_co=(0.0, y, 0.0), plane_no=(0.0, 1.0, 0.0),
                           clear_inner=True, clear_outer=False)
    bm.to_mesh(obj.data)
    bm.free()


def cleanup_keep_normals(obj):
    """겹정점 용접 + 퇴화면 제거 + 삼각형화. recalc_face_normals 를 부르지 **않는다** -
    불리언이 만들어 둔(공동 안쪽에서 안쪽을 보는) 노멀을 보존한다. 안팎 셸 간격이
    0.5m 이상이라 dist=1e-4 용접이 셸을 붙일 수 없다."""
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    bmesh.ops.remove_doubles(bm, verts=bm.verts[:], dist=1e-4)
    bmesh.ops.dissolve_degenerate(bm, dist=1e-4, edges=bm.edges[:])
    bmesh.ops.triangulate(bm, faces=bm.faces[:])
    slivers = [f for f in bm.faces if f.calc_area() < 1e-9]
    if slivers:
        bmesh.ops.dissolve_faces(bm, faces=slivers, use_verts=True)
        bmesh.ops.triangulate(bm, faces=bm.faces[:])
    bm.to_mesh(obj.data)
    bm.free()


# ── cave_a: 돔형 ──────────────────────────────────────────────────────────────
def build_cave_a():
    rng = mg.Rng(SEED_A)
    outer = rocky_ellipsoid("shell", rng.sub(1), (0.0, 0.35, 0.0), (5.6, 5.8, 5.1),
                            amp=0.48, freq=1.4, subdiv=4)
    cavity = rocky_ellipsoid("cavity", rng.sub(2), (0.0, 0.35, -0.10), (4.10, 4.15, 3.60),
                             amp=0.26, freq=1.6, subdiv=4)
    # 에어포켓: 공동 천장(중앙 ~4.5m)보다 위로 더 파인 돔(꼭대기 ~5.3m). 공동과 겹쳐 연결된다.
    pocket = rocky_ellipsoid("pocket", rng.sub(3), (0.0, 3.78, -0.60), (2.30, 1.50, 2.10),
                             amp=0.10, freq=1.6, subdiv=3)
    boolean_diff(outer, cavity)
    boolean_diff(outer, pocket)
    # +Z 입구 아치: 폭 3.6 x 지상고 ~2.9 (요구 3.0x2.6 에 여유). z=1.2~7.2 로 앞벽 관통.
    arch = arch_cutter("arch", rng.sub(4), half_w=1.80, half_h=2.05, center=(0.0, 0.85),
                       z0=1.2, z1=7.2)
    boolean_diff(outer, arch)
    open_bottom(outer)
    cleanup_keep_normals(outer)
    return outer


# ── cave_b: 터널형 ────────────────────────────────────────────────────────────
RX_B, RZ_B, ZC_B = 4.5, 5.0, 2.5


def path_b(t):
    """반타원 아크. 양끝(t=0,1)이 z=ZC 에서 -Z 로 진입 = 입구 둘 다 +Z 를 본다."""
    a = math.pi * t
    return Vector((-RX_B * math.cos(a), 0.0, ZC_B - RZ_B * math.sin(a)))


def bump(t, width=0.20):
    return math.exp(-((t - 0.5) / width) ** 2)


def build_cave_b():
    rng = mg.Rng(SEED_B)
    # 외피: 끝 반폭 2.55/높이 ~3.7, 중앙(방) 반폭 3.6/높이 ~5.0. 바닥은 y<0 로 잠긴다.
    outer = swept_solid(
        "shell", rng.sub(1), path_b, 0.0, 1.0, rings=40, sides=24,
        half_w=lambda t: 2.55 + 1.05 * bump(t),
        cy=lambda t: 0.6,
        semi_v=lambda t: 3.1 + 1.3 * bump(t),
        amp=0.40, freq=0.9)
    # 공동: 단면 3.5x3(끝) -> 방 6.0 폭 x 3.4 높이(중앙). 경로를 양끝 밖(t<0, t>1)까지
    # 연장해 캡을 뚫는다 = 관통 터널. 바닥(y=1.2-1.8=-0.6)이 지면 아래라 개방 바닥이 된다.
    inner = swept_solid(
        "bore", rng.sub(2), path_b, -0.07, 1.07, rings=38, sides=18,
        # 끝(입구) 부근을 0.22m 벌려 침식된 나팔꼴 입구를 만든다.
        half_w=lambda t: (1.75 + 1.25 * bump(t)
                          + 0.22 * (math.exp(-(t / 0.12) ** 2)
                                    + math.exp(-((t - 1.0) / 0.12) ** 2))),
        cy=lambda t: 1.2,
        semi_v=lambda t: 1.80 + 0.40 * bump(t),
        amp=0.12, freq=1.3)
    boolean_diff(outer, inner)
    open_bottom(outer)
    cleanup_keep_normals(outer)
    return outer


# ── 실측 검증 (BVH 레이캐스트) ───────────────────────────────────────────────
def bvh_of(obj):
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    tree = BVHTree.FromBMesh(bm)
    bm.free()
    return tree


def scan_entrance(tree, hi, x_lo, x_hi, z_pass, step=0.08, y_max=3.8):
    """입구 앞(z=hi.z+0.8)에서 -Z 평행 레이 그리드. 첫 히트 z < z_pass 면 '입구를 통과해
    내부까지 들어간' 것으로 본다(벽/막힘이면 입구 부근 z 에서 바로 맞는다).
    반환: (y=1.0 에서의 최대 연속 개구 폭, x=개구 중심 열의 최대 개구 높이, 개구 중심 x)."""
    z0 = hi.z + 0.8
    d = Vector((0.0, 0.0, -1.0))
    nx = int((x_hi - x_lo) / step) + 1
    ny = int(y_max / step) + 1
    grid = [[False] * ny for _ in range(nx)]
    for ix in range(nx):
        x = x_lo + ix * step
        for iy in range(ny):
            y = 0.04 + iy * step
            hit = tree.ray_cast(Vector((x, y, z0)), d)
            if hit[0] is not None and hit[0].z < z_pass:
                grid[ix][iy] = True
    # y=1.0 행에서 최대 연속 폭
    iy1 = int((1.0 - 0.04) / step)
    best_w, best_c, run, run_s = 0.0, None, 0, 0
    for ix in range(nx + 1):
        if ix < nx and grid[ix][iy1]:
            if run == 0:
                run_s = ix
            run += 1
        else:
            if run * step > best_w:
                best_w = run * step
                best_c = x_lo + (run_s + run / 2.0) * step
            run = 0
    if best_c is None:
        return 0.0, 0.0, None
    # 개구 높이는 그리드가 아니라 **입구 개구부 바로 안쪽에서 수직 레이**로 실측한다 -
    # 그리드 레이는 둔덕 능선 위를 넘어가 높이를 과대평가할 수 있다(cave_b 실측으로 확인).
    up = tree.ray_cast(Vector((best_c, 0.05, hi.z - 1.1)), Vector((0.0, 1.0, 0.0)))
    h = (up[0].y if up[0] is not None else 0.0)
    return best_w, h, best_c


def probe_cavity(tree, p):
    """공동 내부 점 p 에서 위/아래 레이. 반환 (천장 높이 or None, 천장 노멀 y, 아래가 열렸나)."""
    up = tree.ray_cast(Vector(p), Vector((0, 1, 0)))
    dn = tree.ray_cast(Vector(p), Vector((0, -1, 0)))
    ceil_y = up[0].y if up[0] is not None else None
    ceil_ny = up[1].y if up[0] is not None else None
    floor_open = dn[0] is None            # 바닥이 열려 있으면 아래로는 아무것도 안 맞는다
    return ceil_y, ceil_ny, floor_open


def thickness_stats(tree, probes, seed, z_rim, n=260):
    """내부 점들에서 방사 레이 2회 캐스트: 안쪽면 히트 -> 이어서 바깥면 히트 = 셸 두께.

    z_rim: 입구는 전부 +Z 를 보므로, 첫 히트가 개구부 림 지대(z > z_rim)면 버린다 -
    열린 가장자리 쐐기에서는 두께가 정의상 0 으로 수렴해(입구 림 실측으로 확인)
    벽 결함이 아닌데도 min 을 오염시킨다."""
    rng = mg.Rng(seed)
    vals = []
    for p in probes:
        for _ in range(n):
            d = rng.unit_vector()
            if d.y < -0.15:               # 열린 바닥으로 빠지는 방향은 제외
                continue
            h1 = tree.ray_cast(Vector(p), d)
            if h1[0] is None or h1[0].z > z_rim:
                continue
            h2 = tree.ray_cast(h1[0] + d * 0.02, d)
            if h2[0] is None:
                continue
            t = h2[3] + 0.02
            if 0.03 < t < 4.0:            # 입구/에어포켓 관통 방향의 이상치는 버린다
                vals.append(t)
    vals.sort()
    if not vals:
        return None
    return vals[0], vals[len(vals) // 2], len(vals)


# ── 내부 검증 렌더 (입구 정면 / 내부 / 단면) ─────────────────────────────────
def _cam_look(cam, eye, target):
    z_cam = (Vector(eye) - Vector(target)).normalized()
    x_cam = Vector((0, 1, 0)).cross(z_cam)
    if x_cam.length < 1e-6:
        x_cam = Vector((1, 0, 0))
    x_cam.normalize()
    y_cam = z_cam.cross(x_cam)
    cam.matrix_world = Matrix((
        (x_cam.x, y_cam.x, z_cam.x, eye[0]),
        (x_cam.y, y_cam.y, z_cam.y, eye[1]),
        (x_cam.z, y_cam.z, z_cam.z, eye[2]),
        (0.0, 0.0, 0.0, 1.0)))


def interior_sheet(obj, name, views, section, out_png, px=400, samples=24):
    """입구 정면 / 내부 / 단면 3컷 합성. turntable 뒤에 부른다(월드/태양광 재사용).

    views   [(라벨, eye, target, is_ortho, ortho_scale), ...] 2개
    section ("X"|"Y", 평면 좌표, clear_outer?) - 메시 사본을 바이섹트해 렌더
    """
    from PIL import Image, ImageDraw, ImageFont

    scene = bpy.context.scene
    scene.render.resolution_x = px
    scene.render.resolution_y = px
    scene.cycles.samples = samples

    ground = mg._build_ground(24.0)

    light_data = bpy.data.lights.new("CaveInner", type="POINT")
    light_data.energy = 900.0
    light = bpy.data.objects.new("CaveInner", light_data)
    bpy.context.collection.objects.link(light)
    light.location = Vector((0.0, 2.2, 0.0))

    cam_data = bpy.data.cameras.new("CaveCam")
    cam = bpy.data.objects.new("CaveCam", cam_data)
    bpy.context.collection.objects.link(cam)
    scene.camera = cam

    tmp_dir = os.path.join(os.path.dirname(out_png), "_cavetiles")
    os.makedirs(tmp_dir, exist_ok=True)
    tiles = []

    for i, (label, eye, target, ortho, oscale) in enumerate(views):
        cam_data.type = "ORTHO" if ortho else "PERSP"
        if ortho:
            cam_data.ortho_scale = oscale
        else:
            cam_data.lens = 20.0
        _cam_look(cam, eye, target)
        tile = os.path.join(tmp_dir, f"c{i}.png")
        scene.render.filepath = tile
        bpy.ops.render.render(write_still=True)
        tiles.append((label, tile))

    # 단면: 사본을 바이섹트해 원본 대신 렌더.
    axis, coord, clear_outer = section
    cut_mesh = obj.data.copy()
    cut = bpy.data.objects.new("cave_section", cut_mesh)
    bpy.context.collection.objects.link(cut)
    if obj.data.materials:
        cut.data.materials.clear()
        cut.data.materials.append(obj.data.materials[0])
    bm = bmesh.new()
    bm.from_mesh(cut_mesh)
    no = (1.0, 0.0, 0.0) if axis == "X" else (0.0, 1.0, 0.0)
    co = (coord, 0.0, 0.0) if axis == "X" else (0.0, coord, 0.0)
    bmesh.ops.bisect_plane(bm, geom=bm.verts[:] + bm.edges[:] + bm.faces[:],
                           plane_co=co, plane_no=no,
                           clear_inner=not clear_outer, clear_outer=clear_outer)
    bm.to_mesh(cut_mesh)
    bm.free()
    obj.hide_render = True
    lo, hi = mg.bbox(obj)
    span = max(hi.x - lo.x, hi.y - lo.y, hi.z - lo.z)
    if axis == "X":
        # clear_outer=True 면 +X 쪽이 잘려 나가 -X 쪽 절반이 남는다 -> +X 에서 들여다본다.
        eye = (hi.x + 8.0, hi.y * 0.55, 0.0)
        target = (0.0, hi.y * 0.4, 0.0)
    else:
        eye = (0.0, hi.y + 10.0, 0.01)
        target = (0.0, 0.0, 0.0)
    cam_data.type = "ORTHO"
    cam_data.ortho_scale = span * 1.35
    _cam_look(cam, eye, target)
    tile = os.path.join(tmp_dir, "sec.png")
    scene.render.filepath = tile
    bpy.ops.render.render(write_still=True)
    tiles.append((f"SECTION {axis}={coord:g}", tile))
    obj.hide_render = False
    bpy.data.objects.remove(cut, do_unlink=True)
    bpy.data.meshes.remove(cut_mesh, do_unlink=True)

    font_path = "/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf"
    try:
        f_label = ImageFont.truetype(font_path, 16)
    except OSError:
        f_label = ImageFont.load_default()
    sheet = Image.new("RGB", (px * 3, px + 30), (24, 26, 30))
    draw = ImageDraw.Draw(sheet)
    draw.text((12, 5), name + " - interior check", font=f_label, fill=(235, 238, 242))
    for i, (label, tile) in enumerate(tiles):
        img = Image.open(tile).convert("RGB")
        sheet.paste(img, (i * px, 30))
        draw.rectangle([i * px + 4, 34, i * px + 10 + int(draw.textlength(label, font=f_label)),
                        56], fill=(18, 20, 24))
        draw.text((i * px + 8, 36), label, font=f_label, fill=(235, 238, 242))
        os.remove(tile)
    os.rmdir(tmp_dir)
    sheet.save(out_png)

    bpy.data.objects.remove(ground, do_unlink=True)
    bpy.data.objects.remove(light, do_unlink=True)
    bpy.data.lights.remove(light_data, do_unlink=True)
    bpy.data.objects.remove(cam, do_unlink=True)
    bpy.data.cameras.remove(cam_data, do_unlink=True)
    return out_png


# ── 조립·계약·출력 ────────────────────────────────────────────────────────────
def produce(name, builder, entrance_windows, z_pass, probes, thick_probes, views_fn):
    mg.reset_scene()
    obj = builder()
    mg.shade_flat(obj)
    tris = mg.decimate_to_budget(obj, 7800)
    cleanup_keep_normals(obj)          # 감면 잔여 슬리버 정리(노멀 보존)
    mg.box_uv(obj, tile=2.5)
    mg.assign_material(obj, mg.preview_material("pv_" + name, base_color=(0.35, 0.33, 0.30)))
    stats = mg.enforce_contract(obj, tri_budget=mg.TRI_BUDGET["large_structure"],
                                tri_floor=1000, name=name, align="bbox")

    out = os.path.join(mg.MODELS_DIR, name + ".obj")
    mg.export_obj(obj, out)
    stats = mg.verify_obj_file(out, stats)
    mg.inject_usemtl(out)              # export -> verify 통과 후에만(호출 순서 계약)

    # ── 실측 ──
    tree = bvh_of(obj)
    lo, hi = mg.bbox(obj)
    print(f"[{name}] size {stats['size'][0]:.2f} x {stats['size'][1]:.2f} x "
          f"{stats['size'][2]:.2f} m  tris {stats['tris']}/{stats['budget']}")
    entrances = []
    for label, (x_lo, x_hi) in entrance_windows:
        w, h, cx = scan_entrance(tree, hi, x_lo, x_hi, z_pass)
        entrances.append((label, w, h, cx))
        print(f"[{name}] entrance {label}: width(y=1.0) {w:.2f} m, height {h:.2f} m, "
              f"center x {cx if cx is None else round(cx, 2)}")
        if w < 2.6 or h < 2.4:
            raise mg.ContractError(f"{name}: 입구 {label} 개구부가 좁다/막혔다 (w {w}, h {h})")
    for p in probes:
        cy, ny, fo = probe_cavity(tree, p)
        print(f"[{name}] cavity probe {tuple(round(v, 2) for v in p)}: "
              f"ceiling {cy if cy is None else round(cy, 2)} m, ceil normal.y "
              f"{ny if ny is None else round(ny, 2)}, floor open {fo}")
        if cy is None or ny is None or ny > -0.1:
            raise mg.ContractError(f"{name}: 공동이 비어 있지 않거나 안쪽 노멀이 뒤집혔다 {p}")
        if not fo:
            raise mg.ContractError(f"{name}: 바닥이 막혀 있다 {p}")
    th = thickness_stats(tree, thick_probes, seed=sum(map(ord, name)) * 977,
                         z_rim=hi.z - 1.5)
    if th is None:
        raise mg.ContractError(f"{name}: 두께 샘플을 얻지 못했다")
    print(f"[{name}] shell thickness: min {th[0]:.2f} m, median {th[1]:.2f} m ({th[2]} rays)")
    if th[0] < 0.15:
        raise mg.ContractError(f"{name}: 셸이 뚫릴 만큼 얇은 곳이 있다 (min {th[0]:.3f} m)")

    mg.turntable(obj, os.path.join(mg.PREVIEW_DIR, name + ".png"),
                 title=name, stats=stats, px=400, samples=20,
                 notes=f"seed fixed / hollow shell")
    interior_sheet(obj, name, *views_fn(hi),
                   out_png=os.path.join(mg.PREVIEW_DIR, name + "_interior.png"))
    mg.report(stats)
    return stats, entrances, th


def views_a(hi):
    views = [
        ("ENTRANCE +Z", (0.0, 1.7, hi.z + 7.5), (0.0, 1.4, hi.z - 1.0), False, 0.0),
        ("INSIDE -> exit", (0.0, 1.5, -2.0), (0.0, 1.2, hi.z), False, 0.0),
    ]
    return views, ("X", 0.0, True)     # +X 절반을 잘라 -X 절반 단면을 +X 에서 본다


def views_b(hi):
    views = [
        ("ENTRANCES +Z", (0.0, 2.6, hi.z + 11.0), (0.0, 1.2, 0.0), False, 0.0),
        ("INSIDE chamber", (0.0, 1.5, -1.6), (4.0, 1.2, 2.6), False, 0.0),
    ]
    return views, ("Y", 1.5, True)     # y=1.5 위를 걷어내고 위에서 내려본다(터널 경로 확인)


def main():
    a = produce(
        "cave_a", build_cave_a,
        entrance_windows=[("front", (-3.5, 3.5))], z_pass=1.0,
        probes=[(0.0, 1.5, 0.0), (0.0, 1.5, -1.5), (1.5, 1.5, 0.5)],
        thick_probes=[(0.0, 1.5, 0.0), (0.0, 3.2, -0.6)],
        views_fn=views_a)
    b = produce(
        "cave_b", build_cave_b,
        # z_pass 1.6: 입구 벽/캡 표면은 z>=2.2 에서 맞고, 입구를 통과한 레이는 굽은 터널
        # 안쪽 벽(z<1.6)에서 맞는다 - 직선 레이가 곡선 터널을 과소평가하지 않게 한 값.
        entrance_windows=[("left(-x)", (-6.8, -2.6)), ("right(+x)", (2.6, 6.8))], z_pass=1.6,
        probes=[(0.0, 1.4, -2.4), (-3.6, 1.4, 0.3), (3.6, 1.4, 0.3)],
        thick_probes=[(0.0, 1.4, -2.4), (-4.0, 1.3, 1.2), (4.0, 1.3, 1.2)],
        views_fn=views_b)
    print("CAVE_MANIFEST")
    for stats, entrances, th in (a, b):
        s = stats["size"]
        ent = "  ".join(f"{lb} {w:.2f}x{h:.2f}m" for lb, w, h, _ in entrances)
        print(f"  {stats['name']}  {stats['tris']}tri  {s[0]:.2f}x{s[1]:.2f}x{s[2]:.2f}m  "
              f"entrances[{ent}]  wall min {th[0]:.2f}/med {th[1]:.2f}m")
    print("[cave] 완료 - 2종")


if __name__ == "__main__":
    main()
