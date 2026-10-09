// Escamas.hlsl — piel de serpiente (y de lo que tenga escamas) para Guardianes/Escamas (copia de
// HDRP/Lit con ganchos). Sin deformacion de vertices: solo el color.
//
// _BaseColorMap (importada LINEAL, sin sRGB) es una mascara, no un color:
//   r = manchas (0 fondo .. 1 mancha), g = variacion de cada escama (0,5 media),
//   b = vientre (escudos ventrales) y claros (orlas, centros de los ocelos), a = 1.
// UV: u da la vuelta al cuerpo (0 y 1 = linea del vientre, 0,5 = lomo), v a lo largo.
// Globales que fija el juego (PielSerpiente en Basiliskos):
//   _GD_EscamasDorso, _GD_EscamasVientre, _GD_EscamasManchas   colores lineales (a = 1 si estan puestos)
//   _GD_EscamasRasgos   x = v donde acaba la cabeza, y = musgo 0..1, z = brillo de las escamas (0..1),
//                       w = piedra 0..1 (petrificada por un espejo)

#ifndef GD_ESCAMAS_HLSL
#define GD_ESCAMAS_HLSL

float4 _GD_EscamasDorso;
float4 _GD_EscamasVientre;
float4 _GD_EscamasManchas;
float4 _GD_EscamasRasgos;

#define GD_HOOK_SUPERFICIE

float GD_Hash21(float2 p)
{
    p = frac(p * float2(123.34, 456.21));
    p += dot(p, p + 45.32);
    return frac(p.x * p.y);
}

float GD_RuidoValor(float2 p)
{
    float2 i = floor(p);
    float2 f = frac(p);
    f = f * f * (3.0 - 2.0 * f);
    float a = GD_Hash21(i), b = GD_Hash21(i + float2(1, 0)), c = GD_Hash21(i + float2(0, 1)), d = GD_Hash21(i + float2(1, 1));
    return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
}

void GD_AjustarSuperficie(FragInputs input, inout SurfaceData surfaceData, float alpha)
{
    float3 m = surfaceData.baseColor;
    float2 uv = input.texCoord0.xy;

    // Sin colores puestos (en el editor antes de jugar): los de la cria.
    float3 dorso = _GD_EscamasDorso.a > 0.5 ? _GD_EscamasDorso.rgb : float3(0.45, 0.083, 0.013);
    float3 vientre = _GD_EscamasVientre.a > 0.5 ? _GD_EscamasVientre.rgb : float3(0.85, 0.64, 0.29);
    float3 manchas = _GD_EscamasManchas.a > 0.5 ? _GD_EscamasManchas.rgb : float3(0.064, 0.007, 0.003);
    float finCabeza = _GD_EscamasRasgos.x > 0.0 ? _GD_EscamasRasgos.x : 1.5;

    // 0 en el lomo .. 1 en la linea del vientre.
    float lado = abs(frac(uv.x) - 0.5) * 2.0;

    // En la cabeza el dibujo del cuerpo se borra: queda el color del lomo con una banda oscura del
    // ojo a la comisura (como en las boas) y la barbilla clara.
    float cabeza = 1.0 - smoothstep(finCabeza * 0.8, finCabeza * 1.25, uv.y);
    float banda = smoothstep(0.62, 0.56, abs(lado - 0.5) + 0.5) * smoothstep(finCabeza * 0.15, finCabeza * 0.35, uv.y);
    float mancha = lerp(saturate(m.r), banda * 0.85, cabeza);
    float claro = lerp(saturate(m.b), smoothstep(0.62, 0.8, lado), cabeza);

    float3 col = lerp(dorso, manchas, mancha);
    col = lerp(col, vientre, claro);
    col *= lerp(0.7, 1.24, saturate(m.g));
    // Contrasombreado: el lomo algo mas oscuro que los flancos; y una variacion lenta a lo largo.
    col *= lerp(0.88, 1.06, smoothstep(0.05, 0.6, lado));
    col *= lerp(0.92, 1.08, GD_RuidoValor(float2(uv.x * 6.0, uv.y * 0.35)));

    // Musgo en el lomo (el Coronado): manchas verdes que crecen desde la linea dorsal.
    float musgo = _GD_EscamasRasgos.y;
    if (musgo > 0.001)
    {
        float n = GD_RuidoValor(float2(uv.x * 40.0, uv.y * 2.2)) * 0.6 + GD_RuidoValor(float2(uv.x * 140.0, uv.y * 8.0)) * 0.4;
        float sobre = smoothstep(0.55, 0.15, lado) * (1.0 - cabeza);
        float verde = saturate((n - (1.0 - musgo * 0.8)) * 6.0) * sobre;
        col = lerp(col, float3(0.05, 0.11, 0.02) * (0.7 + 0.6 * n), verde);
        surfaceData.perceptualSmoothness = lerp(surfaceData.perceptualSmoothness, 0.15, verde);
        surfaceData.normalWS = normalize(lerp(surfaceData.normalWS, input.tangentToWorld[2], verde * 0.7));
    }

    // Piedra (petrificada): gris, mate.
    float piedra = _GD_EscamasRasgos.w;
    if (piedra > 0.001)
    {
        float l = dot(col, float3(0.3, 0.59, 0.11));
        col = lerp(col, float3(0.16, 0.155, 0.15) * (0.6 + l * 3.0), piedra);
        surfaceData.perceptualSmoothness = lerp(surfaceData.perceptualSmoothness, 0.1, piedra);
    }

    surfaceData.baseColor = col;
    // Las escamas son lisas y brillan (mas las grandes del basilisco).
    surfaceData.perceptualSmoothness = saturate(surfaceData.perceptualSmoothness + _GD_EscamasRasgos.z * 0.12);
}

#endif // GD_ESCAMAS_HLSL
