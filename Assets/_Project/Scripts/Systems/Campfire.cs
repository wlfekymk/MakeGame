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
        [Tooltip("점화에 필요한 발화 도구 (파이어스타터, 무제한 사용 가능)")]
        public ItemData fireStarterItem;

        [Tooltip("대체 발화 도구 (라이터, 유한 사용 - 시작 아이템으로 지급됨). 버그 수정: 플레이어가 " +
            "불시착 직후 라이터를 들고 시작하는데도 점화 판정이 파이어스타터만 확인해 실제로는 전혀 쓰이지 " +
            "않는 죽은 시작 아이템이었다. 파이어스타터가 없으면 이 아이템으로도 점화할 수 있게 한다.")]
        public ItemData alternateFireStarterItem;

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
        /// 버그 수정: 파이어스타터(무제한)가 없으면 라이터(유한, 시작 아이템)로도 점화할 수 있다 -
        /// 파이어스타터를 우선 사용해 유한 자원인 라이터를 최대한 아낀다. 실제로 소모되는 쪽만
        /// PlayerInventory.UseItem으로 내구도를 1 줄인다(파이어스타터는 무제한이라 사실상 그대로 유지).
        /// </summary>
        public bool TryLight(PlayerInventory inventory)
        {
            if (inventory == null)
                return false;

            InventoryItem starterItem = FindAvailableFireStarter(inventory);
            if (fireStarterItem != null || alternateFireStarterItem != null)
            {
                if (starterItem == null)
                    return false;
            }

            if (fuelItem != null)
            {
                if (inventory.GetItemCount(fuelItem) <= 0)
                    return false;
                inventory.RemoveItems(fuelItem, 1);
            }

            if (starterItem != null)
                inventory.UseItem(starterItem);

            isLit = true;
            remainingFuelSeconds += secondsPerFuel;

            // 연결(B-3): tech-artist가 AudioManager에 만들어 둔 전용 모닥불 점화 성공음으로 교체.
            // 예전에는 PlayCraftSuccess()를 재사용해 "제작"과 "점화"가 같은 소리로 구분이 안 됐다.
            AudioManager.Instance?.PlayCampfireLit();

            return true;
        }

        /// <summary>
        /// 보유한 발화 도구 중 실제로 사용할 인스턴스 하나를 찾는다. 무제한 도구(파이어스타터)를
        /// 우선하고, 없으면 유한 도구(라이터)를 사용한다. 어느 쪽도 지정/보유하지 않았으면 null.
        /// </summary>
        private InventoryItem FindAvailableFireStarter(PlayerInventory inventory)
        {
            if (fireStarterItem != null)
            {
                var item = inventory.FindItem(fireStarterItem);
                if (item != null)
                    return item;
            }

            if (alternateFireStarterItem != null)
            {
                var item = inventory.FindItem(alternateFireStarterItem);
                if (item != null)
                    return item;
            }

            return null;
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
