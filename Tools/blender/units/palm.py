#!/usr/bin/env python3
"""
야자수(코코야자 Cocos nucifera) 12종 - palm_a ~ palm_l.

    python3 Tools/blender/units/palm.py            # 12종 전부
    python3 Tools/blender/units/palm.py a c e      # 일부만

산출물 (이 파일들만 건드린다)
  Assets/_Project/Resources/Models/palm_a~l.obj  (+ 같은 이름의 .mtl - inject_usemtl 이 만든다)
  Tools/blender/_preview/_palm/palm_a~l.png      ← 저장소에 넣지 않는다(전용 폴더: 동시 실행 충돌 방지)

════════════════════════════════════════════════════════════════════════════════
1. 왜 다시 만들었나 (구 모델 진단 - 2026-08-17 판 palm_a~f 실측)

  구 모델은 "고사리/나무고사리"로 읽혔다. 렌더를 놓고 본 결함은 다음 6가지다.

  (a) 소엽이 **엽축과 같은 평면**에 누워 있었다. 실물 야자잎의 단면은 V자다 - 소엽이
      엽축에서 위로 솟았다가 바깥에서 아래로 꺾인다. 평면 소엽은 잎 전체가 한 장의
      납작한 깃털이 되어 야자로 안 읽힌다. (구 _flat_frond 는 y=0 평면에서만 만들었다.)
  (b) 소엽의 sweep 이 38~60°로 **너무 앞으로 누워** 있었다. 코코야자 소엽은 밑동 쪽에서
      엽축과 거의 직각(25~35°)이고 끝으로 가면서 눕는다. 전부 눕히면 고사리 깃털이다.
  (c) 잎이 **처지지 않았다**. 구 old_turn 최대 132°도 launch 가 2~16°라 잎 끝이 겨우
      수평 아래였다. 실물은 늙은 잎이 수평보다 **아래로 30~50° 내려가** 줄기에 붙어
      늘어진다. 이 나이 편차가 왕관의 실루엣을 만드는 유일한 요소다.
  (d) 줄기가 **위로 38% 가늘어졌다**(taper 0.62). 코코야자 줄기는 30~45cm 로 거의 일정하고
      밑동(bole)만 부푼다. 원뿔형 줄기는 소나무·판다누스지 야자가 아니다.
  (e) 잎흔(leaf scar)이 링 정점의 리지/그루브 교대뿐이라 **주름진 튜브**로 보였다.
      실물의 잎흔은 좁고 뚜렷한 홈이고 위로 갈수록 촘촘하다.
  (f) 코코넛 송이·잎자루 기부의 섬유질·죽은 갈색 잎이 전부 없었다. 왕관 기부가
      "잎이 막대에 꽂힌" 상태로 비어 있었다.

  구 모델 트라이: 1,004 ~ 2,024. 예산(2,500)의 절반을 남긴 채 위 요소를 포기했다.

2. 실물 레퍼런스 (이번에 조사한 수치 - 전부 텍스트 출처)

  - 전체 높이 20~30.5m / 수관 폭 6.7~10m / 줄기 지름 30~45cm(거의 일정) / 잎 4~6m
    (dimensions.com, Cocos nucifera)
  - 수관의 활엽 25~35장. 배열은 나선. 잎은 "수평 또는 아치" 자세.
    잎자루는 두껍고 섬유질이며 **밑동이 넓은 초(sheath)가 되어 줄기를 감싼다**.
    줄기의 고리는 나이테가 아니라 **잎흔**이다. (brainvoyage.blog 형태학)
  - 잎 1장에 소엽 200~250장, 소엽은 길이 50~150cm · 폭 1.5~5cm 의 선상피침형.
    성목은 연 12~18장의 잎을 내고 키는 연 30~50cm 자란다.
    → **잎흔 간격 = 30~50cm / 12~18장 ≒ 2.5~3.5cm**. 늙어 생장이 느려지면 더 촘촘해진다.
    (raskisimani "cocos nucifera" 자료집)
  - 열매는 지름 20~30cm, 무게 1.5~2.5kg. 성목은 연 75~200개를 맺고 송이는 **잎겨드랑이**,
    즉 왕관 기부의 잎자루 사이에 달려 바깥·아래로 늘어진다. (botanical-online)
  - 줄기는 바람·빛 때문에 기울어 자라는 개체가 흔하다(같은 출처의 "often tilted").

  이 수치를 게임 스케일로 옮길 때의 환산 규칙은 아래 3장에 적었다.

3. 게임 스케일과의 타협 (실측 근거 - C# 은 이번 배치에서 못 고친다)

  IslandMeshGenerator.MeshLibrary.cs:442
      PalmModelHeights = { 5.295, 6.789, 7.954, 3.291, 7.406, 6.521 }
  이 값은 **모델의 전체 바운딩 박스 높이**이고, Vegetation.cs:2005 가
      fit = 목표높이(4.6~7.6m) / PalmModelHeights[i]
  의 **균등 배율**로 쓴다. 즉 이 표와 모델이 어긋나면 나무가 통째로 크거나 작아진다.
  → **palm_a~f 의 전체 높이를 표의 값에 소수점 셋째 자리까지 그대로 맞춘다.**
    (_scale_to_height 가 마지막에 균등 배율로 정확히 맞춘다 - 비례는 유지된다.)
  palm_g~l 은 C# 이 아직 모르는 신규분이라 자유롭게 잡되, 선택 밴드
  (VariantSizeBand ±35%, 목표 4.6~7.6m)를 벗어나면 영원히 안 뽑히므로 10.5m 이하로 둔다.

  줄기 굵기: CreatePalm 의 baseRadius 0.266~0.388m(외접 반지름)가 **줄기 차단 캡슐**의
  반지름이다(Vegetation.cs:1983). 모델 줄기 밑동 반지름이 이 대역을 크게 벗어나면
  보이는 나무와 못 지나가는 벽이 어긋난다. 그래서 fit 배율을 곱한 뒤의 밑동 반지름이
  0.27~0.40m 에 들어오게 잡았다(각 변종 실측은 실행 로그에 찍는다).

  비례의 타협: 실물은 줄기 지름 0.375m 에 높이 25m(1:66)라 게임 5~10m 짜리에 그대로 쓰면
  이쑤시개가 된다. 이 프로젝트의 야자수는 1:9~1:14 의 "굵고 낮은" 비례를 유지하고,
  **실물에서 가져오는 것은 비례가 아니라 형태 규칙**이다(일정한 지름, 잎흔, V자 소엽,
  나이별 처짐, 코코넛 위치). 수관 폭은 실물비(수관/높이 = 0.3)를 쓰면 왕관이 초라해지므로
  0.72~0.98 × 높이로 잡았다 - 짧은 줄기에 성체 크기의 잎이 달린 어린 코코야자의 비례다.

4. 형태 규칙 (구현 지도)

  줄기 build_trunk
    · 지름 거의 일정: taper 는 꼭대기에서 0.88배까지만. 밑동은 exp 감쇠로 부푼 bole.
    · 잎흔: 마디마다 **링 2장**(바로 아래 리지 1.024 / 홈 0.958)을 좁은 간격으로 놓아
      톱니 단면을 만든다. 홈 띠만 flat 셰이딩이라 법선이 끊겨 **밝기 링**이 남는다 -
      기하 굴곡만으로는 20m 밖에서 한 픽셀도 안 남는다(mgbuild.swept_tube 주석과 같은 근거).
    · 간격은 위로 갈수록 좁다(t = u^0.82). 실물의 "생장 둔화 → 잎흔 조밀"과 같은 방향이다.
    · 나선 위상: 잎흔이 한쪽에서 더 튀어나와 수평 링이 사방 균일한 나사산으로 안 보인다.
    · 단면은 3·5차 로브로 살짝 비원형이다(프리미티브 원기둥과 실루엣을 가른다).

  잎 build_frond — **평면에서 만들고 나중에 휘지 않는다.**
    구 코드는 평면 잎을 만든 뒤 _bend_frond 로 휘었는데, 그러면 소엽이 엽축 평면을 벗어날
    수 없다(위 1-(a)). 이번에는 엽축 곡선의 **이동 프레임(T,B,N)** 위에 소엽을 직접 심는다.
    · 엽축: pitch(s) = launch - turn·s^1.35, yaw(s) = sway·s^1.6 (옆으로도 살짝 휜다).
    · 소엽: 기부 s0(0.17~0.22, 잎자루 구간)부터 s=0.99 까지 좌우로.
      - 길이 종모양: 중간이 가장 길고 밑동/끝이 짧다(실물 50~150cm 분포).
      - sweep(엽축과의 각) 27° → 63°: 밑동은 거의 직각, 끝으로 갈수록 눕는다.
      - rise(엽축 평면에서 위로 솟는 각) 46° → 12°: **이것이 V자 단면**이다.
      - hang(바깥 절반이 아래로 꺾이는 각): 소엽마다 다르고 끝으로 갈수록 크다.
      - 긴 소엽만 2마디(꺾임 있음), 짧은 소엽은 1마디. 트라이 예산을 실루엣에 몰아준다.
      - 폭은 이웃 간격의 62%. 나머지 38%가 하늘이 비치는 틈 = 우상복엽의 결정적 신호다.
    · UV 는 면을 만들 때 **손으로 적는다**(loop uv). 3D 에서 planar_uv 를 부르면 늘어진
      소엽이 위에서 눌려 무늬가 뭉갠다. u=폭방향 미터/타일, v=잎 밑동부터의 호길이/타일.

  왕관 build_crown
    · 나선 배치(황금각 137.5° + 지터). 나이를 index 순으로 주면 나이가 방위각에 고르게 퍼진다.
    · **나이 편차가 핵심.** age 0(어린 잎) launch ≈ +78°, age 1(늙은 잎) launch ≈ -20°.
      turn 도 어린 잎 48° → 늙은 잎 100°. 늙은 잎은 끝이 수평보다 40° 이상 아래로 내려간다.
    · 잎 밑동은 왕관 기부에서 반지름만큼 바깥·아래로 흩어 붙는다(잎자루가 겹쳐 나오는 모양).

  왕관 기부·열매 build_crown_base / build_nuts  — **갈색이라 trunk 그룹에 넣는다.**
    게임은 파일의 `o` 두 그룹에 각각 갈색(bark)/초록(frond)을 칠한다. 코코넛과 섬유질,
    죽은 잎은 실물에서 갈색이므로 **crown 이 아니라 trunk 오브젝트에 합친다** - 렌더러를
    늘리지 않고 색을 하나 더 얻는 유일한 방법이다.
    · 섬유질: 왕관 기부에서 아래로 늘어진 좁은 양면 플랩 8~12장(잎자루 초의 잔해).
    · 코코넛: 잎겨드랑이 높이에서 바깥·아래로 4~9개 뭉치. 반지름 0.11~0.15m(실물 20~30cm).
    · 죽은 잎: 늙은 변종만 1~2장. 소엽을 성기게 줄이고 줄기를 따라 늘어뜨린다.

5. 계약 (Docs/AssetPipeline.md)
  · 미터 / +Y up / +Z front / **접지 중심 원점** / OBJ + vn + vt / 머티리얼 미포함.
  · 파일 1개 = `o` 2개: `<name>_trunk`, `<name>_crown` **순서와 철자 고정**.
    ResourceVisualLibrary.TryLoadTwoPartModel 이 이 이름 규칙에 걸린다.
  · export_obj → verify_obj_file → **mg.inject_usemtl** 순서(호출 순서 계약).
  · 잎은 단면 + mgbuild.make_double_sided. **그 뒤에 clean_bmesh/remove_doubles 금지**
    (복제 정점이 녹아 뒷면이 통째로 사라진다 - mgbuild 주석).
  · 알파 컷아웃 없음. 실루엣은 전부 지오메트리다.
  · 시드 76001~76012 고정 = 같은 md5(2회 실행 대조).
════════════════════════════════════════════════════════════════════════════════
"""

import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

import mgbuild as mg  # noqa: E402  (bpy 를 먼저 끌어온다 - bmesh 가 그 뒤에야 import 된다)
import bpy  # noqa: E402
import bmesh  # noqa: E402
from mathutils import Matrix, Vector  # noqa: E402

# ── 예산 ──────────────────────────────────────────────────────────────────────
# 계약표의 "대형 구조물 8,000"이 아니라 **종당 5,000**이다. 근거: Vegetation.cs:296 의
# palmCount = ScaledCount(radius*0.60, 20, 80) - 섬 하나에 최대 **80그루**다.
# 80 × 5,000 = 400,000 삼각형이 상한이고, 실제로는 변종이 섞여 그 아래로 들어온다.
TRI_BUDGET = 5000
TRI_FLOOR = 1200

TRUNK_SIDES = 8            # 게임 폴백의 PalmTrunkSides 와 같은 급
BARK_TILE = 0.55           # bark.png 한 장이 줄기 세로 0.55m
BARK_WRAPS = 2.0
FROND_UV_TILE = 0.38       # frond.png 한 장이 잎 0.38m (소엽 폭·길이 양쪽에 같은 밀도)
NUT_UV_TILE = 0.30

PALM_BARK = (0.534, 0.368, 0.202)      # IslandMeshGenerator.PalmBarkColor
FROND_GREEN = (0.420, 0.659, 0.247)    # StructureVisualBuilder.FrondGreen

UV_LAYER = "UVMap"

_GOLDEN = 137.507764


# ══════════════════════════════════════════════════════════════════════════════
# 변종표
#
#   height  : **전체 바운딩 박스 높이(m)**. a~f 는 C# PalmModelHeights 와 동일해야 한다.
#   trunk_r : 밑동 외접 반지름(m). 균등 배율 후의 실측은 실행 로그에 찍는다.
#   lean    : 꼭대기의 수평 이동(m, +Z). 클수록 크게 휜다.
#   curve   : 휨의 지수. 낮으면 밑동부터 휘고(굽은 노목), 높으면 위쪽만 휜다.
#   sway    : 2차 S자 굴곡의 진폭(m). 바람에 시달린 개체.
#   fronds  : 활엽 수(실물 25~35 → 폴리 예산상 7~15).
#   frond_l : 잎 길이(m).
#   leaflets: 한쪽 소엽 수(양쪽 = 2배). 실물 100~125/쪽 → 15~24/쪽.
#   nuts    : 코코넛 총 개수(0 이면 열매 없는 개체).
#   dead    : 줄기에 붙어 늘어진 갈색 죽은 잎 수.
#   tear    : 소엽 결손률(열대폭풍 흔적). 0 이면 온전.
#   age0/1  : 왕관의 나이 분포 하한/상한(0=어린 잎만, 1=늙은 잎까지).
# ══════════════════════════════════════════════════════════════════════════════
VARIANTS = [
    dict(tfrac=0.88, name="palm_a", seed=76001, height=5.295, trunk_r=0.242, lean=0.34, curve=2.1,
         sway=0.05, fronds=12, frond_l=3.00, leaflets=21, nuts=0, dead=0, tear=0.0,
         age0=0.05, age1=0.92, note="곧은 장년목 / 열매 없음"),
    dict(tfrac=0.88, name="palm_b", seed=76002, height=6.789, trunk_r=0.258, lean=0.68, curve=2.0,
         sway=0.09, fronds=13, frond_l=3.60, leaflets=20, nuts=6, dead=0, tear=0.0,
         age0=0.02, age1=0.95, note="완만히 휜 성목 / 코코넛 1송이"),
    dict(tfrac=0.90, name="palm_c", seed=76003, height=7.954, trunk_r=0.282, lean=1.15, curve=1.9,
         sway=0.12, fronds=14, frond_l=4.15, leaflets=19, nuts=9, dead=1, tear=0.0,
         age0=0.00, age1=1.00, note="큰 성목 / 코코넛 2송이 / 죽은 잎 1"),
    dict(tfrac=0.62, name="palm_d", seed=76004, height=3.291, trunk_r=0.196, lean=0.10, curve=2.6,
         sway=0.03, fronds=9, frond_l=2.10, leaflets=18, nuts=0, dead=0, tear=0.0,
         age0=0.00, age1=0.72, note="어린 나무 / 줄기 짧고 잎이 곧추선다"),
    dict(tfrac=0.90, name="palm_e", seed=76005, height=7.406, trunk_r=0.232, lean=1.05, curve=1.5,
         sway=0.22, fronds=9, frond_l=3.95, leaflets=20, nuts=4, dead=2, tear=0.06,
         age0=0.35, age1=1.00, note="노목 / 잎 적고 처짐 심함 / 죽은 잎 2"),
    dict(tfrac=0.86, name="palm_f", seed=76006, height=6.521, trunk_r=0.244, lean=1.70, curve=1.4,
         sway=0.30, fronds=11, frond_l=3.45, leaflets=21, nuts=0, dead=1, tear=0.0,
         age0=0.10, age1=0.98, wind=0.60,
         note="바람에 한쪽으로 쏠린 개체 / 왕관이 풍하로 밀린다"),

    dict(tfrac=0.68, name="palm_g", seed=76007, height=4.320, trunk_r=0.226, lean=0.80, curve=1.3,
         sway=0.06, fronds=10, frond_l=2.75, leaflets=20, nuts=3, dead=0, tear=0.0,
         age0=0.00, age1=0.80, note="어린 해안목 / 밑동부터 낮게 기운다"),
    dict(tfrac=0.84, name="palm_h", seed=76008, height=4.960, trunk_r=0.234, lean=0.48, curve=2.0,
         sway=0.14, fronds=11, frond_l=3.05, leaflets=22, nuts=0, dead=1, tear=0.30,
         age0=0.15, age1=1.00, note="폭풍 피해목 / 소엽 30% 결손"),
    dict(tfrac=0.80, name="palm_i", seed=76009, height=5.850, trunk_r=0.202, lean=0.38, curve=2.0,
         sway=0.08, fronds=8, frond_l=2.85, leaflets=19, nuts=2, dead=0, tear=0.0,
         age0=0.05, age1=0.90,
         twin=dict(h=0.70, r=0.80, lean=-0.58, fronds=7, frond_l=2.40, yaw=196.0),
         note="쌍둥이 줄기 / 두 왕관"),
    dict(tfrac=0.95, name="palm_j", seed=76010, height=8.600, trunk_r=0.264, lean=0.32, curve=2.4,
         sway=0.05, fronds=12, frond_l=3.85, leaflets=21, nuts=12, dead=0, tear=0.0,
         age0=0.05, age1=0.95, note="곧고 큰 성목 / 코코넛 다량(3송이)"),
    dict(tfrac=0.92, name="palm_k", seed=76011, height=9.500, trunk_r=0.282, lean=2.35, curve=1.55,
         sway=0.62, fronds=12, frond_l=4.35, leaflets=20, nuts=6, dead=1, tear=0.0,
         age0=0.00, age1=1.00, note="엽서형 C커브 / 크게 휜 큰 나무"),
    dict(tfrac=0.95, name="palm_l", seed=76012, height=10.400, trunk_r=0.318, lean=1.25, curve=1.8,
         sway=0.26, fronds=10, frond_l=4.50, leaflets=21, nuts=5, dead=2, tear=0.10,
         age0=0.42, age1=1.00, note="고목 / 굵은 bole, 성긴 왕관, 심한 처짐"),
]


# ══════════════════════════════════════════════════════════════════════════════
# 공용 - UV 를 손으로 적는 면 생성
# ══════════════════════════════════════════════════════════════════════════════
def _uv_layer(bm):
    return bm.loops.layers.uv.get(UV_LAYER) or bm.loops.layers.uv.new(UV_LAYER)


def _quad(bm, uvl, p0, p1, p2, p3, uv0, uv1, uv2, uv3, smooth=False):
    """UV 를 명시한 사각형 하나. 비평면이어도 된다 - 삼각화하면 대각선을 따라 접힌 소엽이 된다.

    소엽에 일부러 비평면 쿼드를 쓴다: 평면 판 하나짜리 소엽은 어느 각도에서 보나 밝기가
    같아 종이처럼 보이는데, 접힌 쿼드는 두 삼각형의 법선이 갈려 **접힌 잎**으로 읽힌다.
    삼각형 1장을 더 쓰지 않고 얻는 이득이라 예산 대비 효율이 가장 높은 장치다.
    """
    verts = [bm.verts.new(p) for p in (p0, p1, p2, p3)]
    f = bm.faces.new(verts)
    f.smooth = smooth
    for loop, uvc in zip(f.loops, (uv0, uv1, uv2, uv3)):
        loop[uvl].uv = uvc
    return f


# ══════════════════════════════════════════════════════════════════════════════
# 줄기
# ══════════════════════════════════════════════════════════════════════════════
def _spine(t, height, lean, curve, sway, phase, wind=0.0):
    """t(0~1) -> 줄기 중심선(월드). y 는 정확히 height*t.

    z : lean·t^curve        기본 휨. curve 가 낮으면 밑동부터, 높으면 위쪽만 휜다.
      + sway·sin(pi·t)      2차 S 굴곡(가운데가 가장 크게 밀린다). 바람에 시달린 개체.
      + wind·t^3            풍하 쪽으로 꼭대기를 더 밀어낸다(palm_f).
    x : 아주 작은 흔들림. 완전한 평면 곡선은 인공물로 읽힌다.
    """
    return Vector((
        height * 0.016 * math.sin(2.4 * t + phase) * t,
        height * t,
        lean * (t ** curve) + sway * math.sin(math.pi * t) + wind * (t ** 3.0),
    ))


def _spine_tangent(t, *args, **kw):
    d = 1e-3
    a = _spine(max(0.0, t - d), *args, **kw)
    b = _spine(min(1.0, t + d), *args, **kw)
    return (b - a).normalized()


def build_trunk(rng, height, base_r, lean, curve, sway, wind=0.0, nodes=None):
    """잎흔 고리가 있는 스윕 튜브 하나. (오브젝트, 꼭대기 위치, 꼭대기 접선, 꼭대기 반지름)."""
    phase = rng.uniform(0.0, math.tau)
    spiral = rng.uniform(2.2, 4.0)
    sp = (height, lean, curve, sway, phase, wind)

    # 잎흔 마디 수: 실물 간격 2.5~3.5cm 를 그대로 쓰면 6m 줄기에 200개다(폴리 폭발).
    # 눈에 읽히는 최소 개수만 남긴다 - 위로 갈수록 촘촘해지는 **경향**이 사실감의 본체다.
    if nodes is None:
        nodes = int(max(12, min(21, round(height * 2.4))))

    flare_top = min(0.26, height * 0.052)     # bole(밑동 팽대) 구간
    t_flare = flare_top / height
    ts, kinds = [], []                        # kinds: 0=밑동, 1=리지, 2=잎흔 홈
    for k, f in enumerate((0.0, 0.16, 0.40, 0.70, 1.0)):
        ts.append(t_flare * f)
        kinds.append(0)
    for k in range(nodes):
        u = (k + 1) / nodes
        t = t_flare + (1.0 - t_flare) * (u ** 0.82)     # 위로 갈수록 간격이 좁아진다
        gap = (1.0 - t_flare) / nodes
        ts.append(t - gap * 0.30)
        kinds.append(1)                       # 잎흔 바로 아래의 두툼한 리지
        ts.append(t)
        kinds.append(2)                       # 잎흔 홈

    rings, smooth_bands = [], []
    for idx, (t, kind) in enumerate(zip(ts, kinds)):
        center = _spine(t, *sp)
        # 지름 거의 일정: 꼭대기에서 0.88배까지만 준다(실물 30~45cm 의 "거의 일정"에 대응).
        taper = 1.0 - 0.12 * t
        y = height * t
        flare = (1.0 + 0.34 * math.exp(-y / 0.16)          # 뿌리 보스
                 + 0.15 * math.exp(-y / (height * 0.15)))  # bole 의 완만한 부풀음
        node = {0: 1.0, 1: 1.024, 2: 0.958}[kind]
        radii = []
        for i in range(TRUNK_SIDES):
            a = math.tau * i / TRUNK_SIDES
            # 나선 위상: 잎흔이 한쪽에서 더 튀어나온다(수평 링이 사방 균일하면 나사산이 된다).
            twist = 1.0 + (node - 1.0) * 0.75 * math.cos(a - spiral * t * math.pi)
            lobe = (1.0 + 0.024 * math.cos(3.0 * a + 2.1 * t)
                    + 0.014 * math.cos(5.0 * a - 1.4 * t))
            radii.append(base_r * taper * flare * node * twist * lobe)
        rings.append((center, radii))

    # 잎흔 홈으로 들어가는 띠만 flat: 법선이 끊겨 멀리서도 **밝기 링**이 남는다.
    for k in range(1, len(kinds)):
        smooth_bands.append(kinds[k] != 2)

    bm = bmesh.new()
    mg.swept_tube(bm, rings, sides=TRUNK_SIDES, cap_bottom=True, cap_top=True,
                  smooth=smooth_bands)
    bmesh.ops.triangulate(bm, faces=bm.faces[:])
    obj = mg.new_object("trunk", bm)
    mg.cylinder_uv(obj, tile=BARK_TILE, wraps=BARK_WRAPS)

    top = _spine(1.0, *sp)
    tan = _spine_tangent(0.995, *sp)
    top_r = sum(rings[-1][1]) / len(rings[-1][1])
    return obj, top, tan, top_r


# ══════════════════════════════════════════════════════════════════════════════
# 잎 (우상복엽)
# ══════════════════════════════════════════════════════════════════════════════
def _rachis_frames(length, launch_deg, turn_deg, sway_deg, samples=48):
    """엽축을 따라가는 이동 프레임 [(pos, T, B, N)] 을 s=0..1 로 균등 샘플해 돌려준다.

    pitch(s) = launch - turn·s^1.35   밑동에서 솟았다가 끝으로 갈수록 아래로(현수선 느낌).
    yaw(s)   = sway·s^1.6             옆으로도 살짝 휜다(완전한 평면 잎은 인공물이다).
    B(좌우) 는 항상 수평 성분만 갖는다 - 소엽이 좌우로 벌어지는 기준선이다.
    N = T×B 가 잎면의 위쪽. 소엽을 N 방향으로 들어 올리면 그것이 V자 단면이 된다.
    """
    frames = []
    p = Vector((0.0, 0.0, 0.0))
    ds = 1.0 / samples
    for i in range(samples + 1):
        s = i * ds
        pitch = math.radians(launch_deg - turn_deg * (s ** 1.35))
        yaw = math.radians(sway_deg * (s ** 1.6))
        t = Vector((math.sin(yaw) * math.cos(pitch), math.sin(pitch),
                    math.cos(yaw) * math.cos(pitch)))
        b = Vector((math.cos(yaw), 0.0, -math.sin(yaw)))
        n = t.cross(b)
        frames.append((p.copy(), t, b, n))
        p = p + t * (length * ds)
    return frames


def _frame_at(frames, s):
    f = max(0.0, min(1.0 - 1e-6, s)) * (len(frames) - 1)
    i = int(f)
    w = f - i
    p0, t0, b0, n0 = frames[i]
    p1, t1, b1, n1 = frames[i + 1]
    return (p0.lerp(p1, w), t0.lerp(t1, w).normalized(),
            b0.lerp(b1, w).normalized(), n0.lerp(n1, w).normalized())


def build_frond(bm, uvl, rng, length, launch_deg, turn_deg, sway_deg, per_side,
                base_offset, tear=0.0, width_scale=1.0, dead=False):
    """잎 1장을 bm 에 직접 심는다. 로컬 기준 - 밑동 원점, 진행 +Z, 위 +Y.

    구 버전과의 결정적 차이: **평면에서 만들어 나중에 휘지 않는다.** 소엽을 엽축 프레임
    (T,B,N) 위에서 바로 3D 로 심으므로 소엽이 엽축 평면 밖으로 솟을 수 있다(V자 단면).
    """
    frames = _rachis_frames(length, launch_deg, turn_deg, sway_deg)
    s_leaf0 = rng.uniform(0.16, 0.21)          # 잎자루(소엽 없는 구간)
    s_leaf1 = 0.985
    inv = 1.0 / FROND_UV_TILE

    # ── 엽축(잎자루+중륵) ─────────────────────────────────────────────────────
    stations = 7
    prev = None
    for k in range(stations + 1):
        s = k / stations
        pos, t, b, n = _frame_at(frames, s)
        # 잎자루 기부는 두껍고(0.070m) 끝은 실처럼 가늘다. 실물 "두껍고 섬유질인 잎자루".
        w = (0.070 * (1.0 - s) ** 0.55 + 0.007) * width_scale
        arc = s * length
        cur = (pos + base_offset, b * w, arc)
        if prev is not None:
            p0, e0, a0 = prev
            p1, e1, a1 = cur
            # 중륵도 살짝 V 로 접는다(위로 볼록). 잎 전체 단면의 V 를 밑에서 받쳐 준다.
            _quad(bm, uvl,
                  p0 - e0, p0 + e0, p1 + e1, p1 - e1,
                  (0.5 - e0.length * inv, a0 * inv), (0.5 + e0.length * inv, a0 * inv),
                  (0.5 + e1.length * inv, a1 * inv), (0.5 - e1.length * inv, a1 * inv))
        prev = cur

    # ── 소엽 ──────────────────────────────────────────────────────────────────
    span = s_leaf1 - s_leaf0
    spacing = span * length / max(1, per_side)
    half_w = spacing * 0.31 * width_scale       # 이웃 간격의 62%를 채운다 = 틈 38%
    # 실물 소엽 길이 50~150cm 를 잎 길이 4~6m 로 나누면 최대 0.30·L 정도다.
    leaf_max = length * (0.29 if not dead else 0.24)

    for k in range(per_side):
        u = (k + 0.5) / per_side
        s = s_leaf0 + span * u
        pos, t, b, n = _frame_at(frames, s)
        # 길이 분포. 2차 렌더에서 sin(pi·u^0.80)^0.42 는 **끝까지 길이가 안 줄어** 잎 끝이
        # 뭉툭한 빗자루로 끝났다. 실물 코코야자 잎은 밑동에서 이미 길고(레퍼런스 50cm~),
        # 1/3 지점이 최장(150cm)이며 **끝은 뾰족하게 수렴**한다. 지수를 바꿔 그 프로파일로.
        bell = (math.sin(math.pi * (u ** 0.62)) ** 0.5) * min(1.0, 0.55 + 3.0 * u)
        # 밑동 쪽 소엽은 엽축과 거의 직각, 끝으로 갈수록 눕는다(구 버전의 38~60°가 너무 누웠다).
        sweep = math.radians(27.0 + 36.0 * (u ** 1.15))
        # V자 단면: 밑동 46° -> 끝 12°. 이 각이 0 이면 잎이 납작한 깃털이 된다.
        # 2차 렌더의 34°는 얕아서 잎이 여전히 평면 부채로 보였다 - 실물 잎 단면의 V 는 깊다.
        rise = math.radians((46.0 - 34.0 * u) * (0.55 if dead else 1.0))

        for side in (-1.0, 1.0):
            if tear > 0.0 and rng.uniform(0.0, 1.0) < tear:
                continue                        # 폭풍으로 뜯긴 소엽
            ln = leaf_max * bell * rng.uniform(0.80, 1.14)
            if tear > 0.0 and rng.uniform(0.0, 1.0) < tear * 0.7:
                ln *= rng.uniform(0.30, 0.62)   # 찢겨 짧아진 소엽
            if ln < 0.045:
                continue

            sw = sweep * rng.uniform(0.88, 1.12)
            ri = rise * rng.uniform(0.72, 1.28)
            # 처짐: 바깥 절반이 아래로 꺾이는 각. 끝쪽 소엽일수록, 늙은 잎일수록 크다.
            hang = math.radians(rng.uniform(24.0, 52.0) + 34.0 * u)

            plane = (b * side * math.cos(sw) + t * math.sin(sw)).normalized()
            d0 = (plane * math.cos(ri) + n * math.sin(ri)).normalized()
            d1 = (plane * math.cos(ri - hang) + n * math.sin(ri - hang)).normalized()

            # 리본의 폭 방향은 엽축(T) 방향이다. 소엽마다 조금씩 비틀어 같은 각도로 안 서게 한다.
            roll = Matrix.Rotation(rng.uniform(-0.42, 0.42), 4, d0)
            e = (roll @ t) * half_w * rng.uniform(0.82, 1.10)

            p_base = pos + b * side * (0.045 * width_scale) + base_offset
            # 2마디(꺾임 있는) 소엽은 **잎 중간 구간에만** 준다. 꺾임이 실루엣에 기여하는
            # 곳이 거기뿐이고(밑동·끝의 짧은 소엽은 꺾여도 1px 차이도 안 난다), 이 한 줄이
            # 잎당 삼각형을 330 -> 275 로 내려 잎 14장짜리(palm_c)를 예산 안에 넣는다.
            seg2 = 0.22 < u < 0.74 and ln > leaf_max * 0.50
            if seg2:
                l0 = ln * rng.uniform(0.42, 0.55)
                p_mid = p_base + d0 * l0
                p_tip = p_mid + d1 * (ln - l0)
                e_mid = e * 0.88
                e_tip = e * 0.16
                a0, a1, a2 = 0.0, l0 * inv, ln * inv
                _quad(bm, uvl,
                      p_base - e, p_base + e, p_mid + e_mid, p_mid - e_mid,
                      (0.5 - half_w * inv, a0), (0.5 + half_w * inv, a0),
                      (0.5 + half_w * inv, a1), (0.5 - half_w * inv, a1))
                _quad(bm, uvl,
                      p_mid - e_mid, p_mid + e_mid, p_tip + e_tip, p_tip - e_tip,
                      (0.5 - half_w * inv, a1), (0.5 + half_w * inv, a1),
                      (0.5 + half_w * inv, a2), (0.5 - half_w * inv, a2))
            else:
                # 1마디 소엽도 끝을 d1 로 살짝 꺾어 둔다(비평면 쿼드 = 접힌 잎).
                p_tip = p_base + (d0 * 0.55 + d1 * 0.45).normalized() * ln
                e_tip = e * 0.18
                _quad(bm, uvl,
                      p_base - e, p_base + e, p_tip + e_tip, p_tip - e_tip,
                      (0.5 - half_w * inv, 0.0), (0.5 + half_w * inv, 0.0),
                      (0.5 + half_w * inv, ln * inv), (0.5 - half_w * inv, ln * inv))
    return bm


def build_crown(rng, top, tangent, spec, r_top, fronds, frond_l, per_side,
                age0, age1, tear=0.0, yaw0=0.0):
    """활엽 여러 장을 메시 **한 장**으로 합친다(렌더러 1개).

    나선 배치 + **나이 편차**가 이 함수의 전부다. 나이 index 를 황금각 배치에 그대로
    태우면 어린 잎/늙은 잎이 방위각에 고르게 섞여, 어느 각도에서 봐도 위로 선 잎과
    늘어진 잎이 같이 보인다(실물 왕관의 인상).
    """
    align = Vector((0.0, 1.0, 0.0)).rotation_difference(tangent).to_matrix().to_4x4()
    bm = bmesh.new()
    uvl = _uv_layer(bm)

    for i in range(fronds):
        age = age0 + (age1 - age0) * (i / max(1, fronds - 1))
        age = min(1.0, max(0.0, age + rng.uniform(-0.06, 0.06)))
        yaw = yaw0 + _GOLDEN * i + rng.uniform(-11.0, 11.0)

        # 어린 잎: 거의 곧추서서 위로. 늙은 잎: 수평보다 아래에서 출발해 축 늘어진다.
        launch = 78.0 - 98.0 * (age ** 1.08) + rng.uniform(-5.0, 5.0)
        turn = 48.0 + 54.0 * age + rng.uniform(-6.0, 6.0)
        sway = rng.uniform(-16.0, 16.0)
        ln = frond_l * (0.80 + 0.26 * age) * rng.uniform(0.94, 1.06)

        # 잎 밑동은 왕관 기부에서 바깥·아래로 흩어진다(잎자루가 겹쳐 나오는 모양).
        drop = frond_l * 0.035 + r_top * (0.25 + 1.9 * age) + rng.uniform(0.0, 0.05)
        out = r_top * rng.uniform(0.55, 1.25)
        base_offset = Vector((0.0, -drop, out))

        sub = bmesh.new()
        subuv = _uv_layer(sub)
        build_frond(sub, subuv, rng.sub(100 + i), ln, launch, turn, sway, per_side,
                    base_offset, tear=tear)
        bmesh.ops.triangulate(sub, faces=sub.faces[:])
        # 잎은 단면 메시다. 뒷면이 없으면 백페이스 컬링에 통째로 사라진다.
        # ★ 이 뒤로 remove_doubles(clean_bmesh)를 부르면 복제 정점이 녹아 뒷면이 사라진다.
        mg.make_double_sided(sub)
        tmp = mg.new_object(f"_f{i}", sub)
        mg.shade_flat(tmp)
        tmp.matrix_world = (Matrix.Translation(top) @ align
                            @ Matrix.Rotation(math.radians(yaw), 4, "Y"))
        mesh = tmp.data.copy()
        mesh.transform(tmp.matrix_world)
        bm.from_mesh(mesh)
        bpy.data.meshes.remove(mesh, do_unlink=True)
        bpy.data.objects.remove(tmp, do_unlink=True)

    obj = mg.new_object("crown", bm)
    mg.shade_flat(obj)
    return obj


# ══════════════════════════════════════════════════════════════════════════════
# 왕관 기부의 갈색 부속 (섬유질 / 코코넛 / 죽은 잎) - 전부 trunk 그룹으로 들어간다
# ══════════════════════════════════════════════════════════════════════════════
def build_crown_base(rng, top, tangent, r_top, count=10):
    """잎자루 초(sheath)의 잔해 - 왕관 기부에서 아래로 늘어진 좁은 플랩.

    실물 코코야자는 잎이 떨어진 자리에 넓은 섬유질 초가 한동안 남아 왕관 기부가 지저분하다.
    이게 없으면 "막대에 잎을 꽂은" 인상이 된다.
    """
    align = Vector((0.0, 1.0, 0.0)).rotation_difference(tangent).to_matrix().to_4x4()
    bm = bmesh.new()
    uvl = _uv_layer(bm)
    inv = 1.0 / FROND_UV_TILE
    for i in range(count):
        a = math.tau * i / count + rng.uniform(-0.16, 0.16)
        drop = r_top * rng.uniform(1.6, 3.6)
        out = rng.uniform(0.95, 1.35)
        w0 = r_top * rng.uniform(0.34, 0.58)
        w1 = w0 * rng.uniform(0.25, 0.55)
        d = Vector((math.sin(a), 0.0, math.cos(a)))
        side = Vector((math.cos(a), 0.0, -math.sin(a)))
        p0 = d * (r_top * 0.92) + Vector((0.0, rng.uniform(-0.02, 0.06), 0.0))
        p1 = d * (r_top * out) - Vector((0.0, drop, 0.0))
        _quad(bm, uvl,
              p0 - side * w0, p0 + side * w0, p1 + side * w1, p1 - side * w1,
              (0.0, 0.0), (w0 * 2 * inv, 0.0), (w0 * 2 * inv, drop * inv), (0.0, drop * inv))
    bmesh.ops.triangulate(bm, faces=bm.faces[:])
    mg.make_double_sided(bm)               # 좁은 플랩이라 뒤에서도 보여야 한다
    obj = mg.new_object("crownbase", bm)
    mg.shade_flat(obj)
    obj.matrix_world = Matrix.Translation(top) @ align
    return obj


def build_nuts(rng, top, tangent, r_top, total):
    """코코넛 송이. 실물은 **잎겨드랑이**에 달려 바깥·아래로 늘어진다.

    반지름 0.11~0.15m = 실물 지름 22~30cm. 송이당 3~6개, 총 개수에 맞춰 송이를 나눈다.
    갈색이라 trunk 오브젝트에 합친다(crown 에 넣으면 초록 코코넛이 된다).
    """
    align = Vector((0.0, 1.0, 0.0)).rotation_difference(tangent).to_matrix().to_4x4()
    parts = []
    remaining = total
    idx = 0
    while remaining > 0:
        n = min(remaining, rng.randint(3, 6))
        remaining -= n
        a = rng.uniform(0.0, math.tau)
        d = Vector((math.sin(a), 0.0, math.cos(a)))
        # 1차 렌더에서 송이가 왕관 안쪽에 묻혀 한 알도 안 보였다. 실물도 송이는 잎겨드랑이에서
        # **아래·바깥으로 늘어져** 잎보다 낮은 위치에 온다 - 그만큼 내리고 밀어낸다.
        hub = (d * (r_top * rng.uniform(1.30, 2.10))
               - Vector((0.0, r_top * rng.uniform(2.2, 3.6), 0.0)))
        for j in range(n):
            rad = rng.uniform(0.115, 0.158)
            bm = bmesh.new()
            bmesh.ops.create_uvsphere(bm, u_segments=6, v_segments=3,
                                      radius=1.0, matrix=Matrix.Identity(4))
            # 코코넛은 완전한 구가 아니라 세로로 살짝 긴 삼릉형이다.
            sc = Matrix.Diagonal((rad * 0.93, rad * 1.10, rad * 0.93, 1.0))
            bmesh.ops.transform(bm, matrix=sc, verts=bm.verts[:])
            off = Vector((rng.uniform(-1.0, 1.0), rng.uniform(-1.0, 0.35),
                          rng.uniform(-1.0, 1.0)))
            off = off * (rad * 1.25)
            bmesh.ops.translate(bm, vec=hub + off, verts=bm.verts[:])
            bmesh.ops.triangulate(bm, faces=bm.faces[:])
            o = mg.new_object(f"_nut{idx}", bm)
            mg.shade_flat(o)
            mg.box_uv(o, tile=NUT_UV_TILE)
            parts.append(o)
            idx += 1
    if not parts:
        return None
    obj = mg.join_objects(parts, name="nuts")
    obj.matrix_world = Matrix.Translation(top) @ align
    return obj


def build_dead_fronds(rng, top, tangent, r_top, count, frond_l, per_side):
    """줄기를 따라 늘어진 갈색 죽은 잎. 노목의 인상을 만드는 가장 싼 장치다."""
    align = Vector((0.0, 1.0, 0.0)).rotation_difference(tangent).to_matrix().to_4x4()
    bm = bmesh.new()
    uvl = _uv_layer(bm)
    for i in range(count):
        yaw = rng.uniform(0.0, 360.0)
        launch = rng.uniform(-52.0, -22.0)      # 처음부터 아래를 본다
        turn = rng.uniform(46.0, 74.0)          # 그대로 줄기에 붙어 늘어진다
        ln = frond_l * rng.uniform(0.62, 0.86)
        sub = bmesh.new()
        subuv = _uv_layer(sub)
        build_frond(sub, subuv, rng.sub(300 + i), ln, launch, turn,
                    rng.uniform(-24.0, 24.0), max(8, per_side - 6),
                    Vector((0.0, -r_top * 1.9, r_top * 0.75)),
                    tear=0.22, width_scale=0.85, dead=True)
        bmesh.ops.triangulate(sub, faces=sub.faces[:])
        mg.make_double_sided(sub)
        tmp = mg.new_object(f"_d{i}", sub)
        mg.shade_flat(tmp)
        tmp.matrix_world = Matrix.Rotation(math.radians(yaw), 4, "Y")
        mesh = tmp.data.copy()
        mesh.transform(tmp.matrix_world)
        bm.from_mesh(mesh)
        bpy.data.meshes.remove(mesh, do_unlink=True)
        bpy.data.objects.remove(tmp, do_unlink=True)
    obj = mg.new_object("dead", bm)
    mg.shade_flat(obj)
    obj.matrix_world = Matrix.Translation(top) @ align
    return obj


# ══════════════════════════════════════════════════════════════════════════════
# 조립
# ══════════════════════════════════════════════════════════════════════════════
def _stem(rng, spec, height, base_r, lean, curve, sway, wind, fronds, frond_l,
          per_side, age0, age1, tear, dead_n, nuts_n, yaw0=0.0, x_shift=0.0):
    """줄기 1개 + 그 위의 왕관/열매/섬유질을 만들어 (갈색 파츠 리스트, 초록 파츠) 로 돌려준다."""
    trunk, top, tan, r_top = build_trunk(rng.sub(1), height, base_r, lean, curve, sway, wind)
    # 줄기 튜브만의 중간 굵기(합치기 전에 잰다 - 죽은 잎·코코넛이 섞이면 값이 오염된다).
    _mid_y = height * 0.40
    _mid = [v.co for v in trunk.data.vertices if abs(v.co.y - _mid_y) < height * 0.03]
    _cx = sum(v.x for v in _mid) / len(_mid)
    _cz = sum(v.z for v in _mid) / len(_mid)
    r_mid = sum(math.hypot(v.x - _cx, v.z - _cz) for v in _mid) / len(_mid)
    brown = [trunk]
    brown.append(build_crown_base(rng.sub(2), top, tan, r_top,
                                  count=int(max(7, min(12, round(fronds * 0.85))))))
    if nuts_n > 0:
        n = build_nuts(rng.sub(3), top, tan, r_top, nuts_n)
        if n is not None:
            brown.append(n)
    if dead_n > 0:
        brown.append(build_dead_fronds(rng.sub(4), top, tan, r_top, dead_n,
                                       frond_l, per_side))
    crown = build_crown(rng.sub(5), top, tan, spec, r_top, fronds, frond_l,
                        per_side, age0, age1, tear=tear, yaw0=yaw0)

    if abs(x_shift) > 1e-9:
        shift = Matrix.Translation(Vector((x_shift, 0.0, 0.0)))
        for o in brown + [crown]:
            mg.apply_transform(o)
            o.data.transform(shift)
    return brown, crown, r_mid


def _scale_to_height(objs, target_h):
    """전체를 **균등 배율**로 정확히 target_h 높이에 맞춘다.

    C# PalmModelHeights(MeshLibrary.cs:442)가 이 높이를 fit 배율의 분모로 쓰기 때문에
    palm_a~f 는 표의 값과 mm 단위로 같아야 한다. 균등 배율이라 비례·접지 규약은 그대로다.
    """
    for o in objs:
        mg.apply_transform(o)
    lo, hi = mg.union_bbox(objs)
    k = target_h / max(1e-6, (hi.y - lo.y))
    m = Matrix.Diagonal((k, k, k, 1.0))
    for o in objs:
        o.data.transform(m)
    return k


def build_palm(spec):
    rng = mg.Rng(spec["seed"])
    twin = spec.get("twin")
    # tfrac = 줄기의 명목 높이 / 전체 높이. 나머지는 왕관이 위로 채운다.
    # 이 값이 **어린 나무와 고목을 가르는 축**이다: 어린 코코야자는 줄기가 거의 없고 왕관만
    # 땅에서 솟아 있고(0.45), 고목은 맨 줄기가 길고 왕관이 작다(0.95).
    nominal = spec["height"] * spec.get("tfrac", 0.88) * (0.96 if twin else 1.0)

    brown, crown, r_mid = _stem(
        rng.sub(11), spec, nominal, spec["trunk_r"], spec["lean"], spec["curve"],
        spec["sway"], spec.get("wind", 0.0), spec["fronds"], spec["frond_l"],
        spec["leaflets"], spec["age0"], spec["age1"], spec["tear"],
        spec["dead"], spec["nuts"],
        x_shift=(-spec["trunk_r"] * 0.85 if twin else 0.0))

    if twin:
        b2, c2, _ = _stem(
            rng.sub(12), spec, nominal * twin["h"], spec["trunk_r"] * twin["r"],
            twin["lean"], spec["curve"] * 1.15, spec["sway"] * 0.6, 0.0,
            twin["fronds"], twin["frond_l"], spec["leaflets"] - 2,
            spec["age0"], spec["age1"], spec["tear"], 0, 0,
            yaw0=twin["yaw"], x_shift=spec["trunk_r"] * 1.35)
        brown += b2
        crown = mg.join_objects([crown, c2], name="crown")

    trunk = mg.join_objects(brown, name="trunk")
    k = _scale_to_height([trunk, crown], spec["height"])
    return trunk, crown, {"r_mid": r_mid * k, "scale": k}


# ══════════════════════════════════════════════════════════════════════════════
def main(only=None):
    out_dir = os.path.join(mg.PREVIEW_DIR, "_palm")     # 전용 폴더(동시 실행 충돌 방지)
    specs = [s for s in VARIANTS if not only or s["name"][-1] in only]
    print(f"[palm] 야자수 {len(specs)}종 생성  ->  {out_dir}")
    all_stats = []
    for spec in specs:
        name = spec["name"]
        mg.reset_scene()
        trunk, crown, info = build_palm(spec)
        trunk.name, crown.name = f"{name}_trunk", f"{name}_crown"

        # ★ `o` 순서 계약: 파일 안에서 **trunk 가 먼저**여야 한다.
        # ResourceVisualLibrary.TryLoadTwoPartModel 은 이름(trunk/crown)으로 먼저 가르지만,
        # 이름으로 못 가를 때 **`o` 등장 순서**로 폴백하고 "줄기가 항상 먼저"를 전제한다.
        # 그런데 wm.obj_export 는 이름순이 아니라 **오브젝트 생성(컬렉션 등록) 순서**로 쓴다
        # (실험으로 확인: 같은 이름쌍이라도 만든 순서대로 나온다). 이번 생성기는 왕관을 만든 뒤에
        # 갈색 파츠를 join 해서 trunk 를 만들므로 그대로 두면 crown 이 먼저 나가 구 파일과
        # 순서가 뒤집힌다. 컬렉션에서 crown 을 떼었다 다시 붙여 맨 뒤로 민다.
        _coll = bpy.context.collection
        _coll.objects.unlink(crown)
        _coll.objects.link(crown)
        stats = mg.enforce_contract_group([trunk, crown], tri_budget=TRI_BUDGET,
                                          tri_floor=TRI_FLOOR, name=name)

        base = [v.co for v in trunk.data.vertices if v.co.y < 0.03]
        bx = sum(v.x for v in base) / len(base)
        bz = sum(v.z for v in base) / len(base)
        br = sum(math.hypot(v.x - bx, v.z - bz) for v in base) / len(base)
        mr = info["r_mid"]      # 줄기 튜브만의 중간 굵기(build_palm 이 합치기 전에 잰 값)

        obj_path = os.path.join(mg.MODELS_DIR, f"{name}.obj")
        mg.export_obj([trunk, crown], obj_path)
        stats = mg.verify_obj_file(obj_path, stats)
        mg.inject_usemtl(obj_path)          # 반드시 verify 뒤(호출 순서 계약)

        mg.assign_material(trunk, mg.preview_material(
            f"prev_{name}_bark", texture_name="bark", base_color=PALM_BARK, roughness=0.86))
        mg.assign_material(crown, mg.preview_material(
            f"prev_{name}_frond", texture_name="frond", base_color=FROND_GREEN, roughness=0.62))
        mg.turntable([trunk, crown], os.path.join(out_dir, f"{name}.png"),
                     title=f"{name}  seed {spec['seed']}  fronds {spec['fronds']}"
                           f"  nuts {spec['nuts']}",
                     stats=stats, notes=spec["note"])

        mg.report(stats)
        print(f"             밑동 r {br:.3f} m  중간(40%) r {mr:.3f} m  "
              f"배율 {info['scale']:.3f}  밑동 오프셋 (x {bx:+.3f}, z {bz:+.3f})")
        all_stats.append(stats)

    print(f"[palm] 완료 - 렌더: {os.path.relpath(out_dir, mg.PROJECT_ROOT)}/palm_*.png")
    return all_stats


if __name__ == "__main__":
    main(set("".join(sys.argv[1:])) or None)
