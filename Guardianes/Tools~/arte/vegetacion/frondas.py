"""Frondas en 2 columnas (cada una ancho/2 x alto completo): palmera de coco, helecho y platano.

En todas, el eje (raquis / nervio central) sube recto por el centro de la columna desde v=0 (base)
hasta v=1 (punta), y +V apunta de la insercion a la punta (la normal se calcula en ese marco).
Columna 0 = fronda sana; columna 1 = vieja / seca / rota.
"""
import numpy as np

import hojas as H
from comun import Lienzo, hexl, mezclar, variar, suave, satura


class Columna:
    def __init__(self, x0, y0, ancho, alto):
        self.x0, self.y0, self.ancho, self.alto = float(x0), float(y0), float(ancho), float(alto)
        self.esc = self.ancho / 1024.0
        self.cx = self.x0 + self.ancho * 0.5

    def y(self, v):
        return self.y0 + (1.0 - v) * self.alto


def _perfil_campana(v, v0, vmax, v1, base=0.3, punta=0.08, exp_sub=1.0, exp_baj=1.3):
    """Longitud relativa de foliolos a lo largo del raquis: sube hasta vmax y baja hasta la punta."""
    v = np.asarray(v, np.float64)
    sube = base + (1 - base) * np.sin(0.5 * np.pi * np.clip((v - v0) / (vmax - v0), 0, 1)) ** exp_sub
    baja = punta + (1 - punta) * np.cos(0.5 * np.pi * np.clip((v - vmax) / (v1 - vmax), 0, 1)) ** exp_baj
    return np.where(v <= vmax, sube, baja)


# ----------------------------------------------------------------------------------------------
# Palmera de coco
# ----------------------------------------------------------------------------------------------
PAL_COCO_SANA = dict(
    viejo=hexl("#4a6a20"), joven=hexl("#6f8c2c"), vena=hexl("#7f9a3c"), nervio=hexl("#b2b25a"),
    raquis=hexl("#9c9f4c"), raquis_b=hexl("#7d8a3a"), amarillo=hexl("#b5a93e"), seco=hexl("#8f7550"),
    seco_oscuro=hexl("#4a3624"), halo=hexl("#a8a245"), liquen=hexl("#a3aa94"),
)
PAL_COCO_SECA = dict(
    viejo=hexl("#7a7a2e"), joven=hexl("#9a8f3a"), vena=hexl("#a39a52"), nervio=hexl("#c2b070"),
    raquis=hexl("#a8925a"), raquis_b=hexl("#8e7a48"), amarillo=hexl("#b39a46"), seco=hexl("#8a6e4c"),
    seco_oscuro=hexl("#4a3420"), halo=hexl("#a89a4c"), liquen=hexl("#a8a896"),
)


def _perfil_foliolo_palma(rng):
    S = [0.0, 0.02, 0.08, 0.28, 0.55, 0.78, 0.92, 1.0]
    W = [0.42, 0.62, 0.86, 1.0, 0.94, 0.68, 0.32, 0.0]
    W = [w * (1 + rng.uniform(-0.05, 0.05)) if 0 < i < len(W) - 1 else w for i, w in enumerate(W)]
    p = H.Perfil(S, W)
    p.modular(1.0 + 0.01 * H.ruido.ruido1d(p.s.size, rng, suavidad=p.s.size / 30.0))
    return p


def _corte_roto(s_corte, rng, banco, irregular=0.025, desgarro=None):
    """Recorte para un foliolo roto: termina en s_corte con borde desflecado."""
    o = rng.uniform(0, 512, 2)

    def f(ctx):
        n = banco.leer("fbm_fino", ctx["t"] * 2.0, ctx["t"] * 0.0 + o[0], 1.0, (0, o[1]))
        lim = s_corte + irregular * n
        ctx["f"] = np.maximum(ctx["f"], (ctx["s"] - lim) * ctx["L"])
        borde = np.exp(-np.clip((lim - ctx["s"]) * ctx["L"], 0, None) / (3.0 * ctx["esc"] + 1))
        ctx["c"] = mezclar(ctx["c"], ctx["e"]["color_seco"], borde * 0.85)
        ctx["suav"] = ctx["suav"] - 0.25 * borde
    return f


def _corte_partido(s_inicio, t_rel, ancho_px, rng):
    """Recorte para un foliolo partido a lo largo: hendidura desde s_inicio hasta la punta."""

    def f(ctx):
        w = np.maximum(ctx["wpx"], 0.5)
        tn = ctx["t"] / w
        abre = np.clip((ctx["s"] - s_inicio) / max(1 - s_inicio, 1e-3), 0, 1)
        g = ancho_px * abre ** 0.7 + 0.15
        d = np.abs(tn - t_rel) * w - g
        ctx["f"] = np.maximum(ctx["f"], np.where(ctx["s"] > s_inicio, -d, -1e3))
        ctx["c"] = mezclar(ctx["c"], ctx["e"]["color_seco"], np.exp(-np.clip(d, 0, None) / 1.5) * (ctx["s"] > s_inicio) * 0.4)
    return f


def fronda_palmera(lienzo, col, rng, banco, seca=False):
    esc = col.esc
    W, Hc = col.ancho, col.alto
    pal = PAL_COCO_SECA if seca else PAL_COCO_SANA
    v0, v1 = 0.06, 0.985
    paso = (0.021 if not seca else 0.022) * Hc * rng.uniform(0.97, 1.03)
    foliolos = []
    for lado in (1, -1):
        v = v0 + rng.uniform(0, paso / Hc) + (0.5 * paso / Hc if lado < 0 else 0)
        while v < v1 - 0.004:
            foliolos.append((v, lado))
            v += paso / Hc * rng.uniform(0.85, 1.15) * (1.0 - 0.25 * max(0.0, (v - 0.8) / 0.2))
    foliolos.sort(key=lambda a: a[0])
    radio_raquis = lambda v: (0.024 * (1 - v) ** 1.2 + 0.003) * W
    ruido_ang = {l: H.ruido.ruido1d(256, rng, suavidad=10.0) for l in (1, -1)}
    ruido_lon = {l: H.ruido.ruido1d(256, rng, suavidad=6.0) for l in (1, -1)}
    capas = []
    for (v, lado) in foliolos:
        r = np.random.default_rng(rng.integers(1 << 30))
        iv = int(np.clip(v, 0, 0.999) * 256)
        alfa = np.radians(np.interp(v, [0.0, 0.5, 1.0], [52, 43, 33]) + 2.5 * ruido_ang[lado][iv] + r.uniform(-1.0, 1.0))
        alcance = 0.46 * W * _perfil_campana(v, v0, 0.42, 1.0, base=0.32, punta=0.1) * (1 + 0.025 * ruido_lon[lado][iv])
        alcance = min(alcance * r.uniform(0.97, 1.01), 0.46 * W)
        rr = radio_raquis(v)
        L = (alcance - rr * 0.6) / np.sin(alfa)
        # que la punta no se salga por arriba
        L = min(L, ((0.992 - v) * Hc) / max(np.cos(alfa), 0.2))
        if L < 12 * esc:
            continue
        ang = np.pi / 2 - lado * alfa
        x = col.cx + lado * rr * 0.55
        y = col.y(v)
        ancho = 0.0125 * W * r.uniform(0.9, 1.1) * (0.75 + 0.25 * min(1.0, L / (0.5 * W)))
        k2 = r.uniform(-0.05, 0.05) - lado * 0.02
        estado = dict(roto=None, partido=None, falta=False, seco=0.0)
        if seca:
            u = r.random()
            if u < 0.18:
                estado["falta"] = True
            elif u < 0.5:
                estado["roto"] = r.uniform(0.35, 0.85)
            estado["seco"] = float(np.clip(r.normal(0.55, 0.25), 0, 1))
            if r.random() < 0.35:
                estado["partido"] = r.uniform(0.3, 0.7)
            # foliolos colgando: se ven algo mas abiertos y escorzados (mas cortos)
            if r.random() < 0.22:
                ang = np.pi / 2 - lado * (alfa + r.uniform(0.08, 0.3))
                L *= r.uniform(0.6, 0.85)
        else:
            if r.random() < 0.06:
                estado["roto"] = r.uniform(0.55, 0.9)
            if r.random() < 0.09:
                estado["partido"] = r.uniform(0.45, 0.8)
            estado["seco"] = float(np.clip(r.normal(0.05, 0.08), 0, 1))
        if estado["falta"]:
            # munon: queda solo la base del foliolo (ancho completo, extremo desgarrado)
            estado["roto"] = r.uniform(0.04, 0.12)
            estado["partido"] = None
        foliolos_geo = dict(v=v, lado=lado, x=x, y=y, ang=ang, L=L, ancho=ancho, k2=k2, estado=estado,
                            semilla=int(r.integers(1 << 30)))
        capas.append(foliolos_geo)

    # pintar foliolos (de abajo hacia arriba: los de arriba montan sobre los de abajo junto al raquis)
    for g in capas:
        r = np.random.default_rng(g["semilla"])
        P = _perfil_foliolo_palma(r)
        m = H.Marco(g["x"], g["y"], g["ang"], g["L"], g["k2"], r.uniform(-0.02, 0.02))
        est = g["estado"]
        base = mezclar(pal["viejo"], pal["joven"], r.uniform(0.2, 0.9))
        base = variar(base, r, 0.012, 0.1, 0.1)
        e = dict(color=base, color_vena=variar(pal["vena"], r, 0.01, 0.1, 0.1), color_nervio=pal["nervio"],
                 color_amarillo=pal["amarillo"], color_seco=pal["seco"], color_seco_oscuro=pal["seco_oscuro"],
                 color_halo=pal["halo"], color_liquen=pal["liquen"], venacion="paralela",
                 n_long=int(r.integers(4, 6)), cada_long=2, fuerza_menor=0.5, ancho_vena=1.4,
                 esp_transversal=r.uniform(5, 8), ancho_nervio=2.6, contraste_nervio=0.55, contraste_vena=0.2,
                 contraste_terciaria=0.04, contraste_areola=0.03, var_valor=r.uniform(0.06, 0.1),
                 var_tono=r.uniform(0.04, 0.1), escala_manchas=200.0, combado=0.0, plegado=r.uniform(0.18, 0.3),
                 arqueo=0.0, alto_nervio=0.6, surco_nervio=0.6, alto_vena=0.3, alto_terciaria_t=0.05,
                 abollonado=0.0, enrollado_borde=0.6, suavidad=0.62 - 0.25 * est["seco"], grosor=0.22,
                 incl_t=r.normal(0, 0.05), incl_s=r.normal(0, 0.03), estirar_s=0.18)
        danos = []
        recortes = []
        if est["roto"] is not None:
            recortes.append(_corte_roto(est["roto"], r, banco))
        if est["partido"] is not None:
            recortes.append(_corte_partido(est["partido"], r.uniform(-0.25, 0.25), r.uniform(1.0, 3.0) * esc, r))
        if est["seco"] > 0.02:
            danos.append(dict(tipo="clorosis", modo="punta", desde=float(np.clip(0.75 - 0.6 * est["seco"], 0.1, 0.9)),
                              fuerza=float(np.clip(0.4 + est["seco"], 0, 1))))
        if seca and r.random() < 0.7:
            danos.append(dict(tipo="punta_seca", desde=float(np.clip(r.uniform(0.35, 0.9) - 0.3 * est["seco"], 0.2, 0.95))))
        elif r.random() < 0.18:
            danos.append(dict(tipo="punta_seca", desde=r.uniform(0.9, 0.97)))
        if r.random() < (0.5 if seca else 0.15):
            for _ in range(r.integers(1, 4)):
                danos.append(dict(tipo="mancha", s=r.uniform(0.2, 0.9), t=r.uniform(-0.5, 0.5) * g["ancho"],
                                  r=r.uniform(1.5, 3.5) * esc, halo=1.6, fuerza_halo=0.3))
        if seca:
            danos.append(dict(tipo="borde_seco", ancho=r.uniform(1.0, 3.0)))

        def recorte(ctx, recortes=recortes):
            for rc in recortes:
                rc(ctx)

        capa = H.render_hoja(m, lambda s, P=P, a=g["ancho"]: P(s) * a, lambda s, P=P, a=g["ancho"]: P(s) * a, e, r,
                             banco, z=10 + 8 * g["v"] + r.uniform(0, 3), danos=danos, esc=esc,
                             recorte=recorte if recortes else None)
        lienzo.pintar(capa)

    # raquis encima: tubo que se estrecha hacia la punta, con cresta clara
    n = 120
    vs = np.linspace(0.0, 1.0, n)
    xs = col.cx + 1.5 * esc * np.sin(vs * 7.0 + rng.uniform(0, 6))
    pts = np.stack([xs, col.y(vs)], axis=1)
    radios = np.array([radio_raquis(v) for v in vs])
    radios[vs < v0] *= 1.0 + 0.35 * (1 - vs[vs < v0] / v0)
    capa, _ = H.render_tubo(pts, radios, banco, rng, pal["raquis_b"], pal["raquis"], z=40.0, suav=0.45 if not seca else 0.3,
                            grosor=0.95, estrias=0.9, contraste=1.2)
    lienzo.pintar(capa)


def atlas_palmera(res, rng, banco):
    lienzo = Lienzo(res, res)
    for i in range(2):
        col = Columna(i * res // 2, 0, res // 2, res)
        fronda_palmera(lienzo, col, np.random.default_rng(rng.integers(1 << 30)), banco, seca=(i == 1))
        # nivel de albedo: foliolos verdes ~0.15-0.17 lineal en G; la fronda seca algo mas clara
        lienzo.calibrar(0.7 if i == 0 else 0.82, x0=i * res // 2, x1=(i + 1) * res // 2)
    return lienzo, dict(recortes=[(res // 3, res // 4), (res // 3, 3 * res // 4)])


# ----------------------------------------------------------------------------------------------
# Helecho bipinnado
# ----------------------------------------------------------------------------------------------
PAL_HELECHO = dict(
    viejo=hexl("#3f6a1f"), joven=hexl("#64952c"), vena=hexl("#76a03c"), nervio=hexl("#8fae4c"),
    costa=hexl("#6f8c34"), raquis=hexl("#6d8436"), estipe=hexl("#5a4a30"),
    amarillo=hexl("#a9a53e"), seco=hexl("#86663f"), seco_oscuro=hexl("#4a3420"),
)
PAL_HELECHO_VIEJO = dict(
    viejo=hexl("#2f5219"), joven=hexl("#4a7424"), vena=hexl("#5b8233"), nervio=hexl("#759441"),
    costa=hexl("#5e7330"), raquis=hexl("#5d6230"), estipe=hexl("#4e3e28"),
    amarillo=hexl("#9a9038"), seco=hexl("#7c5c38"), seco_oscuro=hexl("#43301d"),
)


def pinna_helecho(lienzo, x, y, ang, Lp, k2, prm, rng, banco, z, esc, pal, vieja):
    """Una pinna: costa con pinnulas oblongas lobuladas alternas. Todo por campos implicitos."""
    beta = prm["beta"]
    delta = prm["delta"]
    rho = prm["rho"]
    lpin_max = prm["lpin"]

    def lpin(sn):
        sn = np.asarray(sn, np.float64)
        p = np.interp(sn, [0.0, 0.08, 0.25, 0.6, 0.85, 1.0], [0.55, 0.85, 1.0, 0.72, 0.38, 0.08])
        return lpin_max * p * ((sn >= -0.01) & (sn <= 1.01))

    m = H.Marco(x, y, ang, Lp, k2, 0.0)
    ext = lambda s: lpin(np.clip(s, 0, 1)) * 1.0 + 2
    y0, x0, h, w = m.recuadro(ext, ext, 0.0, 1.0, margen=int(6 + 3 * esc))
    s, t = m.locales(y0, x0, h, w)
    sp = s * Lp
    at = np.abs(t)
    lado = t >= 0
    off = np.where(lado, prm["desf_a"], prm["desf_b"])
    cb, sb_ = np.cos(beta), np.sin(beta)
    proj = sp - at * (cb / sb_)
    mejor_f = np.full(s.shape, 1e9, np.float32)
    an_m = np.zeros_like(mejor_f)
    bn_m = np.zeros_like(mejor_f)
    k_m = np.zeros_like(mejor_f)
    lk_m = np.ones_like(mejor_f)
    nl = prm["n_lobulos"]
    for j in (-1, 0, 1):
        k = np.round((proj - off) / delta) + j
        sbase = off + k * delta
        Lk = lpin(sbase / Lp) * (0.9 + 0.2 * H._hash(k, np.where(lado, 3, 4)))
        Lk = np.maximum(Lk, 1e-3)
        dx = sp - sbase
        a = dx * cb + at * sb_
        b = -dx * sb_ + at * cb
        an = a / Lk
        forma = np.clip(1 - np.clip(an, 0, 1) ** 2.6, 0, 1) ** 0.5 * (0.86 + 0.14 * np.sin(np.pi * np.clip(an, 0, 1)))
        # margen lobulado (festoneado), lobulos mas marcados hacia la base de la pinnula
        fase_l = an * nl
        lob = 1 - prm["prof_lob"] * (0.6 + 0.4 * (1 - an)) * (0.5 - 0.5 * np.cos(2 * np.pi * fase_l)) ** 3
        wk = Lk * rho * forma * lob
        fk = np.abs(b) - wk
        fk = np.maximum(fk, -a - 1.0)            # no invadir el otro lado de la costa
        fk = np.maximum(fk, (an - 1.0) * Lk)
        fk = np.where(Lk > 1.5, fk, 1e9)
        mejor = fk < mejor_f
        mejor_f = np.where(mejor, fk, mejor_f)
        an_m = np.where(mejor, an, an_m)
        bn_m = np.where(mejor, b / np.maximum(Lk * rho, 0.5), bn_m)
        k_m = np.where(mejor, k, k_m)
        lk_m = np.where(mejor, Lk, lk_m)
    # ala continua a lo largo de la costa (las pinnulas se sueldan hacia la punta de la pinna)
    sn = np.clip(s, 0, 1)
    ala = lpin(sn) * (0.12 + 0.55 * suave(0.55, 1.0, sn)) * rho * 2.2 + 1.2 * esc
    f_ala = np.maximum(at - ala, np.maximum(-sp, (s - 1.0) * Lp))
    f = np.minimum(mejor_f, f_ala)
    f = H.distancia_px(f)
    alfa = H.cobertura(f)
    d_borde = np.clip(-f, 0, None)

    # ---- venas: costa, nervio de la pinnula y venillas a cada lobulo ----
    w_costa = prm["ancho_costa"] * esc * (1 - 0.7 * sn) + 0.5
    m_costa = np.exp(-(at / w_costa) ** 2)
    m_mid = np.exp(-(np.abs(bn_m) * lk_m * rho / (0.9 * esc + 0.35)) ** 2) * (an_m < 0.9) * (an_m > 0.0)
    fase_v = an_m * nl - np.abs(bn_m) * 0.45
    kv, dfv, dv = H._fase_px(fase_v)
    m_ven = np.exp(-(dv / (0.6 * esc + 0.3)) ** 2) * (np.abs(bn_m) > 0.15) * (np.abs(bn_m) < 0.85) * 0.7
    # ---- color ----
    desp = rng.uniform(0, 512, 2)
    base = mezclar(pal["viejo"], pal["joven"], rng.uniform(0.3, 0.9))
    base = variar(base, rng, 0.012, 0.08, 0.1)
    c = np.broadcast_to(base, (h, w, 3)).astype(np.float32)
    man = banco.leer("fbm", sp, t, 1.6 * esc, desp)
    var_k = (H._hash(k_m, 7) - 0.5) * 0.12
    c = c * (1 + 0.08 * man + var_k + 0.1 * np.clip(an_m, 0, 1) * 0.5)[..., None]
    c = mezclar(c, pal["vena"], satura(m_mid * 0.45 + m_ven * 0.25))
    c = mezclar(c, pal["costa"], m_costa * 0.8)
    grano = banco.leer("grano", sp, t, 0.7 * esc, desp + 50)
    c = c * (1 + 0.04 * grano)[..., None]
    suav = np.full(s.shape, 0.42 if not vieja else 0.36, np.float32) + 0.03 * man
    grosor = 0.17 + 0.25 * m_mid + 0.1 * m_ven + 0.55 * m_costa
    # pinnulas danadas / secas (sobre todo en la fronda vieja)
    if prm["p_seca"] > 0:
        seca = (H._hash(k_m * 3 + np.where(lado, 1, 0), 11) < prm["p_seca"]).astype(np.float32)
        frente = suave(0.35, 0.75, an_m + 0.25 * banco.leer("fbm", sp, t, 0.9 * esc, desp + 90))
        mseca = seca * frente
        c = mezclar(c, pal["seco"] * (0.85 + 0.3 * satura(0.5 + 0.4 * man))[..., None], mseca * 0.9)
        suav = suav - 0.15 * mseca
        grosor = grosor + 0.2 * mseca
    if prm["amarilleo"] > 0:
        am = suave(0.5, 1.0, sn + 0.15 * man) * prm["amarilleo"]
        c = mezclar(c, pal["amarillo"] * (c.mean(-1, keepdims=True) / max(float(pal["amarillo"].mean()), 1e-4)), am)
    # ---- relieve ----
    alt = -0.18 * lk_m * rho * np.clip(np.abs(bn_m), 0, 1.2) ** 2           # pinnula convexa
    alt += -prm["arqueo"] * Lp * (s - 0.35) ** 2                              # la pinna se arquea
    alt += 1.2 * esc * m_costa - 0.6 * esc * m_mid - 0.35 * esc * m_ven
    alt += -0.8 * esc * np.exp(-d_borde / (1.5 * esc + 0.5))
    alt += 0.1 * esc * grano + prm["incl"] * t
    normal = H.normal_desde_altura(alt, 1.0)
    ao = 1 - 0.15 * m_mid - 0.1 * m_ven
    lienzo.pintar(H.Capa(y0, x0, alfa, c, normal, np.clip(suav, 0, 1), np.clip(grosor, 0, 1), z + alt, ao))
    # costa como tubo fino encima
    sc = np.linspace(0, 1, 24)
    xc, yc = m.a_imagen(sc, np.zeros_like(sc))
    rc = (prm["ancho_costa"] * esc * (1 - 0.7 * sc) + 0.4) * 0.8
    capa, _ = H.render_tubo(np.stack([xc, yc], 1), rc, banco, rng, pal["costa"] * 0.9, pal["costa"], z=z + 3,
                            suav=0.4, grosor=0.7, estrias=0.3)
    lienzo.pintar(capa)


def fronda_helecho(lienzo, col, rng, banco, vieja=False):
    esc = col.esc
    W, Hc = col.ancho, col.alto
    pal = PAL_HELECHO_VIEJO if vieja else PAL_HELECHO
    v0, v1 = 0.07, 0.975
    curva = rng.uniform(-0.04, 0.04)
    eje_x = lambda v: col.cx + curva * W * (v ** 2) + 2 * esc * np.sin(v * 9 + 1.3)
    n_lado = int(rng.integers(20, 25))
    paso = (v1 - v0) / n_lado
    pinnas = []
    for lado in (1, -1):
        for i in range(n_lado):
            v = v0 + (i + (0.5 if lado < 0 else 0.0) + rng.uniform(-0.12, 0.12)) * paso
            if v > v1 - 0.01:
                continue
            pinnas.append((v, lado))
    pinnas.sort()
    for (v, lado) in pinnas:
        r = np.random.default_rng(rng.integers(1 << 30))
        perfil = _perfil_campana(v, v0, 0.33, 1.0, base=0.62, punta=0.05, exp_sub=1.0, exp_baj=1.1)
        alcance = 0.45 * W * perfil * r.uniform(0.94, 1.02)
        beta_p = np.radians(np.interp(v, [0, 0.5, 1], [72, 64, 50]) + r.uniform(-3, 3))
        Lp = alcance / np.sin(beta_p)
        Lp = min(Lp, (0.99 - v) * Hc / max(np.cos(beta_p), 0.25))
        if Lp < 10 * esc:
            continue
        x = eje_x(v)
        y = col.y(v)
        ang = np.pi / 2 - lado * beta_p
        lpin = float(np.clip(0.11 * Lp, 9 * esc, 52 * esc)) * r.uniform(0.92, 1.08)
        prm = dict(beta=np.radians(r.uniform(62, 74)), delta=lpin * r.uniform(0.62, 0.72), rho=r.uniform(0.3, 0.36),
                   lpin=lpin, desf_a=r.uniform(0, 1) * lpin * 0.6, desf_b=0.0, n_lobulos=int(r.integers(4, 7)),
                   prof_lob=r.uniform(0.25, 0.5), ancho_costa=2.0 * (Lp / (0.4 * W)) ** 0.5,
                   arqueo=r.uniform(0.0, 0.05), incl=r.normal(0, 0.05),
                   p_seca=(r.uniform(0.0, 0.25) if vieja else (0.03 if r.random() < 0.2 else 0.0)),
                   amarilleo=(r.uniform(0.0, 0.5) if vieja and r.random() < 0.4 else 0.0))
        prm["desf_b"] = prm["desf_a"] + prm["delta"] * 0.5
        k2 = lado * r.uniform(0.02, 0.09)  # las pinnas se curvan hacia la punta de la fronda
        pinna_helecho(lienzo, x, y, ang, Lp, k2, prm, r, banco, 10 + 10 * v + r.uniform(0, 4), esc, pal, vieja)
    # raquis + estipe con escamas
    n = 140
    vs = np.linspace(0.0, 0.995, n)
    pts = np.stack([eje_x(vs), col.y(vs)], axis=1)
    radios = (0.011 * (1 - vs) ** 1.1 + 0.0018) * W
    capa, _ = H.render_tubo(pts, radios, banco, rng, pal["estipe"], pal["raquis"], z=40.0, suav=0.35, grosor=0.92,
                            estrias=0.8, lenticelas=0.35, contraste=1.3, transicion=(0.04, 0.3))
    # el estipe (por debajo de las primeras pinnas) es pardo y escamoso
    lienzo.pintar(capa)


def atlas_helecho(res, rng, banco):
    lienzo = Lienzo(res, res)
    for i in range(2):
        col = Columna(i * res // 2, 0, res // 2, res)
        fronda_helecho(lienzo, col, np.random.default_rng(rng.integers(1 << 30)), banco, vieja=(i == 1))
        lienzo.calibrar(0.64 if i == 0 else 0.95, x0=i * res // 2, x1=(i + 1) * res // 2)
    return lienzo, dict(recortes=[(res // 3, res // 4), (2 * res // 3, 3 * res // 4)])


# ----------------------------------------------------------------------------------------------
# Hoja de platano
# ----------------------------------------------------------------------------------------------
PAL_PLATANO = dict(
    color=hexl("#4f7826"), joven=hexl("#5f8a2b"), vena=hexl("#77a03e"), nervio=hexl("#a7b46a"),
    nervio_b=hexl("#8f9f52"), costa=hexl("#6a923a"), amarillo=hexl("#aaa23e"), seco=hexl("#8a7048"), seco_oscuro=hexl("#4a3622"),
    halo=hexl("#a2a040"), liquen=hexl("#a3aa94"),
)


def _desgarros(rng, n, profundidad, lado_sal, rota):
    """Desgarros por lado: posicion en unidades de vena, profundidad (u final) y apertura (px)."""
    pos = np.sort(rng.uniform(0.08, 0.95, n))
    res = []
    for p in pos:
        if rota:
            u_fin = float(np.clip(rng.choice([rng.uniform(0.0, 0.06), rng.uniform(0.1, 0.7)], p=[0.55, 0.45]), 0, 0.9))
            g = rng.uniform(4.0, 34.0) * (1.0 if rng.random() < 0.7 else 0.3)
        else:
            u_fin = float(rng.uniform(*profundidad))
            g = rng.uniform(0.8, 4.0)
        res.append((p, u_fin, g))
    return res


def hoja_platano(lienzo, col, rng, banco, rota=False):
    esc = col.esc
    W, Hc = col.ancho, col.alto
    pal = PAL_PLATANO
    L = Hc * 0.995
    m = H.Marco(col.cx, col.y(0.0), np.pi / 2, L, 0.0, 0.0)
    wmax = 0.47 * W
    S = [0.0, 0.05, 0.062, 0.09, 0.15, 0.3, 0.55, 0.8, 0.9, 0.95, 0.972, 0.985]
    Wt = [0.0, 0.0, 0.3, 0.62, 0.88, 0.99, 1.0, 0.97, 0.86, 0.68, 0.42, 0.0]
    asim = rng.uniform(0.0, 0.04)
    Pi = H.Perfil(S, [w * (1 + asim) for w in Wt])
    Pd = H.Perfil(S, [w * (1 - asim) for w in Wt])
    for P in (Pi, Pd):
        P.modular(1.0 + 0.006 * H.ruido.ruido1d(P.s.size, rng, suavidad=P.s.size / 40.0))
    nv = int(rng.integers(105, 125))
    e = dict(color=variar(pal["color"], rng, 0.01, 0.06, 0.06), color_vena=pal["vena"], color_nervio=pal["nervio"],
             color_amarillo=pal["amarillo"], color_seco=pal["seco"], color_seco_oscuro=pal["seco_oscuro"],
             color_halo=pal["halo"], color_liquen=pal["liquen"],
             n_venas=nv, angulo_vena=rng.uniform(68, 74), curva_vena=0.12, irregular_venas=0.35, desfase_lados=0.5,
             dist_intramarginal=0.0, cada=1, ancho_vena=0.9, terciaria=False, ancho_nervio=0.0,
             contraste_vena=0.38, contraste_nervio=0.0, contraste_areola=0.0, contraste_borde=0.15,
             var_valor=0.09, var_tono=0.12, escala_manchas=260.0, estirar_s=1.0, estirar_t=0.25,
             estria=0.045, n_estria=4, combado=0.05, plegado=0.0, arqueo=0.0, alto_vena=0.35, abollonado=0.0,
             pliegues=0.45, alto_terciaria=0.0, micro=0.08, enrollado_borde=1.5, suavidad=0.55, var_suavidad=0.05,
             grosor=0.2, aclarar_base=0.0, var_mitades=0.03)
    danos = [dict(tipo="borde_seco", ancho=(1.5 if not rota else 4.0))]
    for _ in range(int(rng.integers(3, 7)) if not rota else int(rng.integers(8, 14))):
        danos.append(dict(tipo="mancha", s=rng.uniform(0.15, 0.95), t=rng.uniform(-0.9, 0.9) * wmax,
                          r=rng.uniform(3, 10) * esc, halo=rng.uniform(1.5, 2.5), fuerza_halo=rng.uniform(0.2, 0.6)))
    if rota:
        danos.append(dict(tipo="clorosis", modo="borde", desde=0.7, fuerza=0.45))
    # desgarros a lo largo de las venas
    n_d = int(rng.integers(26, 36)) if rota else int(rng.integers(5, 9))
    desg = {lado: _desgarros(rng, n_d, (0.75, 0.97), lado, rota) for lado in (1, -1)}
    o = rng.uniform(0, 512, 2)

    def recorte(ctx):
        fase = ctx["fase"]
        gy, gx = np.gradient(fase)
        gf = np.sqrt(gx * gx + gy * gy) + 1e-4
        u = ctx["u"]
        s = ctx["s"]
        quitar = np.zeros(fase.shape, np.float32) - 1e3
        borde_rasgado = np.zeros(fase.shape, np.float32)
        for lado, lista in desg.items():
            mascara_lado = (ctx["t"] >= 0) if lado > 0 else (ctx["t"] < 0)
            for (p, u_fin, g) in lista:
                # posicion del desgarro en unidades de fase (entre dos venas)
                fz = np.round(p * nv) + 0.5 + (0.5 if lado < 0 else 0.0)
                if not np.any(mascara_lado):
                    continue
                d = np.abs(fase - fz) / gf
                prog = np.clip((u - u_fin) / max(1 - u_fin, 1e-3), 0, 1)
                gap = 0.35 + g * esc * prog ** 1.5
                # flecos: el borde del desgarro no es recto
                n = banco.leer("fbm_fino", ctx["sL"] * 0.8, ctx["t"] * 0.2, 1.0, o + p * 97)
                campo = gap * (1 + 0.35 * n) - d
                campo = np.where(mascara_lado & (u > u_fin), campo, -1e3)
                quitar = np.maximum(quitar, campo)
                borde_rasgado = np.maximum(borde_rasgado, np.exp(-np.clip(-campo, 0, None) / (2.5 * esc + 0.6)) * (u > u_fin))
        ctx["f"] = np.maximum(ctx["f"], quitar)
        costa = np.exp(-(np.abs(ctx["t"]) / (0.11 * wmax)) ** 2) * (1 - H.suave(0.9, 1.0, s))
        ctx["c"] = mezclar(ctx["c"], pal["costa"], costa * 0.45)
        seco = borde_rasgado * (0.5 if not rota else 0.85)
        ctx["c"] = mezclar(ctx["c"], ctx["e"]["color_seco"], seco)
        ctx["suav"] = ctx["suav"] - 0.25 * seco
        ctx["grosor"] = ctx["grosor"] + 0.2 * seco
        ctx["alt"] = ctx["alt"] - 0.8 * esc * borde_rasgado

    capa = H.render_hoja(m, lambda s: Pi(s) * wmax, lambda s: Pd(s) * wmax, e, rng, banco, z=10.0, danos=danos,
                         esc=esc, recorte=recorte)
    lienzo.pintar(capa)
    # nervio central: ancho, claro, con surco
    n = 160
    vs = np.linspace(0.0, 0.985, n)
    pts = np.stack([np.full(n, col.cx), col.y(vs)], axis=1)
    radios = (0.026 * (1 - vs) ** 0.9 + 0.004) * W
    capa, _ = H.render_tubo(pts, radios, banco, rng, pal["nervio_b"], pal["nervio"], z=30.0, suav=0.5, grosor=0.9,
                            estrias=0.6)
    lienzo.pintar(capa)


def atlas_platano(res, rng, banco):
    lienzo = Lienzo(res, res)
    for i in range(2):
        col = Columna(i * res // 2, 0, res // 2, res)
        hoja_platano(lienzo, col, np.random.default_rng(rng.integers(1 << 30)), banco, rota=(i == 1))
    lienzo.calibrar(0.76)
    return lienzo, dict(recortes=[(res // 2, res // 4), (res // 2, 3 * res // 4)])
