#!/usr/bin/env python3
"""
대나무 한 포기 3종 - bamboo_a / bamboo_b / bamboo_c.

    python3 Tools/blender/units/bamboo.py

산출물 (이 세 파일만 건드린다)
  Assets/_Project/Resources/Models/bamboo_a.obj   (+ b, c)
  Tools/blender/_preview/bamboo_a.png             (+ b, c)  ← 저장소에 넣지 않는다
  Tools/blender/_preview/bamboo_tint_proposal.png ← 색 제안용(아래 [색] 참고)

────────────────────────────────────────────────────────────────────────────────
크기의 근거 (게임 코드 실측 - 이번 배치에서 코드는 못 고치므로 여기에 맞춘다)

  IslandResourceSpawner.cs:414 GetNodeShape("대나무")
      루트 = Cylinder, scale (0.30, 2.10, 0.30)
      → 총 높이 = scale.y × 2 = 4.20 m, 세로 지터 0.85~1.25(:345) = **3.57 ~ 5.25 m**
      → 채집 콜라이더 = 캡슐 **지름 0.30 m** (이 값에 조준이 걸려 있다)
  IslandResourceSpawner.cs:1061 BambooCulmUnit
      루트 줄기 메시 반지름 0.22 → 보이는 밑동 지름 0.132 m · 마디 5~7개 · 테이퍼 0.70
  IslandResourceSpawner.cs:1084 BambooCulmMeters (곁줄기, 미터 규격)
      높이 2.25~3.85 m · 지름 6.4~9.6 cm · 기울기 0.14~0.38 m · 마디 5~9
  IslandResourceSpawner.cs:626 대나무 상세 파츠
      곁줄기 2~4개(총 3~5줄기) · 밑동 간격 0.12~0.34 m · 잎다발 2~3개
      잎다발 높이 = 0.62~0.94 × 4.2 m · 잎 메시는 야자잎(FrondMeters, 길이 0.44~0.58 m)
  IslandResourceSpawner.cs:1003  StemSides = 6
  개수  IslandResourceSpawner.cs:203  count = baseCount × 섬 배율(1/2/3/4)

  ★ 굵기는 게임의 "보이는 지름 13.2cm"가 아니라 **콜라이더 지름 30cm** 쪽에 맞췄다.
    감독 지적("12cm 줄기가 5.2m로 솟아 갈대처럼 보인다")이 맞다 - 세장비 32는 대나무가
    아니라 갈대다. 굵은 줄기를 지름 16~20cm 로 올려 세장비를 22 로 내렸고, 그래도
    콜라이더(30cm) 안에 들어가므로 **채집 조준은 1mm도 안 변한다**.
    높이는 게임 범위(3.57~5.25 m)의 하단대(3.60~4.50 m)를 쓴다 - 굵고 낮은 쪽이 대나무로 읽힌다.

형태 규칙 (프리미티브 원통 다발과 갈라지는 이유)
  1. **마디**. 링 3개(아래 1.00 / 칼라 1.40 / 위 1.05)를 높이의 1.3% 안에 몰아 놓아
     날카로운 턱을 만들고, 그 사이는 길게 비워 곧게 뻗게 한다. 마디 간격 0.75~0.90 m 로
     넓혀 **멀리서도 세어진다**. 칼라 두 띠는 **플랫 셰이딩**이라 법선이 끊겨 밝기 링이 생긴다 -
     기하 굴곡만으로는 20m 밖에서 한 픽셀도 안 남는다(3차 렌더에서 확인).
  2. **굵기를 섞는다**. 굵은 줄기(지름 16~20cm, 8각) 2~3개 + 가는 줄기(7~10cm, 6각) 3~5개.
     전부 같은 굵기면 파이프 다발로 읽힌다.
  3. **잎은 길고 늘어진 잎날 다발**. 길이 0.60~0.90 m · 폭 5~8 cm 를 줄기
     **상단 20%(꼭대기 포함)** 에 3~5장씩 묶어 아래로 늘어뜨린다. 사방으로 뻗은 짧고 넓은 조각은
     엉겅퀴로 읽힌다(4차 렌더에서 실제로 그랬다).
  4. 잎은 **두께 없는 단면 + 굽힌 양면**이다(계약 4장 - 알파 컷아웃 셰이더 설정이 없다).

[색] 줄기가 어두운 것은 UV 가 아니라 **런타임 틴트** 때문이다.
  IslandResourceSpawner.cs:871/907 이 대나무에 Driftwood(#8C6640, 탁한 갈색)를 물린다.
  UV 쪽에서 할 수 있는 것은 다 했다 - bamboo.png 타일을 0.42 → 0.30 m, 둘레 반복을 1 → 2 로
  올려 결이 촘촘해지면서 어두운 줄무늬가 평균화돼 밝게 읽힌다.
  나머지는 코드다(이번 락 밖). 제안 색: **#B4BE64**(황록). 미리보기는
  `_preview/bamboo_tint_proposal.png` 에 그 색으로 한 장 더 냈다.

원점 규약: **접지 중심**(mg.enforce_contract_group 의 align="ground").
  포기 밑면(줄기 밑동 링들)의 XZ 중심이 원점이다. 바운딩 박스는 비대칭이어도 된다.

파일 구조: OBJ 하나에 `o` 오브젝트 2개(<name>_culms / <name>_leaves).
  줄기(Driftwood · bamboo 텍스처)와 잎(FrondGreen · frond 텍스처)은 색이 달라야 하고
  머티리얼은 런타임 코드가 만든다(계약 4장). 자세한 근거는 units/palm.py 헤더 참고.
  렌더러 2개 - 지금 코드의 최대 8개(루트 1 + 곁줄기 4 + 잎다발 3)에서 4배 줄어든다.
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
    # 이름,       시드,  굵은줄기, 가는줄기, 최고높이 m, 굵은마디, 가는마디, 다발/줄기, 잎/다발
    ("bamboo_a", 11527, 2, 3, 3.60, 4, 3, 3, 5),
    ("bamboo_b", 24631, 3, 4, 4.05, 4, 3, 3, 4),
    ("bamboo_c", 39887, 3, 5, 4.50, 4, 3, 3, 3),
]

# ── 2026-08-17 확장 변종 d/e/f ────────────────────────────────────────────────
# a/b/c 는 줄기 5~8개의 크기 시리즈라 형태 축이 시드뿐이었다. d/e/f 는 **구성과 자세**로
# 갈린다. 10번째 원소(style dict)가 build_clump 의 기본값을 덮어쓴다 - 키가 없으면 기존
# 상수 그대로라(기본값 = 기존 리터럴) a/b/c 의 난수 소비·출력은 1바이트도 안 변한다.
#
#   bamboo_d  [v2 2026-08-17] 성긴 중키 포기. 줄기 4개(2+2), 4.30m, 가늘고 곧다. 잎 2다발.
#             v1은 "어린 포기" 2.30m였는데 스폰 높이 대역(3.57~5.25)에 한 번도 안 걸려
#             영영 선택되지 않는 죽은 변종이었다(0.2.12 커밋 노트). 대역 하단(4.3m 근처)을
#             맡는 "가늘고 성긴" 포기로 재정의한다 - e(꽉 참)/f(기움)와 구성 축이 계속 갈린다.
#   bamboo_e  꽉 찬 노숲 포기. 줄기 8개(4+4), 5.05m - 게임 높이 대역(3.57~5.25)의 상단.
#             잎다발 3×5 로 가장 무성하다.
#   bamboo_f  바람 맞은 포기. 줄기 5개(2+3)가 **전부 같은 방향으로** 크게 기운다(wind_azim).
#             잎다발 2×3 으로 성기다 - 기운 실루엣이 잎에 가려지지 않게.
NEW_VARIANTS = [
    ("bamboo_d", 52967, 2, 2, 4.30, 5, 4, 2, 4, {
        "thick_radius": (0.055, 0.068), "thin_radius": (0.030, 0.042),
        "thick_offset": (0.10, 0.16), "thin_offset": (0.12, 0.22),
        "thick_lean": (0.06, 0.14), "thin_lean": (0.05, 0.12),
        "thin_height": (0.62, 0.86),
        "tri_floor": 380,      # 성긴 포기는 줄기 4개가 정체성이다 - 공용 하한 600 을 못 채워도 맞다
    }),
    # 처음엔 4+4 / 마디 5/4 / 잎 3×5 로 잡았다가 2,576 삼각형으로 예산(1,800)을 뚫었다.
    # 굵은 줄기 4개가 이 변종의 실루엣 축이므로 그쪽을 지키고 나머지로 줄였다(실측 1,684).
    ("bamboo_e", 66101, 4, 3, 5.05, 4, 3, 3, 3, {
        "thick_offset": (0.34, 0.55), "thin_offset": (0.28, 0.50),
    }),
    ("bamboo_f", 73019, 2, 3, 4.30, 4, 3, 2, 3, {
        "wind_azim": 0.65,                     # 전 줄기가 이 방위로 기운다(라디안)
        "thick_lean": (0.55, 0.85), "thin_lean": (0.40, 0.70),
    }),
]

# 감독 승인으로 1,200 → 1,800. 굵기와 마디에 쓰는 편이 낫다는 판단.
TRI_BUDGET = 1800
TRI_FLOOR = 600

THICK_SIDES = 8         # 지름 16~20cm 는 근접 채집 대상이라 6각이면 육각기둥으로 보인다
THIN_SIDES = 6          # IslandResourceSpawner.StemSides = 6 과 같다
NODE_GAP = 0.013        # 마디 링 3개를 높이의 1.3% 안에 몰아 턱을 세운다(좁을수록 턱이 선다)
BAMBOO_TILE = 0.30      # bamboo.png 세로 타일(0.42 → 0.30: 결을 촘촘하게 = 평균이 밝아진다)
BAMBOO_WRAPS = 2.0      # 둘레 한 바퀴에 두 번(1 → 2: 같은 이유)

DRIFTWOOD = (0.549, 0.400, 0.251)      # StructureVisualBuilder.Driftwood - 게임이 지금 쓰는 색
BAMBOO_PROPOSED = (0.706, 0.745, 0.392)  # #B4BE64 - 감독에게 제안하는 황록
FROND_GREEN = (0.420, 0.659, 0.247)    # StructureVisualBuilder.FrondGreen


# ── 줄기 ──────────────────────────────────────────────────────────────────────
def build_culm(bm, base, height, radius, lean_dir, lean, nodes, sides, phase):
    """줄기 1개를 bm 에 **누적**한다(포기 전체가 메시 한 장이 되게).

    링 배치 = 마디마다 3개(아래 / 칼라 / 위) + 꼭대기 1개.
    3차까지는 마디를 링 2개(등간격 교대)로 만들었는데, 그러면 칼라에서 다음 마디까지
    굵기가 계속 줄어 **원뿔을 쌓은 것**처럼 보인다. 위 링을 하나 더 둬서 칼라를 통과한
    직후 굵기를 되돌려야 마디 사이가 진짜로 곧아진다.

    칼라 두 띠는 플랫 셰이딩으로 남긴다(smooth 리스트). 매끈하게 두면 굴곡이 뭉개져
    멀리서 마디가 안 보인다 - 대나무의 정체성이 통째로 사라진다.
    """
    ts, factors, bands = [], [], []
    for i in range(nodes):
        t0 = i / nodes
        ts += [t0, t0 + NODE_GAP, t0 + NODE_GAP * 2.0]
        factors += [1.00, 1.40, 1.05]
    ts.append(1.0)
    factors.append(0.98)
    for k in range(len(ts) - 1):
        # 마디 안쪽 두 띠(아래→칼라, 칼라→위)만 플랫. 나머지 곧은 마디 사이는 스무스.
        bands.append(not (k % 3 in (0, 1)))

    ring_list = []
    for k, t in enumerate(ts):
        pos = base + Vector((lean_dir.x, 0.0, lean_dir.z)) * (lean * (t ** 1.7))
        pos.y = base.y + height * t
        taper = 1.0 - 0.24 * t
        radii = []
        for i in range(sides):
            a = math.tau * i / sides
            radii.append(radius * taper * factors[k]
                         * (1.0 + 0.028 * math.cos(2.0 * a + phase)))
        ring_list.append((pos, radii))

    mg.swept_tube(bm, ring_list, sides=sides,
                  cap_bottom=True, cap_top=True, smooth=bands)
    return ring_list


# ── 잎 ────────────────────────────────────────────────────────────────────────
def _blade(length, half_width, droop, rng):
    """대나무 잎날 1장. **길고 가늘다**(세장비 1:20). 쿼드 1장 = 양면 4삼각형.

    평면(XZ)에서 만들어 planar_uv 로 UV 를 뜬 **뒤에** 휜다(휜 뒤 투영하면 무늬가 뭉개진다).
    4차 렌더의 잎은 0.34~0.52 m 에 폭 7~11 cm 라 짧고 넓어 엉겅퀴 가시로 보였다.
    대나무 잎날은 길이가 폭의 스무 배쯤 되고, 그 비례 자체가 실루엣의 신호다.
    """
    bm = bmesh.new()

    def v(x, z):
        return bm.verts.new((x, 0.0, z))

    a0, a1 = v(-half_width * 0.55, 0.0), v(half_width * 0.55, 0.0)
    c0, c1 = v(-half_width * 0.08, length), v(half_width * 0.08, length)
    # 밑동보다 1/3 지점이 넓은 것이 잎날인데, 쿼드 1장으로는 못 한다. 대신 밑동을 좁혀
    # (0.55배) 밑동→중간이 벌어지는 인상을 만든다. 삼각형을 늘리지 않는 선의 최선이다.
    m0, m1 = v(-half_width, length * 0.34), v(half_width, length * 0.34)
    bm.faces.new((a0, a1, m1, m0))
    bm.faces.new((m0, m1, c1, c0))
    bmesh.ops.triangulate(bm, faces=bm.faces[:])
    mg.make_double_sided(bm)

    obj = mg.new_object("blade", bm)
    mg.shade_flat(obj)
    mg.planar_uv(obj, axis="Y", tile=(half_width * 4.0, length), offset=(0.5, 0.0))

    twist = rng.uniform(-0.16, 0.16)
    for vert in obj.data.vertices:
        s = vert.co.z / length
        vert.co.y -= droop * length * (s ** 1.7)     # 끝이 확 처진다
        vert.co.y += twist * vert.co.x * s
    return obj


def build_leaves(seed, culms, clusters, per_cluster):
    """잎을 전부 **메시 한 장**으로 합친다(mg.join_objects). 렌더러 1개.

    잎날을 **다발(cluster)** 로 묶는다. 한 지점에서 좁은 부채꼴로 갈라져 아래로 늘어지는
    묶음이 대나무 잎의 모습이고, 방위를 고르게 흩뿌리면(4차 렌더) 별표가 된다.
    다발은 줄기 **상단 32%** 에만 단다 - 아래가 맨 줄기여야 대나무로 읽힌다
    (게임 코드도 잎다발을 0.62~0.94 높이에만 단다: IslandResourceSpawner.cs:646).
    """
    rng = mg.Rng(seed).sub(41)
    blades = []
    for ci, ring_list in enumerate(culms):
        sub = rng.sub(ci)
        top = len(ring_list) - 1
        for w in range(clusters):
            # 5차 렌더는 잎이 79% 에서 끝나 **꼭대기 1m 가 맨 장대**였다. 꼭대기까지 올린다.
            k = max(1, int(round(top * (0.80 + 0.20 * (w + 0.5) / clusters))))
            k = min(top, k)
            center, radii = ring_list[k]
            azim = sub.uniform(0.0, math.tau)
            # 다발 밑동을 줄기에서 조금 띄운다(짧은 곁가지 끝에 달린 것처럼).
            hub = center + Vector((math.cos(azim), 0.0, math.sin(azim))) * (radii[0] + sub.uniform(0.02, 0.10))
            for b in range(per_cluster):
                spread = (b - (per_cluster - 1) * 0.5) / max(1, per_cluster - 1)
                leaf = _blade(sub.uniform(0.60, 0.90), sub.uniform(0.026, 0.038),
                              sub.uniform(0.42, 0.78), sub)
                # 좁은 부채꼴(±32도) + 전부 아래로. 이 두 가지가 "다발"과 "별표"를 가른다.
                yaw = azim + spread * math.radians(64.0) + sub.uniform(-0.10, 0.10)
                # 전부 아래로 꽂으면 잎면이 하늘을 안 봐서 통째로 어둡게 렌더된다.
                # 위쪽 잎 몇 장은 수평 근처에 둬 빛을 받게 한다.
                pitch = math.radians(-6.0 + spread * 22.0 + sub.uniform(-20.0, 10.0))
                roll = math.radians(sub.uniform(-40.0, 40.0))
                leaf.matrix_world = (Matrix.Translation(hub)
                                     @ Matrix.Rotation(-yaw, 4, "Y")
                                     @ Matrix.Rotation(pitch, 4, "X")
                                     @ Matrix.Rotation(roll, 4, "Z"))
                blades.append(leaf)
    return mg.join_objects(blades, name="leaves")


def build_clump(seed, thick, thin, top_height, thick_nodes, thin_nodes,
                clusters, per_cluster, style=None):
    # style: d/e/f 변종용 오버라이드. 기본값이 기존 리터럴과 정확히 같아서 style 을 안 주면
    # (a/b/c) 난수 소비 횟수·범위가 1비트도 안 변한다 - 그게 md5 보존의 전제다.
    style = style or {}
    thick_radius = style.get("thick_radius", (0.080, 0.100))
    thin_radius = style.get("thin_radius", (0.034, 0.052))
    thick_offset = style.get("thick_offset", (0.30, 0.48))
    thin_offset = style.get("thin_offset", (0.22, 0.44))
    thick_lean = style.get("thick_lean", (0.22, 0.48))
    thin_lean = style.get("thin_lean", (0.10, 0.30))
    thin_height = style.get("thin_height", (0.52, 0.80))
    wind_azim = style.get("wind_azim")      # 있으면 전 줄기가 이 방위로 기운다(바람 자세)

    rng = mg.Rng(seed)
    bm = bmesh.new()
    ring_lists = []
    total = thick + thin
    for i in range(total):
        is_thick = i < thick
        if is_thick:
            # 굵은 줄기는 **자기들끼리** 원을 나눠 갖는다. 5차 렌더까지는 전체 인덱스로
            # 방위를 나눠서 굵은 셋이 한쪽에 몰렸고(i=0 은 아예 중심), 정면에서 슬래브
            # 하나로 보였다. 굵은 것이 정면에서 따로 세어져야 "포기"로 읽힌다.
            azim = math.tau * i / thick + rng.uniform(-0.30, 0.30)
            offset = 0.10 if i == 0 else rng.uniform(*thick_offset)
            height = top_height * rng.uniform(0.86, 1.0)
            radius = rng.uniform(*thick_radius)     # 기본 지름 16~20 cm (감독 지시)
            sides, nodes = THICK_SIDES, thick_nodes
            lean = rng.uniform(*thick_lean)
        else:
            # 가는 줄기는 굵은 것 **사이**에 끼워 넣는다(방위를 반 칸 어긋나게).
            azim = math.tau * (i - thick + 0.5) / thin + rng.uniform(-0.35, 0.35)
            offset = rng.uniform(*thin_offset)
            height = top_height * rng.uniform(*thin_height)
            radius = rng.uniform(*thin_radius)      # 기본 지름 6.8~10.4 cm (게임 곁줄기 대역)
            sides, nodes = THIN_SIDES, thin_nodes
            lean = rng.uniform(*thin_lean)
        base = Vector((math.cos(azim) * offset, 0.0, math.sin(azim) * offset))
        if wind_azim is None:
            out = Vector((math.cos(azim), 0.0, math.sin(azim)))     # 밖으로 벌어진다
        else:
            out = Vector((math.cos(wind_azim), 0.0, math.sin(wind_azim)))  # 한 방향(바람)
        ring_lists.append(build_culm(
            bm, base, height, radius, out, lean,
            nodes, sides, rng.uniform(0.0, math.tau)))

    bmesh.ops.triangulate(bm, faces=bm.faces[:])
    culms = mg.new_object("culms", bm)
    mg.cylinder_uv(culms, tile=BAMBOO_TILE, wraps=BAMBOO_WRAPS)

    leaves = build_leaves(seed, ring_lists, clusters, per_cluster)
    return culms, leaves


def main():
    print("[bamboo] 대나무 6종 생성")
    all_stats = []
    for entry in VARIANTS + NEW_VARIANTS:
        (name, seed, thick, thin, height, thick_nodes, thin_nodes,
         clusters, per_cluster) = entry[:9]
        style = entry[9] if len(entry) > 9 else None
        mg.reset_scene()
        culms, leaves = build_clump(seed, thick, thin, height, thick_nodes,
                                    thin_nodes, clusters, per_cluster, style)
        culms.name, leaves.name = f"{name}_culms", f"{name}_leaves"

        floor = (style or {}).get("tri_floor", TRI_FLOOR)
        stats = mg.enforce_contract_group([culms, leaves], tri_budget=TRI_BUDGET,
                                          tri_floor=floor, name=name, align="ground")

        # 접지 중심 정렬이 실제로 먹었는지 재측정한다(0 에 붙어야 한다).
        gc = mg.ground_center([culms, leaves])

        obj_path = os.path.join(mg.MODELS_DIR, f"{name}.obj")
        mg.export_obj([culms, leaves], obj_path)
        stats = mg.verify_obj_file(obj_path, stats)

        mg.assign_material(culms, mg.preview_material(
            f"prev_{name}_culm", texture_name="bamboo", base_color=DRIFTWOOD, roughness=0.55))
        mg.assign_material(leaves, mg.preview_material(
            f"prev_{name}_leaf", texture_name="frond", base_color=FROND_GREEN, roughness=0.60))
        mg.turntable([culms, leaves], os.path.join(mg.PREVIEW_DIR, f"{name}.png"),
                     title=f"{name}  seed {seed}  culms {thick}+{thin}",
                     stats=stats,
                     notes="tint Driftwood (game) / bamboo %.2fm x%.0f" % (BAMBOO_TILE, BAMBOO_WRAPS))

        # 감독에게 낼 색 제안 한 장(코드가 Driftwood 를 물리는 한 게임에서는 이 색이 안 나온다).
        if name == "bamboo_b":
            mg.assign_material(culms, mg.preview_material(
                f"prop_{name}_culm", texture_name="bamboo",
                base_color=BAMBOO_PROPOSED, roughness=0.50))
            mg.turntable([culms, leaves],
                         os.path.join(mg.PREVIEW_DIR, "bamboo_tint_proposal.png"),
                         title=f"{name}  TINT PROPOSAL #B4BE64",
                         stats=stats,
                         notes="runtime tint swap proposal - code is out of lock")

        mg.report(stats)
        print(f"             접지 중심 (x {gc.x:+.4f}, z {gc.z:+.4f}) m   "
              f"굵은 줄기 지름 16~20cm / 가는 줄기 6.8~10.4cm")
        all_stats.append(stats)

    print("[bamboo] 완료 - 렌더: Tools/blender/_preview/bamboo_*.png")
    return all_stats


if __name__ == "__main__":
    main()
