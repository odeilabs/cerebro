"""Hojas de revision (solo para comprobar a ojo): paneles reducidos y recortes 1:1.

Hojas: albedo sobre gris, normal, iluminado con alpha test 0.5, contraluz (albedo*(1-grosor) tintado),
AO, suavidad, grosor y alfa. Cortezas: mosaico 2x2 (para ver costuras) de albedo, normal e iluminado,
mas altura, AO y suavidad.
"""
import os

import numpy as np
from PIL import Image, ImageDraw

from comun import lineal_a_srgb

FONDO = np.array([0.18, 0.19, 0.2], np.float32)


def _img(rgb01):
    return Image.fromarray(np.clip(rgb01 * 255 + 0.5, 0, 255).astype(np.uint8), "RGB")


def _gris(g):
    g = np.clip(g, 0, 1)
    return np.repeat(g[..., None], 3, axis=-1)


def iluminar(color, normal, suav, ao, luz=(-0.45, 0.5, 0.74), ambiente=0.22):
    L = np.array(luz, np.float32)
    L /= np.linalg.norm(L)
    n = normal
    ndl = np.clip((n * L).sum(-1), 0, 1)
    Hh = L + np.array([0, 0, 1], np.float32)
    Hh /= np.linalg.norm(Hh)
    ndh = np.clip((n * Hh).sum(-1), 0, 1)
    expo = 2.0 ** (1.0 + 10.0 * suav)
    spec = ndh ** expo * (expo + 2) / 8.0 * 0.04 * suav
    lin = color * (ambiente * ao + 0.95 * ndl)[..., None] + spec[..., None]
    return lin


def _panel(arr01, lado):
    im = _img(arr01)
    w, h = im.size
    f = lado / max(w, h)
    return im.resize((max(1, int(w * f)), max(1, int(h * f))), Image.LANCZOS)


def _rotulo(im, texto):
    d = ImageDraw.Draw(im)
    d.rectangle([0, 0, 8 * len(texto) + 8, 16], fill=(0, 0, 0))
    d.text((4, 2), texto, fill=(255, 255, 255))
    return im


def _componer(paneles, columnas, lado):
    filas = (len(paneles) + columnas - 1) // columnas
    ws = [p.size[0] for p in paneles]
    hs = [p.size[1] for p in paneles]
    W = max(ws)
    Hh = max(hs)
    hoja = Image.new("RGB", (W * columnas, Hh * filas), (40, 40, 40))
    for i, p in enumerate(paneles):
        hoja.paste(p, ((i % columnas) * W, (i // columnas) * Hh))
    return hoja


def revision_hojas(carpeta, nombre, m, lado=640, recortes=None):
    os.makedirs(carpeta, exist_ok=True)
    a = m["alfa"][..., None]
    col = m["color"]
    srgb_sobre_gris = lineal_a_srgb(col) * a + lineal_a_srgb(FONDO) * (1 - a)
    corte = (m["alfa"] >= 0.5)[..., None]
    lit = iluminar(col, m["normal"], m["suav"], m["ao"])
    lit_srgb = lineal_a_srgb(lit * 1.6) * corte + lineal_a_srgb(FONDO * 0.6) * (1 - corte)
    tinte = np.array([1.0, 0.93, 0.75], np.float32)
    contra = col * (1 - m["grosor"])[..., None] * tinte * 7.0
    contra_srgb = lineal_a_srgb(contra) * corte + 0.02 * (1 - corte)
    normal = m["normal"] * 0.5 + 0.5
    paneles = [
        _rotulo(_panel(srgb_sobre_gris, lado), "albedo"),
        _rotulo(_panel(normal, lado), "normal"),
        _rotulo(_panel(lit_srgb, lado), "iluminado (alpha test)"),
        _rotulo(_panel(contra_srgb, lado), "contraluz"),
        _rotulo(_panel(_gris(m["ao"]), lado), "AO"),
        _rotulo(_panel(_gris(m["suav"]), lado), "suavidad"),
        _rotulo(_panel(_gris(m["grosor"]), lado), "grosor"),
        _rotulo(_panel(_gris(m["alfa"]), lado), "alfa"),
    ]
    _componer(paneles, 4, lado).save(os.path.join(carpeta, f"{nombre}_resumen.png"))
    # recortes 1:1 para ver el detalle
    H, W = m["alfa"].shape
    if recortes is None:
        recortes = [(H // 4, W // 4)]
    tam = min(700, H // 2, W // 2)
    trozos = []
    for (cy, cx) in recortes:
        y0 = int(np.clip(cy - tam // 2, 0, H - tam))
        x0 = int(np.clip(cx - tam // 2, 0, W - tam))
        sl = (slice(y0, y0 + tam), slice(x0, x0 + tam))
        trozos.append(_img(srgb_sobre_gris[sl]))
        trozos.append(_img(lit_srgb[sl]))
    _componer(trozos, 2, tam).save(os.path.join(carpeta, f"{nombre}_detalle.png"))


def revision_corteza(carpeta, nombre, m, lado=640):
    os.makedirs(carpeta, exist_ok=True)
    col = m["color"]
    lit = iluminar(col, m["normal"], m["suav"], m["ao"], luz=(-0.6, 0.35, 0.72), ambiente=0.25)

    def mosaico(x):
        return np.concatenate([np.concatenate([x, x], 1)] * 2, 0)

    paneles = [
        _rotulo(_panel(mosaico(lineal_a_srgb(col)), lado), "albedo 2x2"),
        _rotulo(_panel(mosaico(m["normal"] * 0.5 + 0.5), lado), "normal 2x2"),
        _rotulo(_panel(mosaico(lineal_a_srgb(lit * 1.5)), lado), "iluminado 2x2"),
        _rotulo(_panel(_gris(m.get("altura", m["ao"])), lado), "altura"),
        _rotulo(_panel(_gris(m["ao"]), lado), "AO"),
        _rotulo(_panel(_gris(m["suav"]), lado), "suavidad"),
    ]
    _componer(paneles, 3, lado).save(os.path.join(carpeta, f"{nombre}_resumen.png"))
    H, W = col.shape[:2]
    tam = min(800, H // 2)
    # recorte 1:1 sobre la esquina (cruza las costuras del mosaico)
    big_c = mosaico(lineal_a_srgb(col))
    big_l = mosaico(lineal_a_srgb(lit * 1.5))
    y0, x0 = H - tam // 2, W - tam // 2
    sl = (slice(y0, y0 + tam), slice(x0, x0 + tam))
    _componer([_img(big_c[sl]), _img(big_l[sl])], 2, tam).save(os.path.join(carpeta, f"{nombre}_detalle.png"))
