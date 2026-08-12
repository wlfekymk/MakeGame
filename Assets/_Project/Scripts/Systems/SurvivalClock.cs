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
        /// 매 프레임 경과 시간을 누적한다.
        /// </summary>
        private void Update()
        {
            elapsedSeconds += Time.deltaTime;
        }
    }
}
