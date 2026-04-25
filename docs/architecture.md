# Architecture

## Module Split

- `Core`
  - `LiquidColorId`
  - `GlassState`
  - `PourMove`
  - Owns deterministic puzzle rules only.

- `Input`
  - `HexSortInputManager`
  - Translates mouse and touch into normalized primary drag, camera pan, and zoom signals.

- `Camera`
  - `HexSortCameraController`
  - Owns camera pivot, smoothing, pan, and zoom.

- `Gameplay`
  - `HexSortBoardController`
  - `HexSortGlassController`
  - `GlassPourAnimator`
  - Owns hold/release flow, drag-to-pour orchestration, target evaluation, move application, and DOTween-driven hold/pour/release animation.

- `View`
  - `HexSortMaterialLibrary`
  - `GlassVisualBuilder`
  - `GlassLiquidView`
  - `LiquidMeshFactory`
  - `LiquidDynamicsSample`
  - `PourStreamView`
  - `RuntimeViewUtility`
  - `Shaders/HexSortLiquid.shader`
  - Owns runtime visual construction and presentation effects.

- `Bootstrap`
  - `LiquidSortDemoBootstrap`
  - Creates the demo scene setup and wires dependencies.

## Dependency Direction

- `Bootstrap -> Input / Camera / Gameplay / View / Core`
- `Gameplay -> Core / View`
- `View -> Core`
- `Input -> none`
- `Core -> none`

## Why This Split

- The previous prototype coupled puzzle rules, camera logic, drag handling, liquid rendering, and scene construction in one file.
- That made interaction feel work expensive because every change touched unrelated behavior.
- The new structure keeps puzzle correctness isolated while allowing drag feel, liquid visuals, and camera tuning to evolve independently.

## Current Limitations

- Materials are created at runtime, so the visual system is structured but not yet art-pipeline ready (the liquid shader is an authored `.shader` file, but its instanced materials are constructed in code).
- The drag-to-pour logic is much better than click-to-click, but still uses tuned heuristics rather than a more physical lip/volume model. Tilt is mapped to a smoothstep curve and lift is empirically tuned.
- Fill level does not perform exact volume conservation when the glass tilts; instead it is clamped at the rim's lowest world-Y to prevent overflow artefacts.

## Next Architectural Step

- Move visual tuning into authored assets and data objects once the interaction loop is stable.
