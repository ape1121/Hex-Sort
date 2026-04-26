using System.Collections.Generic;
using Ape.Data;
using UnityEngine;

public sealed class HexSortMaterialLibrary
{
    private readonly Dictionary<LiquidColorId, Color> liquidColors = new Dictionary<LiquidColorId, Color>();
    private readonly Material highlightTemplate;
    private readonly Material streamTemplate;
    private readonly Material dropletTemplate;
    private readonly Texture2D streamFlowTexture;
    private readonly Texture2D dropletSpriteTexture;
    private readonly Shader liquidShader;

    public HexSortMaterialLibrary(ColorsConfig colorsConfig = null)
    {
        // Seed palette from ColorsConfig if provided, otherwise fall back to baked defaults so
        // the library still works in scenes / tests without a config asset wired up.
        if (colorsConfig != null && colorsConfig.Entries != null)
        {
            for (int i = 0; i < colorsConfig.Entries.Length; i++)
            {
                var entry = colorsConfig.Entries[i];
                if (entry.Id != LiquidColorId.None)
                {
                    liquidColors[entry.Id] = entry.Color;
                }
            }
        }
        if (!liquidColors.ContainsKey(LiquidColorId.Coral)) liquidColors[LiquidColorId.Coral] = new Color(0.96f, 0.39f, 0.35f);
        if (!liquidColors.ContainsKey(LiquidColorId.Sky))   liquidColors[LiquidColorId.Sky]   = new Color(0.25f, 0.67f, 0.98f);
        if (!liquidColors.ContainsKey(LiquidColorId.Mint))  liquidColors[LiquidColorId.Mint]  = new Color(0.34f, 0.88f, 0.69f);
        if (!liquidColors.ContainsKey(LiquidColorId.Gold))  liquidColors[LiquidColorId.Gold]  = new Color(0.98f, 0.79f, 0.28f);
        if (!liquidColors.ContainsKey(LiquidColorId.Grape)) liquidColors[LiquidColorId.Grape] = new Color(0.58f, 0.41f, 0.89f);
        if (!liquidColors.ContainsKey(LiquidColorId.Rose))  liquidColors[LiquidColorId.Rose]  = new Color(0.97f, 0.50f, 0.71f);

        GlassMaterial = RuntimeViewUtility.CreateTransparentLitMaterial(new Color(0.93f, 0.97f, 1f, 0.24f), 1f);
        BoardMaterial = RuntimeViewUtility.CreateOpaqueMaterial(new Color(0.96f, 0.91f, 0.84f), 0.12f);
        TableMaterial = RuntimeViewUtility.CreateOpaqueMaterial(new Color(0.89f, 0.81f, 0.72f), 0.16f);
        BackdropMaterial = RuntimeViewUtility.CreateOpaqueMaterial(new Color(0.84f, 0.91f, 0.98f), 0.05f);
        AccentMaterial = RuntimeViewUtility.CreateOpaqueMaterial(new Color(0.67f, 0.79f, 0.92f), 0.20f);

        streamFlowTexture = RuntimeViewUtility.CreateFlowStreakTexture(48, 256, 11);
        dropletSpriteTexture = RuntimeViewUtility.CreateDropletSpriteTexture(64);

        highlightTemplate = RuntimeViewUtility.CreateTransparentUnlitMaterial(new Color(0.2f, 0.66f, 1f, 0f));

        streamTemplate = RuntimeViewUtility.CreateTransparentUnlitMaterial(new Color(1f, 1f, 1f, 0.92f));
        RuntimeViewUtility.SetMaterialMainTexture(streamTemplate, streamFlowTexture);

        dropletTemplate = RuntimeViewUtility.CreateParticleAlphaBlendedMaterial(new Color(1f, 1f, 1f, 0.95f));
        RuntimeViewUtility.SetMaterialMainTexture(dropletTemplate, dropletSpriteTexture);

        liquidShader = Shader.Find("HexSort/Liquid");
        if (liquidShader == null)
        {
            Debug.LogError("HexSort/Liquid shader could not be located. Liquid rendering will fall back to a magenta error material.");
        }
    }

    public Material GlassMaterial { get; }

    public Material BoardMaterial { get; }

    public Material TableMaterial { get; }

    public Material BackdropMaterial { get; }

    public Material AccentMaterial { get; }

    public Color GetLiquidColor(LiquidColorId color)
    {
        if (color == LiquidColorId.None)
        {
            return Color.white;
        }

        return liquidColors[color];
    }

    public Material CreateHighlightMaterialInstance()
    {
        return new Material(highlightTemplate);
    }

    public Material CreateStreamMaterialInstance()
    {
        Material material = new Material(streamTemplate);
        RuntimeViewUtility.SetMaterialMainTexture(material, streamFlowTexture);
        return material;
    }

    public Material CreateDropletMaterialInstance()
    {
        Material material = RuntimeViewUtility.CreateParticleAlphaBlendedMaterial(new Color(1f, 1f, 1f, 0.95f));
        RuntimeViewUtility.SetMaterialMainTexture(material, dropletSpriteTexture);
        return material;
    }

    public Material CreateLiquidMaterialInstance()
    {
        Shader shader = liquidShader != null ? liquidShader : Shader.Find("Universal Render Pipeline/Unlit");
        Material material = new Material(shader);
        material.SetColor("_FoamColor", new Color(1f, 1f, 1f, 1f));
        material.SetFloat("_FoamThickness", 0.055f);
        material.SetFloat("_FoamStrength", 0.55f);
        material.SetFloat("_DepthTint", 0.22f);
        material.SetFloat("_WobbleAmount", 0.018f);
        material.SetFloat("_WobbleSpeed", 1.6f);
        return material;
    }
}
