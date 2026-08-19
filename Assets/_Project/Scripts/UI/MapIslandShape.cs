using UnityEngine;
using UnityEngine.UI;

namespace MakeGame.UI
{
    /// <summary>
    /// 전체 지도의 "상세" 단계에서 섬을 **실제 해안선 모양**으로 그리는 도형.
    ///
    /// 왜 필요한가: 이 프로젝트에는 스프라이트가 한 장도 없어서 uGUI로 원 말고는 그릴 수가 없다.
    /// 그런데 섬마다 64샘플 방사형 윤곽(`MaldivesLayout.Entry.mask`)이 이미 있고 지형이 그 값으로
    /// 만들어진다. 그 배열을 그대로 폴리곤으로 채우면 **지도의 섬 모양과 실제 섬 모양이 일치**한다.
    ///
    /// 계약(`WorldMapManager.GetMaldivesRadialMask` 주석과 동일하게 지켜야 한다):
    /// · 0번 샘플 = **+X축**, 이후 **반시계** 등간격
    /// · 하한 `MaskFloor`로 클램프 — 지형 쪽(IslandMeshGenerator.MaskAt)이 쓰는 값과 같아야
    ///   지도와 실제 섬의 실루엣이 어긋나지 않는다
    /// · 지도는 +Y가 월드 +Z(북)이고 +X가 그대로 오른쪽이라, 월드 각도를 그대로 쓰면 된다
    ///
    /// **반드시 지형에 주입된 것과 같은 배열을 넣어라.** 시작 섬(0번)은 회전된 사본을 쓰므로
    /// 원본 데이터를 그냥 넘기면 시작 섬만 모양이 돌아간다.
    /// </summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public class MapIslandShape : MaskableGraphic
    {
        /// <summary>지형 쪽과 같은 하한. 이 값보다 오목한 곳은 없다고 본다.</summary>
        public const float MaskFloor = 0.15f;

        private float[] mask;
        private float radiusPixels = 6f;

        /// <summary>
        /// 윤곽과 크기를 정한다. 값이 실제로 바뀐 경우에만 메시를 다시 만든다 —
        /// 지도는 매 프레임 갱신되는데 SetVerticesDirty를 그때마다 부르면 50섬 × 64각형을
        /// 매 프레임 다시 굽게 된다.
        /// </summary>
        public void Configure(float[] radialMask, float pixelRadius)
        {
            bool changed = !ReferenceEquals(mask, radialMask)
                || !Mathf.Approximately(radiusPixels, pixelRadius);

            mask = radialMask;
            radiusPixels = pixelRadius;

            if (changed)
                SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();

            int count = mask != null ? mask.Length : 0;
            if (count < 3 || radiusPixels <= 0.01f)
                return;

            Color32 c = color;

            // 중심 정점 하나 + 둘레 정점 n개로 삼각형 부채꼴을 만든다.
            var center = UIVertex.simpleVert;
            center.color = c;
            center.position = Vector3.zero;
            vh.AddVert(center);

            float step = Mathf.PI * 2f / count;
            for (int i = 0; i < count; i++)
            {
                float r = Mathf.Max(mask[i], MaskFloor) * radiusPixels;
                float a = step * i;   // 0번 = +X, 반시계
                var v = UIVertex.simpleVert;
                v.color = c;
                v.position = new Vector3(Mathf.Cos(a) * r, Mathf.Sin(a) * r, 0f);
                vh.AddVert(v);
            }

            for (int i = 0; i < count; i++)
                vh.AddTriangle(0, 1 + i, 1 + (i + 1) % count);
        }
    }
}
