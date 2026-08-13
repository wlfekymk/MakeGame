using UnityEngine;

namespace MakeGame.Systems
{
    /// <summary>
    /// 플레이어가 제작해 설치한 쉼터(Shelter). Stranded Deep 기준: 비/햇빛을 막아주고 휴식 지점이 된다.
    /// 별도의 능동적 동작은 없으며, 지붕 콜라이더가 "Shade" 레이어에 있어
    /// SurvivalTickDriver의 그늘 판정 레이캐스트에 자동으로 걸리도록 하는 것이 핵심 역할이다.
    /// </summary>
    public class Shelter : MonoBehaviour
    {
        [Tooltip("이 쉼터가 제공하는 그늘 판정 반경(참고용 수치, 실제 판정은 레이캐스트로 이뤄진다)")]
        public float shadeRadius = 3f;

        [Tooltip("지붕이 바닥으로부터 떠 있어야 하는 높이. 설치 시 Instantiate가 루트 위치를 바닥(설치 지점)에\n맞춰버리므로, 이 값만큼 스스로 들어올려 지붕이 바닥에 깔리지 않게 한다.")]
        public float roofHeight = 2.2f;

        /// <summary>
        /// 설치 직후 루트(지붕)를 roofHeight만큼 들어올리고, 바닥까지 닿는 기둥 4개를 절차적으로 붙여
        /// 판자 한 장뿐이던 플레이스홀더를 실제 쉼터처럼 보이게 만든다.
        /// </summary>
        private void Awake()
        {
            transform.position += Vector3.up * roofHeight;
            BuildVisual();
        }

        /// <summary>
        /// 지붕 색을 이엉(초가) 색으로 바꾸고, 스케일이 비균일한 루트(지붕 Plane) 아래에
        /// 스케일 영향을 받지 않는 보정용 빈 오브젝트를 하나 만들어 그 밑에 기둥 4개를 붙인다.
        /// </summary>
        private void BuildVisual()
        {
            var rootRenderer = GetComponent<MeshRenderer>();
            if (rootRenderer != null)
                rootRenderer.sharedMaterial = StructureVisualBuilder.CreateColorMaterial(new Color(0.55f, 0.42f, 0.22f));

            // 루트가 지붕용으로 (4, 0.3, 4) 비균일 스케일되어 있어, 그 스케일을 상쇄하는 빈 부모를 만든다.
            var visualParts = new GameObject("VisualParts");
            visualParts.transform.SetParent(transform, false);
            Vector3 parentScale = transform.localScale;
            visualParts.transform.localScale = new Vector3(
                parentScale.x != 0f ? 1f / parentScale.x : 1f,
                parentScale.y != 0f ? 1f / parentScale.y : 1f,
                parentScale.z != 0f ? 1f / parentScale.z : 1f);

            Vector3[] legOffsets =
            {
                new Vector3(1.6f, 0f, 1.6f),
                new Vector3(-1.6f, 0f, 1.6f),
                new Vector3(1.6f, 0f, -1.6f),
                new Vector3(-1.6f, 0f, -1.6f),
            };

            foreach (var offset in legOffsets)
            {
                StructureVisualBuilder.CreateVisualPart(visualParts.transform, "Leg", PrimitiveType.Cylinder,
                    offset + Vector3.down * (roofHeight * 0.5f), new Vector3(0.12f, roofHeight * 0.5f, 0.12f),
                    new Color(0.35f, 0.22f, 0.1f));
            }
        }
    }
}
