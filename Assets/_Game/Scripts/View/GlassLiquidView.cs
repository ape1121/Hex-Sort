using System.Collections.Generic;
using UnityEngine;

public sealed class GlassLiquidView : MonoBehaviour
{
    private const int RadialSegments = 24;
    private const int VerticalSegments = 14;
    private const int MaxLayerSlots = 6;
    private const int IgnoreRaycastLayer = 2;

    // The surface disc is built unit-radius and scaled so it always covers the largest possible
    // body cross-section even at extreme tilts. The shader clips to the implicit body cylinder
    // so any extra disc radius outside the glass is discarded.
    private const float SurfaceDiscOversize = 2.5f;

    private readonly List<DisplayLayer> displayLayers = new List<DisplayLayer>();

    private Transform liquidRoot;
    private Transform liquidColumnTransform;
    private MeshRenderer liquidRenderer;
    private GameObject surfaceObject;
    private Transform surfaceTransform;
    private MeshRenderer surfaceRenderer;
    private Material liquidMaterial;
    private GlassState boundState;
    private HexSortMaterialLibrary materials;
    private int capacity;
    private float interiorBottomLocalY;
    private float unitHeight;
    private float interiorBottomRadius;
    private float interiorTopRadius;
    private float meshTopLocalY;
    private float rimLocalY;

    private LiquidColorId previewColor;
    private float previewUnits;
    private LiquidDynamicsSample dynamics;
    private LiquidDynamicsSample previousDynamics;
    private bool hasPreviousDynamics;
    private float currentAgitation;
    private float wobbleSeed;

    // 2D damped-spring slosh state in world XZ. Driven by glass acceleration.
    private Vector2 sloshOffset;
    private Vector2 sloshVelocity;

    [Header("Slosh Tuning")]
    [SerializeField] private float sloshStiffness = 90f;
    [SerializeField] private float sloshDamping = 11f;
    [SerializeField] private float sloshSensitivity = 0.45f;
    [SerializeField] private float sloshMaxAmplitude = 0.18f;

    public void Initialize(Transform renderRoot, GlassState state, HexSortMaterialLibrary materialLibrary, int maxCapacity,
        float interiorBottomLocalY, float unitHeight, float interiorBottomRadius, float interiorTopRadius, float meshTopLocalY, float rimLocalY)
    {
        liquidRoot = renderRoot;
        boundState = state;
        materials = materialLibrary;
        capacity = Mathf.Min(maxCapacity, MaxLayerSlots);
        wobbleSeed = Mathf.Abs(GetInstanceID() * 0.137f) % 17f;

        this.interiorBottomLocalY = interiorBottomLocalY;
        this.unitHeight = unitHeight;
        this.interiorBottomRadius = interiorBottomRadius;
        this.interiorTopRadius = interiorTopRadius;
        this.meshTopLocalY = meshTopLocalY;
        this.rimLocalY = rimLocalY;

        BuildRenderer();
        RefreshGeometry();
    }

    public void SetPreview(LiquidColorId color, float units)
    {
        previewColor = color;
        previewUnits = units;
        RefreshGeometry();
    }

    public void ClearPreview()
    {
        previewColor = LiquidColorId.None;
        previewUnits = 0f;
        RefreshGeometry();
    }

    public void SetDynamics(LiquidDynamicsSample sample)
    {
        dynamics = sample;
    }

    public void AddSloshImpulse(Vector3 worldImpulse)
    {
        sloshVelocity += new Vector2(worldImpulse.x, worldImpulse.z);
    }

    public void RefreshGeometry()
    {
        if (boundState == null || liquidRoot == null || liquidMaterial == null)
        {
            return;
        }

        BuildDisplayLayers();
        ApplyLayersToMaterial();
    }

    private void LateUpdate()
    {
        if (liquidMaterial == null)
        {
            return;
        }

        float deltaTime = Time.deltaTime;
        if (deltaTime <= 0f)
        {
            return;
        }

        UpdateSloshState(deltaTime);

        float agitationTarget = Mathf.Clamp01(
            dynamics.Agitation +
            sloshOffset.magnitude * 0.6f +
            (dynamics.IsPouring ? 0.30f : 0f) +
            (dynamics.IsHeld ? 0.10f : 0f));
        currentAgitation = Mathf.Lerp(currentAgitation, agitationTarget, 1f - Mathf.Exp(-6f * deltaTime));

        float wobbleAmount = Mathf.Lerp(0.012f, 0.045f, currentAgitation);
        float wobbleSpeed = Mathf.Lerp(1.6f, 4.2f, currentAgitation);
        liquidMaterial.SetFloat("_WobbleAmount", wobbleAmount);
        liquidMaterial.SetFloat("_WobbleSpeed", wobbleSpeed);
        liquidMaterial.SetFloat("_WobbleSeed", wobbleSeed);

        Vector2 sloshTilt = sloshOffset;
        float tiltMag = sloshTilt.magnitude;
        if (tiltMag > sloshMaxAmplitude)
        {
            sloshTilt = sloshTilt * (sloshMaxAmplitude / tiltMag);
        }

        liquidMaterial.SetFloat("_SloshX", sloshTilt.x);
        liquidMaterial.SetFloat("_SloshZ", sloshTilt.y);
        liquidMaterial.SetFloat("_GlassCenterX", transform.position.x);
        liquidMaterial.SetFloat("_GlassCenterZ", transform.position.z);

        float fillLevel = ComputeFillLevelWorldY();
        PushFillUniforms(fillLevel);
        UpdateSurfaceDisc(fillLevel);
        UpdateLayerBoundaries(fillLevel);
        UpdateRendererVisibility();
    }

    private void UpdateSloshState(float deltaTime)
    {
        Vector3 currentVel = dynamics.LinearVelocity;
        Vector3 prevVel = hasPreviousDynamics ? previousDynamics.LinearVelocity : currentVel;
        Vector3 accel = (currentVel - prevVel) / Mathf.Max(0.0001f, deltaTime);
        Vector2 accelXZ = new Vector2(accel.x, accel.z);

        Vector2 force = -accelXZ * sloshSensitivity
                        - sloshOffset * sloshStiffness
                        - sloshVelocity * sloshDamping;
        sloshVelocity += force * deltaTime;
        sloshOffset += sloshVelocity * deltaTime;

        const float maxOffset = 0.5f;
        if (sloshOffset.sqrMagnitude > maxOffset * maxOffset)
        {
            sloshOffset = sloshOffset.normalized * maxOffset;
        }

        previousDynamics = dynamics;
        hasPreviousDynamics = true;
    }

    private float ComputeFillLevelWorldY()
    {
        float totalUnits = GetTotalUnits();
        if (totalUnits <= 0.001f)
        {
            return transform.position.y - 100f;
        }

        float fillLocalHeight = totalUnits * unitHeight;
        float fillLocalTop = interiorBottomLocalY + fillLocalHeight;
        float uprightFillLevel = transform.position.y + fillLocalTop;

        // Clamp at the actual world-Y of the rim's lowest point (accounts for tilt).
        // The rim is a circle of radius rimRadius around the rim centre, in the plane perpendicular
        // to glass.up. Its lowest world Y equals rimCenterY - rimRadius * sin(tiltAngle), where
        // sin(tiltAngle) = horizontal component magnitude of glass.up.
        Vector3 glassUp = transform.up;
        float upY = Mathf.Max(0.05f, glassUp.y);
        float horizontalLean = Mathf.Sqrt(Mathf.Max(0f, 1f - upY * upY));
        float rimCenterWorldY = transform.position.y + (upY * rimLocalY);
        // Approximate the rim's outer radius as the interior top radius (no rimRadius available
        // here without further plumbing — close enough for the visual safety margin).
        float effectiveRimRadius = Mathf.Max(interiorBottomRadius, interiorTopRadius);
        float rimLowestWorldY = rimCenterWorldY - (horizontalLean * (effectiveRimRadius + 0.02f));

        return Mathf.Min(uprightFillLevel, rimLowestWorldY);
    }

    private void PushFillUniforms(float fillLevel)
    {
        liquidMaterial.SetFloat("_FillLevel", fillLevel);

        // Implicit-body-cylinder uniforms used by the surface fragment for clipping.
        Vector3 up = transform.up;
        liquidMaterial.SetVector("_GlassCenter", new Vector4(transform.position.x, transform.position.y, transform.position.z, 0f));
        liquidMaterial.SetVector("_GlassUp", new Vector4(up.x, up.y, up.z, 0f));
        liquidMaterial.SetFloat("_BodyBottomLocalY", interiorBottomLocalY);
        liquidMaterial.SetFloat("_BodyTopLocalY", rimLocalY);
        liquidMaterial.SetFloat("_BodyBottomRadius", interiorBottomRadius);
        liquidMaterial.SetFloat("_BodyTopRadius", interiorTopRadius);
    }

    private void UpdateSurfaceDisc(float fillLevel)
    {
        if (surfaceTransform == null)
        {
            return;
        }

        bool empty = GetTotalUnits() <= 0.001f;
        if (empty)
        {
            if (surfaceObject.activeSelf)
            {
                surfaceObject.SetActive(false);
            }
            return;
        }

        if (!surfaceObject.activeSelf)
        {
            surfaceObject.SetActive(true);
        }

        // Always-horizontal world-space disc: the fragment shader clips it to the actual body
        // cross-section so we can use a generous size without worrying about overflow.
        surfaceTransform.position = new Vector3(transform.position.x, fillLevel, transform.position.z);
        surfaceTransform.rotation = Quaternion.identity;

        float horizontalScale = liquidColumnTransform != null
            ? Mathf.Max(0.0001f, liquidColumnTransform.lossyScale.x)
            : 1f;
        float maxRadius = Mathf.Max(interiorBottomRadius, interiorTopRadius) * horizontalScale * SurfaceDiscOversize;
        surfaceTransform.localScale = new Vector3(maxRadius, 1f, maxRadius);
    }

    private void UpdateLayerBoundaries(float fillLevel)
    {
        // World-Y boundaries scaled to fit between bottom and fillLevel, so layers stay horizontal
        // in world (gravity-aligned) and the visible band heights compress when the rim caps the fill.
        float bottomWorldY = transform.position.y + interiorBottomLocalY;
        float totalUnits = GetTotalUnits();
        float visibleSpan = Mathf.Max(0.0001f, fillLevel - bottomWorldY);
        float perUnit = totalUnits > 0.001f ? visibleSpan / totalUnits : 0f;

        int layerCount = Mathf.Min(displayLayers.Count, MaxLayerSlots);

        float runningUnits = 0f;
        for (int i = 0; i < MaxLayerSlots; i++)
        {
            string boundaryProp = "_Boundary" + i;
            if (i < layerCount)
            {
                liquidMaterial.SetFloat(boundaryProp, bottomWorldY + runningUnits * perUnit);
                runningUnits += displayLayers[i].Units;
            }
            else
            {
                liquidMaterial.SetFloat(boundaryProp, bottomWorldY - 100f);
            }
        }
    }

    private void UpdateRendererVisibility()
    {
        bool empty = GetTotalUnits() <= 0.001f;
        if (liquidRenderer != null && liquidRenderer.enabled == empty)
        {
            liquidRenderer.enabled = !empty;
        }
    }

    private void BuildRenderer()
    {
        GameObject meshObject = new GameObject("LiquidColumn");
        meshObject.transform.SetParent(liquidRoot, false);
        liquidColumnTransform = meshObject.transform;
        RuntimeViewUtility.SetLayerRecursively(meshObject, IgnoreRaycastLayer);

        MeshFilter filter = meshObject.AddComponent<MeshFilter>();
        // Build the column once at the full glass interior height. The shader clips above fillLevel.
        filter.sharedMesh = LiquidMeshFactory.BuildLiquidColumn(
            RadialSegments,
            VerticalSegments,
            interiorBottomRadius,
            interiorTopRadius,
            interiorBottomLocalY,
            rimLocalY,
            includeTopCap: false);

        liquidRenderer = meshObject.AddComponent<MeshRenderer>();
        liquidRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        liquidRenderer.receiveShadows = false;
        liquidMaterial = materials.CreateLiquidMaterialInstance();
        liquidRenderer.sharedMaterial = liquidMaterial;

        BuildSurfaceMesh();
    }

    private void BuildSurfaceMesh()
    {
        surfaceObject = new GameObject("LiquidSurface_" + GetInstanceID());
        surfaceTransform = surfaceObject.transform;
        RuntimeViewUtility.SetLayerRecursively(surfaceObject, IgnoreRaycastLayer);

        MeshFilter filter = surfaceObject.AddComponent<MeshFilter>();
        filter.sharedMesh = LiquidMeshFactory.BuildLiquidSurface(RadialSegments);

        surfaceRenderer = surfaceObject.AddComponent<MeshRenderer>();
        surfaceRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        surfaceRenderer.receiveShadows = false;
        surfaceRenderer.sharedMaterial = liquidMaterial;

        surfaceObject.SetActive(false);
    }

    private void OnDestroy()
    {
        if (surfaceObject != null)
        {
            Object.Destroy(surfaceObject);
            surfaceObject = null;
        }
    }

    private void BuildDisplayLayers()
    {
        displayLayers.Clear();
        IReadOnlyList<LiquidColorId> units = boundState.Units;
        for (int i = 0; i < units.Count; i++)
        {
            AddLayerUnit(units[i], 1f);
        }

        if (previewColor == LiquidColorId.None || Mathf.Approximately(previewUnits, 0f))
        {
            return;
        }

        if (previewUnits < 0f && displayLayers.Count > 0)
        {
            int topIndex = displayLayers.Count - 1;
            DisplayLayer topLayer = displayLayers[topIndex];
            topLayer.Units = Mathf.Max(0f, topLayer.Units + previewUnits);
            if (topLayer.Units <= 0.001f)
            {
                displayLayers.RemoveAt(topIndex);
            }
            else
            {
                displayLayers[topIndex] = topLayer;
            }
        }
        else if (previewUnits > 0f)
        {
            AddLayerUnit(previewColor, previewUnits);
        }
    }

    private void AddLayerUnit(LiquidColorId color, float units)
    {
        if (displayLayers.Count == 0 || displayLayers[displayLayers.Count - 1].Color != color)
        {
            displayLayers.Add(new DisplayLayer(color, units));
            return;
        }

        DisplayLayer layer = displayLayers[displayLayers.Count - 1];
        layer.Units += units;
        displayLayers[displayLayers.Count - 1] = layer;
    }

    private void ApplyLayersToMaterial()
    {
        int layerCount = Mathf.Min(displayLayers.Count, MaxLayerSlots);
        liquidMaterial.SetFloat("_LayerCount", layerCount);

        Color topColor = Color.white;

        for (int i = 0; i < MaxLayerSlots; i++)
        {
            string colorProp = "_Color" + i;
            if (i < layerCount)
            {
                Color layerColor = materials.GetLiquidColor(displayLayers[i].Color);
                liquidMaterial.SetColor(colorProp, layerColor);
                topColor = layerColor;
            }
            else
            {
                liquidMaterial.SetColor(colorProp, Color.white);
            }
        }

        liquidMaterial.SetColor("_TopLayerColor", topColor);
    }

    private float GetTotalUnits()
    {
        float total = 0f;
        for (int i = 0; i < displayLayers.Count; i++)
        {
            total += displayLayers[i].Units;
        }

        return total;
    }

    private struct DisplayLayer
    {
        public DisplayLayer(LiquidColorId color, float units)
        {
            Color = color;
            Units = units;
        }

        public LiquidColorId Color;
        public float Units;
    }
}
