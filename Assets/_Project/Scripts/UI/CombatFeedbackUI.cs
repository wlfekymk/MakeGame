using UnityEngine;

namespace MakeGame.UI
{
    /// <summary>
    /// 전투(맹수/식인종/벌떼 등 위험요소와의 접촉)로 피해를 입었을 때만 화면 가장자리를 짧게
    /// 붉게 번쩍이는 시각 피드백을 보여주는 싱글턴.
    /// 버그 수정: SurvivalStats.TakeDamage는 굶주림/갈증/일사병처럼 상시로 반복 호출되는 피해에도
    /// 똑같이 쓰이는 공용 메서드라, 거기에 그대로 플래시를 걸면 평소에도 화면이 계속 번쩍이는
    /// 부작용이 생긴다. 그래서 TakeDamage가 아니라 HazardSource.ApplyHazardEffect(위험요소와
    /// "접촉한 그 순간"에만 호출됨)에서 TriggerHit()을 호출해, 전투/접촉 피해 시에만 발동하게 했다.
    /// 씬에 미리 배치할 필요 없이 RuntimeInitializeOnLoadMethod로 최초 접근 시 스스로 생성된다.
    /// </summary>
    public class CombatFeedbackUI : MonoBehaviour
    {
        public static CombatFeedbackUI Instance { get; private set; }

        [Tooltip("피격 플래시가 완전히 사라지기까지 걸리는 시간(초)")]
        public float flashDuration = 0.35f;

        [Tooltip("피격 순간의 최대 테두리 불투명도 (0~1)")]
        public float maxAlpha = 0.45f;

        private float flashTimer = 0f;

        /// <summary>
        /// 씬에 이 컴포넌트가 아직 없으면 스스로 생성해 DontDestroyOnLoad로 등록한다.
        /// AudioManager/GameManager처럼 Managers 오브젝트에 미리 붙여둘 필요 없이,
        /// 게임 시작 후 씬이 로드되는 시점에 자동으로 준비된다.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (Instance != null)
                return;

            var go = new GameObject("CombatFeedbackUI");
            go.AddComponent<CombatFeedbackUI>();
        }

        /// <summary>
        /// 싱글턴 인스턴스를 초기화하고, 씬 전환에도 파괴되지 않게 한다.
        /// </summary>
        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        /// <summary>
        /// 위험요소와 접촉해 전투 피해를 입은 순간 호출한다. 타이머를 최대치로 되돌려
        /// OnGUI가 다시 처음부터 페이드아웃하는 플래시를 그리게 한다.
        /// </summary>
        public void TriggerHit()
        {
            flashTimer = flashDuration;
        }

        /// <summary>
        /// 매 프레임 플래시 타이머를 감소시킨다.
        /// </summary>
        private void Update()
        {
            if (flashTimer > 0f)
                flashTimer -= Time.deltaTime;
        }

        /// <summary>
        /// 플래시 타이머가 남아 있는 동안 화면 가장자리를 붉게 덮어, 시간이 지날수록
        /// 옅어지는 피격 이펙트를 그린다. 화면 전체를 가리지 않도록 중앙은 비워둔 테두리 형태로 그린다.
        /// </summary>
        private void OnGUI()
        {
            if (flashTimer <= 0f)
                return;

            float t = Mathf.Clamp01(flashTimer / flashDuration);
            float alpha = maxAlpha * t;

            GUI.color = new Color(0.8f, 0f, 0f, alpha);

            float edge = Mathf.Lerp(0f, Screen.height * 0.18f, t);
            // 상/하/좌/우 네 개의 얇은 띠로 테두리 비네트를 그린다.
            GUI.DrawTexture(new Rect(0, 0, Screen.width, edge), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(0, Screen.height - edge, Screen.width, edge), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(0, 0, edge, Screen.height), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(Screen.width - edge, 0, edge, Screen.height), Texture2D.whiteTexture);

            GUI.color = Color.white;
        }

        /// <summary>
        /// 이 인스턴스가 파괴될 때 정적 참조가 죽은 오브젝트를 계속 가리키지 않도록 정리한다.
        /// </summary>
        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }
    }
}
