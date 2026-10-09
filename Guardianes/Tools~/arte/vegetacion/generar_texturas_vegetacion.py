"""Genera las texturas PBR de la vegetacion de la selva (atlas de hojas con alfa y cortezas tileables).

Uso (desde la raiz del repo):
    python Tools/arte/vegetacion/generar_texturas_vegetacion.py [--res 2048] [--solo fronda_palmera,corteza_ceiba]
                                                               [--semilla 11] [--procesos 4] [--sin-revision]

Salida: <proyecto>/Assets/_Game/Art/Vegetacion/Texturas/<conjunto>/<conjunto>_<mapa>.png (--proyecto o --salida)
Hojas de revision: Tools/arte/vegetacion/_revision/ (ignoradas por git).

Mapas de los atlas de hojas (tarjetas con alpha test a 0.5):
    _albedo   sRGB RGBA, A = cobertura con antialias; RGB dilatado (push-pull) fuera del borde
    _normal   tangente OpenGL (+V = de la insercion hacia la punta)
    _mascara  lineal: R metalico 0, G AO, B 0, A suavidad
    _grosor   lineal: 0 fino (mucha luz a contraluz) .. 1 grueso
Cortezas (tileables en U y V): _albedo, _normal, _mascara y _altura (gris 16 bits).
Todo es determinista: misma semilla -> mismos PNG (cada conjunto tiene su propia semilla derivada).
"""
import argparse
import os
import sys
import time
import zlib

import numpy as np

AQUI = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, AQUI)

RAIZ = os.path.dirname(os.path.dirname(os.path.dirname(AQUI)))
SALIDA = None
REVISION = os.path.join(AQUI, "_revision")

# nombre -> (tipo, modulo, funcion, alto relativo al ancho)
CONJUNTOS = {
    "hojas_dosel_a": ("hojas", "ramilletes", "atlas_dosel_a", 1.0),
    "hojas_dosel_b": ("hojas", "ramilletes", "atlas_dosel_b", 1.0),
    "hojas_ceiba": ("hojas", "ramilletes", "atlas_ceiba", 1.0),
    "fronda_palmera": ("hojas", "frondas", "atlas_palmera", 1.0),
    "fronda_helecho": ("hojas", "frondas", "atlas_helecho", 1.0),
    "hoja_platano": ("hojas", "frondas", "atlas_platano", 1.0),
    "hojas_grandes": ("hojas", "grandes", "atlas_grandes", 1.0),
    "hierba_alta": ("hojas", "hierba", "atlas_hierba", 0.5),
    "hojas_bambu": ("hojas", "ramilletes", "atlas_bambu", 1.0),
    "flores": ("hojas", "flores", "atlas_flores", 1.0),
    "corteza_ceiba": ("corteza", "cortezas", "corteza_ceiba", 1.0),
    "corteza_tropical": ("corteza", "cortezas", "corteza_tropical", 1.0),
    "corteza_palmera": ("corteza", "cortezas", "corteza_palmera", 1.0),
    "tronco_helecho": ("corteza", "cortezas", "tronco_helecho", 1.0),
    "bambu": ("corteza", "cortezas", "corteza_bambu", 1.0),
    "madera_astillada": ("corteza", "cortezas", "madera_astillada", 1.0),
    "anillos_tronco": ("anillos", "cortezas", "anillos_tronco", 1.0),
}


def semilla_de(nombre, semilla):
    return (zlib.crc32(nombre.encode("utf-8")) ^ (semilla * 2654435761)) & 0xFFFFFFFF


def generar(nombre, res, semilla, salida, revision):
    import importlib

    import comun
    import ruido
    tipo, modulo, funcion, rel = CONJUNTOS[nombre]
    t0 = time.time()
    rng = np.random.default_rng(semilla_de(nombre, semilla))
    banco = ruido.BancoRuido(np.random.default_rng(semilla_de("banco", semilla)))
    f = getattr(importlib.import_module(modulo), funcion)
    rng_guardado = np.random.default_rng(7)
    if tipo == "hojas":
        lienzo, info = f(res, rng, banco)
        mapas = lienzo.finalizar(escala_px=res / 2048.0)
        comun.guardar_atlas_hojas(salida, nombre, mapas, rng_guardado)
        if revision:
            import revision as rev
            rev.revision_hojas(REVISION, nombre, mapas, recortes=info.get("recortes") if isinstance(info, dict) else None)
    elif tipo == "corteza":
        mapas = f(res, rng, banco)
        comun.guardar_corteza(salida, nombre, mapas, rng_guardado)
        if revision:
            import revision as rev
            rev.revision_corteza(REVISION, nombre, mapas)
    else:  # anillos: no tileable, RGBA
        mapas = f(res // 2, rng, banco)
        comun.guardar_corteza(salida, nombre, mapas, rng_guardado)
        if revision:
            import revision as rev
            rev.revision_hojas(REVISION, nombre, dict(mapas, grosor=np.ones_like(mapas["ao"])))
    dt = time.time() - t0
    return nombre, dt


def _trabajo(args):
    return generar(*args)


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--res", type=int, default=2048, help="resolucion base (ancho del atlas), 2048 por defecto")
    ap.add_argument("--solo", type=str, default="", help="lista de conjuntos separada por comas")
    ap.add_argument("--semilla", type=int, default=11)
    ap.add_argument("--salida", type=str, default=None, help="carpeta raiz de salida (manda sobre --proyecto)")
    ap.add_argument("--proyecto", type=str, default=None, help="carpeta del proyecto Unity")
    ap.add_argument("--procesos", type=int, default=1, help="conjuntos en paralelo (procesos)")
    ap.add_argument("--sin-revision", action="store_true", help="no escribir hojas de revision")
    a = ap.parse_args()
    if not a.salida:
        if not a.proyecto:
            sys.exit("Falta --proyecto <carpeta del proyecto Unity> o --salida <carpeta>")
        a.salida = os.path.join(a.proyecto, "Assets", "_Game", "Art", "Vegetacion", "Texturas")
    if a.res % 64:
        sys.exit("--res debe ser multiplo de 64")
    nombres = [n.strip() for n in a.solo.split(",") if n.strip()] or list(CONJUNTOS)
    malos = [n for n in nombres if n not in CONJUNTOS]
    if malos:
        sys.exit("Conjuntos desconocidos: %s\nDisponibles: %s" % (", ".join(malos), ", ".join(CONJUNTOS)))
    trabajos = [(n, a.res, a.semilla, a.salida, not a.sin_revision) for n in nombres]
    t0 = time.time()
    if a.procesos > 1 and len(trabajos) > 1:
        from multiprocessing import Pool
        with Pool(min(a.procesos, len(trabajos))) as pool:
            for nombre, dt in pool.imap_unordered(_trabajo, trabajos):
                print(f"  {nombre:20s} {dt:7.1f} s", flush=True)
    else:
        for t in trabajos:
            nombre, dt = generar(*t)
            print(f"  {nombre:20s} {dt:7.1f} s", flush=True)
    print(f"Total {time.time() - t0:.1f} s -> {a.salida}")


if __name__ == "__main__":
    main()
