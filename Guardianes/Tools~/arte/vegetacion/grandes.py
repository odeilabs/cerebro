"""hojas_grandes: 2x2 celdas con una hoja grande por celda.

Fila de arriba: Monstera (cortes profundos desde el margen y perforaciones, verde oscuro brillante).
Fila de abajo: oreja de elefante (Colocasia y Alocasia/Xanthosoma: corazon/flecha, venas claras,
mate-aterciopelada).

Punta arriba (v~0.97 de la celda); insercion del peciolo en (0.5, ~0.10); los lobulos basales bajan
hasta v~0.03. El contorno es un campo implicito en el marco de la hoja (Y a lo largo del nervio desde
la insercion, X perpendicular); las venas son curvas explicitas y su distancia se mide en pixeles.
"""
import numpy as np

import hojas as H
from comun import Capa, Lienzo, hexl, mezclar, normal_desde_altura, suave, satura, variar
from ramilletes import Celda


def _perfil(puntos):
    s, w = zip(*puntos)
    return H.Perfil(s, w)


class HojaGrande:
    """Campos comunes de una hoja grande en su celda.

    Contorno: |X| < w(Y) por arriba de la insercion (Y >= 0) y, por debajo, el borde inferior de los
    lobulos basales Y > y_min * B(|X| / w0), con B(0) = apice del seno y B(xl) = 1 (punto mas bajo)."""

    def __init__(self, celda, rng, L, W, y_min, prof_sup, B, inclinacion=0.0, k2=0.0, asim=0.0):
        self.c = celda
        self.rng = rng
        self.L, self.W, self.y_min = L, W, y_min
        ax, ay = celda.img(0.5, 0.10)
        self.marco = H.Marco(ax, ay, np.pi / 2 + inclinacion, L, k2, 0.0)
        self.prof = prof_sup          # Perfil de semianchura relativa para Y >= 0 (s en [0, 1])
        self.B = B                    # funcion x_rel (0..1) -> fondo del lobulo relativo (0..1)
        self.asim = asim
        self.y0, self.x0 = int(celda.y0), int(celda.x0)
        self.h, self.w = int(celda.alto), int(celda.ancho)
        s, t = self.marco.locales(self.y0, self.x0, self.h, self.w)
        self.s, self.t = s, t
        self.Y = s * L
        self.X = t
        lado = np.where(t >= 0, 1 + asim, 1 - asim)
        self.w0 = float(self.prof(np.array([0.0]))[0]) * W
        wsup = self.prof(np.clip(s, 0, 1)) * W * lado
        self.wpx = wsup.astype(np.float32)
        aX = np.abs(t)
        fondo = y_min * B(np.clip(aX / (self.w0 * lado), 0, 1))
        f = np.maximum(aX - wsup, fondo - self.Y)
        f = np.maximum(f, self.Y - L)
        self.f = H.distancia_px(f)

    def semianchura(self, Y, lado=1):
        Y = np.asarray(Y, np.float64)
        f = 1 + self.asim if lado > 0 else 1 - self.asim
        return self.prof(np.clip(Y / self.L, 0, 1)) * self.W * f

    def dentro(self, Y, X, lado=1, frac=1.0):
        """True si (Y, X) esta dentro del contorno (encogido por frac)."""
        w = float(self.semianchura(max(Y, 0.0), lado)) * frac
        if abs(X) >= w or Y > self.L * 0.995:
            return False
        if Y < 0:
            f = 1 + self.asim if lado > 0 else 1 - self.asim
            fondo = self.y_min * float(self.B(np.clip(abs(X) / (self.w0 * f), 0, 1)))
            return Y > fondo * frac
        return True

    def a_img(self, Y, X):
        return self.marco.a_imagen(np.asarray(Y) / self.L, np.asarray(X))

    def campo_curvas(self, curvas, radio):
        """Distancia minima (px) a un conjunto de curvas (listas de (Y, X)) + abscisa relativa e indice."""
        D = np.full((self.h, self.w), np.inf, np.float32)
        S = np.zeros((self.h, self.w), np.float32)
        I = np.full((self.h, self.w), -1, np.int32)
        for i, cv in enumerate(curvas):
            cv = np.asarray(cv, np.float64)
            xs, ys = self.a_img(cv[:, 0], cv[:, 1])
            d, sa, _, _, _, acum = H.campo_polilinea(np.stack([xs, ys], 1), self.y0, self.x0, self.h, self.w, radio)
            mejor = d < D
            D = np.where(mejor, d, D)
            S = np.where(mejor, np.clip(sa / max(acum[-1], 1e-6), 0.0, 1.0), S)
            I = np.where(mejor, i, I)
        return D, S, I


def fondo_lobulos(b0=0.0, xl=0.45, p_in=0.45, p_out=0.8):
    """B(x): 0..1 a lo largo del borde inferior; b0 = altura relativa del apice del seno."""
    def B(x):
        x = np.asarray(x, np.float64)
        sube = b0 + (1 - b0) * np.sin(0.5 * np.pi * np.clip(x / xl, 0, 1)) ** p_in
        baja = np.cos(0.5 * np.pi * np.clip((x - xl) / (1 - xl), 0, 1)) ** p_out
        return np.where(x < xl, sube, baja)
    return B


def _vena_hasta_margen(hg, Y0, X0, ang, lado, paso, curva=0.0, max_pasos=400, frac_margen=0.97):
    """Curva desde (Y0, X0) con angulo ang (respecto al eje +Y, hacia afuera) hasta el margen."""
    pts = [(Y0, X0)]
    a = ang
    for _ in range(max_pasos):
        Y, X = pts[-1]
        nY = Y + paso * np.cos(a)
        nX = X + lado * paso * np.sin(a)
        if not hg.dentro(nY, nX, lado, frac_margen):
            break
        pts.append((nY, nX))
        a = a - curva * paso / hg.W
    return np.array(pts)


# ----------------------------------------------------------------------------------------------
# Monstera
# ----------------------------------------------------------------------------------------------
PAL_MONSTERA = dict(color=hexl("#1f3d14"), joven=hexl("#2e5419"), vena=hexl("#4b6d28"), nervio=hexl("#6f8a3a"),
                    seco=hexl("#7a5a38"), seco_oscuro=hexl("#3a2818"), amarillo=hexl("#9a9238"),
                    peciolo=hexl("#3f5e20"))


def monstera(lienzo, celda, rng, banco):
    C = celda.ancho
    esc = celda.esc
    L = 0.87 * C
    W = 0.44 * C * rng.uniform(0.96, 1.02)
    y_min = -0.07 * C
    prof = _perfil([(0, 0.62), (0.06, 0.8), (0.2, 0.96), (0.36, 1.0), (0.55, 0.9), (0.72, 0.7), (0.85, 0.45),
                    (0.93, 0.22), (0.975, 0.08), (1.0, 0.0)])
    B = fondo_lobulos(b0=0.0, xl=rng.uniform(0.32, 0.42), p_in=0.35, p_out=0.75)
    hg = HojaGrande(celda, rng, L, W, y_min, prof, B, inclinacion=rng.uniform(-0.05, 0.05),
                    k2=rng.uniform(-0.05, 0.05), asim=rng.uniform(-0.05, 0.05))
    Y, X, aX = hg.Y, hg.X, np.abs(hg.X)
    # ---- venas: nervio central y secundarias pinnadas (cada segmento lleva una) ----
    n_lado = int(rng.integers(8, 11))
    venas = []
    origenes = {}
    for lado in (1, -1):
        ys = np.linspace(0.02 * L, 0.86 * L, n_lado) + rng.uniform(-0.015, 0.015, n_lado) * L
        ys += (0.03 * L if lado < 0 else 0.0)
        origenes[lado] = []
        for k, y0 in enumerate(ys):
            ang = np.radians(np.interp(y0 / L, [0, 0.5, 1], [70, 55, 40]) + rng.uniform(-4, 4))
            cv = _vena_hasta_margen(hg, y0, lado * 2.0, ang, lado, 6.0 * esc, curva=0.35)
            if len(cv) > 3:
                venas.append(cv)
                origenes[lado].append((y0, ang, cv))
        # venas de los lobulos basales (salen de la insercion hacia abajo/afuera)
        for a in (100, 125):
            cv = _vena_hasta_margen(hg, 0.0, lado * 2.0, np.radians(a + rng.uniform(-6, 6)), lado, 6.0 * esc, curva=-0.2)
            if len(cv) > 3:
                venas.append(cv)
    D_v, S_v, I_v = hg.campo_curvas(venas, 14 * esc)
    nerv = np.stack([np.linspace(0, L * 0.995, 60), np.zeros(60)], 1)
    D_m, S_m, _ = hg.campo_curvas([nerv], 40 * esc)
    w_m = (9.0 * (1 - np.clip(Y / L, 0, 1)) ** 0.8 + 2.0) * esc
    m_m = np.exp(-(D_m / w_m) ** 2)
    w_v = (3.6 * (1 - S_v) ** 0.7 + 1.0) * esc
    m_v = np.exp(-(D_v / w_v) ** 2)

    # ---- cortes desde el margen entre venas consecutivas y perforaciones ----
    cortes, agujeros = [], []
    for lado in (1, -1):
        ori = sorted(origenes[lado], key=lambda a: a[0])
        for k in range(len(ori) - 1):
            ya, aa, cva = ori[k]
            yb, ab, cvb = ori[k + 1]
            frac = rng.uniform(0.0, 1.0)
            ym = 0.5 * (ya + yb)
            am = 0.5 * (aa + ab)
            cv = _vena_hasta_margen(hg, ym, lado * 2.0, am + rng.uniform(-0.04, 0.04), lado, 6.0 * esc,
                                    curva=rng.uniform(0.2, 0.6), frac_margen=1.05)
            if len(cv) < 4:
                continue
            if rng.random() < 0.85 and ym < 0.9 * L:
                prof_c = rng.uniform(0.35, 0.75)  # fraccion de la vena desde el margen hacia dentro
                i0 = int(len(cv) * (1 - prof_c))
                tramo = cv[i0:]
                ancho_ext = rng.uniform(7, 20) * esc * (1 + 0.4 * (ym / L < 0.5)) * (1.5 if rng.random() < 0.15 else 1.0)
                cortes.append((tramo, ancho_ext))
                fin_corte = i0
            else:
                fin_corte = len(cv)
            # 1-2 perforaciones alargadas entre el nervio y el fondo del corte
            if ym < 0.82 * L and rng.random() < 0.8:
                for j in range(int(rng.integers(1, 3))):
                    q = rng.uniform(0.18, 0.42) if j == 0 else rng.uniform(0.5, 0.62)
                    idx = int(np.clip(q * fin_corte, 1, len(cv) - 2))
                    if idx >= fin_corte - 3:
                        continue
                    c0 = cv[idx]
                    d = cv[idx + 1] - cv[idx - 1]
                    agujeros.append((c0, np.arctan2(d[1], d[0]), rng.uniform(14, 30) * esc, rng.uniform(4, 9) * esc))
    f = hg.f.copy()
    borde_corte = np.zeros_like(f)
    if cortes:
        Dc, Sc, Ic = hg.campo_curvas([c for c, _ in cortes], 40 * esc)
        anchos = np.array([a for _, a in cortes], np.float32)
        aext = anchos[np.clip(Ic, 0, None)]
        g = (1.6 * esc + aext * np.clip(Sc, 0, 1) ** 1.1) * (Ic >= 0)
        campo = g - Dc
        f = np.maximum(f, np.where(Ic >= 0, campo, -1e3))
        borde_corte = np.exp(-np.clip(-campo, 0, None) / (2.0 * esc)) * (Ic >= 0)
    for (c0, a, la, lb) in agujeros:
        dY = Y - c0[0]
        dX = X - c0[1]
        ca, sa = np.cos(a), np.sin(a)
        u = dY * ca + dX * sa
        v = -dY * sa + dX * ca
        q = np.sqrt((u / la) ** 2 + (v / lb) ** 2)
        campo = (1 - q) * lb
        f = np.maximum(f, campo)
        borde_corte = np.maximum(borde_corte, np.exp(-np.clip(-campo, 0, None) / (2.0 * esc)))
    alfa = H.cobertura(f)
    d_borde = np.clip(-f, 0, None)

    # ---- color ----
    desp = rng.uniform(0, 512, 2)
    base = variar(PAL_MONSTERA["color"], rng, 0.01, 0.06, 0.06)
    c = np.broadcast_to(base, f.shape + (3,)).astype(np.float32)
    man = banco.leer("fbm", Y, X, 2.2 * esc, desp)
    man2 = banco.leer("fbm_fino", Y, X, 1.0 * esc, desp + 40)
    c = c * (1 + 0.10 * man + 0.04 * man2)[..., None]
    c = mezclar(c, PAL_MONSTERA["joven"], satura(0.25 + 0.25 * banco.leer("suave", Y, X, 2.5 * esc, desp + 80)))
    red = banco.leer("red", Y, X, 1.8 * esc, desp + 120)
    m_t = np.exp(-(red / 0.9) ** 2) * 0.5
    c = mezclar(c, PAL_MONSTERA["vena"], satura(m_v * 0.55 + m_t * 0.06))
    c = mezclar(c, PAL_MONSTERA["nervio"], m_m * 0.75)
    grano = banco.leer("grano", Y, X, 0.7 * esc, desp + 160)
    c = c * (1 + 0.03 * grano)[..., None]
    c = mezclar(c, PAL_MONSTERA["vena"] * 0.9, np.exp(-d_borde / (1.5 * esc)) * 0.25)
    # alguna mancha y algo de suciedad cerca de los cortes
    for _ in range(int(rng.integers(2, 6))):
        cy, cx = rng.uniform(0.1, 0.9) * L, rng.uniform(-0.7, 0.7) * W
        r = rng.uniform(3, 9) * esc
        d = np.sqrt((Y - cy) ** 2 + (X - cx) ** 2) / r
        nuc = 1 - suave(0.7, 1.0, d)
        c = mezclar(c, PAL_MONSTERA["amarillo"] * 0.7, (1 - suave(1.0, 2.2, d)) * 0.35)
        c = mezclar(c, PAL_MONSTERA["seco"], nuc)
    c = mezclar(c, PAL_MONSTERA["vena"] * 0.8, borde_corte * 0.3)

    # ---- relieve ----
    u = np.clip(aX / np.maximum(hg.wpx, 1), 0, 1.2)
    alt = -0.12 * hg.wpx * u ** 2 + 0.04 * hg.wpx * u              # mitades caidas, leve V junto al nervio
    alt += -0.035 * L * ((Y / L) - 0.4) ** 2
    alt += 3.0 * esc * m_m - 1.0 * esc * np.exp(-(D_m / (0.35 * w_m)) ** 2)
    alt += -1.2 * esc * m_v + 1.6 * esc * satura(D_v / (24 * esc)) * (1 - suave(0.85, 1.0, u))
    alt += -0.15 * esc * m_t + 0.12 * esc * grano
    alt += -1.4 * esc * np.exp(-d_borde / (3.0 * esc))
    normal = normal_desde_altura(alt, 1.0)
    suav = 0.66 + 0.04 * man - 0.06 * m_v - 0.2 * borde_corte * 0.5
    grosor = 0.3 + 0.45 * m_m + 0.2 * m_v
    ao = 1 - 0.2 * m_v - 0.1 * borde_corte
    lienzo.pintar(Capa(hg.y0, hg.x0, alfa, c, normal, np.clip(suav, 0, 1), np.clip(grosor, 0, 1), 20 + alt, ao))
    _peciolo(lienzo, celda, rng, banco, PAL_MONSTERA["peciolo"], 11 * esc, encima=False)


# ----------------------------------------------------------------------------------------------
# Oreja de elefante (Colocasia / Alocasia-Xanthosoma)
# ----------------------------------------------------------------------------------------------
PAL_COLOCASIA = dict(color=hexl("#3b5a2a"), joven=hexl("#4c6b33"), vena=hexl("#a4b48c"), nervio=hexl("#b9c49c"),
                     seco=hexl("#7a6040"), seco_oscuro=hexl("#3a2a18"), amarillo=hexl("#a0a048"),
                     peciolo=hexl("#5d7a3c"), mancha=hexl("#5a3a48"))
PAL_ALOCASIA = dict(color=hexl("#2c4a20"), joven=hexl("#3b5c27"), vena=hexl("#c2caa6"), nervio=hexl("#cfd6b4"),
                    seco=hexl("#7a6040"), seco_oscuro=hexl("#3a2a18"), amarillo=hexl("#a0a048"),
                    peciolo=hexl("#5a7638"), mancha=hexl("#3e4a30"))


def oreja_elefante(lienzo, celda, rng, banco, flecha=False):
    C = celda.ancho
    esc = celda.esc
    pal = PAL_ALOCASIA if flecha else PAL_COLOCASIA
    L = 0.87 * C
    y_min = -0.07 * C
    if flecha:
        W = 0.37 * C
        prof = _perfil([(0, 0.98), (0.06, 1.0), (0.2, 0.93), (0.4, 0.78), (0.6, 0.58), (0.78, 0.38), (0.9, 0.2),
                        (0.97, 0.07), (1.0, 0.0)])
        B = fondo_lobulos(b0=0.12, xl=rng.uniform(0.6, 0.72), p_in=0.9, p_out=0.45)
    else:
        W = 0.45 * C
        prof = _perfil([(0, 0.82), (0.08, 0.95), (0.2, 1.0), (0.38, 0.95), (0.58, 0.8), (0.75, 0.58), (0.88, 0.33),
                        (0.96, 0.12), (1.0, 0.0)])
        B = fondo_lobulos(b0=0.42, xl=rng.uniform(0.42, 0.5), p_in=0.7, p_out=0.7)
    hg = HojaGrande(celda, rng, L, W, y_min, prof, B, inclinacion=rng.uniform(-0.04, 0.04),
                    k2=rng.uniform(-0.04, 0.04), asim=rng.uniform(-0.04, 0.04))
    # margen algo ondulado (Alocasia) / liso (Colocasia)
    if flecha:
        ond = banco.leer("suave", hg.Y * 0.0 + 33, hg.Y * 0.9 + np.where(hg.X > 0, 0, 200), 1.0)
        hg.f = hg.f + 2.5 * esc * ond
    Y, X, aX = hg.Y, hg.X, np.abs(hg.X)
    # ---- venas: principal, dos basales hacia los lobulos y secundarias de cada una ----
    venas = []
    nerv = np.stack([np.linspace(0, L * 0.99, 80), np.zeros(80)], 1)
    basales = {}
    for lado in (1, -1):
        cv = _vena_hasta_margen(hg, 0.0, lado * 1.0, np.radians(rng.uniform(140, 152) if not flecha else rng.uniform(150, 160)),
                                lado, 4.0 * esc, curva=-0.35 if not flecha else -0.05, frac_margen=0.9)
        basales[lado] = cv
    n_lado = int(rng.integers(6, 8)) if not flecha else int(rng.integers(7, 9))
    for lado in (1, -1):
        ys = np.linspace(0.06 * L, 0.84 * L, n_lado) + rng.uniform(-0.02, 0.02, n_lado) * L
        for y0 in ys:
            ang = np.radians(np.interp(y0 / L, [0, 0.5, 1], [62, 50, 38]) + rng.uniform(-4, 4))
            cv = _vena_hasta_margen(hg, y0, lado * 2.0, ang, lado, 5.0 * esc, curva=0.55, frac_margen=0.94)
            if len(cv) > 3:
                venas.append(cv)
        # secundarias que salen de la basal hacia el margen del lobulo
        b = basales[lado]
        for q in (0.35, 0.7):
            i = int(q * (len(b) - 1))
            if i < 2:
                continue
            Yb, Xb = b[i]
            ang = np.radians(rng.uniform(100, 125))
            cv = _vena_hasta_margen(hg, Yb, Xb, ang, lado, 5.0 * esc, curva=0.2, frac_margen=0.94)
            if len(cv) > 2:
                venas.append(cv)
    D_v, S_v, _ = hg.campo_curvas(venas, 16 * esc)
    D_m, S_m, I_m = hg.campo_curvas([nerv, basales[1], basales[-1]], 40 * esc)
    w_m = np.where(I_m == 0, (7.5 * (1 - np.clip(Y / L, 0, 1)) ** 0.8 + 2.0), (6.0 * (1 - S_m) ** 0.8 + 1.8)) * esc
    m_m = np.exp(-(D_m / w_m) ** 2)
    w_v = (3.0 * (1 - S_v) ** 1.0 + 0.8) * esc
    m_v = np.exp(-(D_v / w_v) ** 2)
    # vena colectora junto al margen (tipica de las araceas)
    d_borde = np.clip(-hg.f, 0, None)
    m_col = np.exp(-((d_borde - 9 * esc) / (1.2 * esc)) ** 2) * 0.6
    alfa = H.cobertura(hg.f)

    # ---- color: mate, ligeramente azulado, venas claras ----
    desp = rng.uniform(0, 512, 2)
    base = variar(pal["color"], rng, 0.01, 0.06, 0.06)
    c = np.broadcast_to(base, Y.shape + (3,)).astype(np.float32)
    man = banco.leer("fbm", Y, X, 2.4 * esc, desp)
    man2 = banco.leer("fbm_fino", Y, X, 1.0 * esc, desp + 40)
    c = c * (1 + 0.09 * man + 0.04 * man2)[..., None]
    c = mezclar(c, pal["joven"], satura(0.2 + 0.25 * banco.leer("suave", Y, X, 2.5 * esc, desp + 80)))
    red = banco.leer("red", Y, X, 1.6 * esc, desp + 120)
    m_t = np.exp(-(red / 0.9) ** 2)
    red2 = banco.leer("red_fina", Y, X, 1.0 * esc, desp + 130)
    m_t = 0.5 * m_t + 0.5 * np.exp(-(red2 / 0.8) ** 2)
    k_v = 0.75 if flecha else 0.5
    c = mezclar(c, pal["vena"], satura(m_v * k_v + m_col * 0.3 + m_t * 0.025))
    c = mezclar(c, pal["nervio"], m_m * (0.85 if flecha else 0.65))
    grano = banco.leer("grano", Y, X, 0.6 * esc, desp + 160)
    c = c * (1 + 0.04 * grano)[..., None]
    # mancha purpura en la insercion del peciolo (Colocasia esculenta)
    dA = np.sqrt(Y ** 2 + X ** 2) / (16 * esc)
    c = mezclar(c, pal["mancha"], (1 - suave(0.6, 1.0, dA)) * 0.75)
    # margen algo seco/amarillento en una zona y alguna mancha
    n = banco.leer("fbm", Y, X, 1.2 * esc, desp + 200)
    seco_borde = (1 - suave(0.0, 1.0, (d_borde - np.maximum(2.5 * esc * (1 + 1.5 * n), 0.3)) / (2.0 * esc)))
    seco_borde *= satura(banco.leer("suave", Y, X, 3.0 * esc, desp + 240) - 0.3)
    c = mezclar(c, pal["amarillo"], (1 - suave(0, 8 * esc, d_borde)) * satura(n) * 0.25)
    c = mezclar(c, pal["seco"], seco_borde)
    for _ in range(int(rng.integers(1, 5))):
        cy, cx = rng.uniform(0.1, 0.9) * L, rng.uniform(-0.7, 0.7) * W
        r = rng.uniform(3, 8) * esc
        d = np.sqrt((Y - cy) ** 2 + (X - cx) ** 2) / r
        c = mezclar(c, pal["amarillo"] * 0.8, (1 - suave(1.0, 2.0, d)) * 0.3)
        c = mezclar(c, pal["seco"], 1 - suave(0.7, 1.0, d))

    # ---- relieve: acolchado entre venas, venas hundidas, lobulos caidos ----
    u = np.clip(aX / np.maximum(hg.wpx, 1), 0, 1.2)
    alt = -0.1 * hg.wpx * u ** 2
    alt += -0.03 * L * ((Y / L) - 0.35) ** 2
    alt += -1.6 * esc * m_m - 1.2 * esc * m_v + 2.2 * esc * satura(D_v / (26 * esc)) * satura(D_m / (30 * esc))
    alt += -0.2 * esc * m_t + 0.15 * esc * grano - 1.2 * esc * np.exp(-d_borde / (3.0 * esc))
    alt += -2.0 * esc * (1 - suave(0.0, 1.0, dA))  # hundido en la insercion
    normal = normal_desde_altura(alt, 1.0)
    suav = 0.3 + 0.03 * man - 0.08 * seco_borde
    grosor = 0.28 + 0.45 * m_m + 0.22 * m_v + 0.15 * seco_borde
    ao = 1 - 0.18 * m_v - 0.12 * m_m
    lienzo.pintar(Capa(hg.y0, hg.x0, alfa, c, normal, np.clip(suav, 0, 1), np.clip(grosor, 0, 1), 20 + alt, ao))
    _peciolo(lienzo, celda, rng, banco, pal["peciolo"], 12 * esc, encima=False)


def _peciolo(lienzo, celda, rng, banco, color, radio, encima=False):
    """Tocon del peciolo desde la insercion hasta el borde inferior de la celda (visto por el seno)."""
    ax, ay = celda.img(0.5, 0.10)
    bx, by = celda.img(0.5 + rng.uniform(-0.01, 0.01), 0.025)
    pts = np.stack([np.linspace(ax, bx, 12), np.linspace(ay, by, 12)], 1)
    capa, _ = H.render_tubo(pts, np.linspace(radio * 0.9, radio * 1.1, 12), banco, rng, color * 0.8, color * 1.05,
                            z=(40 if encima else 0), suav=0.4, grosor=0.95, estrias=0.6)
    if encima:
        lienzo.pintar(capa)
    else:
        # por debajo de la hoja: solo se ve donde la hoja no cubre
        _pintar_debajo(lienzo, capa)


def _pintar_debajo(lienzo, capa):
    """Composicion 'under': la capa queda detras de lo ya pintado."""
    h, w = capa.alfa.shape
    y0, x0 = capa.y0, capa.x0
    ya, xa = max(0, y0), max(0, x0)
    yb, xb = min(lienzo.alto, y0 + h), min(lienzo.ancho, x0 + w)
    src = (slice(ya - y0, yb - y0), slice(xa - x0, xb - x0))
    dst = (slice(ya, yb), slice(xa, xb))
    A = lienzo.A[dst]
    a = capa.alfa[src] * (1 - A)
    a3 = a[..., None]
    lienzo.C[dst] += capa.color[src] * a3
    lienzo.N[dst] += capa.normal[src] * a3
    lienzo.S[dst] += capa.suav[src] * a
    lienzo.G[dst] += capa.grosor[src] * a
    lienzo.Z[dst] += capa.z[src] * a
    lienzo.O[dst] += capa.ao[src] * a
    lienzo.A[dst] = A + a


def atlas_grandes(res, rng, banco):
    lienzo = Lienzo(res, res)
    lado = res // 2
    for i in range(4):
        celda = Celda((i % 2) * lado, (i // 2) * lado, lado)
        r = np.random.default_rng(rng.integers(1 << 30))
        if i < 2:
            monstera(lienzo, celda, r, banco)
        else:
            oreja_elefante(lienzo, celda, r, banco, flecha=(i == 3))
    return lienzo, dict(recortes=[(res // 4, res // 4), (3 * res // 4, res // 4)])
