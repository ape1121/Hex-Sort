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
        _FillLevel("Fill Level (World Y)", Float) = 0
        _BottomLevel("Bottom Level (World Y)", Float) = -10

        _FoamColor("Foam Color", Color) = (1, 1, 1, 1)
        _FoamThickness("Foam Thickness", Float) = 0.06
        _FoamStrength("Foam Strength", Range(0, 1)) = 0.55
        _DepthTint("Depth Tint", Range(0, 1)) = 0.18

        _WobbleAmount("Wobble Amount", Float) = 0.018
        _WobbleSpeed("Wobble Speed", Float) = 1.6
        _WobbleSeed("Wobble Seed", Float) = 0

        _LeanX("Surface Lean X (per meter)", Float) = 0
        _LeanZ("Surface Lean Z (per meter)", Float) = 0
        _LeanCenterX("Lean Center X", Float) = 0
        _LeanCenterZ("Lean Center Z", Float) = 0
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
                float _FillLevel;
                float _BottomLevel;
                float4 _FoamColor;
                float _FoamThickness;
                float _FoamStrength;
                float _DepthTint;
                float _WobbleAmount;
                float _WobbleSpeed;
                float _WobbleSeed;
                float _LeanX;
                float _LeanZ;
                float _LeanCenterX;
                float _LeanCenterZ;
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
                float wobbledFillLevel : TEXCOORD4;
            };

            float ComputeWobbledFillLevel(float3 worldPos)
            {
                float t = _Time.y * _WobbleSpeed;
                float w1 = sin(t + (worldPos.x * 4.2) + _WobbleSeed) * 0.55;
                float w2 = sin((t * 1.37) + (worldPos.z * 5.6) + (_WobbleSeed * 1.7)) * 0.45;
                float w3 = cos((t * 2.11) + ((worldPos.x + worldPos.z) * 3.1) + (_WobbleSeed * 2.3)) * 0.30;
                float wobble = (w1 + w2 + w3) * _WobbleAmount;

                float lean =
                    (_LeanX * (worldPos.x - _LeanCenterX)) +
                    (_LeanZ * (worldPos.z - _LeanCenterZ));

                return _FillLevel + wobble + lean;
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

                float3 worldPos;
                float3 worldNormal;

                if (IN.uv.y > 0.99)
                {
                    float3 glassCenterWS = mul(unity_ObjectToWorld, float4(0, 0, 0, 1)).xyz;
                    float discRadius = sqrt((IN.positionOS.x * IN.positionOS.x) + (IN.positionOS.z * IN.positionOS.z));
                    float discAngle = atan2(IN.positionOS.z, IN.positionOS.x);

                    float fl = ComputeWobbledFillLevel(float3(glassCenterWS.x, 0, glassCenterWS.z));

                    worldPos = float3(
                        glassCenterWS.x + (cos(discAngle) * discRadius),
                        fl,
                        glassCenterWS.z + (sin(discAngle) * discRadius));

                    worldNormal = float3(0, 1, 0);
                }
                else
                {
                    worldPos = TransformObjectToWorld(IN.positionOS.xyz);
                    worldNormal = TransformObjectToWorldNormal(IN.normalOS);
                }

                OUT.positionHCS = TransformWorldToHClip(worldPos);
                OUT.worldPos = worldPos;
                OUT.worldNormal = worldNormal;
                OUT.uv = IN.uv;
                OUT.fogCoord = ComputeFogFactor(OUT.positionHCS.z);
                OUT.wobbledFillLevel = ComputeWobbledFillLevel(worldPos);
                return OUT;
            }

            half4 Frag(Varyings IN) : SV_Target
            {
                float fl = IN.wobbledFillLevel;

                float aboveSurface = IN.worldPos.y - fl;
                if ((IN.uv.y < 0.99) && (aboveSurface > 0.001))
                {
                    discard;
                }

                if (IN.worldPos.y < _BottomLevel - 0.05)
                {
                    discard;
                }

                float colorLookupY = (IN.uv.y > 0.99) ? (fl - 0.0005) : IN.worldPos.y;
                float3 baseColor = GetLayerColor(colorLookupY);

                float depthBelow = max(0, fl - IN.worldPos.y);
                float depthFactor = saturate(depthBelow / 0.85);
                baseColor *= lerp(1.05, 1.0 - _DepthTint, depthFactor);

                float distToSurface = abs(fl - IN.worldPos.y);
                float foamMask = saturate(1.0 - (distToSurface / max(0.001, _FoamThickness)));
                foamMask = pow(foamMask, 1.7);
                baseColor = lerp(baseColor, _FoamColor.rgb, foamMask * _FoamStrength);

                if (IN.uv.y > 0.99)
                {
                    float3 capColor = lerp(baseColor, _FoamColor.rgb, 0.18);
                    capColor *= 1.04;
                    baseColor = capColor;
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
