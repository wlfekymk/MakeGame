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

        [Header("최소 보장 섬 개수")]
        [Tooltip("초기 섬 생성 중 최소 이만큼은 대형 섬으로 보장한다. 배 도면 습득 지점(BoatBlueprintPickup)은" +
            " 한 번 사용하면 사라지는 일회성이고 1~2단계 도면 모두 대형 섬에서만 나오므로, 대형 섬이 1개뿐이면" +
            " 1단계 도면을 그 섬에서 얻는 순간 2단계 도면을 구할 방법이 영영 사라진다. 그래서 최소 2개를" +
            " 보장한다(순수 확률 기본 15%에만 맡기면 운이 나쁜 시드에서 배 엔딩이 막힐 수 있음).")]
        public int minimumLargeIslands = 2;

        [Tooltip("초기 섬 생성 중 최소 이만큼은 특대 섬으로 보장한다. 특대 섬이 없으면 배 최종(3단계) 도면을" +
            " 영영 구할 수 없으므로, 순수 확률(기본 5%)에만 맡기면 8개 섬 기준 약 66% 확률로 특대 섬이" +
            " 하나도 없는 시드가 나올 수 있다. 경비행기 엔딩은 시작 섬에 항상 있어 별도 보장이 필요 없다.")]
        public int minimumExtraLargeIslands = 1;

        /// <summary>지금까지 생성한 섬의 개수 (초반 보정 판단에 사용)</summary>
        private int generatedIslandCount = 0;

        /// <summary>지금까지 생성 확정된 대형 섬 개수 (최소 보장 판단에 사용).</summary>
        private int largeGeneratedCount = 0;

        /// <summary>지금까지 생성 확정된 특대 섬 개수 (최소 보장 판단에 사용).</summary>
        private int extraLargeGeneratedCount = 0;

        /// <summary>
        /// 다음 섬의 규모를 랜덤하게 결정하여 반환한다.
        /// 초반 구간(earlyGameIslandCount 이내)에는 소형 섬 확률에 보너스 배수를 적용해
        /// 시작 지점 근처에는 소형 섬이 더 잘 나오도록 만든다.
        /// </summary>
        /// <param name="islandIndex">지금 생성 중인 섬이 전체 초기 생성 순서에서 몇 번째(0부터)인지.</param>
        /// <param name="totalIslandCount">이번 초기 생성에서 만들 전체 섬 개수.</param>
        public IslandSize GenerateNextIslandSize(int islandIndex, int totalIslandCount)
        {
            // 버그 수정(#1 - 치명): spawnConfig가 Inspector에서 연결되지 않은 채로 호출되면 바로 아래에서
            // NullReferenceException이 터져 WorldMapManager.Start() 체인 전체가 멈추고 섬이 단 하나도
            // 생성되지 않는다(게임이 시작조차 못 함). 다른 스포너들의 방어 패턴(null 조건부 연산자 `?.`,
            // `!= null` 가드)과 맞춰, 여기서도 즉시 실패하지 않고 소형 섬으로 안전하게 폴백하면서
            // 원인을 로그로 명확히 남긴다.
            if (spawnConfig == null)
            {
                Debug.LogError("[IslandGenerator] spawnConfig가 연결되지 않았습니다. 섬 규모를 결정할 수 없어 " +
                    "기본값(Small)으로 대체합니다. Inspector에서 IslandSpawnConfig 에셋을 연결하세요.");
                return IslandSize.Small;
            }

            bool isEarlyGame = generatedIslandCount < spawnConfig.earlyGameIslandCount;
            generatedIslandCount++;

            // 이 섬을 포함하지 않고, 이후로 몇 번의 생성 기회가 더 남았는지.
            int slotsRemainingAfterThis = Mathf.Max(0, totalIslandCount - islandIndex - 1);

            // 남은 기회 안에 특대 섬 최소 보장 개수를 채우지 못할 위기라면(예: 마지막 섬인데 아직 하나도
            // 안 나왔다면) 확률과 상관없이 강제로 특대 섬을 배정해, 배 최종 도면을 구할 방법이 아예
            // 사라지는 시드가 나오지 않도록 한다.
            int extraLargeStillNeeded = Mathf.Max(0, minimumExtraLargeIslands - extraLargeGeneratedCount);
            if (extraLargeStillNeeded > 0 && slotsRemainingAfterThis < extraLargeStillNeeded)
            {
                extraLargeGeneratedCount++;
                return IslandSize.ExtraLarge;
            }

            // 대형 섬도 같은 방식으로 최소 보장한다. 특대 섬 강제 배정 몫은 남은 기회 계산에서 미리 뺀다.
            int largeStillNeeded = Mathf.Max(0, minimumLargeIslands - largeGeneratedCount);
            if (largeStillNeeded > 0 && slotsRemainingAfterThis < largeStillNeeded + extraLargeStillNeeded)
            {
                largeGeneratedCount++;
                return IslandSize.Large;
            }

            float smallWeight = spawnConfig.GetBaseSpawnRate(IslandSize.Small);
            if (isEarlyGame)
                smallWeight *= spawnConfig.earlyGameSmallIslandBonusMultiplier;

            float mediumWeight = spawnConfig.GetBaseSpawnRate(IslandSize.Medium);
            float largeWeight = spawnConfig.GetBaseSpawnRate(IslandSize.Large);
            float extraLargeWeight = spawnConfig.GetBaseSpawnRate(IslandSize.ExtraLarge);

            float totalWeight = smallWeight + mediumWeight + largeWeight + extraLargeWeight;
            float roll = Random.Range(0f, totalWeight);

            IslandSize result;
            if (roll < smallWeight)
            {
                result = IslandSize.Small;
            }
            else
            {
                roll -= smallWeight;
                if (roll < mediumWeight)
                {
                    result = IslandSize.Medium;
                }
                else
                {
                    roll -= mediumWeight;
                    result = roll < largeWeight ? IslandSize.Large : IslandSize.ExtraLarge;
                }
            }

            if (result == IslandSize.Large)
                largeGeneratedCount++;
            else if (result == IslandSize.ExtraLarge)
                extraLargeGeneratedCount++;

            return result;
        }

        /// <summary>
        /// 생성 카운트를 초기화한다. 새 게임을 시작할 때 호출한다.
        /// </summary>
        public void ResetGenerationState()
        {
            generatedIslandCount = 0;
            largeGeneratedCount = 0;
            extraLargeGeneratedCount = 0;
        }
    }
}
