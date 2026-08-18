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
    /// discoveredIslandIds/skills/inventory 등 기존 리스트 필드들도 이미 동일한 방식에 의존한다.
    /// [세이브 키 v2] 세이브 키 스키마 버전 필드(saveKeyVersion, 파일 맨 끝)가 하나 있다 - 절차 생성
    /// 개체(자원/위험요소/사냥감)의 키가 러닝 카운터에서 안정 해시(StableSpawnKey)로 바뀌면서 옛 목록을
    /// 구분하기 위해서다. 그 외 필드는 여전히 "추가만, 맨 끝에" 관례로 하위호환을 지킨다.
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

        // ── 배 제작 진행 (제거됨) ──────────────────────────────────────────────────
        // 3단계 도면-작업대 배 시스템(BoatConstructionSystem)이 통째로 삭제되면서 여기 있던 다섯 필드
        // (boatCurrentStage / boatHasBlueprint / boatHighestCompletedStage / boatIsFullyComplete /
        // boatCollectedMaterials)도 함께 없앴다. **옛 세이브를 여는 데는 아무 문제가 없다** -
        // JsonUtility.FromJson은 대상 타입에 없는 JSON 키를 조용히 버리므로, 옛 파일의 boat* 값은
        // 읽히지 않고 사라질 뿐 예외도 경고도 나지 않는다. 새 뗏목 상태는 파일 맨 끝에 추가했다
        // (아래 raftBaseTileCount/raftInstalledParts - "추가만, 맨 끝에" 관례).

        [Header("경비행기 수리 진행")]
        public bool aircraftRepairComplete;

        // [B25] 건축 시스템(바닥/벽/문/창문)이 놓은 조각 전체. BuildingSystem이 스스로 JsonUtility로
        // 직렬화한 문자열을 그대로 담는다. **추가만 했다** - JsonUtility는 JSON에 없는 필드를 건드리지
        // 않으므로 옛 세이브를 읽으면 ""로 남고, RestoreFromJson은 ""/null에서 아무것도 하지 않는다.
        public string buildStructureJson = "";
        public List<ItemCountEntry> aircraftCollectedMaterials = new List<ItemCountEntry>();

        [Header("설치 구조물 (B2-15 1단계)")]
        [Tooltip("플레이어가 설치한 모닥불/쉼터/물 증류기/제작대·용광로·베틀 각각의 위치·회전·상태 목록.")]
        public List<StructureSaveEntry> structures = new List<StructureSaveEntry>();

        [Header("자원 노드 채집 상태 (B3-4)")]
        [Tooltip("완전한 상태(remainingHarvestCount == maxHarvestCount)가 아닌 자원 노드만 기록한다.\n" +
            "특대 섬 하나에만 최대 수백 개의 노드가 나올 수 있어(섬 8개면 수천 개), 대부분을 차지하는" +
            " '아직 하나도 안 캔 노드'까지 전부 저장하면 세이브 파일이 불필요하게 커진다. 목록에 없는" +
            " (islandIndex, stableKey)는 '온전한 상태'로 간주한다(RestoreResourceNodes 참고).")]
        public List<ResourceNodeSaveEntry> partialResourceNodes = new List<ResourceNodeSaveEntry>();

        [Header("위험요소 처치 상태 (B3-5)")]
        [Tooltip("현재 처치되어(isDefeated) 재등장 대기 중인 위험 요소의 (islandIndex, stableKey) 키 목록.\n" +
            "모든 위험 요소 종류는 시간이 지나면 자동으로 재등장하므로(HazardSource.Update, 영구 제거되는" +
            " 종류 없음) '처치됨' 여부만 기록하면 충분하다. 재등장까지 남은 시간은 저장하지 않고 불러올 때" +
            " respawnTimer를 0으로 되돌린다 - 실시간(벽시계) 경과는 반영하지 않는다는 뜻이다. 자세한 근거는" +
            " SaveLoadController.RestoreHazardsAndCreatures 주석 참고.")]
        public List<SpawnKeySaveEntry> defeatedHazards = new List<SpawnKeySaveEntry>();

        [Header("사냥감 포획 상태 (B3-5)")]
        [Tooltip("현재 잡혀서(isCaught) 재등장 대기 중인 사냥감/물고기의 (islandIndex, stableKey) 키 목록.\n" +
            "위험 요소와 동일하게 '잡힘' 여부만 기록하고 재등장까지 남은 시간은 저장하지 않는다.")]
        public List<SpawnKeySaveEntry> caughtCreatures = new List<SpawnKeySaveEntry>();

        // ── 보관 상자 (배치 39) ────────────────────────────────────────────────────
        // **맨 끝에 추가만 했다.** 기존 필드는 하나도 지우거나 이름을 바꾸지 않았다 - JsonUtility는
        // JSON에 없는 필드를 손대지 않으므로, 이 필드가 없던 옛 세이브를 불러오면 초기화 구문
        // (= new List<>())대로 빈 목록이 되어 경고 하나 없이 열린다(structures를 처음 넣을 때와 같은 관례).
        //
        // 상자는 건축 조각(buildStructureJson)이 아니라 여기에 담긴다. 조각 목록은 "격자 위 형상"만
        // 다루는데 상자는 등급과 내용물을 함께 실어야 하고, 두 곳에 다 쓰면 불러올 때 상자가 둘이 된다.
        // 격자 자리(공간/셀/층)와 회전은 그대로 저장하므로 조각과 같은 격자 위에 그대로 되살아난다.

        [Header("보관 상자 (배치 39)")]
        [Tooltip("설치된 보관 상자 각각의 격자 자리·회전·등급·내용물. 옛 세이브에는 이 필드가 없어" +
            " 빈 목록으로 읽히며, 그 경우 BuildingSystem.RestoreChests는 아무것도 하지 않는다.")]
        public List<ChestSaveEntry> storageChests = new List<ChestSaveEntry>();

        // ── 세이브 키 스키마 버전 ─────────────────────────────────────────────────────
        // **맨 끝에 추가만 했다**(JsonUtility 규칙). 이 필드가 없는 옛 세이브는 0으로 읽힌다.
        //
        // v2(현재): 자원 노드/위험 요소/사냥감의 세이브 키가 (islandIndex, spawnOrder=생성 순서
        // 러닝 카운터)에서 (islandIndex, stableKey=결정론적 안정 해시)로 바뀌었다(StableSpawnKey 참고).
        // v1(0으로 읽히는 세이브 포함) 파일의 partialResourceNodes/defeatedHazards/caughtCreatures는
        // 새 키와 대조할 수 없으므로 로드 시 **그 세 목록만** 버린다(경고 로그 1줄). 인벤토리·건축·
        // 상자·퀘스트·구조물 등 나머지는 전부 그대로 복원된다 - 월드 자체는 worldSeed로 재생성되는
        // 구조라 세 목록을 버려도 "채집/처치/포획 진행이 온전한 상태로 리셋"될 뿐 깨지는 것이 없다.
        // 마이그레이션은 하지 않는다(사용자가 옛 세이브 포기를 허락했다 - v1 spawnOrder를 새 키로
        // 환산하려면 당시 스폰 구성을 완벽 재현해야 해서 그 자체가 새 세금이 된다).
        [Tooltip("세이브 키 스키마 버전. 0(필드 없음)/1 = 옛 러닝 카운터 키, 2 = 안정 해시 키." +
            " 로드 시 2 미만이면 채집/처치/포획 목록만 버리고 나머지는 그대로 복원한다.")]
        public int saveKeyVersion;

        /// <summary>현재 세이브 키 스키마 버전. Save()가 기록하고 Load()가 비교한다.</summary>
        public const int CurrentSaveKeyVersion = 2;

        // ── 뗏목 (해안 건조) ────────────────────────────────────────────────────────
        // **맨 끝에 추가만 했다**(JsonUtility 관례). 이 필드가 없는 옛 세이브는 전부 0으로 읽히고,
        // 그것이 정확히 "아직 뗏목을 한 칸도 안 깔았다"는 뜻이라 별도 마이그레이션이 필요 없다.

        [Header("뗏목 (해안 건조)")]
        [Tooltip("해안에 깐 바닥판 칸 수(0 ~ RaftStructure.MaxBaseTiles). 복원 시 범위 밖 값은 잘린다.")]
        public int raftBaseTileCount;

        [Tooltip("장착된 뗏목 부품 비트 플래그((int)RaftPart). 돛=1 · 키=2 · 닻=4 · 노=8 · 모터=16.\n" +
            "enum이 아니라 int로 저장하는 이유: JsonUtility는 enum을 정수로 쓰긴 하지만, 나중에 열거자" +
            " 이름이 바뀌어도 파일 형식이 흔들리지 않도록 저장 계층에서는 정수로 고정한다.")]
        public int raftInstalledParts;

        // ── 콘텐츠 스키마 버전 ───────────────────────────────────────────────────────
        // saveKeyVersion과 **일부러 분리했다.** 그쪽은 "절차 생성 개체의 대조 키" 전용이고, 값이
        // 올라가면 로드 시 채집/처치/포획 목록을 통째로 버린다. 배→뗏목 전환은 그 목록들과 아무 관계가
        // 없으므로, 거기에 얹어서 올리면 멀쩡한 진행을 이유 없이 날리게 된다.
        [Tooltip("게임 콘텐츠 스키마 버전. 0(필드 없음) = 3단계 도면-작업대 배 시스템 시절의 세이브," +
            " 1 = 해안 뗏목 시스템(칸 수만 기록), 2 = 칸별 구성(종류·바닥재)까지 기록.\n" +
            "로드 시 0이면 배 진행이 사라졌다는 안내 로그를, 1이면 칸별 구성이 없어 통나무 바닥판으로" +
            " 승격했다는 안내 로그를 남긴다. 어느 경우든 그 외 모든 데이터는 그대로 복원한다.")]
        public int saveContentVersion;

        /// <summary>현재 콘텐츠 스키마 버전. v2 = 뗏목 바닥판 칸별 구성(raftBaseTiles).</summary>
        public const int CurrentSaveContentVersion = 2;

        /// <summary>3단계 도면-작업대 배 시스템이 사라진 버전(뗏목 도입). 이 미만이면 배 진행이 없어진다.</summary>
        public const int RaftContentVersion = 1;

        /// <summary>바닥판 칸별 구성이 들어간 버전. 이 미만이면 raftBaseTiles가 비어 있다.</summary>
        public const int RaftTileDetailContentVersion = 2;

        // ── 뗏목 바닥판 칸별 구성 (콘텐츠 v2) ──────────────────────────────────────────
        // **맨 끝에 추가만 했다**(JsonUtility 관례). 이 필드가 없는 v1 세이브는 빈 목록으로 읽히고,
        // RaftStructure.ApplySavedState가 그 경우를 "통나무 바닥판 + 갑판 바닥재 n칸"으로 승격한다
        // (승격 규칙은 그 메서드 한 곳에만 있다 - 로드 경로마다 다르게 채우면 갑판이 갈라진다).
        //
        // 왜 raftBaseTileCount 하나로는 부족한가: 이제 칸마다 **종류**(통나무/부력통/드럼통)와
        // **갑판 바닥재 유무**가 따로 있고, 종류는 부력(항해 성능)까지 정한다. 칸 수만 저장하면
        // 드럼통 8칸으로 만든 뗏목이 불러오기 한 번에 통나무 8칸이 된다.

        [Header("뗏목 바닥판 칸별 구성 (콘텐츠 v2)")]
        [Tooltip("격자 순번대로 한 칸에 정수 하나. 하위 3비트 = RaftBaseTileKind(1=통나무 · 2=부력통 ·" +
            " 3=드럼통), 8비트(값 8) = 갑판 바닥재가 깔림.\n" +
            "예: 9 = 통나무 + 바닥재, 3 = 드럼통(바닥재 없음). 목록 길이는 raftBaseTileCount와 같다.\n" +
            "문자열로 압축하지 않고 List<int>로 두는 이유: 칸이 최대 8개뿐이라 압축 이득이 없고," +
            " JsonUtility가 List<int>를 그대로 직렬화하므로 파싱 코드(=버그 자리)가 아예 필요 없다.")]
        public List<int> raftBaseTiles = new List<int>();
    }

    /// <summary>
    /// 보관 상자 하나의 저장 항목. 좌표는 그 상자가 속한 **공간의 로컬 값**이다
    /// (Ground면 월드 좌표, Deck이면 뗏목 로컬 좌표 - 건축 조각의 BuildPieceSaveEntry와 같은 규약).
    /// </summary>
    [System.Serializable]
    public class ChestSaveEntry
    {
        [Tooltip("BuildSpace (0=지면 / 1=뗏목 갑판)")]
        public int space;

        [Header("격자 자리")]
        public int cellX;
        public int cellZ;

        [Tooltip("상자가 딛고 선 바닥의 층 번호")]
        public int level;

        [Header("좌표/회전")]
        public float posX;
        public float posY;
        public float posZ;
        public float yaw;

        [Tooltip("등급(0=소형 50칸 / 1=중형 100 / 2=대형 150 / 3=특대 200). 범위를 벗어난 값은 복원 시 잘린다.")]
        public int tier;

        [Tooltip("상자에 든 아이템(이름 + 개수 + 남은 사용 횟수).")]
        public List<ChestItemSaveEntry> items = new List<ChestItemSaveEntry>();
    }

    /// <summary>
    /// 상자에 든 아이템 한 줄. InventorySaveEntry와 같은 규약이다 - ItemData는 직렬화할 수 없으므로
    /// 이름으로 적어 두고 불러올 때 ItemDataRegistry로 되찾는다. 남은 사용 횟수가 다른 도구는
    /// 한 줄로 접지 않으므로(BuildingSystem.AppendChestItems) 내구도가 뭉개지지 않는다.
    /// </summary>
    [System.Serializable]
    public class ChestItemSaveEntry
    {
        public string itemName;

        [Tooltip("이 줄이 나타내는 개수. 0 이하이면 1개로 해석한다(InventorySaveEntry와 같은 안전장치).")]
        public int count = 1;

        public int remainingUses;

        /// <summary>실제 개수. 0 이하는 1로 해석한다.</summary>
        public int Count => count > 0 ? count : 1;
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

        // ── 스택 (정착 배치 3) ───────────────────────────────────────────────────────────
        // **추가만 했다.** itemName/remainingUses는 이름·타입·의미 전부 그대로다.
        //
        // 옛 세이브 호환이 이 필드의 전부다: 예전에는 야자잎 42개가 항목 42줄로 나열됐고 그 줄들에는
        // count 키가 없다. JsonUtility는 JSON에 없는 필드를 건드리지 않으므로 그 줄들의 count는 0으로
        // 읽힌다 - 그래서 **필드를 직접 읽지 말고 반드시 Count 프로퍼티를 거쳐야 한다**(0을 1로 해석).
        // 이 폴백이 없으면 옛 세이브의 아이템이 전부 0개가 되어 통째로 사라진다.
        // 필드 초기값(= 1)에도 같은 안전장치를 걸어 뒀지만, JsonUtility가 역직렬화 시 생성자를 거치는지에
        // 의존하지 않기 위해 판정은 프로퍼티 쪽에 둔다.

        [Tooltip("이 항목이 나타내는 개수(스택). 0 이하이면 옛 세이브 형식으로 보고 1개로 해석한다.")]
        public int count = 1;

        /// <summary>실제 개수. 0 이하(= count 키가 없던 옛 세이브)는 1로 해석한다.</summary>
        public int Count => count > 0 ? count : 1;
    }

    /// <summary>아이템 이름 + 개수 쌍 (경비행기 수리 투입 재료·저장궤 내용물 등 수량 기반 저장에 사용).</summary>
    [System.Serializable]
    public class ItemCountEntry
    {
        public string itemName;
        public int count;
    }

    /// <summary>
    /// 플레이어가 설치한 구조물(모닥불/쉼터/물 증류기/제작대·용광로·베틀) 하나의 저장 항목.
    ///
    /// [제작 시설 3종 - 필드를 하나도 늘리지 않았다] 제작대/용광로/베틀은 위치·회전 외에 저장할 상태가
    /// 없다(연료도, 내용물도, 진행도도 없다). 그래서 StructureType에 값 셋을 **맨 끝에** 추가하는 것으로
    /// 끝났고, 이 클래스의 필드 목록은 예전과 완전히 동일하다 - 즉 세이브 파일의 JSON 스키마 자체가
    /// 바뀌지 않아, 새 세이브를 옛 빌드에서 열어도(그럴 일은 없지만) 깨지는 필드가 없다.
    /// 옛 세이브는 그저 type이 3~5인 항목이 없을 뿐이라 마이그레이션도, 버전 상승도 필요 없다
    /// (saveContentVersion을 올리지 않은 이유 - 그 값은 "복원 규칙이 달라졌다"는 신호이고, 여기서는
    /// 기존 항목의 복원 규칙이 한 줄도 달라지지 않았다. storageChests를 처음 넣을 때와 같은 판단이다).
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

        // ── 저장궤 (정착 배치 2) ──────────────────────────────────────────────────────────
        // 여기도 **추가만 했다.** 리스트 필드는 초기화 구문(= new List<>())이 있으므로 이 필드가 없던
        // 옛 세이브를 읽어도 빈 리스트가 되어 NRE 없이 안전하다 - structures 필드를 처음 추가할 때와
        // 완전히 같은 관례다(이 파일 맨 위 하위호환 설명 참고). 저장궤가 없는 쉼터/모닥불/증류기
        // 항목에서는 세 필드 모두 비어 있는 채로 무시된다.
        //
        // 마지막 정산 시각(Shelter.lastSettleSeconds)은 **일부러 저장하지 않는다.** 불러오면 시계도
        // 저장 시점으로 함께 되돌아가므로, 복원 후 첫 정산에서 현재 시계로 기준점을 다시 잡으면
        // 경과가 정확히 0이 된다. 저장했다가 어긋나면 없던 시간이 통째로 익혀지는 쪽이 더 위험하다.

        [Header("쉼터 저장궤 전용 (정착 배치 2)")]
        [Tooltip("저장궤에 보관 중인 재료(연료/물통/생식품)의 이름+개수 목록.")]
        public List<ItemCountEntry> chestItems = new List<ItemCountEntry>();

        [Tooltip("저장궤가 밤사이 모아 둔, 아직 수거하지 않은 결과물(생수/익힌 음식)의 이름+개수 목록.")]
        public List<ItemCountEntry> chestYield = new List<ItemCountEntry>();

        [Tooltip("생식품 훈연 진행도(게임 내 초). dryingSecondsPerItem에 도달할 때마다 1개가 익는다.")]
        public float chestDryingProgress;
    }

    /// <summary>
    /// 절차적으로 생성되는 자원 노드/위험 요소/사냥감을 다시 가리키기 위한 안정적인 키.
    ///
    /// [세이브 키 v2] 실제 대조 키는 (islandIndex, stableKey)다. stableKey는
    /// StableSpawnKey.Compute(섬 번호, 종류 이름/HazardType, 그 종류 안에서의 생성 순번)의 순수 해시라,
    /// 다른 종류의 개수·순서가 바뀌어도(엔트리 추가/증량) 이 종류의 키는 밀리지 않는다 -
    /// v1 키였던 spawnOrder(섬 전체 러닝 카운터)가 콘텐츠 변경마다 "rng 소비 순서를 비트 단위로
    /// 보존해야 한다"는 세금을 강요하던 문제를 없앴다.
    /// spawnOrder 필드는 디버깅 참고용으로 계속 기록하지만 복원 대조에는 더 이상 쓰지 않는다
    /// (필드 제거는 파괴적 변경이라 하지 않는다 - 이 파일 상단 하위호환 관례 참고).
    /// 섬에 속하지 않는 개체(예: 상어)는 islandIndex가 -1일 수 있다.
    /// </summary>
    [System.Serializable]
    public class SpawnKeySaveEntry
    {
        public int islandIndex;
        public int spawnOrder;

        // [세이브 키 v2] **맨 끝에 추가만 했다.** 이 필드가 없는 옛(v1) 세이브는 0으로 읽히지만,
        // 어차피 로드 시 saveKeyVersion 검사에서 v1의 채집/처치/포획 목록은 통째로 버려지므로
        // 0 값이 대조에 쓰이는 일은 없다(SaveLoadController.Load 참고).
        [Tooltip("결정론적 안정 키. StableSpawnKey.Compute(islandIndex, 종류, 종류 내 순번)의 해시값.")]
        public int stableKey;
    }

    /// <summary>
    /// 자원 노드 하나의 부분/소진 채집 상태 저장 항목. SpawnKeySaveEntry에 남은 채집 가능 횟수를 더한
    /// 형태다 - 단순 bool(소진 여부)이 아니라 remainingHarvestCount 자체를 저장해, maxHarvestCount가
    /// 3인 노드를 1번만 캔 "부분 채집" 상태도 정확히 복원할 수 있게 했다.
    /// [세이브 키 v2] 대조 키는 (islandIndex, stableKey)다 - SpawnKeySaveEntry 주석 참고.
    /// </summary>
    [System.Serializable]
    public class ResourceNodeSaveEntry
    {
        public int islandIndex;
        public int spawnOrder;
        public int remainingHarvestCount;

        // [세이브 키 v2] 맨 끝에 추가만 했다(위 SpawnKeySaveEntry.stableKey와 같은 규칙).
        [Tooltip("결정론적 안정 키. StableSpawnKey.Compute(islandIndex, 아이템 이름, 종류 내 순번)의 해시값.")]
        public int stableKey;
    }

    /// <summary>
    /// [세이브 키 v2] 절차 생성 개체(자원 노드/위험 요소/사냥감)의 결정론적 안정 키 계산기.
    ///
    /// 키 공식: Hash(islandIndex, 종류 식별자, perTypeIndex)
    ///  - 종류 식별자: 자원/사냥감은 아이템 이름(문자열 → FNV-1a), 위험 요소는 (int)HazardType.
    ///  - perTypeIndex: **같은 종류 안에서** 몇 번째로 생성됐는지(0부터). 같은 종류 안에서만 세므로
    ///    다른 종류의 개수가 바뀌어도(엔트리 추가/증량) 이 종류의 키는 밀리지 않는다 - 이것이
    ///    v1 키(섬 전체 러닝 카운터 spawnOrder)와의 유일하고 결정적인 차이다.
    ///
    /// 순수 함수다: System.Random/UnityEngine.Random을 일절 쓰지 않고(월드 생성 rng 소비량 0),
    /// 같은 입력이면 언제나 같은 출력이다. 해시 구조는 이 프로젝트에 이미 있는
    /// HazardSpawner.IsBearCubIndividual(소수 곱 → xorshift-곱 finalizer)을 그대로 따랐다 -
    /// 입력 세 값이 전부 작은 정수라 단순 덧셈으로는 상관이 남기 때문에 섞는다.
    ///
    /// 충돌: int 해시라 이론상 충돌이 가능하다. (islandIndex, stableKey) 쌍으로 저장/대조하므로
    /// 실질 확률은 무시 가능한 수준이지만, 로드 시 같은 쌍이 2개 발견되면 SaveLoadController가
    /// 경고 로그를 남긴다(먼저 발견된 개체를 유지).
    /// </summary>
    public static class StableSpawnKey
    {
        /// <summary>위험 요소용: 종류 식별자가 정수((int)HazardType)인 형태.</summary>
        public static int Compute(int islandIndex, int typeId, int perTypeIndex)
        {
            unchecked
            {
                uint h = (uint)(islandIndex * 73856093) ^ (uint)(typeId * 19349663)
                    ^ (uint)(perTypeIndex * 83492791) ^ 0x9E3779B9u;
                h ^= h >> 16;
                h *= 0x7FEB352Du;
                h ^= h >> 15;
                h *= 0x846CA68Bu;
                h ^= h >> 16;
                return (int)h;
            }
        }

        /// <summary>자원 노드/사냥감용: 종류 식별자가 이름(아이템 이름 등 문자열)인 형태.</summary>
        public static int Compute(int islandIndex, string typeName, int perTypeIndex)
        {
            return Compute(islandIndex, HashName(typeName), perTypeIndex);
        }

        /// <summary>
        /// 문자열 → 결정적 int 해시(FNV-1a). string.GetHashCode()를 쓰지 않는 이유: .NET 런타임/버전에
        /// 따라 값이 달라질 수 있어(해시 무작위화) 세이브 파일에 남는 키로는 부적합하다. FNV-1a는
        /// 문자 값만으로 계산하는 순수 함수라 빌드/플랫폼이 바뀌어도 같은 이름이면 같은 값이다.
        /// </summary>
        public static int HashName(string name)
        {
            unchecked
            {
                uint h = 2166136261u;
                if (!string.IsNullOrEmpty(name))
                {
                    for (int i = 0; i < name.Length; i++)
                    {
                        h ^= name[i];
                        h *= 16777619u;
                    }
                }
                return (int)h;
            }
        }
    }
}
