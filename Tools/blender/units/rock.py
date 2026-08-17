#!/usr/bin/env python3
"""
바위(화강암 덩어리) 5종 - rock_a / rock_b / rock_c (+ 2026-08-17 확장: rock_d / rock_e).

    python3 Tools/blender/units/rock.py

산출물 (덮어쓰기: 이 다섯 파일만 건드린다)
  Assets/_Project/Resources/Models/rock_a.obj   (+ b, c, d, e)
  Tools/blender/_preview/rock_a.png             (+ b, c, d, e)  ← 저장소에 넣지 않는다

  ★ a/b/c 는 이미 게임에 연결돼 있다. 그 세 변종의 시드·파라미터·생성 경로는 한 글자도
    건드리지 않았고, 재실행해도 바이트 단위로 같은 파일이 나온다(md5 대조함).
    d/e 는 **별도 빌더**(build_shaped)를 쓴다 - build_rock 을 공유하다 실수로 a/b/c 의
    출력이 바뀌는 사고를 구조적으로 막기 위해서다.

변종 확장의 이유 (2026-08-17)
  게임은 목표 폭에 가장 가까운 변종을 고른다(TryGetRockModel). a/b/c 는 전부 "둥그스름한
  노두"라 시드 축으로만 갈렸다 - 실루엣 축이 없었다. d/e 가 그 축을 연다:
    rock_d = **판석**(flat slab). 폭 대비 높이 0.32 - 납작하게 누운 퇴적판.
    rock_e = **첨탑**(spire). 폭 대비 높이 1.5 - 위로 갈수록 좁아져 끝이 서는 뾰족 바위.

────────────────────────────────────────────────────────────────────────────────
크기의 근거 (게임 코드 실측 — 이번 배치에서 코드는 못 고치므로 여기에 맞춘다)

  IslandMeshGenerator.cs:444 CreateRockCluster
      mainWidth  = 1.7 ~ 3.6 m
      mainHeight = mainWidth × 0.50 ~ 0.84
      mainDepth  = mainWidth × 0.74 ~ 1.02
      → 실제 큰 덩어리 = 1.7~3.6 (W) × 0.85~3.02 (H) × 1.26~3.67 (D) m
      밑동을 높이의 22~34%(최소 0.2m)만큼 지면 아래로 묻는다.
  색·텍스처: StructureVisualBuilder.WeatheredStone(#808085, :25) + "rock" 텍스처
      (IslandMeshGenerator.cs:307-312 — 1.0 / 0.84 / 1.06+채도 세 틴트를 돌려 쓴다)

  세 변종의 (W,H,D)는 전부 위 구간 안에 있고, 비율도 코드가 뽑는 비율 범위 안이다.
  → 코드가 지금 만드는 프리미티브 덩어리를 **치수를 하나도 안 바꾸고** 대체할 수 있다.

  참고: 자원 노드 "돌조각"(IslandResourceSpawner.cs:435, GetNodeShape)은 이것과 다른
  물건이다 — Sphere scale (0.5, 0.32, 0.5) × 지터 = 0.43~0.59 × 0.27~0.40 m 의 **소품**이고
  채집 콜라이더가 그 치수에 걸려 있다. 이 바위를 거기에 꽂으면 안 된다.

형태 규칙 (프리미티브 구/큐브와 갈라지는 이유)
  1. 로브 4~6개로 저주파 실루엣을 깬다 → 정면·측면·후면의 윤곽이 서로 다르다.
  2. 수평 단차(terrace) 3~4단 → 화강암 절리처럼 층이 진다.
  3. 밑동 플레어 + 바닥 절단 → 아래가 넓고 땅에 묻힌 자세.
  4. 무작위 평면 절단 7~9회 → 평평한 벽개면. 각이 서고 구가 아니게 된다.
────────────────────────────────────────────────────────────────────────────────
"""

import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

import mgbuild as mg  # noqa: E402  (bpy 를 먼저 끌어온다 - bmesh 가 그 뒤에야 import 된다)
import bmesh  # noqa: E402
from mathutils import Vector  # noqa: E402

# 시드는 여기 박아 둔다. 같은 시드 = 같은 메시(계약 5장).
VARIANTS = [
    # 이름,     시드,   (W, H, D) 미터,        단차,   로브
    ("rock_a", 20817, (1.85, 1.20, 1.60), 3, 5),
    ("rock_b", 41163, (2.60, 1.55, 2.30), 4, 6),
    ("rock_c", 60529, (3.20, 2.35, 2.60), 4, 4),
]

# 2026-08-17 확장 변종. a/b/c 와 **다른 빌더**를 쓴다(위 헤더 참고).
# 폭은 a/b/c(1.85/2.60/3.20)와 겹치지 않게 사이를 메운다 - 게임/연결 코드가 목표 폭으로
# 변종을 고를 때 다섯 실루엣이 전부 후보가 되게.
SHAPED_VARIANTS = [
    # 이름,     시드,   (W, H, D) 미터,        모양
    ("rock_d", 81271, (2.95, 0.95, 2.45), "slab"),    # 판석: 납작하게 누운 퇴적판
    ("rock_e", 92413, (2.15, 3.20, 1.90), "spire"),   # 첨탑: 위로 좁아지는 뾰족 바위
]

TRI_BUDGET = mg.TRI_BUDGET["medium_prop"]   # 4,000 (중형 소품)
TRI_FLOOR = 1500
TRI_TARGET = 3400        # 예산 4,000 안에서 노리는 값(감면 목표)
UV_TILE = 1.15          # rock.png 한 장이 1.15m 를 덮는다
STONE = (0.502, 0.502, 0.522)   # StructureVisualBuilder.WeatheredStone


def _surface_radius(direction, lobes, seed):
    """방향만으로 결정되는 반지름. 난수 표집이 아니라 함수라서 이웃 삼각형에 틈이 안 생긴다."""
    r = 1.0
    for axis, amp, power in lobes:
        d = direction.dot(axis)
        if d > 0.0:
            r += amp * (d ** power)
    s = seed * 0.6180339
    # 잔주름: 면마다 각이 서게 하는 고주파. 진폭이 작아 실루엣은 로브가 지배한다.
    r += 0.045 * math.sin(direction.x * 6.3 + s)
    r += 0.038 * math.sin(direction.z * 7.1 + s * 1.7)
    r += 0.030 * math.sin(direction.y * 5.2 + s * 2.3)
    return r


def build_rock(seed, terraces, lobe_count):
    rng = mg.Rng(seed)

    # bmesh 의 subdivisions 는 1부터 센다: 1=20면 / 2=80 / 3=320 / 4=1,280 / 5=5,120 / 6=20,480.
    # 절단이 표면의 최대 93% 를 깎아 내므로(실측) 7 에서 시작해 마지막에 감면으로 예산을 맞춘다.
    bm = bmesh.new()
    bmesh.ops.create_icosphere(bm, subdivisions=7, radius=1.0)

    # ── 1. 로브: 큰 덩어리 몇 개가 붙어 굳은 실루엣 ────────────────────────────
    # power 를 낮추면 로브가 넓고 크게 부풀어 **실루엣 자체**가 비대칭이 된다.
    # (1차 렌더가 "감자"로 보인 이유 중 하나가 로브가 좁고 얕았던 것이다)
    lobes = []
    for _ in range(lobe_count):
        axis = rng.unit_vector()
        axis.y *= 0.45          # 위아래로 뻗은 로브는 억제한다(공처럼 부푸는 것을 막는다)
        axis.normalize()
        lobes.append((axis, rng.uniform(0.26, 0.52), rng.uniform(1.1, 2.1)))

    for v in bm.verts:
        d = v.co.normalized()
        v.co = d * _surface_radius(d, lobes, seed)

    ys = [v.co.y for v in bm.verts]
    ymin, ymax = min(ys), max(ys)
    height = ymax - ymin

    # ── 2. 수평 단차 + 밑동 플레어 + 위로 갈수록 좁아지는 테이퍼 ───────────────
    # 단차를 **한쪽에만** 준다(strata_dir 방향이 강하고 반대쪽은 거의 없다).
    # 사방에 균일하게 주면 층이 정확히 포개져 "쌓아 올린 팬케이크"가 된다 - 2차 렌더에서
    # 실제로 그렇게 보였고, 그건 바위가 아니라 인공물로 읽힌다.
    terr_amp = rng.uniform(0.11, 0.16)
    flare = rng.uniform(0.28, 0.36)
    taper = rng.uniform(0.16, 0.26)
    twist = rng.uniform(0.0, math.tau)
    strata_dir = rng.uniform(0.0, math.tau)
    for v in bm.verts:
        t = (v.co.y - ymin) / height
        around = math.atan2(v.co.z, v.co.x)
        # 층이 드러나는 정도(0.15 ~ 1.0). 반대쪽 면은 매끈해서 벽개면과 대비가 생긴다.
        strata = 0.15 + 0.85 * (0.5 + 0.5 * math.cos(around - strata_dir)) ** 1.6
        # 층 두께를 조금씩 다르게 한다(등간격이면 나사산처럼 보인다).
        band = t * terraces + 0.12 * math.sin(t * 7.3 + twist)
        phase = band % 1.0
        # 층 아랫면을 0 에서 시작해 12% 구간에 걸쳐 밀어낸다. 계단 함수를 그대로 쓰면
        # 층 경계가 **종이처럼 얇은 차양**이 되어(3차 렌더에서 옆면에 실제로 생겼다)
        # 게임에서 보면 메시가 깨진 것처럼 보인다. lead 가 그 밑면에 경사를 준다.
        lead = min(1.0, phase / 0.12)
        f = 1.0 + terr_amp * strata * lead * (1.0 - phase) ** 0.45
        # 층 경계를 기울여 놓는다(완전 수평이면 선반처럼 인공적으로 보인다).
        f += terr_amp * 0.5 * math.sin(around * 2.0 + twist) * (1.0 - phase)
        # 테이퍼: 아래가 넓고 위가 좁다 = 땅에서 솟은 노두. 구/큐브와 실루엣이 갈리는 핵심.
        f *= 1.0 - taper * t
        # 밑동 플레어: 아래 30% 를 더 바깥으로 밀어 "땅에 박힌" 자세를 만든다.
        if t < 0.30:
            f *= 1.0 + flare * ((0.30 - t) / 0.30) ** 1.25
        v.co.x *= f
        v.co.z *= f

    # ── 3. 바닥 절단 (밑면 평탄화) ────────────────────────────────────────────
    _cut(bm, Vector((0.0, ymin + height * 0.14, 0.0)), Vector((0.0, -1.0, 0.0)))

    # ── 4. 벽개면: 무작위 평면 절단 ───────────────────────────────────────────
    # 절단 깊이는 **원점 기준 support 의 비율이 아니라 그 방향 두께(hi-lo)의 비율**로 잡는다.
    # 원점 기준으로 하면 밑동 절단·플레어로 무게중심이 밀린 뒤 평면이 덩어리 한복판을 지나
    # 메시가 통째로 깎여 나간다(실측: 5,120 → 1,182 삼각형).
    #
    # 큰 벽개면 2장을 **깊게**(두께의 13~19%) 먼저 치고, 잔면을 얕게 덧친다.
    # 1차는 전부 얕은 절단(4~12%)이라 각이 거의 안 섰고, 2차는 3~4장을 26%까지 깊게 쳐서
    # 이번엔 실루엣이 통째로 깎여 **커팅한 보석**처럼 됐다. 그 사이가 여기다.
    for _ in range(2):
        n = rng.unit_vector()
        n.y = rng.uniform(-0.12, 0.22)      # 거의 수직인 벽 - 옆에서 봤을 때 직선으로 읽힌다
        n.normalize()
        _slab_cut(bm, n, rng.uniform(0.81, 0.87))

    for _ in range(rng.randint(7, 10)):
        n = rng.unit_vector()
        n.y = rng.uniform(-0.30, 0.55)
        if n.length < 1e-4:
            continue
        n.normalize()
        _slab_cut(bm, n, rng.uniform(0.85, 0.94))

    # 꼭대기 평면 하나는 반드시 넣는다 - 화강암 노두의 가장 강한 신호다.
    # 살짝 기울여서(최대 13도) 완전한 수평 뚜껑이 되지 않게 한다.
    top_n = Vector((rng.uniform(-0.24, 0.24), 1.0, rng.uniform(-0.24, 0.24))).normalized()
    _slab_cut(bm, top_n, rng.uniform(0.84, 0.90))

    # 절단면끼리 만나는 곳에서 바늘 삼각형이 나온다 - 여기서 녹인다.
    mg.clean_bmesh(bm, dist=2e-4)
    return bm


def build_shaped(seed, shape):
    """rock_d(판석)/rock_e(첨탑) 전용 빌더. build_rock 은 건드리지 않는다(a/b/c 바이트 보존).

    a/b/c 와 실루엣이 **한눈에** 갈리는 게 존재 이유다:
      slab  - 위·아래 평행 절단이 지배한다. 수평 층리가 강하고, 옆면만 벽개면이다.
              위에서 보면 다각 판이고 옆에서 보면 낮고 긴 띠다.
      spire - 높이 방향으로 늘린 뒤 **위로 갈수록 심하게 좁힌다**(taper 0.62). 꼭대기는
              평면 뚜껑 대신 가파른 절단 3장을 서로 다른 방위에서 쳐 끝이 선다.
    """
    rng = mg.Rng(seed)
    bm = bmesh.new()
    bmesh.ops.create_icosphere(bm, subdivisions=7, radius=1.0)

    # 로브: build_rock 과 같은 저주파 실루엣 파괴. 모양별로 축과 진폭을 제한한다.
    # (1차 렌더에서 spire 가 로브에 밀려 달걀이 됐다 - 첨탑은 로브를 얕게 줘야 테이퍼가 이긴다.)
    lobe_amp = (0.22, 0.44) if shape == "slab" else (0.10, 0.22)
    lobes = []
    for _ in range(5):
        axis = rng.unit_vector()
        axis.y *= 0.25 if shape == "slab" else 0.55
        axis.normalize()
        lobes.append((axis, rng.uniform(*lobe_amp), rng.uniform(1.2, 2.2)))
    for v in bm.verts:
        d = v.co.normalized()
        v.co = d * _surface_radius(d, lobes, seed)

    ys = [v.co.y for v in bm.verts]
    ymin, ymax = min(ys), max(ys)
    height = ymax - ymin

    if shape == "slab":
        # 수평 층리 3단 + 약한 테이퍼. 판석은 옆면이 층으로 갈라진 것이 정체성이다.
        # (1차 렌더에서 층이 절단면에 다 깎여 안 보였다 - 진폭 0.13 → 0.20, 층 수 2 → 3.)
        twist = rng.uniform(0.0, math.tau)
        strata_dir = rng.uniform(0.0, math.tau)
        for v in bm.verts:
            t = (v.co.y - ymin) / height
            around = math.atan2(v.co.z, v.co.x)
            strata = 0.30 + 0.70 * (0.5 + 0.5 * math.cos(around - strata_dir)) ** 1.2
            phase = (t * 3.0 + 0.10 * math.sin(t * 5.1 + twist)) % 1.0
            lead = min(1.0, phase / 0.15)
            f = 1.0 + 0.20 * strata * lead * (1.0 - phase) ** 0.5
            f *= 1.0 - 0.10 * t                      # 테이퍼는 약하게(판이지 봉이 아니다)
            v.co.x *= f
            v.co.z *= f
        # 위·아래를 평행하게 깎아 판을 만든다. 윗면은 살짝 기울인다(수평 뚜껑 금지).
        _cut(bm, Vector((0.0, ymin + height * 0.20, 0.0)), Vector((0.0, -1.0, 0.0)))
        top_n = Vector((rng.uniform(-0.14, 0.14), 1.0, rng.uniform(-0.14, 0.14))).normalized()
        _slab_cut(bm, top_n, rng.uniform(0.58, 0.64))    # 깊게 - 납작함은 여기서 나온다
        # 옆면 벽개면: 거의 수직으로 5~7장. 위에서 본 윤곽이 다각형이 된다.
        for _ in range(rng.randint(5, 7)):
            n = rng.unit_vector()
            n.y = rng.uniform(-0.08, 0.10)
            n.normalize()
            _slab_cut(bm, n, rng.uniform(0.80, 0.90))
        # 모서리 하나는 크게 떼어 낸다(깨져 나간 판 - 대칭 파괴).
        chip = rng.unit_vector(); chip.y = 0.35; chip.normalize()
        _slab_cut(bm, chip, rng.uniform(0.72, 0.78))
    else:  # spire
        lean_dir = rng.uniform(0.0, math.tau)
        lean = rng.uniform(0.10, 0.16)               # 기둥이 살짝 기울어야 자연물로 읽힌다
        for v in bm.verts:
            t = (v.co.y - ymin) / height
            f = 1.0 - 0.84 * (t ** 0.90)             # 강한 테이퍼: 첨탑의 정체성
            if t < 0.26:                             # 밑동 플레어: 땅에 박힌 자세
                f *= 1.0 + 0.34 * ((0.26 - t) / 0.26) ** 1.3
            v.co.x *= f
            v.co.z *= f
            v.co.y *= 1.70                           # 높이 방향으로 늘린다
            v.co.x += math.cos(lean_dir) * lean * v.co.y
            v.co.z += math.sin(lean_dir) * lean * v.co.y
        ys = [v.co.y for v in bm.verts]
        ymin, ymax = min(ys), max(ys)
        height = ymax - ymin
        _cut(bm, Vector((0.0, ymin + height * 0.10, 0.0)), Vector((0.0, -1.0, 0.0)))
        # 세로 벽개면: 거의 수직 절단 6~8장이 기둥 옆면에 각을 세운다.
        for _ in range(rng.randint(6, 8)):
            n = rng.unit_vector()
            n.y = rng.uniform(-0.15, 0.15)
            n.normalize()
            _slab_cut(bm, n, rng.uniform(0.84, 0.93))
        # 꼭대기: 평면 뚜껑 대신 가파른 절단 4장을 방위를 나눠 친다 → 끝이 뾰족하게 선다.
        # (1차 렌더에서 깊이 0.86~0.92 는 어깨만 스쳐 달걀이 됐고, 2차의 0.70~0.80 도
        #  능선이 남아 오벨리스크에 그쳤다 - 0.58~0.68 로 더 깊게, 두 바퀴로 나눠 친다.)
        apex0 = rng.uniform(0.0, math.tau)
        for k in range(4):
            a = apex0 + math.tau * k / 4 + rng.uniform(-0.3, 0.3)
            n = Vector((math.cos(a) * 0.95, 1.0, math.sin(a) * 0.95)).normalized()
            _slab_cut(bm, n, rng.uniform(0.58, 0.68))
        for k in range(3):                            # 두 번째 바퀴: 더 가파르게, 얕게
            a = apex0 + math.tau * (k + 0.5) / 3 + rng.uniform(-0.3, 0.3)
            n = Vector((math.cos(a) * 0.55, 1.0, math.sin(a) * 0.55)).normalized()
            _slab_cut(bm, n, rng.uniform(0.78, 0.86))

    mg.clean_bmesh(bm, dist=2e-4)
    return bm


def _slab_cut(bm, normal, depth):
    """방향 normal 의 두께에서 바깥 (1-depth) 만큼만 잘라 평평한 벽개면을 남긴다."""
    dots = [v.co.dot(normal) for v in bm.verts]
    lo, hi = min(dots), max(dots)
    if hi - lo < 1e-4:
        return
    _cut(bm, normal * (lo + (hi - lo) * depth), normal)


def _cut(bm, plane_co, plane_no):
    """plane_no 가 가리키는 쪽을 잘라내고 단면을 메운다(구멍을 남기면 뒷면이 보인다)."""
    geom = bm.verts[:] + bm.edges[:] + bm.faces[:]
    res = bmesh.ops.bisect_plane(bm, geom=geom, dist=1e-6,
                                 plane_co=plane_co, plane_no=plane_no,
                                 clear_outer=True)
    edges = [e for e in res["geom_cut"] if isinstance(e, bmesh.types.BMEdge)]
    if edges:
        bmesh.ops.edgenet_fill(bm, edges=edges)


def _finish(obj, name, seed, size, note):
    """감면 → 크기 → UV → 계약 → 내보내기 → 렌더. 다섯 변종이 똑같이 지나는 공통 꼬리."""
    mg.decimate_to_budget(obj, TRI_TARGET)   # UV 를 펴기 전에 감면한다
    mg.fit_size(obj, size)
    mg.shade_flat(obj)
    mg.box_uv(obj, tile=UV_TILE)

    stats = mg.enforce_contract(obj, tri_budget=TRI_BUDGET, tri_floor=TRI_FLOOR,
                                expect_size=size, name=name)

    obj_path = os.path.join(mg.MODELS_DIR, f"{name}.obj")
    mg.export_obj(obj, obj_path)
    stats = mg.verify_obj_file(obj_path, stats)

    mg.assign_material(obj, mg.preview_material(
        f"prev_{name}", texture_name="rock", base_color=STONE, roughness=0.82))
    mg.turntable(obj, os.path.join(mg.PREVIEW_DIR, f"{name}.png"),
                 title=f"{name}   seed {seed}",
                 stats=stats,
                 notes=note)
    mg.report(stats)
    return stats


def main():
    print("[rock] 바위 5종 생성")
    all_stats = []
    for name, seed, size, terraces, lobes in VARIANTS:
        mg.reset_scene()
        bm = build_rock(seed, terraces, lobes)
        obj = mg.new_object(name, bm)
        all_stats.append(_finish(obj, name, seed, size,
                                 "tex rock.png / box UV %.2fm" % UV_TILE))

    for name, seed, size, shape in SHAPED_VARIANTS:
        mg.reset_scene()
        bm = build_shaped(seed, shape)
        obj = mg.new_object(name, bm)
        all_stats.append(_finish(obj, name, seed, size,
                                 "%s / tex rock.png / box UV %.2fm" % (shape, UV_TILE)))

    print("[rock] 완료 - 렌더: Tools/blender/_preview/rock_*.png")
    return all_stats


if __name__ == "__main__":
    main()
