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
    /// 판정 로직은 이 UI가 새로 만들지 않는다. 대상 컴포넌트가 이미 public으로 노출한 값
    /// (ResourceNode.CanHarvest/requiresTool/requiredTool, Campfire.isLit/fuelItem,
    /// BoatConstructionSystem.CanFindBlueprintOnIsland/GetCurrentStageRequirements 등)과
    /// PlayerInventory의 보유 수량만 읽어 문장으로 옮긴다.
    ///
    /// 주의(레이캐스트 중복): 지금 InteractionController는 조준 대상을 외부에 노출하는 public API가 없어서
    /// (TryGetLookTarget이 private), 이 UI가 InteractionController.interactionCamera/interactionDistance
    /// 라는 public 필드를 그대로 읽어 동일한 조건의 레이를 한 번 더 쏜다. 판정 파라미터는 전부 컨트롤러
    /// 쪽 값을 참조하므로 거리/카메라가 바뀌어도 자동으로 따라가지만, 레이캐스트 자체가 두 벌인 것은
    /// 여전히 어긋날 여지가 있다. systems-engineer에게 public 접근자(예:
    /// `public bool TryGetLookTarget(out GameObject target)`) 공개를 요청해 둔 상태이며, 공개되는 즉시
    /// 아래 TryGetLookTarget()을 그 호출로 교체하면 된다(이 파일 외 수정 불필요).
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

            if (!TryGetLookTarget(out GameObject target))
            {
                SetOpen(false);
                return;
            }

            if (!TryBuildPrompt(target, out string main, out string sub, out bool blocked))
            {
                SetOpen(false);
                return;
            }

            SetOpen(true);
            // 불가 상태는 문구 전체를 회색으로 낮춘다 - "지금은 못 하지만 조건만 갖추면 된다"는 신호이지
            // 위험 경고(Danger Red)가 아니기 때문이다. 색은 팔레트 밖으로 나가지 않는 무채색만 쓴다.
            mainLabel.color = blocked ? BlockedColor : AvailableColor;
            subLabel.color = blocked ? BlockedColor : SubInfoColor;

            if (mainLabel.text != main)
                mainLabel.text = main;
            if (subLabel.text != sub)
                subLabel.text = sub;
        }

        /// <summary>
        /// InteractionController가 쓰는 것과 동일한 카메라/거리로 정면 레이를 쏴 대상 오브젝트를 얻는다.
        /// (클래스 주석 참고 - 컨트롤러가 public 접근자를 공개하면 이 메서드는 그 호출로 교체한다.)
        /// </summary>
        private bool TryGetLookTarget(out GameObject target)
        {
            target = null;
            Camera cam = interaction.interactionCamera;
            if (cam == null)
                return false;

            Ray ray = new Ray(cam.transform.position, cam.transform.forward);
            if (!Physics.Raycast(ray, out RaycastHit hit, interaction.interactionDistance))
                return false;

            target = hit.collider.gameObject;
            return true;
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
            {
                string yieldName = resourceNode.yieldItem != null ? resourceNode.yieldItem.itemName : "자원";
                main = $"{key} {yieldName} 채집";

                if (!resourceNode.CanHarvest)
                {
                    blocked = true;
                    sub = "지금은 고갈됨 - 잠시 뒤 다시 자란다";
                }
                else if (resourceNode.requiresTool && resourceNode.requiredTool != null
                         && (inventory == null || inventory.FindItem(resourceNode.requiredTool) == null))
                {
                    // 실측(Balance_SceneSnapshot.md): 금속조각은 손도끼가 있어야 채집된다. 손도끼가 없으면
                    // Harvest()가 조용히 실패할 뿐이라, 여기서 사유를 반드시 보여줘야 한다.
                    blocked = true;
                    sub = $"{resourceNode.requiredTool.itemName} 필요";
                }
                else
                {
                    sub = $"남은 채집 {resourceNode.remainingHarvestCount}회 · 1회당 {resourceNode.yieldPerHarvest}개";
                }
                return true;
            }

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

            var blueprint = target.GetComponent<BoatBlueprintPickup>();
            if (blueprint != null)
            {
                var boat = blueprint.boatConstruction;
                main = $"{key} 배 도면 습득";
                if (boat == null)
                {
                    blocked = true;
                    sub = "지금은 습득할 수 없다";
                }
                else if (!boat.CanFindBlueprintOnIsland(blueprint.islandSize))
                {
                    blocked = true;
                    // 규칙은 BoatConstructionSystem이 정한다(1~2단계=대형 섬, 3단계=특대 섬). 여기서는 문장만 만든다.
                    sub = boat.currentStage <= 2
                        ? $"{boat.currentStage}단계 도면은 대형 섬에서만 습득 가능"
                        : "최종 단계 도면은 특대 섬에서만 습득 가능";
                }
                else
                {
                    main = $"{key} 배 {boat.currentStage}단계 도면 습득";
                    sub = "도면이 있어야 그 단계를 조립할 수 있다";
                }
                return true;
            }

            var workbench = target.GetComponent<BoatWorkbench>();
            if (workbench != null)
                return BuildBoatWorkbenchPrompt(workbench, inventory, key, out main, out sub, out blocked);

            var wreck = target.GetComponent<AircraftWreck>();
            if (wreck != null)
                return BuildAircraftWreckPrompt(wreck, inventory, key, out main, out sub, out blocked);

            var hazard = target.GetComponent<HazardSource>();
            if (hazard != null)
                return BuildHazardPrompt(hazard, inventory, key, out main, out sub, out blocked);

            var shelter = target.GetComponent<Shelter>();
            if (shelter != null)
            {
                main = $"{key} 쉼터에서 취침";
                var clock = interaction.survivalClock;
                if (clock == null)
                {
                    blocked = true;
                    sub = "지금은 잠들 수 없다";
                }
                else if (clock.IsDaytime)
                {
                    blocked = true;
                    sub = "밤에만 취침할 수 있다";
                }
                else
                {
                    sub = $"아침까지 건너뛰고 체력 {shelter.sleepHealAmount:F0} 회복";
                }
                return true;
            }

            return false;
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
        /// 배 작업대 프롬프트. 현재 단계 필요 재료는 BoatConstructionSystem에서 읽고(하드코딩하지 않는다),
        /// 아직 부족한 재료를 이름과 개수까지 보여준다.
        /// </summary>
        private bool BuildBoatWorkbenchPrompt(BoatWorkbench workbench, PlayerInventory inventory, string key,
            out string main, out string sub, out bool blocked)
        {
            main = "";
            sub = "";
            blocked = false;

            var boat = workbench.boatConstruction;
            if (boat == null)
            {
                main = $"{key} 배 조립";
                blocked = true;
                sub = "제작 진행 정보를 찾을 수 없다";
                return true;
            }

            main = $"{key} 배 조립 ({boat.currentStage}/{BoatConstructionSystem.TotalStages}단계)";

            if (!boat.hasCurrentStageBlueprint)
            {
                blocked = true;
                sub = $"{boat.currentStage}단계 도면 필요";
                return true;
            }

            string missing = BuildMissingMaterialsText(inventory, boat.GetCurrentStageRequirements(), boat);
            sub = string.IsNullOrEmpty(missing)
                ? "재료 충족 - 지금 이 단계를 완성할 수 있다"
                : $"부족: {missing}";
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
        /// 배 제작(BoatConstructionSystem) 요구 재료 목록에서 아직 부족한 항목을 "이름 n개" 형태로 잇는다.
        /// 작업대는 인벤토리 재료를 자동 투입하므로(BoatWorkbench.ContributeAvailableMaterials),
        /// "이미 투입한 양 + 지금 들고 있는 양"을 합쳐 남은 부족분을 계산한다.
        /// </summary>
        private string BuildMissingMaterialsText(PlayerInventory inventory,
            System.Collections.Generic.List<BoatConstructionSystem.MaterialRequirement> requirements,
            BoatConstructionSystem boat)
        {
            if (requirements == null)
                return "";

            var parts = new System.Collections.Generic.List<string>();
            foreach (var req in requirements)
            {
                if (req == null || req.item == null)
                    continue;

                int owned = boat.GetCollectedQuantity(req.item) + (inventory != null ? inventory.GetItemCount(req.item) : 0);
                int shortage = req.quantity - owned;
                if (shortage > 0)
                    parts.Add($"{req.item.itemName} {shortage}개");
            }

            return string.Join(", ", parts);
        }

        /// <summary>
        /// 경비행기 수리(AircraftRepairSystem) 요구 재료 목록에서 아직 부족한 항목을 "이름 n개" 형태로 잇는다.
        /// (두 시스템의 MaterialRequirement는 이름만 같고 서로 다른 타입이라 오버로드로 나눠 둔다.)
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
