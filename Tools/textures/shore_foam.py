#!/usr/bin/env python3
"""
shore_foam - 해변 스와시(파도가 밀려왔다 빠지는) 거품 텍스처 절차 생성.

    python3 Tools/textures/shore_foam.py

산출물 (Assets/_Project/Resources/Textures/):
  shore_foam.png  1024x1024 RGBA 무이음 - 스와시 거품 셰이더가 샘플하는 유일한 텍스처.

왜 필요한가
  해변 스와시는 "파도가 올라왔다가 물이 모래에 스며들며 거품만 남고 사라지는" 연출이다.
  이걸 알파 페이드로 처리하면 거품 전체가 유리처럼 균일하게 투명해져서 물기가 아니라
  안개처럼 보인다. 실제로는 거품막이 **안쪽부터 구멍이 뚫리며 찢어지듯** 사라진다.
  그래서 셰이더는 알파 페이드가 아니라 **디졸브(erosion)** 를 쓴다 - 노이즈 임계값을
  0->1로 올려가며 거품 마스크를 갉아먹는다. 그러려면 마스크와 디졸브 노이즈가 **한 장에**
  같이 있어야 하고(샘플 1회로 끝나야 모바일/URP에서 싸다), 노이즈가 마스크와 살짝
  상관되어 있어야 "두꺼운 거품이 늦게 마른다"는 물리적 인상이 생긴다.
  채널 4개를 나눠 담아 텍스처 페치 1번으로 마스크/디졸브/디테일/얼룩을 전부 해결한다.

채널 계약 (셰이더가 이대로 읽는다)
  R  거품 마스크(밀도).  0 = 맨 모래, 1 = 꽉 찬 거품막.
                        파도 앞머리(아래 UV 규약의 p=0 부근)는 굵은 덩어리가 서로 붙어 있고
                        뒤로 갈수록 성긴 알갱이만 남는다. 셀 구조라 가장자리가 톱니처럼 불규칙.
  G  디졸브 노이즈.      다옥타브. 히스토그램을 **균등**하게 펴 놓았으므로
                        `step(threshold, G)` 의 threshold를 0->1로 선형으로 올리면
                        거품 면적이 선형으로 줄어든다(연출 커브를 셰이더에서 그대로 믿어도 된다).
                        R과 약한 양의 상관 - 두꺼운 거품일수록 G가 커서 늦게 지워진다.
  B  미세 디테일.        고주파 거품 알갱이. 근접 시 R만으로는 밋밋한 면을 잘게 깨는 용도.
                        보통 R에 곱하거나 디졸브 임계값에 소량 더해 가장자리를 거칠게 만든다.
  A  큰 덩어리 마스크.    저주파(파도 규모) 얼룩. 거품이 해안선 전체에 균일하게 깔리지 않고
                        어느 구간에만 뭉치게 하는 용도. R에 곱해 쓴다(텍스처에는 미리 안 곱해 놨다 -
                        셰이더가 세기를 조절할 수 있어야 하므로).

UV 규약
  V축 = 해안에 수직인 방향(물 -> 육지), **1타일 = 스와시 1주기**.
  파도 앞머리(가장 두꺼운 거품 선)는 V = FRONT_V 근처에 있고, 거기서 V가 커지는 쪽으로
  거품이 서서히 성겨진다. 앞머리 선은 노이즈로 휘어 놔서 직선으로 보이지 않는다.
  U축 = 해안선을 따라가는 방향(특별한 구조 없음, 그냥 반복해도 된다).

무이음(tileable)
  - 노이즈: surface_set.py와 같은 주기적 값 노이즈(래핑 보간).
  - 보로노이: 지터 격자 씨앗을 **모듈로 인덱싱**으로 감아(3x3 이웃 탐색) 토러스에서 계산한다.
  - 앞머리 프로파일: frac(V - FRONT_V)의 함수이고 p=0/p=1 양끝 값이 같도록 감쇠를 정규화했다.
    즉 텍스처 경계(V=0)에서 값이 튀지 않는다. 앞머리의 급한 단차는 경계가 아니라 텍스처
    **안쪽**(V=FRONT_V)에 있다 - 그게 파도 선이다.
  - 블러는 3x3 타일링 후 가운데를 잘라내는 랩어라운드 블러(PIL 블러는 가장자리를 클램프한다).

시드 고정
  아래 SEED_* 상수만이 유일한 난수원이다(np.random.default_rng). 두 번 실행하면
  바이트 단위로 같은 PNG가 나온다(검증: md5 2회 대조).
"""

import os

import numpy as np
from PIL import Image, ImageFilter

SIZE = 1024

# 파도 앞머리(가장 두꺼운 거품 선)의 V 위치. 텍스처 경계(V=0)에서 충분히 떨어뜨려
# 단차가 이음매로 오해받지 않게 한다.
FRONT_V = 0.30
# 앞머리 뒤로 남는 잔거품의 최소 밀도(0이면 뒤쪽이 완전히 빈 띠가 되어 부자연스럽다).
TAIL_FLOOR = 0.14

# 유일한 난수원. 바꾸지 않는 한 산출물은 바이트 단위로 재현된다.
SEED_WARP_A = 41001      # 보로노이 도메인 워프(저주파)
SEED_WARP_B = 41003      # 보로노이 도메인 워프(고주파, 톱니 가장자리)
SEED_CELL_BIG = 41011    # 굵은 거품 덩어리
SEED_CELL_MID = 41017    # 중간 거품
SEED_CELL_FINE = 41023   # 잔거품
SEED_FRONT_WARP = 41029  # 앞머리 선을 휘게 하는 노이즈
SEED_BREAKUP = 41039     # 마스크 문턱을 흔드는 고주파(가장자리 톱니)
SEED_BREAKUP2 = 41040    # 더 잔 톱니(픽셀 단위 뜯김)
SEED_STREAK = 41041      # 해안선과 나란한 물자국 결
SEED_HOLE = 41043        # 거품막을 뚫는 기포 구멍(레이스 구조)
SEED_DISSOLVE = 41047    # G 기본 노이즈
SEED_DETAIL = 41053      # B 고주파 노이즈
SEED_DETAIL_CELL = 41059 # B 미세 기포
SEED_BLOTCH = 41063      # A 저주파 얼룩

# G에서 "거품 두께"가 차지하는 비중. 0이면 완전 독립 노이즈(무작위로 뚫린다),
# 1이면 두께 그 자체(디졸브가 등고선처럼 보인다). 0.2~0.5 상관을 노린 값.
DISSOLVE_THICKNESS_MIX = 0.30


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


def periodic_value_noise_xy(size, cells_x, cells_y, seed, octaves=3, persistence=0.55):
    """
    축마다 주파수가 다른 무이음 값 노이즈 [0,1]. 위 함수의 비등방 버전.

    해안선과 나란한 물자국(가로로 길게 늘어난 결)을 만들 때 쓴다. surface_set.py는 이걸
    PIL resize로 눌러서 만들지만 resize는 가장자리를 클램프해 이음매가 생길 수 있다.
    격자 자체를 직사각형으로 잡으면 래핑 보간이 그대로 유지되므로 무이음이 보장된다.
    """
    rng = np.random.default_rng(seed)
    out = np.zeros((size, size), dtype=np.float64)
    amp = 1.0
    total = 0.0
    for o in range(octaves):
        nx = cells_x * (2 ** o)
        ny = cells_y * (2 ** o)
        grid = rng.random((ny, nx))
        ix = np.linspace(0, nx, size, endpoint=False)
        iy = np.linspace(0, ny, size, endpoint=False)
        x0 = np.floor(ix).astype(int) % nx
        x1 = (x0 + 1) % nx
        y0 = np.floor(iy).astype(int) % ny
        y1 = (y0 + 1) % ny
        fx = ix - np.floor(ix)
        fy = iy - np.floor(iy)
        fx = (fx * fx * (3 - 2 * fx))[np.newaxis, :]
        fy = (fy * fy * (3 - 2 * fy))[:, np.newaxis]
        a = grid[np.ix_(y0, x0)]
        b = grid[np.ix_(y0, x1)]
        c = grid[np.ix_(y1, x0)]
        d = grid[np.ix_(y1, x1)]
        out += (a * (1 - fx) * (1 - fy) + b * fx * (1 - fy)
                + c * (1 - fx) * fy + d * fx * fy) * amp
        total += amp
        amp *= persistence
    return out / total


def smoothstep(edge0, edge1, x):
    """numpy용 smoothstep. edge는 스칼라/배열 아무거나."""
    t = np.clip((x - edge0) / (edge1 - edge0), 0.0, 1.0)
    return t * t * (3.0 - 2.0 * t)


def base_uv():
    """픽셀 중심 UV [0,1). U=가로, V=세로."""
    lin = (np.arange(SIZE) + 0.5) / SIZE
    u = np.tile(lin[np.newaxis, :], (SIZE, 1))
    v = np.tile(lin[:, np.newaxis], (1, SIZE))
    return u, v


def voronoi_blobs(cells, seed, u, v, rad_min, rad_max, softness):
    """
    지터 격자 보로노이 "기포 덩어리" 필드 [0,1]. 1 = 어떤 기포 안쪽, 0 = 기포 밖.

    카우스틱(F2-F1 그물망)과 달리 여기서 필요한 건 **면**이라 F1 거리로 원반을 채운다.
    씨앗마다 반지름이 달라 크고 작은 기포가 섞이고, 반지름이 셀 간격보다 크면 이웃과 융합해
    "여러 개가 들러붙은 거품 덩어리"가 된다 - 파도 앞머리의 굵은 거품이 딱 그 모양이다.

    씨앗 격자를 모듈로로 감고 3x3 이웃만 보므로 토러스에서 정확하고(무이음) 씨앗 수와
    무관하게 9번의 배열 연산으로 끝난다.
    """
    rng = np.random.default_rng(seed)
    off = rng.random((cells, cells, 2))                       # 셀 내부 씨앗 위치
    rad = rng.uniform(rad_min, rad_max, (cells, cells))       # 셀 단위 반지름
    fu = u * cells
    fv = v * cells
    gu = np.floor(fu).astype(np.int64)
    gv = np.floor(fv).astype(np.int64)
    lu = fu - gu
    lv = fv - gv
    out = np.zeros((SIZE, SIZE), dtype=np.float64)
    for dv in (-1, 0, 1):
        for du in (-1, 0, 1):
            su = off[(gv + dv) % cells, (gu + du) % cells, 0] + du
            sv = off[(gv + dv) % cells, (gu + du) % cells, 1] + dv
            r = rad[(gv + dv) % cells, (gu + du) % cells]
            d = np.sqrt((su - lu) ** 2 + (sv - lv) ** 2) / r
            out = np.maximum(out, 1.0 - smoothstep(1.0 - softness, 1.0, d))
    return out


def wrap_blur(field, radius):
    """랩어라운드 가우시안 블러. PIL은 가장자리를 클램프하므로 3x3 타일 후 가운데를 잘라낸다."""
    tiled = np.tile(np.clip(field, 0.0, 1.0), (3, 3))
    img = Image.fromarray((tiled * 255.0 + 0.5).astype(np.uint8), "L")
    img = img.filter(ImageFilter.GaussianBlur(radius))
    return np.asarray(img).astype(np.float64)[SIZE:2 * SIZE, SIZE:2 * SIZE] / 255.0


def normalize(field):
    """[0,1] 풀스케일로 펴기(min-max)."""
    lo = float(field.min())
    hi = float(field.max())
    if hi - lo < 1e-9:
        return np.zeros_like(field)
    return (field - lo) / (hi - lo)


def uniform_rank(field, seed):
    """
    히스토그램 균등화(랭크 변환) -> 정확히 균등 분포 [0,1].

    디졸브 임계값을 0->1로 선형으로 올릴 때 거품이 **일정한 속도로** 사라지려면
    노이즈의 CDF가 직선이어야 한다. 다옥타브 값 노이즈는 합산 때문에 종 모양이라
    가운데 임계값에서만 확 사라진다. 랭크 변환은 순서를 보존하는 단조 사상이라
    공간적 연속성(=무이음)과 R과의 순위상관을 그대로 두고 분포만 편다.
    동점은 시드 고정 난수로 깨서 재현성을 유지한다.
    """
    rng = np.random.default_rng(seed)
    flat = field.ravel() + rng.random(field.size) * 1e-9
    order = np.argsort(flat, kind="stable")
    ranks = np.empty(flat.size, dtype=np.float64)
    ranks[order] = np.arange(flat.size, dtype=np.float64)
    return (ranks / (flat.size - 1.0)).reshape(field.shape)


def front_profile(v):
    """
    스와시 앞머리 프로파일 [TAIL_FLOOR, 1]. p = frac(V - FRONT_V) 기준.

    p=0에서 1(앞머리, 가장 두꺼운 거품) -> p가 커질수록 감쇠(뒤로 갈수록 성김).
    감쇠항을 p=1에서 정확히 0이 되도록 정규화해서 p=1(=p=0 직전)과 p=0의 값이 둘 다
    TAIL_FLOOR가 되게 했다. 덕분에 텍스처 V 경계는 완전히 연속이고, 급한 단차는
    p=0(=V=FRONT_V) 안쪽에만 생긴다 - 그게 파도 선이다.
    """
    # 앞머리 선을 휘게: 저주파로 크게 굽히고 고주파로 잘게 뜯는다.
    warp = (periodic_value_noise(SIZE, 3, SEED_FRONT_WARP, octaves=4) - 0.5) * 0.20
    warp += (periodic_value_noise(SIZE, 11, SEED_FRONT_WARP + 977, octaves=3) - 0.5) * 0.07
    p = np.mod(v + warp - FRONT_V, 1.0)

    k = 4.2
    decay = (np.exp(-k * p) - np.exp(-k)) / (1.0 - np.exp(-k))   # p=0 -> 1, p=1 -> 0
    rise = smoothstep(0.0, 0.035, p)                             # 앞머리의 급한 상승면
    return TAIL_FLOOR + (1.0 - TAIL_FLOOR) * rise * decay


def warped_uv(u, v):
    """보로노이용 도메인 워프 좌표. 저주파로 크게 굽히고 고주파로 잘게 뜯는다(무이음 유지)."""
    wu = (periodic_value_noise(SIZE, 4, SEED_WARP_A, octaves=4) - 0.5) * 2.0
    wv = (periodic_value_noise(SIZE, 4, SEED_WARP_A + 977, octaves=4) - 0.5) * 2.0
    hu = (periodic_value_noise(SIZE, 16, SEED_WARP_B, octaves=3) - 0.5) * 2.0
    hv = (periodic_value_noise(SIZE, 16, SEED_WARP_B + 977, octaves=3) - 0.5) * 2.0
    return (np.mod(u + wu * 0.055 + hu * 0.018, 1.0),
            np.mod(v + wv * 0.055 + hv * 0.018, 1.0))


def foam_thickness(wu_, wv_):
    """거품 "두께" 필드 [0,1] - 마스크(R)와 디졸브(G) 둘 다 이걸 재료로 쓴다."""
    # 3층: 굵은 덩어리 / 중간 / 잔거품. 반지름이 셀 간격(1.0)에 가까울수록 이웃과 융합한다.
    big = voronoi_blobs(9, SEED_CELL_BIG, wu_, wv_, 0.42, 0.86, 0.55)
    mid = voronoi_blobs(19, SEED_CELL_MID, wu_, wv_, 0.34, 0.78, 0.60)
    fine = voronoi_blobs(38, SEED_CELL_FINE, wu_, wv_, 0.26, 0.66, 0.70)
    return normalize(0.52 * big + 0.31 * mid + 0.17 * fine)


def make_shore_foam(out_dir):
    u, v = base_uv()

    wu_, wv_ = warped_uv(u, v)
    thick = foam_thickness(wu_, wv_)
    profile = front_profile(v)

    # --- R: 거품 마스크 -------------------------------------------------------
    # (1) 문턱을 앞머리에서는 낮게(덩어리가 거의 다 통과 -> 굵고 서로 붙은 거품),
    #     뒤로 갈수록 높게(꼭대기만 통과 -> 성긴 알갱이) 움직인다.
    # (2) 문턱 자체를 주파수가 다른 노이즈 3장으로 흔든다. 이게 없으면 윤곽이 매끈한 원이라
    #     "우유 방울"처럼 보인다 - 거품 가장자리는 셀이 터진 자리라 톱니여야 한다.
    #     streak는 해안선과 나란히 늘어난 결이라 물이 빠진 자국 방향을 만든다.
    breakup = periodic_value_noise(SIZE, 26, SEED_BREAKUP, octaves=4) - 0.5
    breakup2 = periodic_value_noise(SIZE, 90, SEED_BREAKUP2, octaves=2) - 0.5
    streak = periodic_value_noise_xy(SIZE, 4, 26, SEED_STREAK, octaves=3) - 0.5
    thr = (0.92 - 0.70 * profile) + breakup * 0.20 + breakup2 * 0.09 + streak * 0.13
    # 문턱 폭을 넓게 잡아 이진 실루엣이 아니라 밀도 그라데이션이 남게 한다.
    mask = smoothstep(thr - 0.13, thr + 0.13, thick)

    # (3) 기포 구멍: 거품막은 꽉 찬 흰 덩어리가 아니라 기포 벽이 얽힌 레이스다.
    #     크기가 다른 원반 2층을 파내야 "먼지 얼룩"이 아니라 기포 조직으로 읽힌다.
    holes = np.maximum(voronoi_blobs(30, SEED_HOLE, wu_, wv_, 0.16, 0.46, 0.80),
                       0.75 * voronoi_blobs(64, SEED_HOLE + 977, wu_, wv_, 0.18, 0.50, 0.85))
    mask *= 1.0 - 0.55 * holes
    # 내부 밀도 얼룩: 덩어리 안쪽이 순백으로 평평하면 근접 시 종이처럼 보인다.
    # 기포 벽 두께 차이를 흉내내 ±0.18쯤 흔든다(마스크가 0인 곳은 건드리지 않는다).
    inner = periodic_value_noise(SIZE, 34, SEED_HOLE + 1213, octaves=3, persistence=0.6)
    mask *= 0.82 + 0.36 * inner

    # (4) 앞머리에 얇은 물막(덩어리 사이를 메우는 반투명 젖은 면). 없으면 앞머리가
    #     점묘처럼 뚝뚝 끊긴다 - 실제로는 굵은 거품 사이가 물로 이어져 있다.
    film = 0.34 * (profile ** 1.5) * smoothstep(0.18, 0.62, thick)
    mask = np.clip(mask + film, 0.0, 1.0)
    mask = wrap_blur(mask, 0.7)

    # --- G: 디졸브 노이즈 -----------------------------------------------------
    # 독립 다옥타브 노이즈 + 거품 두께를 섞어 약한 양의 상관을 만든 뒤 히스토그램을 편다.
    dissolve = periodic_value_noise(SIZE, 7, SEED_DISSOLVE, octaves=6, persistence=0.58)
    thick_soft = wrap_blur(0.55 * thick + 0.45 * profile, 3.0)
    g_raw = (1.0 - DISSOLVE_THICKNESS_MIX) * dissolve + DISSOLVE_THICKNESS_MIX * thick_soft
    g = uniform_rank(g_raw, SEED_DISSOLVE + 1)

    # --- B: 미세 디테일 -------------------------------------------------------
    micro = voronoi_blobs(96, SEED_DETAIL_CELL, u, v, 0.20, 0.52, 0.85)
    grain = periodic_value_noise(SIZE, 64, SEED_DETAIL, octaves=3, persistence=0.62)
    b = normalize(0.55 * micro + 0.45 * grain)

    # --- A: 큰 덩어리 얼룩 ----------------------------------------------------
    blotch = periodic_value_noise(SIZE, 3, SEED_BLOTCH, octaves=5, persistence=0.45)
    a = normalize(blotch)
    a = smoothstep(0.10, 0.90, a)   # 대비를 세워 "뭉친 구간 / 빈 구간"이 뚜렷하게

    rgba = np.stack([mask, g, b, a], axis=-1)
    img = Image.fromarray((np.clip(rgba, 0.0, 1.0) * 255.0 + 0.5).astype(np.uint8), "RGBA")
    path = os.path.join(out_dir, "shore_foam.png")
    img.save(path, optimize=True)
    print("저장:", os.path.basename(path), f"({SIZE}x{SIZE} RGBA)")


def main():
    out_dir = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)),
                                           "..", "..", "Assets", "_Project", "Resources", "Textures"))
    os.makedirs(out_dir, exist_ok=True)
    make_shore_foam(out_dir)


if __name__ == "__main__":
    main()
