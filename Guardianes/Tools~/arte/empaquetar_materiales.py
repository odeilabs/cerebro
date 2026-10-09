"""Empaqueta materiales de AssetsCreator (creador.py material <receta>) al formato del juego.

    python Tools/arte/empaquetar_materiales.py <carpeta_assetscreator> <destino> [receta ...]

Cada receta de AssetsCreator deja <r>_basecolor/normal/roughness/ao[/metallic].png. Aqui se
escriben en <destino>/<r>/:
    <r>_albedo.png   RGB (sRGB), alfa 255
    <r>_normal.png   normal OpenGL tal cual
    <r>_mascara.png  mascara HDRP: R metalico, G AO, B 0, A suavidad (1 - rugosidad)
"""
import os
import sys

import numpy as np
from PIL import Image


def gris(ruta, defecto, forma):
    if not os.path.exists(ruta):
        return np.full(forma, defecto, np.float32)
    im = Image.open(ruta)
    a = np.asarray(im.convert("L") if im.mode not in ("I;16", "I") else im, dtype=np.float32)
    return a / (65535.0 if a.max() > 255 else 255.0)


def empaquetar(origen, destino, receta):
    base = os.path.join(origen, receta, receta)
    color = Image.open(base + "_basecolor.png").convert("RGB")
    forma = (color.height, color.width)
    os.makedirs(os.path.join(destino, receta), exist_ok=True)
    salida = os.path.join(destino, receta, receta)
    color.putalpha(255)
    color.save(salida + "_albedo.png")
    Image.open(base + "_normal.png").convert("RGB").save(salida + "_normal.png")
    metal = gris(base + "_metallic.png", 0.0, forma)
    ao = gris(base + "_ao.png", 1.0, forma)
    rug = gris(base + "_roughness.png", 0.6, forma)
    m = np.stack([metal, ao, np.zeros(forma, np.float32), 1.0 - rug], axis=-1)
    Image.fromarray(np.clip(m * 255.0 + 0.5, 0, 255).astype(np.uint8), "RGBA").save(salida + "_mascara.png")
    print("ok", receta)


def main():
    if len(sys.argv) < 3:
        sys.exit(__doc__)
    origen, destino = sys.argv[1], sys.argv[2]
    recetas = sys.argv[3:] or sorted(d for d in os.listdir(origen) if os.path.isdir(os.path.join(origen, d)))
    for r in recetas:
        empaquetar(origen, destino, r)


if __name__ == "__main__":
    main()
