using System;
using System.Collections.Generic;
using UnityEngine;

namespace MakeGame.Data
{
    /// <summary>
    /// 섬 생성 시 규모별 등장 확률과 초반 보정치를 담는 ScriptableObject.
    /// 기본 비율: 소 50% / 중 30% / 대 15% / 특대 5% (Docs/Story/story_dictionary.json 기준).
    /// </summary>
    [CreateAssetMenu(fileName = "NewIslandSpawnConfig", menuName = "MakeGame/Island Spawn Config", order = 1)]
    public class IslandSpawnConfig : ScriptableObject
    {
        [Serializable]
        public class SizeWeight
        {
            public IslandSize size;

            [Range(0f, 100f)]
            [Tooltip("기본 등장 확률(%)")]
            public float baseSpawnRatePercent;
        }

        [Tooltip("섬 규모별 기본 등장 확률 목록")]
        public List<SizeWeight> sizeWeights = new List<SizeWeight>
        {
            new SizeWeight { size = IslandSize.Small, baseSpawnRatePercent = 50f },
            new SizeWeight { size = IslandSize.Medium, baseSpawnRatePercent = 30f },
            new SizeWeight { size = IslandSize.Large, baseSpawnRatePercent = 15f },
            new SizeWeight { size = IslandSize.ExtraLarge, baseSpawnRatePercent = 5f },
        };

        [Tooltip("초반 몇 번째 섬까지 소형 섬 등장 확률에 가중치를 더 줄지")]
        public int earlyGameIslandCount = 3;

        [Tooltip("초반 구간에서 소형 섬 등장 확률에 곱해줄 배수")]
        public float earlyGameSmallIslandBonusMultiplier = 2f;

        /// <summary>
        /// 지정한 섬 규모의 기본 등장 확률(%)을 반환한다. 목록에 없으면 0을 반환한다.
        /// </summary>
        public float GetBaseSpawnRate(IslandSize size)
        {
            foreach (var weight in sizeWeights)
            {
                if (weight.size == size)
                    return weight.baseSpawnRatePercent;
            }
            return 0f;
        }
    }
}
