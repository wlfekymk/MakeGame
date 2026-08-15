using UnityEngine;
using MakeGame.Player;

namespace MakeGame.Systems
{
    /// <summary>
    /// 물 증류기 (Stranded Deep 기준: 나뭇잎 증발/빗물 등으로 시간이 지나면 담수를 생산하는 제작 구조물).
    /// 코코넛 워터로 임시 해갈하다가, 이 구조물을 제작하면 지속적으로 담수를 확보할 수 있게 된다.
    /// </summary>
    public class WaterStill : MonoBehaviour
    {
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
            BuildVisual();
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

            // 돔 천막을 받치는 중심 지지대
            StructureVisualBuilder.CreateVisualPart(transform, "Pole", PrimitiveType.Cylinder,
                new Vector3(0f, 0.75f, 0f), new Vector3(0.06f, 0.5f, 0.06f), new Color(0.4f, 0.28f, 0.15f));

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
