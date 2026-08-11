using System.Collections.Generic;
using UnityEngine;
using MakeGame.Data;

namespace MakeGame.Player
{
    /// <summary>
    /// 플레이어의 스킬 레벨링을 관리한다 (Stranded Deep 기준: 채집/제작/요리/신체/사냥).
    /// 각 스킬은 경험치를 쌓아 레벨업하며, 레벨이 높을수록 상위 제작법 등을 사용할 수 있게 된다.
    /// </summary>
    public class PlayerSkills : MonoBehaviour
    {
        /// <summary>스킬 하나의 진행 상태(레벨, 현재 경험치).</summary>
        [System.Serializable]
        public class SkillProgress
        {
            public SkillType type;
            public int level = 1;
            public float experience = 0f;
        }

        [Tooltip("스킬별 진행 상태 목록. 다섯 개 스킬이 모두 레벨 1로 초기화되어 있어야 한다.")]
        public List<SkillProgress> skills = new List<SkillProgress>
        {
            new SkillProgress { type = SkillType.Harvesting },
            new SkillProgress { type = SkillType.Craftsmanship },
            new SkillProgress { type = SkillType.Cooking },
            new SkillProgress { type = SkillType.Physical },
            new SkillProgress { type = SkillType.Hunting },
        };

        [Tooltip("한 레벨을 올리는 데 필요한 경험치량 (단순 선형 곡선. 추후 밸런싱 예정)")]
        public float experiencePerLevel = 100f;

        [Tooltip("최대 레벨")]
        public int maxLevel = 10;

        /// <summary>
        /// 지정한 스킬의 진행 상태를 찾아 반환한다. 목록에 없으면 새로 만들어 추가한다.
        /// </summary>
        private SkillProgress GetOrCreate(SkillType type)
        {
            foreach (var skill in skills)
            {
                if (skill.type == type)
                    return skill;
            }

            var newSkill = new SkillProgress { type = type };
            skills.Add(newSkill);
            return newSkill;
        }

        /// <summary>
        /// 지정한 스킬의 현재 레벨을 반환한다.
        /// </summary>
        public int GetLevel(SkillType type)
        {
            return GetOrCreate(type).level;
        }

        /// <summary>
        /// 지정한 스킬에 경험치를 추가한다. 누적 경험치가 기준치를 넘으면 레벨업하고 남은 경험치는 이월된다.
        /// 최대 레벨에 도달하면 더 이상 경험치를 쌓지 않는다.
        /// </summary>
        public void AddExperience(SkillType type, float amount)
        {
            var skill = GetOrCreate(type);
            if (skill.level >= maxLevel)
                return;

            skill.experience += amount;

            while (skill.experience >= experiencePerLevel && skill.level < maxLevel)
            {
                skill.experience -= experiencePerLevel;
                skill.level++;
            }

            if (skill.level >= maxLevel)
                skill.experience = 0f;
        }
    }
}
