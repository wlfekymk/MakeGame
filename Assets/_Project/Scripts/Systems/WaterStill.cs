using UnityEngine;
using MakeGame.Data;
using MakeGame.Player;

namespace MakeGame.Systems
{
    /// <summary>
    /// 물 증류기 (Stranded Deep 기준: 나뭇잎 증발/빗물 등으로 시간이 지나면 담수를 생산하는 제작 구조물).
    /// 코코넛 워터로 임시 해갈하다가, 이 구조물을 제작하면 지속적으로 담수를 확보할 수 있게 된다.
    ///
    /// B4-1 (Spec_15 3단계 배선): SurvivalBalanceConfig를 선택적(nullable) 참조로 받는다.
    /// 폴백으로 읽는 config 필드 — waterPerSecond ← waterStillPerSecond, maxStorage ← waterStillMaxStorage.
    /// 폴백은 해당 필드가 0 이하(미설정)일 때만 적용되므로, 프리팹(Prefabs/WaterStill.prefab: 0.3/20)에
    /// 직렬화된 값이 항상 이긴다 — SurvivalStats.ApplyBalanceConfigFallback과 완전히 동일한 규칙이다.
    /// </summary>
    public class WaterStill : MonoBehaviour
    {
        [Header("밸런스 config (선택, B4-1)")]
        [Tooltip("연결하면, 아래 waterPerSecond/maxStorage가 0 이하로(미설정) 남아있는 경우에 한해" +
            " config의 waterStillPerSecond/waterStillMaxStorage 값을 대신 쓴다. 씬/프리팹에 이미" +
            " 의미 있는(양수) 값이 직렬화돼 있으면 이 config는 절대 그 값을 덮어쓰지 않는다.")]
        public SurvivalBalanceConfig balanceConfig;

        // 밸런스 하향(B2-1, Spec_13): 물 증류기가 담수 확보를 지나치게 손쉽게 만들어 갈증 관리 긴장감을
        // 떨어뜨린다는 판단으로 생산 속도와 저장량을 낮췄다. [디렉터 조치 요청] 실측 결과 실제 오버라이드가
        // Prefabs/WaterStill.prefab에 있으므로(0.3f/20f), 이 코드 기본값만으로는 게임에 반영되지 않는다 -
        // 프리팹 쪽 값도 함께 바꿔야 한다.
        [Tooltip("초당 생산되는 물의 양")]
        public float waterPerSecond = 0.10f;

        [Tooltip("현재 저장된 물의 양")]
        public float storedWater = 0f;

        [Tooltip("최대로 저장할 수 있는 물의 양")]
        public float maxStorage = 12f;

        // [game-designer 요청 - Design_BalancePass 0장/5장] 생수를 "아이템으로" 받아가는 경로.
        //
        // 왜 필요했나: CollectInto가 ConsumeWater()로 갈증만 회복시켰기 때문에, 증류기는 생수를 단
        // 1개도 만들지 못했다. 배 엔딩의 requiredWaterCount(현재 씬 12)를 채울 수 있는 유일한 공급원은
        // 시작 지급 1개뿐이었고, 그래서 배 엔딩이 수학적으로 불가능했다. 씬에 생수 자원 노드가
        // 추가되어 급한 불은 껐지만, 그건 "탐색"으로 푸는 방법이고 증류기는 "설치해두고 다른 일 하기"로
        // 푸는 방법이다 - 두 경로는 배타적이지 않고, 증류기 쪽이 이 구조물의 존재 이유다.
        //
        // 마시기 vs 담기의 선택: 기존 "바로 마시기"는 그대로 남는다. bottleModifierKey를 누른 채로
        // 상호작용했을 때만 병입으로 갈라진다. 상호작용 진입점(InteractionController)은 이 에이전트의
        // 소유 파일이 아니라 새 키를 등록할 수 없어서, 이미 열려 있는 E 입력의 "수식 키" 상태를 이
        // 클래스가 직접 읽는 방식으로 두 갈래를 만들었다. 키를 KeyCode.None으로 두면 Input.GetKey가
        // 항상 false라 병입 경로 자체가 꺼지고 예전 동작(항상 마시기)으로 정확히 되돌아간다.
        [Header("생수 병입 (배 엔딩 비축 경로)")]
        [Tooltip("병입해서 지급할 아이템(생수). 비워두면 병입 경로가 동작하지 않고 예전처럼 마시기만 한다.\n" +
            "이름으로 찾지 않고 반드시 이 참조로만 지급하므로, 프리팹에서 연결하지 않으면 기능이 꺼진 것과 같다.")]
        public ItemData bottledWaterItem;

        [Tooltip("병입에 필요한 용기 아이템(물통). 연결하면 이 아이템을 인벤토리에 갖고 있을 때만 병입할 수 있다.\n" +
            "비워두면 용기 없이도 병입할 수 있다(용기 조건 없음).")]
        public ItemData requiredContainerItem;

        // 밸런스 근거 (waterPerSecond 0.10 / maxStorage 12 기준, 요구치 12개):
        //   · 저장통을 가득 채우는 데 12 / 0.10 = 120초. waterPerBottle 6이면 "가득 찬 증류기 = 정확히 2병"
        //     이라 플레이어가 규칙을 한 번 보고 외울 수 있고, 2분마다 비우지 않으면 넘쳐서 버려진다는
        //     방치 상한도 생긴다(증류기를 세워두고 잊어버리는 것이 최적해가 되지 않는다).
        //   · 12병 = 72유닛 = 증류기 1대 기준 순수 생산 720초(12분) + 최소 6회 왕복. 배 엔딩의 다른 게이트인
        //     15일(취침을 아는 플레이어 기준 74.5분, 모르면 147분)의 약 16%다. 물이 15일 조건을 밀어내고
        //     주 게이트가 되어버리지 않는 선이다.
        //   · 너무 후한 쪽(예: 1병 = 1유닛)을 피한 이유: 12병이 120초 만에 나오면 물 조건이 사실상 없는
        //     것과 같아진다. 너무 짠 쪽(예: 생수의 갈증 회복량 40과 1:1로 40유닛/병)을 피한 이유: 12병에
        //     4800초(80분)가 들어 15일 조건보다 물이 더 큰 게이트가 되는 주객전도가 일어난다.
        //   · 6유닛(직접 마시면 갈증 6 회복)을 갈증 40짜리 생수 1개로 바꾸는 것이므로 병입이 마시기보다
        //     6.7배 효율적이다. 이 격차는 물통 제작 비용과 왕복 동선에 대한 대가로 의도한 것이다.
        [Tooltip("생수 1개를 만드는 데 소모되는 저장 물의 양. 0 이하이면 병입 경로가 동작하지 않는다.")]
        public float waterPerBottle = 6f;

        [Tooltip("이 키를 누른 채로 증류기와 상호작용하면 마시는 대신 생수로 담는다(그냥 누르면 예전처럼 마신다).\n" +
            "None으로 두면 병입 경로가 완전히 꺼진다.")]
        public KeyCode bottleModifierKey = KeyCode.LeftShift;

        [Tooltip("병입한 생수를 넣을 인벤토리. 증류기는 런타임에 설치되어 인스펙터 연결 수단이 없으므로," +
            " 비어 있으면 최초 병입 시 씬에서 자동으로 찾는다.")]
        public PlayerInventory targetInventory;

        /// <summary>
        /// 생성 직후 캡슐 하나뿐인 밋밋한 프리팹 대신, 바스킷+지지대+집수 돔으로 구성된
        /// 실제 물 증류기 모양의 시각 파츠를 절차적으로 만든다.
        /// </summary>
        private void Awake()
        {
            ApplyBalanceConfigFallback();
            BuildVisual();
        }

        /// <summary>
        /// balanceConfig가 있을 때, 0 이하로 남아있는(=미설정) 필드만 골라 config 값으로 채운다.
        /// waterPerSecond/maxStorage는 정상적인 밸런스 값이라면 0이 될 일이 없으므로(0이면 증류기가
        /// 아무 일도 하지 않는 것과 같다), 0 이하를 "아직 설정되지 않음"의 안전한 신호로 삼는다.
        /// balanceConfig가 비어 있으면 아무 것도 하지 않는다(기존 동작 100% 유지, NRE 없음).
        /// </summary>
        private void ApplyBalanceConfigFallback()
        {
            // B4-2: 인스펙터에서 연결되지 않았으면 Resources의 공용 에셋을 자동으로 집는다.
            // 런타임 생성 컴포넌트(WeatherSystem/Campfire/WaterStill 등)는 인스펙터 연결 수단이
            // 아예 없어서, 이 경로가 없으면 balanceConfig가 영원히 null로 남는다.
            if (balanceConfig == null)
                balanceConfig = SurvivalBalanceConfig.Active;
            if (balanceConfig == null)
                return;

            if (waterPerSecond <= 0f) waterPerSecond = balanceConfig.waterStillPerSecond;
            if (maxStorage <= 0f) maxStorage = balanceConfig.waterStillMaxStorage;
        }

        /// <summary>
        /// 매 프레임 자동으로 시간 경과 로직을 진행시킨다 (별도 드라이버 없이 스스로 작동).
        /// </summary>
        private void Update()
        {
            Tick(Time.deltaTime);
        }

        /// <summary>
        /// 프리팹 루트의 캡슐 MeshRenderer(플레이스홀더)는 숨기고, 그 자리에 바스킷(물받이)/중심 지지대/
        /// 집수용 돔 천막을 절차적으로 조합해 붙인다. 상호작용에 쓰이는 CapsuleCollider는 루트에 그대로 둔다.
        /// </summary>
        private void BuildVisual()
        {
            var rootRenderer = GetComponent<MeshRenderer>();
            if (rootRenderer != null)
                rootRenderer.enabled = false;

            // 바닥에 놓이는 물받이 바스킷
            StructureVisualBuilder.CreateVisualPart(transform, "Basin", PrimitiveType.Cylinder,
                new Vector3(0f, 0.25f, 0f), new Vector3(0.9f, 0.25f, 0.9f), new Color(0.16f, 0.16f, 0.16f));

            // 돔 천막을 받치는 중심 지지대.
            // [tech-artist-B 요청 - 인공물 시각 언어] 원기둥 → 각진 사각 기둥 + 밧줄 결속(ArtDirection 2장 4번).
            // 원기둥 메시는 높이가 2단위라 scale.y에 0.5(=실제 높이 1.0m)를 넣고 있었는데, CreateLashedPost는
            // 큐브라 실제 높이를 그대로 받는다 - 1.0f를 넘겨 기존과 동일하게 y 0.25~1.25 구간을 채운다
            // (바스킷 윗면 0.5m와 겹치고 집수 돔 1.15m를 받치는 위치도 그대로다).
            StructureVisualBuilder.CreateLashedPost(transform, "Pole", new Vector3(0f, 0.75f, 0f),
                1f, 0.06f, new Color(0.4f, 0.28f, 0.15f));

            // 증발한 수분을 모으는 반투명한 느낌의 집수 돔(비닐 천막)
            StructureVisualBuilder.CreateVisualPart(transform, "Tarp", PrimitiveType.Sphere,
                new Vector3(0f, 1.15f, 0f), new Vector3(0.85f, 0.5f, 0.85f), new Color(0.78f, 0.87f, 0.9f));
        }

        /// <summary>
        /// 시간 경과에 따라 물을 생산한다. 저장량이 최대치를 넘지 않는다.
        /// </summary>
        public void Tick(float deltaTime)
        {
            storedWater = Mathf.Min(maxStorage, storedWater + waterPerSecond * deltaTime);
        }

        /// <summary>
        /// 저장된 물을 모두 수확하여 반환하고, 저장량을 0으로 초기화한다.
        /// </summary>
        public float Collect()
        {
            float collected = storedWater;
            storedWater = 0f;
            return collected;
        }

        /// <summary>
        /// 지금 저장된 물로 만들 수 있는 생수 개수(내림). 물이 모자라거나 병입 설정이 비어 있으면 0이다.
        /// 상태를 바꾸지 않으므로 조준 프롬프트가 매 프레임 호출해도 안전하다.
        /// </summary>
        public int GetAvailableBottleCount()
        {
            if (bottledWaterItem == null || waterPerBottle <= 0f)
                return 0;

            return Mathf.FloorToInt(storedWater / waterPerBottle);
        }

        /// <summary>
        /// 병입에 필요한 용기(requiredContainerItem)를 갖고 있는지 확인한다.
        /// requiredContainerItem이 비어 있으면 용기 조건 자체가 없는 것으로 보고 항상 true다.
        /// </summary>
        public bool HasBottleContainer(PlayerInventory inventory)
        {
            if (requiredContainerItem == null)
                return true;

            return inventory != null && inventory.FindItem(requiredContainerItem) != null;
        }

        /// <summary>
        /// 지금 이 인벤토리로 병입이 가능한지 여부(물 충분 + 용기 보유 + 지급 아이템 연결됨).
        /// 상태를 바꾸지 않는다 - UI가 "담기" 안내를 띄울지 판단할 때 쓰라고 공개한다.
        /// </summary>
        public bool CanBottle(PlayerInventory inventory)
        {
            return inventory != null && GetAvailableBottleCount() > 0 && HasBottleContainer(inventory);
        }

        /// <summary>
        /// 저장된 물을 생수 아이템으로 바꿔 인벤토리에 넣고, 만든 개수를 반환한다(0이면 아무 일도 없었다).
        /// 병 하나에 못 미치는 나머지 물은 저장통에 그대로 남는다 - Collect()처럼 전부 비우면 자투리가
        /// 조용히 증발해 "일찍 가면 손해"가 되고, 플레이어가 저장량을 눈으로 볼 수단이 없어서 그 손해를
        /// 인지할 수도 없다.
        /// </summary>
        public int CollectIntoBottles(PlayerInventory inventory)
        {
            if (!CanBottle(inventory))
                return 0;

            int bottles = GetAvailableBottleCount();
            storedWater = Mathf.Max(0f, storedWater - bottles * waterPerBottle);

            for (int i = 0; i < bottles; i++)
                inventory.AddItem(bottledWaterItem);

            AudioManager.Instance?.PlayPickup(); // 채집/획득과 같은 "받았다" 신호
            return bottles;
        }

        /// <summary>
        /// 저장된 물을 수확한다. 기본 동작은 예전과 같이 "바로 마시기"(갈증 회복)이고,
        /// bottleModifierKey를 누른 채로 상호작용하면 대신 생수 아이템으로 담는다.
        ///
        /// 인벤토리를 넘기지 않는 편의 오버로드. 씬의 정상 경로(InteractionController)는 이제
        /// 배선된 inventory를 직접 넘기는 2인자 오버로드를 부르므로 이 경로를 타지 않는다.
        /// 프리팹을 단독으로 쓰거나 인벤토리 참조가 없는 호출부를 위한 폴백으로만 남겨 둔다
        /// (ResolveInventory의 FindAnyObjectByType은 여기서만 실행된다).
        /// </summary>
        public void CollectInto(SurvivalStats targetStats)
        {
            CollectInto(targetStats, ResolveInventory());
        }

        /// <summary>
        /// 인벤토리를 명시적으로 지정하는 오버로드. 병입 조건이 모두 갖춰졌고 실제로 1병 이상 만들어졌을
        /// 때만 병입으로 끝나고, 그 외에는 예전과 100% 동일하게 "전부 마시기"로 떨어진다
        /// (물이 병 하나에 모자라거나 물통이 없으면 헛손질이 되지 않고 그냥 마신다).
        /// </summary>
        public void CollectInto(SurvivalStats targetStats, PlayerInventory inventory)
        {
            if (IsBottleRequested() && CollectIntoBottles(inventory) > 0)
                return;

            if (targetStats == null)
                return;

            float collected = Collect();
            targetStats.ConsumeWater(collected);
        }

        /// <summary>
        /// 이번 상호작용이 "담기" 요청인지(수식 키를 누르고 있는지). bottleModifierKey가 None이면
        /// Input.GetKey가 항상 false를 돌려주므로 병입 경로 전체가 꺼진다.
        /// </summary>
        private bool IsBottleRequested()
        {
            return Input.GetKey(bottleModifierKey);
        }

        /// <summary>
        /// 병입 대상 인벤토리를 찾는다. 증류기는 플레이어가 설치할 때 런타임에 생성되어 인스펙터에서
        /// 연결할 수단이 없으므로(balanceConfig가 SurvivalBalanceConfig.Active로 폴백하는 것과 같은
        /// 사정), 비어 있으면 씬에서 한 번 찾아 캐시한다. 못 찾으면 null이며 이때는 그냥 마시기로 떨어진다.
        /// </summary>
        private PlayerInventory ResolveInventory()
        {
            if (targetInventory == null)
                targetInventory = FindAnyObjectByType<PlayerInventory>();

            return targetInventory;
        }
    }
}
