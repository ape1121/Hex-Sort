# Hex Sort

A water-sort puzzle game prototype built in Unity. Drag a glass, hold it near another, and the liquid pours into it one unit at a time. Sort each glass to a single colour to clear the level. Endless procedurally generated levels with star grading.

![Image](ss.png)

## Tech

- **Engine**: Unity **6000.3.8f1** (Unity 6.x)
- **Render pipeline**: URP **17.3** (custom liquid shader, `Shaders/HexSortLiquid.shader`)
- **Input**: Unity Input System **1.18**
- **UI**: TextMeshPro (uGUI)
- **Tweens**: DOTween (in `Assets/Plugins/Demigiant/DOTween`)
- **Target**: Mobile (touch + mouse), portrait or landscape — camera auto-fits to aspect

## Quick Start

1. Open `Assets/_Game/Scenes/Loader.unity` for the full app boot, or `Game.unity` for direct gameplay.
2. Press Play.
3. Mouse: left-drag a glass; middle/right-drag pans; wheel zooms; drag empty space orbits.
4. Touch: one finger drags / orbits; two fingers pan + pinch zoom.
5. `R` resets the board to its starting layout.

## Authoring Levels

1. `Create → HexSort → Level Data` to make a `HexSortLevelData` asset. Set `capacity` and per-glass `units`, **or** use the inspector's **Generate** button (Hanoi-style reverse-move scrambler — every output is provably solvable). The generator also writes `parMoves` for star grading.
2. Add the asset to a `LevelsConfig` (`Create → HexSort → Configs → LevelsConfig`).
3. Reference the `LevelsConfig` from your `GameConfig` (alongside `BoardConfig`, `CameraConfig`, `ColorsConfig`).
4. On the scene's `HexSortManager`, set `Level Index` for debugging or assign a `Level Data Override` for one-off scenes. At runtime, the persistent `SaveData.Level` is used (incremented by `LoadNextLevel`, cleared by main-menu Reset Progress).

Star grading: 3 stars at moves ≤ par, 2 at ≤ ⌈par × 1.5⌉, 1 otherwise. Best per-level stars are persisted in `SaveData.LevelStars`.

## Project Layout

```
Assets/
├── Common/                 # Reusable framework (App lifecycle, configs, sound, profile, scenes)
│   ├── Scripts/Core/       # App.cs, IManager, GameManager
│   ├── Scripts/Data/       # AppConfig, GameConfig + sections
│   ├── Scripts/Profile/    # ProfileManager + SaveData (PlayerPrefs JSON)
│   ├── Scripts/Sounds/     # SoundManager + AllSounds + Sound assets
│   └── Scriptables/        # AppConfig, AllSounds, sound assets
├── _Game/
│   ├── Scenes/             # Loader.unity, Game.unity
│   ├── Scripts/
│   │   ├── Core/           # HexSortManager, level data + generator, GlassState, PourMove
│   │   ├── Input/          # HexSortInputManager (claim-based primary + multi-touch arbitration)
│   │   ├── Camera/         # HexSortCameraController (aspect-aware fit, DOTween intro/transitions)
│   │   ├── Gameplay/       # HexSortBoardController, HexSortGlassController, GlassPourAnimator
│   │   ├── View/           # GlassLiquidView, PourStreamView, MaterialLibrary, mesh factory
│   │   ├── UI/             # MainMenuUI, GameUI, LevelCompletePopup
│   │   └── Editor/         # HexSortLevelDataEditor (Generate buttons + presets)
│   ├── Shaders/            # HexSortLiquid.shader
│   └── ScriptableObjects/  # GameConfig, LevelsConfig, BoardConfig, CameraConfig, ColorsConfig, levels
└── Plugins/Demigiant/DOTween
```

## Architectural Decisions

- **Modular split** — `Core` (deterministic puzzle rules) / `Data` (configs) / `Input` / `Camera` / `Gameplay` / `View` / `UI` with one-way dependencies. Puzzle correctness is isolated from rendering and input feel.
- **Data-driven configuration** — gameplay tunables live in `GameConfig` ScriptableObject sections (`BoardConfig`, `CameraConfig`, `ColorsConfig`, `LevelsConfig`) under `Assets/Common/Scripts/Data` so designers iterate without code changes. Accessed at runtime via `App.Game.Config`.
- **App framework** — `App.cs` is a singleton holding the four cross-scene managers (`Profile`, `Scenes`, `Sound`, `Game`). `DontDestroyOnLoad` carries it across scene loads. `HexSortSceneBootstrapper` re-binds the scene to `App.Game` on each load.
- **In-place level transitions** — restart and next-level never reload the scene. `HexSortBoardController` exposes `ApplyLayouts` (fast path: same glass count + capacity, just swap fills + snap to rest positions) and `RebindGlasses` (slow path: destroy + re-instantiate when the level's glass count or capacity changes). Camera glides via DOTween `Reframe`.
- **Camera framing** — the fitted distance is solved analytically from FOV + screen aspect + pitch foreshortening + padding. Min/max zoom are multipliers of the fitted distance. Board extents are derived from actual glass positions + footprint (not a static config), so 7-glass levels frame correctly without manual tuning. Refits live on screen-size change.
- **Liquid rendering** — the liquid is the geometric intersection of the glass interior cylinder with a horizontal world plane at the fill level (cylinder body + horizontal surface disc, both clipped against `_FillLevel` in the `HexSort/Liquid` URP shader). Slosh is a 2D damped-spring driven by transform acceleration.
- **Free-drag + proximity-based pour** — `HexSortGlassController.BeginHold` / `DriveHold` delegate to a DOTween-driven `GlassPourAnimator` state machine (`Idle → FreeDragging → Engaging → Pouring → Disengaging → Returning`). The board controller commits pour units at a fixed rate while the animator is in `Pouring` state and fires a `MoveApplied` event on each commit.
- **Robust glass picking** — `RaycastAll` + nearest-glass filter, plus world-space and screen-space radius fallbacks. The held glass is moved to the IgnoreRaycast layer during a hold so it can never block subsequent picks. `Physics.ComputePenetration` against peer colliders keeps the dragged glass outside its neighbours.
- **Procedural levels by reverse-move scramble** — generator starts from the solved state and applies N random reverse moves; the inverse of any reverse sequence is a legal forward solve, so every generated level is provably solvable. Scramble count is also written to `parMoves` for star grading.
