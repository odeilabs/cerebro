"""Junta las PNG de una carpeta en una hoja de contactos (para revisar secuencias de capturas).

    python Tools/hoja_contactos.py <carpeta> [--columnas 5] [--ancho 384] [--salida hoja.png]
"""
import argparse
import glob
import os

from PIL import Image, ImageDraw


def main():
    p = argparse.ArgumentParser()
    p.add_argument("carpeta")
    p.add_argument("--columnas", type=int, default=5)
    p.add_argument("--ancho", type=int, default=384)
    p.add_argument("--salida", default=None)
    a = p.parse_args()
    rutas = sorted(r for r in glob.glob(os.path.join(a.carpeta, "*.png")) if not os.path.basename(r).startswith("hoja"))
    if not rutas:
        raise SystemExit("sin imagenes")
    primera = Image.open(rutas[0])
    alto = int(a.ancho * primera.height / primera.width)
    filas = (len(rutas) + a.columnas - 1) // a.columnas
    hoja = Image.new("RGB", (a.columnas * a.ancho, filas * alto), (20, 20, 20))
    d = ImageDraw.Draw(hoja)
    for i, r in enumerate(rutas):
        im = Image.open(r).convert("RGB").resize((a.ancho, alto))
        x, y = (i % a.columnas) * a.ancho, (i // a.columnas) * alto
        hoja.paste(im, (x, y))
        d.text((x + 4, y + 4), os.path.splitext(os.path.basename(r))[0], fill=(255, 255, 0))
    salida = a.salida or os.path.join(a.carpeta, "hoja.png")
    hoja.save(salida)
    print(salida)


if __name__ == "__main__":
    main()
