using System.Collections.Generic;
using UnityEngine;
using MakeGame.Data;

namespace MakeGame.Systems
{
    /// <summary>
    /// 저장 파일 하나에 담기는 전체 게임 상태.
    /// JsonUtility로 직렬화되므로 모든 필드가 값 타입이거나 [System.Serializable] 클래스여야 한다.
    /// B2-15 1단계: 플레이어가 설치한 구조물(모닥불/쉼터/물 증류기)의 위치·상태를 저장·복원 대상에
    /// 추가했다(structures 필드, 아래 StructureSaveEntry). 자원 노드 채집 상태와 위험 요소·사냥감의
    /// 처치/재등장 상태는 여전히 이번 범위에서 제외한다 - 둘 다 시드 기반 절차적 생성 순서에 의존하는
    /// "섬 인덱스 + 생성 순번" 키가 필요해 설계 난이도가 높고, 절반만 동작하는 상태로 남기는 위험을
    /// 피하기 위해 다음 배치로 미뤘다(자세한 내용은 SaveLoadController 보고 참고). 섬/자원/위험요소
    /// "배치"(위치) 자체는 worldSeed로 다시 생성되어 동일하게 재현되지만, 그 중 무엇을 이미
    /// 채집/처치했는지는 아직 기억하지 못한다.
    /// 하위호환: JsonUtility.FromJson은 대상 타입을 필드 초기값으로 먼저 만든 뒤 JSON에 있는 필드만
    /// 덮어쓰므로, structures처럼 나중에 추가된 필드가 없는 옛 세이브 파일을 불러와도 초기화 구문
    /// (= new List&lt;...&gt;())대로 빈 리스트가 되어 NullReferenceException 없이 안전하게 동작한다 -
    /// discoveredIslandIds/skills/inventory 등 기존 리스트 필드들도 이미 동일한 방식에 의존하고 있어
    /// (이 프로젝트에는 세이브 버전 필드가 없다) 별도 버전 필드/마이그레이션 없이 이 관례를 그대로
    /// 따랐다.
    /// </summary>
    [System.Serializable]
    public class SaveData
    {
        [Tooltip("섬/자원/위험요소 배치를 재현하기 위한 난수 시드")]
        public int worldSeed;

        [Tooltip("SurvivalClock의 경과 게임 내 시간(초)")]
        public float elapsedSeconds;

        [Tooltip("플레이어가 현재 위치한 섬 번호")]
        public int currentIslandId;

        [Tooltip("지금까지 발견한(방문한) 섬 번호 목록. worldSeed로 섬을 다시 만들면 IslandInstance가" +
            " 전부 새로 생성되어 isDiscovered가 초기값(false)으로 리셋되므로, 불러올 때 이 목록으로" +
            " 다시 발견 상태를 복원해야 미니맵 섬 목록이 방문 기록을 잃지 않는다.")]
        public List<int> discoveredIslandIds = new List<int>();

        [Tooltip("첫 엔딩(배 제작 완성) 달성 여부")]
        public bool hasCompletedFirstEnding;

        [Header("플레이어 위치")]
        public float playerX;
        public float playerY;
        public float playerZ;
        public float playerRotY;

        [Header("생존 수치")]
        public float health;
        public float maxHealth;
        public float hunger;
        public float thirst;
        public float sunstroke;
        public float oxygen;
        public bool isPoisoned;
        public bool isBleeding;
        public bool hasBrokenBone;

        [Header("스킬")]
        public List<SkillSaveEntry> skills = new List<SkillSaveEntry>();

        [Header("인벤토리")]
        public List<InventorySaveEntry> inventory = new List<InventorySaveEntry>();

        [Header("배 제작 진행")]
        public int boatCurrentStage;
        public bool boatHasBlueprint;
        public int boatHighestCompletedStage;
        public bool boatIsFullyComplete;
        public List<ItemCountEntry> boatCollectedMaterials = new List<ItemCountEntry>();

        [Header("경비행기 수리 진행")]
        public bool aircraftRepairComplete;
        public List<ItemCountEntry> aircraftCollectedMaterials = new List<ItemCountEntry>();

        [Header("설치 구조물 (B2-15 1단계)")]
        [Tooltip("플레이어가 설치한 모닥불/쉼터/물 증류기 각각의 위치·회전·상태 목록.")]
        public List<StructureSaveEntry> structures = new List<StructureSaveEntry>();

        [Header("자원 노드 채집 상태 (B3-4)")]
        [Tooltip("완전한 상태(remainingHarvestCount == maxHarvestCount)가 아닌 자원 노드만 기록한다.\n" +
            "특대 섬 하나에만 최대 수백 개의 노드가 나올 수 있어(섬 8개면 수천 개), 대부분을 차지하는" +
            " '아직 하나도 안 캔 노드'까지 전부 저장하면 세이브 파일이 불필요하게 커진다. 목록에 없는" +
            " (islandIndex, spawnOrder)는 '온전한 상태'로 간주한다(RestoreResourceNodes 참고).")]
        public List<ResourceNodeSaveEntry> partialResourceNodes = new List<ResourceNodeSaveEntry>();

        [Header("위험요소 처치 상태 (B3-5)")]
        [Tooltip("현재 처치되어(isDefeated) 재등장 대기 중인 위험 요소의 (islandIndex, spawnOrder) 키 목록.\n" +
            "모든 위험 요소 종류는 시간이 지나면 자동으로 재등장하므로(HazardSource.Update, 영구 제거되는" +
            " 종류 없음) '처치됨' 여부만 기록하면 충분하다. 재등장까지 남은 시간은 저장하지 않고 불러올 때" +
            " respawnTimer를 0으로 되돌린다 - 실시간(벽시계) 경과는 반영하지 않는다는 뜻이다. 자세한 근거는" +
            " SaveLoadController.RestoreHazardsAndCreatures 주석 참고.")]
        public List<SpawnKeySaveEntry> defeatedHazards = new List<SpawnKeySaveEntry>();

        [Header("사냥감 포획 상태 (B3-5)")]
        [Tooltip("현재 잡혀서(isCaught) 재등장 대기 중인 사냥감/물고기의 (islandIndex, spawnOrder) 키 목록.\n" +
            "위험 요소와 동일하게 '잡힘' 여부만 기록하고 재등장까지 남은 시간은 저장하지 않는다.")]
        public List<SpawnKeySaveEntry> caughtCreatures = new List<SpawnKeySaveEntry>();
    }

    /// <summary>스킬 하나의 저장 항목(종류, 레벨, 경험치).</summary>
    [System.Serializable]
    public class SkillSaveEntry
    {
        public SkillType type;
        public int level;
        public float experience;
    }

    /// <summary>인벤토리 아이템 하나의 저장 항목. ItemData 자체는 저장할 수 없으므로 이름으로 기록해두고,
    /// 불러올 때 이름으로 다시 ItemData 에셋을 찾아 연결한다.</summary>
    [System.Serializable]
    public class InventorySaveEntry
    {
        public string itemName;
        public int remainingUses;
    }

    /// <summary>아이템 이름 + 개수 쌍 (배 제작 투입 재료 등 수량 기반 저장에 사용).</summary>
    [System.Serializable]
    public class ItemCountEntry
    {
        public string itemName;
        public int count;
    }

    /// <summary>
    /// 플레이어가 설치한 구조물(모닥불/쉼터/물 증류기) 하나의 저장 항목.
    /// 세 종류 모두 필요한 필드가 조금씩 달라(Campfire는 점화 상태, WaterStill은 저장된 물), 종류별로
    /// 클래스를 나누는 대신 하나의 평평한(flat) 구조체에 전부 담고 type으로 구분한다 - JsonUtility는
    /// 다형성(상속 기반 직렬화)을 지원하지 않으므로, 이 프로젝트의 기존 저장 항목들(SkillSaveEntry,
    /// InventorySaveEntry, ItemCountEntry)과 동일하게 평평한 구조를 따랐다. 해당 없는 필드는 기본값
    /// (false/0)으로 남는다(예: Shelter 항목의 isLit/remainingFuelSeconds/storedWater는 항상 무시됨).
    /// </summary>
    [System.Serializable]
    public class StructureSaveEntry
    {
        [Tooltip("구조물 종류")]
        public StructureType type;

        [Header("위치/회전")]
        public float posX;
        public float posY;
        public float posZ;
        public float rotY;

        [Header("모닥불 전용")]
        public bool isLit;
        public float remainingFuelSeconds;

        [Header("물 증류기 전용")]
        public float storedWater;

        // ── 쉼터 전용 (정착 배치 1) ───────────────────────────────────────────────────────
        // **추가만 했다.** 기존 필드는 하나도 제거·개명하지 않았다 - JsonUtility는 JSON에 없는 필드를
        // 기본값으로 채우므로 필드 추가는 옛 세이브와 호환되지만(level 0 / slotMask 0으로 읽힌다),
        // 제거·개명은 그 필드를 통째로 잃는 파괴적 변경이다(AGENT_BRIEF 3장).

        [Header("쉼터 전용")]
        [Tooltip("쉼터 단계(1=쉼터 / 2=오두막 / 3=집). 이 필드가 없던 옛 세이브는 0으로 읽히며," +
            " Shelter.ApplySavedState가 0을 Lv1로 해석한다.")]
        public int level;

        [Tooltip("설치된 슬롯 비트마스크. bit0=문 / bit1=침상 / bit2=저장궤 (Shelter.SlotDoor 등 상수 참고).")]
        public int slotMask;
    }

    /// <summary>
    /// 절차적으로 생성되는 자원 노드/위험 요소/사냥감을 다시 가리키기 위한 안정적인 키.
    /// (islandIndex, spawnOrder)는 스포너가 섬별 결정적 System.Random으로 생성 순서대로 부여한 값으로,
    /// 같은 worldSeed로 WorldMapManager.RegenerateWorld를 다시 실행하면 항상 동일한 키에 동일한
    /// 노드/위험요소/사냥감이 나온다(B3-3 재현성 논증 참고, IslandResourceSpawner/HazardSpawner/
    /// CreatureSpawner의 주석 및 각 컴포넌트의 islandIndex/spawnOrder 필드 참고). 섬에 속하지 않는
    /// 개체(예: 상어)는 islandIndex가 -1일 수 있다.
    /// </summary>
    [System.Serializable]
    public class SpawnKeySaveEntry
    {
        public int islandIndex;
        public int spawnOrder;
    }

    /// <summary>
    /// 자원 노드 하나의 부분/소진 채집 상태 저장 항목. SpawnKeySaveEntry에 남은 채집 가능 횟수를 더한
    /// 형태다 - 단순 bool(소진 여부)이 아니라 remainingHarvestCount 자체를 저장해, maxHarvestCount가
    /// 3인 노드를 1번만 캔 "부분 채집" 상태도 정확히 복원할 수 있게 했다.
    /// </summary>
    [System.Serializable]
    public class ResourceNodeSaveEntry
    {
        public int islandIndex;
        public int spawnOrder;
        public int remainingHarvestCount;
    }
}
