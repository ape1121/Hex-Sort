using UnityEngine;
using UnityEngine.Rendering;

public static class RuntimeViewUtility
{
    public static void SetLayerRecursively(GameObject rootObject, int layer)
    {
        rootObject.layer = layer;
        Transform rootTransform = rootObject.transform;
        for (int i = 0; i < rootTransform.childCount; i++)
        {
            SetLayerRecursively(rootTransform.GetChild(i).gameObject, layer);
        }
    }

    public static void SetMaterialColor(Material material, Color color)
    {
        if (material == null)
        {
            return;
        }

        if (material.HasProperty("_BaseColor"))
        {
            material.SetColor("_BaseColor", color);
        }

        if (material.HasProperty("_Color"))
        {
            material.SetColor("_Color", color);
        }
    }

    public static void SetMaterialMainTexture(Material material, Texture2D texture)
    {
        if (material == null || texture == null)
        {
            return;
        }

        if (material.HasProperty("_BaseMap"))
        {
            material.SetTexture("_BaseMap", texture);
        }

        if (material.HasProperty("_MainTex"))
        {
            material.SetTexture("_MainTex", texture);
        }

        material.mainTexture = texture;
    }

    public static void SetMaterialMainTextureOffset(Material material, Vector2 offset)
    {
        if (material == null)
        {
            return;
        }

        if (material.HasProperty("_BaseMap"))
        {
            material.SetTextureOffset("_BaseMap", offset);
        }

        if (material.HasProperty("_MainTex"))
        {
            material.SetTextureOffset("_MainTex", offset);
        }

        material.mainTextureOffset = offset;
    }

    public static Texture2D CreateFlowStreakTexture(int width = 32, int height = 256, int seed = 0)
    {
        Texture2D texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
        {
            name = "ProceduralFlowStreaks",
            wrapMode = TextureWrapMode.Repeat,
            filterMode = FilterMode.Bilinear,
        };

        Color[] pixels = new Color[width * height];
        float seedOffset = seed * 0.137f;

        for (int y = 0; y < height; y++)
        {
            float v = y / (float)height;
            for (int x = 0; x < width; x++)
            {
                float u = x / (float)width;

                float coreFalloff = 1f - Mathf.Abs((u * 2f) - 1f);
                coreFalloff = Mathf.Pow(Mathf.Clamp01(coreFalloff), 0.65f);

                float streak = Mathf.PerlinNoise((u * 9f) + seedOffset, (v * 14f) + seedOffset);
                float fine = Mathf.PerlinNoise((u * 26f) + seedOffset + 11f, (v * 48f) + seedOffset + 7f);
                float speckle = Mathf.PerlinNoise((u * 62f) + seedOffset + 31f, (v * 132f) + seedOffset + 41f);

                float intensity = Mathf.Lerp(0.55f, 1.05f, streak);
                intensity *= Mathf.Lerp(0.7f, 1.1f, fine);
                intensity *= Mathf.Lerp(0.85f, 1.05f, speckle);

                float alpha = Mathf.Clamp01(intensity * coreFalloff);
                pixels[(y * width) + x] = new Color(1f, 1f, 1f, alpha);
            }
        }

        texture.SetPixels(pixels);
        texture.Apply(false, false);
        return texture;
    }

    public static Texture2D CreateDropletSpriteTexture(int size = 64)
    {
        Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            name = "ProceduralDroplet",
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
        };

        Color[] pixels = new Color[size * size];
        Vector2 center = new Vector2(size * 0.5f, size * 0.5f);
        float maxRadius = size * 0.5f;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float distance = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), center) / maxRadius;
                float alpha = Mathf.Clamp01(1f - (distance * distance));
                alpha = Mathf.Pow(alpha, 1.4f);
                pixels[(y * size) + x] = new Color(1f, 1f, 1f, alpha);
            }
        }

        texture.SetPixels(pixels);
        texture.Apply(false, false);
        return texture;
    }

    public static Material CreateOpaqueMaterial(Color color, float smoothness)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null)
        {
            shader = Shader.Find("Standard");
        }

        Material material = new Material(shader);
        SetMaterialColor(material, color);

        if (material.HasProperty("_Smoothness"))
        {
            material.SetFloat("_Smoothness", smoothness);
        }

        if (material.HasProperty("_Glossiness"))
        {
            material.SetFloat("_Glossiness", smoothness);
        }

        return material;
    }

    public static Material CreateTransparentLitMaterial(Color color, float smoothness)
    {
        Material material = CreateOpaqueMaterial(color, smoothness);
        ConfigureTransparency(material);
        return material;
    }

    public static Material CreateTransparentUnlitMaterial(Color color)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null)
        {
            shader = Shader.Find("Unlit/Color");
        }

        Material material = new Material(shader);
        SetMaterialColor(material, color);
        ConfigureTransparency(material);
        return material;
    }

    public static Material CreateParticleAlphaBlendedMaterial(Color color)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        if (shader == null)
        {
            shader = Shader.Find("Particles/Standard Unlit");
        }

        if (shader == null)
        {
            shader = Shader.Find("Mobile/Particles/Alpha Blended");
        }

        if (shader == null)
        {
            shader = Shader.Find("Universal Render Pipeline/Unlit");
        }

        Material material = new Material(shader);
        SetMaterialColor(material, color);
        ConfigureTransparency(material);

        if (material.HasProperty("_ColorMode"))
        {
            material.SetFloat("_ColorMode", 0f);
        }

        return material;
    }

    public static void ConfigureTransparency(Material material)
    {
        material.renderQueue = (int)RenderQueue.Transparent;
        material.SetOverrideTag("RenderType", "Transparent");

        if (material.HasProperty("_Surface"))
        {
            material.SetFloat("_Surface", 1f);
        }

        if (material.HasProperty("_Blend"))
        {
            material.SetFloat("_Blend", 0f);
        }

        if (material.HasProperty("_SrcBlend"))
        {
            material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        }

        if (material.HasProperty("_DstBlend"))
        {
            material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
        }

        if (material.HasProperty("_ZWrite"))
        {
            material.SetFloat("_ZWrite", 0f);
        }

        material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
    }
}
