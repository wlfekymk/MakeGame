using UnityEngine;
using MakeGame.Data;
using MakeGame.Player;

namespace MakeGame.Systems
{
    /// <summary>
    /// 플레이어 카메라 정면으로 레이를 쏴서 상호작용 가능한 대상(채집 노드, 사냥감, 모닥불, 물 증류기,
    /// 배 도면, 배 작업대 등)을 찾아 입력에 맞는 행동을 실행한다.
    /// 인벤토리 안의 아이템 섭취/조리처럼 월드 오브젝트가 필요 없는 행동도 함께 처리한다.
    /// </summary>
    public class InteractionController : MonoBehaviour
    {
        [Header("레이캐스트 설정")]
        [Tooltip("레이캐스트를 쏠 기준 카메라")]
        public Camera interactionCamera;

        [Tooltip("상호작용 가능 거리")]
        public float interactionDistance = 4f;

        [Header("키 설정")]
        [Tooltip("정면 대상과 상호작용하는 키 (채집/사냥/불 붙이기/물 수확/도면 습득/배 조립)")]
        public KeyCode interactKey = KeyCode.E;

        [Tooltip("정면의 켜진 모닥불에 인벤토리 첫 생음식을 조리하는 키")]
        public KeyCode cookKey = KeyCode.R;

        [Tooltip("인벤토리의 첫 음식/음료 아이템을 섭취하는 키")]
        public KeyCode consumeKey = KeyCode.C;

        [Tooltip("인벤토리의 첫 설치형(빌드) 아이템을 정면 바닥에 설치하는 키 (예: 물 증류기 키트, 쉼터 키트)")]
        public KeyCode placeKey = KeyCode.G;

        [Tooltip("설치형 아이템을 플레이어 앞 얼마나 떨어진 위치에 놓을지 (미터)")]
        public float placementDistance = 3f;

        [Header("연결할 플레이어 컴포넌트")]
        public PlayerInventory inventory;
        public PlayerSkills skills;
        public SurvivalStats survivalStats;
        public ConsumptionSystem consumptionSystem;

        [Tooltip("쉼터 취침(Shelter.TrySleep) 판정에 필요한 게임 내 시계. 비워두면 쉼터에서 취침할 수 없다.")]
        public SurvivalClock survivalClock;

        /// <summary>
        /// 매 프레임 입력을 감지해 정면 상호작용 또는 인벤토리 행동(조리/섭취)을 실행한다.
        /// </summary>
        private void Update()
        {
            if (Input.GetKeyDown(interactKey))
            {
                // [보관 상자] 창이 열려 있는 동안에는 이 키가 곧 '닫기'이고, 그 프레임의 월드 상호작용은
                // 일어나지 않는다. 상자 창은 조준했을 때만 열리는 창이라 전용 토글 키를 따로 만들지
                // 않았다(Tab/V/J/M/B/Esc가 이미 차 있다). CloseIfOpen은 **실제로 닫았을 때만** true를
                // 돌려주므로 상자와 무관한 상황의 E를 삼키지 않는다.
                if (!MakeGame.UI.ChestUI.CloseIfOpen())
                    InteractWithTarget();
            }

            if (Input.GetKeyDown(cookKey))
                CookFirstRawFoodAtTarget();

            if (Input.GetKeyDown(consumeKey))
                ConsumeFirstFoodOrDrink();

            if (Input.GetKeyDown(placeKey))
                PlaceFirstPlaceableItem();
        }

        /// <summary>
        /// 카메라 정면으로 레이를 쏴서 맞은 대상의 컴포넌트 종류를 확인해 알맞은 상호작용을 실행한다.
        /// </summary>
        private void InteractWithTarget()
        {
            if (!TryGetLookTarget(out GameObject target))
                return;

            // 보관 상자: 조준하고 누르면 보관 창(ChestUI)이 열린다. 자식 파츠(뚜껑·손잡이 등)에 레이가
            // 맞아도 같은 상자로 이어지도록 GetComponentInParent를 쓴다(BoatWorkbench와 같은 이유).
            var storageChest = target.GetComponentInParent<StorageChest>();
            if (storageChest != null)
            {
                MakeGame.UI.ChestUI.OpenFor(storageChest);
                return;
            }

            var resourceNode = target.GetComponent<ResourceNode>();
            if (resourceNode != null)
            {
                resourceNode.Harvest(inventory, skills);
                return;
            }

            var creature = target.GetComponent<HuntableCreature>();
            if (creature != null)
            {
                creature.TryHunt(inventory, skills);
                return;
            }

            var waterStill = target.GetComponent<WaterStill>();
            if (waterStill != null)
            {
                // 인벤토리를 명시적으로 넘기는 오버로드를 쓴다. 인자 1개짜리를 부르면 WaterStill이
                // 상호작용할 때마다 FindAnyObjectByType<PlayerInventory>()로 씬 전체를 훑는데(첫 1회
                // 이후에는 캐시되지만, 그 1회가 굳이 필요 없다), 여기 inventory는 이미 인스펙터에서
                // 배선돼 있어 정답을 손에 들고도 다시 찾는 셈이었다. WaterStill 쪽 폴백은 프리팹을
                // 단독으로 쓰는 경우를 위해 그대로 남겨 둔다 - 정상 경로에서만 타지 않게 하는 것이 목적.
                waterStill.CollectInto(survivalStats, inventory);
                return;
            }

            var campfire = target.GetComponent<Campfire>();
            if (campfire != null)
            {
                campfire.TryLight(inventory);
                return;
            }

            var blueprint = target.GetComponent<BoatBlueprintPickup>();
            if (blueprint != null)
            {
                blueprint.TryObtain();
                return;
            }

            // GetComponentInParent를 쓴다(자기 자신도 포함하므로 기존 작업대는 동작이 100% 동일하다).
            // 실체 뗏목(RaftStructure)은 본체에 BoatWorkbench를 달고 있지만, 승선 발판처럼 콜라이더를
            // 가진 자식 파츠도 있어서 레이가 자식에 맞을 수 있다. 그때도 같은 작업대로 이어져야 한다.
            var workbench = target.GetComponentInParent<BoatWorkbench>();
            if (workbench != null)
            {
                workbench.TryBuild(inventory);
                return;
            }

            var aircraftWreck = target.GetComponent<AircraftWreck>();
            if (aircraftWreck != null)
            {
                aircraftWreck.TryRepair(inventory);
                return;
            }

            // 여객기 내부 부품 수거 지점: 1회 한정 부품 수거. **반드시 AirlinerWreck 분기보다 먼저**
            // 검사해야 한다 - 수거 지점은 잔해 시각 루트의 자식이라, 아래 AirlinerWreck의
            // GetComponentInParent가 먼저 돌면 지점 콜라이더를 조준해도 부모 잔해 수색으로 흡수돼
            // 지점이 영영 안 잡힌다. (지점 자체도 자식 시각 파츠에 레이가 맞을 수 있어 InParent.)
            var salvagePoint = target.GetComponentInParent<AirlinerSalvagePoint>();
            if (salvagePoint != null)
            {
                salvagePoint.TryCollect(inventory);
                return;
            }

            // 여객기 잔해(시작 섬 해안): 1회 한정 물자 수색. 콜라이더가 동체/날개 등 자식 파츠에
            // 붙어 있으므로 GetComponentInParent를 쓴다(BoatWorkbench와 같은 이유).
            var airliner = target.GetComponentInParent<AirlinerWreck>();
            if (airliner != null)
            {
                airliner.TrySearch(inventory);
                return;
            }

            var hazard = target.GetComponent<HazardSource>();
            if (hazard != null)
            {
                hazard.TryAttack(inventory, skills);
                return;
            }

            // 쉼터: 밤에 상호작용하면 취침해 아침까지 시간을 건너뛰고 소량 회복한다 (Shelter.TrySleep 참고).
            // [정착 배치 1] 취침이 불가능한 시간대(= 낮)에는 같은 키가 **건축**이 된다: 빈 슬롯을 채우거나
            // 다음 단계로 승급한다(Design_Settlement 2-2 "새 키 없음, 새 설치 절차 없음").
            // 순서가 중요하다 - TrySleep을 먼저 시도해야 밤의 기존 동작이 100% 그대로 유지된다.
            var shelter = target.GetComponent<Shelter>();
            if (shelter != null)
            {
                if (!shelter.TrySleep(survivalClock, survivalStats))
                    shelter.TryBuildNext(inventory);
                return;
            }

            // [건축 4티어] 건축 부품(바닥/벽/문/창/계단/지붕)을 조준한 E는 **제자리 티어 승급**이다.
            // 부품 식별은 BuildingSystem의 격자 역조회(TryGetPieceTier → pieceByRoot)를 그대로 쓴다 -
            // 여기서 판정을 새로 만들면 프롬프트가 보여주는 대상과 E가 잡는 대상이 갈라진다.
            // 반드시 위의 모든 분기(수거 지점/여객기/상자/쉼터 등)보다 **뒤**에 둔다: 상자는 건축 부품
            // 실물이지만 자체 등급 승급(ChestUI)이 있어 맨 위 StorageChest 분기가 먼저 가져가고,
            // TryGetPieceTier도 상자는 대상에서 제외한다. 성공/실패(재료 부족·최고 티어)의 효과음은
            // TryUpgradePiece가 내부에서 처리하므로 여기서는 분기만 소비한다.
            var building = BuildingSystem.Instance;
            if (building != null && building.TryGetPieceTier(target.transform, out _, out _))
            {
                building.TryUpgradePiece(target.transform, inventory);
                return;
            }

            // [보관 상자 폴백] 상자 본체에 콜라이더가 없고 별도의 조준 판정(StorageChest.Focused)으로만
            // 자기를 알리는 구성일 수 있다. **다른 상호작용이 전부 없었을 때만** 이 값을 본다 - 위쪽에
            // 두면 조준 판정이 근접 판정으로 바뀌는 순간 채집/사냥이 조용히 상자 열기에 먹힌다.
            if (StorageChest.Focused != null)
                MakeGame.UI.ChestUI.OpenFor(StorageChest.Focused);
        }

        /// <summary>
        /// 정면에 켜진 모닥불이 있으면, 인벤토리에서 가장 먼저 발견되는 생음식을 조리한다.
        /// </summary>
        private void CookFirstRawFoodAtTarget()
        {
            if (!TryGetLookTarget(out GameObject target))
                return;

            var campfire = target.GetComponent<Campfire>();
            if (campfire == null || inventory == null)
                return;

            ItemData firstRawFood = FindFirstRawFoodInInventory();
            if (firstRawFood != null)
                campfire.CookItem(inventory, skills, firstRawFood);
        }

        /// <summary>
        /// 인벤토리에서 섭취 가능한(음식/음료/치료) 아이템을 찾아 섭취한다.
        /// 현재 활성화된 상태 이상(출혈/중독/골절)을 치료할 수 있는 아이템이 있으면 그것을 최우선으로 사용한다.
        /// 그렇지 않으면 인벤토리에서 가장 먼저 발견되는 섭취 가능 아이템을 사용한다.
        /// (예전에는 순서상 앞에 있는 생수/음식이 먼저 소모되어, 출혈 중에 붕대를 들고도 C 키로
        /// 치료를 못 하고 엉뚱한 음식만 먹게 되는 버그가 있었다.)
        /// </summary>
        private void ConsumeFirstFoodOrDrink()
        {
            if (inventory == null || consumptionSystem == null)
                return;

            if (survivalStats != null)
            {
                foreach (var item in inventory.items)
                {
                    if (item.data == null)
                        continue;

                    bool curesActiveEffect =
                        (item.data.curesBleeding && survivalStats.isBleeding) ||
                        (item.data.curesPoison && survivalStats.isPoisoned) ||
                        (item.data.curesBrokenBone && survivalStats.hasBrokenBone);

                    if (curesActiveEffect)
                    {
                        consumptionSystem.Consume(item);
                        return;
                    }
                }
            }

            foreach (var item in inventory.items)
            {
                if (item.data != null && item.data.IsConsumable)
                {
                    consumptionSystem.Consume(item);
                    return;
                }
            }
        }

        /// <summary>
        /// 인벤토리에서 가장 먼저 발견되는 설치형(빌드) 아이템을 찾아 플레이어 정면 바닥에 설치한다.
        /// 설치에 성공하면 아이템을 인벤토리에서 소모한다(사용 횟수 1 차감, 대부분 그대로 소진됨).
        /// </summary>
        private void PlaceFirstPlaceableItem()
        {
            if (inventory == null)
                return;

            InventoryItem placeableItem = null;
            foreach (var item in inventory.items)
            {
                if (item.data != null && item.data.isPlaceable && item.data.placementPrefab != null)
                {
                    placeableItem = item;
                    break;
                }
            }

            if (placeableItem == null)
                return;

            Vector3 spawnPosition = transform.position + transform.forward * placementDistance;
            Instantiate(placeableItem.data.placementPrefab, spawnPosition, Quaternion.identity);

            inventory.UseItem(placeableItem);

            // 설치 완료 피드백음. 전용 효과음이 없어 제작 성공음을 재사용한다 (기존에는 설치 시 무음이었음).
            AudioManager.Instance?.PlayCraftSuccess();
        }

        /// <summary>
        /// 인벤토리에서 가장 먼저 발견되는 생음식(isRawFood) 아이템 데이터를 찾는다.
        /// </summary>
        private ItemData FindFirstRawFoodInInventory()
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
        /// 카메라 정면 레이캐스트로 맞은 오브젝트를 반환한다. 아무것도 맞지 않으면 false를 반환한다.
        ///
        /// [ui-engineer 요청] public으로 공개한다. InteractionPromptUI가 조준 대상을 알아내려고
        /// interactionCamera/interactionDistance를 직접 읽어 같은 조건의 레이를 한 번 더 쏘고 있었는데,
        /// 레이캐스트가 두 벌이면 나중에 이 판정에 레이어 마스크나 QueryTriggerInteraction 같은 조건이
        /// 하나라도 추가되는 순간 화면에 보이는 대상과 E키가 실제로 잡는 대상이 조용히 갈라진다.
        /// InteractWithTarget / CookFirstRawFoodAtTarget이 쓰는 바로 이 메서드를 그대로 공개해
        /// 조준 판정의 유일한 소스로 만든다 - UI 쪽에 별도 구현을 두지 말 것.
        ///
        /// 상태를 바꾸지 않으므로 매 프레임 호출해도 안전하다.
        /// </summary>
        /// <param name="target">조준 중인 오브젝트. 아무것도 맞지 않으면 null.</param>
        /// <returns>조준 대상이 있으면 true.</returns>
        public bool TryGetLookTarget(out GameObject target)
        {
            target = null;
            if (interactionCamera == null)
                return false;

            Ray ray = new Ray(interactionCamera.transform.position, interactionCamera.transform.forward);
            if (!Physics.Raycast(ray, out RaycastHit hit, interactionDistance))
                return false;

            target = hit.collider.gameObject;
            return true;
        }
    }
}
