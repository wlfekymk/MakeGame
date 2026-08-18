using UnityEngine;

namespace MakeGame.Data
{
    /// <summary>
    /// 섬 유형(아키타입) 8종. "섬마다 확연히 다른 인상"을 만드는 단일 축이다.
    ///
    /// [왜 필요했나] B47(형태 프로파일 8종)이 **윤곽과 높이**는 갈라 놓았지만, 지면색·모래색·잔디선·
    /// 초목 밀도·바위 양은 전 섬 공통이었다. 그래서 모양이 달라도 "같은 색·같은 밀도의 열대섬"으로
    /// 읽혔고, 사용자 신고("수중·육지 바위 다양성, 50%가 바위인 섬")가 그 지점을 찔렀다.
    /// 아키타입은 형태(프로파일)와 **직교하는** 축이다 - 같은 초승달 프로파일이라도 화산암섬이면
    /// 검은 암반에 초목이 희박하고, 정글섬이면 짙은 초록에 야자가 빽빽하다.
    ///
    /// [형태 프로파일(IslandMeshGenerator.SelectShapeProfile)과의 관계]
    ///  · 프로파일 = 실루엣/높이장(단봉·쌍봉·초승달·수로·석호·능선·고원).
    ///  · 아키타입 = 표면 성질(색·식생·암석량) + 높이장에 곱하는 **계수 3개**(heightScaleMul /
    ///    plateauPowMul / terrainNoiseMul). 프로파일 식 자체는 한 줄도 바뀌지 않는다.
    /// 둘은 서로 다른 해시 salt를 쓰므로 8×8 = 64가지 조합이 나온다.
    /// </summary>
    public enum IslandArchetype
    {
        /// <summary>열대 일반. 현재(아키타입 도입 이전) 기본값과 **완전히 동일한** 파라미터다.</summary>
        Tropical,

        /// <summary>바위섬. 지면의 약 50%가 암석 피복, 초목 대폭 감소, 회갈색 지면.</summary>
        Rocky,

        /// <summary>백사장섬. 모래 비율이 크다(잔디선 매우 높게 = 잔디 적게), 야자 성기게, 유목 많이.</summary>
        Sandy,

        /// <summary>정글섬. 초목 밀집(야자·덤불 대폭 증가), 잔디선 낮게(잔디 많게), 짙은 초록.</summary>
        Jungle,

        /// <summary>화산암섬. 검은 암반 지면, 주상절리/첨탑 계열 바위, 초목 희박, 모래도 어둡게.</summary>
        Volcanic,

        /// <summary>산호섬(환초). 낮고 평평, 백사장 넓게, 초목 최소, 얕은 해안.</summary>
        Atoll,

        /// <summary>습지섬. 짙은 녹갈색 지면, 잔디 매우 많고 키 큼, 덤불 많음, 바위 적음.</summary>
        Marsh,

        /// <summary>절벽섬. 가장자리 고도가 급하고(절벽 계열 바위 다수) 상부는 평탄한 초지.</summary>
        Cliff
    }

    /// <summary>
    /// 아키타입 하나의 파라미터 묶음. **읽기 전용 정적 표**(IslandArchetypes.Table)의 원소이며,
    /// struct라 Get()이 복사본을 돌려준다 - 호출부가 실수로 표를 오염시킬 수 없다
    /// (IslandMeshGenerator.IslandShapeProfile은 class라 호출부가 필드를 덮어쓰는데, 그쪽은
    /// 섬마다 새로 만드는 일회용이라 괜찮다. 이 표는 월드 전체가 공유하므로 값 타입으로 둔다).
    ///
    /// 배율 계열은 전부 "Tropical = 1.0(또는 무변화)"을 기준으로 한다. 즉 Tropical 섬에서는
    /// 이 표를 태워도 아키타입 도입 이전과 **수치가 한 비트도 달라지지 않는다**(회귀 안전장치).
    /// </summary>
    public struct IslandArchetypeProfile
    {
        /// <summary>이 항목이 서술하는 아키타입.</summary>
        public IslandArchetype archetype;

        /// <summary>한국어 표시명(로그·디버그 HUD용).</summary>
        public string displayName;

        // ── 초목 개수 배율 (IslandMeshGenerator.Vegetation의 palm/bush/tuft 개수 결정식에 곱한다) ──
        /// <summary>야자수 그루 수 배율.</summary>
        public float palmScale;
        /// <summary>덤불 개수 배율.</summary>
        public float bushScale;
        /// <summary>풀포기(프리미티브 tuft) 개수 배율. 잔디 필드(GrassFieldSystem)와는 별개다.</summary>
        public float tuftScale;

        /// <summary>
        /// 섬당 초목 인스턴스 상한(MaxVegetationInstancesPerIsland)에 곱하는 배율.
        /// 이것이 없으면 특대 섬은 상한(284)에 이미 정확히 닿아 있어, 정글섬의 배율이 트림에
        /// 그대로 되깎여 열대섬과 같은 밀도가 된다(상한이 "살아 있는 가드"라는 뜻이 여기서 발목을 잡는다).
        /// 렌더러 예산: 정글 1.50 = 426개, 화산/산호 0.45 = 128개.
        /// </summary>
        public float vegetationCapScale;

        /// <summary>바위(바위 무리 + 대형 석재 4계열) 개수 배율.</summary>
        public float rockScale;

        /// <summary>표류물(유목·궤짝) 개수 배율. 백사장섬의 "유목 많이"가 이 값이다.</summary>
        public float driftScale;

        /// <summary>
        /// ★ 이번 웨이브에서는 **값만 정의**한다(실제 배치 없음) ★
        /// 지면 중 암석(암반 노두)이 덮는 목표 면적 비율(0~1). 다음 웨이브의 암석 피복 배치기가
        /// 이 값을 읽어 "지면 캡을 암반 캡으로 치환할 삼각형 비율"로 쓴다.
        /// 사용자 요구의 "섬의 50%가 바위로 이루어진 섬" = Rocky의 0.50이 이 필드다.
        /// </summary>
        public float rockCoverage;

        // ── 잔디선(모래:풀 경계) 파라미터 ──────────────────────────────────────────
        // t가 클수록 경계선이 높다 = 모래가 넓고 잔디가 적다(IslandMeshGenerator.GrassLineHeightFromT).
        /// <summary>섬 해시 t에 곱하는 계수(변주 폭 조절. 1이면 원본 스펙트럼 그대로).</summary>
        public float grassLineTScale;
        /// <summary>섬 해시 t에 더하는 오프셋. +면 모래섬 쪽, -면 초지 쪽으로 민다.</summary>
        public float grassLineTOffset;
        /// <summary>오프셋 적용 후 t의 하한.</summary>
        public float grassLineTMin;
        /// <summary>오프셋 적용 후 t의 상한.</summary>
        public float grassLineTMax;

        /// <summary>잔디 필드 목표 개수 배율(GrassFieldSystem.Build). t에 의한 감소와 **곱해진다**.</summary>
        public float grassDensityScale;

        /// <summary>잔디 카드 높이(scaleY) 배율. 습지섬의 "키 큰 잔디"가 이 값이다.</summary>
        public float grassHeightScale;

        // ── 색 ────────────────────────────────────────────────────────────────────
        /// <summary>지면(초지 상당) 색. Tropical은 StructureVisualBuilder.MeadowGreen과 같은 값이다.</summary>
        public Color groundColor;

        /// <summary>모래 기준색. Tropical은 StructureVisualBuilder.IslandSand와 같은 값이다.
        /// 3단 모래 캡(마른/축축한/젖은)의 명도 계단(1.0 / 0.88 / 0.78)은 이 색 위에 그대로 얹힌다.</summary>
        public Color sandColor;

        // ── 고도 프로파일 계수 (SculptHeight의 항을 늘리지 않는 최소 개입) ─────────
        /// <summary>IslandShapeProfile.heightScale에 곱한다. 섬이 통째로 높아지거나 낮아진다.</summary>
        public float heightScaleMul;

        /// <summary>
        /// IslandShapeProfile.plateauPow에 곱한다. **가장자리 급경사도**가 이 값이다.
        /// y = maxH·hs·cos(q·π/2)^pow 이므로 pow가 작을수록 정상부가 평평해지고 q=1(해안) 근처에서
        /// 기울기가 급격히 커진다 - 절벽섬(0.60)이 이것을 쓰고, 산호섬(0.65)은 같은 평탄화를
        /// 낮은 heightScaleMul(0.72)과 묶어 "낮고 평평한" 섬을 만든다.
        /// (pow를 내리면 중간 반경의 y가 **올라가므로** 육지 면적은 줄지 않는다 - B47이 튜닝한
        ///  "0.8R 안 육지 비율 ≥ 70%" 제약이 어느 아키타입에서도 나빠지지 않는다.)
        /// </summary>
        public float plateauPowMul;

        /// <summary>IslandShapeProfile.noiseAmp에 곱한다. 암석계(Rocky/Volcanic)는 거칠게, 산호섬은 매끈하게.</summary>
        public float terrainNoiseMul;
    }

    /// <summary>
    /// 아키타입 파라미터 표 + **결정적 배정 규칙**.
    ///
    /// ★ 난수 소비 0 ★ (IslandMeshGenerator의 B46/B47/B52가 지킨 것과 같은 제약, 같은 방법)
    /// 배정은 (worldSeed, islandId, size)만 입력으로 받는 **순수 해시**다. System.Random을 만들지도
    /// 소비하지도 않으므로, 자원·위험요소·초목의 추첨 순서가 한 칸도 밀리지 않는다.
    ///
    /// ★ 세이브 포맷 불변 ★ 아키타입은 시드에서 언제든 재계산되므로 저장하지 않는다(저장 필드 추가 금지).
    /// 같은 시드를 다시 열면 같은 섬이 항상 같은 아키타입을 받는다.
    ///
    /// [사용자 승인 사항] 이 배치는 **기존 월드의 재현성을 의도적으로 재구성한다**(새 게임 필요).
    /// 초목 개수가 아키타입 배율로 바뀌면 초목 전용 rng 스트림의 소비량이 달라져 같은 worldSeed라도
    /// 숲 배치가 달라지기 때문이다. 사용자가 명시적으로 승인했다. **같은 시드 안에서의 결정성은 그대로다.**
    /// </summary>
    public static class IslandArchetypes
    {
        /// <summary>아키타입 개수(enum 길이와 반드시 일치).</summary>
        public const int Count = 8;

        // ── 파라미터 표 ────────────────────────────────────────────────────────────
        // 행 순서는 enum 순서와 정확히 같아야 한다(Get이 (int)archetype으로 인덱싱한다). 각 항목이
        // archetype 필드에 자기 자신을 다시 적어 두므로, 순서가 어긋나면 Get(x).archetype != x 로 드러난다.
        private static readonly IslandArchetypeProfile[] Table =
        {
            // 0. Tropical — 기준선. 모든 배율 1.0, 색은 기존 상수와 같은 값(회귀 안전장치).
            new IslandArchetypeProfile
            {
                archetype = IslandArchetype.Tropical, displayName = "열대",
                palmScale = 1.00f, bushScale = 1.00f, tuftScale = 1.00f,
                vegetationCapScale = 1.00f, driftScale = 1.00f, rockScale = 1.00f, rockCoverage = 0.08f,
                grassLineTScale = 1.00f, grassLineTOffset = 0.00f,
                grassLineTMin = 0.00f, grassLineTMax = 1.00f,
                grassDensityScale = 1.00f, grassHeightScale = 1.00f,
                groundColor = new Color(0.541f, 0.659f, 0.310f), // = MeadowGreen #8AA84F
                sandColor = new Color(0.761f, 0.698f, 0.502f),   // = IslandSand  #C2B280
                heightScaleMul = 1.00f, plateauPowMul = 1.00f, terrainNoiseMul = 1.00f,
            },

            // 1. Rocky — "섬의 50%가 바위". 초목을 반 이하로 줄이고 바위를 2배 이상 늘린다.
            new IslandArchetypeProfile
            {
                archetype = IslandArchetype.Rocky, displayName = "바위",
                palmScale = 0.35f, bushScale = 0.45f, tuftScale = 0.50f,
                vegetationCapScale = 0.55f, driftScale = 0.90f, rockScale = 2.20f, rockCoverage = 0.50f,
                grassLineTScale = 1.00f, grassLineTOffset = 0.15f,
                grassLineTMin = 0.15f, grassLineTMax = 1.00f,
                grassDensityScale = 0.55f, grassHeightScale = 0.90f,
                groundColor = new Color(0.541f, 0.498f, 0.431f), // 회갈 #8A7F6E
                sandColor = new Color(0.710f, 0.675f, 0.576f),   // 자갈 섞인 회모래 #B5AC93
                heightScaleMul = 1.05f, plateauPowMul = 1.00f, terrainNoiseMul = 1.30f,
            },

            // 2. Sandy — 백사장섬. 잔디선을 크게 밀어 올려 섬 대부분이 모래가 되게 한다.
            new IslandArchetypeProfile
            {
                archetype = IslandArchetype.Sandy, displayName = "백사장",
                palmScale = 0.55f, bushScale = 0.50f, tuftScale = 0.60f,
                vegetationCapScale = 0.60f, driftScale = 1.80f, rockScale = 0.80f, rockCoverage = 0.10f,
                grassLineTScale = 0.80f, grassLineTOffset = 0.30f,
                grassLineTMin = 0.40f, grassLineTMax = 1.00f,
                grassDensityScale = 0.50f, grassHeightScale = 0.90f,
                groundColor = new Color(0.659f, 0.682f, 0.447f), // 옅은 황록 #A8AE72
                sandColor = new Color(0.839f, 0.780f, 0.604f),   // 밝은 백사 #D6C79A
                heightScaleMul = 0.85f, plateauPowMul = 0.75f, terrainNoiseMul = 0.90f,
            },

            // 3. Jungle — 초목 밀집. 상한(vegetationCapScale)을 같이 올리지 않으면 특대 섬에서
            //    트림에 되깎여 열대섬과 구분되지 않는다(위 필드 주석).
            new IslandArchetypeProfile
            {
                archetype = IslandArchetype.Jungle, displayName = "정글",
                palmScale = 1.80f, bushScale = 2.00f, tuftScale = 1.50f,
                vegetationCapScale = 1.50f, driftScale = 0.80f, rockScale = 0.70f, rockCoverage = 0.05f,
                grassLineTScale = 0.90f, grassLineTOffset = -0.30f,
                grassLineTMin = 0.00f, grassLineTMax = 0.55f,
                grassDensityScale = 1.45f, grassHeightScale = 1.15f,
                groundColor = new Color(0.247f, 0.420f, 0.165f), // 짙은 초록 #3F6B2A
                sandColor = new Color(0.710f, 0.647f, 0.471f),   // 그늘진 모래 #B5A578
                heightScaleMul = 1.05f, plateauPowMul = 0.95f, terrainNoiseMul = 1.00f,
            },

            // 4. Volcanic — 검은 암반. 모래까지 어둡게 내려 "검은 모래 해변"이 되게 한다.
            new IslandArchetypeProfile
            {
                archetype = IslandArchetype.Volcanic, displayName = "화산암",
                palmScale = 0.30f, bushScale = 0.35f, tuftScale = 0.35f,
                vegetationCapScale = 0.45f, driftScale = 0.80f, rockScale = 1.90f, rockCoverage = 0.42f,
                grassLineTScale = 1.00f, grassLineTOffset = 0.20f,
                grassLineTMin = 0.20f, grassLineTMax = 1.00f,
                grassDensityScale = 0.40f, grassHeightScale = 0.85f,
                groundColor = new Color(0.227f, 0.216f, 0.200f), // 검은 현무암 #3A3733
                sandColor = new Color(0.361f, 0.333f, 0.302f),   // 검은 모래 #5C554D
                heightScaleMul = 1.15f, plateauPowMul = 0.95f, terrainNoiseMul = 1.35f,
            },

            // 5. Atoll — 낮고 평평. heightScaleMul로 낮추고 plateauPowMul로 평평하게 편다
            //    (pow를 내리면 중간 반경 y가 올라가므로 낮춰도 육지가 줄지 않는다 - 필드 주석).
            new IslandArchetypeProfile
            {
                archetype = IslandArchetype.Atoll, displayName = "산호",
                palmScale = 0.45f, bushScale = 0.35f, tuftScale = 0.50f,
                vegetationCapScale = 0.45f, driftScale = 1.30f, rockScale = 0.50f, rockCoverage = 0.05f,
                grassLineTScale = 0.85f, grassLineTOffset = 0.35f,
                grassLineTMin = 0.45f, grassLineTMax = 1.00f,
                grassDensityScale = 0.40f, grassHeightScale = 0.85f,
                groundColor = new Color(0.725f, 0.745f, 0.518f), // 바랜 초지 #B9BE84
                sandColor = new Color(0.878f, 0.835f, 0.706f),   // 산호 백사 #E0D5B4
                heightScaleMul = 0.72f, plateauPowMul = 0.65f, terrainNoiseMul = 0.75f,
            },

            // 6. Marsh — 잔디가 매우 많고 키 크다. 바위는 가장 적다.
            new IslandArchetypeProfile
            {
                archetype = IslandArchetype.Marsh, displayName = "습지",
                palmScale = 0.80f, bushScale = 1.90f, tuftScale = 1.90f,
                vegetationCapScale = 1.35f, driftScale = 1.00f, rockScale = 0.45f, rockCoverage = 0.03f,
                grassLineTScale = 0.90f, grassLineTOffset = -0.35f,
                grassLineTMin = 0.00f, grassLineTMax = 0.45f,
                grassDensityScale = 1.60f, grassHeightScale = 1.40f,
                groundColor = new Color(0.337f, 0.376f, 0.227f), // 녹갈 #56603A
                sandColor = new Color(0.620f, 0.565f, 0.408f),   // 진흙 섞인 모래 #9E9068
                heightScaleMul = 0.80f, plateauPowMul = 0.80f, terrainNoiseMul = 0.85f,
            },

            // 7. Cliff — 상부는 평탄한 초지, 가장자리는 급하다. 절벽 계열 바위가 많다.
            new IslandArchetypeProfile
            {
                archetype = IslandArchetype.Cliff, displayName = "절벽",
                palmScale = 0.90f, bushScale = 0.90f, tuftScale = 1.20f,
                vegetationCapScale = 1.00f, driftScale = 0.90f, rockScale = 1.70f, rockCoverage = 0.30f,
                grassLineTScale = 0.95f, grassLineTOffset = -0.15f,
                grassLineTMin = 0.00f, grassLineTMax = 0.70f,
                grassDensityScale = 1.15f, grassHeightScale = 1.00f,
                groundColor = new Color(0.431f, 0.518f, 0.286f), // 회록 초지 #6E8449
                sandColor = new Color(0.690f, 0.643f, 0.533f),   // 자갈 해변 #B0A488
                heightScaleMul = 1.30f, plateauPowMul = 0.60f, terrainNoiseMul = 1.15f,
            },
        };

        /// <summary>
        /// 아키타입 → 파라미터. **값 복사**를 돌려주므로 호출부가 표를 오염시킬 수 없다.
        /// 알 수 없는 값은 Tropical(기준선)로 폴백한다 - 이 프로젝트의 관례(IslandSizeMetrics.SelectBySize).
        /// </summary>
        public static IslandArchetypeProfile Get(IslandArchetype archetype)
        {
            int i = (int)archetype;
            return (uint)i < (uint)Table.Length ? Table[i] : Table[0];
        }

        /// <summary>표시명만 필요한 곳(로그·HUD)을 위한 편의 함수.</summary>
        public static string DisplayName(IslandArchetype archetype)
        {
            return Get(archetype).displayName;
        }

        // ═══════════════════════════════════════════════════════════════════════════
        //  배정 규칙 — 크기별 가중 추첨(순수 해시)
        // ═══════════════════════════════════════════════════════════════════════════
        //
        // [규칙]
        //  · islandId 0(시작 섬)은 **항상 Tropical**이다. 온보딩 구간이고(Docs/Design_Onboarding),
        //    경비행기 잔해·배 작업대가 중심 근처에 고정 배치되며, 사용자가 여기서 처음 집을 짓는다.
        //    SelectShapeProfile이 id 0을 항상 0번(완만한 초원)으로 고정하는 것과 같은 보호다.
        //  · 그 외는 **크기별 가중치 표**에서 해시로 하나를 뽑는다.
        //
        // [왜 하드 게이트가 아니라 가중치인가 — 실측 근거]
        // 실제 월드(MaldivesLayout 50섬)의 규모 분포는 Small 36 / Medium 10 / Large 3 / ExtraLarge 1이다.
        // "Rocky·Cliff는 중형 이상"을 **하드 게이트**로 걸면 후보가 14섬뿐이라 바위섬이 월드에 2~3개밖에
        // 안 나온다 - 사용자 요구("바위로 이루어진 섬")를 정면으로 배신한다. 그래서 크기 제약은
        // 가중치 기울기로 표현한다(소형에도 낮은 가중치로 등장 = 작은 암초/시스택으로 읽힌다).
        // 진짜 하드 게이트는 하나뿐이다: **Atoll은 Large/ExtraLarge에서 가중치 0**
        // (환초는 낮고 평평한 소·중형 지형이고, 대형 섬에 쓰면 넓은 물웅덩이가 되어 이동만 불편하다).
        //
        // [가중치 표] 행 = 아키타입(enum 순서), 열 = 규모(Small/Medium/Large/ExtraLarge).
        // Small 열이 전체의 72%를 결정하므로 Small 열을 거의 평평하게 두고, 큰 섬에서만
        // 지형 특성이 강한 유형(Rocky/Volcanic/Cliff/Jungle)을 밀어 올린다.
        private static readonly int[,] Weights =
        {
            //                 S    M    L   XL
            /* Tropical */ {  18,  14,  12,  10 },
            /* Rocky    */ {  18,  20,  20,  18 },
            /* Sandy    */ {  18,  12,   8,   6 },
            /* Jungle   */ {  14,  18,  20,  22 },
            /* Volcanic */ {  11,  12,  22,  26 },
            /* Atoll    */ {  16,  10,   0,   0 },
            /* Marsh    */ {  13,  16,   8,   6 },
            /* Cliff    */ {  12,  18,  22,  22 },
        };

        /// <summary>
        /// (worldSeed, islandId)에서 **지형 시드 키**를 만든다. 난수 소비 0의 순수 해시.
        ///
        /// ★ 단일 소스 ★ IslandMeshGenerator.ComputeNoiseSeed가 이 함수에 위임한다. 두 곳이 같은 값을
        /// 내야 하는 이유: GenerateIslandMesh는 (worldSeed, islandId)를 **모르고** noiseSeed만 받는데,
        /// 거기서도 아키타입 고도 계수를 적용해야 하기 때문이다(호출부 WorldMapManager는 편집 범위 밖).
        /// noiseSeed가 곧 시드 키이므로 FromSeedKey로 같은 아키타입을 되찾을 수 있다.
        /// 식은 예전 ComputeNoiseSeed와 **비트 단위로 동일**하다(두 소수 곱 → xorshift-곱 finalizer,
        /// LegacyNoiseSeed 충돌 회피 포함) - 지형 노이즈 오프셋이 한 비트도 달라지지 않는다.
        /// </summary>
        public static int SeedKey(int worldSeed, int islandId)
        {
            unchecked
            {
                uint h = (uint)(worldSeed * 73856093) ^ (uint)(islandId * 19349663) ^ 0x9E3779B9u;
                h ^= h >> 16;
                h *= 0x7FEB352Du;
                h ^= h >> 15;
                h *= 0x846CA68Bu;
                h ^= h >> 16;
                int seed = (int)h;
                // 센티널(IslandMeshGenerator.LegacyNoiseSeed = int.MinValue)과 겹치면 그 섬만 조용히
                // 예전 지형으로 되돌아간다. 확률은 2^-32이지만 눈으로 못 잡는 실패 모드라 비켜 준다.
                return seed == int.MinValue ? 0 : seed;
            }
        }

        /// <summary>
        /// 섬 하나의 아키타입을 결정한다. **난수를 소비하지 않는 순수 함수다.**
        /// islandId 0은 항상 Tropical(온보딩 보호).
        /// </summary>
        public static IslandArchetype For(int worldSeed, int islandId, IslandSize size)
        {
            if (islandId <= 0)
                return IslandArchetype.Tropical;

            return FromSeedKey(SeedKey(worldSeed, islandId), size, false);
        }

        /// <summary>
        /// SeedKey 값에서 직접 아키타입을 뽑는다. (worldSeed, islandId)를 모르는 호출부
        /// (IslandMeshGenerator.GenerateIslandMesh - noiseSeed만 받는다)를 위한 진입점이다.
        /// </summary>
        /// <param name="isStartIsland">
        /// 시작 섬이면 무조건 Tropical. GenerateIslandMesh는 shapeProfile == 0 여부로 이것을 안다
        /// (SelectShapeProfile이 islandId 0에만 0번을 돌려주는 것이 그 근거다).
        /// </param>
        public static IslandArchetype FromSeedKey(int seedKey, IslandSize size, bool isStartIsland)
        {
            if (isStartIsland)
                return IslandArchetype.Tropical;

            int column = SizeColumn(size);

            int total = 0;
            for (int a = 0; a < Count; a++)
                total += Weights[a, column];
            if (total <= 0)
                return IslandArchetype.Tropical; // 표가 비면(있을 수 없음) 기준선으로 폴백

            // 지형 노이즈 오프셋(NoiseOffsetFromSeed)과 **다른 salt**를 쓴다. 같은 salt를 쓰면
            // 아키타입과 노이즈 위상이 상관되어 "같은 유형이면 같은 무늬"가 된다.
            float roll;
            unchecked
            {
                uint h = (uint)seedKey ^ 0x7A5D3C11u;
                h ^= h >> 16;
                h *= 0x7FEB352Du;
                h ^= h >> 15;
                h *= 0x846CA68Bu;
                h ^= h >> 16;
                roll = (h & 0xFFFFFFu) / (float)0x1000000u;
            }

            int cursor = Mathf.FloorToInt(roll * total);
            if (cursor >= total) cursor = total - 1; // 부동소수 경계 방어

            int acc = 0;
            for (int a = 0; a < Count; a++)
            {
                acc += Weights[a, column];
                if (cursor < acc)
                    return (IslandArchetype)a;
            }
            return IslandArchetype.Tropical;
        }

        /// <summary>규모 → 가중치 표의 열 인덱스. 알 수 없는 값은 Small 열(IslandSizeMetrics 관례).</summary>
        private static int SizeColumn(IslandSize size)
        {
            switch (size)
            {
                case IslandSize.Small: return 0;
                case IslandSize.Medium: return 1;
                case IslandSize.Large: return 2;
                case IslandSize.ExtraLarge: return 3;
                default: return 0;
            }
        }

        /// <summary>
        /// 지형 반지름 → 규모. GenerateIslandMesh는 IslandSize를 모르고 radius만 받으므로 역산이 필요하다.
        /// 경계는 IslandSizeMetrics.GetTerrainRadius(50/90/140/200)의 중간값이다 - 반지름 공식이 바뀌어도
        /// 가장 가까운 규모로 떨어진다(Vegetation.PlaceLargeStones의 bracket 판정과 같은 방식·같은 값).
        /// </summary>
        public static IslandSize SizeFromRadius(float radius)
        {
            if (radius <= 70f) return IslandSize.Small;
            if (radius <= 115f) return IslandSize.Medium;
            if (radius <= 170f) return IslandSize.Large;
            return IslandSize.ExtraLarge;
        }

        // ── 월드 생성 1회 분포 로그 ────────────────────────────────────────────────

        /// <summary>이미 요약을 찍은 worldSeed. 0은 "아직 안 찍음"(worldSeed 0은 Awake에서 치환되므로 실사용 값이 아니다).</summary>
        private static int loggedWorldSeed;

        /// <summary>
        /// [R1 규약] 도메인 리로드를 끈 상태에서도 정적 상태가 이전 플레이 세션에서 새지 않게 리셋한다
        /// (SeabedGenerator.ResetStaticCache가 선례다).
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticCache()
        {
            loggedWorldSeed = 0;
        }

        /// <summary>
        /// 월드 생성 시 아키타입 분포를 **한 줄로 1회** 출력한다(같은 worldSeed에 두 번 찍지 않는다).
        /// 규모는 실측 배치 데이터(MaldivesLayout)에서 읽는다 - 그 데이터가 곧 이 월드의 섬 목록이다.
        /// 데이터가 없으면(폴백 랜덤 배치) 규모를 미리 알 수 없으므로 조용히 건너뛴다.
        /// **난수를 소비하지 않고, 씬 오브젝트도 건드리지 않는다.**
        /// </summary>
        public static void LogWorldDistributionOnce(int worldSeed)
        {
            if (worldSeed == loggedWorldSeed)
                return;
            loggedWorldSeed = worldSeed;

            var data = MaldivesLayout.Islands;
            if (data == null || data.Length < 2)
                return;

            var counts = new int[Count];
            for (int i = 0; i < data.Length; i++)
                counts[(int)For(worldSeed, i, data[i].size)]++;

            var sb = new System.Text.StringBuilder(160);
            sb.Append("[IslandArchetype] 월드 시드 ").Append(worldSeed)
              .Append(" · 섬 ").Append(data.Length).Append("개 유형 분포:");
            for (int a = 0; a < Count; a++)
                sb.Append(' ').Append(Table[a].displayName).Append('=').Append(counts[a]);
            Debug.Log(sb.ToString());
        }
    }
}
