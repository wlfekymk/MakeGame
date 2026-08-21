using UnityEngine;

namespace MakeGame.Systems
{
    /// <summary>
    /// 건축 부품(바닥/벽/문/창문/계단/지붕)의 실물 메시와 배치 미리보기(고스트)를 절차적으로 만든다.
    ///
    /// 이 프로젝트에는 3D 모델 에셋이 0개다(AGENT_BRIEF 1장). 따라서 전부 프리미티브 조합이며,
    /// 조립은 공용 도구인 <see cref="StructureVisualBuilder"/>를 그대로 재사용한다(색·텍스처·무광
    /// 처리의 단일 소스). 새 팔레트 색은 하나도 만들지 않는다.
    ///
    /// 머티리얼은 **부품 종류와 무관하게 프로세스 전체에서 3개(실물) + 2개(고스트)**만 만들어 공유한다.
    /// 집 한 채는 부품 수십 개 = 파츠 수백 개가 되므로, 파츠마다 머티리얼을 만들면 SRP 배처가 죽는다
    /// (AGENT_BRIEF 4장 마지막 줄, RaftStructure.EnsureMaterials와 같은 방식).
    ///
    /// ── 로컬 좌표 규약(고정, 스냅 수학의 전제) ───────────────────────────────────
    ///  Floor : 셀 **중심**. 로컬 원점 = 바닥 **윗면** 중심. 두께는 전부 원점 아래(-Y)로 간다.
    ///  Wall / Doorway / Window : 셀 **모서리**. 로컬 원점 = 벽 **밑면** 중심.
    ///                            길이 = 로컬 X로 2m(±1), 높이 = +Y로 2.5m, 두께 = 로컬 Z로 ±0.06m.
    ///  Stair : 한 칸을 차지하고 밑면 원점에서 +Y 2.5m / +Z 2m로 올라간다.
    ///  Roof  : 셀 **중심**. 로컬 원점 = 지붕 **밑면**(처마 밑면 = 그 층의 천장 평면). 두께는 전부
    ///          원점 위(+Y)로 가고, 로컬 -Z가 낮은 쪽 / +Z가 높은 쪽이다(경사는 메시에 구워져 있다).
    /// 이 규약을 벗어나는 파츠는 하나도 없다(장식 결속만 두께 방향으로 ±0.06 경계에 딱 맞춘다).
    /// </summary>
    public static class BuildPieceVisualBuilder
    {
        // ── 격자 규격 (BuildPieceCatalog가 단일 소스) ─────────────────────────────
        private const float HalfCell = BuildPieceCatalog.CellSize * 0.5f;   // 1.0
        private const float WallHeight = BuildPieceCatalog.LevelHeight;     // 2.5
        private const float WallHalfThickness = 0.06f;                      // 벽 두께 ±0.06 (총 0.12)
        private const float PlankThickness = 0.09f;                         // 널판(벽면)의 두께
        private const float FloorThickness = 0.20f;                         // 바닥 총 두께(원점 아래)

        // ── 문/창 구멍 치수 ───────────────────────────────────────────────────────
        // 플레이어 CharacterController는 씬 실측 radius 0.5 / height 2 / skinWidth 0.08 / stepOffset 0.3다.
        // 문 구멍을 1.0m로 잡으면 캡슐 지름과 정확히 같아 스킨 두께 때문에 끼인다 - 1.4m / 높이 2.1m로 잡는다.
        private const float DoorHalfWidth = 0.70f;
        private const float DoorHeight = 2.10f;
        private const float JambHalfWidth = 0.15f;                          // 문설주/창설주 폭 0.30
        private const float JambCenterX = HalfCell - JambHalfWidth;         // ±0.85

        private const float WindowSillTop = 1.05f;                          // 창턱 윗면
        private const float WindowHeadBottom = 1.75f;                       // 창 상인방 밑면

        // ── 계단 ─────────────────────────────────────────────────────────────────
        // 단 수는 **stepOffset(0.3)이 정한다.** 2.5m를 9단으로 나누면 한 단 0.278m로 딱 걸린다.
        // 5단(0.5m)이나 8단(0.3125m)으로 만들면 CharacterController가 올라가지 못하고,
        // 경사로 콜라이더로 대체하는 것도 불가능하다(경사 51.3° > slopeLimit 45°).
        private const int StairStepCount = 9;
        private const float StairWidth = 1.80f;

        // ── 지붕(한쪽으로 기운 shed roof) ────────────────────────────────────────
        // 로컬 원점 = **처마 밑면**(y=0)이고 밑면은 셀 전체를 덮는 평면이다 - 즉 방 안에서 올려다보면
        // 평평한 천장이고, 밖에서 보면 기운 지붕이다. 기울기는 **메시 정점에 구워 넣는다**:
        // 부모 스케일이 비균등한 경우 회전한 자식은 전단(shear)으로 찌그러지므로, 이 프로젝트에서
        // 기운 형상은 회전이 아니라 정점으로만 만든다(AGENT_BRIEF 4장).
        private const float RoofRise = 0.75f;        // 낮은 쪽(-Z)에서 높은 쪽(+Z)까지 올라가는 높이 → 20.6도
        private const float RoofSlabThickness = 0.16f; // 처마 끝 두께(= 천장 판의 두께)
        private const float RoofBeamThickness = 0.18f; // 처마/마루 보의 굵기
        private const int RoofColliderSteps = 4;       // 경사면을 근사하는 계단형 콜라이더 수

        // ── 보관 상자(배치 39) ───────────────────────────────────────────────────
        // 로컬 원점 = **밑면 중심**(딛고 선 바닥의 윗면과 같은 평면)이고, 상자가 바라보는 쪽은 로컬 +Z다
        // (계단이 +Z로 올라가는 것과 같은 방향 규약 - 회전 입력의 의미가 부품마다 뒤집히지 않게).
        // 등급이 오르면 **같은 형상이 커지기만 한다.** 셀 한 변이 2m이므로 특대(가로 1.72)도 칸을 넘지 않고,
        // 색을 등급별로 바꾸지 않는다 - 머티리얼은 프로세스 전체에서 3개뿐이라는 규칙을 깨지 않기 위해서다.
        private static readonly float[] ChestWidths = { 1.00f, 1.24f, 1.48f, 1.72f };
        private static readonly float[] ChestDepths = { 0.64f, 0.72f, 0.80f, 0.90f };
        private static readonly float[] ChestHeights = { 0.60f, 0.70f, 0.80f, 0.92f };
        private const float ChestLidHeight = 0.14f;

        /// <summary>지붕 몸통 메시. 프로세스 전체에서 **하나만** 만들어 모든 지붕이 공유한다.</summary>
        private static Mesh roofMesh;

        // ── 공유 머티리얼 ─────────────────────────────────────────────────────────
        private static Material postMaterial;    // 기둥/보 - 굵은 구조재
        private static Material plankMaterial;   // 널판 - 구조재보다 살짝 밝게 민 명도 변형(색상각은 그대로)
        private static Material lashingMaterial; // 결속(밧줄/섬유)
        private static Material ghostValidMaterial;
        private static Material ghostInvalidMaterial;

        // ── 부품 티어 머티리얼 (건축 4티어) ──────────────────────────────────────
        // 티어가 올라도 **지오메트리는 그대로**고 재질(머티리얼)만 바뀐다. 슬롯(post/plank/lashing) ×
        // 티어(4)로 프로세스 전체 12개가 상한이며, 1티어 슬롯은 위의 기존 나무 머티리얼 그 자체다 -
        // 파츠마다 머티리얼을 만들면 SRP 배처가 죽는다는 규칙(클래스 주석)을 티어에도 그대로 적용한다.
        // 인덱스 = 티어 - 1.
        private static Material[] tierPostMaterials;
        private static Material[] tierPlankMaterials;
        private static Material[] tierLashingMaterials;

        /// <summary>
        /// 도메인 리로드를 끈 플레이 모드에서 static 캐시가 이전 실행의 파괴된 메시/머티리얼을
        /// 들고 시작하지 않게 초기 상태로 되돌린다.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStaticCache()
        {
            roofMesh = null;
            postMaterial = null;
            plankMaterial = null;
            lashingMaterial = null;
            ghostValidMaterial = null;
            ghostInvalidMaterial = null;
            tierPostMaterials = null;
            tierPlankMaterials = null;
            tierLashingMaterials = null;
        }

        /// <summary>고스트 반투명도. 뒤의 지형이 비쳐 보이되 형태는 읽혀야 한다.</summary>
        private const float GhostAlpha = 0.38f;

        // ─────────────────────────────────────────────────────────────────────────
        //  공개 API
        // ─────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// 실제로 지어진 부품을 만든다. 콜라이더가 붙어 있어 바닥은 밟히고 벽은 막는다.
        /// **문의 구멍과 창의 구멍은 콜라이더가 비어 있다**(통짜 박스가 아니다).
        /// </summary>
        public static GameObject CreateSolid(BuildPieceType type, Transform parent)
        {
            EnsureSolidMaterials();

            GameObject root = CreateRoot($"BuildPiece_{type}", parent);
            BuildParts(type, root.transform, postMaterial, plankMaterial, lashingMaterial);
            AddColliders(type, root);
            return root;
        }

        /// <summary>
        /// 등급이 있는 보관 상자의 실물을 만든다. tier는 0~3(소·중·대·특대)이며 범위를 벗어나면 잘린다.
        /// CreateSolid(BuildPieceType.Chest, parent)는 이 함수의 tier 0과 완전히 같은 결과다.
        /// </summary>
        public static GameObject CreateChestSolid(Transform parent, int tier)
        {
            EnsureSolidMaterials();

            GameObject root = CreateRoot($"BuildPiece_{BuildPieceType.Chest}", parent);
            BuildChest(root.transform, postMaterial, plankMaterial, lashingMaterial, tier);
            AddChestCollider(root, tier);
            return root;
        }

        /// <summary>
        /// 이미 서 있는 상자의 **겉모습과 콜라이더만** 새 등급으로 갈아 끼운다.
        /// 루트 GameObject는 그대로 두는 것이 이 함수의 핵심이다 - 루트에는 StorageChest 컴포넌트가
        /// 붙어 있고 UI가 그 인스턴스를 들고 있으므로, 업그레이드할 때마다 루트를 다시 만들면
        /// 열려 있던 상자 UI의 참조가 그 자리에서 끊긴다(내용물도 함께 날아간다).
        /// </summary>
        public static void RebuildChest(GameObject root, int tier)
        {
            if (root == null)
                return;

            EnsureSolidMaterials();

            // Destroy는 프레임 끝까지 지연된다. 먼저 꺼서 이번 프레임에 새로 만든 파츠와 겹쳐 보이지 않게 한다.
            for (int i = root.transform.childCount - 1; i >= 0; i--)
            {
                GameObject child = root.transform.GetChild(i).gameObject;
                child.SetActive(false);
                UnityEngine.Object.Destroy(child);
            }

            var colliders = root.GetComponents<BoxCollider>();
            for (int i = 0; i < colliders.Length; i++)
            {
                // 콜라이더는 끌 수 없으면 즉시 물리에서 빠지지 않는다. Destroy가 프레임 끝에 반영되므로
                // enabled=false로 이번 프레임부터 새 콜라이더와 이중으로 잡히지 않게 한다.
                colliders[i].enabled = false;
                UnityEngine.Object.Destroy(colliders[i]);
            }

            BuildChest(root.transform, postMaterial, plankMaterial, lashingMaterial, tier);
            AddChestCollider(root, tier);
        }

        /// <summary>
        /// 이미 서 있는 부품의 **렌더러 머티리얼만** 티어 재질로 갈아 끼운다. 지오메트리·콜라이더·루트는
        /// 일절 건드리지 않는다(상자 RebuildChest가 형상을 다시 만드는 것과 달리, 부품 티어는 재질 교체가
        /// 전부다). 각 렌더러가 어느 슬롯(구조재/널판/결속)이었는지는 지금 물고 있는 공유 머티리얼의
        /// 정체로 역추적하므로, 부품 종류별 파츠 구성을 여기서 알 필요가 없다.
        /// tier는 1~4(나무/돌/강철/대리석)이고 범위 밖은 잘린다. 1티어 적용은 기존 나무 재질로 되돌린다.
        /// </summary>
        public static void ApplyTier(GameObject root, int tier)
        {
            if (root == null)
                return;

            EnsureSolidMaterials();
            EnsureTierMaterials();

            int index = BuildPieceCatalog.ClampPieceTier(tier) - 1;
            var renderers = root.GetComponentsInChildren<MeshRenderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] == null)
                    continue;

                int slot = SlotOfMaterial(renderers[i].sharedMaterial);
                switch (slot)
                {
                    case 0: renderers[i].sharedMaterial = tierPostMaterials[index]; break;
                    case 1: renderers[i].sharedMaterial = tierPlankMaterials[index]; break;
                    case 2: renderers[i].sharedMaterial = tierLashingMaterials[index]; break;
                    // 모르는 머티리얼(고스트 등)은 그대로 둔다.
                }
            }
        }

        /// <summary>이 공유 머티리얼이 어느 슬롯 소속인지(0=구조재 1=널판 2=결속, 아니면 -1).</summary>
        private static int SlotOfMaterial(Material material)
        {
            if (material == null || tierPostMaterials == null)
                return -1;

            for (int t = 0; t < tierPostMaterials.Length; t++)
            {
                if (material == tierPostMaterials[t]) return 0;
                if (material == tierPlankMaterials[t]) return 1;
                if (material == tierLashingMaterials[t]) return 2;
            }
            return -1;
        }

        /// <summary>
        /// 배치 미리보기용 고스트를 만든다. 콜라이더가 없고(레이캐스트·이동을 방해하지 않는다) 반투명이며,
        /// 그림자도 드리우지 않는다. 유효 = 초록, 무효 = 빨강.
        /// </summary>
        public static GameObject CreateGhost(BuildPieceType type, Transform parent, bool valid)
        {
            EnsureGhostMaterials();

            GameObject root = CreateRoot($"BuildGhost_{type}", parent);
            Material material = valid ? ghostValidMaterial : ghostInvalidMaterial;

            // 고스트는 색이 파츠마다 다를 이유가 없으므로 세 슬롯에 같은 머티리얼을 넣는다.
            BuildParts(type, root.transform, material, material, material);

            var renderers = root.GetComponentsInChildren<MeshRenderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] == null)
                    continue;

                // 머티리얼 쪽 ShadowCaster 패스를 껐지만, 렌더러에서도 확실히 꺼 둔다
                // (반투명 고스트가 지형에 진한 그림자를 던지면 유효/무효 색보다 그림자가 먼저 읽힌다).
                renderers[i].shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderers[i].receiveShadows = false;
            }

            return root;
        }

        /// <summary>
        /// 고스트의 유효/무효 색만 바꾼다. **메시를 다시 만들지 않는다** - 매 프레임 불리는 경로다.
        ///
        /// 현재 상태는 렌더러 하나의 sharedMaterial이 곧 상태라서 따로 들고 있을 필요가 없다.
        /// 색이 이미 그 상태면 즉시 돌아간다 - 즉 실제로 유효/무효가 **바뀐 프레임에만** 렌더러를 훑고,
        /// 그때만 GetComponentsInChildren 배열이 한 번 생긴다(가만히 들고 있는 동안은 할당이 0이다).
        /// 머티리얼은 공유본을 갈아끼우는 것이라(sharedMaterial) 인스턴스가 늘어나지도 않는다.
        /// </summary>
        /// <summary>
        /// 뗏목을 세울 자리를 보여 주는 발자국 고스트. 부품 고스트와 **같은 머티리얼 규약**을 쓰므로
        /// <see cref="SetGhostValid"/>가 그대로 유효/불가 색을 갈아 끼운다.
        ///
        /// 그리는 것은 뗏목 실물이 아니라 **자리**다: 선체 크기(DeckWidth x DeckLength)의 테두리와
        /// 통나무 세 줄, 모서리 말뚝 넷, 그리고 뱃머리 표시 하나. 뱃머리 표시가 없으면 90도 회전이
        /// 화면에서 구별되지 않는다(4x8이라 두 번 돌리면 같아 보인다).
        /// </summary>
        public static GameObject CreateRaftSiteGhost(Transform parent, bool valid)
        {
            EnsureGhostMaterials();

            GameObject root = CreateRoot("RaftSiteGhost", parent);
            Material material = valid ? ghostValidMaterial : ghostInvalidMaterial;

            const float rim = 0.22f;
            const float postHeight = 1.1f;

            float width = RaftStructure.DeckWidth;
            float length = RaftStructure.DeckLength;
            float halfX = width * 0.5f;
            float halfZ = length * 0.5f;

            // 테두리 네 줄. 위치는 뗏목 로컬 원점(선체 중심)과 같아, 고스트가 선 자리가 곧 선체 자리다.
            Part(root.transform, "RimFore", new Vector3(0f, 0f, halfZ - rim * 0.5f),
                new Vector3(width, rim, rim), material);
            Part(root.transform, "RimAft", new Vector3(0f, 0f, -halfZ + rim * 0.5f),
                new Vector3(width, rim, rim), material);
            Part(root.transform, "RimPort", new Vector3(-halfX + rim * 0.5f, 0f, 0f),
                new Vector3(rim, rim, length), material);
            Part(root.transform, "RimStarboard", new Vector3(halfX - rim * 0.5f, 0f, 0f),
                new Vector3(rim, rim, length), material);

            // 통나무 세 줄 - "여기에 뗏목이 선다"가 테두리만으로는 잘 안 읽힌다.
            for (int i = 0; i < 3; i++)
            {
                float z = -halfZ * 0.5f + halfZ * 0.5f * i;
                Part(root.transform, $"Log_{i}", new Vector3(0f, -0.14f, z),
                    new Vector3(width - rim * 2f, 0.18f, 0.55f), material);
            }

            // 모서리 말뚝 넷. 물 위에서 발자국이 수면에 묻히지 않게 세로로 세운다.
            for (int i = 0; i < 4; i++)
            {
                float x = (i % 2 == 0 ? -1f : 1f) * (halfX - rim * 0.5f);
                float z = (i < 2 ? -1f : 1f) * (halfZ - rim * 0.5f);
                Part(root.transform, $"Post_{i}", new Vector3(x, postHeight * 0.5f, z),
                    new Vector3(0.16f, postHeight, 0.16f), material);
            }

            // 뱃머리 표시(+Z가 앞이다 - RaftStructure.PlaceAt의 facing과 같은 규약).
            Part(root.transform, "BowMark", new Vector3(0f, 0.18f, halfZ + 0.5f),
                new Vector3(0.9f, 0.14f, 0.9f), material);

            var renderers = root.GetComponentsInChildren<MeshRenderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] == null)
                    continue;

                renderers[i].shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderers[i].receiveShadows = false;
            }

            return root;
        }

        public static void SetGhostValid(GameObject ghost, bool valid)
        {
            if (ghost == null)
                return;

            EnsureGhostMaterials();

            Material target = valid ? ghostValidMaterial : ghostInvalidMaterial;

            var probe = ghost.GetComponentInChildren<MeshRenderer>(true);
            if (probe != null && probe.sharedMaterial == target)
                return;

            var renderers = ghost.GetComponentsInChildren<MeshRenderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] != null)
                    renderers[i].sharedMaterial = target;
            }
        }

        // ─────────────────────────────────────────────────────────────────────────
        //  머티리얼
        // ─────────────────────────────────────────────────────────────────────────

        private static void EnsureSolidMaterials()
        {
            if (postMaterial != null)
                return;

            postMaterial = StructureVisualBuilder.CreateColorMaterial(StructureVisualBuilder.Driftwood, "wood");
            plankMaterial = StructureVisualBuilder.CreateColorMaterial(
                Color.Lerp(StructureVisualBuilder.Driftwood, StructureVisualBuilder.SalvageMarkerWhite, 0.22f), "wood");
            lashingMaterial = StructureVisualBuilder.CreateColorMaterial(StructureVisualBuilder.PalmFiber, "leaf");
        }

        /// <summary>
        /// 티어별 슬롯 머티리얼을 한 번만 만든다. 1티어는 기존 나무 3종을 **그대로 재사용**하므로
        /// (새 인스턴스 없음) 나무 부품의 겉모습은 티어 도입 전과 비트 하나 다르지 않다.
        /// · 2티어 돌: 회색(WeatheredStone 계열) + "rock" 텍스처.
        /// · 3티어 강철: 어두운 금속(SalvageMetal을 어둡게) + "metal" 텍스처(에셋이 없으면 단색 - 안전).
        /// · 4티어 대리석: 밝은 백색(명도 0.90 부근) + "noise" 텍스처(매끈한 무늬).
        /// </summary>
        private static void EnsureTierMaterials()
        {
            if (tierPostMaterials != null)
                return;

            EnsureSolidMaterials();

            Color stone = StructureVisualBuilder.WeatheredStone;
            Color metal = StructureVisualBuilder.SalvageMetal;
            var marble = new Color(0.90f, 0.89f, 0.87f);

            tierPostMaterials = new Material[BuildPieceCatalog.PieceTierCount];
            tierPlankMaterials = new Material[BuildPieceCatalog.PieceTierCount];
            tierLashingMaterials = new Material[BuildPieceCatalog.PieceTierCount];

            // 1티어(나무) = 기존 머티리얼 그 자체.
            tierPostMaterials[0] = postMaterial;
            tierPlankMaterials[0] = plankMaterial;
            tierLashingMaterials[0] = lashingMaterial;

            // 2티어(돌): 구조재는 짙은 회색, 널판 자리는 살짝 밝은 회색(나무의 명도 변형과 같은 수법),
            // 결속 자리는 줄눈(모르타르)처럼 어둡게 - 밧줄 색이 돌벽에 남아 있으면 재질이 섞여 보인다.
            tierPostMaterials[1] = StructureVisualBuilder.CreateColorMaterial(stone * 0.85f, "rock");
            tierPlankMaterials[1] = StructureVisualBuilder.CreateColorMaterial(
                Color.Lerp(stone, StructureVisualBuilder.SalvageMarkerWhite, 0.18f), "rock");
            tierLashingMaterials[1] = StructureVisualBuilder.CreateColorMaterial(stone * 0.65f, "rock");

            // 3티어(강철): 어두운 금속. 결속 자리는 리벳 띠처럼 가장 어둡게.
            tierPostMaterials[2] = StructureVisualBuilder.CreateColorMaterial(metal * 0.62f, "metal");
            tierPlankMaterials[2] = StructureVisualBuilder.CreateColorMaterial(metal * 0.82f, "metal");
            tierLashingMaterials[2] = StructureVisualBuilder.CreateColorMaterial(metal * 0.45f, "metal");

            // 4티어(대리석): 밝은 백색 0.90. 결속 자리는 살짝 회색 줄무늬(결)로.
            tierPostMaterials[3] = StructureVisualBuilder.CreateColorMaterial(marble * 0.90f, "noise");
            tierPlankMaterials[3] = StructureVisualBuilder.CreateColorMaterial(marble, "noise");
            tierLashingMaterials[3] = StructureVisualBuilder.CreateColorMaterial(marble * 0.78f, "noise");
        }

        private static void EnsureGhostMaterials()
        {
            if (ghostValidMaterial != null)
                return;

            // 색은 팔레트에서 가져온다. 무효는 DangerRed(위험 신호 전용 색), 유효는 월드 3D 표면용 초록인
            // FrondGreen이다 - Medic Green(#4FA87A)은 UI/아이콘 전용이라 월드 메시에 쓰지 않는다
            // (ArtDirection 1장 / AGENT_BRIEF 5장).
            ghostValidMaterial = CreateGhostMaterial(StructureVisualBuilder.FrondGreen);
            ghostInvalidMaterial = CreateGhostMaterial(StructureVisualBuilder.DangerRed);
        }

        /// <summary>
        /// URP Lit 머티리얼을 **실제로** 반투명으로 바꾼다.
        /// StructureVisualBuilder.CreateColorMaterial이 만드는 것은 URP/Lit의 기본 불투명 머티리얼이라,
        /// 알파만 낮추면 화면에는 아무 변화도 없다(불투명 패스는 알파를 버린다). Surface Type을 인스펙터에서
        /// Transparent로 바꿨을 때 URP가 내부적으로 하는 일을 코드로 그대로 해 준다:
        /// _Surface/_Blend 값, 블렌드 모드, ZWrite 끄기, 키워드 교체, 렌더 큐 이동, RenderType 태그.
        /// 셰이더가 Standard로 폴백된 경우에도 경고가 나지 않도록 프로퍼티는 전부 HasProperty로 감싼다.
        /// </summary>
        private static Material CreateGhostMaterial(Color baseColor)
        {
            var material = StructureVisualBuilder.CreateColorMaterial(baseColor, "noise");

            var tinted = new Color(baseColor.r, baseColor.g, baseColor.b, GhostAlpha);
            material.color = tinted;
            if (material.HasProperty("_BaseColor"))
                material.SetColor("_BaseColor", tinted);

            if (material.HasProperty("_Surface"))
                material.SetFloat("_Surface", 1f);      // 0 = Opaque, 1 = Transparent
            if (material.HasProperty("_Blend"))
                material.SetFloat("_Blend", 0f);        // 0 = Alpha blend
            if (material.HasProperty("_AlphaClip"))
                material.SetFloat("_AlphaClip", 0f);
            if (material.HasProperty("_SrcBlend"))
                material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            if (material.HasProperty("_DstBlend"))
                material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            if (material.HasProperty("_ZWrite"))
                material.SetFloat("_ZWrite", 0f);

            material.SetOverrideTag("RenderType", "Transparent");
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.DisableKeyword("_ALPHATEST_ON");
            material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            material.SetShaderPassEnabled("ShadowCaster", false);

            return material;
        }

        // ─────────────────────────────────────────────────────────────────────────
        //  조립 (실물과 고스트가 **완전히 같은 형상**을 쓴다 - 미리보기와 결과가 어긋나지 않게)
        // ─────────────────────────────────────────────────────────────────────────

        private static GameObject CreateRoot(string name, Transform parent)
        {
            var root = new GameObject(name);
            root.transform.SetParent(parent, false);
            root.transform.localPosition = Vector3.zero;
            root.transform.localRotation = Quaternion.identity;
            root.transform.localScale = Vector3.one;
            return root;
        }

        /// <summary>
        /// 종류별 파츠를 만든다. 파츠 수는 부품당 7~9개다(상한 10개).
        /// post = 굵은 구조재, plank = 널판, lashing = 결속. 고스트는 셋 다 같은 머티리얼을 넘긴다.
        /// </summary>
        private static void BuildParts(BuildPieceType type, Transform root, Material post, Material plank, Material lashing)
        {
            switch (type)
            {
                case BuildPieceType.Floor: BuildFloor(root, post, plank, lashing); break;
                case BuildPieceType.Wall: BuildWall(root, post, plank, lashing); break;
                case BuildPieceType.Doorway: BuildDoorway(root, post, plank, lashing); break;
                case BuildPieceType.Window: BuildWindow(root, post, plank, lashing); break;
                case BuildPieceType.Stair: BuildStair(root, post, plank); break;
                case BuildPieceType.Roof: BuildRoof(root, post, plank, lashing); break;
                // 상자는 등급마다 크기가 다르다. 여기(고스트/기본 경로)는 항상 소형이다 - 새로 놓는
                // 상자는 언제나 소형이고, 상위 등급은 CreateChestSolid/RebuildChest로만 만들어진다.
                case BuildPieceType.Chest: BuildChest(root, post, plank, lashing, 0); break;
                default: BuildFloor(root, post, plank, lashing); break;
            }
        }

        private static GameObject Part(Transform root, string name, Vector3 position, Vector3 scale, Material material)
        {
            return StructureVisualBuilder.CreateVisualPart(root, name, PrimitiveType.Cube, position, scale, material);
        }

        /// <summary>
        /// 바닥 8파츠: 널 4장(로컬 X 방향으로 깔고 사이에 2cm 널 틈) + 밑에서 받치는 장선 2개 + 결속 2줄.
        /// 윗면이 정확히 y=0이다 - 널의 두께 0.10은 전부 원점 아래로 간다.
        /// </summary>
        private static void BuildFloor(Transform root, Material post, Material plank, Material lashing)
        {
            const float plankThickness = 0.10f;
            const float plankWidth = 0.46f;

            for (int i = 0; i < 4; i++)
            {
                float z = -0.75f + 0.5f * i;
                Part(root, $"Deck_{i}", new Vector3(0f, -plankThickness * 0.5f, z),
                    new Vector3(BuildPieceCatalog.CellSize, plankThickness, plankWidth), plank);
            }

            for (int s = -1; s <= 1; s += 2)
            {
                Part(root, s < 0 ? "Joist_L" : "Joist_R", new Vector3(0.85f * s, -0.15f, 0f),
                    new Vector3(0.16f, plankThickness, BuildPieceCatalog.CellSize), post);

                // 널을 장선에 묶은 밧줄. 윗면 위로 1cm만 나오게 해서 밟는 면(y=0)을 사실상 방해하지 않는다.
                Part(root, s < 0 ? "Lashing_L" : "Lashing_R", new Vector3(0.85f * s, -0.02f, 0f),
                    new Vector3(0.14f, 0.06f, 1.92f), lashing);
            }
        }

        /// <summary>
        /// 벽 8파츠: 모서리 기둥 2개 + 세로 널 4장 + 허리/어깨 높이의 결속 2줄.
        /// 밑면이 y=0, 윗면이 y=2.5(다음 층 바닥이 얹히는 높이)다.
        /// </summary>
        private static void BuildWall(Transform root, Material post, Material plank, Material lashing)
        {
            float halfHeight = WallHeight * 0.5f;

            for (int s = -1; s <= 1; s += 2)
            {
                Part(root, s < 0 ? "Post_L" : "Post_R", new Vector3(0.94f * s, halfHeight, 0f),
                    new Vector3(0.12f, WallHeight, WallHalfThickness * 2f), post);
            }

            float[] plankX = { -0.66f, -0.22f, 0.22f, 0.66f };
            for (int i = 0; i < plankX.Length; i++)
            {
                Part(root, $"Plank_{i}", new Vector3(plankX[i], halfHeight, 0f),
                    new Vector3(0.42f, WallHeight, PlankThickness), plank);
            }

            Part(root, "Lashing_Low", new Vector3(0f, 0.50f, 0f),
                new Vector3(1.90f, 0.08f, WallHalfThickness * 2f), lashing);
            Part(root, "Lashing_High", new Vector3(0f, 2.00f, 0f),
                new Vector3(1.90f, 0.08f, WallHalfThickness * 2f), lashing);
        }

        /// <summary>
        /// 문 7파츠: 문설주 2개 + 상인방 1개 + 상인방 결속 1줄 + 설주 결속 2줄 + 문지방 널 1장.
        /// 구멍은 x ±0.70 / y 0~2.10 (플레이어 캡슐 지름 1.0 + 스킨 여유). 문지방은 **콜라이더가 없다**.
        /// </summary>
        private static void BuildDoorway(Transform root, Material post, Material plank, Material lashing)
        {
            for (int s = -1; s <= 1; s += 2)
            {
                Part(root, s < 0 ? "Jamb_L" : "Jamb_R", new Vector3(JambCenterX * s, WallHeight * 0.5f, 0f),
                    new Vector3(JambHalfWidth * 2f, WallHeight, WallHalfThickness * 2f), post);

                Part(root, s < 0 ? "JambLashing_L" : "JambLashing_R", new Vector3(JambCenterX * s, 1.10f, 0f),
                    new Vector3(0.36f, 0.07f, WallHalfThickness * 2f), lashing);
            }

            float lintelHeight = WallHeight - DoorHeight;                    // 0.40
            Part(root, "Lintel", new Vector3(0f, DoorHeight + lintelHeight * 0.5f, 0f),
                new Vector3(BuildPieceCatalog.CellSize, lintelHeight, PlankThickness), plank);

            Part(root, "LintelLashing", new Vector3(0f, DoorHeight - 0.04f, 0f),
                new Vector3(1.44f, 0.07f, WallHalfThickness * 2f), lashing);

            Part(root, "Threshold", new Vector3(0f, 0.04f, 0f),
                new Vector3(DoorHalfWidth * 2f, 0.08f, WallHalfThickness * 2f), plank);
        }

        /// <summary>
        /// 창문 8파츠: 창설주 2개 + 아래 널 2장 + 결속 1줄 + 창턱 1개 + 상인방 1개 + 위 널 1장.
        /// 구멍은 x ±0.70 / y 1.05~1.75. 사람이 지나갈 수 없는 높이라 콜라이더도 그 형태로 4개를 나눠 붙인다.
        /// </summary>
        private static void BuildWindow(Transform root, Material post, Material plank, Material lashing)
        {
            for (int s = -1; s <= 1; s += 2)
            {
                Part(root, s < 0 ? "Jamb_L" : "Jamb_R", new Vector3(JambCenterX * s, WallHeight * 0.5f, 0f),
                    new Vector3(JambHalfWidth * 2f, WallHeight, WallHalfThickness * 2f), post);
            }

            Part(root, "LowerPlank_0", new Vector3(0f, 0.245f, 0f), new Vector3(1.40f, 0.45f, PlankThickness), plank);
            Part(root, "LowerPlank_1", new Vector3(0f, 0.715f, 0f), new Vector3(1.40f, 0.45f, PlankThickness), plank);
            Part(root, "LowerLashing", new Vector3(0f, 0.475f, 0f),
                new Vector3(1.44f, 0.07f, WallHalfThickness * 2f), lashing);

            Part(root, "Sill", new Vector3(0f, WindowSillTop - 0.05f, 0f),
                new Vector3(1.76f, 0.10f, WallHalfThickness * 2f), post);
            Part(root, "Head", new Vector3(0f, WindowHeadBottom + 0.05f, 0f),
                new Vector3(1.76f, 0.10f, WallHalfThickness * 2f), post);

            float upperHeight = WallHeight - (WindowHeadBottom + 0.10f);     // 0.65
            Part(root, "UpperPlank", new Vector3(0f, WallHeight - upperHeight * 0.5f, 0f),
                new Vector3(1.40f, upperHeight, PlankThickness), plank);
        }

        /// <summary>
        /// 계단 9파츠. 한 단이 밑면(y=0)까지 내려오는 기둥 형태라 옆에서 보면 통짜 목재 계단으로 읽히고,
        /// 별도의 옆판(stringer)이 필요 없다 - 파츠 상한 10개를 지키는 방법이다.
        /// 단마다 구조재/널 머티리얼을 번갈아 써서 단의 경계가 색으로도 읽힌다(형태 + 명도, ArtDirection 2장).
        /// </summary>
        private static void BuildStair(Transform root, Material post, Material plank)
        {
            float rise = BuildPieceCatalog.LevelHeight / StairStepCount;     // 0.2778
            float run = BuildPieceCatalog.CellSize / StairStepCount;         // 0.2222

            for (int i = 0; i < StairStepCount; i++)
            {
                float top = rise * (i + 1);
                Part(root, $"Step_{i}", new Vector3(0f, top * 0.5f, run * i + run * 0.5f),
                    new Vector3(StairWidth, top, run), (i % 2 == 0) ? post : plank);
            }
        }

        /// <summary>
        /// 지붕 7파츠: 기운 몸통(메시 1개) + 처마 보 + 마루 보 + 결속 4줄.
        ///
        /// 몸통은 프리미티브가 아니라 **정점에 경사를 구운 메시 하나**다. 큐브를 기울여 붙이면
        /// (a) 부모 스케일이 비균등해지는 순간 전단으로 찌그러지고 (b) 기운 판의 옆구리가 열려
        /// 삼각형 틈이 생긴다. 밑면(y=0)은 셀 전체를 덮는 평면이라 **방 안에서는 평평한 천장**으로
        /// 보이고, 윗면만 -Z(낮은 쪽)에서 +Z(높은 쪽)로 올라간다. 옆면·마구리가 다 막혀 있어
        /// 비스듬한 판 한 장을 얹었을 때 생기는 빈 틈이 없다.
        ///
        /// 보와 결속은 전부 축에 나란한 상자라 회전이 필요 없다(경사면에 눕는 부재는 만들지 않는다).
        /// </summary>
        private static void BuildRoof(Transform root, Material post, Material plank, Material lashing)
        {
            StructureVisualBuilder.CreateMeshPart(root, "RoofPanel", GetRoofMesh(),
                Vector3.zero, Vector3.one, Quaternion.identity, plank);

            float eaveY = RoofBeamThickness * 0.5f;
            float ridgeY = RoofRise + RoofSlabThickness - RoofBeamThickness * 0.5f;

            // 처마 보 / 마루 보. z를 셀 경계(±1)에 두어 절반이 밖으로 나오게 하면 얇은 처마 턱이 생긴다.
            Part(root, "EaveBeam", new Vector3(0f, eaveY, -HalfCell),
                new Vector3(BuildPieceCatalog.CellSize, RoofBeamThickness, RoofBeamThickness), post);
            Part(root, "RidgeBeam", new Vector3(0f, ridgeY, HalfCell),
                new Vector3(BuildPieceCatalog.CellSize, RoofBeamThickness, RoofBeamThickness), post);

            for (int s = -1; s <= 1; s += 2)
            {
                // 처마 쪽 결속만 3cm 올려 감는다 - 그러지 않으면 띠의 아랫자락이 천장면(y=0) 밑으로
                // 3cm 삐져나와, 방 안에서 올려다볼 때 천장에 혹 두 개가 달린 것처럼 보인다.
                Part(root, s < 0 ? "EaveLashing_L" : "EaveLashing_R",
                    new Vector3(0.72f * s, eaveY + 0.03f, -HalfCell),
                    new Vector3(0.10f, RoofBeamThickness + 0.06f, RoofBeamThickness + 0.06f), lashing);

                Part(root, s < 0 ? "RidgeLashing_L" : "RidgeLashing_R",
                    new Vector3(0.72f * s, ridgeY, HalfCell),
                    new Vector3(0.10f, RoofBeamThickness + 0.06f, RoofBeamThickness + 0.06f), lashing);
            }
        }

        /// <summary>
        /// 보관 상자 7파츠: 몸통 + 뚜껑 + 좌우 모서리 기둥 2개 + 허리 결속 2줄 + 정면 걸쇠.
        /// 밑면이 정확히 y=0이라 바닥 윗면에 딱 앉고, 걸쇠가 붙은 **로컬 +Z가 정면**이다.
        /// 등급(0~3)은 가로·세로·높이만 키운다 - 파츠 구성과 색은 등급과 무관하게 같다.
        /// </summary>
        private static void BuildChest(Transform root, Material post, Material plank, Material lashing, int tier)
        {
            int t = BuildPieceCatalog.ClampChestTier(tier);
            float width = ChestWidths[t];
            float depth = ChestDepths[t];
            float height = ChestHeights[t];

            float bodyHeight = height - ChestLidHeight;
            float halfWidth = width * 0.5f;
            float halfDepth = depth * 0.5f;

            Part(root, "Body", new Vector3(0f, bodyHeight * 0.5f, 0f),
                new Vector3(width, bodyHeight, depth), plank);

            // 뚜껑은 사방으로 3cm씩 내밀어 처마처럼 그림자 선을 만든다(형태로 읽히게).
            Part(root, "Lid", new Vector3(0f, bodyHeight + ChestLidHeight * 0.5f, 0f),
                new Vector3(width + 0.06f, ChestLidHeight, depth + 0.06f), post);

            for (int s = -1; s <= 1; s += 2)
            {
                Part(root, s < 0 ? "Corner_L" : "Corner_R",
                    new Vector3((halfWidth - 0.06f) * s, bodyHeight * 0.5f, 0f),
                    new Vector3(0.12f, bodyHeight, depth + 0.02f), post);
            }

            // 몸통을 두르는 결속 두 줄. 몸통보다 1cm씩 크게 만들어 표면에 묻히지 않게 한다.
            float[] bandY = { bodyHeight * 0.28f, bodyHeight * 0.74f };
            for (int i = 0; i < bandY.Length; i++)
            {
                Part(root, $"Band_{i}", new Vector3(0f, bandY[i], 0f),
                    new Vector3(width + 0.02f, 0.06f, depth + 0.02f), lashing);
            }

            Part(root, "Latch", new Vector3(0f, bodyHeight - 0.03f, halfDepth + 0.02f),
                new Vector3(0.16f, 0.18f, 0.06f), lashing);
        }

        /// <summary>
        /// 상자 콜라이더 하나. 통짜 상자라 구멍이 없고, 뚜껑이 내민 3cm는 콜라이더에 넣지 않는다
        /// (몸통 폭으로만 막아야 옆 칸에 벽을 세울 때 미리보기가 걸리지 않는다).
        /// </summary>
        private static void AddChestCollider(GameObject root, int tier)
        {
            int t = BuildPieceCatalog.ClampChestTier(tier);
            AddBox(root, new Vector3(0f, ChestHeights[t] * 0.5f, 0f),
                new Vector3(ChestWidths[t], ChestHeights[t], ChestDepths[t]));
        }

        /// <summary>
        /// 지붕 몸통 메시를 한 번만 만들어 캐시한다. 형상은 밑면이 평평하고 윗면만 기운 육면체
        /// (쐐기)이며, 여섯 면이 전부 닫혀 있다. 감김은 WorldMeshBuilder가 기준 법선으로 맞춰 주므로
        /// 인덱스 표를 손으로 적지 않는다(이 프로젝트가 왼손 좌표계라 표를 옮기면 안쪽으로 컬링된다).
        /// </summary>
        private static Mesh GetRoofMesh()
        {
            if (roofMesh != null)
                return roofMesh;

            const float h = HalfCell;
            const float t = RoofSlabThickness;
            const float r = RoofRise;

            // 밑면 네 귀퉁이(y=0)와 윗면 네 귀퉁이(-Z쪽은 t, +Z쪽은 t+r).
            var b0 = new Vector3(-h, 0f, -h);
            var b1 = new Vector3(-h, 0f, h);
            var b2 = new Vector3(h, 0f, h);
            var b3 = new Vector3(h, 0f, -h);
            var t0 = new Vector3(-h, t, -h);
            var t1 = new Vector3(-h, t + r, h);
            var t2 = new Vector3(h, t + r, h);
            var t3 = new Vector3(h, t, -h);

            var builder = new WorldMeshBuilder();
            builder.AddQuad(b0, b1, b2, b3, Vector3.down, false);                       // 천장(방 안에서 보이는 면)
            builder.AddQuad(t0, t1, t2, t3, new Vector3(0f, 2f * h, -r), false);        // 경사진 지붕면
            builder.AddQuad(b0, b1, t1, t0, Vector3.left, false);                       // -X 마구리(사다리꼴)
            builder.AddQuad(b3, b2, t2, t3, Vector3.right, false);                      // +X 마구리
            builder.AddQuad(b0, b3, t3, t0, Vector3.back, false);                       // 낮은 쪽 처마 끝
            builder.AddQuad(b1, b2, t2, t1, Vector3.forward, false);                    // 높은 쪽 마루 끝

            roofMesh = builder.Finish("BuildRoofWedge");
            return roofMesh;
        }

        // ─────────────────────────────────────────────────────────────────────────
        //  콜라이더 (실물 전용)
        // ─────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// 루트에 BoxCollider를 직접 붙인다. 파츠에는 붙이지 않는다
        /// (StructureVisualBuilder.CreateVisualPart가 프리미티브의 자동 콜라이더를 이미 제거한다).
        /// 문·창의 구멍 자리에는 콜라이더를 두지 않는 것이 이 함수의 핵심이다.
        /// </summary>
        private static void AddColliders(BuildPieceType type, GameObject root)
        {
            switch (type)
            {
                case BuildPieceType.Floor:
                    AddBox(root, new Vector3(0f, -FloorThickness * 0.5f, 0f),
                        new Vector3(BuildPieceCatalog.CellSize, FloorThickness, BuildPieceCatalog.CellSize));
                    break;

                case BuildPieceType.Wall:
                    AddBox(root, new Vector3(0f, WallHeight * 0.5f, 0f),
                        new Vector3(BuildPieceCatalog.CellSize, WallHeight, WallHalfThickness * 2f));
                    break;

                case BuildPieceType.Doorway:
                {
                    // 좌우 설주 + 문 위 상인방. 구멍(x ±0.70 / y 0~2.10)은 비운다.
                    float sideWidth = HalfCell - DoorHalfWidth;              // 0.30
                    float sideCenter = HalfCell - sideWidth * 0.5f;          // 0.85
                    for (int s = -1; s <= 1; s += 2)
                    {
                        AddBox(root, new Vector3(sideCenter * s, WallHeight * 0.5f, 0f),
                            new Vector3(sideWidth, WallHeight, WallHalfThickness * 2f));
                    }

                    float lintelHeight = WallHeight - DoorHeight;            // 0.40
                    AddBox(root, new Vector3(0f, DoorHeight + lintelHeight * 0.5f, 0f),
                        new Vector3(BuildPieceCatalog.CellSize, lintelHeight, WallHalfThickness * 2f));
                    break;
                }

                case BuildPieceType.Window:
                {
                    AddBox(root, new Vector3(0f, WindowSillTop * 0.5f, 0f),
                        new Vector3(BuildPieceCatalog.CellSize, WindowSillTop, WallHalfThickness * 2f));

                    float upperHeight = WallHeight - WindowHeadBottom;       // 0.75
                    AddBox(root, new Vector3(0f, WallHeight - upperHeight * 0.5f, 0f),
                        new Vector3(BuildPieceCatalog.CellSize, upperHeight, WallHalfThickness * 2f));

                    float sideWidth = HalfCell - DoorHalfWidth;              // 0.30
                    float sideCenter = HalfCell - sideWidth * 0.5f;          // 0.85
                    float openingHeight = WindowHeadBottom - WindowSillTop;  // 0.70
                    for (int s = -1; s <= 1; s += 2)
                    {
                        AddBox(root, new Vector3(sideCenter * s, WindowSillTop + openingHeight * 0.5f, 0f),
                            new Vector3(sideWidth, openingHeight, WallHalfThickness * 2f));
                    }
                    break;
                }

                case BuildPieceType.Stair:
                {
                    // 시각 파츠와 1:1로 같은 계단형 콜라이더. 경사로 하나로 대체하지 않는다(위 StairStepCount 주석).
                    float rise = BuildPieceCatalog.LevelHeight / StairStepCount;
                    float run = BuildPieceCatalog.CellSize / StairStepCount;
                    for (int i = 0; i < StairStepCount; i++)
                    {
                        float top = rise * (i + 1);
                        AddBox(root, new Vector3(0f, top * 0.5f, run * i + run * 0.5f),
                            new Vector3(StairWidth, top, run));
                    }
                    break;
                }

                case BuildPieceType.Roof:
                {
                    // 경사면을 네 단으로 근사한다(계단과 같은 방식 - 기운 BoxCollider를 쓰려면 회전이
                    // 필요하고, 그 회전이 곧 전단 함정으로 이어진다). 단 사이 높이차는
                    // RoofRise/4 = 0.19m라 stepOffset(0.3) 안이므로 지붕에 올라서도 걸리지 않는다.
                    float segment = BuildPieceCatalog.CellSize / RoofColliderSteps;
                    for (int i = 0; i < RoofColliderSteps; i++)
                    {
                        float centerZ = -HalfCell + segment * (i + 0.5f);
                        float top = RoofSlabThickness + RoofRise * ((centerZ + HalfCell) / BuildPieceCatalog.CellSize);
                        AddBox(root, new Vector3(0f, top * 0.5f, centerZ),
                            new Vector3(BuildPieceCatalog.CellSize, top, segment));
                    }
                    break;
                }

                case BuildPieceType.Chest:
                    AddChestCollider(root, 0);
                    break;
            }
        }

        private static void AddBox(GameObject root, Vector3 center, Vector3 size)
        {
            var box = root.AddComponent<BoxCollider>();
            box.center = center;
            box.size = size;
        }
    }
}
