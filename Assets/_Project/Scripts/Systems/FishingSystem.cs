using UnityEngine;
using UnityEngine.UI;
using MakeGame.Data;
using MakeGame.Player;
using MakeGame.UI;

namespace MakeGame.Systems
{
    /// <summary>
    /// [낚시] 낚싯대로 하는 독립된 낚시 활동. Stranded Deep의 낚시대/작살처럼 "물고기를 잡는 방법"이
    /// 여러 갈래가 되도록, 기존 사냥(HuntableCreature - E키 한 번, 도구 불필요, 성공률 60%)을 **한 줄도
    /// 건드리지 않고** 그 옆에 선택지를 하나 더 놓는 것이 이 파일의 전부다.
    ///
    /// 흐름(전용 키 Z 하나로 전부 진행한다):
    ///   1. 캐스팅  — 낚싯대를 소지한 채 물을 겨누고 Z. 찌가 포물선을 그리며 날아가 수면에 앉는다.
    ///                 낚싯대 내구도 1 소모(HuntableCreature.TryHunt의 "시도 자체로 1 닳는다"와 같은 규칙),
    ///                 가방에 미끼가 있으면 1개 소모한다.
    ///   2. 입질 대기 — 랜덤 대기(미끼 유무로 갈린다). 이 동안 Z를 누르면 **성급한 챔질**로 실패한다.
    ///   3. 입질     — 찌가 물속으로 잠기고 경고음이 난다. 이때부터 hookWindowSeconds 안에 Z를 누르면 챔질 성공 판정.
    ///   4. 결과     — 성공하면 전리품 표를 굴려 생선/해조류/나뭇가지를 주고 사냥 스킬 경험치를 준다.
    ///
    /// [왜 씬에 없는가] 씬 편집은 이 작업의 락 밖이다. 그래서 이 컴포넌트는 **PlayerController.Awake가
    /// 런타임에 AddComponent로 붙인다**(CameraShake를 카메라에 런타임 부착하는 것과 같은 수법).
    /// 즉 인스펙터 직렬화 값이 존재하지 않으므로 **아래 코드 기본값이 곧 실동작값이다**
    /// (AGENT_BRIEF 0장의 "씬 값이 이긴다"가 적용되지 않는 경우 - HuntableCreature와 같은 처지다).
    ///
    /// [rng] 확률은 전부 UnityEngine.Random이다. 월드 생성 스트림(SeededRandomExtensions.CreateForIsland)은
    /// 한 비트도 건드리지 않는다 - 낚시는 배치가 아니라 런타임 판정이라 조리/식중독 확률과 같은 갈래다
    /// (AGENT_BRIEF 2장 6번의 명시된 예외).
    ///
    /// [세이브] 낚시 진행 상태는 저장하지 않는다. 순수 런타임 타이머뿐이고 세이브 스키마(SaveData)를
    /// 건드리지 않는다 - 불러오면 찌를 걷은 상태에서 시작한다.
    ///
    /// [OnGUI 금지] 상태 표시는 UIBuilder로 만든 uGUI 캔버스다. sortOrder 3은 프로젝트에서 비어 있는
    /// 번호이고(실측: 4 프롬프트 / 5 HUD / 6 상태이상 / 7 나침반 / 8 건축 / 9 미니맵 / 10 모달 / 12 피격 /
    /// 13 툴팁 / 15 디버그·타이틀 / 16 설정 / 20 사망 / 21 엔딩), 패널 위치도 화면 중앙 **위쪽**이라
    /// 조준 프롬프트(중앙 아래)와 겹치지 않는다.
    /// </summary>
    public class FishingSystem : MonoBehaviour
    {
        /// <summary>낚시 한 번의 진행 단계.</summary>
        public enum FishingPhase
        {
            /// <summary>줄을 걷은 평상 상태.</summary>
            Idle,
            /// <summary>찌가 날아가는 중.</summary>
            Casting,
            /// <summary>수면에 앉아 입질을 기다리는 중.</summary>
            Waiting,
            /// <summary>입질 신호가 떴고 챔질을 받는 짧은 창.</summary>
            Biting,
        }

        /// <summary>캐스팅이 막힌 사유. 화면 문구는 <see cref="GetFailureText"/> 하나에서만 만든다.</summary>
        public enum CastFailure
        {
            None,
            /// <summary>가방에 낚싯대가 없다.</summary>
            NoRod,
            /// <summary>수면을 겨누고 있지 않다(하늘/위쪽/정확히 발밑).</summary>
            NotAimingAtWater,
            /// <summary>겨눈 자리가 물이 아니다(뭍/여울 밖).</summary>
            NotWaterThere,
            /// <summary>머리가 수면 아래다(잠수 중).</summary>
            HeadUnderwater,
        }

        // ── 아이템 이름 (ItemDataRegistry 실재 itemName - 전부 대조 완료) ──────────────
        //
        // ItemData 참조를 인스펙터로 받을 수 없으므로(런타임 부착이라 직렬화가 없다) 다른 런타임 생성
        // 시스템(IslandResourceSpawner.FindRegistryItemByName / CraftStation / AirlinerWreck)과 같은
        // 방식으로 ItemDataRegistry에서 **이름으로** 찾는다. 세이브가 쓰는 키와 같은 문자열이다.

        /// <summary>낚싯대(Item_낚싯대.asset의 itemName).</summary>
        public const string RodItemName = "낚싯대";

        /// <summary>미끼(Item_미끼.asset의 itemName).</summary>
        public const string BaitItemName = "미끼";

        /// <summary>주 전리품(기존 Item_생선.asset).</summary>
        public const string FishItemName = "생선";

        /// <summary>낮은 확률 전리품 1(기존 Item_해조류.asset).</summary>
        public const string SeaweedItemName = "해조류";

        /// <summary>낮은 확률 전리품 2 - 떠내려온 잡동사니(기존 Item_나뭇가지.asset).</summary>
        public const string DriftwoodItemName = "나뭇가지";

        // ── 수치 (코드 기본값이 곧 실동작값 - 클래스 주석 참고) ────────────────────────

        [Tooltip("찌를 던질 수 있는 최소 수평 거리(m). 발밑 물가에 그대로 떨어뜨리지 못하게 하는 하한이다.")]
        public float minCastDistance = 4f;

        [Tooltip("찌를 던질 수 있는 최대 수평 거리(m). 시선이 더 먼 수면을 가리켜도 여기까지만 날아간다.")]
        public float maxCastDistance = 18f;

        [Tooltip("찌가 날아가는 시간(초).")]
        public float castFlightSeconds = 0.55f;

        [Tooltip("찌가 그리는 포물선의 최고 높이(m).")]
        public float castArcHeight = 2.2f;

        [Tooltip("미끼 없이 던졌을 때 입질까지의 최소 대기(초).")]
        public float plainBiteDelayMin = 5f;

        [Tooltip("미끼 없이 던졌을 때 입질까지의 최대 대기(초).")]
        public float plainBiteDelayMax = 12f;

        [Tooltip("미끼를 꿰고 던졌을 때 입질까지의 최소 대기(초).")]
        public float baitedBiteDelayMin = 2.5f;

        [Tooltip("미끼를 꿰고 던졌을 때 입질까지의 최대 대기(초).")]
        public float baitedBiteDelayMax = 6f;

        [Tooltip("입질 신호가 뜬 뒤 챔질을 받아주는 창(초). 이 안에 키를 다시 누르면 성공 판정이 돌아간다.")]
        public float hookWindowSeconds = 1.2f;

        [Tooltip("미끼 없이 챔질했을 때의 기본 성공 확률(0~1).")]
        [Range(0f, 1f)]
        public float plainHookChance = 0.45f;

        [Tooltip("미끼를 꿰고 챔질했을 때의 기본 성공 확률(0~1).")]
        [Range(0f, 1f)]
        public float baitedHookChance = 0.7f;

        // HuntableCreature.huntingLevelSuccessBonus와 **같은 값**이다. 낚시도 사냥 스킬을 쓰므로
        // 레벨 페이로드가 두 갈래로 갈라지면 "어느 쪽으로 올리는 게 이득인가"가 조용히 어긋난다.
        [Tooltip("사냥 스킬 레벨 1당(Lv1 초과분) 챔질 성공 확률에 더할 값. Lv10이면 +0.27이다.")]
        public float huntingLevelSuccessBonus = 0.03f;

        [Tooltip("낚시 성공 시 지급할 사냥(Hunting) 스킬 경험치. 사냥 1회(20)보다 낮다 - 낚시는 안전하기 때문이다.")]
        public float fishingExperience = 15f;

        [Tooltip("전리품 표: 생선이 나올 확률(0~1). 나머지를 해조류/나뭇가지가 나눠 가진다.")]
        [Range(0f, 1f)]
        public float fishLootChance = 0.78f;

        [Tooltip("전리품 표: 해조류가 나올 확률(0~1). 생선 확률 바로 다음 구간이고, 그 뒤 나머지가 나뭇가지다.")]
        [Range(0f, 1f)]
        public float seaweedLootChance = 0.15f;

        [Tooltip("찌에서 이만큼 멀어지면 줄이 끊겨 낚시가 중단된다(m). 던진 뒤 걸어가 버리는 경우를 정리한다.")]
        public float lineBreakDistance = 30f;

        [Tooltip("결과 문구를 화면에 남겨 두는 시간(초).")]
        public float resultBannerSeconds = 2.2f;

        // ── 런타임 상태 (전부 순수 런타임 - 직렬화하지 않는다) ─────────────────────────

        /// <summary>지금 살아 있는 낚시 시스템. 프롬프트 UI가 상태를 물어보는 유일한 통로다.</summary>
        public static FishingSystem Active { get; private set; }

        private PlayerController owner;
        private PlayerInventory inventory;
        private PlayerSkills skills;
        private Transform eye;

        private ItemData rodData;
        private ItemData baitData;
        private ItemData fishData;
        private ItemData seaweedData;
        private ItemData driftwoodData;
        private bool itemLookupDone;

        private FishingPhase phase = FishingPhase.Idle;
        private Vector3 castStart;
        private Vector3 castTarget;
        private float castTimer;
        private float waitTimer;
        private float biteDelay;
        private float biteTimer;
        private bool usedBait;
        private float resultTimer;

        private GameObject bobber;

        private GameObject canvasRoot;
        private GameObject panelRoot;
        private Text mainLabel;
        private Text subLabel;

        // 팔레트는 새로 만들지 않는다(ArtDirection 1장). 조준 프롬프트가 이미 쓰는 무채색 두 개와,
        // 이 프로젝트가 "주목시키는 안내"에 쓰는 옅은 금색(SurvivalHudUI.objectiveLabel / MinimapUI)을 재사용한다.
        private static readonly Color CalmColor = Color.white;
        private static readonly Color SubInfoColor = new Color(0.8f, 0.8f, 0.8f, 1f);
        private static readonly Color AlertColor = new Color(1f, 0.9f, 0.4f, 1f);

        /// <summary>줄이 나가 있는 동안인지(캐스팅/대기/입질). 프롬프트가 중복 안내를 피하는 데 쓴다.</summary>
        public bool IsLineOut => phase != FishingPhase.Idle;

        /// <summary>지금의 진행 단계(읽기 전용).</summary>
        public FishingPhase Phase => phase;

        /// <summary>
        /// 실제로 낚시에 쓰이는 키. 주인은 PlayerController(직렬화 필드 fishingKey)이고 여기서는 읽기만
        /// 한다 - 화면 문구가 키를 하드코딩하지 않게 하는 통로다(InteractionPromptUI.GetCraftToggleKey와
        /// 같은 규약: 표시하는 쪽이 실제 입력을 가진 쪽에서 값을 읽는다).
        /// </summary>
        public KeyCode FishingKey => owner != null ? owner.fishingKey : KeyCode.Z;

        /// <summary>
        /// [R1 규약] 도메인 리로드를 끈 플레이 모드에서 이전 세션의 정적 참조가 새지 않게 되돌린다
        /// (OceanWaves.ResetStaticState / RainWetness와 같은 훅). 정적 상태는 Active 하나뿐이다 -
        /// 아이템 참조와 UI는 전부 인스턴스 필드라 오브젝트와 함께 사라진다.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticState()
        {
            Active = null;
        }

        /// <summary>
        /// 소유자(PlayerController)가 참조를 밀어 넣는다. 이 컴포넌트는 런타임에 AddComponent로 붙으므로
        /// 인스펙터 배선이 존재하지 않는다 - 인벤토리/스킬/카메라를 이미 손에 들고 있는 쪽이 넘겨주는 것이
        /// 씬을 건드리지 않고 배선하는 유일한 방법이다. 값이 비어 있으면 같은 오브젝트에서 한 번 더 찾는다.
        /// </summary>
        public void Configure(PlayerController controller, PlayerInventory playerInventory,
            PlayerSkills playerSkills, Transform cameraTransform)
        {
            owner = controller;
            inventory = playerInventory != null ? playerInventory : GetComponent<PlayerInventory>();
            skills = playerSkills != null ? playerSkills : GetComponent<PlayerSkills>();
            eye = cameraTransform != null ? cameraTransform : transform;
        }

        private void Awake()
        {
            Active = this;
        }

        /// <summary>아이템 참조를 찾고 상태 표시 UI와 찌를 미리 만들어 둔다.</summary>
        private void Start()
        {
            ResolveItemData();
            BuildUI();
            SetPanelOpen(false);

            // 찌를 **미리** 만든다. CreateVisualPart의 Object.Destroy(collider)는 프레임 끝까지 지연되므로,
            // 첫 캐스팅 순간에 만들면 그 한 프레임 동안 카메라 코앞에 콜라이더가 떠 있어 조준 레이
            // (InteractionController)가 그것을 집을 수 있다. 시작할 때 만들어 두면 그 창이 아예 없다.
            EnsureBobber();
        }

        private void OnDestroy()
        {
            if (Active == this)
                Active = null;

            if (bobber != null)
                Destroy(bobber);

            // UIBuilder.CreateCanvas는 씬 루트에 별도 오브젝트를 만든다 - 이 컴포넌트가 사라지면
            // 주인 없는 캔버스가 남으므로 함께 지운다(다른 자체 부트스트랩 UI와 같은 정리 규칙).
            if (canvasRoot != null)
                Destroy(canvasRoot);
        }

        /// <summary>
        /// ItemDataRegistry(Resources)에서 이름으로 아이템 다섯 개를 찾아 캐시한다. 레지스트리가 없거나
        /// 등록이 빠져 있으면 해당 참조만 null로 남고, 낚싯대가 없으면 낚시 기능 자체가 조용히 꺼진다
        /// (경고는 한 번만 남긴다 - 등록 누락을 눈으로 잡기 위한 것이며 동작을 막지는 않는다).
        /// </summary>
        private void ResolveItemData()
        {
            if (itemLookupDone)
                return;
            itemLookupDone = true;

            ItemDataRegistry registry = ItemDataRegistry.LoadFromResources();
            if (registry == null || registry.allItems == null)
            {
                Debug.LogWarning("[FishingSystem] ItemDataRegistry를 불러오지 못해 낚시를 켤 수 없다.");
                return;
            }

            for (int i = 0; i < registry.allItems.Count; i++)
            {
                ItemData item = registry.allItems[i];
                if (item == null || string.IsNullOrEmpty(item.itemName))
                    continue;

                if (item.itemName == RodItemName)
                    rodData = item;
                else if (item.itemName == BaitItemName)
                    baitData = item;
                else if (item.itemName == FishItemName)
                    fishData = item;
                else if (item.itemName == SeaweedItemName)
                    seaweedData = item;
                else if (item.itemName == DriftwoodItemName)
                    driftwoodData = item;
            }

            if (rodData == null)
                Debug.LogWarning($"[FishingSystem] ItemDataRegistry에 '{RodItemName}'이(가) 없어 낚시를 켤 수 없다.");
        }

        // ────────────────────────────────────────────────────────────────────────
        // 입력 (주인은 PlayerController다 - 그쪽이 이미 가진 입력 게이트를 그대로 쓴다)
        // ────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// 낚시 키가 눌린 그 프레임에 PlayerController가 부른다. **키 판정도 게이트도 여기서 하지 않는다** -
        /// timeScale 0(타이틀·설정·사망·엔딩) · 커서 잠금 해제(창이 열림) · MovementSuspended(뗏목 조종)는
        /// 전부 PlayerController.HandleCombatInput이 이미 거르고 부른다(회피/투척과 같은 관문).
        ///
        /// 한 키가 단계에 따라 다른 뜻을 갖는다:
        ///  · Idle    → 캐스팅
        ///  · Casting → 무시(찌가 날아가는 중)
        ///  · Waiting → **성급한 챔질**. 실패로 처리한다(낚시의 긴장을 만드는 규칙이자, 던져 놓고 그만두고
        ///              싶을 때의 취소 경로이기도 하다 - 내구도는 던질 때 이미 나갔으므로 여기서 또 깎지 않는다).
        ///  · Biting  → 챔질 판정
        /// </summary>
        public void HandleFishingKey()
        {
            switch (phase)
            {
                case FishingPhase.Casting:
                    return;

                case FishingPhase.Waiting:
                    EndAttempt("성급하게 챘다", "찌가 흔들리기도 전에 당겨 물고기가 달아났다", false);
                    return;

                case FishingPhase.Biting:
                    ResolveHook();
                    return;

                default:
                    TryStartCast();
                    return;
            }
        }

        /// <summary>
        /// 찌를 던진다. 낚싯대 내구도 1과 미끼 1개(있으면)를 소모하고 입질 대기에 들어간다.
        ///
        /// 낚싯대가 아예 없으면 **소리 없이 아무 일도 하지 않는다** - 전용 키는 평소에 아무 뜻도 없는
        /// 입력이라, 여기서 실패음을 내면 걷다가 무심코 누를 때마다 게임이 삑삑거린다
        /// (PlayerController.TryThrowSpear가 창이 없을 때 침묵하는 것과 같은 규칙).
        /// 반대로 **낚싯대를 들고 있는데 못 던진 경우**에는 거부음을 낸다 - 그때는 "눌렀는데 아무 일이
        /// 없다"가 버그로 읽히기 때문이다(AudioManager.PlayActionFail 주석의 용법 그대로).
        /// </summary>
        private void TryStartCast()
        {
            InventoryItem rod = FindRod();
            if (rod == null)
                return;

            if (!TryGetAimedWaterPoint(out Vector3 point, out CastFailure failure))
            {
                AudioManager.Instance?.PlayActionFail();
                ShowTransientBanner("낚시를 시작할 수 없다", GetFailureText(failure), true);
                return;
            }

            // 미끼는 던지는 순간 바늘에 꿴다. baitData가 없거나 가방에 없으면 RemoveItems가 false를
            // 돌려주므로(수량 부족 시 아무것도 지우지 않는다) 그대로 "미끼 없음"으로 진행한다.
            usedBait = baitData != null && inventory != null && inventory.RemoveItems(baitData, 1);

            // 내구도 1 소모. 이번 사용으로 다 닳으면 PlayerInventory가 알아서 목록에서 빼고 파손음까지
            // 낸다(기존 규약 - 여기서 흉내내지 않는다). 이미 나간 줄은 그대로 끝까지 진행시킨다.
            if (inventory != null)
                inventory.UseItem(rod);

            castStart = eye != null ? eye.position + eye.forward * 0.6f : transform.position + Vector3.up * 1.5f;
            castTarget = point;
            castTimer = 0f;
            waitTimer = 0f;
            biteTimer = 0f;
            biteDelay = usedBait
                ? Random.Range(baitedBiteDelayMin, baitedBiteDelayMax)
                : Random.Range(plainBiteDelayMin, plainBiteDelayMax);

            phase = FishingPhase.Casting;
            EnsureBobber();
            bobber.transform.position = castStart;
            bobber.SetActive(true);

            ShowBanner("찌를 던졌다", usedBait ? "미끼를 꿰었다 - 입질이 빠르다" : "미끼 없음 - 입질이 느리다", false);
        }

        /// <summary>
        /// 챔질 판정. 성공 확률 = 기본값(미끼 유무) + 사냥 레벨 보너스이고, 반드시 Clamp01한다
        /// (미끼 0.7 + Lv10 0.27 = 0.97이지만 수치를 올리면 1을 넘길 수 있다 - HuntableCreature와 같은 방어).
        /// </summary>
        private void ResolveHook()
        {
            float chance = usedBait ? baitedHookChance : plainHookChance;
            if (skills != null)
                chance += huntingLevelSuccessBonus * (skills.GetLevel(SkillType.Hunting) - 1);
            chance = Mathf.Clamp01(chance);

            if (Random.value >= chance)
            {
                EndAttempt("놓쳤다", "바늘이 헛돌았다 - 다시 던져 보자", false);
                return;
            }

            ItemData loot = RollLoot();
            if (loot == null || inventory == null)
            {
                EndAttempt("무언가 걸렸지만 놓쳤다", "", false);
                return;
            }

            // 경험치는 "낚아 올렸다"는 사실에 붙인다(가방이 가득 차 못 챙긴 경우에도 준다 -
            // HuntableCreature.ResolveHuntAttempt가 taken 수와 무관하게 경험치를 주는 것과 같은 규칙).
            if (skills != null)
                skills.AddExperience(SkillType.Hunting, fishingExperience);

            if (!inventory.TryAddItem(loot))
            {
                EndAttempt($"{loot.itemName}을(를) 낚았지만 놓쳤다",
                    "가방이 가득 찼다 - Tab에서 정리하거나 저장궤에 넣어라", true);
                return;
            }

            AudioManager.Instance?.PlayPickup();
            EndAttempt($"{loot.itemName}을(를) 낚았다!", "", false);
        }

        /// <summary>
        /// 전리품 표를 굴린다(UnityEngine.Random - 월드 생성 스트림과 무관한 런타임 판정이다).
        /// 생선 78% / 해조류 15% / 나뭇가지 7%. 어떤 항목이 레지스트리에 없으면 생선으로 떨어지고,
        /// 생선마저 없으면 null이라 호출부가 "놓쳤다"로 처리한다.
        /// </summary>
        private ItemData RollLoot()
        {
            float roll = Random.value;
            float fishCut = Mathf.Clamp01(fishLootChance);
            float seaweedCut = Mathf.Clamp01(fishCut + Mathf.Max(0f, seaweedLootChance));

            if (roll < fishCut)
                return fishData;

            if (roll < seaweedCut)
                return seaweedData != null ? seaweedData : fishData;

            return driftwoodData != null ? driftwoodData : fishData;
        }

        /// <summary>
        /// 한 번의 낚시를 끝내고 줄을 걷는다(성공/실패 공통 출구). 결과 문구는 resultBannerSeconds 동안 남는다.
        /// </summary>
        private void EndAttempt(string headline, string detail, bool blocked)
        {
            phase = FishingPhase.Idle;
            usedBait = false;
            if (bobber != null)
                bobber.SetActive(false);

            ShowTransientBanner(headline, detail, blocked);
        }

        /// <summary>
        /// 잠깐 떴다가 스스로 사라지는 문구(결과 · 거부 사유). 진행 중 상태 문구(ShowBanner)와 달리
        /// 다음 단계가 덮어써 주지 않으므로 자동 소멸 타이머를 함께 건다.
        /// </summary>
        private void ShowTransientBanner(string headline, string detail, bool blocked)
        {
            ShowBanner(headline, detail, blocked);
            resultTimer = Mathf.Max(0.2f, resultBannerSeconds);
        }

        // ────────────────────────────────────────────────────────────────────────
        // 진행
        // ────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// 낚시 단계 타이머를 굴리고 찌를 수면에 얹는다. 정지 화면(timeScale 0)에서는 통째로 멈추고
        /// 패널도 숨긴다 - 타이틀/설정/사망/엔딩 위에 낚시 상태가 남아 있으면 안 된다
        /// (InteractionPromptUI.Update가 같은 조건으로 자신을 숨기는 것과 같은 규칙).
        /// </summary>
        private void Update()
        {
            if (Time.timeScale <= 0f)
            {
                SetPanelOpen(false);
                return;
            }

            float dt = Time.deltaTime;

            if (resultTimer > 0f)
            {
                resultTimer -= dt;
                if (resultTimer <= 0f && phase == FishingPhase.Idle)
                    SetPanelOpen(false);
            }

            if (phase == FishingPhase.Idle)
                return;

            // 소유 컨트롤러가 꺼진 상태(타이틀 화면 · 사망 · 엔딩에서 MainMenuController 등이 끈다)면
            // 줄을 걷는다. 켜져 있는 동안에만 진행하는 활동이다.
            if (owner == null || !owner.enabled)
            {
                EndAttempt("낚시를 중단했다", "", false);
                return;
            }

            // 던져 놓고 걸어가 버리면 줄이 끊긴다. 제자리를 벗어난 채로 입질을 기다리는 것을 막는 것이
            // 목적이라 판정은 수평 거리 하나뿐이다(sqrMagnitude - 매 프레임 도는 곳이라 제곱근을 피한다).
            Vector3 toBobber = castTarget - transform.position;
            toBobber.y = 0f;
            float breakDistance = Mathf.Max(minCastDistance + 1f, lineBreakDistance);
            if (toBobber.sqrMagnitude > breakDistance * breakDistance)
            {
                EndAttempt("줄이 끊겼다", "찌에서 너무 멀어졌다", true);
                return;
            }

            switch (phase)
            {
                case FishingPhase.Casting:
                    TickCasting(dt);
                    break;

                case FishingPhase.Waiting:
                    TickWaiting(dt);
                    break;

                case FishingPhase.Biting:
                    TickBiting(dt);
                    break;
            }
        }

        /// <summary>찌가 날아가는 구간. 포물선 보간이라 힙 할당이 없다.</summary>
        private void TickCasting(float dt)
        {
            castTimer += dt;
            float flight = Mathf.Max(0.05f, castFlightSeconds);
            float k = Mathf.Clamp01(castTimer / flight);

            if (bobber != null)
            {
                Vector3 p = Vector3.Lerp(castStart, castTarget, k);
                p.y += castArcHeight * 4f * k * (1f - k); // 0에서 시작해 중간에 최대, 끝에서 0인 포물선
                bobber.transform.position = p;
            }

            if (k < 1f)
                return;

            phase = FishingPhase.Waiting;
            waitTimer = 0f;
            ShowBanner("입질을 기다리는 중...",
                usedBait ? "미끼를 꿰었다 - 입질이 빠르다" : "미끼 없음 - 입질이 느리다", false);
        }

        /// <summary>입질 대기 구간. 찌는 파도를 타고 오르내린다.</summary>
        private void TickWaiting(float dt)
        {
            waitTimer += dt;
            PlaceBobberOnSurface(0f);

            if (waitTimer < biteDelay)
                return;

            phase = FishingPhase.Biting;
            biteTimer = 0f;

            // 입질 신호. 상태 이상 경고음과 같은 "지금 이 순간을 봐라"는 1회성 알림이라 그 소리를
            // 재사용한다(AudioManager.PlayStatusOnset - 0.3초 재발동 가드가 있어 겹쳐 울지 않는다).
            AudioManager.Instance?.PlayStatusOnset();
            ShowBanner("입질! 지금 챔질하라", GetHookKeyHint(), false);
        }

        /// <summary>챔질 창. 다 지나가면 실패다. 찌는 물속으로 잠긴다.</summary>
        private void TickBiting(float dt)
        {
            biteTimer += dt;

            // 잠겼다 떴다 하는 짧은 진동. Mathf.Sin 한 번이라 할당이 없다.
            PlaceBobberOnSurface(-0.22f - 0.08f * Mathf.Sin(Time.time * 18f));

            if (biteTimer >= Mathf.Max(0.1f, hookWindowSeconds))
                EndAttempt("입질을 놓쳤다", "챔질이 늦었다", false);
        }

        /// <summary>찌를 그 자리 수면 높이에 얹는다(파도 연동). verticalOffset이 음수면 물속으로 잠긴다.</summary>
        private void PlaceBobberOnSurface(float verticalOffset)
        {
            if (bobber == null)
                return;

            Vector3 p = castTarget;
            p.y = OceanWaves.SampleHeight(castTarget) + verticalOffset;
            bobber.transform.position = p;
        }

        // ────────────────────────────────────────────────────────────────────────
        // 조준 / 물 판정
        // ────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// 시선이 가리키는 수면 위의 착수 지점을 구한다. **캐스팅과 프롬프트가 함께 쓰는 유일한 판정이다** -
        /// UI가 같은 조건을 다시 구현하면 화면에 보이는 것과 실제 동작이 갈라진다(InteractionPromptUI의 원칙).
        ///
        /// 판정 순서:
        ///  1. 머리(카메라)가 수면 아래면 잠수 중이다 - 낚시가 아니다.
        ///  2. 시선이 아래를 향하지 않으면 수면과 만나지 않는다.
        ///  3. 시선-수면 평면 교점을 구하고, 수평 거리를 [minCastDistance, maxCastDistance]로 자른다.
        ///  4. 그 자리에 실제로 물이 있는지는 <see cref="OceanWaves.SubmergenceScale"/> 하나로 묻는다.
        ///     그 함수는 (섬 메시 레이 → 해저 해석식 → 외해) 순으로 수심을 재고 수심이 0 이하면 정확히
        ///     0을 돌려주므로, "여기에 물기둥이 있는가"의 단일 소스다. 결과가 0.4초/0.75m 반경으로
        ///     캐시돼 있어 프롬프트가 0.1초마다 불러도 레이가 다시 나가지 않고 힙 할당도 0이다.
        ///
        /// 해수면 높이는 <see cref="OceanWaves.SeaLevel"/>(WorldMapManager.seaLevel을 받는 단일 소스)을 쓴다.
        /// PlayerController.waterLevel(수영 판정용 자체 기준선)을 쓰지 않는 이유는, 착수 지점이 플레이어
        /// 발밑이 아니라 수십 미터 밖이라 그쪽의 파도 편차 보정과 기준이 다르기 때문이다.
        /// </summary>
        private bool TryGetAimedWaterPoint(out Vector3 point, out CastFailure failure)
        {
            point = Vector3.zero;

            Transform aim = eye != null ? eye : transform;
            Vector3 origin = aim.position;
            Vector3 forward = aim.forward;
            float seaY = OceanWaves.SeaLevel;

            if (origin.y <= seaY)
            {
                failure = CastFailure.HeadUnderwater;
                return false;
            }

            if (forward.y > -0.02f)
            {
                failure = CastFailure.NotAimingAtWater;
                return false;
            }

            Vector3 flatDir = new Vector3(forward.x, 0f, forward.z);
            if (flatDir.sqrMagnitude < 0.000001f)
            {
                // 정확히 발밑을 내려다보는 경우. 어느 방향으로 던질지 정할 수 없다.
                failure = CastFailure.NotAimingAtWater;
                return false;
            }
            flatDir.Normalize();

            // 시선이 수면 평면과 만나는 점까지의 수평 거리.
            float t = (seaY - origin.y) / forward.y;
            Vector3 planeHit = origin + forward * t;
            float horizontal = new Vector2(planeHit.x - origin.x, planeHit.z - origin.z).magnitude;

            float minDistance = Mathf.Max(0.5f, minCastDistance);
            float maxDistance = Mathf.Max(minDistance, maxCastDistance);
            float distance = Mathf.Clamp(horizontal, minDistance, maxDistance);

            point = new Vector3(origin.x + flatDir.x * distance, seaY, origin.z + flatDir.z * distance);

            if (OceanWaves.SubmergenceScale(point) <= 0f)
            {
                failure = CastFailure.NotWaterThere;
                return false;
            }

            failure = CastFailure.None;
            return true;
        }

        /// <summary>가방에서 낚싯대를 하나 찾는다. 없으면 null.</summary>
        private InventoryItem FindRod()
        {
            if (inventory == null)
                return null;

            ResolveItemData();
            return rodData != null ? inventory.FindItem(rodData) : null;
        }

        /// <summary>캐스팅 실패 사유를 플레이어가 읽을 한 줄로 바꾼다(문구를 만드는 유일한 곳).</summary>
        private static string GetFailureText(CastFailure failure)
        {
            switch (failure)
            {
                case CastFailure.NoRod: return "낚싯대 필요";
                case CastFailure.HeadUnderwater: return "물속에서는 낚싯대를 던질 수 없다";
                case CastFailure.NotWaterThere: return "겨눈 자리에 물이 없다";
                case CastFailure.NotAimingAtWater:
                default: return "물을 내려다보고 던져야 한다";
            }
        }

        // ────────────────────────────────────────────────────────────────────────
        // 프롬프트(InteractionPromptUI)가 읽는 공개 조회
        // ────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// 지금 낚싯대를 들고 물을 겨누고 있는지, 겨누고 있다면 화면에 뭐라고 쓸지 알려준다.
        ///
        /// **줄이 나가 있는 동안에는 false를 돌려준다** - 진행 상태는 이 시스템 자신의 패널이 이미
        /// 보여주고 있어, 조준 프롬프트까지 같은 말을 하면 화면에 같은 안내가 두 벌이 된다.
        /// 낚싯대가 없거나 수면을 겨누지 않은 경우에도 false다(뭍에서 하늘을 볼 때마다 회색 프롬프트가
        /// 뜨는 것을 막는다). 겨누기는 했는데 그 자리가 물이 아닐 때만 blocked = true로 사유를 보여준다.
        /// </summary>
        /// <param name="keyLabel">"[Z]"처럼 이미 대괄호까지 붙인 키 표기. 프롬프트 쪽 표기 규칙을 그대로 따른다.</param>
        public bool TryDescribeCastPrompt(string keyLabel, out string headline, out string detail, out bool blocked)
        {
            headline = "";
            detail = "";
            blocked = false;

            if (IsLineOut || FindRod() == null)
                return false;

            if (!TryGetAimedWaterPoint(out Vector3 unusedPoint, out CastFailure failure))
            {
                // 방향 자체가 물이 아닌 경우(하늘/발밑/잠수)는 안내할 것이 없다.
                if (failure != CastFailure.NotWaterThere)
                    return false;

                headline = $"{keyLabel} 낚시";
                detail = GetFailureText(failure);
                blocked = true;
                return true;
            }

            int baitCount = baitData != null && inventory != null ? inventory.GetItemCount(baitData) : 0;
            bool willUseBait = baitCount > 0;

            float chance = willUseBait ? baitedHookChance : plainHookChance;
            if (skills != null)
                chance += huntingLevelSuccessBonus * (skills.GetLevel(SkillType.Hunting) - 1);
            chance = Mathf.Clamp01(chance);

            headline = $"{keyLabel} 낚시";
            detail = willUseBait
                ? $"미끼 {baitCount}개 · 챔질 성공률 {Mathf.RoundToInt(chance * 100f)}% · 입질 뒤 {hookWindowSeconds:F1}초 안에 다시 {keyLabel}"
                : $"미끼 없음 · 챔질 성공률 {Mathf.RoundToInt(chance * 100f)}% · 입질 뒤 {hookWindowSeconds:F1}초 안에 다시 {keyLabel}";
            return true;
        }

        /// <summary>
        /// 낚시가 지금 가능한 상태일 때 다른 프롬프트 끝에 덧붙일 짧은 꼬리말(예: " · [Z] 낚시").
        /// 뗏목 갑판처럼 **항상 무언가를 겨누게 되는 자리**에서도 낚시를 발견할 수 있게 하는 통로다.
        /// 조건이 안 맞으면 빈 문자열이라 기존 문장이 한 글자도 달라지지 않는다
        /// (InteractionPromptUI.BuildThrowHint와 같은 방식).
        /// </summary>
        public string GetHintSuffix(string keyLabel)
        {
            if (IsLineOut || FindRod() == null)
                return "";

            return TryGetAimedWaterPoint(out Vector3 unusedPoint, out CastFailure unusedFailure)
                ? $" · {keyLabel} 낚시"
                : "";
        }

        /// <summary>입질 창 안내 문구. 소유자에게서 실제 키를 읽어 온다(하드코딩 금지).</summary>
        private string GetHookKeyHint()
        {
            return $"[{FishingKey}] 를 {hookWindowSeconds:F1}초 안에 다시 눌러라";
        }

        // ────────────────────────────────────────────────────────────────────────
        // 찌 / UI
        // ────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// 찌 오브젝트를 한 번만 만들어 두고 재사용한다(던질 때마다 만들면 그대로 GC 압력이 된다).
        /// 파츠는 StructureVisualBuilder를 거치므로 콜라이더가 없다 - 레이캐스트(조준·지형 스냅)에
        /// 절대 걸리면 안 되기 때문이다(TerrainSampler가 "Island_" 이름만 인정하긴 하지만,
        /// InteractionController의 조준 레이는 이름을 가리지 않는다).
        /// </summary>
        private void EnsureBobber()
        {
            if (bobber != null)
                return;

            bobber = new GameObject("FishingBobber");

            StructureVisualBuilder.CreateVisualPart(bobber.transform, "Float", PrimitiveType.Sphere,
                Vector3.zero, new Vector3(0.16f, 0.16f, 0.16f), new Color(0.85f, 0.25f, 0.2f));
            StructureVisualBuilder.CreateVisualPart(bobber.transform, "Stem", PrimitiveType.Cylinder,
                new Vector3(0f, 0.13f, 0f), new Vector3(0.03f, 0.1f, 0.03f), new Color(0.95f, 0.93f, 0.85f));

            bobber.SetActive(false);
        }

        /// <summary>
        /// 화면 중앙 **위쪽**에 낚시 상태 패널을 만든다. 조준 프롬프트(중앙 아래, sortOrder 4)와 위치도
        /// 정렬 순서도 겹치지 않는다. HUD류이므로 배경 알파 0.55 + 상단 테두리(ArtDirection 4.3).
        /// </summary>
        private void BuildUI()
        {
            var canvas = UIBuilder.CreateCanvas("FishingStatusCanvas", sortOrder: 3);
            canvasRoot = canvas.gameObject;

            var panel = UIBuilder.CreatePanel(
                canvas.transform, "FishingStatusPanel",
                anchorMin: new Vector2(0.5f, 0.5f), anchorMax: new Vector2(0.5f, 0.5f),
                offsetMin: new Vector2(-260f, 76f), offsetMax: new Vector2(260f, 148f),
                color: new Color(0f, 0f, 0f, 0.55f),
                addTopBorder: true);

            panelRoot = panel.gameObject;

            var vlg = panelRoot.AddComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(14, 14, 8, 8);
            vlg.spacing = 2f;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            vlg.childAlignment = TextAnchor.MiddleCenter;

            mainLabel = UIBuilder.CreateText(panel, "Main", "", 18, CalmColor, TextAnchor.MiddleCenter);
            mainLabel.gameObject.AddComponent<LayoutElement>().minHeight = 26f;

            subLabel = UIBuilder.CreateText(panel, "Sub", "", 12, SubInfoColor, TextAnchor.MiddleCenter);
            subLabel.gameObject.AddComponent<LayoutElement>().minHeight = 18f;

            // 화면 중앙 근처라 클릭을 가로채면 위험하다(조준 프롬프트와 같은 조치).
            foreach (var graphic in panelRoot.GetComponentsInChildren<Graphic>(true))
                graphic.raycastTarget = false;
        }

        /// <summary>
        /// 상태 문구를 화면에 올린다. **단계가 바뀌는 순간에만 부른다** - 매 프레임 부르면 문자열 보간이
        /// 그대로 프레임당 할당이 된다. 값이 실제로 달라졌을 때만 Text에 대입해 레이아웃 재계산도 아낀다.
        /// </summary>
        private void ShowBanner(string headline, string detail, bool blocked)
        {
            if (panelRoot == null)
                return;

            SetPanelOpen(true);
            resultTimer = 0f;

            Color mainColor = phase == FishingPhase.Biting ? AlertColor : (blocked ? SubInfoColor : CalmColor);
            if (mainLabel != null)
            {
                mainLabel.color = mainColor;
                if (mainLabel.text != headline)
                    mainLabel.text = headline;
            }

            if (subLabel != null)
            {
                subLabel.color = SubInfoColor;
                if (subLabel.text != detail)
                    subLabel.text = detail;
            }
        }

        /// <summary>상태 패널을 켜거나 끈다.</summary>
        private void SetPanelOpen(bool open)
        {
            if (panelRoot != null && panelRoot.activeSelf != open)
                panelRoot.SetActive(open);
        }
    }
}
