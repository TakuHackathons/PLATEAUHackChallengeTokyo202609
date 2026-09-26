# Anchor grapple

SampleScene uses the FPS `PlayerCapsule` prefab with `AnchorGrapple`.

- Aim at a wall or another non-trigger Collider within 150 m. **Right mouse button** (gamepad **right shoulder**) fires an anchor. Press it again to detach.
- The centered reticle says **ANCHOR READY** when a Collider is under the crosshair. **NO ANCHOR** means the next shot will miss. A missed shot still extends and retracts a short wire.
- **Hold left mouse button** (gamepad **left shoulder**) while anchored to accelerate toward the anchor. The first pull gives an upward launch. Release to stop pulling; the cable remains attached, and the player can swing.
- The cable is visible from the camera side to a point slightly inside the hit surface. Its marker is embedded in that surface. When the player is closer to the anchor than the cable length, the cable visibly sags.
- A simple gauntlet and wire muzzle are visible at the lower right of the first person camera. The cable has a dark outer sheath and a bright inner strand.
- Detaching during flight transfers the current velocity back to the FPS movement controller. The player continues through the air and falls under gravity. WASD or the left stick can steer while anchored.

The input actions are in `Assets/Starter Assets/Runtime/InputSystem/StarterAssets.inputactions`. The grapple logic is in `Assets/Scripts/AnchorGrapple.cs`, and the FPS controller contains the release velocity handoff. Change range, pull acceleration, reel speed, steering, cable width, or sag in the `Anchor Grapple` component on the `PlayerCapsule` prefab.

To check in Play mode, aim at a building face, attach, hold Pull, then release Pull and move sideways. Detach while airborne to confirm that horizontal speed continues and gravity brings the player down.
