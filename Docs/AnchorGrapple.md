# Dual anchor grapple

SampleScene uses the FPS `PlayerCapsule` prefab with `AnchorGrapple`. Both hands have an independent anchor, cable, wall marker, and missed-shot animation. Aim with the center reticle at a non-trigger Collider within 150 m.

| Action | Mouse | Keyboard | Gamepad |
| --- | --- | --- | --- |
| Fire/detach left anchor | Left button | Q | Left shoulder |
| Fire/detach right anchor | Right button | E | Right shoulder |
| Reel toward the most recently fired attached anchor | Hold middle button | Hold R | Hold right trigger |

A missed shot extends and retracts its own cable. The left cable has a cyan core; the right has a pale core. The two launchers are visible on the left and right of the FPS view.

While reeling, the CharacterController moves straight toward the anchor at up to `flightSpeed`, with gravity and air steering suspended. The target position accounts for the player's capsule size so the player stops against the hit surface. Once reached, the player remains attached to that surface until its anchor is detached, even after releasing the reel button. The hold position follows a moving anchored object. With two anchors, the most recently fired anchor is the reel target; the other rope stays attached and is allowed to slacken while reeling. Releasing the reel control before arrival returns to gravity and tethered movement.

Detaching the reel target during flight cancels reeling immediately. The actual travel velocity is passed to the FPS controller when the last anchor detaches, and gravity resumes. If one anchor remains, gravity resumes under that tether. Press reel again after releasing it to choose the remaining anchor.

The input actions are in `Assets/Starter Assets/Runtime/InputSystem/StarterAssets.inputactions`; the movement and visuals are in `Assets/Scripts/AnchorGrapple.cs`. The FPS controller handles the release velocity and gravity after both cables are detached.
