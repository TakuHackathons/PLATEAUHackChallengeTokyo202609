using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace AltitudeZero.Editor
{
    /// <summary>PLATEAU 都市 Mesh に BoxCollider を優先して貼り、複雑な形だけ MeshCollider にする。</summary>
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
        private const float BuildingCellSize = 10f;
        private const int MaximumBuildingBoxes = 32;
        private const int MaximumConvexTriangles = 64;

        [Serializable]
        public sealed class Report
        {
            public int demMeshes;
            public int groundBoxes;
            public int groundSkippedTiles;
            public int buildings;
            public int buildingBoxes;
            public int boxedBuildings;
            public int convexBuildings;
            public int meshBuildings;
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

        private struct FootprintTriangle
        {
            public Vector2 a, b, c;
            public float minX, maxX, minZ, maxZ;
        }

        private sealed class MeshPart
        {
            public Mesh mesh;
            public Matrix4x4 localToRoot;
        }

        private sealed class BuildingPlan
        {
            public string name;
            public readonly List<Box> boxes = new List<Box>();
            public readonly List<MeshPart> meshes = new List<MeshPart>();
            public bool convex;
        }

        private sealed class Plan
        {
            public readonly List<Box> ground = new List<Box>();
            public readonly List<BuildingPlan> buildings = new List<BuildingPlan>();
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
                var building = new GameObject(entry.name);
                building.transform.SetParent(buildings.transform, false);
                foreach (var box in entry.boxes) AddBox(building, box);
                foreach (var part in entry.meshes) AddMeshCollider(building, part, entry.convex);
            }
            return result;
        }

        [MenuItem("Tools/Altitude Zero/City Box Colliders/Rebuild Sample Collider Prefab")]
        public static void RebuildSampleColliderPrefab()
        {
            var path = AssetDatabase.GUIDToAssetPath(CityPrefabGuid);
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (model == null) throw new InvalidOperationException("PLATEAU 都市 FBX が見つかりません: " + path);

            var city = (GameObject)PrefabUtility.InstantiatePrefab(model);
            GameObject generated = null;
            try
            {
                // SampleScene の都市 Prefab インスタンスと同じ Transform にして計測する。
                city.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                city.transform.localScale = Vector3.one;
                generated = BuildColliderObject(city.transform, out var report);
                generated.name = "CityBoxColliders";
                PrefabUtility.SaveAsPrefabAsset(generated, BakedPrefabPath);
                Debug.Log(Format(report));
            }
            finally
            {
                if (generated != null) UnityEngine.Object.DestroyImmediate(generated);
                UnityEngine.Object.DestroyImmediate(city);
            }
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

        private static void AddMeshCollider(GameObject parent, MeshPart part, bool convex)
        {
            var target = new GameObject(parent.name + " Mesh");
            target.transform.SetParent(parent.transform, false);
            target.transform.localPosition = part.localToRoot.GetPosition();
            target.transform.localRotation = part.localToRoot.rotation;
            target.transform.localScale = part.localToRoot.lossyScale;
            var collider = target.AddComponent<MeshCollider>();
            collider.sharedMesh = part.mesh;
            collider.convex = convex;
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
                var buildingPlan = AnalyzeBuilding(transform, cityRoot);
                if (buildingPlan == null)
                {
                    plan.report.skippedBuildings++;
                    skipped.Add(transform.name);
                }
                else
                {
                    plan.buildings.Add(buildingPlan);
                    if (buildingPlan.boxes.Count > 0)
                    {
                        plan.report.boxedBuildings++;
                        plan.report.buildingBoxes += buildingPlan.boxes.Count;
                    }
                    else if (buildingPlan.convex) plan.report.convexBuildings++;
                    else plan.report.meshBuildings++;
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
            plan.report.skipped = skipped.ToArray();
            return plan;
        }

        private static BuildingPlan AnalyzeBuilding(Transform building, Transform cityRoot)
        {
            var result = new BuildingPlan { name = building.name };
            var bounds = new Bounds();
            var found = false;
            var canReadAll = true;
            var totalTriangles = 0;
            var triangles = new List<FootprintTriangle>();
            foreach (var filter in building.GetComponentsInChildren<MeshFilter>(true))
            {
                bool nested = false;
                for (var parent = filter.transform; parent != null && parent != building; parent = parent.parent)
                    if (parent.name.StartsWith("bldg_", StringComparison.OrdinalIgnoreCase)) nested = true;
                if (nested) continue;

                var mesh = filter.sharedMesh;
                if (mesh == null) continue;
                var localToRoot = cityRoot.worldToLocalMatrix * filter.transform.localToWorldMatrix;
                result.meshes.Add(new MeshPart { mesh = mesh, localToRoot = localToRoot });
                if (!mesh.isReadable) { canReadAll = false; continue; }
                var vertices = mesh.vertices;
                var points = new Vector3[vertices.Length];
                for (var i = 0; i < vertices.Length; i++)
                {
                    var point = localToRoot.MultiplyPoint3x4(vertices[i]);
                    points[i] = point;
                    if (!found) { bounds = new Bounds(point, Vector3.zero); found = true; }
                    else bounds.Encapsulate(point);
                }
                for (var sub = 0; sub < mesh.subMeshCount; sub++)
                {
                    if (mesh.GetTopology(sub) != MeshTopology.Triangles) continue;
                    var indices = mesh.GetTriangles(sub);
                    totalTriangles += indices.Length / 3;
                    for (var i = 0; i + 2 < indices.Length; i += 3)
                    {
                        var a = points[indices[i]];
                        var b = points[indices[i + 1]];
                        var c = points[indices[i + 2]];
                        triangles.Add(new FootprintTriangle
                        {
                            a = new Vector2(a.x, a.z), b = new Vector2(b.x, b.z), c = new Vector2(c.x, c.z),
                            minX = Mathf.Min(a.x, b.x, c.x), maxX = Mathf.Max(a.x, b.x, c.x),
                            minZ = Mathf.Min(a.z, b.z, c.z), maxZ = Mathf.Max(a.z, b.z, c.z)
                        });
                    }
                }
            }
            if (result.meshes.Count == 0) return null;
            var sideRatio = found
                ? Mathf.Max(bounds.size.x, bounds.size.z) / Mathf.Max(0.1f, Mathf.Min(bounds.size.x, bounds.size.z))
                : float.PositiveInfinity;
            // 細長い低ポリゴンの建物は屋根や通路の下に空間がある場合がある。
            // 平面の占有範囲を建物の全高に広げた Box では通行可能な空間まで塞いでしまう。
            var needsSurfaceCollider = result.meshes.Count == 1 && canReadAll &&
                totalTriangles > 0 && totalTriangles <= MaximumConvexTriangles && sideRatio > 4f;
            if (!needsSurfaceCollider && canReadAll && found && triangles.Count > 0 &&
                TryBuildBoxes(bounds, triangles, result.boxes))
            {
                result.meshes.Clear();
                return result;
            }

            result.boxes.Clear();
            result.convex = result.meshes.Count == 1 && canReadAll &&
                totalTriangles > 0 && totalTriangles <= MaximumConvexTriangles && sideRatio <= 3f;
            return result;
        }

        private static bool TryBuildBoxes(Bounds bounds, List<FootprintTriangle> triangles, List<Box> boxes)
        {
            var cellSize = Mathf.Max(BuildingCellSize,
                Mathf.Max(bounds.size.x, bounds.size.z) / 16f);
            var xCount = Mathf.Max(1, Mathf.CeilToInt(bounds.size.x / cellSize));
            var zCount = Mathf.Max(1, Mathf.CeilToInt(bounds.size.z / cellSize));
            var occupied = new bool[xCount, zCount];
            var xStep = Mathf.Max(0.001f, bounds.size.x / xCount);
            var zStep = Mathf.Max(0.001f, bounds.size.z / zCount);

            foreach (var triangle in triangles)
            {
                var x0 = Mathf.Clamp(Mathf.FloorToInt((triangle.minX - bounds.min.x) / xStep), 0, xCount - 1);
                var x1 = Mathf.Clamp(Mathf.FloorToInt((triangle.maxX - bounds.min.x) / xStep), 0, xCount - 1);
                var z0 = Mathf.Clamp(Mathf.FloorToInt((triangle.minZ - bounds.min.z) / zStep), 0, zCount - 1);
                var z1 = Mathf.Clamp(Mathf.FloorToInt((triangle.maxZ - bounds.min.z) / zStep), 0, zCount - 1);
                for (var x = x0; x <= x1; x++)
                for (var z = z0; z <= z1; z++)
                {
                    if (occupied[x, z]) continue;
                    var center = new Vector2(bounds.min.x + (x + 0.5f) * xStep,
                        bounds.min.z + (z + 0.5f) * zStep);
                    if (TriangleOverlapsCell(triangle, center, new Vector2(xStep, zStep) * 0.5f))
                        occupied[x, z] = true;
                }
            }

            var used = new bool[xCount, zCount];
            for (var z = 0; z < zCount; z++)
            for (var x = 0; x < xCount; x++)
            {
                if (!occupied[x, z] || used[x, z]) continue;
                var endX = x + 1;
                while (endX < xCount && occupied[endX, z] && !used[endX, z]) endX++;
                var endZ = z + 1;
                while (endZ < zCount)
                {
                    var complete = true;
                    for (var checkX = x; checkX < endX; checkX++)
                        if (!occupied[checkX, endZ] || used[checkX, endZ]) complete = false;
                    if (!complete) break;
                    endZ++;
                }
                for (var markX = x; markX < endX; markX++)
                for (var markZ = z; markZ < endZ; markZ++) used[markX, markZ] = true;

                var minX = bounds.min.x + x * xStep;
                var maxX = Mathf.Min(bounds.max.x, bounds.min.x + endX * xStep);
                var minZ = bounds.min.z + z * zStep;
                var maxZ = Mathf.Min(bounds.max.z, bounds.min.z + endZ * zStep);
                boxes.Add(new Box
                {
                    center = new Vector3((minX + maxX) * 0.5f, bounds.center.y, (minZ + maxZ) * 0.5f),
                    size = new Vector3(Mathf.Max(0.1f, maxX - minX) + BuildingHorizontalPadding,
                        Mathf.Max(0.1f, bounds.size.y),
                        Mathf.Max(0.1f, maxZ - minZ) + BuildingHorizontalPadding)
                });
                if (boxes.Count > MaximumBuildingBoxes) return false;
            }
            return boxes.Count > 0;
        }

        private static bool TriangleOverlapsCell(FootprintTriangle triangle, Vector2 center, Vector2 halfSize)
        {
            if (triangle.maxX < center.x - halfSize.x || triangle.minX > center.x + halfSize.x ||
                triangle.maxZ < center.y - halfSize.y || triangle.minZ > center.y + halfSize.y)
                return false;
            var vertices = new[] { triangle.a, triangle.b, triangle.c };
            for (var i = 0; i < 3; i++)
            {
                var edge = vertices[(i + 1) % 3] - vertices[i];
                var axis = new Vector2(-edge.y, edge.x);
                if (axis.sqrMagnitude < 0.000001f) continue;
                var low = float.PositiveInfinity;
                var high = float.NegativeInfinity;
                for (var vertex = 0; vertex < 3; vertex++)
                {
                    var projected = Vector2.Dot(vertices[vertex], axis);
                    low = Mathf.Min(low, projected);
                    high = Mathf.Max(high, projected);
                }
                var projectedCenter = Vector2.Dot(center, axis);
                var projectedRadius = halfSize.x * Mathf.Abs(axis.x) + halfSize.y * Mathf.Abs(axis.y);
                if (high < projectedCenter - projectedRadius - 0.0001f ||
                    low > projectedCenter + projectedRadius + 0.0001f) return false;
            }
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
            return $"City Colliders: ground {report.groundBoxes} boxes from {report.demMeshes} DEM meshes " +
                   $"({report.groundSkippedTiles} tiles skipped); buildings {report.boxedBuildings} with " +
                   $"{report.buildingBoxes} boxes, {report.convexBuildings} convex MeshColliders, " +
                   $"{report.meshBuildings} static MeshColliders, {report.skippedBuildings} without Mesh " +
                   $"(total {report.buildings}).";
        }
    }
}
