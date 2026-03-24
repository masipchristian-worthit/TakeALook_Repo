Shader "PSX/URP_LitCutout"
{
    Properties
    {
        _Color("Color (RGBA)", Color) = (1, 1, 1, 1)
        _MainTex("Texture", 2D) = "white" {}
        _EmissionColor("Emission Color (RGBA)", Color) = (0,0,0,0)
        _EmissiveTex("Emissive", 2D) = "black" {}
        _Cutoff("Alpha cutoff", Range(0,1)) = 0.1
        
        [Toggle(PSX_FLAT_SHADING)] _FlatShading("Enable Flat Shading", Float) = 0
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
            #pragma target 4.5 // Requerido para variables de geometría y URP avanzado
            #pragma vertex vert
            #pragma geometry geom
            #pragma fragment frag
            
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma shader_feature PSX_FLAT_SHADING
            
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            // Variables Globales Inyectadas por PSXShaderManager.cs
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
            };

            struct v2g
            {
                float4 positionCS : SV_POSITION;
                float4 positionOS : TEXCOORD3;
                float2 uv : TEXCOORD0;
                float3 normalWS : NORMAL;
            };

            struct g2f
            {
                float4 positionCS : SV_POSITION;
                float3 affineUV : TEXCOORD0;
                float3 normalWS : NORMAL;
                float3 viewDirWS : TEXCOORD1;
                float4 colorLight : COLOR; // Almacena la luz precalculada (Vertex Lit / Flat)
            };

            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            TEXTURE2D(_EmissiveTex); SAMPLER(sampler_EmissiveTex);
            
            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float4 _Color;
                float4 _EmissionColor;
                float _Cutoff;
            CBUFFER_END

            // --- Utilidades Matemáticas PSX ---

            float3 SnapVertexToGrid(float3 vertex)
            {
                return _PSX_GridSize < 0.00001f ? vertex : (floor(vertex * _PSX_GridSize) / _PSX_GridSize);
            }

            int PSX_GetDitherOffset(int2 pixelPosition)
            {
                const int ditheringMatrix4x4[16] =
                {
                    -4, +0, -3, +1,
                    +2, -2, +3, -1,
                    -3, +1, -4, +0,
                    +3, -1, +2, -2
                };
                return ditheringMatrix4x4[pixelPosition.x % 4 + (pixelPosition.y % 4) * 4];
            }

            half4 PSX_DitherColor(half4 color, int2 pixelPosition)
            {
                int4 col255 = round(color * 255.0);
                col255 = (col255 + PSX_GetDitherOffset(pixelPosition.xy)) >> 3;
                return half4(col255) / 31.0h;
            }

            // --- Vertex Shader ---
            v2g vert(Attributes input)
            {
                v2g output;
                output.positionOS = input.positionOS;
                output.uv = TRANSFORM_TEX(input.uv, _MainTex);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                return output;
            }

            // --- Geometry Shader (Maneja el Flat Shading y el Snapping) ---
            [maxvertexcount(3)]
            void geom(triangle v2g IN[3], inout TriangleStream<g2f> triStream)
            {
                g2f o[3];
                float3 flatNormalWS = IN[0].normalWS;
                
                #ifdef PSX_FLAT_SHADING
                // Cálculo de luz plana en el centro del triángulo
                float3 p0 = TransformObjectToWorld(IN[0].positionOS.xyz);
                float3 p1 = TransformObjectToWorld(IN[1].positionOS.xyz);
                float3 p2 = TransformObjectToWorld(IN[2].positionOS.xyz);
                flatNormalWS = normalize(cross(p1 - p0, p2 - p0));
                #endif

                for (int i = 0; i < 3; i++)
                {
                    float4 clipPos = IN[i].positionCS;
                    
                    // Cálculo de Iluminación URP (Vertex Level)
                    Light mainLight = GetMainLight();
                    float3 normalToUse = normalize(flatNormalWS);
                    float NdotL = saturate(dot(normalToUse, mainLight.direction));
                    NdotL = lerp(1.0, NdotL, _PSX_LightingNormalFactor); // Aplicación del factor retro
                    o[i].colorLight = half4(mainLight.color * NdotL, 1.0);
                    o[i].colorLight.rgb += half3(0.1, 0.1, 0.1); // Ambiente base estático

                    // Affine Texture Mapping
                    float affineFactor = _PSX_TextureWarpingMode < 0.5f ? length(TransformObjectToViewPos(IN[i].positionOS.xyz)) : max(clipPos.w, 0.1);
                    affineFactor = lerp(1.0, affineFactor, _PSX_TextureWarpingFactor);
                    o[i].affineUV = float3(IN[i].uv * affineFactor, affineFactor);

                    // Vertex Snapping (Sustituye a la matriz legada matrix_p)
                    float4 snappedClip = clipPos;
                    snappedClip.xy = SnapVertexToGrid(clipPos.xyz).xy; // Simplificación para Clip Space nativo
                    
                    o[i].positionCS = lerp(clipPos, snappedClip, _PSX_VertexWobbleMode);
                    o[i].normalWS = normalToUse;
                    o[i].viewDirWS = GetWorldSpaceNormalizeViewDir(TransformObjectToWorld(IN[i].positionOS.xyz));
                    
                    triStream.Append(o[i]);
                }
                triStream.RestartStrip();
            }

            // --- Fragment Shader ---
            half4 frag(g2f input, float4 screenPos : SV_POSITION) : SV_Target
            {
                // Resolución del mapeo afín
                float2 uv = input.affineUV.xy / input.affineUV.z;
                
                half4 albedo = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv) * _Color;
                
                clip(albedo.a - _Cutoff);

                // Multiplicación por la iluminación calculada en vértices
                half4 finalColor = albedo * input.colorLight;

                // Emisivo
                half4 emissive = SAMPLE_TEXTURE2D(_EmissiveTex, sampler_EmissiveTex, uv) * _EmissionColor;
                finalColor.rgb += emissive.rgb;

                // Aplicación de Dithering Per-Object basado en la matriz 4x4 PS1
                if (_PSX_ObjectDithering > 0.5)
                {
                    finalColor = PSX_DitherColor(finalColor, int2(screenPos.xy));
                }

                return finalColor;
            }
            ENDHLSL
        }
    }
    Fallback "Hidden/Universal Render Pipeline/Unlit"
}