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

        // 긴급 정정(#2 회귀 수정): 이 필드들을 한 차례 제거하고 IslandSizeMetrics 직접 호출로 바꿨었는데,
        // 실제 배포된 SampleScene.unity에 이 컴포넌트가 배치되어 있고 이 필드들에 코드 기본값과 다른
        // 값(디자이너가 조정한 실제 밸런스 값)이 직렬화되어 있다는 사실이 뒤늦게 확인되었다. 필드를
        // 제거하면 Unity가 그 직렬화 값을 잃어버리고 조용히 코드 기본값으로 되돌아간다 - "스테이징 범위에
        // 씬 파일이 없다"는 것이 "프로젝트에 씬 파일이 없다"는 뜻이 아니었다. 필드명/타입/기본값을
        // 원래(리팩터링 이전) 그대로 복원해 씬 직렬화 값이 다시 정상적으로 바인딩되도록 되돌렸다.
        // IslandSizeMetrics는 삭제하지 않고, 이 필드가 의미 있게 설정되지 않았을 때(0 이하)만 쓰는
        // "폴백 단일 소스"로 역할을 낮췄다 (GetMultiplier/GetScatterRadius 참고).
        [Header("섬 규모별 개체 수 배율")]
        public float smallMultiplier = 1f;
        public float mediumMultiplier = 1.5f;
        public float largeMultiplier = 2f;
        public float extraLargeMultiplier = 2.5f;

        [Header("섬 규모별 산포 반경")]
        // 버그 수정 (#1006): 예전에는 scatterRadius가 섬 규모와 무관한 값 하나(90f)뿐이었다.
        // WorldMapManager.GetSizeScale의 지형 반지름(50/90/140/200)과 어긋나 있어서, 소형 섬에서는
        // 사냥감이 바다 쪽에 배치될 수 있었고 특대 섬에서는 중심 근처로만 몰렸다. IslandResourceSpawner/
        // HazardSpawner와 동일하게 각 섬 지형 반지름의 80%에 맞춰 규모별 반경을 따로 뒀다.
        // [B5 디렉터 수정] 위 주석은 "규모별로 따로 뒀다"고 적혀 있었지만 실제로는 네 값이 전부 같았다.
        // 즉 주석이 고쳤다고 주장하는 버그가 그대로 살아 있었다(qa-reviewer 지적). 소형 섬(지형 반지름 50)에
        // 반경 90로 흩뿌리면 배치물이 바다로 나가고, 특대 섬(200)은 중심 근처에만 몰린다.
        // IslandSizeMetrics.GetTerrainRadius(50/90/140/200)의 80%로 실제로 분리했다.
        // 씬의 낡은 `scatterRadius` 단일 키는 코드에 대응 필드가 없는 죽은 키라 함께 제거했다.
        public float smallScatterRadius = 40f;
        public float mediumScatterRadius = 72f;
        public float largeScatterRadius = 112f;
        public float extraLargeScatterRadius = 160f;

        /// <summary>
        /// 지정한 섬에 규모에 맞는 개체 수만큼 사냥감/물고기를 배치한다.
        /// B3-3: worldSeed를 추가로 받아, 이 섬(island.islandId) 전용 결정적 System.Random 스트림으로
        /// 배치 위치·크기/방향 지터를 전부 뽑는다(재현성 근거는 IslandResourceSpawner 상단 주석과 동일).
        /// 각 개체마다 (island.islandId, spawnOrder) 식별자를 부여한다.
        /// </summary>
        public List<HuntableCreature> SpawnCreaturesForIsland(IslandInstance island, Transform parent, int worldSeed)
        {
            var spawned = new List<HuntableCreature>();
            if (island == null)
                return spawned;

            System.Random rng = SeededRandomExtensions.CreateForIsland(worldSeed, island.islandId);
            int spawnOrder = 0;

            float multiplier = GetMultiplier(island.size);
            float radius = GetScatterRadius(island.size);

            foreach (var entry in creatureEntries)
            {
                if (entry.yieldItem == null)
                    continue;

                int count = Mathf.RoundToInt(entry.baseCount * multiplier);
                for (int i = 0; i < count; i++)
                {
                    // preferShoreline이면 반경의 바깥쪽 80~100% 지점에 배치해 해안에 가깝게 흉내낸다.
                    float radiusScale = entry.preferShoreline ? rng.NextFloat(0.8f, 1f) : rng.NextValue01();
                    Vector2 offset = rng.NextInsideUnitCircle().normalized * radius * radiusScale;
                    Vector3 position = island.mapPosition + new Vector3(offset.x, 0f, offset.y);
                    position = TerrainSampler.SnapToGround(position);
                    spawned.Add(SpawnSingleCreature(entry, position, parent, rng, island.islandId, spawnOrder));
                    spawnOrder++;
                }
            }

            return spawned;
        }

        /// <summary>
        /// 사냥감/물고기 개체 하나를 실제로 생성한다. 시각화용 캡슐(육상 동물) 또는 구체(물고기) 프리미티브에
        /// HuntableCreature 컴포넌트를 붙인다.
        /// </summary>
        private HuntableCreature SpawnSingleCreature(CreatureEntry entry, Vector3 position, Transform parent, System.Random rng, int islandIndex, int spawnOrder)
        {
            PrimitiveType primitiveType = entry.preferShoreline ? PrimitiveType.Sphere : PrimitiveType.Capsule;
            GameObject go = GameObject.CreatePrimitive(primitiveType);
            go.transform.SetParent(parent);

            // 퀄리티 개선(#324 재점검): 자원 노드와 같은 문제 - 같은 종류 개체가 완전히 동일한 크기로
            // 찍혀 클론처럼 보이는 것을 막기 위해 개체마다 살짝 다른 크기 배율과 몸 방향(Y축 회전)을 준다.
            // B3-3: 시드 없는 UnityEngine.Random 대신 이 섬 전용 rng(System.Random)를 쓴다.
            float sizeJitter = rng.NextFloat(0.9f, 1.15f);
            Quaternion facing = Quaternion.Euler(0f, rng.NextFloat(0f, 360f), 0f);

            if (entry.preferShoreline)
            {
                go.transform.localScale = new Vector3(0.35f, 0.2f, 0.5f) * sizeJitter; // 납작하고 길쭉한 물고기 형태
                go.transform.position = position + Vector3.up * 0.15f;
                go.transform.rotation = facing;
                go.name = $"Fish_{entry.yieldItem.itemName}";
            }
            else
            {
                go.transform.localScale = new Vector3(0.45f, 0.6f, 0.45f) * sizeJitter; // 작은 동물 크기의 캡슐
                go.transform.position = position + Vector3.up * 0.6f;
                go.transform.rotation = facing;
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
                // [tech-artist-B 요청 - 눈이 몸통 안에 묻혀 있다] 기존 로컬 (±0.12, 0.6, 0.28)은 몸통 캡슐
                // 상단 반구 중심(0, 0.5, 0)에서 로컬 거리 0.32였다 - 표면(0.5)보다 안쪽이라 어느 각도에서도
                // 보이지 않았다(HuntableCreature.BuildHeadSilhouette 주석의 실측과 동일한 값).
                // 이제 눈은 몸통이 아니라 HuntableCreature가 붙이는 머리 돌기(로컬 (0, 0.72, 0.42),
                // 월드 반지름 0.14m) 표면에 얹는다. 검산(몸통 스케일 (0.45, 0.6, 0.45) 기준, 월드 단위):
                //   눈 중심   = (0.11·0.45, 0.81·0.60, 0.68·0.45) = (0.0495, 0.486, 0.306)
                //   머리 중심 = (0,         0.72·0.60, 0.42·0.45) = (0,      0.432, 0.189)
                //   두 점 거리 = √(0.0495² + 0.054² + 0.117²) = 0.1380m ≈ 머리 반지름 0.14m
                // 즉 눈 중심이 머리 표면 바로 위(0.002m 안쪽)에 놓여 눈 구체의 절반이 머리 밖으로 드러난다.
                // 세 번째 인자는 반지름이 아니라 월드 "지름"이다(AddCompensated는 worldSize를 그대로
                // localScale로 환산한다 - CreatureVisualBuilder.AddCompensatedSphere의 worldRadius와 다르다).
                // 0.08 → 0.05로 줄인 이유: 지름 0.28m짜리 머리 위에서 0.08m 눈은 과하게 커 보인다.
                // 주의: 두 파츠 모두 몸통(go) 로컬 좌표계의 형제이므로, 머리 위치/반지름이 바뀌면 이 값도
                // 함께 다시 계산해야 한다(HuntableCreature.BuildHeadSilhouette와 짝을 이루는 값이다).
                AddCompensated(go, PrimitiveType.Sphere, new Vector3(0.11f, 0.81f, 0.68f), new Vector3(0.05f, 0.05f, 0.05f), s, new Color(0.05f, 0.05f, 0.05f), "EyeL");
                AddCompensated(go, PrimitiveType.Sphere, new Vector3(-0.11f, 0.81f, 0.68f), new Vector3(0.05f, 0.05f, 0.05f), s, new Color(0.05f, 0.05f, 0.05f), "EyeR");

                // 연결(A-2): tech-artist가 만든 CreatureVisualBuilder.AddQuadrupedLegs를 호출해 짧은 다리
                // 4개를 붙인다. 몸통 캡슐 + 눈뿐이면 사람 형태(곰/식인종 HazardSource 캡슐)와 실루엣이
                // 겹쳐 구분이 안 되던 문제를 보강한다. 물고기(preferShoreline) 분기에는 넣지 않는다.
                CreatureVisualBuilder.AddQuadrupedLegs(go, s, bodyColor);
            }

            var creature = go.AddComponent<HuntableCreature>();
            creature.yieldItem = entry.yieldItem;
            creature.requiredTool = entry.requiredTool;
            creature.successChance = entry.successChance;
            creature.respawnSeconds = entry.respawnSeconds;
            creature.islandIndex = islandIndex;
            creature.spawnOrder = spawnOrder;
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
        /// 긴급 정정(#2 회귀 수정): 인스펙터(씬 직렬화)에 설정된 필드 값을 항상 우선한다. 필드가 0
        /// 이하로 남아있어(설정 실수/아직 배치 안 된 새 컴포넌트 등) 의미 있게 설정되지 않은 경우에만
        /// IslandSizeMetrics.GetLinearDensityMultiplier를 안전한 기본값 폴백으로 사용한다.
        /// </summary>
        private float GetMultiplier(IslandSize size)
        {
            switch (size)
            {
                case IslandSize.Small: return smallMultiplier > 0f ? smallMultiplier : IslandSizeMetrics.GetLinearDensityMultiplier(size);
                case IslandSize.Medium: return mediumMultiplier > 0f ? mediumMultiplier : IslandSizeMetrics.GetLinearDensityMultiplier(size);
                case IslandSize.Large: return largeMultiplier > 0f ? largeMultiplier : IslandSizeMetrics.GetLinearDensityMultiplier(size);
                case IslandSize.ExtraLarge: return extraLargeMultiplier > 0f ? extraLargeMultiplier : IslandSizeMetrics.GetLinearDensityMultiplier(size);
                default: return smallMultiplier > 0f ? smallMultiplier : IslandSizeMetrics.GetLinearDensityMultiplier(size);
            }
        }

        /// <summary>
        /// 섬 규모에 대응하는 사냥감/물고기 산포 반경을 반환한다.
        /// 긴급 정정(#2 회귀 수정): 인스펙터(씬 직렬화)에 설정된 필드 값을 항상 우선한다. 필드가 0 이하로
        /// 남아있을 때만 IslandSizeMetrics.GetScatterRadius를 안전한 기본값 폴백으로 사용한다.
        /// </summary>
        private float GetScatterRadius(IslandSize size)
        {
            switch (size)
            {
                case IslandSize.Small: return smallScatterRadius > 0f ? smallScatterRadius : IslandSizeMetrics.GetScatterRadius(size);
                case IslandSize.Medium: return mediumScatterRadius > 0f ? mediumScatterRadius : IslandSizeMetrics.GetScatterRadius(size);
                case IslandSize.Large: return largeScatterRadius > 0f ? largeScatterRadius : IslandSizeMetrics.GetScatterRadius(size);
                case IslandSize.ExtraLarge: return extraLargeScatterRadius > 0f ? extraLargeScatterRadius : IslandSizeMetrics.GetScatterRadius(size);
                default: return smallScatterRadius > 0f ? smallScatterRadius : IslandSizeMetrics.GetScatterRadius(size);
            }
        }
    }
}
