#!/usr/bin/env python3
"""
rain_set - 비 연출용 절차 텍스처 3종 생성 (2026-08-18, 강우 고퀄리티화).

    python3 Tools/textures/rain_set.py

산출물 (Assets/_Project/Resources/Textures/):
  rain_ripple.png   1024x1024 RGBA 무이음 - 젖은 표면/수면 셰이더가 섞는 빗방울 파문.
  rain_streak.png   512x512   RGBA 세로 무이음 - 빗줄기 파티클 빌보드.
  rain_droplet.png  512x512   RGBA 비무이음 - 화면(카메라 렌즈)에 얹는 물방울 오버레이.

왜 세 장인가
  "비"는 한 가지 현상이 아니라 세 층이 겹쳐야 비로 읽힌다.
    (1) 공중을 지나가는 물줄기      -> rain_streak (파티클)
    (2) 바닥/수면에 닿아 퍼지는 파문 -> rain_ripple (젖은 표면 셰이더)
    (3) 카메라(=눈)에 맺힌 물방울    -> rain_droplet (풀스크린 오버레이)
  하나만 있으면 각각 "흰 선만 떨어짐 / 땅이 그냥 반짝임 / 렌즈만 더러움"으로 보인다.

  세 장 모두 **데이터 텍스처**다(노멀·마스크·위상을 채널에 나눠 담았다).
  Unity 임포터에서 반드시 **sRGB 체크 해제(Linear)** 로 읽어야 한다. sRGB로 읽으면
  0.5(=평평한 노멀)가 0.21로 왜곡돼 표면 전체가 한쪽으로 기운다.
  타입은 Default(노멀맵 타입 아님 - RG만 노멀이고 BA는 마스크다). 압축은 알파가 있는
  채널 계약이라 DXT5/BC7 권장(BC1은 알파가 날아간다).

--------------------------------------------------------------------------------
1) rain_ripple.png - 채널 계약 (셰이더가 이대로 읽는다)
--------------------------------------------------------------------------------
  R,G  접선공간 노멀의 xy.  0.5 = 평평. OpenGL 규약(G = +V 방향이 위)으로 인코딩했다.
                          Unity 표준 노멀맵과 같은 방향이라 flipGreenChannel 불필요.
                          디코드: `float2 n = tex.rg * 2 - 1;` z는 `sqrt(1 - dot(n,n))`.
  B    파문 위상 오프셋 [0,1). **파문 하나마다 다른 상수값**이 그 파문의 영역 전체에 깔려 있다.
                          `float t = frac(_Time.y * _RippleSpeed + B);` 로 쓰면 파문마다
                          서로 다른 타이밍에 태어나고 죽는다(모든 파문이 동시에 뛰지 않는다).
                          B는 알파가 0인 빈 공간에도 (가장 가까운 파문의 값으로) 채워져 있으므로
                          `A == 0`인 픽셀의 B는 그냥 무시하면 된다.
  A    파문 세기 마스크 [0,1]. 중심 1, 가장자리 0. 노멀과 위상 둘 다 이 마스크로 페이드한다.

  권장 사용 (젖은 표면 셰이더):
      float4 rp = SAMPLE_TEXTURE2D(_RippleMap, s, worldXZ * _RippleTiling);
      float  t  = frac(_Time.y * _RippleSpeed + rp.b);      // 파문마다 다른 위상
      float  life = t * (1 - t) * 4;                        // 태어나고(0) 커지고 사라짐(0)
      float  amp  = rp.a * life * _RainIntensity;
      float2 n    = (rp.rg * 2 - 1) * amp;                  // 평평(0.5)에서 amp만큼만 흔든다
      // 링이 바깥으로 퍼지는 느낌은 UV가 아니라 amp에 t를 섞어 만든다(스케일 애니메이션 불필요).

  구조: 4x4 지터 격자에 파문 16개(요구 12~20). 크기 편차 ±40%(배율 0.6~1.4)를 유지한 채
        전역 스케일 하나만 조여 **파문끼리 겹치지 않게** 했다(실측: 겹침 0쌍, 최소 간격 0.005 UV,
        지름 123~232px). 겹치면 한 픽셀에 위상이 두 개가 되어 B 계약이 깨진다(경계에 위상 단차
        = 셰이더에서 찢어진 링으로 보인다). 겹치지 않으므로 B는 파문 영역 안에서 정확히 상수다.
  높이장: h(r) = -cos(2*pi*rings*r) * exp(-k r) * env(r) - 중심이 패이고 링 2.5~3.5개가
        감쇠하며 퍼지는 실제 빗방울 충돌 단면. 여기서 랩어라운드 중심차분으로 노멀을 뽑았다.
  무이음: 중심까지의 거리를 토러스 랩(dx = dx - round(dx))으로 재고, 노멀 미분도 np.roll
        (=랩어라운드)로 한다. 도메인 워프 노이즈도 주기적이라 사방 어디로 이어도 이음매가 없다.

--------------------------------------------------------------------------------
2) rain_streak.png - 채널 계약
--------------------------------------------------------------------------------
  R,G,B  빗줄기 색(흰색~아주 옅은 청백, 0.86~1.0). **알파 0인 배경에도 같은 색을 깔아 놨다** -
         배경을 검게 두면 밉맵/이중선형 보간이 검정을 끌어와 빗줄기에 검은 테두리가 생긴다.
         파티클 머티리얼에서 이 RGB에 틴트를 곱해 쓰면 된다(가산/알파블렌드 둘 다 무방).
  A      빗줄기 알파. 배경 0.

  세로 무이음 전용(가로는 무관하지만 덤으로 같이 감아 놨다). 빗줄기가 위/아래 경계를 넘으면
  반대편에서 이어진다. 파티클 UV를 세로로 스크롤해 "쏟아지는" 연출을 하려면 세로 랩이 필수다.
  가닥 70개, 굵기/길이/밝기 제각각이고 위아래 끝이 부드럽게 사라진다. 가닥 안쪽에는
  세로로 빠르게 변하는 노이즈를 실어(물기둥 굴절) 밋밋한 막대가 되지 않게 했다.

--------------------------------------------------------------------------------
3) rain_droplet.png - 채널 계약
--------------------------------------------------------------------------------
  R,G  물방울 노멀 xy (렌즈 굴절용). 0.5 = 평평. rain_ripple과 같은 OpenGL 규약.
       사용: 화면 UV를 `uv += (d.rg * 2 - 1) * d.a * _DropRefraction;` 로 밀어 굴절시킨다.
  B    흐름 마스크 [0,1]. 물방울이 아래로 흘러내린 젖은 자국. 물방울 본체보다 옅고 길다.
       굴절은 거의 없고 하이라이트/스펙큘러/블러 세기에 쓰라고 분리해 놨다.
  A    물방울 마스크 [0,1]. 물방울 본체(+흐른 자국에 남은 잔방울).

  무이음 아님(화면 한 장에 1:1로 덮는 용도). 물방울 52개, 반지름 3~26px.
  **화면 가장자리에 몰리고 중앙은 성기다** - 중앙이 가리면 조준/시야가 방해된다.
  가중치는 중심에서 멀수록 커지는 체비셰프(사각) 거리라 화면 네 변을 따라 고르게 깔린다.
  중력 방향은 **이미지 아래쪽(행 증가 방향)** 이다. Unity 기본 샘플링에서 PNG 위쪽 행이
  v=1이므로 화면에서도 그대로 아래가 아래다(뒤집을 필요 없음). 물방울은 아래로 흐르고
  젖은 자국은 **지나온 자리, 즉 방울보다 위쪽에** 남는다 - 실제 유리창 물방울이 그렇다
  (구슬이 아래 끝에 있고 꼬리가 위로 뻗는다). 자국 위에는 잔방울 몇 개가 남아 A에도 실린다.

--------------------------------------------------------------------------------
시드 고정
  아래 SEED_* 상수만이 유일한 난수원이다(np.random.default_rng). 두 번 실행하면
  바이트 단위로 같은 PNG 3장이 나온다(검증: md5 2회 대조).
"""

import os

import numpy as np
from PIL import Image, ImageFilter

RIPPLE_SIZE = 1024
STREAK_SIZE = 512
DROPLET_SIZE = 512

# --- rain_ripple -------------------------------------------------------------
SEED_RIPPLE_GRID = 52001    # 파문 중심 지터 / 반지름 / 링 수 / 위상
SEED_RIPPLE_WARP = 52003    # 파문 원형을 살짝 찌그러뜨리는 도메인 워프
SEED_RIPPLE_MICRO = 52011   # 파문 사이를 채우는 미세 물결(노멀에만 아주 약하게)
RIPPLE_CELLS = 4            # 4x4 지터 격자 = 파문 16개(요구 12~20)
RIPPLE_RADIUS = 0.135       # UV 단위 반지름 **상한**(배율 1.0 기준). 보통은 패킹이 먼저 건다.
RIPPLE_RADIUS_VAR = 0.40    # ±40% 크기 편차(배율 0.6~1.4, 최대/최소 2.33배)
RIPPLE_JITTER = 0.22        # 셀 크기 대비 중심 지터. 크면 격자 티가 덜 나지만 파문이 작아진다.
RIPPLE_PACK_SAFETY = 0.97   # 겹침 금지 여유(1.0이면 두 파문이 정확히 한 점에서 닿는다)

# --- rain_streak -------------------------------------------------------------
SEED_STREAK = 53001         # 가닥 위치/굵기/길이/밝기
SEED_STREAK_GRAIN = 53003   # 가닥 내부 세로 굴절 노이즈
STREAK_COUNT = 70

# --- rain_droplet ------------------------------------------------------------
SEED_DROP = 54001           # 물방울 위치/크기/찌그러짐
SEED_DROP_TAIL = 54003      # 꼬리 길이/잔방울
SEED_DROP_GRAIN = 54011     # 흐름 자국의 얼룩
DROP_COUNT = 52
DROP_TAIL_RATIO = 0.52      # 꼬리를 단 물방울 비율


# =============================================================================
# 공용 헬퍼 (surface_set.py / shore_foam.py와 같은 구현 - 툴 간 사본 정책)
# =============================================================================

def periodic_value_noise(size, cells, seed, octaves=4, persistence=0.55):
    """무이음 다옥타브 값 노이즈 [0,1]. cells = 최저 옥타브 격자 수."""
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
        out += (a * (1 - fx) * (1 - fy) + b * fx * (1 - fy)
                + c * (1 - fx) * fy + d * fx * fy) * amp
        total += amp
        amp *= persistence
    return out / total


def periodic_value_noise_xy(size, cells_x, cells_y, seed, octaves=3, persistence=0.55):
    """축마다 주파수가 다른 무이음 값 노이즈 [0,1](shore_foam.py와 같은 구현)."""
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
    """numpy용 smoothstep. edge0 > edge1이면 감소 함수가 된다(의도적으로 허용)."""
    t = np.clip((x - edge0) / (edge1 - edge0), 0.0, 1.0)
    return t * t * (3.0 - 2.0 * t)


def base_uv(size):
    """픽셀 중심 UV [0,1). u = 가로(열), v = 세로(행, 위에서 아래)."""
    lin = (np.arange(size) + 0.5) / size
    u = np.tile(lin[np.newaxis, :], (size, 1))
    v = np.tile(lin[:, np.newaxis], (1, size))
    return u, v


def wrap_delta(d):
    """토러스 최단 부호차 [-0.5, 0.5]."""
    return d - np.round(d)


def wrap_blur(field, radius, size):
    """랩어라운드 가우시안 블러(PIL은 가장자리를 클램프하므로 3x3 타일 후 중앙만)."""
    tiled = np.tile(np.clip(field, 0.0, 1.0), (3, 3))
    img = Image.fromarray((tiled * 255.0 + 0.5).astype(np.uint8), "L")
    img = img.filter(ImageFilter.GaussianBlur(radius))
    return np.asarray(img).astype(np.float64)[size:2 * size, size:2 * size] / 255.0


def height_to_normal_rg(h, wrap, slope_target=0.85, pct=99.0):
    """
    높이장 -> 접선공간 노멀 RG [0,1] (0.5 = 평평, OpenGL/Unity 규약).

    G 규약: Unity 노멀맵의 +G는 +V(위로 가는 UV) 방향이다. 이미지 행 번호는 아래로 갈수록
    커지므로 d/dV = -d/drow이고, 따라서 ny = -dh/dV = +dh/drow 가 된다. (R은 nx = -dh/dU.)
    기울기는 |grad|의 백분위수로 정규화한다 - 높이 진폭을 손으로 맞추지 않아도 노멀 분포가
    항상 0.5 중심의 적당한 폭으로 나온다(게임이 곱셈/가산으로 쓰므로 극단 쏠림 금지).
    """
    if wrap:
        dh_dcol = (np.roll(h, -1, axis=1) - np.roll(h, 1, axis=1)) * 0.5
        dh_drow = (np.roll(h, -1, axis=0) - np.roll(h, 1, axis=0)) * 0.5
    else:
        dh_drow, dh_dcol = np.gradient(h)
    mag = np.sqrt(dh_dcol ** 2 + dh_drow ** 2)
    ref = float(np.percentile(mag, pct))
    scale = slope_target / max(ref, 1e-12)
    nx = np.clip(-dh_dcol * scale, -4.0, 4.0)
    ny = np.clip(dh_drow * scale, -4.0, 4.0)
    inv = 1.0 / np.sqrt(nx * nx + ny * ny + 1.0)
    return nx * inv * 0.5 + 0.5, ny * inv * 0.5 + 0.5


def save_rgba(path, r, g, b, a):
    rgba = np.stack([r, g, b, a], axis=-1)
    img = Image.fromarray((np.clip(rgba, 0.0, 1.0) * 255.0 + 0.5).astype(np.uint8), "RGBA")
    img.save(path, optimize=True)
    print("저장:", os.path.basename(path), f"({img.size[0]}x{img.size[1]} RGBA)",
          f"{os.path.getsize(path) / 1024:.0f}KB")


# =============================================================================
# 1) rain_ripple.png
# =============================================================================

def ripple_layout():
    """
    파문 배치 - 4x4 지터 격자. 크기 편차 +-40%를 **그대로 지키면서** 겹침만 없앤다.

    겹치면 한 픽셀이 두 파문에 속해 B(위상)가 어느 한쪽으로 잘리고, 그 경계가 셰이더에서
    "링이 도중에 끊긴 자국"으로 보인다. 파문은 서로 다른 빗방울이 떨어진 자리라 애초에
    떨어져 있는 게 자연스럽기도 하다.

    개별 반지름을 이웃 거리에 맞춰 깎으면(가장 단순한 방법) 큰 파문만 잘려서 크기 편차가
    +-40% -> +-19%로 뭉개진다. 그래서 **배율 m_i(0.6~1.4)를 먼저 뽑고 전역 스케일 B 하나만**
    조인다: 모든 쌍에 대해 B*(m_i + m_j) <= d_ij 여야 하므로 B = min(d_ij / (m_i + m_j)).
    이러면 겹침 0이 보장되면서 배율 비(최대/최소 = 2.33배)는 정확히 보존되고, 그 조건 안에서
    파문이 가능한 한 크게 나온다(SAFETY로 여유만 조금 둔다).
    """
    rng = np.random.default_rng(SEED_RIPPLE_GRID)
    cell = 1.0 / RIPPLE_CELLS
    cx, cy, mul, rings = [], [], [], []
    for j in range(RIPPLE_CELLS):
        for i in range(RIPPLE_CELLS):
            jx, jy = rng.uniform(-RIPPLE_JITTER, RIPPLE_JITTER, 2)
            cx.append((i + 0.5 + jx) * cell)
            cy.append((j + 0.5 + jy) * cell)
            mul.append(rng.uniform(1.0 - RIPPLE_RADIUS_VAR, 1.0 + RIPPLE_RADIUS_VAR))
            rings.append(rng.uniform(3.0, 4.0))
    cx = np.array(cx)
    cy = np.array(cy)
    mul = np.array(mul)
    rings = np.array(rings)

    # 위상: 균등 분포를 셔플해 배정(무작위 추출보다 타이밍이 고르게 흩어진다).
    n = cx.size
    phase = (np.arange(n) + 0.5) / n
    rng.shuffle(phase)

    dx = wrap_delta(cx[:, None] - cx[None, :])
    dy = wrap_delta(cy[:, None] - cy[None, :])
    d = np.sqrt(dx * dx + dy * dy)
    msum = mul[:, None] + mul[None, :]
    np.fill_diagonal(d, np.inf)
    scale = float(np.min(d / msum)) * RIPPLE_PACK_SAFETY
    rad = mul * min(scale, RIPPLE_RADIUS)   # RIPPLE_RADIUS는 상한(너무 커지는 것만 막는다)
    return cx, cy, rad, rings, phase


def make_rain_ripple(out_dir):
    size = RIPPLE_SIZE
    u, v = base_uv(size)
    cx, cy, rad, rings, phase = ripple_layout()

    # 완전한 원은 CG 티가 난다. 주기적 노이즈로 UV를 아주 살짝 밀어 링을 찌그러뜨린다.
    wu = (periodic_value_noise(size, 6, SEED_RIPPLE_WARP, octaves=4) - 0.5) * 0.020
    wv = (periodic_value_noise(size, 6, SEED_RIPPLE_WARP + 977, octaves=4) - 0.5) * 0.020
    uw = u + wu
    vw = v + wv

    height = np.zeros((size, size))
    mask = np.zeros((size, size))
    best = np.full((size, size), -1.0)      # 지금까지 가장 "안쪽"인 파문의 점수
    phase_map = np.zeros((size, size))

    for i in range(cx.size):
        dx = wrap_delta(uw - cx[i])
        dy = wrap_delta(vw - cy[i])
        r = np.sqrt(dx * dx + dy * dy) / rad[i]     # 0 = 중심, 1 = 바깥 끝

        env = smoothstep(1.0, 0.0, r) ** 0.62       # 중심 1 -> 가장자리 0
        damp = np.exp(-0.95 * r)                    # 링이 멀어질수록 약해진다
        # 중심이 패이고(-cos(0) = -1) 링이 감쇠하며 퍼지는 빗방울 충돌 단면
        height += -np.cos(2.0 * np.pi * rings[i] * r) * damp * env

        m = env ** 1.15
        mask = np.maximum(mask, m)
        # 위상: "가장 안쪽" 파문의 값을 쓴다. 겹침이 없으므로 파문 영역 안에서는 상수,
        # 빈 공간은 가장 가까운 파문의 값으로 채워진다(A=0이라 화면에는 영향 없음).
        score = 1.0 - r
        take = score > best
        best = np.where(take, score, best)
        phase_map = np.where(take, phase[i], phase_map)

    # 파문 사이 빈 수면이 완전 평면이면 젖은 표면이 유리처럼 보인다. 아주 약한 잔물결을
    # 노멀에만 더한다(마스크에는 넣지 않는다 - 셰이더가 A로 파문 세기를 조절해야 하므로).
    micro = (periodic_value_noise(size, 24, SEED_RIPPLE_MICRO, octaves=3) - 0.5)
    height = height + micro * 0.07

    nr, ng = height_to_normal_rg(height, wrap=True, slope_target=1.15, pct=99.5)
    mask = wrap_blur(mask, 1.0, size)

    save_rgba(os.path.join(out_dir, "rain_ripple.png"), nr, ng, phase_map, mask)
    return dict(count=int(cx.size), radius_min=float(rad.min()), radius_max=float(rad.max()))


# =============================================================================
# 2) rain_streak.png
# =============================================================================

def make_rain_streak(out_dir):
    size = STREAK_SIZE
    u, v = base_uv(size)
    rng = np.random.default_rng(SEED_STREAK)

    # 물기둥 굴절: 세로로 빠르게, 가로로 느리게 변하는 노이즈. 세로 랩이 보장된다.
    grain = periodic_value_noise_xy(size, 6, 40, SEED_STREAK_GRAIN, octaves=3, persistence=0.6)
    grain2 = periodic_value_noise_xy(size, 12, 96, SEED_STREAK_GRAIN + 977, octaves=2)

    alpha = np.zeros((size, size))
    lum = np.zeros((size, size))    # 밝기(가닥 코어가 더 희다)

    for k in range(STREAK_COUNT):
        # x는 층화 표본(각 가닥에 1/N 구간을 배정하고 그 안에서 흔든다). 완전 무작위로 뽑으면
        # 몇 군데에 뭉치고 넓은 빈 띠가 생겨 "비"가 아니라 "커튼 몇 장"으로 보인다.
        sx = (k + rng.uniform(-0.45, 1.45)) / STREAK_COUNT
        sy = rng.random()
        # 굵기: 대부분 가늘고 몇 가닥만 굵다(제곱 분포). UV 단위.
        w = (0.5 + 3.6 * rng.random() ** 2.4) / size
        half_len = rng.uniform(0.10, 0.40)          # 길이 편차 4배
        bright = rng.uniform(0.30, 1.0) ** 1.2

        dx = np.abs(wrap_delta(u - sx)) / w
        dy = np.abs(wrap_delta(v - sy)) / half_len

        # 가로 단면: 부드러운 가우시안 + 더 좁고 밝은 코어(물기둥의 하이라이트)
        cross = np.exp(-dx * dx * 1.6)
        core = np.exp(-dx * dx * 9.0)
        # 세로: 가운데는 꽉 차고 양 끝 45% 구간에서 서서히 사라진다(뚝 끊기지 않게)
        length = smoothstep(1.0, 0.55, dy)

        a = cross * length * bright
        # 가닥 안쪽 밝기 변화(굴절). 완전히 0이 되지 않게 0.55 바닥을 둔다.
        a *= 0.55 + 0.45 * (0.65 * grain + 0.35 * grain2)

        alpha = 1.0 - (1.0 - alpha) * (1.0 - np.clip(a, 0.0, 1.0))
        lum = np.maximum(lum, core * length * bright)

    alpha = np.clip(alpha, 0.0, 1.0)
    # 아주 약한 블러로 계단(에일리어싱) 제거 - 세로 랩 유지를 위해 랩어라운드 블러.
    alpha = wrap_blur(alpha, 0.6, size)

    # 색: 흰색~아주 옅은 청백. 배경(알파 0)에도 같은 색을 깔아 컷아웃 검은 테두리를 막는다.
    tint = 0.86 + 0.14 * np.clip(lum + 0.35 * alpha, 0.0, 1.0)
    r = tint * 0.955
    g = tint * 0.980
    b = np.minimum(tint * 1.020, 1.0)

    save_rgba(os.path.join(out_dir, "rain_streak.png"), r, g, b, alpha)
    return dict(count=STREAK_COUNT)


# =============================================================================
# 3) rain_droplet.png
# =============================================================================

def droplet_layout():
    """
    물방울 배치 - 화면 가장자리에 몰리고 중앙은 성기게(기각 표본추출).

    가중치는 중심에서의 **체비셰프 거리**(= max(|x|,|y|))를 쓴다. 유클리드 반지름을 쓰면
    네 모서리에만 뭉치는데, 체비셰프는 네 변을 따라 고르게 퍼진다 - 화면 테두리를 두르는
    실제 렌즈 물방울 분포에 가깝다.
    """
    rng = np.random.default_rng(SEED_DROP)
    xs, ys, rs, sq = [], [], [], []
    tries = 0
    while len(xs) < DROP_COUNT and tries < 20000:
        tries += 1
        x, y = rng.random(2)
        edge = max(abs(x - 0.5), abs(y - 0.5)) * 2.0        # 0 = 중앙, 1 = 테두리
        w = 0.08 + 0.92 * smoothstep(0.15, 0.95, edge)      # 중앙은 8%만 통과
        if rng.random() > w:
            continue
        # 겹치는 물방울은 하나로 뭉쳐 보여 개수만 낭비된다 - 최소 간격을 둔다.
        rad = (3.0 + 23.0 * rng.random() ** 2.0) / DROPLET_SIZE
        ok = True
        for j in range(len(xs)):
            d = np.hypot(x - xs[j], y - ys[j])
            if d < (rad + rs[j]) * 0.85:
                ok = False
                break
        if not ok:
            continue
        xs.append(x)
        ys.append(y)
        rs.append(rad)
        sq.append(rng.uniform(0.82, 1.18))                  # 세로로 눌린/늘어난 정도
    return (np.array(xs), np.array(ys), np.array(rs), np.array(sq))


def make_rain_droplet(out_dir):
    size = DROPLET_SIZE
    u, v = base_uv(size)
    xs, ys, rs, sq = droplet_layout()
    rng = np.random.default_rng(SEED_DROP_TAIL)

    height = np.zeros((size, size))
    mask = np.zeros((size, size))
    flow = np.zeros((size, size))

    # 흐름 자국의 얼룩(균일한 띠는 물자국이 아니라 스티커로 보인다). 비무이음이라 그냥 노이즈.
    grain = periodic_value_noise(size, 10, SEED_DROP_GRAIN, octaves=4)

    for i in range(xs.size):
        dx = (u - xs[i]) / rs[i]
        dy = (v - ys[i]) / (rs[i] * sq[i])
        r2 = dx * dx + dy * dy
        inside = r2 < 1.0
        # 돔(렌즈): 가장자리에서 기울기가 0으로 수렴해 노멀이 튀지 않는다.
        dome = np.where(inside, np.clip(1.0 - r2, 0.0, 1.0) ** 1.2, 0.0)
        height += dome * rs[i] * size          # 큰 방울일수록 실제로 두껍다
        mask = np.maximum(mask, smoothstep(1.0, 0.80, np.sqrt(np.maximum(r2, 0.0))))

        if rng.random() >= DROP_TAIL_RATIO:
            continue

        # 꼬리: 방울 **위쪽**(지나온 자리)으로 남는 젖은 자국. 이미지 아래로 흐르는 방울이므로
        # 자국은 방울보다 위(행이 작은 쪽)에 있어야 물리적으로 맞다.
        length = rs[i] * rng.uniform(4.0, 11.0)
        t = np.clip((ys[i] - v) / length, 0.0, 1.0)          # 0 = 방울, 1 = 자국 끝
        width = rs[i] * (0.40 - 0.24 * t) * rng.uniform(0.8, 1.2)
        # 자국은 곧게 내려오지 않고 조금 비틀거린다.
        wob = (grain - 0.5) * rs[i] * 1.1
        lx = np.abs(u - xs[i] - wob * t) / np.maximum(width, 1e-6)
        trail = np.exp(-lx * lx * 1.8) * (1.0 - t) ** 0.7
        trail = np.where((t > 0.0) & (t < 1.0), trail, 0.0)
        trail *= 0.55 + 0.45 * grain
        flow = np.maximum(flow, np.clip(trail, 0.0, 1.0))

        # 자국에 남은 잔방울 몇 개(굵은 자국일수록 많이 남는다)
        for _ in range(int(rng.integers(2, 6))):
            tt = rng.uniform(0.12, 0.95)
            bx = xs[i] + rng.uniform(-0.6, 0.6) * rs[i]
            by = ys[i] - length * tt
            br = rs[i] * rng.uniform(0.16, 0.42)
            bdx = (u - bx) / br
            bdy = (v - by) / br
            br2 = bdx * bdx + bdy * bdy
            bdome = np.where(br2 < 1.0, np.clip(1.0 - br2, 0.0, 1.0) ** 1.2, 0.0)
            height += bdome * br * size
            mask = np.maximum(mask, smoothstep(1.0, 0.78, np.sqrt(np.maximum(br2, 0.0))))

    # 노멀: 돔 경사가 급해 백분위수를 낮게 잡으면 대부분이 0.5 근처로 눌린다.
    # 99.5%를 기준선으로 잡아 방울 안쪽이 골고루 기울게 한다.
    nr, ng = height_to_normal_rg(height, wrap=False, slope_target=0.95, pct=99.5)

    mask = np.clip(mask, 0.0, 1.0)
    # 흐름 자국이 방울 본체와 겹치는 부분은 본체가 이긴다(B는 "본체가 아닌 젖은 자국"이다).
    flow = np.clip(flow * (1.0 - mask * 0.85), 0.0, 1.0)

    save_rgba(os.path.join(out_dir, "rain_droplet.png"), nr, ng, flow, mask)
    return dict(count=int(xs.size),
                radius_px_min=float(rs.min() * size), radius_px_max=float(rs.max() * size))


def main():
    out_dir = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)),
                                           "..", "..", "Assets", "_Project", "Resources", "Textures"))
    os.makedirs(out_dir, exist_ok=True)
    a = make_rain_ripple(out_dir)
    b = make_rain_streak(out_dir)
    c = make_rain_droplet(out_dir)
    print(f"  파문 {a['count']}개 (반지름 UV {a['radius_min']:.3f}~{a['radius_max']:.3f})")
    print(f"  빗줄기 {b['count']}가닥")
    print(f"  물방울 {c['count']}개 (반지름 {c['radius_px_min']:.1f}~{c['radius_px_max']:.1f}px)")


if __name__ == "__main__":
    main()
