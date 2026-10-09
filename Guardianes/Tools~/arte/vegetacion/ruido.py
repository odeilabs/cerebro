"""Ruido procedural determinista para las texturas de vegetacion (solo numpy/scipy).

Todo el ruido 2D de aqui es periodico (tileable) por construccion:
  - espectral(): sintesis en el dominio de Fourier (ruido 1/f^p filtrado), periodico por la FFT.
  - celular():  Worley/Voronoi con cKDTree(boxsize=...), que trabaja en un toro.
  - muestrear(): lectura bilineal con envoltura, para leer un ruido en coordenadas arbitrarias
    (p. ej. coordenadas locales de una hoja) o para deformar el dominio sin romper la periodicidad.
"""
import numpy as np
from scipy import ndimage as ndi
from scipy.spatial import cKDTree


def normalizar(c):
    """Media 0, desviacion 1."""
    c = c - c.mean()
    d = c.std()
    return (c / d if d > 1e-12 else c).astype(np.float32)


def a_01(c):
    """Reescala a [0, 1]."""
    mn, mx = float(c.min()), float(c.max())
    return ((c - mn) / max(mx - mn, 1e-12)).astype(np.float32)


def espectral(forma, rng, p=1.0, fmin=1.0, fmax=None, estiramiento=1.0, angulo=0.0):
    """Ruido periodico por sintesis espectral.

    p: pendiente del espectro de amplitud (1 ~ fBm natural, >1 mas suave, <1 mas granulado).
    fmin/fmax: banda en ciclos por imagen (fmax con caida gaussiana).
    estiramiento: >1 alarga las formas en vertical (eje V), p. ej. vetas de corteza.
    angulo: gira la anisotropia (radianes)."""
    h, w = forma
    fy = np.fft.fftfreq(h) * h
    fx = np.fft.rfftfreq(w) * w
    FY, FX = np.meshgrid(fy, fx, indexing="ij")
    if angulo:
        ca, sa = np.cos(angulo), np.sin(angulo)
        FX, FY = FX * ca - FY * sa, FX * sa + FY * ca
    f = np.sqrt(FX ** 2 + (FY * estiramiento) ** 2)
    with np.errstate(divide="ignore"):
        amp = np.where(f > 0, f ** (-p), 0.0)
    # corte suave en la frecuencia minima para que no aparezcan escalones
    amp *= 1.0 - np.exp(-((f / max(fmin, 1e-6)) ** 4))
    if fmax is not None:
        amp *= np.exp(-((f / fmax) ** 2))
    fase = rng.standard_normal(amp.shape) + 1j * rng.standard_normal(amp.shape)
    campo = np.fft.irfft2(amp * fase, s=(h, w))
    return normalizar(campo)


def celular(forma, n, rng, estiramiento=1.0, k=2, puntos=None, devolver_puntos=False):
    """Worley periodico. Devuelve (F1, F2, id) en pixeles (F2 solo si k>=2).

    estiramiento>1 alarga las celdas en vertical."""
    h, w = forma
    hy = h / estiramiento
    if puntos is None:
        puntos = rng.random((n, 2)) * np.array([hy, w])
    arbol = cKDTree(puntos, boxsize=[hy, w])
    yy, xx = np.mgrid[0:h, 0:w].astype(np.float32)
    q = np.stack([((yy + 0.5) / estiramiento).ravel() % hy, (xx + 0.5).ravel() % w], axis=1)
    d, i = arbol.query(q, k=k, workers=-1)
    if k == 1:
        d = d[:, None]
        i = i[:, None]
    res = [d[:, j].reshape(h, w).astype(np.float32) for j in range(k)]
    ids = i[:, 0].reshape(h, w)
    salida = tuple(res) + (ids,)
    if devolver_puntos:
        salida = salida + (puntos,)
    return salida


def muestrear(tex, y, x, orden=1):
    """Lee la textura periodica tex en las coordenadas (y, x) en pixeles de la textura, con envoltura."""
    coords = np.stack([np.asarray(y, np.float32) - 0.5, np.asarray(x, np.float32) - 0.5])
    return ndi.map_coordinates(tex, coords, order=orden, mode="grid-wrap").astype(np.float32)


def deformar(campo, dy, dx, orden=1):
    """Deformacion de dominio periodica: campo(y + dy, x + dx)."""
    h, w = campo.shape
    yy, xx = np.mgrid[0:h, 0:w].astype(np.float32)
    return muestrear(campo, yy + 0.5 + dy, xx + 0.5 + dx, orden)


def ruido1d(n, rng, suavidad=8.0, periodico=False):
    """Ruido 1D suave (media 0, desv 1) de longitud n; suavidad = escala en muestras."""
    if periodico:
        f = np.fft.rfftfreq(n) * n
        amp = np.exp(-((f * suavidad / n * 6.0) ** 2))
        amp[0] = 0
        fase = rng.standard_normal(amp.shape) + 1j * rng.standard_normal(amp.shape)
        r = np.fft.irfft(amp * fase, n=n)
    else:
        extra = int(4 * suavidad) + 2
        r = rng.standard_normal(n + 2 * extra)
        r = ndi.gaussian_filter1d(r, suavidad)[extra:-extra]
    r = r - r.mean()
    return (r / max(r.std(), 1e-9)).astype(np.float32)


class BancoRuido:
    """Coleccion de ruidos periodicos precalculados para leer en coordenadas locales de cada elemento.

    Evita calcular ruido por cada hoja: cada hoja lee el banco con un desplazamiento aleatorio."""

    def __init__(self, rng, lado=512):
        self.lado = lado
        f = (lado, lado)
        self.fbm = espectral(f, rng, p=1.15, fmin=2.0)              # manchas suaves multiescala
        self.fbm_fino = espectral(f, rng, p=0.75, fmin=8.0)         # granulado medio
        self.grano = espectral(f, rng, p=0.2, fmin=40.0)            # micro grano
        self.suave = espectral(f, rng, p=1.6, fmin=1.0, fmax=24.0)  # ondulaciones grandes
        # red de venas terciarias: bordes de Voronoi (F2-F1) y valor por celda (areolas)
        f1, f2, ids = celular(f, 2600, rng)
        self.red = (f2 - f1).astype(np.float32)
        self.celda = rng.random(2600).astype(np.float32)[ids]
        f1b, f2b, idsb = celular(f, 9000, rng)
        self.red_fina = (f2b - f1b).astype(np.float32)
        self.celda_fina = rng.random(9000).astype(np.float32)[idsb]
        # ruido estirado (fibras) para tallos y peciolos
        self.fibra = espectral(f, rng, p=0.9, fmin=3.0, estiramiento=6.0)

    def leer(self, nombre, y, x, escala=1.0, desp=(0.0, 0.0)):
        """Lee el ruido 'nombre' en (y, x)/escala + desp (pixeles del banco)."""
        tex = getattr(self, nombre)
        return muestrear(tex, np.asarray(y) / escala + desp[0], np.asarray(x) / escala + desp[1])
