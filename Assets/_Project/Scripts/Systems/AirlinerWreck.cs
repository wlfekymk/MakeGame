using System.Collections.Generic;
using UnityEngine;
using MakeGame.Data;

namespace MakeGame.Systems
{
    /// <summary>
    /// 시작 섬 해안의 폭발한 여객기 잔해. 시각 파츠 + 걸어 들어갈 수 있는 BoxCollider들 + 연기를
    /// 만들고, 1회 한정 비상 물자 수색(TrySearch)을 제공한다. 수색은 InteractionController가
    /// 부르고 프롬프트는 InteractionPromptUI가 HasSalvage로 판정한다.
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

        /// <summary>
        /// [실사고 0.2.13] Unity 6.5 OBJ 임포터는 `o` 오브젝트 5개를 **"default" 메시 한 장의
        /// 서브메시 5개**로 합쳐 온다(진단 덤프: "MeshFilter 1개 [default/default]"). 이때는
        /// 렌더러 하나에 머티리얼 5장을 배열로 주면 파츠별 색이 살아난다 - 대나무/야자수의
        /// subMeshCount>=2 분기(IslandResourceSpawner.Visuals.cs:349, Vegetation.cs:1052)와 같은 규칙.
        /// 이름별 개별 메시(예전 임포터 동작)와 병합 메시 둘 다 지원한다.
        /// </summary>
        private static Mesh mergedMesh;

        /// <summary>프레임당 1회 프로브 가드(TryGetBambooModel과 같은 규칙). -1 = 아직 프로브 안 함.</summary>
        private static int probeFrame = -1;

        /// <summary>프로브 시도 횟수. 진단 경고(아래) 발화 시점 판정용 - 도메인 리로드로 초기화돼도 무해.</summary>
        private static int probeAttempts = 0;

        /// <summary>진단 경고를 이미 냈는지(스팸 방지). 실사고 추적용 - 원인이 잡히면 이 진단은 유지비 0이다.</summary>
        private static bool probeWarned = false;

        /// <summary>
        /// 콜라이더/연기 위치의 정렬 오프셋. 모델 빌드 단계에서 파츠 배치 좌표(로컬)를 최종 메시
        /// 좌표로 옮기며 생긴 값이라, 명세의 로컬 center에 이 값을 더해야 메시와 정확히 겹친다.
        /// v2 모델(실물급 37×9.6×39m) 재제작으로 값이 갱신됐다 - 메시를 다시 구우면 이 값과
        /// 콜라이더 표도 같이 갱신해야 한다.
        /// </summary>
        private static readonly Vector3 AlignOffset = new Vector3(2.8246f, 0f, -2.8362f);

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

            // 빌드 확인 로그(정보 수준 - 스모크 경고 집계에 잡히지 않는다). 실사고 0.2.13
            // "여객기가 없어" 추적에서 경고로 썼다가 원인(임포터 병합) 확정 후 강등했다.
            Debug.Log("[AirlinerWreck] 시각 빌드 완료 @ " + transform.position
                + " yaw " + transform.eulerAngles.y.ToString("F0")
                + (mergedMesh != null ? " (병합 메시 sub" + mergedMesh.subMeshCount + ")" : " (개별 메시 5)"));
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
                probeAttempts++;

                // 로드는 반드시 Load<GameObject> + GetComponentsInChildren<MeshFilter> 경로다.
                // 실사고(0.2.14 검증에서 발견): Resources.LoadAll<Mesh>(파일 경로)는 이 프로젝트의
                // 모델 에셋에서 **빈 배열**을 돌려줘 잔해가 영영 안 만들어졌다. 검증된 로더
                // (ResourceVisualLibrary.TryLoadTwoPartModel)와 같은 방식만 쓴다.
                // 확장자를 붙이면 항상 null이다(AssetPipeline 3장).
                var prefab = Resources.Load<GameObject>("Models/airliner_wreck_a");
                if (prefab != null)
                {
                    var filters = prefab.GetComponentsInChildren<MeshFilter>(true);

                    // 병합 임포트(현재 Unity 6.5의 실제 동작): MeshFilter 1개 = 서브메시 5개.
                    if (filters.Length == 1 && filters[0] != null && filters[0].sharedMesh != null)
                    {
                        mergedMesh = filters[0].sharedMesh;
                    }

                    // 개별 메시 임포트(임포터 동작이 되돌아올 경우의 방어): 이름으로 가른다.
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
            bool complete = mergedMesh != null;
            if (!complete)
            {
                complete = true;
                for (int i = 0; i < partMeshes.Length; i++)
                {
                    if (partMeshes[i] == null)
                        complete = false;
                }
            }

            // 진단(실사고 추적): 프로브 300회(에디터 기준 약 5초)가 지나도 5장이 안 모이면 원인을
            // 한 번만 자세히 남긴다. 사용자 보고 "여객기가 없어"의 원인 후보는 (a) 프리팹 로드 실패
            // (b) 임포터가 o 오브젝트를 합침/이름 변경 (c) 일부 파츠 누락 - 아래 덤프가 셋을 가른다.
            if (!complete && !probeWarned && probeAttempts >= 300)
            {
                probeWarned = true;
                var prefabDump = Resources.Load<GameObject>("Models/airliner_wreck_a");
                if (prefabDump == null)
                {
                    Debug.LogWarning("[AirlinerWreck] 진단: Resources.Load<GameObject>(Models/airliner_wreck_a)가 null - 에셋 경로/임포트 문제.");
                }
                else
                {
                    var fs = prefabDump.GetComponentsInChildren<MeshFilter>(true);
                    var sb = new System.Text.StringBuilder();
                    sb.Append("[AirlinerWreck] 진단: 프리팹은 로드됨, MeshFilter ").Append(fs.Length).Append("개 [");
                    for (int m = 0; m < fs.Length; m++)
                    {
                        Mesh mm = fs[m] != null ? fs[m].sharedMesh : null;
                        // 주의: isReadable=0 메시라 triangles 접근은 에러 로그를 낸다 - 서브메시 수만 본다.
                        sb.Append(fs[m] != null ? fs[m].gameObject.name : "?")
                          .Append('/').Append(mm != null ? mm.name : "null")
                          .Append("/sub").Append(mm != null ? mm.subMeshCount : 0).Append("; ");
                    }
                    sb.Append("] merged=").Append(mergedMesh != null ? "O" : "X").Append(" 매칭된 파츠: ");
                    for (int i = 0; i < partMeshes.Length; i++)
                        sb.Append(PartMeshNames[i]).Append('=').Append(partMeshes[i] != null ? "O" : "X").Append(' ');
                    Debug.LogWarning(sb.ToString());
                }
            }
            return complete;
        }

        private static void BuildVisualParts(Transform root)
        {
            // [사용자 피드백 "회색이다"] 팔레트 명도 변주(SalvageMetal 기반)를 버리고 명시 색으로 -
            // 흰 동체 + 빨간 리버리가 여객기의 정체성이라 다른 잔해와의 팔레트 통일보다 우선한다.
            Color hull = new Color(0.92f, 0.93f, 0.94f);   // 흰 동체
            Color dark = new Color(0.24f, 0.26f, 0.28f);   // 엔진·파편·절단면
            Color stripe = new Color(0.68f, 0.13f, 0.11f); // 빨간 리버리
            Color window = new Color(0.08f, 0.10f, 0.13f); // 창
            Color soot = new Color(0.05f, 0.05f, 0.05f);   // 지면 그을음

            // 머티리얼은 전부 월드 공유 캐시에서 받는다(여기서 새로 만들지 않는다 - CreateMeshPart 주석).
            Material[] materials =
            {
                ResourceVisualLibrary.GetMaterial(hull, "metal"),
                ResourceVisualLibrary.GetMaterial(dark, "metal"),
                ResourceVisualLibrary.GetMaterial(stripe, "metal"),
                ResourceVisualLibrary.GetMaterial(window, "noise"),
                ResourceVisualLibrary.GetMaterial(soot, "noise"),
            };

            if (mergedMesh != null)
            {
                // 병합 임포트 경로: 렌더러 하나 + 머티리얼 배열. 서브메시 순서는 OBJ의 `o` 순서
                // (hull, dark, stripe, window, soot)를 따른다 - airliner.py의 objs 순서가 그 근거다.
                var part = StructureVisualBuilder.CreateMeshPart(root, "airliner_body", mergedMesh,
                    Vector3.zero, Vector3.one, Quaternion.identity, materials[0]);
                var renderer = part != null ? part.GetComponent<MeshRenderer>() : null;
                if (renderer != null && mergedMesh.subMeshCount >= 2)
                {
                    int count = mergedMesh.subMeshCount;
                    var slots = new Material[count];
                    for (int s = 0; s < count; s++)
                        slots[s] = materials[Mathf.Min(s, materials.Length - 1)];
                    renderer.sharedMaterials = slots;
                }
                return;
            }

            for (int i = 0; i < partMeshes.Length; i++)
            {
                StructureVisualBuilder.CreateMeshPart(root, PartMeshNames[i], partMeshes[i],
                    Vector3.zero, Vector3.one, Quaternion.identity, materials[i]);
            }
        }

        /// <summary>
        /// BoxCollider 15개. 값은 v2 모델 빌드에서 산출한 실측치를 코드에 박아 둔 것이라
        /// 메시(airliner_wreck_a.obj)를 다시 구우면 이 표도 같이 갱신해야 한다.
        ///
        /// v2는 절단부가 개방된 양면 셸 + 내부 바닥판이라 **사람이 걸어 들어간다** - 그래서 예전
        /// fuse_front 같은 동체 통짜 박스는 전부 버리고 바닥/벽/천장을 분해해서 깐다(통짜 박스가
        /// 하나라도 남으면 입구가 보이는데 들어갈 수 없는 투명 벽 사고가 난다).
        /// 회전이 있는 박스는 BoxCollider가 회전을 못 가지므로 자식 transform의 회전으로 준다 -
        /// 자식 위치를 박스 중심에 두고 콜라이더 center를 0으로 두면 회전축이 곧 박스 중심이 된다.
        /// 전부 기본 레이어 · isTrigger=false(플레이어가 밟고 걸어 다니는 실제 충돌면이다).
        /// </summary>
        private static void BuildColliders(Transform root)
        {
            // 전방 객실: 바닥(윗면 y=0.95가 보행면) + 양쪽 벽 + 천장 + 조종석 노즈 막음.
            AddBoxCollider(root, "cabin_floor_front", new Vector3(0f, 0.435f, 9.4f),
                new Vector3(3.2f, 1.03f, 14.4f), Quaternion.identity);
            AddBoxCollider(root, "cabin_wall_front_L", new Vector3(-1.82f, 2.4f, 9.4f),
                new Vector3(0.4f, 2.9f, 14.4f), Quaternion.identity);
            AddBoxCollider(root, "cabin_wall_front_R", new Vector3(1.82f, 2.4f, 9.4f),
                new Vector3(0.4f, 2.9f, 14.4f), Quaternion.identity);
            AddBoxCollider(root, "cabin_ceiling_front", new Vector3(0f, 4.05f, 9.4f),
                new Vector3(3.2f, 0.5f, 14.4f), Quaternion.identity);
            AddBoxCollider(root, "nose_block", new Vector3(0f, 1.35f, 19.3f),
                new Vector3(2.6f, 2.6f, 3.6f), Quaternion.identity);

            // 후방 객실(yaw 24도로 꺾여 나뒹군 동체): 같은 바닥/벽/천장 구성 + 꼬리 막음 + 수직 꼬리날개.
            AddBoxCollider(root, "cabin_floor_rear", new Vector3(-6.93f, 0.43f, -6.12f),
                new Vector3(3.2f, 1.03f, 11.4f), Quaternion.Euler(0f, 24f, 0f));
            AddBoxCollider(root, "cabin_wall_rear_L", new Vector3(-8.59f, 2.4f, -5.38f),
                new Vector3(0.4f, 2.9f, 11.4f), Quaternion.Euler(0f, 24f, 0f));
            AddBoxCollider(root, "cabin_wall_rear_R", new Vector3(-5.26f, 2.4f, -6.86f),
                new Vector3(0.4f, 2.9f, 11.4f), Quaternion.Euler(0f, 24f, 0f));
            AddBoxCollider(root, "cabin_ceiling_rear", new Vector3(-6.93f, 4.05f, -6.12f),
                new Vector3(3.2f, 0.5f, 11.4f), Quaternion.Euler(0f, 24f, 0f));
            AddBoxCollider(root, "tail_block", new Vector3(-10.14f, 2.6f, -13.34f),
                new Vector3(2.4f, 2.4f, 4.6f), Quaternion.Euler(0f, 24f, 0f));
            AddBoxCollider(root, "tail_fin", new Vector3(-10.71f, 6.6f, -14.62f),
                new Vector3(0.6f, 6.2f, 3.2f), Quaternion.Euler(0f, 24f, 0f));

            // 오른 날개는 올라갈 수 있는 경사로다 - 날개 기울기를 z 롤 -4.9도로 근사한다.
            // 두툼한 박스(두께 0.5)라 경사면 위에서 발이 빠지지 않는다.
            AddBoxCollider(root, "wing_right_ramp", new Vector3(8.2f, 2.28f, 7.0f),
                new Vector3(13.6f, 0.5f, 4.6f), Quaternion.Euler(0f, -19f, -4.9f));
            AddBoxCollider(root, "wing_left_torn", new Vector3(-16.8f, 0.31f, 10.4f),
                new Vector3(10.4f, 0.7f, 4.4f), Quaternion.Euler(0f, -34f, 0f));
            AddBoxCollider(root, "engine_attached", new Vector3(7.6f, 1.02f, 6.6f),
                new Vector3(1.9f, 1.9f, 3.8f), Quaternion.identity);
            AddBoxCollider(root, "engine_torn", new Vector3(-3.4f, 0.95f, 4.6f),
                new Vector3(1.9f, 1.9f, 3.8f), Quaternion.Euler(0f, 50f, 0f));
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
        /// 타다 남은 연기 3곳: 전방 절단면 + 후방 절단면 + 뜯긴 엔진. 좌표는 명세 로컬값에
        /// AlignOffset을 더해 메시 좌표로 옮긴다(콜라이더와 같은 규칙).
        /// 모닥불 연기(안전 신호)와 헷갈리지 않도록 옅고 느린 것은 CreateWreckSmoke가 보장한다
        /// (AircraftWreck 끝부분과 같은 사용법 - null 가드 후 Play).
        /// </summary>
        private static void BuildSmoke(Transform root)
        {
            AddSmoke(root, new Vector3(0.6f, 2.3f, 1.2f));    // 전방 동체 절단면
            AddSmoke(root, new Vector3(-4.21f, 2.3f, -0.76f)); // 후방 동체 절단면
            AddSmoke(root, new Vector3(-3.4f, 1.7f, 4.6f));   // 뜯긴 엔진
        }

        private static void AddSmoke(Transform root, Vector3 localPosition)
        {
            var smoke = EffectBuilder.CreateWreckSmoke(root, localPosition + AlignOffset);
            if (smoke != null)
                smoke.Play();
        }

        // ---------------------------------------------------------------------------------------
        // 수색 상호작용 (1회 한정 비상 물자)
        // ---------------------------------------------------------------------------------------

        /// <summary>
        /// 수색 지급표: (레지스트리 itemName, 개수). 이름은 전부 ItemDataRegistry에 실재하는
        /// 에셋으로 확인했다(Item_천조각/Item_금속조각/Item_비상식량/Item_생수.asset).
        /// </summary>
        private static readonly string[] SalvageNames = { "천조각", "금속조각", "비상식량", "생수" };
        private static readonly int[] SalvageCounts = { 3, 3, 2, 2 };

        /// <summary>
        /// 아직 지급하지 않은 물자(아이템 1개당 항목 1개). null = 아직 수색 안 함.
        /// 인벤토리가 꽉 차 일부만 들어간 경우 넘친 아이템은 버리지 않고 여기 남아,
        /// 다음 수색에서 이어서 지급된다.
        ///
        /// [한계] 수색 여부는 세이브에 저장하지 않는다 - 잔해는 월드 재생성마다 새로 만들어지는
        /// 배경 오브젝트라 로드할 때마다 물자가 리셋된다. 부품 수거(잔해 해체) 시스템을 추후
        /// 확장할 때 세이브 연동을 함께 넣을 예정이다.
        /// </summary>
        private List<ItemData> pendingSalvage;

        /// <summary>아직 수색으로 얻을 물자가 남아 있는가(수색 전이면 true). InteractionPromptUI가 쓴다.</summary>
        public bool HasSalvage => pendingSalvage == null || pendingSalvage.Count > 0;

        /// <summary>
        /// 잔해에서 비상 물자를 수색한다(1회 한정). InteractionController가 부른다.
        /// DebugHud.GrantDevelopmentMaterials와 같은 패턴: 레지스트리에서 이름으로 ItemData를 찾아
        /// TryAddItem으로 넣는다(용량 존중 - 이 프로젝트는 아이템이 조용히 사라진 사고가 4번 있었다).
        /// 인벤토리가 차서 일부만 들어가면 성공(true) 처리하되, 못 넣은 아이템은 pendingSalvage에
        /// 남겨 다음 수색에서 이어받는다. 하나도 못 넣었으면 false(TryAddItem이 실패음/경고를 낸다).
        /// </summary>
        /// <returns>아이템을 하나라도 지급했으면 true.</returns>
        public bool TrySearch(MakeGame.Player.PlayerInventory inventory)
        {
            if (inventory == null)
                return false;

            if (pendingSalvage == null)
            {
                var built = BuildSalvageList();
                if (built == null)
                    return false; // 레지스트리 로드 실패 - 수색 소모 없이 다음 시도에서 재시도한다.
                pendingSalvage = built;
            }

            if (pendingSalvage.Count == 0)
                return false; // 이미 다 털었다(프롬프트는 HasSalvage로 이 상태를 미리 보여준다).

            int granted = 0;
            while (pendingSalvage.Count > 0)
            {
                // 실패 시 TryAddItem이 실패음 + AddRejected + 경고를 스스로 낸다(추가 알림 불필요).
                if (!inventory.TryAddItem(pendingSalvage[0]))
                    break;
                pendingSalvage.RemoveAt(0);
                granted++;
            }

            if (granted > 0)
            {
                Debug.Log("[AirlinerWreck] 잔해 수색: 물자 " + granted + "개 지급"
                    + (pendingSalvage.Count > 0 ? " (가방이 차서 " + pendingSalvage.Count + "개는 잔해에 남음)" : " 완료"));
            }
            return granted > 0;
        }

        /// <summary>
        /// 지급표를 실제 ItemData 목록(아이템 1개당 항목 1개)으로 편다. 레지스트리 자체가 없으면
        /// null(재시도 가능), 개별 이름이 없으면 그 항목만 빼고 경고를 남긴다.
        /// rng는 소비하지 않는다 - 지급물은 고정 표다.
        /// </summary>
        private static List<ItemData> BuildSalvageList()
        {
            var registry = ItemDataRegistry.LoadFromResources();
            if (registry == null || registry.allItems == null)
            {
                Debug.LogWarning("[AirlinerWreck] ItemDataRegistry를 불러오지 못해 수색 물자를 만들지 못했다.");
                return null;
            }

            var list = new List<ItemData>();
            for (int n = 0; n < SalvageNames.Length; n++)
            {
                ItemData found = null;
                for (int i = 0; i < registry.allItems.Count; i++)
                {
                    var candidate = registry.allItems[i];
                    if (candidate != null && candidate.itemName == SalvageNames[n])
                    {
                        found = candidate;
                        break;
                    }
                }

                if (found == null)
                {
                    Debug.LogWarning("[AirlinerWreck] 지급표의 '" + SalvageNames[n]
                        + "'을(를) 레지스트리에서 찾지 못해 수색 물자에서 뺐다.");
                    continue;
                }

                for (int c = 0; c < SalvageCounts[n]; c++)
                    list.Add(found);
            }
            return list;
        }
    }
}
