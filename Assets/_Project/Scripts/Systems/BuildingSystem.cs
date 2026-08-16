using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using MakeGame.Data;
using MakeGame.Player;

namespace MakeGame.Systems
{
    /// <summary>
    /// 조각을 지금 놓을 수 없는 이유. 매 프레임 문자열을 만들지 않기 위해 열거형으로 들고 있다가,
    /// UI가 화면에 띄울 때만 DescribeBlockReason으로 미리 만들어 둔 상수 문자열로 바꾼다.
    /// </summary>
    public enum BuildBlockReason
    {
        None = 0,
        /// <summary>레이가 지형에도 조각에도 닿지 않았다(허공/너무 멀다).</summary>
        NoTarget = 1,
        /// <summary>그 칸/모서리에 이미 조각이 있다.</summary>
        Occupied = 2,
        /// <summary>벽·문·창문을 받쳐 줄 바닥 조각이 없다.</summary>
        NoSupportingFloor = 3,
        /// <summary>지형이 조각을 뚫고 올라오거나(파묻힘) 너무 크게 떠 있다.</summary>
        GroundTooUneven = 4,
        /// <summary>땅이 아니다(바다 위 등). 이번 배치는 땅 위에만 짓는다.</summary>
        NotOnGround = 5,
        /// <summary>재료가 모자란다.</summary>
        NotEnoughMaterials = 6,
    }

    /// <summary>
    /// 건축 모드의 배치·스냅·미리보기·철거를 담당하는 코어 시스템.
    ///
    /// 역할 분담: **부품의 겉모습과 재료표는 이 파일이 갖고 있지 않다.** 형상은
    /// <see cref="BuildPieceVisualBuilder"/>, 재료·표시 이름은 <see cref="BuildPieceCatalog"/>가 단일
    /// 소스이고, 이 파일은 "어디에 놓을 수 있는가 / 놓아도 되는가 / 무엇을 돌려주는가"만 판단한다.
    ///
    /// 격자 규약(고정):
    /// · 원점은 월드 (0,0,0), 셀 한 변 <see cref="BuildPieceCatalog.CellSize"/>(2m), 한 층
    ///   <see cref="BuildPieceCatalog.LevelHeight"/>(2.5m).
    /// · 셀 (cx,cz)는 월드 X [cx*2, cx*2+2), Z [cz*2, cz*2+2) 구간이고 중심은 ((cx+0.5)*2, (cz+0.5)*2).
    /// · 바닥은 셀 중심에 놓이며 **로컬 원점이 윗면**이라 position.y가 곧 사람이 딛는 높이다.
    /// · 벽/문/창문은 셀 모서리에 놓인다. 모서리는 (ex, ez, axis)로 canonical하게 표기한다 -
    ///   axis 0 = z가 일정한(= X축을 따라 뻗은) 모서리, axis 1 = x가 일정한 모서리. 이렇게 하면
    ///   맞닿은 두 셀이 같은 모서리를 서로 다른 키로 잡는 일이 없다(같은 자리에 벽이 두 장 겹치는 사고).
    ///
    /// 씬에 인스턴스가 없다(씬 파일을 편집할 수 없다). DayNightCycle·QuestUI와 같은
    /// RuntimeInitializeOnLoadMethod + sceneLoaded 패턴으로 씬 로드마다 스스로 생성되므로
    /// **여기 적힌 코드 기본값이 유일한 진실**이다(AGENT_BRIEF 3장의 "씬에 없는 컴포넌트" 규칙).
    /// </summary>
    public class BuildingSystem : MonoBehaviour
    {
        // ────────────────────────────────────────────────────────────────────────
        // 설정 (코드 기본값이 유일한 소스)
        // ────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// 건축 모드 토글 키. B는 이 프로젝트에서 비어 있다(전수 grep 결과 사용 중인 키는
        /// C E F G J M R V Tab Space Escape F3 F5 F6 F7 F8 F9 +/- Shift Ctrl 뿐이다).
        /// </summary>
        public KeyCode toggleKey = KeyCode.B;

        /// <summary>
        /// 90도 회전 키. **감독 지시의 R은 쓸 수 없다** - InteractionController.cookKey(조리)가
        /// 이미 R이고, 건축 중에도 조리 입력은 살아 있으므로 같은 프레임에 두 동작이 함께 난다.
        /// Q는 전수 grep에서 어디에도 쓰이지 않는다. 마우스 휠도 함께 받는다(휠은 프로젝트 전체에서
        /// 아무도 쓰지 않는다 - MinimapUI의 확대/축소는 +/- 키다).
        /// </summary>
        public KeyCode rotateKey = KeyCode.Q;

        /// <summary>조각을 놓을 수 있는 최대 거리(m). 상호작용 거리(4m)보다 넉넉해야 벽 한 장 너머가 잡힌다.</summary>
        public float buildDistance = 8f;

        // ── 격자 상수 ───────────────────────────────────────────────────────────
        private const float CellSize = BuildPieceCatalog.CellSize;
        private const float LevelHeight = BuildPieceCatalog.LevelHeight;

        /// <summary>지형으로 인정하는 콜라이더 이름 접두사. TerrainSampler와 같은 규칙이다.</summary>
        private const string TerrainNamePrefix = "Island_";

        /// <summary>
        /// 바닥 윗면을 조준했을 때 "가장자리 띠"로 보는 비율(셀 반폭 기준). 안쪽을 보면 위로 쌓고,
        /// 이 띠를 보면 옆 칸으로 잇는다. 이 규칙이 있어야 2층 바닥을 옆으로 넓힐 수 있다(2층 옆
        /// 허공에는 레이가 맞을 콜라이더가 없어서, 조준점만으로는 층을 알 수 없기 때문이다).
        /// </summary>
        private const float EdgeBand = 0.7f;

        /// <summary>지형이 바닥 윗면보다 이만큼 넘게 솟아 있으면 "파묻힘"으로 보고 막는다(m).</summary>
        private const float BuriedTolerance = 0.45f;

        /// <summary>
        /// 지면에 처음 놓는 바닥이 허용하는 네 모서리 높이차 상한(m). 2m 폭에서 0.6m면 약 17도 경사이고,
        /// 그보다 가파른 곳에는 못 짓는다(한쪽이 땅에 처박히거나 반대쪽이 허공에 뜬다).
        /// </summary>
        private const float GroundFlatnessTolerance = 0.6f;

        /// <summary>지면에 놓는 바닥을 지면보다 살짝 띄우는 값(m). Z-파이팅 방지용.</summary>
        private const float GroundClearance = 0.02f;

        /// <summary>실내 판정 탐색 상한(칸). 이보다 넓은 영역은 "방"이 아니라 야외 데크로 본다.</summary>
        private const int MaxEnclosureCells = 64;

        // ────────────────────────────────────────────────────────────────────────
        // 상태
        // ────────────────────────────────────────────────────────────────────────

        /// <summary>이 씬의 건축 시스템(씬 리로드마다 새 인스턴스로 교체된다).</summary>
        public static BuildingSystem Instance { get; private set; }

        /// <summary>모드/선택/조각 수가 바뀌었을 때 발행된다. UI가 매 프레임 다시 그리지 않게 하는 통로다.</summary>
        public event System.Action Changed;

        /// <summary>놓여 있는 조각 하나의 등록 정보.</summary>
        private class PlacedPiece
        {
            public BuildPieceType type;
            public GameObject go;
            public Transform root;
            public int cellX;      // 바닥: 셀 좌표 · 벽류: 모서리 좌표
            public int cellZ;
            public int level;      // 양자화된 층 번호(= Round(y / LevelHeight))
            public int axis;       // 벽류 전용(0 = X축을 따라 뻗은 모서리, 1 = Z축)
            public Vector3 position;
            public float yaw;
        }

        private readonly List<PlacedPiece> pieces = new List<PlacedPiece>();
        private readonly Dictionary<long, PlacedPiece> floorByKey = new Dictionary<long, PlacedPiece>();
        private readonly Dictionary<long, PlacedPiece> wallByKey = new Dictionary<long, PlacedPiece>();

        /// <summary>레이가 맞은 콜라이더에서 조각 본체를 거슬러 찾기 위한 표(루트 Transform → 조각).</summary>
        private readonly Dictionary<Transform, PlacedPiece> pieceByRoot = new Dictionary<Transform, PlacedPiece>();

        private Transform piecesRoot;
        private Transform ghostRoot;

        private GameObject ghost;
        private BuildPieceType ghostType;
        private bool ghostValid = true;

        private bool buildMode;
        private BuildPieceType selectedType = BuildPieceType.Floor;
        private int rotationSteps;

        // 이번 프레임의 조준 결과.
        private bool hasTarget;
        private bool targetValid;
        private BuildBlockReason blockReason = BuildBlockReason.NoTarget;
        private Vector3 targetPosition;
        private float targetYaw;
        private int targetCellX;
        private int targetCellZ;
        private int targetLevel;
        private int targetAxis;

        private Camera cachedCamera;
        private PlayerInventory cachedInventory;

        /// <summary>조각이 놓이거나 부서질 때마다 올라간다. 실내 판정 캐시를 통째로 버리는 기준이다.</summary>
        private int structureVersion;

        private readonly Dictionary<long, bool> enclosureCache = new Dictionary<long, bool>();
        private int enclosureCacheVersion = -1;

        // 재사용 버퍼(매 프레임 new 금지).
        private static readonly RaycastHit[] rayBuffer = new RaycastHit[32];
        private static readonly RaycastHit[] groundBuffer = new RaycastHit[32];
        private readonly List<int> bfsCellX = new List<int>();
        private readonly List<int> bfsCellZ = new List<int>();
        private readonly HashSet<long> bfsVisited = new HashSet<long>();
        private readonly List<BuildPieceCost> refundBuffer = new List<BuildPieceCost>();

        private Dictionary<string, ItemData> itemByName;

        // ────────────────────────────────────────────────────────────────────────
        // 공개 조회 (UI 전용 - 상태를 바꾸지 않는다)
        // ────────────────────────────────────────────────────────────────────────

        public bool IsBuildModeOn => buildMode;
        public BuildPieceType SelectedType => selectedType;
        public BuildBlockReason BlockReason => blockReason;
        public bool CanPlaceNow => hasTarget && targetValid;
        public int PieceCount => pieces.Count;

        /// <summary>이번 배치에서 실제로 지을 수 있는 부품인지(계단은 다음 배치라 잠겨 있다).</summary>
        public static bool IsTypeUnlocked(BuildPieceType type)
        {
            return type != BuildPieceType.Stair;
        }

        /// <summary>플레이어 인벤토리(없으면 null). 매 호출 씬 전체를 훑지 않도록 캐시한다.</summary>
        public PlayerInventory Inventory
        {
            get
            {
                if (cachedInventory == null)
                    cachedInventory = FindAnyObjectByType<PlayerInventory>();
                return cachedInventory;
            }
        }

        /// <summary>
        /// 인벤토리에 같은 이름의 아이템이 몇 개 있는지 센다(ItemData 참조가 아니라 이름으로 대조).
        /// 재료표가 이름만 들고 있어서(BuildPieceCost.itemName) 이름 대조가 유일한 경로다.
        /// </summary>
        public int CountOwned(string itemName)
        {
            PlayerInventory inventory = Inventory;
            if (inventory == null || inventory.items == null || string.IsNullOrEmpty(itemName))
                return 0;

            int count = 0;
            for (int i = 0; i < inventory.items.Count; i++)
            {
                InventoryItem item = inventory.items[i];
                if (item != null && item.data != null && item.data.itemName == itemName)
                    count++;
            }
            return count;
        }

        /// <summary>이 부품의 재료를 전부 들고 있는지 확인한다(소모하지 않는다).</summary>
        public bool HasMaterialsFor(BuildPieceType type)
        {
            IReadOnlyList<BuildPieceCost> cost = BuildPieceCatalog.GetCost(type);
            if (cost == null)
                return true;

            for (int i = 0; i < cost.Count; i++)
            {
                BuildPieceCost entry = cost[i];
                if (string.IsNullOrEmpty(entry.itemName) || entry.count <= 0)
                    continue;
                if (CountOwned(entry.itemName) < entry.count)
                    return false;
            }
            return true;
        }

        /// <summary>UI에 띄울 사유 문구. 미리 만들어 둔 상수만 돌려주므로 매 프레임 불러도 안전하다.</summary>
        public static string DescribeBlockReason(BuildBlockReason reason)
        {
            switch (reason)
            {
                case BuildBlockReason.None: return "설치 가능";
                case BuildBlockReason.Occupied: return "이미 조각이 있다";
                case BuildBlockReason.NoSupportingFloor: return "받쳐 줄 바닥이 없다";
                case BuildBlockReason.GroundTooUneven: return "지면이 고르지 않다";
                case BuildBlockReason.NotOnGround: return "땅 위가 아니다";
                case BuildBlockReason.NotEnoughMaterials: return "재료가 모자란다";
                default: return "놓을 자리를 찾는 중";
            }
        }

        // ────────────────────────────────────────────────────────────────────────
        // 수명 주기
        // ────────────────────────────────────────────────────────────────────────

        /// <summary>씬이 로드될 때마다 새 BuildingSystem을 만든다(DayNightCycle·QuestUI와 같은 패턴).</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Bootstrap()
        {
            SceneManager.sceneLoaded += (scene, mode) =>
            {
                var go = new GameObject("BuildingSystem");
                go.AddComponent<BuildingSystem>();
            };
        }

        private void Awake()
        {
            // Start가 아니라 Awake에서 잡는다 - BuildMenuUI도 런타임 생성이라 실행 순서가 보장되지
            // 않는데(AGENT_BRIEF 4장), 그쪽 Start가 먼저 돌아도 Instance가 이미 있어야 한다.
            Instance = this;

            var piecesGo = new GameObject("Pieces");
            piecesGo.transform.SetParent(transform, false);
            piecesRoot = piecesGo.transform;

            var ghostGo = new GameObject("Ghost");
            ghostGo.transform.SetParent(transform, false);
            ghostRoot = ghostGo.transform;
        }

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        private void Update()
        {
            // 엔딩/사망 화면은 Time.timeScale을 0으로 세운다(AGENT_BRIEF 4장). 그 동안에는 입력을
            // 아예 받지 않는다 - 죽은 화면 뒤에서 집이 지어지면 안 된다.
            if (Time.timeScale <= 0f)
                return;

            if (Input.GetKeyDown(toggleKey))
                SetBuildMode(!buildMode);

            if (!buildMode)
                return;

            HandleRotationInput();

            ResolveTarget();
            UpdateGhost();

            if (Input.GetMouseButtonDown(0) && !IsPointerOverUI())
                TryPlace();
            else if (Input.GetMouseButtonDown(1) && !IsPointerOverUI())
                TryDemolish();
        }

        /// <summary>휠/회전키로 90도씩 돌린다. 회전의 의미는 부품 종류마다 다르다(GetYawFor 참고).</summary>
        private void HandleRotationInput()
        {
            // Input.mouseScrollDelta는 InputManager의 축 정의에 의존하지 않는다(GetAxis("Mouse
            // ScrollWheel")과 달리 축이 지워져 있어도 예외가 나지 않는다). ProjectSettings는 이번
            // 스테이징에 없어 축 정의를 확인할 수 없으므로 확인이 필요 없는 쪽을 쓴다.
            float wheel = Input.mouseScrollDelta.y;
            if (wheel > 0.01f)
                Rotate(1);
            else if (wheel < -0.01f)
                Rotate(-1);
            else if (Input.GetKeyDown(rotateKey))
                Rotate(1);
        }

        private void Rotate(int steps)
        {
            rotationSteps = ((rotationSteps + steps) % 4 + 4) % 4;
        }

        // ────────────────────────────────────────────────────────────────────────
        // 모드 / 선택
        // ────────────────────────────────────────────────────────────────────────

        public void SetBuildMode(bool on)
        {
            if (buildMode == on)
                return;

            buildMode = on;
            if (!on)
            {
                DestroyGhost();
                hasTarget = false;
                targetValid = false;
                blockReason = BuildBlockReason.NoTarget;
            }

            Changed?.Invoke();
        }

        /// <summary>부품을 고른다. 잠긴 부품(계단)은 무시한다.</summary>
        public void SelectType(BuildPieceType type)
        {
            if (!IsTypeUnlocked(type) || selectedType == type)
                return;

            selectedType = type;
            rotationSteps = 0;
            Changed?.Invoke();
        }

        // ────────────────────────────────────────────────────────────────────────
        // 조준 / 스냅
        // ────────────────────────────────────────────────────────────────────────

        private void ResolveTarget()
        {
            hasTarget = false;
            targetValid = false;
            blockReason = BuildBlockReason.NoTarget;

            Camera cam = GetCamera();
            if (cam == null)
                return;

            Transform camTransform = cam.transform;
            Ray ray = new Ray(camTransform.position, camTransform.forward);

            if (selectedType == BuildPieceType.Floor)
                ResolveFloorTarget(ray);
            else
                ResolveWallTarget(ray);

            // 재료 검사는 자리 판정이 끝난 뒤에 한 번만 한다(자리가 없으면 재료를 셀 필요도 없다).
            if (hasTarget && targetValid && !HasMaterialsFor(selectedType))
            {
                targetValid = false;
                blockReason = BuildBlockReason.NotEnoughMaterials;
            }
        }

        /// <summary>바닥 조각의 놓을 자리를 정한다(지면 스냅 · 옆으로 잇기 · 위로 쌓기).</summary>
        private void ResolveFloorTarget(Ray ray)
        {
            if (!CastBuildRay(ray, out RaycastHit hit, out PlacedPiece piece))
                return;

            int cellX;
            int cellZ;
            float topY;
            bool placingOnGround;

            if (piece != null && piece.type == BuildPieceType.Floor && hit.normal.y > 0.5f)
            {
                // 이미 있는 바닥의 윗면을 보고 있다. 안쪽이면 위층, 가장자리면 옆 칸.
                float dx = hit.point.x - CellCenterCoord(piece.cellX);
                float dz = hit.point.z - CellCenterCoord(piece.cellZ);
                cellX = piece.cellX;
                cellZ = piece.cellZ;

                if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dz)) > CellSize * 0.5f * EdgeBand)
                {
                    if (Mathf.Abs(dx) >= Mathf.Abs(dz))
                        cellX += dx > 0f ? 1 : -1;
                    else
                        cellZ += dz > 0f ? 1 : -1;

                    topY = piece.position.y;          // 같은 층을 옆으로 잇는다
                }
                else
                {
                    topY = piece.position.y + LevelHeight; // 위로 한 층 쌓는다
                }

                placingOnGround = false;
            }
            else
            {
                cellX = CellIndexOf(hit.point.x);
                cellZ = CellIndexOf(hit.point.z);

                // 옆 칸에 이미 바닥이 있으면 그 높이에 맞춘다 - 지면이 울퉁불퉁해도 데크가 평평해진다.
                PlacedPiece neighbor = FindNeighborFloorNear(cellX, cellZ, hit.point.y);
                if (neighbor != null)
                {
                    topY = neighbor.position.y;
                    placingOnGround = false;
                }
                else if (piece == null)
                {
                    topY = hit.point.y;               // 지형에 직접 놓는다
                    placingOnGround = true;
                }
                else
                {
                    // 벽 옆면 등을 봤는데 붙일 바닥도 없다.
                    blockReason = BuildBlockReason.NoTarget;
                    return;
                }
            }

            // 지형 검사는 자리를 확정하기 **전에** 한다. 지면에 처음 놓는 바닥은 네 모서리 중 가장 높은
            // 지면에 윗면을 맞춰야 어느 구석도 땅에 처박히지 않는다(그래서 topY가 여기서 바뀔 수 있다).
            bool groundFound = TryGetCellGround(cellX, cellZ, topY, out float maxGround, out float minGround);

            hasTarget = true;
            targetCellX = cellX;
            targetCellZ = cellZ;
            targetAxis = 0;
            targetYaw = GetYawFor(BuildPieceType.Floor, 0);

            if (!groundFound)
            {
                // 이번 배치는 땅 위에만 짓는다(감독 결정). 이 검사가 없으면 바닥을 한 칸씩 이어 붙여
                // 바다 위로 다리를 놓을 수 있다 - 뗏목 갑판 위 건축은 다음 배치다.
                targetLevel = LevelOf(topY);
                targetPosition = new Vector3(CellCenterCoord(cellX), topY, CellCenterCoord(cellZ));
                blockReason = BuildBlockReason.NotOnGround;
                return;
            }

            if (placingOnGround)
                topY = maxGround + GroundClearance;

            targetLevel = LevelOf(topY);
            targetPosition = new Vector3(CellCenterCoord(cellX), topY, CellCenterCoord(cellZ));

            if (floorByKey.ContainsKey(PieceKey(cellX, cellZ, targetLevel, 0)))
            {
                blockReason = BuildBlockReason.Occupied;
                return;
            }

            if (placingOnGround && maxGround - minGround > GroundFlatnessTolerance)
            {
                blockReason = BuildBlockReason.GroundTooUneven;
                return;
            }

            if (!placingOnGround && maxGround > topY + BuriedTolerance)
            {
                blockReason = BuildBlockReason.GroundTooUneven;
                return;
            }

            targetValid = true;
            blockReason = BuildBlockReason.None;
        }

        /// <summary>벽/문/창문의 놓을 자리를 정한다. **바닥 조각의 네 모서리에만 붙는다.**</summary>
        private void ResolveWallTarget(Ray ray)
        {
            if (!CastBuildRay(ray, out RaycastHit hit, out PlacedPiece _))
                return;

            Vector3 point = hit.point;
            int centerX = CellIndexOf(point.x);
            int centerZ = CellIndexOf(point.z);

            PlacedPiece bestFloor = null;
            float bestSqr = float.MaxValue;
            int bestEdgeX = 0;
            int bestEdgeZ = 0;
            int bestAxis = 0;

            // 조준점 주변 3x3 칸의 바닥을 훑어 가장 가까운 모서리를 고른다. 조준점이 벽 위라 셀
            // 경계에 딱 걸려도(어느 쪽 셀로 반올림될지 모르는 상황) 옆 칸 바닥이 후보에 들어오므로
            // "벽을 보고 있는데 붙일 데가 없다"가 되지 않는다.
            for (int ox = -1; ox <= 1; ox++)
            {
                for (int oz = -1; oz <= 1; oz++)
                {
                    int cx = centerX + ox;
                    int cz = centerZ + oz;
                    PlacedPiece floor = FindFloorNear(cx, cz, point.y);
                    if (floor == null)
                        continue;

                    for (int side = 0; side < 4; side++)
                    {
                        GetEdgeOfCell(cx, cz, side, out int ex, out int ez, out int axis);
                        Vector3 mid = EdgeMidpoint(ex, ez, axis, floor.position.y);

                        // 벽 높이의 절반쯤을 기준으로 재야 "벽 위쪽을 겨눴을 때"도 그 모서리가 이긴다.
                        mid.y += LevelHeight * 0.5f;
                        float sqr = (mid - point).sqrMagnitude;
                        if (sqr >= bestSqr)
                            continue;

                        bestSqr = sqr;
                        bestFloor = floor;
                        bestEdgeX = ex;
                        bestEdgeZ = ez;
                        bestAxis = axis;
                    }
                }
            }

            if (bestFloor == null)
            {
                blockReason = BuildBlockReason.NoSupportingFloor;
                return;
            }

            hasTarget = true;
            targetCellX = bestEdgeX;
            targetCellZ = bestEdgeZ;
            targetLevel = bestFloor.level;
            targetAxis = bestAxis;
            targetPosition = EdgeMidpoint(bestEdgeX, bestEdgeZ, bestAxis, bestFloor.position.y);
            targetYaw = GetYawFor(selectedType, bestAxis);

            if (wallByKey.ContainsKey(PieceKey(bestEdgeX, bestEdgeZ, bestFloor.level, bestAxis)))
            {
                blockReason = BuildBlockReason.Occupied;
                return;
            }

            targetValid = true;
            blockReason = BuildBlockReason.None;
        }

        /// <summary>
        /// 부품의 최종 y 회전. 벽류는 모서리 방향이 회전을 결정하므로(그래야 모서리에 정확히 맞는다)
        /// 회전 입력은 앞뒤 뒤집기(180도)로만 쓴다. 바닥은 정사각형이라 90도 회전이 겉모습 문제다.
        /// </summary>
        private float GetYawFor(BuildPieceType type, int axis)
        {
            if (type == BuildPieceType.Floor)
                return rotationSteps * 90f;

            float baseYaw = axis == 0 ? 0f : 90f;
            return baseYaw + (rotationSteps % 2) * 180f;
        }

        /// <summary>
        /// 지형/조각만 걸러서 가장 가까운 히트를 돌려준다. 초목·자원 노드·사냥감·플레이어는 통과시킨다
        /// (TerrainSampler가 "Island_" 접두사만 지형으로 인정하는 것과 같은 이유 - 콜라이더가 붙은
        /// 장식물에 조준이 걸리면 배치 높이가 통째로 틀어진다).
        /// </summary>
        private bool CastBuildRay(Ray ray, out RaycastHit bestHit, out PlacedPiece bestPiece)
        {
            bestHit = default;
            bestPiece = null;

            int count = Physics.RaycastNonAlloc(ray, rayBuffer, buildDistance);
            bool found = false;
            float bestDistance = float.MaxValue;

            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = rayBuffer[i];
                Collider collider = hit.collider;
                if (collider == null)
                    continue;

                PlacedPiece piece = FindPieceOf(collider.transform);
                bool isTerrain = piece == null
                    && collider.gameObject.name.StartsWith(TerrainNamePrefix, System.StringComparison.Ordinal);

                if (piece == null && !isTerrain)
                    continue;

                if (hit.distance >= bestDistance)
                    continue;

                bestDistance = hit.distance;
                bestHit = hit;
                bestPiece = piece;
                found = true;
            }

            return found;
        }

        /// <summary>콜라이더가 붙은 자식에서 조각 본체까지 부모를 거슬러 올라간다(할당 없음).</summary>
        private PlacedPiece FindPieceOf(Transform t)
        {
            while (t != null)
            {
                if (pieceByRoot.TryGetValue(t, out PlacedPiece piece))
                    return piece;
                t = t.parent;
            }
            return null;
        }

        /// <summary>
        /// 이 셀에서 y에 가장 가까운 바닥을 찾는다. 지면에 놓인 바닥은 y가 정수배가 아니므로
        /// 양자화 층 번호가 한 칸 어긋날 수 있어 L-1 · L · L+1 세 층을 본다.
        /// </summary>
        private PlacedPiece FindFloorNear(int cellX, int cellZ, float y)
        {
            int level = LevelOf(y);
            PlacedPiece best = null;
            float bestDelta = float.MaxValue;

            for (int d = -1; d <= 1; d++)
            {
                if (!floorByKey.TryGetValue(PieceKey(cellX, cellZ, level + d, 0), out PlacedPiece floor))
                    continue;

                // 조준점보다 조금이라도 위에 있는 바닥은 "딛고 선 바닥"이 아니다(위층 바닥에 벽을
                // 붙이려면 그 위층을 조준해야 한다).
                if (floor.position.y > y + 0.6f)
                    continue;

                float delta = Mathf.Abs(floor.position.y - y);
                if (delta >= bestDelta)
                    continue;

                bestDelta = delta;
                best = floor;
            }

            return best;
        }

        /// <summary>맞닿은 네 칸 중 이 높이에 가장 가까운 바닥(데크 평탄화용).</summary>
        private PlacedPiece FindNeighborFloorNear(int cellX, int cellZ, float y)
        {
            PlacedPiece best = null;
            float bestDelta = float.MaxValue;

            for (int side = 0; side < 4; side++)
            {
                GetNeighborCell(cellX, cellZ, side, out int nx, out int nz);

                int level = LevelOf(y);
                for (int d = -1; d <= 1; d++)
                {
                    if (!floorByKey.TryGetValue(PieceKey(nx, nz, level + d, 0), out PlacedPiece floor))
                        continue;

                    float delta = Mathf.Abs(floor.position.y - y);
                    if (delta > LevelHeight * 0.8f || delta >= bestDelta)
                        continue;

                    bestDelta = delta;
                    best = floor;
                }
            }

            return best;
        }

        /// <summary>
        /// 한 칸의 네 모서리 지면 높이를 재서 가장 높은 곳과 낮은 곳을 돌려준다(레이 4번).
        /// 네 모서리 중 하나도 지형에 닿지 않으면 false - 그 칸 밑에는 섬이 없다는 뜻이다.
        /// </summary>
        private static bool TryGetCellGround(int cellX, int cellZ, float aroundY, out float maxGround, out float minGround)
        {
            float half = CellSize * 0.5f * 0.9f; // 셀 안쪽으로 조금 당겨서 잰다(바로 옆 절벽에 걸리지 않게)
            float centerX = CellCenterCoord(cellX);
            float centerZ = CellCenterCoord(cellZ);

            int found = 0;
            maxGround = float.MinValue;
            minGround = float.MaxValue;

            for (int i = 0; i < 4; i++)
            {
                float sx = centerX + ((i & 1) == 0 ? -half : half);
                float sz = centerZ + ((i & 2) == 0 ? -half : half);
                if (!TryGetGroundHeight(sx, sz, aroundY, out float groundY))
                    continue;

                found++;
                if (groundY > maxGround) maxGround = groundY;
                if (groundY < minGround) minGround = groundY;
            }

            // **네 모서리가 전부** 지형에 닿아야 한다. 하나라도 비면 그 칸은 물가 밖으로 절반쯤
            // 걸쳐 있다는 뜻이고, 그대로 놓으면 바다 위로 튀어나온 데크가 된다.
            if (found == 4)
                return true;

            maxGround = 0f;
            minGround = 0f;
            return false;
        }

        /// <summary>
        /// 그 XZ의 지형 높이를 잰다. TerrainSampler.SnapToGround와 **같은 규칙**(이름이 "Island_"로
        /// 시작하는 콜라이더만 지형)이지만, 그쪽은 못 찾았을 때 입력 위치를 그대로 돌려줘서 성공/실패를
        /// 구분할 수 없다. 바다 위 배치를 막으려면 실패 여부가 필요해 여기서 따로 쏜다.
        /// </summary>
        private static bool TryGetGroundHeight(float x, float z, float aroundY, out float groundY)
        {
            groundY = 0f;
            Vector3 origin = new Vector3(x, aroundY + 60f, z);
            int count = Physics.RaycastNonAlloc(origin, Vector3.down, groundBuffer, 120f);

            bool found = false;
            float closest = float.MaxValue;

            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = groundBuffer[i];
                if (hit.collider == null)
                    continue;
                if (!hit.collider.gameObject.name.StartsWith(TerrainNamePrefix, System.StringComparison.Ordinal))
                    continue;
                if (hit.distance >= closest)
                    continue;

                closest = hit.distance;
                groundY = hit.point.y;
                found = true;
            }

            return found;
        }

        // ────────────────────────────────────────────────────────────────────────
        // 미리보기(고스트)
        // ────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// 고스트는 **타입이 바뀔 때만** 다시 만든다. 위치/회전은 옮기기만 하고, 유효/무효는
        /// SetGhostValid로 색만 바꾼다(매 프레임 오브젝트를 만들면 GC와 SRP 배처가 함께 죽는다).
        /// </summary>
        private void UpdateGhost()
        {
            if (!hasTarget)
            {
                if (ghost != null && ghost.activeSelf)
                    ghost.SetActive(false);
                return;
            }

            EnsureGhost();
            if (ghost == null)
                return;

            if (!ghost.activeSelf)
                ghost.SetActive(true);

            ghost.transform.SetPositionAndRotation(targetPosition, Quaternion.Euler(0f, targetYaw, 0f));

            if (ghostValid != targetValid)
            {
                BuildPieceVisualBuilder.SetGhostValid(ghost, targetValid);
                ghostValid = targetValid;
            }
        }

        private void EnsureGhost()
        {
            if (ghost != null && ghostType == selectedType)
                return;

            DestroyGhost();

            ghost = BuildPieceVisualBuilder.CreateGhost(selectedType, ghostRoot, targetValid);
            ghostType = selectedType;
            ghostValid = targetValid;
        }

        private void DestroyGhost()
        {
            if (ghost == null)
                return;

            // Destroy는 프레임 끝까지 지연된다(AGENT_BRIEF 4장). 먼저 꺼서 이번 프레임에 새로 만드는
            // 고스트와 겹쳐 보이지 않게 한다.
            ghost.SetActive(false);
            Destroy(ghost);
            ghost = null;
        }

        // ────────────────────────────────────────────────────────────────────────
        // 설치
        // ────────────────────────────────────────────────────────────────────────

        /// <summary>지금 조준한 자리에 조각을 세운다. 유효하지 않으면 아무것도 하지 않는다.</summary>
        public void TryPlace()
        {
            if (!hasTarget || !targetValid)
            {
                AudioManager.Instance?.PlayActionFail();
                return;
            }

            // **순서가 곧 안전장치다.** 실물을 먼저 만들고, 성공한 뒤에야 재료를 지운다.
            // (이 프로젝트에서 아이템이 증발한 사고가 네 번 있었고 전부 반대 순서였다.)
            GameObject go = BuildPieceVisualBuilder.CreateSolid(selectedType, piecesRoot);
            if (go == null)
            {
                Debug.LogWarning($"[BuildingSystem] '{BuildPieceCatalog.GetDisplayName(selectedType)}' 실물 생성에 " +
                    "실패해 설치를 취소했다. 재료는 소모하지 않았다.");
                AudioManager.Instance?.PlayActionFail();
                return;
            }

            go.transform.SetPositionAndRotation(targetPosition, Quaternion.Euler(0f, targetYaw, 0f));

            if (!ConsumeCost(selectedType))
            {
                // 여기까지 오면 안 된다(ResolveTarget에서 이미 걸렀다). 그래도 왔다면 방금 만든 실물을
                // 되돌려서 "재료는 남고 조각도 없다"는 안전한 상태로 끝낸다.
                go.SetActive(false);
                Destroy(go);
                Debug.LogWarning("[BuildingSystem] 재료 소모에 실패해 방금 만든 조각을 되돌렸다.");
                AudioManager.Instance?.PlayActionFail();
                return;
            }

            RegisterPiece(selectedType, go, targetCellX, targetCellZ, targetLevel, targetAxis, targetPosition, targetYaw);

            // Physics.autoSyncTransforms는 꺼져 있다(AGENT_BRIEF 4장). 방금 만든 콜라이더에 다음
            // 프레임 레이캐스트가 맞으려면 여기서 물리 씬에 반영해야 한다.
            Physics.SyncTransforms();

            AudioManager.Instance?.PlayCraftSuccess();
            Changed?.Invoke();
        }

        /// <summary>
        /// 재료를 실제로 소모한다. **전부 있는지 먼저 확인한 뒤에 지운다** - 중간에 모자라면 이미 지운
        /// 재료를 되돌릴 방법이 없다(Shelter.ConsumeMaterials와 같은 규칙).
        /// </summary>
        private bool ConsumeCost(BuildPieceType type)
        {
            IReadOnlyList<BuildPieceCost> cost = BuildPieceCatalog.GetCost(type);
            if (cost == null || cost.Count == 0)
                return true;

            PlayerInventory inventory = Inventory;
            if (inventory == null || inventory.items == null)
                return false;

            for (int i = 0; i < cost.Count; i++)
            {
                BuildPieceCost entry = cost[i];
                if (string.IsNullOrEmpty(entry.itemName) || entry.count <= 0)
                    continue;
                if (CountOwned(entry.itemName) < entry.count)
                    return false;
            }

            for (int i = 0; i < cost.Count; i++)
            {
                BuildPieceCost entry = cost[i];
                if (string.IsNullOrEmpty(entry.itemName) || entry.count <= 0)
                    continue;

                int remaining = entry.count;
                for (int k = inventory.items.Count - 1; k >= 0 && remaining > 0; k--)
                {
                    InventoryItem item = inventory.items[k];
                    if (item == null || item.data == null || item.data.itemName != entry.itemName)
                        continue;

                    inventory.items.RemoveAt(k);
                    remaining--;
                }
            }

            inventory.NotifyInventoryChanged();
            return true;
        }

        // ────────────────────────────────────────────────────────────────────────
        // 철거
        // ────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// 조준한 조각을 부수고 재료의 **절반(내림)** 을 돌려준다.
        /// · 인벤토리가 가득 차 돌려줄 수 없으면 철거 자체를 취소한다(아이템 증발 금지).
        /// · 위에 다른 조각이 얹혀 있는 바닥은 부술 수 없다(공중에 뜬 벽이 생긴다).
        /// </summary>
        public void TryDemolish()
        {
            Camera cam = GetCamera();
            if (cam == null)
                return;

            Transform camTransform = cam.transform;
            Ray ray = new Ray(camTransform.position, camTransform.forward);
            if (!CastBuildRay(ray, out RaycastHit _, out PlacedPiece piece) || piece == null)
            {
                AudioManager.Instance?.PlayActionFail();
                return;
            }

            if (piece.type == BuildPieceType.Floor && HasLoadAbove(piece))
            {
                Debug.LogWarning("[BuildingSystem] 이 바닥 위에 얹힌 조각이 있어 부술 수 없다. 위쪽부터 철거하라.");
                AudioManager.Instance?.PlayActionFail();
                return;
            }

            if (!CollectRefund(piece.type, refundBuffer))
            {
                Debug.LogWarning("[BuildingSystem] 돌려줄 재료의 ItemData를 찾지 못해 철거를 취소했다" +
                    " (ItemDataRegistry 미배치 가능성).");
                AudioManager.Instance?.PlayActionFail();
                return;
            }

            if (!CanAcceptRefund(refundBuffer))
            {
                Debug.LogWarning("[BuildingSystem] 인벤토리가 가득 차 철거를 취소했다. 반환 재료가 사라지지 않게 하는 조치다.");
                AudioManager.Instance?.PlayActionFail();
                return;
            }

            UnregisterPiece(piece);

            if (piece.go != null)
            {
                // 먼저 끄고 파괴한다 - Destroy는 프레임 끝까지 지연되므로, 그 사이 같은 프레임에
                // 다시 조준/배치하면 이미 없어진 조각의 콜라이더에 레이가 맞는다.
                piece.go.SetActive(false);
                Destroy(piece.go);
            }

            Physics.SyncTransforms();
            GiveRefund(refundBuffer);

            AudioManager.Instance?.PlayBreak();
            Changed?.Invoke();
        }

        /// <summary>이 바닥이 사라지면 공중에 뜨는 조각이 있는지 확인한다.</summary>
        private bool HasLoadAbove(PlacedPiece floor)
        {
            // 바로 위층 바닥.
            if (floorByKey.ContainsKey(PieceKey(floor.cellX, floor.cellZ, floor.level + 1, 0)))
                return true;

            // 이 바닥 위에 선 벽류. 단, 모서리를 함께 쓰는 옆 칸 바닥이 아직 있으면 그쪽이 받쳐 준다.
            for (int side = 0; side < 4; side++)
            {
                GetEdgeOfCell(floor.cellX, floor.cellZ, side, out int ex, out int ez, out int axis);
                if (!wallByKey.ContainsKey(PieceKey(ex, ez, floor.level, axis)))
                    continue;

                GetNeighborCell(floor.cellX, floor.cellZ, side, out int nx, out int nz);
                if (!floorByKey.ContainsKey(PieceKey(nx, nz, floor.level, 0)))
                    return true;
            }

            return false;
        }

        /// <summary>반환 재료 목록(원가의 절반, 내림)을 채운다. ItemData를 못 찾으면 false.</summary>
        private bool CollectRefund(BuildPieceType type, List<BuildPieceCost> buffer)
        {
            buffer.Clear();

            IReadOnlyList<BuildPieceCost> cost = BuildPieceCatalog.GetCost(type);
            if (cost == null)
                return true;

            for (int i = 0; i < cost.Count; i++)
            {
                BuildPieceCost entry = cost[i];
                if (string.IsNullOrEmpty(entry.itemName) || entry.count <= 0)
                    continue;

                int back = entry.count / 2; // 절반 내림
                if (back <= 0)
                    continue;

                if (ResolveItem(entry.itemName) == null)
                    return false;

                buffer.Add(new BuildPieceCost(entry.itemName, back));
            }

            return true;
        }

        /// <summary>
        /// 반환 재료를 전부 받을 자리가 있는지 확인한다. PlayerInventory.CanAccept는 한 종류씩만
        /// 보므로, 종류가 둘 이상일 때는 필요한 칸을 직접 합산해야 정확하다.
        /// </summary>
        private bool CanAcceptRefund(List<BuildPieceCost> refund)
        {
            if (refund.Count == 0)
                return true;

            PlayerInventory inventory = Inventory;
            if (inventory == null)
                return false;

            int needed = 0;
            for (int i = 0; i < refund.Count; i++)
            {
                ItemData data = ResolveItem(refund[i].itemName);
                if (data == null)
                    return false;

                int max = data.MaxStackSize;
                int have = CountOwned(refund[i].itemName);
                needed += SlotsFor(have + refund[i].count, max) - SlotsFor(have, max);
            }

            return needed <= inventory.FreeSlots;
        }

        private void GiveRefund(List<BuildPieceCost> refund)
        {
            PlayerInventory inventory = Inventory;
            if (inventory == null)
                return;

            for (int i = 0; i < refund.Count; i++)
            {
                ItemData data = ResolveItem(refund[i].itemName);
                if (data == null)
                    continue;

                for (int k = 0; k < refund[i].count; k++)
                    inventory.TryAddItem(data);
            }
        }

        /// <summary>개수 count를 한 칸 max개짜리 스택으로 담을 때 필요한 칸 수(PlayerInventory와 같은 식).</summary>
        private static int SlotsFor(int count, int max)
        {
            if (count <= 0)
                return 0;
            if (max <= 1)
                return count;
            return (count + max - 1) / max;
        }

        /// <summary>
        /// 이름으로 ItemData를 찾는다. 우선순위는 (1) 이미 만든 캐시 (2) 플레이어가 실제로 들고 있는
        /// 아이템 (3) ItemDataRegistry (4) 메모리에 올라온 전수 조회다. 철거할 때만 부르므로
        /// 매 프레임 비용이 아니다.
        /// </summary>
        private ItemData ResolveItem(string itemName)
        {
            if (string.IsNullOrEmpty(itemName))
                return null;

            if (itemByName == null)
                itemByName = new Dictionary<string, ItemData>();

            if (itemByName.TryGetValue(itemName, out ItemData cached) && cached != null)
                return cached;

            PlayerInventory inventory = Inventory;
            if (inventory != null && inventory.items != null)
            {
                for (int i = 0; i < inventory.items.Count; i++)
                {
                    InventoryItem item = inventory.items[i];
                    if (item == null || item.data == null || item.data.itemName != itemName)
                        continue;

                    itemByName[itemName] = item.data;
                    return item.data;
                }
            }

            ItemDataRegistry registry = ItemDataRegistry.LoadFromResources();
            if (registry != null && registry.allItems != null)
            {
                for (int i = 0; i < registry.allItems.Count; i++)
                {
                    ItemData data = registry.allItems[i];
                    if (data == null || data.itemName != itemName)
                        continue;

                    itemByName[itemName] = data;
                    return data;
                }
            }

            var all = Resources.FindObjectsOfTypeAll<ItemData>();
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] == null || all[i].itemName != itemName)
                    continue;

                itemByName[itemName] = all[i];
                return all[i];
            }

            return null;
        }

        // ────────────────────────────────────────────────────────────────────────
        // 등록 / 해제
        // ────────────────────────────────────────────────────────────────────────

        private void RegisterPiece(BuildPieceType type, GameObject go, int cellX, int cellZ, int level, int axis,
            Vector3 position, float yaw)
        {
            var piece = new PlacedPiece
            {
                type = type,
                go = go,
                root = go.transform,
                cellX = cellX,
                cellZ = cellZ,
                level = level,
                axis = axis,
                position = position,
                yaw = yaw,
            };

            pieces.Add(piece);
            pieceByRoot[piece.root] = piece;

            long key = PieceKey(cellX, cellZ, level, axis);
            if (type == BuildPieceType.Floor)
                floorByKey[PieceKey(cellX, cellZ, level, 0)] = piece;
            else if (type != BuildPieceType.Stair)
                wallByKey[key] = piece;

            structureVersion++;
        }

        private void UnregisterPiece(PlacedPiece piece)
        {
            pieces.Remove(piece);

            // Unity의 == 는 파괴된 오브젝트를 null로 취급하므로 `piece.root != null`로 거르면 이미
            // 파괴된 조각의 표 항목이 영원히 남는다. 참조 동일성으로만 검사한다.
            if (!ReferenceEquals(piece.root, null))
                pieceByRoot.Remove(piece.root);

            if (piece.type == BuildPieceType.Floor)
                floorByKey.Remove(PieceKey(piece.cellX, piece.cellZ, piece.level, 0));
            else if (piece.type != BuildPieceType.Stair)
                wallByKey.Remove(PieceKey(piece.cellX, piece.cellZ, piece.level, piece.axis));

            structureVersion++;
        }

        private void ClearAllPieces()
        {
            for (int i = 0; i < pieces.Count; i++)
            {
                GameObject go = pieces[i].go;
                if (go == null)
                    continue;

                go.SetActive(false);
                Destroy(go);
            }

            pieces.Clear();
            pieceByRoot.Clear();
            floorByKey.Clear();
            wallByKey.Clear();
            structureVersion++;
        }

        // ────────────────────────────────────────────────────────────────────────
        // 저장 / 복원 (SaveLoadController가 배선한다)
        // ────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// 놓여 있는 조각 전부를 JSON으로 만든다. **빈 상태면 ""를 돌려준다** - 세이브 파일에
        /// 의미 없는 빈 객체가 들어가지 않게 하고, 호출부가 "건축 기록 없음"을 문자열 하나로 판정한다.
        /// </summary>
        public string SerializeToJson()
        {
            if (pieces.Count == 0)
                return "";

            var data = new BuildStructureSaveData();
            for (int i = 0; i < pieces.Count; i++)
            {
                PlacedPiece piece = pieces[i];
                data.pieces.Add(new BuildPieceSaveEntry
                {
                    type = (int)piece.type,
                    cellX = piece.cellX,
                    cellZ = piece.cellZ,
                    level = piece.level,
                    axis = piece.axis,
                    posX = piece.position.x,
                    posY = piece.position.y,
                    posZ = piece.position.z,
                    yaw = piece.yaw,
                });
            }

            return JsonUtility.ToJson(data);
        }

        /// <summary>
        /// 저장된 조각을 그대로 되살린다. json이 ""/null이면 **아무것도 하지 않는다**(건축 기능이
        /// 없던 시절의 옛 세이브 호환 - 지금 지어 둔 것을 지우지도 않는다).
        /// </summary>
        public void RestoreFromJson(string json)
        {
            if (string.IsNullOrEmpty(json))
                return;

            BuildStructureSaveData data;
            try
            {
                data = JsonUtility.FromJson<BuildStructureSaveData>(json);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[BuildingSystem] 건축 저장 데이터를 읽지 못했다: {e.Message}");
                return;
            }

            if (data == null || data.pieces == null)
                return;

            ClearAllPieces();

            for (int i = 0; i < data.pieces.Count; i++)
            {
                BuildPieceSaveEntry entry = data.pieces[i];
                if (entry == null)
                    continue;

                if (entry.type < (int)BuildPieceType.Floor || entry.type > (int)BuildPieceType.Stair)
                {
                    Debug.LogWarning($"[BuildingSystem] 알 수 없는 부품 종류 {entry.type} 를 건너뛴다.");
                    continue;
                }

                var type = (BuildPieceType)entry.type;
                long key = PieceKey(entry.cellX, entry.cellZ, entry.level, entry.axis);
                bool occupied = type == BuildPieceType.Floor
                    ? floorByKey.ContainsKey(PieceKey(entry.cellX, entry.cellZ, entry.level, 0))
                    : wallByKey.ContainsKey(key);

                if (occupied)
                {
                    Debug.LogWarning("[BuildingSystem] 같은 자리에 조각이 둘 저장돼 있어 뒤엣것을 건너뛴다.");
                    continue;
                }

                GameObject go = BuildPieceVisualBuilder.CreateSolid(type, piecesRoot);
                if (go == null)
                    continue;

                var position = new Vector3(entry.posX, entry.posY, entry.posZ);
                go.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, entry.yaw, 0f));

                RegisterPiece(type, go, entry.cellX, entry.cellZ, entry.level, entry.axis, position, entry.yaw);
            }

            // 방금 만든 콜라이더가 곧바로 레이캐스트에 잡히게 한다(autoSyncTransforms가 꺼져 있다).
            Physics.SyncTransforms();
            Changed?.Invoke();
        }

        // ────────────────────────────────────────────────────────────────────────
        // 실내(집) 판정
        // ────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// 이 좌표가 "지붕 대신 벽으로 둘러싸인 실내"인지 판정한다. Shelter 등 다른 시스템이 매 프레임
        /// 여러 번 부를 수 있어, 결과를 (칸, 층) 단위로 캐시하고 조각이 바뀔 때만 통째로 버린다.
        ///
        /// 판정: 발밑 바닥 칸에서 시작해 **같은 층의 바닥을 따라 퍼져 나가며**, 벽류가 없는 모서리를
        /// 넘어갔을 때 바닥도 없으면 "바깥으로 샌다"로 보고 실외 판정한다. 벽으로 다 막힌 방이면
        /// 탐색이 방 안에서 끝난다. 이렇게 하면 1x1 오두막부터 여러 칸짜리 방까지 같은 규칙으로 잡히고,
        /// 벽 없는 데크는 첫 걸음에 새므로 즉시 실외가 된다(문·창문은 벽류로 쳐서 막힌 것으로 본다).
        ///
        /// **지붕도 요구한다**: 방을 이루는 모든 칸의 바로 위층에 바닥이 있어야 한다. 이 배치에는
        /// 지붕 전용 부품이 없고 위층 바닥이 곧 지붕이다(위로 계속 쌓는 구조의 자연스러운 귀결이다).
        /// 이 조건이 없으면 벽 네 장만 세운 우리가 그늘 취급을 받는다 - Shelter.IsInsideHome이 이 값을
        /// 일사병 그늘 판정에 쓰기 때문에, 머리 위가 뚫려 있으면 실내가 아니어야 한다.
        /// </summary>
        public static bool IsInsideEnclosedStructure(Vector3 worldPos)
        {
            BuildingSystem system = Instance;
            if (system == null)
                return false;

            return system.IsInsideInternal(worldPos);
        }

        private bool IsInsideInternal(Vector3 worldPos)
        {
            if (floorByKey.Count == 0)
                return false;

            int cellX = CellIndexOf(worldPos.x);
            int cellZ = CellIndexOf(worldPos.z);

            PlacedPiece floor = FindFloorUnder(cellX, cellZ, worldPos.y);
            if (floor == null)
                return false;

            if (enclosureCacheVersion != structureVersion)
            {
                enclosureCache.Clear();
                enclosureCacheVersion = structureVersion;
            }

            long key = PieceKey(cellX, cellZ, floor.level, 0);
            if (enclosureCache.TryGetValue(key, out bool cachedResult))
                return cachedResult;

            bool result = ComputeEnclosed(cellX, cellZ, floor.level);
            enclosureCache[key] = result;
            return result;
        }

        /// <summary>이 좌표가 딛고 서 있는 바닥(한 층 안쪽에 있는 것 중 가장 높은 것).</summary>
        private PlacedPiece FindFloorUnder(int cellX, int cellZ, float y)
        {
            int level = LevelOf(y);
            PlacedPiece best = null;

            for (int d = -1; d <= 1; d++)
            {
                if (!floorByKey.TryGetValue(PieceKey(cellX, cellZ, level + d, 0), out PlacedPiece floor))
                    continue;

                float delta = y - floor.position.y;
                if (delta < -0.3f || delta > LevelHeight)
                    continue;

                if (best == null || floor.position.y > best.position.y)
                    best = floor;
            }

            return best;
        }

        private bool ComputeEnclosed(int cellX, int cellZ, int level)
        {
            bfsCellX.Clear();
            bfsCellZ.Clear();
            bfsVisited.Clear();

            bfsCellX.Add(cellX);
            bfsCellZ.Add(cellZ);
            bfsVisited.Add(PieceKey(cellX, cellZ, level, 0));

            for (int head = 0; head < bfsCellX.Count; head++)
            {
                if (bfsCellX.Count > MaxEnclosureCells)
                    return false; // 방이라기엔 너무 넓다 - 야외 데크로 본다

                int x = bfsCellX[head];
                int z = bfsCellZ[head];

                // 머리 위(바로 위층 바닥 = 지붕)가 없으면 실내가 아니다.
                if (!floorByKey.ContainsKey(PieceKey(x, z, level + 1, 0)))
                    return false;

                for (int side = 0; side < 4; side++)
                {
                    GetEdgeOfCell(x, z, side, out int ex, out int ez, out int axis);
                    if (wallByKey.ContainsKey(PieceKey(ex, ez, level, axis)))
                        continue; // 벽·문·창문이 막고 있다

                    GetNeighborCell(x, z, side, out int nx, out int nz);
                    long neighborKey = PieceKey(nx, nz, level, 0);
                    if (!floorByKey.ContainsKey(neighborKey))
                        return false; // 벽도 바닥도 없다 → 바깥으로 샌다

                    if (bfsVisited.Add(neighborKey))
                    {
                        bfsCellX.Add(nx);
                        bfsCellZ.Add(nz);
                    }
                }
            }

            return true;
        }

        // ────────────────────────────────────────────────────────────────────────
        // 격자 계산
        // ────────────────────────────────────────────────────────────────────────

        private static int CellIndexOf(float world)
        {
            return Mathf.FloorToInt(world / CellSize);
        }

        private static float CellCenterCoord(int index)
        {
            return (index + 0.5f) * CellSize;
        }

        /// <summary>
        /// y를 층 번호로 양자화한다. **Mathf.RoundToInt를 쓰면 안 된다** - 그쪽은 정확히 .5일 때
        /// 짝수로 붙이는(banker's rounding) 규칙이라 1.5→2, 2.5→2 가 되어 층이 단조증가하지 않는다.
        /// 그러면 y와 y+2.5(정확히 한 층 위)가 같은 번호로 접혀 "이미 조각이 있다"로 잘못 막힌다.
        /// 항상 반올림(내림+0.5)을 쓰면 +2.5는 언제나 정확히 +1층이다.
        /// </summary>
        private static int LevelOf(float y)
        {
            return Mathf.FloorToInt(y / LevelHeight + 0.5f);
        }

        /// <summary>
        /// 셀 (x,z)의 side번째 모서리를 canonical 좌표로 돌려준다.
        /// side 0 = -Z · 1 = +Z · 2 = -X · 3 = +X.
        /// </summary>
        private static void GetEdgeOfCell(int x, int z, int side, out int edgeX, out int edgeZ, out int axis)
        {
            switch (side)
            {
                case 0: edgeX = x; edgeZ = z; axis = 0; break;
                case 1: edgeX = x; edgeZ = z + 1; axis = 0; break;
                case 2: edgeX = x; edgeZ = z; axis = 1; break;
                default: edgeX = x + 1; edgeZ = z; axis = 1; break;
            }
        }

        /// <summary>셀 (x,z)의 side번째 모서리 건너편 셀.</summary>
        private static void GetNeighborCell(int x, int z, int side, out int nx, out int nz)
        {
            switch (side)
            {
                case 0: nx = x; nz = z - 1; break;
                case 1: nx = x; nz = z + 1; break;
                case 2: nx = x - 1; nz = z; break;
                default: nx = x + 1; nz = z; break;
            }
        }

        /// <summary>모서리 (ex,ez,axis)의 중점(= 벽 밑면 중심이 놓일 자리).</summary>
        private static Vector3 EdgeMidpoint(int edgeX, int edgeZ, int axis, float y)
        {
            if (axis == 0)
                return new Vector3(CellCenterCoord(edgeX), y, edgeZ * CellSize);

            return new Vector3(edgeX * CellSize, y, CellCenterCoord(edgeZ));
        }

        /// <summary>
        /// (x, z, level, axis)를 long 하나로 접는다. x/z 각 21비트(±1,048,575 - 월드 반경 20,000m를
        /// 셀 크기 2로 나눠도 10,000이라 넉넉하다), level 12비트, axis 2비트로 총 56비트다.
        /// </summary>
        private static long PieceKey(int x, int z, int level, int axis)
        {
            long kx = (long)(x + 1048576) & 0x1FFFFF;
            long kz = (long)(z + 1048576) & 0x1FFFFF;
            long kl = (long)(level + 512) & 0xFFF;
            long ka = axis & 0x3;
            return (kx << 35) | (kz << 14) | (kl << 2) | ka;
        }

        // ────────────────────────────────────────────────────────────────────────
        // 잡동사니
        // ────────────────────────────────────────────────────────────────────────

        private Camera GetCamera()
        {
            if (cachedCamera == null)
                cachedCamera = Camera.main;
            return cachedCamera;
        }

        private static bool IsPointerOverUI()
        {
            var eventSystem = UnityEngine.EventSystems.EventSystem.current;
            return eventSystem != null && eventSystem.IsPointerOverGameObject();
        }
    }

    /// <summary>
    /// 조각 하나의 저장 항목. **필드 제거·개명은 세이브를 깬다 - 추가만 하라**(AGENT_BRIEF 3장).
    /// 좌표를 격자 키와 실제 위치 양쪽으로 저장하는 이유: 격자 키는 점유 판정과 실내 판정에 필요하고,
    /// 실제 위치는 지형 높이에 맞춰 놓은 1층 바닥의 y를 원래대로 되살리는 데 필요하다(지형이 같은
    /// 시드로 다시 생성되더라도 다시 레이를 쏴서 재계산하면 미세하게 달라질 수 있다).
    /// </summary>
    [System.Serializable]
    public class BuildPieceSaveEntry
    {
        public int type;
        public int cellX;
        public int cellZ;
        public int level;
        public int axis;
        public float posX;
        public float posY;
        public float posZ;
        public float yaw;
    }

    /// <summary>
    /// 건축물 전체 저장 데이터. JsonUtility는 최상위 리스트를 직렬화하지 못해 감싸는 클래스가 필요하다.
    /// version은 앞으로 형식이 바뀔 때 분기하기 위한 자리다(지금은 읽기만 하고 쓰지 않는다).
    /// </summary>
    [System.Serializable]
    public class BuildStructureSaveData
    {
        public int version = 1;
        public List<BuildPieceSaveEntry> pieces = new List<BuildPieceSaveEntry>();
    }
}
