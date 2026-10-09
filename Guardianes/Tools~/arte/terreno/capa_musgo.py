"""Suelo/roca cubierto de musgo: cojines densos y mullidos de varios verdes con huecos de
tierra oscura, y una textura fibrosa de altisima frecuencia hecha con cientos de miles de
brotes individuales (rosetas estrelladas tipo Polytrichum, frondas plumosas tipo Hypnum /
Thuidium y grano aterciopelado) apilados por maximo de altura sobre cada cojin.
Las puntas de los brotes son mas claras y amarillentas; el interior del cojin, mas oscuro.
"""
import math

import numpy as np

from elementos import estampar_hoja, estampar_ramita, perfil_hoja
from exportar import ResultadoCapa
from nucleo import (F32, Lienzo, Sprites, col, desenfocar, mezclar, puntos_poisson, ruido_m,
                    salpicar, sprite_supermuestreado, suave, tinte_macro, voronoi)

TAM_M = 3.0

# especie: color base (sRGB), color de puntas, peso
ESPECIES = {
    "estrella": ((54, 80, 32), (104, 126, 48), 0.40),
    "pluma": ((112, 124, 46), (158, 162, 70), 0.38),
    "terciopelo": ((76, 102, 40), (118, 138, 56), 0.22),
}


def _sprites_musgo(rng):
    sp = Sprites()
    tipos = {"estrella": [], "pluma": [], "terciopelo": []}
    # rosetas: brazos (hojitas) que salen del tallo
    for _ in range(64):
        R = rng.uniform(2.0, 4.2)
        brazos = int(rng.integers(5, 9))
        a0 = rng.uniform(0, 2 * math.pi)
        lado = int(2 * math.ceil(R) + 3)

        def f(u, v, R=R, brazos=brazos, a0=a0):
            cob = np.zeros(u.shape, bool)
            t = np.full(u.shape, 1.0, F32)
            for k in range(brazos):
                a = a0 + 2 * math.pi * k / brazos + rng.normal(0, 0.15)
                s = u * math.cos(a) + v * math.sin(a)
                d = -u * math.sin(a) + v * math.cos(a)
                ancho = 0.55 * (1 - np.clip(s / R, 0, 1)) ** 0.7 + 0.12
                dentro = (s > 0) & (s < R) & (np.abs(d) < ancho)
                cob |= dentro
                t = np.where(dentro, np.minimum(t, np.clip(s / R, 0, 1)), t)
            centro = (u * u + v * v) < 0.7 ** 2
            cob |= centro
            t = np.where(centro, 0.0, t)
            alt = 0.0016 * (1 - 0.55 * t)
            return cob, alt, t
        a, hh, t = sprite_supermuestreado(lado, f)
        tipos["estrella"].append(sp.anadir(a, hh, {"t": t}))
    # frondas plumosas (pinnadas), algo curvas
    for _ in range(64):
        L = rng.uniform(6.0, 14.0)
        ang = rng.uniform(0, 2 * math.pi)
        curva = rng.normal(0, 0.08)
        lado = int(2 * math.ceil(L * 0.6) + 4)

        def f(u, v, L=L, ang=ang, curva=curva):
            x = u * math.cos(ang) + v * math.sin(ang) + L * 0.5
            y = -u * math.sin(ang) + v * math.cos(ang)
            y = y - curva * (x ** 2) / L
            s = np.clip(x / L, 0, 1)
            eje = (x > 0) & (x < L) & (np.abs(y) < 0.38 * (1 - 0.5 * s) + 0.12)
            cob = eje.copy()
            t = np.where(eje, s, 1.0).astype(F32)
            paso = 1.15
            for i in range(int(L / paso)):
                x0 = (i + 0.5) * paso
                lado_p = 1 if i % 2 == 0 else -1
                lp = 0.42 * L * (1 - x0 / L) ** 0.8 + 0.6
                a = math.radians(50) * lado_p
                du, dv = x - x0, y
                ss = du * math.cos(a) + dv * math.sin(a)
                dd = -du * math.sin(a) + dv * math.cos(a)
                p = (ss > 0) & (ss < lp) & (np.abs(dd) < 0.3 * (1 - ss / lp) + 0.1)
                cob |= p
                t = np.where(p, np.minimum(t, np.clip((x0 / L) * 0.7 + 0.3 * ss / lp, 0, 1)), t)
            alt = 0.0012 * (1 - 0.4 * t)
            return cob, alt, t
        a, hh, t = sprite_supermuestreado(lado, f)
        tipos["pluma"].append(sp.anadir(a, hh, {"t": t}))
    # grano aterciopelado
    for _ in range(16):
        R = rng.uniform(1.0, 1.8)
        lado = int(2 * math.ceil(R) + 3)

        def f(u, v, R=R):
            r = np.sqrt(u * u + v * v) / R
            cob = r < 1
            return cob, 0.0009 * np.sqrt(np.clip(1 - r * r, 0, 1)), r.astype(F32)
        a, hh, t = sprite_supermuestreado(lado, f)
        tipos["terciopelo"].append(sp.anadir(a, hh, {"t": t}))
    # esporofitos (capsulas sobre setas): puntitos altos
    tipos["capsula"] = []
    for _ in range(6):
        def f(u, v):
            r = np.sqrt(u * u + (v * 1.4) ** 2)
            return r < 0.9, 0.006 * np.ones_like(u), np.zeros_like(u)
        a, hh, t = sprite_supermuestreado(5, f)
        tipos["capsula"].append(sp.anadir(a, hh, {"t": t}))
    return sp.cerrar(), tipos


def generar(ctx):
    n = ctx.n
    rb = ctx.rng("base")
    lz = Lienzo(n)

    # ---------------- sustrato: tierra oscura con alguna piedra bajo el musgo ----------------
    sustrato = 0.012 * ruido_m(ctx, rb, 1.2, 0.12, beta=2.3)
    # piedras bajo el musgo: bultos irregulares (no esferas perfectas)
    rp = ctx.rng("piedras")
    pts_p = rp.random((int(3 * ctx.tam ** 2), 2)) * n
    rad_p = ctx.px(rp.uniform(0.06, 0.18, len(pts_p))).astype(F32)
    wp = ctx.px(0.05)
    vp = voronoi(n, pts_p, warp=(wp * ruido_m(ctx, rp, 0.3, 0.05, beta=2.0), wp * ruido_m(ctx, rp, 0.3, 0.05, beta=2.0)), k=1)
    qp = vp["f1"] / rad_p[vp["id1"]]
    piedras = np.clip(1 - qp ** 2, 0, 1) ** 0.7 * rad_p[vp["id1"]] * ctx.texel * 0.3
    sustrato = sustrato + piedras

    # ---------------- cojines (Voronoi organico) y subgrumos ----------------
    rcj = ctx.rng("cojines")
    pts = puntos_poisson(n, rcj, ctx.px(0.09))
    m = len(pts)
    w = ctx.px(0.025)
    warp = (w * ruido_m(ctx, rcj, 0.2, 0.06, beta=2.2), w * ruido_m(ctx, rcj, 0.2, 0.06, beta=2.2))
    v = voronoi(n, pts, warp=warp, k=2)
    cid = v["id1"]
    borde = v["borde"] * ctx.texel
    H = rcj.uniform(0.010, 0.034, m).astype(F32)
    hueco = (rcj.uniform(0.0, 0.005, m) * (rcj.random(m) < 0.5)).astype(F32)
    perfil = suave(borde - hueco[cid], 0.0, 0.02) ** 0.5
    domo = 1.0 - 0.35 * np.clip(v["f1"] * ctx.texel / 0.06, 0, 1) ** 2
    cojin = H[cid] * perfil * domo
    v2 = voronoi(n, puntos_poisson(n, rcj, ctx.px(0.028)), k=2)
    sub = 0.004 * suave(v2["borde"] * ctx.texel, 0.0, 0.012) ** 0.5
    # calvas sin musgo (tierra desnuda) con borde deshilachado
    rcv = ctx.rng("calvas")
    calva = suave(ruido_m(ctx, rcv, 0.6, 0.08, beta=2.2) + 0.35 * ruido_m(ctx, rcv, 0.03, 0.005, beta=1.0), 1.3, 1.9)
    cubierto = np.clip(perfil * 1.6, 0, 1) * (1 - calva)
    sup = sustrato + (cojin + sub * perfil) * (1 - calva)

    # especie y color por cojin
    nombres = list(ESPECIES)
    pesos = np.array([ESPECIES[k][2] for k in nombres])
    esp_c = rcj.choice(len(nombres), size=m, p=pesos / pesos.sum())
    brillo = np.exp(rcj.normal(0, 0.13, m)).astype(F32)
    amar = rcj.uniform(-0.25, 0.35, m).astype(F32)   # deriva hacia amarillo (+) o verde azulado (-)
    muerto = (rcj.random(m) < 0.08).astype(F32) * rcj.uniform(0.4, 0.9, m).astype(F32)

    # ---------------- base: tierra oscura grumosa con restos ----------------
    m1 = ruido_m(ctx, rb, 0.05, 0.004, beta=1.2)
    grumos = np.maximum(ruido_m(ctx, rb, 0.006, 0.0015, beta=0.3) - 0.3, 0)
    tierra = col(58, 46, 36) * (1 + 0.12 * m1 + 0.12 * grumos)[..., None]
    lz.altura[:] = sustrato + 0.0004 * m1 + 0.0006 * grumos
    lz.albedo[:] = tierra
    lz.suavidad[:] = 0.36 - 0.06 * np.clip(grumos, 0, 1)

    # ---------------- brotes ----------------
    sp, tipos = _sprites_musgo(ctx.rng("sprites"))
    rs = ctx.rng("brotes")
    densidad = {"estrella": 0.20, "pluma": 0.075, "terciopelo": 0.33}  # brotes por px^2
    col_base = {k: col(*ESPECIES[k][0]) for k in nombres}
    col_punta = {k: col(*ESPECIES[k][1]) for k in nombres}
    c_muerto = col(104, 90, 56)
    for ie, nombre in enumerate(nombres):
        cant = int(densidad[nombre] * n * n * (pesos[ie] / pesos.sum()) * 2.6)
        y = rs.random(cant) * n
        x = rs.random(cant) * n
        iy, ix = y.astype(int) % n, x.astype(int) % n
        c_id = cid[iy, ix]
        # densidad proporcional a la cobertura: bordes de cojin y de calvas deshilachados
        p_ok = rs.random(cant) < cubierto[iy, ix] ** 1.5
        ok = (esp_c[c_id] == ie) & p_ok
        # tambien algo de mezcla en bordes de cojin de otras especies
        ok |= (rs.random(cant) < 0.04) & p_ok
        y, x, iy, ix, c_id = y[ok], x[ok], iy[ok], ix[ok], c_id[ok]
        k = len(y)
        sid = rs.choice(tipos[nombre], size=k)
        base = sup[iy, ix] + rs.uniform(-0.0006, 0.0010, k).astype(F32)
        b = brillo[c_id] * np.exp(rs.normal(0, 0.08, k)).astype(F32)
        a_ = amar[c_id]
        cb = col_base[nombre][None, :] * b[:, None]
        cp = col_punta[nombre][None, :] * b[:, None]
        shift = np.stack([1 + 0.25 * a_, 1 + 0.05 * a_, 1 - 0.4 * a_], 1).astype(F32)
        cb, cp = cb * shift, cp * shift
        mu = muerto[c_id][:, None]
        cb = cb + (c_muerto * 0.8 - cb) * mu
        cp = cp + (c_muerto * 1.1 - cp) * mu
        te = sp.extra["t"]

        def color_fn(inst, g, cb=cb, cp=cp, te=te):
            t = te[g][:, None]
            return cb[inst] + (cp[inst] - cb[inst]) * t

        suav = (0.26 + 0.05 * rs.random(k)).astype(F32)
        salpicar(lz, sp, sid, y, x, base, color_fn=color_fn, suavidad=suav)

    # esporofitos en algunos cojines de 'estrella'
    cant = int(250 * ctx.tam ** 2)
    y = rs.random(cant) * n
    x = rs.random(cant) * n
    iy, ix = y.astype(int) % n, x.astype(int) % n
    ok = (esp_c[cid[iy, ix]] == 0) & (cubierto[iy, ix] > 0.5) & (rs.random(cant) < 0.5)
    y, x, iy, ix = y[ok], x[ok], iy[ok], ix[ok]
    k = len(y)
    c_caps = np.where((rs.random(k) < 0.6)[:, None], col(110, 64, 40)[None, :], col(150, 110, 60)[None, :]).astype(F32)
    salpicar(lz, sp, rs.choice(tipos["capsula"], size=k), y, x, sup[iy, ix], color=c_caps,
             suavidad=np.full(k, 0.4, F32))

    # ---------------- restos caidos encima ----------------
    tex = {"moteado": m1, "parches": ruido_m(ctx, rb, 0.07, 0.01, beta=1.8),
           "borde": ruido_m(ctx, rb, 0.05, 0.012, beta=2.0),
           "agujeros_q": np.full((n, n), 9.0, F32), "agujeros_u": np.ones((n, n), F32)}
    rr = ctx.rng("restos")
    for _ in range(int(1.5 * ctx.tam ** 2)):
        largo = ctx.px(rr.uniform(0.04, 0.14))
        c = col(*[(96, 70, 48), (70, 52, 40), (128, 100, 70)][rr.integers(3)])
        hoja = dict(forma="gotero", largo=largo, semiancho=largo / rr.uniform(2.2, 3.2) / 2,
                    angulo=rr.uniform(0, 6.3), curva=rr.normal(0, 0.05), peciolo=largo * 0.08,
                    perfil=perfil_hoja(rr, "gotero"), copa=rr.uniform(0, 0.2), pliegue=0.0, arco=0.0, punta=0.0,
                    nervio=-1.0, color=c, color2=c * 0.65, color_nervio=c * 1.25, parche=1.0,
                    borde_oscuro=0.6, manchas=0.0, agujeros=0.0, irregular=0.05, suav=0.35,
                    rigidez=1.0, apoyo=0.5, corte=None, paralelas=False, n_venas=9, ang_venas=0.8,
                    gradiente=0.0, grosor_m=0.0003)
        estampar_hoja(lz, ctx.texel, *(rr.random(2) * n), hoja, tex)
    for _ in range(int(2.0 * ctx.tam ** 2)):
        c = col(*[(84, 70, 56), (60, 50, 42)][rr.integers(2)])
        r = dict(largo=ctx.px(rr.uniform(0.04, 0.2)), radio=ctx.px(rr.uniform(0.0012, 0.003)),
                 angulo=rr.uniform(0, 6.3), curva=rr.normal(0, 0.08), color=c, color2=c * 1.3,
                 manchas2=0.4, suav=0.3, conicidad=0.3, enterrado=0.2, rigidez=ctx.px(0.02))
        estampar_ramita(lz, ctx.texel, *(rr.random(2) * n), r, tex)

    # ---------------- acabado ----------------
    h = lz.altura
    # el interior del cojin queda en sombra de color: mas oscuro cuanto mas hondo bajo las puntas
    hondo = suave(desenfocar(h, 3.0) - h, 0.0, 0.0015)
    albedo = lz.albedo * (1.0 - 0.15 * hondo)[..., None]
    humedad = suave(ruido_m(ctx, ctx.rng("humedad"), 0.8, 0.1, beta=2.0), 0.3, 1.5)
    suav = lz.suavidad + 0.1 * humedad
    albedo *= tinte_macro(ctx, ctx.rng("tinte"), 0.07)

    return ResultadoCapa(
        albedo=albedo.astype(F32), altura=h.astype(F32), suavidad=suav.astype(F32),
        fuerza_normal=0.8, radio_ao_m=0.05, intensidad_ao=0.75, oscurecer_cavidad=0.3,
        ecualizar_altura=0.4)
