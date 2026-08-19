#!/usr/bin/env python3
"""
섬 배치 재생성기 — Assets/_Project/Scripts/Utils/MaldivesLayout.cs 를 통째로 다시 쓴다.

왜 스크립트인가: 배치는 손으로 고칠 수 있는 데이터가 아니다(50섬 × 좌표 + 윤곽 64샘플).
숫자 하나를 손으로 고치면 최소 간격·규모 분포·시작 섬 규칙이 조용히 깨진다.
배치를 바꾸고 싶으면 **아래 파라미터만 고치고 이 스크립트를 다시 돌려라.**

윤곽(mask 64샘플)은 몰디브 Z28 환초 실측값을 그대로 재사용한다 — 좌표와 규모만 다시 정한다.
같은 SEED면 항상 같은 배치가 나온다(재현성).
"""
import json, math, os, random

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, "..", ".."))
OUT  = os.path.join(ROOT, "Assets/_Project/Scripts/Utils/MaldivesLayout.cs")

SEED = 20260819

# ── 규모 배율 ────────────────────────────────────────────────────────────────
# 디렉터 결정(2026-08-19): "면적 5~10배". 그 가운데인 **면적 7.6배 = 선형 2.75배**를 쓴다.
# 예전 배치가 10.2 × 10.5 km였으므로 새 배치는 약 28 × 28 km다.
# 이웃 섬 간격 중앙값 342 m → 약 940 m. 돛(4.0 m/s)으로 약 4분, 모터(6.0)로 2.6분 —
# 한 번의 항해가 게임 시간 0.3~0.4일로 떨어져 "하루의 일과"가 된다.
# 디렉터 재조정(2026-08-20): "가까운 섬 간격은 20~30% 멀리, 멀리 떨어진 섬 간격은 10~20% 가깝게."
# 즉 **거리 폭을 좁힌다** — 붙어 있던 것은 벌리고 멀리 있던 것은 당긴다.
# 가까운 쪽 ×1.25, 먼 쪽(월드 상자) ×0.85로 잡았다(각 범위의 가운데).
HALF_EXTENT   = 11900.0   # 14000 × 0.85. 좌표는 ±11.9 km에 들어간다
MIN_SPACING   = 1075.0    # 780 × 1.38. 같은 군집 안에서 두 섬 중심 사이 최소 거리.
                          # ★ 1.25배로만 올렸더니 이웃 중앙값이 +13%밖에 안 올랐다 —
                          #   군집 사이 간격을 0.85배로 줄인 탓에 군집 밖 이웃이 가까워져
                          #   중앙값을 끌어내린다. 최소 간격을 더 올려 보정한다.

# ★ 균일 산포가 아니라 **군집(환초) 배치**를 쓴다. 50섬을 761 km²에 균일하게 뿌리면
#   이웃까지 중앙값 2.1 km(돛으로 8.7분)가 되어 섬 하나 옮기는 데 하루가 다 간다.
#   실제 몰디브가 그렇듯 몇 섬씩 묶인 군집을 넓게 떨어뜨리면,
#   군집 안은 짧은 항해(3~4분)로 돌고 군집 사이는 진짜 원정이 된다.
CLUSTER_COUNT       = 13       # 군집 수
CLUSTER_MIN_GAP     = 3570.0   # 4200 × 0.85. 군집 중심끼리 최소 거리
CLUSTER_RADIUS      = (965.0, 2895.0)   # (700, 2100) × 1.38. 군집 반경 범위

# 시작 섬에서 이 거리 구간마다 군집을 하나씩 **보장**한다(징검다리).
# 구석에서 시작하면 시작 섬 쪽 사분면이 비기 쉬워서, 여기만 난수에 맡기지 않는다.
STEPPING_STONE_RINGS = [(3200.0, 4800.0), (5600.0, 7600.0)]

# ★ oceanSize(씬 값 40000 = ±20 km)를 넘으면 섬이 바다 밖에 놓인다.
#   HALF_EXTENT는 반드시 그보다 넉넉히 작아야 한다.
OCEAN_HALF    = 20000.0

# ── 시작 섬 ──────────────────────────────────────────────────────────────────
# "시작 섬은 최대한 구석에서". 정확한 모서리에 두면 절반의 방위에 아무것도 없어
# 나침반이 죽으므로, 모서리에서 1.7 km 안쪽에 둔다.
START_POS = (-10400.0, -10400.0)   # 상자가 ±11.9 km로 줄면서 같이 당겼다(모서리에서 1.5 km 안쪽)

# ── 규모 배치 규칙 (시작 섬 기준 거리, m) ────────────────────────────────────
# "시작섬 근처에는 가급적 소·중". 초반에 대형/특대를 만나면 위험요소 8~16마리와
# 곰을 준비 없이 마주치게 된다.
NEAR_RADIUS      = 4000.0    # 이 안쪽은 무조건 소형만
MEDIUM_MIN_DIST  = 4000.0    # 중형이 나타나기 시작하는 거리
LARGE_MIN_DIST   = 8000.0    # 대형이 나타나기 시작하는 거리
# ── 특대 섬 ──────────────────────────────────────────────────────────────────
# 디렉터 지시(2026-08-20): "특대형을 시작섬에서 최대한 멀리, 그리고 특대형만 단독으로 위치 지정."
# 군집에 끼워 넣지 않고 **시작 섬의 대각선 반대편 구석에 혼자** 놓는다.
# 상자 안쪽으로 조금 물리는 이유: 정확히 모서리면 접근 방위가 한쪽으로만 열려 답답하다.
XL_CORNER_INSET  = 1200.0    # 상자 모서리에서 안쪽으로 물리는 거리
XL_SOLITUDE      = 4200.0    # 다른 섬이 이 거리 안에 들어오지 못한다(= 진짜 외딴섬)

COUNTS = {"Small": 36, "Medium": 10, "Large": 3, "ExtraLarge": 1}   # 합 50, 예전과 동일


def generate_positions(rng, count):
    """
    군집(환초) 배치. 먼저 군집 중심을 넓게 흩고, 각 군집 안에 섬을 촘촘히 놓는다.
    시작 섬은 남서쪽 구석 군집의 일원이라 근처에 항상 이웃이 몇 개 있다.
    """
    # 0) 특대 섬 자리를 **먼저** 확정한다. 시작 섬의 대각선 반대편 구석, 혼자.
    #    나중에 고르면 군집에 끼어 이웃이 생긴다 - "단독"이 요구사항이라 자리부터 잡는다.
    sgnx = 1.0 if START_POS[0] < 0 else -1.0
    sgnz = 1.0 if START_POS[1] < 0 else -1.0
    xl_pos = (sgnx * (HALF_EXTENT - XL_CORNER_INSET),
              sgnz * (HALF_EXTENT - XL_CORNER_INSET))

    # 1) 군집 중심 — 서로 CLUSTER_MIN_GAP 이상 떨어뜨린다. 첫 번째는 시작 섬 자리.
    #    특대 섬 자리도 미리 넣어 두면 군집이 그 근처에 못 생긴다(외딴섬 보장).
    centers = [START_POS]

    # ★ 시작 섬 주변 징검다리를 **강제로** 심는다. 난수에만 맡겼더니 시작 군집 다음 섬이
    #   8.2 km 밖에 있어(그 사이 6.6 km가 완전 공백) 초반이 통째로 막혔다.
    #   구석에서 시작하는 배치에서는 시작 섬 쪽이 원래 비기 쉬우므로 여기만 손으로 보장한다.
    sx0, sz0 = START_POS
    for lo, hi in STEPPING_STONE_RINGS:
        for _ in range(4000):
            ang = rng.uniform(0, math.tau)
            r = rng.uniform(lo, hi)
            x, z = sx0 + math.cos(ang) * r, sz0 + math.sin(ang) * r
            if abs(x) > HALF_EXTENT or abs(z) > HALF_EXTENT:
                continue
            if all((x - cx) ** 2 + (z - cz) ** 2 >= CLUSTER_MIN_GAP ** 2 for cx, cz in centers):
                centers.append((x, z))
                break
        else:
            raise SystemExit(f"징검다리 군집 배치 실패: {lo}~{hi}m")

    tries = 0
    while len(centers) < CLUSTER_COUNT and tries < 200000:
        tries += 1
        x = rng.uniform(-HALF_EXTENT, HALF_EXTENT)
        z = rng.uniform(-HALF_EXTENT, HALF_EXTENT)
        if (x - xl_pos[0]) ** 2 + (z - xl_pos[1]) ** 2 < (XL_SOLITUDE + CLUSTER_RADIUS[1]) ** 2:
            continue   # 특대 섬 주변은 비워 둔다
        if all((x - cx) ** 2 + (z - cz) ** 2 >= CLUSTER_MIN_GAP ** 2 for cx, cz in centers):
            centers.append((x, z))
    if len(centers) < CLUSTER_COUNT:
        raise SystemExit("군집 중심 배치 실패 — CLUSTER_MIN_GAP을 줄여라")

    # 2) 각 군집에 몇 개씩 넣을지. 시작 군집은 작게(초반에 고를 것이 너무 많으면 산만하다).
    quota = [0] * CLUSTER_COUNT
    quota[0] = 4
    remaining = (count - 1) - sum(quota)   # -1 = 특대 섬(군집 밖에 혼자 선다)
    order = list(range(1, CLUSTER_COUNT))
    while remaining > 0:
        rng.shuffle(order)
        for ci in order:
            if remaining <= 0:
                break
            if quota[ci] < 6:
                quota[ci] += 1
                remaining -= 1

    # 3) 군집 안에 실제 좌표를 찍는다.
    pts = [START_POS]
    for ci, (cx, cz) in enumerate(centers):
        need = quota[ci] - (1 if ci == 0 else 0)
        # 반경 하한: 칸 수만큼의 섬이 MIN_SPACING을 지키며 들어갈 수 있어야 한다.
        # (하한이 없으면 작은 반경이 뽑혔을 때 영원히 자리를 못 찾는다.)
        radius = max(rng.uniform(*CLUSTER_RADIUS), MIN_SPACING * math.sqrt(max(1, quota[ci])) * 1.05)
        placed, tries = 0, 0
        while placed < need and tries < 60000:
            tries += 1
            ang = rng.uniform(0, math.tau)
            # sqrt를 씌워 원 안에 고르게(중심으로 몰리지 않게) 찍는다.
            r = radius * math.sqrt(rng.random())
            x, z = cx + math.cos(ang) * r, cz + math.sin(ang) * r
            if abs(x) > HALF_EXTENT or abs(z) > HALF_EXTENT:
                continue
            if (x - xl_pos[0]) ** 2 + (z - xl_pos[1]) ** 2 < XL_SOLITUDE ** 2:
                continue
            if all((x - px) ** 2 + (z - pz) ** 2 >= MIN_SPACING ** 2 for px, pz in pts):
                pts.append((x, z))
                placed += 1
        if placed < need:
            raise SystemExit(f"군집 {ci} 배치 실패 ({placed}/{need}) — CLUSTER_RADIUS를 늘려라")
    pts.append(xl_pos)   # 특대 섬은 언제나 마지막 = 군집 어디에도 속하지 않는다

    if len(pts) != count:
        raise SystemExit(f"섬 수 불일치 {len(pts)}/{count}")
    return pts


def assign_sizes(pts):
    """거리 순으로 규모를 배정한다. 가까운 곳은 소형, 먼 곳에 대형·특대."""
    sx, sz = pts[0]
    others = [(i, math.hypot(x - sx, z - sz)) for i, (x, z) in enumerate(pts) if i > 0]
    others.sort(key=lambda t: t[1])

    sizes = {0: "Small"}  # 시작 섬은 언제나 소형(WorldMapManager가 그렇게 가정한다)

    # 특대: generate_positions가 **맨 마지막에** 붙인 외딴 자리 하나. 거리로 고르지 않는다.
    xl_index = len(pts) - 1
    sizes[xl_index] = "ExtraLarge"

    # 대형 3: LARGE_MIN_DIST 밖에서 거리 구간을 3등분해 하나씩.
    # far는 **특대를 뺀** 나머지 중 가장 먼 거리다 - 특대가 구석에 혼자 있어서
    # 그걸 기준으로 삼으면 대형 하나가 특대 옆(빈 바다)으로 끌려간다.
    pool = [t for t in others if t[1] >= LARGE_MIN_DIST and t[0] not in sizes]
    far = max(t[1] for t in others if t[0] != xl_index)
    for target in (LARGE_MIN_DIST + (far - LARGE_MIN_DIST) * f for f in (0.15, 0.5, 0.85)):
        pick = min((t for t in pool if t[0] not in sizes), key=lambda t: abs(t[1] - target))
        sizes[pick[0]] = "Large"

    # 중형 10: MEDIUM_MIN_DIST 밖에서 거리 축을 따라 고르게
    pool = [t for t in others if t[1] >= MEDIUM_MIN_DIST and t[0] not in sizes]
    for f in (i / 9.0 for i in range(10)):
        target = MEDIUM_MIN_DIST + (far - MEDIUM_MIN_DIST) * f
        pick = min((t for t in pool if t[0] not in sizes), key=lambda t: abs(t[1] - target))
        sizes[pick[0]] = "Medium"

    for i, _ in others:
        sizes.setdefault(i, "Small")

    # 근거리 규칙 검증 — 조용히 깨지면 초반 난이도가 통째로 무너진다
    for i, dist in others:
        if dist < NEAR_RADIUS and sizes[i] != "Small":
            raise SystemExit(f"근거리 규칙 위반: {i}번 {sizes[i]} @ {dist:.0f}m")
    return sizes


def main():
    masks = json.load(open(os.path.join(HERE, "masks.json"), encoding="utf-8"))
    rng = random.Random(SEED)
    pts = generate_positions(rng, len(masks))
    sizes = assign_sizes(pts)

    # 윤곽 재사용: 규모가 같은 원본 윤곽을 우선 쓴다(큰 섬 윤곽을 작은 섬에 씌우면
    # 실루엣이 어색해진다). 남으면 아무 윤곽이나 돌려 쓴다.
    by_size = {}
    for m in masks:
        by_size.setdefault(m["size"], []).append(m)
    leftovers = list(masks)
    chosen = []
    for i in range(len(pts)):
        want = sizes[i]
        pool = by_size.get(want) or []
        src = pool.pop(0) if pool else leftovers.pop(0)
        if src in leftovers:
            leftovers.remove(src)
        chosen.append(src)

    xs = [p[0] for p in pts]; zs = [p[1] for p in pts]
    span_x, span_z = max(xs) - min(xs), max(zs) - min(zs)
    assert max(abs(v) for v in xs + zs) < OCEAN_HALF, "섬이 바다 평면 밖으로 나갔다"

    dists = []
    for i, (x, z) in enumerate(pts):
        nearest = min(math.hypot(x - ox, z - oz)
                      for j, (ox, oz) in enumerate(pts) if j != i)
        dists.append(nearest)
    dists.sort()
    median = dists[len(dists) // 2]

    sx, sz = pts[0]
    def d0(i): return math.hypot(pts[i][0] - sx, pts[i][1] - sz)
    xl_i = next(i for i, s in sizes.items() if s == "ExtraLarge")
    large_d = sorted(d0(i) for i, s in sizes.items() if s == "Large")

    lines = []
    lines.append(f"// 자동 생성: Tools/mapgen/gen_layout.py (SEED={SEED}) — 손으로 고치지 말고 재생성해라.")
    lines.append(f"// 섬 {len(pts)}개 · span {span_x/1000:.1f} x {span_z/1000:.1f} km · 최근접 {dists[0]:.0f}m · 이웃 중앙값 {median:.0f}m")
    lines.append(f"// 시작 섬 = 남서쪽 구석({sx:.0f}, {sz:.0f}) · 반경 {NEAR_RADIUS/1000:.0f}km 안은 전부 소형")
    lines.append(f"// 특대 섬까지 {d0(xl_i)/1000:.1f}km · 대형 3개까지 {', '.join(f'{d/1000:.1f}' for d in large_d)}km")
    lines.append("// 윤곽(mask 64샘플)은 몰디브 Z28 환초 실측값 재사용.")
    lines.append("namespace MakeGame.Data")
    lines.append("{")
    lines.append("    /// <summary>섬 배치·윤곽 데이터. WorldMapManager가 소비한다. 데이터 길이가 곧 섬 수다.</summary>")
    lines.append("    public static class MaldivesLayout")
    lines.append("    {")
    lines.append("        public const int SampleCount = 64;")
    lines.append('        public const string Source = "Maldives Z28 outlines, regenerated layout 2026-08-19";')
    lines.append("")
    lines.append("        public struct Entry")
    lines.append("        {")
    lines.append("            public string sourceId;")
    lines.append("            public IslandSize size;")
    lines.append("            public float posX;")
    lines.append("            public float posZ;")
    lines.append("            public float[] mask;")
    lines.append("        }")
    lines.append("")
    lines.append("        public static readonly Entry[] Islands =")
    lines.append("        {")
    for i, (x, z) in enumerate(pts):
        src = chosen[i]
        mask = ", ".join(f"{v:.4f}f" for v in src["mask"])
        lines.append("            new Entry")
        lines.append("            {")
        lines.append(f'                sourceId = "{src["sourceId"]}", size = IslandSize.{sizes[i]},')
        lines.append(f"                posX = {x:.1f}f, posZ = {z:.1f}f,")
        lines.append(f"                mask = new float[] {{ {mask} }},")
        lines.append("            },")
    lines.append("        };")
    lines.append("    }")
    lines.append("}")
    open(OUT, "w", encoding="utf-8").write("\n".join(lines) + "\n")

    print(f"span      {span_x/1000:.1f} x {span_z/1000:.1f} km  (면적 {span_x*span_z/1e6:.0f} km²)")
    print(f"최근접    {dists[0]:.0f} m · 이웃 중앙값 {median:.0f} m")
    print(f"시작 섬   ({sx:.0f}, {sz:.0f})")
    print(f"특대까지  {d0(xl_i)/1000:.2f} km")
    print(f"대형까지  {', '.join(f'{d/1000:.2f}' for d in large_d)} km")
    print(f"근거리    반경 {NEAR_RADIUS/1000:.0f}km 안 섬 {sum(1 for i in range(1,len(pts)) if d0(i) < NEAR_RADIUS)}개 (전부 소형)")
    from collections import Counter
    print("규모      ", dict(Counter(sizes.values())))


if __name__ == "__main__":
    main()
