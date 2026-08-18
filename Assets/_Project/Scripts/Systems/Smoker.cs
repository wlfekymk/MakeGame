using System.Collections.Generic;
using UnityEngine;
using MakeGame.Data;
using MakeGame.Player;

namespace MakeGame.Systems
{
    /// <summary>
    /// 훈연기. 생고기/생선을 넣고 불을 피워 두면 시간이 지나 **절대 상하지 않는** 훈제육/훈제생선이 된다.
    /// Stranded Deep의 훈연기와 같은 역할이며, 이 게임에서는 "부패(FoodSpoilage)를 이기는 유일한 가공"이라
    /// 중반 목표가 된다.
    ///
    /// [설치·저장 규약은 CraftStation과 같다]
    ///  · 설치: 인벤토리의 키트 아이템(ItemData.isPlaceable) + G. 키트 에셋의 placementPrefab이 비어
    ///    있으면 시작 시 런타임 원본(템플릿)을 만들어 끼워 넣는다(CraftStation.EnsureKitPlacementTemplates와
    ///    완전히 같은 절차 - 프리팹 에셋 생성은 이 작업의 락 밖이다).
    ///  · 활성 목록: Campfire.Active / CraftStation.Active와 같은 방식의 정적 목록(R1 리셋 훅 포함).
    ///  · 외형: 신규 모델 없이 StructureVisualBuilder의 프리미티브 조합으로 Awake에서 조립한다.
    ///
    /// [상호작용에 새 키를 만들지 않는다 - 모닥불 경로에 얹는다]
    /// InteractionController.cs가 이 작업의 락 밖이라 새 E/R 분기를 넣을 수 없고, 남는 키도 없다
    /// (AGENT_BRIEF 3장 키 배정표). 그래서 훈연기는 같은 오브젝트에 <see cref="Campfire"/>를 하나 달고
    /// 기존 두 경로를 그대로 물려받는다.
    ///  · E(상호작용) → Campfire.TryLight: 발화 도구 + 나뭇가지 1개로 불을 지핀다. 연료 규칙이 곧 훈연기의
    ///    연료 규칙이 된다(연료가 다 타면 훈연도 멈춘다).
    ///  · R(조리)   → Campfire.CookItem: 훈연기 위에서는 즉시 굽는 대신 이 클래스의 <see cref="TryInsert"/>로
    ///    넘어가 재료가 훈연대에 걸린다(Campfire.CookItem 첫 줄의 분기 참고).
    /// 완성품은 플레이어가 근처에 있으면 자동으로 가방에 들어간다 - 수거 전용 키를 만들 수 없기 때문이며,
    /// 가방이 가득 차 있으면 훈연기가 계속 들고 있다가 자리가 나면 넘긴다(아이템이 사라지는 경로가 없다).
    ///
    /// [에셋이 없으면 조용히 비활성이다]
    /// 훈제육/훈제생선 ItemData가 아직 없으면 <see cref="TryInsert"/>가 재료를 소모하지 않고 실패만 하고,
    /// 훈연기키트 ItemData가 없으면 설치 자체가 존재하지 않는다. 어느 쪽도 예외를 던지지 않는다.
    /// 필요한 에셋 목록은 보고서의 [요청] 항목에 정확한 이름·필드값으로 적어 두었다.
    /// </summary>
    public class Smoker : MonoBehaviour
    {
        // ── 이름 규약 ────────────────────────────────────────────────────────────
        //
        // 아래 이름들은 ItemData.itemName과 **문자 그대로** 대조된다(CraftStation의 키트 이름 규약과 같다).
        // 재료 두 종은 실측값이고(Item_생고기.asset / Item_생선.asset), 결과물 두 종과 키트는
        // **아직 에셋이 없다** - 다음 담당이 이 이름 그대로 만들어야 코드가 켜진다.

        /// <summary>훈연기 키트 아이템의 itemName. (기존 키트 규약대로 공백 없음)</summary>
        public const string SmokerKitItemName = "훈연기키트";

        /// <summary>화면에 보여줄 시설 이름.</summary>
        public const string DisplayName = "훈연기";

        /// <summary>훈연 재료 - 생고기(Item_생고기.asset 실측값).</summary>
        public const string RawMeatItemName = "생고기";

        /// <summary>훈연 결과 - 훈제육(신규 에셋 필요).</summary>
        public const string SmokedMeatItemName = "훈제육";

        /// <summary>훈연 재료 - 생선(Item_생선.asset 실측값).</summary>
        public const string RawFishItemName = "생선";

        /// <summary>훈연 결과 - 훈제생선(신규 에셋 필요).</summary>
        public const string SmokedFishItemName = "훈제생선";

        /// <summary>연료 아이템 이름(Item_나뭇가지.asset 실측값). 모닥불과 같은 연료를 쓴다.</summary>
        public const string FuelItemName = "나뭇가지";

        /// <summary>발화 도구 이름(Item_파이어스타터.asset / Item_라이터.asset 실측값).</summary>
        public const string FireStarterItemName = "파이어스타터";
        public const string LighterItemName = "라이터";

        // ── 설정 (코드 기본값이 유일한 소스 - 프리팹이 없다) ────────────────────────

        [Tooltip("재료 1개를 훈제로 바꾸는 데 걸리는 시간(초). 불이 켜져 있는 동안에만 흐른다.")]
        public float smokeSecondsPerItem = 75f;

        [Tooltip("훈연기의 연료 1개(나뭇가지)당 유지 시간(초). 모닥불(30초)보다 오래 간다 - 재료 하나를" +
            " 훈연하는 데 75초가 필요해 모닥불 값 그대로면 한 개를 굽는 데 나뭇가지가 3개씩 든다.")]
        public float fuelSecondsPerUnit = 60f;

        [Tooltip("훈연대에 한 번에 걸 수 있는 재료 + 완성품 개수의 합.")]
        public int capacity = 8;

        [Tooltip("완성품을 플레이어 가방으로 넘겨주는 거리(미터). 이 밖에 있으면 훈연기가 계속 들고 있는다.")]
        public float deliveryRadius = 8f;

        [Tooltip("재료 1개를 훈제로 완성했을 때 주는 요리(Cooking) 스킬 경험치. 모닥불 조리(8)보다 높다.")]
        public float smokingExperience = 12f;

        // ── 활성 목록 / 템플릿 ───────────────────────────────────────────────────

        private static readonly List<Smoker> activeSmokers = new List<Smoker>();

        /// <summary>현재 씬에 살아 있는 훈연기 목록(읽기 전용). 설치 원본(템플릿)은 포함되지 않는다.</summary>
        public static IReadOnlyList<Smoker> Active => activeSmokers;

        /// <summary>설치 원본을 담아 두는 루트(DontDestroyOnLoad). CraftStation.templateRoot와 같은 방식이다.</summary>
        private static GameObject templateRoot;

        /// <summary>템플릿을 세워 두는 y 좌표. 지형(y ≈ 0~30)에서 충분히 떨어져 있으면 된다.</summary>
        private const float TemplateParkY = -5000f;

        /// <summary>이름 → ItemData 조회 캐시(R1 리셋 대상). 훈제 결과물 에셋이 없으면 비어 있는 채로 남는다.</summary>
        private static Dictionary<string, ItemData> itemsByName;

        /// <summary>결과물 에셋이 없다는 경고를 한 번만 남기기 위한 래치.</summary>
        private static bool missingSmokedItemWarned;

        /// <summary>
        /// 이 인스턴스가 설치 원본인지. 부모가 templateRoot인지로만 판정하므로 별도 플래그가 필요 없다
        /// (Instantiate로 만든 사본은 부모가 없어 언제나 false다 - CraftStation과 같은 규칙).
        /// </summary>
        private bool IsPlacementTemplate =>
            templateRoot != null && transform.parent == templateRoot.transform;

        // ── 상태 (세이브 대상) ────────────────────────────────────────────────────

        /// <summary>훈연대에 걸려 있는(아직 안 익은) 재료. 앞에서부터 하나씩 처리한다.</summary>
        private readonly List<ItemData> pendingRaw = new List<ItemData>();

        /// <summary>다 된 완성품(아직 플레이어에게 못 넘긴 것).</summary>
        private readonly List<ItemData> readyOutput = new List<ItemData>();

        /// <summary>지금 처리 중인 재료의 진행 시간(초).</summary>
        private float progressSeconds;

        /// <summary>경험치를 줄 대상. 마지막으로 재료를 넣은 플레이어에게서 받아 둔다(없으면 씬에서 찾는다).</summary>
        private PlayerSkills cachedSkills;

        private bool visualBuilt;

        /// <summary>훈연대에 걸린 재료(읽기 전용 - 세이브용).</summary>
        public IReadOnlyList<ItemData> PendingRaw => pendingRaw;

        /// <summary>아직 수거되지 않은 완성품(읽기 전용 - 세이브용).</summary>
        public IReadOnlyList<ItemData> ReadyOutput => readyOutput;

        /// <summary>지금 처리 중인 재료의 진행 시간(초 - 세이브용).</summary>
        public float ProgressSeconds => progressSeconds;

        /// <summary>이 훈연기의 불(연료·점화 상태를 소유한다). 아직 붙지 않았으면 null일 수 있다.</summary>
        public Campfire Fire => GetComponent<Campfire>();

        // ── 수명 주기 ────────────────────────────────────────────────────────────

        /// <summary>
        /// [R1 규약] 도메인 리로드를 끈 플레이 모드에서 이전 세션의 정적 상태가 새지 않게 비운다
        /// (CraftStation.ResetStaticCache와 같은 이유).
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticCache()
        {
            activeSmokers.Clear();
            templateRoot = null;
            itemsByName = null;
            missingSmokedItemWarned = false;
            cachedInventory = null;
            cachedPlayerTransform = null;
        }

        private void OnEnable()
        {
            if (IsPlacementTemplate)
                return;

            if (!activeSmokers.Contains(this))
                activeSmokers.Add(this);
        }

        private void OnDisable()
        {
            activeSmokers.Remove(this);
        }

        /// <summary>
        /// 콜라이더·외형을 만들고 지면에 내려놓은 뒤, E/R 경로를 물려받기 위한 Campfire를 붙인다
        /// (설치 원본에는 붙이지 않는다 - 원본까지 Campfire.Active/FindObjectsByType에 잡히면 저장할
        /// 때마다 지하 -5000m에 모닥불이 하나씩 기록된다. CraftStation이 겪은 것과 같은 함정이다).
        /// </summary>
        private void Awake()
        {
            EnsureCollider();
            BuildVisual();

            if (IsPlacementTemplate)
                return;

            transform.position = TerrainSampler.SnapToGround(transform.position);
            EnsureFire();
        }

        /// <summary>
        /// 같은 오브젝트의 Campfire를 확보하고 훈연기용 값으로 맞춘다.
        /// 이미 붙어 있으면(설치 원본을 복제한 사본) 값만 확인한다 - 연료/점화 상태는 그대로 둔다.
        /// 발화 도구·연료 아이템은 씬/프리팹 배선이 없으므로 이름으로 찾아 꽂는다. 찾지 못하면
        /// null로 남는데, 그 경우 Campfire.TryLight는 그 조건을 건너뛴다(예외는 나지 않는다).
        /// </summary>
        private void EnsureFire()
        {
            var fire = GetComponent<Campfire>();
            if (fire == null)
                fire = gameObject.AddComponent<Campfire>();

            fire.secondsPerFuel = fuelSecondsPerUnit > 0f ? fuelSecondsPerUnit : fire.secondsPerFuel;

            if (fire.fuelItem == null)
                fire.fuelItem = FindItemByName(FuelItemName);
            if (fire.fireStarterItem == null)
                fire.fireStarterItem = FindItemByName(FireStarterItemName);
            if (fire.alternateFireStarterItem == null)
                fire.alternateFireStarterItem = FindItemByName(LighterItemName);
        }

        /// <summary>
        /// 다 된 완성품을 플레이어 가방으로 넘긴다.
        /// **훈연 진행은 여기서 하지 않는다** - Campfire.Tick이 "태운 연료 시간"만큼 AdvanceSmoking을
        /// 불러 주기 때문이다(그래야 취침으로 건너뛴 시간·우천 가속에서도 연료와 훈연이 어긋나지 않는다).
        /// </summary>
        private void Update()
        {
            TryDeliverOutput();
        }

        /// <summary>
        /// 훈연을 seconds만큼 진행시킨다. **호출자는 Campfire.Tick 하나다**(태운 연료 시간을 넘겨준다).
        /// 재료가 하나도 없을 때 진행도를 0으로 되돌리는 이유는 Shelter.AdvanceDrying과 같다 -
        /// 빈 훈연기가 시간을 쌓아 두었다가 넣자마자 즉시 완성시키는 것을 막는다.
        /// </summary>
        public void AdvanceSmoking(float deltaTime)
        {
            if (pendingRaw.Count == 0)
            {
                progressSeconds = 0f;
                return;
            }

            Campfire fire = Fire;
            if (fire == null || !fire.isLit || deltaTime <= 0f)
                return;

            float perItem = smokeSecondsPerItem > 0f ? smokeSecondsPerItem : 75f;
            progressSeconds += deltaTime;

            // guard: smokeSecondsPerItem이 아주 작게 설정돼도 한 프레임을 잡아먹지 않도록 상한을 둔다.
            for (int guard = 0; guard < 64 && progressSeconds >= perItem && pendingRaw.Count > 0; guard++)
            {
                progressSeconds -= perItem;
                CompleteOne();
            }

            if (pendingRaw.Count == 0)
                progressSeconds = 0f;
        }

        /// <summary>재료 하나를 훈제로 바꿔 완성품 칸으로 옮기고 요리 경험치를 준다.</summary>
        private void CompleteOne()
        {
            ItemData raw = pendingRaw[0];
            pendingRaw.RemoveAt(0);

            if (!TryGetSmokedResult(raw, out ItemData smoked))
                return; // 에셋이 사라진 경우(있을 수 없지만) 재료만 사라지지 않게 조용히 버린다.

            readyOutput.Add(smoked);

            PlayerSkills skills = ResolveSkills();
            if (skills != null && smokingExperience > 0f)
                skills.AddExperience(SkillType.Cooking, smokingExperience);
        }

        /// <summary>
        /// 완성품을 근처 플레이어의 가방으로 넘긴다. 거리 밖이거나 칸이 없으면 아무 것도 하지 않고
        /// 계속 들고 있는다(아이템이 사라지는 경로를 만들지 않는다). CanAccept로 먼저 확인하므로
        /// 가방이 가득 찬 동안 실패음/경고가 매 프레임 쏟아지지 않는다.
        /// </summary>
        private void TryDeliverOutput()
        {
            if (readyOutput.Count == 0)
                return;

            PlayerInventory inventory = ResolveInventory();
            if (inventory == null)
                return;

            // 거리 기준은 **플레이어 본체**다. PlayerInventory가 플레이어 오브젝트에 붙어 있다고
            // 단정하지 않는다(씬 직렬화는 에이전트가 확인할 수 없다 - AGENT_BRIEF 4장 4번).
            // PlayerController는 정의상 플레이어에 있고, 못 찾으면 인벤토리 쪽 위치로 떨어진다.
            Transform playerTransform = ResolvePlayerTransform(inventory);
            if (playerTransform == null)
                return;

            float radius = deliveryRadius > 0f ? deliveryRadius : 8f;
            if ((playerTransform.position - transform.position).sqrMagnitude > radius * radius)
                return;

            for (int i = readyOutput.Count - 1; i >= 0; i--)
            {
                ItemData smoked = readyOutput[i];
                if (smoked == null)
                {
                    readyOutput.RemoveAt(i);
                    continue;
                }

                if (!inventory.CanAccept(smoked))
                    continue;

                inventory.AddItem(smoked);
                readyOutput.RemoveAt(i);
                AudioManager.Instance?.PlayPickup();
            }
        }

        // ── 투입 (Campfire.CookItem이 넘겨준다) ──────────────────────────────────

        /// <summary>
        /// 생음식 하나를 훈연대에 건다. **Campfire.CookItem이 훈연기 위에서 이 메서드로 넘긴다**
        /// (그래서 조작은 예전과 같은 "훈연기를 조준하고 R" 하나뿐이다).
        ///
        /// 실패하는 경우(재료를 절대 소모하지 않는다):
        ///  · 불이 꺼져 있다 - 연료를 먼저 넣어야 한다(E).
        ///  · 훈제 결과 아이템 에셋이 아직 없다(훈제육/훈제생선) - 경고 한 번만 남기고 조용히 실패한다.
        ///  · 훈연대가 가득 찼다.
        /// </summary>
        /// <returns>실제로 걸었으면 true.</returns>
        public bool TryInsert(PlayerInventory inventory, PlayerSkills skills, ItemData rawFood)
        {
            if (inventory == null || rawFood == null)
                return false;

            Campfire fire = Fire;
            if (fire == null || !fire.isLit)
            {
                AudioManager.Instance?.PlayActionFail();
                Debug.Log("[Smoker] 불이 꺼져 있어 훈연할 수 없다 (E로 나뭇가지를 넣어 불을 지펴라).");
                return false;
            }

            if (!TryGetSmokedResult(rawFood, out ItemData smoked))
            {
                AudioManager.Instance?.PlayActionFail();
                if (!missingSmokedItemWarned)
                {
                    missingSmokedItemWarned = true;
                    Debug.LogWarning($"[Smoker] '{rawFood.itemName}'의 훈제 결과 아이템을 찾지 못해 훈연할 수 없다." +
                        $" ItemData.smokedResult를 연결하거나 '{SmokedMeatItemName}'/'{SmokedFishItemName}'" +
                        " 에셋을 만들어야 한다.");
                }
                return false;
            }

            if (pendingRaw.Count + readyOutput.Count >= Mathf.Max(1, capacity))
            {
                AudioManager.Instance?.PlayActionFail();
                Debug.Log("[Smoker] 훈연대가 가득 찼다.");
                return false;
            }

            if (!inventory.RemoveItems(rawFood, 1))
                return false;

            pendingRaw.Add(rawFood);
            if (skills != null)
                cachedSkills = skills;

            AudioManager.Instance?.PlayCraftSuccess();
            return true;
        }

        // ── 조회 ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// 이 재료를 훈연했을 때 나오는 아이템을 찾는다.
        /// 우선순위: ItemData.smokedResult(에셋 배선) → 이름 규칙(생고기→훈제육 / 생선→훈제생선).
        /// 어느 쪽으로도 찾지 못하면 false다(= 그 재료는 훈연할 수 없다).
        /// </summary>
        public static bool TryGetSmokedResult(ItemData rawFood, out ItemData smoked)
        {
            smoked = null;
            if (rawFood == null)
                return false;

            if (rawFood.smokedResult != null)
            {
                smoked = rawFood.smokedResult;
                return true;
            }

            string targetName = null;
            if (rawFood.itemName == RawMeatItemName)
                targetName = SmokedMeatItemName;
            else if (rawFood.itemName == RawFishItemName)
                targetName = SmokedFishItemName;

            if (targetName == null)
                return false;

            smoked = FindItemByName(targetName);
            return smoked != null;
        }

        /// <summary>
        /// 이름으로 ItemData를 찾는다(최초 1회만 표를 만든다). 레지스트리 에셋이 없으면
        /// SaveLoadController.EnsureItemDataCache와 같은 폴백(현재 로드된 ItemData 전수 조회)을 쓴다.
        /// </summary>
        private static ItemData FindItemByName(string itemName)
        {
            if (string.IsNullOrEmpty(itemName))
                return null;

            if (itemsByName == null)
            {
                itemsByName = new Dictionary<string, ItemData>();

                ItemDataRegistry registry = ItemDataRegistry.LoadFromResources();
                if (registry != null && registry.allItems != null)
                {
                    for (int i = 0; i < registry.allItems.Count; i++)
                        AddToNameCache(registry.allItems[i]);
                }
                else
                {
                    var all = Resources.FindObjectsOfTypeAll<ItemData>();
                    for (int i = 0; i < all.Length; i++)
                        AddToNameCache(all[i]);
                }
            }

            itemsByName.TryGetValue(itemName, out ItemData found);
            return found;
        }

        private static void AddToNameCache(ItemData item)
        {
            if (item == null || string.IsNullOrEmpty(item.itemName))
                return;

            if (!itemsByName.ContainsKey(item.itemName))
                itemsByName.Add(item.itemName, item);
        }

        /// <summary>경험치를 줄 PlayerSkills를 확보한다(마지막 투입자 → 없으면 씬 조회).</summary>
        private PlayerSkills ResolveSkills()
        {
            if (cachedSkills == null)
                cachedSkills = FindAnyObjectByType<PlayerSkills>();

            return cachedSkills;
        }

        /// <summary>완성품을 넘길 플레이어 인벤토리를 확보한다.</summary>
        private static PlayerInventory cachedInventory;

        /// <summary>거리 판정에 쓸 플레이어 본체의 Transform.</summary>
        private static Transform cachedPlayerTransform;

        private static PlayerInventory ResolveInventory()
        {
            if (cachedInventory == null)
                cachedInventory = FindAnyObjectByType<PlayerInventory>();

            return cachedInventory;
        }

        /// <summary>플레이어 본체 Transform을 확보한다(없으면 인벤토리가 붙은 오브젝트로 대체).</summary>
        private static Transform ResolvePlayerTransform(PlayerInventory inventory)
        {
            if (cachedPlayerTransform == null)
            {
                var controller = FindAnyObjectByType<PlayerController>();
                if (controller != null)
                    cachedPlayerTransform = controller.transform;
            }

            if (cachedPlayerTransform != null)
                return cachedPlayerTransform;

            return inventory != null ? inventory.transform : null;
        }

        // ── 세이브 복원 ──────────────────────────────────────────────────────────

        /// <summary>
        /// 저장된 상태(걸린 재료 · 완성품 · 진행도)를 그대로 되돌린다. 재료 목록은 이미 ItemData로
        /// 해석된 것을 받는다 - 이름 해석은 캐시를 들고 있는 SaveLoadController 쪽에서 한다
        /// (Shelter.RestoreChestState와 같은 분담).
        /// </summary>
        public void ApplySavedState(List<ItemData> savedPending, List<ItemData> savedOutput, float savedProgress)
        {
            pendingRaw.Clear();
            readyOutput.Clear();

            if (savedPending != null)
            {
                for (int i = 0; i < savedPending.Count; i++)
                {
                    if (savedPending[i] != null)
                        pendingRaw.Add(savedPending[i]);
                }
            }

            if (savedOutput != null)
            {
                for (int i = 0; i < savedOutput.Count; i++)
                {
                    if (savedOutput[i] != null)
                        readyOutput.Add(savedOutput[i]);
                }
            }

            progressSeconds = Mathf.Max(0f, savedProgress);
        }

        // ── 설치 원본(placementPrefab) 공급 ─────────────────────────────────────
        //
        // CraftStation.EnsureKitPlacementTemplates와 완전히 같은 이유·같은 절차다: G 설치 경로는
        // placementPrefab != null 인 아이템만 놓는데, 새로 만들어질 훈연기키트 에셋에는 그 필드를
        // 채울 프리팹 자체가 없다(프리팹 3개뿐 - AGENT_BRIEF 1장). **비어 있을 때만** 런타임 원본을
        // 만들어 끼우므로, 나중에 진짜 프리팹이 배선되면 이 경로는 저절로 꺼진다.

        /// <summary>
        /// 훈연기 키트의 placementPrefab이 비어 있으면 런타임 원본을 만들어 채운다.
        /// 키트 에셋이 아직 없으면 아무 일도 하지 않는다(= 훈연기는 존재하지 않는 상태로 남는다).
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void EnsureKitPlacementTemplate()
        {
            ItemData kit = FindItemByName(SmokerKitItemName);
            if (kit == null || !kit.isPlaceable || kit.placementPrefab != null)
                return;

            kit.placementPrefab = GetOrCreateTemplate();
        }

        /// <summary>
        /// 설치 원본을 만들어(또는 이미 있으면 그대로) 돌려준다.
        /// 비활성으로 만들어 붙인 **다음** 활성화한다 - 활성 오브젝트에 AddComponent를 하면 그 자리에서
        /// Awake가 돌아 아직 원점인 위치에서 지면 스냅이 일어난다(CraftStation.GetOrCreateTemplate와 같은 이유).
        /// </summary>
        private static GameObject GetOrCreateTemplate()
        {
            if (templateRoot == null)
            {
                templateRoot = new GameObject("SmokerTemplates");
                templateRoot.transform.position = new Vector3(0f, TemplateParkY, 0f);
                DontDestroyOnLoad(templateRoot);
            }

            const string templateName = "SmokerTemplate";
            Transform existing = templateRoot.transform.Find(templateName);
            if (existing != null)
                return existing.gameObject;

            var go = new GameObject(templateName);
            go.SetActive(false);
            go.transform.SetParent(templateRoot.transform, false);
            go.AddComponent<Smoker>();
            go.SetActive(true);
            return go;
        }

        // ── 외형 ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// 상호작용/충돌용 몸통 콜라이더를 보장한다(시각 파츠는 콜라이더가 없는 순수 시각 오브젝트라
        /// 이 하나가 훈연기의 물리적 실체 전부다). 조준 판정도 이 콜라이더로 이뤄진다.
        /// </summary>
        private void EnsureCollider()
        {
            if (GetComponent<Collider>() != null)
                return;

            var box = gameObject.AddComponent<BoxCollider>();
            box.center = new Vector3(0f, 0.80f, 0f);
            box.size = new Vector3(1.30f, 1.60f, 0.90f);
        }

        /// <summary>
        /// 외형을 프리미티브로 조립한다(신규 모델 없음 - CraftStation.BuildVisual과 같은 방식).
        /// 실루엣 규칙(ArtDirection 2장)에 맞춰 다른 시설과 겹치지 않는 형태를 골랐다:
        /// **아래는 돌로 두른 낮은 화덕, 위는 밧줄로 묶은 사각 틀 + 가로대에 걸린 고기 조각**이라
        /// 멀리서도 "모닥불 위에 무언가 걸려 있다"로 읽힌다.
        /// 이미 자식 파츠가 있으면(설치 원본을 복제한 사본) 다시 만들지 않는다.
        /// </summary>
        private void BuildVisual()
        {
            if (visualBuilt || transform.childCount > 0)
            {
                visualBuilt = true;
                return;
            }
            visualBuilt = true;

            Color wood = StructureVisualBuilder.Driftwood;
            Color stone = StructureVisualBuilder.WeatheredStone;

            // 화덕: 낮은 돌 테두리 + 가운데 숯. 불꽃/연기는 Campfire가 붙이는 CampfireEffect가 낸다.
            StructureVisualBuilder.CreateVisualPart(transform, "Hearth", PrimitiveType.Cylinder,
                new Vector3(0f, 0.10f, 0f), new Vector3(0.90f, 0.10f, 0.90f), stone, null, "rock");
            StructureVisualBuilder.CreateVisualPart(transform, "Char", PrimitiveType.Cylinder,
                new Vector3(0f, 0.19f, 0f), new Vector3(0.60f, 0.03f, 0.60f),
                new Color(0.16f, 0.14f, 0.13f), null, "rock");

            // 훈연대: 묶어 세운 기둥 넷 + 가로대 둘. 높이 1.5m로 제작대(0.9)·용광로(1.4)와 실루엣이 갈린다.
            StructureVisualBuilder.CreateLashedPost(transform, "PostFL", new Vector3(-0.50f, 0.72f, 0.32f), 1.44f, 0.08f, wood);
            StructureVisualBuilder.CreateLashedPost(transform, "PostFR", new Vector3(0.50f, 0.72f, 0.32f), 1.44f, 0.08f, wood);
            StructureVisualBuilder.CreateLashedPost(transform, "PostBL", new Vector3(-0.50f, 0.72f, -0.32f), 1.44f, 0.08f, wood);
            StructureVisualBuilder.CreateLashedPost(transform, "PostBR", new Vector3(0.50f, 0.72f, -0.32f), 1.44f, 0.08f, wood);

            StructureVisualBuilder.CreateVisualPart(transform, "RackBarFront", PrimitiveType.Cube,
                new Vector3(0f, 1.34f, 0.32f), new Vector3(1.14f, 0.07f, 0.07f), wood, null, "driftwood");
            StructureVisualBuilder.CreateVisualPart(transform, "RackBarBack", PrimitiveType.Cube,
                new Vector3(0f, 1.34f, -0.32f), new Vector3(1.14f, 0.07f, 0.07f), wood, null, "driftwood");

            // 걸린 고기 조각 3장 - 이 시설이 "고기를 말리는 곳"임을 알리는 유일한 신호다.
            Color meat = new Color(0.56f, 0.28f, 0.22f);
            StructureVisualBuilder.CreateVisualPart(transform, "Strip1", PrimitiveType.Cube,
                new Vector3(-0.32f, 1.10f, 0f), new Vector3(0.22f, 0.42f, 0.05f), meat, null, "noise");
            StructureVisualBuilder.CreateVisualPart(transform, "Strip2", PrimitiveType.Cube,
                new Vector3(0f, 1.06f, 0f), new Vector3(0.22f, 0.48f, 0.05f), meat, null, "noise");
            StructureVisualBuilder.CreateVisualPart(transform, "Strip3", PrimitiveType.Cube,
                new Vector3(0.32f, 1.12f, 0f), new Vector3(0.22f, 0.38f, 0.05f), meat, null, "noise");

            // 지붕 대신 얹은 야자잎 덮개 - 연기를 가둔다는 것을 형태로 보여준다.
            StructureVisualBuilder.CreateVisualPart(transform, "Cover", PrimitiveType.Cube,
                new Vector3(0f, 1.46f, 0f), new Vector3(1.24f, 0.06f, 0.80f),
                StructureVisualBuilder.PalmFiber, null, "thatch");
        }
    }
}
