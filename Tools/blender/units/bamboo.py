#!/usr/bin/env python3
"""
대나무 8종 - bamboo_a ~ bamboo_h  (v3 · 실물 레퍼런스 재제작)

    python3 Tools/blender/units/bamboo.py

산출물 (이 파일들만 건드린다)
  Assets/_Project/Resources/Models/bamboo_a~h.obj (+ .mtl)
  Tools/blender/_preview/_bamboo/bamboo_a~h.png              ← 저장소에 넣지 않는다

════════════════════════════════════════════════════════════════════════════════
[왜 통째로 다시 만들었나 - v2 진단]

v2(bamboo_a~f)를 렌더로 보면 대나무로 안 읽히는 이유가 넷이었다.

  1. **가지가 하나도 없다.** 잎다발이 줄기 옆구리에 직접 붙어 있었다. 실물 대나무는
     잎이 줄기에 붙지 않는다 - 마디에서 가지가 뻗고 그 가지 끝에만 잎이 달린다.
     (RBG Kew GrassBase / Guadua Bamboo: "one main branch and two or more secondary
      branches emerge from each node", 굵은 가지 1개가 우세)
  2. **마디 간격이 3배 길다.** v2는 마디 4~5개에 간격 0.75~0.90 m 였다. 실물 열대
     대나무(Bambusa vulgaris)는 지름 4~10 cm 에 마디 간격 25~35 cm, 즉
     **마디 간격 / 지름 = 3~6**이다. v2는 지름 0.16~0.20 m 에 간격 0.80 m 라 비율 4~5로
     비율만은 맞았지만, 그 굵기가 애초에 4 m 대나무의 굵기가 아니었다(아래 5번).
  3. **잎이 3~4배 크다.** v2 잎날은 길이 0.60~0.90 m · 폭 5~8 cm. 실물 잎은
     **길이 7~25 cm · 폭 1~4 cm**(Kew: Bambusa bambos 7~18 cm × 10~18 mm,
     Guadua: B. vulgaris 15~25 cm × 2~4 cm). 잎이 크니 장수가 모자라 성기고,
     한 장 한 장이 야자잎처럼 보였다.
  4. **자세가 전부 같다.** a~e 는 전부 직립이고 f 만 한쪽으로 기울었다. 실물은
     직립(erect) / 끄덕임(nodding, 상단이 활처럼 숙음) / 늘어짐(pendulous)이 갈린다.
     v2 는 마디 링을 `mg.swept_tube` 의 **수평 단면**으로 만들어서, 애초에 크게 휜
     줄기를 만들 수단이 없었다(수평 링은 90도 누우면 리본으로 찌그러진다).
  그 밖에: 마디 간격이 높이에 따라 안 변한다(등간격), 죽순·죽피가 없다, 죽은 죽간이 없다.

[v3 가 바꾼 것]
  · 줄기를 **평행이송 프레임(parallel transport) 튜브**로 새로 만든다(이 파일 안의 `add_tube`).
    단면이 줄기 축에 수직이라 늘어진 자세에서도 마디 링이 안 찌그러진다. UV 도 튜브를 따라
    호 길이로 감아서 가지처럼 원점에서 멀리 떨어진 조각도 결이 바로 선다
    (mg.cylinder_uv 는 **월드 원점 기준** 각도라 가지에 쓰면 무늬가 눕는다).
  · **마디**: 링 3개(아래 0.972 / 융기 1.115 / 칼라 1.045)를 1.6 cm 안에 몰아 턱을 세우고,
    칼라 두 띠만 플랫 셰이딩으로 남겨 밝기 링을 만든다(v2에서 검증된 유일한 원거리 신호).
  · **마디 간격이 높이 따라 변한다**: 밑동 0.50배 → 중간 1.30배 → 끝 0.50배(sin 프로파일).
  · **가지 + 잎다발**: 상단 마디마다 굵은 가지 1개(4각) + 가는 가지 1개(3각), 가지 끝(과
    굵은 가지는 중간에도) 잎다발. 잎날은 길이 15~25 cm · 폭 3~5 cm 로 실물 규격.
  · **자세 3종**: erect / nodding / pendulous 를 기울기 프로파일 θ(s)=θ0+(θ1-θ0)·s^p 로 만든다.
  · **군생**: 한 오브젝트에 죽간 3~7대. 어린 죽순(죽피 계단), 마른 죽간(잎 없음·부러진 끝)도 섞는다.

════════════════════════════════════════════════════════════════════════════════
[게임 계약 - 이 숫자는 협상 대상이 아니다]

  IslandResourceSpawner.MeshLibrary.cs:271
      BambooModelHeights = { 3.349, 3.885, 4.463, 4.113, 5.070, 4.252 }   ← a~f 순서
  IslandResourceSpawner.Visuals.cs:349
      fit = clumpHeight / bambooModelHeight   (clumpHeight = 3.57~5.25 m)

  즉 **모델의 실측 높이가 위 배열과 다르면 게임이 잘못된 배율로 키운다.** C# 은 이번 락
  밖이므로 a~f 는 높이를 1 mm 도 바꾸지 않는다(각 종을 만든 뒤 균등 배율로 정확히 맞춘다).
  신규 g/h 는 C# 배열에 없어 **현재는 게임이 못 읽는다** - 등록은 디렉터에게 보고로 요청한다.
  g/h 높이는 a~f 가 비워 둔 선택 구간(3.57~3.62 / 4.61~4.91)을 메우도록 골랐다.

  ★ 채집 콜라이더(루트 캡슐 지름 0.30 m)는 이 파일과 무관하다 - 여기 메시에는 콜라이더가 없다.

원점 규약: **접지 중심**(mg.enforce_contract_group align="ground"). 죽간 밑동들의 XZ 중심이 원점.
파일 구조: OBJ 하나에 `o` 2개 = `<name>_culms` / `<name>_leaves` (**v2와 철자까지 동일**).
  로더가 이름의 culm / leaf 로 줄기·잎을 가른다(MeshLibrary.cs:112). 순서도 줄기가 먼저다.
  v2 는 .mtl 이 없어서 임포터가 서브메시 1개로 합쳐 왔고 - 그래서 **잎까지 줄기색으로** 칠해졌다
  (Visuals.cs:361 의 subMeshCount>=2 폴백이 안 걸린다). v3 는 mg.inject_usemtl 로 .mtl 을 동봉해
  서브메시 2개를 보장한다. inject 는 export→verify **뒤에** 부른다(계약 3장).
════════════════════════════════════════════════════════════════════════════════
"""

import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

import mgbuild as mg  # noqa: E402  (bpy 를 먼저 끌어온다 - bmesh 가 그 뒤에야 import 된다)
import bmesh  # noqa: E402
from mathutils import Matrix, Vector  # noqa: E402

PREVIEW_DIR = os.path.join(mg.PREVIEW_DIR, "_bamboo")

# 런타임 틴트(게임이 실제로 쓰는 색) - 미리보기용일 뿐 OBJ 에는 안 실린다.
BAMBOO_CULM = (0.706, 0.745, 0.392)    # StructureVisualBuilder.BambooCulm #B4BE64
FROND_GREEN = (0.420, 0.659, 0.247)    # StructureVisualBuilder.FrondGreen

BAMBOO_TILE = 0.30      # bamboo.png 가 줄기 축 방향으로 덮는 거리(m)
BAMBOO_WRAPS = 2.0      # 둘레 한 바퀴에 두 번

# 마디 링 3개를 이 두께 안에 몰아넣는다(m). 좁을수록 턱이 선다.
NODE_BELOW = 0.024      # 융기 아래 잘록한 곳까지
NODE_ABOVE = 0.020      # 융기 위 죽피 칼라까지
NODE_FACTORS = (0.945, 1.195, 1.075)    # 아래 / 융기 / 칼라
# ★ 실물 마디 융기는 지름의 5~10% 인데 여기서는 **25%** 로 과장한다. 1차 렌더에서 1.115 는
#   4 m 짜리 죽간에서 한 픽셀도 안 남아 매끈한 장대로 보였다 - 마디가 안 읽히면 대나무가
#   아니라 갈대다. 로우폴리 + 원거리에서는 과장이 정답이라는 것이 이 저장소의 반복된 결론이다.


# ══════════════════════════════════════════════════════════════════════════════
# 기하 기본기
# ══════════════════════════════════════════════════════════════════════════════
def _tangents(pts):
    """점열에서 접선을 뽑는다(끝점은 한쪽 차분, 안쪽은 중앙 차분)."""
    out = []
    n = len(pts)
    for i in range(n):
        if i == 0:
            t = pts[1] - pts[0]
        elif i == n - 1:
            t = pts[-1] - pts[-2]
        else:
            t = pts[i + 1] - pts[i - 1]
        if t.length < 1e-9:
            t = Vector((0.0, 1.0, 0.0))
        out.append(t.normalized())
    return out


def _frames(tangents):
    """평행이송 프레임. 튜브가 크게 휘어도 비틀림이 안 쌓인다.

    to_track_quat 을 쓰면 안 된다 - 그쪽은 Blender 의 Z-up 을 전제하는데 이 파이프라인은
    Y-up(Unity 좌표)이라 측면 단면이 90도 돌아 나온다(mgbuild.turntable 주석의 같은 사고).
    """
    t0 = tangents[0]
    ref = Vector((0.0, 0.0, 1.0))
    n = ref - t0 * ref.dot(t0)
    if n.length < 1e-5:
        ref = Vector((1.0, 0.0, 0.0))
        n = ref - t0 * ref.dot(t0)
    n.normalize()

    out = []
    prev = t0
    for t in tangents:
        axis = prev.cross(t)
        if axis.length > 1e-9:
            n = (Matrix.Rotation(prev.angle(t), 4, axis.normalized()) @ n)
        n = n - t * n.dot(t)
        if n.length < 1e-7:
            n = Vector((1.0, 0.0, 0.0))
        n.normalize()
        out.append((n, t.cross(n)))     # (법선, 종법선) - b = t × n 이라야 감김이 바깥이다
        prev = t
    return out


def add_tube(bm, uvl, rings, sides, tile, wraps, smooth_bands,
             cap_bottom=True, cap_top=True):
    """축에 **수직인** 단면으로 튜브를 굽는다. rings = [(중심, 접선, 반지름 or 반지름리스트)].

    mg.swept_tube 와 갈리는 점 둘:
      (1) 단면이 수평(XZ)이 아니라 접선에 수직이다 → 늘어진(pendulous) 줄기에서 마디 링이
          리본으로 찌그러지지 않는다. 대신 마디 링은 줄기가 누우면 같이 눕는다(실물이 그렇다).
      (2) UV 를 **여기서** 굽는다. 둘레 = U(0..wraps), 축 방향 누적 호 길이 / tile = V.
          mg.cylinder_uv 는 월드 원점 기준 atan2 라, 원점에서 떨어진 가지에 쓰면 결이 눕는다.

    감김은 손으로 맞춘다(recalc_face_normals 를 안 쓴다). b = t × n 일 때
    (lo[i], lo[j], hi[j], hi[i]) 순서가 바깥을 향한다 - 밑 캡만 뒤집으면 된다.
    recalc 를 쓰면 면 루프 순서가 뒤집혀 방금 구운 UV 대응이 깨진다.
    """
    frames = _frames([r[1] for r in rings])
    loops, vs = [], []
    acc, prev_c = 0.0, None
    for (c, _t, rad), (n, b) in zip(rings, frames):
        if prev_c is not None:
            acc += (c - prev_c).length
        prev_c = c
        vs.append(acc / tile)
        radii = rad if isinstance(rad, (list, tuple)) else [rad] * sides
        loop = []
        for i in range(sides):
            a = math.tau * i / sides
            loop.append(bm.verts.new(c + n * (math.cos(a) * radii[i])
                                     + b * (math.sin(a) * radii[i])))
        loops.append(loop)

    made = []
    for k, (lo, hi) in enumerate(zip(loops, loops[1:])):
        for i in range(sides):
            j = (i + 1) % sides
            f = bm.faces.new((lo[i], lo[j], hi[j], hi[i]))
            f.smooth = smooth_bands[k]
            u0, u1 = i / sides * wraps, (i + 1) / sides * wraps
            made.append((f, {lo[i]: (u0, vs[k]), lo[j]: (u1, vs[k]),
                             hi[j]: (u1, vs[k + 1]), hi[i]: (u0, vs[k + 1])}))
    if cap_bottom:
        f = bm.faces.new(tuple(reversed(loops[0])))
        f.smooth = False
        made.append((f, {v: (0.5, vs[0]) for v in loops[0]}))
    if cap_top:
        f = bm.faces.new(tuple(loops[-1]))
        f.smooth = False
        made.append((f, {v: (0.5, vs[-1]) for v in loops[-1]}))

    for f, table in made:
        for lp in f.loops:
            lp[uvl].uv = table[lp.vert]
    return loops


# ══════════════════════════════════════════════════════════════════════════════
# 죽간 중심선
# ══════════════════════════════════════════════════════════════════════════════
def build_path(arc_len, azim, tilt0, tilt1, tilt_pow, swing, phase, drift, steps=72):
    """기울기 프로파일 θ(s) = θ0 + (θ1-θ0)·s^p 를 **적분해** 중심선을 만든다.

    수직에서 잰 기울기를 호 길이를 따라 적분하므로, θ1 이 90도를 넘으면 끝이 실제로
    아래로 넘어간다 - 늘어짐(pendulous) 자세가 그냥 나온다. 직선을 옆으로 미는 방식
    (v2)으로는 끝이 숙는 모양이 안 나온다.
    swing 은 완만한 S자(한 주기 사인), drift 는 방위가 서서히 도는 양이다.
    """
    ds = 1.0 / steps
    pos = Vector((0.0, 0.0, 0.0))
    pts = [pos.copy()]
    for i in range(steps):
        s = (i + 0.5) * ds
        th = tilt0 + (tilt1 - tilt0) * (s ** tilt_pow) + swing * math.sin(math.tau * s + phase)
        az = azim + drift * s
        d = Vector((math.sin(th) * math.cos(az), math.cos(th), math.sin(th) * math.sin(az)))
        pos = pos + d * (arc_len * ds)
        pts.append(pos.copy())
    return pts


def path_for_apex(apex_y, **kw):
    """꼭대기 높이가 apex_y 가 되도록 호 길이를 역산한다(3회면 0.1% 안에 든다)."""
    arc = apex_y
    pts = build_path(arc, **kw)
    for _ in range(3):
        top = max(p.y for p in pts)
        if top < 1e-4:
            break
        arc *= apex_y / top
        pts = build_path(arc, **kw)
    return pts, arc


def sample(pts, s):
    """중심선을 s∈[0,1] 로 보간해 (위치, 접선) 을 준다."""
    n = len(pts) - 1
    x = min(max(s, 0.0), 1.0) * n
    i = min(int(x), n - 1)
    f = x - i
    p = pts[i].lerp(pts[i + 1], f)
    lo = max(0, i - 1)
    hi = min(n, i + 2)
    t = (pts[hi] - pts[lo])
    if t.length < 1e-9:
        t = Vector((0.0, 1.0, 0.0))
    return p, t.normalized()


def node_positions(count):
    """마디 위치 s. **밑동 짧음 → 중간 김 → 끝 짧음**(실물 레퍼런스의 핵심 신호).

    프로파일 = 0.50 + 0.80·sin(π·u^0.8). u=0 에서 0.50, 중간에서 1.28, u=1 에서 0.50 이라
    가장 긴 마디가 중간보다 살짝 아래에 온다(실물의 비대칭도 같은 방향이다).
    """
    lens = [0.50 + 0.80 * math.sin(math.pi * (((i + 0.5) / count) ** 0.8))
            for i in range(count)]
    total = sum(lens)
    acc, out = 0.0, []
    for L in lens:
        acc += L / total
        out.append(acc * 0.965)     # 마지막 마디 위로 짧은 끝 마디를 남긴다
    return out


# ══════════════════════════════════════════════════════════════════════════════
# 죽간
# ══════════════════════════════════════════════════════════════════════════════
def add_culm(bm, uvl, base, pts, arc, radius, sides, nodes, taper, phase, dead=False):
    """죽간 1개를 bm 에 누적하고, 가지를 달 마디 목록을 돌려준다.

    링 배치는 마디마다 3개다(아래 잘록 / 융기 / 죽피 칼라). v2 는 마디 사이가 계속
    가늘어져 "원뿔을 쌓은 것"으로 보였는데, 칼라 링을 융기 **위**에 하나 더 두면
    마디를 지난 직후 굵기가 되돌아와 마디 사이가 진짜로 곧아진다.
    """
    below = NODE_BELOW / arc
    above = NODE_ABOVE / arc
    ns = node_positions(nodes)

    # (s, 굵기 배수, 이 링에서 다음 링까지의 띠를 스무스로 둘 것인가)
    seq = [(0.0, 1.10, False), (min(0.020, ns[0] * 0.25), 1.00, True)]
    for s in ns:
        seq.append((s - below, NODE_FACTORS[0], False))
        seq.append((s, NODE_FACTORS[1], False))
        seq.append((s + above, NODE_FACTORS[2], True))
    seq.append((1.0, 0.90 if not dead else 0.86, True))
    seq = [e for e in seq if 0.0 <= e[0] <= 1.0]
    seq.sort(key=lambda e: e[0])

    rings, bands, nodes_out = [], [], []
    for k, (s, fac, smooth) in enumerate(seq):
        c, t = sample(pts, s)
        r0 = radius * (1.0 - taper * (s ** 1.06)) * fac
        radii = [r0 * (1.0 + 0.022 * math.cos(2.0 * (math.tau * i / sides) + phase))
                 for i in range(sides)]
        rings.append((base + c, t, radii))
        if k < len(seq) - 1:
            bands.append(smooth)
    add_tube(bm, uvl, rings, sides, BAMBOO_TILE, BAMBOO_WRAPS, bands)

    for s in ns:
        c, t = sample(pts, s)
        nodes_out.append((base + c, t, radius * (1.0 - taper * (s ** 1.06)), s))
    return nodes_out


def add_branch(bm, uvl, origin, direction, length, radius, droop, sides, segs=2):
    """마디에서 뻗는 가지 1개. 끝으로 갈수록 가늘어지고 아래로 처진다.

    가지에도 마디가 있지만 지름 1 cm 짜리라 게임 거리에서 한 픽셀도 안 남는다 -
    삼각형은 잎에 쓰는 편이 낫다. 대신 **가지가 있다/없다**는 실루엣에서 바로 읽힌다.
    """
    d = direction.normalized()
    p = origin.copy()
    pts = [p.copy()]
    for i in range(segs):
        d = (d + Vector((0.0, -droop / segs, 0.0))).normalized()
        p = p + d * (length / segs)
        pts.append(p.copy())
    tans = _tangents(pts)
    rings = [(pts[i], tans[i], max(0.0035, radius * (1.0 - 0.62 * i / segs)))
             for i in range(len(pts))]
    add_tube(bm, uvl, rings, sides, 0.22, 1.0, [True] * (len(rings) - 1))
    return pts[-1], tans[-1]


def add_shoot(bm, uvl, base, height, radius, azim, rng):
    """어린 죽순(죽피가 남아 있는 상태). 죽피가 겹쳐 계단처럼 보이는 원뿔이다.

    죽피 조각을 따로 붙이지 않는다 - 얇은 판을 줄기 메시(단면 아님)에 섞으면 뒷면이
    사라진다. 반지름 프로파일을 톱니로 만들면 같은 실루엣이 삼각형 절반 값에 나온다.
    """
    pts = build_path(height * 1.02, azim, 0.05, 0.30, 2.0, 0.0, 0.0, 0.0, steps=24)
    sheaths = 5
    seq = [(0.0, 1.06, False)]
    for i in range(sheaths):
        s = (i + 1) / (sheaths + 0.6)
        seq.append((s - 0.012, 1.14, False))
        seq.append((s + 0.012, 0.94, True))
    seq.append((1.0, 0.10, True))
    seq = [e for e in seq if 0.0 <= e[0] <= 1.0]
    seq.sort(key=lambda e: e[0])
    rings, bands = [], []
    for k, (s, fac, smooth) in enumerate(seq):
        c, t = sample(pts, s)
        r = max(0.006, radius * (1.0 - 0.80 * (s ** 0.85)) * fac)
        rings.append((base + c, t, r))
        if k < len(seq) - 1:
            bands.append(smooth)
    add_tube(bm, uvl, rings, 6, BAMBOO_TILE, BAMBOO_WRAPS, bands)


# ══════════════════════════════════════════════════════════════════════════════
# 잎
# ══════════════════════════════════════════════════════════════════════════════
def add_blade(bm, uvl, hub, direction, length, half_w, droop, roll):
    """피침형 잎날 1장 = 삼각형 2개(양면 처리 뒤 4개).

    실물 규격: 길이 7~25 cm · 폭 1~4 cm(Kew GrassBase / Guadua Bamboo).
    v2 는 0.60~0.90 m 짜리를 썼는데 그건 대나무 잎이 아니라 야자 소엽 크기다.
    폭이 가장 넓은 곳을 길이의 30% 에 두고 밑동을 한 점으로 모으면 피침형이 나온다.
    """
    z = direction.normalized()
    x = z.cross(Vector((0.0, 1.0, 0.0)))
    if x.length < 1e-5:
        x = Vector((1.0, 0.0, 0.0))
    x.normalize()
    y = x.cross(z)
    ca, sa = math.cos(roll), math.sin(roll)
    x, y = x * ca + y * sa, y * ca - x * sa

    def put(vx, vy, vz):
        return bm.verts.new(hub + x * vx + y * vy + z * vz)

    dz = droop * length
    v_base = put(0.0, 0.0, 0.0)
    v_m0 = put(-half_w, -dz * 0.10, length * 0.30)
    v_m1 = put(half_w, -dz * 0.10, length * 0.30)
    v_tip = put(0.0, -dz, length)
    uvs = {v_base: (0.5, 0.0), v_m0: (0.04, 0.30), v_m1: (0.96, 0.30), v_tip: (0.5, 1.0)}
    for tri in ((v_base, v_m0, v_m1), (v_m0, v_tip, v_m1)):
        f = bm.faces.new(tri)
        f.smooth = False
        for lp in f.loops:
            lp[uvl].uv = uvs[lp.vert]


def add_cluster(bm, uvl, hub, forward, count, rng, leaf_len, leaf_hw, fan=0.85):
    """가지 끝 잎다발. 좁은 부채꼴로 갈라져 전부 아래로 처진다.

    방위를 고르게 흩뿌리면 별표가 된다(v2 4차 렌더의 실패). 부채꼴 + 일관된 처짐이
    "다발"을 만든다. 몇 장은 수평 근처에 둬야 잎면이 빛을 받아 통째로 어둡게 안 나온다.
    """
    f = forward.normalized()
    side = f.cross(Vector((0.0, 1.0, 0.0)))
    if side.length < 1e-5:
        side = Vector((1.0, 0.0, 0.0))
    side.normalize()
    up = side.cross(f)
    for i in range(count):
        spread = (i - (count - 1) * 0.5) / max(1, count - 1)     # -0.5..0.5
        yaw = spread * fan + rng.uniform(-0.16, 0.16)
        # 1차 렌더는 pitch 가 -0.55~0.12 라 잎면이 거의 다 아래를 봤고, 다발이 통째로
        # 검은 점으로 찍혔다. 절반쯤은 수평 위로 들어 올려야 태양광을 받는다.
        pitch = rng.uniform(-0.42, 0.34) - abs(spread) * 0.22
        d = (f * math.cos(yaw) + side * math.sin(yaw)
             + up * math.tan(pitch) * 0.9).normalized()
        add_blade(bm, uvl, hub + d * rng.uniform(0.0, 0.035), d,
                  rng.uniform(*leaf_len), rng.uniform(*leaf_hw),
                  rng.uniform(0.22, 0.52), rng.uniform(-0.9, 0.9))


# ══════════════════════════════════════════════════════════════════════════════
# 한 종 만들기
# ══════════════════════════════════════════════════════════════════════════════
POSTURE = {
    #             tilt0        tilt1(꼭대기 기울기 rad)  s^p 지수   S자 진폭
    "erect":     ((0.02, 0.06), (0.10, 0.26), 1.9, (0.010, 0.030)),
    "nodding":   ((0.02, 0.06), (0.55, 0.86), 3.1, (0.010, 0.028)),
    "pendulous": ((0.06, 0.14), (1.10, 1.42), 2.0, (0.012, 0.034)),
}


def build(spec):
    """한 종(포기 또는 단독)을 만들어 (줄기오브젝트, 잎오브젝트, 실측표) 를 준다."""
    rng = mg.Rng(spec["seed"])
    cbm = bmesh.new()
    cuv = cbm.loops.layers.uv.new("UVMap")
    lbm = bmesh.new()
    luv = lbm.loops.layers.uv.new("UVMap")

    n_live = spec["live"]
    n_dead = spec.get("dead", 0)
    n_shoot = spec.get("shoots", 0)
    total = n_live + n_dead
    apex = spec["apex"]
    clump_r = spec["clump_r"]
    blade_count = 0
    culm_specs = []

    for i in range(total):
        dead = i >= n_live
        # 굵은 것이 가운데, 어린/가는 것이 바깥 - 실제 군생 대나무의 배치다
        # (새 죽순은 포기 바깥 가장자리에서 올라온다).
        rank = i / max(1, total - 1) if total > 1 else 0.0
        azim = math.tau * i / max(1, total) + rng.uniform(-0.35, 0.35)
        dist = 0.0 if (i == 0 and total > 2) else clump_r * (0.35 + 0.65 * rank) * rng.uniform(0.7, 1.15)
        base = Vector((math.cos(azim) * dist, 0.0, math.sin(azim) * dist))

        h_scale = rng.uniform(*spec["height_mix"]) if i > 0 else 1.0
        if dead:
            h_scale *= rng.uniform(0.62, 0.80)      # 마른 죽간은 끝이 부러져 짧다
        top = apex * h_scale

        posture = spec["posture"] if not dead else "erect"
        t0r, t1r, tpow, swr = POSTURE[posture]
        tilt1 = rng.uniform(*t1r) * (1.0 if not dead else 0.45)
        pts, arc = path_for_apex(
            top, azim=azim + rng.uniform(-0.5, 0.5),
            tilt0=rng.uniform(*t0r), tilt1=tilt1, tilt_pow=tpow,
            swing=rng.uniform(*swr), phase=rng.uniform(0.0, math.tau),
            drift=rng.uniform(-0.5, 0.5))

        radius = rng.uniform(*spec["radius"]) * (1.0 - 0.30 * rank)
        if dead:
            radius *= 0.82
        sides = spec["sides"] if radius >= spec["radius"][0] * 0.85 else max(5, spec["sides"] - 2)
        # 마디 간격 / 지름 = 3~6 (Bambusa vulgaris: 25~35cm / 4~10cm) 을 지켜 마디 수를 정한다
        gap = radius * 2.0 * spec["node_ratio"]
        n_nodes = max(4, min(16, int(round(arc / gap))))

        node_list = add_culm(cbm, cuv, base, pts, arc, radius, sides, n_nodes,
                             spec["taper"], rng.uniform(0.0, math.tau), dead)
        culm_specs.append((radius * 2.0, top, n_nodes, arc / n_nodes, dead))

        # ── 가지 + 잎 ────────────────────────────────────────────────────────
        golden = 2.39996
        bstart = spec["branch_start"]
        for k, (c, t, r, s) in enumerate(node_list):
            if s < bstart:
                continue
            up_w = (s - bstart) / max(1e-3, 1.0 - bstart)      # 위로 갈수록 가지가 많다
            side = t.cross(Vector((0.0, 1.0, 0.0)))
            if side.length < 1e-5:
                side = Vector((1.0, 0.0, 0.0))
            side.normalize()
            other = side.cross(t)
            for bi in range(2 if not dead else 1):
                if bi == 1 and rng.uniform(0.0, 1.0) > 0.78 + 0.22 * up_w:
                    continue
                ang = golden * (k * 2 + bi) + rng.uniform(-0.3, 0.3)
                out = (side * math.cos(ang) + other * math.sin(ang)).normalized()
                elev = rng.uniform(0.62, 1.00) if bi == 0 else rng.uniform(0.85, 1.25)
                d = (out * math.sin(elev) + t * math.cos(elev)).normalized()
                blen = spec["branch_len"][0] + (spec["branch_len"][1] - spec["branch_len"][0]) * up_w
                blen *= rng.uniform(0.75, 1.2) * (1.0 if bi == 0 else 0.58)
                brad = max(0.005, r * (0.16 if bi == 0 else 0.10))
                tip, tdir = add_branch(cbm, cuv, c + out * (r * 0.85), d, blen, brad,
                                       rng.uniform(0.35, 0.85), 4 if bi == 0 else 3)
                if dead:
                    continue
                n_leaf = rng.randint(*spec["cluster"])
                add_cluster(lbm, luv, tip, tdir, n_leaf, rng,
                            spec["leaf_len"], spec["leaf_hw"])
                blade_count += n_leaf
                if bi == 0 and blen > 0.24:      # 굵은 가지는 중간에도 한 다발
                    mid = c + out * (r * 0.85) + (tip - c - out * (r * 0.85)) * 0.55
                    n2 = max(3, n_leaf - 1)
                    add_cluster(lbm, luv, mid, tdir, n2, rng,
                                spec["leaf_len"], spec["leaf_hw"], fan=1.05)
                    blade_count += n2
        # 꼭대기 마디 위의 끝가지 - v2 는 여기가 비어 "맨 장대"로 끝났다
        if not dead:
            c, t = sample(pts, 0.995)
            n_leaf = rng.randint(*spec["cluster"])
            add_cluster(lbm, luv, base + c, t, n_leaf + 1, rng,
                        spec["leaf_len"], spec["leaf_hw"], fan=1.5)
            blade_count += n_leaf + 1

    for i in range(n_shoot):
        azim = math.tau * (i + 0.5) / max(1, n_shoot) + rng.uniform(-0.4, 0.4)
        dist = clump_r * rng.uniform(0.95, 1.35)
        base = Vector((math.cos(azim) * dist, 0.0, math.sin(azim) * dist))
        add_shoot(cbm, cuv, base, apex * rng.uniform(0.14, 0.36),
                  spec["radius"][1] * rng.uniform(0.85, 1.05), azim, rng)

    bmesh.ops.triangulate(cbm, faces=cbm.faces[:])
    culms = mg.new_object("culms", cbm)

    bmesh.ops.triangulate(lbm, faces=lbm.faces[:])
    mg.make_double_sided(lbm)       # ★ 이 뒤에 remove_doubles 를 부르면 뒷면이 통째로 녹는다
    leaves = mg.new_object("leaves", lbm)
    mg.shade_flat(leaves)

    # ── 높이를 계약값에 정확히 맞춘다(균등 배율 - 비례는 안 흔든다) ────────────
    lo, hi = mg.union_bbox([culms, leaves])
    k = spec["height"] / max(1e-6, hi.y - lo.y)
    for o in (culms, leaves):
        o.data.transform(Matrix.Diagonal((k, k, k, 1.0)))

    # 잎이 죽간 밑동보다 아래로 내려가면 접지 정렬이 포기 전체를 공중에 띄운다.
    lo2, _ = mg.union_bbox([culms, leaves])
    floor = min(v.co.y for v in culms.data.vertices)
    sunk = 0
    for v in leaves.data.vertices:
        if v.co.y < floor + 0.05:
            v.co.y = floor + 0.05
            sunk += 1

    meas = {
        "blades": blade_count,
        "culms": culm_specs,
        "scale": k,
        "sunk": sunk,
    }
    return culms, leaves, meas


# ══════════════════════════════════════════════════════════════════════════════
# 8종 표
# ══════════════════════════════════════════════════════════════════════════════
#  height : ★ a~f 는 IslandResourceSpawner.MeshLibrary.cs:271 의 BambooModelHeights 와
#           바이트 단위로 같아야 한다(게임이 이 값으로 배율을 계산한다). 고치지 마라.
#           g/h 는 a~f 가 비워 둔 선택 구간을 메우도록 고른 신규 값이다(C# 등록 필요).
#  apex   : 배율을 먹이기 전의 목표 꼭대기 높이. height 와 가깝게 둬야 배율이 1 근처다.
#  node_ratio : 마디 간격 / 지름. 실물 3~6 (Bambusa vulgaris 25~35cm / 4~10cm).
SPECIES = [
    dict(name="bamboo_a", seed=77001, height=3.349, apex=3.30, budget=6000, floor=1500,
         concept="굵고 낮은 종 (thick & low) · 죽간 3대",
         label="thick & low  3 culms  erect",
         live=3, dead=0, shoots=0, posture="erect", clump_r=0.30,
         radius=(0.072, 0.088), sides=8, taper=0.36, node_ratio=3.0,
         height_mix=(0.80, 0.97), branch_start=0.32, branch_len=(0.24, 0.50),
         cluster=(6, 8), leaf_len=(0.19, 0.29), leaf_hw=(0.022, 0.033)),

    dict(name="bamboo_b", seed=77002, height=3.885, apex=3.85, budget=6000, floor=1500,
         concept="어린 죽순 섞인 군생 · 죽간 4대 + 죽순 3",
         label="clump 4 + 3 young shoots (sheathed)",
         live=4, dead=0, shoots=3, posture="erect", clump_r=0.34,
         radius=(0.050, 0.064), sides=6, taper=0.46, node_ratio=4.2,
         height_mix=(0.62, 0.94), branch_start=0.40, branch_len=(0.22, 0.48),
         cluster=(5, 7), leaf_len=(0.20, 0.30), leaf_hw=(0.023, 0.034)),

    dict(name="bamboo_c", seed=77003, height=4.463, apex=4.45, budget=6000, floor=2000,
         concept="빽빽한 군생 · 죽간 7대",
         label="dense clump  7 culms",
         live=7, dead=0, shoots=0, posture="erect", clump_r=0.42,
         radius=(0.049, 0.061), sides=6, taper=0.48, node_ratio=5.0,
         height_mix=(0.56, 0.98), branch_start=0.48, branch_len=(0.22, 0.48),
         cluster=(5, 7), leaf_len=(0.21, 0.31), leaf_hw=(0.024, 0.035)),

    dict(name="bamboo_d", seed=77004, height=4.113, apex=4.09, budget=2500, floor=700,
         concept="단독 중간 · 죽간 2대(주 1 + 곁 1)",
         label="solitary medium  2 culms",
         live=2, dead=0, shoots=0, posture="erect", clump_r=0.18,
         radius=(0.050, 0.060), sides=8, taper=0.46, node_ratio=4.4,
         height_mix=(0.55, 0.72), branch_start=0.40, branch_len=(0.22, 0.48),
         cluster=(7, 9), leaf_len=(0.19, 0.29), leaf_hw=(0.023, 0.033)),

    dict(name="bamboo_e", seed=77005, height=5.070, apex=5.02, budget=2500, floor=700,
         concept="단독 큰 대 · 죽간 2대 · 끝이 숙는다(nodding)",
         label="solitary large  nodding tip",
         live=2, dead=0, shoots=0, posture="nodding", clump_r=0.20,
         radius=(0.058, 0.070), sides=8, taper=0.50, node_ratio=4.0,
         height_mix=(0.50, 0.68), branch_start=0.44, branch_len=(0.26, 0.56),
         cluster=(7, 9), leaf_len=(0.20, 0.30), leaf_hw=(0.024, 0.034)),

    dict(name="bamboo_f", seed=77006, height=4.252, apex=4.20, budget=6000, floor=1500,
         concept="늘어짐(pendulous) · 죽간 4대가 활처럼 휜다",
         label="pendulous  4 arching culms",
         live=4, dead=0, shoots=0, posture="pendulous", clump_r=0.24,
         radius=(0.046, 0.058), sides=6, taper=0.52, node_ratio=4.6,
         height_mix=(0.64, 0.94), branch_start=0.38, branch_len=(0.18, 0.42),
         cluster=(5, 7), leaf_len=(0.18, 0.27), leaf_hw=(0.021, 0.031)),

    dict(name="bamboo_g", seed=77007, height=3.650, apex=3.62, budget=6000, floor=1200,
         concept="[신규] 마른 죽간 섞인 군생 · 생 3 + 마름 2",
         label="[new] clump 3 live + 2 dead culms",
         live=3, dead=2, shoots=1, posture="erect", clump_r=0.32,
         radius=(0.050, 0.062), sides=6, taper=0.46, node_ratio=4.2,
         height_mix=(0.60, 0.95), branch_start=0.40, branch_len=(0.20, 0.44),
         cluster=(5, 7), leaf_len=(0.19, 0.28), leaf_hw=(0.022, 0.032)),

    dict(name="bamboo_h", seed=77008, height=4.750, apex=4.70, budget=6000, floor=1500,
         concept="[신규] 끄덕임(nodding) 군생 · 죽간 5대의 끝이 모두 숙는다",
         label="[new] nodding clump  5 culms",
         live=5, dead=0, shoots=0, posture="nodding", clump_r=0.36,
         radius=(0.048, 0.060), sides=6, taper=0.50, node_ratio=4.4,
         height_mix=(0.60, 0.95), branch_start=0.44, branch_len=(0.20, 0.46),
         cluster=(5, 7), leaf_len=(0.19, 0.28), leaf_hw=(0.022, 0.032)),
]


def main():
    print("[bamboo] 대나무 8종 생성 (v3)")
    rows = []
    for spec in SPECIES:
        name = spec["name"]
        mg.reset_scene()
        culms, leaves, meas = build(spec)
        culms.name, leaves.name = f"{name}_culms", f"{name}_leaves"

        stats = mg.enforce_contract_group([culms, leaves], tri_budget=spec["budget"],
                                          tri_floor=spec["floor"], name=name, align="ground")
        gc = mg.ground_center([culms, leaves])

        out = os.path.join(mg.MODELS_DIR, f"{name}.obj")
        mg.export_obj([culms, leaves], out)
        stats = mg.verify_obj_file(out, stats)
        # ★ 반드시 verify 뒤다 - verify 는 mtllib/usemtl 이 있으면 계약 위반으로 거절한다.
        mg.inject_usemtl(out)

        mg.assign_material(culms, mg.preview_material(
            f"prev_{name}_culm", texture_name="bamboo", base_color=BAMBOO_CULM, roughness=0.48))
        mg.assign_material(leaves, mg.preview_material(
            f"prev_{name}_leaf", texture_name="frond", base_color=FROND_GREEN, roughness=0.60))
        dia = [c[0] for c in meas["culms"]]
        gaps = [c[3] for c in meas["culms"]]
        mg.turntable([culms, leaves], os.path.join(PREVIEW_DIR, f"{name}.png"),
                     title=f"{name}  seed {spec['seed']}  {spec['label']}",
                     stats=stats,
                     notes="D%.0f-%.0fmm  N%.0f-%.0fcm  L%d"
                           % (min(dia) * 1000 * meas["scale"], max(dia) * 1000 * meas["scale"],
                              min(gaps) * 100 * meas["scale"], max(gaps) * 100 * meas["scale"],
                              meas["blades"]))
        mg.report(stats)
        print(f"             접지 중심 (x {gc.x:+.4f}, z {gc.z:+.4f}) m   "
              f"높이맞춤 배율 {meas['scale']:.4f}   잎 {meas['blades']}장   "
              f"바닥보정 {meas['sunk']}정점")
        rows.append((name, stats, meas, spec))

    print("\n[bamboo] 실측표")
    print("  이름        시드   W×H×D (m)              tris/예산   죽간  마디간격cm  잎장")
    for name, st, meas, spec in rows:
        s = st["size"]
        gaps = [c[3] * meas["scale"] * 100 for c in meas["culms"]]
        print(f"  {name:<11}{spec['seed']}  "
              f"{s[0]:.2f} x {s[1]:.3f} x {s[2]:.2f}   {st['tris']:>5}/{st['budget']}   "
              f"{len(meas['culms']):>2}   {min(gaps):.0f}~{max(gaps):.0f}   {meas['blades']:>4}")
    print("[bamboo] 완료 - 렌더: Tools/blender/_preview/_bamboo/bamboo_*.png")
    return rows


if __name__ == "__main__":
    main()
