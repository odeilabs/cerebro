// Particula.shader — particulas para HDRP (polvo, humo, fuego, chispas, fogonazos, trazadoras).
// Color de vertice (el del ParticleSystem), particulas suaves contra la escena (profundidad de
// camara) y dos tipos de luz:
//   · Emisivas (fuego, chispas, fogonazos): brillan en nits con la exposicion de la camara.
//   · Iluminadas (polvo, humo): toman la luz del sol y del cielo de HDRP (sonda ambiente y la
//     luz direccional principal, con dispersion hacia delante: el humo a contraluz se ilumina).
// Mezcla alfa o aditiva segun _Origen/_Destino.
Shader "Guardianes/Particula"
{
    Properties
    {
        _MainTex("Textura", 2D) = "white" {}
        _Color("Color", Color) = (1, 1, 1, 1)
        _Intensidad("Intensidad de la emision (nits)", Float) = 1
        _Emisiva("Emisiva (0 = iluminada por el sol y el cielo)", Range(0, 1)) = 0
        _Suavidad("Distancia de fundido con la escena (m)", Float) = 1.5
        _Dispersion("Dispersion a contraluz", Range(0, 1)) = 0.5
        [Enum(UnityEngine.Rendering.BlendMode)] _Origen("Origen", Float) = 5      // SrcAlpha
        [Enum(UnityEngine.Rendering.BlendMode)] _Destino("Destino", Float) = 10   // OneMinusSrcAlpha
    }

    SubShader
    {
        Tags { "RenderPipeline" = "HDRenderPipeline" "RenderType" = "Transparent" "Queue" = "Transparent" }

        Pass
        {
            Name "ForwardOnly"
            Tags { "LightMode" = "ForwardOnly" }
            Blend [_Origen] [_Destino]
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/SphericalHarmonics.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            float4 _MainTex_ST;
            float4 _Color;
            float _Intensidad;
            float _Emisiva;
            float _Suavidad;
            float _Dispersion;

            struct Atributos
            {
                float3 posicion : POSITION;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 posCS : SV_POSITION;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
                float3 posRWS : TEXCOORD1;
                float3 luz : TEXCOORD2;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            // Luz que recibe una nube de particulas (en lux/pi, antes de la exposicion).
            float3 LuzDifusa(float3 posRWS)
            {
                float3 V = normalize(-posRWS);                    // hacia la camara (relativo a camara)
                float3 n = normalize(V * 0.6 + float3(0, 0.8, 0)); // la nube "mira" a la camara y al cielo
                float3 cielo = max(SampleSH9(_AmbientProbeData, n), 0) * 0.7 + max(SampleSH9(_AmbientProbeData, float3(0, 1, 0)), 0) * 0.3;
                float3 sol = 0;
                if (_DirectionalLightCount > 0)
                {
                    int i = _DirectionalShadowIndex >= 0 ? _DirectionalShadowIndex : 0;
                    DirectionalLightData l = _DirectionalLightDatas[i];
                    float3 L = -l.forward;
                    float envoltura = saturate(dot(n, L) * 0.5 + 0.5);
                    // Dispersion hacia delante (Henyey-Greenstein g = 0.6) mirando hacia el sol.
                    float g = 0.6, c = dot(-V, L);
                    float hg = (1 - g * g) / pow(abs(1 + g * g - 2 * g * c), 1.5) * 0.25;
                    sol = l.color * (envoltura * 0.8 + hg * _Dispersion) * INV_PI;
                }
                return cielo + sol;
            }

            Varyings Vert(Atributos i)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(i);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                float3 rws = TransformObjectToWorld(i.posicion);
                o.posCS = TransformWorldToHClip(rws);
                o.posRWS = rws;
                o.color = i.color * _Color;
                o.uv = i.uv * _MainTex_ST.xy + _MainTex_ST.zw;
                float exposicion = GetCurrentExposureMultiplier();
                o.luz = _Emisiva > 0.5 ? _Intensidad * exposicion : LuzDifusa(rws) * exposicion;
                return o;
            }

            float4 Frag(Varyings i) : SV_Target
            {
                float4 c = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv) * i.color;
                // Particula suave: se funde donde corta la geometria de la escena.
                float zEscena = LinearEyeDepth(LoadCameraDepth(uint2(i.posCS.xy)), _ZBufferParams);
                float zParticula = -TransformWorldToView(i.posRWS).z;
                c.a *= saturate((zEscena - zParticula) / max(_Suavidad, 1e-3));
                // Y se apaga pegada a la camara (no tapa la pantalla al atravesarla).
                c.a *= saturate((zParticula - 0.5) / 3.0);
                c.rgb *= i.luz;
                return c;
            }
            ENDHLSL
        }
    }
}
