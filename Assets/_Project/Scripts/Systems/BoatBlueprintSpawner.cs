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

        [Tooltip("대형 섬에 도면 습득 지점이 생성될 확률 (0~1). 1~2단계 도면은 대형 섬에서만 나온다.\n" +
            "도면 습득 지점은 한 번 쓰면 사라지는 일회성이라, 이 확률이 낮으면 IslandGenerator가 최소 2개로" +
            " 보장하는 대형 섬 중 일부에 도면이 아예 없어 2단계 진행이 막힐 수 있다. 기본값을 0.9로 높여" +
            " 그 위험을 크게 낮췄다(완전히 1로 고정하지 않은 이유는 약간의 탐험 긴장감을 남기기 위함).")]
        [Range(0f, 1f)]
        public float largeIslandSpawnChance = 0.9f;

        [Tooltip("특대 섬에 도면 습득 지점이 생성될 확률 (0~1). 최종(3단계) 도면은 특대 섬에서만 나온다.\n" +
            "특대 섬은 최소 1개만 보장되므로 여기서 놓치면 배 엔딩이 완전히 막히기 때문에 대형 섬보다도" +
            " 더 높은 기본값(0.95)을 쓴다.")]
        [Range(0f, 1f)]
        public float extraLargeIslandSpawnChance = 0.95f;

        // 긴급 정정(#3 회귀 수정) 이력: 한 차례 placementOffset 필드를 제거하고 IslandSizeMetrics.GetScatterRadius로
        // 대체했었는데, 실제 배포된 SampleScene.unity에 이 컴포넌트가 배치되어 있고 placementOffset=4가
        // 직렬화되어 있다는 사실이 뒤늦게 확인되어 필드를 되살렸었다. 절대 지우지 않는다(씬 직렬화 값 보존).
        [Tooltip("(구버전 호환용) 도면 습득 지점을 섬 중심으로부터 흩뿌릴 기본 반경. largePlacementOffset/" +
            "extraLargePlacementOffset이 0 이하로 비어 있을 때만 폴백으로 쓰인다.")]
        public float placementOffset = 4f;

        // 기획 확정(B2-8): 예전에는 placementOffset(4f) 하나로 대형/특대 섬 모두를 처리해, 도면이 항상
        // 섬 중심 4m 반경(플레이어 착지 지점과 거의 겹치는 거리)에만 나와 사실상 탐색이 없었다. 섬 규모별로
        // 분리된 반경을 새로 도입해, 큰 섬일수록 더 넓게 흩어지도록 했다. 0 이하로 남아있으면(과거 저장된
        // 씬처럼 아직 이 필드를 모르는 경우) 구버전 필드 placementOffset으로 안전하게 폴백한다.
        [Tooltip("대형 섬에서 도면 습득 지점을 섬 중심으로부터 흩뿌릴 반경 (0 이하면 placementOffset으로 폴백)")]
        public float largePlacementOffset = 30f;

        [Tooltip("특대 섬에서 도면 습득 지점을 섬 중심으로부터 흩뿌릴 반경 (0 이하면 placementOffset으로 폴백)")]
        public float extraLargePlacementOffset = 45f;

        /// <summary>
        /// 지정한 섬이 대형/특대 섬이면 확률적으로 배 도면 습득 지점을 하나 생성한다.
        /// 소형/중형 섬에는 절대 생성하지 않는다 (Stranded Deep 기준: 배 도면은 큰 섬에서만 발견됨).
        /// B3-3: worldSeed를 추가로 받아, 이 섬(island.islandId) 전용 결정적 System.Random 스트림으로
        /// 등장 확률 판정과 산포 위치를 뽑는다(재현성 근거는 IslandResourceSpawner 상단 주석과 동일).
        /// </summary>
        public BoatBlueprintPickup SpawnBlueprintForIsland(IslandInstance island, Transform parent, int worldSeed)
        {
            if (island == null)
                return null;

            if (island.size != IslandSize.Large && island.size != IslandSize.ExtraLarge)
                return null;

            System.Random rng = SeededRandomExtensions.CreateForIsland(worldSeed, island.islandId);

            float chance = island.size == IslandSize.Large ? largeIslandSpawnChance : extraLargeIslandSpawnChance;
            if (rng.NextValue01() > chance)
                return null;

            // 기획 확정(B2-8): 섬 규모별 전용 반경을 우선 사용하고, 아직 설정되지 않았으면(0 이하)
            // 구버전 필드 placementOffset으로 폴백한다.
            float scatterRadius = island.size == IslandSize.Large
                ? (largePlacementOffset > 0f ? largePlacementOffset : placementOffset)
                : (extraLargePlacementOffset > 0f ? extraLargePlacementOffset : placementOffset);

            Vector2 offset = rng.NextInsideUnitCircle() * scatterRadius;
            Vector3 position = island.mapPosition + new Vector3(offset.x, 0f, offset.y);
            position = TerrainSampler.SnapToGround(position);

            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.transform.SetParent(parent);
            go.transform.localScale = new Vector3(0.8f, 0.8f, 0.8f);
            go.transform.position = position + Vector3.up * 0.4f; // 구체 피벗이 중심이므로 절반 높이만큼 띄워 지형 위에 놓이게 한다
            go.name = $"BoatBlueprint_{island.islandId}_{island.size}";

            // 버그 수정: 그동안 머티리얼을 전혀 지정하지 않아 기본 흰색/회색 구체로만 보여, 다른 월드
            // 오브젝트(자원 노드/사냥감 등)와 달리 눈에 띄지 않았다. 도면다운 금색 계열로 칠하고,
            // StructureVisualBuilder.CreateColorMaterial을 거쳐 다른 프리미티브들과 동일한 표면
            // 그레인 텍스처(noise)도 함께 적용해 일관성을 맞췄다.
            var renderer = go.GetComponent<Renderer>();
            if (renderer != null)
                renderer.material = StructureVisualBuilder.CreateColorMaterial(new Color(0.9f, 0.75f, 0.35f, 1f));

            var pickup = go.AddComponent<BoatBlueprintPickup>();
            pickup.islandSize = island.size;
            pickup.boatConstruction = boatConstruction;
            return pickup;
        }
    }
}
