# Project Instructions

## Core Engineering Rules

- Keep responsibilities narrow. One class should own one kind of decision.
- Follow one-way dependencies:
  - `Core` has no scene/view dependencies.
  - `Input` normalizes device input but does not own gameplay rules.
  - `Gameplay` coordinates rules and scene actors but should not own rendering details.
  - `View` renders data and motion feedback but should not decide game rules.
  - `Bootstrap` wires systems together and should stay thin.
- Prefer composition over inheritance for gameplay systems.
- Avoid hidden coupling between scene objects. Pass dependencies explicitly during setup.
- Avoid code repetition. Shared runtime utility should live in a dedicated helper instead of being copied.

## File Organization

- Keep puzzle/domain logic in `Assets/HexSort/Scripts/Core`.
- Keep board orchestration and scene gameplay controllers in `Assets/HexSort/Scripts/Gameplay`.
- Keep input normalization in `Assets/HexSort/Scripts/Input`.
- Keep camera movement in `Assets/HexSort/Scripts/Camera`.
- Keep rendering helpers and visual components in `Assets/HexSort/Scripts/View`.
- Keep docs in `docs/` as Markdown files.

## Interaction Rules

- Use one centralized input manager for mouse/touch translation.
- Camera controls must not be mixed into gameplay controllers.
- Glass dragging and pouring should feel continuous and smoothed, never snapped unless intentionally designed.
- Puzzle state remains deterministic even if the visual presentation is continuous.

## Liquid System Rules

- Keep liquid visuals reusable outside the puzzle board.
- Liquid view components should consume state and dynamics samples, not inspect unrelated systems directly.
- Visual blending, wobble, caustics, and Brownian motion should be additive presentation layers, not rule logic.
- Never infer game correctness from visual state.

## Documentation Rules

- Documentation is part of the deliverable.
- Update `TODO_AGENT.md` as a checkbox roadmap after meaningful progress.
- Add or update Markdown docs in `docs/` when architecture, controls, or rendering approach changes.
- Keep docs practical: responsibilities, data flow, current limitations, and next steps.

## Quality Rules

- Favor readability over cleverness.
- Use comments only where intent is non-obvious.
- Keep runtime-created demo code structured so it can be migrated to authored prefabs and assets later.
- Compile-check after meaningful refactors.
