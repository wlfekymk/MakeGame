# API_REFERENCE — 공용 API 시그니처 사전

> **⚠ 이 문서는 0.2.51 기준이다. 시그니처를 바꾸면 이 문서도 같이 고쳐라.**

**사용 규칙 (에이전트 필독):**
1. **이 문서를 Read로 통째 읽지 마라.** 610줄 약 9K 토큰이라, 통째로 읽으면 남은 모든 턴에서
   다시 처리되어 아끼려던 것보다 비싸진다. 반드시 **grep으로 필요한 줄만** 꺼낸다.
   - 심볼 하나: `grep -n -A4 "AddItem" Docs/API_REFERENCE.md`
   - 파일 절 전체: `sed -n '/^## Systems\/CraftStation/,/^## /p' Docs/API_REFERENCE.md`
2. 여기 실린 시그니처는 **원본 재확인이 불필요하다** — 그대로 믿고 쓴다.
3. 여기 **없는** 심볼만 원본에서 grep으로 확인하고, 그 심볼을 실제로 쓰게 되면 **이 문서에 추가**한다.

표기: 경로는 `Assets/_Project/Scripts/` 기준. `=> ...`는 식 본문 생략. 필드 초기값은 참고용으로 남겼다.

---

## Player/PlayerInventory.cs — `class PlayerInventory : MonoBehaviour`

| 시그니처 | 요약 |
|---|---|
| `public List<ItemData> startingItemPool` | 시작 지급 풀 |
| `public List<InventoryItem> items` | 소지 목록(직접 수정 시 NotifyInventoryChanged 필요) |
| `public const int DefaultSlotCapacity = 100` | slotCapacity 미설정 시 기본 칸 수 |
| `public int slotCapacity = DefaultSlotCapacity` | |
| `public event System.Action InventoryChanged` | 추가/제거/복원 시 발행(UI 구독용) |
| `public event System.Action<ItemData> AddRejected` | 용량 초과로 거부된 아이템과 함께 발행 |
| `public int SlotCapacity => ...` | 실제 적용 칸 수 상한 |
| `public int UsedSlots => ...` | 사용 중 칸 수(GetStacks 스택 수와 동일) |
| `public int FreeSlots => ...` | 남은 빈 칸 수(음수 없음) |
| `public bool IsFull => ...` | 빈 칸 없음 여부(기존 스택 빈자리는 별도) |
| `public void GrantStartingLoadout()` | 시작 아이템 지급(최초 1회) |
| `public void UseItem(InventoryItem item)` | 1회 사용, 소진 시 제거 |
| `public bool CanCarryToIsland(ItemData itemData, IslandSize destinationSize)` | 목적지 섬 반입 가능 여부 |
| `public void AddItem(ItemData itemData)` | 재료 1개 추가 |
| `public bool TryAddItem(ItemData itemData)` | AddItem과 동일하되 성공 여부 반환 |
| `public InventoryItem AddItemIgnoringCapacity(ItemData itemData)` | 용량 무시 추가(**시작 지급·세이브 복원 전용**) |
| `public InventoryItem AddItemIgnoringCapacity(ItemData itemData, int remainingUses)` | 사용 횟수 지정 복원용 |
| `public InventoryItem AddItemIgnoringCapacity(ItemData itemData, int remainingUses, float spoilAgeSeconds)` | 부패 경과까지 지정(세이브 복원 전용) |
| `public bool CanAccept(ItemData itemData, int count = 1)` | count개 수용 가능 여부(무상태) |
| `public List<InventoryStack> GetStacks()` | 칸 단위 뷰 생성(UI·세이브 압축용) |
| `public void GetStacks(List<InventoryStack> buffer)` | 버퍼 재사용판(매 프레임 UI용) |
| `public void NotifyInventoryChanged()` | items 직접 수정 후 UI 통지 통로 |
| `internal static int CountUsedSlots(List<InventoryItem> items, List<ItemData> kindBuffer, List<int> kindCountBuffer)` | 칸 수 계산 공용 구현 |
| `internal static int SlotsFor(int count, int max)` | count개를 max개 스택으로 담는 칸 수(올림) |
| `public bool RemoveItem(InventoryItem item)` | 특정 인스턴스 제거(인스턴스 선택 가능한 유일 경로) |
| `public InventoryItem FindItem(ItemData itemData)` | 해당 종류 인스턴스 하나 검색 |
| `public InventoryItem FindMostSpoiled(ItemData itemData)` | 가장 상한 인스턴스 검색(없으면 null) |
| `public int GetItemCount(ItemData itemData)` | 소지 개수 |
| `public bool RemoveItems(ItemData itemData, int count)` | count개 제거 |

## Player/InventoryItem.cs

`class InventoryItem` — 소지 아이템 1개의 상태(원본 + 남은 사용 횟수 + 부패 경과).

| 시그니처 | 요약 |
|---|---|
| `public ItemData data` / `public int remainingUses` / `public float spoilAgeSeconds` | |
| `public bool CanSpoil => ...` | 부패 대상 여부 |
| `public float Freshness01 => ...` | 남은 신선도(비부패 아이템은 항상 1) |
| `public FoodSpoilStage SpoilStage => ...` | 부패 단계 |
| `public string FreshnessLabel => ...` | 화면용 단계 문구(비대상은 빈 문자열) |
| `public InventoryItem(ItemData itemData)` | |
| `public bool Use()` | 1회 사용(무제한 아이템은 미소모) |

`class InventoryStack` — 슬롯 1칸의 **읽기 전용 뷰**(PlayerInventory.GetStacks 산출물).

| 시그니처 | 요약 |
|---|---|
| `public ItemData data` / `public int count` | 종류·개수(1 ≤ count ≤ MaxStackSize) |
| `public InventoryItem representative` | 대표 인스턴스(내구도 표시·UseItem용) |
| `public int RemainingUses => ...` | 대표의 남은 횟수(무제한 -1, 없으면 0) |
| `public InventoryItem oldest` | 칸에서 가장 상한 인스턴스 |
| `public bool ShowsFreshness => ...` / `public float Freshness01 => ...` | 신선도 표시 여부/값 |
| `public FoodSpoilStage SpoilStage => ...` / `public string FreshnessLabel => ...` | 부패 단계/문구 |
| `public InventoryStack(ItemData data, int count, InventoryItem representative)` | |

## Player/PlayerSkills.cs — `class PlayerSkills : MonoBehaviour`

| 시그니처 | 요약 |
|---|---|
| `public class SkillProgress { SkillType type; int level = 1; float experience = 0f; }` | 스킬 1개 진행 상태 |
| `public List<SkillProgress> skills` | |
| `public float experiencePerLevel = 100f` / `public int maxLevel = 10` | |
| `public int GetLevel(SkillType type)` | 현재 레벨 |
| `public void AddExperience(SkillType type, float amount)` | 경험치 추가·레벨업(잔여 이월) |
| `public const float HarvestingBonusYieldChancePerLevel = 0.04f` | 채집 Lv당 추가 수확 확률 |
| `public const float CookingRestoreBonusPerLevel = 0.03f` | 요리 Lv당 회복 배율 |
| `public const float PhysicalMoveSpeedBonusPerLevel = 0.01f` | 신체 Lv당 이속 배율 |
| `public const float PhysicalOxygenDrainReductionPerLevel = 0.02f` | 신체 Lv당 산소 소모 감소 |
| `public float GetHarvestingBonusYieldChance()` | 추가 수확 확률(0~1) |
| `public float GetCookingRestoreMultiplier()` | 조리 음식 회복 배율 |
| `public float GetPhysicalMoveSpeedMultiplier()` | 지상 이속 배율 |
| `public float GetPhysicalOxygenDrainMultiplier()` | 잠수 산소 소모 배율 |

`enum SkillType` (Utils/SkillType.cs): `Harvesting, Craftsmanship, Cooking, Physical, Hunting`

## Player/SurvivalStats.cs — `class SurvivalStats : MonoBehaviour`

`enum DamageCause`: `Unknown, Starvation, Sunstroke, Poison, Bleeding, Drowning, Predator, SharkAttack`

| 시그니처 | 요약 |
|---|---|
| `public SurvivalBalanceConfig balanceConfig` | |
| `public const float LowHealthRatio = 0.25f / LowHungerRatio = 0.2f / LowThirstRatio = 0.2f / HighSunstrokeRatio = 0.8f / LowOxygenRatio = 0.25f` | HUD 경고 기준값 |
| `public const float MaxStatValue = 100f` | 공통 상한(0~100 스케일) |
| `public float health = 100f / maxHealth = 100f / hunger = 100f / thirst = 100f` | |
| `public float hungerDecayPerSecond = 0.05f / thirstDecayPerSecond = 0.08f / starvationDamagePerSecond = 1f` | |
| `public float sunstroke = 0f / sunstrokeGainPerSecond = 0.1f / sunstrokeRecoveryPerSecond = 0.2f / sunstrokeDamagePerSecond = 0.5f` | |
| `public bool isPoisoned / isBleeding / hasBrokenBone` | 상태 이상 플래그 |
| `public float poisonDamagePerSecond = 0.8f / bleedingDamagePerSecond = 1.2f` | |
| `public float healthRegenThreshold = 50f / healthRegenPerSecond = 0.5f` | |
| `public float oxygen = 100f / oxygenRecoveryPerSecond = 25f / oxygenDrainPerSecond = 5f` | |
| `public float oxygenDrainMultiplier = 1f` | 산소통 등 런타임 배율 |
| `public bool isHeadInAirPocket = false` | 에어포켓 안 여부 |
| `public float airPocketRecoveryMultiplier = 3f / drowningDamagePerSecond = 3f` | |
| `public float coconutOverdoseThreshold = MaxStatValue` | |
| `public bool AnyDebugCheatActive => ...` | 치트 활성 여부 |
| `public bool IsDead => health <= 0f` | |
| `public DamageCause lastDamageCause` | 마지막 피해 원인(사망 문구용) |
| `public float crisisGraceSeconds = 8f` / `public int CrisisCount => ...` | 위기 통계 |
| `public void Tick(float deltaTime, bool isInShade, bool isUnderwater = false, bool isDaytime = true, ...)` | 매 주기 생존 수치 갱신 |
| `public void TakeDamage(float amount, DamageCause cause = DamageCause.Unknown)` | 체력 감소(0 미만 없음) |
| `public void Heal(float amount)` / `public void ConsumeFood(float amount)` / `public void ConsumeWater(float amount)` | 회복류 |
| `public void ConsumeCoconutWater(float amount, float overdoseThreshold = -1f)` | 과음 부작용 있는 수분 섭취 |
| `public void ApplyPoison() / CurePoison() / ApplyBleeding() / BandageBleeding() / ApplyBrokenBone() / HealBrokenBone()` | 상태 이상 부여/치료 |

## Player/PlayerController.cs (공개 필드·키만)

| 시그니처 | 요약 |
|---|---|
| `public float moveSpeed = 5f / brokenBoneSpeedMultiplier = 0.4f / jumpForce = 6f / gravity = -20f` | |
| `public float waterLevel = 0f / swimSpeed = 3f / swimVerticalSpeed = 2f / buoyancy = 1f` | |
| `public bool followOceanWaves = true` / `public float oceanWaveFollowScale = 0.75f` | RaftStructure.waveHeaveScale과 동일해야 함 |
| `public KeyCode diveKey = KeyCode.LeftControl` / `diveKeyAlt = KeyCode.LeftShift` | 잠수 |
| `public KeyCode dodgeKey = KeyCode.X` | 회피 |
| `public float dodgeCooldownSeconds = 1.8f / dodgeSpeed = 13f / dodgeDurationSeconds = 0.28f / dodgeRecoverySeconds = 0.22f / dodgeRecoverySpeedMultiplier = 0.35f / dodgeHungerCost = 1.5f / dodgeThirstCost = 2f` | |
| `public int throwMouseButton = 1` / `public KeyCode throwKey = KeyCode.T` | 투척 |
| `public float throwCooldownSeconds = 0.8f / throwSpeed = 26f / underwaterThrowSpeedMultiplier = 0.5f` | |
| `public KeyCode fishingKey = KeyCode.Z` | 낚시 |
| `public PlayerSkills playerSkills` / `public SurvivalStats survivalStats` / `public Transform cameraTransform` | |
| `public float lookSensitivity = 2f` / `public int lookSettleFrames = 2` | |
| `public const string SwimFinsItemName = "오리발"` / `OxygenTankItemName = "산소통"` | 패시브 장비 itemName |
| `public float finSwimSpeedMultiplier = 1.4f / oxygenTankDrainMultiplier = 0.5f` | |

---

## Utils/ItemData.cs — `class ItemData : ScriptableObject`

| 시그니처 | 요약 |
|---|---|
| `public string itemName / description` / `public Sprite icon` | |
| `public int maxUses = 1` | -1 = 무제한 |
| `public const int DefaultMaxStackSize = 20` / `public int maxStackSize = DefaultMaxStackSize` | |
| `public bool blockedFromLargeIslandsByCurrent = false` | |
| `public MaterialFamily materialFamily = MaterialFamily.None` | |
| `public float hungerRestoreAmount = 0f / thirstRestoreAmount = 0f` | |
| `public bool isRawFood = false` / `public ItemData cookedResult` | |
| `public bool isCoconutWaterSource = false` | |
| `public float spoilDays = 0f` | 0 = 절대 안 상함 |
| `public ItemData smokedResult` | 훈연 결과물 |
| `public bool isPlaceable = false` / `public GameObject placementPrefab` | |
| `public bool isWeapon = false` / `public float weaponDamage = 10f` | |
| `public bool curesBleeding / curesPoison / curesBrokenBone` | |
| `public bool IsUnlimited => maxUses < 0` | |
| `public bool IsStackable => maxUses <= 1` | |
| `public int MaxStackSize => ...` | 스택 불가 아이템은 1 |
| `public bool IsConsumable => ...` | 회복·치료 효과가 있으면 true |

## Utils/ItemDataRegistry.cs — `class ItemDataRegistry : ScriptableObject`

| 시그니처 | 요약 |
|---|---|
| `public List<ItemData> allItems` | |
| `public static ItemDataRegistry LoadFromResources()` | Resources에서 레지스트리 로드 |
| `public void ValidateEntries(string context)` | 정합성 검사(경고 로그만, 수정 없음) |

## Utils/CraftingRecipe.cs — `class CraftingRecipe : ScriptableObject`

| 시그니처 | 요약 |
|---|---|
| `public class MaterialRequirement { ItemData item; int quantity = 1; }` | |
| `public string recipeName / description` | |
| `public List<MaterialRequirement> requiredMaterials` | |
| `public ItemData resultItem` / `public int resultQuantity = 1` | |
| `public SkillType requiredSkill = SkillType.Craftsmanship` / `public int requiredSkillLevel = 1` | |
| `public float experienceReward = 10f` | |

## Utils/StructureType.cs

`enum StructureType`: `Campfire = 0, Shelter = 1, WaterStill = 2, Workbench = 3, Furnace = 4, Loom = 5` — 세이브 복원 매핑 축. **값 변경·중간 삽입 금지.**

## Utils/IslandSize.cs

`enum IslandSize`: `Small, Medium, Large, ExtraLarge`

## Utils/IslandArchetype.cs

`enum IslandArchetype` (8종): `Tropical, Rocky, Sandy, Jungle, Volcanic, Atoll, Marsh, Cliff`

`struct IslandArchetypeProfile` — 읽기 전용 정적 표의 원소. 필드: `archetype, displayName, palmScale, bushScale, tuftScale, vegetationCapScale, rockScale, driftScale, rockCoverage, grassLineTScale, grassLineTOffset, grassLineTMin, grassLineTMax, grassDensityScale, grassHeightScale, groundColor, sandColor, heightScaleMul, plateauPowMul, terrainNoiseMul` (전부 public)

`static class IslandArchetypes`:

| 시그니처 | 요약 |
|---|---|
| `public const int Count = 8` | enum 길이와 일치 필수 |
| `public static IslandArchetypeProfile Get(IslandArchetype archetype)` | 값 복사 반환 |
| `public static string DisplayName(IslandArchetype archetype)` | |
| `public static int SeedKey(int worldSeed, int islandId)` | 난수 소비 0의 순수 해시 |
| `public static IslandArchetype For(int worldSeed, int islandId, IslandSize size)` | 순수 함수(난수 미소비) |
| `public static IslandArchetype FromSeedKey(int seedKey, IslandSize size, bool isStartIsland)` | |
| `public static IslandSize SizeFromRadius(float radius)` | 반지름 → 규모 역산 |
| `public static void LogWorldDistributionOnce(int worldSeed)` | 분포 1회 로그 |

## Utils/IslandInstance.cs — `class IslandInstance`

| 시그니처 | 요약 |
|---|---|
| `public int islandId` / `public IslandSize size` / `public Vector3 mapPosition` | |
| `public bool isDiscovered / isStartingIsland` / `public GameObject placeholderObject` | |
| `public IslandArchetype GetArchetype(int worldSeed)` | 결정적 아키타입 |
| `public IslandArchetypeProfile GetArchetypeProfile(int worldSeed)` | 편의 래퍼 |

## Utils/SeededRandomExtensions.cs — `static class SeededRandomExtensions`

| 시그니처 | 요약 |
|---|---|
| `public static System.Random CreateForSalt(int worldSeed, int salt)` | 결정적 rng 생성 |
| `public static System.Random CreateForIsland(int worldSeed, int islandIndex)` | salt = islandId |
| `public static float NextFloat(this System.Random rng, float min, float max)` | [min, max) |
| `public static int NextInt(this System.Random rng, int minInclusive, int maxExclusive)` | [min, max) |
| `public static float NextValue01(this System.Random rng)` | [0, 1) |
| `public static Vector2 NextInsideUnitCircle(this System.Random rng)` | 원판 내 균등 분포 |

---

## Systems/CombatSystem.cs — `static class CombatSystem` (정적 유틸, 씬에 없음)

| 시그니처 | 요약 |
|---|---|
| `public const string RefinedPrefix = "정제 "` | 뒤 공백 포함 |
| `public const string KnifeItemName = "칼" / HatchetItemName = "손도끼" / SpearItemName = "창"` | |
| `public const string RefinedKnifeItemName / RefinedHatchetItemName / RefinedSpearItemName` | RefinedPrefix + 기본명 |
| `public const float ThrowDamageRatio = 0.78f` | 투척 = 근접 × 비율(반올림) |
| `public const float RefinedHarvestYieldMultiplier = 2f` | 정제 도구 수확 배율 |
| `public static bool IsRefined(ItemData data)` | itemName 접두어 판정 |
| `public static bool IsThrowable(ItemData data)` | 창 계열만 true |
| `public static float GetThrowDamage(ItemData data)` | weaponDamage 파생 |
| `public static int GetRefinedBonusYield(int baseYieldPerHarvest)` | bonusYieldPerHarvest 가산량 |
| `public static InventoryItem FindThrowable(PlayerInventory inventory)` | 최고 피해 창 선택 |

## Systems/CraftStation.cs — `class CraftStation : MonoBehaviour`

`enum CraftStationKind`: `Workbench = 0, Furnace = 1, Loom = 2` — 값 변경·중간 삽입 금지.

| 시그니처 | 요약 |
|---|---|
| `public const string WorkbenchKitItemName = "제작대키트" / FurnaceKitItemName = "용광로키트" / LoomKitItemName = "베틀키트"` | |
| `public const string WorkbenchDisplayName = "제작대" / FurnaceDisplayName = "용광로" / LoomDisplayName = "베틀"` | |
| `public const float DefaultUseRadius = 4f` | interactionDistance와 동일 |
| `public CraftStationKind kind` / `public float useRadius = DefaultUseRadius` | |
| `public static IReadOnlyList<CraftStation> Active => ...` | 씬 내 시설 목록 |
| `public static bool IsNear(Vector3 worldPosition, CraftStationKind kind)` | 제작대 요구 판정의 유일 소스 |
| `public static string GetDisplayName(CraftStationKind kind)` | |
| `public static bool TryGetKindForKitItem(string itemName, out CraftStationKind kind)` | 키트 → 시설 종류 |

## Systems/FoodSpoilage.cs — `class FoodSpoilage : MonoBehaviour`

`enum FoodSpoilStage`: `None = 0, Fresh = 1, Spoiling = 2, Rotten = 3` — 값은 끝에만 추가.

| 시그니처 | 요약 |
|---|---|
| `public const float RawFoodSpoilDays = 1f / CookedFoodSpoilDays = 3f` | 부패 소요 일수 |
| `public const float FallbackSecondsPerDay = 600f` | 시계 부재 시 하루 길이 |
| `public const float SpoilingThreshold01 = 0.5f / RottenThreshold01 = 0.15f` | 단계 경계 |
| `public const float SpoilingRestoreMultiplier = 0.6f / RottenRestoreMultiplier = 0.25f` | 허기 회복 배율 |
| `public const float SpoilingExtraPoisonChance = 0.10f / RottenExtraPoisonChance = 0.50f` | 가산 식중독 확률 |
| `public const string EmergencyRationItemName = "비상식량"` / `SmokedItemNamePrefix = "훈제"` | |
| `public bool spoilageEnabled = true` / `public float spoilRateMultiplier = 1f / tickInterval = 1f` | |
| `public static FoodSpoilage Instance { get; private set; }` | null 가능 — 항상 확인 |
| `public static float SecondsPerDay { get; }` | |
| `public static float GetSpoilDays(ItemData data)` | 0이면 절대 안 상함 |
| `public static float GetSpoilLifetimeSeconds(ItemData data)` | |
| `public static bool CanSpoil(ItemData data)` | |
| `public static float GetFreshness01(InventoryItem item)` | 비대상은 항상 1 |
| `public static FoodSpoilStage GetStage(InventoryItem item)` | |
| `public static FoodSpoilStage GetStageForFreshness(float freshness01)` | |
| `public static float GetRestoreMultiplier(FoodSpoilStage stage)` | 허기만(갈증 미적용) |
| `public static float GetExtraPoisonChance(FoodSpoilStage stage)` | |
| `public static string GetStageLabel(FoodSpoilStage stage)` | 비대상은 빈 문자열 |

## Systems/Smoker.cs — `class Smoker : MonoBehaviour`

| 시그니처 | 요약 |
|---|---|
| `public const string SmokerKitItemName = "훈연기키트" / DisplayName = "훈연기"` | |
| `public const string RawMeatItemName = "생고기" / SmokedMeatItemName = "훈제육" / RawFishItemName = "생선" / SmokedFishItemName = "훈제생선"` | |
| `public const string FuelItemName = "나뭇가지" / FireStarterItemName = "파이어스타터" / LighterItemName = "라이터"` | |
| `public float smokeSecondsPerItem = 75f / fuelSecondsPerUnit = 60f` / `public int capacity = 8` | |
| `public float deliveryRadius = 8f / smokingExperience = 12f` | |
| `public static IReadOnlyList<Smoker> Active => ...` | 씬 내 훈연기 목록 |
| `public IReadOnlyList<ItemData> PendingRaw => ...` / `ReadyOutput => ...` | 세이브용 읽기 전용 |
| `public float ProgressSeconds => ...` | 진행 시간(세이브용) |
| `public Campfire Fire => ...` | 연료·점화 소유자(null 가능) |
| `public void AdvanceSmoking(float deltaTime)` | 호출자는 Campfire.Tick 하나 |
| `public bool TryInsert(PlayerInventory inventory, PlayerSkills skills, ItemData rawFood)` | 재료 걸기 |
| `public static bool TryGetSmokedResult(ItemData rawFood, out ItemData smoked)` | 재료 → 결과물 |
| `public void ApplySavedState(List<ItemData> savedPending, List<ItemData> savedOutput, float savedProgress)` | 세이브 복원 |

## Systems/FarmPlot.cs — `class FarmPlot : MonoBehaviour`

`enum FarmCropKind`: `PalmSapling = 0, Seaweed = 1, Herb = 2`

`struct/class CropDefinition` — readonly 필드: `Kind, DisplayName, SeedItemName, HarvestItemName, GrowSeconds, MinYield, MaxYield, PlantCount, HarvestExperience, MatureHeight, FoliageColor, FruitColor`

| 시그니처 | 요약 |
|---|---|
| `public const string FarmPlotKitItemName = "밭키트" / DisplayName = "밭" / WaterItemName = "생수"` | |
| `public const float RainGrowthBonus = 1.0f / WateredGrowthBonus = 0.6f / WaterDurationSeconds = 300f` | 성장 가속 |
| `public static IReadOnlyList<FarmPlot> Active => ...` | 씬 내 밭 목록 |
| `public bool HasCrop => ...` / `public FarmCropKind CropKind => ...` | |
| `public float GrowthSeconds => ...` / `WateredSecondsRemaining => ...` | 세이브용 |
| `public bool IsWatered => ...` / `public float Progress01 { get; }` / `public bool IsRipe => ...` | |
| `public string CropDisplayName => ...` / `public ItemData HarvestItem => ...` | |
| `public bool TryGetYieldRange(out int minYield, out int maxYield)` | 프롬프트용 수량 범위 |
| `public static bool TryFindPlantableSeed(PlayerInventory inventory, out ItemData seed, out FarmCropKind kind)` | 첫 씨앗 탐색 |
| `public static bool TryGetKindForSeedItem(string itemName, out FarmCropKind kind)` | |
| `public static string GetCropDisplayName(FarmCropKind kind)` / `public static float GetGrowDays(FarmCropKind kind)` | |
| `public static bool IsRainBoostActive => ...` | |
| `public bool CanWater(PlayerInventory inventory)` / `CanHarvest(PlayerInventory inventory)` | 사전 판정 |
| `public void Interact(PlayerInventory inventory, PlayerSkills skills)` | E 한 번을 상태별 분기 |
| `public bool TryPlant(...)` / `TryWater(...)` / `TryHarvest(PlayerInventory inventory, PlayerSkills skills)` | 실패 시 재료 미소모 |
| `public void ApplySavedState(bool savedHasCrop, int savedCropKind, float savedGrowthSeconds, ...)` | 세이브 복원 |

## Systems/FishingSystem.cs — `class FishingSystem : MonoBehaviour`

`enum FishingPhase`: `Idle, Casting, Waiting, Biting` · `enum CastFailure`: `None, NoRod, NotAimingAtWater, NotWaterThere, HeadUnderwater`

| 시그니처 | 요약 |
|---|---|
| `public const string RodItemName = "낚싯대" / BaitItemName = "미끼" / FishItemName = "생선" / SeaweedItemName = "해조류" / DriftwoodItemName = "나뭇가지"` | |
| `public float minCastDistance = 4f / maxCastDistance = 18f / castFlightSeconds = 0.55f / castArcHeight = 2.2f` | |
| `public float plainBiteDelayMin = 5f / plainBiteDelayMax = 12f / baitedBiteDelayMin = 2.5f / baitedBiteDelayMax = 6f` | |
| `public float hookWindowSeconds = 1.2f / plainHookChance = 0.45f / baitedHookChance = 0.7f / huntingLevelSuccessBonus = 0.03f` | |
| `public float fishingExperience = 15f / fishLootChance = 0.78f / seaweedLootChance = 0.15f / lineBreakDistance = 30f / resultBannerSeconds = 2.2f` | |
| `public static FishingSystem Active { get; private set; }` | 프롬프트 UI의 유일 통로 |
| `public bool IsLineOut => ...` / `public FishingPhase Phase => ...` | |
| `public KeyCode FishingKey => ...` | PlayerController.fishingKey 읽기 |
| `public void Configure(PlayerController controller, PlayerInventory playerInventory, ...)` | 소유자가 참조 주입 |
| `public void HandleFishingKey()` | 키 눌린 프레임에 PlayerController가 호출 |
| `public bool TryDescribeCastPrompt(string keyLabel, out string headline, out string detail, out bool blocked)` | 프롬프트 문구 |
| `public string GetHintSuffix(string keyLabel)` | 꼬리말(예: " · [Z] 낚시") |

## Systems/BossCreature.cs (공개만)

`enum BossKind`: `GiantShark = 0, GiantMoray = 1, AbyssHorror = 2` — 값 중간 삽입 금지(세이브 축).

| 시그니처 | 요약 |
|---|---|
| `public const int KindCount = 3` | 세이브 배열 길이 |
| `public static bool IsDefeated(int kind)` / `HasTrophy(int kind)` | 진행도 조회 |
| `public static int TrophyCount { get; }` / `DefeatedCount { get; }` | 0~3 |
| `public static bool AllTrophiesCollected => ...` | 세 번째 엔딩 조건 |
| `public static string GetDisplayName(int kind)` / `GetTrophyName(int kind)` | 범위 밖은 빈 문자열 |
| `public static void ApplySavedProgress(IList<bool> defeated, IList<bool> trophies)` | SaveLoadController 전용 |
| `public static bool TryEnsureMeshes(int kind)` | 프레임당 1회 프로브 |
| `public static BossCreature Spawn(int kind, Vector3 position, float yawDegrees, ...)` | BossSpawner 전용(메시 미로드 시 null) |
| `public Vector3 Home => ...` | |
| `public static GameObject SpawnTrophy(int kind, Vector3 position, Transform parent, float seaLevel)` | 트로피 수거 지점 생성 |
| `public void Setup(int trophyKind, AirlinerSalvagePoint point)` | (트로피 컴포넌트) SpawnTrophy가 호출 |

## Systems/AirlinerSalvagePoint.cs — `class AirlinerSalvagePoint : MonoBehaviour`

| 시그니처 | 요약 |
|---|---|
| `public struct LootEntry { string itemName; int count; LootEntry(string, int) }` | 고정 지급표 한 줄 |
| `public string displayName = "부품 더미"` / `public LootEntry[] loot` | |
| `public bool HasLoot => ...` | 수거 잔여 여부 |
| `public bool TryCollect(PlayerInventory inventory)` | 1회 한정 수거 |
| `public static List<ItemData> BuildLootList(LootEntry[] entries, string logTag)` | 지급표 → ItemData 목록 |
| `public static int GrantPending(List<ItemData> pending, PlayerInventory inventory)` | TryAddItem으로 지급, 들어간 것만 제거 |

## Systems/ResourceNode.cs (공개만) — `class ResourceNode : MonoBehaviour`

`enum HarvestFailure`: `None = 0, Depleted = 1, MissingTool = 2, NoInventory = 3, NoYieldItem = 4, InventoryFull = 5`

| 시그니처 | 요약 |
|---|---|
| `public ItemData yieldItem` / `public int yieldPerHarvest = 1 / maxHarvestCount = 3 / remainingHarvestCount = 3` | |
| `public float harvestExperience = 5f / respawnSeconds = 60f` | |
| `public bool requiresTool = false` / `public ItemData requiredTool` | |
| `public ItemData bonusTool` / `public int bonusYieldPerHarvest = 0` | 정제 도구 가산 |
| `public int islandIndex = -1 / spawnOrder = -1 / stableKey = 0` | 결정적 식별 |
| `public bool CanHarvest => ...` | 남은 횟수 존재 여부 |
| `public static event System.Action<ResourceNode, HarvestFailure> HarvestFailed` | 실패 순간 발생 |
| `public static event System.Action<ResourceNode> Harvested` | 성공 순간 발생 |
| `public void Tick(float deltaTime)` | 재생 진행 |
| `public bool Harvest(PlayerInventory inventory, PlayerSkills skills)` | 채집 실행 |
| `public int GetEffectiveYield(PlayerInventory inventory)` | 실제 1회 수확량 |
| `public HarvestFailure GetHarvestFailure(PlayerInventory inventory)` | 사전 거부 사유(None = 가능) |

## Systems/CraftingSystem.cs (공개만) — `class CraftingSystem : MonoBehaviour`

| 시그니처 | 요약 |
|---|---|
| `public PlayerInventory inventory` / `public PlayerSkills skills` | |
| `public int CraftedRecipeCount => ...` | 제작 성공 종류 수(통계용) |
| `public bool HasCrafted(CraftingRecipe recipe)` | null이면 false |
| `public static bool TryGetRequiredStation(CraftingRecipe recipe, out CraftStationKind kind)` | 시설 요구 여부 |
| `public bool HasRequiredStation(CraftingRecipe recipe)` | 시설 불필요 시 항상 true |
| `public bool IsMissingRequiredStation(CraftingRecipe recipe, out CraftStationKind kind)` | |
| `public bool CanCraft(CraftingRecipe recipe)` | |
| `public bool TryCraft(CraftingRecipe recipe)` | 재료 소모 + 지급 + 경험치 |

## Systems/TerrainSampler.cs — `static class TerrainSampler`

| 시그니처 | 요약 |
|---|---|
| `public static Vector3 SnapToGround(Vector3 position, float rayStartHeight = 60f, float rayLength = 120f)` | 위→아래 레이로 섬 지형 y 스냅(이름 "Island_" 콜라이더만 지형 인정) |

## Systems/SeabedGenerator.cs (공개만) — `static class SeabedGenerator`

| 시그니처 | 요약 |
|---|---|
| `public const string SeabedNamePrefix = "Seabed_"` | "Island_" 미시작이 TerrainSampler 안전 전제 |
| (스커트 항목) `public Transform transform; Vector3 center; float innerRadius; float outerRadius; float[] rimHeights;` | 스커트 폭 = clamp(R × 0.6, 30, 90) |
| `public static void Build(GameObject islandObject, Mesh islandMesh, float radius)` | 섬 메시 생성 직후 동기 호출 |
| `public static bool TrySampleSeabed(Vector3 worldPos, out float seabedY)` | 산호/해초 분포기용 샘플러 |

## Systems/OceanWaves.cs (공개만) — `class OceanWaves : MonoBehaviour` (파도의 단일 소스)

| 시그니처 | 요약 |
|---|---|
| `public const int ComponentCount = 4` | 셰이더 float4 제약으로 고정 |
| `public float stormAmplitudeScale = 2.9f / stormSpeedScale = 1.45f / roughnessFollowSeconds = 6f` | |
| `public static OceanWaves Active { get; private set; }` | 없으면 null(샘플러는 기본값 동작) |
| `public static float SeaLevel => ...` | 평균 해수면 y |
| `public static float Roughness01 => ...` | 0 = 잔잔, 1 = 폭풍 |
| `public static float WaveTime => Time.time` | 셰이더 _MG_WaveTime과 동기 |
| `public static float SampleHeight(Vector3 worldPos)` / `SampleHeight(Vector3 worldPos, float time)` | 수면 절대 높이(m) |
| `public static float SampleWaveOffset(Vector3 worldPos)` | 파도 편차(얕은 물 감쇠 포함) |
| `public static float SubmergenceScale(Vector3 worldPos)` | 쇄파 감쇠 계수 0~1 |
| `public static float SampleWaveOffset(float x, float z, float time)` | 원 파형 본체(감쇠 없음) |
| `public static Vector3 SampleNormal(Vector3 worldPos)` / `SampleNormal(Vector3 worldPos, float time)` | 수면 법선 |
| `public static void SampleSlope(float x, float z, float time, out float slopeX, out float slopeZ)` | 해석적 기울기 |

## Systems/WeatherSystem.cs (공개만) — `class WeatherSystem : MonoBehaviour`

| 시그니처 | 요약 |
|---|---|
| `public SurvivalBalanceConfig balanceConfig` | |
| `public float minClearSeconds = 90f / maxClearSeconds = 240f / minRainSeconds = 40f / maxRainSeconds = 100f` | 단계 길이 |
| `public float rainDimFactor = 0.55f / rainHeightAboveTarget = 15f / rainFadeSeconds = 5f / rainEmissionRate = 460f` | |
| `public Vector2 rainWind = (2.6f, 1.4f)` / `public bool enableRainSplashes = true` / `public int rainStreakTilesX = 16` | |
| `public bool enableNearRainLayer = true` / `public float nearRainHeightAboveTarget = 5.5f / nearRainEmissionRate = 95f / nearRainAlpha = 0.4f` | |
| `public bool stopRainIndoors = true / shelterRadiusCountsAsIndoors = true` / `public float indoorCheckInterval = 0.25f / indoorFadeSeconds = 0.8f` / `public bool enableRainCollision = true` | 실내 판정 |
| `public bool IsRaining { get; private set; }` | 즉시 뒤집히는 논리값 |
| `public float RainIntensity01 { get; private set; }` | 연출 세기(페이드 반영) |
| `public float preStormSeconds = 35f / preStormRoughness = 0.45f / seaRoughnessFadeSeconds = 8f` | |
| `public float SeaRoughness01 { get; private set; }` | OceanWaves가 읽는 단일 값 |
| `public Color rainFogColor` / `public float rainFogDensity = 0.006f` | |
| `public float rainWaterStillMultiplier = 3f / rainCampfireFuelMultiplier = 1.5f / rainTargetRescanSeconds = 3f` | |
| `public static WeatherSystem Active { get; private set; }` | Bootstrap이 런타임 생성 |
| `public float ShelteredFactor01 => ...` | 1 = 실외, 0 = 실내 |
| `public bool IsIndoors => ...` | 연출 전용 읽기값 |

## Systems/SurvivalClock.cs — `class SurvivalClock : MonoBehaviour`

| 시그니처 | 요약 |
|---|---|
| `public SurvivalBalanceConfig balanceConfig` | |
| `public float secondsPerDay = 600f / elapsedSeconds = 0f` | |
| `public int ElapsedDays => ...` | 0일차부터 |
| `public float TimeOfDay01 => ...` | 0 = 자정, 0.5 = 한낮 |
| `public bool IsDaytime => ...` | 0.25~0.75 |
| `public float sunsetWarningTimeOfDay = 0.65f` / `public int sunsetWarningDay = 0` | |
| `public event System.Action SunsetWarningRaised` | 세션당 1회 |
| `public bool SunsetWarningFired { get; private set; }` / `public float SunsetWarningTime { get; private set; } = -1f` | |

## Systems/EffectBuilder.cs — `static class EffectBuilder` (코드 전용 파티클 유틸)

| 시그니처 | 요약 |
|---|---|
| `public static readonly Color FoodOrange / SunstrokeGold / NeutralGray / DangerRed / PalmFiber` | 이펙트 팔레트 |
| `public static Material GetParticleMaterial()` | 전 파티클 공유 머티리얼(캐시) |
| `public static ParticleSystem CreateCampfireFlame(Transform parent, Vector3 localPosition)` | |
| `public static ParticleSystem CreateCampfireSmoke(Transform parent, Vector3 localPosition)` | |
| `public static ParticleSystem CreateWreckSmoke(Transform parent, Vector3 localPosition)` | |
| `public static void PlayHarvestPop(GameObject node)` | 채집 성공 팝(노드 표면색 사용) |
| `public static void PlayHitBurst(Vector3 worldPosition)` | 피격 팝 |
| `public static ParticleSystem CreateRainSplashes(Transform parent)` | |
| `public static ParticleSystem CreateDiveBubbles(Transform parent)` | |
| `public static ParticleSystem CreateRainDripDrops(Transform parent)` / `CreateRainDripSplashes(Transform parent)` | 낙수 연출 |

## Systems/StructureVisualBuilder.cs — `static class StructureVisualBuilder`

| 시그니처 | 요약 |
|---|---|
| `public static readonly Color IslandSand / Driftwood / BambooCulm / WeatheredStone / SalvageMetal / PalmFiber / FrondGreen / MeadowGreen / DangerRed / SupplyKhaki / SalvageMarkerWhite` | 아트 팔레트(단일 출처) |
| `public const float DefaultSmoothness = 0.15f` | 프리미티브 기본 광택 |
| `public static GameObject CreateVisualPart(Transform parent, string name, PrimitiveType primitiveType, ...)` | 시각 전용 파츠(2개 오버로드 — 공유 머티리얼판 있음) |
| `public const string RuntimeMaterialPrefix = "MG~"` | 런타임 머티리얼 이름 접두어 |
| `public static Material CreateColorMaterial(Color color, string textureName = null)` | |
| `public static GameObject CreateLashedPost(Transform parent, string name, Vector3 localPosition, ...)` | 밧줄 결속 기둥 |
| `public static GameObject CreateMeshPart(Transform parent, string name, Mesh mesh, ...)` | 콜라이더 없는 절차 메시 파츠 |
| `public class WorldMeshBuilder` | 절차 메시 조립기(아래 멤버) |
| `.AddChunk(Vector3 center, Vector3 size, int seed, float jitter, int subdivisions)` | 각진 덩어리 |
| `static Mesh Chunk(string name, Vector3 size, int seed, float jitter, int subdivisions)` | 단일 덩어리 즉석 생성(캐시할 것) |
| `.AddBox(Vector3 center, Vector3 size, Quaternion rotation)` / `.AddTube(Vector3[] centers, float[] radii, int sides, bool capStart, bool capEnd, float uvTile)` | |
| `.AddQuad(a, b, c, d, reference, doubleSided)` / `.AddFace(a, b, c, reference)` | |
| `.Finish(string name)` → `Mesh` | 반드시 캐시·재사용 |

## UI/UIBuilder.cs — `static class UIBuilder` (주요 Create*)

| 시그니처 | 요약 |
|---|---|
| `public static Canvas CreateCanvas(string name, int sortOrder = 0)` | Overlay 캔버스 |
| `public static RectTransform CreatePanel(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax, Color color, bool addTopBorder = false)` | 단색 패널 |
| `public static Text CreateText(Transform parent, string name, string content, int fontSize, Color color, TextAnchor alignment)` | |
| `public static RectTransform CreateIcon(Transform parent, string name, float size, Color backgroundColor, string letter)` | 정사각 아이콘 |
| `public static void ApplyItemIcon(RectTransform iconRt, ItemData item)` | 아이콘에 스프라이트 적용 |
| `public static Image CreateProgressBar(Transform parent, string name, Color backgroundColor, Color fillColor)` | 0~1 가로 막대 |
| `public static Button CreateButton(Transform parent, string name, string label, UnityAction onClick)` | |
| `public static Slider CreateSlider(Transform parent, string name, float minValue, float maxValue, float value, ...)` | |
| `public static RectTransform CreateWindow(Transform parent, string name, float width, float height)` | 한 점 앵커 + 고정 크기 창 |
| `public static RectTransform CreateTitleBar(RectTransform window, string title, float height)` | |
| `public static Button CreateCloseButton(RectTransform titleBar, UnityAction onClick)` | 우상단 X |
| `public static SlotVisual CreateItemSlot(Transform parent, string name, bool withDurabilityBar = false)` | 표준 격자 칸 |
| `public static RectTransform CreateSlotGrid(RectTransform window, string name, int columns, float slotSize, float spacing, float topOffset)` | 고정 열 격자 |

## Systems/GameSettings.cs — `static class GameSettings` (PlayerPrefs 전역 설정)

`enum GameDifficulty`: `Easy = 0, Normal = 1, Hard = 2` — 값 재배치 금지, 끝에만 추가.

| 시그니처 | 요약 |
|---|---|
| `public const float MinMouseSensitivity = 0.3f / MaxMouseSensitivity = 3f / DefaultMouseSensitivity = 1f` | |
| `public const int SaveSlotCount = 3` | 슬롯 1~3 |
| `public static event System.Action Changed` | 이산 설정 변경 시 발행 |
| `public static float MouseSensitivity { get; set; }` | lookSensitivity에 곱해지는 배율 |
| `public static bool InvertLookY { get; set; }` | |
| `public static GameDifficulty Difficulty { get; set; }` | |
| `public static int SaveSlot { get; set; }` | |
| `public static void CycleDifficulty()` | 쉬움→보통→어려움 순환 |
| `public static float HungerDrainMultiplier => ...` / `ThirstDrainMultiplier => ...` | 난이도 배율 |
| `public static float ThreatDamageMultiplier { get; }` | 위협 피해 배율 |
| `public static string DifficultyLabel(GameDifficulty value)` | 한글 표기 |
| `public static float ClampSensitivity(float value)` / `public static int ClampSlot(int slot)` | |

## Systems/RaftStructure.cs — `class RaftStructure : MonoBehaviour` (뗏목 상태 소유자)

`enum RaftPart` (비트 플래그): `None = 0, Sail = 1<<0, Rudder = 1<<1, Anchor = 1<<2, Oar = 1<<3, Motor = 1<<4`
`enum RaftBaseTileKind`: `None = 0, Wood = 1, Buoy = 2, Barrel = 3` (부력: Wood 1.0 / Barrel 2.0)

**★ 갑판 계약 8개 — 이름·시그니처를 바꾸면 갑판 위 건축(BuildingSystem)이 통째로 죽는다:**
`Active` · `DeckRoot` · `PlacedStructures` · `DeckSurfaceName` · `HasDeck` · `DeckLocalSize` · `DeckTopLocalY` · `DeckRebuilt`

| 시그니처 | 요약 |
|---|---|
| `public const float BaseTilePitch = 2f` | 격자 칸 간격(m) = 실물 바닥판 발자국 |
| `public const float DeckLength / DeckWidth / DeckSurfaceY` | 격자에서 유도한 갑판 치수 |
| `public const int BaseGridColumns = 2 / BaseGridRows = 4 / MaxBaseTiles = 8` | 격자 치수 단일 출처 |
| `public const int SeaworthyTileCount = 4 / OceanReadyTileCount = 6` | 항해/탈출 최소 칸 수 |
| `public const string PlacedStructuresName = "PlacedStructures"` | 갑판 재생성이 지우지 않는 유일 자식 |
| `public const string DeckSurfaceName = "DeckSurface"` | 【계약】 갑판 윗면 콜라이더 이름(DeckRoot의 자식) |
| `public float shoreOutwardOffset = 0.2f / refreshInterval = 0.2f` / `public bool waveMotionEnabled = true` | |
| `public float waveHeaveScale = 0.75f` | PlayerController.oceanWaveFollowScale과 동일 필수 |
| `public float maxHeaveMeters = 1.2f / maxTiltDegrees = 9f / waveMotionDamping = 6.5f` | |
| `public static RaftStructure Active => ...` | 【계약】 씬의 뗏목(없으면 null) |
| `public Transform DeckRoot { get; }` | 【계약】 갑판 위 부착 부모 |
| `public Transform PlacedStructures { get; }` | 【계약】 건축물 전용 컨테이너 |
| `public bool HasDeck { get; }` | 【계약】 온전한 갑판 여부 |
| `public float DeckTopLocalY => DeckSurfaceY` | 【계약】 갑판 윗면 로컬 y |
| `public Vector2 DeckLocalSize { get; }` | 【계약】 실제 깔린 갑판 크기(없으면 (0,0)) |
| `public event System.Action DeckRebuilt` | 【계약】 갑판 재생성 시 발생 |
| `public event System.Action ProgressChanged` | 진행 상태 변경 시 발생 |
| `public bool Exists => ...` / `public int BaseTileCount => ...` / `public RaftPart InstalledParts => ...` | |
| `public RaftBaseTileKind GetBaseTileKind(int index)` / `public bool HasFloorAt(int index)` | |
| `public int FloorTileCount { get; }` / `public int NextFloorlessTileIndex { get; }` | -1 = 없음 |
| `public static float GetBuoyancy(RaftBaseTileKind kind)` / `public float TotalBuoyancy { get; }` | 부력 단일 출처 |
| `public static string GetBaseTileKindName(RaftBaseTileKind kind)` / `GetPartName(RaftPart part)` | UI 문구 단일 출처 |
| `public bool HasPart(RaftPart part)` / `public bool HasPropulsion => ...` | 노/모터/돛+키 중 하나 |
| `public bool IsSeaworthy => ...` / `public bool IsOceanReady => ...` | 항해/배 엔딩 판정 |
| `public float GetOverallProgress()` | 0~1 |
| `public string DescribeState()` | 한 줄 요약(HUD/퀘스트 공유) |
| `public bool AddBaseTile()` / `AddBaseTile(RaftBaseTileKind kind)` / `AddFloorTile()` | 꽉 차면 false |
| `public void SetBaseTileCount(int count)` | |
| `public bool InstallPart(RaftPart part)` / `RemovePart(RaftPart part)` | 중복 장착 false |
| `public void ApplySavedState(int savedBaseTileCount, RaftPart savedParts)` / `ApplySavedState(..., 칸별 구성)` | 세이브 복원(v1/v2) |
| `public void WriteBaseTileCodes(List<int> buffer)` | 칸별 구성 → 세이브 목록 |
| `public void NotifyProgressChanged()` | 외부 복원 후 통지 |
| `public static RaftStructure EnsureInstance()` | 인스턴스 확보(씬 우선) |
| `public RaftSailing Sailing => ...` | Awake 이후 non-null |
| `public void RefreshVisual()` | 상태 지문 동일 시 no-op |
| `public bool PlacementResolved => ...` | 해안 자리 확정 여부 |

## Systems/IslandResourceSpawner.MeshLibrary.cs — `static class ResourceVisualLibrary`

자원 노드가 공유하는 머티리얼/메시 보관소 (B28).

| 시그니처 | 요약 |
|---|---|
| `public static Color Shade(Color color, float factor)` | 명도 변주(알파 1 고정) |
| `public static Material GetMaterial(Color color, string textureName)` | (색+텍스처)당 1개 캐시 |
| `public static bool TryLoadTwoPartModel(string resourcePath, out Mesh trunk, out Mesh foliage)` | 줄기+잎 OBJ |
| `public static bool TryLoadMultiPartModel(string resourcePath, string[] partNames, ...)` | 다중 `o` OBJ 프로브 |
| `public static bool AnyPartMissing(Mesh[] partMeshes)` | 프로브 필요 판정 |
| `public static bool IsMultiPartModelComplete(Mesh mergedMesh, Mesh[] partMeshes)` | 빌드 가능 판정 |
| `public static void ApplySubmeshMaterials(MeshRenderer renderer, Mesh mesh, Material[] materials)` | 서브메시 = OBJ `o` 순서 |
| `public static void BuildMultiPartVisual(Transform root, string mergedPartName, Mesh mergedMesh, ...)` | 다중 파트 시각 조립 |
| `public static bool TryGetBambooModel(float targetHeight, Vector3 worldPosition, ...)` | 대나무 공유 메시 2장 |
| `public static int OreRockVariantCount { get; }` / `public static bool TryGetOreRockModel(int variant, out Mesh mesh, out Vector3 size)` | 원석 바위 |
| `public static Mesh BambooCulmUnit(int variant) / BambooCulmMeters(int variant) / BranchStickUnit(int variant) / TwigMeters(int variant) / FrondMeters(int variant) / RockChunkUnit(int variant) / StoneFlakeUnit(int variant)` | 절차 메시(전부 공유 캐시) |
