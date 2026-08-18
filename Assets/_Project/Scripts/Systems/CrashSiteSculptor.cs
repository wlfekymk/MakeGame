using System.Collections.Generic;
using UnityEngine;
using MakeGame.Data;

namespace MakeGame.Systems
{
    /// <summary>
    /// 시작 섬 해안의 여객기 잔해(AirlinerWreck) 주변을 "불시착 현장"으로 조각하는 후처리.
    /// WorldMapManager.SpawnAirlinerWreck이 잔해 배치를 **확정한 직후** 1회 호출한다.
    ///
    /// 하는 일(전부 순수 장식 - 세이브 키/자원 노드/상호작용물 불변):
    ///   ① 트렌치: 동체 아래에서 바다 쪽으로 끌린 고랑(중심 최대 ~1.1m, 폭 ~13m, 총 ~55m)을
    ///      지형 본체 메시 + 모래 캡 3장(Dry/Damp/WetSandCap)의 정점 y에 직접 판다.
    ///      가장자리에는 +0.25m 둔덕(rim berm - 뒤집힌 흙 둑)을 올린다.
    ///   ② 초목 제거: 존 안의 순수 장식 초목(Veg_* = 야자/덤불/풀포기, Deco_Drift* = 표류물)과
    ///      잔디 카드(GrassFieldSystem 인스턴스 배열)를 제거한다. 자원 노드("Resource_")·
    ///      상호작용물은 IslandSurface 밖 다른 계층이라 구조적으로 건드릴 수 없다.
    ///   ③ 흙색: 존을 어두운 토양색 오버레이 메시(변형된 지형 삼각형을 그대로 잘라 쓴 덮개,
    ///      캡과 같은 방식)로 덮는다. URP Lit은 정점 색을 읽지 않아(BuildCapLayer의 B10 주석에서
    ///      검증된 사실) 버텍스 컬러 경로는 불가능하고, 이 프로젝트의 검증된 경로인
    ///      "지형 삼각형 잘라낸 덮개 + 단색 머티리얼 + 기존 sand 텍스처"를 그대로 따른다.
    ///      중심(고랑 바닥)일수록 진한 3톤 서브메시 + 삼각형 해시 디더로 경계를 점묘화한다.
    ///   ④ 재착지: MeshCollider를 갱신(sharedMesh 재대입)한 뒤 잔해 루트 y를 새 지면에 재스냅해
    ///      동체가 고랑 바닥에 안착하게 한다(시각/콜라이더는 전부 자식이라 함께 내려간다).
    ///
    /// ★ 난수 소비 0 ★ System.Random/UnityEngine.Random을 일절 쓰지 않는다. 미세 요철·디더는
    /// 전부 위치 기반 순수 해시(GrassFieldSystem.Hash01과 같은 finalizer)다. 잔해 위치·섬 지형이
    /// worldSeed에 결정적이므로 같은 시드 = 같은 현장이 항상 재현된다.
    ///
    /// ★ 배치 불변 ★ SpawnAirlinerWreck의 해변 탐색이 끝난 뒤에만 불리는 순수 후처리라
    /// 잔해/자원/위험요소의 배치에 영향을 줄 수 없다. 지형 정점 y만 바꾸므로(XZ·토폴로지 불변)
    /// TerrainSampler("Island_" 이름 필터)·스포너 산포 반경 전제도 그대로 성립한다.
    ///
    /// 멱등성: 오버레이 오브젝트(CrashSoilOverlay)를 마커로 써서 같은 섬에 두 번 적용되지 않는다.
    /// RegenerateWorld는 섬 오브젝트를 통째로 지우고 새로 만들므로 마커도 함께 사라져 자연 리셋된다.
    /// </summary>
    public static class CrashSiteSculptor
    {
        // ── 트렌치 형태 파라미터 (잔해 로컬 좌표계: +Z = 기수 = 섬 안쪽, -Z = 바다 쪽) ────────
        // AirlinerWreck v2 모델은 기수 z=+19.3, 꼬리 z=-14.6(동체 약 39m). 트렌치는 기수 바로
        // 앞(+20)에서 시작해 바다 쪽 -35까지 = 총 55m(동체 39m + 바다 쪽 끌린 자국 ~16m).
        // 바다 쪽 꼬리가 섬 메시 밖으로 나가면 해당 정점이 없어 자연히 잘린다(불투명 바다 아래라 무해).

        /// <summary>트렌치 반폭(m). 전폭 ~13m - 동체 폭(콜라이더 기준 ~4m) + 찢긴 날개가 쓸고 간 폭.</summary>
        private const float TrenchHalfWidth = 6.5f;

        /// <summary>고랑 가장자리 둔덕(rim berm)의 폭(m).</summary>
        private const float BermWidth = 3.0f;

        /// <summary>중심 최대 깊이(m).</summary>
        private const float MaxTrenchDepth = 1.1f;

        /// <summary>둔덕 최대 높이(m) - "뒤집힌 흙 둑".</summary>
        private const float BermHeight = 0.25f;

        /// <summary>트렌치의 기수 쪽 끝(잔해 로컬 z, m).</summary>
        private const float NoseEndLocalZ = 20f;

        /// <summary>트렌치의 바다 쪽 끝(잔해 로컬 z, m).</summary>
        private const float TailEndLocalZ = -35f;

        /// <summary>위치 해시 미세 요철의 진폭(±m).</summary>
        private const float MicroNoiseAmplitude = 0.15f;

        /// <summary>흙색 오버레이가 트렌치+둔덕 밖으로 번지는 마진(m).</summary>
        private const float SoilMargin = 2.0f;

        /// <summary>초목/잔디 제거 존이 트렌치+둔덕 밖으로 넓어지는 마진(m).</summary>
        private const float VegetationMargin = 3.0f;

        /// <summary>
        /// 흙 오버레이를 지형 위에 띄우는 높이(m). 모래 캡(0.08m)보다 높아야 존 안의 캡을 덮는다.
        /// 캡 오프셋 근거는 BuildGroundCaps의 capOffset 주석과 같다(z-파이팅 회피 + 배치물 파묻힘 최소).
        /// </summary>
        private const float SoilOverlayOffset = 0.12f;

        /// <summary>멱등성 마커 겸 오버레이 오브젝트 이름(섬 지형 오브젝트의 직계 자식).</summary>
        private const string SoilOverlayName = "CrashSoilOverlay";

        /// <summary>
        /// 트렌치 바닥이 해수면 위로 유지해야 하는 최소 높이(m). 해변(0.2~1.2m)에서 1.1m를 그대로
        /// 파면 고랑 바닥과 재착지한 동체가 바다 평면(y=0 불투명 - IslandMeshGenerator의 전제) 아래로
        /// 잠겨 현장이 물에 찬다. 파는 깊이를 "원래 지면 높이 - 이 값"으로 클램프해 바닥이 항상
        /// 물가 바로 위에 남게 한다(해수면 아래 구간은 애초에 파지 않는다 - 어차피 보이지 않는다).
        /// </summary>
        private const float MinFloorAboveSea = 0.05f;

        /// <summary>모래 캡이 지형 위에 떠 있는 높이(BuildGroundCaps.capOffset과 같은 값). 캡 정점의
        /// 월드 y에서 이만큼 빼야 실제 지면 높이가 나온다(깊이 클램프가 지형과 같은 기준을 쓰게).</summary>
        private const float SandCapLift = 0.08f;

        /// <summary>흙 톤 개수(서브메시). 0 = 중심(가장 진한 토양), 마지막 = 존 가장자리.</summary>
        private const int SoilToneCount = 3;

        // 어두운 토양 팔레트. 중심(고랑 바닥, 젖은 뒤집힌 흙) → 가장자리(마른 흙) 순.
        // Driftwood(#8C6640)와 같은 갈색 계열을 어둡게 누른 값이라 기존 팔레트와 이질감이 없다.
        private static readonly Color[] SoilTones =
        {
            new Color(0.26f, 0.19f, 0.12f), // 중심 - 진한 습토
            new Color(0.34f, 0.25f, 0.16f), // 중간 - 뒤집힌 흙
            new Color(0.44f, 0.34f, 0.22f), // 가장자리 - 마른 흙(모래와의 전이)
        };

        /// <summary>
        /// 시작 섬 해안의 여객기 잔해 주변을 불시착 현장으로 조각한다.
        /// SpawnAirlinerWreck이 잔해 루트를 세운 직후(해변 탐색 확정 후) 1회 호출된다.
        /// </summary>
        /// <param name="wreckRoot">AirlinerWreck 루트(위치 = 해변 지면, +Z = 기수 = 섬 안쪽).</param>
        /// <param name="startIsland">시작 섬 인스턴스(placeholderObject = "Island_0_..." 지형).</param>
        public static void Apply(Transform wreckRoot, IslandInstance startIsland)
        {
            if (wreckRoot == null || startIsland == null || startIsland.placeholderObject == null)
                return;

            GameObject islandObject = startIsland.placeholderObject;
            Transform islandTransform = islandObject.transform;

            // 멱등성 가드. 같은 섬 오브젝트에는 한 번만 조각한다(월드 재생성 시 섬이 통째로
            // 새로 만들어지므로 마커도 함께 사라져 자연 리셋된다).
            if (islandTransform.Find(SoilOverlayName) != null)
                return;

            var terrainFilter = islandObject.GetComponent<MeshFilter>();
            Mesh terrainMesh = terrainFilter != null ? terrainFilter.sharedMesh : null;
            if (terrainMesh == null)
                return; // islandPlaceholderPrefab 구성이면 절차 지형이 없어 조각할 수 없다 - 조용히 무동작.

            // 잔해 로컬 수평 좌표계. 섬/잔해 계층은 yaw 회전뿐이고 스케일 1이라(직접 확인)
            // 수평 forward/right 두 축이면 존 판정에 충분하다.
            Vector3 origin = wreckRoot.position;
            Vector3 forward = wreckRoot.forward;
            forward.y = 0f;
            forward = forward.sqrMagnitude < 0.0001f ? Vector3.forward : forward.normalized;
            Vector3 right = new Vector3(forward.z, 0f, -forward.x);

            // ── ① 트렌치: 지형 본체 + 모래 캡 전부의 정점 y를 같은 월드 함수로 변형 ─────────
            // 캡 메시(Dry/Damp/WetSandCap)는 지형 정점을 그대로 복사해 +0.08m 띄운 덮개라
            // (BuildCapLayer), 지형만 파면 캡이 고랑 위에 다리처럼 떠 버린다 - 반드시 함께 판다.
            DeformMesh(islandTransform, terrainMesh, origin, forward, right, 0f);

            Transform surfaceRoot = islandTransform.Find(IslandMeshGenerator.SurfaceRootName);
            if (surfaceRoot != null)
            {
                for (int i = 0; i < surfaceRoot.childCount; i++)
                {
                    Transform child = surfaceRoot.GetChild(i);
                    if (!child.name.EndsWith("SandCap"))
                        continue;
                    var capFilter = child.GetComponent<MeshFilter>();
                    if (capFilter != null && capFilter.sharedMesh != null)
                        DeformMesh(child, capFilter.sharedMesh, origin, forward, right, SandCapLift);
                }
            }

            // ── MeshCollider 갱신: sharedMesh 재대입으로 물리 셰이프를 다시 굽는다 ───────────
            // 같은 Mesh 참조를 다시 넣기만 하면 PhysX가 변경을 모른다 - null로 비웠다 재대입해야
            // 쿠킹이 다시 돈다. 플레이어가 고랑을 걸을 수 있어야 하고, 아래 재스냅 레이도 이 콜라이더를 쓴다.
            var terrainCollider = islandObject.GetComponent<MeshCollider>();
            if (terrainCollider != null)
            {
                terrainCollider.sharedMesh = null;
                terrainCollider.sharedMesh = terrainMesh;
            }
            // Physics.autoSyncTransforms는 기본 false(AGENT_BRIEF의 반복 함정) - 같은 프레임의
            // 후속 SnapToGround(아래 재착지 + 뒤이어 불리는 SpawnBoatWorkbench)가 새 지형을 보게 한다.
            Physics.SyncTransforms();

            // ── ③ 흙색 오버레이(변형된 지형 삼각형 기준이므로 반드시 변형 후에) ────────────
            BuildSoilOverlay(islandTransform, terrainMesh, origin, forward, right,
                IslandSizeMetrics.GetTerrainRadius(startIsland.size));

            // ── ② 존 안의 순수 장식 초목 제거 ───────────────────────────────────────────
            RemoveDecorativeVegetation(surfaceRoot, origin, forward, right);

            // 잔디/꽃 카드 제거(회전 박스 존). 존 = 트렌치 + 둔덕 + 마진.
            Vector3 grassBoxCenter = origin + forward * ((NoseEndLocalZ + TailEndLocalZ) * 0.5f);
            var grassBoxHalfExtents = new Vector3(
                TrenchHalfWidth + BermWidth + VegetationMargin,
                50f, // 높이는 사실상 무제한(존은 수평 판정이면 충분하다)
                (NoseEndLocalZ - TailEndLocalZ) * 0.5f + VegetationMargin);
            GrassFieldSystem.RemoveInstancesInOrientedBox(
                islandTransform, grassBoxCenter, wreckRoot.rotation, grassBoxHalfExtents);

            // ── ④ 잔해 재착지: 루트 y를 고랑 바닥의 새 지면에 재스냅 ────────────────────────
            // AirlinerWreck의 시각/콜라이더/샐비지 포인트는 전부 이 루트의 자식이라 함께 내려간다.
            Vector3 snapped = TerrainSampler.SnapToGround(origin);
            if (!Mathf.Approximately(snapped.y, origin.y))
                wreckRoot.position = snapped;
        }

        // ═══════════════════════════════════════════════════════════════════════════
        //  트렌치 높이장
        // ═══════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// 메시 하나의 정점을 월드 좌표로 판정해 트렌치/둔덕 변형을 적용한다.
        /// 섬 계층은 회전/스케일이 없어(지형 오브젝트는 위치만 갖는다 - CreateProceduralIslandTerrain)
        /// 월드 Δy를 로컬 y에 그대로 더해도 된다. 원본 메시가 RecalculateNormals로 만들어졌으므로
        /// (GenerateIslandMesh/BuildCapLayer 둘 다) 같은 방법으로 다시 계산해야 셰이딩이 일관된다 -
        /// 이 메시들은 이음매 없는 공유 정점 토폴로지라 부분 재계산이 필요 없다.
        /// </summary>
        /// <param name="surfaceLift">이 메시가 지형 위에 떠 있는 높이(지형 본체 0, 모래 캡 0.08m).
        /// 월드 y에서 빼서 실제 지면 높이를 구한다 - 깊이 클램프가 지형/캡에서 같은 값이 되어
        /// 변형 후에도 캡-지형 간격 8cm가 정확히 보존된다(간격 0 = z-파이팅 사고 방지).</param>
        private static void DeformMesh(Transform meshTransform, Mesh mesh,
            Vector3 origin, Vector3 forward, Vector3 right, float surfaceLift)
        {
            Vector3[] vertices = mesh.vertices;
            bool changed = false;

            for (int i = 0; i < vertices.Length; i++)
            {
                Vector3 world = meshTransform.TransformPoint(vertices[i]);
                float delta = TrenchDeltaY(world, world.y - surfaceLift, origin, forward, right);
                if (delta != 0f)
                {
                    vertices[i].y += delta;
                    changed = true;
                }
            }

            if (!changed)
                return;

            mesh.vertices = vertices;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
        }

        /// <summary>
        /// 월드 한 점의 트렌치 변형량(Δy, m). 0이면 존 밖.
        /// 단면: 중심 -MaxTrenchDepth(smoothstep 종 모양) → 가장자리 0 → 둔덕 +BermHeight(포물선) → 0.
        /// 종단: 양 끝에서 smoothstep으로 잦아들고, 바다 쪽은 얕게(끌리기 시작한 자국) 기수 쪽으로
        /// 깊어진다. 미세 요철은 위치 기반 격자 해시 노이즈(rng 소비 0)다.
        /// 파는 깊이는 "지면이 해수면 위 MinFloorAboveSea를 유지할 수 있는 만큼"으로 클램프한다
        /// (groundY 파라미터 - 물가 근처에서 고랑이 자연히 얕아지고, 현장이 물에 잠기지 않는다).
        /// </summary>
        private static float TrenchDeltaY(Vector3 world, float groundY,
            Vector3 origin, Vector3 forward, Vector3 right)
        {
            Vector3 p = world - origin;
            float s = p.x * forward.x + p.z * forward.z; // 잔해 로컬 z(기수 방향 +)
            float t = p.x * right.x + p.z * right.z;     // 잔해 로컬 x(우측 +)

            if (s < TailEndLocalZ || s > NoseEndLocalZ)
                return 0f;

            float d = Mathf.Abs(t);
            float lateralExtent = TrenchHalfWidth + BermWidth;
            if (d >= lateralExtent)
                return 0f;

            // 종단 봉투: 바다 쪽 끝에서 8m, 기수 쪽 끝에서 6m에 걸쳐 잦아든다.
            float envelope = SmoothStep01((s - TailEndLocalZ) / 8f)
                * (1f - SmoothStep01((s - (NoseEndLocalZ - 6f)) / 6f));
            if (envelope <= 0f)
                return 0f;

            // 바다 쪽(끌리기 시작)은 얕고 동체 아래로 갈수록 깊다.
            float depthScale = Mathf.Lerp(0.45f, 1f, SmoothStep01((s - TailEndLocalZ) / 25f));

            float delta;
            if (d < TrenchHalfWidth)
            {
                // 고랑: 중심 1 → 가장자리 0의 smoothstep 종 모양(경계 기울기 0이라 둔덕과 이음매가 없다).
                float bell = 1f - SmoothStep01(d / TrenchHalfWidth);
                delta = -MaxTrenchDepth * depthScale * bell;
            }
            else
            {
                // 둔덕: 4r(1-r) 포물선(양 끝 0이라 고랑/바깥 지형과 연속).
                float r = (d - TrenchHalfWidth) / BermWidth;
                delta = BermHeight * (4f * r * (1f - r));
            }

            // 미세 요철(±0.15m): 파장 2.6m 격자 해시 노이즈. 존 중심에서 최대, 가장자리로 0.
            float presence = Mathf.Clamp01(1f - d / lateralExtent);
            float micro = (LatticeNoise01(world.x, world.z, 2.6f, 0xC5A3D1E7u) - 0.5f)
                * 2f * MicroNoiseAmplitude * presence;

            float result = (delta + micro) * envelope;

            // 깊이 클램프: 지면이 해수면(y=0 - IslandMeshGenerator의 바다 평면 전제) 위
            // MinFloorAboveSea를 유지할 수 있는 만큼만 판다. 둔덕(+)은 클램프하지 않는다.
            if (result < 0f)
                result = Mathf.Max(result, -Mathf.Max(0f, groundY - MinFloorAboveSea));

            return result;
        }

        // ═══════════════════════════════════════════════════════════════════════════
        //  흙색 오버레이 (변형된 지형 삼각형을 잘라낸 덮개 - BuildCapLayer와 같은 방식)
        // ═══════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// 존을 덮는 어두운 토양색 덮개 메시를 만든다. 삼각형이 하나도 안 잡혀도 오브젝트는
        /// 만든다(멱등성 마커 겸용). 캡(+0.08m)보다 높은 +0.12m에 띄워 존 안의 모래 캡을 가린다.
        /// </summary>
        private static void BuildSoilOverlay(Transform islandTransform, Mesh terrainMesh,
            Vector3 origin, Vector3 forward, Vector3 right, float radius)
        {
            var overlayGo = new GameObject(SoilOverlayName);
            overlayGo.transform.SetParent(islandTransform, false);
            overlayGo.transform.localPosition = new Vector3(0f, SoilOverlayOffset, 0f);

            Vector3[] sourceVertices = terrainMesh.vertices; // 변형 후 정점 - 고랑 굴곡을 그대로 따른다.
            int[] sourceTriangles = terrainMesh.triangles;
            Vector2[] sourceUvs = terrainMesh.uv;
            bool hasUv = sourceUvs != null && sourceUvs.Length == sourceVertices.Length;

            var remap = new Dictionary<int, int>(256);
            var vertices = new List<Vector3>();
            var uvs = new List<Vector2>();
            var toneTriangles = new List<int>[SoilToneCount];
            for (int i = 0; i < SoilToneCount; i++)
                toneTriangles[i] = new List<int>();

            for (int tri = 0; tri + 2 < sourceTriangles.Length; tri += 3)
            {
                int i0 = sourceTriangles[tri];
                int i1 = sourceTriangles[tri + 1];
                int i2 = sourceTriangles[tri + 2];

                Vector3 centroidLocal = (sourceVertices[i0] + sourceVertices[i1] + sourceVertices[i2]) / 3f;
                Vector3 centroidWorld = islandTransform.TransformPoint(centroidLocal);

                float strength = SoilStrength(centroidWorld, origin, forward, right);
                if (strength <= 0.02f)
                    continue;

                // 삼각형 해시 디더로 톤 경계를 점묘화(직선 경계 사고 - B9 - 의 재발 방지책과 동일).
                float dither = (Hash01(
                    Mathf.RoundToInt(centroidWorld.x * 4f),
                    Mathf.RoundToInt(centroidWorld.z * 4f), 0x51ED270Bu) - 0.5f) * 0.16f;
                float graded = strength + dither;
                int tone = graded >= 0.55f ? 0 : (graded >= 0.24f ? 1 : 2);

                var bucket = toneTriangles[tone];
                bucket.Add(RemapVertex(i0, remap, sourceVertices, sourceUvs, hasUv, vertices, uvs));
                bucket.Add(RemapVertex(i1, remap, sourceVertices, sourceUvs, hasUv, vertices, uvs));
                bucket.Add(RemapVertex(i2, remap, sourceVertices, sourceUvs, hasUv, vertices, uvs));
            }

            var usedTones = new List<int>(SoilToneCount);
            for (int i = 0; i < SoilToneCount; i++)
            {
                if (toneTriangles[i].Count > 0)
                    usedTones.Add(i);
            }
            if (usedTones.Count == 0)
                return; // 존이 메시 밖(전부 바다)인 극단 - 오브젝트는 마커로 남는다.

            var mesh = new Mesh();
            mesh.name = "CrashSoilOverlay";
            mesh.SetVertices(vertices);
            if (hasUv)
                mesh.SetUVs(0, uvs);
            mesh.subMeshCount = usedTones.Count;
            for (int sub = 0; sub < usedTones.Count; sub++)
                mesh.SetTriangles(toneTriangles[usedTones[sub]], sub);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            overlayGo.AddComponent<MeshFilter>().sharedMesh = mesh;

            var renderer = overlayGo.AddComponent<MeshRenderer>();
            var materials = new Material[usedTones.Count];
            for (int sub = 0; sub < usedTones.Count; sub++)
            {
                int tone = usedTones[sub];
                // 새 텍스처를 만들지 않는다 - 기존 "sand" 그레인 텍스처에 토양색만 곱한다.
                var material = StructureVisualBuilder.CreateColorMaterial(SoilTones[tone], "sand");
                // 캡과 같은 타일링 규칙(radius × 1.5): UV가 섬 전체 0~1 정규화라 반지름 비례가 필요하다.
                material.mainTextureScale = new Vector2(radius * 1.5f, radius * 1.5f);
                // 톤마다 위상을 어긋내 같은 그레인 무늬가 경계에서 그대로 이어지지 않게 한다(캡과 동일).
                material.mainTextureOffset = new Vector2(tone * 0.37f, tone * 0.19f);
                materials[sub] = material;
            }
            renderer.sharedMaterials = materials;

            // 지면에서 12cm 떠 있는 덮개라 그림자를 드리우면 자기 그림자로 얼룩진다(캡과 같은 규칙).
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = true;
        }

        /// <summary>
        /// 흙색 강도(0~1). 종단 봉투 × (트렌치 중심 1 → 트렌치+둔덕+마진 바깥 0의 완만한 감쇠).
        /// 톤 분류(중심 진하게)와 포함 판정에 함께 쓴다.
        /// </summary>
        private static float SoilStrength(Vector3 world, Vector3 origin, Vector3 forward, Vector3 right)
        {
            Vector3 p = world - origin;
            float s = p.x * forward.x + p.z * forward.z;
            float t = p.x * right.x + p.z * right.z;

            if (s < TailEndLocalZ || s > NoseEndLocalZ)
                return 0f;

            float envelope = SmoothStep01((s - TailEndLocalZ) / 8f)
                * (1f - SmoothStep01((s - (NoseEndLocalZ - 6f)) / 6f));
            if (envelope <= 0f)
                return 0f;

            float outer = TrenchHalfWidth + BermWidth + SoilMargin;
            float lateral = 1f - SmoothStep01(Mathf.Abs(t) / outer);
            return envelope * lateral;
        }

        /// <summary>원본 지형 정점을 오버레이 메시로 옮기고(중복 없이) 새 인덱스를 돌려준다.</summary>
        private static int RemapVertex(int sourceIndex, Dictionary<int, int> remap,
            Vector3[] sourceVertices, Vector2[] sourceUvs, bool hasUv,
            List<Vector3> vertices, List<Vector2> uvs)
        {
            if (remap.TryGetValue(sourceIndex, out int existing))
                return existing;

            int newIndex = vertices.Count;
            vertices.Add(sourceVertices[sourceIndex]);
            if (hasUv)
                uvs.Add(sourceUvs[sourceIndex]);
            remap.Add(sourceIndex, newIndex);
            return newIndex;
        }

        // ═══════════════════════════════════════════════════════════════════════════
        //  장식 초목 제거
        // ═══════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// 존(트렌치+둔덕+마진) 안의 순수 장식 초목만 제거한다. 대상은 IslandSurface 직계 자식 중
        /// "Veg_"(야자/덤불/풀포기)와 "Deco_Drift"(표류물) 접두 오브젝트뿐이다 - 바위/석재(Deco_Rock*,
        /// Deco_Stone*)는 불시착에도 남을 법한 지형지물이라 남기고, 자원 노드("Resource_")·상호작용물은
        /// 애초에 이 계층에 없다(WorldMapManager 직속). 이름 접두는 IslandMeshGenerator.Vegetation.cs에서
        /// 직접 확인한 값이다. 각 오브젝트의 transform.position이 접지점(지면 스팟)이라 위치 판정에 그대로 쓴다.
        /// </summary>
        private static void RemoveDecorativeVegetation(Transform surfaceRoot,
            Vector3 origin, Vector3 forward, Vector3 right)
        {
            if (surfaceRoot == null)
                return;

            for (int i = surfaceRoot.childCount - 1; i >= 0; i--)
            {
                Transform child = surfaceRoot.GetChild(i);
                string childName = child.name;
                bool decorative = childName.StartsWith("Veg_") || childName.StartsWith("Deco_Drift");
                if (!decorative)
                    continue;

                if (IsInClearZone(child.position, origin, forward, right))
                    Object.Destroy(child.gameObject);
            }
        }

        /// <summary>초목 제거 존 판정(트렌치+둔덕+마진의 회전 박스, 수평 전용).</summary>
        private static bool IsInClearZone(Vector3 world, Vector3 origin, Vector3 forward, Vector3 right)
        {
            Vector3 p = world - origin;
            float s = p.x * forward.x + p.z * forward.z;
            float t = p.x * right.x + p.z * right.z;
            return s >= TailEndLocalZ - VegetationMargin
                && s <= NoseEndLocalZ + VegetationMargin
                && Mathf.Abs(t) <= TrenchHalfWidth + BermWidth + VegetationMargin;
        }

        // ═══════════════════════════════════════════════════════════════════════════
        //  결정적 해시 (rng 소비 0 - GrassFieldSystem.Hash01과 같은 finalizer 계열)
        // ═══════════════════════════════════════════════════════════════════════════

        /// <summary>격자 정수 좌표 + salt → [0,1). System.Random/UnityEngine.Random을 일절 쓰지 않는다.</summary>
        private static float Hash01(int xi, int zi, uint salt)
        {
            unchecked
            {
                uint h = (uint)(xi * 73856093) ^ (uint)(zi * 19349663) ^ salt;
                h ^= h >> 16;
                h *= 0x7FEB352Du;
                h ^= h >> 15;
                h *= 0x846CA68Bu;
                h ^= h >> 16;
                return (h & 0xFFFFFFu) / (float)0x1000000u;
            }
        }

        /// <summary>파장 wavelength의 값 노이즈 [0,1]: 격자점 해시를 smoothstep 이중 선형 보간
        /// (GrassFieldSystem.LatticeNoise와 같은 구조). 미세 요철 전용.</summary>
        private static float LatticeNoise01(float x, float z, float wavelength, uint salt)
        {
            float fx = x / wavelength;
            float fz = z / wavelength;
            int x0 = Mathf.FloorToInt(fx);
            int z0 = Mathf.FloorToInt(fz);
            float tx = fx - x0;
            float tz = fz - z0;
            tx = tx * tx * (3f - 2f * tx);
            tz = tz * tz * (3f - 2f * tz);
            float h00 = Hash01(x0, z0, salt);
            float h10 = Hash01(x0 + 1, z0, salt);
            float h01 = Hash01(x0, z0 + 1, salt);
            float h11 = Hash01(x0 + 1, z0 + 1, salt);
            return Mathf.Lerp(Mathf.Lerp(h00, h10, tx), Mathf.Lerp(h01, h11, tx), tz);
        }

        /// <summary>0 이하 → 0, 1 이상 → 1, 사이는 3t²-2t³.</summary>
        private static float SmoothStep01(float t)
        {
            t = Mathf.Clamp01(t);
            return t * t * (3f - 2f * t);
        }
    }
}
