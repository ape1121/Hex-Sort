# Liquid System

## Current Approach

The liquid is rendered as **the geometric intersection of the glass interior cylinder with the half-space below a horizontal world plane** — exactly how real water behaves under gravity. There are two meshes, both using a single `HexSort/Liquid` shader instance:

1. **Body mesh** — a side-walled cylinder + bottom cap, built once at the full interior height (`interiorBottomLocalY → rimLocalY`), parented to the glass. It tilts as a rigid body with the glass. The fragment shader **discards anything above `_FillLevel` (world Y)**, so the visible body is automatically the "scoop" shape: liquid in a tilted cup pools to the low side.
2. **Surface disc** — a unit-radius flat disc, top-level (no parent so it never inherits glass tilt), positioned each frame at `(glass.x, fillLevel, glass.z)` with identity rotation. It's scaled to ~2.5× the max body radius so it generously covers any cross-section. The fragment shader **clips it to the implicit body cylinder** so the visible surface is the exact ellipse where the horizontal fill plane intersects the (possibly tilted, possibly tapered) cylinder.

A 2D damped-spring **slosh** state drives `_SloshX`/`_SloshZ`; the surface vertex shader uses these to lift/lower the disc proportional to the world-XZ offset from the glass centre. Brownian noise adds organic micro-motion. Caustics are a procedural 3-octave value-noise field scrolled by time.

`GlassLiquidView` owns:

- One body mesh + one surface disc per glass, both sharing one `HexSort/Liquid` material instance.
- Per-frame uniform updates: layer colours, world-Y layer boundaries (scaled to fit `[bottom, fillLevel]`), `_TopLayerColor`, `_FillLevel`, glass centre + up vector, body radii / Y range (for the implicit-cylinder clip), foam / depth / wobble / caustic parameters, and slosh tilt.
- 2D slosh state (`sloshOffset`, `sloshVelocity`) integrated each `LateUpdate` with a spring-damper driven by the glass's horizontal acceleration. `AddSloshImpulse` lets gameplay hooks (grab, release, pour engage) inject one-shot kicks.

`PourStreamView` renders the falling stream as:

- A procedural tube mesh sampled along a parabolic trajectory derived from `StreamGravity` and an intensity-driven horizontal speed.
- A scrolling-UV streak texture so the stream visibly flows along its length, with scroll speed proportional to flow intensity.
- Per-frame radius and lateral wobble for a "wet" look.
- A `ParticleSystem` that emits short-lived droplets at the receiver impact point, tinted with the current liquid color, with size and rate driven by intensity.

## How the Liquid Shader Works

Vertex stage:

- All vertices start as `TransformObjectToWorld(positionOS)`.
- For surface fragments (`uv.y > 0.99`): apply `sloshLift = (worldXZ - glassCenter.xz) · (sloshX, sloshZ)` and brownian wobble to `worldPos.y`. The disc oscillates in world Y while staying horizontal-on-average.
- For body fragments: untouched.

Fragment stage:

**Surface fragment** (`uv.y > 0.99`):
1. **Implicit body-cylinder clip**: project the fragment onto the glass's central axis (`glass.position` + `t * glass.up`), measure perpendicular distance, and discard if it's outside the body radius at that axis position (lerp between `_BodyBottomRadius` and `_BodyTopRadius`). This makes the disc geometrically equal to the elliptical intersection of the horizontal fill plane with the cylinder.
2. Colour with `_TopLayerColor`, full-strength caustic shimmer, and a touch of foam.

**Body fragment** (`uv.y < 0.99`):
1. **World-Y discard**: `if (worldPos.y > _FillLevel) discard;`. The remaining geometry is exactly the liquid volume.
2. Bottom cap (`uv.y < 0.01`): rendered with `_Color0` darkened.
3. Side wall: layer colour by world Y against `_Boundary0`..`_Boundary5`, depth-tinted by distance below the surface, with a softer caustic shimmer.

All fragments end with a single Lambert + ambient + view-direction fresnel pass.

Layer boundaries are in **world Y**, scaled to fit `[glass.y + interiorBottomLocalY, fillLevel]` so layers compress smoothly when the rim caps the fill at high tilt while remaining gravity-aligned (horizontal) bands.

## Why This Approach

- The puzzle state is discrete and we want determinism.
- Full fluid simulation would fight readability and mobile performance.
- A layered visual system plus a flowing stream and a leveled top surface can still feel convincingly like real liquid.
- Keeping all dynamic geometry procedural means we can iterate on feel without authoring URP assets first.

## Slosh Dynamics

`GlassLiquidView` runs a 2D damped-spring simulation each frame:

```
accel  = (linearVelocity_t - linearVelocity_{t-1}) / dt        // estimated XZ acceleration
force  = -accel * sloshSensitivity
       - sloshOffset * sloshStiffness
       - sloshVelocity * sloshDamping
sloshVelocity += force * dt
sloshOffset   += sloshVelocity * dt
```

The result is a 2D vector in world XZ representing the equivalent surface tilt (units: rise per metre of horizontal offset from the glass centre). It's pushed to the shader as `_SloshX`, `_SloshZ`, with `_GlassCenterX`, `_GlassCenterZ` providing the radial origin. The shader uses these to compute a per-vertex `sloshLift` in the vertex stage (see above).

Tunables on `GlassLiquidView` (all `[SerializeField]`):

| Field | Default | Effect |
|---|---|---|
| `sloshStiffness` | 90 | Spring constant. Higher = stiffer / faster oscillation. |
| `sloshDamping` | 11 | Damping coefficient. Higher = settles faster. |
| `sloshSensitivity` | 0.45 | How strongly glass acceleration excites the slosh. |
| `sloshMaxAmplitude` | 0.18 | Hard cap on the surface tilt to avoid blow-up under extreme motion. |

Brownian wobble amplitude scales with `agitation + sloshOffset.magnitude * 0.6`, so the surface visibly chops harder while the slosh is excited.

Impulse hooks (`AddSloshImpulse(Vector3)`) let gameplay code inject one-shot velocity kicks:

- `HexSortGlassController.BeginHold`: kick of `0.45 * cursorDirection` (the liquid jumps toward the player's grab direction).
- `HexSortGlassController.EndHold`: small randomised settle kick.
- DOTween snaps in `GlassPourAnimator` (engage / disengage) naturally produce velocity spikes that the slosh state already tracks via the per-frame acceleration estimator — no explicit hook needed.

## Controls and Pour Animation

The interaction loop is now free-drag with proximity-based auto-pour, animated entirely through DOTween. There is no manual tilt-to-pour.

### Animator State Machine

`GlassPourAnimator` (one per glass) owns the held-glass transform during a hold and runs a five-state machine:

| State | Trigger | Behaviour |
|---|---|---|
| `Idle` | At rest, not held | No transform updates. |
| `FreeDragging` | Glass picked up, no target in range | `LateUpdate` exponentially damps `transform.position` and `transform.rotation` toward `(cursorXZ, freeHoldHeight - midGlassHeight)` and identity. |
| `Engaging` | Cursor entered a target's enter distance | DOTween `Sequence`: first `DOMove` rises by `riseExtraHeight` (Y only), then `DOMove`+`DORotateQuaternion` to the pour pose. On complete, transitions to `Pouring`. |
| `Pouring` | Engaging tween finished | Glass is locked at pour pose. The board controller drives unit transfers and the stream while this state is active. |
| `Disengaging` | Cursor left target's exit distance, or pour exhausted | DOTween `DOMove`+`DORotateQuaternion` back to a free-drag pose, then transitions to `FreeDragging`. |
| `Returning` | User released the mouse | DOTween to `restPosition` with `Ease.OutBack`, then transitions to `Idle`. |

### Pour Pose Geometry

For a held glass and chosen target, the pour pose is constructed from these tunables (all `[SerializeField]` on `GlassPourAnimator`):

- `pourTiltAngle` (default 55°): held glass rotates by `AngleAxis(angle, Cross(world up, toTargetDirection))` so the lip extends toward the target.
- `pourMidLateralOffset` (default 0.72m): mid-body anchor sits this far back from the target on the source-facing side.
- `pourMidHeight` (default 2.7m): mid-body anchor world Y above the target's foot.
- `pourLipClearance` (default 0.05m): small extra rise so the lip clears the target rim.
- `midGlassHeight` (default 1.18m): position is computed so the mid-body lands on the anchor: `glass.position = pourAnchor - pourRotation * (up * midGlassHeight)`.

### Board Controller Loop

`HexSortBoardController.UpdateHeldGlass` each frame:

1. Projects the cursor onto a horizontal plane at `y = 1.85m` and clamps to board extents.
2. `ResolveEngagementTarget(cursor)` returns the closest pourable glass, applying hysteresis: a new target only engages when the cursor is within `engageEnterDistance` (1.05m); the current target stays engaged until the cursor leaves `engageExitDistance` (1.55m). Targets that the held glass cannot pour into are filtered out.
3. `heldGlass.DriveHold(cursor, target, engagement)` dispatches to the animator: `EngageTarget` if a new target was found, `DisengageTarget` if the previous target is gone, `UpdateCursor` otherwise.
4. `TickPourFlow(target)` only ticks while `heldGlass.AnimatorIsPouring` (i.e., the engage sequence has completed). Pour progress advances at `1 / perUnitPourSeconds` per second; the stream is shown with intensity ramped from `activePourProgress`. Each completed unit calls `ApplyMoveTo`.

The "rise first, then pour" feel comes from the gating of step 4 on `AnimatorIsPouring`: the stream cannot appear until the DOTween `Engaging` sequence has finished its rise+traverse phases.

### Tunables

All animation knobs are `[SerializeField]` on `GlassPourAnimator`:

| Field | Default | Effect |
|---|---|---|
| `freeHoldHeight` | 2.2m | Mid-body Y while free-dragging. |
| `freeFollowDamping` | 14 | Higher = snappier cursor follow. |
| `riseDuration` | 0.18s | First step of the engage sequence (vertical lift). |
| `riseExtraHeight` | 0.35m | How much higher than the pour pose the glass rises before traversing. |
| `riseEase` | OutQuad | Ease on the rise step. |
| `positionDuration` | 0.42s | Second step (traverse + tilt). |
| `positionEase` | InOutQuad | Ease on the traverse step. |
| `pourTiltAngle` | 55° | Lip-to-target tilt at the pour pose. |
| `pourMidLateralOffset` | 0.72m | Source-side offset of the held mid-body from the target. |
| `pourMidHeight` | 2.7m | World Y of the held mid-body at the pour pose. |
| `pourLipClearance` | 0.05m | Extra clearance above the target rim. |
| `disengageDuration` | 0.32s | Tween back to free-drag. |
| `returnDuration` | 0.45s | Tween back to rest after release. |
| `perUnitPourSeconds` | 0.85s (on `HexSortBoardController`) | Time to transfer one unit. |
| `engageEnterDistance` | 1.05m (on `HexSortBoardController`) | Cursor distance to start a pour. |
| `engageExitDistance` | 1.55m (on `HexSortBoardController`) | Cursor distance to stop a pour. |

Today these are tweakable by editing the source defaults; once glasses are authored as prefabs, the same fields become inspector-tunable.

## Stream Trajectory

`PourStreamView.ComputeArcPoints` solves for the parabola explicitly:

- `horizontalSpeed = lerp(1.6, 3.4, intensity)`
- `timeOfFlight = horizontalDistance / horizontalSpeed` (or a free-fall time if pouring nearly straight down)
- `vY_init = ((endY - startY) + 0.5 * g * t²) / t`

The result is a believable arc that gets flatter as the player tilts harder (stronger horizontal velocity), and naturally falls more sharply for low-intensity pours.

## Dynamics Inputs

The liquid view is fed by a `LiquidDynamicsSample`:

- container up vector (world Y of the glass-local +Y axis)
- downhill direction (rim downhill projected onto the rim plane)
- linear velocity (world XZ + Y, used for slosh forcing)
- angular speed (degrees/sec, used to amplify agitation)
- flow readiness (0..1 pour engagement signal)
- agitation (0..1, amplifies wobble)
- held / pouring booleans

This keeps the liquid system reusable. It reacts to motion but does not need to know board rules.

## What Still Needs Improvement

- Replace mesh-only blend bands with shader-based vertical falloff.
- Add receiver-side ripples on the target's top surface when the stream lands.
- Add droplet break-up at very low flow rates (continuous tube → individual beads).
- Add foam / meniscus detail near the surface.
- Make caustics depend more strongly on fill height and camera angle.
- Move runtime materials and procedural textures to URP assets / shader graphs.
