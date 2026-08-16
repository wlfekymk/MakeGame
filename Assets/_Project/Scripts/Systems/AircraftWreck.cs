using UnityEngine;
using MakeGame.Player;

namespace MakeGame.Systems
{
    /// <summary>
    /// 시작 섬에 놓인 불시착한 경비행기 잔해. 상호작용 시 인벤토리에 있는 필요 재료를
    /// 자동으로 최대한 투입하고, 조건이 충족되면 수리를 완료시킨다 (BoatWorkbench와 동일한 사용 패턴).
    /// </summary>
    public class AircraftWreck : MonoBehaviour
    {
        [Tooltip("이 잔해가 진행 상태를 갱신할 경비행기 수리 시스템")]
        public AircraftRepairSystem repairSystem;

        /// <summary>실루엣 파츠를 이미 만들었는지(중복 생성 방지). 시각 전용이라 세이브와 무관하다.</summary>
        private bool visualBuilt = false;

        /// <summary>
        /// WorldMapManager.SpawnAircraftWreck이 임시로 붙여 둔 플레이스홀더 파츠 이름.
        /// 이 잔해의 형태를 여기서 다시 만들므로 그 4개는 겹치지 않게 걷어낸다.
        /// (WorldMapManager는 이 배치의 편집 범위 밖이라 그쪽에서 지울 수 없다 - ResourceNode가
        ///  스포너가 만든 파츠 위에 실루엣을 얹는 것과 같은 구조다.)
        /// </summary>
        private static readonly string[] PlaceholderPartNames = { "Fuselage", "WingLeft", "WingRight", "TailFin" };

        /// <summary>
        /// [B29] 잔해의 실제 형태를 만든다. 게임플레이 값은 하나도 건드리지 않는다 - 여기서 만드는 것은
        /// 전부 콜라이더가 없는 시각 파츠이고, 상호작용 판정은 WorldMapManager가 붙인 루트의
        /// BoxCollider(center 0,0.6,0 / size 4.5,1.6,2) 하나뿐이라 그대로다.
        ///
        /// 예전 형태는 눕힌 원기둥 1 + 납작한 큐브 3이었다. 게임의 시작점이자 "왜 여기 있는가"를
        /// 설명하는 유일한 오브젝트인데 20m 밖에서는 회색 막대 뭉치였다. 잔해로 읽히게 하는 신호를
        /// 형태로 넣는다(색이 아니라 형태 - ArtDirection 2장):
        ///   (1) 부러져 떨어져 나간 꼬리. 동체와 꼬리 붐이 **어긋나 있다**는 것 하나가 "추락"을 말한다.
        ///   (2) 코를 박은 자세. 동체 중심선이 앞으로 갈수록 내려가고 기수는 모래에 묻혀 있다.
        ///   (3) 꺾인 날개. 왼쪽은 중간에서 위로 접혔고 오른쪽은 짧게 부러져 땅에 박혔다.
        ///   (4) 열린 문 + 어두운 문간. 사람이 나온 구멍이라 "내가 저기서 나왔다"가 읽힌다.
        ///   (5) 그을음과 가느다란 연기.
        ///
        /// 파츠 예산: 10개(잔해는 월드에 단 하나뿐이라 자원 노드 예산 규칙과 무관하다). 동체·꼬리·
        /// 날개·프로펠러처럼 여러 덩어리로 보이는 것들은 전부 **메시 한 장에 구워** 파츠를 늘리지 않았다
        /// (B28 자원 노드에서 확립한 원칙). 머티리얼은 5개이고 전부 월드 공유 캐시에서 받는다.
        /// </summary>
        private void Start()
        {
            BuildWreckVisual();
        }

        private void BuildWreckVisual()
        {
            if (visualBuilt)
                return;
            visualBuilt = true;

            // 플레이스홀더 정리. Destroy는 프레임 끝까지 지연되므로 즉시 SetActive(false)를 먼저 부른다
            // (AGENT_BRIEF 4장 - 지연 파괴 사이에 물리/렌더가 한 프레임 남는 문제).
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                Transform child = transform.GetChild(i);
                for (int p = 0; p < PlaceholderPartNames.Length; p++)
                {
                    if (child.name != PlaceholderPartNames[p])
                        continue;
                    child.gameObject.SetActive(false);
                    Destroy(child.gameObject);
                    break;
                }
            }

            var root = new GameObject("WreckVisual");
            root.transform.SetParent(transform, false);
            root.transform.localPosition = Vector3.zero;
            root.transform.localRotation = Quaternion.identity;
            root.transform.localScale = Vector3.one; // 균등 스케일 = 자식이 회전해도 전단이 없다

            Color hull = ResourceVisualLibrary.Shade(StructureVisualBuilder.SalvageMetal, 1.08f);
            Color hullDark = ResourceVisualLibrary.Shade(StructureVisualBuilder.SalvageMetal, 0.80f);
            Color stripe = ResourceVisualLibrary.Shade(StructureVisualBuilder.DangerRed, 0.75f);
            Color glass = ResourceVisualLibrary.Shade(StructureVisualBuilder.SalvageMetal, 0.32f);
            Color soot = ResourceVisualLibrary.Shade(StructureVisualBuilder.SalvageMetal, 0.20f);

            Material hullMaterial = ResourceVisualLibrary.GetMaterial(hull, "metal");
            Material hullDarkMaterial = ResourceVisualLibrary.GetMaterial(hullDark, "metal");
            Material stripeMaterial = ResourceVisualLibrary.GetMaterial(stripe, "metal");
            Material glassMaterial = ResourceVisualLibrary.GetMaterial(glass, "noise");
            Material sootMaterial = ResourceVisualLibrary.GetMaterial(soot, "noise");

            // 메시는 전부 **잔해 로컬 미터 좌표**로 구워져 있다. 그래서 아래 파츠는 예외 없이
            // 위치 0 · 회전 identity · 스케일 1로 붙는다 - 회전/스케일을 호출부에서 다시 주면
            // "메시를 바꿨는데 호출부 스케일이 그대로여서 찌그러진" 과거 사고를 반복하게 된다.
            AddPart(root.transform, "Hull", BuildHullMesh(), hullMaterial);
            AddPart(root.transform, "Wings", BuildWingMesh(), hullDarkMaterial);
            AddPart(root.transform, "Engine", BuildEngineMesh(), hullDarkMaterial);
            AddPart(root.transform, "DoorFrame", BuildDoorwayMesh(), glassMaterial);
            AddPart(root.transform, "Door", BuildDoorPanelMesh(), hullMaterial);
            AddPart(root.transform, "Canopy", BuildCanopyMesh(), glassMaterial);
            AddPart(root.transform, "TailStripe", BuildTailStripeMesh(), stripeMaterial);
            AddPart(root.transform, "Scorch", BuildScorchMesh(), sootMaterial);
            AddPart(root.transform, "Debris", BuildDebrisMesh(), hullDarkMaterial);

            // 타다 남은 엔진에서 올라오는 가느다란 연기. 모닥불 연기(안전 신호)와 헷갈리지 않도록
            // 훨씬 옅고 느리며 양이 적다 - EffectBuilder.CreateWreckSmoke 주석 참고.
            var smoke = EffectBuilder.CreateWreckSmoke(root.transform, new Vector3(1.75f, 0.62f, 0.08f));
            if (smoke != null)
                smoke.Play();
        }

        /// <summary>메시 하나를 콜라이더 없는 시각 파츠로 붙인다(위치·회전·스케일은 전부 메시에 구워져 있다).</summary>
        private static void AddPart(Transform parent, string name, Mesh mesh, Material material)
        {
            StructureVisualBuilder.CreateMeshPart(parent, name, mesh,
                Vector3.zero, Vector3.one, Quaternion.identity, material);
        }

        /// <summary>
        /// 동체 + 떨어져 나간 꼬리 붐 + 수직 꼬리날개 + 수평 안정판을 한 메시에 굽는다.
        /// 동체 중심선이 앞으로 갈수록 내려가(0.72m → 0.26m) 배와 기수가 모래에 파묻힌 자세가 되고,
        /// 꼬리 붐은 동체 축에서 어긋난 방향으로 누워 "부러져 떨어졌다"가 실루엣만으로 읽힌다.
        /// </summary>
        private static Mesh BuildHullMesh()
        {
            var builder = new WorldMeshBuilder();

            // 동체: 뒤쪽 파단면(-1.30)에서 기수(1.95)까지. 굵기 변화가 곧 형태다(파츠로 나누지 않는다).
            var body = new[]
            {
                new Vector3(-1.30f, 0.72f, 0.02f),
                new Vector3(-0.60f, 0.66f, 0.01f),
                new Vector3(0.20f, 0.56f, 0f),
                new Vector3(0.95f, 0.44f, 0.02f),
                new Vector3(1.55f, 0.32f, 0.04f),
                new Vector3(1.95f, 0.26f, 0.05f),
            };
            var bodyRadii = new[] { 0.34f, 0.48f, 0.50f, 0.45f, 0.34f, 0.22f };
            builder.AddTube(body, bodyRadii, 8, true, true, 2f);

            // 꼬리 붐: 동체와 0.35m 떨어지고 축도 어긋나 있다. 이 어긋남 하나가 "부러졌다"의 전부다.
            var boom = new[]
            {
                new Vector3(-1.68f, 0.82f, 0.16f),
                new Vector3(-2.25f, 0.70f, 0.30f),
                new Vector3(-2.86f, 0.54f, 0.46f),
            };
            var boomRadii = new[] { 0.30f, 0.22f, 0.15f };
            builder.AddTube(boom, boomRadii, 7, true, true, 1.5f);

            // 수직 꼬리날개(붐 끝에서 위로, 뒤로 눕힘) + 수평 안정판.
            builder.AddBox(new Vector3(-2.70f, 1.08f, 0.42f), new Vector3(0.66f, 0.92f, 0.08f),
                Quaternion.Euler(0f, 12f, 16f));
            builder.AddBox(new Vector3(-2.62f, 0.62f, 0.42f), new Vector3(0.44f, 0.07f, 1.30f),
                Quaternion.Euler(0f, 12f, 6f));

            return builder.Finish("Wreck_Hull");
        }

        /// <summary>
        /// 날개 2장. 왼쪽은 중간에서 위로 접혔고(마디 2개), 오른쪽은 짧게 부러져 앞쪽 아래로 박혔다.
        /// 좌우를 대칭으로 두면 "주기장에 세워둔 비행기"가 되므로 반드시 비대칭이어야 한다.
        /// </summary>
        private static Mesh BuildWingMesh()
        {
            var builder = new WorldMeshBuilder();

            // 왼쪽(+Z): 안쪽 마디는 거의 수평, 바깥 마디는 26도 접혀 올라간다.
            builder.AddBox(new Vector3(0.30f, 0.46f, 1.00f), new Vector3(1.30f, 0.13f, 1.30f),
                Quaternion.Euler(-4f, 0f, 0f));
            builder.AddBox(new Vector3(0.22f, 0.70f, 2.05f), new Vector3(1.05f, 0.11f, 1.10f),
                Quaternion.Euler(-26f, 3f, 0f));

            // 오른쪽(-Z): 파단면이 짧고, 끝이 모래에 박히도록 아래로 기울었다.
            builder.AddBox(new Vector3(0.34f, 0.36f, -0.86f), new Vector3(1.24f, 0.13f, 1.05f),
                Quaternion.Euler(18f, 0f, 0f));
            builder.AddBox(new Vector3(0.44f, 0.08f, -1.55f), new Vector3(0.82f, 0.10f, 0.66f),
                Quaternion.Euler(30f, -6f, 0f));

            return builder.Finish("Wreck_Wings");
        }

        /// <summary>기수의 엔진 카울 + 휘어진 프로펠러 날 2장(추락한 프로펠러는 곧지 않다).</summary>
        private static Mesh BuildEngineMesh()
        {
            var builder = new WorldMeshBuilder();

            var cowl = new[]
            {
                new Vector3(1.90f, 0.27f, 0.05f),
                new Vector3(2.14f, 0.24f, 0.05f),
                new Vector3(2.22f, 0.23f, 0.05f),
            };
            builder.AddTube(cowl, new[] { 0.24f, 0.22f, 0.10f }, 8, true, true, 1f);

            // 날 2장. 한 장은 위로 휘고 한 장은 뒤로 접혔다.
            builder.AddBox(new Vector3(2.24f, 0.62f, 0.02f), new Vector3(0.07f, 0.80f, 0.16f),
                Quaternion.Euler(0f, 0f, 14f));
            builder.AddBox(new Vector3(2.20f, 0.10f, -0.18f), new Vector3(0.07f, 0.62f, 0.15f),
                Quaternion.Euler(24f, 0f, -38f));

            return builder.Finish("Wreck_Engine");
        }

        /// <summary>열린 문 뒤의 어두운 문간(동체 표면보다 살짝 안쪽에 넣어 z-파이팅을 피한다).</summary>
        private static Mesh BuildDoorwayMesh()
        {
            var builder = new WorldMeshBuilder();
            builder.AddBox(new Vector3(-0.30f, 0.64f, 0.44f), new Vector3(0.78f, 0.80f, 0.22f),
                Quaternion.Euler(0f, 0f, -3f));
            return builder.Finish("Wreck_Doorway");
        }

        /// <summary>바깥으로 열려 젖혀진 문짝. 동체 옆으로 튀어나온 이 한 장이 "열려 있다"를 만든다.</summary>
        private static Mesh BuildDoorPanelMesh()
        {
            var builder = new WorldMeshBuilder();
            builder.AddBox(new Vector3(-0.03f, 0.66f, 0.72f), new Vector3(0.76f, 0.78f, 0.07f),
                Quaternion.Euler(0f, -58f, -5f));
            return builder.Finish("Wreck_Door");
        }

        /// <summary>조종석 창(어두운 유리). 앞으로 기운 쐐기라 기수 쪽 실루엣이 밋밋하지 않다.</summary>
        private static Mesh BuildCanopyMesh()
        {
            var builder = new WorldMeshBuilder();
            builder.AddBox(new Vector3(1.20f, 0.62f, 0.03f), new Vector3(0.55f, 0.26f, 0.90f),
                Quaternion.Euler(0f, 0f, -14f));
            return builder.Finish("Wreck_Canopy");
        }

        /// <summary>꼬리날개의 도색 띠. 잔해에서 유일한 채색면이라 멀리서 눈이 여기로 간다.</summary>
        private static Mesh BuildTailStripeMesh()
        {
            var builder = new WorldMeshBuilder();
            builder.AddBox(new Vector3(-2.70f, 1.26f, 0.42f), new Vector3(0.60f, 0.24f, 0.11f),
                Quaternion.Euler(0f, 12f, 16f));
            return builder.Finish("Wreck_TailStripe");
        }

        /// <summary>
        /// 그을음. 엔진에서 시작해 동체 위쪽으로 번진 자국 3개를 납작한 덩어리로 굽는다.
        ///
        /// **위치는 동체 표면에 정확히 맞춰야 한다.** 처음 잡은 값은 세 덩어리가 전부 동체 반지름
        /// 안쪽이라 정점의 92%가 파묻혀 화면에 거의 보이지 않았다(수치로 확인했다). 지금은 각 x에서의
        /// 동체 축 높이 + 반지름(= 윗면)에서 두께의 3분의 1만 안으로 밀어 넣은 값이라, 얇은 뚜껑만
        /// 표면 위로 드러나 "얼룩"으로 읽힌다. 동체 중심선을 옮기면 이 세 값도 같이 옮겨야 한다.
        /// </summary>
        private static Mesh BuildScorchMesh()
        {
            var builder = new WorldMeshBuilder();
            builder.AddChunk(new Vector3(1.58f, 0.62f, 0.04f), new Vector3(0.66f, 0.17f, 0.56f), 91, 0.5f, 0);
            builder.AddChunk(new Vector3(0.86f, 0.87f, 0.05f), new Vector3(0.78f, 0.17f, 0.50f), 137, 0.5f, 0);
            builder.AddChunk(new Vector3(0.18f, 1.02f, -0.04f), new Vector3(0.56f, 0.15f, 0.42f), 205, 0.5f, 0);
            return builder.Finish("Wreck_Scorch");
        }

        /// <summary>추락하며 흩어진 금속 파편 3개. 잔해 주위 지면에 반쯤 박혀 있어 충돌 반경을 넓게 읽히게 한다.</summary>
        private static Mesh BuildDebrisMesh()
        {
            var builder = new WorldMeshBuilder();
            builder.AddChunk(new Vector3(2.85f, 0.06f, 1.05f), new Vector3(0.70f, 0.16f, 0.52f), 311, 0.55f, 0);
            builder.AddChunk(new Vector3(-1.10f, 0.05f, -1.70f), new Vector3(0.54f, 0.14f, 0.62f), 373, 0.55f, 0);
            builder.AddChunk(new Vector3(0.95f, 0.07f, 2.30f), new Vector3(0.46f, 0.18f, 0.40f), 419, 0.55f, 0);
            return builder.Finish("Wreck_Debris");
        }

        /// <summary>
        /// 인벤토리에서 아직 부족한 재료를 확인해, 가진 만큼 최대한 자동으로 투입한다.
        /// </summary>
        public void ContributeAvailableMaterials(PlayerInventory inventory)
        {
            if (repairSystem == null || inventory == null)
                return;

            foreach (var requirement in repairSystem.requiredMaterials)
            {
                int alreadyCollected = repairSystem.GetCollectedQuantity(requirement.item);
                int stillNeeded = requirement.quantity - alreadyCollected;
                if (stillNeeded <= 0)
                    continue;

                int available = inventory.GetItemCount(requirement.item);
                int toContribute = Mathf.Min(stillNeeded, available);
                if (toContribute > 0)
                    repairSystem.ContributeMaterial(inventory, requirement.item, toContribute);
            }
        }

        /// <summary>
        /// 재료를 최대한 투입한 뒤, 조건이 충족되면 수리 완료를 시도한다.
        /// 완료되면 축하 효과음을 재생하고 true를 반환한다.
        /// </summary>
        public bool TryRepair(PlayerInventory inventory)
        {
            ContributeAvailableMaterials(inventory);

            if (repairSystem == null)
                return false;

            bool completed = repairSystem.TryCompleteRepair();
            if (completed)
                AudioManager.Instance?.PlayStageComplete(); // 수리 완료 축하 효과음 (배 단계 완료와 동일한 효과음 재사용)

            return completed;
        }
    }
}
