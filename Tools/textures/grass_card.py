#!/usr/bin/env python3
"""
grass_card.png - 잔디 카드 아틀라스 절차 생성 (2026-08-18, 잔디 사실화 v2).

    python3 Tools/textures/grass_card.py

산출물: Assets/_Project/Resources/Textures/grass_card.png (1024x1024, RGBA)

왜 필요한가
  실사급 잔디(사용자 레퍼런스)의 밀도감은 blade 지오메트리가 아니라 **카드 한 장에
  풀잎 수십 가닥이 그려진 알파 컷아웃 텍스처**에서 온다. 쿼드 1장 = 풀잎 20가닥이 되어
  같은 인스턴스 수로 10배의 밀도감이 난다.

아틀라스 2x2 (셀 512px):
  (0,0) 촘촘한 초록 클러스터 22가닥   (1,0) 성긴/가는 클러스터 14가닥 + 이삭 3
  (0,1) 마른 풀 섞인 클러스터 18가닥  (1,1) 꽃 스파이크(분홍 루핀풍) 3대 + 풀 8가닥
  UV: 셰이더가 인스턴스 해시로 셀을 고른다(꽃 셀은 별도 머티리얼 배치가 사용).

컷아웃 할로(검은 테두리) 방지: 알파 0 영역에 이웃 색을 번지게(dilate) 채운다.
시드 90210 고정 - 같은 시드 = 같은 PNG.
"""

import math
import os
import random

from PIL import Image, ImageDraw, ImageFilter

SEED = 90210
CELL = 1024
SS = 2  # 슈퍼샘플링 배율


def lerp(a, b, t):
    return a + (b - a) * t


def draw_blade(draw, rng, base_x, base_y, height, width, lean, curve, root_c, tip_c):
    """풀잎 한 가닥: 2차 베지어를 따라 이어지는 세그먼트 폴리곤(끊김 없는 연속 스트로크)."""
    cx = base_x + lean * height * 0.6 + curve * height * 0.25
    cy = base_y - height * 0.55
    tx = base_x + lean * height
    ty = base_y - height
    steps = 30
    pts = []
    for i in range(steps + 1):
        t = i / steps
        x = lerp(lerp(base_x, cx, t), lerp(cx, tx, t), t)
        y = lerp(lerp(base_y, cy, t), lerp(cy, ty, t), t)
        w = max(0.8, width * (1.0 - t) ** 0.85)
        pts.append((x, y, w, t))
    def stroke(width_mul, color_fn, t_lo=0.0, t_hi=1.0):
        for i in range(steps):
            x0, y0, w0, t0 = pts[i]
            x1, y1, w1, t1 = pts[i + 1]
            if t1 < t_lo or t0 > t_hi:
                continue
            dx, dy = x1 - x0, y1 - y0
            ln = math.hypot(dx, dy) or 1.0
            nx, ny = -dy / ln, dx / ln
            w0m, w1m = w0 * width_mul, w1 * width_mul
            draw.polygon([(x0 + nx * w0m, y0 + ny * w0m), (x0 - nx * w0m, y0 - ny * w0m),
                          (x1 - nx * w1m, y1 - ny * w1m), (x1 + nx * w1m, y1 + ny * w1m)],
                         fill=color_fn(t0))

    def ao(t):
        # 뿌리 AO: 아래 18% 구간을 어둡게 - 지면 접점의 접촉 음영.
        k = min(1.0, t / 0.18)
        return 0.58 + 0.42 * (k * k * (3 - 2 * k))

    def body_color(t):
        base = [lerp(root_c[k], tip_c[k], t ** 1.1) * ao(t) for k in range(3)]
        return tuple(int(min(255, v)) for v in base) + (255,)

    def shadow_color(t):
        base = [lerp(root_c[k], tip_c[k], t ** 1.1) * ao(t) * 0.62 for k in range(3)]
        return tuple(int(v) for v in base) + (255,)

    def vein_color(t):
        base = [min(255, lerp(root_c[k], tip_c[k], t ** 1.1) * ao(t) * 1.38 + 18) for k in range(3)]
        return tuple(int(v) for v in base) + (255,)

    stroke(1.22, shadow_color)            # 윤곽 그림자(깊이감)
    stroke(1.0, body_color)               # 본체
    stroke(0.34, vein_color, 0.08, 0.9)   # 중심 잎맥 하이라이트


def draw_seed_head(draw, rng, x, y):
    """이삭: 가는 줄기 끝의 낟알 다발."""
    for i in range(7):
        a = rng.uniform(-0.9, 0.9)
        gx = x + a * 7
        gy = y - abs(a) * 5 - rng.uniform(0, 9)
        c = (198 + rng.randint(-12, 12), 186 + rng.randint(-12, 12), 122, 255)
        draw.ellipse((gx - 2.6, gy - 4.2, gx + 2.6, gy + 4.2), fill=c)


def draw_flower_spike(draw, rng, base_x, base_y, height, color_a, color_b):
    """루핀/분홍바늘꽃풍 수상꽃차례: 줄기 + 위로 갈수록 작아지는 꽃잎 뭉치."""
    lean = rng.uniform(-0.10, 0.10)
    # 줄기(연속 폴리곤 - draw_blade와 같은 이유로 점선 방지)
    draw_blade(draw, rng, base_x, base_y, height, 3.2, lean, 0.0,
               (74, 96, 52), (86, 110, 60))
    # 꽃 뭉치(아래 60%~꼭대기)
    n = 26
    for i in range(n):
        t = 0.38 + 0.62 * (i / (n - 1))
        x = base_x + lean * height * t
        y = base_y - height * t
        r = lerp(10.5, 3.0, (t - 0.38) / 0.62) * rng.uniform(0.8, 1.15)
        for _ in range(5):
            px = x + rng.uniform(-r, r) * 0.9
            py = y + rng.uniform(-r * 0.5, r * 0.5)
            c = tuple(int(lerp(color_a[k], color_b[k], rng.random())) for k in range(3)) + (255,)
            pr = r * rng.uniform(0.42, 0.68)
            draw.ellipse((px - pr, py - pr, px + pr, py + pr), fill=c)


def make_cell(kind, rng):
    img = Image.new("RGBA", (CELL * SS, CELL * SS), (0, 0, 0, 0))
    draw = ImageDraw.Draw(img)
    W = CELL * SS
    base_y = W - 6 * SS

    GREEN_ROOT = (44, 74, 34)
    GREEN_TIP = (118, 156, 62)
    DRY_ROOT = (96, 88, 44)
    DRY_TIP = (176, 160, 92)

    def blades(count, dry_ratio=0.0, height_lo=0.55, height_hi=0.97, width=5.6):
        for _ in range(count):
            bx = rng.uniform(W * 0.06, W * 0.94)
            h = W * rng.uniform(height_lo, height_hi)
            dry = rng.random() < dry_ratio
            rc, tc = (DRY_ROOT, DRY_TIP) if dry else (GREEN_ROOT, GREEN_TIP)
            # 명도 지터
            j = rng.uniform(0.85, 1.14)
            rc = tuple(min(255, int(c * j)) for c in rc)
            tc = tuple(min(255, int(c * j)) for c in tc)
            draw_blade(draw, rng, bx, base_y, h,
                       width * SS * rng.uniform(0.75, 1.25) / 2,
                       rng.uniform(-0.55, 0.55), rng.uniform(-0.85, 0.85), rc, tc)

    if kind == "dense":
        blades(64, dry_ratio=0.08, width=7.8)
    elif kind == "thin":
        blades(30, dry_ratio=0.12, height_lo=0.6, height_hi=1.0, width=5.0)
        for _ in range(3):
            bx = rng.uniform(W * 0.2, W * 0.8)
            h = W * rng.uniform(0.8, 0.98)
            lean = rng.uniform(-0.18, 0.18)
            draw_blade(draw, rng, bx, base_y, h, 2.2 * SS,
                       lean, rng.uniform(-0.3, 0.3), (110, 116, 66), (172, 168, 108))
            draw_seed_head(draw, rng, bx + lean * h, base_y - h)
    elif kind == "dry":
        blades(48, dry_ratio=0.55, width=7.0)
    else:  # flower
        blades(16, dry_ratio=0.10, height_lo=0.4, height_hi=0.75, width=6.2)
        # 분홍 주조 + 흰색·연보라 액센트 (레퍼런스: 분홍 무리 속 흰 꽃이 드문드문)
        draw_flower_spike(draw, rng, W * rng.uniform(0.28, 0.38), base_y, W * 0.90,
                          (214, 96, 168), (240, 150, 205))
        draw_flower_spike(draw, rng, W * rng.uniform(0.58, 0.70), base_y, W * 0.76,
                          (200, 84, 152), (236, 140, 196))
        draw_flower_spike(draw, rng, W * rng.uniform(0.44, 0.54), base_y, W * 0.62,
                          (196, 150, 220), (226, 194, 244))   # 연보라
        draw_flower_spike(draw, rng, W * rng.uniform(0.16, 0.24), base_y, W * 0.52,
                          (238, 235, 226), (252, 250, 244))   # 흰색(짧은 대)
        draw_flower_spike(draw, rng, W * rng.uniform(0.76, 0.86), base_y, W * 0.46,
                          (236, 214, 130), (250, 236, 170))   # 노랑 액센트(더 짧게)

    img = img.resize((CELL, CELL), Image.LANCZOS)
    return img


def dilate_colors(img, passes=6):
    """알파 0 픽셀에 이웃 색을 번지게 채워 컷아웃 검은 할로를 없앤다(알파는 보존)."""
    rgb = img.convert("RGB")
    alpha = img.getchannel("A")
    mask = alpha.point(lambda a: 255 if a > 8 else 0)
    for _ in range(passes):
        grown = rgb.filter(ImageFilter.MaxFilter(3))
        rgb = Image.composite(rgb, grown, mask)
        mask = mask.filter(ImageFilter.MaxFilter(3))
    out = rgb.convert("RGBA")
    out.putalpha(alpha)
    return out


def main():
    rng = random.Random(SEED)
    atlas = Image.new("RGBA", (CELL * 2, CELL * 2), (0, 0, 0, 0))
    # 셀 배열: (0,0) dense / (1,0) thin / (0,1) dry / (1,1) flower
    # PIL 좌표는 위가 0이므로 UV(0,0)=왼쪽 아래 = 이미지 왼쪽 위와 뒤집힘에 주의 -
    # 셰이더 쪽 셀 선택은 UV 기준 (0,0)dense (1,0)thin (0,1)dry (1,1)flower 로 계약한다.
    cells = {
        (0, 1): make_cell("dense", random.Random(SEED + 1)),   # UV (0,0) = 이미지 왼쪽 아래
        (1, 1): make_cell("thin", random.Random(SEED + 2)),    # UV (1,0)
        (0, 0): make_cell("dry", random.Random(SEED + 3)),     # UV (0,1) = 이미지 왼쪽 위
        (1, 0): make_cell("flower", random.Random(SEED + 4)),  # UV (1,1)
    }
    for (cx, cy), im in cells.items():
        atlas.paste(im, (cx * CELL, cy * CELL))

    atlas = dilate_colors(atlas)

    out_dir = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..",
                           "Assets", "_Project", "Resources", "Textures")
    out_dir = os.path.abspath(out_dir)
    os.makedirs(out_dir, exist_ok=True)
    out = os.path.join(out_dir, "grass_card.png")
    atlas.save(out)
    print("저장:", out, atlas.size)


if __name__ == "__main__":
    main()
