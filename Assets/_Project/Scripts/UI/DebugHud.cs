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

            builder.Append('\n').Append('[').Append(toggleKey.ToString()).Append("] 디버그 패널 끄기");

            bodyLabel.text = builder.ToString();
        }

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
