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

            foreach (var offset in legOffsets)
            {
                StructureVisualBuilder.CreateVisualPart(visualParts.transform, "Leg", PrimitiveType.Cylinder,
                    offset + Vector3.down * (roofHeight * 0.5f), new Vector3(0.12f, roofHeight * 0.5f, 0.12f),
                    new Color(0.35f, 0.22f, 0.1f));
            }
        }

        /// <summary>
        /// 밤에 쉼터에서 상호작용하면 취침해 아침(다음 날 일출, TimeOfDay01 0.25)까지 시간을 건너뛰고
        /// 소량의 체력을 회복하며 일사병 수치를 완전히 초기화한다. 신규 기능: 예전에는 밤이 되어도
        /// 그냥 지켜보거나 돌아다니는 것 외에 할 수 있는 게 없었는데, Stranded Deep처럼 쉼터를 지은
        /// 보람이 있도록 "밤을 건너뛰는 능동적 행동"을 추가했다. 낮에는 건너뛸 밤이 없으므로 실패한다.
        /// 세이브/로드나 SurvivalStats.Tick과 별개로 시계만 앞으로 이동시키므로, 건너뛴 시간 동안
        /// 허기/갈증이 소모되지 않는다 - 이는 "쉼터에서 안전하게 밤을 보낸다"는 컨셉을 살리기 위한
        /// 의도된 단순화다(허기/갈증까지 실시간 시뮬레이션하려면 별도의 대규모 시간가속 처리가 필요).
        /// </summary>
        public bool TrySleep(SurvivalClock clock, SurvivalStats survivalStats)
        {
            if (clock == null || clock.IsDaytime)
                return false;

            // 다음 날의 일출 시각(TimeOfDay01 == 0.25)으로 정확히 이동시킨다.
            int nextDay = clock.ElapsedDays + 1;
            clock.elapsedSeconds = (nextDay + 0.25f) * clock.secondsPerDay;

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
    }
}
