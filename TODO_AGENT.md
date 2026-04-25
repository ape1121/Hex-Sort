# Hex Sort TODO / Roadmap

## Current Focus

- [x] Replace the single-file prototype with modular `Core`, `Gameplay`, `Input`, `Camera`, and `View` systems.
- [x] Add a centralized input manager for mouse and touch.
- [x] Switch from click-to-click pouring to drag-to-tilt interaction.
- [x] Add camera pan/zoom controls for mouse and touch.
- [x] Refactor liquid rendering into a reusable `GlassLiquidView`.
- [x] Add soft layer transition bands, animated caustic overlays, and Brownian micro-motion.
- [x] Add project-wide conventions in `INSTRUCTIONS.md`.
- [x] Start architecture and progress docs in `docs/`.

## Done In This Pass

- [x] Pure puzzle state moved into `GlassState` and `PourMove`.
- [x] `HexSortInputManager` now normalizes primary drag, touch pinch zoom, mouse wheel zoom, and camera pan.
- [x] `HexSortCameraController` now owns board camera movement and smoothing.
- [x] `HexSortBoardController` now owns drag pickup, target evaluation, continuous pour preview, and move application.
- [x] `HexSortGlassController` now owns hold pose, tilt smoothing, pour intent calculations, and glass-local visual updates.
- [x] `GlassLiquidView` now renders liquid layers independently of puzzle orchestration.
- [x] `PourStreamView` now owns stream visuals independently of board logic.
- [x] Demo bootstrap now only wires the scene together and creates the test environment.

## Liquid Feel Pass

- [x] Replace stretched-cylinder pour with a procedural arced tube mesh that follows a parabolic trajectory.
- [x] Add scrolling-UV flow texture (procedural streak texture) so the stream visibly moves along its length.
- [x] Add per-segment width and lateral wobble that scales with flow intensity for a "wet" look.
- [x] Add an impact splash particle system that emits droplets at the receiver lip in the stream color.

## Shader-Based Liquid Pass

- [x] Author `HexSort/Liquid` URP shader with world-space horizontal clipping, multi-layer color lookup, foam line near the surface, and surface wobble with directional lean.
- [x] Replace the cylinder-stack + cap-sphere visual with a single tessellated cylinder per glass that uses the liquid shader.
- [x] Rebuild `GlassLiquidView` to drive shader uniforms (layer colors and world-Y boundaries, fill level, wobble, lean) every frame from `LiquidDynamicsSample`.
- [x] Cap fill level at the rim's lowest world-Y point so heavy tilts cannot push water above the visible glass.
- [x] Project top-disc vertices to a horizontal plane centered on the glass position, so the surface stays flat in world space when the glass rotates.
- [x] Move stream/droplet/highlight material creation into the library; remove obsolete blend-band and caustic templates.

## Control & Pour Detection Fix Pass

- [x] Replace Euler-based glass tilt with `AngleAxis` rotation around an axis perpendicular to drag direction.
- [x] Rotate the glass around its mid-body (~1.18m above the pivot) by compensating glass position with the inverse rotation, so the bottom of the glass swings naturally instead of acting as the pivot.
- [x] Move the receive point onto the source-facing side of the receiver rim so the stream lands inside the rim instead of being pushed away from the source.

## Free-Drag Auto-Pour Pass

- [x] Replace the drag-relative-to-rest-position scheme with free cursor positioning. The held glass mid-body simply tracks the cursor world XZ at a fixed hold height (no `MaxDragDistance` clamp).
- [x] Replace tilt-driven pour with proximity-driven pour. `HexSortBoardController.ResolveEngagementTarget` finds the closest pourable glass to the cursor with hysteresis (enter at 1.05m, exit at 1.55m).
- [x] Closed bottom disc added to the liquid mesh so looking down through a tilted glass shows a filled bottom instead of an open hole.
- [x] Fill level clamp relaxed from "rim's lowest world-Y" (which dropped below the visible glass at heavy tilt) to "rim center world-Y - small margin", so liquid remains visible across the full tilt range.

## DOTween Pour Animation Pass

- [x] Add `GlassPourAnimator` MonoBehaviour that owns held-glass transform animation. State machine: `Idle -> FreeDragging -> Engaging -> Pouring -> Disengaging -> Returning`.
- [x] Use `DG.Tweening` (DOTween, present at `Assets/Plugins/Demigiant/DOTween`) for all state transitions. Free-drag uses an exponential damp to follow the cursor; transitions use sequenced `DOMove` and `DORotateQuaternion`.
- [x] `EngageTarget(target)` builds a `Sequence` that first appends a `DOMove` rise (height bump, no XZ change) and then appends a combined position+rotation tween into the pour pose. Pouring (`IsPouring`) is only set when the sequence completes.
- [x] `DisengageTarget(cursor)` and `ReturnToRest(restPosition)` tween position and rotation back simultaneously. Old tweens are killed before new ones start so re-engagements are smooth.
- [x] Pour pose computed from per-target `(target.position - toTargetDirection * pourMidLateralOffset + Vector3.up * pourMidHeight)` so the held glass lip ends up just over the target rim corner facing the source. Tilt direction = `Cross(world up, toTarget)` so the lip extends towards the receiver.
- [x] All tween durations, eases, rise extra height, pour tilt angle, mid-body lateral offset, and pour mid height are `[SerializeField]` fields on `GlassPourAnimator` (tunable by editing the source defaults today; ready to expose via prefab when authoring assets land).
- [x] `HexSortGlassController` is now a thin dispatcher: `BeginHold/DriveHold/EndHold` delegate to the animator, and the controller's `LateUpdate` only computes liquid-view dynamics (velocity, agitation) from the animator-driven transform.
- [x] `HexSortBoardController.UpdateHeldGlass` resolves the engagement target each frame (with hysteresis), drives the animator, and only ticks the pour timer (`perUnitPourSeconds`, default 0.85s) and shows the stream while `heldGlass.AnimatorIsPouring` is true. This guarantees the visual is "rise into pour pose first, *then* the liquid flows".

## Liquid Physics Feel Pass

Goal: liquid should *feel like water under gravity*. Surface and layers should react to glass motion (slosh), settle with damped oscillation, and read clearly with caustic shimmer. Cap and layers tilt with the rigid mesh (already in place from the closed-mesh refactor); this pass adds reactive motion **on top of** that.

### Industry-standard approach
- **Slosh** is modelled as a 2D damped spring (mass on a horizontal plane). External force = `-acceleration` of the container. Spring pulls the offset back to centre; damper bleeds energy. The offset → tilt of the surface plane.
- **Surface tilt** is applied at vertex stage: cap vertex Y is displaced by `dot(world_xz_offset_from_center, slosh_dir) * slosh_magnitude`. Far side of the slosh direction goes up, near side goes down. Same idea applies to layer boundaries inside the body.
- **Brownian** noise stays as cap micro-detail; its amplitude scales with `agitation`.
- **Caustics** are a procedural shimmer pattern (2-octave value noise) modulated by depth and time. Visible on side walls and surface; brighter when motion is high.
- **Impulses** on grab/release/snap inject energy into the slosh state (one-shot velocity kick), so the liquid visibly reacts to player actions.

### Plan

- [x] Audit existing dynamics path. `LiquidDynamicsSample` only carries `agitation` (scalar) and `containerUp` (rotation). Need horizontal velocity / acceleration for slosh.
- [x] Extend `LiquidDynamicsSample` with `LinearVelocity` (world XYZ) and `AngularSpeedDeg` (scalar) so the view can derive slosh forcing without re-sampling the transform.
- [x] `HexSortGlassController.LateUpdate` now forwards linear velocity and angular speed.
- [x] Slosh state added to `GlassLiquidView`: spring-damper integration in `LateUpdate`, `_SloshX`/`_SloshZ` plus `_GlassCenterX`/`_GlassCenterZ` shader uniforms, tunable via `sloshStiffness`/`sloshDamping`/`sloshSensitivity`/`sloshMaxAmplitude` SerializeFields.
- [x] Shader vertex stage applies `sloshLift = dot(worldXZ - glassCenter, sloshXZ)` to top cap (full magnitude + brownian wobble) and side wall (30%), bottom cap untouched.
- [x] Fragment stage looks up layer colour at `objectY - sloshLift`, so layer boundaries visibly slope with the slosh tilt.
- [x] Wobble amplitude is now `lerp(0.012, 0.045, agitation + sloshMag*0.6)` — visibly chops with motion.
- [x] Caustic shimmer strengthened: 3 noise octaves and a `_CausticStrength` uniform (default 0.35), 100% on cap and 40% on walls.
- [x] Impulse hooks: `AddSloshImpulse` on `GlassLiquidView`, called from `HexSortGlassController.BeginHold` (0.45 × cursor direction) and `EndHold` (small randomised settle). DOTween snaps from `GlassPourAnimator` excite slosh naturally via the velocity estimator.
- [ ] Bump cap radial segments from 24 → 32 if banding still visible after testing.
- [x] `docs/liquid-system.md` updated with slosh model, shader behaviour, uniforms, and tuning fields.

## Real-Liquid Implicit-Body Pass

Goal: render the liquid as the *actual physical intersection* of the glass interior cylinder with a horizontal world plane at the fill level. Cap is geometrically correct at any tilt; layers stay gravity-aligned.

- [x] Body cylinder built **once** at full glass interior height (`interiorBottomLocalY → rimLocalY`), no top cap. Parented to glass, tilts with the body. Fragments above `_FillLevel` (world Y) are discarded by the shader, so the visible body is automatically the "scoop" shape of liquid in a tilted cup.
- [x] Surface disc is a top-level GameObject, scaled to `2.5 × maxRadius` so it always covers the largest possible elliptical cross-section. Each frame: positioned at `(glass.x, fillLevel, glass.z)` with identity rotation (gravity-horizontal).
- [x] Shader **implicit-body cylinder test** clips the disc to the exact body interior cross-section. Surface fragments outside the cylinder are discarded — the visible disc is the *correct ellipse* at any tilt, taper, or scale.
- [x] Layer colours via **world-Y boundaries** scaled to fit `[bottom, fillLevel]`. Layers stay horizontal in world (real-liquid behaviour) and compress smoothly when the rim caps the fill.
- [x] Top-layer colour passed as `_TopLayerColor` uniform; surface uses it directly with caustic + foam treatment.
- [x] Slosh tilt and brownian wobble continue to drive the surface vertex Y so the disc oscillates within the implicit cylinder.

## Pour Pose & Surface Gravity Pass

Goal: pour pose must read correctly (no body intersection between glasses, lip near target rim, visible stream); the cap must read as a real **gravity-aligned** surface during pour, not a cap that tilts with the body.

- [x] `LiquidMeshFactory.BuildLiquidColumn` accepts `includeTopCap` (default `true`); column is now built **without** a top cap.
- [x] `LiquidMeshFactory.BuildLiquidSurface` (re-added) builds a unit-radius flat disc with `uv.y = 1` everywhere.
- [x] `GlassLiquidView` spawns a top-level `LiquidSurface_<id>` GameObject (no parent → immune to glass tilt).
- [x] Disc is positioned each `LateUpdate` at `glass.TransformPoint(0, fillLocalTop, 0)` — the central-axis intersection with the body's top edge plane — and rotation is forced to identity, so the surface is always horizontal in world (gravity-aligned).
- [x] Disc radius scaled by `liquidColumnTransform.lossyScale.x` to match parent scale, plus a 5% bias so it always covers the elliptical cross-section of a tilted cylinder.
- [x] Slosh tilt (already in shader) applies to disc verts identically to the old top-cap path because the disc UVs all read `uv.y = 1`.
- [x] `GlassPourAnimator.ComputePourPosition` is now a clean inverse-kinematic solve: `desiredLipWorld = target.position + up*target.RimLocalY - toTargetDir*(target.RimRadius + pourLipClearance)`, then `source.position = desiredLipWorld - pourRotation*(up*SourceRimLocalY + toTargetDir*SourceRimRadius)`. The source's tilted downhill rim point lands **exactly** on the target's source-facing rim edge (lip-touching). Body clearance was removed — it was over-conservative and caused a visible 0.18m offset between lip and target rim. Glass shells are transparent; the implicit-cylinder shader clips the liquid to its own body, so any small visual overlap of the shells is harmless.
- [x] `PourStreamView.UpdateVisual` cull-out threshold lowered from 0.05 → 0.005 so the stream reliably renders even at short pour distances.

## Next High-Value Tasks

- [ ] Replace primitive runtime geometry with authored prefabs or prefab builders that can swap art without touching gameplay logic.
- [ ] Move liquid material creation from runtime code to real URP assets and shader graphs.
- [ ] Add a shader-based meniscus, fresnel edge boost, and depth-based color attenuation.
- [ ] Improve drag feel with adaptive grip offset so the held glass follows the cursor/finger more naturally.
- [ ] Add a more physically believable pour trigger based on liquid volume and lip height, not only tilt threshold.
- [ ] Add receiver-side liquid disturbance (ripple on the target's surface mesh) when the stream lands.
- [ ] Add droplet break-up at very low flow intensity (Plateau-Rayleigh-style beads instead of a continuous tube).
- [ ] Separate board setup into authored level data assets instead of hardcoded layouts.
- [ ] Add undo/restart UI and solved-state progression flow.
- [ ] Add tests for move rules and edge cases such as full-target, empty-source, and multi-unit pours.

## Interaction / Feel Tasks

- [ ] Tune camera panning sensitivity separately for mouse and touch.
- [ ] Add inertial release on camera pan.
- [ ] Add subtle haptics hooks for touch devices.
- [ ] Add glass pickup / release squash and settle motion.
- [ ] Add audio hooks for grab, stream start, stream loop, pour end, invalid target, and solved glass.
- [ ] Add target snap visualization when the lip is close enough to pour.
- [ ] Add optional slow-motion or aim assist for touch pouring on small screens.

## Liquid Rendering Tasks

- [ ] Move blend bands to a shader-based vertical gradient instead of mesh-only overlap.
- [ ] Add soft foam/meniscus detail at the top surface.
- [ ] Add receiver-side ripples that depend on incoming flow intensity.
- [ ] Add more layered caustic patterns and per-color absorption tuning.
- [ ] Add optional droplets/splash polish that does not affect gameplay state.
- [ ] Support reusing `GlassLiquidView` in non-puzzle scenes with externally provided layer data.

## Documentation Tasks

- [x] `INSTRUCTIONS.md` created.
- [x] `docs/architecture.md` created.
- [x] `docs/liquid-system.md` created.
- [x] `docs/progress.md` created.
- [ ] Keep `TODO_AGENT.md` updated with checked/unchecked progress after every significant pass.
- [ ] Add level authoring documentation once data assets exist.
- [ ] Add shader/material documentation once URP assets replace runtime-created materials.

## Suggestions

- [ ] Keep puzzle rules deterministic and discrete even if the liquid visuals become more advanced.
- [ ] Prefer data-driven configuration objects for tuning once the current feel loop stabilizes.
- [ ] Treat `Input`, `Camera`, `Gameplay`, and `View` as separate modules with one-way dependencies only.
- [ ] When a system is expanded, document the responsibility split in `docs/` before piling more behavior into one class.
