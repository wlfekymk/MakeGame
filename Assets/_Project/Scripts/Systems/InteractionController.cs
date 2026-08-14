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
                InteractWithTarget();

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
                waterStill.CollectInto(survivalStats);
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

            var workbench = target.GetComponent<BoatWorkbench>();
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

            var hazard = target.GetComponent<HazardSource>();
            if (hazard != null)
            {
                hazard.TryAttack(inventory, skills);
                return;
            }

            // 쉼터: 밤에 상호작용하면 취침해 아침까지 시간을 건너뛰고 소량 회복한다 (Shelter.TrySleep 참고).
            var shelter = target.GetComponent<Shelter>();
            if (shelter != null)
            {
                shelter.TrySleep(survivalClock, survivalStats);
                return;
            }
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
        /// </summary>
        private bool TryGetLookTarget(out GameObject target)
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
