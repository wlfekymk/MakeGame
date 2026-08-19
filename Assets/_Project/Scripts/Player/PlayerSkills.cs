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

        // ── 스킬 효과 수치 (단일 소스) ────────────────────────────────────────────
        //
        // 다섯 스킬 모두 경험치가 들어오는 경로는 있었지만(채집=ResourceNode, 제작=CraftingSystem,
        // 요리=Campfire, 신체=HazardSource 격퇴, 사냥=HuntableCreature), 레벨을 실제로 **쓰는** 곳은
        // 제작(레시피 레벨 제한)과 사냥(성공 확률 가산) 둘뿐이었다. 나머지 셋은 숫자만 오르고 아무 일도
        // 일어나지 않는 죽은 스킬이었다.
        //
        // 수치를 이 클래스에 모으는 이유: 효과를 쓰는 쪽(ResourceNode·Campfire·PlayerController…)이
        // 파일마다 자기 상수를 들고 있으면 밸런스 조정이 다섯 군데 수정이 되고, 어디 하나가 빠지면
        // "스킬 설명과 실제 효과가 다르다"가 된다. 계산은 여기서, 적용만 각자 파일에서 한다.
        //
        // 배율의 기준은 **레벨 1이 1.0**이다(레벨 1에서 공짜 버프가 붙지 않는다). 예외는 채집 확률
        // 하나인데, 이쪽은 배율이 아니라 0에서 시작하는 확률이라 Lv1에 4%가 붙어도 기존 수확량이
        // 줄지 않는다(더 나오기만 한다).

        /// <summary>채집 레벨 1당 늘어나는 "추가 수확 1개" 확률. Lv1 = 4%, Lv10 = 40%.</summary>
        public const float HarvestingBonusYieldChancePerLevel = 0.04f;

        /// <summary>요리 레벨 1당 늘어나는 조리 음식 회복량 배율. Lv10에서 +27%(1.27배).</summary>
        public const float CookingRestoreBonusPerLevel = 0.03f;

        /// <summary>신체 레벨 1당 늘어나는 이동 속도 배율. Lv10에서 +9%(1.09배).</summary>
        public const float PhysicalMoveSpeedBonusPerLevel = 0.01f;

        /// <summary>신체 레벨 1당 줄어드는 산소 소모 비율. Lv10에서 -18%(0.82배).</summary>
        public const float PhysicalOxygenDrainReductionPerLevel = 0.02f;

        /// <summary>
        /// [채집 스킬 효과] 채집 1회당 재료 1개가 더 나올 확률(0~1). 실제 주사위는 ResourceNode.Harvest가
        /// 굴린다 - 이 메서드는 확률만 알려주고 rng를 쓰지 않으므로 UI가 매 프레임 물어봐도 안전하다.
        /// </summary>
        public float GetHarvestingBonusYieldChance()
        {
            int level = GetLevel(SkillType.Harvesting);
            if (level <= 0)
                return 0f;

            return Mathf.Clamp01(level * HarvestingBonusYieldChancePerLevel);
        }

        /// <summary>
        /// [요리 스킬 효과] 조리된 음식이 회복시키는 양의 배율(레벨 1 = 1.0).
        ///
        /// **0.2.51에서 배선됨** — ConsumptionSystem.Consume이 익힌 음식(isRawFood=false)에만 곱한다. 참고로 원래 후보였던 두 지점:
        ///  · Campfire.CookItem - 조리 결과를 ItemData.cookedResult 에셋 그대로 넣기만 한다. 회복량은
        ///    에셋에 박힌 값이라 조리 시점에 배율을 곱할 대상 자체가 없다(개당 회복량을 저장할 필드가
        ///    InventoryItem에 없다).
        ///  · ConsumptionSystem.Consume - survivalStats.ConsumeFood(item.data.hungerRestoreAmount)로
        ///    회복시킨다. **배선하려면 여기가 맞다**(섭취 시 보너스). 그 한 줄을
        ///    ConsumeFood(hungerRestoreAmount * skills.GetCookingRestoreMultiplier())로 바꾸고,
        ///    isRawFood가 아닌(=조리된) 음식에만 곱하면 된다. ConsumptionSystem에는 PlayerSkills 참조가
        ///    아직 없어 필드 하나(+ 씬 배선 또는 FindAnyObjectByType 폴백)가 함께 필요하다.
        /// </summary>
        public float GetCookingRestoreMultiplier()
        {
            int level = GetLevel(SkillType.Cooking);
            return 1f + Mathf.Max(0, level - 1) * CookingRestoreBonusPerLevel;
        }

        /// <summary>
        /// [신체 스킬 효과] 지상 이동 속도 배율(레벨 1 = 1.0).
        ///
        /// **0.2.51에서 배선됨** — PlayerController.HandleMove의 `float speed = moveSpeed;`
        /// 가 이 작업의 락 밖이다. 배선은 그 한 줄을
        /// `moveSpeed * (playerSkills != null ? playerSkills.GetPhysicalMoveSpeedMultiplier() : 1f)`
        /// 로 바꾸면 끝난다(PlayerController는 playerSkills 참조를 이미 갖고 있다).
        /// 달리기 배율은 그 뒤에 곱해지므로 자동으로 함께 오른다.
        /// </summary>
        public float GetPhysicalMoveSpeedMultiplier()
        {
            int level = GetLevel(SkillType.Physical);
            return 1f + Mathf.Max(0, level - 1) * PhysicalMoveSpeedBonusPerLevel;
        }

        /// <summary>
        /// [신체 스킬 효과] 잠수 중 산소 소모 배율(레벨 1 = 1.0, 낮을수록 오래 버틴다).
        /// 0.5 미만으로는 내려가지 않게 잠갔다 - 산소 압박이 사라지면 수중 동굴 콘텐츠의 긴장이 통째로 죽는다.
        ///
        /// **0.2.51에서 배선됨** — PlayerController.PushOxygenDrainMultiplier가 산소통 배율에 곱해 민다. SurvivalStats.oxygenDrainMultiplier가 적용 지점이지만 그 필드의
        /// 단일 소스는 PlayerController.oxygenTankDrainMultiplier라고 SurvivalStats 주석이 못박고 있고,
        /// 두 파일 모두 이 작업의 락 밖이다. 배선한다면 산소통 배율에 이 값을 **곱해서** 넣어야 한다
        /// (덮어쓰면 산소통 효과가 사라진다).
        /// </summary>
        public float GetPhysicalOxygenDrainMultiplier()
        {
            int level = GetLevel(SkillType.Physical);
            return Mathf.Max(0.5f, 1f - Mathf.Max(0, level - 1) * PhysicalOxygenDrainReductionPerLevel);
        }
    }
}
