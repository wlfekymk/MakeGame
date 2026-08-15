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

        [Tooltip("사냥 성공 시 지급할 사냥(Hunting) 스킬 경험치")]
        public float huntExperience = 12f;

        [Tooltip("사냥 시도 성공 확률 (0~1)")]
        [Range(0f, 1f)]
        public float successChance = 0.7f;

        [Tooltip("잡히거나 도망친 뒤 다시 나타나기까지 걸리는 시간(초)")]
        public float respawnSeconds = 90f;

        // B3-3: ResourceNode/HazardSource와 동일한 목적의 안정적 식별자. CreatureSpawner가 섬별 결정적
        // System.Random을 쓰게 되어, 같은 worldSeed면 항상 같은 (islandIndex, spawnOrder)에 같은
        // 사냥감/물고기가 나온다는 전제가 성립한다.
        [Tooltip("이 개체를 배치한 섬 번호(IslandInstance.islandId).")]
        public int islandIndex = -1;

        [Tooltip("이 섬 안에서 몇 번째로 생성된 개체인지(생성 순번, 0부터).")]
        public int spawnOrder = -1;

        private bool isCaught = false;
        private float respawnTimer = 0f;

        /// <summary>현재 사냥을 시도할 수 있는 상태인지(아직 잡히지 않았는지) 여부.</summary>
        public bool IsAvailable => !isCaught;

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

            bool success = Random.value < successChance;
            if (success && yieldItem != null)
            {
                inventory.AddItem(yieldItem);
                if (skills != null)
                    skills.AddExperience(SkillType.Hunting, huntExperience);

                // 사냥/낚시 성공 피드백음. 채집(ResourceNode)과 동일하게 "아이템 획득" 효과음을 재사용해
                // 플레이어가 성공 여부를 소리로도 즉시 알 수 있게 한다 (기존에는 사운드 피드백이 전혀 없었음).
                AudioManager.Instance?.PlayPickup();
            }

            return success;
        }
    }
}
