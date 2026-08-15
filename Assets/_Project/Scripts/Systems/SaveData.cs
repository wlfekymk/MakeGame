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
    }
}
