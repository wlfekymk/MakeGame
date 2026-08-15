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

            return spawned;
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
            Vector3 scaleJitter = new Vector3(
                rng.NextFloat(0.85f, 1.18f),
                rng.NextFloat(0.85f, 1.25f),
                rng.NextFloat(0.85f, 1.18f));
            scale = Vector3.Scale(scale, scaleJitter);
            rotation = rotation * Quaternion.Euler(0f, rng.NextFloat(0f, 360f), 0f);

            GameObject go = GameObject.CreatePrimitive(primitive);
            go.transform.SetParent(parent);
            go.transform.localScale = scale;
            go.transform.rotation = rotation;
            go.transform.position = position + Vector3.up * GetHalfHeight(primitive, scale); // 프리미티브 종류별 반높이만큼 띄워 지형 위에 놓이게 한다
            go.name = $"Resource_{itemName}";

            // 아이템 종류(무기/음식/음료/설치형/이동수단/일반 재료)에 맞는 색을 입혀 카테고리 단위로 구분한다.
            Color color = UIBuilder.GetItemCategoryColor(yieldItem);
            string textureName = GetSurfaceTextureName(yieldItem);

            var renderer = go.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.material.color = color;

                // 단색이 밋밋해 보이는 문제 개선: 아이템 종류에 맞는 흑백 그레인 텍스처를 곱해 씌워
                // 나무 재질(세로 결)/돌 재질(반점)/그 외(부드러운 얼룩) 표면 디테일을 준다. 색상 구분은
                // 여전히 위 material.color가 담당하고, 텍스처는 표면 질감만 추가한다.
                var surfaceTexture = Resources.Load<Texture2D>($"Textures/{textureName}");
                if (surfaceTexture != null)
                {
                    renderer.material.mainTexture = surfaceTexture;
                    renderer.material.mainTextureScale = new Vector2(1.5f, 2f);
                }
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
                case "나뭇가지": // 짧고 가는 나뭇가지 다발 (아래 AddResourceDetailParts에서 곁가지 2개 추가)
                    primitive = PrimitiveType.Cylinder;
                    scale = new Vector3(0.09f, 0.32f, 0.09f);
                    break;
                case "대나무": // 키가 큰 얇은 기둥 (마디는 AddResourceDetailParts에서 추가)
                    primitive = PrimitiveType.Cylinder;
                    scale = new Vector3(0.14f, 1.05f, 0.14f);
                    break;
                case "돌조각": // 납작하게 눌린 바위 무더기
                    primitive = PrimitiveType.Sphere;
                    scale = new Vector3(0.5f, 0.32f, 0.5f);
                    break;
                case "부싯돌": // 얇고 각진 파편 - 살짝 비스듬히 기울여 둠
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
                case "야자잎": // 짧은 줄기 위에 잎사귀들이 부채꼴로 퍼짐 (AddResourceDetailParts에서 추가)
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
        /// </summary>
        private float GetHalfHeight(PrimitiveType primitive, Vector3 scale)
        {
            return primitive == PrimitiveType.Cylinder ? scale.y * 1f : scale.y * 0.5f;
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
                    // 퀄리티 개선: 곁가지 개수(1~3개)와 각도를 스폰마다 무작위로 바꿔, 같은 나뭇가지라도
                    // 어떤 건 가지가 많고 어떤 건 홑가지처럼 보이게 해서 클론처럼 보이지 않게 했다.
                    {
                        int twigCount = rng.NextInt(1, 4);
                        for (int i = 0; i < twigCount; i++)
                        {
                            float baseAngle = 55f + i * 40f;
                            float jitter = rng.NextFloat(-15f, 15f);
                            Vector3 pos = new Vector3(rng.NextFloat(-0.06f, 0.06f), rng.NextFloat(-0.02f, 0.05f), rng.NextFloat(-0.04f, 0.04f));
                            AddPart(go, $"Twig{i}", PrimitiveType.Cylinder, pos, new Vector3(0.07f, rng.NextFloat(0.22f, 0.32f), 0.07f), parentScale, Quaternion.Euler(15f, 0f, (i % 2 == 0 ? 1f : -1f) * baseAngle + jitter), color, textureName);
                        }
                        break;
                    }

                case "대나무":
                    // 퀄리티 개선: 마디 개수를 무작위로 바꿔 대나무 길이감이 다양해 보이게 했다.
                    // [tech-artist-B 요청 - 파츠 예산] 2~4개 → 2~3개 (야자잎 주석의 근거와 동일).
                    {
                        int jointCount = rng.NextInt(2, 4);
                        for (int i = 0; i < jointCount; i++)
                        {
                            float t = (float)i / Mathf.Max(1, jointCount - 1); // 0~1
                            float y = Mathf.Lerp(-0.7f, 0.55f, t);
                            AddPart(go, $"Joint{i}", PrimitiveType.Cylinder, new Vector3(0f, y, 0f), new Vector3(1.15f, 0.02f, 1.15f), parentScale, Quaternion.identity, color * 0.75f, textureName);
                        }
                        break;
                    }

                case "돌조각":
                    // 퀄리티 개선: 곁돌 개수(0~3개)를 무작위로 바꿔 돌무더기 크기가 다양해 보이게 했다.
                    {
                        int rockCount = rng.NextInt(0, 4);
                        Vector3[] offsets = { new Vector3(0.35f, -0.15f, 0.1f), new Vector3(-0.3f, -0.18f, -0.15f), new Vector3(0.1f, -0.12f, -0.32f) };
                        for (int i = 0; i < rockCount && i < offsets.Length; i++)
                        {
                            float size = rng.NextFloat(0.16f, 0.28f);
                            AddPart(go, $"Rock{i + 2}", PrimitiveType.Sphere, offsets[i], new Vector3(size, size * 0.75f, size), parentScale, Quaternion.identity, color, textureName);
                        }
                        break;
                    }

                case "코코넛":
                    // 퀄리티 개선: 열매가 1개짜리 노드도, 3개까지 뭉친 노드도 나오게 해서 다발 크기가 다양해 보이게 했다.
                    {
                        int extraCount = rng.NextInt(0, 3);
                        Vector3[] offsets = { new Vector3(0.4f, -0.05f, 0.1f), new Vector3(-0.35f, -0.08f, 0.25f) };
                        for (int i = 0; i < extraCount && i < offsets.Length; i++)
                            AddPart(go, $"Coconut{i + 2}", PrimitiveType.Sphere, offsets[i], new Vector3(0.38f, 0.38f, 0.38f), parentScale, Quaternion.identity, color, textureName);
                        break;
                    }

                case "천조각":
                    // 퀄리티 개선: 접힌 주름이 있을 때도(70% 확률) 없을 때도 있게 해 밋밋한 조각과 구겨진 조각이 섞여 보이게 했다.
                    if (rng.NextValue01() < 0.7f)
                        AddPart(go, "Fold", PrimitiveType.Cube, new Vector3(0.05f, 0.3f, -0.05f), new Vector3(0.4f, 0.05f, 0.3f), parentScale, Quaternion.Euler(0f, rng.NextFloat(0f, 36f), 3f), color * 0.92f, textureName);
                    break;

                case "야자잎":
                    {
                        // 퀄리티 개선: 잎사귀 개수와 부채꼴 각도 간격, 개별 잎 길이를 무작위로 바꿔
                        // 같은 야자잎이라도 풍성해 보이는 것과 성긴 것이 섞여 보이게 했다.
                        // [tech-artist-B 요청 - 파츠 예산] 4~6장 → 3~5장. 특대 섬에는 자원 노드가 90개 넘게
                        // 깔리고 노드당 파츠 하나가 곧 드로우콜 하나다. ResourceNode 쪽에서 노드당 4개 상한을
                        // 코드로 강제하고 있으므로, 소스에서 그 상한을 넘겨 만들어 놓고 트림당하는 낭비를 없앤다.
                        //
                        // [systems-engineer-B 요청 - 파츠 예산 최종] 3~5장 → 2~3장. 3~5장은 루트를 포함해
                        // 4~6개라 ResourceNode.MaxVisualPrimitives(4, 루트 포함)를 여전히 넘겼다 - 야자잎은
                        // 12종 자원 중 유일한 초과 항목이었다. 두 해법 중 여기(소스에서 감량)를 고른 이유:
                        //   (1) ResourceNode의 트림은 자식을 "뒤에서부터" 지운다(TrimDetailChildren). 잎은
                        //       -spread/2 → +spread/2 순서로 각도순 배치되므로 뒤를 자르면 한쪽 날개만 남은
                        //       비대칭 반쪽 부채꼴이 된다. 같은 장수라도 대칭 부채꼴 쪽이 훨씬 잘 읽힌다.
                        //   (2) 트림은 GameObject를 만들었다가 곧바로 Destroy하는 낭비다 - 바로 위 주석이
                        //       없애자고 한 그 낭비를, 야자잎에 트림을 붙이면 다시 들여오게 된다.
                        // 2장이면 V자, 3장이면 부채꼴로 읽힌다. 아래 각도 보간식이 leafCount 1도 안전하게
                        // 처리하므로(Mathf.Max(1, ...)) 범위를 더 줄여도 0으로 나누지 않는다.
                        int leafCount = rng.NextInt(2, 4);
                        float spread = rng.NextFloat(100f, 140f); // 부채꼴 전체 펼침 각도
                        for (int i = 0; i < leafCount; i++)
                        {
                            float angle = -spread * 0.5f + spread * i / Mathf.Max(1, leafCount - 1);
                            float rad = angle * Mathf.Deg2Rad;
                            float leafLength = rng.NextFloat(0.45f, 0.62f);
                            Vector3 localPos = new Vector3(Mathf.Sin(rad) * 0.26f, 0.1f, Mathf.Cos(rad) * 0.26f);
                            Quaternion rot = Quaternion.Euler(-20f, angle, 0f);
                            AddPart(go, $"Leaf{i}", PrimitiveType.Cube, localPos, new Vector3(0.05f, 0.02f, leafLength), parentScale, rot, color, textureName);
                        }
                        break;
                    }

                case "금속조각":
                    // 퀄리티 개선: 구부러진 정도(각도)를 무작위로 바꿔 찌그러진 모양이 조금씩 다르게 보이게 했다.
                    AddPart(go, "Bend", PrimitiveType.Cube, new Vector3(-0.05f, 0.4f, 0.05f), new Vector3(0.32f, 0.06f, 0.22f), parentScale, Quaternion.Euler(0f, rng.NextFloat(-50f, -20f), 8f), color * 0.85f, textureName);
                    break;

                case "부력통":
                    AddPart(go, "Rim", PrimitiveType.Cylinder, new Vector3(0f, 0.85f, 0f), new Vector3(1.08f, 0.04f, 1.08f), parentScale, Quaternion.identity, color * 0.8f, textureName);
                    break;

                case "비상식량":
                    AddPart(go, "Label", PrimitiveType.Cube, new Vector3(0f, 0.1f, 0.51f), new Vector3(0.26f, 0.1f, 0.02f), parentScale, Quaternion.identity, Color.white * 0.9f, "noise");
                    break;

                case "연료":
                    AddPart(go, "Spout", PrimitiveType.Cylinder, new Vector3(0.08f, 0.62f, 0f), new Vector3(0.14f, 0.12f, 0.14f), parentScale, Quaternion.identity, color * 0.85f, textureName);
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
                            AddPart(go, $"Bolt{i}", PrimitiveType.Cube, localPos, new Vector3(0.06f, 0.06f, 0.06f), parentScale, Quaternion.identity, color * 0.8f, textureName);
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
        /// </summary>
        private void AddPart(GameObject parent, string name, PrimitiveType primitive, Vector3 localPosition,
            Vector3 worldSize, Vector3 parentScale, Quaternion localRotation, Color color, string textureName)
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

            var renderer = part.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.material.color = color;
                var tex = Resources.Load<Texture2D>($"Textures/{textureName}");
                if (tex != null)
                {
                    renderer.material.mainTexture = tex;
                    renderer.material.mainTextureScale = new Vector2(1.5f, 2f);
                }
            }
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
}
