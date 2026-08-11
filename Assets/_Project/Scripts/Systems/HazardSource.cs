using UnityEngine;
using MakeGame.Data;
using MakeGame.Player;

namespace MakeGame.Systems
{
    /// <summary>
    /// 섬에 배치되는 위험 요소(독사, 전갈, 곰, 벌떼, 함정, 식인종 등) 하나를 나타낸다.
    /// 플레이어와 접촉하면 위험 요소 종류에 맞는 상태 이상/피해를 SurvivalStats에 적용한다.
    /// 음식 부족/탈수는 개별 오브젝트가 아니라 SurvivalStats의 허기/갈증 감소 로직으로 이미 처리되므로 여기서는 다루지 않는다.
    /// </summary>
    public class HazardSource : MonoBehaviour
    {
        [Tooltip("이 위험 요소의 종류")]
        public HazardType hazardType;

        [Tooltip("접촉 시 즉시 입히는 피해량 (곰, 식인종처럼 직접 공격하는 유형에 사용)")]
        public float directDamage = 10f;

        /// <summary>
        /// 지정한 대상에게 이 위험 요소의 효과를 적용한다.
        /// 위험 요소 종류에 따라 중독/출혈/골절/직접 피해 중 알맞은 효과를 준다.
        /// </summary>
        public void ApplyHazardEffect(SurvivalStats target)
        {
            if (target == null)
                return;

            switch (hazardType)
            {
                case HazardType.VenomousSnake:
                case HazardType.Scorpion:
                    // 독사/전갈: 중독 상태로 만든다.
                    target.ApplyPoison();
                    break;

                case HazardType.Bear:
                case HazardType.Cannibal:
                    // 곰/식인종: 직접 피해 + 출혈을 유발한다.
                    target.TakeDamage(directDamage);
                    target.ApplyBleeding();
                    break;

                case HazardType.BeeSwarm:
                    // 벌떼: 직접 피해를 입힌다 (중독/출혈은 없음).
                    target.TakeDamage(directDamage);
                    break;

                case HazardType.Trap:
                    // 함정: 골절 상태로 만든다.
                    target.ApplyBrokenBone();
                    break;

                case HazardType.FoodShortage:
                case HazardType.Dehydration:
                    // 음식 부족/탈수는 SurvivalStats의 허기/갈증 감소 로직에서 이미 처리되므로 별도 효과 없음.
                    break;
            }
        }

        /// <summary>
        /// 플레이어의 콜라이더와 접촉했을 때 자동으로 위험 요소 효과를 적용한다.
        /// 플레이어 오브젝트에는 SurvivalStats 컴포넌트가 붙어 있어야 한다.
        /// </summary>
        private void OnTriggerEnter(Collider other)
        {
            SurvivalStats stats = other.GetComponent<SurvivalStats>();
            if (stats != null)
                ApplyHazardEffect(stats);
        }
    }
}
