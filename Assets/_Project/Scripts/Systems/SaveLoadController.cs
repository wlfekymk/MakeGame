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
    /// (SaveStructures/RestoreStructures 참고).
    /// B3-4/B3-5: IslandResourceSpawner/HazardSpawner/CreatureSpawner가 섬별 결정적 System.Random을
    /// 쓰도록 바뀌어(B3-3) 같은 worldSeed로 재생성하면 항상 같은 개체가 같은 자리에 나온다는 전제가
    /// 성립하게 되면서, 자원 노드의 부분/완전 채집 상태(SaveResourceNodes/RestoreResourceNodes)와
    /// 위험 요소·사냥감의 처치/포획 상태(SaveHazardsAndCreatures/RestoreHazardsAndCreatures)도
    /// 저장·복원 대상에 추가했다.
    /// [세이브 키 v2] 대조 키가 (islandIndex, spawnOrder=생성 순서 러닝 카운터)에서 (islandIndex,
    /// stableKey=Hash(섬, 종류, 종류 내 순번))로 바뀌었다(SaveData.StableSpawnKey / saveKeyVersion 참고).
    /// 옛(v1) 세이브는 로드 시 채집/처치/포획 목록만 버리고 나머지는 전부 살린다(Load의 버전 검사).
    /// </summary>
    public class SaveLoadController : MonoBehaviour
    {
        [Header("연결")]
        public Transform player;
        public SurvivalStats survivalStats;
        public PlayerSkills playerSkills;
        public PlayerInventory playerInventory;
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

        /// <summary>기록 중인 임시 파일. 다 쓰고 나서야 SavePath로 이름이 바뀐다(WriteSaveFileSafely 참고).</summary>
        private string TempSavePath => SavePath + ".tmp";

        /// <summary>직전 저장본. 본 파일이 깨졌을 때 Load가 여기로 폴백한다.</summary>
        private string BackupSavePath => SavePath + ".bak";

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
                // [세이브 키 v2] 이 파일의 채집/처치/포획 목록이 안정 키(stableKey)로 기록됐음을 표시.
                saveKeyVersion = SaveData.CurrentSaveKeyVersion,
                // [뗏목 v1] 이 파일이 해안 뗏목 시스템으로 기록됐음을 표시(옛 배 진행 필드는 없다).
                saveContentVersion = SaveData.CurrentSaveContentVersion,
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
                // 스택 단위로 접어 기록한다(정착 배치 3). 야자잎 42개가 42줄이 아니라 3줄이 된다.
                // 한 스택 안의 remainingUses가 대표값 하나로 접혀도 정보가 사라지지 않는 이유:
                // 스택으로 묶이는 것은 ItemData.IsStackable(maxUses <= 1)뿐이고, 이들은 인벤토리에
                // 있는 동안 remainingUses가 항상 같은 값이다(무제한 -1 또는 1회용 1). 내구도가 닳는
                // 도구(창/손도끼/라이터)는 스택되지 않아 인스턴스마다 한 줄씩 그대로 기록된다.
                foreach (var stack in playerInventory.GetStacks())
                {
                    if (stack.data == null)
                        continue;
                    data.inventory.Add(new InventorySaveEntry
                    {
                        itemName = stack.data.itemName,
                        remainingUses = stack.RemainingUses,
                        count = stack.count
                    });
                }
            }

            // 뗏목 상태. 씬에 배선하는 참조가 없다 - RaftStructure는 씬 로드마다 스스로 생기는
            // 런타임 오브젝트라 static Active로만 접근한다. 뗏목이 아직 없으면 두 값 모두 0이 나가고,
            // 그것이 정확히 "한 칸도 안 깔았다"는 뜻이다.
            var raft = RaftStructure.Active;
            if (raft != null)
            {
                data.raftBaseTileCount = raft.BaseTileCount;
                data.raftInstalledParts = (int)raft.InstalledParts;

                // [콘텐츠 v2] 칸별 구성(종류 + 갑판 바닥재). raftBaseTileCount는 **그대로 함께 기록한다** -
                // 두 값이 어긋날 일이 없고(같은 상태에서 뽑는다), 이 파일을 여는 옛 빌드가 칸 수만 읽어도
                // 뗏목이 그럭저럭 되살아난다. WriteBaseTileCodes는 넘긴 목록을 채우므로 할당이 없다.
                raft.WriteBaseTileCodes(data.raftBaseTiles);
            }

            // [B25] 건축 조각. BuildingSystem은 RuntimeInitializeOnLoadMethod로 스스로 생기므로
            // 씬 배선이 없다 - Instance로만 접근한다. 아무것도 안 지었으면 ""가 저장된다.
            data.buildStructureJson = BuildingSystem.Instance != null
                ? BuildingSystem.Instance.SerializeToJson()
                : "";

            // [배치 39] 보관 상자. 조각 JSON과 달리 등급·내용물까지 실어야 해서 별도 목록으로 나간다
            // (SaveData.storageChests). BuildingSystem이 없으면 목록은 비어 있는 채로 저장된다.
            if (BuildingSystem.Instance != null)
                BuildingSystem.Instance.SerializeChests(data.storageChests);

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
            SaveResourceNodes(data);
            SaveHazardsAndCreatures(data);

            string json = JsonUtility.ToJson(data, true);
            if (!WriteSaveFileSafely(json))
            {
                lastStatusMessage = "저장에 실패했습니다(파일 기록 오류).";
                return;
            }

            lastStatusMessage = $"저장 완료 ({System.DateTime.Now:HH:mm:ss})";
            AudioManager.Instance?.PlaySaveOrLoadFeedback(); // 저장 완료 확인음
            Debug.Log($"[SaveLoadController] 게임을 저장했습니다: {SavePath}");
        }

        /// <summary>
        /// 저장 파일을 "임시 파일에 전부 쓰고 → 기존 파일을 .bak으로 물린 뒤 → 임시 파일의 이름을 바꾼다"
        /// 순서로 기록한다. 성공하면 true.
        /// 예전에는 File.WriteAllText가 기존 파일을 먼저 잘라내고 그 자리에 바로 썼기 때문에, 쓰는 도중에
        /// 게임이 죽거나(크래시/강제 종료) 디스크가 꽉 차면 하나뿐인 세이브 파일이 반쯤 잘린 채 남아
        /// 그 판이 통째로 사라졌다. 이 순서라면 어느 순간에 멈추더라도 SavePath 또는 BackupSavePath 중
        /// 최소 한 쪽은 항상 온전한 파일이다(Load가 본 파일 → .bak 순으로 시도한다).
        /// [세이브 호환성] 파일의 "내용" 형식은 1비트도 바뀌지 않는다 - 기존 makegame_save.json을 그대로
        /// 읽고 그대로 쓴다. 늘어나는 것은 옆에 생기는 .bak/.tmp 파일뿐이다.
        /// </summary>
        private bool WriteSaveFileSafely(string json)
        {
            try
            {
                File.WriteAllText(TempSavePath, json);

                if (File.Exists(SavePath))
                {
                    // File.Replace가 가장 깔끔하지만 플랫폼/파일시스템에 따라 지원되지 않을 수 있어
                    // 실패하면 "기존 파일을 .bak으로 옮기고 임시 파일을 본 파일로 옮기는" 방식으로 떨어진다.
                    try
                    {
                        File.Replace(TempSavePath, SavePath, BackupSavePath);
                        return true;
                    }
                    // [B12 qa 지적] IOException 한정이면 File.Replace가 UnauthorizedAccessException /
                    // PlatformNotSupportedException을 던지는 플랫폼에서 폴백 경로를 못 타고 바깥 catch로
                    // 빠져 매번 저장 실패한다. 본 파일은 안 깨지지만 .tmp 잔여물만 쌓인다.
                    catch (System.Exception)
                    {
                        if (File.Exists(BackupSavePath))
                            File.Delete(BackupSavePath);
                        File.Move(SavePath, BackupSavePath);
                    }
                }

                File.Move(TempSavePath, SavePath);
                return true;
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[SaveLoadController] 저장 파일을 기록하지 못했습니다: {e.Message}");
                return false;
            }
        }

        /// <summary>
        /// 저장 파일 하나를 읽어 SaveData로 되돌린다. 파일이 없거나 내용이 깨져 있으면 null을 반환한다.
        /// JsonUtility.FromJson은 잘린/망가진 JSON에 대해 null이 아니라 예외를 던지므로, 예전 코드의
        /// "null이면 실패" 검사만으로는 걸러지지 않고 F9를 누를 때마다 예외가 그대로 터져 나왔다.
        /// </summary>
        private SaveData ReadSaveData(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
                return null;

            try
            {
                return JsonUtility.FromJson<SaveData>(File.ReadAllText(path));
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[SaveLoadController] 저장 파일을 읽지 못했습니다({path}): {e.Message}");
                return null;
            }
        }

        /// <summary>
        /// 저장 파일이 있으면 읽어와 현재 게임 상태(플레이어 위치, 생존 수치, 스킬, 인벤토리, 배 제작 진행,
        /// 경과 일수, 현재 섬, 엔딩 달성 여부)에 되돌려 적용한다. 파일이 없으면 아무 것도 하지 않는다.
        /// </summary>
        public void Load()
        {
            SaveData data = ReadSaveData(SavePath);

            // 본 파일이 없거나 깨졌으면 직전 저장본(.bak)으로 되살려 본다. 저장이 쓰다 만 채로 중단돼도
            // 한 판이 통째로 날아가지 않게 하는 마지막 방어선이다(WriteSaveFileSafely 참고).
            if (data == null)
            {
                data = ReadSaveData(BackupSavePath);
                if (data != null)
                    Debug.LogWarning("[SaveLoadController] 본 저장 파일을 읽지 못해 직전 백업(.bak)으로 불러옵니다.");
            }

            if (data == null)
            {
                lastStatusMessage = File.Exists(SavePath) ? "저장 파일을 읽는 데 실패했습니다." : "저장 파일이 없습니다.";
                Debug.LogWarning($"[SaveLoadController] {lastStatusMessage}");
                return;
            }

            // [세이브 키 v2] 옛(v1 이하) 세이브의 채집/처치/포획 목록은 러닝 카운터(spawnOrder) 키로
            // 기록돼 있어 새 안정 키(stableKey)와 대조할 수 없다. **그 세 목록만 버리고** 나머지
            // (인벤토리·건축·상자·구조물·뗏목/비행기 진행·시계·발견 섬 등)는 전부 그대로 복원한다 -
            // 월드는 worldSeed로 재생성되므로 세 목록을 버리면 노드/위험요소/사냥감이 전부 "온전한
            // 상태"로 시작할 뿐, 다른 진행은 아무것도 잃지 않는다. 마이그레이션은 하지 않는다
            // (SaveData.saveKeyVersion 주석 참고 - 사용자가 옛 세이브 포기를 허락했다).
            if (data.saveKeyVersion < SaveData.CurrentSaveKeyVersion)
            {
                Debug.LogWarning($"[SaveLoadController] 옛 세이브 키 버전(v{data.saveKeyVersion})입니다." +
                    " 자원 채집/위험요소 처치/사냥감 포획 상태만 초기화하고 나머지는 그대로 불러옵니다.");
                data.partialResourceNodes?.Clear();
                data.defeatedHazards?.Clear();
                data.caughtCreatures?.Clear();
            }

            // [뗏목 v1] 3단계 도면-작업대 배 시스템 시절의 세이브(saveContentVersion 없음 = 0)를 열었다.
            // 옛 boat* 키는 JsonUtility가 조용히 버리므로 데이터가 깨지지는 않지만, 플레이어 입장에서는
            // "배 진행이 사라졌다"로 보이므로 로그 한 줄로 이유를 남긴다. 그 외 모든 진행은 그대로다.
            //
            // [콘텐츠 v2] 버전이 둘로 늘었으므로 **단계별로** 안내한다. 예전처럼
            // "< CurrentSaveContentVersion" 하나로 묶으면, 배 진행과 아무 상관없는 v1(해안 뗏목) 세이브를
            // 열 때마다 "배 단계가 사라졌다"는 엉뚱한 경고가 나온다.
            if (data.saveContentVersion < SaveData.RaftContentVersion)
            {
                Debug.LogWarning("[SaveLoadController] 옛 배 제작 시스템 시절의 세이브입니다." +
                    " 배 단계/도면/투입 재료는 더 이상 존재하지 않아 사라지고, 뗏목은 바닥판 0칸에서" +
                    " 시작합니다. 나머지 진행(인벤토리·건축·구조물·시계 등)은 그대로 불러옵니다.");
            }
            else if (data.saveContentVersion < SaveData.RaftTileDetailContentVersion)
            {
                Debug.LogWarning("[SaveLoadController] 바닥판 칸별 구성이 없던 시절의 세이브입니다." +
                    $" 뗏목 {data.raftBaseTileCount}칸을 모두 '통나무 바닥판 + 갑판 바닥재'로 되살립니다" +
                    " (갑판 높이가 같아 갑판 위 건축물은 그대로 유지됩니다). 장착 부품은 그대로입니다.");
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
                TeleportPlayer(new Vector3(data.playerX, data.playerY, data.playerZ), data.playerRotY);

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

                    // saved.Count는 옛 세이브(count 키가 없어 0으로 읽히는 항목)를 1개로 해석한다.
                    // 따라서 야자잎이 42줄로 나열된 옛 세이브도 42개 그대로 복원되고, 복원된 뒤
                    // 스택 뷰(GetStacks)에서 3칸으로 접혀 보인다 - 아이템이 사라지는 경로가 없다.
                    // 용량 검사를 거치지 않는 AddItemIgnoringCapacity를 쓰는 이유는 PlayerInventory
                    // 쪽 주석 참고(넘치면 버리는 대신 넘친 채로 복원하고 경고만 남긴다).
                    int count = saved.Count;
                    for (int i = 0; i < count; i++)
                        playerInventory.AddItemIgnoringCapacity(itemData, saved.remainingUses);
                }

                playerInventory.NotifyInventoryChanged();

                if (playerInventory.UsedSlots > playerInventory.SlotCapacity)
                {
                    Debug.LogWarning($"[SaveLoad] 복원된 인벤토리가 칸 상한을 넘었다" +
                        $" ({playerInventory.UsedSlots}/{playerInventory.SlotCapacity}칸)." +
                        " 아이템은 그대로 두고, 칸이 빌 때까지 새로 줍는 것만 막힌다.");
                }
            }

            // 뗏목 상태 복원. **건축 조각 복원보다 반드시 앞이어야 한다** - 갑판(BuildSpace.Deck) 위에
            // 지은 조각은 뗏목에 갑판이 있어야만 되살아나고(BuildingSystem.IsDeckReady), 갑판 유무는
            // 바로 아래에서 되돌리는 바닥판 칸 수가 정한다. 순서가 뒤집히면 갑판 조각이 전부
            // pendingDeckEntries로 밀려 그 세션 동안 보이지 않는다.
            //
            // RaftStructure.EnsureInstance()로 확보하는 이유: 뗏목은 씬 로드 훅으로 생기는데, 그 훅과
            // 이 복원 코드의 실행 순서는 보장되지 않는다(AGENT_BRIEF 4장). 이미 있으면 그대로 쓴다.
            var raft = RaftStructure.EnsureInstance();
            if (raft != null)
            {
                // ApplySavedState 하나로 값 대입 + 외형 재생성 + ProgressChanged 발행이 모두 끝난다.
                // 뗏목이 아직 해안 자리를 못 잡았으면 외형은 정박 직후 프레임에 자동으로 세워진다.
                //
                // [콘텐츠 v2] 칸별 구성을 함께 넘긴다. 목록이 비었거나(v1 이하) 칸 수보다 짧으면
                // ApplySavedState가 모자란 칸을 통나무 + 갑판 바닥재로 승격한다 - 승격 규칙을 여기
                // 두지 않는 이유는, 로드 경로가 늘어날 때마다 규칙이 갈라지면 갑판 높이가 어긋나기 때문이다.
                raft.ApplySavedState(data.raftBaseTileCount, (RaftPart)data.raftInstalledParts,
                    data.raftBaseTiles);
            }

            // [B25] 건축 조각 복원. **반드시 RegenerateWorld(위쪽) 뒤여야 한다** -
            // 순서가 뒤집히면 새 지형을 만드는 도중의 레이캐스트에 방금 되살린 조각이 섞인다.
            // 조각은 저장된 절대 좌표로 되살아나므로 지형 재생성 자체에는 의존하지 않는다.
            if (BuildingSystem.Instance != null)
            {
                BuildingSystem.Instance.RestoreFromJson(data.buildStructureJson);

                // [배치 39] 상자는 **조각 복원 뒤**여야 한다 - RestoreFromJson이 격자 표를 통째로 비우므로
                // 순서가 뒤집히면 방금 세운 상자가 그대로 지워진다. 옛 세이브에는 이 목록이 없어 빈 채로
                // 읽히고, 그때 RestoreChests는 아무것도 하지 않는다(경고도 나지 않는다).
                BuildingSystem.Instance.RestoreChests(data.storageChests);
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
            RestoreResourceNodes(data);
            RestoreHazardsAndCreatures(data);

            lastStatusMessage = $"불러오기 완료 ({System.DateTime.Now:HH:mm:ss})";
            AudioManager.Instance?.PlaySaveOrLoadFeedback(); // 불러오기 완료 확인음
            Debug.Log("[SaveLoadController] 게임을 불러왔습니다.");
        }

        /// <summary>
        /// 저장된 위치/시선으로 플레이어를 순간이동시킨다.
        /// CharacterController가 붙어 있으면 잠깐 껐다가 다시 켠다 - CharacterController는 PhysX 쪽에
        /// 자기 위치를 따로 들고 있어서, 켜진 채로 transform.position만 대입하면 다음 Move()에서 옛
        /// 위치로 되돌아갈 수 있다(이 프로젝트는 Physics.autoSyncTransforms가 기본 false라 더 잘 걸린다 -
        /// AGENT_BRIEF 4장). 껐다 켜면 활성화 시점의 transform 값으로 내부 위치가 다시 잡힌다.
        /// 컨트롤러는 이 메서드 안에서 동기적으로 다시 켜지므로 PlayerController.Update가 끼어들 틈이 없다.
        /// 마지막에 Physics.SyncTransforms를 불러, 같은 프레임 안에서 이어지는 구조물/자원 복원이 옛
        /// 위치의 콜라이더를 보지 않게 한다.
        /// </summary>
        private void TeleportPlayer(Vector3 position, float rotationY)
        {
            var controller = player.GetComponent<CharacterController>();
            bool wasEnabled = controller != null && controller.enabled;
            if (wasEnabled)
                controller.enabled = false;

            player.position = position;
            player.eulerAngles = new Vector3(0f, rotationY, 0f);

            if (wasEnabled)
                controller.enabled = true;

            Physics.SyncTransforms();
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
                    // 정착 배치 1: 단계와 슬롯. 슬롯은 좌표가 아니라 인덱스라 집 한 채가 여전히
                    // StructureSaveEntry 1개 + 정수 1개로 끝난다(Design_Settlement 2-1).
                    level = sh.level,
                    slotMask = sh.slotMask,
                    // 정착 배치 2: 저장궤의 보관물/결과물/훈연 진행도. ItemData는 직렬화할 수 없으므로
                    // 인벤토리(InventorySaveEntry)와 같은 관례대로 itemName으로만 기록한다.
                    chestItems = ToItemCountEntries(sh.ChestStock),
                    chestYield = ToItemCountEntries(sh.ChestYield),
                    chestDryingProgress = sh.DryingProgressSeconds,
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
        /// [B3-4/B3-5로 해소됨] 예전에는 여기서 자원 노드 채집 상태·위험 요소/사냥감 처치 상태를
        /// 다음 배치로 미뤘었다 - 절차적 생성(IslandResourceSpawner/HazardSpawner/CreatureSpawner)이
        /// "같은 worldSeed로 다시 생성했을 때 정확히 같은 순번으로 같은 노드/위험요소가 나온다"는
        /// 전제가 100% 보장돼야 개별 상태를 안정적인 키로 저장할 수 있는데, 당시 코드에는 위치 지터·
        /// 크기 지터처럼 시드 없는 UnityEngine.Random이 섞여 있어 그 순서 보장을 확신할 수 없었다.
        /// B3-3에서 5개 스포너 전부가 섬별 결정적 System.Random 스트림을 쓰도록 바뀌어 그 전제가
        /// 성립하게 되면서, 이제 RestoreResourceNodes/RestoreHazardsAndCreatures가 (islandIndex,
        /// spawnOrder) 키로 자원 노드 채집 상태·위험 요소/사냥감 처치 상태까지 복원한다.
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
            GameObject go = Instantiate(shelterPrefab, spawnPosition, Quaternion.Euler(0f, entry.rotY, 0f));

            // 정착 배치 1: 단계/슬롯 복원. Awake는 이미 Lv1 비주얼을 그린 상태이므로 ApplySavedState가
            // 값을 되돌린 뒤 BuildVisual로 다시 그린다(재료는 소모하지 않는다). level 0은 이 필드가
            // 없던 옛 세이브라는 뜻이며 Lv1로 해석된다. 위치 보정은 Awake 1회뿐이라 여기서 다시
            // 떠오르는 일은 없다.
            var shelter = go.GetComponent<Shelter>();
            if (shelter != null)
            {
                shelter.ApplySavedState(entry.level, entry.slotMask);

                // 정착 배치 2: 저장궤 상태. 이름 → ItemData 해석은 이미 캐시를 들고 있는 이쪽에서 한다
                // (Shelter가 같은 캐시를 또 만들지 않도록). 이름이 풀리지 않는 항목은 조용히 버려진다 -
                // 인벤토리 복원(RestoreInventory)이 이름 미해결 항목을 다루는 방식과 같은 규칙이다.
                shelter.RestoreChestState(
                    ToShelterStacks(entry.chestItems),
                    ToShelterStacks(entry.chestYield),
                    entry.chestDryingProgress);
            }
        }

        /// <summary>Shelter의 저장궤 묶음 목록을 이름+개수 저장 항목으로 바꾼다(ItemData는 직렬화 불가).</summary>
        private static List<ItemCountEntry> ToItemCountEntries(IReadOnlyList<ShelterItemStack> stacks)
        {
            var entries = new List<ItemCountEntry>();
            if (stacks == null)
                return entries;

            for (int i = 0; i < stacks.Count; i++)
            {
                ShelterItemStack stack = stacks[i];
                if (stack == null || stack.data == null || stack.count <= 0)
                    continue;

                entries.Add(new ItemCountEntry { itemName = stack.data.itemName, count = stack.count });
            }

            return entries;
        }

        /// <summary>저장된 이름+개수 목록을 ItemData 참조가 붙은 저장궤 묶음으로 되돌린다.</summary>
        private List<ShelterItemStack> ToShelterStacks(List<ItemCountEntry> entries)
        {
            var stacks = new List<ShelterItemStack>();
            if (entries == null)
                return stacks;

            foreach (var entry in entries)
            {
                if (entry == null || string.IsNullOrEmpty(entry.itemName) || entry.count <= 0)
                    continue;

                ItemData data = FindItemDataByName(entry.itemName);
                if (data == null)
                {
                    Debug.LogWarning($"[SaveLoadController] 저장궤에 있던 '{entry.itemName}'의 ItemData를 찾지 못해 복원하지 못했습니다.");
                    continue;
                }

                stacks.Add(new ShelterItemStack(data, entry.count));
            }

            return stacks;
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

        /// <summary>
        /// 섬 인덱스와 안정 키를 딕셔너리 키로 쓸 수 있는 long 하나로 합친다. islandIndex를 상위
        /// 32비트, stableKey를 하위 32비트에 넣는다 - islandIndex는 음수(-1, 섬에 속하지 않는 상어
        /// 등)일 수 있어 그대로 두고, stableKey는 부호와 무관한 해시 비트열이므로 uint로 캐스팅해
        /// 부호 확장으로 상위 비트가 오염되지 않게 한다.
        /// [세이브 키 v2] 두 번째 인자가 spawnOrder(러닝 카운터)에서 stableKey(종류별 안정 해시)로
        /// 바뀌었다. (islandIndex, stableKey) 쌍으로 대조하므로 32비트 해시 충돌은 같은 섬 안에서만
        /// 문제가 되고, 실질 확률은 무시 가능하다(섬 하나의 개체 수백 개 기준 ~1e-5 수준). 그래도
        /// 같은 쌍이 2개 발견되면 복원 딕셔너리를 만들 때 경고 로그를 남긴다.
        /// </summary>
        private static long CombineSpawnKey(int islandIndex, int stableKey)
        {
            return ((long)islandIndex << 32) | (uint)stableKey;
        }

        /// <summary>
        /// B3-4: 씬에 있는 모든 ResourceNode 중 "온전한 상태(remainingHarvestCount == maxHarvestCount)"가
        /// 아닌 노드만 골라 (islandIndex, stableKey, remainingHarvestCount)로 기록한다
        /// ([세이브 키 v2] spawnOrder도 디버깅 참고용으로 함께 적지만 대조에는 쓰지 않는다).
        /// 온전한 노드까지 전부 저장하지 않는 이유: 특대 섬 하나에만 자원 노드가 최대 수백 개(예: 나뭇가지
        /// baseCount=3 x extraLargeMultiplier=4 = 12개 같은 항목이 resourceEntries 개수만큼) 나올 수 있고
        /// 섬이 8개라면 전체 노드 수가 수천 개에 이를 수 있는데, 그중 대다수는 플레이어가 아직 손대지
        /// 않은 "완전 채집 가능" 상태다. 목록에 없는 (islandIndex, spawnOrder)는 RestoreResourceNodes가
        /// "온전한 상태로 남겨둔다"로 해석하므로, 부분/완전 소진된 노드만 저장해도 정보 손실이 없다.
        /// spawnOrder가 음수인 노드(스포너를 거치지 않고 수동 배치된 노드 등, ResourceNode.islandIndex/
        /// spawnOrder 주석의 -1 기본값 참고)는 안정적인 키가 없으므로 저장 대상에서 제외한다.
        /// </summary>
        private void SaveResourceNodes(SaveData data)
        {
            foreach (var node in Object.FindObjectsByType<ResourceNode>(FindObjectsInactive.Include))
            {
                if (node.spawnOrder < 0)
                    continue;

                if (node.remainingHarvestCount == node.maxHarvestCount)
                    continue;

                data.partialResourceNodes.Add(new ResourceNodeSaveEntry
                {
                    islandIndex = node.islandIndex,
                    spawnOrder = node.spawnOrder,
                    remainingHarvestCount = node.remainingHarvestCount,
                    stableKey = node.stableKey,
                });
            }
        }

        /// <summary>
        /// B3-4: RegenerateWorld로 새로 생성된 자원 노드들 중, 저장된 (islandIndex, stableKey) 키와
        /// 일치하는 노드를 찾아 remainingHarvestCount를 되돌린다.
        /// [세이브 키 v2] stableKey는 (섬, 아이템 이름, 같은 아이템 안에서의 순번)의 순수 해시라
        /// (StableSpawnKey 참고), 같은 worldSeed로 재생성하면 같은 노드가 항상 같은 키를 받는다 -
        /// 게다가 종류 안에서만 순번을 세므로 다른 종류의 엔트리 추가/증량에도 키가 밀리지 않는다.
        /// 목록에 없는 노드는 손대지 않는다(스폰 직후의 기본값 그대로 = 온전한 상태이므로 맞다).
        /// 버그 수정: 이 메서드는 WorldMapManager.RegenerateWorld 호출 직후, 같은 Load() 호출 안에서(=
        /// 같은 프레임 안에서) 실행된다. Destroy()는 프레임 끝까지 지연되므로, 이 시점에는 RegenerateWorld가
        /// 막 지운 "옛" 자원 노드와 방금 새로 생성한 "새" 자원 노드가 동일한 (islandIndex, stableKey) 키로
        /// 동시에 씬에 존재할 수 있다. FindObjectsInactive.Include로 둘 다 주웠다면 어느 쪽이 딕셔너리에
        /// 최종적으로 남는지가 Unity의 열거 순서에 암묵적으로 의존하게 되고, 하필 옛(파괴 예정) 노드가
        /// 남으면 그 노드에 채집 상태를 복원한 뒤 프레임 끝에 함께 파괴되어 방금 복원한 진행도가 조용히
        /// 사라진다. WorldMapManager.RegenerateWorld는 Destroy 예약 직전에 옛 오브젝트를 SetActive(false)로
        /// 먼저 비활성화하므로(WorldMapManager.cs 주석 참고), "지금 active인 노드 = 방금 새로 생성된 진짜
        /// 노드"라는 구분이 성립한다. FindObjectsInactive.Exclude로 바꿔 비활성 상태(=파괴 예정인 옛 노드)를
        /// 아예 조회 대상에서 뺐다. ResourceNode 자신은 채집이 소진되어도 gameObject.SetActive(false)를
        /// 스스로 호출하지 않으므로(ResourceNode.cs 확인 - Tick/Harvest 모두 데이터 필드만 바꾼다) Exclude로
        /// 바꿔도 정상적으로 살아있는 노드가 걸러지는 부작용은 없다.
        /// [주의] 이 필터링은 WorldMapManager.RegenerateWorld가 Destroy 전에 SetActive(false)를 호출한다는
        /// 전제에 의존한다 - 그 SetActive(false) 호출이 나중에 제거되면 이 구분이 조용히 무너진다.
        /// </summary>
        private void RestoreResourceNodes(SaveData data)
        {
            if (data.partialResourceNodes == null || data.partialResourceNodes.Count == 0)
                return;

            var nodesByKey = new Dictionary<long, ResourceNode>();
            foreach (var node in Object.FindObjectsByType<ResourceNode>(FindObjectsInactive.Exclude))
            {
                if (node.spawnOrder < 0)
                    continue;

                // [세이브 키 v2] 해시 충돌 감시: 같은 (islandIndex, stableKey) 쌍이 2개면 경고를 남기고
                // 먼저 발견된 노드를 유지한다(뒤 노드는 대조에서 빠져 온전한 상태로 남는다 - 데이터가
                // 엉뚱한 노드에 붙는 것보다 안전한 실패다).
                long key = CombineSpawnKey(node.islandIndex, node.stableKey);
                if (nodesByKey.ContainsKey(key))
                {
                    Debug.LogWarning($"[SaveLoadController] 자원 노드 안정 키 충돌: island {node.islandIndex}," +
                        $" stableKey {node.stableKey} ({node.name}). 먼저 발견된 노드를 유지합니다.");
                    continue;
                }
                nodesByKey.Add(key, node);
            }

            foreach (var saved in data.partialResourceNodes)
            {
                if (nodesByKey.TryGetValue(CombineSpawnKey(saved.islandIndex, saved.stableKey), out ResourceNode node))
                    node.remainingHarvestCount = Mathf.Clamp(saved.remainingHarvestCount, 0, node.maxHarvestCount);
            }
        }

        /// <summary>
        /// B3-5: 씬에 있는 모든 HazardSource/HuntableCreature 중 현재 처치/포획된 것만 (islandIndex,
        /// stableKey) 키로 기록한다([세이브 키 v2] spawnOrder는 디버깅 참고용으로만 함께 적는다).
        /// 온전한(살아있는/잡히지 않은) 개체는 저장하지 않는다 - 자원 노드와
        /// 같은 이유로 목록에 없는 키는 "온전한 상태"로 간주되기 때문이다.
        /// </summary>
        private void SaveHazardsAndCreatures(SaveData data)
        {
            foreach (var hazard in Object.FindObjectsByType<HazardSource>(FindObjectsInactive.Include))
            {
                if (hazard.spawnOrder < 0 || hazard.IsActive)
                    continue;

                data.defeatedHazards.Add(new SpawnKeySaveEntry
                {
                    islandIndex = hazard.islandIndex,
                    spawnOrder = hazard.spawnOrder,
                    stableKey = hazard.stableKey,
                });
            }

            foreach (var creature in Object.FindObjectsByType<HuntableCreature>(FindObjectsInactive.Include))
            {
                if (creature.spawnOrder < 0 || creature.IsAvailable)
                    continue;

                data.caughtCreatures.Add(new SpawnKeySaveEntry
                {
                    islandIndex = creature.islandIndex,
                    spawnOrder = creature.spawnOrder,
                    stableKey = creature.stableKey,
                });
            }
        }

        /// <summary>
        /// B3-5: RegenerateWorld로 새로 생성된 위험 요소/사냥감 중, 저장된 키와 일치하는 대상을 찾아
        /// 처치/포획 상태로 되돌린다(HazardSource.RestoreDefeatedState / HuntableCreature.RestoreCaughtState).
        /// [설계 결정 - 오프라인 경과 시간 미반영] HazardSource/HuntableCreature 모두 모든 종류가 시간이
        /// 지나면 자동으로 재등장하며(HazardSource.Update 확인 결과 영구 제거되는 위험 요소 종류는 없다 -
        /// 전투 불가 종류인 독사/전갈/함정도 그냥 isDefeated가 될 수 없을 뿐 별도의 "영구 제거" 코드 경로가
        /// 없다), 저장 시점에 처치/포획 상태였던 respawnTimer 진행분은 저장하지 않고 항상 0으로 되돌린다.
        /// 즉 "저장하고 나서 실제로(현실 시간으로) 얼마나 지났는지"는 재등장 진행에 전혀 반영하지 않는다.
        /// 이렇게 결정한 이유: (1) 이 프로젝트의 다른 시간 기반 시스템(WaterStill의 증류 진행, Campfire의
        /// 남은 연료)도 저장된 값을 그대로 복원할 뿐 저장~로드 사이의 실시간 경과를 반영하는 벽시계
        /// 인프라가 전혀 없다 - 여기서만 오프라인 진행을 계산하면 시스템 간 동작이 일관되지 않는다.
        /// (2) SurvivalClock.elapsedSeconds도 게임 내 시간일 뿐 실제 시스템 시계를 기록/비교하지 않는다.
        /// (3) 오프라인 진행을 도입하려면 저장 시각(System.DateTime)을 SaveData에 별도로 추가해야 하는데,
        /// 이는 이번 B3-5 범위를 넘어서는 새 설계이고, 굳이 필요하다면 다음 배치에서 다른 시스템들과 함께
        /// 일관되게 재설계하는 편이 낫다고 판단했다.
        /// 버그 수정: RestoreResourceNodes와 동일한 이유로 FindObjectsInactive.Include를 Exclude로 바꿨다.
        /// 이 메서드도 WorldMapManager.RegenerateWorld 직후 같은 프레임에 실행되어, Destroy 예약만 되고
        /// 아직 실제로 파괴되지 않은 옛 HazardSource/HuntableCreature가 새로 생성된 것과 같은 (islandIndex,
        /// stableKey) 키로 공존할 수 있다 - Include였다면 처치/포획 상태가 하필 옛(파괴 예정) 개체에
        /// 복원되어 프레임 끝에 함께 사라지는 조용한 진행도 손실이 생길 수 있었다. HazardSource는 처치돼도
        /// SetVisualActive에서 Renderer/Collider의 enabled만 끄고 gameObject.SetActive(false)는 호출하지
        /// 않으며(HazardSource.cs 확인), HuntableCreature는 잡혀도 데이터 필드(isCaught)만 바꿀 뿐 아예
        /// SetActive를 호출하지 않으므로(HuntableCreature.cs 확인), "지금 active인 개체 = 방금 새로 생성된
        /// 진짜 개체"라는 구분이 정상적인 게임플레이 상태(처치/포획 여부)와 상관없이 성립한다. 즉 Exclude로
        /// 바꿔도 이미 처치/포획된 살아있는 개체가 걸러지는 부작용은 없다 - 씬에 남아있는 한 여전히 active다.
        /// [주의] 이 필터링은 WorldMapManager.RegenerateWorld가 Destroy 전에 SetActive(false)를 호출한다는
        /// 전제에 의존한다 - 그 SetActive(false) 호출이 나중에 제거되면 이 구분이 조용히 무너진다.
        /// </summary>
        private void RestoreHazardsAndCreatures(SaveData data)
        {
            // [세이브 키 v2] 아래 두 딕셔너리 모두 (islandIndex, stableKey)로 대조한다. 해시 충돌
            // (같은 쌍 2개)은 경고를 남기고 먼저 발견된 개체를 유지한다(RestoreResourceNodes와 같은 규칙).
            if (data.defeatedHazards != null && data.defeatedHazards.Count > 0)
            {
                var hazardsByKey = new Dictionary<long, HazardSource>();
                foreach (var hazard in Object.FindObjectsByType<HazardSource>(FindObjectsInactive.Exclude))
                {
                    if (hazard.spawnOrder < 0)
                        continue;

                    long key = CombineSpawnKey(hazard.islandIndex, hazard.stableKey);
                    if (hazardsByKey.ContainsKey(key))
                    {
                        Debug.LogWarning($"[SaveLoadController] 위험 요소 안정 키 충돌: island {hazard.islandIndex}," +
                            $" stableKey {hazard.stableKey} ({hazard.name}). 먼저 발견된 개체를 유지합니다.");
                        continue;
                    }
                    hazardsByKey.Add(key, hazard);
                }

                foreach (var saved in data.defeatedHazards)
                {
                    if (hazardsByKey.TryGetValue(CombineSpawnKey(saved.islandIndex, saved.stableKey), out HazardSource hazard))
                        hazard.RestoreDefeatedState(true);
                }
            }

            if (data.caughtCreatures != null && data.caughtCreatures.Count > 0)
            {
                var creaturesByKey = new Dictionary<long, HuntableCreature>();
                foreach (var creature in Object.FindObjectsByType<HuntableCreature>(FindObjectsInactive.Exclude))
                {
                    if (creature.spawnOrder < 0)
                        continue;

                    long key = CombineSpawnKey(creature.islandIndex, creature.stableKey);
                    if (creaturesByKey.ContainsKey(key))
                    {
                        Debug.LogWarning($"[SaveLoadController] 사냥감 안정 키 충돌: island {creature.islandIndex}," +
                            $" stableKey {creature.stableKey} ({creature.name}). 먼저 발견된 개체를 유지합니다.");
                        continue;
                    }
                    creaturesByKey.Add(key, creature);
                }

                foreach (var saved in data.caughtCreatures)
                {
                    if (creaturesByKey.TryGetValue(CombineSpawnKey(saved.islandIndex, saved.stableKey), out HuntableCreature creature))
                        creature.RestoreCaughtState(true);
                }
            }
        }
    }
}
