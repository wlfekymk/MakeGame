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
        /// <summary>[B57] "거목 야자" 당첨 판정용 salt(Vegetation.cs CreatePalm).</summary>
        private const uint EmergentPalmSalt = 0x51A7B005u;
        /// <summary>[B57] 당첨된 거목의 목표 높이를 정하는 salt. 당첨 판정과 독립이어야 하므로 따로 둔다.</summary>
        private const uint EmergentPalmHeightSalt = 0x51A7B006u;
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
        ///
        /// [B57] 이 float[] 오버로드의 유일한 호출부였던 야자수는 아키타입 가중이 붙으면서
        /// PickPalmVariant로 옮겨 갔다. 지우지 않고 남겨 두는 이유는 (a) 아래 Vector3 오버로드와 쌍이라
        /// 한쪽만 없으면 "폭 기준 표는 되고 높이 기준 표는 안 된다"로 읽히고, (b) 높이 한 축으로 fit하는
        /// 다음 모델군(대나무 계열을 이 파일로 옮기는 등)이 그대로 쓸 수 있기 때문이다.
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
        /// [B57] 야자수 **전면 재제작 + 12종**. a~f는 **같은 이름으로 교체**됐고 실측 높이는 1mm도
        /// 바뀌지 않았다(아래 PalmModelHeights 앞 6개 = 예전 값 그대로 - 이 파일이 지켜야 할 계약이다.
        /// 바뀐 것은 형태와 수관 폭뿐이며, 수관이 평균 +19% 넓어진 것을 Vegetation.cs의 숲 간격이 받는다).
        /// 신규 6종: g 어린 해안목 4.320 / h 폭풍 피해(소엽 결손) 4.960 / i 쌍둥이 줄기 5.850 /
        /// j 코코넛 다량 8.600 / k C커브 9.500 / l 고목 10.400.
        /// 전부 `o` 2개(palm?_trunk / palm?_crown)라 TryLoadTwoPartModel의 이름 규칙에 그대로 걸린다.</summary>
        private static readonly string[] PalmModelResourcePaths =
        {
            "Models/palm_a", "Models/palm_b", "Models/palm_c",
            "Models/palm_d", "Models/palm_e", "Models/palm_f",
            "Models/palm_g", "Models/palm_h", "Models/palm_i",
            "Models/palm_j", "Models/palm_k", "Models/palm_l"
        };

        /// <summary>각 모델의 실측 전체 높이(m, 밑면 y=0 기준). 위 경로와 인덱스가 일대일로 대응한다.
        /// (앞 6개는 재제작 전과 동일한 값이다 - 위 경로 주석의 "높이 계약".)</summary>
        private static readonly float[] PalmModelHeights =
        {
            5.295f, 6.789f, 7.954f, 3.291f, 7.406f, 6.521f,
            4.320f, 4.960f, 5.850f, 8.600f, 9.500f, 10.400f
        };

        // [B57] 배열 크기는 경로 배열 길이에서 온다(대나무 쪽에서 먼저 쓴 규칙 - 종 수가 6→12로
        // 바뀔 때 고칠 곳을 한 군데로 묶는다). 리셋 훅(ResetMeshLibraryStaticCache)의 Array.Clear도
        // .Length를 쓰므로 크기 변경이 저절로 따라온다 - 훅에 새로 등록할 static은 없다.
        // 필드 초기자는 선언 순서대로 실행되므로 PalmModelResourcePaths(위)가 먼저 채워진다.
        private static readonly Mesh[] palmTrunkMeshes = new Mesh[PalmModelResourcePaths.Length];
        private static readonly Mesh[] palmCrownMeshes = new Mesh[PalmModelResourcePaths.Length];
        private static int palmModelProbeFrame = -1;

        /// <summary>
        /// [B57] 아키타입별 야자 변종 가중치. 행 = <see cref="IslandArchetype"/> **선언 순서** 8종
        /// (Tropical / Rocky / Sandy / Jungle / Volcanic / Atoll / Marsh / Cliff),
        /// 열 = 위 PalmModelResourcePaths의 12종(a~l). 1.0이 기준이고 크면 자주, 작으면 드물게 나온다.
        ///
        /// [왜 가중치이고 필터가 아닌가] 후보 집합은 **여전히 높이 밴드**(목표 높이 ±35%)가 먼저 정한다.
        /// 가중치는 그 후보 안에서만 확률을 기울인다 - 즉 "절벽섬에는 폭풍 피해목만"이 아니라
        /// "절벽섬에서는 폭풍 피해목이 더 자주"다. 0을 쓰지 않는 이유도 같다: 어떤 높이에서 후보가
        /// 하나뿐일 때 그 하나의 가중치가 0이면 선택이 무너진다(아래 total<=0 방어와 같은 이유).
        ///
        /// 편향 근거(제작 담당 메모 그대로): h 폭풍 피해 → 바람 맞는 절벽/바위/화산암섬,
        /// j 코코넛 다량 → 열대/정글, l 고목 → 정글(원시림), i 쌍둥이 줄기 → 정글/습지,
        /// d·g 어린 해안목 → 백사장/산호섬, k C커브(바람에 휜 줄기) → 해안 노출이 큰 백사장/절벽.
        /// rng 소비는 0이다(아래 PickPalmVariant는 위치 해시만 쓴다).
        /// </summary>
        private static readonly float[,] PalmArchetypeWeights =
        {
            //                 a     b     c     d     e     f     g     h     i     j     k     l
            /* Tropical */ { 1.0f, 1.0f, 1.0f, 1.0f, 1.0f, 1.0f, 1.0f, 0.7f, 1.0f, 2.0f, 1.0f, 1.0f },
            /* Rocky    */ { 1.0f, 0.9f, 0.8f, 1.2f, 0.7f, 1.0f, 1.2f, 2.2f, 0.8f, 0.6f, 1.4f, 0.5f },
            /* Sandy    */ { 1.2f, 1.0f, 0.9f, 1.6f, 0.8f, 1.0f, 1.8f, 1.0f, 0.7f, 1.2f, 1.8f, 0.6f },
            /* Jungle   */ { 1.0f, 1.2f, 1.4f, 0.8f, 2.0f, 1.1f, 0.7f, 0.5f, 1.8f, 1.6f, 0.7f, 2.2f },
            /* Volcanic */ { 1.0f, 0.9f, 0.8f, 1.2f, 0.7f, 1.0f, 1.1f, 2.0f, 0.7f, 0.6f, 1.2f, 0.5f },
            /* Atoll    */ { 1.2f, 1.0f, 0.8f, 1.6f, 0.7f, 1.0f, 1.8f, 1.2f, 0.7f, 1.4f, 1.6f, 0.5f },
            /* Marsh    */ { 1.0f, 1.1f, 1.1f, 1.0f, 1.4f, 1.0f, 0.9f, 0.7f, 1.8f, 1.0f, 0.7f, 1.2f },
            /* Cliff    */ { 1.0f, 1.0f, 1.0f, 1.0f, 0.9f, 1.0f, 1.0f, 2.2f, 0.8f, 0.7f, 1.8f, 0.8f },
        };

        /// <summary>가중치 표 조회(표가 짧아도 죽지 않게 1.0으로 폴백 · 음수는 0으로 자른다).</summary>
        private static float PalmVariantWeight(int archetypeRow, int variantIndex)
        {
            if (archetypeRow < 0 || archetypeRow >= PalmArchetypeWeights.GetLength(0))
                return 1f;
            if (variantIndex < 0 || variantIndex >= PalmArchetypeWeights.GetLength(1))
                return 1f;
            return Mathf.Max(0f, PalmArchetypeWeights[archetypeRow, variantIndex]);
        }

        /// <summary>
        /// [B57] 야자 전용 변종 선택. 공용 PickVariantByPosition과
        /// 1단계(목표 높이 ±<see cref="VariantSizeBand"/> 후보 수집)는 **완전히 같고**, 2단계만
        /// 균등 선택 → 아키타입 가중 선택으로 바뀐다. 공용 함수를 건드리지 않고 여기 사본을 두는 이유는
        /// 바위/덤불/풀이 같은 함수를 쓰기 때문이다(그쪽 선택이 이 변경으로 흔들리면 안 된다).
        /// rng 소비 0 - 입력은 (이미 뽑힌 목표 높이, 이미 확정된 위치, 섬이 이미 정한 아키타입)뿐이라
        /// 같은 worldSeed면 항상 같은 결과다.
        /// </summary>
        private static int PickPalmVariant(float targetHeight, Vector3 worldPosition, IslandArchetype archetype)
        {
            var candidates = variantCandidateBuffer;
            candidates.Clear();

            int nearest = -1;
            float nearestDelta = float.MaxValue;
            float band = targetHeight * VariantSizeBand;
            for (int i = 0; i < palmTrunkMeshes.Length; i++)
            {
                if (palmTrunkMeshes[i] == null)
                    continue;

                float delta = Mathf.Abs(PalmModelHeights[i] - targetHeight);
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

            // 행 폴백 규칙은 PlaceRockforms(archetypeRow)와 같다 - 알 수 없는 값은 Tropical(0행).
            int row = (int)archetype;
            if (row < 0 || row >= PalmArchetypeWeights.GetLength(0))
                row = 0;

            float total = 0f;
            for (int c = 0; c < candidates.Count; c++)
                total += PalmVariantWeight(row, candidates[c]);

            // 표가 어긋났거나 후보가 전부 0가중이면 공용 함수와 똑같이 균등 선택으로 되돌린다.
            if (total <= 0f)
                return candidates[(int)(DecorationPositionHash(worldPosition, PalmVariantSalt) % (uint)candidates.Count)];

            float roll = DecorationPositionHash01(worldPosition, PalmVariantSalt) * total;
            for (int c = 0; c < candidates.Count; c++)
            {
                roll -= PalmVariantWeight(row, candidates[c]);
                if (roll < 0f)
                    return candidates[c];
            }
            return candidates[candidates.Count - 1];
        }

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
        ///
        /// [B57] 12종이 되면서 2단계가 <see cref="PickPalmVariant"/>(아키타입 가중 선택)로 바뀌었다.
        /// 1단계 밴드는 그대로다. **키 큰 모델(j 8.6 / k 9.5 / l 10.4)** 은 밴드 상한이 목표 높이의
        /// 1.35배라, 목표 높이가 4.6~7.6m뿐이면 10.26m를 넘는 l이 영원히 후보에 못 든다 -
        /// 그래서 "목표 높이"쪽을 손봤다(Vegetation.cs CreatePalm의 emergentChance 주석).
        /// 이 함수는 목표 높이를 그대로 받기만 한다(여기서 높이를 만들지 않는다).
        /// </summary>
        private static bool TryGetPalmModel(float targetHeight, Vector3 worldPosition, IslandArchetype archetype,
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

            int pick = PickPalmVariant(targetHeight, worldPosition, archetype);
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

        // ═══════════════════════════════════════════════════════════════════════════
        //  [해변 스와시] 물가 거리장(shore SDF) 굽기 + MGShoreline 에셋 프로브
        // ═══════════════════════════════════════════════════════════════════════════
        //
        // 왜 필요한가: MGShoreline 셰이더는 파도 위상을 **해안선과 평행하게** 진행시켜야 한다
        // (만은 오목하게 밀려들고, 곶은 늦게 닿는다). 그러려면 각 지점의 "물가까지의 수평 거리"가
        // 필요한데, 이 프로젝트는 섬을 절차 생성하므로 **지형 생성 시점에 정점별로 한 번 구워** 두면
        // 런타임 비용이 0이 된다(셰이더는 보간된 UV2를 읽기만 한다).
        //
        // ★ 근사가 아니라 실제 등고선 거리를 쓴 이유 ★
        // 가장 싼 근사는 "정점 y ÷ 해변 경사"로 수평 거리를 환산하는 것이다. 실측(아래 값)으로
        // 해변 경사가 섬마다 0.23~0.69(m/m)로 3배 벌어지고, 만/수로/석호가 있는 프로파일에서는
        // **같은 높이라도 물가가 두 방향에 있다**(수로 안쪽 물가와 바깥 바다). 높이 환산은 그 둘을
        // 구분하지 못해 수로 옆 모래가 파도 위상에서 통째로 빠진다. 반면 등고선 거리는
        //   (1) 지형 삼각형에서 y=0 등고선을 마칭 트라이앵글로 뽑고
        //   (2) 정점마다 그 선분들까지의 최소 거리를 재는
        // 두 단계뿐이고, 지형 토폴로지(중심 + 링 × 세그먼트 원판)와 무관하게 성립한다.
        // 비용은 세계 생성 1회뿐이다(아래 성능 주석).
        //
        // ★ 결정성 ★ 입력이 이미 확정된 정점 배열뿐인 **순수 기하 계산**이다. System.Random을
        // 만들지도 소비하지도 않으므로 월드 배치 재현성(같은 worldSeed = 같은 월드)에 무영향이고,
        // 세이브 포맷과도 무관하다(구운 값은 메시에만 들어간다).
        //
        // ★ 실측 값 범위(Tools/terrain/preview.py의 높이식으로 검산) ★
        //   시작 섬 R=50 · 프로파일 0 : 거리장 -13.8m ~ +37.1m, 등고선 선분 158개
        //       물가 → WetTop(0.30m) 0.43m / DampTop(0.75m) 1.30m / 1.30m 4.4m / 잔디선(1.93m) 15.3m
        //   R=90 · 프로파일 4(수로)   : -29.1m ~ +33.5m, 선분 438개 (0.70 / 2.30 / 11.7m)
        //   R=200 · 프로파일 3(초승달): -138.6m ~ +121.2m, 선분 272개 (2.48 / 7.20 / 18.8m)
        //   → 해변 경사 0.23~0.69. 스와시 도달거리 1.3m(잔잔)~2.8m(거침)가 이 폭에 정확히 얹힌다.

        /// <summary>
        /// 등고선을 하나도 못 찾았을 때 쓰는 "무한히 멀다" 값(m). 셰이더의 도달거리(최대 3m 남짓)를
        /// 한참 넘으므로 젖음/거품이 전혀 나오지 않는다 - 올바른 무동작이다.
        /// </summary>
        private const float ShoreFieldFar = 9999f;

        /// <summary>
        /// 지형 메시의 정점마다 **물가(y=0 등고선)까지의 부호 있는 수평 거리**를 구워 UV2로 돌려준다.
        ///   x = 거리(m). + = 내륙(해발), - = 물속.
        ///   y = 그 정점의 해수면 기준 높이(m). 예비 채널(셰이더는 현재 x만 쓴다).
        ///
        /// 섬은 로컬 y가 곧 해수면 기준 높이다(WorldMapManager.seaLevel = 0이고 섬 오브젝트는
        /// y = 0에 놓인다) - 그래서 등고선은 그냥 y = 0이다.
        ///
        /// 성능: 정점 V × 등고선 선분 S. 최악(R=200: V=3601, S=272)이 98만 쌍인데,
        ///   (a) 앞 정점의 결과 + 두 정점 사이 거리를 상한으로 물려받고(거리장은 1-Lipschitz라
        ///       항상 유효한 상한이다. 정점은 링/세그먼트 순서라 이웃이 3~5m 안에 있다)
        ///   (b) 선분의 중점·반길이로 만든 하한이 그 상한을 못 이기면 정확 계산을 건너뛴다
        /// 두 가지로 대부분의 쌍이 곱셈 4번에서 걸러진다. 최소 거리를 내는 선분은 하한 정의상
        /// 절대 걸러지지 않으므로 결과는 완전 탐색과 **동일하다**(근사가 아니다).
        /// 호출은 섬 하나당 1회, 월드 생성 시점뿐이다(프레임당 할당·계산 0).
        ///
        /// [해변 파도 3단계] 여기서 뽑는 **등고선 선분 집합이 곧 해안선 폴리라인**이다. 마루 리본
        /// (ShorelineRibbon)이 그것을 기준선으로 재사용하도록 contour로 함께 돌려준다 - 같은 등고선을
        /// 두 곳이 각자 뽑으면 값이 갈라질 수 있고, 이미 낸 비용을 두 번 내는 일이다.
        /// 리본이 쓰는 두 가지가 더 실린다(거리장 계산에는 쓰이지 않는다 - **UV2 결과는 불변**):
        ///   · 끝점이 놓인 **지형 메시 변의 식별자**(정점 인덱스 쌍). 이웃 삼각형은 같은 변을
        ///     가로지르므로 이 키로 조각을 이으면 부동소수 허용치 없이 폴리라인이 복원된다.
        ///   · 그 삼각형의 **내리막 단위 방향(XZ)** = 바다 쪽. 반지름 방향을 쓰면 석호·수로 안쪽
        ///     물가에서 바다 방향이 뒤집히므로 높이 기울기에서 직접 얻는다.
        /// </summary>
        private static Vector2[] BakeShoreField(Vector3[] vertices, int[] triangles, out ShoreContour contour)
        {
            var field = new Vector2[vertices.Length];
            contour = null;

            // ── (1) 마칭 트라이앵글로 y = 0 등고선 선분을 모은다 ──
            // 삼각형 하나에서 부호가 갈리는 변은 항상 0개 또는 2개다(세 정점의 부호 조합상).
            // 2개면 두 교점을 잇는 선분 하나가 그 삼각형 안의 등고선이다.
            var segAx = new List<float>();
            var segAz = new List<float>();
            var segBx = new List<float>();
            var segBz = new List<float>();
            var segKeyA = new List<long>();
            var segKeyB = new List<long>();
            var segDownX = new List<float>();
            var segDownZ = new List<float>();
            long vertexStride = vertices.Length + 1L; // 변 키 = min·stride + max (충돌 없는 접기)

            for (int t = 0; t + 2 < triangles.Length; t += 3)
            {
                int i0 = triangles[t];
                int i1 = triangles[t + 1];
                int i2 = triangles[t + 2];
                Vector3 p0 = vertices[i0];
                Vector3 p1 = vertices[i1];
                Vector3 p2 = vertices[i2];

                Vector2 first = default;
                Vector2 second = default;
                long firstKey = 0L;
                long secondKey = 0L;
                int found = 0;

                if (ShoreEdgeCrossing(p0, p1, out Vector2 h01))
                {
                    first = h01;
                    firstKey = ShoreEdgeKey(i0, i1, vertexStride);
                    found = 1;
                }
                if (ShoreEdgeCrossing(p1, p2, out Vector2 h12))
                {
                    long key = ShoreEdgeKey(i1, i2, vertexStride);
                    if (found == 0) { first = h12; firstKey = key; }
                    else { second = h12; secondKey = key; }
                    found++;
                }
                if (found < 2 && ShoreEdgeCrossing(p2, p0, out Vector2 h20))
                {
                    long key = ShoreEdgeKey(i2, i0, vertexStride);
                    if (found == 0) { first = h20; firstKey = key; }
                    else { second = h20; secondKey = key; }
                    found++;
                }

                if (found < 2)
                    continue;

                segAx.Add(first.x); segAz.Add(first.y);
                segBx.Add(second.x); segBz.Add(second.y);
                segKeyA.Add(firstKey); segKeyB.Add(secondKey);

                ShoreDownhill(p0, p1, p2, first, second, out float downX, out float downZ);
                segDownX.Add(downX); segDownZ.Add(downZ);
            }

            int segmentCount = segAx.Count;
            if (segmentCount == 0)
            {
                // 섬 전체가 물 위이거나 전체가 물속 - 이 지형에서는 있을 수 없지만(바깥 테두리가
                // 항상 해수면 아래로 잠긴다), 값이 없으면 조용히 "물가 없음"으로 둔다.
                for (int i = 0; i < vertices.Length; i++)
                {
                    float y = vertices[i].y;
                    field[i] = new Vector2(y >= 0f ? ShoreFieldFar : -ShoreFieldFar, y);
                }
                return field;
            }

            // 리스트 인덱서를 벗기고(내부 루프가 수십만 번 돈다) 하한 판정용 중점/반길이를 미리 만든다.
            float[] ax = segAx.ToArray();
            float[] az = segAz.ToArray();
            float[] bx = segBx.ToArray();
            float[] bz = segBz.ToArray();

            // [해변 파도 3단계] 같은 선분 배열을 리본에도 넘긴다(복사만 - 거리장 계산에는 무관).
            contour = new ShoreContour
            {
                count = segmentCount,
                ax = ax,
                az = az,
                bx = bx,
                bz = bz,
                keyA = segKeyA.ToArray(),
                keyB = segKeyB.ToArray(),
                downX = segDownX.ToArray(),
                downZ = segDownZ.ToArray(),
            };

            var midX = new float[segmentCount];
            var midZ = new float[segmentCount];
            var halfLength = new float[segmentCount];
            for (int s = 0; s < segmentCount; s++)
            {
                midX[s] = (ax[s] + bx[s]) * 0.5f;
                midZ[s] = (az[s] + bz[s]) * 0.5f;
                float ex = bx[s] - ax[s];
                float ez = bz[s] - az[s];
                halfLength[s] = Mathf.Sqrt(ex * ex + ez * ez) * 0.5f;
            }

            // ── (2) 정점마다 등고선까지의 최소 거리 ──
            float previousBest = -1f;
            float previousX = 0f;
            float previousZ = 0f;

            for (int i = 0; i < vertices.Length; i++)
            {
                Vector3 v = vertices[i];

                // 이웃 정점의 결과를 상한으로 물려받는다(1-Lipschitz). 처음 한 번만 무한대로 시작한다.
                float best = float.MaxValue;
                if (previousBest >= 0f)
                {
                    float dx = v.x - previousX;
                    float dz = v.z - previousZ;
                    best = previousBest + Mathf.Sqrt(dx * dx + dz * dz);
                }

                for (int s = 0; s < segmentCount; s++)
                {
                    // 하한: 선분 위 어떤 점도 중점에서 halfLength보다 멀지 않다.
                    float mx = v.x - midX[s];
                    float mz = v.z - midZ[s];
                    float reachable = best + halfLength[s];
                    if (mx * mx + mz * mz > reachable * reachable)
                        continue;

                    float d = PointSegmentDistance2D(v.x, v.z, ax[s], az[s], bx[s], bz[s]);
                    if (d < best)
                        best = d;
                }

                previousBest = best;
                previousX = v.x;
                previousZ = v.z;

                field[i] = new Vector2(v.y >= 0f ? best : -best, v.y);
            }

            return field;
        }

        /// <summary>
        /// 지형 메시 변(정점 인덱스 두 개)의 식별자. 순서와 무관하게 같은 값이 나오도록 정렬해 접는다.
        /// 이웃한 두 삼각형은 공유 변에서 **같은 키**를 내므로, 리본의 조각 잇기가 좌표 비교(허용치가
        /// 필요하다) 대신 위상 비교로 성립한다. stride = 정점 수 + 1이라 (min, max) 쌍이 충돌하지 않는다.
        /// </summary>
        private static long ShoreEdgeKey(int i0, int i1, long stride)
        {
            return i0 < i1 ? i0 * stride + i1 : i1 * stride + i0;
        }

        /// <summary>
        /// 삼각형 (p0, p1, p2)의 높이장 기울기에서 **내리막 단위 방향(XZ)** 을 구한다 = 바다 쪽.
        /// 세 정점이 XZ에서 퇴화(면적 0)했거나 완전히 평평하면, 등고선 선분에 수직인 두 방향 중
        /// 삼각형 중심에서 멀어지는 쪽을 쓴다(그래도 방향이 없으면 0을 돌려주고 소비 측이 무시한다).
        /// </summary>
        private static void ShoreDownhill(Vector3 p0, Vector3 p1, Vector3 p2, Vector2 segA, Vector2 segB,
            out float downX, out float downZ)
        {
            float e1x = p1.x - p0.x, e1z = p1.z - p0.z, dy1 = p1.y - p0.y;
            float e2x = p2.x - p0.x, e2z = p2.z - p0.z, dy2 = p2.y - p0.y;
            float det = e1x * e2z - e1z * e2x;

            if (Mathf.Abs(det) > 1e-9f)
            {
                // ∇y (크라메르). 내리막은 그 반대 방향이다.
                float gx = (dy1 * e2z - dy2 * e1z) / det;
                float gz = (e1x * dy2 - e2x * dy1) / det;
                float length = Mathf.Sqrt(gx * gx + gz * gz);
                if (length > 1e-7f)
                {
                    downX = -gx / length;
                    downZ = -gz / length;
                    return;
                }
            }

            // 폴백: 등고선 선분의 법선 두 개 중 삼각형 중심에서 바깥으로 나가는 쪽.
            float ex = segB.x - segA.x;
            float ez = segB.y - segA.y;
            float edgeLength = Mathf.Sqrt(ex * ex + ez * ez);
            if (edgeLength < 1e-7f)
            {
                downX = 0f;
                downZ = 0f;
                return;
            }

            float nx = ez / edgeLength;
            float nz = -ex / edgeLength;
            float cx = (p0.x + p1.x + p2.x) / 3f - (segA.x + segB.x) * 0.5f;
            float cz = (p0.z + p1.z + p2.z) / 3f - (segA.y + segB.y) * 0.5f;
            if (nx * cx + nz * cz > 0f)
            {
                nx = -nx;
                nz = -nz;
            }

            downX = nx;
            downZ = nz;
        }

        // ── [해변 파도 3단계] 등고선 인계 슬롯 ─────────────────────────────────────
        // GenerateIslandMesh(메시를 만드는 곳)와 BuildGroundCaps(섬 오브젝트를 손에 쥔 곳)는
        // 서로 다른 호출이고, 그 사이를 WorldMapManager(편집 범위 밖)가 잇는다. 인자를 늘리지
        // 않고 값을 넘기려고 **메시 참조로 잠근 한 칸짜리 슬롯**을 쓴다. 흐름이
        // "메시 생성 → 같은 섬의 BuildIslandSurface"로 한 호출 안에서 이어지므로(WorldMapManager.
        // SpawnPlaceholder) 한 칸이면 충분하고, 혹시 어긋나면 참조 비교가 걸러 리본만 조용히
        // 생략된다(잘못된 섬의 등고선이 쓰이는 일은 구조적으로 없다).
        private static Mesh pendingContourMesh;
        private static ShoreContour pendingContour;

        /// <summary>방금 구운 등고선을 메시에 묶어 둔다(BakeShoreField 직후 1회).</summary>
        private static void StashShoreContour(Mesh mesh, ShoreContour contour)
        {
            pendingContourMesh = mesh;
            pendingContour = contour;
        }

        /// <summary>그 메시에 묶어 둔 등고선을 꺼내며 슬롯을 비운다(다른 메시면 null).</summary>
        private static ShoreContour ConsumeShoreContour(Mesh mesh)
        {
            if (mesh == null || pendingContourMesh != mesh)
                return null;

            ShoreContour contour = pendingContour;
            pendingContourMesh = null;
            pendingContour = null;
            return contour;
        }

        /// <summary>
        /// 변 (a, b)가 해수면(y = 0)을 가로지르면 그 교점의 XZ를 돌려준다.
        /// 두 끝점의 y 부호가 같으면 교차가 없다.
        /// </summary>
        private static bool ShoreEdgeCrossing(Vector3 a, Vector3 b, out Vector2 hit)
        {
            hit = default;
            if ((a.y > 0f) == (b.y > 0f))
                return false;

            float denominator = a.y - b.y;
            if (Mathf.Abs(denominator) < 1e-6f)
                return false;

            float s = a.y / denominator; // a + s·(b − a) 에서 y = 0
            hit = new Vector2(a.x + (b.x - a.x) * s, a.z + (b.z - a.z) * s);
            return true;
        }

        /// <summary>
        /// 점 (px, pz)에서 선분 (x0,z0)-(x1,z1)까지의 XZ 평면 거리.
        /// IslandMeshGenerator.SegmentDistance와 같은 식이지만, 이쪽은 수십만 번 도는 내부 루프
        /// 전용이라 인자를 풀어 둔 별도 사본이다(높이장 쪽 식은 한 글자도 건드리지 않는다).
        /// </summary>
        private static float PointSegmentDistance2D(float px, float pz, float x0, float z0, float x1, float z1)
        {
            float ex = x1 - x0;
            float ez = z1 - z0;
            float lengthSq = ex * ex + ez * ez;

            float t = lengthSq > 1e-12f
                ? Mathf.Clamp01(((px - x0) * ex + (pz - z0) * ez) / lengthSq)
                : 0f;

            float dx = px - (x0 + ex * t);
            float dz = pz - (z0 + ez * t);
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        // ── MGShoreline 에셋 프로브 ────────────────────────────────────────────────
        // [로드 규칙] Resources.Load는 정적 필드 초기자에서 부르지 않는다(초기자는 Unity가 Load를
        // 막는 시점에 돌 수 있다 - 위 ProbeSingleMeshModels 주석과 같은 함정). 그리고 **실패를
        // 영구히 캐시하지 않는다** - 거품 텍스처(Textures/shore_foam)는 다른 에이전트가 동시에
        // 만드는 중이라 지금은 없을 수 있고, 그 null을 굳히면 텍스처가 들어와도 세션 내내 거품이
        // 안 나온다. 성공할 때까지 프레임당 한 번만 다시 살핀다.

        private static Shader shorelineShader;
        private static int shorelineShaderProbeFrame = -1;
        private static Texture2D shoreFoamTexture;
        private static int shoreFoamProbeFrame = -1;

        /// <summary>MGShoreline의 거품 텍스처 슬롯. 문자열 조회를 섬마다 반복하지 않으려고 ID로 굳힌다.</summary>
        private static readonly int FoamMapProperty = Shader.PropertyToID("_FoamMap");

        /// <summary>
        /// 모래 캡용 MG/Shoreline 셰이더. 없으면 null이고, 그때 캡은 예전 그대로 URP Lit으로 남는다
        /// (해변이 정적인 모래로 보일 뿐 렌더는 멀쩡하다 - MGOcean/MGGrass와 같은 폴백 계약).
        /// </summary>
        private static Shader GetShorelineShader()
        {
            if (shorelineShader != null || shorelineShaderProbeFrame == Time.frameCount)
                return shorelineShader;

            shorelineShaderProbeFrame = Time.frameCount;
            shorelineShader = Resources.Load<Shader>("Shaders/MGShoreline");
            return shorelineShader;
        }

        /// <summary>
        /// 스와시 거품 텍스처(RGBA = 거품 마스크 / 디졸브 노이즈 / 미세 디테일 / 큰 덩어리).
        /// 없으면 null이고, 셰이더의 _FoamMap 기본값 "black"이 그대로 남아 **거품만 조용히 생략**된다
        /// (R = 0 → 거품 마스크 0). 젖은 모래 연출은 텍스처 없이도 그대로 동작한다.
        /// </summary>
        private static Texture2D GetShoreFoamTexture()
        {
            if (shoreFoamTexture != null || shoreFoamProbeFrame == Time.frameCount)
                return shoreFoamTexture;

            shoreFoamProbeFrame = Time.frameCount;
            shoreFoamTexture = Resources.Load<Texture2D>("Textures/shore_foam");
            return shoreFoamTexture;
        }

        /// <summary>
        /// 이미 만들어진 캡 머티리얼의 셰이더를 MG/Shoreline으로 갈아 끼운다(성공하면 true).
        ///
        /// 새 머티리얼을 만들지 않고 갈아 끼우는 이유: StructureVisualBuilder.CreateColorMaterial이
        /// 런타임 머티리얼 이름 접두사([B29] "지워도 되는 인스턴스" 표식)와 모래 텍스처 로드를
        /// 한 곳에서 담당한다. 그 경로를 그대로 통과시키고 셰이더만 바꾸면 표식/텍스처 규약이
        /// 저절로 유지된다. 프로퍼티는 이름이 같으면 셰이더 교체 후에도 살아남으므로
        /// material.color([MainColor] _BaseColor)와 mainTexture([MainTexture] _BaseMap)가
        /// URP Lit 때와 같은 의미로 이어진다 - **아키타입 sandColor가 그대로 유지되는 근거**다.
        /// URP Lit 전용이던 _Smoothness는 새 셰이더에 없어 조용히 버려진다(그래서 새 셰이더는
        /// 이름이 겹치지 않는 _DrySmoothness/_WetSmoothness를 쓴다).
        /// </summary>
        private static bool TryApplyShorelineShader(Material material)
        {
            Shader shader = GetShorelineShader();
            if (material == null || shader == null)
                return false;

            material.shader = shader;

            var foam = GetShoreFoamTexture();
            if (foam != null)
                material.SetTexture(FoamMapProperty, foam);

            return true;
        }

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
            shorelineShader = null;
            shorelineShaderProbeFrame = -1;
            shoreFoamTexture = null;
            shoreFoamProbeFrame = -1;
            pendingContourMesh = null;
            pendingContour = null;
        }
    }
}
