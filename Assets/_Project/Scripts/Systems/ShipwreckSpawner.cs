using UnityEngine;
using MakeGame.Data;
using MakeGame.Player;

namespace MakeGame.Systems
{
    /// <summary>
    /// 섬 해저 스커트의 **난파선 잔해**(부러진 선체 + 흩어진 화물 + 수거 지점 2~4곳) 배치기.
    ///
    /// ── 왜 필요한가 ───────────────────────────────────────────────────────────────
    /// 이 게임의 비제작 부품(엔진부품 등) 공급원은 여객기 잔해 6지점(1회성)과 해저 화물 소수뿐이라,
    /// "바다에 나가 잠수해 뒤진다"는 **상시 자원 루프**가 없었다. 난파선은 그 빈자리를 메우는
    /// 반복 가능한 탐험 목표다 - 섬마다 0~2척이 수심 5~16m에 흩어져 있고, 한 척에 수거 지점이
    /// 2~4곳 붙어 있어 배 한 척을 다 털려면 여러 번 잠수해야 한다.
    ///
    /// ── 호출 지점 ────────────────────────────────────────────────────────────────
    /// SeabedGenerator.Build가 스커트 레코드를 등록하고 SeabedFloraSpawner → UnderwaterCaveSpawner
    /// → MarineLifeSpawner를 부른 **직후, 같은 동기 흐름에서** 마지막으로 호출된다.
    /// 스커트가 먼저 등록돼 있어야 TrySampleSeabed 접지가 유효하다(앞 세 스포너와 같은 전제).
    ///
    /// ── 신규 모델 0개 (기존 메시 조합) ────────────────────────────────────────────
    /// 3D 에셋 제작은 이 작업 밖이라, 난파선은 **이미 있는 뗏목/화물 모델의 재조합**이다.
    ///   · 선체     : raft_base_wood(2×2m 갑판 타일) 여러 장을 앞·뒤 두 동강으로 나눠 각각 다른
    ///                각도로 기울여 깔면 "허리가 부러져 옆으로 누운 배"가 된다.
    ///   · 갑판 파편 : raft_floor(얇은 판) 1~2장.
    ///   · 부러진 돛대: raft_sail(3.2m 돛대+돛)을 60~85° 눕혀 모래에 처박는다.
    ///   · 선미/선수 : raft_rudder(방향타), raft_anchor(닻).
    ///   · 잔해     : plankpile_a(널판 더미) 2~4개.
    ///   · 화물     : crate_a / barrel_a 3~6개(SeabedFloraSpawner 침몰 화물과 같은 두 모델).
    /// 머티리얼은 침몰 화물과 **완전히 같은 두 색**(wood/metal)을 GetMaterial 공유 캐시에서 받아
    /// 새 머티리얼이 늘지 않고, 돛천 한 장만 새로 생긴다(월드 전체 1장).
    ///
    /// ── rng 격리 (최중요) ─────────────────────────────────────────────────────────
    /// 섬마다 `new System.Random(unchecked(worldSeed * 397 ^ islandId ^ 0xB0A7))` 전용 독립
    /// 스트림만 소비한다. 기존 스트림(생태 0x5EABED · 조개 0xC1A0 · 동굴 0xCA7E · 지형지물 0x5EAF ·
    /// 잔해 0x0B0CCA · 해양생물 0x1A9E · 상어 -1000000 · 섬 레이아웃 -2000000 · 보스 -3000000 ·
    /// 초목 3000000+ 대역)은 만들지도 이어 뽑지도 않는다. 섬 id는 50 미만이라 xor 시드끼리도
    /// 충돌할 수 없다(두 스트림이 같은 시드가 되려면 islandId 차이가 salt 차이(≥0x1400)와 같아야
    /// 한다). UnityEngine.Random도 일절 쓰지 않는다(SeededRandomExtensions 상단 주석).
    ///
    /// ── 세이브 무관 ──────────────────────────────────────────────────────────────
    /// ResourceNode/Campfire 등 세이브 대상 컴포넌트를 하나도 붙이지 않으므로 저장 파일에 한
    /// 바이트도 들어가지 않는다. 수거 지점(AirlinerSalvagePoint)의 수거 여부가 로드마다 리셋되는
    /// 것은 침몰 화물·진주조개와 **완전히 같은 기존 한계**다(AirlinerSalvagePoint [한계] 주석).
    ///
    /// ── TerrainSampler 오염 없음 ─────────────────────────────────────────────────
    /// 모든 오브젝트 이름이 "Shipwreck_"/"Wreck"/"Hull"/"Cargo"/"Salvage" 계열이라 "Island_"로
    /// 시작하지 않는다 - SnapToGround류 지형 판정(TerrainSampler.IsTerrainHit의 이름 접두 필터)이
    /// 수거 지점 BoxCollider를 뚫고 지나간다(침몰 화물/동굴 셸과 같은 안전 근거).
    ///
    /// ── 성능 ────────────────────────────────────────────────────────────────────
    /// 한 척당 렌더러 약 15~25개, 섬당 최대 2척. 플레이어가 <see cref="ActiveDistance"/>(260m)보다
    /// 멀면 척마다 컨테이너를 통째로 SetActive(false)로 끈다(기존 200~300m 관례 - MarineLifeDriver
    /// 200m / BossCreature 300m 사이). 드라이버는 15프레임에 한 번 거리 제곱 1회만 계산하고
    /// **프레임당 할당이 0**이다(문자열 조립·LINQ·new 없음).
    /// </summary>
    public static class ShipwreckSpawner
    {
        /// <summary>섬별 난파선 루트 이름 접두사. "Island_"로 시작하지 않는 것이 지형 판정 안전의 전제다.</summary>
        private const string ShipwreckRootPrefix = "Shipwreck_";

        /// <summary>rng 격리용 시드 소금. 기존 0x5EABED/0xC1A0/0xCA7E/0x5EAF/0x0B0CCA/0x1A9E 및
        /// -1000000/-2000000/-3000000/3000000+ salt 대역 어느 것과도 겹치지 않는 새 값이다.</summary>
        private const int SeedSalt = 0xB0A7;

        /// <summary>난파선이 설 수 있는 수심대(m). 잠수해서 들어가야 하도록 5m부터 시작한다.</summary>
        private const float DepthMin = 5f;
        private const float DepthMax = 16f;

        /// <summary>배치 후보 시도 수(고정 - 무한 루프 금지, TryPickPoint와 같은 규칙).</summary>
        private const int MaxAttempts = 20;

        /// <summary>난파선 반경(m). 스커트 안팎 경계에서 이만큼 물려 잔해가 환형 밖으로 안 나가게 한다.</summary>
        private const float WreckFootprint = 9f;

        /// <summary>원거리 컬링 거리(m). 이보다 멀면 척 전체를 SetActive(false)로 끈다.</summary>
        private const float ActiveDistance = 260f;

        /// <summary>컬링 판정 주기(프레임). 척이 몇 개든 매 프레임 전부 계산하지 않게 스태거한다.</summary>
        private const int CullCheckInterval = 15;

        // ── 모델 카탈로그 (Resources/Models, 확장자 없음) ──────────────────────────
        // 순서 = 아래 인덱스 상수. 뗏목 모델 5종은 전부 `o` 그룹 2개(첫째 wood, 둘째 metal 또는
        // cloth)이고, plankpile/crate/barrel은 `o` 1개다 - RaftStructure.EnsureModelsLoaded가
        // 검증한 것과 같은 구조라 로더도 그대로 TryLoadTwoPartModel을 쓴다.

        private const int ModelHull = 0;     // raft_base_wood : 2.0 × 0.28 × 2.0
        private const int ModelDeck = 1;     // raft_floor     : 2.0 × 0.08 × 2.0
        private const int ModelMast = 2;     // raft_sail      : 1.8 × 3.20 × 0.48 (둘째 o = cloth)
        private const int ModelRudder = 3;   // raft_rudder    : 0.5 × 1.40 × 0.90
        private const int ModelAnchor = 4;   // raft_anchor    : 0.6 × 0.80 × 0.60
        private const int ModelPlanks = 5;   // plankpile_a    : 2.1 × 0.22 × 0.86
        private const int ModelCrate = 6;    // crate_a        : 0.82 × 0.66 × 0.74
        private const int ModelBarrel = 7;   // barrel_a       : 0.60 × 0.86 × 0.60

        private static readonly string[] ModelNames =
        {
            "raft_base_wood", "raft_floor", "raft_sail", "raft_rudder",
            "raft_anchor", "plankpile_a", "crate_a", "barrel_a",
        };

        /// <summary>각 모델의 실측 크기(m, W×H×D · 밑면 y=0 · XZ 대략 중심). OBJ 정점 실측값이고,
        /// 기울인 파츠를 들어 올리는 보정(lift)과 수거 지점 BoxCollider 대략치에 쓴다.</summary>
        private static readonly Vector3[] ModelSizes =
        {
            new Vector3(2.00f, 0.28f, 2.00f),
            new Vector3(2.00f, 0.08f, 2.00f),
            new Vector3(1.80f, 3.20f, 0.48f),
            new Vector3(0.50f, 1.40f, 0.90f),
            new Vector3(0.60f, 0.80f, 0.60f),
            new Vector3(2.10f, 0.22f, 0.86f),
            new Vector3(0.82f, 0.66f, 0.74f),
            new Vector3(0.60f, 0.86f, 0.60f),
        };

        // ── 공유 메시 캐시 ──────────────────────────────────────────────────────────
        private static readonly Mesh[] modelPrimary = new Mesh[8];
        private static readonly Mesh[] modelSecondary = new Mesh[8];

        /// <summary>프레임당 1회 프로브 가드(SeabedFloraSpawner.probeFrame과 같은 규칙 - 같은 프레임의
        /// 섬 생성 루프에서 Resources.Load가 반복되지 않게 하되, 실패를 영구 래치하지 않는다).</summary>
        private static int probeFrame = -1;

        // ── 플레이어 참조 공유 캐시 (척마다 따로 찾지 않게) ───────────────────────────
        private static Transform playerTransform;
        private static int playerProbeFrame = -1;

        /// <summary>
        /// 도메인 리로드를 끈 플레이 모드에서 static 캐시가 이전 실행의 파괴된 자원을 들고 시작하지
        /// 않게 초기 상태로 되돌린다(R1 규칙 - SeabedGenerator/MarineLifeSpawner와 같은 훅).
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStaticCache()
        {
            System.Array.Clear(modelPrimary, 0, modelPrimary.Length);
            System.Array.Clear(modelSecondary, 0, modelSecondary.Length);
            probeFrame = -1;
            playerTransform = null;
            playerProbeFrame = -1;
        }

        // ══════════════════════════════════════════════════════════════════════════
        // 배치
        // ══════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// 섬 하나의 난파선을 배치한다. SeabedGenerator.Build가 스커트를 등록하고 앞선 세 스포너를
        /// 부른 뒤 같은 동기 흐름에서 마지막으로 호출한다(그래야 TrySampleSeabed 접지가 유효하다).
        ///
        /// 척 수는 섬 규모별로 소형 0~1 / 중형 1~2 / 대형 1~2 / 특대 2다(반지름 경계 70·115·170은
        /// SeabedFloraSpawner.SizeScale와 같은 중간값 규약).
        /// </summary>
        /// <param name="islandObject">섬 지형 루트("Island_{id}_{size}"). 난파선은 전부 이 자식이다.</param>
        /// <param name="radius">섬 지형 반지름 R(m). 스커트 안쪽 경계와 같다.</param>
        public static void Spawn(GameObject islandObject, float radius)
        {
            if (islandObject == null || radius <= 0f)
                return;

            // 같은 섬에 두 번 불려도(방어) 난파선이 겹으로 깔리지 않게 한다(앞선 세 스포너와 동일).
            string rootName = ShipwreckRootPrefix + islandObject.name;
            if (islandObject.transform.Find(rootName) != null)
                return;

            // worldSeed/seaLevel은 섬 루트의 부모(WorldMapManager)에서 읽는다 - 읽기 전용 접근이라
            // 어떤 rng 스트림도 소비하지 않는다(SeabedFloraSpawner.Spawn과 같은 경로).
            var manager = islandObject.GetComponentInParent<WorldMapManager>();
            int worldSeed = manager != null ? manager.worldSeed : 0;
            float seaLevel = manager != null ? manager.seaLevel : 0f;
            int islandId = ParseIslandId(islandObject.name);

            // [rng 격리] 이 섬 전용 독립 스트림. 여기서 몇 번을 뽑든 다른 시스템의 추첨 순서는 불변이다.
            var rng = new System.Random(unchecked(worldSeed * 397 ^ islandId ^ SeedSalt));

            int wreckCount;
            if (radius < 70f)
                wreckCount = rng.NextInt(0, 2);   // 소형 0~1
            else if (radius < 115f)
                wreckCount = rng.NextInt(1, 3);   // 중형 1~2
            else if (radius < 170f)
                wreckCount = rng.NextInt(1, 3);   // 대형 1~2
            else
                wreckCount = 2;                   // 특대 2

            if (wreckCount <= 0)
                return; // 이 섬에는 난파선이 없다 - 루트도 만들지 않는다(빈 오브젝트 금지).

            EnsureModelsLoaded();

            // 스커트 폭. SeabedGenerator.SkirtWidth와 같은 식의 사본이다(그쪽은 private) - 어긋나도
            // 후보 적중률만 떨어질 뿐, 접지 정답은 항상 TrySampleSeabed가 준다(범위 밖이면 false).
            float skirtWidth = Mathf.Clamp(radius * 0.6f, 30f, 90f);
            float rMin = radius + WreckFootprint;
            float rMax = radius + skirtWidth - WreckFootprint;

            Vector3 center = islandObject.transform.position;

            var root = new GameObject(rootName);
            root.transform.SetParent(islandObject.transform, false);
            root.transform.localPosition = Vector3.zero;
            root.transform.localRotation = Quaternion.identity;
            root.transform.localScale = Vector3.one;

            for (int i = 0; i < wreckCount; i++)
            {
                // 후보 채택은 rng(각·반경)만 소비하고 지형 샘플은 결정적이라, 같은 시드면 같은 자리다.
                bool found = TryPickPoint(rng, center, rMin, rMax, seaLevel, out Vector3 pos);

                // 자세/구성 draw는 채택 성공 여부와 무관하게 **항상 같은 횟수·순서로** 소비한다
                // (SpawnPearlClams와 같은 결정성 규칙 - 배치가 실패해도 뒤 척의 추첨이 밀리지 않는다).
                var plan = DrawWreckPlan(rng);

                if (!found)
                    continue;

                BuildWreck(root.transform, center, pos, plan, i);
            }

            // 척이 하나도 서지 못했으면 빈 루트를 남기지 않는다(다음 월드에서 다시 시도된다).
            if (root.transform.childCount == 0)
            {
                root.SetActive(false);
                Object.Destroy(root);
            }
        }

        /// <summary>
        /// 스커트 환형 안에서 수심 5~16m 지점을 결정적으로 찾는다. 시도 수는 고정이고,
        /// 실패하면 그 척만 버린다(SeabedFloraSpawner.TryPickPoint와 같은 규약).
        /// worldPos.y에는 해저 y가 들어간다.
        /// </summary>
        private static bool TryPickPoint(System.Random rng, Vector3 center, float rMin, float rMax,
            float seaLevel, out Vector3 worldPos)
        {
            worldPos = Vector3.zero;
            if (rMax <= rMin)
                return false; // 스커트가 난파선을 담기엔 좁다(draw 소비 없이 즉시 실패)

            for (int attempt = 0; attempt < MaxAttempts; attempt++)
            {
                float angle = rng.NextFloat(0f, Mathf.PI * 2f);
                float r = rng.NextFloat(rMin, rMax);
                var candidate = new Vector3(
                    center.x + Mathf.Cos(angle) * r, 0f, center.z + Mathf.Sin(angle) * r);

                if (!SeabedGenerator.TrySampleSeabed(candidate, out float seabedY))
                    continue;

                float depth = seaLevel - seabedY;
                if (depth < DepthMin || depth > DepthMax)
                    continue;

                worldPos = new Vector3(candidate.x, seabedY, candidate.z);
                return true;
            }

            return false;
        }

        // ══════════════════════════════════════════════════════════════════════════
        // 난파선 한 척의 구성 계획 (draw 전부 - 메시 로드 여부와 무관하게 같은 횟수)
        // ══════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// 난파선 한 척의 모든 무작위 결정. **struct라 힙 할당이 없고**, 배치·메시 확인 전에
        /// 전부 뽑아 두므로 임포트가 한 프레임 늦어도 같은 시드의 다음 월드에서 같은 배가 나온다
        /// (SeabedFloraSpawner.PlaceCargoPile의 "draw 전부 → 메시 확인 → 생성" 규칙).
        /// </summary>
        private struct WreckPlan
        {
            public float heading;        // 용골 방향(도) - 배 전체의 yaw
            public float scale;          // 배 크기 배율
            public float bowRoll;        // 선수 동강의 좌우 기울기(도)
            public float sternRoll;      // 선미 동강의 기울기(도) - 더 크게 넘어가 있다
            public float breakGap;       // 두 동강 사이 벌어짐(m)
            public float sternYawOffset; // 선미 동강이 용골에서 틀어진 각(도)
            public int bowTiles;         // 선수 동강 갑판 타일 수
            public int sternTiles;       // 선미 동강 갑판 타일 수
            public float mastPitch;      // 부러진 돛대가 누운 각(도)
            public float mastYaw;        // 돛대가 쓰러진 방향(도, 용골 기준 상대)
            public float mastScale;
            public bool hasDeckPanel;    // 갑판 파편(raft_floor) 유무
            public bool hasRudder;
            public bool hasAnchor;
            public int plankCount;       // 널판 더미 2~4
            public int cargoCount;       // 궤짝/통 3~6
            public int salvageCount;     // 수거 지점 2~4
            public uint detailSeed;      // 세부 자세(각도/오프셋)를 뽑는 결정적 해시 시드
        }

        /// <summary>한 척의 계획을 뽑는다. 소비 draw 수는 항상 고정(20회)이다.</summary>
        private static WreckPlan DrawWreckPlan(System.Random rng)
        {
            WreckPlan p;
            p.heading = rng.NextFloat(0f, 360f);
            p.scale = rng.NextFloat(0.95f, 1.35f);
            p.bowRoll = rng.NextFloat(14f, 42f) * (rng.NextValue01() < 0.5f ? -1f : 1f);
            p.sternRoll = rng.NextFloat(48f, 82f) * (rng.NextValue01() < 0.5f ? -1f : 1f);
            p.breakGap = rng.NextFloat(1.1f, 2.6f);
            p.sternYawOffset = rng.NextFloat(-38f, 38f);
            p.bowTiles = rng.NextInt(2, 4);      // 2~3장
            p.sternTiles = rng.NextInt(2, 4);    // 2~3장
            p.mastPitch = rng.NextFloat(62f, 86f);
            p.mastYaw = rng.NextFloat(-70f, 70f);
            p.mastScale = rng.NextFloat(1.1f, 1.6f);
            p.hasDeckPanel = rng.NextValue01() < 0.7f;
            p.hasRudder = rng.NextValue01() < 0.8f;
            p.hasAnchor = rng.NextValue01() < 0.55f;
            p.plankCount = rng.NextInt(2, 5);    // 2~4
            p.cargoCount = rng.NextInt(3, 7);    // 3~6
            p.salvageCount = rng.NextInt(2, 5);  // 2~4
            p.detailSeed = (uint)rng.NextInt(1, int.MaxValue);
            return p;
        }

        // ══════════════════════════════════════════════════════════════════════════
        // 조립
        // ══════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// 난파선 한 척을 세운다. 구조는 루트("Wreck_{i}") → 컨테이너("Parts") → 파츠들이고,
        /// 원거리 컬링은 **컨테이너만** 끈다(드라이버는 루트에 붙어 계속 살아 있어야 다시 켤 수 있다).
        ///
        /// 세부 자세(널판/화물의 각도·오프셋)는 rng가 아니라 계획의 detailSeed에서 파생한 순수
        /// 해시다 - 파츠 수가 메시 로드 상태에 따라 흔들려도 rng draw 수는 이미 확정돼 있어
        /// 결정성이 깨지지 않는다(계획 단계에서 draw를 전부 끝낸다는 규칙의 연장).
        /// </summary>
        private static void BuildWreck(Transform islandRoot, Vector3 islandCenter, Vector3 worldPos,
            in WreckPlan plan, int index)
        {
            // 선체 타일이 하나도 없으면 "배"로 읽히지 않는다 - 그때는 아무것도 만들지 않는다
            // (보이지 않는 수거 지점 금지 - PlaceCargoPile과 같은 폴백. 래치는 걸지 않는다).
            if (modelPrimary[ModelHull] == null)
                return;

            Material wood = ResourceVisualLibrary.GetMaterial(new Color(0.24f, 0.19f, 0.14f), "wood");
            Material metal = ResourceVisualLibrary.GetMaterial(new Color(0.22f, 0.24f, 0.26f), "metal");
            Material cloth = ResourceVisualLibrary.GetMaterial(new Color(0.58f, 0.56f, 0.50f), "noise");

            var wreck = new GameObject("Wreck_" + index);
            wreck.transform.SetParent(islandRoot, false);
            wreck.transform.localPosition = worldPos - islandCenter;
            wreck.transform.localRotation = Quaternion.Euler(0f, plan.heading, 0f);

            var container = new GameObject("Parts");
            container.transform.SetParent(wreck.transform, false);
            Transform parts = container.transform;

            float s = plan.scale;
            float tile = ModelSizes[ModelHull].x * s;          // 갑판 타일 한 변(월드 m)
            float bowLength = plan.bowTiles * tile;
            float sternLength = plan.sternTiles * tile;

            // 용골은 로컬 +Z가 선수. 선수 동강은 +Z쪽, 선미 동강은 -Z쪽에 벌어진 틈을 두고 놓는다.
            float bowStart = plan.breakGap * 0.5f;
            float sternStart = -plan.breakGap * 0.5f;

            // ── 선수 동강 ──────────────────────────────────────────────────────────
            for (int i = 0; i < plan.bowTiles; i++)
            {
                float z = bowStart + tile * (i + 0.5f);
                PlaceHullTile(parts, wood, metal, ModelHull, new Vector3(0f, 0f, z),
                    0f, plan.bowRoll, s, "HullBow_" + i);
            }

            // ── 선미 동강(용골에서 틀어지고 더 넘어가 있다) ─────────────────────────
            var sternPivot = new GameObject("HullStern");
            sternPivot.transform.SetParent(parts, false);
            sternPivot.transform.localRotation = Quaternion.Euler(0f, plan.sternYawOffset, 0f);
            for (int i = 0; i < plan.sternTiles; i++)
            {
                float z = sternStart - tile * (i + 0.5f);
                PlaceHullTile(sternPivot.transform, wood, metal, ModelHull, new Vector3(0f, 0f, z),
                    0f, plan.sternRoll, s, "HullStern_" + i);
            }

            // ── 갑판 파편 ──────────────────────────────────────────────────────────
            if (plan.hasDeckPanel && modelPrimary[ModelDeck] != null)
            {
                float z = bowStart + bowLength + tile * 0.4f;
                PlaceHullTile(parts, wood, metal, ModelDeck, new Vector3(tile * 0.5f, 0f, z),
                    0f, plan.bowRoll * 0.4f + 18f, s, "DeckPanel");
            }

            // ── 부러진 돛대(모래에 처박혀 있다) ─────────────────────────────────────
            if (modelPrimary[ModelMast] != null)
            {
                var rot = Quaternion.Euler(0f, plan.mastYaw, 0f)
                    * Quaternion.AngleAxis(plan.mastPitch, Vector3.right);
                // 눕힌 기둥은 밑면 모서리가 원점 아래로 내려간다 - 수평 반폭 × sin(각)만큼 들어 올린다.
                float lift = 0.5f * ModelSizes[ModelMast].x * plan.mastScale * s
                    * Mathf.Abs(Mathf.Sin(plan.mastPitch * Mathf.Deg2Rad));
                CreatePart(parts, "BrokenMast", ModelMast, wood, cloth,
                    new Vector3(0f, lift - 0.08f, bowStart + tile * 0.6f), rot, plan.mastScale * s);
            }

            // ── 방향타 / 닻 ───────────────────────────────────────────────────────
            if (plan.hasRudder && modelPrimary[ModelRudder] != null)
            {
                float z = sternStart - sternLength - tile * 0.35f;
                var rot = Quaternion.Euler(0f, plan.sternYawOffset + 180f, 0f)
                    * Quaternion.AngleAxis(74f, Vector3.right);
                float lift = 0.5f * ModelSizes[ModelRudder].z * s * Mathf.Sin(74f * Mathf.Deg2Rad);
                CreatePart(parts, "Rudder", ModelRudder, wood, metal,
                    new Vector3(0f, lift - 0.05f, z), rot, s);
            }

            if (plan.hasAnchor && modelPrimary[ModelAnchor] != null)
            {
                float ax = HashRange(plan.detailSeed, 91u, -3.2f, 3.2f);
                float az = bowStart + bowLength + HashRange(plan.detailSeed, 92u, 0.5f, 3.5f);
                CreatePart(parts, "Anchor", ModelAnchor, wood, metal,
                    new Vector3(ax, -0.04f, az),
                    Quaternion.Euler(0f, HashRange(plan.detailSeed, 93u, 0f, 360f), 0f)
                        * Quaternion.AngleAxis(78f, Vector3.forward), s);
            }

            // ── 흩어진 널판 ───────────────────────────────────────────────────────
            if (modelPrimary[ModelPlanks] != null)
            {
                for (int i = 0; i < plan.plankCount; i++)
                {
                    uint k = (uint)(200 + i * 7);
                    float a = HashRange(plan.detailSeed, k, 0f, Mathf.PI * 2f);
                    float d = HashRange(plan.detailSeed, k + 1u, 2.0f, 6.5f);
                    float lean = HashRange(plan.detailSeed, k + 2u, -14f, 14f);
                    float ps = HashRange(plan.detailSeed, k + 3u, 0.85f, 1.25f) * s;
                    float lift = 0.5f * ModelSizes[ModelPlanks].z * ps
                        * Mathf.Abs(Mathf.Sin(lean * Mathf.Deg2Rad));
                    CreatePart(parts, "WreckPlanks_" + i, ModelPlanks, wood, wood,
                        new Vector3(Mathf.Cos(a) * d, lift - 0.06f, Mathf.Sin(a) * d),
                        Quaternion.Euler(0f, HashRange(plan.detailSeed, k + 4u, 0f, 360f), 0f)
                            * Quaternion.AngleAxis(lean, Vector3.forward), ps);
                }
            }

            // ── 흩어진 화물(궤짝/통) ─────────────────────────────────────────────
            // 화물이 하나도 서지 못했을 때 3번 수거 지점이 배 원점에 겹치지 않게 하는 기본값.
            Vector3 cargoCluster = new Vector3(tile * 0.9f, 0f, 0f);
            for (int i = 0; i < plan.cargoCount; i++)
            {
                uint k = (uint)(400 + i * 9);
                int kind = HashRange(plan.detailSeed, k, 0f, 1f) < 0.55f ? ModelCrate : ModelBarrel;
                if (modelPrimary[kind] == null)
                    kind = kind == ModelCrate ? ModelBarrel : ModelCrate;
                if (modelPrimary[kind] == null)
                    continue; // 두 컨테이너 모두 미로드 - 화물만 빠진다(선체는 이미 서 있다)

                float a = HashRange(plan.detailSeed, k + 1u, 0f, Mathf.PI * 2f);
                float d = HashRange(plan.detailSeed, k + 2u, 1.6f, 5.5f);
                float lean = kind == ModelCrate
                    ? HashRange(plan.detailSeed, k + 3u, 8f, 34f)
                    : HashRange(plan.detailSeed, k + 3u, 66f, 100f);
                float cs = HashRange(plan.detailSeed, k + 4u, 0.9f, 1.2f) * s;

                Vector3 size = ModelSizes[kind] * cs;
                float lift = 0.5f * Mathf.Max(size.x, size.z) * Mathf.Abs(Mathf.Sin(lean * Mathf.Deg2Rad));
                float sink = Mathf.Min(0.12f, (0.5f * size.y + lift) * 0.3f);

                var offset = new Vector3(Mathf.Cos(a) * d, lift - sink, Mathf.Sin(a) * d);
                if (i == 0)
                    cargoCluster = new Vector3(offset.x, 0f, offset.z);

                CreatePart(parts, "WreckCargo_" + i, kind, wood, metal, offset,
                    Quaternion.Euler(0f, HashRange(plan.detailSeed, k + 5u, 0f, 360f), 0f)
                        * Quaternion.AngleAxis(lean, Vector3.forward), cs);
            }

            // ── 수거 지점 2~4곳 ───────────────────────────────────────────────────
            BuildSalvagePoints(parts, plan, bowStart, bowLength, sternStart, sternLength,
                tile, cargoCluster);

            // ── 원거리 컬링 드라이버 ──────────────────────────────────────────────
            var driver = wreck.AddComponent<ShipwreckCullDriver>();
            driver.container = parts;
            driver.center = worldPos;
            driver.phase = index % CullCheckInterval;
        }

        /// <summary>
        /// 갑판 타일 한 장. 좌우로 기울인 판은 아래쪽 모서리가 원점 밑으로 내려가므로
        /// 수평 반폭 × sin(roll)만큼 들어 올린 뒤 모래에 살짝 묻는다(PlaceCargoPile과 같은 식).
        /// </summary>
        private static void PlaceHullTile(Transform parent, Material wood, Material metal,
            int model, Vector3 localPos, float pitch, float roll, float scale, string name)
        {
            Vector3 size = ModelSizes[model] * scale;
            float lift = 0.5f * Mathf.Max(size.x, size.z) * Mathf.Abs(Mathf.Sin(roll * Mathf.Deg2Rad));
            var rot = Quaternion.AngleAxis(roll, Vector3.forward) * Quaternion.AngleAxis(pitch, Vector3.right);
            CreatePart(parent, name, model, wood, metal,
                localPos + new Vector3(0f, lift - 0.10f, 0f), rot, scale);
        }

        /// <summary>
        /// 수거 지점 2~4곳을 붙인다. 지점마다 BoxCollider(InteractionController의 레이가 맞는 면)와
        /// AirlinerSalvagePoint(수거 로직·지급표)를 단다 - 침몰 화물/진주조개와 완전히 같은 경로다
        /// (루트 콜라이더 → GetComponentInParent&lt;AirlinerSalvagePoint&gt;).
        ///
        /// 지급표는 4종 고정이고 순서도 고정이라 rng를 한 번도 쓰지 않는다. 앞의 2곳(선체 화물칸·
        /// 부서진 갑판)은 항상 있고, 3·4번째(흩어진 화물·선장실 사물함)는 salvageCount에 따라 붙는다.
        /// 전리품은 전부 레지스트리에 실재하는 이름이다(금속조각/천조각/노끈/부목/부싯돌/연료/
        /// 엔진부품/강철) - 특히 **엔진부품은 제작 레시피가 없어** 난파선이 사실상 유일한 상시 공급원이다.
        /// </summary>
        private static void BuildSalvagePoints(Transform parts, in WreckPlan plan,
            float bowStart, float bowLength, float sternStart, float sternLength,
            float tile, Vector3 cargoCluster)
        {
            int count = Mathf.Clamp(plan.salvageCount, 2, 4);

            for (int i = 0; i < count; i++)
            {
                Vector3 pos;
                string label;
                AirlinerSalvagePoint.LootEntry[] loot;

                switch (i)
                {
                    case 0:
                        pos = new Vector3(0f, 0f, bowStart + bowLength * 0.5f);
                        label = "선체 화물칸";
                        loot = new[]
                        {
                            new AirlinerSalvagePoint.LootEntry("금속조각", 3),
                            new AirlinerSalvagePoint.LootEntry("천조각", 2),
                        };
                        break;
                    case 1:
                        pos = new Vector3(0f, 0f, sternStart - sternLength * 0.5f);
                        label = "부서진 갑판";
                        loot = new[]
                        {
                            new AirlinerSalvagePoint.LootEntry("부목", 2),
                            new AirlinerSalvagePoint.LootEntry("노끈", 2),
                            new AirlinerSalvagePoint.LootEntry("금속조각", 1),
                        };
                        break;
                    case 2:
                        pos = cargoCluster;
                        label = "흩어진 화물";
                        loot = new[]
                        {
                            new AirlinerSalvagePoint.LootEntry("연료", 2),
                            new AirlinerSalvagePoint.LootEntry("엔진부품", 1),
                        };
                        break;
                    default:
                        pos = new Vector3(tile * 0.45f, 0f, (bowStart + sternStart) * 0.5f);
                        label = "선장실 사물함";
                        loot = new[]
                        {
                            new AirlinerSalvagePoint.LootEntry("부싯돌", 1),
                            new AirlinerSalvagePoint.LootEntry("엔진부품", 1),
                            new AirlinerSalvagePoint.LootEntry("강철", 1),
                        };
                        break;
                }

                // 이름이 "WreckSalvage_"라 지형 판정("Island_" 접두 필터)에는 구조적으로 안 잡힌다.
                var go = new GameObject("WreckSalvage_" + i);
                go.transform.SetParent(parts, false);
                go.transform.localPosition = pos;

                var box = go.AddComponent<BoxCollider>();
                box.center = new Vector3(0f, 0.6f, 0f);
                box.size = new Vector3(1.4f, 1.2f, 1.4f);

                // [한계] 수거 여부는 세이브 미저장 - 침몰 화물/진주조개와 같은 기존 한계다.
                var salvage = go.AddComponent<AirlinerSalvagePoint>();
                salvage.displayName = label;
                salvage.loot = loot;
            }
        }

        /// <summary>
        /// 공유 메시 + 공유 머티리얼로 파츠 하나. 뗏목 모델은 `o` 그룹이 2개라 개별 임포트면
        /// 파츠 2개(각 한 색), 병합 임포트(서브메시 2)면 파츠 1개 + sharedMaterials 두 장이다
        /// (RaftStructure.CreateModelPart가 검증한 분기 그대로). 수중이라 그림자는 캐스팅/수신
        /// 모두 끈다(해저 스커트·생태와 같은 규칙).
        /// </summary>
        private static void CreatePart(Transform parent, string name, int model,
            Material primaryMaterial, Material secondaryMaterial,
            Vector3 localPos, Quaternion localRotation, float scale)
        {
            Mesh primary = modelPrimary[model];
            if (primary == null)
                return; // 이 모델만 아직 안 로드됨 - 조용히 건너뛴다(래치 없음, 다음 월드에서 복구)

            var part = StructureVisualBuilder.CreateMeshPart(parent, name, primary,
                localPos, Vector3.one * scale, localRotation, primaryMaterial);
            DisableShadows(part);

            Mesh secondary = modelSecondary[model];
            if (secondary != null)
            {
                var extra = StructureVisualBuilder.CreateMeshPart(parent, name + "_B", secondary,
                    localPos, Vector3.one * scale, localRotation, secondaryMaterial);
                DisableShadows(extra);
                return;
            }

            if (primary.subMeshCount < 2)
                return;

            var renderer = part.GetComponent<MeshRenderer>();
            if (renderer != null)
                renderer.sharedMaterials = new[] { primaryMaterial, secondaryMaterial };
        }

        /// <summary>수중 파츠의 그림자 캐스팅/수신을 끈다(보이지 않는 그림자에 드로우를 쓰지 않는다).</summary>
        private static void DisableShadows(GameObject go)
        {
            var renderer = go.GetComponent<MeshRenderer>();
            if (renderer == null)
                return;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        // ══════════════════════════════════════════════════════════════════════════
        // 로더 / 유틸
        // ══════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// 모델 8종의 공유 메시를 채운다. ResourceVisualLibrary.TryLoadTwoPartModel(검증된
        /// Load&lt;GameObject&gt;+MeshFilter 로더)을 그대로 쓰고, 프레임당 1회만 프로브하며,
        /// 실패를 영구 래치하지 않는다(SeabedFloraSpawner.EnsureModelsLoaded와 같은 규칙).
        /// 뗏목 모델의 `o` 2개(wood/metal 또는 wood/cloth)는 trunk/leaf 키워드에 안 걸리므로
        /// 로더의 "`o` 등장 순서" 폴백이 wood → metal/cloth 순서를 보장한다(RaftStructure 주석).
        /// </summary>
        private static void EnsureModelsLoaded()
        {
            bool anyMissing = false;
            for (int i = 0; i < modelPrimary.Length && !anyMissing; i++)
                anyMissing = modelPrimary[i] == null;

            if (!anyMissing || probeFrame == Time.frameCount)
                return;
            probeFrame = Time.frameCount;

            for (int i = 0; i < ModelNames.Length; i++)
            {
                if (modelPrimary[i] != null)
                    continue;
                if (ResourceVisualLibrary.TryLoadTwoPartModel("Models/" + ModelNames[i],
                        out Mesh first, out Mesh second))
                {
                    modelPrimary[i] = first;
                    modelSecondary[i] = second; // 병합 임포트면 null - 서브메시 분기가 처리한다
                }
            }
        }

        /// <summary>"Island_{id}_{size}"에서 islandId를 파싱한다(SeabedFloraSpawner.ParseIslandId의
        /// 사본 - 그쪽은 private다. 파싱 실패면 0이고 그래도 worldSeed 격리는 유지된다).</summary>
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
        /// (계획 시드, 키) → [0,1) 결정적 해시. rng draw를 한 번도 소비하지 않고 세부 자세를
        /// 흩는 데 쓴다(SeabedFloraSpawner.PositionHash와 같은 xorshift-곱 finalizer 계열).
        /// </summary>
        private static float Hash01(uint seed, uint key)
        {
            unchecked
            {
                uint h = seed * 2654435761u ^ (key * 2246822519u);
                h ^= h >> 16;
                h *= 0x7FEB352Du;
                h ^= h >> 15;
                h *= 0x846CA68Bu;
                h ^= h >> 16;
                return (h & 0xFFFFFFu) / (float)0x1000000;
            }
        }

        /// <summary>위 해시를 [min, max) 구간으로 편 값.</summary>
        private static float HashRange(uint seed, uint key, float min, float max)
        {
            return min + (max - min) * Hash01(seed, key);
        }

        /// <summary>
        /// 플레이어 Transform 공유 캐시. 못 찾았을 때만, 그것도 60프레임에 한 번, 프레임당 최대
        /// 1회 재탐색한다(MarineLifeSpawner.EnsurePlayer와 같은 저빈도 재시도 규칙 - 정상 경로에서
        /// 탐색 비용·할당 0). 척이 몇 개든 이 가드 하나를 공유한다.
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
            playerTransform = stats.transform;
        }

        /// <summary>
        /// 난파선 한 척의 원거리 컬링 드라이버. 루트("Wreck_{i}")에 붙어 **컨테이너만** 끄고 켠다
        /// (루트가 꺼지면 스스로 다시 켤 수 없다). 15프레임에 한 번, 거리 제곱 비교 1회만 하고
        /// 프레임당 할당이 0이다. 섬이 파괴되면(RegenerateWorld) 드라이버도 함께 사라진다.
        /// </summary>
        private sealed class ShipwreckCullDriver : MonoBehaviour
        {
            public Transform container;
            public Vector3 center;
            public int phase;

            /// <summary>컨테이너의 현재 활성 상태(변할 때만 SetActive를 부른다 - 프레임당 비용 0).</summary>
            private bool active = true;

            private void LateUpdate()
            {
                if (container == null)
                    return;
                if ((Time.frameCount + phase) % CullCheckInterval != 0)
                    return;

                EnsurePlayer();

                Vector3 viewer = playerTransform != null
                    ? playerTransform.position
                    : (Camera.main != null ? Camera.main.transform.position : center);

                float dx = viewer.x - center.x;
                float dz = viewer.z - center.z;
                bool nearby = dx * dx + dz * dz <= ActiveDistance * ActiveDistance;
                if (nearby != active)
                {
                    active = nearby;
                    container.gameObject.SetActive(nearby);
                }
            }
        }
    }
}
