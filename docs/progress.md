# Progress

## Current State

The implementation pass through data-driven configuration, level data + generator, level-completion detection, aspect-aware camera framing, and pour-tuning offsets is complete. The game runs end-to-end with authored configs and procedurally generated levels.

## Major Milestones

- Modular split: `Core`, `Data`, `Input`, `Camera`, `Gameplay`, `View`.
- Free-drag pickup with proximity-based auto-pour (DOTween state machine).
- Custom URP liquid shader: implicit-cylinder surface clip, world-Y layer boundaries, slosh + foam + caustics.
- Spring-damper slosh state in `GlassLiquidView`, excited by transform acceleration and gameplay impulses.
- Robust pickup: `RaycastAll` + nearest-glass filter, world-radius and screen-radius fallbacks. Held glass moves to IgnoreRaycast so it never blocks subsequent picks.
- `GameConfig` / `LevelsConfig` / `BoardConfig` / `CameraConfig` / `ColorsConfig` ScriptableObjects under `Assets/Common/Scripts/Data` accessed via `App.Game.Config`.
- `HexSortLevelData` + `HexSortLevelGenerator` (Hanoi-style reverse-move scrambler — every generated level is provably solvable).
- Aspect-aware camera framing: distance solved from FOV + screen aspect + pitch foreshortening + padding; zoom range derived as multipliers of the fitted distance; refits live on screen-size change.
- Pour tuning knobs: `pourHeightOffset`, `pourCenterBias`, `pourLipClearance`, dynamic tilt range (`pourTiltAngleFull` → `pourTiltAngleEmpty`).
- Level completion event: `HexSortBoardController.MoveApplied` → `HexSortManager` checks all glasses are empty / `IsSolvedComplete` and logs once.

## Controls

- Mouse: left drag = hold/pour, middle/right drag = pan, wheel = zoom, drag empty space = orbit yaw.
- Touch: one finger = hold/pour or orbit (claimed past `orbitDragThreshold` pixels), two fingers = pan + pinch zoom.
- `R` resets the board to its starting layout.

## Authoring a Level

1. Create a `HexSortLevelData` asset (`Create → HexSort → Level Data`) — set capacity and per-glass `units`, **or** use the inspector's Generate button on the asset to populate it via `HexSortLevelGenerator.Parameters`.
2. Add the asset to a `LevelsConfig` (`Create → HexSort → Configs → LevelsConfig`).
3. Reference the `LevelsConfig` from your `GameConfig` (alongside `BoardConfig`, `CameraConfig`, `ColorsConfig`).
4. On the scene's `HexSortManager`, set `Level Index`, or assign an inline `Level Data Override` for one-off scenes.

## Known Gaps

- Materials are runtime-built from a single authored shader; URP material assets are not yet authored.
- No undo / restart UI; `MoveApplied` event is wired but not surfaced to the player.
- No audio hooks, no haptics.
- No automated tests for move rules or generator solvability.
