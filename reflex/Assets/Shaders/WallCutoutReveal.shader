Shader "Reflex/WallCutoutReveal"
{
    Properties
    {
        _BaseMap ("Base Map", 2D) = "white" {}
        _BaseColor ("Base Color", Color) = (1, 1, 1, 1)
        _CutoutCenterSS ("Cutout Center SS", Vector) = (0, 0, 0, 0)
        _CutoutRadius ("Cutout Radius", Float) = 96
        _CutoutVerticalScale ("Cutout Vertical Scale", Float) = 1.35
        _CutoutSoftness ("Cutout Softness", Float) = 24
        _CutoutActive ("Cutout Active", Range(0, 1)) = 0
        _EdgeDitherSize ("Edge Dither Size", Float) = 3
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "TransparentCutout"
            "Queue" = "AlphaTest"
            "RenderPipeline" = "UniversalPipeline"
        }

        Cull Back
        ZWrite On
        ZTest LEqual
        Blend Off

        Pass
        {
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 normalWS : TEXCOORD0;
                float2 uv : TEXCOORD1;
                float4 screenPos : TEXCOORD2;
            };

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor;
                float4 _CutoutCenterSS;
                half _CutoutRadius;
                half _CutoutVerticalScale;
                half _CutoutSoftness;
                half _CutoutActive;
                half _EdgeDitherSize;
            CBUFFER_END

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionHCS = TransformObjectToHClip(input.positionOS.xyz);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                output.screenPos = ComputeScreenPos(output.positionHCS);
                return output;
            }

            half BayerThreshold(float2 screenPosition, half ditherSize)
            {
                uint2 cell = (uint2)floor(screenPosition / max((float)ditherSize, 1.0));
                cell = cell & uint2(3u, 3u);
                uint index = cell.x + cell.y * 4u;

                half threshold = 0.0;
                threshold = index == 0u ? 0.03125 : threshold;
                threshold = index == 1u ? 0.53125 : threshold;
                threshold = index == 2u ? 0.15625 : threshold;
                threshold = index == 3u ? 0.65625 : threshold;
                threshold = index == 4u ? 0.78125 : threshold;
                threshold = index == 5u ? 0.28125 : threshold;
                threshold = index == 6u ? 0.90625 : threshold;
                threshold = index == 7u ? 0.40625 : threshold;
                threshold = index == 8u ? 0.21875 : threshold;
                threshold = index == 9u ? 0.71875 : threshold;
                threshold = index == 10u ? 0.09375 : threshold;
                threshold = index == 11u ? 0.59375 : threshold;
                threshold = index == 12u ? 0.96875 : threshold;
                threshold = index == 13u ? 0.46875 : threshold;
                threshold = index == 14u ? 0.84375 : threshold;
                threshold = index == 15u ? 0.34375 : threshold;
                return threshold;
            }

            half4 frag(Varyings input) : SV_Target
            {
                half4 color = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv) * _BaseColor;
                clip(color.a - 0.01);

                float2 screenUV = input.screenPos.xy / input.screenPos.w;
                float2 screenPixel = screenUV * _ScreenParams.xy;
                float2 cutoutDelta = screenPixel - _CutoutCenterSS.xy;
                cutoutDelta.y /= max((float)_CutoutVerticalScale, 0.001);

                half active = saturate(_CutoutActive);
                half easedActive = active * active * (3.0 - 2.0 * active);
                half cutoutDistance = length(cutoutDelta);
                half cutoutSoftness = max(_CutoutSoftness, 1.0);
                half revealRadius = lerp(-cutoutSoftness, _CutoutRadius, easedActive);
                half edgeProgress = saturate((cutoutDistance - revealRadius) / cutoutSoftness);
                half clipMask = edgeProgress * edgeProgress * (3.0 - 2.0 * edgeProgress);
                half ditherThreshold = BayerThreshold(input.positionHCS.xy, _EdgeDitherSize);
                clip(clipMask - ditherThreshold);

                half3 normalWS = normalize(input.normalWS);
                Light mainLight = GetMainLight();
                half mainLightAmount = saturate(dot(normalWS, mainLight.direction));
                half3 lighting = SampleSH(normalWS) + (mainLight.color * mainLightAmount);
                return half4(color.rgb * lighting, 1.0);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
