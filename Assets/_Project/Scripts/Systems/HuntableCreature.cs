using UnityEngine;
using MakeGame.Data;
using MakeGame.Player;

namespace MakeGame.Systems
{
    /// <summary>
    /// 사냥/낚시로 잡을 수 있는 야생 동물이나 물고기 하나를 나타낸다.
    /// 창을 보유한 상태로 상호작용하면 확률적으로 생고기/생선을 획득하고 사냥(Hunting) 스킬 경험치를 얻는다.
    /// 잡히거나 도망친 뒤에는 일정 시간이 지나야 다시 등장한다.
    /// </summary>
    public class HuntableCreature : MonoBehaviour
    {
        [Tooltip("사냥 성공 시 얻는 아이템 (생고기, 생선 등)")]
        public ItemData yieldItem;

        [Tooltip("사냥에 필요한 도구 (창). 비워두면 도구 없이도 시도할 수 있다.")]
        public ItemData requiredTool;

        // [B9] 12 → 20. Docs/Design_MidGameContent.md 4장 "안 1(a) 파우셋 유량 재조정"의 지정값이다.
        // 씬/프리팹 어디에도 HuntableCreature 인스턴스가 없고(런타임에 CreatureSpawner가 만든다)
        // CreatureSpawner.CreatureEntry에도 대응 필드가 없으므로, 이 코드 기본값이 곧 실동작값이다
        // (AGENT_BRIEF 0장의 "씬 값이 이긴다"가 적용되지 않는 경우 — 확인 완료).
        // 근거: 씬 experiencePerLevel 100 기준 20이면 사냥 5회 = 1레벨. 채집(ResourceNode)과의 XP
        // 유량 격차를 좁혀, 레벨이 빨리 오르는 쪽이 "밖에 나가 사냥하는 쪽"이 되게 한다.
        [Tooltip("사냥 성공 시 지급할 사냥(Hunting) 스킬 경험치")]
        public float huntExperience = 20f;

        [Tooltip("사냥 시도 성공 확률 (0~1)")]
        [Range(0f, 1f)]
        public float successChance = 0.7f;

        [Tooltip("잡히거나 도망친 뒤 다시 나타나기까지 걸리는 시간(초)")]
        public float respawnSeconds = 90f;

        // ── [B9] 야간 사냥 (Docs/Design_MidGameContent.md 4장 "안 2. 밤을 판다") ────────────
        // 왜: 같은 문서 0장 계산대로 이 게임의 길이는 74.5분~147분 사이에서 플레이어가 고르는 값인데,
        // 밤에 살 수 있는 것이 하나도 없어서 전원이 취침(=74.5분)을 고른다. 밤 5분에는 이미
        // "총 플레이 +5분"이라는 가격표가 붙어 있고 진열대만 비어 있었다.
        // 무엇: 사냥감을 야행성으로 만든다. 밤에는 성공률 +0.2, 수확량 +1.
        // 그래서: 취침(실시간 0초 + HP + 안전) vs 야간 사냥(실시간 5분 + 식량 2배 + 사냥/요리 XP + 위험)이
        // 처음으로 정면 경쟁하는 선택지가 된다.
        // 값의 출처: 두 필드 모두 문서 지정값이며 CreatureSpawner.CreatureEntry에 같은 이름의 필드를
        // 추가해 종류별로 덮어쓸 수 있게 했다. 씬 creatureEntries에는 아직 이 키가 없으므로
        // 엔트리 쪽 코드 기본값(0.2 / 1)이 그대로 전달된다 — 디렉터 조치 없이도 동작한다.
        [Tooltip("밤(SurvivalClock.IsDaytime == false)에 사냥 성공 확률에 더할 보너스. 0이면 밤낮 차이 없음")]
        public float nightSuccessBonus = 0.2f;

        [Tooltip("밤에 사냥 성공 시 추가로 더 주는 수확 개수. 0이면 밤낮 차이 없음")]
        public int nightYieldBonus = 1;

        // Docs/Design_MidGameContent.md 4장 "안 1(b) 레벨 페이로드" — 사냥 레벨이 성공률로 되돌아온다.
        // Lv10에서 +0.27이라 물고기(0.6)는 0.87, 육상(0.7)은 0.97이 되고, 밤 보너스와 겹치면
        // Clamp01에 걸린다. 상한을 넘겨도 확률이 깨지지 않도록 TryHunt에서 반드시 클램프한다.
        [Tooltip("사냥 스킬 레벨 1당(Lv1 초과분) 성공 확률에 더할 값")]
        public float huntingLevelSuccessBonus = 0.03f;

        // B3-3: ResourceNode/HazardSource와 동일한 목적의 안정적 식별자. CreatureSpawner가 섬별 결정적
        // System.Random을 쓰게 되어, 같은 worldSeed면 항상 같은 (islandIndex, spawnOrder)에 같은
        // 사냥감/물고기가 나온다는 전제가 성립한다.
        [Tooltip("이 개체를 배치한 섬 번호(IslandInstance.islandId).")]
        public int islandIndex = -1;

        [Tooltip("이 섬 안에서 몇 번째로 생성된 개체인지(생성 순번, 0부터).")]
        public int spawnOrder = -1;

        private bool isCaught = false;
        private float respawnTimer = 0f;
        private bool headBuilt = false;

        // [B9] 밤/낮 판정용 SurvivalClock 캐시. 개체가 섬당 4~10마리라 매 사냥 시도마다
        // FindAnyObjectByType을 도는 것은 피하고 싶지만, 시계가 없는 테스트 씬도 성립해야 하므로
        // "찾아봤는지" 플래그를 따로 둬서 null 결과도 캐시한다(매번 재탐색 방지).
        private SurvivalClock cachedClock;
        private bool clockLookupDone = false;

        /// <summary>현재 사냥을 시도할 수 있는 상태인지(아직 잡히지 않았는지) 여부.</summary>
        public bool IsAvailable => !isCaught;

        /// <summary>
        /// 스포너가 몸통/다리를 만든 뒤 실행되는 시각 보강 단계(게임플레이 값은 건드리지 않는다).
        /// </summary>
        private void Start()
        {
            BuildHeadSilhouette();
        }

        /// <summary>
        /// [B29 이후로는 사실상 실행되지 않는 하위 호환 경로다 - 지우지 말고 조건을 그대로 둘 것]
        ///
        /// 원래 목적: 육상 사냥감 몸통 앞위쪽에 머리 돌기 하나를 붙여 "어느 쪽이 앞인지"를 드러낸다.
        /// 지금은 CreatureSpawner가 몸통 프리미티브의 메시를 사족보행 동물 절차 메시로 갈아 끼우고
        /// (CreatureVisualBuilder.BuildHuntableBody), 머리·목·주둥이·귀가 전부 그 메시 안에 있다.
        ///
        /// 그래서 아래 `sharedMesh.name.StartsWith("Capsule")` 검사가 자연스럽게 false가 되어
        /// 이 메서드는 아무 것도 하지 않는다(절차 메시 이름은 "Cre_HuntLandBody"다). 이 검사는
        /// 스포너를 거치지 않고 수동으로 캡슐을 만들어 이 컴포넌트를 붙인 경우를 위한 안전망으로만 남는다.
        /// 검사를 지우면 새 몸통 위에 머리 구체가 하나 더 얹혀 코 옆에 혹이 생긴다 - 자원 노드 쪽에서
        /// 같은 유형의 함정을 이미 한 번 밟았다(ResourceNode.RootTopLocalY의 B28 주석 참고).
        /// </summary>
        private void BuildHeadSilhouette()
        {
            if (headBuilt)
                return;
            headBuilt = true;

            var meshFilter = GetComponent<MeshFilter>();
            if (meshFilter == null || meshFilter.sharedMesh == null || !meshFilter.sharedMesh.name.StartsWith("Capsule"))
                return; // 육상 사냥감(캡슐 몸통)만 대상

            var bodyRenderer = GetComponent<MeshRenderer>();
            Color bodyColor = bodyRenderer != null && bodyRenderer.sharedMaterial != null
                ? bodyRenderer.sharedMaterial.color
                : new Color(0.55f, 0.4f, 0.25f);

            CreatureVisualBuilder.AddCompensatedSphere(transform, new Vector3(0f, 0.72f, 0.42f), 0.14f,
                transform.localScale, bodyColor * 0.85f, "Head");
        }

        /// <summary>
        /// 매 프레임 자동으로 재생 타이머를 진행시킨다.
        /// </summary>
        private void Update()
        {
            Tick(Time.deltaTime);
        }

        /// <summary>
        /// 잡힌 개체를 시간 경과에 따라 다시 등장시킨다.
        /// </summary>
        public void Tick(float deltaTime)
        {
            if (!isCaught)
                return;

            respawnTimer += deltaTime;
            if (respawnTimer >= respawnSeconds)
            {
                isCaught = false;
                respawnTimer = 0f;
            }
        }

        /// <summary>
        /// B3-5: 세이브 파일에서 읽어온 포획 상태를 그대로 되돌린다. TryHunt와 달리 인벤토리/스킬을
        /// 전혀 거치지 않고 isCaught만 직접 맞춘다. 재등장까지 남은 시간은 저장하지 않으므로
        /// (SaveData.caughtCreatures 주석 참고) respawnTimer는 항상 0부터 다시 시작한다 - 오프라인 경과
        /// 시간은 반영하지 않는다(SaveLoadController.RestoreHazardsAndCreatures 주석 참고).
        /// </summary>
        public void RestoreCaughtState(bool caught)
        {
            isCaught = caught;
            respawnTimer = 0f;
        }

        /// <summary>
        /// 사냥을 시도한다. 도구가 지정되어 있으면 인벤토리에 해당 도구를 보유해야 시도할 수 있다.
        /// 시도하면 성공 여부와 관계없이 개체는 자리를 벗어나 재생 타이머가 시작된다.
        /// 성공 시 재료를 지급하고 사냥 스킬 경험치를 준다.
        /// 버그 수정: 창(15회 사용)도 전투/채집과 같은 도구 내구도 소모 대상인데 사냥 시도에서는
        /// 전혀 소모되지 않던 문제를 고쳤다 - 성공 여부와 무관하게 던지는(찌르는) 시도 자체로
        /// 내구도가 1 닳는다 (실제로 휘두른 것은 성공/실패와 무관하기 때문).
        /// </summary>
        public bool TryHunt(PlayerInventory inventory, PlayerSkills skills)
        {
            if (!IsAvailable || inventory == null)
                return false;

            InventoryItem toolItem = null;
            if (requiredTool != null)
            {
                toolItem = inventory.FindItem(requiredTool);
                if (toolItem == null)
                    return false;
            }

            isCaught = true;
            respawnTimer = 0f;

            if (toolItem != null)
                inventory.UseItem(toolItem); // 시도 자체로 도구 내구도 소모 (성공 여부와 무관)

            // [B9] 성공 확률 = 기본값 + 사냥 레벨 보너스 + 야간 보너스. 세 항을 더한 뒤 반드시 클램프한다
            // (Lv10 + 밤이면 육상 사냥감이 0.7 + 0.27 + 0.2 = 1.17로 1을 넘는다).
            bool isNight = IsNightNow();
            float chance = successChance;
            if (skills != null)
                chance += huntingLevelSuccessBonus * (skills.GetLevel(SkillType.Hunting) - 1);
            if (isNight)
                chance += nightSuccessBonus;
            chance = Mathf.Clamp01(chance);

            bool success = Random.value < chance;
            if (success && yieldItem != null)
            {
                // 야간 수확량 보너스. 인벤토리 AddItem은 1개씩 넣는 API라 개수만큼 반복한다
                // (스택 용량/무게 규칙을 우회하지 않기 위해 일부러 루프로 같은 경로를 통과시킨다).
                int yieldCount = 1;
                if (isNight && nightYieldBonus > 0)
                    yieldCount += nightYieldBonus;

                // [B18] 용량 도입 후 AddItem이 거부될 수 있다. 거부되면 사냥감은 이미 죽었는데
                // 고기가 사라진다 - 리스폰까지 기다려야 하므로 채집 실패보다 손해가 크다.
                // 들어간 만큼만 세고, 하나도 못 넣었으면 그 사실을 남긴다.
                int taken = 0;
                for (int i = 0; i < yieldCount; i++)
                {
                    if (!inventory.TryAddItem(yieldItem))
                        break;

                    taken++;
                }

                if (taken < yieldCount)
                    Debug.Log($"[HuntableCreature] 가방이 가득 차 {yieldCount - taken}개를 못 챙겼다");

                if (skills != null)
                    skills.AddExperience(SkillType.Hunting, huntExperience);

                // 사냥/낚시 성공 피드백음. 채집(ResourceNode)과 동일하게 "아이템 획득" 효과음을 재사용해
                // 플레이어가 성공 여부를 소리로도 즉시 알 수 있게 한다 (기존에는 사운드 피드백이 전혀 없었음).
                AudioManager.Instance?.PlayPickup();
            }

            return success;
        }

        /// <summary>
        /// [B9] 지금이 밤인지 판정한다. SurvivalClock.IsDaytime(TimeOfDay01 0.25~0.75가 낮)의 반대다.
        /// 시계가 없는 씬에서는 항상 낮으로 취급해 야간 보너스가 조용히 꺼진다
        /// (SurvivalTickDriver가 clock == null을 "항상 낮"으로 다루는 것과 같은 규칙).
        /// </summary>
        private bool IsNightNow()
        {
            if (!clockLookupDone)
            {
                cachedClock = FindAnyObjectByType<SurvivalClock>();
                clockLookupDone = true;
            }

            return cachedClock != null && !cachedClock.IsDaytime;
        }
    }
}
