using System.Collections.Generic;
using UnityEngine;

public sealed class GlassLiquidView : MonoBehaviour
{
    private const float InteriorBottomLocalY = 0.18f;
    private const float UnitHeight = 0.44f;
    private const float InteriorRadius = 0.42f;
    private const float MeshTopLocalY = 4.5f;
    private const float RimLocalY = 2.18f;
    private const int RadialSegments = 24;
    private const int VerticalSegments = 14;
    private const int MaxLayerSlots = 6;
    private const int IgnoreRaycastLayer = 2;

    private readonly List<DisplayLayer> displayLayers = new List<DisplayLayer>();

    private Transform liquidRoot;
    private MeshRenderer liquidRenderer;
    private Material liquidMaterial;
    private GlassState boundState;
    private HexSortMaterialLibrary materials;
    private int capacity;

    private LiquidColorId previewColor;
    private float previewUnits;
    private LiquidDynamicsSample dynamics;
    private float currentAgitation;
    private float wobbleSeed;
    private float currentLeanX;
    private float currentLeanZ;

    public void Initialize(Transform renderRoot, GlassState state, HexSortMaterialLibrary materialLibrary, int maxCapacity)
    {
        liquidRoot = renderRoot;
        boundState = state;
        materials = materialLibrary;
        capacity = Mathf.Min(maxCapacity, MaxLayerSlots);
        wobbleSeed = Mathf.Abs(GetInstanceID() * 0.137f) % 17f;

        BuildMesh();
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

    public void RefreshGeometry()
    {
        if (boundState == null || liquidRoot == null || liquidMaterial == null)
        {
            return;
        }

        BuildDisplayLayers();
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

        float agitationTarget = Mathf.Clamp01(
            dynamics.Agitation +
            (dynamics.IsPouring ? 0.30f : 0f) +
            (dynamics.IsHeld ? 0.10f : 0f));
        currentAgitation = Mathf.Lerp(currentAgitation, agitationTarget, 1f - Mathf.Exp(-6f * deltaTime));

        float wobbleAmount = Mathf.Lerp(0.010f, 0.030f, currentAgitation);
        float wobbleSpeed = Mathf.Lerp(1.4f, 3.4f, currentAgitation);
        liquidMaterial.SetFloat("_WobbleAmount", wobbleAmount);
        liquidMaterial.SetFloat("_WobbleSpeed", wobbleSpeed);
        liquidMaterial.SetFloat("_WobbleSeed", wobbleSeed);

        Vector3 horizontalLean = Vector3.ProjectOnPlane(dynamics.ContainerUp, Vector3.up);
        if (horizontalLean.sqrMagnitude < 0.0001f)
        {
            horizontalLean = Vector3.zero;
        }

        float maxLeanPerMeter = 0.16f;
        float targetLeanX = -horizontalLean.x * maxLeanPerMeter * dynamics.FlowReadiness;
        float targetLeanZ = -horizontalLean.z * maxLeanPerMeter * dynamics.FlowReadiness;

        currentLeanX = Mathf.Lerp(currentLeanX, targetLeanX, 1f - Mathf.Exp(-7f * deltaTime));
        currentLeanZ = Mathf.Lerp(currentLeanZ, targetLeanZ, 1f - Mathf.Exp(-7f * deltaTime));

        liquidMaterial.SetFloat("_LeanX", currentLeanX);
        liquidMaterial.SetFloat("_LeanZ", currentLeanZ);
        liquidMaterial.SetFloat("_LeanCenterX", transform.position.x);
        liquidMaterial.SetFloat("_LeanCenterZ", transform.position.z);

        float fillLevel = ComputeFillLevelWorldY();
        liquidMaterial.SetFloat("_FillLevel", fillLevel);
        liquidMaterial.SetFloat("_BottomLevel", transform.position.y + InteriorBottomLocalY - 0.08f);

        ApplyLayersToMaterial();
    }

    private float ComputeFillLevelWorldY()
    {
        float totalUnits = GetTotalUnits();
        if (totalUnits <= 0.001f)
        {
            return transform.position.y - 100f;
        }

        float fillLocalHeight = totalUnits * UnitHeight;
        float fillLocalTop = InteriorBottomLocalY + fillLocalHeight;
        float uprightFillLevel = transform.position.y + fillLocalTop;

        Vector3 glassUp = transform.up;
        float upY = Mathf.Max(0.05f, glassUp.y);
        float rimCenterWorldY = transform.position.y + (upY * RimLocalY);
        float rimSafeWorldY = rimCenterWorldY - 0.05f;

        return Mathf.Min(uprightFillLevel, rimSafeWorldY);
    }

    private void BuildMesh()
    {
        GameObject meshObject = new GameObject("LiquidColumn");
        meshObject.transform.SetParent(liquidRoot, false);
        RuntimeViewUtility.SetLayerRecursively(meshObject, IgnoreRaycastLayer);

        Mesh mesh = LiquidMeshFactory.BuildLiquidColumn(
            RadialSegments,
            VerticalSegments,
            InteriorRadius,
            InteriorBottomLocalY,
            MeshTopLocalY);

        MeshFilter filter = meshObject.AddComponent<MeshFilter>();
        filter.sharedMesh = mesh;

        liquidRenderer = meshObject.AddComponent<MeshRenderer>();
        liquidRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        liquidRenderer.receiveShadows = false;
        liquidMaterial = materials.CreateLiquidMaterialInstance();
        liquidRenderer.sharedMaterial = liquidMaterial;
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

        float currentBottomLocalY = InteriorBottomLocalY;
        float baseWorldY = transform.position.y;

        for (int i = 0; i < MaxLayerSlots; i++)
        {
            string colorProp = "_Color" + i;
            string boundaryProp = "_Boundary" + i;

            if (i < layerCount)
            {
                DisplayLayer layer = displayLayers[i];
                Color layerColor = materials.GetLiquidColor(layer.Color);
                liquidMaterial.SetColor(colorProp, layerColor);
                liquidMaterial.SetFloat(boundaryProp, baseWorldY + currentBottomLocalY);
                currentBottomLocalY += layer.Units * UnitHeight;
            }
            else
            {
                liquidMaterial.SetColor(colorProp, Color.white);
                liquidMaterial.SetFloat(boundaryProp, baseWorldY - 100f);
            }
        }
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
