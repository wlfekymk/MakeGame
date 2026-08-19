using System.Collections.Generic;
using UnityEngine;
using MakeGame.Data;
using MakeGame.Player; // ProgressionTracker가 PlayerInventory/SurvivalStats를 읽는다(읽기 전용).

namespace MakeGame.Systems
{
    /// <summary>
    /// 월드 맵(섬들의 배치)을 관리한다.
    /// 0번 섬은 항상 플레이어가 불시착하는 시작 섬이며, 이후 섬들은 IslandGenerator로 규모를 정하고
    /// 이 매니저가 맵 상의 위치를 정해 계속 생성해 나간다.
    /// 아직 실제 섬 지형/에셋이 없으므로, 규모에 맞는 크기의 원기둥 플레이스홀더로 시각화한다.
    /// </summary>
    public partial class WorldMapManager : MonoBehaviour
    {
        [Tooltip("섬 규모를 결정할 때 사용하는 생성기 (같은 Managers 오브젝트의 IslandGenerator를 연결)")]
        public IslandGenerator islandGenerator;

        [Tooltip("지금까지 생성된 모든 섬 목록 (0번이 시작 섬)")]
        public List<IslandInstance> islands = new List<IslandInstance>();

        [Header("배치 설정")]
        // 퀄리티 개선: 섬 반지름을 10배로 키운 것(GetSizeScale)에 맞춰, 섬끼리 배치되는 거리도
        // 같은 비율로 키우지 않으면 훨씬 커진 섬들이 서로 파고들며 겹쳐버린다.
        [Tooltip("섬 하나가 추가될 때마다 시작 섬으로부터 멀어지는 기본 거리")]
        public float baseDistanceStep = 1200f;

        [Tooltip("배치 거리에 더해지는 무작위 편차 범위")]
        public float distanceJitter = 400f;

        [Tooltip("섬끼리 서로 겹치지 않도록 유지할 최소 간격")]
        public float minSpacingBetweenIslands = 500f;

        [Tooltip("겹치지 않는 위치를 찾기 위한 최대 시도 횟수")]
        public int maxPlacementAttempts = 20;

        [Header("시각화")]
        [Tooltip("섬을 표시할 플레이스홀더 프리팹. 비워두면 원기둥 프리미티브를 자동 생성한다.")]
        public GameObject islandPlaceholderPrefab;

        [Header("섬 콘텐츠 배치")]
        [Tooltip("섬이 생성될 때 채집 자원 노드를 함께 배치할 스포너 (비워두면 자원을 배치하지 않는다)")]
        public IslandResourceSpawner resourceSpawner;

        [Tooltip("섬이 생성될 때 위험 요소를 함께 배치할 스포너 (비워두면 위험 요소를 배치하지 않는다)")]
        public HazardSpawner hazardSpawner;

        [Tooltip("섬이 생성될 때 사냥감/물고기를 함께 배치할 스포너 (비워두면 사냥감을 배치하지 않는다).\n" +
                 "이게 없으면 생고기/생선 자체를 얻을 방법이 없어 사냥/낚시/조리 시스템이 전부 죽은 콘텐츠가 된다.")]
        public CreatureSpawner creatureSpawner;

        [Tooltip("모든 섬 생성이 끝난 뒤 섬 사이 깊은 바다에 상어를 배치할 스포너 (비워두면 상어를 배치하지 않는다).\n" +
                 "섬 위험요소와 달리 섬 하나가 아니라 전체 섬 목록을 참고해야 안전지대를 계산할 수 있으므로, 섬 생성 루프가 끝난 뒤 한 번만 호출한다.")]
        public SharkSpawner sharkSpawner;

        [Header("경비행기 수리 엔딩")]
        [Tooltip("특대(XL) 섬에 배치할 경비행기 잔해가 진행 상태를 갱신할 수리 시스템 (비워두면 잔해를 배치하지 않는다)")]
        public AircraftRepairSystem aircraftRepair;

        [Tooltip("시작 섬 중심 기준 여객기 잔해(플레이어가 타고 온 기체)의 방향 앵커. 경비행기 잔해가 특대 섬으로\n" +
                 "옮겨간 뒤에는 이 값의 수평 방향이 (1) 시작 섬 해안에 여객기 잔해를 놓을 방위, (2) 플레이어 시작\n" +
                 "회전(이 반대 방향)을 함께 결정한다. 필드 이름은 씬 직렬화 호환을 위해 그대로 둔다\n" +
                 "(RaftStructure도 같은 방향을 읽는다).")]
        public Vector3 aircraftWreckOffset = new Vector3(6f, 0f, -4f);

        [Header("시작 시선 (Design_Onboarding 2장, game-designer 요청)")]
        // 왜 회전을 코드에서 잡는가: 잔해는 런타임에 절차적으로 생성되므로 씬에서 "잔해 반대편"을
        // 손으로 맞춰 둘 수가 없다. 잔해 위치가 바뀌면 씬에 박아둔 각도는 조용히 틀린 값이 된다.
        // 같은 상수(aircraftWreckOffset)에서 두 값을 함께 유도해야 어긋나지 않는다.
        //
        // 시선 앵커는 이제 **시작 섬 해안의 여객기 잔해**다(경비행기 잔해는 특대 섬으로 이전됐다).
        // 여객기는 aircraftWreckOffset의 수평 방향 위 해변에 놓이므로, 이 방향의 반대를 보게 하면
        // "플레이어 등 뒤에 타고 온 기체가 있다" 관계가 그대로 유지된다.
        //
        // 왜 등 뒤인가(설계 근거): 타고 온 여객기 잔해는 "왜 여기 있는가"를 설명하는 배경 오브젝트다.
        // 0분에 정면으로 보이면 시선이 생존 행동(물/음식)보다 잔해 조사로 먼저 간다.
        // 등 뒤에 두면 나중에 뒤를 돌아봤을 때 발견된다 - 발견 자체를 막는 게 아니라 발견 시점을
        // 늦추는 것이 목적이다.
        [Tooltip("게임 시작 시 여객기 잔해 반대 방향을 보도록 회전시킬 플레이어 트랜스폼.\n" +
                 "비워두면 씬에서 PlayerController를 한 번 찾아 쓴다. 그래도 없으면 회전을 건드리지 않는다.")]
        public Transform playerTransform;

        [Tooltip("시작 시 플레이어를 잔해 반대 방향으로 돌려세울지 여부. 끄면 씬에 직렬화된 회전을 그대로 쓴다.")]
        public bool orientPlayerAwayFromWreck = true;

        /// <summary>시작 시선을 이미 한 번 잡았는지 여부(불러오기로 월드를 재생성해도 다시 돌리지 않기 위함).</summary>
        private bool startingFacingApplied = false;

        [Header("테스트용 자동 생성")]
        [Tooltip("플레이 시작 시 자동으로 시작 섬 + 여러 섬을 생성해서 맵을 미리 확인할 수 있게 한다.")]
        public bool generateOnStart = true;

        [Tooltip("자동 생성 시 시작 섬 외에 추가로 생성할 섬 개수")]
        public int initialIslandCount = 8;

        [Header("월드 시드")]
        [Tooltip("섬 배치/자원/위험요소 생성에 사용하는 난수 시드. 저장/불러오기 시 이 값을 기록해두면\n" +
                 "같은 섬 배치를 다시 만들어낼 수 있다. 0이면 실행할 때마다 무작위 시드를 새로 뽑는다.")]
        public int worldSeed = 0;

        // qa 결함 수정(B3-4/B3-5 전제 붕괴 원인): 섬 "크기"(IslandGenerator.GenerateNextIslandSize)와
        // "위치"(FindValidPosition)는 섬 콘텐츠 스포너(B3-3에서 이미 섬별 독립 스트림으로 격리됨)보다
        // 한 계층 위에 있는데, 여전히 전역 UnityEngine.Random을 썼다. 이전엔 여기서 Random.InitState로
        // 전역 스트림을 리셋해 "일단 재현되는 것처럼" 보였지만, 그 전역 스트림을 WeatherSystem도
        // Random.Range로 함께 소비한다(StartClearPhase/StartRainPhase). WeatherSystem은
        // [RuntimeInitializeOnLoadMethod]+sceneLoaded로 스스로 생성되므로 그 Start()가 이 섬 생성 루프
        // 앞/중간/뒤 중 어느 시점에 실행될지 Unity가 보장하지 않는다 - 최초 플레이(비동기적 초기화 순서)와
        // 불러오기(RegenerateWorld, 게임 도중 동기 호출이라 WeatherSystem 간섭이 없음) 사이에 전역
        // 스트림 소비 이력이 달라져, 같은 worldSeed인데 섬 크기/위치가 달라질 수 있었다.
        // 해결: 섬 레이아웃(크기+위치) 생성 전용 독립 System.Random 스트림을 이 매니저가 만들어(전역
        // Random과 완전히 분리) GenerateNextIsland 안에서 크기 결정 다음 위치 결정 순서로 그대로
        // 넘겨쓴다. 섬을 순서대로 하나씩 만드는 단일 루프이므로, 크기 롤 다음 위치 롤이 바로 이어지는
        // "호출 순서에 의존하는 단일 스트림"이 두 개의 독립 스트림을 따로 관리하는 것보다 자연스럽고
        // 구현도 단순하다(스트림 하나만 만들어서 그대로 두 메서드에 넘기면 됨). 이제 Random.InitState를
        // 아예 호출하지 않으므로, 이 매니저가 WeatherSystem을 비롯한 다른 시스템의 전역 Random 상태를
        // 더 이상 오염시키지 않는다(부수 효과 제거).
        private System.Random islandLayoutRng;
        private const int IslandLayoutSeedSalt = -2000000; // SharkSpawner.SharkSeedSalt(-1000000)와 겹치지 않는 별도 예약 salt.

        /// <summary>
        /// 섬 레이아웃 전용 rng를 (재)시드하고, IslandGenerator의 누적 카운터(생성 개수/대형·특대 최소
        /// 보장 카운트)도 함께 초기화한다. qa 결함 수정 겸 추가 발견: ResetGenerationState()가 지금까지
        /// 어디서도 호출되지 않아, RegenerateWorld를 두 번째로 호출하면(예: F9 불러오기를 두 번 연속
        /// 수행) IslandGenerator의 누적 카운터가 첫 생성 때 값을 그대로 들고 있어 같은 worldSeed라도
        /// 대형/특대 섬 강제 배정 시점이 최초 생성과 달라질 수 있었다 - "같은 worldSeed면 항상 같은
        /// 결과"라는 이번 수정의 전제 자체가 이 초기화 누락으로 다시 깨질 뻔했다.
        /// </summary>
        private void SeedIslandLayoutRng()
        {
            islandLayoutRng = SeededRandomExtensions.CreateForSalt(worldSeed, IslandLayoutSeedSalt);
            islandGenerator?.ResetGenerationState();
        }

        /// <summary>
        /// 섬 생성보다 먼저 실행되어야 하므로 Awake에서 worldSeed를 확정하고 섬 레이아웃 rng를 시드한다.
        /// worldSeed가 0(미지정)이면 이번 실행에 사용할 시드를 무작위로 뽑아 기록해둔다.
        /// 이후 SaveLoadController가 이 값을 저장 파일에 함께 기록해, 다음에 같은 섬 배치를 재현할 수 있게 한다.
        /// </summary>
        private void Awake()
        {
            if (worldSeed == 0)
                worldSeed = System.Environment.TickCount;

            SeedIslandLayoutRng();
        }

        /// <summary>
        /// generateOnStart가 켜져 있으면 플레이 시작과 동시에 시작 섬과 초기 섬들을 생성해
        /// 맵이 어떻게 만들어지는지 바로 확인할 수 있게 한다.
        /// </summary>
        private void Start()
        {
            CreateOcean();

            if (!generateOnStart)
                return;

            // [B52] 섬 수는 데이터가 결정한다(실측 50섬 = 시작 섬 + 49). initialIslandCount는 폴백 전용.
            int additionalIslandCount = ResolveAdditionalIslandCount();
            GenerateStartingIsland();
            for (int i = 0; i < additionalIslandCount; i++)
            {
                GenerateNextIsland(i, additionalIslandCount);
            }

            // 경비행기 잔해(수리 엔딩)는 특대 섬 위에 놓이므로 대상 섬이 생성된 뒤에 1회 배치한다.
            // 반드시 동기 호출을 유지할 것 - SaveLoadController가 같은 프레임 안에서 복원하는 계약이
            // 있으므로 코루틴/지연 호출로 미루면 안 된다. rng는 전혀 소비하지 않는다(결정적 탐색).
            SpawnAircraftWreck();

            sharkSpawner?.SpawnSharks(islands, oceanSize, seaLevel, transform, worldSeed);
        }

        /// <summary>
        /// 지정한 시드로 월드(섬/바다/자원/위험요소/사냥감/도면/작업대/잔해)를 처음부터 다시 생성한다.
        /// 저장/불러오기에서 같은 worldSeed로 같은 섬 배치를 재현하기 위해 사용한다.
        /// 예전에는 SaveLoadController.Load()가 worldSeed 필드 값만 갱신하고 실제로 월드를 다시 만들지
        /// 않아서, "월드 시드로 같은 섬 배치를 재현한다"는 설계 의도가 실제로는 전혀 동작하지 않는
        /// 죽은 기능이었다 (불러오기를 해도 이미 생성되어 있던 이전 섬 배치가 그대로 남아있었음).
        /// </summary>
        public void RegenerateWorld(int seed)
        {
            // 기존에 생성해 둔 섬/바다/자원/위험요소 등 이 매니저 아래의 모든 오브젝트를 제거한다.
            // 버그 수정(F9 불러오기 후 모든 아이템이 하늘로 떠오르는 문제): Destroy()는 "이번 프레임
            // 끝"에야 실제로 파괴되는데, 바로 아래에서 같은 프레임 안에 새 섬/자원/위험요소를 즉시
            // 다시 생성한다. 그 사이 옛 오브젝트의 콜라이더가 물리 씬에 그대로 남아있는 채로 새 노드를
            // 배치하다 보니, 새 노드 위치를 지면에 맞추는 TerrainSampler.SnapToGround의 레이가 지형이
            // 아니라 아직 안 지워진 옛 노드의 윗면에 맞아 그 위에 새 노드가 얹히고, 불러오기를 반복할수록
            // 계속 쌓여 올라갔다. SetActive(false)는 Destroy와 달리 즉시 반영되어 콜라이더가 그 순간
            // 물리 씬에서 바로 빠지므로, Destroy를 예약하기 전에 먼저 비활성화해 이 프레임 내에도 새로
            // 만드는 오브젝트가 옛 오브젝트와 물리적으로 부딪히지 않게 한다.
            // 자식 순회 중에는 SetParent(null) 등으로 부모를 바꾸지 않는다 - 역순 인덱스 순회 도중
            // transform.childCount/GetChild(i)가 가리키는 자식이 바뀌면 순회가 꼬일 수 있기 때문이다.
            // [의존 관계 주의] 이 SetActive(false)는 물리 문제뿐 아니라 SaveLoadController.RestoreResourceNodes/
            // RestoreHazardsAndCreatures가 "지금 active인 오브젝트 = 방금 새로 생성된 진짜 오브젝트, 지금
            // inactive인 오브젝트 = 파괴 예정인 옛 오브젝트"를 구분하는 근거로도 그대로 쓰인다(해당 메서드들이
            // FindObjectsInactive.Exclude로 조회하기 때문). 이 한 줄을 지우면 물리 버그가 되살아나는 것은
            // 물론, 불러오기 시 자원/위험요소/사냥감의 채집·처치·포획 진행도가 옛 오브젝트에 잘못 복원됐다가
            // 프레임 끝에 함께 사라지는 별개의 조용한 버그까지 함께 되살아난다 - 두 파일을 함께 확인할 것.
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                GameObject child = transform.GetChild(i).gameObject;
                child.SetActive(false);
                Destroy(child);
            }

            islands.Clear();

            worldSeed = seed;
            SeedIslandLayoutRng();

            CreateOcean();
            // [B52] 섬 수는 데이터가 결정한다(실측 50섬 = 시작 섬 + 49). initialIslandCount는 폴백 전용.
            int additionalIslandCount = ResolveAdditionalIslandCount();
            GenerateStartingIsland();
            for (int i = 0; i < additionalIslandCount; i++)
            {
                GenerateNextIsland(i, additionalIslandCount);
            }

            // Start()와 동일한 계약: 특대 섬 생성 완료 후 1회, 같은 프레임 안의 동기 호출로 배치한다
            // (SaveLoadController.Load가 RegenerateWorld 직후 같은 프레임에 상태를 복원한다).
            SpawnAircraftWreck();

            sharkSpawner?.SpawnSharks(islands, oceanSize, seaLevel, transform, worldSeed);
        }

        [Header("바다")]
        // 퀄리티 개선: 섬 배치 간격(baseDistanceStep 등)을 10배로 키운 것에 맞춰 바다도 같이
        // 키우지 않으면 먼 섬이 바다 평면 밖으로 나가 시각적으로 끊겨 보인다.
        [Tooltip("바다 평면의 한 변 크기. 섬들이 모두 이 범위 안에 들어올 만큼 충분히 커야 한다.")]
        public float oceanSize = 40000f;

        [Tooltip("해수면 높이. 섬 지형의 가장자리 높이(0)와 맞닿으며, PlayerController.waterLevel과 같아야 한다.")]
        public float seaLevel = 0f;

        [Tooltip("바다 표면 텍스처가 흘러가는 속도(타일/초). 값이 크면 물살이 빨라 보인다.\n" +
                 "mainTextureScale이 oceanSize/10이므로 1타일 = 월드 10미터다 - 0.02면 초당 0.2미터로 아주 완만하게 흐른다.")]
        public Vector2 oceanScrollSpeed = new Vector2(0.02f, 0.013f);

        [Tooltip("해안선 주변 얕은 물 띠의 색. Deep Ocean(#1A598C)을 밝기+청록 방향으로 민 파생색으로," +
                 "\"여기부터 물이 얕다\"를 색만으로 알려주는 용도다. 알파는 아래 그라데이션 텍스처가 담당한다.")]
        public Color shorelineBandColor = new Color(0.45f, 0.72f, 0.75f);

        [Tooltip("해안선 띠가 섬 반지름의 몇 배까지 바깥으로 퍼지는지. 1.5면 반지름 50m 섬에서 25m 폭의 띠가 생긴다.")]
        public float shorelineBandOuterScale = 1.5f;

        [Tooltip("해안선 띠를 해수면보다 얼마나 위에 띄울지(미터). 바다 평면과의 z-파이팅만 막으면 되므로 아주 작게 둔다.")]
        public float shorelineBandHeight = 0.05f;

        /// <summary>
        /// 바다 평면에 실제로 적용된 머티리얼 인스턴스. UV 스크롤(Update)에서 오프셋을 매 프레임 옮긴다.
        /// CreateOceanMaterial()이 매번 new Material()을 만들어 돌려주므로 공유 에셋이 아니고,
        /// 여기서 직접 수정해도 다른 오브젝트에 번지지 않는다.
        /// </summary>
        private Material oceanMaterial;

        /// <summary>
        /// 커스텀 바다 셰이더(MGOcean)의 파도 시간 프로퍼티 ID. 셰이더는 내장 _Time 대신 이 값을
        /// 쓰므로, C#이 넣어주는 Time.time이 곧 파도의 시계다(타이틀 화면 정지 동작의 근거).
        /// </summary>
        private static readonly int OceanWaveTimeProperty = Shader.PropertyToID("_MG_WaveTime");

        /// <summary>
        /// oceanMaterial이 _MG_WaveTime 프로퍼티를 갖는지(= 커스텀 셰이더 경로인지).
        /// CreateOceanMaterial()에서 한 번 판정해 두고, Update()가 매 프레임 HasProperty를
        /// 부르지 않게 하는 캐시다. URP Lit 폴백 경로에서는 false.
        /// </summary>
        private bool oceanHasWaveTime;

        /// <summary>
        /// 커스텀 바다 셰이더(MGOcean) 경로가 실제로 살아 있는지. CreateOceanMaterial()에서 판정한다.
        /// [바다 v3] MGOcean이 반투명 + 깊이 기반 해안 거품을 그리므로, 이 값이 true면
        /// CreateShorelineBand가 띠 생성을 건너뛴다(거품이 띠를 대체). URP Lit 폴백 경로에서는
        /// false라 기존 띠가 그대로 생긴다 - 셰이더 부재 시에도 해안 가독성이 유지되는 폴백 보존.
        /// 호출 순서 보장: Start()/RegenerateWorld() 모두 CreateOcean()을 섬 생성(→ 띠 생성)보다
        /// 먼저 부르므로, 띠 생성 시점에는 이 판정이 항상 끝나 있다.
        /// </summary>
        private bool oceanCustomShaderActive;

        /// <summary>
        /// 퀄리티 개선(바다): 수면 텍스처의 UV를 아주 느리게 흘려보내 바다에 움직임을 준다.
        /// 커스텀 셰이더(MGOcean) 경로에서는 추가로 파도 시간(_MG_WaveTime)을 매 프레임 넣는다 -
        /// 셰이더가 내장 _Time을 쓰지 않기 때문에 이 값이 없으면 파도가 아예 움직이지 않는다.
        /// 둘 다 Time.time 기반이므로 타이틀 화면(Time.timeScale = 0)에서는 UV 스크롤과 버텍스
        /// 파도가 함께 자연스럽게 멈춘다(기존 동작 유지).
        /// </summary>
        private void Update()
        {
            if (oceanMaterial == null)
                return;

            oceanMaterial.mainTextureOffset = new Vector2(
                Mathf.Repeat(oceanScrollSpeed.x * Time.time, 1f),
                Mathf.Repeat(oceanScrollSpeed.y * Time.time, 1f));

            if (oceanHasWaveTime)
                oceanMaterial.SetFloat(OceanWaveTimeProperty, Time.time);
        }

        /// <summary>
        /// 섬들을 모두 감싸는 커다란 바다 평면을 한 번 만든다. 플레이어의 수영/잠수 판정은 y 좌표만으로
        /// 이뤄지므로, 이 평면의 콜라이더는 제거해 시각적 표시 용도로만 쓴다.
        /// </summary>
        private void CreateOcean()
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Plane);
            go.name = "Ocean";
            go.transform.SetParent(transform);
            go.transform.position = new Vector3(0f, seaLevel, 0f);

            // [B10 "화면 정중앙 흰 세로선" 조치] 예전에는 내장 Plane(10×10 격자, 200삼각형)을
            // oceanSize/10 = 4000배로 스케일했다. 그 결과 삼각형 하나가 한 변 4000m·대각선 5657m가 되고,
            // 격자 정점이 4000m 간격, 즉 **정확히 월드 원점에 정점이 하나 놓였다**. 플레이어 시작 위치는
            // 씬 값으로 (0,14,0) — 즉 카메라 바로 아래 지점이 그 정점이다. 카메라가 수평을 보면 바로
            // 아래 지점의 뷰 공간 z ≈ 0 → 클립 공간 w ≈ 0 이라, 그 한 점에서 만나는 최대 28km짜리
            // 삼각형 8장이 전부 근평면(w=0) 클리핑을 거친다. 인접 삼각형의 클립 교점은 같은 두 정점에서
            // 계산되지만 순서가 다르면 마지막 ulp가 갈리고, 좌표 크기가 2만을 넘으면 그 ulp가
            // 서브픽셀 틈이 된다 - 이 틈으로 뒤의 스카이박스(수평선 부근이 가장 밝다)가 비쳐
            // **카메라 바로 아래에서 수평선까지 이어지는 얇은 흰 선**이 된다. 하늘 쪽에 없고, 수평선에서
            // 끊기고, 화면 정중앙에 오는 신고 내용이 전부 이 하나로 설명된다.
            // 조치는 두 가지이고 둘 다 이 메시 안에서 끝난다(콜라이더/레이어 로직은 손대지 않는다):
            //   (1) 격자를 64×64(칸 625m)로 잘게 나눈다. 카메라 주변 정점 좌표가 2만대 → 1천대로 줄어
            //       클립 정밀도가 약 35배 좋아지고, 칸 대각선 884m가 far clip 1000m보다 짧아
            //       "삼각형 하나가 발밑부터 수평선까지 덮는" 상황 자체가 사라진다.
            //   (2) 격자 위상을 x/z에 **서로 다른 비율**로 어긋나게 깔아, 월드 원점이 정점 위에도
            //       대각선 위에도 놓이지 않게 한다. 시작 지점과 격자의 우연한 일치를 제거하는 것이다.
            // 비용: 삼각형 200 → 8,192(섬 하나 초목 8,016과 비슷한 수준), 정점 4,225, 드로우콜은 1로 동일.
            var oceanFilter = go.GetComponent<MeshFilter>();
            if (oceanFilter != null)
                oceanFilter.sharedMesh = GenerateOceanMesh(oceanSize, OceanGridCells);
            // 메시에 실제 크기를 구웠으므로 스케일은 1이다(머티리얼 타일링은 예전 그대로 oceanSize/10).
            go.transform.localScale = Vector3.one;

            int waterLayer = LayerMask.NameToLayer("Water");
            if (waterLayer >= 0)
                go.layer = waterLayer;

            var collider = go.GetComponent<Collider>();
            if (collider != null)
                Destroy(collider);

            var renderer = go.GetComponent<MeshRenderer>();
            if (renderer != null)
            {
                oceanMaterial = CreateOceanMaterial();
                renderer.sharedMaterial = oceanMaterial;
                // 40km짜리 평면은 그림자를 드리울 대상이 아래에 없는데도 지향광 섀도맵의 캐스터 바운즈를
                // 통째로 부풀려 섬/초목 그림자 품질을 깎는다. ShorelineBand가 같은 이유로 이미 끄고 있다.
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
        }

        /// <summary>바다 평면을 나누는 한 변당 칸 수. 64칸 = oceanSize 40000 기준 한 칸 625m.</summary>
        private const int OceanGridCells = 64;

        /// <summary>
        /// 바다 평면에 사용할 머티리얼을 만든다.
        ///
        /// 퀄리티 개선(바다 v2): 커스텀 URP 셰이더(Resources/Shaders/MGOcean)를 먼저 시도한다.
        /// 버텍스 파도(진폭 합 0.24m, 시각 전용)·잔물결 노멀 2겹·프레넬 색 블렌드를 제공하며,
        /// 파도 시간은 Update()가 넣는 _MG_WaveTime으로만 흐른다(셰이더는 _Time을 안 쓴다 -
        /// 타이틀 화면 정지 유지).
        ///
        /// [바다 v3 - 반투명 전환, 사용자 요청 "물속이 보이는 바다"]: MGOcean은 이제
        /// Transparent 큐(셰이더 SubShader 태그가 지정, 머티리얼 renderQueue 추가 세팅 불필요)에
        /// Blend SrcAlpha OneMinusSrcAlpha / ZWrite Off / Cull Off로 그려지고, URP 에셋에서 켜져
        /// 있음을 확인한 _CameraDepthTexture로 물 기둥 깊이(흡수색·깊이 알파·해안 거품)를 계산한다.
        /// 알려진 부작용(의도된 트레이드오프, 셰이더 헤더와 동일 명시):
        ///   - Transparent는 메인 라이트 그림자를 받지 않는다(원래 그림자 수신이 없던 셰이더라 실질 무변화).
        ///   - 반투명끼리 정렬 문제가 생길 수 있으나 바다는 월드에 평면 1장이라 실질 무해.
        /// ShorelineBand는 셰이더의 깊이 기반 거품이 대체하므로 이 경로에서는 생성을 건너뛴다
        /// (oceanCustomShaderActive, CreateShorelineBand 참고). 폴백 경로는 띠를 그대로 만든다.
        ///
        /// 셰이더 로드가 실패하면(에셋 누락/컴파일 실패) 예전 URP Lit 경로로 그대로 폴백한다 -
        /// 어느 쪽이든 게임은 돌아가야 한다. 두 경로 모두 water.png를 [MainTexture]로 쓰고
        /// mainTextureScale = oceanSize/10, 즉 "1타일 = 월드 10미터" 계약을 지킨다.
        /// </summary>
        private Material CreateOceanMaterial()
        {
            var oceanShader = Resources.Load<Shader>("Shaders/MGOcean");
            if (oceanShader != null)
            {
                var oceanMat = new Material(oceanShader);
                // 색은 셰이더 프로퍼티 기본값(_DeepColor/_ShallowColor, 기존 0.1/0.35/0.55 근처)에
                // 맡긴다. _BaseColor는 흰색 그대로 둬야 이중으로 어두워지지 않는다.
                var customWaterTexture = Resources.Load<Texture2D>("Textures/water");
                if (customWaterTexture != null)
                {
                    // [MainTexture] _BaseMap이므로 mainTexture/mainTextureScale이 그대로 통한다.
                    oceanMat.mainTexture = customWaterTexture;
                    oceanMat.mainTextureScale = new Vector2(oceanSize / 10f, oceanSize / 10f);
                }
                oceanHasWaveTime = oceanMat.HasProperty(OceanWaveTimeProperty);
                // [바다 v3] 커스텀 셰이더 경로 확정 - 이 플래그가 ShorelineBand 생성을 건너뛰게 한다.
                // 큐/블렌드/컬링은 전부 셰이더가 스스로 선언하므로(SubShader 태그 + Pass 스테이트)
                // 여기서 renderQueue나 키워드를 만질 필요가 없다(URP Lit과 달리 키워드 분기가 없는 셰이더다).
                oceanCustomShaderActive = true;
                return oceanMat;
            }

            // ---- 폴백: 기존 URP Lit 경로(커스텀 셰이더 도입 전과 동일) ----
            oceanHasWaveTime = false;
            oceanCustomShaderActive = false; // 폴백은 불투명 Lit - ShorelineBand가 계속 해안을 표시한다.
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            var material = new Material(shader != null ? shader : Shader.Find("Standard"));
            material.color = new Color(0.1f, 0.35f, 0.55f);

            if (material.HasProperty("_Smoothness"))
                material.SetFloat("_Smoothness", 0.85f);
            if (material.HasProperty("_Metallic"))
                material.SetFloat("_Metallic", 0.15f);

            var waterTexture = Resources.Load<Texture2D>("Textures/water");
            if (waterTexture != null)
            {
                material.mainTexture = waterTexture;
                // 바다 평면이 아주 크므로(oceanSize 단위) 촘촘하게 반복시켜야 잔물결처럼 보인다.
                // 밉맵이 켜져 있어(streamingMipmaps 등) 이 정도 반복에서도 far distance 노이즈/모아레는 완화된다.
                material.mainTextureScale = new Vector2(oceanSize / 10f, oceanSize / 10f);
            }
            return material;
        }

        /// <summary>
        /// 게임 시작 시 호출한다. 0번 섬(불시착 시작 섬, 소형 고정)을 원점에 생성한다.
        /// </summary>
        public IslandInstance GenerateStartingIsland()
        {
            var startIsland = new IslandInstance
            {
                islandId = 0,
                // [B50→B52] Small 고정은 실측 데이터와도 정합한다 - MaldivesLayout.Islands[0](50섬
                // 데이터에서는 Z28-017, gameIndex 0)이 Small이다. 위치도 GetMaldivesPosition이 0번을
                // 원점으로 평행이동하므로 Vector3.zero 그대로가 곧 실측 배치다(플레이어 시작 위치/
                // 착륙 원/잔해 전제 유지).
                size = IslandSize.Small,
                mapPosition = Vector3.zero,
                isDiscovered = true,
                isStartingIsland = true,
            };

            SpawnPlaceholder(startIsland);

            // 자원 배치보다 먼저 시선을 확정한다. 착륙 원(IslandResourceSpawner)이 첫 노드를 플레이어
            // 정면에 놓으려면 "정면이 어디인지"를 배치 시점에 알고 있어야 하기 때문이다.
            ApplyStartingFacing();

            SpawnIslandContent(startIsland);
            SpawnAirlinerWreck(startIsland);
            // [뗏목 재배선] 예전에는 여기서 시작 섬 캠프에 배 작업대(BoatWorkbench)를 놓았다. 새 뗏목은
            // 해안에서 바닥판을 직접 놓는 방식이라 작업대가 없고, 뗏목 본체는 RaftStructure가 스스로
            // 씬 로드마다 만들어 해안에 자리를 잡는다(월드 생성 rng를 한 번도 소비하지 않는다).
            islands.Add(startIsland);
            return startIsland;
        }

        /// <summary>
        /// 시작 시선을 확정한다. 플레이어를 여객기 잔해(시작 섬 해안, aircraftWreckOffset 방향)의
        /// 정반대 방향으로 돌려세우고, 같은 방향을
        /// 착륙 원 스포너에도 알려 첫 자원 노드(코코넛)가 플레이어 정면에 오게 한다.
        ///
        /// 위치는 손대지 않는다 - 씬의 시작 위치(현재 y 14, 지형 기복 상향 대응으로 5에서 올라간 값)를
        /// 코드가 덮어쓰면 씬 조정이 조용히 무효가 된다. **회전만** 잡는다.
        ///
        /// 회전은 세션당 한 번만 적용한다(startingFacingApplied). RegenerateWorld(F9 불러오기)가
        /// GenerateStartingIsland를 다시 부르는데, 그때 시선을 초기값으로 되돌리면 저장해 둔 시선을
        /// 잃는다. 실제로 SaveLoadController.Load는 RegenerateWorld 직후에 저장된 회전을 다시
        /// 적용하므로 최종 결과는 어차피 세이브 값이 이기지만, 그 순서에 기대지 않고 여기서 막는다.
        /// </summary>
        private void ApplyStartingFacing()
        {
            if (!orientPlayerAwayFromWreck)
                return;

            Vector3 towardWreck = aircraftWreckOffset;
            towardWreck.y = 0f;

            // 잔해가 시작 지점 바로 위에 있으면 "등 뒤"라는 개념이 성립하지 않는다(방향 0 벡터).
            if (towardWreck.sqrMagnitude < 0.0001f)
                return;

            Vector3 facing = -towardWreck.normalized;
            float yaw = Mathf.Atan2(facing.x, facing.z) * Mathf.Rad2Deg;

            // 착륙 원 정렬은 회전 적용 여부와 무관하게 매번 갱신해 둔다(스포너가 값을 들고 있으므로
            // 재생성 시에도 같은 값이 유지되고, 같은 worldSeed면 같은 자리에 다시 배치된다).
            resourceSpawner?.SetLandingCircleFacingYaw(yaw);

            if (startingFacingApplied)
                return;

            Transform target = ResolveStartingPlayerTransform();
            if (target == null)
                return;

            target.rotation = Quaternion.Euler(0f, yaw, 0f);
            startingFacingApplied = true;
        }

        /// <summary>
        /// 시선을 잡을 플레이어 트랜스폼을 찾는다. 인스펙터 연결(playerTransform)이 있으면 그것을 쓰고,
        /// 없으면 씬에서 PlayerController를 한 번만 찾아 캐시한다. 둘 다 없으면 null이고, 이때는
        /// 회전을 건드리지 않아 씬에 직렬화된 값이 그대로 남는다(기능이 꺼진 것과 같다).
        /// </summary>
        private Transform ResolveStartingPlayerTransform()
        {
            if (playerTransform != null)
                return playerTransform;

            var controller = FindAnyObjectByType<PlayerController>();
            if (controller != null)
                playerTransform = controller.transform;

            return playerTransform;
        }

        /// <summary>
        /// 특대(XL) 섬에 경비행기 잔해를 한 번 배치한다(수리 엔딩의 원정 목표 - 예전에는 시작 섬 중심
        /// 근처였다). 프리미티브를 조합해 부서진 기체 형태를 만들고,
        /// AircraftWreck 컴포넌트를 붙여 상호작용(E키)으로 수리 재료를 투입할 수 있게 한다.
        ///
        /// 호출 시점: 모든 섬 생성이 끝난 뒤 Start()/RegenerateWorld()가 각각 1회 호출한다(같은 프레임
        /// 안의 동기 흐름 - SaveLoadController 복원 계약). 대상 섬의 지형 콜라이더는 SpawnPlaceholder의
        /// Physics.SyncTransforms로 이미 물리 씬에 반영돼 있어 SnapToGround가 바로 맞는다.
        /// 난수 규율: 섬 선택/위치 탐색 전부 결정적이다 - islandLayoutRng를 비롯한 어떤 rng도 소비하지
        /// 않으므로 같은 worldSeed = 같은 월드 계약이 한 칸도 밀리지 않는다.
        /// </summary>
        private void SpawnAircraftWreck()
        {
            if (aircraftRepair == null)
                return;

            IslandInstance targetIsland = FindAircraftWreckIsland();
            if (targetIsland == null)
                return;

            Vector3 position = FindAircraftWreckPosition(targetIsland);

            var go = new GameObject("AircraftWreck");
            go.transform.SetParent(transform);
            go.transform.position = position;

            // 동체(원기둥, 옆으로 눕힘) + 부러진 날개(양옆 납작한 큐브) + 꼬리날개(세로 큐브)로 부서진 기체 형태를 표현한다.
            StructureVisualBuilder.CreateVisualPart(go.transform, "Fuselage", PrimitiveType.Cylinder,
                Vector3.up * 0.6f, new Vector3(0.8f, 2.2f, 0.8f), new Color(0.55f, 0.58f, 0.6f),
                Quaternion.Euler(0f, 0f, 90f));
            StructureVisualBuilder.CreateVisualPart(go.transform, "WingLeft", PrimitiveType.Cube,
                new Vector3(-1.6f, 0.6f, 0.3f), new Vector3(2.2f, 0.15f, 0.8f), new Color(0.45f, 0.48f, 0.5f),
                Quaternion.Euler(0f, 0f, 15f));
            StructureVisualBuilder.CreateVisualPart(go.transform, "WingRight", PrimitiveType.Cube,
                new Vector3(1.8f, 0.5f, -0.4f), new Vector3(1.8f, 0.15f, 0.8f), new Color(0.45f, 0.48f, 0.5f),
                Quaternion.Euler(0f, 0f, -25f));
            StructureVisualBuilder.CreateVisualPart(go.transform, "TailFin", PrimitiveType.Cube,
                new Vector3(-2.2f, 1f, 0f), new Vector3(0.15f, 1f, 0.6f), new Color(0.6f, 0.25f, 0.2f));

            // 상호작용 레이캐스트가 맞을 수 있도록 전체를 감싸는 박스 콜라이더를 추가한다.
            var boxCollider = go.AddComponent<BoxCollider>();
            boxCollider.center = new Vector3(0f, 0.6f, 0f);
            boxCollider.size = new Vector3(4.5f, 1.6f, 2f);

            var wreck = go.AddComponent<AircraftWreck>();
            wreck.repairSystem = aircraftRepair;
        }

        /// <summary>
        /// 경비행기 잔해를 놓을 섬을 고른다. 실측 배치(MaldivesLayout)가 켜져 있으면 데이터에서
        /// size == ExtraLarge인 항목의 배열 인덱스(= gameIndex = islandId, 50섬 데이터에서는 Z28-043
        /// 하나뿐)를 찾아 그 섬을 쓴다. 실측 배치가 꺼진 폴백 월드에서는 생성된 섬 중 가장 큰 등급의
        /// 첫 섬을 고른다(시작 섬은 캠프이므로 가능하면 제외, 다른 섬이 하나도 없으면 시작 섬).
        /// 어느 경로든 rng를 소비하지 않는 결정적 선택이다.
        /// </summary>
        private IslandInstance FindAircraftWreckIsland()
        {
            if (IsMaldivesLayoutActive())
            {
                var data = MaldivesLayout.Islands;
                for (int i = 0; i < data.Length; i++)
                {
                    if (data[i].size != IslandSize.ExtraLarge)
                        continue;

                    var island = GetIsland(i);
                    if (island != null)
                        return island;
                }
            }

            // 폴백: 생성 순서대로 훑으며 가장 큰 등급이 처음 나온 섬(결정적 - 동률이면 먼저 생성된 쪽).
            IslandInstance best = null;
            foreach (var island in islands)
            {
                if (island == null || island.isStartingIsland)
                    continue;
                if (best == null || island.size > best.size)
                    best = island;
            }
            if (best == null && islands.Count > 0)
                best = islands[0];
            return best;
        }

        /// <summary>
        /// 섬 중심 기준 "뭍 위" 배치 지점을 결정적으로 찾는다. 후보는 반지름 비율 4단계 × 방위 8개의
        /// 고정 순서이고, 각 후보를 TerrainSampler.SnapToGround로 지면에 스냅해 해수면(seaLevel)보다
        /// 충분히(+0.5m 이상) 높은 첫 지점을 쓴다. 전부 실패하면 섬 중심.
        /// 특대 섬의 실측 윤곽(radialMask)은 방향에 따라 0.08R까지 좁아지므로 고정 오프셋 하나로는
        /// 물 위에 뜰 수 있어 이런 탐색이 필요하다. rng는 전혀 소비하지 않는다(같은 시드 = 같은 위치).
        /// </summary>
        private Vector3 FindAircraftWreckPosition(IslandInstance island)
        {
            Vector3 center = island.mapPosition;
            float terrainRadius = IslandSizeMetrics.GetTerrainRadius(island.size);
            // 안쪽부터 바깥으로 시도한다(중심에 가까울수록 물에 잠길 확률이 낮다). 0.1은 마지막 보루.
            float[] radiusFractions = { 0.2f, 0.35f, 0.5f, 0.1f };
            const int bearingCount = 8;
            const float minHeightAboveSea = 0.5f;

            foreach (float fraction in radiusFractions)
            {
                for (int bearing = 0; bearing < bearingCount; bearing++)
                {
                    float angle = bearing * (Mathf.PI * 2f / bearingCount);
                    Vector3 candidate = center + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle))
                        * (terrainRadius * fraction);
                    Vector3 snapped = TerrainSampler.SnapToGround(candidate);

                    // SnapToGround는 지형을 못 맞히면 입력 y(= 0 = 해수면)를 그대로 돌려주므로
                    // "물 밖에 있고 레이도 실제 지형에 맞았다"가 이 한 검사로 함께 보장된다.
                    if (snapped.y >= seaLevel + minHeightAboveSea)
                        return snapped;
                }
            }

            return TerrainSampler.SnapToGround(center);
        }

        /// <summary>
        /// 시작 섬 해안에 플레이어가 타고 온 여객기 잔해를 한 번 배치한다(시각+콜라이더 전용 배경
        /// 오브젝트 - 형태와 콜라이더는 AirlinerWreck 컴포넌트가 스스로 만들므로 여기서는 루트만 세운다).
        /// 경비행기 잔해(수리 엔딩)가 특대 섬으로 옮겨간 뒤, "왜 여기 있는가"를 설명하는 역할과
        /// ApplyStartingFacing의 "등 뒤" 앵커 역할을 이 잔해가 물려받는다.
        ///
        /// 방향: aircraftWreckOffset의 수평 방향(정규화)을 그대로 쓴다 - 시작 시선이 이 반대 방향이므로
        /// 여객기는 항상 플레이어 등 뒤 해안에 놓인다. 거리: 섬 중심에서 그 방향으로 8m부터 4m 간격으로
        /// 바깥으로 걸어가며 SnapToGround 높이가 해수면+0.2~+1.2m에 들어오는 첫 "해변" 지점을 찾는다
        /// (못 찾으면 마지막으로 뭍이었던 지점). 회전: 기수(+Z)가 바다 쪽에서 섬 안쪽을 향하도록(탐색
        /// 방향의 반대) yaw만 준다. 전 과정이 결정적이라 rng를 전혀 소비하지 않고, 생명주기는 기존
        /// 잔해 배치와 같다(신규 게임/RegenerateWorld 각각 정확히 1회 - GenerateStartingIsland에서 호출,
        /// RegenerateWorld는 자식을 전부 지우고 다시 만들므로 중복이 생기지 않는다).
        /// </summary>
        private void SpawnAirlinerWreck(IslandInstance startIsland)
        {
            Vector3 direction = aircraftWreckOffset;
            direction.y = 0f;
            direction = direction.sqrMagnitude < 0.0001f ? Vector3.forward : direction.normalized;

            Vector3 center = startIsland.mapPosition;
            float maxDistance = IslandSizeMetrics.GetTerrainRadius(startIsland.size);

            bool foundBeach = false;
            bool foundLand = false;
            Vector3 beach = Vector3.zero;
            Vector3 lastLand = Vector3.zero;

            for (float distance = 8f; distance <= maxDistance; distance += 4f)
            {
                Vector3 snapped = TerrainSampler.SnapToGround(center + direction * distance);
                float heightAboveSea = snapped.y - seaLevel;

                // SnapToGround가 지형을 못 맞히면 입력 y(= 0 = 해수면)가 그대로 돌아와 뭍 판정에서 걸러진다.
                if (heightAboveSea > 0.01f)
                {
                    lastLand = snapped;
                    foundLand = true;
                }

                if (heightAboveSea >= 0.2f && heightAboveSea <= 1.2f)
                {
                    beach = snapped;
                    foundBeach = true;
                    break;
                }
            }

            if (!foundBeach)
                beach = foundLand ? lastLand : TerrainSampler.SnapToGround(center + direction * 8f);

            // 기수(+Z 모델 기준)가 바다 쪽에서 섬 안쪽을 향하게 = 해안 탐색(바깥) 방향의 반대.
            Vector3 nose = -direction;
            float yaw = Mathf.Atan2(nose.x, nose.z) * Mathf.Rad2Deg;

            var go = new GameObject("AirlinerWreck");
            go.transform.SetParent(transform);
            go.transform.position = beach; // 루트 y = 해변 지면 높이(착지).
            go.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            go.AddComponent<AirlinerWreck>(); // 시각/콜라이더는 이 컴포넌트가 만든다(매개변수 없음).

            // [불시착 현장] 잔해 배치(위 해변 탐색)가 확정된 뒤의 순수 후처리 - 트렌치(끌린 고랑)/
            // 둔덕/초목 제거/흙색/잔해 재착지. rng 소비 0(전부 위치 해시)이라 배치 재현성이 불변이고,
            // RegenerateWorld 경로에서도 이 메서드가 다시 불리므로(GenerateStartingIsland) 자동 적용된다.
            CrashSiteSculptor.Apply(go.transform, startIsland);
        }

        /// <summary>
        /// 새 섬을 하나 생성한다. IslandGenerator로 규모를 정하고, 기존 섬과 겹치지 않는 위치를 찾아 배치한다.
        /// </summary>
        /// <param name="islandIndex">초기 생성 순서에서 이 섬이 몇 번째(0부터)인지. 대형/특대 섬 최소 보장 판단에 쓰인다.</param>
        /// <param name="totalIslandCount">이번 초기 생성에서 만들 전체 섬 개수.</param>
        public IslandInstance GenerateNextIsland(int islandIndex = 0, int totalIslandCount = 1)
        {
            // [B50] 시작 섬 포함 순번 = MaldivesLayout의 gameIndex. 실측 배치가 켜져 있고 데이터 범위
            // 안이면 크기 등급(실측 면적 순위)과 위치(실측 상대 배치 x 배율)를 데이터에서 읽는다.
            // 이 경로는 islandLayoutRng를 소비하지 않는다 - 전용 격리 스트림이라 안전(위 [B50] 블록 주석).
            // 폴백(데이터 없음/개수 불일치/범위 밖)은 기존 랜덤 경로 그대로다.
            int gameIndex = islands.Count;
            bool useMaldives = IsMaldivesLayoutActive()
                && gameIndex > 0 && gameIndex < MaldivesLayout.Islands.Length;

            IslandSize size;
            Vector3 position;
            if (useMaldives)
            {
                size = MaldivesLayout.Islands[gameIndex].size;
                position = GetMaldivesPosition(gameIndex);
            }
            else
            {
                size = islandGenerator != null
                    ? islandGenerator.GenerateNextIslandSize(islandIndex, totalIslandCount, islandLayoutRng)
                    : IslandSize.Small;
                position = FindValidPosition(islandLayoutRng);
            }

            var newIsland = new IslandInstance
            {
                islandId = gameIndex,
                size = size,
                mapPosition = position,
                isDiscovered = false,
                isStartingIsland = false,
            };

            SpawnPlaceholder(newIsland);
            SpawnIslandContent(newIsland);
            islands.Add(newIsland);
            return newIsland;
        }

        /// <summary>
        /// 섬이 생성된 직후, 연결된 스포너가 있다면 채집 자원, 위험 요소, 사냥감을 함께 배치한다.
        /// B3-3: 각 스포너가 이 섬 전용 결정적 System.Random 스트림을 만들 수 있도록 worldSeed를 함께
        /// 넘긴다(스포너 내부에서 island.islandId와 조합해 시드를 만든다 - SeededRandomExtensions 참고).
        /// </summary>
        private void SpawnIslandContent(IslandInstance island)
        {
            resourceSpawner?.SpawnResourcesForIsland(island, transform, worldSeed);
            hazardSpawner?.SpawnHazardsForIsland(island, transform, worldSeed);
            creatureSpawner?.SpawnCreaturesForIsland(island, transform, worldSeed);
        }

        /// <summary>
        /// 기존에 생성된 섬들과 최소 간격 이상 떨어진 새 위치를 찾는다.
        /// 시작 섬으로부터의 거리는 생성된 섬 개수에 비례해 점점 멀어진다 (섬이 늘어날수록 더 먼 바다로 확장).
        /// 정해진 횟수 안에 조건을 만족하는 위치를 못 찾으면 마지막 후보 위치를 그대로 반환한다.
        /// qa 결함 수정: 시드 없는 전역 UnityEngine.Random 대신, 호출자(GenerateNextIsland)가 넘겨주는
        /// 섬 레이아웃 전용 결정적 rng를 쓴다(WorldMapManager.islandLayoutRng 주석 참고) - 각도/거리
        /// 재시도 루프까지 포함해 이 섬 하나를 배치하는 데 쓰이는 모든 난수 호출이 이 rng 하나로 통일된다.
        /// </summary>
        private Vector3 FindValidPosition(System.Random rng)
        {
            Vector3 candidate = Vector3.zero;

            for (int attempt = 0; attempt < maxPlacementAttempts; attempt++)
            {
                float angle = rng.NextFloat(0f, 360f) * Mathf.Deg2Rad;
                float distance = baseDistanceStep * islands.Count + rng.NextFloat(-distanceJitter, distanceJitter);
                distance = Mathf.Max(distance, baseDistanceStep * 0.5f);

                candidate = new Vector3(Mathf.Cos(angle) * distance, 0f, Mathf.Sin(angle) * distance);

                if (IsFarEnoughFromAllIslands(candidate))
                    return candidate;
            }

            // 조건을 만족하는 위치를 못 찾았어도 마지막 후보를 사용한다 (완전히 막히지 않도록).
            return candidate;
        }

        /// <summary>
        /// 지정한 위치가 기존의 모든 섬과 최소 간격 이상 떨어져 있는지 확인한다.
        /// </summary>
        private bool IsFarEnoughFromAllIslands(Vector3 position)
        {
            foreach (var island in islands)
            {
                if (Vector3.Distance(position, island.mapPosition) < minSpacingBetweenIslands)
                    return false;
            }
            return true;
        }

        // ─────────────────────────────────────────────────────────────────────────
        // [B50 몰디브 실측 배치] MaldivesLayout(몰디브 Z28 환초 상위 9섬, Tools/terrain/maldives_extract가
        // 재생성하는 데이터)을 소비해 9섬의 위치·크기 등급·윤곽(radialMask)을 실측값으로 바꾼다.
        //
        // 폴백 규약([B52] 개정): 데이터가 없거나 항목이 2개 미만이면 **기존 랜덤 배치(IslandGenerator
        // 롤 + FindValidPosition, 섬 수는 씬의 initialIslandCount)로 통째로 되돌아간다.** 데이터가
        // 있으면 데이터 길이가 곧 섬 수다(9섬 시절의 "씬 개수와 일치해야 활성" 조건은 폐기). 이 프로젝트의
        // 관례다(spawnConfig 누락 폴백, LegacyNoiseSeed 회귀 경로와 같은 계열). 마스크 주입도 같은
        // 조건으로 함께 꺼져서 "배치는 랜덤인데 윤곽만 실측"인 반쪽 상태가 생기지 않는다.
        //
        // 난수: 실측 경로는 islandLayoutRng를 한 번도 소비하지 않는다. 안전한 이유 - 이 rng는 섬 크기
        // 롤 + FindValidPosition **전용 격리 스트림**이다(salt -2000000, SeedIslandLayoutRng 주석 참고).
        // 초목(3000000+)·상어(-1000000)·스포너(islandId별 CreateForIsland)는 전부 별도 스트림이라
        // 소비량이 줄어도 다른 시스템의 배치가 한 칸도 밀리지 않는다.
        // ─────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// 실측 좌표에 곱하는 전체 균등 배율.
        /// [B52] 4 → **1**. 9섬 시절의 x4는 "실측 x0.06998 축소 좌표(span 1.6×2.6km, 최근접 89m)"를
        /// 게임 스케일로 되살리는 배율이었다. 50섬 재생성 데이터는 **이미 게임 미터**다(파일 머리 주석과
        /// 실측 검증: 최근접 786m, span 27.0×27.7km, 시작 섬 기준 최원거리 35.5km). 여기 x4를 그대로
        /// 두면 좌표 절댓값이 56km까지 벌어져 **oceanSize 40000의 반폭 20km 밖**이 된다(바다 평면 밖
        /// 허공에 섬이 뜬다). x1 검증 수치:
        ///   · 최근접 786m ≥ 최악 지형 반지름 합 340m(ExtraLarge 200 + Large 140) - 메시 겹침 없음.
        ///     (얕은 물 띠 1.5R 최악 510m까지 봐도 여유가 남는다.)
        ///   · 좌표 절댓값 최대 14.0km < 20km(바다 안). 시작 섬은 남서쪽 구석(-12300, -12300)이고
        ///     반경 4km 안은 소형 섬 3개뿐이다 - 초반에 큰 섬을 만날 수 없다는 뜻이라 진행 곡선이 걸려 있다.
        /// </summary>
        private const float MaldivesLayoutScale = 1f;

        /// <summary>
        /// 시작 섬(0번) 실측 윤곽의 마스크 조회를 앞당기는 샘플 수(64샘플 기준 16 = 90도. 윤곽이
        /// 시계 방향으로 90도 돈 것과 같다). **모양은 그대로고 방향만 돈다.**
        /// 왜 필요한가 - 시작 섬에는 방위가 코드에 고정된 콘텐츠가 있다:
        /// 여객기 잔해 방향(aircraftWreckOffset +6,-4 = 326도, 예전에는 경비행기 잔해 자리),
        /// 착륙 원(잔해 반대 = 146도, 반지름 최대 9m), 배 작업대(-6,-3 = 207도).
        /// 9섬 시절 마스크(Z28-056)는 착륙 원 방향 해안이 0.15R = 7.5m라 첫 자원 노드(5.6~9m)가
        /// 물에 잠겨 이 회전이 필수였다.
        /// [B52] 50섬 데이터의 시작 섬은 Z28-017로 바뀌었다. 새 마스크 검산: 회전 16 기준 해안 거리
        /// 착륙 원 34m / 작업대 44m / 잔해 30m(회전 0이어도 46m 이상)로 세 방향 모두 육지다.
        /// 회전을 유지하는 이유: 값을 0으로 바꾸면 이미 검증된 경로를 이유 없이 흔드는 것뿐이고,
        /// 어느 값이든 안전이 수치로 확인됐다.
        /// </summary>
        private const int StartingIslandMaskRotationSamples = 16;

        /// <summary>
        /// 실측 배치를 쓸 수 있는 상태인지. 데이터가 없거나 항목이 2개 미만(시작 섬 + 최소 1섬)이면
        /// false(랜덤 배치 폴백).
        /// [B52] 예전에는 `Length == initialIslandCount + 1`(씬 값 8 → 9섬)이었는데, 데이터가 50섬으로
        /// 재생성되면서 **데이터 길이가 곧 섬 수**가 되도록 뒤집었다 - 씬의 initialIslandCount(8)는
        /// 이제 폴백(랜덤 배치) 전용이다(ResolveAdditionalIslandCount 참고). 씬을 고칠 수 없는
        /// 환경이므로 개수 결정권을 코드(데이터)로 옮기는 것이 유일한 경로다.
        /// </summary>
        private bool IsMaldivesLayoutActive()
        {
            var data = MaldivesLayout.Islands;
            return data != null && data.Length >= 2;
        }

        /// <summary>
        /// [B52] 시작 섬 **외에** 추가로 생성할 섬 개수. 실측 배치가 켜져 있으면 데이터 길이 - 1
        /// (시작 섬은 데이터 0번이 GenerateStartingIsland에서 따로 만들어진다), 폴백이면 씬의
        /// initialIslandCount다. Start()와 RegenerateWorld()가 반드시 같은 값을 쓰도록 한 곳에 모았다 -
        /// 두 경로의 섬 수가 다르면 세이브의 discoveredIslandIds/currentIslandId가 존재하지 않는 섬을
        /// 가리키게 된다.
        /// </summary>
        private int ResolveAdditionalIslandCount()
        {
            return IsMaldivesLayoutActive() ? MaldivesLayout.Islands.Length - 1 : initialIslandCount;
        }

        /// <summary>
        /// gameIndex(=islandId)번 섬의 실측 배치 위치. 데이터 좌표계를 **0번 섬(시작 섬)이 원점에 오도록
        /// 평행이동**한 뒤 MaldivesLayoutScale을 곱한다 - 플레이어 시작 위치(씬 (0,14,0))와 착륙 원·잔해
        /// 배치가 전부 "시작 섬 중심 = 월드 원점"을 전제하므로 상대 배치만 실측을 따르고 원점은 유지한다.
        /// </summary>
        private Vector3 GetMaldivesPosition(int gameIndex)
        {
            var data = MaldivesLayout.Islands;
            MaldivesLayout.Entry origin = data[0];
            MaldivesLayout.Entry entry = data[gameIndex];
            return new Vector3(
                (entry.posX - origin.posX) * MaldivesLayoutScale,
                0f,
                (entry.posZ - origin.posZ) * MaldivesLayoutScale);
        }

        /// <summary>
        /// islandId번 섬에 주입할 실측 radialMask. 실측 배치가 꺼져 있으면 null(하모닉 마스크 경로).
        /// 규약(IslandShapeProfile.radialMask): 0번 = +X축, 반시계 등간격, 하한 0.15 클램프와 64샘플
        /// 선형 보간은 소비 측(IslandMeshGenerator.MaskAt)이 처리한다.
        /// 시작 섬(0번)만 StartingIslandMaskRotationSamples만큼 돌린 사본을 준다(위 상수 주석 참고).
        /// </summary>
        private float[] GetMaldivesRadialMask(int islandId)
        {
            if (!IsMaldivesLayoutActive())
                return null;

            var data = MaldivesLayout.Islands;
            if (islandId < 0 || islandId >= data.Length)
                return null;

            float[] mask = data[islandId].mask;
            if (mask == null || mask.Length < 3)
                return null;

            if (islandId != 0)
                return mask;

            var rotated = new float[mask.Length];
            for (int i = 0; i < mask.Length; i++)
                rotated[i] = mask[(i + StartingIslandMaskRotationSamples) % mask.Length];
            return rotated;
        }

        [Header("섬 지형 생성")]
        [Tooltip("절차적 섬 지형(언덕 메시)의 중심부 최대 높이. 걸어서 오르기 편하도록 섬 규모와 무관하게 완만한 값으로 고정한다.")]
        public float terrainMaxHeight = 2.5f;

        [Tooltip("섬 지형 메시에 사용할 머티리얼 (비워두면 기본 URP Lit 모래색 머티리얼을 사용한다)")]
        public Material terrainMaterial;

        /// <summary>
        /// 섬 규모에 맞는 크기의 지형 오브젝트를 생성해 배치한다.
        /// islandPlaceholderPrefab이 지정돼 있으면 그것을 사용하고, 없으면 걸어다닐 수 있는 절차적 언덕 메시(IslandMeshGenerator)를 만든다.
        /// </summary>
        private void SpawnPlaceholder(IslandInstance island)
        {
            GameObject placeholder;

            if (islandPlaceholderPrefab != null)
            {
                placeholder = Instantiate(islandPlaceholderPrefab, island.mapPosition, Quaternion.identity, transform);
            }
            else
            {
                float radius = GetSizeScale(island.size);
                placeholder = CreateProceduralIslandTerrain(radius, island.mapPosition, island.islandId);
            }

            placeholder.name = $"Island_{island.islandId}_{island.size}";
            island.placeholderObject = placeholder;

            // [B7 디렉터] 지면 구분(해안 모래 / 내륙 풀밭)과 초목 배치. tech-artist가 처음에는 편집 권한
            // 때문에 HazardSpawner.SpawnHazardsForIsland에 얹었는데, 초목은 위험 요소와 아무 관계가 없고
            // 시작 섬은 위험 요소를 면제받으므로 그 자리에 두면 조기 반환에 걸리기 쉬웠다. 섬 지오메트리를
            // 만드는 진짜 주인인 여기로 옮겼다.
            // 시드: 초목 전용 salt 대역(3000000+)을 쓴다. 위험 요소/자원 스트림과 절대 공유하면 안 된다 -
            // 공유하면 초목 개수를 바꿀 때마다 자원·위험요소 배치가 통째로 밀려 세이브 복원 키가 어긋난다.
            // Physics.autoSyncTransforms는 기본 false다. 방금 만들어 위치를 잡은 MeshCollider는
            // 아직 물리 씬에 반영되지 않았을 수 있고, 그 상태로 TerrainSampler.SnapToGround가 레이를
            // 쏘면 지형을 못 맞혀 초목이 전부 y=0(해수면)에 깔린다. 명시적으로 한 번 동기화한다.
            // [B52] 월드당 50회로 늘었지만 여전히 시작/불러오기 1프레임 안의 일회성 비용이다(호출당
            // 수 ms 미만, 총 수십 ms 수준). 이 호출을 루프 밖으로 빼면 안 된다 - 각 섬의 초목 배치
            // 레이가 "방금 만든 그 섬"의 콜라이더를 맞혀야 하기 때문이다.
            Physics.SyncTransforms();

            IslandMeshGenerator.BuildIslandSurface(
                placeholder,
                IslandSizeMetrics.GetTerrainRadius(island.size),
                SeededRandomExtensions.CreateForSalt(worldSeed, VegetationSeedSalt + island.islandId));
        }

        /// <summary>초목 배치 전용 난수 salt 대역. 자원/위험요소/섬 레이아웃 스트림과 분리돼 있다.</summary>
        private const int VegetationSeedSalt = 3000000;

        /// <summary>
        /// 섬 규모에 대응하는 시각적 크기 배율(=지형 반지름, 미터)을 반환한다.
        /// 사용자 피드백: 기존 값(5/9/14/20)이 실제로 걸어보니 너무 작아서 답답하다는 지적을 받아,
        /// Small 기준 약 10배(5→50)로 키우고 나머지 등급도 같은 비율로 함께 키워
        /// Small<Medium<Large<ExtraLarge 상대적 크기 순서와 비율은 그대로 유지했다.
        /// 이 값을 바꾸면 섬끼리 겹치지 않기 위한 배치 간격(baseDistanceStep 등)과 바다 크기(oceanSize),
        /// 자원/위험요소/사냥감 산포 반경(scatterRadius)도 함께 비례해서 커져야 한다 - 그렇지 않으면
        /// 훨씬 커진 섬의 극히 일부 구역에만 콘텐츠가 몰리게 된다.
        /// 리팩터링(#2): 예전에는 이 반지름 값(50/90/140/200)을 IslandResourceSpawner/HazardSpawner/
        /// CreatureSpawner가 각자 산포 반경으로 다시 하드코딩해 총 네 곳에 같은 숫자가 흩어져 있었다.
        /// IslandSizeMetrics.GetTerrainRadius를 단일 소스로 삼아 위임하도록 바꿨다(반환값은 기존과 동일).
        /// </summary>
        private float GetSizeScale(IslandSize size)
        {
            return IslandSizeMetrics.GetTerrainRadius(size);
        }

        /// <summary>
        /// 섬 번호로 섬 인스턴스를 찾는다. 없으면 null을 반환한다.
        /// </summary>
        public IslandInstance GetIsland(int islandId)
        {
            foreach (var island in islands)
            {
                if (island.islandId == islandId)
                    return island;
            }
            return null;
        }

        /// <summary>
        /// 지정한 섬을 발견 상태로 표시한다 (플레이어가 시야 확보 또는 도착했을 때 호출).
        /// </summary>
        public void DiscoverIsland(int islandId)
        {
            var island = GetIsland(islandId);
            if (island != null)
                island.isDiscovered = true;
        }
    }

    /// <summary>
    /// Docs/Design_Progression.md 3장의 5단계 목표 체인.
    /// 값은 문서의 단계 번호와 그대로 일치시킨다(1~5) - UI에서 "N/5" 같은 표기를 하려면 (int)로 캐스팅한다.
    /// </summary>
    public enum ProgressionStage
    {
        /// <summary>1단계 - 생존 확보(0~10분). 물/음식을 먼저 찾게 만드는 구간.</summary>
        Survival = 1,

        /// <summary>2단계 - 도구 확보(10~25분). 제작(V)과 손도끼/창을 알려주는 구간.</summary>
        Tools = 2,

        /// <summary>3단계 - 탐험(25~60분). 지도(M)와 대형 섬 이동을 알려주는 구간.</summary>
        Exploration = 3,

        /// <summary>4단계 - 탈출 준비(60~150분). 배/경비행기 두 갈래가 처음으로 명시되는 구간.</summary>
        EscapePreparation = 4,

        /// <summary>5단계 - 탈출. 한쪽 경로가 완성된 뒤(Docs/Design_Ending.md).</summary>
        Escape = 5,
    }

}
