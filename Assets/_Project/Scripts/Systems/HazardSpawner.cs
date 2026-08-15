using System.Collections.Generic;
using UnityEngine;
using MakeGame.Data;

namespace MakeGame.Systems
{
    /// <summary>
    /// 섬 하나에 위험 요소(HazardSource)들을 배치하는 스포너.
    /// 섬 규모가 클수록 위험 요소가 많아진다 (Stranded Deep 기준: 큰 섬일수록 위험도 큼).
    /// 플레이어가 불시착하는 시작 섬(isStartingIsland)에는 안전을 위해 위험 요소를 배치하지 않는다.
    ///
    /// [B8 구조 변경 — "확률 굴림"에서 "면적 기준 마릿수"로]
    /// 예전 구조는 hazardEntries의 엔트리마다 확률을 정확히 1회 굴렸다. 그래서 엔트리가 6종인 이상
    /// **섬 하나에 최대 6마리**가 구조적 상한이었고, 배율(1/1.75/2.5/3.25)은 그 상한 안에서만 움직였다.
    /// 반면 산포 면적(π·산포반경²)은 5,027 / 16,286 / 39,408 / 80,425 m²로 1 : 3.24 : 7.84 : 16 으로 커진다.
    /// 결과적으로 밀도(마리/만m²)가 2.19 / 1.18 / 0.70 / 0.44 로 **단조 감소**했다 —
    /// 즉 큰 섬일수록 안전해져서 "큰 섬 = 위험하지만 보상이 큰 곳"이라는 설계 의도와 정반대였다
    /// (game-designer 실측: Docs/Design_MidGame.md 5장).
    /// 이제는 마릿수를 면적에 비례해 먼저 정하고(ComputeHazardCount), 그 마릿수를 엔트리 가중치로
    /// 분배한다(PickWeightedEntry). 밀도가 규모와 무관하게 hazardsPerTenThousandSquareMeters로 고정된다.
    ///
    /// B4-1 (Spec_15 3단계 배선): SurvivalBalanceConfig를 선택적(nullable) 참조로 받는다.
    /// 폴백으로 읽는 config 필드 — smallMultiplier ← hazardSmallMultiplier,
    /// mediumMultiplier ← hazardMediumMultiplier, largeMultiplier ← hazardLargeMultiplier,
    /// extraLargeMultiplier ← hazardExtraLargeMultiplier.
    /// 산포 반경(*ScatterRadius)은 Spec_15가 의도적으로 config에서 제외한 값이라 배선하지 않았다
    /// (밸런스가 아니라 월드 지형 스케일 종속값).
    /// 폴백은 해당 필드가 0 이하(미설정)일 때만 적용되므로 씬 직렬화 값이 항상 이긴다. 즉 이 배선은
    /// GetMultiplier의 기존 3단 우선순위에 중간 단계를 하나 끼워 넣는 것과 같다:
    /// 씬/인스펙터 값 > balanceConfig > IslandSizeMetrics(최후 폴백).
    /// [주의/실측 대조] Balance_SceneSnapshot.md 기준 씬 직렬화 값은 1/1.5/2/2.5인 반면, 이 스크립트의
    /// 코드 기본값과 SurvivalBalanceConfig는 B3-7 상향안(1/1.75/2.5/3.25)을 담고 있어 서로 다르다.
    /// 씬 값이 전부 양수라 폴백이 실행되지 않으므로 이 배선 자체는 실동작을 바꾸지 않지만, 어느 쪽이
    /// 최종 밸런스인지는 기획 결정 사항이다(씬 값 갱신은 디렉터 담당).
    /// </summary>
    public class HazardSpawner : MonoBehaviour
    {
        [Header("밸런스 config (선택, B4-1)")]
        [Tooltip("연결하면, 아래 섬 규모별 배율이 0 이하로(미설정) 남아있는 경우에 한해 config의" +
            " hazard*Multiplier 값을 대신 쓴다. 씬에 이미 의미 있는(양수) 값이 직렬화돼 있으면" +
            " 절대 덮어쓰지 않는다.")]
        public SurvivalBalanceConfig balanceConfig;

        [System.Serializable]
        public class HazardEntry
        {
            [Tooltip("위험 요소 종류")]
            public HazardType type;

            // [B8 의미 변경 — 필드는 그대로, 뜻만 바뀌었다]
            // 예전: "이 엔트리가 등장할 확률"(엔트리마다 1회 굴림 → 섬당 최대 6마리 상한의 원인).
            // 지금: "이 섬에 배치될 마릿수를 종류별로 나눌 때 쓰는 **상대 가중치**".
            // 절대값이 아니라 비율만 의미가 있다. 씬(SampleScene.unity:972-984)에 6개 엔트리가
            // 0.25/0.20/0.15/0.20/0.20/0.10(합 1.10)으로 직렬화돼 있으므로 필드명·타입·[Range]를
            // 절대 바꾸지 않는다. 예컨대 독사(0.25)는 전체 마릿수의 0.25/1.10 = 22.7%를 차지한다.
            // [Range(0,1)]이 남아 있는 것도 의도된 것이다 — 비율만 쓰므로 0~1 안에서도 표현에 제약이 없다.
            [Tooltip("이 위험 요소가 섬 전체 마릿수에서 차지하는 상대 가중치. 절대값이 아니라 다른" +
                " 엔트리와의 비율만 의미가 있다(예: 0.25와 0.10이면 2.5배 더 자주 나온다).")]
            [Range(0f, 1f)]
            public float baseChance = 0.2f;
        }

        [Tooltip("섬에 등장 가능한 위험 요소 종류와 종류별 상대 가중치 목록")]
        public List<HazardEntry> hazardEntries = new List<HazardEntry>();

        // [B8] 마릿수를 정하는 새 단일 기준. 산포 면적 1만m²당 몇 마리를 놓을지다.
        // 2.0인 근거(game-designer 목표 + systems-engineer 전투 검산):
        //  - 대형 섬 산포 반경 112 → 면적 39,408m² → 2.0 × 3.9408 = 7.88 → 8마리(목표 7.9와 일치).
        //  - 소형 섬은 1마리로 떨어져 기존 기댓값 1.10과 사실상 같다(초반 난이도 그대로 유지).
        //  - 위험요소는 이동/추격 코드가 없는 정적 오브젝트라(HazardSource.Update는 쿨다운/재등장
        //    타이머만 돈다) 마릿수가 곧 조우 빈도이고, 8마리여도 대형 섬 조우 기대시간이 약 14분이다.
        // 이 필드는 씬에 아직 직렬화된 키가 없다 → 씬의 기존 HazardSpawner는 이 코드 기본값 2.0을 쓴다.
        // 값을 바꾸고 싶으면 씬 값이 이기므로 반드시 디렉터에게 YAML 키 추가를 요청할 것.
        [Header("면적 기준 밀도 (B8)")]
        [Tooltip("산포 면적 1만m²당 배치할 위험 요소 마릿수. 섬 규모와 무관하게 이 밀도가 유지되도록" +
            " 마릿수를 면적에 비례해 계산한다. 0 이하면 위험 요소를 배치하지 않는다.")]
        public float hazardsPerTenThousandSquareMeters = 2f;

        /// <summary>
        /// [B9] 마릿수 하드 캡의 코드 상수 기본값. maxHazardsPerIsland가 미설정(0 이하)일 때 쓴다.
        /// 20인 근거(셋 다 같은 방향을 가리킨다):
        ///  (1) 기획 — 오늘의 명목 마릿수 상한은 특대 섬 16마리다(아래 ComputeHazardCount 표).
        ///      20은 그 1.25배라 "디렉터가 특대 위험도를 조금 더 올린다"까지는 통과시키되,
        ///      GetSizeDangerWeight의 최대 트림 4배가 만들어낼 수 있는 64마리는 확실히 막는다.
        ///  (2) 성능 — SpawnSingleHazard는 개체마다 StructureVisualBuilder.CreateColorMaterial을
        ///      호출하고 그 메서드는 캐시 없이 매번 new Material을 만든다(StructureVisualBuilder.cs:103).
        ///      보조 파츠까지 세면 위험요소 1마리당 머티리얼이 약 5~7개다(곰 6 / 벌떼 6 / 상어 5).
        ///      특대 섬에는 이미 자원 노드 약 100개와 사냥감 약 10마리가 함께 깔린다 →
        ///      16마리면 위험요소 몫이 약 96개, 20마리면 약 120개. 64마리면 위험요소 하나만으로
        ///      약 384개가 되어 AGENT_BRIEF 4장이 경고하는 "섬당 400 머티리얼" 선을 단독으로 넘는다.
        ///  (3) 체감 — Design_MidGameContent.md 3장의 목표는 "섬 체류 30분 기대 조우 2.2회"다.
        ///      16↔20은 그 안에서 오차 범위지만 64는 목표의 4배라 다른 게임이 된다.
        /// 0 이하를 "상한 없음"이 아니라 "미설정"으로 해석하는 것은 의도된 것이다 — 이 프로젝트의
        /// 다른 0 이하 판정(GetMultiplier/GetScatterRadius/ApplyBalanceConfigFallback)과 규칙을 맞추고,
        /// 필드를 0으로 비워 상한을 통째로 꺼버리는 사고를 원천 차단한다.
        /// </summary>
        public const int DefaultMaxHazardsPerIsland = 20;

        [Tooltip("섬 하나에 배치할 위험 요소 마릿수의 절대 상한. 밀도·규모 트림을 곱한 결과가 이 값을" +
            " 넘으면 여기서 잘린다. 0 이하로 두면 미설정으로 보고 코드 상수(20)를 쓴다 — 0은 '상한 없음'이 아니다.")]
        public int maxHazardsPerIsland = DefaultMaxHazardsPerIsland;

        // 긴급 정정(#2 회귀 수정): 이 필드들을 한 차례 제거하고 IslandSizeMetrics 직접 호출로 바꿨었는데,
        // 실제 배포된 SampleScene.unity에 이 컴포넌트가 배치되어 있고 이 필드들에 코드 기본값과 다른
        // 값(디자이너가 조정한 실제 밸런스 값)이 직렬화되어 있다는 사실이 뒤늦게 확인되었다. 필드를
        // 제거하면 Unity가 그 직렬화 값을 잃어버리고 조용히 코드 기본값으로 되돌아간다 - "스테이징 범위에
        // 씬 파일이 없다"는 것이 "프로젝트에 씬 파일이 없다"는 뜻이 아니었다. 필드명/타입/기본값을
        // 원래(리팩터링 이전) 그대로 복원해 씬 직렬화 값이 다시 정상적으로 바인딩되도록 되돌렸다.
        // IslandSizeMetrics는 삭제하지 않고, 이 필드가 의미 있게 설정되지 않았을 때(0 이하)만 쓰는
        // "폴백 단일 소스"로 역할을 낮췄다 (GetMultiplier/GetScatterRadius 참고).
        // B3-7: 기획 결정으로 1/1.5/2/2.5 → 1/1.75/2.5/3.25로 상향한다. 자원 배율(IslandResourceSpawner,
        // 씬 실측 1/2/3/4)과 비교했을 때 기존 위험 배율은 대형 섬 기준 보상÷위험 비율이 1.5배가 되어
        // 소형/중형 섬을 굳이 찾아갈 이유가 사라진다는 문제가 있었다. 그렇다고 자원과 같은 배율(4배)로
        // 올리면 배 제작 재료를 구하러 반드시 가야 하는 후반 동선(대형/특대 섬)이 지나치게 가혹해진다.
        // 절충안으로 자원 곡선(1/2/3/4)과 기존 위험 곡선(1/1.5/2/2.5)의 산술 평균을 택했다:
        // (1+1)/2=1, (2+1.5)/2=1.75, (3+2)/2=2.5, (4+2.5)/2=3.25.
        // 씬(SampleScene.unity)에도 1/1.5/2/2.5가 직렬화돼 있어 코드 기본값만으로는 반영되지 않는다 -
        // 디렉터가 씬 값을 직접 맞춘다(코디네이터 보고 [디렉터 조치 요청] 항목 참고). 자원 배율
        // (IslandResourceSpawner의 smallMultiplier 등)은 이번 변경 대상이 아니므로 손대지 않았다.
        // [B8 의미 변경] 이 배율은 더 이상 "등장 확률에 곱하는 값"이 아니다. 마릿수는 이제 면적이 정하고,
        // 이 배율은 그 위에 얹는 **규모별 위험도 트림**으로 남는다(GetSizeDangerWeight 참고):
        //   가중치 = 이 필드 값 / 같은 규모의 기준값(NominalMultiplier, 아래 GetNominalMultiplier).
        // 씬(SampleScene.unity:985-988)과 SurvivalBalanceConfig.asset(40-43줄)에 직렬화된 현재 값
        // 1/1.75/2.5/3.25가 곧 기준값이므로 **오늘 기준 가중치는 네 규모 모두 정확히 1.0**이다.
        // 즉 이 배선은 지금 당장 아무 것도 바꾸지 않으면서, 디렉터가 씬 값을 예컨대 largeMultiplier
        // 2.5 → 5로 올리면 대형 섬 위험요소가 2배가 되는 튜닝 손잡이로 계속 살아 있다.
        // 필드를 제거하지 않는 이유는 이 프로젝트가 이미 한 번 사고를 낸 그것이다(위 #2 회귀 수정 주석).
        [Header("섬 규모별 위험도 트림 (B8부터 기준값 대비 상대값)")]
        public float smallMultiplier = 1f;
        public float mediumMultiplier = 1.75f;
        public float largeMultiplier = 2.5f;
        public float extraLargeMultiplier = 3.25f;

        [Header("섬 규모별 산포 반경")]
        // 버그 수정 (#1006 - 섬 크기별 밀도 공식 정립 연장선): 예전에는 scatterRadius가 섬 규모와 무관한
        // 값 하나(100f)뿐이었다. WorldMapManager.GetSizeScale의 지형 반지름(50/90/140/200)과 어긋나 있어서,
        // 소형 섬에서는 위험 요소가 지형 밖(바다)에 배치될 수 있었고 특대 섬에서는 중심 근처로만 몰렸다.
        // IslandResourceSpawner와 동일하게 각 섬 지형 반지름의 80%에 맞춰 규모별 반경을 따로 뒀다.
        // [B5 디렉터 수정] 위 주석은 "규모별로 따로 뒀다"고 적혀 있었지만 실제로는 네 값이 전부 같았다.
        // 즉 주석이 고쳤다고 주장하는 버그가 그대로 살아 있었다(qa-reviewer 지적). 소형 섬(지형 반지름 50)에
        // 반경 100로 흩뿌리면 배치물이 바다로 나가고, 특대 섬(200)은 중심 근처에만 몰린다.
        // IslandSizeMetrics.GetTerrainRadius(50/90/140/200)의 80%로 실제로 분리했다.
        // 씬의 낡은 `scatterRadius` 단일 키는 코드에 대응 필드가 없는 죽은 키라 함께 제거했다.
        public float smallScatterRadius = 40f;
        public float mediumScatterRadius = 72f;
        public float largeScatterRadius = 112f;
        public float extraLargeScatterRadius = 160f;

        /// <summary>
        /// 초목 배치 전용 난수 스트림의 salt 기준값. 섬 레이아웃(-2000000)/상어(-1000000)/섬별 위험요소
        /// (islandId 그대로, 0 이상의 작은 값)와 겹치지 않는 별도 대역을 예약해, 초목 개수를 나중에
        /// 조정해도 다른 스포너의 난수 시퀀스에 전혀 영향이 없게 한다.
        /// </summary>

        /// <summary>
        /// 초기화 시점에 balanceConfig 폴백을 적용한다. SpawnHazardsForIsland는 월드 생성 흐름에서
        /// 호출되므로, 그 전에 배율이 확정돼 있어야 한다.
        /// </summary>
        private void Awake()
        {
            ApplyBalanceConfigFallback();
        }

        /// <summary>
        /// balanceConfig가 있을 때, 0 이하로 남아있는(=미설정) 배율만 골라 config 값으로 채운다.
        /// 판정 기준(0 이하 = 미설정)은 GetMultiplier/GetScatterRadius가 이미 쓰던 것과 동일하며,
        /// 폴백이 채운 뒤에도 여전히 0 이하로 남는 배율은 기존대로 IslandSizeMetrics가 처리한다.
        /// balanceConfig가 비어 있으면 아무 것도 하지 않는다(기존 동작 100% 유지, NRE 없음).
        /// </summary>
        private void ApplyBalanceConfigFallback()
        {
            // B4-2: 인스펙터에서 연결되지 않았으면 Resources의 공용 에셋을 자동으로 집는다.
            // 런타임 생성 컴포넌트(WeatherSystem/Campfire/WaterStill 등)는 인스펙터 연결 수단이
            // 아예 없어서, 이 경로가 없으면 balanceConfig가 영원히 null로 남는다.
            if (balanceConfig == null)
                balanceConfig = SurvivalBalanceConfig.Active;
            if (balanceConfig == null)
                return;

            if (smallMultiplier <= 0f) smallMultiplier = balanceConfig.hazardSmallMultiplier;
            if (mediumMultiplier <= 0f) mediumMultiplier = balanceConfig.hazardMediumMultiplier;
            if (largeMultiplier <= 0f) largeMultiplier = balanceConfig.hazardLargeMultiplier;
            if (extraLargeMultiplier <= 0f) extraLargeMultiplier = balanceConfig.hazardExtraLargeMultiplier;
        }

        /// <summary>
        /// 지정한 섬에 규모(면적)에 맞는 마릿수만큼 위험 요소를 배치한다. 시작 섬에는 배치하지 않는다.
        /// B3-3: worldSeed를 추가로 받아, 이 섬(island.islandId) 전용 결정적 System.Random 스트림으로
        /// 종류 추첨·산포 위치·크기/회전 지터를 전부 뽑는다(재현성 근거는 IslandResourceSpawner
        /// 상단 주석과 동일). 실제로 등장한 위험 요소마다 (island.islandId, spawnOrder) 식별자를 부여한다.
        ///
        /// [B8] 루프의 축이 "엔트리"에서 "마릿수"로 바뀌었다. 예전에는 엔트리 6개를 한 번씩 훑으며
        /// 확률을 굴려서 섬당 최대 6마리가 상한이었다. 이제는 ComputeHazardCount가 면적으로 마릿수를
        /// 먼저 정하고, 매 마리마다 PickWeightedEntry가 baseChance 비율대로 종류를 뽑는다.
        /// spawnOrder는 예전과 똑같이 "실제로 생성된 개체의 0부터 시작하는 러닝 카운터"다 —
        /// 부여 방식 자체는 손대지 않았다(SaveLoadController가 (islandIndex, spawnOrder)를 세이브 키로
        /// 쓰기 때문. 세이브 영향 분석은 이 변경의 보고서 참고).
        /// </summary>
        public List<HazardSource> SpawnHazardsForIsland(IslandInstance island, Transform parent, int worldSeed)
        {
            var spawned = new List<HazardSource>();
            if (island == null)
                return spawned;

            // 시작 섬 면제: 불시착 지점에서 바로 죽지 않도록 위험 요소를 하나도 놓지 않는다.
            // 이 조기 반환은 B8 구조 변경에서도 그대로 유지한다.
            if (island.isStartingIsland)
                return spawned;

            // 가중치 합. 엔트리가 비어 있거나 전부 0 이하면 배치할 종류가 없으므로 그대로 끝낸다
            // (예전 구조에서도 baseChance가 0이면 절대 등장하지 않았다 — 동작이 같다).
            float totalWeight = 0f;
            foreach (var entry in hazardEntries)
            {
                if (entry != null && entry.baseChance > 0f)
                    totalWeight += entry.baseChance;
            }
            if (totalWeight <= 0f)
                return spawned;

            System.Random rng = SeededRandomExtensions.CreateForIsland(worldSeed, island.islandId);
            int spawnOrder = 0;

            float radius = GetScatterRadius(island.size);
            int hazardCount = ComputeHazardCount(island.size);

            for (int i = 0; i < hazardCount; i++)
            {
                HazardEntry entry = PickWeightedEntry(rng, totalWeight);
                if (entry == null)
                    continue;

                Vector2 offset = rng.NextInsideUnitCircle() * radius;
                Vector3 position = island.mapPosition + new Vector3(offset.x, 0f, offset.y);
                position = TerrainSampler.SnapToGround(position);
                spawned.Add(SpawnSingleHazard(entry.type, position, parent, rng, island.islandId, spawnOrder));
                spawnOrder++;
            }

            return spawned;
        }

        /// <summary>
        /// [B8] 이 섬 규모에 배치할 위험 요소 총 마릿수를 산포 면적에 비례해 계산한다.
        /// 면적 = π · 산포반경², 마릿수 = 밀도 × (면적 / 10,000) × 규모별 위험도 트림.
        /// 씬 실측값 기준 결과(밀도 2.0, 트림 1.0):
        ///   소형  r=40  → 5,027m²  → 1.01 → 1마리  (밀도 1.99/만m²)
        ///   중형  r=72  → 16,286m² → 3.26 → 3마리  (밀도 1.84/만m²)
        ///   대형  r=112 → 39,408m² → 7.88 → 8마리  (밀도 2.03/만m²)
        ///   특대  r=160 → 80,425m² → 16.08 → 16마리 (밀도 1.99/만m²)
        /// 예전 구조(2.19 / 1.18 / 0.70 / 0.44)와 달리 밀도가 규모에 따라 떨어지지 않는다.
        /// 밀도가 양수인데 반올림 결과가 0이 되는 경우(아주 작은 섬)에는 최소 1마리를 보장한다 —
        /// 위험 요소가 아예 없는 섬은 "면제"가 아니라 버그로 읽히기 때문이다.
        /// 밀도 자체가 0 이하면(디자이너가 의도적으로 끈 경우) 0을 그대로 돌려준다.
        ///
        /// [B9] 마지막에 maxHazardsPerIsland로 하드 캡을 건다. 면적 비례 공식은 입력(밀도 · 규모 트림)이
        /// 둘 다 인스펙터에서 자유롭게 조정 가능한 값이라, 곱이 커지면 마릿수가 상한 없이 따라 커진다.
        /// 오늘의 씬 값에서는 특대 16마리로 캡(20)에 닿지 않으므로 이 줄은 실동작을 바꾸지 않는다 —
        /// 순수하게 오설정 방어다. 캡 값의 근거는 DefaultMaxHazardsPerIsland 주석 참고.
        /// </summary>
        private int ComputeHazardCount(IslandSize size)
        {
            if (hazardsPerTenThousandSquareMeters <= 0f)
                return 0;

            float radius = GetScatterRadius(size);
            if (radius <= 0f)
                return 0;

            float areaInTenThousandSqm = Mathf.PI * radius * radius / 10000f;
            int count = Mathf.RoundToInt(hazardsPerTenThousandSquareMeters * areaInTenThousandSqm * GetSizeDangerWeight(size));
            if (count < 1)
                count = 1;

            int cap = maxHazardsPerIsland > 0 ? maxHazardsPerIsland : DefaultMaxHazardsPerIsland;
            if (count > cap)
                count = cap;

            return count;
        }

        /// <summary>
        /// [B8] baseChance를 상대 가중치로 보고 종류 하나를 뽑는다. rng를 정확히 1회만 소비한다
        /// (마릿수 루프의 난수 소비량을 일정하게 유지해 시퀀스 추적을 단순하게 만든다).
        /// totalWeight는 호출자가 미리 계산해 넘긴다 — 매 마리마다 리스트를 다시 합산하지 않기 위함이다.
        /// </summary>
        private HazardEntry PickWeightedEntry(System.Random rng, float totalWeight)
        {
            float roll = rng.NextValue01() * totalWeight;
            float accumulated = 0f;

            for (int i = 0; i < hazardEntries.Count; i++)
            {
                HazardEntry entry = hazardEntries[i];
                if (entry == null || entry.baseChance <= 0f)
                    continue;

                accumulated += entry.baseChance;
                if (roll < accumulated)
                    return entry;
            }

            // 부동소수 누적 오차로 마지막 구간을 넘어간 경우의 안전망: 가중치가 있는 마지막 엔트리.
            for (int i = hazardEntries.Count - 1; i >= 0; i--)
            {
                HazardEntry entry = hazardEntries[i];
                if (entry != null && entry.baseChance > 0f)
                    return entry;
            }

            return null;
        }

        /// <summary>
        /// SharkSpawner처럼 섬이 아닌 곳(바다 한가운데)에 위험 요소를 배치해야 하는 다른 스포너가
        /// 이 클래스의 시각/전투 설정 테이블(GetVisualConfig, HazardSource.ConfigureForType)을 그대로
        /// 재사용할 수 있도록 공개한 진입점. 섬 배치(SpawnHazardsForIsland)와 달리 확률/섬 규모 개념이
        /// 없고, 호출자가 이미 정한 위치에 정확히 하나를 생성한다.
        /// B3-3: 호출자(SharkSpawner)가 자신만의 독립된 결정적 rng와 spawnOrder를 넘겨야 한다 - 섬에
        /// 속하지 않는 스폰이므로 islandIndex는 호출자가 판단해 넘긴다(SharkSpawner는 -1을 쓴다).
        /// </summary>
        public HazardSource SpawnHazardAtPosition(HazardType type, Vector3 position, Transform parent, System.Random rng, int islandIndex, int spawnOrder)
        {
            return SpawnSingleHazard(type, position, parent, rng, islandIndex, spawnOrder);
        }

        /// <summary>
        /// 위험 요소 하나를 실제로 생성한다. 종류별로 형태/크기/색상/회전이 다른 프리미티브를 사용해
        /// 플레이어가 캡슐 하나로는 구분할 수 없던 곰/식인종/독사/전갈/벌떼/함정/상어를 한눈에 구별할 수 있게 한다.
        /// </summary>
        private HazardSource SpawnSingleHazard(HazardType type, Vector3 position, Transform parent, System.Random rng, int islandIndex, int spawnOrder)
        {
            HazardVisualConfig config = GetVisualConfig(type);

            GameObject go = GameObject.CreatePrimitive(config.primitiveType);
            go.transform.SetParent(parent);

            // 퀄리티 개선(#325 재점검): 자원 노드/사냥감과 같은 문제 - 같은 종류의 위험 요소가 여러 섬에
            // 걸쳐 완전히 동일한 크기/방향으로 찍히는 것을 막기 위해 개체마다 살짝 다른 크기 배율과
            // 세워진 축(Y) 기준 방향을 추가로 준다. Trap처럼 대칭적인 원판은 시각적으로 티가 안 나지만
            // 해를 끼치지도 않으므로 모든 타입에 공통 적용해 코드를 단순하게 유지한다.
            // B3-3: 시드 없는 UnityEngine.Random 대신 호출자가 넘긴 결정적 rng를 쓴다.
            float sizeJitter = rng.NextFloat(0.9f, 1.15f);
            Quaternion yawJitter = Quaternion.Euler(0f, rng.NextFloat(0f, 360f), 0f);

            go.transform.localScale = config.localScale * sizeJitter;
            go.transform.rotation = yawJitter * Quaternion.Euler(config.rotationEuler);
            go.transform.position = position + Vector3.up * config.groundOffset;
            go.name = $"Hazard_{type}";

            var renderer = go.GetComponent<MeshRenderer>();
            if (renderer != null)
                renderer.sharedMaterial = StructureVisualBuilder.CreateColorMaterial(config.color);

            // 퀄리티 개선: 몸통 하나짜리 프리미티브만으로는 위험 요소가 밋밋해 보여, 종류별로
            // 알아볼 수 있는 작은 보조 파츠(눈, 벌떼 무리 등)를 덧붙인다.
            // sizeJitter가 적용된 실제 스케일(go.transform.localScale)을 넘겨야 보정 계산이 실제 배치된
            // 크기와 맞아떨어진다(config.localScale은 jitter 이전 원본값이라 그대로 쓰면 이후 유지보수 시
            // 혼동의 여지가 있어 명시적으로 실제 값을 전달한다).
            AddDetailParts(go, type, config, go.transform.localScale, rng);

            var col = go.GetComponent<Collider>();
            if (col != null)
                col.isTrigger = true;

            var hazard = go.AddComponent<HazardSource>();
            hazard.hazardType = type;
            hazard.ConfigureForType(); // 종류(곰/식인종/벌떼 등)에 맞춰 전투 가능 여부와 체력을 설정한다.
            hazard.islandIndex = islandIndex;
            hazard.spawnOrder = spawnOrder;
            return hazard;
        }

        /// <summary>
        /// 종류별로 몸통 프리미티브 하나로는 표현할 수 없던 디테일(눈, 벌떼 무리)을 자식 오브젝트로 추가한다.
        /// 자식의 localScale은 부모의 비균일 localScale(config.localScale)로 나눠 보정해, 몸통이
        /// 눌리거나 늘어난 축(예: 상어의 길쭉한 몸통)에서도 눈이 타원으로 찌그러지지 않고 둥글게 보이게 한다.
        /// </summary>
        private void AddDetailParts(GameObject go, HazardType type, HazardVisualConfig config, Vector3 appliedScale, System.Random rng)
        {
            Vector3 s = appliedScale;
            Color darkEye = new Color(0.05f, 0.05f, 0.05f);

            switch (type)
            {
                case HazardType.Bear:
                case HazardType.Cannibal:
                    // 몸통 캡슐 위쪽(머리 부근)에 작은 눈 두 개를 붙인다.
                    AddCompensatedSphere(go, new Vector3(0.18f, 0.75f, 0.35f), 0.09f, s, darkEye, "EyeL");
                    AddCompensatedSphere(go, new Vector3(-0.18f, 0.75f, 0.35f), 0.09f, s, darkEye, "EyeR");
                    break;

                case HazardType.Shark:
                    // 상어는 rotationEuler(0,0,90)으로 눕혀져 있다. Z축 +90도 회전은 +X→+Y, +Y→-X이므로
                    // 로컬 축의 의미가 이렇게 바뀐다: 로컬 +X = 월드 위쪽, 로컬 +Y = 몸통 진행 방향(머리),
                    // 로컬 +Z = 좌우.
                    //
                    // [B5 수정 - 눈이 좌우가 아니라 위아래로 쌓여 있었다] 기존 값은 눈 두 개를 로컬
                    // X ±0.22에 두었는데, 위 축 관계에 따르면 로컬 X는 좌우가 아니라 "월드 수직"이다.
                    // 즉 두 눈이 머리의 정수리와 턱 아래에 하나씩 박혀 있었다(등지느러미가 옆구리에
                    // 붙어 있던 B4-3 버그와 완전히 같은 축 혼동). 좌우인 로컬 Z로 옮긴다.
                    //
                    // 값 근거(식인종 창이 몸통에 파묻혀 있던 사례와 같은 실측): 몸통 캡슐의 로컬 반지름은
                    // 0.5이고, 눈을 붙이는 로컬 y=0.7 지점은 이미 반구 캡 구간이라 그 단면 반지름은
                    // sqrt(0.5² - 0.2²) ≈ 0.458이다. 눈 중심을 로컬 (x=0.12, z=±0.40)에 두면 축 기준
                    // 반경이 sqrt(0.12² + 0.40²) ≈ 0.418로 표면 바로 안쪽이고, 여기에 눈 자체의 월드
                    // 반지름 0.07m가 더해져 몸통 밖으로 확실히 드러난다(몸통 로컬 스케일 0.45 기준
                    // 월드 표면 0.206m 대비 눈 바깥면 0.258m). x를 0으로 두지 않고 0.12만큼 올린 것은
                    // 눈이 머리 옆면 가운데가 아니라 약간 위쪽에 붙어야 상어처럼 읽히기 때문이다.
                    AddCompensatedSphere(go, new Vector3(0.12f, 0.7f, 0.40f), 0.07f, s, darkEye, "EyeL");
                    AddCompensatedSphere(go, new Vector3(0.12f, 0.7f, -0.40f), 0.07f, s, darkEye, "EyeR");
                    // 등지느러미: 작은 원뿔 대신 얇은 큐브로 단순하게 표현.
                    //
                    // B4-3(축 정정 + 확대 + 색): 세 가지를 함께 고쳤다.
                    // (1) 축 — 상어 몸통은 rotationEuler(0,0,90)으로 눕혀져 있어서 "로컬 +X가 월드 위쪽,
                    //     로컬 +Y가 몸통 진행 방향, 로컬 +Z가 좌우"가 된다(Unity의 Z축 +90도 회전은
                    //     +X→+Y, +Y→-X). 기존 값은 로컬 +Z로 0.32 밀고 두께 0.06을 로컬 X(=월드 수직)에
                    //     줬기 때문에, 등지느러미가 아니라 옆구리에 붙은 납작한 판이었고 수직 높이가
                    //     사실상 0이었다. 그래서 "지느러미가 작아 안 보인다"는 지적은 크기 이전에
                    //     방향 문제였다. 이제 로컬 +X(월드 위)로 세운다.
                    // (2) 크기 — 최대 치수 0.22 → 0.42(약 1.9배), 두께 0.06 → 0.10(약 1.7배)로
                    //     지시받은 1.5~2배 범위 안에서 키웠다.
                    // (3) 색 — Danger Red #CC3333(ArtDirection 1.1). 근거는 아래 finColor 주석 참고.
                    //
                    // 주의: 이 파츠는 CreateVisualPart가 콜라이더를 제거한 순수 시각 오브젝트다.
                    // 판정은 몸통 캡슐의 트리거 콜라이더 하나뿐이라 지느러미를 키워도 공격 범위는
                    // 1mm도 변하지 않는다(시각/판정이 이미 분리되어 있음을 확인함).
                    Color finColor = new Color(0.8f, 0.2f, 0.2f); // Danger Red #CC3333
                    AddCompensatedBox(go, new Vector3(0.4f, 0.12f, 0f), new Vector3(0.42f, 0.42f, 0.1f), s, finColor, "Fin");
                    break;

                case HazardType.BeeSwarm:
                    // 공 하나가 아니라 작은 벌 여러 마리가 뭉쳐 있는 것처럼 보이도록 주변에 작은 구체를 흩뿌린다.
                    // B3-3: 시드 없는 UnityEngine.Random 대신 호출자가 넘긴 결정적 rng를 쓴다.
                    // [tech-artist-B 요청 - 벌이 아니라 덩어리로 보인다] 벌 한 마리의 월드 반지름이 0.22m로
                    // 몸통 구체 반지름(localScale 0.5 → 0.25m)과 거의 같았다. 같은 크기의 구체 6개가 반경
                    // 0.8 안에서 서로 파고들면 "무리"가 아니라 울퉁불퉁한 덩어리 하나로 읽힌다.
                    // 0.22 → 0.09(몸통의 약 1/3)로 줄여 개별 개체가 몸통과 분리돼 보이게 한다.
                    // 개수(5)와 산포 범위(±0.8)는 그대로 둔다 - rng 소비량이 바뀌지 않아 재현성도 유지된다.
                    for (int i = 0; i < 5; i++)
                    {
                        Vector3 offset = new Vector3(
                            rng.NextFloat(-0.8f, 0.8f),
                            rng.NextFloat(-0.8f, 0.8f),
                            rng.NextFloat(-0.8f, 0.8f));
                        AddCompensatedSphere(go, offset, 0.09f, s, config.color, $"Bee{i}");
                    }
                    break;
            }

            // 연결(A-1): CreatureVisualBuilder.AddHazardDetailsIfMissing을 호출해 보조 디테일을 추가한다.
            //
            // [B5 정정 - 이전 주석이 사실과 달랐다(qa-reviewer 지적)] 여기에는 "곰/식인종/상어/벌떼는
            // CreatureVisualBuilder가 아무 것도 하지 않으므로(직접 확인함) 중복이 없다"고 적혀 있었으나
            // 사실이 아니다. AddHazardDetailsIfMissing의 switch는 VenomousSnake/Scorpion/Trap뿐 아니라
            // Bear(귀+주둥이)/Cannibal(창+돌촉)/Shark(꼬리지느러미)까지 여섯 종류를 실제로 처리한다
            // (CreatureVisualBuilder.cs의 AddHazardDetailsIfMissing 본문 참조). 아무 것도 하지 않는 것은
            // BeeSwarm 하나뿐이다.
            //
            // 그럼에도 중복 파츠가 생기지 않는 진짜 이유는 "파츠 이름이 겹치지 않기" 때문이다.
            // 위 switch가 만드는 이름은 EyeL/EyeR/Fin/Bee*이고, CreatureVisualBuilder가 만드는 이름은
            // EarL/EarR/Snout/Spear/SpearHead/TailFin 등으로 서로 완전히 분리돼 있다. 즉 두 곳은
            // "역할 분담"이지 "한쪽이 비어 있어서"가 아니다.
            // → 새 디테일을 추가할 때는 반드시 양쪽 이름 목록을 모두 확인할 것. 같은 이름을 쓰면
            //   같은 자리에 파츠 두 개가 겹쳐 z-파이팅으로 지글거린다.
            CreatureVisualBuilder.AddHazardDetailsIfMissing(go, type, s, config.color);
        }

        /// <summary>
        /// 부모의 비균일 스케일을 상쇄한 구체 파츠를 만든다(둥근 형태 유지용).
        /// </summary>
        private void AddCompensatedSphere(GameObject parent, Vector3 localPos, float worldRadius, Vector3 parentScale, Color color, string name)
        {
            Vector3 compScale = new Vector3(
                worldRadius * 2f / Mathf.Max(0.0001f, parentScale.x),
                worldRadius * 2f / Mathf.Max(0.0001f, parentScale.y),
                worldRadius * 2f / Mathf.Max(0.0001f, parentScale.z));
            StructureVisualBuilder.CreateVisualPart(parent.transform, name, PrimitiveType.Sphere, localPos, compScale, color);
        }

        /// <summary>
        /// 부모의 비균일 스케일을 상쇄한 박스 파츠를 만든다(지느러미 등 납작한 형태용).
        /// </summary>
        private void AddCompensatedBox(GameObject parent, Vector3 localPos, Vector3 worldSize, Vector3 parentScale, Color color, string name)
        {
            Vector3 compScale = new Vector3(
                worldSize.x / Mathf.Max(0.0001f, parentScale.x),
                worldSize.y / Mathf.Max(0.0001f, parentScale.y),
                worldSize.z / Mathf.Max(0.0001f, parentScale.z));
            StructureVisualBuilder.CreateVisualPart(parent.transform, name, PrimitiveType.Cube, localPos, compScale, color);
        }

        /// <summary>
        /// 위험 요소 시각 정보(프리미티브 종류, 크기, 회전, 색상, 지면으로부터 띄울 높이)를 담는 구조체.
        /// </summary>
        private struct HazardVisualConfig
        {
            public PrimitiveType primitiveType;
            public Vector3 localScale;
            public Vector3 rotationEuler;
            public Color color;
            public float groundOffset;
        }

        /// <summary>
        /// 위험 요소 종류별로 구분 가능한 형태/크기/색상을 반환한다.
        /// 곰=크고 진한 갈색 캡슐, 식인종=사람 크기의 적갈색 캡슐, 독사=길고 납작한 초록 캡슐(눕혀서 배치),
        /// 전갈=작고 납작한 어두운 주황 캡슐, 벌떼=작은 노란 구체, 함정=땅에 깔린 어두운 회갈색 원판.
        /// </summary>
        private HazardVisualConfig GetVisualConfig(HazardType type)
        {
            switch (type)
            {
                case HazardType.Bear:
                    return new HazardVisualConfig
                    {
                        primitiveType = PrimitiveType.Capsule,
                        localScale = new Vector3(0.9f, 1.1f, 0.9f),
                        rotationEuler = Vector3.zero,
                        color = new Color(0.32f, 0.2f, 0.12f), // 진한 갈색
                        groundOffset = 1.1f
                    };

                case HazardType.Cannibal:
                    return new HazardVisualConfig
                    {
                        primitiveType = PrimitiveType.Capsule,
                        localScale = new Vector3(0.55f, 0.9f, 0.55f),
                        rotationEuler = Vector3.zero,
                        color = new Color(0.6f, 0.35f, 0.25f), // 적갈색
                        groundOffset = 0.9f
                    };

                case HazardType.VenomousSnake:
                    return new HazardVisualConfig
                    {
                        primitiveType = PrimitiveType.Capsule,
                        localScale = new Vector3(0.18f, 0.6f, 0.18f),
                        rotationEuler = new Vector3(0f, 0f, 90f), // 눕혀서 길게 배치
                        color = new Color(0.15f, 0.55f, 0.2f), // 초록
                        groundOffset = 0.1f
                    };

                case HazardType.Scorpion:
                    return new HazardVisualConfig
                    {
                        primitiveType = PrimitiveType.Capsule,
                        localScale = new Vector3(0.16f, 0.3f, 0.16f),
                        rotationEuler = new Vector3(0f, 0f, 90f), // 눕혀서 낮고 짧게 배치
                        color = new Color(0.45f, 0.22f, 0.05f), // 어두운 주황/흙색
                        groundOffset = 0.09f
                    };

                case HazardType.BeeSwarm:
                    return new HazardVisualConfig
                    {
                        primitiveType = PrimitiveType.Sphere,
                        localScale = new Vector3(0.5f, 0.5f, 0.5f),
                        rotationEuler = Vector3.zero,
                        color = new Color(0.95f, 0.75f, 0.1f), // 노란색
                        groundOffset = 1.4f // 벌떼는 공중에 떠 있게
                    };

                case HazardType.Trap:
                    return new HazardVisualConfig
                    {
                        primitiveType = PrimitiveType.Cylinder,
                        localScale = new Vector3(0.6f, 0.04f, 0.6f), // 얇은 원판 형태로 땅에 깔아둔다
                        rotationEuler = Vector3.zero,
                        color = new Color(0.35f, 0.3f, 0.25f), // 어두운 회갈색
                        groundOffset = 0.04f
                    };

                case HazardType.Shark:
                    return new HazardVisualConfig
                    {
                        primitiveType = PrimitiveType.Capsule,
                        localScale = new Vector3(0.45f, 1.4f, 0.45f), // 길쭉하게 눕혀서 상어 몸통처럼 보이게
                        rotationEuler = new Vector3(0f, 0f, 90f),
                        color = new Color(0.28f, 0.35f, 0.42f), // 어두운 청회색
                        groundOffset = 0f // SharkSpawner가 이미 해수면 아래 정확한 위치를 계산해 넘겨준다
                    };

                default:
                    return new HazardVisualConfig
                    {
                        primitiveType = PrimitiveType.Capsule,
                        localScale = new Vector3(0.6f, 0.6f, 0.6f),
                        rotationEuler = Vector3.zero,
                        color = Color.gray,
                        groundOffset = 0.6f
                    };
            }
        }

        /// <summary>
        /// [B8] 규모별 위험도 트림. 씬/config에 설정된 배율을 "기준값 대비 상대값"으로 환산해 돌려준다.
        /// 배율이 기준값과 같으면 1.0이 나오므로, 오늘의 씬 값(1/1.75/2.5/3.25)에서는 마릿수가 순수하게
        /// 면적×밀도로만 결정된다. 디렉터가 씬 값을 올리거나 내리면 그 비율만큼 마릿수가 움직인다.
        /// 극단적인 오설정으로 섬 하나가 통째로 도살장이 되지 않도록 0.25~4배로 묶는다.
        /// </summary>
        private float GetSizeDangerWeight(IslandSize size)
        {
            float nominal = GetNominalMultiplier(size);
            if (nominal <= 0f)
                return 1f;

            float actual = GetMultiplier(size);
            if (actual <= 0f)
                return 1f;

            return Mathf.Clamp(actual / nominal, 0.25f, 4f);
        }

        /// <summary>
        /// [B8] "트림 없음(가중치 1.0)"을 뜻하는 규모별 기준 배율.
        /// ⚠️ 이 값은 SampleScene.unity의 HazardSpawner 배율(985-988줄)과 SurvivalBalanceConfig.asset
        /// (hazardSmallMultiplier 등, 40-43줄)에 직렬화된 B3-7 상향안과 **같은 숫자여야 한다**.
        /// 한쪽만 바꾸면 트림이 조용히 1.0에서 벗어나 마릿수가 의도 없이 변한다 —
        /// 이 프로젝트가 반복해서 낸 "코드 기본값과 씬 값이 갈라진다" 사고와 정확히 같은 형태다.
        /// 세 곳(코드/씬/config)을 함께 바꾸거나, 아예 셋 다 그대로 두는 것만 허용된다.
        /// </summary>
        private static float GetNominalMultiplier(IslandSize size)
        {
            switch (size)
            {
                case IslandSize.Small: return 1f;
                case IslandSize.Medium: return 1.75f;
                case IslandSize.Large: return 2.5f;
                case IslandSize.ExtraLarge: return 3.25f;
                default: return 1f;
            }
        }

        /// <summary>
        /// 섬 규모에 대응하는 위험 요소 배율(B8부터는 위험도 트림의 원본값)을 반환한다.
        /// 긴급 정정(#2 회귀 수정): 인스펙터(씬 직렬화)에 설정된 필드 값을 항상 우선한다. 필드가 0
        /// 이하로 남아있어(설정 실수/아직 배치 안 된 새 컴포넌트 등) 의미 있게 설정되지 않은 경우에만
        /// IslandSizeMetrics.GetLinearDensityMultiplier를 안전한 기본값 폴백으로 사용한다.
        /// </summary>
        private float GetMultiplier(IslandSize size)
        {
            switch (size)
            {
                case IslandSize.Small: return smallMultiplier > 0f ? smallMultiplier : IslandSizeMetrics.GetLinearDensityMultiplier(size);
                case IslandSize.Medium: return mediumMultiplier > 0f ? mediumMultiplier : IslandSizeMetrics.GetLinearDensityMultiplier(size);
                case IslandSize.Large: return largeMultiplier > 0f ? largeMultiplier : IslandSizeMetrics.GetLinearDensityMultiplier(size);
                case IslandSize.ExtraLarge: return extraLargeMultiplier > 0f ? extraLargeMultiplier : IslandSizeMetrics.GetLinearDensityMultiplier(size);
                default: return smallMultiplier > 0f ? smallMultiplier : IslandSizeMetrics.GetLinearDensityMultiplier(size);
            }
        }

        /// <summary>
        /// 섬 규모에 대응하는 위험 요소 산포 반경을 반환한다.
        /// 긴급 정정(#2 회귀 수정): 인스펙터(씬 직렬화)에 설정된 필드 값을 항상 우선한다. 필드가 0 이하로
        /// 남아있을 때만 IslandSizeMetrics.GetScatterRadius를 안전한 기본값 폴백으로 사용한다.
        /// </summary>
        private float GetScatterRadius(IslandSize size)
        {
            switch (size)
            {
                case IslandSize.Small: return smallScatterRadius > 0f ? smallScatterRadius : IslandSizeMetrics.GetScatterRadius(size);
                case IslandSize.Medium: return mediumScatterRadius > 0f ? mediumScatterRadius : IslandSizeMetrics.GetScatterRadius(size);
                case IslandSize.Large: return largeScatterRadius > 0f ? largeScatterRadius : IslandSizeMetrics.GetScatterRadius(size);
                case IslandSize.ExtraLarge: return extraLargeScatterRadius > 0f ? extraLargeScatterRadius : IslandSizeMetrics.GetScatterRadius(size);
                default: return smallScatterRadius > 0f ? smallScatterRadius : IslandSizeMetrics.GetScatterRadius(size);
            }
        }
    }
}
