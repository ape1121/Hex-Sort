Shader "HexSort/Liquid"
{
    Properties
    {
        _Color0("Layer 0 Color", Color) = (1, 1, 1, 1)
        _Color1("Layer 1 Color", Color) = (1, 1, 1, 1)
        _Color2("Layer 2 Color", Color) = (1, 1, 1, 1)
        _Color3("Layer 3 Color", Color) = (1, 1, 1, 1)
        _Color4("Layer 4 Color", Color) = (1, 1, 1, 1)
        _Color5("Layer 5 Color", Color) = (1, 1, 1, 1)

        _Boundary0("Layer 0 Bottom (World Y)", Float) = 0
        _Boundary1("Layer 1 Bottom (World Y)", Float) = 0
        _Boundary2("Layer 2 Bottom (World Y)", Float) = 0
        _Boundary3("Layer 3 Bottom (World Y)", Float) = 0
        _Boundary4("Layer 4 Bottom (World Y)", Float) = 0
        _Boundary5("Layer 5 Bottom (World Y)", Float) = 0

        _LayerCount("Layer Count", Float) = 0
        _TopLayerColor("Top Layer Color", Color) = (1, 1, 1, 1)
        _FillLevel("Fill Level (World Y)", Float) = 0

        _GlassCenter("Glass Centre (World)", Vector) = (0, 0, 0, 0)
        _GlassUp("Glass Up (World)", Vector) = (0, 1, 0, 0)
        _BodyBottomLocalY("Body Bottom Local Y", Float) = 0
        _BodyTopLocalY("Body Top Local Y", Float) = 1
        _BodyBottomRadius("Body Bottom Radius", Float) = 0.42
        _BodyTopRadius("Body Top Radius", Float) = 0.42

        _FoamColor("Foam Color", Color) = (1, 1, 1, 1)
        _FoamStrength("Foam Strength", Range(0, 1)) = 0.10
        _DepthTint("Depth Tint", Range(0, 1)) = 0.18

        _WobbleAmount("Wobble Amount", Float) = 0.018
        _WobbleSpeed("Wobble Speed", Float) = 1.6
        _WobbleSeed("Wobble Seed", Float) = 0

        _SloshX("Slosh X (per metre)", Float) = 0
        _SloshZ("Slosh Z (per metre)", Float) = 0
        _GlassCenterX("Glass Centre X (legacy)", Float) = 0
        _GlassCenterZ("Glass Centre Z (legacy)", Float) = 0

        _CausticStrength("Caustic Strength", Range(0, 1)) = 0.35
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Geometry+10"
            "IgnoreProjector" = "True"
        }
        LOD 200
        Cull Off

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS_CASCADE

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _Color0;
                float4 _Color1;
                float4 _Color2;
                float4 _Color3;
                float4 _Color4;
                float4 _Color5;
                float _Boundary0;
                float _Boundary1;
                float _Boundary2;
                float _Boundary3;
                float _Boundary4;
                float _Boundary5;
                float _LayerCount;
                float4 _TopLayerColor;
                float _FillLevel;
                float4 _GlassCenter;
                float4 _GlassUp;
                float _BodyBottomLocalY;
                float _BodyTopLocalY;
                float _BodyBottomRadius;
                float _BodyTopRadius;
                float4 _FoamColor;
                float _FoamStrength;
                float _DepthTint;
                float _WobbleAmount;
                float _WobbleSpeed;
                float _WobbleSeed;
                float _SloshX;
                float _SloshZ;
                float _GlassCenterX;
                float _GlassCenterZ;
                float _CausticStrength;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 worldPos : TEXCOORD0;
                float3 worldNormal : TEXCOORD1;
                float2 uv : TEXCOORD2;
                float fogCoord : TEXCOORD3;
            };

            float Hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 78.233);
                return frac(p.x * p.y);
            }

            float ValueNoise2D(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                float a = Hash21(i);
                float b = Hash21(i + float2(1, 0));
                float c = Hash21(i + float2(0, 1));
                float d = Hash21(i + float2(1, 1));
                float2 u = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y) * 2.0 - 1.0;
            }

            float ComputeBrownianWobble(float3 worldPos)
            {
                float t = _Time.y * _WobbleSpeed;
                float n1 = ValueNoise2D(worldPos.xz * 1.7 + float2(t, t * 0.71) + _WobbleSeed);
                float n2 = ValueNoise2D(worldPos.xz * 4.3 + float2(-t * 0.83, t * 1.21) + _WobbleSeed * 1.5);
                float n3 = sin(t * 2.4 + (worldPos.x + worldPos.z) * 3.1 + _WobbleSeed * 2.3) * 0.4;
                return (n1 * 0.6 + n2 * 0.3 + n3 * 0.2) * _WobbleAmount;
            }

            float ComputeCausticShimmer(float3 worldPos)
            {
                float t = _Time.y;
                float a = ValueNoise2D(worldPos.xz * 5.3 + float2(t * 0.9, -t * 0.6));
                float b = ValueNoise2D(worldPos.xz * 9.7 + float2(-t * 1.1, t * 0.7));
                float c = ValueNoise2D(worldPos.xz * 2.1 + float2(t * 0.4, t * 0.5));
                float caustic = saturate(a * 0.45 + b * 0.35 + c * 0.30 + 0.30);
                return caustic * caustic;
            }

            float ComputeSloshLift(float3 worldPos)
            {
                float2 fromCentre = worldPos.xz - _GlassCenter.xz;
                return fromCentre.x * _SloshX + fromCentre.y * _SloshZ;
            }

            // Effective surface Y at this world XZ — used by both the surface vertex displacement
            // AND the body's discard line so the cut and the cap stay glued together as the
            // surface wobbles and sloshes.
            float ComputeEffectiveFillY(float3 worldPos)
            {
                return _FillLevel + ComputeSloshLift(worldPos) + ComputeBrownianWobble(worldPos);
            }

            // Returns true if `worldPos` is inside the implicit body cylinder (used to clip the
            // surface disc to the glass interior cross-section regardless of tilt or taper).
            bool IsInsideBodyCylinder(float3 worldPos)
            {
                float3 axisDir = normalize(_GlassUp.xyz);
                float3 toFrag = worldPos - _GlassCenter.xyz;
                float t = dot(toFrag, axisDir);
                float3 perp = toFrag - t * axisDir;
                float distFromAxis = length(perp);

                float span = max(0.0001, _BodyTopLocalY - _BodyBottomLocalY);
                float fillT = saturate((t - _BodyBottomLocalY) / span);
                float bodyRadius = lerp(_BodyBottomRadius, _BodyTopRadius, fillT);

                return distFromAxis <= bodyRadius
                    && t >= _BodyBottomLocalY - 0.02
                    && t <= _BodyTopLocalY + 0.02;
            }

            float3 GetLayerColor(float worldY)
            {
                float3 color = _Color0.rgb;
                if ((_LayerCount > 1.5) && (worldY >= _Boundary1)) color = _Color1.rgb;
                if ((_LayerCount > 2.5) && (worldY >= _Boundary2)) color = _Color2.rgb;
                if ((_LayerCount > 3.5) && (worldY >= _Boundary3)) color = _Color3.rgb;
                if ((_LayerCount > 4.5) && (worldY >= _Boundary4)) color = _Color4.rgb;
                if ((_LayerCount > 5.5) && (worldY >= _Boundary5)) color = _Color5.rgb;
                return color;
            }

            Varyings Vert(Attributes IN)
            {
                Varyings OUT = (Varyings)0;

                float3 worldPos = TransformObjectToWorld(IN.positionOS.xyz);
                float3 worldNormal = TransformObjectToWorldNormal(IN.normalOS);

                if (IN.uv.y > 0.99)
                {
                    // Surface disc: place each vertex at the effective surface height for its XZ,
                    // with a small upward bias so it sits a hair above the body cut and never
                    // z-fights with body fragments at the surface.
                    worldPos.y = ComputeEffectiveFillY(worldPos) + 0.0025;
                    worldNormal = float3(0, 1, 0);
                }

                OUT.positionHCS = TransformWorldToHClip(worldPos);
                OUT.worldPos = worldPos;
                OUT.worldNormal = worldNormal;
                OUT.uv = IN.uv;
                OUT.fogCoord = ComputeFogFactor(OUT.positionHCS.z);
                return OUT;
            }

            half4 Frag(Varyings IN) : SV_Target
            {
                float3 baseColor;

                // Foam band: thin slab above the wobbled surface where both the disc and the
                // body render the same foam colour. Acts as a "flat cover" beneath the wobble
                // disc — wherever the disc has interpolation gaps, the body's foam band shows
                // the same colour, so the seam is invisible and you can't see inside the mesh.
                const float foamBandThickness = 0.006;

                if (IN.uv.y > 0.99)
                {
                    // Surface fragment: clip to actual body cross-section so the disc reads as the
                    // exact intersection of the horizontal fill plane with the (possibly tilted) cylinder.
                    if (!IsInsideBodyCylinder(IN.worldPos))
                    {
                        discard;
                    }

                    // Upper clip only: discard fragments that interpolate above the body's foam-
                    // band ceiling. The lower bound is intentionally NOT clipped — where disc verts
                    // interpolate below the surface, the body's foam band renders the *same* foam
                    // colour at the same world position, so any z-fight is invisible. This kills
                    // the small "rips" caused by per-fragment lower-bound discards.
                    float effectiveFill = ComputeEffectiveFillY(IN.worldPos);
                    if (IN.worldPos.y > effectiveFill + foamBandThickness)
                    {
                        discard;
                    }

                    baseColor = _TopLayerColor.rgb * 1.05;
                    float caustic = ComputeCausticShimmer(IN.worldPos);
                    baseColor += caustic * _CausticStrength * 0.4 * _FoamColor.rgb;
                }
                else
                {
                    // Body fragment: hard discard above the foam band. Inside the band, render
                    // the same foam colour as the disc; below, render the normal layer colour.
                    float effectiveFill = ComputeEffectiveFillY(IN.worldPos);
                    if (IN.worldPos.y > effectiveFill + foamBandThickness)
                    {
                        discard;
                    }

                    if (IN.uv.y < 0.01)
                    {
                        // Bottom cap.
                        baseColor = _Color0.rgb * 0.92;
                    }
                    else if (IN.worldPos.y > effectiveFill)
                    {
                        // Foam band fills the slab above the wobbly surface so disc gaps never
                        // expose the body's far inside wall.
                        baseColor = _TopLayerColor.rgb * 1.05;
                        float caustic = ComputeCausticShimmer(IN.worldPos);
                        baseColor += caustic * _CausticStrength * 0.4 * _FoamColor.rgb;
                    }
                    else
                    {
                        // Side wall below the surface: layer colour by world Y.
                        baseColor = GetLayerColor(IN.worldPos.y);

                        float depthFromTop = max(0, effectiveFill - IN.worldPos.y);
                        float depthFactor = saturate(depthFromTop / 0.85);
                        baseColor *= lerp(1.05, 1.0 - _DepthTint, depthFactor);

                        float caustic = ComputeCausticShimmer(IN.worldPos);
                        baseColor += caustic * _CausticStrength * 0.4 * _FoamColor.rgb;
                    }
                }

                Light mainLight = GetMainLight();
                float3 normalWS = normalize(IN.worldNormal);
                float ndl = saturate(dot(normalWS, mainLight.direction)) * 0.55 + 0.45;
                float3 lit = baseColor * (mainLight.color.rgb * ndl + unity_AmbientSky.rgb * 0.4);

                float3 viewDir = normalize(_WorldSpaceCameraPos - IN.worldPos);
                float fresnel = pow(1.0 - saturate(dot(normalWS, viewDir)), 3.0);
                lit += fresnel * 0.18 * _FoamColor.rgb;

                lit = MixFog(lit, IN.fogCoord);
                return half4(lit, 1.0);
            }
            ENDHLSL
        }
    }
}
