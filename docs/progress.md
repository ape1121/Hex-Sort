# Progress

## Completed

- Modular refactor away from the original single bootstrap file.
- Centralized input manager for mouse and touch.
- Dedicated camera controller with pan and zoom.
- Drag-to-tilt holding and continuous pour preview/application.
- Reusable liquid view with blend bands, caustics, and Brownian micro-motion.
- Project instructions and docs baseline.

## Current Demo Behavior

- Hold a non-empty glass with primary drag.
- Tilt direction follows drag direction with smoothing.
- When the lip aligns with a valid target and tilt is high enough, the game previews a continuous pour and commits one discrete unit at a time.
- Mouse:
  - left drag: hold/tilt glass
  - middle/right drag: pan camera
  - wheel: zoom
- Touch:
  - one finger: hold/tilt glass
  - two fingers: pan/zoom camera

## Known Gaps

- Visual quality is still prototype-level because the demo uses runtime-created materials and primitive meshes.
- Pour heuristics are tuned, not yet physically modeled.
- No authored level data, audio, or undo yet.
