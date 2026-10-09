"""Nucleo comun de las capas de terreno: ruido periodico, Voronoi toroidal, estampado de
elementos con envoltura, mapas derivados (normal, AO) y exportacion.

Todo es periodico (el tile es un toro): FFT para el ruido, cKDTree con boxsize para Voronoi,
np.roll / mode='wrap' para filtros y estampado con indices modulo N. Asi cualquier mapa
encaja sin costuras al repetirse.

Unidades: las alturas se trabajan en METROS y los tamanos de elemento en metros; Ctx convierte
a pixeles segun el tamano fisico del tile (tam_m / res).
"""
import math
import zlib

import numpy as np
import scipy.fft as sfft
from scipy import ndimage
from scipy.spatial import cKDTree

F32 = np.float32


# ---------------------------------------------------------------------------------------------
# Contexto
# ---------------------------------------------------------------------------------------------
class Ctx:
    """Contexto de una capa: resolucion, tamano fisico del tile y semillas deterministas."""

    def __init__(self, nombre, res, tam_m, semilla):
        self.nombre = nombre
        self.n = int(res)
        self.tam = float(tam_m)
        self.texel = self.tam / self.n
        self.semilla = int(semilla)

    def rng(self, etiqueta):
        """Generador independiente por componente: cambiar una parte no altera las demas."""
        return np.random.default_rng(
            [self.semilla, zlib.crc32(self.nombre.encode()), zlib.crc32(etiqueta.encode())])

    def px(self, metros):
        return metros / self.texel

    def ciclos(self, metros):
        """Ciclos por tile de una longitud de onda dada en metros."""
        return self.tam / metros


# ---------------------------------------------------------------------------------------------
# Utilidades basicas
# ---------------------------------------------------------------------------------------------
def suave(x, a, b):
    """smoothstep(a, b, x)."""
    t = np.clip((x - a) / (b - a), 0.0, 1.0)
    return t * t * (3.0 - 2.0 * t)


def mezclar(a, b, t):
    return a + (b - a) * t


def normalizar(x, p_bajo=0.5, p_alto=99.5):
    lo, hi = np.percentile(x, [p_bajo, p_alto])
    return np.clip((x - lo) / max(hi - lo, 1e-12), 0.0, 1.0).astype(F32)


def estandarizar(x):
    x = x - x.mean()
    return (x / max(x.std(), 1e-12)).astype(F32)


def srgb_a_lineal(c):
    c = np.asarray(c, dtype=np.float64)
    return np.where(c <= 0.04045, c / 12.92, ((c + 0.055) / 1.055) ** 2.4)


def lineal_a_srgb(c):
    c = np.clip(c, 0.0, 1.0)
    return np.where(c <= 0.0031308, c * 12.92, 1.055 * np.power(c, 1.0 / 2.4) - 0.055)


def col(r, g, b):
    """Color sRGB 0-255 -> lineal (float32, 3)."""
    return srgb_a_lineal(np.array([r, g, b], dtype=np.float64) / 255.0).astype(F32)


def luminancia(rgb):
    return rgb[..., 0] * 0.2126 + rgb[..., 1] * 0.7152 + rgb[..., 2] * 0.0722


def desenfocar(img, sigma):
    """Gaussiano periodico (mode wrap). Acepta 2D o HxWxC (no mezcla canales)."""
    if sigma <= 0:
        return img
    if img.ndim == 3:
        return ndimage.gaussian_filter(img, (sigma, sigma, 0), mode="wrap").astype(F32)
    return ndimage.gaussian_filter(img, sigma, mode="wrap").astype(F32)


def desenfocar_fft(img, sigma):
    """Gaussiano periodico por FFT (rapido para sigma grandes)."""
    n = img.shape[0]
    fy, fx = _frecuencias(n)
    g = np.exp(-2.0 * (math.pi * sigma / n) ** 2 * (fy * fy + fx * fx)).astype(F32)
    if img.ndim == 3:
        return np.stack([desenfocar_fft(img[..., i], sigma) for i in range(img.shape[2])], -1)
    return sfft.irfft2(sfft.rfft2(img, workers=-1) * g, s=img.shape, workers=-1).astype(F32)


def rodar(img, dy, dx):
    return np.roll(img, (dy, dx), axis=(0, 1))


# ---------------------------------------------------------------------------------------------
# Ruido periodico por sintesis espectral
# ---------------------------------------------------------------------------------------------
_CACHE_F = {}


def _frecuencias(n):
    if n not in _CACHE_F:
        fy = (np.fft.fftfreq(n) * n).astype(F32)[:, None]
        fx = (np.fft.rfftfreq(n) * n).astype(F32)[None, :]
        _CACHE_F[n] = (fy, fx)
    return _CACHE_F[n]


def ruido(n, rng, f_min=1.0, f_max=None, beta=2.0, anis=None, normalizado=True):
    """Ruido fractal periodico. Frecuencias en ciclos por tile.

    beta: pendiente del espectro de potencia (P ~ f^-beta): 2-3 suave/browniano, 1 rosa, 0 blanco.
    anis: (factor, angulo_rad): alarga las formas `factor` veces en la direccion `angulo`.
    Devuelve media 0 y desviacion 1 (si normalizado).
    """
    fy, fx = _frecuencias(n)
    if anis is not None:
        fac, ang = anis
        c, s = math.cos(ang), math.sin(ang)
        fpar = fx * c + fy * s
        fper = -fx * s + fy * c
        f = np.sqrt((fpar * fac) ** 2 + fper ** 2)
    else:
        f = np.sqrt(fx * fx + fy * fy)
    f = np.maximum(f, 1e-6)
    amp = f ** (-beta * 0.5)
    amp = amp / (1.0 + np.minimum(f_min / f, 1e5) ** 6)
    if f_max is not None:
        amp = amp / (1.0 + np.minimum(f / f_max, 1e5) ** 6)
    amp[0, 0] = 0.0
    blanco = rng.standard_normal((n, n)).astype(F32)
    out = sfft.irfft2(sfft.rfft2(blanco, workers=-1) * amp.astype(F32), s=(n, n), workers=-1)
    return estandarizar(out) if normalizado else out.astype(F32)


def ruido_m(ctx, rng, long_max_m, long_min_m=None, beta=2.0, anis=None):
    """Ruido fractal definido por longitudes de onda en metros (mayor y menor)."""
    f_min = max(ctx.ciclos(long_max_m), 1.0)
    f_max = None if long_min_m is None else ctx.ciclos(long_min_m)
    return ruido(ctx.n, rng, f_min, f_max, beta, anis)


def fbm_crestas(ctx, rng, long_max_m, long_min_m, octavas=5, ganancia=0.5):
    """Ruido de crestas (ridged) periodico: suma de 1-|ruido banda| por octavas."""
    out = np.zeros((ctx.n, ctx.n), F32)
    amp, tot = 1.0, 0.0
    lm = long_max_m
    for _ in range(octavas):
        if lm < long_min_m * 0.999:
            break
        b = ruido_m(ctx, rng, lm, lm * 0.5, beta=0.0)
        out += amp * (1.0 - np.abs(np.tanh(b * 0.8)))
        tot += amp
        amp *= ganancia
        lm *= 0.5
    return (out / tot).astype(F32)


def deformar(img, dy, dx, orden=1):
    """Muestrea img en (y+dy, x+dx) con envoltura periodica (domain warping)."""
    n = img.shape[0]
    yy, xx = np.mgrid[0:n, 0:n].astype(F32)
    coords = [yy + dy, xx + dx]
    if img.ndim == 3:
        return np.stack([ndimage.map_coordinates(img[..., i], coords, order=orden, mode="grid-wrap")
                         for i in range(img.shape[2])], -1).astype(F32)
    return ndimage.map_coordinates(img, coords, order=orden, mode="grid-wrap").astype(F32)


def muestrear(img, y, x, orden=1):
    """Muestreo bilineal periodico en coordenadas arbitrarias (arrays)."""
    return ndimage.map_coordinates(img, [y, x], order=orden, mode="grid-wrap").astype(F32)


def tinte_macro(ctx, rng, amplitud=0.08, long_m=None):
    """Variacion de tono/brillo a gran escala (RGB multiplicativo, media 1) para disimular la
    repeticion. Mezcla varias frecuencias para que ninguna mancha domine el tile."""
    long_m = long_m or ctx.tam / 2.5
    a = ruido_m(ctx, rng, long_m, long_m / 6, beta=2.2)
    b = ruido_m(ctx, rng, long_m / 2, long_m / 10, beta=2.0)
    c = ruido_m(ctx, rng, long_m, long_m / 6, beta=2.2)
    brillo = 1.0 + amplitud * (0.75 * a + 0.45 * b)
    calor = amplitud * 0.5 * c
    t = np.stack([brillo * (1 + calor), brillo, brillo * (1 - calor)], -1)
    return t.astype(F32)


# ---------------------------------------------------------------------------------------------
# Voronoi toroidal
# ---------------------------------------------------------------------------------------------
def voronoi(n, puntos, warp=None, k=2):
    """Voronoi periodico. puntos: (M,2) en pixeles (y, x). warp: (dy, dx) para bordes irregulares.

    Devuelve dict con f1, f2 (distancias en px), id1, id2 y borde (distancia euclidea exacta al
    bisector entre las dos semillas mas cercanas, en px).
    """
    puntos = np.mod(np.asarray(puntos, np.float64), n)
    puntos = np.minimum(puntos, n - 1e-6)
    arbol = cKDTree(puntos, boxsize=n)
    yy, xx = np.mgrid[0:n, 0:n].astype(np.float64)
    if warp is not None:
        yy = yy + warp[0]
        xx = xx + warp[1]
    q = np.stack([np.mod(yy, n).ravel(), np.mod(xx, n).ravel()], 1)
    q = np.minimum(q, n - 1e-6)
    d, i = arbol.query(q, k=list(range(1, k + 1)), workers=-1)
    f1 = d[:, 0].reshape(n, n).astype(F32)
    id1 = i[:, 0].reshape(n, n)
    res = {"f1": f1, "id1": id1}
    if k >= 2:
        f2 = d[:, 1].reshape(n, n).astype(F32)
        id2 = i[:, 1].reshape(n, n)
        # distancia exacta al bisector: (d2^2 - d1^2) / (2 |s2 - s1|) con separacion periodica
        sep = puntos[id2.ravel()] - puntos[id1.ravel()]
        sep = sep - n * np.round(sep / n)
        dist_s = np.sqrt((sep ** 2).sum(1)).reshape(n, n)
        borde = (f2.astype(np.float64) ** 2 - f1.astype(np.float64) ** 2) / (2 * np.maximum(dist_s, 1e-6))
        res.update(f2=f2, id2=id2, borde=borde.astype(F32))
    return res


def puntos_poisson(n, rng, distancia_min, max_puntos=None, intentos=30):
    """Muestreo de Poisson-disk periodico simple (rechazo con rejilla). Devuelve (M,2) y,x px."""
    celda = distancia_min / math.sqrt(2)
    g = int(math.ceil(n / celda))
    celda = n / g
    rejilla = -np.ones((g, g), dtype=np.int64)
    pts = []
    # estrategia: lanzar muchos candidatos aleatorios por lotes (rapido y suficiente aqui)
    total = int((n / distancia_min) ** 2 * 1.6) if max_puntos is None else max_puntos * 4
    cand = rng.random((total * 2, 2)) * n
    r2 = distancia_min ** 2
    for y, x in cand:
        gy, gx = int(y / celda) % g, int(x / celda) % g
        ok = True
        for oy in (-2, -1, 0, 1, 2):
            for ox in (-2, -1, 0, 1, 2):
                j = rejilla[(gy + oy) % g, (gx + ox) % g]
                if j >= 0:
                    py, px_ = pts[j]
                    dy = abs(py - y)
                    dx = abs(px_ - x)
                    dy = min(dy, n - dy)
                    dx = min(dx, n - dx)
                    if dy * dy + dx * dx < r2:
                        ok = False
                        break
            if not ok:
                break
        if ok:
            rejilla[gy, gx] = len(pts)
            pts.append((y, x))
            if max_puntos is not None and len(pts) >= max_puntos:
                break
    return np.array(pts, dtype=np.float64)


# ---------------------------------------------------------------------------------------------
# Lienzo con estampado periodico de elementos
# ---------------------------------------------------------------------------------------------
def ventana(n, y0, x0, h, w):
    """Indices de una ventana h x w empezando en (y0, x0) con envoltura (slices si no cruza)."""
    if 0 <= y0 and y0 + h <= n and 0 <= x0 and x0 + w <= n:
        return (slice(y0, y0 + h), slice(x0, x0 + w))
    filas = np.arange(y0, y0 + h) % n
    cols = np.arange(x0, x0 + w) % n
    return np.ix_(filas, cols)


class Lienzo:
    """Altura (m), albedo lineal, suavidad y canales extra (humedad, etc.), todos periodicos."""

    def __init__(self, n, extras=()):
        self.n = n
        self.altura = np.zeros((n, n), F32)
        self.albedo = np.zeros((n, n, 3), F32)
        self.suavidad = np.zeros((n, n), F32)
        self.extra = {k: np.zeros((n, n), F32) for k in extras}

    def componer(self, idx, alfa, altura, albedo=None, suavidad=None, extra=None):
        """Composicion 'sobre' (el elemento tapa lo de debajo segun alfa)."""
        a = alfa
        h0 = self.altura[idx]
        self.altura[idx] = h0 + (altura - h0) * a
        if albedo is not None:
            c0 = self.albedo[idx]
            self.albedo[idx] = c0 + (albedo - c0) * a[..., None]
        if suavidad is not None:
            s0 = self.suavidad[idx]
            self.suavidad[idx] = s0 + (suavidad - s0) * a
        if extra:
            for k, v in extra.items():
                e0 = self.extra[k][idx]
                self.extra[k][idx] = e0 + (v - e0) * a

    def componer_max(self, idx, alfa, altura, albedo=None, suavidad=None, extra=None, suav_borde=0.0):
        """Composicion 'max' (tile sampler): el elemento solo aparece donde sobresale."""
        h0 = self.altura[idx]
        if suav_borde > 0:
            gana = np.clip((altura - h0) / suav_borde + 0.5, 0.0, 1.0)
        else:
            gana = (altura > h0).astype(F32)
        a = alfa * gana
        self.componer(idx, a, np.maximum(altura, h0) if suav_borde > 0 else altura, albedo, suavidad, extra)
        return a


class Sprites:
    """Biblioteca de sprites pequenos para salpicar miles de elementos vectorizadamente.

    Cada sprite: alfa (cobertura 0-1), altura (m sobre la base), y canales opcionales por pixel
    (p.ej. 't' = posicion a lo largo de la brizna). Se guardan solo los pixeles con alfa > 0.
    """

    def __init__(self):
        self._dy, self._dx, self._a, self._h, self._extra = [], [], [], [], {}
        self.inicio, self.cuenta = [], []
        self._total = 0

    def anadir(self, alfa, altura, extra=None, centro=None):
        h, w = alfa.shape
        cy, cx = (h // 2, w // 2) if centro is None else centro
        m = alfa > 1e-3
        ys, xs = np.nonzero(m)
        self.inicio.append(self._total)
        self.cuenta.append(len(ys))
        self._total += len(ys)
        self._dy.append((ys - cy).astype(np.int32))
        self._dx.append((xs - cx).astype(np.int32))
        self._a.append(alfa[m].astype(F32))
        self._h.append(altura[m].astype(F32))
        for k, v in (extra or {}).items():
            self._extra.setdefault(k, []).append(v[m].astype(F32))
        return len(self.cuenta) - 1

    def cerrar(self):
        self.dy = np.concatenate(self._dy)
        self.dx = np.concatenate(self._dx)
        self.a = np.concatenate(self._a)
        self.h = np.concatenate(self._h)
        self.extra = {k: np.concatenate(v) for k, v in self._extra.items()}
        self.inicio = np.array(self.inicio, np.int64)
        self.cuenta = np.array(self.cuenta, np.int64)
        return self


def sprite_supermuestreado(lado, funcion, ss=4):
    """Rasteriza un sprite pequeno con supermuestreo (antialias de elementos de 1-2 px).

    funcion(u, v) -> (cobertura 0/1, altura, extra) en coordenadas centradas en px (float).
    Devuelve alfa (media), altura (maximo donde hay cobertura) y extra (media ponderada).
    """
    m = lado * ss
    c = (m - 1) / 2.0
    yy, xx = np.mgrid[0:m, 0:m].astype(F32)
    u = (xx - c) / ss
    v = (yy - c) / ss
    cob, alt, ext = funcion(u, v)
    cob = cob.astype(F32)
    alt = (alt * cob).astype(F32)
    ext = (ext * cob).astype(F32)
    a = cob.reshape(lado, ss, lado, ss).mean((1, 3))
    hmax = alt.reshape(lado, ss, lado, ss).max((1, 3))
    e = ext.reshape(lado, ss, lado, ss).sum((1, 3)) / np.maximum(cob.reshape(lado, ss, lado, ss).sum((1, 3)), 1e-6)
    return a, hmax, e


def salpicar(lienzo, sprites, sid, y, x, base, escala_h=None, color=None, suavidad=None,
             extra=None, modo="max", lote=6_000_000, color_fn=None):
    """Estampa muchas instancias de sprites con composicion por maximo de altura.

    sid, y, x, base: arrays por instancia (indice de sprite, posicion px, altura base m).
    color: (M,3) lineal por instancia; color_fn(inst, g) -> (T,3) permite variar por pixel
    usando los canales extra del sprite. suavidad: (M,) por instancia.
    modo 'max': gana el mas alto por pixel; el borde se mezcla con el alfa del sprite.
    """
    n = lienzo.n
    sid = np.asarray(sid, np.int64)
    y = np.asarray(np.round(y), np.int64)
    x = np.asarray(np.round(x), np.int64)
    base = np.asarray(base, F32)
    escala_h = np.ones(len(sid), F32) if escala_h is None else np.asarray(escala_h, F32)
    cnt = sprites.cuenta[sid]
    # partir en lotes por numero de pixeles
    acum = np.cumsum(cnt)
    cortes = [0]
    while cortes[-1] < len(sid):
        lim = (acum[cortes[-1] - 1] if cortes[-1] > 0 else 0) + lote
        j = int(np.searchsorted(acum, lim, side="right"))
        cortes.append(max(j, cortes[-1] + 1))
    H = lienzo.altura.reshape(-1)
    A = lienzo.albedo.reshape(-1, 3)
    S = lienzo.suavidad.reshape(-1)
    for i0, i1 in zip(cortes[:-1], cortes[1:]):
        c = cnt[i0:i1]
        tot = int(c.sum())
        if tot == 0:
            continue
        inst = np.repeat(np.arange(i0, i1), c)
        arranque = np.repeat(np.cumsum(c) - c, c)
        g = sprites.inicio[sid[inst]] + (np.arange(tot) - arranque)
        yy = (y[inst] + sprites.dy[g]) % n
        xx = (x[inst] + sprites.dx[g]) % n
        plano = yy * n + xx
        h = base[inst] + sprites.h[g] * escala_h[inst]
        a = sprites.a[g]
        h0 = H[plano]
        sobre = h > h0
        if not sobre.any():
            continue
        plano, h, a, inst, g, h0 = plano[sobre], h[sobre], a[sobre], inst[sobre], g[sobre], h0[sobre]
        tmp = np.full(n * n, -np.inf, F32)
        np.maximum.at(tmp, plano, h)
        gana = h >= tmp[plano]
        plano, h, a, inst, g, h0 = plano[gana], h[gana], a[gana], inst[gana], g[gana], h0[gana]
        H[plano] = h0 + (h - h0) * a
        if color is not None or color_fn is not None:
            cnew = color_fn(inst, g) if color_fn is not None else color[inst]
            A[plano] = A[plano] + (cnew - A[plano]) * a[:, None]
        if suavidad is not None:
            S[plano] = S[plano] + (suavidad[inst] - S[plano]) * a
        if extra:
            for k, v in extra.items():
                E = lienzo.extra[k].reshape(-1)
                vv = v[inst] if np.ndim(v) else np.full(len(inst), v, F32)
                E[plano] = E[plano] + (vv - E[plano]) * a
    return lienzo


# ---------------------------------------------------------------------------------------------
# Mapas derivados
# ---------------------------------------------------------------------------------------------
def gradiente(h):
    """Derivadas periodicas (kernel de Scharr): dh/dcol, dh/dfila en unidades de h por pixel."""
    dx = np.roll(h, -1, 1) - np.roll(h, 1, 1)
    gx = (3 * np.roll(dx, 1, 0) + 10 * dx + 3 * np.roll(dx, -1, 0)) / 32.0
    dy = np.roll(h, -1, 0) - np.roll(h, 1, 0)
    gy = (3 * np.roll(dy, 1, 1) + 10 * dy + 3 * np.roll(dy, -1, 1)) / 32.0
    return gx.astype(F32), gy.astype(F32)


def normal_desde_altura(h_m, texel_m, fuerza=1.0):
    """Normal en espacio tangente, convencion OpenGL (+Y = arriba en la imagen, como Unity).

    n = (-dh/du, -dh/dv, 1); v crece hacia arriba (fila decreciente) => n_y = +dh/dfila.
    """
    gx, gy = gradiente(h_m)
    k = fuerza / texel_m
    nx = -gx * k
    ny = gy * k
    nz = np.ones_like(nx)
    inv = 1.0 / np.sqrt(nx * nx + ny * ny + nz * nz)
    return np.stack([nx * inv, ny * inv, nz * inv], -1).astype(F32)


def oclusion(h_m, texel_m, radio_m, n_dir=8, pasos=9, intensidad=1.0):
    """AO por horizonte (tipo HBAO) sobre el campo de alturas periodico.

    Para cada direccion busca el maximo angulo de elevacion del horizonte (con atenuacion por
    distancia) y acumula sin^2 (fraccion ocluida con peso coseno). 1 = sin ocluir.
    """
    rmax = max(2.0, radio_m / texel_m)
    radios = np.unique(np.round(np.geomspace(1.0, rmax, pasos)).astype(int))
    acum = np.zeros_like(h_m)
    for k in range(n_dir):
        ang = (k + 0.5) * 2 * math.pi / n_dir
        mejor = np.zeros_like(h_m)
        vistos = set()
        for r in radios:
            dy = int(round(r * math.sin(ang)))
            dx = int(round(r * math.cos(ang)))
            if (dy, dx) in vistos or (dy == 0 and dx == 0):
                continue
            vistos.add((dy, dx))
            d = math.hypot(dx, dy) * texel_m
            aten = max(0.0, 1.0 - (d / radio_m) ** 2)
            if aten <= 0:
                continue
            dh = np.roll(h_m, (-dy, -dx), axis=(0, 1)) - h_m
            s = dh / np.sqrt(dh * dh + d * d)
            np.maximum(mejor, s * aten, out=mejor)
        acum += mejor * mejor
    return np.clip(1.0 - intensidad * acum / n_dir, 0.0, 1.0).astype(F32)


def cavidad(h_m, texel_m, radio_m):
    """Curvatura concava (0 plano/convexo .. 1 grieta) a una escala dada."""
    s = max(0.7, radio_m / texel_m)
    dif = desenfocar(h_m, s) - h_m
    escala = np.percentile(np.abs(dif), 99) + 1e-9
    return np.clip(dif / escala, 0.0, 1.0).astype(F32)


# ---------------------------------------------------------------------------------------------
# Comprobaciones
# ---------------------------------------------------------------------------------------------
def costura(img):
    """Cociente salto-en-borde / salto-interior medio (~1.0 => sin costura)."""
    img = img.astype(np.float64)
    if img.ndim == 3:
        img = img.mean(-1)
    borde_x = np.abs(img[:, 0] - img[:, -1]).mean()
    borde_y = np.abs(img[0, :] - img[-1, :]).mean()
    int_x = np.abs(np.diff(img, axis=1)).mean()
    int_y = np.abs(np.diff(img, axis=0)).mean()
    return max(borde_x / max(int_x, 1e-12), borde_y / max(int_y, 1e-12))
