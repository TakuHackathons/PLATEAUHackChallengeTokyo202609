# Anchor grapple

The FPS `PlayerCapsule` in SampleScene uses `AnchorGrapple` with the existing Starter Assets `CharacterController` and `PlayerInput`.

- Aim at a collider and press **right mouse button** or **gamepad right shoulder** to fire an anchor. The nearest non-trigger collider under the crosshair is selected, up to 150 m away.
- The rope and anchor marker stay visible while the character flies toward the hit point. Press the same button again to cancel. The flight also ends on arrival, obstruction, or timeout.
- Camera look stays active during flight; walking, jumping, and Starter Assets gravity resume afterward.

Select the `PlayerCapsule` prefab or its instance and find **Anchor Grapple** in the Inspector. **Flight Speed** controls the top travel speed (default 32 m/s); **Acceleration** controls how quickly that speed is reached (default 80 m/s²). `Max Range`, `Anchor Layers`, `Surface Clearance`, `Arrival Distance`, and `Max Flight Time` are also editable.

The action and bindings live in `Assets/Starter Assets/Runtime/InputSystem/StarterAssets.inputactions`. The movement code is `Assets/Scripts/AnchorGrapple.cs`. Starter Assets' `FirstPersonController` has a small external-movement hook so its usual movement and gravity do not fight the grapple while camera look continues.
