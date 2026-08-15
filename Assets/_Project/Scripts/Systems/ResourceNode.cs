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

        // B3-3: 이 노드를 배치한 섬 번호와, 그 섬 안에서 몇 번째로 생성됐는지(생성 순번). 절차적으로
        // 생성되는 노드라 고유한 프리팹/에셋 식별자가 없으므로, 이 두 값의 조합이 세이브 파일에서 노드
        // 하나를 다시 가리킬 수 있는 유일한 안정적인 키가 된다 - 같은 worldSeed로 재생성하면 항상 같은
        // (islandIndex, spawnOrder)에 같은 노드가 나온다는 전제가 있어야 성립하며, 이 전제는
        // IslandResourceSpawner가 섬별 결정적 System.Random을 쓰도록 바뀐 뒤에야 보장된다. B3-4(자원
        // 노드 채집 상태 저장)에서 이 값을 그대로 세이브 키로 쓴다. -1은 "아직 스포너가 설정하지 않음"을
        // 뜻하는 안전한 기본값(스포너 밖에서 수동으로 생성된 노드가 있어도 크래시하지 않도록).
        [Tooltip("이 노드를 배치한 섬 번호(IslandInstance.islandId). B3-4 세이브 키로 쓰인다.")]
        public int islandIndex = -1;

        [Tooltip("이 섬 안에서 몇 번째로 생성된 노드인지(생성 순번, 0부터). B3-4 세이브 키로 쓰인다.")]
        public int spawnOrder = -1;

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
        /// 버그 수정: 손도끼처럼 채집에도 쓰이고 전투에도 쓰이는 도구가 채집으로는 전혀 닳지 않던 문제를
        /// 고쳤다 - 요구 도구를 실제로 보유한 InventoryItem 인스턴스 하나를 찾아 내구도를 1 소모시킨다.
        /// </summary>
        public bool Harvest(PlayerInventory inventory, PlayerSkills skills)
        {
            if (!CanHarvest || inventory == null || yieldItem == null)
                return false;

            InventoryItem toolItem = null;
            if (requiresTool && requiredTool != null)
            {
                toolItem = inventory.FindItem(requiredTool);
                if (toolItem == null)
                    return false;
            }

            for (int i = 0; i < yieldPerHarvest; i++)
                inventory.AddItem(yieldItem);

            if (skills != null)
                skills.AddExperience(SkillType.Harvesting, harvestExperience);

            // 도구 내구도 소모: 무제한(IsUnlimited) 도구는 UseItem 내부에서 자동으로 소모되지 않는다.
            if (toolItem != null)
                inventory.UseItem(toolItem);

            remainingHarvestCount--;
            AudioManager.Instance?.PlayPickup(); // 채집 성공 효과음

            // B4-11: 채집이 성공한 그 순간, 노드 위치에 짧은 파티클 팝을 터뜨린다. 지금까지 채집 성공의
            // 유일한 신호가 효과음뿐이라 소리를 껐거나 여러 노드를 연달아 칠 때 "방금 게 먹혔는지"가
            // 보이지 않았다. 입자 색은 EffectBuilder가 노드 표면 색을 그대로 읽어 쓴다.
            EffectBuilder.PlayHarvestPop(gameObject);

            return true;
        }
    }
}
