using System.Collections.Generic;
using UnityEngine;
using MakeGame.Data;
using MakeGame.Player;

namespace MakeGame.Systems
{
    /// <summary>
    /// 섬별 해저 주변 **해양 생물**(물고기 떼 / 바다거북 / 가오리 / 해파리 / 문어 / 돌고래) 분포기.
    ///
    /// SeabedGenerator가 해저 스커트를 깔고 레코드를 등록한 뒤, UnderwaterCaveSpawner 직후 **같은
    /// 동기 흐름에서** 호출된다(스커트가 먼저 있어야 TrySampleSeabed 접지/수심 판정이 유효하다).
    ///
    /// ── [세이브 무관 - 순수 배경] ──────────────────────────────────────────────────
    /// 여기서 만드는 것은 전부 채집 불가·전투 불가 배경이다. ResourceNode/HazardSource/
    /// AirlinerSalvagePoint를 하나도 붙이지 않으므로 SaveLoadController의 FindObjectsByType 순회에
    /// 걸리지 않고, 저장 파일에 한 바이트도 들어가지 않는다. 불러오기(RegenerateWorld) 후에는
    /// 같은 worldSeed → 같은 시드 → 같은 배치로 다시 생성될 뿐이다.
    ///
    /// ── rng 격리 (최중요) ─────────────────────────────────────────────────────────
    /// 섬마다 `new System.Random(unchecked(worldSeed * 397 ^ islandId ^ 0x1A9E))` 로 만든 **전용
    /// 독립 스트림**만 소비한다. 기존 스트림(생태 0x5EABED · 조개 0xC1A0 · 동굴 0xCA7E · 지형지물
    /// 0x5EAF · 잔해 0x0B0CCA · 섬 레이아웃/초목/자원/위험요소 salt 대역)은 어느 것도 만들지도,
    /// 이어 뽑지도 않는다. 섬 id는 50 미만이므로 `worldSeed*397 ^ islandId ^ salt` 끼리도 충돌할 수
    /// 없다(두 스트림이 같은 시드가 되려면 islandId 차이가 salt 차이와 같아야 한다).
    /// UnityEngine.Random은 일절 쓰지 않는다(전역 상태 오염 금지).
    ///
    /// **draw 전량 소비 원칙**: 마릿수·경로·색·크기 draw는 배치 성공 여부, 모델 로드 여부, 렌더러
    /// 예산 초과 여부와 **무관하게 항상 같은 횟수·순서로** 소비한다(UnderwaterCaveSpawner.BuildCave와
    /// 같은 규약). 그래서 임포트가 한 프레임 늦어도 같은 시드의 배치가 흔들리지 않는다.
    ///
    /// ── 생명주기 편승 ────────────────────────────────────────────────────────────
    /// 배치 루트는 그 섬의 루트 오브젝트("Island_{id}_{size}") 자식이라, RegenerateWorld가 섬을
    /// SetActive(false)+Destroy 하면 드라이버까지 함께 사라진다(별도 정리 코드 불요).
    /// 이름은 "MarineLife_" / "MarineFish_" 등 **"Island_" 비접두**라 TerrainSampler.SnapToGround류
    /// 지형 판정에서 구조적으로 제외된다.
    ///
    /// ── 물리 없음 ────────────────────────────────────────────────────────────────
    /// 생물에는 Rigidbody도 Collider도 붙이지 않는다. 위치는 전부 **순수 수학**(위상·사인)으로
    /// 계산하고, 개체마다 MonoBehaviour를 붙이지 않는다 - 섬당 드라이버 **하나**(MarineLifeDriver)가
    /// 구조체 배열을 돌며 갱신한다(GrassFieldDriver/KelpSwayDriver 선례). 프레임당 할당 0.
    /// 해파리 접촉 독은 콜라이더가 아니라 드라이버의 **거리 판정**이다 - 이유는 아래 [독 접촉] 주석.
    ///
    /// ── 시각 로더 ────────────────────────────────────────────────────────────────
    /// marine_a~h는 `o` 그룹 2개(**body → fin 순서**)라 산호/조개와 같은 처지다:
    /// ResourceVisualLibrary.TryLoadTwoPartModel의 "o 등장 순서" 폴백이 body → fin을 보장하고,
    /// Unity 6.5 임포터가 병합해 오면 첫 메시(서브메시 2)만 오고 fin은 null이다 → 그때는 렌더러
    /// 하나에 sharedMaterials = [몸통색, 지느러미색]을 준다(AirlinerWreck/PlaceCoral과 같은 분기).
    /// 머티리얼은 전부 ResourceVisualLibrary.GetMaterial 공유 캐시라 월드 전체에서 최대 16장이다.
    ///
    /// ── 원점 보정 (이 계열의 예외) ────────────────────────────────────────────────
    /// 이 8종은 소품이 아니라 헤엄치는 생물인데 파이프라인 계약상 원점이 접지(밑면 y=0)다.
    /// 그래서 생물 루트 아래 모델 파츠를 하나 달고 `localPosition = -pivot × scale` 을 준다
    /// (제작 담당 실측 pivot 표 = MarinePivots). 그러면 몸통 중심(해파리는 갓 꼭대기, 가오리는
    /// 원반 중심)이 루트 원점에 오고, 루트를 돌리면 제자리에서 회전한다. 머리는 +Z다.
    ///
    /// ── 성능 ────────────────────────────────────────────────────────────────────
    /// 섬당 생물 오브젝트 상한 60(MaxCreaturesPerIsland, 물고기 떼 포함). 병합 임포트면 렌더러도
    /// 60(드로우콜 기준 최대 120 - 서브메시 2), 개별 임포트면 렌더러 120이다(산호와 같은 조건).
    /// 갱신은 **활성 섬만** - 플레이어가 섬 테두리에서 200m를 넘으면 생물 컨테이너를
    /// SetActive(false)로 통째로 끄고 갱신도 건너뛴다(드라이버는 거리 1회 계산만 한다).
    /// 수중이라 그림자는 캐스팅/수신 모두 끈다.
    /// </summary>
    public static class MarineLifeSpawner
    {
        /// <summary>섬별 배치 루트 이름 접두사. "Island_" 비접두가 지형 판정 안전의 전제다(스모크 체크 키).</summary>
        private const string MarineRootPrefix = "MarineLife_";

        /// <summary>거리 컷으로 통째로 껐다 켜는 생물 컨테이너 이름(루트의 유일한 자식).</summary>
        private const string CreatureContainerName = "MarineCreatures";

        /// <summary>rng 격리용 시드 소금. 기존 0x5EABED/0xC1A0/0xCA7E/0x5EAF/0x0B0CCA 및 3000000+
        /// salt 대역과 겹치지 않는 값이다.</summary>
        private const int SeedSalt = 0x1A9E;

        /// <summary>섬당 생물 오브젝트 상한(물고기 떼 포함). 물고기 마릿수는 이 예산에서 대형 생물을
        /// 뺀 나머지를 무리별 요청량에 비례 배분해 채운다(요청 draw는 예산과 무관하게 전량 소비).</summary>
        private const int MaxCreaturesPerIsland = 60;

        /// <summary>플레이어가 섬 테두리에서 이 거리를 넘어가면 그 섬 생물은 갱신·렌더를 통째로 쉰다(m).</summary>
        private const float ActiveDistance = 200f;

        /// <summary>물고기 떼가 흩어지기 시작하는 플레이어 거리(m, 무리 중심 기준).</summary>
        private const float ScatterRadius = 6f;

        /// <summary>해파리 독 재적용 쿨다운(초, 개체당). 물속에서 밀착해 있어도 도배되지 않는다.</summary>
        private const float PoisonCooldown = 8f;

        // ── 모델 카탈로그 (Resources/Models, 확장자 없음) ──────────────────────────

        /// <summary>해양 생물 8종. 인덱스 = 아래 Model* 상수(0~2 물고기 a/b/c, 3 거북, 4 가오리,
        /// 5 해파리, 6 문어, 7 돌고래). 전부 `o` 2개(body → fin)다.</summary>
        private static readonly string[] MarineModelNames =
        {
            "marine_a", "marine_b", "marine_c", "marine_d",
            "marine_e", "marine_f", "marine_g", "marine_h",
        };

        private const int ModelFishA = 0;
        private const int ModelTurtle = 3;
        private const int ModelRay = 4;
        private const int ModelJelly = 5;
        private const int ModelOctopus = 6;
        private const int ModelDolphin = 7;

        /// <summary>제작 담당 실측 pivot(모델 로컬, m). 모델 파츠에 `localPosition = -pivot × scale`을
        /// 주면 몸통 중심이 생물 루트 원점에 온다. f(해파리)는 **갓 꼭대기**, e(가오리)는 원반 중심
        /// 기준이다(Tools/blender/units/marine.py 원점 규약 주석).</summary>
        private static readonly Vector3[] MarinePivots =
        {
            new Vector3(0f, 0.0445f, 0.0270f),        // a 소형 물고기 A (L 0.22m)
            new Vector3(0f, 0.0580f, 0.0147f),        // b 소형 물고기 B (L 0.18m)
            new Vector3(0f, 0.0964f, 0.0230f),        // c 중형 나비고기 (L 0.30m)
            new Vector3(0f, 0.1758f, 0f),             // d 바다거북 (L 1.10m)
            new Vector3(0f, 0.0921f, 0.2699f),        // e 가오리 (W 1.60m)
            new Vector3(0.0012f, 0.8719f, 0.0084f),   // f 해파리 (갓 0.55m · 갓 꼭대기 기준)
            new Vector3(0.0127f, 0.2401f, -0.1695f),  // g 문어 (L 0.90m)
            new Vector3(0f, 0.3410f, 0.1074f),        // h 돌고래 (L 2.20m)
        };

        /// <summary>해파리 갓 반경(m, marine_f OBJ 정점 실측 x 범위 ±0.275). 독 접촉 반경의 기준값이다.</summary>
        private const float JellyBellRadius = 0.275f;

        // ── 팔레트 (순수 Color 상수라 필드 초기화식에 두어도 안전하다 - Unity API 호출 없음) ────

        /// <summary>물고기 떼 3계열 몸통색(은청 / 노랑 / 줄무늬 흰). 무리 하나는 한 색으로 통일한다
        /// (실제 어군의 색 통일감). 종(a/b/c)은 무리 안에서 섞인다.</summary>
        private static readonly Color[] FishBodyPalette =
        {
            new Color(0.58f, 0.68f, 0.78f), // 은청
            new Color(0.92f, 0.78f, 0.24f), // 노랑
            new Color(0.90f, 0.88f, 0.82f), // 줄무늬 흰
        };

        /// <summary>물고기 지느러미색(몸통과 대비되는 어두운/밝은 짝).</summary>
        private static readonly Color[] FishFinPalette =
        {
            new Color(0.82f, 0.87f, 0.92f), // 은청 → 밝은 은
            new Color(0.30f, 0.33f, 0.40f), // 노랑 → 검은 지느러미
            new Color(0.20f, 0.22f, 0.28f), // 줄무늬 → 검은 줄
        };

        /// <summary>대형 생물 몸통색(인덱스 = 모델 인덱스 - 3: 거북 갈녹 / 가오리 회갈 /
        /// 해파리 밝은 연보라 / 문어 적갈 / 돌고래 회청).</summary>
        private static readonly Color[] BigBodyPalette =
        {
            new Color(0.24f, 0.32f, 0.20f), // 거북 갈녹
            new Color(0.32f, 0.29f, 0.25f), // 가오리 회갈
            new Color(0.74f, 0.70f, 0.94f), // 해파리 연보라(불투명 - 아래 주석)
            new Color(0.52f, 0.18f, 0.16f), // 문어 적갈
            new Color(0.40f, 0.48f, 0.58f), // 돌고래 회청
        };

        /// <summary>대형 생물 지느러미/촉수/다리색.</summary>
        private static readonly Color[] BigFinPalette =
        {
            new Color(0.55f, 0.58f, 0.38f), // 거북 등딱지 무늬 밝은 황록
            new Color(0.44f, 0.41f, 0.36f), // 가오리 날개 밝은 회갈
            new Color(0.88f, 0.80f, 0.96f), // 해파리 촉수 - 갓보다 더 밝게(반투명 인상)
            new Color(0.74f, 0.44f, 0.36f), // 문어 다리 밝은 적갈
            new Color(0.22f, 0.26f, 0.34f), // 돌고래 지느러미 짙은 회청
        };

        // ── 섬 규모별 마릿수 표 (인덱스 = 규모 티어 0 소형 / 1 중형 / 2 대형 / 3 특대) ──────
        // 경계는 SeabedFloraSpawner.SizeScale와 같은 반지름 중간값(70 / 115 / 170)이다.

        /// <summary>물고기 떼 무리 수(고정 - draw 없음). 무리당 마릿수는 8~20에서 뽑는다.</summary>
        private static readonly int[] SchoolCount = { 2, 3, 4, 5 };

        private static readonly int[] TurtleMin = { 0, 1, 1, 1 };
        private static readonly int[] TurtleMax = { 1, 1, 2, 2 };
        private static readonly int[] RayMin = { 0, 1, 1, 2 };
        private static readonly int[] RayMax = { 1, 1, 2, 2 };
        private static readonly int[] JellyMin = { 1, 2, 3, 4 };
        private static readonly int[] JellyMax = { 2, 3, 4, 5 };
        private static readonly int[] OctopusMin = { 0, 0, 1, 1 };
        private static readonly int[] OctopusMax = { 0, 1, 1, 2 };
        private static readonly int[] DolphinMin = { 0, 0, 0, 1 };
        private static readonly int[] DolphinMax = { 0, 0, 1, 1 };

        // ── 공유 메시 캐시 (모델 8종 × body/fin 두 장) ─────────────────────────────
        private static readonly Mesh[] marineBody = new Mesh[8];
        private static readonly Mesh[] marineFin = new Mesh[8];

        /// <summary>프레임당 1회 프로브 가드(SeabedFloraSpawner.probeFrame과 같은 규칙 - 같은 프레임의
        /// 섬 생성 루프에서 Resources.Load가 반복되지 않게 하되, 실패를 영구 래치하지 않는다).</summary>
        private static int probeFrame = -1;

        // ── 플레이어 참조 공유 캐시 (섬마다 드라이버가 따로 찾지 않게) ────────────────
        private static Transform playerTransform;
        private static SurvivalStats playerStats;

        /// <summary>플레이어 재탐색 프레임 가드. 섬 드라이버가 몇 개든 FindAnyObjectByType는
        /// 프레임당 최대 1회다(정상 경로에서는 아예 호출되지 않는다).</summary>
        private static int playerProbeFrame = -1;

        /// <summary>
        /// 도메인 리로드를 끈 플레이 모드에서 static 캐시/래치가 이전 실행의 파괴된 자원을 들고
        /// 시작하지 않게 초기 상태로 되돌린다.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStaticCache()
        {
            System.Array.Clear(marineBody, 0, marineBody.Length);
            System.Array.Clear(marineFin, 0, marineFin.Length);
            probeFrame = -1;
            playerTransform = null;
            playerStats = null;
            playerProbeFrame = -1;
        }

        // ══════════════════════════════════════════════════════════════════════════
        // 배치
        // ══════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// 섬 하나의 해저 주변 해양 생물을 배치한다. SeabedGenerator.Build가 스커트 레코드를 등록하고
        /// UnderwaterCaveSpawner를 부른 직후 같은 동기 흐름에서 호출한다(TrySampleSeabed 접지 전제).
        /// 마릿수·경로·색 draw는 배치/로드 성패와 무관하게 전량 소비한다.
        /// </summary>
        /// <param name="islandObject">섬 지형 루트("Island_{id}_{size}"). 배치물은 전부 이 자식이다.</param>
        /// <param name="radius">섬 지형 반지름 R(m). 스커트 안쪽 경계와 같다.</param>
        public static void Spawn(GameObject islandObject, float radius)
        {
            if (islandObject == null || radius <= 0f)
                return;

            // 같은 섬에 두 번 불려도(방어) 생물이 겹으로 깔리지 않게 한다(SeabedGenerator.Build와 동일).
            string rootName = MarineRootPrefix + islandObject.name;
            if (islandObject.transform.Find(rootName) != null)
                return;

            // worldSeed/seaLevel/islandId 읽기는 SeabedFloraSpawner.Spawn과 같은 경로(읽기 전용 -
            // 어떤 rng 스트림도 소비하지 않는다).
            var manager = islandObject.GetComponentInParent<WorldMapManager>();
            int worldSeed = manager != null ? manager.worldSeed : 0;
            float seaLevel = manager != null ? manager.seaLevel : 0f;
            int islandId = ParseIslandId(islandObject.name);

            // [rng 격리] 이 섬 전용 독립 스트림. 여기서 몇 번을 뽑든 다른 시스템의 추첨 순서는 불변이다.
            var rng = new System.Random(unchecked(worldSeed * 397 ^ islandId ^ SeedSalt));

            EnsureModelsLoaded();

            Vector3 center = islandObject.transform.position;
            // 스커트 폭. SeabedGenerator.SkirtWidth와 같은 식의 사본이다(그쪽은 private) - 어긋나도
            // 후보 적중률만 떨어질 뿐, 접지 정답은 항상 TrySampleSeabed가 준다(범위 밖이면 false).
            float skirtWidth = Mathf.Clamp(radius * 0.6f, 30f, 90f);
            float rMin = radius + 3f;
            float rMax = radius + skirtWidth - 3f;
            int tier = Tier(radius);

            // ── 1) 마릿수 draw (항상 같은 횟수·순서. 예산 초과는 draw가 아니라 배치에서 흡수한다) ──
            int schools = SchoolCount[tier];
            var schoolSizes = new int[schools];
            for (int i = 0; i < schools; i++)
                schoolSizes[i] = rng.NextInt(8, 21); // 무리당 8~20마리

            int turtles = rng.NextInt(TurtleMin[tier], TurtleMax[tier] + 1);
            int rays = rng.NextInt(RayMin[tier], RayMax[tier] + 1);
            int jellies = rng.NextInt(JellyMin[tier], JellyMax[tier] + 1);
            int octopuses = rng.NextInt(OctopusMin[tier], OctopusMax[tier] + 1);
            int dolphins = rng.NextInt(DolphinMin[tier], DolphinMax[tier] + 1);

            // 물고기 예산: 상한에서 대형 생물을 뺀 나머지를 무리별 요청량에 비례 배분한다.
            // (대형 생물 합은 티어 상한이 12라 예산은 항상 48 이상 = 무리당 최소 4마리를 보장한다.)
            int bigCount = turtles + rays + jellies + octopuses + dolphins;
            int fishBudget = Mathf.Max(0, MaxCreaturesPerIsland - bigCount);
            int requested = 0;
            for (int i = 0; i < schools; i++)
                requested += schoolSizes[i];
            if (requested > fishBudget && requested > 0)
            {
                float factor = fishBudget / (float)requested;
                for (int i = 0; i < schools; i++)
                    schoolSizes[i] = Mathf.Clamp(Mathf.FloorToInt(schoolSizes[i] * factor), 4, schoolSizes[i]);
            }

            // ── 2) 루트/컨테이너 ──────────────────────────────────────────────────
            var root = new GameObject(rootName);
            root.transform.SetParent(islandObject.transform, false);
            root.transform.localPosition = Vector3.zero;
            root.transform.localRotation = Quaternion.identity;
            root.transform.localScale = Vector3.one;

            var container = new GameObject(CreatureContainerName);
            container.transform.SetParent(root.transform, false);
            container.transform.localPosition = Vector3.zero;
            container.transform.localRotation = Quaternion.identity;
            container.transform.localScale = Vector3.one;

            var agents = new List<AgentState>(MaxCreaturesPerIsland);
            var bodies = new List<Transform>(MaxCreaturesPerIsland);
            var modelsA = new List<Transform>(MaxCreaturesPerIsland);
            var schoolStates = new List<SchoolState>(schools);

            // ── 3) 개체 배치 (종별 draw 순서 고정) ────────────────────────────────
            SpawnSchools(rng, container.transform, center, rMin, rMax, seaLevel,
                schoolSizes, schoolStates, agents, bodies, modelsA);
            SpawnPatrollers(rng, container.transform, center, rMin, rMax, seaLevel,
                AgentKind.Turtle, turtles, agents, bodies, modelsA);
            SpawnPatrollers(rng, container.transform, center, rMin, rMax, seaLevel,
                AgentKind.Ray, rays, agents, bodies, modelsA);
            SpawnJellies(rng, container.transform, center, rMin, rMax, seaLevel,
                jellies, agents, bodies, modelsA);
            SpawnOctopuses(rng, container.transform, islandObject.transform, center, rMin, rMax, seaLevel,
                octopuses, agents, bodies, modelsA);
            SpawnPatrollers(rng, container.transform, center, rMin, rMax, seaLevel,
                AgentKind.Dolphin, dolphins, agents, bodies, modelsA);

            // ── 4) 드라이버 하나 (개체마다 MonoBehaviour를 붙이지 않는다 - 성능 규칙) ──
            var driver = root.AddComponent<MarineLifeDriver>();
            driver.container = container.transform;
            driver.islandCenter = center;
            driver.islandRadius = radius;
            driver.seaLevel = seaLevel;
            driver.minRadius = radius + 2f;
            driver.maxRadius = radius + skirtWidth - 2f;
            driver.agents = agents.ToArray();
            driver.bodies = bodies.ToArray();
            driver.models = modelsA.ToArray();
            driver.schools = schoolStates.ToArray();
        }

        /// <summary>
        /// 물고기 떼. 무리 중심은 수심 2~12m 대역에서 뽑고, 중심이 리사주 궤도(주기가 다른 두 사인)를
        /// 돌면 개체는 그 주위 타원 오프셋 + 개체별 위상으로 흔들린다. 무리 하나는 색이 통일이고
        /// 종(a/b/c)은 섞인다. 개체 draw 수는 예산 배분이 끝난 마릿수를 따른다(모델 로드와 무관).
        /// </summary>
        private static void SpawnSchools(System.Random rng, Transform container, Vector3 center,
            float rMin, float rMax, float seaLevel, int[] schoolSizes,
            List<SchoolState> schoolStates, List<AgentState> agents,
            List<Transform> bodies, List<Transform> models)
        {
            for (int s = 0; s < schoolSizes.Length; s++)
            {
                int colorIndex = rng.NextInt(0, FishBodyPalette.Length);
                float depth = rng.NextFloat(2f, 12f);
                float radiusX = rng.NextFloat(6f, 14f);
                float radiusZ = rng.NextFloat(6f, 14f);
                float speedA = rng.NextFloat(0.10f, 0.18f);   // rad/s - 중심 선속도 ≈ 1~2.5m/s
                float speedB = rng.NextFloat(0.07f, 0.13f);
                float phaseA = rng.NextFloat(0f, Mathf.PI * 2f);
                float phaseB = rng.NextFloat(0f, Mathf.PI * 2f);
                float bobAmp = rng.NextFloat(0.6f, 1.8f);
                float bobSpeed = rng.NextFloat(0.25f, 0.5f);
                bool placed = TryPickAnchor(rng, center, rMin, rMax, seaLevel, depth,
                    out Vector3 anchor, out float seabedY);

                var state = new SchoolState
                {
                    anchor = anchor,
                    radiusX = radiusX,
                    radiusZ = radiusZ,
                    speedA = speedA,
                    speedB = speedB,
                    phaseA = phaseA,
                    phaseB = phaseB,
                    bobAmp = bobAmp,
                    bobSpeed = bobSpeed,
                    seabedY = seabedY,
                    scatter = 0f,
                    center = anchor,
                    forward = Vector3.forward,
                };

                int schoolIndex = schoolStates.Count;
                schoolStates.Add(state);

                for (int f = 0; f < schoolSizes[s]; f++)
                {
                    // draw는 배치 성공 여부와 무관하게 항상 소비한다(아래 placed 분기보다 먼저).
                    int variant = PickFishVariant(rng);
                    float ox = rng.NextFloat(-3.2f, 3.2f);
                    float oy = rng.NextFloat(-1.1f, 1.1f);
                    float oz = rng.NextFloat(-3.2f, 3.2f);
                    float phase = rng.NextFloat(0f, Mathf.PI * 2f);
                    float phase2 = rng.NextFloat(0f, Mathf.PI * 2f);
                    float scale = rng.NextFloat(0.85f, 1.25f);

                    if (!placed)
                        continue;

                    Transform model;
                    Transform body = CreateCreature(container, "MarineFish_" + schoolIndex + "_" + f,
                        variant, anchor, scale, FishBodyPalette[colorIndex], FishFinPalette[colorIndex],
                        out model);
                    if (body == null)
                        continue;

                    agents.Add(new AgentState
                    {
                        kind = (int)AgentKind.Fish,
                        school = schoolIndex,
                        offset = new Vector3(ox, oy, oz),
                        anchor = anchor,
                        phase = phase,
                        phase2 = phase2,
                        scale = scale,
                        seabedY = seabedY,
                    });
                    bodies.Add(body);
                    models.Add(model);
                }
            }
        }

        /// <summary>
        /// 순회형 대형 생물(거북 · 가오리 · 돌고래). 각자 타원 순회 경로를 돌고, 해저 높이를 따라
        /// 일정 고도를 유지한다(가오리는 바닥 가까이, 거북·돌고래는 목표 수심). 속도/경로 반경만
        /// 종별로 다르고 나머지 규약은 같다.
        /// </summary>
        private static void SpawnPatrollers(System.Random rng, Transform container, Vector3 center,
            float rMin, float rMax, float seaLevel, AgentKind kind, int count,
            List<AgentState> agents, List<Transform> bodies, List<Transform> models)
        {
            for (int i = 0; i < count; i++)
            {
                float depthMin, depthMax, speedMin, speedMax, pathMin, pathMax, scaleMin, scaleMax;
                int model;
                switch (kind)
                {
                    case AgentKind.Turtle: // 느리게 유영, 수심 3~12m
                        depthMin = 3f; depthMax = 12f;
                        speedMin = 0.45f; speedMax = 0.9f;
                        pathMin = 8f; pathMax = 16f;
                        scaleMin = 0.85f; scaleMax = 1.15f;
                        model = ModelTurtle;
                        break;
                    case AgentKind.Ray:    // 바닥 가까이, 수심 5~14m
                        depthMin = 5f; depthMax = 14f;
                        speedMin = 0.7f; speedMax = 1.2f;
                        pathMin = 7f; pathMax = 15f;
                        scaleMin = 0.8f; scaleMax = 1.2f;
                        model = ModelRay;
                        break;
                    default:               // 돌고래 - 빠르게, 수심 3~10m
                        depthMin = 3f; depthMax = 10f;
                        speedMin = 3f; speedMax = 4.5f;
                        pathMin = 18f; pathMax = 30f;
                        scaleMin = 0.9f; scaleMax = 1.1f;
                        model = ModelDolphin;
                        break;
                }

                float depth = rng.NextFloat(depthMin, depthMax);
                float pathRadius = rng.NextFloat(pathMin, pathMax);
                float linearSpeed = rng.NextFloat(speedMin, speedMax);
                float phase = rng.NextFloat(0f, Mathf.PI * 2f);
                float phase2 = rng.NextFloat(0f, Mathf.PI * 2f);
                float scale = rng.NextFloat(scaleMin, scaleMax);
                float altitude = rng.NextFloat(0.6f, 1.4f); // 가오리 전용 - 해저 위 고도
                bool clockwise = rng.NextValue01() < 0.5f;
                bool placed = TryPickAnchor(rng, center, rMin, rMax, seaLevel, depth,
                    out Vector3 anchor, out float seabedY);
                if (!placed)
                    continue;

                int paletteIndex = model - ModelTurtle;
                Transform modelPart;
                Transform body = CreateCreature(container, KindName(kind) + "_" + i, model, anchor, scale,
                    BigBodyPalette[paletteIndex], BigFinPalette[paletteIndex], out modelPart);
                if (body == null)
                    continue;

                agents.Add(new AgentState
                {
                    kind = (int)kind,
                    school = -1,
                    anchor = anchor,
                    pathRadius = pathRadius,
                    // 각속도로 환산해 두면 갱신에서 나눗셈이 사라진다(선속도 = 반경 × 각속도).
                    angularSpeed = (clockwise ? -1f : 1f) * linearSpeed / Mathf.Max(1f, pathRadius),
                    phase = phase,
                    phase2 = phase2,
                    depth = depth,
                    altitude = altitude,
                    scale = scale,
                    seabedY = seabedY,
                });
                bodies.Add(body);
                models.Add(modelPart);
            }
        }

        /// <summary>
        /// 해파리. 수심 1~10m를 아주 느리게 표류하며 갓을 수축시키는 맥동(모델 파츠의 y 스케일)을
        /// 위아래 표류와 같은 주기로 준다. **접촉 시 중독**(드라이버의 거리 판정 - 클래스 주석).
        /// </summary>
        private static void SpawnJellies(System.Random rng, Transform container, Vector3 center,
            float rMin, float rMax, float seaLevel, int count,
            List<AgentState> agents, List<Transform> bodies, List<Transform> models)
        {
            for (int i = 0; i < count; i++)
            {
                float depth = rng.NextFloat(1f, 10f);
                float pathRadius = rng.NextFloat(2f, 5f);
                float linearSpeed = rng.NextFloat(0.12f, 0.28f);
                float phase = rng.NextFloat(0f, Mathf.PI * 2f);
                float phase2 = rng.NextFloat(0f, Mathf.PI * 2f);
                float scale = rng.NextFloat(0.8f, 1.35f);
                float pulseSpeed = rng.NextFloat(1.2f, 1.9f);
                bool clockwise = rng.NextValue01() < 0.5f;
                bool placed = TryPickAnchor(rng, center, rMin, rMax, seaLevel, depth,
                    out Vector3 anchor, out float seabedY);
                if (!placed)
                    continue;

                int paletteIndex = ModelJelly - ModelTurtle;
                Transform modelPart;
                Transform body = CreateCreature(container, "MarineJelly_" + i, ModelJelly, anchor, scale,
                    BigBodyPalette[paletteIndex], BigFinPalette[paletteIndex], out modelPart);
                if (body == null)
                    continue;

                agents.Add(new AgentState
                {
                    kind = (int)AgentKind.Jelly,
                    school = -1,
                    anchor = anchor,
                    pathRadius = pathRadius,
                    angularSpeed = (clockwise ? -1f : 1f) * linearSpeed / Mathf.Max(1f, pathRadius),
                    phase = phase,
                    phase2 = phase2,
                    depth = depth,
                    scale = scale,
                    pulseSpeed = pulseSpeed,
                    // 접촉 반경 = 갓 반경 × 크기 + 0.2m(요구 규격).
                    contactRadius = JellyBellRadius * scale + 0.2f,
                    pivot = MarinePivots[ModelJelly],
                    seabedY = seabedY,
                });
                bodies.Add(body);
                models.Add(modelPart);
            }
        }

        /// <summary>
        /// 문어. 거의 정지 상태로 해저에 정착하고 가끔 자세(요)만 바뀐다. 배치 자리는 후보 지점에서
        /// **가장 가까운 바위/산호**(SeabedFlora_ 루트의 "SeaRock_"/"Coral_" 자식) 발치로 당긴다 -
        /// rng를 소비하지 않는 순수 기하 보정이라 추첨 순서는 한 칸도 밀리지 않는다.
        /// </summary>
        private static void SpawnOctopuses(System.Random rng, Transform container, Transform islandRoot,
            Vector3 center, float rMin, float rMax, float seaLevel, int count,
            List<AgentState> agents, List<Transform> bodies, List<Transform> models)
        {
            for (int i = 0; i < count; i++)
            {
                float depth = rng.NextFloat(4f, 12f);
                float phase = rng.NextFloat(0f, Mathf.PI * 2f);
                float phase2 = rng.NextFloat(0f, Mathf.PI * 2f);
                float scale = rng.NextFloat(0.85f, 1.2f);
                float yaw = rng.NextFloat(0f, 360f);
                bool placed = TryPickAnchor(rng, center, rMin, rMax, seaLevel, depth,
                    out Vector3 anchor, out float seabedY);
                if (!placed)
                    continue;

                // 바위/산호 근처로 당긴다(찾지 못하면 원래 자리 그대로).
                Vector3 perch = FindNearestShelter(islandRoot, anchor);
                if (perch.sqrMagnitude > 0f)
                {
                    Vector3 away = anchor - perch;
                    away.y = 0f;
                    if (away.sqrMagnitude < 0.01f)
                        away = Vector3.forward;
                    anchor = perch + away.normalized * 1.1f;
                    if (SeabedGenerator.TrySampleSeabed(anchor, out float perchY))
                        seabedY = perchY;
                }

                // 문어는 헤엄치지 않고 바닥에 앉는다 - 몸통 중심이 해저 살짝 위다.
                anchor.y = seabedY + MarinePivots[ModelOctopus].y * scale;

                int paletteIndex = ModelOctopus - ModelTurtle;
                Transform modelPart;
                Transform body = CreateCreature(container, "MarineOctopus_" + i, ModelOctopus, anchor, scale,
                    BigBodyPalette[paletteIndex], BigFinPalette[paletteIndex], out modelPart);
                if (body == null)
                    continue;

                agents.Add(new AgentState
                {
                    kind = (int)AgentKind.Octopus,
                    school = -1,
                    anchor = anchor,
                    phase = phase,
                    phase2 = phase2,
                    depth = depth,
                    scale = scale,
                    baseYaw = yaw,
                    seabedY = seabedY,
                });
                bodies.Add(body);
                models.Add(modelPart);
            }
        }

        // ══════════════════════════════════════════════════════════════════════════
        // 개체 조립
        // ══════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// 생물 하나(루트 + 모델 파츠). 모델 파츠는 `localPosition = -pivot × scale`로 몸통 중심을
        /// 루트 원점에 맞춘다. 병합 임포트(서브메시 2)면 렌더러 하나에 [몸통색, 지느러미색] 배열을,
        /// 개별 메시 2장이면 파츠를 하나 더 만든다(PlaceCoral/PlaceClam과 같은 분기).
        /// 메시가 아직 로드되지 않았으면 null을 돌려주고 호출부가 그 개체만 건너뛴다(래치 없음).
        /// </summary>
        private static Transform CreateCreature(Transform container, string name, int model,
            Vector3 worldPos, float scale, Color bodyColor, Color finColor, out Transform modelPart)
        {
            modelPart = null;
            Mesh body = marineBody[model];
            if (body == null)
                return null; // 이 종만 아직 안 로드됨 - 다음 월드에서 자연 복구

            Material bodyMaterial = ResourceVisualLibrary.GetMaterial(bodyColor, "noise");
            Material finMaterial = ResourceVisualLibrary.GetMaterial(finColor, "noise");

            // 생물 루트("Marine*_"). "Island_" 비접두라 지형 판정에서 구조적으로 제외된다.
            var go = new GameObject(name);
            go.transform.SetParent(container, false);
            go.transform.position = worldPos;
            go.transform.rotation = Quaternion.identity;

            Vector3 pivotOffset = -MarinePivots[model] * scale;
            var part = CreateVisualPart(go.transform, MarineModelNames[model], body, bodyMaterial,
                pivotOffset, scale);
            modelPart = part.transform;

            var renderer = part.GetComponent<MeshRenderer>();
            if (marineFin[model] != null)
            {
                // 개별 메시 임포트: 지느러미를 같은 위치/스케일의 파츠로 하나 더(임포터 동작 방어).
                CreateVisualPart(go.transform, MarineModelNames[model] + "_fin", marineFin[model],
                    finMaterial, pivotOffset, scale);
            }
            else if (renderer != null && body.subMeshCount >= 2)
            {
                // 병합 임포트(Unity 6.5 실동작): 서브메시 순서는 OBJ `o` 순서(body → fin)다.
                renderer.sharedMaterials = new[] { bodyMaterial, finMaterial };
            }

            return go.transform;
        }

        /// <summary>공유 메시 + 공유 머티리얼로 순수 시각 파츠 하나(콜라이더 없음). 수중이라 그림자
        /// 캐스팅/수신을 모두 끈다(SeabedFloraSpawner.CreateVisualPart와 같은 규칙).</summary>
        private static GameObject CreateVisualPart(Transform parent, string name, Mesh mesh,
            Material material, Vector3 localPos, float scale)
        {
            var go = StructureVisualBuilder.CreateMeshPart(parent, name, mesh,
                localPos, Vector3.one * scale, Quaternion.identity, material);
            var renderer = go.GetComponent<MeshRenderer>();
            if (renderer != null)
            {
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }

            return go;
        }

        // ══════════════════════════════════════════════════════════════════════════
        // 후보 샘플링 / 유틸
        // ══════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// 섬 중심 기준 극좌표 후보를 뽑아 SeabedGenerator.TrySampleSeabed로 접지하고, 요청 수심만큼
        /// 잠길 여유(해저 아래로 파고들지 않을 것)가 있으면 채택한다. 시도 수는 고정(무한 루프 금지)
        /// 이고 실패해도 draw 수는 이 함수 안에서만 달라진다(결정적 - 해저 수식이 순수 함수다).
        /// worldPos.y에는 "해수면 - 요청 수심"(= 유영 고도)이 들어간다.
        /// </summary>
        private static bool TryPickAnchor(System.Random rng, Vector3 center, float rMin, float rMax,
            float seaLevel, float depth, out Vector3 worldPos, out float seabedY)
        {
            for (int attempt = 0; attempt < 16; attempt++)
            {
                float angle = rng.NextFloat(0f, Mathf.PI * 2f);
                float r = rng.NextFloat(rMin, rMax);
                var candidate = new Vector3(
                    center.x + Mathf.Cos(angle) * r, 0f, center.z + Mathf.Sin(angle) * r);

                if (!SeabedGenerator.TrySampleSeabed(candidate, out float y))
                    continue;

                // 해저가 요청 수심보다 얕으면(바닥에 파묻힌다) 그 후보만 버린다.
                float floorDepth = seaLevel - y;
                if (floorDepth < depth + 1f)
                    continue;

                worldPos = new Vector3(candidate.x, seaLevel - depth, candidate.z);
                seabedY = y;
                return true;
            }

            worldPos = Vector3.zero;
            seabedY = 0f;
            return false;
        }

        /// <summary>
        /// 후보 지점에서 12m 안의 가장 가까운 수중 바위/산호 위치(문어 정착지). 없으면 Vector3.zero다.
        /// 해저 생태 루트의 자식 이름만 보는 순수 기하 탐색이라 rng를 소비하지 않는다
        /// (배치 시점 1회 - 섬당 문어 최대 2마리라 비용도 무시할 수준이다).
        /// </summary>
        private static Vector3 FindNearestShelter(Transform islandRoot, Vector3 near)
        {
            if (islandRoot == null)
                return Vector3.zero;

            Transform flora = islandRoot.Find("SeabedFlora_" + islandRoot.name);
            if (flora == null)
                return Vector3.zero;

            Vector3 best = Vector3.zero;
            float bestSq = 144f; // 12m
            int count = flora.childCount;
            for (int i = 0; i < count; i++)
            {
                Transform child = flora.GetChild(i);
                if (child == null)
                    continue;
                string childName = child.name;
                if (!childName.StartsWith("SeaRock_", System.StringComparison.Ordinal)
                    && !childName.StartsWith("Coral_", System.StringComparison.Ordinal))
                    continue;

                Vector3 pos = child.position;
                float dx = pos.x - near.x;
                float dz = pos.z - near.z;
                float sq = dx * dx + dz * dz;
                if (sq < bestSq)
                {
                    bestSq = sq;
                    best = pos;
                }
            }

            return best;
        }

        /// <summary>물고기 종 추첨(a 45% / b 35% / c 20%). draw는 항상 1회다.</summary>
        private static int PickFishVariant(System.Random rng)
        {
            float roll = rng.NextValue01();
            if (roll < 0.45f)
                return ModelFishA;
            if (roll < 0.80f)
                return ModelFishA + 1;
            return ModelFishA + 2;
        }

        /// <summary>순회형 생물의 오브젝트 이름 접두("Island_" 비접두 규약).</summary>
        private static string KindName(AgentKind kind)
        {
            switch (kind)
            {
                case AgentKind.Turtle: return "MarineTurtle";
                case AgentKind.Ray: return "MarineRay";
                case AgentKind.Dolphin: return "MarineDolphin";
                default: return "MarineLife";
            }
        }

        /// <summary>섬 규모 티어(0 소형 / 1 중형 / 2 대형 / 3 특대). 경계는 반지름 중간값
        /// (SeabedFloraSpawner.SizeScale / UnderwaterCaveSpawner와 같은 70·115·170).</summary>
        private static int Tier(float radius)
        {
            if (radius < 70f) return 0;
            if (radius < 115f) return 1;
            if (radius < 170f) return 2;
            return 3;
        }

        /// <summary>"Island_{id}_{size}"에서 islandId 파싱(SeabedFloraSpawner.ParseIslandId 사본).</summary>
        private static int ParseIslandId(string islandName)
        {
            if (string.IsNullOrEmpty(islandName))
                return 0;
            string[] tokens = islandName.Split('_');
            if (tokens.Length >= 2 && int.TryParse(tokens[1], out int id))
                return id;
            return 0;
        }

        /// <summary>
        /// 모델 8종(marine_a~h)의 공유 메시를 채운다. ResourceVisualLibrary.TryLoadTwoPartModel의
        /// 검증된 경로 그대로이고(Load&lt;GameObject&gt; + MeshFilter), 프레임당 1회만 프로브하며
        /// 실패를 영구 래치하지 않는다. `o` 이름(body/fin)은 로더의 이름 키워드(trunk/leaf 계열)에
        /// 걸리지 않으므로 "o 등장 순서" 폴백이 body → fin 순서를 보장한다.
        /// </summary>
        private static void EnsureModelsLoaded()
        {
            bool anyMissing = false;
            for (int i = 0; i < marineBody.Length && !anyMissing; i++)
                anyMissing = marineBody[i] == null;

            if (!anyMissing || probeFrame == Time.frameCount)
                return;
            probeFrame = Time.frameCount;

            for (int i = 0; i < MarineModelNames.Length; i++)
            {
                if (marineBody[i] != null)
                    continue;
                if (ResourceVisualLibrary.TryLoadTwoPartModel("Models/" + MarineModelNames[i],
                        out Mesh body, out Mesh fin))
                {
                    marineBody[i] = body;
                    marineFin[i] = fin; // 병합 임포트면 null - CreateCreature의 서브메시 분기가 처리
                }
            }
        }

        /// <summary>
        /// 플레이어 Transform/SurvivalStats 공유 캐시. 못 찾았을 때만, 그것도 60프레임에 한 번,
        /// 프레임당 최대 1회 재탐색한다(GrassFieldDriver의 저빈도 재시도 규칙 - 정상 경로에서 탐색
        /// 비용·할당 0). 섬 드라이버가 몇 개든 이 가드 하나를 공유한다.
        /// </summary>
        private static void EnsurePlayer()
        {
            if (playerTransform != null)
                return;
            if (playerProbeFrame == Time.frameCount || Time.frameCount % 60 != 0)
                return;
            playerProbeFrame = Time.frameCount;

            var stats = Object.FindAnyObjectByType<SurvivalStats>();
            if (stats == null)
                return;
            playerStats = stats;
            playerTransform = stats.transform;
        }

        // ══════════════════════════════════════════════════════════════════════════
        // 갱신 (섬당 드라이버 1개 · 프레임당 할당 0)
        // ══════════════════════════════════════════════════════════════════════════

        /// <summary>생물 종류. AgentState.kind에 int로 들어간다(직렬화 안전).</summary>
        private enum AgentKind
        {
            Fish = 0,
            Turtle = 1,
            Ray = 2,
            Jelly = 3,
            Octopus = 4,
            Dolphin = 5,
        }

        /// <summary>
        /// 물고기 무리 하나의 상태. center/forward는 매 프레임 계산 결과를 담아 개체 갱신이 다시
        /// 계산하지 않게 하는 캐시다(무리당 사인 4회 → 개체는 덧셈만).
        /// </summary>
        [System.Serializable]
        private struct SchoolState
        {
            public Vector3 anchor;      // 궤도 중심(월드). y = 유영 고도
            public float radiusX;
            public float radiusZ;
            public float speedA;
            public float speedB;
            public float phaseA;
            public float phaseB;
            public float bobAmp;
            public float bobSpeed;
            public float seabedY;       // 최근 샘플한 해저 높이(스태거 갱신)
            public float scatter;       // 0 = 모임 / 1 = 완전히 흩어짐
            public Vector3 center;      // 이번 프레임 무리 중심
            public Vector3 forward;     // 이번 프레임 무리 진행 방향
        }

        /// <summary>
        /// 개체 하나의 상태. 전부 값 타입이라 배열째로 도메인 리로드를 넘어 살아남고(직렬화 가능),
        /// 갱신 중 어떤 할당도 만들지 않는다.
        /// </summary>
        [System.Serializable]
        private struct AgentState
        {
            public int kind;
            public int school;          // 물고기만 유효(그 외 -1)
            public Vector3 offset;      // 물고기: 무리 중심 기준 오프셋
            public Vector3 anchor;      // 순회 중심(월드)
            public float pathRadius;
            public float angularSpeed;  // rad/s(부호 = 회전 방향)
            public float phase;
            public float phase2;
            public float depth;         // 목표 수심(해수면 기준, m)
            public float altitude;      // 가오리 전용 - 해저 위 고도(m)
            public float scale;
            public float pulseSpeed;    // 해파리 갓 맥동 주기
            public float contactRadius; // 해파리 독 접촉 반경
            public Vector3 pivot;       // 해파리 맥동에서 갓 꼭대기를 고정하는 데 쓴다
            public float baseYaw;       // 문어 기준 자세
            public float seabedY;       // 최근 샘플한 해저 높이(스태거 갱신)
            public float poisonTimer;   // 해파리 독 쿨다운 잔량(초)
        }

        /// <summary>
        /// 섬 하나의 모든 생물을 갱신하는 **유일한** 컴포넌트. 배치 루트("MarineLife_*")에 붙어
        /// RegenerateWorld의 섬 파괴에 함께 편승한다.
        ///
        /// LateUpdate인 이유: 플레이어 이동(Update)이 끝난 최종 위치로 흩어짐/독 접촉을 판정해야
        /// 한 프레임 늦지 않는다(GrassFieldDriver와 같은 자리).
        ///
        /// 시간은 Time.time이라 timeScale = 0(타이틀·엔딩·사망 화면)에서 바다도 함께 멈춘다.
        /// </summary>
        private sealed class MarineLifeDriver : MonoBehaviour
        {
            public Transform container;
            public Vector3 islandCenter;
            public float islandRadius;
            public float seaLevel;
            public float minRadius;      // 생물이 벗어나면 안 되는 스커트 안쪽 경계
            public float maxRadius;      // 〃 바깥 경계
            public AgentState[] agents;
            public SchoolState[] schools;
            public Transform[] bodies;
            public Transform[] models;

            /// <summary>컨테이너의 현재 활성 상태(변할 때만 SetActive를 부른다 - 프레임당 비용 0).</summary>
            private bool creaturesActive = true;

            private void LateUpdate()
            {
                // 도메인 리로드 직후처럼 배열이 비어 있을 수 있다(플래그로 참조 존재를 보증하지 않는다).
                if (agents == null || bodies == null || schools == null || container == null)
                    return;

                EnsurePlayer();

                // ── 비활성 거리: 섬 테두리에서 200m를 넘으면 통째로 쉰다(거리 계산 1회) ──
                Vector3 viewer = playerTransform != null
                    ? playerTransform.position
                    : (Camera.main != null ? Camera.main.transform.position : islandCenter);
                float dx = viewer.x - islandCenter.x;
                float dz = viewer.z - islandCenter.z;
                bool nearby = Mathf.Sqrt(dx * dx + dz * dz) - islandRadius <= ActiveDistance;
                if (nearby != creaturesActive)
                {
                    creaturesActive = nearby;
                    container.gameObject.SetActive(nearby);
                }

                if (!nearby)
                    return;

                float time = Time.time;
                float dt = Time.deltaTime;
                int frame = Time.frameCount;

                UpdateSchools(time, dt, viewer, frame);

                // 두 배열은 같은 길이로 만들어지지만, 도메인 리로드 복원이 어긋나도 인덱스가 튀지
                // 않게 짧은 쪽을 따른다(플래그로 참조 존재를 보증하지 않는다는 규칙과 같은 이유).
                int count = Mathf.Min(agents.Length, bodies.Length);
                for (int i = 0; i < count; i++)
                {
                    Transform body = bodies[i];
                    if (body == null)
                        continue;

                    switch ((AgentKind)agents[i].kind)
                    {
                        case AgentKind.Fish:
                            UpdateFish(i, body, time, viewer);
                            break;
                        case AgentKind.Jelly:
                            UpdateJelly(i, body, time, dt, frame);
                            break;
                        case AgentKind.Octopus:
                            UpdateOctopus(i, body, time);
                            break;
                        default:
                            UpdatePatroller(i, body, time, frame);
                            break;
                    }
                }
            }

            /// <summary>
            /// 무리 중심의 리사주 궤도(주기가 다른 두 사인 + 완만한 상하)와 흩어짐 계수를 갱신한다.
            /// 플레이어가 6m 안에 들어오면 빠르게(2.5/s) 흩어지고, 벗어나면 천천히(0.55/s) 다시 모인다.
            /// </summary>
            private void UpdateSchools(float time, float dt, Vector3 viewer, int frame)
            {
                for (int s = 0; s < schools.Length; s++)
                {
                    SchoolState school = schools[s];

                    float cx = school.anchor.x + school.radiusX * Mathf.Sin(time * school.speedA + school.phaseA);
                    float cz = school.anchor.z + school.radiusZ * Mathf.Sin(time * school.speedB + school.phaseB);
                    float cy = school.anchor.y + school.bobAmp * Mathf.Sin(time * school.bobSpeed + school.phaseA);

                    // 진행 방향 = 궤도의 해석적 접선(위치 차분을 저장할 필요가 없다).
                    float fx = school.radiusX * school.speedA * Mathf.Cos(time * school.speedA + school.phaseA);
                    float fz = school.radiusZ * school.speedB * Mathf.Cos(time * school.speedB + school.phaseB);
                    float fy = school.bobAmp * school.bobSpeed * Mathf.Cos(time * school.bobSpeed + school.phaseA);

                    var center = new Vector3(cx, cy, cz);
                    center = ClampToSkirt(center);

                    // 해저 샘플은 무리마다 12프레임에 한 번(스태거)으로 나눠 낸다.
                    if ((frame + s) % 12 == 0 && SeabedGenerator.TrySampleSeabed(center, out float seabedY))
                        school.seabedY = seabedY;
                    center.y = ClampDepthY(center.y, school.seabedY + 1.2f, 0.6f);

                    float distanceSq = HorizontalSqDistance(center, viewer);
                    float target = distanceSq < ScatterRadius * ScatterRadius ? 1f : 0f;
                    float rate = target > school.scatter ? 2.5f : 0.55f;
                    school.scatter = Mathf.MoveTowards(school.scatter, target, rate * dt);

                    school.center = center;
                    var forward = new Vector3(fx, fy, fz);
                    school.forward = forward.sqrMagnitude > 1e-6f ? forward.normalized : Vector3.forward;
                    schools[s] = school;
                }
            }

            /// <summary>
            /// 물고기 한 마리: 무리 중심 + 개체 오프셋(흩어짐 배수) + 개체 위상 흔들림. 흩어짐 중에는
            /// 플레이어 반대쪽으로 밀려나고 머리도 그쪽을 본다. 물리·콜라이더 없음, 순수 위치 계산이다.
            /// </summary>
            private void UpdateFish(int i, Transform body, float time, Vector3 viewer)
            {
                AgentState agent = agents[i];
                if (agent.school < 0 || agent.school >= schools.Length)
                    return;

                SchoolState school = schools[agent.school];
                float spread = 1f + school.scatter * 2.4f;

                Vector3 pos = school.center + agent.offset * spread;
                pos.x += 0.25f * Mathf.Sin(time * 1.7f + agent.phase);
                pos.y += 0.18f * Mathf.Sin(time * 2.3f + agent.phase2);
                pos.z += 0.25f * Mathf.Cos(time * 1.9f + agent.phase);

                Vector3 forward = school.forward;
                if (school.scatter > 0.01f)
                {
                    // 플레이어에게서 멀어지는 수평 방향으로 밀어내고, 그 방향을 본다.
                    float ax = pos.x - viewer.x;
                    float az = pos.z - viewer.z;
                    float len = Mathf.Sqrt(ax * ax + az * az);
                    if (len > 0.01f)
                    {
                        ax /= len;
                        az /= len;
                        pos.x += ax * school.scatter * 2.5f;
                        pos.z += az * school.scatter * 2.5f;
                        forward = Vector3.Lerp(forward, new Vector3(ax, 0f, az), school.scatter);
                    }
                }

                pos = ClampToSkirt(pos);
                pos.y = ClampDepthY(pos.y, school.seabedY + 0.5f, 0.35f);
                body.SetPositionAndRotation(pos, LookRotation(forward));
            }

            /// <summary>
            /// 순회형(거북·가오리·돌고래): 타원 경로를 돌며 해저를 따라 고도를 유지한다.
            /// 가오리는 해저 위 0.6~1.4m, 나머지는 목표 수심 ± 완만한 상하다. 진행 방향은 경로 접선.
            /// </summary>
            private void UpdatePatroller(int i, Transform body, float time, int frame)
            {
                AgentState agent = agents[i];
                float angle = time * agent.angularSpeed + agent.phase;
                float cos = Mathf.Cos(angle);
                float sin = Mathf.Sin(angle);

                var pos = new Vector3(
                    agent.anchor.x + agent.pathRadius * cos,
                    agent.anchor.y,
                    agent.anchor.z + agent.pathRadius * 0.75f * sin);
                pos = ClampToSkirt(pos);

                // 해저 샘플은 개체마다 12프레임에 한 번(스태거) - 활성 섬 전체로도 프레임당 1~2회다.
                if ((frame + i) % 12 == 0 && SeabedGenerator.TrySampleSeabed(pos, out float seabedY))
                {
                    agent.seabedY = seabedY;
                    agents[i] = agent;
                }

                float targetY;
                if ((AgentKind)agent.kind == AgentKind.Ray)
                    targetY = agent.seabedY + agent.altitude; // 바닥 가까이
                else
                    targetY = seaLevel - agent.depth + 0.6f * Mathf.Sin(time * 0.35f + agent.phase2);

                float minY = agent.seabedY + (((AgentKind)agent.kind == AgentKind.Ray) ? 0.25f : 1.0f);
                pos.y = ClampDepthY(targetY, minY, 0.8f);

                // 접선(-sin, cos) × 반경 - 수직 성분은 완만해서 요만 따라가면 충분하다.
                var forward = new Vector3(-sin * agent.angularSpeed, 0f, cos * 0.75f * agent.angularSpeed);
                body.SetPositionAndRotation(pos, LookRotation(forward));
            }

            /// <summary>
            /// 해파리: 아주 느린 표류 + 갓 수축 맥동. 맥동은 모델 파츠의 y 스케일로 흉내 내고,
            /// 파츠 위치를 `-pivot × scale`로 함께 보정해 **갓 꼭대기(회전/맥동 중심)가 고정**되게 한다.
            /// 수축할 때 살짝 떠오르도록 상하 표류를 같은 주기·다른 위상으로 묶었다.
            ///
            /// [독 접촉] 콜라이더가 아니라 거리 판정이다. 이 생물들은 Rigidbody가 없어 트리거를 붙여도
            /// (a) 매 프레임 움직이는 static 콜라이더라 물리 트리 재구축 비용이 들고 (b) 개체마다
            /// MonoBehaviour를 붙이지 않는 규칙 때문에 OnTrigger 콜백을 받을 주체가 없으며
            /// (c) Physics.queriesHitTriggers 기본값이 true라 상호작용 레이를 가로챈다.
            /// 반경 규격(갓 반경 × 크기 + 0.2m)은 그대로 쓰고, 플레이어는 발밑~머리(2m) 선분으로 봐서
            /// 캡슐 몸통에 정확히 대응시킨다. 개체당 8초 쿨다운으로 도배를 막는다.
            /// </summary>
            private void UpdateJelly(int i, Transform body, float time, float dt, int frame)
            {
                AgentState agent = agents[i];
                float angle = time * agent.angularSpeed + agent.phase;
                float pulsePhase = time * agent.pulseSpeed + agent.phase2;

                var pos = new Vector3(
                    agent.anchor.x + agent.pathRadius * Mathf.Cos(angle),
                    agent.anchor.y + 0.55f * Mathf.Sin(pulsePhase - 0.6f),
                    agent.anchor.z + agent.pathRadius * Mathf.Sin(angle));
                pos = ClampToSkirt(pos);

                if ((frame + i) % 12 == 0 && SeabedGenerator.TrySampleSeabed(pos, out float seabedY))
                    agent.seabedY = seabedY;

                pos.y = ClampDepthY(pos.y, agent.seabedY + 0.8f, 0.4f);
                body.SetPositionAndRotation(pos, Quaternion.Euler(0f, agent.phase * Mathf.Rad2Deg + time * 6f, 0f));

                // 갓 수축: y만 ±16% 맥동시키고, 파츠 위치로 갓 꼭대기를 원위치에 붙들어 둔다.
                Transform model = models != null && i < models.Length ? models[i] : null;
                if (model != null)
                {
                    float pulse = 1f + 0.16f * Mathf.Sin(pulsePhase);
                    var scaleVector = new Vector3(agent.scale, agent.scale * pulse, agent.scale);
                    model.localScale = scaleVector;
                    model.localPosition = new Vector3(
                        -agent.pivot.x * scaleVector.x,
                        -agent.pivot.y * scaleVector.y,
                        -agent.pivot.z * scaleVector.z);
                }

                // 접촉 독(개체당 8초 쿨다운).
                if (agent.poisonTimer > 0f)
                    agent.poisonTimer -= dt;
                else if (playerStats != null && playerTransform != null)
                {
                    float reach = agent.contactRadius + 0.5f; // 플레이어 캡슐 반경(씬 0.5m)
                    if (SqDistanceToPlayerBody(pos, playerTransform.position) <= reach * reach)
                    {
                        playerStats.ApplyPoison();
                        agent.poisonTimer = PoisonCooldown;
                    }
                }

                agents[i] = agent;
            }

            /// <summary>문어: 거의 정지. 위치는 배치 때 잡은 해저 접지 그대로이고, 자세(요)만 두 개의
            /// 느린 사인으로 가끔 바뀐다(+ 아주 작은 숨쉬기 상하).</summary>
            private void UpdateOctopus(int i, Transform body, float time)
            {
                AgentState agent = agents[i];
                float yaw = agent.baseYaw
                    + 28f * Mathf.Sin(time * 0.11f + agent.phase)
                    + 10f * Mathf.Sin(time * 0.29f + agent.phase2);
                var pos = agent.anchor;
                pos.y += 0.04f * Mathf.Sin(time * 0.6f + agent.phase);
                body.SetPositionAndRotation(pos, Quaternion.Euler(0f, yaw, 0f));
            }

            /// <summary>스커트 환형(섬 메시 밖 ~ 스커트 바깥) 안으로 XZ를 잡아당긴다 - 생물이 섬 지형
            /// 안이나 스커트 밖 허공으로 헤엄쳐 나가지 않게 하는 유일한 경계 조건이다.</summary>
            private Vector3 ClampToSkirt(Vector3 pos)
            {
                float dx = pos.x - islandCenter.x;
                float dz = pos.z - islandCenter.z;
                float distance = Mathf.Sqrt(dx * dx + dz * dz);
                if (distance < 0.001f)
                    return new Vector3(islandCenter.x + minRadius, pos.y, islandCenter.z);

                float clamped = Mathf.Clamp(distance, minRadius, maxRadius);
                if (Mathf.Approximately(clamped, distance))
                    return pos;

                float scale = clamped / distance;
                return new Vector3(islandCenter.x + dx * scale, pos.y, islandCenter.z + dz * scale);
            }

            /// <summary>해저 바닥(floorY)과 수면 사이로 y를 가둔다. 얕은 자리에서 구간이 뒤집히면
            /// **수면 쪽을 우선**한다 - Mathf.Clamp는 min &gt; max일 때 min을 돌려주므로 그대로 두면
            /// 생물이 해수면 위로 튀어나올 수 있다(스커트 안쪽 얕은 링에서 실제로 가능하다).</summary>
            private float ClampDepthY(float y, float floorY, float surfaceMargin)
            {
                float maxY = seaLevel - surfaceMargin;
                float minY = Mathf.Min(floorY, maxY);
                return Mathf.Clamp(y, minY, maxY);
            }

            private static float HorizontalSqDistance(Vector3 a, Vector3 b)
            {
                float dx = a.x - b.x;
                float dz = a.z - b.z;
                return dx * dx + dz * dz;
            }

            /// <summary>생물 중심에서 플레이어 몸통(발밑~머리 2m 선분)까지의 제곱 거리.</summary>
            private static float SqDistanceToPlayerBody(Vector3 point, Vector3 playerFeet)
            {
                float y = Mathf.Clamp(point.y, playerFeet.y, playerFeet.y + 2f);
                float dx = point.x - playerFeet.x;
                float dy = point.y - y;
                float dz = point.z - playerFeet.z;
                return dx * dx + dy * dy + dz * dz;
            }

            /// <summary>머리가 +Z인 계약이라 진행 방향을 그대로 LookRotation에 넣는다(0 벡터 방어).</summary>
            private static Quaternion LookRotation(Vector3 forward)
            {
                if (forward.sqrMagnitude < 1e-6f)
                    return Quaternion.identity;
                return Quaternion.LookRotation(forward, Vector3.up);
            }
        }
    }
}
