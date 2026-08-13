using UnityEngine;

namespace MakeGame.Systems
{
    /// <summary>
    /// 설치형 구조물(물 증류기, 쉼터 등)의 시각적 파츠를 절차적으로 만들어주는 공용 유틸리티.
    /// 프리미티브 하나짜리 밋밋한 플레이스홀더 대신, 여러 프리미티브를 조합해 형태를 갖추게 하는 데 쓴다.
    /// </summary>
    public static class StructureVisualBuilder
    {
        /// <summary>
        /// 지정한 프리미티브로 순수 시각용 파츠를 만들어 parent의 자식으로 붙인다.
        /// 자동으로 생성되는 콜라이더는 제거해 부모의 상호작용용 콜라이더와 중복/간섭되지 않게 한다.
        /// </summary>
        public static GameObject CreateVisualPart(Transform parent, string name, PrimitiveType primitiveType,
            Vector3 localPosition, Vector3 localScale, Color color, Quaternion? localRotation = null)
        {
            var go = GameObject.CreatePrimitive(primitiveType);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            go.transform.localRotation = localRotation ?? Quaternion.identity;
            go.transform.localScale = localScale;

            // 시각 전용 파츠이므로 프리미티브 생성 시 자동으로 붙는 콜라이더는 제거한다.
            var collider = go.GetComponent<Collider>();
            if (collider != null)
                Object.Destroy(collider);

            var renderer = go.GetComponent<MeshRenderer>();
            if (renderer != null)
                renderer.sharedMaterial = CreateColorMaterial(color);

            return go;
        }

        /// <summary>
        /// 지정한 단색의 기본 URP Lit 머티리얼을 만든다 (섬 지형 생성 시 사용한 것과 동일한 방식).
        /// </summary>
        public static Material CreateColorMaterial(Color color)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            var material = new Material(shader != null ? shader : Shader.Find("Standard"));
            material.color = color;
            return material;
        }
    }
}
