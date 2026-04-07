Shader "PSX/URP_LitCutout"
{
    Properties
    {
        [Header(Base Material)]
        _Color("Color (RGBA)", Color) = (1, 1, 1, 1)
        _MainTex("Albedo Texture", 2D) = "white" {}
        _Cutoff("Alpha cutoff", Range(0,1)) = 0.1
        
        [Header(PBR Maps)]
        _MetallicTex("Metallic Map (R)", 2D) = "white" {}
        _Metallic("Metallic Multiplier", Range(0, 1)) = 0.0
        
        _RoughnessTex("Roughness Map (R)", 2D) = "white" {}
        _Roughness("Roughness Multiplier", Range(0, 1)) = 0.5
        
        [Header(Surface Detail)]
        _HeightMap("Height Map (R)", 2D) = "black" {}
        _HeightAmplitude("Height Amplitude", Range(0, 0.1)) = 0.02
        
        [Header(Emission and HDR Support)]
        [HDR] _EmissionColor("Emission Color (HDR)", Color) = (0,0,0,0)
        _EmissiveTex("Emissive Texture", 2D) = "black" {}

        [Header(PSX Effects)]
        [Toggle(PSX_FLAT_SHADING)] _FlatShading("Enable Flat Shading", Float) = 0
        [Toggle(CUSTOM_VERTEX_GRID)] _EnableCustomGrid("Enable Custom Vertex Grid", Float) = 0
        _CustomGridSize("Custom Grid Size", Float) = 100
    }
    
    SubShader
    {
        Tags 
        { 
            "RenderType" = "TransparentCutout" 
            "RenderPipeline" = "UniversalPipeline" 
            "Queue" = "AlphaTest" 
        }
        
        LOD 100
        ZWrite On
        Cull Off

        Pass
        {
            Name "UniversalForward"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma target 4.5
            #pragma require geometry 
            #pragma vertex vert
            #pragma geometry geom
            #pragma fragment frag
            
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _FORWARD_PLUS
            #pragma multi_compile _ _ADDITIONAL_LIGHT_SHADOWS
            
            #pragma multi_compile_fog 
            #pragma shader_feature PSX_FLAT_SHADING
            #pragma shader_feature CUSTOM_VERTEX_GRID
            
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            float _PSX_GridSize;
            float _PSX_TextureWarpingFactor;
            float _PSX_TextureWarpingMode;
            float _PSX_VertexWobbleMode;
            float _PSX_LightingNormalFactor;
            float _PSX_ObjectDithering;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                float3 normalOS : NORMAL;
                float4 tangentOS : TANGENT; // Extracción de la tangente del modelo 3D
            };

            struct v2g
            {
                float4 positionOS : TEXCOORD3;
                float2 uv : TEXCOORD0;
                float3 normalWS : NORMAL;
                float4 tangentWS : TEXCOORD4;
            };

            struct g2f
            {
                float4 positionCS : SV_POSITION;
                float3 affineUV : TEXCOORD0;
                float fogFactor : TEXCOORD1;
                float3 normalWS : NORMAL;
                float3 viewDirWS : TEXCOORD2;
                float3 positionWS : TEXCOORD3;
                float3 tangentWS : TEXCOORD4;
                float3 bitangentWS : TEXCOORD5;
            };

            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            TEXTURE2D(_EmissiveTex); 
            TEXTURE2D(_MetallicTex);
            TEXTURE2D(_RoughnessTex);
            TEXTURE2D(_HeightMap);
            
            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float4 _Color;
                float4 _EmissionColor; 
                float _Cutoff;
                float _CustomGridSize;
                half _Metallic;
                half _Roughness;
                float _HeightAmplitude;
            CBUFFER_END

            float3 SnapVertexToGrid(float3 vertex)
            {
                float gridRes = _PSX_GridSize;
                #if defined(CUSTOM_VERTEX_GRID)
                gridRes = _CustomGridSize;
                #endif
                return gridRes < 0.00001f ? vertex : (floor(vertex * gridRes) / gridRes);
            }

            int PSX_GetDitherOffset(int2 pixelPosition)
            {
                const int ditheringMatrix4x4[16] = { -4, +0, -3, +1, +2, -2, +3, -1, -3, +1, -4, +0, +3, -1, +2, -2 };
                return ditheringMatrix4x4[pixelPosition.x % 4 + (pixelPosition.y % 4) * 4];
            }

            half4 PSX_DitherColor(half4 color, int2 pixelPosition)
            {
                int4 col255 = round(color * 255.0);
                col255 = (col255 + PSX_GetDitherOffset(pixelPosition.xy)) >> 3;
                return half4(col255) / 31.0h;
            }

            v2g vert(Attributes input)
            {
                v2g output;
                output.positionOS = input.positionOS;
                output.uv = TRANSFORM_TEX(input.uv, _MainTex);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                
                // Transformación del vector Tangente al espacio de mundo
                output.tangentWS = float4(TransformObjectToWorldDir(input.tangentOS.xyz), input.tangentOS.w);
                return output;
            }

            [maxvertexcount(3)]
            void geom(triangle v2g IN[3], inout TriangleStream<g2f> triStream)
            {
                g2f o[3];
                float3 flatNormalWS = IN[0].normalWS;
                
                #ifdef PSX_FLAT_SHADING
                float3 p0 = TransformObjectToWorld(IN[0].positionOS.xyz);
                float3 p1 = TransformObjectToWorld(IN[1].positionOS.xyz);
                float3 p2 = TransformObjectToWorld(IN[2].positionOS.xyz);
                flatNormalWS = normalize(cross(p1 - p0, p2 - p0));
                #endif

                for (int i = 0; i < 3; i++)
                {
                    float3 worldPos = TransformObjectToWorld(IN[i].positionOS.xyz);
                    float3 viewPos = TransformWorldToView(worldPos);
                    float4 clipPos = mul(GetViewToHClipMatrix(), float4(viewPos, 1.0));

                    float3 snappedViewPos = SnapVertexToGrid(viewPos);
                    float4 viewSnappedClip = mul(GetViewToHClipMatrix(), float4(snappedViewPos, 1.0));
                    float4 clipSnappedClip = clipPos;
                    clipSnappedClip.xy = SnapVertexToGrid(clipPos.xyz).xy; 
                    
                    o[i].positionCS = lerp(viewSnappedClip, clipSnappedClip, _PSX_VertexWobbleMode);
                    o[i].fogFactor = ComputeFogFactor(o[i].positionCS.z);
                    o[i].positionWS = worldPos;
                    
                    o[i].normalWS = normalize(flatNormalWS);
                    o[i].viewDirWS = GetWorldSpaceNormalizeViewDir(worldPos);

                    // Cálculo matemático de la Bitangente para la matriz TBN
                    float3 tangentWS = normalize(IN[i].tangentWS.xyz);
                    float tangentSign = IN[i].tangentWS.w * unity_WorldTransformParams.w;
                    o[i].tangentWS = tangentWS;
                    o[i].bitangentWS = cross(o[i].normalWS, tangentWS) * tangentSign;

                    float affineFactor = _PSX_TextureWarpingMode < 0.5f ? length(viewPos) : max(clipPos.w, 0.1);
                    affineFactor = lerp(1.0, affineFactor, _PSX_TextureWarpingFactor);
                    o[i].affineUV = float3(IN[i].uv * affineFactor, affineFactor);
                    
                    triStream.Append(o[i]);
                }
                triStream.RestartStrip();
            }

            half4 frag(g2f input, float4 screenPos : SV_POSITION) : SV_Target
            {
                float2 uv = input.affineUV.xy / input.affineUV.z;
                
                // --- MATEMÁTICA DE PARALLAX (HEIGHT MAP) ---
                // 1. Construimos la matriz TBN (Tangent-Bitangent-Normal)
                float3x3 tangentSpaceTransform = float3x3(input.tangentWS, input.bitangentWS, input.normalWS);
                // 2. Transformamos la dirección de la cámara al espacio de la textura
                float3 viewDirTS = mul(tangentSpaceTransform, input.viewDirWS);
                // 3. Leemos el mapa de altura
                float height = SAMPLE_TEXTURE2D(_HeightMap, sampler_MainTex, uv).r;
                // 4. Calculamos el desplazamiento visual (añadimos +0.42 al divisor para estabilizar ángulos extremos)
                float2 parallaxOffset = (viewDirTS.xy / (viewDirTS.z + 0.42)) * (height * _HeightAmplitude);
                // 5. Aplicamos la distorsión a las coordenadas UV maestras
                uv += parallaxOffset;
                
                // Leemos las texturas usando el UV modificado por el Parallax y el Affine Mapping
                half4 albedo = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv) * _Color;
                clip(albedo.a - _Cutoff);

                // --- MATEMÁTICA PBR MODULAR ---
                half metallicMap = SAMPLE_TEXTURE2D(_MetallicTex, sampler_MainTex, uv).r;
                half roughnessMap = SAMPLE_TEXTURE2D(_RoughnessTex, sampler_MainTex, uv).r;

                half metallic = metallicMap * _Metallic;
                half smoothness = 1.0h - (roughnessMap * _Roughness); 
                
                BRDFData brdfData;
                InitializeBRDFData(albedo.rgb, metallic, half3(0.04, 0.04, 0.04), smoothness, albedo.a, brdfData);

                #if defined(_MAIN_LIGHT_SHADOWS) || defined(_MAIN_LIGHT_SHADOWS_CASCADE)
                    float4 shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                #else
                    float4 shadowCoord = float4(0, 0, 0, 0);
                #endif
                
                Light mainLight = GetMainLight(shadowCoord);
                float3 retroNormalMain = normalize(lerp(mainLight.direction, input.normalWS, _PSX_LightingNormalFactor));
                half3 LitColor = LightingPhysicallyBased(brdfData, mainLight, retroNormalMain, input.viewDirWS);

                #if defined(_ADDITIONAL_LIGHTS) || defined(_FORWARD_PLUS)
                    uint pixelLightCount = GetAdditionalLightsCount();
                    for (uint lightIndex = 0; lightIndex < pixelLightCount; ++lightIndex)
                    {
                        Light additionalLight = GetAdditionalLight(lightIndex, input.positionWS);
                        float3 retroNormalAdd = normalize(lerp(additionalLight.direction, input.normalWS, _PSX_LightingNormalFactor));
                        LitColor += LightingPhysicallyBased(brdfData, additionalLight, retroNormalAdd, input.viewDirWS);
                    }
                #endif

                LitColor += albedo.rgb * half3(0.1, 0.1, 0.1); 

                half3 emissive = SAMPLE_TEXTURE2D(_EmissiveTex, sampler_MainTex, uv).rgb * _EmissionColor.rgb;
                half3 finalColor = LitColor + emissive;

                finalColor.rgb = MixFog(finalColor.rgb, input.fogFactor);
                half4 finalColorWithAlpha = half4(finalColor, albedo.a);
                
                if (_PSX_ObjectDithering > 0.5)
                {
                    finalColorWithAlpha = PSX_DitherColor(finalColorWithAlpha, int2(screenPos.xy));
                }

                return finalColorWithAlpha;
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            
            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float _Cutoff;
            CBUFFER_END

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = TRANSFORM_TEX(input.uv, _MainTex);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                half4 albedo = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);
                clip(albedo.a - _Cutoff);
                return 0;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask 0

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            
            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float _Cutoff;
            CBUFFER_END

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = TRANSFORM_TEX(input.uv, _MainTex);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                half4 albedo = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);
                clip(albedo.a - _Cutoff);
                return 0;
            }
            ENDHLSL
        }
    }
    Fallback "Hidden/Universal Render Pipeline/Unlit"
}