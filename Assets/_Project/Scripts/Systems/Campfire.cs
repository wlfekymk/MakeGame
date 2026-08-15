using UnityEngine;
using MakeGame.Data;
using MakeGame.Player;

namespace MakeGame.Systems
{
    /// <summary>
    /// 모닥불. 발화 도구와 나뭇가지를 소모해 불을 붙이고, 불이 켜져 있는 동안 생음식을 조리할 수 있다.
    /// Stranded Deep 기준: 조리하지 않은 음식을 먹으면 식중독 위험이 있으므로, 요리(Cooking) 스킬 진행의 핵심 시설이다.
    ///
    /// B4-1 (Spec_15 3단계 배선): SurvivalBalanceConfig를 선택적(nullable) 참조로 받는다.
    /// 폴백으로 읽는 config 필드 — secondsPerFuel ← campfireSecondsPerFuel,
    /// cookingExperience ← campfireCookingExperience.
    /// 폴백은 해당 필드가 0 이하(미설정)일 때만 적용되므로 씬/프리팹 직렬화 값이 항상 이긴다
    /// (SurvivalStats.ApplyBalanceConfigFallback과 동일한 규칙).
    /// </summary>
    public class Campfire : MonoBehaviour
    {
        [Header("밸런스 config (선택, B4-1)")]
        [Tooltip("연결하면, 아래 secondsPerFuel/cookingExperience가 0 이하로(미설정) 남아있는 경우에" +
            " 한해 config의 campfireSecondsPerFuel/campfireCookingExperience 값을 대신 쓴다." +
            " 씬/프리팹에 이미 의미 있는(양수) 값이 직렬화돼 있으면 절대 덮어쓰지 않는다.")]
        public SurvivalBalanceConfig balanceConfig;

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
        /// 초기화 시점에 balanceConfig 폴백을 적용한다.
        /// </summary>
        private void Awake()
        {
            ApplyBalanceConfigFallback();

            // B4-11: 불꽃/연기/불빛 이펙트를 코드로 붙인다. CampfireEffect는 isLit을 읽기만 하는
            // 시각 전용 컴포넌트라 연료/조리 로직에는 아무 영향이 없다(이 한 줄을 지우면 이펙트만
            // 사라지고 게임플레이는 그대로다).
            CampfireEffect.EnsureAttached(gameObject);
        }

        /// <summary>
        /// balanceConfig가 있을 때, 0 이하로 남아있는(=미설정) 필드만 골라 config 값으로 채운다.
        /// secondsPerFuel이 0이면 불이 붙자마자 꺼지고 cookingExperience가 0이면 요리 스킬이 전혀
        /// 오르지 않으므로, 0 이하를 "아직 설정되지 않음"의 안전한 신호로 삼는다.
        /// balanceConfig가 비어 있으면 아무 것도 하지 않는다(기존 동작 100% 유지, NRE 없음).
        /// </summary>
        private void ApplyBalanceConfigFallback()
        {
            // B4-2: 인스펙터에서 연결되지 않았으면 Resources의 공용 에셋을 자동으로 집는다.
            // 런타임 생성 컴포넌트(WeatherSystem/Campfire/WaterStill 등)는 인스펙터 연결 수단이
            // 아예 없어서, 이 경로가 없으면 balanceConfig가 영원히 null로 남는다.
            if (balanceConfig == null)
                balanceConfig = SurvivalBalanceConfig.Active;
            if (balanceConfig == null)
                return;

            if (secondsPerFuel <= 0f) secondsPerFuel = balanceConfig.campfireSecondsPerFuel;
            if (cookingExperience <= 0f) cookingExperience = balanceConfig.campfireCookingExperience;
        }

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
