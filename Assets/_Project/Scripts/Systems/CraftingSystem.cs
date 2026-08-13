using UnityEngine;
using MakeGame.Data;
using MakeGame.Player;

namespace MakeGame.Systems
{
    /// <summary>
    /// 제작(크래프팅) 시스템. 재료 확인, 재료 소모, 결과물 지급, 스킬 경험치 지급을 처리한다.
    /// Stranded Deep처럼 쉼터/도구/뗏목 등 대부분의 진행이 이 제작 시스템을 통해 이루어진다.
    /// </summary>
    public class CraftingSystem : MonoBehaviour
    {
        [Tooltip("제작 재료를 확인/소모할 대상 인벤토리")]
        public PlayerInventory inventory;

        [Tooltip("제작 성공 시 경험치를 지급할 대상 스킬 목록")]
        public PlayerSkills skills;

        /// <summary>
        /// 지정한 제작법을 지금 제작할 수 있는지 확인한다.
        /// 필요 스킬 레벨을 만족하고, 필요한 재료를 모두 충분히 보유하고 있어야 한다.
        /// </summary>
        public bool CanCraft(CraftingRecipe recipe)
        {
            if (recipe == null || inventory == null)
                return false;

            if (skills != null && skills.GetLevel(recipe.requiredSkill) < recipe.requiredSkillLevel)
                return false;

            foreach (var requirement in recipe.requiredMaterials)
            {
                if (inventory.GetItemCount(requirement.item) < requirement.quantity)
                    return false;
            }

            return true;
        }

        /// <summary>
        /// 제작을 시도한다. 제작 가능하면 재료를 소모하고 결과물을 지급하며, 관련 스킬에 경험치를 준다.
        /// 조건을 만족하지 못하면 아무 변화 없이 false를 반환한다.
        /// </summary>
        public bool TryCraft(CraftingRecipe recipe)
        {
            if (!CanCraft(recipe))
                return false;

            foreach (var requirement in recipe.requiredMaterials)
            {
                inventory.RemoveItems(requirement.item, requirement.quantity);
            }

            for (int i = 0; i < recipe.resultQuantity; i++)
            {
                inventory.AddItem(recipe.resultItem);
            }

            if (skills != null)
                skills.AddExperience(recipe.requiredSkill, recipe.experienceReward);

            AudioManager.Instance?.PlayCraftSuccess(); // 제작 성공 효과음
            return true;
        }
    }
}
