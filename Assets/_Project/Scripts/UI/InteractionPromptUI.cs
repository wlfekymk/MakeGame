using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using MakeGame.Data;
using MakeGame.Player;
using MakeGame.Systems;

namespace MakeGame.UI
{
    /// <summary>
    /// 조준 중인 대상이 무엇이고 어떤 키로 무엇을 할 수 있는지를 화면 중앙 아래에 한 줄로 보여주는 프롬프트.
    /// "상호작용" 같은 무의미한 문구 대신 항상 동사까지 보여준다(예: "[E] 대나무 채집", "[E] 모닥불 피우기").
    ///
    /// 가장 중요한 설계 원칙: **불가능한 이유도 반드시 보여준다.** 예전에는 손도끼 없이 금속조각을 쳐도
    /// (ResourceNode.Harvest가 조용히 false를 반환) 화면에 아무 변화가 없어, 플레이어가 "버그인가?"라고
    /// 생각할 수밖에 없었다. 이제 조건이 안 맞으면 프롬프트 전체를 회색으로 낮추고 아래 줄에 "손도끼 필요"
    /// 처럼 정확한 사유를 적는다.
    ///
    /// 판정 로직은 이 UI가 새로 만들지 않는다. 대상 컴포넌트가 이미 public으로 노출한 판정
    /// (ResourceNode.GetHarvestFailure, Campfire.isLit/fuelItem,
    /// AircraftRepairSystem.requiredMaterials, RaftStructure.DescribeState 등)과
    /// PlayerInventory의 보유 수량만 읽어 문장으로 옮긴다. UI가 같은 조건을 다시 구현하는 순간
    /// 화면에 보이는 것과 실제 동작이 조용히 갈라지기 때문이다.
    ///
    /// 레이캐스트도 같은 원칙이다: 조준 대상은 InteractionController.TryGetLookTarget(public)이 유일한
    /// 소스다. 예전에는 이 UI가 interactionCamera/interactionDistance를 직접 읽어 같은 조건의 레이를 한 번
    /// 더 쐈는데, 그러면 컨트롤러 쪽 판정에 레이어 마스크 하나만 추가돼도 "화면에 뜬 대상"과 "E키가 잡는
    /// 대상"이 어긋난다. 이 파일에 별도 레이캐스트를 다시 만들지 말 것.
    ///
    /// OnGUI(IMGUI)는 절대 쓰지 않는다 - IMGUI는 sortingOrder와 무관하게 Screen Space Overlay Canvas 위에
    /// 덮어 그려져 다른 UGUI 화면을 통째로 가려버린 사고가 있었다(GameOverController.OnGUI 사례).
    /// </summary>
    public class InteractionPromptUI : MonoBehaviour
    {
        [Tooltip("프롬프트 문구를 다시 계산하는 주기(초). 매 프레임 문자열을 새로 만들면 GC 할당이 쌓이므로 살짝 간격을 둔다.")]
        public float refreshInterval = 0.1f;

        // 팔레트(ArtDirection.md 1.1): 가능/불가 상태를 색으로만 구분한다(폰트는 크기·색 위계만 사용).
        private static readonly Color AvailableColor = Color.white;
        private static readonly Color BlockedColor = new Color(0.6f, 0.6f, 0.6f, 1f);   // 회색: 지금은 못 한다(사유 문구 포함)
        private static readonly Color SubInfoColor = new Color(0.8f, 0.8f, 0.8f, 1f);   // 보조 정보(본문)

        private InteractionController interaction;

        private GameObject panelRoot;
        private Text mainLabel;
        private Text subLabel;

        private float refreshTimer = 0f;

        /// <summary>
        /// 씬이 로드될 때마다(최초 시작이든 사망 후 재시작이든) 새 프롬프트 UI를 만든다.
        /// SurvivalHudUI와 동일한 패턴이다 - 참조 대상(InteractionController)이 씬과 함께 새로 생성되므로,
        /// AfterSceneLoad(최초 1회)로 만들면 재시작 후 죽은 참조를 들고 있게 된다.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Bootstrap()
        {
            SceneManager.sceneLoaded += (scene, mode) =>
            {
                var go = new GameObject("InteractionPromptUI");
                go.AddComponent<InteractionPromptUI>();
            };
        }

        /// <summary>
        /// 조준 판정에 쓸 InteractionController를 찾아두고 UI를 만든다.
        /// </summary>
        private void Start()
        {
            interaction = FindAnyObjectByType<InteractionController>();
            BuildUI();
            SetOpen(false);
        }

        /// <summary>
        /// 화면 중앙 살짝 아래(조준점 바로 밑)에 프롬프트 패널을 만든다.
        /// HUD류이므로 배경 알파는 0.55(ArtDirection.md 4.3), 상단 테두리 2px/흰색 12%를 넣는다.
        /// </summary>
        private void BuildUI()
        {
            // sortOrder 4: SurvivalHud(5)/상태이상 배너(6)/인벤·제작 모달(10)/피격 플래시(12)보다 아래에 둬서
            // 다른 화면을 절대 가리지 않게 한다. 프롬프트는 화면 중앙 아래라 다른 패널과 위치도 겹치지 않는다.
            var canvas = UIBuilder.CreateCanvas("InteractionPromptCanvas", sortOrder: 4);

            var panel = UIBuilder.CreatePanel(
                canvas.transform, "InteractionPromptPanel",
                anchorMin: new Vector2(0.5f, 0.5f), anchorMax: new Vector2(0.5f, 0.5f),
                offsetMin: new Vector2(-300f, -152f), offsetMax: new Vector2(300f, -80f),
                color: new Color(0f, 0f, 0f, 0.55f),
                addTopBorder: true);

            panelRoot = panel.gameObject;

            var vlg = panelRoot.AddComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(14, 14, 8, 8);
            vlg.spacing = 2f;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            vlg.childAlignment = TextAnchor.MiddleCenter;

            // 주 문구(H2 16): "[E] 대나무 채집"처럼 키 + 대상 + 동사를 한 줄로.
            mainLabel = UIBuilder.CreateText(panel, "Main", "", 16, AvailableColor, TextAnchor.MiddleCenter);
            mainLabel.gameObject.AddComponent<LayoutElement>().minHeight = 22f;

            // 보조 문구(Body 12): 불가 사유 또는 부가 정보(남은 채집 횟수, 부족한 재료 등).
            subLabel = UIBuilder.CreateText(panel, "Sub", "", 12, SubInfoColor, TextAnchor.MiddleCenter);
            subLabel.gameObject.AddComponent<LayoutElement>().minHeight = 18f;

            // 이 패널은 순수 정보 표시용이라 클릭을 절대 받아선 안 된다. 화면 정중앙 근처에 있어서
            // raycastTarget을 켜둔 채 두면 다른 UI(버튼)나 마우스 입력을 가로챌 위험이 있다.
            foreach (var graphic in panelRoot.GetComponentsInChildren<Graphic>(true))
                graphic.raycastTarget = false;
        }

        /// <summary>
        /// 일정 주기마다 조준 대상을 다시 확인해 프롬프트 문구를 갱신한다.
        /// </summary>
        private void Update()
        {
            // 타이틀/설정/게임오버/엔딩 화면은 Time.timeScale = 0으로 게임을 멈춘다(MainMenuController,
            // SettingsMenuController, GameOverController, EndingChecker). 그 위에 "[E] 대나무 채집" 같은
            // 프롬프트가 남아 있으면 안 되므로 멈춘 동안은 무조건 숨긴다.
            if (Time.timeScale <= 0f)
            {
                SetOpen(false);
                return;
            }

            // 타이머는 unscaled로 센다(위에서 이미 정지 상태를 걸러내므로 값이 튀지 않는다).
            refreshTimer -= Time.unscaledDeltaTime;
            if (refreshTimer > 0f)
                return;
            refreshTimer = Mathf.Max(0.02f, refreshInterval);

            if (interaction == null)
            {
                // 씬이 늦게 준비되는 경우를 대비해 다시 찾아본다(찾지 못하면 그냥 숨긴 상태 유지).
                interaction = FindAnyObjectByType<InteractionController>();
                if (interaction == null)
                {
                    SetOpen(false);
                    return;
                }
            }

            // [뗏목 항해] 조종 중에는 조준 대상과 무관하게 **항상** 항해 상태를 띄운다. 조종은
            // "무엇을 겨누고 있는가"와 상관없이 계속되는 모드이고(E도 조준과 무관하게 나가기다 -
            // InteractionController의 같은 분기), 속도·바람·연료·적재는 겨눌 대상이 없는 먼바다에서
            // 오히려 더 필요한 정보이기 때문이다. 문장은 전부 RaftSailing이 만든다(판정 재구현 금지).
            var sailing = MakeGame.Systems.RaftSailing.Active;
            if (sailing != null && sailing.IsSteering)
            {
                // 항해 문구는 회색으로 낮추지 않는다. 이 UI의 회색은 "지금은 못 한다"라는 뜻인데
                // (클래스 주석), 항해 중에 뜨는 것은 전부 진행 중인 사실과 경고라 뜻이 정반대다.
                ShowPrompt(
                    sailing.GetSteeringHeadline(interaction.interactKey.ToString()),
                    sailing.GetSteeringDetail(),
                    false);
                return;
            }

            // 조준 판정은 InteractionController가 실제 상호작용에 쓰는 그 메서드를 그대로 부른다
            // (UI 전용 레이캐스트 사본 금지 - 클래스 주석 참고).
            if (!interaction.TryGetLookTarget(out GameObject target))
            {
                SetOpen(false);
                return;
            }

            if (!TryBuildPrompt(target, out string main, out string sub, out bool blocked))
            {
                SetOpen(false);
                return;
            }

            ShowPrompt(main, sub, blocked);
        }

        /// <summary>
        /// 프롬프트 두 줄을 실제로 화면에 올린다. 조준 대상에서 만든 문구와 항해 상태 문구가 **같은
        /// 한 곳**을 통과하도록 뽑아 둔 것이다(색 규칙이 두 벌이 되지 않게).
        ///
        /// 불가 상태는 문구 전체를 회색으로 낮춘다 - "지금은 못 하지만 조건만 갖추면 된다"는 신호이지
        /// 위험 경고(Danger Red)가 아니기 때문이다. 색은 팔레트 밖으로 나가지 않는 무채색만 쓴다.
        /// 문자열이 실제로 달라졌을 때만 Text에 대입한다(레이아웃 재계산을 매 갱신마다 부르지 않게).
        /// </summary>
        private void ShowPrompt(string main, string sub, bool blocked)
        {
            SetOpen(true);

            mainLabel.color = blocked ? BlockedColor : AvailableColor;
            subLabel.color = blocked ? BlockedColor : SubInfoColor;

            if (mainLabel.text != main)
                mainLabel.text = main;
            if (subLabel.text != sub)
                subLabel.text = sub;
        }

        /// <summary>
        /// 대상 오브젝트에 붙은 컴포넌트 종류를 InteractionController.InteractWithTarget과 **동일한 우선순위**로
        /// 확인해 문구를 만든다(순서가 어긋나면 화면에 보이는 행동과 실제 실행되는 행동이 달라진다).
        /// 표시할 것이 없으면 false를 반환해 프롬프트를 숨긴다.
        /// </summary>
        private bool TryBuildPrompt(GameObject target, out string main, out string sub, out bool blocked)
        {
            main = "";
            sub = "";
            blocked = false;

            string key = $"[{interaction.interactKey}]";
            PlayerInventory inventory = interaction.inventory;

            var resourceNode = target.GetComponent<ResourceNode>();
            if (resourceNode != null)
                return BuildResourceNodePrompt(resourceNode, inventory, key, out main, out sub, out blocked);

            var creature = target.GetComponent<HuntableCreature>();
            if (creature != null)
            {
                if (!creature.IsAvailable)
                    return false;

                string yieldName = creature.yieldItem != null ? creature.yieldItem.itemName : "사냥감";
                main = $"{key} {yieldName} 사냥";

                if (creature.requiredTool != null && (inventory == null || inventory.FindItem(creature.requiredTool) == null))
                {
                    blocked = true;
                    sub = $"{creature.requiredTool.itemName} 필요";
                }
                else
                {
                    sub = $"성공률 {Mathf.RoundToInt(creature.successChance * 100f)}% · 실패해도 도망간다";
                }
                return true;
            }

            var waterStill = target.GetComponent<WaterStill>();
            if (waterStill != null)
            {
                main = $"{key} 증류수 마시기";
                if (waterStill.storedWater <= 0.01f)
                {
                    blocked = true;
                    sub = "아직 물이 모이지 않음";
                }
                else
                {
                    sub = $"모인 물 {waterStill.storedWater:F1} / {waterStill.maxStorage:F0}";
                }
                return true;
            }

            var campfire = target.GetComponent<Campfire>();
            if (campfire != null)
                return BuildCampfirePrompt(campfire, inventory, key, out main, out sub, out blocked);

            // [뗏목 재배선] 배 도면 습득 지점 / 배 작업대 프롬프트는 두 컴포넌트가 삭제되면서 사라졌다
            // ("n/3단계" 문구도 함께 없어졌다 - 뗏목에는 단계가 없다).
            //
            // **뗏목 프롬프트는 여기가 아니라 이 메서드 맨 아래에 있다.** InteractionController가
            // 뗏목 분기를 모든 분기의 뒤로 보냈기 때문이다(갑판 위 건축 부품·상자가 뗏목의 자손이라
            // 앞에 두면 그 둘을 통째로 가린다 - 그쪽 주석 참고). 이 파일의 절대 규칙은 "컨트롤러와
            // **동일한 우선순위**"이므로 여기서도 같은 자리에 둔다.

            var wreck = target.GetComponent<AircraftWreck>();
            if (wreck != null)
                return BuildAircraftWreckPrompt(wreck, inventory, key, out main, out sub, out blocked);

            // 여객기 내부 부품 수거 지점: InteractionController와 같은 이유로 **AirlinerWreck 분기보다
            // 먼저** 검사한다 - 지점은 잔해의 자식이라 순서가 바뀌면 프롬프트가 항상 "잔해 수색"으로
            // 나와 화면과 E키 동작이 갈라진다.
            var salvagePoint = target.GetComponentInParent<AirlinerSalvagePoint>();
            if (salvagePoint != null)
                return BuildSalvagePointPrompt(salvagePoint, key, out main, out sub, out blocked);

            // 여객기 잔해: 콜라이더가 동체/날개 등 자식 파츠에 붙어 있으므로 InParent
            // (InteractionController의 AirlinerWreck 분기와 같은 기준·같은 우선순위 위치).
            var airliner = target.GetComponentInParent<AirlinerWreck>();
            if (airliner != null)
                return BuildAirlinerWreckPrompt(airliner, key, out main, out sub, out blocked);

            var hazard = target.GetComponent<HazardSource>();
            if (hazard != null)
                return BuildHazardPrompt(hazard, inventory, key, out main, out sub, out blocked);

            var shelter = target.GetComponent<Shelter>();
            if (shelter != null)
                return BuildShelterPrompt(shelter, inventory, key, out main, out sub, out blocked);

            // [건축 4티어] 건축 부품 조준 시 티어 승급 프롬프트. InteractionController와 같은 우선순위
            // 위치(모든 분기의 맨 뒤)이고, 부품 식별도 같은 소스(BuildingSystem.TryGetPieceTier -
            // 격자 역조회)만 쓴다 - UI가 부품 판정을 따로 만들지 않는다(클래스 주석의 원칙).
            var building = BuildingSystem.Instance;
            if (building != null && building.TryGetPieceTier(target.transform, out BuildPieceType pieceType, out int pieceTier))
                return BuildPieceUpgradePrompt(building, pieceType, pieceTier, key, out main, out sub, out blocked);

            // [뗏목 항해] 조타 자리. InteractionController와 **같은 자리**(뗏목 제작 분기 바로 앞)다 -
            // 순서가 뒤집히면 화면에는 "조종 시작"이 뜨는데 E는 제작 창을 열게 된다.
            var helm = target.GetComponent<RaftHelm>();
            if (helm != null && helm.sailing != null)
            {
                main = helm.sailing.GetHelmHeadline(key);
                sub = helm.sailing.GetHelmDetail(out blocked);
                return true;
            }

            // [뗏목 제작] 모든 분기의 맨 뒤 - InteractionController.InteractWithTarget의 뗏목 분기와
            // 같은 자리다(순서가 어긋나면 화면에 보이는 행동과 E키가 실제로 하는 행동이 갈라진다).
            var raft = target.GetComponentInParent<RaftStructure>();
            if (raft != null)
                return BuildRaftPrompt(raft, key, out main, out sub, out blocked);

            return false;
        }

        /// <summary>
        /// 뗏목(또는 바닥판 0칸일 때의 "제작 예정지") 프롬프트. 예: "[E] 뗏목 제작" / "바닥판 3/8 · 돛".
        ///
        /// 상태 문장은 <see cref="RaftStructure.DescribeState"/> **하나만** 쓴다. HUD·퀘스트·디버그
        /// 패널·제작 창이 전부 같은 문장을 쓰는 단일 출처이므로, 여기서 "칸 3개, 부품 1개" 같은 문장을
        /// 새로 조립하면 화면마다 다른 표현이 생긴다.
        ///
        /// 재료가 모자란지는 여기서 보지 않는다 - 항목이 아홉 개라 한 줄에 담을 수 없고, 창을 열면
        /// 줄마다 필요/보유가 숫자로 나온다. 프롬프트의 역할은 "여기서 창이 열린다"까지다.
        /// 그래서 blocked도 항상 false다(뗏목은 언제 조준해도 창이 열린다).
        /// </summary>
        private bool BuildRaftPrompt(RaftStructure raft, string key,
            out string main, out string sub, out bool blocked)
        {
            blocked = false;

            if (raft.BaseTileCount <= 0)
            {
                // 아직 아무것도 없는 상태. "제작"이 아니라 "시작"이라고 말해야 여기서 뗏목이라는
                // 기능이 시작된다는 것이 전달된다(이 표시가 유일한 발견 경로다).
                main = $"{key} 뗏목 만들기 시작";
                sub = $"제작 예정지 · {raft.DescribeState()}";
                return true;
            }

            main = $"{key} 뗏목 제작";
            sub = raft.DescribeState();
            return true;
        }

        /// <summary>
        /// [건축 4티어] 부품 티어 승급 프롬프트. 예: "[E] 『벽』 2티어(돌) 승급 - 석재 2".
        /// 승급비·티어 이름은 전부 BuildPieceCatalog(단일 소스)에서 읽고, 보유 수량 대조는 실제 소모와
        /// 같은 경로(BuildingSystem.CountOwned - 이름 문자열 대조)를 쓴다. 최고 티어(4=대리석)는
        /// 숨기지 않고 회색으로 사실을 알려준다("눌러도 아무 일이 없다"가 가장 나쁜 경험이기 때문이다).
        /// </summary>
        private bool BuildPieceUpgradePrompt(BuildingSystem building, BuildPieceType pieceType, int tier,
            string key, out string main, out string sub, out bool blocked)
        {
            string pieceName = BuildPieceCatalog.GetDisplayName(pieceType);

            if (tier >= BuildPieceCatalog.PieceTierCount)
            {
                main = $"『{pieceName}』 {tier}티어({BuildPieceCatalog.GetPieceTierDisplayName(tier)})";
                sub = "최고 티어 - 더 승급할 수 없다";
                blocked = true;
                return true;
            }

            int nextTier = tier + 1;
            main = $"{key} 『{pieceName}』 {nextTier}티어({BuildPieceCatalog.GetPieceTierDisplayName(nextTier)}) 승급";

            var cost = BuildPieceCatalog.GetPieceUpgradeCost(pieceType, tier);
            var costParts = new System.Collections.Generic.List<string>();
            var missingParts = new System.Collections.Generic.List<string>();
            for (int i = 0; i < cost.Count; i++)
            {
                if (string.IsNullOrEmpty(cost[i].itemName) || cost[i].count <= 0)
                    continue;

                costParts.Add($"{cost[i].itemName} {cost[i].count}");

                int shortage = cost[i].count - building.CountOwned(cost[i].itemName);
                if (shortage > 0)
                    missingParts.Add($"{cost[i].itemName} {shortage}개");
            }

            blocked = missingParts.Count > 0;
            sub = blocked
                ? $"부족: {string.Join(", ", missingParts)}"
                : string.Join(", ", costParts);
            return true;
        }

        /// <summary>
        /// [game-designer 요청] 쉼터 취침 프롬프트.
        ///
        /// 왜 문구를 이렇게까지 자세히 쓰는가: Design_BalancePass.md 2장에 따르면 Shelter.TrySleep은 시계를
        /// 다음 날 일출로 **점프**시키므로, 매일 밤 취침하는 플레이어는 15일 조건을 147분이 아니라 74.5분에
        /// 끝낸다. 2배 차이가 순수하게 "이 기능을 아는가"에서 나오는데 지금까지 게임 어디에도 안내가
        /// 없었다(같은 문서가 "그 전제는 아직 참이 아니다"라고 못박은 지점). 이 프롬프트가 유일한 발견
        /// 경로이므로, "잘 수 있다"가 아니라 **얼마나 건너뛰는지**를 숫자로 보여준다.
        ///
        /// 낮에도 "밤이 되면 무엇을 할 수 있는지"를 함께 알려준다 - 쉼터를 지어놓고도 밤에 다시 찾아올
        /// 이유를 모르면 기능이 없는 것과 같기 때문이다. 판정(밤인지)과 실제 점프는 전부
        /// SurvivalClock/Shelter가 하고, 여기서는 그 값을 읽어 문장으로만 옮긴다.
        /// </summary>
        private bool BuildShelterPrompt(Shelter shelter, PlayerInventory inventory, string key,
            out string main, out string sub, out bool blocked)
        {
            main = $"{key} 쉼터에서 취침";
            sub = "";
            blocked = false;

            var clock = interaction.survivalClock;
            if (clock == null)
            {
                blocked = true;
                sub = "지금은 잠들 수 없다";
                return true;
            }

            if (clock.IsDaytime)
            {
                // [정착 배치 1] 낮에는 E가 취침이 아니라 **건축**이다(Shelter.TryBuildNext).
                // 예전에는 "밤에만 취침할 수 있다"고 회색으로 막아 뒀는데, 이제 낮의 E는 실제로
                // 동작하는 행동이라 막힌 것처럼 보이면 안 된다. 재료가 모자랄 때만 blocked로 둔다.
                string next = shelter.DescribeNextBuildAction(inventory);
                main = $"{key} 집 짓기 (Lv{shelter.level})";
                sub = next;
                blocked = !next.EndsWith("가능");
                return true;
            }

            // 목적지 계산은 **전혀 하지 않는다** - Shelter가 실제 취침에 쓰는 public static을 그대로
            // 부른다. 예전에는 여기서 `ElapsedDays + 1`을 자체 계산했는데, systems가 자정 이후 취침이
            // 1.25일을 건너뛰던 버그를 Shelter 쪽에서만 고쳐(GetWakeDay 주석 참고) 프롬프트만 틀린
            // 값을 말하게 됐다(자정 직후 실제 2.4분인데 "12.4분", 날짜도 하루 밀림). 채집 프롬프트가
            // ResourceNode.GetHarvestFailure의 결론만 받아 옮기는 것과 같은 규칙을 적용한다.
            // 시계는 실시간 1초당 1초씩 흐르므로(SurvivalClock.Update) 건너뛰는 게임 내 초 = 아껴지는 실제 초다.
            // 두 메서드는 clock이 null이거나 secondsPerDay <= 0이면 0을 돌려주는데, 위에서 clock null을
            // 이미 걸렀고 secondsPerDay <= 0인 씬이면 Shelter.TrySleep 자체가 실패하므로 표시도 0이 맞다.
            int wakeDay = Shelter.GetWakeDay(clock);
            float wakeSeconds = Shelter.GetWakeSeconds(clock);
            float skipped = Mathf.Max(0f, wakeSeconds - clock.elapsedSeconds);

            // HUD의 "N일차"와 같은 기준(0 = 1일차)으로 눈뜰 날짜를 표기한다.
            main = $"{key} 쉼터에서 취침 - {FormatSkipDuration(skipped)} 건너뛰기";
            sub = $"{wakeDay + 1}일차 아침으로 이동 · 체력 {shelter.CurrentSleepHealAmount:F0} 회복 · 자는 동안 허기·갈증 소모 없음";
            return true;
        }

        /// <summary>
        /// 건너뛰는 시간(초)을 프롬프트 한 줄에 들어갈 짧은 문구로 만든다. 1분 미만은 초 단위로 올림해
        /// "0분"처럼 아무 것도 아닌 것처럼 보이는 표시를 피한다.
        /// </summary>
        private static string FormatSkipDuration(float seconds)
        {
            if (seconds < 60f)
                return $"{Mathf.CeilToInt(seconds)}초";

            return $"약 {Mathf.RoundToInt(seconds / 60f)}분";
        }

        /// <summary>
        /// 채집 노드 프롬프트. 가능 여부 판정은 **전혀 하지 않는다** - ResourceNode.GetHarvestFailure가
        /// Harvest()와 같은 코드로 내려준 결론을 받아 한국어 문구로 옮기기만 한다.
        /// (예전에는 이 자리에서 CanHarvest/requiresTool/FindItem을 UI가 다시 조합했는데, 그러면
        /// 채집 조건이 한 줄만 바뀌어도 화면 문구가 조용히 거짓말을 하기 시작한다.)
        /// 매 프레임 호출해도 상태가 바뀌지 않고 소리도 나지 않는다(GetHarvestFailure 주석의 보장).
        /// </summary>
        private bool BuildResourceNodePrompt(ResourceNode node, PlayerInventory inventory, string key,
            out string main, out string sub, out bool blocked)
        {
            string yieldName = node.yieldItem != null ? node.yieldItem.itemName : "자원";

            // 수확량은 성공/실패와 무관하게 항상 보여준다 - "이걸 치면 몇 개가 들어오는가"는 어느 노드를
            // 먼저 칠지 고르는 정보라, 도구가 없어 지금 막혀 있을 때도 알아야 하는 값이다.
            int yield = GetEffectiveYieldPerHarvest(node, inventory);
            main = $"{key} {yieldName} 채집 (+{yield})";

            ResourceNode.HarvestFailure failure = node.GetHarvestFailure(inventory);
            blocked = failure != ResourceNode.HarvestFailure.None;
            sub = blocked
                ? GetHarvestFailureText(node, failure)
                : $"남은 채집 {node.remainingHarvestCount}회";
            return true;
        }

        /// <summary>
        /// 채집 실패 사유(enum) 하나를 플레이어가 읽을 문장으로 바꾼다. 이 메서드가 UI가 채집에 대해
        /// 하는 일의 전부다 - 조건 판정은 ResourceNode가 이미 끝냈다.
        /// NoInventory/NoYieldItem은 플레이어가 해결할 수 있는 사유가 아니지만(배선/데이터 오류),
        /// 그렇다고 프롬프트를 숨기면 다시 "쳐도 아무 일이 없다"가 되므로 "지금은 채집할 수 없다"고
        /// 분명히 말한다.
        /// </summary>
        private static string GetHarvestFailureText(ResourceNode node, ResourceNode.HarvestFailure failure)
        {
            switch (failure)
            {
                case ResourceNode.HarvestFailure.Depleted:
                    return "지금은 고갈됨 - 잠시 뒤 다시 자란다";

                case ResourceNode.HarvestFailure.MissingTool:
                    // 실측(Balance_SceneSnapshot.md): 금속조각은 손도끼가 있어야 채집된다.
                    return node.requiredTool != null ? $"{node.requiredTool.itemName} 필요" : "도구 필요";

                case ResourceNode.HarvestFailure.InventoryFull:
                    // [B18] 용량 도입으로 생긴 사유. "가방이 가득 찼다"만 말하면 무엇을 버려야 할지
                    // 모르므로, 버릴 수 있는 곳(인벤토리 키)까지 알려준다.
                    return "가방이 가득 찼다 - Tab에서 정리하거나 저장궤에 넣어라";

                case ResourceNode.HarvestFailure.NoInventory:
                case ResourceNode.HarvestFailure.NoYieldItem:
                default:
                    return "지금은 채집할 수 없다";
            }
        }

        /// <summary>
        /// 이 노드를 한 번 채집하면 실제로 몇 개가 들어오는지. **수확량 계산은 이 메서드 한 곳에서만 한다**
        /// - 프롬프트 문구가 여러 군데서 각자 계산하면 보너스가 붙는 순간 서로 다른 숫자를 표시한다.
        ///
        /// 지금은 ResourceNode.Harvest의 지급 루프(yieldPerHarvest회 AddItem)와 정확히 같은 값이다.
        /// [B7 디렉터] 보너스 도구(bonusTool / bonusYieldPerHarvest)가 ResourceNode에 들어왔으므로
        /// 판정을 그쪽 단일 소스에 위임한다. GetEffectiveYield는 상태를 바꾸지 않고 inventory가 null이어도
        /// 기본 수확량을 돌려주므로 매 프레임 호출해도 안전하다.
        /// 수확량 계산이 이 프로젝트에 두 벌 생기면 "표시는 +3인데 실제로는 +2"가 되므로, 여기서 직접
        /// 더하지 말고 반드시 ResourceNode 쪽만 고칠 것.
        /// </summary>
        private static int GetEffectiveYieldPerHarvest(ResourceNode node, PlayerInventory inventory)
        {
            return node.GetEffectiveYield(inventory);
        }

        /// <summary>
        /// 모닥불 프롬프트. 꺼져 있으면 점화(발화 도구 + 연료), 켜져 있으면 장작 추가/조리를 안내한다.
        /// 점화 조건 판정 자체는 Campfire.TryLight의 규칙(파이어스타터 우선, 없으면 라이터, 연료 1개 소모)을
        /// 그대로 읽어 문장으로만 옮긴다.
        /// </summary>
        private bool BuildCampfirePrompt(Campfire campfire, PlayerInventory inventory, string key,
            out string main, out string sub, out bool blocked)
        {
            main = "";
            sub = "";
            blocked = false;

            bool needsStarter = campfire.fireStarterItem != null || campfire.alternateFireStarterItem != null;
            bool hasStarter = !needsStarter || HasAnyOf(inventory, campfire.fireStarterItem, campfire.alternateFireStarterItem);
            bool hasFuel = campfire.fuelItem == null || (inventory != null && inventory.GetItemCount(campfire.fuelItem) > 0);
            string starterName = campfire.fireStarterItem != null
                ? campfire.fireStarterItem.itemName
                : (campfire.alternateFireStarterItem != null ? campfire.alternateFireStarterItem.itemName : "발화 도구");
            string fuelName = campfire.fuelItem != null ? campfire.fuelItem.itemName : "연료";

            if (!campfire.isLit)
            {
                main = $"{key} 모닥불 피우기";
                if (!hasStarter)
                {
                    blocked = true;
                    sub = $"{starterName} 필요";
                }
                else if (!hasFuel)
                {
                    blocked = true;
                    sub = $"{fuelName} 필요";
                }
                else
                {
                    sub = $"{fuelName} 1개 소모 · {campfire.secondsPerFuel:F0}초 유지";
                }
                return true;
            }

            // 켜져 있을 때: 연료가 있으면 장작 추가가 주 행동, 없으면 조리라도 안내한다("아무 반응 없음" 방지).
            ItemData rawFood = FindFirstCookableFood(inventory);
            if (hasFuel)
            {
                main = $"{key} 모닥불에 {fuelName} 넣기";
                sub = rawFood != null
                    ? $"연료 {campfire.remainingFuelSeconds:F0}초 남음 · [{interaction.cookKey}] {rawFood.itemName} 굽기"
                    : $"연료 {campfire.remainingFuelSeconds:F0}초 남음";
                return true;
            }

            if (rawFood != null)
            {
                main = $"[{interaction.cookKey}] {rawFood.itemName} 굽기";
                sub = $"{fuelName}이(가) 없어 장작은 넣을 수 없다";
                return true;
            }

            main = $"{key} 모닥불에 {fuelName} 넣기";
            blocked = true;
            sub = $"{fuelName} 필요";
            return true;
        }

        /// <summary>
        /// 경비행기 잔해 프롬프트. 필요 재료는 AircraftRepairSystem에서 읽는다.
        /// </summary>
        private bool BuildAircraftWreckPrompt(AircraftWreck wreck, PlayerInventory inventory, string key,
            out string main, out string sub, out bool blocked)
        {
            main = "";
            sub = "";
            blocked = false;

            var repair = wreck.repairSystem;
            if (repair == null)
            {
                main = $"{key} 경비행기 수리";
                blocked = true;
                sub = "수리 진행 정보를 찾을 수 없다";
                return true;
            }

            if (repair.isRepairComplete)
            {
                main = "경비행기 수리 완료";
                blocked = true;
                sub = "더 이상 투입할 재료가 없다";
                return true;
            }

            main = $"{key} 경비행기 수리 ({Mathf.RoundToInt(repair.GetOverallProgress() * 100f)}%)";

            string missing = BuildMissingMaterialsText(inventory, repair.requiredMaterials, repair);
            sub = string.IsNullOrEmpty(missing)
                ? "재료 충족 - 지금 수리를 완료할 수 있다"
                : $"부족: {missing}";
            return true;
        }

        /// <summary>
        /// 여객기 잔해 프롬프트. 수색 가능 여부 판정은 AirlinerWreck.HasSalvage(TrySearch와 같은 소스)만
        /// 읽어 문장으로 옮긴다. 이미 수색을 마친 잔해도 프롬프트를 숨기지 않고 회색으로 사실을
        /// 알려준다 - "눌러도 아무 일이 없다"가 가장 나쁜 경험이기 때문이다(경비행기 수리 완료와 같은 방식).
        /// </summary>
        private bool BuildAirlinerWreckPrompt(AirlinerWreck airliner, string key,
            out string main, out string sub, out bool blocked)
        {
            if (airliner.HasSalvage)
            {
                main = $"{key} 여객기 잔해 수색";
                sub = "1회 한정 - 남은 물자를 챙긴다";
                blocked = false;
                return true;
            }

            main = "이미 수색한 여객기 잔해";
            sub = "더 나올 물자가 없다";
            blocked = true;
            return true;
        }

        /// <summary>
        /// 여객기 내부 부품 수거 지점 프롬프트. 판정은 AirlinerSalvagePoint.HasLoot(TryCollect와 같은
        /// 소스)만 읽어 문장으로 옮긴다. 이미 수거한 지점도 숨기지 않고 회색으로 사실을 알려준다
        /// (여객기 잔해 수색/경비행기 수리 완료와 같은 방식 - "눌러도 아무 일이 없다"가 가장 나쁘다).
        /// </summary>
        private bool BuildSalvagePointPrompt(AirlinerSalvagePoint point, string key,
            out string main, out string sub, out bool blocked)
        {
            string name = string.IsNullOrEmpty(point.displayName) ? "부품 더미" : point.displayName;

            if (point.HasLoot)
            {
                main = $"{key} {name} 수거";
                sub = "1회 한정 - 부품을 챙긴다";
                blocked = false;
                return true;
            }

            main = $"이미 수거한 {name}";
            sub = "더 나올 부품이 없다";
            blocked = true;
            return true;
        }

        /// <summary>
        /// 위험 요소 프롬프트. 전투 대상이 아닌 위험 요소(벌떼/함정 등)는 "공격할 수 없다"는 사실 자체가
        /// 정보이므로 회색으로 알려준다 - 때려도 아무 일이 없는 것이 가장 나쁜 경험이기 때문이다.
        /// </summary>
        private bool BuildHazardPrompt(HazardSource hazard, PlayerInventory inventory, string key,
            out string main, out string sub, out bool blocked)
        {
            main = "";
            sub = "";
            blocked = false;

            if (!hazard.IsActive)
                return false;

            string hazardName = GetHazardDisplayName(hazard.hazardType);

            if (!hazard.isCombatTarget)
            {
                main = hazardName;
                blocked = true;
                sub = "공격할 수 없다 - 접촉하면 피해를 입는다";
                return true;
            }

            InventoryItem weapon = FindBestWeapon(inventory);
            main = $"{key} {hazardName} 공격";

            if (weapon == null)
            {
                blocked = true;
                sub = "무기 필요";
                return true;
            }

            sub = $"{weapon.data.itemName} 사용 · 남은 체력 {hazard.currentHealth:F0}/{hazard.maxHealth:F0}";
            return true;
        }

        /// <summary>
        /// 경비행기 수리(AircraftRepairSystem) 요구 재료 목록에서 아직 부족한 항목을 "이름 n개" 형태로 잇는다.
        /// </summary>
        private string BuildMissingMaterialsText(PlayerInventory inventory,
            System.Collections.Generic.List<AircraftRepairSystem.MaterialRequirement> requirements,
            AircraftRepairSystem repair)
        {
            if (requirements == null)
                return "";

            var parts = new System.Collections.Generic.List<string>();
            foreach (var req in requirements)
            {
                if (req == null || req.item == null)
                    continue;

                int owned = repair.GetCollectedQuantity(req.item) + (inventory != null ? inventory.GetItemCount(req.item) : 0);
                int shortage = req.quantity - owned;
                if (shortage > 0)
                    parts.Add($"{req.item.itemName} {shortage}개");
            }

            return string.Join(", ", parts);
        }

        /// <summary>
        /// 두 후보 아이템 중 하나라도 인벤토리에 있으면 true (모닥불 발화 도구 판정용).
        /// </summary>
        private static bool HasAnyOf(PlayerInventory inventory, ItemData first, ItemData second)
        {
            if (inventory == null)
                return false;

            if (first != null && inventory.FindItem(first) != null)
                return true;

            return second != null && inventory.FindItem(second) != null;
        }

        /// <summary>
        /// 인벤토리에서 모닥불로 구울 수 있는(생음식 + 조리 결과가 지정된) 첫 아이템을 찾는다.
        /// InteractionController.CookFirstRawFoodAtTarget이 실제로 고르는 것과 같은 "첫 생음식"이다.
        /// </summary>
        private static ItemData FindFirstCookableFood(PlayerInventory inventory)
        {
            if (inventory == null)
                return null;

            foreach (var item in inventory.items)
            {
                if (item.data != null && item.data.isRawFood && item.data.cookedResult != null)
                    return item.data;
            }
            return null;
        }

        /// <summary>
        /// 인벤토리에서 공격에 실제로 쓰일 무기(피해량이 가장 큰 무기)를 찾는다.
        /// HazardSource.TryAttack이 무기가 없으면 조용히 실패하므로, "무기 필요"를 미리 알려주기 위해 필요하다.
        /// </summary>
        private static InventoryItem FindBestWeapon(PlayerInventory inventory)
        {
            if (inventory == null)
                return null;

            InventoryItem best = null;
            foreach (var item in inventory.items)
            {
                if (item.data == null || !item.data.isWeapon)
                    continue;

                if (best == null || item.data.weaponDamage > best.data.weaponDamage)
                    best = item;
            }
            return best;
        }

        /// <summary>
        /// 위험 요소 종류의 한국어 표시 이름. HazardType 자체에는 표시용 이름이 없어 UI 쪽에서만 매핑한다
        /// (게임 규칙이 아니라 순수 표시 문자열이므로 여기 두는 것이 맞다).
        /// </summary>
        private static string GetHazardDisplayName(HazardType type)
        {
            switch (type)
            {
                case HazardType.VenomousSnake: return "독사";
                case HazardType.Scorpion: return "전갈";
                case HazardType.Bear: return "곰";
                case HazardType.BeeSwarm: return "벌떼";
                case HazardType.Trap: return "함정";
                case HazardType.Cannibal: return "식인종";
                case HazardType.Shark: return "상어";
                default: return "위험 요소";
            }
        }

        /// <summary>
        /// 프롬프트 패널을 켜거나 끈다.
        /// </summary>
        private void SetOpen(bool open)
        {
            if (panelRoot != null && panelRoot.activeSelf != open)
                panelRoot.SetActive(open);
        }
    }
}
