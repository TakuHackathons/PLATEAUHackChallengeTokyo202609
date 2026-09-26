# City minimap

The FPS PlayerCapsule in SampleScene has a CityMinimap component. In Play mode it creates an orthographic top-down camera and shows its live city view in the upper-right UI. The map is north-up and follows the player.

A red overlay marks cells where a non-trigger Collider intersects a standing player's body near the street height sampled below the player's spawn point. The collision layer is resampled from live colliders, so changing the city model or its colliders changes the overlay without a baked map image. The cyan arrow shows the player and facing direction. A pink dot shows the raid boss; when it is outside the map, the dot stays at the border and the distance remains visible.

SampleScene assigns its VRM character directly to the CityMinimap component's Raid Boss field. With no assignment in another scene, the component looks for a non-player humanoid Animator or SkinnedMeshRenderer. Map Span, Collision Resolution, refresh interval, and sample height are editable on the PlayerCapsule prefab.

A city map needs working Colliders for the blocked-space overlay. The camera still shows geometry without them, but the map cannot infer physical walls from visuals alone. The overlay represents passability at the street height around spawn. Collider bounds are used for BoxColliders; other collider types are sampled near their surfaces. The map does not calculate a route, vertical stairs, door states, or overpasses.
