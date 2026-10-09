// Calor.shader — el calor de los cuerpos para la vision termica de una criatura (las fosetas de la
// serpiente de Basiliskos). Se dibuja como material de sustitucion en un pase propio de HDRP
// (DrawRenderersCustomPass, despues del posproceso): cada cuerpo caliente se ve a traves de todo
// (ZTest Always), con el borde mas brillante (como el aire caliente que lo rodea) y un latido lento.
// Sin exposicion: va en los valores de pantalla.
Shader "Guardianes/Calor"
{
    Properties
    {
        _Color("Color del calor", Color) = (1, 0.45, 0.12, 1)
        _Intensidad("Intensidad", Float) = 0.75
        _Borde("Brillo del borde", Float) = 1.6
    }

    SubShader
    {
        Tags { "RenderPipeline" = "HDRenderPipeline" "RenderType" = "Transparent" "Queue" = "Transparent" }

        Pass
        {
            Name "ForwardOnly"
            Tags { "LightMode" = "ForwardOnly" }
            Blend One One
            ZWrite Off
            ZTest Always
            Cull Back

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            // El GPU Resident Drawer (BatchRendererGroup) dibuja los MeshRenderer: hace falta la variante DOTS.
            #pragma multi_compile _ DOTS_INSTANCING_ON

            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"

            // Las propiedades en UnityPerMaterial: el GPU Resident Drawer exige el SRP Batcher.
            CBUFFER_START(UnityPerMaterial)
            float4 _Color;
            float _Intensidad;
            float _Borde;
            CBUFFER_END

            struct Atributos
            {
                float3 posicion : POSITION;
                float3 normal : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 posCS : SV_POSITION;
                float3 normalWS : TEXCOORD0;
                float3 posRWS : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Atributos i)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(i);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                float3 rws = TransformObjectToWorld(i.posicion);
                o.posCS = TransformWorldToHClip(rws);
                o.posRWS = rws;
                o.normalWS = TransformObjectToWorldNormal(i.normal);
                return o;
            }

            float4 Frag(Varyings i) : SV_Target
            {
                float3 V = normalize(-i.posRWS);
                float3 n = normalize(i.normalWS);
                float borde = pow(1.0 - saturate(abs(dot(n, V))), 2.0);
                float latido = 0.9 + 0.1 * sin(_Time.y * 3.0);
                float3 c = _Color.rgb * _Intensidad * (0.35 + borde * _Borde) * latido;
                return float4(c, 1.0);
            }
            ENDHLSL
        }
    }
}
