#!/usr/bin/env python3
"""
surface_set - 재질 시스템 기본 텍스처 5종 절차 생성 (2026-08-18, 표면 질감 일괄).

    python3 Tools/textures/surface_set.py

산출물 (Assets/_Project/Resources/Textures/):
  noise.png  중립 얼룩 노이즈  - GetMaterial 기본값. 소품 대부분이 이걸 기대한다.
  leaf.png   풀잎 결 모틀     - 지형 본체/GrassCap/잎 계열(CreateDefaultTerrainMaterial).
  sand.png   모래 입자+물결    - 해변 캡/해저 스커트.
  metal.png  브러시드 금속+긁힘 - 금속 소품/여객기/경비행기.
  water.png  물결 간섭 그레인  - MGOcean _BaseMap(1타일=10m 계약).

왜 필요한가
  StructureVisualBuilder.CreateColorMaterial은 Textures/<kind>를 곱셈 텍스처로 기대하는데
  이 다섯 파일이 지금까지 없어서 조용히 null - 게임 표면 대부분이 민무늬 단색이었다.
  전부 **곱셈용**이므로 평균 밝기를 높게(0.80~0.88) 잡아 색을 어둡히지 않고 질감만 싣는다.

전부 무이음(tileable) - 주기적 값 노이즈(래핑 보간)로 만든다. 시드 고정.
"""

import math
import os

import numpy as np
from PIL import Image, ImageFilter

SIZE = 512


def periodic_value_noise(size, cells, seed, octaves=4, persistence=0.55):
    """무이음 다옥타브 값 노이즈 [0,1]. cells = 최저 옥타브 격자 수."""
    rng = np.random.default_rng(seed)
    out = np.zeros((size, size), dtype=np.float64)
    amp = 1.0
    total = 0.0
    for o in range(octaves):
        n = cells * (2 ** o)
        grid = rng.random((n, n))
        # 주기적 이중선형 업샘플
        idx = np.linspace(0, n, size, endpoint=False)
        i0 = np.floor(idx).astype(int) % n
        i1 = (i0 + 1) % n
        f = idx - np.floor(idx)
        # 스무스스텝
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


def save_mul(path, arr_rgb):
    """[0,1] RGB 배열 저장."""
    img = Image.fromarray((np.clip(arr_rgb, 0, 1) * 255).astype(np.uint8), "RGB")
    img.save(path)
    print("저장:", os.path.basename(path))


def make_noise(out_dir):
    n = periodic_value_noise(SIZE, 6, 11001, octaves=5)
    v = 0.78 + 0.20 * n  # 평균 ~0.88
    rgb = np.stack([v, v, v], axis=-1)
    save_mul(os.path.join(out_dir, "noise.png"), rgb)


def make_leaf(out_dir):
    base = periodic_value_noise(SIZE, 8, 12001, octaves=5)
    # 세로 결(풀잎 방향 스트릭): 가로로 얇게 늘인 고주파 노이즈
    streak = periodic_value_noise(SIZE, 48, 12007, octaves=2)
    streak = np.asarray(Image.fromarray((streak * 255).astype(np.uint8))
                        .resize((SIZE, SIZE // 6)).resize((SIZE, SIZE), Image.BILINEAR)) / 255.0
    v = 0.72 + 0.16 * base + 0.12 * streak
    # 미세한 색조: 초록 쪽 살짝(곱셈이라 은은하게)
    r = v * 0.95
    g = v * 1.02
    b = v * 0.90
    save_mul(os.path.join(out_dir, "leaf.png"), np.stack([r, g, b], axis=-1))


def make_sand(out_dir):
    grain = periodic_value_noise(SIZE, 64, 13001, octaves=3, persistence=0.7)
    ripple_phase = periodic_value_noise(SIZE, 4, 13007, octaves=2)
    yy = np.linspace(0, 2 * math.pi * 7, SIZE)[:, np.newaxis]
    ripple = 0.5 + 0.5 * np.sin(yy + ripple_phase * 5.0)
    v = 0.74 + 0.14 * grain + 0.10 * ripple
    r = v * 1.03
    g = v * 0.99
    b = v * 0.92
    save_mul(os.path.join(out_dir, "sand.png"), np.stack([r, g, b], axis=-1))


def make_metal(out_dir):
    streak = periodic_value_noise(SIZE, 40, 14001, octaves=2)
    streak = np.asarray(Image.fromarray((streak * 255).astype(np.uint8))
                        .resize((SIZE // 8, SIZE)).resize((SIZE, SIZE), Image.BILINEAR)) / 255.0
    base = periodic_value_noise(SIZE, 5, 14007, octaves=3)
    v = 0.76 + 0.12 * streak + 0.10 * base
    # 긁힘 몇 줄(가는 밝은 세로선을 노이즈 문턱으로)
    scratch = periodic_value_noise(SIZE, 90, 14013, octaves=1)
    v = np.where(scratch > 0.90, np.minimum(1.0, v + 0.10), v)
    rgb = np.stack([v * 0.99, v, v * 1.02], axis=-1)
    save_mul(os.path.join(out_dir, "metal.png"), rgb)


def make_water(out_dir):
    a = periodic_value_noise(SIZE, 7, 15001, octaves=4)
    b = periodic_value_noise(SIZE, 11, 15007, octaves=3)
    inter = 0.5 + 0.5 * np.sin((a * 6.0 + b * 5.0) * math.pi)
    v = 0.74 + 0.12 * a + 0.12 * inter * b
    rgb = np.stack([v * 0.96, v * 1.00, v * 1.04], axis=-1)
    save_mul(os.path.join(out_dir, "water.png"), rgb)


def main():
    out_dir = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)),
                                           "..", "..", "Assets", "_Project", "Resources", "Textures"))
    os.makedirs(out_dir, exist_ok=True)
    make_noise(out_dir)
    make_leaf(out_dir)
    make_sand(out_dir)
    make_metal(out_dir)
    make_water(out_dir)


if __name__ == "__main__":
    main()
