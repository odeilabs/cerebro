"""Genera las capas PBR del suelo de la jungla (TerrainLayer de HDRP) de forma procedural.

Cada capa se construye como en un tile sampler de Substance: un suelo base periodico sobre el
que se esparcen y apilan elementos discretos (hojas, ramitas, piedras, briznas, grietas...) con
forma, tamano, giro y color aleatorios. De la altura resultante salen la normal, el AO y el
canal de altura de la mascara; el albedo y la suavidad se componen por elemento.

Uso (desde la raiz del repo):
    python Tools/arte/terreno/generar_capas_terreno.py [--res 2048] [--solo hojarasca,roca] [--semilla 7]

Salida:  <proyecto>/Assets/_Game/Art/Terreno/Capas/<capa>/<capa>_{albedo,normal,mascara}.png (--proyecto o --salida)
Revision: Tools/arte/terreno/_revision/<capa>_revision.png y <capa>_detalle.png

Todo es determinista para una misma semilla y resolucion, y tileable (periodico).
"""
import argparse
import importlib
import os
import sys
import time

AQUI = os.path.dirname(os.path.abspath(__file__))
RAIZ = os.path.dirname(os.path.dirname(os.path.dirname(AQUI)))
DIR_CAPAS = None
DIR_REVISION = os.path.join(AQUI, "_revision")

sys.path.insert(0, AQUI)
sys.dont_write_bytecode = True  # no dejar __pycache__ dentro del repo

from nucleo import Ctx  # noqa: E402
from exportar import exportar  # noqa: E402

# capa -> modulo (cada modulo define TAM_M = metros por repeticion y generar(ctx))
CAPAS = [
    "hojarasca",
    "barro",
    "musgo",
    "roca",
    "guijarros",
    "arena",
    "hierba_suelo",
    "contaminado",
]


def main():
    ap = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    ap.add_argument("--res", type=int, default=2048, help="resolucion (potencia de 2)")
    ap.add_argument("--solo", type=str, default="", help="lista de capas separadas por comas")
    ap.add_argument("--semilla", type=int, default=7)
    ap.add_argument("--sin-revision", action="store_true", help="no escribir hojas de revision")
    ap.add_argument("--salida", type=str, default=None, help="carpeta de capas (manda sobre --proyecto)")
    ap.add_argument("--proyecto", type=str, default=None, help="carpeta del proyecto Unity")
    args = ap.parse_args()
    if not args.salida:
        if not args.proyecto:
            sys.exit("Falta --proyecto <carpeta del proyecto Unity> o --salida <carpeta>")
        args.salida = os.path.join(args.proyecto, "Assets", "_Game", "Art", "Terreno", "Capas")

    capas = [c.strip() for c in args.solo.split(",") if c.strip()] or CAPAS
    for c in capas:
        if c not in CAPAS:
            sys.exit(f"Capa desconocida: {c}. Disponibles: {', '.join(CAPAS)}")

    total = time.time()
    for nombre in capas:
        mod = importlib.import_module(f"capa_{nombre}")
        ctx = Ctx(nombre, args.res, mod.TAM_M, args.semilla)
        t0 = time.time()
        print(f"[{nombre}] generando {args.res}px, tile {mod.TAM_M:g} m ({ctx.texel * 1000:.2f} mm/px)...", flush=True)
        res = mod.generar(ctx)
        t_gen = time.time() - t0
        stats = exportar(ctx, res, args.salida, None if args.sin_revision else DIR_REVISION, t_gen)
        t_tot = time.time() - t0
        print(f"[{nombre}] {t_gen:.1f}s generacion, {t_tot:.1f}s con exportacion  | " +
              "  ".join(f"{k}={v:.3f}" for k, v in stats.items()), flush=True)
    print(f"Hecho en {time.time() - total:.1f}s -> {args.salida}")


if __name__ == "__main__":
    main()
