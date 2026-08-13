using System.Collections.Generic;
using UnityEngine;
using MakeGame.Data;

namespace MakeGame.Systems
{
    /// <summary>
    /// 섬 하나에 채집 가능한 자원 노드들을 배치하는 스포너.
    /// 섬 규모가 클수록 더 많은 자원 노드를 생성한다 (Stranded Deep 기준: 큰 섬일수록 자원이 풍부).
    /// 실제 지형/나무 에셋이 없으므로, 큐브 플레이스홀더에 ResourceNode를 붙여 시각화한다.
    /// </summary>
    public class IslandResourceSpawner : MonoBehaviour
    {
        [System.Serializable]
        public class ResourceEntry
        {
            [Tooltip("이 자원 노드를 채집했을 때 얻는 아이템")]
            public ItemData yieldItem;

            [Tooltip("소형 섬 기준 기본 배치 개수 (규모가 커질수록 배율이 곱해진다)")]
            public int baseCount = 3;
        }

        [Tooltip("섬에 배치할 자원 종류와 기본 개수 목록")]
        public List<ResourceEntry> resourceEntries = new List<ResourceEntry>();

        [Header("섬 규모별 배치 배율")]
        public float smallMultiplier = 1f;
        public float mediumMultiplier = 2f;
        public float largeMultiplier = 3f;
        public float extraLargeMultiplier = 4f;

        [Tooltip("자원 노드를 흩뿌릴 반경 (섬 플레이스홀더 크기에 맞춰 조절)")]
        public float scatterRadius = 8f;

        /// <summary>
        /// 지정한 섬 인스턴스 위에 규모에 맞는 개수만큼 자원 노드를 생성한다.
        /// 각 노드는 섬 위치를 중심으로 scatterRadius 반경 안에 무작위 배치된다.
        /// </summary>
        public List<ResourceNode> SpawnResourcesForIsland(IslandInstance island, Transform parent)
        {
            var spawned = new List<ResourceNode>();
            if (island == null)
                return spawned;

            float multiplier = GetMultiplier(island.size);

            foreach (var entry in resourceEntries)
            {
                if (entry.yieldItem == null)
                    continue;

                int count = Mathf.RoundToInt(entry.baseCount * multiplier);
                for (int i = 0; i < count; i++)
                {
                    Vector2 offset = Random.insideUnitCircle * scatterRadius;
                    Vector3 position = island.mapPosition + new Vector3(offset.x, 0f, offset.y);
                    position = TerrainSampler.SnapToGround(position);
                    spawned.Add(SpawnSingleNode(entry.yieldItem, position, parent));
                }
            }

            return spawned;
        }

        /// <summary>
        /// 자원 노드 하나를 실제로 생성한다. 시각화용 큐브 프리미티브에 ResourceNode 컴포넌트를 붙인다.
        /// </summary>
        private ResourceNode SpawnSingleNode(ItemData yieldItem, Vector3 position, Transform parent)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.transform.SetParent(parent);
            go.transform.localScale = new Vector3(1f, 1.5f, 1f);
            go.transform.position = position + Vector3.up * 0.75f; // 큐브 피벗이 중심이므로 절반 높이만큼 띄워 지형 위에 놓이게 한다
            go.name = $"Resource_{yieldItem.itemName}";

            var node = go.AddComponent<ResourceNode>();
            node.yieldItem = yieldItem;
            node.remainingHarvestCount = node.maxHarvestCount;
            return node;
        }

        /// <summary>
        /// 섬 규모에 대응하는 자원 개수 배율을 반환한다.
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
