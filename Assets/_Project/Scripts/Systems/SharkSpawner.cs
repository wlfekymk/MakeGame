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

        // B3-6: 기획 결정으로 2f → 0.6f로 낮춘다. 곰/식인종은 사전에 시각적으로 식별 가능하고 날씨도
        // 예고되는데, 상어만 완전히 수면 아래 은신해 있으면 이 게임의 "위험은 미리 알아챌 수 있어야
        // 한다"는 디자인 언어와 어긋난다는 지적이다. 상어는 직접피해 18 + 출혈이 동시에 들어가는 만큼,
        // 등지느러미(AddDetailParts의 Fin 파츠)가 수면 위로 살짝 드러나 회피할 기회를 줘야 한다는 것이
        // 근거. 씬(SampleScene.unity)에도 2가 직렬화돼 있어 코드 기본값만으로는 반영되지 않는다 -
        // 디렉터가 씬 값을 직접 0.6으로 맞춘다(코디네이터 보고 [디렉터 조치 요청] 항목 참고).
        [Tooltip("해수면보다 이만큼 아래에 상어를 배치한다 (B3-6: 등지느러미가 수면 위로 살짝 드러나 미리 식별할 수 있도록 낮춤)")]
        public float depthBelowSeaLevel = 0.6f;

        [Tooltip("겹치지 않는 위치를 찾기 위한 섬 하나당 최대 시도 횟수")]
        public int maxPlacementAttempts = 20;

        // B3-3: 상어는 특정 섬 하나에 속하지 않고 바다 전체에 걸쳐 배치되므로, 섬 인덱스 대신 이 전용
        // salt로 결정적 System.Random 스트림을 만든다. 실제 섬 islandId(0부터 시작)와 절대 겹치지 않도록
        // 아주 작은(음수) 값을 골랐다 - 혹시라도 같은 salt를 실수로 섬 스폰에도 쓰는 사고를 방지하기 위해
        // 식별하기 쉬운 값으로 정했다.
        private const int SharkSeedSalt = -1000000;

        /// <summary>
        /// 지정한 섬 목록을 참고해 바다 위 무작위 위치에 상어들을 배치한다. 모든 섬 생성이 끝난 뒤
        /// (WorldMapManager.Start/RegenerateWorld) 한 번 호출해야 섬 위치를 정확히 피할 수 있다.
        /// B3-3: worldSeed를 추가로 받아, 상어 배치 전용 결정적 System.Random 스트림(SharkSeedSalt)으로
        /// 위치를 뽑는다. 이 스트림은 섬 콘텐츠 생성에 쓰이는 섬별 스트림과 완전히 분리돼 있어, 섬이
        /// 몇 개든 몇 번째로 생성됐든 상어 배치 결과에 영향을 주지 않는다.
        /// </summary>
        public List<HazardSource> SpawnSharks(List<IslandInstance> islands, float oceanSize, float seaLevel, Transform parent, int worldSeed)
        {
            var spawned = new List<HazardSource>();
            if (hazardSpawner == null || islands == null)
                return spawned;

            System.Random rng = SeededRandomExtensions.CreateForSalt(worldSeed, SharkSeedSalt);
            int spawnOrder = 0;

            float halfRange = oceanSize * 0.5f * placementRangeRatio;

            for (int i = 0; i < sharkCount; i++)
            {
                Vector3? position = FindValidOceanPosition(islands, halfRange, seaLevel, rng);
                if (position.HasValue)
                {
                    // 상어는 특정 섬에 속하지 않으므로 islandIndex는 -1(섬에 속하지 않음)로 둔다.
                    spawned.Add(hazardSpawner.SpawnHazardAtPosition(HazardType.Shark, position.Value, parent, rng, -1, spawnOrder));
                    spawnOrder++;
                }
            }

            return spawned;
        }

        /// <summary>
        /// 모든 섬(및 시작 섬 안전지대)과 최소 거리 이상 떨어진 바다 위 무작위 위치를 찾는다.
        /// 정해진 시도 횟수 안에 조건을 만족하는 위치를 못 찾으면 null을 반환해 그 상어는 건너뛴다
        /// (섬이 매우 빽빽한 예외적인 경우에도 무한 루프에 빠지지 않도록).
        /// </summary>
        private Vector3? FindValidOceanPosition(List<IslandInstance> islands, float halfRange, float seaLevel, System.Random rng)
        {
            for (int attempt = 0; attempt < maxPlacementAttempts; attempt++)
            {
                Vector3 candidate = new Vector3(
                    rng.NextFloat(-halfRange, halfRange),
                    seaLevel - depthBelowSeaLevel,
                    rng.NextFloat(-halfRange, halfRange));

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
