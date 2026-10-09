// Cascada.shader — lamina de agua que cae (HDRP, transparente, ForwardOnly).
// Dos capas de espuma (chorros verticales) que corren hacia abajo a distinta velocidad sobre un
// agua verde clara; iluminada por el sol y el cielo de HDRP como Particula.shader (difuso con
// dispersion hacia delante: a contraluz la espuma brilla), mas un brillo especular suave.
// El color de vertice: a = opacidad (los bordes de la lamina se desvanecen), r = velocidad.
// Se funde con la roca por profundidad (particula suave) para no mostrar el corte con el terreno.
Shader "Guardianes/Cascada"
{
    Properties
    {
        _MainTex("Espuma (RGB color, A densidad)", 2D) = "white" {}
        _ColorAgua("Color del agua", Color) = (0.42, 0.62, 0.55, 0.55)
        _Velocidad("Velocidad de caida (repeticiones/s)", Float) = 1.1
        _Suavidad("Fundido con la escena (m)", Float) = 2
        _Brillo("Brillo de la espuma", Float) = 1.25
    }

    SubShader
    {
        Tags { "RenderPipeline" = "HDRenderPipeline" "RenderType" = "Transparent" "Queue" = "Transparent" }

        Pass
        {
            Name "ForwardOnly"
            Tags { "LightMode" = "ForwardOnly" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/SphericalHarmonics.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            float4 _MainTex_ST;
            float4 _ColorAgua;
            float _Velocidad;
            float _Suavidad;
            float _Brillo;

            struct Atributos
            {
                float3 posicion : POSITION;
                float3 normal : NORMAL;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 posCS : SV_POSITION;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
                float3 posRWS : TEXCOORD1;
                float3 normalWS : TEXCOORD2;
            };

            Varyings Vert(Atributos i)
            {
                Varyings o;
                float3 rws = TransformObjectToWorld(i.posicion);
                o.posCS = TransformWorldToHClip(rws);
                o.posRWS = rws;
                o.normalWS = TransformObjectToWorldNormal(i.normal);
                o.color = i.color;
                o.uv = i.uv * _MainTex_ST.xy + _MainTex_ST.zw;
                return o;
            }

            float3 Luz(float3 posRWS, float3 n)
            {
                float3 V = normalize(-posRWS);
                float3 cielo = max(SampleSH9(_AmbientProbeData, n), 0) * 0.6 + max(SampleSH9(_AmbientProbeData, float3(0, 1, 0)), 0) * 0.4;
                float3 sol = 0;
                if (_DirectionalLightCount > 0)
                {
                    int k = _DirectionalShadowIndex >= 0 ? _DirectionalShadowIndex : 0;
                    DirectionalLightData l = _DirectionalLightDatas[k];
                    float3 L = -l.forward;
                    float envoltura = saturate(dot(n, L) * 0.5 + 0.5);
                    float g = 0.55, c = dot(-V, L);
                    float hg = (1 - g * g) / pow(abs(1 + g * g - 2 * g * c), 1.5) * 0.25;
                    sol = l.color * (envoltura * 0.85 + hg * 0.6) * INV_PI;
                }
                return cielo + sol;
            }

            float4 Frag(Varyings i, bool delante : SV_IsFrontFace) : SV_Target
            {
                float t = _TimeParameters.x;
                float velocidad = 0.6 + i.color.r;
                float a1 = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv + float2(0.0, t * _Velocidad * velocidad)).a;
                float a2 = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv * float2(1.7, 1.3) + float2(0.37, t * _Velocidad * velocidad * 1.6)).a;
                float espuma = saturate(a1 * 0.65 + a2 * 0.55);
                float3 n = normalize(delante ? i.normalWS : -i.normalWS);
                float3 luz = Luz(i.posRWS, n) * GetCurrentExposureMultiplier();
                float3 color = lerp(_ColorAgua.rgb, float3(0.95, 0.97, 1.0) * _Brillo, espuma);
                float alfa = saturate(lerp(_ColorAgua.a, 1.0, espuma)) * i.color.a;
                // Particula suave contra la roca y el agua del pie.
                float zEscena = LinearEyeDepth(LoadCameraDepth(uint2(i.posCS.xy)), _ZBufferParams);
                float zAqui = -TransformWorldToView(i.posRWS).z;
                alfa *= saturate((zEscena - zAqui) / max(_Suavidad, 1e-3));
                return float4(color * luz, alfa);
            }
            ENDHLSL
        }
    }
}
