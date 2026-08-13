using UnityEngine;
using MakeGame.Player;

namespace MakeGame.Systems
{
    /// <summary>
    /// 시작 섬에 놓인 불시착한 경비행기 잔해. 상호작용 시 인벤토리에 있는 필요 재료를
    /// 자동으로 최대한 투입하고, 조건이 충족되면 수리를 완료시킨다 (BoatWorkbench와 동일한 사용 패턴).
    /// </summary>
    public class AircraftWreck : MonoBehaviour
    {
        [Tooltip("이 잔해가 진행 상태를 갱신할 경비행기 수리 시스템")]
        public AircraftRepairSystem repairSystem;

        /// <summary>
        /// 인벤토리에서 아직 부족한 재료를 확인해, 가진 만큼 최대한 자동으로 투입한다.
        /// </summary>
        public void ContributeAvailableMaterials(PlayerInventory inventory)
        {
            if (repairSystem == null || inventory == null)
                return;

            foreach (var requirement in repairSystem.requiredMaterials)
            {
                int alreadyCollected = repairSystem.GetCollectedQuantity(requirement.item);
                int stillNeeded = requirement.quantity - alreadyCollected;
                if (stillNeeded <= 0)
                    continue;

                int available = inventory.GetItemCount(requirement.item);
                int toContribute = Mathf.Min(stillNeeded, available);
                if (toContribute > 0)
                    repairSystem.ContributeMaterial(inventory, requirement.item, toContribute);
            }
        }

        /// <summary>
        /// 재료를 최대한 투입한 뒤, 조건이 충족되면 수리 완료를 시도한다.
        /// 완료되면 축하 효과음을 재생하고 true를 반환한다.
        /// </summary>
        public bool TryRepair(PlayerInventory inventory)
        {
            ContributeAvailableMaterials(inventory);

            if (repairSystem == null)
                return false;

            bool completed = repairSystem.TryCompleteRepair();
            if (completed)
                AudioManager.Instance?.PlayStageComplete(); // 수리 완료 축하 효과음 (배 단계 완료와 동일한 효과음 재사용)

            return completed;
        }
    }
}
