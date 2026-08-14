using UnityEngine;
using MakeGame.Data;
using MakeGame.Player;

namespace MakeGame.Systems
{
    /// <summary>
    /// 모닥불. 발화 도구와 나뭇가지를 소모해 불을 붙이고, 불이 켜져 있는 동안 생음식을 조리할 수 있다.
    /// Stranded Deep 기준: 조리하지 않은 음식을 먹으면 식중독 위험이 있으므로, 요리(Cooking) 스킬 진행의 핵심 시설이다.
    /// </summary>
    public class Campfire : MonoBehaviour
    {
        [Tooltip("점화에 필요한 발화 도구 (파이어스타터)")]
        public ItemData fireStarterItem;

        [Tooltip("연료로 사용하는 아이템 (나뭇가지)")]
        public ItemData fuelItem;

        [Tooltip("연료 1개당 유지되는 시간(초)")]
        public float secondsPerFuel = 30f;

        [Tooltip("현재 불이 켜져 있는지 여부")]
        public bool isLit = false;

        [Tooltip("불이 꺼지기까지 남은 시간(초)")]
        public float remainingFuelSeconds = 0f;

        [Tooltip("조리 성공 시 지급할 요리(Cooking) 스킬 경험치")]
        public float cookingExperience = 8f;

        /// <summary>
        /// 매 프레임 자동으로 남은 연료 시간을 줄인다.
        /// </summary>
        private void Update()
        {
            Tick(Time.deltaTime);
        }

        /// <summary>
        /// 남은 연료 시간을 줄이고, 다 떨어지면 불을 끈다.
        /// </summary>
        public void Tick(float deltaTime)
        {
            if (!isLit)
                return;

            remainingFuelSeconds -= deltaTime;
            if (remainingFuelSeconds <= 0f)
            {
                remainingFuelSeconds = 0f;
                isLit = false;
            }
        }

        /// <summary>
        /// 인벤토리에서 발화 도구 보유 여부를 확인하고 연료 1개를 소모하여 모닥불을 켠다.
        /// 이미 켜져 있으면 연료를 추가로 소모해 유지 시간을 늘린다 (장작 추가).
        /// </summary>
        public bool TryLight(PlayerInventory inventory)
        {
            if (inventory == null)
                return false;

            if (fireStarterItem != null && inventory.GetItemCount(fireStarterItem) <= 0)
                return false;

            if (fuelItem != null)
            {
                if (inventory.GetItemCount(fuelItem) <= 0)
                    return false;
                inventory.RemoveItems(fuelItem, 1);
            }

            isLit = true;
            remainingFuelSeconds += secondsPerFuel;

            // 점화 성공 피드백음. 전용 효과음이 없어 제작 성공음을 재사용한다
            // (ConsumptionSystem의 치료 성공 피드백과 동일한 재사용 패턴).
            AudioManager.Instance?.PlayCraftSuccess();

            return true;
        }

        /// <summary>
        /// 생음식 하나를 익힌 음식으로 조리한다. 불이 켜져 있어야 하고, 재료가 생음식(isRawFood)이어야 한다.
        /// 성공 시 인벤토리의 생음식 1개를 조리 결과 아이템으로 바꾸고 요리 스킬 경험치를 준다.
        /// </summary>
        public bool CookItem(PlayerInventory inventory, PlayerSkills skills, ItemData rawFood)
        {
            if (!isLit || inventory == null || rawFood == null)
                return false;

            if (!rawFood.isRawFood || rawFood.cookedResult == null)
                return false;

            if (!inventory.RemoveItems(rawFood, 1))
                return false;

            inventory.AddItem(rawFood.cookedResult);

            if (skills != null)
                skills.AddExperience(SkillType.Cooking, cookingExperience);

            return true;
        }
    }
}
