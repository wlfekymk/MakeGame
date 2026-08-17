using UnityEngine;

namespace MakeGame.Systems
{
    /// <summary>
    /// 시작 섬 해안의 폭발한 여객기 잔해. 순수 배경 오브젝트라 상호작용/게임플레이 값이 하나도 없고,
    /// 여기서 만드는 것은 시각 파츠 + 걸어 다닐 수 있는 BoxCollider들 + 연기뿐이다.
    /// WorldMapManager가 AddComponent로 붙이며, 인스펙터에서 채울 필드는 없다.
    ///
    /// 경비행기 잔해(AircraftWreck)와 달리 형태를 절차 메시로 조립하지 않는다 - 여객기는 실물 OBJ
    /// (Models/airliner_wreck_a, `o` 오브젝트 5개)가 있고, 정점이 전부 잔해 로컬 미터 좌표로
    /// 구워져 있어 파츠는 예외 없이 위치 0 · 회전 identity · 스케일 1로 붙는다
    /// (호출부에서 회전/스케일을 다시 주면 "메시를 바꿨는데 호출부 스케일이 그대로여서 찌그러진"
    /// 과거 사고를 반복하게 된다 - ResourceVisualLibrary 주석 참고).
    /// </summary>
    public class AirlinerWreck : MonoBehaviour
    {
        /// <summary>
        /// OBJ의 `o` 오브젝트 이름과 메시 이름 매칭 키. 순서는 아래 색/머티리얼 표와 일대일이다.
        /// </summary>
        private static readonly string[] PartMeshNames =
        {
            "airliner_hull",   // 흰 동체·날개·꼬리
            "airliner_dark",   // 엔진·파편·절단면
            "airliner_stripe", // 빨간 리버리
            "airliner_window", // 창
            "airliner_soot",   // 지면 그을음
        };

        /// <summary>
        /// 공유 메시 캐시. 잔해는 월드에 하나뿐이지만 도메인 리로드로 비워져도 아래 프로브가
        /// 자연히 다시 채우므로(래치 없음) 별도 복구 코드가 필요 없다.
        /// </summary>
        private static readonly Mesh[] partMeshes = new Mesh[5];

        /// <summary>프레임당 1회 프로브 가드(TryGetBambooModel과 같은 규칙). -1 = 아직 프로브 안 함.</summary>
        private static int probeFrame = -1;

        /// <summary>
        /// 콜라이더/연기 위치의 정렬 오프셋. 모델 빌드 단계에서 파츠 배치 좌표(로컬)를 최종 메시
        /// 좌표로 옮기며 생긴 값이라, 명세의 로컬 center에 이 값을 더해야 메시와 정확히 겹친다.
        /// (연기 위치 상수에는 이미 반영돼 있다.)
        /// </summary>
        private static readonly Vector3 AlignOffset = new Vector3(1.8773f, 0.0473f, -1.2652f);

        /// <summary>시각+콜라이더+연기를 이미 만들었는지(1회 빌드 가드). 시각 전용이라 세이브와 무관하다.</summary>
        private bool built = false;

        // Resources.Load 계열은 필드 초기화식/생성자에서 부르면 안 된다(생성자 시점이라 null이 온다 -
        // AGENT_BRIEF 4장). 그래서 로드는 전부 Start/Update에서만 시도한다.
        private void Start()
        {
            TryBuild();
        }

        /// <summary>
        /// 모델이 아직 로드되지 않았으면 로드될 때까지 매 프레임 재시도한다. 실패를 latch하면
        /// (에셋 임포트가 한 프레임 늦는 에디터 상황 등에서) 잔해가 영영 빈 껍데기로 남는다.
        /// 빌드가 끝나면 더 할 일이 없으므로 컴포넌트를 꺼서 Update 비용을 없앤다.
        /// </summary>
        private void Update()
        {
            TryBuild();
        }

        private void TryBuild()
        {
            if (built)
            {
                enabled = false;
                return;
            }

            if (!TryLoadMeshes())
                return;

            built = true;
            enabled = false;

            var root = new GameObject("AirlinerWreckVisual");
            root.transform.SetParent(transform, false);
            root.transform.localPosition = Vector3.zero;
            root.transform.localRotation = Quaternion.identity;
            root.transform.localScale = Vector3.one; // 균등 스케일 = 자식이 회전해도 전단이 없다

            BuildVisualParts(root.transform);
            BuildColliders(root.transform);
            BuildSmoke(root.transform);
        }

        /// <summary>
        /// OBJ에서 `o` 오브젝트 5개의 공유 메시를 이름으로 꺼낸다. 다 채워져 있으면 즉시 true.
        /// 프로브는 프레임당 1회만 한다(가드가 없으면 로드 실패가 지속되는 동안 매 프레임
        /// LoadAll이 불린다). 실패를 영구 캐시하지 않으므로 도메인 리로드 후에도 자연 복구된다.
        /// </summary>
        private static bool TryLoadMeshes()
        {
            bool anyMissing = false;
            for (int i = 0; i < partMeshes.Length; i++)
            {
                if (partMeshes[i] == null)
                    anyMissing = true;
            }

            if (anyMissing && probeFrame != Time.frameCount)
            {
                probeFrame = Time.frameCount;

                // 로드는 반드시 Load<GameObject> + GetComponentsInChildren<MeshFilter> 경로다.
                // 실사고(0.2.14 검증에서 발견): Resources.LoadAll<Mesh>(파일 경로)는 이 프로젝트의
                // 모델 에셋에서 **빈 배열**을 돌려줘 잔해가 영영 안 만들어졌다. 검증된 로더
                // (ResourceVisualLibrary.TryLoadTwoPartModel)와 같은 방식만 쓴다.
                // 확장자를 붙이면 항상 null이다(AssetPipeline 3장).
                var prefab = Resources.Load<GameObject>("Models/airliner_wreck_a");
                if (prefab != null)
                {
                    var filters = prefab.GetComponentsInChildren<MeshFilter>(true);
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
                                ((meshName != null && meshName.Contains(PartMeshNames[i])) ||
                                 (nodeName != null && nodeName.Contains(PartMeshNames[i]))))
                            {
                                partMeshes[i] = mesh;
                                break;
                            }
                        }
                    }
                }
            }

            // 5장 전부 있어야 빌드한다 - 같은 OBJ의 서브에셋이라 일부만 로드되는 상황은 임포트가
            // 아직 끝나지 않았다는 뜻이고, 반쪽짜리 잔해를 만들었다가 다시 지우는 것보다 한 프레임
            // 더 기다리는 쪽이 싸다.
            for (int i = 0; i < partMeshes.Length; i++)
            {
                if (partMeshes[i] == null)
                    return false;
            }
            return true;
        }

        private static void BuildVisualParts(Transform root)
        {
            // 색 배합은 AircraftWreck과 같은 문법(팔레트 색의 명도 변주)이되, 여객기는 더 크고 하얗게 -
            // 경비행기(1.08f)보다 밝은 1.15f라 두 잔해가 같은 금속 팔레트 안에서도 구분된다.
            Color hull = ResourceVisualLibrary.Shade(StructureVisualBuilder.SalvageMetal, 1.15f);
            Color dark = ResourceVisualLibrary.Shade(StructureVisualBuilder.SalvageMetal, 0.55f);
            Color stripe = ResourceVisualLibrary.Shade(StructureVisualBuilder.DangerRed, 0.80f);
            Color window = ResourceVisualLibrary.Shade(StructureVisualBuilder.SalvageMetal, 0.25f);
            Color soot = ResourceVisualLibrary.Shade(StructureVisualBuilder.SalvageMetal, 0.16f);

            // 머티리얼은 전부 월드 공유 캐시에서 받는다(여기서 새로 만들지 않는다 - CreateMeshPart 주석).
            Material[] materials =
            {
                ResourceVisualLibrary.GetMaterial(hull, "metal"),
                ResourceVisualLibrary.GetMaterial(dark, "metal"),
                ResourceVisualLibrary.GetMaterial(stripe, "metal"),
                ResourceVisualLibrary.GetMaterial(window, "noise"),
                ResourceVisualLibrary.GetMaterial(soot, "noise"),
            };

            for (int i = 0; i < partMeshes.Length; i++)
            {
                StructureVisualBuilder.CreateMeshPart(root, PartMeshNames[i], partMeshes[i],
                    Vector3.zero, Vector3.one, Quaternion.identity, materials[i]);
            }
        }

        /// <summary>
        /// 걸어 다닐 수 있는 BoxCollider 7개. 값은 모델 빌드에서 산출한 실측치를 코드에 박아 둔 것이라
        /// 메시(airliner_wreck_a.obj)를 다시 구우면 이 표도 같이 갱신해야 한다.
        /// 회전이 있는 박스는 BoxCollider가 회전을 못 가지므로 자식 transform의 회전으로 준다 -
        /// 자식 위치를 박스 중심에 두고 콜라이더 center를 0으로 두면 회전축이 곧 박스 중심이 된다.
        /// 전부 기본 레이어 · isTrigger=false(플레이어가 밟고 올라가는 실제 충돌면이다).
        /// </summary>
        private static void BuildColliders(Transform root)
        {
            AddBoxCollider(root, "fuse_front", new Vector3(0.0f, 1.44f, 7.6f), new Vector3(2.7f, 2.75f, 12.4f),
                Quaternion.identity);
            AddBoxCollider(root, "fuse_rear", new Vector3(-4.6f, 1.52f, -5.0f), new Vector3(2.7f, 2.75f, 9.8f),
                Quaternion.Euler(0f, 26f, 0f));
            AddBoxCollider(root, "tail_fin", new Vector3(-7.5f, 4.6f, -9.4f), new Vector3(0.5f, 5.0f, 2.6f),
                Quaternion.Euler(0f, 26f, 0f));
            // 오른 날개는 올라갈 수 있는 경사로다. 날개 기울기(루트 y1.02 → 끝 y1.95 상승)를 X축 롤로
            // 근사한다 - 끝이 +X 쪽으로 높으므로 z 롤은 음수다. 두툼한 박스(두께 0.4)라 경사면 위에서
            // 발이 빠지지 않는다.
            AddBoxCollider(root, "wing_right_ramp", new Vector3(5.8f, 1.42f, 4.9f), new Vector3(9.6f, 0.4f, 3.6f),
                Quaternion.Euler(0f, 0f, -5.5f));
            AddBoxCollider(root, "wing_left_torn", new Vector3(-11.6f, 0.22f, 6.2f), new Vector3(7.4f, 0.5f, 3.2f),
                Quaternion.Euler(0f, -32f, 0f));
            AddBoxCollider(root, "engine_attached", new Vector3(5.4f, 0.72f, 4.9f), new Vector3(1.4f, 1.4f, 2.8f),
                Quaternion.identity);
            AddBoxCollider(root, "engine_torn", new Vector3(-2.6f, 0.95f, 3.4f), new Vector3(1.4f, 1.4f, 2.8f),
                Quaternion.Euler(0f, 55f, 0f));
        }

        /// <summary>명세의 로컬 center에 AlignOffset을 더해 메시 좌표로 옮긴 뒤 자식 박스를 하나 붙인다.</summary>
        private static void AddBoxCollider(Transform parent, string name, Vector3 localCenter, Vector3 size,
            Quaternion localRotation)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localCenter + AlignOffset;
            go.transform.localRotation = localRotation;
            go.transform.localScale = Vector3.one;

            var box = go.AddComponent<BoxCollider>();
            box.center = Vector3.zero;
            box.size = size;
            box.isTrigger = false;
        }

        /// <summary>
        /// 타다 남은 연기 2곳: 전방 절단면 근처 + 뜯긴 엔진. 좌표에는 AlignOffset이 이미 반영돼 있다.
        /// 모닥불 연기(안전 신호)와 헷갈리지 않도록 옅고 느린 것은 CreateWreckSmoke가 보장한다
        /// (AircraftWreck 끝부분과 같은 사용법 - null 가드 후 Play).
        /// </summary>
        private static void BuildSmoke(Transform root)
        {
            var frontSmoke = EffectBuilder.CreateWreckSmoke(root, new Vector3(1.9f, 1.6f, 0.6f));
            if (frontSmoke != null)
                frontSmoke.Play();

            var engineSmoke = EffectBuilder.CreateWreckSmoke(root, new Vector3(-0.7f, 1.1f, 2.1f));
            if (engineSmoke != null)
                engineSmoke.Play();
        }
    }
}
