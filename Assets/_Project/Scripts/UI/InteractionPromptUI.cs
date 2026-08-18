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

        /// <summary>제작 창 키를 읽어 오는 CraftingUI 캐시(제작대 프롬프트 전용). GetCraftToggleKey 참고.</summary>
        private CraftingUI craftingUI;

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
            // 조준 대상이 없거나(바다를 바라보는 경우 - 바다 평면은 콜라이더가 지워져 있고 레이도
            // 4m뿐이다) 대상에 붙일 문구가 없을 때만 낚시 안내가 들어간다.
            //
            // [낚시 - 분기 순서 규약] 이 자리는 **기존 어떤 분기도 가리지 않는다.** TryBuildPrompt가
            // false를 돌려준 뒤, 즉 예전 코드가 프롬프트를 통째로 끄던 그 자리에만 끼어들기 때문이다.
            // TryBuildPrompt 안(분기 사슬)에 넣지 않은 이유가 이것이다 - 거기 넣으면 순서에 따라
            // E 동사를 가진 분기를 덮을 수 있다. 조준 대상이 잡히는 자리(뗏목 갑판 등)에서의 안내는
            // 문장 끝에 붙는 꼬리말(BuildFishHint)이 맡는다.
            // (&&로 이은 out 변수는 조건이 참인 분기 안에서 확실히 대입돼 있다 - 이 파일의
            //  BuildingSystem.TryGetPieceTier 분기가 이미 쓰고 있는 것과 같은 형태다.)
            if (interaction.TryGetLookTarget(out GameObject target)
                && TryBuildPrompt(target, out string main, out string sub, out bool blocked))
            {
                ShowPrompt(main, sub, blocked);
                return;
            }

            if (TryBuildFishingPrompt(out string fishMain, out string fishSub, out bool fishBlocked))
            {
                ShowPrompt(fishMain, fishSub, fishBlocked);
                return;
            }

            SetOpen(false);
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

            // [전투 깊이 확장 - 땅에 꽂힌 창] 이 분기는 **어떤 기존 분기도 가리지 않는다.**
            // ThrownWeapon은 런타임에 자기 전용 오브젝트로만 생기고 그 오브젝트에는 다른 상호작용
            // 컴포넌트가 붙지 않으므로, 순서를 앞에 둬도 채집/사냥/상자가 이 분기에 먹힐 수 없다.
            //
            // ⚠️ InteractionController에는 대응 분기가 **없다**(그 파일은 이 작업의 락 밖이다).
            // 그래도 "화면과 E키가 갈라지는" 문제가 생기지 않는 이유: 여기서 안내하는 행동이 E가
            // 아니라 **접근**이기 때문이다. 창을 겨눈 채 E를 눌러도 컨트롤러 쪽에서 아무 분기에도
            // 걸리지 않아 조용히 지나가고, 화면은 처음부터 E를 약속하지 않는다.
            var thrownWeapon = target.GetComponent<ThrownWeapon>();
            if (thrownWeapon != null)
                return BuildThrownWeaponPrompt(thrownWeapon, inventory, out main, out sub, out blocked);

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
                    sub = $"성공률 {Mathf.RoundToInt(creature.successChance * 100f)}% · 실패해도 도망간다"
                        + BuildThrowHint(inventory);
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

            // [식량 루프 - 훈연기] Smoker는 **Campfire와 같은 오브젝트에** 붙는다(Smoker 클래스 주석:
            // E는 Campfire.TryLight로, R은 Campfire.CookItem → Smoker.TryInsert로 들어간다). 그래서
            // Campfire 분기가 먼저 걸리면 훈연기를 조준해도 화면에는 "모닥불 피우기"가 떴다.
            //
            // 자리는 **Campfire 분기 바로 앞**이다(분기 순서 규약을 어기지 않는다):
            //  · E가 실제로 하는 일은 두 분기가 완전히 같다(점화 / 장작 추가 - 둘 다 Campfire의 것).
            //    즉 순서를 바꿔도 "화면이 약속한 행동"과 "E키가 하는 행동"이 갈라지지 않는다. 바뀌는
            //    것은 문구뿐이다.
            //  · 평범한 모닥불에는 Smoker가 없으므로 이 검사 한 번 외에 기존 경로는 그대로다.
            var smoker = target.GetComponent<Smoker>();
            var campfire = target.GetComponent<Campfire>();
            if (smoker != null)
                return BuildSmokerPrompt(smoker, campfire, inventory, key, out main, out sub, out blocked);

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

            // [농사] 밭. 자리는 **E 동사를 가진 분기들 사이**이고, 위의 어떤 분기도 가리지 않는다 -
            // 밭 오브젝트에는 상자/자원/사냥감/증류기/모닥불/훈연기/잔해/위험요소/쉼터 컴포넌트가
            // 하나도 붙지 않기 때문이다(FarmPlot은 자기 오브젝트를 통째로 소유한다).
            //
            // ⚠️ InteractionController에는 대응 분기가 **없다**(그 파일은 이 작업의 락 밖이다). 그런데도
            // 여기서 E를 약속해도 되는 이유는 ThrownWeapon 분기와 다르다: 밭은 E를 **자기가 직접 읽는다**
            // (FarmPlot.TryHandleInteractKey - 조준 판정은 InteractionController.TryGetLookTarget을
            // 그대로 빌려 오므로 화면이 잡는 대상과 키가 잡는 대상이 같은 레이 하나다). 그래서
            // "화면은 E라는데 눌러도 아무 일이 없다"가 생기지 않는다.
            //
            // GetComponentInParent를 쓰는 이유는 상자/뗏목과 같다 - 지금은 루트에만 콜라이더가 있지만
            // 나중에 시각 파츠에 콜라이더가 붙어도 같은 밭으로 이어지게 한다(FarmPlot 쪽 키 처리도
            // 정확히 같은 조회를 쓴다 - 두 곳이 갈라지지 않게).
            var farmPlot = target.GetComponentInParent<FarmPlot>();
            if (farmPlot != null)
                return BuildFarmPlotPrompt(farmPlot, inventory, key, out main, out sub, out blocked);

            // [제작대 3종] 조준하면 이름이라도 뜨게 한다. 자리는 **E 동사를 가진 분기 전부의 뒤,
            // 폴백 분기(건축 부품 승급 / 조타 / 뗏목)의 앞**이다. 근거 두 가지:
            //  (1) InteractionController에는 CraftStation 분기가 아예 없다(그 파일은 이 작업의 락 밖).
            //      그래서 어떤 순서를 잡아도 "화면과 E키가 갈라지는" 문제는 생기지 않지만, 반대로 이
            //      분기가 E 동사를 가진 분기보다 앞에 서면 실제로 E가 하는 일을 가릴 수 있다 - 뒤에 둔다.
            //  (2) 폴백보다는 앞이어야 한다. 뗏목 분기는 GetComponentInParent라, 갑판 위에 놓인 제작대를
            //      뒤에 두면 "제작대"가 아니라 "뗏목 제작"이 뜬다(살바지 지점을 여객기보다 앞에 둔 것과 같은 이유).
            var craftStation = target.GetComponent<CraftStation>();
            if (craftStation != null)
                return BuildCraftStationPrompt(craftStation, inventory, out main, out sub, out blocked);

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
        /// [전투 깊이 확장] 땅에 꽂힌 창 프롬프트. 예: "창 회수" / "가까이 가면 줍는다 · 남은 사용 12회".
        ///
        /// **키를 안내하지 않는다.** 회수는 E가 아니라 접근으로 일어나기 때문이다(클래스 규칙:
        /// 화면이 약속한 것과 실제 동작이 갈라지면 안 된다). 대신 어떻게 주우면 되는지를 문장으로 적는다.
        /// 판정은 전부 ThrownWeapon/PlayerInventory가 이미 공개한 값만 읽는다 - 여기서 회수 조건을
        /// 새로 구현하지 않는다(용량 판정은 실제 회수가 쓰는 CanAccept 그대로다).
        /// </summary>
        private bool BuildThrownWeaponPrompt(ThrownWeapon weapon, PlayerInventory inventory,
            out string main, out string sub, out bool blocked)
        {
            main = $"{weapon.DisplayName} 회수";
            sub = "";
            blocked = false;

            if (!weapon.IsStuck)
            {
                // 아직 날아가는 중이면 회수 안내가 뜻이 없다(콜라이더도 없어 사실상 도달하지 않는 경로).
                sub = "날아가는 중";
                blocked = true;
                return true;
            }

            if (!weapon.IsRecoverable || weapon.WeaponData == null)
            {
                blocked = true;
                sub = "부러져서 주울 수 없다";
                return true;
            }

            if (inventory == null || !inventory.CanAccept(weapon.WeaponData, 1))
            {
                blocked = true;
                sub = "가방이 가득 찼다 - Tab에서 정리하거나 저장궤에 넣어라";
                return true;
            }

            sub = weapon.RemainingUses >= 0
                ? $"가까이 가면 줍는다 · 남은 사용 {weapon.RemainingUses}회"
                : "가까이 가면 줍는다";
            return true;
        }

        /// <summary>
        /// [농사] 밭 프롬프트. 밭의 상태가 곧 E가 하는 일이라 세 갈래로 갈린다.
        ///  · 빈 밭  → "[E] 야자씨앗 심기"      / "야자 묘목 · 3일이면 다 자란다"
        ///  · 자라는 중 → "[E] 야자 묘목 물주기" / "성장 중 62% · 비가 오면 더 빨리 자란다"
        ///    (물을 줄 수 없으면 회색으로 "야자 묘목 성장 중 62%" + 사유)
        ///  · 다 자람 → "[E] 야자 묘목 수확"     / "코코넛 2~3개 · 씨앗을 일부 돌려받는다"
        ///
        /// **판정을 하나도 새로 만들지 않는다.** 심을 수 있는 씨앗은 FarmPlot.TryFindPlantableSeed,
        /// 물주기 가능 여부는 FarmPlot.CanWater, 수확 가능 여부는 FarmPlot.CanHarvest, 비 보정은
        /// FarmPlot.IsRainBoostActive — 전부 FarmPlot이 실제 동작에 쓰는 바로 그 메서드다
        /// (ResourceNode.GetHarvestFailure의 결론만 받아 옮기는 것과 같은 규칙).
        /// 넷 다 상태를 바꾸지 않아 매 갱신마다 호출해도 안전하다.
        /// </summary>
        private bool BuildFarmPlotPrompt(FarmPlot plot, PlayerInventory inventory, string key,
            out string main, out string sub, out bool blocked)
        {
            main = FarmPlot.DisplayName;
            sub = "";
            blocked = false;

            // ── 빈 밭: 심기 ──────────────────────────────────────────────────────
            if (!plot.HasCrop)
            {
                if (!FarmPlot.TryFindPlantableSeed(inventory, out ItemData seed, out FarmCropKind seedKind))
                {
                    blocked = true;
                    sub = "심을 씨앗이 없다 - 야자씨앗 / 해조류씨앗 / 약초씨앗을 만들어라";
                    return true;
                }

                main = $"{key} {seed.itemName} 심기";
                sub = $"{FarmPlot.GetCropDisplayName(seedKind)} · " +
                    $"{FormatGrowDays(FarmPlot.GetGrowDays(seedKind))}일이면 다 자란다";
                return true;
            }

            string cropName = plot.CropDisplayName;

            // ── 다 자람: 수확 ────────────────────────────────────────────────────
            if (plot.IsRipe)
            {
                string yieldText = plot.TryGetYieldRange(out int minYield, out int maxYield)
                    ? (minYield == maxYield ? $"{minYield}개" : $"{minYield}~{maxYield}개")
                    : "";
                ItemData harvest = plot.HarvestItem;
                string harvestName = harvest != null ? harvest.itemName : "수확물";

                if (!plot.CanHarvest(inventory))
                {
                    main = $"{cropName} 수확 가능";
                    blocked = true;
                    sub = harvest == null
                        ? $"{harvestName} 아이템이 없어 수확할 수 없다"
                        : "가방이 가득 찼다 - Tab에서 정리하거나 저장궤에 넣어라";
                    return true;
                }

                main = $"{key} {cropName} 수확";
                sub = $"{harvestName} {yieldText} · 씨앗을 일부 돌려받는다";
                return true;
            }

            // ── 자라는 중: 물주기 ────────────────────────────────────────────────
            // 진행도는 FarmPlot.Progress01(외형 단계가 보는 것과 같은 값)을 그대로 백분율로 옮긴다.
            int percent = Mathf.Clamp(Mathf.FloorToInt(plot.Progress01 * 100f), 0, 99);
            string growthText = $"성장 중 {percent}%";

            if (FarmPlot.IsRainBoostActive)
                growthText += " · 비가 와서 빨리 자란다";
            else if (plot.IsWatered)
                growthText += " · 물이 대어져 있다";

            if (!plot.CanWater(inventory))
            {
                main = $"{cropName} {growthText}";
                blocked = true;
                sub = plot.IsWatered
                    ? "물은 이미 충분하다"
                    : $"{FarmPlot.WaterItemName} 1개가 있으면 물을 줘서 더 빨리 키울 수 있다";
                return true;
            }

            main = $"{key} {cropName} 물주기";
            sub = $"{growthText} · {FarmPlot.WaterItemName} 1개를 쓴다";
            return true;
        }

        /// <summary>
        /// 성장 일수를 "3" / "1.5"처럼 짧게 적는다(정수면 소수점을 붙이지 않는다).
        /// </summary>
        private static string FormatGrowDays(float days)
        {
            return Mathf.Approximately(days, Mathf.Round(days))
                ? Mathf.RoundToInt(days).ToString()
                : days.ToString("0.#");
        }

        /// <summary>
        /// [제작대 3종] 제작 시설 프롬프트. 예: "제작대" / "여기서 [V] 제작 창을 열면 전용 제작법이 열린다".
        ///
        /// **상호작용 키(E)를 안내하지 않는다** - ThrownWeapon 프롬프트와 같은 이유다. 이 시설의 사용법은
        /// "조준 + E"가 아니라 "반경 안에 서서 제작 창 열기"이고(CraftStation 클래스 주석),
        /// InteractionController에는 대응 분기가 아예 없다. E를 약속하면 눌러도 아무 일이 없다.
        ///
        /// 이름은 CraftStation.GetDisplayName, 반경 판정은 CraftStation.IsNear만 쓴다 - 둘 다 제작 시스템이
        /// 실제로 쓰는 바로 그 소스이며(CraftingSystem.HasRequiredStation이 같은 IsNear를 본다), 여기서
        /// 거리 계산을 새로 만들면 "화면은 쓸 수 있다는데 제작 창은 아니라고 한다"가 된다.
        /// 제작 키도 문자열로 박지 않고 씬의 CraftingUI.toggleKey에서 읽는다(SettingsMenuController와 같은 규약).
        /// </summary>
        private bool BuildCraftStationPrompt(CraftStation station, PlayerInventory inventory,
            out string main, out string sub, out bool blocked)
        {
            main = CraftStation.GetDisplayName(station.kind);
            blocked = false;

            // 제작 시스템이 반경을 재는 기준점과 같은 위치를 쓴다(CraftingSystem.CraftPosition:
            // 인벤토리를 들고 있는 오브젝트 = 플레이어. 없으면 조준을 맡은 컨트롤러 자신).
            Vector3 playerPosition = inventory != null
                ? inventory.transform.position
                : (interaction != null ? interaction.transform.position : transform.position);

            if (!CraftStation.IsNear(playerPosition, station.kind))
            {
                blocked = true;
                sub = "조금 더 가까이 가야 쓸 수 있다";
                return true;
            }

            sub = $"여기서 [{GetCraftToggleKey()}] 제작 창을 열면 전용 제작법이 열린다";
            return true;
        }

        /// <summary>
        /// 제작 창을 여는 키를 씬의 CraftingUI에서 읽어 온다(찾지 못하면 그 컴포넌트의 코드 기본값 V).
        /// 한 번 찾으면 캐시한다 - 프롬프트는 0.1초마다 갱신되므로 매번 씬을 훑게 두면 안 되고, 반대로
        /// Start에서 한 번만 찾으면 이 UI가 씬 로드 콜백으로 먼저 생길 때 놓칠 수 있다(그때는 다음
        /// 갱신에서 다시 찾는다). 캐시는 인스턴스 필드라 R1 정적 상태 누수와 무관하다.
        /// </summary>
        private KeyCode GetCraftToggleKey()
        {
            if (craftingUI == null)
                craftingUI = FindAnyObjectByType<CraftingUI>();

            return craftingUI != null ? craftingUI.toggleKey : KeyCode.V;
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
            // [낚시] 갑판 위에서는 어디를 봐도 뗏목이 조준되므로 "대상 없음" 분기에 닿지 못한다.
            // 물 위에서 낚시를 발견할 수 있는 유일한 자리라 꼬리말만 덧붙인다(조건이 안 맞으면 빈 문자열).
            sub = raft.DescribeState() + BuildFishHint();
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
        /// [식량 루프] 훈연기 프롬프트. 훈연기는 모닥불(Campfire)을 부품으로 얹고 있어 E 동작(점화 /
        /// 장작 추가)이 모닥불과 완전히 같으므로, 여기서는 **같은 행동을 훈연기의 말로** 안내하고
        /// 훈연기에만 있는 정보(R 투입 · 훈연대 현황)를 한 줄에 덧붙인다.
        ///
        /// 판정은 전부 기존 소스를 읽기만 한다 - 점화 조건은 Campfire(fireStarterItem / fuelItem /
        /// isLit), R이 집는 재료는 InteractionController.FindFirstRawFoodInInventory와 **같은 규칙**
        /// (첫 isRawFood), 훈연 가능 여부는 Smoker.TryGetSmokedResult다. UI가 규칙을 새로 만들지 않는다.
        ///
        /// campfire가 null인 경우(설치 직후 아직 Campfire가 붙기 전)에도 안전하다 - 그때는 "불을
        /// 지펴야 한다"만 알려주고 어떤 필드도 건드리지 않는다.
        /// </summary>
        private bool BuildSmokerPrompt(Smoker smoker, Campfire campfire, PlayerInventory inventory, string key,
            out string main, out string sub, out bool blocked)
        {
            main = "";
            sub = "";
            blocked = false;

            string fuelName = campfire != null && campfire.fuelItem != null
                ? campfire.fuelItem.itemName
                : Smoker.FuelItemName;

            // R을 누르면 실제로 집히는 재료(= 컨트롤러와 같은 "첫 생음식")와, 그것이 훈연되는지 여부.
            ItemData rawFood = FindFirstRawFood(inventory);
            bool canSmoke = rawFood != null && Smoker.TryGetSmokedResult(rawFood, out ItemData _);
            string insertHint = canSmoke
                ? $" · [{interaction.cookKey}] {rawFood.itemName} 투입"
                : "";

            // 불이 꺼져 있으면 E가 하는 일은 점화다(Campfire.TryLight). 훈연은 불이 붙어야만 진행된다.
            if (campfire == null || !campfire.isLit)
            {
                main = $"{key} {Smoker.DisplayName} 불 지피기";

                if (campfire == null)
                {
                    sub = "불을 지펴야 훈연이 시작된다";
                    return true;
                }

                bool needsStarter = campfire.fireStarterItem != null || campfire.alternateFireStarterItem != null;
                bool hasStarter = !needsStarter || HasAnyOf(inventory, campfire.fireStarterItem, campfire.alternateFireStarterItem);
                bool hasFuelToLight = campfire.fuelItem == null
                    || (inventory != null && inventory.GetItemCount(campfire.fuelItem) > 0);
                string starterName = campfire.fireStarterItem != null
                    ? campfire.fireStarterItem.itemName
                    : (campfire.alternateFireStarterItem != null ? campfire.alternateFireStarterItem.itemName : "발화 도구");

                if (!hasStarter)
                {
                    blocked = true;
                    sub = $"{starterName} 필요";
                }
                else if (!hasFuelToLight)
                {
                    blocked = true;
                    sub = $"{fuelName} 필요";
                }
                else
                {
                    sub = $"{fuelName} 1개 소모 · 불이 붙어야 훈연이 진행된다{insertHint}";
                }
                return true;
            }

            // 불이 붙어 있을 때. 훈연대 현황은 Smoker가 이미 읽기 전용으로 공개한 값만 쓴다.
            int pending = smoker.PendingRaw != null ? smoker.PendingRaw.Count : 0;
            int ready = smoker.ReadyOutput != null ? smoker.ReadyOutput.Count : 0;
            string status = $"훈연 중 {pending}개 · 완성 {ready}개 (칸 {pending + ready}/{smoker.capacity})";

            bool hasFuel = campfire.fuelItem == null
                || (inventory != null && inventory.GetItemCount(campfire.fuelItem) > 0);

            if (hasFuel)
            {
                main = $"{key} {Smoker.DisplayName}에 {fuelName} 넣기";
                sub = $"연료 {campfire.remainingFuelSeconds:F0}초 남음 · {status}{insertHint}";
                return true;
            }

            // 연료가 떨어졌어도 넣을 재료가 있으면 R 안내가 주 행동이다("아무 반응 없음" 방지).
            if (canSmoke)
            {
                main = $"[{interaction.cookKey}] {rawFood.itemName} 훈연";
                sub = $"{fuelName}이(가) 없어 장작은 넣을 수 없다 · {status}";
                return true;
            }

            main = $"{key} {Smoker.DisplayName}에 {fuelName} 넣기";
            blocked = true;
            sub = $"{fuelName} 필요 · {status}";
            return true;
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

            string hazardName = GetHazardDisplayName(hazard);

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

            sub = $"{weapon.data.itemName} 사용 · 남은 체력 {hazard.currentHealth:F0}/{hazard.maxHealth:F0}"
                + BuildThrowHint(inventory);
            return true;
        }

        /// <summary>
        /// [낚시] 낚싯대를 들고 물을 겨누고 있을 때 뜨는 프롬프트("[Z] 낚시").
        ///
        /// **판정을 여기서 다시 만들지 않는다** - 낚싯대 보유·조준 방향·그 자리에 물이 있는지·성공률은
        /// 전부 FishingSystem.TryDescribeCastPrompt가 실제 캐스팅과 **같은 코드**로 판정해 문장까지
        /// 만들어 준다(이 파일의 대원칙: 대상 컴포넌트가 공개한 결론만 옮긴다).
        /// 줄이 이미 나가 있는 동안에는 그쪽이 false를 돌려주므로 여기서도 아무것도 뜨지 않는다 -
        /// 진행 상태는 FishingSystem 자신의 화면 중앙 위 패널이 이미 보여 준다(같은 안내의 중복 방지).
        /// 키 표기는 문자열로 박지 않고 FishingSystem.FishingKey(= PlayerController.fishingKey)에서 읽는다.
        /// </summary>
        private bool TryBuildFishingPrompt(out string main, out string sub, out bool blocked)
        {
            main = "";
            sub = "";
            blocked = false;

            FishingSystem fishing = FishingSystem.Active;
            if (fishing == null)
                return false;

            return fishing.TryDescribeCastPrompt($"[{fishing.FishingKey}]", out main, out sub, out blocked);
        }

        /// <summary>
        /// [낚시] 낚시가 지금 가능할 때만 붙는 " · [Z] 낚시" 꼬리말.
        ///
        /// 뗏목 갑판처럼 **어디를 봐도 무언가가 조준되는 자리**에서는 위의 "대상 없음" 분기에 절대
        /// 도달하지 못해 낚시를 발견할 방법이 없다. 그 구멍만 메운다(창 투척의 BuildThrowHint와 같은 방식).
        /// 조건이 안 맞으면 빈 문자열이라 기존 문장이 한 글자도 달라지지 않는다.
        /// </summary>
        private static string BuildFishHint()
        {
            FishingSystem fishing = FishingSystem.Active;
            if (fishing == null)
                return "";

            return fishing.GetHintSuffix($"[{fishing.FishingKey}]");
        }

        /// <summary>
        /// [전투 깊이 확장] 창을 들고 있을 때만 붙는 "우클릭 투척" 안내 꼬리말.
        ///
        /// **이 게임에서 투척의 유일한 발견 경로다.** 새 입력은 어디에도 표시되지 않으면 없는 기능과
        /// 같고(쉼터 취침 프롬프트가 같은 이유로 자세히 쓰여 있다), 조작 목록을 보여 주는
        /// SettingsMenuController는 이 작업의 락 밖이라 손댈 수 없다(보고서의 [막힘] 항목).
        ///
        /// 던질 것이 없으면 빈 문자열이라 기존 문장이 한 글자도 달라지지 않는다.
        /// 피해량은 CombatSystem이 계산한 값을 그대로 읽는다 - 여기서 다시 계산하지 않는다.
        /// </summary>
        private static string BuildThrowHint(PlayerInventory inventory)
        {
            InventoryItem throwable = CombatSystem.FindThrowable(inventory);
            if (throwable == null || throwable.data == null)
                return "";

            return $" · 우클릭 투척 {CombatSystem.GetThrowDamage(throwable.data):F0}";
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
        /// 조준한 위험 요소의 표시 이름. **보스면 보스 이름을 쓴다.**
        ///
        /// 보스 3종(거대 상어 / 대왕 곰치 / 심해 괴수)은 전부 HazardSource를 얹고 hazardType을
        /// Shark로 **고정**한다(수중 피해 + 출혈 + SharkAttack 사인이 필요해서다 - BossCreature 클래스
        /// 주석 참고). 그 대가로 hazardType만 보는 아래 표에서는 세 보스가 모두 "상어"로 나왔다.
        /// 여기서 BossCreature가 붙어 있는지만 한 번 더 보고 이름을 바로잡는다 - 전투 규칙은 한 줄도
        /// 건드리지 않고 표시 문자열만 고르는 것이라 UI에 두는 것이 맞다.
        /// </summary>
        private static string GetHazardDisplayName(HazardSource hazard)
        {
            var boss = hazard.GetComponent<BossCreature>();
            if (boss != null)
            {
                string bossName = GetBossDisplayName(boss);
                if (!string.IsNullOrEmpty(bossName))
                    return bossName;
            }

            return GetHazardDisplayName(hazard.hazardType);
        }

        /// <summary>
        /// 보스 개체의 표시 이름을 BossCreature에게 물어본다. 못 알아내면 빈 문자열(호출부가 기존
        /// hazardType 표로 폴백한다 - 화면에서 이름이 사라지는 경우는 만들지 않는다).
        ///
        /// [왜 오브젝트 이름으로 종류를 되찾는가] BossCreature의 종류(kind)는 private 필드이고
        /// **공개 인스턴스 접근자가 없다**(공개된 인스턴스 멤버는 Home 하나뿐이다). 그 파일은 이 작업의
        /// 락 밖이라 접근자를 추가할 수도 없다. 대신 BossCreature.Spawn이 오브젝트 이름을
        /// `"Boss_" + (BossKind)kind`로 **결정적으로** 짓고, 이름 → 이름 변환에 필요한 나머지
        /// (BossKind · KindCount · GetDisplayName)는 전부 공개 API다. 그래서 그 규칙을 그대로 되짚는다.
        /// 이름 규칙이 깨지면 조용히 폴백할 뿐 오작동하지 않는다.
        /// [요청] systems-engineer: BossCreature에 `public int Kind => kind;` 한 줄이 생기면 이
        /// 이름 되짚기는 그 자리에서 지울 수 있다.
        /// </summary>
        private static string GetBossDisplayName(BossCreature boss)
        {
            string[] names = BossObjectNames;
            string objectName = boss.gameObject.name;

            for (int i = 0; i < names.Length; i++)
            {
                if (string.Equals(objectName, names[i], System.StringComparison.Ordinal))
                    return BossCreature.GetDisplayName(i);
            }

            return "";
        }

        /// <summary>
        /// 보스 종류별 오브젝트 이름 캐시("Boss_GiantShark" 등). 프롬프트는 매 프레임 갱신되므로
        /// 여기서 문자열을 조립하면 그대로 프레임당 할당이 된다 - 최초 1회만 만들어 두고 재사용한다.
        /// </summary>
        private static string[] bossObjectNames;

        private static string[] BossObjectNames
        {
            get
            {
                if (bossObjectNames == null)
                {
                    bossObjectNames = new string[BossCreature.KindCount];
                    for (int i = 0; i < BossCreature.KindCount; i++)
                        bossObjectNames[i] = "Boss_" + (BossKind)i;
                }
                return bossObjectNames;
            }
        }

        /// <summary>
        /// 인벤토리의 첫 생음식. **InteractionController.FindFirstRawFoodInInventory와 같은 규칙**
        /// (isRawFood 하나만 본다 - cookedResult는 보지 않는다)이라, R을 눌렀을 때 실제로 집히는
        /// 재료와 화면에 적히는 재료가 어긋나지 않는다. 훈연기 프롬프트 전용이다.
        /// </summary>
        private static ItemData FindFirstRawFood(PlayerInventory inventory)
        {
            if (inventory == null)
                return null;

            foreach (var item in inventory.items)
            {
                if (item.data != null && item.data.isRawFood)
                    return item.data;
            }
            return null;
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
                // [전투 깊이 확장] 대왕 크랩(HazardType 9)이 빠져 있어 조준하면 "위험 요소"로만 떴다.
                // 순수 표시 문자열이라 게임 규칙에는 영향이 없다.
                case HazardType.GiantCrab: return "대왕 크랩";
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
