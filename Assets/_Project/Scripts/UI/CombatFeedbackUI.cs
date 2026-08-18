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

        [Tooltip("상태 이상이 시작된 순간에 보여줄 플래시의 세기 배수 (피격보다 약하게)")]
        public float statusOnsetStrength = 0.55f;

        [Tooltip("상태 이상 시작 플래시가 사라지기까지 걸리는 시간(초)")]
        public float statusOnsetDuration = 0.5f;

        [Tooltip("피격 세기 구분 기준(약함/보통). 이 값 미만의 피해는 가장 약한 단계로 표시한다.\n실측 HazardSource.GetContactDamage: 독사·전갈·함정 0 / 곰·벌떼 10 / 상어 18")]
        public float lightHitDamage = 6f;

        [Tooltip("피격 세기 구분 기준(보통/강함). 이 값 이상의 피해는 가장 강한 단계로 표시한다.")]
        public float heavyHitDamage = 15f;

        [Tooltip("가장 약한 피격 단계의 플래시 세기 배수 (0 피해도 이 단계로 반드시 표시된다)")]
        public float lightHitStrength = 0.45f;

        [Tooltip("중간 피격 단계의 플래시 세기 배수")]
        public float mediumHitStrength = 0.75f;

        private float flashTimer = 0f;

        // 지금 재생 중인 플래시의 총 길이/색/세기. TriggerHit(C단계 위협)과 TriggerStatusOnset(상태 이상
        // 시작 순간)이 같은 비네트 연출을 공유하되 세기와 색만 다르게 쓰기 위해 필드로 뺐다.
        private float currentFlashDuration = 0.35f;
        private float currentFlashStrength = 1f;
        private Color currentFlashColor = new Color(0.8f, 0f, 0f, 1f);

        private GameObject panelRoot;
        // 화면 가장자리 네 조각(상/하/좌/우)의 RectTransform과 Image. 플래시가 진행되는 동안 두께(edge)와
        // 알파가 함께 줄어들며 옅어지는 비네트 효과를 낸다.
        private RectTransform topRt, bottomRt, leftRt, rightRt;
        private Image topImage, bottomImage, leftImage, rightImage;

        // ── [전투 깊이 확장] 적중 표식(hit marker) ──────────────────────────────────
        //
        // 위의 비네트가 "내가 맞았다"라면 이것은 **"내가 맞혔다"**이다. 두 신호는 절대 섞이면 안 되므로
        // 채널을 완전히 갈라 둔다: 비네트는 화면 가장자리 + Danger Red, 표식은 화면 정중앙 + 흰색이며
        // 처치한 순간에만 붉은색으로 바뀐다. 서로 다른 타이머를 쓰므로 동시에 떠도 간섭하지 않는다.
        //
        // 왜 필요한가: 지금까지 공격의 유일한 피드백은 효과음(PlayHit) 하나였다. 원거리(창 투척)가
        // 생기면서 "맞았는지 빗나갔는지"가 처음으로 불확실해졌는데, 소리만으로는 몇 미터 앞의
        // 명중 여부를 확신할 수 없다. 화면 정중앙 = 조준점 자리가 그 답을 두기에 가장 정확한 곳이다.

        [Tooltip("적중 표식이 사라지기까지 걸리는 시간(초)")]
        public float hitMarkerDuration = 0.18f;

        [Tooltip("대상을 쓰러뜨렸을 때 적중 표식이 사라지기까지 걸리는 시간(초). 보통 적중보다 길게 남는다.")]
        public float hitMarkerDefeatDuration = 0.34f;

        [Tooltip("적중 표식 네 조각이 화면 중앙에서 벌어져 나가는 시작/끝 거리(px)")]
        public float hitMarkerInnerRadius = 9f;

        [Tooltip("적중 표식이 다 벌어졌을 때의 거리(px)")]
        public float hitMarkerOuterRadius = 17f;

        /// <summary>적중 표식 조각 하나의 크기(px). 얇은 막대 네 개가 X자를 이룬다.</summary>
        private static readonly Vector2 HitMarkerDashSize = new Vector2(13f, 3f);

        /// <summary>보통 적중 색(따뜻한 흰색 - Danger Red 계열과 절대 헷갈리지 않는다).</summary>
        private static readonly Color HitMarkerNormalColor = new Color(0.96f, 0.94f, 0.86f, 1f);

        /// <summary>처치 적중 색(ArtDirection의 Danger Red).</summary>
        private static readonly Color HitMarkerDefeatColor = new Color(0.8f, 0.2f, 0.2f, 1f);

        private GameObject hitMarkerRoot;
        private readonly RectTransform[] hitMarkerRts = new RectTransform[4];
        private readonly Image[] hitMarkerImages = new Image[4];
        private float hitMarkerTimer;
        private float currentHitMarkerDuration = 0.18f;
        private float currentHitMarkerScale = 1f;
        private Color currentHitMarkerColor = new Color(0.96f, 0.94f, 0.86f, 1f);

        /// <summary>네 조각의 중심 방향(대각선 네 방향). 여기에 반지름을 곱해 anchoredPosition을 만든다.</summary>
        private static readonly Vector2[] HitMarkerDirections =
        {
            new Vector2(0.7071f, 0.7071f),    // 우상
            new Vector2(-0.7071f, 0.7071f),   // 좌상
            new Vector2(-0.7071f, -0.7071f),  // 좌하
            new Vector2(0.7071f, -0.7071f)    // 우하
        };

        /// <summary>각 조각의 기울기(도). 막대가 대각선 방향을 따라 눕도록 45도씩 준다.</summary>
        private static readonly float[] HitMarkerAngles = { 45f, -45f, 45f, -45f };

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

            BuildHitMarker(canvas.transform);
        }

        /// <summary>
        /// [전투 깊이 확장] 화면 정중앙에 X자 적중 표식 네 조각을 만든다.
        /// 비네트(panelRoot)와 **형제**로 두고 따로 켜고 끈다 - 두 연출의 수명이 서로 다르기 때문이다.
        /// 조각은 클릭을 절대 받으면 안 되므로 raycastTarget을 끈다(화면 정중앙이라 위험하다).
        /// </summary>
        private void BuildHitMarker(Transform canvasTransform)
        {
            hitMarkerRoot = new GameObject("HitMarkerRoot", typeof(RectTransform));
            hitMarkerRoot.transform.SetParent(canvasTransform, false);

            var rootRt = hitMarkerRoot.GetComponent<RectTransform>();
            rootRt.anchorMin = new Vector2(0.5f, 0.5f);
            rootRt.anchorMax = new Vector2(0.5f, 0.5f);
            rootRt.sizeDelta = Vector2.zero;
            rootRt.anchoredPosition = Vector2.zero;

            Vector2 half = HitMarkerDashSize * 0.5f;
            for (int i = 0; i < hitMarkerRts.Length; i++)
            {
                RectTransform rt = UIBuilder.CreatePanel(hitMarkerRoot.transform, "Dash" + i,
                    new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                    -half, half, Color.clear);

                rt.localRotation = Quaternion.Euler(0f, 0f, HitMarkerAngles[i]);
                hitMarkerRts[i] = rt;

                Image image = rt.GetComponent<Image>();
                if (image != null)
                    image.raycastTarget = false;
                hitMarkerImages[i] = image;
            }

            hitMarkerRoot.SetActive(false);
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
        /// [C단계 = 위협] 위험요소와 접촉해 전투 피해를 입은 순간 호출한다. 타이머를 최대치로 되돌려
        /// 다음 프레임부터 처음부터 다시 페이드아웃하는 붉은 비네트 플래시를 보여준다.
        ///
        /// ArtDirection.md 4.2의 3단계 피드백에서 이 클래스가 담당하는 단계는 C(위협) 하나뿐이다:
        /// - A단계(일상: 채집/이동/창 열기) → 화면 이펙트 금지, 짧은 효과음만(AudioManager.PlayPickup 등).
        ///   이 클래스에는 A단계용 API를 일부러 두지 않는다 - API가 있으면 언젠가 누군가 쓰기 때문이다.
        /// - B단계(성취: 제작/설치/조리/치료/취침) → 전용 효과음 + 버튼 자체 피드백. 역시 여기 없음.
        /// - C단계(위협) → 아래 두 메서드. 반드시 "그 순간"에만 호출하고, 허기/일사병처럼 매 프레임
        ///   반복되는 상시 피해에는 절대 걸지 않는다.
        /// </summary>
        public void TriggerHit()
        {
            TriggerHitFlash(1f, flashDuration);
        }

        /// <summary>
        /// [C단계 = 위협, 세기 3단계] 피해량을 아는 호출부(HazardSource.ApplyHazardEffect →
        /// GetContactDamage())가 쓰는 오버로드. 곰 10 / 상어 18 / 벌떼 10 / 독사·전갈·함정 0처럼 위협의
        /// 무게가 실제로 다른데 전부 똑같은 세기로 번쩍이면, 플래시는 "맞았다"만 말하고 "얼마나 위험한
        /// 상황인가"는 말하지 못한다. 세기를 3단계로 나눠 상어에게 물린 것과 함정을 밟은 것이 화면에서
        /// 구분되게 한다.
        ///
        /// **피해 0도 반드시 번쩍인다.** 독사·전갈·함정은 접촉 순간의 직접 피해가 0이지만 중독/골절을
        /// 걸어 오는 진짜 위협이고, 무엇보다 "아무 반응이 없는 것"이 이 프로젝트가 반복해서 저지른
        /// 실패다. 0은 "피격이 아님"이 아니라 **가장 약한 단계**로 다룬다 - 여기서 조용히 return하면
        /// 함정을 밟은 플레이어는 다시 아무 것도 못 보게 된다.
        ///
        /// 무인자 TriggerHit()은 기존 호출부를 위해 그대로 남아 있으며 동작도 예전과 100% 같다(최대 세기).
        /// </summary>
        /// <param name="damage">접촉 순간의 직접 피해량. 0 이하도 유효한 입력이다(가장 약한 단계).</param>
        public void TriggerHit(float damage)
        {
            float strength;
            if (damage < lightHitDamage)
                strength = Mathf.Clamp01(lightHitStrength);
            else if (damage < heavyHitDamage)
                strength = Mathf.Clamp01(mediumHitStrength);
            else
                strength = 1f;

            // 약한 피격은 짧게, 강한 피격은 길게 남는다 - 세기(알파/두께)만 다르면 순간적으로는
            // 구분이 잘 안 되기 때문에 지속 시간도 함께 움직인다.
            TriggerHitFlash(strength, flashDuration * Mathf.Lerp(0.7f, 1.25f, strength));
        }

        /// <summary>
        /// 붉은 피격 비네트를 지정한 세기/길이로 처음부터 재생한다(두 TriggerHit 오버로드의 공통 구현).
        /// 색은 항상 Danger Red 계열로 고정한다 - 색까지 세기별로 바꾸면 상태 이상 플래시와 구분이 흐려진다.
        /// </summary>
        private void TriggerHitFlash(float strength, float duration)
        {
            currentFlashDuration = Mathf.Max(0.05f, duration);
            currentFlashStrength = Mathf.Clamp01(strength);
            currentFlashColor = new Color(0.8f, 0f, 0f, 1f);
            flashTimer = currentFlashDuration;
        }

        /// <summary>
        /// [C단계 = 위협, 약한 세기] 상태 이상(중독/출혈/일사병/익사)이 **시작된 그 순간**에만 호출한다.
        /// 피격보다 약한 세기(statusOnsetStrength)로, 상태 이상 색(중독=연두, 출혈=빨강, 일사병=금색,
        /// 익사=청록)으로 한 번만 번쩍인다 - "지금 뭔가 시작됐다"를 놓치지 않게 하되 피격과 혼동되지 않게
        /// 색과 세기를 구분한다.
        ///
        /// 중요: 상태 이상이 지속되는 동안 매 프레임 호출하면 화면이 계속 번쩍여 피로해진다
        /// (ArtDirection.md 4.2 규칙 위반). 호출부는 반드시 false→true로 바뀐 프레임에만 부른다
        /// (StatusEffectWarningUI가 그렇게 부르고 있다).
        /// </summary>
        public void TriggerStatusOnset(Color effectColor)
        {
            currentFlashDuration = Mathf.Max(0.05f, statusOnsetDuration);
            currentFlashStrength = Mathf.Clamp01(statusOnsetStrength);
            currentFlashColor = effectColor;
            flashTimer = currentFlashDuration;
        }

        /// <summary>
        /// 매 프레임 플래시 타이머를 감소시키고, 타이머가 남아있는 동안만 가장자리 네 조각의
        /// 두께/알파를 갱신한다. 타이머가 다 되면 패널 전체를 꺼서 더 이상 갱신하지 않는다.
        /// </summary>
        /// <summary>
        /// [전투 깊이 확장 · 내가 맞혔을 때] 플레이어의 공격이 대상에 적중한 순간 호출한다.
        /// 화면 정중앙(조준점 자리)에 X자 표식이 짧게 번쩍이고 사라진다.
        ///
        /// **이것은 위 TriggerHit(내가 맞았을 때)과 완전히 다른 신호다.** 색·위치·타이머를 전부
        /// 분리해 두었으니 두 메서드를 서로 대신 쓰지 마라 - 공격이 화면 가장자리를 붉게 물들이면
        /// 플레이어는 자기가 피해를 입은 줄 안다.
        ///
        /// ArtDirection.md 4.2의 3단계 기준으로는 C(위협)가 아니라 **B(성취)에 가까운 신호**라,
        /// 세기를 일부러 작게 잡았다(화면 12% 남짓의 얇은 막대 네 개, 0.18초).
        /// </summary>
        /// <param name="damage">이번 공격이 입힌 피해량. 0도 유효하다(체력이 없는 위험 요소를 맞힌 경우).</param>
        /// <param name="defeated">이 공격으로 대상을 쓰러뜨렸는지. true면 더 크고 길고 붉게 표시한다.</param>
        public void TriggerAttackConfirm(float damage, bool defeated)
        {
            if (hitMarkerRoot == null)
                return;

            currentHitMarkerDuration = Mathf.Max(0.05f, defeated ? hitMarkerDefeatDuration : hitMarkerDuration);
            currentHitMarkerColor = defeated ? HitMarkerDefeatColor : HitMarkerNormalColor;

            // 피해가 클수록 표식이 조금 커진다. 상한을 두어 화면을 뒤덮지 않게 한다
            // (기준 18 = 창의 근접 피해량. 그보다 세면 1.35배에서 멈춘다).
            float weight = Mathf.Clamp01(Mathf.Max(0f, damage) / 18f);
            currentHitMarkerScale = defeated ? 1.5f : Mathf.Lerp(0.85f, 1.35f, weight);

            hitMarkerTimer = currentHitMarkerDuration;
        }

        /// <summary>
        /// 적중 표식을 매 프레임 갱신한다. 비네트와 **완전히 독립된 타이머**를 쓰며, 같은 이유로
        /// unscaledDeltaTime을 쓴다(사망/엔딩 화면이 timeScale 0을 걸면 표식이 화면에 얼어붙는다).
        /// </summary>
        private void UpdateHitMarker()
        {
            if (hitMarkerRoot == null)
                return;

            if (hitMarkerTimer > 0f)
                hitMarkerTimer -= Time.unscaledDeltaTime;

            bool active = hitMarkerTimer > 0f;
            if (hitMarkerRoot.activeSelf != active)
                hitMarkerRoot.SetActive(active);

            if (!active)
                return;

            // t = 1(방금 맞힘) → 0(사라짐). 알파는 t를 따라 줄고, 네 조각은 바깥으로 벌어진다.
            float t = Mathf.Clamp01(hitMarkerTimer / Mathf.Max(0.01f, currentHitMarkerDuration));
            float radius = Mathf.Lerp(hitMarkerOuterRadius, hitMarkerInnerRadius, t) * currentHitMarkerScale;
            Color color = new Color(currentHitMarkerColor.r, currentHitMarkerColor.g, currentHitMarkerColor.b, t);

            for (int i = 0; i < hitMarkerRts.Length; i++)
            {
                RectTransform rt = hitMarkerRts[i];
                if (rt == null)
                    continue;

                rt.anchoredPosition = HitMarkerDirections[i] * radius;
                rt.sizeDelta = HitMarkerDashSize * currentHitMarkerScale;

                if (hitMarkerImages[i] != null)
                    hitMarkerImages[i].color = color;
            }
        }

        private void Update()
        {
            UpdateHitMarker();

            // 버그 수정(Design_Ending.md 1장 제약 A): Time.deltaTime을 쓰면 timeScale = 0인 동안 타이머가
            // 절대 줄지 않는다. 플레이어가 피격과 동시에 죽으면 GameOverController가 즉시 timeScale = 0을
            // 걸기 때문에, 이 붉은 비네트가 최대 알파 그대로 화면에 영구히 얼어붙어 사망 화면을 덮고 있었다
            // (엔딩 트리거도 동일하게 timeScale = 0을 건다). unscaledDeltaTime으로 세면 멈춘 화면 위에서도
            // 정상적으로 페이드아웃돼 사라진다.
            if (flashTimer > 0f)
                flashTimer -= Time.unscaledDeltaTime;

            bool active = flashTimer > 0f;
            if (panelRoot.activeSelf != active)
                SetOpen(active);

            if (!active)
                return;

            // 진행률 t는 "이번 플래시의 길이"(피격 0.35초 / 상태 이상 시작 0.5초)로 나눈다.
            float t = Mathf.Clamp01(flashTimer / Mathf.Max(0.01f, currentFlashDuration));
            float alpha = maxAlpha * currentFlashStrength * t;
            float edge = Mathf.Lerp(0f, Screen.height * 0.18f * currentFlashStrength, t);
            Color color = new Color(currentFlashColor.r, currentFlashColor.g, currentFlashColor.b, alpha);

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
