using UnityEngine;

namespace Ape.Data
{
    /// <summary>
    /// Tunable camera framing & input-feel parameters shared by the Hex Sort camera controller.
    /// Lives in <see cref="GameConfig"/> so designers can iterate on framing without touching
    /// the scene's camera GameObject.
    /// </summary>
    [CreateAssetMenu(fileName = "CameraConfig", menuName = "HexSort/Configs/CameraConfig")]
    public class CameraConfig : ScriptableObject
    {
        [Header("Framing")]
        [Tooltip("Pitch (down-tilt) of the camera, in degrees.")]
        [Range(0f, 89f)]
        public float Pitch = 24f;

        [Tooltip("Extra empty space around the board when auto-fitting zoom to the screen. 0.15 = 15% margin on each side.")]
        [Range(0f, 1f)]
        public float FramePadding = 0.15f;

        [Tooltip("How tightly the user can zoom in, as a multiple of the auto-fitted distance.")]
        [Range(0.1f, 1f)]
        public float ZoomInFactor = 0.6f;

        [Tooltip("How far the user can zoom out, as a multiple of the auto-fitted distance.")]
        [Range(1f, 3f)]
        public float ZoomOutFactor = 1.6f;

        [Header("Pan")]
        [Min(0f)] public float PanSensitivity = 0.016f;
        [Min(0f)] public float PanSharpness = 12f;

        [Header("Zoom")]
        [Min(0f)] public float ZoomSensitivity = 1.4f;
        [Min(0f)] public float ZoomSharpness = 10f;

        [Header("Intro / Transitions")]
        [Tooltip("How far back the camera starts before tweening in on first level load. 1 = no intro, 1.5 = starts 50% further back than the fitted distance.")]
        [Range(1f, 3f)]
        public float IntroZoomMultiplier = 1.45f;
        [Tooltip("Duration of the intro pull-in on first level load.")]
        [Min(0f)]
        public float IntroDuration = 0.9f;
        [Tooltip("Duration of camera tween when reframing for restart / next level.")]
        [Min(0f)]
        public float TransitionDuration = 0.5f;

        [Header("Orbit")]
        [Tooltip("Degrees of yaw per pixel of horizontal drag on empty space.")]
        [Min(0f)] public float YawSensitivity = 0.35f;
        [Tooltip("How quickly the camera yaw eases to its target. Higher = snappier.")]
        [Min(0f)] public float YawSharpness = 14f;
        [Tooltip("Limit the orbit yaw range, in degrees. 0 = unlimited.")]
        [Min(0f)] public float YawClamp = 0f;
        [Tooltip("Pixels the primary must drag from press origin before the camera claims it for orbit. Below this, the press falls through to glass pickup.")]
        [Min(0f)] public float OrbitDragThreshold = 8f;
    }
}
