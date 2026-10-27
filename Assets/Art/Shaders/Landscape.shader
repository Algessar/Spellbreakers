Shader "Spellbreakers/HeightBlendTerrain"
{
    Properties
    {
        [MainTexture] _GroundTex("Ground", 2D) = "white" {}
        _RockTex("Rock", 2D) = "white" {}
        _SnowTex("Snow", 2D) = "white" {}

        _TextureTiling("Texture Tiling", Float) = 1.0

        _RockHeight("Rock Height", Float) = 5.0
        _RockBlend("Rock Blend", Float) = 1.0

        _SnowHeight("Snow Height", Float) = 10.0
        _SnowBlend("Snow Blend", Float) = 1.0
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Geometry"
        }

        Pass
        {
            Name "ForwardLit"
            Tags
            {
                "LightMode" = "UniversalForward"
            }

            HLSLPROGRAM

            #pragma vertex vert
            #pragma fragment frag

            #pragma multi_compile_fragment _ _MAIN_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fragment _ _SHADOWS_SOFT

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                float2 uv         : TEXCOORD2;
            };

            TEXTURE2D(_GroundTex);
            SAMPLER(sampler_GroundTex);

            TEXTURE2D(_RockTex);
            SAMPLER(sampler_RockTex);

            TEXTURE2D(_SnowTex);
            SAMPLER(sampler_SnowTex);

            CBUFFER_START(UnityPerMaterial)

                float _TextureTiling;

                float _RockHeight;
                float _RockBlend;

                float _SnowHeight;
                float _SnowBlend;

            CBUFFER_END

            Varyings vert(Attributes input)
            {
                Varyings output;

                VertexPositionInputs positionInputs =
                    GetVertexPositionInputs(input.positionOS.xyz);

                VertexNormalInputs normalInputs =
                    GetVertexNormalInputs(input.normalOS);

                output.positionCS = positionInputs.positionCS;
                output.positionWS = positionInputs.positionWS;
                output.normalWS = normalInputs.normalWS;
                output.uv = input.uv * _TextureTiling;

                return output;
            }

            float3 GetTerrainAlbedo(Varyings input)
            {
                float height = TransformWorldToObject(input.positionWS).z;

                float rockWeight = smoothstep(
                    _RockHeight - _RockBlend,
                    _RockHeight + _RockBlend,
                    height);

                float snowWeight = smoothstep(
                    _SnowHeight - _SnowBlend,
                    _SnowHeight + _SnowBlend,
                    height);

                float3 ground =
                    SAMPLE_TEXTURE2D(
                        _GroundTex,
                        sampler_GroundTex,
                        input.uv).rgb;

                float3 rock =
                    SAMPLE_TEXTURE2D(
                        _RockTex,
                        sampler_RockTex,
                        input.uv).rgb;

                float3 snow =
                    SAMPLE_TEXTURE2D(
                        _SnowTex,
                        sampler_SnowTex,
                        input.uv).rgb;

                float3 groundRock = lerp(
                    ground,
                    rock,
                    rockWeight);

                return lerp(
                    groundRock,
                    snow,
                    snowWeight);
            }

            half4 frag(Varyings input) : SV_Target
            {
                float3 albedo = GetTerrainAlbedo(input);

                InputData inputData = (InputData)0;

                inputData.positionWS = input.positionWS;
                inputData.normalWS = normalize(input.normalWS);
                inputData.viewDirectionWS =
                    GetWorldSpaceNormalizeViewDir(input.positionWS);

                inputData.normalizedScreenSpaceUV =
                    GetNormalizedScreenSpaceUV(input.positionCS);

                SurfaceData surfaceData = (SurfaceData)0;

                surfaceData.albedo = albedo;
                surfaceData.alpha = 1.0;
                surfaceData.metallic = 0.0;
                surfaceData.smoothness = 0.0;
                surfaceData.normalTS = float3(0.0, 0.0, 1.0);
                surfaceData.occlusion = 1.0;
                surfaceData.emission = 0.0;

                return UniversalFragmentPBR(
                    inputData,
                    surfaceData);
            }

            ENDHLSL
        }

        UsePass "Universal Render Pipeline/Lit/ShadowCaster"
        UsePass "Universal Render Pipeline/Lit/DepthOnly"
    }
}