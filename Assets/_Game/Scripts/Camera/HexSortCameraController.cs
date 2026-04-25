using UnityEngine;

public sealed class HexSortCameraController : MonoBehaviour
{
    [SerializeField] private float panSensitivity = 0.016f;
    [SerializeField] private float zoomSensitivity = 1.4f;
    [SerializeField] private float zoomSharpness = 10f;
    [SerializeField] private float panSharpness = 12f;
    [SerializeField] private float minZoom = 6.5f;
    [SerializeField] private float maxZoom = 13.5f;
    [SerializeField] private float pitch = 24f;

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
    private float desiredYaw;
    private float currentYaw;
    private bool isOrbiting;
    private Vector3 lookOffset = new Vector3(0f, 0.95f, 0f);

    public void Initialize(HexSortInputManager input, Camera targetCamera, Vector3 boardPivot, float initialZoom)
    {
        inputManager = input;
        controlledCamera = targetCamera;
        desiredPivot = boardPivot;
        currentPivot = boardPivot;
        desiredZoom = initialZoom;
        currentZoom = initialZoom;
        ApplyCameraTransform(true);
    }

    private void LateUpdate()
    {
        if (inputManager == null || controlledCamera == null)
        {
            return;
        }

        HandleInput(Time.deltaTime);
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
