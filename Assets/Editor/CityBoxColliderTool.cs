using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace AltitudeZero.Editor
{
    /// <summary>Coarse, repeatable BoxCollider fitting for a PLATEAU city FBX.</summary>
    public static class CityBoxColliderTool
    {
        private const string GeneratedName = "__GeneratedBoxColliders";
        private const string SampleScenePath = "Assets/Scenes/SampleScene.unity";
        private const string CityPrefabGuid = "fee1e66bad70e404db93f4e8bd5e39ac";
        private const string BakedPrefabPath = "Assets/Generated/CityBoxColliders.prefab";

        private const float GroundTileSize = 40f;
        private const float MinimumGroundTileSize = 10f;
        private const float GroundSplitHeightRange = 2f;
        private const float BuildingHorizontalPadding = 0.5f;
        private const float MinimumBuildingHeight = 2f;
        private const float MaximumBuildingSide = 80f;

        [Serializable]
        public sealed class Report
        {
            public int demMeshes;
            public int groundBoxes;
            public int groundSkippedTiles;
            public int buildings;
            public int buildingBoxes;
            public int skippedBuildings;
            public string[] skipped;
        }

        private struct Box
        {
            public Vector3 center;
            public Vector3 size;
        }

        private struct TerrainTriangle
        {
            public Vector3 a, b, c;
            public float minX, maxX, minZ, maxZ, denominator;
        }

        private sealed class Plan
        {
            public readonly List<Box> ground = new List<Box>();
            public readonly List<KeyValuePair<string, Box>> buildings = new List<KeyValuePair<string, Box>>();
            public Report report;
        }

        [MenuItem("Tools/Altitude Zero/City Box Colliders/Analyze Selected")]
        public static void AnalyzeSelected()
        {
            var selected = Selection.activeGameObject;
            if (selected == null)
            {
                Debug.LogWarning("Select the PLATEAU city root first.");
                return;
            }
            Debug.Log(Format(Analyze(selected.transform).report));
        }

        [MenuItem("Tools/Altitude Zero/City Box Colliders/Apply Selected")]
        public static void ApplySelected()
        {
            var selected = Selection.activeGameObject;
            if (selected == null || EditorUtility.IsPersistent(selected))
            {
                Debug.LogWarning("Select a city root in the scene first.");
                return;
            }
            Apply(selected.transform);
        }

        [MenuItem("Tools/Altitude Zero/City Box Colliders/Clear Selected")]
        public static void ClearSelected()
        {
            var selected = Selection.activeGameObject;
            if (selected == null || EditorUtility.IsPersistent(selected)) return;
            ClearGenerated(selected.transform);
            if (IsSampleCityRoot(selected.transform)) ClearBakedPrefab(selected.scene);
            EditorSceneManager.MarkSceneDirty(selected.scene);
        }

        [MenuItem("Tools/Altitude Zero/City Box Colliders/Apply to SampleScene")]
        public static void ApplyToSampleScene()
        {
            var scene = SceneManager.GetSceneByPath(SampleScenePath);
            if (!scene.IsValid() || !scene.isLoaded)
            {
                Debug.LogWarning("Open SampleScene first.");
                return;
            }
            foreach (var gameObject in scene.GetRootGameObjects())
            {
                if (!IsSampleCityRoot(gameObject.transform)) continue;
                Apply(gameObject.transform);
                return;
            }
            Debug.LogError("The PLATEAU city root was not found in SampleScene.");
        }

        /// <summary>Build a standalone collider object for the selected city root or an FBX instance.</summary>
        public static GameObject BuildColliderObject(Transform cityRoot, out Report report)
        {
            var plan = Analyze(cityRoot);
            report = plan.report;
            var result = new GameObject(GeneratedName);

            var ground = new GameObject("Ground");
            ground.transform.SetParent(result.transform, false);
            foreach (var box in plan.ground) AddBox(ground, box);

            var buildings = new GameObject("Buildings");
            buildings.transform.SetParent(result.transform, false);
            foreach (var entry in plan.buildings)
            {
                var building = new GameObject(entry.Key);
                building.transform.SetParent(buildings.transform, false);
                AddBox(building, entry.Value);
            }
            return result;
        }

        private static void Apply(Transform cityRoot)
        {
            var generated = BuildColliderObject(cityRoot, out var report);
            ClearGenerated(cityRoot);
            if (IsSampleCityRoot(cityRoot)) ClearBakedPrefab(cityRoot.gameObject.scene);
            Undo.RegisterCreatedObjectUndo(generated, "Generate city box colliders");
            generated.transform.SetParent(cityRoot, false);
            EditorSceneManager.MarkSceneDirty(cityRoot.gameObject.scene);
            Debug.Log(Format(report));
        }

        private static void AddBox(GameObject target, Box box)
        {
            var collider = target.AddComponent<BoxCollider>();
            collider.center = box.center;
            collider.size = box.size;
        }

        private static void ClearGenerated(Transform root)
        {
            foreach (var child in root.GetComponentsInChildren<Transform>(true))
                if (child.name == GeneratedName && child != root)
                    Undo.DestroyObjectImmediate(child.gameObject);
        }

        private static void ClearBakedPrefab(Scene scene)
        {
            foreach (var gameObject in scene.GetRootGameObjects())
                if (gameObject.name == "CityBoxColliders" &&
                    PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(gameObject) == BakedPrefabPath)
                    Undo.DestroyObjectImmediate(gameObject);
        }

        private static bool IsSampleCityRoot(Transform root)
        {
            return PrefabUtility.GetNearestPrefabInstanceRoot(root.gameObject) == root.gameObject &&
                   PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(root.gameObject) ==
                   AssetDatabase.GUIDToAssetPath(CityPrefabGuid);
        }

        private static Plan Analyze(Transform cityRoot)
        {
            var plan = new Plan { report = new Report() };
            var terrain = new List<TerrainTriangle>();
            var skipped = new List<string>();
            float groundMinY = float.PositiveInfinity;
            float groundMinX = float.PositiveInfinity, groundMaxX = float.NegativeInfinity;
            float groundMinZ = float.PositiveInfinity, groundMaxZ = float.NegativeInfinity;

            foreach (var transform in cityRoot.GetComponentsInChildren<Transform>(true))
            {
                if (transform.name.StartsWith("dem_", StringComparison.OrdinalIgnoreCase))
                {
                    foreach (var filter in transform.GetComponentsInChildren<MeshFilter>(true))
                    {
                        if (filter.sharedMesh == null || !filter.sharedMesh.isReadable) continue;
                        plan.report.demMeshes++;
                        var source = filter.sharedMesh.vertices;
                        var vertices = new Vector3[source.Length];
                        for (int i = 0; i < source.Length; i++)
                        {
                            var point = cityRoot.InverseTransformPoint(filter.transform.TransformPoint(source[i]));
                            vertices[i] = point;
                            groundMinY = Mathf.Min(groundMinY, point.y);
                            groundMinX = Mathf.Min(groundMinX, point.x);
                            groundMaxX = Mathf.Max(groundMaxX, point.x);
                            groundMinZ = Mathf.Min(groundMinZ, point.z);
                            groundMaxZ = Mathf.Max(groundMaxZ, point.z);
                        }
                        for (int sub = 0; sub < filter.sharedMesh.subMeshCount; sub++)
                        {
                            if (filter.sharedMesh.GetTopology(sub) != MeshTopology.Triangles) continue;
                            var indices = filter.sharedMesh.GetTriangles(sub);
                            for (int i = 0; i + 2 < indices.Length; i += 3)
                            {
                                var a = vertices[indices[i]];
                                var b = vertices[indices[i + 1]];
                                var c = vertices[indices[i + 2]];
                                float denominator = (b.z - c.z) * (a.x - c.x) + (c.x - b.x) * (a.z - c.z);
                                if (Mathf.Abs(denominator) < 0.001f) continue;
                                terrain.Add(new TerrainTriangle
                                {
                                    a = a, b = b, c = c, denominator = denominator,
                                    minX = Mathf.Min(a.x, Mathf.Min(b.x, c.x)),
                                    maxX = Mathf.Max(a.x, Mathf.Max(b.x, c.x)),
                                    minZ = Mathf.Min(a.z, Mathf.Min(b.z, c.z)),
                                    maxZ = Mathf.Max(a.z, Mathf.Max(b.z, c.z))
                                });
                            }
                        }
                    }
                }
                if (!transform.name.StartsWith("bldg_", StringComparison.OrdinalIgnoreCase)) continue;
                bool nested = false;
                for (var parent = transform.parent; parent != null && parent != cityRoot; parent = parent.parent)
                    if (parent.name.StartsWith("bldg_", StringComparison.OrdinalIgnoreCase)) nested = true;
                if (nested) continue;

                plan.report.buildings++;
                if (TryBuildingBox(transform, cityRoot, out var box))
                    plan.buildings.Add(new KeyValuePair<string, Box>(transform.name, box));
                else
                {
                    plan.report.skippedBuildings++;
                    skipped.Add(transform.name);
                }
            }

            if (terrain.Count > 0)
            {
                for (float x = groundMinX; x < groundMaxX; x += GroundTileSize)
                for (float z = groundMinZ; z < groundMaxZ; z += GroundTileSize)
                    AddGroundTile(terrain, plan, x, Mathf.Min(x + GroundTileSize, groundMaxX),
                        z, Mathf.Min(z + GroundTileSize, groundMaxZ), groundMinY - 5f);
            }
            plan.report.groundBoxes = plan.ground.Count;
            plan.report.buildingBoxes = plan.buildings.Count;
            plan.report.skipped = skipped.ToArray();
            return plan;
        }

        private static bool TryBuildingBox(Transform building, Transform cityRoot, out Box box)
        {
            box = default;
            var bounds = new Bounds();
            bool found = false;
            foreach (var filter in building.GetComponentsInChildren<MeshFilter>(true))
            {
                bool nested = false;
                for (var parent = filter.transform; parent != null && parent != building; parent = parent.parent)
                    if (parent.name.StartsWith("bldg_", StringComparison.OrdinalIgnoreCase)) nested = true;
                if (nested) continue;
                var mesh = filter.sharedMesh;
                if (mesh == null || !mesh.isReadable) return false;
                foreach (var vertex in mesh.vertices)
                {
                    var point = cityRoot.InverseTransformPoint(filter.transform.TransformPoint(vertex));
                    if (!found) { bounds = new Bounds(point, Vector3.zero); found = true; }
                    else bounds.Encapsulate(point);
                }
            }
            if (!found || bounds.size.y < MinimumBuildingHeight ||
                bounds.size.x < 0.5f || bounds.size.z < 0.5f ||
                bounds.size.x > MaximumBuildingSide || bounds.size.z > MaximumBuildingSide)
                return false;
            box = new Box
            {
                center = bounds.center,
                size = new Vector3(bounds.size.x + BuildingHorizontalPadding, bounds.size.y,
                    bounds.size.z + BuildingHorizontalPadding)
            };
            return true;
        }

        private static void AddGroundTile(List<TerrainTriangle> terrain, Plan plan,
            float x0, float x1, float z0, float z1, float bottom)
        {
            var heights = new List<float>(9);
            for (int x = 0; x < 3; x++)
            for (int z = 0; z < 3; z++)
            {
                float sampleX = Mathf.Lerp(x0, x1, 0.1f + 0.4f * x);
                float sampleZ = Mathf.Lerp(z0, z1, 0.1f + 0.4f * z);
                float height = TerrainHeight(terrain, sampleX, sampleZ);
                if (!float.IsNegativeInfinity(height)) heights.Add(height);
            }
            if (heights.Count < 5)
            {
                plan.report.groundSkippedTiles++;
                return;
            }
            heights.Sort();
            if (heights[heights.Count - 1] - heights[0] > GroundSplitHeightRange &&
                x1 - x0 > MinimumGroundTileSize && z1 - z0 > MinimumGroundTileSize)
            {
                float middleX = (x0 + x1) * 0.5f;
                float middleZ = (z0 + z1) * 0.5f;
                AddGroundTile(terrain, plan, x0, middleX, z0, middleZ, bottom);
                AddGroundTile(terrain, plan, middleX, x1, z0, middleZ, bottom);
                AddGroundTile(terrain, plan, x0, middleX, middleZ, z1, bottom);
                AddGroundTile(terrain, plan, middleX, x1, middleZ, z1, bottom);
                return;
            }
            float top = heights[heights.Count / 2];
            plan.ground.Add(new Box
            {
                center = new Vector3((x0 + x1) * 0.5f, (bottom + top) * 0.5f, (z0 + z1) * 0.5f),
                size = new Vector3(x1 - x0, top - bottom, z1 - z0)
            });
        }

        private static float TerrainHeight(List<TerrainTriangle> terrain, float x, float z)
        {
            float height = float.NegativeInfinity;
            foreach (var triangle in terrain)
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

        private static string Format(Report report)
        {
            return $"City Box Colliders: ground {report.groundBoxes} boxes from {report.demMeshes} DEM meshes " +
                   $"({report.groundSkippedTiles} tiles skipped); buildings {report.buildingBoxes}/{report.buildings} " +
                   $"boxes ({report.skippedBuildings} skipped).";
        }
    }
}
