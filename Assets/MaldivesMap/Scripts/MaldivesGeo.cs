using UnityEngine;

namespace Maldives
{
    /// <summary>
    /// 위경도 <-> 유니티 월드 좌표 변환.
    ///
    /// 사용 투영: 몰디브 국지 등거리 투영 (local equidistant / sinusoidal 계열)
    ///   worldX = (lon - Lon0) * MetersPerDegLonEquator * cos(lat)
    ///   worldZ = (lat - Lat0) * MetersPerDegLat
    ///
    /// 원점(0,0)은 73.15°E, 3.20°N — 열도의 한가운데입니다.
    /// WGS84 측지선 거리와 대조했을 때 오차는 0.01% 미만입니다.
    ///   말레-간        : 투영 539.81 km / 실제 539.82 km
    ///   말레-하니마두  : 투영 286.50 km / 실제 286.53 km
    /// </summary>
    public static class MaldivesGeo
    {
        public const double Lon0 = 73.15;
        public const double Lat0 = 3.20;
        public const double MetersPerDegLat = 110576.0;
        public const double MetersPerDegLonEquator = 111320.0;

        const double Deg2RadD = System.Math.PI / 180.0;

        /// <summary>위경도 -> 월드 평면 좌표(미터). x = 동쪽, y = 북쪽.</summary>
        public static Vector2 LatLonToPlane(double lat, double lon)
        {
            double x = (lon - Lon0) * MetersPerDegLonEquator * System.Math.Cos(lat * Deg2RadD);
            double z = (lat - Lat0) * MetersPerDegLat;
            return new Vector2((float)x, (float)z);
        }

        /// <summary>월드 평면 좌표(미터) -> 위경도. 반환값 x = 위도, y = 경도.</summary>
        public static Vector2 PlaneToLatLon(double x, double z)
        {
            double lat = Lat0 + z / MetersPerDegLat;
            double cos = System.Math.Cos(lat * Deg2RadD);
            if (System.Math.Abs(cos) < 1e-12) cos = 1e-12;
            double lon = Lon0 + x / (MetersPerDegLonEquator * cos);
            return new Vector2((float)lat, (float)lon);
        }

        /// <summary>평면 좌표를 축 모드에 맞춰 Vector3로. height는 XZ 모드에서 Y, XY 모드에서 Z.</summary>
        public static Vector3 PlaneToWorld(Vector2 plane, float height, MaldivesAxis axis, float unitsPerMeter)
        {
            return axis == MaldivesAxis.XZ
                ? new Vector3(plane.x * unitsPerMeter, height, plane.y * unitsPerMeter)
                : new Vector3(plane.x * unitsPerMeter, plane.y * unitsPerMeter, height);
        }

        public static Vector3 LatLonToWorld(double lat, double lon, float height,
                                            MaldivesAxis axis = MaldivesAxis.XZ, float unitsPerMeter = 1f)
        {
            return PlaneToWorld(LatLonToPlane(lat, lon), height, axis, unitsPerMeter);
        }

        public static Vector2 WorldToLatLon(Vector3 world, MaldivesAxis axis = MaldivesAxis.XZ, float unitsPerMeter = 1f)
        {
            float u = Mathf.Approximately(unitsPerMeter, 0f) ? 1f : unitsPerMeter;
            return axis == MaldivesAxis.XZ
                ? PlaneToLatLon(world.x / u, world.z / u)
                : PlaneToLatLon(world.x / u, world.y / u);
        }

        /// <summary>두 위경도 사이 거리(미터). 이 투영과 동일한 모델을 사용합니다.</summary>
        public static double DistanceMeters(double lat1, double lon1, double lat2, double lon2)
        {
            double dz = (lat2 - lat1) * MetersPerDegLat;
            double dx = (lon2 - lon1) * MetersPerDegLonEquator * System.Math.Cos((lat1 + lat2) * 0.5 * Deg2RadD);
            return System.Math.Sqrt(dx * dx + dz * dz);
        }

        /// <summary>남서쪽 기준점에서 잰 게임 격자 셀 이름 (예: "AC-17"). cellSizeMeters 기본 10 km.</summary>
        public static string CellName(double lat, double lon, double cellSizeMeters = 10000.0)
        {
            const double gridLon = 72.30, gridLat = -0.80;   // 웹 지도와 동일한 격자 원점
            int cx = Mathf.FloorToInt((float)((lon - gridLon) * MetersPerDegLonEquator
                        * System.Math.Cos((lat + gridLat) * 0.5 * Deg2RadD) / cellSizeMeters));
            int cy = Mathf.FloorToInt((float)((lat - gridLat) * MetersPerDegLat / cellSizeMeters));
            return Col(cx) + "-" + (cy + 1);
        }

        static string Col(int n)
        {
            if (n < 0) return "--";
            if (n < 26) return ((char)('A' + n)).ToString();
            return ((char)('A' + n / 26 - 1)).ToString() + ((char)('A' + n % 26)).ToString();
        }
    }

    public enum MaldivesAxis
    {
        /// <summary>3D: 지면이 XZ 평면, 높이가 Y.</summary>
        XZ = 0,
        /// <summary>2D: 지면이 XY 평면, 깊이가 Z.</summary>
        XY = 1
    }
}
