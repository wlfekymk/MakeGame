using System.Collections.Generic;
using UnityEngine;

namespace Maldives
{
    /// <summary>
    /// 씬에 몰디브 지형을 만들어 주는 메인 컴포넌트.
    ///
    /// 사용법
    ///  1. 빈 GameObject 에 이 스크립트를 붙입니다.
    ///  2. Mesh Pack 에 Data/maldives_meshes.bytes 를 (또는 Json Data 에 maldives_map.json 을) 넣습니다.
    ///  3. 인스펙터 우클릭 → "Build Now" 또는 플레이하면 생성됩니다.
    ///
    /// 실제 크기는 남북 약 820 km 입니다. Units Per Meter 를 0.001 로 두면 전체가 820 유닛으로 줄어듭니다.
    /// </summary>
    [DisallowMultipleComponent]
    public class MaldivesMapLoader : MonoBehaviour
    {
        [Header("데이터 (둘 중 하나만 있으면 됩니다)")]
        [Tooltip("Data/maldives_meshes.bytes — 빠름. 권장.")]
        public TextAsset meshPack;
        [Tooltip("Data/maldives_map.json — 사람이 읽을 수 있는 형식.")]
        public TextAsset jsonData;

        [Header("좌표계")]
        public MaldivesAxis axis = MaldivesAxis.XZ;
        [Tooltip("1 = 1 유닛이 1 m (실제 크기). 0.001 이면 1 유닛 = 1 km.")]
        public float unitsPerMeter = 1f;

        [Header("생성할 레이어")]
        public bool buildIslands = true;
        public bool buildZones = true;
        [Tooltip("이 면적(km²) 미만인 섬은 건너뜁니다. 0 이면 전부 생성.")]
        public float minIslandAreaKm2 = 0f;

        [Header("모양")]
        [Tooltip("섬을 이 높이(m)만큼 밀어 올립니다. 0 이면 평면.")]
        public float islandExtrudeMeters = 2f;
        [Tooltip("환초(석호) 면을 놓을 높이(m). 보통 섬보다 살짝 아래.")]
        public float zonePlaneMeters = -0.5f;
        public float uvPerMeter = 0.01f;
        [Tooltip("면이 뒤집혀 보이면 이 값을 바꿔 보세요.")]
        public bool flipWinding = true;

        [Header("머티리얼 / 콜라이더")]
        public Material islandMaterial;
        public Material zoneMaterial;
        public bool addMeshColliders = false;
        [Tooltip("섬 GameObject 에 붙일 레이어 이름. 비워 두면 기본 레이어.")]
        public string islandLayer = "";

        [Header("실행")]
        public bool buildOnStart = true;

        readonly Dictionary<string, MaldivesShape> _byId = new Dictionary<string, MaldivesShape>();
        readonly Dictionary<string, Transform> _spawned = new Dictionary<string, Transform>();
        Transform _root;

        public IEnumerable<MaldivesShape> Shapes { get { return _byId.Values; } }

        void Start()
        {
            if (buildOnStart && _root == null) BuildNow();
        }

        [ContextMenu("Build Now")]
        public void BuildNow()
        {
            ClearNow();
            var shapes = LoadShapes();
            if (shapes.Count == 0)
            {
                Debug.LogError("MaldivesMapLoader: 데이터가 비어 있습니다. meshPack 또는 jsonData 를 지정하세요.", this);
                return;
            }

            _root = new GameObject("MaldivesMap").transform;
            _root.SetParent(transform, false);
            var zoneRoot = new GameObject("Zones").transform;   zoneRoot.SetParent(_root, false);
            var islandRoot = new GameObject("Islands").transform; islandRoot.SetParent(_root, false);

            int layer = string.IsNullOrEmpty(islandLayer) ? -1 : LayerMask.NameToLayer(islandLayer);

            foreach (var s in shapes)
            {
                _byId[s.id] = s;
                bool isZone = !s.IsIsland;
                if (isZone && !buildZones) continue;
                if (!isZone && !buildIslands) continue;
                if (!isZone && s.areaKm2 < minIslandAreaKm2) continue;

                float extrude = isZone ? 0f : islandExtrudeMeters;
                var mesh = MaldivesMeshBuilder.Build(s, axis, unitsPerMeter, extrude, uvPerMeter, flipWinding);
                if (mesh == null) continue;

                var go = new GameObject(s.id);
                go.transform.SetParent(isZone ? zoneRoot : islandRoot, false);
                go.transform.localPosition = MaldivesGeo.PlaneToWorld(
                    new Vector2(s.originX, s.originZ),
                    (isZone ? zonePlaneMeters : 0f) * unitsPerMeter, axis, unitsPerMeter);

                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = isZone ? zoneMaterial : islandMaterial;

                if (addMeshColliders && !isZone)
                    go.AddComponent<MeshCollider>().sharedMesh = mesh;
                if (!isZone && layer >= 0) go.layer = layer;

                var tag = go.AddComponent<MaldivesFeature>();
                tag.Init(s);

                _spawned[s.id] = go.transform;
            }

            Debug.Log(string.Format("MaldivesMap: {0}개 오브젝트 생성 (전체 {1}개 도형)", _spawned.Count, shapes.Count));
        }

        [ContextMenu("Clear")]
        public void ClearNow()
        {
            _byId.Clear();
            _spawned.Clear();
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                var c = transform.GetChild(i);
                if (c.name != "MaldivesMap") continue;
                if (Application.isPlaying) Destroy(c.gameObject);
                else DestroyImmediate(c.gameObject);
            }
            _root = null;
        }

        public List<MaldivesShape> LoadShapes()
        {
            if (meshPack != null) return MaldivesMeshPack.Read(meshPack);
            if (jsonData != null)
            {
                var data = MaldivesMapData.Parse(jsonData);
                if (data != null && data.shapes != null) return new List<MaldivesShape>(data.shapes);
            }
            return new List<MaldivesShape>();
        }

        // ---------- 편의 API ----------

        public Vector3 WorldOf(double lat, double lon, float height = 0f)
        {
            return transform.TransformPoint(MaldivesGeo.LatLonToWorld(lat, lon, height, axis, unitsPerMeter));
        }

        public Vector2 LatLonOf(Vector3 world)
        {
            return MaldivesGeo.WorldToLatLon(transform.InverseTransformPoint(world), axis, unitsPerMeter);
        }

        public Transform Get(string id)
        {
            Transform t;
            return _spawned.TryGetValue(id, out t) ? t : null;
        }

        /// <summary>지정한 월드 위치에서 가장 가까운 섬을 찾습니다.</summary>
        public MaldivesShape NearestIsland(Vector3 world)
        {
            Vector2 ll = LatLonOf(world);
            MaldivesShape best = null;
            double bestD = double.MaxValue;
            foreach (var s in _byId.Values)
            {
                if (!s.IsIsland) continue;
                double d = MaldivesGeo.DistanceMeters(ll.x, ll.y, s.lat, s.lon);
                if (d < bestD) { bestD = d; best = s; }
            }
            return best;
        }
    }

    /// <summary>생성된 오브젝트에 붙는 메타데이터 태그.</summary>
    public class MaldivesFeature : MonoBehaviour
    {
        public string id;
        public string kind;
        public string zone;
        public float latitude;
        public float longitude;
        public float areaKm2;
        public string refName;

        public void Init(MaldivesShape s)
        {
            id = s.id; kind = s.kind; zone = s.zone;
            latitude = s.lat; longitude = s.lon; areaKm2 = s.areaKm2; refName = s.refName;
        }

        public string CellName { get { return MaldivesGeo.CellName(latitude, longitude); } }
    }
}
