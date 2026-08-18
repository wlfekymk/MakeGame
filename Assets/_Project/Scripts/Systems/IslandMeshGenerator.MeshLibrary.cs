using System.Collections.Generic;
using UnityEngine;
using MakeGame.Data;

namespace MakeGame.Systems
{
    public static partial class IslandMeshGenerator
    {
        /// <summary>
        /// 콜라이더가 **자동으로는** 전혀 붙지 않는 시각 전용 파츠를 만든다.
        /// StructureVisualBuilder.CreateVisualPart는 GameObject.CreatePrimitive로 만든 뒤 콜라이더를
        /// Object.Destroy하는데, Destroy는 프레임 끝까지 지연되므로 그 사이에 실행되는 다른 스포너의
        /// SnapToGround 레이가 초목 콜라이더를 스칠 수 있다. 초목은 개수가 수백 개라 그 위험을 감수할
        /// 이유가 없어, 콜라이더가 애초에 생기지 않는 경로로 따로 만든다.
        /// (물리 차단이 필요한 파츠 — 바위 큰 덩어리·야자수 줄기 — 는 호출부가 명시적으로만 단다.
        ///  Vegetation.cs 상단 [콜라이더 정책] 주석 참고. 예전 [콜라이더 절대 금지]는 그 정책으로 대체됐다.)
        /// </summary>
        private static GameObject CreatePart(Transform parent, string name, PrimitiveType primitiveType,
            Vector3 localPosition, Vector3 localScale, Quaternion localRotation, Material material)
        {
            return CreatePart(parent, name, GetPrimitiveMesh(primitiveType),
                localPosition, localScale, localRotation, material);
        }

        /// <summary>
        /// 위와 같지만 내장 프리미티브 대신 이 클래스가 만든 저폴리 메시를 쓴다(B9).
        /// 메시는 반드시 캐시된 공유 메시를 넘겨라 - 파츠마다 새 Mesh를 만들면 수백 개가 쌓인다.
        /// </summary>
        private static GameObject CreatePart(Transform parent, string name, Mesh mesh,
            Vector3 localPosition, Vector3 localScale, Quaternion localRotation, Material material)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            go.transform.localRotation = localRotation;
            go.transform.localScale = localScale;

            if (mesh != null)
                go.AddComponent<MeshFilter>().sharedMesh = mesh;

            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            return go;
        }

        /// <summary>
        /// 프리미티브 종류별 내장 메시를 한 번만 뽑아 캐시한다.
        /// 임시 프리미티브에 자동으로 붙는 콜라이더가 물리 씬에 한 프레임도 남지 않도록, 지연 파괴
        /// (Object.Destroy)에 앞서 즉시 SetActive(false)로 비활성화한다(비활성화는 즉시 반영된다).
        /// 반환하는 메시는 Unity 내장 공유 메시라 임시 오브젝트를 파괴해도 사라지지 않는다.
        /// </summary>
        private static Mesh GetPrimitiveMesh(PrimitiveType primitiveType)
        {
            if (primitiveMeshCache.TryGetValue(primitiveType, out Mesh cached) && cached != null)
                return cached;

            var temporary = GameObject.CreatePrimitive(primitiveType);
            temporary.SetActive(false);
            var filter = temporary.GetComponent<MeshFilter>();
            Mesh mesh = filter != null ? filter.sharedMesh : null;
            Object.Destroy(temporary);

            primitiveMeshCache[primitiveType] = mesh;
            return mesh;
        }

        private static readonly Dictionary<PrimitiveType, Mesh> primitiveMeshCache = new Dictionary<PrimitiveType, Mesh>();

        // ─────────────────────────────────────────────────────────────────────────
        //  저폴리 초목 메시 (B9)  —  "가장 안 보이는 파츠가 삼각형 예산의 96%를 쓰던" 문제
        // ─────────────────────────────────────────────────────────────────────────
        //
        // 실측(특대 섬, 교체 전): 풀포기 78×768 = 59,904 · 덤불 로브 120×768 = 92,160 ·
        // 야자수 전체 5,760 → 합계 157,824. 즉 덤불+풀이 96%였다. 반면 야자수는 그루당 360삼각형
        // (내장 Cylinder 3×80 + 큐브 10×12)뿐이라 애초에 쌌다 - B8에서 "그루 수 42 → 16으로 상쇄했다"고
        // 한 것은 렌더러 예산에는 맞았지만 삼각형 관점에서는 잘못된 곳을 줄인 것이었다.
        //
        // 내장 Sphere는 768삼각형짜리 UV 구다. 덤불 로브와 풀포기는 둘 다 비균일 스케일로 납작하게
        // 눌러 쓰는 데다 대부분 5m 밖에서 보이므로, 그 정밀도가 화면에 도달하지 않는다
        // (ArtDirection 2장 "폴리곤을 아낄 곳은 언제나 5m 밖에서 안 보이는 디테일").
        //
        // 두 메시 모두 **내장 Sphere와 동일한 로컬 규격**(지름 1, 중심 원점, [-0.5,0.5]^3)으로 만든다.
        // 그래야 호출부의 스케일·회전·오프셋을 한 줄도 고치지 않고 그대로 쓸 수 있고, B8에서 확정한
        // 실루엣 규칙(덤불: 기울인 로브 3개·폭>>높이 / 풀: 두께가 폭의 30%)이 그대로 보존된다.

        /// <summary>
        /// 덤불 로브용 저폴리 덩어리 = 정이십면체(20삼각형). 내장 Sphere 768삼각형의 1/38.
        ///
        /// 왜 정이십면체인가: 20삼각형만으로 실루엣이 거의 원에 가깝고(면이 균일해 어느 각도에서 봐도
        /// 윤곽이 무너지지 않는다), 평면 셰이딩된 각진 면이 오히려 "매끈한 돌덩이와 덤불을 가른다"는
        /// B8의 목표를 강화한다. 로우폴리/스타일라이즈드 방향(ArtDirection 0장)과도 정확히 맞는다.
        ///
        /// 평면 셰이딩을 위해 정점을 면마다 분리한다(60정점) - 정점 수는 삼각형과 달리 이 프로젝트의
        /// 병목이 아니고, 공유 정점으로 부드럽게 셰이딩하면 20면짜리 저폴리가 찌그러진 구로 보인다.
        /// </summary>
        /// <summary>
        /// [B29] 덤불 한 포기 전체를 담은 메시(로브 3개 + 삐져나온 잎끝 8장, 92삼각형).
        ///
        /// 예전에는 정이십면체 로브 3개가 각각 별도 파츠였다(B9). 형태는 맞았지만 파츠 3개를 쓰면서도
        /// 실루엣은 "매끈한 덩어리 3개"였다 - 덤불에만 있는 신호인 **삐져나온 잎끝**이 없었기 때문이다.
        /// 지금은 로브를 메시 안으로 옮기고(파츠 3 → 1) 그 예산으로 잎끝을 넣었다.
        ///   · 로브: 방향 함수로 반지름을 흔든 각진 덩어리(WorldMeshBuilder.AddChunk) - 매끈한 구가
        ///     아니라 울퉁불퉁해서 바위와 형태가 겹치지 않는다.
        ///   · 잎끝: 두께 없는 양면 사각면 8장. 윤곽선 위로 튀어나와야 20m 밖에서 "잎"으로 읽힌다.
        ///
        /// **규격(호출부와의 계약): x·z ∈ [-0.5, 0.5], y ∈ [0, 1], 원점이 밑동.**
        /// 구 규격([-0.5,0.5]^3)이 아니다. CreateBush가 위치에 지면 좌표를 그대로 넣고 스케일에
        /// (폭, 높이, 깊이)를 미터로 넣는 것이 이 규격 때문이다 - 둘 중 하나만 바꾸면 안 된다.
        /// </summary>
        private static Mesh GetBushClumpMesh(int variant)
        {
            int v = Mathf.Abs(variant) % 3;
            string key = "bushClump" + v;
            Mesh cached;
            if (decorationMeshCache.TryGetValue(key, out cached) && cached != null)
                return cached;

            var builder = new WorldMeshBuilder();
            int seed = 4300 + v * 17;

            builder.AddChunk(new Vector3(0f, 0.42f, 0f), new Vector3(1.00f, 0.80f, 0.94f), seed, 0.30f, 0);
            builder.AddChunk(new Vector3(-0.17f, 0.62f, 0.12f), new Vector3(0.62f, 0.58f, 0.60f), seed + 5, 0.34f, 0);
            builder.AddChunk(new Vector3(0.19f, 0.57f, -0.14f), new Vector3(0.56f, 0.54f, 0.54f), seed + 11, 0.34f, 0);

            // 잎끝: 로브 표면에서 바깥·위로 뻗는다. 끝을 가늘게 좁혀 "판"이 아니라 "잎"으로 읽히게 한다.
            const int bladeCount = 8;
            for (int i = 0; i < bladeCount; i++)
            {
                float angle = (i * 360f / bladeCount + v * 17f) * Mathf.Deg2Rad;
                var outward = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                var side = new Vector3(-Mathf.Sin(angle), 0f, Mathf.Cos(angle));

                float lift = 0.52f + 0.10f * Mathf.Sin(angle * 3f + v);
                Vector3 baseCenter = outward * 0.26f + new Vector3(0f, lift, 0f);
                Vector3 tipCenter = outward * (0.46f + 0.04f * Mathf.Sin(angle * 2f))
                    + new Vector3(0f, lift + 0.34f, 0f);

                Vector3 b0 = baseCenter - side * 0.075f;
                Vector3 b1 = baseCenter + side * 0.075f;
                Vector3 t1 = tipCenter + side * 0.018f;
                Vector3 t0 = tipCenter - side * 0.018f;
                builder.AddQuad(b0, b1, t1, t0, Vector3.up, true);
            }

            Mesh mesh = builder.Finish("Veg_BushClump" + v);
            decorationMeshCache[key] = mesh;
            return mesh;
        }

        // ─────────────────────────────────────────────────────────────────────────
        //  [B45] 실물 바위 모델 (rock_a~c · [B50] rock_d/e 추가)
        // ─────────────────────────────────────────────────────────────────────────
        //
        // ── 좌표 계약(에셋이 이렇게 구워져 있다. 여기서 축·원점을 다시 만지지 않는다) ──────────
        //   단위 = 미터 · +Y 위 · +Z 정면 · **밑면이 정확히 y = 0** · X/Z 중심 정렬 · UV 있음.
        //   실측: rock_a 1.85×1.20×1.60 · rock_b 2.60×1.55×2.30 · rock_c 3.20×2.35×2.60 ·
        //   rock_d 2.95×0.95×2.45(판석) · rock_e 2.15×3.20×1.90(첨탑) (W×H×D),
        //   3,364~3,366삼각형(AssetPipeline 2장 "중형 소품 4,000" 이내).
        //
        // ── 절차 메시와 규약이 반대다 ─────────────────────────────────────────────────
        //   GetBoulderMesh는 [-0.5,0.5]^3 단위 규격이라 호출부가 (폭,높이,깊이)를 미터로 곱했다.
        //   이 모델들은 이미 미터라 같은 스케일을 곱하면 2~3배로 부푼다. 모델 경로는 목표 폭을
        //   모델 실측 폭으로 나눈 **균등 배율** 하나만 쓴다(CreateRockCluster 참고).
        //
        // ── 프리팹을 Instantiate하지 않는다 ──────────────────────────────────────────
        //   OBJ는 Resources.Load<GameObject>로만 온다(Mesh로는 null). 하지만 필요한 것은 메시 한 장뿐이고,
        //   이 파일의 파츠 생성 경로(CreatePart)가 이미 "빈 GameObject + MeshFilter + MeshRenderer"라
        //   프리팹 인스턴스를 만들면 계층·컴포넌트·머티리얼 슬롯이 공짜로 딸려 온다. 게다가 임포터
        //   설정에 따라 **MeshCollider가 딸려 올 수 있는데** 이 파일은 콜라이더가 "임포터 설정에 따라
        //   우연히" 생기면 안 된다 - 어떤 파츠가 물리를 갖는지는 호출부가 명시적으로 정한다
        //   (Vegetation.cs 상단 [콜라이더 정책] - 예전 [콜라이더 절대 금지]의 후신). 메시만 꺼내 쓰면
        //   그 우연이 구조적으로 없다.
        //
        // ── 머티리얼 ────────────────────────────────────────────────────────────────
        //   새로 만들지 않는다. BuildIslandSurface가 만든 rockMaterials(WeatheredStone × "rock" 텍스처,
        //   ResourceVisualLibrary.GetMaterial 공유 캐시 = "MG~" 접두사 · enableInstancing)를 그대로 받는다.

        /// <summary>모델 에셋 경로(Resources 기준, 확장자 없음 - 붙이면 항상 null이 돌아온다).
        /// [B50] rock_d(판석 - 낮고 넓다)·rock_e(첨탑 - 3.2m로 높다) 2종 추가. 실측은 디스크 OBJ에서
        /// 확인했다(전부 밑면 y=0 · X/Z 중심 · 미터).</summary>
        private static readonly string[] RockModelResourcePaths =
        {
            "Models/rock_a", "Models/rock_b", "Models/rock_c", "Models/rock_d", "Models/rock_e"
        };

        /// <summary>각 모델의 실측 크기(m, W×H×D). 위 경로와 인덱스가 일대일로 대응한다.</summary>
        private static readonly Vector3[] RockModelSizes =
        {
            new Vector3(1.85f, 1.20f, 1.60f),
            new Vector3(2.60f, 1.55f, 2.30f),
            new Vector3(3.20f, 2.35f, 2.60f),
            new Vector3(2.95f, 0.95f, 2.45f), // rock_d 판석: 매립 계산이 이 실측 높이(0.95)를 쓰므로 통째로 잠기지 않는다
            new Vector3(2.15f, 3.20f, 1.90f), // rock_e 첨탑: convex 헐 옆면이 가팔라 자연히 못 올라간다
        };

        private static readonly Mesh[] rockModelMeshes = new Mesh[5];
        private static int rockModelProbeFrame = -1;

        /// <summary>
        /// 목표 폭에 맞는 바위 모델의 **공유 메시**를 돌려준다. 하나도 못 찾으면 false다.
        ///
        /// [로드 규칙] Resources.Load는 정적 필드 초기자에서 부르지 않는다 - 초기자는 생성자 시점에
        /// 돌 수 있고 Unity가 그 시점의 Load를 막아 null을 준다. 그리고 **실패를 영구히 캐시하지 않는다.**
        /// 그 null을 "에셋 없음"으로 굳히면 세션 내내 절차 바위만 나온다(곰 모델이 실제로 그렇게 죽었다,
        /// AGENT_BRIEF 4장 3번). 성공할 때까지 프레임당 한 번만 다시 살핀다 -
        /// CreatureVisualBuilder.BearModelPrefab과 같은 패턴이고, 섬 하나가 바위를 최대 12개 만들므로
        /// 프레임 가드가 없으면 한 프레임에 Load가 60번(12무리 × 5경로) 불린다.
        ///
        /// [B50 변종 선택 - 2단계] 예전 "최근접 폭 1개"는 5종을 넣어도 목표 폭 구간마다 같은 변종만
        /// 나오게 한다(1.7~3.6m 구간이 변종 5개의 보로노이 구간으로 갈려 이웃 바위가 전부 같은 모델).
        /// 그래서 (1) 목표 폭 ±35% 안의 변종을 전부 후보로 모으고(없으면 최근접 1개),
        /// (2) **배치 위치 해시**(DecorationPositionHash - HazardSpawner.IsBearCubIndividual 계열)로
        /// 후보 중 하나를 결정론적으로 고른다. rng는 여기서도 0회 소비다(배치 재현성 불변) -
        /// 입력이 (이미 뽑힌 목표 폭, 이미 확정된 위치)뿐인 순수 함수라 같은 worldSeed면 같은 변종이다.
        /// 균등 배율은 ±35% 밴드 정의상 0.74~1.54 안이고 실제 폭 분포(1.7~3.6m)에서는 0.79~1.39다.
        /// </summary>
        private static bool TryGetRockModel(float targetWidth, Vector3 worldPosition, out Mesh mesh, out Vector3 size)
        {
            mesh = null;
            size = Vector3.one;

            ProbeSingleMeshModels(RockModelResourcePaths, rockModelMeshes, ref rockModelProbeFrame);

            int pick = PickVariantByPosition(RockModelSizes, rockModelMeshes, targetWidth,
                worldPosition, RockVariantSalt);
            if (pick < 0)
                return false;

            mesh = rockModelMeshes[pick];
            size = RockModelSizes[pick];
            return true;
        }

        // ── [B50] 변종 다양성 공용 도구 ─────────────────────────────────────────────
        //   신규 모델(바위 5종·야자수 6종·덤불/풀 2종)을 넣어도 "최근접 크기" 선택으로는 다양성이
        //   생기지 않아(위 TryGetRockModel 주석), 선택을 2단계(±35% 후보 → 위치 해시)로 바꿨다.
        //   ★ 새 rng 추첨 금지 ★ 여기 있는 어떤 함수도 System.Random을 받지 않는다. 이미 뽑힌 값과
        //   이미 확정된 배치 위치만 입력으로 쓴다 - 월드 배치 재현성(같은 worldSeed = 같은 월드)의 전제다.

        /// <summary>1단계 후보 밴드: 목표 크기의 ±35%. 이 밖의 변종은 배율이 1.54를 넘어 늘어나 보인다.</summary>
        private const float VariantSizeBand = 0.35f;

        // 해시 salt. 같은 위치라도 용도(변종 선택 / 스케일 보강)마다 독립인 값이 나오게 가른다.
        private const uint RockVariantSalt = 0x51A7B001u;
        private const uint PalmVariantSalt = 0x51A7B002u;
        private const uint BushVariantSalt = 0x51A7B003u;
        private const uint GrassVariantSalt = 0x51A7B004u;
        private const uint BushStretchSalt = 0x51A7B013u;
        private const uint GrassStretchSalt = 0x51A7B014u;

        /// <summary>
        /// [B50] 배치 위치 → 결정적 해시. HazardSpawner.IsBearCubIndividual과 같은 계열이다:
        /// 좌표를 정수화(0.1m 양자화 - 배치 좌표는 시드가 정하는 결정적 값이라 양자화가 흔들리지 않는다)
        /// 한 뒤 소수 곱으로 섞고 xorshift-곱 마무리(FNV/Murmur 계열 finalizer)로 상관을 없앤다.
        /// x·z가 작은 정수라 단순 덧셈만으로는 이웃 위치의 해시가 이어져 버리기 때문이다.
        /// rng 소비 0 - 순수 함수다.
        /// </summary>
        private static uint DecorationPositionHash(Vector3 worldPosition, uint salt)
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

        /// <summary>[B50] 위 해시를 [0,1) 실수로. 다양성 보강 축(스케일 지터 등)에 쓴다.</summary>
        private static float DecorationPositionHash01(Vector3 worldPosition, uint salt)
        {
            return (DecorationPositionHash(worldPosition, salt) & 0xFFFFFFu) / (float)0x1000000;
        }

        /// <summary>2단계 선택의 후보 버퍼. 메인 스레드 전용이라 재사용해도 안전하다(할당 방지).</summary>
        private static readonly List<int> variantCandidateBuffer = new List<int>(8);

        /// <summary>
        /// [B50] 변종 선택 2단계: (1) 로드된 변종 중 |기본크기 − 목표| ≤ 목표×35%를 후보로 모으고
        /// (없으면 최근접 1개), (2) 위치 해시로 후보 중 하나를 고른다. 로드된 변종이 없으면 -1이다.
        /// </summary>
        private static int PickVariantByPosition(float[] baseSizes, Mesh[] loadedMeshes, float targetSize,
            Vector3 worldPosition, uint salt)
        {
            var candidates = variantCandidateBuffer;
            candidates.Clear();

            int nearest = -1;
            float nearestDelta = float.MaxValue;
            float band = targetSize * VariantSizeBand;
            for (int i = 0; i < loadedMeshes.Length; i++)
            {
                if (loadedMeshes[i] == null)
                    continue;

                float delta = Mathf.Abs(baseSizes[i] - targetSize);
                if (delta < nearestDelta)
                {
                    nearestDelta = delta;
                    nearest = i;
                }
                if (delta <= band)
                    candidates.Add(i);
            }

            if (candidates.Count == 0)
                return nearest;
            if (candidates.Count == 1)
                return candidates[0];
            return candidates[(int)(DecorationPositionHash(worldPosition, salt) % (uint)candidates.Count)];
        }

        /// <summary>위와 같지만 기본 크기가 W×H×D 벡터인 테이블용(폭 = x로 비교한다).</summary>
        private static int PickVariantByPosition(Vector3[] baseSizes, Mesh[] loadedMeshes, float targetSize,
            Vector3 worldPosition, uint salt)
        {
            var candidates = variantCandidateBuffer;
            candidates.Clear();

            int nearest = -1;
            float nearestDelta = float.MaxValue;
            float band = targetSize * VariantSizeBand;
            for (int i = 0; i < loadedMeshes.Length; i++)
            {
                if (loadedMeshes[i] == null)
                    continue;

                float delta = Mathf.Abs(baseSizes[i].x - targetSize);
                if (delta < nearestDelta)
                {
                    nearestDelta = delta;
                    nearest = i;
                }
                if (delta <= band)
                    candidates.Add(i);
            }

            if (candidates.Count == 0)
                return nearest;
            if (candidates.Count == 1)
                return candidates[0];
            return candidates[(int)(DecorationPositionHash(worldPosition, salt) % (uint)candidates.Count)];
        }

        /// <summary>
        /// [B50] `o` 1개짜리 OBJ 모델들의 공용 프로브(바위/덤불/풀/표류물이 같은 규칙을 나눠 쓴다).
        /// 규칙은 TryGetRockModel 주석의 [로드 규칙] 그대로다: 필드 초기자에서 부르지 않고,
        /// 실패를 영구 캐시하지 않으며(다음 프레임에 다시 살핀다), 프레임당 1회만 Load를 시도한다.
        /// 프리팹은 Instantiate하지 않고 MeshFilter.sharedMesh만 꺼낸다 - 임포터가 붙였을 콜라이더가
        /// 씬에 구조적으로 들어올 수 없다.
        /// </summary>
        private static void ProbeSingleMeshModels(string[] resourcePaths, Mesh[] meshes, ref int probeFrame)
        {
            bool anyMissing = false;
            for (int i = 0; i < meshes.Length; i++)
            {
                if (meshes[i] == null)
                    anyMissing = true;
            }

            if (!anyMissing || probeFrame == Time.frameCount)
                return;

            probeFrame = Time.frameCount;
            for (int i = 0; i < meshes.Length; i++)
            {
                if (meshes[i] != null)
                    continue;

                // OBJ는 GameObject로 온다. 메시는 루트 또는 그 자식의 MeshFilter.sharedMesh다
                // (Unity의 OBJ 임포터는 `o` 그룹을 자식으로 만들 수도, 루트에 얹을 수도 있다).
                var prefab = Resources.Load<GameObject>(resourcePaths[i]);
                if (prefab == null)
                    continue;

                var filter = prefab.GetComponent<MeshFilter>();
                if (filter == null || filter.sharedMesh == null)
                    filter = prefab.GetComponentInChildren<MeshFilter>(true);
                if (filter != null)
                {
                    meshes[i] = filter.sharedMesh;
                    RegisterCollisionHull(resourcePaths[i], filter.sharedMesh);
                }
            }
        }

        // ── [B51] 콜라이더 전용 저폴리 헐 ──────────────────────────────────────────
        //   PhysX의 convex cook은 256폴리곤이 상한이라, 3,000면대 렌더 메시를 그대로 물리면
        //   "Couldn't create a Convex Mesh ... The partial hull will be used" 경고와 함께
        //   **부분 헐**로 잘린다(스모크 테스트가 잡았다 - 경고 6건). 그래서 바위·대형 석재는
        //   <모델명>_col.obj(볼록 헐, 220면 이하)를 따로 두고 콜라이더는 그것만 쓴다.
        //   _col이 없으면 예전처럼 렌더 메시를 그대로 쓴다(폴백 - 작은 모델은 문제없다).
        //   매핑은 도메인 리로드로 비워져도 프로브가 다시 채운다(실패 비영구 규칙과 동일).
        private static readonly Dictionary<Mesh, Mesh> collisionHullByRenderMesh = new Dictionary<Mesh, Mesh>();

        private static void RegisterCollisionHull(string renderPath, Mesh renderMesh)
        {
            if (renderMesh == null || collisionHullByRenderMesh.ContainsKey(renderMesh))
                return;

            var hullPrefab = Resources.Load<GameObject>(renderPath + "_col");
            if (hullPrefab == null)
                return; // 헐이 없는 모델(덤불·풀·표류물 등)은 렌더 메시 폴백

            var hullFilter = hullPrefab.GetComponent<MeshFilter>();
            if (hullFilter == null || hullFilter.sharedMesh == null)
                hullFilter = hullPrefab.GetComponentInChildren<MeshFilter>(true);
            if (hullFilter != null && hullFilter.sharedMesh != null)
                collisionHullByRenderMesh[renderMesh] = hullFilter.sharedMesh;
        }

        /// <summary>렌더 메시에 대응하는 콜라이더 전용 헐(없으면 null). AddRockCollider가 쓴다.</summary>
        internal static Mesh GetCollisionHull(Mesh renderMesh)
        {
            if (renderMesh == null)
                return null;
            Mesh hull;
            return collisionHullByRenderMesh.TryGetValue(renderMesh, out hull) ? hull : null;
        }

        // ── [B48] 야자수 실물 모델 ──────────────────────────────────────────────────
        //   위 바위 로더와 **같은 패턴**이다(프레임당 1회 프로브 · 실패를 영구 캐시하지 않음 ·
        //   프리팹을 Instantiate하지 않고 공유 메시만 꺼냄 · 폴백 경로 유지).
        //   다른 점은 하나뿐이다: 야자수 OBJ는 `o` 오브젝트가 **2개**(줄기 + 크라운)라 머티리얼이
        //   둘(갈색 껍질 / 초록 잎)이고, 메시도 두 장을 꺼내야 한다.

        /// <summary>모델 에셋 경로(Resources 기준, 확장자 없음 - 붙이면 항상 null이 돌아온다).
        /// [B50] palm_d(어린 3.29m)·palm_e(노목 잎13장 7.41m)·palm_f(V왕관 잎8장 6.52m) 3종 추가.
        /// 전부 `o` 2개(palm?_trunk / palm?_crown)라 TryLoadTwoPartModel의 이름 규칙에 그대로 걸린다.</summary>
        private static readonly string[] PalmModelResourcePaths =
        {
            "Models/palm_a", "Models/palm_b", "Models/palm_c",
            "Models/palm_d", "Models/palm_e", "Models/palm_f"
        };

        /// <summary>각 모델의 실측 전체 높이(m, 밑면 y=0 기준). 위 경로와 인덱스가 일대일로 대응한다.</summary>
        private static readonly float[] PalmModelHeights = { 5.295f, 6.789f, 7.954f, 3.291f, 7.406f, 6.521f };

        private static readonly Mesh[] palmTrunkMeshes = new Mesh[6];
        private static readonly Mesh[] palmCrownMeshes = new Mesh[6];
        private static int palmModelProbeFrame = -1;

        /// <summary>
        /// 목표 높이에 맞는 야자수 모델의 **공유 메시 두 장**(줄기 / 크라운)을 돌려준다.
        /// 하나도 못 찾으면 false이고, 그때 호출부는 예전 절차 메시로 돌아간다.
        ///
        /// [로드 규칙] TryGetRockModel과 동일하다 - Resources.Load를 정적/필드 초기자에서 부르지 않고,
        /// 실패를 영구히 캐시하지 않는다(그 null을 굳히면 세션 내내 절차 야자수만 나온다,
        /// AGENT_BRIEF 4장 3번). 성공할 때까지 **프레임당 한 번만** 다시 살핀다 - 섬 하나가 야자수를
        /// 최대 80그루 만들므로 프레임 가드가 없으면 한 프레임에 Load가 수백 번 불린다.
        ///
        /// [B50 변종 선택 - 2단계] 난수는 여전히 0회다. 예전 "최근접 높이 1개"는 6종을 넣어도 높이
        /// 구간마다 같은 변종만 나오게 하므로, 목표 높이(4.6~7.6m) ±35% 후보 → 위치 해시 선택으로
        /// 바꿨다(근거·해시는 TryGetRockModel / DecorationPositionHash 주석). 균등 배율은 밴드 정의상
        /// 0.74~1.54 안이다(최악은 어린 palm_d 3.29m가 4.6m 목표에 뽑히는 1.40배 - 균등 배율이라
        /// 비례는 유지된다). 같은 높이 목표라도 위치가 다르면 다른 변종이 나와, 노목(잎 13장)과
        /// V왕관(잎 8장)이 실제로 숲에 섞인다.
        /// </summary>
        private static bool TryGetPalmModel(float targetHeight, Vector3 worldPosition,
            out Mesh trunk, out Mesh crown, out float modelHeight)
        {
            trunk = null;
            crown = null;
            modelHeight = 1f;

            bool anyMissing = false;
            for (int i = 0; i < palmTrunkMeshes.Length; i++)
            {
                if (palmTrunkMeshes[i] == null)
                    anyMissing = true;
            }

            if (anyMissing && palmModelProbeFrame != Time.frameCount)
            {
                palmModelProbeFrame = Time.frameCount;
                for (int i = 0; i < palmTrunkMeshes.Length; i++)
                {
                    if (palmTrunkMeshes[i] != null)
                        continue;

                    // `o` 2개짜리 OBJ에서 줄기/잎 메시를 갈라 꺼내는 공용 로더(자원 노드의 대나무와 공유).
                    Mesh loadedTrunk, loadedCrown;
                    if (!ResourceVisualLibrary.TryLoadTwoPartModel(PalmModelResourcePaths[i], out loadedTrunk, out loadedCrown))
                        continue;

                    palmTrunkMeshes[i] = loadedTrunk;
                    palmCrownMeshes[i] = loadedCrown;
                }
            }

            int pick = PickVariantByPosition(PalmModelHeights, palmTrunkMeshes, targetHeight,
                worldPosition, PalmVariantSalt);
            if (pick < 0)
                return false;

            trunk = palmTrunkMeshes[pick];
            crown = palmCrownMeshes[pick];
            modelHeight = PalmModelHeights[pick];
            return trunk != null;
        }

        // ── [B50] 덤불 / 풀 / 표류물 실물 모델 ──────────────────────────────────────
        //   바위 로더와 같은 패턴(공용 ProbeSingleMeshModels: 프레임당 1회 프로브 · 실패 비영구 ·
        //   sharedMesh만 · Instantiate 없음)이고, 변종 선택도 같은 2단계 규칙이다. 폴백(절차 메시)은
        //   전부 유지한다 - 임포트 전·프로브 실패에서 장식이 사라지면 안 된다.
        //   ★ 좌표 규약 주의 ★ 셋 다 접지 중심 원점(밑면 y=0 · X/Z 중심 · 미터)이다. 구 규격
        //   [-0.5,0.5]^3이던 절차 메시와 달리 "중심을 반높이만큼 올리는" 보정을 하면 공중에 뜬다 -
        //   특히 풀은 호출부의 groundPosition + up*(height*0.35) 보정을 모델 경로에서 빼야 한다
        //   (CreateGrassTuft 주석).

        /// <summary>덤불 모델 경로. `o` 1개 / 텍스처는 기존 런타임 "leaf"를 그대로 쓴다(머티리얼 신규 0).</summary>
        private static readonly string[] BushModelResourcePaths =
        {
            "Models/bush_a", "Models/bush_b"
        };

        /// <summary>덤불 실측 크기(m, W×H×D). 게임 목표 폭 1.3~2.2m를 폭 기준으로 fit한다.</summary>
        private static readonly Vector3[] BushModelSizes =
        {
            new Vector3(1.60f, 0.75f, 1.45f),
            new Vector3(2.10f, 0.95f, 1.90f),
        };

        private static readonly Mesh[] bushModelMeshes = new Mesh[2];
        private static int bushModelProbeFrame = -1;

        /// <summary>[B50] 목표 폭 ±35% 후보 → 위치 해시로 덤불 모델을 고른다. 없으면 false(폴백 경로).</summary>
        private static bool TryGetBushModel(float targetWidth, Vector3 worldPosition, out Mesh mesh, out Vector3 size)
        {
            mesh = null;
            size = Vector3.one;

            ProbeSingleMeshModels(BushModelResourcePaths, bushModelMeshes, ref bushModelProbeFrame);

            int pick = PickVariantByPosition(BushModelSizes, bushModelMeshes, targetWidth,
                worldPosition, BushVariantSalt);
            if (pick < 0)
                return false;

            mesh = bushModelMeshes[pick];
            size = BushModelSizes[pick];
            return true;
        }

        /// <summary>풀포기 모델 경로. `o` 1개 / 텍스처는 기존 런타임 "leaf" 그대로다.</summary>
        private static readonly string[] GrassModelResourcePaths =
        {
            "Models/grass_a", "Models/grass_b"
        };

        /// <summary>풀포기 실측 크기(m, W×H×D). 게임 목표 폭 0.32~0.62m를 폭 기준으로 fit한다.</summary>
        private static readonly Vector3[] GrassModelSizes =
        {
            new Vector3(0.46f, 0.34f, 0.42f),
            new Vector3(0.62f, 0.45f, 0.56f),
        };

        private static readonly Mesh[] grassModelMeshes = new Mesh[2];
        private static int grassModelProbeFrame = -1;

        /// <summary>[B50] 목표 폭 ±35% 후보 → 위치 해시로 풀 모델을 고른다. 없으면 false(폴백 경로).</summary>
        private static bool TryGetGrassModel(float targetWidth, Vector3 worldPosition, out Mesh mesh, out Vector3 size)
        {
            mesh = null;
            size = Vector3.one;

            ProbeSingleMeshModels(GrassModelResourcePaths, grassModelMeshes, ref grassModelProbeFrame);

            int pick = PickVariantByPosition(GrassModelSizes, grassModelMeshes, targetWidth,
                worldPosition, GrassVariantSalt);
            if (pick < 0)
                return false;

            mesh = grassModelMeshes[pick];
            size = GrassModelSizes[pick];
            return true;
        }

        /// <summary>표류물 모델 경로. 인덱스는 CreateDriftItem의 종류(index%3: 궤짝/통/널판)와 일대일이다.</summary>
        private static readonly string[] DriftModelResourcePaths =
        {
            "Models/crate_a", "Models/barrel_a", "Models/plankpile_a"
        };

        /// <summary>표류물 실측 크기(m, W×H×D). 절차 메시 시절의 미터 크기와 **정확히 같게** 구워져 있어
        /// (0.82×0.66×0.74 / 0.60×0.86×0.60 / 2.10×0.22×0.86), 명세대로 fit 없이 배율 지터
        /// 0.85~1.25만 곱한다(지터는 기존 rng draw - 새 추첨 없음).</summary>
        private static readonly Vector3[] DriftModelSizes =
        {
            new Vector3(0.82f, 0.66f, 0.74f),
            new Vector3(0.60f, 0.86f, 0.60f),
            new Vector3(2.10f, 0.22f, 0.86f),
        };

        private static readonly Mesh[] driftModelMeshes = new Mesh[3];
        private static int driftModelProbeFrame = -1;

        /// <summary>[B50] 종류(궤짝/통/널판)당 모델이 1개라 크기 선택·해시가 없다. 없으면 false(폴백 경로).</summary>
        private static bool TryGetDriftModel(int kind, out Mesh mesh, out Vector3 size)
        {
            mesh = null;
            size = Vector3.one;

            ProbeSingleMeshModels(DriftModelResourcePaths, driftModelMeshes, ref driftModelProbeFrame);

            int i = Mathf.Abs(kind) % DriftModelResourcePaths.Length;
            if (driftModelMeshes[i] == null)
                return false;

            mesh = driftModelMeshes[i];
            size = DriftModelSizes[i];
            return true;
        }

        // ── [B51] 대형 석재 7종 (rock_mega / rock_rubble / rock_stack / rock_cliff) ──
        //   바위 로더와 같은 패턴(공용 ProbeSingleMeshModels: 프레임당 1회 프로브 · 실패 비영구 ·
        //   sharedMesh만 · Instantiate 없음)이다. 좌표 계약도 같다: 미터 · 밑면 y=0(단, 절벽 2종만
        //   y=-0.5까지 내려간다 - 경사지에 얹을 때 모서리가 뜨지 않게 하는 여유분이라, 호출부는
        //   지표 높이에 그대로 놓으면 된다) · X/Z 중심 · rock.png 박스 UV.
        //
        //   ★ 기존 바위(rock_a~e)와 달리 **절차 폴백이 없다** ★ rock_a~e는 "임포트 전/프로브 실패에서
        //   바위가 사라지면 안 되는" 기존 장식의 교체라 폴백을 유지했지만, 이 7종은 0.2.08 신규
        //   장식이다 - 모델이 없으면 그 개체를 **아예 배치하지 않는 것**이 올바른 폴백이다
        //   (절차 근사를 새로 만들면 "모델이 로드됐는지"를 화면만 보고 판별할 수 없게 된다).
        //   프로브 실패는 여전히 비영구다(다음 프레임에 다시 살핀다 - AGENT_BRIEF 4장 3번).

        /// <summary>대형 석재 모델 경로(Resources 기준, 확장자 없음). 인덱스는 아래 StoneModel* 상수와 일대일.</summary>
        private static readonly string[] StoneModelResourcePaths =
        {
            "Models/rock_mega_a", "Models/rock_mega_b",
            "Models/rock_rubble_a", "Models/rock_rubble_b",
            "Models/rock_stack_a",
            "Models/rock_cliff_a", "Models/rock_cliff_b",
        };

        // StoneModelResourcePaths의 이름 붙은 인덱스. 호출부(PlaceLargeStones)가 종류별 교대 선택에 쓴다.
        private const int StoneMegaA = 0;
        private const int StoneMegaB = 1;
        private const int StoneRubbleA = 2;
        private const int StoneRubbleB = 3;
        private const int StoneStackA = 4;
        private const int StoneCliffA = 5;
        private const int StoneCliffB = 6;

        /// <summary>각 모델의 실측 크기(m, W×H×D - 디스크 OBJ 정점에서 검산했다. 절벽 2종의 H는
        /// y=-0.5 여유분을 포함한 전고이고, 지상 노출 높이는 5.40/6.20이다).</summary>
        private static readonly Vector3[] StoneModelSizes =
        {
            new Vector3(3.60f, 4.10f, 3.30f), // mega_a 둥근 거암(꼭대기 평탄면 - 올라서기)
            new Vector3(3.20f, 5.20f, 2.90f), // mega_b 모난 거석
            new Vector3(2.80f, 0.45f, 2.40f), // rubble_a 흩어진 판형(밟고 지나감 - 콜라이더 없음)
            new Vector3(2.20f, 0.75f, 1.95f), // rubble_b 무더기형(〃)
            new Vector3(2.60f, 3.30f, 2.35f), // stack_a 겹바위(랜드마크)
            new Vector3(8.50f, 5.90f, 4.20f), // cliff_a 일자 단애(+Z가 수직면 - 내리막/해안 쪽)
            new Vector3(9.50f, 6.70f, 4.60f), // cliff_b 굽은 단애(凹면이 공터를 만든다)
        };

        private static readonly Mesh[] stoneModelMeshes = new Mesh[7];
        private static int stoneModelProbeFrame = -1;

        /// <summary>
        /// [B51] 지정한 인덱스(StoneMegaA 등)의 대형 석재 공유 메시를 돌려준다. 그 모델이 아직 로드되지
        /// 않았으면 false - 호출부는 그 개체를 건너뛴다(위 주석: 신규 장식이라 절차 폴백이 없다).
        /// 종류·교대가 호출부에서 확정되므로 크기 후보·위치 해시 선택은 여기 없다(표류물 로더와 같은 꼴).
        /// </summary>
        private static bool TryGetStoneModel(int index, out Mesh mesh, out Vector3 size)
        {
            mesh = null;
            size = Vector3.one;

            ProbeSingleMeshModels(StoneModelResourcePaths, stoneModelMeshes, ref stoneModelProbeFrame);

            if (index < 0 || index >= stoneModelMeshes.Length || stoneModelMeshes[index] == null)
                return false;

            mesh = stoneModelMeshes[index];
            size = StoneModelSizes[index];
            return true;
        }


        // ── [B55] 육상 암층 12종 (rockform_a~l) ────────────────────────────────────
        //   로드 규칙은 위 대형 석재 7종과 **완전히 같다**(공용 ProbeSingleMeshModels: 필드 초기자에서
        //   Resources.Load 금지 · 실패를 영구 캐시하지 않고 프레임당 1회만 다시 살핌 · 프리팹을
        //   Instantiate하지 않고 sharedMesh만 꺼냄). 좌표 계약도 같다: 미터 · 밑면 y=0 · X/Z 중심.
        //
        //   ★ 기존 바위(rock_a~e)의 일반 풀에 **절대 섞지 않는다** ★ 제작 담당 경고 그대로다 -
        //   TryGetRockModel은 "목표 폭 ±35% 후보"로 고르는 소품용 로더라, 여기에 7m 첨탑(b)이나
        //   6.2m 아치(a)를 넣으면 폭만 맞고 높이는 3배인 모델이 소품 자리에 꽂힌다. 그래서 대형 석재
        //   7종과 마찬가지로 **인덱스 지정 전용 로더**(TryGetRockformModel)를 따로 둔다 - 종류·계층은
        //   전부 호출부(Vegetation.PlaceRockforms)가 확정하므로 크기 후보·위치 해시 선택이 여기 없다.
        //
        //   ★ 절차 폴백 없음 ★ 대형 석재 7종과 같은 이유다(신규 장식이라 "없으면 없는 것"이 폴백).
        //
        //   ★ _col 헐이 없다 ★ rockform_*.obj에는 <모델명>_col.obj가 없으므로 GetCollisionHull은 항상
        //   null을 돌려준다. 그래서 호출부는 AddRockCollider(convex)가 아니라 **비볼록 MeshCollider**를
        //   쓴다(Vegetation.AddRockformCollider). 관통형(a 자연아치 · k 쐐기바위)은 볼록 헐이면 구멍이
        //   메워지고, 나머지도 정점 수가 PhysX convex cook 상한(255)을 넘어 부분 헐로 잘리기 때문이다
        //   (b 258정점 ~ g 3,655정점 - 전부 상한 초과. e/j만 예외지만 규칙을 갈라 둘 이유가 없다).

        /// <summary>암층 모델 경로(Resources 기준, 확장자 없음). 인덱스는 아래 Rockform* 상수와 일대일.</summary>
        private static readonly string[] RockformModelResourcePaths =
        {
            "Models/rockform_a", "Models/rockform_b", "Models/rockform_c", "Models/rockform_d",
            "Models/rockform_e", "Models/rockform_f", "Models/rockform_g", "Models/rockform_h",
            "Models/rockform_i", "Models/rockform_j", "Models/rockform_k", "Models/rockform_l",
        };

        // 이름 붙은 인덱스. 호출부가 계층(피복/중형/랜드마크)별 배열에 이 상수를 담아 쓴다.
        internal const int RockformArch = 0;       // a 자연아치   6.20×4.30×2.10 (관통 개구 2.5m)
        internal const int RockformSpire = 1;      // b 첨탑       2.20×7.00×1.85
        internal const int RockformColumns = 2;    // c 주상절리   4.00×5.00×3.40
        internal const int RockformMushroom = 3;   // d 버섯바위   3.50×3.10×3.30
        internal const int RockformBedded = 4;     // e 층리 슬랩  3.90×3.00×3.15
        internal const int RockformCracked = 5;    // f 균열 거석  3.20×4.50×2.90
        internal const int RockformErratics = 6;   // g 표석 군집  6.00×2.10×4.40
        internal const int RockformTilted = 7;     // h 기울어진 판석 2.60×3.50×2.30
        internal const int RockformHoneycomb = 8;  // i 벌집풍화암 2.50×2.10×2.45
        internal const int RockformSteps = 9;      // j 계단 노두  5.00×2.00×4.20 (단높이 0.52m - 오를 수 있음)
        internal const int RockformWedge = 10;     // k 쐐기바위   4.00×2.90×1.90 (관통 1.7m)
        internal const int RockformShelf = 11;     // l 낮은 노두판 7.00×1.20×5.50

        /// <summary>암층 모델의 개수. 위 경로 배열/아래 크기표/호출부 가중치 표의 길이가 전부 이 값이다.</summary>
        internal const int RockformCount = 12;

        /// <summary>각 모델의 실측 크기(m, W×H×D). 디스크 OBJ 정점에서 검산했다(전부 밑면 y=0).
        /// 호출부는 이 H를 **매립 깊이 계산**에 쓰므로(비율 매립), 값이 틀리면 바위가 뜨거나 통째로 잠긴다.</summary>
        private static readonly Vector3[] RockformModelSizes =
        {
            new Vector3(6.20f, 4.30f, 2.10f), // a 자연아치
            new Vector3(2.20f, 7.00f, 1.85f), // b 첨탑
            new Vector3(4.00f, 5.00f, 3.40f), // c 주상절리 다발
            new Vector3(3.50f, 3.10f, 3.30f), // d 버섯바위
            new Vector3(3.90f, 3.00f, 3.15f), // e 층리 슬랩
            new Vector3(3.20f, 4.50f, 2.90f), // f 균열 거석
            new Vector3(6.00f, 2.10f, 4.40f), // g 표석 군집
            new Vector3(2.60f, 3.50f, 2.30f), // h 기울어진 판석
            new Vector3(2.50f, 2.10f, 2.45f), // i 벌집풍화암
            new Vector3(5.00f, 2.00f, 4.20f), // j 계단 노두
            new Vector3(4.00f, 2.90f, 1.90f), // k 쐐기바위
            new Vector3(7.00f, 1.20f, 5.50f), // l 낮은 노두판
        };

        private static readonly Mesh[] rockformModelMeshes = new Mesh[RockformCount];
        private static int rockformModelProbeFrame = -1;

        /// <summary>
        /// [B55] 지정한 인덱스(RockformArch 등)의 암층 공유 메시를 돌려준다. 아직 로드되지 않았으면
        /// false - 호출부는 그 개체를 배치하지 않는다(대형 석재 7종과 같은 규약: 절차 폴백 없음).
        /// </summary>
        internal static bool TryGetRockformModel(int index, out Mesh mesh, out Vector3 size)
        {
            mesh = null;
            size = Vector3.one;

            ProbeSingleMeshModels(RockformModelResourcePaths, rockformModelMeshes, ref rockformModelProbeFrame);

            if (index < 0 || index >= rockformModelMeshes.Length || rockformModelMeshes[index] == null)
                return false;

            mesh = rockformModelMeshes[index];
            size = RockformModelSizes[index];
            return true;
        }

        /// <summary>
        /// [B29] 큰 바위 / 곁에 붙는 작은 덩어리의 공유 메시(구 규격 [-0.5,0.5]^3).
        ///
        /// large면 정이십면체를 한 번 소분할한 80면, 아니면 20면이다. 면 수를 크기에 맞춰 나누는 이유는
        /// ArtDirection 2장의 디테일 밀도 규칙 그대로다 - 3m짜리 바위는 화면을 크게 차지하니 20면이면
        /// 실루엣이 각져 보이고, 0.8m짜리 곁돌은 80면을 줘도 화면에 도달하지 않는다.
        /// 변주는 4종뿐이고 전부 정적 캐시라, 섬 9개의 바위 전부가 이 8장을 나눠 쓴다.
        ///
        /// [B45] large=true 경로는 이제 **폴백 전용**이다(모델이 없을 때만 큰 덩어리에 쓰인다).
        /// large=false(곁돌)는 예전 그대로 항상 이 메시를 쓴다 - 지우지 마라, 두 경로 다 살아 있어야 한다.
        /// </summary>
        private static Mesh GetBoulderMesh(int variant, bool large)
        {
            int v = Mathf.Abs(variant) % 4;
            string key = (large ? "boulder" : "rockChip") + v;
            Mesh cached;
            if (decorationMeshCache.TryGetValue(key, out cached) && cached != null)
                return cached;

            Mesh mesh = WorldMeshBuilder.Chunk("Deco_" + key, Vector3.one,
                large ? 7100 + v * 13 : 7700 + v * 19, large ? 0.32f : 0.44f, large ? 1 : 0);
            decorationMeshCache[key] = mesh;
            return mesh;
        }

        /// <summary>
        /// [B29] 표류 궤짝(구 규격 [-0.5,0.5]^3). 몸통 상자 하나에 모서리 기둥 4개와 결속 띠 1개를
        /// 겹쳐 구웠다 - 널판 사이의 홈이 실루엣이 아니라 그림자로 읽히는 것이 목표라 파츠로 나눌 이유가 없다.
        /// 자연물과 인공물을 가르는 신호(ArtDirection 2장 4번)인 "각진 모서리 + 결속"을 그대로 쓴다.
        /// </summary>
        private static Mesh GetCrateMesh()
        {
            Mesh cached;
            if (decorationMeshCache.TryGetValue("crate", out cached) && cached != null)
                return cached;

            var builder = new WorldMeshBuilder();
            builder.AddBox(Vector3.zero, new Vector3(0.86f, 0.86f, 0.86f), Quaternion.identity);
            for (int i = 0; i < 4; i++)
            {
                float x = (i < 2 ? -1f : 1f) * 0.43f;
                float z = (i % 2 == 0 ? -1f : 1f) * 0.43f;
                builder.AddBox(new Vector3(x, 0f, z), new Vector3(0.14f, 1.0f, 0.14f), Quaternion.identity);
            }
            builder.AddBox(new Vector3(0f, 0.06f, 0f), new Vector3(1.0f, 0.13f, 0.92f), Quaternion.identity);
            builder.AddBox(new Vector3(0f, 0.06f, 0f), new Vector3(0.92f, 0.13f, 1.0f), Quaternion.identity);

            Mesh mesh = builder.Finish("Deco_Crate");
            decorationMeshCache["crate"] = mesh;
            return mesh;
        }

        /// <summary>
        /// [B29] 표류 통(구 규격 [-0.5,0.5]^3). 배가 부른 옆선과 테 2줄을 **반지름 변화만으로** 만든다
        /// (B28에서 대나무 마디를 원반 파츠 → 줄기 굵기 변화로 바꾼 것과 같은 처리다).
        /// 8각이라 옆면이 각져서, 같은 자리에 있는 바위(둥근 덩어리)와 실루엣이 겹치지 않는다.
        /// </summary>
        private static Mesh GetBarrelMesh()
        {
            Mesh cached;
            if (decorationMeshCache.TryGetValue("barrel", out cached) && cached != null)
                return cached;

            float[] heights = { -0.50f, -0.46f, -0.30f, -0.26f, 0f, 0.26f, 0.30f, 0.46f, 0.50f };
            float[] radii = { 0.355f, 0.395f, 0.445f, 0.480f, 0.500f, 0.480f, 0.445f, 0.395f, 0.355f };

            var centers = new Vector3[heights.Length];
            for (int i = 0; i < heights.Length; i++)
                centers[i] = new Vector3(0f, heights[i], 0f);

            var builder = new WorldMeshBuilder();
            builder.AddTube(centers, radii, 8, true, true, 1f);

            Mesh mesh = builder.Finish("Deco_Barrel");
            decorationMeshCache["barrel"] = mesh;
            return mesh;
        }

        /// <summary>
        /// [B29] 밀려온 널판 더미(구 규격 [-0.5,0.5]^3, 호출부가 2.1m × 0.22m × 0.86m로 늘린다).
        /// 널판 3장을 서로 다른 각도로 겹쳐 한 메시에 구웠다 - 판이 어긋나 쌓인 그림이라야 "쌓아 둔 것"이
        /// 아니라 "밀려와 걸린 것"으로 읽힌다.
        /// </summary>
        private static Mesh GetPlankPileMesh()
        {
            Mesh cached;
            if (decorationMeshCache.TryGetValue("plankPile", out cached) && cached != null)
                return cached;

            var builder = new WorldMeshBuilder();
            builder.AddBox(new Vector3(0.02f, -0.33f, -0.22f), new Vector3(0.96f, 0.32f, 0.30f),
                Quaternion.Euler(0f, 5f, 0f));
            builder.AddBox(new Vector3(-0.03f, 0f, 0.10f), new Vector3(0.90f, 0.32f, 0.28f),
                Quaternion.Euler(0f, -8f, 0f));
            builder.AddBox(new Vector3(0.05f, 0.33f, -0.05f), new Vector3(0.78f, 0.30f, 0.26f),
                Quaternion.Euler(0f, 13f, 4f));

            Mesh mesh = builder.Finish("Deco_PlankPile");
            decorationMeshCache["plankPile"] = mesh;
            return mesh;
        }

        /// <summary>[B29] 장식물(덤불 포기·바위·표류물) 공유 메시 캐시. 월드 전체가 15장 안팎을 나눠 쓴다.</summary>
        private static readonly Dictionary<string, Mesh> decorationMeshCache = new Dictionary<string, Mesh>();

        /// <summary>
        /// 야자수 줄기 마디용 저폴리 프리즘(8각, 28삼각형). 내장 Cylinder 80삼각형의 35%.
        ///
        /// 규격은 **내장 Cylinder와 완전히 동일**하다 - 정점이 반지름 0.5 원 위에 놓이고 높이는 y = -1~+1.
        /// 그래야 CreatePalm의 스케일 식 (segmentRadius*2, segmentLength*0.53, segmentRadius*2)의 의미가
        /// 한 글자도 바뀌지 않는다(굵기 보정은 스케일 식이 아니라 baseRadius 범위에서 한다 - 그쪽 주석 참고).
        ///
        /// 삼각형 내역: 옆면 8×2 = 16, 캡 2×(8-2) = 12 → 28. 캡은 중심 정점 없이 모서리에서 부채꼴로
        /// 감아(=n-2개) 삼각형을 아낀다. 캡을 아예 빼면 12삼각형까지 내려가지만, 마디가 6% 겹쳐 있다는
        /// 전제가 깨지는 순간(기울기 누적이 커지면 이음매가 벌어질 수 있다) 줄기 속이 뚫려 보이므로 남긴다.
        ///
        /// 옆면 법선은 반경 방향으로 **직접 지정**한다. RecalculateNormals에 맡기면 정점을 면마다 나눈
        /// 구조가 아니어도 면 법선이 평균돼 결과가 파이프라인 버전에 의존하고, 무엇보다 평면 셰이딩이
        /// 되면 이웃 면 사이 45° 법선 단차가 그대로 밝기 단차로 나온다(PalmTrunkSides 주석의 근거).
        /// 캡은 정점을 따로 두고 ±Y 법선을 줘 옆면과 섞이지 않게 한다.
        /// </summary>
        private static Mesh GetPalmTrunkPrismMesh()
        {
            if (palmTrunkPrismMesh != null)
                return palmTrunkPrismMesh;

            const int sides = PalmTrunkSides;
            const float radius = 0.5f;

            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector2>();

            // 옆면: 이음매 정점을 한 번 더 둬서 UV가 한 바퀴 돌 때 되감기지 않게 한다.
            int sideStart = vertices.Count;
            for (int i = 0; i <= sides; i++)
            {
                float angle = (float)i / sides * Mathf.PI * 2f;
                var radial = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                Vector3 p = radial * radius;
                float u = (float)i / sides;

                vertices.Add(new Vector3(p.x, -1f, p.z));
                normals.Add(radial);
                uvs.Add(new Vector2(u, 0f));

                vertices.Add(new Vector3(p.x, 1f, p.z));
                normals.Add(radial);
                uvs.Add(new Vector2(u, 1f));
            }

            int topStart = vertices.Count;
            for (int i = 0; i < sides; i++)
            {
                float angle = (float)i / sides * Mathf.PI * 2f;
                var p = new Vector3(Mathf.Cos(angle) * radius, 1f, Mathf.Sin(angle) * radius);
                vertices.Add(p);
                normals.Add(Vector3.up);
                uvs.Add(new Vector2(p.x + 0.5f, p.z + 0.5f));
            }

            int bottomStart = vertices.Count;
            for (int i = 0; i < sides; i++)
            {
                float angle = (float)i / sides * Mathf.PI * 2f;
                var p = new Vector3(Mathf.Cos(angle) * radius, -1f, Mathf.Sin(angle) * radius);
                vertices.Add(p);
                normals.Add(Vector3.down);
                uvs.Add(new Vector2(p.x + 0.5f, p.z + 0.5f));
            }

            Vector3[] positions = vertices.ToArray();
            var triangles = new List<int>();

            for (int i = 0; i < sides; i++)
            {
                int b0 = sideStart + i * 2;
                int t0 = b0 + 1;
                int b1 = b0 + 2;
                int t1 = b0 + 3;

                float mid = ((float)i + 0.5f) / sides * Mathf.PI * 2f;
                var outward = new Vector3(Mathf.Cos(mid), 0f, Mathf.Sin(mid));
                AddOrientedTriangle(triangles, positions, b0, t0, t1, outward);
                AddOrientedTriangle(triangles, positions, b0, t1, b1, outward);
            }

            for (int i = 1; i < sides - 1; i++)
            {
                AddOrientedTriangle(triangles, positions, topStart, topStart + i, topStart + i + 1, Vector3.up);
                AddOrientedTriangle(triangles, positions, bottomStart, bottomStart + i, bottomStart + i + 1, Vector3.down);
            }

            var mesh = new Mesh { name = "Veg_PalmTrunkPrism" };
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds(); // 법선은 위에서 직접 넣었으므로 RecalculateNormals를 부르면 안 된다.

            palmTrunkPrismMesh = mesh;
            return palmTrunkPrismMesh;
        }

        /// <summary>
        /// 삼각형 하나를 감김 방향까지 맞춰 넣는다. 기하 법선이 reference와 반대면 감김을 뒤집는다.
        /// 이 프로젝트는 왼손 좌표계라 표준 인덱스 표를 그대로 옮기면 통째로 안쪽을 향해 컬링되는
        /// 사고가 반복됐다(BuildFlatShadedMesh 주석). 표를 믿지 않고 계산으로 확정하는 방식을 그대로 쓴다.
        /// </summary>
        private static void AddOrientedTriangle(List<int> triangles, Vector3[] positions,
            int i0, int i1, int i2, Vector3 reference)
        {
            Vector3 geometric = Vector3.Cross(positions[i1] - positions[i0], positions[i2] - positions[i0]);
            if (Vector3.Dot(geometric, reference) < 0f)
            {
                int swap = i1;
                i1 = i2;
                i2 = swap;
            }

            triangles.Add(i0);
            triangles.Add(i1);
            triangles.Add(i2);
        }

        /// <summary>
        /// 풀포기용 저폴리 잎다발 = 부채꼴로 벌린 잎 5장(양면·2마디라 40삼각형). 내장 Sphere(768)의 1/19.
        ///
        /// 풀포기는 이미 "두께를 폭의 30%로 눌러 좌우로 눕힌" 형태라(B8), 눌린 구가 실제로 화면에서
        /// 하던 일은 "위로 솟은 납작한 잎 다발"이었다. 그 실루엣은 평면 조합으로 그대로 재현되고,
        /// 오히려 끝이 뾰족한 잎이 생겨 풀로 더 잘 읽힌다.
        ///
        /// 잎은 단면이라 뒷면에서 보이지 않으므로 감김을 뒤집은 사본을 함께 넣어 양면으로 만든다
        /// (양면 셰이더나 두께가 있는 상자를 쓰는 것보다 싸다). 규격은 Sphere와 같은
        /// [-0.5,0.5]^3 이라 호출부의 (width, height, width*0.30) 스케일 의미가 그대로 유지된다.
        /// </summary>
        private static Mesh GetGrassBladeMesh()
        {
            if (grassBladeMesh != null)
                return grassBladeMesh;

            var points = new List<Vector3>();
            var faces = new List<int>();

            // [B9 디렉터 수정] 잎 3장 × 폭 0.30 은 실기에서 "풀"이 아니라 **반투명 판때기**로 보였다.
            // 원인은 지오메트리가 아니라 비례다 - 호출부 스케일(폭 0.7~1.5m)에 잎이 3장뿐이라
            // 한 장이 0.5m 폭짜리 벽이 됐다. 잎을 늘리고 각각을 가늘게 해야 풀로 읽힌다.
            // 잎 5장을 0°/40°/78°/118°/155°로 벌린다. 호출부가 z를 30%로 누르므로 결과는 부채꼴이 된다.
            float[] yaws = { 0f, 40f, 78f, 118f, 155f };
            float[] tipHeights = { 0.50f, 0.34f, 0.44f, 0.30f, 0.40f };   // 끝 높이를 다르게 해 윗변이 평평해지지 않게 한다
            float[] tipOuts = { 0.30f, 0.18f, 0.38f, 0.16f, 0.26f };      // 바깥으로 벌어지는 정도

            // [B29] 잎 하나를 **2마디로 꺾어** 휘게 만들었다. 곧은 사각형 한 장은 어느 각도에서 봐도
            // 직선 윤곽이라 5장을 부채꼴로 펴도 "삐죽한 판"으로 읽혔다. 중간 마디를 바깥으로 조금만
            // 내보내면 윤곽선이 곡선이 되고 끝이 아래로 처져 풀로 읽힌다(야자수 잎을 2마디로 꺾은
            // B8의 처리와 같은 이유다 - 이 프로젝트에서 식물을 식물로 만드는 것은 언제나 꺾임이다).
            // 규격은 그대로 [-0.5,0.5]^3이라 호출부 스케일 (width, height, width*0.30)의 의미가 안 바뀐다.
            for (int i = 0; i < yaws.Length; i++)
            {
                float rad = yaws[i] * Mathf.Deg2Rad;
                Vector3 outward = new Vector3(Mathf.Sin(rad), 0f, Mathf.Cos(rad));
                Vector3 side = new Vector3(Mathf.Cos(rad), 0f, -Mathf.Sin(rad));

                // 폭: 밑동 0.10 → 중간 0.06 → 끝 0.03. 잎이 "칼날"이 아니라 "풀잎"으로 읽히는 최소 비례다
                // (높이 1.0 대비 폭 0.10 = 10:1). 이전 0.30은 3.3:1이라 판때기였다.
                float midHeight = tipHeights[i] * 0.34f;               // 꺾이는 지점(밑동과 끝 사이)
                float midOut = tipOuts[i] * 0.30f;                     // 중간은 거의 곧게 선다
                Vector3 b0 = side * -0.05f + outward * -0.03f + Vector3.down * 0.5f;
                Vector3 b1 = side * 0.05f + outward * -0.03f + Vector3.down * 0.5f;
                Vector3 m0 = side * -0.03f + outward * midOut + Vector3.up * midHeight;
                Vector3 m1 = side * 0.03f + outward * midOut + Vector3.up * midHeight;
                Vector3 t0 = side * -0.012f + outward * tipOuts[i] + Vector3.up * tipHeights[i];
                Vector3 t1 = side * 0.012f + outward * tipOuts[i] + Vector3.up * tipHeights[i];

                int b = points.Count;
                points.Add(b0); points.Add(b1); points.Add(m1); points.Add(m0);
                points.Add(t1); points.Add(t0);

                // 아래 마디(밑동 → 중간)
                faces.Add(b); faces.Add(b + 1); faces.Add(b + 2);
                faces.Add(b); faces.Add(b + 2); faces.Add(b + 3);
                // 위 마디(중간 → 끝)
                faces.Add(b + 3); faces.Add(b + 2); faces.Add(b + 4);
                faces.Add(b + 3); faces.Add(b + 4); faces.Add(b + 5);
                // 뒷면(감김 반대). 법선도 반대로 나오므로 양쪽에서 정상적으로 조명을 받는다.
                faces.Add(b); faces.Add(b + 2); faces.Add(b + 1);
                faces.Add(b); faces.Add(b + 3); faces.Add(b + 2);
                faces.Add(b + 3); faces.Add(b + 4); faces.Add(b + 2);
                faces.Add(b + 3); faces.Add(b + 5); faces.Add(b + 4);
            }

            // 잎은 닫힌 볼륨이 아니라 중심 기준 바깥 판정(ensureOutward)을 쓸 수 없다 - 감김을 그대로 둔다.
            grassBladeMesh = BuildFlatShadedMesh("Veg_GrassBlades", points.ToArray(), faces.ToArray(), false);
            return grassBladeMesh;
        }

        /// <summary>
        /// 면마다 정점을 분리한 평면 셰이딩 메시를 만든다.
        /// ensureOutward가 켜져 있으면 각 삼각형의 법선이 원점 바깥을 향하도록 감김을 바로잡는다
        /// (닫힌 볼록 다면체에만 유효하다 - 이 프로젝트는 왼손 좌표계라 표준 인덱스 표를 그대로
        /// 옮기면 안쪽을 향해 통째로 컬링되는 사고가 나기 쉬워, 표를 믿지 않고 계산으로 확정한다).
        /// UV는 XY 평면 투영이다 - 표면 그레인 텍스처를 곱하는 용도라 정밀한 전개가 필요 없다.
        /// </summary>
        private static Mesh BuildFlatShadedMesh(string meshName, Vector3[] points, int[] faces, bool ensureOutward)
        {
            var vertices = new Vector3[faces.Length];
            var uvs = new Vector2[faces.Length];
            var triangles = new int[faces.Length];

            for (int f = 0; f + 2 < faces.Length; f += 3)
            {
                Vector3 p0 = points[faces[f]];
                Vector3 p1 = points[faces[f + 1]];
                Vector3 p2 = points[faces[f + 2]];

                if (ensureOutward && Vector3.Dot(Vector3.Cross(p1 - p0, p2 - p0), (p0 + p1 + p2) / 3f) < 0f)
                {
                    Vector3 swap = p1;
                    p1 = p2;
                    p2 = swap;
                }

                vertices[f] = p0;
                vertices[f + 1] = p1;
                vertices[f + 2] = p2;
                uvs[f] = new Vector2(p0.x + 0.5f, p0.y + 0.5f);
                uvs[f + 1] = new Vector2(p1.x + 0.5f, p1.y + 0.5f);
                uvs[f + 2] = new Vector2(p2.x + 0.5f, p2.y + 0.5f);
                triangles[f] = f;
                triangles[f + 1] = f + 1;
                triangles[f + 2] = f + 2;
            }

            var mesh = new Mesh { name = meshName };
            mesh.vertices = vertices;
            mesh.uv = uvs;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static Mesh grassBladeMesh;
        private static Mesh palmTrunkPrismMesh;

        /// <summary>
        /// 도메인 리로드를 끈 플레이 모드에서 static 캐시가 이전 실행의 파괴된 메시를 들고
        /// 시작하지 않게 초기 상태로 되돌린다(이 partial 파일이 선언한 static만 다룬다).
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetMeshLibraryStaticCache()
        {
            primitiveMeshCache.Clear();
            collisionHullByRenderMesh.Clear();
            decorationMeshCache.Clear();
            System.Array.Clear(rockModelMeshes, 0, rockModelMeshes.Length);
            rockModelProbeFrame = -1;
            System.Array.Clear(palmTrunkMeshes, 0, palmTrunkMeshes.Length);
            System.Array.Clear(palmCrownMeshes, 0, palmCrownMeshes.Length);
            palmModelProbeFrame = -1;
            System.Array.Clear(bushModelMeshes, 0, bushModelMeshes.Length);
            bushModelProbeFrame = -1;
            System.Array.Clear(grassModelMeshes, 0, grassModelMeshes.Length);
            grassModelProbeFrame = -1;
            System.Array.Clear(driftModelMeshes, 0, driftModelMeshes.Length);
            driftModelProbeFrame = -1;
            System.Array.Clear(stoneModelMeshes, 0, stoneModelMeshes.Length);
            stoneModelProbeFrame = -1;
            System.Array.Clear(rockformModelMeshes, 0, rockformModelMeshes.Length);
            rockformModelProbeFrame = -1;
            grassBladeMesh = null;
            palmTrunkPrismMesh = null;
        }
    }
}
