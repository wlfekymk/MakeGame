using UnityEngine;
using MakeGame.Player;

namespace MakeGame.Systems
{
    /// <summary>
    /// 물 증류기 (Stranded Deep 기준: 나뭇잎 증발/빗물 등으로 시간이 지나면 담수를 생산하는 제작 구조물).
    /// 코코넛 워터로 임시 해갈하다가, 이 구조물을 제작하면 지속적으로 담수를 확보할 수 있게 된다.
    /// </summary>
    public class WaterStill : MonoBehaviour
    {
        [Tooltip("초당 생산되는 물의 양")]
        public float waterPerSecond = 0.3f;

        [Tooltip("현재 저장된 물의 양")]
        public float storedWater = 0f;

        [Tooltip("최대로 저장할 수 있는 물의 양")]
        public float maxStorage = 20f;

        /// <summary>
        /// 매 프레임 자동으로 시간 경과 로직을 진행시킨다 (별도 드라이버 없이 스스로 작동).
        /// </summary>
        private void Update()
        {
            Tick(Time.deltaTime);
        }

        /// <summary>
        /// 시간 경과에 따라 물을 생산한다. 저장량이 최대치를 넘지 않는다.
        /// </summary>
        public void Tick(float deltaTime)
        {
            storedWater = Mathf.Min(maxStorage, storedWater + waterPerSecond * deltaTime);
        }

        /// <summary>
        /// 저장된 물을 모두 수확하여 반환하고, 저장량을 0으로 초기화한다.
        /// </summary>
        public float Collect()
        {
            float collected = storedWater;
            storedWater = 0f;
            return collected;
        }

        /// <summary>
        /// 저장된 물을 수확하여 곧바로 지정한 플레이어의 갈증 수치를 회복시킨다.
        /// </summary>
        public void CollectInto(SurvivalStats targetStats)
        {
            if (targetStats == null)
                return;

            float collected = Collect();
            targetStats.ConsumeWater(collected);
        }
    }
}
