using System;
using UnityEngine;

namespace Maldives
{
    /// <summary>
    /// maldives_map.json 의 스키마. UnityEngine.JsonUtility 로 그대로 파싱됩니다.
    /// (중첩 배열을 쓰지 않도록 ringStarts / verts / tris 를 1차원으로 펼쳐 두었습니다.)
    /// </summary>
    [Serializable]
    public class MaldivesMapData
    {
        public string format;
        public MaldivesProjection projection;
        public MaldivesBounds bounds;
        public int zoneCount;
        public int islandCount;
        public MaldivesShape[] shapes;

        public static MaldivesMapData Parse(string json)
        {
            return JsonUtility.FromJson<MaldivesMapData>(json);
        }

        public static MaldivesMapData Parse(TextAsset asset)
        {
            return asset == null ? null : Parse(asset.text);
        }

        public MaldivesShape Find(string id)
        {
            if (shapes == null) return null;
            for (int i = 0; i < shapes.Length; i++)
                if (shapes[i].id == id) return shapes[i];
            return null;
        }
    }

    [Serializable]
    public class MaldivesProjection
    {
        public string type;
        public double lon0;
        public double lat0;
        public double metersPerDegLat;
        public double metersPerDegLonEquator;
        public string note;
    }

    [Serializable]
    public class MaldivesBounds
    {
        public float minX, maxX, minZ, maxZ;
        public float SizeX { get { return maxX - minX; } }
        public float SizeZ { get { return maxZ - minZ; } }
    }

    /// <summary>
    /// 섬 하나 또는 환초 구역 하나.
    /// verts 는 originX/originZ 를 기준으로 한 **로컬** 좌표(미터)이며 (x,z) 쌍으로 펼쳐져 있습니다.
    /// float 정밀도를 지키려면 GameObject 위치를 origin 으로 두고 verts 를 그대로 메시에 쓰세요.
    /// </summary>
    [Serializable]
    public class MaldivesShape
    {
        public string id;          // "Z15" (환초) / "Z15-076" (섬)
        public string kind;        // "zone" | "island"
        public string zone;        // 소속 환초 ID
        public float originX;      // 월드 좌표(미터)
        public float originZ;
        public float lat;
        public float lon;
        public float areaKm2;
        public float sizeX;        // 바운딩 박스 크기(미터)
        public float sizeZ;
        public int islandCount;    // 환초일 때 소속 섬 개수
        public string refName;     // 참고용 실제 지명 (환초만)
        public int[] ringStarts;   // 각 외곽 링이 verts 에서 시작하는 정점 인덱스
        public float[] verts;      // x0,z0, x1,z1, ... (로컬 미터)
        public int[] tris;         // 삼각분할 인덱스 (verts 기준)

        public bool IsIsland { get { return kind == "island"; } }
        public int VertexCount { get { return verts == null ? 0 : verts.Length / 2; } }
        public int RingCount { get { return ringStarts == null ? 0 : ringStarts.Length; } }

        public Vector2 LocalVertex(int i)
        {
            return new Vector2(verts[i * 2], verts[i * 2 + 1]);
        }

        public int RingLength(int ring)
        {
            int start = ringStarts[ring];
            int end = (ring + 1 < ringStarts.Length) ? ringStarts[ring + 1] : VertexCount;
            return end - start;
        }
    }
}
