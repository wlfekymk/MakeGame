#!/usr/bin/env python3
"""
야자수 3종 - palm_a / palm_b / palm_c.

    python3 Tools/blender/units/palm.py

산출물 (이 세 파일만 건드린다)
  Assets/_Project/Resources/Models/palm_a.obj   (+ b, c)
  Tools/blender/_preview/palm_a.png             (+ b, c)  ← 저장소에 넣지 않는다

────────────────────────────────────────────────────────────────────────────────
크기의 근거 (게임 코드 실측 - 이번 배치에서 코드는 못 고치므로 여기에 맞춘다)

  IslandMeshGenerator.cs:1721 CreatePalm
      height       = 4.6 ~ 7.6 m            (줄기 마디 3개의 합)
      baseRadius   = 0.266 ~ 0.388 m        (외접 반지름)
      leanStart    = 1 ~ 5도, leanStep = 4 ~ 9도  → 마지막 마디 최대 23도
      frondLength  = 2.2 ~ 3.4 m,  잎 5장 (안쪽 0.44L + 바깥 0.64L 로 꺾임)
      잎 폭        = 0.42 m (안쪽 마디) / 0.28 m (바깥 마디)
  IslandMeshGenerator.cs:1683-1704
      PalmTrunkSegments 3 / PalmTrunkSides 8 / PalmFrondCount 5
  IslandMeshGenerator.cs:2248 GetPalmTrunkPrismMesh
      마디 1개 = 28삼각형 (옆면 16 + 캡 12) → 그루당 렌더러 13개 / 204삼각형
      줄기 테이퍼: segmentRadius = Lerp(baseRadius, baseRadius*0.62, t), t=(i+0.5)/3
      → 보이는 밑동 지름 = 2 × baseRadius × 0.937 = 0.50 ~ 0.73 m
  색·텍스처
      줄기 PalmBarkColor(IslandMeshGenerator.cs:2546, Driftwood 명도 0.93 채도 1.20 = #885E33)
           + "bark" 텍스처 (IslandMeshGenerator.cs:898)
      잎   FrondGreen #6BA83F (StructureVisualBuilder.cs:38) + "frond" 텍스처 (:911)
  개수  IslandMeshGenerator.cs:964  palmCount = clamp(radius × 0.12, 4, 16)

  세 변종의 높이·밑동 굵기·잎 길이는 전부 위 구간 안에 있다. 콜라이더는 CreatePalm 경로에
  아예 없으므로(CreatePart = 프리미티브 미경유) 치수 변경 위험은 "보이는 크기"뿐이다.

형태 규칙 (프리미티브 원통 다발과 갈라지는 이유)
  1. 줄기가 **한 장의 스윕 튜브**다. 마디 3개를 이어 붙이지 않으므로 이음매 단차가 없고,
     휨이 연속 곡선이다(게임 코드는 3마디라 꺾인 관절이 보인다).
  2. 잎자국 링. 링 정점 자체를 리지/그루브로 번갈아 놓아 **공짜로** 마디 굴곡을 만든다
     (별도 링을 추가하면 삼각형이 3배가 된다). 나선으로 살짝 비틀어 놓았다.
  3. 밑동 뿌리 보스 + 단면 비원형(3·5차 로브). 어느 각도에서도 원기둥으로 안 읽힌다.
  4. 잎이 **판자가 아니라 우상복엽**이다. 소엽(leaflet)을 메시로 잘라 실루엣을 만든다 -
     이 프로젝트에는 알파 컷아웃 셰이더 설정이 없어서(계약 4장) 알파로는 못 한다.
  5. 잎은 **두께 없는 단면 + 굽힌 양면**이다(mgbuild.make_double_sided).

파일 구조 (중요)
  OBJ 하나에 `o` 오브젝트 **2개**(<name>_trunk / <name>_crown)가 들어 있다.
  Unity OBJ 임포터는 `o` 를 자식 GameObject 로 만들고 상대 위치를 보존한다
  (IslandMeshGenerator.cs:2094 주석이 이미 이 동작을 전제한다).
  왜 한 메시로 안 합쳤나: 계약 4장에 따라 머티리얼은 런타임 코드가 만든다. 한 메시면
  머티리얼도 하나라 **줄기 갈색과 잎 초록이 한 색으로 뭉갠다**. 파일을 둘로 쪼개면
  왕관 OBJ 가 제 밑면을 y=0 으로 맞추면서 "줄기 꼭대기에 얹히는 오프셋"을 잃는다(계약 1장).
  → 파일 1개 / 오브젝트 2개 / 렌더러 2개. 지금 코드의 13개에서 6.5배 줄어든다.

원점 규약: **접지 중심**(mg.enforce_contract_group 의 align="ground" 기본값).
  1차 배치에서는 바운딩 박스 중심으로 맞췄는데, 휜 야자수는 크라운이 bbox 를 지배해서
  줄기 밑동이 원점에서 **최대 0.74m 밀려났다**(실측). 그러면 스폰 지점에 나무가 안 선다.
  지금은 밑동 링의 XZ 중심이 원점이다 - 배치 코드가 보정할 것이 없다.
  바운딩 박스는 기울기만큼 비대칭이다(그게 정상이다).
────────────────────────────────────────────────────────────────────────────────
"""

import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

import mgbuild as mg  # noqa: E402  (bpy 를 먼저 끌어온다 - bmesh 가 그 뒤에야 import 된다)
import bmesh  # noqa: E402
from mathutils import Matrix, Vector  # noqa: E402

# 시드는 여기 박아 둔다. 같은 시드 = 같은 메시(계약 5장). 2회 실행 md5 동일 확인함.
VARIANTS = [
    # 이름,     시드,   높이 m, 밑동반지름 m, 꼭대기 수평이동 m, 잎수, 잎길이 m, 잎 마디수
    ("palm_a", 30411, 4.75, 0.270, 0.22, 7, 2.35, 11),
    ("palm_b", 50123, 6.05, 0.320, 0.46, 9, 2.90, 11),
    ("palm_c", 70819, 7.05, 0.372, 0.74, 11, 3.35, 10),
]

# 계약표는 "대형 구조물 8,000"이지만 야자수는 섬당 4~16그루라 개수 × 삼각형으로 봐야 한다.
# 특대 섬 16그루 × 2,500 = 40,000 이 상한선이고, 실제로는 그 절반 아래로 들어온다.
TRI_BUDGET = 2500
TRI_FLOOR = 900

TRUNK_SIDES = 9        # 게임의 8각(PalmTrunkSides)과 같은 급. 홀수라 정면에서 능선이 겹치지 않는다
TRUNK_NODES = 11       # 잎자국 마디 수(리지 11 + 그루브 11 + 밑동 4 = 링 26개)
BARK_TILE = 0.55       # bark.png 한 장이 줄기 세로 0.55m 를 덮는다
BARK_WRAPS = 2.0       # 둘레 한 바퀴에 두 번 반복

PALM_BARK = (0.534, 0.368, 0.202)      # IslandMeshGenerator.PalmBarkColor
FROND_GREEN = (0.420, 0.659, 0.247)    # StructureVisualBuilder.FrondGreen


# ── 줄기 ──────────────────────────────────────────────────────────────────────
def _spine(t, height, top_offset, wander, phase):
    """t(0~1) -> 줄기 중심선. y 는 정확히 height*t 라 총 높이가 파라미터와 1mm도 안 어긋난다.

    z 는 t^2.2 - 밑동은 지면에 수직으로 박히고 위로 갈수록 휜다. 게임 코드가 마디마다
    기울기를 누적시켜 얻는 모양과 같지만, 이쪽은 관절이 없는 연속 곡선이다.
    """
    return Vector((wander * math.sin(2.4 * t + phase) * t,
                   height * t,
                   top_offset * (t ** 2.2)))


def build_trunk(seed, height, base_r, top_offset):
    rng = mg.Rng(seed)
    wander = height * 0.014
    phase = rng.uniform(0.0, math.tau)
    spiral = rng.uniform(2.4, 4.2)       # 잎자국 링이 감기는 속도

    # 링 y 배치: 밑동 4개는 뿌리 보스를 표현하려고 촘촘하게, 그 위는 리지/그루브 교대.
    flare_top = min(0.22, height * 0.045)
    ts = [0.0, flare_top * 0.18 / height, flare_top * 0.45 / height, flare_top / height]
    steps = TRUNK_NODES * 2
    for k in range(1, steps + 1):
        ts.append(ts[3] + (1.0 - ts[3]) * k / steps)

    rings = []
    for idx, t in enumerate(ts):
        center = _spine(t, height, top_offset, wander, phase)
        taper = 1.0 - 0.38 * t                                  # 게임의 0.62배 테이퍼
        y = height * t
        flare = (1.0 + 0.55 * math.exp(-y / 0.13)               # 뿌리 보스
                 + 0.16 * math.exp(-y / 0.70))                  # 그 위 완만한 부풀음
        if idx < 4:
            node = 1.0
        else:
            # 리지(짝수)와 그루브(홀수)를 번갈아 놓는다. 링을 더 넣지 않고 굴곡을 얻는 방법이다.
            node = 1.058 if (idx - 4) % 2 == 1 else 0.955
        radii = []
        for i in range(TRUNK_SIDES):
            a = math.tau * i / TRUNK_SIDES
            # 나선: 잎자국이 한쪽에서 더 튀어나온다(수평 링이 사방 균일하면 나사산으로 보인다).
            twist = 1.0 + (node - 1.0) * 0.85 * math.cos(a - spiral * t * math.pi)
            lobe = 1.0 + 0.021 * math.cos(3.0 * a + 2.1 * t) + 0.013 * math.cos(5.0 * a - 1.4 * t)
            radii.append(base_r * taper * flare * (1.0 + (node - 1.0)) * twist * lobe)
        rings.append((center, radii))

    bm = bmesh.new()
    mg.swept_tube(bm, rings, sides=TRUNK_SIDES, cap_bottom=True, cap_top=True, smooth=True)
    bmesh.ops.triangulate(bm, faces=bm.faces[:])
    obj = mg.new_object("trunk", bm)
    mg.cylinder_uv(obj, tile=BARK_TILE, wraps=BARK_WRAPS)
    return obj, rings[-1][0]


# ── 잎 ────────────────────────────────────────────────────────────────────────
def _flat_frond(bm, length, stations, rng):
    """잎 1장을 **평면(XZ, y=0)** 에 먼저 만든다. 길이 +Z, 폭 ±X.

    평면에서 만드는 이유는 planar_uv 때문이다. 3D 로 휜 뒤에 투영하면 늘어진 소엽이
    위에서 봤을 때 납작해져 UV 가 뭉개진다. 평면에서 UV 를 뜨고 **그 다음에** 휘면
    텍스처 밀도가 잎 어디서나 같다(잎맥이 균일하게 흐른다).
    """
    leaf_max = length * 0.43
    zs = [length * (k / stations) for k in range(stations + 1)]
    # 잎자루(rachis) 반폭: 밑동 6cm -> 끝 0.6cm. 게임의 잎 폭 0.42/0.28m 은 판자 전체 폭이고
    # 여기서는 잎자루만 해당한다 - 폭은 소엽이 만든다(합쳐서 게임 값과 같은 대역).
    ws = [0.062 * (1.0 - k / stations) ** 0.6 + 0.006 for k in range(stations + 1)]

    def v(x, z):
        return bm.verts.new((x, 0.0, z))

    for k in range(stations):
        a0, a1 = v(-ws[k], zs[k]), v(ws[k], zs[k])
        b0, b1 = v(-ws[k + 1], zs[k + 1]), v(ws[k + 1], zs[k + 1])
        bm.faces.new((a0, a1, b1, b0))

        s = (k + 0.5) / stations
        # 종 모양: 잎 중간의 소엽이 가장 길고 밑동/끝으로 갈수록 짧다.
        bell = math.sin(math.pi * (s ** 0.88)) ** 0.52
        sweep = math.radians(38.0 + 22.0 * s)       # 끝쪽으로 눕는 각
        for side in (-1.0, 1.0):
            # 좌우 길이를 따로 흔든다. 같으면 실루엣이 완벽한 대칭이라 인공물로 읽힌다.
            ln = leaf_max * bell * rng.uniform(0.82, 1.12)
            if ln < 0.03:
                continue
            # 소엽 밑변을 마디 길이의 70%만 쓴다. 마디를 꽉 채우면 소엽끼리 붙어 **한 장의
            # 판때기**가 되고(1차 렌더가 그랬다) 야자수가 아니라 고사리로 읽힌다.
            # 남긴 30%가 잎 사이로 하늘이 비치는 틈이 된다 - 이게 우상복엽의 결정적 신호다.
            gap = 0.15
            za = zs[k] + (zs[k + 1] - zs[k]) * gap
            zb = zs[k + 1] - (zs[k + 1] - zs[k]) * gap
            base_a = Vector((side * ws[k], 0.0, za))
            base_b = Vector((side * ws[k + 1], 0.0, zb))
            direction = Vector((side * math.cos(sweep), 0.0, math.sin(sweep)))
            tip = (base_a + base_b) * 0.5 + direction * ln
            # 끝을 한 점으로 모으면 바늘 삼각형이 되고(퇴화면 검사에 걸린다) 잎끝이 너무 뾰족하다.
            # 12mm 짜리 짧은 변으로 닫아 "가늘게 뾰족한" 소엽을 만든다.
            along = (base_b - base_a).normalized() * 0.012
            bm.faces.new((v(base_a.x, base_a.z), v(base_b.x, base_b.z),
                          v(tip.x + along.x, tip.z + along.z),
                          v(tip.x - along.x, tip.z - along.z)))
    return bm


def _bend_frond(obj, length, launch_deg, turn_deg, droop0, droop1):
    """평면 잎을 3D 로 휜다. 잎자루 곡선 P(s) + 좌우 처짐.

    (x, 0, z) -> P(s) + B·x - N·droop(s)·|x|^1.4     (s = z/length, B=+X, N=잎면 위쪽)
    소엽이 바깥으로 갈수록 아래로 처지는 것이 야자수 잎의 결정적 신호다. |x|^1.4 라
    잎자루 근처는 거의 안 처지고 끝만 확 떨어진다.
    """
    samples = 240
    span = 1.25                      # 뒤로 눕힌 소엽이 s>1 로 가므로 여유를 둔다
    pts, tans = [], []
    p = Vector((0.0, 0.0, 0.0))
    ds = span / samples
    for i in range(samples + 1):
        s = i * ds
        pitch = math.radians(launch_deg - turn_deg * (min(s, 1.0) ** 1.45))
        t = Vector((0.0, math.sin(pitch), math.cos(pitch)))
        pts.append(p.copy())
        tans.append(t)
        p = p + t * (length * ds)

    def frame(s):
        f = max(0.0, min(span - 1e-6, s)) / ds
        i = int(f)
        w = f - i
        pos = pts[i].lerp(pts[i + 1], w)
        tan = tans[i].lerp(tans[i + 1], w).normalized()
        binorm = Vector((1.0, 0.0, 0.0))
        return pos, binorm, tan.cross(binorm)      # T×B = 잎면 위쪽

    for vert in obj.data.vertices:
        x, z = vert.co.x, vert.co.z
        s = z / length
        pos, binorm, normal = frame(s)
        droop = droop0 + droop1 * min(1.0, max(0.0, s))
        vert.co = pos + binorm * x - normal * (droop * (abs(x) ** 1.4))


def build_crown(seed, top, tangent, count, length, stations):
    """잎 여러 장을 **메시 한 장**으로 합친다(mg.join_objects). 렌더러 1개."""
    rng = mg.Rng(seed).sub(77)
    align = Vector((0.0, 1.0, 0.0)).rotation_difference(tangent).to_matrix().to_4x4()
    fronds = []
    for i in range(count):
        # 나선 배치. 황금각에 가깝게 흔들어 잎이 겹쳐 부채 하나로 보이지 않게 한다.
        yaw = 360.0 * i / count + rng.uniform(-13.0, 13.0)
        # 2층: 안쪽(젊은) 잎은 위로 서고 바깥(늙은) 잎은 수평에서 시작해 더 늘어진다.
        young = (i % 3 == 0)
        launch = rng.uniform(30.0, 42.0) if young else rng.uniform(2.0, 16.0)
        turn = rng.uniform(72.0, 92.0) if young else rng.uniform(88.0, 116.0)
        ln = length * (rng.uniform(0.80, 0.92) if young else rng.uniform(0.94, 1.06))

        bm = bmesh.new()
        _flat_frond(bm, ln, stations, rng.sub(i))
        bmesh.ops.triangulate(bm, faces=bm.faces[:])
        # 잎은 단면 메시다. 두께를 주면 삼각형이 배로 늘고 얇은 판이 각목이 된다.
        # 뒷면이 없으면 백페이스 컬링에 통째로 사라지므로 여기서 양면으로 굽는다.
        mg.make_double_sided(bm)
        leaf = mg.new_object(f"frond{i}", bm)
        mg.shade_flat(leaf)
        # UV 는 **휘기 전 평면 좌표**로 뜬다. u 는 잎 폭(중앙 0.5), v 는 잎 길이 0->0.9.
        mg.planar_uv(leaf, axis="Y", tile=(ln * 1.60, ln * 1.12), offset=(0.5, 0.0))

        _bend_frond(leaf, ln, launch, turn,
                    droop0=rng.uniform(0.08, 0.18), droop1=rng.uniform(0.40, 0.62))
        leaf.matrix_world = (Matrix.Translation(top) @ align
                             @ Matrix.Rotation(math.radians(yaw), 4, "Y"))
        fronds.append(leaf)

    return mg.join_objects(fronds, name="crown")


def build_palm(name, seed, height, base_r, top_offset, fronds, frond_len, stations):
    trunk, top = build_trunk(seed, height, base_r, top_offset)
    # 왕관은 줄기 꼭대기의 접선을 따라 기운다(줄기가 휜 만큼 왕관도 기울어야 자연스럽다).
    below = _spine(0.94, height, top_offset, height * 0.014, mg.Rng(seed).uniform(0.0, math.tau))
    tangent = (top - below).normalized()
    crown = build_crown(seed, top, tangent, fronds, frond_len, stations)
    return trunk, crown


def main():
    print("[palm] 야자수 3종 생성")
    all_stats = []
    for name, seed, height, base_r, top_offset, fronds, frond_len, stations in VARIANTS:
        mg.reset_scene()
        trunk, crown = build_palm(name, seed, height, base_r, top_offset,
                                  fronds, frond_len, stations)
        trunk.name, crown.name = f"{name}_trunk", f"{name}_crown"

        stats = mg.enforce_contract_group([trunk, crown], tri_budget=TRI_BUDGET,
                                          tri_floor=TRI_FLOOR, name=name)

        # 접지 중심 정렬이 실제로 먹었는지 재측정한다(0 에 붙어야 한다).
        base = [v.co for v in trunk.data.vertices if v.co.y < 0.02]
        bx = sum(v.x for v in base) / len(base)
        bz = sum(v.z for v in base) / len(base)
        bh = max(v.co.y for v in trunk.data.vertices)

        obj_path = os.path.join(mg.MODELS_DIR, f"{name}.obj")
        mg.export_obj([trunk, crown], obj_path)
        stats = mg.verify_obj_file(obj_path, stats)

        mg.assign_material(trunk, mg.preview_material(
            f"prev_{name}_bark", texture_name="bark", base_color=PALM_BARK, roughness=0.86))
        mg.assign_material(crown, mg.preview_material(
            f"prev_{name}_frond", texture_name="frond", base_color=FROND_GREEN, roughness=0.62))
        mg.turntable([trunk, crown], os.path.join(mg.PREVIEW_DIR, f"{name}.png"),
                     title=f"{name}   seed {seed}   fronds {fronds}",
                     stats=stats,
                     notes="bark %.2fm / frond planar" % BARK_TILE)

        mg.report(stats)
        print(f"             줄기 높이 {bh:.2f} m   밑동 오프셋 (x {bx:+.3f}, z {bz:+.3f}) m")
        all_stats.append(stats)

    print("[palm] 완료 - 렌더: Tools/blender/_preview/palm_*.png")
    return all_stats


if __name__ == "__main__":
    main()
