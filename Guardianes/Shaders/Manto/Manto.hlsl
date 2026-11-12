// Manto.hlsl — las capas del pelaje de un animal para Guardianes/Manto (copia del Lit de HDRP
// con ganchos, generada por Tools~/generar_shaders_lit.py). Portado de AssetsCreator
// (unity/ShadersLit/Manto.hlsl, 12-11-2026) sin cambiar lo que hace.
//
// Sale de Pelaje.hlsl (el gorila) con tres diferencias:
//  - TODO VA POR MATERIAL y nada es global: dos especies en escena no comparten parametros;
//  - el pelo va PEINADO a lo largo del flujo: de punta parece terciopelo. La vaca tumba sus
//    hebras de 20 mm hasta dejar el manto en ~8 mm de alto: casi todo el largo es desplazamiento
//    a lo largo de la piel, no hacia fuera;
//  - largo, altura y flujo salen de los mapas horneados de las hebras de la toma
//    (ultra\animal_juego.py), y cada hebra acaba donde acaba en la toma.
//
// El mismo SkinnedMeshRenderer (la malla de capas) se pinta una vez por material. Propiedades del
// Lit que el pelaje no usa, reutilizadas (el cbuffer no se toca: asi sigue valiendo el batcher):
//   _TexWorldScale               fraccion de la capa, 0 piel .. 1 punta
//   _ThicknessRemap              x largo_max (m), y peinado, z caida, w peso del ruido de fibra
//   _Thickness                   la lisura del brillo de fibra
//   _SubsurfaceMask              lo que suben de mas las capas vistas de canto
//   _IridescenceThicknessRemap   x repeticiones del ruido, y oscuro de raiz, z realce de la altura,
//                                w el F0 de la fibra en los oscuros
//   _SubsurfaceMaskMap           el flujo: RG angulo doblado, BA vector suavizado (signo)
//   _ThicknessMap                R largo y G altura del manto (de largo_max), B el azar de la
//                                hebra de encima, A su altura respecto de su entorno
//   _IridescenceMaskMap          ruido de fibra repetido
// Con estas ranuras el Lit no hace nada: sus palabras clave solo actuan en SSS, transmision e
// iridiscencia, que este material no enciende.

#ifndef GD_MANTO_HLSL
#define GD_MANTO_HLSL

#define GD_Capa            _TexWorldScale
#define GD_LargoMax        _ThicknessRemap.x
#define GD_Peinado         _ThicknessRemap.y
#define GD_Caida           _ThicknessRemap.z
#define GD_RuidoFuerza     _ThicknessRemap.w
#define GD_RuidoTiling     _IridescenceThicknessRemap.x
#define GD_OscuroRaiz      _IridescenceThicknessRemap.y
#define GD_Realce          _IridescenceThicknessRemap.z
#define GD_F0Fibra         _IridescenceThicknessRemap.w
#define GD_LisuraFibra     _Thickness
#define GD_Borde           _SubsurfaceMask
// lo que mide un metro en la UV del atlas: la tesela de fibra son 40 mm (FIBRA_MM) y se repite
// GD_RuidoTiling veces en el atlas
#define GD_UVPorMetro      (25.0 / GD_RuidoTiling)

// el mip de lo que se lee en el vertice: el largo y el flujo a ~13 mm por texel, para que la
// malla de capas (triangulos de 20-40 mm) no salte de una hebra a otra
#define GD_MIP_VERTICE 4.0

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
#if defined(ATTRIBUTES_NEED_TEXCOORD0) && defined(ATTRIBUTES_NEED_TANGENT) && defined(ATTRIBUTES_NEED_NORMAL)
    float h = GD_Capa;
    float2 uv = input.uv0.xy;
    float4 lg = SAMPLE_TEXTURE2D_LOD(_ThicknessMap, sampler_ThicknessMap, uv, GD_MIP_VERTICE);
    float4 fl = SAMPLE_TEXTURE2D_LOD(_SubsurfaceMaskMap, sampler_SubsurfaceMaskMap, uv, GD_MIP_VERTICE);
    float largo = lg.r * GD_LargoMax;
    float alto = min(lg.g * GD_LargoMax * GD_Realce, largo);
    float3 n = normalize(input.normalOS);
    // LA SILUETA. Vista de canto, una linea de vista cruza decenas de fibras y basta una para taparla:
    // el contorno sale liso a la altura de las mas altas. Y una hebra suelta se separa del manto, que
    // de frente no se ve y en el contorno es lo que lo deshilacha: las capas suben mas de canto
    float nv = saturate(dot(TransformObjectToWorldNormal(n), GetWorldSpaceNormalizeViewDir(TransformObjectToWorld(input.positionOS))));
    alto *= 1.0 + GD_Borde * (1.0 - nv) * (1.0 - nv) * (1.0 - nv);
    float3 t = normalize(input.tangentOS.xyz);
    float3 b = cross(n, t) * input.tangentOS.w;
    // el vector suavizado, no el doblado: el peinado necesita saber hacia donde va la punta
    float2 d = fl.ba * 2.0 - 1.0;
    float3 flujo = t * d.x + b * d.y;
    flujo /= max(length(flujo), 1e-4);
    // la hebra sale casi de punta y se tumba: lo tumbado crece con el cuadrado de la capa
    float tumbado = sqrt(max(largo * largo - alto * alto, 0.0));
    float3 caida = TransformWorldToObjectDir(float3(0.0, -1.0, 0.0), false);
    input.positionOS += n * h * alto
                      + flujo * h * h * tumbado * GD_Peinado
                      + caida * h * h * largo * GD_Caida;
#endif
    return input;
}

#endif // pasadas de rasterizado

// ---------------------------------------------------------------- Ganchos de superficie
#define GD_HOOK_ALFA
#define GD_HOOK_SUPERFICIE

// La capa sobrevive donde hay una hebra que llega a su altura. El mapa dice cuanto sube la hebra de
// encima de la toma en este texel, y el ruido repetido parte eso en fibras mas finas que un texel. El
// ruido va SIN mips: promediado, de lejos vale lo mismo en todas partes y la silueta sale lisa; a mip
// 0 parpadea, y es el TAA el que lo convierte en cobertura (el descarte estocastico de RDR2, doc 14
// §7.1). Sin hebra (ubre, borde del ojo) no sale nada.
void GD_AjustarAlfa(FragInputs input, inout float alphaValue)
{
    float h = GD_Capa;
    float2 uv = input.texCoord0.xy;
    float4 lg = SAMPLE_TEXTURE2D(_ThicknessMap, sampler_ThicknessMap, uv);
    float fibra = SAMPLE_TEXTURE2D_LOD(_IridescenceMaskMap, sampler_IridescenceMaskMap, uv * GD_RuidoTiling, 0.0).r;
    float punta = lerp(lg.a, fibra, GD_RuidoFuerza);
    float cubre = step(0.02, lg.r) * step(0.02, lg.g);
    alphaValue = cubre * (_AlphaCutoff + (punta - h) * 4.0);
}

void GD_AjustarSuperficie(FragInputs input, inout SurfaceData surfaceData, float alpha)
{
    float h = GD_Capa;
    float2 uv = input.texCoord0.xy;
    float4 lg = SAMPLE_TEXTURE2D(_ThicknessMap, sampler_ThicknessMap, uv);
    // el oscurecimiento de raiz a punta: las capas de dentro ven menos cielo. Exponente 0.75 de
    // la investigacion (§5.7), y nada donde el manto no tiene fondo (a menos de 4 mm las capas van
    // casi juntas y la de abajo ganaria al azar: era una raya oscura junto a la trufa)
    float fondo = saturate(lg.g * GD_LargoMax / 0.004);
    float profundidad = lerp(1.0, lerp(GD_OscuroRaiz, 1.0, pow(saturate(h), 0.75)), fondo);
    // EL COLOR NO SE PEINA DOS VECES. El atlas ya tiene cada color donde cae la hebra de la toma,
    // peinada; la capa, al tumbarse otra vez, se lo llevaba 1-2 cm hacia la cola y las manchas no
    // casaban con la toma (dE 5.1 -> 6.4). El color se lee donde la capa cae, y no en su raiz. La
    // fibra sigue en su raiz: es lo que hace que se incline
    float4 lm = SAMPLE_TEXTURE2D_LOD(_ThicknessMap, sampler_ThicknessMap, uv, GD_MIP_VERTICE);
    float2 fm = SAMPLE_TEXTURE2D_LOD(_SubsurfaceMaskMap, sampler_SubsurfaceMaskMap, uv, GD_MIP_VERTICE).ba * 2.0 - 1.0;
    fm /= max(length(fm), 1e-4);
    float largo = lm.r * GD_LargoMax;
    float alto = min(lm.g * GD_LargoMax * GD_Realce, largo);
    float tumbado = sqrt(max(largo * largo - alto * alto, 0.0));
    float2 uvColor = uv + fm * h * h * tumbado * GD_Peinado * GD_UVPorMetro;
    surfaceData.baseColor = SAMPLE_TEXTURE2D(_BaseColorMap, sampler_BaseColorMap, uvColor).rgb * _BaseColor.rgb * profundidad;
    surfaceData.ambientOcclusion *= profundidad;
#ifdef _MATERIAL_FEATURE_ANISOTROPY
    // EL BRILLO DE FIBRA, con la tangente del Lit a lo largo del flujo (el angulo doblado: un campo
    // de lineas). El Lit en diferido solo guarda anisotropia con F0 0.04, y con todo el manto asi el
    // negro de la frisona sale gris (L ~40 contra 7 de la toma); con solo unas hebras a F0 0.04 sale
    // salpicado de motas. Pero sigue siendo el F0 del flujo metalico: F0 = lerp(0.04, base, metalico)
    // y difuso = base * (1 - metalico). En un negro basta un metalico de 0.7 con la base que pida
    // GD_F0Fibra para bajar el F0 y dejar el difuso como estaba: GD_F0Fibra 0.03 da base 0.026 y
    // difuso 0.008 (el del atlas, 0.011; con metalico 1 el negro en sombra bajaba de L 7 a 4). Cada
    // hebra lleva su F0 (el azar horneado, sin mips): asi el brillo se parte en fibras sin parpadear
    float2 d = SAMPLE_TEXTURE2D(_SubsurfaceMaskMap, sampler_SubsurfaceMaskMap, uv).ba * 2.0 - 1.0;
    surfaceData.tangentWS = normalize(d.x * input.tangentToWorld[0] + d.y * input.tangentToWorld[1]);
    surfaceData.perceptualSmoothness = GD_LisuraFibra;
    float azar = SAMPLE_TEXTURE2D_LOD(_ThicknessMap, sampler_ThicknessMap, uv, 0.0).b;
    // (solo en el negro de verdad, el 0.011 de la frisona: en un marron oscuro se comeria su color)
    float oscuro = 1.0 - smoothstep(0.02, 0.05, Luminance(surfaceData.baseColor));
    surfaceData.metallic = 0.7 * oscuro;
    surfaceData.baseColor = lerp(surfaceData.baseColor, max(GD_F0Fibra * (0.5 + azar) - 0.012, 0.0).xxx / 0.7, oscuro);
#endif
}

#endif // GD_MANTO_HLSL
