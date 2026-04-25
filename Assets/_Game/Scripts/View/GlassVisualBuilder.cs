using UnityEngine;

public readonly struct GlassVisualReferences
{
    public GlassVisualReferences(Renderer highlightRenderer, Transform liquidRoot)
    {
        HighlightRenderer = highlightRenderer;
        LiquidRoot = liquidRoot;
    }

    public Renderer HighlightRenderer { get; }

    public Transform LiquidRoot { get; }
}

public static class GlassVisualBuilder
{
    public static GlassVisualReferences Build(Transform glassRoot, HexSortMaterialLibrary materials)
    {
        const int ignoreRaycastLayer = 2;
        const float bodyRadius = 0.56f;
        const float wallHeight = 2.28f;
        const float wallThickness = 0.04f;
        const float wallWidth = 0.24f;
        const int wallSegmentCount = 16;

        Transform shellRoot = new GameObject("Shell").transform;
        shellRoot.SetParent(glassRoot, false);

        for (int i = 0; i < wallSegmentCount; i++)
        {
            float angle = (Mathf.PI * 2f * i) / wallSegmentCount;
            Vector3 position = new Vector3(Mathf.Cos(angle) * bodyRadius, 0.22f + (wallHeight * 0.5f), Mathf.Sin(angle) * bodyRadius);
            Quaternion rotation = Quaternion.Euler(0f, (-angle * Mathf.Rad2Deg) + 90f, 0f);

            GameObject wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.name = "Wall_" + i;
            wall.transform.SetParent(shellRoot, false);
            wall.transform.localPosition = position;
            wall.transform.localRotation = rotation;
            wall.transform.localScale = new Vector3(wallWidth, wallHeight, wallThickness);
            wall.GetComponent<Renderer>().sharedMaterial = materials.GlassMaterial;
            Object.Destroy(wall.GetComponent<Collider>());
            RuntimeViewUtility.SetLayerRecursively(wall, ignoreRaycastLayer);
        }

        GameObject baseDisc = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        baseDisc.name = "Base";
        baseDisc.transform.SetParent(shellRoot, false);
        baseDisc.transform.localPosition = new Vector3(0f, 0.11f, 0f);
        baseDisc.transform.localScale = new Vector3(1.1f, 0.08f, 1.1f);
        baseDisc.GetComponent<Renderer>().sharedMaterial = materials.GlassMaterial;
        Object.Destroy(baseDisc.GetComponent<Collider>());
        RuntimeViewUtility.SetLayerRecursively(baseDisc, ignoreRaycastLayer);

        GameObject rim = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        rim.name = "Rim";
        rim.transform.SetParent(shellRoot, false);
        rim.transform.localPosition = new Vector3(0f, 2.35f, 0f);
        rim.transform.localScale = new Vector3(1.18f, 0.045f, 1.18f);
        rim.GetComponent<Renderer>().sharedMaterial = materials.GlassMaterial;
        Object.Destroy(rim.GetComponent<Collider>());
        RuntimeViewUtility.SetLayerRecursively(rim, ignoreRaycastLayer);

        GameObject highlight = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        highlight.name = "Highlight";
        highlight.transform.SetParent(glassRoot, false);
        highlight.transform.localPosition = new Vector3(0f, 0.045f, 0f);
        highlight.transform.localScale = new Vector3(1.48f, 0.025f, 1.48f);
        Renderer highlightRenderer = highlight.GetComponent<Renderer>();
        highlightRenderer.sharedMaterial = materials.CreateHighlightMaterialInstance();
        Object.Destroy(highlight.GetComponent<Collider>());
        RuntimeViewUtility.SetLayerRecursively(highlight, ignoreRaycastLayer);

        Transform liquidRoot = new GameObject("Liquid").transform;
        liquidRoot.SetParent(glassRoot, false);
        liquidRoot.localPosition = Vector3.zero;
        liquidRoot.localRotation = Quaternion.identity;

        return new GlassVisualReferences(highlightRenderer, liquidRoot);
    }
}
