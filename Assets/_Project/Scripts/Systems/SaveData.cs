using System.Collections.Generic;
using UnityEngine;
using MakeGame.Data;

namespace MakeGame.Systems
{
    /// <summary>
    /// 저장 파일 하나에 담기는 전체 게임 상태.
    /// JsonUtility로 직렬화되므로 모든 필드가 값 타입이거나 [System.Serializable] 클래스여야 한다.
    /// 자원 노드 채집 상태, 위험 요소 처치/재등장 타이머, 플레이어가 설치한 구조물(물 증류기/쉼터)은
    /// 1차 구현 범위에서 의도적으로 제외한다 (섬/자원/위험요소 배치는 worldSeed로 다시 생성됨).
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
}
