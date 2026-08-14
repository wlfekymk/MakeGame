using UnityEngine;

namespace MakeGame.Systems
{
    /// <summary>
    /// 게임 내 경과 일수를 계산한다.
    /// 배 엔딩의 "상하지 않는 음식/물 30일치 확보" 조건 판정과 UI 표시에 사용된다.
    /// </summary>
    public class SurvivalClock : MonoBehaviour
    {
        [Tooltip("게임 내 하루의 길이(실제 초). 예: 600이면 실제 10분이 게임 내 하루")]
        public float secondsPerDay = 600f;

        [Tooltip("현재까지 경과한 게임 내 시간(초)")]
        public float elapsedSeconds = 0f;

        /// <summary>현재까지 경과한 게임 내 일수 (0일차부터 시작).</summary>
        public int ElapsedDays => Mathf.FloorToInt(elapsedSeconds / secondsPerDay);

        /// <summary>
        /// 현재 하루 중 진행률(0~1). 0=하루의 시작(자정), 0.5=한낮 기준으로 DayNightCycle이
        /// 태양 각도/밝기를 계산하는 데 사용한다. secondsPerDay로 나눈 나머지이므로 하루가 지나면
        /// 다시 0으로 돌아온다.
        /// </summary>
        public float TimeOfDay01 => (elapsedSeconds % secondsPerDay) / secondsPerDay;

        /// <summary>
        /// 현재 태양이 떠 있는(낮) 시간대인지 여부. DayNightCycle의 태양 각도 계산과 같은 기준으로,
        /// TimeOfDay01이 0.25(일출)~0.75(일몰) 사이일 때를 낮으로 본다. 일사병(UpdateSunstroke)이
        /// 밤에도 계속 증가하지 않도록 이 값을 참조한다.
        /// </summary>
        public bool IsDaytime => TimeOfDay01 >= 0.25f && TimeOfDay01 <= 0.75f;

        /// <summary>
        /// 매 프레임 경과 시간을 누적한다.
        /// </summary>
        private void Update()
        {
            elapsedSeconds += Time.deltaTime;
        }
    }
}
