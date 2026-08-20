using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using MakeGame.Data;
using MakeGame.Player;

namespace MakeGame.Systems
{
    /// <summary>
    /// 초지(내륙 풀밭) 잔디 필드 v2. 섬 지형 위에 수만 개의 잔디 카드를 GPU 인스턴싱으로 깐다.
    ///
    /// ── 구조 (SeabedGenerator의 "생성 훅 + 정적 레지스트리" 패턴을 그대로 따른다) ──
    ///  · 배치: 섬 메시 생성 직후 IslandMeshGenerator.BuildGroundCaps가 Build()를 1회 호출한다
    ///    (SeabedGenerator.Build 호출 바로 다음 줄 - 같은 동기 흐름). 섬별 인스턴스 행렬 배열을
    ///    한 번 구워 레지스트리에 담아 두고, 이후에는 절대 재계산하지 않는다.
    ///  · 렌더: 씬마다 자동 생성되는 GrassFieldDriver(UnderwaterAmbience/AtmospherePostFX와 같은
    ///    RuntimeInitializeOnLoadMethod + sceneLoaded 부트스트랩)가 LateUpdate에서
    ///    Graphics.RenderMeshInstanced로 그린다. GameObject/MeshRenderer가 인스턴스마다 생기지
    ///    않으므로 씬 오브젝트 수는 드라이버 1개뿐이다.
    ///
    /// ── v2: 알파 컷아웃 카드 텍스처 + 군락감 + 꽃 무리 ──
    ///  · 텍스처: Resources.Load&lt;Texture2D&gt;("Textures/grass_card") - 2×2 아틀라스
    ///    ((0,0) 촘촘한 초록 / (1,0) 성긴+이삭 / (0,1) 마른 풀 / (1,1) 분홍 꽃 스파이크).
    ///    카드 한 장에 풀잎 수십 가닥이 그려져 있어 같은 인스턴스 수로 밀도감이 크게 오른다.
    ///  · 머티리얼 2장(같은 MG/Grass 셰이더): 잔디(_CellOverride -1 = 원점 해시로 잔디 3셀 택1,
    ///    틴트 온) + 꽃(_CellOverride 3 = (1,1) 고정, _TintStrength 0 = 텍스처 원색).
    ///  · 폴백: 텍스처가 null이면 v1 방식으로 돌아간다 - 좁은 blade 메시 + 틴트 그라데이션만
    ///    (셰이더 _BaseMap 기본값 white가 곧 v1 렌더), 꽃 배치는 생략. 셰이더 자체가 없으면
    ///    기존 계약대로 잔디 전체 생략(폴백 렌더 없음 - 조용히 무동작, 행렬 메모리도 안 쓴다).
    ///  · 군락감(패치니스): 채택 확률에 파장 ~7m 저주파 격자 해시 노이즈(0.35~1.0 배율)를 곱해
    ///    잔디가 균일 카펫이 아니라 무성한 곳/성긴 곳으로 갈린다. 1차 통과가 배율 합계를 세므로
    ///    총 개수는 여전히 목표치 근처다.
    ///  · 꽃 무리: 파장 ~11m의 별도 해시 노이즈가 문턱(상위 ~12%)을 넘는 지대에서만 잔디의
    ///    8~15%를 꽃 배치로 전환 - 꽃이 드문드문 '무리 지어' 핀다. 꽃 인스턴스는 별도 행렬
    ///    리스트로 모아 꽃 머티리얼 배치로 렌더한다(LOD A/B 분할은 잔디와 동일 규칙).
    ///
    /// ── 셰이더 계약 (같은 웨이브의 MG/Grass, Resources/Shaders/MGGrass) ──
    ///  · _MG_WindTime(Time.time) / _MG_PlayerPos(플레이어 월드 위치)를 매 프레임 주입한다
    ///    (_MG_WindTime은 이제 **폴백 시계**다 - 평소 바람의 방향·세기·위상은 전역 WindSystem이
    ///     넣는 _MG_Wind에서 오고, 그게 비어 있을 때만 셰이더가 이 값으로 되돌아간다)
    ///    (잔디 머티리얼 - 꽃 머티리얼에도 같이 넣는다). 바람/밟힘 애니메이션은 전부 셰이더
    ///    정점 단계 - C#은 행렬을 다시 만지지 않는다.
    ///  · 카드 메시는 계약 규격([v4] 별모양 쿼드 3장 60도 간격, 폭 0.72m·높이 0.70m·피벗 밑동·
    ///    UV.y 0뿌리~1끝)대로 이 클래스가 코드로 생성한다(텍스처 폴백 시에는 v1 blade 규격
    ///    교차 2장 0.14m×1m). Cull Off 셰이더라 단면 지오메트리면 충분하다.
    ///
    /// ── [v4] 겹침·군집·융합 ("듬성듬성 조잡" 대응 - 실사 게임 표준 기법) ──
    ///  · 겹침: 격자 간격 0.42m &lt; 카드 폭 0.72m라 이웃 카드가 항상 겹쳐 낱장 실루엣이 사라진다.
    ///  · 다발 군집: 채택 셀 = 다발 앵커. 앵커당 카드 2~3장(위치 해시)을 반경 0.22m 안에 군집
    ///    생성 - 균일 산포 대신 실측 잔디처럼 다발+자연 틈. 카드 목표 = 기존 목표 × 1.6
    ///    (섬당 70k 클램프)을 평균 장수 2.5로 나눠 앵커 채택률을 역산하므로 기존 목표 개수
    ///    계약·B52 섬별 비율 변주 구조는 그대로다.
    ///  · 무작위 기울임 ±8도(축도 해시): 위에서 봐도 교차가 안 읽히고 윗선이 들쭉날쭉해진다.
    ///  · 뿌리색-지면 융합: _RootColor 기본값을 지면색 MeadowGreen의 어두운 변주로 - 밑동이
    ///    땅에 녹아든다(셰이더 기본값·머티리얼 세팅 양쪽).
    ///
    /// ── [B54] 섬 아키타입별 뿌리색 ────────────────────────────────────────────────
    /// 아키타입 8종이 지면색을 갈라 놓자(화산암 #3A3733 / 습지 #56603A / 바위 #8A7F6E …) 위의
    /// "뿌리색 = MeadowGreen의 그늘"이 열대 섬에서만 맞고 나머지에서는 밑동이 지면과 어긋났다.
    /// 머티리얼은 월드 공유 1장 그대로 두고, 섬별 MaterialPropertyBlock(RenderParams.matProps)으로
    /// _RootColor = 아키타입 groundColor × 0.56을 주입한다 - 배치 단위가 이미 섬별이라 블록이
    /// 그 섬의 드로우에만 걸린다. 블록은 아키타입당 1개(최대 8개)를 캐시해 같은 유형끼리 공유하고,
    /// 섬 레코드는 참조만 들고 있으므로 프레임당 할당은 0 그대로다. 열대 섬은 블록을 달지 않아
    /// (matProps == null) 기존 렌더와 **비트 단위로 같다**. 배치·밀도·해시는 한 줄도 안 바뀐다.
    ///
    /// ── [B56] 암반 필드 위에는 잔디가 없다 ─────────────────────────────────────
    /// B55가 아키타입 rockCoverage(바위 0.50 / 화산암 0.42 / 절벽 0.30 / 열대 0.08)만큼 지면을
    /// 암반 캡으로 덮고 초목을 배제했지만, 잔디는 그때 이미 구워진 뒤라 암반 위에 그대로 남아
    /// 있었다("지면 절반이 바위인데 그 위에 풀"). 이제 호출부가 암반 필드를 **잔디보다 먼저**
    /// 확정해 판정 함수(rockGrassKeep)를 Build에 넘기고, 배치 루프가 코어에서는 카드를 건너뛰고
    /// 경계 완충대(피복의 22%)에서는 유지 계수를 채택 확률로 써서 밀도를 서서히 올린다.
    /// **순수 감산이다** - 목표 개수식·기존 해시·패치/꽃/LOD 로직은 한 줄도 바뀌지 않았고,
    /// 1차 통과(eligibleWeight)도 그대로라 빠진 만큼이 다른 데로 재분배되지 않는다.
    /// rng 소비는 여전히 0(암반 판정도 순수 노이즈/해시다).
    ///
    /// ── 초지 판정 (IslandMeshGenerator의 실제 색 경계 규칙 재사용) ──
    /// B47부터 지면 캡 경계는 반경이 아니라 **해수면 기준 높이**다: DryTop(기준 높이 + BandWobble(angle)
    /// ± 디더 0.18m) 위가 지형 본체 = Meadow Green 초원, 아래는 모래 캡 3단이다(BuildGroundCaps).
    /// [B52] 그 기준 높이는 전 섬 공통 1.30m가 아니라 **섬별 grassLine**(1.15~3.70m,
    /// IslandMeshGenerator.GrassLineHeight - 캡의 DryTop과 단일 소스)이다.
    /// [B57] 경계식이 두 가지 바뀌었다:
    ///   · 굽이 항이 각도 기준 2층 → **미터 기준 6항 3층**(IslandMeshGenerator.ShoreBandWobble).
    ///     삼각형 디더는 사라졌다 - 캡을 그 곡선에서 **실제로 자르므로**(BuildCapLayer) 톱니를
    ///     가릴 항이 필요 없다. 잔디도 같은 함수(GrassBoundaryHeight)를 호출한다(단일 소스 그대로).
    ///   · "경계 위만 채택"이 **전이대 램프**로 바뀌었다. 경계선 아래 0.55m부터 위 1.15m까지
    ///     밀도가 0 → 1로 차오르고, 그 구간의 카드는 키가 작고(0.60배) 마른 풀 셀 비율이 높다.
    ///     모래 위 0.55m에 성긴 마른 풀이 서는 것은 이제 **의도**다(예전 "모래 위 잔디 금지"
    ///     규약은 경계가 톱니였을 때의 방어였다 - EcotoneBelowFraction 상수 주석에 근거).
    /// phaseA/phaseB는 훅에서 그대로 넘겨받으므로 경계가 실제 모래 경계와 같은 위상으로 굽이친다.
    /// 수면 근처는 이 높이 조건이 자동으로 배제하고(해수면 0m ≪ 0.6m+), 바위 절벽 지대(P7 메사 등)는
    /// 경사 30도 초과 제외가 걸러낸다.
    ///
    /// ── [B52] 섬별 잔디량 스펙트럼 ("잔디가 너무 많다" 대응) ──
    /// 목표 개수 = 기존 목표 × 전역 0.65 × 섬별 lerp(1.0, 0.45, t). t는 grassLine과 **같은**
    /// 섬 해시(IslandMeshGenerator.ComputeGrassLineT)라, 경계선이 높은 모래 섬일수록 남은 초지의
    /// 잔디도 성기다. t가 균등 분포이므로 섬 평균 밀도 계수는 0.65 × 0.725 ≈ 0.47 - 경계 상승으로
    /// 초지 면적 자체도 줄어 체감 잔디량은 현재 대비 약 45~50% 이하다. 꽃 무리·패치니스·LOD
    /// 로직은 전부 불변이고 모수(목표 개수)만 줄어든다.
    ///
    /// ── 높이 샘플: 레이캐스트 0회 ──
    /// 섬 메시는 런타임 생성이라 readable이고, 정점 배열이 결정적 극좌표 격자다
    /// (index 0 = 중심, ring k(1..ringCount)의 seg s = 1+(k-1)*segments+s, 각도 = s/segments·2π,
    /// 반경 = k/ringCount·R - IslandMeshGenerator.GenerateIslandMesh). 그 격자를 (ring, seg) 이중
    /// 선형 보간으로 직접 읽는다. 후보 수만 개 × 섬 50개를 Physics.Raycast로 하면 수백만 캐스트라
    /// 애초에 성립하지 않는 비용이고, 정점 보간은 곱셈 몇 번이다.
    ///
    /// ── rng 불변 (SeabedGenerator와 같은 근거) ──
    /// System.Random/UnityEngine.Random을 만들지도 소비하지도 않는다. 지터·선별·회전·스케일 변주·
    /// 패치 노이즈·꽃 전환까지 전부 (격자 정수 좌표, 섬 월드 위치 유래 salt)만 입력으로 받는 순수
    /// 해시다(IslandMeshGenerator.ComputeNoiseSeed / SeabedGenerator.LatticeHash01과 같은 finalizer
    /// 계열). 따라서 자원/위험요소/초목의 추첨 순서는 한 칸도 밀리지 않는다.
    ///
    /// ── 성능 가드 ──
    ///  · 행렬 배열은 섬당 1회 생성 후 재사용(프레임당 할당 0). RenderParams는 스택 구조체다.
    ///  · 간이 LOD: 배치 시 위치 해시로 A/B 두 그룹(각 절반)으로 미리 갈라 두고, 카메라-섬 테두리
    ///    거리 60m 이내면 A+B(전체), 60~300m면 A만(절반 밀도), 300m 밖이면 스킵한다. 꽃도 같은
    ///    규칙으로 A/B를 가른다. 프레임당 인스턴스 단위 재계산은 없다 - 거리 비교는 섬당 1회다.
    ///  · 비활성 섬(RegenerateWorld의 SetActive(false) → Destroy 흐름)은 그리지 않고, 파괴된 섬의
    ///    레코드는 조회 시 걸러 제거한다(SeabedGenerator.TrySampleSeabed와 같은 정리 규칙).
    /// </summary>
    public static class GrassFieldSystem
    {
        // ── 밀도/배치 상수 ─────────────────────────────────────────────────────────

        /// <summary>전체 잔디 밀도 배율. 성능 튜닝은 이 값 하나로 한다(0.5 = 절반, 0 = 잔디 끔).</summary>
        public const float DensityMultiplier = 1f;

        /// <summary>
        /// 후보 격자 간격(m). 지터가 ±0.45×이 값이라 격자무늬가 눈에 남지 않는다.
        /// [v4] 0.55 → 0.42: 카드 폭(0.72m)보다 좁아 이웃 카드가 항상 겹친다 - 낱장 빌보드가
        /// 개별로 읽히던 "듬성듬성" 실루엣이 사라진다(겹침은 실사 잔디의 기본 기법).
        /// </summary>
        private const float CellSpacing = 0.42f;

        // ── [B57 해변 전이대(ecotone)] 모래 → 성긴 풀 → 빽빽한 풀 ────────────────────────
        // 예전에는 잔디가 "경계 높이 + 디더 반폭 0.18m" 위에서 **밀도 100%로 시작**했다. 그래서
        // 화면에서는 백사장과 초지가 한 줄로 딱 잘려, 캡 경계의 삼각형 톱니와 겹쳐 더 인공적으로
        // 읽혔다. 실제 열대 해변은 모래에서 초지로 수 미터에 걸쳐 옮겨 간다.
        //
        // 조치: 경계선 H(x,z) = grassLine + ShoreBandWobble(단일 소스, 캡을 자르는 바로 그 곡선)를
        // 기준으로 **높이 대역**을 잡고 그 안에서 밀도를 램프시킨다.
        //   · 대역 아래 1/3보다 낮으면 : 잔디 없음(순수 백사장)
        //   · 아래 1/3 ~ 경계선 H      : 성긴 풀. 마른 풀 셀 비율이 높고 키가 작다(모래 위 잔풀)
        //   · H ~ 대역 위 2/3          : 밀도가 100%까지 차오른다
        // 대역 높이는 **섬마다 다르다** - IslandMeshGenerator가 잔디선 대표 경사를 재서
        // "목표 수평 6m × 경사"로 환산해 넘긴다(BuildGroundCaps의 boundarySlope 주석에 실측 근거:
        // 잔디선의 실제 경사는 물가 근처의 0.23~0.69가 아니라 0.04~0.30이다). 고정 높이 대역을
        // 쓰면 완만한 섬에서 전이대가 20m를 넘어 백사장을 통째로 덮는다.
        //
        // ★ 잔디가 모래 위에 서는 것이 이제 **의도**다 ★ B52의 "어떤 디더 값에서도 모래 위에 서지
        // 않는다"는 규약은 경계가 톱니였을 때 그 톱니 위에 카드가 서는 것을 막으려던 것이었다.
        // 경계를 실제로 잘라 매끈해진 지금(BuildCapLayer의 [B57]), 모래 쪽 0.55m에 성긴 마른 풀이
        // 서는 것은 레퍼런스 그대로다. 캡 상단은 셰이더가 초지색으로 녹이므로 색도 이어진다.

        /// <summary>
        /// 전이대 높이 대역 중 **아래**(모래 쪽)가 차지하는 비율. 나머지가 위(초지 쪽)다.
        /// 1:2 = 모래 위로 살짝 침범하고 초지 쪽으로 길게 차오르는 배분(수평 6m면 2m + 4m).
        /// </summary>
        private const float EcotoneBelowFraction = 1f / 3f;

        /// <summary>
        /// Build가 대역을 못 받았을 때(호출부가 옛 시그니처)의 기본 높이 대역(m).
        /// 실측 경계 경사 중앙값 0.12 × 목표 수평 6m ≈ 0.72m.
        /// </summary>
        private const float EcotoneHeightFallback = 0.72f;

        /// <summary>
        /// 전이대가 내려갈 수 있는 하한 = 축축한 모래 캡 상한(BuildGroundCaps의 DampTop 기준값
        /// 0.75m + 삼각형 디더 반폭 0.18m). 이 위로만 잔디가 선다.
        /// </summary>
        private const float DampSandTopCeiling = 0.93f;

        /// <summary>전이대 바닥에서의 카드 높이 배율(위로 갈수록 1.0). 물가 쪽 풀이 짧다.</summary>
        private const float EcotoneMinHeightScale = 0.60f;

        /// <summary>전이대 바닥에서 **마른 풀 다발**이 될 확률. 위로 갈수록 0으로 준다.</summary>
        private const float EcotoneDryChanceMax = 0.90f;

        /// <summary>마른 풀 다발 추첨용 salt(이 파일의 다른 salt와 겹치지 않는 새 값).</summary>
        private const uint DryTuftSalt = 0x5BD1E995u;

        /// <summary>[B52] 전역 잔디 감소 계수. 전 섬 공통으로 목표 개수에 곱한다.</summary>
        private const float GlobalDensityScale = 0.65f;

        /// <summary>[B52] 섬별 밀도 계수 하한. t=0 섬은 1.0, t=1 섬(모래 섬)은 이 값까지 성기다.</summary>
        private const float IslandDensityMin = 0.45f;

        /// <summary>경사 상한 tan(30°). 정점 격자 기울기가 이보다 크면 바위 절벽 지대로 보고 제외한다.</summary>
        private const float MaxSlopeTan = 0.57735f;

        /// <summary>
        /// [B56] 암반 경계 완충대의 채택 판정용 salt. 이 파일의 다른 salt와 겹치지 않는 새 값이라
        /// 지터·선별·패치·꽃·LOD 어느 해시와도 상관이 없다(기존 해시는 한 줄도 바뀌지 않았다).
        /// </summary>
        private const uint RockBandSalt = 0x3C79AC49u;

        /// <summary>경사 측정용 유한 차분 간격(m). 메시 삼각형(2~5m)보다 작아 국소 경사를 잡는다.</summary>
        private const float SlopeProbeStep = 0.6f;

        /// <summary>카드 밑동을 지면에 살짝 심는 깊이(m). 경사면에서 밑동이 뜨는 것을 가린다.</summary>
        private const float RootSinkDepth = 0.04f;

        // ── [v3] 이봉(bimodal) 스케일 변주 상수 (다발 tuft - 순수 위치 해시, rng 소비 0) ──
        // 실제 초지는 낮은 하층 풀 사이로 솟은 다발이 드문드문 섞인다. 위치 해시로 80%는
        // 짧은 하층(0.5~0.9), 20%는 솟은 다발(1.0~1.5, xz도 1.0~1.25로 살짝 넓게)로 가른다.
        // 배치 개수·판정·LOD 로직은 불변 - 행렬 스케일 계산만 이 상수를 쓴다.

        /// <summary>솟은 다발(tuft)로 뽑히는 비율.</summary>
        private const float TuftFraction = 0.2f;

        /// <summary>하층 짧은 풀의 높이 스케일 범위.</summary>
        private const float UnderScaleYMin = 0.5f;
        private const float UnderScaleYMax = 0.9f;

        /// <summary>하층 짧은 풀의 폭(xz) 스케일 범위(v2와 동일).</summary>
        private const float UnderScaleXZMin = 0.8f;
        private const float UnderScaleXZMax = 1.1f;

        /// <summary>솟은 다발의 높이 스케일 범위.</summary>
        private const float TuftScaleYMin = 1.0f;
        private const float TuftScaleYMax = 1.5f;

        /// <summary>솟은 다발의 폭(xz) 스케일 범위 - 키만 크면 빗자루 같아서 살짝 넓게.</summary>
        private const float TuftScaleXZMin = 1.0f;
        private const float TuftScaleXZMax = 1.25f;

        // ── [v4] 다발(tuft cluster) 군집 배치 상수 (전부 순수 위치 해시, rng 소비 0) ──
        // 실측 잔디는 균일 산포가 아니라 다발로 자란다. 채택된 격자 셀을 '다발 앵커'로 삼아
        // 카드 2~3장을 앵커 주변 반경 0.22m 안에 군집 생성한다 - 군집 사이에 자연스러운 틈이
        // 남고, 군집 안에서는 카드가 서로 겹쳐 낱장 실루엣이 완전히 사라진다.

        /// <summary>목표 인스턴스 수 배율. 기존 목표 × 1.6이 카드 장수 목표가 된다.</summary>
        private const float ClusterDensityBoost = 1.6f;

        /// <summary>다발 앵커 주변 카드 산포 반경(m).</summary>
        private const float ClusterRadius = 0.22f;

        /// <summary>앵커당 카드 장수 기대값(2장 50% / 3장 50% = 평균 2.5). 앵커 채택률 역산용.</summary>
        private const float ClusterCardsAvg = 2.5f;

        /// <summary>카드 무작위 기울임 최대각(도). 축도 위치 해시 - 위에서 봐도 별모양이 안 읽힌다.</summary>
        private const float MaxTiltDegrees = 8f;

        /// <summary>섬당 인스턴스 수 상한(XL 기준 ~70개 × 1023 배치 - RenderMeshInstanced 허용 범위).</summary>
        private const int MaxInstancesPerIsland = 70000;

        // ── 군락(패치니스)/꽃 무리 상수 (전부 순수 해시 노이즈 - rng 소비 0) ────────────

        /// <summary>패치 노이즈 파장(m). 이 스케일로 무성한 곳/성긴 곳이 갈린다.</summary>
        private const float PatchWavelength = 7f;

        /// <summary>패치 노이즈 최저 배율. 성긴 곳도 완전히 비지는 않는다(0.35~1.0).</summary>
        private const float PatchFloor = 0.35f;

        /// <summary>꽃 무리 노이즈 파장(m). 패치보다 넓어 꽃밭이 더 큰 덩어리로 뭉친다.</summary>
        private const float FlowerWavelength = 11f;

        /// <summary>
        /// 꽃 지대 문턱. 보간된 격자 해시 노이즈는 0.5 근처로 몰리므로 0.76 초과는 면적 기준
        /// 상위 ~12% - 레퍼런스처럼 꽃이 드문드문 무리 지어 핀다.
        /// </summary>
        private const float FlowerZoneThreshold = 0.76f;

        /// <summary>꽃 지대 안에서 잔디→꽃 전환 비율의 하한/상한(문턱 초과 정도로 보간).</summary>
        private const float FlowerRatioMin = 0.08f;
        private const float FlowerRatioMax = 0.15f;

        // ── 렌더/LOD 상수 ──────────────────────────────────────────────────────────

        /// <summary>이 거리(카메라→섬 테두리) 이내면 A+B 전체를 그린다.</summary>
        private const float FullDetailDistance = 60f;

        /// <summary>이 거리(카메라→섬 테두리) 밖이면 섬을 통째로 스킵한다.</summary>
        private const float MaxRenderDistance = 300f;

        /// <summary>Graphics.RenderMeshInstanced 1회 호출당 인스턴스 상한(계약: 1023개 단위 배치).</summary>
        private const int InstancesPerBatch = 1023;

        // ── 레지스트리 (SeabedGenerator.SkirtRecord와 같은 생명주기 규칙) ──────────────

        private sealed class GrassRecord
        {
            public Transform root;         // 섬 지형 오브젝트. 파괴(RegenerateWorld) 감지용.
            public Vector3 center;         // 섬 중심(월드)
            public float radius;           // 섬 지형 반지름 R
            public Matrix4x4[] groupA;     // 잔디 LOD 그룹 A(약 절반). 원거리에서는 이것만 그린다.
            public Matrix4x4[] groupB;     // 잔디 LOD 그룹 B(나머지 절반). 근거리에서만 추가.
            public Matrix4x4[] dryA;       // [B57] 전이대 마른 풀 LOD 그룹 A(마른 풀 셀 고정 머티리얼).
            public Matrix4x4[] dryB;       // [B57] 같은 그룹 B. 없으면 null - 드로우콜도 0이다.
            public Matrix4x4[] flowerA;    // 꽃 LOD 그룹 A(꽃 머티리얼로 렌더. 폴백 시 null).
            public Matrix4x4[] flowerB;    // 꽃 LOD 그룹 B.
            public Bounds bounds;          // RenderParams.worldBounds(섬 단위)

            /// <summary>
            /// [B54] 이 섬의 _RootColor 주입 블록(아키타입 지면색 × RootColorGroundFactor).
            /// Tropical이면 null - 머티리얼에 구워 둔 기본값이 곧 열대 값이라 주입할 것이 없다
            /// (RootColorBlockFor 주석). 배치 시 1회 잡고 프레임에서는 참조만 넘긴다(할당 0).
            /// </summary>
            public MaterialPropertyBlock rootProps;
        }

        private static readonly List<GrassRecord> registry = new List<GrassRecord>();

        /// <summary>
        /// [B54] 아키타입별 _RootColor 블록 캐시(최대 8개 = IslandArchetypes.Count). 지면색은
        /// 아키타입 표에서만 오므로 섬 50개가 아니라 **유형 수**만큼만 있으면 된다 - 같은 유형의
        /// 섬들은 블록 하나를 공유한다(MaterialPropertyBlock은 읽기 전용으로만 쓰인다).
        /// null 슬롯 = 아직 안 만들었거나 Tropical(주입 불필요).
        /// </summary>
        private static MaterialPropertyBlock[] rootColorBlocks;

        private static Mesh bladeMesh;
        private static Material grassMaterial;
        private static Material flowerMaterial;  // 카드 텍스처 폴백 시 null - 꽃 배치 자체가 없다.
        private static Material dryMaterial;     // [B57] 전이대 마른 풀(아틀라스 2번 셀 고정). 폴백 시 null.
        private static bool shaderMissing;       // 한 번 실패하면 이후 전부 조용히 무동작(계약)
        private static bool hasCardTexture;      // grass_card 로드 성공 여부(꽃/카드 규격 스위치)
        private static int windTimeId;
        private static int playerPosId;
        private static int rootColorId;

        /// <summary>
        /// [B54] 아키타입 지면색 → 잔디 뿌리색 계수. v4가 뿌리색을 "지면 MeadowGreen의 어두운
        /// 변주"로 정한 그 계수(머티리얼 기본값 (0.30, 0.37, 0.17) ≈ MeadowGreen × 0.56)를
        /// 그대로 쓴다 - 섬 지면색이 무엇이든 밑동이 **같은 색상각의 그늘**로 읽혀 땅에 녹아든다.
        /// </summary>
        private const float RootColorGroundFactor = 0.56f;

        /// <summary>
        /// [R1 규약] 도메인 리로드를 끈 상태에서 이전 플레이 세션의 정적 캐시가 새지 않게 비운다
        /// (IslandArchetypes.ResetStaticCache / SeabedGenerator.ResetStaticCache가 선례다).
        /// 블록은 아키타입 표만 보고 언제든 다시 만들 수 있으므로 버리는 것이 안전하다.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticCache()
        {
            rootColorBlocks = null;
        }

        // ═══════════════════════════════════════════════════════════════════════════
        //  배치 (섬당 1회, 결정적)
        // ═══════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// 섬 하나의 잔디/꽃 인스턴스 배열을 굽는다. IslandMeshGenerator.BuildGroundCaps에서
        /// 섬 메시 확보 직후 같은 동기 흐름으로 호출된다(SeabedGenerator.Build와 같은 훅 지점).
        /// </summary>
        /// <param name="islandObject">섬 지형 오브젝트("Island_{id}_{size}").</param>
        /// <param name="islandMesh">그 섬의 지형 메시(정점 높이를 직접 읽는 소스).</param>
        /// <param name="radius">섬 지형 반지름 R(m). 메시 XZ 반경과 같다.</param>
        /// <param name="phaseA">모래 경계 BandWobble 위상 A(BuildGroundCaps와 같은 값).</param>
        /// <param name="phaseB">모래 경계 BandWobble 위상 B(BuildGroundCaps와 같은 값).</param>
        /// <param name="rockGrassKeep">
        /// [B56] 암반 필드 판정을 통째로 포장한 **순수 함수**(월드 좌표 → 잔디 유지 계수 [0,1]).
        /// 1 = 암반 밖(예전 그대로) / 0 = 암반 코어(초목 배제와 같은 기준 - 카드를 놓지 않는다) /
        /// 그 사이 = 코어와 피복 경계 사이 완충대(값을 채택 확률로 써서 밀도를 서서히 올린다).
        /// null이면 암반 피복이 없는 섬이라는 뜻이고, 그때 잔디는 예전과 **비트 단위로 같다**.
        /// 판정 본체는 IslandMeshGenerator.Vegetation의 RockGrassKeep 하나뿐이다(노이즈 상수·임계·
        /// 디더 해시를 여기로 복사하지 않는 이유 - 단일 소스 규약).
        /// </param>
        /// <param name="ecotoneHeight">
        /// [B57] 잔디 전이대의 높이 대역(m). IslandMeshGenerator가 그 섬 잔디선의 대표 경사에서
        /// 환산해 넘긴다(캡을 자르는 경계선과 단일 소스). 0 이하면 기본값으로 떨어진다.
        /// </param>
        public static void Build(GameObject islandObject, Mesh islandMesh, float radius,
            float phaseA, float phaseB, System.Func<Vector3, float> rockGrassKeep = null,
            float ecotoneHeight = 0f)
        {
            if (islandObject == null || islandMesh == null || radius <= 0f || DensityMultiplier <= 0f)
                return;

            // 셰이더가 없으면 배치 자체를 생략한다(폴백 렌더 없음 - 계약). 행렬 메모리도 아낀다.
            if (!EnsureGrassAssets())
                return;

            Transform islandTransform = islandObject.transform;
            for (int i = 0; i < registry.Count; i++)
            {
                if (registry[i].root == islandTransform)
                    return; // 같은 섬에 두 번 불려도(방어) 잔디가 겹으로 깔리지 않게 한다.
            }

            // ── 메시 정점 격자 복원 ────────────────────────────────────────────────
            // 정점 순서는 GenerateIslandMesh가 결정적으로 보장한다(클래스 주석). 최외곽 링의
            // 정점 수를 세어 radialSegments를 얻고(SeabedGenerator.ExtractRimHeights와 같은
            // 문턱 R-1m - 링 간격이 R/ringCount ≥ 5m라 오분류가 없다) 총 정점 수로 검산한다.
            Vector3[] verts = islandMesh.vertices;
            float rimThreshold = radius - 1f;
            int segments = 0;
            for (int i = 0; i < verts.Length; i++)
            {
                Vector3 v = verts[i];
                if (Mathf.Sqrt(v.x * v.x + v.z * v.z) >= rimThreshold)
                    segments++;
            }
            if (segments < 8 || (verts.Length - 1) % segments != 0)
                return; // 절차적 지형이 아닌 구성(placeholder 프리팹 등)이면 조용히 건너뛴다.
            int rings = (verts.Length - 1) / segments;
            if (rings < 2)
                return;

            Vector3 center = islandTransform.position;

            // 섬별 해시 salt. 섬 월드 위치(worldSeed에 결정적)만 입력으로 받는 순수 해시라
            // 같은 시드면 항상 같은 잔디가 나오고, 반지름이 같은 다른 섬과는 패턴이 갈린다.
            uint islandSalt;
            unchecked
            {
                islandSalt = Mix32((uint)(Mathf.RoundToInt(center.x * 4f) * 73856093)
                    ^ (uint)(Mathf.RoundToInt(center.z * 4f) * 19349663) ^ 0x3D4A2C15u);
            }

            // [B52] 섬별 잔디 경계선(단일 소스: IslandMeshGenerator - 캡의 DrySandCap 상한(DryTop)과
            // 정확히 같은 값이다. 어긋나면 모래 위 잔디/잔디 없는 초원이 생긴다). t는 밀도에도 재사용한다.
            // ComputeGrassLineT는 섬 월드 위치만 입력인 순수 해시라 rng 소비 0, 세이브 로드 후에도 동일하다.
            // [B54 아키타입] ComputeGrassLineT(GameObject)가 이미 아키타입 오프셋을 태워 돌려준다
            // (BuildGroundCaps의 DryTop과 **같은 함수·같은 값** - 단일 소스 규약 그대로다).
            // 밀도/키 배율만 여기서 따로 읽는다.
            var archetype = IslandMeshGenerator.ArchetypeProfileOf(islandObject);
            float grassHeightScale = Mathf.Max(0.1f, archetype.grassHeightScale);
            float grassLineT = IslandMeshGenerator.ComputeGrassLineT(islandObject);
            // [B57] 디더 반폭(+0.18)을 더하지 않는다. 캡의 DryTop에서 삼각형 디더가 사라졌고
            // (경계를 실제로 자르므로 톱니를 가릴 항이 필요 없다), 전이대가 이 기준선을 위아래로
            // 걸치는 것이 이번 배치의 목적이다 - 위 전이대 상수 주석.
            float grassLineBase = IslandMeshGenerator.GrassLineHeightFromT(grassLineT);
            // [B57] 전이대 대역을 아래(모래 쪽)/위(초지 쪽)로 1:2 배분한다.
            float ecoBand = ecotoneHeight > 0f ? ecotoneHeight : EcotoneHeightFallback;
            float ecoBelow = ecoBand * EcotoneBelowFraction;
            // 전이대가 **축축한 모래**(DampTop 0.75m + 디더 0.18m)까지 내려가지는 않게 자른다.
            // 잔디가 마른 모래에 성기게 올라앉는 것은 의도지만(위 주석), 파도가 닿는 띠에까지
            // 서면 그건 다른 그림이다. 경계 굽이는 잔디선과 DampTop에 **같은 값**이 더해지므로
            // 이 비교는 굽이와 무관하게 성립한다(기준 높이끼리의 차이만 본다).
            ecoBelow = Mathf.Min(ecoBelow, Mathf.Max(0f, grassLineBase - DampSandTopCeiling));

            // 섬 크기별 목표 개수: Small(R50) ~12k / Medium(R90) ~20k / Large(R140) ~30k / XL(R200) ~40k.
            // 반지름 선형 보간이 위 네 점을 ±4% 안으로 지나므로 별도 테이블이 필요 없다.
            // [B52] 여기에 전역 0.65 × 섬별 lerp(1.0, 0.45, t)를 곱한다 - 경계선이 높은 모래 섬(t 큼)
            // 일수록 남은 초지의 잔디도 성기다. 목표 개수만 줄고 이후 로직(패치·꽃·LOD)은 불변이다.
            // [B54] 아키타입 밀도 배율이 t 감쇠와 **곱해진다**. 두 항의 역할이 다르기 때문이다:
            // t는 "이 섬의 초지가 얼마나 좁은가"(경계선 높이와 같은 소스), 배율은 "그 초지가 얼마나
            // 무성한가"다. 습지섬(1.60)은 t까지 낮아 실효 밀도가 크게 오르고, 산호섬(0.40)은 t도
            // 높아 이중으로 성기다 - 유형 차이가 화면에서 확실히 읽히게 하는 것이 목적이다.
            float targetCount = DensityMultiplier
                * GlobalDensityScale
                * Mathf.Lerp(1f, IslandDensityMin, grassLineT)
                * Mathf.Max(0.05f, archetype.grassDensityScale)
                * Mathf.Lerp(12000f, 40000f, Mathf.InverseLerp(50f, 200f, radius));
            if (targetCount < 1f)
                return;

            int cellRange = Mathf.CeilToInt(radius / CellSpacing);
            float maxPlaceRadiusSq = radius * 0.98f * radius * 0.98f;

            // ── 1차 통과: 초지 조건을 만족하는 후보 셀의 패치 배율 합을 구한다(저장 없음) ──
            // v1은 개수만 셌지만 v2는 채택 확률에 패치 노이즈 배율(0.35~1.0)이 곱해지므로,
            // 배율 합계로 나눠야 총 개수가 목표치 근처에 남는다. 경사 검사는 여기서 하지
            // 않는다(비싼 검사라 채택된 소수에만 건다 - 목표는 "~" 근사치라 몇 % 미달은 허용).
            // [B57] 전이대 계수(0~1)를 여기서도 곱한다. 1차/2차 통과가 **같은 가중치**를 봐야
            // 총 카드 수가 목표치 근처에 남는다(패치 노이즈를 양쪽에 곱하는 것과 같은 이유).
            float eligibleWeight = 0f;
            for (int iz = -cellRange; iz <= cellRange; iz++)
            {
                for (int ix = -cellRange; ix <= cellRange; ix++)
                {
                    float x, z, y, eco;
                    if (TryGetGrassCandidate(ix, iz, islandSalt, verts, rings, segments, radius,
                            maxPlaceRadiusSq, phaseA, phaseB, grassLineBase, ecoBelow, ecoBand,
                            out x, out z, out y, out eco))
                        eligibleWeight += PatchDensity(x, z, islandSalt) * eco;
                }
            }
            if (eligibleWeight <= 0f)
                return;

            // [v4] 카드 장수 목표 = 기존 목표 × 1.6(겹침·군집으로 밀도감을 올린다) - 상한 70k
            // 클램프(XL도 1023 배치 ~70개 - RenderMeshInstanced 허용 범위). 목표 개수 계약과
            // B52 섬별 비율 변주는 targetCount에 이미 들어 있으므로 구조는 그대로다.
            float cardTarget = Mathf.Min(targetCount * ClusterDensityBoost, MaxInstancesPerIsland);

            // 패치 배율이 곱해진 채택 판정에서 총 기대 앵커 수 = keepProbability × 배율 합.
            // [v4] 앵커 하나가 카드 평균 2.5장을 만들므로 앵커 목표 = 카드 목표 / 2.5로 역산한다.
            // 1을 넘으면(작은 초지) 무성한 셀이 전부 채택될 뿐이라 클램프가 필요 없다.
            float keepProbability = cardTarget / ClusterCardsAvg / eligibleWeight;

            // ── 2차 통과: 해시 선별(×패치 배율) + 경사 검사 + 다발 카드 생성 + 꽃 전환 ──────
            int expected = Mathf.CeilToInt(cardTarget * 0.55f) + 16;
            var listA = new List<Matrix4x4>(expected);
            var listB = new List<Matrix4x4>(expected);
            // [B57] 전이대 마른 풀 리스트. 마른 풀 셀(아틀라스 2번)을 고정한 별도 머티리얼로
            // 그리므로 배열을 따로 모은다 - 셰이더의 셀 선택이 인스턴스 단위가 아니라 머티리얼
            // 프로퍼티(_CellOverride)라 이 방법 말고는 셀을 편향시킬 수단이 없다(MGGrass는 수정
            // 금지 파일이고, 퍼 인스턴스 커스텀 데이터도 받지 않는다).
            List<Matrix4x4> dryListA = null;
            List<Matrix4x4> dryListB = null;
            if (dryMaterial != null)
            {
                dryListA = new List<Matrix4x4>(512);
                dryListB = new List<Matrix4x4>(512);
            }
            List<Matrix4x4> flowerListA = null;
            List<Matrix4x4> flowerListB = null;
            if (flowerMaterial != null)
            {
                // 꽃은 전체의 최대 ~2%(면적 12% × 전환 15%) 수준 - 소용량으로 시작해도 충분하다.
                flowerListA = new List<Matrix4x4>(256);
                flowerListB = new List<Matrix4x4>(256);
            }
            float minY = float.MaxValue;
            float maxY = float.MinValue;

            for (int iz = -cellRange; iz <= cellRange; iz++)
            {
                for (int ix = -cellRange; ix <= cellRange; ix++)
                {
                    float x, z, y, eco;
                    if (!TryGetGrassCandidate(ix, iz, islandSalt, verts, rings, segments, radius,
                            maxPlaceRadiusSq, phaseA, phaseB, grassLineBase, ecoBelow, ecoBand,
                            out x, out z, out y, out eco))
                        continue;

                    // 군락감: 채택 확률에 파장 ~7m 저주파 노이즈 배율(0.35~1.0)을 곱한다 -
                    // 균일 카펫이 아니라 무성한 곳/성긴 곳으로 갈린다. 1차 통과와 같은 식이라
                    // 총 개수는 목표치 근처를 유지한다.
                    // [B57] 여기에 전이대 계수를 곱한다 = 경계에서 밀도가 0으로 **완만히** 빠진다
                    // (예전에는 경계 한 줄에서 100% → 0으로 끊겼다).
                    float patch = PatchDensity(x, z, islandSalt);
                    if (Hash01(ix, iz, islandSalt ^ 0x9E3779B9u) > keepProbability * patch * eco)
                        continue;

                    // 경사 30도 초과 제외(유한 차분). 절벽(P7 메사)·수로 둑 상단 급경사를 걸러낸다.
                    float gx = (SampleHeight(verts, rings, segments, radius, x + SlopeProbeStep, z)
                              - SampleHeight(verts, rings, segments, radius, x - SlopeProbeStep, z))
                              / (2f * SlopeProbeStep);
                    float gz = (SampleHeight(verts, rings, segments, radius, x, z + SlopeProbeStep)
                              - SampleHeight(verts, rings, segments, radius, x, z - SlopeProbeStep))
                              / (2f * SlopeProbeStep);
                    if (gx * gx + gz * gz > MaxSlopeTan * MaxSlopeTan)
                        continue;

                    // [B56] 암반 필드 배제. 아키타입 rockCoverage만큼 지면을 덮은 암반 캡
                    // (IslandMeshGenerator의 RockCap) 위에는 카드를 놓지 않는다 - 바위섬은 지면
                    // 절반이 암반인데 그 위에 잔디가 자라 보이던 것이 이 스킵 이전의 상태다.
                    //  · 코어(계수 0)는 야자수/덤불/풀포기를 배제하는 것과 **같은 임계**다.
                    //  · 완충대(0<계수<1)는 계수를 그대로 채택 확률로 써서 밀도가 서서히 오른다
                    //    (평균 절반). 앵커 단위 판정이라 한 다발이 통째로 남거나 통째로 빠진다 -
                    //    카드 2~3장이 반쪽만 남아 다발이 찢어지는 일이 없다.
                    //  · 위치는 카드와 같은 규약의 월드 좌표(center + 로컬)다.
                    //  · ★ 여기서만 스킵한다 ★ 1차 통과(eligibleWeight)는 일부러 건드리지 않았다.
                    //    거기서도 빼면 keepProbability가 그만큼 올라가 남은 초지에 같은 총량이
                    //    다시 몰린다 - "암반만큼 잔디가 준다"는 의도가 상쇄돼 사라진다.
                    //    기존 목표 개수식·해시·패치 노이즈는 한 줄도 바뀌지 않았다(순수 감산).
                    if (rockGrassKeep != null)
                    {
                        float rockKeep = rockGrassKeep(
                            new Vector3(center.x + x, center.y + y, center.z + z));
                        if (rockKeep <= 0f)
                            continue;
                        if (rockKeep < 1f && Hash01(ix, iz, islandSalt ^ RockBandSalt) >= rockKeep)
                            continue;
                    }

                    // [v4] 다발 군집: 이 셀은 '다발 앵커'다. 카드 2~3장(위치 해시)을 반경 0.22m
                    // 안에 군집 생성한다. [v3] 이봉 스케일의 다발/하층 분류는 앵커 단위(한 다발이
                    // 통째로 솟거나 낮다 - 실측 다발의 응집성), 나머지 변주는 카드 단위다.
                    bool isTuft = Hash01(ix, iz, islandSalt ^ 0x94D049BBu) < TuftFraction;
                    int cardCount = Hash01(ix, iz, islandSalt ^ 0x632BE59Bu) < 0.5f ? 2 : 3;

                    // [B57] 마른 풀 판정은 **앵커 단위**다 - 한 다발이 통째로 마르거나 통째로
                    // 푸르러야 다발이 두 색으로 찢어지지 않는다(암반 완충대 판정과 같은 규칙).
                    // 확률은 전이대 바닥에서 0.90, 위로 갈수록 0이다(제곱으로 빨리 준다).
                    // 바닷가 쪽이 메마른 것이 실제 해변과 맞다.
                    float dryChance = dryListA != null
                        ? EcotoneDryChanceMax * (1f - eco) * (1f - eco)
                        : 0f;
                    bool isDryTuft = dryChance > 0f
                        && Hash01(ix, iz, islandSalt ^ DryTuftSalt) < dryChance;

                    // [B57] 카드 키: 전이대 바닥에서 0.60배로 낮아진다(모래에 붙은 잔풀).
                    float ecoHeight = Mathf.Lerp(EcotoneMinHeightScale, 1f, eco);

                    // 꽃 무리 지대 판정은 앵커 단위 1회(파장 ~11m 노이즈, 문턱 = 면적 상위 ~12%).
                    // 전환 자체는 카드 단위라 꽃도 다발 구조 안에서 무리 지어 핀다.
                    // 텍스처 폴백 시에는 flowerList가 null이라 이 분기 전체가 죽는다(잔디-only).
                    // [B57] 전이대에서는 꽃도 함께 준다(× eco). 마른 모래 위에 꽃밭이 피면
                    // 전이대를 만든 의미가 상쇄된다. 마른 풀 다발에는 아예 꽃이 없다.
                    float flowerRatio = 0f;
                    if (flowerListA != null && !isDryTuft)
                    {
                        float zone = LatticeNoise(x, z, FlowerWavelength, islandSalt ^ 0x68E31DA4u);
                        if (zone > FlowerZoneThreshold)
                            flowerRatio = Mathf.Lerp(FlowerRatioMin, FlowerRatioMax,
                                (zone - FlowerZoneThreshold) / (1f - FlowerZoneThreshold)) * eco;
                    }

                    for (int card = 0; card < cardCount; card++)
                    {
                        // 카드별 해시 salt: 같은 (ix, iz)에서 카드마다 독립 변주가 나오도록
                        // 카드 인덱스를 섞는다(순수 해시 - rng 소비 0은 그대로다).
                        uint cardSalt = Mix32(islandSalt + 0x68E31DA4u * (uint)(card + 1));

                        // 앵커 주변 원판 균등 산포(sqrt로 반경 편중 제거).
                        float offAngle = Hash01(ix, iz, cardSalt ^ 0x51ED270Bu) * (Mathf.PI * 2f);
                        float offRadius = Mathf.Sqrt(Hash01(ix, iz, cardSalt ^ 0x1B873593u)) * ClusterRadius;
                        float cx = x + Mathf.Cos(offAngle) * offRadius;
                        float cz = z + Mathf.Sin(offAngle) * offRadius;
                        // 카드 위치에서 높이를 다시 샘플해 경사면에서도 밑동이 뜨지 않는다.
                        float cy = SampleHeight(verts, rings, segments, radius, cx, cz);

                        // 카드 변주(전부 위치 해시): yaw 0~360° + 무작위 기울임 ±8도(축도 해시) +
                        // [v3] 이봉 스케일. 기울임 덕에 위에서 봐도 별모양 교차가 안 읽히고
                        // 다발 윗선이 들쭉날쭉해진다.
                        float yaw = Hash01(ix, iz, cardSalt ^ 0x85EBCA6Bu) * 360f;
                        float tiltDeg = (Hash01(ix, iz, cardSalt ^ 0xA511E9B3u) * 2f - 1f) * MaxTiltDegrees;
                        float axisAngle = Hash01(ix, iz, cardSalt ^ 0x2545F491u) * (Mathf.PI * 2f);
                        var tiltAxis = new Vector3(Mathf.Cos(axisAngle), 0f, Mathf.Sin(axisAngle));
                        var rotation = Quaternion.AngleAxis(tiltDeg, tiltAxis)
                            * Quaternion.Euler(0f, yaw, 0f);

                        float hY = Hash01(ix, iz, cardSalt ^ 0xC2B2AE35u);
                        float hXZ = Hash01(ix, iz, cardSalt ^ 0x27D4EB2Fu);
                        // [B54] 아키타입 높이 배율. 습지섬 1.40(키 큰 잔디) / 화산·산호 0.85.
                        // 변주 구조(이봉 스케일)는 그대로 두고 결과에만 곱한다 - Tropical은 1.00이라
                        // 아키타입 도입 이전과 비트 단위로 같은 행렬이 나온다.
                        // [B57] ecoHeight(전이대 키 감쇠)가 여기 한 곳에 곱해진다 - 변주 구조는
                        // 그대로 두고 결과에만 곱하는 아키타입 배율과 같은 방식이다.
                        float scaleY = (isTuft
                            ? Mathf.Lerp(TuftScaleYMin, TuftScaleYMax, hY)
                            : Mathf.Lerp(UnderScaleYMin, UnderScaleYMax, hY)) * grassHeightScale * ecoHeight;
                        float scaleXZ = isTuft
                            ? Mathf.Lerp(TuftScaleXZMin, TuftScaleXZMax, hXZ)
                            : Mathf.Lerp(UnderScaleXZMin, UnderScaleXZMax, hXZ);

                        var worldPos = new Vector3(
                            center.x + cx, center.y + cy - RootSinkDepth, center.z + cz);
                        var matrix = Matrix4x4.TRS(worldPos, rotation,
                            new Vector3(scaleXZ, scaleY, scaleXZ));

                        bool isFlower = flowerRatio > 0f
                            && Hash01(ix, iz, cardSalt ^ 0xB5297A4Du) < flowerRatio;

                        // LOD 그룹 배정도 카드별 위치 해시(프레임에서 절대 재계산하지 않는다).
                        bool inGroupA = Hash01(ix, iz, cardSalt ^ 0x165667B1u) < 0.5f;
                        if (isFlower)
                            (inGroupA ? flowerListA : flowerListB).Add(matrix);
                        else if (isDryTuft)
                            (inGroupA ? dryListA : dryListB).Add(matrix);
                        else
                            (inGroupA ? listA : listB).Add(matrix);

                        if (cy < minY) minY = cy;
                        if (cy > maxY) maxY = cy;
                    }
                }
            }

            int total = listA.Count + listB.Count
                + (dryListA != null ? dryListA.Count + dryListB.Count : 0)
                + (flowerListA != null ? flowerListA.Count + flowerListB.Count : 0);
            if (total == 0)
                return;

            // worldBounds는 섬 단위 하나. 높이는 최대 신장 + 바람 진폭 여유 - [v3] 다발 스케일
            // 상한 1.5 × 폴백 blade 높이 1m = 1.5m까지 덮도록 여유를 1.65로 올렸다(카드 0.65m는
            // 0.975m라 원래도 여유. 컬링 바운즈만 커질 뿐 배치·판정은 불변).
            float boundsMinY = center.y + minY - RootSinkDepth - 0.2f;
            float boundsMaxY = center.y + maxY + 1.65f;
            var bounds = new Bounds(
                new Vector3(center.x, (boundsMinY + boundsMaxY) * 0.5f, center.z),
                new Vector3(radius * 2f + 2f, boundsMaxY - boundsMinY, radius * 2f + 2f));

            registry.Add(new GrassRecord
            {
                root = islandTransform,
                center = center,
                radius = radius,
                groupA = listA.ToArray(),
                groupB = listB.ToArray(),
                dryA = dryListA != null && dryListA.Count > 0 ? dryListA.ToArray() : null,
                dryB = dryListB != null && dryListB.Count > 0 ? dryListB.ToArray() : null,
                flowerA = flowerListA != null && flowerListA.Count > 0 ? flowerListA.ToArray() : null,
                flowerB = flowerListB != null && flowerListB.Count > 0 ? flowerListB.ToArray() : null,
                bounds = bounds,
                // [B54] 뿌리색은 이 섬의 지면색에서 온다. 배치 시 1회 잡아 두고 프레임에서는
                // 참조만 RenderParams에 꽂는다(프레임당 할당 0 계약 유지).
                rootProps = RootColorBlockFor(archetype),
            });
        }

        /// <summary>
        /// [불시착 현장] 지정한 섬의 잔디/꽃 인스턴스 중 회전 박스 존 안의 카드를 제거한다.
        /// CrashSiteSculptor가 시작 섬 트렌치 존을 비울 때 1회 호출한다.
        ///
        ///  · 빌드 로직/해시/다른 섬 레코드는 건드리지 않는다 - 해당 섬 레코드의 배열만 압축 교체한다.
        ///    렌더 루프(RenderGroup)는 배열 Length 기반이라 짧아진 배열을 그대로 소화한다
        ///    (빈 배열이면 루프가 0회 돌 뿐이다).
        ///  · 난수 소비 0(순수 기하 판정) - 호출 시점과 무관하게 재현성이 유지된다.
        ///  · 인스턴스 위치는 TRS 행렬의 이동 성분(GetColumn(3), 월드 좌표)에서 읽는다.
        /// </summary>
        /// <param name="islandRoot">섬 지형 오브젝트("Island_{id}_{size}")의 트랜스폼(레코드 키).</param>
        /// <param name="boxCenter">존 박스 중심(월드).</param>
        /// <param name="boxRotation">존 박스 회전(잔해 yaw).</param>
        /// <param name="boxHalfExtents">존 박스 반크기(m).</param>
        public static void RemoveInstancesInOrientedBox(Transform islandRoot, Vector3 boxCenter,
            Quaternion boxRotation, Vector3 boxHalfExtents)
        {
            if (islandRoot == null)
                return;

            Quaternion inverseRotation = Quaternion.Inverse(boxRotation);
            for (int i = 0; i < registry.Count; i++)
            {
                GrassRecord record = registry[i];
                if (record.root != islandRoot)
                    continue;

                record.groupA = FilterOutsideBox(record.groupA, boxCenter, inverseRotation, boxHalfExtents);
                record.groupB = FilterOutsideBox(record.groupB, boxCenter, inverseRotation, boxHalfExtents);
                record.dryA = FilterOutsideBox(record.dryA, boxCenter, inverseRotation, boxHalfExtents);
                record.dryB = FilterOutsideBox(record.dryB, boxCenter, inverseRotation, boxHalfExtents);
                record.flowerA = FilterOutsideBox(record.flowerA, boxCenter, inverseRotation, boxHalfExtents);
                record.flowerB = FilterOutsideBox(record.flowerB, boxCenter, inverseRotation, boxHalfExtents);
                return; // 레코드는 섬당 하나다(Build의 중복 등록 가드).
            }
        }

        /// <summary>
        /// [B54] 섬 아키타입 → 잔디 뿌리색 주입 블록. 아키타입당 1개를 만들어 캐시하고 같은 유형의
        /// 섬들이 공유한다(최대 8개). 머티리얼은 월드 공유 1장 그대로 - 장수를 늘리지 않는다.
        ///
        /// [왜 MaterialPropertyBlock이 이 경로에서 유효한가]
        /// 렌더 배치 단위가 이미 **섬별**이다 - GrassFieldDriver.LateUpdate가 레코드마다 RenderParams를
        /// 새로 만들어 Graphics.RenderMeshInstanced를 부르므로, RenderParams.matProps에 섬의 블록을
        /// 꽂으면 그 섬의 드로우에만 _RootColor가 적용된다(블록은 배치 전체에 균일하게 걸린다 -
        /// 인스턴스마다 다른 값이 필요한 것이 아니므로 스칼라 주입으로 충분하다). 셰이더의
        /// _RootColor는 UnityPerMaterial CBUFFER의 평범한 머티리얼 프로퍼티라 블록이 그대로 덮는다.
        ///
        /// [Tropical이 null인 이유 - 회귀 안전장치]
        /// 머티리얼에 구워 둔 기본값 (0.30, 0.37, 0.17)이 곧 v4가 고른 열대 뿌리색이다. 열대 섬은
        /// 블록을 아예 달지 않으므로 matProps == null → 머티리얼 값 그대로 = 아키타입 도입 이전과
        /// **비트 단위로 같은 렌더**다. (표의 Tropical.groundColor × 0.56 = (0.30296, 0.36904, 0.1736)
        /// 으로 기본값과 소수 둘째 자리에서 갈리는데, 그 반올림 차를 열대 섬에 새로 태우지 않는다.)
        /// </summary>
        private static MaterialPropertyBlock RootColorBlockFor(IslandArchetypeProfile profile)
        {
            if (profile.archetype == IslandArchetype.Tropical)
                return null;

            int index = (int)profile.archetype;
            if ((uint)index >= (uint)IslandArchetypes.Count)
                return null; // 알 수 없는 값은 열대 기본값으로 폴백(IslandArchetypes.Get과 같은 관례)

            if (rootColorBlocks == null)
                rootColorBlocks = new MaterialPropertyBlock[IslandArchetypes.Count];
            if (rootColorBlocks[index] != null)
                return rootColorBlocks[index];

            Color ground = profile.groundColor;
            var root = new Color(
                ground.r * RootColorGroundFactor,
                ground.g * RootColorGroundFactor,
                ground.b * RootColorGroundFactor,
                1f); // 알파는 1 고정 - 셰이더가 _RootColor.rgb만 읽지만 계약 형태를 맞춘다.

            var block = new MaterialPropertyBlock();
            block.SetColor(rootColorId, root);
            rootColorBlocks[index] = block;
            return block;
        }

        /// <summary>박스 밖 인스턴스만 남긴 압축 배열을 돌려준다(제거된 것이 없으면 원본 참조 그대로).</summary>
        private static Matrix4x4[] FilterOutsideBox(Matrix4x4[] source, Vector3 boxCenter,
            Quaternion inverseRotation, Vector3 halfExtents)
        {
            if (source == null || source.Length == 0)
                return source;

            var kept = new List<Matrix4x4>(source.Length);
            for (int i = 0; i < source.Length; i++)
            {
                Vector3 position = source[i].GetColumn(3);
                Vector3 local = inverseRotation * (position - boxCenter);
                bool inside = Mathf.Abs(local.x) <= halfExtents.x
                    && Mathf.Abs(local.y) <= halfExtents.y
                    && Mathf.Abs(local.z) <= halfExtents.z;
                if (!inside)
                    kept.Add(source[i]);
            }

            return kept.Count == source.Length ? source : kept.ToArray();
        }

        /// <summary>
        /// 격자 셀 (ix, iz) 하나를 초지 후보로 평가한다. 지터 위치가 산포 원 안이고 지형 높이가
        /// **전이대 바닥**(경계선 - ecoBelow) 위면 true와 함께 섬 로컬 (x, z, y)와
        /// 전이대 계수 eco(0~1)를 돌려준다. [B57] 예전에는 경계선 한 줄을 넘는지만 봤다.
        /// 1차(계수)와 2차(생성) 통과가 반드시 같은 판정을 내려야 하므로 한 함수로 공유한다.
        /// </summary>
        /// <param name="grassLineBase">[B52/B57] IslandMeshGenerator.GrassLineHeightFromT(t).
        /// Build가 섬당 1회 계산해 넘긴다(캡을 자르는 경계선과 단일 소스).</param>
        /// <param name="eco">[B57] 전이대 계수 0~1(0 = 경계 아래 끝, 1 = 완전한 초지).
        /// 밀도·카드 키·마른 풀 비율·꽃 비율이 전부 이 한 값에서 나온다.</param>
        private static bool TryGetGrassCandidate(int ix, int iz, uint islandSalt, Vector3[] verts,
            int rings, int segments, float radius, float maxPlaceRadiusSq, float phaseA, float phaseB,
            float grassLineBase, float ecoBelow, float ecoBand,
            out float x, out float z, out float y, out float eco)
        {
            eco = 0f;
            x = ix * CellSpacing + (Hash01(ix, iz, islandSalt ^ 0x51ED270Bu) - 0.5f) * 0.9f * CellSpacing;
            z = iz * CellSpacing + (Hash01(ix, iz, islandSalt ^ 0x1B873593u) - 0.5f) * 0.9f * CellSpacing;
            y = 0f;

            float rSq = x * x + z * z;
            if (rSq > maxPlaceRadiusSq)
                return false;

            float angle = Mathf.Atan2(z, x);
            y = SampleHeightPolar(verts, rings, segments, radius, Mathf.Sqrt(rSq), angle);

            // 초지 경계: 모래 캡을 실제로 자르는 바로 그 곡선이다(IslandMeshGenerator.GrassBoundaryHeight
            // - 단일 소스. 굽이 항을 여기에 복사해 두면 한쪽만 고쳐 어긋나는 사고가 재발한다).
            float boundary = IslandMeshGenerator.GrassBoundaryHeight(x, z, grassLineBase, phaseA, phaseB);

            // [B57] 전이대: 경계선 아래 ecoBelow부터 대역 ecoBand에 걸쳐 0 → 1로 차오른다.
            // smoothstep이라 양 끝에서 기울기가 0 - 전이대 시작/끝에 밀도 단차가 생기지 않는다.
            float u = (y - (boundary - ecoBelow)) / ecoBand;
            if (u <= 0f)
                return false;
            if (u >= 1f)
            {
                eco = 1f;
                return true;
            }
            eco = u * u * (3f - 2f * u);
            return true;
        }

        // ── 저주파 격자 해시 노이즈 (군락/꽃 무리 - rng 소비 0) ────────────────────────

        /// <summary>패치 밀도 배율 0.35~1.0. 파장 ~7m 노이즈로 무성한 곳/성긴 곳을 가른다.</summary>
        private static float PatchDensity(float x, float z, uint islandSalt)
        {
            return Mathf.Lerp(PatchFloor, 1f,
                LatticeNoise(x, z, PatchWavelength, islandSalt ^ 0x7F4A7C15u));
        }

        /// <summary>
        /// 파장 wavelength의 값 노이즈 [0,1]: 격자점 해시(Hash01)를 smoothstep 이중 선형 보간.
        /// 입력이 (섬 로컬 좌표, salt)뿐인 순수 함수라 1차/2차 통과가 항상 같은 값을 본다.
        /// </summary>
        private static float LatticeNoise(float x, float z, float wavelength, uint salt)
        {
            float fx = x / wavelength;
            float fz = z / wavelength;
            int x0 = Mathf.FloorToInt(fx);
            int z0 = Mathf.FloorToInt(fz);
            float tx = fx - x0;
            float tz = fz - z0;
            tx = tx * tx * (3f - 2f * tx);
            tz = tz * tz * (3f - 2f * tz);
            float h00 = Hash01(x0, z0, salt);
            float h10 = Hash01(x0 + 1, z0, salt);
            float h01 = Hash01(x0, z0 + 1, salt);
            float h11 = Hash01(x0 + 1, z0 + 1, salt);
            return Mathf.Lerp(Mathf.Lerp(h00, h10, tx), Mathf.Lerp(h01, h11, tx), tz);
        }

        // ── 메시 정점 격자 높이 샘플러 ────────────────────────────────────────────────

        /// <summary>섬 로컬 (x, z)의 지형 높이. 극좌표로 바꿔 정점 격자를 이중 선형 보간한다.</summary>
        private static float SampleHeight(Vector3[] verts, int rings, int segments, float radius,
            float x, float z)
        {
            return SampleHeightPolar(verts, rings, segments, radius,
                Mathf.Sqrt(x * x + z * z), Mathf.Atan2(z, x));
        }

        /// <summary>
        /// (반경 r, 각도 angle)의 지형 높이. ring 0은 중심 정점(index 0), ring k(1..rings)의
        /// seg s는 verts[1+(k-1)*segments+s]다(GenerateIslandMesh의 결정적 정점 순서).
        /// </summary>
        private static float SampleHeightPolar(Vector3[] verts, int rings, int segments, float radius,
            float r, float angle)
        {
            float ringF = Mathf.Clamp(r / radius * rings, 0f, rings - 0.0001f);
            int ring0 = (int)ringF;
            float tRing = ringF - ring0;

            float twoPi = Mathf.PI * 2f;
            float a = angle - Mathf.Floor(angle / twoPi) * twoPi; // [0, 2π)
            float segF = a / twoPi * segments;
            int seg0 = (int)segF;
            if (seg0 >= segments) seg0 = 0;
            int seg1 = (seg0 + 1) % segments;
            float tSeg = segF - (int)segF;

            float h0 = RingHeight(verts, segments, ring0, seg0, seg1, tSeg);
            float h1 = RingHeight(verts, segments, ring0 + 1, seg0, seg1, tSeg);
            return Mathf.Lerp(h0, h1, tRing);
        }

        /// <summary>ring(0 = 중심 정점) 위 각도 보간 높이.</summary>
        private static float RingHeight(Vector3[] verts, int segments, int ring, int seg0, int seg1, float tSeg)
        {
            if (ring <= 0)
                return verts[0].y;
            int baseIndex = 1 + (ring - 1) * segments;
            return Mathf.Lerp(verts[baseIndex + seg0].y, verts[baseIndex + seg1].y, tSeg);
        }

        // ── 결정적 해시 (rng 소비 0 - SeabedGenerator.LatticeHash01과 같은 finalizer 계열) ──

        /// <summary>격자 정수 좌표 + salt → [0,1). System.Random/UnityEngine.Random을 일절 쓰지 않는다.</summary>
        private static float Hash01(int xi, int zi, uint salt)
        {
            unchecked
            {
                uint h = (uint)(xi * 73856093) ^ (uint)(zi * 19349663) ^ salt;
                h ^= h >> 16;
                h *= 0x7FEB352Du;
                h ^= h >> 15;
                h *= 0x846CA68Bu;
                h ^= h >> 16;
                return (h & 0xFFFFFFu) / (float)0x1000000u;
            }
        }

        /// <summary>uint 하나를 섞는다(IslandMeshGenerator.Mix32와 같은 finalizer).</summary>
        private static uint Mix32(uint h)
        {
            unchecked
            {
                h ^= h >> 16;
                h *= 0x7FEB352Du;
                h ^= h >> 15;
                h *= 0x846CA68Bu;
                h ^= h >> 16;
                return h;
            }
        }

        // ── 공유 에셋 (카드 메시 1개 + 머티리얼 2장, 월드 전체 공유) ─────────────────────

        /// <summary>
        /// 셰이더/텍스처 로드와 머티리얼/카드 메시를 준비한다. 셰이더가 없으면 false를 래치하고
        /// 이후 모든 경로가 조용히 무동작한다(계약: 폴백 렌더 없음). 텍스처(grass_card)가 없으면
        /// v1 방식(좁은 blade + 틴트만, 꽃 없음)으로 폴백한다. 필드 초기화식이 아니라 호출 시
        /// 로드한다(Unity 6.5: 필드 초기화식에서 Resources.Load 금지).
        /// </summary>
        private static bool EnsureGrassAssets()
        {
            if (shaderMissing)
                return false;
            if (grassMaterial != null && bladeMesh != null)
                return true;

            if (grassMaterial == null)
            {
                Shader shader = Resources.Load<Shader>("Shaders/MGGrass");
                if (shader == null)
                {
                    shaderMissing = true;
                    return false;
                }

                // 카드 텍스처(2×2 아틀라스). null이어도 계속 간다 - 셰이더 _BaseMap 기본값
                // white가 곧 v1 틴트 그라데이션 렌더라 잔디는 그대로 나온다(꽃만 생략).
                Texture2D cardTexture = Resources.Load<Texture2D>("Textures/grass_card");
                hasCardTexture = cardTexture != null;

                // 잔디 머티리얼: 해시 셀 선택(_CellOverride 기본 -1), 틴트 온(톤 조절용 완화 승수).
                grassMaterial = new Material(shader) { name = "MGGrassFieldMaterial" };
                grassMaterial.enableInstancing = true;
                // 틴트 기본값(계약): 뿌리/끝/마른 풀. 나머지(_WindStrength/_TrampleRadius)는 셰이더
                // Properties 기본값을 그대로 쓴다.
                // [v4] 뿌리색-지면 융합: 지형 초지색 StructureVisualBuilder.MeadowGreen
                // (0.541, 0.659, 0.310)의 ~0.56배 어두운 변주 - 카드 밑동이 MeadowGreen 지면과
                // 같은 색상각의 그늘로 읽혀 땅에 녹아든다(밑동-지면 색 틈이 도드라지던 원인 제거).
                // [B54] 이 값은 이제 **Tropical(= MeadowGreen 지면) 전용 기본값**이다. 다른 7종은
                // 섬별 MaterialPropertyBlock이 자기 지면색으로 덮어쓴다(RootColorBlockFor).
                // 머티리얼은 여전히 월드 공유 1장이다 - 아키타입마다 머티리얼을 늘리지 않는다.
                grassMaterial.SetColor("_RootColor", new Color(0.30f, 0.37f, 0.17f, 1f));
                grassMaterial.SetColor("_TipColor", new Color(0.45f, 0.62f, 0.28f, 1f));
                grassMaterial.SetColor("_DryTint", new Color(0.55f, 0.52f, 0.30f, 1f));
                // [v3] 음영: 풀끝 스페큘러 시트 + 역광 투과(셰이더 기본값과 같지만 명시 세팅이 계약).
                grassMaterial.SetFloat("_SheenStrength", 0.35f);
                grassMaterial.SetFloat("_TranslucencyStrength", 0.4f);
                if (hasCardTexture)
                {
                    grassMaterial.SetTexture("_BaseMap", cardTexture);
                    grassMaterial.SetFloat("_TintStrength", 0.65f); // 텍스처가 이미 뿌리→끝 색을 가진다
                }
                else
                {
                    grassMaterial.SetFloat("_TintStrength", 1f);    // v1 방식: 틴트가 곧 알베도
                }

                // [B57] 전이대 마른 풀 머티리얼: 아틀라스 **2번 셀(0,1) = 마른 풀** 고정.
                // 왜 별도 머티리얼인가: MGGrass의 셀 선택은 정점 셰이더의 원점 해시이고 편향
                // 파라미터가 _CellOverride(머티리얼 프로퍼티) 하나뿐이다. 퍼 인스턴스 커스텀
                // 데이터를 받지 않으므로(셰이더 헤더의 계약) 셀을 편향시키려면 머티리얼을 가르는
                // 수밖에 없다. 늘어나는 것은 전이대에 마른 다발이 실제로 있는 섬에서만 드로우콜
                // 1~2개다(카드 총수의 3~6% 수준 - 1023 배치 하나에 대개 다 들어간다).
                // 색: 뿌리/끝을 마른 풀 쪽으로 당기고 틴트를 조금 세게 건다. 아키타입 지면색
                // 블록을 걸지 않는 이유는 **마른 풀은 어느 섬에서나 짚색**이기 때문이다 -
                // 초지색을 따라가면 "초록색 마른 풀"이라는 모순이 나온다.
                if (hasCardTexture)
                {
                    dryMaterial = new Material(shader) { name = "MGGrassDryMaterial" };
                    dryMaterial.enableInstancing = true;
                    dryMaterial.SetTexture("_BaseMap", cardTexture);
                    dryMaterial.SetFloat("_CellOverride", 2f);
                    dryMaterial.SetColor("_RootColor", new Color(0.42f, 0.38f, 0.22f, 1f));
                    dryMaterial.SetColor("_TipColor", new Color(0.66f, 0.60f, 0.36f, 1f));
                    dryMaterial.SetColor("_DryTint", new Color(0.62f, 0.57f, 0.34f, 1f));
                    dryMaterial.SetFloat("_TintStrength", 0.75f);
                    dryMaterial.SetFloat("_SheenStrength", 0.28f);
                    dryMaterial.SetFloat("_TranslucencyStrength", 0.45f);
                }

                // 꽃 머티리얼: (1,1) 분홍 꽃 스파이크 셀 고정 + 틴트 오프(텍스처 원색).
                // 텍스처 폴백 시에는 만들지 않는다 - 꽃 배치 분기 자체가 죽는다.
                if (hasCardTexture)
                {
                    flowerMaterial = new Material(shader) { name = "MGGrassFlowerMaterial" };
                    flowerMaterial.enableInstancing = true;
                    flowerMaterial.SetTexture("_BaseMap", cardTexture);
                    flowerMaterial.SetFloat("_CellOverride", 3f);
                    flowerMaterial.SetFloat("_TintStrength", 0f);
                    // [v3] 꽃은 시트를 약하게(꽃잎이 번들거리면 플라스틱 느낌), 투과는 더 세게
                    // (얇은 꽃잎이 역광에서 더 잘 비친다).
                    flowerMaterial.SetFloat("_SheenStrength", 0.2f);
                    flowerMaterial.SetFloat("_TranslucencyStrength", 0.5f);
                }

                windTimeId = Shader.PropertyToID("_MG_WindTime");
                playerPosId = Shader.PropertyToID("_MG_PlayerPos");
                rootColorId = Shader.PropertyToID("_RootColor");
            }

            if (bladeMesh == null)
                bladeMesh = hasCardTexture ? CreateCardMesh() : CreateBladeMesh();
            return true;
        }

        /// <summary>
        /// 카드 메시([v4] 규격): 별모양 쿼드 3장(60도 간격) = 정점 12개·삼각형 6개.
        /// 폭 0.72m·높이 0.70m·피벗 밑동, UV.y 0(뿌리)~1(끝). 폭이 격자 간격(0.42m)보다 넓어
        /// 이웃 카드가 항상 겹치고, 3장 별모양은 어느 각도(위에서 포함)에서도 X자 교차가
        /// 안 읽힌다. Cull Off 셰이더라 단면이면 충분하고, 높이 변주는 인스턴스 행렬 스케일 몫이다.
        /// </summary>
        private static Mesh CreateCardMesh()
        {
            return CreateQuadFanMesh("GrassCard", 0.36f, 0.70f, 3);
        }

        /// <summary>
        /// v1 blade 메시(텍스처 폴백 규격): 교차 쿼드 2장, 폭 0.14m·높이 1m. grass_card가 없을 때
        /// 틴트 그라데이션만으로 그리던 v1 모양을 그대로 유지한다.
        /// </summary>
        private static Mesh CreateBladeMesh()
        {
            return CreateQuadFanMesh("GrassBlade", 0.07f, 1f, 2);
        }

        /// <summary>
        /// 세로 쿼드 planeCount장을 Y축 기준 180°/planeCount 간격으로 교차시킨 메시 공용 생성기.
        /// (2장 = 기존 십자 교차, 3장 = 60도 별모양.) 피벗 밑동, UV.y 0(뿌리)~1(끝).
        /// </summary>
        private static Mesh CreateQuadFanMesh(string name, float halfWidth, float height, int planeCount)
        {
            var mesh = new Mesh { name = name };

            var vertices = new Vector3[planeCount * 4];
            var uv = new Vector2[planeCount * 4];
            var normals = new Vector3[planeCount * 4];
            var triangles = new int[planeCount * 6];

            for (int p = 0; p < planeCount; p++)
            {
                // 쿼드 p: XY 평면을 Y축으로 p × (180°/planeCount) 돌린 것. 가로 방향
                // (cos, 0, -sin), 법선 (sin, 0, cos) - p = 0이면 기존 쿼드 1과 동일하다.
                float a = Mathf.PI / planeCount * p;
                var right = new Vector3(Mathf.Cos(a), 0f, -Mathf.Sin(a));
                var normal = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
                var up = new Vector3(0f, height, 0f);

                int v = p * 4;
                vertices[v + 0] = -right * halfWidth;
                vertices[v + 1] = right * halfWidth;
                vertices[v + 2] = -right * halfWidth + up;
                vertices[v + 3] = right * halfWidth + up;
                uv[v + 0] = new Vector2(0f, 0f);
                uv[v + 1] = new Vector2(1f, 0f);
                uv[v + 2] = new Vector2(0f, 1f);
                uv[v + 3] = new Vector2(1f, 1f);
                normals[v + 0] = normal;
                normals[v + 1] = normal;
                normals[v + 2] = normal;
                normals[v + 3] = normal;

                int t = p * 6;
                triangles[t + 0] = v + 0;
                triangles[t + 1] = v + 2;
                triangles[t + 2] = v + 3;
                triangles[t + 3] = v + 0;
                triangles[t + 4] = v + 3;
                triangles[t + 5] = v + 1;
            }

            mesh.vertices = vertices;
            mesh.uv = uv;
            mesh.normals = normals;
            mesh.triangles = triangles;
            // 바람 진폭(0.12m)·스케일 상한을 덮는 여유 바운즈. 컬링은 어차피
            // RenderParams.worldBounds(섬 단위)가 담당하므로 넉넉해도 비용이 없다.
            mesh.bounds = new Bounds(new Vector3(0f, height * 0.6f, 0f),
                new Vector3(halfWidth * 2f + 0.5f, height * 1.5f, halfWidth * 2f + 0.5f));
            return mesh;
        }

        // ═══════════════════════════════════════════════════════════════════════════
        //  렌더 드라이버 (매 프레임)
        // ═══════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// 씬마다 자동 생성되는 렌더 드라이버. UnderwaterAmbience/AtmospherePostFX와 같은
        /// SubsystemRegistration + sceneLoaded 부트스트랩 패턴이다(씬 수정 없음, 재시작 안전).
        /// 렌더 상태(레지스트리/메시/머티리얼)는 정적이므로 이 컴포넌트는 순수한 프레임 펌프다.
        /// SeabedGenerator.SeabedBatchLogger처럼 중첩 private MonoBehaviour로 둔다(AddComponent 전용).
        /// </summary>
        private sealed class GrassFieldDriver : MonoBehaviour
        {
            private Camera targetCamera;
            private PlayerController player;

            [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
            private static void Bootstrap()
            {
                SceneManager.sceneLoaded += (scene, mode) =>
                {
                    if (FindAnyObjectByType<GrassFieldDriver>() != null)
                        return;

                    var go = new GameObject("GrassFieldDriver");
                    go.AddComponent<GrassFieldDriver>();
                };
            }

            /// <summary>
            /// LateUpdate인 이유: 플레이어/카메라 이동(Update)이 끝난 뒤의 최종 위치로
            /// _MG_PlayerPos와 거리 컷을 계산해야 잔디 밟힘이 한 프레임 늦지 않는다
            /// (UnderwaterAmbience의 "마지막 승자" 관례와 같은 자리).
            /// </summary>
            private void LateUpdate()
            {
                if (registry.Count == 0 || grassMaterial == null || bladeMesh == null)
                    return; // 셰이더 실패 포함 - 조용히 무동작(계약)

                // 카메라는 파괴/재생성될 수 있으므로 null이면 다시 집는다(파괴된 오브젝트 == null 규칙).
                if (targetCamera == null)
                {
                    targetCamera = Camera.main;
                    if (targetCamera == null)
                        return;
                }

                // 플레이어 1회 캐시·null 저빈도 재시도(UnderwaterAmbience가 WorldMapManager를 찾는
                // 것과 같은 규칙 - 정상 경로에서 탐색 비용/할당 0).
                if (player == null && Time.frameCount % 60 == 0)
                    player = FindAnyObjectByType<PlayerController>();

                // 매 프레임 주입(계약). 잔디/꽃 머티리얼 둘 다 - 꽃도 같은 바람/밟힘을 받는다.
                // 플레이어를 아직 못 찾았으면 _MG_PlayerPos는 셰이더 기본값(지하 -10000)에 남아
                // 밟힘이 없다 - 올바른 무동작이다.
                float now = Time.time;
                grassMaterial.SetFloat(windTimeId, now);
                if (dryMaterial != null)
                    dryMaterial.SetFloat(windTimeId, now);
                if (flowerMaterial != null)
                    flowerMaterial.SetFloat(windTimeId, now);
                if (player != null)
                {
                    Vector3 p = player.transform.position;
                    var packed = new Vector4(p.x, p.y, p.z, 0f);
                    grassMaterial.SetVector(playerPosId, packed);
                    if (dryMaterial != null)
                        dryMaterial.SetVector(playerPosId, packed);
                    if (flowerMaterial != null)
                        flowerMaterial.SetVector(playerPosId, packed);
                }

                Vector3 camPos = targetCamera.transform.position;

                for (int i = registry.Count - 1; i >= 0; i--)
                {
                    GrassRecord record = registry[i];

                    // 섬 파괴(RegenerateWorld) 시 정리: 유니티 == 오버로드로 감지해 레코드를 버린다
                    // (행렬 배열은 관리 메모리라 이 제거로 GC 대상이 된다 - SeabedGenerator와 같은 규칙).
                    if (record.root == null)
                    {
                        registry.RemoveAt(i);
                        continue;
                    }

                    // RegenerateWorld는 Destroy 전에 SetActive(false)를 먼저 건다(WorldMapManager).
                    // 비활성 섬의 잔디를 이번 프레임에 그리면 유령 잔디가 남으므로 함께 쉰다.
                    if (!record.root.gameObject.activeInHierarchy)
                        continue;

                    // 거리 컷은 섬당 1회: 카메라 → 섬 테두리(중심 거리 - R).
                    float edgeDistance = Vector3.Distance(camPos, record.center) - record.radius;
                    if (edgeDistance > MaxRenderDistance)
                        continue;

                    bool fullDetail = edgeDistance <= FullDetailDistance;

                    // [B54] matProps에 섬의 뿌리색 블록을 꽂는다. 배치 단위가 섬이라 이 드로우에만
                    // 걸린다(열대 섬은 null → 머티리얼 기본값 그대로). 머티리얼은 공유 1장이고
                    // 블록은 배치 때 만들어 둔 것이라 프레임당 할당은 여전히 0이다.
                    var rparams = new RenderParams(grassMaterial)
                    {
                        worldBounds = record.bounds,
                        shadowCastingMode = ShadowCastingMode.Off,
                        receiveShadows = true,
                        matProps = record.rootProps,
                    };

                    // 간이 LOD: 미리 갈라 둔 그룹을 거리로 선택만 한다(인스턴스 단위 재계산 없음).
                    RenderGroup(rparams, record.groupA);
                    if (fullDetail)
                        RenderGroup(rparams, record.groupB);

                    // [B57] 전이대 마른 풀. 배열이 없으면(전이대에 마른 다발이 없는 섬) 드로우콜 0이다.
                    // 뿌리색 블록을 걸지 않는 이유는 머티리얼 생성부 주석에 있다(마른 풀은 짚색 고정).
                    if (dryMaterial != null && (record.dryA != null || record.dryB != null))
                    {
                        var dryParams = new RenderParams(dryMaterial)
                        {
                            worldBounds = record.bounds,
                            shadowCastingMode = ShadowCastingMode.Off,
                            receiveShadows = true,
                        };
                        RenderGroup(dryParams, record.dryA);
                        if (fullDetail)
                            RenderGroup(dryParams, record.dryB);
                    }

                    // 꽃 배치: 별도 머티리얼((1,1) 셀 고정·틴트 오프), LOD 분할은 잔디와 동일 규칙.
                    // [B54] 꽃에는 뿌리색 블록을 걸지 않는다 - _TintStrength가 0이라 _RootColor가
                    // 결과에 아예 들어가지 않는다(꽃은 텍스처 원색이 계약이다).
                    if (flowerMaterial != null && (record.flowerA != null || record.flowerB != null))
                    {
                        var flowerParams = new RenderParams(flowerMaterial)
                        {
                            worldBounds = record.bounds,
                            shadowCastingMode = ShadowCastingMode.Off,
                            receiveShadows = true,
                        };
                        RenderGroup(flowerParams, record.flowerA);
                        if (fullDetail)
                            RenderGroup(flowerParams, record.flowerB);
                    }
                }
            }

            /// <summary>행렬 배열을 1023개 단위로 잘라 그린다. 배열 재사용 - 프레임당 할당 0.</summary>
            private static void RenderGroup(in RenderParams rparams, Matrix4x4[] matrices)
            {
                if (matrices == null)
                    return;
                for (int start = 0; start < matrices.Length; start += InstancesPerBatch)
                {
                    Graphics.RenderMeshInstanced(rparams, bladeMesh, 0, matrices,
                        Mathf.Min(InstancesPerBatch, matrices.Length - start), start);
                }
            }
        }
    }
}
