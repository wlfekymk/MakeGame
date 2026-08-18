using UnityEngine;

namespace MakeGame.Systems
{
    /// <summary>
    /// 모닥불의 런타임 시각 교체. 프리팹(campfirePrefab)의 프리미티브 시각을 실물 OBJ
    /// (Models/campfire_a, `o` 오브젝트 3개: stone/wood/char)로 바꾼다. 프리팹/씬은 건드릴 수
    /// 없으므로 Campfire.Awake가 AddComponent로 붙이는 시각 전용 컴포넌트다 - 게임플레이
    /// (연료/조리/상호작용 콜라이더)에는 아무 영향이 없다.
    ///
    /// 로드/빌드 패턴은 AirlinerWreck과 동일하다: Load&lt;GameObject&gt; +
    /// GetComponentsInChildren&lt;MeshFilter&gt; 경로만 쓰고(LoadAll&lt;Mesh&gt;는 이 프로젝트의
    /// 모델 에셋에서 빈 배열을 준 실사고가 있다), Unity 6.5 임포터가 `o` 3개를 서브메시 3개짜리
    /// 메시 한 장으로 합쳐 오는 경우(현재 실제 동작)와 이름별 개별 메시 둘 다 지원한다.
    /// 정점이 전부 접지 로컬 미터 좌표로 구워져 있어 파츠는 위치 0 · 회전 identity · 스케일 1이다.
    /// </summary>
    public class CampfireVisual : MonoBehaviour
    {
        /// <summary>OBJ의 `o` 오브젝트 이름 매칭 키. 순서는 서브메시/머티리얼 표와 일대일이다.</summary>
        private static readonly string[] PartMeshNames =
        {
            "campfire_stone", // 돌 둘레
            "campfire_wood",  // 장작
            "campfire_char",  // 가운데 숯/재
        };

        /// <summary>공유 메시 캐시. 도메인 리로드로 비워져도 아래 프로브가 다시 채운다(래치 없음).</summary>
        private static readonly Mesh[] partMeshes = new Mesh[3];

        /// <summary>병합 임포트(메시 1장 + 서브메시 3개 - 현재 Unity 6.5의 실제 동작) 경로의 캐시.</summary>
        private static Mesh mergedMesh;

        /// <summary>프레임당 1회 프로브 가드(AirlinerWreck.probeFrame과 같은 규칙). -1 = 아직 프로브 안 함.</summary>
        private static int probeFrame = -1;

        /// <summary>
        /// 도메인 리로드를 끈 플레이 모드에서 static 캐시가 이전 실행의 파괴된 메시를 들고
        /// 시작하지 않게 초기 상태로 되돌린다.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStaticCache()
        {
            System.Array.Clear(partMeshes, 0, partMeshes.Length);
            mergedMesh = null;
            probeFrame = -1;
        }

        /// <summary>이 인스턴스가 시각 교체를 이미 끝냈는지(1회 빌드 가드). 시각 전용이라 세이브와 무관하다.</summary>
        private bool built = false;

        // Resources.Load 계열은 필드 초기화식/생성자에서 부르면 안 된다(생성자 시점이라 null이 온다).
        // 로드는 전부 Start/Update에서만 시도한다.
        private void Start()
        {
            TryBuild();
        }

        /// <summary>
        /// 모델이 아직 로드되지 않았으면 로드될 때까지 매 프레임 재시도한다. 실패를 latch하면
        /// (에셋 임포트가 한 프레임 늦는 에디터 상황 등에서) 모닥불이 영영 프리미티브로 남는다.
        /// 빌드가 끝나면 컴포넌트를 꺼서 Update 비용을 없앤다.
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

            // 프리팹의 기존 프리미티브 시각을 숨긴다. Destroy가 아니라 disable인 이유: 프리팹/세이브가
            // 이 렌더러들을 참조하고 있을 수 있고, 시각만 끄면 어떤 참조도 깨지지 않는다.
            // 새 모델 파츠는 아직 만들지 않았으므로 여기서 걸릴 수 없고, 불꽃/연기(CampfireEffect 소유)는
            // ParticleSystemRenderer라 MeshRenderer 순회에 아예 잡히지 않는다. 콜라이더는 건드리지
            // 않으므로 프리팹의 상호작용 콜라이더는 그대로 유지된다.
            var renderers = GetComponentsInChildren<MeshRenderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] != null)
                    renderers[i].enabled = false;
            }

            var root = new GameObject("CampfireModelVisual");
            root.transform.SetParent(transform, false);
            root.transform.localPosition = Vector3.zero;
            root.transform.localRotation = Quaternion.identity;
            root.transform.localScale = Vector3.one; // 정점에 좌표가 구워져 있다 - 스케일을 다시 주지 않는다

            BuildVisualParts(root.transform);
        }

        /// <summary>
        /// OBJ에서 `o` 오브젝트 3개의 공유 메시를 꺼낸다(AirlinerWreck.TryLoadMeshes와 같은 패턴).
        /// 프로브는 프레임당 1회만 하고, 실패를 영구 캐시하지 않으므로 도메인 리로드 후에도 자연 복구된다.
        /// </summary>
        private static bool TryLoadMeshes()
        {
            bool anyMissing = ResourceVisualLibrary.AnyPartMissing(partMeshes);

            if (anyMissing && probeFrame != Time.frameCount)
            {
                probeFrame = Time.frameCount;

                ResourceVisualLibrary.TryLoadMultiPartModel("Models/campfire_a",
                    PartMeshNames, partMeshes, ref mergedMesh);
            }

            // 병합 메시가 있거나 3장이 전부 모여야 빌드한다 - 반쪽짜리를 만들었다 지우는 것보다
            // 한 프레임 더 기다리는 쪽이 싸다(AirlinerWreck과 같은 규칙, 판정은 공용 헬퍼).
            return ResourceVisualLibrary.IsMultiPartModelComplete(mergedMesh, partMeshes);
        }

        private static void BuildVisualParts(Transform root)
        {
            Color stone = new Color(0.46f, 0.45f, 0.43f); // 돌 둘레
            Color wood = new Color(0.38f, 0.26f, 0.16f);  // 장작
            Color charred = new Color(0.07f, 0.06f, 0.06f); // 숯/재

            // 머티리얼은 전부 월드 공유 캐시에서 받는다(파츠마다 새로 만들지 않는다).
            // 텍스처 kind는 Resources/Textures에 실재하는 파일로 확인했다(rock.png/bark.png).
            Material[] materials =
            {
                ResourceVisualLibrary.GetMaterial(stone, "rock"),
                ResourceVisualLibrary.GetMaterial(wood, "bark"),
                ResourceVisualLibrary.GetMaterial(charred, "noise"),
            };

            // 병합 임포트 경로면 렌더러 하나 + 머티리얼 배열, 아니면 파트별 렌더러 하나씩.
            // 서브메시 순서는 OBJ의 `o` 순서(stone, wood, char)를 따른다.
            ResourceVisualLibrary.BuildMultiPartVisual(root, "campfire_body", mergedMesh,
                PartMeshNames, partMeshes, materials);
        }
    }
}
