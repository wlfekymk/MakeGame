#!/usr/bin/env python3
"""
caustics - 수중 카우스틱(수면 굴절 빛무늬) 텍스처 절차 생성 (수중 시각 웨이브).

    python3 Tools/textures/caustics.py

산출물 (Assets/_Project/Resources/Textures/):
  caustics.png  512x512 무이음 카우스틱 그물망 - MGCaustics.shader의 _CausticsMap.

왜 이 방식인가 (보로노이 F2-F1 거리장)
  카우스틱은 "수면의 곡률이 빛을 한 선으로 모은 자리"라 실제로는 **셀 경계에 몰린 가는 그물망**
  으로 보인다. 그 형태를 가장 싸게 재현하는 정석이 보로노이 2차 거리장이다:
  각 픽셀에서 가장 가까운 씨앗까지 거리 F1, 두 번째 F2를 구하면 (F2 - F1)은 셀 경계에서 0이고
  안쪽으로 갈수록 커진다. 즉 `1 - smoothstep(0, w, F2 - F1)` 이 곧 그물망 필라멘트다.
  씨앗 격자를 노이즈로 왜곡(domain warp)해 보로노이 특유의 규칙적인 벌집 느낌을 깨고,
  주파수가 다른 층 2장을 합성해 굵은 그물 + 잔가지가 겹친 실제 카우스틱 구조를 만든다.

애니메이션은 **텍스처가 아니라 셰이더가** 만든다 (MGCaustics)
  프레임 시퀀스(32~64장)는 용량이 수십 MB로 뛴다. 대신 이 한 장을 서로 다른 배율/속도로
  두 번 흘려 겹치고 min()으로 합치면(셰이더 쪽 계약) 정지 텍스처 한 장으로 계속 변형되는
  그물망이 나온다 - 게임 업계 표준 트릭이고 용량은 512² 한 장(약 100KB)뿐이다.

무이음(tileable)
  씨앗 거리 계산을 **토러스 랩(dx = min(|dx|, 1-|dx|))** 으로 하고, 왜곡 노이즈도 주기적
  값 노이즈(surface_set.py와 같은 함수)를 쓴다. 따라서 상하좌우 어느 쪽으로 이어 붙여도
  이음매가 없다 - 셰이더가 월드 XZ를 그대로 UV로 쓰므로 무이음이 필수다.

시드 고정
  아래 SEED_* 상수만이 유일한 난수원이다(np.random.default_rng). 두 번 실행하면
  바이트 단위로 같은 PNG가 나온다(검증: md5 2회 대조).
"""

import os

import numpy as np
from PIL import Image, ImageFilter

SIZE = 512

# 유일한 난수원. 바꾸지 않는 한 산출물은 바이트 단위로 재현된다.
SEED_WARP_A = 31001
SEED_WARP_B = 31007
SEED_CELLS_A = 31013
SEED_CELLS_B = 31019
SEED_SHADE = 31031


def periodic_value_noise(size, cells, seed, octaves=4, persistence=0.55):
    """무이음 다옥타브 값 노이즈 [0,1]. surface_set.py와 같은 구현(툴 간 사본 정책)."""
    rng = np.random.default_rng(seed)
    out = np.zeros((size, size), dtype=np.float64)
    amp = 1.0
    total = 0.0
    for o in range(octaves):
        n = cells * (2 ** o)
        grid = rng.random((n, n))
        idx = np.linspace(0, n, size, endpoint=False)
        i0 = np.floor(idx).astype(int) % n
        i1 = (i0 + 1) % n
        f = idx - np.floor(idx)
        f = f * f * (3 - 2 * f)
        a = grid[np.ix_(i0, i0)]
        b = grid[np.ix_(i0, i1)]
        c = grid[np.ix_(i1, i0)]
        d = grid[np.ix_(i1, i1)]
        fx = f[np.newaxis, :]
        fy = f[:, np.newaxis]
        layer = a * (1 - fx) * (1 - fy) + b * fx * (1 - fy) + c * (1 - fx) * fy + d * fx * fy
        out += layer * amp
        total += amp
        amp *= persistence
    return out / total


def jittered_seeds(cells, seed, jitter=0.42):
    """cells x cells 지터 격자 씨앗 [0,1)². 순수 랜덤보다 밀도가 고르다(빈 구멍이 없다)."""
    rng = np.random.default_rng(seed)
    step = 1.0 / cells
    gy, gx = np.mgrid[0:cells, 0:cells]
    base = np.stack([(gx + 0.5) * step, (gy + 0.5) * step], axis=-1).reshape(-1, 2)
    offset = (rng.random((cells * cells, 2)) - 0.5) * (2.0 * jitter * step)
    return np.mod(base + offset, 1.0)


def voronoi_web(cells, seed_cells, seed_warp, warp_amp, width):
    """
    보로노이 F2-F1 그물망 [0,1]. 1 = 셀 경계(밝은 필라멘트), 0 = 셀 안쪽(어둠).
    도메인 워프로 격자 느낌을 깨고, 거리는 토러스 랩이라 무이음이다.
    """
    lin = (np.arange(SIZE) + 0.5) / SIZE
    px = np.tile(lin[np.newaxis, :], (SIZE, 1))
    py = np.tile(lin[:, np.newaxis], (1, SIZE))

    # 도메인 워프: 주기 노이즈 2장을 [-1,1]로 옮겨 좌표를 흔든다(무이음 유지).
    wx = periodic_value_noise(SIZE, 4, seed_warp, octaves=3) * 2.0 - 1.0
    wy = periodic_value_noise(SIZE, 4, seed_warp + 977, octaves=3) * 2.0 - 1.0
    px = np.mod(px + wx * warp_amp, 1.0)
    py = np.mod(py + wy * warp_amp, 1.0)

    f1 = np.full((SIZE, SIZE), 4.0)
    f2 = np.full((SIZE, SIZE), 4.0)
    for sx, sy in jittered_seeds(cells, seed_cells):
        dx = np.abs(px - sx)
        dx = np.minimum(dx, 1.0 - dx)   # 토러스 랩 - 무이음의 핵심
        dy = np.abs(py - sy)
        dy = np.minimum(dy, 1.0 - dy)
        d = np.sqrt(dx * dx + dy * dy)
        # F1/F2 갱신(정렬 없이 두 값만 굴린다 - 씨앗 수만큼의 O(1) 갱신).
        newf1 = np.minimum(f1, d)
        f2 = np.minimum(f2, np.maximum(f1, d))
        f1 = newf1

    edge = f2 - f1
    # 경계(edge≈0)에서 1, width를 넘으면 0. smoothstep을 numpy로 직접 쓴다.
    t = np.clip(edge / width, 0.0, 1.0)
    return 1.0 - (t * t * (3.0 - 2.0 * t))


def make_caustics(out_dir):
    # 굵은 그물(저주파) + 잔가지(고주파) 2층. 층마다 씨앗/워프 시드가 달라 겹쳐도 상관성이 없다.
    coarse = voronoi_web(cells=6, seed_cells=SEED_CELLS_A, seed_warp=SEED_WARP_A,
                         warp_amp=0.090, width=0.055)
    fine = voronoi_web(cells=11, seed_cells=SEED_CELLS_B, seed_warp=SEED_WARP_B,
                       warp_amp=0.055, width=0.024)

    # 합성: 굵은 그물이 골격, 잔가지는 그 위에 얹힌다. max가 아니라 가중합인 이유는
    # 교차점(둘 다 밝은 자리)이 더 밝아져야 카우스틱 특유의 "매듭"이 생기기 때문이다.
    web = 0.80 * coarse + 0.34 * fine
    web = np.clip(web, 0.0, 1.0)

    # 저주파 밝기 얼룩: 그물 전체가 균일하게 빛나면 그물망 벽지처럼 보인다.
    # 일부 구획만 밝게 남겨 "빛이 모인 자리"가 생기게 한다.
    shade = periodic_value_noise(SIZE, 3, SEED_SHADE, octaves=3)
    web *= 0.55 + 0.75 * shade

    # 감마: 필라멘트를 가늘고 또렷하게(가산 합성이라 배경은 완전히 0이어야 지형이 뜨지 않는다).
    web = np.clip(web, 0.0, 1.0) ** 2.2

    # 아주 약한 블러 - 픽셀 계단을 없애되 필라멘트가 뭉개지지 않는 반경.
    # PIL 블러는 가장자리를 복제(클램프)하므로 그대로 걸면 이음매가 생긴다. 3x3으로 타일링해
    # 블러한 뒤 가운데를 잘라내 **랩 어라운드 블러**로 만든다(무이음 계약 유지).
    tiled = np.tile(web, (3, 3))
    img = Image.fromarray((tiled * 255.0 + 0.5).astype(np.uint8), "L")
    img = img.filter(ImageFilter.GaussianBlur(0.8))
    v = np.asarray(img).astype(np.float64)[SIZE:2 * SIZE, SIZE:2 * SIZE] / 255.0

    # 정규화: 최대치를 1로 끌어올려 셰이더의 _Intensity가 예측 가능한 범위에서 놀게 한다.
    peak = float(v.max())
    if peak > 0.0:
        v = np.clip(v / peak, 0.0, 1.0)

    # 살짝 청록(수중 빛은 붉은 파장이 먼저 죽는다). R을 낮추고 G/B를 남긴다.
    rgb = np.stack([v * 0.78, v * 1.00, v * 0.95], axis=-1)
    out = Image.fromarray((np.clip(rgb, 0, 1) * 255.0 + 0.5).astype(np.uint8), "RGB")
    path = os.path.join(out_dir, "caustics.png")
    out.save(path)
    print("저장:", os.path.basename(path), f"({SIZE}x{SIZE})")


def main():
    out_dir = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)),
                                           "..", "..", "Assets", "_Project", "Resources", "Textures"))
    os.makedirs(out_dir, exist_ok=True)
    make_caustics(out_dir)


if __name__ == "__main__":
    main()
