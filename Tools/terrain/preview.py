#!/usr/bin/env python3
"""
IslandMeshGenerator 의 **높이 함수만** 파이썬으로 옮겨, 프로파일 8종을 한 장의 PNG(후보 시트)로 렌더한다.
Unity 를 켜지 않고 "섬마다 형태가 실제로 갈라졌는가 / 자원이 물에 빠지지 않는가 / 걸어다닐 수 있는가"를
눈과 숫자로 판정하기 위한 도구다.

    python3 Tools/terrain/preview.py

출력: Tools/terrain/_preview/island_profile_sheet.png   (.gitignore 처리됨)

────────────────────────────────────────────────────────────────────────────
★ 노이즈 동등성에 대한 정직한 고지 ★
Unity 의 Mathf.PerlinNoise 는 구현이 공개돼 있지 않다(내부 순열표/그래디언트 표가 문서화되지 않음).
이 스크립트는 Ken Perlin 의 improved noise(2002) 레퍼런스 구현(3D, z=0 평면)을 쓰고
결과를 [-1,1] → [0,1] 로 옮긴다. 즉:

  · **같은 종류·같은 통계**의 노이즈다 — 격자 주기 256, 5차 페이드(6t^5-15t^4+10t^3),
    격자점에서 정확히 0.5, 진폭 분포가 사실상 동일하다.
  · 그러나 **Unity 와 픽셀 단위로 같은 값이 나오지는 않는다.** 그래서 이 그림은
    "게임에 나올 바로 그 섬의 사진"이 아니라 **"프로파일이 서로 충분히 다른가"의 판정용**이다.
    절대 형태를 최종 확인하려면 Unity 에서 봐야 한다.

  · 아래 표(육지 비율·최대 경사·평탄 구역)에서 **노이즈에 의존하는 부분은 오차가 있다.**
    다만 그 세 지표를 지배하는 것은 조각(마스크·돔·능선·수로·석호)이고 노이즈 진폭은 ±1m 급이라,
    판정(70% 육지 / 45도 / 평탄 구역 유무)이 뒤집히지 않을 여유를 두고 튜닝했다.

높이 함수(마스크·이방성·돔·능선·수로·석호 링·메사·다중 옥타브 노이즈·해안 잠수)와 프로파일 파라미터
테이블, 해시는 C# 소스와 **한 줄씩 대응**하도록 옮겼다. 해시는 정수 연산이라 C# 과 완전히 동일하다.

★ 이식 검증(실측) ★ 펄린만 양쪽에서 상수 0.5로 중화하고(= 노이즈 항 0) C# SculptHeight 와 이 파일의
sculpt_height 를 프로파일 8종 × 441점에서 비교한 결과, 최대 차이가 **0.000011 m** 였다(float32 반올림
수준). 즉 노이즈를 뺀 조각(마스크·만·돔·능선·수로·석호·메사·해안 잠수)은 두 구현이 같은 함수다 —
이 시트에서 읽은 형태·육지 비율·경사는 Unity 에서도 그대로 나온다. 다른 것은 펄린 굴곡(±1m)뿐이다.
프로파일 배정(SelectShapeProfile)도 worldSeed=12345 에서 양쪽이 0,4,5,3,6,7,1,2,6 으로 일치했다.
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
    한글 라벨을 쓰려면 CJK 폰트가 필요하다. 없으면 matplotlib 이 글자를 두부(□)로 그리고
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
SHELF_DROP_PER_Q = 3.2          # q>1 (프로파일 해안 바깥) 구간의 추가 하강(m per q)
SHELF_DROP_MAX = 9.0
NOISE_OFFSET_SPAN = 256.0
LEGACY_NOISE_SEED = -(2 ** 31)  # int.MinValue

# WorldMapManager: terrainMaxHeight 는 **씬에 8로 직렬화**돼 있다(코드 기본값 2.5는 무시된다).
TERRAIN_MAX_HEIGHT = 8.0
NOISE_SCALE = 0.05
NOISE_AMPLITUDE = 2.0

# IslandSizeMetrics 의 지형 반지름.
RADIUS_SMALL = 50.0
RADIUS_MEDIUM = 90.0
RADIUS_LARGE = 140.0

# IslandResourceSpawner 의 산포 반경(지형 반지름의 80%). 이 원 안이 자원/위험요소가 뿌려지는 구역이다.
SCATTER_FRACTION = 0.80

# 건축 평탄 구역 판정(디렉터 지시): 반경 12m 안에서 높이 편차 1.5m 이하.
FLAT_PROBE_RADIUS = 12.0
FLAT_MAX_RELIEF = 1.5


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


# ── 해시 (C# ComputeNoiseSeed / NoiseOffsetFromSeed / Hash01FromSeed 와 정수 단위로 동일) ──
_U32 = 0xFFFFFFFF


def _to_int32(u):
    u &= _U32
    return u - 0x100000000 if u >= 0x80000000 else u


def _mix32(h):
    h &= _U32
    h ^= h >> 16
    h = (h * 0x7FEB352D) & _U32
    h ^= h >> 15
    h = (h * 0x846CA68B) & _U32
    h ^= h >> 16
    return h


def compute_noise_seed(world_seed, island_id):
    h = ((world_seed * 73856093) & _U32) ^ ((island_id * 19349663) & _U32) ^ 0x9E3779B9
    seed = _to_int32(_mix32(h))
    return 0 if seed == LEGACY_NOISE_SEED else seed


def noise_offset_from_seed(noise_seed, axis_salt):
    return (_mix32((noise_seed & _U32) ^ axis_salt) & 0xFFFFFF) / float(0x1000000) * NOISE_OFFSET_SPAN


def hash01(noise_seed, salt):
    """[0,1) 결정적 스칼라. 난수 스트림을 만들지 않는 순수 해시다(C# Hash01FromSeed 와 동일)."""
    return (_mix32((noise_seed & _U32) ^ salt) & 0xFFFFFF) / float(0x1000000)


def hash_range(noise_seed, salt, lo, hi):
    return lo + (hi - lo) * hash01(noise_seed, salt)


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


# ── 프로파일 선택 (C# SelectShapeProfile 과 동일) ──────────────────────────────────────
PROFILE_COUNT = 8
PROFILE_NAMES_KO = [
    "0 완만한 초원",     # 시작 섬 고정
    "1 단봉",
    "2 쌍봉",
    "3 초승달",
    "4 가운데 수로",
    "5 석호(환초)",
    "6 길쭉한 능선",
    "7 고원+절벽",
]
PROFILE_NAMES_EN = [
    "0 Gentle Meadow",
    "1 Single Peak",
    "2 Twin Peaks",
    "3 Crescent",
    "4 Split Channel",
    "5 Lagoon Atoll",
    "6 Long Ridge",
    "7 Mesa & Cliff",
]
PROFILE_DESC_KO = [
    "넓고 평평한 초원. 시작 섬 전용",
    "중심에서 비껴난 봉우리 하나 + 완만한 어깨",
    "안부(saddle)로 이어진 봉우리 둘",
    "한쪽에 큰 만(灣)이 파인 초승달",
    "좁은 수로가 섬을 가로질러 두 쪽으로 가른다",
    "가운데가 얕은 물인 고리 모양 섬",
    "한 축으로 길게 늘어난 능선",
    "평평한 고원 + 한쪽 절벽 · 반대쪽 완경사 우회로",
]
PROFILE_DESC_EN = [
    "Wide flat meadow. Reserved for the start island",
    "One off-centre peak with a gentle shoulder",
    "Two peaks joined by a saddle",
    "A large bay bitten out of one side",
    "A narrow channel splits the island in two",
    "Ring-shaped island around a shallow inner lagoon",
    "Stretched along one axis into a ridge",
    "Flat mesa with a cliff on one side and a ramp opposite",
]


def select_shape_profile(world_seed, island_id):
    """
    islandId 0 은 항상 0(가장 완만한 프로파일). 1~7 은 나머지 7종의 **시드 의존 순열**이라
    첫 8개 섬이 서로 다른 프로파일을 받는다. 8 이상은 순수 해시로 고른다.
    난수 스트림을 만들지 않는다.
    """
    if island_id <= 0:
        return 0
    if island_id < PROFILE_COUNT:
        pool = list(range(1, PROFILE_COUNT))
        # Fisher-Yates. 셔플 seed 는 worldSeed 만 쓰므로 섬마다 같은 순열을 재현한다.
        h = _mix32((world_seed & _U32) ^ 0x2545F491)
        for i in range(len(pool) - 1, 0, -1):
            h = _mix32(h ^ 0x9E3779B9)
            j = h % (i + 1)
            pool[i], pool[j] = pool[j], pool[i]
        return pool[island_id - 1]
    return 1 + (_mix32(((world_seed * 40503) & _U32) ^ ((island_id * 2654435761) & _U32)) % (PROFILE_COUNT - 1))


# ── 프로파일 파라미터 테이블 ────────────────────────────────────────────────────────
# 좌표(x/z/반경/폭)는 **R 대비 비율**, 높이/깊이는 **maxHeight 대비 비율**이다.
# 이 표는 C# IslandMeshGenerator.BuildProfile 의 switch 와 값이 1:1로 같다.
class Profile:
    __slots__ = (
        "index", "height_scale", "plateau_pow",
        "mask_base", "mask_h2", "mask_h3", "mask_h5",
        "stretch", "spin",
        "bite_x", "bite_z", "bite_radius", "bite_strength",
        "dome_a", "dome_b",           # (x, z, radius, amp)
        "ridge",                      # (x0, z0, x1, z1, width, amp)
        "channel",                    # (x0, z0, x1, z1, width, depth)
        "ring_radius", "ring_width", "ring_amp", "basin_radius", "basin_depth",
        "mesa_amp", "mesa_radius", "mesa_x", "mesa_z", "mesa_cliff_angle", "mesa_soft_min", "mesa_soft_max",
        "noise_amp", "roughness",
        "radial_mask",                # 사진 윤곽 주입용(등간격 각도 샘플). None 이면 위 하모닉 사용.
    )

    def __init__(self, **kw):
        for slot in self.__slots__:
            setattr(self, slot, kw.get(slot, _DEFAULTS[slot]))


_DEFAULTS = dict(
    index=0, height_scale=0.6, plateau_pow=1.0,
    mask_base=0.94, mask_h2=0.0, mask_h3=0.0, mask_h5=0.0,
    stretch=1.0, spin=0.0,
    bite_x=0.0, bite_z=0.0, bite_radius=0.0, bite_strength=0.0,
    dome_a=(0.0, 0.0, 0.0, 0.0), dome_b=(0.0, 0.0, 0.0, 0.0),
    ridge=(0.0, 0.0, 0.0, 0.0, 0.0, 0.0),
    channel=(0.0, 0.0, 0.0, 0.0, 0.0, 0.0),
    ring_radius=0.0, ring_width=0.0, ring_amp=0.0, basin_radius=0.0, basin_depth=0.0,
    mesa_amp=0.0, mesa_radius=0.0, mesa_x=0.0, mesa_z=0.0, mesa_cliff_angle=0.0, mesa_soft_min=0.0, mesa_soft_max=0.0,
    noise_amp=1.0, roughness=1.0,
    radial_mask=None,
)


def build_profile(index, noise_seed):
    """(프로파일 번호, 섬 시드) → 파라미터. 같은 프로파일이라도 섬마다 조금씩 다르게 흔든다."""
    spin = hash_range(noise_seed, 0x3C6EF372, 0.0, 2.0 * math.pi)
    j1 = hash01(noise_seed, 0x85EBCA6B)   # [0,1) 범용 지터
    j2 = hash01(noise_seed, 0xC2B2AE35)
    j3 = hash01(noise_seed, 0x27D4EB2F)

    if index == 0:  # 완만한 초원 — 시작 섬
        # 시작 섬은 "튜토리얼 평지"라 마스크 흔들림도 가장 얌전하다(그래도 원은 아니다).
        return Profile(
            index=0, height_scale=0.30, plateau_pow=0.40,
            mask_base=0.90, mask_h2=0.070 + 0.020 * j1, mask_h3=0.050, mask_h5=0.026,
            spin=spin,
            dome_a=(0.20 - 0.40 * j2, 0.18 - 0.36 * j3, 0.55, 0.09),
            noise_amp=0.50, roughness=0.9,
        )

    if index == 1:  # 단봉
        return Profile(
            index=1, height_scale=0.36, plateau_pow=1.15,
            mask_base=0.87, mask_h2=0.115, mask_h3=0.080 + 0.025 * j1, mask_h5=0.040,
            spin=spin,
            dome_a=(0.26, 0.12 - 0.24 * j2, 0.50, 0.72),
            dome_b=(-0.38, -0.30, 0.40, 0.16),
            noise_amp=1.0, roughness=1.0,
        )

    if index == 2:  # 쌍봉
        return Profile(
            index=2, height_scale=0.30, plateau_pow=1.0,
            mask_base=0.86, mask_h2=0.135, mask_h3=0.070, mask_h5=0.038 + 0.020 * j1,
            stretch=1.22, spin=spin,
            dome_a=(0.40, 0.18, 0.40, 0.72),
            dome_b=(-0.38, -0.22, 0.36, 0.62 + 0.10 * j2),
            ridge=(0.34, 0.15, -0.32, -0.19, 0.26, 0.16),
            noise_amp=1.0, roughness=1.05,
        )

    if index == 3:  # 초승달
        return Profile(
            index=3, height_scale=0.34, plateau_pow=0.75,
            mask_base=0.92, mask_h2=0.060, mask_h3=0.050, mask_h5=0.026,
            spin=spin,
            # 만(灣)의 중심을 섬 밖(0.98R)에 두고 반경을 크게 잡아, 안으로 크게 파고들되
            # 파인 면적의 대부분이 산포 원(0.8R) **바깥**에 남게 한다.
            bite_x=0.79, bite_z=0.0, bite_radius=0.82, bite_strength=3.9 + 0.5 * j1,
            dome_a=(-0.44, 0.0, 0.54, 0.46),
            ridge=(-0.30, 0.56, -0.30, -0.56, 0.28, 0.30),
            noise_amp=0.95, roughness=1.0,
        )

    if index == 4:  # 가운데 수로
        return Profile(
            index=4, height_scale=0.26, plateau_pow=0.50,
            mask_base=0.88, mask_h2=0.100, mask_h3=0.065, mask_h5=0.034,
            spin=spin,
            dome_a=(0.42, 0.36, 0.44, 0.34),
            dome_b=(-0.42, -0.36, 0.44, 0.30 + 0.08 * j2),
            # 수로는 섬을 완전히 가로지른다(끝점이 마스크 밖). 폭을 좁게 유지해 육지 비율을 지키고,
            # 깊이는 "중심 육지 높이 + 1.5m" 수준이면 충분하다(더 깊으면 둑이 45도를 넘는다).
            channel=(-1.25, 0.66, 1.25, -0.66, 0.150, 0.50),
            noise_amp=0.8, roughness=1.0,
        )

    if index == 5:  # 석호(환초)
        # 링을 **넓고 낮게** 잡는다. 좁고 높은 링은 능선이라 건축 평탄 구역이 생기지 않는다.
        return Profile(
            index=5, height_scale=0.16, plateau_pow=0.45,
            mask_base=0.89, mask_h2=0.090, mask_h3=0.060 + 0.020 * j1, mask_h5=0.032,
            stretch=1.10, spin=spin,
            ring_radius=0.62, ring_width=0.38, ring_amp=0.38,
            basin_radius=0.46, basin_depth=0.56,
            noise_amp=0.6, roughness=1.0,
        )

    if index == 6:  # 길쭉한 능선
        return Profile(
            index=6, height_scale=0.28, plateau_pow=0.85,
            mask_base=0.85, mask_h2=0.100, mask_h3=0.070, mask_h5=0.036,
            stretch=1.38, spin=spin,
            ridge=(0.55, 0.0, -0.55, 0.0, 0.50, 0.62 + 0.08 * j3),
            dome_a=(0.12, 0.0, 0.72, 0.12),
            noise_amp=0.95, roughness=1.0,
        )

    # 7: 고원 + 절벽
    return Profile(
        index=7, height_scale=0.26, plateau_pow=0.45,
        mask_base=0.88, mask_h2=0.095, mask_h3=0.062, mask_h5=0.033,
        spin=spin,
        mesa_amp=0.80, mesa_radius=0.34, mesa_cliff_angle=0.0,
        mesa_soft_min=0.09, mesa_soft_max=1.70,
        mesa_x=0.20, mesa_z=0.10,
        dome_a=(-0.40, -0.05, 0.60, 0.12),
        noise_amp=0.6, roughness=1.0,
    )


# ── 조립 가능한 높이 프리미티브 ──────────────────────────────────────────────────────
def bump(d):
    """d(정규화 거리) 0 → 1, 1 이상 → 0. (1-d²)² 라 경계에서 기울기가 0이라 이음매가 없다."""
    dd = np.clip(d, 0.0, 1.0)
    w = 1.0 - dd * dd
    return w * w


def seg_distance(x, z, x0, z0, x1, z1):
    """점 (x,z) 에서 선분 (x0,z0)-(x1,z1) 까지의 거리."""
    dx, dz = x1 - x0, z1 - z0
    ll = dx * dx + dz * dz
    if ll <= 1e-9:
        return np.hypot(x - x0, z - z0)
    s = np.clip(((x - x0) * dx + (z - z0) * dz) / ll, 0.0, 1.0)
    return np.hypot(x - (x0 + s * dx), z - (z0 + s * dz))


def mask_at(angle, p):
    """각도별 반지름 마스크(0.15~1.0). 윤곽을 원에서 벗어나게 하는 유일한 장치다."""
    if p.radial_mask is not None:
        # ★ 사진 윤곽 주입 지점 ★ 등간격 각도 샘플 배열을 선형 보간해 그대로 쓴다.
        n = len(p.radial_mask)
        f = (angle % (2.0 * math.pi)) / (2.0 * math.pi) * n
        i0 = np.floor(f).astype(np.int64) % n
        i1 = (i0 + 1) % n
        w = f - np.floor(f)
        arr = np.asarray(p.radial_mask, dtype=np.float64)
        m = arr[i0] * (1.0 - w) + arr[i1] * w
    else:
        m = (p.mask_base
             + p.mask_h2 * np.cos(2.0 * angle)
             + p.mask_h3 * np.cos(3.0 * angle + 1.7)
             + p.mask_h5 * np.cos(5.0 * angle + 0.6))
    return np.clip(m, 0.15, 1.0)


def fbm(x, z, ox, oz, scale, octaves=3):
    """다중 옥타브 펄린. 반환값 대략 [-0.5, 0.5] 정규화."""
    total = np.zeros_like(np.asarray(x, dtype=np.float64))
    amp, freq, norm = 1.0, 1.0, 0.0
    for _ in range(octaves):
        total += (perlin01(x * scale * freq + ox * freq, z * scale * freq + oz * freq) - 0.5) * amp
        norm += amp
        amp *= 0.45
        freq *= 2.17
    return total / norm


def sculpt_height(x, z, radius, max_height, noise_seed, p):
    """
    ★ 조각된 높이장 ★ — 메시 토폴로지는 그대로 두고 y 만 조각한다.
    y < 0 인 곳은 그대로 바다가 된다(불투명 바다 평면이 y=0 에 있다).
    """
    ox, oz, sox, soz = offsets_for_seed(noise_seed)

    # (1) 섬 전체 회전 — 같은 프로파일이라도 섬마다 방향이 다르다.
    ca, sa = math.cos(p.spin), math.sin(p.spin)
    xr = x * ca + z * sa
    zr = -x * sa + z * ca

    # (2) 이방성 — 한 축으로 늘린다(면적은 보존된다: a·b = R²·mask²).
    u = xr * p.stretch
    v = zr / p.stretch
    re = np.hypot(u, v)
    ang = np.arctan2(v, u)

    # (3) 각도별 반지름 마스크 → q. q=1 이 이 프로파일의 해안선이다.
    m = mask_at(ang, p)
    q = re / np.maximum(1e-4, radius * m)

    # (4) 만(灣) — q 를 국소적으로 부풀려 해안을 안쪽으로 밀어 넣는다(초승달).
    if p.bite_strength > 0.0:
        bd = np.hypot(xr - p.bite_x * radius, zr - p.bite_z * radius) / (p.bite_radius * radius)
        q = q * (1.0 + p.bite_strength * bump(bd))

    qc = np.minimum(q, 1.0)

    # (5) 기본 낙차. plateau_pow < 1 이면 정상부가 평평해지고 가장자리가 가팔라진다.
    y = max_height * p.height_scale * np.power(np.cos(qc * math.pi * 0.5), p.plateau_pow)

    # (6) 돔 2개
    for (dx, dz, dr, da) in (p.dome_a, p.dome_b):
        if da != 0.0 and dr > 0.0:
            d = np.hypot(xr - dx * radius, zr - dz * radius) / (dr * radius)
            y = y + max_height * da * bump(d)

    # (7) 능선(선분 거리 감쇠)
    x0, z0, x1, z1, rw, ra = p.ridge
    if ra != 0.0 and rw > 0.0:
        d = seg_distance(xr, zr, x0 * radius, z0 * radius, x1 * radius, z1 * radius) / (rw * radius)
        y = y + max_height * ra * bump(d)

    # (8) 수로(음의 능선). y<0 까지 내려가 실제로 물이 흐른다.
    cx0, cz0, cx1, cz1, cw, cd = p.channel
    if cd != 0.0 and cw > 0.0:
        d = seg_distance(xr, zr, cx0 * radius, cz0 * radius, cx1 * radius, cz1 * radius) / (cw * radius)
        y = y - max_height * cd * bump(d)

    # (9) 석호: 링(양) + 중앙 분지(음)
    if p.ring_amp != 0.0:
        rr = np.hypot(xr, zr)
        d = np.abs(rr - p.ring_radius * radius) / (p.ring_width * radius)
        y = y + max_height * p.ring_amp * bump(d)
    if p.basin_depth != 0.0:
        rr = np.hypot(xr, zr)
        y = y - max_height * p.basin_depth * bump(rr / (p.basin_radius * radius))

    # (10) 메사(고원). 절벽 방향은 가장자리 폭이 좁고(급경사), 반대쪽은 넓다(걸어 오르는 우회로).
    if p.mesa_amp != 0.0:
        mx, mz = xr - p.mesa_x * radius, zr - p.mesa_z * radius
        rr = np.hypot(mx, mz)
        a = np.arctan2(mz, mx)
        soft = p.mesa_soft_min + (p.mesa_soft_max - p.mesa_soft_min) * 0.5 * (1.0 - np.cos(a - p.mesa_cliff_angle))
        k = np.clip(((1.0 + soft) - rr / (p.mesa_radius * radius)) / np.maximum(1e-4, soft), 0.0, 1.0)
        y = y + max_height * p.mesa_amp * (k * k * (3.0 - 2.0 * k))

    # (11) 다중 옥타브 노이즈. 해안 쪽에서 줄여 물가가 지저분해지지 않게 한다.
    n = fbm(x, z, ox, oz, NOISE_SCALE * p.roughness) * NOISE_AMPLITUDE * p.noise_amp * (1.0 - qc * 0.85)
    y = y + n

    # (12) 해안 잠수. q 기준이라 마스크/만으로 안쪽에 들어온 해안에서도 똑같이 동작한다.
    shore_t = (q - (1.0 - SHORE_BAND_FRACTION)) / SHORE_BAND_FRACTION
    band = np.clip(shore_t, 0.0, 1.0)
    submerge = SHORE_SUBMERGE_DEPTH * band * band
    submerge = submerge + np.minimum(SHELF_DROP_MAX, SHELF_DROP_PER_Q * np.maximum(0.0, q - 1.0))
    shore_noise = (
        (perlin01(x * SHORE_NOISE_SCALE + sox, z * SHORE_NOISE_SCALE + soz) - 0.5)
        * SHORE_NOISE_AMPLITUDE * band
    )
    y = y - submerge + shore_noise

    return y


def legacy_height(x, z, radius, max_height, noise_scale=NOISE_SCALE, noise_amplitude=NOISE_AMPLITUDE):
    """회귀 안전장치 경로(noiseSeed 생략 = LegacyNoiseSeed) — 예전 코사인 돔 그대로."""
    ox, oz, sox, soz = offsets_for_seed(LEGACY_NOISE_SEED)
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
    inner = np.maximum(0.0, base_height + noise)
    outer = base_height + noise - submerge + shore_noise
    return np.where(shore_t <= 0.0, inner, outer)


def island_height(x, z, radius, max_height, noise_seed, profile=None):
    if noise_seed == LEGACY_NOISE_SEED or profile is None:
        y = legacy_height(x, z, radius, max_height)
    else:
        y = sculpt_height(x, z, radius, max_height, noise_seed, profile)
    r = np.hypot(x, z)
    return np.where(r <= radius, y, np.nan)   # 메시 바깥(원 밖)은 지형이 아니다


# ── 측정 ───────────────────────────────────────────────────────────────────────────
def sample_field(radius, noise_seed, profile, resolution=360):
    span = radius * 1.02
    axis = np.linspace(-span, span, resolution)
    x, z = np.meshgrid(axis, axis)
    y = island_height(x, z, radius, TERRAIN_MAX_HEIGHT, noise_seed, profile)
    return axis, x, z, y


def land_fraction(radius, noise_seed, profile, resolution=420):
    """★ 이번 배치의 핵심 지표 ★ 산포 원(0.8R) 안에서 y>0 인 면적 비율."""
    span = radius * SCATTER_FRACTION
    axis = np.linspace(-span, span, resolution)
    x, z = np.meshgrid(axis, axis)
    inside = np.hypot(x, z) <= span
    y = island_height(x, z, radius, TERRAIN_MAX_HEIGHT, noise_seed, profile)
    land = inside & np.isfinite(y) & (y > 0.0)
    return land.sum() / max(1, inside.sum())


def mesh_grid_vertices(radius, noise_seed, profile):
    """
    WorldMapManager 와 **같은 링/세그먼트 해상도**로 실제 메시 정점을 만든다.
    경사는 이 격자에서 재야 의미가 있다 — 플레이어가 걷는 것은 이 삼각형들이다.
    """
    ring_count = int(np.clip(round(radius / 5.0), 6, 40))
    radial_segments = int(np.clip(round(radius * 1.5), 24, 90))
    ring = np.arange(1, ring_count + 1)[:, None]
    seg = np.arange(radial_segments)[None, :]
    t = ring / ring_count
    ang = seg / radial_segments * 2.0 * math.pi
    x = np.cos(ang) * (t * radius)
    z = np.sin(ang) * (t * radius)
    y = island_height(x, z, radius, TERRAIN_MAX_HEIGHT, noise_seed, profile)
    return x, z, y, ring_count, radial_segments


def max_walkable_slope(radius, noise_seed, profile):
    """
    메시 정점 격자에서 이웃 정점 사이 경사를 재고, **육지(양 끝 모두 y>0)** 구간의 최대/95퍼센타일을
    돌려준다. 물속 사면은 플레이어가 걸을 일이 없으므로 제외한다.
    """
    x, z, y, ring_count, radial_segments = mesh_grid_vertices(radius, noise_seed, profile)

    def slopes(dx, dz, dy, mask):
        run = np.hypot(dx, dz)
        with np.errstate(divide="ignore", invalid="ignore"):
            s = np.degrees(np.arctan2(np.abs(dy), np.maximum(run, 1e-6)))
        return s[mask]

    # 반경 방향(링 → 다음 링)
    land = (y > 0.0)
    m_r = land[:-1, :] & land[1:, :]
    sr = slopes(x[1:, :] - x[:-1, :], z[1:, :] - z[:-1, :], y[1:, :] - y[:-1, :], m_r)
    # 원주 방향(세그먼트 → 다음 세그먼트)
    xs, zs, ys = np.roll(x, -1, axis=1), np.roll(z, -1, axis=1), np.roll(y, -1, axis=1)
    m_s = land & (ys > 0.0)
    ss = slopes(xs - x, zs - z, ys - y, m_s)

    allslope = np.concatenate([sr, ss])
    if allslope.size == 0:
        return 0.0, 0.0
    return float(allslope.max()), float(np.percentile(allslope, 95.0))


def flat_zone(radius, noise_seed, profile, resolution=140):
    """
    건축 가능성: 반경 FLAT_PROBE_RADIUS(12m) 안의 육지 높이 편차가 FLAT_MAX_RELIEF(1.5m) 이하인
    지점이 실제로 존재하는가. (중심 후보는 산포 원 안 + 물가에서 12m 이상 떨어진 육지만 본다.)
    반환: (존재 여부, 그런 지점의 비율, 가장 평탄한 지점의 편차)
    """
    span = radius * SCATTER_FRACTION
    axis = np.linspace(-span, span, resolution)
    cx, cz = np.meshgrid(axis, axis)
    cand = (np.hypot(cx, cz) <= span)
    cy = island_height(cx, cz, radius, TERRAIN_MAX_HEIGHT, noise_seed, profile)
    cand &= np.isfinite(cy) & (cy > 0.0)

    # 각 후보 주변 12m 원을 16방위 × 3반경으로 훑는다(격자 컨볼루션보다 후보 수가 적어 더 싸다).
    offs = []
    for k in range(16):
        a = k / 16.0 * 2.0 * math.pi
        for rr in (FLAT_PROBE_RADIUS * 0.45, FLAT_PROBE_RADIUS * 0.78, FLAT_PROBE_RADIUS):
            offs.append((math.cos(a) * rr, math.sin(a) * rr))

    idx = np.argwhere(cand)
    if idx.size == 0:
        return False, 0.0, float("inf")
    px = cx[cand]
    pz = cz[cand]
    lo = cy[cand].copy()
    hi = cy[cand].copy()
    ok = np.ones(px.shape, dtype=bool)
    for (dx, dz) in offs:
        s = island_height(px + dx, pz + dz, radius, TERRAIN_MAX_HEIGHT, noise_seed, profile)
        s = np.where(np.isfinite(s), s, -99.0)
        ok &= (s > 0.0)          # 물에 걸치면 바닥을 깔 수 없다
        lo = np.minimum(lo, s)
        hi = np.maximum(hi, s)
    relief = np.where(ok, hi - lo, np.inf)
    good = relief <= FLAT_MAX_RELIEF
    total = max(1, int(cand.sum()))
    return bool(good.any()), float(good.sum()) / total, float(relief.min())


def walkable_approach(radius, noise_seed, profile, azimuths=72, step=2.0):
    """
    ★ 경사 상한 우회로 검사 ★ 섬 중심을 향해 방위각마다 **물가에서 안쪽으로** 2m씩 걸어 들어가며
    구간 경사를 잰다. 그 방위에서 한 번도 45도를 넘지 않으면 "걸어 올라갈 수 있는 접근로"로 센다.
    절벽이 있는 프로파일이라도 이 비율이 0이 아니면 우회로가 실제로 존재한다는 뜻이다.
    """
    rs = np.arange(0.0, radius, step)
    ok = 0
    for k in range(azimuths):
        a = k / azimuths * 2.0 * math.pi
        x = np.cos(a) * rs
        z = np.sin(a) * rs
        y = island_height(x, z, radius, TERRAIN_MAX_HEIGHT, noise_seed, profile)
        y = np.where(np.isfinite(y), y, -99.0)
        land = y > 0.0
        pair = land[:-1] & land[1:]
        if not pair.any():
            continue
        d = np.degrees(np.arctan2(np.abs(np.diff(y)), step))
        if d[pair].max() <= 45.0:
            ok += 1
    return ok / azimuths


def measure(radius, noise_seed, profile):
    y_all = sample_field(radius, noise_seed, profile)[3]
    finite = y_all[np.isfinite(y_all)]
    peak = float(finite.max()) if finite.size else 0.0
    land = land_fraction(radius, noise_seed, profile)
    smax, s95 = max_walkable_slope(radius, noise_seed, profile)
    has_flat, flat_ratio, best_relief = flat_zone(radius, noise_seed, profile)
    walk = walkable_approach(radius, noise_seed, profile)
    return dict(peak=peak, land=land, slope_max=smax, slope_p95=s95,
                flat=has_flat, flat_ratio=flat_ratio, best_relief=best_relief, walk=walk)


# ── 렌더 ───────────────────────────────────────────────────────────────────────────
from matplotlib.colors import LinearSegmentedColormap

# 육지 전용 컬러맵. matplotlib 의 "terrain"은 높은 곳이 흰색(설선)이라 열대 섬으로 안 읽힌다.
# 물가 모래 → 초원 → 관목 → 바위 순으로 올라가는 램프를 직접 만든다.
LAND_CMAP = LinearSegmentedColormap.from_list("island_land", [
    (0.00, "#DACB9B"),   # 물가 모래
    (0.12, "#C7C57F"),
    (0.30, "#9DB863"),   # 초원
    (0.55, "#7C9A4E"),
    (0.78, "#93884F"),   # 마른 관목/흙
    (1.00, "#C4B49A"),   # 노출 암반
])


def hillshade(y, step, ve=2.2):
    filled = np.nan_to_num(y, nan=0.0)
    gz, gx = np.gradient(filled, step)
    nx, nz = -gx * ve, -gz * ve
    nl = np.sqrt(nx * nx + nz * nz + 1.0)
    az, alt = math.radians(315.0), math.radians(45.0)
    lx, lz, ly = math.cos(alt) * math.cos(az), math.cos(alt) * math.sin(az), math.sin(alt)
    return np.clip((nx * lx + nz * lz + ly) / nl, 0.0, 1.0)


def render_top(ax, radius, noise_seed, profile, stats):
    axis, x, z, y = sample_field(radius, noise_seed, profile)
    span = axis[-1]
    extent = [-span, span, -span, span]

    # 물속(y<=0)은 파랗게, 육지는 지형색. 두 컬러맵을 따로 깔아 물/육지를 색 계열로 완전히 가른다.
    water = np.where(np.isfinite(y) & (y <= 0.0), y, np.nan)
    landy = np.where(np.isfinite(y) & (y > 0.0), y, np.nan)
    vmax = max(1.0, TERRAIN_MAX_HEIGHT * 0.95)
    ax.imshow(water, extent=extent, origin="lower", cmap="Blues_r",
              vmin=-7.0, vmax=1.6, interpolation="bilinear")
    ax.imshow(landy, extent=extent, origin="lower", cmap=LAND_CMAP,
              vmin=0.0, vmax=vmax, interpolation="bilinear")

    ax.imshow(np.where(np.isfinite(y), hillshade(y, axis[1] - axis[0]), np.nan), extent=extent,
              origin="lower", cmap="gray", vmin=0.30, vmax=1.0, alpha=0.38, interpolation="bilinear")

    ax.contour(x, z, y, levels=np.arange(1.0, TERRAIN_MAX_HEIGHT * 1.4, 1.0),
               colors="black", linewidths=0.3, alpha=0.45)
    ax.contour(x, z, y, levels=[0.0], colors="#06214F", linewidths=2.4)   # 물가

    # 산포 원(0.8R) — 자원/위험요소가 뿌려지는 구역. 육지 비율은 이 원 안에서 잰다.
    th = np.linspace(0, 2 * math.pi, 200)
    ax.plot(np.cos(th) * radius * SCATTER_FRACTION, np.sin(th) * radius * SCATTER_FRACTION,
            color="#D81E5B", lw=1.1, ls="--", alpha=0.95)

    ax.set_xticks([])
    ax.set_yticks([])
    return stats


def render_3d(ax, radius, noise_seed, profile):
    """
    3D 사면 음영 — 위에서만 보면 못 읽히는 "높이 감"을 준다.
    바다는 별도 평면을 겹치지 않고 **높이를 0에서 자른 같은 서피스**로 그린다
    (matplotlib 3d 는 서피스끼리의 깊이 정렬이 신뢰할 수 없어, 반투명 평면을 겹치면
     섬이 물 아래로 사라지는 그림이 나온다 — 1차 렌더에서 실제로 그렇게 나왔다).
    """
    n = 150
    span = radius
    axis = np.linspace(-span, span, n)
    x, z = np.meshgrid(axis, axis)
    y = island_height(x, z, radius, TERRAIN_MAX_HEIGHT, noise_seed, profile)
    y = np.nan_to_num(y, nan=-6.0)

    surface = np.maximum(y, 0.0)                       # 수면 위는 지형, 수면 아래는 평평한 바다
    land_t = np.clip(y / max(1.0, TERRAIN_MAX_HEIGHT * 0.95), 0.0, 1.0)
    colors = LAND_CMAP(land_t)
    deep = np.clip((-y) / 5.0, 0.0, 1.0)               # 깊을수록 진한 파랑
    sea_rgb = np.stack([
        0.42 - 0.28 * deep, 0.68 - 0.36 * deep, 0.88 - 0.30 * deep,
        np.ones_like(deep),
    ], axis=-1)
    colors = np.where((y > 0.0)[..., None], colors, sea_rgb)

    ax.plot_surface(x, z, surface, rstride=1, cstride=1, linewidth=0,
                    facecolors=colors, shade=True, antialiased=False)
    ax.set_zlim(0.0, TERRAIN_MAX_HEIGHT * 1.25)
    ax.set_xlim(-span, span)
    ax.set_ylim(-span, span)
    # 수직 과장. 90m 섬에 8m 지형이라 실축(1:1)으로 그리면 어떤 프로파일도 종잇장으로 보인다.
    ax.set_box_aspect((1, 1, 0.34))
    ax.view_init(elev=24, azim=-62)
    ax.set_xticks([])
    ax.set_yticks([])
    ax.set_zticks([])
    for pane in (ax.xaxis, ax.yaxis, ax.zaxis):
        pane.pane.set_visible(False)
        pane.line.set_visible(False)
    ax.grid(False)


def main():
    here = os.path.dirname(os.path.abspath(__file__))
    out_dir = os.path.join(here, "_preview")
    os.makedirs(out_dir, exist_ok=True)

    world_seed = 12345   # WorldMapManager.worldSeed 는 씬 직렬화 값(0 이면 실행 시 무작위)이다. 상대 비교용.
    radius = RADIUS_MEDIUM

    names = PROFILE_NAMES_KO if HANGUL_OK else PROFILE_NAMES_EN
    descs = PROFILE_DESC_KO if HANGUL_OK else PROFILE_DESC_EN

    rows = []
    for index in range(PROFILE_COUNT):
        # 프로파일마다 대표 섬 시드 하나를 골라 그린다(islandId 는 프로파일 번호를 그대로 쓴다).
        seed = compute_noise_seed(world_seed, index)
        prof = build_profile(index, seed)
        rows.append((index, seed, prof, measure(radius, seed, prof)))

    # constrained layout 은 imshow 의 aspect="equal" 과 3d 축이 섞이면 칸 사이에 큰 빈 공간을
    # 만들고 라벨이 칸 밖으로 삐져나간다(2차 렌더에서 실제로 그렇게 나왔다). 좌표를 직접 잡는다.
    fig = plt.figure(figsize=(20.0, 6.6))
    gs = fig.add_gridspec(2, PROFILE_COUNT, height_ratios=[1.0, 0.80],
                          left=0.006, right=0.994, top=0.885, bottom=0.012,
                          wspace=0.02, hspace=0.02)

    for col, (index, seed, prof, st) in enumerate(rows):
        ax = fig.add_subplot(gs[0, col])
        render_top(ax, radius, seed, prof, st)
        ax.set_title(names[index], fontsize=11, pad=3)
        if HANGUL_OK:
            flat_txt = "평탄구역 O" if st["flat"] else "평탄구역 X"
            stat = (f"육지 {st['land']*100:.0f}%  ·  최대경사 {st['slope_max']:.0f}°\n"
                    f"최고 {st['peak']:.1f}m  ·  {flat_txt}")
        else:
            flat_txt = "flat ok" if st["flat"] else "flat NONE"
            stat = (f"land {st['land']*100:.0f}%  ·  slope {st['slope_max']:.0f}°\n"
                    f"peak {st['peak']:.1f}m  ·  {flat_txt}")
        ok = st["land"] >= 0.70 and st["flat"]
        ax.text(0.03, 0.03, stat, transform=ax.transAxes, fontsize=8.6, color="white",
                va="bottom", ha="left", linespacing=1.35, clip_on=False,
                bbox=dict(boxstyle="round,pad=0.30", fc="#1B7F3B" if ok else "#B00020",
                          alpha=0.90, lw=0))
        ax.text(0.03, 0.97, descs[index], transform=ax.transAxes, fontsize=7.4, color="#101010",
                va="top", ha="left", wrap=True, clip_on=False,
                bbox=dict(boxstyle="round,pad=0.24", fc="white", alpha=0.78, lw=0))

        ax3 = fig.add_subplot(gs[1, col], projection="3d")
        render_3d(ax3, radius, seed, prof)

    if HANGUL_OK:
        suptitle = (
            f"IslandMeshGenerator — 지형 프로파일 8종 후보 시트 (R={radius:.0f}m, terrainMaxHeight=8, worldSeed={world_seed})\n"
            "굵은 남색 = 물가(y=0) · 분홍 점선 = 자원 산포 원 0.8R(이 안의 육지 비율이 70% 이상이어야 채택) · "
            "노이즈는 Unity Mathf.PerlinNoise 와 같은 계열의 근사이며 비트 단위로 같지는 않다"
        )
    else:
        suptitle = (
            f"IslandMeshGenerator - 8 terrain profiles (R={radius:.0f}m, terrainMaxHeight=8, worldSeed={world_seed})\n"
            "thick navy = waterline y=0 | pink dashed = 0.8R scatter ring (land inside must be >= 70%) | "
            "noise is a same-family approximation of Unity Mathf.PerlinNoise, not bit-identical"
        )
    fig.suptitle(suptitle, fontsize=11.5, y=0.985)

    out_path = os.path.join(out_dir, "island_profile_sheet.png")
    fig.savefig(out_path, dpi=105)
    plt.close(fig)
    print(out_path)

    # ── 표 ────────────────────────────────────────────────────────────────────────
    print("\n{:<18} {:>7} {:>9} {:>9} {:>8} {:>6} {:>7} {:>9}".format(
        "profile", "land%", "slopeMax", "slopeP95", "peak", "flat", "flat%", "walkable"))
    for index, seed, prof, st in rows:
        print("{:<18} {:>6.1f}% {:>8.1f}° {:>8.1f}° {:>7.2f}m {:>6} {:>6.1f}% {:>8.0f}%".format(
            PROFILE_NAMES_EN[index], st["land"] * 100.0, st["slope_max"], st["slope_p95"],
            st["peak"], "yes" if st["flat"] else "NO", st["flat_ratio"] * 100.0,
            st["walk"] * 100.0))

    # 규모별 재확인 — 반지름이 작을수록 같은 비율의 수로/절벽이 더 가팔라진다.
    print("\n[per-radius recheck] land% / slopeMax")
    for r in (RADIUS_SMALL, RADIUS_MEDIUM, RADIUS_LARGE):
        cells = []
        for index in range(PROFILE_COUNT):
            seed = compute_noise_seed(world_seed, index)
            prof = build_profile(index, seed)
            lf = land_fraction(r, seed, prof, resolution=300)
            sm, _ = max_walkable_slope(r, seed, prof)
            cells.append(f"P{index}:{lf*100:4.0f}%/{sm:4.0f}°")
        print(f"  R={r:>5.0f}m  " + "  ".join(cells))

    # 서로 다른 섬인가 — 같은 프로파일이라도 시드가 다르면 갈리는지 확인한다.
    print("\n[profile assignment] worldSeed=%d" % world_seed)
    print("  " + "  ".join(f"island{i}->P{select_shape_profile(world_seed, i)}" for i in range(9)))


if __name__ == "__main__":
    main()
