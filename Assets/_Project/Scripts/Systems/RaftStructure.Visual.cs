using UnityEngine;

namespace MakeGame.Systems
{
    /// <summary>
    /// RaftStructure의 외형 조립 partial 분할 파일. 공유 머티리얼/실물 모델(static 메시) 캐시와
    /// 로드 절차(EnsureMaterials/EnsureModelsLoaded/TryLoadPart/CreateModelPart), RebuildVisual과
    /// Build* 조립 메서드(바닥판·부품·유령 칸·제작 예정지 마커·난간·화물·경사로), 콜라이더 적용
    /// (ApplyHullCollider/ApplyDeckSurfaceCollider)을 RaftStructure.cs에서 **내용 수정 없이
    /// 그대로** 옮겨 왔다(순수 이동 리팩토링). static 캐시의 리셋은 본체의 ResetStatics
    /// (SubsystemRegistration 훅)가 계속 담당한다 - 부트스트랩 가드와 한 몸이라 본체에 남겼다.
    /// </summary>
    public partial class RaftStructure : MonoBehaviour
    {
        // ─────────────────────────────────────────────────────────────────────────
        //  외형 조립
        // ─────────────────────────────────────────────────────────────────────────

        // 공유 머티리얼 7개. 파츠 수와 무관하게 이것만 만든다.
        //
        // ★ static인 이유(뗏목 다중화). "파츠 수와 무관하게 이것만"은 뗏목이 하나일 때만 참이었다.
        //   인스턴스 필드로 두면 뗏목 N대에 7N장이 생겨 (a) SRP 배처가 뗏목 경계마다 끊기고
        //   (b) 런타임 생성 Material은 GC 대상이 아니라 불러오기 때마다(DestroyAll + Create)
        //   7장씩 영구히 샌다. 뗏목마다 색이 달라야 할 이유가 없으므로 월드에 한 벌이면 된다.
        //   ResetStatics가 모델 캐시와 함께 비운다.
        private static Material hullWoodMaterial;
        private static Material plankWoodMaterial;
        private static Material fiberMaterial;
        private static Material sailMaterial;
        private static Material cargoMaterial;

        /// <summary>실물 모델의 `metal` 그룹(드럼통 테·닻·모터 몸통·바닥재 못)에 쓰는 금속색.</summary>
        private static Material metalMaterial;

        /// <summary>"제작 예정지" 유령 칸에 쓰는 반투명 머티리얼(불투명 URP Lit은 알파를 버린다).</summary>
        private static Material ghostMaterial;

        // ─────────────────────────────────────────────────────────────────────────
        //  실물 모델 (Resources/Models/raft_*.obj)
        // ─────────────────────────────────────────────────────────────────────────
        //
        // 로드 규칙은 이 프로젝트의 검증된 경로 그대로다(ResourceVisualLibrary.TryLoadTwoPartModel +
        // 프레임당 1회 프로브 가드 + SubsystemRegistration 리셋 훅):
        //  · Resources.Load는 **필드 초기자에서 부르지 않는다**(정적 생성자 시점이라 null이 온다).
        //  · 실패를 영구 래치하지 않는다 - 임포트가 한 프레임 늦어도 다음 프레임에 자연 복구된다.
        //  · 모델이 하나도 없으면 옛 프리미티브 조립으로 폴백해 뗏목이 "안 보이는" 상태가 되지 않는다.
        //
        // 모델 8종 전부 `o` 그룹이 2개다: 첫째 `wood`, 둘째 `metal`(단, raft_sail만 `cloth`).
        // TryLoadTwoPartModel은 이름 규칙(trunk/leaf 등)에 걸리는 것이 없으면 **`o` 등장 순서**로
        // 가르므로 primary = wood, secondary = metal/cloth가 그대로 나온다.
        // Unity 6.5의 실제 임포터는 보통 MeshFilter 1개 + 서브메시 2개로 병합해 오는데, 그때는
        // secondary가 null이고 primary.subMeshCount가 2라 sharedMaterials 두 장으로 칠한다
        // (SeabedFloraSpawner.PlaceCoral / MarineLifeSpawner와 같은 분기).

        /// <summary>바닥판 3종 모델(인덱스 = RaftBaseTileKind - 1). primary = wood 그룹.</summary>
        private static readonly Mesh[] baseTilePrimary = new Mesh[3];
        private static readonly Mesh[] baseTileSecondary = new Mesh[3];

        /// <summary>바닥판 3종의 리소스 경로(위 배열과 같은 순서).</summary>
        private static readonly string[] BaseTileModelPaths =
        {
            "Models/raft_base_wood", "Models/raft_base_buoy", "Models/raft_base_barrel",
        };

        /// <summary>바닥판 3종 모델의 실측 두께(m). 윗면을 FrameTopY에 맞추기 위한 값이다.</summary>
        private static readonly float[] BaseTileModelHeights = { 0.28f, 0.45f, 0.55f };

        private static Mesh floorPrimary;
        private static Mesh floorSecondary;
        private static Mesh sailPrimary;
        private static Mesh sailSecondary;
        private static Mesh rudderPrimary;
        private static Mesh rudderSecondary;
        private static Mesh anchorPrimary;
        private static Mesh anchorSecondary;
        private static Mesh motorPrimary;
        private static Mesh motorSecondary;

        /// <summary>
        /// 프레임당 1회 프로브 가드(SeabedFloraSpawner.probeFrame과 같은 규칙). 같은 프레임에
        /// 뗏목이 여러 번 재조립돼도 Resources.Load는 한 번만 나가고, 실패는 다음 프레임에 다시 시도된다.
        /// </summary>
        private static int modelProbeFrame = -1;

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
            metalMaterial = StructureVisualBuilder.CreateColorMaterial(StructureVisualBuilder.SalvageMetal, "metal");
            ghostMaterial = CreateGhostMaterial(StructureVisualBuilder.SalvageMarkerWhite);
        }

        /// <summary>
        /// URP Lit 머티리얼을 **실제로** 반투명으로 바꾼다. 불투명 패스는 알파를 버리므로 색의 알파만
        /// 낮춰서는 화면에 아무 변화도 없다 - 인스펙터에서 Surface Type을 Transparent로 바꿨을 때 URP가
        /// 내부적으로 하는 일(_Surface/_Blend, 블렌드 모드, ZWrite, 키워드, 렌더 큐, RenderType 태그)을
        /// 코드로 그대로 해 준다. BuildPieceVisualBuilder.CreateGhostMaterial과 같은 절차다
        /// (그쪽은 private이라 부를 수 없어 같은 규칙을 여기에 둔다 - 값이 갈라지지 않게 알파도 같은 0.38).
        /// </summary>
        private static Material CreateGhostMaterial(Color baseColor)
        {
            const float GhostAlpha = 0.38f;

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

        /// <summary>
        /// 아직 못 얻은 뗏목 모델을 한 번 프로브한다. 전부 채워졌으면 즉시 돌아오고, 같은 프레임에
        /// 두 번 이상 로드하지 않는다(modelProbeFrame). 실패를 래치하지 않으므로 임포트가 늦어도
        /// 다음 재조립에서 자연 복구된다.
        /// </summary>
        private static void EnsureModelsLoaded()
        {
            bool anyMissing = floorPrimary == null || sailPrimary == null || rudderPrimary == null
                || anchorPrimary == null || motorPrimary == null;
            for (int i = 0; i < baseTilePrimary.Length && !anyMissing; i++)
                anyMissing = baseTilePrimary[i] == null;

            if (!anyMissing || modelProbeFrame == Time.frameCount)
                return;

            modelProbeFrame = Time.frameCount;

            for (int i = 0; i < BaseTileModelPaths.Length; i++)
            {
                if (baseTilePrimary[i] != null)
                    continue;

                if (ResourceVisualLibrary.TryLoadTwoPartModel(BaseTileModelPaths[i],
                        out Mesh wood, out Mesh metal))
                {
                    baseTilePrimary[i] = wood;
                    baseTileSecondary[i] = metal;   // 병합 임포트면 null - 서브메시 분기가 처리한다
                }
            }

            TryLoadPart("Models/raft_floor", ref floorPrimary, ref floorSecondary);
            TryLoadPart("Models/raft_sail", ref sailPrimary, ref sailSecondary);
            TryLoadPart("Models/raft_rudder", ref rudderPrimary, ref rudderSecondary);
            TryLoadPart("Models/raft_anchor", ref anchorPrimary, ref anchorSecondary);
            TryLoadPart("Models/raft_motor", ref motorPrimary, ref motorSecondary);
        }

        /// <summary>모델 하나를 (이미 있으면 건너뛰고) 프로브한다. EnsureModelsLoaded 전용 헬퍼.</summary>
        private static void TryLoadPart(string resourcePath, ref Mesh primary, ref Mesh secondary)
        {
            if (primary != null)
                return;

            if (ResourceVisualLibrary.TryLoadTwoPartModel(resourcePath, out Mesh first, out Mesh second))
            {
                primary = first;
                secondary = second;
            }
        }

        /// <summary>
        /// 두 색짜리 실물 모델 하나를 붙인다. 개별 메시 임포트면 파츠 2개(각각 한 색), 병합 임포트
        /// (서브메시 2)면 파츠 1개 + sharedMaterials 두 장이다. 어느 쪽이든 콜라이더는 생기지 않는다
        /// (StructureVisualBuilder.CreateMeshPart는 프리미티브를 거치지 않는다).
        /// </summary>
        private void CreateModelPart(string name, Mesh primary, Mesh secondary,
            Material primaryMaterial, Material secondaryMaterial,
            Vector3 localPosition, Quaternion localRotation)
        {
            if (primary == null)
                return;

            var part = StructureVisualBuilder.CreateMeshPart(visualRoot, name, primary,
                localPosition, Vector3.one, localRotation, primaryMaterial);

            if (secondary != null)
            {
                StructureVisualBuilder.CreateMeshPart(visualRoot, name + "_B", secondary,
                    localPosition, Vector3.one, localRotation, secondaryMaterial);
                return;
            }

            if (primary.subMeshCount < 2)
                return;

            var renderer = part.GetComponent<MeshRenderer>();
            if (renderer != null)
                renderer.sharedMaterials = new[] { primaryMaterial, secondaryMaterial };
        }

        /// <summary>
        /// 지금 상태(바닥판 칸 수 + 장착 부품)의 뗏목을 통째로 다시 만든다.
        /// 기존 파츠는 Destroy 전에 SetActive(false)를 먼저 부른다 - Destroy는 프레임 끝까지 지연되므로,
        /// 같은 프레임에 새로 만드는 승선 발판 콜라이더와 옛 것이 겹쳐 있는 시간을 없앤다(AGENT_BRIEF 4장).
        /// </summary>
        private void RebuildVisual()
        {
            EnsureMaterials();
            EnsureModelsLoaded();

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

            if (baseTileCount <= 0)
            {
                // 바닥판 0칸: 뗏목이 아직 없다. 대신 **여기서 만들 수 있다**는 표시를 세운다
                // (밧줄로 묶은 말뚝 네 개 + 첫 칸 자리의 반투명 유령 바닥판) - 이게 없으면 해안
                // 어디서도 뗏목 UI를 열 수 없고, 플레이어는 뗏목이라는 기능의 존재조차 알 수 없다.
                BuildSiteMarker();
            }
            else
            {
                BuildHull();
                BuildLashings();
                BuildBaseTiles();
                BuildFloorPlanks();

                // 승선 발판은 바닥판이 한 칸이라도 있으면 놓는다. 선체 윗면이 이미 갑판 높이(0.72)라
                // CharacterController.stepOffset(씬 값 0.3)으로는 올라설 수 없어서, 발판이 없으면
                // "뗏목은 보이는데 탈 수가 없는" 상태가 된다.
                BuildBoardingRamp();

                // 난간과 보급품은 **온전한 갑판이 생긴 뒤**에만 올린다. 반만 깔린 행 위에 세우면
                // 빈 칸 위 허공에 뜬다(판정은 DeckLocalSize 하나에서 유도한다).
                if (HasDeck)
                {
                    BuildRailings();
                    BuildCargo();
                }

                BuildInstalledParts();
            }

            ApplyHullCollider();

            // 갑판 콜라이더가 방금 바뀌었다. 구독자가 이 프레임에 레이캐스트를 쏠 수 있으므로
            // 물리 씬을 먼저 맞춘다(Physics.autoSyncTransforms = false - AGENT_BRIEF 4장).
            Physics.SyncTransforms();

            DeckRebuilt?.Invoke();
        }

        /// <summary>
        /// 부력 통나무. 바닥판이 깔린 길이만큼만 깐다 - 바닥판 2칸짜리 뗏목 밑에 8m 통나무가 있으면
        /// "아직 만드는 중"이 보이지 않는다. 폭은 바닥판이 놓인 열만큼만 넓어진다.
        /// </summary>
        private void BuildHull()
        {
            GetHullExtent(out float minZ, out float length, out float minX, out float width);

            int logCount = Mathf.Max(1, Mathf.RoundToInt(width / (LogDiameter * 1.05f)));
            float spacing = width / logCount;

            for (int i = 0; i < logCount; i++)
            {
                float x = minX + spacing * (i + 0.5f);

                StructureVisualBuilder.CreateVisualPart(visualRoot, $"HullLog{i}", PrimitiveType.Cylinder,
                    new Vector3(x, LogCenterY, minZ + length * 0.5f),
                    new Vector3(LogDiameter, length * 0.5f, LogDiameter),
                    hullWoodMaterial, Quaternion.Euler(90f, 0f, 0f));
            }

            // 통나무를 가로질러 묶는 가로보. 앞뒤 끝에서 조금씩 들어온 자리에 둔다.
            for (int side = -1; side <= 1; side += 2)
            {
                float z = minZ + length * (side < 0 ? 0.15f : 0.85f);
                StructureVisualBuilder.CreateVisualPart(visualRoot, $"Crossbeam{side}", PrimitiveType.Cube,
                    new Vector3(minX + width * 0.5f, CrossbeamY, z),
                    new Vector3(width + 0.3f, 0.12f, 0.32f), hullWoodMaterial);
            }
        }

        /// <summary>
        /// 지금 깔린 바닥판이 차지하는 로컬 범위(z 시작/길이, x 시작/폭). 선체·묶음·콜라이더·승선
        /// 판정이 전부 이 하나를 공유한다. **놓인 칸의 실제 경계**이므로 임의 형태에서도 맞는다.
        /// 한 칸도 없으면 첫 칸(옛 순차 격자의 시작 자리) 한 칸짜리 범위를 준다 - 제작 예정지 표시가
        /// 쓸 자리다.
        /// </summary>
        public void GetHullExtent(out float minZ, out float length, out float minX, out float width)
        {
            if (!GetCellBounds(out int cellMinX, out int cellMaxX, out int cellMinZ, out int cellMaxZ))
            {
                cellMinX = cellMaxX = LegacyFirstCellX;
                cellMinZ = cellMaxZ = LegacyFirstCellZ;
            }

            minX = cellMinX * BaseTilePitch;
            width = (cellMaxX - cellMinX + 1) * BaseTilePitch;
            minZ = cellMinZ * BaseTilePitch;
            length = (cellMaxZ - cellMinZ + 1) * BaseTilePitch;
        }

        /// <summary>통나무를 가로로 묶은 밧줄 띠. 통나무 윗면(0.5)을 감싸도록 얹는다.</summary>
        private void BuildLashings()
        {
            GetHullExtent(out float minZ, out float length, out float minX, out float width);

            const int LashingCount = 2;
            for (int i = 0; i < LashingCount; i++)
            {
                float z = minZ + length * (i + 0.5f) / LashingCount;
                StructureVisualBuilder.CreateVisualPart(visualRoot, $"Lashing{i}", PrimitiveType.Cube,
                    new Vector3(minX + width * 0.5f, 0.44f, z),
                    new Vector3(width + 0.12f, 0.14f, 0.18f), fiberMaterial);
            }
        }

        /// <summary>
        /// 바닥판. 격자(BaseGridColumns x BaseGridRows) 순서대로 고물(-Z) 왼쪽부터 채워진다 -
        /// 칸을 하나 놓을 때마다 눈앞에서 판이 하나씩 붙는 것이 보이는 것이 이 배치의 목적이다.
        /// </summary>
        private void BuildBaseTiles()
        {
            for (int i = 0; i < baseTileCount; i++)
            {
                RaftBaseTileKind kind = (RaftBaseTileKind)(tiles[i].code & KindMask);
                int slot = (int)kind - 1;
                if (slot < 0 || slot >= baseTilePrimary.Length)
                    slot = 0;

                Vector3 center = GetBaseTileCenter(i);
                Mesh model = baseTilePrimary[slot];

                if (model != null)
                {
                    // 실물 모델은 원점이 **칸 중심 + 밑면**이다(bbox y가 0부터 시작한다). 종류마다
                    // 두께가 달라도 윗면이 항상 FrameTopY가 되도록 밑면을 두께만큼 내려 놓는다.
                    CreateModelPart($"BaseTile{i}_{kind}", model, baseTileSecondary[slot],
                        plankWoodMaterial, metalMaterial,
                        new Vector3(center.x, FrameTopY - BaseTileModelHeights[slot], center.z),
                        Quaternion.identity);
                    continue;
                }

                // 폴백(모델 임포트 전/실패): 예전 프리미티브 널판. 뗏목이 "안 보이는" 상태를 만들지 않는다.
                StructureVisualBuilder.CreateVisualPart(visualRoot, $"BaseTile{i}", PrimitiveType.Cube,
                    new Vector3(center.x, DeckPlankY, center.z),
                    new Vector3(BaseTilePitch - 0.06f, DeckPlankThickness, BaseTilePitch - 0.06f),
                    plankWoodMaterial);
            }
        }

        /// <summary>
        /// 순번 index의 칸 중심(로컬 XZ, y는 0). 바닥판·바닥재가 전부 이 하나를 쓴다 - 좌표 계산이
        /// 두 벌이면 바닥재가 바닥판에서 살짝 어긋나 이음매가 벌어진다.
        /// 자리는 이제 순번이 아니라 **칸에 저장된 좌표**가 정한다(GetCellCenterLocal).
        /// </summary>
        private Vector3 GetBaseTileCenter(int index)
        {
            if (!GetBaseTileCell(index, out int cx, out int cz))
                return GetCellCenterLocal(LegacyFirstCellX, LegacyFirstCellZ);

            return GetCellCenterLocal(cx, cz);
        }

        /// <summary>
        /// 갑판 바닥재(raft_floor). 바닥판 골조 윗면(FrameTopY)에 얹으면 그 윗면이 정확히
        /// DeckSurfaceY(0.72) = 플레이어가 딛는 면이 된다. 바닥재가 없는 칸은 8cm 낮은 골조가 노출된다.
        /// </summary>
        private void BuildFloorPlanks()
        {
            if (floorPrimary == null)
                return;

            for (int i = 0; i < baseTileCount; i++)
            {
                if ((tiles[i].code & FloorBit) == 0)
                    continue;

                Vector3 center = GetBaseTileCenter(i);
                CreateModelPart($"Floor{i}", floorPrimary, floorSecondary,
                    plankWoodMaterial, metalMaterial,
                    new Vector3(center.x, FrameTopY, center.z), Quaternion.identity);
            }
        }

        /// <summary>
        /// "여기서 뗏목을 만든다" 표시(바닥판 0칸일 때만). 새 모델을 만들지 않는다 -
        /// 말뚝은 프리미티브(StructureVisualBuilder.CreateLashedPost), 유령 칸은 이미 있는
        /// raft_base_wood 메시를 반투명 머티리얼로 한 번 더 그린 것이다.
        ///
        /// 유령 칸은 **첫 칸이 실제로 놓일 자리**(격자 순번 0 = 고물 좌현)에 정확히 겹쳐 둔다.
        /// 이 오브젝트가 곧 상호작용 조준 대상이기도 하다(ApplyHullCollider의 0칸 분기 참고).
        /// </summary>
        private void BuildSiteMarker()
        {
            // 네 귀퉁이의 말뚝. 뗏목이 차지할 넓이를 눈으로 알려 준다(물 위 60cm).
            // 머티리얼은 공유본만 쓴다 - StructureVisualBuilder.CreateLashedPost는 Color 오버로드라
            // 말뚝마다 머티리얼을 새로 만든다(이 클래스가 피하려는 바로 그 비용이다).
            for (int sx = -1; sx <= 1; sx += 2)
            {
                for (int sz = -1; sz <= 1; sz += 2)
                {
                    Vector3 stakeAt = new Vector3(
                        sx * (DeckWidth * 0.5f - 0.12f), 0.3f, sz * (DeckLength * 0.5f - 0.12f));

                    var stake = StructureVisualBuilder.CreateVisualPart(visualRoot, $"SiteStake{sx}_{sz}",
                        PrimitiveType.Cube, stakeAt, new Vector3(0.16f, 1.2f, 0.16f), hullWoodMaterial);

                    StructureVisualBuilder.CreateVisualPart(stake.transform, "Lashing", PrimitiveType.Cube,
                        new Vector3(0f, 0.34f, 0f), new Vector3(1.35f, 0.09f, 1.35f), fiberMaterial);
                }
            }

            // 말뚝을 잇는 밧줄(윗변 네 줄). 말뚝만 있으면 "네 개의 기둥"으로 읽히고, 줄이 있어야
            // "여기가 한 구획"으로 읽힌다.
            float ropeY = 0.78f;
            float halfX = DeckWidth * 0.5f - 0.12f;
            float halfZ = DeckLength * 0.5f - 0.12f;
            for (int side = -1; side <= 1; side += 2)
            {
                StructureVisualBuilder.CreateVisualPart(visualRoot, $"SiteRopeX{side}", PrimitiveType.Cube,
                    new Vector3(side * halfX, ropeY, 0f), new Vector3(0.05f, 0.05f, halfZ * 2f), fiberMaterial);
                StructureVisualBuilder.CreateVisualPart(visualRoot, $"SiteRopeZ{side}", PrimitiveType.Cube,
                    new Vector3(0f, ropeY, side * halfZ), new Vector3(halfX * 2f, 0.05f, 0.05f), fiberMaterial);
            }

            // 첫 칸의 유령. 실물 통나무 바닥판을 **놓일 자리 그대로** 반투명하게 한 번 더 그린다
            // (두 색 그룹 모두 같은 유령 머티리얼 - 병합 임포트에서 서브메시 하나가 빠져 반쪽만
            // 보이는 일이 없게 CreateModelPart를 그대로 쓴다). 모델이 아직 없으면 같은 크기의 큐브다.
            Vector3 center = GetCellCenterLocal(LegacyFirstCellX, LegacyFirstCellZ);
            if (baseTilePrimary[0] != null)
            {
                CreateModelPart("SiteGhostTile", baseTilePrimary[0], baseTileSecondary[0],
                    ghostMaterial, ghostMaterial,
                    new Vector3(center.x, FrameTopY - BaseTileModelHeights[0], center.z),
                    Quaternion.identity);
            }
            else
            {
                StructureVisualBuilder.CreateVisualPart(visualRoot, "SiteGhostTile", PrimitiveType.Cube,
                    new Vector3(center.x, FrameTopY - BaseTileModelHeights[0] * 0.5f, center.z),
                    new Vector3(BaseTilePitch - 0.06f, BaseTileModelHeights[0], BaseTilePitch - 0.06f),
                    ghostMaterial);
            }
        }

        /// <summary>
        /// 승선 발판. CharacterController의 stepOffset은 씬 값 0.3이라 갑판(0.72)에 그냥 올라설 수 없다.
        /// 해변 실측 높이보다 RampFootDig만큼 파묻은 밑동(rampFootLocalY)에서 갑판까지 이어지는
        /// 경사판을 놓고, 여기에만 콜라이더를 남긴다(파묻는 이유는 TryAnchorToShore 주석).
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
        /// 장착된 부품을 외형으로 옮긴다. 부품 하나 = 파츠 한 묶음이라, 다음 웨이브가 부품을 추가할 때
        /// 여기 case를 하나 늘리면 된다(진행 단계별 분기가 아니다).
        /// </summary>
        private void BuildInstalledParts()
        {
            if (HasPart(RaftPart.Sail))
                BuildMastAndSail();

            if (HasPart(RaftPart.Rudder))
                BuildRudder();

            if (HasPart(RaftPart.Oar))
                BuildOars();

            if (HasPart(RaftPart.Anchor))
                BuildAnchor();

            if (HasPart(RaftPart.Motor))
                BuildMotor();

            // 조타 자리는 "잡을 것"이 하나라도 달렸을 때 생긴다. 노만 있어도 생기는 것이 중요하다 -
            // Stranded Deep에서 노(paddle)가 곧 조종 수단이기 때문이다.
            if (HasSteeringStation)
                BuildHelmStation();
        }

        /// <summary>고물에서 잡을 것(노·키·모터)이 하나라도 달려 있는가. 조타 자리 생성 조건이다.</summary>
        public bool HasSteeringStation =>
            HasPart(RaftPart.Oar) || HasPart(RaftPart.Rudder) || HasPart(RaftPart.Motor);

        /// <summary>
        /// 조타 자리(고물 뒤편의 트리거 상자 하나). 실물 파츠를 새로 만들지 않는다 - 키·모터·노가
        /// 이미 그 자리에 서 있으므로, 여기서 필요한 것은 "그 자리를 조준했다"를 알리는 콜라이더뿐이다.
        ///
        /// **로컬 z를 고물 끝(-DeckLength/2)보다 뒤로 뺀다.** 갑판 셀은 |z| &lt;= DeckLength/2 안쪽에만
        /// 있으므로, 이렇게 두면 BuildingSystem.CastBuildRay가 이 상자를 "뗏목에 막힘"으로 보는 일이
        /// 갑판 건축을 방해하지 않는다(갑판 칸 위에 두면 그 칸에 집을 못 짓게 된다 - RaftHelm 주석).
        /// 트리거인 이유도 같은 주석에 있다(승선 발판이 이 아래를 지난다).
        /// </summary>
        private void BuildHelmStation()
        {
            var stationObject = new GameObject("HelmStation");
            stationObject.transform.SetParent(visualRoot, false);
            stationObject.transform.localPosition =
                new Vector3(0f, DeckSurfaceY + 0.55f, -DeckLength * 0.5f - 0.45f);

            var box = stationObject.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = new Vector3(2.4f, 1.6f, 0.8f);

            var helm = stationObject.AddComponent<RaftHelm>();
            helm.sailing = sailing != null ? sailing : GetComponent<RaftSailing>();
        }

        /// <summary>
        /// 돛대 + 돛(raft_sail 모델). 모델 원점은 **접지 중심**(밑면 y = 0)이라 갑판 윗면에 그대로
        /// 얹으면 된다. 폭 1.8m라 갑판 폭 4.0m 한가운데 세워도 양옆에 1.1m씩 통로가 남는다.
        /// 앞뒤 지지 밧줄만 프리미티브로 이어 붙인다(모델에 없는 부분 - 돛대와 갑판을 잇는 신호).
        /// 모델이 아직 없으면 예전 프리미티브 조립(BuildMastAndSailPrimitive)으로 폴백한다.
        /// </summary>
        private void BuildMastAndSail()
        {
            const float SailZ = 0.8f;

            if (sailPrimary == null)
            {
                BuildMastAndSailPrimitive();
                return;
            }

            CreateModelPart("Sail", sailPrimary, sailSecondary, hullWoodMaterial, sailMaterial,
                new Vector3(0f, DeckSurfaceY, SailZ), Quaternion.identity);

            StructureVisualBuilder.CreateVisualPart(visualRoot, "MastFootLashing", PrimitiveType.Cube,
                new Vector3(0f, DeckSurfaceY + 0.18f, SailZ),
                new Vector3(0.44f, 0.15f, 0.44f), fiberMaterial);

            // 모델 실측: 돛대 높이 3.2m. 꼭대기에서 고물/뱃머리로 지지줄을 내린다.
            Vector3 mastTop = new Vector3(0f, DeckSurfaceY + 3.1f, SailZ);
            BuildStay("StayAft", mastTop, new Vector3(0f, DeckSurfaceY + 0.1f, -DeckLength * 0.5f + 0.5f));
            BuildStay("StayFore", mastTop, new Vector3(0f, DeckSurfaceY + 0.1f, DeckLength * 0.5f - 0.5f));
        }

        /// <summary>돛 모델을 못 얻었을 때의 폴백(옛 프리미티브 돛대 + 활대 + 돛).</summary>
        private void BuildMastAndSailPrimitive()
        {
            const float MastHeight = 3.6f;
            const float MastZ = 0.6f;

            StructureVisualBuilder.CreateVisualPart(visualRoot, "Mast", PrimitiveType.Cube,
                new Vector3(0f, DeckSurfaceY + MastHeight * 0.5f, MastZ),
                new Vector3(0.26f, MastHeight, 0.26f), hullWoodMaterial);

            StructureVisualBuilder.CreateVisualPart(visualRoot, "MastFootLashing", PrimitiveType.Cube,
                new Vector3(0f, DeckSurfaceY + 0.18f, MastZ),
                new Vector3(0.44f, 0.15f, 0.44f), fiberMaterial);

            float yardY = DeckSurfaceY + MastHeight - 0.25f;

            StructureVisualBuilder.CreateVisualPart(visualRoot, "Yard", PrimitiveType.Cube,
                new Vector3(0f, yardY, MastZ), new Vector3(3.2f, 0.14f, 0.14f), hullWoodMaterial);

            StructureVisualBuilder.CreateVisualPart(visualRoot, "Sail", PrimitiveType.Cube,
                new Vector3(0f, yardY - 1.15f, MastZ + 0.08f),
                new Vector3(3.0f, 2.1f, 0.06f), sailMaterial);

            Vector3 mastTop = new Vector3(0f, DeckSurfaceY + MastHeight - 0.1f, MastZ);
            BuildStay("StayAft", mastTop, new Vector3(0f, DeckSurfaceY + 0.1f, -DeckLength * 0.5f + 0.5f));
            BuildStay("StayFore", mastTop, new Vector3(0f, DeckSurfaceY + 0.1f, DeckLength * 0.5f - 0.5f));
        }

        /// <summary>
        /// 고물 한가운데 붙는 키(raft_rudder 모델). 모델은 원점이 접지 중심이고 위로 1.4m 자란다
        /// (자루 끝 -Z 0.6m). 밑동을 해수면(로컬 y = -0.2)에 두면 날은 물에 잠기고 손잡이는 갑판
        /// 윗면(0.72)보다 위로 올라와 "잡을 수 있는 자루"로 읽힌다.
        /// </summary>
        private void BuildRudder()
        {
            if (rudderPrimary != null)
            {
                CreateModelPart("Rudder", rudderPrimary, rudderSecondary, hullWoodMaterial, metalMaterial,
                    new Vector3(0f, -0.2f, -DeckLength * 0.5f - 0.1f), Quaternion.identity);
                return;
            }

            BuildRudderPrimitive();
        }

        /// <summary>키 모델을 못 얻었을 때의 폴백(옛 프리미티브 방향타).</summary>
        private void BuildRudderPrimitive()
        {
            StructureVisualBuilder.CreateVisualPart(visualRoot, "RudderShaft", PrimitiveType.Cube,
                new Vector3(0.95f, DeckSurfaceY + 0.15f, -DeckLength * 0.5f + 0.25f),
                new Vector3(0.12f, 1.7f, 0.12f), hullWoodMaterial, Quaternion.Euler(38f, 0f, 0f));

            StructureVisualBuilder.CreateVisualPart(visualRoot, "RudderBlade", PrimitiveType.Cube,
                new Vector3(0.95f, -0.2f, -DeckLength * 0.5f - 0.6f),
                new Vector3(0.1f, 0.75f, 0.5f), hullWoodMaterial, Quaternion.Euler(38f, 0f, 0f));
        }

        /// <summary>좌우 뱃전에 걸쳐 둔 노 두 자루.</summary>
        private void BuildOars()
        {
            for (int side = -1; side <= 1; side += 2)
            {
                float x = side * (DeckWidth * 0.5f - 0.25f);

                StructureVisualBuilder.CreateVisualPart(visualRoot, $"OarShaft{side}", PrimitiveType.Cylinder,
                    new Vector3(x, DeckSurfaceY + 0.12f, -0.6f),
                    new Vector3(0.09f, 1.3f, 0.09f), hullWoodMaterial, Quaternion.Euler(90f, 0f, side * 8f));

                StructureVisualBuilder.CreateVisualPart(visualRoot, $"OarBlade{side}", PrimitiveType.Cube,
                    new Vector3(x, DeckSurfaceY + 0.12f, -2.2f),
                    new Vector3(0.28f, 0.05f, 0.7f), plankWoodMaterial, Quaternion.Euler(0f, 0f, side * 8f));
            }
        }

        /// <summary>
        /// 뱃머리 좌현 갑판에 얹은 닻(raft_anchor 모델, 0.6 × 0.8 × 0.6). 원점이 접지 중심이라
        /// 갑판 윗면에 그대로 놓는다. x = -1.2면 갑판 반폭 2.0 안에 여유 0.5m가 남는다.
        /// </summary>
        private void BuildAnchor()
        {
            if (anchorPrimary != null)
            {
                Vector3 anchorAt = new Vector3(-1.2f, DeckSurfaceY, DeckLength * 0.5f - 1.0f);

                CreateModelPart("Anchor", anchorPrimary, anchorSecondary, hullWoodMaterial, metalMaterial,
                    anchorAt, Quaternion.Euler(0f, 24f, 0f));

                StructureVisualBuilder.CreateVisualPart(visualRoot, "AnchorRope", PrimitiveType.Cylinder,
                    anchorAt + new Vector3(0.6f, 0.06f, 0f),
                    new Vector3(0.42f, 0.09f, 0.42f), fiberMaterial);
                return;
            }

            BuildAnchorPrimitive();
        }

        /// <summary>닻 모델을 못 얻었을 때의 폴백(옛 프리미티브 돌닻 + 밧줄 뭉치).</summary>
        private void BuildAnchorPrimitive()
        {
            Vector3 anchorSpot = new Vector3(-1.5f, DeckSurfaceY + 0.22f, DeckLength * 0.5f - 1.0f);

            StructureVisualBuilder.CreateVisualPart(visualRoot, "AnchorStone", PrimitiveType.Cube,
                anchorSpot, new Vector3(0.55f, 0.42f, 0.55f), cargoMaterial, Quaternion.Euler(0f, 24f, 0f));

            StructureVisualBuilder.CreateVisualPart(visualRoot, "AnchorRope", PrimitiveType.Cylinder,
                anchorSpot + new Vector3(0.55f, -0.06f, 0f),
                new Vector3(0.42f, 0.09f, 0.42f), fiberMaterial);
        }

        /// <summary>
        /// 고물 우현 뱃전에 매다는 선외기(raft_motor 모델). 원점이 접지 중심(= 프로펠러 끝)이고 위로
        /// 1.1m 자라며, 조종 손잡이(wood 그룹)가 +Z 쪽으로 0.77m 뻗는다. 밑동을 로컬 y = -0.35에 두면
        /// 프로펠러는 물속, 손잡이는 갑판 높이 근처(0.37~0.65)에 와서 "고물에서 잡는 자루"가 된다.
        /// 키(x = 0)와 자리가 겹치지 않도록 우현(x = +1.0)에 단다.
        /// </summary>
        private void BuildMotor()
        {
            if (motorPrimary != null)
            {
                CreateModelPart("Motor", motorPrimary, motorSecondary, hullWoodMaterial, metalMaterial,
                    new Vector3(1.0f, -0.35f, -DeckLength * 0.5f - 0.1f), Quaternion.identity);
                return;
            }

            BuildMotorPrimitive();
        }

        /// <summary>모터 모델을 못 얻었을 때의 폴백(옛 프리미티브 선외기).</summary>
        private void BuildMotorPrimitive()
        {
            Vector3 motorSpot = new Vector3(-0.95f, DeckSurfaceY + 0.3f, -DeckLength * 0.5f + 0.3f);

            StructureVisualBuilder.CreateVisualPart(visualRoot, "MotorBody", PrimitiveType.Cube,
                motorSpot, new Vector3(0.42f, 0.6f, 0.5f), cargoMaterial);

            StructureVisualBuilder.CreateVisualPart(visualRoot, "MotorShaft", PrimitiveType.Cylinder,
                motorSpot + new Vector3(0f, -0.55f, -0.25f),
                new Vector3(0.14f, 0.45f, 0.14f), hullWoodMaterial, Quaternion.Euler(20f, 0f, 0f));
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
        /// 난간. 바닥판이 놓인 **칸의 바깥 변**마다 세운다(빈 칸 위 허공에 뜨지 않게).
        /// 콜라이더를 붙이지 않는다 - 붙이면 갑판에 올라간 플레이어가 갇힌다.
        /// </summary>
        private void BuildRailings()
        {
            if (!GetCellBounds(out int minCellX, out int maxCellX, out int minCellZ, out int maxCellZ))
                return;

            float railY = DeckSurfaceY + 0.45f;
            float inset = BaseTilePitch * 0.5f - 0.08f;

            // ★ 사각형이 아니라 **칸의 바깥 변**마다 세운다. 예전처럼 갑판 사각형의 양옆에 긴 막대를
            //   하나씩 놓으면, 그 사각형 안에 빈 칸이 있는 모양(반만 깐 행 · ㄱ자 뗏목)에서 난간이
            //   물 위 허공에 뜬다.
            for (int i = 0; i < tiles.Count; i++)
            {
                int cellX = tiles[i].x;
                int cellZ = tiles[i].z;
                Vector3 center = GetCellCenterLocal(cellX, cellZ);

                for (int side = -1; side <= 1; side += 2)
                {
                    // 옆 칸이 있으면 그건 바깥이 아니라 이음매다.
                    if (HasBaseTileAt(cellX + side, cellZ))
                        continue;

                    float railX = center.x + side * inset;

                    StructureVisualBuilder.CreateVisualPart(visualRoot, $"RailBar{cellX}_{cellZ}_{side}",
                        PrimitiveType.Cube, new Vector3(railX, railY, center.z),
                        new Vector3(0.09f, 0.09f, BaseTilePitch), plankWoodMaterial);

                    StructureVisualBuilder.CreateVisualPart(visualRoot, $"RailPost{cellX}_{cellZ}_{side}",
                        PrimitiveType.Cube, new Vector3(railX, DeckSurfaceY + 0.22f, center.z),
                        new Vector3(0.11f, 0.45f, 0.11f), plankWoodMaterial);
                }
            }

            // 바닥판을 끝까지 깔았을 때만 뱃머리 난간이 닫힌다(완성 신호).
            if (tiles.Count < MaxBaseTiles)
                return;

            for (int i = 0; i < tiles.Count; i++)
            {
                int cellX = tiles[i].x;
                int cellZ = tiles[i].z;
                if (HasBaseTileAt(cellX, cellZ + 1))
                    continue;

                Vector3 center = GetCellCenterLocal(cellX, cellZ);
                StructureVisualBuilder.CreateVisualPart(visualRoot, $"BowRail{cellX}_{cellZ}",
                    PrimitiveType.Cube, new Vector3(center.x, railY, center.z + inset),
                    new Vector3(BaseTilePitch - 0.16f, 0.09f, 0.09f), plankWoodMaterial);
            }
        }

        /// <summary>갑판 위 보급품. 갑판이 생기면 궤짝이, 바닥판을 다 깔면 물통이 하나 더 놓인다.</summary>
        private void BuildCargo()
        {
            // 자리는 **실재하는 칸** 위에서 고른다. 상수로 박아 두면 그 칸이 없는 모양에서
            // 궤짝이 물 위에 뜬다(순차 격자 8칸에서는 예전과 같은 자리로 떨어진다).
            if (FindCornerCell(1, -1, out int crateX, out int crateZ))
            {
                Vector3 crateCenter = GetCellCenterLocal(crateX, crateZ);
                StructureVisualBuilder.CreateVisualPart(visualRoot, "SupplyCrate", PrimitiveType.Cube,
                    new Vector3(crateCenter.x + 0.65f, DeckSurfaceY + 0.31f, crateCenter.z + 0.7f),
                    new Vector3(0.62f, 0.62f, 0.62f), plankWoodMaterial, Quaternion.Euler(0f, 18f, 0f));
            }

            if (tiles.Count < MaxBaseTiles)
                return;

            if (FindCornerCell(-1, -1, out int barrelX, out int barrelZ))
            {
                Vector3 barrelCenter = GetCellCenterLocal(barrelX, barrelZ);
                StructureVisualBuilder.CreateVisualPart(visualRoot, "SupplyBarrel", PrimitiveType.Cylinder,
                    new Vector3(barrelCenter.x - 0.65f, DeckSurfaceY + 0.35f, barrelCenter.z + 0.4f),
                    new Vector3(0.52f, 0.35f, 0.52f), cargoMaterial);
            }
        }

        /// <summary>
        /// 지정한 방향의 모퉁이에 가장 가까운 **실재하는** 칸. dirX/dirZ가 +1이면 큰 쪽, -1이면 작은 쪽이다.
        /// 갑판 위 장식·부품이 빈 칸 위에 놓이지 않게 하는 공용 자리 고르개다.
        /// </summary>
        private bool FindCornerCell(int dirX, int dirZ, out int cellX, out int cellZ)
        {
            cellX = 0;
            cellZ = 0;
            if (tiles.Count == 0)
                return false;

            int bestScore = int.MinValue;
            for (int i = 0; i < tiles.Count; i++)
            {
                // z를 먼저(가중치 8) 보고 x로 가른다. 격자 폭이 4칸이라 8이면 순위가 섞이지 않는다.
                int score = tiles[i].z * dirZ * 8 + tiles[i].x * dirX;
                if (score <= bestScore)
                    continue;

                bestScore = score;
                cellX = tiles[i].x;
                cellZ = tiles[i].z;
            }

            return true;
        }

        /// <summary>
        /// 선체 콜라이더를 현재 상태에 맞춘다. 이것이 (1) 플레이어가 올라서는 발판이자
        /// (2) 상호작용 레이캐스트가 맞는 대상이다.
        /// 바닥판이 0칸이면 뗏목이 실재하지 않으므로 콜라이더를 **끈다** - 켜 두면 아무것도 없는
        /// 물 위에 보이지 않는 벽이 선다.
        /// </summary>
        private void ApplyHullCollider()
        {
            if (hullCollider == null)
                return;

            if (baseTileCount <= 0)
            {
                // [제작 예정지] 예전에는 여기서 콜라이더를 껐다("아무것도 없는 물 위의 보이지 않는 벽"
                // 방지). 그런데 뗏목 제작 UI는 뗏목을 **조준해서** 여는 창이라, 0칸에서 콜라이더가
                // 없으면 첫 바닥판을 놓을 방법이 원리적으로 사라진다(닭-달걀).
                // 그래서 유령 첫 칸(BuildSiteMarker) 자리에만 한 칸짜리 상자를 켠다 - 눈에 보이는
                // 표시가 있는 자리라 "보이지 않는 벽"이 아니고, 높이도 갑판 골조 높이(0.64m)뿐이다.
                // 상자 밑면은 유령 판(0.36m)보다 아래인 해수면까지 내린다: 파도에 살짝 잠겨도 조준이
                // 끊기지 않게 하기 위해서다.
                //
                // **자리를 확정하기 전에는 켜지 않는다**(anchored). 정박 전 뗏목 루트는 아직 월드
                // 원점 근처에 있을 수 있어, 그 자리에 상자를 켜면 시작 섬 한복판에 상자가 선다.
                Vector3 ghostCenter = GetCellCenterLocal(LegacyFirstCellX, LegacyFirstCellZ);

                hullCollider.enabled = anchored;
                hullCollider.center = new Vector3(ghostCenter.x, FrameTopY * 0.5f, ghostCenter.z);
                hullCollider.size = new Vector3(BaseTilePitch, FrameTopY, BaseTilePitch);

                ApplyDeckSurfaceCollider();
                return;
            }

            GetHullExtent(out float minZ, out float length, out float minX, out float width);

            hullCollider.enabled = true;
            hullCollider.center = new Vector3(minX + width * 0.5f, DeckSurfaceY * 0.5f, minZ + length * 0.5f);
            hullCollider.size = new Vector3(width, DeckSurfaceY, length);

            ApplyDeckSurfaceCollider();
        }

        /// <summary>
        /// 갑판 윗면 콜라이더를 현재 바닥판 범위에 맞춘다.
        ///
        /// **왜 이게 따로 필요한가:** 건축 시스템은 레이가 맞은 콜라이더의 부모를 거슬러 올라가
        /// DeckRoot에 닿을 때만 BuildSpace.Deck으로 전환한다(BuildingSystem.IsDeckCollider). 그런데
        /// 뗏목의 콜라이더는 (1) 뗏목 **본체**에 붙은 선체 BoxCollider와 (2) RaftVisual 밑의 승선
        /// 발판뿐이고, DeckRoot는 이 둘의 **형제/부모**라 부모 사슬로 절대 닿지 않는다.
        /// DeckRoot 밑에 실제 콜라이더를 하나 두는 것으로 조건을 충족시킨다.
        ///
        /// 이 판은 바닥판과 정확히 같은 자리(중심 y = DeckPlankY, 두께 = 바닥판 두께)에 있고, 선체
        /// 콜라이더의 윗면이 이미 DeckSurfaceY이므로 **선체 상자 안에 완전히 들어간다** - 새로 막히는
        /// 면이 생기지 않아 이동/충돌은 종전과 1mm도 다르지 않다.
        /// </summary>
        private void ApplyDeckSurfaceCollider()
        {
            if (deckSurfaceCollider == null)
                return;

            // HasDeck 게이트가 있어야 "갑판이라고 부르기 전"(바닥판 5칸 이하)에 이 판이 켜지지 않는다.
            GetDeckedSpan(out float minX, out float maxX, out float minZ, out float maxZ);
            float span = maxZ - minZ;
            float breadth = maxX - minX;

            if (!HasDeck || span <= 0.01f || breadth <= 0.01f)
            {
                deckSurfaceCollider.enabled = false;
                return;
            }

            // DeckRoot는 뗏목 본체와 로컬 원점/회전이 같으므로(EnsureDeckRoot) 아래 값은 곧 뗏목 로컬이다.
            deckSurfaceCollider.center = new Vector3(
                (minX + maxX) * 0.5f, DeckPlankY, (minZ + maxZ) * 0.5f);
            deckSurfaceCollider.size = new Vector3(breadth, DeckPlankThickness, span);
            deckSurfaceCollider.enabled = true;
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
