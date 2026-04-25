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
- [x] Replace tilt-driven pour with proximity-driven pour. `HexSortBoardController.FindNearestPourTarget` finds the closest pourable glass to the cursor (XZ); engagement is `1 - clamp((distance - min) / (max - min))`.
- [x] `HexSortGlassController.UpdateHoldPose` now takes `(cursorWorld, target, engagement)` and rotates the glass towards the target by `engagement * MaxTiltAngle` (capped at 45°). The tilt is purely visual; pour activation is independent.
- [x] Lift scales with engagement so the held glass rises when leaning towards a target, keeping the rim above the receiver rim.
- [x] Wire engagement from `BoardController` -> `GlassController` -> `LiquidView` via `LiquidDynamicsSample.FlowReadiness`, replacing the old tilt-angle-based readiness so the surface lean matches whether the glass is currently pouring.
- [x] Pour starts when engagement crosses `PourEngagementThreshold` (0.4) and continues unit-by-unit while the cursor stays close to the target. Pour rate scales with engagement.
- [x] Closed bottom disc added to the liquid mesh so looking down through a tilted glass shows a filled bottom instead of an open hole.
- [x] Fill level clamp relaxed from "rim's lowest world-Y" (which dropped below the visible glass at heavy tilt) to "rim center world-Y - small margin", so liquid remains visible across the full tilt range.

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
