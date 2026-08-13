using System.Collections.Generic;
using UnityEngine;
using MakeGame.Data;

namespace MakeGame.Systems
{
    /// <summary>
    /// 섬 하나에 위험 요소(HazardSource)들을 배치하는 스포너.
    /// 섬 규모가 클수록 위험 요소 등장 확률이 높아진다 (Stranded Deep 기준: 큰 섬일수록 위험도 큼).
    /// 플레이어가 불시착하는 시작 섬(isStartingIsland)에는 안전을 위해 위험 요소를 배치하지 않는다.
    /// </summary>
    public class HazardSpawner : MonoBehaviour
    {
        [System.Serializable]
        public class HazardEntry
        {
            [Tooltip("위험 요소 종류")]
            public HazardType type;

            [Tooltip("소형 섬 기준 기본 등장 확률(0~1). 규모가 커질수록 배율이 곱해진다.")]
            [Range(0f, 1f)]
            public float baseChance = 0.2f;
        }

        [Tooltip("섬에 등장 가능한 위험 요소 종류와 기본 확률 목록")]
        public List<HazardEntry> hazardEntries = new List<HazardEntry>();

        [Header("섬 규모별 등장 확률 배율")]
        public float smallMultiplier = 1f;
        public float mediumMultiplier = 1.5f;
        public float largeMultiplier = 2f;
        public float extraLargeMultiplier = 2.5f;

        [Tooltip("위험 요소를 흩뿌릴 반경")]
        public float scatterRadius = 10f;

        /// <summary>
        /// 지정한 섬에 규모와 확률에 따라 위험 요소를 배치한다. 시작 섬에는 배치하지 않는다.
        /// </summary>
        public List<HazardSource> SpawnHazardsForIsland(IslandInstance island, Transform parent)
        {
            var spawned = new List<HazardSource>();
            if (island == null || island.isStartingIsland)
                return spawned;

            float multiplier = GetMultiplier(island.size);

            foreach (var entry in hazardEntries)
            {
                float chance = Mathf.Clamp01(entry.baseChance * multiplier);
                if (Random.value <= chance)
                {
                    Vector2 offset = Random.insideUnitCircle * scatterRadius;
                    Vector3 position = island.mapPosition + new Vector3(offset.x, 0f, offset.y);
                    position = TerrainSampler.SnapToGround(position);
                    spawned.Add(SpawnSingleHazard(entry.type, position, parent));
                }
            }

            return spawned;
        }

        /// <summary>
        /// 위험 요소 하나를 실제로 생성한다. 시각화용 캡슐 프리미티브에 트리거 콜라이더와 HazardSource를 붙인다.
        /// </summary>
        private HazardSource SpawnSingleHazard(HazardType type, Vector3 position, Transform parent)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            go.transform.SetParent(parent);
            go.transform.localScale = new Vector3(0.6f, 0.6f, 0.6f);
            go.transform.position = position + Vector3.up * 0.6f; // 캡슐 피벗이 중심이므로 절반 높이만큼 띄워 지형 위에 놓이게 한다
            go.name = $"Hazard_{type}";

            var col = go.GetComponent<Collider>();
            if (col != null)
                col.isTrigger = true;

            var hazard = go.AddComponent<HazardSource>();
            hazard.hazardType = type;
            return hazard;
        }

        /// <summary>
        /// 섬 규모에 대응하는 위험 요소 등장 확률 배율을 반환한다.
        /// </summary>
        private float GetMultiplier(IslandSize size)
        {
            switch (size)
            {
                case IslandSize.Small: return smallMultiplier;
                case IslandSize.Medium: return mediumMultiplier;
                case IslandSize.Large: return largeMultiplier;
                case IslandSize.ExtraLarge: return extraLargeMultiplier;
                default: return smallMultiplier;
            }
        }
    }
}
