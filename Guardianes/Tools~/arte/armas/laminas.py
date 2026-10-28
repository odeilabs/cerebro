"""Las laminas de referencia de AssetsCreator (solo se leen): silueta, caja, contornos y rejillas.

    python laminas.py rejilla <lamina> <salida.png> [--paso 50] [--recorte x0,y0,x1,y1] [--zoom 2]
    python laminas.py silueta <lamina> <salida.png> [--umbral 22]
    python laminas.py contornos <lamina> <salida.json> [--umbral 22] [--tolerancia 1.5]

<lamina> es una ruta a un PNG o "<carpeta>/<vista>" dentro de las laminas de armas de AssetsCreator
(p. ej. 030-glock-17/lateral). Las vistas son lateral, tres_cuartos, detalle_empunadura y detalle_boca.

- rejilla: la lamina con una rejilla en pixeles rotulada, para leer coordenadas a ojo (con --recorte y
  --zoom, un trozo ampliado con rejilla fina). Asi se apuntan los perfiles del modelo en pixeles.
- silueta: la mascara del arma (fondo gris de estudio fuera) y su caja en pixeles.
- contornos: el contorno exterior y los huecos de la silueta, simplificados (Douglas-Peucker), en
  pixeles de la lamina; los lee el kit de Blender (kit_armas.Lamina) para extruir la silueta real.

Las laminas son imagenes generadas, no planos: pueden venir estiradas (la de la Glock lo esta un 18 % a
lo largo). Por eso el kit calibra los pixeles con dos escalas (largo y alto reales), no con una.
"""
import argparse
import glob
import json
import os
import sys

import numpy as np
from PIL import Image, ImageDraw, ImageFont

AQUI = os.path.dirname(os.path.abspath(__file__))
VISTAS = ("lateral", "tres_cuartos", "detalle_empunadura", "detalle_boca")


# ------------------------------------------------------------------ rutas
def assetscreator():
    """La carpeta AssetsCreatorWin: %ASSETSCREATOR% o subiendo desde aqui hasta encontrar
    AssetsCreator/AssetsCreatorWin/creador.py (como ConstructorHumanos)."""
    env = os.environ.get("ASSETSCREATOR")
    if env:
        for c in (env, os.path.join(env, "AssetsCreatorWin")):
            if os.path.isfile(os.path.join(c, "creador.py")):
                return os.path.abspath(c)
    d = AQUI
    while True:
        c = os.path.join(d, "AssetsCreator", "AssetsCreatorWin")
        if os.path.isfile(os.path.join(c, "creador.py")):
            return c
        padre = os.path.dirname(d)
        if padre == d:
            raise SystemExit("No encuentro AssetsCreator\\AssetsCreatorWin (pon %ASSETSCREATOR%)")
        d = padre


def carpeta_laminas():
    return os.path.join(assetscreator(), "referencias", "armas", "armas de fuego")


def ruta_lamina(lamina, vista=None):
    """Ruta del PNG de una lamina: una ruta directa, o la carpeta de un arma (030-glock-17, o solo 030)
    y la vista."""
    if vista is None and os.path.isfile(lamina):
        return lamina
    if vista is None and "/" in lamina.replace("\\", "/"):
        lamina, vista = lamina.replace("\\", "/").rsplit("/", 1)
    base = carpeta_laminas()
    candidatas = [d for d in sorted(os.listdir(base)) if os.path.isdir(os.path.join(base, d))
                  and (d == lamina or d.startswith(lamina + "-"))]
    if not candidatas:
        raise SystemExit(f"No hay lamina '{lamina}' en {base}")
    pngs = glob.glob(os.path.join(base, candidatas[0], f"*_{vista}.png"))
    if not pngs:
        raise SystemExit(f"La lamina {candidatas[0]} no tiene la vista '{vista}'")
    return pngs[0]


def fuente(tam=14):
    for f in ("arial.ttf", "C:/Windows/Fonts/arial.ttf", "DejaVuSans.ttf"):
        try:
            return ImageFont.truetype(f, tam)
        except OSError:
            pass
    return ImageFont.load_default()


# ------------------------------------------------------------------ silueta
def fondo_estimado(rgb, borde=24):
    """El fondo de estudio (gris con degradado suave): un plano cuadratico ajustado por minimos cuadrados
    a los pixeles del marco de la imagen, canal a canal."""
    h, w, _ = rgb.shape
    yy, xx = np.mgrid[0:h, 0:w]
    marco = np.zeros((h, w), bool)
    marco[:borde, :] = marco[-borde:, :] = marco[:, :borde] = marco[:, -borde:] = True
    x = xx[marco] / w - 0.5
    y = yy[marco] / h - 0.5
    A = np.stack([np.ones_like(x), x, y, x * x, y * y, x * y], 1)
    X = xx / w - 0.5
    Y = yy / h - 0.5
    B = np.stack([np.ones_like(X), X, Y, X * X, Y * Y, X * Y], -1)
    fondo = np.empty_like(rgb)
    for c in range(3):
        coef, *_ = np.linalg.lstsq(A, rgb[..., c][marco], rcond=None)
        fondo[..., c] = B @ coef
    return fondo


def silueta(ruta, umbral=22.0, hueco_min=0.0004, pieza_min=0.002):
    """Mascara booleana del arma: lo que se aparta del fondo estimado mas de `umbral` (0..255, distancia
    de color). Rellena los huecos pequenos (brillos dentro del arma, < hueco_min del area) y quita las
    motas (< pieza_min). Los huecos grandes (el del guardamonte) se quedan."""
    from scipy import ndimage
    rgb = np.asarray(Image.open(ruta).convert("RGB"), np.float32)
    dif = np.sqrt(((rgb - fondo_estimado(rgb)) ** 2).sum(-1))
    m = dif > umbral
    m = ndimage.binary_closing(m, np.ones((3, 3), bool), iterations=2)
    area = m.size
    lab, n = ndimage.label(m)
    if n:
        tam = ndimage.sum(m, lab, range(1, n + 1))
        m = np.isin(lab, 1 + np.nonzero(tam >= pieza_min * area)[0])
    hueco = ~m
    lab, n = ndimage.label(hueco)
    if n:
        tam = ndimage.sum(hueco, lab, range(1, n + 1))
        borde = set(np.unique(np.concatenate([lab[0], lab[-1], lab[:, 0], lab[:, -1]])))
        for i in range(1, n + 1):
            if i not in borde and tam[i - 1] < hueco_min * area:
                m[lab == i] = True
    return m


def caja(mascara):
    """(x0, y0, x1, y1) en pixeles, inclusivos."""
    ys, xs = np.nonzero(mascara)
    return int(xs.min()), int(ys.min()), int(xs.max()), int(ys.max())


def contornos(mascara, tolerancia=1.5, suavizado=2.0):
    """-> {"exterior": [[x, y], ...], "huecos": [[[x, y], ...], ...]} en pixeles (el borde del pixel),
    simplificados con Douglas-Peucker a `tolerancia` px. El exterior es el de mas area. `suavizado` (sigma en px)
    desenfoca la mascara antes de trazar: se lleva los dientes del picado de una empunadura (1 mm) y deja las
    formas (los huecos de los dedos, de 5 mm)."""
    from scipy import ndimage
    from skimage import measure
    m = np.pad(mascara.astype(np.float32), 1)
    if suavizado > 0:
        m = ndimage.gaussian_filter(m, suavizado)
    cs = measure.find_contours(m, 0.5)
    salida = []
    for c in cs:
        c = measure.approximate_polygon(c, tolerance=tolerancia)
        pts = [[round(float(p[1]) - 1.0, 2), round(float(p[0]) - 1.0, 2)] for p in c]
        if len(pts) > 3 and pts[0] == pts[-1]:
            pts = pts[:-1]
        if len(pts) >= 3:
            salida.append(pts)
    if not salida:
        return {"exterior": [], "huecos": []}

    def area(p):
        a = np.asarray(p)
        return 0.5 * abs(np.dot(a[:, 0], np.roll(a[:, 1], 1)) - np.dot(a[:, 1], np.roll(a[:, 0], 1)))

    salida.sort(key=area, reverse=True)
    return {"exterior": salida[0], "huecos": [p for p in salida[1:] if area(p) > 30.0]}


def datos_lamina(ruta, umbral=22.0, tolerancia=1.5, suavizado=2.0):
    """Todo lo que el kit necesita de una vista: tamano, caja y contornos (en pixeles)."""
    m = silueta(ruta, umbral)
    im = Image.open(ruta)
    d = {"ruta": ruta, "ancho": im.width, "alto": im.height, "umbral": umbral, "suavizado": suavizado,
         "caja": caja(m)}
    d.update(contornos(m, tolerancia, suavizado))
    return d


# ------------------------------------------------------------------ rejilla
def rejilla(ruta, salida, paso=50, recorte=None, zoom=1, contorno=True, umbral=22.0):
    """La lamina con una rejilla rotulada en pixeles de la lamina (y el contorno de la silueta en cian)."""
    im = Image.open(ruta).convert("RGB")
    x0, y0, x1, y1 = recorte or (0, 0, im.width, im.height)
    trozo = im.crop((x0, y0, x1, y1)).resize(((x1 - x0) * zoom, (y1 - y0) * zoom), Image.LANCZOS)
    d = ImageDraw.Draw(trozo)
    if contorno:
        c = contornos(silueta(ruta, umbral), 1.0)
        for p in [c["exterior"]] + c["huecos"]:
            q = [((x - x0) * zoom, (y - y0) * zoom) for x, y in p]
            if len(q) > 2:
                d.line(q + [q[0]], fill=(0, 200, 230), width=1)
    f = fuente(12)
    for gx in range((x0 // paso) * paso, x1 + 1, paso):
        if gx < x0:
            continue
        X = (gx - x0) * zoom
        fuerte = gx % (paso * 5) == 0
        d.line([(X, 0), (X, trozo.height)], fill=(255, 40, 40) if fuerte else (255, 150, 150), width=1)
        d.text((X + 2, 2), str(gx), fill=(200, 0, 0), font=f)
    for gy in range((y0 // paso) * paso, y1 + 1, paso):
        if gy < y0:
            continue
        Y = (gy - y0) * zoom
        fuerte = gy % (paso * 5) == 0
        d.line([(0, Y), (trozo.width, Y)], fill=(40, 40, 255) if fuerte else (150, 150, 255), width=1)
        d.text((2, Y + 2), str(gy), fill=(0, 0, 200), font=f)
    trozo.save(salida)
    return salida


def main():
    p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    p.add_argument("accion", choices=("rejilla", "silueta", "contornos"))
    p.add_argument("lamina")
    p.add_argument("salida")
    p.add_argument("--paso", type=int, default=50)
    p.add_argument("--recorte", default=None, help="x0,y0,x1,y1 en pixeles de la lamina")
    p.add_argument("--zoom", type=int, default=1)
    p.add_argument("--umbral", type=float, default=22.0)
    p.add_argument("--tolerancia", type=float, default=1.5)
    p.add_argument("--suavizado", type=float, default=2.0)
    a = p.parse_args()
    ruta = ruta_lamina(a.lamina)
    if a.accion == "rejilla":
        rec = tuple(int(v) for v in a.recorte.split(",")) if a.recorte else None
        print(rejilla(ruta, a.salida, a.paso, rec, a.zoom, umbral=a.umbral))
    elif a.accion == "silueta":
        m = silueta(ruta, a.umbral)
        Image.fromarray((m * 255).astype(np.uint8)).save(a.salida)
        print("caja", caja(m), "->", a.salida)
    else:
        d = datos_lamina(ruta, a.umbral, a.tolerancia, a.suavizado)
        with open(a.salida, "w", encoding="utf-8") as f:
            json.dump(d, f, indent=1)
        print(f"caja {d['caja']}, exterior {len(d['exterior'])} puntos, {len(d['huecos'])} huecos -> {a.salida}")


if __name__ == "__main__":
    sys.exit(main())
