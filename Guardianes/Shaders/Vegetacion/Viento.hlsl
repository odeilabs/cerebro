// Viento.hlsl — animacion de la vegetacion para Guardianes/Vegetacion (copia de HDRP/Lit).
//
// Se engancha en HDRP por ApplyMeshModification (VertMesh.hlsl), que se evalua en TODAS las
// pasadas (depth, gbuffer, sombras, forward) y tambien con el tiempo del fotograma anterior en la
// pasada de motion vectors: el follaje sale bien con TAA/DLSS y no hay z-fighting entre pasadas.
//
// Pesos por vertice (los escribe el generador de mallas, Guardianes.EditorTools.MallaPlanta):
//   color.a  flexion del tronco: 0 en la base, 1 en la copa (crece con la altura al cuadrado)
//   color.r  flexion de rama: 0 en el arranque de la rama, 1 en la punta
//   color.g  aleteo de hoja: 0 en el nervio central, 1 en el borde de la hoja
//   color.b  fase aleatoria de la rama (0..1) para que no se muevan todas a la vez
//
// Globales (las fija Guardianes.Vegetacion.Viento cada fotograma):
//   _GD_Viento          xy direccion en XZ (normalizada), z fuerza (0 calma .. 1 temporal), w turbulencia
//   _GD_VientoParams    x velocidad de las rafagas, y escala espacial de las rafagas,
//                       z amplitud de las ramas (m), w amplitud de las hojas (m)
//   _GD_Interactores[]  xyz posicion ABSOLUTA en mundo, w radio: Kong y lo que empuja la maleza
//   _GD_NumInteractores

#ifndef GD_VIENTO_HLSL
#define GD_VIENTO_HLSL

#if !defined(SHADER_STAGE_RAY_TRACING) && ((SHADERPASS < SHADERPASS_RAYTRACING) || (SHADERPASS > SHADERPASS_RAY_TRACING_DEBUG))

#define HAVE_MESH_MODIFICATION
#define GD_MAX_INTERACTORES 8

float4 _GD_Viento;
float4 _GD_VientoParams;
float4 _GD_Interactores[GD_MAX_INTERACTORES];
float  _GD_NumInteractores;

float GD_CurvaSuave(float x) { return x * x * (3.0 - 2.0 * x); }
float GD_OndaTriangular(float x) { return abs(frac(x + 0.5) * 2.0 - 1.0); }
// Onda periodo 1 en [-1, 1] mas barata que sin() y sin armonicos duros (GPU Gems 3, cap. 16).
float GD_OndaSuave(float x) { return GD_CurvaSuave(GD_OndaTriangular(x)) * 2.0 - 1.0; }

float3 GD_DesplazamientoViento(float3 posAWS, float3 pivoteAWS, float3 normalWS, float4 pesos, float t)
{
    float2 dir = _GD_Viento.xy;
    float fuerza = _GD_Viento.z;
    float turbulencia = _GD_Viento.w;

    // Rafagas: un frente que viaja en la direccion del viento; dos ondas lentas cruzadas.
    float avance = dot(pivoteAWS.xz, dir) * _GD_VientoParams.y - t * _GD_VientoParams.x;
    float rafaga = 0.5 + 0.5 * GD_OndaSuave(avance * 0.031) * GD_OndaSuave(avance * 0.0137 + 0.37);
    float empuje = fuerza * (0.45 + 1.1 * rafaga);

    float alturaRel = max(0.0, posAWS.y - pivoteAWS.y);
    float semilla = frac(dot(pivoteAWS.xz, float2(0.0713, 0.1371)));

    // 1) Tronco: la planta entera se inclina con el viento y cabecea despacio.
    float cabeceo = GD_OndaSuave(t * (0.11 + 0.05 * semilla) + semilla) * (0.25 + 0.35 * turbulencia);
    float flexion = pesos.a * alturaRel * 0.022 * empuje * (1.0 + cabeceo);
    float3 desp = float3(dir.x, 0.0, dir.y) * flexion;

    // 2) Ramas: balanceo con el viento y rebote vertical, cada rama con su fase.
    float faseRama = pesos.b * 6.2831853 + semilla * 13.0;
    float2 ondas = float2(GD_OndaSuave(t * 0.53 + faseRama), GD_OndaSuave(t * 0.81 + faseRama * 1.37));
    float ampRama = pesos.r * _GD_VientoParams.z * (0.35 + empuje);
    desp += (float3(dir.x, 0.0, dir.y) * (0.6 + 0.4 * ondas.x) + float3(-dir.y, 0.0, dir.x) * ondas.y * 0.35) * ampRama * turbulencia;
    desp.y += ondas.y * ampRama * 0.45;

    // 3) Hojas: aleteo rapido a lo largo de la normal.
    float aleteo = GD_OndaSuave(t * (2.3 + 1.1 * pesos.b) + faseRama * 3.1 + dot(posAWS, float3(0.9, 0.7, 1.1)));
    desp += normalWS * aleteo * pesos.g * _GD_VientoParams.w * (0.25 + empuje) * (0.5 + turbulencia);

    return desp;
}

float3 GD_DesplazamientoInteraccion(float3 posAWS, float4 pesos)
{
    float3 desp = 0.0;
    float flexible = saturate(max(pesos.a, pesos.r * 0.7));
    [loop]
    for (int i = 0; i < GD_MAX_INTERACTORES; i++)
    {
        if (i >= (int)_GD_NumInteractores)
            break;
        float4 it = _GD_Interactores[i];
        float2 d = posAWS.xz - it.xz;
        float dist = length(d);
        float influencia = saturate(1.0 - dist / max(it.w, 0.001));
        influencia *= influencia;
        // Solo empuja lo que esta por debajo de la "cintura" del interactor.
        float bajo = saturate((it.y + it.w * 0.35 - posAWS.y) / max(it.w * 0.5, 0.001));
        float k = influencia * bajo * flexible;
        float2 haciaFuera = dist > 1e-3 ? d / dist : float2(0.0, 1.0);
        desp.xz += haciaFuera * k * it.w * 0.55;
        desp.y -= k * it.w * 0.25;
    }
    return desp;
}

AttributesMesh ApplyMeshModification(AttributesMesh input, float3 timeParameters
#ifdef USE_CUSTOMINTERP_SUBSTRUCT
    , inout VaryingsMeshType varyings
#endif
#ifdef HAVE_VFX_MODIFICATION
    , AttributesElement element
#endif
    )
{
    float4 pesos = float4(0.0, 0.0, 0.0, 0.0);
#ifdef ATTRIBUTES_NEED_COLOR
    pesos = input.color;
#endif
    float3 normalOS = float3(0.0, 1.0, 0.0);
#ifdef ATTRIBUTES_NEED_NORMAL
    normalOS = input.normalOS;
#endif

    float3 pivoteAWS = GetAbsolutePositionWS(TransformObjectToWorld(float3(0.0, 0.0, 0.0)));
    float3 posAWS = GetAbsolutePositionWS(TransformObjectToWorld(input.positionOS));
    float3 normalWS = TransformObjectToWorldNormal(normalOS);

    float3 rel = posAWS - pivoteAWS;
    float largo = length(rel);

    float3 desp = GD_DesplazamientoViento(posAWS, pivoteAWS, normalWS, pesos, timeParameters.x);
    desp += GD_DesplazamientoInteraccion(posAWS, pesos);

    // Conserva la distancia al pivote: al doblarse, la copa baja en vez de estirarse.
    float3 relNuevo = rel + desp;
    float largoNuevo = length(relNuevo);
    if (largoNuevo > 1e-4)
        relNuevo *= largo / largoNuevo;

    float3 posNuevaRWS = GetCameraRelativePositionWS(pivoteAWS + relNuevo);
    input.positionOS = TransformWorldToObject(posNuevaRWS);
    return input;
}

#endif // pasadas de rasterizado

// ---------------------------------------------------------------- Linea de vision de la camara
// Con un gorila de 17 m en una selva de 30 m, el dosel taparia a Kong casi siempre: el follaje
// (y los troncos) que quedan entre la camara y Kong, o pegados a la camara, se disuelven con un
// tramado de pantalla. No se aplica en la pasada de sombras (las sombras no cambian).
//   _GD_Vision.xyz   posicion ABSOLUTA de la camara      w radio del cilindro (m)
//   _GD_VisionFin.xyz posicion ABSOLUTA de Kong (cabeza)  w 1 = activo
float4 _GD_Vision;
float4 _GD_VisionFin;

#define GD_HOOK_ALFA

void GD_AjustarAlfa(FragInputs input, inout float alphaValue)
{
#if SHADERPASS != SHADERPASS_SHADOWS
    if (_GD_VisionFin.w < 0.5)
        return;
    float3 p = GetAbsolutePositionWS(input.positionRWS);
    float3 a = _GD_Vision.xyz;
    float3 b = _GD_VisionFin.xyz;
    float3 ab = b - a;
    float t = saturate(dot(p - a, ab) / max(dot(ab, ab), 1e-3));
    float d = length(p - (a + ab * t));
    // Cilindro que se estrecha cerca de Kong (su silueta se ve a traves del follaje).
    float radio = _GD_Vision.w * lerp(1.0, 0.45, t);
    float dentro = (1.0 - smoothstep(radio * 0.55, radio, d)) * (1.0 - smoothstep(0.85, 0.97, t));
    // Y todo lo que esta a menos de 6 m de la camara.
    float cerca = 1.0 - smoothstep(2.5, 6.0, length(input.positionRWS));
    float quitar = saturate(max(dentro, cerca));
    // Tramado de pantalla (ruido de gradiente entrelazado de Jimenez).
    float ruido = frac(52.9829189 * frac(dot(input.positionSS.xy, float2(0.06711056, 0.00583715))));
    if (quitar > ruido)
        alphaValue = 0.0;
#endif
}

#endif // GD_VIENTO_HLSL
