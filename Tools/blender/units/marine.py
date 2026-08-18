#!/usr/bin/env python3
"""
marine_a~h - 눈에 띄는 해양 생물 8종 (2026-08-18, "바다가 비어 보인다" 대응).

    python3 Tools/blender/units/marine.py

산출물 (전부 신규)
  Assets/_Project/Resources/Models/marine_a~h.obj (+ .mtl - inject_usemtl 이 만든다)
  Tools/blender/_preview/_marine/marine_*.png    렌더 - 저장소에 넣지 않는다

  a 소형 물고기 A  L 0.22m  방추형 / 갈라진 꼬리 / 옆으로 납작 (떼 유영용)
  b 소형 물고기 B  L 0.18m  둥근 체형(자리돔풍) / 짧은 꼬리
  c 중형 물고기    L 0.30m  원반형(나비고기풍) / 뾰족한 주둥이 / 큰 등지느러미
  d 바다거북       L 1.10m  육각 판 등딱지 / 큰 앞지느러미 / 머리·꼬리
  e 가오리         W 1.60m  마름모 날개 / 긴 채찍 꼬리 / 납작
  f 해파리         D 0.55m  반구 갓 + 촉수 10가닥 + 구완 4가닥
  g 문어           L 0.90m  둥근 외투막 + 구부러진 다리 8개
  h 돌고래         L 2.20m  유선형 / 등지느러미 / 부리 / 수평 갈라진 꼬리

────────────────────────────────────────────────────────────────────────────────
원점 규약 (**이 계열의 예외 사항 - 게임 코드가 읽어야 한다**)

  이 8종은 지면에 놓이는 소품이 아니라 **헤엄쳐 다니는 생물**이라 몸통 중심이 원점이어야
  자연스럽다. 그런데 mgbuild 는 계약 1장("밑면 y=0")을 코드로 강제하고, 중심 정렬 옵션이
  없다 - align 은 'bbox'(bbox 의 XZ 중심) / 'ground'(접지부 XZ 중심) 둘뿐이고 **둘 다 밑면을
  y=0 으로 내린다**. Y 를 통째로 내리는 sink 파라미터는 enforce_contract(단일 오브젝트)
  에만 있고, 이 계열은 `o` 그룹이 2개라 enforce_contract_group 을 써야 해서 못 쓴다.
  mgbuild 수정은 이번 배치의 락 밖이다.

  => **접지 정렬(align="bbox", 밑면 y=0)을 유지하고, 보정 오프셋을 여기서 실측해 보고한다.**
     각 종의 PIVOT_NOTE / 스크립트 말미 MARINE_PIVOT 표에 몸통 중심 좌표를 미터로 찍는다.
     게임 코드는 모델을 자식으로 달고 `localPosition = -pivot` 을 주면 몸통 중심이
     부모 Transform 원점에 오고, 그 자리에서 요/피치를 돌리면 된다.

     pivot = **body 그룹의 bbox 중심**(x 는 0, y 가 실제 보정값). 예외 둘:
       f 해파리 = **갓 꼭대기**. 갓이 끌고 촉수가 끌려오므로 회전 중심이 갓 위쪽이라야
                  자연스럽다(촉수 bbox 중심으로 잡으면 갓이 궤도를 크게 돈다).
       e 가오리 = **원반(날개) bbox 중심**. body(중앙 몸통)의 중심은 원반 중심보다
                  훨씬 앞이고 전체 bbox 중심은 채찍 꼬리에 끌려 훨씬 뒤라 둘 다 못 쓴다.

`o` 그룹 (**순서 고정 - body 먼저**)
  body  몸통(거북 등딱지+머리, 해파리 갓, 문어 외투막+머리, 가오리 몸통)
  fin   지느러미·촉수·다리(해파리 촉수/구완, 문어 다리 8개, 가오리 날개+꼬리)
  게임에서 두 색을 따로 입힌다. 여기 머티리얼은 프리뷰 렌더 전용이고 OBJ 로 안 나간다.

  가오리(e)의 날개를 fin 에 둔 이유: 몸통과 날개를 **한 솔리드로 잘라 나누면** 경계에서
  틈/Z 파이팅이 난다. 대신 날개 블레이드를 몸통 솔리드 **안으로 파고들게** 겹쳐 둔다
  (seaform 이 덩어리를 겹치는 것과 같은 수법 - 관통은 렌더에 안 보인다).

계약: 미터 / +Y up / **+Z front(머리가 +Z)** / OBJ+법선+UV / 머티리얼 없이 export ->
verify_obj_file -> mg.inject_usemtl(서브메시 구분자). 시드 75001~75008 고정 = 같은 md5.
삼각형 예산: a/b/c small_prop(1500, 실제 300 안팎 - 떼로 수십 마리 나온다),
d~h large_structure(8000, 실제 1000~1800).

렌더는 **전용 하위 폴더** _preview/_marine/ 에 쓴다. 다른 에이전트가 동시에 Blender 를
돌릴 수 있어 _preview 루트에서 타일 임시 폴더가 겹치는 사고를 피한다.
"""

import math
import os
import sys

_UNITS_DIR = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.dirname(_UNITS_DIR))

import mgbuild as mg  # noqa: E402
import bmesh  # noqa: E402
from mathutils import Matrix, Vector  # noqa: E402


# ── 블레이드(지느러미) 로컬 평면 → 월드 회전 ─────────────────────────────────
# blade() 의 아웃라인은 로컬 (u, v) 평면에 그리고 로컬 +Z 로 두께를 준다.
# VERT: u->월드 +Z, v->월드 +Y, 두께 X   (등/뒷/꼬리지느러미 - 몸통 옆면에 서는 판)
# HORZ: u->월드 +X, v->월드 +Z, 두께 Y   (가슴지느러미·돌고래 꼬리날개 - 눕는 판)
VERT = Matrix.Rotation(math.radians(-90.0), 4, "Y")
HORZ = Matrix.Rotation(math.radians(90.0), 4, "X")
MIRROR_X = Matrix.Diagonal((-1.0, 1.0, 1.0, 1.0))


def blade(bm, outline, thickness, mat, smooth=False):
    """두께 있는 얇은 판(지느러미). outline = 닫힌 (u, v) 폴리곤(오목해도 된다).

    잎처럼 단면(두께 0)으로 두면 URP 기본 백페이스 컬링에서 뒤에서 사라진다
    (mgbuild.make_double_sided 주석 참고). 물고기 지느러미는 두께 2~30mm 면 삼각형이
    거의 안 늘면서(앞뒤 ngon 2 + 옆면 링) 어느 각도에서도 보인다 - 그쪽을 쓴다.

    앞/뒷면은 ngon 으로 만들고 나중에 mg.triangulate 이 귀 자르기로 삼각화한다.
    오목 폴리곤(갈라진 꼬리의 V 홈)이 그대로 통과하는 이유가 이것이다.
    """
    h = thickness * 0.5
    front = [bm.verts.new(mat @ Vector((u, v, h))) for u, v in outline]
    back = [bm.verts.new(mat @ Vector((u, v, -h))) for u, v in outline]
    faces = [bm.faces.new(front), bm.faces.new(list(reversed(back)))]
    n = len(outline)
    for i in range(n):
        j = (i + 1) % n
        faces.append(bm.faces.new((front[i], front[j], back[j], back[i])))
    for f in faces:
        f.smooth = smooth
    bmesh.ops.recalc_face_normals(bm, faces=faces)
    return faces


def sail(z_front, z_back, y_base, h_front, h_peak, h_back, peak_t=0.42, n=7, sign=1.0):
    """등/뒷지느러미(돛) 아웃라인. 밑변은 체표 **안쪽**(y_base)에 두어 몸통에 파고들게 한다.

    [1차 렌더 반성] 처음엔 꼭짓점 4~8개를 손으로 찍었더니 지느러미가 각진 판때기로 나왔고
    (marine_b/c), 아웃라인이 접혀 몸통에서 떠 보이기까지 했다. 바깥 가장자리를 **매끈한
    아치 함수**로 뽑고 밑변을 직선으로 닫으면 접힘이 원천적으로 불가능하다.
    """
    pts = [(z_front, y_base)]
    for k in range(n):
        t = k / (n - 1.0)
        z = z_front + (z_back - z_front) * t
        if t <= peak_t:
            u = t / max(peak_t, 1e-6)
            h = h_front + (h_peak - h_front) * math.sin(u * math.pi * 0.5) ** 0.80
        else:
            u = (t - peak_t) / max(1.0 - peak_t, 1e-6)
            h = h_back + (h_peak - h_back) * math.cos(u * math.pi * 0.5) ** 0.80
        pts.append((z, y_base + sign * h))
    pts.append((z_back, y_base))
    return pts


def forked_tail(z_base, h_base, z_tip, y_tip, z_notch, tip_cut=0.16, n=4):
    """갈라진 꼬리지느러미 아웃라인(위/아래 엽 + 중앙 V 홈).

    V 홈이 이 파이프라인에서 "물고기"와 "캡슐에 붙은 판"을 가르는 유일한 실루엣이다 -
    노치 없이 부채꼴로 두면 정면/후면 컷에서 정체를 알 수 없다(1차 렌더로 확인).
    """
    up = []
    for k in range(1, n):
        t = k / (n - 1.0)
        z = z_base + (z_tip - z_base) * t
        y = h_base + (y_tip - h_base) * math.sin(t * math.pi * 0.5) ** 1.25
        up.append((z, y))
    inner = (z_tip + (z_base - z_tip) * tip_cut, y_tip * 0.62)
    top = [(z_base, h_base)] + up + [inner]
    return top + [(z_notch, 0.0)] + [(z, -y) for z, y in reversed(top)]


def leaf_outline(length, w_lead, w_trail, sweep=0.30, n=7):
    """노(paddle)형 아웃라인 - 가슴지느러미·거북 앞발·돌고래 가슴지느러미 공용.

    u = 밑동(0) -> 끝(length), v = 앞(+) / 뒤(-). sweep 은 끝이 뒤로 밀리는 정도(낫 모양).
    밑동에서 폭이 0 이 되지 않게(env 하한) 두어 몸통 안으로 파고드는 밑변이 남는다.
    """
    lead, trail = [], []
    for k in range(n):
        t = k / (n - 1.0)
        u = length * t
        shift = -sweep * length * t ** 1.7
        env = math.sin(math.pi * (0.12 + 0.86 * t)) ** 0.75
        lead.append((u, shift + w_lead * env))
        trail.append((u, shift - w_trail * env))
    return lead + list(reversed(trail))


def blade_pair(bm, outline, thickness, mat, smooth=False):
    """좌우 한 쌍(가슴지느러미·앞발). 오른쪽을 만들고 X 를 뒤집어 왼쪽을 만든다.

    미러는 감김을 뒤집지만 blade() 안의 recalc_face_normals 가 껍질 단위로 바로잡는다.
    """
    blade(bm, outline, thickness, mat, smooth)
    blade(bm, outline, thickness, MIRROR_X @ mat, smooth)


# ── 로프트(몸통) ──────────────────────────────────────────────────────────────
def loft(bm, sections, sides=8, smooth=True):
    """단면 링을 이어 붙인 닫힌 몸통. sections = [(z, cy, rx, ry_top, ry_bot)].

    단면은 **상하 비대칭 타원**이다: 위쪽 반은 반경 ry_top, 아래쪽 반은 ry_bot.
    등이 낮고 배가 불룩한(또는 그 반대인) 실루엣이 한 값짜리 타원으로는 안 나온다.
    y=0 선에서 접선이 꺾이지만 그 자리가 마침 물고기의 **측선**이라 오히려 자연스럽다.

    rx(가로 반경)를 ry 보다 훨씬 작게 주면 **옆으로 납작한** 물고기가 된다 - 이게
    "캡슐이 아니라 물고기"로 읽히게 하는 첫 번째 조건이다(두 번째는 뾰족한 주둥이,
    세 번째는 갈라진 꼬리).
    """
    loops = []
    for z, cy, rx, ryt, ryb in sections:
        loop = []
        for i in range(sides):
            a = math.tau * i / sides
            s = math.sin(a)
            ry = ryt if s >= 0.0 else ryb
            loop.append(bm.verts.new((rx * math.cos(a), cy + ry * s, z)))
        loops.append(loop)
    faces = []
    for lo, hi in zip(loops, loops[1:]):
        for i in range(sides):
            j = (i + 1) % sides
            faces.append(bm.faces.new((lo[i], lo[j], hi[j], hi[i])))
    faces.append(bm.faces.new(loops[0]))
    faces.append(bm.faces.new(loops[-1]))
    for f in faces:
        f.smooth = smooth
    bmesh.ops.recalc_face_normals(bm, faces=faces)
    return loops


def tube(bm, path, radii, sides=6, up=(0.0, 1.0, 0.0), smooth=True):
    """임의 3D 경로를 따라 쓸어낸 닫힌 튜브(촉수·다리·채찍 꼬리).

    mg.swept_tube 는 단면이 **수평 XZ 원**이라 세로로 선 줄기 전용이다 - 문어 다리처럼
    수평으로 뻗는 구간이 있으면 단면이 눕혀져 리본이 된다. 여기서는 접선에서 프레임을
    만들되 기준축 up 을 고정해(seaform.sweep 과 같은 방식) 비틀림을 없앤다.
    접선이 up 과 나란해지면(수직으로 늘어진 촉수) 대체축으로 넘어간다.
    """
    n = len(path)
    upv = Vector(up).normalized()
    loops = []
    for i, c in enumerate(path):
        if i == 0:
            tan = path[1] - path[0]
        elif i == n - 1:
            tan = path[-1] - path[-2]
        else:
            tan = path[i + 1] - path[i - 1]
        tan = tan.normalized()
        side = upv.cross(tan)
        if side.length < 1e-4:
            side = Vector((1.0, 0.0, 0.0)).cross(tan)
        side.normalize()
        nrm = tan.cross(side).normalized()
        r = radii[i]
        ra, rb = r if isinstance(r, (tuple, list)) else (r, r)
        loops.append([bm.verts.new(c + side * (ra * math.cos(math.tau * k / sides))
                                   + nrm * (rb * math.sin(math.tau * k / sides)))
                      for k in range(sides)])
    faces = []
    for lo, hi in zip(loops, loops[1:]):
        for i in range(sides):
            j = (i + 1) % sides
            faces.append(bm.faces.new((lo[i], lo[j], hi[j], hi[i])))
    faces.append(bm.faces.new(loops[0]))
    faces.append(bm.faces.new(loops[-1]))
    for f in faces:
        f.smooth = smooth
    bmesh.ops.recalc_face_normals(bm, faces=faces)
    return loops


def lathe(bm, profile, sides=16, smooth=True):
    """Y 축 회전체. profile = **닫힌** (r, y) 폴리곤(바깥면 + 안쪽면을 한 바퀴로 잇는다).

    해파리 갓처럼 **속이 빈 그릇**은 바깥면과 안쪽면을 가진 껍질이라, 단면 폴리곤을
    통째로 돌리는 이 방식이 가장 싸고 확실하다(솔리디파이 모디파이어 없이 닫힌 솔리드).
    r == 0 인 마디는 극점으로 보고 정점 하나를 공유해 삼각형 팬을 만든다 - 그냥 반경 0
    링을 만들면 면적 0 삼각형이 생겨 enforce_contract 가 죽인다.
    """
    rings = []
    for r, y in profile:
        if r < 1e-6:
            rings.append([bm.verts.new((0.0, y, 0.0))] * sides)
        else:
            rings.append([bm.verts.new((r * math.cos(math.tau * k / sides), y,
                                        r * math.sin(math.tau * k / sides)))
                          for k in range(sides)])
    faces = []
    pairs = list(zip(rings, rings[1:])) + [(rings[-1], rings[0])]
    for lo, hi in pairs:
        for i in range(sides):
            j = (i + 1) % sides
            vs = [lo[i], lo[j], hi[j], hi[i]]
            uniq = []
            for v in vs:
                if v not in uniq:
                    uniq.append(v)
            if len(uniq) >= 3:
                faces.append(bm.faces.new(uniq))
    for f in faces:
        f.smooth = smooth
    bmesh.ops.recalc_face_normals(bm, faces=faces)
    return faces


def ellipsoid(bm, center, semi, subdiv=2, smooth=True, squash_y=None):
    """아이코스피어 -> 타원체(머리·눈·외투막 덩어리). 극점 특이점이 없다.

    셰이딩 플래그는 **이번에 만든 껍질에만** 건다. 예전에 bm.faces 전체를 돌았더니
    거북 등갑(플랫)이 뒤이어 붙는 머리(스무스)에 통째로 덮여 육각 판이 사라졌다.
    """
    res = bmesh.ops.create_icosphere(bm, subdivisions=subdiv, radius=1.0)
    mine = set()
    for v in res["verts"]:
        n = v.co.normalized()
        y = n.y
        if squash_y is not None and y < 0.0:
            y *= squash_y
        v.co = Vector((n.x * semi[0], y * semi[1], n.z * semi[2])) + Vector(center)
        mine.update(v.link_faces)
    for f in mine:
        f.smooth = smooth
    return res["verts"]


def finish(name, bm, smooth_default=True):
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
    return mg.new_object(name, bm)


# ══════════════════════════════════════════════════════════════════════════════
# a: 소형 물고기 A - 방추형, 갈라진 꼬리, 옆으로 납작
# ══════════════════════════════════════════════════════════════════════════════
def build_a(rng):
    body = bmesh.new()
    # (z, cy, rx, ry_top, ry_bot). rx 가 ry 의 55% 라 옆으로 납작하다.
    # 링 8개 x 8면 = 124tri. 떼로 수십 마리가 동시에 나오는 종이라(스펙 "300tri 이하")
    # 링을 하나 줄였다 - 꼬리 쪽 중간 링은 실루엣에 기여가 없어 눈으로 차이가 안 난다.
    loft(body, [
        (0.110, 0.000, 0.0030, 0.0038, 0.0034),   # 주둥이 끝(뾰족)
        (0.098, 0.001, 0.0085, 0.0105, 0.0092),
        (0.080, 0.002, 0.0135, 0.0185, 0.0160),   # 머리·눈
        (0.055, 0.002, 0.0152, 0.0248, 0.0222),   # 어깨
        (0.022, 0.001, 0.0145, 0.0268, 0.0238),   # 최대 체고
        (-0.014, 0.000, 0.0105, 0.0196, 0.0168),
        (-0.042, -0.001, 0.0048, 0.0088, 0.0072),  # 미병(자루) - 급격히 가늘다
        (-0.056, -0.001, 0.0026, 0.0048, 0.0040),
    ], sides=8, smooth=True)
    # 눈: 실루엣이 아니라 **가까이서 물고기로 읽히게** 하는 요소. 몸통에 파고들게 겹친다.
    for sx in (-1.0, 1.0):
        ellipsoid(body, (sx * 0.0130, 0.0075, 0.0790), (0.0035, 0.0038, 0.0038),
                  subdiv=1, smooth=True)
    body_obj = finish("body", body)

    fin = bmesh.new()
    # 지느러미 아웃라인 점 수(n)는 **삼각형 예산의 주범**이다 - 점 하나가 앞뒤 캡 2 + 옆면
    # 링 2 = 4tri 다. 실루엣을 정하는 등/꼬리는 n 을 남기고, 가슴지느러미(작고 몸에 붙어
    # 윤곽 기여가 적다)를 n=4 로 깎아 종 전체를 300tri 아래로 맞췄다.
    # 등지느러미 - 등선(y≈0.026) 안쪽에서 시작해 위로 아치.
    blade(fin, sail(0.062, -0.008, 0.018, 0.008, 0.036, 0.006, peak_t=0.38, n=5),
          0.0032, VERT)
    # 뒷지느러미
    blade(fin, sail(0.002, -0.034, -0.016, 0.005, 0.024, 0.004, peak_t=0.45, n=5,
                    sign=-1.0), 0.0030, VERT)
    # 꼬리지느러미 - **깊은 V 홈**이 이 종의 식별 포인트다.
    blade(fin, forked_tail(-0.040, 0.008, -0.110, 0.042, -0.066, n=3), 0.0030, VERT)
    # 가슴지느러미 한 쌍 - 눕는 판이라 HORZ, 뒤로 젖힌다.
    pect = Matrix.Translation((0.012, -0.005, 0.052)) \
        @ Matrix.Rotation(math.radians(-40.0), 4, "Y") \
        @ Matrix.Rotation(math.radians(-30.0), 4, "Z") @ HORZ
    blade_pair(fin, leaf_outline(0.030, 0.009, 0.007, sweep=0.35, n=4), 0.0020, pect)
    fin_obj = finish("fin", fin)
    return body_obj, fin_obj


# ══════════════════════════════════════════════════════════════════════════════
# b: 소형 물고기 B - 자리돔풍 둥근 체형, 짧은 꼬리
# ══════════════════════════════════════════════════════════════════════════════
def build_b(rng):
    body = bmesh.new()
    # a 보다 짧고(0.18) 훨씬 깊다: 체고/체장 = 0.072/0.135 ≈ 0.53(a 는 0.31).
    loft(body, [
        (0.082, 0.000, 0.0035, 0.0042, 0.0040),
        (0.072, 0.002, 0.0105, 0.0130, 0.0120),
        (0.056, 0.004, 0.0175, 0.0270, 0.0230),
        (0.034, 0.004, 0.0205, 0.0360, 0.0330),
        (0.006, 0.002, 0.0200, 0.0375, 0.0345),   # 최대 체고 0.072
        (-0.024, 0.000, 0.0158, 0.0282, 0.0252),
        (-0.048, -0.001, 0.0086, 0.0138, 0.0114),
        (-0.062, -0.001, 0.0032, 0.0055, 0.0046),
    ], sides=8, smooth=True)
    for sx in (-1.0, 1.0):
        ellipsoid(body, (sx * 0.0165, 0.0130, 0.0570), (0.0040, 0.0044, 0.0044),
                  subdiv=1, smooth=True)
    body_obj = finish("body", body)

    fin = bmesh.new()
    # 등지느러미가 등선을 따라 길게 눕는다(자리돔은 등지느러미가 몸 길이의 절반 이상).
    # 앞쪽 극선부가 높고 뒤가 낮은 아치라 a 의 짧은 삼각 돛과 실루엣이 갈린다.
    blade(fin, sail(0.062, -0.032, 0.026, 0.010, 0.030, 0.016, peak_t=0.30, n=5),
          0.0032, VERT)
    blade(fin, sail(0.010, -0.042, -0.026, 0.007, 0.026, 0.014, peak_t=0.40, n=5,
                    sign=-1.0), 0.0032, VERT)
    # 짧은 꼬리 - 노치가 얕다(a 의 깊은 포크와 대비되는 식별점).
    blade(fin, forked_tail(-0.050, 0.011, -0.090, 0.032, -0.072, tip_cut=0.22, n=3),
          0.0030, VERT)
    pect = Matrix.Translation((0.017, -0.003, 0.030)) \
        @ Matrix.Rotation(math.radians(-42.0), 4, "Y") \
        @ Matrix.Rotation(math.radians(-24.0), 4, "Z") @ HORZ
    blade_pair(fin, leaf_outline(0.032, 0.011, 0.009, sweep=0.32, n=4), 0.0022, pect)
    fin_obj = finish("fin", fin)
    return body_obj, fin_obj


# ══════════════════════════════════════════════════════════════════════════════
# c: 중형 물고기 - 나비고기풍 원반형, 뾰족한 주둥이, 큰 등지느러미
# ══════════════════════════════════════════════════════════════════════════════
def build_c(rng):
    body = bmesh.new()
    # 원반형: 체고 0.135 / 체장 0.20 = 0.68. rx 는 체고의 1/6 이하 - **종잇장처럼 납작**.
    loft(body, [
        (0.150, 0.000, 0.0035, 0.0045, 0.0042),   # 주둥이 끝
        (0.135, 0.000, 0.0075, 0.0090, 0.0088),   # 긴 부리 - 나비고기의 결정적 특징
        (0.118, 0.001, 0.0110, 0.0140, 0.0135),
        (0.098, 0.004, 0.0165, 0.0330, 0.0300),   # 이마에서 급격히 솟는다
        (0.070, 0.006, 0.0200, 0.0560, 0.0520),
        (0.030, 0.005, 0.0215, 0.0690, 0.0660),   # 최대 체고 0.135
        (-0.010, 0.003, 0.0198, 0.0640, 0.0600),
        (-0.048, 0.000, 0.0150, 0.0430, 0.0390),
        (-0.076, -0.001, 0.0092, 0.0230, 0.0195),
        (-0.094, -0.001, 0.0050, 0.0105, 0.0088),  # 미병
        (-0.104, -0.001, 0.0030, 0.0060, 0.0050),
    ], sides=10, smooth=True)
    for sx in (-1.0, 1.0):
        ellipsoid(body, (sx * 0.0180, 0.0250, 0.1000), (0.0055, 0.0062, 0.0062),
                  subdiv=1, smooth=True)
    body_obj = finish("body", body)

    fin = bmesh.new()
    # 큰 등지느러미 - 원반 윤곽을 그대로 이어받아 위로 크게 솟는다(체고의 +55%).
    # 이 한 장이 "나비고기"를 만든다: 몸통만 보면 그냥 납작한 방추형이다.
    blade(fin, sail(0.096, -0.072, 0.030, 0.016, 0.070, 0.020, peak_t=0.45), 0.0042, VERT)
    # 뒷지느러미 - 등지느러미와 거울처럼 아래로. 원반 실루엣이 완성된다.
    blade(fin, sail(0.028, -0.084, -0.030, 0.014, 0.060, 0.018, peak_t=0.50, sign=-1.0),
          0.0042, VERT)
    # 꼬리 - 짧고 노치가 얕다(나비고기는 포크가 거의 없다).
    blade(fin, forked_tail(-0.098, 0.014, -0.150, 0.046, -0.126, tip_cut=0.20),
          0.0038, VERT)
    pect = Matrix.Translation((0.019, -0.008, 0.062)) \
        @ Matrix.Rotation(math.radians(-38.0), 4, "Y") \
        @ Matrix.Rotation(math.radians(-26.0), 4, "Z") @ HORZ
    blade_pair(fin, leaf_outline(0.046, 0.016, 0.013, sweep=0.32), 0.0028, pect)
    fin_obj = finish("fin", fin)
    return body_obj, fin_obj


# ══════════════════════════════════════════════════════════════════════════════
# d: 바다거북 - 육각 판 등딱지, 큰 앞지느러미
# ══════════════════════════════════════════════════════════════════════════════
# 등갑 판(scute) 중심 - XZ 평면 좌표(미터, 정규화 전). 척추판 5 + 좌우 늑판 4씩 +
# 가장자리 연판. 여기에 부드러운 융기를 얹으면 **판 사이가 골로 남아** 육각 무늬로 읽힌다.
def _scute_centers():
    pts = [(0.0, z) for z in (-0.30, -0.15, 0.00, 0.15, 0.30)]
    for sx in (-1.0, 1.0):
        for z in (-0.29, -0.10, 0.09, 0.27):
            pts.append((sx * 0.215, z))
    for i in range(14):                       # 연판(가장자리 한 줄)
        a = math.tau * (i + 0.5) / 14.0
        pts.append((0.345 * math.sin(a), 0.455 * math.cos(a)))
    return pts


SCUTES = _scute_centers()
SCUTE_R = 0.150


def _scute_bump(x, z):
    """가장 가까운 판 중심까지의 거리로 만든 융기(0 = 판 경계, 1 = 판 한가운데).

    [2차 렌더 반성] 가우시안을 **더하면** 이웃 판끼리 봉우리가 섞여 골이 메워진다 -
    아무리 진폭을 올려도 매끈한 베개였다. **최댓값**을 쓰면 두 중심의 중간선에서 장이
    꺾여(C0) 골이 선으로 남고, 플랫 셰이딩과 만나 육각 판 경계로 읽힌다.
    """
    best = 0.0
    for cx, cz in SCUTES:
        d2 = (x - cx) ** 2 + (z - cz) ** 2
        if d2 < SCUTE_R * SCUTE_R:
            b = 1.0 - (math.sqrt(d2) / SCUTE_R) ** 1.7
            if b > best:
                best = b
    return best


def _shell_outline(u):
    """등갑 윤곽(위에서 본 모양). 심장형: 앞이 넓고 뒤로 좁아진다. u=0 이 +Z(앞)."""
    rear = (1.0 - math.cos(u)) * 0.5          # 0 = 앞, 1 = 뒤
    w = 1.0 + 0.05 * math.cos(u) - 0.04 * math.cos(2.0 * u)
    x = 0.395 * math.sin(u) * w * (1.0 - 0.26 * rear ** 1.6)
    z = 0.500 * math.cos(u) * w
    return x, z


def build_d(rng):
    body = bmesh.new()
    # [1차 렌더 반성] 30x11 격자에서는 판 하나(폭 0.20m)에 링이 2줄밖에 안 걸려 융기가
    # 스무스 셰이딩에 통째로 먹혔다 - **매끈한 베개**로 나왔다. v 방향을 15줄로 올리고
    # 융기 폭(SCUTE_S2)을 좁혀야 골이 살아난다. 등갑은 **플랫 셰이딩** - 판이 각져야
    # 육각 판으로 읽히고, 스무스로 두면 아무리 밀어도 물방울로 뭉갠다(실측).
    NU, NV = 32, 15                            # 둘레 32 x 위아래 15
    rings = []
    for iv in range(NV):
        v = iv / (NV - 1.0)
        phi = v * math.pi                      # 0 = 등갑 꼭대기, pi = 배갑 바닥
        rf = max(math.sin(phi), 1e-4) ** 0.55
        if phi <= math.pi * 0.5:
            y = 0.150 * math.cos(phi) ** 0.72  # 납작한 돔(1차 0.185 는 통통한 베개였다)
        else:
            y = -0.048 * (-math.cos(phi)) ** 1.35   # 배갑(plastron)은 거의 평평하다
        ring = []
        for iu in range(NU):
            u = math.tau * iu / NU
            ox, oz = _shell_outline(u)
            x, z, yy = ox * rf, oz * rf, y     # yy: 링 높이 y 를 **덮어쓰지 않는다**
            if yy > 0.004:                     # 등갑에만 육각 판 융기
                b = _scute_bump(x, z)
                k = 1.0 + 0.045 * b
                x, z = x * k, z * k
                yy += 0.048 * b - 0.024
            ring.append(bm_v(body, (x, yy, z)))
        rings.append(ring)
    grid_faces(body, rings, close_u=True, cap_first=True, cap_last=True, smooth=False)

    # 목 + 머리(+Z). 등갑 앞(z=0.50) **바깥으로 확실히 나오게** 뽑는다 - 1차 렌더는
    # 머리가 등갑 그늘에 파묻혀 정면 컷에서 거북인지 알 수 없었다.
    tube(body, [Vector((0.0, 0.020, 0.42)), Vector((0.0, 0.042, 0.52)),
                Vector((0.0, 0.062, 0.60))],
         [0.078, 0.066, 0.058], sides=8, up=(0.0, 1.0, 0.0))
    ellipsoid(body, (0.0, 0.070, 0.660), (0.070, 0.060, 0.086), subdiv=2, smooth=True)
    ellipsoid(body, (0.0, 0.052, 0.732), (0.042, 0.034, 0.040), subdiv=1, smooth=True)  # 주둥이
    for sx in (-1.0, 1.0):                     # 눈
        ellipsoid(body, (sx * 0.056, 0.094, 0.682), (0.016, 0.016, 0.016),
                  subdiv=1, smooth=True)
    # 짧은 꼬리(-Z)
    tube(body, [Vector((0.0, -0.010, -0.46)), Vector((0.0, -0.020, -0.53)),
                Vector((0.0, -0.026, -0.575))],
         [0.048, 0.030, 0.014], sides=6, up=(0.0, 1.0, 0.0))
    body_obj = finish("body", body)

    fin = bmesh.new()
    # 앞지느러미: 크게 펼친 노(paddle). 길이 0.56m - 등갑 반폭(0.40)보다 훨씬 길어
    # "날개처럼 펼친 팔"로 읽힌다. 이게 거북과 (등딱지 얹은) 돌덩이를 가르는 실루엣이다.
    front = Matrix.Translation((0.20, -0.020, 0.235)) \
        @ Matrix.Rotation(math.radians(-30.0), 4, "Y") \
        @ Matrix.Rotation(math.radians(-13.0), 4, "Z") @ HORZ
    blade_pair(fin, leaf_outline(0.560, 0.145, 0.120, sweep=0.30, n=8),
               0.022, front, smooth=True)
    # 뒷지느러미: 작고 뭉툭한 방향타.
    rear = Matrix.Translation((0.215, -0.038, -0.315)) \
        @ Matrix.Rotation(math.radians(34.0), 4, "Y") \
        @ Matrix.Rotation(math.radians(-7.0), 4, "Z") @ HORZ
    blade_pair(fin, leaf_outline(0.235, 0.078, 0.068, sweep=0.22, n=6),
               0.019, rear, smooth=True)
    fin_obj = finish("fin", fin)
    return body_obj, fin_obj


def bm_v(bm, co):
    return bm.verts.new(co)


def grid_faces(bm, rings, close_u=True, cap_first=False, cap_last=False, smooth=True):
    """링 격자를 면으로 잇는다. 극점(첫/마지막 링이 한 점으로 수렴)은 캡 ngon 으로 막는다."""
    faces = []
    nu = len(rings[0])
    for lo, hi in zip(rings, rings[1:]):
        for i in range(nu if close_u else nu - 1):
            j = (i + 1) % nu
            vs = [lo[i], lo[j], hi[j], hi[i]]
            uniq = []
            for v in vs:
                if v not in uniq:
                    uniq.append(v)
            if len(uniq) >= 3:
                faces.append(bm.faces.new(uniq))
    if cap_first and len(set(rings[0])) >= 3:
        faces.append(bm.faces.new(rings[0]))
    if cap_last and len(set(rings[-1])) >= 3:
        faces.append(bm.faces.new(rings[-1]))
    for f in faces:
        f.smooth = smooth
    bmesh.ops.recalc_face_normals(bm, faces=faces)
    return faces


# ══════════════════════════════════════════════════════════════════════════════
# e: 가오리 - 마름모 날개, 긴 채찍 꼬리, 납작
# ══════════════════════════════════════════════════════════════════════════════
def _ray_station(s):
    """가오리 날개 단면 위치. s = 0(몸통 옆) ~ 1(날개 끝). (x, 앞모서리 z, 뒷모서리 z)."""
    # [2차 렌더 반성] 앞모서리를 너무 쓸었더니 원반 길이/폭이 1:2.15 가 되어 **종이비행기**로
    # 보였다. 실제 노랑가오리류는 1:1.1~1.3 이다 - 원반을 Z 로 늘려 마름모답게 만든다.
    x = 0.020 + 0.780 * s
    zl = 0.400 - 0.520 * s ** 1.45        # 앞모서리(뒤로 쓸린다)
    zt = -0.520 + 0.400 * s ** 1.75       # 뒷모서리(앞으로 죈다) -> 끝에서 만나 뾰족해진다
    return x, zl, zt


def ray_wing(bm, sign, ns=9, nc=6, s_max=0.96, th0=0.046, lift=0.085):
    """가오리 한쪽 날개. **두께가 끝으로 갈수록 얇아지고 끝이 들리는** 익형 격자.

    1차 렌더는 blade() 로 만든 **두께 균일 평판**이었다 - 위에서 보면 마름모지만 옆에서
    보면 12mm 판때기라 "가오리"가 아니라 "잘라 낸 종이"였다. 날개 끝 두께를 0 에 가깝게
    좁히고 끝을 살짝 들어 올리면 같은 삼각형 수로 살아 있는 날개가 된다.
    앞/뒷모서리(c=0, c=1)에서는 위·아래 표면이 **한 정점을 공유**해 칼날처럼 닫힌다.
    """
    loops = []
    for i in range(ns):
        s = s_max * i / (ns - 1.0)
        x, zl, zt = _ray_station(s)
        y0 = 0.010 + lift * s ** 2.2
        th = th0 * (1.0 - s) ** 1.6
        loop = []
        for k in range(nc):                               # 위 표면(앞 -> 뒤)
            c = k / (nc - 1.0)
            loop.append(bm.verts.new((sign * x, y0 + th * math.sin(math.pi * c) ** 0.55,
                                      zl + (zt - zl) * c)))
        for k in range(nc - 2, 0, -1):                    # 아래 표면(뒤 -> 앞)
            c = k / (nc - 1.0)
            loop.append(bm.verts.new((sign * x, y0 - th * math.sin(math.pi * c) ** 0.55,
                                      zl + (zt - zl) * c)))
        loops.append(loop)
    n = len(loops[0])
    faces = []
    for lo, hi in zip(loops, loops[1:]):
        for i in range(n):
            j = (i + 1) % n
            faces.append(bm.faces.new((lo[i], lo[j], hi[j], hi[i])))
    faces.append(bm.faces.new(loops[0]))
    faces.append(bm.faces.new(loops[-1]))
    for f in faces:
        f.smooth = True
    bmesh.ops.recalc_face_normals(bm, faces=faces)


def build_e(rng):
    body = bmesh.new()
    # 몸통(중앙). 납작한 방추형 - 앞이 뾰족(주둥이), 뒤로 가늘어진다.
    loft(body, [
        (0.430, 0.000, 0.020, 0.010, 0.008),
        (0.360, 0.000, 0.075, 0.030, 0.024),
        (0.240, 0.000, 0.150, 0.058, 0.040),
        (0.090, 0.000, 0.195, 0.072, 0.046),   # 최대 두께 0.118
        (-0.070, 0.000, 0.185, 0.062, 0.040),
        (-0.220, 0.000, 0.135, 0.042, 0.028),
        (-0.330, 0.000, 0.075, 0.026, 0.018),
        (-0.400, 0.000, 0.038, 0.016, 0.012),
    ], sides=12, smooth=True)
    for sx in (-1.0, 1.0):                     # 눈 - 등면 위로 튀어나온다(가오리 특징)
        ellipsoid(body, (sx * 0.085, 0.052, 0.235), (0.030, 0.024, 0.032),
                  subdiv=1, smooth=True)
    for sx in (-1.0, 1.0):                     # 분수공(spiracle)
        ellipsoid(body, (sx * 0.115, 0.048, 0.165), (0.032, 0.018, 0.030),
                  subdiv=1, smooth=True)
    body_obj = finish("body", body)

    fin = bmesh.new()
    # 날개: 몸통 솔리드 **안으로 파고드는** 마름모 판. 앞모서리가 앞으로 쓸리고 끝이
    # 뾰족하다 - 이 실루엣이 없으면 그냥 '납작한 돌'이다.
    # 두께를 주면 끝이 각목이 되므로 얇게(12mm) 두고 스무스 셰이딩으로 이어 붙인다.
    ray_wing(fin, +1.0)
    ray_wing(fin, -1.0)
    # 배지느러미 한 쌍(꼬리 앞 아래)
    pelv = Matrix.Translation((0.02, -0.006, -0.470)) @ HORZ
    blade_pair(fin, [(0.000, 0.070), (0.145, 0.025), (0.170, -0.070), (0.030, -0.098)],
               0.012, pelv, smooth=True)
    # 채찍 꼬리 - 몸 길이만큼 길어야 '가오리'로 읽힌다.
    path, radii = [], []
    for i in range(13):
        t = i / 12.0
        z = -0.420 - 0.700 * t
        path.append(Vector((0.0, 0.006 + 0.030 * math.sin(t * 2.1), z)))
        radii.append((0.034 * (1.0 - t) ** 0.85 + 0.0035,
                      0.028 * (1.0 - t) ** 0.85 + 0.0030))
    tube(fin, path, radii, sides=6, up=(0.0, 1.0, 0.0))
    # 꼬리 가시(barb) - 채찍 위에 **얹혀야** 한다. 1차 렌더에서 y 를 안 맞춰 꼬리와
    # 갈라진 두 번째 꼬챙이처럼 보였다(위 path 의 y 곡선을 그대로 따라간 좌표로 고침).
    blade(fin, [(-0.500, 0.018), (-0.532, 0.068), (-0.604, 0.032), (-0.547, 0.013)],
          0.007, VERT)
    fin_obj = finish("fin", fin)
    return body_obj, fin_obj


# ══════════════════════════════════════════════════════════════════════════════
# f: 해파리 - 반구 갓 + 늘어진 촉수
# ══════════════════════════════════════════════════════════════════════════════
def build_f(rng):
    body = bmesh.new()
    # 갓 단면(r, y): 바깥면을 꼭대기 -> 립까지 내려온 뒤, 안쪽면을 립 -> 꼭대기로 되올린다.
    # 닫힌 폴리곤이라 lathe 하면 **속이 빈 종** 한 덩어리가 나온다.
    R, H, TH = 0.275, 0.205, 0.020
    outer, inner = [], []
    N = 9
    for i in range(N + 1):
        t = i / N
        a = t * math.pi * 0.5
        r = R * math.sin(a) ** 0.86
        y = H * math.cos(a) ** 1.15
        outer.append((r, y))
        ri = max(r - TH * (0.45 + 0.55 * t), 0.0)
        inner.append((ri, y - TH * (1.0 - 0.35 * t)))
    profile = outer + list(reversed(inner))
    lathe(body, profile, sides=24, smooth=True)
    # 갓 가장자리 스캘럽(물결 립). 회전체는 정의상 완벽한 원이라 립이 접시처럼 보인다 -
    # 립 쪽에서만(w) 반경/높이를 8주기로 흔들어 **살아 있는 갓**으로 만든다.
    for v in body.verts:
        r = math.hypot(v.co.x, v.co.z)
        if r < 1e-5:
            continue
        th = math.atan2(v.co.z, v.co.x)
        w = min(max(1.0 - v.co.y / (H * 0.80), 0.0), 1.0)
        c = math.cos(8.0 * th)
        k = 1.0 + 0.050 * c * w ** 2
        v.co.x *= k
        v.co.z *= k
        v.co.y -= 0.026 * w ** 3 * (0.5 + 0.5 * c)
    body_obj = finish("body", body)

    fin = bmesh.new()
    # 촉수 10가닥 - 립 가장자리에서 늘어진다. 길이를 어긋나게 해 다발로 보이지 않게.
    NT = 10
    for i in range(NT):
        a = math.tau * i / NT
        r0 = R * 0.93
        length = 0.60 + 0.22 * math.sin(i * 2.1)
        phase = i * 1.31
        path, radii = [], []
        for k in range(8):
            t = k / 7.0
            drift = 0.055 * math.sin(t * 3.0 + phase) * t
            rr = r0 + drift + 0.030 * t
            y = -0.004 - length * t ** 1.06
            path.append(Vector((rr * math.cos(a) + 0.012 * math.sin(t * 5.0 + phase),
                                y,
                                rr * math.sin(a) + 0.012 * math.cos(t * 4.2 + phase))))
            radii.append(0.0075 * (1.0 - t) ** 0.7 + 0.0022)
        # up 을 +Z 로 준다 - 촉수는 거의 수직이라 up=+Y 면 프레임이 무너진다.
        tube(fin, path, radii, sides=6, up=(0.0, 0.0, 1.0))
    # 구완(oral arm) 4가닥 - 갓 아래 중앙에서 주름진 넓은 띠로 늘어진다.
    for i in range(4):
        a = math.tau * i / 4.0 + 0.4
        mat = Matrix.Translation((0.052 * math.cos(a), -0.030, 0.052 * math.sin(a))) \
            @ Matrix.Rotation(-a, 4, "Y")
        blade(fin, [(0.000, 0.000), (0.048, -0.010), (0.070, -0.150), (0.030, -0.310),
                    (-0.010, -0.360), (-0.032, -0.230), (-0.030, -0.070)],
              0.010, mat @ VERT, smooth=True)
    fin_obj = finish("fin", fin)
    # 갓 꼭대기가 y=0 근처가 되도록 전체를 내린다(원점 규약 주석 참고).
    # 최종 정렬은 enforce_contract_group 이 다시 하므로 여기서는 모양만 만든다.
    return body_obj, fin_obj


# ══════════════════════════════════════════════════════════════════════════════
# g: 문어 - 둥근 외투막 + 다리 8개(구부러진 자세)
# ══════════════════════════════════════════════════════════════════════════════
def build_g(rng):
    body = bmesh.new()
    # 외투막(mantle): 뒤(-Z)로 뾰족한 물방울. squash_y 로 아랫배를 눌러 눌린 자루로.
    ellipsoid(body, (0.0, 0.330, -0.150), (0.150, 0.165, 0.235),
              subdiv=3, smooth=True, squash_y=0.88)
    ellipsoid(body, (0.0, 0.300, -0.330), (0.072, 0.080, 0.090), subdiv=2, smooth=True)
    # 머리(눈이 붙는 자리) - 외투막보다 낮고 앞(+Z).
    ellipsoid(body, (0.0, 0.285, 0.045), (0.135, 0.120, 0.140), subdiv=3, smooth=True)
    for sx in (-1.0, 1.0):                     # 문어의 상징 - 튀어나온 눈
        ellipsoid(body, (sx * 0.110, 0.345, 0.055), (0.048, 0.046, 0.050),
                  subdiv=2, smooth=True)
        ellipsoid(body, (sx * 0.128, 0.352, 0.062), (0.026, 0.024, 0.026),
                  subdiv=1, smooth=True)
    body_obj = finish("body", body)

    fin = bmesh.new()
    # 다리 8개. 입(z≈0.10, y≈0.20) 둘레에서 나와 바깥·아래로 휘고 끝이 말린다.
    # [1차 렌더 반성] reach 0.335 는 외투막(길이 0.47)보다 짧아 **거미 다리**로 보였다.
    # 실제 문어 팔은 외투막의 2~3배다. reach 를 0.52 대로 올리면 정규화가 몸을 줄여
    # 상대적으로 팔이 길어지고, 그때 비로소 "문어"로 읽힌다.
    NA = 8
    for i in range(NA):
        a = math.tau * (i + 0.5) / NA          # +0.5 로 정면 정중앙에 다리가 안 오게
        reach = 0.500 + 0.110 * math.sin(i * 1.7)
        curl = 0.075 + 0.060 * math.cos(i * 2.3)
        phase = i * 0.9
        path, radii = [], []
        NK = 11
        for k in range(NK):
            t = k / (NK - 1.0)
            spread = reach * math.sin(t * 1.42) / math.sin(1.42)
            y = 0.235 - 0.245 * (1.0 - math.cos(t * 1.95)) / (1.0 - math.cos(1.95))
            y += curl * t ** 3.0 + 0.032 * math.sin(t * 3.4 + phase) * t
            path.append(Vector((math.cos(a) * spread + 0.038 * math.sin(t * 2.6 + phase),
                                max(y, 0.008),
                                0.100 + math.sin(a) * spread * 1.10)))
            radii.append(0.050 * (1.0 - t) ** 0.85 + 0.0050)
        tube(fin, path, radii, sides=7, up=(0.0, 1.0, 0.0))
    fin_obj = finish("fin", fin)
    return body_obj, fin_obj


# ══════════════════════════════════════════════════════════════════════════════
# h: 돌고래 - 유선형, 등지느러미, 부리, 갈라진 (수평) 꼬리
# ══════════════════════════════════════════════════════════════════════════════
def build_h(rng):
    body = bmesh.new()
    loft(body, [
        (1.050, 0.000, 0.020, 0.022, 0.020),    # 부리 끝
        (0.930, 0.000, 0.038, 0.036, 0.040),    # 부리
        (0.815, 0.004, 0.052, 0.048, 0.054),    # 부리 밑동 - 여기서 한 번 잘록해진다
        (0.760, 0.020, 0.076, 0.104, 0.066),    # 멜론(이마)이 급격히 솟는다
        (0.660, 0.020, 0.112, 0.150, 0.098),    # 멜론 정점 - 큰돌고래의 얼굴
        (0.560, 0.014, 0.136, 0.160, 0.122),
        (0.400, 0.006, 0.158, 0.184, 0.162),
        (0.150, 0.000, 0.176, 0.196, 0.186),    # 최대 둘레
        (-0.100, 0.000, 0.166, 0.180, 0.176),
        (-0.350, 0.000, 0.126, 0.142, 0.130),
        (-0.580, 0.000, 0.080, 0.098, 0.086),
        (-0.760, 0.006, 0.042, 0.062, 0.050),   # 미병 - 옆으로 납작해진다
        (-0.890, 0.010, 0.024, 0.042, 0.032),
    ], sides=12, smooth=True)
    for sx in (-1.0, 1.0):                      # 눈
        ellipsoid(body, (sx * 0.108, 0.030, 0.660), (0.017, 0.017, 0.019),
                  subdiv=1, smooth=True)
    body_obj = finish("body", body)

    fin = bmesh.new()
    # 등지느러미 - 뒤로 낫처럼 휜다(falcate). 돌고래의 1순위 식별 실루엣.
    blade(fin, [(0.210, 0.150), (0.120, 0.330), (-0.030, 0.430), (-0.150, 0.410),
                (-0.060, 0.300), (-0.030, 0.180)],
          0.028, VERT, smooth=True)
    # 가슴지느러미 한 쌍 - 뒤로 크게 젖힌 낫 모양.
    pect = Matrix.Translation((0.105, -0.100, 0.375)) \
        @ Matrix.Rotation(math.radians(-34.0), 4, "Y") \
        @ Matrix.Rotation(math.radians(-42.0), 4, "Z") @ HORZ
    blade_pair(fin, leaf_outline(0.330, 0.090, 0.075, sweep=0.36, n=7),
               0.020, pect, smooth=True)
    # 꼬리날개(fluke) - **수평**이다. 물고기의 세로 꼬리와 갈리는 결정적 차이.
    # 앞모서리가 뒤로 쓸리고 뒷모서리가 오목하게 파인 초승달. 중앙에 얕은 노치.
    fluke = []
    for k in range(6):                          # 오른쪽 앞모서리
        t = k / 5.0
        fluke.append((0.335 * t, -0.790 - 0.300 * t ** 1.45))
    for k in range(4):                          # 오른쪽 뒷모서리(오목) - t=0 은 노치가 대신한다
        t = 1.0 - k / 4.0
        fluke.append((0.325 * t, -1.100 + 0.155 * (1.0 - t) ** 0.75))
    fluke.append((0.0, -0.925))                 # 중앙 노치
    # 첫/마지막 점(둘 다 x=0)을 빼고 미러링한다 - 안 그러면 폴리곤에 **겹친 정점**이 생겨
    # bm.faces.new 가 죽거나 면적 0 삼각형이 남는다.
    fluke += [(-u, v) for u, v in reversed(fluke[1:-1])]
    blade(fin, fluke, 0.028, HORZ, smooth=True)
    fin_obj = finish("fin", fin)
    return body_obj, fin_obj


# ══════════════════════════════════════════════════════════════════════════════
# 파이프라인
# ══════════════════════════════════════════════════════════════════════════════
# (이름, 시드, 빌더, 예산키, 정규화 축, 목표 치수, 몸통색, 지느러미색, pivot 규약, 메모)
SPECS = [
    # 프리뷰 색은 **두 `o` 그룹이 눈으로 갈리도록** 일부러 대비를 크게 준다(OBJ 로 안 나간다).
    ("marine_a", 75001, build_a, "small_prop", "z", 0.22,
     (0.20, 0.40, 0.66), (0.96, 0.68, 0.14), "body", "small fish A / school"),
    ("marine_b", 75002, build_b, "small_prop", "z", 0.18,
     (0.94, 0.52, 0.08), (0.10, 0.14, 0.30), "body", "small fish B / damsel"),
    ("marine_c", 75003, build_c, "small_prop", "z", 0.30,
     (0.97, 0.87, 0.24), (0.08, 0.09, 0.13), "body", "butterflyfish"),
    ("marine_d", 75004, build_d, "large_structure", "z", 1.10,
     (0.20, 0.28, 0.16), (0.70, 0.72, 0.46), "body", "sea turtle"),
    ("marine_e", 75005, build_e, "large_structure", "x", 1.60,
     (0.20, 0.19, 0.22), (0.46, 0.43, 0.48), "disc", "stingray"),
    ("marine_f", 75006, build_f, "large_structure", "x", 0.55,
     (0.62, 0.56, 0.90), (0.98, 0.52, 0.66), "bell_top", "jellyfish"),
    ("marine_g", 75007, build_g, "large_structure", "z", 0.90,
     (0.58, 0.14, 0.18), (0.96, 0.62, 0.52), "body", "octopus"),
    ("marine_h", 75008, build_h, "large_structure", "z", 2.20,
     (0.42, 0.48, 0.56), (0.09, 0.10, 0.14), "body", "dolphin"),
]

AXIS = {"x": 0, "y": 1, "z": 2}


def normalize(objs, axis, target):
    """조립체 전체를 **균일 배율**로 키워 지정 축 치수를 정확히 맞춘다.

    fit_size 는 축마다 따로 늘려 비례를 깨므로 생물에는 쓸 수 없다(납작한 물고기가
    통통해진다). 균일 배율이라 파츠 사이 상대 위치도 그대로다.
    """
    lo, hi = mg.union_bbox(objs)
    ext = (hi - lo)[AXIS[axis]]
    s = target / max(ext, mg.EPS)
    for o in objs:
        o.data.transform(Matrix.Scale(s, 4))
    return s


def produce(name, seed, builder, budget_key, axis, target, body_col, fin_col,
            pivot_kind, note):
    mg.reset_scene()
    rng = mg.Rng(seed)
    body, fin = builder(rng)
    body.name = body.data.name = "body"
    fin.name = fin.data.name = "fin"
    objs = [body, fin]                          # o 그룹 순서 고정: body 먼저

    normalize(objs, axis, target)
    for o in objs:
        mg.triangulate(o)
    lo, hi = mg.union_bbox(objs)
    span = max(hi - lo)
    mg.box_uv(body, tile=max(0.05, span * 0.55))
    mg.box_uv(fin, tile=max(0.05, span * 0.40))
    mg.assign_material(body, mg.preview_material("pv_" + name + "_body",
                                                 base_color=body_col, roughness=0.55))
    mg.assign_material(fin, mg.preview_material("pv_" + name + "_fin",
                                                base_color=fin_col, roughness=0.50))

    budget = mg.TRI_BUDGET[budget_key]
    # align="bbox": mgbuild 에 중심 정렬 옵션이 없다(파일 머리말 "원점 규약" 참고).
    # 밑면 y=0 은 그대로 두고 보정 오프셋을 아래에서 실측해 보고한다.
    stats = mg.enforce_contract_group(objs, tri_budget=budget, tri_floor=120,
                                      name=name, align="bbox")

    out = os.path.join(mg.MODELS_DIR, name + ".obj")
    mg.export_obj(objs, out)
    stats = mg.verify_obj_file(out, stats)
    mg.inject_usemtl(out)                       # 반드시 verify 뒤(호출 순서 계약)

    # ── pivot 실측 ──
    blo, bhi = mg.bbox(body)
    if pivot_kind == "bell_top":
        # 해파리는 갓이 끌고 촉수가 끌려온다 - 회전 중심은 갓 **꼭대기** 쪽이라야 자연스럽다.
        pivot = Vector(((blo.x + bhi.x) * 0.5, bhi.y, (blo.z + bhi.z) * 0.5))
    elif pivot_kind == "disc":
        # 가오리는 body(중앙 몸통)의 중심이 원반 중심보다 훨씬 앞이고, 전체 bbox 중심은
        # 긴 채찍 꼬리에 끌려 훨씬 뒤다. 둘 다 회전 중심으로 못 쓴다 - 날개(=원반) 정점만
        # 골라 그 bbox 중심을 쓴다(꼬리는 x≈0 이라 |x| 문턱으로 걸러진다).
        xs = [abs(v.co.x) for v in fin.data.vertices]
        thr = max(xs) * 0.30
        w = [v.co for v in fin.data.vertices if abs(v.co.x) > thr]
        pivot = Vector((0.0,
                        (min(p.y for p in w) + max(p.y for p in w)) * 0.5,
                        (min(p.z for p in w) + max(p.z for p in w)) * 0.5))
    else:
        pivot = (blo + bhi) * 0.5
    stats["pivot"] = (pivot.x, pivot.y, pivot.z)
    stats["pivot_kind"] = pivot_kind

    png = os.path.join(mg.PREVIEW_DIR, "_marine", name + ".png")
    mg.turntable(objs, png, title=name, stats=stats, px=360, samples=16,
                 notes=f"seed {seed} / {note}")
    mg.report(stats)
    return stats


def main():
    only = set(sys.argv[1:])
    rows = []
    for spec in SPECS:
        if only and spec[0] not in only and spec[0][-1] not in only:
            continue
        rows.append((spec[0], spec[1], produce(*spec)))

    print("MARINE_MANIFEST")
    for name, seed, st in rows:
        s = st["size"]
        print(f"  {name}  seed={seed}  {st['tris']}/{st['budget']}tri  "
              f"{s[0]:.3f} x {s[1]:.3f} x {s[2]:.3f} m  parts={st['parts']}")
    print("MARINE_PIVOT  (게임 코드: model.localPosition = -pivot 로 몸통 중심을 원점에)")
    for name, seed, st in rows:
        p = st["pivot"]
        print(f"  {name}  pivot=({p[0]:+.4f}, {p[1]:+.4f}, {p[2]:+.4f}) m"
              f"  [{st['pivot_kind']}]  bbox_h={st['size'][1]:.3f}")
    print(f"[marine] 완료 - {len(rows)}종")


if __name__ == "__main__":
    main()
