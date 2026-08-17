using UnityEngine;
using MakeGame.Data;

namespace MakeGame.Systems
{
    /// <summary>
    /// 섬별 해저 생태(산호밭 / 해초 숲 / 수중 바위) 분포기.
    ///
    /// SeabedGenerator가 해저 스커트(섬 가장자리 → 수심 18m 환형 모래 바닥)를 깔고 레코드를 등록한
    /// **직후, 같은 동기 흐름에서** 호출된다(SeabedGenerator.Build 끝부분 - 스커트가 먼저 있어야
    /// TrySampleSeabed 접지 샘플이 유효하다).
    ///
    /// ── [세이브 무관 - 순수 배경] ──────────────────────────────────────────────────
    /// 여기서 만드는 것은 전부 채집 불가 장식이다. ResourceNode/HazardSource 등 세이브 대상 컴포넌트를
    /// 하나도 붙이지 않으므로 SaveLoadController의 FindObjectsByType 순회(ResourceNode/Campfire/…)에
    /// 걸리지 않고, 저장 파일에 한 바이트도 들어가지 않는다. 불러오기(RegenerateWorld) 후에는 같은
    /// worldSeed → 같은 시드 → 같은 배치로 그냥 다시 생성될 뿐이다.
    ///
    /// ── rng 격리 (최중요) ─────────────────────────────────────────────────────────
    /// 섬마다 `new System.Random(unchecked(worldSeed * 397 ^ islandId ^ 0x5EABED))` 로 만든
    /// **전용 독립 스트림**만 소비한다. 기존 스트림(섬 레이아웃 CreateForSalt, 초목
    /// VegetationSeedSalt(3000000+id), 자원 CreateForIsland, 위험요소/사냥감/도면 salt 대역)은
    /// 어느 것도 만들지도, 이어 뽑지도 않는다 - 0x5EABED xor 시드는 위 어떤 salt 조합과도 겹치지
    /// 않는 별도 시드 공간이고, 여기서 몇 번을 뽑든 다른 시스템의 추첨 순서는 한 칸도 밀리지 않는다.
    /// UnityEngine.Random은 일절 쓰지 않는다(전역 상태 오염 금지 - SeededRandomExtensions 상단 주석).
    ///
    /// ── 생명주기 편승 ────────────────────────────────────────────────────────────
    /// 배치물 루트는 반드시 **그 섬의 루트 오브젝트("Island_{id}_{size}") 자식**이다. 그래서
    /// RegenerateWorld가 섬을 SetActive(false)+Destroy 하면 함께 사라지고(스커트와 동일한 편승),
    /// 별도의 정리/추적 코드가 필요 없다. 정적 캐시는 공유 메시/머티리얼뿐이라 월드 재생성과 무관하다.
    ///
    /// ── 기존 배치가 영향받지 않는 근거 (SeabedGenerator와 동일 계열) ───────────────
    ///  (1) 모든 오브젝트 이름이 "SeabedFlora_"/"Coral_"/"Kelp_"/"SeaRock_" - "Island_"로 시작하지
    ///      않으므로 TerrainSampler.SnapToGround류 지형 판정에서 구조적으로 제외된다.
    ///  (2) 전부 r ≥ R(섬 메시 밖) 해수면 아래라, 뭍 배치(산포 반경 0.8R 이내)의 레이가 위를
    ///      지나갈 일 자체가 없다.
    ///  (3) 콜라이더는 "큰 바위"에만 BoxCollider(대략치)를 달아 잠수 시 실제로 부딪히는 랜드마크로
    ///      만들고, 산호/해초/작은 바위는 콜라이더 없음(수중 이동을 막지 않는다).
    ///
    /// ── 시각 로더 ────────────────────────────────────────────────────────────────
    /// IslandResourceSpawner.MeshLibrary(ResourceVisualLibrary.TryLoadTwoPartModel)의 검증된 패턴
    /// 그대로다: Resources.Load&lt;GameObject&gt; + GetComponentsInChildren&lt;MeshFilter&gt;로 공유 메시만
    /// 꺼내 쓰고(Instantiate 금지 - 임포터가 붙였을 수 있는 콜라이더가 씬에 못 들어온다), 프레임당
    /// 1회만 프로브하며, 실패를 영구 래치하지 않는다. 산호 OBJ는 `o` 오브젝트 2개(body/tip)라
    /// Unity 6.5 임포터가 서브메시 2짜리 한 메시로 합쳐 올 수 있는데(AirlinerWreck 실사고 0.2.13),
    /// 그때는 렌더러 하나에 sharedMaterials = [body색, tip색] 배열을 준다(AirlinerWreck/
    /// IslandResourceSpawner.Visuals의 subMeshCount>=2 분기와 같은 규칙). 머티리얼은 전부
    /// ResourceVisualLibrary.GetMaterial 공유 캐시(색+텍스처당 1장, enableInstancing)에서 받는다.
    ///
    /// ── 성능 ────────────────────────────────────────────────────────────────────
    /// 섬당 렌더러 약 20~45개(산호는 서브메시 2라 드로우콜 기준 약 30~60). 모델 50종 × 머티리얼
    /// 19장(산호 12색×2단 중 실제 사용 조합 + 해초 4 + 바위 3)은 전부 월드 공유 정적 캐시다.
    /// 수중이라 그림자는 캐스팅/수신 모두 끈다(스커트와 같은 이유 - 보이지 않는 그림자에 드로우를
    /// 쓰지 않는다).
    /// </summary>
    public static class SeabedFloraSpawner
    {
        /// <summary>섬별 배치 루트 이름 접두사. "Island_"로 시작하지 않는 것이 지형 판정 안전의 전제다.</summary>
        private const string FloraRootPrefix = "SeabedFlora_";

        /// <summary>rng 격리용 시드 소금. 기존 어떤 salt 대역(3000000+ 등)과도 겹치지 않는 xor 상수.</summary>
        private const int SeedSalt = 0x5EABED;

        // ── 모델 카탈로그 (Resources/Models, 확장자 없음) ──────────────────────────

        /// <summary>산호 20종. 계열 순서: branch 6 → table 4 → brain 3 → fan 4 → tube 3.</summary>
        private static readonly string[] CoralModelNames =
        {
            "coral_branch_a", "coral_branch_b", "coral_branch_c", "coral_branch_d", "coral_branch_e", "coral_branch_f",
            "coral_table_a", "coral_table_b", "coral_table_c", "coral_table_d",
            "coral_brain_a", "coral_brain_b", "coral_brain_c",
            "coral_fan_a", "coral_fan_b", "coral_fan_c", "coral_fan_d",
            "coral_tube_a", "coral_tube_b", "coral_tube_c",
        };

        /// <summary>산호 계열(branch/table/brain/fan/tube)의 CoralModelNames 시작 인덱스와 개수.
        /// 같은 패치엔 같은 계열 60% 편향(실제 산호초 군락감)을 주는 데 쓴다.</summary>
        private static readonly int[] CoralFamilyStart = { 0, 6, 10, 13, 17 };
        private static readonly int[] CoralFamilyCount = { 6, 4, 3, 4, 3 };

        /// <summary>해초 10종(kelp_a~j, `o` 1개 = 양면 blade 메시).</summary>
        private static readonly string[] KelpModelNames =
        {
            "kelp_a", "kelp_b", "kelp_c", "kelp_d", "kelp_e",
            "kelp_f", "kelp_g", "kelp_h", "kelp_i", "kelp_j",
        };

        /// <summary>각 해초 모델의 실측 전체 높이(m, OBJ 정점 maxY - 밑면 y=0). 리본형/방석형 판정 소스다:
        /// 높이 1.4m 이상(a~d,h~j)은 물결에 뻗는 리본형 → 깊은 쪽, 0.7m 미만(e~g)은 방석형 → 얕은 쪽.</summary>
        private static readonly float[] KelpModelHeights =
        {
            2.47f, 3.67f, 1.62f, 4.15f, 0.37f,
            0.26f, 0.64f, 2.10f, 2.66f, 1.45f,
        };

        /// <summary>수중 바위 20종(searock_a~t, `o` 1개). a~e 표석, f~j 판형, k~o 첨탑(1.2~3.1m), p~t 군집.</summary>
        private static readonly string[] RockModelNames =
        {
            "searock_a", "searock_b", "searock_c", "searock_d", "searock_e",
            "searock_f", "searock_g", "searock_h", "searock_i", "searock_j",
            "searock_k", "searock_l", "searock_m", "searock_n", "searock_o",
            "searock_p", "searock_q", "searock_r", "searock_s", "searock_t",
        };

        /// <summary>각 바위 모델의 실측 크기(m, W×H×D · 밑면 y=0 · XZ 대략 중심). OBJ 정점 실측값 -
        /// BoxCollider 대략치와 "큰 것(&gt;1.5m)" 판정에 쓴다(OreRockModelSizes와 같은 사본 정책).</summary>
        private static readonly Vector3[] RockModelSizes =
        {
            new Vector3(0.87f, 0.72f, 0.89f), new Vector3(1.47f, 1.06f, 1.28f), new Vector3(0.56f, 0.45f, 0.50f),
            new Vector3(1.79f, 1.37f, 1.93f), new Vector3(1.10f, 0.90f, 1.15f),
            new Vector3(1.21f, 0.40f, 1.34f), new Vector3(2.15f, 0.54f, 1.99f), new Vector3(0.88f, 0.33f, 0.91f),
            new Vector3(3.06f, 0.64f, 2.09f), new Vector3(1.59f, 0.46f, 1.49f),
            new Vector3(0.60f, 1.75f, 0.84f), new Vector3(1.10f, 2.47f, 0.91f), new Vector3(0.55f, 1.22f, 0.51f),
            new Vector3(1.12f, 3.07f, 1.12f), new Vector3(0.75f, 2.01f, 0.73f),
            new Vector3(0.89f, 0.41f, 0.87f), new Vector3(1.52f, 0.45f, 1.13f), new Vector3(0.56f, 0.29f, 0.65f),
            new Vector3(2.06f, 0.73f, 1.58f), new Vector3(1.16f, 0.46f, 1.23f),
        };

        /// <summary>첨탑 바위(searock_k~o)의 인덱스 구간. 잠수 랜드마크라 깊이 6m 이상에만 배치한다.</summary>
        private const int SpireStart = 10;
        private const int SpireCount = 5;

        // ── 팔레트 (순수 Color 상수라 필드 초기화식에 두어도 안전하다 - Unity API 호출 없음) ────

        /// <summary>산호 12색 팔레트(핑크/주황/자홍/노랑/청록/보라 등). 변종 인덱스 % 12로 순환하고,
        /// tip은 같은 색을 Shade 1.35로 밝혀 성장점이 살아 있는 산호로 읽히게 한다.</summary>
        private static readonly Color[] CoralPalette =
        {
            new Color(0.94f, 0.55f, 0.55f), // 산호 핑크
            new Color(0.93f, 0.53f, 0.25f), // 주황
            new Color(0.85f, 0.30f, 0.55f), // 자홍
            new Color(0.90f, 0.78f, 0.32f), // 노랑
            new Color(0.30f, 0.75f, 0.70f), // 청록
            new Color(0.62f, 0.42f, 0.78f), // 보라
            new Color(0.88f, 0.36f, 0.28f), // 주홍
            new Color(0.95f, 0.68f, 0.50f), // 살구
            new Color(0.48f, 0.55f, 0.85f), // 청보라
            new Color(0.42f, 0.78f, 0.50f), // 초록빛 청록
            new Color(0.90f, 0.45f, 0.65f), // 장미
            new Color(0.85f, 0.62f, 0.25f), // 호박
        };

        /// <summary>해초 4단 녹갈 팔레트(어두운 갈조 → 밝은 녹조). 변종 인덱스 % 4로 순환한다.</summary>
        private static readonly Color[] KelpPalette =
        {
            new Color(0.33f, 0.28f, 0.14f),
            new Color(0.38f, 0.40f, 0.16f),
            new Color(0.30f, 0.46f, 0.22f),
            new Color(0.42f, 0.58f, 0.26f),
        };

        /// <summary>수중 바위 3단(어두운 현무암 회색 → 해조 낀 회록). 변종 인덱스 % 3으로 순환한다.</summary>
        private static readonly Color[] RockPalette =
        {
            new Color(0.26f, 0.27f, 0.28f),
            new Color(0.33f, 0.35f, 0.34f),
            new Color(0.36f, 0.42f, 0.36f),
        };

        // ── 공유 메시 캐시 (모델 50종 - 월드 전체가 나눠 쓴다) ─────────────────────────
        // 산호는 body/tip 두 장(임포터가 합치면 primary 한 장에 서브메시 2, secondary는 null).
        private static readonly Mesh[] coralPrimary = new Mesh[20];
        private static readonly Mesh[] coralSecondary = new Mesh[20];
        private static readonly Mesh[] kelpMeshes = new Mesh[10];
        private static readonly Mesh[] rockMeshes = new Mesh[20];

        /// <summary>프레임당 1회 프로브 가드(TryGetBambooModel/AirlinerWreck.probeFrame과 같은 규칙).
        /// 실패를 영구 래치하지 않으므로 임포트가 한 프레임 늦어도 다음 섬/다음 월드에서 자연 복구된다.</summary>
        private static int probeFrame = -1;

        /// <summary>
        /// 섬 하나의 해저 생태를 배치한다. SeabedGenerator.Build가 스커트 레코드를 등록한 직후
        /// 같은 동기 흐름에서 호출한다(그래야 아래 TrySampleSeabed가 이 섬의 스커트를 안다).
        /// </summary>
        /// <param name="islandObject">섬 지형 루트("Island_{id}_{size}"). 배치물은 전부 이 자식이다.</param>
        /// <param name="radius">섬 지형 반지름 R(m). 스커트 안쪽 경계와 같다.</param>
        public static void Spawn(GameObject islandObject, float radius)
        {
            if (islandObject == null || radius <= 0f)
                return;

            // 같은 섬에 두 번 불려도(방어) 생태가 겹으로 깔리지 않게 한다(SeabedGenerator.Build와 동일).
            string rootName = FloraRootPrefix + islandObject.name;
            if (islandObject.transform.Find(rootName) != null)
                return;

            // worldSeed/seaLevel은 섬 루트의 부모(WorldMapManager.transform - SpawnPlaceholder가
            // 섬을 그 자식으로 만든다)에서 읽는다. rng를 얻기 위한 읽기 전용 접근이라 어떤 스트림도
            // 소비하지 않는다. islandId는 이름 "Island_{id}_{size}"에서 파싱한다(이름은
            // BuildIslandSurface 호출 전에 확정된다 - WorldMapManager.SpawnPlaceholder 순서).
            var manager = islandObject.GetComponentInParent<WorldMapManager>();
            int worldSeed = manager != null ? manager.worldSeed : 0;
            float seaLevel = manager != null ? manager.seaLevel : 0f;
            int islandId = ParseIslandId(islandObject.name);

            // [rng 격리] 이 섬 전용 독립 스트림. 여기서 몇 번을 뽑든 다른 시스템의 추첨 순서는 불변이다.
            var rng = new System.Random(unchecked(worldSeed * 397 ^ islandId ^ SeedSalt));

            EnsureModelsLoaded();

            var root = new GameObject(rootName);
            root.transform.SetParent(islandObject.transform, false);
            root.transform.localPosition = Vector3.zero;
            root.transform.localRotation = Quaternion.identity;
            root.transform.localScale = Vector3.one;

            Vector3 center = islandObject.transform.position;
            // 스커트 폭. SeabedGenerator.SkirtWidth와 같은 식의 사본이다(그쪽은 private) - 어긋나도
            // 후보 적중률만 떨어질 뿐, 접지 정답은 항상 TrySampleSeabed가 준다(범위 밖이면 false).
            float skirtWidth = Mathf.Clamp(radius * 0.6f, 30f, 90f);
            float rMin = radius + 1f;
            float rMax = radius + skirtWidth - 1f;

            // 섬 크기별 스케일: Small ×0.7 / Medium ×1.0 / Large ×1.4 / XL ×1.8.
            // 반지름 경계(50/90/140/200 - IslandSizeMetrics.GetTerrainRadius)의 중간값으로 가른다
            // (IslandMeshGenerator.Vegetation의 규모 판정과 같은 전략 - 반지름 공식이 바뀌어도 따라간다).
            float scale = SizeScale(radius);

            SpawnCoralPatches(rng, root.transform, center, rMin, rMax, seaLevel, scale);
            SpawnKelpGroves(rng, root.transform, center, rMin, rMax, seaLevel, scale);
            SpawnSeaRocks(rng, root.transform, center, rMin, rMax, seaLevel, scale);
        }

        // ── 배치 ────────────────────────────────────────────────────────────────────

        /// <summary>
        /// 산호밭 2~3패치(×크기 스케일). 패치 중심은 깊이 1.5~7m 대역에서 뽑고, 패치당 산호 5~9개를
        /// 중심 반경 3~7m에 산포한다. 같은 패치엔 같은 계열 60% 편향(군락감).
        /// </summary>
        private static void SpawnCoralPatches(System.Random rng, Transform root, Vector3 center,
            float rMin, float rMax, float seaLevel, float scale)
        {
            int patchCount = Mathf.Clamp(Mathf.RoundToInt(rng.NextInt(2, 4) * scale), 1, 5);
            for (int p = 0; p < patchCount; p++)
            {
                // 패치 중심: 깊이 1.5~7m 대역. 최대 시도 수 고정(무한 루프 금지) - 실패하면 이 패치만 버린다.
                if (!TryPickPoint(rng, center, rMin, rMax, seaLevel, 1.5f, 7f, 12,
                        out Vector3 patchCenter, out _))
                    continue;

                int patchFamily = rng.NextInt(0, CoralFamilyStart.Length);
                int coralCount = rng.NextInt(5, 10);
                for (int c = 0; c < coralCount; c++)
                {
                    // 산포 후보: 중심 반경 3~7m 극좌표. 접지 실패(스커트 범위 밖)면 그 후보만 버린다.
                    Vector3 pos = Vector3.zero;
                    bool found = false;
                    for (int attempt = 0; attempt < 4 && !found; attempt++)
                    {
                        float angle = rng.NextFloat(0f, Mathf.PI * 2f);
                        float dist = rng.NextFloat(3f, 7f);
                        var candidate = patchCenter
                            + new Vector3(Mathf.Cos(angle) * dist, 0f, Mathf.Sin(angle) * dist);
                        if (SeabedGenerator.TrySampleSeabed(candidate, out float y))
                        {
                            pos = new Vector3(candidate.x, y, candidate.z);
                            found = true;
                        }
                    }

                    if (!found)
                        continue;

                    // 계열 편향 60% + 계열 내 변종 균등. 변종 인덱스가 곧 팔레트 순환 키다.
                    int family = rng.NextValue01() < 0.6f ? patchFamily : rng.NextInt(0, CoralFamilyStart.Length);
                    int variant = CoralFamilyStart[family] + rng.NextInt(0, CoralFamilyCount[family]);
                    float yaw = rng.NextFloat(0f, 360f);
                    float size = rng.NextFloat(0.8f, 1.35f);
                    PlaceCoral(root, center, pos, variant, yaw, size);
                }
            }
        }

        /// <summary>
        /// 해초 숲 1~3군데(×크기 스케일), 깊이 2~10m. 군데당 kelp 5~10개를 중심 반경 1.5~4.5m에 심고,
        /// 심는 지점의 실제 깊이로 리본형(깊은 쪽, 높이 1.4m+)/방석형(얕은 쪽)을 가른다.
        /// </summary>
        private static void SpawnKelpGroves(System.Random rng, Transform root, Vector3 center,
            float rMin, float rMax, float seaLevel, float scale)
        {
            int groveCount = Mathf.Clamp(Mathf.RoundToInt(rng.NextInt(1, 4) * scale), 1, 5);
            for (int g = 0; g < groveCount; g++)
            {
                if (!TryPickPoint(rng, center, rMin, rMax, seaLevel, 2f, 10f, 12,
                        out Vector3 groveCenter, out _))
                    continue;

                int kelpCount = rng.NextInt(5, 11);
                for (int k = 0; k < kelpCount; k++)
                {
                    Vector3 pos = Vector3.zero;
                    float depth = 0f;
                    bool found = false;
                    for (int attempt = 0; attempt < 4 && !found; attempt++)
                    {
                        float angle = rng.NextFloat(0f, Mathf.PI * 2f);
                        float dist = rng.NextFloat(1.5f, 4.5f);
                        var candidate = groveCenter
                            + new Vector3(Mathf.Cos(angle) * dist, 0f, Mathf.Sin(angle) * dist);
                        if (SeabedGenerator.TrySampleSeabed(candidate, out float y))
                        {
                            pos = new Vector3(candidate.x, y, candidate.z);
                            depth = seaLevel - y;
                            found = true;
                        }
                    }

                    if (!found)
                        continue;

                    // 리본형은 깊은 쪽(수면까지 뻗을 공간이 필요), 방석형은 얕은 쪽. 경계 4.5m.
                    int variant = PickKelpVariant(rng, depth >= 4.5f);
                    if (variant < 0)
                        continue;

                    float yaw = rng.NextFloat(0f, 360f);
                    float size = rng.NextFloat(0.8f, 1.4f);
                    PlaceKelp(root, center, pos, variant, yaw, size);
                }
            }
        }

        /// <summary>
        /// 수중 바위 8~14개(×크기 스케일)를 스커트 전 깊이에 산포한다. 첨탑(k~o)은 깊이 6m 이상에만
        /// (잠수 랜드마크), 스케일 적용 후 최대 치수 1.5m 초과인 것에만 BoxCollider(대략치)를 단다.
        /// </summary>
        private static void SpawnSeaRocks(System.Random rng, Transform root, Vector3 center,
            float rMin, float rMax, float seaLevel, float scale)
        {
            int rockCount = Mathf.Clamp(Mathf.RoundToInt(rng.NextInt(8, 15) * scale), 5, 26);
            int placed = 0;
            int maxAttempts = rockCount * 4; // 시도 수 고정 - 무한 루프 금지
            for (int attempt = 0; attempt < maxAttempts && placed < rockCount; attempt++)
            {
                if (!TryPickPoint(rng, center, rMin, rMax, seaLevel, 0.5f, 18.5f, 1,
                        out Vector3 pos, out float depth))
                    continue;

                // 깊이 6m 이상에서만 30% 확률로 첨탑. 얕은 곳은 표석/판형/군집(비첨탑 15종)만.
                int variant;
                if (depth >= 6f && rng.NextValue01() < 0.30f)
                {
                    variant = SpireStart + rng.NextInt(0, SpireCount);
                }
                else
                {
                    int pick = rng.NextInt(0, RockModelNames.Length - SpireCount); // 0~14
                    variant = pick < SpireStart ? pick : pick + SpireCount;        // 첨탑 구간 건너뜀
                }

                float yaw = rng.NextFloat(0f, 360f);
                float size = rng.NextFloat(0.8f, 1.6f);
                if (PlaceRock(root, center, pos, variant, yaw, size))
                    placed++;
            }
        }

        // ── 개별 배치물 조립 ──────────────────────────────────────────────────────────

        /// <summary>
        /// 산호 하나. 병합 임포트(서브메시 2)면 렌더러 하나 + sharedMaterials [body색, tip색],
        /// 개별 메시 2장이면 파츠 2개(각각 한 색). tip은 body색의 Shade 1.35(성장점이 밝다).
        /// </summary>
        private static void PlaceCoral(Transform root, Vector3 islandCenter, Vector3 worldPos,
            int variant, float yaw, float scale)
        {
            Mesh body = coralPrimary[variant];
            if (body == null)
                return; // 이 변종만 아직 안 로드됨 - 조용히 건너뛴다(래치 없음, 다음 월드에서 복구)

            Color bodyColor = CoralPalette[variant % CoralPalette.Length];
            Material bodyMaterial = ResourceVisualLibrary.GetMaterial(bodyColor, "noise");
            Material tipMaterial = ResourceVisualLibrary.GetMaterial(
                ResourceVisualLibrary.Shade(bodyColor, 1.35f), "noise");

            // 밑면 y=0 접지 모델을 모래 기복(±0.6m) 속에 살짝 묻는다 - 가장자리 들뜸 방지.
            Vector3 localPos = worldPos - islandCenter + new Vector3(0f, -0.08f, 0f);
            var part = CreateVisualPart(root, "Coral_" + CoralModelNames[variant], body,
                bodyMaterial, localPos, yaw, scale);

            var renderer = part.GetComponent<MeshRenderer>();
            if (coralSecondary[variant] != null)
            {
                // 개별 메시 임포트: tip을 같은 위치/회전/스케일의 파츠로 하나 더(임포터 동작 방어).
                CreateVisualPart(root, "Coral_" + CoralModelNames[variant] + "_tip",
                    coralSecondary[variant], tipMaterial, localPos, yaw, scale);
            }
            else if (renderer != null && body.subMeshCount >= 2)
            {
                // 병합 임포트(Unity 6.5 실동작): 서브메시 순서는 OBJ `o` 순서(body → tip)다.
                renderer.sharedMaterials = new[] { bodyMaterial, tipMaterial };
            }
        }

        /// <summary>해초 하나(양면 blade 메시 1장, 콜라이더 없음).</summary>
        private static void PlaceKelp(Transform root, Vector3 islandCenter, Vector3 worldPos,
            int variant, float yaw, float scale)
        {
            Mesh blade = kelpMeshes[variant];
            if (blade == null)
                return;

            Material material = ResourceVisualLibrary.GetMaterial(
                KelpPalette[variant % KelpPalette.Length], "leaf");
            Vector3 localPos = worldPos - islandCenter + new Vector3(0f, -0.06f, 0f);
            CreateVisualPart(root, "Kelp_" + KelpModelNames[variant], blade, material, localPos, yaw, scale);
        }

        /// <summary>
        /// 수중 바위 하나. 스케일 적용 후 최대 치수가 1.5m를 넘으면 BoxCollider(실측 크기의 85% 폭,
        /// 대략치)를 붙여 잠수 시 실제로 부딪히는 랜드마크로 만든다. 작은 것은 콜라이더 없음.
        /// </summary>
        private static bool PlaceRock(Transform root, Vector3 islandCenter, Vector3 worldPos,
            int variant, float yaw, float scale)
        {
            Mesh mesh = rockMeshes[variant];
            if (mesh == null)
                return false;

            Material material = ResourceVisualLibrary.GetMaterial(
                RockPalette[variant % RockPalette.Length], "rock");
            Vector3 localPos = worldPos - islandCenter + new Vector3(0f, -0.10f, 0f);
            var part = CreateVisualPart(root, "SeaRock_" + RockModelNames[variant], mesh,
                material, localPos, yaw, scale);

            Vector3 size = RockModelSizes[variant];
            float maxDimension = Mathf.Max(size.x, Mathf.Max(size.y, size.z)) * scale;
            if (maxDimension > 1.5f)
            {
                // 파츠 로컬 공간 기준 대략치 - 부모 스케일이 균등이라 콜라이더도 함께 커진다.
                // 이름이 "SeaRock_"이라 지형 판정("Island_" 필터)에는 구조적으로 안 잡힌다.
                var box = part.AddComponent<BoxCollider>();
                box.center = new Vector3(0f, size.y * 0.5f, 0f);
                box.size = new Vector3(size.x * 0.85f, size.y, size.z * 0.85f);
            }

            return true;
        }

        /// <summary>
        /// 공유 메시 + 공유 머티리얼로 순수 시각 파츠 하나(콜라이더 없는 경로 - CreateMeshPart).
        /// 수중이라 그림자 캐스팅/수신을 모두 끈다(스커트와 같은 규칙).
        /// </summary>
        private static GameObject CreateVisualPart(Transform root, string name, Mesh mesh,
            Material material, Vector3 localPos, float yaw, float scale)
        {
            var go = StructureVisualBuilder.CreateMeshPart(root, name, mesh,
                localPos, Vector3.one * scale, Quaternion.Euler(0f, yaw, 0f), material);
            var renderer = go.GetComponent<MeshRenderer>();
            if (renderer != null)
            {
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }

            return go;
        }

        // ── 후보 샘플링 ─────────────────────────────────────────────────────────────

        /// <summary>
        /// 섬 중심 기준 극좌표 후보를 뽑아 SeabedGenerator.TrySampleSeabed로 접지하고, 깊이
        /// (seaLevel - 해저 y)가 [depthMin, depthMax]에 들면 채택한다. 시도 수는 고정(무한 루프 금지)
        /// 이고, 실패한 후보는 그 후보만 버린다. worldPos.y에는 해저 y가 들어간다.
        /// </summary>
        private static bool TryPickPoint(System.Random rng, Vector3 center, float rMin, float rMax,
            float seaLevel, float depthMin, float depthMax, int maxAttempts,
            out Vector3 worldPos, out float depth)
        {
            for (int attempt = 0; attempt < maxAttempts; attempt++)
            {
                float angle = rng.NextFloat(0f, Mathf.PI * 2f);
                float r = rng.NextFloat(rMin, rMax);
                var candidate = new Vector3(
                    center.x + Mathf.Cos(angle) * r, 0f, center.z + Mathf.Sin(angle) * r);

                if (!SeabedGenerator.TrySampleSeabed(candidate, out float seabedY))
                    continue;

                float d = seaLevel - seabedY;
                if (d < depthMin || d > depthMax)
                    continue;

                worldPos = new Vector3(candidate.x, seabedY, candidate.z);
                depth = d;
                return true;
            }

            worldPos = Vector3.zero;
            depth = 0f;
            return false;
        }

        /// <summary>
        /// 리본형(높이 1.4m 이상)/방석형 중 요청된 형에서 로드된 변종 하나를 뽑는다.
        /// 해당 형이 하나도 안 로드됐으면 -1(호출부가 이 후보만 버린다).
        /// 후보 집계가 결정적(모델 로드는 전부-아니면-일부지만 실측 높이 표는 상수)이라
        /// 같은 시드면 같은 선택이 나온다.
        /// </summary>
        private static int PickKelpVariant(System.Random rng, bool ribbon)
        {
            int count = 0;
            for (int i = 0; i < kelpMeshes.Length; i++)
            {
                if (kelpMeshes[i] != null && (KelpModelHeights[i] >= 1.4f) == ribbon)
                    count++;
            }

            if (count == 0)
                return -1;

            int pick = rng.NextInt(0, count);
            for (int i = 0; i < kelpMeshes.Length; i++)
            {
                if (kelpMeshes[i] == null || (KelpModelHeights[i] >= 1.4f) != ribbon)
                    continue;
                if (pick == 0)
                    return i;
                pick--;
            }

            return -1;
        }

        // ── 로더 ────────────────────────────────────────────────────────────────────

        /// <summary>
        /// 모델 50종의 공유 메시를 채운다. ResourceVisualLibrary.TryLoadTwoPartModel(검증된
        /// Load&lt;GameObject&gt;+MeshFilter 로더)을 그대로 쓰고, 프레임당 1회만 프로브하며(같은 프레임의
        /// 섬 50개 생성 루프에서 Load가 50번 반복되지 않게), 실패를 영구 래치하지 않는다.
        /// 산호 OBJ의 `o` 2개는 이름 키워드(trunk/leaf류)에 안 걸리므로 그 로더의 "o 등장 순서" 폴백이
        /// body → tip 순서를 보장하고, 병합 임포트면 첫 메시(서브메시 2)만 오고 tip은 null이다.
        /// </summary>
        private static void EnsureModelsLoaded()
        {
            bool anyMissing = false;
            for (int i = 0; i < coralPrimary.Length && !anyMissing; i++)
                anyMissing = coralPrimary[i] == null;
            for (int i = 0; i < kelpMeshes.Length && !anyMissing; i++)
                anyMissing = kelpMeshes[i] == null;
            for (int i = 0; i < rockMeshes.Length && !anyMissing; i++)
                anyMissing = rockMeshes[i] == null;

            if (!anyMissing || probeFrame == Time.frameCount)
                return;
            probeFrame = Time.frameCount;

            for (int i = 0; i < CoralModelNames.Length; i++)
            {
                if (coralPrimary[i] != null)
                    continue;
                if (ResourceVisualLibrary.TryLoadTwoPartModel("Models/" + CoralModelNames[i],
                        out Mesh body, out Mesh tip))
                {
                    coralPrimary[i] = body;
                    coralSecondary[i] = tip; // 병합 임포트면 null - PlaceCoral의 서브메시 분기가 처리
                }
            }

            for (int i = 0; i < KelpModelNames.Length; i++)
            {
                if (kelpMeshes[i] != null)
                    continue;
                if (ResourceVisualLibrary.TryLoadTwoPartModel("Models/" + KelpModelNames[i],
                        out Mesh blade, out _))
                    kelpMeshes[i] = blade;
            }

            for (int i = 0; i < RockModelNames.Length; i++)
            {
                if (rockMeshes[i] != null)
                    continue;
                if (ResourceVisualLibrary.TryLoadTwoPartModel("Models/" + RockModelNames[i],
                        out Mesh rock, out _))
                    rockMeshes[i] = rock;
            }
        }

        // ── 유틸 ────────────────────────────────────────────────────────────────────

        /// <summary>"Island_{id}_{size}"에서 islandId를 파싱한다(이름은 SpawnPlaceholder가 붙인다).
        /// 파싱 실패(placeholder 프리팹 등 비표준 이름)면 0 - 그래도 worldSeed 격리는 유지된다.</summary>
        private static int ParseIslandId(string islandName)
        {
            if (string.IsNullOrEmpty(islandName))
                return 0;
            string[] tokens = islandName.Split('_');
            if (tokens.Length >= 2 && int.TryParse(tokens[1], out int id))
                return id;
            return 0;
        }

        /// <summary>섬 크기별 배치 스케일. 반지름 경계(50/90/140/200)의 중간값으로 가른다.</summary>
        private static float SizeScale(float radius)
        {
            if (radius < 70f) return 0.7f;   // Small
            if (radius < 115f) return 1.0f;  // Medium
            if (radius < 170f) return 1.4f;  // Large
            return 1.8f;                     // ExtraLarge
        }
    }
}
