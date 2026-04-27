using UnityEngine;

namespace Ape.Data
{
    /// <summary>
    /// Tunable board layout & camera defaults shared across all Hex Sort levels.
    /// </summary>
    [CreateAssetMenu(fileName = "BoardConfig", menuName = "HexSort/Configs/BoardConfig")]
    public class BoardConfig : ScriptableObject
    {
        [Header("Glass Layout")]
        [Tooltip("Default per-glass capacity used when a level doesn't override it.")]
        [Min(1)]
        public int DefaultGlassCapacity = 4;

        [Tooltip("Horizontal spacing between auto-instantiated glasses, in metres.")]
        [Min(0.1f)]
        public float GlassSpacing = 1.9f;

        [Tooltip("World-Y position of glass bottoms when auto-instantiated.")]
        public float GlassY = 0.54f;

        [Tooltip("Approximate horizontal half-size of one glass (mesh radius + a small margin), used as padding when computing actual board extents from glass positions.")]
        [Min(0.05f)]
        public float GlassFootprintRadius = 0.6f;

        [Header("Board Bounds")]
        [Tooltip("Half-extents of the playable board (X = horizontal half-width, Y = depth half-extent).")]
        public Vector2 BoardExtents = new Vector2(5.5f, 1.85f);

        [Tooltip("Centre point of the board in world space — what the camera looks at by default.")]
        public Vector3 BoardPivot = new Vector3(0f, 0.95f, 0f);
    }
}
