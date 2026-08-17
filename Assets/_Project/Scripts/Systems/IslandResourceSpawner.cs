using System.Collections.Generic;
using UnityEngine;
using MakeGame.Data;
using MakeGame.UI;

namespace MakeGame.Systems
{
    /// <summary>
    /// 섬 하나에 채집 가능한 자원 노드들을 배치하는 스포너.
    /// 섬 규모가 클수록 더 많은 자원 노드를 생성한다 (Stranded Deep 기준: 큰 섬일수록 자원이 풍부).
    /// 실제 3D 모델 에셋이 없으므로, 자원 종류별로 다르게 조합한 프리미티브(GetNodeShape/
    /// AddResourceDetailParts 참고)에 ResourceNode를 붙여 시각화한다. 처음엔 전부 동일한 큐브였는데,
    /// 비스듬한 각도에서 보면 큐브 옆면 3개가 보여 죄다 육각형 상자처럼 보인다는 사용자 피드백을 받고
    /// 자원별 프리미티브 조합으로 실루엣을 다르게 만들었다.
    /// 버그 수정: 예전에는 모든 자원 노드가 색 지정 없이 기본 회색 큐브로만 나와, 나뭇가지/돌조각/코코넛/
    /// 금속조각 등 종류가 전혀 구분되지 않았다. 인벤토리/제작 UI에서 이미 쓰던
    /// UIBuilder.GetItemCategoryColor를 재사용해 최소한 음식/음료/일반 재료 정도는 색으로 구분되게 했다.
    /// 퀄리티 개선: "같은 종류 자원이라도 보여지는 모양이 다양했으면 좋겠다"는 추가 피드백을 받아,
    /// SpawnSingleNode에서 스폰마다 축별 크기 배율과 Y축 회전을 무작위로 주고, AddResourceDetailParts의
    /// 곁가지/마디/잎사귀/볼트 등 반복 파츠 개수도 무작위 범위로 바꿔 같은 자원이라도 인스턴스마다
    /// 조금씩 다르게 생기도록 했다(완전한 클론처럼 보이지 않게).
    /// </summary>
    public class IslandResourceSpawner : MonoBehaviour
    {
        [System.Serializable]
        public class ResourceEntry
        {
            [Tooltip("이 자원 노드를 채집했을 때 얻는 아이템")]
            public ItemData yieldItem;

            [Tooltip("소형 섬 기준 기본 배치 개수 (규모가 커질수록 배율이 곱해진다)")]
            public int baseCount = 3;

            [Tooltip("이 자원이 등장할 수 있는 최소 섬 규모. 예를 들어 Large로 설정하면 대형/특대 섬에만 등장하고" +
                " 소형/중형 섬에는 전혀 등장하지 않는다. 희귀 재료(금속조각/부력통/엔진부품 등)의 등장 위치를 제한할 때 사용한다.")]
            public IslandSize minimumIslandSize = IslandSize.Small;

            [Tooltip("이 자원을 채집하는 데 도구가 필요한지 여부. true면 requiredTool을 인벤토리에 보유해야 채집할 수 있고,\n" +
                "채집할 때마다 그 도구의 내구도(ItemData.maxUses)가 1씩 소모된다 (예: 나뭇가지 채집에 손도끼 필요).")]
            public bool requiresTool = false;

            [Tooltip("채집에 필요한 도구 아이템 (requiresTool이 true일 때만 사용, 예: 손도끼)")]
            public ItemData requiredTool;

            // [game-designer 요청 - Design_BalancePass 3장] 보너스 도구는 requiredTool과 정반대의
            // 물건이다. requiredTool은 "없으면 채집 거부"(잠금 위험)이고, bonusTool은 "있으면 더 많이"
            // (가산)라서 실패값이 '경로 소멸'이 아니라 '느려짐'이다. 그래서 이 두 필드는 채집 성공/실패
            // 판정(ResourceNode.GetHarvestFailure)에 절대 전달되지 않고 수확량 계산에만 쓰인다.
            //
            // 중요 - 기존 씬 엔트리와의 호환: 이 두 키는 SampleScene.unity의 resourceEntries 12개
            // 항목 어디에도 아직 없다. 역직렬화 시 없는 키가 무엇으로 채워지든(초기화식 값이든 0/null
            // 이든) 결과가 "보너스 없음"으로 같아지도록, bonusTool = null / bonusYieldPerHarvest = 0을
            // 기본값으로 잡고 ResourceNode 쪽에서 두 조건을 AND로 검사한다. 즉 디렉터가 씬에 값을
            // 넣기 전까지 채집량은 1도 바뀌지 않는다.
            [Tooltip("보유하고 있으면 이 자원의 채집량이 늘어나는 보너스 도구 (예: 야자잎에 칼).\n" +
                "requiredTool과 달리, 없어도 채집은 정상적으로 성공한다 - 수확량 가산에만 관여한다.")]
            public ItemData bonusTool;

            [Tooltip("bonusTool을 보유했을 때 1회 채집당 추가로 얻는 개수. 0이면 보너스가 없다(기본값).")]
            public int bonusYieldPerHarvest = 0;
        }

        [Tooltip("섬에 배치할 자원 종류와 기본 개수 목록")]
        public List<ResourceEntry> resourceEntries = new List<ResourceEntry>();

        // 긴급 정정(#2 회귀 수정): 이 필드들을 한 차례 제거하고 IslandSizeMetrics 직접 호출로 바꿨었는데,
        // 실제 배포된 SampleScene.unity에 이 컴포넌트가 배치되어 있고 이 필드들에 코드 기본값과 다른
        // 값(디자이너가 조정한 실제 밸런스 값)이 직렬화되어 있다는 사실이 뒤늦게 확인되었다. 필드를
        // 제거하면 Unity가 그 직렬화 값을 잃어버리고 조용히 코드 기본값으로 되돌아간다 - "스테이징 범위에
        // 씬 파일이 없다"는 것이 "프로젝트에 씬 파일이 없다"는 뜻이 아니었다. 필드명/타입/기본값을
        // 원래(리팩터링 이전) 그대로 복원해 씬 직렬화 값이 다시 정상적으로 바인딩되도록 되돌렸다.
        // IslandSizeMetrics는 삭제하지 않고, 이 필드가 의미 있게 설정되지 않았을 때(0 이하)만 쓰는
        // "폴백 단일 소스"로 역할을 낮췄다 (GetMultiplier/GetScatterRadius 참고).
        [Header("섬 규모별 배치 배율")]
        // [B7 디렉터 정정] 이 자리에 있던 주석은 "배율을 면적비(1 : 3.24 : 7.84 : 16)에 맞춰 올렸다"고
        // 적혀 있었지만 값은 선형 1/2/3/4 그대로였다. 씬 값도 1/2/3/4다. 즉 주석이 주장하는 수정이
        // 코드에 반영된 적이 없다 - 바로 옆 scatterRadius가 정확히 같은 방식으로 오래 살아남았던
        // 버그와 같은 유형이다(qa-reviewer 지적).
        //
        // 확인 후 선형을 유지하기로 했다. 면적비를 적용하면 특대 섬의 자원 노드가 16배가 되어
        // 노드 수백 개가 깔린다(현재 특대 기준 약 96개 → 380개 이상). 드로우콜·세이브 크기·탐색 재미가
        // 전부 나빠진다. 큰 섬은 "자원이 빽빽한 곳"이 아니라 "특별한 자원(금속조각·부력통·엔진부품)이
        // 있는 곳"으로 차별화하는 것이 이 게임의 설계다(Docs/Design_Progression.md).
        // 밀도가 옅어지는 것은 의도된 것이다 - 넓은 섬은 실제로 더 많이 걸어야 한다.
        // IslandSizeMetrics.GetAreaProportionalMultiplier는 이 필드들이 0 이하일 때만 쓰이는 폴백이라
        // 현재 호출되지 않는다. 지우지는 않았다(다른 밸런스 실험에서 다시 쓸 수 있다).
        public float smallMultiplier = 1f;
        public float mediumMultiplier = 2f;
        public float largeMultiplier = 3f;
        public float extraLargeMultiplier = 4f;

        [Header("섬 규모별 산포 반경")]
        // 버그 수정 (#1003/#1006): 예전에는 scatterRadius가 섬 규모와 무관하게 값 하나(80f)뿐이었다.
        // WorldMapManager.GetSizeScale의 지형 반지름(50/90/140/200)과 전혀 맞지 않아서, 소형 섬은
        // 지형 밖(바다)까지 자원이 튀어나가고 특대 섬은 중심 근처에만 자원이 몰려 대부분의 면적이 텅
        // 비어 보이는 문제가 있었다. 각 섬 지형 반지름의 80%(해안에 자원이 물에 잠기지 않도록 여백 확보)
        // 에 맞춰 규모별 반경을 따로 뒀다.
        // [B5 디렉터 수정] 위 주석은 "규모별로 따로 뒀다"고 적혀 있었지만 실제로는 네 값이 전부 같았다.
        // 즉 주석이 고쳤다고 주장하는 버그가 그대로 살아 있었다(qa-reviewer 지적). 소형 섬(지형 반지름 50)에
        // 반경 80로 흩뿌리면 배치물이 바다로 나가고, 특대 섬(200)은 중심 근처에만 몰린다.
        // IslandSizeMetrics.GetTerrainRadius(50/90/140/200)의 80%로 실제로 분리했다.
        // 씬의 낡은 `scatterRadius` 단일 키는 코드에 대응 필드가 없는 죽은 키라 함께 제거했다.
        public float smallScatterRadius = 40f;
        public float mediumScatterRadius = 72f;
        public float largeScatterRadius = 112f;
        public float extraLargeScatterRadius = 160f;

        [Header("시작 섬 착륙 원 (game-designer 요청)")]
        // 문제: 시작 섬은 소형(배율 1)이라 자원 노드가 18개뿐인데 산포 반경이 40m다. 노드 하나가 평균
        // 약 280제곱미터를 차지하므로, 불시착 지점(섬 중심)에 서 있는 플레이어 눈앞이 통째로 비어 있는
        // 경우가 흔하다 - 첫 채집까지 헤매는 원인이 이 밀도다.
        // 해결: 시작 섬에 한해 "무엇을 어떻게 줍는지"를 가르치는 최소 3종(수분=코코넛, 목재=나뭇가지,
        // 석재=돌조각. 손도끼 레시피 재료가 뒤 둘이다)을 시작 지점 근처에 확정 배치한다.
        // 밸런스를 바꾸지 않기 위해 기존 무작위 스폰은 한 줄도 건드리지 않고 3개를 "더한다"
        // (시작 섬 노드 20 → 23개, 다른 8개 섬은 변화 없음).
        [Tooltip("시작 섬(isStartingIsland)에 한해 플레이어 시작 지점 근처에 기초 자원 3종을 확정 배치할지 여부.\n" +
                 "끄면 예전처럼 무작위 스폰만 돌아간다(기존 무작위 스폰 자체는 이 값과 무관하게 그대로다).")]
        public bool spawnLandingCircle = true;

        [Tooltip("착륙 원의 최대 반경(미터). 플레이어 시작 지점에서 이 거리 안에 3종이 배치된다.\n" +
                 "game-designer 요청 상한이 12m라 그보다 크게 설정해도 12m로 잘린다.")]
        public float landingCircleRadius = 9f;

        /// <summary>착륙 원에 확정 배치할 자원 3종. resourceEntries에서 이름으로 찾아 그 항목의 설정(도구 요구 등)을 그대로 재사용한다.</summary>
        private static readonly string[] LandingCircleItemNames = { "코코넛", "나뭇가지", "돌조각" };

        /// <summary>착륙 원 상한 반경(미터). game-designer가 지정한 "시작 지점 반경 12m 안" 규격.</summary>
        private const float LandingCircleMaxRadius = 12f;

        /// <summary>
        /// 착륙 원 기준 각도(도, +X축 기준 반시계)의 **폴백값**. 시선 정보(landingCircleFacingYaw)가
        /// 없을 때만 쓴다. 시작 섬의 고정 배치물 두 개 - 경비행기 잔해(+6, -4 → 약 -34도)와
        /// 배 작업대(-6, -3 → 약 207도) - 와 각도가 겹치지 않도록 20도에서 시작한다.
        /// 정상 경로(WorldMapManager가 시선을 넣어주는 경우)에서는 이 값이 쓰이지 않는다.
        /// </summary>
        private const float LandingCircleBaseAngle = 20f;

        /// <summary>
        /// 플레이어 시작 시선(Unity yaw, 도). WorldMapManager가 경비행기 잔해의 반대 방향을 계산해
        /// 자원 배치 직전에 넣어준다. 값이 들어와 있으면 착륙 원의 첫 번째 노드(코코넛)가 그 방향,
        /// 즉 **플레이어 정면**에 오도록 원 전체를 회전시킨다.
        ///
        /// null(미설정)이면 예전처럼 절대 각도 LandingCircleBaseAngle을 쓴다 - 이 스포너를
        /// WorldMapManager 없이 단독으로 쓰는 경우 동작이 바뀌지 않게 하기 위한 폴백이다.
        /// 직렬화 대상이 아니므로(private + Nullable) 씬 값이 이 필드를 덮을 일도 없다.
        /// </summary>
        private float? landingCircleFacingYaw = null;

        /// <summary>
        /// 기준 각도로부터 각 노드를 얼마나 벌려 놓을지(도). LandingCircleItemNames와 같은 순서다.
        ///
        /// 시선 정보가 있을 때 이 값은 Design_Onboarding.md 2장 배치표를 그대로 옮긴 것이 된다 -
        /// 코코넛 **정면**(0도), 나뭇가지 **좌측**(+90도), 돌조각 **우측**(-90도).
        /// (수학 각도계에서 +90도가 좌측인 이유: Unity yaw가 시계 방향이라 θ = 90 - yaw 변환이
        /// 부호를 뒤집는다. GetLandingCircleBaseAngle 주석 참고.)
        ///
        /// 왜 균등 120도가 아닌가: 세 노드가 정면·좌·우로 서면 플레이어가 고개만 돌려 셋을 전부
        /// 볼 수 있다. 120도 간격이면 하나는 항상 등 뒤로 가는데, 그 하나는 "존재하지 않는 것"과
        /// 같다 - 등 뒤를 확인할 이유를 아직 배우지 못한 0분의 플레이어에게는 특히 그렇다.
        /// </summary>
        private static readonly float[] LandingCircleAngleOffsets = { 0f, 90f, -90f };

        // [B28] 파츠별 명도 변주표. 감독 지시("줄기마다 색을 조금씩 다르게")를 만족시키되, 값을 **고정 표**로
        // 두는 것이 핵심이다 - 무작위 실수 배율을 쓰면 색 조합마다 머티리얼이 새로 만들어져 공유가 깨진다.
        // 표가 4단계뿐이라 자원 하나가 쓰는 머티리얼은 최대 4개이고, 그 4개를 월드의 모든 섬이 공유한다.
        // 색상(hue)은 건드리지 않고 명도만 바꾼다 - 팔레트 소유권은 ArtDirection/UIBuilder에 있다.
        private static readonly float[] CulmTints = { 0.86f, 1.06f, 0.94f, 1.00f };
        private static readonly float[] TwigTints = { 1.04f, 0.88f, 0.96f, 0.80f };
        private static readonly float[] RockTints = { 0.92f, 1.05f, 0.84f };
        private static readonly float[] FrondTints = { 0.90f, 1.04f, 0.96f };

        /// <summary>
        /// 지정한 섬 인스턴스 위에 규모에 맞는 개수만큼 자원 노드를 생성한다.
        /// 각 노드는 섬 위치를 중심으로 scatterRadius 반경 안에 무작위 배치된다.
        /// B3-3: worldSeed를 추가로 받아, 이 섬(island.islandId) 전용 결정적 System.Random 스트림을
        /// 만들어 쓴다. 같은 worldSeed로 다시 호출하면(WorldMapManager.RegenerateWorld) 이 섬에서
        /// 생성되는 노드의 위치·모양(스케일/회전 지터 포함)·개수·순서가 정확히 그대로 재현된다 -
        /// 다른 섬이 그 사이에 몇 개를 뽑았는지와 완전히 무관하다(섬마다 독립된 스트림이므로).
        /// 각 노드에는 (island.islandId, spawnOrder) 쌍으로 이뤄진 안정적인 식별자를 부여한다
        /// (ResourceNode.islandIndex/spawnOrder 참고) - B3-4에서 이 쌍을 세이브 키로 그대로 쓴다.
        /// </summary>
        public List<ResourceNode> SpawnResourcesForIsland(IslandInstance island, Transform parent, int worldSeed)
        {
            var spawned = new List<ResourceNode>();
            if (island == null)
                return spawned;

            System.Random rng = SeededRandomExtensions.CreateForIsland(worldSeed, island.islandId);
            int spawnOrder = 0;

            float multiplier = GetMultiplier(island.size);
            float radius = GetScatterRadius(island.size);

            foreach (var entry in resourceEntries)
            {
                if (entry.yieldItem == null)
                    continue;

                // 최소 섬 규모 미만이면 이 자원은 아예 등장하지 않는다 (희귀 재료 위치 제한용).
                if (island.size < entry.minimumIslandSize)
                    continue;

                int count = Mathf.RoundToInt(entry.baseCount * multiplier);
                for (int i = 0; i < count; i++)
                {
                    Vector2 offset = rng.NextInsideUnitCircle() * radius;
                    Vector3 position = island.mapPosition + new Vector3(offset.x, 0f, offset.y);
                    position = TerrainSampler.SnapToGround(position);
                    spawned.Add(SpawnSingleNode(entry, position, parent, rng, island.islandId, spawnOrder));
                    spawnOrder++;
                }
            }

            // 착륙 원은 반드시 위 무작위 루프가 끝난 "뒤"에 처리한다. 이유가 두 개다.
            // (1) 난수: 같은 rng를 이어 쓰되 모든 추가 draw가 기존 노드들의 draw 뒤에 오므로, 기존 18개
            //     노드의 위치/모양/개수가 1mm도 바뀌지 않는다(기존 밸런스·레이아웃 완전 보존).
            // (2) 세이브 키: 채집 상태가 (islandIndex, spawnOrder) 쌍으로 저장되므로(B3-4) 새 노드는
            //     기존 번호 뒤에 이어 붙는 번호를 받아야 한다. 앞에 끼워 넣으면 기존 세이브의 채집 상태가
            //     통째로 한 칸씩 밀린다. 뒤에 붙이면 옛 세이브도 그대로 복원되고, 새 노드 3개만 미채집
            //     상태로 시작한다.
            if (spawnLandingCircle && island.isStartingIsland)
                SpawnLandingCircleNodes(island, parent, rng, spawned, ref spawnOrder);

            // [B49 디렉터 지시 "대나무를 5배로"] 증량분은 **모든 기존 노드(착륙 원 포함) 뒤**에 붙인다.
            // 위 (1)(2)와 정확히 같은 이유이고, 대나무는 resourceEntries 5번째 항목이라 그 자리에서
            // count를 늘리면 뒤따르는 8종(천조각/부싯돌/금속조각/부력통/비상식량/연료/엔진부품/생수) +
            // 착륙 원 3개의 spawnOrder가 통째로 밀려 기존 세이브의 채집 상태가 엉뚱한 노드에 복원된다.
            // 이 호출은 위 루프와 착륙 원이 draw를 전부 끝낸 **뒤에** 시작하므로, 기존 노드의
            // 위치·모양·개수·번호가 하나도 바뀌지 않는다.
            SpawnExtraBambooNodes(island, parent, rng, spawned, ref spawnOrder);

            return spawned;
        }

        /// <summary>이 이름의 자원을 증량 대상으로 삼는다. 씬 엔트리를 이름으로 찾는다(착륙 원과 같은 방식).</summary>
        private const string BambooItemName = "대나무";

        /// <summary>
        /// [B49] 대나무 **추가** 배치 배수. 기존 배치분(baseCount × 규모 배율)의 이 배수만큼을 뒤에 덧붙여
        /// 합계가 (1 + 이 값)배가 된다. 사용자 요청이 "5배"이므로 4다.
        /// 씬 `resourceEntries`의 `baseCount`를 고치는 방식은 쓸 수 없다 - 그 방식은 대나무가 목록
        /// 중간(5번째)에 있어서 뒤따르는 모든 노드의 spawnOrder를 밀어버리고, 그 값이 곧 세이브 키다.
        /// </summary>
        private const int BambooExtraSpawnMultiplier = 4;

        /// <summary>
        /// [B49] 대나무 노드를 기존 배치분의 <see cref="BambooExtraSpawnMultiplier"/>배만큼 **추가로** 배치한다.
        ///
        /// ★ 이 함수가 지키는 계약 ★
        ///  · 반드시 SpawnResourcesForIsland의 무작위 루프와 착륙 원이 **모두 끝난 뒤**에 호출된다.
        ///    그래야 (a) 기존 노드의 spawnOrder 0..N-1이 예전과 같은 노드에 그대로 붙고(세이브 안전),
        ///    (b) 여기서 소비하는 rng draw가 전부 기존 draw **뒤**에 와서 기존 노드의 위치·스케일·회전
        ///    지터가 1mm도 바뀌지 않는다. 추가분은 N 이후 번호를 받아 미채집 상태로 시작한다(정상).
        ///  · 배치 규칙을 **새로 만들지 않는다.** 위 무작위 루프와 완전히 같은 식을 쓴다 -
        ///    같은 산포 반경(GetScatterRadius), 같은 균등 원판 표집(NextInsideUnitCircle),
        ///    같은 TerrainSampler.SnapToGround, 같은 SpawnSingleNode 경로.
        ///    따라서 콜라이더(루트 캡슐)·모델(bamboo_a/b/c)·머티리얼·채집 규칙이 기존 대나무와 동일하다.
        ///  · 씬 엔트리를 이름으로 찾아 **그대로 재사용**한다(FindEntryByItemName). 코드에서 새
        ///    ResourceEntry를 만들면 씬에 직렬화된 설정(도구 요구/최소 섬 규모/보너스 도구)과 어긋난다.
        ///  · 최소 섬 규모 판정도 위 루프와 같은 조건을 그대로 쓴다. 대나무는 현재 minimumIslandSize가
        ///    Small이라 전 섬에 나오지만, 디렉터가 씬에서 이 값을 올리면 증량분도 함께 사라져야 한다.
        /// </summary>
        private void SpawnExtraBambooNodes(IslandInstance island, Transform parent, System.Random rng,
            List<ResourceNode> spawned, ref int spawnOrder)
        {
            ResourceEntry entry = FindEntryByItemName(BambooItemName);
            if (entry == null || entry.yieldItem == null)
                return; // 씬에 대나무 항목이 없으면 조용히 아무것도 하지 않는다(설정 누락에 NRE로 죽지 않게).

            if (island.size < entry.minimumIslandSize)
                return;

            // 위 무작위 루프가 이 섬에 실제로 배치한 대나무 개수와 **같은 식**으로 다시 계산한다.
            int baseSpawnedCount = Mathf.RoundToInt(entry.baseCount * GetMultiplier(island.size));
            int extraCount = baseSpawnedCount * BambooExtraSpawnMultiplier;
            if (extraCount <= 0)
                return;

            float radius = GetScatterRadius(island.size);
            for (int i = 0; i < extraCount; i++)
            {
                Vector2 offset = rng.NextInsideUnitCircle() * radius;
                Vector3 position = island.mapPosition + new Vector3(offset.x, 0f, offset.y);
                position = TerrainSampler.SnapToGround(position);
                spawned.Add(SpawnSingleNode(entry, position, parent, rng, island.islandId, spawnOrder));
                spawnOrder++;
            }
        }

        /// <summary>
        /// 시작 섬 한정으로, 플레이어 시작 지점 주변에 기초 자원 3종(코코넛/나뭇가지/돌조각)을 확정 배치한다.
        ///
        /// 시작 지점을 섬 중심(island.mapPosition)으로 잡는 근거: 씬의 Player 트랜스폼이 (0, 14, 0)이고
        /// WorldMapManager.GenerateStartingIsland가 0번 섬의 mapPosition을 Vector3.zero로 두므로, 두 지점의
        /// XZ 좌표가 정확히 같다(플레이어 시작 위치를 참조하는 별도 배선을 만들 필요가 없다).
        /// 높이(y 5 → 14, 지형 기복 상향 대응)가 바뀌어도 이 배치는 XZ만 쓰고 TerrainSampler.SnapToGround로
        /// 지면에 붙이므로 영향을 받지 않는다.
        ///
        /// 방향: SetLandingCircleFacingYaw가 호출됐다면 코코넛이 플레이어 정면, 나뭇가지가 좌측,
        /// 돌조각이 우측에 온다(LandingCircleAngleOffsets / GetLandingCircleBaseAngle 참고).
        ///
        /// 결정성: UnityEngine.Random을 쓰지 않고 호출자가 넘긴 섬 전용 System.Random 스트림을 그대로
        /// 이어 쓴다. 같은 worldSeed면 RegenerateWorld(F9 불러오기) 후에도 같은 3개 위치가 재현된다.
        /// 각도/거리에만 작은 지터를 줘서(격자처럼 보이지 않게) 3종이 늘 같은 방향에 박혀 있지는 않되,
        /// 시드가 같으면 항상 같은 자리에 오도록 한다.
        /// </summary>
        private void SpawnLandingCircleNodes(IslandInstance island, Transform parent, System.Random rng,
            List<ResourceNode> spawned, ref int spawnOrder)
        {
            float radius = Mathf.Clamp(landingCircleRadius, 3f, LandingCircleMaxRadius);
            float baseAngle = GetLandingCircleBaseAngle();

            for (int i = 0; i < LandingCircleItemNames.Length; i++)
            {
                ResourceEntry entry = FindEntryByItemName(LandingCircleItemNames[i]);
                if (entry == null)
                    continue; // 그 자원이 resourceEntries에 없으면 조용히 건너뛴다(설정 누락에 NRE로 죽지 않게).

                // 지터는 ±12도로 그대로 둔다 - 격자처럼 보이지 않을 만큼은 흔들되, 정면에 둔 노드가
                // 시야(60도 FOV) 밖으로 나갈 만큼은 흔들지 않는 폭이다.
                float offset = i < LandingCircleAngleOffsets.Length ? LandingCircleAngleOffsets[i] : 0f;
                float angle = (baseAngle + offset + rng.NextFloat(-12f, 12f)) * Mathf.Deg2Rad;
                float distance = radius * rng.NextFloat(0.62f, 1f); // 너무 발밑에 붙지 않게 최소 62%는 띄운다.
                Vector3 position = island.mapPosition + new Vector3(Mathf.Cos(angle) * distance, 0f, Mathf.Sin(angle) * distance);
                position = TerrainSampler.SnapToGround(position);

                spawned.Add(SpawnSingleNode(entry, position, parent, rng, island.islandId, spawnOrder));
                spawnOrder++;
            }
        }

        /// <summary>
        /// 착륙 원을 플레이어 시작 시선에 맞춰 회전시킨다. WorldMapManager가 시작 섬의 자원을 배치하기
        /// **직전**에 호출한다(그 뒤에 부르면 이미 배치가 끝나 아무 효과가 없다).
        /// </summary>
        /// <param name="yawDegrees">플레이어가 바라볼 방향(Unity yaw, 도).</param>
        public void SetLandingCircleFacingYaw(float yawDegrees)
        {
            landingCircleFacingYaw = yawDegrees;
        }

        /// <summary>
        /// 착륙 원 첫 번째 노드의 기준 각도를 구한다.
        ///
        /// 좌표계 변환에 주의: Unity yaw는 +Z가 0도이고 시계 방향으로 증가하는데(정면 벡터가
        /// (sin y, cos y)), 이 배치 코드는 (cos θ, sin θ)를 (x, z)로 쓰는 수학 각도를 쓴다.
        /// 두 표현을 맞추면 θ = 90 - yaw 다. 이 한 줄을 틀리면 코코넛이 정면이 아니라 옆이나 등 뒤에
        /// 놓이고, 증상이 "가끔 안 보인다"라서 원인을 찾기 어렵다.
        ///
        /// 시선 정보가 없으면 기존 절대 각도를 그대로 쓴다(동작 불변).
        /// </summary>
        private float GetLandingCircleBaseAngle()
        {
            if (!landingCircleFacingYaw.HasValue)
                return LandingCircleBaseAngle;

            return 90f - landingCircleFacingYaw.Value;
        }

        /// <summary>
        /// resourceEntries에서 지정한 이름의 자원 항목을 찾는다(없으면 null).
        /// 착륙 원이 이 조회를 쓰는 이유: 새 ResourceEntry를 코드에서 만들어 쓰면 씬에 직렬화된 실제 설정
        /// (도구 요구 여부/최소 섬 규모 등)과 어긋난 노드가 생긴다. 씬 항목을 그대로 재사용해야
        /// "같은 자원은 어디서 나오든 같은 규칙"이 유지된다.
        /// </summary>
        private ResourceEntry FindEntryByItemName(string itemName)
        {
            if (resourceEntries == null || string.IsNullOrEmpty(itemName))
                return null;

            foreach (var entry in resourceEntries)
            {
                if (entry != null && entry.yieldItem != null && entry.yieldItem.itemName == itemName)
                    return entry;
            }
            return null;
        }

        /// <summary>
        /// 자원 노드 하나를 실제로 생성한다. ResourceNode 컴포넌트를 붙인다.
        /// 버그 수정: requiresTool/requiredTool을 ResourceNode에 전달하지 않아, ResourceNode.Harvest에
        /// 도구 요구 로직이 있어도 실제로 생성되는 노드는 전부 도구 없이 채집 가능했던 문제를 고쳤다
        /// (ResourceEntry에 requiresTool/requiredTool 필드가 아예 없어 절차적으로 설정할 방법 자체가 없었음).
        /// 퀄리티 개선: 예전엔 모든 자원 종류가 동일한 큐브(1x1.5x1)라 카메라를 비스듬히 보면 다 똑같은
        /// "육각형 상자"로 보여 색상만으로 뭘 채집할 수 있는지 구분해야 했다(사용자 피드백으로 발견).
        /// GetNodeShape로 자원 종류별 실제 프리미티브/크기를 다르게 하고, AddResourceDetailParts로
        /// 보조 파츠(대나무 마디, 야자잎 부채꼴 등)를 덧붙여 실루엣만 보고도 구분되게 했다.
        /// </summary>
        private ResourceNode SpawnSingleNode(ResourceEntry entry, Vector3 position, Transform parent, System.Random rng, int islandIndex, int spawnOrder)
        {
            ItemData yieldItem = entry.yieldItem;
            string itemName = yieldItem.itemName;

            GetNodeShape(itemName, out PrimitiveType primitive, out Vector3 scale, out Quaternion rotation);

            // 퀄리티 개선: 사용자 피드백("같은 종류 자원이라도 보여지는 모양이 다양했으면 좋겠다")을 반영해,
            // 자원 하나하나가 완전히 같은 크기/방향으로 찍히지 않도록 스폰마다 축별로 살짝 다른 배율을 곱하고
            // Y축(위아래 축) 기준으로 무작위 회전을 더한다.
            // B3-3: 시드 없는 UnityEngine.Random 대신 이 섬 전용 rng(System.Random)를 쓰도록 바꿔, 같은
            // worldSeed면 이 스케일/회전 지터까지도 정확히 재현되게 했다(SpawnResourcesForIsland 주석 참고).
            //
            // [B28] 가로 지터를 x/z에 **같은 값**으로 준다. 예전에는 x와 z를 따로 뽑아서 루트의 가로
            // 단면이 최대 1.39:1 타원이 됐는데, 이것이 자식 파츠에까지 번졌다 - 부모 스케일이
            // diag(a, b, c)이고 a != c면 Y축으로 돌린 자식이 회전각에 따라 굵기가 변한다(원기둥 줄기가
            // 방향에 따라 납작해진다). a == c로 맞추면 Y 회전이 **정확한 회전**이 되어(스케일 행렬과
            // 교환됨) 아래 AddMeshPart가 미터 단위로 만든 메시를 그대로, 왜곡 없이 세울 수 있다.
            float horizontalJitter = rng.NextFloat(0.85f, 1.18f);
            float verticalJitter = rng.NextFloat(0.85f, 1.25f);
            int shapeVariant = rng.NextInt(0, 6); // 루트 메시 변주 선택(모양 자체가 인스턴스마다 달라진다)
            scale = Vector3.Scale(scale, new Vector3(horizontalJitter, verticalJitter, horizontalJitter));

            // [B28] 곱하는 순서를 뒤집었다. 예전 `rotation * yaw`는 yaw를 **기울어진 로컬 축** 기준으로
            // 적용해서, 기울여 놓은 자원(부싯돌/금속조각)이 제자리에서 축만 도는 꼴이었다(월드 방향이
            // 바뀌지 않는다). 월드 Y로 먼저 돌려야 "같은 자원이 여러 방향을 향한다"가 실제로 성립한다.
            rotation = Quaternion.Euler(0f, rng.NextFloat(0f, 360f), 0f) * rotation;

            GameObject go = GameObject.CreatePrimitive(primitive);
            go.transform.SetParent(parent);
            go.transform.localScale = scale;
            go.transform.rotation = rotation;
            go.transform.position = position + Vector3.up * GetHalfHeight(primitive, scale, rotation); // 프리미티브 종류별 반높이만큼 띄워 지형 위에 놓이게 한다
            go.name = $"Resource_{itemName}";

            // 아이템 종류(무기/음식/음료/설치형/이동수단/일반 재료)에 맞는 색을 입혀 카테고리 단위로 구분한다.
            Color color = GetWorldSurfaceColor(itemName, UIBuilder.GetItemCategoryColor(yieldItem));
            string textureName = GetSurfaceTextureName(yieldItem);

            // [B28] 프리미티브 그대로는 표현할 수 없는 형태(마디 있는 대나무 줄기, 옹이 있는 나뭇가지,
            // 각진 돌 파편)만 절차 메시로 갈아 끼운다. 메시는 ResourceVisualLibrary가 **정적으로 캐시**하고
            // (섬 9개 전체가 30장을 공유한다) 프리미티브의 로컬 규격(실린더 |y|<=1 / 큐브·구 |v|<=0.5)을
            // 그대로 지키도록 만들어져 있어서, 콜라이더·GetHalfHeight·ResourceNode.RootTopLocalY 계산이
            // 하나도 달라지지 않는다.
            ApplyRootMesh(go, itemName, shapeVariant);
            WidenHarvestCollider(go, itemName, scale);

            var renderer = go.GetComponent<Renderer>();
            if (renderer != null)
            {
                // [B28] renderer.material은 접근할 때마다 머티리얼을 **복제**한다. 특대 섬 하나에 노드가
                // 100개 가까이 깔리므로 예전에는 노드 수만큼 머티리얼 인스턴스가 생겼고, 파츠까지 합치면
                // 섬 하나에 300개가 넘었다(AGENT_BRIEF 4장 "머티리얼을 파츠마다 만들지 마라"). 색+텍스처
                // 조합으로 캐시한 공유 머티리얼을 sharedMaterial에 그대로 꽂아 월드 전체가 수십 개를
                // 나눠 쓰게 한다. 텍스처가 아직 없으면(Resources.Load가 null) CreateColorMaterial이
                // 조용히 단색으로 넘어간다 - 이 경로에서는 아무 일도 일어나지 않는다.
                renderer.sharedMaterial = ResourceVisualLibrary.GetMaterial(color, textureName);
            }

            AddResourceDetailParts(go, itemName, scale, color, textureName, rng);

            var node = go.AddComponent<ResourceNode>();
            node.yieldItem = yieldItem;
            node.remainingHarvestCount = node.maxHarvestCount;
            node.requiresTool = entry.requiresTool;
            node.requiredTool = entry.requiredTool;
            node.bonusTool = entry.bonusTool;
            node.bonusYieldPerHarvest = entry.bonusYieldPerHarvest;
            node.islandIndex = islandIndex;
            node.spawnOrder = spawnOrder;
            return node;
        }

        /// <summary>
        /// 자원 종류별로 실제 사용할 프리미티브 형태/크기/기울기를 정한다. 예전엔 전부 큐브(1x1.5x1)
        /// 하나뿐이었던 것을, 원기둥(대나무/부력통/연료 스파우트 등)·구(돌/코코넛)·납작한 큐브(천/금속조각)
        /// 등으로 나눠 실루엣만 봐도 어떤 자원인지 구분할 수 있게 했다.
        /// </summary>
        private void GetNodeShape(string itemName, out PrimitiveType primitive, out Vector3 scale, out Quaternion rotation)
        {
            rotation = Quaternion.identity;

            switch (itemName)
            {
                case "나뭇가지": // 굵은 가지 하나(옹이·테이퍼는 ApplyRootMesh) + 흩어진 잔가지 2~4개(AddResourceDetailParts)
                    primitive = PrimitiveType.Cylinder;
                    scale = new Vector3(0.09f, 0.32f, 0.09f);
                    break;
                case "대나무": // 한 포기의 중심 줄기. 마디는 **메시 안에** 있고(ApplyRootMesh), 곁줄기 2~4개와 잎다발은 AddResourceDetailParts
                    // [B29 감독 보고 "대나무가 너무 짧음"] 1.05 → 2.10.
                    // 실린더는 로컬 높이가 2(=-1~+1)이므로 **총 높이 = scale.y × 2**다. 2.10이면 4.2m이고,
                    // 세로 지터(0.85~1.25)까지 합치면 3.57~5.25m - 눈높이 1.6m를 한참 올려다보게 된다
                    // (예전은 1.79~2.63m로 사람 키 남짓이었다).
                    // 가로 0.14 → 0.30은 두 가지를 동시에 노린다:
                    //  (1) **콜라이더 = 채집 판정**이다. CreatePrimitive가 붙인 캡슐의 반지름은
                    //      0.5 × scale.x이므로 지름이 0.14m → 0.30m가 된다. 눈에 보이는 포기 폭(곁줄기가
                    //      중심에서 0.34m까지 퍼진다)에 비해 판정이 너무 가늘다는 지적을 여기서 갚는다.
                    //      높이도 2.1 → 4.2m가 되어 올려다보는 각도에서도 줄기 어디를 조준하든 맞는다.
                    //  (2) 보이는 줄기 굵기는 콜라이더와 **분리해서** 정한다 - BambooCulmUnit의 메시
                    //      반지름을 0.34 → 0.22로 함께 줄였으므로 실제로 보이는 지름은 0.136m가 아니라
                    //      0.132m다(예전 0.095m). 높이가 2배가 됐는데 굵기가 그대로면 국수 가락이 되고,
                    //      정비례로 2배(0.19m)면 통나무가 된다. 세장비 22 → 32로 **더 늘씬해지되**
                    //      절대 굵기는 1.4배로 함께 키운 값이다.
                    primitive = PrimitiveType.Cylinder;
                    scale = new Vector3(0.30f, 2.10f, 0.30f);
                    break;
                case "돌조각": // 각진 파편 무더기 (파편 형태는 ApplyRootMesh, 곁돌 2~3개는 AddResourceDetailParts)
                    primitive = PrimitiveType.Sphere;
                    scale = new Vector3(0.5f, 0.32f, 0.5f);
                    break;
                case "부싯돌": // 얇고 각진 석기 파편 - 살짝 비스듬히 기울여 둠 (형태는 ApplyRootMesh)
                    primitive = PrimitiveType.Cube;
                    scale = new Vector3(0.32f, 0.1f, 0.42f);
                    rotation = Quaternion.Euler(8f, 25f, -5f);
                    break;
                case "코코넛": // 둥근 열매 (여분 하나는 AddResourceDetailParts에서 추가)
                    primitive = PrimitiveType.Sphere;
                    scale = new Vector3(0.42f, 0.42f, 0.42f);
                    break;
                case "천조각": // 얇고 넓은 천 조각
                    primitive = PrimitiveType.Cube;
                    scale = new Vector3(0.55f, 0.05f, 0.4f);
                    break;
                case "야자잎": // 짧은 잎자루 위로 잎맥 있는 잎 3장이 부채꼴로 퍼짐 (AddResourceDetailParts에서 추가)
                    // 이 스케일은 **보이는 잎자루(지름 5cm · 높이 16cm)**의 크기이고, 잎 3장은 여기에
                    // 포함되지 않는다(AddMeshPart가 부모 스케일을 정확히 상쇄해 미터 메시를 그대로 세운다).
                    // 그래서 채집 판정을 이 스케일에 맡기면 지름 5cm짜리 막대만 맞고 폭 1m가 넘는 잎은
                    // 통째로 허공이 된다 - 조준이 거의 안 맞던 실제 원인이다.
                    // **콜라이더는 WidenHarvestCollider가 따로 넓힌다**(대나무에서 검증한 "보이는 굵기와
                    // 채집 판정을 분리한다" 규칙과 같다). 이 값을 키워서 고치려 하지 마라 - 잎자루가
                    // 함께 굵어지고, 잎이 붙는 높이(stemTop = parentScale.y × 0.9)까지 같이 올라간다.
                    primitive = PrimitiveType.Cylinder;
                    scale = new Vector3(0.05f, 0.08f, 0.05f);
                    break;
                case "금속조각": // 찌그러진 얇은 금속판
                    primitive = PrimitiveType.Cube;
                    scale = new Vector3(0.5f, 0.06f, 0.34f);
                    rotation = Quaternion.Euler(6f, 20f, 0f);
                    break;
                case "부력통": // 짧고 통통한 드럼통 형태
                    primitive = PrimitiveType.Cylinder;
                    scale = new Vector3(0.42f, 0.42f, 0.42f);
                    break;
                case "비상식량": // 작은 배급 상자
                    primitive = PrimitiveType.Cube;
                    scale = new Vector3(0.34f, 0.22f, 0.26f);
                    break;
                case "연료": // 각진 연료통 몸체 (주둥이는 AddResourceDetailParts에서 추가)
                    primitive = PrimitiveType.Cube;
                    scale = new Vector3(0.28f, 0.4f, 0.22f);
                    break;
                case "엔진부품": // 짧은 원판형 부품 (볼트는 AddResourceDetailParts에서 추가)
                    primitive = PrimitiveType.Cylinder;
                    scale = new Vector3(0.3f, 0.22f, 0.3f);
                    break;
                case "생수": // 표류한 생수병 (목/뚜껑은 AddResourceDetailParts에서 추가)
                    // [B28 버그 수정] 씬 resourceEntries 13번째 항목이 생수인데(baseCount 1, 중형 섬 이상)
                    // 여기에 case가 없어서 default로 떨어졌다 - 중형 이상 섬마다 **1×1.5×1m짜리 파란 큐브**가
                    // 2~4개씩 서 있었다. 다른 자원 노드(0.2~0.5m)의 세 배 크기라 멀리서 보면 건축물처럼
                    // 보인다. 실제 크기(지름 0.14m · 높이 0.28m)의 병으로 바꾼다.
                    primitive = PrimitiveType.Cylinder;
                    scale = new Vector3(0.07f, 0.14f, 0.07f);
                    break;
                default: // 목록에 없는 새 자원이 추가되면 기존 큐브로 안전하게 폴백
                    primitive = PrimitiveType.Cube;
                    scale = new Vector3(1f, 1.5f, 1f);
                    break;
            }
        }

        /// <summary>
        /// 프리미티브 종류별 로컬 단위 형태 차이를 감안해, 지정한 스케일일 때 피벗(중심)을 지면 위
        /// 몇 미터에 둬야 바닥이 정확히 지면에 닿는지 계산한다. 큐브/구는 반높이가 scale.y*0.5인데
        /// 실린더는 기본 높이가 2(로컬 -1~+1)라서 반높이가 scale.y*1이다 - 이 차이를 반영하지 않으면
        /// 프리미티브 종류에 따라 절반이 땅에 묻히거나 붕 떠 보인다.
        ///
        /// [B28] 회전을 함께 받는다. 예전에는 스케일의 y성분만 봤기 때문에, **기울여 놓은** 자원
        /// (부싯돌 Euler(8,25,-5) · 금속조각 Euler(6,20,0))은 기울어진 만큼 모서리가 지면 아래로
        /// 파고들었다. Y축만 도는 자원은 아래 식이 예전 값과 **정확히 같은 값**을 돌려주므로
        /// (회전 행렬 2행이 (0,1,0)이라 y항만 남는다) 기존 배치는 1mm도 움직이지 않는다.
        /// </summary>
        private float GetHalfHeight(PrimitiveType primitive, Vector3 scale, Quaternion rotation)
        {
            // 회전 행렬의 2행 = 로컬 축들이 월드 Y에 기여하는 비율.
            Matrix4x4 basis = Matrix4x4.Rotate(rotation);
            float rx = basis.m10;
            float ry = basis.m11;
            float rz = basis.m12;

            if (primitive == PrimitiveType.Sphere)
            {
                // 구(타원체)는 상자 근사를 쓰면 회전할 때마다 최대 73%까지 과대평가되어 공중에 뜬다.
                // 타원체의 지지함수는 정확히 아래 형태라 회전과 무관하게 딱 맞는다.
                float ex = rx * scale.x * 0.5f;
                float ey = ry * scale.y * 0.5f;
                float ez = rz * scale.z * 0.5f;
                return Mathf.Sqrt(ex * ex + ey * ey + ez * ez);
            }

            float halfY = primitive == PrimitiveType.Cylinder || primitive == PrimitiveType.Capsule ? 1f : 0.5f;
            return Mathf.Abs(rx) * scale.x * 0.5f + Mathf.Abs(ry) * scale.y * halfY + Mathf.Abs(rz) * scale.z * 0.5f;
        }

        /// <summary>
        /// 루트 프리미티브의 메시를 자원 종류 전용 절차 메시로 갈아 끼운다(해당 종류가 없으면 그대로 둔다).
        ///
        /// 지켜야 하는 계약이 하나 있다: **메시는 프리미티브의 로컬 규격을 벗어나지 않는다.**
        /// 실린더/캡슐은 y가 -1~+1, 큐브/구는 -0.5~+0.5다. 이 규격을 지키는 한
        /// (a) CreatePrimitive가 붙여 준 콜라이더(= 채집 판정 범위)와 (b) 위 GetHalfHeight의 지면 접지
        /// 계산과 (c) ResourceNode.RootTopLocalY의 파츠 부착 기준이 전부 예전 값 그대로 유지된다.
        /// 콜라이더는 손대지 않는다 - 형태만 바뀌고 채집 판정 범위는 이전과 동일하다.
        /// </summary>
        private void ApplyRootMesh(GameObject go, string itemName, int variant)
        {
            Mesh mesh = null;
            switch (itemName)
            {
                case "대나무": mesh = ResourceVisualLibrary.BambooCulmUnit(variant); break;
                case "나뭇가지": mesh = ResourceVisualLibrary.BranchStickUnit(variant); break;
                case "돌조각": mesh = ResourceVisualLibrary.RockChunkUnit(variant); break;
                case "부싯돌": mesh = ResourceVisualLibrary.StoneFlakeUnit(variant); break;
            }

            if (mesh == null)
                return;

            var filter = go.GetComponent<MeshFilter>();
            if (filter != null)
                filter.sharedMesh = mesh;
        }

        /// <summary>
        /// **채집 판정 콜라이더만** 실제 실루엣에 맞춰 넓힌다. 보이는 메시는 한 폴리곤도 건드리지 않는다.
        ///
        /// 대부분의 자원은 루트 프리미티브 자체가 실루엣의 대부분이라 "루트 스케일 = 콜라이더"로 충분하다
        /// (돌조각: 구 반지름 0.25m, 대나무: 캡슐 반지름 0.15m). 그런데 야자잎만은 루트가 **잎자루**이고
        /// 실루엣의 99%가 AddMeshPart로 붙는 잎 3장이다. AddMeshPart는 부모 스케일을 정확히 상쇄하므로
        /// (자식 localScale = S⁻¹) 잎 크기는 루트 스케일과 완전히 독립이고, 결과적으로 지름 5cm짜리
        /// 콜라이더가 폭 1m가 넘는 잎을 대표하고 있었다.
        ///
        /// 여기서 콜라이더 필드(=로컬 단위)를 직접 조정한다. 루트 스케일을 키우는 방식은 잎자루 굵기와
        /// 잎이 붙는 높이가 함께 변해 승인된 디자인이 바뀌므로 쓸 수 없다.
        ///
        /// 목표 반지름 0.24m의 근거: 잎 길이가 0.44~0.58m(FrondMeters 변주 0~2)이므로 잎 하나의 안쪽
        /// 절반을 덮는 값이고, 대나무(보이는 포기 반경 0.34m에 콜라이더 반지름 0.15m)와 같은 비율대다.
        /// 예전 판정(반지름 0.025m)의 약 10배다.
        ///
        /// 캡슐은 높이(0.16~0.20m)보다 지름(0.48m)이 커서 Unity가 **구로 클램프**한다 - 잎이 사방으로
        /// 퍼진 납작한 부채꼴에는 오히려 이쪽이 맞다. 지터(scale.x = scale.z)를 나눠 주므로 개체마다
        /// 판정 크기가 흔들리지 않고 항상 정확히 0.24m다(잎 메시가 미터 고정이라 그게 맞다).
        /// </summary>
        private void WidenHarvestCollider(GameObject go, string itemName, Vector3 scale)
        {
            if (itemName != "야자잎")
                return;

            var capsule = go.GetComponent<CapsuleCollider>();
            if (capsule == null)
                return;

            const float FrondHitRadiusMeters = 0.24f;

            // CapsuleCollider의 월드 반지름 = radius x max(|scale.x|, |scale.z|).
            float horizontal = Mathf.Max(0.0001f, Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z)));
            capsule.radius = FrondHitRadiusMeters / horizontal;
        }

        /// <summary>
        /// 자원 종류별 보조 파츠를 덧붙여 기본 프리미티브만으로는 부족한 디테일(마디/부채꼴 잎/볼트 등)을
        /// 더한다. 파츠는 순수 시각용이라 콜라이더를 만들지 않고(AddPart에서 제거), 부모의 상호작용용
        /// 콜라이더와 절대 간섭하지 않는다.
        /// </summary>
        private void AddResourceDetailParts(GameObject go, string itemName, Vector3 parentScale, Color color, string textureName, System.Random rng)
        {
            switch (itemName)
            {
                case "나뭇가지":
                    // [B28] "주워 모을 잔가지 더미"로 다시 만들었다. 예전에는 세운 원기둥에 곁가지를
                    // Euler(15,0,±55~135)로 붙였는데, 부모 스케일이 (0.09, 0.32, 0.09)라 x:y가 3.5:1이고
                    // **비균일 스케일 부모 밑에서 회전한 자식은 전단(shear)으로 찌그러진다** - 굵기가 각도에
                    // 따라 3배까지 변해서 가지가 아니라 구부러진 리본으로 보였다.
                    // 지금은 기울기를 메시에 구워 넣고(AddMeshPart 주석 참고) 자식에는 Y 회전만 준다.
                    // 길이(0.26~0.52m)·굵기(2.2~4.0cm)·들린 각도(12~70도)·갈래 유무가 변주 6종에 나뉘어 있어
                    // 같은 더미 안에서도 굵기와 각도가 제각각으로 읽힌다.
                    {
                        float groundY = -parentScale.y; // 루트 실린더 바닥(로컬 -1)까지의 미터 거리
                        int twigCount = rng.NextInt(2, 5);
                        for (int i = 0; i < twigCount; i++)
                        {
                            float around = rng.NextFloat(0f, 360f) * Mathf.Deg2Rad;
                            float dist = rng.NextFloat(0.015f, 0.085f);
                            Vector3 offset = new Vector3(Mathf.Cos(around) * dist, groundY + 0.008f, Mathf.Sin(around) * dist);
                            Material material = ResourceVisualLibrary.GetMaterial(
                                ResourceVisualLibrary.Shade(color, TwigTints[i % TwigTints.Length]), textureName);
                            AddMeshPart(go, $"Twig{i}", offset, parentScale,
                                ResourceVisualLibrary.TwigMeters(rng.NextInt(0, 6)), material, rng.NextFloat(0f, 360f));
                        }
                        break;
                    }

                case "대나무":
                    // [B28 최우선 과제] 밋밋한 원통 하나 → **한 포기(3~5줄기)**.
                    // 예전 마디는 `worldSize (1.15, 0.02, 1.15)`였는데 AddPart의 worldSize는 로컬 배수가
                    // 아니라 **미터**다(아래 AddPart 주석). 즉 지름 14cm 줄기에 지름 **1.15m짜리 원반**이
                    // 2~3장 꽂혀 있었다 - 이게 대나무가 대나무로 안 보이던 진짜 원인이다.
                    // 지금 마디는 파츠가 아니라 줄기 메시 자체의 굵기 변화다(마디 아래를 0.93으로 조이고
                    // 마디에서 1.22로 부풀린다). 파츠를 하나도 쓰지 않으므로 줄기당 3~5마디가 공짜다.
                    {
                        // [B29] groundY는 **미터**이고 루트 피벗이 지면 위 parentScale.y(=반높이)에 있으므로,
                        // 루트 스케일을 키워도 곁줄기 밑동은 자동으로 지면에 붙는다(계산을 다시 할 필요가 없다).
                        float groundY = -parentScale.y;

                        // ── [B48] 실물 대나무 모델(bamboo_a/b/c) ────────────────────────────
                        //  · 모델 하나가 이미 **한 포기**(줄기 여러 대 + 잎)라, 절차 곁줄기·잎다발을 통째로
                        //    대체한다. 파츠는 최대 8개 → 2개다.
                        //  · 목표 높이는 **이미 뽑아 둔 세로 지터**가 정한 루트 높이(parentScale.y × 2 =
                        //    3.57~5.25m)다. 새 난수는 0회이고, 변종 선택도 그 값으로 결정론적으로 한다.
                        //  · 크기 규약: 모델은 이미 미터 규격(밑면 y=0)이다. 그래서 AddMeshPart의 미터
                        //    좌표계에 **fit = 목표 높이 / 모델 실측 높이**의 균등 배율만 곱한다(1.06~1.18).
                        //  · ★ 채집 콜라이더는 손대지 않는다 ★ 루트 캡슐(지름 0.30m × 세로 지터)이 조준
                        //    판정이고, 여기서 만드는 것은 콜라이더가 없는 순수 시각 파츠뿐이다.
                        float clumpHeight = parentScale.y * 2f;
                        Mesh bambooCulms, bambooLeaves;
                        float bambooModelHeight;
                        bool useBambooModel = ResourceVisualLibrary.TryGetBambooModel(
                            clumpHeight, out bambooCulms, out bambooLeaves, out bambooModelHeight);

                        int culmCount = rng.NextInt(2, 5); // 루트 줄기 + 2~4 = 한 포기에 3~5줄기
                        for (int i = 0; i < culmCount; i++)
                        {
                            float around = rng.NextFloat(0f, 360f) * Mathf.Deg2Rad;
                            // [B29] 0.07~0.19m → 0.12~0.34m. 줄기가 2배 길어졌는데 밑동 간격이 그대로면
                            // 한 다발로 뭉쳐 굵은 기둥 하나처럼 보인다(기울기도 함께 2배로 키웠다).
                            float dist = rng.NextFloat(0.12f, 0.34f);
                            // ★ [B48] 난수 소비 불변 ★ 아래 두 draw(메시 변주 · 방위각)는 모델 경로에서도
                            // **반드시 여기서 뽑는다.** 인자 안에서 뽑던 것을 지역변수로 끌어낸 이유가 그것이다
                            // (바위에서 쓴 방법). 한 번이라도 덜 뽑으면 같은 worldSeed에서 뒤따르는 노드의
                            // 위치·지터가 통째로 밀리고, spawnOrder가 세이브 키라 기존 월드가 어긋난다.
                            int culmVariant = rng.NextInt(0, 5);
                            float culmYaw = rng.NextFloat(0f, 360f);
                            if (useBambooModel)
                                continue;

                            Vector3 offset = new Vector3(Mathf.Cos(around) * dist, groundY, Mathf.Sin(around) * dist);
                            Material material = ResourceVisualLibrary.GetMaterial(
                                ResourceVisualLibrary.Shade(color, CulmTints[i % CulmTints.Length]), textureName);
                            AddMeshPart(go, $"Culm{i}", offset, parentScale,
                                ResourceVisualLibrary.BambooCulmMeters(culmVariant), material, culmYaw);
                        }

                        // 잎 다발: 실루엣 위쪽을 깨 주는 역할이라 성기게 붙인다(대나무 잎은 작고 성기다).
                        // 살아 있는 잎이므로 팔레트의 Frond Green을 쓴다 - 줄기(B48 이후 Bamboo Culm
                        // 황록색)와 색이 갈라져야 "줄기 + 잎"으로 읽힌다.
                        // [B29] 1~2 → 2~3. 4~5m 줄기 꼭대기에 잎다발이 하나뿐이면 위쪽이 텅 빈 장대가 된다.
                        // 파츠 예산은 루트 1 + 곁줄기 2~4 + 잎 2~3 = 최대 8로 ClumpVisualPrimitives(8)와 정확히 같다.
                        // 붙는 높이는 parentScale.y에 비례하는 식이라(아래) 루트가 커진 만큼 저절로 따라 올라간다.
                        int sprigCount = rng.NextInt(2, 4);
                        Material leafMaterial = ResourceVisualLibrary.GetMaterial(StructureVisualBuilder.FrondGreen, "frond");
                        for (int i = 0; i < sprigCount; i++)
                        {
                            // ★ [B48] 난수 소비 불변 ★ 위 곁줄기 루프와 같은 이유로 두 draw를 먼저 뽑는다.
                            float sprigT = rng.NextFloat(0.62f, 0.94f);
                            float sprigYaw = rng.NextFloat(0f, 360f);
                            if (useBambooModel)
                                continue;

                            float height = groundY + sprigT * parentScale.y * 2f;
                            AddMeshPart(go, $"Sprig{i}", new Vector3(0f, height, 0f), parentScale,
                                ResourceVisualLibrary.FrondMeters(3 + (i % 2)), leafMaterial, sprigYaw);
                        }

                        if (useBambooModel)
                        {
                            // 루트 실린더(절차 줄기)는 **그리지 않는다.** 메시와 콜라이더는 그대로 둔 채
                            // 렌더러만 끈다 - ResourceNode.RootTopLocalY와 GetHalfHeight가 루트 메시의
                            // 경계상자에 걸려 있어서, 메시를 지우면 접지·파츠 높이 계산이 조용히 어긋난다.
                            var rootRenderer = go.GetComponent<MeshRenderer>();
                            if (rootRenderer != null)
                                rootRenderer.enabled = false;

                            float fit = clumpHeight / Mathf.Max(0.01f, bambooModelHeight);
                            // 방위각은 0으로 둔다. 루트 오브젝트가 이미 무작위 Y 회전을 갖고 있어(SpawnSingleNode)
                            // 포기 전체가 그대로 돌아간다 - 여기서 rng를 더 뽑을 이유가 없다.
                            var culmPart = AddMeshPart(go, "BambooModelCulms", new Vector3(0f, groundY, 0f),
                                parentScale, bambooCulms,
                                ResourceVisualLibrary.GetMaterial(color, textureName), 0f, fit);

                            if (bambooLeaves != null)
                            {
                                AddMeshPart(go, "BambooModelLeaves", new Vector3(0f, groundY, 0f),
                                    parentScale, bambooLeaves, leafMaterial, 0f, fit);
                            }
                            else if (bambooCulms.subMeshCount >= 2 && culmPart != null)
                            {
                                // 임포터가 `o` 2개를 한 메시의 서브메시로 합쳐 온 경우 - 렌더러 하나에
                                // 머티리얼 두 장을 주면 줄기/잎이 각각 칠해진다(메시를 새로 만들지 않는다).
                                var culmRenderer = culmPart.GetComponent<MeshRenderer>();
                                if (culmRenderer != null)
                                {
                                    culmRenderer.sharedMaterials = new[]
                                    {
                                        ResourceVisualLibrary.GetMaterial(color, textureName),
                                        leafMaterial
                                    };
                                }
                            }
                        }
                        break;
                    }

                case "돌조각":
                    // [B28] 곁돌 개수를 0~3 → 2~3으로 올려 "무더기"의 최소 밀도를 보장한다(0개가 나오면
                    // 눌린 구 하나뿐이라 무더기로 읽히지 않았다). 대신 곁돌도 루트와 같은 각진 파편 메시를
                    // 쓰고 살짝 파묻히게 놓아, 개수가 늘어도 실루엣이 지저분해지지 않는다.
                    {
                        int rockCount = rng.NextInt(2, 4);
                        Vector3[] offsets = { new Vector3(0.60f, -0.25f, 0.22f), new Vector3(-0.52f, -0.30f, -0.28f), new Vector3(0.15f, -0.22f, -0.62f) };
                        for (int i = 0; i < rockCount && i < offsets.Length; i++)
                        {
                            float size = rng.NextFloat(0.18f, 0.30f);
                            AddPart(go, $"Rock{i + 2}", PrimitiveType.Sphere, offsets[i], new Vector3(size, size * 0.72f, size),
                                parentScale, Quaternion.identity,
                                ResourceVisualLibrary.Shade(color, RockTints[i % RockTints.Length]), textureName,
                                ResourceVisualLibrary.RockChunkUnit(rng.NextInt(0, 4)));
                        }
                        break;
                    }

                case "코코넛":
                    // 퀄리티 개선: 열매가 1개짜리 노드도, 2개까지 뭉친 노드도 나오게 해서 다발 크기가 다양해 보이게 했다.
                    // [B28 파츠 예산] 여분 열매 0~2 → 0~1. 코코넛은 이미 구 하나로 충분히 읽히는 유일한
                    // 자원이라(다른 자원과 실루엣이 겹치지 않는다) 여기서 아낀 예산을 대나무 포기에 넘긴다.
                    {
                        int extraCount = rng.NextInt(0, 2);
                        Vector3[] offsets = { new Vector3(0.4f, -0.05f, 0.1f), new Vector3(-0.35f, -0.08f, 0.25f) };
                        for (int i = 0; i < extraCount && i < offsets.Length; i++)
                            AddPart(go, $"Coconut{i + 2}", PrimitiveType.Sphere, offsets[i], new Vector3(0.38f, 0.38f, 0.38f), parentScale, Quaternion.identity, color, textureName);
                        break;
                    }

                case "천조각":
                    // 퀄리티 개선: 접힌 주름이 있을 때도(70% 확률) 없을 때도 있게 해 밋밋한 조각과 구겨진 조각이 섞여 보이게 했다.
                    if (rng.NextValue01() < 0.7f)
                        AddPart(go, "Fold", PrimitiveType.Cube, new Vector3(0.05f, 0.3f, -0.05f), new Vector3(0.4f, 0.05f, 0.3f), parentScale, Quaternion.Euler(0f, rng.NextFloat(0f, 36f), 3f), ResourceVisualLibrary.Shade(color, 0.92f), textureName);
                    break;

                case "야자잎":
                    {
                        // [B28] 예전 잎은 `Cube (0.05, 0.02, 0.45~0.62)` - 두께 2cm짜리 **납작한 판**이었고,
                        // 게다가 Euler(-20, angle, 0)로 돌린 자식이라 부모 스케일(0.05, 0.08, 0.05)의
                        // y:z = 1.6:1 비대칭 때문에 살짝 찌그러져 있었다.
                        // 지금은 잎 한 장이 "잎맥(중앙 리브) + 좌우로 갈라진 잎깃 5~7쌍"으로 된 메시 한 장이다.
                        // 톱니 실루엣이 생겨 멀리서도 야자잎으로 읽히고, 잎깃은 양면이라 아래에서 봐도 사라지지
                        // 않는다(단면 메시가 컬링되어 없어지는 사고 방지). 기울기·처짐은 전부 메시에 구워
                        // 넣었으므로 자식 회전은 부채꼴 각도(Y)뿐이다 - 전단이 원리적으로 생기지 않는다.
                        const int leafCount = 3;
                        float spread = rng.NextFloat(96f, 148f); // 부채꼴 전체 펼침 각도
                        float baseYaw = rng.NextFloat(0f, 360f);
                        float stemTop = parentScale.y * 0.9f; // 줄기(실린더) 꼭대기 = 로컬 +1
                        for (int i = 0; i < leafCount; i++)
                        {
                            float yaw = baseYaw - spread * 0.5f + spread * i / (leafCount - 1) + rng.NextFloat(-7f, 7f);
                            Material material = ResourceVisualLibrary.GetMaterial(
                                ResourceVisualLibrary.Shade(color, FrondTints[i % FrondTints.Length]), textureName);
                            AddMeshPart(go, $"Frond{i}", new Vector3(0f, stemTop, 0f), parentScale,
                                ResourceVisualLibrary.FrondMeters(rng.NextInt(0, 3)), material, yaw);
                        }
                        break;
                    }

                case "금속조각":
                    // 퀄리티 개선: 구부러진 정도(각도)를 무작위로 바꿔 찌그러진 모양이 조금씩 다르게 보이게 했다.
                    AddPart(go, "Bend", PrimitiveType.Cube, new Vector3(-0.05f, 0.4f, 0.05f), new Vector3(0.32f, 0.06f, 0.22f), parentScale, Quaternion.Euler(0f, rng.NextFloat(-50f, -20f), 8f), ResourceVisualLibrary.Shade(color, 0.85f), textureName);
                    break;

                case "부력통":
                    // [B28 버그 수정] worldSize는 로컬 배수가 아니라 **미터**다. 1.08을 넘기고 있어서
                    // 지름 0.42m 드럼통에 지름 **1.08m짜리 원반**이 꽂혀 있었다(대나무 마디와 같은 실수).
                    // 드럼통보다 살짝 큰 테(0.47m)로 고친다 - 파츠 개수는 그대로 1개다.
                    AddPart(go, "Rim", PrimitiveType.Cylinder, new Vector3(0f, 0.85f, 0f), new Vector3(0.47f, 0.05f, 0.47f), parentScale, Quaternion.identity, ResourceVisualLibrary.Shade(color, 0.8f), textureName);
                    break;

                case "비상식량":
                    AddPart(go, "Label", PrimitiveType.Cube, new Vector3(0f, 0.1f, 0.51f), new Vector3(0.26f, 0.1f, 0.02f), parentScale, Quaternion.identity, ResourceVisualLibrary.Shade(Color.white, 0.9f), "noise");
                    break;

                case "연료":
                    AddPart(go, "Spout", PrimitiveType.Cylinder, new Vector3(0.08f, 0.62f, 0f), new Vector3(0.14f, 0.12f, 0.14f), parentScale, Quaternion.identity, ResourceVisualLibrary.Shade(color, 0.85f), textureName);
                    break;

                case "생수":
                    // [B28] 병목 + 뚜껑. 원기둥 하나만으로는 부력통/엔진부품과 실루엣이 겹치는데,
                    // 위로 갈수록 가늘어지는 2단 실루엣은 이 자원에만 있다.
                    // 위치는 부모 로컬 단위라(아래 AddPart 주석) 세로 지터가 붙어도 몸통을 정확히 따라간다.
                    AddPart(go, "Neck", PrimitiveType.Cylinder, new Vector3(0f, 1.16f, 0f), new Vector3(0.036f, 0.022f, 0.036f),
                        parentScale, Quaternion.identity, ResourceVisualLibrary.Shade(color, 0.88f), textureName);
                    AddPart(go, "Cap", PrimitiveType.Cylinder, new Vector3(0f, 1.42f, 0f), new Vector3(0.05f, 0.014f, 0.05f),
                        parentScale, Quaternion.identity, ResourceVisualLibrary.Shade(color, 0.6f), textureName);
                    break;

                case "엔진부품":
                    // 퀄리티 개선: 볼트 개수를 무작위로 바꿔 부품마다 조립 상태가 달라 보이게 했다.
                    // [tech-artist-B 요청 - 파츠 예산] 3~6개 → 2~3개 (야자잎 주석의 근거와 동일).
                    // 볼트는 원주 배치라 개수가 줄어도 360/boltCount 간격이 자동으로 벌어져 형태가 깨지지 않는다.
                    {
                        int boltCount = rng.NextInt(2, 4);
                        for (int i = 0; i < boltCount; i++)
                        {
                            float rad = i * (360f / boltCount) * Mathf.Deg2Rad;
                            Vector3 localPos = new Vector3(Mathf.Cos(rad) * 0.24f, 0.05f, Mathf.Sin(rad) * 0.24f);
                            AddPart(go, $"Bolt{i}", PrimitiveType.Cube, localPos, new Vector3(0.06f, 0.06f, 0.06f), parentScale, Quaternion.identity, ResourceVisualLibrary.Shade(color, 0.8f), textureName);
                        }
                        break;
                    }
            }
        }

        /// <summary>
        /// 순수 시각용 보조 파츠 하나를 만들어 parent의 자식으로 붙인다. worldSize를 parentScale로 나눠
        /// 자식의 localScale로 지정하면, 부모가 비균일 스케일(예: 얇고 넓은 큐브)이어도 파츠가 찌그러지지
        /// 않고 의도한 크기로 보인다(CreatureSpawner.AddCompensated와 동일한 보정 방식).
        /// 자동으로 붙는 콜라이더는 즉시 제거해 부모의 상호작용용 콜라이더와 간섭하지 않게 한다.
        ///
        /// **단위 주의(사고 2건의 원인):** localPosition은 **부모 로컬 단위**(실린더 y=1이 곧 꼭대기)인데
        /// worldSize는 **미터**다. 이 둘을 헷갈려 대나무 마디(1.15)와 부력통 테(1.08)가 각각 지름 1m가
        /// 넘는 원반으로 나와 있었다. 새 파츠를 넣을 때는 worldSize에 반드시 실제 미터 값을 적어라.
        ///
        /// meshOverride를 주면 프리미티브 메시 대신 그 메시를 쓴다. 메시가 프리미티브의 로컬 규격
        /// (큐브/구 |v|<=0.5, 실린더 |y|<=1)을 지키면 worldSize의 의미가 그대로 유지된다.
        /// </summary>
        private void AddPart(GameObject parent, string name, PrimitiveType primitive, Vector3 localPosition,
            Vector3 worldSize, Vector3 parentScale, Quaternion localRotation, Color color, string textureName,
            Mesh meshOverride = null)
        {
            var part = GameObject.CreatePrimitive(primitive);
            part.name = name;
            part.transform.SetParent(parent.transform, false);
            part.transform.localPosition = localPosition;
            part.transform.localRotation = localRotation;
            part.transform.localScale = new Vector3(
                worldSize.x / Mathf.Max(0.0001f, parentScale.x),
                worldSize.y / Mathf.Max(0.0001f, parentScale.y),
                worldSize.z / Mathf.Max(0.0001f, parentScale.z));

            var collider = part.GetComponent<Collider>();
            if (collider != null)
                Object.Destroy(collider);

            if (meshOverride != null)
            {
                var filter = part.GetComponent<MeshFilter>();
                if (filter != null)
                    filter.sharedMesh = meshOverride;
            }

            // [B28] renderer.material(복제)에서 공유 머티리얼로. SpawnSingleNode의 루트 주석과 같은 이유다.
            var renderer = part.GetComponent<Renderer>();
            if (renderer != null)
                renderer.sharedMaterial = ResourceVisualLibrary.GetMaterial(color, textureName);
        }

        /// <summary>
        /// **미터 단위로 만들어 둔 절차 메시**를 파츠 하나로 붙인다(대나무 줄기·잔가지·야자잎 전용).
        ///
        /// 왜 별도 경로가 필요한가 - 이 프로젝트에서 반복된 "기울인 자식이 찌그러진다" 사고의 근본 원인:
        /// 부모 스케일이 S = diag(a, b, a)이고 자식이 회전 R을 가지면 합성 행렬에 S·R이 들어가는데,
        /// a != b면 X/Z축 회전에서 전단(shear)이 생긴다(대나무 x:y = 1:7.5, 나뭇가지 1:3.5).
        /// 여기서는 자식 스케일을 **정확히 S⁻¹**로 두어 합성 스케일을 1로 만든다. 그러면
        ///   v_world = S·t + R_y·v_mesh
        /// 가 되어 (1) 메시 좌표가 곧 미터이고 (2) Y 회전은 a == a 덕분에 스케일과 교환되어 **정확한 회전**이
        /// 된다. 기울기·굽음은 전부 메시에 구워 넣고 여기서는 방위각(Y)만 돌리므로 전단이 원리적으로 없다.
        /// (부모의 가로 지터를 x/z 공통으로 바꾼 것이 이 성질의 전제다 - SpawnSingleNode 주석 참고.)
        ///
        /// CreatePrimitive를 쓰지 않아 콜라이더가 **처음부터 생기지 않는다**(만들었다 지우는 낭비도 없다).
        /// 시각 파츠에는 콜라이더가 없어야 한다는 규칙을 구조적으로 보장하는 경로다.
        /// </summary>
        private GameObject AddMeshPart(GameObject parent, string name, Vector3 worldOffset, Vector3 parentScale,
            Mesh mesh, Material material, float yawDegrees, float uniformScale = 1f)
        {
            if (mesh == null)
                return null;

            float sx = Mathf.Max(0.0001f, parentScale.x);
            float sy = Mathf.Max(0.0001f, parentScale.y);
            float sz = Mathf.Max(0.0001f, parentScale.z);

            var part = new GameObject(name);
            part.transform.SetParent(parent.transform, false);
            part.transform.localPosition = new Vector3(worldOffset.x / sx, worldOffset.y / sy, worldOffset.z / sz);
            part.transform.localRotation = Quaternion.Euler(0f, yawDegrees, 0f);
            // [B48] uniformScale은 **균등** 배율이다(기본 1 = 예전과 완전히 동일). S⁻¹에 균등 배율을
            // 곱한 것이므로 위 주석의 성질이 그대로 유지된다: S·(R_y·k·S⁻¹)v = k·R_y·v - 배율이
            // 균등하고 회전이 Y뿐이라 전단이 원리적으로 생기지 않는다. 미터 규격 OBJ를 목표 치수에
            // 맞추는 fit 배율(= 목표 높이 / 모델 실측 높이)이 여기로 들어온다.
            part.transform.localScale = new Vector3(uniformScale / sx, uniformScale / sy, uniformScale / sz);

            part.AddComponent<MeshFilter>().sharedMesh = mesh;

            var renderer = part.AddComponent<MeshRenderer>();
            if (material != null)
                renderer.sharedMaterial = material;

            return part;
        }

        /// <summary>
        /// 아이템이 어떤 표면 질감 텍스처(Resources/Textures/*)를 씌울지 결정한다.
        /// B3-9: game-designer의 Spec_B2_11_MaterialFamilyField.md 권장 매핑에 따라, ItemData.materialFamily
        /// 필드가 설정돼 있으면(None이 아니면) 그 값을 우선 참조한다. 필드가 아직 None인 경우(43개의
        /// 기존 .asset이 이 필드가 추가되기 전부터 있었으므로 game-designer가 값을 채우기 전까지는 전부
        /// None이다) 예전과 동일한 itemName 문자열 추론 로직(GetSurfaceTextureNameFromName)으로 폴백해,
        /// .asset 값이 채워지기 전까지는 동작이 전혀 바뀌지 않는다.
        /// </summary>
        private string GetSurfaceTextureName(ItemData item)
        {
            if (item == null)
                return "noise";

            // [B28] 종(種) 전용 텍스처가 먼저다. materialFamily는 "나무 계열"까지만 구분할 수 있는데,
            // 대나무(마디 있는 매끈한 세로결)와 나뭇가지(거친 껍질)는 같은 Wood 계열이면서 표면이 완전히
            // 다르다 - 계열 필드로는 표현할 수 없는 차이라 이름으로 먼저 가른다. 여기서 잡히지 않는
            // 자원은 예전 경로(계열 → 이름 폴백)로 그대로 내려간다.
            string speciesTexture = GetSpeciesTextureName(item.itemName);
            if (speciesTexture != null)
                return speciesTexture;

            switch (item.materialFamily)
            {
                case MaterialFamily.Wood: return "wood";
                case MaterialFamily.Stone: return "stone";
                case MaterialFamily.Metal: return "metal";
                case MaterialFamily.Fiber: return "leaf";
                case MaterialFamily.Fruit: return "noise";
                case MaterialFamily.Supply: return "noise";
                case MaterialFamily.None:
                default:
                    return GetSurfaceTextureNameFromName(item.itemName);
            }
        }

        /// <summary>
        /// [B28] 자원 종류별 전용 타일 텍스처 이름(해당 없으면 null).
        ///
        /// 이름은 Resources/Textures/ 아래 파일명(확장자 없음)이며 StructureVisualBuilder.CreateColorMaterial이
        /// Resources.Load로 집어간다. **파일이 아직 없어도 안전하다** - 로드가 null이면 CreateColorMaterial이
        /// 텍스처를 씌우지 않고 단색으로 넘어가므로(StructureVisualBuilder.cs:152 가드) 예외도, 분홍색
        /// 머티리얼도 나오지 않는다. 즉 이 표는 텍스처가 들어오는 순간 저절로 켜진다.
        /// </summary>
        /// <summary>
        /// [B48] **월드에 서 있는 실물**의 표면색이 아이템 카테고리 색과 달라야 하는 종(種)만 여기서 덮는다.
        /// 해당 없으면 넘겨받은 카테고리 색을 그대로 돌려준다.
        ///
        /// 왜 UIBuilder를 고치지 않는가: UIBuilder.GetItemCategoryColor는 인벤토리/제작 UI의 카테고리
        /// 색까지 겸하고 있고(재질 계열 = 목재 = 갈색), 그 규칙은 UI에서 여전히 맞다. 반면 월드에 자라
        /// 있는 대나무는 **살아 있는 식물**이라 마른 목재의 갈색(Driftwood #8C6640)이면 마른 나뭇가지로
        /// 보인다(디렉터 지적). 그래서 UI 규칙은 그대로 두고 월드 표면만 황록색으로 가른다.
        /// 잎(Sprig/모델 잎)은 예전 그대로 Frond Green이다 - 줄기와 잎이 색으로 갈려야 한다.
        ///
        /// 이 값은 루트 줄기·곁줄기·모델 줄기 · EffectBuilder.PlayHarvestPop의 채집 입자 색까지
        /// 한 곳에서 따라간다(전부 이 색을 읽는다).
        /// </summary>
        private Color GetWorldSurfaceColor(string itemName, Color categoryColor)
        {
            if (itemName == "대나무")
                return StructureVisualBuilder.BambooCulm;

            return categoryColor;
        }

        private string GetSpeciesTextureName(string itemName)
        {
            if (string.IsNullOrEmpty(itemName))
                return null;

            switch (itemName)
            {
                case "대나무": return "bamboo";   // 마디 사이의 매끈한 세로결
                case "나뭇가지": return "bark";     // 거친 나무 껍질
                case "야자잎": return "frond";    // 잎맥
                case "돌조각": return "rock";     // 거친 암석
                case "부싯돌": return "rock";
                case "천조각": return "thatch";   // 엮인 섬유
                case "코코넛": return "thatch";   // 코코넛 겉껍질의 섬유질
                case "비상식량": return "driftwood"; // 표류한 나무 배급 상자
                default: return null;
            }
        }

        /// <summary>
        /// (B3-9 이전 로직, materialFamily가 None일 때의 폴백) 아이템 이름을 보고 어떤 표면 질감
        /// 텍스처(Resources/Textures/*)를 씌울지 추론한다. 처음에는 wood/stone/noise 3종뿐이었는데,
        /// 금속과 잎/식물류가 돌·나무와 뭉뚱그려져 있어 leaf(잎맥 얼룩)와 metal(브러시드 메탈 스크래치)을
        /// 추가로 분리했다.
        /// </summary>
        private string GetSurfaceTextureNameFromName(string itemName)
        {
            if (string.IsNullOrEmpty(itemName))
                return "noise";

            if (itemName.Contains("금속조각"))
                return "metal";

            if (itemName.Contains("야자잎"))
                return "leaf";

            if (itemName.Contains("나뭇가지") || itemName.Contains("대나무"))
                return "wood";

            if (itemName.Contains("돌조각") || itemName.Contains("부싯돌"))
                return "stone";

            return "noise";
        }

        /// <summary>
        /// 섬 규모에 대응하는 자원 개수 배율을 반환한다.
        /// 긴급 정정(#2 회귀 수정): 인스펙터(씬 직렬화)에 설정된 필드 값을 항상 우선한다. 필드가 0
        /// 이하로 남아있어(설정 실수/아직 배치 안 된 새 컴포넌트 등) 의미 있게 설정되지 않은 경우에만
        /// IslandSizeMetrics.GetAreaProportionalMultiplier를 안전한 기본값 폴백으로 사용한다.
        /// </summary>
        private float GetMultiplier(IslandSize size)
        {
            switch (size)
            {
                case IslandSize.Small: return smallMultiplier > 0f ? smallMultiplier : IslandSizeMetrics.GetAreaProportionalMultiplier(size);
                case IslandSize.Medium: return mediumMultiplier > 0f ? mediumMultiplier : IslandSizeMetrics.GetAreaProportionalMultiplier(size);
                case IslandSize.Large: return largeMultiplier > 0f ? largeMultiplier : IslandSizeMetrics.GetAreaProportionalMultiplier(size);
                case IslandSize.ExtraLarge: return extraLargeMultiplier > 0f ? extraLargeMultiplier : IslandSizeMetrics.GetAreaProportionalMultiplier(size);
                default: return smallMultiplier > 0f ? smallMultiplier : IslandSizeMetrics.GetAreaProportionalMultiplier(size);
            }
        }

        /// <summary>
        /// 섬 규모에 대응하는 자원 산포 반경을 반환한다.
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

    /// <summary>
    /// [B28] 자원 노드가 **공유**하는 머티리얼/메시 보관소.
    ///
    /// [B48] 여기에 **실물 OBJ 모델 로더**(TryLoadTwoPartModel / TryGetBambooModel)가 더 붙었다.
    /// 모델이 있는 자원은 모델을 쓰고, 없으면 아래 절차 메시로 폴백한다 - 두 경로 다 살아 있어야 한다.
    ///
    /// (아래는 절차 메시 쪽 근거다) 이 프로젝트는 오랫동안 3D 모델 에셋이 0개라 모든 형태를 런타임에
    /// 조립했다. 그런데 프리미티브만으로는
    /// 마디 있는 대나무 줄기·옹이 있는 잔가지·각진 돌 파편·잎맥 있는 야자잎을 만들 수 없고, 프리미티브를
    /// 겹쳐서 흉내 내면 파츠(=드로우콜)가 폭증한다. 여기서는 그 형태들을 **절차 메시 한 장**으로 만든다.
    /// 대나무 줄기 하나에 마디를 5개 넣어도 파츠는 그대로 1개다 - 굵기 변화는 메시 안에 있기 때문이다.
    ///
    /// 세 가지 원칙:
    ///  1. 전부 정적 캐시다. 월드 전체(섬 9개 · 노드 수백 개)가 메시 30장과 머티리얼 40개 안팎을
    ///     나눠 쓴다. 예전에는 파츠 하나가 머티리얼 하나였다 - 특대 섬 한 곳에서만 320개다
    ///     (자원 13종 × 노드 100개 × 파츠 평균 3.2). 실측 내역은 ResourceNode.ClumpVisualPrimitives 주석.
    ///  2. 메시 좌표계는 두 가지뿐이다. 이름이 `~Unit`이면 프리미티브 로컬 규격(실린더 |y|<=1,
    ///     큐브·구 |v|<=0.5)이고, `~Meters`면 1단위 = 1미터에 원점이 밑동이다(AddMeshPart 전용).
    ///     루트 메시는 반드시 Unit이어야 한다 - 접지·콜라이더 계산이 그 규격을 전제로 한다.
    ///  3. 감김(winding)을 표로 외우지 않는다. 이 프로젝트는 왼손 좌표계라 표준 인덱스 표를 옮기면
    ///     통째로 안쪽을 향해 컬링되는 사고가 반복됐다(IslandMeshGenerator.AddOrientedTriangle 주석).
    ///     여기서도 삼각형마다 기하 법선을 계산해 기준 방향과 맞춘다.
    /// </summary>
    public static class ResourceVisualLibrary
    {
        /// <summary>줄기·가지의 기본 단면 분할 수. 6이면 로우폴리 실루엣이 유지되면서도 둥글게 읽힌다.</summary>
        private const int StemSides = 6;

        private static readonly Dictionary<string, Material> materialCache = new Dictionary<string, Material>();
        private static readonly Dictionary<string, Mesh> meshCache = new Dictionary<string, Mesh>();

        /// <summary>
        /// 팔레트 색의 명도만 바꾼 변주(알파는 항상 1). IslandMeshGenerator.Shade와 같은 규칙이다 -
        /// URP Lit Opaque에서 `color * 0.75f` 처럼 곱하면 알파까지 0.75가 되는 실수를 막는다.
        /// </summary>
        public static Color Shade(Color color, float factor)
        {
            return new Color(
                Mathf.Clamp01(color.r * factor),
                Mathf.Clamp01(color.g * factor),
                Mathf.Clamp01(color.b * factor),
                1f);
        }

        /// <summary>
        /// (색 + 텍스처) 조합당 머티리얼 하나를 만들어 재사용한다. 색은 채널당 64단계로 양자화해
        /// 눈에 보이지 않는 차이로 캐시가 늘어나지 않게 한다.
        /// 파괴된 머티리얼(씬 언로드 등)이 캐시에 남아 있으면 다시 만든다 - Unity의 == 오버로드가
        /// 파괴된 오브젝트를 null로 알려주므로 이 검사 하나로 충분하다.
        /// </summary>
        public static Material GetMaterial(Color color, string textureName)
        {
            string key = Mathf.RoundToInt(color.r * 64f) + "_" + Mathf.RoundToInt(color.g * 64f) + "_"
                + Mathf.RoundToInt(color.b * 64f) + "_" + (string.IsNullOrEmpty(textureName) ? "noise" : textureName);

            Material cached;
            if (materialCache.TryGetValue(key, out cached) && cached != null)
                return cached;

            // 텍스처 로드 실패(null)는 CreateColorMaterial 안에서 조용히 처리된다 - 단색으로 나올 뿐이다.
            Material created = StructureVisualBuilder.CreateColorMaterial(color, textureName);
            if (created != null)
            {
                // 같은 메시 + 같은 머티리얼 조합이 섬마다 수십 개씩 나오므로 인스턴싱이 실제로 걸린다.
                created.enableInstancing = true;
            }
            materialCache[key] = created;
            return created;
        }

        // ── [B48] 실물 OBJ 모델 로더 (야자수 · 대나무 공용) ─────────────────────────────
        /// <summary>
        /// `o` 오브젝트가 **2개**(줄기 + 잎)인 OBJ에서 공유 메시 두 장을 꺼낸다. 못 찾으면 false다.
        ///
        /// [프리팹을 Instantiate하지 않는다] 바위(IslandMeshGenerator.TryGetRockModel)와 같다.
        /// MeshFilter.sharedMesh만 꺼내 쓰면 임포터가 붙였을 수 있는 콜라이더가 씬에 **구조적으로**
        /// 들어올 수 없다(초목·자원의 시각 파츠에 콜라이더가 생기면 TerrainSampler.SnapToGround와
        /// 배치 높이 계산이 통째로 깨진다).
        ///
        /// [줄기/잎을 어떻게 가르나] Unity의 OBJ 임포터는 `o` 그룹을 자식 GameObject로 만들 수도,
        /// 하나를 루트에 얹을 수도 있다. 그래서 **루트를 포함한 모든 MeshFilter를 순회**하고
        ///   (1) 이름(메시 이름 + 오브젝트 이름)에 trunk/culm/stem이 있으면 줄기,
        ///       crown/leaf/foliage/frond가 있으면 잎으로 본다.
        ///   (2) 이름으로 못 가르면 **OBJ의 `o` 등장 순서**로 폴백한다(줄기가 항상 먼저다).
        ///   (3) 메시가 하나뿐이면 그것을 줄기로 주고 잎은 null이다 - 호출부가 서브메시 2개짜리
        ///       (임포터가 합쳐 온) 경우를 머티리얼 두 장으로 따로 처리한다.
        ///
        /// [로드 규칙] Resources.Load는 필드 초기자에서 부르지 않고(생성자 시점이라 null이 온다),
        /// 실패를 영구히 캐시하지 않는다(AGENT_BRIEF 4장 3번). 프레임당 1회 재시도 가드는 호출부가 갖는다.
        /// </summary>
        public static bool TryLoadTwoPartModel(string resourcePath, out Mesh trunk, out Mesh foliage)
        {
            trunk = null;
            foliage = null;

            // 확장자를 붙이면 항상 null이다(AssetPipeline 3장).
            var prefab = Resources.Load<GameObject>(resourcePath);
            if (prefab == null)
                return false;

            var filters = prefab.GetComponentsInChildren<MeshFilter>(true);
            Mesh firstInOrder = null;
            Mesh secondInOrder = null;

            for (int i = 0; i < filters.Length; i++)
            {
                MeshFilter filter = filters[i];
                if (filter == null || filter.sharedMesh == null)
                    continue;

                Mesh mesh = filter.sharedMesh;
                string label = (mesh.name + "/" + filter.gameObject.name).ToLowerInvariant();

                if (trunk == null && (label.Contains("trunk") || label.Contains("culm") || label.Contains("stem")))
                    trunk = mesh;
                else if (foliage == null && (label.Contains("crown") || label.Contains("leaf")
                    || label.Contains("foliage") || label.Contains("frond")))
                    foliage = mesh;

                if (firstInOrder == null)
                    firstInOrder = mesh;
                else if (secondInOrder == null)
                    secondInOrder = mesh;
            }

            if (trunk == null)
                trunk = firstInOrder != foliage ? firstInOrder : secondInOrder;
            if (foliage == null && secondInOrder != null)
                foliage = secondInOrder != trunk ? secondInOrder : firstInOrder;

            return trunk != null;
        }

        /// <summary>모델 에셋 경로(Resources 기준, 확장자 없음).</summary>
        private static readonly string[] BambooModelResourcePaths =
        {
            "Models/bamboo_a", "Models/bamboo_b", "Models/bamboo_c"
        };

        /// <summary>각 모델의 실측 전체 높이(m, 밑면 y=0 기준). 위 경로와 인덱스가 일대일로 대응한다.</summary>
        private static readonly float[] BambooModelHeights = { 3.349f, 3.885f, 4.463f };

        private static readonly Mesh[] bambooCulmMeshes = new Mesh[3];
        private static readonly Mesh[] bambooLeafMeshes = new Mesh[3];
        private static int bambooModelProbeFrame = -1;

        /// <summary>
        /// 목표 높이에 가장 가까운 대나무 모델의 **공유 메시 두 장**(줄기 다발 / 잎)을 돌려준다.
        /// 하나도 못 찾으면 false이고, 그때 호출부는 예전 절차 포기(곁줄기 + 잎다발)로 돌아간다.
        ///
        /// 바위·야자수와 완전히 같은 규칙이다: 프레임당 1회만 프로브하고(섬 하나에 대나무 노드가
        /// 최대 8개라 가드가 없으면 한 프레임에 Load가 24번 불린다), 실패를 영구 캐시하지 않으며,
        /// 변종 선택에 난수를 쓰지 않는다(이미 뽑아 둔 세로 지터가 정한 높이로 고른다).
        /// </summary>
        public static bool TryGetBambooModel(float targetHeight, out Mesh culms, out Mesh leaves, out float modelHeight)
        {
            culms = null;
            leaves = null;
            modelHeight = 1f;

            bool anyMissing = false;
            for (int i = 0; i < bambooCulmMeshes.Length; i++)
            {
                if (bambooCulmMeshes[i] == null)
                    anyMissing = true;
            }

            if (anyMissing && bambooModelProbeFrame != Time.frameCount)
            {
                bambooModelProbeFrame = Time.frameCount;
                for (int i = 0; i < bambooCulmMeshes.Length; i++)
                {
                    if (bambooCulmMeshes[i] != null)
                        continue;

                    Mesh loadedCulms, loadedLeaves;
                    if (!TryLoadTwoPartModel(BambooModelResourcePaths[i], out loadedCulms, out loadedLeaves))
                        continue;

                    bambooCulmMeshes[i] = loadedCulms;
                    bambooLeafMeshes[i] = loadedLeaves;
                }
            }

            float bestDelta = float.MaxValue;
            for (int i = 0; i < bambooCulmMeshes.Length; i++)
            {
                if (bambooCulmMeshes[i] == null)
                    continue;

                float delta = Mathf.Abs(BambooModelHeights[i] - targetHeight);
                if (delta >= bestDelta)
                    continue;

                bestDelta = delta;
                culms = bambooCulmMeshes[i];
                leaves = bambooLeafMeshes[i];
                modelHeight = BambooModelHeights[i];
            }

            return culms != null;
        }

        // ── 대나무 ─────────────────────────────────────────────────────────────
        /// <summary>
        /// 노드 루트용 대나무 줄기(실린더 규격). 마디 5~7개 · 위로 갈수록 가늘어짐.
        /// 반지름을 규격 상한(0.5)이 아니라 0.22에서 시작하는 이유: 루트 스케일(0.30)이 콜라이더
        /// 크기이기도 해서 줄이면 채집 판정이 좁아진다. 콜라이더는 넓게 두고 **보이는 줄기만**
        /// 지름 13.2cm로 가늘게 만든다(0.22 × 2 × 0.30m).
        ///
        /// [B29] 루트 높이가 2.1m → 4.2m가 되면서 함께 손본 값:
        ///  · 반지름 0.34 → 0.22 (스케일이 0.14 → 0.30이므로 보이는 굵기는 9.5cm → 13.2cm로 **커진다**)
        ///  · 마디 수 3~5 → 5~7. 그대로 두면 마디 간격이 0.42~0.70m → 0.84~1.40m로 벌어져 대나무가
        ///    아니라 매듭 몇 개 있는 기둥이 된다. 5~7이면 0.60~0.84m로, 높이에 맞춰 간격도 함께 커지되
        ///    "마디가 촘촘한 줄기"라는 실루엣은 유지된다.
        ///  · uvTile 3 → 6. 텍스처 밀도(1.43타일/m)를 높이 2배에도 그대로 유지한다.
        /// </summary>
        public static Mesh BambooCulmUnit(int variant)
        {
            int v = Mathf.Abs(variant) % 3;
            string key = "bambooUnit" + v;
            Mesh cached;
            if (meshCache.TryGetValue(key, out cached) && cached != null)
                return cached;

            Mesh mesh = BuildSegmentedStem("Res_BambooCulmUnit" + v, 5 + v, -1f, 1f, 0.22f, 0.155f, 0f, 1.22f, StemSides, 6f);
            meshCache[key] = mesh;
            return mesh;
        }

        /// <summary>
        /// 곁줄기용 대나무(미터 규격, 밑동이 원점). 변주마다 높이 2.25~3.85m · 지름 6.4~9.6cm ·
        /// 기울기 0.14~0.38m · 마디 5~9개가 다르게 조합돼 있어, 한 포기 안에서 줄기가 서로 다르게 읽힌다.
        /// 기울기를 메시에 구워 넣는 이유는 AddMeshPart 주석 참고(전단 방지).
        ///
        /// [B29] 높이 1.05~1.80m → 2.25~3.85m(×2.15). 루트 줄기(3.57~5.25m)와 합치면 한 포기 안의
        /// 최대/최소 차이가 1.6m → 3.0m로 벌어져 "무리"로 읽힌다 - 변주 폭을 유지하라는 지시대로,
        /// 비율(가장 큰 것 ÷ 가장 작은 것 = 1.71)은 예전 그대로 두고 전체를 밀어 올렸다.
        /// 굵기 ×1.55 · 기울기 ×2 · 마디 수 ×1.8을 함께 올린 이유는 BambooCulmUnit 주석과 같다.
        /// </summary>
        public static Mesh BambooCulmMeters(int variant)
        {
            int v = Mathf.Abs(variant) % 5;
            string key = "bambooMeters" + v;
            Mesh cached;
            if (meshCache.TryGetValue(key, out cached) && cached != null)
                return cached;

            float[] heights = { 3.10f, 2.25f, 3.85f, 2.70f, 3.50f };
            float[] radii = { 0.040f, 0.032f, 0.048f, 0.035f, 0.043f };
            float[] leans = { 0.20f, -0.32f, 0.14f, 0.38f, -0.22f };
            int[] bands = { 7, 5, 9, 6, 8 };

            Mesh mesh = BuildSegmentedStem("Res_BambooCulmM" + v, bands[v], 0f, heights[v],
                radii[v], radii[v] * 0.74f, leans[v], 1.22f, StemSides, heights[v] * 2f);
            meshCache[key] = mesh;
            return mesh;
        }

        // ── 나뭇가지 ───────────────────────────────────────────────────────────
        /// <summary>
        /// 노드 루트용 굵은 가지(실린더 규격). 옹이 2개 + 위로 갈수록 급하게 가늘어지는 테이퍼 +
        /// 살짝 굽음. 단면을 5각으로 두어 대나무(6각 · 매끈)와 실루엣이 겹치지 않게 한다.
        /// </summary>
        public static Mesh BranchStickUnit(int variant)
        {
            int v = Mathf.Abs(variant) % 3;
            string key = "branchUnit" + v;
            Mesh cached;
            if (meshCache.TryGetValue(key, out cached) && cached != null)
                return cached;

            float[] leans = { 0.10f, -0.14f, 0.05f };
            Mesh mesh = BuildSegmentedStem("Res_BranchUnit" + v, 2, -1f, 1f, 0.42f, 0.13f, leans[v], 1.34f, 5, 2f);
            meshCache[key] = mesh;
            return mesh;
        }

        /// <summary>
        /// 흩어진 잔가지 하나(미터 규격, 원점이 지면의 더미 중심). 들린 각도 12~70도 · 길이 0.26~0.52m ·
        /// 굵기 2.2~4.0cm가 변주마다 다르고, 절반은 갈래가 하나 더 나 있다.
        /// "주워 모을 것"으로 읽히려면 이 셋이 제각각이어야 한다는 것이 이번 지시의 요구다.
        /// </summary>
        public static Mesh TwigMeters(int variant)
        {
            int v = Mathf.Abs(variant) % 6;
            string key = "twigMeters" + v;
            Mesh cached;
            if (meshCache.TryGetValue(key, out cached) && cached != null)
                return cached;

            float[] lengths = { 0.42f, 0.30f, 0.52f, 0.36f, 0.46f, 0.26f };
            float[] radii = { 0.017f, 0.013f, 0.020f, 0.015f, 0.018f, 0.011f };
            float[] tilts = { 22f, 58f, 12f, 40f, 30f, 70f };
            float[] shifts = { -0.10f, 0.06f, -0.14f, 0.09f, -0.05f, 0.12f };
            bool[] forked = { true, false, true, false, true, false };

            float tilt = tilts[v] * Mathf.Deg2Rad;
            float length = lengths[v];
            float radius = radii[v];
            Vector3 direction = new Vector3(0f, Mathf.Sin(tilt), Mathf.Cos(tilt));
            Vector3 start = new Vector3(0f, radius * 1.1f, shifts[v]);
            Vector3 middle = start + direction * (length * 0.55f);
            Vector3 end = start + direction * length + new Vector3(0f, -0.015f, 0f); // 끝이 살짝 처진다

            var builder = new MeshBuilder();
            builder.AddTube(new[] { start, middle, end }, new[] { radius, radius * 0.82f, radius * 0.42f }, 5, true, true, 2f);

            if (forked[v])
            {
                Vector3 forkDirection = (direction + new Vector3(0.85f, 0.2f, 0f)).normalized;
                Vector3 forkEnd = middle + forkDirection * (length * 0.42f);
                builder.AddTube(new[] { middle, forkEnd }, new[] { radius * 0.62f, radius * 0.24f }, 4, false, true, 1f);
            }

            Mesh mesh = builder.Finish("Res_TwigM" + v);
            meshCache[key] = mesh;
            return mesh;
        }

        // ── 야자잎 ─────────────────────────────────────────────────────────────
        /// <summary>
        /// 잎 한 장(미터 규격, 원점이 잎자루 밑동이고 +Z로 뻗는다).
        /// 중앙 잎맥(가는 관) + 좌우로 갈라진 잎깃 4~7쌍으로 되어 있어 가장자리가 톱니로 읽힌다.
        /// 잎깃은 두께가 없는 면이라 **양면**으로 넣는다 - 한 면만 넣으면 아래에서 볼 때 통째로 사라진다
        /// (IslandMeshGenerator.GetGrassBladeMesh가 같은 이유로 같은 방식을 쓴다).
        ///
        /// 변주 0~2가 야자잎(길이 0.44~0.58m), 3~4가 대나무 잎다발(0.48~0.60m, 더 많이 처진다).
        /// [B29] 대나무 잎다발(3~4)만 0.24~0.30m → 0.48~0.60m로 키웠다. 줄기가 4~5m가 되면서 예전
        /// 크기로는 꼭대기에서 점으로 보였다. **야자잎이 쓰는 0~2는 한 값도 건드리지 않았다**
        /// (호출부: 야자잎 rng.NextInt(0,3) / 대나무 3 + i%2 - 두 범위가 겹치지 않는다).
        /// **주의:** 호출부가 이 메시를 스케일 1로 쓴다는 전제로 길이가 미터로 박혀 있다. 스케일을
        /// 따로 곱하지 마라 - 과거 "풀 메시를 바꿨는데 호출부 스케일이 그대로여서 거대한 판이 된" 사고와
        /// 같은 유형이 된다. 크기를 바꾸려면 이 표의 숫자를 바꿔라.
        /// </summary>
        public static Mesh FrondMeters(int variant)
        {
            int v = Mathf.Abs(variant) % 5;
            string key = "frondMeters" + v;
            Mesh cached;
            if (meshCache.TryGetValue(key, out cached) && cached != null)
                return cached;

            float[] lengths = { 0.50f, 0.58f, 0.44f, 0.60f, 0.48f };
            float[] widths = { 0.16f, 0.19f, 0.14f, 0.21f, 0.17f };
            float[] droops = { 0.07f, 0.09f, 0.06f, 0.26f, 0.22f };
            int[] pairs = { 6, 7, 5, 6, 5 };

            float length = lengths[v];
            float halfWidth = widths[v] * 0.5f;
            float droop = droops[v];
            int pairCount = pairs[v];

            var builder = new MeshBuilder();

            // 중앙 잎맥: 밑동에서 끝으로 가며 가늘어지고 아래로 처지는 가는 관.
            var ribCenters = new Vector3[5];
            var ribRadii = new float[5];
            for (int i = 0; i < 5; i++)
            {
                float t = i / 4f;
                ribCenters[i] = RibPoint(t, length, droop);
                ribRadii[i] = Mathf.Lerp(0.011f, 0.003f, t);
            }
            builder.AddTube(ribCenters, ribRadii, 4, true, true, 2f);

            // 잎깃: 잎맥에서 좌우로 갈라져 나가며 끝으로 갈수록 뒤로 눕는다.
            for (int i = 0; i < pairCount; i++)
            {
                float t0 = 0.10f + 0.82f * i / pairCount;
                float t1 = Mathf.Min(0.99f, t0 + 0.86f / pairCount);
                Vector3 inner0 = RibPoint(t0, length, droop);
                Vector3 inner1 = RibPoint(t1, length, droop);

                // 폭은 가운데가 가장 넓고 밑동/끝에서 0에 가까워진다(잎 전체가 방추형으로 읽힌다).
                // 최소 폭을 남겨 둔다 - 면적이 0인 삼각형은 RecalculateNormals에서 법선이 0이 되어
                // 그 잎깃 하나만 새까맣게 보인다.
                float w0 = halfWidth * Mathf.Max(0.14f, Mathf.Sin(Mathf.Pow(t0, 0.7f) * Mathf.PI));
                float w1 = halfWidth * Mathf.Max(0.14f, Mathf.Sin(Mathf.Pow(t1, 0.7f) * Mathf.PI));

                for (int side = 0; side < 2; side++)
                {
                    float sign = side == 0 ? 1f : -1f;
                    Vector3 outward = new Vector3(sign * 0.90f, -0.30f, 0.32f); // 밖 + 아래 + 끝 방향
                    Vector3 outer0 = inner0 + outward * w0;
                    Vector3 outer1 = inner1 + outward * w1;
                    builder.AddQuad(inner0, inner1, outer1, outer0, Vector3.up, true);
                }
            }

            Mesh mesh = builder.Finish("Res_FrondM" + v);
            meshCache[key] = mesh;
            return mesh;
        }

        /// <summary>잎맥 곡선 위의 한 점(t = 0 밑동 ~ 1 끝). 처짐은 t²이라 밑동은 평평하고 끝만 내려간다.</summary>
        private static Vector3 RibPoint(float t, float length, float droop)
        {
            return new Vector3(0f, -droop * t * t, length * t);
        }

        // ── 돌 ────────────────────────────────────────────────────────────────
        /// <summary>
        /// 각진 돌덩이(구 규격). 정이십면체의 각 꼭짓점 반지름을 결정적으로 흔들어 만든 저폴리 파편이라
        /// 20면 전부가 평면으로 셰이딩된다 - 눌린 구(예전 형태)와 달리 "쪼개진 돌"로 읽힌다.
        /// y 방향 반지름을 정확히 0.5로 정규화하므로 접지 계산(GetHalfHeight)이 그대로 맞는다.
        /// </summary>
        public static Mesh RockChunkUnit(int variant)
        {
            int v = Mathf.Abs(variant) % 4;
            string key = "rockUnit" + v;
            Mesh cached;
            if (meshCache.TryGetValue(key, out cached) && cached != null)
                return cached;

            Mesh mesh = BuildAngularChunk("Res_RockChunk" + v, 8100 + v, 0.30f, false);
            meshCache[key] = mesh;
            return mesh;
        }

        /// <summary>
        /// 얇고 각진 석기 파편(큐브 규격). 축마다 따로 정규화해 큐브 상자를 꽉 채우므로,
        /// 부싯돌 루트의 납작한 스케일(0.32 × 0.10 × 0.42)이 그대로 "깨진 돌조각"이 된다.
        /// </summary>
        public static Mesh StoneFlakeUnit(int variant)
        {
            int v = Mathf.Abs(variant) % 4;
            string key = "flakeUnit" + v;
            Mesh cached;
            if (meshCache.TryGetValue(key, out cached) && cached != null)
                return cached;

            Mesh mesh = BuildAngularChunk("Res_StoneFlake" + v, 5300 + v, 0.38f, true);
            meshCache[key] = mesh;
            return mesh;
        }

        // ── 공용 빌더 ─────────────────────────────────────────────────────────
        /// <summary>
        /// 마디(또는 옹이)가 있는 기둥을 만든다. bandCount개의 마디마다 링을 3장 넣어
        /// "바로 아래를 0.93으로 조이고 → 마디에서 bandBulge로 부풀리고 → 위를 1.04로 넓게" 잇는다.
        /// 이 조임-부풂 실루엣이 대나무를 대나무로 읽히게 하는 유일한 신호다(색은 밤에 안 보인다).
        /// leanX는 t²에 비례해 휘므로 밑동은 곧고 위로 갈수록 기운다.
        /// </summary>
        private static Mesh BuildSegmentedStem(string name, int bandCount, float yBottom, float yTop,
            float radiusBottom, float radiusTop, float leanX, float bandBulge, int sides, float uvTile)
        {
            var ratios = new List<float>();
            ratios.Add(0f);
            for (int i = 0; i < bandCount; i++)
            {
                float t = (i + 0.75f) / (bandCount + 0.55f);
                ratios.Add(Mathf.Clamp(t - 0.035f, 0.01f, 0.96f));
                ratios.Add(Mathf.Clamp(t, 0.02f, 0.97f));
                ratios.Add(Mathf.Clamp(t + 0.040f, 0.03f, 0.98f));
            }
            ratios.Add(1f);

            var centers = new Vector3[ratios.Count];
            var radii = new float[ratios.Count];
            for (int i = 0; i < ratios.Count; i++)
            {
                float t = ratios[i];
                float factor = 1f;
                if (i > 0 && i < ratios.Count - 1)
                {
                    int slot = (i - 1) % 3;
                    factor = slot == 0 ? 0.93f : (slot == 1 ? bandBulge : 1.04f);
                }

                centers[i] = new Vector3(leanX * t * t, Mathf.Lerp(yBottom, yTop, t), 0f);
                radii[i] = Mathf.Max(0.004f, Mathf.Lerp(radiusBottom, radiusTop, t) * factor);
            }

            var builder = new MeshBuilder();
            builder.AddTube(centers, radii, sides, true, true, uvTile);
            return builder.Finish(name);
        }

        /// <summary>
        /// 정이십면체 기반 각진 덩어리. jitter는 꼭짓점별 반지름 흔들림 폭(0~1)이고, 시드가 같으면
        /// 항상 같은 모양이 나온다(UnityEngine.Random을 쓰지 않는다 - 재현성 규칙).
        /// fitBox가 true면 축마다 따로 정규화해 [-0.5, 0.5]³ 상자를 채우고,
        /// false면 y 반지름만 0.5로 맞춘다(구 규격의 접지 계산과 정확히 일치시키기 위해서다).
        /// </summary>
        private static Mesh BuildAngularChunk(string name, int seed, float jitter, bool fitBox)
        {
            const float phi = 1.618034f;
            var points = new[]
            {
                new Vector3(-1f, phi, 0f), new Vector3(1f, phi, 0f), new Vector3(-1f, -phi, 0f), new Vector3(1f, -phi, 0f),
                new Vector3(0f, -1f, phi), new Vector3(0f, 1f, phi), new Vector3(0f, -1f, -phi), new Vector3(0f, 1f, -phi),
                new Vector3(phi, 0f, -1f), new Vector3(phi, 0f, 1f), new Vector3(-phi, 0f, -1f), new Vector3(-phi, 0f, 1f),
            };

            var faces = new[]
            {
                0, 11, 5,  0, 5, 1,   0, 1, 7,   0, 7, 10,  0, 10, 11,
                1, 5, 9,   5, 11, 4,  11, 10, 2, 10, 7, 6,  7, 1, 8,
                3, 9, 4,   3, 4, 2,   3, 2, 6,   3, 6, 8,   3, 8, 9,
                4, 9, 5,   2, 4, 11,  6, 2, 10,  8, 6, 7,   9, 8, 1,
            };

            var random = new System.Random(seed);
            for (int i = 0; i < points.Length; i++)
            {
                float scale = 1f + ((float)random.NextDouble() * 2f - 1f) * jitter;
                points[i] = points[i].normalized * scale;
            }

            // 정규화. 축마다 최대 반지름을 정확히 0.5로 맞춘다 - 이걸 균일 배율로 하면 꼭짓점 흔들림
            // 때문에 가로가 세로의 최대 1.9배까지 커져서, 콜라이더(= 채집 판정)보다 눈에 띄게 큰 돌이 나온다.
            // y를 항상 따로 맞추는 이유는 접지 계산(GetHalfHeight)이 y 반지름 0.5를 전제하기 때문이다.
            float maxX = 0.0001f;
            float maxY = 0.0001f;
            float maxZ = 0.0001f;
            for (int i = 0; i < points.Length; i++)
            {
                maxX = Mathf.Max(maxX, Mathf.Abs(points[i].x));
                maxY = Mathf.Max(maxY, Mathf.Abs(points[i].y));
                maxZ = Mathf.Max(maxZ, Mathf.Abs(points[i].z));
            }

            // 구 규격에서는 가로 두 축을 같은 배율로 줄여 바닥 윤곽이 한쪽으로 늘어나지 않게 한다.
            float scaleX = fitBox ? 0.5f / maxX : 0.5f / Mathf.Max(maxX, maxZ);
            float scaleZ = fitBox ? 0.5f / maxZ : 0.5f / Mathf.Max(maxX, maxZ);
            float scaleY = 0.5f / maxY;
            for (int i = 0; i < points.Length; i++)
                points[i] = new Vector3(points[i].x * scaleX, points[i].y * scaleY, points[i].z * scaleZ);

            var builder = new MeshBuilder();
            for (int f = 0; f + 2 < faces.Length; f += 3)
            {
                Vector3 a = points[faces[f]];
                Vector3 b = points[faces[f + 1]];
                Vector3 c = points[faces[f + 2]];
                // 원점을 감싸는 볼록 덩어리라 무게중심 방향이 곧 바깥 방향이다.
                builder.AddFace(a, b, c, (a + b + c) / 3f);
            }
            return builder.Finish(name);
        }

        /// <summary>
        /// 정점/UV/삼각형을 모아 메시 하나로 마무리하는 최소 빌더. 삼각형을 넣을 때마다 기하 법선을
        /// 기준 방향과 비교해 감김을 바로잡으므로, 좌표계 손잡이 방향을 착각해도 안쪽으로 뒤집히지 않는다.
        /// </summary>
        private class MeshBuilder
        {
            private readonly List<Vector3> vertices = new List<Vector3>();
            private readonly List<Vector2> uvs = new List<Vector2>();
            private readonly List<int> triangles = new List<int>();

            /// <summary>중심선(centers)과 반지름(radii)을 따라가는 관을 하나 잇는다.</summary>
            public void AddTube(Vector3[] centers, float[] radii, int sides, bool capStart, bool capEnd, float uvTile)
            {
                if (centers == null || radii == null || centers.Length < 2 || radii.Length != centers.Length || sides < 3)
                    return;

                Vector3 axis = centers[centers.Length - 1] - centers[0];
                if (axis.sqrMagnitude < 0.0000001f)
                    axis = Vector3.up;
                axis = axis.normalized;

                Vector3 helper = Mathf.Abs(axis.y) > 0.9f ? Vector3.forward : Vector3.up;
                Vector3 right = Vector3.Cross(helper, axis).normalized;
                Vector3 forward = Vector3.Cross(axis, right);

                int start = vertices.Count;
                int stride = sides + 1; // 이음매(seam)에서 UV가 끊기도록 정점을 한 개 겹쳐 둔다
                for (int r = 0; r < centers.Length; r++)
                {
                    for (int s = 0; s <= sides; s++)
                    {
                        float angle = (float)s / sides * Mathf.PI * 2f;
                        Vector3 direction = right * Mathf.Cos(angle) + forward * Mathf.Sin(angle);
                        vertices.Add(centers[r] + direction * radii[r]);
                        uvs.Add(new Vector2((float)s / sides, (float)r / (centers.Length - 1) * uvTile));
                    }
                }

                for (int r = 0; r + 1 < centers.Length; r++)
                {
                    for (int s = 0; s < sides; s++)
                    {
                        int a0 = start + r * stride + s;
                        int a1 = a0 + 1;
                        int b0 = a0 + stride;
                        int b1 = b0 + 1;
                        float mid = ((float)s + 0.5f) / sides * Mathf.PI * 2f;
                        Vector3 outward = right * Mathf.Cos(mid) + forward * Mathf.Sin(mid);
                        AddTriangle(a0, b0, b1, outward);
                        AddTriangle(a0, b1, a1, outward);
                    }
                }

                if (capStart)
                    AddCap(start, sides, centers[0], -axis);
                if (capEnd)
                    AddCap(start + (centers.Length - 1) * stride, sides, centers[centers.Length - 1], axis);
            }

            /// <summary>사각면 하나. doubleSided면 감김을 뒤집은 사본을 함께 넣어 양쪽에서 보이게 한다.</summary>
            public void AddQuad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 reference, bool doubleSided)
            {
                AddQuadFace(a, b, c, d, reference);
                if (doubleSided)
                    AddQuadFace(a, b, c, d, -reference);
            }

            /// <summary>평면 셰이딩용 삼각면 하나(정점을 공유하지 않아 면마다 각이 선다).</summary>
            public void AddFace(Vector3 a, Vector3 b, Vector3 c, Vector3 reference)
            {
                int index = vertices.Count;
                vertices.Add(a);
                vertices.Add(b);
                vertices.Add(c);
                uvs.Add(new Vector2(a.x + 0.5f, a.z + 0.5f));
                uvs.Add(new Vector2(b.x + 0.5f, b.z + 0.5f));
                uvs.Add(new Vector2(c.x + 0.5f, c.z + 0.5f));
                AddTriangle(index, index + 1, index + 2, reference);
            }

            public Mesh Finish(string name)
            {
                var mesh = new Mesh();
                mesh.name = name;
                mesh.SetVertices(vertices);
                mesh.SetUVs(0, uvs);
                mesh.SetTriangles(triangles, 0);
                mesh.RecalculateNormals();
                mesh.RecalculateBounds();
                return mesh;
            }

            private void AddQuadFace(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 reference)
            {
                int index = vertices.Count;
                vertices.Add(a);
                vertices.Add(b);
                vertices.Add(c);
                vertices.Add(d);
                uvs.Add(new Vector2(0f, 0f));
                uvs.Add(new Vector2(0f, 1f));
                uvs.Add(new Vector2(1f, 1f));
                uvs.Add(new Vector2(1f, 0f));
                AddTriangle(index, index + 1, index + 2, reference);
                AddTriangle(index, index + 2, index + 3, reference);
            }

            private void AddCap(int ringStart, int sides, Vector3 center, Vector3 reference)
            {
                int centerIndex = vertices.Count;
                vertices.Add(center);
                uvs.Add(new Vector2(0.5f, 0.5f));
                for (int s = 0; s < sides; s++)
                    AddTriangle(centerIndex, ringStart + s, ringStart + s + 1, reference);
            }

            /// <summary>
            /// 삼각형 하나를 감김 방향까지 맞춰 넣는다(IslandMeshGenerator.AddOrientedTriangle과 같은 방식).
            /// 기하 법선이 기준과 반대면 두 인덱스를 바꿔 넣는다.
            /// </summary>
            private void AddTriangle(int i0, int i1, int i2, Vector3 reference)
            {
                Vector3 geometric = Vector3.Cross(vertices[i1] - vertices[i0], vertices[i2] - vertices[i0]);
                if (Vector3.Dot(geometric, reference) < 0f)
                {
                    int swap = i1;
                    i1 = i2;
                    i2 = swap;
                }

                triangles.Add(i0);
                triangles.Add(i1);
                triangles.Add(i2);
            }
        }
    }
}
