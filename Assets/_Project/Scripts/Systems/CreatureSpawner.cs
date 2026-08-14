using System.Collections.Generic;
using UnityEngine;
using MakeGame.Data;

namespace MakeGame.Systems
{
    /// <summary>
    /// 섬 하나에 사냥/낚시로 잡을 수 있는 생물(HuntableCreature)들을 배치하는 스포너.
    /// 예전에는 HuntableCreature 스크립트 자체는 완성되어 있었지만 이 스포너가 없어서
    /// 월드 어디에도 사냥감이 실제로 등장하지 않았고, 생고기/생선 아이템 자체를 얻을 방법이
    /// 전혀 없어 모닥불 조리(요리) 시스템도 사실상 사용할 수 없는 죽은 콘텐츠였다.
    /// 섬 규모가 클수록 사냥감/물고기 개체 수가 늘어난다.
    /// </summary>
    public class CreatureSpawner : MonoBehaviour
    {
        [System.Serializable]
        public class CreatureEntry
        {
            [Tooltip("사냥 성공 시 얻는 아이템 (생고기, 생선 등)")]
            public ItemData yieldItem;

            [Tooltip("사냥에 필요한 도구. 비워두면 도구 없이도 시도할 수 있다 (예: 낚시는 도구 불필요, 사냥은 창 필요).")]
            public ItemData requiredTool;

            [Tooltip("소형 섬 기준 기본 배치 개체 수 (규모가 커질수록 배율이 곱해진다)")]
            public int baseCount = 2;

            [Tooltip("사냥 시도 성공 확률 (0~1)")]
            [Range(0f, 1f)]
            public float successChance = 0.7f;

            [Tooltip("잡히거나 도망친 뒤 다시 나타나기까지 걸리는 시간(초)")]
            public float respawnSeconds = 90f;

            [Tooltip("물고기처럼 해안 근처에 배치할지 여부. true면 흩뿌림 반경 바깥쪽 가장자리에 가깝게 배치한다.")]
            public bool preferShoreline = false;
        }

        [Tooltip("섬에 등장 가능한 사냥감/물고기 종류와 기본 개체 수 목록")]
        public List<CreatureEntry> creatureEntries = new List<CreatureEntry>();

        [Header("섬 규모별 개체 수 배율")]
        public float smallMultiplier = 1f;
        public float mediumMultiplier = 1.5f;
        public float largeMultiplier = 2f;
        public float extraLargeMultiplier = 2.5f;

        // 퀄리티 개선: 섬 반지름이 10배로 커진 것(WorldMapManager.GetSizeScale)에 맞춰 함께 10배로 키웠다.
        [Tooltip("사냥감을 흩뿌릴 반경 (섬 플레이스홀더 크기에 맞춰 조절)")]
        public float scatterRadius = 90f;

        /// <summary>
        /// 지정한 섬에 규모에 맞는 개체 수만큼 사냥감/물고기를 배치한다.
        /// </summary>
        public List<HuntableCreature> SpawnCreaturesForIsland(IslandInstance island, Transform parent)
        {
            var spawned = new List<HuntableCreature>();
            if (island == null)
                return spawned;

            float multiplier = GetMultiplier(island.size);

            foreach (var entry in creatureEntries)
            {
                if (entry.yieldItem == null)
                    continue;

                int count = Mathf.RoundToInt(entry.baseCount * multiplier);
                for (int i = 0; i < count; i++)
                {
                    // preferShoreline이면 반경의 바깥쪽 80~100% 지점에 배치해 해안에 가깝게 흉내낸다.
                    float radiusScale = entry.preferShoreline ? Random.Range(0.8f, 1f) : Random.value;
                    Vector2 offset = Random.insideUnitCircle.normalized * scatterRadius * radiusScale;
                    Vector3 position = island.mapPosition + new Vector3(offset.x, 0f, offset.y);
                    position = TerrainSampler.SnapToGround(position);
                    spawned.Add(SpawnSingleCreature(entry, position, parent));
                }
            }

            return spawned;
        }

        /// <summary>
        /// 사냥감/물고기 개체 하나를 실제로 생성한다. 시각화용 캡슐(육상 동물) 또는 구체(물고기) 프리미티브에
        /// HuntableCreature 컴포넌트를 붙인다.
        /// </summary>
        private HuntableCreature SpawnSingleCreature(CreatureEntry entry, Vector3 position, Transform parent)
        {
            PrimitiveType primitiveType = entry.preferShoreline ? PrimitiveType.Sphere : PrimitiveType.Capsule;
            GameObject go = GameObject.CreatePrimitive(primitiveType);
            go.transform.SetParent(parent);

            if (entry.preferShoreline)
            {
                go.transform.localScale = new Vector3(0.35f, 0.2f, 0.5f); // 납작하고 길쭉한 물고기 형태
                go.transform.position = position + Vector3.up * 0.15f;
                go.name = $"Fish_{entry.yieldItem.itemName}";
            }
            else
            {
                go.transform.localScale = new Vector3(0.45f, 0.6f, 0.45f); // 작은 동물 크기의 캡슐
                go.transform.position = position + Vector3.up * 0.6f;
                go.name = $"Creature_{entry.yieldItem.itemName}";
            }

            Color bodyColor = entry.preferShoreline
                ? new Color(0.35f, 0.55f, 0.65f) // 물고기: 청회색
                : new Color(0.55f, 0.4f, 0.25f); // 육상 동물: 갈색

            var renderer = go.GetComponent<MeshRenderer>();
            if (renderer != null)
                renderer.sharedMaterial = StructureVisualBuilder.CreateColorMaterial(bodyColor);

            // 퀄리티 개선: 몸통 프리미티브 하나뿐이라 "어떤 생물인지" 형태로는 전혀 구분되지 않던 문제를
            // 완화하기 위해 눈/꼬리지느러미 같은 작은 보조 파츠를 붙인다. 부모의 비균일 스케일 때문에
            // 파츠가 타원으로 찌그러지지 않도록 로컬 스케일을 부모 스케일로 나눠 보정한다.
            Vector3 s = go.transform.localScale;
            if (entry.preferShoreline)
            {
                AddCompensated(go, PrimitiveType.Sphere, new Vector3(0f, 0.1f, 0.4f), new Vector3(0.12f, 0.12f, 0.12f), s, new Color(0.05f, 0.05f, 0.05f), "Eye");
                AddCompensated(go, PrimitiveType.Cube, new Vector3(0f, 0f, -0.55f), new Vector3(0.15f, 0.3f, 0.2f), s, bodyColor * 0.8f, "TailFin");
            }
            else
            {
                AddCompensated(go, PrimitiveType.Sphere, new Vector3(0.12f, 0.6f, 0.28f), new Vector3(0.08f, 0.08f, 0.08f), s, new Color(0.05f, 0.05f, 0.05f), "EyeL");
                AddCompensated(go, PrimitiveType.Sphere, new Vector3(-0.12f, 0.6f, 0.28f), new Vector3(0.08f, 0.08f, 0.08f), s, new Color(0.05f, 0.05f, 0.05f), "EyeR");
            }

            var creature = go.AddComponent<HuntableCreature>();
            creature.yieldItem = entry.yieldItem;
            creature.requiredTool = entry.requiredTool;
            creature.successChance = entry.successChance;
            creature.respawnSeconds = entry.respawnSeconds;
            return creature;
        }

        /// <summary>
        /// 부모의 비균일 스케일을 상쇄한 보조 파츠(눈, 꼬리지느러미 등)를 만든다.
        /// worldSize를 부모 localScale로 나눠 자식의 localScale로 지정하면, 부모가 아무리
        /// 눌리거나 늘어나 있어도(예: 납작한 물고기) 파츠가 세계 좌표 기준으로 의도한 크기로 보인다.
        /// </summary>
        private void AddCompensated(GameObject parent, PrimitiveType primitive, Vector3 localPos, Vector3 worldSize, Vector3 parentScale, Color color, string name)
        {
            Vector3 compScale = new Vector3(
                worldSize.x / Mathf.Max(0.0001f, parentScale.x),
                worldSize.y / Mathf.Max(0.0001f, parentScale.y),
                worldSize.z / Mathf.Max(0.0001f, parentScale.z));
            StructureVisualBuilder.CreateVisualPart(parent.transform, name, primitive, localPos, compScale, color);
        }

        /// <summary>
        /// 섬 규모에 대응하는 사냥감/물고기 개체 수 배율을 반환한다.
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
