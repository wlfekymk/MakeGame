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
        /// 사냥을 시도한다. 도구가 지정되어 있으면 인벤토리에 해당 도구를 보유해야 시도할 수 있다.
        /// 시도하면 성공 여부와 관계없이 개체는 자리를 벗어나 재생 타이머가 시작된다.
        /// 성공 시 재료를 지급하고 사냥 스킬 경험치를 준다.
        /// </summary>
        public bool TryHunt(PlayerInventory inventory, PlayerSkills skills)
        {
            if (!IsAvailable || inventory == null)
                return false;

            if (requiredTool != null && inventory.GetItemCount(requiredTool) <= 0)
                return false;

            isCaught = true;
            respawnTimer = 0f;

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
