// Tarjeta.hlsl — las tarjetas del pelo largo de un animal para Guardianes/Tarjeta (copia del Lit
// de HDRP con ganchos, generada por Tools~/generar_shaders_lit.py). Portado de AssetsCreator
// (unity/ShadersLit/Tarjeta.hlsl, 12-11-2026) sin cambiar lo que hace.
//
// El atlas de ultra\animal_tarjetas.py lleva el color y la cobertura de cada mechon, a 1 mm por
// texel: ahi no cabe una hebra, que mide 0.1-0.3 mm, y a solas sale una pincelada de borde duro. Una
// tesela de hebras, compartida y repetida, parte la tira en pelos: la cobertura dice cuanto pelo hay
// y la tesela cuales pasan. Va SIN mips y con un ruido por fotograma: el TAA lo funde en un borde
// blando, el descarte estocastico de las capas (Manto.hlsl).
//
// Todo va por material. Propiedades del Lit que la tarjeta no usa, reutilizadas (el cbuffer no se
// toca: asi sigue valiendo el batcher):
//   _ThicknessRemap              xy repeticiones de la tesela, z lo que corta la tesela la
//                                cobertura, w lo que varia el color de una hebra a otra
//   _IridescenceThicknessRemap   x peso del ruido por fotograma
//   _SubsurfaceMask              lo que se empuja la malla en la pasada de sombra, en metros: ver abajo
//   _IridescenceMaskMap          la tesela: la U cruza las hebras y la V va a lo largo

#ifndef GD_TARJETA_HLSL
#define GD_TARJETA_HLSL

#define GD_Tiling       _ThicknessRemap.xy
#define GD_Corte        _ThicknessRemap.z
#define GD_ColorFibra   _ThicknessRemap.w
#define GD_Ruido        _IridescenceThicknessRemap.x
#define GD_Empuje       _SubsurfaceMask

#if !defined(SHADER_STAGE_RAY_TRACING) && ((SHADERPASS < SHADERPASS_RAYTRACING) || (SHADERPASS > SHADERPASS_RAY_TRACING_DEBUG))

#define HAVE_MESH_MODIFICATION

// LA SOMBRA DEL PELO EN EL SUELO. Las tarjetas no echan sombra: sobre el manto salian manchas negras.
// La echa una malla hinchada, solo en la pasada de sombra y empujada a lo largo del rayo de luz: la
// sombra cae en el suelo donde tiene que caer, y el empuje la deja detras de la piel que tiene delante,
// que asi no se oscurece.
AttributesMesh ApplyMeshModification(AttributesMesh input, float3 timeParameters
#ifdef USE_CUSTOMINTERP_SUBSTRUCT
    , inout VaryingsMeshType varyings
#endif
#ifdef HAVE_VFX_MODIFICATION
    , AttributesElement element
#endif
    )
{
#if defined(SHADERPASS) && (SHADERPASS == SHADERPASS_SHADOWS)
    input.positionOS += TransformWorldToObjectDir(GetViewForwardDir(), false) * GD_Empuje;
#endif
    return input;
}

#endif // pasadas de rasterizado

// ---------------------------------------------------------------- Ganchos de superficie
#define GD_HOOK_ALFA
#define GD_HOOK_SUPERFICIE

float GD_Hebra(FragInputs input)
{
    return SAMPLE_TEXTURE2D_LOD(_IridescenceMaskMap, sampler_IridescenceMaskMap, input.texCoord0.xy * GD_Tiling, 0.0).r;
}

void GD_AjustarAlfa(FragInputs input, inout float alphaValue)
{
    float ruido = InterleavedGradientNoise(input.positionSS.xy, (int)_TaaFrameInfo.z) - 0.5;
    alphaValue += (GD_Hebra(input) - 0.5) * GD_Corte + ruido * GD_Ruido;
}

void GD_AjustarSuperficie(FragInputs input, inout SurfaceData surfaceData, float alpha)
{
    surfaceData.baseColor *= 1.0 + (GD_Hebra(input) - 0.5) * 2.0 * GD_ColorFibra;
}

#endif // GD_TARJETA_HLSL
