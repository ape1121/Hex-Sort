# Liquid System

## Current Approach

The liquid is rendered with a custom URP shader (`HexSort/Liquid`) that does world-space horizontal clipping in the fragment stage. Stacked-color rendering, top surface, and foam are all driven from a single mesh per glass.

`GlassLiquidView` owns:

- One tessellated, closed-top cylinder mesh per glass (radius matching the glass interior, height tall enough that the top cap stays above the maximum fill level at any tilt).
- A `HexSort/Liquid` material instance per glass with per-frame shader uniforms: per-layer colors and world-Y boundaries, current fill level, foam parameters, wobble parameters, and a directional surface lean.
- Per-frame upload of those uniforms in `LateUpdate`, including a fill-level clamp at the rim's lowest world-Y so heavy tilts cannot push water above the visible glass.

`PourStreamView` renders the falling stream as:

- A procedural tube mesh sampled along a parabolic trajectory derived from `StreamGravity` and an intensity-driven horizontal speed.
- A scrolling-UV streak texture so the stream visibly flows along its length, with scroll speed proportional to flow intensity.
- Per-frame radius and lateral wobble for a "wet" look.
- A `ParticleSystem` that emits short-lived droplets at the receiver impact point, tinted with the current liquid color, with size and rate driven by intensity.

## How the Liquid Shader Works

Vertex stage:

- Side-wall vertices are transformed normally into world space.
- Top-disc vertices (marked by `uv.y > 0.99`) are reprojected: their world XZ is recomputed as `glass_center_world_XZ + (cos(angle), sin(angle)) * disc_radius`, and their world Y is set to the wobbled fill level. This means the top disc is always a flat horizontal disc centered over the glass, regardless of tilt.

Fragment stage:

- For side-wall fragments, anything above the wobbled fill level is `discard`ed. The visible top edge of the side wall traces an ellipse in world space wherever the horizontal fill plane cuts the tilted cylinder.
- For top-disc fragments, no discard happens — they form the visible water surface.
- Color is looked up by world-Y against the per-layer boundary array (`_Boundary0`..`_Boundary5` and `_Color0`..`_Color5`). Layers therefore appear as horizontal bands in world space, not in glass-local space, so they remain visually horizontal even when the glass is tilted.
- A foam mask is applied near the surface based on `|worldY - fillLevel|` so the surface gets a brighter rim.
- Depth-based tinting darkens the color slightly at greater depths to suggest absorption.
- A view-direction fresnel adds a subtle highlight on the side walls.

The fill level uniform itself is computed in `GlassLiquidView.ComputeFillLevelWorldY`:

1. Start from `glass_position.y + bottomMargin + totalUnits * unitHeight` (upright fill height).
2. Compute the lowest point on the tilted rim: `glass_position.y + glass.up.y * RimHeight - InteriorRadius * |horizontal_component_of_glass.up|`.
3. Take the minimum of the two so water can never visually exceed the rim, even at extreme tilts.

## Why This Approach

- The puzzle state is discrete and we want determinism.
- Full fluid simulation would fight readability and mobile performance.
- A layered visual system plus a flowing stream and a leveled top surface can still feel convincingly like real liquid.
- Keeping all dynamic geometry procedural means we can iterate on feel without authoring URP assets first.

## Surface Levelling Logic

When the glass tilts, the cap (top surface) is positioned and rotated entirely in world space:

1. `tiltFactor = clamp01(angle(containerUp, world up) / 70°)`.
2. The cap world position lerps between the glass-local top and a straight-up-from-pivot position based on `tiltFactor * 0.65`.
3. A slosh offset in world space pushes the cap towards the downhill direction (scaled by fill level), with a small downward bias.
4. The cap world rotation is set to a small downhill lean (up to ~9°, scaled by `flowReadiness`) plus brownian wobble — never inheriting the glass tilt.
5. The liquid root is partially counter-rotated (~45% of the glass tilt) so the body cylinders look like water bunching in the lower side of the glass rather than a rigid stack rotating with the container.

## Controls and Pour Detection

The interaction loop is now free-drag with proximity-based auto-pour. There is no manual tilt-to-pour.

`HexSortBoardController.UpdateHeldGlass` each frame:

1. Projects the cursor onto a horizontal plane at `y = 1.85m` and clamps to the board extents.
2. Calls `FindNearestPourTarget(cursorWorld, out engagement)`, which scans every other glass that the held glass can legally pour into and picks the closest one in horizontal distance. Engagement is `InverseLerp(PourEngagementMaxDistance = 1.85m, PourEngagementMinDistance = 0.55m, distance)` — `0` when the cursor is far, `1` when the cursor is right next to a pourable glass.
3. Calls `heldGlass.UpdateHoldPose(cursorWorld, candidate, engagement)`.
4. If a candidate exists and `engagement >= PourEngagementThreshold (0.4)`, advances `activePourProgress` and applies one unit when it crosses 1. Pour rate scales with engagement.

`HexSortGlassController.UpdateHoldPose(cursorWorld, target, engagement)`:

1. Held glass mid-body world position = `(cursor.x, HoldHeight + engagement * MaxTiltLift, cursor.z)`. No drag-relative-to-rest constraint; the glass simply follows the cursor.
2. If a target is supplied and engagement is non-zero, the glass is rotated by `Quaternion.AngleAxis(smoothstep(engagement) * MaxTiltAngle, Cross(world_up, toTarget))` so it leans towards the target. The tilt is purely a visual cue — pour activation is purely about engagement (proximity).
3. The glass is rotated around its mid-body by computing `desiredPosition = holdCenter - rotation * (Vector3.up * MidGlassHeight)`. The base swings backwards while the top tips towards the target, matching real wrist rotation.

Engagement is also stored on `HexSortGlassController.currentEngagement` and threaded into `LiquidDynamicsSample.FlowReadiness`, so the liquid surface leans towards the lip in lockstep with the pour intensity.

## Stream Trajectory

`PourStreamView.ComputeArcPoints` solves for the parabola explicitly:

- `horizontalSpeed = lerp(1.6, 3.4, intensity)`
- `timeOfFlight = horizontalDistance / horizontalSpeed` (or a free-fall time if pouring nearly straight down)
- `vY_init = ((endY - startY) + 0.5 * g * t²) / t`

The result is a believable arc that gets flatter as the player tilts harder (stronger horizontal velocity), and naturally falls more sharply for low-intensity pours.

## Dynamics Inputs

The liquid view is fed by a `LiquidDynamicsSample`:

- container up vector
- downhill direction
- flow readiness
- agitation
- held state
- pouring state

This keeps the liquid system reusable. It reacts to motion but does not need to know board rules.

## What Still Needs Improvement

- Replace mesh-only blend bands with shader-based vertical falloff.
- Add receiver-side ripples on the target's top surface when the stream lands.
- Add droplet break-up at very low flow rates (continuous tube → individual beads).
- Add foam / meniscus detail near the surface.
- Make caustics depend more strongly on fill height and camera angle.
- Move runtime materials and procedural textures to URP assets / shader graphs.
