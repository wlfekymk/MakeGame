using System;
using System.Collections.Generic;
using UnityEngine;

namespace MakeGame.Data
{
    /// <summary>
    /// 제작법 하나를 정의하는 ScriptableObject (Stranded Deep 기준: 도구/쉼터/뗏목 등 제작 시스템의 핵심).
    /// 필요한 재료, 필요 스킬/레벨, 결과물을 담는다.
    /// </summary>
    [CreateAssetMenu(fileName = "NewCraftingRecipe", menuName = "MakeGame/Crafting Recipe", order = 2)]
    public class CraftingRecipe : ScriptableObject
    {
        [Serializable]
        public class MaterialRequirement
        {
            public ItemData item;
            [Min(1)]
            public int quantity = 1;
        }

        [Tooltip("제작법 이름 (예: 쉼터, 물 증류기, 뗏목 부품 등)")]
        public string recipeName;

        [TextArea]
        [Tooltip("제작법 설명")]
        public string description;

        [Tooltip("제작에 필요한 재료와 각 재료의 필요 개수")]
        public List<MaterialRequirement> requiredMaterials = new List<MaterialRequirement>();

        [Tooltip("제작 결과로 얻는 아이템")]
        public ItemData resultItem;

        [Tooltip("제작 결과 아이템의 개수")]
        public int resultQuantity = 1;

        [Tooltip("이 제작법을 사용하기 위해 필요한 스킬 종류")]
        public SkillType requiredSkill = SkillType.Craftsmanship;

        [Tooltip("이 제작법을 사용하기 위해 필요한 최소 스킬 레벨")]
        public int requiredSkillLevel = 1;

        [Tooltip("제작 성공 시 requiredSkill에 지급할 경험치")]
        public float experienceReward = 10f;
    }
}
