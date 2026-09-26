using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace AltitudeZero.Editor
{
    /// <summary>
    /// Builds conservative box colliders under PLATEAU building objects. Never creates MeshColliders.
    /// Flat roof triangles define solid horizontal cells; unsupported or complex buildings are skipped.
    /// </summary>
    public static class CityBoxColliderTool
    {
        private const string GeneratedName = "__GeneratedBoxColliders";
        private const string SampleScenePath = "Assets/Scenes/SampleScene.unity";
        private const string CityPrefabGuid = "fee1e66bad70e404db93f4e8bd5e39ac";
        private const string BakedPrefabPath = "Assets/Generated/CityBoxColliders.prefab";

        // One metre cells preserve typical PLATEAU building footprints without making thousands of colliders.
        private const float CellSize = 1f;
        private const float FineCellSize = 0.5f;
        private const float CornerInset = 0.05f;
        private const float FlatHeightTolerance = 0.4f;
        private const float HeightGroupTolerance = 0.5f;
        private const float MinimumHeight = 1.5f;
        private const int MaximumGridCells = 12000;
        private const int MaximumBoxesPerBuilding = 32;
        private const float MinimumRoofCellCoverage = 0.45f;

        [Serializable]
        public sealed class Report
        {
            public int buildings;
            public int acceptedBuildings;
            public int skippedBuildings;
            public int boxes;
            public int vertices;
            public int roofTriangles;
            public int roofCells;
            public int fittedCells;
            public string[] skipped;
        }

        private struct RoofTriangle
        {
            public Vector3 a, b, c;
            public float minX, maxX, minZ, maxZ;
            public float denominator;
        }

        private struct Box
        {
            public Vector3 center;
            public Vector3 size;
        }

        private sealed class BuildingPlan
        {
            public Transform building;
            public readonly List<Box> boxes = new List<Box>();
            public string reason;
        }

        [MenuItem("Tools/Altitude Zero/City Box Colliders/Analyze Selected")]
        public static void AnalyzeSelected()
        {
            var root = Selection.activeGameObject;
            if (root == null)
            {
                Debug.LogWarning("Select the PLATEAU city root first.");
                return;
            }

            var report = Analyze(root.transform, out _);
            Debug.Log(FormatReport(report));
        }

        [MenuItem("Tools/Altitude Zero/City Box Colliders/Apply Selected")]
        public static void ApplySelected()
        {
            var root = Selection.activeGameObject;
            if (root == null || EditorUtility.IsPersistent(root))
            {
                Debug.LogWarning("Select a city root in the scene first.");
                return;
            }

            var report = Apply(root.transform, true);
            EditorSceneManager.MarkSceneDirty(root.scene);
            Debug.Log(FormatReport(report));
        }

        [MenuItem("Tools/Altitude Zero/City Box Colliders/Clear Selected")]
        public static void ClearSelected()
        {
            var root = Selection.activeGameObject;
            if (root == null || EditorUtility.IsPersistent(root)) return;
            int removed = ClearGenerated(root.transform, true);
            if (IsSampleCityRoot(root.transform)) removed += ClearBakedPrefab(root.scene, true);
            EditorSceneManager.MarkSceneDirty(root.scene);
            Debug.Log($"Removed {removed} generated collider containers under {root.name}.");
        }

        [MenuItem("Tools/Altitude Zero/City Box Colliders/Apply to SampleScene")]
        public static void ApplyToSampleScene()
        {
            var scene = SceneManager.GetSceneByPath(SampleScenePath);
            if (!scene.IsValid() || !scene.isLoaded)
            {
                Debug.LogWarning("Open SampleScene before running this command.");
                return;
            }

            var city = FindCity(scene);
            if (city == null)
            {
                Debug.LogError("PLATEAU city prefab was not found in SampleScene.");
                return;
            }

            var report = Apply(city, true);
            EditorSceneManager.MarkSceneDirty(scene);
            Debug.Log(FormatReport(report));
        }

        private static Transform FindCity(Scene scene)
        {
            foreach (var root in scene.GetRootGameObjects())
            {
                if (IsSampleCityRoot(root.transform))
                    return root.transform;
            }
            return null;
        }

        private static bool IsSampleCityRoot(Transform root)
        {
            return PrefabUtility.GetNearestPrefabInstanceRoot(root.gameObject) == root.gameObject &&
                   PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(root.gameObject) ==
                   AssetDatabase.GUIDToAssetPath(CityPrefabGuid);
        }

        private static Report Apply(Transform root, bool useUndo)
        {
            var report = Analyze(root, out var plans);
            ClearGenerated(root, useUndo);
            if (IsSampleCityRoot(root)) ClearBakedPrefab(root.gameObject.scene, useUndo);
            var generatedRoot = new GameObject(GeneratedName);
            if (useUndo) Undo.RegisterCreatedObjectUndo(generatedRoot, "Generate city box colliders");
            generatedRoot.transform.SetParent(root, false);
            foreach (var plan in plans)
            {
                if (plan.reason != null) continue;
                var container = new GameObject(plan.building.name);
                if (useUndo) Undo.RegisterCreatedObjectUndo(container, "Generate city box colliders");
                container.transform.SetParent(generatedRoot.transform, false);
                foreach (var box in plan.boxes)
                {
                    var collider = useUndo ? Undo.AddComponent<BoxCollider>(container) : container.AddComponent<BoxCollider>();
                    collider.center = box.center;
                    collider.size = box.size;
                }
            }
            return report;
        }

        private static int ClearGenerated(Transform root, bool useUndo)
        {
            var transforms = root.GetComponentsInChildren<Transform>(true);
            int removed = 0;
            for (int i = transforms.Length - 1; i >= 0; i--)
            {
                if (transforms[i].name != GeneratedName) continue;
                if (useUndo) Undo.DestroyObjectImmediate(transforms[i].gameObject);
                else UnityEngine.Object.DestroyImmediate(transforms[i].gameObject);
                removed++;
            }
            return removed;
        }

        private static int ClearBakedPrefab(Scene scene, bool useUndo)
        {
            int removed = 0;
            foreach (var gameObject in scene.GetRootGameObjects())
            {
                if (gameObject.name != "CityBoxColliders" ||
                    PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(gameObject) != BakedPrefabPath) continue;
                if (useUndo) Undo.DestroyObjectImmediate(gameObject);
                else UnityEngine.Object.DestroyImmediate(gameObject);
                removed++;
            }
            return removed;
        }

        private static Report Analyze(Transform root, out List<BuildingPlan> plans)
        {
            plans = new List<BuildingPlan>();
            var skipped = new List<string>();
            var report = new Report();
            foreach (var transform in root.GetComponentsInChildren<Transform>(true))
            {
                if (!transform.name.StartsWith("bldg_", StringComparison.OrdinalIgnoreCase)) continue;
                // Ignore any nested building node to keep one collider set per PLATEAU building.
                bool nested = false;
                for (var parent = transform.parent; parent != null && parent != root; parent = parent.parent)
                    if (parent.name.StartsWith("bldg_", StringComparison.OrdinalIgnoreCase)) nested = true;
                if (nested) continue;

                report.buildings++;
                int oldVertices = report.vertices;
                int oldTriangles = report.roofTriangles;
                int oldRoofCells = report.roofCells;
                int oldFittedCells = report.fittedCells;
                var plan = AnalyzeBuilding(transform, root, report, CellSize);
                if (plan.reason == "flat box coverage below 45%" || plan.reason == "no flat footprint cells")
                {
                    report.vertices = oldVertices;
                    report.roofTriangles = oldTriangles;
                    report.roofCells = oldRoofCells;
                    report.fittedCells = oldFittedCells;
                    plan = AnalyzeBuilding(transform, root, report, FineCellSize);
                }
                plans.Add(plan);
                if (plan.reason == null)
                {
                    report.acceptedBuildings++;
                    report.boxes += plan.boxes.Count;
                }
                else
                {
                    report.skippedBuildings++;
                    skipped.Add(transform.name + ": " + plan.reason);
                }
            }
            report.skipped = skipped.ToArray();
            return report;
        }

        private static BuildingPlan AnalyzeBuilding(Transform building, Transform root, Report report, float cellSize)
        {
            var plan = new BuildingPlan { building = building };
            var roofs = new List<RoofTriangle>();
            float minY = float.PositiveInfinity;
            float minX = float.PositiveInfinity, maxX = float.NegativeInfinity;
            float minZ = float.PositiveInfinity, maxZ = float.NegativeInfinity;

            foreach (var filter in building.GetComponentsInChildren<MeshFilter>(true))
            {
                bool belongsToNestedBuilding = false;
                for (var parent = filter.transform; parent != null && parent != building; parent = parent.parent)
                    if (parent.name.StartsWith("bldg_", StringComparison.OrdinalIgnoreCase)) belongsToNestedBuilding = true;
                if (belongsToNestedBuilding) continue;

                var mesh = filter.sharedMesh;
                if (mesh == null || !mesh.isReadable)
                {
                    plan.reason = "mesh is not readable";
                    return plan;
                }

                var source = mesh.vertices;
                var vertices = new Vector3[source.Length];
                report.vertices += source.Length;
                for (int i = 0; i < source.Length; i++)
                {
                    var point = root.InverseTransformPoint(filter.transform.TransformPoint(source[i]));
                    vertices[i] = point;
                    minY = Mathf.Min(minY, point.y);
                }

                for (int sub = 0; sub < mesh.subMeshCount; sub++)
                {
                    if (mesh.GetTopology(sub) != MeshTopology.Triangles) continue;
                    var indices = mesh.GetTriangles(sub);
                    for (int i = 0; i + 2 < indices.Length; i += 3)
                    {
                        var a = vertices[indices[i]];
                        var b = vertices[indices[i + 1]];
                        var c = vertices[indices[i + 2]];
                        var normal = Vector3.Cross(b - a, c - a);
                        // Vertical walls cannot define solid horizontal footprint cells.
                        if (Mathf.Abs(normal.y) < 0.35f * normal.magnitude) continue;
                        float denominator = (b.z - c.z) * (a.x - c.x) + (c.x - b.x) * (a.z - c.z);
                        if (Mathf.Abs(denominator) < 0.01f) continue;
                        var triangle = new RoofTriangle
                        {
                            a = a, b = b, c = c, denominator = denominator,
                            minX = Mathf.Min(a.x, Mathf.Min(b.x, c.x)),
                            maxX = Mathf.Max(a.x, Mathf.Max(b.x, c.x)),
                            minZ = Mathf.Min(a.z, Mathf.Min(b.z, c.z)),
                            maxZ = Mathf.Max(a.z, Mathf.Max(b.z, c.z))
                        };
                        roofs.Add(triangle);
                        minX = Mathf.Min(minX, triangle.minX);
                        maxX = Mathf.Max(maxX, triangle.maxX);
                        minZ = Mathf.Min(minZ, triangle.minZ);
                        maxZ = Mathf.Max(maxZ, triangle.maxZ);
                    }
                }
            }

            report.roofTriangles += roofs.Count;
            if (roofs.Count == 0 || float.IsInfinity(minY))
            {
                plan.reason = "no usable roof surfaces";
                return plan;
            }

            int width = Mathf.CeilToInt((maxX - minX) / cellSize);
            int depth = Mathf.CeilToInt((maxZ - minZ) / cellSize);
            if (width < 1 || depth < 1 || (long)width * depth > MaximumGridCells)
            {
                plan.reason = "footprint is too large or too narrow";
                return plan;
            }

            var heights = new float[width, depth];
            var groups = new int[width, depth];
            var filled = new bool[width, depth];
            int roofCells = 0;
            int fittedCells = 0;
            for (int x = 0; x < width; x++)
            for (int z = 0; z < depth; z++)
            {
                float x0 = minX + x * cellSize + CornerInset;
                float x1 = Mathf.Min(minX + (x + 1) * cellSize, maxX) - CornerInset;
                float z0 = minZ + z * cellSize + CornerInset;
                float z1 = Mathf.Min(minZ + (z + 1) * cellSize, maxZ) - CornerInset;
                if (x1 <= x0 || z1 <= z0) continue;

                float a = RoofHeight(roofs, x0, z0);
                float b = RoofHeight(roofs, x1, z0);
                float c = RoofHeight(roofs, x0, z1);
                float d = RoofHeight(roofs, x1, z1);
                float e = RoofHeight(roofs, (x0 + x1) * 0.5f, (z0 + z1) * 0.5f);
                if (!float.IsNegativeInfinity(e) && e - minY >= MinimumHeight) roofCells++;
                float low = Mathf.Min(e, Mathf.Min(a, Mathf.Min(b, Mathf.Min(c, d))));
                float high = Mathf.Max(e, Mathf.Max(a, Mathf.Max(b, Mathf.Max(c, d))));
                if (float.IsNegativeInfinity(low) || high - low > FlatHeightTolerance || low - minY < MinimumHeight)
                    continue;
                heights[x, z] = low;
                groups[x, z] = Mathf.RoundToInt(low / HeightGroupTolerance);
                filled[x, z] = true;
                fittedCells++;
            }

            report.roofCells += roofCells;
            report.fittedCells += fittedCells;
            if (roofCells == 0 || (float)fittedCells / roofCells < MinimumRoofCellCoverage)
            {
                plan.reason = "flat box coverage below 45%";
                return plan;
            }

            var used = new bool[width, depth];
            for (int z = 0; z < depth; z++)
            for (int x = 0; x < width; x++)
            {
                int group = groups[x, z];
                if (!filled[x, z] || used[x, z]) continue;
                int runWidth = 1;
                while (x + runWidth < width && filled[x + runWidth, z] && !used[x + runWidth, z] && groups[x + runWidth, z] == group)
                    runWidth++;
                int runDepth = 1;
                bool extend = true;
                while (z + runDepth < depth && extend)
                {
                    for (int k = 0; k < runWidth; k++)
                        if (!filled[x + k, z + runDepth] || used[x + k, z + runDepth] || groups[x + k, z + runDepth] != group) extend = false;
                    if (extend) runDepth++;
                }

                float top = float.PositiveInfinity;
                for (int dz = 0; dz < runDepth; dz++)
                for (int dx = 0; dx < runWidth; dx++)
                {
                    used[x + dx, z + dz] = true;
                    top = Mathf.Min(top, heights[x + dx, z + dz]);
                }
                float left = minX + x * cellSize + CornerInset;
                float right = Mathf.Min(minX + (x + runWidth) * cellSize, maxX) - CornerInset;
                float front = minZ + z * cellSize + CornerInset;
                float back = Mathf.Min(minZ + (z + runDepth) * cellSize, maxZ) - CornerInset;
                plan.boxes.Add(new Box
                {
                    center = new Vector3((left + right) * 0.5f, (minY + top) * 0.5f, (front + back) * 0.5f),
                    size = new Vector3(right - left, top - minY, back - front)
                });
                if (plan.boxes.Count <= MaximumBoxesPerBuilding) continue;
                plan.boxes.Clear();
                plan.reason = "needs more than " + MaximumBoxesPerBuilding + " boxes";
                return plan;
            }

            if (plan.boxes.Count == 0) plan.reason = "no flat footprint cells";
            return plan;
        }

        private static float RoofHeight(List<RoofTriangle> roofs, float x, float z)
        {
            float height = float.NegativeInfinity;
            foreach (var triangle in roofs)
            {
                if (x < triangle.minX || x > triangle.maxX || z < triangle.minZ || z > triangle.maxZ) continue;
                float u = ((triangle.b.z - triangle.c.z) * (x - triangle.c.x) +
                           (triangle.c.x - triangle.b.x) * (z - triangle.c.z)) / triangle.denominator;
                float v = ((triangle.c.z - triangle.a.z) * (x - triangle.c.x) +
                           (triangle.a.x - triangle.c.x) * (z - triangle.c.z)) / triangle.denominator;
                if (u < -0.0001f || v < -0.0001f || u + v > 1.0001f) continue;
                height = Mathf.Max(height, u * triangle.a.y + v * triangle.b.y + (1f - u - v) * triangle.c.y);
            }
            return height;
        }

        private static string FormatReport(Report report)
        {
            return $"City Box Colliders: {report.acceptedBuildings}/{report.buildings} buildings fitted, " +
                   $"{report.boxes} boxes, {report.skippedBuildings} skipped. " +
                   $"Measured {report.vertices} vertices, {report.roofTriangles} horizontal triangles, " +
                   $"and {report.fittedCells}/{report.roofCells} supported roof cells. " +
                   (report.skippedBuildings > 0 ? "First skipped: " + string.Join("; ", new ArraySegment<string>(report.skipped, 0, Mathf.Min(8, report.skipped.Length))) : "");
        }
    }
}
