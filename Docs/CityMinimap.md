# City map in SampleScene

`City Map Canvas` is a saved uGUI Canvas in SampleScene. Its `map` RawImage uses `Assets/Generated/CityMapPreview.png`, so the map is visible in the Scene and Game views before entering Play mode. The preview was rasterized from the current `CityBoxColliders` prefab around the player start position. Red is a blocked collider footprint; dark blue is open ground. The cyan triangle is the player, and the pink dot is the VRM raid boss.

During Play, `CityMinimap` on the Canvas replaces the preview with a texture rebuilt from enabled, non-trigger colliders at street height. It follows the player and updates the boss marker. Press **M** or click **SWITCH** to toggle between 120 m local and 400 m wide views. The PlayerCapsule no longer carries the old minimap script. The map creates no camera and leaves the FPS Main Camera output untouched.

For another city, place this Canvas in that scene and assign its `player` and `raidBoss` references. The Play-mode map automatically reads that scene's colliders. The saved preview PNG represents SampleScene only; regenerate it from the new scene's colliders if an accurate pre-Play preview is needed. Collider footprints show horizontal blockage at one street height, not vertical routes or entrances.
