using System.Collections.Generic;
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
    /// 섬/자원/위험요소/사냥감 배치는 저장된 worldSeed로 WorldMapManager.RegenerateWorld를 호출해
    /// 저장 시점과 동일하게 다시 만들어낸다 (재현 가능).
    /// B2-15 1단계: 플레이어가 설치한 구조물(모닥불/쉼터/물 증류기)의 위치·상태를 저장·복원한다
    /// (SaveStructures/RestoreStructures 참고). 개별 자원 노드의 채집 여부와 위험 요소·사냥감의
    /// 처치 여부는 여전히 저장하지 않으므로, 불러오면 섬 배치는 동일하지만 자원/위험요소는 다시
    /// 미채집/생존 상태로 리셋된다(다음 배치 예정, 이유는 RestoreStructures 근처 주석 참고).
    /// </summary>
    public class SaveLoadController : MonoBehaviour
    {
        [Header("연결")]
        public Transform player;
        public SurvivalStats survivalStats;
        public PlayerSkills playerSkills;
        public PlayerInventory playerInventory;
        public BoatConstructionSystem boatConstruction;
        public AircraftRepairSystem aircraftRepair;
        public SurvivalClock survivalClock;
        public IslandTravel islandTravel;
        public WorldMapManager worldMapManager;

        [Header("설치 구조물 프리팹 (B2-15 1단계)")]
        [Tooltip("불러오기 시 저장된 모닥불을 재생성할 프리팹. ItemData(모닥불키트).placementPrefab과" +
            " 같은 프리팹을 연결해야 한다. 비워두면 저장된 모닥불을 복원하지 못하고 경고만 남긴다.")]
        public GameObject campfirePrefab;

        [Tooltip("불러오기 시 저장된 쉼터를 재생성할 프리팹. ItemData(쉼터키트).placementPrefab과" +
            " 같은 프리팹을 연결해야 한다. 비워두면 저장된 쉼터를 복원하지 못하고 경고만 남긴다.")]
        public GameObject shelterPrefab;

        [Tooltip("불러오기 시 저장된 물 증류기를 재생성할 프리팹. ItemData(물증류기키트).placementPrefab과" +
            " 같은 프리팹을 연결해야 한다. 비워두면 저장된 물 증류기를 복원하지 못하고 경고만 남긴다.")]
        public GameObject waterStillPrefab;

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

            // 발견한 섬 목록을 함께 저장한다. RegenerateWorld는 섬을 처음부터 다시 만들기 때문에
            // 이 목록이 없으면 불러왔을 때 이미 가 봤던 섬도 전부 "미발견"으로 되돌아가 버린다.
            if (worldMapManager != null)
            {
                foreach (var island in worldMapManager.islands)
                {
                    if (island.isDiscovered)
                        data.discoveredIslandIds.Add(island.islandId);
                }
            }

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

            if (aircraftRepair != null)
            {
                data.aircraftRepairComplete = aircraftRepair.isRepairComplete;

                foreach (var entry in aircraftRepair.collectedMaterials)
                {
                    if (entry.item == null)
                        continue;
                    data.aircraftCollectedMaterials.Add(new ItemCountEntry { itemName = entry.item.itemName, count = entry.quantity });
                }
            }

            SaveStructures(data);

            string json = JsonUtility.ToJson(data, true);
            File.WriteAllText(SavePath, json);
            lastStatusMessage = $"저장 완료 ({System.DateTime.Now:HH:mm:ss})";
            AudioManager.Instance?.PlaySaveOrLoadFeedback(); // 저장 완료 확인음
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

            // 저장된 worldSeed로 섬/바다/자원/위험요소/사냥감 배치를 처음부터 다시 만들어, 저장 시점과
            // 동일한 섬 배치를 재현한다. (예전에는 필드 값만 갱신하고 실제로 재생성하지 않아 이 기능이
            // 이름만 있고 실제로는 아무 효과가 없는 죽은 기능이었다.)
            if (worldMapManager != null)
            {
                worldMapManager.RegenerateWorld(data.worldSeed);

                // 방금 새로 만든 섬들은 전부 isDiscovered=false 상태이므로, 저장해 둔 발견 목록으로
                // 다시 표시해 미니맵 섬 목록이 방문 기록을 잃지 않게 한다. 혹시 currentIslandId가
                // 목록에 빠져 있어도(구버전 저장 파일 등) 최소한 지금 서 있는 섬은 발견 상태로 맞춰준다.
                foreach (int islandId in data.discoveredIslandIds)
                    worldMapManager.DiscoverIsland(islandId);
                worldMapManager.DiscoverIsland(data.currentIslandId);
            }

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

            if (aircraftRepair != null)
            {
                aircraftRepair.isRepairComplete = data.aircraftRepairComplete;

                aircraftRepair.collectedMaterials.Clear();
                foreach (var saved in data.aircraftCollectedMaterials)
                {
                    ItemData itemData = FindItemDataByName(saved.itemName);
                    if (itemData == null)
                        continue;

                    aircraftRepair.collectedMaterials.Add(
                        new AircraftRepairSystem.MaterialRequirement { item = itemData, quantity = saved.count });
                }
            }

            if (survivalClock != null)
                survivalClock.elapsedSeconds = data.elapsedSeconds;

            if (islandTravel != null)
                islandTravel.currentIslandId = data.currentIslandId;

            if (data.hasCompletedFirstEnding && GameManager.Instance != null)
                GameManager.Instance.CompleteEnding();

            RestoreStructures(data);

            lastStatusMessage = $"불러오기 완료 ({System.DateTime.Now:HH:mm:ss})";
            AudioManager.Instance?.PlaySaveOrLoadFeedback(); // 불러오기 완료 확인음
            Debug.Log("[SaveLoadController] 게임을 불러왔습니다.");
        }

        /// <summary>
        /// 이름 → ItemData 조회 캐시. 최초 조회 시 1회만 구축하고 이후에는 이 캐시를 재사용한다.
        /// null이면 아직 구축 전(EnsureItemDataCache가 채워야 함)임을 뜻한다.
        /// </summary>
        private Dictionary<string, ItemData> itemDataByName;

        /// <summary>
        /// 이름으로 ItemData 에셋을 찾는다.
        /// 성능 개선(#4): 예전에는 Load()가 인벤토리/배 제작/경비행기 수리 재료를 복원할 때마다
        /// (아이템 개수만큼 반복 호출되므로) 매번 Resources.FindObjectsOfTypeAll로 메모리의 모든 ItemData를
        /// 처음부터 순회했다(O(n) 반복 호출 = O(n·m)). 최초 1회만 이름→ItemData 딕셔너리를 구축해
        /// 캐싱해두고, 이후 호출은 O(1) 딕셔너리 조회로 처리한다.
        /// 근본 한계(캐싱으로 해결되지 않는 부분)는 이 메서드가 아니라 EnsureItemDataCache의 XML 주석에
        /// 남겨 두었다 - 코디네이터 보고의 [요청] 항목 참고.
        /// </summary>
        private ItemData FindItemDataByName(string itemName)
        {
            EnsureItemDataCache();

            itemDataByName.TryGetValue(itemName, out ItemData found);
            return found;
        }

        /// <summary>
        /// itemDataByName 캐시가 비어 있으면(최초 호출) 이름→ItemData 딕셔너리를 구축한다.
        /// B2-16: 근본 한계 해결 - ItemDataRegistry(Resources/ 아래 배치될 SO, allItems 리스트로 모든
        /// ItemData를 직접 참조)를 우선 사용한다. Unity는 레지스트리 에셋을 로드하는 순간 그 리스트가
        /// 참조하는 ItemData를 전부 함께 로드하므로, 씬의 어떤 컴포넌트도 참조하지 않는 ItemData라도
        /// 레지스트리에만 등록돼 있으면 찾을 수 있다(예전 FindObjectsOfTypeAll의 "지금 메모리에 로드된
        /// 것만 찾는다"는 한계를 없앤다).
        /// [주의] 레지스트리 `.asset` 인스턴스는 아직 만들어지지 않았다(1단계: 클래스+로딩 경로만,
        /// 실제 에셋 생성/등록은 game-designer 담당 - 코디네이터 보고서의 [요청] 항목 참고). 레지스트리가
        /// 없으면 LoadFromResources()가 null을 반환하므로, 그 경우 이전 방식(FindObjectsOfTypeAll로 현재
        /// 로드된 모든 ItemData 순회)으로 안전하게 폴백한다 - 레지스트리가 생기기 전까지는 동작이 전혀
        /// 바뀌지 않는다.
        /// </summary>
        private void EnsureItemDataCache()
        {
            if (itemDataByName != null)
                return;

            itemDataByName = new Dictionary<string, ItemData>();

            ItemDataRegistry registry = ItemDataRegistry.LoadFromResources();
            if (registry != null)
            {
                foreach (var item in registry.allItems)
                    AddToItemDataCache(item);
            }
            else
            {
                // 레지스트리 에셋이 아직 없다(B2-16 1단계 상태) - 예전과 동일한 폴백 방식을 그대로 쓴다.
                var allItems = Resources.FindObjectsOfTypeAll<ItemData>();
                foreach (var item in allItems)
                    AddToItemDataCache(item);
            }
        }

        /// <summary>
        /// itemDataByName 캐시에 ItemData 하나를 추가한다. 이름이 비어 있으면 무시하고, 이름이 같은
        /// ItemData가 이미 있으면(설정 실수 등) 먼저 등록된 것을 유지하고 경고만 남긴다.
        /// </summary>
        private void AddToItemDataCache(ItemData item)
        {
            if (item == null || string.IsNullOrEmpty(item.itemName))
                return;

            if (itemDataByName.ContainsKey(item.itemName))
            {
                Debug.LogWarning($"[SaveLoadController] 이름이 중복된 ItemData를 발견했습니다: {item.itemName}. 먼저 찾은 항목을 사용합니다.");
                return;
            }

            itemDataByName.Add(item.itemName, item);
        }

        /// <summary>
        /// B2-15 1단계: 씬에 현재 설치돼 있는 모닥불/쉼터/물 증류기를 전부 찾아 위치·회전·상태를
        /// data.structures에 기록한다. 구조물은 InteractionController.PlaceFirstPlaceableItem이
        /// Instantiate(prefab, pos, rot) 로 부모 없이(루트로) 생성하므로, WorldMapManager 자식만 찾는
        /// 방식으로는 찾을 수 없어 FindObjectsByType으로 씬 전체에서 직접 찾는다.
        /// </summary>
        private void SaveStructures(SaveData data)
        {
            foreach (var cf in Object.FindObjectsByType<Campfire>(FindObjectsInactive.Exclude))
            {
                data.structures.Add(new StructureSaveEntry
                {
                    type = StructureType.Campfire,
                    posX = cf.transform.position.x,
                    posY = cf.transform.position.y,
                    posZ = cf.transform.position.z,
                    rotY = cf.transform.eulerAngles.y,
                    isLit = cf.isLit,
                    remainingFuelSeconds = cf.remainingFuelSeconds,
                });
            }

            foreach (var sh in Object.FindObjectsByType<Shelter>(FindObjectsInactive.Exclude))
            {
                data.structures.Add(new StructureSaveEntry
                {
                    type = StructureType.Shelter,
                    posX = sh.transform.position.x,
                    posY = sh.transform.position.y,
                    posZ = sh.transform.position.z,
                    rotY = sh.transform.eulerAngles.y,
                });
            }

            foreach (var ws in Object.FindObjectsByType<WaterStill>(FindObjectsInactive.Exclude))
            {
                data.structures.Add(new StructureSaveEntry
                {
                    type = StructureType.WaterStill,
                    posX = ws.transform.position.x,
                    posY = ws.transform.position.y,
                    posZ = ws.transform.position.z,
                    rotY = ws.transform.eulerAngles.y,
                    storedWater = ws.storedWater,
                });
            }
        }

        /// <summary>
        /// B2-15 1단계: 저장된 구조물 목록을 복원한다.
        /// [주의] RegenerateWorld는 WorldMapManager 자신의 자식만 지우므로(WorldMapManager.cs의 for문 참고), 부모 없이
        /// 루트로 생성되는 구조물은 그 청소 대상에 포함되지 않는다. 그래서 복원 전에 먼저
        /// ClearExistingStructures로 "불러오기 시점에 씬에 이미 남아있던" 구조물을 명시적으로 지워야,
        /// 불러온 뒤 옛 구조물과 복원된 구조물이 겹쳐 남는 중복 생성을 막을 수 있다.
        /// [다음 배치로 미룬 것] 자원 노드 채집 상태·위험 요소/사냥감 처치 상태는 이번에 포함하지
        /// 않았다 - 둘 다 절차적 생성(IslandResourceSpawner/HazardSpawner/CreatureSpawner)이 섬마다
        /// "규모별 배율 → 자원/확률 목록 순회 → UnityEngine.Random 호출"로 정해지는 순서에 의존하는데,
        /// 개별 채집/처치 상태를 안정적인 키로 저장하려면 "같은 worldSeed로 다시 생성했을 때 정확히
        /// 같은 순번으로 같은 노드/위험요소가 나온다"는 가정이 100% 보장돼야 한다. 지금 생성 코드에는
        /// 위치 지터·크기 지터처럼 시드 없는 UnityEngine.Random을 함께 쓰는 곳이 섞여 있어(예:
        /// IslandResourceSpawner.SpawnSingleNode의 scaleJitter), 그 순서 보장을 검증 없이 확신할 수
        /// 없었다. 절반만 맞는 인덱싱으로 엉뚱한 노드가 "이미 채집됨" 처리되는 것이 안 하느니만 못한
        /// 결과라고 판단해, 이번 배치에서는 검증 가능한 범위(구조물)만 완성하고 자원/위험요소는
        /// 다음 배치로 넘겼다(코디네이터 보고 참고).
        /// </summary>
        private void RestoreStructures(SaveData data)
        {
            ClearExistingStructures();

            if (data.structures == null)
                return;

            foreach (var entry in data.structures)
            {
                switch (entry.type)
                {
                    case StructureType.Campfire:
                        RestoreCampfire(entry);
                        break;
                    case StructureType.Shelter:
                        RestoreShelter(entry);
                        break;
                    case StructureType.WaterStill:
                        RestoreWaterStill(entry);
                        break;
                }
            }
        }

        /// <summary>
        /// 불러오기 시작 시점에 씬에 이미 설치돼 있던 모닥불/쉼터/물 증류기를 전부 제거한다.
        /// RegenerateWorld가 지우지 못하는 대상이라(위 RestoreStructures 주석 참고) 여기서 직접
        /// 정리해야, 복원된 구조물과 기존 구조물이 겹쳐서 이중으로 남지 않는다.
        /// </summary>
        private void ClearExistingStructures()
        {
            foreach (var cf in Object.FindObjectsByType<Campfire>(FindObjectsInactive.Include))
                Destroy(cf.gameObject);

            foreach (var sh in Object.FindObjectsByType<Shelter>(FindObjectsInactive.Include))
                Destroy(sh.gameObject);

            foreach (var ws in Object.FindObjectsByType<WaterStill>(FindObjectsInactive.Include))
                Destroy(ws.gameObject);
        }

        private bool campfirePrefabMissingWarned = false;
        private bool shelterPrefabMissingWarned = false;
        private bool waterStillPrefabMissingWarned = false;

        /// <summary>저장된 모닥불 한 채를 campfirePrefab으로 재생성하고 점화 상태/남은 연료를 되돌린다.</summary>
        private void RestoreCampfire(StructureSaveEntry entry)
        {
            if (campfirePrefab == null)
            {
                if (!campfirePrefabMissingWarned)
                {
                    Debug.LogError("[SaveLoadController] campfirePrefab이 연결되지 않아 저장된 모닥불을 복원할 수 없습니다. " +
                        "Inspector에서 ItemData(모닥불키트)와 같은 프리팹을 연결하세요.");
                    campfirePrefabMissingWarned = true;
                }
                return;
            }

            Vector3 position = new Vector3(entry.posX, entry.posY, entry.posZ);
            GameObject go = Instantiate(campfirePrefab, position, Quaternion.Euler(0f, entry.rotY, 0f));
            var campfire = go.GetComponent<Campfire>();
            if (campfire != null)
            {
                campfire.isLit = entry.isLit;
                campfire.remainingFuelSeconds = entry.remainingFuelSeconds;
            }
        }

        /// <summary>
        /// 저장된 쉼터 한 채를 shelterPrefab으로 재생성한다.
        /// 주의: Shelter.Awake()가 인스턴스화 직후 스스로 transform.position을 roofHeight만큼 위로
        /// 이동시킨다(지붕을 바닥에서 띄우기 위함). 저장해 둔 위치는 이미 그 보정이 끝난 "최종 위치"이므로,
        /// 그대로 다시 Instantiate하면 Awake가 또 한 번 roofHeight를 더해 저장/불러오기를 반복할 때마다
        /// 지붕이 계속 위로 떠오르는 누적 버그가 생긴다. 프리팹의 roofHeight 값을 미리 읽어 스폰 위치에서
        /// 빼 둠으로써, Awake 보정 후 정확히 저장된 위치로 돌아오게 한다.
        /// </summary>
        private void RestoreShelter(StructureSaveEntry entry)
        {
            if (shelterPrefab == null)
            {
                if (!shelterPrefabMissingWarned)
                {
                    Debug.LogError("[SaveLoadController] shelterPrefab이 연결되지 않아 저장된 쉼터를 복원할 수 없습니다. " +
                        "Inspector에서 ItemData(쉼터키트)와 같은 프리팹을 연결하세요.");
                    shelterPrefabMissingWarned = true;
                }
                return;
            }

            var prefabShelter = shelterPrefab.GetComponent<Shelter>();
            float roofHeight = prefabShelter != null ? prefabShelter.roofHeight : 0f;

            Vector3 savedPosition = new Vector3(entry.posX, entry.posY, entry.posZ);
            Vector3 spawnPosition = savedPosition - Vector3.up * roofHeight;
            Instantiate(shelterPrefab, spawnPosition, Quaternion.Euler(0f, entry.rotY, 0f));
        }

        /// <summary>저장된 물 증류기 한 대를 waterStillPrefab으로 재생성하고 저장된 물의 양을 되돌린다.</summary>
        private void RestoreWaterStill(StructureSaveEntry entry)
        {
            if (waterStillPrefab == null)
            {
                if (!waterStillPrefabMissingWarned)
                {
                    Debug.LogError("[SaveLoadController] waterStillPrefab이 연결되지 않아 저장된 물 증류기를 복원할 수 없습니다. " +
                        "Inspector에서 ItemData(물증류기키트)와 같은 프리팹을 연결하세요.");
                    waterStillPrefabMissingWarned = true;
                }
                return;
            }

            Vector3 position = new Vector3(entry.posX, entry.posY, entry.posZ);
            GameObject go = Instantiate(waterStillPrefab, position, Quaternion.Euler(0f, entry.rotY, 0f));
            var waterStill = go.GetComponent<WaterStill>();
            if (waterStill != null)
                waterStill.storedWater = entry.storedWater;
        }
    }
}
