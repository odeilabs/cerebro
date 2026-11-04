// Pelaje.hlsl — pelo por capas (shells) para Guardianes/Pelaje (copia de HDRP/Lit con ganchos).
//
// El mismo SkinnedMeshRenderer de Kong se pinta N veces (un material por capa). Cada capa:
//   _TexWorldScale  fraccion de la capa 0..1 (0 = la piel). El Lit solo lo usa en mapeo
//                   planar/triplanar, que Kong no usa: lo reutilizamos como parametro por material.
//   _BaseColorMap   textura de mechones repetida: alfa = altura del mechon (0..1), RGB = variacion.
//   Con _TexWorldScale < 0 son tarjetas de mechones (MechonesKoba en Gorilla Revenge): cintas ya
//   formadas que solo cuelgan y se mueven con el aire; color de vertice r = lo recorrido del mechon
//   (0 raiz .. 1 punta), g = plata, a = tono.
// Color de vertice (lo pinta Tools/kong/generar_kong.py):
//   r = largo del pelo (0 piel desnuda .. 1), g = plata (silla del espalda plateada), b = piel,
//   a = cavidad de la piel (1 lisa .. 0 el fondo de un pliegue).
// Globales que fija el juego (PelajeKong en King Kong):
//   _GD_PeloLargo      metros de pelo en las zonas de largo 1
//   _GD_PeloGravedad   xyz direccion de caida en mundo, w cuanto cuelgan las puntas
//   _GD_ColorPelo, _GD_ColorPlata, _GD_ColorPiel   (lineales)
//   _GD_PeloViento     xyz empuje del aire/movimiento en mundo (las puntas se van hacia atras)

#ifndef GD_PELAJE_HLSL
#define GD_PELAJE_HLSL

float  _GD_PeloLargo;
float4 _GD_PeloGravedad;
float4 _GD_PeloViento;
float4 _GD_ColorPelo;
float4 _GD_ColorPlata;
float4 _GD_ColorPiel;
float  _GD_PeloBorra;   // cuanto se oscurece el pelo de las capas cuando encima van mechones (0 nada)

#if !defined(SHADER_STAGE_RAY_TRACING) && ((SHADERPASS < SHADERPASS_RAYTRACING) || (SHADERPASS > SHADERPASS_RAY_TRACING_DEBUG))

#define HAVE_MESH_MODIFICATION

AttributesMesh ApplyMeshModification(AttributesMesh input, float3 timeParameters
#ifdef USE_CUSTOMINTERP_SUBSTRUCT
    , inout VaryingsMeshType varyings
#endif
#ifdef HAVE_VFX_MODIFICATION
    , AttributesElement element
#endif
    )
{
    float capa = _TexWorldScale;
    float largo = _GD_PeloLargo;
#ifdef ATTRIBUTES_NEED_COLOR
    largo *= input.color.r;
#endif
#ifdef ATTRIBUTES_NEED_NORMAL
    float3 n = normalize(input.normalOS);
#else
    float3 n = float3(0.0, 1.0, 0.0);
#endif
    // Las puntas cuelgan (gravedad) y se van con el aire: curva cuadratica a lo largo del mechon.
    // Sin normalizar: el aire puede ser cero (normalizarlo daria NaN y Kong desapareceria).
    float3 caida = TransformWorldToObjectDir(_GD_PeloGravedad.xyz, false) * _GD_PeloGravedad.w;
    float3 aire = TransformWorldToObjectDir(_GD_PeloViento.xyz, false);
    float ondula = sin(timeParameters.x * 3.1 + dot(input.positionOS, float3(0.11, 0.07, 0.13))) * 0.08;
#ifdef ATTRIBUTES_NEED_COLOR
    if (capa < 0.0)
    {
        float t = input.color.r;
        input.positionOS += (caida + aire * (1.0 + ondula)) * t * t * _GD_PeloLargo;
        return input;
    }
#endif
    input.positionOS += (n * capa + (caida + aire * (1.0 + ondula)) * capa * capa) * largo;
    return input;
}

#endif // pasadas de rasterizado

// ---------------------------------------------------------------- Ganchos de superficie
#define GD_HOOK_ALFA
#define GD_HOOK_SUPERFICIE

// La capa sobrevive donde el mechon llega hasta ella. La piel (capa 0) es siempre opaca. Donde
// el pelo es corto (largo < 1) las capas exteriores desaparecen antes.
void GD_AjustarAlfa(FragInputs input, inout float alphaValue)
{
    float capa = _TexWorldScale;
    if (capa < 0.0) return; // tarjetas: el alfa de la textura, tal cual
    float largo = 1.0;
#if defined(VARYINGS_NEED_COLOR) || defined(VARYINGS_DS_NEED_COLOR)
    largo = input.color.r;
#endif
    // Prueba del Lit: alphaValue >= _AlphaCutoff. Queremos: altura * largo >= capa.
    float altura = alphaValue;
    alphaValue = capa <= 0.001 ? 1.0 : _AlphaCutoff + (altura * largo - capa) * 4.0;
}

void GD_AjustarSuperficie(FragInputs input, inout SurfaceData surfaceData, float alpha)
{
    float capa = _TexWorldScale;
    float4 m = float4(1.0, 0.0, 0.0, 1.0);
#if defined(VARYINGS_NEED_COLOR) || defined(VARYINGS_DS_NEED_COLOR)
    m = input.color;
#endif
    if (capa < 0.0)
    {
        // Mechon: oscuro en la raiz, donde apenas llega la luz, y algo mas claro en la punta.
        float t = saturate(m.r);
        float3 mechon = lerp(_GD_ColorPelo.rgb, _GD_ColorPlata.rgb, saturate(m.g));
        mechon *= surfaceData.baseColor * 2.0 * lerp(0.6, 1.45, m.a);
        surfaceData.baseColor = mechon * lerp(0.45, 1.25, smoothstep(0.0, 0.85, t));
        surfaceData.ambientOcclusion *= lerp(0.7, 1.0, t);
        surfaceData.specularOcclusion *= lerp(0.5, 1.0, t);
        surfaceData.perceptualSmoothness = 0.3;
        return;
    }
    // Color: piel desnuda, pelo negro-pardo y silla plateada; la textura aporta la variacion de
    // cada mechon (media ~0.5 -> factor ~1).
    float3 pelo = lerp(_GD_ColorPelo.rgb, _GD_ColorPlata.rgb, saturate(m.g)) * (1.0 - _GD_PeloBorra);
    float piel = saturate(m.b) * (1.0 - saturate(capa * 4.0));
    float3 baseColor = lerp(pelo, _GD_ColorPiel.rgb, piel);
    baseColor *= lerp(1.0, surfaceData.baseColor * 2.0, 1.0 - piel * 0.6);
    // Autosombra del pelaje: las capas de dentro reciben menos luz.
    float profundidad = lerp(0.42, 1.0, sqrt(saturate(capa)));
    surfaceData.baseColor = baseColor * lerp(profundidad, m.a, piel);
    surfaceData.ambientOcclusion *= lerp(lerp(0.35, 1.0, saturate(capa)), 1.0, piel);
    surfaceData.specularOcclusion *= lerp(0.5, 1.0, saturate(capa));
    // El pelo brilla poco y de forma difusa; la piel de la cara y las palmas es algo grasa.
    surfaceData.perceptualSmoothness = lerp(0.32 + capa * 0.12, 0.3, piel);
}

#endif // GD_PELAJE_HLSL
