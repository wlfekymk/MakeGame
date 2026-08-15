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
    public class WorldMapManager : MonoBehaviour
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

        [Tooltip("섬이 생성될 때 배 도면 습득 지점을 함께 배치할 스포너 (비워두면 도면을 배치하지 않는다)")]
        public BoatBlueprintSpawner blueprintSpawner;

        [Tooltip("섬이 생성될 때 사냥감/물고기를 함께 배치할 스포너 (비워두면 사냥감을 배치하지 않는다).\n" +
                 "이게 없으면 생고기/생선 자체를 얻을 방법이 없어 사냥/낚시/조리 시스템이 전부 죽은 콘텐츠가 된다.")]
        public CreatureSpawner creatureSpawner;

        [Tooltip("모든 섬 생성이 끝난 뒤 섬 사이 깊은 바다에 상어를 배치할 스포너 (비워두면 상어를 배치하지 않는다).\n" +
                 "섬 위험요소와 달리 섬 하나가 아니라 전체 섬 목록을 참고해야 안전지대를 계산할 수 있으므로, 섬 생성 루프가 끝난 뒤 한 번만 호출한다.")]
        public SharkSpawner sharkSpawner;

        [Header("경비행기 수리 엔딩")]
        [Tooltip("시작 섬에 배치할 경비행기 잔해가 진행 상태를 갱신할 수리 시스템 (비워두면 잔해를 배치하지 않는다)")]
        public AircraftRepairSystem aircraftRepair;

        [Tooltip("시작 섬 중심을 기준으로 경비행기 잔해를 놓을 위치(오프셋). 플레이어 시작 회전을 이 반대\n" +
                 "방향으로 잡는 계산에도 같은 값을 쓰므로, 잔해를 옮기면 시작 시선도 자동으로 따라온다.")]
        public Vector3 aircraftWreckOffset = new Vector3(6f, 0f, -4f);

        [Header("시작 시선 (Design_Onboarding 2장, game-designer 요청)")]
        // 왜 회전을 코드에서 잡는가: 잔해는 런타임에 절차적으로 생성되므로 씬에서 "잔해 반대편"을
        // 손으로 맞춰 둘 수가 없다. 잔해 위치가 바뀌면 씬에 박아둔 각도는 조용히 틀린 값이 된다.
        // 같은 상수(aircraftWreckOffset)에서 두 값을 함께 유도해야 어긋나지 않는다.
        //
        // 왜 등 뒤인가(설계 근거): 경비행기 잔해는 재료 15개를 요구하는 엔드게임 오브젝트다. 0분에
        // 정면으로 보이면 플레이어는 그것부터 조사하러 가고, "아직 아무것도 못 한다"는 좌절만 얻는다.
        // 등 뒤에 두면 나중에 뒤를 돌아봤을 때 발견된다 - 발견 자체를 막는 게 아니라 발견 시점을
        // 늦추는 것이 목적이다.
        [Tooltip("게임 시작 시 경비행기 잔해 반대 방향을 보도록 회전시킬 플레이어 트랜스폼.\n" +
                 "비워두면 씬에서 PlayerController를 한 번 찾아 쓴다. 그래도 없으면 회전을 건드리지 않는다.")]
        public Transform playerTransform;

        [Tooltip("시작 시 플레이어를 잔해 반대 방향으로 돌려세울지 여부. 끄면 씬에 직렬화된 회전을 그대로 쓴다.")]
        public bool orientPlayerAwayFromWreck = true;

        /// <summary>시작 시선을 이미 한 번 잡았는지 여부(불러오기로 월드를 재생성해도 다시 돌리지 않기 위함).</summary>
        private bool startingFacingApplied = false;

        [Header("배 제작 엔딩")]
        [Tooltip("시작 섬에 배치할 배 작업대가 진행 상태를 갱신할 배 제작 시스템 (비워두면 작업대를 배치하지 않는다).\n" +
                 "작업대가 없으면 도면과 재료를 다 모아도 실제로 배를 조립할 방법이 없으므로 반드시 연결해야 한다.")]
        public BoatConstructionSystem boatConstruction;

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

            GenerateStartingIsland();
            for (int i = 0; i < initialIslandCount; i++)
            {
                GenerateNextIsland(i, initialIslandCount);
            }

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
            GenerateStartingIsland();
            for (int i = 0; i < initialIslandCount; i++)
            {
                GenerateNextIsland(i, initialIslandCount);
            }

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

        /// <summary>모든 섬이 공유하는 해안선 띠 머티리얼(색/텍스처가 같으므로 하나면 충분하다).</summary>
        private Material shorelineBandMaterial;

        /// <summary>
        /// 퀄리티 개선(바다): 수면 텍스처의 UV를 아주 느리게 흘려보내 정지 화면 같던 바다에
        /// 최소한의 움직임을 준다. 셰이더를 새로 만들 수 없는 파이프라인이라(3D/셰이더 에셋 0개)
        /// 머티리얼 오프셋 애니메이션이 물살을 표현할 수 있는 유일한 수단이다.
        /// Time.time을 쓰므로 타이틀 화면(Time.timeScale = 0)에서는 자연스럽게 멈춘다.
        /// </summary>
        private void Update()
        {
            if (oceanMaterial == null)
                return;

            oceanMaterial.mainTextureOffset = new Vector2(
                Mathf.Repeat(oceanScrollSpeed.x * Time.time, 1f),
                Mathf.Repeat(oceanScrollSpeed.y * Time.time, 1f));
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
        /// 한 변이 size인 수평 격자 평면 메시를 만든다(y = 0, 위를 향한 법선).
        ///
        /// UV는 내장 Plane과 동일하게 평면 전체에 0~1로 정규화한다 - CreateOceanMaterial의
        /// mainTextureScale(oceanSize/10)과 Update의 스크롤 오프셋 의미가 한 글자도 바뀌지 않게 하기
        /// 위해서다("1타일 = 월드 10미터"라는 인스펙터 툴팁이 계속 사실이어야 한다).
        ///
        /// 격자 위상: 정점이 x = -size/2 + (i + 0.37)·cell, z = -size/2 + (j + 0.23)·cell 에 놓인다.
        /// x/z에 서로 다른 비율을 쓰는 것이 핵심이다 - 같은 비율이면 칸의 대각선이 z = x 직선이 되어
        /// 월드 원점(플레이어 시작 지점)을 그대로 지나간다. 두 값이 다르면 원점은 정점도 모서리도
        /// 대각선도 아닌 칸 내부의 한 점이 된다(CreateOcean의 흰 세로선 주석 참고).
        /// </summary>
        private static Mesh GenerateOceanMesh(float size, int cells)
        {
            cells = Mathf.Clamp(cells, 2, 200);
            float cell = size / cells;
            float half = size * 0.5f;
            const float phaseX = 0.37f;
            const float phaseZ = 0.23f;

            int lineCount = cells + 1;
            var vertices = new Vector3[lineCount * lineCount];
            var normals = new Vector3[vertices.Length];
            var uvs = new Vector2[vertices.Length];
            var triangles = new int[cells * cells * 6];

            for (int j = 0; j < lineCount; j++)
            {
                float z = -half + (j + phaseZ) * cell;
                for (int i = 0; i < lineCount; i++)
                {
                    float x = -half + (i + phaseX) * cell;
                    int index = j * lineCount + i;
                    vertices[index] = new Vector3(x, 0f, z);
                    normals[index] = Vector3.up;
                    uvs[index] = new Vector2(x / size + 0.5f, z / size + 0.5f);
                }
            }

            int t = 0;
            for (int j = 0; j < cells; j++)
            {
                for (int i = 0; i < cells; i++)
                {
                    int a = j * lineCount + i;          // (i,   j)
                    int b = a + 1;                      // (i+1, j)
                    int c = a + lineCount;              // (i,   j+1)
                    int d = c + 1;                      // (i+1, j+1)

                    // 왼손 좌표계에서 위를 향한 면의 감김. 반대로 감으면 바다가 통째로 사라진다
                    // (IslandMeshGenerator / GenerateShorelineBandMesh와 같은 함정).
                    triangles[t++] = a;
                    triangles[t++] = c;
                    triangles[t++] = b;

                    triangles[t++] = b;
                    triangles[t++] = c;
                    triangles[t++] = d;
                }
            }

            var mesh = new Mesh();
            mesh.name = "OceanGrid";
            // 65,535 정점을 넘길 일은 없지만(64칸 = 4,225), cells 상한이 바뀌어도 안전하게 32비트로 둔다.
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.vertices = vertices;
            mesh.normals = normals;
            mesh.uv = uvs;
            mesh.triangles = triangles;
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>
        /// 바다 평면에 사용할 파란색 URP Lit 머티리얼을 만든다.
        /// 버그 수정: 그동안 완전 단색 평면이라 거대한 바다가 하나의 색종이처럼 밋밋하게 보였다.
        /// 물결 느낌의 흑백 그레인 텍스처(Resources/Textures/water.png)를 곱해 씌우고, 매끈하고
        /// 살짝 금속성 있는 표면(Smoothness/Metallic)으로 설정해 햇빛이 비칠 때 반짝이는 느낌을 준다.
        /// </summary>
        private Material CreateOceanMaterial()
        {
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
            SpawnAircraftWreck(startIsland);
            SpawnBoatWorkbench(startIsland);
            islands.Add(startIsland);
            return startIsland;
        }

        /// <summary>
        /// 시작 시선을 확정한다. 플레이어를 경비행기 잔해의 정반대 방향으로 돌려세우고, 같은 방향을
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
        /// 시작 섬(캠프)에 배 작업대를 한 번 배치한다. 도면과 재료를 다 모아도 이 오브젝트와 상호작용(E키)해야
        /// 실제로 재료가 투입되고 단계가 진행되므로, 배 엔딩 경로에 반드시 필요한 배치다.
        /// 간단한 작업대(받침대+공구대) 형태를 프리미티브 조합으로 표현한다.
        /// </summary>
        private void SpawnBoatWorkbench(IslandInstance startIsland)
        {
            if (boatConstruction == null)
                return;

            Vector3 position = startIsland.mapPosition + new Vector3(-6f, 0f, -3f);
            position = TerrainSampler.SnapToGround(position);

            var go = new GameObject("BoatWorkbench");
            go.transform.SetParent(transform);
            go.transform.position = position;

            // 작업대 상판(넓적한 큐브) + 다리 2개(가는 원기둥) + 위에 놓인 공구(작은 큐브)로 작업대 형태를 표현한다.
            StructureVisualBuilder.CreateVisualPart(go.transform, "Tabletop", PrimitiveType.Cube,
                Vector3.up * 0.8f, new Vector3(2.2f, 0.15f, 1.2f), new Color(0.5f, 0.35f, 0.2f));
            StructureVisualBuilder.CreateVisualPart(go.transform, "LegLeft", PrimitiveType.Cylinder,
                new Vector3(-0.8f, 0.4f, 0f), new Vector3(0.12f, 0.4f, 0.12f), new Color(0.35f, 0.24f, 0.14f));
            StructureVisualBuilder.CreateVisualPart(go.transform, "LegRight", PrimitiveType.Cylinder,
                new Vector3(0.8f, 0.4f, 0f), new Vector3(0.12f, 0.4f, 0.12f), new Color(0.35f, 0.24f, 0.14f));
            StructureVisualBuilder.CreateVisualPart(go.transform, "Tool", PrimitiveType.Cube,
                new Vector3(0.4f, 0.95f, 0.2f), new Vector3(0.5f, 0.12f, 0.12f), new Color(0.4f, 0.4f, 0.42f),
                Quaternion.Euler(0f, 30f, 0f));

            var boxCollider = go.AddComponent<BoxCollider>();
            boxCollider.center = new Vector3(0f, 0.6f, 0f);
            boxCollider.size = new Vector3(2.4f, 1.2f, 1.4f);

            var workbench = go.AddComponent<BoatWorkbench>();
            workbench.boatConstruction = boatConstruction;
        }

        /// <summary>
        /// 시작 섬(불시착 지점)에 경비행기 잔해를 한 번 배치한다. 프리미티브를 조합해 부서진 기체 형태를 만들고,
        /// AircraftWreck 컴포넌트를 붙여 상호작용(E키)으로 수리 재료를 투입할 수 있게 한다.
        /// </summary>
        private void SpawnAircraftWreck(IslandInstance startIsland)
        {
            if (aircraftRepair == null)
                return;

            // 오프셋을 필드로 뺐다(기본값은 기존과 동일한 6,0,-4). ApplyStartingFacing이 같은 값으로
            // 플레이어 시작 시선을 계산하므로, 잔해를 옮겨도 "등 뒤" 관계가 자동으로 유지된다.
            Vector3 position = startIsland.mapPosition + aircraftWreckOffset;
            position = TerrainSampler.SnapToGround(position);

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
        /// 새 섬을 하나 생성한다. IslandGenerator로 규모를 정하고, 기존 섬과 겹치지 않는 위치를 찾아 배치한다.
        /// </summary>
        /// <param name="islandIndex">초기 생성 순서에서 이 섬이 몇 번째(0부터)인지. 대형/특대 섬 최소 보장 판단에 쓰인다.</param>
        /// <param name="totalIslandCount">이번 초기 생성에서 만들 전체 섬 개수.</param>
        public IslandInstance GenerateNextIsland(int islandIndex = 0, int totalIslandCount = 1)
        {
            IslandSize size = islandGenerator != null
                ? islandGenerator.GenerateNextIslandSize(islandIndex, totalIslandCount, islandLayoutRng)
                : IslandSize.Small;

            var newIsland = new IslandInstance
            {
                islandId = islands.Count,
                size = size,
                mapPosition = FindValidPosition(islandLayoutRng),
                isDiscovered = false,
                isStartingIsland = false,
            };

            SpawnPlaceholder(newIsland);
            SpawnIslandContent(newIsland);
            islands.Add(newIsland);
            return newIsland;
        }

        /// <summary>
        /// 섬이 생성된 직후, 연결된 스포너가 있다면 채집 자원, 위험 요소, 배 도면 습득 지점을 함께 배치한다.
        /// B3-3: 각 스포너가 이 섬 전용 결정적 System.Random 스트림을 만들 수 있도록 worldSeed를 함께
        /// 넘긴다(스포너 내부에서 island.islandId와 조합해 시드를 만든다 - SeededRandomExtensions 참고).
        /// </summary>
        private void SpawnIslandContent(IslandInstance island)
        {
            resourceSpawner?.SpawnResourcesForIsland(island, transform, worldSeed);
            hazardSpawner?.SpawnHazardsForIsland(island, transform, worldSeed);
            blueprintSpawner?.SpawnBlueprintForIsland(island, transform, worldSeed);
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
                placeholder = CreateProceduralIslandTerrain(radius, island.mapPosition);
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
            // 섬 생성은 월드당 9회뿐이라 이 호출의 비용은 무시할 수 있다.
            Physics.SyncTransforms();

            IslandMeshGenerator.BuildIslandSurface(
                placeholder,
                IslandSizeMetrics.GetTerrainRadius(island.size),
                SeededRandomExtensions.CreateForSalt(worldSeed, VegetationSeedSalt + island.islandId));
        }

        /// <summary>초목 배치 전용 난수 salt 대역. 자원/위험요소/섬 레이아웃 스트림과 분리돼 있다.</summary>
        private const int VegetationSeedSalt = 3000000;

        /// <summary>
        /// 지정한 반지름/위치에 절차적 섬 지형 메시를 생성한다.
        /// MeshFilter/MeshRenderer/MeshCollider를 직접 붙여 플레이어가 실제로 걸어다닐 수 있게 한다.
        /// </summary>
        private GameObject CreateProceduralIslandTerrain(float radius, Vector3 position)
        {
            var go = new GameObject("IslandTerrain");
            go.transform.SetParent(transform);
            go.transform.position = position;

            // 퀄리티 개선: 섬 반지름이 10배로 커진 뒤 예전 고정 해상도(링6/세그먼트24)를 그대로 쓰면
            // 삼각형 하나하나가 너무 커져 각진 저해상도 지형처럼 보인다. 반지름에 비례해 링/세그먼트
            // 수를 늘려 단위 면적당 디테일 밀도를 비슷하게 유지하되, 정점 수가 지나치게 많아지지
            // 않도록 상한선을 둔다.
            int ringCount = Mathf.Clamp(Mathf.RoundToInt(radius / 5f), 6, 40);
            int radialSegments = Mathf.Clamp(Mathf.RoundToInt(radius * 1.5f), 24, 90);
            var mesh = IslandMeshGenerator.GenerateIslandMesh(radius, terrainMaxHeight, ringCount, radialSegments);

            var meshFilter = go.AddComponent<MeshFilter>();
            meshFilter.sharedMesh = mesh;

            var meshRenderer = go.AddComponent<MeshRenderer>();
            meshRenderer.sharedMaterial = terrainMaterial != null ? terrainMaterial : CreateDefaultTerrainMaterial(radius);

            var meshCollider = go.AddComponent<MeshCollider>();
            meshCollider.sharedMesh = mesh;

            CreateShorelineBand(go.transform, radius, radialSegments);

            return go;
        }

        /// <summary>
        /// 퀄리티 개선(바다): 섬을 두르는 얕은 물 띠(고리 메시)를 해수면 바로 위에 깐다.
        /// 그동안 바다는 수평선까지 완전한 단색 평면이라, 물이 얕은 해안과 깊은 바다가 시각적으로
        /// 전혀 구분되지 않았다(어디까지 걸어 들어가도 되는지 색으로 알 수 없었다).
        /// 셰이더를 새로 만들 수 없으므로, 절차적 고리 메시 + 코드로 생성한 반경 방향 알파 그라데이션
        /// 텍스처 조합으로 "안쪽은 밝고 바깥으로 갈수록 스르르 사라지는 띠"를 만든다.
        /// 콜라이더가 없는 순수 시각 요소이고, 해수면(seaLevel) 판정은 PlayerController가 y좌표만으로
        /// 하므로 수영/잠수 판정에는 아무 영향이 없다.
        /// </summary>
        private void CreateShorelineBand(Transform islandTransform, float radius, int radialSegments)
        {
            if (shorelineBandOuterScale <= 1f || radius <= 0f)
                return;

            var go = new GameObject("ShorelineBand");
            go.transform.SetParent(islandTransform, false);
            // 섬 중심의 XZ는 그대로 두고 높이만 해수면 바로 위로 올린다(바다 평면과의 z-파이팅 회피).
            // localPosition이 아니라 월드 position으로 지정해, 부모 트랜스폼에 어떤 값이 들어 있어도 항상 해수면에 맞는다.
            go.transform.position = new Vector3(
                islandTransform.position.x, seaLevel + shorelineBandHeight, islandTransform.position.z);

            var meshFilter = go.AddComponent<MeshFilter>();
            meshFilter.sharedMesh = GenerateShorelineBandMesh(
                radius * 0.95f, radius * shorelineBandOuterScale, radialSegments);

            var meshRenderer = go.AddComponent<MeshRenderer>();
            meshRenderer.sharedMaterial = GetShorelineBandMaterial();
            // 얕은 물 띠가 그림자를 드리우거나 받으면 평평한 판때기가 도드라져 보인다.
            meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            meshRenderer.receiveShadows = false;
        }

        /// <summary>
        /// 안쪽 반지름 innerRadius, 바깥 반지름 outerRadius인 납작한 고리(annulus) 메시를 만든다.
        /// UV는 u = 반경 방향 진행도(0=안쪽, 1=바깥), v = 각도로 잡아, 알파 그라데이션 텍스처 한 장을
        /// 반경 방향으로 그대로 입힐 수 있게 한다.
        /// </summary>
        private static Mesh GenerateShorelineBandMesh(float innerRadius, float outerRadius, int radialSegments)
        {
            radialSegments = Mathf.Clamp(radialSegments, 12, 120);

            var mesh = new Mesh();
            mesh.name = "ShorelineBand";

            var vertices = new Vector3[radialSegments * 2];
            var uvs = new Vector2[radialSegments * 2];
            var triangles = new int[radialSegments * 6];

            for (int seg = 0; seg < radialSegments; seg++)
            {
                float angle = (float)seg / radialSegments * Mathf.PI * 2f;
                float cos = Mathf.Cos(angle);
                float sin = Mathf.Sin(angle);
                float v = (float)seg / radialSegments;

                int inner = seg * 2;
                int outer = inner + 1;

                vertices[inner] = new Vector3(cos * innerRadius, 0f, sin * innerRadius);
                vertices[outer] = new Vector3(cos * outerRadius, 0f, sin * outerRadius);
                uvs[inner] = new Vector2(0f, v);
                uvs[outer] = new Vector2(1f, v);
            }

            for (int seg = 0; seg < radialSegments; seg++)
            {
                int inner = seg * 2;
                int outer = inner + 1;
                int nextInner = ((seg + 1) % radialSegments) * 2;
                int nextOuter = nextInner + 1;

                int t = seg * 6;
                // IslandMeshGenerator와 같은 이유로 감는 방향에 주의한다 - 반대로 감으면 위에서 봤을 때
                // 뒷면 컬링으로 띠가 통째로 사라진다.
                triangles[t + 0] = inner;
                triangles[t + 1] = nextOuter;
                triangles[t + 2] = outer;

                triangles[t + 3] = inner;
                triangles[t + 4] = nextInner;
                triangles[t + 5] = nextOuter;
            }

            mesh.vertices = vertices;
            mesh.uv = uvs;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            return mesh;
        }

        /// <summary>
        /// 해안선 띠용 반투명 머티리얼을 만들어 캐시한다. 모든 섬이 같은 색/텍스처를 쓰므로 하나만 만든다.
        /// URP Lit은 기본이 Opaque라 알파가 무시되므로, EffectBuilder.GetParticleMaterial()이 실측으로
        /// 검증해 둔 것과 같은 순서로 투명 모드 프로퍼티/키워드를 직접 세팅한다.
        /// </summary>
        private Material GetShorelineBandMaterial()
        {
            if (shorelineBandMaterial != null)
                return shorelineBandMaterial;

            var shader = Shader.Find("Universal Render Pipeline/Lit");
            var material = new Material(shader != null ? shader : Shader.Find("Standard"));
            material.color = shorelineBandColor;
            material.mainTexture = CreateShorelineGradientTexture();

            if (material.HasProperty("_Smoothness"))
                material.SetFloat("_Smoothness", 0.6f);
            if (material.HasProperty("_Metallic"))
                material.SetFloat("_Metallic", 0f);

            if (material.HasProperty("_Surface"))
                material.SetFloat("_Surface", 1f); // 0=Opaque, 1=Transparent
            if (material.HasProperty("_Blend"))
                material.SetFloat("_Blend", 0f); // Alpha 블렌드
            if (material.HasProperty("_SrcBlend"))
                material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            if (material.HasProperty("_DstBlend"))
                material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            if (material.HasProperty("_ZWrite"))
                material.SetFloat("_ZWrite", 0f);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.DisableKeyword("_ALPHATEST_ON");
            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;

            shorelineBandMaterial = material;
            return shorelineBandMaterial;
        }

        /// <summary>
        /// 가로(u) 방향으로만 알파가 변하는 그라데이션 텍스처를 코드로 생성한다.
        /// u=0(해안 쪽)에서 가장 진하고 u=1(먼바다 쪽)에서 완전히 투명해지는 2차 감쇠 곡선이라,
        /// 띠의 바깥 경계가 선으로 보이지 않고 깊은 바다색에 자연스럽게 녹아든다.
        /// 세로(v) 방향으로는 변화가 없으므로 높이 2픽셀이면 충분하다.
        /// </summary>
        private static Texture2D CreateShorelineGradientTexture()
        {
            const int width = 64;
            const int height = 2;
            const float peakAlpha = 0.55f;

            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            texture.name = "ShorelineGradient";
            texture.wrapMode = TextureWrapMode.Clamp; // 반복시키면 u=1(투명)과 u=0(불투명)이 맞닿아 경계선이 생긴다.
            texture.filterMode = FilterMode.Bilinear;
            texture.hideFlags = HideFlags.HideAndDontSave;

            var pixels = new Color32[width * height];
            for (int x = 0; x < width; x++)
            {
                float u = (float)x / (width - 1);
                float fade = (1f - u) * (1f - u);
                byte alpha = (byte)Mathf.RoundToInt(Mathf.Clamp01(peakAlpha * fade) * 255f);
                var pixel = new Color32(255, 255, 255, alpha);

                for (int y = 0; y < height; y++)
                    pixels[y * width + x] = pixel;
            }

            texture.SetPixels32(pixels);
            texture.Apply();
            return texture;
        }

        /// <summary>
        /// 섬 지형용 머티리얼이 지정되지 않았을 때 사용할 기본 모래색 URP Lit 머티리얼을 만든다.
        /// radius: 이 섬의 실제 반지름(미터). UV가 0~1로 정규화돼 있어(IslandMeshGenerator),
        /// 타일 반복 횟수를 고정값으로 두면 섬이 커질수록 텍스처 한 칸이 늘어나 흐릿해 보인다.
        /// 반지름에 비례해서 반복 횟수를 늘려 실제 모래 알갱이 크기가 섬 크기와 무관하게 일정하게 보이도록 한다.
        /// </summary>
        private Material CreateDefaultTerrainMaterial(float radius)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            var material = new Material(shader != null ? shader : Shader.Find("Standard"));
            material.color = new Color(0.76f, 0.7f, 0.5f);

            // 모래 그레인 노이즈 텍스처를 곱해 씌워, 밋밋한 단색 대신 표면에 자잘한 질감을 준다.
            // (Resources/Textures/sand.png. 절차적으로 생성한 흑백 타일링 노이즈로, 색상은 위 material.color가 그대로 담당한다.)
            var sandTexture = Resources.Load<Texture2D>("Textures/sand");
            if (sandTexture != null)
            {
                material.mainTexture = sandTexture;
                float tiling = radius * 1.5f; // 섬 크기 대비 타일 반복 횟수(반지름 비례)
                material.mainTextureScale = new Vector2(tiling, tiling);
            }
            return material;
        }

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

    /// <summary>
    /// 플레이어의 현재 진행 단계를 판정하는 읽기 전용 유틸리티(Docs/Design_Progression.md 6장 요청).
    ///
    /// 설계 원칙 세 가지:
    /// 1. **상태를 만들지 않는다.** 판정 입력이 전부 이미 public이라, 매 호출마다 그 값만 보고 다시 계산한다.
    ///    새 필드도, 새 컴포넌트도, 세이브 포맷 변경도 없다(game-designer 명시 조건). 따라서 F9 불러오기나
    ///    씬 재진입 후에도 "복원해야 할 진행 단계" 같은 것이 애초에 존재하지 않는다.
    /// 2. **위 단계부터 아래로 판정한다.** 높은 단계 조건이 하나라도 성립하면 즉시 그 단계를 반환하므로,
    ///    "탈출 준비 중인데 배가 고파져서 1단계로 되돌아가는" 역행이 구조적으로 불가능하다.
    /// 3. **아이템은 이름으로 조회한다.** 이 유틸리티는 static이라 인스펙터 참조를 가질 수 없다. 이 프로젝트가
    ///    이미 여러 곳에서 쓰는 한국어 itemName 스위치와 같은 관례다(IslandResourceSpawner.GetNodeShape 등).
    ///
    /// 이 파일에 있는 이유: 파일 소유권 규칙상 systems-engineer-B가 편집할 수 있는 파일 안에 둬야 했고,
    /// 그 중 "월드 전체 상태를 다루는" 성격에 가장 가까운 것이 이 파일이다. WorldMapManager 자체와는
    /// 의존 관계가 전혀 없으므로(참조도 호출도 하지 않는다) 나중에 독립 파일로 그대로 옮길 수 있다.
    /// </summary>
    public static class ProgressionTracker
    {
        /// <summary>대형 섬 금속조각을 여는 유일한 열쇠. 2단계 → 3단계 전환 신호(Design_Progression 2장).</summary>
        public const string HatchetItemName = "손도끼";

        /// <summary>대형 섬 이상에서만 나오는 재료. 최초 획득이 3단계 → 4단계 전환 신호.</summary>
        public const string MetalScrapItemName = "금속조각";

        /// <summary>손도끼 레시피 재료(나뭇가지 1 + 돌조각 2). 채집 루프를 이해했다는 증거로 쓴다.</summary>
        public const string StickItemName = "나뭇가지";

        /// <summary>손도끼 레시피 재료.</summary>
        public const string StoneItemName = "돌조각";

        /// <summary>
        /// 1단계 → 2단계 전환 임계 비율. 문서의 전환 신호는 "허기·갈증이 70% 아래로 떨어졌다가 회복"인데,
        /// "떨어진 적이 있다"는 이력이라 상태 저장 없이는 볼 수 없다. 상태를 만들지 않기 위해 두 가지
        /// 관측 가능한 대체 신호를 OR로 쓴다 - (a) 지금 70% 아래이거나, (b) 이미 기초 재료를 채집했거나.
        /// (b)가 있어서 밥을 먹어 허기가 100%로 돌아와도 1단계로 되돌아가지 않는다(재료는 남아 있으므로).
        /// 갈증은 0.08/초로 줄어 약 6분이면 70%에 닿으므로, 실제로도 문서의 "0~10분" 구간과 잘 맞는다.
        /// </summary>
        public const float SurvivalPressureRatio = 0.7f;

        /// <summary>
        /// 현재 진행 단계를 판정한다. 인자는 전부 null을 허용한다(연결되지 않은 참조가 있어도 NRE 없이
        /// 판정 가능한 범위까지만 계산한다 - 이 프로젝트에서 씬 참조 누락은 실제로 반복된 사고다).
        /// 매 프레임 호출해도 되는 비용(인벤토리 1회 순회 × 최대 4번)이며, 부작용이 전혀 없다.
        /// </summary>
        public static ProgressionStage Evaluate(PlayerInventory inventory, SurvivalStats stats,
            BoatConstructionSystem boat, AircraftRepairSystem aircraft, IslandTravel travel)
        {
            // 5단계 - 한쪽 경로가 100%에 도달했다.
            if ((boat != null && boat.isFullyComplete) || (aircraft != null && aircraft.isRepairComplete))
                return ProgressionStage.Escape;

            // 4단계 - 탈출 경로에 실제로 착수했다. 도면 습득/단계 완료/재료 투입/금속조각 최초 획득 중 하나.
            bool boatStarted = boat != null &&
                (boat.hasCurrentStageBlueprint || boat.highestCompletedStage >= 1 || boat.GetOverallProgress() > 0f);
            bool aircraftStarted = aircraft != null && aircraft.GetOverallProgress() > 0f;
            if (boatStarted || aircraftStarted || CountByName(inventory, MetalScrapItemName) > 0)
                return ProgressionStage.EscapePreparation;

            // 3단계 - 손도끼를 얻었거나(문서의 전환 신호), 이미 시작 섬을 떠나 탐험을 시작했다.
            bool leftStartingIsland = travel != null && travel.currentIslandId != 0;
            if (leftStartingIsland || CountByName(inventory, HatchetItemName) > 0)
                return ProgressionStage.Exploration;

            // 2단계 - 생존 압박을 한 번 겪었거나, 이미 기초 재료를 채집했다(위 SurvivalPressureRatio 주석).
            float pressureThreshold = SurvivalStats.MaxStatValue * SurvivalPressureRatio;
            bool feltSurvivalPressure = stats != null &&
                (stats.hunger <= pressureThreshold || stats.thirst <= pressureThreshold);
            bool hasGatheredBasics = CountByName(inventory, StickItemName) > 0 || CountByName(inventory, StoneItemName) > 0;
            if (feltSurvivalPressure || hasGatheredBasics)
                return ProgressionStage.Tools;

            return ProgressionStage.Survival;
        }

        /// <summary>
        /// 단계에 대응하는 HUD 목표 문구를 반환한다. 문구는 Docs/Design_Progression.md 3장에 확정된 것을
        /// 그대로 옮긴 것이다(임의로 바꾸지 말 것 - 키 안내가 문구의 핵심이다).
        /// 4단계만 두 줄('\n' 구분)이며, 이는 "두 갈래가 처음 명시되는 유일한 지점"이라는 설계 의도다.
        /// boat/aircraft를 넘기면 4단계 문구에 실제 진행도가 채워지고, 넘기지 않으면 진행도 없이 나온다.
        /// </summary>
        public static string GetObjectiveText(ProgressionStage stage,
            BoatConstructionSystem boat = null, AircraftRepairSystem aircraft = null)
        {
            switch (stage)
            {
                case ProgressionStage.Tools:
                    return "제작(V)으로 도구를 만드세요 — 손도끼 · 창";

                case ProgressionStage.Exploration:
                    return "지도(M)를 열어 큰 섬으로 이동하세요";

                case ProgressionStage.EscapePreparation:
                {
                    // 배는 "몇 단계까지 왔는가", 경비행기는 "재료를 몇 % 모았는가"가 각각 자연스러운 단위다
                    // (BoatConstructionSystem은 단계제, AircraftRepairSystem은 재료 누적제).
                    int boatStage = boat != null
                        ? Mathf.Clamp(boat.currentStage, 1, BoatConstructionSystem.TotalStages)
                        : 1;
                    int aircraftPercent = aircraft != null
                        ? Mathf.RoundToInt(Mathf.Clamp01(aircraft.GetOverallProgress()) * 100f)
                        : 0;

                    // 배(A)를 먼저 쓴다 - Design_Progression 4장의 결정(기본 안내 경로를 배로 둔다).
                    return $"탈출 경로 A — 배 제작 (작업대: 시작 섬)  [{BoatConstructionSystem.TotalStages}단계 중 {boatStage}단계]\n" +
                           $"탈출 경로 B — 경비행기 수리 (잔해: 시작 섬)  [재료 {aircraftPercent}%]";
                }

                case ProgressionStage.Escape:
                    return "탈출 준비 완료 — 시작 섬에서 탈출하세요";

                case ProgressionStage.Survival:
                default:
                    return "물과 음식을 확보하세요";
            }
        }

        /// <summary>
        /// 판정과 문구 생성을 한 번에 한다(UI가 가장 자주 쓸 형태). Evaluate와 동일하게 부작용이 없다.
        /// </summary>
        public static string GetObjectiveText(PlayerInventory inventory, SurvivalStats stats,
            BoatConstructionSystem boat, AircraftRepairSystem aircraft, IslandTravel travel)
        {
            return GetObjectiveText(Evaluate(inventory, stats, boat, aircraft, travel), boat, aircraft);
        }

        /// <summary>
        /// 인벤토리에서 지정한 이름의 아이템 개수를 센다(PlayerInventory.GetItemCount와 같은 셈법 -
        /// InventoryItem 하나가 1개다). ItemData 참조 없이 이름만으로 셀 수 있어야 이 유틸리티가
        /// 인스펙터 배선 없이 동작한다. 인벤토리가 null이거나 비어 있으면 0.
        /// </summary>
        public static int CountByName(PlayerInventory inventory, string itemName)
        {
            if (inventory == null || inventory.items == null || string.IsNullOrEmpty(itemName))
                return 0;

            int count = 0;
            foreach (var item in inventory.items)
            {
                if (item != null && item.data != null && item.data.itemName == itemName)
                    count++;
            }
            return count;
        }
    }
}
