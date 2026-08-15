using UnityEngine;
using UnityEngine.UI;

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
    /// 개선(B2-14): OnGUI(레거시 IMGUI)로 직접 그리던 것을 UIBuilder 기반 UGUI로 옮겼다. IMGUI는
    /// Screen Space Overlay Canvas보다 항상 나중에(최상단에) 그려져 다른 UGUI 화면(GameOverUI 등)을
    /// 가려버리는 문제가 있었기 때문에(GameOverController.OnGUI 사례 참고) OnGUI를 완전히 제거했다.
    /// </summary>
    public class CombatFeedbackUI : MonoBehaviour
    {
        public static CombatFeedbackUI Instance { get; private set; }

        [Tooltip("피격 플래시가 완전히 사라지기까지 걸리는 시간(초)")]
        public float flashDuration = 0.35f;

        [Tooltip("피격 순간의 최대 테두리 불투명도 (0~1)")]
        public float maxAlpha = 0.45f;

        private float flashTimer = 0f;

        private GameObject panelRoot;
        // 화면 가장자리 네 조각(상/하/좌/우)의 RectTransform과 Image. 플래시가 진행되는 동안 두께(edge)와
        // 알파가 함께 줄어들며 옅어지는 비네트 효과를 낸다.
        private RectTransform topRt, bottomRt, leftRt, rightRt;
        private Image topImage, bottomImage, leftImage, rightImage;

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
        /// 싱글턴 인스턴스를 초기화하고, 씬 전환에도 파괴되지 않게 한다. UI 계층은 여기서 바로 만들지
        /// 않는다 - UIBuilder.CreateCanvas가 새로 만드는 캔버스는 이 오브젝트의 자식이 아닌 별도
        /// 최상위 오브젝트라, DontDestroyOnLoad는 이 오브젝트(및 그 시점의 자식들)에만 적용되기
        /// 때문이다. BuildUI()에서 캔버스를 만든 뒤 반드시 this.transform 아래로 옮겨 붙여야
        /// 캔버스도 함께 씬 전환에서 살아남는다(아래 BuildUI 참고).
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
            BuildUI();
            SetOpen(false);
        }

        /// <summary>
        /// 캔버스와 화면 가장자리를 덮는 4개의 비네트 조각(상/하/좌/우)을 생성한다.
        /// 생성된 캔버스는 반드시 이 싱글턴 오브젝트(DontDestroyOnLoad 대상) 아래로 재부모화해서,
        /// 씬이 바뀌어도 캔버스가 파괴되지 않고 함께 살아남게 한다.
        /// </summary>
        private void BuildUI()
        {
            var canvas = UIBuilder.CreateCanvas("CombatFeedbackCanvas", sortOrder: 12);
            // DontDestroyOnLoad는 호출 시점에 gameObject가 속한 계층 전체를 유지해준다. UIBuilder가
            // 만든 캔버스는 처음엔 이 오브젝트의 자식이 아니므로, 여기서 명시적으로 재부모화해야
            // 다음 씬 전환 때 캔버스까지 함께 보존된다.
            canvas.transform.SetParent(transform, false);

            panelRoot = new GameObject("VignetteRoot", typeof(RectTransform));
            panelRoot.transform.SetParent(canvas.transform, false);
            var rootRt = panelRoot.GetComponent<RectTransform>();
            rootRt.anchorMin = Vector2.zero;
            rootRt.anchorMax = Vector2.one;
            rootRt.offsetMin = Vector2.zero;
            rootRt.offsetMax = Vector2.zero;

            // 각 조각은 화면의 한쪽 변에 붙어 그 변을 따라 늘어나도록 서로 다른 anchorMin/Max를 준다
            // (원본 OnGUI의 Rect(0,0,Screen.width,edge) 등 네 방향 계산과 동일한 배치).
            // 상: 위쪽 변에 붙어 좌우로 꽉 참. 두께(offsetMin.y)는 Update()가 매 프레임 갱신한다.
            topRt = CreateEdgePanel(panelRoot.transform, "TopEdge", new Vector2(0f, 1f), new Vector2(1f, 1f), out topImage);
            // 하: 아래쪽 변에 붙어 좌우로 꽉 참.
            bottomRt = CreateEdgePanel(panelRoot.transform, "BottomEdge", new Vector2(0f, 0f), new Vector2(1f, 0f), out bottomImage);
            // 좌: 왼쪽 변에 붙어 위아래로 꽉 참.
            leftRt = CreateEdgePanel(panelRoot.transform, "LeftEdge", new Vector2(0f, 0f), new Vector2(0f, 1f), out leftImage);
            // 우: 오른쪽 변에 붙어 위아래로 꽉 참.
            rightRt = CreateEdgePanel(panelRoot.transform, "RightEdge", new Vector2(1f, 0f), new Vector2(1f, 1f), out rightImage);
        }

        /// <summary>
        /// 가장자리 한 조각(사각형 Image)을 생성한다. anchorMin/anchorMax로 어느 변에 붙어 늘어날지만
        /// 정해두고, 실제 두께는 Update()가 매 프레임(플래시가 진행 중일 때만) offsetMin/Max로 갱신한다.
        /// </summary>
        private RectTransform CreateEdgePanel(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax, out Image image)
        {
            var rt = UIBuilder.CreatePanel(parent, name, anchorMin, anchorMax, Vector2.zero, Vector2.zero, Color.clear);
            image = rt.GetComponent<Image>();
            return rt;
        }

        /// <summary>
        /// 위험요소와 접촉해 전투 피해를 입은 순간 호출한다. 타이머를 최대치로 되돌려
        /// 다음 프레임부터 처음부터 다시 페이드아웃하는 비네트 플래시를 보여주게 한다.
        /// </summary>
        public void TriggerHit()
        {
            flashTimer = flashDuration;
        }

        /// <summary>
        /// 매 프레임 플래시 타이머를 감소시키고, 타이머가 남아있는 동안만 가장자리 네 조각의
        /// 두께/알파를 갱신한다. 타이머가 다 되면 패널 전체를 꺼서 더 이상 갱신하지 않는다.
        /// </summary>
        private void Update()
        {
            if (flashTimer > 0f)
                flashTimer -= Time.deltaTime;

            bool active = flashTimer > 0f;
            if (panelRoot.activeSelf != active)
                SetOpen(active);

            if (!active)
                return;

            float t = Mathf.Clamp01(flashTimer / flashDuration);
            float alpha = maxAlpha * t;
            float edge = Mathf.Lerp(0f, Screen.height * 0.18f, t);
            Color color = new Color(0.8f, 0f, 0f, alpha);

            // 상/하/좌/우 네 개의 얇은 띠로 테두리 비네트를 그린다 (원본 OnGUI의 4개 GUI.DrawTexture와 동일한 배치).
            topImage.color = color;
            topRt.offsetMin = new Vector2(0f, -edge);
            topRt.offsetMax = Vector2.zero;

            bottomImage.color = color;
            bottomRt.offsetMin = Vector2.zero;
            bottomRt.offsetMax = new Vector2(0f, edge);

            leftImage.color = color;
            leftRt.offsetMin = Vector2.zero;
            leftRt.offsetMax = new Vector2(edge, 0f);

            rightImage.color = color;
            rightRt.offsetMin = new Vector2(-edge, 0f);
            rightRt.offsetMax = Vector2.zero;
        }

        /// <summary>
        /// 비네트 패널 전체를 열거나 닫는다.
        /// </summary>
        private void SetOpen(bool open)
        {
            if (panelRoot != null)
                panelRoot.SetActive(open);
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
