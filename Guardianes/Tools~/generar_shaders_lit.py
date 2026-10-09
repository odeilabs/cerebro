"""Genera variantes de HDRP/Lit con modificacion de vertices (y opcionalmente de superficie) propia.

HDRP 17 no deja meter animacion de vertices en el Lit de serie (solo Shader Graph). Copiamos el
Lit.shader del paquete HDRP de un proyecto y le enganchamos un ApplyMeshModification (el gancho de
VertMesh.hlsl que usa el terreno), de modo que conservamos TODO el Lit: translucidez con perfil de
difusion, SSS, doble cara, alpha test, sombras, motion vectors, LOD crossfade y el inspector LitGUI.

Las variantes "con superficie" copian ademas LitData.hlsl con dos ganchos de pixel:
    GD_HOOK_ALFA        GD_AjustarAlfa(input, alphaValue)               antes del alpha test
    GD_HOOK_SUPERFICIE  GD_AjustarSuperficie(input, surfaceData, alpha) tras leer las texturas
(el .hlsl de la variante define las macros y las funciones).

Uso (con un proyecto Unity que tenga HDRP importado al menos una vez):
    python Tools~/generar_shaders_lit.py --proyecto "<carpeta del proyecto Unity>"

Variantes (en Shaders/ del paquete):
    Guardianes/Vegetacion  ->  Shaders/Vegetacion/VegetacionLit.shader  (+ Viento.hlsl, con superficie)
    Guardianes/Pelaje      ->  Shaders/Pelaje/PelajeLit.shader          (+ Pelaje.hlsl, con superficie)
    Guardianes/Rio         ->  Shaders/Rio/RioLit.shader                (+ Rio.hlsl)
    Guardianes/Escamas     ->  Shaders/Escamas/EscamasLit.shader        (+ Escamas.hlsl, con superficie)

Las pasadas de profundidad y motion vectors del Lit solo piden color/normal de vertice si hay
desplazamiento; aqui los forzamos para que la deformacion sea identica en todas las pasadas.
Una variante cuyo .hlsl aun no existe se salta.
"""
import argparse
import glob
import os
import re
import sys

PAQUETE = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
PAQ = "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/Lit/"

VARIANTES = [
    # (nombre del shader, carpeta destino en el paquete, archivo .shader, include de modificacion, con ganchos de superficie)
    ("Guardianes/Vegetacion", "Shaders/Vegetacion", "VegetacionLit.shader", "Viento.hlsl", True),
    ("Guardianes/Pelaje", "Shaders/Pelaje", "PelajeLit.shader", "Pelaje.hlsl", True),
    ("Guardianes/Rio", "Shaders/Rio", "RioLit.shader", "Rio.hlsl", False),
    ("Guardianes/Escamas", "Shaders/Escamas", "EscamasLit.shader", "Escamas.hlsl", True),
]

CABECERA = """// GENERADO por Tools~/generar_shaders_lit.py a partir de HDRP/Lit ({version}). No editar a mano:
// los cambios van en {mod} o en el generador.
"""

ALFA_ORIGINAL = "    float alphaValue = alphaTex * _BaseColor.a;\n"
ALFA_GANCHO = ALFA_ORIGINAL + """    #ifdef GD_HOOK_ALFA
    GD_AjustarAlfa(input, alphaValue);
    #endif
"""
SUPERFICIE_ORIGINAL = "    float alpha = GetSurfaceData(input, layerTexCoord, surfaceData, normalTS, bentNormalTS);\n"
SUPERFICIE_GANCHO = SUPERFICIE_ORIGINAL + """    #ifdef GD_HOOK_SUPERFICIE
    GD_AjustarSuperficie(input, surfaceData, alpha);
    #endif
"""


def carpeta_hdrp(proyecto):
    candidatos = glob.glob(os.path.join(proyecto, "Library", "PackageCache", "com.unity.render-pipelines.high-definition@*"))
    if not candidatos:
        sys.exit("No encuentro HDRP en %s/Library/PackageCache: abre el proyecto en Unity una vez." % proyecto)
    return candidatos[0]


def forzar_atributos(texto):
    texto = re.sub(r"#define REQUIRE_NORMAL .*", "#define REQUIRE_NORMAL 1 // Guardianes: la deformacion usa la normal", texto)
    texto = re.sub(r"#define REQUIRE_VERTEX_COLOR .*", "#define REQUIRE_VERTEX_COLOR 1 // Guardianes: pesos en el color de vertice", texto)
    return texto


def main():
    ap = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    ap.add_argument("--proyecto", required=True, help="proyecto Unity con HDRP (para copiar su Lit)")
    ap.add_argument("--solo", default="", help="variantes separadas por comas (Vegetacion,Pelaje,Rio,Escamas)")
    a = ap.parse_args()
    solo = set(s.strip() for s in a.solo.split(",") if s.strip())

    hdrp = carpeta_hdrp(a.proyecto)
    version = os.path.basename(hdrp)
    lit_dir = os.path.join(hdrp, "Runtime", "Material", "Lit")
    lit = open(os.path.join(lit_dir, "Lit.shader"), encoding="utf-8").read()
    depth = open(os.path.join(lit_dir, "ShaderPass", "LitDepthPass.hlsl"), encoding="utf-8").read()
    mv = open(os.path.join(lit_dir, "ShaderPass", "LitMotionVectorPass.hlsl"), encoding="utf-8").read()
    datos = open(os.path.join(lit_dir, "LitData.hlsl"), encoding="utf-8").read()

    for nombre, carpeta, archivo, mod, con_superficie in VARIANTES:
        if solo and nombre.split("/")[-1] not in solo:
            continue
        destino = os.path.join(PAQUETE, carpeta)
        if not os.path.exists(os.path.join(destino, mod)):
            print("(%s sin %s/%s todavia: no se genera)" % (nombre, carpeta, mod))
            continue
        prefijo = os.path.splitext(archivo)[0].replace("Lit", "")
        cab = CABECERA.format(version=version, mod=mod)

        s = lit.replace('Shader "HDRP/Lit"', 'Shader "%s"' % nombre, 1)
        s = s.replace('"%sShaderPass/LitDepthPass.hlsl"' % PAQ, '"%sDepthPass.hlsl"' % prefijo)
        s = s.replace('"%sShaderPass/LitMotionVectorPass.hlsl"' % PAQ, '"%sMotionVectorPass.hlsl"' % prefijo)
        n = s.count('"%sLitData.hlsl"' % PAQ)
        if n < 8:
            sys.exit("Lit.shader ha cambiado: solo %d includes de LitData" % n)
        # La modificacion va ANTES de los datos (los ganchos de superficie se usan dentro de LitData).
        datos_incl = '"%sData.hlsl"' % prefijo if con_superficie else '"%sLitData.hlsl"' % PAQ
        s = s.replace('#include "%sLitData.hlsl"' % PAQ, '#include "%s"\n            #include %s' % (mod, datos_incl))
        open(os.path.join(destino, archivo), "w", encoding="utf-8", newline="\n").write(cab + s)
        open(os.path.join(destino, prefijo + "DepthPass.hlsl"), "w", encoding="utf-8", newline="\n").write(cab + forzar_atributos(depth))
        open(os.path.join(destino, prefijo + "MotionVectorPass.hlsl"), "w", encoding="utf-8", newline="\n").write(cab + forzar_atributos(mv))
        if con_superficie:
            if ALFA_ORIGINAL not in datos or SUPERFICIE_ORIGINAL not in datos:
                sys.exit("LitData.hlsl ha cambiado: no encuentro donde enganchar la superficie")
            d = datos.replace(ALFA_ORIGINAL, ALFA_GANCHO, 1).replace(SUPERFICIE_ORIGINAL, SUPERFICIE_GANCHO, 1)
            open(os.path.join(destino, prefijo + "Data.hlsl"), "w", encoding="utf-8", newline="\n").write(cab + d)
        print("%s -> %s/%s (%d pasadas enganchadas%s)" % (nombre, carpeta, archivo, n, ", con superficie" if con_superficie else ""))


if __name__ == "__main__":
    main()
