using UnityEngine;
using MakeGame.Data;

namespace MakeGame.Systems
{
    /// <summary>
    /// 게임 내 경과 일수를 계산한다.
    /// 배 엔딩의 "상하지 않는 음식/물 30일치 확보" 조건 판정과 UI 표시에 사용된다.
    ///
    /// B4-1 (Spec_15 3단계 배선): SurvivalBalanceConfig를 선택적(nullable) 참조로 받는다.
    /// 폴백으로 읽는 config 필드 — secondsPerDay ← secondsPerDay.
    /// 폴백은 secondsPerDay가 0 이하(미설정)일 때만 적용되므로 씬 직렬화 값(실측 600)이 항상 이긴다.
    /// secondsPerDay는 ElapsedDays/TimeOfDay01의 나눗셈 분모라, 0이면 0으로 나누기가 되는 값이므로
    /// 폴백 후에도 0 이하로 남으면 안전한 기본값(600)으로 마지막 가드를 건다.
    /// </summary>
    public class SurvivalClock : MonoBehaviour
    {
        /// <summary>secondsPerDay가 어떤 경로로도 설정되지 않았을 때 쓰는 최후 기본값(0 나누기 방지).</summary>
        private const float DefaultSecondsPerDay = 600f;

        [Header("밸런스 config (선택, B4-1)")]
        [Tooltip("연결하면, 아래 secondsPerDay가 0 이하로(미설정) 남아있는 경우에 한해 config의" +
            " secondsPerDay 값을 대신 쓴다. 씬에 이미 의미 있는(양수) 값이 직렬화돼 있으면 절대 덮어쓰지 않는다.")]
        public SurvivalBalanceConfig balanceConfig;

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
        /// 초기화 시점에 balanceConfig 폴백을 적용한다.
        /// </summary>
        private void Awake()
        {
            ApplyBalanceConfigFallback();
        }

        /// <summary>
        /// balanceConfig가 있을 때, secondsPerDay가 0 이하로(=미설정) 남아있는 경우에만 config 값으로
        /// 채운다. balanceConfig가 비어 있으면 아무 것도 하지 않는다(기존 동작 100% 유지, NRE 없음).
        /// config까지 없거나 config 값도 0 이하라면, ElapsedDays/TimeOfDay01의 0 나누기를 막기 위해
        /// 최후 기본값(600)으로 되돌린다 - 이 경로는 씬에 정상 값이 있으면 절대 실행되지 않는다.
        /// </summary>
        private void ApplyBalanceConfigFallback()
        {
            // B4-2: 인스펙터에서 연결되지 않았으면 Resources의 공용 에셋을 자동으로 집는다.
            // 런타임 생성 컴포넌트(WeatherSystem/Campfire/WaterStill 등)는 인스펙터 연결 수단이
            // 아예 없어서, 이 경로가 없으면 balanceConfig가 영원히 null로 남는다.
            if (balanceConfig == null)
                balanceConfig = SurvivalBalanceConfig.Active;
            if (balanceConfig != null && secondsPerDay <= 0f)
                secondsPerDay = balanceConfig.secondsPerDay;

            if (secondsPerDay <= 0f)
                secondsPerDay = DefaultSecondsPerDay;
        }

        /// <summary>
        /// 매 프레임 경과 시간을 누적한다.
        /// </summary>
        private void Update()
        {
            elapsedSeconds += Time.deltaTime;
        }
    }
}
