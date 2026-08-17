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

        [Tooltip("표시할 배 제작 진행 상태")]
        public BoatConstructionSystem boatConstruction;

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
        // 배 엔딩은 경과 15일(매일 취침해도 실질 74분, 안 자면 150분)이 조건이라 실기로는 도달할 수
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

        // 디버그 전체 지도 + 자유 이동 토글 키. 감독은 "F6 같은 빈 키"라 했지만 F6은 이미 배 엔딩
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

            var panel = UIBuilder.CreatePanel(
                canvas.transform, "DebugHudPanel",
                anchorMin: new Vector2(1f, 1f), anchorMax: new Vector2(1f, 1f),
                offsetMin: new Vector2(-360f, -640f), offsetMax: new Vector2(-20f, -200f),
                color: new Color(0f, 0f, 0f, 0.55f),
                addTopBorder: true);

            panelRoot = panel.gameObject;

            // 폰트 12 = ArtDirection.md 4.3 Body 등급. raw 숫자 나열이라 본문 취급이 맞다.
            bodyLabel = UIBuilder.CreateText(panel, "Body", "", 12, new Color(0.9f, 0.9f, 0.9f, 1f), TextAnchor.UpperLeft);
            bodyLabel.lineSpacing = 1.15f;
            var rt = bodyLabel.rectTransform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(10f, 10f);
            rt.offsetMax = new Vector2(-10f, -10f);
        }

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

            if (boatConstruction != null)
            {
                builder.Append("배 제작: ").Append(boatConstruction.currentStage)
                    .Append(" / ").Append(BoatConstructionSystem.TotalStages)
                    .Append("단계 (도면 ").Append(boatConstruction.hasCurrentStageBlueprint ? "보유" : "없음").Append(")\n");
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
                builder.Append('[').Append(PreviewBoatEndingKey.ToString()).Append("] 배 엔딩   ")
                    .Append('[').Append(PreviewAircraftEndingKey.ToString()).Append("] 비행기 엔딩   ")
                    .Append('[').Append(PreviewGameOverKey.ToString()).Append("] 사망 화면\n");
                builder.Append("같은 키를 다시 누르면 닫힘\n");
                builder.Append('[').Append(ToggleFullMapKey.ToString()).Append("] 전체 지도 표시+자유 이동 (지금 ")
                    .Append(IslandTravel.DebugRevealAllActive ? "ON" : "OFF").Append(")\n");
            }
#endif

            builder.Append('\n').Append('[').Append(toggleKey.ToString()).Append("] 디버그 패널 끄기");

            bodyLabel.text = builder.ToString();
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
#endif
        }

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
