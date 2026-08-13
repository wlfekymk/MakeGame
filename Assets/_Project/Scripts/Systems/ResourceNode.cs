using UnityEngine;
using MakeGame.Data;
using MakeGame.Player;

namespace MakeGame.Systems
{
    /// <summary>
    /// 섬에 배치되는 채집 가능한 자원 하나(나무, 바위, 덤불 등)를 나타낸다.
    /// 상호작용 시 지정된 재료 아이템을 인벤토리에 지급하고 채집 스킬 경험치를 준다.
    /// 채집 가능 횟수가 모두 소진되면 일정 시간 후 다시 채집 가능한 상태로 재생된다.
    /// </summary>
    public class ResourceNode : MonoBehaviour
    {
        [Tooltip("이 노드를 채집했을 때 얻는 재료 아이템")]
        public ItemData yieldItem;

        [Tooltip("1회 채집 시 얻는 재료 개수")]
        public int yieldPerHarvest = 1;

        [Tooltip("재생되기 전까지 채집 가능한 총 횟수")]
        public int maxHarvestCount = 3;

        [Tooltip("현재 남은 채집 가능 횟수")]
        public int remainingHarvestCount = 3;

        [Tooltip("채집 시 지급할 채집(Harvesting) 스킬 경험치")]
        public float harvestExperience = 5f;

        [Tooltip("모두 소진된 뒤 다시 채집 가능해지기까지 걸리는 시간(초)")]
        public float respawnSeconds = 60f;

        [Tooltip("채집에 도구가 필요한지 여부. true면 requiredTool을 인벤토리에 보유해야 채집할 수 있다.")]
        public bool requiresTool = false;

        [Tooltip("채집에 필요한 도구 아이템 (requiresTool이 true일 때만 사용, 예: 손도끼)")]
        public ItemData requiredTool;

        private float respawnTimer = 0f;

        /// <summary>현재 채집이 가능한 상태인지 여부(남은 횟수가 있는지).</summary>
        public bool CanHarvest => remainingHarvestCount > 0;

        /// <summary>
        /// 매 프레임 자동으로 재생 타이머를 진행시킨다.
        /// </summary>
        private void Update()
        {
            Tick(Time.deltaTime);
        }

        /// <summary>
        /// 소진된 노드를 시간 경과에 따라 재생시킨다.
        /// </summary>
        public void Tick(float deltaTime)
        {
            if (CanHarvest)
                return;

            respawnTimer += deltaTime;
            if (respawnTimer >= respawnSeconds)
            {
                remainingHarvestCount = maxHarvestCount;
                respawnTimer = 0f;
            }
        }

        /// <summary>
        /// 이 노드를 채집한다. 도구가 필요한 경우 인벤토리에 도구가 있는지 먼저 확인한다.
        /// 성공 시 인벤토리에 재료를 지급하고 채집 스킬 경험치를 주며, 남은 횟수를 1 줄인다.
        /// </summary>
        public bool Harvest(PlayerInventory inventory, PlayerSkills skills)
        {
            if (!CanHarvest || inventory == null || yieldItem == null)
                return false;

            if (requiresTool && requiredTool != null && inventory.GetItemCount(requiredTool) <= 0)
                return false;

            for (int i = 0; i < yieldPerHarvest; i++)
                inventory.AddItem(yieldItem);

            if (skills != null)
                skills.AddExperience(SkillType.Harvesting, harvestExperience);

            remainingHarvestCount--;
            AudioManager.Instance?.PlayPickup(); // 채집 성공 효과음
            return true;
        }
    }
}
