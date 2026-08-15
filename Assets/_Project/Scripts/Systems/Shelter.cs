using UnityEngine;
using MakeGame.Player;

namespace MakeGame.Systems
{
    /// <summary>
    /// 플레이어가 제작해 설치한 쉼터(Shelter). Stranded Deep 기준: 비/햇빛을 막아주고 휴식 지점이 된다.
    /// 그늘 판정(지붕 콜라이더가 "Shade" 레이어) 외에도, 밤에 상호작용(E)하면 취침해 아침까지
    /// 시간을 건너뛰고 소량의 체력/일사병을 회복하는 능동적 기능도 제공한다 (TrySleep).
    /// </summary>
    public class Shelter : MonoBehaviour
    {
        [Tooltip("이 쉼터가 제공하는 그늘 판정 반경(참고용 수치, 실제 판정은 레이캐스트로 이뤄진다)")]
        public float shadeRadius = 3f;

        [Tooltip("취침 시 즉시 회복되는 체력량")]
        public float sleepHealAmount = 15f;

        [Tooltip("지붕이 바닥으로부터 떠 있어야 하는 높이. 설치 시 Instantiate가 루트 위치를 바닥(설치 지점)에\n맞춰버리므로, 이 값만큼 스스로 들어올려 지붕이 바닥에 깔리지 않게 한다.")]
        public float roofHeight = 2.2f;

        /// <summary>
        /// 설치 직후 루트(지붕)를 roofHeight만큼 들어올리고, 바닥까지 닿는 기둥 4개를 절차적으로 붙여
        /// 판자 한 장뿐이던 플레이스홀더를 실제 쉼터처럼 보이게 만든다.
        /// </summary>
        private void Awake()
        {
            transform.position += Vector3.up * roofHeight;
            BuildVisual();
        }

        /// <summary>
        /// 지붕 색을 이엉(초가) 색으로 바꾸고, 스케일이 비균일한 루트(지붕 Plane) 아래에
        /// 스케일 영향을 받지 않는 보정용 빈 오브젝트를 하나 만들어 그 밑에 기둥 4개를 붙인다.
        /// </summary>
        private void BuildVisual()
        {
            var rootRenderer = GetComponent<MeshRenderer>();
            if (rootRenderer != null)
                rootRenderer.sharedMaterial = StructureVisualBuilder.CreateColorMaterial(new Color(0.55f, 0.42f, 0.22f));

            // 루트가 지붕용으로 (4, 0.3, 4) 비균일 스케일되어 있어, 그 스케일을 상쇄하는 빈 부모를 만든다.
            var visualParts = new GameObject("VisualParts");
            visualParts.transform.SetParent(transform, false);
            Vector3 parentScale = transform.localScale;
            visualParts.transform.localScale = new Vector3(
                parentScale.x != 0f ? 1f / parentScale.x : 1f,
                parentScale.y != 0f ? 1f / parentScale.y : 1f,
                parentScale.z != 0f ? 1f / parentScale.z : 1f);

            Vector3[] legOffsets =
            {
                new Vector3(1.6f, 0f, 1.6f),
                new Vector3(-1.6f, 0f, 1.6f),
                new Vector3(1.6f, 0f, -1.6f),
                new Vector3(-1.6f, 0f, -1.6f),
            };

            // [tech-artist-B 요청 - 인공물 시각 언어] 매끈한 원기둥 다리는 야자수 줄기/대나무 자원과 같은
            // 형태 언어라 "내가 지은 것"으로 읽히지 않는다(ArtDirection 2장 4번). 각진 사각 기둥 + 밧줄
            // 결속으로 바꾼다. 높이 인자가 원기둥과 다르다는 점에 주의: 원기둥 메시는 높이가 2단위라
            // scale.y에 절반(roofHeight * 0.5)을 넣어야 했지만, CreateLashedPost는 큐브라 실제 높이를
            // 그대로 받는다 - 그래서 roofHeight를 넘긴다(중심은 지붕 아래 roofHeight/2로 동일하므로
            // 기둥이 바닥~지붕을 정확히 잇는 결과도 이전과 완전히 같다).
            foreach (var offset in legOffsets)
            {
                StructureVisualBuilder.CreateLashedPost(visualParts.transform, "Leg",
                    offset + Vector3.down * (roofHeight * 0.5f), roofHeight, 0.12f, new Color(0.35f, 0.22f, 0.1f));
            }
        }

        /// <summary>
        /// 밤에 쉼터에서 상호작용하면 취침해 **이 밤이 끝나는 아침**(TimeOfDay01 0.25)까지 시간을 건너뛰고
        /// 소량의 체력을 회복하며 일사병 수치를 완전히 초기화한다. 신규 기능: 예전에는 밤이 되어도
        /// 그냥 지켜보거나 돌아다니는 것 외에 할 수 있는 게 없었는데, Stranded Deep처럼 쉼터를 지은
        /// 보람이 있도록 "밤을 건너뛰는 능동적 행동"을 추가했다. 낮에는 건너뛸 밤이 없으므로 실패한다.
        /// 세이브/로드나 SurvivalStats.Tick과 별개로 시계만 앞으로 이동시키므로, 건너뛴 시간 동안
        /// 허기/갈증이 소모되지 않는다 - 이는 "쉼터에서 안전하게 밤을 보낸다"는 컨셉을 살리기 위한
        /// 의도된 단순화다(허기/갈증까지 실시간 시뮬레이션하려면 별도의 대규모 시간가속 처리가 필요).
        /// </summary>
        public bool TrySleep(SurvivalClock clock, SurvivalStats survivalStats)
        {
            if (clock == null || clock.IsDaytime || clock.secondsPerDay <= 0f)
                return false;

            // "이 밤이 끝나는 아침"(TimeOfDay01 == 0.25)으로 이동시킨다. GetWakeDay 주석 참고 -
            // 예전의 ElapsedDays + 1은 자정을 넘긴 뒤에 자면 하루를 통째로 더 건너뛰었다.
            clock.elapsedSeconds = GetWakeSeconds(clock);

            if (survivalStats != null)
            {
                survivalStats.Heal(sleepHealAmount);
                survivalStats.sunstroke = 0f; // 밤새 푹 쉬어 더위(일사병)가 완전히 가라앉는다.
            }

            // 연결(B-2): tech-artist가 AudioManager에 만들어 둔 전용 취침 성공음으로 교체.
            // 예전에는 PlayCraftSuccess()를 재사용해 "제작"과 "취침"이 같은 소리로 구분이 안 됐다.
            AudioManager.Instance?.PlaySleepSuccess();
            return true;
        }

        // ── 취침 목적지 계산 (game-designer 지적: 자정 이후 취침이 1.25일을 건너뛴다) ──────────────
        //
        // 무엇이 틀렸었나: 예전 계산은 `ElapsedDays + 1`이었다. 밤은 하루의 끝(TimeOfDay01 0.75~1.0)과
        // **다음 날의 시작**(0~0.25) 양쪽에 걸쳐 있는데, 자정을 넘긴 시각에는 ElapsedDays가 이미 +1 된
        // 상태다. 거기에 또 +1을 하면 의도(그날 아침까지 0.25일)의 5배인 1.25일을 건너뛴다.
        // 그 결과 배 엔딩의 15일 조건 도달이 74.5분 → 59.5분으로 20% 짧아졌다(Design_MidGame 7장).
        //
        // 고친 방법: 시각을 0.75일(= 밤의 시작) 앞으로 밀어 놓고 날짜를 내림한다. 그러면 일몰 직후와
        // 자정 직후가 **같은 날 번호**로 접히므로, 한 밤 안의 어느 시각에 자든 도착지가 하나로 정해진다.
        //   · 일몰 직후 t=0.76 (day D) → floor(D+0.76+0.75) = D+1 → 도착 (D+1.25)일 = 0.49일 건너뜀
        //   · 자정 직후 t=0.01 (day D+1, 같은 밤) → floor(D+1.01+0.75) = D+1 → 같은 도착지, 0.24일 건너뜀
        //   · 일출 직전 t=0.24 (day D+1) → floor(D+1.24+0.75) = D+1 → 같은 도착지, 0.01일 건너뜀
        // 시계가 뒤로 가는 경우는 없다: n = floor(x + 0.75) > x - 0.25 이므로 항상 n + 0.25 > x다.

        /// <summary>밤의 시작(= 낮의 끝) 시각. SurvivalClock.IsDaytime의 상한과 같은 기준이다.</summary>
        private const float NightStartTimeOfDay = 0.75f;

        /// <summary>일출(아침) 시각. SurvivalClock.IsDaytime의 하한/DayNightCycle과 같은 기준이다.</summary>
        private const float MorningTimeOfDay = 0.25f;

        /// <summary>
        /// 지금 취침하면 눈을 뜨는 날(ElapsedDays 기준, 0 = 1일차). 위 주석의 계산이다.
        /// 시계가 없거나 하루 길이가 0 이하면(0 나누기) 0을 돌려주므로, 호출부는 시계를 먼저 확인할 것.
        /// [ui-engineer] InteractionPromptUI가 같은 식을 따로 들고 있었는데, 그쪽이 이 메서드를 부르면
        /// 프롬프트의 "N일차 아침" 표기가 실제 도착지와 갈라지지 않는다.
        /// </summary>
        public static int GetWakeDay(SurvivalClock clock)
        {
            if (clock == null || clock.secondsPerDay <= 0f)
                return 0;

            return Mathf.FloorToInt(clock.elapsedSeconds / clock.secondsPerDay + NightStartTimeOfDay);
        }

        /// <summary>
        /// 지금 취침하면 도달하는 게임 내 경과 시간(초). 곧 다음 아침(TimeOfDay01 == 0.25)이다.
        /// 건너뛰는 시간을 표시하려면 이 값에서 clock.elapsedSeconds를 빼면 된다.
        /// </summary>
        public static float GetWakeSeconds(SurvivalClock clock)
        {
            if (clock == null || clock.secondsPerDay <= 0f)
                return 0f;

            return (GetWakeDay(clock) + MorningTimeOfDay) * clock.secondsPerDay;
        }
    }
}
