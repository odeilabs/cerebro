"""Primitivas para rasterizar hojas, foliolos, peciolos y ramitas como capas componibles.

Una hoja se describe en un marco local: s en [0, 1] a lo largo del nervio central (0 = base,
1 = punta) y t en pixeles perpendicular al nervio (+ a la izquierda mirando hacia la punta).
El nervio puede curvarse (c(s) = L*(k2*s^2 + k3*s^3)); para curvaturas suaves la aproximacion de
grafo es exacta a efectos visuales y cuesta O(pixeles del recuadro).

El contorno es |t| < w(s) (perfil por puntos de control, PCHIP) y el antialias sale de un campo de
distancia aproximado normalizado por su gradiente: bordes limpios para alpha test a 0.5.
"""
import numpy as np
from scipy.interpolate import PchipInterpolator

from comun import Capa, mezclar, normal_desde_altura, suave, satura
import ruido


# ----------------------------------------------------------------------------------------------
# Perfiles de anchura
# ----------------------------------------------------------------------------------------------
class Perfil:
    """Semi-anchura relativa w(s) (maximo ~1) por puntos de control, interpolada con PCHIP."""

    def __init__(self, s_pts, w_pts, n=2049):
        s_pts = np.asarray(s_pts, np.float64)
        w_pts = np.asarray(w_pts, np.float64)
        f = PchipInterpolator(s_pts, w_pts)
        self.s0, self.s1 = float(s_pts[0]), float(s_pts[-1])
        self.s = np.linspace(self.s0, self.s1, n)
        self.w = np.clip(f(self.s), 0, None)

    def __call__(self, s):
        return np.interp(s, self.s, self.w, left=0.0, right=0.0).astype(np.float32)

    def modular(self, factor):
        """Multiplica el perfil por factor(s) (array evaluado en self.s)."""
        self.w = np.clip(self.w * factor, 0, None)
        return self


def perfil_eliptico(rng, pos_max=0.45, base_redonda=0.5, largo_punta=0.12, ancho_punta=0.06,
                    ancho_en_punta=0.30, ondulacion=0.012):
    """Hoja eliptica/ovada con punta acuminada (punta de goteo) tipica del sotobosque tropical."""
    pm = float(np.clip(pos_max + rng.uniform(-0.04, 0.04), 0.25, 0.65))
    sp = 1.0 - largo_punta
    b = base_redonda
    S = [0.0, 0.015, 0.06, pm * 0.5, pm, (pm + sp) * 0.5, sp,
         sp + largo_punta * 0.35, sp + largo_punta * 0.72, 1.0]
    W = [0.0, 0.06 + 0.28 * b, 0.30 + 0.32 * b, 0.80 + 0.12 * b, 1.0, 0.80 + rng.uniform(-0.04, 0.04),
         ancho_en_punta, ancho_punta * 1.5, ancho_punta * 0.75, 0.0]
    p = Perfil(S, W)
    if ondulacion > 0:
        p.modular(1.0 + ondulacion * ruido.ruido1d(p.s.size, rng, suavidad=p.s.size / 28.0))
    return p


def perfil_lanceolado(rng, pos_max=0.35, largo_punta=0.25, base_redonda=0.4, ondulacion=0.006):
    """Lanceolado (bambu, foliolos de ceiba): estrecho, punta larga y afilada."""
    pm = float(np.clip(pos_max + rng.uniform(-0.04, 0.04), 0.15, 0.6))
    b = base_redonda
    sp = 1.0 - largo_punta
    S = [0.0, 0.02, 0.07, pm * 0.6, pm, (pm + sp) * 0.5, sp, sp + largo_punta * 0.5, 1.0]
    W = [0.0, 0.10 + 0.30 * b, 0.40 + 0.30 * b, 0.90, 1.0, 0.86, 0.55, 0.22, 0.0]
    p = Perfil(S, W)
    if ondulacion > 0:
        p.modular(1.0 + ondulacion * ruido.ruido1d(p.s.size, rng, suavidad=p.s.size / 30.0))
    return p


def perfil_oblongo(rng, base_redonda=0.7, punta_redonda=0.6, asimetria_base=0.0, ondulacion=0.01):
    """Foliolo oblongo (leguminosas): lados casi paralelos, base y punta redondeadas."""
    b, pr = base_redonda, punta_redonda
    S = [0.0, 0.02, 0.08, 0.25, 0.5, 0.75, 0.90, 0.96, 1.0]
    W = [0.0, 0.15 + 0.35 * b, 0.55 + 0.30 * b, 0.94, 1.0, 0.93, 0.62 + 0.15 * pr, 0.35 + 0.2 * pr, 0.0]
    p = Perfil(S, W)
    if ondulacion > 0:
        p.modular(1.0 + ondulacion * ruido.ruido1d(p.s.size, rng, suavidad=p.s.size / 20.0))
    return p


# ----------------------------------------------------------------------------------------------
# Marco local de una hoja (nervio curvo como grafo)
# ----------------------------------------------------------------------------------------------
class Marco:
    """Marco local: base en (x, y) de imagen, angulo en radianes (0 = derecha, pi/2 = arriba)."""

    def __init__(self, x, y, angulo, largo, k2=0.0, k3=0.0):
        self.x, self.y = float(x), float(y)
        self.ang = float(angulo)
        self.L = float(largo)
        self.k2, self.k3 = float(k2), float(k3)
        self.ca, self.sa = np.cos(self.ang), np.sin(self.ang)

    def _c(self, s):
        s = np.clip(s, -0.3, 1.3)
        return self.L * (self.k2 * s * s + self.k3 * s * s * s), 2 * self.k2 * s + 3 * self.k3 * s * s

    def a_imagen(self, s, t):
        """(s normalizado, t px) -> (x, y) de imagen."""
        s = np.asarray(s, np.float64)
        t = np.asarray(t, np.float64)
        c, m = self._c(s)
        nrm = np.sqrt(1 + m * m)
        a = s * self.L - t * m / nrm
        b = c + t / nrm
        X = a * self.ca - b * self.sa
        Y = a * self.sa + b * self.ca
        return self.x + X, self.y - Y

    def direccion(self, s):
        """Angulo (rad) de la tangente del nervio en s."""
        _, m = self._c(np.asarray(s, np.float64))
        return self.ang + np.arctan(m)

    def recuadro(self, perfil_px_izq, perfil_px_der, s0=0.0, s1=1.0, margen=4):
        s = np.linspace(s0, s1, 160)
        xs, ys = [], []
        for t in (perfil_px_izq(s), -perfil_px_der(s), np.zeros_like(s)):
            x, y = self.a_imagen(s, t)
            xs.append(x)
            ys.append(y)
        xs = np.concatenate(xs)
        ys = np.concatenate(ys)
        x0 = int(np.floor(xs.min())) - margen
        y0 = int(np.floor(ys.min())) - margen
        x1 = int(np.ceil(xs.max())) + margen
        y1 = int(np.ceil(ys.max())) + margen
        return y0, x0, y1 - y0, x1 - x0

    def locales(self, y0, x0, h, w):
        """Coordenadas locales (s, t) de cada pixel del recuadro."""
        yy, xx = np.mgrid[0:h, 0:w].astype(np.float32)
        rx = xx + (x0 + 0.5 - self.x)
        ry = -(yy + (y0 + 0.5 - self.y))
        a = rx * self.ca + ry * self.sa
        b = -rx * self.sa + ry * self.ca
        s = a / self.L
        c, m = self._c(s)
        t = (b - c) / np.sqrt(1 + m * m)
        return s.astype(np.float32), t.astype(np.float32)


def distancia_px(f, lim=(0.25, 4.0)):
    """Convierte un campo implicito en distancia aproximada en pixeles (divide por |grad|)."""
    gy, gx = np.gradient(f)
    g = np.sqrt(gx * gx + gy * gy)
    return f / np.clip(g, lim[0], lim[1])


def cobertura(f_px):
    return satura(0.5 - f_px)


# ----------------------------------------------------------------------------------------------
# Polilineas (ramitas, peciolos, hierbas muy curvadas)
# ----------------------------------------------------------------------------------------------
def campo_polilinea(pts, y0, x0, h, w, radio):
    """Distancia a una polilinea, abscisa curvilinea y vector al eje, solo cerca de cada segmento.

    Devuelve D (inf lejos), S (px), T (px con signo, + izquierda), VX, VY (pixel - punto del eje)."""
    D = np.full((h, w), np.inf, np.float32)
    S = np.zeros((h, w), np.float32)
    T = np.zeros((h, w), np.float32)
    VX = np.zeros((h, w), np.float32)
    VY = np.zeros((h, w), np.float32)
    pts = np.asarray(pts, np.float64)
    seg = np.diff(pts, axis=0)
    lon = np.sqrt((seg ** 2).sum(1))
    acum = np.concatenate([[0], np.cumsum(lon)])
    for i in range(len(seg)):
        if lon[i] < 1e-6:
            continue
        a = pts[i]
        d = seg[i]
        ya = int(np.floor(min(a[1], a[1] + d[1]) - radio)) - y0
        yb = int(np.ceil(max(a[1], a[1] + d[1]) + radio)) - y0 + 1
        xa = int(np.floor(min(a[0], a[0] + d[0]) - radio)) - x0
        xb = int(np.ceil(max(a[0], a[0] + d[0]) + radio)) - x0 + 1
        ya, xa = max(ya, 0), max(xa, 0)
        yb, xb = min(yb, h), min(xb, w)
        if ya >= yb or xa >= xb:
            continue
        yy, xx = np.mgrid[ya:yb, xa:xb].astype(np.float32)
        px = xx + (x0 + 0.5) - a[0]
        py = yy + (y0 + 0.5) - a[1]
        L2 = lon[i] ** 2
        u = np.clip((px * d[0] + py * d[1]) / L2, 0.0, 1.0)
        vx = px - u * d[0]
        vy = py - u * d[1]
        dist = np.sqrt(vx * vx + vy * vy)
        cruz = -(d[0] * py - d[1] * px)  # >0 a la izquierda (orientacion matematica, y arriba)
        sub = (slice(ya, yb), slice(xa, xb))
        mejor = dist < D[sub]
        D[sub] = np.where(mejor, dist, D[sub])
        S[sub] = np.where(mejor, acum[i] + u * lon[i], S[sub])
        T[sub] = np.where(mejor, np.where(cruz >= 0, dist, -dist), T[sub])
        VX[sub] = np.where(mejor, vx, VX[sub])
        VY[sub] = np.where(mejor, vy, VY[sub])
    return D, S, T, VX, VY, acum


def curva_suave(p0, ang0, largo, curvatura=0.0, n=24, ruido_amp=0.0, rng=None, giro_final=0.0):
    """Polilinea que sale de p0 con angulo ang0 y va girando (curvatura en rad por unidad de largo)."""
    s = np.linspace(0, 1, n)
    giro = curvatura * s + giro_final * s ** 2
    if ruido_amp and rng is not None:
        giro = giro + ruido_amp * np.cumsum(rng.standard_normal(n)) / np.sqrt(n)
    ang = ang0 + giro
    paso = largo / (n - 1)
    x = [p0[0]]
    y = [p0[1]]
    for i in range(1, n):
        x.append(x[-1] + paso * np.cos(ang[i - 1]))
        y.append(y[-1] - paso * np.sin(ang[i - 1]))
    return np.stack([x, y], axis=1)


def render_tubo(pts, radios, banco, rng, color_a, color_b=None, z=0.0, suav=0.22, grosor=0.92,
                lenticelas=0.0, estrias=0.6, nudos=(), contraste=1.0, transicion=(0.0, 1.0)):
    """Tubo (ramita/peciolo/tallo) visto desde arriba: normal cilindrica, color con estrias.

    radios: array por punto de la polilinea (px). color_a en la base, color_b en la punta."""
    pts = np.asarray(pts, np.float64)
    radios = np.asarray(radios, np.float64)
    rmax = float(radios.max()) + 2.0
    x0 = int(np.floor(pts[:, 0].min() - rmax))
    y0 = int(np.floor(pts[:, 1].min() - rmax))
    w = int(np.ceil(pts[:, 0].max() + rmax)) - x0 + 1
    h = int(np.ceil(pts[:, 1].max() + rmax)) - y0 + 1
    D, S, T, VX, VY, acum = campo_polilinea(pts, y0, x0, h, w, rmax)
    r = np.interp(S, acum, radios).astype(np.float32)
    f = D - r
    alfa = cobertura(f)
    alfa[~np.isfinite(D)] = 0
    q = np.clip(D / np.maximum(r, 0.5), 0, 0.985)
    nz = np.sqrt(1 - q * q)
    Dd = np.maximum(D, 1e-4)
    nx = VX / Dd * q
    ny = -VY / Dd * q
    normal = np.stack([nx, ny, nz], axis=-1)
    total = acum[-1]
    sl = S / max(total, 1e-6)
    if color_b is None:
        color_b = color_a
    col = mezclar(color_a, color_b, suave(transicion[0], transicion[1], sl))
    desp = rng.uniform(0, 512, 2)
    vt = np.clip(T / np.maximum(r, 0.5), -1, 1)
    fib = banco.leer("fibra", S * 0.35, vt * 6.0, 1.0, desp)
    man = banco.leer("fbm", S * 0.08, vt * 3.0, 1.0, desp)
    col = col * (1 + contraste * (estrias * 0.10 * fib + 0.10 * man))[..., None]
    if lenticelas > 0:
        cel = banco.leer("red_fina", S * 0.5, vt * 9.0, 1.0, desp + 100)
        idc = banco.leer("celda_fina", S * 0.5, vt * 9.0, 1.0, desp + 100)
        punto = satura((cel - 2.5) / 2.0) * (idc > 1 - lenticelas)
        col = mezclar(col, col * 1.6 + 0.02, punto * 0.6)
    for (s_n, fuerza) in nudos:  # anillos oscuros (nudos/cicatrices)
        dn = np.abs(S - s_n * total)
        col = col * (1 - fuerza * np.exp(-(dn / (r * 0.6 + 1)) ** 2))[..., None]
    ao = 0.72 + 0.28 * nz
    zc = z + r * nz
    return Capa(y0, x0, alfa, col, normal, suav, grosor, zc, ao), (D, S, r)


# ----------------------------------------------------------------------------------------------
# Hoja simple (venacion pinnada)
# ----------------------------------------------------------------------------------------------
_LUT = np.random.default_rng(12345).random(4096).astype(np.float32)


def _hash(k, sal=0):
    return _LUT[(np.asarray(k).astype(np.int64) * 7919 + sal * 104729) % 4096]


ESTILO_BASE = dict(
    # color (lineal)
    color=None, color_vena=None, color_nervio=None, color_amarillo=None, color_seco=None,
    color_seco_oscuro=None, color_halo=None, color_liquen=None,
    var_valor=0.12, var_tono=0.10, escala_manchas=140.0, var_mitades=0.04,
    contraste_nervio=0.75, contraste_vena=0.35, contraste_terciaria=0.12, contraste_areola=0.06,
    contraste_borde=0.10, aclarar_base=0.10,
    # venacion: "pinnada" (secundarias + red), "paralela" (bambu, palma, hierba)
    venacion="pinnada",
    n_venas=11, angulo_vena=52.0, curva_vena=0.55, ancho_nervio=4.0, ancho_vena=1.7,
    desfase_lados=0.35, irregular_venas=0.25, dist_intramarginal=5.0, escala_terciaria=1.1,
    cada=1, fuerza_menor=0.5, terciaria=True,
    n_long=9, cada_long=3, esp_transversal=6.0, alto_terciaria_t=None,
    # relieve (alturas en px, salvo las fracciones)
    combado=0.22, plegado=0.06, arqueo=0.05, alto_nervio=1.6, surco_nervio=0.5, alto_vena=0.9,
    abollonado=0.5, alto_terciaria=0.25, micro=0.12, enrollado_borde=1.2, intensidad_normal=1.0,
    incl_t=0.0, incl_s=0.0, pliegues=0.0,
    # anisotropia del ruido de color (hojas largas: manchas alargadas a lo largo del limbo)
    estirar_s=1.0, estirar_t=1.0, estria=0.0, n_estria=4,
    # material
    suavidad=0.6, var_suavidad=0.04, grosor=0.2,
)


def _fase_px(fase):
    """Distancia en px a la isolinea entera mas cercana de un campo de fase."""
    k = np.round(fase)
    df = fase - k
    gy, gx = np.gradient(fase)
    g = np.sqrt(gx * gx + gy * gy) + 1e-4
    return k, df, np.abs(df) / g


def _venas_pinnadas(e, s, t, u, sL, L, wpx, wref, izq, sc, d_borde, banco, desp, esc):
    nv = e["n_venas"]
    ang = np.radians(e["angulo_vena"])
    G = (wref / L) * (u / np.tan(ang) + e["curva_vena"] * u ** 2.2)
    irr = e["irregular_venas"] / nv * banco.leer("suave", sL * 0.0 + desp[0], sL * 0.9 + np.where(izq, 0, 211), 1.0)
    desf = np.where(izq, 0.0, e["desfase_lados"])
    fase = (s + irr - G) * nv + desf
    kv, dfase, dv = _fase_px(fase)
    s_origen = (kv - desf) / nv
    fuerza = (0.65 + 0.35 * _hash(kv, np.where(izq, 1, 2)))
    if e["cada"] > 1:
        fuerza = fuerza * np.where((kv.astype(np.int64) % e["cada"]) == 0, 1.0, e["fuerza_menor"])
    fuerza = fuerza * suave(0.015, 0.06, s_origen) * (1 - suave(0.86, 0.97, s_origen))
    wv = e["ancho_vena"] * esc * (1 - 0.6 * np.clip(u, 0, 1)) * (1 - 0.35 * sc) + 0.35
    if e["dist_intramarginal"] > 0:
        mv = np.exp(-(dv / wv) ** 2) * fuerza * (1 - suave(0.78, 0.97, u))
        dim = e["dist_intramarginal"] * esc * (0.6 + 0.4 * wpx / max(wref, 1))
        mi = np.exp(-((d_borde - dim) / (0.75 * esc + 0.35)) ** 2) * suave(0.45, 0.7, u) * 0.55
        mv = np.maximum(mv, mi)
    else:
        mv = np.exp(-(dv / wv) ** 2) * fuerza * (1 - suave(0.97, 1.02, u))
    return mv, dfase, fase


def _venas_paralelas(e, s, t, u, sL, L, wpx, esc, banco, desp):
    """Venas longitudinales que siguen el contorno (isolineas de |t|/w) y nervillos transversales."""
    n = e["n_long"]
    k, df, d = _fase_px(np.clip(u, 0, 1.2) * n)
    fuerza = np.where((k.astype(np.int64) % max(e["cada_long"], 1)) == 0, 1.0, e["fuerza_menor"])
    fuerza = fuerza * (0.8 + 0.2 * _hash(k, 5)) * (k > 0)
    wv = e["ancho_vena"] * esc * 0.5 + 0.3
    mv = np.exp(-(d / wv) ** 2) * fuerza * (1 - suave(0.92, 1.0, u))
    # teselado: nervillos transversales cortos y desordenados entre venas longitudinales
    esp = e["esp_transversal"] * esc
    ruido_t = banco.leer("fbm_fino", sL * 0.3, t * 0.3, 1.0, desp + 33)
    kt, dft, dt = _fase_px(sL / esp + 0.35 * ruido_t + 0.5 * k)
    mt = np.exp(-(dt / (0.5 * esc + 0.3)) ** 2) * (0.4 + 0.6 * _hash(kt * 31 + k, 9)) * (1 - suave(0.9, 1.0, u))
    return mv, df, mt


def render_hoja(marco, perfil_izq, perfil_der, est, rng, banco, z=0.0, danos=None, esc=1.0,
                recorte=None, s_min=0.0):
    """Rasteriza una hoja (limbo) y devuelve una Capa.

    perfil_izq/der: funciones s -> semi-anchura en PIXELES (cada lado).
    est: diccionario de estilo (ver ESTILO_BASE). danos: lista de dicts (ver aplicar_danos).
    recorte: funcion opcional (ctx) que modifica el contexto (p. ej. ctx["f"] para cortar)."""
    L = marco.L
    y0, x0, h, w = marco.recuadro(perfil_izq, perfil_der, s_min, 1.0, margen=int(4 + 3 * esc))
    s, t = marco.locales(y0, x0, h, w)
    izq = t >= 0
    sc = np.clip(s, s_min, 1.0)
    wpx = np.where(izq, perfil_izq(sc), perfil_der(sc))
    muestra = np.linspace(s_min, 1, 200)
    wref = float(np.max(np.maximum(perfil_izq(muestra), perfil_der(muestra))))
    f = np.abs(t) - wpx
    f = np.maximum(f, (s_min - s) * L)
    f = np.maximum(f, (s - 1.0) * L)
    f = distancia_px(f)
    return sombrear_lamina(y0, x0, s, t, wpx, wref, L, f, est, rng, banco, z, danos, esc, recorte, s_min)


def render_lamina_curva(pts, perfil, ancho, est, rng, banco, z=0.0, danos=None, esc=1.0, recorte=None):
    """Lamina estrecha a lo largo de una polilinea arbitraria (hierbas que se doblan mucho).

    perfil(s) -> semianchura relativa; ancho = semianchura maxima en px."""
    pts = np.asarray(pts, np.float64)
    r = ancho + 3.0 + 2 * esc
    x0 = int(np.floor(pts[:, 0].min() - r))
    y0 = int(np.floor(pts[:, 1].min() - r))
    w = int(np.ceil(pts[:, 0].max() + r)) - x0 + 1
    h = int(np.ceil(pts[:, 1].max() + r)) - y0 + 1
    D, S, T, _, _, acum = campo_polilinea(pts, y0, x0, h, w, r)
    L = float(acum[-1])
    s = (S / max(L, 1e-6)).astype(np.float32)
    t = np.where(np.isfinite(D), T, 1e4).astype(np.float32)
    wpx = (perfil(np.clip(s, 0, 1)) * ancho).astype(np.float32)
    f = np.abs(t) - wpx
    # tapa en la punta: la proyeccion se pega al extremo, asi que |t| ya es la distancia radial
    f = np.where(np.isfinite(D), f, 1e4)
    f = distancia_px(np.minimum(f, 50.0))
    return sombrear_lamina(y0, x0, s, t, wpx, float(ancho), L, f, est, rng, banco, z, danos, esc, recorte, 0.0)


def sombrear_lamina(y0, x0, s, t, wpx, wref, L, f, est, rng, banco, z=0.0, danos=None, esc=1.0, recorte=None,
                    s_min=0.0):
    """Venas, color, relieve y material de una lamina dada su geometria local (s, t, wpx, f)."""
    e = dict(ESTILO_BASE)
    e.update(est)
    h, w = s.shape
    izq = t >= 0
    sc = np.clip(s, s_min, 1.0)
    at = np.abs(t)
    u = np.clip(at / np.maximum(wpx, 0.5), 0.0, 1.5)
    d_borde = np.clip(-f, 0, None)  # distancia al borde hacia dentro (px)

    desp = rng.uniform(0, 512, 2)
    sL = s * L

    # ---- venas ----
    wm = e["ancho_nervio"] * esc * (1 - 0.78 * np.clip(sc, 0, 1)) + 0.45
    mm = np.exp(-(at / wm) ** 2) * (1 - suave(0.9, 1.0, s) * 0.5)
    et = e["escala_terciaria"] * esc
    areola = banco.leer("celda", sL, t, et, desp)
    fase = None
    if e["venacion"] == "paralela":
        mv, dfase, mt = _venas_paralelas(e, s, t, u, sL, L, wpx, esc, banco, desp)
        mt2 = np.zeros_like(mt)
    else:
        mv, dfase, fase = _venas_pinnadas(e, s, t, u, sL, L, wpx, wref, izq, sc, d_borde, banco, desp, esc)
        if e["terciaria"]:
            red = banco.leer("red", sL, t, et, desp)
            mt = np.exp(-(red / 0.85) ** 2) * (1 - 0.6 * mv) * (1 - suave(0.85, 1.0, u))
            red2 = banco.leer("red_fina", sL, t, et, desp + 37)
            mt2 = np.exp(-(red2 / 0.8) ** 2) * 0.5
        else:
            mt = np.zeros_like(mv)
            mt2 = mt

    # ---- color ----
    c = np.broadcast_to(e["color"], (h, w, 3)).astype(np.float32)
    nsL = sL * e["estirar_s"]
    nt = t * e["estirar_t"]
    man = banco.leer("fbm", nsL, nt, e["escala_manchas"] * esc / 140.0 * 1.6, desp + 60)
    man2 = banco.leer("fbm_fino", nsL, nt, 0.9 * esc, desp + 90)
    tono = banco.leer("suave", nsL, nt, 1.2 * esc, desp + 150)
    mitad = np.where(izq, 1.0, -1.0) * rng.uniform(-1, 1) * e["var_mitades"]
    c = c * (1 + e["var_valor"] * (0.8 * man + 0.35 * man2) + mitad)[..., None]
    if e["color_amarillo"] is not None and e["var_tono"] > 0:
        c = mezclar(c, e["color_amarillo"] * (c.mean(-1, keepdims=True) / max(float(e["color_amarillo"].mean()), 1e-4)),
                    satura(e["var_tono"] * (tono + 0.5)))
    # base del limbo y zona del nervio algo mas claras
    c = c * (1 + e["aclarar_base"] * (1 - suave(0.0, 0.35, s)) * (1 - np.clip(u, 0, 1) * 0.5))[..., None]
    c = mezclar(c, e["color_nervio"], mm * e["contraste_nervio"])
    c = mezclar(c, e["color_vena"], satura(mv * e["contraste_vena"] + mt * e["contraste_terciaria"] + mt2 * e["contraste_terciaria"] * 0.5))
    c = c * (1 + e["contraste_areola"] * (areola - 0.5) * 2)[..., None]
    grano = banco.leer("grano", sL, t, 0.7 * esc, desp + 200)
    c = c * (1 + 0.035 * grano)[..., None]
    if e["estria"]:
        # venillas paralelas finas entre las laterales principales (platano)
        c = c * (1 + e["estria"] * np.cos(2 * np.pi * dfase * e["n_estria"]))[..., None]
    borde = np.exp(-d_borde / (1.2 * esc + 0.4))
    c = mezclar(c, e["color_vena"], borde * e["contraste_borde"])

    # ---- relieve ----
    uc = np.clip(u, 0, 1.2)
    alt = wpx * (e["plegado"] * uc - e["combado"] * uc * uc)
    alt += -e["arqueo"] * L * (s - 0.45) ** 2
    alt += e["alto_nervio"] * esc * mm - e["surco_nervio"] * esc * np.exp(-(at / (0.35 * wm)) ** 2)
    alt += -e["alto_vena"] * esc * mv
    entre = 0.5 - 0.5 * np.cos(2 * np.pi * dfase)
    alt += e["abollonado"] * esc * entre * suave(0.08, 0.25, uc) * (1 - suave(0.8, 1.0, uc))
    alt_t = e["alto_terciaria"] if (e["venacion"] != "paralela" or e["alto_terciaria_t"] is None) else e["alto_terciaria_t"]
    alt += -alt_t * esc * mt + 0.12 * esc * areola
    alt += e["micro"] * esc * grano
    alt += -e["enrollado_borde"] * esc * np.exp(-d_borde / (2.5 * esc + 0.5))
    alt += e["incl_t"] * t + e["incl_s"] * sL
    if e["pliegues"]:
        # plisado suave paralelo a las venas (platano, palmas)
        alt += e["pliegues"] * esc * np.sin(2 * np.pi * dfase * 0.5 + 0.3) ** 2

    suav = e["suavidad"] + e["var_suavidad"] * man - 0.05 * mv - 0.02 * mt
    grosor = e["grosor"] * (0.8 + 0.35 * (1 - np.clip(u, 0, 1))) + 0.45 * mm + 0.16 * mv + 0.04 * mt
    ao = 1 - 0.18 * mv * (e["alto_vena"] > 0) - 0.06 * mt

    ctx = dict(s=s, t=t, u=u, sL=sL, L=L, wpx=wpx, f=f, c=c, alt=alt, suav=suav, grosor=grosor, ao=ao,
               d_borde=d_borde, mm=mm, mv=mv, esc=esc, desp=desp, izq=izq, e=e, dfase=dfase, nsL=nsL, nt=nt,
               fase=fase, wref=wref)
    if recorte is not None:
        recorte(ctx)
    if danos:
        aplicar_danos(ctx, danos, rng, banco)
    f = ctx["f"]
    alfa = cobertura(f)
    normal = normal_desde_altura(ctx["alt"], e["intensidad_normal"])
    return Capa(y0, x0, alfa, ctx["c"], normal, np.clip(ctx["suav"], 0, 1), np.clip(ctx["grosor"], 0, 1),
                z + ctx["alt"], np.clip(ctx["ao"], 0, 1))


# ----------------------------------------------------------------------------------------------
# Danos: agujeros de insecto, mordiscos en el borde, manchas necroticas con halo, clorosis,
# punta seca, liquenes/epifilos. Trabajan sobre el contexto de la hoja (campos en el recuadro).
# ----------------------------------------------------------------------------------------------
def _dist_irregular(ctx, cs, ct, r, rng, banco, irreg=0.35, escala=0.6, alargar=1.0):
    """Distancia (px) normalizada por un radio irregular alrededor del punto (cs, ct).

    La irregularidad depende del angulo (ruido suave leido sobre un circulo) para que el
    contorno sea una mancha/agujero continuo y no se fragmente."""
    dx = ctx["sL"] - cs * ctx["L"]
    dy = ctx["t"] - ct
    if alargar != 1.0:
        fi = rng.uniform(0, np.pi)
        cf, sf = np.cos(fi), np.sin(fi)
        dx, dy = (dx * cf + dy * sf) / alargar, (-dx * sf + dy * cf)
    d = np.sqrt(dx * dx + dy * dy)
    th = np.arctan2(dy, dx)
    o = rng.uniform(0, 512, 2)
    k = 9.0 + 6.0 * escala
    n = banco.leer("suave", np.sin(th) * k, np.cos(th) * k, 1.0, o)
    # algo de detalle fino en el borde (sin fragmentar)
    nf = banco.leer("fbm_fino", dx * 0.5, dy * 0.5, 1.0, o + 50)
    fac = np.clip(1 + irreg * np.tanh(n * 0.8) * 1.25 + 0.06 * nf, 0.4, None)
    return d / (r * fac)


def aplicar_danos(ctx, danos, rng, banco):
    e = ctx["e"]
    esc = ctx["esc"]
    for d in danos:
        tipo = d["tipo"]
        if tipo == "agujero":
            # agujero masticado: forma irregular, borde marron fino
            rr = _dist_irregular(ctx, d["s"], d["t"], d["r"], rng, banco, irreg=0.55, escala=1.4,
                                 alargar=rng.uniform(1.0, 2.2))
            dist_px = (rr - 1.0) * d["r"]
            ctx["f"] = np.maximum(ctx["f"], -dist_px)
            anillo = np.exp(-np.clip(dist_px, 0, None) / (1.3 * esc + 0.6)) * (dist_px > -1)
            ctx["c"] = mezclar(ctx["c"], e["color_seco_oscuro"], anillo * 0.85)
            ctx["suav"] = ctx["suav"] - 0.2 * anillo
            ctx["grosor"] = ctx["grosor"] + 0.3 * anillo
            ctx["alt"] = ctx["alt"] + 0.6 * esc * anillo
        elif tipo == "mordisco":
            # mordisco semicircular en el margen (oruga)
            lado = 1 if d["lado"] > 0 else -1
            s_b = d["s"]
            ct = lado * d["w"] * 1.05
            rr = _dist_irregular(ctx, s_b, ct, d["r"], rng, banco, irreg=0.25, escala=1.2)
            dist_px = (rr - 1.0) * d["r"]
            ctx["f"] = np.maximum(ctx["f"], -dist_px)
            anillo = np.exp(-np.clip(dist_px, 0, None) / (1.6 * esc + 0.6))
            ctx["c"] = mezclar(ctx["c"], e["color_seco"], anillo * 0.75)
            ctx["suav"] = ctx["suav"] - 0.15 * anillo
        elif tipo == "mancha":
            # mancha necrotica: centro marron oscuro, anillo claro, halo amarillo difuso
            rr = _dist_irregular(ctx, d["s"], d["t"], d["r"], rng, banco, irreg=0.3)
            nucleo = 1 - suave(0.75, 1.0, rr)
            anillo = np.exp(-((rr - 0.8) / 0.18) ** 2)
            halo = (1 - suave(1.0, d.get("halo", 2.2), rr)) * (1 - nucleo)
            ctx["c"] = mezclar(ctx["c"], e["color_halo"] * (0.8 + 0.4 * rng.random()), halo * d.get("fuerza_halo", 0.6))
            ctx["c"] = mezclar(ctx["c"], e["color_seco"], nucleo)
            ctx["c"] = mezclar(ctx["c"], e["color_seco_oscuro"], satura(anillo * 0.7 + nucleo * 0.3 * (rr < 0.4)))
            ctx["suav"] = ctx["suav"] - 0.28 * nucleo
            ctx["grosor"] = ctx["grosor"] + 0.35 * nucleo
            ctx["alt"] = ctx["alt"] - 0.5 * esc * nucleo + 0.4 * esc * anillo
        elif tipo == "clorosis":
            # amarilleo hacia la punta o los bordes, con frontera irregular
            n = banco.leer("fbm", ctx["nsL"], ctx["nt"], 2.2 * esc, ctx["desp"] + 300)
            if d.get("modo", "punta") == "punta":
                m = suave(d["desde"], d["desde"] + 0.3, ctx["s"] + 0.08 * n)
            else:
                m = suave(d["desde"], d["desde"] + 0.35, ctx["u"] + 0.1 * n)
            m = m * d.get("fuerza", 0.8)
            amarillo = e["color_amarillo"] * (0.85 + 0.3 * satura(0.5 + 0.5 * n))[..., None]
            ctx["c"] = mezclar(ctx["c"], amarillo, m)
            ctx["suav"] = ctx["suav"] - 0.08 * m
            ctx["grosor"] = ctx["grosor"] - 0.03 * m
        elif tipo == "punta_seca":
            n = banco.leer("fbm", ctx["nsL"], ctx["nt"], 1.5 * esc, ctx["desp"] + 400)
            pos = d["desde"]
            sn = ctx["s"] + 0.02 * n
            m = suave(pos, pos + 0.025, sn)
            frente = np.exp(-((sn - pos - 0.006) / 0.004) ** 2)
            transicion = suave(pos - 0.07, pos, sn) * (1 - m)
            ctx["c"] = mezclar(ctx["c"], e["color_amarillo"], transicion * 0.45)
            ctx["c"] = mezclar(ctx["c"], e["color_seco"] * (0.9 + 0.2 * satura(0.5 + 0.5 * n))[..., None], m)
            ctx["c"] = mezclar(ctx["c"], e["color_seco_oscuro"], frente * 0.6)
            ctx["suav"] = ctx["suav"] - 0.3 * m
            ctx["grosor"] = ctx["grosor"] + 0.3 * m
            ctx["alt"] = ctx["alt"] + 0.4 * esc * frente
        elif tipo == "borde_seco":
            # necrosis a lo largo del margen (hojas viejas, desgarros)
            n = banco.leer("fbm", ctx["nsL"], ctx["nt"], 1.3 * esc, ctx["desp"] + 500)
            anchura = np.maximum(d["ancho"] * esc * (1 + 0.8 * n), 0.15)
            m = 1 - suave(0.0, 1.0, (ctx["d_borde"] - anchura * 0.6) / (anchura * 0.4))
            tr = (1 - suave(0.0, 1.0, (ctx["d_borde"] - anchura) / (anchura * 1.2))) * (1 - m)
            ctx["c"] = mezclar(ctx["c"], e["color_amarillo"], tr * 0.55)
            ctx["c"] = mezclar(ctx["c"], e["color_seco"], m)
            ctx["suav"] = ctx["suav"] - 0.25 * m
            ctx["grosor"] = ctx["grosor"] + 0.25 * m
        elif tipo == "liquen":
            # epifilos: discos claros grisaceos pequenos
            for _ in range(d["n"]):
                cs = rng.uniform(0.08, 0.92)
                ct = rng.uniform(-0.8, 0.8) * np.interp(cs, [0, 1], [1, 1]) * d["w"]
                r = rng.uniform(2.5, 6.0) * esc
                rr = _dist_irregular(ctx, cs, ct, r, rng, banco, irreg=0.25)
                m = (1 - suave(0.6, 1.0, rr)) * (0.55 + 0.45 * suave(0.2, 0.9, rr))
                ctx["c"] = mezclar(ctx["c"], e["color_liquen"] * (0.8 + 0.3 * rng.random()), m * rng.uniform(0.35, 0.7))
                ctx["suav"] = ctx["suav"] - 0.3 * m
                ctx["alt"] = ctx["alt"] + 0.3 * esc * m


def danos_aleatorios(rng, s_rango=(0.12, 0.9), wmax=50.0, esc=1.0, edad=0.5, prob=1.0, liquen=1.0):
    """Lista de danos plausibles para una hoja (edad 0 joven .. 1 vieja)."""
    d = []
    p = prob
    if rng.random() < 0.3 * p * (0.4 + edad):
        for _ in range(rng.integers(1, 3)):
            d.append(dict(tipo="agujero", s=rng.uniform(*s_rango), t=rng.uniform(-0.6, 0.6) * wmax,
                          r=rng.uniform(4.0, 11.0) * esc))
    if rng.random() < 0.14 * p * (0.5 + edad):
        d.append(dict(tipo="mordisco", s=rng.uniform(0.25, 0.8), lado=rng.choice([-1, 1]),
                      w=wmax * rng.uniform(0.75, 0.95), r=rng.uniform(8, 22) * esc))
    if rng.random() < 0.3 * p * (0.2 + edad):
        for _ in range(rng.integers(1, 4)):
            r = rng.uniform(1.5, 6.0) * esc
            d.append(dict(tipo="mancha", s=rng.uniform(*s_rango), t=rng.uniform(-0.7, 0.7) * wmax,
                          r=r, halo=rng.uniform(1.3, 2.6), fuerza_halo=rng.uniform(0.0, 0.55)))
    if rng.random() < 0.12 * p * (0.4 + edad):
        modo = "punta" if rng.random() < 0.65 else "borde"
        d.append(dict(tipo="clorosis", modo=modo, desde=rng.uniform(0.45, 0.75) if modo == "punta" else rng.uniform(0.7, 0.85),
                      fuerza=rng.uniform(0.3, 0.7) if modo == "punta" else rng.uniform(0.25, 0.5)))
    if rng.random() < 0.2 * p * (0.4 + edad):
        d.append(dict(tipo="punta_seca", desde=rng.uniform(0.9, 0.97)))
    if rng.random() < 0.35 * p * edad * liquen:
        d.append(dict(tipo="liquen", n=int(rng.integers(2, 10)), w=wmax))
    return d
