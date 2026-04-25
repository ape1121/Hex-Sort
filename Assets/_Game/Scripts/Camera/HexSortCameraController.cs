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

    private HexSortInputManager inputManager;
    private Camera controlledCamera;
    private Vector3 desiredPivot;
    private Vector3 currentPivot;
    private float desiredZoom;
    private float currentZoom;
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

        float panLerp = 1f - Mathf.Exp(-panSharpness * deltaTime);
        float zoomLerp = 1f - Mathf.Exp(-zoomSharpness * deltaTime);
        currentPivot = Vector3.Lerp(currentPivot, desiredPivot, panLerp);
        currentZoom = Mathf.Lerp(currentZoom, desiredZoom, zoomLerp);
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
        }

        Quaternion orbit = Quaternion.Euler(pitch, 0f, 0f);
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
        return (-right * screenDelta.x * panScale) + (-forward * screenDelta.y * panScale);
    }
}
