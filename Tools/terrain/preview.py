#!/usr/bin/env python3
"""
IslandMeshGenerator.GenerateIslandMesh 의 **높이 함수만** 파이썬으로 옮겨, 섬 여러 개를
한 장의 PNG로 렌더한다. Unity를 켜지 않고 "섬마다 지형이 실제로 갈라졌는가"를 눈으로 판정하기 위한 도구다.

    python3 Tools/terrain/preview.py

출력: Tools/terrain/_preview/island_seed_preview.png   (.gitignore 처리됨)

────────────────────────────────────────────────────────────────────────────
★ 노이즈 동등성에 대한 정직한 고지 ★
Unity의 Mathf.PerlinNoise 는 구현이 공개돼 있지 않다(내부 순열표/그래디언트 표가 문서화되지 않음).
이 스크립트는 Ken Perlin 의 improved noise(2002) 레퍼런스 구현(3D, z=0 평면)을 쓰고
결과를 [-1,1] → [0,1] 로 옮긴다. 즉:

  · **같은 종류·같은 통계**의 노이즈다 — 격자 주기 256, 5차 페이드(6t^5-15t^4+10t^3),
    격자점에서 정확히 0.5, 진폭 분포가 사실상 동일하다.
  · 그러나 **Unity와 픽셀 단위로 같은 값이 나오지는 않는다.** 그래서 이 그림은
    "게임에 나올 바로 그 섬의 사진"이 아니라 **"섬들이 서로 충분히 다른가"의 판정용**이다.
    절대 형태를 최종 확인하려면 Unity에서 봐야 한다.

높이 함수 자체(코사인 낙차 · 1옥타브 펄린 · 가장자리 감쇠 (1-t) · 해안 잠수 · 해안 펄린)와
오프셋 유도 해시는 C# 소스와 **한 줄씩 대응**하도록 옮겼다. 해시는 정수 연산이라 C#과 완전히 동일하다.
────────────────────────────────────────────────────────────────────────────
"""

import math
import os

import glob

import matplotlib

matplotlib.use("Agg")
import matplotlib.font_manager as fm
import matplotlib.pyplot as plt
import numpy as np


def _setup_font():
    """
    한글 라벨을 쓰려면 CJK 폰트가 필요하다. 없으면 matplotlib이 글자를 두부(□)로 그리고
    경고만 뱉으므로, 폰트를 못 찾으면 **라벨을 영문으로 내린다**(그림이 못 읽히는 것보다 낫다).
    반환값 True = 한글 사용 가능.
    """
    candidates = []
    for pattern in (
        "/usr/share/fonts/**/NotoSansCJK*.ttc",
        "/usr/share/fonts/**/NanumGothic*.ttf",
        "/usr/share/fonts/**/*CJK*.otf",
    ):
        candidates.extend(glob.glob(pattern, recursive=True))

    for path in candidates:
        try:
            fm.fontManager.addfont(path)
            name = fm.FontProperties(fname=path).get_name()
        except Exception:
            continue
        plt.rcParams["font.family"] = name
        plt.rcParams["axes.unicode_minus"] = False
        return True
    return False


HANGUL_OK = _setup_font()

# ── IslandMeshGenerator.cs 의 상수 (값이 바뀌면 여기도 함께 고쳐야 한다) ────────────────
SHORE_BAND_FRACTION = 0.12
SHORE_SUBMERGE_DEPTH = 1.8
SHORE_NOISE_SCALE = 0.035
SHORE_NOISE_AMPLITUDE = 1.4
NOISE_OFFSET_SPAN = 256.0
LEGACY_NOISE_SEED = -(2 ** 31)  # int.MinValue

# WorldMapManager: terrainMaxHeight 는 **씬에 8로 직렬화**돼 있다(코드 기본값 2.5는 무시된다).
TERRAIN_MAX_HEIGHT = 8.0
NOISE_SCALE = 0.05
NOISE_AMPLITUDE = 2.0

# IslandSizeMetrics 의 지형 반지름.
RADIUS_MEDIUM = 90.0
RADIUS_LARGE = 140.0


# ── Ken Perlin improved noise (레퍼런스 구현) ────────────────────────────────────────
_PERM_BASE = [
    151, 160, 137, 91, 90, 15, 131, 13, 201, 95, 96, 53, 194, 233, 7, 225, 140, 36, 103,
    30, 69, 142, 8, 99, 37, 240, 21, 10, 23, 190, 6, 148, 247, 120, 234, 75, 0, 26, 197,
    62, 94, 252, 219, 203, 117, 35, 11, 32, 57, 177, 33, 88, 237, 149, 56, 87, 174, 20,
    125, 136, 171, 168, 68, 175, 74, 165, 71, 134, 139, 48, 27, 166, 77, 146, 158, 231,
    83, 111, 229, 122, 60, 211, 133, 230, 220, 105, 92, 41, 55, 46, 245, 40, 244, 102,
    143, 54, 65, 25, 63, 161, 1, 216, 80, 73, 209, 76, 132, 187, 208, 89, 18, 169, 200,
    196, 135, 130, 116, 188, 159, 86, 164, 100, 109, 198, 173, 186, 3, 64, 52, 217, 226,
    250, 124, 123, 5, 202, 38, 147, 118, 126, 255, 82, 85, 212, 207, 206, 59, 227, 47,
    16, 58, 17, 182, 189, 28, 42, 223, 183, 170, 213, 119, 248, 152, 2, 44, 154, 163,
    70, 221, 153, 101, 155, 167, 43, 172, 9, 129, 22, 39, 253, 19, 98, 108, 110, 79,
    113, 224, 232, 178, 185, 112, 104, 218, 246, 97, 228, 251, 34, 242, 193, 238, 210,
    144, 12, 191, 179, 162, 241, 81, 51, 145, 235, 249, 14, 239, 107, 49, 192, 214, 31,
    181, 199, 106, 157, 184, 84, 204, 176, 115, 121, 50, 45, 127, 4, 150, 254, 138, 236,
    205, 93, 222, 114, 67, 29, 24, 72, 243, 141, 128, 195, 78, 66, 215, 61, 156, 180,
]
_PERM = np.array(_PERM_BASE * 2, dtype=np.int64)


def _fade(t):
    return t * t * t * (t * (t * 6.0 - 15.0) + 10.0)


def _grad2(h, x, y):
    """improved noise 의 grad() 를 z=0 평면에 대해 편 것."""
    h = h & 15
    u = np.where(h < 8, x, y)
    v = np.where(h < 4, y, np.where((h == 12) | (h == 14), x, 0.0))
    u = np.where((h & 1) == 0, u, -u)
    v = np.where((h & 2) == 0, v, -v)
    return u + v


def perlin01(x, y):
    """Unity Mathf.PerlinNoise 대응(근사): 격자 주기 256, 반환값 대략 [0,1], 격자점에서 0.5."""
    x = np.asarray(x, dtype=np.float64)
    y = np.asarray(y, dtype=np.float64)

    xi = np.floor(x).astype(np.int64) & 255
    yi = np.floor(y).astype(np.int64) & 255
    xf = x - np.floor(x)
    yf = y - np.floor(y)
    u = _fade(xf)
    v = _fade(yf)

    a = _PERM[xi] + yi
    b = _PERM[xi + 1] + yi
    n = (
        (1 - v) * ((1 - u) * _grad2(_PERM[a], xf, yf) + u * _grad2(_PERM[b], xf - 1, yf))
        + v * ((1 - u) * _grad2(_PERM[a + 1], xf, yf - 1) + u * _grad2(_PERM[b + 1], xf - 1, yf - 1))
    )
    return n * 0.5 + 0.5


# ── 오프셋 유도 해시 (C# ComputeNoiseSeed / NoiseOffsetFromSeed 와 정수 단위로 동일) ──
_U32 = 0xFFFFFFFF


def _to_int32(u):
    u &= _U32
    return u - 0x100000000 if u >= 0x80000000 else u


def compute_noise_seed(world_seed, island_id):
    h = ((world_seed * 73856093) & _U32) ^ ((island_id * 19349663) & _U32) ^ 0x9E3779B9
    h &= _U32
    h ^= h >> 16
    h = (h * 0x7FEB352D) & _U32
    h ^= h >> 15
    h = (h * 0x846CA68B) & _U32
    h ^= h >> 16
    seed = _to_int32(h)
    return 0 if seed == LEGACY_NOISE_SEED else seed


def noise_offset_from_seed(noise_seed, axis_salt):
    h = (noise_seed & _U32) ^ axis_salt
    h &= _U32
    h ^= h >> 16
    h = (h * 0x7FEB352D) & _U32
    h ^= h >> 15
    h = (h * 0x846CA68B) & _U32
    h ^= h >> 16
    return (h & 0xFFFFFF) / float(0x1000000) * NOISE_OFFSET_SPAN


def offsets_for_seed(noise_seed):
    """(noiseOffsetX, noiseOffsetZ, shoreOffsetX, shoreOffsetZ) — C# 분기와 같은 순서/같은 salt."""
    if noise_seed == LEGACY_NOISE_SEED:
        return 1000.0, 1000.0, 517.0, 517.0
    return (
        1000.0 + noise_offset_from_seed(noise_seed, 0x51ED270B),
        1000.0 + noise_offset_from_seed(noise_seed, 0x1B873593),
        517.0 + noise_offset_from_seed(noise_seed, 0x27D4EB2F),
        517.0 + noise_offset_from_seed(noise_seed, 0x165667B1),
    )


# ── GenerateIslandMesh 의 높이식을 그대로 옮긴 것 ────────────────────────────────────
def island_height(x, z, radius, max_height, noise_seed,
                  noise_scale=NOISE_SCALE, noise_amplitude=NOISE_AMPLITUDE):
    """
    C# 은 링/세그먼트 격자의 정점에서만 이 식을 계산하지만, 여기서는 같은 식을
    직교 격자에서 조밀하게 평가한다(등고선을 매끄럽게 그리기 위해서다).
    t 의 정의(중심 0 → 가장자리 1)와 각 항은 C# 과 한 줄씩 대응한다.
    """
    ox, oz, sox, soz = offsets_for_seed(noise_seed)

    r = np.hypot(x, z)
    t = np.clip(r / radius, 0.0, 1.0)

    base_height = max_height * np.cos(t * math.pi * 0.5)
    noise = (perlin01(x * noise_scale + ox, z * noise_scale + oz) - 0.5) * noise_amplitude * (1.0 - t)

    shore_t = np.clip((t - (1.0 - SHORE_BAND_FRACTION)) / SHORE_BAND_FRACTION, 0.0, 1.0)
    submerge = SHORE_SUBMERGE_DEPTH * shore_t * shore_t
    shore_noise = (
        (perlin01(x * SHORE_NOISE_SCALE + sox, z * SHORE_NOISE_SCALE + soz) - 0.5)
        * SHORE_NOISE_AMPLITUDE * shore_t
    )

    inner = np.maximum(0.0, base_height + noise)          # shoreT <= 0 분기
    outer = base_height + noise - submerge + shore_noise  # shoreT > 0 분기
    y = np.where(shore_t <= 0.0, inner, outer)

    # 메시 바깥(원 밖)은 지형이 아니다 — 렌더에서만 가린다.
    return np.where(r <= radius, y, np.nan)


def sample_field(radius, noise_seed, resolution=340):
    """한 섬의 높이장을 정사각 격자에서 평가한다(섬 바깥은 NaN)."""
    span = radius * 1.02
    axis = np.linspace(-span, span, resolution)
    x, z = np.meshgrid(axis, axis)
    y = island_height(x, z, radius, TERRAIN_MAX_HEIGHT, noise_seed)
    return axis, x, z, y


def dome_of(x, z, radius):
    t = np.clip(np.hypot(x, z) / radius, 0.0, 1.0)
    return TERRAIN_MAX_HEIGHT * np.cos(t * math.pi * 0.5)


def render_residual_panel(ax, radius, noise_seed):
    """
    코사인 돔(= 시드와 무관하게 모든 섬이 공유하는 성분)을 뺀 **굴곡만** 그린다.
    위 고도 그림에서는 8m짜리 돔이 색을 다 먹어서 ±1m 굴곡 차이가 안 보인다 -
    "섬마다 실제로 갈렸는가"는 이 줄을 봐야 판정할 수 있다.
    """
    axis, x, z, y = sample_field(radius, noise_seed)
    span = axis[-1]
    resid = np.where(np.isfinite(y), y - dome_of(x, z, radius), np.nan)
    # 해안 띠는 시드와 무관한 submerge(-1.8m)가 지배하므로 색 범위를 **실제 굴곡 진폭**에 맞춰 자른다.
    # 노이즈 항은 중심에서 ±1m이고 (1-t)로 감쇠하므로 내륙 표준편차가 0.25m 안팎이다 - ±0.6m면 꽉 찬다.
    ax.imshow(resid, extent=[-span, span, -span, span], origin="lower",
              cmap="RdBu_r", vmin=-0.6, vmax=0.6, interpolation="bilinear")
    ax.contour(x, z, resid, levels=[-0.25, 0.0, 0.25], colors="black", linewidths=0.4, alpha=0.55)
    ax.set_xticks([])
    ax.set_yticks([])


def render_panel(ax, radius, noise_seed, title):
    axis, x, z, y = sample_field(radius, noise_seed)
    span = axis[-1]
    finite = y[np.isfinite(y)]
    peak = float(finite.max()) if finite.size else 0.0

    extent = [-span, span, -span, span]

    # (1) 고도 색. 회전 대칭인 코사인 돔이 색의 대부분을 차지하므로 여기서는 "어디가 높은가"만 읽힌다.
    ax.imshow(y, extent=extent, origin="lower", cmap="terrain",
              vmin=-SHORE_SUBMERGE_DEPTH, vmax=TERRAIN_MAX_HEIGHT, interpolation="bilinear")

    # (2) 음영 기복을 낮은 알파로 덧씌워 굴곡(=시드가 바꾸는 부분)을 도드라지게 한다.
    #     수직 과장은 3배로 낮춘다 - 크게 잡으면 돔 경사에 눌려 전 섬이 같은 그라데이션으로 보인다.
    filled = np.nan_to_num(y, nan=0.0)
    step = axis[1] - axis[0]
    gz, gx = np.gradient(filled, step)
    ve = 3.0
    nx, nz = -gx * ve, -gz * ve
    nl = np.sqrt(nx * nx + nz * nz + 1.0)
    az, alt = math.radians(315.0), math.radians(45.0)
    lx, lz, ly = math.cos(alt) * math.cos(az), math.cos(alt) * math.sin(az), math.sin(alt)
    shade = np.clip((nx * lx + nz * lz + ly) / nl, 0.0, 1.0)
    ax.imshow(np.where(np.isfinite(y), shade, np.nan), extent=extent, origin="lower",
              cmap="gray", vmin=0.35, vmax=1.0, alpha=0.45, interpolation="bilinear")

    # (3) 등고선. 0.5m 간격이라 굴곡이 만드는 닫힌 고리(작은 봉우리/안부)가 그대로 보인다.
    ax.contour(x, z, y, levels=np.arange(0.5, TERRAIN_MAX_HEIGHT, 0.5),
               colors="black", linewidths=0.3, alpha=0.5)
    # (4) 물가(y=0 등고선). 원이 아니라는 점이 해안 노이즈가 갈렸다는 증거다.
    ax.contour(x, z, y, levels=[0.0], colors="#0B2E6B", linewidths=1.8)

    ax.set_title(title, fontsize=8.5, pad=3)
    ax.text(
        0.02, 0.02,
        f"R={radius:.0f}m   peak={peak:.2f}m",
        transform=ax.transAxes, fontsize=7, color="white",
        bbox=dict(boxstyle="round,pad=0.25", fc="black", alpha=0.6, lw=0),
    )
    ax.set_xticks([])
    ax.set_yticks([])
    return peak


def distinctness_report(panels):
    """
    "눈으로 보니 달라 보인다"는 주관적이라, 굴곡 성분만 남겨 수치로도 확인한다.
    회전 대칭인 코사인 돔은 시드와 무관하게 모든 섬이 공유하므로 빼고(= 노이즈 성분만 비교),
    같은 반지름끼리 상관계수와 RMS 차이를 낸다. 상관계수가 0 근처면 완전히 다른 지형이다.
    """
    groups = {}
    for radius, seed, label in panels:
        axis, x, z, y = sample_field(radius, seed)
        r = np.hypot(x, z)
        t = np.clip(r / radius, 0.0, 1.0)
        dome = TERRAIN_MAX_HEIGHT * np.cos(t * math.pi * 0.5)

        # 내륙(shoreT = 0, 즉 t <= 1 - ShoreBandFraction)만 본다. 해안 띠는 시드와 무관한
        # submerge 항(-1.8m까지)이 노이즈(±1m)보다 커서, 같이 넣으면 "공유 성분" 때문에
        # 상관계수가 인위적으로 1에 붙는다 - 그 수치는 아무것도 말해 주지 않는다.
        inland = np.isfinite(y) & (t <= 1.0 - SHORE_BAND_FRACTION)
        residual = np.where(inland, y - dome, np.nan)

        # 물가 등고선(y=0)의 반경을 각도별로 재서 따로 비교한다 - 해안 노이즈가 갈렸는지의 지표다.
        groups.setdefault(radius, []).append((label, residual, waterline_radii(radius, seed)))

    lines = []
    for radius, items in groups.items():
        for i in range(len(items)):
            for j in range(i + 1, len(items)):
                (la, a, wa), (lb, b, wb) = items[i], items[j]
                mask = np.isfinite(a) & np.isfinite(b)
                av, bv = a[mask], b[mask]
                rms = float(np.sqrt(np.mean((av - bv) ** 2)))
                corr = float(np.corrcoef(av, bv)[0, 1])
                wmask = np.isfinite(wa) & np.isfinite(wb)
                wcorr = float(np.corrcoef(wa[wmask], wb[wmask])[0, 1]) if wmask.sum() > 8 else float("nan")
                lines.append(
                    f"  R={radius:>5.0f}m  inland_corr={corr:+.3f}  rms={rms:.3f}m  "
                    f"shore_corr={wcorr:+.3f}   {la}  vs  {lb}"
                )
    return lines


def waterline_radii(radius, noise_seed, angle_steps=360, radial_steps=400):
    """
    각 방위각에서 지형이 처음 해수면(y=0) 아래로 내려가는 반경. 해안선 모양을 1차원으로 편 것이고,
    이 배열이 섬마다 갈리면 "물가 모양이 전 섬 동일"이라는 문제가 실제로 해소된 것이다.
    격자 이미지를 읽지 않고 높이식을 극좌표에서 **직접** 평가한다(격자 스냅 때문에 생기는
    가짜 상관을 피하기 위해서다).
    """
    ang = np.linspace(0.0, 2.0 * math.pi, angle_steps, endpoint=False)
    rs = np.linspace(radius * 0.80, radius, radial_steps)
    xs = np.outer(np.cos(ang), rs)
    zs = np.outer(np.sin(ang), rs)
    y = island_height(xs, zs, radius, TERRAIN_MAX_HEIGHT, noise_seed)

    below = y <= 0.0
    has = below.any(axis=1)
    idx = np.argmax(below, axis=1)
    return np.where(has, rs[idx], np.nan)


def main():
    here = os.path.dirname(os.path.abspath(__file__))
    out_dir = os.path.join(here, "_preview")
    os.makedirs(out_dir, exist_ok=True)

    world_seed = 12345  # WorldMapManager.worldSeed 는 씬 직렬화 값이다. 그림은 상대 비교용이라 임의값으로 충분하다.

    if HANGUL_OK:
        control_label = "[대조군] seed 없음 — 예전엔 중형 섬이 전부 이 한 모양이었다"
        medium_label, large_label = "중형", "대형"
        suptitle = (
            "IslandMeshGenerator — 섬별 노이즈 시드 도입 전/후 (음영 기복 + 1m 등고선, 굵은 파란선 = 물가 y=0)\n"
            "노이즈는 Unity Mathf.PerlinNoise 와 같은 계열의 근사이며 값이 비트 단위로 같지는 않다 — 상대 비교용"
        )
    else:
        control_label = "[CONTROL] no seed - every medium island used to look like this"
        medium_label, large_label = "Medium", "Large"
        suptitle = (
            "IslandMeshGenerator - per-island noise seed, before/after "
            "(hillshade + 1m contours, thick blue = waterline y=0)\n"
            "Noise is a same-family approximation of Unity Mathf.PerlinNoise, not bit-identical - for relative comparison only"
        )

    panels = []
    # 대조군: 시드를 안 넘겼을 때(= 예전 코드). 반지름만 같으면 모든 섬이 이 한 장이었다.
    panels.append((RADIUS_MEDIUM, LEGACY_NOISE_SEED, control_label))
    for island_id in (0, 1, 2):
        panels.append((RADIUS_MEDIUM, compute_noise_seed(world_seed, island_id), f"{medium_label} islandId={island_id}"))
    for island_id in (3, 4, 5):
        panels.append((RADIUS_LARGE, compute_noise_seed(world_seed, island_id), f"{large_label} islandId={island_id}"))

    if HANGUL_OK:
        row_labels = ("실제 지형\n(고도 + 등고선)", "돔을 뺀 굴곡만\n(시드가 바꾸는 부분)")
    else:
        row_labels = ("Elevation\n(+ contours)", "Residual only\n(what the seed changes)")

    fig, axes = plt.subplots(2, len(panels), figsize=(3.0 * len(panels), 7.4), layout="constrained")
    for col, (radius, seed, title) in enumerate(panels):
        render_panel(axes[0, col], radius, seed, title)
        render_residual_panel(axes[1, col], radius, seed)
    for row in (0, 1):
        axes[row, 0].set_ylabel(row_labels[row], fontsize=8)

    fig.suptitle(suptitle, fontsize=10)

    out_path = os.path.join(out_dir, "island_seed_preview.png")
    fig.savefig(out_path, dpi=110)
    print(out_path)

    print("\n[distinctness] dome 성분을 뺀 굴곡만 비교 (corr 0 근처 = 완전히 다른 지형)")
    for line in distinctness_report(panels):
        print(line)


if __name__ == "__main__":
    main()
