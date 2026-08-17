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

            // [B9] 야간 사냥 보너스(Docs/Design_MidGameContent.md 4장 "안 2"). 종류별로 야행성 정도를
            // 다르게 줄 수 있도록 엔트리에 둔다. HuntableCreature의 같은 이름 필드로 그대로 전달된다.
            // ⚠️ 이 두 필드는 리스트의 **끝에 추가**했다. 씬 creatureEntries(SampleScene.unity:1244~1256)에는
            // 아직 이 YAML 키가 없는데, Unity는 직렬화된 키가 없는 필드에 대해 C# 필드 초기값을 그대로
            // 남기므로 씬을 고치지 않아도 0.2 / 1이 살아 있다(디렉터 조치는 선택 사항).
            // 엔트리 개수·순서는 손대지 않았다 — 필드 추가만으로는 스폰 개수·순서·난수 소비가
            // 전혀 움직이지 않는다([세이브 키 v2] 세이브 키는 이제 종류별 안정 해시라 애초에 안 밀린다).
            [Tooltip("밤에 사냥 성공 확률에 더할 보너스. 0이면 이 종류는 밤낮 차이가 없다")]
            public float nightSuccessBonus = 0.2f;

            [Tooltip("밤에 사냥 성공 시 추가로 더 주는 수확 개수. 0이면 이 종류는 밤낮 차이가 없다")]
            public int nightYieldBonus = 1;

            // [B30] 게(소형 크랩) 표시. 켜면 물고기/육상 사냥감 대신 게 외형(CreatureVisualBuilder.BuildCrabBody,
            // giant=false)으로 만들고, 배치는 해안선 선호로 강제된다(게는 조간대 생물이다 - 대왕 크랩이
            // HazardSpawner.PrefersShoreline으로 같은 규칙을 쓰는 것과 짝이 맞는다).
            //
            // ⚠️ **이 필드는 리스트의 맨 끝에 추가했다.** 씬 creatureEntries(SampleScene.unity:1244~1256)에는
            // 이 YAML 키가 없고, Unity는 직렬화된 키가 없는 필드에 C# 초기값을 그대로 남기므로 기존 두 항목
            // (생고기/창/육상, 생선/도구없음/해안)은 false로 읽혀 예전과 100% 동일하게 동작한다.
            // 기존 필드는 하나도 제거·개명·재정렬하지 않았다.
            //
            // ⚠️ 게 항목을 씬에 추가할 때도 **creatureEntries의 맨 끝**에 넣기를 권장한다.
            // [세이브 키 v2] 세이브 키는 종류별 안정 해시(stableKey)로 바뀌어 중간 삽입이 **세이브를
            // 깨뜨리지는 않게 됐다.** 다만 순서가 바뀌면 개체들의 rng draw 순서가 바뀌어 같은 worldSeed의
            // 배치(위치·지터)가 달라진다 - "같은 시드 = 같은 월드" 재현성을 위해 맨 끝 추가 관례는 유지한다.
            // 이 스포너는 creatureEntries를 정렬하거나 필터링하지 않고 선언 순서대로만 순회한다(아래 foreach).
            [Tooltip("이 항목을 게(소형 크랩)로 만들지 여부. 켜면 해안선 배치 + 게 외형이 된다.")]
            public bool isCrab = false;
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
        /// 각 개체마다 (island.islandId, spawnOrder) 식별자와 [세이브 키 v2] 결정론적 안정 키
        /// (HuntableCreature.stableKey)를 부여한다. 세이브 대조는 stableKey로만 한다.
        /// </summary>
        public List<HuntableCreature> SpawnCreaturesForIsland(IslandInstance island, Transform parent, int worldSeed)
        {
            var spawned = new List<HuntableCreature>();
            if (island == null)
                return spawned;

            System.Random rng = SeededRandomExtensions.CreateForIsland(worldSeed, island.islandId);
            int spawnOrder = 0;

            // [세이브 키 v2] 종류 이름 → 이 섬에서 지금까지 스폰된 그 종류의 마릿수. 같은 종류 안에서만
            // 세므로 다른 종류의 개수가 바뀌어도 이 종류의 키는 밀리지 않는다. rng 소비량 0(순수 계산).
            var perTypeCounts = new Dictionary<string, int>();

            float multiplier = GetMultiplier(island.size);
            float radius = GetScatterRadius(island.size);
            // [육상 필수 재배치] 해수면. IslandResourceSpawner와 같은 공유 유틸로 읽는다(못 찾으면 0).
            float seaLevel = SpawnLandPlacement.ResolveSeaLevel();

            foreach (var entry in creatureEntries)
            {
                if (entry.yieldItem == null)
                    continue;

                int count = Mathf.RoundToInt(entry.baseCount * multiplier);
                for (int i = 0; i < count; i++)
                {
                    // [육상 필수 재배치] 뭍 생물(현재 씬 기준 생고기 사냥감 1종 - 게도 물고기도 아닌
                    // 항목)이 수면 아래에 떨어지면 위치만 재추첨한다. 게·물고기는 해안/물이 자연스러워
                    // 예전 경로 그대로다(draw 수도 예전과 동일). 어떤 경우에도 개체를 건너뛰지 않으므로
                    // count와 perTypeIndex(stableKey v2)는 불변 - 세이브 대조는 안전하다. 단, 재추첨이
                    // draw를 추가 소비하면 같은 worldSeed의 배치가 이전 버전과 달라진다(1회성 변화).
                    Vector3 position = PickCreaturePosition(island, entry, radius, rng, seaLevel);

                    // [세이브 키 v2] 같은 종류 안에서의 생성 순번. 종류 이름은 GetStableTypeName 참고
                    // (게는 물고기와 yieldItem이 같아도 다른 종류로 센다).
                    string typeName = GetStableTypeName(entry);
                    perTypeCounts.TryGetValue(typeName, out int perTypeIndex);
                    perTypeCounts[typeName] = perTypeIndex + 1;

                    spawned.Add(SpawnSingleCreature(entry, position, parent, rng, island.islandId, spawnOrder, typeName, perTypeIndex));
                    spawnOrder++;
                }
            }

            return spawned;
        }

        /// <summary>
        /// 사냥감/물고기/게 개체 하나를 실제로 생성한다. 시각화용 캡슐(육상 동물) 또는 구체(물고기·게)
        /// 프리미티브에 HuntableCreature 컴포넌트를 붙인다.
        /// </summary>
        private HuntableCreature SpawnSingleCreature(CreatureEntry entry, Vector3 position, Transform parent, System.Random rng, int islandIndex, int spawnOrder,
            string stableTypeName, int perTypeIndex)
        {
            // [B30] 종류는 셋이다: 게(isCrab) / 물고기(게가 아니면서 preferShoreline) / 육상 사냥감(그 외).
            // 게도 물고기와 같은 Sphere 프리미티브를 쓴다 - 몸통 메시는 BuildCrabBody가 갈아 끼우고
            // 남는 것은 SphereCollider뿐인데, 등딱지가 넓고 낮은 소형 게에는 구 판정이 가장 가깝다
            // (대왕 크랩만 1.6m 등딱지라 큐브 판정을 쓴다 - HazardSpawner.GetVisualConfig 주석 참고).
            bool isCrab = entry.isCrab;
            bool isFish = !isCrab && entry.preferShoreline;
            PrimitiveType primitiveType = (isCrab || isFish) ? PrimitiveType.Sphere : PrimitiveType.Capsule;
            GameObject go = GameObject.CreatePrimitive(primitiveType);
            go.transform.SetParent(parent);

            // 퀄리티 개선(#324 재점검): 자원 노드와 같은 문제 - 같은 종류 개체가 완전히 동일한 크기로
            // 찍혀 클론처럼 보이는 것을 막기 위해 개체마다 살짝 다른 크기 배율과 몸 방향(Y축 회전)을 준다.
            // B3-3: 시드 없는 UnityEngine.Random 대신 이 섬 전용 rng(System.Random)를 쓴다.
            float sizeJitter = rng.NextFloat(0.9f, 1.15f);
            Quaternion facing = Quaternion.Euler(0f, rng.NextFloat(0f, 360f), 0f);

            if (isCrab)
            {
                // 규격은 **숫자를 옮겨 적지 않고** CreatureVisualBuilder의 상수를 직접 참조한다. 대왕 크랩을
                // 놓는 HazardSpawner도 같은 상수를 참조하고 있어(HazardSpawner.cs:641·644), 규격이 한쪽만
                // 바뀌어 게가 땅에 묻히거나 뜨는 이 프로젝트의 단골 사고가 원천 봉쇄된다.
                go.transform.localScale = CreatureVisualBuilder.CrabSmallBodyScale * sizeJitter;
                // 접지 높이에도 sizeJitter를 곱하는 것은 아래 물고기/육상과 같은 [B29] 규칙이다.
                // (BuildCrabBody가 giant일 때만 하는 SnapPivotForJitter 보정은 그래서 소형에 필요 없다.)
                go.transform.position = position + Vector3.up * (CreatureVisualBuilder.CrabSmallGroundOffset * sizeJitter);
                go.transform.rotation = facing;
                go.name = $"Crab_{entry.yieldItem.itemName}";
            }
            else if (isFish)
            {
                go.transform.localScale = new Vector3(0.35f, 0.2f, 0.5f) * sizeJitter; // 납작하고 길쭉한 물고기 형태
                // [B29] 띄우는 높이에도 sizeJitter를 곱한다. 몸통 바닥은 스케일에 비례해 내려가는데
                // 높이만 고정이면 작은 개체는 뜨고 큰 개체는 파묻힌다(육상 사냥감에서 0.09m까지 벌어졌다).
                go.transform.position = position + Vector3.up * (0.15f * sizeJitter);
                go.transform.rotation = facing;
                go.name = $"Fish_{entry.yieldItem.itemName}";
            }
            else
            {
                go.transform.localScale = new Vector3(0.45f, 0.6f, 0.45f) * sizeJitter; // 작은 동물 크기의 캡슐
                go.transform.position = position + Vector3.up * (0.6f * sizeJitter); // [B29] 위 물고기 주석과 같은 접지 보정
                go.transform.rotation = facing;
                go.name = $"Creature_{entry.yieldItem.itemName}";
            }

            // 게 색은 대왕 크랩(0.72, 0.28, 0.18 - HazardSpawner.cs:643)보다 한 단계 옅고 주황에 가깝다.
            // 같은 종류로 읽히되(같은 색 계열) 위협인지 사냥감인지는 크기와 밝기로 즉시 갈린다 -
            // 아트 기준의 "형태로 구분한다"에 맞춰 색만으로 구분하게 두지 않는다.
            Color bodyColor = isCrab
                ? new Color(0.85f, 0.42f, 0.24f)   // 소형 크랩: 밝은 주황빛 갑각
                : isFish
                    ? new Color(0.35f, 0.55f, 0.65f) // 물고기: 청회색
                    : new Color(0.55f, 0.4f, 0.25f); // 육상 동물: 갈색

            // [B29] 몸통 프리미티브를 절차 메시로 갈아 끼우고(사족보행 동물 / 방추형 물고기), 눈만
            // 파츠로 남긴다. 예전에는 육상 개체 하나가 캡슐 + 눈 2 + 머리 구체 + 다리 캡슐 4 = 8파츠였고
            // 파츠마다 머티리얼을 하나씩 새로 만들었다(특대 섬 사냥감만으로 머티리얼 약 80개).
            // 지금은 파츠 3개 · 새 머티리얼 0개다 - 메시와 머티리얼을 월드 전체가 공유한다.
            //
            // 콜라이더 크기/스케일/난수 소비는 건드리지 않는다. 메시 교체는 MeshFilter만 바꾸고,
            // 프리미티브 콜라이더는 파라메트릭이라 사냥 조준 판정 범위가 1mm도 변하지 않는다.
            // (배치 높이만 위에서 sizeJitter를 곱하도록 고쳤다 - 접지 오차 최대 0.09m 수정.)
            // 눈 좌표 검산은 CreatureVisualBuilder.BuildHuntableBody 주석 참고.
            //
            // [B30] 게는 같은 자리에서 같은 방식으로 BuildCrabBody를 부른다(프리미티브를 만들고
            // localScale/position/rotation을 정한 **직후** 1회). 두 함수 모두 콜라이더를 건드리지 않으므로
            // 사냥 조준 판정 범위는 프리미티브 그대로다.
            if (isCrab)
                CreatureVisualBuilder.BuildCrabBody(go, bodyColor, false);
            else
                CreatureVisualBuilder.BuildHuntableBody(go, bodyColor, isFish);

            var creature = go.AddComponent<HuntableCreature>();
            creature.yieldItem = entry.yieldItem;
            creature.requiredTool = entry.requiredTool;
            creature.successChance = entry.successChance;
            creature.respawnSeconds = entry.respawnSeconds;
            // [B9] 야간 사냥 보너스 전달. 난수를 소비하지 않는 순수 대입이라 rng 시퀀스와 spawnOrder에
            // 아무 영향이 없다(세이브 재현성 유지).
            creature.nightSuccessBonus = entry.nightSuccessBonus;
            creature.nightYieldBonus = entry.nightYieldBonus;
            creature.islandIndex = islandIndex;
            creature.spawnOrder = spawnOrder;
            // [세이브 키 v2] 세이브 대조용 안정 키. spawnOrder는 판별/디버깅용으로만 남는다.
            creature.stableKey = StableSpawnKey.Compute(islandIndex, stableTypeName, perTypeIndex);
            return creature;
        }

        /// <summary>
        /// [세이브 키 v2] 안정 키에 쓰는 종류 이름. 기본은 yieldItem.itemName이지만, 게(isCrab)는
        /// 물고기 항목과 yieldItem("생선")이 같아도 **다른 종류**로 세야 한다 - 같은 이름으로 묶으면
        /// 물고기 개수를 조정할 때 게의 종류 내 순번이 밀려 안정 키 설계의 목적(종류 간 독립)이 깨진다.
        /// 접미사는 세이브 파일에 해시로만 남는 내부 문자열이라 표기를 바꾸면 기존 v2 세이브의 게 키가
        /// 전부 바뀐다 - 한 번 정한 뒤 바꾸지 말 것.
        /// </summary>
        private static string GetStableTypeName(CreatureEntry entry)
        {
            string itemName = entry.yieldItem != null ? entry.yieldItem.itemName : "";
            return entry.isCrab ? itemName + "#crab" : itemName;
        }

        /// <summary>
        /// [육상 필수 재배치] 이 항목이 물속 배치가 부자연스러운 "뭍 생물"인가.
        /// 게(isCrab)와 물고기(preferShoreline)는 해안/물이 서식지라 제외 - 현재 씬 creatureEntries
        /// 3항목 기준으로는 생고기 사냥감(육상 동물 캡슐) 하나만 해당한다. 자원 쪽(IslandResourceSpawner)
        /// 처럼 이름 표가 아닌 배치 속성으로 판정하는 이유: 이 스포너는 "육상 동물이냐"를 이미
        /// 프리미티브/외형 분기(isCrab/preferShoreline)로 갖고 있어 같은 판정을 재사용하면
        /// 이름 표와 씬이 어긋날 일이 없다.
        /// </summary>
        private static bool IsLandCreature(CreatureEntry entry)
        {
            return !entry.isCrab && !entry.preferShoreline;
        }

        /// <summary>육상 생물이 물에 빠졌을 때 같은 반경으로 다시 뽑는 최대 횟수(PickBearWanderTarget의 6회 상한 선례).</summary>
        private const int LandRedrawAttempts = 6;

        /// <summary>재추첨까지 실패했을 때, 마지막 오프셋을 섬 중심 방향으로 절반씩 줄여 추가로 시도하는 횟수(rng 미소비).</summary>
        private const int LandShrinkAttempts = 2;

        /// <summary>
        /// 개체 하나의 배치 위치를 뽑는다. IslandResourceSpawner.PickScatterPosition과 같은 규칙이되,
        /// 위치 표집식은 이 스포너의 기존 식(방향 벡터 정규화 × 반경 스케일 - 해안 선호 분기 포함)을
        /// 그대로 쓴다(배치 분포를 새로 만들지 않는다).
        ///  · 뭍 생물이 수면 아래(해수면 + SpawnLandPlacement.LandMinHeightAboveSea 미만)로 떨어지면
        ///    같은 rng 스트림으로 최대 LandRedrawAttempts회 재추첨(시도당 draw 3회 = 기존 1회 표집과 동일).
        ///  · 그래도 실패하면 마지막 오프셋을 섬 중심 방향으로 절반씩 줄이며 LandShrinkAttempts회 더
        ///    시도(rng 미소비), 최종 실패 시 마지막 후보에 그대로 둔다(개체 수 불변이 위치보다 우선 -
        ///    stableKey 순번 보호).
        ///  · 게·물고기(뭍 생물이 아님)와 지형 미히트(판정 불가) 경우는 첫 표집을 그대로 쓴다(기존 동작).
        /// </summary>
        private Vector3 PickCreaturePosition(IslandInstance island, CreatureEntry entry, float radius,
            System.Random rng, float seaLevel)
        {
            bool landRequired = IsLandCreature(entry);

            Vector2 offset = SampleCreatureOffset(entry, radius, rng);
            Vector3 snapped = SpawnLandPlacement.SnapToGroundWithHit(
                island.mapPosition + new Vector3(offset.x, 0f, offset.y), out bool hitTerrain);
            if (!landRequired || SpawnLandPlacement.IsAboveWater(snapped, hitTerrain, seaLevel))
                return snapped;

            for (int attempt = 0; attempt < LandRedrawAttempts; attempt++)
            {
                offset = SampleCreatureOffset(entry, radius, rng);
                snapped = SpawnLandPlacement.SnapToGroundWithHit(
                    island.mapPosition + new Vector3(offset.x, 0f, offset.y), out hitTerrain);
                if (SpawnLandPlacement.IsAboveWater(snapped, hitTerrain, seaLevel))
                    return snapped;
            }

            for (int attempt = 0; attempt < LandShrinkAttempts; attempt++)
            {
                offset *= 0.5f;
                snapped = SpawnLandPlacement.SnapToGroundWithHit(
                    island.mapPosition + new Vector3(offset.x, 0f, offset.y), out hitTerrain);
                if (SpawnLandPlacement.IsAboveWater(snapped, hitTerrain, seaLevel))
                    return snapped;
            }

            return snapped; // 최종 실패 - 가장 안쪽 후보에 그대로 둔다(개체 수 불변).
        }

        /// <summary>
        /// 기존 배치식 그대로의 XZ 오프셋 표집. preferShoreline이면 반경의 바깥쪽 80~100% 지점에 배치해
        /// 해안에 가깝게 흉내낸다. [B30] 게는 조간대 생물이라 항상 이 해안 경로를 탄다(PrefersShoreline
        /// 참고). 두 분기 모두 rng draw를 정확히 1회 소비하고(NextFloat/NextValue01 둘 다 NextDouble
        /// 1회 - SeededRandomExtensions.cs:55·67) NextInsideUnitCircle이 2회를 더해, 호출 1회당 정확히
        /// 3 draw다.
        /// </summary>
        private static Vector2 SampleCreatureOffset(CreatureEntry entry, float radius, System.Random rng)
        {
            float radiusScale = PrefersShoreline(entry) ? rng.NextFloat(0.8f, 1f) : rng.NextValue01();
            return rng.NextInsideUnitCircle().normalized * radius * radiusScale;
        }

        /// <summary>
        /// [B30] 이 항목을 해안 가장자리(산포 반경의 80~100%)에 놓아야 하는가.
        /// 게는 조간대 생물이라 인스펙터 체크와 무관하게 항상 해안이다 - 섬 한가운데 숲에서 게가 나오면
        /// 종 자체가 거짓말이 된다(HazardSpawner.PrefersShoreline이 대왕 크랩에 대해 쓰는 것과 같은 근거).
        /// 배치 계산 자체는 물고기가 쓰던 기존 로직을 그대로 재사용하며 새로 만들지 않는다.
        /// </summary>
        private static bool PrefersShoreline(CreatureEntry entry)
        {
            return entry.preferShoreline || entry.isCrab;
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
