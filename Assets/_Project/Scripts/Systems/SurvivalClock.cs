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

        // ── 일몰 예고 (Design_Onboarding.md 6장, game-designer 요청) ─────────────────────────────
        //
        // 왜 필요한가: "첫 밤 사망은 허용한다. 다만 예고 없는 사망은 교육이 아니라 사고다"가 설계
        // 결정이다. 밤이 온다는 사실을 밤이 오기 **전에** 알려야 한다.
        //
        // 이 클래스가 하는 일은 **시각 판정과 1회성 보장뿐**이다. 화면 표시는 UI 담당이므로 여기서는
        // 이벤트(SunsetWarningRaised)와 상태(SunsetWarningFired)만 노출한다.
        //
        // 1회성을 "구독자 쪽 플래그"가 아니라 여기서 보장하는 이유: 구독자가 여럿이어도, 씬이 도중에
        // UI를 새로 만들어도 예고는 정확히 한 번만 발생해야 한다. 매일 뜨면 소음이 되고, 소음이 되면
        // 정작 첫날에 읽히지 않는다.

        /// <summary>밤이 시작되는 TimeOfDay01 값. IsDaytime의 상한(0.75)과 같은 기준이다.</summary>
        private const float NightStartTimeOfDay = 0.75f;

        [Header("일몰 예고 (Design_Onboarding 6장)")]
        [Tooltip("해가 기울기 시작했다고 판단하는 하루 진행률(0~1). 설계 기준값 0.65.\n" +
            "밤 시작(0.75)보다 반드시 작아야 한다 - 크거나 같으면 예고가 밤보다 늦어져 의미가 없다.")]
        public float sunsetWarningTimeOfDay = 0.65f;

        [Tooltip("일몰 예고를 발생시킬 날짜(ElapsedDays 기준, 0 = 1일차). 이 날을 놓치면 예고는 다시 뜨지 않는다.")]
        public int sunsetWarningDay = 0;

        /// <summary>
        /// 1일차 일몰 예고가 발생하는 순간 한 번만 호출된다(세션당 1회). UI가 구독해 안내 문구를 띄우면 된다.
        /// 구독은 예고 시각(기본값 기준 1일차 390초 지점)보다 먼저 이뤄지기만 하면 되므로 OnEnable/Start 어느
        /// 쪽이어도 늦지 않는다. 늦게 생성되는 UI를 위해 아래 SunsetWarningFired를 폴링해도 된다.
        /// </summary>
        public event System.Action SunsetWarningRaised;

        /// <summary>
        /// 일몰 예고가 이미 발생했거나(=표시할 때가 지났음) 발생 기회를 놓쳐 소진되었는지 여부.
        /// 한 번 true가 되면 다시 false로 돌아가지 않는다.
        /// </summary>
        public bool SunsetWarningFired { get; private set; }

        /// <summary>
        /// 일몰 예고가 실제로 발생한 시각(Time.time). 아직 발생하지 않았으면 -1이다.
        /// 이벤트를 놓친 UI가 "몇 초 전에 떴는지"를 계산해 표시 시간을 결정할 수 있도록 함께 노출한다.
        /// </summary>
        public float SunsetWarningTime { get; private set; } = -1f;

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
            UpdateSunsetWarning();
        }

        /// <summary>
        /// 지정한 날(sunsetWarningDay)의 일몰 직전 구간에 들어서면 예고를 한 번만 발생시킨다.
        ///
        /// 세 가지 경우를 구분한다.
        ///  (1) 아직 그 날이 오지 않음 → 아무 것도 하지 않고 기다린다.
        ///  (2) 그 날의 예고 구간(threshold ~ 밤 시작) 안 → 발생시키고 소진 처리한다.
        ///  (3) 그 날이 이미 지나감 → 조용히 소진 처리한다. 불러오기(SaveLoadController가
        ///      elapsedSeconds를 되돌린다)로 5일차에서 시작한 플레이어에게 "곧 밤이 됩니다"를
        ///      새삼 띄우지 않기 위해서다. 이미 밤을 여러 번 겪은 사람에게는 정보가 아니라 소음이다.
        /// </summary>
        private void UpdateSunsetWarning()
        {
            if (SunsetWarningFired)
                return;

            int day = ElapsedDays;
            if (day < sunsetWarningDay)
                return;

            if (day > sunsetWarningDay)
            {
                SunsetWarningFired = true; // 기회를 놓쳤다 - 다시 뜨지 않도록 소진만 한다(이벤트 없음).
                return;
            }

            // 밤 시작 직전까지로 상한을 둔다. 잘못된 설정값(0.75 이상)이 들어와도 예고가 밤보다
            // 늦게 뜨는 일은 없게 만든다.
            float threshold = Mathf.Clamp(sunsetWarningTimeOfDay, 0f, NightStartTimeOfDay - 0.001f);
            float t = TimeOfDay01;
            if (t < threshold || t >= NightStartTimeOfDay)
                return;

            SunsetWarningFired = true;
            SunsetWarningTime = Time.time;
            SunsetWarningRaised?.Invoke();
        }
    }
}
