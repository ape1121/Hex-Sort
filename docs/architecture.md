# Architecture

## Module Split

- `Core`
  - `LiquidColorId`, `GlassState`, `PourMove`
  - `HexSortManager` — scene composer; resolves level + configs, instantiates glasses, wires controllers, listens for completion.
  - `HexSortSceneBootstrapper` — `App.Game` lifecycle adapter for the scene.
  - `HexSortLevelData` — per-level fill ScriptableObject.
  - `HexSortLevelGenerator` — static Hanoi-style reverse-move scrambler, guaranteed solvable.

- `Data` (`Ape.Data`, lives under `Assets/Common`)
  - `GameConfig` — top-level container (`Levels`, `Board`, `Camera`, `Colors`).
  - `LevelsConfig` — ordered `HexSortLevelData[]` with `GetLevel(index)` / `GetLevelLooped(index)`.
  - `BoardConfig` — glass spawn layout (capacity default, spacing, Y, board extents, board pivot).
  - `CameraConfig` — pitch, frame padding, zoom factors, pan/zoom/orbit feel.
  - `ColorsConfig` — `LiquidColorId → Color` palette.

- `Input`
  - `HexSortInputManager` — primary press/drag, multi-touch pinch + pan, claim-based gesture arbitration via `TryClaimUnhandledDrag`.

- `Camera`
  - `HexSortCameraController` — yaw orbit, pinch/wheel zoom, two-finger pan; auto-fits camera distance to board extents using FOV + aspect + pitch foreshortening; refits on screen-size change.

- `Gameplay`
  - `HexSortBoardController` — pickup, target evaluation with hysteresis, continuous pour preview, partial-commit on disengage, fires `MoveApplied` on each commit. Robust pickup (`RaycastAll` + world-radius + screen-radius fallbacks); held glass moves to IgnoreRaycast layer during hold.
  - `HexSortGlassController` — per-glass facade; owns `GlassState`, exposes `DisplayedFillUnits` for smooth tilt, forwards collision peers to the animator.
  - `GlassPourAnimator` — DOTween state machine (`Idle / FreeDragging / Engaging / Pouring / Disengaging / Returning`). Fill-aware dynamic tilt; configurable lip clearance, pour height offset, pour center bias; `Physics.ComputePenetration` peer collision resolution.

- `View`
  - `HexSortMaterialLibrary` — palette seeded from `ColorsConfig`; templates for highlights, streams, droplets, liquid material instances.
  - `GlassLiquidView` — body cylinder + horizontal surface disc per glass; spring-damper slosh; shader uniform updates.
  - `LiquidMeshFactory`, `LiquidDynamicsSample`, `RuntimeViewUtility`, `PourStreamView`.
  - `Shaders/HexSortLiquid.shader` — implicit-cylinder clipped surface, world-Y layer boundaries, foam, caustics.

## Dependency Direction

- `Core` and `Data` are leaves.
- `Input` depends on nothing game-specific.
- `Camera`, `View` depend on `Core` + `Data`.
- `Gameplay` depends on `Core` + `View`.
- `HexSortManager` (Core) wires everything together but is itself referenced only from the scene.

## Why This Split

- Puzzle rules stay isolated from rendering and input feel.
- Tuning lives in `GameConfig` sections so designers can iterate without code changes.
- View, input, and camera modules can be reused or swapped without touching gameplay logic.

## Current Limitations

- Liquid materials are still constructed at runtime from a single authored shader; URP/material assets are not yet authored.
- Pour trigger is proximity + dynamic-tilt based, not volume- or lip-physics based.
- Fill level is clamped at the rim's lowest world-Y rather than conserving volume.
- No undo / restart UI yet — completion event fires but isn't wired to a flow.

## Next Architectural Step

- Move per-glass tuning (rim radius, capacity-derived heights, etc.) into prefabs.
- Promote runtime materials to authored URP assets / shader graphs.
