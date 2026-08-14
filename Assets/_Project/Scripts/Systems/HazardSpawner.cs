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
        /// SharkSpawner처럼 섬이 아닌 곳(바다 한가운데)에 위험 요소를 배치해야 하는 다른 스포너가
        /// 이 클래스의 시각/전투 설정 테이블(GetVisualConfig, HazardSource.ConfigureForType)을 그대로
        /// 재사용할 수 있도록 공개한 진입점. 섬 배치(SpawnHazardsForIsland)와 달리 확률/섬 규모 개념이
        /// 없고, 호출자가 이미 정한 위치에 정확히 하나를 생성한다.
        /// </summary>
        public HazardSource SpawnHazardAtPosition(HazardType type, Vector3 position, Transform parent)
        {
            return SpawnSingleHazard(type, position, parent);
        }

        /// <summary>
        /// 위험 요소 하나를 실제로 생성한다. 종류별로 형태/크기/색상/회전이 다른 프리미티브를 사용해
        /// 플레이어가 캡슐 하나로는 구분할 수 없던 곰/식인종/독사/전갈/벌떼/함정/상어를 한눈에 구별할 수 있게 한다.
        /// </summary>
        private HazardSource SpawnSingleHazard(HazardType type, Vector3 position, Transform parent)
        {
            HazardVisualConfig config = GetVisualConfig(type);

            GameObject go = GameObject.CreatePrimitive(config.primitiveType);
            go.transform.SetParent(parent);
            go.transform.localScale = config.localScale;
            go.transform.rotation = Quaternion.Euler(config.rotationEuler);
            go.transform.position = position + Vector3.up * config.groundOffset;
            go.name = $"Hazard_{type}";

            var renderer = go.GetComponent<MeshRenderer>();
            if (renderer != null)
                renderer.sharedMaterial = StructureVisualBuilder.CreateColorMaterial(config.color);

            var col = go.GetComponent<Collider>();
            if (col != null)
                col.isTrigger = true;

            var hazard = go.AddComponent<HazardSource>();
            hazard.hazardType = type;
            hazard.ConfigureForType(); // 종류(곰/식인종/벌떼 등)에 맞춰 전투 가능 여부와 체력을 설정한다.
            return hazard;
        }

        /// <summary>
        /// 위험 요소 시각 정보(프리미티브 종류, 크기, 회전, 색상, 지면으로부터 띄울 높이)를 담는 구조체.
        /// </summary>
        private struct HazardVisualConfig
        {
            public PrimitiveType primitiveType;
            public Vector3 localScale;
            public Vector3 rotationEuler;
            public Color color;
            public float groundOffset;
        }

        /// <summary>
        /// 위험 요소 종류별로 구분 가능한 형태/크기/색상을 반환한다.
        /// 곰=크고 진한 갈색 캡슐, 식인종=사람 크기의 적갈색 캡슐, 독사=길고 납작한 초록 캡슐(눕혀서 배치),
        /// 전갈=작고 납작한 어두운 주황 캡슐, 벌떼=작은 노란 구체, 함정=땅에 깔린 어두운 회갈색 원판.
        /// </summary>
        private HazardVisualConfig GetVisualConfig(HazardType type)
        {
            switch (type)
            {
                case HazardType.Bear:
                    return new HazardVisualConfig
                    {
                        primitiveType = PrimitiveType.Capsule,
                        localScale = new Vector3(0.9f, 1.1f, 0.9f),
                        rotationEuler = Vector3.zero,
                        color = new Color(0.32f, 0.2f, 0.12f), // 진한 갈색
                        groundOffset = 1.1f
                    };

                case HazardType.Cannibal:
                    return new HazardVisualConfig
                    {
                        primitiveType = PrimitiveType.Capsule,
                        localScale = new Vector3(0.55f, 0.9f, 0.55f),
                        rotationEuler = Vector3.zero,
                        color = new Color(0.6f, 0.35f, 0.25f), // 적갈색
                        groundOffset = 0.9f
                    };

                case HazardType.VenomousSnake:
                    return new HazardVisualConfig
                    {
                        primitiveType = PrimitiveType.Capsule,
                        localScale = new Vector3(0.18f, 0.6f, 0.18f),
                        rotationEuler = new Vector3(0f, 0f, 90f), // 눕혀서 길게 배치
                        color = new Color(0.15f, 0.55f, 0.2f), // 초록
                        groundOffset = 0.1f
                    };

                case HazardType.Scorpion:
                    return new HazardVisualConfig
                    {
                        primitiveType = PrimitiveType.Capsule,
                        localScale = new Vector3(0.16f, 0.3f, 0.16f),
                        rotationEuler = new Vector3(0f, 0f, 90f), // 눕혀서 낮고 짧게 배치
                        color = new Color(0.45f, 0.22f, 0.05f), // 어두운 주황/흙색
                        groundOffset = 0.09f
                    };

                case HazardType.BeeSwarm:
                    return new HazardVisualConfig
                    {
                        primitiveType = PrimitiveType.Sphere,
                        localScale = new Vector3(0.5f, 0.5f, 0.5f),
                        rotationEuler = Vector3.zero,
                        color = new Color(0.95f, 0.75f, 0.1f), // 노란색
                        groundOffset = 1.4f // 벌떼는 공중에 떠 있게
                    };

                case HazardType.Trap:
                    return new HazardVisualConfig
                    {
                        primitiveType = PrimitiveType.Cylinder,
                        localScale = new Vector3(0.6f, 0.04f, 0.6f), // 얇은 원판 형태로 땅에 깔아둔다
                        rotationEuler = Vector3.zero,
                        color = new Color(0.35f, 0.3f, 0.25f), // 어두운 회갈색
                        groundOffset = 0.04f
                    };

                case HazardType.Shark:
                    return new HazardVisualConfig
                    {
                        primitiveType = PrimitiveType.Capsule,
                        localScale = new Vector3(0.45f, 1.4f, 0.45f), // 길쭉하게 눕혀서 상어 몸통처럼 보이게
                        rotationEuler = new Vector3(0f, 0f, 90f),
                        color = new Color(0.28f, 0.35f, 0.42f), // 어두운 청회색
                        groundOffset = 0f // SharkSpawner가 이미 해수면 아래 정확한 위치를 계산해 넘겨준다
                    };

                default:
                    return new HazardVisualConfig
                    {
                        primitiveType = PrimitiveType.Capsule,
                        localScale = new Vector3(0.6f, 0.6f, 0.6f),
                        rotationEuler = Vector3.zero,
                        color = Color.gray,
                        groundOffset = 0.6f
                    };
            }
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
