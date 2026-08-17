#!/usr/bin/env python3
"""
석(石) 계열 확장 4계열 7종 - 거석 / 잔해 / 겹바위 / 절벽 (2026-08-17).

    python3 Tools/blender/units/rockforms.py

산출물 (전부 **신규 파일** - 기존 rock_a~e 는 한 바이트도 건드리지 않는다)
  Assets/_Project/Resources/Models/rock_mega_a.obj    거석: 둥근 거암   (H 4.1m)
  Assets/_Project/Resources/Models/rock_mega_b.obj    거석: 모난 덩어리 (H 5.2m)
  Assets/_Project/Resources/Models/rock_rubble_a.obj  잔해: 흩어진 판형 조각 8개
  Assets/_Project/Resources/Models/rock_rubble_b.obj  잔해: 무더기형 조각 7개
  Assets/_Project/Resources/Models/rock_stack_a.obj   겹바위: 3덩이 퍼치드 스택
  Assets/_Project/Resources/Models/rock_cliff_a.obj   절벽: 일자 단애 (W 8.5m)
  Assets/_Project/Resources/Models/rock_cliff_b.obj   절벽: 凹 굽은 단애 (W 9.5m)
  Tools/blender/_preview/rock_<이름>.png              렌더 - 저장소에 넣지 않는다

  ★ 기존 units/rock.py 의 빌더는 **호출하지 않고 형태 헬퍼만 import** 한다
    (_surface_radius / _slab_cut / _cut, 그리고 STONE 색·UV_TILE 상수).
    rock.py 의 난수 소비 경로를 전혀 건드리지 않으므로 rock_a~e 는 재실행해도 그대로다.

왜 이 일곱인가 (사용자 요청: "사람보다 큰 바위 / 깨진 잔해 / 바위 위 바위 / 절벽")
  기존 rock_a~e 는 전부 폭 1.85~3.20 / 높이 0.95~3.20 의 "걸어 지나치는 노두"다.
  이 배치는 **스케일 축과 구성 축**을 연다:
    mega   - 플레이어 키 2m 를 명확히 넘는 4.1 / 5.2m. 밑동이 넓고 위에 평평한 자리가 있다
             (다음 배치에서 콜라이더가 붙으면 올라설 수 있다).
    rubble - 높이 0.45 / 0.75m 의 낮은 군집. 조각 7~8개를 **한 메시로 합쳐** 드로우콜 1이다.
    stack  - 큰 바위 위에 중간·작은 바위가 얹힌 3덩이(perched rock). 한 메시.
    cliff  - 앞면(+Z)이 수직 단애 + 층리, 뒷면은 완만한 경사(지형에 파묻는 쪽).

절벽만의 원점 예외 (mgbuild.enforce_contract 의 sink=0.5 확장)
  원점은 여전히 **접지 기준 y=0** 이지만, 메시가 y=-0.5 까지 내려간다.
  경사면 위에 얹을 때 앞모서리가 떠 보이지 않게 하는 지면 아래 여유분이다.
  배치 코드는 지표면 높이에 그대로 놓으면 된다(보정 오프셋 불필요). 나머지 6종은 밑면 y=0.

크기의 근거
  플레이어 CharacterController height = 2.0m (렌더의 2m 기준 막대와 동일).
  기존 바위 연결부 TryGetRockModel(IslandMeshGenerator.MeshLibrary.cs:204)은 목표 폭에
  가장 가까운 변종을 고른다 - 이 일곱은 그 목록에 넣지 말고 **별도 배치 경로**로 쓴다
  (mega 를 그 목록에 넣으면 일반 바위 자리에 4m 거석이 꽂힌다). 권장 배치 규칙은
  이 배치의 보고 명세표에 있다.

시드는 아래 표에 박아 둔다. 같은 시드 = 같은 메시 = 같은 md5 (2회 실행 대조함).
"""

import math
import os
import sys

_UNITS_DIR = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.dirname(_UNITS_DIR))
sys.path.insert(0, _UNITS_DIR)

import mgbuild as mg  # noqa: E402  (bpy 를 먼저 끌어온다)
import rock  # noqa: E402  (형태 헬퍼/상수 재사용 - 빌더는 호출하지 않는다)
import bmesh  # noqa: E402
from mathutils import Matrix, Vector  # noqa: E402

UV_TILE = rock.UV_TILE          # 1.15m - 기존 바위와 같은 rock.png 박스 UV
STONE = rock.STONE              # WeatheredStone - 기존 바위와 같은 색 계열
CLIFF_SINK = 0.5                # 절벽 밑동이 지면 아래로 내려가는 깊이(m)

# 이름, 시드, (W, H, D)  ※ H 는 bbox 전체 - 절벽은 지상 높이 + CLIFF_SINK 다.
MEGA = [
    ("rock_mega_a", 30817, (3.60, 4.10, 3.30), "round"),   # 둥근 거암
    ("rock_mega_b", 31259, (3.20, 5.20, 2.90), "block"),   # 모난 덩어리
]
RUBBLE = [
    ("rock_rubble_a", 40111, (2.80, 0.45, 2.40), "scatter"),  # 흩어진 판형
    ("rock_rubble_b", 40733, (2.20, 0.75, 1.95), "pile"),     # 무더기형
]
STACK = [
    ("rock_stack_a", 50423, (2.60, 3.30, 2.35)),
]
CLIFF = [
    ("rock_cliff_a", 60817, (8.50, 5.90, 4.20), False),    # 일자 벽 (지상 5.4m)
    ("rock_cliff_b", 61931, (9.50, 6.70, 4.60), True),     # 凹 굽은 벽 (지상 6.2m)
]

BUDGET = {"mega": 6000, "rubble": 2500, "stack": 5000, "cliff": 8000}
TARGET = {"mega": 5300, "rubble": 2250, "stack": 4500, "cliff": 7100}


# ────────────────────────────────────────────────────────────────────────────
# 거석 - 4m 급. rock.build_rock 의 형태 어휘(로브→층리→플레어→절단)를 큰 스케일로.
# ────────────────────────────────────────────────────────────────────────────
def build_mega(seed, style):
    rng = mg.Rng(seed)
    bm = bmesh.new()
    bmesh.ops.create_icosphere(bm, subdivisions=7, radius=1.0)

    # round 는 로브가 크고 절단이 얕다(부푼 거암). block 은 로브를 죽이고 절단이 깊다(각진 덩어리).
    if style == "round":
        lobe_amp, lobe_n, y_damp = (0.26, 0.48), 6, 0.42
    else:
        # 1차 렌더에서 mega_b 가 매끈한 기둥으로 나왔다 - 로브를 더 죽여 절단면이 지배하게 한다.
        lobe_amp, lobe_n, y_damp = (0.10, 0.24), 4, 0.35
    lobes = []
    for _ in range(lobe_n):
        axis = rng.unit_vector()
        axis.y *= y_damp
        axis.normalize()
        lobes.append((axis, rng.uniform(*lobe_amp), rng.uniform(1.1, 2.0)))
    for v in bm.verts:
        d = v.co.normalized()
        v.co = d * rock._surface_radius(d, lobes, seed)

    ys = [v.co.y for v in bm.verts]
    ymin, ymax = min(ys), max(ys)
    height = ymax - ymin

    # 층리 + 테이퍼 + 밑동 플레어. 거석은 층 수를 5로 늘린다 - 4m 덩어리에 3층이면
    # 층 하나가 1.3m 라 "층리"가 아니라 "이상한 굴곡"으로 읽힌다.
    terraces = 5
    terr_amp = rng.uniform(0.09, 0.13)
    # 밑동이 넓어야 안정감이 선다(요구사항). 1차 렌더에서 mega_b 의 밑동이 허리보다
    # 좁게 나와(수직 절단이 플레어를 깎아 먹었다) 넘어질 것처럼 보였다 - 플레어를 키우고
    # 아래(블록 스타일 절단 부분)에서 깊은 절단의 법선을 위로 틀어 밑동을 지나가지 않게 한다.
    flare = rng.uniform(0.36, 0.44) if style == "block" else rng.uniform(0.30, 0.38)
    taper = rng.uniform(0.18, 0.26)
    twist = rng.uniform(0.0, math.tau)
    strata_dir = rng.uniform(0.0, math.tau)
    for v in bm.verts:
        t = (v.co.y - ymin) / height
        around = math.atan2(v.co.z, v.co.x)
        strata = 0.15 + 0.85 * (0.5 + 0.5 * math.cos(around - strata_dir)) ** 1.6
        band = t * terraces + 0.12 * math.sin(t * 7.3 + twist)
        phase = band % 1.0
        lead = min(1.0, phase / 0.12)
        f = 1.0 + terr_amp * strata * lead * (1.0 - phase) ** 0.45
        f += terr_amp * 0.5 * math.sin(around * 2.0 + twist) * (1.0 - phase)
        f *= 1.0 - taper * t
        if t < 0.35:
            f *= 1.0 + flare * ((0.35 - t) / 0.35) ** 1.25
        v.co.x *= f
        v.co.z *= f

    rock._cut(bm, Vector((0.0, ymin + height * 0.10, 0.0)), Vector((0.0, -1.0, 0.0)))

    if style == "round":
        for _ in range(rng.randint(5, 7)):
            n = rng.unit_vector()
            n.y = rng.uniform(-0.20, 0.40)
            n.normalize()
            rock._slab_cut(bm, n, rng.uniform(0.88, 0.94))
    else:
        # 깊은 벽개면 4장이 "모난 덩어리"의 정체성이다. 법선을 위로 틀어(y 0.12~0.32)
        # 절단 평면이 허리~어깨를 지나고 넓은 밑동은 살아남게 한다(1차 렌더 교훈).
        for _ in range(4):
            n = rng.unit_vector()
            n.y = rng.uniform(0.12, 0.32)
            n.normalize()
            rock._slab_cut(bm, n, rng.uniform(0.76, 0.84))
        for _ in range(rng.randint(7, 9)):
            n = rng.unit_vector()
            n.y = rng.uniform(-0.25, 0.45)
            n.normalize()
            rock._slab_cut(bm, n, rng.uniform(0.84, 0.92))

    # 꼭대기 평탄면 - 거의 수평(최대 6~8도). 올라섰을 때 설 자리가 되는 요구사항.
    tilt = 0.14 if style == "round" else 0.10
    top_n = Vector((rng.uniform(-tilt, tilt), 1.0, rng.uniform(-tilt, tilt))).normalized()
    rock._slab_cut(bm, top_n, rng.uniform(0.78, 0.84))

    mg.clean_bmesh(bm, dist=2e-4)
    return bm


# ────────────────────────────────────────────────────────────────────────────
# 잔해 - 깨진 조각 7~8개를 한 메시로. 조각은 각지고(깊은 절단) 납작하다.
# ────────────────────────────────────────────────────────────────────────────
def _shard(rng, seed, flat):
    """조각 하나(반지름 ~1 정규 좌표). flat 이면 판형으로 눌린다."""
    bm = bmesh.new()
    bmesh.ops.create_icosphere(bm, subdivisions=4, radius=1.0)
    lobes = []
    for _ in range(3):
        axis = rng.unit_vector()
        lobes.append((axis, rng.uniform(0.15, 0.38), rng.uniform(1.2, 2.0)))
    for v in bm.verts:
        d = v.co.normalized()
        v.co = d * rock._surface_radius(d, lobes, seed)
    # 깊은 절단 4~6장 - "떨어져 나온 파편"은 절단면이 몸의 절반이다.
    for _ in range(rng.randint(4, 6)):
        n = rng.unit_vector()
        rock._slab_cut(bm, n, rng.uniform(0.68, 0.84))
    if flat:
        sy = rng.uniform(0.30, 0.44)
        for v in bm.verts:
            v.co.y *= sy
    mg.clean_bmesh(bm, dist=1e-4)
    return bm


def _place_shard(bm, scale, yaw, tilt_axis, tilt, pos_x, pos_z, base_y):
    """조각을 굽는다: 스케일 → 임의 회전 → 바닥 절단 없이 밑면을 base_y 에."""
    m = (Matrix.Rotation(yaw, 4, "Y") @
         Matrix.Rotation(tilt, 4, tilt_axis) @
         Matrix.Diagonal(Vector((scale[0], scale[1], scale[2], 1.0))))
    bmesh.ops.transform(bm, matrix=m, verts=bm.verts[:])
    ymin = min(v.co.y for v in bm.verts)
    bmesh.ops.transform(
        bm, matrix=Matrix.Translation(Vector((pos_x, base_y - ymin, pos_z))),
        verts=bm.verts[:])
    return bm


def build_rubble(seed, style):
    rng = mg.Rng(seed)
    parts = []

    if style == "scatter":
        # 판형 조각 8개가 고리 모양으로 흩어진다 - 가운데를 살짝 비워 "부서져 튄" 인상을 만든다.
        n_shards = 8
        for i in range(n_shards):
            srng = rng.sub(i)
            bm = _shard(srng, seed + i * 7, flat=True)
            s = srng.uniform(0.34, 0.62)
            scale = (s, s * srng.uniform(0.85, 1.1), s * srng.uniform(0.75, 1.05))
            ang = i * (math.tau / n_shards) + srng.uniform(-0.35, 0.35)
            # 1차 렌더에서 조각이 가운데로 몰려 "군집"으로 보였다 - 바깥까지 흩고,
            # 두 조각은 멀리 튄 파편으로 뺀다("부서져 흩어짐"의 신호).
            dist = srng.uniform(0.85, 1.25) if i % 4 == 3 else srng.uniform(0.40, 1.00)
            # 밑면을 1.5cm 묻는다 - 흙에 반쯤 박힌 파편. 절단면 모서리가 지면과 만나
            # 떠 보이는 것을 막는다(전체 정렬이 최저점을 y=0 으로 올린다).
            _place_shard(bm, scale, srng.uniform(0.0, math.tau), "X",
                         srng.uniform(-0.14, 0.14),
                         math.cos(ang) * dist * 1.25, math.sin(ang) * dist * 1.05,
                         -0.015)
            parts.append(mg.new_object(f"shard{i}", bm))
    else:  # pile
        # 바닥층 5개가 맞닿은 고리 + 위층 2개가 이음매에 얹힌 무더기.
        ground_tops = []
        for i in range(5):
            srng = rng.sub(i)
            bm = _shard(srng, seed + i * 7, flat=False)
            s = srng.uniform(0.36, 0.52)
            scale = (s, s * srng.uniform(0.62, 0.80), s * srng.uniform(0.82, 1.05))
            # 1차 렌더에서 5개 전부 고리로 돌아 가운데가 뚫린 도넛으로 보였다 -
            # 첫 조각을 중심에 박아 무더기(mound)로 읽히게 한다.
            if i == 0:
                px = pz = 0.0
            else:
                ang = i * (math.tau / 4) + srng.uniform(-0.25, 0.25)
                dist = srng.uniform(0.34, 0.55)
                px, pz = math.cos(ang) * dist * 1.15, math.sin(ang) * dist
            _place_shard(bm, scale, srng.uniform(0.0, math.tau), "Z",
                         srng.uniform(-0.10, 0.10), px, pz, -0.02)
            top = max(v.co.y for v in bm.verts)
            ground_tops.append((px, pz, top))
            parts.append(mg.new_object(f"shard{i}", bm))
        for j in range(2):
            srng = rng.sub(100 + j)
            bm = _shard(srng, seed + 61 + j * 7, flat=False)
            s = srng.uniform(0.28, 0.40)
            scale = (s, s * srng.uniform(0.60, 0.78), s * srng.uniform(0.85, 1.05))
            # 이웃한 바닥 조각 두 개의 이음매 위, 낮은 쪽 꼭대기보다 0.14 아래로 파묻어 얹는다.
            a = ground_tops[j * 2]
            b = ground_tops[j * 2 + 1]
            px, pz = (a[0] + b[0]) * 0.5, (a[1] + b[1]) * 0.5
            base = min(a[2], b[2]) - 0.14
            _place_shard(bm, scale, srng.uniform(0.0, math.tau), "X",
                         srng.uniform(-0.18, 0.18), px, pz, base)
            parts.append(mg.new_object(f"top{j}", bm))

    return mg.join_objects(parts, "rubble")


# ────────────────────────────────────────────────────────────────────────────
# 겹바위 - 큰 바위 위에 중간·작은 바위가 얹힌 3덩이. 접촉부는 0.2m 안팎 겹쳐서
# 실루엣이 "얹힘"으로 읽히되 틈이 벌어지지 않게 한다.
# ────────────────────────────────────────────────────────────────────────────
def _boulder(rng, seed, top_flat):
    """스택용 덩어리. 위에 다음 돌이 앉도록 top_flat 이면 꼭대기를 넓고 평평하게 깎는다."""
    bm = bmesh.new()
    bmesh.ops.create_icosphere(bm, subdivisions=5, radius=1.0)
    lobes = []
    for _ in range(4):
        axis = rng.unit_vector()
        axis.y *= 0.40
        axis.normalize()
        lobes.append((axis, rng.uniform(0.18, 0.40), rng.uniform(1.2, 2.0)))
    for v in bm.verts:
        d = v.co.normalized()
        v.co = d * rock._surface_radius(d, lobes, seed)
    ys = [v.co.y for v in bm.verts]
    ymin, ymax = min(ys), max(ys)
    rock._cut(bm, Vector((0.0, ymin + (ymax - ymin) * 0.10, 0.0)), Vector((0.0, -1.0, 0.0)))
    for _ in range(rng.randint(4, 6)):
        n = rng.unit_vector()
        n.y = rng.uniform(-0.25, 0.35)
        n.normalize()
        rock._slab_cut(bm, n, rng.uniform(0.84, 0.92))
    if top_flat:
        top_n = Vector((rng.uniform(-0.10, 0.10), 1.0, rng.uniform(-0.10, 0.10))).normalized()
        rock._slab_cut(bm, top_n, rng.uniform(0.80, 0.86))
    mg.clean_bmesh(bm, dist=1e-4)
    return bm


def build_stack(seed):
    rng = mg.Rng(seed)
    #        (W, H, D)          겹침(m)    수평 밀림 한계(m)  기울기(rad)
    # 1~2차 렌더에서 세 덩이가 축에 정렬돼 "눈사람"에 가까웠다 - 밀림과 기울기를 키워
    # 위 돌이 한쪽으로 쏠린 퍼치드 록(perched rock)으로 읽히게 한다. 겹침 0.20~0.24m 는
    # 유지한다(밀림이 커져도 접촉부 실루엣이 벌어지지 않는 안전분).
    layers = [
        ((2.50, 1.65, 2.30), 0.00, 0.00, 0.00),
        ((1.80, 1.30, 1.65), 0.24, 0.44, 0.14),
        ((1.10, 0.85, 1.00), 0.20, 0.34, 0.20),
    ]
    parts = []
    y_top = 0.0
    cx = cz = 0.0
    for i, (size, overlap, drift, tilt_max) in enumerate(layers):
        srng = rng.sub(i)
        bm = _boulder(srng, seed + i * 13, top_flat=(i < 2))
        obj = mg.new_object(f"boulder{i}", bm)
        mg.fit_size(obj, size)
        lo, hi = mg.bbox(obj)
        # 위층은 살짝 기울여 얹는다(perched). 회전 후 최저점 기준으로 다시 앉힌다.
        rot = (Matrix.Rotation(srng.uniform(0.0, math.tau), 4, "Y") @
               Matrix.Rotation(srng.uniform(-tilt_max, tilt_max), 4, "X") @
               Matrix.Rotation(srng.uniform(-tilt_max, tilt_max), 4, "Z"))
        obj.data.transform(rot)
        lo, hi = mg.bbox(obj)
        dx = cx + srng.uniform(-drift, drift)
        dz = cz + srng.uniform(-drift, drift)
        dy = y_top - overlap - lo.y
        obj.data.transform(Matrix.Translation(Vector((dx - (lo.x + hi.x) * 0.5,
                                                      dy,
                                                      dz - (lo.z + hi.z) * 0.5))))
        lo, hi = mg.bbox(obj)
        y_top = hi.y
        cx, cz = (lo.x + hi.x) * 0.5, (lo.z + hi.z) * 0.5
        parts.append(obj)
    return mg.join_objects(parts, "stack")


# ────────────────────────────────────────────────────────────────────────────
# 절벽 - 앞면(+Z) 수직 단애 + 층리, 뒷면(-Z) 완만한 경사(지형에 묻는 쪽).
# 절단으로 앞면을 만들면 평면 한 장이 되어 층리가 안 남는다 - 그래서 앞면은
# **클램프 + 밴드별 오프셋**(높이 해상도를 보존하는 변위)으로 만든다.
# ────────────────────────────────────────────────────────────────────────────
def build_cliff(seed, curved):
    rng = mg.Rng(seed)
    bm = bmesh.new()
    bmesh.ops.create_icosphere(bm, subdivisions=7, radius=1.0)

    for v in bm.verts:          # 넓고(x) 높고(y) 얕은(z) 판 비례로
        v.co.x *= 1.75
        v.co.y *= 1.15
        v.co.z *= 0.80

    lobes = []
    for _ in range(5):
        axis = rng.unit_vector()
        axis.y *= 0.30
        axis.normalize()
        lobes.append((axis, rng.uniform(0.10, 0.22), rng.uniform(1.3, 2.2)))
    for v in bm.verts:
        v.co *= rock._surface_radius(v.co.normalized(), lobes, seed)

    ys = [v.co.y for v in bm.verts]
    ymin, ymax = min(ys), max(ys)
    height = ymax - ymin
    xmax = max(abs(v.co.x) for v in bm.verts)
    zmax = max(v.co.z for v in bm.verts)
    backmax = -min(v.co.z for v in bm.verts)

    # 1차 렌더 교훈 둘: (a) 앞면 기준 평면을 0.60 → 0.50 으로 물려 뒷면 경사에 깊이를 더 주고
    # (b) 층리 밴드 진폭을 0.11 → 0.16 으로 키운다(0.11 은 cliff_a 에서 거의 안 읽혔다).
    zf0 = zmax * 0.50                       # 앞면 기준 평면
    curve_amp = zf0 * 0.55 if curved else 0.0
    nb = 7                                  # 층리 밴드 수
    salt = seed * 0.37

    def band_off(k):
        # 밴드별 돌출/후퇴(결정적). 층리 선이 여기서 나온다.
        return 0.16 * zf0 * math.sin(k * 12.9898 + salt)

    for v in bm.verts:
        t = (v.co.y - ymin) / height
        u = v.co.x / xmax
        # 凹: 가운데가 물러난다(양끝이 앞으로 감싸는 굽은 벽).
        zf = zf0 - curve_amp * (1.0 - u * u) * 0.5
        band = t * nb + 0.20 * math.sin(v.co.x * 1.3 + salt)
        k = math.floor(band)
        phase = band - k
        # 밴드 경계 12% 구간을 경사로 잇는다(계단 함수 그대로면 종이 차양이 생긴다 - rock.py 의 교훈).
        ledge = band_off(k - 1) + (band_off(k) - band_off(k - 1)) * min(1.0, phase / 0.12)
        target = zf + ledge + 0.045 * zf0 * math.sin(v.co.y * 3.1 + v.co.x * 2.3 + salt)
        if v.co.z > target:
            v.co.z = target
        # 뒷면: 바닥에서 backmax, 꼭대기에서 0.1*backmax 로 좁아지는 경사 평면 + 잔거침.
        zb = -backmax * (1.0 - 0.90 * t)
        if v.co.z < zb:
            v.co.z = zb - 0.05 * backmax * (0.5 + 0.5 * math.sin(v.co.x * 1.7 + t * 6.0 + salt))

    rock._cut(bm, Vector((0.0, ymin + height * 0.05, 0.0)), Vector((0.0, -1.0, 0.0)))

    # 꼭대기: 뒤로 낮아지는 경사 절단. 1차 렌더에서 0.82~0.88 은 둥근 "식빵 뚜껑"이 남아
    # 절벽이 아니라 큰 바위로 읽혔다 - 더 깊고(0.72~0.78) 더 눕혀(뒤 기울기 0.45~0.60) 쳐서
    # 앞 크레스트가 날카롭게 서고, 넓은 뒤 경사 상판이 뒷면과 이어져 지형에 파묻기 좋게 한다.
    top_n = Vector((rng.uniform(-0.07, 0.07), 1.0, -rng.uniform(0.45, 0.60))).normalized()
    rock._slab_cut(bm, top_n, rng.uniform(0.72, 0.78))

    # 양끝: 거의 수직 절단 - 벽이 뭉툭하게 끝나지 않고 "부러진 단면"으로 끝난다.
    for sx in (1.0, -1.0):
        n = Vector((sx, rng.uniform(-0.05, 0.12), rng.uniform(-0.25, 0.25))).normalized()
        rock._slab_cut(bm, n, rng.uniform(0.86, 0.93))

    for _ in range(rng.randint(3, 5)):      # 잔면 몇 장
        n = rng.unit_vector()
        n.y = rng.uniform(-0.10, 0.30)
        n.normalize()
        rock._slab_cut(bm, n, rng.uniform(0.90, 0.96))

    mg.clean_bmesh(bm, dist=2e-4)
    return bm


# ────────────────────────────────────────────────────────────────────────────
def _finish(obj, name, seed, size, family, note, align="bbox", sink=0.0):
    mg.decimate_to_budget(obj, TARGET[family])
    mg.fit_size(obj, size)
    mg.shade_flat(obj)
    mg.box_uv(obj, tile=UV_TILE)
    stats = mg.enforce_contract(obj, tri_budget=BUDGET[family], tri_floor=1200,
                                expect_size=size, name=name, align=align,
                                ground_band=0.05 if align == "ground" else None,
                                sink=sink)
    obj_path = os.path.join(mg.MODELS_DIR, f"{name}.obj")
    mg.export_obj(obj, obj_path)
    stats = mg.verify_obj_file(obj_path, stats)
    mg.assign_material(obj, mg.preview_material(
        f"prev_{name}", texture_name="rock", base_color=STONE, roughness=0.82))
    mg.turntable(obj, os.path.join(mg.PREVIEW_DIR, f"{name}.png"),
                 title=f"{name}   seed {seed}", stats=stats, notes=note)
    mg.report(stats)
    return stats


def main():
    print("[rockforms] 석 계열 7종 생성 (mega/rubble/stack/cliff)")
    all_stats = []

    for name, seed, size, style in MEGA:
        mg.reset_scene()
        obj = mg.new_object(name, build_mega(seed, style))
        all_stats.append(_finish(obj, name, seed, size, "mega",
                                 f"{style} / box UV {UV_TILE:.2f}m"))

    for name, seed, size, style in RUBBLE:
        mg.reset_scene()
        obj = build_rubble(seed, style)
        obj.name = name
        all_stats.append(_finish(obj, name, seed, size, "rubble",
                                 f"{style} / one mesh / box UV {UV_TILE:.2f}m",
                                 align="ground"))

    for name, seed, size in STACK:
        mg.reset_scene()
        obj = build_stack(seed)
        obj.name = name
        all_stats.append(_finish(obj, name, seed, size, "stack",
                                 f"3 boulders / one mesh / box UV {UV_TILE:.2f}m",
                                 align="ground"))

    for name, seed, size, curved in CLIFF:
        mg.reset_scene()
        obj = mg.new_object(name, build_cliff(seed, curved))
        all_stats.append(_finish(obj, name, seed, size, "cliff",
                                 f"{'curved' if curved else 'straight'} / sink {CLIFF_SINK}m "
                                 f"/ box UV {UV_TILE:.2f}m",
                                 sink=CLIFF_SINK))

    print("[rockforms] 완료 - 렌더: Tools/blender/_preview/rock_*.png")
    return all_stats


if __name__ == "__main__":
    main()
