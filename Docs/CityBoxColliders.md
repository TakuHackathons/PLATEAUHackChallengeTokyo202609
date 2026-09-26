# PLATEAU city BoxCollider tool

`Assets/Editor/CityBoxColliderTool.cs` adds these Unity Editor menu commands:

- **Tools > Altitude Zero > City Box Colliders > Analyze Selected**: select a PLATEAU city root, then inspect the Console summary without changing the scene.
- **Apply Selected**: generate BoxColliders for the selected root. Running it again replaces the previously generated colliders.
- **Clear Selected**: remove the generated colliders from the selected root.
- **Apply to SampleScene**: apply the tool to the city prefab in the open SampleScene. Save the scene afterward.

The tool examines each descendant named `bldg_*`. It samples horizontal mesh triangles in the city root's coordinate system, first on a 1 m grid and then on a 0.5 m grid if needed. Cells with a nearly flat roof are merged into rectangles; each rectangle becomes a solid BoxCollider from the building's lowest vertex to that roof. It requires at least 45% of candidate roof cells to fit and limits each building to 32 boxes. Buildings that fail these checks receive no collider. `dem_*` terrain is excluded.

The current SampleScene uses the generated collider-only prefab at `Assets/Generated/CityBoxColliders.prefab`. The measured result was **255 of 407 buildings**, using **3,369 BoxColliders** and no MeshCollider. The other 152 buildings were skipped because the flat-box fit failed or needed too many boxes. The earlier temporary ground collider remains a separate floor; it does not approximate DEM terrain.

If the city is moved or replaced, delete or regenerate the collider-only prefab instance and run **Apply Selected** on the city root so the collision geometry follows the new mesh layout. Inspect tight passages and roofs in the Scene view before relying on them for gameplay.
