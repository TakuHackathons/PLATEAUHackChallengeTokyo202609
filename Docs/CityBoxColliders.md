# PLATEAU city BoxCollider tool

The SampleScene collision prefab is `Assets/Generated/CityBoxColliders.prefab`. It contains only BoxColliders: **361 for the DEM ground** and **396 for 407 buildings**, for **757 total**. Eleven buildings that do not meet the size checks are left without colliders. The old flat temporary ground collider has been removed.

## Reuse in the Unity Editor

Select the root of a PLATEAU city model in the Hierarchy, then use **Tools > Altitude Zero > City Box Colliders**:

- **Analyze Selected** reports the planned collider count without editing the scene.
- **Apply Selected** creates `__GeneratedBoxColliders` under the selected city root. Repeating it replaces the prior generated colliders. In SampleScene, it also removes the supplied baked prefab instance to avoid duplicate colliders. Save the scene afterward.
- **Clear Selected** removes generated colliders and, in SampleScene, the supplied baked prefab instance.
- **Apply to SampleScene** finds the city model in the open SampleScene and applies the same process.

The script is `Assets/Editor/CityBoxColliderTool.cs`. The FBX import must allow mesh reading so the Editor can measure its geometry. The collider fitting happens in the Editor; it does not add MeshColliders at runtime.

The tool samples triangles in `dem_*` meshes to estimate terrain height. It covers the DEM with 40 m square boxes and subdivides tiles where sampled heights differ by more than 2 m, down to approximately 10 m. Each box rises to the median sampled height. This gives a coarse, stepped ground surface; check steep slopes, tile edges, and the FPS spawn position in Play mode.

The FPS spawn in SampleScene is set near the generated ground at `(0, 38.5, 110)`; the ground collider there reaches approximately `y = 37.48`.

For each `bldg_*` group, the tool measures the bounds of all its mesh vertices and creates **one solid BoxCollider** with 0.5 m total horizontal padding. It skips groups with unreadable meshes, very small dimensions, or a width/depth over 80 m instead of making an unreliable box. This intentionally fills courtyards and overhangs within a building's bounds. Inspect entrances or narrow passages that need bespoke collision.
