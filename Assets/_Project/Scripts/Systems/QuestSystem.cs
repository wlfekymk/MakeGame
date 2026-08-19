using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using MakeGame.Data;
using MakeGame.Player;

namespace MakeGame.Systems
{
    /// <summary>
    /// 퀘스트("할 일")가 속한 묶음. 표시 순서는 이 값의 순서를 그대로 따른다.
    /// **항해가 맨 위에 오지 않는다** - 감독 방향은 탈출이 아니라 생존·정착이다(AGENT_BRIEF 현재 방향).
    /// </summary>
    public enum QuestCategory
    {
        /// <summary>생존 - 물·음식·체온. 한 번 끝나는 목표가 아니라 **지금 안전한가**를 보는 유지 목표다.</summary>
        Survival = 0,

        /// <summary>정착 - 쉼터/집 Lv1→Lv3, 모닥불, 물 증류기. 한 번 달성하면 되돌아가지 않는 마일스톤.</summary>
        Settlement = 1,

        /// <summary>항해 - 뗏목 건조 단계와 경비행기 수리. 맨 아래에 둔다.</summary>
        Voyage = 2,
    }

    /// <summary>
    /// 퀘스트 한 줄의 표시용 상태. QuestSystem이 소유하고 재사용하는 객체이므로
    /// **들고 있지 마라** - 갱신 때마다 같은 인스턴스의 내용이 바뀐다(InventoryStack과 같은 규칙).
    /// </summary>
    public class QuestEntry
    {
        /// <summary>묶음(생존/정착/항해).</summary>
        public QuestCategory category;

        /// <summary>변화 감지·완료 래치에 쓰는 고정 키. 화면에 나오지 않는다.</summary>
        public string id;

        /// <summary>퀘스트 제목 한 줄.</summary>
        public string title;

        /// <summary>진행도 문구. 예: "나뭇가지 4/6 · 노끈 1/3".</summary>
        public string progress;

        /// <summary>0~1 진행률(막대용).</summary>
        public float fraction;

        /// <summary>완료되었는가(체크 + 흐리게).</summary>
        public bool completed;

        /// <summary>아직 앞 단계가 안 끝나 손댈 수 없는 상태인가(더 흐리게, 강조하지 않는다).</summary>
        public bool locked;
    }

    /// <summary>
    /// "지금 무엇이 할 일인가"를 판정하는 시스템. **표시는 하지 않는다**(그것은 UI/QuestUI.cs의 몫).
    ///
    /// 설계 규칙:
    /// 1. **다른 시스템에 새 필드를 추가하지 않는다.** 판정은 전부 이미 존재하는 public 시그니처만
    ///    읽어서 만든다(Shelter.ActiveShelters/level/levelNRequirements/DescribeNextBuildAction,
    ///    Campfire.Active/isLit, WaterStill.Active, RaftStructure의 뗏목 상태 API,
    ///    AircraftRepairSystem.GetOverallProgress, ProgressionTracker.CountByName).
    /// 2. **씬에 인스턴스가 없다.** SurvivalHudUI와 동일하게 RuntimeInitializeOnLoadMethod +
    ///    sceneLoaded로 씬이 로드될 때마다 새로 생성된다. 코드 기본값이 유일한 진실이다.
    /// 3. **폴링은 unscaledDeltaTime.** Time.timeScale = 0인 화면(설정/타이틀/엔딩/게임오버)에서도
    ///    멈추지 않아야 한다. InvokeRepeating은 쓰지 않는다(timeScale 0에서 죽는다 - 볼륨 슬라이더 전례).
    /// 4. **문자열은 값이 바뀔 때만 만든다.** 매 갱신마다 조립하되, 실제로 달라진 항목이 하나도 없으면
    ///    Changed 이벤트를 쏘지 않아 UI가 라벨을 다시 대입하지 않는다.
    ///
    /// 완료 래치: 정착·항해 마일스톤은 한 번 달성하면 그 세션 동안 완료로 남는다(재료를 써버려서
    /// 조건식이 다시 false가 되어도 체크가 풀리지 않는다). 생존은 래치하지 않는다 - 물·음식·체온은
    /// 한 번 끝나는 목표가 아니라 "지금 안전한가"이기 때문이다.
    /// </summary>
    public class QuestSystem : MonoBehaviour
    {
        /// <summary>이 씬의 판정 시스템(씬 리로드마다 새 인스턴스로 교체된다).</summary>
        public static QuestSystem Instance { get; private set; }

        /// <summary>판정 주기(초). 인벤토리 전수 순회가 섞여 있어 매 프레임 돌릴 이유가 없다.</summary>
        public const float RefreshInterval = 0.5f;

        /// <summary>
        /// 생존 항목이 "지금 안전하다"로 판정되는 비율. SurvivalStats의 위험 임계값
        /// (LowThirstRatio 0.2 등)은 이미 빨간 경고가 뜨는 지점이라 퀘스트로 삼기엔 너무 늦다.
        /// 절반을 기준으로 두면 HUD 막대가 아직 평상시 색일 때 "슬슬 채워라"가 먼저 뜬다.
        /// </summary>
        public const float SurvivalSafeRatio = 0.5f;

        /// <summary>생존 묶음에서 "확보했다"고 볼 소지품 개수(물/음식 각각).</summary>
        public const int SurvivalStockTarget = 2;

        // 아이템 이름은 이 프로젝트의 기존 관례대로 한국어 itemName을 그대로 쓴다
        // (ProgressionTracker.HatchetItemName / Shelter.level2Requirements 등과 같은 방식).
        private const string ShelterKitName = "쉼터키트";
        private const string CampfireKitName = "모닥불키트";
        private const string WaterStillKitName = "물증류기키트";

        private PlayerInventory playerInventory;
        private SurvivalStats survivalStats;
        private RaftStructure raft;
        private AircraftRepairSystem aircraftRepair;

        private readonly List<QuestEntry> quests = new List<QuestEntry>();

        /// <summary>완료 래치. id가 여기 들어가면 그 세션 동안 계속 완료로 표시된다.</summary>
        private readonly HashSet<string> latchedComplete = new HashSet<string>();

        private float refreshTimer = 0f;
        private int writeIndex = 0;
        private bool dirty = false;

        /// <summary>표시용 퀘스트 목록(읽기 전용). 순서는 생존 → 정착 → 항해로 고정이다.</summary>
        public IReadOnlyList<QuestEntry> Quests => quests;

        /// <summary>
        /// HUD 한 줄에 쓸 "현재 할 일". 완료되지 않고 잠기지도 않은 첫 퀘스트의 **제목**이다
        /// (진행도 문구는 붙이지 않는다 - HUD는 한 줄만 남긴다).
        /// </summary>
        public string CurrentObjective { get; private set; } = "";

        /// <summary>퀘스트 목록 중 실제로 표시가 달라진 갱신에서만 발행된다.</summary>
        public event System.Action Changed;

        /// <summary>완료 개수(창 제목 옆 "3/10" 표기용).</summary>
        public int CompletedCount { get; private set; }

        /// <summary>
        /// 씬이 로드될 때마다 새 QuestSystem을 만든다(SurvivalHudUI와 완전히 같은 패턴).
        /// 씬 파일을 편집할 수 없으므로 자기 완결 부트스트랩이 유일한 경로다.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Bootstrap()
        {
            SceneManager.sceneLoaded += (scene, mode) =>
            {
                var go = new GameObject("QuestSystem");
                go.AddComponent<QuestSystem>();
            };
        }

        private void Start()
        {
            Instance = this;

            playerInventory = FindAnyObjectByType<PlayerInventory>();
            survivalStats = FindAnyObjectByType<SurvivalStats>();
            raft = RaftStructure.Active;
            aircraftRepair = FindAnyObjectByType<AircraftRepairSystem>();

            // 첫 프레임부터 목록이 채워져 있어야 창을 바로 열어도 비어 있지 않다.
            Rebuild();
        }

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        private void Update()
        {
            refreshTimer -= Time.unscaledDeltaTime;
            if (refreshTimer > 0f)
                return;

            refreshTimer = RefreshInterval;
            Rebuild();
        }

        /// <summary>
        /// 창을 여는 순간처럼 "지금 당장 최신값이 필요한" 곳에서 부른다. 폴링 대기를 취소하고
        /// 곧바로 한 번 다시 판정한다(0.5초 늦게 갱신되는 첫 화면을 없앤다).
        /// </summary>
        public void RefreshNow()
        {
            refreshTimer = RefreshInterval;
            Rebuild();
        }

        // ────────────────────────────────────────────────────────────────────────
        // 판정
        // ────────────────────────────────────────────────────────────────────────

        private void Rebuild()
        {
            // 참조가 씬 로드 순서 때문에 첫 Start에서 비어 있었을 수 있다(런타임 생성 컴포넌트와
            // 씬 컴포넌트의 실행 순서는 지정돼 있지 않다 - AGENT_BRIEF 4장). 비어 있을 때만 다시 찾는다.
            if (playerInventory == null)
                playerInventory = FindAnyObjectByType<PlayerInventory>();
            if (survivalStats == null)
                survivalStats = FindAnyObjectByType<SurvivalStats>();
            if (raft == null)
                raft = RaftStructure.Active;
            if (aircraftRepair == null)
                aircraftRepair = FindAnyObjectByType<AircraftRepairSystem>();

            writeIndex = 0;
            dirty = false;

            BuildSurvivalQuests();
            BuildSettlementQuests();
            BuildVoyageQuests();

            // 이번 판정에서 쓰이지 않은 잉여 항목은 없다(항목 수가 고정이다). 혹시 줄어들면 잘라낸다.
            if (writeIndex < quests.Count)
            {
                quests.RemoveRange(writeIndex, quests.Count - writeIndex);
                dirty = true;
            }

            int completed = 0;
            string objective = null;
            for (int i = 0; i < quests.Count; i++)
            {
                var quest = quests[i];
                if (quest.completed)
                {
                    completed++;
                    continue;
                }

                // 잠긴 항목(앞 단계 미완료)은 "지금 할 일"이 될 수 없다.
                // **제목만 쓴다.** HUD에 남기는 것은 한 줄뿐이고(감독 지시), 좌상단 HUD 패널의 글자
                // 폭은 256px(패널 280 - 좌우 여백 24)이라 진행도 문구까지 붙이면 세 줄로 접혀
                // 아래 체력 막대를 덮는다. 진행도는 퀘스트 창(J)이 보여준다.
                if (objective == null && !quest.locked)
                    objective = quest.title;
            }

            if (completed != CompletedCount)
            {
                CompletedCount = completed;
                dirty = true;
            }

            objective = objective ?? "모든 할 일을 끝냈다 — 섬에서 지내보자";
            if (objective != CurrentObjective)
            {
                CurrentObjective = objective;
                dirty = true;
            }

            if (dirty)
                Changed?.Invoke();
        }

        // ── 생존 ────────────────────────────────────────────────────────────────

        /// <summary>
        /// 물·음식·체온. 래치하지 않는다 - 이 셋은 달성이 아니라 유지다. "완료(체크+흐림)"는
        /// **지금 안전하다**는 뜻이고, 값이 떨어지면 다시 강조 상태로 돌아온다.
        /// </summary>
        private void BuildSurvivalQuests()
        {
            int drinkCount = CountConsumable(wantDrink: true);
            int foodCount = CountConsumable(wantDrink: false);

            float thirstRatio = survivalStats != null
                ? Mathf.Clamp01(survivalStats.thirst / SurvivalStats.MaxStatValue)
                : 0f;
            float hungerRatio = survivalStats != null
                ? Mathf.Clamp01(survivalStats.hunger / SurvivalStats.MaxStatValue)
                : 0f;
            float sunstrokeRatio = survivalStats != null
                ? Mathf.Clamp01(survivalStats.sunstroke / SurvivalStats.MaxStatValue)
                : 0f;

            // 갈증이 허기보다 1.6배 빨리 준다(씬 실측 0.08 vs 0.05). 물을 위에 둔다.
            Write(QuestCategory.Survival, "quest.water", "마실 물을 확보한다",
                $"갈증 {Percent(thirstRatio)}% · 음료 {Mathf.Min(drinkCount, SurvivalStockTarget)}/{SurvivalStockTarget}",
                thirstRatio,
                completed: thirstRatio >= SurvivalSafeRatio && drinkCount >= SurvivalStockTarget,
                locked: false, latch: false);

            Write(QuestCategory.Survival, "quest.food", "먹을 것을 확보한다",
                $"허기 {Percent(hungerRatio)}% · 음식 {Mathf.Min(foodCount, SurvivalStockTarget)}/{SurvivalStockTarget}",
                hungerRatio,
                completed: hungerRatio >= SurvivalSafeRatio && foodCount >= SurvivalStockTarget,
                locked: false, latch: false);

            // 체온: 이 게임에 온도 수치는 없다(전수 grep 확인). 실제로 존재하는 열 압박 지표는
            // SurvivalStats.sunstroke 하나이고, 그늘(쉼터)과 모닥불이 그것을 다루는 수단이다.
            bool hasShade = Shelter.ActiveShelters.Count > 0;
            bool hasFire = HasLitCampfire();
            string heatDetail = hasShade
                ? (hasFire ? "그늘 · 모닥불 확보" : "그늘 확보")
                : (hasFire ? "모닥불 확보" : "그늘 없음");

            // 막대는 "안전할수록 길게"가 직관적이라 일사병의 여집합을 쓴다.
            Write(QuestCategory.Survival, "quest.heat", "체온을 지킨다 (그늘 · 모닥불)",
                $"일사병 {Percent(sunstrokeRatio)}% · {heatDetail}",
                1f - sunstrokeRatio,
                completed: sunstrokeRatio < SurvivalSafeRatio && (hasShade || hasFire),
                locked: false, latch: false);
        }

        /// <summary>
        /// 소지품에서 음료(갈증 회복) 또는 음식(허기 회복) 개수를 센다. 분류는 ItemData가 이미 들고 있는
        /// thirstRestoreAmount / hungerRestoreAmount로만 한다(UIBuilder.GetItemCategory와 같은 기준).
        /// 코코넛은 둘 다 0보다 커서 양쪽에 잡히는데, 실제로 물도 되고 요기도 되므로 의도된 결과다.
        /// PlayerInventory.items는 "1항목 = 1개"인 평면 리스트다(InventoryItem 주석).
        /// </summary>
        private int CountConsumable(bool wantDrink)
        {
            if (playerInventory == null || playerInventory.items == null)
                return 0;

            int count = 0;
            var items = playerInventory.items;
            for (int i = 0; i < items.Count; i++)
            {
                var item = items[i];
                if (item == null || item.data == null)
                    continue;

                float amount = wantDrink ? item.data.thirstRestoreAmount : item.data.hungerRestoreAmount;
                if (amount > 0f)
                    count++;
            }
            return count;
        }

        /// <summary>불이 붙어 있는 모닥불이 하나라도 있는가(Campfire.Active + isLit).</summary>
        private static bool HasLitCampfire()
        {
            var campfires = Campfire.Active;
            for (int i = 0; i < campfires.Count; i++)
            {
                if (campfires[i] != null && campfires[i].isLit)
                    return true;
            }
            return false;
        }

        // ── 정착 ────────────────────────────────────────────────────────────────

        /// <summary>
        /// 쉼터 → 모닥불 → 물 증류기 → 집 Lv2 → 집 Lv3. 전부 래치한다(재료를 소모해도 체크가 풀리지 않는다).
        /// </summary>
        private void BuildSettlementQuests()
        {
            Shelter shelter = FindPrimaryShelter();

            // 1) 쉼터를 세운다(Lv1). 세우기 전에는 쉼터키트 보유가 곧 진행도다.
            bool hasShelter = shelter != null;
            int shelterKit = ProgressionTracker.CountByName(playerInventory, ShelterKitName);
            Write(QuestCategory.Settlement, "quest.shelter1", "쉼터를 세운다 (집 Lv1)",
                hasShelter ? "설치 완료" : $"{ShelterKitName} {Mathf.Min(shelterKit, 1)}/1 · 제작(V) 후 설치(G)",
                hasShelter ? 1f : Mathf.Clamp01(shelterKit),
                completed: hasShelter, locked: false, latch: true);

            // 2) 모닥불. 설치만으로는 완료가 아니다 - 불이 붙어야 체온·조리가 열린다.
            bool campfirePlaced = Campfire.Active.Count > 0;
            bool campfireLit = HasLitCampfire();
            int campfireKit = ProgressionTracker.CountByName(playerInventory, CampfireKitName);
            string campfireProgress = campfireLit
                ? "불 붙음"
                : (campfirePlaced
                    ? "설치됨 · 점화 필요 (파이어스타터/라이터)"
                    : $"{CampfireKitName} {Mathf.Min(campfireKit, 1)}/1 · 제작(V) 후 설치(G)");
            Write(QuestCategory.Settlement, "quest.campfire", "모닥불을 피운다",
                campfireProgress,
                campfireLit ? 1f : (campfirePlaced ? 0.5f : Mathf.Clamp01(campfireKit) * 0.5f),
                completed: campfireLit, locked: false, latch: true);

            // 3) 물 증류기. 설치하면 물 걱정이 구조적으로 줄어든다.
            bool stillPlaced = WaterStill.Active.Count > 0;
            int stillKit = ProgressionTracker.CountByName(playerInventory, WaterStillKitName);
            Write(QuestCategory.Settlement, "quest.waterstill", "물 증류기를 설치한다",
                stillPlaced ? "설치 완료" : $"{WaterStillKitName} {Mathf.Min(stillKit, 1)}/1 · 제작(V) 후 설치(G)",
                stillPlaced ? 1f : Mathf.Clamp01(stillKit),
                completed: stillPlaced, locked: false, latch: true);

            // 4~5) 집 Lv2 / Lv3 승급.
            WriteShelterUpgradeQuest(shelter, 2, "quest.shelter2", "쉼터를 오두막으로 올린다 (Lv2)");
            WriteShelterUpgradeQuest(shelter, 3, "quest.shelter3", "오두막을 집으로 올린다 (Lv3)");
        }

        /// <summary>
        /// 홈으로 삼을 쉼터 하나를 고른다. 여러 채를 지을 수 있으므로 **가장 높은 레벨**을 대표로 쓴다
        /// (진행도가 뒤로 가지 않게 하려는 것이다).
        /// </summary>
        private static Shelter FindPrimaryShelter()
        {
            Shelter best = null;
            var shelters = Shelter.ActiveShelters;
            for (int i = 0; i < shelters.Count; i++)
            {
                var shelter = shelters[i];
                if (shelter == null)
                    continue;
                if (best == null || shelter.level > best.level)
                    best = shelter;
            }
            return best;
        }

        /// <summary>
        /// 집 승급 퀘스트 한 줄. 진행도는 Shelter가 이미 들고 있는 승급 재료 목록
        /// (level2Requirements / level3Requirements)과 소지품 개수를 대조해 만든다 - 재료 표를
        /// 이 파일에 다시 적지 않는다(적으면 Shelter가 바뀔 때 조용히 어긋난다).
        /// 지금 바로 지을 수 있는 단계(shelter.level + 1)일 때는 Shelter.DescribeNextBuildAction이
        /// 만든 문구를 그대로 쓴다 - 상호작용 프롬프트와 퀘스트 창이 다른 말을 하지 않게 하려는 것이다.
        /// </summary>
        private void WriteShelterUpgradeQuest(Shelter shelter, int targetLevel, string id, string title)
        {
            if (shelter == null)
            {
                Write(QuestCategory.Settlement, id, title, "쉼터를 먼저 세워야 한다", 0f,
                    completed: false, locked: true, latch: true);
                return;
            }

            if (shelter.level >= targetLevel)
            {
                Write(QuestCategory.Settlement, id, title, $"Lv{shelter.level} 도달", 1f,
                    completed: true, locked: false, latch: true);
                return;
            }

            var requirements = targetLevel == 2 ? shelter.level2Requirements : shelter.level3Requirements;
            float fraction = MaterialFraction(requirements);

            // 아직 앞 레벨이 안 끝났으면 이 줄은 "대기"다. 재료 문구까지 띄우면 지금 모아야 할 것과
            // 나중에 모아야 할 것이 섞여 읽힌다.
            if (shelter.level + 1 != targetLevel)
            {
                Write(QuestCategory.Settlement, id, title, $"Lv{targetLevel - 1} 먼저", fraction,
                    completed: false, locked: true, latch: true);
                return;
            }

            string detail = shelter.DescribeNextBuildAction(playerInventory);
            if (string.IsNullOrEmpty(detail))
                detail = DescribeMissingMaterials(requirements);

            Write(QuestCategory.Settlement, id, title, detail, fraction,
                completed: false, locked: false, latch: true);
        }

        /// <summary>
        /// 요구 재료 대비 소지 비율(0~1). 종류별 충족률의 평균이라
        /// 종류별 충족률의 평균이라는 점에서 다른 진행률 계산과 같은 셈법이다.
        /// </summary>
        private float MaterialFraction(List<ShelterMaterialRequirement> requirements)
        {
            if (requirements == null || requirements.Count == 0)
                return 0f;

            float sum = 0f;
            int counted = 0;
            for (int i = 0; i < requirements.Count; i++)
            {
                var requirement = requirements[i];
                if (requirement == null || string.IsNullOrEmpty(requirement.itemName) || requirement.count <= 0)
                    continue;

                int have = ProgressionTracker.CountByName(playerInventory, requirement.itemName);
                sum += Mathf.Clamp01((float)have / requirement.count);
                counted++;
            }

            return counted == 0 ? 0f : sum / counted;
        }

        /// <summary>모자란 재료를 최대 3종까지 "이름 x/y"로 적는다(폴백 - 보통은 Shelter 쪽 문구를 쓴다).</summary>
        private string DescribeMissingMaterials(List<ShelterMaterialRequirement> requirements)
        {
            if (requirements == null || requirements.Count == 0)
                return "재료 정보 없음";

            var builder = new System.Text.StringBuilder();
            int shown = 0;
            for (int i = 0; i < requirements.Count && shown < 3; i++)
            {
                var requirement = requirements[i];
                if (requirement == null || string.IsNullOrEmpty(requirement.itemName) || requirement.count <= 0)
                    continue;

                int have = ProgressionTracker.CountByName(playerInventory, requirement.itemName);
                if (have >= requirement.count)
                    continue;

                if (shown > 0)
                    builder.Append(" · ");
                builder.Append(requirement.itemName).Append(' ').Append(have).Append('/').Append(requirement.count);
                shown++;
            }

            return shown == 0 ? "재료 준비 완료" : builder.ToString();
        }

        // ── 항해 ────────────────────────────────────────────────────────────────

        /// <summary>
        /// 뗏목 건조 + 경비행기 수리. 감독 방향상 **맨 아래**에 둔다. 여기 새 목표를 넣지 않는다
        /// (AGENT_BRIEF: 엔딩에 새 작업을 넣지 마라) - 판정 대상만 새 뗏목 계약으로 갈아 끼웠다.
        ///
        /// [퀘스트 id 교체] 예전 3줄(quest.boat1/2/3)은 3단계 도면-작업대 시스템의 단계였다.
        /// 그 시스템이 사라졌으므로 뗏목 기반 3줄(quest.raft.tiles / quest.raft.rig / quest.raft.supply)로
        /// 바꿨다. **세이브 영향 없음** - 완료 래치(latchedComplete)는 이 컴포넌트의 메모리에만 있고
        /// SaveData에 퀘스트 필드가 하나도 없다(확인 완료). 옛 id는 그냥 다시 등장하지 않을 뿐이다.
        /// </summary>
        private void BuildVoyageQuests()
        {
            WriteRaftTileQuest();
            WriteRaftRigQuest();
            WriteRaftSupplyQuest();

            int aircraftPercent = aircraftRepair != null
                ? Mathf.RoundToInt(Mathf.Clamp01(aircraftRepair.GetOverallProgress()) * 100f)
                : 0;
            bool aircraftDone = aircraftRepair != null && aircraftRepair.isRepairComplete;

            // 특대 섬 항로가 이 줄의 진짜 선행 조건이다. 재료를 100% 모아도 배가 못 가면 아무것도
            // 못 하므로, "재료 %"만 보여 주면 플레이어를 특대 섬 앞에서 헤매게 만든다.
            // 판정은 IslandTravel.CurrentBypass.OceanReadyWithMotor와 같은 값을 본다.
            bool oceanRoute = raft != null && raft.IsOceanReady && raft.HasPart(RaftPart.Motor);
            Write(QuestCategory.Voyage, "quest.aircraft", "특대 섬의 경비행기를 수리한다 (선택)",
                aircraftDone ? "수리 완료"
                    : oceanRoute ? $"재료 {aircraftPercent}%"
                    : "먼저 특대 섬 항로를 연다 - 대양 규격 뗏목 + 모터",
                aircraftPercent / 100f,
                completed: aircraftDone, locked: aircraftRepair == null || !oceanRoute, latch: true);

            BuildBossQuests();
        }

        /// <summary>
        /// [엔드게임 보스] 보스 3종 체크리스트. 항해 묶음의 맨 아래에 둔다 - 이 셋은 탈출의
        /// **전제**가 아니라 탈출에 얹는 선택 목표이고(세 번째 엔딩 "정복"의 조건),
        /// 뗏목/경비행기보다 먼저 손댈 일이 없기 때문이다.
        ///
        /// 진행도 문구가 곧 공략 힌트다("섬에서 먼 외해" 등) - 이 게임에는 보스를 안내하는 다른
        /// 수단이 없고(미니맵에 표시하지 않는다), 바다는 40km²라 힌트가 없으면 찾을 방법이 없다.
        ///
        /// **래치하지 않는다**(latch: false). 보스 진행도는 이미 영구 상태(BossCreature의 static +
        /// 세이브 필드)라 래치가 더 줄 것이 없고, 오히려 F9 불러오기로 진행도가 되감겼을 때
        /// 래치가 남아 "잡지 않은 보스가 완료로 표시되는" 거짓말이 된다.
        /// </summary>
        private void BuildBossQuests()
        {
            for (int kind = 0; kind < BossCreature.KindCount; kind++)
            {
                bool defeated = BossCreature.IsDefeated(kind);
                bool hasTrophy = BossCreature.HasTrophy(kind);

                string progress = hasTrophy
                    ? "전리품 확보"
                    : (defeated
                        ? $"처치 완료 · {BossCreature.GetTrophyName(kind)} 수거 전"
                        : BossLocationHints[kind]);

                Write(QuestCategory.Voyage, "quest.boss." + kind,
                    $"{BossCreature.GetDisplayName(kind)}를 물리치고 전리품을 얻는다 (선택)",
                    progress,
                    hasTrophy ? 1f : (defeated ? 0.5f : 0f),
                    completed: hasTrophy, locked: false, latch: false);
            }
        }

        /// <summary>보스가 어디에 있는지 알려 주는 한 줄(배치 규칙은 BossSpawner가 정한다).</summary>
        private static readonly string[] BossLocationHints =
        {
            "섬에서 먼 외해에 있다",
            "수중 동굴 근처에 있다",
            "가장 깊은 해저에 있다",
        };

        /// <summary>1단계: 해안에 바닥판을 깔아 항해 가능한 크기까지 키운다.</summary>
        private void WriteRaftTileQuest()
        {
            const string Id = "quest.raft.tiles";
            string title = $"해안에 뗏목 바닥판을 {RaftStructure.SeaworthyTileCount}칸 깐다";

            if (raft == null)
            {
                Write(QuestCategory.Voyage, Id, title, "해안을 찾지 못했다", 0f,
                    completed: false, locked: true, latch: true);
                return;
            }

            int tiles = raft.BaseTileCount;
            int target = RaftStructure.SeaworthyTileCount;
            bool done = tiles >= target;

            Write(QuestCategory.Voyage, Id, title,
                done ? "완료" : $"바닥판 {tiles}/{target}칸",
                target > 0 ? (float)tiles / target : 0f,
                completed: done, locked: false, latch: true);
        }

        /// <summary>2단계: 돛과 키를 달아 방향을 잡을 수 있게 만든다(모터가 있으면 그것으로 대체).</summary>
        private void WriteRaftRigQuest()
        {
            const string Id = "quest.raft.rig";
            const string Title = "뗏목에 돛과 키를 단다";

            if (raft == null)
            {
                Write(QuestCategory.Voyage, Id, Title, "뗏목이 없다", 0f,
                    completed: false, locked: true, latch: true);
                return;
            }

            bool motor = raft.HasPart(RaftPart.Motor);
            bool sail = raft.HasPart(RaftPart.Sail);
            bool rudder = raft.HasPart(RaftPart.Rudder);
            bool done = motor || (sail && rudder);

            // 바닥판이 먼저다 - 깔 자리가 없으면 돛대를 세울 수 없다.
            if (!done && raft.BaseTileCount < RaftStructure.SeaworthyTileCount)
            {
                Write(QuestCategory.Voyage, Id, Title, "바닥판 먼저", 0f,
                    completed: false, locked: true, latch: true);
                return;
            }

            int installed = (sail ? 1 : 0) + (rudder ? 1 : 0);
            string detail = done
                ? (motor ? "모터 장착" : "완료")
                : DescribeMissingParts(sail, rudder);

            Write(QuestCategory.Voyage, Id, Title, detail, installed * 0.5f,
                completed: done, locked: false, latch: true);
        }

        /// <summary>3단계: 대양에 나갈 크기까지 바닥판을 마저 깔고 탈출 준비를 끝낸다.</summary>
        private void WriteRaftSupplyQuest()
        {
            const string Id = "quest.raft.supply";
            string title = $"뗏목을 대양 항해 규격(바닥판 {RaftStructure.OceanReadyTileCount}칸)까지 키운다";

            if (raft == null)
            {
                Write(QuestCategory.Voyage, Id, title, "뗏목이 없다", 0f,
                    completed: false, locked: true, latch: true);
                return;
            }

            int tiles = raft.BaseTileCount;
            int target = RaftStructure.OceanReadyTileCount;
            bool done = raft.IsOceanReady;

            if (!raft.IsSeaworthy)
            {
                Write(QuestCategory.Voyage, Id, title, "먼저 항해 가능한 뗏목으로", 0f,
                    completed: false, locked: true, latch: true);
                return;
            }

            // 모터 문구를 여기 붙이는 이유: 대양 규격은 귀환 엔딩 자격일 뿐이고, 특대 섬 해류는
            // 거기에 모터까지 달려야 뚫린다(IslandTravel). 두 조건이 다르다는 것을 이 줄에서 알린다.
            string doneDetail = raft.HasPart(RaftPart.Motor)
                ? "출항 준비 완료 — 비축 물자를 채워라"
                : "출항 준비 완료 — 비축 물자를 채워라 (특대 섬으로 가려면 모터가 더 필요하다)";

            Write(QuestCategory.Voyage, Id, title,
                done ? doneDetail : $"바닥판 {tiles}/{target}칸 · {raft.DescribeState()}",
                target > 0 ? Mathf.Clamp01((float)tiles / target) : 0f,
                completed: done, locked: false, latch: true);
        }

        /// <summary>아직 없는 삭구 부품을 적는다("돛 · 키" 형태).</summary>
        private static string DescribeMissingParts(bool sail, bool rudder)
        {
            if (!sail && !rudder)
                return $"{RaftStructure.GetPartName(RaftPart.Sail)} · {RaftStructure.GetPartName(RaftPart.Rudder)} 필요";

            return sail
                ? $"{RaftStructure.GetPartName(RaftPart.Rudder)} 필요"
                : $"{RaftStructure.GetPartName(RaftPart.Sail)} 필요";
        }

        // ────────────────────────────────────────────────────────────────────────
        // 기록
        // ────────────────────────────────────────────────────────────────────────

        private static int Percent(float ratio)
        {
            return Mathf.RoundToInt(Mathf.Clamp01(ratio) * 100f);
        }

        /// <summary>
        /// 퀘스트 한 줄을 목록의 writeIndex 자리에 기록한다. 항목 객체는 재사용하고, 화면에 보이는
        /// 값(제목/진행도 문구/완료/잠김/막대 정수 퍼센트) 중 하나라도 달라졌을 때만 dirty를 세운다.
        /// 막대를 float 그대로 비교하지 않는 이유: 표시는 정수 퍼센트 폭이라 소수점 흔들림은
        /// 화면상 아무 차이도 만들지 않는다(SurvivalHudUI가 쓰는 것과 같은 절약 규칙).
        /// </summary>
        private void Write(QuestCategory category, string id, string title, string progress, float fraction,
            bool completed, bool locked, bool latch)
        {
            if (latch)
            {
                if (completed)
                    latchedComplete.Add(id);
                else if (latchedComplete.Contains(id))
                    completed = true;
            }

            if (completed)
                locked = false;

            fraction = Mathf.Clamp01(completed ? 1f : fraction);

            QuestEntry entry;
            if (writeIndex < quests.Count)
            {
                entry = quests[writeIndex];
            }
            else
            {
                entry = new QuestEntry();
                quests.Add(entry);
                dirty = true;
            }
            writeIndex++;

            if (entry.id != id || entry.category != category || entry.title != title
                || entry.progress != progress || entry.completed != completed || entry.locked != locked
                || Percent(entry.fraction) != Percent(fraction))
            {
                dirty = true;
            }

            entry.category = category;
            entry.id = id;
            entry.title = title;
            entry.progress = progress;
            entry.fraction = fraction;
            entry.completed = completed;
            entry.locked = locked;
        }
    }
}
