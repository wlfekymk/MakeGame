using UnityEngine;
using MakeGame.Data;
using MakeGame.Player;

namespace MakeGame.Systems
{
    /// <summary>
    /// 음식/음료 아이템을 섭취하는 처리를 담당한다.
    /// 허기/갈증을 회복시키고, 익히지 않은 음식(isRawFood)을 먹으면 확률적으로 식중독(중독)에 걸린다.
    /// </summary>
    public class ConsumptionSystem : MonoBehaviour
    {
        [Tooltip("아이템을 소모할 대상 인벤토리")]
        public PlayerInventory inventory;

        [Tooltip("허기/갈증을 회복시킬 대상 생존 수치")]
        public SurvivalStats survivalStats;

        [Tooltip("요리 스킬 보너스를 읽어올 대상. 비워 두면 같은 GameObject → 씬 순서로 자동으로 찾는다.")]
        public PlayerSkills playerSkills;

        [Tooltip("익히지 않은 음식을 먹었을 때 식중독(중독)에 걸릴 확률 (0~1)")]
        [Range(0f, 1f)]
        public float rawFoodPoisonChance = 0.3f;

        // [죽은 config 값 배선 (B)] SurvivalBalanceConfig.rawFoodPoisonChance(=0.3)도 아무도 읽지 않는
        // 죽은 값이었다. 그런데 이 필드는 이 프로젝트가 다른 12곳에서 쓰는 "0 이하 = 미설정" 폴백 패턴을
        // 쓸 수 없다:
        //   · 확률 0(= 생식해도 절대 식중독에 걸리지 않는다)은 완전히 유효한 밸런스 설정이라
        //     "미설정"과 구분되지 않는다. 0을 넣으면 조용히 config의 0.3으로 되돌아간다.
        //   · [Range(0,1)]가 붙어 있어 음수 센티널(-1 = 미설정)도 쓸 수 없다 - 인스펙터 슬라이더가
        //     음수를 입력할 방법을 주지 않고, 값을 넣어도 클램프된다.
        // 그래서 "값의 크기"가 아니라 "명시적 토글"로 출처를 고른다. 기본값 false = 씬 동작 불변이며,
        // [B12 정정] 예전 주석은 "씬 0.3 = config 0.3이라 토글을 켜도 안 바뀐다"였는데 둘 다 0.15로
        // 내려갔다(밤에 30분 안에 죽는 유일한 실질 사인이었다). 여전히 두 값이 같아 토글의 효과는 0이지만,
        // **값이 갈리는 순간 조용히 달라진다** - 이 프로젝트에서 가장 자주 사고가 난 지점이다.
        // 지금 씬에는 balanceConfig가 비어 있고 useConfigPoisonChance도 false라, config의
        // rawFoodPoisonChance는 실제로 한 번도 읽히지 않는다. config만 고치고 반영됐다고 착각하지 말 것.
        [Header("밸런스 config (선택)")]
        [Tooltip("식중독 확률을 config에서 읽고 싶을 때 연결한다. 비워두면 useConfigPoisonChance가 켜진 " +
            "경우에 한해 Resources의 공용 SurvivalBalanceConfig를 자동으로 집는다.")]
        public SurvivalBalanceConfig balanceConfig;

        [Tooltip("켜면 위 rawFoodPoisonChance 대신 config의 rawFoodPoisonChance를 쓴다.\n" +
            "끄면(기본값) 이 컴포넌트에 직렬화된 값만 쓴다 - config가 연결돼 있어도 무시한다.")]
        public bool useConfigPoisonChance = false;

        /// <summary>
        /// 토글이 켜져 있을 때만 config를 확보한다. 꺼져 있으면 Resources 조회조차 하지 않으므로,
        /// 기본 상태에서는 이 필드가 존재하지 않던 때와 실행 경로가 완전히 같다.
        /// </summary>
        private void Awake()
        {
            if (useConfigPoisonChance && balanceConfig == null)
                balanceConfig = SurvivalBalanceConfig.Active;
        }

        /// <summary>
        /// 이번 섭취에 실제로 적용할 식중독 확률. 토글이 켜져 있고 config가 실제로 잡혔을 때만
        /// config 값을 쓰고, 그 외에는 항상 이 컴포넌트의 직렬화 값을 쓴다(에셋이 없어도 NRE 없음).
        /// </summary>
        private float GetRawFoodPoisonChance()
        {
            if (useConfigPoisonChance && balanceConfig != null)
                return balanceConfig.rawFoodPoisonChance;

            return rawFoodPoisonChance;
        }

        /// <summary>
        /// [요리 스킬 배선] 보너스를 읽을 PlayerSkills를 확보한다(인스펙터 지정 → 같은 GameObject → 씬 조회).
        /// Awake에서 한 번에 잡지 않고 섭취 시점에 늦게 잡는 이유: ConsumptionSystem이 Player보다 먼저
        /// Awake를 받는 씬/부트스트랩 순서에서 영구 null이 되는 사고를 피하기 위한 것이다
        /// (Smoker.ResolveSkills가 같은 이유로 같은 형태를 쓴다). 못 찾으면 null을 그대로 돌려주고,
        /// 호출부가 배율 1.0으로 처리하므로 스킬 컴포넌트가 없는 씬에서도 예전 동작 그대로다.
        /// </summary>
        private PlayerSkills ResolveSkills()
        {
            if (playerSkills == null)
                playerSkills = GetComponent<PlayerSkills>();

            if (playerSkills == null)
                playerSkills = FindAnyObjectByType<PlayerSkills>();

            return playerSkills;
        }

        /// <summary>
        /// [요리 스킬 배선] 이번 섭취에 곱할 요리 스킬 배율. 수치의 단일 소스는
        /// PlayerSkills.GetCookingRestoreMultiplier()이며(Lv1 = 1.0 / Lv10 = 1.27), 여기서는 "누구에게
        /// 적용할지"만 정한다.
        ///
        /// **적용 대상: 익힌 음식(isRawFood == false)만.** 요리 스킬이므로 생식품에 붙는 것은 말이 안 되고,
        /// 붙이면 "굽지 않고 그냥 먹는 것"이 스킬로 강해져 조리 루프 자체를 무의미하게 만든다.
        /// 참고로 지금 데이터에서 이 조건에 걸리는 것은 구운고기·구운생선·훈제육·훈제생선(= 실제 조리
        /// 결과물)과 비상식량·해조류 두 종이다. 뒤의 둘은 엄밀히는 조리 결과물이 아니지만
        /// (Lv10에서도 +5.4 / +1.6 허기로 영향이 미미하다) 이들을 걸러내려면 ItemData에 "조리 결과물"
        /// 플래그를 새로 넣거나 아이템 이름 규칙에 기대야 해서 이번 범위에서는 넣지 않았다.
        ///
        /// **갈증(음료)에는 적용하지 않는다** - 요리 스킬은 허기 회복 배율이고, 음료는 조리 대상이 아니다.
        /// </summary>
        private float GetCookingRestoreMultiplier(ItemData data)
        {
            if (data == null || data.isRawFood)
                return 1f;

            PlayerSkills skills = ResolveSkills();
            if (skills == null)
                return 1f;

            return skills.GetCookingRestoreMultiplier();
        }

        /// <summary>
        /// 지정한 인벤토리 아이템을 섭취한다.
        /// 음식/음료 효과가 있는 아이템만 섭취할 수 있으며, 성공 시 사용 횟수를 1 소모한다(소진되면 인벤토리에서 제거).
        /// </summary>
        public bool Consume(InventoryItem item)
        {
            if (item == null || item.data == null || inventory == null || survivalStats == null)
                return false;

            if (!item.data.IsConsumable)
                return false;

            // [식량 루프] **같은 종류 중 가장 오래된 것부터 먹는다(FIFO).**
            // 인벤토리 한 칸은 그 칸에서 가장 상한 것의 신선도를 표시하는데(InventoryStack.oldest),
            // 정작 먹을 때 목록 앞쪽의 신선한 인스턴스가 소모되면 화면과 실제가 갈리고, 플레이어가
            // 상한 것만 계속 쌓아 두는(그리고 영원히 안 먹는) 상태가 된다. 부패 대상이 아닌
            // 아이템(치료제·음료·재료)은 이 경로를 타지 않으므로 예전 동작 그대로다.
            if (FoodSpoilage.CanSpoil(item.data))
            {
                InventoryItem oldest = inventory.FindMostSpoiled(item.data);
                if (oldest != null)
                    item = oldest;
            }

            FoodSpoilStage stage = FoodSpoilage.GetStage(item);

            if (item.data.hungerRestoreAmount > 0f)
            {
                // 상할수록 허기 회복이 줄어든다(신선 1.0 / 상하기 시작 0.6 / 부패 0.25).
                // 갈증 회복에는 적용하지 않는다 - 음료는 부패 대상이 아니다(FoodSpoilage.GetSpoilDays).
                //
                // [요리 스킬 배선] 부패 배율 **뒤에** 요리 배율을 한 번 더 곱한다. 두 배율은 서로 다른
                // 축이라(신선도 = 아이템 상태 / 요리 = 플레이어 숙련) 이중 적용이 아니며, 곱셈이라
                // 순서를 바꿔도 결과가 같다. 여기서 굳이 `(회복량 * 부패배율)`을 통째로 남기고 그 뒤에
                // 곱한 이유는, 요리 배율이 정확히 1f인 레벨 1에서 예전 식과 **비트 단위로 동일한 값**이
                // 나오게 하기 위한 것이다(x * 1f == x). 즉 Lv1 회귀 0.
                // 부패한 음식은 요리 실력이 좋아도 여전히 0.25배로 줄어든다 - 의도한 동작이다.
                survivalStats.ConsumeFood(item.data.hungerRestoreAmount * FoodSpoilage.GetRestoreMultiplier(stage)
                    * GetCookingRestoreMultiplier(item.data));
                AudioManager.Instance?.PlayEat(); // 음식 섭취 효과음
            }

            if (item.data.thirstRestoreAmount > 0f)
            {
                if (item.data.isCoconutWaterSource)
                    survivalStats.ConsumeCoconutWater(item.data.thirstRestoreAmount);
                else
                    survivalStats.ConsumeWater(item.data.thirstRestoreAmount);

                AudioManager.Instance?.PlayDrink(); // 음료 섭취 효과음
            }

            // [식량 루프] 식중독 확률 = (생음식이면 기존 확률) + (부패 단계별 가산분).
            // 익힌 음식이라도 썩으면 위험해지고, 생음식은 원래 위험에 부패분이 더해진다.
            // 판정은 예전과 같이 UnityEngine.Random 한 번이다(월드 생성 스트림이 아니라 무관 -
            // AGENT_BRIEF 2장 6번의 "조리 확률 등 비생성 계열은 예외").
            float poisonChance = item.data.isRawFood ? GetRawFoodPoisonChance() : 0f;
            poisonChance = Mathf.Clamp01(poisonChance + FoodSpoilage.GetExtraPoisonChance(stage));

            if (poisonChance > 0f && Random.value < poisonChance)
                survivalStats.ApplyPoison();

            // 치료 효과가 있는 아이템(붕대/해독제/부목 등)을 사용하면 해당 상태 이상을 치료한다.
            // 예전에는 이 아이템들을 사용할 방법 자체가 없어 중독/출혈/골절이 한 번 걸리면 영구 지속되는
            // 치명적인 버그가 있었다 (CurePoison/BandageBleeding/HealBrokenBone 호출부가 어디에도 없었음).
            if (item.data.curesBleeding && survivalStats.isBleeding)
            {
                survivalStats.BandageBleeding();
                // 연결(B-1): tech-artist가 AudioManager에 만들어 둔 전용 치료 성공음으로 교체.
                // 예전에는 PlayCraftSuccess()를 재사용해 "제작"과 "치료"가 같은 소리로 구분이 안 됐다.
                AudioManager.Instance?.PlayHealSuccess();
            }

            if (item.data.curesPoison && survivalStats.isPoisoned)
                survivalStats.CurePoison();

            if (item.data.curesBrokenBone && survivalStats.hasBrokenBone)
                survivalStats.HealBrokenBone();

            inventory.UseItem(item);
            return true;
        }
    }
}
