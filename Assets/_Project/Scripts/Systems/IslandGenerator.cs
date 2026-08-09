using UnityEngine;
using MakeGame.Data;

namespace MakeGame.Systems
{
    /// <summary>
    /// 섬을 랜덤하게 생성(규모 결정)하는 시스템.
    /// 게임 시작 시 섬은 랜덤 생성되며, 초반 구간에는 소형 섬이 더 자주 등장하도록 가중치를 보정한다.
    /// </summary>
    public class IslandGenerator : MonoBehaviour
    {
        [Tooltip("섬 규모별 등장 확률 설정 데이터")]
        public IslandSpawnConfig spawnConfig;

        [Tooltip("지금까지 생성한 섬의 개수 (초반 보정 판단에 사용)")]
        private int generatedIslandCount = 0;

        /// <summary>
        /// 다음 섬의 규모를 랜덤하게 결정하여 반환한다.
        /// 초반 구간(earlyGameIslandCount 이내)에는 소형 섬 확률에 보너스 배수를 적용해
        /// 시작 지점 근처에는 소형 섬이 더 잘 나오도록 만든다.
        /// </summary>
        public IslandSize GenerateNextIslandSize()
        {
            bool isEarlyGame = generatedIslandCount < spawnConfig.earlyGameIslandCount;
            generatedIslandCount++;

            float smallWeight = spawnConfig.GetBaseSpawnRate(IslandSize.Small);
            if (isEarlyGame)
                smallWeight *= spawnConfig.earlyGameSmallIslandBonusMultiplier;

            float mediumWeight = spawnConfig.GetBaseSpawnRate(IslandSize.Medium);
            float largeWeight = spawnConfig.GetBaseSpawnRate(IslandSize.Large);
            float extraLargeWeight = spawnConfig.GetBaseSpawnRate(IslandSize.ExtraLarge);

            float totalWeight = smallWeight + mediumWeight + largeWeight + extraLargeWeight;
            float roll = Random.Range(0f, totalWeight);

            if (roll < smallWeight)
                return IslandSize.Small;
            roll -= smallWeight;

            if (roll < mediumWeight)
                return IslandSize.Medium;
            roll -= mediumWeight;

            if (roll < largeWeight)
                return IslandSize.Large;

            return IslandSize.ExtraLarge;
        }

        /// <summary>
        /// 생성 카운트를 초기화한다. 새 게임을 시작할 때 호출한다.
        /// </summary>
        public void ResetGenerationState()
        {
            generatedIslandCount = 0;
        }
    }
}
