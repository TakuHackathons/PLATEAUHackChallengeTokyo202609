using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace AltitudeZero.Editor
{
    /// <summary>都市 Prefab を指定して Collider 付き Prefab と再適用用 C# を作る。</summary>
    public sealed class CityColliderPrefabWindow : EditorWindow
    {
        private const string GeneratedChildName = "__GeneratedBoxColliders";
        private const string OutputFolder = "Assets/Generated/CityColliderPrefabs";
        private const string ScriptFolder = "Assets/Editor/Generated";

        [SerializeField] private GameObject sourcePrefab;
        private string lastResult;

        [MenuItem("Tools/Altitude Zero/City Collider Prefab Window")]
        public static void Open() => GetWindow<CityColliderPrefabWindow>("City Colliders");

        private void OnGUI()
        {
            EditorGUILayout.LabelField("都市 3D Prefab", EditorStyles.boldLabel);
            sourcePrefab = (GameObject)EditorGUILayout.ObjectField(
                "Prefab / FBX", sourcePrefab, typeof(GameObject), false);
            EditorGUILayout.HelpBox(
                "Prefab または FBX をここへドラッグします。BoxCollider を優先し、" +
                "形を分割できない場合も大きめの Box で覆います。" +
                "3D には PolygonCollider がないため、必要な形は複数の Box で近似します。",
                MessageType.Info);

            var valid = TryGetPrefabPath(sourcePrefab, out _, out var error);
            if (!valid && sourcePrefab != null) EditorGUILayout.HelpBox(error, MessageType.Warning);
            using (new EditorGUI.DisabledScope(!valid))
            {
                if (GUILayout.Button("Apply", GUILayout.Height(32)))
                {
                    try
                    {
                        var result = ApplyPrefab(sourcePrefab);
                        lastResult = result.outputPath + "\n" + result.scriptPath + "\n" +
                                     result.report.buildingBoxes + " building boxes / " +
                                     result.report.groundBoxes + " ground boxes / " +
                                     result.report.meshBuildings + " MeshColliders";
                        Debug.Log("都市 Collider を生成しました: " + lastResult);
                    }
                    catch (Exception exception)
                    {
                        Debug.LogException(exception);
                        lastResult = exception.Message;
                    }
                }
            }
            if (!string.IsNullOrEmpty(lastResult)) EditorGUILayout.HelpBox(lastResult, MessageType.None);
        }

        public struct ApplyResult
        {
            public string outputPath;
            public string scriptPath;
            public CityBoxColliderTool.Report report;
        }

        public static ApplyResult ApplyPrefabByGuid(string guid)
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null) throw new InvalidOperationException("都市 Prefab が見つかりません: " + guid);
            return ApplyPrefab(prefab);
        }

        public static ApplyResult ApplyPrefab(GameObject prefab)
        {
            if (!TryGetPrefabPath(prefab, out var sourcePath, out var error))
                throw new ArgumentException(error);

            var isModel = PrefabUtility.GetPrefabAssetType(prefab) == PrefabAssetType.Model;
            var outputPath = isModel
                ? OutputFolder + "/" + Path.GetFileNameWithoutExtension(sourcePath) + "_WithColliders.prefab"
                : sourcePath;
            if (isModel) EnsureAssetFolder(OutputFolder);

            GameObject root = null;
            try
            {
                root = isModel
                    ? (GameObject)PrefabUtility.InstantiatePrefab(prefab)
                    : PrefabUtility.LoadPrefabContents(sourcePath);
                if (root == null) throw new InvalidOperationException("Prefab を開けません: " + sourcePath);
                var existing = root.transform.Find(GeneratedChildName);
                if (existing != null) DestroyImmediate(existing.gameObject);

                var generated = CityBoxColliderTool.BuildColliderObject(root.transform, out var report, true);
                generated.transform.SetParent(root.transform, false);
                if (report.groundBoxes + report.buildingBoxes + report.convexBuildings +
                    report.meshBuildings == 0)
                    throw new InvalidOperationException("Mesh が見つからないため Collider を生成できませんでした。");

                PrefabUtility.SaveAsPrefabAsset(root, outputPath);
                var scriptPath = WriteReplayScript(AssetDatabase.AssetPathToGUID(sourcePath), sourcePath);
                AssetDatabase.SaveAssets();
                return new ApplyResult { outputPath = outputPath, scriptPath = scriptPath, report = report };
            }
            finally
            {
                if (root != null)
                {
                    if (isModel) DestroyImmediate(root);
                    else PrefabUtility.UnloadPrefabContents(root);
                }
            }
        }

        private static bool TryGetPrefabPath(GameObject prefab, out string path, out string error)
        {
            path = prefab == null ? null : AssetDatabase.GetAssetPath(prefab);
            error = "Project 内の Prefab または FBX ファイルを指定してください。";
            if (string.IsNullOrEmpty(path) || !path.StartsWith("Assets/", StringComparison.Ordinal)) return false;
            var type = PrefabUtility.GetPrefabAssetType(prefab);
            if (type != PrefabAssetType.Regular && type != PrefabAssetType.Variant &&
                type != PrefabAssetType.Model) return false;
            return true;
        }

        private static string WriteReplayScript(string sourceGuid, string sourcePath)
        {
            EnsureAssetFolder(ScriptFolder);
            var className = "CityColliderReplay_" + sourceGuid;
            var scriptPath = ScriptFolder + "/" + className + ".cs";
            // GUID だけをコードに埋め込む。Prefab を移動しても再実行できる。
            var script = "// Generated by CityColliderPrefabWindow for " + sourcePath + "\n" +
                         "// Apply と同じ Mesh 計測を再実行して Collider を更新します。\n" +
                         "namespace AltitudeZero.Editor.Generated\n{\n" +
                         "    public static class " + className + "\n    {\n" +
                         "        [UnityEditor.MenuItem(\"Tools/Altitude Zero/Replay City Collider/" +
                         sourceGuid + "\")]\n" +
                         "        public static void Apply()\n        {\n" +
                         "            AltitudeZero.Editor.CityColliderPrefabWindow.ApplyPrefabByGuid(\"" + sourceGuid + "\");\n" +
                         "        }\n    }\n}\n";
            File.WriteAllText(scriptPath, script);
            AssetDatabase.ImportAsset(scriptPath);
            return scriptPath;
        }

        private static void EnsureAssetFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            var parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
            if (!string.IsNullOrEmpty(parent) && !AssetDatabase.IsValidFolder(parent)) EnsureAssetFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }
}
