using System.Collections.Generic;
using UnityEngine;
using MakeGame.Data;

namespace MakeGame.Systems
{
    /// <summary>
    /// 섬이 아니라 섬과 섬 사이의 깊은 바다에 상어(HazardSource, HazardType.Shark)를 배치하는 스포너.
    /// 기존 HazardSpawner는 섬 하나를 중심으로 반경 안에 흩뿌리는 방식이라 바다 한가운데 배치에는
    /// 맞지 않으므로 별도 클래스로 분리했다. 실제 위험 요소 생성(형태/색상/전투 설정)은 HazardSpawner의
    /// GetVisualConfig/ConfigureForType 테이블을 그대로 재사용해(SpawnHazardAtPosition), 상어의
    /// 시각/전투 스펙이 두 곳에 중복 정의되지 않게 했다.
    /// 예전에는 수영/잠수 시스템이 있어도 바다 자체에는 아무런 위험 요소가 없어서, 넓은 바다를
    /// 헤엄쳐 건너는 행위에 아무 긴장감이 없었다는 공백을 메우기 위해 신규로 추가한 시스템이다.
    /// </summary>
    public class SharkSpawner : MonoBehaviour
    {
        [Tooltip("실제 위험 요소 생성 로직을 재사용할 HazardSpawner (같은 Managers 오브젝트의 것을 연결)")]
        public HazardSpawner hazardSpawner;

        [Tooltip("생성할 상어 개체 수")]
        public int sharkCount = 6;

        // 퀄리티 개선: 섬 반지름이 10배로 커진 것(WorldMapManager.GetSizeScale, 최대 200)에 맞춰
        // 함께 10배로 키우지 않으면 큰 섬 지형 안에서 상어가 튀어나오는 문제가 생긴다.
        [Tooltip("모든 섬 중심으로부터 이만큼 떨어진 곳에만 상어를 배치한다 (섬 지형 안이나 바로 옆에서 튀어나오지 않도록)")]
        public float minDistanceFromIslands = 350f;

        [Tooltip("시작 섬(불시착 지점) 주변 이 반경 안에는 상어를 배치하지 않는다 (게임 시작하자마자 습격당하는 것을 방지)")]
        public float safeZoneRadiusFromStart = 600f;

        [Tooltip("바다 전체 크기(WorldMapManager.oceanSize)에서 이 비율 이내로만 배치해 맵 가장자리에 몰리지 않게 한다")]
        [Range(0.1f, 1f)]
        public float placementRangeRatio = 0.7f;

        [Tooltip("해수면보다 이만큼 아래에 상어를 배치한다 (수면 위로 등이 튀어나오지 않도록)")]
        public float depthBelowSeaLevel = 2f;

        [Tooltip("겹치지 않는 위치를 찾기 위한 섬 하나당 최대 시도 횟수")]
        public int maxPlacementAttempts = 20;

        /// <summary>
        /// 지정한 섬 목록을 참고해 바다 위 무작위 위치에 상어들을 배치한다. 모든 섬 생성이 끝난 뒤
        /// (WorldMapManager.Start/RegenerateWorld) 한 번 호출해야 섬 위치를 정확히 피할 수 있다.
        /// </summary>
        public List<HazardSource> SpawnSharks(List<IslandInstance> islands, float oceanSize, float seaLevel, Transform parent)
        {
            var spawned = new List<HazardSource>();
            if (hazardSpawner == null || islands == null)
                return spawned;

            float halfRange = oceanSize * 0.5f * placementRangeRatio;

            for (int i = 0; i < sharkCount; i++)
            {
                Vector3? position = FindValidOceanPosition(islands, halfRange, seaLevel);
                if (position.HasValue)
                    spawned.Add(hazardSpawner.SpawnHazardAtPosition(HazardType.Shark, position.Value, parent));
            }

            return spawned;
        }

        /// <summary>
        /// 모든 섬(및 시작 섬 안전지대)과 최소 거리 이상 떨어진 바다 위 무작위 위치를 찾는다.
        /// 정해진 시도 횟수 안에 조건을 만족하는 위치를 못 찾으면 null을 반환해 그 상어는 건너뛴다
        /// (섬이 매우 빽빽한 예외적인 경우에도 무한 루프에 빠지지 않도록).
        /// </summary>
        private Vector3? FindValidOceanPosition(List<IslandInstance> islands, float halfRange, float seaLevel)
        {
            for (int attempt = 0; attempt < maxPlacementAttempts; attempt++)
            {
                Vector3 candidate = new Vector3(
                    Random.Range(-halfRange, halfRange),
                    seaLevel - depthBelowSeaLevel,
                    Random.Range(-halfRange, halfRange));

                if (IsFarEnoughFromAllIslands(candidate, islands))
                    return candidate;
            }

            return null;
        }

        /// <summary>
        /// 후보 위치가 모든 섬(수평 거리 기준, y 무시)과 minDistanceFromIslands 이상, 시작 섬과는
        /// safeZoneRadiusFromStart 이상 떨어져 있는지 확인한다.
        /// </summary>
        private bool IsFarEnoughFromAllIslands(Vector3 candidate, List<IslandInstance> islands)
        {
            Vector2 candidateFlat = new Vector2(candidate.x, candidate.z);

            foreach (var island in islands)
            {
                Vector2 islandFlat = new Vector2(island.mapPosition.x, island.mapPosition.z);
                float requiredDistance = island.isStartingIsland ? safeZoneRadiusFromStart : minDistanceFromIslands;

                if (Vector2.Distance(candidateFlat, islandFlat) < requiredDistance)
                    return false;
            }

            return true;
        }
    }
}
