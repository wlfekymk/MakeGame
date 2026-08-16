using UnityEngine;
using MakeGame.Player;

namespace MakeGame.Systems
{
    /// <summary>
    /// 배를 실제로 조립하는 작업대.
    /// 상호작용 시 인벤토리에 있는 현재 단계 필요 재료를 자동으로 최대한 투입하고,
    /// 조건이 충족되면 다음 단계로 진행(혹은 최종 3단계면 배 100% 완성)시킨다.
    /// </summary>
    public class BoatWorkbench : MonoBehaviour
    {
        [Tooltip("이 작업대가 진행 상태를 갱신할 배 제작 시스템")]
        public BoatConstructionSystem boatConstruction;

        /// <summary>
        /// 인벤토리에서 현재 단계에 아직 부족한 재료를 확인해, 가진 만큼 최대한 자동으로 투입한다.
        /// </summary>
        public void ContributeAvailableMaterials(PlayerInventory inventory)
        {
            if (boatConstruction == null || inventory == null)
                return;

            foreach (var requirement in boatConstruction.GetCurrentStageRequirements())
            {
                int alreadyCollected = boatConstruction.GetCollectedQuantity(requirement.item);
                int stillNeeded = requirement.quantity - alreadyCollected;
                if (stillNeeded <= 0)
                    continue;

                int available = inventory.GetItemCount(requirement.item);
                int toContribute = Mathf.Min(stillNeeded, available);
                if (toContribute > 0)
                    boatConstruction.ContributeMaterial(inventory, requirement.item, toContribute);
            }
        }

        /// <summary>
        /// 재료를 최대한 투입한 뒤, 조건이 충족되면 다음 단계로 진행(또는 최종 완성)을 시도한다.
        /// 최종 3단계에서 조건을 만족하면 배가 100% 완성된 것이므로 true를 반환한다.
        /// </summary>
        public bool TryBuild(PlayerInventory inventory)
        {
            if (boatConstruction == null)
                return false;

            // [발견 후 수정] 이미 100% 완성된 배에 다시 E를 누르면 TryAdvanceStage가 매번 성공 처리되어
            // 단계 완료 효과음(PlayStageComplete)이 눌릴 때마다 다시 울렸다. 완성 이후에는 투입할 재료도
            // 남은 단계도 없으므로 여기서 끊는다. 엔딩 판정은 EndingChecker가 CanAdvanceStage로 따로
            // 하므로(EndingChecker.cs:373) 이 가드가 엔딩을 막지 않는다.
            if (boatConstruction.isFullyComplete)
                return false;

            ContributeAvailableMaterials(inventory);
            return boatConstruction.TryAdvanceStage();
        }
    }
}
