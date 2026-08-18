using System.Collections.Generic;
using UnityEngine;

namespace MakeGame.Systems
{
    /// <summary>
    /// 섬별 해저 스커트(seabed skirt) 생성기.
    ///
    /// 바다가 반투명(MGOcean v3, 깊이 흡수·수중 안개)이 되면서 물밑이 보이는데, 섬 지형 메시는
    /// 가장자리(반지름 R)에서 그냥 끝나 그 밖은 "바닥 없는 허공"이었다. 섬 메시 테두리에서 시작해
    /// 바깥으로 코사인 감쇠로 내려가 외곽에서 수심 약 -18m에 닿는 환형(annulus) 모래 바닥을 깔아,
    /// 다음 웨이브의 산호/해초/수중 바위 분포가 설 지면을 만든다.
    ///
    /// ── 안쪽 경계를 "해안선(radialMask)"이 아니라 "메시 테두리(r=R)"에 두는 이유 ──
    /// 섬 지형 메시는 마스크(각도별 해안 반경, 최소 0.14R)와 무관하게 **항상 반지름 R의 원판 전체**를
    /// 차지하고(IslandMeshGenerator.GenerateIslandMesh - XZ는 프로파일과 무관), 해안선 밖 q>1 구간도
    /// 이미 해수면 아래로 잠긴 지형+콜라이더다(SculptHeight (12) 해안 잠수: ShoreSubmergeDepth 1.8m +
    /// ShelfDrop 최대 9m). 스커트를 마스크 경계에서 시작하면 그 구간에서 섬 콜라이더와 이중 지면이
    /// 되므로, 스커트는 섬 메시가 끝나는 r=R **바깥에서만** 시작한다. 그래도 안쪽 경계는 섬 모양과
    /// 맞물린다 - 스커트 안쪽 링의 높이를 **섬 메시의 실제 최외곽 링 정점**(radialMask·프로파일·섬별
    /// 시드가 전부 반영된 값)에서 각도별로 읽어 오기 때문이다. 마스크가 좁은 방위는 테두리가 깊고
    /// (q가 커서 ShelfDrop이 큼) 넓은 방위는 얕아, 스커트의 시작 수심이 섬 윤곽을 그대로 따라간다.
    /// (마스크 원본 배열을 다시 받는 대신 "마스크가 이미 구워진 메시"를 호출부에서 넘겨받는 경로다 -
    /// 지형 수식을 복제하지 않으므로 수식이 바뀌어도 자동으로 따라간다. BuildCapLayer와 같은 전략.)
    ///
    /// ── 기존 배치(자원/잔해/위험요소/초목)가 영향받지 않는 근거 ──
    /// (1) 이름 필터: 뭍 배치는 전부 TerrainSampler.SnapToGround를 거치고, 그 판정은
    ///     "콜라이더 이름이 'Island_'로 시작"일 때만 지형으로 채택한다(TerrainSampler.cs:29,72-75).
    ///     스커트 오브젝트는 "Seabed_" 접두사라 레이가 **뚫고 지나간다**. BuildingSystem.CastBuildRay·
    ///     지면 프로브, WeatherSystem, BearAI도 같은 필터 계열이다(BuildIslandSurface 상단 주석의 전수 목록).
    /// (2) 기하 자체로도 안전: 스커트는 전부 r ≥ R(섬 메시 밖)이고 전 정점이 해수면 아래인데,
    ///     자원/위험요소/사냥감/초목의 산포 반경은 0.8R 이내다(IslandResourceSpawner 등) - 뭍 배치
    ///     레이가 스커트 위 상공을 지나는 경우 자체가 없다.
    /// (3) rng 불변: 이 파일은 System.Random·UnityEngine.Random을 만들지도 소비하지도 않는다.
    ///     표면 기복은 (worldX, worldZ)만 입력으로 받는 순수 격자 해시 노이즈다
    ///     (IslandMeshGenerator.ComputeNoiseSeed / Hash01과 같은 finalizer 계열). 호출 지점
    ///     (BuildGroundCaps)도 rng를 쥐고 있지 않으므로 어떤 추첨 순서도 한 칸도 밀리지 않는다.
    ///
    /// ── 다음 웨이브(산호/해초/수중 바위 분포기) 인터페이스 ──
    ///   · TrySampleSeabed(worldPos, out seabedY): 해당 XZ가 어느 섬의 스커트 범위면 그 지점의
    ///     해저 높이(월드 y)를 돌려준다. 레이캐스트 없이 생성식과 동일한 수식으로 계산하므로
    ///     프레임 어디서 불러도 안전하고 결정적이다.
    ///   · 스커트 GameObject 이름은 항상 "Seabed_" 접두사(SeabedNamePrefix) - 레이캐스트로 찾는
    ///     분포기는 이 접두사로 식별하면 된다.
    /// </summary>
    public static class SeabedGenerator
    {
        /// <summary>스커트 오브젝트 이름 접두사. "Island_"로 시작하지 않는 것이 TerrainSampler 안전의 전제다.</summary>
        public const string SeabedNamePrefix = "Seabed_";

        /// <summary>각도 분할 수. 요구 범위 48~64 안. 56 × (7+1)링 = 정점 448 ≤ 섬당 상한 500.</summary>
        private const int AngularSegments = 56;

        /// <summary>반경 방향 링 수(요구 범위 6~8). 링 경계는 rings+1개다.</summary>
        private const int RadialRings = 7;

        /// <summary>스커트 외곽에서 도달하는 목표 수심(m, 해수면 기준).</summary>
        private const float OuterDepth = -18f;

        /// <summary>모래 언덕 기복 진폭(m). 합성 노이즈가 [-1,1]이라 최종 기복은 ±이 값이다.</summary>
        private const float DuneAmplitude = 0.6f;

        /// <summary>스커트 폭 = clamp(R × 0.6, 30, 90). 작은 섬도 최소 30m의 산호 분포 면적을 확보한다.</summary>
        private static float SkirtWidth(float radius) => Mathf.Clamp(radius * 0.6f, 30f, 90f);

        // ── TrySampleSeabed용 레지스트리 ─────────────────────────────────────────────
        // 월드 재생성으로 섬이 파괴되면 transform이 null(유니티 == 오버로드)이 되므로 조회 시 걸러낸다.
        private sealed class SkirtRecord
        {
            public Transform transform;   // 파괴 감지용
            public Vector3 center;        // 섬 중심(월드). 스커트 로컬 원점과 같다.
            public float innerRadius;     // = 섬 메시 반지름 R
            public float outerRadius;     // = R + SkirtWidth
            public float[] rimHeights;    // 섬 메시 최외곽 링의 y(각도순, 0번 = +X축, 반시계 등간격)
        }

        private static readonly List<SkirtRecord> registry = new List<SkirtRecord>();

        // 정점 수 보고(총합 1줄)용 누적기. 50개 섬 생성이 전부 같은 프레임 안의 동기 흐름이므로,
        // 첫 스커트에 붙인 로거 컴포넌트의 Start()(= 그 프레임의 생성 루프가 끝난 뒤)가 총합을 찍는다.
        private static int pendingVertexTotal;
        private static int pendingSkirtCount;
        private static bool loggerScheduled;

        /// <summary>
        /// 도메인 리로드를 끈 플레이 모드에서 static 레지스트리/누적기가 이전 실행의 값을 들고
        /// 시작하지 않게 초기 상태로 되돌린다.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStaticCache()
        {
            registry.Clear();
            pendingVertexTotal = 0;
            pendingSkirtCount = 0;
            loggerScheduled = false;
        }

        /// <summary>
        /// 섬 하나의 해저 스커트를 생성한다. 섬 메시 생성 직후 같은 동기 흐름에서 호출된다
        /// (IslandMeshGenerator.BuildGroundCaps → 여기. 코루틴/지연 없음).
        /// </summary>
        /// <param name="islandObject">섬 지형 오브젝트("Island_{id}_{size}"). 스커트는 이 자식으로 붙는다.</param>
        /// <param name="islandMesh">그 섬의 지형 메시(최외곽 링 높이를 읽는 소스).</param>
        /// <param name="radius">섬 지형 반지름 R(m). IslandSizeMetrics.GetTerrainRadius 값 = 메시 XZ 반경.</param>
        public static void Build(GameObject islandObject, Mesh islandMesh, float radius)
        {
            if (islandObject == null || islandMesh == null || radius <= 0f)
                return;

            // 같은 섬에 두 번 불려도(방어) 스커트가 겹으로 깔리지 않게 한다.
            string name = SeabedNamePrefix + islandObject.name;
            if (islandObject.transform.Find(name) != null)
                return;

            float[] rimHeights = ExtractRimHeights(islandMesh, radius);
            if (rimHeights == null)
                return; // 절차적 지형이 아닌 구성(placeholder 프리팹 등)이면 조용히 건너뛴다.

            var record = new SkirtRecord
            {
                center = islandObject.transform.position,
                innerRadius = radius,
                outerRadius = radius + SkirtWidth(radius),
                rimHeights = rimHeights,
            };

            // ── 환형 메시 생성 ──────────────────────────────────────────────────────
            int vertexCount = (RadialRings + 1) * AngularSegments;
            var vertices = new Vector3[vertexCount];
            var uvs = new Vector2[vertexCount];

            // 모래 결 밀도를 지면 캡과 맞춘다: 캡은 UV 정규화(/2R) × 타일 R×1.5 → 한 결 주기 약 1.33m.
            // 스커트는 로컬 미터 좌표에 직접 0.75/m를 곱어 같은 주기를 얻는다(머티리얼은 공유 캐시라
            // 타일링 프로퍼티를 건드리면 안 되므로 UV 쪽에서 맞춘다).
            const float uvScale = 0.75f;

            for (int ring = 0; ring <= RadialRings; ring++)
            {
                float s = (float)ring / RadialRings; // 0(섬 테두리) ~ 1(외곽)
                float r = Mathf.Lerp(record.innerRadius, record.outerRadius, s);
                for (int seg = 0; seg < AngularSegments; seg++)
                {
                    // 각도 규약은 섬 메시와 동일(0번 = +X축, 반시계). x=cos, z=sin.
                    float angle = (float)seg / AngularSegments * Mathf.PI * 2f;
                    float x = Mathf.Cos(angle) * r;
                    float z = Mathf.Sin(angle) * r;
                    float y = ComputeLocalHeight(x, z, record.center.x + x, record.center.z + z, record);

                    int i = ring * AngularSegments + seg;
                    vertices[i] = new Vector3(x, y, z);
                    uvs[i] = new Vector2(x * uvScale, z * uvScale);
                }
            }

            var triangles = new int[RadialRings * AngularSegments * 6];
            int t = 0;
            for (int ring = 0; ring < RadialRings; ring++)
            {
                int ringStart = ring * AngularSegments;
                int nextRingStart = (ring + 1) * AngularSegments;
                for (int seg = 0; seg < AngularSegments; seg++)
                {
                    int a = ringStart + seg;
                    int b = ringStart + (seg + 1) % AngularSegments;
                    int c = nextRingStart + seg;
                    int d = nextRingStart + (seg + 1) % AngularSegments;

                    // ★ 감김 주의 ★ IslandMeshGenerator의 링-링 루프(a,b,d)(a,d,c)와 완전히 같은 순서다.
                    // 이 저장소는 감김을 뒤집어 위에서 본 지형에 구멍이 뚫린 사고 전력이 있다
                    // (IslandMeshGenerator.cs:742-752 주석). 반대로 감으면 스커트가 아래를 보고 컬링된다.
                    triangles[t++] = a;
                    triangles[t++] = b;
                    triangles[t++] = d;

                    triangles[t++] = a;
                    triangles[t++] = d;
                    triangles[t++] = c;
                }
            }

            var mesh = new Mesh();
            mesh.name = "SeabedSkirt";
            mesh.vertices = vertices;
            mesh.uv = uvs;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            // ── 오브젝트 조립 ──────────────────────────────────────────────────────
            var go = new GameObject(name);
            go.transform.SetParent(islandObject.transform, false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;

            go.AddComponent<MeshFilter>().sharedMesh = mesh;

            var renderer = go.AddComponent<MeshRenderer>();
            // 섬 모래 재질과 같은 캐시 경로(ResourceVisualLibrary.GetMaterial - 월드 전체 (색+텍스처)당
            // 1장, enableInstancing). 색은 IslandSand 계열을 어둡고(×0.66) 채도를 낮춘(회색 35% 혼합)
            // "수중 모래". 50개 섬이 전부 같은 색이므로 머티리얼은 월드 전체에 정확히 1장이고,
            // 서브메시도 1개라 스커트는 섬당 1드로우콜이다.
            renderer.sharedMaterial = ResourceVisualLibrary.GetMaterial(UnderwaterSandColor(), "sand");
            // 수심 2~18m 바닥이 물 위로 그림자를 쏘면 안 되고(요구: 캐스팅 off), 받는 쪽도 끈다 -
            // 수중 안개·깊이 흡수에 묻혀 보이지 않는 그림자에 드로우를 쓰지 않는다.
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            // 정적 콜라이더(Rigidbody 없는 MeshCollider = PhysX static, convex 기본값 false = non-convex).
            // 플레이어가 잠수해 바닥에 설 수 있다. 이름이 "Seabed_"라 SnapToGround류 지형 판정에서는
            // 구조적으로 제외된다(클래스 상단 근거 (1)).
            go.AddComponent<MeshCollider>().sharedMesh = mesh;

            record.transform = go.transform;
            registry.Add(record);

            // [해저 생태] 스커트 레코드 등록 직후 같은 동기 흐름에서 산호/해초/수중 바위를 깐다
            // (등록이 먼저여야 TrySampleSeabed 접지가 유효). 순수 배경·독립 rng - SeabedFloraSpawner 주석.
            SeabedFloraSpawner.Spawn(islandObject, radius);

            // 정점 수 총합 로그 예약(프레임당 1줄). 로거의 Start는 이 프레임의 모든 섬 생성이 끝난 뒤 돈다.
            pendingVertexTotal += vertexCount;
            pendingSkirtCount++;
            if (!loggerScheduled)
            {
                loggerScheduled = true;
                go.AddComponent<SeabedBatchLogger>();
            }
        }

        /// <summary>
        /// 산호/해초 분포기용 공개 샘플러. worldPos의 XZ가 어느 섬의 스커트 환형 범위 안이면
        /// 그 지점의 해저 높이(월드 y)를 계산해 돌려준다(생성 수식과 동일 - 레이캐스트 없음).
        /// 섬 메시 본체 위(r &lt; R)는 스커트 담당이 아니므로 false다(그쪽은 지형 레이캐스트를 쓰면 된다).
        /// 스커트끼리 겹치는 지점(이웃 섬)은 먼저 등록된 쪽 하나를 돌려준다.
        /// </summary>
        public static bool TrySampleSeabed(Vector3 worldPos, out float seabedY)
        {
            for (int i = registry.Count - 1; i >= 0; i--)
            {
                SkirtRecord record = registry[i];
                if (record.transform == null)
                {
                    registry.RemoveAt(i); // 월드 재생성으로 파괴된 스커트는 등록 해제
                    continue;
                }

                float dx = worldPos.x - record.center.x;
                float dz = worldPos.z - record.center.z;
                float sq = dx * dx + dz * dz;
                if (sq < record.innerRadius * record.innerRadius
                    || sq > record.outerRadius * record.outerRadius)
                    continue;

                seabedY = record.center.y
                    + ComputeLocalHeight(dx, dz, worldPos.x, worldPos.z, record);
                return true;
            }

            seabedY = 0f;
            return false;
        }

        // ── 높이장 (생성과 샘플링이 같은 함수를 쓴다 - 어긋날 수 없다) ───────────────────

        /// <summary>
        /// 스커트 로컬 XZ의 해저 높이(섬 로컬 y). 안쪽 링(s=0)은 섬 테두리 높이 그대로, 바깥으로
        /// 갈수록 코사인 이징으로 OuterDepth까지 내려가고, 그 위에 해시 노이즈 기복(±0.6m)이 얹힌다.
        /// 기복은 이음매(s=0)에서 0으로 페이드해 섬 메시와의 접합이 벌어지지 않는다.
        /// </summary>
        private static float ComputeLocalHeight(float localX, float localZ, float worldX, float worldZ,
            SkirtRecord record)
        {
            float r = Mathf.Sqrt(localX * localX + localZ * localZ);
            float s = Mathf.Clamp01((r - record.innerRadius)
                / Mathf.Max(1e-4f, record.outerRadius - record.innerRadius));

            float rimY = SampleRim(record.rimHeights, Mathf.Atan2(localZ, localX));
            float ease = 0.5f * (1f - Mathf.Cos(s * Mathf.PI)); // 코사인 감쇠(양끝 기울기 0)
            float baseY = Mathf.Lerp(rimY, OuterDepth, ease);

            // 기복은 월드 좌표 기반이라 섬끼리 스커트가 겹쳐도 무늬가 연속이고, TrySampleSeabed가
            // 같은 입력으로 정확히 재현한다.
            return baseY + DuneNoise(worldX, worldZ) * Mathf.Min(1f, s * 4f);
        }

        /// <summary>
        /// 섬 메시 최외곽 링(XZ 반경이 정확히 R인 정점들)의 y를 각도순으로 뽑는다.
        /// 최외곽 링과 그 안쪽 링의 반경 차는 R/ringCount ≥ 5m라 문턱 R-1m로 오분류가 없다.
        /// </summary>
        private static float[] ExtractRimHeights(Mesh islandMesh, float radius)
        {
            Vector3[] verts = islandMesh.vertices;
            float threshold = radius - 1f;
            var rim = new List<Vector3>(96); // (angle, y) 쌍을 x=angle, y=y로 담는다
            for (int i = 0; i < verts.Length; i++)
            {
                Vector3 v = verts[i];
                float mag = Mathf.Sqrt(v.x * v.x + v.z * v.z);
                if (mag < threshold)
                    continue;
                float angle = Mathf.Atan2(v.z, v.x);
                if (angle < 0f)
                    angle += Mathf.PI * 2f; // [0, 2π)
                rim.Add(new Vector3(angle, v.y, 0f));
            }

            if (rim.Count < 8)
                return null;

            // 최외곽 링은 등간격 각도(0번 = +X축, 반시계)로 생성되므로(GenerateIslandMesh),
            // 각도순으로 정렬하면 인덱스 i ↔ 각도 i/n·2π 가 성립한다(SampleRim의 전제).
            rim.Sort((a, b) => a.x.CompareTo(b.x));
            var heights = new float[rim.Count];
            for (int i = 0; i < rim.Count; i++)
                heights[i] = rim[i].y;
            return heights;
        }

        /// <summary>등간격 각도 배열의 순환 선형 보간. IslandMeshGenerator.MaskAt과 같은 규약이다.</summary>
        private static float SampleRim(float[] heights, float angle)
        {
            int n = heights.Length;
            float twoPi = Mathf.PI * 2f;
            float a = angle - Mathf.Floor(angle / twoPi) * twoPi; // [0, 2π)
            float f = a / twoPi * n;
            int i0 = (int)Mathf.Floor(f) % n;
            if (i0 < 0) i0 += n;
            int i1 = (i0 + 1) % n;
            return Mathf.Lerp(heights[i0], heights[i1], f - Mathf.Floor(f));
        }

        // ── 결정적 해시 노이즈 (rng 소비 0) ───────────────────────────────────────────

        /// <summary>
        /// 정수 격자점 해시 → [0,1). IslandMeshGenerator.ComputeNoiseSeed와 같은 xorshift-곱
        /// finalizer 계열의 순수 함수다. System.Random/UnityEngine.Random을 일절 쓰지 않는다.
        /// </summary>
        private static float LatticeHash01(int xi, int zi)
        {
            unchecked
            {
                uint h = (uint)(xi * 73856093) ^ (uint)(zi * 19349663) ^ 0x5F356495u;
                h ^= h >> 16;
                h *= 0x7FEB352Du;
                h ^= h >> 15;
                h *= 0x846CA68Bu;
                h ^= h >> 16;
                return (h & 0xFFFFFFu) / (float)0x1000000u;
            }
        }

        /// <summary>격자 해시를 스무스스텝으로 보간한 값 노이즈, 반환 [-1, 1]. 파장 = 1/스케일.</summary>
        private static float ValueNoise(float x, float z)
        {
            float fx = Mathf.Floor(x);
            float fz = Mathf.Floor(z);
            int xi = (int)fx;
            int zi = (int)fz;
            float tx = x - fx;
            float tz = z - fz;
            tx = tx * tx * (3f - 2f * tx);
            tz = tz * tz * (3f - 2f * tz);

            float a = LatticeHash01(xi, zi);
            float b = LatticeHash01(xi + 1, zi);
            float c = LatticeHash01(xi, zi + 1);
            float d = LatticeHash01(xi + 1, zi + 1);
            return (Mathf.Lerp(Mathf.Lerp(a, b, tx), Mathf.Lerp(c, d, tx), tz) - 0.5f) * 2f;
        }

        /// <summary>
        /// 모래 언덕 기복(m). 파장 약 18m의 큰 언덕 + 약 7.7m의 잔결 2옥타브, 합성 진폭 ±DuneAmplitude.
        /// 두 옥타브의 오프셋을 달리해 격자 위상이 겹치지 않게 한다(Fbm의 비정수 배율과 같은 취지).
        /// </summary>
        private static float DuneNoise(float worldX, float worldZ)
        {
            float n = ValueNoise(worldX * 0.055f, worldZ * 0.055f) * 0.75f
                    + ValueNoise(worldX * 0.13f + 71.3f, worldZ * 0.13f - 38.9f) * 0.25f;
            return n * DuneAmplitude;
        }

        // ── 재질 ────────────────────────────────────────────────────────────────────

        /// <summary>
        /// 수중 모래색: IslandSand(#C2B280)를 어둡게(×0.66) 하고 채도를 낮춘다(상대휘도 회색 35% 혼합).
        /// 팔레트에 새 색을 추가하는 것이 아니라 지면 캡(Shade(IslandSand, 0.78/0.88))과 같은
        /// "IslandSand의 한 단계"다 - 물의 깊이 흡수까지 더해지면 실기에서는 더 어둡고 푸르게 보인다.
        /// 필드 초기화식이 아니라 호출 시 계산한다(Unity 6.5: 필드 초기화식에서 Unity API 호출 금지 -
        /// StructureVisualBuilder의 팔레트 상수는 순수 Color라 안전하지만 규칙을 통일한다).
        /// </summary>
        private static Color UnderwaterSandColor()
        {
            Color deep = ResourceVisualLibrary.Shade(StructureVisualBuilder.IslandSand, 0.66f);
            float luma = 0.2126f * deep.r + 0.7152f * deep.g + 0.0722f * deep.b;
            return Color.Lerp(deep, new Color(luma, luma, luma, 1f), 0.35f);
        }

        // ── 정점 수 총합 로그 (프레임당 1줄) ─────────────────────────────────────────

        /// <summary>
        /// 첫 스커트에 붙는 일회성 로거. 섬 50개 생성은 전부 같은 프레임의 동기 루프이므로,
        /// Start()(생성 프레임의 루프 종료 후, 첫 Update 전)가 돌 때는 누적이 끝나 있다 - 총합만 1줄 찍는다.
        /// OnGUI/코루틴을 쓰지 않고 "생성 흐름 밖에서 정확히 한 번"을 얻는 최소 장치다.
        /// </summary>
        private sealed class SeabedBatchLogger : MonoBehaviour
        {
            private bool logged;

            private void Start()
            {
                Debug.Log($"[SeabedGenerator] 해저 스커트 {pendingSkirtCount}개 생성, 총 정점 {pendingVertexTotal}개 (섬당 {(RadialRings + 1) * AngularSegments})");
                logged = true;
                ResetPending();
                Destroy(this);
            }

            private void OnDestroy()
            {
                // Start 전에 월드가 재생성돼 파괴되면 누적 플래그만 되돌린다(다음 배치가 다시 예약한다).
                if (!logged)
                    ResetPending();
            }

            private static void ResetPending()
            {
                pendingVertexTotal = 0;
                pendingSkirtCount = 0;
                loggerScheduled = false;
            }
        }
    }
}
