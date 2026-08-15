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
        /// 저장된 물을 수확하여 곧바로 지정한 플레이어의 갈증 수치를 회복시킨다.
        /// </summary>
        public void CollectInto(SurvivalStats targetStats)
        {
            if (targetStats == null)
                return;

            float collected = Collect();
            targetStats.ConsumeWater(collected);
        }
    }
}
