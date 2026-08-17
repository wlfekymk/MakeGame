using System.Collections.Generic;
using UnityEngine;

namespace Maldives
{
    /// <summary>
    /// MaldivesShape 를 유니티 Mesh 로 만듭니다.
    /// 정점은 shape.origin 기준 로컬 좌표이므로, 만든 메시를 붙일 GameObject 의
    /// 위치를 origin 으로 두면 됩니다 (float 정밀도 유지).
    /// </summary>
    public static class MaldivesMeshBuilder
    {
        /// <param name="extrudeHeight">0 이면 평면 한 장, 0보다 크면 옆면까지 있는 입체.</param>
        /// <param name="uvPerMeter">UV 타일링 밀도. 0.01 이면 100 m 마다 UV 1칸.</param>
        /// <param name="flipWinding">면이 반대로 보이면 이 값을 뒤집으세요.</param>
        public static Mesh Build(MaldivesShape shape,
                                 MaldivesAxis axis = MaldivesAxis.XZ,
                                 float unitsPerMeter = 1f,
                                 float extrudeHeight = 0f,
                                 float uvPerMeter = 0.01f,
                                 bool flipWinding = true)
        {
            if (shape == null || shape.verts == null || shape.verts.Length < 6) return null;

            int n = shape.VertexCount;
            var verts = new List<Vector3>(n * 2);
            var uvs = new List<Vector2>(n * 2);
            var tris = new List<int>(shape.tris.Length * 3);

            float top = extrudeHeight > 0f ? extrudeHeight * unitsPerMeter : 0f;

            // ---- 윗면 ----
            for (int i = 0; i < n; i++)
            {
                Vector2 p = shape.LocalVertex(i);
                verts.Add(MaldivesGeo.PlaneToWorld(p, top, axis, unitsPerMeter));
                uvs.Add(new Vector2(p.x * uvPerMeter, p.y * uvPerMeter));
            }
            for (int i = 0; i < shape.tris.Length; i += 3)
                AddTri(tris, shape.tris[i], shape.tris[i + 1], shape.tris[i + 2], flipWinding);

            if (extrudeHeight > 0f)
            {
                // ---- 아랫면 ----
                int baseIdx = verts.Count;
                for (int i = 0; i < n; i++)
                {
                    Vector2 p = shape.LocalVertex(i);
                    verts.Add(MaldivesGeo.PlaneToWorld(p, 0f, axis, unitsPerMeter));
                    uvs.Add(new Vector2(p.x * uvPerMeter, p.y * uvPerMeter));
                }
                for (int i = 0; i < shape.tris.Length; i += 3)
                    AddTri(tris, baseIdx + shape.tris[i + 2], baseIdx + shape.tris[i + 1],
                                 baseIdx + shape.tris[i], flipWinding);

                // ---- 옆면 ----
                for (int r = 0; r < shape.RingCount; r++)
                {
                    int start = shape.ringStarts[r], len = shape.RingLength(r);
                    if (len < 3) continue;
                    for (int k = 0; k < len; k++)
                    {
                        int a = start + k, b = start + (k + 1) % len;
                        int bp = verts.Count;
                        Vector2 pa = shape.LocalVertex(a), pb = shape.LocalVertex(b);
                        float edge = Vector2.Distance(pa, pb) * uvPerMeter;
                        float hv = extrudeHeight * uvPerMeter;
                        verts.Add(MaldivesGeo.PlaneToWorld(pa, 0f, axis, unitsPerMeter));
                        verts.Add(MaldivesGeo.PlaneToWorld(pb, 0f, axis, unitsPerMeter));
                        verts.Add(MaldivesGeo.PlaneToWorld(pb, top, axis, unitsPerMeter));
                        verts.Add(MaldivesGeo.PlaneToWorld(pa, top, axis, unitsPerMeter));
                        uvs.Add(new Vector2(0f, 0f)); uvs.Add(new Vector2(edge, 0f));
                        uvs.Add(new Vector2(edge, hv)); uvs.Add(new Vector2(0f, hv));
                        AddTri(tris, bp, bp + 1, bp + 2, flipWinding);
                        AddTri(tris, bp, bp + 2, bp + 3, flipWinding);
                    }
                }
            }

            var mesh = new Mesh();
            mesh.name = shape.id;
            if (verts.Count > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(verts);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        static void AddTri(List<int> t, int a, int b, int c, bool flip)
        {
            if (flip) { t.Add(c); t.Add(b); t.Add(a); }
            else { t.Add(a); t.Add(b); t.Add(c); }
        }

        /// <summary>외곽선을 LineRenderer 용 점 배열로. 링 하나씩 돌려줍니다.</summary>
        public static List<Vector3[]> Outlines(MaldivesShape shape,
                                               MaldivesAxis axis = MaldivesAxis.XZ,
                                               float unitsPerMeter = 1f,
                                               float height = 0f)
        {
            var result = new List<Vector3[]>();
            if (shape == null || shape.ringStarts == null) return result;
            for (int r = 0; r < shape.RingCount; r++)
            {
                int start = shape.ringStarts[r], len = shape.RingLength(r);
                if (len < 3) continue;
                var pts = new Vector3[len];
                for (int k = 0; k < len; k++)
                    pts[k] = MaldivesGeo.PlaneToWorld(shape.LocalVertex(start + k), height, axis, unitsPerMeter);
                result.Add(pts);
            }
            return result;
        }
    }
}
