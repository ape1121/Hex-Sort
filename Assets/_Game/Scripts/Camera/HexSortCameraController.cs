using Ape.Data;
using DG.Tweening;
using UnityEngine;

public sealed class HexSortCameraController : MonoBehaviour
{
    [SerializeField] private float panSensitivity = 0.016f;
    [SerializeField] private float zoomSensitivity = 1.4f;
    [SerializeField] private float zoomSharpness = 10f;
    [SerializeField] private float panSharpness = 12f;
    [SerializeField] private float pitch = 24f;

    [Header("Aspect-Aware Framing")]
    [Tooltip("Extra empty space around the board when auto-fitting zoom to the screen. 0.15 = 15% margin on each side.")]
    [Range(0f, 1f)]
    [SerializeField] private float framePadding = 0.15f;
    [Tooltip("How tightly the user can zoom in, as a multiple of the fitted distance.")]
    [Range(0.1f, 1f)]
    [SerializeField] private float zoomInFactor = 0.6f;
    [Tooltip("How far the user can zoom out, as a multiple of the fitted distance.")]
    [Range(1f, 3f)]
    [SerializeField] private float zoomOutFactor = 1.6f;

    [Header("Intro / Transitions")]
    [Tooltip("How far back the camera starts before tweening in on first level load. 1 = no intro.")]
    [Range(1f, 3f)]
    [SerializeField] private float introZoomMultiplier = 1.45f;
    [SerializeField] private float introDuration = 0.9f;
    [SerializeField] private Ease introEase = Ease.OutCubic;
    [Tooltip("Duration of the camera tween for level reset / next level transitions.")]
    [SerializeField] private float transitionDuration = 0.5f;
    [SerializeField] private Ease transitionEase = Ease.InOutQuad;

    [Header("Orbit")]
    [Tooltip("Degrees of yaw per pixel of horizontal drag on empty space.")]
    [SerializeField] private float yawSensitivity = 0.35f;
    [Tooltip("How quickly the camera yaw eases to its target. Higher = snappier.")]
    [SerializeField] private float yawSharpness = 14f;
    [Tooltip("Limit the orbit yaw range. 0 = unlimited.")]
    [SerializeField] private float yawClamp = 0f;
    [Tooltip("Pixels the primary must drag from press origin before the camera claims it for orbit. Below this, the press falls through to other systems (e.g. glass pickup) so single taps and short clicks always reach the gameplay layer.")]
    [SerializeField] private float orbitDragThreshold = 8f;

    private HexSortInputManager inputManager;
    private Camera controlledCamera;
    private Vector3 desiredPivot;
    private Vector3 currentPivot;
    private float desiredZoom;
    private float currentZoom;
    private float minZoom;
    private float maxZoom;
    private float desiredYaw;
    private float currentYaw;
    private bool isOrbiting;
    private Vector3 lookOffset = new Vector3(0f, 0.95f, 0f);
    private Vector2 boardHalfExtents = new Vector2(5.5f, 1.85f);
    private int lastFramedScreenWidth;
    private int lastFramedScreenHeight;
    private Tween transitionTween;
    private bool isTransitioning;

    public void Initialize(HexSortInputManager input, Camera targetCamera, Vector3 boardPivot, Vector2 boardExtents, CameraConfig config = null)
    {
        inputManager = input;
        controlledCamera = targetCamera;
        desiredPivot = boardPivot;
        currentPivot = boardPivot;
        boardHalfExtents = boardExtents;

        if (config != null)
        {
            pitch = config.Pitch;
            framePadding = config.FramePadding;
            zoomInFactor = config.ZoomInFactor;
            zoomOutFactor = config.ZoomOutFactor;
            panSensitivity = config.PanSensitivity;
            panSharpness = config.PanSharpness;
            zoomSensitivity = config.ZoomSensitivity;
            zoomSharpness = config.ZoomSharpness;
            yawSensitivity = config.YawSensitivity;
            yawSharpness = config.YawSharpness;
            yawClamp = config.YawClamp;
            orbitDragThreshold = config.OrbitDragThreshold;
            introZoomMultiplier = config.IntroZoomMultiplier;
            introDuration = config.IntroDuration;
            transitionDuration = config.TransitionDuration;
        }

        // Compute fitted zoom and zoom range, but place the camera pulled back so the intro
        // tween can ease in to the fitted distance.
        float fittedZoom = ComputeFittedZoom();
        minZoom = fittedZoom * zoomInFactor;
        maxZoom = fittedZoom * zoomOutFactor;
        desiredZoom = fittedZoom;
        currentZoom = fittedZoom * Mathf.Max(1f, introZoomMultiplier);
        currentYaw = desiredYaw;
        lastFramedScreenWidth = Screen.width;
        lastFramedScreenHeight = Screen.height;
        ApplyCameraTransform(false);

        if (introDuration > 0.001f && introZoomMultiplier > 1.001f)
        {
            StartTransitionTween(introDuration, introEase);
        }
        else
        {
            currentZoom = fittedZoom;
            ApplyCameraTransform(false);
        }
    }

    /// <summary>
    /// Replay the intro pull-in: snap the current zoom out by <see cref="introZoomMultiplier"/>
    /// and tween back to the fitted distance over <see cref="introDuration"/>. Pivot and yaw
    /// are reset to the current desired values. Use this when the player presses Play from a
    /// menu so the camera animates into the gameplay view.
    /// </summary>
    public void PlayIntro()
    {
        float fittedZoom = ComputeFittedZoom();
        minZoom = fittedZoom * zoomInFactor;
        maxZoom = fittedZoom * zoomOutFactor;
        desiredZoom = fittedZoom;
        desiredYaw = 0f;

        currentZoom = fittedZoom * Mathf.Max(1f, introZoomMultiplier);
        currentPivot = desiredPivot;
        currentYaw = desiredYaw;
        ApplyCameraTransform(false);

        StartTransitionTween(introDuration, introEase);
    }

    /// <summary>
    /// Reframe to a new pivot / extents and tween the camera there from its current pose.
    /// Used for restart / next-level transitions so the camera glides back to the fitted view
    /// instead of snapping (which would feel jarring after the user has panned/zoomed/orbited).
    /// </summary>
    public void Reframe(Vector3 boardPivot, Vector2 boardExtents)
    {
        boardHalfExtents = boardExtents;
        float fittedZoom = ComputeFittedZoom();
        minZoom = fittedZoom * zoomInFactor;
        maxZoom = fittedZoom * zoomOutFactor;
        desiredPivot = boardPivot;
        desiredZoom = fittedZoom;
        desiredYaw = 0f;
        lastFramedScreenWidth = Screen.width;
        lastFramedScreenHeight = Screen.height;
        StartTransitionTween(transitionDuration, transitionEase);
    }

    private void StartTransitionTween(float duration, Ease ease)
    {
        if (transitionTween != null && transitionTween.IsActive())
        {
            transitionTween.Kill();
        }

        if (duration <= 0.001f)
        {
            currentPivot = desiredPivot;
            currentZoom = desiredZoom;
            currentYaw = desiredYaw;
            isTransitioning = false;
            ApplyCameraTransform(false);
            return;
        }

        Vector3 startPivot = currentPivot;
        float startZoom = currentZoom;
        float startYaw = currentYaw;
        Vector3 endPivot = desiredPivot;
        float endZoom = desiredZoom;
        float endYaw = desiredYaw;

        isTransitioning = true;
        transitionTween = DOTween.To(() => 0f, t =>
        {
            currentPivot = Vector3.LerpUnclamped(startPivot, endPivot, t);
            currentZoom = Mathf.LerpUnclamped(startZoom, endZoom, t);
            currentYaw = Mathf.LerpUnclamped(startYaw, endYaw, t);
        }, 1f, duration).SetEase(ease).OnComplete(() =>
        {
            currentPivot = endPivot;
            currentZoom = endZoom;
            currentYaw = endYaw;
            isTransitioning = false;
        });
    }

    private void OnDisable()
    {
        if (transitionTween != null && transitionTween.IsActive())
        {
            transitionTween.Kill();
        }
        isTransitioning = false;
    }

    /// <summary>
    /// Distance from the look target needed for the board (with padding) to fit the screen,
    /// accounting for camera FOV, screen aspect, and the fixed pitch tilt.
    /// </summary>
    private float ComputeFittedZoom()
    {
        if (controlledCamera == null)
        {
            return 9.4f;
        }

        float boardWidth = Mathf.Max(0.1f, boardHalfExtents.x * 2f);
        float boardDepth = Mathf.Max(0.1f, boardHalfExtents.y * 2f);
        float padding = 1f + Mathf.Max(0f, framePadding);
        float aspect = (float)Screen.width / Mathf.Max(1, Screen.height);
        float halfFovV = Mathf.Deg2Rad * controlledCamera.fieldOfView * 0.5f;
        float halfFovH = Mathf.Atan(Mathf.Tan(halfFovV) * aspect);

        // The board lies flat (XZ); camera looks down with `pitch` from horizontal. Width
        // (X) projects to camera horizontal directly. Depth (Z) projects to camera vertical
        // foreshortened by cos(pitch). Solve for distance d so the half-extents fit each
        // half-frustum.
        float horizontalRequired = (boardWidth * 0.5f * padding) / Mathf.Tan(halfFovH);
        float verticalRequired = (boardDepth * 0.5f * Mathf.Cos(pitch * Mathf.Deg2Rad) * padding) / Mathf.Tan(halfFovV);
        return Mathf.Max(horizontalRequired, verticalRequired);
    }

    private void RefitToScreenIfChanged()
    {
        if (Screen.width == lastFramedScreenWidth && Screen.height == lastFramedScreenHeight)
        {
            return;
        }

        lastFramedScreenWidth = Screen.width;
        lastFramedScreenHeight = Screen.height;

        float fittedZoom = ComputeFittedZoom();
        // Preserve the user's relative zoom level (if they pinched in/out) when the aspect
        // changes, instead of snapping back to fit.
        float previousFitted = (minZoom + maxZoom) * 0.5f / ((zoomInFactor + zoomOutFactor) * 0.5f);
        float zoomScale = previousFitted > 0.0001f ? desiredZoom / previousFitted : 1f;

        minZoom = fittedZoom * zoomInFactor;
        maxZoom = fittedZoom * zoomOutFactor;
        desiredZoom = Mathf.Clamp(fittedZoom * zoomScale, minZoom, maxZoom);
    }

    private void LateUpdate()
    {
        if (inputManager == null || controlledCamera == null)
        {
            return;
        }

        if (!isTransitioning)
        {
            RefitToScreenIfChanged();
            HandleInput(Time.deltaTime);
        }
        ApplyCameraTransform(false);
    }

    private void HandleInput(float deltaTime)
    {
        if (inputManager.CameraPanActive)
        {
            Vector3 worldPan = ConvertScreenPanToWorld(inputManager.CameraPanDelta);
            desiredPivot += worldPan;
            desiredPivot.x = Mathf.Clamp(desiredPivot.x, -3.8f, 3.8f);
            desiredPivot.z = Mathf.Clamp(desiredPivot.z, -1.4f, 1.8f);
        }

        desiredZoom = Mathf.Clamp(desiredZoom - (inputManager.ZoomDelta * zoomSensitivity), minZoom, maxZoom);

        HandleOrbit();

        float panLerp = 1f - Mathf.Exp(-panSharpness * deltaTime);
        float zoomLerp = 1f - Mathf.Exp(-zoomSharpness * deltaTime);
        float yawLerp = 1f - Mathf.Exp(-yawSharpness * deltaTime);
        currentPivot = Vector3.Lerp(currentPivot, desiredPivot, panLerp);
        currentZoom = Mathf.Lerp(currentZoom, desiredZoom, zoomLerp);
        currentYaw = Mathf.LerpAngle(currentYaw, desiredYaw, yawLerp);
    }

    private void HandleOrbit()
    {
        // Empty-space drag → yaw orbit around the board pivot. We use TryClaimUnhandledDrag so:
        //  • Single taps / short clicks never trigger orbit — they fall through to the board
        //    controller's glass pickup logic, which captures primary on press.
        //  • If the user starts dragging past `orbitDragThreshold` pixels and *no other system*
        //    has already grabbed primary (e.g. board controller didn't pick up a glass), camera
        //    claims it.
        //  • Two-finger touch → multi-touch gesture is active → claim refuses → pan/zoom owns
        //    the gesture instead. Single-finger touch on empty space: orbit.
        if (!isOrbiting && inputManager.TryClaimUnhandledDrag(this, orbitDragThreshold))
        {
            isOrbiting = true;
        }

        if (isOrbiting)
        {
            // Multi-touch starts (pan/zoom takes over) or any release ends the orbit.
            if (!inputManager.OwnsPrimary(this) ||
                inputManager.PrimaryReleasedThisFrame ||
                inputManager.MultiTouchGestureActive)
            {
                isOrbiting = false;
                inputManager.ReleasePrimary(this);
                return;
            }

            // Drag right → world rotates left (camera circles to the right around the board).
            desiredYaw += inputManager.PrimaryScreenDelta.x * yawSensitivity;

            if (yawClamp > 0.01f)
            {
                desiredYaw = Mathf.Clamp(desiredYaw, -yawClamp, yawClamp);
            }
        }
    }

    private void ApplyCameraTransform(bool immediate)
    {
        if (controlledCamera == null)
        {
            return;
        }

        if (immediate)
        {
            currentPivot = desiredPivot;
            currentZoom = desiredZoom;
            currentYaw = desiredYaw;
        }

        // Yaw first (around world Y) then pitch (around camera-local right). The combined
        // rotation orbits the camera around the look target at the configured pitch.
        Quaternion orbit = Quaternion.Euler(0f, currentYaw, 0f) * Quaternion.Euler(pitch, 0f, 0f);
        Vector3 cameraOffset = orbit * new Vector3(0f, 0f, -currentZoom);
        Vector3 lookTarget = currentPivot + lookOffset;
        controlledCamera.transform.position = lookTarget + cameraOffset;
        controlledCamera.transform.rotation = Quaternion.LookRotation(lookTarget - controlledCamera.transform.position, Vector3.up);
    }

    private Vector3 ConvertScreenPanToWorld(Vector2 screenDelta)
    {
        Vector3 right = controlledCamera.transform.right;
        right.y = 0f;
        right.Normalize();

        Vector3 forward = Vector3.Cross(Vector3.up, right).normalized;
        float panScale = panSensitivity * Mathf.Lerp(0.75f, 1.4f, Mathf.InverseLerp(minZoom, maxZoom, currentZoom));
        // Vertical screen delta now uses +forward so dragging up moves the camera back (the
        // world appears to slide downward), matching the natural "grab the world and drag" feel.
        return (-right * screenDelta.x * panScale) + (forward * screenDelta.y * panScale);
    }
}
