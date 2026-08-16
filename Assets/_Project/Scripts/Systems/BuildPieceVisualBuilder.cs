using UnityEngine;

namespace MakeGame.Systems
{
    /// <summary>
    /// 건축 부품(바닥/벽/문/창문/계단)의 실물 메시와 배치 미리보기(고스트)를 절차적으로 만든다.
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

        // ── 공유 머티리얼 ─────────────────────────────────────────────────────────
        private static Material postMaterial;    // 기둥/보 - 굵은 구조재
        private static Material plankMaterial;   // 널판 - 구조재보다 살짝 밝게 민 명도 변형(색상각은 그대로)
        private static Material lashingMaterial; // 결속(밧줄/섬유)
        private static Material ghostValidMaterial;
        private static Material ghostInvalidMaterial;

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
