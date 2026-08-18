using System.Collections.Generic;
using UnityEngine;

namespace MakeGame.Systems
{
    /// <summary>
    /// [B28] 자원 노드가 **공유**하는 머티리얼/메시 보관소.
    ///
    /// [B48] 여기에 **실물 OBJ 모델 로더**(TryLoadTwoPartModel / TryGetBambooModel)가 더 붙었다.
    /// 모델이 있는 자원은 모델을 쓰고, 없으면 아래 절차 메시로 폴백한다 - 두 경로 다 살아 있어야 한다.
    ///
    /// (아래는 절차 메시 쪽 근거다) 이 프로젝트는 오랫동안 3D 모델 에셋이 0개라 모든 형태를 런타임에
    /// 조립했다. 그런데 프리미티브만으로는
    /// 마디 있는 대나무 줄기·옹이 있는 잔가지·각진 돌 파편·잎맥 있는 야자잎을 만들 수 없고, 프리미티브를
    /// 겹쳐서 흉내 내면 파츠(=드로우콜)가 폭증한다. 여기서는 그 형태들을 **절차 메시 한 장**으로 만든다.
    /// 대나무 줄기 하나에 마디를 5개 넣어도 파츠는 그대로 1개다 - 굵기 변화는 메시 안에 있기 때문이다.
    ///
    /// 세 가지 원칙:
    ///  1. 전부 정적 캐시다. 월드 전체(섬 9개 · 노드 수백 개)가 메시 30장과 머티리얼 40개 안팎을
    ///     나눠 쓴다. 예전에는 파츠 하나가 머티리얼 하나였다 - 특대 섬 한 곳에서만 320개다
    ///     (자원 13종 × 노드 100개 × 파츠 평균 3.2). 실측 내역은 ResourceNode.ClumpVisualPrimitives 주석.
    ///  2. 메시 좌표계는 두 가지뿐이다. 이름이 `~Unit`이면 프리미티브 로컬 규격(실린더 |y|<=1,
    ///     큐브·구 |v|<=0.5)이고, `~Meters`면 1단위 = 1미터에 원점이 밑동이다(AddMeshPart 전용).
    ///     루트 메시는 반드시 Unit이어야 한다 - 접지·콜라이더 계산이 그 규격을 전제로 한다.
    ///  3. 감김(winding)을 표로 외우지 않는다. 이 프로젝트는 왼손 좌표계라 표준 인덱스 표를 옮기면
    ///     통째로 안쪽을 향해 컬링되는 사고가 반복됐다(IslandMeshGenerator.AddOrientedTriangle 주석).
    ///     여기서도 삼각형마다 기하 법선을 계산해 기준 방향과 맞춘다.
    /// </summary>
    public static class ResourceVisualLibrary
    {
        /// <summary>줄기·가지의 기본 단면 분할 수. 6이면 로우폴리 실루엣이 유지되면서도 둥글게 읽힌다.</summary>
        private const int StemSides = 6;

        private static readonly Dictionary<string, Material> materialCache = new Dictionary<string, Material>();
        private static readonly Dictionary<string, Mesh> meshCache = new Dictionary<string, Mesh>();

        /// <summary>
        /// 팔레트 색의 명도만 바꾼 변주(알파는 항상 1). IslandMeshGenerator.Shade와 같은 규칙이다 -
        /// URP Lit Opaque에서 `color * 0.75f` 처럼 곱하면 알파까지 0.75가 되는 실수를 막는다.
        /// </summary>
        public static Color Shade(Color color, float factor)
        {
            return new Color(
                Mathf.Clamp01(color.r * factor),
                Mathf.Clamp01(color.g * factor),
                Mathf.Clamp01(color.b * factor),
                1f);
        }

        /// <summary>
        /// (색 + 텍스처) 조합당 머티리얼 하나를 만들어 재사용한다. 색은 채널당 64단계로 양자화해
        /// 눈에 보이지 않는 차이로 캐시가 늘어나지 않게 한다.
        /// 파괴된 머티리얼(씬 언로드 등)이 캐시에 남아 있으면 다시 만든다 - Unity의 == 오버로드가
        /// 파괴된 오브젝트를 null로 알려주므로 이 검사 하나로 충분하다.
        /// </summary>
        public static Material GetMaterial(Color color, string textureName)
        {
            string key = Mathf.RoundToInt(color.r * 64f) + "_" + Mathf.RoundToInt(color.g * 64f) + "_"
                + Mathf.RoundToInt(color.b * 64f) + "_" + (string.IsNullOrEmpty(textureName) ? "noise" : textureName);

            Material cached;
            if (materialCache.TryGetValue(key, out cached) && cached != null)
                return cached;

            // 텍스처 로드 실패(null)는 CreateColorMaterial 안에서 조용히 처리된다 - 단색으로 나올 뿐이다.
            Material created = StructureVisualBuilder.CreateColorMaterial(color, textureName);
            if (created != null)
            {
                // 같은 메시 + 같은 머티리얼 조합이 섬마다 수십 개씩 나오므로 인스턴싱이 실제로 걸린다.
                created.enableInstancing = true;
            }
            materialCache[key] = created;
            return created;
        }

        // ── [B48] 실물 OBJ 모델 로더 (야자수 · 대나무 공용) ─────────────────────────────
        /// <summary>
        /// `o` 오브젝트가 **2개**(줄기 + 잎)인 OBJ에서 공유 메시 두 장을 꺼낸다. 못 찾으면 false다.
        ///
        /// [프리팹을 Instantiate하지 않는다] 바위(IslandMeshGenerator.TryGetRockModel)와 같다.
        /// MeshFilter.sharedMesh만 꺼내 쓰면 임포터가 붙였을 수 있는 콜라이더가 씬에 **구조적으로**
        /// 들어올 수 없다(초목·자원의 시각 파츠에 콜라이더가 생기면 TerrainSampler.SnapToGround와
        /// 배치 높이 계산이 통째로 깨진다).
        ///
        /// [줄기/잎을 어떻게 가르나] Unity의 OBJ 임포터는 `o` 그룹을 자식 GameObject로 만들 수도,
        /// 하나를 루트에 얹을 수도 있다. 그래서 **루트를 포함한 모든 MeshFilter를 순회**하고
        ///   (1) 이름(메시 이름 + 오브젝트 이름)에 trunk/culm/stem이 있으면 줄기,
        ///       crown/leaf/foliage/frond가 있으면 잎으로 본다.
        ///   (2) 이름으로 못 가르면 **OBJ의 `o` 등장 순서**로 폴백한다(줄기가 항상 먼저다).
        ///   (3) 메시가 하나뿐이면 그것을 줄기로 주고 잎은 null이다 - 호출부가 서브메시 2개짜리
        ///       (임포터가 합쳐 온) 경우를 머티리얼 두 장으로 따로 처리한다.
        ///
        /// [로드 규칙] Resources.Load는 필드 초기자에서 부르지 않고(생성자 시점이라 null이 온다),
        /// 실패를 영구히 캐시하지 않는다(AGENT_BRIEF 4장 3번). 프레임당 1회 재시도 가드는 호출부가 갖는다.
        /// </summary>
        public static bool TryLoadTwoPartModel(string resourcePath, out Mesh trunk, out Mesh foliage)
        {
            trunk = null;
            foliage = null;

            // 확장자를 붙이면 항상 null이다(AssetPipeline 3장).
            var prefab = Resources.Load<GameObject>(resourcePath);
            if (prefab == null)
                return false;

            var filters = prefab.GetComponentsInChildren<MeshFilter>(true);
            Mesh firstInOrder = null;
            Mesh secondInOrder = null;

            for (int i = 0; i < filters.Length; i++)
            {
                MeshFilter filter = filters[i];
                if (filter == null || filter.sharedMesh == null)
                    continue;

                Mesh mesh = filter.sharedMesh;
                string label = (mesh.name + "/" + filter.gameObject.name).ToLowerInvariant();

                if (trunk == null && (label.Contains("trunk") || label.Contains("culm") || label.Contains("stem")))
                    trunk = mesh;
                else if (foliage == null && (label.Contains("crown") || label.Contains("leaf")
                    || label.Contains("foliage") || label.Contains("frond")))
                    foliage = mesh;

                if (firstInOrder == null)
                    firstInOrder = mesh;
                else if (secondInOrder == null)
                    secondInOrder = mesh;
            }

            if (trunk == null)
                trunk = firstInOrder != foliage ? firstInOrder : secondInOrder;
            if (foliage == null && secondInOrder != null)
                foliage = secondInOrder != trunk ? secondInOrder : firstInOrder;

            return trunk != null;
        }

        // ── [B1] 다중 파트 OBJ 모델 로더 (AirlinerWreck · CampfireVisual 공용) ─────────
        /// <summary>
        /// `o` 오브젝트가 여러 개인 OBJ를 한 번 프로브한다(프레임당 1회 가드는 호출부가 갖는다 -
        /// TryLoadTwoPartModel과 같은 규칙). 로드는 반드시 Load&lt;GameObject&gt; +
        /// GetComponentsInChildren&lt;MeshFilter&gt; 경로다(LoadAll&lt;Mesh&gt;는 이 프로젝트의
        /// 모델 에셋에서 빈 배열을 준 실사고가 있다).
        ///
        /// 두 임포트 형태를 모두 지원한다:
        ///  · 병합 임포트(현재 Unity 6.5의 실제 동작): MeshFilter 1개 = 서브메시 N개 → mergedMesh.
        ///    **서브메시 인덱스 = OBJ `o` 그룹 등장 순서**가 절대 계약이다(이름 정렬/재배열 금지).
        ///  · 개별 메시 임포트(임포터 동작이 되돌아올 경우의 방어): partNames[i]가 이름에 포함되는
        ///    메시를 partMeshes[i]에 채운다. 메시 이름이 우선이고, 임포터가 메시 이름을 바꿔도
        ///    노드 이름으로 잡는다.
        /// 실패(프리팹 null)나 미발견 슬롯은 건드리지 않으므로 실패가 영구 캐시되지 않는다.
        /// </summary>
        /// <returns>이 시도 후 빌드 가능한 상태인가(IsMultiPartModelComplete와 같은 판정).</returns>
        public static bool TryLoadMultiPartModel(string resourcePath, string[] partNames,
            Mesh[] partMeshes, ref Mesh mergedMesh)
        {
            // 확장자를 붙이면 항상 null이다(AssetPipeline 3장).
            var prefab = Resources.Load<GameObject>(resourcePath);
            if (prefab != null)
            {
                var filters = prefab.GetComponentsInChildren<MeshFilter>(true);

                // 병합 임포트: MeshFilter 1개 = 서브메시 N개.
                if (filters.Length == 1 && filters[0] != null && filters[0].sharedMesh != null)
                {
                    mergedMesh = filters[0].sharedMesh;
                }

                // 개별 메시 임포트: 이름으로 가른다.
                for (int i = 0; i < partMeshes.Length; i++)
                {
                    if (partMeshes[i] != null)
                        continue;

                    for (int m = 0; m < filters.Length; m++)
                    {
                        Mesh mesh = filters[m] != null ? filters[m].sharedMesh : null;
                        string meshName = mesh != null ? mesh.name.ToLowerInvariant() : null;
                        string nodeName = filters[m] != null
                            ? filters[m].gameObject.name.ToLowerInvariant() : null;
                        // 메시 이름이 우선이고, 임포터가 메시 이름을 바꿔도 노드 이름으로 잡는다.
                        if (mesh != null &&
                            ((meshName != null && meshName.Contains(partNames[i])) ||
                             (nodeName != null && nodeName.Contains(partNames[i]))))
                        {
                            partMeshes[i] = mesh;
                            break;
                        }
                    }
                }
            }

            return IsMultiPartModelComplete(mergedMesh, partMeshes);
        }

        /// <summary>partMeshes에 아직 못 채운 슬롯이 있는가(호출부의 프로브 필요 판정용).</summary>
        public static bool AnyPartMissing(Mesh[] partMeshes)
        {
            for (int i = 0; i < partMeshes.Length; i++)
            {
                if (partMeshes[i] == null)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// 빌드 가능 판정: 병합 메시가 있거나 파트가 전부 모여야 한다 - 같은 OBJ의 서브에셋이라
        /// 일부만 로드되는 상황은 임포트가 아직 끝나지 않았다는 뜻이고, 반쪽짜리를 만들었다 지우는
        /// 것보다 한 프레임 더 기다리는 쪽이 싸다.
        /// </summary>
        public static bool IsMultiPartModelComplete(Mesh mergedMesh, Mesh[] partMeshes)
        {
            return mergedMesh != null || !AnyPartMissing(partMeshes);
        }

        /// <summary>
        /// 병합 메시의 서브메시들에 머티리얼을 슬롯별로 입힌다. **서브메시 인덱스 = OBJ `o` 그룹
        /// 순서 = materials 배열 순서**가 절대 계약이다(이름 정렬/재배열 금지). 서브메시가
        /// 머티리얼보다 많으면 마지막 머티리얼을 반복한다. 서브메시가 1개면 아무것도 하지 않는다
        /// (CreateMeshPart가 이미 materials[0]을 입혀 둔 상태를 유지한다).
        /// </summary>
        public static void ApplySubmeshMaterials(MeshRenderer renderer, Mesh mesh, Material[] materials)
        {
            if (renderer == null || mesh == null || mesh.subMeshCount < 2)
                return;

            int count = mesh.subMeshCount;
            var slots = new Material[count];
            for (int s = 0; s < count; s++)
                slots[s] = materials[Mathf.Min(s, materials.Length - 1)];
            renderer.sharedMaterials = slots;
        }

        /// <summary>
        /// 다중 파트 모델의 시각 파츠를 붙인다. 병합 메시가 있으면 렌더러 하나 + 머티리얼 배열
        /// (ApplySubmeshMaterials), 없으면 파트별 렌더러 하나씩(partNames[i] + materials[i])이다.
        /// 정점이 전부 로컬 미터 좌표로 구워져 있는 모델 전용이라 파츠는 예외 없이
        /// 위치 0 · 회전 identity · 스케일 1로 붙는다(호출부에서 회전/스케일을 다시 주지 않는다).
        /// </summary>
        public static void BuildMultiPartVisual(Transform root, string mergedPartName, Mesh mergedMesh,
            string[] partNames, Mesh[] partMeshes, Material[] materials)
        {
            if (mergedMesh != null)
            {
                // 병합 임포트 경로: 렌더러 하나 + 머티리얼 배열. 서브메시 순서는 OBJ의 `o` 순서를 따른다.
                var part = StructureVisualBuilder.CreateMeshPart(root, mergedPartName, mergedMesh,
                    Vector3.zero, Vector3.one, Quaternion.identity, materials[0]);
                var renderer = part != null ? part.GetComponent<MeshRenderer>() : null;
                ApplySubmeshMaterials(renderer, mergedMesh, materials);
                return;
            }

            for (int i = 0; i < partMeshes.Length; i++)
            {
                StructureVisualBuilder.CreateMeshPart(root, partNames[i], partMeshes[i],
                    Vector3.zero, Vector3.one, Quaternion.identity, materials[i]);
            }
        }

        /// <summary>모델 에셋 경로(Resources 기준, 확장자 없음).</summary>
        private static readonly string[] BambooModelResourcePaths =
        {
            "Models/bamboo_a", "Models/bamboo_b", "Models/bamboo_c",
            "Models/bamboo_d", "Models/bamboo_e", "Models/bamboo_f"
        };

        /// <summary>각 모델의 실측 전체 높이(m, 밑면 y=0 기준). 위 경로와 인덱스가 일대일로 대응한다.
        /// d~f도 OBJ 정점의 maxY를 실측한 값이다(d 4.113(v2 재제작) / e 5.070 / f 4.252).</summary>
        private static readonly float[] BambooModelHeights = { 3.349f, 3.885f, 4.463f, 4.113f, 5.070f, 4.252f };

        // 배열 크기는 경로 배열 길이에서 온다(모델을 더 붙일 때 세 곳을 같이 고치는 실수 방지).
        // 필드 초기자는 선언 순서대로 실행되므로 BambooModelResourcePaths(위)가 먼저 채워진다.
        private static readonly Mesh[] bambooCulmMeshes = new Mesh[BambooModelResourcePaths.Length];
        private static readonly Mesh[] bambooLeafMeshes = new Mesh[BambooModelResourcePaths.Length];
        private static int bambooModelProbeFrame = -1;

        /// <summary>
        /// 목표 높이에 가장 가까운 대나무 모델의 **공유 메시 두 장**(줄기 다발 / 잎)을 돌려준다.
        /// 하나도 못 찾으면 false이고, 그때 호출부는 예전 절차 포기(곁줄기 + 잎다발)로 돌아간다.
        ///
        /// 바위·야자수와 완전히 같은 규칙이다: 프레임당 1회만 프로브하고(섬 하나에 대나무 노드가
        /// 최대 8개라 가드가 없으면 한 프레임에 Load가 24번 불린다), 실패를 영구 캐시하지 않으며,
        /// 변종 선택에 난수를 쓰지 않는다(이미 뽑아 둔 세로 지터가 정한 높이로 고른다).
        /// </summary>
        public static bool TryGetBambooModel(float targetHeight, out Mesh culms, out Mesh leaves, out float modelHeight)
        {
            culms = null;
            leaves = null;
            modelHeight = 1f;

            bool anyMissing = false;
            for (int i = 0; i < bambooCulmMeshes.Length; i++)
            {
                if (bambooCulmMeshes[i] == null)
                    anyMissing = true;
            }

            if (anyMissing && bambooModelProbeFrame != Time.frameCount)
            {
                bambooModelProbeFrame = Time.frameCount;
                for (int i = 0; i < bambooCulmMeshes.Length; i++)
                {
                    if (bambooCulmMeshes[i] != null)
                        continue;

                    Mesh loadedCulms, loadedLeaves;
                    if (!TryLoadTwoPartModel(BambooModelResourcePaths[i], out loadedCulms, out loadedLeaves))
                        continue;

                    bambooCulmMeshes[i] = loadedCulms;
                    bambooLeafMeshes[i] = loadedLeaves;
                }
            }

            float bestDelta = float.MaxValue;
            for (int i = 0; i < bambooCulmMeshes.Length; i++)
            {
                if (bambooCulmMeshes[i] == null)
                    continue;

                float delta = Mathf.Abs(BambooModelHeights[i] - targetHeight);
                if (delta >= bestDelta)
                    continue;

                bestDelta = delta;
                culms = bambooCulmMeshes[i];
                leaves = bambooLeafMeshes[i];
                modelHeight = BambooModelHeights[i];
            }

            return culms != null;
        }

        // ── [4티어 원석] 실물 바위 모델 (rock_a~c 재사용) ─────────────────────────────
        /// <summary>원석(석재/대리석) 본체로 재사용하는 바위 모델 경로(Resources 기준, 확장자 없음).
        /// 신규 모델은 만들지 않는다 - 지형 장식용 rock_a~c를 원석 크기(0.5~0.6m 폭)로 줄여 쓴다.
        /// rock_d(판석)/rock_e(첨탑)는 이 크기로 줄이면 "덩어리"로 안 읽혀 제외했다.</summary>
        private static readonly string[] OreRockModelResourcePaths =
        {
            "Models/rock_a", "Models/rock_b", "Models/rock_c"
        };

        /// <summary>각 모델의 실측 크기(m, W×H×D · 밑면 y=0 · X/Z 중심). IslandMeshGenerator.MeshLibrary의
        /// RockModelSizes 실측값과 같은 값이다(같은 OBJ를 읽는다 - 그 파일은 수정 금지라 여기 사본을 둔다).</summary>
        private static readonly Vector3[] OreRockModelSizes =
        {
            new Vector3(1.85f, 1.20f, 1.60f),
            new Vector3(2.60f, 1.55f, 2.30f),
            new Vector3(3.20f, 2.35f, 2.60f),
        };

        private static readonly Mesh[] oreRockMeshes = new Mesh[OreRockModelResourcePaths.Length];
        private static int oreRockProbeFrame = -1;

        /// <summary>원석 바위 모델 변종 수. 호출부가 변종 draw를 모델 로드 여부와 무관하게 먼저 뽑는 데 쓴다.</summary>
        public static int OreRockVariantCount
        {
            get { return OreRockModelResourcePaths.Length; }
        }

        /// <summary>
        /// 지정 변종의 원석 바위 **공유 메시**를 돌려준다. 그 변종이 아직 안 로드됐으면 로드된 다른
        /// 변종으로 폴백하고, 하나도 없으면 false다(호출부는 절차 파편 메시 루트를 그대로 쓴다).
        ///
        /// 로드 규칙은 TryGetBambooModel과 동일하다: 필드 초기자에서 Load하지 않고, 실패를 영구
        /// 캐시하지 않으며(성공할 때까지 프레임당 1회만 재프로브), 변종 선택에 여기서 난수를 쓰지 않는다
        /// (호출부가 이미 뽑아 둔 변종 인덱스를 받는다). Instantiate하지 않고 메시만 꺼내 쓰므로
        /// 임포터가 붙였을 수 있는 콜라이더가 씬에 구조적으로 들어오지 않는다.
        /// </summary>
        public static bool TryGetOreRockModel(int variant, out Mesh mesh, out Vector3 size)
        {
            mesh = null;
            size = Vector3.one;

            bool anyMissing = false;
            for (int i = 0; i < oreRockMeshes.Length; i++)
            {
                if (oreRockMeshes[i] == null)
                    anyMissing = true;
            }

            if (anyMissing && oreRockProbeFrame != Time.frameCount)
            {
                oreRockProbeFrame = Time.frameCount;
                for (int i = 0; i < oreRockMeshes.Length; i++)
                {
                    if (oreRockMeshes[i] != null)
                        continue;

                    // 바위 OBJ는 `o`가 하나뿐이라 첫 메시가 곧 본체다(두 번째 out은 버린다).
                    Mesh loaded, unused;
                    if (TryLoadTwoPartModel(OreRockModelResourcePaths[i], out loaded, out unused))
                        oreRockMeshes[i] = loaded;
                }
            }

            int pick = Mathf.Abs(variant) % oreRockMeshes.Length;
            if (oreRockMeshes[pick] == null)
            {
                pick = -1;
                for (int i = 0; i < oreRockMeshes.Length; i++)
                {
                    if (oreRockMeshes[i] != null)
                    {
                        pick = i;
                        break;
                    }
                }

                if (pick < 0)
                    return false;
            }

            mesh = oreRockMeshes[pick];
            size = OreRockModelSizes[pick];
            return true;
        }

        // ── 대나무 ─────────────────────────────────────────────────────────────
        /// <summary>
        /// 노드 루트용 대나무 줄기(실린더 규격). 마디 5~7개 · 위로 갈수록 가늘어짐.
        /// 반지름을 규격 상한(0.5)이 아니라 0.22에서 시작하는 이유: 루트 스케일(0.30)이 콜라이더
        /// 크기이기도 해서 줄이면 채집 판정이 좁아진다. 콜라이더는 넓게 두고 **보이는 줄기만**
        /// 지름 13.2cm로 가늘게 만든다(0.22 × 2 × 0.30m).
        ///
        /// [B29] 루트 높이가 2.1m → 4.2m가 되면서 함께 손본 값:
        ///  · 반지름 0.34 → 0.22 (스케일이 0.14 → 0.30이므로 보이는 굵기는 9.5cm → 13.2cm로 **커진다**)
        ///  · 마디 수 3~5 → 5~7. 그대로 두면 마디 간격이 0.42~0.70m → 0.84~1.40m로 벌어져 대나무가
        ///    아니라 매듭 몇 개 있는 기둥이 된다. 5~7이면 0.60~0.84m로, 높이에 맞춰 간격도 함께 커지되
        ///    "마디가 촘촘한 줄기"라는 실루엣은 유지된다.
        ///  · uvTile 3 → 6. 텍스처 밀도(1.43타일/m)를 높이 2배에도 그대로 유지한다.
        /// </summary>
        public static Mesh BambooCulmUnit(int variant)
        {
            int v = Mathf.Abs(variant) % 3;
            string key = "bambooUnit" + v;
            Mesh cached;
            if (meshCache.TryGetValue(key, out cached) && cached != null)
                return cached;

            Mesh mesh = BuildSegmentedStem("Res_BambooCulmUnit" + v, 5 + v, -1f, 1f, 0.22f, 0.155f, 0f, 1.22f, StemSides, 6f);
            meshCache[key] = mesh;
            return mesh;
        }

        /// <summary>
        /// 곁줄기용 대나무(미터 규격, 밑동이 원점). 변주마다 높이 2.25~3.85m · 지름 6.4~9.6cm ·
        /// 기울기 0.14~0.38m · 마디 5~9개가 다르게 조합돼 있어, 한 포기 안에서 줄기가 서로 다르게 읽힌다.
        /// 기울기를 메시에 구워 넣는 이유는 AddMeshPart 주석 참고(전단 방지).
        ///
        /// [B29] 높이 1.05~1.80m → 2.25~3.85m(×2.15). 루트 줄기(3.57~5.25m)와 합치면 한 포기 안의
        /// 최대/최소 차이가 1.6m → 3.0m로 벌어져 "무리"로 읽힌다 - 변주 폭을 유지하라는 지시대로,
        /// 비율(가장 큰 것 ÷ 가장 작은 것 = 1.71)은 예전 그대로 두고 전체를 밀어 올렸다.
        /// 굵기 ×1.55 · 기울기 ×2 · 마디 수 ×1.8을 함께 올린 이유는 BambooCulmUnit 주석과 같다.
        /// </summary>
        public static Mesh BambooCulmMeters(int variant)
        {
            int v = Mathf.Abs(variant) % 5;
            string key = "bambooMeters" + v;
            Mesh cached;
            if (meshCache.TryGetValue(key, out cached) && cached != null)
                return cached;

            float[] heights = { 3.10f, 2.25f, 3.85f, 2.70f, 3.50f };
            float[] radii = { 0.040f, 0.032f, 0.048f, 0.035f, 0.043f };
            float[] leans = { 0.20f, -0.32f, 0.14f, 0.38f, -0.22f };
            int[] bands = { 7, 5, 9, 6, 8 };

            Mesh mesh = BuildSegmentedStem("Res_BambooCulmM" + v, bands[v], 0f, heights[v],
                radii[v], radii[v] * 0.74f, leans[v], 1.22f, StemSides, heights[v] * 2f);
            meshCache[key] = mesh;
            return mesh;
        }

        // ── 나뭇가지 ───────────────────────────────────────────────────────────
        /// <summary>
        /// 노드 루트용 굵은 가지(실린더 규격). 옹이 2개 + 위로 갈수록 급하게 가늘어지는 테이퍼 +
        /// 살짝 굽음. 단면을 5각으로 두어 대나무(6각 · 매끈)와 실루엣이 겹치지 않게 한다.
        /// </summary>
        public static Mesh BranchStickUnit(int variant)
        {
            int v = Mathf.Abs(variant) % 3;
            string key = "branchUnit" + v;
            Mesh cached;
            if (meshCache.TryGetValue(key, out cached) && cached != null)
                return cached;

            float[] leans = { 0.10f, -0.14f, 0.05f };
            Mesh mesh = BuildSegmentedStem("Res_BranchUnit" + v, 2, -1f, 1f, 0.42f, 0.13f, leans[v], 1.34f, 5, 2f);
            meshCache[key] = mesh;
            return mesh;
        }

        /// <summary>
        /// 흩어진 잔가지 하나(미터 규격, 원점이 지면의 더미 중심). 들린 각도 12~70도 · 길이 0.26~0.52m ·
        /// 굵기 2.2~4.0cm가 변주마다 다르고, 절반은 갈래가 하나 더 나 있다.
        /// "주워 모을 것"으로 읽히려면 이 셋이 제각각이어야 한다는 것이 이번 지시의 요구다.
        /// </summary>
        public static Mesh TwigMeters(int variant)
        {
            int v = Mathf.Abs(variant) % 6;
            string key = "twigMeters" + v;
            Mesh cached;
            if (meshCache.TryGetValue(key, out cached) && cached != null)
                return cached;

            float[] lengths = { 0.42f, 0.30f, 0.52f, 0.36f, 0.46f, 0.26f };
            float[] radii = { 0.017f, 0.013f, 0.020f, 0.015f, 0.018f, 0.011f };
            float[] tilts = { 22f, 58f, 12f, 40f, 30f, 70f };
            float[] shifts = { -0.10f, 0.06f, -0.14f, 0.09f, -0.05f, 0.12f };
            bool[] forked = { true, false, true, false, true, false };

            float tilt = tilts[v] * Mathf.Deg2Rad;
            float length = lengths[v];
            float radius = radii[v];
            Vector3 direction = new Vector3(0f, Mathf.Sin(tilt), Mathf.Cos(tilt));
            Vector3 start = new Vector3(0f, radius * 1.1f, shifts[v]);
            Vector3 middle = start + direction * (length * 0.55f);
            Vector3 end = start + direction * length + new Vector3(0f, -0.015f, 0f); // 끝이 살짝 처진다

            var builder = new MeshBuilder();
            builder.AddTube(new[] { start, middle, end }, new[] { radius, radius * 0.82f, radius * 0.42f }, 5, true, true, 2f);

            if (forked[v])
            {
                Vector3 forkDirection = (direction + new Vector3(0.85f, 0.2f, 0f)).normalized;
                Vector3 forkEnd = middle + forkDirection * (length * 0.42f);
                builder.AddTube(new[] { middle, forkEnd }, new[] { radius * 0.62f, radius * 0.24f }, 4, false, true, 1f);
            }

            Mesh mesh = builder.Finish("Res_TwigM" + v);
            meshCache[key] = mesh;
            return mesh;
        }

        // ── 야자잎 ─────────────────────────────────────────────────────────────
        /// <summary>
        /// 잎 한 장(미터 규격, 원점이 잎자루 밑동이고 +Z로 뻗는다).
        /// 중앙 잎맥(가는 관) + 좌우로 갈라진 잎깃 4~7쌍으로 되어 있어 가장자리가 톱니로 읽힌다.
        /// 잎깃은 두께가 없는 면이라 **양면**으로 넣는다 - 한 면만 넣으면 아래에서 볼 때 통째로 사라진다
        /// (IslandMeshGenerator.GetGrassBladeMesh가 같은 이유로 같은 방식을 쓴다).
        ///
        /// 변주 0~2가 야자잎(길이 0.44~0.58m), 3~4가 대나무 잎다발(0.48~0.60m, 더 많이 처진다).
        /// [B29] 대나무 잎다발(3~4)만 0.24~0.30m → 0.48~0.60m로 키웠다. 줄기가 4~5m가 되면서 예전
        /// 크기로는 꼭대기에서 점으로 보였다. **야자잎이 쓰는 0~2는 한 값도 건드리지 않았다**
        /// (호출부: 야자잎 rng.NextInt(0,3) / 대나무 3 + i%2 - 두 범위가 겹치지 않는다).
        /// **주의:** 호출부가 이 메시를 스케일 1로 쓴다는 전제로 길이가 미터로 박혀 있다. 스케일을
        /// 따로 곱하지 마라 - 과거 "풀 메시를 바꿨는데 호출부 스케일이 그대로여서 거대한 판이 된" 사고와
        /// 같은 유형이 된다. 크기를 바꾸려면 이 표의 숫자를 바꿔라.
        /// </summary>
        public static Mesh FrondMeters(int variant)
        {
            int v = Mathf.Abs(variant) % 5;
            string key = "frondMeters" + v;
            Mesh cached;
            if (meshCache.TryGetValue(key, out cached) && cached != null)
                return cached;

            float[] lengths = { 0.50f, 0.58f, 0.44f, 0.60f, 0.48f };
            float[] widths = { 0.16f, 0.19f, 0.14f, 0.21f, 0.17f };
            float[] droops = { 0.07f, 0.09f, 0.06f, 0.26f, 0.22f };
            int[] pairs = { 6, 7, 5, 6, 5 };

            float length = lengths[v];
            float halfWidth = widths[v] * 0.5f;
            float droop = droops[v];
            int pairCount = pairs[v];

            var builder = new MeshBuilder();

            // 중앙 잎맥: 밑동에서 끝으로 가며 가늘어지고 아래로 처지는 가는 관.
            var ribCenters = new Vector3[5];
            var ribRadii = new float[5];
            for (int i = 0; i < 5; i++)
            {
                float t = i / 4f;
                ribCenters[i] = RibPoint(t, length, droop);
                ribRadii[i] = Mathf.Lerp(0.011f, 0.003f, t);
            }
            builder.AddTube(ribCenters, ribRadii, 4, true, true, 2f);

            // 잎깃: 잎맥에서 좌우로 갈라져 나가며 끝으로 갈수록 뒤로 눕는다.
            for (int i = 0; i < pairCount; i++)
            {
                float t0 = 0.10f + 0.82f * i / pairCount;
                float t1 = Mathf.Min(0.99f, t0 + 0.86f / pairCount);
                Vector3 inner0 = RibPoint(t0, length, droop);
                Vector3 inner1 = RibPoint(t1, length, droop);

                // 폭은 가운데가 가장 넓고 밑동/끝에서 0에 가까워진다(잎 전체가 방추형으로 읽힌다).
                // 최소 폭을 남겨 둔다 - 면적이 0인 삼각형은 RecalculateNormals에서 법선이 0이 되어
                // 그 잎깃 하나만 새까맣게 보인다.
                float w0 = halfWidth * Mathf.Max(0.14f, Mathf.Sin(Mathf.Pow(t0, 0.7f) * Mathf.PI));
                float w1 = halfWidth * Mathf.Max(0.14f, Mathf.Sin(Mathf.Pow(t1, 0.7f) * Mathf.PI));

                for (int side = 0; side < 2; side++)
                {
                    float sign = side == 0 ? 1f : -1f;
                    Vector3 outward = new Vector3(sign * 0.90f, -0.30f, 0.32f); // 밖 + 아래 + 끝 방향
                    Vector3 outer0 = inner0 + outward * w0;
                    Vector3 outer1 = inner1 + outward * w1;
                    builder.AddQuad(inner0, inner1, outer1, outer0, Vector3.up, true);
                }
            }

            Mesh mesh = builder.Finish("Res_FrondM" + v);
            meshCache[key] = mesh;
            return mesh;
        }

        /// <summary>잎맥 곡선 위의 한 점(t = 0 밑동 ~ 1 끝). 처짐은 t²이라 밑동은 평평하고 끝만 내려간다.</summary>
        private static Vector3 RibPoint(float t, float length, float droop)
        {
            return new Vector3(0f, -droop * t * t, length * t);
        }

        // ── 돌 ────────────────────────────────────────────────────────────────
        /// <summary>
        /// 각진 돌덩이(구 규격). 정이십면체의 각 꼭짓점 반지름을 결정적으로 흔들어 만든 저폴리 파편이라
        /// 20면 전부가 평면으로 셰이딩된다 - 눌린 구(예전 형태)와 달리 "쪼개진 돌"로 읽힌다.
        /// y 방향 반지름을 정확히 0.5로 정규화하므로 접지 계산(GetHalfHeight)이 그대로 맞는다.
        /// </summary>
        public static Mesh RockChunkUnit(int variant)
        {
            int v = Mathf.Abs(variant) % 4;
            string key = "rockUnit" + v;
            Mesh cached;
            if (meshCache.TryGetValue(key, out cached) && cached != null)
                return cached;

            Mesh mesh = BuildAngularChunk("Res_RockChunk" + v, 8100 + v, 0.30f, false);
            meshCache[key] = mesh;
            return mesh;
        }

        /// <summary>
        /// 얇고 각진 석기 파편(큐브 규격). 축마다 따로 정규화해 큐브 상자를 꽉 채우므로,
        /// 부싯돌 루트의 납작한 스케일(0.32 × 0.10 × 0.42)이 그대로 "깨진 돌조각"이 된다.
        /// </summary>
        public static Mesh StoneFlakeUnit(int variant)
        {
            int v = Mathf.Abs(variant) % 4;
            string key = "flakeUnit" + v;
            Mesh cached;
            if (meshCache.TryGetValue(key, out cached) && cached != null)
                return cached;

            Mesh mesh = BuildAngularChunk("Res_StoneFlake" + v, 5300 + v, 0.38f, true);
            meshCache[key] = mesh;
            return mesh;
        }

        // ── 공용 빌더 ─────────────────────────────────────────────────────────
        /// <summary>
        /// 마디(또는 옹이)가 있는 기둥을 만든다. bandCount개의 마디마다 링을 3장 넣어
        /// "바로 아래를 0.93으로 조이고 → 마디에서 bandBulge로 부풀리고 → 위를 1.04로 넓게" 잇는다.
        /// 이 조임-부풂 실루엣이 대나무를 대나무로 읽히게 하는 유일한 신호다(색은 밤에 안 보인다).
        /// leanX는 t²에 비례해 휘므로 밑동은 곧고 위로 갈수록 기운다.
        /// </summary>
        private static Mesh BuildSegmentedStem(string name, int bandCount, float yBottom, float yTop,
            float radiusBottom, float radiusTop, float leanX, float bandBulge, int sides, float uvTile)
        {
            var ratios = new List<float>();
            ratios.Add(0f);
            for (int i = 0; i < bandCount; i++)
            {
                float t = (i + 0.75f) / (bandCount + 0.55f);
                ratios.Add(Mathf.Clamp(t - 0.035f, 0.01f, 0.96f));
                ratios.Add(Mathf.Clamp(t, 0.02f, 0.97f));
                ratios.Add(Mathf.Clamp(t + 0.040f, 0.03f, 0.98f));
            }
            ratios.Add(1f);

            var centers = new Vector3[ratios.Count];
            var radii = new float[ratios.Count];
            for (int i = 0; i < ratios.Count; i++)
            {
                float t = ratios[i];
                float factor = 1f;
                if (i > 0 && i < ratios.Count - 1)
                {
                    int slot = (i - 1) % 3;
                    factor = slot == 0 ? 0.93f : (slot == 1 ? bandBulge : 1.04f);
                }

                centers[i] = new Vector3(leanX * t * t, Mathf.Lerp(yBottom, yTop, t), 0f);
                radii[i] = Mathf.Max(0.004f, Mathf.Lerp(radiusBottom, radiusTop, t) * factor);
            }

            var builder = new MeshBuilder();
            builder.AddTube(centers, radii, sides, true, true, uvTile);
            return builder.Finish(name);
        }

        /// <summary>
        /// 정이십면체 기반 각진 덩어리. jitter는 꼭짓점별 반지름 흔들림 폭(0~1)이고, 시드가 같으면
        /// 항상 같은 모양이 나온다(UnityEngine.Random을 쓰지 않는다 - 재현성 규칙).
        /// fitBox가 true면 축마다 따로 정규화해 [-0.5, 0.5]³ 상자를 채우고,
        /// false면 y 반지름만 0.5로 맞춘다(구 규격의 접지 계산과 정확히 일치시키기 위해서다).
        /// </summary>
        private static Mesh BuildAngularChunk(string name, int seed, float jitter, bool fitBox)
        {
            const float phi = 1.618034f;
            var points = new[]
            {
                new Vector3(-1f, phi, 0f), new Vector3(1f, phi, 0f), new Vector3(-1f, -phi, 0f), new Vector3(1f, -phi, 0f),
                new Vector3(0f, -1f, phi), new Vector3(0f, 1f, phi), new Vector3(0f, -1f, -phi), new Vector3(0f, 1f, -phi),
                new Vector3(phi, 0f, -1f), new Vector3(phi, 0f, 1f), new Vector3(-phi, 0f, -1f), new Vector3(-phi, 0f, 1f),
            };

            var faces = new[]
            {
                0, 11, 5,  0, 5, 1,   0, 1, 7,   0, 7, 10,  0, 10, 11,
                1, 5, 9,   5, 11, 4,  11, 10, 2, 10, 7, 6,  7, 1, 8,
                3, 9, 4,   3, 4, 2,   3, 2, 6,   3, 6, 8,   3, 8, 9,
                4, 9, 5,   2, 4, 11,  6, 2, 10,  8, 6, 7,   9, 8, 1,
            };

            var random = new System.Random(seed);
            for (int i = 0; i < points.Length; i++)
            {
                float scale = 1f + ((float)random.NextDouble() * 2f - 1f) * jitter;
                points[i] = points[i].normalized * scale;
            }

            // 정규화. 축마다 최대 반지름을 정확히 0.5로 맞춘다 - 이걸 균일 배율로 하면 꼭짓점 흔들림
            // 때문에 가로가 세로의 최대 1.9배까지 커져서, 콜라이더(= 채집 판정)보다 눈에 띄게 큰 돌이 나온다.
            // y를 항상 따로 맞추는 이유는 접지 계산(GetHalfHeight)이 y 반지름 0.5를 전제하기 때문이다.
            float maxX = 0.0001f;
            float maxY = 0.0001f;
            float maxZ = 0.0001f;
            for (int i = 0; i < points.Length; i++)
            {
                maxX = Mathf.Max(maxX, Mathf.Abs(points[i].x));
                maxY = Mathf.Max(maxY, Mathf.Abs(points[i].y));
                maxZ = Mathf.Max(maxZ, Mathf.Abs(points[i].z));
            }

            // 구 규격에서는 가로 두 축을 같은 배율로 줄여 바닥 윤곽이 한쪽으로 늘어나지 않게 한다.
            float scaleX = fitBox ? 0.5f / maxX : 0.5f / Mathf.Max(maxX, maxZ);
            float scaleZ = fitBox ? 0.5f / maxZ : 0.5f / Mathf.Max(maxX, maxZ);
            float scaleY = 0.5f / maxY;
            for (int i = 0; i < points.Length; i++)
                points[i] = new Vector3(points[i].x * scaleX, points[i].y * scaleY, points[i].z * scaleZ);

            var builder = new MeshBuilder();
            for (int f = 0; f + 2 < faces.Length; f += 3)
            {
                Vector3 a = points[faces[f]];
                Vector3 b = points[faces[f + 1]];
                Vector3 c = points[faces[f + 2]];
                // 원점을 감싸는 볼록 덩어리라 무게중심 방향이 곧 바깥 방향이다.
                builder.AddFace(a, b, c, (a + b + c) / 3f);
            }
            return builder.Finish(name);
        }

        /// <summary>
        /// 정점/UV/삼각형을 모아 메시 하나로 마무리하는 최소 빌더. 삼각형을 넣을 때마다 기하 법선을
        /// 기준 방향과 비교해 감김을 바로잡으므로, 좌표계 손잡이 방향을 착각해도 안쪽으로 뒤집히지 않는다.
        /// </summary>
        private class MeshBuilder
        {
            private readonly List<Vector3> vertices = new List<Vector3>();
            private readonly List<Vector2> uvs = new List<Vector2>();
            private readonly List<int> triangles = new List<int>();

            /// <summary>중심선(centers)과 반지름(radii)을 따라가는 관을 하나 잇는다.</summary>
            public void AddTube(Vector3[] centers, float[] radii, int sides, bool capStart, bool capEnd, float uvTile)
            {
                if (centers == null || radii == null || centers.Length < 2 || radii.Length != centers.Length || sides < 3)
                    return;

                Vector3 axis = centers[centers.Length - 1] - centers[0];
                if (axis.sqrMagnitude < 0.0000001f)
                    axis = Vector3.up;
                axis = axis.normalized;

                Vector3 helper = Mathf.Abs(axis.y) > 0.9f ? Vector3.forward : Vector3.up;
                Vector3 right = Vector3.Cross(helper, axis).normalized;
                Vector3 forward = Vector3.Cross(axis, right);

                int start = vertices.Count;
                int stride = sides + 1; // 이음매(seam)에서 UV가 끊기도록 정점을 한 개 겹쳐 둔다
                for (int r = 0; r < centers.Length; r++)
                {
                    for (int s = 0; s <= sides; s++)
                    {
                        float angle = (float)s / sides * Mathf.PI * 2f;
                        Vector3 direction = right * Mathf.Cos(angle) + forward * Mathf.Sin(angle);
                        vertices.Add(centers[r] + direction * radii[r]);
                        uvs.Add(new Vector2((float)s / sides, (float)r / (centers.Length - 1) * uvTile));
                    }
                }

                for (int r = 0; r + 1 < centers.Length; r++)
                {
                    for (int s = 0; s < sides; s++)
                    {
                        int a0 = start + r * stride + s;
                        int a1 = a0 + 1;
                        int b0 = a0 + stride;
                        int b1 = b0 + 1;
                        float mid = ((float)s + 0.5f) / sides * Mathf.PI * 2f;
                        Vector3 outward = right * Mathf.Cos(mid) + forward * Mathf.Sin(mid);
                        AddTriangle(a0, b0, b1, outward);
                        AddTriangle(a0, b1, a1, outward);
                    }
                }

                if (capStart)
                    AddCap(start, sides, centers[0], -axis);
                if (capEnd)
                    AddCap(start + (centers.Length - 1) * stride, sides, centers[centers.Length - 1], axis);
            }

            /// <summary>사각면 하나. doubleSided면 감김을 뒤집은 사본을 함께 넣어 양쪽에서 보이게 한다.</summary>
            public void AddQuad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 reference, bool doubleSided)
            {
                AddQuadFace(a, b, c, d, reference);
                if (doubleSided)
                    AddQuadFace(a, b, c, d, -reference);
            }

            /// <summary>평면 셰이딩용 삼각면 하나(정점을 공유하지 않아 면마다 각이 선다).</summary>
            public void AddFace(Vector3 a, Vector3 b, Vector3 c, Vector3 reference)
            {
                int index = vertices.Count;
                vertices.Add(a);
                vertices.Add(b);
                vertices.Add(c);
                uvs.Add(new Vector2(a.x + 0.5f, a.z + 0.5f));
                uvs.Add(new Vector2(b.x + 0.5f, b.z + 0.5f));
                uvs.Add(new Vector2(c.x + 0.5f, c.z + 0.5f));
                AddTriangle(index, index + 1, index + 2, reference);
            }

            public Mesh Finish(string name)
            {
                var mesh = new Mesh();
                mesh.name = name;
                mesh.SetVertices(vertices);
                mesh.SetUVs(0, uvs);
                mesh.SetTriangles(triangles, 0);
                mesh.RecalculateNormals();
                mesh.RecalculateBounds();
                return mesh;
            }

            private void AddQuadFace(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 reference)
            {
                int index = vertices.Count;
                vertices.Add(a);
                vertices.Add(b);
                vertices.Add(c);
                vertices.Add(d);
                uvs.Add(new Vector2(0f, 0f));
                uvs.Add(new Vector2(0f, 1f));
                uvs.Add(new Vector2(1f, 1f));
                uvs.Add(new Vector2(1f, 0f));
                AddTriangle(index, index + 1, index + 2, reference);
                AddTriangle(index, index + 2, index + 3, reference);
            }

            private void AddCap(int ringStart, int sides, Vector3 center, Vector3 reference)
            {
                int centerIndex = vertices.Count;
                vertices.Add(center);
                uvs.Add(new Vector2(0.5f, 0.5f));
                for (int s = 0; s < sides; s++)
                    AddTriangle(centerIndex, ringStart + s, ringStart + s + 1, reference);
            }

            /// <summary>
            /// 삼각형 하나를 감김 방향까지 맞춰 넣는다(IslandMeshGenerator.AddOrientedTriangle과 같은 방식).
            /// 본문은 WorldMeshBuilder.AddOrientedTriangle(정본)에 있다.
            /// </summary>
            private void AddTriangle(int i0, int i1, int i2, Vector3 reference)
            {
                WorldMeshBuilder.AddOrientedTriangle(vertices, triangles, i0, i1, i2, reference);
            }
        }
    }
}
