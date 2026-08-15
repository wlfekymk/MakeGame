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
        /// 지정한 인벤토리 아이템을 섭취한다.
        /// 음식/음료 효과가 있는 아이템만 섭취할 수 있으며, 성공 시 사용 횟수를 1 소모한다(소진되면 인벤토리에서 제거).
        /// </summary>
        public bool Consume(InventoryItem item)
        {
            if (item == null || item.data == null || inventory == null || survivalStats == null)
                return false;

            if (!item.data.IsConsumable)
                return false;

            if (item.data.hungerRestoreAmount > 0f)
            {
                survivalStats.ConsumeFood(item.data.hungerRestoreAmount);
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

            if (item.data.isRawFood && Random.value < GetRawFoodPoisonChance())
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
