using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Maldives.EditorTools
{
    /// <summary>
    /// Tools ▸ Maldives 메뉴.
    ///  - Create Map In Scene : 로더가 붙은 GameObject 를 만들고 바로 지형을 생성
    ///  - Bake Meshes To Assets : 섬/환초 메시를 .asset 으로 구워서 프로젝트에 저장
    /// </summary>
    public static class MaldivesMapMenu
    {
        const string MeshPackPath = "Assets/MaldivesMap/Data/maldives_meshes.bytes";
        const string JsonPath = "Assets/MaldivesMap/Data/maldives_map.json";
        const string BakeFolder = "Assets/MaldivesMap/GeneratedMeshes";

        [MenuItem("Tools/Maldives/Create Map In Scene", false, 10)]
        public static void CreateInScene()
        {
            var go = new GameObject("Maldives Map");
            var loader = go.AddComponent<MaldivesMapLoader>();
            loader.meshPack = AssetDatabase.LoadAssetAtPath<TextAsset>(MeshPackPath);
            loader.jsonData = AssetDatabase.LoadAssetAtPath<TextAsset>(JsonPath);
            if (loader.meshPack == null && loader.jsonData == null)
                Debug.LogWarning("데이터 파일을 " + MeshPackPath + " 경로에서 찾지 못했습니다. 인스펙터에서 직접 지정하세요.");
            else
                loader.BuildNow();

            Undo.RegisterCreatedObjectUndo(go, "Create Maldives Map");
            Selection.activeGameObject = go;
        }

        [MenuItem("Tools/Maldives/Bake Meshes To Assets", false, 11)]
        public static void BakeMeshes()
        {
            var pack = AssetDatabase.LoadAssetAtPath<TextAsset>(MeshPackPath);
            var json = AssetDatabase.LoadAssetAtPath<TextAsset>(JsonPath);
            List<MaldivesShape> shapes = null;
            if (pack != null) shapes = MaldivesMeshPack.Read(pack);
            else if (json != null)
            {
                var d = MaldivesMapData.Parse(json);
                if (d != null && d.shapes != null) shapes = new List<MaldivesShape>(d.shapes);
            }
            if (shapes == null || shapes.Count == 0)
            {
                EditorUtility.DisplayDialog("Maldives", "데이터 파일을 찾지 못했습니다.\n" + MeshPackPath, "확인");
                return;
            }

            Directory.CreateDirectory(BakeFolder);
            try
            {
                AssetDatabase.StartAssetEditing();
                for (int i = 0; i < shapes.Count; i++)
                {
                    var s = shapes[i];
                    if (EditorUtility.DisplayCancelableProgressBar("Maldives 메시 굽는 중",
                            s.id + "  (" + (i + 1) + "/" + shapes.Count + ")", (float)i / shapes.Count))
                        break;
                    var mesh = MaldivesMeshBuilder.Build(s, MaldivesAxis.XZ, 1f,
                                                         s.IsIsland ? 2f : 0f, 0.01f, true);
                    if (mesh == null) continue;
                    AssetDatabase.CreateAsset(mesh, BakeFolder + "/" + s.id + ".asset");
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
                EditorUtility.ClearProgressBar();
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }
            Debug.Log("Maldives: 메시 " + shapes.Count + "개를 " + BakeFolder + " 에 저장했습니다.");
        }

        [MenuItem("Tools/Maldives/Log Coordinate Check", false, 30)]
        public static void CoordinateCheck()
        {
            LogPoint("말레 Malé", 4.1755, 73.5093);
            LogPoint("벨라나 공항", 4.1917, 73.5292);
            LogPoint("아두 간", -0.6934, 73.1556);
            LogPoint("하니마두", 6.7442, 73.1705);
            double d = MaldivesGeo.DistanceMeters(4.1755, 73.5093, -0.6934, 73.1556) / 1000.0;
            Debug.Log(string.Format("말레 ↔ 간 : {0:F2} km (실제 측지선 539.82 km)", d));
        }

        static void LogPoint(string name, double lat, double lon)
        {
            var w = MaldivesGeo.LatLonToWorld(lat, lon, 0f);
            Debug.Log(string.Format("{0}  world=({1:F0}, {2:F0}, {3:F0})  cell={4}",
                                    name, w.x, w.y, w.z, MaldivesGeo.CellName(lat, lon)));
        }
    }
}
