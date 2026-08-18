#!/usr/bin/env python3
"""
clam_* - 해저 진주조개 3종 (2026-08-18 채집 노드, 열대 바다 모래 스커트 수심 2~10m).

    python3 Tools/blender/units/clam.py

산출물 (전부 신규)
  Assets/_Project/Resources/Models/clam_a.obj  중형 폭 0.50m - 진주 큼
  Assets/_Project/Resources/Models/clam_b.obj  소형 폭 0.35m - 껍데기 납작
  Assets/_Project/Resources/Models/clam_c.obj  대형 폭 0.70m - 주름 깊음
  (+ 각 .mtl - inject_usemtl 이 만든다. mtllib 없이는 Unity 6.5가 서브메시를 안 가른다)
  Tools/blender/_preview/clam_*.png             렌더 - 저장소에 넣지 않는다

설계
  반쯤 벌어진 대왕조개: 아래 껍데기(사발) + 위 껍데기(뚜껑, 힌지 축으로 20~35° 개방),
  가장자리는 방사 스캘럽 주름(반지름 + 높이 물결). 위/아래 립의 물결 위상을 반대로 둬서
  틈이 지그재그로 보인다(대왕조개 특유의 물결 입). 안쪽 사발 중앙 앞쪽에 진주 구슬.

  각 모델은 `o` 오브젝트 2개 = 서브메시 2개 (**순서 고정 - shell 먼저**):
    shell  껍데기 전체(아래+위+힌지)     pearl  진주 구슬
  색은 런타임 코드가 입힌다 - 여기 머티리얼은 프리뷰 렌더 전용이고 OBJ 로 안 나간다.
  원점은 접지 중심(align="ground"), 밑면 y=0 - 모래 바닥에 그대로 놓는다.
  삼각형 예산: small_prop(1500) 안에서 종당 1000 안팎.

시드는 표에 박아 둔다. 같은 시드 = 같은 메시 = 같은 md5.
(coral.py 가 610xx 대역을 이미 쓰므로 여기는 710xx 를 쓴다 - 값 충돌은 무해하지만
 로그에서 헷갈린다.)
"""

import math
import os
import sys

_UNITS_DIR = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.dirname(_UNITS_DIR))

import mgbuild as mg  # noqa: E402
import bmesh  # noqa: E402
from mathutils import Matrix, Vector  # noqa: E402


# ── 껍데기 한 짝 ──────────────────────────────────────────────────────────────
def build_valve(bm, rng, rx, rz, hz, hh, bulge, th, spokes, rings,
                scallops, samp, wamp, phase, sign):
    """힌지 선(뒤쪽 z=hz)에서 앞으로 펼쳐지는 부채꼴 사발 한 짝(두께 있는 닫힌 솔리드).

    sign=-1 아래 껍데기(아래로 불룩), +1 위 껍데기(위로 불룩).
    스캘럽: 반지름에 (1 + samp*cos(scallops*a + phase)), 립 높이에 wamp*cos(...)*v^2.
    만든 정점 전체를 돌려준다(위 껍데기를 힌지 축으로 회전시키기 위해).
    """
    A = 1.12  # 부채 반각(rad) - 힌지 뒤로 감기지 않는 범위(뿔 방지)
    jit = [rng.uniform(0.98, 1.02) for _ in range(spokes + 1)]

    def rim_pt(i, win):
        t = i / spokes
        a = -A + 2.0 * A * t
        s = 1.0 + samp * win * math.cos(scallops * a + phase)
        r = jit[i] * s
        return a, Vector((math.sin(a) * rx * r, 0.0, hz + math.cos(a) * rz * r))

    grid_o, grid_i = [], []
    for i in range(spokes + 1):
        t = i / spokes
        # 부채 끝(옆구리)으로 갈수록 주름을 죽인다 - 끝 스포크에 주름이 실리면
        # 뒤에서 봤을 때 뿔처럼 솟는다(첫 렌더 실사고).
        win = math.sin(math.pi * t) ** 0.5
        a, rim = rim_pt(i, win)
        hp = Vector((hh * (2.0 * t - 1.0), 0.0, hz))
        wave = wamp * win * math.cos(scallops * a + phase)
        col_o, col_i = [], []
        for v_i in range(rings + 1):
            v = v_i / rings
            bowl = math.sin(math.pi * v)
            p_o = hp.lerp(rim, v)
            p_o.y = sign * (th + bulge * bowl) + wave * v * v
            col_o.append(bm.verts.new(p_o))
            p_i = hp.lerp(rim, v * 0.93)
            p_i.y = sign * (bulge - th * 0.5) * bowl + wave * v * v
            col_i.append(bm.verts.new(p_i))
        grid_o.append(col_o)
        grid_i.append(col_i)

    faces = []
    for i in range(spokes):
        for v in range(rings):
            faces.append(bm.faces.new((grid_o[i][v], grid_o[i + 1][v],
                                       grid_o[i + 1][v + 1], grid_o[i][v + 1])))
            faces.append(bm.faces.new((grid_i[i][v + 1], grid_i[i + 1][v + 1],
                                       grid_i[i + 1][v], grid_i[i][v])))
        # 립(테두리) 띠 + 힌지 뒤판 띠.
        faces.append(bm.faces.new((grid_o[i][rings], grid_o[i + 1][rings],
                                   grid_i[i + 1][rings], grid_i[i][rings])))
        faces.append(bm.faces.new((grid_i[i][0], grid_i[i + 1][0],
                                   grid_o[i + 1][0], grid_o[i][0])))
    # 양 옆구리 마감(부채 끝 열).
    for col_o, col_i in ((grid_o[0], grid_i[0]), (grid_o[-1], grid_i[-1])):
        for v in range(rings):
            faces.append(bm.faces.new((col_o[v], col_o[v + 1],
                                       col_i[v + 1], col_i[v])))
    for f in faces:
        f.smooth = True
    bmesh.ops.recalc_face_normals(bm, faces=faces)

    verts = [v for col in grid_o + grid_i for v in col]
    return verts


# ── 조개 한 마리 ──────────────────────────────────────────────────────────────
def build_clam(name, seed, width, depth, bulge, open_deg, scallops, samp, wamp,
               pearl_r, spokes, rings=4):
    rng = mg.Rng(seed)
    rx = width * 0.5 / (1.0 + samp + 0.04)  # 스캘럽/지터 여유를 빼고 목표 폭에 맞춘다
    rz = depth * 0.82
    hz = -depth * 0.42                       # 힌지 z(뒤쪽)
    hh = width * 0.16                        # 힌지 반폭
    th = max(0.008, width * 0.03)            # 껍데기 두께
    phase = rng.uniform(0.0, math.tau)

    bm = bmesh.new()
    # 아래 껍데기(사발). 립 물결 위상 phase.
    build_valve(bm, rng, rx, rz, hz, hh, bulge, th, spokes, rings,
                scallops, samp, wamp, phase, sign=-1)
    # 위 껍데기(뚜껑). 물결 위상을 반 주기 밀어 틈이 지그재그가 되게 한다.
    top = build_valve(bm, rng, rx * 0.97, rz * 0.97, hz, hh, bulge * 0.92, th,
                      spokes, rings, scallops, samp, wamp, phase + math.pi, sign=+1)
    # 힌지 축(x축, z=hz)으로 개방 - 앞쪽(+Z)이 들리는 방향.
    rot = (Matrix.Translation((0, 0, hz))
           @ Matrix.Rotation(-math.radians(open_deg), 4, "X")
           @ Matrix.Translation((0, 0, -hz)))
    for v in top:
        v.co = rot @ v.co
    # 힌지 덮개(관자 돌기) - 두 짝 이음새를 가리는 납작한 육면체. 플랫 셰이딩.
    knob = bmesh.ops.create_cube(bm, size=1.0)["verts"]
    knob_m = (Matrix.Translation((0, th * 0.5, hz - depth * 0.04))
              @ Matrix.Diagonal((hh * 2.0, th * 3.5, depth * 0.10, 1.0)))
    for v in knob:
        v.co = knob_m @ v.co

    shell = mg.new_object("shell", bm)
    mg.box_uv(shell, tile=max(0.25, width * 0.7))

    # 진주: 아래 사발 안쪽, 중앙보다 살짝 앞(개방부에서 보이는 자리).
    v_p = 0.52
    z_p = hz + (rz - 0.0) * v_p * 0.93 + 0.0
    y_floor = -(bulge - th * 0.5) * math.sin(math.pi * v_p)
    bm_p = bmesh.new()
    bmesh.ops.create_icosphere(bm_p, subdivisions=3, radius=pearl_r)
    bm_p.transform(Matrix.Translation((rng.uniform(-0.02, 0.02) * width,
                                       y_floor + pearl_r * 0.80, z_p)))
    for f in bm_p.faces:
        f.smooth = True
    pearl = mg.new_object("pearl", bm_p)
    mg.box_uv(pearl, tile=max(0.15, pearl_r * 3.0))

    # 폭 정규화: 부채각/지터 때문에 실측 폭이 목표에 못 미친다(첫 빌드 0.454 vs 0.50).
    # 균일 스케일이라 비례·조립 관계는 그대로다.
    lo, hi = mg.union_bbox([shell, pearl])
    s = width / (hi.x - lo.x)
    for o in (shell, pearl):
        o.data.transform(Matrix.Scale(s, 4))
    return shell, pearl


# ── 내보내기(coral.py 의 export_two_part 규약) ────────────────────────────────
def export_clam(name, shell, pearl, tri_budget, tri_floor):
    objs = [shell, pearl]                      # o 그룹 순서 고정: shell 먼저
    for o in objs:
        mg.triangulate(o)
    mg.assign_material(shell, mg.preview_material(
        "pv_" + name + "_shell", base_color=(0.78, 0.70, 0.55)))
    mg.assign_material(pearl, mg.preview_material(
        "pv_" + name + "_pearl", base_color=(0.95, 0.93, 0.90), roughness=0.25))

    stats = mg.enforce_contract_group(objs, tri_budget=tri_budget, tri_floor=tri_floor,
                                      name=name, align="ground")
    out = os.path.join(mg.MODELS_DIR, name + ".obj")
    mg.export_obj(objs, out)
    mg.verify_obj_file(out, stats)
    mg.inject_usemtl(out)                      # 반드시 verify 뒤(호출 순서 계약)
    png = os.path.join(mg.PREVIEW_DIR, name + ".png")
    mg.turntable(objs, png, title=name, stats=stats, px=330, samples=16)
    return stats


# ── 명세표: 이름, 시드, 파라미터 ──────────────────────────────────────────────
CLAMS = [
    # (이름, 시드, 폭, 깊이, 불룩, 개방각°, 주름수, 주름반경, 주름높이, 진주r, 스포크)
    ("clam_a", 71001, 0.50, 0.40, 0.100, 26.0, 7, 0.060, 0.014, 0.058, 14),  # 중형 - 진주 큼
    ("clam_b", 71002, 0.35, 0.29, 0.055, 21.0, 7, 0.050, 0.009, 0.030, 12),  # 소형 - 납작
    ("clam_c", 71003, 0.70, 0.49, 0.150, 33.0, 8, 0.105, 0.030, 0.062, 18),  # 대형 - 주름 깊음
]


def main():
    manifest = []
    for name, seed, w, d, bulge, open_deg, sc, samp, wamp, pr, spokes in CLAMS:
        mg.reset_scene()
        shell, pearl = build_clam(name, seed, w, d, bulge, open_deg, sc, samp, wamp,
                                  pr, spokes)
        st = export_clam(name, shell, pearl, tri_budget=1500, tri_floor=300)
        manifest.append((name, seed, st["tris"], tuple(round(v, 3) for v in st["size"]),
                         st["parts"]))

    print("CLAM_MANIFEST")
    for name, seed, tris, size, parts in manifest:
        print(f"  {name}  seed={seed}  {tris}tri  {size[0]}x{size[1]}x{size[2]}m  {parts}")
    print(f"[clam] 완료 - {len(manifest)}종")


if __name__ == "__main__":
    main()
