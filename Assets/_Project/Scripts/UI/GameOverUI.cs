using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using MakeGame.Player;
using MakeGame.Systems;

namespace MakeGame.UI
{
    /// <summary>
    /// 정식 UGUI 기반 게임 오버 화면.
    /// 개선(B2-12): GameOverController(Systems 소유, 사망 판정/재시작 같은 게임 규칙만 담당해야 한다)가
    /// 지금까지 OnGUI로 화면까지 직접 그려온 것은 역할 경계 위반이었다. 이 클래스가 화면 렌더링을
    /// 전담하고, GameOverController는 상태(isGameOver)와 동작(GetDeathMessage, RestartGame)만 노출한다.
    /// 씬에 미리 배치하지 않고 SurvivalHudUI와 동일한 방식으로 스스로 생성된다: 사망 후 재시작은
    /// GameOverController.RestartGame()이 SceneManager.LoadScene으로 씬 전체를 다시 로드하므로,
    /// AfterSceneLoad(최초 1회만 호출)로 만들면 재시작 후 이 UI가 죽은 참조를 들고 있거나 사라지는
    /// 문제가 생긴다. sceneLoaded 이벤트를 구독해 씬이 몇 번을 다시 로드되더라도 그때마다 새
    /// GameOverUI가 새 GameOverController 참조로 생성되게 한다.
    /// </summary>
    public class GameOverUI : MonoBehaviour
    {
        private GameOverController gameOverController;
        private SurvivalStats survivalStats;
        private SurvivalClock survivalClock;
        private WorldMapManager worldMapManager;
        private CraftingSystem craftingSystem;

        private GameObject panelRoot;
        private Text messageLabel;
        private Text hintLabel;
        private Text statsLabel;

        // 팔레트(ArtDirection.md 1.1/1.3).
        private static readonly Color DangerRed = new Color(0.8f, 0.2f, 0.2f, 1f);      // Danger Red #CC3333
        private static readonly Color BodyGray = new Color(0.85f, 0.85f, 0.85f, 1f);    // 본문 보조 텍스트
        private static readonly Color UnknownGray = new Color(0.8f, 0.8f, 0.8f, 0.4f);  // Neutral Gray #CCCCCC 알파 0.4

        // "제작한 물건 종류 수"(Design_Ending.md 4장). 집계는 systems의 몫이고 이 UI는 표시만 한다.
        // 이제 CraftingSystem.CraftedRecipeCount가 존재하므로 평소에는 그 값을 직접 읽고, 이 필드는
        // SetCraftedKindCount로 값을 밀어 넣고 싶을 때의 덮어쓰기 용도로만 쓴다(음수 = 미주입).
        // 둘 다 없으면 빈칸 대신 흐린 대시(— — —)를 보여준다 - 빈칸은 버그로 보이지만 흐린 대시는
        // "여기 뭔가 있는데 채워지지 않았다"로 읽힌다는 Design_Ending.md 3장(페이즈 3) 결정을 그대로 적용.
        private int craftedKindCount = -1;

        // 게임 오버 상태는 한 씬 인스턴스 안에서 false→true로 딱 한 번만 바뀌고 다시 false로 돌아가지
        // 않는다(재시작하면 씬 전체가 새로 로드되어 이 컴포넌트 자체가 새로 만들어진다). 그래서 "이미
        // 한 번 화면을 띄웠는지"만 기억해두면 충분하고, 그 전환 프레임에만 패널을 열고 사망 메시지를
        // 한 번 그려 넣는다(#7/#8과 동일하게, 바뀌지 않는 상태에서 매 프레임 다시 그리지 않기 위함).
        private bool shown = false;

        // ── 개발 전용 미리보기 상태 ────────────────────────────────────────────────────────────
        // 사망 화면은 실기로 띄우려면 실제로 죽어야 하고, 엔딩 화면은 15일(실질 74분)이 걸려 아예
        // 도달할 수가 없다. 디렉터가 화면만 확인할 수 있는 경로를 QA 도구(DebugHud)에 붙이기 위한 상태다.
        // **게임 상태는 바꾸지 않는다** - GameOverController.isGameOver를 건드리지 않고 shown도 세우지
        // 않아, 미리보기를 닫은 뒤에 진짜 사망이 오면 정상적으로 다시 뜬다.
        // previewMode를 true로 만드는 유일한 입구(DebugPreviewGameOver)가 #if로 출시 빌드에서 사라진다.
        private bool previewMode = false;
        private float previewTimeScaleBackup = 1f;

        /// <summary>
        /// ClosePreview가 timeScale을 되돌려도 되는지 판정할 때 쓰는 엔딩 상태 출처.
        /// 이 UI는 EndingChecker를 원래 참조하지 않지만, **진짜 엔딩 화면이 떠 있는 동안 백업값을
        /// 복원하면 정지해야 할 엔딩 연출이 정속으로 흐른다.** EndingUI.ClosePreview는 이미 반대편
        /// (IsShowingEnding)만 보고 GameOverController.isGameOver를 안 보는 비대칭 상태였다 -
        /// 양쪽 모두 "진짜 결말 화면이 하나라도 떠 있으면 복원하지 않는다"로 통일한다.
        /// 실제로 닫을 때만 지연 탐색한다(EndingUI/EndingChecker 모두 이 컴포넌트보다 늦게 생길 수 있다).
        /// </summary>
        private EndingChecker cachedEndingChecker;

        /// <summary>지금 개발용 미리보기로 화면이 떠 있는지(QA 도구가 토글 상태를 알기 위해 읽는다).</summary>
        public bool IsPreviewing => previewMode;

        /// <summary>
        /// 씬이 로드될 때마다(최초 시작이든 재시작이든) 새 GameOverUI를 생성한다.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Bootstrap()
        {
            SceneManager.sceneLoaded += (scene, mode) =>
            {
                var go = new GameObject("GameOverUI");
                go.AddComponent<GameOverUI>();
            };
        }

        /// <summary>
        /// 씬에서 GameOverController를 찾아 참조를 캐시하고 UI를 생성한다. 기본적으로는 닫힌 상태로 둔다.
        /// </summary>
        private void Start()
        {
            gameOverController = FindAnyObjectByType<GameOverController>();
            // 사인별 회피 힌트를 고르려면 lastDamageCause가 필요하다. GameOverController가 이미 들고 있는
            // 참조를 그대로 쓰고(둘이 서로 다른 SurvivalStats를 볼 여지를 없앤다), 미할당일 때만 씬에서 찾는다.
            survivalStats = gameOverController != null && gameOverController.survivalStats != null
                ? gameOverController.survivalStats
                : FindAnyObjectByType<SurvivalStats>();
            survivalClock = FindAnyObjectByType<SurvivalClock>();
            worldMapManager = FindAnyObjectByType<WorldMapManager>();
            craftingSystem = FindAnyObjectByType<CraftingSystem>();

            BuildUI();
            SetOpen(false);
        }

        /// <summary>
        /// "제작한 물건 종류 수"를 외부에서 주입한다(Design_Ending.md 4장 - 승리 엔딩과 사망 화면이
        /// 공유하는 통계 3항목 중 유일하게 아직 집계되지 않는 값). 집계 자체는 CraftingSystem 쪽 책임이고
        /// 이 UI는 표시만 한다. 사망 화면이 이미 떠 있는 상태에서 늦게 호출돼도 즉시 반영된다.
        /// </summary>
        public void SetCraftedKindCount(int count)
        {
            craftedKindCount = count;

            if (shown)
                RefreshStats();
        }

        /// <summary>
        /// 캔버스와 화면 전체를 덮는 어두운 배경, 제목/사망 원인 안내 텍스트, 재시작 버튼을 생성한다.
        /// 기존 GameOverController.OnGUI가 그리던 배경색(짙은 핏빛 오버레이)·글자 크기·색을 그대로 옮겼다.
        /// </summary>
        private void BuildUI()
        {
            // 다른 UI 캔버스(SurvivalHud=5, Minimap=9, Inventory/Crafting=10, MinimapList=11)보다
            // 항상 위에 그려져야 하므로 그보다 높은 sortOrder를 준다.
            var canvas = UIBuilder.CreateCanvas("GameOverCanvas", sortOrder: 20);

            var panel = UIBuilder.CreatePanel(
                canvas.transform, "GameOverPanel",
                anchorMin: Vector2.zero, anchorMax: Vector2.one,
                offsetMin: Vector2.zero, offsetMax: Vector2.zero,
                color: new Color(0.12f, 0.02f, 0.02f, 0.85f));

            panelRoot = panel.gameObject;

            // 개선(Design_Ending.md 5-1): 제목색이 팔레트에 없는 순색 Color.red(#FF0000)였다 - 정적 위험
            // 표시를 Danger Red #CC3333로 통일한 ArtDirection.md 1.3의 마지막 예외였다. 문구도
            // "게임 오버"(시스템 언어) → "이곳에서 끝났다"(서사 언어)로 바꿔, 사인 7종 문구와 톤을 맞춘다.
            var title = UIBuilder.CreateText(panel, "Title", "이곳에서 끝났다", 48, DangerRed, TextAnchor.MiddleCenter);
            PositionCentered(title.rectTransform, yOffset: 170f, height: 70f);

            messageLabel = UIBuilder.CreateText(panel, "Message", "", 20, Color.white, TextAnchor.MiddleCenter);
            PositionCentered(messageLabel.rectTransform, yOffset: 108f, height: 34f);

            // 사인별 회피 힌트 1줄(Design_Ending.md 5-3). 폰트 12 = ArtDirection.md 4.3 Body 등급.
            // 사망 문구 바로 아래에 붙어 "다음에는 이렇게 하면 된다"를 알려주는 것이 이 줄의 유일한 목적이다.
            hintLabel = UIBuilder.CreateText(panel, "Hint", "", 12, BodyGray, TextAnchor.MiddleCenter);
            PositionCentered(hintLabel.rectTransform, yOffset: 78f, height: 22f);

            // 성취 통계 3항목(Design_Ending.md 5-2). 승리 엔딩과 동일한 3항목을 쓰되, 순차 공개는 하지
            // 않고 한 번에 띄운다 - 사망은 축하가 아니므로 리듬을 주지 않는다는 문서 결정 그대로다.
            // (연출이 없으므로 timeScale=0에서 멈출 deltaTime 기반 보간 자체가 존재하지 않는다.)
            statsLabel = UIBuilder.CreateText(panel, "Stats", "", 15, BodyGray, TextAnchor.UpperCenter);
            statsLabel.lineSpacing = 1.3f;
            PositionCentered(statsLabel.rectTransform, yOffset: 6f, height: 80f);

            // R 키로도 여전히 재시작할 수 있지만(아래 참고), 클릭으로도 재시작할 수 있도록 버튼을 둔다.
            // 키는 박아두지 않고 GameOverController가 실제로 들고 있는 값을 읽는다(EndingUI의
            // 계속하기 버튼이 endingChecker.continueKey를 읽는 것과 같은 규칙). Start()에서
            // gameOverController를 먼저 찾은 뒤 BuildUI를 부르므로 이 시점에 값이 확정돼 있다.
            KeyCode restartKey = gameOverController != null ? gameOverController.restartKey : KeyCode.R;
            var restartButton = UIBuilder.CreateButton(panel, "RestartButton", $"다시 시작 ({restartKey})", OnRestartClicked);
            var buttonRt = restartButton.GetComponent<RectTransform>();
            buttonRt.anchorMin = new Vector2(0.5f, 0.5f);
            buttonRt.anchorMax = new Vector2(0.5f, 0.5f);
            buttonRt.pivot = new Vector2(0.5f, 0.5f);
            buttonRt.anchoredPosition = new Vector2(0f, -110f);
            buttonRt.sizeDelta = new Vector2(240f, 48f);
        }

        /// <summary>
        /// 화면 좌우로 꽉 채우고, 수직 중심에서 yOffset만큼 떨어진 위치에 지정 높이로 배치한다
        /// (EndingUI.PositionCentered와 동일한 헬퍼의 축약형).
        /// </summary>
        private void PositionCentered(RectTransform rt, float yOffset, float height)
        {
            rt.anchorMin = new Vector2(0f, 0.5f);
            rt.anchorMax = new Vector2(1f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2(0f, yOffset);
            rt.sizeDelta = new Vector2(0f, height);
        }

        /// <summary>
        /// 사망 원인에 1:1로 대응하는 회피 힌트를 반환한다(Design_Ending.md 5-3 표 그대로).
        /// 레시피 수치(해독제 = 코코넛 1 + 천조각 1, 붕대 = 천조각 2)는 문서가 실측 에셋에서 가져온 값이다 -
        /// **힌트가 틀리면 힌트가 아니라 함정이 되므로** 이 문자열은 임의로 고쳐 쓰지 않는다.
        /// </summary>
        private string GetAvoidanceHint()
        {
            if (survivalStats == null)
                return GetAvoidanceHint(DamageCause.Unknown);

            return GetAvoidanceHint(survivalStats.lastDamageCause);
        }

        /// <summary>
        /// 사인을 인자로 받아 힌트만 돌려주는 순수 버전(GameOverController.GetDeathMessage(DamageCause)와
        /// 같은 구조다). 이 컴포넌트의 상태를 전혀 읽지 않으므로 미리보기가 게임 상태를 건드리지 않고
        /// 사인 7종의 힌트를 전부 확인할 수 있다. 문구는 이 한 곳에만 존재한다 - 미리보기용 표를 따로
        /// 복사하면 조용히 갈라진다.
        /// </summary>
        private static string GetAvoidanceHint(DamageCause cause)
        {
            switch (cause)
            {
                case DamageCause.Starvation:
                    return "코코넛과 생선은 도구 없이도 구할 수 있다.";

                case DamageCause.Sunstroke:
                    return "쉼터의 그늘에서는 체온이 회복된다.";

                case DamageCause.Poison:
                    return "해독제는 코코넛 1 + 천조각 1로 만든다.";

                case DamageCause.Bleeding:
                    return "붕대는 천조각 2로 만든다.";

                case DamageCause.Drowning:
                    return "깊은 물에서는 산소가 빠르게 준다.";

                case DamageCause.Predator:
                    return "창을 들면 맞설 수 있다. 밤에는 피하는 편이 낫다.";

                case DamageCause.SharkAttack:
                    return "지느러미가 보이면 이미 가까이 있다.";

                default:
                    return "허기와 갈증을 먼저 관리하라.";
            }
        }

        /// <summary>
        /// 통계 3항목(생존 일수 / 방문한 섬 / 제작한 물건)을 한 번에 채운다. 참조가 없는 항목은
        /// 빈칸으로 남기지 않고 흐린 대시로 표시한다(Design_Ending.md 3장 페이즈 3의 표기 규칙).
        /// </summary>
        private void RefreshStats()
        {
            if (statsLabel == null)
                return;

            string unknown = ColorTag("— — —", UnknownGray);

            string daysText = survivalClock != null
                ? $"{survivalClock.ElapsedDays + 1}일"
                : unknown;

            string islandsText = unknown;
            if (worldMapManager != null && worldMapManager.islands != null)
            {
                int total = worldMapManager.islands.Count;
                int discovered = 0;
                for (int i = 0; i < total; i++)
                {
                    var island = worldMapManager.islands[i];
                    if (island != null && (island.isDiscovered || island.isStartingIsland))
                        discovered++;
                }

                if (total > 0)
                    islandsText = $"{discovered} / {total}";
            }

            // 주입값이 있으면 그것을 우선하고, 없으면 CraftingSystem이 세고 있는 종류 수를 직접 읽는다.
            // 이 카운터는 세이브에 저장되지 않아 불러오기 이후에는 0부터 다시 센다(집계 정책은
            // CraftingSystem 소유 - 이 UI는 읽어서 보여주기만 한다).
            //
            // [디렉터 결정] 그래서 0은 "정말 아무것도 안 만들었다"와 "불러온 뒤로 집계가 없다"를
            // 구분할 수 없다. "(이번 세션)" 같은 접미사를 붙이지 않고, 값이 0일 때만 다른 미집계
            // 항목과 똑같이 흐린 대시로 표시한다 - 1종 이상이면 그 값 자체는 언제나 정확하므로
            // 그대로 보여준다. 불러오기 여부를 UI가 알아내려고 시스템을 건드리지 않는다.
            int craftedKinds = craftedKindCount >= 0
                ? craftedKindCount
                : (craftingSystem != null ? craftingSystem.CraftedRecipeCount : -1);

            string craftedText = craftedKinds > 0 ? $"{craftedKinds}종" : unknown;

            statsLabel.text = $"생존 일수   {daysText}\n방문한 섬   {islandsText}\n제작한 물건   {craftedText}";
        }

        /// <summary>
        /// UI.Text의 리치 텍스트 태그로 일부 구간만 다른 색으로 칠한다(Text 하나로 "값이 비어 있음"을
        /// 흐리게 표현하기 위한 최소 수단 - 라벨을 항목마다 쪼개지 않으려는 목적).
        /// </summary>
        private static string ColorTag(string content, Color color)
        {
            return $"<color=#{ColorUtility.ToHtmlStringRGBA(color)}>{content}</color>";
        }

        /// <summary>
        /// GameOverController.isGameOver가 false→true로 바뀐 프레임을 감지해 그 한 번만 패널을 열고
        /// 사망 원인 안내 문구를 채운다. R 키 입력 자체는 GameOverController.Update()가 이미 직접
        /// 처리하고 있으므로(이 UI가 없어도 재시작됨) 여기서는 화면 표시만 담당한다.
        /// </summary>
        private void Update()
        {
            if (gameOverController == null || shown)
                return;

            if (!gameOverController.isGameOver)
                return;

            // 미리보기가 떠 있는 동안 진짜 사망이 성립했다면, 미리보기를 먼저 정리하고 진짜 화면으로
            // 넘어간다. 정리하지 않으면 아래에서 패널을 다시 채우는 동안 previewMode가 남아
            // 재시작 버튼이 "미리보기 닫기"로 동작하는 상태가 된다.
            ClosePreview();

            messageLabel.text = gameOverController.GetDeathMessage();
            hintLabel.text = GetAvoidanceHint();
            RefreshStats();
            SetOpen(true);
            shown = true;
        }

        /// <summary>
        /// 재시작 버튼 클릭 시 호출한다. Time.timeScale이 0인 상태에서도 UGUI 버튼 클릭(EventSystem의
        /// 포인터 입력 처리)은 Time.deltaTime과 무관하게 매 프레임 정상 동작하므로 눌린다 - 기존
        /// GameOverController.OnGUI 주석("Time.timeScale이 0이어도 OnGUI/Input은 정상 동작")과 같은
        /// 이유로 UGUI 쪽도 동일하게 동작한다.
        /// </summary>
        private void OnRestartClicked()
        {
            // 미리보기 중에는 재시작(씬 리로드)을 절대 실행하지 않는다. 화면 확인이 목적인데
            // 버튼 하나로 진행 중인 세션이 날아가면 그게 사고다.
            if (previewMode)
            {
                ClosePreview();
                return;
            }

            gameOverController?.RestartGame();
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        /// <summary>
        /// 미리보기에서 순회할 사망 원인 7종. DamageCause.Unknown(원인 불명 기본 문구)은 빼둔다 -
        /// 확인하고 싶은 것은 "사인마다 다른 문구가 실제로 다 들어 있는가"이기 때문이다.
        /// 순서는 SurvivalStats.DamageCause 선언 순서 그대로다(굶주림·일사병·중독·출혈·익사·포식자·상어).
        /// </summary>
        private static readonly DamageCause[] PreviewCauses =
        {
            DamageCause.Starvation,
            DamageCause.Sunstroke,
            DamageCause.Poison,
            DamageCause.Bleeding,
            DamageCause.Drowning,
            DamageCause.Predator,
            DamageCause.SharkAttack,
        };

        /// <summary>지금 미리보기로 보여주고 있는 사인의 PreviewCauses 인덱스.</summary>
        private int previewCauseIndex = -1;

        /// <summary>미리보기 도중 다른 미리보기(엔딩)를 닫기 위한 참조. 실제로 열 때만 지연 탐색한다.</summary>
        private EndingUI cachedEndingUI;

        /// <summary>
        /// [개발 전용] 실제로 죽지 않은 채로 사망 화면만 띄운다(QA/실기 확인용).
        /// GameOverController.isGameOver를 건드리지 않으므로 게임은 계속 진행 가능한 상태로 남는다.
        ///
        /// 사인 7종 순회: 처음 부르면 첫 사인(굶주림)으로 열고, 미리보기가 떠 있는 상태에서 다시
        /// 부르면 다음 사인으로 넘어간다. 마지막 사인(상어) 다음 호출이 닫기다. 문구는
        /// GameOverController.GetDeathMessage(DamageCause) **순수 오버로드**(이미 존재한다)로 얻으므로
        /// survivalStats.lastDamageCause를 쓰지도 바꾸지도 않는다 - 그것을 건드리면 그것이야말로
        /// 게임 상태 변경이다. 회피 힌트도 같은 사인으로 함께 바뀐다.
        ///
        /// 이중 격리: (1) #if로 출시 빌드에서 컴파일되지 않는다. (2) Debug.isDebugBuild를 한 번 더 본다.
        /// 호출부(DebugHud)도 F3 패널이 열려 있을 때만 키를 받는다.
        /// </summary>
        public void DebugPreviewGameOver()
        {
            if (!Debug.isDebugBuild)
                return;

            // 진짜 사망 화면이 이미 떠 있으면 끼어들지 않는다.
            if (shown || (gameOverController != null && gameOverController.isGameOver))
                return;

            // BuildUI 전에 불릴 일은 없지만(호출부가 이 컴포넌트를 씬에서 찾은 뒤에 부른다),
            // 라벨이 없으면 조용히 아무것도 하지 않는다.
            if (messageLabel == null || hintLabel == null)
                return;

            // 떠 있으면 다음 사인, 아니면 처음부터. 마지막 사인을 지나면 닫는다.
            int nextIndex = previewMode ? previewCauseIndex + 1 : 0;
            if (nextIndex >= PreviewCauses.Length)
            {
                ClosePreview();
                return;
            }

            previewCauseIndex = nextIndex;
            DamageCause cause = PreviewCauses[previewCauseIndex];

            messageLabel.text = gameOverController != null
                ? gameOverController.GetDeathMessage(cause)
                : "무인도에서 생존하지 못했습니다.";
            hintLabel.text = GetAvoidanceHint(cause);
            RefreshStats();
            SetOpen(true);

            // 이미 미리보기 중이었다면 timeScale은 이미 0이고 백업도 잡혀 있다. 여기서 다시 백업하면
            // 0을 백업하게 되어 닫아도 0에서 못 빠져나온다(이번 배치에서 고친 사고의 본체다).
            if (previewMode)
                return;

            // ── 미리보기는 한 번에 하나만 ──────────────────────────────────────────────────
            // 엔딩 미리보기가 열려 있으면 **timeScale을 백업하기 전에** 먼저 닫는다. 둘 다 열려
            // 있으면 각자 백업을 뜬 뒤 나중에 연 쪽이 0을 백업하게 되어, 어떤 순서로 닫아도
            // timeScale이 0으로 고정된다(화면엔 아무것도 없는데 게임이 멈춘 상태 - Esc 설정 화면도
            // 0을 백업하고 0을 되돌리므로 복구가 불가능했다).
            if (cachedEndingUI == null)
                cachedEndingUI = FindAnyObjectByType<EndingUI>();
            if (cachedEndingUI != null)
                cachedEndingUI.ClosePreview();

            previewMode = true;

            // 진짜 사망과 같은 조건(GameOverController가 timeScale 0을 건다)을 재현한다.
            previewTimeScaleBackup = Time.timeScale;
            Time.timeScale = 0f;
        }
#endif

        /// <summary>
        /// 개발용 미리보기를 닫고 timeScale을 되돌린다. 미리보기가 아니면 아무 일도 하지 않는다.
        /// </summary>
        public void ClosePreview()
        {
            if (!previewMode)
                return;

            previewMode = false;
            SetOpen(false);

            // 미리보기 도중 진짜 사망/엔딩이 성립했다면 그쪽이 이미 timeScale 0을 걸어둔 상태다.
            // 그때 백업값(보통 1)을 복원하면 멈춰야 할 화면에서 게임이 계속 흐른다.
            if (!IsRealEndScreenActive())
                Time.timeScale = previewTimeScaleBackup;
        }

        /// <summary>
        /// 진짜 결말 화면(사망 또는 엔딩)이 지금 떠 있는지. 둘 중 하나라도 떠 있으면 그쪽이 이미
        /// timeScale 0을 걸어둔 상태이므로 미리보기 백업값을 복원해서는 안 된다.
        /// EndingUI.ClosePreview의 같은 이름 메서드와 판정 내용이 동일하다(비대칭 가드가 이번
        /// 배치의 지적 사항이었다 - 한쪽만 고치면 반대 순서에서 같은 사고가 난다).
        /// </summary>
        private bool IsRealEndScreenActive()
        {
            if (gameOverController != null && gameOverController.isGameOver)
                return true;

            if (cachedEndingChecker == null)
                cachedEndingChecker = FindAnyObjectByType<EndingChecker>();

            return cachedEndingChecker != null && cachedEndingChecker.IsShowingEnding;
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
