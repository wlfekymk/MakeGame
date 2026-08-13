using System.IO;
using UnityEngine;
using MakeGame.Data;
using MakeGame.Player;
using MakeGame.Managers;

namespace MakeGame.Systems
{
    /// <summary>
    /// 게임 진행 상황을 저장/불러오기하는 시스템.
    /// SaveData를 JsonUtility로 직렬화해 Application.persistentDataPath에 파일로 기록하고,
    /// 불러올 때는 씬을 재시작하지 않고 현재 오브젝트들의 값을 저장된 상태로 되돌려 적용한다.
    /// 자원 노드 채집 상태, 위험 요소 처치 상태, 플레이어가 설치한 구조물(물 증류기/쉼터)은
    /// 이번 1차 구현 범위에서는 저장하지 않는다 (섬/자원/위험요소는 월드 시드로 다시 생성됨).
    /// </summary>
    public class SaveLoadController : MonoBehaviour
    {
        [Header("연결")]
        public Transform player;
        public SurvivalStats survivalStats;
        public PlayerSkills playerSkills;
        public PlayerInventory playerInventory;
        public BoatConstructionSystem boatConstruction;
        public SurvivalClock survivalClock;
        public IslandTravel islandTravel;
        public WorldMapManager worldMapManager;

        [Header("단축키")]
        [Tooltip("빠른 저장 단축키")]
        public KeyCode saveKey = KeyCode.F5;
        [Tooltip("빠른 불러오기 단축키")]
        public KeyCode loadKey = KeyCode.F9;

        private const string SaveFileName = "makegame_save.json";

        /// <summary>저장 파일의 전체 경로 (플랫폼별 영구 저장 폴더 아래).</summary>
        private string SavePath => Path.Combine(Application.persistentDataPath, SaveFileName);

        /// <summary>가장 최근 저장/불러오기 결과 메시지 (디버그 HUD 등에서 표시할 수 있도록 보관).</summary>
        public string lastStatusMessage = "";

        /// <summary>매 프레임 저장/불러오기 단축키 입력을 감시한다.</summary>
        private void Update()
        {
            if (Input.GetKeyDown(saveKey))
                Save();

            if (Input.GetKeyDown(loadKey))
                Load();
        }

        /// <summary>
        /// 현재 게임 상태(플레이어 위치, 생존 수치, 스킬, 인벤토리, 배 제작 진행, 경과 일수, 현재 섬, 엔딩 달성 여부,
        /// 월드 시드)를 SaveData로 모아 JSON 파일로 기록한다.
        /// </summary>
        public void Save()
        {
            var data = new SaveData
            {
                worldSeed = worldMapManager != null ? worldMapManager.worldSeed : 0,
                elapsedSeconds = survivalClock != null ? survivalClock.elapsedSeconds : 0f,
                currentIslandId = islandTravel != null ? islandTravel.currentIslandId : 0,
                hasCompletedFirstEnding = GameManager.Instance != null && GameManager.Instance.HasCompletedFirstEnding,
            };

            if (player != null)
            {
                data.playerX = player.position.x;
                data.playerY = player.position.y;
                data.playerZ = player.position.z;
                data.playerRotY = player.eulerAngles.y;
            }

            if (survivalStats != null)
            {
                data.health = survivalStats.health;
                data.maxHealth = survivalStats.maxHealth;
                data.hunger = survivalStats.hunger;
                data.thirst = survivalStats.thirst;
                data.sunstroke = survivalStats.sunstroke;
                data.oxygen = survivalStats.oxygen;
                data.isPoisoned = survivalStats.isPoisoned;
                data.isBleeding = survivalStats.isBleeding;
                data.hasBrokenBone = survivalStats.hasBrokenBone;
            }

            if (playerSkills != null)
            {
                foreach (var skill in playerSkills.skills)
                    data.skills.Add(new SkillSaveEntry { type = skill.type, level = skill.level, experience = skill.experience });
            }

            if (playerInventory != null)
            {
                foreach (var item in playerInventory.items)
                {
                    if (item.data == null)
                        continue;
                    data.inventory.Add(new InventorySaveEntry { itemName = item.data.itemName, remainingUses = item.remainingUses });
                }
            }

            if (boatConstruction != null)
            {
                data.boatCurrentStage = boatConstruction.currentStage;
                data.boatHasBlueprint = boatConstruction.hasCurrentStageBlueprint;
                data.boatHighestCompletedStage = boatConstruction.highestCompletedStage;
                data.boatIsFullyComplete = boatConstruction.isFullyComplete;

                foreach (var entry in boatConstruction.collectedMaterialsForCurrentStage)
                {
                    if (entry.item == null)
                        continue;
                    data.boatCollectedMaterials.Add(new ItemCountEntry { itemName = entry.item.itemName, count = entry.quantity });
                }
            }

            string json = JsonUtility.ToJson(data, true);
            File.WriteAllText(SavePath, json);
            lastStatusMessage = $"저장 완료 ({System.DateTime.Now:HH:mm:ss})";
            Debug.Log($"[SaveLoadController] 게임을 저장했습니다: {SavePath}");
        }

        /// <summary>
        /// 저장 파일이 있으면 읽어와 현재 게임 상태(플레이어 위치, 생존 수치, 스킬, 인벤토리, 배 제작 진행,
        /// 경과 일수, 현재 섬, 엔딩 달성 여부)에 되돌려 적용한다. 파일이 없으면 아무 것도 하지 않는다.
        /// </summary>
        public void Load()
        {
            if (!File.Exists(SavePath))
            {
                lastStatusMessage = "저장 파일이 없습니다.";
                Debug.LogWarning("[SaveLoadController] 저장 파일이 없습니다.");
                return;
            }

            string json = File.ReadAllText(SavePath);
            var data = JsonUtility.FromJson<SaveData>(json);
            if (data == null)
            {
                lastStatusMessage = "저장 파일을 읽는 데 실패했습니다.";
                return;
            }

            // 섬/자원/위험요소 배치는 이번 구현에서 씬을 재생성하지 않으므로, 시드는 기록만 갱신해둔다.
            // (완전한 재현을 원하면 씬을 다시 로드한 뒤 이 시드로 월드를 새로 생성해야 한다.)
            if (worldMapManager != null)
                worldMapManager.worldSeed = data.worldSeed;

            if (player != null)
            {
                player.position = new Vector3(data.playerX, data.playerY, data.playerZ);
                player.eulerAngles = new Vector3(0f, data.playerRotY, 0f);
            }

            if (survivalStats != null)
            {
                survivalStats.health = data.health;
                survivalStats.maxHealth = data.maxHealth;
                survivalStats.hunger = data.hunger;
                survivalStats.thirst = data.thirst;
                survivalStats.sunstroke = data.sunstroke;
                survivalStats.oxygen = data.oxygen;
                survivalStats.isPoisoned = data.isPoisoned;
                survivalStats.isBleeding = data.isBleeding;
                survivalStats.hasBrokenBone = data.hasBrokenBone;
            }

            if (playerSkills != null)
            {
                foreach (var saved in data.skills)
                {
                    foreach (var skill in playerSkills.skills)
                    {
                        if (skill.type != saved.type)
                            continue;
                        skill.level = saved.level;
                        skill.experience = saved.experience;
                        break;
                    }
                }
            }

            if (playerInventory != null)
            {
                playerInventory.items.Clear();
                foreach (var saved in data.inventory)
                {
                    ItemData itemData = FindItemDataByName(saved.itemName);
                    if (itemData == null)
                        continue;

                    var invItem = new InventoryItem(itemData) { remainingUses = saved.remainingUses };
                    playerInventory.items.Add(invItem);
                }
            }

            if (boatConstruction != null)
            {
                boatConstruction.currentStage = data.boatCurrentStage;
                boatConstruction.hasCurrentStageBlueprint = data.boatHasBlueprint;
                boatConstruction.highestCompletedStage = data.boatHighestCompletedStage;
                boatConstruction.isFullyComplete = data.boatIsFullyComplete;

                boatConstruction.collectedMaterialsForCurrentStage.Clear();
                foreach (var saved in data.boatCollectedMaterials)
                {
                    ItemData itemData = FindItemDataByName(saved.itemName);
                    if (itemData == null)
                        continue;

                    boatConstruction.collectedMaterialsForCurrentStage.Add(
                        new BoatConstructionSystem.MaterialRequirement { item = itemData, quantity = saved.count });
                }
            }

            if (survivalClock != null)
                survivalClock.elapsedSeconds = data.elapsedSeconds;

            if (islandTravel != null)
                islandTravel.currentIslandId = data.currentIslandId;

            if (data.hasCompletedFirstEnding && GameManager.Instance != null)
                GameManager.Instance.CompleteEnding();

            lastStatusMessage = $"불러오기 완료 ({System.DateTime.Now:HH:mm:ss})";
            Debug.Log("[SaveLoadController] 게임을 불러왔습니다.");
        }

        /// <summary>
        /// 이름으로 ItemData 에셋을 찾는다. 별도의 Resources 등록 없이도, 씬의 여러 컴포넌트(인벤토리 시작 아이템,
        /// 배 제작 재료 설계, 제작 레시피 등)가 이미 참조 중인 ItemData는 메모리에 로드돼 있으므로
        /// FindObjectsOfTypeAll로 찾을 수 있다.
        /// </summary>
        private ItemData FindItemDataByName(string itemName)
        {
            var allItems = Resources.FindObjectsOfTypeAll<ItemData>();
            foreach (var item in allItems)
            {
                if (item.itemName == itemName)
                    return item;
            }
            return null;
        }
    }
}
