using UnityEngine;
using MakeGame.Data;

namespace MakeGame.Systems
{
    /// <summary>
    /// 배(뗏목)를 월드에 실제로 서 있는 구조물로 만든다.
    ///
    /// [왜 만들었나] 그동안 배는 BoatConstructionSystem의 숫자 카운터일 뿐이라 월드에 아무것도 없었고,
    /// 완성해도 "엔딩 트리거"로만 소비됐다. 방향 전환(AGENT_BRIEF ★현재 방향: 탈출이 정답이 아니다)에
    /// 따라 배는 **보이고, 자라고, 올라설 수 있는 구조물**이어야 한다. 이 컴포넌트가 그 본체다.
    /// 완성 갑판은 8m x 5.2m 평면이며, 다음 배치에서 그 위에 집을 올릴 토대로 쓴다.
    ///
    /// [3D 에셋 0개] 전 파츠를 GameObject.CreatePrimitive로 조립한다(StructureVisualBuilder 경유).
    /// 머티리얼은 5개만 만들어 전 파츠가 공유한다 - 완성 단계 파츠가 40개를 넘고 단계마다 통째로
    /// 다시 만들기 때문에, 파츠마다 머티리얼을 만들면 SRP 배처가 죽는다(AGENT_BRIEF 4장).
    ///
    /// [배치] 시작 섬의 물가. TerrainSampler.SnapToGround로 실제 지형 높이를 재서 해안선을 찾는데,
    /// 이 헬퍼는 이름이 "Island_" 로 시작하는 콜라이더만 지형으로 인정한다는 점을 그대로 이용한다 -
    /// 바다 평면(Ocean)과 자원/위험요소에는 절대 스냅되지 않으므로, "레이가 아무것도 못 맞은 지점"
    /// = "섬 메시가 끝난 지점" = 물이다. 이 성질로 해안선을 찾는다(FindShoreDistance 참고).
    /// 반대로 뗏목 자신에게는 콜라이더가 있지만 이름이 "Island_"가 아니므로, 다른 시스템의 지형
    /// 스냅을 오염시키지 않는다.
    /// </summary>
    [DisallowMultipleComponent]
    public class RaftStructure : MonoBehaviour
    {
        // ── 치수 (전부 로컬 좌표. 로컬 +Z = 뱃머리 = 바다 쪽) ───────────────────────────
        /// <summary>고물~뱃머리 길이(로컬 Z).</summary>
        public const float DeckLength = 8f;

        /// <summary>좌현~우현 폭(로컬 X).</summary>
        public const float DeckWidth = 5.2f;

        /// <summary>
        /// 완성 갑판 윗면 높이(해수면 기준). 플레이어가 서는 면이자 건축 시스템이 집을 올리는 면이다.
        /// **널판 상수에서 유도한다** - 널 높이/두께를 바꿨는데 이 값이 옛날 그대로면 갑판 위 건축물이
        /// 허공에 뜨거나 바닥에 박힌다. 값 자체는 종전과 같은 0.72다.
        /// </summary>
        public const float DeckSurfaceY = DeckPlankY + DeckPlankThickness * 0.5f;

        /// <summary>선체 통나무 지름과 중심 높이. 지름 0.8 / 중심 0.1 이면 윗면이 정확히 0.5다.</summary>
        private const float LogDiameter = 0.8f;
        private const float LogCenterY = 0.1f;

        /// <summary>가로보(통나무를 가로질러 묶는 각재) 중심 높이. 통나무 윗면(0.5)에 얹힌다.</summary>
        private const float CrossbeamY = 0.56f;

        /// <summary>갑판 널 중심 높이/두께. 0.67 ± 0.05 → 윗면이 DeckSurfaceY(0.72)와 일치한다.</summary>
        private const float DeckPlankY = 0.67f;
        private const float DeckPlankThickness = 0.10f;

        /// <summary>
        /// 갑판 널 개수. 갑판 치수의 **단일 출처**다 - BuildDeck(실제 널 배치)과 DeckLocalSize/HasDeck
        /// (건축 시스템에 알려 주는 값)이 둘 다 여기서 나온다. 한쪽만 고치는 사고를 막는다.
        /// </summary>
        private const int DeckPlankCount = 10;

        /// <summary>건축 전용 컨테이너 이름. 갑판 재생성이 절대로 지우지 않는 유일한 자식이다.</summary>
        public const string PlacedStructuresName = "PlacedStructures";

        /// <summary>승선 발판이 고물에서 해변 쪽으로 뻗는 수평 거리.</summary>
        private const float RampRun = 2.2f;

        /// <summary>건조 외형 단계 수(0=골조 … 5=완성). 최소 4단계 요구를 넘겨 6단계로 잡았다.</summary>
        public const int TotalBuildLevels = 6;

        [Tooltip("진행도를 읽어올 배 제작 시스템. 비어 있으면 씬에서 한 번 찾는다.")]
        public BoatConstructionSystem boatConstruction;

        [Tooltip("해안선에서 바다 쪽으로 얼마나 밀어낼지(미터). 고물이 물가에 살짝 걸치도록 잡은 값이다.")]
        public float shoreOutwardOffset = 0.2f;

        [Tooltip("진행도를 다시 읽어 외형을 맞추는 주기(초). 이벤트를 놓치는 경로(F9 불러오기)의 안전망이다.")]
        public float refreshInterval = 0.2f;

        /// <summary>지금 화면에 지어져 있는 단계. -1이면 아직 아무것도 안 지었다.</summary>
        private int builtLevel = -1;

        /// <summary>물가 위치/방향을 확정했는지. 확정 전에는 파츠를 만들지 않는다(엉뚱한 데 지어지지 않게).</summary>
        private bool anchored;

        private float refreshTimer;
        private bool subscribed;

        private WorldMapManager worldMap;
        private Transform visualRoot;
        private BoxCollider hullCollider;

        // ── 건축 시스템 계약 ────────────────────────────────────────────────────────
        private static RaftStructure activeInstance;

        /// <summary>갑판 좌표계의 뿌리. 건조 단계가 바뀌어도 **절대 파괴되지 않는다**.</summary>
        private Transform deckRoot;

        /// <summary>건축 시스템이 소유하는 컨테이너. 여기 있는 것은 갑판 재생성에서 살아남는다.</summary>
        private Transform placedStructures;

        /// <summary>
        /// 씬에 살아 있는 뗏목. 없으면 null이다.
        /// 인스턴스 확보는 BoatConstructionSystem.EnsureRaftStructure가 담당하므로(중복 방지 포함)
        /// 여기서는 "먼저 깨어난 쪽이 이긴다"만 지킨다 - 늦게 깨어난 중복은 스스로 비활성화한다.
        /// </summary>
        public static RaftStructure Active => activeInstance != null ? activeInstance : null;

        /// <summary>
        /// 갑판 위에 물건을 붙일 부모. 이 밑에 두면 뗏목 좌표계를 따라간다(뗏목이 옮겨져도 같이 간다).
        /// 로컬 원점/회전은 뗏목 본체와 동일하므로, 갑판 윗면은 로컬 y = DeckTopLocalY다.
        /// </summary>
        public Transform DeckRoot
        {
            get
            {
                EnsureDeckRoot();
                return deckRoot;
            }
        }

        /// <summary>
        /// 갑판 위 건축물 전용 컨테이너(DeckRoot의 자식). 뗏목은 이걸 만들어 주기만 하고 절대 비우지 않는다.
        /// 내용물의 수명은 건축 시스템이 관리한다.
        /// </summary>
        public Transform PlacedStructures
        {
            get
            {
                EnsureDeckRoot();
                return placedStructures;
            }
        }

        /// <summary>
        /// 지금 건조 단계에 **온전한 갑판이 깔려 있는가**. 통나무/골조만 있는 단계는 false다.
        /// 널이 절반만 깔린 단계(고물 쪽 절반)도 false다 - 중심 대칭으로 쓸 수 있는 면적이 0이라
        /// 조각을 놓으면 뱃머리 쪽 허공에 뜬다. 판정 자체는 DeckLocalSize에서 유도한다.
        /// </summary>
        public bool HasDeck
        {
            get
            {
                Vector2 size = DeckLocalSize;
                return size.x > 0.01f && size.y > 0.01f;
            }
        }

        /// <summary>갑판 윗면의 로컬 y. 널판 상수에서 유도한 값이다.</summary>
        public float DeckTopLocalY => DeckSurfaceY;

        /// <summary>
        /// 실제로 널이 깔린 갑판의 가로(x) x 세로(z), 로컬 미터. 갑판이 없으면 (0,0).
        /// 세로는 **원점 대칭으로 쓸 수 있는 길이**다(건축 시스템이 갑판 중심을 원점으로 보고 셀을 깐다).
        /// </summary>
        public Vector2 DeckLocalSize
        {
            get
            {
                GetDeckedSpan(builtLevel, out float minZ, out float maxZ);
                float usable = 2f * Mathf.Min(-minZ, maxZ);
                return usable > 0.01f ? new Vector2(DeckWidth, usable) : Vector2.zero;
            }
        }

        /// <summary>
        /// 건조 단계가 바뀌어 갑판(뗏목 파츠)이 다시 만들어졌을 때 발생한다.
        /// DeckRoot와 그 밑의 건축 컨테이너는 재생성 대상이 아니므로, 구독자는 보통 아무것도 할 게 없다.
        /// 갑판 높이/크기가 바뀌었을 수 있다는 신호로 쓴다.
        /// </summary>
        public event System.Action DeckRebuilt;

        /// <summary>승선 발판이 닿는 해변 높이(로컬 y). 지형 최대 높이가 씬 값으로 바뀌어도 따라가도록 실측한다.</summary>
        private float rampFootLocalY;

        // 공유 머티리얼 5개. 파츠 수와 무관하게 이것만 만든다.
        private Material hullWoodMaterial;
        private Material plankWoodMaterial;
        private Material fiberMaterial;
        private Material sailMaterial;
        private Material cargoMaterial;

        /// <summary>
        /// 상호작용(E키)용 작업대 컴포넌트를 뗏목 본체에 직접 붙인다.
        /// 해변의 BoatWorkbench는 시작 캠프(섬 중심 근처)에 있어 거기서 재료를 넣으면 뗏목이 보이지 않는다.
        /// 뗏목 자체가 작업대이면 "재료를 넣는다 → 눈앞에서 자란다"가 한 화면 안에서 성립한다.
        /// InteractionController / InteractionPromptUI는 둘 다 BoatWorkbench를 그대로 인식하므로
        /// UI/입력 쪽에 새로 붙일 코드가 없다.
        /// </summary>
        private void Awake()
        {
            // 중복 방지: 정상 경로(BoatConstructionSystem.EnsureRaftStructure)는 이미 하나만 만든다.
            // 여기 걸린다면 씬에 손으로 놓은 것 + 런타임 생성이 겹친 경우다. 먼저 깨어난 쪽이 이미
            // 해안을 잡고 파츠를 지었을 수 있으므로, 나중 것을 파괴하지 않고 조용히 재운다
            // (파괴하면 씬 직렬화 값이 사라진다 - AGENT_BRIEF 2장 2번).
            if (activeInstance != null && activeInstance != this)
            {
                Debug.LogWarning($"[RaftStructure] 뗏목이 이미 있다. 중복 인스턴스 '{name}'을 비활성화한다.");
                enabled = false;
                return;
            }

            activeInstance = this;

            EnsureDeckRoot();
            EnsureMaterials();

            var workbench = GetComponent<BoatWorkbench>();
            if (workbench == null)
                workbench = gameObject.AddComponent<BoatWorkbench>();

            // 선체 콜라이더는 항상 존재한다(레이캐스트 대상이자 발판). 크기는 단계마다 갱신한다.
            hullCollider = GetComponent<BoxCollider>();
            if (hullCollider == null)
                hullCollider = gameObject.AddComponent<BoxCollider>();

            ApplyHullCollider(0);
        }

        private void OnEnable()
        {
            if (activeInstance == null)
                activeInstance = this;

            TrySubscribe();
        }

        private void OnDestroy()
        {
            if (activeInstance == this)
                activeInstance = null;
        }

        /// <summary>
        /// 도메인 리로드를 끈 플레이 모드에서는 static이 이전 실행의 값을 그대로 들고 있다.
        /// 파괴된 뗏목을 가리킨 채로 남으면 Active가 "있는데 죽은" 객체를 돌려준다.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            activeInstance = null;
        }

        private void OnDisable()
        {
            if (subscribed && boatConstruction != null)
                boatConstruction.ProgressChanged -= HandleProgressChanged;

            subscribed = false;
        }

        private void Update()
        {
            TrySubscribe();

            if (!anchored)
            {
                TryAnchorToShore();
                return;
            }

            // 이벤트를 못 받는 경로(SaveLoadController.Load가 필드를 직접 대입한다)를 위한 안전망.
            // Time.timeScale이 0인 엔딩/사망 화면에서도 멈추지 않도록 unscaled를 쓴다.
            refreshTimer -= Time.unscaledDeltaTime;
            if (refreshTimer > 0f)
                return;

            refreshTimer = Mathf.Max(0.05f, refreshInterval);
            ApplyProgress();
        }

        /// <summary>
        /// 진행도 변화 이벤트를 구독한다. 참조가 아직 없으면 씬에서 한 번 찾는다
        /// (디렉터가 씬에 RaftStructure를 손으로 놓는 경우를 위한 폴백).
        /// </summary>
        private void TrySubscribe()
        {
            if (subscribed)
                return;

            if (boatConstruction == null)
                boatConstruction = FindAnyObjectByType<BoatConstructionSystem>();

            if (boatConstruction == null)
                return;

            boatConstruction.ProgressChanged += HandleProgressChanged;
            subscribed = true;

            var workbench = GetComponent<BoatWorkbench>();
            if (workbench != null && workbench.boatConstruction == null)
                workbench.boatConstruction = boatConstruction;
        }

        private void HandleProgressChanged()
        {
            ApplyProgress();
        }

        /// <summary>
        /// 현재 진행도에 해당하는 건조 단계를 계산해, 달라졌으면 외형을 다시 만든다.
        /// 단계가 그대로면 아무것도 하지 않으므로 매 프레임 불러도 비용이 없다.
        /// </summary>
        public void ApplyProgress()
        {
            if (!anchored || boatConstruction == null)
                return;

            int level = GetBuildLevel(boatConstruction.GetDetailedProgress());
            if (level == builtLevel)
                return;

            builtLevel = level;
            RebuildVisual(level);
        }

        /// <summary>
        /// 진행률(0~1)을 건조 외형 단계(0~5)로 바꾼다.
        /// 씬 설계(단계당 재료 3종 x 3단계)에서 진행률은 1/9(0.111) 단위로 움직이므로,
        /// 아래 경계는 0.111 / 0.333 / 0.556 / 0.778 / 완성 지점에 하나씩 걸린다 - 여섯 단계가
        /// 플레이 동안 고르게 나타난다.
        /// </summary>
        public static int GetBuildLevel(float progress)
        {
            if (progress >= 0.99f) return 5; // 완성 (isFullyComplete일 때만 도달한다)
            if (progress >= 0.72f) return 4;
            if (progress >= 0.50f) return 3;
            if (progress >= 0.28f) return 2;
            if (progress >= 0.08f) return 1;
            return 0;
        }

        // ─────────────────────────────────────────────────────────────────────────
        //  배치
        // ─────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// 시작 섬의 물가를 찾아 뗏목을 앉힌다. 섬이 아직 생성되지 않았으면(스크립트 실행 순서상
        /// WorldMapManager.Start가 나중일 수 있다) 아무것도 하지 않고 다음 프레임에 다시 시도한다.
        /// </summary>
        private void TryAnchorToShore()
        {
            if (worldMap == null)
                worldMap = FindAnyObjectByType<WorldMapManager>();

            if (worldMap == null)
            {
                // 월드 매니저가 없는 테스트 씬 등: 지금 있는 자리에 그대로 짓는다.
                rampFootLocalY = 0f;
                anchored = true;
                ApplyProgress();
                return;
            }

            IslandInstance startIsland = null;
            for (int i = 0; i < worldMap.islands.Count; i++)
            {
                var island = worldMap.islands[i];
                if (island == null)
                    continue;

                if (island.isStartingIsland)
                {
                    startIsland = island;
                    break;
                }

                if (startIsland == null && island.islandId == 0)
                    startIsland = island;
            }

            // 지형 오브젝트까지 실제로 만들어져 있어야 해안선을 잴 수 있다.
            if (startIsland == null || startIsland.placeholderObject == null)
                return;

            // 갓 만든 MeshCollider는 Physics.autoSyncTransforms가 기본 false라 아직 물리 씬에 없을 수
            // 있다. 이걸 빠뜨리면 아래 레이가 지형을 못 맞혀 해안선을 못 찾는다(AGENT_BRIEF 4장).
            Physics.SyncTransforms();

            Vector3 facing = ResolveShoreDirection();
            float radius = IslandSizeMetrics.GetTerrainRadius(startIsland.size);
            float shoreDistance = FindShoreDistance(startIsland.mapPosition, facing, radius);

            // 고물이 물가에 살짝 걸치도록 중심을 잡는다(중심 = 해안선 + 배 길이 절반 + 여유).
            Vector3 center = startIsland.mapPosition + facing * (shoreDistance + DeckLength * 0.5f + shoreOutwardOffset);
            center.y = worldMap.seaLevel;

            transform.SetPositionAndRotation(center, Quaternion.LookRotation(facing, Vector3.up));

            // 승선 발판이 닿을 해변 지점의 실제 높이를 잰다. terrainMaxHeight는 씬 직렬화 값(8)이
            // 코드 기본값(2.5)과 다르므로 상수로 가정하면 안 된다 - 반드시 실측한다.
            Vector3 rampFoot = center - facing * (DeckLength * 0.5f + RampRun);
            float groundY = SampleTerrainHeight(rampFoot, out bool hitTerrain);
            rampFootLocalY = hitTerrain
                ? Mathf.Clamp(groundY - center.y, -0.25f, DeckSurfaceY - 0.08f)
                : 0f;

            anchored = true;
            ApplyProgress();
        }

        /// <summary>
        /// 뗏목을 놓을 방향(섬 중심 → 물가). 플레이어가 게임을 시작할 때 바라보는 방향
        /// (WorldMapManager가 경비행기 잔해의 정반대로 시선을 잡는다)과 같은 쪽으로 맞춘다.
        /// 잔해 오프셋 하나에서 두 값을 함께 유도하므로, 디렉터가 잔해를 옮겨도 관계가 유지된다.
        /// </summary>
        private Vector3 ResolveShoreDirection()
        {
            Vector3 facing = worldMap != null ? -worldMap.aircraftWreckOffset : Vector3.forward;
            facing.y = 0f;

            if (facing.sqrMagnitude < 0.0001f)
                facing = Vector3.forward;

            return facing.normalized;
        }

        /// <summary>
        /// 섬 중심에서 facing 방향으로 나아가며 "지형이 끝나는 거리"(= 해안선)를 찾는다.
        /// TerrainSampler.SnapToGround가 "Island_" 콜라이더만 인정하는 성질을 그대로 쓴다 -
        /// 지형을 못 맞히면 바다다. 지형 표면이 해수면 근처(0.2m 이하)로 내려가는 지점도 해안으로 본다.
        /// 아무 판정도 안 서면 규모별 지형 반지름을 그대로 쓴다(안전한 기본값).
        /// </summary>
        private float FindShoreDistance(Vector3 islandCenter, Vector3 facing, float radius)
        {
            float seaLevel = worldMap != null ? worldMap.seaLevel : 0f;

            for (float distance = radius * 0.85f; distance <= radius * 1.25f; distance += 0.5f)
            {
                Vector3 probe = islandCenter + facing * distance;
                float groundY = SampleTerrainHeight(probe, out bool hitTerrain);

                if (!hitTerrain)
                    return distance;

                if (groundY - seaLevel <= 0.2f)
                    return distance;
            }

            return radius;
        }

        /// <summary>
        /// 지정 XZ의 섬 지형 높이를 잰다. 지형에 맞지 않으면 hitTerrain이 false다.
        ///
        /// SnapToGround는 지형을 못 맞히면 **넘긴 위치를 그대로 돌려준다**. 그래서 절대 나올 수 없는
        /// 센티넬 y(-1000)를 넣어 보내고, 돌아온 y가 그대로면 "지형 없음"으로 판정한다.
        /// 센티넬을 쓰는 만큼 기본 레이 길이(위 60 / 아래 120)로는 지형까지 닿지 않으므로,
        /// 시작 높이/길이를 명시적으로 크게 넘긴다.
        /// </summary>
        private float SampleTerrainHeight(Vector3 position, out bool hitTerrain)
        {
            const float Sentinel = -1000f;

            Vector3 probe = new Vector3(position.x, Sentinel, position.z);
            Vector3 result = TerrainSampler.SnapToGround(probe, 1200f, 2400f);

            hitTerrain = result.y > Sentinel + 1f;
            return result.y;
        }

        // ─────────────────────────────────────────────────────────────────────────
        //  갑판 계약 (건축 시스템이 쓰는 부분)
        // ─────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// 갑판 뿌리와 건축 컨테이너를 확보한다. 둘 다 뗏목 파츠(visualRoot)와 **형제**라서
        /// RebuildVisual이 파츠를 통째로 지워도 살아남는다 - 갑판 위에 지은 집이 단계 상승에서
        /// 사라지지 않는 이유가 이 부모 관계 하나다.
        /// </summary>
        private void EnsureDeckRoot()
        {
            if (deckRoot == null)
            {
                var rootObject = new GameObject("DeckRoot");
                rootObject.transform.SetParent(transform, false);
                deckRoot = rootObject.transform;
            }

            if (placedStructures == null)
            {
                var container = new GameObject(PlacedStructuresName);
                container.transform.SetParent(deckRoot, false);
                placedStructures = container.transform;
            }
        }

        /// <summary>
        /// 그 단계에서 실제로 깔리는 갑판 널 개수. BuildDeck과 DeckLocalSize가 공유하는 유일한 규칙이다.
        /// </summary>
        private static int GetBuiltPlankCount(int level)
        {
            if (level < 2)
                return 0;

            return level >= 3 ? DeckPlankCount : DeckPlankCount / 2;
        }

        /// <summary>
        /// 널이 실제로 덮은 로컬 z 구간. 널은 고물(-Z)부터 채워지므로 절반 단계에서는 앞쪽이 비어 있다.
        /// 널이 하나도 없으면 빈 구간(0,0)을 준다.
        /// </summary>
        private static void GetDeckedSpan(int level, out float minZ, out float maxZ)
        {
            int planks = GetBuiltPlankCount(level);
            if (planks <= 0)
            {
                minZ = 0f;
                maxZ = 0f;
                return;
            }

            minZ = -DeckLength * 0.5f;
            maxZ = minZ + (DeckLength / DeckPlankCount) * planks;
        }

        // ─────────────────────────────────────────────────────────────────────────
        //  외형 조립
        // ─────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// 공유 머티리얼을 한 번만 만든다. 색은 전부 StructureVisualBuilder의 팔레트 상수에서 온다
        /// (새 색을 만들지 않는다 - ArtDirection 1장). 갑판 널만 Driftwood를 밝게 민 명도 변형인데,
        /// 색상각을 바꾸지 않으므로 팔레트 밖으로 나가지 않고 "선체 통나무 / 다듬은 널"을 구분해 준다.
        /// </summary>
        private void EnsureMaterials()
        {
            if (hullWoodMaterial != null)
                return;

            hullWoodMaterial = StructureVisualBuilder.CreateColorMaterial(StructureVisualBuilder.Driftwood, "wood");
            plankWoodMaterial = StructureVisualBuilder.CreateColorMaterial(
                Color.Lerp(StructureVisualBuilder.Driftwood, StructureVisualBuilder.SalvageMarkerWhite, 0.22f), "wood");
            fiberMaterial = StructureVisualBuilder.CreateColorMaterial(StructureVisualBuilder.PalmFiber, "leaf");
            sailMaterial = StructureVisualBuilder.CreateColorMaterial(StructureVisualBuilder.SalvageMarkerWhite, "noise");
            cargoMaterial = StructureVisualBuilder.CreateColorMaterial(StructureVisualBuilder.SupplyKhaki, "metal");
        }

        /// <summary>
        /// 현재 단계의 뗏목을 통째로 다시 만든다.
        /// 기존 파츠는 Destroy 전에 SetActive(false)를 먼저 부른다 - Destroy는 프레임 끝까지 지연되므로,
        /// 같은 프레임에 새로 만드는 승선 발판 콜라이더와 옛 것이 겹쳐 있는 시간을 없앤다(AGENT_BRIEF 4장).
        /// </summary>
        private void RebuildVisual(int level)
        {
            EnsureMaterials();

            // 갑판 뿌리/건축 컨테이너는 여기서 절대 건드리지 않는다. 아래에서 지우는 것은
            // visualRoot(뗏목 자신의 파츠)뿐이고, DeckRoot는 그 형제라 재생성의 영향을 받지 않는다.
            EnsureDeckRoot();

            if (visualRoot != null)
            {
                visualRoot.gameObject.SetActive(false);
                Destroy(visualRoot.gameObject);
                visualRoot = null;
            }

            var rootObject = new GameObject("RaftVisual");
            rootObject.transform.SetParent(transform, false);
            visualRoot = rootObject.transform;

            BuildHull(level);

            if (level >= 1)
                BuildLashings();

            if (level >= 2)
                BuildDeck(level);

            if (level >= 3)
                BuildBoardingRamp();

            if (level >= 4)
                BuildMast(level);

            if (level >= 5)
                BuildRigging();

            if (level >= 4)
                BuildRailings(level);

            if (level >= 2)
                BuildCargo(level);

            ApplyHullCollider(level);

            // 갑판 콜라이더가 방금 바뀌었다. 구독자가 이 프레임에 레이캐스트를 쏠 수 있으므로
            // 물리 씬을 먼저 맞춘다(Physics.autoSyncTransforms = false - AGENT_BRIEF 4장).
            Physics.SyncTransforms();

            DeckRebuilt?.Invoke();
        }

        /// <summary>
        /// 0단계: 아직 묶지 않은 짧은 통나무 3개가 모래 위에 삐뚜름하게 놓여 있다.
        /// 1단계 이상: 통나무 6개가 폭을 꽉 채우고 가로보 2개로 고정된 뗏목 골격이 된다.
        /// </summary>
        private void BuildHull(int level)
        {
            bool framed = level >= 1;
            int logCount = framed ? 6 : 3;
            float span = framed ? DeckWidth : 2.6f;
            float logLength = framed ? DeckLength : DeckLength * 0.75f;
            float spacing = span / logCount;

            for (int i = 0; i < logCount; i++)
            {
                float x = -span * 0.5f + spacing * (i + 0.5f);

                // 0단계의 "아직 안 묶인" 느낌: 통나무마다 살짝 다른 각도/높이. 난수를 쓰지 않는다
                // (AGENT_BRIEF 2장 6번 - 전역 UnityEngine.Random 금지). 인덱스에서 결정적으로 만든다.
                float yaw = framed ? 0f : (i - 1) * 4.5f;
                float lift = framed ? 0f : Mathf.Abs(i - 1) * 0.04f;

                StructureVisualBuilder.CreateVisualPart(visualRoot, $"HullLog{i}", PrimitiveType.Cylinder,
                    new Vector3(x, LogCenterY + lift, 0f),
                    new Vector3(LogDiameter, logLength * 0.5f, LogDiameter),
                    hullWoodMaterial, Quaternion.Euler(90f, yaw, 0f));
            }

            if (!framed)
                return;

            for (int side = -1; side <= 1; side += 2)
            {
                StructureVisualBuilder.CreateVisualPart(visualRoot, $"Crossbeam{side}", PrimitiveType.Cube,
                    new Vector3(0f, CrossbeamY, side * 2.7f),
                    new Vector3(DeckWidth + 0.3f, 0.12f, 0.32f), hullWoodMaterial);
            }
        }

        /// <summary>통나무를 가로로 묶은 밧줄 띠. 통나무 윗면(0.5)을 감싸도록 얹는다.</summary>
        private void BuildLashings()
        {
            float[] lashingZ = { -3.4f, -1.2f, 1.2f, 3.4f };

            for (int i = 0; i < lashingZ.Length; i++)
            {
                StructureVisualBuilder.CreateVisualPart(visualRoot, $"Lashing{i}", PrimitiveType.Cube,
                    new Vector3(0f, 0.44f, lashingZ[i]),
                    new Vector3(DeckWidth + 0.12f, 0.14f, 0.18f), fiberMaterial);
            }
        }

        /// <summary>
        /// 갑판 널. 2단계에서는 고물 쪽 절반만 깔리고, 3단계에서 전면이 채워진다.
        /// 널 하나가 폭 전체를 가로지르므로(가로 널) 이음매가 진행 방향으로 줄지어 보인다.
        /// 개수/간격은 GetBuiltPlankCount가 단일 출처다 - DeckLocalSize도 같은 함수를 본다.
        /// </summary>
        private void BuildDeck(int level)
        {
            float pitch = DeckLength / DeckPlankCount;
            int builtPlanks = GetBuiltPlankCount(level);

            for (int i = 0; i < builtPlanks; i++)
            {
                float z = -DeckLength * 0.5f + pitch * (i + 0.5f);
                StructureVisualBuilder.CreateVisualPart(visualRoot, $"DeckPlank{i}", PrimitiveType.Cube,
                    new Vector3(0f, DeckPlankY, z),
                    new Vector3(DeckWidth, DeckPlankThickness, pitch - 0.08f), plankWoodMaterial);
            }
        }

        /// <summary>
        /// 승선 발판. CharacterController의 stepOffset은 씬 값 0.3이라 갑판(0.72)에 그냥 올라설 수 없다.
        /// 해변 실측 높이(rampFootLocalY)에서 갑판까지 이어지는 경사판을 놓고, 여기에만 콜라이더를 남긴다.
        /// slopeLimit(씬 값 45도)보다 훨씬 완만하므로 걸어서 올라갈 수 있다.
        /// </summary>
        private void BuildBoardingRamp()
        {
            float footY = Mathf.Min(rampFootLocalY, DeckSurfaceY - 0.08f);
            float rise = DeckSurfaceY - footY;
            float length = Mathf.Sqrt(RampRun * RampRun + rise * rise);
            float angle = Mathf.Atan2(rise, RampRun) * Mathf.Rad2Deg;

            var ramp = CreateSolidPart("BoardingRamp",
                new Vector3(0f, (DeckSurfaceY + footY) * 0.5f - 0.05f, -DeckLength * 0.5f - RampRun * 0.5f),
                new Vector3(1.8f, 0.12f, length), plankWoodMaterial,
                Quaternion.Euler(-angle, 0f, 0f));

            // 난간 대신 발판 양옆에 낮은 턱만 둔다(콜라이더 없음 - 시각 표시).
            for (int side = -1; side <= 1; side += 2)
            {
                StructureVisualBuilder.CreateVisualPart(ramp.transform, $"RampEdge{side}", PrimitiveType.Cube,
                    new Vector3(side * 0.46f, 0.6f, 0f), new Vector3(0.06f, 1.2f, 1f), hullWoodMaterial);
            }
        }

        /// <summary>
        /// 돛대. 4단계는 아직 짧은 임시 기둥만, 5단계에서 길어지고 활대와 돛이 붙는다.
        /// </summary>
        private void BuildMast(int level)
        {
            float mastHeight = level >= 5 ? 3.6f : 2.2f;
            const float MastZ = 0.6f;

            StructureVisualBuilder.CreateVisualPart(visualRoot, "Mast", PrimitiveType.Cube,
                new Vector3(0f, DeckSurfaceY + mastHeight * 0.5f, MastZ),
                new Vector3(0.26f, mastHeight, 0.26f), hullWoodMaterial);

            StructureVisualBuilder.CreateVisualPart(visualRoot, "MastFootLashing", PrimitiveType.Cube,
                new Vector3(0f, DeckSurfaceY + 0.18f, MastZ),
                new Vector3(0.44f, 0.15f, 0.44f), fiberMaterial);

            if (level < 5)
                return;

            float yardY = DeckSurfaceY + mastHeight - 0.25f;

            StructureVisualBuilder.CreateVisualPart(visualRoot, "Yard", PrimitiveType.Cube,
                new Vector3(0f, yardY, MastZ), new Vector3(3.2f, 0.14f, 0.14f), hullWoodMaterial);

            StructureVisualBuilder.CreateVisualPart(visualRoot, "Sail", PrimitiveType.Cube,
                new Vector3(0f, yardY - 1.15f, MastZ + 0.08f),
                new Vector3(3.0f, 2.1f, 0.06f), sailMaterial);
        }

        /// <summary>5단계 전용: 돛대를 앞뒤로 잡아 주는 밧줄 2줄 + 고물의 키(방향타).</summary>
        private void BuildRigging()
        {
            Vector3 mastTop = new Vector3(0f, DeckSurfaceY + 3.5f, 0.6f);
            BuildStay("StayAft", mastTop, new Vector3(0f, DeckSurfaceY + 0.1f, -DeckLength * 0.5f + 0.5f));
            BuildStay("StayFore", mastTop, new Vector3(0f, DeckSurfaceY + 0.1f, DeckLength * 0.5f - 0.5f));

            StructureVisualBuilder.CreateVisualPart(visualRoot, "RudderShaft", PrimitiveType.Cube,
                new Vector3(0.95f, DeckSurfaceY + 0.15f, -DeckLength * 0.5f + 0.25f),
                new Vector3(0.12f, 1.7f, 0.12f), hullWoodMaterial, Quaternion.Euler(38f, 0f, 0f));

            StructureVisualBuilder.CreateVisualPart(visualRoot, "RudderBlade", PrimitiveType.Cube,
                new Vector3(0.95f, -0.2f, -DeckLength * 0.5f - 0.6f),
                new Vector3(0.1f, 0.75f, 0.5f), hullWoodMaterial, Quaternion.Euler(38f, 0f, 0f));
        }

        /// <summary>두 점을 잇는 가는 밧줄 하나. 큐브의 로컬 +Y를 두 점 방향으로 돌려 세운다.</summary>
        private void BuildStay(string name, Vector3 from, Vector3 to)
        {
            Vector3 delta = to - from;
            float length = delta.magnitude;
            if (length < 0.01f)
                return;

            StructureVisualBuilder.CreateVisualPart(visualRoot, name, PrimitiveType.Cube,
                (from + to) * 0.5f, new Vector3(0.05f, length, 0.05f), fiberMaterial,
                Quaternion.FromToRotation(Vector3.up, delta / length));
        }

        /// <summary>
        /// 난간. 4단계는 고물 쪽 절반만, 5단계에서 뱃머리까지 이어지고 앞 난간이 닫힌다.
        /// 콜라이더를 붙이지 않는다 - 붙이면 갑판에 올라간 플레이어가 갇힌다.
        /// </summary>
        private void BuildRailings(int level)
        {
            float railY = DeckSurfaceY + 0.45f;
            float halfWidth = DeckWidth * 0.5f - 0.08f;
            float startZ = -DeckLength * 0.5f + 0.3f;
            float endZ = level >= 5 ? DeckLength * 0.5f - 0.3f : 0.4f;
            float length = endZ - startZ;

            for (int side = -1; side <= 1; side += 2)
            {
                StructureVisualBuilder.CreateVisualPart(visualRoot, $"RailBar{side}", PrimitiveType.Cube,
                    new Vector3(side * halfWidth, railY, (startZ + endZ) * 0.5f),
                    new Vector3(0.09f, 0.09f, length), plankWoodMaterial);

                const int PostCount = 4;
                for (int i = 0; i < PostCount; i++)
                {
                    float z = startZ + length * i / (PostCount - 1);
                    StructureVisualBuilder.CreateVisualPart(visualRoot, $"RailPost{side}_{i}", PrimitiveType.Cube,
                        new Vector3(side * halfWidth, DeckSurfaceY + 0.22f, z),
                        new Vector3(0.11f, 0.45f, 0.11f), plankWoodMaterial);
                }
            }

            if (level >= 5)
            {
                StructureVisualBuilder.CreateVisualPart(visualRoot, "BowRail", PrimitiveType.Cube,
                    new Vector3(0f, railY, endZ), new Vector3(DeckWidth - 0.16f, 0.09f, 0.09f), plankWoodMaterial);
            }
        }

        /// <summary>갑판 위 보급품. 2단계부터 나무 궤짝, 5단계에서 물통이 하나 더 놓인다.</summary>
        private void BuildCargo(int level)
        {
            StructureVisualBuilder.CreateVisualPart(visualRoot, "SupplyCrate", PrimitiveType.Cube,
                new Vector3(1.65f, DeckSurfaceY + 0.31f, -2.3f),
                new Vector3(0.62f, 0.62f, 0.62f), plankWoodMaterial, Quaternion.Euler(0f, 18f, 0f));

            if (level < 5)
                return;

            StructureVisualBuilder.CreateVisualPart(visualRoot, "SupplyBarrel", PrimitiveType.Cylinder,
                new Vector3(-1.65f, DeckSurfaceY + 0.35f, -2.6f),
                new Vector3(0.52f, 0.35f, 0.52f), cargoMaterial);
        }

        /// <summary>
        /// 선체 콜라이더를 현재 단계에 맞춘다. 이것이 (1) 플레이어가 올라서는 발판이자
        /// (2) InteractionController의 E키 레이캐스트가 맞는 대상이다(BoatWorkbench가 같은 오브젝트에 있다).
        /// 0단계는 통나무 3개뿐이라 폭/길이/높이를 실제 파츠 범위에 맞춰 줄인다 - 안 그러면 보이지 않는
        /// 벽에 부딪힌다.
        /// </summary>
        private void ApplyHullCollider(int level)
        {
            if (hullCollider == null)
                return;

            bool framed = level >= 1;
            bool decked = level >= 2;

            float width = framed ? DeckWidth : 2.6f;
            float length = framed ? DeckLength : DeckLength * 0.75f;
            float top = decked ? DeckSurfaceY : (framed ? CrossbeamY + 0.06f : LogCenterY + LogDiameter * 0.5f);

            hullCollider.center = new Vector3(0f, top * 0.5f, 0f);
            hullCollider.size = new Vector3(width, top, length);
        }

        /// <summary>
        /// 콜라이더를 **남기는** 큐브 파츠. StructureVisualBuilder.CreateVisualPart는 항상 콜라이더를
        /// 지우므로(시각 전용이 원칙), 실제로 밟고 올라가야 하는 승선 발판만 여기서 직접 만든다.
        /// CreatePrimitive가 붙여 주는 BoxCollider를 그대로 쓴다(지웠다가 다시 붙이면 Destroy 지연 때문에
        /// 한 프레임 동안 콜라이더가 2개가 된다).
        /// </summary>
        private GameObject CreateSolidPart(string name, Vector3 localPosition, Vector3 localScale,
            Material material, Quaternion localRotation)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(visualRoot, false);
            go.transform.localPosition = localPosition;
            go.transform.localRotation = localRotation;
            go.transform.localScale = localScale;

            var renderer = go.GetComponent<MeshRenderer>();
            if (renderer != null && material != null)
                renderer.sharedMaterial = material;

            return go;
        }
    }
}
