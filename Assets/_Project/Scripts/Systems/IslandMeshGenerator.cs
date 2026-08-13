using System.Collections.Generic;
using UnityEngine;

namespace MakeGame.Systems
{
    /// <summary>
    /// 섬 지형용 절차적 메시를 생성하는 유틸리티.
    /// 밋밋한 원기둥 플레이스홀더 대신, 중심이 살짝 높고 가장자리로 갈수록 완만하게 낮아지며
    /// 약간의 굴곡(펄린 노이즈)이 있는, 실제로 걸어다닐 수 있는 낮은 언덕 모양의 지형을 만든다.
    /// </summary>
    public static class IslandMeshGenerator
    {
        /// <summary>
        /// 지정한 반지름과 최대 높이를 가진 둥근 언덕 모양의 섬 지형 메시를 생성한다.
        /// 중심에서 가장자리로 갈수록 코사인 곡선으로 완만하게 낮아지고,
        /// 펄린 노이즈로 자연스러운 굴곡을 더하되 가장자리에서는 노이즈를 줄여 매끄럽게 물과 맞닿게 한다.
        /// </summary>
        public static Mesh GenerateIslandMesh(float radius, float maxHeight, int ringCount = 6, int radialSegments = 24, float noiseScale = 0.15f, float noiseAmplitude = 0.6f)
        {
            var mesh = new Mesh();
            mesh.name = "IslandTerrain";

            int vertexCount = 1 + ringCount * radialSegments;
            var vertices = new Vector3[vertexCount];
            var uvs = new Vector2[vertexCount];

            // 중심점 (인덱스 0)
            vertices[0] = new Vector3(0f, maxHeight, 0f);
            uvs[0] = new Vector2(0.5f, 0.5f);

            int index = 1;
            for (int ring = 1; ring <= ringCount; ring++)
            {
                float t = (float)ring / ringCount; // 0(중심)~1(가장자리)
                float r = t * radius;
                float baseHeight = maxHeight * Mathf.Cos(t * Mathf.PI * 0.5f);

                for (int seg = 0; seg < radialSegments; seg++)
                {
                    float angle = (float)seg / radialSegments * Mathf.PI * 2f;
                    float x = Mathf.Cos(angle) * r;
                    float z = Mathf.Sin(angle) * r;

                    float noise = (Mathf.PerlinNoise(x * noiseScale + 1000f, z * noiseScale + 1000f) - 0.5f) * noiseAmplitude * (1f - t);
                    float y = Mathf.Max(0f, baseHeight + noise);

                    vertices[index] = new Vector3(x, y, z);
                    uvs[index] = new Vector2(x / radius * 0.5f + 0.5f, z / radius * 0.5f + 0.5f);
                    index++;
                }
            }

            var triangles = new List<int>();

            // 중심 -> 첫 번째 링 (부채꼴)
            // 주의: c, b 순서로 추가해야 위쪽을 향하는 법선이 나온다 (b, c 순서면 아래를 향해 컬링되어
            // 지형 중앙에 구멍이 뚫린 것처럼 보이고, 콜라이더도 그 구멍으로 플레이어가 빠지는 버그가 있었다).
            for (int seg = 0; seg < radialSegments; seg++)
            {
                int b = 1 + seg;
                int c = 1 + (seg + 1) % radialSegments;
                triangles.Add(0);
                triangles.Add(c);
                triangles.Add(b);
            }

            // 링과 링 사이 (사각형을 삼각형 2개로)
            for (int ring = 1; ring < ringCount; ring++)
            {
                int ringStart = 1 + (ring - 1) * radialSegments;
                int nextRingStart = 1 + ring * radialSegments;

                for (int seg = 0; seg < radialSegments; seg++)
                {
                    int a = ringStart + seg;
                    int b = ringStart + (seg + 1) % radialSegments;
                    int c = nextRingStart + seg;
                    int d = nextRingStart + (seg + 1) % radialSegments;

                    triangles.Add(a);
                    triangles.Add(b);
                    triangles.Add(d);

                    triangles.Add(a);
                    triangles.Add(d);
                    triangles.Add(c);
                }
            }

            mesh.vertices = vertices;
            mesh.uv = uvs;
            mesh.triangles = triangles.ToArray();
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            return mesh;
        }
    }
}
