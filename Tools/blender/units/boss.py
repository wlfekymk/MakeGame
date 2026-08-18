#!/usr/bin/env python3
"""
boss_a~c + boss_trophy_a~c - 엔드게임 보스 3종과 트로피 3종 (2026-08-18).

    python3 Tools/blender/units/boss.py            # 6종 전부
    python3 Tools/blender/units/boss.py boss_a     # 한 종만

산출물 (전부 신규)
  Assets/_Project/Resources/Models/boss_a~c.obj / boss_trophy_a~c.obj
      (+ 같은 이름의 .mtl - inject_usemtl 이 만드는 **서브메시 구분자**다)
  Tools/blender/_preview/_boss/*.png    턴테이블 렌더 - 저장소에 넣지 않는다

  a  거대 상어(메갈로돈급)  L 12.0m  두꺼운 방추형 / 거대한 1등지느러미 / 벌어진 턱 +
                                     위아래 이빨 줄 / 아가미 5쌍 / 물어뜯긴 자국·흉터
  b  대왕 곰치              L  7.0m  S자로 굽은 뱀 몸통 / 큰 머리 + 벌린 턱 /
                                     등~꼬리~배로 이어지는 지느러미 주름
  c  심해 괴수(문어형)      W  9.0m  외투막 3.5m + 굵은 다리 8개(빨판 융기) / 큰 눈

  trophy_a  상어 이빨 표본  H 0.72m  톱니 달린 거대 이빨을 뼈 받침에 꽂았다
  trophy_b  곰치 턱뼈 표본  H 0.66m  벌린 위/아래 턱 + 바늘 이빨, 기둥 받침에 고정
  trophy_c  촉수 표본       W 0.62m  나무판 위에 꼬아 놓은 촉수 + 고정 밴드 2줄

────────────────────────────────────────────────────────────────────────────────
원점 규약 (**게임 코드가 읽어야 한다 - marine.py 와 같은 처리다**)

  보스 3종은 지면에 놓이는 소품이 아니라 **헤엄쳐 다니는 생물**이라 몸통 중심이 원점이어야
  자연스럽다. 그런데 mgbuild 는 계약 1장("밑면 y=0")을 코드로 강제하고 중심 정렬 옵션이
  없다 - align 은 'bbox' / 'ground' 둘뿐이고 **둘 다 밑면을 y=0 으로 내린다**. Y 를 통째로
  내리는 sink 는 enforce_contract(단일 오브젝트) 전용이라 `o` 그룹이 2개인 이 계열은 못 쓴다.
  mgbuild 수정은 이번 배치의 락 밖이다.

  => marine.py 와 똑같이 **접지 정렬(align="bbox", 밑면 y=0)을 유지하고, 보정 오프셋을
     여기서 실측해 보고한다.** 게임 코드는 모델을 자식으로 달고 `localPosition = -pivot`
     을 주면 몸통 중심이 부모 Transform 원점에 오고, 그 자리에서 요/피치를 돌리면 된다.

     pivot = **body 그룹의 bbox 중심**. 예외 하나:
       c 심해 괴수 = **외투막 + 머리 덩어리의 중심**을 쓰되 body bbox 중심이 곧 그것이다
                     (다리는 fin 그룹이라 body bbox 에 안 들어온다 - 문어형에서 회전 중심을
                      다리까지 포함한 bbox 로 잡으면 몸이 궤도를 크게 돈다).

  **트로피 3종은 진열용 접지 소품**이다. align="ground", 밑면 y=0, pivot 보정 없음(0,0,0).

`o` 그룹 (**순서 고정 - body 먼저**)
  보스   body = 몸통·머리·외투막·눈·아가미·흉터 / fin = 지느러미·이빨·다리
  트로피 body = 표본 본체(이빨·턱뼈·촉수 - 밝은 쪽) / fin = 받침·기둥·고정 밴드
  게임에서 두 색을 따로 입힌다. 여기 머티리얼은 프리뷰 렌더 전용이고 OBJ 로 안 나간다.

계약: 미터 / +Y up / **+Z front(머리가 +Z)** / OBJ+법선+UV / 머티리얼 없이 export ->
verify_obj_file -> mg.inject_usemtl(호출 순서 계약). 시드 79001~79006 고정 = 같은 md5.
삼각형 예산: 보스 large_structure(8000), 트로피 small_prop(1500).

렌더는 **전용 하위 폴더** _preview/_boss/ 에 쓴다. 다른 에이전트가 동시에 Blender 를
돌릴 수 있어 _preview 루트에서 타일 임시 폴더가 겹치는 사고를 피한다(turntable 주석 참고).

────────────────────────────────────────────────────────────────────────────────
[렌더 반성 - 지우지 마라. 턴테이블을 눈으로 보고 고친 것만 적는다]

설계 단계에서 미리 피한 함정(marine.py 에서 이미 겪은 것):
  · 상어 입 - 이빨만 붙여서는 "닫힌 캡슐 앞의 톱니"다. 몸통 로프트의 **머리 밑면을 통째로
    끌어올려**(ry_bot 을 앞쪽에서 1/3 로) 위턱 자리를 비우고 아래턱을 말굽 튜브로 따로
    달아야 비로소 "벌어진 입"으로 읽힌다.
  · 곰치 S 자 - 중심선을 x(t)=A·sin(pi t)·sin(tau t) 로 두면 **양 끝에서 x'=0** 이라
    머리가 +Z 를, 꼬리가 -Z 를 곧게 본다. sin(tau t) 만 쓰면 t=0 에서 기울기가 남아
    머리가 비스듬히 튀어나간다(계약의 "+Z front" 위반).
  · 문어 다리 - reach 가 외투막 길이보다 짧으면 통째로 "거미"다(marine_g 의 실사고).

1차 렌더 -> 2차 수정:
  a 꼬리 위엽이 가늘고 아래엽이 거의 없어 12m 몸통에 비해 빈약했다. 두 엽을 다 살찌우고
    위엽 중간의 어중간한 노치는 뺐다(흉터 이야기는 등지느러미 V 노치가 맡는다).
    아래턱 반경 0.15 는 옆에서 **쇠막대**로 보여 0.185/0.235 + 턱 밑살 덩어리로 바꿨다.
    눈(반경 0.13)은 체표에 파묻혀 3/4 컷에서 점 하나였다 - 밖으로 밀어 부풀렸다.
  b 머리가 몸통과 같은 굵기라 통째로 **통나무**였다. 머리를 앞으로 0.24m 늘이고 턱 근육을
    몸통 최대 둘레보다 굵게 잡고, 몸통 테이퍼 지수를 0.50/0.42 -> 0.62/0.52 로 올렸다.
    아래턱 경첩을 z=2.94 -> 2.45 로 물려 앞끝 간격(gape)을 0.30 -> 0.48 로 벌렸다.
    꼬리 노(fin 높이 배율)를 0.85 -> 1.70 으로 키워 꼬리 끝이 노처럼 퍼지게 했다.
  c 눈 중심이 머리 타원체 **안**이라 통째로 파묻혔다("큰 눈" 요구가 통째로 실패).
    중심을 표면 밖(정규화 반경 1.16)으로 빼야 눈알이 튀어나온 실루엣이 된다.
    다리 반경 지수 0.80 은 밑동만 굵고 금세 가늘어져 다리가 실처럼 보였다 -> 0.60.
  trophy_a 톱니 9개 x 깊이 13mm 는 변 길이의 1/3 이라 **화살촉/전나무**였다.
    개수 17 x 깊이 6mm 로 잘게 하고 관부를 넓혀 넓은 삼각날이 되게 했다.
  trophy_c 빨판을 다리 **아랫면**에 박았더니 전부 나무판에 가려 울퉁불퉁한 소시지였다.
    _sucker_row 에 face 인자를 붙여 위쪽에 박는다. 고정 밴드도 옆에 세운 막대였던 것을
    촉수 위로 넘겼다.
  trophy_b 턱이 곧은 쇠막대 두 개였다 - _ramus 에 bow(활 굽음)를 넣었다.

2차 렌더 -> 3차 수정:
  c 외투막 혹(반경 0.10~0.19)이 1m 짜리 외투막에서 한 픽셀도 안 남아 매끈한 풍선이었다.
    반경을 배로 키우고 눌러(y 0.72배) 플랫 셰이딩으로 두어 실루엣에 걸리게 했다.
    ("멀리서 안 보이는 디테일은 없는 것과 같다" - Tools/blender/README 함정 10)
  trophy_c 촉수 밑동:끝 굵기비 2.7:1 은 굵기가 안 변해 **밧줄**이었다 -> 5:1.
"""

import math
import os
import sys

_UNITS_DIR = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.dirname(_UNITS_DIR))

import mgbuild as mg  # noqa: E402
import bmesh  # noqa: E402
from mathutils import Matrix, Vector  # noqa: E402


# ── 블레이드(지느러미) 로컬 평면 → 월드 회전 (marine.py 와 같은 규약) ─────────
# blade() 의 아웃라인은 로컬 (u, v) 평면에 그리고 로컬 +Z 로 두께를 준다.
# VERT: u->월드 +Z, v->월드 +Y, 두께 X   (등/뒷/꼬리지느러미 - 몸통 옆면에 서는 판)
# HORZ: u->월드 +X, v->월드 +Z, 두께 Y   (가슴지느러미 - 눕는 판)
VERT = Matrix.Rotation(math.radians(-90.0), 4, "Y")
HORZ = Matrix.Rotation(math.radians(90.0), 4, "X")
MIRROR_X = Matrix.Diagonal((-1.0, 1.0, 1.0, 1.0))


# ══════════════════════════════════════════════════════════════════════════════
# 공용 형상 헬퍼 (marine.py 의 것과 같은 계열 - 이 파일은 자기완결이다)
# ══════════════════════════════════════════════════════════════════════════════
def blade(bm, outline, thickness, mat, smooth=False):
    """두께 있는 얇은 판(지느러미·이빨). outline = 닫힌 (u, v) 폴리곤(오목해도 된다).

    단면(두께 0)으로 두면 URP 기본 백페이스 컬링에서 뒤에서 사라진다. 앞/뒷면은 ngon 으로
    만들고 mg.triangulate 이 귀 자르기로 삼각화한다 - 물어뜯긴 V 홈(오목)이 그대로 통과한다.
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


def blade_pair(bm, outline, thickness, mat, smooth=False):
    """좌우 한 쌍(가슴/배지느러미). 오른쪽을 만들고 X 를 뒤집어 왼쪽을 만든다."""
    blade(bm, outline, thickness, mat, smooth)
    blade(bm, outline, thickness, MIRROR_X @ mat, smooth)


def lens(bm, outline, thickness, mat, waist=0.78, smooth=False):
    """가운데가 볼록한 판(상어 이빨). outline 을 세 겹(축소-원본-축소)으로 쌓는다.

    blade() 는 앞뒤가 완전히 평평해 트로피처럼 **가까이서 보는 소품**에서는 판지로 보인다.
    가운데 링만 원본 크기로 두면 단면이 렌즈꼴이 되고, 톱니(serration)는 가운데 링의
    실루엣에 그대로 남는다.
    """
    cx = sum(u for u, _ in outline) / len(outline)
    cy = sum(v for _, v in outline) / len(outline)
    rings = []
    for h, s in ((-thickness * 0.5, waist), (0.0, 1.0), (thickness * 0.5, waist)):
        rings.append([bm.verts.new(mat @ Vector((cx + (u - cx) * s,
                                                 cy + (v - cy) * s, h)))
                      for u, v in outline])
    n = len(outline)
    faces = [bm.faces.new(list(reversed(rings[0]))), bm.faces.new(rings[-1])]
    for lo, hi in zip(rings, rings[1:]):
        for i in range(n):
            j = (i + 1) % n
            faces.append(bm.faces.new((lo[i], lo[j], hi[j], hi[i])))
    for f in faces:
        f.smooth = smooth
    bmesh.ops.recalc_face_normals(bm, faces=faces)
    return faces


def leaf_outline(length, w_lead, w_trail, sweep=0.30, n=7):
    """노(paddle)형 아웃라인 - 가슴/배지느러미 공용. u = 밑동(0)→끝, v = 앞(+)/뒤(-)."""
    lead, trail = [], []
    for k in range(n):
        t = k / (n - 1.0)
        u = length * t
        shift = -sweep * length * t ** 1.7
        env = math.sin(math.pi * (0.12 + 0.86 * t)) ** 0.75
        lead.append((u, shift + w_lead * env))
        trail.append((u, shift - w_trail * env))
    return lead + list(reversed(trail))


def sail(z_front, z_back, y_base, h_front, h_peak, h_back, peak_t=0.42, n=7, sign=1.0):
    """등/뒷지느러미(돛) 아웃라인. 밑변은 체표 **안쪽**(y_base)에 두어 몸통에 파고들게 한다."""
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


def loft(bm, sections, sides=12, smooth=True):
    """단면 링을 이어 붙인 닫힌 몸통. sections = [(z, cy, rx, ry_top, ry_bot)].

    단면은 **상하 비대칭 타원**이다: 위쪽 반은 반경 ry_top, 아래쪽 반은 ry_bot.
    상어 머리의 밑면을 통째로 끌어올려(ry_bot 축소) **입이 벌어질 공간**을 만드는 것이
    이 함수의 유일한 존재 이유다 - 한 값짜리 타원으로는 안 나온다.
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


def tube(bm, path, radii, sides=8, up=(0.0, 1.0, 0.0), smooth=True):
    """임의 3D 경로를 따라 쓸어낸 닫힌 튜브(턱·다리·촉수·아가미 융기).

    radii 항목에 (ra, rb) 쌍을 주면 **타원 단면**이다 - ra 는 up×tan(가로), rb 는 그 법선
    (세로). 곰치처럼 옆으로 눌린 몸통이 이 한 줄로 나온다.
    mg.swept_tube 는 단면이 수평 XZ 원이라 세로로 선 줄기 전용이고, 수평으로 뻗는 구간이
    있으면 단면이 눕혀져 리본이 된다 - 그래서 여기서 프레임을 직접 만든다.
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


def ribbon(bm, stations, thickness, smooth=False):
    """두께 있는 띠(곰치의 등~꼬리~배 지느러미 주름). stations = [(밑점, 끝점, 옆방향)].

    지느러미가 **S 자 몸통을 따라 계속 이어지는** 물건이라 blade() 의 평면 아웃라인으로는
    만들 수 없다(아웃라인이 한 평면 위에 있어야 한다). 단면이 직사각형인 튜브로 두면
    몸통 곡선을 그대로 따라가면서 밑변은 체표 안쪽에 파묻힌다.
    """
    rings = []
    for base, tip, side in stations:
        h = Vector(side).normalized() * (thickness * 0.5)
        rings.append([bm.verts.new(p) for p in
                      (base + h, tip + h, tip - h, base - h)])
    faces = []
    for lo, hi in zip(rings, rings[1:]):
        for i in range(4):
            j = (i + 1) % 4
            faces.append(bm.faces.new((lo[i], lo[j], hi[j], hi[i])))
    faces.append(bm.faces.new(rings[0]))
    faces.append(bm.faces.new(list(reversed(rings[-1]))))
    for f in faces:
        f.smooth = smooth
    bmesh.ops.recalc_face_normals(bm, faces=faces)
    return faces


def ellipsoid(bm, center, semi, subdiv=2, smooth=True, squash_y=None):
    """아이코스피어 -> 타원체(머리·눈·외투막·빨판·혹). 극점 특이점이 없다.

    셰이딩 플래그는 **이번에 만든 껍질에만** 건다(bm.faces 전체를 돌면 앞서 만든
    플랫 파츠가 통째로 스무스로 덮인다 - marine.py 에서 실제로 겪었다).
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


def tooth(bm, pos, height, half_w, thick, sign=1.0, yaw=0.0, tilt=0.0, smooth=False):
    """이빨 하나 - 얇은 삼각 프리즘. sign=+1 위로 / -1 아래로, yaw 는 턱 호의 접선 방향.

    yaw 를 안 주면 턱 옆구리의 이빨이 폭 방향을 잘못 향해 **판이 옆으로 서 버린다**.
    말굽 호 (x=A sin a, z=B cos a) 의 접선은 a=0 에서 +X, a=90도에서 -Z 이므로 yaw = a 다.
    tilt(>0)는 이빨 끝을 목구멍 쪽(-Z)으로 눕힌다 - 상어/곰치 이빨의 갈고리 각도.
    """
    m = (Matrix.Translation(Vector(pos))
         @ Matrix.Rotation(yaw, 4, "Y")
         @ Matrix.Rotation(tilt, 4, "X"))
    blade(bm, [(-half_w, 0.0), (half_w, 0.0), (0.0, sign * height)], thick, m, smooth)


def welt(bm, path, r, sides=5, smooth=False):
    """흉터 융기 / 아가미 새열 - 체표 밖으로 살짝 나오는 두둑. 끝이 뾰족하게 잦아든다.

    **플랫 셰이딩**이라 법선이 끊겨 밝기 띠가 생긴다 - 멀리서 흉터를 읽게 하는 유일한
    수단이다(bamboo 의 마디 칼라와 같은 수법). 스무스로 두면 몸통에 그냥 녹는다.
    """
    n = len(path)
    radii = []
    for k in range(n):
        t = k / (n - 1.0)
        radii.append(max(r * math.sin(math.pi * (0.06 + 0.88 * t)) ** 0.55, r * 0.14))
    tube(bm, path, radii, sides=sides, smooth=smooth)


def finish(name, bm):
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
    return mg.new_object(name, bm)


def _interp(table, z):
    """단면표(z 내림차순)에서 z 의 단면 값을 선형 보간한다. 체표에 파츠를 붙일 때 쓴다."""
    if z >= table[0][0]:
        return tuple(table[0][1:])
    if z <= table[-1][0]:
        return tuple(table[-1][1:])
    for lo, hi in zip(table, table[1:]):
        if hi[0] <= z <= lo[0]:
            f = (z - lo[0]) / (hi[0] - lo[0])
            return tuple(p + (q - p) * f for p, q in zip(lo[1:], hi[1:]))
    return tuple(table[-1][1:])


# ══════════════════════════════════════════════════════════════════════════════
# a: 거대 상어 (메갈로돈급) - L 12m
# ══════════════════════════════════════════════════════════════════════════════
# (z, cy, rx, ry_top, ry_bot). 머리(z>3.7)의 ry_bot 이 급격히 작다 = **밑면을 끌어올려
# 입이 벌어질 공간을 비운 것**이고, 그 아래로 아래턱 말굽을 따로 단다. 최대 둘레는 z=1.6
# 에서 폭 2.42m x 높이 2.49m - 12m 짜리에 걸맞은 육중한 통이다(marine 돌고래는 0.79m).
SHARK = [
    (5.60, 0.30, 0.20, 0.18, 0.14),     # 뭉툭한 주둥이 끝(메갈로돈은 코가 넓고 짧다)
    (5.30, 0.32, 0.44, 0.38, 0.26),
    (4.90, 0.30, 0.70, 0.62, 0.36),
    (4.40, 0.24, 0.92, 0.86, 0.46),     # 눈·입꼬리
    (3.70, 0.12, 1.08, 1.06, 0.70),
    (2.80, 0.04, 1.17, 1.22, 0.98),     # 아가미
    (1.60, 0.00, 1.21, 1.31, 1.18),     # 최대 둘레
    (0.40, 0.00, 1.17, 1.27, 1.16),
    (-0.90, 0.00, 1.01, 1.12, 1.00),
    (-2.20, 0.00, 0.79, 0.88, 0.76),
    (-3.30, 0.00, 0.57, 0.64, 0.54),
    (-4.20, 0.02, 0.37, 0.44, 0.34),
    (-4.85, 0.04, 0.22, 0.30, 0.22),    # 미병
    (-5.28, 0.06, 0.13, 0.22, 0.15),
]


def shark_surface(z, a, k=1.0):
    """z 단면의 각도 a 위치의 체표 점. a=0 이 +X 옆구리, a=pi/2 가 등."""
    cy, rx, ryt, ryb = _interp(SHARK, z)
    ry = ryt if math.sin(a) >= 0.0 else ryb
    return Vector((rx * math.cos(a) * k, cy + ry * math.sin(a) * k, z))


def _jaw_arc(n, ax, bz, z0, y0, drop, span=1.55):
    """말굽(U) 턱 호. 앞(+Z) 이 둥글고 뒤로 벌어진다. 반환 [(점, 접선 yaw)]."""
    out = []
    for k in range(n):
        a = -span + 2.0 * span * k / (n - 1.0)
        out.append((Vector((ax * math.sin(a), y0 - drop * (1.0 - math.cos(a)),
                            z0 + bz * math.cos(a))), a))
    return out


def build_a(rng):
    body = bmesh.new()
    loft(body, SHARK, sides=16, smooth=True)
    ellipsoid(body, (0.0, 0.30, 5.56), (0.22, 0.20, 0.17), subdiv=2)   # 주둥이 마감
    for sx in (-1.0, 1.0):                                             # 눈
        # [2차] 0.13 은 체표에 파묻혀 3/4 컷에서 점 하나였다 - 밖으로 밀어 부풀린다.
        ellipsoid(body, (sx * 0.78, 0.55, 4.62), (0.17, 0.16, 0.17), subdiv=2)

    # 위턱 말굽(구개) - 몸통 머리 밑면 바로 아래에 걸친다. 이빨이 여기 매달린다.
    up_arc = _jaw_arc(13, 0.78, 1.58, 3.60, -0.05, 0.40)
    tube(body, [p for p, _ in up_arc], [0.125] * 13, sides=8)
    # 아래턱 말굽 - 크게 떨궈 **벌어진 입**을 만든다(앞쪽 간격 0.89m).
    # [2차] 반경 0.15 는 옆에서 "쇠막대"로 보였다 - 턱은 근육 덩어리라야 위압적이다.
    lo_arc = _jaw_arc(13, 0.72, 1.45, 3.62, -1.25, -0.38)
    tube(body, [p for p, _ in lo_arc], [(0.185, 0.235)] * 13, sides=8)
    ellipsoid(body, (0.0, -1.34, 4.45), (0.52, 0.21, 0.60), subdiv=2)   # 턱 밑살
    # 목구멍 - 이게 없으면 입 안쪽으로 몸통 내부가 그대로 뚫려 보인다.
    ellipsoid(body, (0.0, -0.58, 3.30), (0.74, 0.52, 0.88), subdiv=2)

    # 아가미 5쌍. 체표를 따라 내려가는 융기 - 상어를 상어로 읽게 하는 2순위 신호다
    # (1순위는 등지느러미). 플랫 셰이딩이라 거리에서 밝기 띠로 남는다.
    for z in (1.95, 2.35, 2.75, 3.15, 3.50):
        for sx in (-1.0, 1.0):
            path = []
            for k in range(6):
                a = -0.88 + 1.52 * k / 5.0
                p = shark_surface(z - 0.10 * math.sin(a), a, 0.985)
                path.append(Vector((p.x * sx, p.y, p.z)))
            welt(body, path, 0.075)

    # 흉터. 오른쪽 옆구리에 갈퀴 자국 3줄(같은 방향 평행선 = "물어뜯긴 자국"으로 읽힌다),
    # 왼쪽에 긴 상처 2줄, 등에 가로 상처 1줄. rng 로 길이·각도를 흔든다.
    for i in range(3):
        z0 = 1.85 - 0.42 * i + rng.uniform(-0.10, 0.10)
        path = [shark_surface(z0 - 1.30 * t, 0.62 - 1.05 * t, 0.99)
                for t in [k / 5.0 for k in range(6)]]
        welt(body, path, 0.070 + rng.uniform(-0.008, 0.012))
    for i in range(2):
        z0 = 0.30 - 1.30 * i + rng.uniform(-0.15, 0.15)
        path = [Vector((-p.x, p.y, p.z)) for p in
                [shark_surface(z0 - 1.85 * t, -0.30 + 1.15 * t, 0.99)
                 for t in [k / 5.0 for k in range(6)]]]
        welt(body, path, 0.062 + rng.uniform(-0.006, 0.010))
    path = [shark_surface(-1.05 + 0.55 * math.sin(t * math.pi), 0.95 + 1.35 * (t - 0.5), 0.99)
            for t in [k / 6.0 for k in range(7)]]
    welt(body, path, 0.066)
    body_obj = finish("body", body)

    fin = bmesh.new()
    # 1등지느러미 - 등 위로 1.55m 솟는 거대한 삼각 돛. 뒷가장자리에 **물어뜯긴 V 노치**.
    blade(fin, [(2.35, 1.00), (1.85, 1.75), (1.35, 2.35), (0.92, 2.72), (0.55, 2.86),
                (0.28, 2.60), (0.62, 2.22), (0.16, 2.05), (-0.25, 1.62), (-0.72, 1.28),
                (-1.15, 1.05), (-0.80, 0.90)],
          0.16, VERT, smooth=True)
    # 2등지느러미 / 뒷지느러미 - 작다. 있고 없고가 실루엣의 "완성도"를 가른다.
    blade(fin, sail(-2.95, -3.80, 0.55, 0.10, 0.42, 0.08, peak_t=0.35), 0.09, VERT)
    blade(fin, sail(-3.10, -3.90, -0.48, 0.08, 0.34, 0.06, peak_t=0.35, sign=-1.0),
          0.09, VERT)
    # 가슴지느러미 - 낫처럼 뒤로 젖힌 큰 날개. 좌우 끝 사이 약 5m.
    pect = Matrix.Translation((0.92, -0.55, 1.90)) \
        @ Matrix.Rotation(math.radians(-40.0), 4, "Y") \
        @ Matrix.Rotation(math.radians(-24.0), 4, "Z") @ HORZ
    blade_pair(fin, leaf_outline(1.95, 0.62, 0.48, sweep=0.34, n=8), 0.11, pect, smooth=True)
    # 배지느러미 - 작은 한 쌍.
    pelv = Matrix.Translation((0.48, -0.72, -1.85)) \
        @ Matrix.Rotation(math.radians(-22.0), 4, "Y") \
        @ Matrix.Rotation(math.radians(-40.0), 4, "Z") @ HORZ
    blade_pair(fin, leaf_outline(0.82, 0.30, 0.24, sweep=0.26, n=6), 0.07, pelv)
    # 꼬리 - 위엽이 길고 아래엽이 짧은 **비대칭(heterocercal)** 꼬리. 상어의 서명이다.
    # 위엽 뒷가장자리에도 노치를 하나 파 흉터 이야기를 잇는다.
    # [2차] 1차 꼬리는 위엽이 가늘고 아래엽이 거의 없어 12m 몸에 비해 빈약했다.
    #       두 엽을 다 살찌우고 위엽의 어중간한 중간 노치는 뺀다(흉터는 등지느러미가 맡는다).
    blade(fin, [(-4.55, 0.30), (-5.00, 1.05), (-5.48, 1.85), (-5.95, 2.48), (-6.42, 2.72),
                (-6.30, 2.24), (-5.92, 1.62), (-5.55, 0.92), (-5.32, 0.22),
                (-5.70, -0.28), (-6.05, -0.85), (-6.20, -1.30),
                (-5.86, -1.20), (-5.35, -0.82), (-4.90, -0.42), (-4.60, -0.22)],
          0.14, VERT, smooth=True)

    # 이빨 - 위 15 + 위 보조열 13 + 아래 13. 벌어진 입 안에 **두 줄**이 마주 보는 것이
    # "보스"와 "큰 물고기"를 가른다.
    for p, a in _jaw_arc(15, 0.76, 1.54, 3.60, -0.15, 0.40, span=1.42):
        tooth(fin, p, 0.34, 0.145, 0.055, sign=-1.0, yaw=a, tilt=0.22)
    for p, a in _jaw_arc(13, 0.66, 1.34, 3.42, -0.06, 0.34, span=1.36):
        tooth(fin, p, 0.22, 0.105, 0.045, sign=-1.0, yaw=a, tilt=0.30)
    for p, a in _jaw_arc(13, 0.70, 1.42, 3.62, -1.10, -0.38, span=1.40):
        tooth(fin, p, 0.30, 0.130, 0.050, sign=1.0, yaw=a, tilt=-0.20)
    fin_obj = finish("fin", fin)
    return body_obj, fin_obj


# ══════════════════════════════════════════════════════════════════════════════
# b: 대왕 곰치 - L 7m, S 자 몸통 / 큰 머리 + 벌린 턱 / 등~꼬리 지느러미 주름
# ══════════════════════════════════════════════════════════════════════════════
MORAY_Z0, MORAY_LEN, MORAY_AMP = 2.35, 6.20, 1.16

# 머리 로프트 (z, cy, rx, ry_top, ry_bot). ry_bot 이 앞쪽에서 급격히 작다 = 위턱만 남기고
# 아래를 비운 것. 곰치는 옆으로 눌려 있어 rx < ry_top 이다.
# [2차] 1차 머리는 몸통과 같은 굵기라 "통나무"로 보였다 - 곰치의 정체성은 **몸통보다
# 굵은 머리와 벌린 입**이다. 머리를 앞으로 0.24m 늘이고 턱 근육(z=2.80)을 몸통 최대
# 둘레보다 크게 잡아 머리가 실루엣을 지배하게 한다.
MORAY_HEAD = [
    (3.86, 0.155, 0.070, 0.080, 0.048),
    (3.66, 0.150, 0.135, 0.150, 0.080),
    (3.40, 0.130, 0.205, 0.235, 0.110),
    (3.10, 0.090, 0.278, 0.340, 0.150),
    (2.80, 0.045, 0.330, 0.440, 0.230),   # 턱 근육 - 여기가 제일 굵다
    (2.55, 0.015, 0.330, 0.455, 0.320),
    (2.35, 0.000, 0.310, 0.430, 0.330),
]


def moray_center(t):
    """중심선. x = A sin(pi t) sin(tau t) 는 **양 끝에서 x' = 0** 이라 머리가 +Z 를 보고
    꼬리가 -Z 로 곧게 빠진다. sin(tau t) 만 쓰면 t=0 에서 기울기가 남아 머리가 비뚤어진다."""
    return Vector((MORAY_AMP * math.sin(math.pi * t) * math.sin(math.tau * t),
                   -0.16 * math.sin(math.pi * t) ** 1.4,
                   MORAY_Z0 - MORAY_LEN * t))


def moray_radii(t):
    """(가로, 세로) 반경. 세로가 1.4배 = 옆으로 눌린 뱀장어 단면.

    [2차] 지수를 0.50/0.42 -> 0.62/0.52 로 올려 **뒤로 갈수록 빨리 가늘어지게** 했다.
    1차는 꼬리까지 굵기가 남아 통나무로 보였다 - 가늘어져야 머리가 커 보인다.
    """
    return (0.300 * (1.0 - t) ** 0.62 + 0.012, 0.415 * (1.0 - t) ** 0.52 + 0.014)


def _moray_frame(t):
    """중심선의 접선/옆방향. 지느러미 주름의 두께 방향을 잡는다."""
    d = 1e-3
    tan = (moray_center(min(1.0, t + d)) - moray_center(max(0.0, t - d))).normalized()
    side = Vector((0.0, 1.0, 0.0)).cross(tan)
    if side.length < 1e-4:
        side = Vector((1.0, 0.0, 0.0))
    return tan, side.normalized()


def _moray_jaw(a):
    """아래턱(하악지) 위의 점. a=0 이 앞끝, |a|=1 이 뒤 경첩. 이빨도 같은 식을 쓴다."""
    th = a * 1.70
    return Vector((0.155 * math.sin(th),
                   -0.50 + 0.155 * (1.0 - math.cos(th)),
                   2.60 + 1.14 * math.cos(th)))


def build_b(rng):
    body = bmesh.new()
    loft(body, MORAY_HEAD, sides=12, smooth=True)
    NB = 30
    ts = [k / (NB - 1.0) for k in range(NB)]
    tube(body, [moray_center(t) for t in ts], [moray_radii(t) for t in ts],
         sides=12, smooth=True)

    # 아래턱 - V 자 한 쌍의 하악지. 앞(+Z)에서 붙고 뒤 경첩(z=2.45)까지 벌어진다.
    # 앞끝 간격 0.48m(정규화 후 0.44m) - 곰치는 이 벌린 입이 곧 정체성이다.
    jaw = [_moray_jaw(-1.0 + 2.0 * k / 12.0) for k in range(13)]
    tube(body, jaw, [(0.058, 0.108)] * 13, sides=7, smooth=True)
    ellipsoid(body, (0.0, -0.20, 2.70), (0.22, 0.19, 0.42), subdiv=2)   # 목구멍
    for sx in (-1.0, 1.0):                                             # 눈
        ellipsoid(body, (sx * 0.190, 0.215, 3.38), (0.068, 0.066, 0.070), subdiv=2)
        # 관 모양 콧구멍 - 곰치만 있는 서명 디테일.
        tube(body, [Vector((sx * 0.062, 0.150, 3.72)), Vector((sx * 0.072, 0.182, 3.80)),
                    Vector((sx * 0.078, 0.202, 3.85))],
             [0.026, 0.022, 0.019], sides=5, smooth=True)
    body_obj = finish("body", body)

    fin = bmesh.new()
    # 등 지느러미 주름 - 목 뒤부터 꼬리 끝까지 **끊기지 않고** 이어진다. 높이를 잔물결로
    # 흔들어(주름) 밋밋한 판이 안 되게 한다. 꼬리 쪽에서 다시 커지며 노 모양 꼬리가 된다.
    NF = 40
    top, bot = [], []
    for k in range(NF):
        t = 0.02 + 0.975 * k / (NF - 1.0)
        c = moray_center(t)
        ra, rb = moray_radii(t)
        _, side = _moray_frame(t)
        h = 0.27 * math.sin(math.pi * min(1.0, t * 1.06)) ** 0.32
        h *= 1.0 + 0.26 * math.sin(t * 31.0)                 # 주름
        h *= 1.0 + 1.70 * max(0.0, t - 0.74) / 0.26          # 꼬리 노(2차: 0.85->1.70)
        top.append((c + Vector((0.0, rb * 0.55, 0.0)),
                    c + Vector((0.0, rb + h, 0.0)), side))
        if t > 0.44:
            hb = 0.19 * math.sin(math.pi * min(1.0, (t - 0.40) * 1.55)) ** 0.38
            hb *= 1.0 + 0.28 * math.sin(t * 27.0)
            hb *= 1.0 + 1.80 * max(0.0, t - 0.74) / 0.26
            bot.append((c - Vector((0.0, rb * 0.55, 0.0)),
                        c - Vector((0.0, rb + hb, 0.0)), side))
    ribbon(fin, top, 0.055)
    ribbon(fin, bot, 0.048)

    # 이빨 - 위턱 가장자리 좌우 9개씩, 아래턱 좌우 8개씩. 길고 바늘 같고 뒤로 굽는다.
    for sx in (-1.0, 1.0):
        for k in range(9):
            z = 3.80 - 0.105 * k
            cy, rx, _, ryb = _interp(MORAY_HEAD, z)
            tooth(fin, (sx * rx * 0.60, cy - ryb + 0.012, z),
                  0.130 - 0.005 * k, 0.032, 0.021, sign=-1.0,
                  yaw=sx * 0.10, tilt=0.30)
        for k in range(8):
            p = _moray_jaw(0.12 + 0.11 * k)
            tooth(fin, (sx * abs(p.x), p.y + 0.095, p.z),
                  0.112 - 0.005 * k, 0.028, 0.019, sign=1.0,
                  yaw=sx * 0.12, tilt=-0.26)
    fin_obj = finish("fin", fin)
    return body_obj, fin_obj


# ══════════════════════════════════════════════════════════════════════════════
# c: 심해 괴수(문어형) - 외투막 3.5m, 다리 포함 폭 9m
# ══════════════════════════════════════════════════════════════════════════════
def _sucker_row(bm, path, radii, count, t0, t1, scale, subdiv=1, face=(0.0, -1.0, 0.0)):
    """다리를 따라 빨판 융기를 박는다. face 를 접선에 수직으로 투영해 붙일 면을 정한다.

    [2차] 트로피(꼬인 촉수)는 판 위에 눕혀 놓으므로 아랫면에 박으면 **전부 판에 가려**
    울퉁불퉁한 소시지로만 보인다 - face=+Y 로 위쪽에 박아야 빨판으로 읽힌다.
    """
    n = len(path)
    for k in range(count):
        t = t0 + (t1 - t0) * (k / max(count - 1.0, 1.0))
        f = t * (n - 1)
        i0 = min(int(f), n - 2)
        w = f - i0
        c = path[i0].lerp(path[i0 + 1], w)
        r = radii[i0] * (1.0 - w) + radii[i0 + 1] * w
        tan = (path[min(i0 + 1, n - 1)] - path[max(i0 - 1, 0)]).normalized()
        d = Vector(face)
        d = d - tan * d.dot(tan)
        if d.length < 1e-3:
            d = Vector((1.0, 0.0, 0.0)).cross(tan)
        d.normalize()
        sr = r * scale
        ellipsoid(bm, c + d * (r * 0.70), (sr, sr, sr), subdiv=subdiv, smooth=False)


def build_c(rng):
    body = bmesh.new()
    # 외투막 - 뒤(-Z)로 뾰족한 거대 자루. squash_y 로 아랫배를 눌러 눌린 주머니로.
    ellipsoid(body, (0.0, 2.34, -0.90), (1.04, 1.14, 1.70), subdiv=3, squash_y=0.86)
    ellipsoid(body, (0.0, 2.28, -2.22), (0.46, 0.48, 0.46), subdiv=2)
    # 머리(눈이 붙는 자리) - 외투막보다 낮고 앞(+Z).
    ellipsoid(body, (0.0, 2.02, 1.02), (0.94, 0.82, 0.95), subdiv=3)
    for sx in (-1.0, 1.0):                       # **큰 눈** - 심해 괴수의 1순위 신호
        # [2차] 1차는 눈 중심이 머리 타원체 **안**이라 통째로 파묻혀 안 보였다.
        #       중심을 표면 밖으로 빼야 비로소 눈알이 튀어나온 실루엣이 된다.
        ellipsoid(body, (sx * 0.88, 2.44, 1.16), (0.44, 0.43, 0.45), subdiv=3)
        ellipsoid(body, (sx * 1.06, 2.52, 1.30), (0.22, 0.21, 0.22), subdiv=2)
    # 외투막 혹 - 괴수다운 울퉁불퉁함. rng 로 흩뿌린다.
    # [3차] 반경 0.10~0.19 는 1m 짜리 외투막에서 한 픽셀도 안 남아 매끈한 풍선이었다
    #       ("멀리서 안 보이는 디테일은 없는 것과 같다" - Tools/blender/README 함정 10).
    #       반경을 배로 키우고 플랫 셰이딩으로 두어 각진 융기가 실루엣에 걸리게 한다.
    for _ in range(13):
        a = rng.uniform(0.0, math.tau)
        u = rng.uniform(-0.50, 0.88)
        sq = math.sqrt(max(0.0, 1.0 - u * u))
        c = Vector((1.04 * sq * math.cos(a), 2.34 + 1.14 * u,
                    -0.90 + 1.70 * sq * math.sin(a)))
        r = rng.uniform(0.20, 0.33)
        ellipsoid(body, c, (r, r * 0.72, r), subdiv=1, smooth=False)
    body_obj = finish("body", body)

    fin = bmesh.new()
    # 다리 8개. 입(z=0.95, y=1.45) 둘레에서 나와 바깥·아래로 뻗고 끝이 말려 올라간다.
    # reach 4.55 는 외투막 길이(3.5)의 1.3배 - marine_g 에서 배운 대로 이보다 짧으면
    # 통째로 "거미"로 읽힌다.
    NA = 8
    for i in range(NA):
        a = math.tau * (i + 0.5) / NA
        reach = 4.55 + 0.55 * math.sin(i * 1.7)
        curl = 0.55 + 0.42 * math.cos(i * 2.3)
        phase = i * 0.9
        path, radii = [], []
        NK = 13
        for k in range(NK):
            t = k / (NK - 1.0)
            spread = reach * math.sin(t * 1.42) / math.sin(1.42)
            y = 2.05 - 2.05 * (1.0 - math.cos(t * 1.95)) / (1.0 - math.cos(1.95))
            y += curl * t ** 3.0 + 0.24 * math.sin(t * 3.4 + phase) * t
            path.append(Vector((math.cos(a) * spread + 0.26 * math.sin(t * 2.6 + phase),
                                max(y, 0.06),
                                0.95 + math.sin(a) * spread * 1.02)))
            # [2차] 지수 0.80 은 밑동만 굵고 금세 가늘어져 "다리 8개"가 실처럼 보였다.
            radii.append(0.50 * (1.0 - t) ** 0.60 + 0.055)
        tube(fin, path, radii, sides=8, up=(0.0, 1.0, 0.0))
        _sucker_row(fin, path, radii, 10, 0.08, 0.92, 0.52)
    fin_obj = finish("fin", fin)
    return body_obj, fin_obj


# ══════════════════════════════════════════════════════════════════════════════
# trophy_a: 상어 이빨 표본 - 톱니 달린 거대 이빨 + 뼈 받침
# ══════════════════════════════════════════════════════════════════════════════
def _serrated(p0, p1, n, depth, out_sign):
    """두 점 사이의 절단날에 톱니를 넣는다. 상어 이빨을 상어 이빨로 만드는 유일한 특징이다.

    격점을 **번갈아** 법선 방향으로 밀어 톱니를 만든다. depth 를 변 길이의 1/6 이하로
    두면 볼록한 실루엣에서 자기교차가 원리적으로 불가능하다(면적 0 삼각형 사고 방지).
    """
    ax, ay = p0
    bx, by = p1
    dx, dy = bx - ax, by - ay
    L = math.hypot(dx, dy)
    nx, ny = (dy / L) * out_sign, (-dx / L) * out_sign
    pts = []
    for k in range(1, n + 1):
        t = k / (n + 1.0)
        d = depth if k % 2 == 1 else 0.0
        pts.append((ax + dx * t + nx * d, ay + dy * t + ny * d))
    return pts


def build_trophy_a(rng):
    body = bmesh.new()
    # [2차] 톱니 9개 x 깊이 13mm 는 변 길이의 1/3 이라 **화살촉/전나무**로 보였다.
    #       실제 메갈로돈 이빨의 톱니는 1~2mm 급이다 - 개수를 배로, 깊이를 절반으로.
    #       관부(crown)도 넓혀 뾰족한 첨탑이 아니라 **넓은 삼각날**이 되게 했다.
    apex = (0.0, 0.700)
    l_sh, r_sh = (-0.190, 0.286), (0.190, 0.286)
    outline = [(-0.190, 0.118), (-0.208, 0.238), l_sh]
    outline += _serrated(l_sh, apex, 17, 0.0060, -1.0)
    outline += [apex]
    outline += _serrated(apex, r_sh, 17, 0.0060, -1.0)
    outline += [r_sh, (0.208, 0.238), (0.190, 0.118),
                (0.062, 0.178), (-0.062, 0.178)]        # 두 갈래 뿌리 사이의 홈
    lens(body, outline, 0.056, Matrix.Identity(4), waist=0.76)
    body_obj = finish("body", body)

    fin = bmesh.new()
    # 뼈 받침: 타원 밑판 + 척추뼈처럼 잘록한 기둥 + 이빨 뿌리를 무는 집게 2개.
    ellipsoid(fin, (0.0, 0.042, 0.0), (0.215, 0.042, 0.152), subdiv=2, smooth=False)
    tube(fin, [Vector((0.0, 0.060, 0.0)), Vector((0.0, 0.098, 0.0)),
               Vector((0.0, 0.132, 0.0)), Vector((0.0, 0.170, 0.0))],
         [0.098, 0.062, 0.058, 0.086], sides=12, smooth=False)
    for sx in (-1.0, 1.0):
        tube(fin, [Vector((sx * 0.066, 0.150, 0.0)), Vector((sx * 0.086, 0.232, 0.0)),
                   Vector((sx * 0.082, 0.300, 0.0))],
             [(0.030, 0.026), (0.026, 0.022), (0.020, 0.017)], sides=6, smooth=False)
    fin_obj = finish("fin", fin)
    return body_obj, fin_obj


# ══════════════════════════════════════════════════════════════════════════════
# trophy_b: 곰치 턱뼈 표본 - 벌린 위/아래 턱 + 바늘 이빨, 기둥 받침
# ══════════════════════════════════════════════════════════════════════════════
def _ramus(sx, y_tip, y_hinge, z_tip, z_hinge, x_hinge, bow=0.0, n=7):
    """턱 한쪽 가지(하악지/상악지). 앞끝(+Z, x=0)에서 뒤 경첩으로 벌어진다.

    bow 는 가운데를 위/아래로 휘게 한다 - 1차 렌더에서 턱이 **곧은 쇠막대 두 개**로
    보였다. 실제 턱뼈는 활처럼 굽어 있고, 그 굽음이 "뼈"와 "막대"를 가른다.
    """
    out = []
    for k in range(n):
        t = k / (n - 1.0)
        out.append(Vector((sx * x_hinge * t ** 0.85,
                           y_tip + (y_hinge - y_tip) * t + bow * math.sin(math.pi * t),
                           z_tip + (z_hinge - z_tip) * t)))
    return out


def build_trophy_b(rng):
    body = bmesh.new()
    UP = dict(y_tip=0.592, y_hinge=0.524, z_tip=0.238, z_hinge=-0.118, x_hinge=0.082)
    LO = dict(y_tip=0.428, y_hinge=0.500, z_tip=0.248, z_hinge=-0.118, x_hinge=0.076)
    for sx in (-1.0, 1.0):
        for spec, ra, bow in ((UP, (0.022, 0.032), 0.020), (LO, (0.021, 0.030), -0.024)):
            p = _ramus(sx, bow=bow, **spec)
            tube(body, p, [(ra[0] * (1.0 - 0.50 * k / 6.0), ra[1] * (1.0 - 0.42 * k / 6.0))
                           for k in range(7)][::-1], sides=6, smooth=True)
    ellipsoid(body, (0.0, 0.546, -0.104), (0.078, 0.064, 0.088), subdiv=2)   # 두개골 뒤
    # 바늘 이빨 - 위턱 아래로, 아래턱 위로. 벌린 틈(0.16m)에서 마주 본다.
    for sx in (-1.0, 1.0):
        for k in range(7):
            t = 0.06 + 0.86 * k / 6.0
            pu = Vector((sx * UP["x_hinge"] * t ** 0.85,
                         UP["y_tip"] + (UP["y_hinge"] - UP["y_tip"]) * t
                         + 0.020 * math.sin(math.pi * t) - 0.016,
                         UP["z_tip"] + (UP["z_hinge"] - UP["z_tip"]) * t))
            tooth(body, pu, 0.046, 0.0085, 0.0072, sign=-1.0, yaw=sx * 0.14, tilt=0.26)
            pl = Vector((sx * LO["x_hinge"] * t ** 0.85,
                         LO["y_tip"] + (LO["y_hinge"] - LO["y_tip"]) * t
                         - 0.024 * math.sin(math.pi * t) + 0.015,
                         LO["z_tip"] + (LO["z_hinge"] - LO["z_tip"]) * t))
            tooth(body, pl, 0.040, 0.0080, 0.0068, sign=1.0, yaw=sx * 0.14, tilt=-0.24)
    body_obj = finish("body", body)

    fin = bmesh.new()
    ellipsoid(fin, (0.0, 0.028, 0.0), (0.168, 0.028, 0.132), subdiv=2, smooth=False)
    tube(fin, [Vector((0.0, 0.040, -0.104)), Vector((0.0, 0.240, -0.104)),
               Vector((0.0, 0.430, -0.108)), Vector((0.0, 0.512, -0.110))],
         [0.046, 0.030, 0.028, 0.034], sides=10, smooth=False)
    # 고정 밴드 - 두개골 뒤를 감싸 잡는다.
    tube(fin, [Vector((-0.088, 0.548, -0.098)), Vector((0.0, 0.606, -0.098)),
               Vector((0.088, 0.548, -0.098))],
         [0.016, 0.016, 0.016], sides=6, smooth=False)
    fin_obj = finish("fin", fin)
    return body_obj, fin_obj


# ══════════════════════════════════════════════════════════════════════════════
# trophy_c: 촉수 표본 - 나무판 위에 꼬아 놓은 촉수 + 고정 밴드
# ══════════════════════════════════════════════════════════════════════════════
def build_trophy_c(rng):
    body = bmesh.new()
    NK = 26
    path, radii = [], []
    for k in range(NK):
        t = k / (NK - 1.0)
        ang = t * math.tau * 1.75
        rad = 0.196 * (1.0 - 0.68 * t)
        y = 0.066 + 0.048 * t + 0.115 * max(0.0, t - 0.66) / 0.34   # 끝이 들린다
        path.append(Vector((rad * math.cos(ang), y, rad * math.sin(ang))))
        # [3차] 밑동:끝 = 2.7:1 은 굵기가 거의 안 변해 **밧줄**로 보였다. 5:1 로 벌려
        #       "밑동이 굵고 끝이 가는 팔"이라는 촉수의 기본 신호를 세운다.
        radii.append(0.058 * (1.0 - 0.90 * t) + 0.005)
    tube(body, path, radii, sides=8, smooth=True)
    # 빨판은 **위쪽**에 박는다 - 아랫면에 박으면 판에 가려 통째로 안 보인다(2차 반성).
    _sucker_row(body, path, radii, 16, 0.03, 0.95, 0.62, face=(0.0, 1.0, 0.0))
    body_obj = finish("body", body)

    fin = bmesh.new()
    # 나무판(진열대) - 낮은 타원 슬래브. 플랫 셰이딩이라 판재로 읽힌다.
    ellipsoid(fin, (0.0, 0.030, 0.0), (0.322, 0.030, 0.228), subdiv=2, smooth=False)
    # 고정 밴드 2줄 - 촉수 **위로** 넘어가 판에 눌러 묶는다(1차는 옆에 세워둔 막대였다).
    for zb, w, hh in ((0.150, 0.245, 0.148), (-0.165, 0.230, 0.140)):
        tube(fin, [Vector((-w, 0.034, zb)), Vector((-w * 0.62, hh * 0.86, zb)),
                   Vector((0.0, hh, zb)), Vector((w * 0.62, hh * 0.86, zb)),
                   Vector((w, 0.034, zb))],
             [(0.017, 0.011)] * 5, sides=6, smooth=False)
    fin_obj = finish("fin", fin)
    return body_obj, fin_obj


# ══════════════════════════════════════════════════════════════════════════════
# 파이프라인
# ══════════════════════════════════════════════════════════════════════════════
# (이름, 시드, 빌더, 예산키, 정규화 축, 목표 치수, 몸통색, fin 색, 정렬, 메모)
SPECS = [
    # 프리뷰 색은 **두 `o` 그룹이 눈으로 갈리도록** 일부러 대비를 크게 준다(OBJ 로 안 나간다).
    ("boss_a", 79001, build_a, "large_structure", "z", 12.00,
     (0.26, 0.30, 0.35), (0.90, 0.88, 0.80), "bbox", "megalodon boss"),
    ("boss_b", 79002, build_b, "large_structure", "z", 7.00,
     (0.30, 0.34, 0.20), (0.86, 0.80, 0.42), "bbox", "giant moray boss"),
    ("boss_c", 79003, build_c, "large_structure", "x", 9.00,
     (0.34, 0.14, 0.30), (0.82, 0.44, 0.40), "bbox", "abyss kraken boss"),
    ("boss_trophy_a", 79004, build_trophy_a, "small_prop", "y", 0.72,
     (0.94, 0.92, 0.84), (0.44, 0.40, 0.34), "ground", "megalodon tooth trophy"),
    ("boss_trophy_b", 79005, build_trophy_b, "small_prop", "y", 0.66,
     (0.94, 0.92, 0.84), (0.40, 0.30, 0.22), "ground", "moray jaw trophy"),
    ("boss_trophy_c", 79006, build_trophy_c, "small_prop", "x", 0.62,
     (0.88, 0.72, 0.70), (0.40, 0.28, 0.18), "ground", "kraken tentacle trophy"),
]

AXIS = {"x": 0, "y": 1, "z": 2}


def normalize(objs, axis, target):
    """조립체 전체를 **균일 배율**로 키워 지정 축 치수를 정확히 맞춘다.

    fit_size 는 축마다 따로 늘려 비례를 깨므로 생물에는 쓸 수 없다(옆으로 눌린 곰치가
    통통해진다). 균일 배율이라 파츠 사이 상대 위치도 그대로다.
    """
    lo, hi = mg.union_bbox(objs)
    ext = (hi - lo)[AXIS[axis]]
    s = target / max(ext, mg.EPS)
    for o in objs:
        o.data.transform(Matrix.Scale(s, 4))
    return s


def produce(name, seed, builder, budget_key, axis, target, body_col, fin_col, align, note):
    mg.reset_scene()
    rng = mg.Rng(seed)
    body, fin = builder(rng)
    body.name = body.data.name = "body"
    fin.name = fin.data.name = "fin"
    objs = [body, fin]                          # o 그룹 순서 고정: body 먼저

    scale = normalize(objs, axis, target)
    for o in objs:
        mg.triangulate(o)
    lo, hi = mg.union_bbox(objs)
    span = max(hi - lo)
    mg.box_uv(body, tile=max(0.05, span * 0.42))
    mg.box_uv(fin, tile=max(0.05, span * 0.30))
    mg.assign_material(body, mg.preview_material("pv_" + name + "_body",
                                                 base_color=body_col, roughness=0.55))
    mg.assign_material(fin, mg.preview_material("pv_" + name + "_fin",
                                                base_color=fin_col, roughness=0.50))

    budget = mg.TRI_BUDGET[budget_key]
    # 보스는 align="bbox"(mgbuild 에 중심 정렬 옵션이 없다 - 파일 머리말 "원점 규약").
    # 밑면 y=0 은 그대로 두고 보정 오프셋을 아래에서 실측해 보고한다.
    # 트로피는 진열용 접지 소품이라 align="ground" - 보정값이 없다.
    stats = mg.enforce_contract_group(objs, tri_budget=budget, tri_floor=200,
                                      name=name, align=align)

    out = os.path.join(mg.MODELS_DIR, name + ".obj")
    mg.export_obj(objs, out)
    stats = mg.verify_obj_file(out, stats)
    mg.inject_usemtl(out)                       # 반드시 verify 뒤(호출 순서 계약)

    # ── pivot 실측 ──
    if align == "ground":
        pivot = Vector((0.0, 0.0, 0.0))         # 접지 소품 - 보정 없음
    else:
        blo, bhi = mg.bbox(body)                # body(몸통) bbox 중심 = 회전 중심
        pivot = (blo + bhi) * 0.5
    blo, bhi = mg.bbox(body)
    stats["pivot"] = (pivot.x, pivot.y, pivot.z)
    stats["scale"] = scale
    stats["body_size"] = tuple(bhi - blo)

    png = os.path.join(mg.PREVIEW_DIR, "_boss", name + ".png")
    mg.turntable(objs, png, title=name, stats=stats, px=440, samples=16,
                 notes=f"seed {seed} / {note}")
    mg.report(stats)
    return stats


def main():
    only = set(sys.argv[1:])
    rows = []
    for spec in SPECS:
        if only and spec[0] not in only:
            continue
        rows.append((spec[0], spec[1], produce(*spec)))

    print("BOSS_MANIFEST")
    for name, seed, st in rows:
        s = st["size"]
        b = st["body_size"]
        print(f"  {name}  seed={seed}  {st['tris']}/{st['budget']}tri  "
              f"{s[0]:.3f} x {s[1]:.3f} x {s[2]:.3f} m  parts={st['parts']}  "
              f"body {b[0]:.2f}x{b[1]:.2f}x{b[2]:.2f}")
    print("BOSS_PIVOT  (게임 코드: model.localPosition = -pivot 로 몸통 중심을 원점에)")
    for name, seed, st in rows:
        p = st["pivot"]
        print(f"  {name}  pivot=({p[0]:+.4f}, {p[1]:+.4f}, {p[2]:+.4f}) m"
              f"  align={st['align']}  bbox_h={st['size'][1]:.3f}")
    print(f"[boss] 완료 - {len(rows)}종")


if __name__ == "__main__":
    main()
