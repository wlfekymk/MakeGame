using System.Collections.Generic;
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
        /// [ui-engineer 요청 - Design_Ending.md 4장] 이번 판에 한 번이라도 제작에 성공한 제작법의 집합.
        /// 엔딩/사망 화면의 "제작한 물건 종류" 통계가 읽는 유일한 소스다.
        ///
        /// 같은 제작법을 100번 만들어도 1로 센다(개수가 아니라 "종류"). 세이브에 넣지 않는 것은 설계
        /// 의도다 - 통계를 보여주는 세 화면(배 엔딩/비행기 엔딩/사망)이 전부 한 세션 안에서 완결되고,
        /// 세이브 스키마(SaveData)를 건드리면 기존 세이브 파일과의 호환을 따져야 해서 비용이 튄다.
        /// 불러오기로 이어서 하면 이 값은 0부터 다시 센다.
        ///
        /// private + 읽기 전용 접근자로 노출하는 이유: 외부에서 Add/Clear가 가능하면 "제작했다"는
        /// 사실의 정의가 이 클래스 밖으로 새어 나간다. 넣는 곳은 TryCraft의 성공 분기 하나뿐이다.
        /// </summary>
        private readonly HashSet<CraftingRecipe> craftedRecipes = new HashSet<CraftingRecipe>();

        /// <summary>
        /// 이번 판에 제작에 성공한 제작법의 "종류" 수. 엔딩/사망 화면 통계용 읽기 전용 값이다.
        /// </summary>
        public int CraftedRecipeCount => craftedRecipes.Count;

        /// <summary>
        /// 지정한 제작법을 이번 판에 한 번이라도 만든 적이 있는지. null이면 항상 false다.
        /// (튜토리얼 힌트처럼 "아직 안 만들어 본 것"을 고를 때 쓰라고 함께 노출한다.)
        /// </summary>
        public bool HasCrafted(CraftingRecipe recipe)
        {
            return recipe != null && craftedRecipes.Contains(recipe);
        }

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

            // [ui-engineer 요청 - Design_Ending.md 4장] 성공 분기에서만 기록한다. CanCraft가 false면
            // 위에서 이미 return했으므로 "시도했지만 재료가 없었다"는 여기까지 오지 않는다.
            craftedRecipes.Add(recipe);

            AudioManager.Instance?.PlayCraftSuccess(); // 제작 성공 효과음
            return true;
        }
    }
}
