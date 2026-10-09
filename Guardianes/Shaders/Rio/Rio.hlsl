// Rio.hlsl — corriente de agua para Guardianes/Rio (copia de HDRP/Lit transparente con refraccion).
//
// La cinta del rio (AguaIsla) lleva uv0 en metros/8 (v a lo largo del cauce) y en color.r la
// velocidad relativa del tramo (0 remanso .. 1 rapido). Aqui desplazamos las UV con el tiempo:
// uv0 arrastra el mapa de normales base y uv1 (detalle, otra escala y velocidad) rompe la
// repeticion. Asi el agua "corre" rio abajo en todas las pasadas sin tocar el Lit.

#ifndef GD_RIO_HLSL
#define GD_RIO_HLSL

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
    float t = timeParameters.x;
    float velocidad = 0.6;
#ifdef ATTRIBUTES_NEED_COLOR
    velocidad = lerp(0.35, 3.2, input.color.r);
#endif
#ifdef ATTRIBUTES_NEED_TEXCOORD0
    float2 base = input.uv0.xy;
    #ifdef ATTRIBUTES_NEED_TEXCOORD1
    input.uv1.xy = base * 2.3 + float2(0.37, 0.11);
    input.uv1.y -= t * velocidad * 0.21;
    #endif
    // uv en unidades de 8 m: velocidad en m/s / 8.
    input.uv0.y = base.y - t * velocidad * 0.125;
#endif
    return input;
}

#endif // pasadas de rasterizado
#endif // GD_RIO_HLSL
