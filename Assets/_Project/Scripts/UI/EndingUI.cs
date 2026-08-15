using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using MakeGame.Data;
using MakeGame.Player;
using MakeGame.Systems;

namespace MakeGame.UI
{
    /// <summary>
    /// 정식 UGUI 기반 엔딩(승리) 연출 화면.
    /// 개선(B3-1): EndingChecker(Systems 소유, 두 엔딩 경로 - 배 탈출 / 경비행기 수리 - 의 달성 판정만
    /// 담당해야 한다)가 지금까지 OnGUI로 화면까지 직접 그려온 것은 GameOverController와 동일한 역할
    /// 경계 위반이었다. 이 클래스가 화면 렌더링을 전담하고, EndingChecker는 상태(IsShowingEnding,
    /// EndingMessage)와 동작(DismissEndingUI)만 노출한다.
    /// GameOverUI와 동일하게 씬에 미리 배치하지 않고 스스로 생성된다: sceneLoaded 이벤트를 구독해
    /// 씬이 다시 로드되더라도(재시작 등) 그때마다 새 EndingUI가 새 EndingChecker 참조로 생성되게 한다.
    /// 주의(코디네이터 지시): 이번에는 EndingChecker.OnGUI 제거를 "나중에" 미루지 않는다 - 이전에
    /// GameOverController.OnGUI를 잠시 남겨뒀다가 IMGUI가 Screen Space Overlay Canvas를 전부 가려버리는
    /// 회귀가 실제로 발생했던 사례(GameOverUI 규정)와 같은 문제를 여기서도 반복하지 않기 위함이다.
    ///
    /// 연출(Design_Ending.md 3장): 조건이 충족된 순간 문장 한 줄만 띄우고 끝나던 화면을 5페이즈
    /// 시퀀스(암전 → 배경+제목 → 통계 순차 공개 → 마지막 문장 → 계속하기, 총 약 6.5초)로 바꿨다.
    ///
    /// **이 파일에서 절대 Time.deltaTime을 쓰면 안 된다.** EndingChecker.TriggerEnding은 엔딩이 확정된
    /// 프레임에 즉시 Time.timeScale = 0을 건다(EndingChecker.TriggerEnding 참고). 그 뒤로 deltaTime은
    /// 계속 0이므로, deltaTime 기반 타이머/보간은 첫 프레임에서 그대로 얼어붙고 연출 전체가 정지
    /// 화면이 된다 - CombatFeedbackUI가 같은 이유로 사망 화면을 붉게 덮은 채 굳었던 실제 사고가 있다
    /// (CombatFeedbackUI의 unscaledDeltaTime 수정 주석 참고). 여기서는 모든 타이머를
    /// Time.unscaledDeltaTime / WaitForSecondsRealtime으로만 짠다.
    /// </summary>
    public class EndingUI : MonoBehaviour
    {
        // ── 연출 길이(전부 실시간 초). 합계 약 6.5초 + 통계 6줄 리듬 ──────────────────────────
        private const float BlackoutDuration = 1.0f;      // 페이즈 1: 암전
        private const float BackgroundFadeDuration = 0.6f; // 페이즈 2: 배경 크로스페이드
        private const float TitleFadeDuration = 0.9f;      // 페이즈 2: 제목
        private const float StatFadeDuration = 0.25f;      // 페이즈 3: 통계 한 줄이 떠오르는 시간
        private const float StatInterval = 0.4f;           // 페이즈 3: 줄 사이 간격(문서 지정값)
        private const float BeforeClosingDelay = 0.3f;     // 페이즈 3 → 4 사이의 숨
        private const float ClosingFadeDuration = 0.8f;    // 페이즈 4: 마지막 문장
        private const float FooterFadeDuration = 0.5f;     // 페이즈 4: 안내 문구 + 계속하기 버튼

        /// <summary>통계 줄 개수(Design_Ending.md 3장 페이즈 3 표 - 6항목 고정).</summary>
        private const int StatRowCount = 6;

        // ── 팔레트(ArtDirection.md 1.1, Design_Ending.md 2장) ────────────────────────────────
        private static readonly Color TitleGold = new Color(1f, 0.85f, 0.2f, 1f);
        private static readonly Color BodyGray = new Color(0.85f, 0.85f, 0.85f, 1f);
        private static readonly Color UnknownGray = new Color(0.8f, 0.8f, 0.8f, 0.4f);   // Neutral Gray #CCCCCC 알파 0.4
        private static readonly Color DeepOcean = new Color(0.102f, 0.349f, 0.549f, 1f); // #1A598C
        private static readonly Color DaySkyTint = new Color(0.451f, 0.651f, 0.851f, 1f);// #73A6D9
        private static readonly Color DuskDawn = new Color(1f, 0.6f, 0.35f, 1f);         // #FF9959

        /// <summary>공개되지 않은(또는 아직 집계 수단이 없는) 통계 값 자리에 넣는 흐린 대시.</summary>
        private const string UnknownValue = "— — —";

        private EndingChecker endingChecker;
        private SurvivalClock survivalClock;
        private WorldMapManager worldMapManager;
        private CraftingSystem craftingSystem;
        private BoatConstructionSystem boatConstruction;
        private PlayerSkills playerSkills;

        // [B8 디렉터] "겪은 위기" 통계용. systems가 SurvivalStats.CrisisCount를 실제로 만들었다.
        private SurvivalStats survivalStats;

        private GameObject panelRoot;
        private CanvasGroup blackoutGroup;
        private CanvasGroup backgroundGroup;
        private Image backgroundImage;
        private RawImage backgroundArt;
        private CanvasGroup titleGroup;
        private Text titleLabel;
        private CanvasGroup closingGroup;
        private Text closingLabel;
        private CanvasGroup footerGroup;
        private Text hintLabel;

        private readonly CanvasGroup[] statGroups = new CanvasGroup[StatRowCount];
        private readonly Text[] statLabels = new Text[StatRowCount];
        private readonly float[] statTargetAlpha = new float[StatRowCount];

        private Coroutine sequenceRoutine;
        private Sprite gradientSprite;

        /// <summary>연출이 진행 중인지(=아직 마지막 페이즈에 도달하지 않았는지).</summary>
        private bool presenting = false;

        /// <summary>
        /// 외부에서 주입된 "제작한 물건 종류 수". 음수면 미주입이라는 뜻이고, 그때는 씬의
        /// CraftingSystem.CraftedRecipeCount를 직접 읽는다(GameOverUI와 동일한 규칙).
        /// </summary>
        private int injectedCraftedKindCount = -1;

        // EndingChecker.showEndingUI(→IsShowingEnding)가 false→true로 바뀐 프레임에만 메시지를 다시
        // 채우고 패널을 연다(#7/#8/GameOverUI와 동일한, 바뀌지 않는 상태에서 매 프레임 다시 그리지
        // 않는 캐싱 패턴). 엔딩 화면은 Dismiss 후에도 자유 플레이가 계속되므로(EndingChecker 주석 참고)
        // true→false 전환도 감지해 다시 닫아준다.
        private bool lastShowing = false;

        /// <summary>
        /// 씬이 로드될 때마다(최초 시작이든 재시작이든) 새 EndingUI를 생성한다.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Bootstrap()
        {
            SceneManager.sceneLoaded += (scene, mode) =>
            {
                var go = new GameObject("EndingUI");
                go.AddComponent<EndingUI>();
            };
        }

        /// <summary>
        /// 씬에서 EndingChecker와 통계 출처 컴포넌트들을 찾아 참조를 캐시하고 UI를 생성한다.
        /// 기본적으로는 닫힌 상태로 둔다. EndingChecker가 이미 들고 있는 참조(survivalClock,
        /// boatConstruction)를 우선 재사용해, 같은 값을 서로 다른 인스턴스에서 읽을 여지를 없앤다.
        /// </summary>
        private void Start()
        {
            endingChecker = FindAnyObjectByType<EndingChecker>();

            survivalClock = endingChecker != null && endingChecker.survivalClock != null
                ? endingChecker.survivalClock
                : FindAnyObjectByType<SurvivalClock>();
            boatConstruction = endingChecker != null && endingChecker.boatConstruction != null
                ? endingChecker.boatConstruction
                : FindAnyObjectByType<BoatConstructionSystem>();
            worldMapManager = FindAnyObjectByType<WorldMapManager>();
            craftingSystem = FindAnyObjectByType<CraftingSystem>();
            playerSkills = FindAnyObjectByType<PlayerSkills>();
            survivalStats = FindAnyObjectByType<SurvivalStats>();

            BuildUI();
            SetOpen(false);
        }

        /// <summary>
        /// "제작한 물건 종류 수"를 외부에서 주입한다(GameOverUI.SetCraftedKindCount와 같은 목적/규칙).
        /// 평소에는 이 UI가 CraftingSystem.CraftedRecipeCount를 직접 읽으므로 호출할 필요가 없지만,
        /// 세션 밖에서 집계한 값을 대신 넣고 싶을 때를 위해 주입 지점을 남겨둔다. 음수를 넣으면
        /// "직접 읽기"로 되돌아간다.
        /// </summary>
        public void SetCraftedKindCount(int count)
        {
            injectedCraftedKindCount = count;
        }

        /// <summary>
        /// 연출에 필요한 요소를 전부 미리 만들어 둔다(알파 0). 페이즈 진행은 알파만 건드리므로
        /// 연출 도중에 GameObject를 새로 만들지 않는다.
        /// 그리는 순서(=자식 순서)가 곧 레이어다: 배경 아트/색 → 암전 → 배경(엔딩색) → 글자.
        /// 암전 위에 엔딩 배경을 올려두고 그 알파만 0→1로 올리면, 검정에서 배경색으로 넘어가는
        /// 크로스페이드가 보간 하나로 끝난다(암전 알파를 되돌릴 필요가 없다).
        /// </summary>
        private void BuildUI()
        {
            // GameOverCanvas(20)보다 위에 있어야 한다 - 엔딩은 게임 오버와 동시에 일어날 수 없는
            // 배타적 결말이지만, 다른 모든 HUD/메뉴 캔버스보다는 항상 위에 그려져야 하므로 그보다도
            // 높은 sortOrder를 준다.
            var canvas = UIBuilder.CreateCanvas("EndingCanvas", sortOrder: 21);

            var root = UIBuilder.CreatePanel(
                canvas.transform, "EndingPanel",
                anchorMin: Vector2.zero, anchorMax: Vector2.one,
                offsetMin: Vector2.zero, offsetMax: Vector2.zero,
                color: Color.clear);
            panelRoot = root.gameObject;

            // 페이즈 1 - 암전. 프리미티브 월드를 그대로 배경에 남겨두면 "게임이 멈춘 화면"으로 읽히므로
            // 완전히 덮어 지운다(Design_Ending.md 3장 페이즈 1).
            var blackout = UIBuilder.CreatePanel(root, "Blackout", Vector2.zero, Vector2.one,
                Vector2.zero, Vector2.zero, Color.black);
            blackoutGroup = blackout.gameObject.AddComponent<CanvasGroup>();
            blackoutGroup.alpha = 0f;

            // 페이즈 2 - 엔딩별 배경. 컨테이너 하나에 (있으면) 섬 컨셉 아트 + 엔딩 색을 겹쳐 담고,
            // 컨테이너의 CanvasGroup 알파만 올려서 암전 위로 떠오르게 한다.
            var background = UIBuilder.CreatePanel(root, "EndingBackground", Vector2.zero, Vector2.one,
                Vector2.zero, Vector2.zero, Color.clear);
            backgroundGroup = background.gameObject.AddComponent<CanvasGroup>();
            backgroundGroup.alpha = 0f;

            var backgroundTexture = Resources.Load<Texture2D>("UI/title_background");
            if (backgroundTexture != null)
            {
                // 탈출에 성공했으니 떠나온 섬을 배경으로 깔아둔다(MainMenuController.BuildUI와 동일한
                // AspectRatioFitter.EnvelopeParent 기법 - 원본 GUI.DrawTexture(..., ScaleAndCrop) 재현).
                // 그 위에 엔딩 색을 거의 불투명하게 덮으므로, 아트는 질감으로만 남는다.
                var bgGo = new GameObject("BackgroundArt", typeof(RectTransform), typeof(RawImage), typeof(AspectRatioFitter));
                bgGo.transform.SetParent(background, false);
                var bgRt = bgGo.GetComponent<RectTransform>();
                bgRt.anchorMin = Vector2.zero;
                bgRt.anchorMax = Vector2.one;
                bgRt.offsetMin = Vector2.zero;
                bgRt.offsetMax = Vector2.zero;

                backgroundArt = bgGo.GetComponent<RawImage>();
                backgroundArt.texture = backgroundTexture;

                var fitter = bgGo.GetComponent<AspectRatioFitter>();
                fitter.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
                fitter.aspectRatio = (float)backgroundTexture.width / backgroundTexture.height;
            }

            var tintRt = UIBuilder.CreatePanel(background, "EndingTint", Vector2.zero, Vector2.one,
                Vector2.zero, Vector2.zero, Color.black);
            backgroundImage = tintRt.GetComponent<Image>();

            // 페이즈 2 - 제목. 문구는 ApplyEndingMessage가 채운다(엔딩별로 다르다).
            titleLabel = UIBuilder.CreateText(root, "Title", "", 48, TitleGold, TextAnchor.MiddleCenter);
            PositionCentered(titleLabel.rectTransform, yOffset: 230f, width: 0f, height: 70f);
            titleGroup = titleLabel.gameObject.AddComponent<CanvasGroup>();
            titleGroup.alpha = 0f;

            // 페이즈 3 - 통계 6줄. 한 줄씩 0.4초 간격으로 떠오르는 이 순차성이 이 연출의 유일한
            // "움직임"이다(3D 모델 0개 / 컷신 불가라는 전제 위에서 낼 수 있는 유일한 시간감).
            for (int i = 0; i < StatRowCount; i++)
            {
                var row = UIBuilder.CreateText(root, $"Stat{i}", "", 18, BodyGray, TextAnchor.MiddleCenter);
                PositionCentered(row.rectTransform, yOffset: 130f - i * 34f, width: 0f, height: 30f);
                statLabels[i] = row;
                statGroups[i] = row.gameObject.AddComponent<CanvasGroup>();
                statGroups[i].alpha = 0f;
                statTargetAlpha[i] = 1f;
            }

            // 페이즈 4 - 마지막 문장.
            closingLabel = UIBuilder.CreateText(root, "Closing", "", 20, Color.white, TextAnchor.MiddleCenter);
            PositionCentered(closingLabel.rectTransform, yOffset: -110f, width: 0f, height: 40f);
            closingGroup = closingLabel.gameObject.AddComponent<CanvasGroup>();
            closingGroup.alpha = 0f;

            // 페이즈 4 - 안내 문구 + 계속하기 버튼. 연출 도중에는 알파 0이자 raycast를 받지 않게 해서
            // "보이지 않는 버튼이 눌리는" 상태를 만들지 않는다.
            var footer = UIBuilder.CreatePanel(root, "Footer", Vector2.zero, Vector2.one,
                Vector2.zero, Vector2.zero, Color.clear);
            footerGroup = footer.gameObject.AddComponent<CanvasGroup>();
            footerGroup.alpha = 0f;
            footerGroup.interactable = false;
            footerGroup.blocksRaycasts = false;

            string keyLabel = endingChecker != null ? endingChecker.continueKey.ToString() : "Space";
            hintLabel = UIBuilder.CreateText(footer, "Hint", $"[{keyLabel}] 키를 눌러 계속하기", 16, BodyGray, TextAnchor.MiddleCenter);
            PositionCentered(hintLabel.rectTransform, yOffset: -165f, width: 0f, height: 30f);

            // 기존 키 입력(continueKey, 기본 Space)은 EndingChecker.Update()가 그대로 처리하므로 여기서는
            // 화면 표시만 담당하지만, 키보드가 없는 입력 방식(터치/게임패드 커서 등)도 지원하기 위해
            // 클릭으로도 계속할 수 있는 버튼을 추가로 둔다(GameOverUI의 재시작 버튼과 동일한 이유).
            var continueButton = UIBuilder.CreateButton(footer, "ContinueButton", $"계속하기 ({keyLabel})", OnContinueClicked);
            PositionCentered(continueButton.GetComponent<RectTransform>(), yOffset: -220f, width: 240f, height: 48f);
        }

        /// <summary>
        /// 화면 가로 중앙(width가 0이면 좌우로 꽉 채움) 기준, 수직 중심에서 yOffset만큼 위/아래로
        /// 떨어진 위치에 지정한 크기로 배치한다(MainMenuController.PositionCentered와 동일한 헬퍼).
        /// </summary>
        private void PositionCentered(RectTransform rt, float yOffset, float width, float height)
        {
            if (width <= 0f)
            {
                rt.anchorMin = new Vector2(0f, 0.5f);
                rt.anchorMax = new Vector2(1f, 0.5f);
                rt.sizeDelta = new Vector2(0f, height);
            }
            else
            {
                rt.anchorMin = new Vector2(0.5f, 0.5f);
                rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.sizeDelta = new Vector2(width, height);
            }

            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2(0f, yOffset);
        }

        /// <summary>
        /// EndingChecker.IsShowingEnding이 false→true 혹은 true→false로 바뀐 프레임을 감지해 그때만
        /// 패널을 열거나 닫고, 여는 순간에 문구/통계를 채운 뒤 연출 시퀀스를 시작한다.
        /// 연출 도중 아무 키나 누르면 마지막 페이즈로 즉시 건너뛴다 - 두 번째 플레이에서 7초를
        /// 강제로 앉혀두면 연출이 벌칙이 된다(Design_Ending.md 3장 스킵).
        /// continueKey(기본 Space)만은 예외로 두는데, 그 키는 EndingChecker.Update()가 같은 프레임에
        /// DismissEndingUI로 처리하는 "화면 닫기" 키이기 때문이다(아래 OnContinueClicked 주석 참고).
        /// </summary>
        private void Update()
        {
            if (endingChecker == null)
                return;

            bool showing = endingChecker.IsShowingEnding;
            if (showing != lastShowing)
            {
                lastShowing = showing;

                if (showing)
                    BeginPresentation();
                else
                    ClosePresentation();

                return;
            }

            if (!showing || !presenting)
                return;

            if (Input.anyKeyDown && !Input.GetKeyDown(endingChecker.continueKey))
                SkipToFinalState();
        }

        /// <summary>
        /// 엔딩 문구/통계를 채우고 패널을 연 뒤, 페이즈 시퀀스 코루틴을 시작한다.
        /// </summary>
        private void BeginPresentation()
        {
            bool isAircraft = ResolveIsAircraftEnding(endingChecker.EndingMessage);

            ApplyEndingMessage(endingChecker.EndingMessage, isAircraft);
            ApplyEndingBackground(isAircraft);
            RefreshStats(isAircraft);

            ResetPresentationState();
            SetOpen(true);

            presenting = true;
            if (sequenceRoutine != null)
                StopCoroutine(sequenceRoutine);
            sequenceRoutine = StartCoroutine(PlaySequence());
        }

        /// <summary>
        /// 화면이 닫힐 때(계속하기) 진행 중인 연출을 정리한다. 코루틴을 멈추지 않으면 패널이 닫힌 뒤에도
        /// 알파를 계속 건드리게 되고, 두 번째로 열릴 때 중간 상태에서 시작하는 화면이 나온다.
        /// </summary>
        private void ClosePresentation()
        {
            if (sequenceRoutine != null)
            {
                StopCoroutine(sequenceRoutine);
                sequenceRoutine = null;
            }

            presenting = false;
            SetOpen(false);
        }

        /// <summary>모든 요소를 "아직 아무것도 보이지 않는" 시작 상태로 되돌린다.</summary>
        private void ResetPresentationState()
        {
            SetGroupAlpha(blackoutGroup, 0f);
            SetGroupAlpha(backgroundGroup, 0f);
            SetGroupAlpha(titleGroup, 0f);
            SetGroupAlpha(closingGroup, 0f);
            SetGroupAlpha(footerGroup, 0f);

            if (footerGroup != null)
            {
                footerGroup.interactable = false;
                footerGroup.blocksRaycasts = false;
            }

            for (int i = 0; i < StatRowCount; i++)
                SetGroupAlpha(statGroups[i], 0f);
        }

        /// <summary>
        /// 5페이즈 시퀀스(Design_Ending.md 3장). **모든 대기가 실시간 기준이다** -
        /// Time.timeScale이 0이므로 WaitForSeconds/Time.deltaTime은 영원히 진행되지 않는다.
        /// </summary>
        private IEnumerator PlaySequence()
        {
            // 페이즈 1 - 암전(0.0s → 1.0s)
            yield return FadeGroup(blackoutGroup, 0f, 1f, BlackoutDuration);

            // 페이즈 2 - 배경 크로스페이드 + 제목(1.0s → 2.5s)
            yield return FadeGroup(backgroundGroup, 0f, 1f, BackgroundFadeDuration);
            yield return FadeGroup(titleGroup, 0f, 1f, TitleFadeDuration);

            // 페이즈 3 - 통계 순차 공개(2.5s → 5.0s). 한 줄이 떠오른 뒤 다음 줄까지 0.4초 간격.
            for (int i = 0; i < StatRowCount; i++)
            {
                yield return FadeGroup(statGroups[i], 0f, statTargetAlpha[i], StatFadeDuration);

                float gap = StatInterval - StatFadeDuration;
                if (gap > 0f)
                    yield return new WaitForSecondsRealtime(gap);
            }

            yield return new WaitForSecondsRealtime(BeforeClosingDelay);

            // 페이즈 4 - 마지막 문장 + 계속하기
            yield return FadeGroup(closingGroup, 0f, 1f, ClosingFadeDuration);
            yield return FadeGroup(footerGroup, 0f, 1f, FooterFadeDuration);

            EnableFooterInput();
            presenting = false;
            sequenceRoutine = null;
        }

        /// <summary>
        /// CanvasGroup 알파를 from에서 to까지 실시간으로 보간한다.
        /// Time.unscaledDeltaTime을 쓰는 것이 이 클래스의 핵심 제약이다(클래스 주석 참고).
        /// </summary>
        private IEnumerator FadeGroup(CanvasGroup group, float from, float to, float duration)
        {
            if (group == null || duration <= 0f)
            {
                SetGroupAlpha(group, to);
                yield break;
            }

            group.alpha = from;

            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                group.alpha = Mathf.Lerp(from, to, Mathf.Clamp01(elapsed / duration));
                yield return null;
            }

            group.alpha = to;
        }

        /// <summary>
        /// 연출을 건너뛰고 마지막 페이즈의 화면 상태로 즉시 점프한다(문서 3장 "스킵").
        /// 연출을 "끝난 것"으로 처리하므로 계속하기 버튼도 이 시점에 활성화된다.
        /// </summary>
        private void SkipToFinalState()
        {
            if (sequenceRoutine != null)
            {
                StopCoroutine(sequenceRoutine);
                sequenceRoutine = null;
            }

            SetGroupAlpha(blackoutGroup, 1f);
            SetGroupAlpha(backgroundGroup, 1f);
            SetGroupAlpha(titleGroup, 1f);
            SetGroupAlpha(closingGroup, 1f);
            SetGroupAlpha(footerGroup, 1f);

            for (int i = 0; i < StatRowCount; i++)
                SetGroupAlpha(statGroups[i], statTargetAlpha[i]);

            EnableFooterInput();
            presenting = false;
        }

        /// <summary>계속하기 버튼이 실제로 눌릴 수 있게 만든다(연출이 끝났거나 건너뛴 뒤).</summary>
        private void EnableFooterInput()
        {
            if (footerGroup == null)
                return;

            footerGroup.interactable = true;
            footerGroup.blocksRaycasts = true;
        }

        /// <summary>null 안전한 CanvasGroup 알파 설정.</summary>
        private static void SetGroupAlpha(CanvasGroup group, float alpha)
        {
            if (group != null)
                group.alpha = alpha;
        }

        /// <summary>
        /// EndingChecker가 넘겨준 문구에서 제목과 마지막 문장을 갈라 화면에 배치한다.
        ///
        /// 기대 형식(systems 쪽에 요청한 형식): "&lt;엔딩 id&gt;|&lt;제목&gt;|&lt;마지막 문장&gt;"
        ///   배   : "boat|귀환|당신은 준비된 채로 떠났다."
        ///   비행기: "aircraft|탈출|섬은 아직 그 자리에 있다."
        /// 2조각("제목|문장")만 와도 동작하고, 구분자가 없는 기존 한 줄 문구
        /// ("배를 타고 섬을 탈출했습니다!")가 그대로 와도 동작한다 - 그때는 그 문장을 마지막 문장
        /// 자리에 놓고 제목만 엔딩 종류에 맞춰 채운다. **문구 결정권은 EndingChecker 호출부에 있고
        /// (그 파일은 이 에이전트 소유가 아니다), 이 UI는 어떤 형식이 와도 깨지지 않게만 만든다.**
        /// </summary>
        private void ApplyEndingMessage(string raw, bool isAircraft)
        {
            string title = null;
            string closing = null;

            // [B8 디렉터] systems가 EndingChecker에 EndingTitle / EndingSubtitle / AchievedEnding 세
            // 프로퍼티를 실제로 만들었다. 문자열을 쪼개는 것보다 이쪽이 정확하다 - 구분자 방식은
            // 문구에 그 문자가 섞이는 날 조용히 깨진다(systems가 같은 이유로 구분자 형식을 거부했다).
            // 아래 파싱 경로는 두 프로퍼티가 비어 있을 때만 도는 폴백으로 남긴다.
            if (endingChecker != null
                && !string.IsNullOrEmpty(endingChecker.EndingTitle)
                && !string.IsNullOrEmpty(endingChecker.EndingSubtitle))
            {
                title = endingChecker.EndingTitle.Trim();
                closing = endingChecker.EndingSubtitle.Trim();
            }
            else if (!string.IsNullOrEmpty(raw))
            {
                string[] parts = raw.Split('|');
                if (parts.Length >= 3)
                {
                    title = parts[1].Trim();
                    closing = parts[2].Trim();
                }
                else if (parts.Length == 2)
                {
                    title = parts[0].Trim();
                    closing = parts[1].Trim();
                }
                else
                {
                    closing = raw.Trim();
                }
            }

            if (string.IsNullOrEmpty(title))
                title = isAircraft ? "탈출" : "귀환";

            if (string.IsNullOrEmpty(closing))
                closing = isAircraft ? "섬은 아직 그 자리에 있다." : "당신은 준비된 채로 떠났다.";

            if (titleLabel != null)
                titleLabel.text = title;

            if (closingLabel != null)
                closingLabel.text = closing;
        }

        /// <summary>
        /// 이번 엔딩이 경비행기(탈출)인지 배(귀환)인지 판정한다. 판정 근거는 세 단계로 내려간다.
        /// (1) 문구 앞에 엔딩 id가 붙어 있으면 그대로 신뢰한다.
        /// (2) 없으면 기존 한 줄 문구의 키워드로 가른다("경비행기/하늘" vs "배").
        /// (3) 그것도 아니면 시스템 상태(AircraftRepairSystem.isRepairComplete)를 본다.
        /// 판정 자체가 게임 규칙을 바꾸지는 않는다 - 배경색과 통계 공개량만 고른다.
        /// </summary>
        private bool ResolveIsAircraftEnding(string raw)
        {
            // [B8 디렉터] EndingChecker.AchievedEnding이 실제로 들어왔다. 통계 공개량(배 6항목 /
            // 비행기 3항목)과 배경색을 문구 키워드로 추론하던 것을 상태 판정으로 바꾼다.
            // 문구가 나중에 바뀌어도 공개량이 조용히 뒤집히지 않는다.
            if (endingChecker != null && endingChecker.AchievedEnding != EndingKind.None)
                return endingChecker.AchievedEnding == EndingKind.Aircraft;

            if (!string.IsNullOrEmpty(raw))
            {
                string[] parts = raw.Split('|');
                if (parts.Length >= 3)
                {
                    string id = parts[0].Trim().ToLowerInvariant();
                    if (id == "aircraft")
                        return true;
                    if (id == "boat")
                        return false;
                }

                if (raw.Contains("경비행기") || raw.Contains("하늘"))
                    return true;
                if (raw.Contains("배를") || raw.Contains("배 "))
                    return false;
            }

            var aircraft = endingChecker != null ? endingChecker.aircraftRepair : null;
            return aircraft != null && aircraft.isRepairComplete;
        }

        /// <summary>
        /// 엔딩별 배경색을 적용한다(Design_Ending.md 2장/3장 - 전부 DayNightCycle 실측 색과 팔레트 안의 값).
        /// 비행기: Deep Ocean #1A598C → daySkyTint #73A6D9 세로 그라데이션(하늘로 올라가는 방향).
        /// 배: duskDawnColor #FF9959 단색(수평선의 노을).
        /// 배경 아트가 있으면 색을 알파 0.9로 덮어 아트를 질감 정도로만 남긴다.
        /// </summary>
        private void ApplyEndingBackground(bool isAircraft)
        {
            if (backgroundImage == null)
                return;

            float alpha = backgroundArt != null ? 0.9f : 1f;

            if (isAircraft)
            {
                if (gradientSprite == null)
                    gradientSprite = CreateVerticalGradientSprite(DeepOcean, DaySkyTint);

                backgroundImage.sprite = gradientSprite;
                backgroundImage.type = Image.Type.Simple;
                backgroundImage.color = new Color(1f, 1f, 1f, alpha);
            }
            else
            {
                backgroundImage.sprite = null;
                backgroundImage.color = new Color(DuskDawn.r, DuskDawn.g, DuskDawn.b, alpha);
            }
        }

        /// <summary>
        /// 아래→위로 두 색이 이어지는 1x2 텍스처를 만들어 스프라이트로 감싼다. 그라데이션 이미지
        /// 에셋이 없는 프로젝트(프리미티브 전제)에서 화면 전체 그라데이션을 만드는 가장 싼 방법이고,
        /// Bilinear + Clamp 조합이 두 픽셀 사이를 부드럽게 늘려준다.
        /// </summary>
        private static Sprite CreateVerticalGradientSprite(Color bottom, Color top)
        {
            var texture = new Texture2D(1, 2, TextureFormat.RGBA32, false);
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.filterMode = FilterMode.Bilinear;
            texture.SetPixel(0, 0, bottom);
            texture.SetPixel(0, 1, top);
            texture.Apply();

            return Sprite.Create(texture, new Rect(0f, 0f, 1f, 2f), new Vector2(0.5f, 0.5f), 100f);
        }

        /// <summary>
        /// 통계 6줄을 채우고, 각 줄이 최종적으로 도달할 알파를 정한다(Design_Ending.md 3장 페이즈 3).
        ///
        /// **두 엔딩을 조건이 아니라 공개량으로 차별화한다**: 비행기(탈출)는 1~3번만 공개하고 4~6번은
        /// 흐린 대시로 남겨 "이 섬에 대해 아직 모르는 게 많다"를 칸이 비어 있는 것으로 보여준다.
        /// 배(귀환)는 6줄 전부를 공개한다. 빈칸으로 두지 않는 이유도 문서 그대로다 - 빈칸은 버그로
        /// 보이지만, 흐린 대시는 "여기 뭔가 있는데 네가 채우지 못했다"로 읽힌다.
        /// </summary>
        private void RefreshStats(bool isAircraft)
        {
            SetStatRow(0, "생존 일수", GetSurvivedDaysText(), true);
            SetStatRow(1, "방문한 섬", GetVisitedIslandsText(), true);
            SetStatRow(2, "제작한 물건", GetCraftedKindText(), true);

            // 4~6번: 배 엔딩에서만 공개한다.
            SetStatRow(3, "배 제작 진행", isAircraft ? UnknownValue : GetBoatProgressText(), !isAircraft);
            SetStatRow(4, "채집 스킬 레벨", isAircraft ? UnknownValue : GetHarvestingLevelText(), !isAircraft);
            // [B8 디렉터] 집계가 들어왔다. SurvivalStats.CrisisCount는 "원인별 에피소드 수"다 -
            // 같은 곰에게 연속으로 맞으면 1회, 곰에 맞다가 중독되면 2회(systems 판정 근거는
            // SurvivalStats의 crisisGraceSeconds 주석 참고). 타격 수가 아니라 사건 수다.
            // 참조가 없으면 지어내지 않고 흐린 대시로 되돌아간다.
            SetStatRow(5, "겪은 위기",
                survivalStats != null ? $"{survivalStats.CrisisCount}회" : UnknownValue,
                survivalStats != null && !isAircraft);
        }

        /// <summary>
        /// 통계 한 줄을 채운다. revealed가 false면 줄 전체를 알파 0.4로 흐리게 두어(문서 지정값)
        /// "공개되지 않은 자리"임을 형태로 보여준다.
        /// </summary>
        private void SetStatRow(int index, string label, string value, bool revealed)
        {
            if (index < 0 || index >= StatRowCount || statLabels[index] == null)
                return;

            bool isUnknown = value == UnknownValue;
            string valueText = isUnknown ? ColorTag(UnknownValue, UnknownGray) : value;

            statLabels[index].text = $"{label}   {valueText}";
            statTargetAlpha[index] = revealed ? 1f : 0.4f;
        }

        /// <summary>생존 일수(SurvivalHudUI와 같은 "1일부터 세는" 표기).</summary>
        private string GetSurvivedDaysText()
        {
            return survivalClock != null ? $"{survivalClock.ElapsedDays + 1}일" : UnknownValue;
        }

        /// <summary>방문(발견)한 섬 수 / 전체 섬 수.</summary>
        private string GetVisitedIslandsText()
        {
            if (worldMapManager == null || worldMapManager.islands == null)
                return UnknownValue;

            int total = worldMapManager.islands.Count;
            if (total <= 0)
                return UnknownValue;

            int discovered = 0;
            for (int i = 0; i < total; i++)
            {
                var island = worldMapManager.islands[i];
                if (island != null && (island.isDiscovered || island.isStartingIsland))
                    discovered++;
            }

            return $"{discovered} / {total}";
        }

        /// <summary>
        /// 제작한 물건 종류 수. 주입값이 있으면 그것을, 없으면 CraftingSystem.CraftedRecipeCount를 읽는다.
        /// 이 카운터는 세이브에 저장되지 않으므로 불러오기 이후에는 0부터 다시 센다(집계 정책은
        /// CraftingSystem 소유 - 여기서는 읽어서 보여주기만 한다).
        /// </summary>
        private string GetCraftedKindText()
        {
            if (injectedCraftedKindCount >= 0)
                return $"{injectedCraftedKindCount}종";

            if (craftingSystem != null)
                return $"{craftingSystem.CraftedRecipeCount}종";

            return UnknownValue;
        }

        /// <summary>배 제작 전체 진행률(0~1 → 백분율).</summary>
        private string GetBoatProgressText()
        {
            if (boatConstruction == null)
                return UnknownValue;

            int percent = Mathf.RoundToInt(Mathf.Clamp01(boatConstruction.GetOverallProgress()) * 100f);
            return $"{percent}%";
        }

        /// <summary>채집 스킬 레벨.</summary>
        private string GetHarvestingLevelText()
        {
            return playerSkills != null ? $"{playerSkills.GetLevel(SkillType.Harvesting)}레벨" : UnknownValue;
        }

        /// <summary>
        /// UI.Text의 리치 텍스트 태그로 일부 구간만 다른 색으로 칠한다(GameOverUI와 동일한 헬퍼 -
        /// 라벨을 항목마다 쪼개지 않고 "값이 비어 있음"만 흐리게 표현하기 위한 최소 수단).
        /// </summary>
        private static string ColorTag(string content, Color color)
        {
            return $"<color=#{ColorUtility.ToHtmlStringRGBA(color)}>{content}</color>";
        }

        /// <summary>
        /// 계속하기 버튼 클릭 시 호출한다. Time.timeScale이 0인 상태에서도 UGUI 버튼 클릭(EventSystem의
        /// 포인터 입력 처리)은 Time.deltaTime과 무관하게 매 프레임 정상 동작하므로 눌린다(GameOverUI와
        /// 동일한 이유). EndingChecker.DismissEndingUI()는 상태 플래그와 Time.timeScale, 컨트롤러
        /// 활성화 여부만 되돌리는 멱등(idempotent) 동작이라(두 번 불려도 안전) 별도 재진입 방지 처리는
        /// 필요 없다 - GameOverController.RestartGame()이 씬 리로드 같은 파괴적 동작을 하기 때문에
        /// isRestarting 가드가 필요했던 것과는 성격이 다르다.
        /// </summary>
        private void OnContinueClicked()
        {
            endingChecker?.DismissEndingUI();
        }

        /// <summary>
        /// 패널을 열거나 닫는다.
        /// </summary>
        private void SetOpen(bool open)
        {
            if (panelRoot != null)
                panelRoot.SetActive(open);
        }
    }
}
