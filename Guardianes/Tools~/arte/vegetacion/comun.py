"""Utilidades comunes: color, guardado PNG, normales, dilatacion push-pull y lienzo de composicion.

Convenciones:
  - Todo el color se trabaja en RGB LINEAL (float32) y se pasa a sRGB solo al guardar el albedo.
  - Coordenadas de imagen: x a la derecha, y hacia abajo (fila). "Arriba" en la imagen = +V en Unity.
  - Normales en espacio tangente, convencion OpenGL (verde = +Y = +V = arriba en la imagen).
  - Alturas en "pixeles" (misma unidad que el plano de la imagen) para que las pendientes sean reales.
"""
import colorsys
import os

import numpy as np
from PIL import Image
from scipy import ndimage as ndi

EPS = 1e-6


# ----------------------------------------------------------------------------------------------
# Color
# ----------------------------------------------------------------------------------------------
def srgb_a_lineal(c):
    c = np.asarray(c, np.float32)
    return np.where(c <= 0.04045, c / 12.92, ((c + 0.055) / 1.055) ** 2.4).astype(np.float32)


def lineal_a_srgb(c):
    c = np.clip(np.asarray(c, np.float32), 0.0, 1.0)
    return np.where(c <= 0.0031308, c * 12.92, 1.055 * np.power(c, 1.0 / 2.4) - 0.055).astype(np.float32)


def hexl(h):
    """'#rrggbb' (sRGB) -> RGB lineal."""
    h = h.lstrip("#")
    return srgb_a_lineal(np.array([int(h[i:i + 2], 16) for i in (0, 2, 4)], np.float32) / 255.0)


def variar(lin, rng, dh=0.0, ds=0.0, dv=0.0):
    """Varia un color lineal en HSV (sobre sRGB): dh en vueltas, ds/dv multiplicativos (+-)."""
    s = lineal_a_srgb(lin)
    h, sa, v = colorsys.rgb_to_hsv(*[float(x) for x in s])
    h = (h + rng.uniform(-dh, dh)) % 1.0
    sa = float(np.clip(sa * (1 + rng.uniform(-ds, ds)), 0, 1))
    v = float(np.clip(v * (1 + rng.uniform(-dv, dv)), 0, 1))
    return srgb_a_lineal(np.array(colorsys.hsv_to_rgb(h, sa, v), np.float32))


def mezclar(a, b, t):
    """Mezcla lineal; a y b pueden ser colores (3,) o mapas (...,3); t mapa (...)."""
    t = np.asarray(t, np.float32)
    if t.ndim and np.ndim(a) > 0 and np.shape(a)[-1] == 3 and t.shape[-1:] != (3,):
        t = t[..., None]
    return a + (b - a) * t


def luminancia(c):
    return c[..., 0] * 0.2126 + c[..., 1] * 0.7152 + c[..., 2] * 0.0722


# ----------------------------------------------------------------------------------------------
# Funciones de forma
# ----------------------------------------------------------------------------------------------
def suave(e0, e1, x):
    t = np.clip((np.asarray(x, np.float32) - e0) / (e1 - e0), 0.0, 1.0)
    return t * t * (3 - 2 * t)


def satura(x):
    return np.clip(x, 0.0, 1.0)


# ----------------------------------------------------------------------------------------------
# Guardado
# ----------------------------------------------------------------------------------------------
def _u8(x, rng=None):
    x = np.asarray(x, np.float32) * 255.0
    if rng is not None:  # dither triangular de +-1 LSB para evitar bandas en degradados suaves
        x = x + (rng.random(x.shape, np.float32) - rng.random(x.shape, np.float32))
    return np.clip(x + 0.5, 0, 255).astype(np.uint8)


def asegurar_carpeta(ruta):
    os.makedirs(os.path.dirname(ruta), exist_ok=True)


def guardar_albedo(ruta, color_lin, alfa=None, rng=None):
    asegurar_carpeta(ruta)
    rgb = _u8(lineal_a_srgb(color_lin), rng)
    if alfa is None:
        a = np.full(rgb.shape[:2], 255, np.uint8)
    else:
        a = _u8(alfa)
    Image.fromarray(np.dstack([rgb, a]), "RGBA").save(ruta, optimize=False, compress_level=6)


def guardar_normal(ruta, n, rng=None):
    asegurar_carpeta(ruta)
    Image.fromarray(_u8(n * 0.5 + 0.5, rng), "RGB").save(ruta, compress_level=6)


def guardar_mascara(ruta, ao, suavidad, rng=None):
    """Mascara HDRP Lit: R metalico=0, G AO, B detalle=0, A suavidad (todo lineal)."""
    asegurar_carpeta(ruta)
    z = np.zeros(ao.shape, np.uint8)
    Image.fromarray(np.dstack([z, _u8(ao, rng), z, _u8(suavidad, rng)]), "RGBA").save(ruta, compress_level=6)


def guardar_gris(ruta, g, bits=8, rng=None):
    asegurar_carpeta(ruta)
    if bits == 16:
        v = np.clip(np.asarray(g, np.float64) * 65535.0 + 0.5, 0, 65535).astype(np.uint16)
        Image.fromarray(v).save(ruta)
    else:
        Image.fromarray(_u8(g, rng), "L").save(ruta, compress_level=6)


# ----------------------------------------------------------------------------------------------
# Normales
# ----------------------------------------------------------------------------------------------
def normal_desde_altura(h, intensidad=1.0, envolver=False):
    """Normal tangente OpenGL a partir de una altura en pixeles.

    n = normalize(-dh/du, -dh/dv, 1) con u = columna (derecha) y v = arriba (= -fila)."""
    h = np.asarray(h, np.float32)
    if envolver:
        dx = (np.roll(h, -1, axis=1) - np.roll(h, 1, axis=1)) * 0.5
        dy = (np.roll(h, -1, axis=0) - np.roll(h, 1, axis=0)) * 0.5
    else:
        dy, dx = np.gradient(h)
    nx = -dx * intensidad
    ny = dy * intensidad  # dh/dv = -dh/dfila
    nz = np.ones_like(h)
    n = np.stack([nx, ny, nz], axis=-1)
    return (n / np.linalg.norm(n, axis=-1, keepdims=True)).astype(np.float32)


def normalizar_v(n):
    return (n / np.maximum(np.linalg.norm(n, axis=-1, keepdims=True), EPS)).astype(np.float32)


# ----------------------------------------------------------------------------------------------
# Dilatacion push-pull (rellena el RGB fuera del alfa para que los mips no hagan halos)
# ----------------------------------------------------------------------------------------------
def _reducir(x):
    h, w = x.shape[:2]
    if h % 2 or w % 2:
        pad = [(0, h % 2), (0, w % 2)] + [(0, 0)] * (x.ndim - 2)
        x = np.pad(x, pad, mode="edge")
    return 0.25 * (x[0::2, 0::2] + x[1::2, 0::2] + x[0::2, 1::2] + x[1::2, 1::2])


def _ampliar(x, forma):
    f = (forma[0] / x.shape[0], forma[1] / x.shape[1]) + (1,) * (x.ndim - 2)
    y = ndi.zoom(x, f, order=1, mode="nearest", grid_mode=True)
    return y[: forma[0], : forma[1]]


def push_pull(img, peso):
    """Rellena img (H,W,C) donde peso<1 con el color propagado desde los pixeles conocidos."""
    peso = np.clip(peso, 0, 1).astype(np.float32)
    piramide = []
    c = img * peso[..., None]
    w = peso
    while True:
        piramide.append((c, w))
        if min(c.shape[:2]) <= 2:
            break
        c = _reducir(c)
        w = _reducir(w)
    c, w = piramide[-1]
    acum = c.sum(axis=(0, 1)) / max(float(w.sum()), EPS)
    est = np.broadcast_to(acum, c.shape).astype(np.float32)
    for c, w in reversed(piramide):
        est = _ampliar(est, c.shape[:2]) if est.shape[:2] != c.shape[:2] else est
        propio = c / np.maximum(w, EPS)[..., None]
        k = np.clip(w * 4.0, 0, 1)[..., None]
        est = propio * k + est * (1 - k)
    # en el nivel fino, conservar exactamente el valor conocido
    k = peso[..., None]
    return (img * k + est * (1 - k)).astype(np.float32)


# ----------------------------------------------------------------------------------------------
# Lienzo de composicion para los atlas de hojas
# ----------------------------------------------------------------------------------------------
class Capa:
    """Un elemento ya rasterizado (hoja, peciolo, ramita...) en su recuadro (y0, x0)."""
    __slots__ = ("y0", "x0", "alfa", "color", "normal", "suav", "grosor", "z", "ao")

    def __init__(self, y0, x0, alfa, color, normal, suav, grosor, z, ao=None):
        self.y0, self.x0 = int(y0), int(x0)
        self.alfa = alfa.astype(np.float32)
        self.color = color.astype(np.float32)
        self.normal = normal.astype(np.float32)
        self.suav = np.broadcast_to(suav, alfa.shape).astype(np.float32)
        self.grosor = np.broadcast_to(grosor, alfa.shape).astype(np.float32)
        self.z = np.broadcast_to(z, alfa.shape).astype(np.float32)
        self.ao = np.ones_like(self.alfa) if ao is None else np.broadcast_to(ao, alfa.shape).astype(np.float32)


class Lienzo:
    """Composicion 'over' premultiplicada de capas (de atras hacia delante)."""

    def __init__(self, alto, ancho):
        self.alto, self.ancho = alto, ancho
        self.A = np.zeros((alto, ancho), np.float32)
        self.C = np.zeros((alto, ancho, 3), np.float32)
        self.N = np.zeros((alto, ancho, 3), np.float32)
        self.S = np.zeros((alto, ancho), np.float32)
        self.G = np.zeros((alto, ancho), np.float32)
        self.Z = np.zeros((alto, ancho), np.float32)
        self.O = np.zeros((alto, ancho), np.float32)

    def pintar(self, capa):
        h, w = capa.alfa.shape
        y0, x0 = capa.y0, capa.x0
        ya, xa = max(0, y0), max(0, x0)
        yb, xb = min(self.alto, y0 + h), min(self.ancho, x0 + w)
        if ya >= yb or xa >= xb:
            return
        src = (slice(ya - y0, yb - y0), slice(xa - x0, xb - x0))
        dst = (slice(ya, yb), slice(xa, xb))
        a = capa.alfa[src]
        inv = 1.0 - a
        a3, inv3 = a[..., None], inv[..., None]
        self.C[dst] = capa.color[src] * a3 + self.C[dst] * inv3
        self.N[dst] = capa.normal[src] * a3 + self.N[dst] * inv3
        self.S[dst] = capa.suav[src] * a + self.S[dst] * inv
        self.G[dst] = capa.grosor[src] * a + self.G[dst] * inv
        self.Z[dst] = capa.z[src] * a + self.Z[dst] * inv
        self.O[dst] = capa.ao[src] * a + self.O[dst] * inv
        self.A[dst] = a + self.A[dst] * inv

    def calibrar(self, factor, x0=0, y0=0, x1=None, y1=None):
        """Escala el albedo (lineal) de una region: ajusta el nivel medio a valores fisicos."""
        x1 = self.ancho if x1 is None else x1
        y1 = self.alto if y1 is None else y1
        self.C[y0:y1, x0:x1] *= factor

    def finalizar(self, escala_px=1.0, fuerza_ao=1.0, oscurecer_albedo=0.3, radios=None):
        """Desmultiplica, calcula el AO por solape a partir de la elevacion y dilata todos los mapas.

        Devuelve dict con color (lineal), alfa, normal, ao, suav, grosor (todo float32)."""
        A = self.A
        inv = 1.0 / np.maximum(A, EPS)
        color = self.C * inv[..., None]
        normal = normalizar_v(self.N + np.array([0, 0, 1e-4], np.float32))
        suav = self.S * inv
        grosor = self.G * inv
        z = self.Z * inv
        ao_el = np.where(A > EPS, self.O * inv, 1.0)

        # AO por solape: cuanto mas alta esta la geometria vecina que el pixel, mas oclusion
        if radios is None:
            radios = [(6.0, 0.020), (22.0, 0.010), (60.0, 0.004)]
        ocl = np.zeros_like(A)
        Zp = self.Z
        for r, k in radios:
            r_px = r * escala_px
            den = ndi.gaussian_filter(A, r_px, mode="constant")
            zm = ndi.gaussian_filter(Zp, r_px, mode="constant") / np.maximum(den, EPS)
            ocl += (k / escala_px) * np.clip(zm - z, 0, None) * np.clip(den * 1.5, 0, 1)
        ao = np.clip(1.0 - fuerza_ao * ocl, 0.25, 1.0) * ao_el
        color = color * (1.0 - oscurecer_albedo * (1.0 - ao))[..., None]

        # dilatacion de todo lo que no es alfa
        peso = np.clip(A * 8.0, 0, 1)
        pila = np.concatenate([color, normal, ao[..., None], suav[..., None], grosor[..., None]], axis=-1)
        pila = push_pull(pila, peso)
        return {
            "color": np.clip(pila[..., 0:3], 0, 1),
            "alfa": np.clip(A, 0, 1),
            "normal": normalizar_v(pila[..., 3:6]),
            "ao": np.clip(pila[..., 6], 0, 1),
            "suav": np.clip(pila[..., 7], 0, 1),
            "grosor": np.clip(pila[..., 8], 0, 1),
        }


def guardar_atlas_hojas(carpeta, nombre, mapas, rng):
    """Escribe los cuatro mapas de un atlas de hojas."""
    base = os.path.join(carpeta, nombre, nombre)
    guardar_albedo(base + "_albedo.png", mapas["color"], mapas["alfa"], rng)
    guardar_normal(base + "_normal.png", mapas["normal"], rng)
    guardar_mascara(base + "_mascara.png", mapas["ao"], mapas["suav"], rng)
    guardar_gris(base + "_grosor.png", mapas["grosor"], 8, rng)


def guardar_corteza(carpeta, nombre, mapas, rng):
    """Escribe los mapas de una corteza tileable (albedo RGB opaco, normal, mascara, altura 16 bits)."""
    base = os.path.join(carpeta, nombre, nombre)
    guardar_albedo(base + "_albedo.png", mapas["color"], mapas.get("alfa"), rng)
    guardar_normal(base + "_normal.png", mapas["normal"], rng)
    guardar_mascara(base + "_mascara.png", mapas["ao"], mapas["suav"], rng)
    if "altura" in mapas:
        guardar_gris(base + "_altura.png", mapas["altura"], 16)
