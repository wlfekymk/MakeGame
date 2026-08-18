using System.Text;
using UnityEngine;
using UnityEngine.UI;
using MakeGame.Player;
using MakeGame.Systems;

namespace MakeGame.UI
{
    /// <summary>
    /// QA/디버깅용 raw 숫자 패널. 경과 일수, 생존 수치, 상태 이상, 배/경비행기 진행도, 인벤토리 개수,
    /// 저장 상태를 화면 우측에 텍스트로 나열한다.
    ///
    /// 개선(qa 지적, B4 이번 배치): 예전에는 이 패널을 OnGUI(IMGUI)로 매 프레임 그렸다. **IMGUI는
    /// sortingOrder와 무관하게 Screen Space Overlay Canvas 위에 항상 덮어 그려진다.** 실제로 같은 원인으로
    /// GameOverController.OnGUI가 UI/GameOverUI를 통째로 가려 사망 화면이 아예 안 보였던 사고가 있었고
    /// (GameOverController 주석에 기록됨), 이 패널도 GameOverUI(sortOrder 20)/EndingUI(21)를 가릴 수 있는
    /// 같은 종류의 잠재적 사고 지점이었다. 기능은 그대로 두고 표시 수단만 UGUI로 옮겼다 - 이제 이 패널은
    /// sortOrder 15 캔버스에 그려지므로 사망/엔딩 화면이 항상 그 위에 온다.
    ///
    /// 안전 기본값: QA 도구이므로 기본은 숨김이고 toggleKey(기본 F3)로 켠다. 예전에는 씬에 이
    /// 컴포넌트가 붙어 있기만 하면 항상 화면에 떠 있었다.
    /// </summary>
    public class DebugHud : MonoBehaviour
    {
        [Tooltip("표시할 생존 수치")]
        public SurvivalStats survivalStats;

        [Tooltip("표시할 뗏목. 비워두면 씬에 살아 있는 뗏목(RaftStructure.Active)을 자동으로 쓴다 -" +
            " 뗏목은 런타임에 스스로 생기는 오브젝트라 정상 경로에서는 이 칸이 비어 있는 것이 맞다.")]
        public RaftStructure raftStructure;

        [Tooltip("표시할 경비행기 수리 진행 상태")]
        public AircraftRepairSystem aircraftRepair;

        [Tooltip("표시할 경과 일수")]
        public SurvivalClock survivalClock;

        [Tooltip("표시할 인벤토리")]
        public PlayerInventory inventory;

        [Tooltip("저장/불러오기 상태 메시지를 표시할 세이브 시스템")]
        public SaveLoadController saveLoadController;

        [Tooltip("조작키 안내를 함께 표시할지 여부")]
        public bool showControlsHelp = true;

        [Tooltip("디버그 패널을 켜고 끄는 키")]
        public KeyCode toggleKey = KeyCode.F3;

        [Tooltip("시작하자마자 디버그 패널을 켜 둘지 여부. QA 도구이므로 기본은 꺼짐이다.")]
        public bool visibleOnStart = false;

        [Tooltip("텍스트를 다시 만드는 주기(초). 매 프레임 문자열을 새로 조립하면 GC 할당이 쌓인다.")]
        public float refreshInterval = 0.25f;

        private GameObject panelRoot;
        private Text bodyLabel;
        private float refreshTimer = 0f;

        // ── 결말 화면 미리보기(개발 전용) ────────────────────────────────────────────────────
        // 뗏목 엔딩은 경과 15일(매일 취침해도 실질 74분, 안 자면 150분)이 조건이라 실기로는 도달할 수
        // 없고, 사망 화면은 실제로 죽어야 뜬다. 그래서 이 세 화면은 지금까지 아무도 실행 중에 본 적이
        // 없다. F3 패널이 열려 있는 동안만 받는 키로 화면만 띄운다.
        //
        // 격리 방식(출시 빌드에서 플레이어가 절대 누를 수 없어야 한다):
        //   1) 키 상수와 처리 코드가 전부 #if UNITY_EDITOR || DEVELOPMENT_BUILD 안에 있다 - 출시
        //      빌드에는 컴파일조차 되지 않는다.
        //   2) 그 안에서도 Debug.isDebugBuild를 한 번 더 확인한다.
        //   3) EndingUI/GameOverUI 쪽 진입점(DebugPreviewEnding/DebugPreviewGameOver)에도 같은
        //      #if + Debug.isDebugBuild 가드가 걸려 있다.
        //   4) F3 디버그 패널이 열려 있을 때만 키를 읽는다(패널 기본값은 꺼짐).
        // 공개 필드로 두지 않는 이유: 씬 직렬화 대상이 되면 #if로 빠지는 빌드에서 "씬에는 있는데
        // 읽는 코드가 없는 키"가 남는다. QA 도구의 고정 키라 상수로 충분하다.
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private const KeyCode PreviewBoatEndingKey = KeyCode.F6;
        private const KeyCode PreviewAircraftEndingKey = KeyCode.F7;
        private const KeyCode PreviewGameOverKey = KeyCode.F8;

        // [B31] 개발용 재료 지급 키. 감독이 "재료가 없어서 실기 확인을 못 한다"고 해서 넣었다.
        // 릴리스 빌드에는 절대 들어가지 않는다 - 아래 HandleScreenPreviewKeys와 같은
        // #if UNITY_EDITOR || DEVELOPMENT_BUILD + Debug.isDebugBuild 이중 가드 안에서만 산다.
        private const KeyCode GrantMaterialsKey = KeyCode.F4;

        // 디버그 전체 지도 + 자유 이동 토글 키. 감독은 "F6 같은 빈 키"라 했지만 F6은 이미 뗏목 엔딩
        // 미리보기다 - F3~F9가 전부 사용 중(F3 이 패널, F4 재료, F5 저장, F6/F7/F8 미리보기, F9
        // 불러오기)이라 비어 있는 F10을 쓴다. 플래그 자체는 IslandTravel.debugRevealAllIslands가
        // 들고 있다(소비자가 Systems/UI 양쪽이라 Systems 쪽 소유 - 해당 필드 주석 참고). 기본 ON.
        private const KeyCode ToggleFullMapKey = KeyCode.F10;

        /// <summary>
        /// F4로 한 번에 지급할 개발용 재료표. 이름은 ScriptableObjects/Item_*.asset의 itemName과
        /// 정확히 같아야 한다(문자열 대조다 - 오타는 조용히 무시된다).
        /// 수량은 "인벤토리 30칸, 스택 20개" 규격에 맞춰 잡았다: 스택 아이템 10종 x 20개 = 10칸,
        /// 도구 3종 x 1개 = 3칸. 합쳐도 13칸이라 가득 차지 않는다.
        /// </summary>
        private static readonly (string name, int count)[] DevelopmentMaterials =
        {
            ("나뭇가지", 20), ("노끈", 20), ("대나무", 20), ("야자잎", 20), ("돌조각", 20),
            ("천조각", 20), ("금속조각", 20), ("부싯돌", 10), ("코코넛", 5), ("생수", 5),
            ("칼", 1), ("손도끼", 1), ("창", 1),
        };

        // ── QA 치트 토글 (생명/일사병/산소/허기·갈증 무제한) ─────────────────────────────────
        // 감독 요청: "디버그 모드에 생명, 일사병, 공기 무제한 설정을 넣어줘. 테스트를 하려니깐 힘드네"
        // 실제 플래그는 SurvivalStats가 [System.NonSerialized]로 들고 있고(세이브/씬에 남지 않는다),
        // **그 플래그를 true로 만들 수 있는 코드는 이 #if 블록 안이 전부다.** SurvivalStats 쪽 검사
        // 코드도 같은 #if로 묶여 있어 출시 빌드에는 치트 자체가 컴파일되지 않는다.
        //
        // 키는 F11 하나만 새로 쓴다. F3~F10이 전부 사용 중이고(F3 패널, F4 재료, F5 저장, F6/F7/F8
        // 미리보기, F9 불러오기, F10 전체 지도) F11/F12가 비어 있다. 개별 토글에 숫자키 1~4를 쓰는
        // 안은 버렸다 - BuildMenuUI.SelectKeys가 Alpha1~7을 쓰고 있어서, 건축 모드를 켠 채 치트를
        // 만지면 부품 선택과 정면으로 겹친다(QA는 건축 테스트 중에 무적을 켜고 싶어 한다).
        // 개별 토글은 아래 버튼으로 한다 - 클릭 가능 여부는 BuildCheatControls 주석 참고.
        private const KeyCode ToggleAllCheatsKey = KeyCode.F11;

        /// <summary>치트 버튼 영역이 패널 아래쪽에서 차지하는 높이(px). 이만큼 패널을 키우고 본문을 띄운다.</summary>
        private const float CheatAreaHeight = 118f;

        /// <summary>켜짐 색(호박색). 회색 계열 패널 위에서 한눈에 튀는 색을 쓴다.</summary>
        private static readonly Color CheatOnColor = new Color(0.85f, 0.62f, 0.13f, 1f);
        /// <summary>꺼짐 색(어두운 회색).</summary>
        private static readonly Color CheatOffColor = new Color(0.20f, 0.22f, 0.24f, 1f);

        /// <summary>개별 치트 4종 + "전부" 1개의 순서. 버튼/라벨 배열 인덱스와 아래 헬퍼가 이 순서를 공유한다.</summary>
        private const int CheatHealth = 0;
        private const int CheatHeat = 1;
        private const int CheatOxygen = 2;
        private const int CheatFood = 3;
        private const int CheatCount = 4;

        private readonly Image[] cheatButtonImages = new Image[CheatCount + 1];
        private readonly Text[] cheatButtonLabels = new Text[CheatCount + 1];

        private EndingUI cachedEndingUI;
        private GameOverUI cachedGameOverUI;

        /// <summary>지금 미리보기로 띄워둔 엔딩이 경비행기인지. 같은 키를 다시 누르면 닫기 위한 상태다.</summary>
        private bool previewingAircraftEnding = false;
#endif

        // 문자열 조립용 버퍼. 갱신 때마다 새 StringBuilder를 만들지 않고 Clear해서 재사용한다.
        private readonly StringBuilder builder = new StringBuilder(512);

        /// <summary>패널을 만들고 기본 표시 여부를 적용한다.</summary>
        private void Start()
        {
            BuildUI();
            SetOpen(visibleOnStart);
        }

        /// <summary>
        /// 화면 우측에 반투명 패널 + 좌측 정렬 텍스트 한 덩어리를 만든다.
        /// 위치: 우상단은 미니맵 레이더(MinimapUI, 160px + 여백 20)가 이미 쓰고 있으므로 그 아래에서
        /// 시작하도록 y를 -200 내려 잡는다(IMGUI 시절에도 SurvivalHudUI와 겹치지 않게 우측으로 옮겼던
        /// 것과 같은 이유 - 다른 패널을 가리지 않는 자리를 고른다).
        /// </summary>
        private void BuildUI()
        {
            // GameOverUI(20)/EndingUI(21)보다 반드시 낮게 둔다. 디버그 도구가 결말 화면을 가리면
            // 이번 이관의 목적 자체가 사라진다.
            var canvas = UIBuilder.CreateCanvas("DebugHudCanvas", sortOrder: 15);

            // 패널 아래쪽에 치트 버튼 줄이 붙는 개발 빌드에서만 그만큼 아래로 늘리고 본문을 띄운다.
            // 출시 빌드에서는 두 값이 예전 그대로(-640 / 10)라 레이아웃이 1px도 변하지 않는다.
            float panelBottom = -640f;
            float bodyBottomPad = 10f;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            bool withCheatControls = Debug.isDebugBuild;
            if (withCheatControls)
            {
                panelBottom -= CheatAreaHeight;
                bodyBottomPad += CheatAreaHeight;
            }
#endif

            var panel = UIBuilder.CreatePanel(
                canvas.transform, "DebugHudPanel",
                anchorMin: new Vector2(1f, 1f), anchorMax: new Vector2(1f, 1f),
                offsetMin: new Vector2(-360f, panelBottom), offsetMax: new Vector2(-20f, -200f),
                color: new Color(0f, 0f, 0f, 0.55f),
                addTopBorder: true);

            panelRoot = panel.gameObject;

            // 폰트 12 = ArtDirection.md 4.3 Body 등급. raw 숫자 나열이라 본문 취급이 맞다.
            bodyLabel = UIBuilder.CreateText(panel, "Body", "", 12, new Color(0.9f, 0.9f, 0.9f, 1f), TextAnchor.UpperLeft);
            bodyLabel.lineSpacing = 1.15f;
            var rt = bodyLabel.rectTransform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(10f, bodyBottomPad);
            rt.offsetMax = new Vector2(-10f, -10f);

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (withCheatControls)
                BuildCheatControls(panel);
#endif
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        /// <summary>
        /// 패널 아래쪽에 치트 토글 버튼 5개(개별 4 + 전부 1)를 만든다.
        ///
        /// **버튼을 쓸 수 있는가(커서 문제) - 확인 결과.** 이 패널은 UIDragHandle을 붙이지 않으므로
        /// CursorLockController가 "창 열림"으로 세지 않는다. 즉 F3를 눌러도 커서는 잠긴 채다.
        /// 그런데 이 프로젝트에는 이미 같은 상황에 대한 정해진 답이 있다 - CursorLockController 클래스
        /// 주석: 건축 핫바도 "창"으로 세지 않으며 "굳이 칸을 클릭하고 싶으면 Shift를 눌러 커서를 잠깐
        /// 풀면 된다". 해제 키(LeftShift/RightShift)는 GetKey로 상태만 읽으므로 아무 키와도 다투지
        /// 않고, 누르고 있는 동안 Cursor.lockState = None + visible = true가 되어 버튼 클릭이 실제로
        /// 동작한다. 그래서 **버튼을 유지**하고, 패널 안내에 [Shift]를 함께 적었다.
        /// 이 패널 자체에 UIDragHandle을 붙여 커서를 풀게 만드는 안은 버렸다 - F3를 여는 순간 시야가
        /// 얼어붙어, 지금까지 "패널을 켜 둔 채 돌아다니며 수치를 보던" 기존 사용법이 깨진다.
        /// 마우스에 손대기 싫은 가장 흔한 경우(전부 켜기)는 F11 한 키로 끝난다.
        ///
        /// 배치는 패널 좌하단 기준 절대 좌표다(pivot/anchor를 (0,0)으로 고정). 본문 텍스트는 위에서
        /// offsetMin.y를 CheatAreaHeight만큼 올려 두었으므로 이 영역과 겹치지 않는다.
        /// </summary>
        private void BuildCheatControls(RectTransform panel)
        {
            const float pad = 10f;
            const float gap = 6f;
            const float rowH = 24f;
            float fullW = 340f - pad * 2f;      // 패널 폭 340 - 좌우 여백
            float colW = (fullW - gap) * 0.5f;

            var header = UIBuilder.CreateText(panel, "CheatHeader",
                "── QA 치트 ([Shift] 누르고 클릭 / [F11] 전부) ──",
                11, new Color(0.75f, 0.78f, 0.8f, 1f), TextAnchor.LowerLeft);
            header.horizontalOverflow = HorizontalWrapMode.Overflow;
            PlaceAtPanelBottom(header.rectTransform, pad, 96f, fullW, 18f);

            CreateCheatButton(panel, CheatHealth, "생명 무제한", pad, 70f, colW, rowH);
            CreateCheatButton(panel, CheatHeat, "일사병 면역", pad + colW + gap, 70f, colW, rowH);
            CreateCheatButton(panel, CheatOxygen, "산소 무제한", pad, 40f, colW, rowH);
            CreateCheatButton(panel, CheatFood, "허기·갈증 정지", pad + colW + gap, 40f, colW, rowH);
            CreateCheatButton(panel, CheatCount, "전부 켜기/끄기 [F11]", pad, 10f, fullW, rowH);
        }

        /// <summary>치트 버튼 하나를 만들고 배열에 담는다. index가 CheatCount면 "전부" 버튼이다.</summary>
        private void CreateCheatButton(RectTransform panel, int index, string label,
            float left, float bottom, float width, float height)
        {
            int captured = index; // 클로저가 루프 변수를 잡지 않도록(여기선 상수지만 규칙을 지킨다)
            var button = UIBuilder.CreateButton(panel, "Cheat_" + captured, label, () => OnCheatButtonClicked(captured));

            // Selectable에는 rectTransform 프로퍼티가 없다(Graphic에만 있다). UIBuilder.CreateCloseButton과
            // 같은 방식으로 GetComponent로 집는다.
            PlaceAtPanelBottom(button.GetComponent<RectTransform>(), left, bottom, width, height);

            // UIBuilder.CreateButton의 기본 라벨은 16px이라 폭 157px 칸에서 줄바꿈된다. 11px + 넘침 허용.
            var text = button.GetComponentInChildren<Text>();
            if (text != null)
            {
                text.fontSize = 11;
                text.horizontalOverflow = HorizontalWrapMode.Overflow;
                cheatButtonLabels[captured] = text;
            }

            cheatButtonImages[captured] = button.GetComponent<Image>();
        }

        /// <summary>패널 좌하단(0,0) 기준 절대 좌표로 배치한다.</summary>
        private static void PlaceAtPanelBottom(RectTransform rt, float left, float bottom, float width, float height)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.zero;
            rt.pivot = Vector2.zero;
            rt.anchoredPosition = new Vector2(left, bottom);
            rt.sizeDelta = new Vector2(width, height);
        }
#endif

        /// <summary>
        /// 토글 키를 처리하고, 패널이 켜져 있을 때만 주기적으로 텍스트를 다시 만든다.
        /// 타이머는 unscaledDeltaTime으로 센다 - 사망/엔딩/설정 화면은 Time.timeScale = 0을 걸기 때문에
        /// (Design_Ending.md 1장 제약 A) deltaTime을 쓰면 그 상태에서 디버그 값이 영원히 갱신되지 않는다.
        /// </summary>
        private void Update()
        {
            if (Input.GetKeyDown(toggleKey))
                SetOpen(panelRoot != null && !panelRoot.activeSelf);

            if (panelRoot == null || !panelRoot.activeSelf)
                return;

            HandleScreenPreviewKeys();

            refreshTimer -= Time.unscaledDeltaTime;
            if (refreshTimer > 0f)
                return;

            refreshTimer = Mathf.Max(0.05f, refreshInterval);
            RefreshText();
        }

        /// <summary>
        /// IMGUI 시절 GUILayout.Label로 한 줄씩 그리던 것과 동일한 내용/순서를 한 덩어리 텍스트로 만든다.
        /// </summary>
        private void RefreshText()
        {
            if (bodyLabel == null)
                return;

            builder.Clear();

            if (survivalClock != null)
                builder.Append("경과 일수: ").Append(survivalClock.ElapsedDays).Append("일차\n");

            if (survivalStats != null)
            {
                builder.AppendFormat("체력: {0:F0} / {1:F0}\n", survivalStats.health, survivalStats.maxHealth);
                builder.AppendFormat("허기: {0:F0}   갈증: {1:F0}\n", survivalStats.hunger, survivalStats.thirst);
                builder.AppendFormat("일사병: {0:F0}   산소: {1:F0}\n", survivalStats.sunstroke, survivalStats.oxygen);
                builder.Append("중독:").Append(survivalStats.isPoisoned ? "O" : "X")
                    .Append(" 출혈:").Append(survivalStats.isBleeding ? "O" : "X")
                    .Append(" 골절:").Append(survivalStats.hasBrokenBone ? "O" : "X").Append('\n');
            }

            // 뗏목은 씬 배선이 아니라 런타임 생성이므로 매 갱신마다 Active를 한 번 확인한다
            // (static 프로퍼티 읽기라 전역 검색 비용이 없다).
            if (raftStructure == null)
                raftStructure = RaftStructure.Active;

            if (raftStructure != null)
            {
                builder.Append("뗏목: ").Append(raftStructure.DescribeState())
                    .Append("  [").Append(Mathf.RoundToInt(raftStructure.GetOverallProgress() * 100f)).Append("%]")
                    .Append(raftStructure.IsOceanReady ? " 대양 준비"
                        : raftStructure.IsSeaworthy ? " 항해 가능" : "")
                    .Append('\n');
            }

            if (aircraftRepair != null)
            {
                builder.AppendFormat("경비행기 수리: {0:F0}% {1}\n",
                    aircraftRepair.GetOverallProgress() * 100f,
                    aircraftRepair.isRepairComplete ? "(완료)" : "");
            }

            if (inventory != null)
                builder.Append("인벤토리 아이템 수: ").Append(inventory.items.Count).Append('\n');

            if (saveLoadController != null && !string.IsNullOrEmpty(saveLoadController.lastStatusMessage))
                builder.Append("저장: ").Append(saveLoadController.lastStatusMessage).Append('\n');

            if (showControlsHelp)
            {
                builder.Append('\n');
                builder.Append("[E] 상호작용/공격(무기 필요)   [R] 조리   [C] 섭취   [G] 설치\n");
                builder.Append("[Tab] 인벤토리(열린 상태에서 [F] 카테고리 필터)   [V] 제작   [M] 섬 목록/이동\n");
                builder.Append("[수영중] [Space] 위로   [Ctrl] 잠수\n");
                builder.Append("[F5] 저장   [F9] 불러오기   [Esc] 설정\n");
            }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            // 개발 빌드에서만 존재하는 줄이다. 출시 빌드에는 이 안내 자체가 컴파일되지 않으므로
            // "화면에 적혀 있는데 눌러도 안 되는 키"가 남지 않는다.
            if (Debug.isDebugBuild)
            {
                builder.Append('\n');
                builder.Append("── 결말 화면 미리보기 (상태 변경 없음) ──\n");
                builder.Append('[').Append(PreviewBoatEndingKey.ToString()).Append("] 뗏목 엔딩   ")
                    .Append('[').Append(PreviewAircraftEndingKey.ToString()).Append("] 비행기 엔딩   ")
                    .Append('[').Append(PreviewGameOverKey.ToString()).Append("] 사망 화면\n");
                builder.Append("같은 키를 다시 누르면 닫힘\n");
                builder.Append('[').Append(ToggleFullMapKey.ToString()).Append("] 전체 지도 표시+자유 이동 (지금 ")
                    .Append(IslandTravel.DebugRevealAllActive ? "ON" : "OFF").Append(")\n");

                // 치트 상태는 아래 버튼 색으로도 보이지만, 본문에도 한 줄 남긴다 - 패널을 스크린샷으로
                // 주고받을 때 "이 값이 왜 안 줄어드는지"가 텍스트 한 줄로 설명된다.
                builder.Append('\n');
                // ResolveStats로 한 번 확보해 둔다 - 인스펙터 연결이 비어 있어도 치트 표기/버튼이
                // 동작해야 하기 때문이다(찾으면 survivalStats에 캐시되므로 매 갱신마다 찾지 않는다).
                AppendCheatSummary(ResolveStats());
                builder.Append("↓ 아래 버튼: [Shift]를 누른 채 클릭 (커서 잠금 해제)\n");
            }
#endif

            builder.Append('\n').Append('[').Append(toggleKey.ToString()).Append("] 디버그 패널 끄기");

            bodyLabel.text = builder.ToString();

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (Debug.isDebugBuild)
                RefreshCheatButtons(survivalStats);
#endif
        }

        /// <summary>
        /// 결말 화면 미리보기 키를 처리한다. 출시 빌드에서는 본문이 통째로 컴파일되지 않는 빈 메서드다.
        /// 같은 키를 다시 누르면 닫힌다(토글). 게임 상태는 전혀 바뀌지 않는다 - EndingChecker/
        /// GameOverController의 어떤 값도 쓰지 않고, 화면을 띄우는 쪽이 자기 패널만 열고 닫는다.
        /// </summary>
        private void HandleScreenPreviewKeys()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (!Debug.isDebugBuild)
                return;

            if (Input.GetKeyDown(PreviewBoatEndingKey))
                ToggleEndingPreview(aircraft: false);
            else if (Input.GetKeyDown(PreviewAircraftEndingKey))
                ToggleEndingPreview(aircraft: true);
            else if (Input.GetKeyDown(PreviewGameOverKey))
                ToggleGameOverPreview();
            else if (Input.GetKeyDown(GrantMaterialsKey))
                GrantDevelopmentMaterials();
            else if (Input.GetKeyDown(ToggleFullMapKey))
            {
                IslandTravel.debugRevealAllIslands = !IslandTravel.debugRevealAllIslands;
                refreshTimer = 0f; // 도움말의 ON/OFF 표기를 다음 주기(최대 0.25초)까지 기다리지 않고 즉시 갱신
            }
            else if (Input.GetKeyDown(ToggleAllCheatsKey))
                OnCheatButtonClicked(CheatCount);
#endif
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        /// <summary>
        /// 표시/조작 대상 SurvivalStats를 확보한다. 인스펙터 연결이 비어 있어도 동작해야 한다
        /// (GrantDevelopmentMaterials가 PlayerInventory에 대해 쓰는 것과 같은 지연 탐색 패턴).
        /// </summary>
        private SurvivalStats ResolveStats()
        {
            if (survivalStats == null)
                survivalStats = FindAnyObjectByType<SurvivalStats>();
            return survivalStats;
        }

        /// <summary>
        /// 치트 버튼(또는 F11)을 눌렀을 때. index가 CheatCount면 "전부" 토글이다 -
        /// 하나라도 꺼져 있으면 전부 켜고, 전부 켜져 있으면 전부 끈다("켜기"가 기대 동작인 쪽으로 붙인다).
        /// </summary>
        private void OnCheatButtonClicked(int index)
        {
            SurvivalStats stats = ResolveStats();
            if (stats == null)
            {
                Debug.LogWarning("[DebugHud] SurvivalStats를 찾지 못해 치트를 켜지 못했다.");
                return;
            }

            switch (index)
            {
                case CheatHealth: stats.debugInfiniteHealth = !stats.debugInfiniteHealth; break;
                case CheatHeat: stats.debugNoHeatstroke = !stats.debugNoHeatstroke; break;
                case CheatOxygen: stats.debugInfiniteOxygen = !stats.debugInfiniteOxygen; break;
                case CheatFood: stats.debugNoHungerThirst = !stats.debugNoHungerThirst; break;
                default:
                    bool allOn = stats.debugInfiniteHealth && stats.debugNoHeatstroke
                        && stats.debugInfiniteOxygen && stats.debugNoHungerThirst;
                    bool target = !allOn;
                    stats.debugInfiniteHealth = target;
                    stats.debugNoHeatstroke = target;
                    stats.debugInfiniteOxygen = target;
                    stats.debugNoHungerThirst = target;
                    break;
            }

            // 다음 주기(최대 0.25초)를 기다리지 않고 즉시 반영한다 - 눌렀는데 색이 안 바뀌면
            // "안 눌렸나?" 하고 두 번 누르게 되고, 토글이라 그러면 제자리로 돌아온다.
            refreshTimer = 0f;
            RefreshText();
        }

        /// <summary>버튼 배경색과 라벨의 ON/OFF 표기를 현재 플래그 상태에 맞춘다.</summary>
        private void RefreshCheatButtons(SurvivalStats stats)
        {
            if (cheatButtonImages[CheatHealth] == null)
                return; // 치트 UI를 만들지 않은 실행(Debug.isDebugBuild가 false)

            bool health = stats != null && stats.debugInfiniteHealth;
            bool heat = stats != null && stats.debugNoHeatstroke;
            bool oxygen = stats != null && stats.debugInfiniteOxygen;
            bool food = stats != null && stats.debugNoHungerThirst;

            ApplyCheatButtonState(CheatHealth, "생명 무제한", health);
            ApplyCheatButtonState(CheatHeat, "일사병 면역", heat);
            ApplyCheatButtonState(CheatOxygen, "산소 무제한", oxygen);
            ApplyCheatButtonState(CheatFood, "허기·갈증 정지", food);

            bool allOn = health && heat && oxygen && food;
            ApplyCheatButtonState(CheatCount, allOn ? "전부 끄기 [F11]" : "전부 켜기 [F11]", allOn);
        }

        private void ApplyCheatButtonState(int index, string label, bool on)
        {
            if (cheatButtonImages[index] != null)
                cheatButtonImages[index].color = on ? CheatOnColor : CheatOffColor;

            if (cheatButtonLabels[index] != null)
                cheatButtonLabels[index].text = index == CheatCount ? label : (label + (on ? "  ON" : "  OFF"));
        }

        /// <summary>본문에 넣을 한 줄 요약("왜 안 죽지?" 혼란 방지용).</summary>
        private void AppendCheatSummary(SurvivalStats stats)
        {
            builder.Append("치트: ");
            if (stats == null || !stats.AnyDebugCheatActive)
            {
                builder.Append("전부 꺼짐\n");
                return;
            }

            if (stats.debugInfiniteHealth) builder.Append("생명 ");
            if (stats.debugNoHeatstroke) builder.Append("일사병 ");
            if (stats.debugInfiniteOxygen) builder.Append("산소 ");
            if (stats.debugNoHungerThirst) builder.Append("허기·갈증 ");
            builder.Append("무제한\n");
        }
#endif

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        /// <summary>
        /// [B31] 개발용 재료 한 뭉치를 인벤토리에 넣는다(F4).
        ///
        /// **용량을 무시하지 않는다.** AddItemIgnoringCapacity를 쓰면 30칸을 넘겨 인벤토리 UI와
        /// 저장이 정의되지 않은 상태로 들어간다. TryAddItem으로 넣고 실패하면 거기서 멈춘 뒤
        /// 몇 개를 넣었는지 로그로 알려준다 - 이 프로젝트는 아이템이 조용히 사라진 사고가 4번 있었고,
        /// 개발 도구라도 같은 규칙을 지킨다.
        /// </summary>
        private void GrantDevelopmentMaterials()
        {
            if (inventory == null)
                inventory = FindAnyObjectByType<PlayerInventory>();
            if (inventory == null)
            {
                Debug.LogWarning("[DebugHud] PlayerInventory를 찾지 못해 재료를 지급하지 못했다.");
                return;
            }

            var registry = MakeGame.Data.ItemDataRegistry.LoadFromResources();
            if (registry == null || registry.allItems == null)
            {
                Debug.LogWarning("[DebugHud] ItemDataRegistry를 불러오지 못해 재료를 지급하지 못했다.");
                return;
            }

            int granted = 0;
            int rejected = 0;
            foreach (var wanted in DevelopmentMaterials)
            {
                MakeGame.Data.ItemData found = null;
                foreach (var candidate in registry.allItems)
                {
                    if (candidate != null && candidate.itemName == wanted.name)
                    {
                        found = candidate;
                        break;
                    }
                }

                if (found == null)
                {
                    Debug.LogWarning($"[DebugHud] 재료표의 '{wanted.name}'을(를) 레지스트리에서 찾지 못했다.");
                    continue;
                }

                for (int i = 0; i < wanted.count; i++)
                {
                    if (inventory.TryAddItem(found))
                        granted++;
                    else
                    {
                        rejected++;
                        break;
                    }
                }
            }

            inventory.NotifyInventoryChanged();
            Debug.Log(rejected > 0
                ? $"[DebugHud] 개발용 재료 {granted}개 지급. 가방이 차서 {rejected}종은 중간에 멈췄다."
                : $"[DebugHud] 개발용 재료 {granted}개 지급 완료.");
        }

        /// <summary>
        /// 엔딩 화면 미리보기를 켜고 끈다. 이미 같은 종류를 보고 있으면 닫고, 다른 종류를 보고 있으면
        /// 그쪽으로 갈아탄다(EndingUI.DebugPreviewEnding이 내부에서 먼저 닫고 처음부터 다시 재생한다).
        /// EndingUI/GameOverUI는 씬 오브젝트가 아니라 sceneLoaded에서 런타임 생성되므로 Start()에서
        /// 미리 찾지 않고, 실제로 필요할 때 찾아서 캐시한다(씬 리로드 시 죽은 참조는 null 검사로 걸린다).
        /// </summary>
        private void ToggleEndingPreview(bool aircraft)
        {
            if (cachedEndingUI == null)
                cachedEndingUI = FindAnyObjectByType<EndingUI>();
            if (cachedEndingUI == null)
                return;

            if (cachedEndingUI.IsPreviewing && previewingAircraftEnding == aircraft)
            {
                cachedEndingUI.ClosePreview();
                return;
            }

            previewingAircraftEnding = aircraft;
            cachedEndingUI.DebugPreviewEnding(aircraft);
        }

        /// <summary>사망 화면 미리보기를 켜고 끈다.</summary>
        private void ToggleGameOverPreview()
        {
            if (cachedGameOverUI == null)
                cachedGameOverUI = FindAnyObjectByType<GameOverUI>();
            if (cachedGameOverUI == null)
                return;

            if (cachedGameOverUI.IsPreviewing)
                cachedGameOverUI.ClosePreview();
            else
                cachedGameOverUI.DebugPreviewGameOver();
        }
#endif

        /// <summary>패널을 열거나 닫는다. 여는 순간 즉시 한 번 갱신해 낡은 값이 보이지 않게 한다.</summary>
        private void SetOpen(bool open)
        {
            if (panelRoot == null)
                return;

            panelRoot.SetActive(open);

            if (open)
            {
                refreshTimer = Mathf.Max(0.05f, refreshInterval);
                RefreshText();
            }
        }
    }
}
