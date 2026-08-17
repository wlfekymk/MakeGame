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
    public partial class IslandResourceSpawner : MonoBehaviour
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

        // ═════════════════════════════════════════════════════════════════════════════
        // [육상 필수 분류] 사용자 지적 "석재류나 바다 전용 자원인데 목재까지 바다에 들어갔어".
        // 섬 윤곽이 실측 몰디브 마스크로 바뀌며 산포원(0.8R) 안 육지 비율이 30~58%로 떨어져,
        // 지형을 안 보고 뿌리는 이 스포너의 노드 절반 가까이가 물속에 생길 수 있게 됐다.
        //
        // 분류 기준(아이템 이름 상수 표 - 씬은 이 작업 범위에서 못 고치므로 코드에 둔다):
        //  · 육상 필수(이 표): 유기물 전부. 나뭇가지/대나무/야자잎/코코넛(식물), 천조각/비상식량
        //    (잔해라도 물에 뜨거나 젖어 못 쓰는 것) - 물속에 서 있으면 부자연스럽다.
        //  · 수중 허용(표에 없는 나머지): 돌조각/부싯돌/금속조각(석재·광물 - 사용자가 "바다 OK"라 한
        //    부류), 부력통/연료/엔진부품/생수(표류물 - 물에 있는 것이 오히려 자연스럽다).
        // 목록에 없는 이름은 전부 수중 허용으로 처리되므로, 새 유기물 자원을 씬에 추가하면
        // 이 표에도 이름을 추가해야 한다.
        // ═════════════════════════════════════════════════════════════════════════════
        // [4티어 채집 노드] "대리석"은 유기물이 아니지만 육상 필수다 - 흰 노두(원석)가 물에 잠기면
        // 보이지 않는다(사용자 규격). 씬 resourceEntries에는 대리석 항목이 없으므로 이 이름 추가가
        // 기존 종들의 재추첨 경로(rng draw)를 건드릴 일은 없다 - SpawnQuarryOreNodes(맨 끝)에서만 쓰인다.
        // 석재는 돌조각과 같은 부류(석재·광물 = 수중 허용)라 표에 넣지 않는다.
        private static readonly string[] LandRequiredItemNames =
            { "나뭇가지", "대나무", "야자잎", "코코넛", "천조각", "비상식량", "대리석" };

        /// <summary>이 아이템이 물속 배치가 부자연스러운 "육상 필수" 자원인가.</summary>
        private static bool IsLandRequiredItem(string itemName)
        {
            for (int i = 0; i < LandRequiredItemNames.Length; i++)
            {
                if (LandRequiredItemNames[i] == itemName)
                    return true;
            }
            return false;
        }

        /// <summary>육상 필수 자원이 물에 빠졌을 때 같은 반경으로 다시 뽑는 최대 횟수(PickBearWanderTarget의 6회 상한 선례).</summary>
        private const int LandRedrawAttempts = 6;

        /// <summary>재추첨까지 실패했을 때, 마지막 오프셋을 섬 중심 방향으로 절반씩 줄여 추가로 시도하는 횟수(rng 미소비).</summary>
        private const int LandShrinkAttempts = 2;

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

        /// <summary>
        /// 지정한 섬 인스턴스 위에 규모에 맞는 개수만큼 자원 노드를 생성한다.
        /// 각 노드는 섬 위치를 중심으로 scatterRadius 반경 안에 무작위 배치된다.
        /// B3-3: worldSeed를 추가로 받아, 이 섬(island.islandId) 전용 결정적 System.Random 스트림을
        /// 만들어 쓴다. 같은 worldSeed로 다시 호출하면(WorldMapManager.RegenerateWorld) 이 섬에서
        /// 생성되는 노드의 위치·모양(스케일/회전 지터 포함)·개수·순서가 정확히 그대로 재현된다 -
        /// 다른 섬이 그 사이에 몇 개를 뽑았는지와 완전히 무관하다(섬마다 독립된 스트림이므로).
        /// 각 노드에는 (island.islandId, spawnOrder) 식별자와 [세이브 키 v2] 결정론적 안정 키
        /// (ResourceNode.stableKey = StableSpawnKey.Compute(섬, 아이템 이름, 같은 아이템 안에서의 순번))를
        /// 부여한다. 세이브 대조는 stableKey로만 한다(SaveLoadController.RestoreResourceNodes).
        /// </summary>
        public List<ResourceNode> SpawnResourcesForIsland(IslandInstance island, Transform parent, int worldSeed)
        {
            var spawned = new List<ResourceNode>();
            if (island == null)
                return spawned;

            System.Random rng = SeededRandomExtensions.CreateForIsland(worldSeed, island.islandId);
            int spawnOrder = 0;

            // [세이브 키 v2] 아이템 이름 → 이 섬에서 지금까지 스폰된 그 아이템 노드 수. 무작위 루프·
            // 착륙 원·대나무 증량이 **하나의 카운터를 이어 쓴다**(같은 아이템이면 어느 경로로 스폰되든
            // 종류 내 순번이 이어져야 키가 유일하다).
            var perTypeCounts = new Dictionary<string, int>();

            float multiplier = GetMultiplier(island.size);
            float radius = GetScatterRadius(island.size);
            // 해수면. BearAI(InitBearAI)와 같은 방식으로 WorldMapManager에서 읽고, 못 찾으면 기본값 0.
            float seaLevel = SpawnLandPlacement.ResolveSeaLevel();

            foreach (var entry in resourceEntries)
            {
                if (entry.yieldItem == null)
                    continue;

                // 최소 섬 규모 미만이면 이 자원은 아예 등장하지 않는다 (희귀 재료 위치 제한용).
                if (island.size < entry.minimumIslandSize)
                    continue;

                bool landRequired = IsLandRequiredItem(entry.yieldItem.itemName);
                int count = Mathf.RoundToInt(entry.baseCount * multiplier);
                for (int i = 0; i < count; i++)
                {
                    // [육상 필수] 물에 빠지면 위치만 다시 뽑는다(PickScatterPosition). 어떤 경우에도
                    // 노드를 건너뛰지 않으므로 count(생성 개수)와 perTypeIndex(stableKey v2의 종류 내
                    // 순번)는 예전과 완전히 같다 - 세이브 대조는 안전하다. 단, 재추첨은 같은 rng
                    // 스트림에서 draw를 추가 소비하므로 **같은 worldSeed의 배치가 이전 버전과 달라진다**
                    // (버전 교체 시 1회성 변화 - 세이브가 깨지는 것이 아니라 월드 모습만 바뀐다).
                    Vector3 position = PickScatterPosition(island, radius, rng, landRequired, seaLevel);
                    spawned.Add(SpawnSingleNode(entry, position, parent, rng, island.islandId, spawnOrder, perTypeCounts));
                    spawnOrder++;
                }
            }

            // 착륙 원은 반드시 위 무작위 루프가 끝난 "뒤"에 처리한다.
            // 이유 - 난수: 같은 rng를 이어 쓰되 모든 추가 draw가 기존 노드들의 draw 뒤에 오므로, 기존
            // 노드의 위치/모양/개수가 1mm도 바뀌지 않는다(같은 worldSeed = 같은 월드 배치 재현성 유지).
            // [세이브 키 v2] 예전에는 "(2) 세이브 키: spawnOrder가 밀리면 기존 세이브가 어긋난다"가
            // 두 번째 이유였지만, 키가 종류별 안정 해시(stableKey)로 바뀌어 **세이브 키에 관한 한 이
            // 순서 제약은 더 이상 없다.** 위 배치 재현성 근거만으로도 이 순서가 맞으므로 구조는 그대로 둔다.
            if (spawnLandingCircle && island.isStartingIsland)
                SpawnLandingCircleNodes(island, parent, rng, spawned, ref spawnOrder, perTypeCounts);

            // [B49 디렉터 지시 "대나무를 5배로"] 증량분은 **모든 기존 노드(착륙 원 포함) 뒤**에 붙인다.
            // [세이브 키 v2] 이 "뒤에 덧붙이기" 구조가 원래 지키던 것은 두 가지였다 - (a) 기존 노드의
            // rng draw 순서(= 같은 worldSeed의 월드 배치 재현성)와 (b) spawnOrder 세이브 키. 키가
            // stableKey로 바뀌어 (b)는 더 이상 이 구조를 요구하지 않지만, (a)는 여전히 유효하다 -
            // 같은 시드에서 기존 노드의 위치·모양이 그대로여야 하므로 추가 draw는 전부 뒤에 온다.
            // 이미 동작하는 배치라 구조는 바꾸지 않는다(주석만 갱신).
            SpawnExtraBambooNodes(island, parent, rng, spawned, ref spawnOrder, perTypeCounts);

            // [4티어 채집 노드] 석재/대리석 원석. 반드시 **모든 기존 스폰 경로(무작위 루프·착륙 원·
            // 대나무 증량)가 끝난 뒤**에 호출한다 - 신규 종이 소비하는 rng draw가 전부 기존 draw 뒤에
            // 와야 기존 종들의 추첨 순서·횟수가 1회도 안 바뀐다(같은 worldSeed = 같은 월드 배치).
            SpawnQuarryOreNodes(island, parent, rng, spawned, ref spawnOrder, perTypeCounts);

            return spawned;
        }

        /// <summary>이 이름의 자원을 증량 대상으로 삼는다. 씬 엔트리를 이름으로 찾는다(착륙 원과 같은 방식).</summary>
        private const string BambooItemName = "대나무";

        /// <summary>
        /// [B49] 대나무 **추가** 배치 배수. 기존 배치분(baseCount × 규모 배율)의 이 배수만큼을 뒤에 덧붙여
        /// 합계가 (1 + 이 값)배가 된다. 사용자 요청이 "5배"이므로 4다.
        /// [세이브 키 v2] 이 방식을 택했던 원래 이유 중 "spawnOrder(세이브 키)가 밀린다"는 키 교체로
        /// 사라졌다. 다만 씬 baseCount를 고치면 대나무의 rng draw 시점이 목록 중간(5번째)에서 늘어나
        /// 뒤따르는 8종의 위치·지터가 통째로 바뀐다(같은 worldSeed의 월드 배치 재현성 위반). 그래서
        /// 이미 동작하는 이 "뒤에 덧붙이기" 구조를 유지한다.
        /// </summary>
        private const int BambooExtraSpawnMultiplier = 4;

        /// <summary>
        /// [B49] 대나무 노드를 기존 배치분의 <see cref="BambooExtraSpawnMultiplier"/>배만큼 **추가로** 배치한다.
        ///
        /// ★ 이 함수가 지키는 계약 ★
        ///  · 반드시 SpawnResourcesForIsland의 무작위 루프와 착륙 원이 **모두 끝난 뒤**에 호출된다.
        ///    여기서 소비하는 rng draw가 전부 기존 draw **뒤**에 와야 기존 노드의 위치·스케일·회전
        ///    지터가 1mm도 바뀌지 않는다(같은 worldSeed = 같은 월드). [세이브 키 v2] 예전에는
        ///    "spawnOrder 0..N-1이 그대로 붙어야 한다(세이브 안전)"도 근거였지만, 세이브 키가 종류별
        ///    안정 해시(stableKey)로 바뀌어 그 근거는 소멸했다 - 남은 근거는 배치 재현성 하나다.
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
            List<ResourceNode> spawned, ref int spawnOrder, Dictionary<string, int> perTypeCounts)
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
            float seaLevel = SpawnLandPlacement.ResolveSeaLevel();
            // 대나무는 육상 필수 표에 있으므로 증량분도 무작위 루프와 완전히 같은 물빠짐 재추첨 규칙을
            // 탄다(위 PickScatterPosition 주석 참고 - extraCount 자체는 절대 줄지 않는다).
            bool landRequired = IsLandRequiredItem(entry.yieldItem.itemName);
            for (int i = 0; i < extraCount; i++)
            {
                Vector3 position = PickScatterPosition(island, radius, rng, landRequired, seaLevel);
                spawned.Add(SpawnSingleNode(entry, position, parent, rng, island.islandId, spawnOrder, perTypeCounts));
                spawnOrder++;
            }
        }

        // ═════════════════════════════════════════════════════════════════════════════
        // [4티어 채집 노드 - 석재/대리석 원석] 건축 4티어 재료의 직접 채집 경로.
        // 씬 resourceEntries에는 이 두 종이 없고 씬(.unity) 수정은 금지라, 대나무 증량과 같은
        // "맨 끝 덧붙이기" 코드 스폰으로 넣는다. ItemData는 ItemDataRegistry에서 이름으로 찾는다
        // (AirlinerSalvagePoint와 같은 조회 방식 - 석재/대리석 에셋은 레지스트리에 등록돼 있음을 확인).
        //
        // ★ 이 경로가 지키는 계약(SpawnExtraBambooNodes와 동일) ★
        //  · 반드시 기존 스폰이 전부 끝난 뒤 호출된다 - 추가 draw가 전부 뒤에 오므로 기존 종의
        //    추첨 순서·횟수·위치·지터가 1회도 안 바뀐다.
        //  · stableKey는 (섬, 아이템 이름, 종별 순번) 안정 해시라 신규 종 추가 자체는 기존 세이브와
        //    충돌하지 않는다(perTypeCounts 카운터를 그대로 이어 쓴다).
        //  · 배치 규칙을 새로 만들지 않는다 - 같은 산포 반경, 같은 PickScatterPosition(대리석은
        //    육상 필수 재추첨), 같은 SpawnSingleNode 경로.
        //  · 디렉터가 나중에 씬 resourceEntries **맨 끝**에 같은 종을 정식 추가하면
        //    (FindEntryByItemName이 찾게 되면) 코드 스폰은 스스로 물러난다(이중 스폰 방지).
        // ═════════════════════════════════════════════════════════════════════════════
        private const string StoneOreItemName = "석재";
        private const string MarbleOreItemName = "대리석";

        /// <summary>원석 채집 요구 도구. 금속조각(씬 엔트리: requiresTool=1, requiredTool=손도끼)과 같은 급.
        /// 레지스트리에서 못 찾으면 도구 없이 채집 가능으로 완화된다(진행 잠금이 노드 부재보다 나쁘다).</summary>
        private const string OreRequiredToolName = "손도끼";

        /// <summary>석재 원석의 섬 규모별 스폰 개수 범위(결정적 rng로 그 안에서 확정).
        /// 소량 유지가 규격이다 - 돌조각(3 × 배율)처럼 배율을 곱하지 않는다.</summary>
        private static void GetStoneOreCountRange(IslandSize size, out int min, out int max)
        {
            switch (size)
            {
                case IslandSize.Small: min = 1; max = 2; break;
                case IslandSize.Medium: min = 2; max = 3; break;
                default: min = 3; max = 4; break; // 대형/특대
            }
        }

        /// <summary>대리석 원석의 섬 규모별 스폰 개수 범위. 대형/특대 전용(그 외 0 = 스폰 안 함).</summary>
        private static void GetMarbleOreCountRange(IslandSize size, out int min, out int max)
        {
            switch (size)
            {
                case IslandSize.Large: min = 1; max = 2; break;
                case IslandSize.ExtraLarge: min = 2; max = 3; break;
                default: min = 0; max = 0; break;
            }
        }

        /// <summary>
        /// 석재(전 섬 · 수중 허용 · 채집당 1~2개)와 대리석(대형/특대 · 육상 필수 · 채집당 1개)
        /// 원석 노드를 스폰한다. 호출 순서 계약은 위 블록 주석 참고.
        /// </summary>
        private void SpawnQuarryOreNodes(IslandInstance island, Transform parent, System.Random rng,
            List<ResourceNode> spawned, ref int spawnOrder, Dictionary<string, int> perTypeCounts)
        {
            // Resources.Load 규칙 준수: 필드 초기자가 아닌 스폰 시점(월드 생성)에 로드하고, 실패(null)를
            // 영구 캐시하지 않는다 - 다음 스폰 호출에서 다시 시도된다. Load 자체는 Unity가 캐시한다.
            var registry = ItemDataRegistry.LoadFromResources();
            if (registry == null)
                return; // 레지스트리가 아직 없으면 조용히 건너뛴다(설정 누락에 NRE로 죽지 않게).

            ItemData tool = FindRegistryItemByName(registry, OreRequiredToolName);

            GetStoneOreCountRange(island.size, out int stoneMin, out int stoneMax);
            SpawnOreSpecies(island, parent, rng, spawned, ref spawnOrder, perTypeCounts,
                registry, StoneOreItemName, tool, stoneMin, stoneMax, yieldOneToTwo: true);

            GetMarbleOreCountRange(island.size, out int marbleMin, out int marbleMax);
            SpawnOreSpecies(island, parent, rng, spawned, ref spawnOrder, perTypeCounts,
                registry, MarbleOreItemName, tool, marbleMin, marbleMax, yieldOneToTwo: false);
        }

        /// <summary>
        /// 원석 한 종을 스폰한다. rng draw는 (개수 1회) + 노드당 (수확량 0~1회 + 위치 + 모양 지터)이며
        /// 전부 기존 draw 뒤에 온다. 씬에 같은 종의 엔트리가 생기면(정식 편입) 아무것도 하지 않는다.
        /// </summary>
        private void SpawnOreSpecies(IslandInstance island, Transform parent, System.Random rng,
            List<ResourceNode> spawned, ref int spawnOrder, Dictionary<string, int> perTypeCounts,
            ItemDataRegistry registry, string itemName, ItemData tool, int minCount, int maxCount,
            bool yieldOneToTwo)
        {
            if (maxCount <= 0)
                return;

            if (FindEntryByItemName(itemName) != null)
                return; // 씬이 이 종을 정식으로 갖게 됐다 - 코드 스폰은 물러난다(이중 스폰 방지).

            ItemData item = FindRegistryItemByName(registry, itemName);
            if (item == null)
                return; // 아이템 에셋이 아직 레지스트리에 없으면 조용히 건너뛴다.

            // 코드 엔트리: 씬에 이 종의 직렬화 항목이 존재하지 않으므로(위에서 확인) "씬 설정과 어긋난
            // 노드" 문제가 없다. 도구 규칙은 금속조각과 같은 급(손도끼 요구, 내구도 1 소모).
            var entry = new ResourceEntry
            {
                yieldItem = item,
                baseCount = 0, // 미사용 - 개수는 아래 rng 확정값을 쓴다(배율 미적용, 소량 유지)
                minimumIslandSize = itemName == MarbleOreItemName ? IslandSize.Large : IslandSize.Small,
                requiresTool = tool != null,
                requiredTool = tool,
                bonusTool = null,
                bonusYieldPerHarvest = 0,
            };

            int count = rng.NextInt(minCount, maxCount + 1);
            float radius = GetScatterRadius(island.size);
            float seaLevel = SpawnLandPlacement.ResolveSeaLevel();
            bool landRequired = IsLandRequiredItem(itemName); // 대리석 true / 석재 false(돌조각과 같은 수중 허용)

            for (int i = 0; i < count; i++)
            {
                // 수확량은 노드마다 결정적으로 확정한다(석재 1~2, 대리석은 draw 없이 1 고정).
                int yieldPerHarvest = yieldOneToTwo ? rng.NextInt(1, 3) : 1;
                Vector3 position = PickScatterPosition(island, radius, rng, landRequired, seaLevel);
                ResourceNode node = SpawnSingleNode(entry, position, parent, rng, island.islandId, spawnOrder, perTypeCounts);
                node.yieldPerHarvest = yieldPerHarvest;
                spawned.Add(node);
                spawnOrder++;
            }
        }

        /// <summary>레지스트리에서 이름으로 ItemData를 찾는다(AirlinerSalvagePoint와 같은 조회 방식).</summary>
        private static ItemData FindRegistryItemByName(ItemDataRegistry registry, string itemName)
        {
            if (registry == null || registry.allItems == null || string.IsNullOrEmpty(itemName))
                return null;

            foreach (var candidate in registry.allItems)
            {
                if (candidate != null && candidate.itemName == itemName)
                    return candidate;
            }
            return null;
        }

        /// <summary>
        /// 산포원 안에서 자원 노드 하나의 위치를 뽑는다. 육상 필수 자원(<see cref="LandRequiredItemNames"/>)이
        /// 수면 아래(해수면 + <see cref="SpawnLandPlacement.LandMinHeightAboveSea"/> 미만)에 떨어지면:
        ///  1) 같은 반경으로 최대 <see cref="LandRedrawAttempts"/>회 재추첨한다. 같은 rng 스트림을 그대로
        ///     이어 쓴다(PickBearWanderTarget 선례 - 별도 스트림을 만들면 시드 관리가 이중화된다).
        ///  2) 그래도 실패하면 마지막 오프셋을 섬 중심 방향으로 절반씩 줄이며 <see cref="LandShrinkAttempts"/>회
        ///     더 시도한다(마스크 섬이라도 중심부는 육지일 확률이 가장 높다). 이 단계는 rng를 소비하지 않는다.
        ///  3) 최종 실패 시 마지막(가장 안쪽) 후보를 그대로 반환한다 - 노드를 없애면 생성 개수가 줄어
        ///     stableKey(v2)의 perTypeIndex 순번이 세이브와 어긋나므로, 위치가 어색하더라도 개수는 지킨다.
        /// 수중 허용 자원(landRequired=false)은 예전과 정확히 같은 draw 수(NextInsideUnitCircle 1회 = 2 draw)로
        /// 한 번에 확정된다.
        /// 지형 레이캐스트가 아예 실패한 경우(hitTerrain=false - 지형 미생성 등)는 물/뭍을 판정할 수 없으므로
        /// 기존 동작(입력 위치 유지)을 그대로 따르고 재추첨하지 않는다 - SnapToGround가 실패 시 입력을
        /// 그대로 돌려주는 것을 "y가 안 변했다"로 오판하지 않도록 센티넬 판정(SnapToGroundWithHit)을 쓴다.
        /// </summary>
        private Vector3 PickScatterPosition(IslandInstance island, float radius, System.Random rng,
            bool landRequired, float seaLevel)
        {
            Vector2 offset = rng.NextInsideUnitCircle() * radius;
            Vector3 snapped = SnapOffsetToGround(island, offset, out bool hitTerrain);
            if (!landRequired || SpawnLandPlacement.IsAboveWater(snapped, hitTerrain, seaLevel))
                return snapped;

            // (1) 같은 반경 재추첨 - 시도마다 NextInsideUnitCircle 1회(2 draw)만 추가 소비한다.
            for (int attempt = 0; attempt < LandRedrawAttempts; attempt++)
            {
                offset = rng.NextInsideUnitCircle() * radius;
                snapped = SnapOffsetToGround(island, offset, out hitTerrain);
                if (SpawnLandPlacement.IsAboveWater(snapped, hitTerrain, seaLevel))
                    return snapped;
            }

            // (2) 섬 중심 방향으로 반경 절반씩 축소(rng 미소비 - 마지막 오프셋의 방향을 유지한 채 당긴다).
            for (int attempt = 0; attempt < LandShrinkAttempts; attempt++)
            {
                offset *= 0.5f;
                snapped = SnapOffsetToGround(island, offset, out hitTerrain);
                if (SpawnLandPlacement.IsAboveWater(snapped, hitTerrain, seaLevel))
                    return snapped;
            }

            // (3) 최종 실패 - 가장 안쪽 후보에 그대로 둔다(개수 불변이 위치보다 우선).
            return snapped;
        }

        /// <summary>섬 중심 + XZ 오프셋을 지형 위로 스냅한다(센티넬 실패 판정 포함).</summary>
        private static Vector3 SnapOffsetToGround(IslandInstance island, Vector2 offset, out bool hitTerrain)
        {
            Vector3 position = island.mapPosition + new Vector3(offset.x, 0f, offset.y);
            return SpawnLandPlacement.SnapToGroundWithHit(position, out hitTerrain);
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
            List<ResourceNode> spawned, ref int spawnOrder, Dictionary<string, int> perTypeCounts)
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
                // [육상 필수 재배치] 착륙 원은 물빠짐 재추첨을 태우지 않는다(확인 결과 불필요):
                // 배치 반경이 섬 중심에서 최대 12m인데, 시작 섬은 소형(지형 반지름 50)이고 중심이
                // 플레이어 불시착 지점(씬 Player y=14 - 해수면 0보다 한참 위)이라 이 원은 항상 육지다.
                // 재추첨을 넣으면 정면/좌/우 각도 설계(Design_Onboarding 2장)가 깨질 수 있어 오히려 손해다.
                position = TerrainSampler.SnapToGround(position);

                spawned.Add(SpawnSingleNode(entry, position, parent, rng, island.islandId, spawnOrder, perTypeCounts));
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
        private ResourceNode SpawnSingleNode(ResourceEntry entry, Vector3 position, Transform parent, System.Random rng, int islandIndex, int spawnOrder,
            Dictionary<string, int> perTypeCounts)
        {
            ItemData yieldItem = entry.yieldItem;
            string itemName = yieldItem.itemName;

            // [세이브 키 v2] 같은 아이템 안에서의 생성 순번. 카운터는 호출자(SpawnResourcesForIsland)가
            // 섬 하나당 하나를 만들어 모든 스폰 경로(무작위 루프/착륙 원/대나무 증량)에 이어 쓴다.
            // rng를 전혀 소비하지 않는 순수 계산이라 월드 배치 재현성에는 영향이 없다.
            perTypeCounts.TryGetValue(itemName, out int perTypeIndex);
            perTypeCounts[itemName] = perTypeIndex + 1;

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
            // [세이브 키 v2] 세이브 대조용 안정 키. spawnOrder는 판별/디버깅용으로만 남는다.
            node.stableKey = StableSpawnKey.Compute(islandIndex, itemName, perTypeIndex);
            return node;
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
    /// [육상 필수 재배치] IslandResourceSpawner와 CreatureSpawner가 공유하는 "물속인가?" 판정 유틸.
    /// 두 스포너의 소유 파일이 이 둘뿐이라(다른 파일 수정 금지) 별도 파일 대신 여기에 둔다.
    /// </summary>
    internal static class SpawnLandPlacement
    {
        /// <summary>
        /// 육상 필수 배치물이 서 있어도 되는 해수면 위 최소 높이(m).
        /// IslandMeshGenerator.Vegetation의 VegetationMinGroundY(0.25)와 같은 기준을 쓴다 -
        /// 초목이 서는 자리 = 유기물 자원이 서도 자연스러운 자리. (곰 이동의 BearShoreMarginY 0.55는
        /// 움직이는 개체의 파도선 여유라 더 크다.)
        /// </summary>
        internal const float LandMinHeightAboveSea = 0.25f;

        /// <summary>센티넬 낙차(m). HazardSource.SampleGroundY와 같은 방식(실제 지형이 못 내려가는 깊이).</summary>
        private const float SentinelDrop = 100f;

        /// <summary>
        /// 해수면을 WorldMapManager.seaLevel에서 읽는다(씬 값 0). 못 찾으면 0
        /// (WorldMapManager.seaLevel 기본값과 같은 값 - HazardSource.InitBearAI와 동일한 폴백).
        /// </summary>
        internal static float ResolveSeaLevel()
        {
            WorldMapManager world = Object.FindAnyObjectByType<WorldMapManager>();
            return world != null ? world.seaLevel : 0f;
        }

        /// <summary>
        /// TerrainSampler.SnapToGround로 지형에 스냅하되, 실패(지형 미히트)를 out으로 알려준다.
        /// SnapToGround는 실패해도 입력 좌표를 그대로 돌려줘 호출자가 실패를 알 수 없으므로,
        /// 절대 나올 수 없는 y(입력 y - 100)를 센티넬로 넣고 "돌아온 y가 센티넬 그대로면 실패"로
        /// 판정한다(HazardSource.SampleGroundY의 검증된 관례). 레이 구간은 기존 스포너 호출의
        /// 기본값(입력 y+60에서 아래로 120m = y-60까지)과 정확히 같게 잡아, 스냅 성공 시 결과가
        /// 예전 SnapToGround(position) 호출과 1mm도 다르지 않다.
        /// </summary>
        internal static Vector3 SnapToGroundWithHit(Vector3 position, out bool hitTerrain)
        {
            Vector3 sentinelPos = new Vector3(position.x, position.y - SentinelDrop, position.z);
            Vector3 result = TerrainSampler.SnapToGround(sentinelPos, SentinelDrop + 60f, 120f);
            hitTerrain = result.y > sentinelPos.y + 1f;
            return hitTerrain ? result : position;
        }

        /// <summary>
        /// 스냅 결과가 "물 밖"인가. 지형을 못 맞힌 경우(hitTerrain=false)는 판정 자체가 불가능하므로
        /// true를 돌려 기존 동작(그 자리 유지)을 따르게 한다 - 지형이 아직 없는 상황에서 재추첨을
        /// 반복해 봐야 전부 실패라 rng만 낭비하고, 배치 높이는 어차피 SnapToGround 실패 경로와 같다.
        /// </summary>
        internal static bool IsAboveWater(Vector3 snapped, bool hitTerrain, float seaLevel)
        {
            if (!hitTerrain)
                return true;
            return snapped.y >= seaLevel + LandMinHeightAboveSea;
        }
    }
}
