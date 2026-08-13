using System.Collections.Generic;
using UnityEngine;
using MakeGame.Data;

namespace MakeGame.Systems
{
    /// <summary>
    /// 월드 맵(섬들의 배치)을 관리한다.
    /// 0번 섬은 항상 플레이어가 불시착하는 시작 섬이며, 이후 섬들은 IslandGenerator로 규모를 정하고
    /// 이 매니저가 맵 상의 위치를 정해 계속 생성해 나간다.
    /// 아직 실제 섬 지형/에셋이 없으므로, 규모에 맞는 크기의 원기둥 플레이스홀더로 시각화한다.
    /// </summary>
    public class WorldMapManager : MonoBehaviour
    {
        [Tooltip("섬 규모를 결정할 때 사용하는 생성기 (같은 Managers 오브젝트의 IslandGenerator를 연결)")]
        public IslandGenerator islandGenerator;

        [Tooltip("지금까지 생성된 모든 섬 목록 (0번이 시작 섬)")]
        public List<IslandInstance> islands = new List<IslandInstance>();

        [Header("배치 설정")]
        [Tooltip("섬 하나가 추가될 때마다 시작 섬으로부터 멀어지는 기본 거리")]
        public float baseDistanceStep = 120f;

        [Tooltip("배치 거리에 더해지는 무작위 편차 범위")]
        public float distanceJitter = 40f;

        [Tooltip("섬끼리 서로 겹치지 않도록 유지할 최소 간격")]
        public float minSpacingBetweenIslands = 60f;

        [Tooltip("겹치지 않는 위치를 찾기 위한 최대 시도 횟수")]
        public int maxPlacementAttempts = 20;

        [Header("시각화")]
        [Tooltip("섬을 표시할 플레이스홀더 프리팹. 비워두면 원기둥 프리미티브를 자동 생성한다.")]
        public GameObject islandPlaceholderPrefab;

        [Header("섬 콘텐츠 배치")]
        [Tooltip("섬이 생성될 때 채집 자원 노드를 함께 배치할 스포너 (비워두면 자원을 배치하지 않는다)")]
        public IslandResourceSpawner resourceSpawner;

        [Tooltip("섬이 생성될 때 위험 요소를 함께 배치할 스포너 (비워두면 위험 요소를 배치하지 않는다)")]
        public HazardSpawner hazardSpawner;

        [Tooltip("섬이 생성될 때 배 도면 습득 지점을 함께 배치할 스포너 (비워두면 도면을 배치하지 않는다)")]
        public BoatBlueprintSpawner blueprintSpawner;

        [Tooltip("섬이 생성될 때 사냥감/물고기를 함께 배치할 스포너 (비워두면 사냥감을 배치하지 않는다).\n" +
                 "이게 없으면 생고기/생선 자체를 얻을 방법이 없어 사냥/낚시/조리 시스템이 전부 죽은 콘텐츠가 된다.")]
        public CreatureSpawner creatureSpawner;

        [Header("경비행기 수리 엔딩")]
        [Tooltip("시작 섬에 배치할 경비행기 잔해가 진행 상태를 갱신할 수리 시스템 (비워두면 잔해를 배치하지 않는다)")]
        public AircraftRepairSystem aircraftRepair;

        [Header("배 제작 엔딩")]
        [Tooltip("시작 섬에 배치할 배 작업대가 진행 상태를 갱신할 배 제작 시스템 (비워두면 작업대를 배치하지 않는다).\n" +
                 "작업대가 없으면 도면과 재료를 다 모아도 실제로 배를 조립할 방법이 없으므로 반드시 연결해야 한다.")]
        public BoatConstructionSystem boatConstruction;

        [Header("테스트용 자동 생성")]
        [Tooltip("플레이 시작 시 자동으로 시작 섬 + 여러 섬을 생성해서 맵을 미리 확인할 수 있게 한다.")]
        public bool generateOnStart = true;

        [Tooltip("자동 생성 시 시작 섬 외에 추가로 생성할 섬 개수")]
        public int initialIslandCount = 8;

        [Header("월드 시드")]
        [Tooltip("섬 배치/자원/위험요소 생성에 사용하는 난수 시드. 저장/불러오기 시 이 값을 기록해두면\n" +
                 "같은 섬 배치를 다시 만들어낼 수 있다. 0이면 실행할 때마다 무작위 시드를 새로 뽑는다.")]
        public int worldSeed = 0;

        /// <summary>
        /// 섬 생성보다 먼저 실행되어야 하므로 Awake에서 난수 시드를 고정한다.
        /// worldSeed가 0(미지정)이면 이번 실행에 사용할 시드를 무작위로 뽑아 기록해둔다.
        /// 이후 SaveLoadController가 이 값을 저장 파일에 함께 기록해, 다음에 같은 섬 배치를 재현할 수 있게 한다.
        /// </summary>
        private void Awake()
        {
            if (worldSeed == 0)
                worldSeed = System.Environment.TickCount;

            Random.InitState(worldSeed);
        }

        /// <summary>
        /// generateOnStart가 켜져 있으면 플레이 시작과 동시에 시작 섬과 초기 섬들을 생성해
        /// 맵이 어떻게 만들어지는지 바로 확인할 수 있게 한다.
        /// </summary>
        private void Start()
        {
            CreateOcean();

            if (!generateOnStart)
                return;

            GenerateStartingIsland();
            for (int i = 0; i < initialIslandCount; i++)
            {
                GenerateNextIsland(i, initialIslandCount);
            }
        }

        /// <summary>
        /// 지정한 시드로 월드(섬/바다/자원/위험요소/사냥감/도면/작업대/잔해)를 처음부터 다시 생성한다.
        /// 저장/불러오기에서 같은 worldSeed로 같은 섬 배치를 재현하기 위해 사용한다.
        /// 예전에는 SaveLoadController.Load()가 worldSeed 필드 값만 갱신하고 실제로 월드를 다시 만들지
        /// 않아서, "월드 시드로 같은 섬 배치를 재현한다"는 설계 의도가 실제로는 전혀 동작하지 않는
        /// 죽은 기능이었다 (불러오기를 해도 이미 생성되어 있던 이전 섬 배치가 그대로 남아있었음).
        /// </summary>
        public void RegenerateWorld(int seed)
        {
            // 기존에 생성해 둔 섬/바다/자원/위험요소 등 이 매니저 아래의 모든 오브젝트를 제거한다.
            for (int i = transform.childCount - 1; i >= 0; i--)
                Destroy(transform.GetChild(i).gameObject);

            islands.Clear();

            worldSeed = seed;
            Random.InitState(worldSeed);

            CreateOcean();
            GenerateStartingIsland();
            for (int i = 0; i < initialIslandCount; i++)
            {
                GenerateNextIsland(i, initialIslandCount);
            }
        }

        [Header("바다")]
        [Tooltip("바다 평면의 한 변 크기. 섬들이 모두 이 범위 안에 들어올 만큼 충분히 커야 한다.")]
        public float oceanSize = 4000f;

        [Tooltip("해수면 높이. 섬 지형의 가장자리 높이(0)와 맞닿으며, PlayerController.waterLevel과 같아야 한다.")]
        public float seaLevel = 0f;

        /// <summary>
        /// 섬들을 모두 감싸는 커다란 바다 평면을 한 번 만든다. 플레이어의 수영/잠수 판정은 y 좌표만으로
        /// 이뤄지므로, 이 평면의 콜라이더는 제거해 시각적 표시 용도로만 쓴다.
        /// </summary>
        private void CreateOcean()
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Plane);
            go.name = "Ocean";
            go.transform.SetParent(transform);
            go.transform.position = new Vector3(0f, seaLevel, 0f);

            // 기본 Plane 메시는 10x10 크기이므로, oceanSize에 맞춰 스케일한다.
            float scale = oceanSize / 10f;
            go.transform.localScale = new Vector3(scale, 1f, scale);

            int waterLayer = LayerMask.NameToLayer("Water");
            if (waterLayer >= 0)
                go.layer = waterLayer;

            var collider = go.GetComponent<Collider>();
            if (collider != null)
                Destroy(collider);

            var renderer = go.GetComponent<MeshRenderer>();
            if (renderer != null)
                renderer.sharedMaterial = CreateOceanMaterial();
        }

        /// <summary>
        /// 바다 평면에 사용할 기본 파란색 URP Lit 머티리얼을 만든다.
        /// </summary>
        private Material CreateOceanMaterial()
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            var material = new Material(shader != null ? shader : Shader.Find("Standard"));
            material.color = new Color(0.1f, 0.35f, 0.55f);
            return material;
        }

        /// <summary>
        /// 게임 시작 시 호출한다. 0번 섬(불시착 시작 섬, 소형 고정)을 원점에 생성한다.
        /// </summary>
        public IslandInstance GenerateStartingIsland()
        {
            var startIsland = new IslandInstance
            {
                islandId = 0,
                size = IslandSize.Small,
                mapPosition = Vector3.zero,
                isDiscovered = true,
                isStartingIsland = true,
            };

            SpawnPlaceholder(startIsland);
            SpawnIslandContent(startIsland);
            SpawnAircraftWreck(startIsland);
            SpawnBoatWorkbench(startIsland);
            islands.Add(startIsland);
            return startIsland;
        }

        /// <summary>
        /// 시작 섬(캠프)에 배 작업대를 한 번 배치한다. 도면과 재료를 다 모아도 이 오브젝트와 상호작용(E키)해야
        /// 실제로 재료가 투입되고 단계가 진행되므로, 배 엔딩 경로에 반드시 필요한 배치다.
        /// 간단한 작업대(받침대+공구대) 형태를 프리미티브 조합으로 표현한다.
        /// </summary>
        private void SpawnBoatWorkbench(IslandInstance startIsland)
        {
            if (boatConstruction == null)
                return;

            Vector3 position = startIsland.mapPosition + new Vector3(-6f, 0f, -3f);
            position = TerrainSampler.SnapToGround(position);

            var go = new GameObject("BoatWorkbench");
            go.transform.SetParent(transform);
            go.transform.position = position;

            // 작업대 상판(넓적한 큐브) + 다리 2개(가는 원기둥) + 위에 놓인 공구(작은 큐브)로 작업대 형태를 표현한다.
            StructureVisualBuilder.CreateVisualPart(go.transform, "Tabletop", PrimitiveType.Cube,
                Vector3.up * 0.8f, new Vector3(2.2f, 0.15f, 1.2f), new Color(0.5f, 0.35f, 0.2f));
            StructureVisualBuilder.CreateVisualPart(go.transform, "LegLeft", PrimitiveType.Cylinder,
                new Vector3(-0.8f, 0.4f, 0f), new Vector3(0.12f, 0.4f, 0.12f), new Color(0.35f, 0.24f, 0.14f));
            StructureVisualBuilder.CreateVisualPart(go.transform, "LegRight", PrimitiveType.Cylinder,
                new Vector3(0.8f, 0.4f, 0f), new Vector3(0.12f, 0.4f, 0.12f), new Color(0.35f, 0.24f, 0.14f));
            StructureVisualBuilder.CreateVisualPart(go.transform, "Tool", PrimitiveType.Cube,
                new Vector3(0.4f, 0.95f, 0.2f), new Vector3(0.5f, 0.12f, 0.12f), new Color(0.4f, 0.4f, 0.42f),
                Quaternion.Euler(0f, 30f, 0f));

            var boxCollider = go.AddComponent<BoxCollider>();
            boxCollider.center = new Vector3(0f, 0.6f, 0f);
            boxCollider.size = new Vector3(2.4f, 1.2f, 1.4f);

            var workbench = go.AddComponent<BoatWorkbench>();
            workbench.boatConstruction = boatConstruction;
        }

        /// <summary>
        /// 시작 섬(불시착 지점)에 경비행기 잔해를 한 번 배치한다. 프리미티브를 조합해 부서진 기체 형태를 만들고,
        /// AircraftWreck 컴포넌트를 붙여 상호작용(E키)으로 수리 재료를 투입할 수 있게 한다.
        /// </summary>
        private void SpawnAircraftWreck(IslandInstance startIsland)
        {
            if (aircraftRepair == null)
                return;

            Vector3 position = startIsland.mapPosition + new Vector3(6f, 0f, -4f);
            position = TerrainSampler.SnapToGround(position);

            var go = new GameObject("AircraftWreck");
            go.transform.SetParent(transform);
            go.transform.position = position;

            // 동체(원기둥, 옆으로 눕힘) + 부러진 날개(양옆 납작한 큐브) + 꼬리날개(세로 큐브)로 부서진 기체 형태를 표현한다.
            StructureVisualBuilder.CreateVisualPart(go.transform, "Fuselage", PrimitiveType.Cylinder,
                Vector3.up * 0.6f, new Vector3(0.8f, 2.2f, 0.8f), new Color(0.55f, 0.58f, 0.6f),
                Quaternion.Euler(0f, 0f, 90f));
            StructureVisualBuilder.CreateVisualPart(go.transform, "WingLeft", PrimitiveType.Cube,
                new Vector3(-1.6f, 0.6f, 0.3f), new Vector3(2.2f, 0.15f, 0.8f), new Color(0.45f, 0.48f, 0.5f),
                Quaternion.Euler(0f, 0f, 15f));
            StructureVisualBuilder.CreateVisualPart(go.transform, "WingRight", PrimitiveType.Cube,
                new Vector3(1.8f, 0.5f, -0.4f), new Vector3(1.8f, 0.15f, 0.8f), new Color(0.45f, 0.48f, 0.5f),
                Quaternion.Euler(0f, 0f, -25f));
            StructureVisualBuilder.CreateVisualPart(go.transform, "TailFin", PrimitiveType.Cube,
                new Vector3(-2.2f, 1f, 0f), new Vector3(0.15f, 1f, 0.6f), new Color(0.6f, 0.25f, 0.2f));

            // 상호작용 레이캐스트가 맞을 수 있도록 전체를 감싸는 박스 콜라이더를 추가한다.
            var boxCollider = go.AddComponent<BoxCollider>();
            boxCollider.center = new Vector3(0f, 0.6f, 0f);
            boxCollider.size = new Vector3(4.5f, 1.6f, 2f);

            var wreck = go.AddComponent<AircraftWreck>();
            wreck.repairSystem = aircraftRepair;
        }

        /// <summary>
        /// 새 섬을 하나 생성한다. IslandGenerator로 규모를 정하고, 기존 섬과 겹치지 않는 위치를 찾아 배치한다.
        /// </summary>
        /// <param name="islandIndex">초기 생성 순서에서 이 섬이 몇 번째(0부터)인지. 대형/특대 섬 최소 보장 판단에 쓰인다.</param>
        /// <param name="totalIslandCount">이번 초기 생성에서 만들 전체 섬 개수.</param>
        public IslandInstance GenerateNextIsland(int islandIndex = 0, int totalIslandCount = 1)
        {
            IslandSize size = islandGenerator != null
                ? islandGenerator.GenerateNextIslandSize(islandIndex, totalIslandCount)
                : IslandSize.Small;

            var newIsland = new IslandInstance
            {
                islandId = islands.Count,
                size = size,
                mapPosition = FindValidPosition(),
                isDiscovered = false,
                isStartingIsland = false,
            };

            SpawnPlaceholder(newIsland);
            SpawnIslandContent(newIsland);
            islands.Add(newIsland);
            return newIsland;
        }

        /// <summary>
        /// 섬이 생성된 직후, 연결된 스포너가 있다면 채집 자원, 위험 요소, 배 도면 습득 지점을 함께 배치한다.
        /// </summary>
        private void SpawnIslandContent(IslandInstance island)
        {
            resourceSpawner?.SpawnResourcesForIsland(island, transform);
            hazardSpawner?.SpawnHazardsForIsland(island, transform);
            blueprintSpawner?.SpawnBlueprintForIsland(island, transform);
            creatureSpawner?.SpawnCreaturesForIsland(island, transform);
        }

        /// <summary>
        /// 기존에 생성된 섬들과 최소 간격 이상 떨어진 새 위치를 찾는다.
        /// 시작 섬으로부터의 거리는 생성된 섬 개수에 비례해 점점 멀어진다 (섬이 늘어날수록 더 먼 바다로 확장).
        /// 정해진 횟수 안에 조건을 만족하는 위치를 못 찾으면 마지막 후보 위치를 그대로 반환한다.
        /// </summary>
        private Vector3 FindValidPosition()
        {
            Vector3 candidate = Vector3.zero;

            for (int attempt = 0; attempt < maxPlacementAttempts; attempt++)
            {
                float angle = Random.Range(0f, 360f) * Mathf.Deg2Rad;
                float distance = baseDistanceStep * islands.Count + Random.Range(-distanceJitter, distanceJitter);
                distance = Mathf.Max(distance, baseDistanceStep * 0.5f);

                candidate = new Vector3(Mathf.Cos(angle) * distance, 0f, Mathf.Sin(angle) * distance);

                if (IsFarEnoughFromAllIslands(candidate))
                    return candidate;
            }

            // 조건을 만족하는 위치를 못 찾았어도 마지막 후보를 사용한다 (완전히 막히지 않도록).
            return candidate;
        }

        /// <summary>
        /// 지정한 위치가 기존의 모든 섬과 최소 간격 이상 떨어져 있는지 확인한다.
        /// </summary>
        private bool IsFarEnoughFromAllIslands(Vector3 position)
        {
            foreach (var island in islands)
            {
                if (Vector3.Distance(position, island.mapPosition) < minSpacingBetweenIslands)
                    return false;
            }
            return true;
        }

        [Header("섬 지형 생성")]
        [Tooltip("절차적 섬 지형(언덕 메시)의 중심부 최대 높이. 걸어서 오르기 편하도록 섬 규모와 무관하게 완만한 값으로 고정한다.")]
        public float terrainMaxHeight = 2.5f;

        [Tooltip("섬 지형 메시에 사용할 머티리얼 (비워두면 기본 URP Lit 모래색 머티리얼을 사용한다)")]
        public Material terrainMaterial;

        /// <summary>
        /// 섬 규모에 맞는 크기의 지형 오브젝트를 생성해 배치한다.
        /// islandPlaceholderPrefab이 지정돼 있으면 그것을 사용하고, 없으면 걸어다닐 수 있는 절차적 언덕 메시(IslandMeshGenerator)를 만든다.
        /// </summary>
        private void SpawnPlaceholder(IslandInstance island)
        {
            GameObject placeholder;

            if (islandPlaceholderPrefab != null)
            {
                placeholder = Instantiate(islandPlaceholderPrefab, island.mapPosition, Quaternion.identity, transform);
            }
            else
            {
                float radius = GetSizeScale(island.size);
                placeholder = CreateProceduralIslandTerrain(radius, island.mapPosition);
            }

            placeholder.name = $"Island_{island.islandId}_{island.size}";
            island.placeholderObject = placeholder;
        }

        /// <summary>
        /// 지정한 반지름/위치에 절차적 섬 지형 메시를 생성한다.
        /// MeshFilter/MeshRenderer/MeshCollider를 직접 붙여 플레이어가 실제로 걸어다닐 수 있게 한다.
        /// </summary>
        private GameObject CreateProceduralIslandTerrain(float radius, Vector3 position)
        {
            var go = new GameObject("IslandTerrain");
            go.transform.SetParent(transform);
            go.transform.position = position;

            var mesh = IslandMeshGenerator.GenerateIslandMesh(radius, terrainMaxHeight);

            var meshFilter = go.AddComponent<MeshFilter>();
            meshFilter.sharedMesh = mesh;

            var meshRenderer = go.AddComponent<MeshRenderer>();
            meshRenderer.sharedMaterial = terrainMaterial != null ? terrainMaterial : CreateDefaultTerrainMaterial();

            var meshCollider = go.AddComponent<MeshCollider>();
            meshCollider.sharedMesh = mesh;

            return go;
        }

        /// <summary>
        /// 섬 지형용 머티리얼이 지정되지 않았을 때 사용할 기본 모래색 URP Lit 머티리얼을 만든다.
        /// </summary>
        private Material CreateDefaultTerrainMaterial()
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            var material = new Material(shader != null ? shader : Shader.Find("Standard"));
            material.color = new Color(0.76f, 0.7f, 0.5f);
            return material;
        }

        /// <summary>
        /// 섬 규모에 대응하는 시각적 크기 배율을 반환한다 (플레이스홀더 스케일 계산용).
        /// </summary>
        private float GetSizeScale(IslandSize size)
        {
            switch (size)
            {
                case IslandSize.Small: return 5f;
                case IslandSize.Medium: return 9f;
                case IslandSize.Large: return 14f;
                case IslandSize.ExtraLarge: return 20f;
                default: return 5f;
            }
        }

        /// <summary>
        /// 섬 번호로 섬 인스턴스를 찾는다. 없으면 null을 반환한다.
        /// </summary>
        public IslandInstance GetIsland(int islandId)
        {
            foreach (var island in islands)
            {
                if (island.islandId == islandId)
                    return island;
            }
            return null;
        }

        /// <summary>
        /// 지정한 섬을 발견 상태로 표시한다 (플레이어가 시야 확보 또는 도착했을 때 호출).
        /// </summary>
        public void DiscoverIsland(int islandId)
        {
            var island = GetIsland(islandId);
            if (island != null)
                island.isDiscovered = true;
        }
    }
}
