using UnityEngine;
using UnityEngine.SceneManagement;
using MakeGame.Data;

namespace MakeGame.Systems
{
    /// <summary>
    /// 보스 3종을 월드에 **각각 한 마리씩만** 배치하는 스포너. 개체의 체력·AI·트로피는 BossCreature가,
    /// "어디에 · 몇 마리 · 다시 나오는가"만 이 클래스가 정한다.
    ///
    /// ── 왜 씬 배선이 아니라 자가 부트스트랩인가 ──────────────────────────────────
    /// 기존 스포너(SharkSpawner 등)는 씬의 Managers 오브젝트에 붙어 있고 WorldMapManager가 직접
    /// 호출한다. 그런데 씬 파일(SampleScene.unity)과 WorldMapManager.cs는 둘 다 이 작업의 락 밖이라
    /// 새 스포너를 그 경로로 끼워 넣을 수단이 없다. 그래서 SurvivalHudUI/CombatFeedbackUI가 이미 쓰는
    /// **자가 생성 패턴**(SubsystemRegistration + sceneLoaded)으로 스스로 붙고, 월드가 준비됐는지
    /// 0.5초마다 폴링해서 한 번만 배치한다. 씬을 한 글자도 고치지 않아도 동작한다.
    ///
    /// ── 배치 규칙(결정적) ────────────────────────────────────────────────────────
    ///  · a 거대 상어  - 시작 섬에서 900~1650m 떨어진 **외해**(모든 섬에서 550m 이상). 수심 8m.
    ///  · b 대왕 곰치  - 실제로 생성된 **수중 동굴**("UnderwaterCave_0")의 바깥 22m 지점, 동굴과 같은 수심대.
    ///  · c 심해 괴수  - 가장 큰 섬 해저 스커트를 훑어 찾은 **가장 깊은 지점**(스커트 최심 -18m 부근).
    /// 난수는 보스 전용 격리 스트림(<see cref="BossSeedSalt"/>) 하나뿐이고 그마저 각도 오프셋 2개만
    /// 뽑는다. 나머지는 전부 섬 위치·해저 높이를 읽는 **결정적 스캔**이라 월드 생성 스트림에 영향이 없다.
    ///
    /// ── 재등장 없음 ─────────────────────────────────────────────────────────────
    /// 처치된 보스는 다시 만들지 않는다(BossCreature의 static 진행도 = SaveData의 보스 필드).
    /// 잡았지만 아직 안 주운 트로피는 불러오기 때문에 **그 보스가 지키던 자리**에 다시 놓는다
    /// (죽은 좌표는 저장하지 않는다 - 세이브 필드를 늘리는 값어치가 없고, 지키던 자리는 결정적이다).
    ///
    /// ── 성능 ────────────────────────────────────────────────────────────────────
    /// 폴링은 0.5초에 한 번, 배치는 월드당 1회다. 개체의 원거리 컬링(300m)은 BossCreature가 한다.
    /// </summary>
    public class BossSpawner : MonoBehaviour
    {
        /// <summary>보스 배치 전용 격리 난수 salt. 기존 salt(상어 -1000000 / 섬 레이아웃 -2000000 /
        /// 초목 +3000000대 / 섬별 0x… 대역)와 겹치지 않는 값이다.</summary>
        private const int BossSeedSalt = -3000000;

        /// <summary>보스와 트로피를 담는 컨테이너 이름. 스모크 테스트가 이 접두로 존재를 확인한다.</summary>
        public const string BossRootName = "BossRoot";

        /// <summary>월드 준비 상태를 확인하는 주기(초). 배치는 월드당 1회뿐이라 이 간격이면 충분하다.</summary>
        private const float PollInterval = 0.5f;

        /// <summary>상어 후보 반지름(시작 섬 기준, m). 가까운 것부터 시도한다.</summary>
        private static readonly float[] SharkRingRadii = { 900f, 1150f, 1400f, 1650f };

        /// <summary>상어 후보가 모든 섬에서 떨어져 있어야 하는 최소 거리(m). 특대 섬 스커트 바깥(290m)보다 넉넉하다.</summary>
        private const float SharkMinIslandDistance = 550f;

        /// <summary>상어를 놓을 수심(m).</summary>
        private const float SharkDepth = 8f;

        /// <summary>곰치를 동굴에서 바깥쪽으로 밀어 놓는 거리(m). 동굴 입구를 막지 않으면서 지키는 거리다.</summary>
        private const float MorayCaveOffset = 22f;

        /// <summary>괴수를 해저에서 띄우는 높이(m).</summary>
        private const float HorrorSeabedLift = 4f;

        /// <summary>해저 최심점 스캔: 각도 분할 수와 반지름 진행 폭(m)·최대 진행 거리(m).</summary>
        private const int DeepScanAngles = 16;
        private const float DeepScanStep = 8f;
        private const float DeepScanMaxOutward = 120f;

        private WorldMapManager manager;
        private Transform bossRoot;
        private readonly bool[] resolved = new bool[BossCreature.KindCount];
        private float pollTimer;

        /// <summary>
        /// 씬이 로드될 때마다 스포너를 하나 만든다(SurvivalHudUI.Bootstrap과 같은 패턴).
        /// WorldMapManager가 없는 씬(타이틀 등)에서는 폴링만 하고 아무 것도 만들지 않는다.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Bootstrap()
        {
            SceneManager.sceneLoaded += (scene, mode) =>
            {
                var go = new GameObject("BossSpawner");
                go.AddComponent<BossSpawner>();
            };
        }

        /// <summary>
        /// 월드가 준비됐는지, 그리고 이미 배치한 월드가 아직 살아 있는지 0.5초마다 확인한다.
        /// RegenerateWorld(F9 불러오기·시드 변경)는 WorldMapManager의 자식을 전부 파괴하므로
        /// 컨테이너(bossRoot)가 null이 되는 것으로 "새 월드"를 알 수 있다 - 별도 신호가 필요 없다.
        /// </summary>
        private void Update()
        {
            pollTimer -= Time.unscaledDeltaTime;
            if (pollTimer > 0f)
                return;
            pollTimer = PollInterval;

            if (manager == null)
            {
                manager = FindAnyObjectByType<WorldMapManager>();
                if (manager == null)
                    return;
            }

            if (manager.islands == null || manager.islands.Count == 0)
                return;   // 아직 월드 생성 전(또는 재생성 중) - 다음 폴링에서 다시 본다

            if (bossRoot == null)
            {
                // 새 월드다. 지난 월드의 배치 기록을 버리고 컨테이너부터 다시 만든다.
                for (int i = 0; i < resolved.Length; i++)
                    resolved[i] = false;

                var root = new GameObject(BossRootName);
                root.transform.SetParent(manager.transform, false);
                bossRoot = root.transform;
            }

            PlacePending();
        }

        /// <summary>
        /// 아직 이 월드에 놓지 않은 보스/트로피를 놓는다. 종류당 **정확히 한 번만** 놓는다
        /// (resolved 플래그) - 그래야 배치 뒤에 플레이어가 잡아서 오브젝트가 사라져도 다시 솟지 않는다.
        /// 모델 로드가 아직이면 그 종류만 미해결로 남기고 다음 폴링에서 재시도한다.
        /// </summary>
        private void PlacePending()
        {
            System.Random rng = SeededRandomExtensions.CreateForSalt(manager.worldSeed, BossSeedSalt);

            // draw 전량 소비 원칙: 배치 성공 여부와 무관하게 항상 같은 순서로 두 개만 뽑는다.
            float sharkAngle = rng.NextFloat(0f, Mathf.PI * 2f);
            float deepAngle = rng.NextFloat(0f, Mathf.PI * 2f);

            for (int kind = 0; kind < BossCreature.KindCount; kind++)
            {
                if (resolved[kind])
                    continue;

                if (BossCreature.IsDefeated(kind) && BossCreature.HasTrophy(kind))
                {
                    resolved[kind] = true;   // 잡았고 전리품도 챙겼다 - 이 월드에 아무 것도 놓지 않는다
                    continue;
                }

                if (!TryGetHome(kind, sharkAngle, deepAngle, out Vector3 home))
                {
                    resolved[kind] = true;   // 자리를 못 찾았다(섬이 아주 적은 예외 월드) - 조용히 생략
                    Debug.LogWarning("[BossSpawner] " + BossCreature.GetDisplayName(kind)
                        + "의 배치 자리를 찾지 못해 이 월드에서는 생략한다.");
                    continue;
                }

                if (BossCreature.IsDefeated(kind))
                {
                    // 이미 잡은 보스: 아직 안 주운 전리품만 그 자리에 되돌려 놓는다.
                    if (BossCreature.SpawnTrophy(kind, home, bossRoot, manager.seaLevel) != null)
                        resolved[kind] = true;
                    continue;
                }

                float yaw = (kind * 120f) + Mathf.Rad2Deg * sharkAngle;
                if (BossCreature.Spawn(kind, home, yaw, bossRoot, manager.seaLevel) != null)
                {
                    resolved[kind] = true;
                    Debug.Log("[BossSpawner] " + BossCreature.GetDisplayName(kind) + " 배치: "
                        + home.ToString("F0"));
                }
            }
        }

        /// <summary>종류별 배치 자리를 계산한다(결정적). 못 찾으면 false.</summary>
        private bool TryGetHome(int kind, float sharkAngle, float deepAngle, out Vector3 home)
        {
            switch (kind)
            {
                case (int)BossKind.GiantShark: return TryGetOpenOceanHome(sharkAngle, out home);
                case (int)BossKind.GiantMoray: return TryGetCaveHome(out home);
                default: return TryGetDeepestHome(deepAngle, out home);
            }
        }

        /// <summary>
        /// [a 거대 상어] 시작 섬에서 방사 방향으로 나가며 **모든 섬에서 550m 이상** 떨어진 첫 후보를 쓴다.
        /// 시작 섬을 기준으로 잡는 이유는 "찾아갈 수 있어야 한다"이고(뗏목으로 나가는 방향이 정해진다),
        /// 그럼에도 섬 스커트(특대 290m)의 두 배 밖이라 육지에서 보이지 않는 진짜 외해다.
        /// </summary>
        private bool TryGetOpenOceanHome(float baseAngle, out Vector3 home)
        {
            Vector3 start = GetStartingIslandCenter();
            float limit = manager.oceanSize * 0.45f;
            Vector3 fallback = Vector3.zero;
            bool hasFallback = false;

            for (int r = 0; r < SharkRingRadii.Length; r++)
            {
                for (int step = 0; step < 12; step++)
                {
                    float angle = baseAngle + step * (Mathf.PI * 2f / 12f);
                    var candidate = new Vector3(
                        start.x + Mathf.Cos(angle) * SharkRingRadii[r],
                        manager.seaLevel - SharkDepth,
                        start.z + Mathf.Sin(angle) * SharkRingRadii[r]);

                    if (Mathf.Abs(candidate.x) > limit || Mathf.Abs(candidate.z) > limit)
                        continue;   // 바다 평면 밖으로 나가지 않는다

                    if (!hasFallback)
                    {
                        fallback = candidate;
                        hasFallback = true;
                    }

                    if (MinIslandDistance(candidate) >= SharkMinIslandDistance)
                    {
                        home = candidate;
                        return true;
                    }
                }
            }

            home = fallback;
            return hasFallback;
        }

        /// <summary>
        /// [b 대왕 곰치] 실제로 생성된 수중 동굴을 찾아 그 **바깥쪽 22m**를 지키게 한다.
        /// 동굴 루트는 섬 루트의 직속 자식이고 이름이 "UnderwaterCave_0"으로 고정이라
        /// (UnderwaterCaveSpawner.CaveRootPrefix + 인덱스) Find 한 번으로 정확히 잡힌다.
        /// 동굴이 하나도 없는 월드(대형·특대 섬이 없거나 동굴 모델 로드가 늦어 그 섬만 건너뛴 경우)에는
        /// **가장 큰 섬의 스커트 얕은 쪽**으로 떨어진다 - 보스가 통째로 사라지는 것보다 자리가 조금
        /// 어긋나는 편이 낫다(동굴은 원래 대형·특대 섬 스커트에만 생기므로 같은 수심대다).
        /// </summary>
        private bool TryGetCaveHome(out Vector3 home)
        {
            home = Vector3.zero;

            for (int i = 0; i < manager.islands.Count; i++)
            {
                IslandInstance island = manager.islands[i];
                if (island == null || island.placeholderObject == null)
                    continue;

                Transform cave = island.placeholderObject.transform.Find("UnderwaterCave_0");
                if (cave == null)
                    continue;

                Vector3 cavePosition = cave.position;
                Vector3 outward = cavePosition - island.mapPosition;
                outward.y = 0f;
                outward = outward.sqrMagnitude > 0.0001f ? outward.normalized : Vector3.right;

                Vector3 candidate = cavePosition + outward * MorayCaveOffset;
                candidate.y = Mathf.Min(cavePosition.y + 3f, manager.seaLevel - 4f);

                // 해저를 뚫지 않게 올린다(스커트 샘플이 있으면 그 높이 기준).
                if (SeabedGenerator.TrySampleSeabed(candidate, out float seabedY))
                    candidate.y = Mathf.Clamp(candidate.y, seabedY + 2f, manager.seaLevel - 4f);

                morayIslandId = island.islandId;
                home = candidate;
                return true;
            }

            // 폴백: 동굴이 없다. 가장 큰 섬의 스커트 안쪽(반지름 + 25m)에 세운다.
            IslandInstance fallbackIsland = PickDeepIsland();
            if (fallbackIsland == null)
                return false;

            float radius = IslandSizeMetrics.GetTerrainRadius(fallbackIsland.size) + 25f;
            var fallback = new Vector3(
                fallbackIsland.mapPosition.x + radius,
                manager.seaLevel - 12f,
                fallbackIsland.mapPosition.z);

            if (SeabedGenerator.TrySampleSeabed(fallback, out float fallbackSeabedY))
                fallback.y = Mathf.Clamp(fallback.y, fallbackSeabedY + 2f, manager.seaLevel - 4f);

            morayIslandId = fallbackIsland.islandId;
            home = fallback;
            return true;
        }

        /// <summary>곰치를 배치한 섬 번호. 괴수가 같은 섬을 고르지 않게 하는 데만 쓴다(-1 = 없음).</summary>
        private int morayIslandId = -1;

        /// <summary>
        /// [c 심해 괴수] 가장 큰 섬(가능하면 곰치가 쓰지 않은 섬)의 해저 스커트를 각도·반지름으로 훑어
        /// **가장 깊은 지점**을 찾는다. 스커트는 바깥으로 갈수록 -18m까지 내려가므로(SeabedGenerator의
        /// OuterDepth) 여기가 이 게임에서 실제로 도달 가능한 가장 깊은 물이다.
        /// 스커트 폭 공식은 SeabedGenerator의 private이라 옮겨 적지 않는다 - 바깥으로 나가며 샘플하다가
        /// TrySampleSeabed가 false를 주는 지점이 곧 스커트 끝이다(공개 API만으로 자기 교정된다).
        /// </summary>
        private bool TryGetDeepestHome(float baseAngle, out Vector3 home)
        {
            home = Vector3.zero;

            IslandInstance target = PickDeepIsland();
            if (target == null)
                return false;

            float radius = IslandSizeMetrics.GetTerrainRadius(target.size);
            Vector3 center = target.mapPosition;

            float bestY = float.MaxValue;
            Vector3 bestPoint = Vector3.zero;
            bool found = false;

            for (int a = 0; a < DeepScanAngles; a++)
            {
                float angle = baseAngle + a * (Mathf.PI * 2f / DeepScanAngles);
                float cos = Mathf.Cos(angle);
                float sin = Mathf.Sin(angle);

                for (float outward = DeepScanStep; outward <= DeepScanMaxOutward; outward += DeepScanStep)
                {
                    float r = radius + outward;
                    var probe = new Vector3(center.x + cos * r, 0f, center.z + sin * r);
                    if (!SeabedGenerator.TrySampleSeabed(probe, out float seabedY))
                        break;   // 스커트 바깥으로 나갔다 - 이 각도는 여기까지다

                    if (seabedY < bestY)
                    {
                        bestY = seabedY;
                        bestPoint = probe;
                        found = true;
                    }
                }
            }

            if (!found)
                return false;

            home = new Vector3(bestPoint.x, Mathf.Min(bestY + HorrorSeabedLift, manager.seaLevel - 4f), bestPoint.z);
            return true;
        }

        /// <summary>
        /// 괴수를 놓을 섬을 고른다. 우선순위는 (1) 곰치가 쓰지 않은 섬, (2) 지형 반지름이 가장 큰 섬,
        /// (3) 같은 크기면 islandId가 작은 섬이다 - 전부 결정적이라 같은 시드면 항상 같은 섬이 나온다.
        /// </summary>
        private IslandInstance PickDeepIsland()
        {
            IslandInstance best = null;
            float bestRadius = -1f;
            bool bestIsFree = false;

            for (int i = 0; i < manager.islands.Count; i++)
            {
                IslandInstance island = manager.islands[i];
                if (island == null || island.isStartingIsland)
                    continue;

                float radius = IslandSizeMetrics.GetTerrainRadius(island.size);
                bool free = island.islandId != morayIslandId;

                // 곰치가 쓰지 않은 섬을 항상 우선한다(같은 섬에 보스 둘이 붙어 있으면 영역이 겹친다).
                if (best == null || (free && !bestIsFree) || (free == bestIsFree && radius > bestRadius))
                {
                    best = island;
                    bestRadius = radius;
                    bestIsFree = free;
                }
            }

            return best;
        }

        /// <summary>시작 섬의 중심(없으면 원점). 상어 배치의 기준점이다.</summary>
        private Vector3 GetStartingIslandCenter()
        {
            for (int i = 0; i < manager.islands.Count; i++)
            {
                IslandInstance island = manager.islands[i];
                if (island != null && island.isStartingIsland)
                    return island.mapPosition;
            }

            return Vector3.zero;
        }

        /// <summary>후보 지점에서 가장 가까운 섬까지의 수평 거리(m). 섬이 없으면 무한대로 본다.</summary>
        private float MinIslandDistance(Vector3 candidate)
        {
            float best = float.MaxValue;
            for (int i = 0; i < manager.islands.Count; i++)
            {
                IslandInstance island = manager.islands[i];
                if (island == null)
                    continue;

                float dx = candidate.x - island.mapPosition.x;
                float dz = candidate.z - island.mapPosition.z;
                float distance = Mathf.Sqrt(dx * dx + dz * dz);
                if (distance < best)
                    best = distance;
            }

            return best;
        }
    }
}
