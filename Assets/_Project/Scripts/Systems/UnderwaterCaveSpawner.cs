using UnityEngine;
using MakeGame.Data;

namespace MakeGame.Systems
{
    /// <summary>
    /// 대형·특대 섬 해저 스커트의 수중 동굴(cave_a 돔형 / cave_b 터널형) 배치기.
    ///
    /// SeabedGenerator.Build가 SeabedFloraSpawner.Spawn을 부른 **직후, 같은 동기 흐름에서**
    /// 호출된다(같은 인자 규약 - 스커트 레코드가 등록돼 있어야 TrySampleSeabed 접지가 유효하고,
    /// 조개 메시 캐시(SeabedFloraSpawner.EnsureModelsLoaded)도 그 호출이 이미 채워 둔 상태다).
    ///
    /// ── 배치 규칙 ────────────────────────────────────────────────────────────────
    /// 대형 섬(반지름 115~170m 경계 - SeabedFloraSpawner.SizeScale와 같은 중간값 경계) 1개,
    /// 특대 섬 2개(cave_a 1 + cave_b 1). 대형 섬의 모델 택1은 rng가 아니라 **섬 중심 위치 해시**다
    /// (draw 소비 0 - 채집 해초 당첨 판정과 같은 원칙). 후보점은 섬 중심에서 방사 방향으로
    /// 수심 8~14m(그리고 모델 높이+1m 이상 수면 아래) 지점을 결정적으로 스캔하고, 입구(+Z)가
    /// 섬 쪽(얕은 쪽)을 향하게 yaw를 준다. 수심 조건을 못 채우면 조용히 생략한다.
    ///
    /// ── [결정성] draw는 배치 시도 전에 전부 소비 ─────────────────────────────────
    /// 동굴 하나의 모든 rng draw(내부 산호/조개/보물 계획 포함)는 배치 스캔·메시 로드 확인
    /// **전에** 끝낸다(SeabedFloraSpawner.PlaceCargoPile의 "draw 전부 → 메시 확인 → 생성" 규칙).
    /// 배치 스캔 자체는 rng를 쓰지 않는 결정적 지형 샘플링이라, 임포트가 한 프레임 늦거나 배치가
    /// 실패해도 같은 시드의 다음 월드에서는 같은 자리·같은 내부 구성으로 나온다.
    ///
    /// ── rng 격리 (최중요) ─────────────────────────────────────────────────────────
    /// 섬마다 `new System.Random(unchecked(worldSeed * 397 ^ islandId ^ 0xCA7E))` 전용 독립
    /// 스트림만 소비한다. 기존 스트림(0x5EABED 해저 생태 / 0xC1A0 진주조개 / 섬 레이아웃·자원·
    /// 초목 salt 대역)은 만들지도 이어 뽑지도 않는다 - 검증 완료된 기존 월드 재현성은 1비트 불변이다.
    /// UnityEngine.Random도 일절 쓰지 않는다(SeededRandomExtensions 상단 주석).
    ///
    /// ── TerrainSampler 오염 없음 ─────────────────────────────────────────────────
    /// 동굴 루트/셸/보물 이름은 전부 "UnderwaterCave_"/"CaveShell_"/"CaveTreasure" - "Island_"로
    /// 시작하지 않으므로 SnapToGround류 지형 판정(TerrainSampler.IsTerrainHit의 이름 접두 필터)이
    /// 셸 MeshCollider를 **뚫고 지나간다**. 셸 콜라이더는 비볼록 정적 MeshCollider(Rigidbody 없음,
    /// convex 기본 false, sharedMesh = 렌더 메시)라 플레이어가 잠수해 안팎을 실제로 더듬을 수 있다.
    ///
    /// ── 세이브 불변 ─────────────────────────────────────────────────────────────
    /// 세이브 대상 컴포넌트를 하나도 붙이지 않는다. 진주조개·보물의 AirlinerSalvagePoint는 세이브
    /// 비대상(수거 여부 미저장 - AirlinerSalvagePoint [한계] 주석과 동일 규약)이라 저장 파일에는
    /// 한 바이트도 들어가지 않고, 로드마다 같은 시드로 그대로 재생성될 뿐이다. 배치물 루트는 섬
    /// 루트("Island_{id}_{size}")의 자식이라 RegenerateWorld의 섬 파괴에 함께 편승한다.
    ///
    /// ── 에어포켓 로컬 좌표 (모델 실측 - OBJ 정점/BVH 레이 실측값) ─────────────────
    /// cave_a: 공동 천장(중앙 ~4.5m) 위로 파인 에어포켓 돔(cave.py pocket: 중심 (0, 3.78, -0.60),
    ///   반경 (2.30, 1.50, 2.10)). 내보낸 OBJ 실측으로 (0, ·, -0.6) 기둥의 공동 천장 y≈5.28,
    ///   바닥 개방 - 존 중심 (0, 4.7, -0.6)은 공동 안 허공이다. radius 1.3.
    /// cave_b: enforce_contract(align="bbox")가 로컬 z를 +1.5m 재중심했다(설계 중앙 방 z=-2.5 →
    ///   실측 천장 최고점 z≈-1.0, 천장 y≈3.35). 존 중심 (0, 2.6, -1.0)은 방 안 허공이다. radius 1.0.
    /// 내부 배치 앵커(산호 링/터널 경로/보물 자리)도 전부 같은 실측 좌표계 기준이다.
    ///
    /// ── 성능 ────────────────────────────────────────────────────────────────────
    /// 월드 생성 1회. 동굴당 렌더러: 셸 1 + 발광 산호 4~7(병합 임포트 시 1개씩) + 진주조개 2~3 +
    /// 화물 더미 2 = 통상 10~13개(≤15 유지). 포인트 라이트는 동굴당 1개, 그림자 끔.
    /// </summary>
    public static class UnderwaterCaveSpawner
    {
        /// <summary>동굴 루트 이름 접두사. "Island_"로 시작하지 않는 것이 지형 판정 안전의 전제다.</summary>
        private const string CaveRootPrefix = "UnderwaterCave_";

        /// <summary>rng 격리용 시드 소금. 기존 salt(0x5EABED/0xC1A0/3000000+ 대역 등)와 겹치지 않는다.</summary>
        private const int SeedSalt = 0xCA7E;

        /// <summary>대형 섬 모델 택1용 위치 해시 salt. 기존 0x51A7B0xx/0x6B3E1Fxx 대역과 겹치지 않는다.</summary>
        private const uint ModelPickSalt = 0xCA7E0001u;

        /// <summary>배치 수심 대역(m). 하한은 모델별로 max(8, 모델 높이+1)로 올린다.</summary>
        private const float DepthMin = 8f;
        private const float DepthMax = 14f;

        /// <summary>셸 밑단(y=0, 바닥 개방)을 모래 기복(±0.6m)/경사에 파묻는 침하량(m).</summary>
        private const float RootSink = 0.30f;

        /// <summary>에미시브 HDR 강도(은은한 발광 - 요구 ~1.5).</summary>
        private const float GlowIntensity = 1.5f;

        // ── 모델 카탈로그 (Resources/Models, 확장자 없음) ──────────────────────────

        /// <summary>동굴 셸 2종. `o` 1개(shell) 솔리드 두께 셸 - 비볼록 MeshCollider로 그대로 쓴다.</summary>
        private static readonly string[] CaveModelNames = { "cave_a", "cave_b" };

        /// <summary>모델 실측 전체 높이(m, OBJ 정점 maxY - 밑면 y=0). "높이+1m 수면 아래" 판정 소스다.</summary>
        private static readonly float[] CaveModelHeights = { 6.25f, 4.72f };

        /// <summary>동굴 내부 발광 산호 4종(기존 coral 메시 재사용 - 계열별 1종씩 실루엣이 다르게).</summary>
        private static readonly string[] GlowCoralModelNames =
        {
            "coral_branch_b", "coral_fan_a", "coral_tube_b", "coral_brain_a",
        };

        /// <summary>보물 더미 화물 2종(crate_a/barrel_a). 실측 크기는 SeabedFloraSpawner.CargoModelSizes와
        /// 같은 값의 사본이다(그쪽은 private - RockModelSizes와 같은 사본 정책).</summary>
        private static readonly string[] CargoModelNames = { "crate_a", "barrel_a" };
        private static readonly Vector3[] CargoModelSizes =
        {
            new Vector3(0.82f, 0.66f, 0.74f), // crate_a
            new Vector3(0.60f, 0.86f, 0.60f), // barrel_a
        };

        // ── 발광 팔레트 (CoralPalette의 청록[4]/보라[5]/호박[11] 값 사본 - 순수 Color 상수) ──
        private static readonly Color[] GlowPalette =
        {
            new Color(0.30f, 0.75f, 0.70f), // 청록
            new Color(0.62f, 0.42f, 0.78f), // 보라
            new Color(0.85f, 0.62f, 0.25f), // 호박
        };

        // ── 공유 메시/머티리얼 캐시 (프레임 프로브 + R1 리셋 - SeabedFloraSpawner와 같은 규칙) ──
        private static readonly Mesh[] caveMeshes = new Mesh[2];
        // 산호는 body/tip 두 장(병합 임포트면 primary 한 장에 서브메시 2, secondary는 null).
        private static readonly Mesh[] glowCoralPrimary = new Mesh[4];
        private static readonly Mesh[] glowCoralSecondary = new Mesh[4];
        private static readonly Mesh[] cargoMeshes = new Mesh[2];

        /// <summary>프레임당 1회 프로브 가드(SeabedFloraSpawner.probeFrame과 같은 규칙 - 실패를
        /// 영구 래치하지 않으므로 임포트가 한 프레임 늦어도 다음 월드에서 자연 복구된다).</summary>
        private static int probeFrame = -1;

        /// <summary>발광 산호 에미시브 머티리얼(GlowPalette와 일대일 - 월드 전체 최대 3장).
        /// ResourceVisualLibrary.GetMaterial 공유 캐시는 **절대 변조하지 않는다** - 그 캐시의
        /// 머티리얼에 에미션을 켜면 같은 (색+텍스처)를 쓰는 다른 배치물까지 전부 발광해 버린다.
        /// 그래서 CreateColorMaterial로 전용 인스턴스를 만들어 여기서만 캐시한다.</summary>
        private static readonly Material[] glowMaterials = new Material[3];

        /// <summary>
        /// 도메인 리로드를 끈 플레이 모드에서 static 캐시가 이전 실행의 파괴된 자원을 들고
        /// 시작하지 않게 초기 상태로 되돌린다(R1 규약 - SeabedFloraSpawner.ResetStaticCache와 동일).
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStaticCache()
        {
            System.Array.Clear(caveMeshes, 0, caveMeshes.Length);
            System.Array.Clear(glowCoralPrimary, 0, glowCoralPrimary.Length);
            System.Array.Clear(glowCoralSecondary, 0, glowCoralSecondary.Length);
            System.Array.Clear(cargoMeshes, 0, cargoMeshes.Length);
            System.Array.Clear(glowMaterials, 0, glowMaterials.Length);
            probeFrame = -1;
        }

        /// <summary>
        /// 섬 하나의 수중 동굴을 배치한다. SeabedGenerator.Build가 SeabedFloraSpawner.Spawn 직후
        /// 같은 동기 흐름에서 호출한다(같은 인자 규약).
        /// </summary>
        /// <param name="islandObject">섬 지형 루트("Island_{id}_{size}"). 배치물은 전부 이 자식이다.</param>
        /// <param name="radius">섬 지형 반지름 R(m). 스커트 안쪽 경계와 같다.</param>
        public static void Spawn(GameObject islandObject, float radius)
        {
            if (islandObject == null || radius <= 0f)
                return;

            // 대형(115~170)/특대(170~)만. 경계는 SeabedFloraSpawner.SizeScale/SpawnSunkenCargo와
            // 같은 반지름 중간값(115/170)이다.
            if (radius < 115f)
                return;
            int caveCount = radius < 170f ? 1 : 2;

            // 같은 섬에 두 번 불려도(방어) 동굴이 겹으로 생기지 않게 한다(SeabedGenerator.Build와 동일).
            if (islandObject.transform.Find(CaveRootPrefix + "0") != null)
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
            for (int n = 0; n < caveCount; n++)
            {
                // 특대 = cave_a(0번) + cave_b(1번) 각 1개. 대형 = 섬 중심 위치 해시로 택1
                // (draw 소비 0 - 순수 해시라 이 선택이 rng 스트림을 한 칸도 밀지 않는다).
                int model = caveCount == 2 ? n
                    : (PositionHash01(center, ModelPickSalt) < 0.5f ? 0 : 1);
                BuildCave(rng, islandObject, center, radius, seaLevel, model, n);
            }
        }

        // ── 동굴 하나 ────────────────────────────────────────────────────────────────

        private static void BuildCave(System.Random rng, GameObject islandObject, Vector3 center,
            float radius, float seaLevel, int model, int caveIndex)
        {
            bool tunnel = model == 1; // cave_b = 터널형(내부 앵커가 경로 좌표계)

            // ── 1) draw 전부 (배치 스캔/메시 로드 여부와 무관하게 항상 같은 횟수·순서로 소비) ──
            float baseAngle = rng.NextFloat(0f, Mathf.PI * 2f);

            // 발광 산호 4~7: (a,b)는 모델별 앵커 매개변수(돔형 = 링 각도/반경, 터널형 = 경로 t/횡편차).
            int coralCount = rng.NextInt(4, 8);
            var coralA = new float[coralCount];
            var coralB = new float[coralCount];
            var coralVariants = new int[coralCount];
            var coralColors = new int[coralCount];
            var coralYaws = new float[coralCount];
            var coralScales = new float[coralCount];
            for (int i = 0; i < coralCount; i++)
            {
                coralA[i] = rng.NextFloat(0f, 1f);
                coralB[i] = rng.NextFloat(0f, 1f);
                coralVariants[i] = rng.NextInt(0, GlowCoralModelNames.Length);
                coralColors[i] = rng.NextInt(0, GlowPalette.Length);
                coralYaws[i] = rng.NextFloat(0f, 360f);
                coralScales[i] = rng.NextFloat(0.7f, 1.1f);
            }

            // 진주조개 2~3 (0.2.37 clam 규약 재사용 - draw 범위도 SpawnPearlClams와 동일).
            int clamCount = rng.NextInt(2, 4);
            var clamA = new float[clamCount];
            var clamB = new float[clamCount];
            var clamVariants = new int[clamCount];
            var clamYaws = new float[clamCount];
            var clamScales = new float[clamCount];
            var clamPearls = new int[clamCount];
            for (int i = 0; i < clamCount; i++)
            {
                clamA[i] = rng.NextFloat(0f, 1f);
                clamB[i] = rng.NextFloat(0f, 1f);
                clamVariants[i] = rng.NextInt(0, SeabedFloraSpawner.ClamVariantCount);
                clamYaws[i] = rng.NextFloat(0f, 360f);
                clamScales[i] = rng.NextFloat(0.85f, 1.25f);
                clamPearls[i] = rng.NextInt(1, 3); // 진주 1~2 - SpawnPearlClams와 동일
            }

            // 보물 더미: 자리 지터 + 화물 더미(작게 2개) 자세 + 지급 진주 수.
            float treasureJx = rng.NextFloat(-0.4f, 0.4f);
            float treasureJz = rng.NextFloat(-0.4f, 0.4f);
            var cargoKinds = new int[2];
            var cargoAngles = new float[2];
            var cargoDists = new float[2];
            var cargoYaws = new float[2];
            var cargoLeans = new float[2];
            var cargoScales = new float[2];
            for (int i = 0; i < 2; i++)
            {
                cargoKinds[i] = rng.NextValue01() < 0.55f ? 0 : 1;
                cargoAngles[i] = rng.NextFloat(0f, Mathf.PI * 2f);
                cargoDists[i] = rng.NextFloat(0.15f, 0.5f);
                cargoYaws[i] = rng.NextFloat(0f, 360f);
                // 궤짝은 모서리 기울기, 통은 굴러 누운 자세(SeabedFloraSpawner.PlaceCargoPile 문법).
                cargoLeans[i] = cargoKinds[i] == 0 ? rng.NextFloat(8f, 30f) : rng.NextFloat(68f, 98f);
                cargoScales[i] = rng.NextFloat(0.8f, 1.0f);
            }
            int treasurePearls = rng.NextInt(2, 4); // 진주 2~3
            int lightColor = rng.NextInt(0, GlowPalette.Length);

            // ── 2) 결정적 배치 스캔 (rng 소비 0 - 지형 샘플만) ──────────────────────────
            // 섬 중심 기준 방사 방향을 baseAngle에서 좌우 교대로 12방위 돌며, 각 방위에서 스커트
            // 환형을 3m 간격으로 훑어 수심 [max(8, 모델높이+1), 14]m 지점을 찾는다. 후보 반경은
            // 동굴 footprint 반쪽(≤7.2m)이 환형 안에 온전히 들어오게 안쪽/바깥쪽 8m를 물린다.
            float depthMin = Mathf.Max(DepthMin, CaveModelHeights[model] + 1f);
            float skirtWidth = Mathf.Clamp(radius * 0.6f, 30f, 90f); // SkirtWidth 사본(그쪽은 private)
            float distMin = radius + 8f;
            float distMax = radius + skirtWidth - 8f;

            bool placed = false;
            Vector3 cavePos = Vector3.zero;
            for (int step = 0; step < 12 && !placed; step++)
            {
                // 0, +30°, -30°, +60°, -60°, ... 순서의 교대 스캔(특대 섬의 두 동굴은 baseAngle
                // draw가 달라 서로 다른 방위에서 시작한다 - 겹침을 완화하는 정도로 충분하다).
                int k = (step + 1) / 2;
                float sign = (step % 2 == 1) ? 1f : -1f;
                float angle = baseAngle + sign * k * (Mathf.PI * 2f / 12f);
                for (float dist = distMin; dist <= distMax && !placed; dist += 3f)
                {
                    var candidate = new Vector3(
                        center.x + Mathf.Cos(angle) * dist, 0f, center.z + Mathf.Sin(angle) * dist);
                    if (!SeabedGenerator.TrySampleSeabed(candidate, out float seabedY))
                        continue;
                    float depth = seaLevel - seabedY;
                    if (depth < depthMin || depth > DepthMax)
                        continue;
                    cavePos = new Vector3(candidate.x, seabedY, candidate.z);
                    placed = true;
                }
            }

            // ── 3) 배치 실패/셸 미로드면 조용히 생략 (draw는 이미 전부 소비됨 - 결정성 불변) ──
            if (!placed || caveMeshes[model] == null)
                return;

            // ── 4) 조립 ────────────────────────────────────────────────────────────────
            // 루트: 섬 루트의 자식(생명주기 편승). 입구(+Z)가 섬 중심(얕은 쪽)을 향하게 yaw.
            var root = new GameObject(CaveRootPrefix + caveIndex);
            root.transform.SetParent(islandObject.transform, false);
            root.transform.localPosition = cavePos - center + new Vector3(0f, -RootSink, 0f);
            Vector3 toIsland = center - cavePos;
            toIsland.y = 0f;
            root.transform.localRotation = Quaternion.LookRotation(toIsland.normalized, Vector3.up);

            // 셸: 어두운 현무암(기존 "rock" 텍스처, GetMaterial 공유 캐시 - 월드 전체 1장) +
            // 비볼록 정적 MeshCollider(sharedMesh = 렌더 메시). 이름이 "CaveShell_"이라
            // TerrainSampler 지형 판정("Island_" 필터)에는 구조적으로 안 잡힌다(클래스 상단 근거).
            Material shellMaterial = ResourceVisualLibrary.GetMaterial(
                new Color(0.20f, 0.21f, 0.22f), "rock");
            var shell = StructureVisualBuilder.CreateMeshPart(root.transform,
                "CaveShell_" + CaveModelNames[model], caveMeshes[model],
                Vector3.zero, Vector3.one, Quaternion.identity, shellMaterial);
            var shellRenderer = shell.GetComponent<MeshRenderer>();
            if (shellRenderer != null)
            {
                // 수중이라 그림자 캐스팅/수신 모두 끈다(해저 스커트/생태와 같은 이유).
                shellRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                shellRenderer.receiveShadows = false;
            }
            shell.AddComponent<MeshCollider>().sharedMesh = caveMeshes[model]; // convex 기본 false

            // 내부 연출: 발광 산호 → 진주조개 → 보물 → 에어포켓 → 라이트.
            for (int i = 0; i < coralCount; i++)
            {
                Vector2 anchor = CoralAnchor(tunnel, coralA[i], coralB[i]);
                PlaceGlowCoral(root.transform, anchor, coralVariants[i], coralColors[i],
                    coralYaws[i], coralScales[i]);
            }

            // 진주조개는 SeabedFloraSpawner.PlaceClam(0.2.37 규약)을 그대로 재사용한다. PlaceClam은
            // localPosition = worldPos - islandCenter (무회전 부모 전제)라, 회전한 동굴 루트 아래에
            // "월드 무회전 + 섬 중심 위치"의 홀더를 하나 끼워 그 전제를 만족시킨다(렌더러 없는
            // 트랜스폼 1개 - 동굴 파괴에 함께 편승한다).
            var lootHolder = new GameObject("CaveClams");
            lootHolder.transform.SetParent(root.transform, false);
            lootHolder.transform.position = center;
            lootHolder.transform.rotation = Quaternion.identity;
            for (int i = 0; i < clamCount; i++)
            {
                Vector2 anchor = ClamAnchor(tunnel, clamA[i], clamB[i]);
                if (!TryGroundWorld(root.transform, anchor.x, anchor.y, out Vector3 world))
                    continue;
                SeabedFloraSpawner.PlaceClam(lootHolder.transform, center, world,
                    clamVariants[i], clamYaws[i], clamScales[i], clamPearls[i], i);
            }

            PlaceTreasure(root.transform, tunnel, treasureJx, treasureJz, cargoKinds, cargoAngles,
                cargoDists, cargoYaws, cargoLeans, cargoScales, treasurePearls);

            // 에어포켓: 모델 실측 공동 좌표(클래스 상단 산출 근거). 존/기포 쉼머는 AirPocketZone이
            // 스스로 책임진다(산소 회복 연동은 PlayerController → AirPocketZone.IsInsideAny).
            var pocket = new GameObject("CaveAirPocket");
            pocket.transform.SetParent(root.transform, false);
            pocket.transform.localPosition = tunnel
                ? new Vector3(0f, 2.6f, -1.0f)   // cave_b 중앙 방 천장(실측 y≈3.35) 아래 허공
                : new Vector3(0f, 4.7f, -0.6f);  // cave_a 에어포켓 돔(실측 천장 y≈5.28) 안 허공
            pocket.AddComponent<AirPocketZone>().radius = tunnel ? 1.0f : 1.3f;
            // spawnBubbleShimmer 기본값 true = 기포 쉼머 on(요구사항).

            // 은은한 포인트 라이트 1개(산호 톤). 동굴당 1개뿐이라 성능 무시 가능. 그림자 끔.
            var lightGo = new GameObject("CaveGlowLight");
            lightGo.transform.SetParent(root.transform, false);
            lightGo.transform.localPosition = tunnel
                ? new Vector3(0f, 1.6f, -1.0f)
                : new Vector3(0f, 2.2f, -0.1f);
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Point;
            light.range = 7f;
            light.intensity = 1.1f;
            light.color = GlowPalette[lightColor];
            light.shadows = LightShadows.None;
        }

        // ── 내부 앵커 (모델 실측 좌표계 - 클래스 상단 산출 근거) ──────────────────────

        /// <summary>
        /// 발광 산호 앵커(동굴 로컬 XZ). 돔형은 공동 중심 (0, -0.1) 둘레 링 r 1.9~2.9(벽·바닥
        /// 가장자리 - 실측으로 전 구간 공동 안·바닥 개방 확인), 터널형은 경로 t 0.25~0.75 ×
        /// 횡편차 ±0.9m(입구 나팔 구간 제외).
        /// </summary>
        private static Vector2 CoralAnchor(bool tunnel, float a, float b)
        {
            if (!tunnel)
            {
                float angle = a * Mathf.PI * 2f;
                float r = 1.9f + b * 1.0f;
                return new Vector2(Mathf.Cos(angle) * r, -0.1f + Mathf.Sin(angle) * r);
            }
            return TunnelPoint(0.25f + a * 0.5f, (b - 0.5f) * 1.8f);
        }

        /// <summary>진주조개 앵커. 돔형은 링 r 1.4~2.6, 터널형은 t 0.3~0.7 × 횡편차 ±0.8m.</summary>
        private static Vector2 ClamAnchor(bool tunnel, float a, float b)
        {
            if (!tunnel)
            {
                float angle = a * Mathf.PI * 2f;
                float r = 1.4f + b * 1.2f;
                return new Vector2(Mathf.Cos(angle) * r, -0.1f + Mathf.Sin(angle) * r);
            }
            return TunnelPoint(0.3f + a * 0.4f, (b - 0.5f) * 1.6f);
        }

        /// <summary>
        /// cave_b 터널 경로의 로컬 XZ. cave.py path_b의 반타원 아크에 bbox 재중심 오프셋(z +1.5m -
        /// 실측 검증: t=0.5 → (0, -1.0) = 중앙 방 천장 최고점 실측 위치)을 반영한 식이고,
        /// lateral은 경로 접선의 수평 수직 방향 편차다.
        /// </summary>
        private static Vector2 TunnelPoint(float t, float lateral)
        {
            const float rx = 4.5f, rz = 5.0f, zc = 4.0f; // zc = 설계 2.5 + bbox 재중심 1.5
            float a = Mathf.PI * t;
            float x = -rx * Mathf.Cos(a);
            float z = zc - rz * Mathf.Sin(a);
            // 접선 (rx·π·sin, -rz·π·cos) → 수평 수직 = (tan.z, -tan.x) 정규화 (path_b와 같은 규약).
            float tx = rx * Mathf.Sin(a);
            float tz = -rz * Mathf.Cos(a);
            float mag = Mathf.Sqrt(tx * tx + tz * tz);
            if (mag > 1e-4f)
            {
                x += tz / mag * lateral;
                z += -tx / mag * lateral;
            }
            return new Vector2(x, z);
        }

        // ── 개별 배치물 ─────────────────────────────────────────────────────────────

        /// <summary>
        /// 발광 산호 하나. 접지는 실제 해저 높이(TrySampleSeabed - 동굴 바닥이 곧 해저 모래)이고,
        /// 병합 임포트(서브메시 2)면 렌더러 하나에 같은 발광 머티리얼 2장을 준다(PlaceCoral 분기와
        /// 같은 규칙 - 발광이라 body/tip 명도 차이는 에미션에 묻히므로 한 장으로 통일).
        /// </summary>
        private static void PlaceGlowCoral(Transform caveRoot, Vector2 anchor, int variant,
            int colorIndex, float yaw, float scale)
        {
            Mesh body = glowCoralPrimary[variant];
            if (body == null)
                return; // 이 변종만 아직 안 로드됨 - 조용히 건너뛴다(래치 없음, 다음 월드에서 복구)
            if (!TryGroundLocal(caveRoot, anchor.x, anchor.y, 0.08f, out Vector3 localPos))
                return;

            Material glow = GetGlowMaterial(colorIndex);
            var part = StructureVisualBuilder.CreateMeshPart(caveRoot,
                "CaveCoral_" + GlowCoralModelNames[variant], body,
                localPos, Vector3.one * scale, Quaternion.Euler(0f, yaw, 0f), glow);
            var renderer = part.GetComponent<MeshRenderer>();
            if (renderer != null)
            {
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                if (glowCoralSecondary[variant] == null && body.subMeshCount >= 2)
                    renderer.sharedMaterials = new[] { glow, glow };
            }
            if (glowCoralSecondary[variant] != null)
            {
                // 개별 메시 임포트 방어: tip을 같은 위치/회전/스케일 파츠로 하나 더.
                var tip = StructureVisualBuilder.CreateMeshPart(caveRoot,
                    "CaveCoral_" + GlowCoralModelNames[variant] + "_tip", glowCoralSecondary[variant],
                    localPos, Vector3.one * scale, Quaternion.Euler(0f, yaw, 0f), glow);
                var tipRenderer = tip.GetComponent<MeshRenderer>();
                if (tipRenderer != null)
                {
                    tipRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    tipRenderer.receiveShadows = false;
                }
            }
        }

        /// <summary>
        /// 보물 수거 지점: 작은 화물 더미(crate/barrel 2개) + AirlinerSalvagePoint "동굴 보물"
        /// (진주 2~3 + 부싯돌 2 + 금속조각 2 - 전부 ItemDataRegistry 실재 itemName).
        /// 두 화물 메시가 모두 미로드면 만들지 않는다(보이지 않는 수거 지점 금지 -
        /// SeabedFloraSpawner.PlaceCargoPile과 같은 폴백. draw는 호출 전에 이미 소비됨).
        /// </summary>
        private static void PlaceTreasure(Transform caveRoot, bool tunnel, float jx, float jz,
            int[] kinds, float[] angles, float[] dists, float[] yaws, float[] leans, float[] scales,
            int pearlCount)
        {
            if (cargoMeshes[0] == null && cargoMeshes[1] == null)
                return;

            // 보물 자리: 공동 안쪽 뒷벽 근처(실측 검증 앵커 ± 0.4m 지터).
            Vector2 anchor = tunnel
                ? new Vector2(0.4f + jx, -2.2f + jz)  // cave_b 중앙 방 뒤쪽
                : new Vector2(0.9f + jx, -2.3f + jz); // cave_a 공동 뒤쪽
            if (!TryGroundLocal(caveRoot, anchor.x, anchor.y, 0.05f, out Vector3 localPos))
                return;

            Material woodMaterial = ResourceVisualLibrary.GetMaterial(new Color(0.24f, 0.19f, 0.14f), "wood");
            Material metalMaterial = ResourceVisualLibrary.GetMaterial(new Color(0.22f, 0.24f, 0.26f), "metal");

            var pile = new GameObject("CaveTreasure");
            pile.transform.SetParent(caveRoot, false);
            pile.transform.localPosition = localPos;

            for (int i = 0; i < kinds.Length; i++)
            {
                // 한쪽 메시만 로드됐으면 그쪽으로 대체(추가 draw 없음 - PlaceCargoPile과 같은 규칙).
                int kind = cargoMeshes[kinds[i]] != null ? kinds[i] : 1 - kinds[i];
                Vector3 worldSize = CargoModelSizes[kind] * scales[i];
                // 접지 원점 모델을 기울이면 밑면 가장자리가 내려간다 - 들어 올림(PlaceCargoPile 식).
                float radians = leans[i] * Mathf.Deg2Rad;
                float lift = 0.5f * Mathf.Max(worldSize.x, worldSize.z) * Mathf.Abs(Mathf.Sin(radians));
                var offset = new Vector3(
                    Mathf.Cos(angles[i]) * dists[i], lift - 0.06f, Mathf.Sin(angles[i]) * dists[i]);
                var part = StructureVisualBuilder.CreateMeshPart(pile.transform,
                    "TreasureCargo_" + CargoModelNames[kind], cargoMeshes[kind], offset,
                    Vector3.one * scales[i],
                    Quaternion.Euler(0f, yaws[i], 0f) * Quaternion.AngleAxis(leans[i], Vector3.forward),
                    kind == 0 ? woodMaterial : metalMaterial);
                var renderer = part.GetComponent<MeshRenderer>();
                if (renderer != null)
                {
                    renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    renderer.receiveShadows = false;
                }
            }

            // 수거 지점: 더미 루트의 BoxCollider를 InteractionController 레이가 맞으면
            // GetComponentInParent로 같은 오브젝트의 AirlinerSalvagePoint가 잡힌다(0.2.37 경로).
            var box = pile.AddComponent<BoxCollider>();
            box.center = new Vector3(0f, 0.5f, 0f);
            box.size = new Vector3(1.2f, 1.0f, 1.2f);

            // [한계] 수거 여부는 세이브 미저장 - AirlinerSalvagePoint [한계] 주석과 동일 규약
            // (월드 재생성 배경 오브젝트라 로드마다 리셋. 세이브 파일 형식은 1바이트도 안 바뀐다).
            var salvage = pile.AddComponent<AirlinerSalvagePoint>();
            salvage.displayName = "동굴 보물";
            salvage.loot = new[]
            {
                new AirlinerSalvagePoint.LootEntry("진주", pearlCount),
                new AirlinerSalvagePoint.LootEntry("부싯돌", 2),
                new AirlinerSalvagePoint.LootEntry("금속조각", 2),
            };
        }

        // ── 접지 ────────────────────────────────────────────────────────────────────

        /// <summary>
        /// 동굴 로컬 XZ를 월드로 변환해 해저(스커트) 높이에 접지한 **로컬** 좌표를 준다.
        /// 동굴 바닥은 개방(해저 모래가 바닥)이라 내부 배치물의 정답 높이는 항상 TrySampleSeabed다.
        /// 스커트 범위 밖(있을 수 없지만 방어)이면 false - 그 배치물만 버린다.
        /// </summary>
        private static bool TryGroundLocal(Transform caveRoot, float localX, float localZ,
            float sink, out Vector3 localPos)
        {
            if (!TryGroundWorld(caveRoot, localX, localZ, out Vector3 world))
            {
                localPos = Vector3.zero;
                return false;
            }
            localPos = caveRoot.InverseTransformPoint(world + new Vector3(0f, -sink, 0f));
            return true;
        }

        /// <summary>위와 같되 접지된 **월드** 좌표를 준다(PlaceClam 재사용 경로용).</summary>
        private static bool TryGroundWorld(Transform caveRoot, float localX, float localZ,
            out Vector3 world)
        {
            Vector3 probe = caveRoot.TransformPoint(new Vector3(localX, 0f, localZ));
            if (!SeabedGenerator.TrySampleSeabed(probe, out float seabedY))
            {
                world = Vector3.zero;
                return false;
            }
            world = new Vector3(probe.x, seabedY, probe.z);
            return true;
        }

        // ── 재질/로더 ───────────────────────────────────────────────────────────────

        /// <summary>
        /// 발광 산호 머티리얼(색당 1장, 월드 전체 최대 3장). URP Lit의 에미션은 _EMISSION 키워드 +
        /// _EmissionColor(HDR = 색 × 1.5)로 켠다. 전부 런타임 생성물이라 GI 베이크와 무관하다.
        /// 파괴된 머티리얼은 Unity == 오버로드가 null로 알려주므로 다시 만든다(GetMaterial과 같은 검사).
        /// </summary>
        private static Material GetGlowMaterial(int colorIndex)
        {
            Material cached = glowMaterials[colorIndex];
            if (cached != null)
                return cached;

            Color color = GlowPalette[colorIndex];
            var material = StructureVisualBuilder.CreateColorMaterial(color, "noise");
            material.name = StructureVisualBuilder.RuntimeMaterialPrefix + "CaveGlow_" + colorIndex;
            material.EnableKeyword("_EMISSION");
            if (material.HasProperty("_EmissionColor"))
                material.SetColor("_EmissionColor", color * GlowIntensity);
            material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            material.enableInstancing = true;
            glowMaterials[colorIndex] = material;
            return material;
        }

        /// <summary>
        /// 셸/산호/화물 메시를 채운다. ResourceVisualLibrary.TryLoadTwoPartModel(검증된
        /// Load&lt;GameObject&gt;+MeshFilter 로더 - Instantiate 금지) + 프레임당 1회 프로브 +
        /// 실패 영구 래치 없음(SeabedFloraSpawner.EnsureModelsLoaded와 같은 규칙).
        /// 동굴 OBJ는 `o` 1개(shell)라 첫 out으로 그대로 온다.
        /// </summary>
        private static void EnsureModelsLoaded()
        {
            bool anyMissing = false;
            for (int i = 0; i < caveMeshes.Length && !anyMissing; i++)
                anyMissing = caveMeshes[i] == null;
            for (int i = 0; i < glowCoralPrimary.Length && !anyMissing; i++)
                anyMissing = glowCoralPrimary[i] == null;
            for (int i = 0; i < cargoMeshes.Length && !anyMissing; i++)
                anyMissing = cargoMeshes[i] == null;

            if (!anyMissing || probeFrame == Time.frameCount)
                return;
            probeFrame = Time.frameCount;

            for (int i = 0; i < CaveModelNames.Length; i++)
            {
                if (caveMeshes[i] != null)
                    continue;
                if (ResourceVisualLibrary.TryLoadTwoPartModel("Models/" + CaveModelNames[i],
                        out Mesh shell, out _))
                    caveMeshes[i] = shell;
            }

            for (int i = 0; i < GlowCoralModelNames.Length; i++)
            {
                if (glowCoralPrimary[i] != null)
                    continue;
                if (ResourceVisualLibrary.TryLoadTwoPartModel("Models/" + GlowCoralModelNames[i],
                        out Mesh body, out Mesh tip))
                {
                    glowCoralPrimary[i] = body;
                    glowCoralSecondary[i] = tip; // 병합 임포트면 null - PlaceGlowCoral 분기가 처리
                }
            }

            for (int i = 0; i < CargoModelNames.Length; i++)
            {
                if (cargoMeshes[i] != null)
                    continue;
                if (ResourceVisualLibrary.TryLoadTwoPartModel("Models/" + CargoModelNames[i],
                        out Mesh cargo, out _))
                    cargoMeshes[i] = cargo;
            }
        }

        // ── 유틸 (SeabedFloraSpawner의 사본 - 그쪽은 private라 참조할 수 없다) ─────────

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

        /// <summary>위치 → 결정적 해시(SeabedFloraSpawner.PositionHash 사본 - 알고리즘 근거는
        /// IslandMeshGenerator.MeshLibrary [B50] 주석). rng 소비 0 - 순수 함수.</summary>
        private static uint PositionHash(Vector3 worldPosition, uint salt)
        {
            unchecked
            {
                int qx = Mathf.RoundToInt(worldPosition.x * 10f);
                int qz = Mathf.RoundToInt(worldPosition.z * 10f);
                uint h = (uint)(qx * 73856093) ^ (uint)(qz * 19349663) ^ salt;
                h ^= h >> 16;
                h *= 0x7FEB352Du;
                h ^= h >> 15;
                h *= 0x846CA68Bu;
                h ^= h >> 16;
                return h;
            }
        }

        /// <summary>위 해시를 [0,1) 실수로(모델 택1 판정용 - PositionHash01 사본).</summary>
        private static float PositionHash01(Vector3 worldPosition, uint salt)
        {
            return (PositionHash(worldPosition, salt) & 0xFFFFFFu) / (float)0x1000000;
        }
    }
}
