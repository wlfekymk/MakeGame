using UnityEngine;
using MakeGame.Data;

namespace MakeGame.Systems
{
    /// <summary>
    /// 섬이 생성될 때 배 도면 습득 지점(BoatBlueprintPickup)을 배치하는 스포너.
    /// Stranded Deep 기준: 배 도면은 대형(대)/특대 섬에서만 발견되며, 매번 확정적으로 있는 것은 아니다.
    /// IslandResourceSpawner/HazardSpawner와 동일하게 WorldMapManager.SpawnIslandContent에서 함께 호출된다.
    /// </summary>
    public class BoatBlueprintSpawner : MonoBehaviour
    {
        [Tooltip("도면을 지급할 대상 배 제작 시스템 (같은 Managers 오브젝트의 BoatConstructionSystem을 연결)")]
        public BoatConstructionSystem boatConstruction;

        [Tooltip("대형 섬에 도면 습득 지점이 생성될 확률 (0~1). 1~2단계 도면은 대형 섬에서만 나온다.")]
        [Range(0f, 1f)]
        public float largeIslandSpawnChance = 0.5f;

        [Tooltip("특대 섬에 도면 습득 지점이 생성될 확률 (0~1). 최종(3단계) 도면은 특대 섬에서만 나온다.")]
        [Range(0f, 1f)]
        public float extraLargeIslandSpawnChance = 0.7f;

        [Tooltip("도면 습득 지점을 섬 중심으로부터 흩뿌릴 반경")]
        public float placementOffset = 4f;

        /// <summary>
        /// 지정한 섬이 대형/특대 섬이면 확률적으로 배 도면 습득 지점을 하나 생성한다.
        /// 소형/중형 섬에는 절대 생성하지 않는다 (Stranded Deep 기준: 배 도면은 큰 섬에서만 발견됨).
        /// </summary>
        public BoatBlueprintPickup SpawnBlueprintForIsland(IslandInstance island, Transform parent)
        {
            if (island == null)
                return null;

            if (island.size != IslandSize.Large && island.size != IslandSize.ExtraLarge)
                return null;

            float chance = island.size == IslandSize.Large ? largeIslandSpawnChance : extraLargeIslandSpawnChance;
            if (Random.value > chance)
                return null;

            Vector2 offset = Random.insideUnitCircle * placementOffset;
            Vector3 position = island.mapPosition + new Vector3(offset.x, 0f, offset.y);
            position = TerrainSampler.SnapToGround(position);

            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.transform.SetParent(parent);
            go.transform.localScale = new Vector3(0.8f, 0.8f, 0.8f);
            go.transform.position = position + Vector3.up * 0.4f; // 구체 피벗이 중심이므로 절반 높이만큼 띄워 지형 위에 놓이게 한다
            go.name = $"BoatBlueprint_{island.islandId}_{island.size}";

            var pickup = go.AddComponent<BoatBlueprintPickup>();
            pickup.islandSize = island.size;
            pickup.boatConstruction = boatConstruction;
            return pickup;
        }
    }
}
