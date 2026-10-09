"""Suelo de claro de selva: hierba corta enmaranada vista desde arriba sobre tierra.

Capas de abajo arriba (composicion por maximo de altura, cada brizna con su color):
  tierra grumosa -> paja muerta tendida -> estolones -> briznas verdes tendidas/arqueadas ->
  treboles y rosetas de hoja ancha por rodales -> briznas erguidas (puntas vistas desde arriba)
  -> briznas secas amarillas sueltas y alguna hoja caida.
Las briznas se rasterizan con supermuestreo (son de 1-3 px de ancho) en una biblioteca de
sprites con varias longitudes, curvaturas y giros; se colocan vectorizadamente por cientos de
miles. La altura relativa (y el AO que sale de ella) da la profundidad del cesped.
"""
import math

import numpy as np

from elementos import estampar_hoja, perfil_hoja
from exportar import ResultadoCapa
from nucleo import (F32, Lienzo, Sprites, col, desenfocar, mezclar, ruido_m, salpicar,
                    sprite_supermuestreado, suave, tinte_macro)

TAM_M = 4.0


def _sprite_brizna(sp, largo, ancho, curva, ang, alto, erguida, pliegue=0.25):
    lado = int(largo + 2 * ancho + 6) | 1

    def f(u, v):
        x = u * math.cos(ang) + v * math.sin(ang)
        y = -u * math.sin(ang) + v * math.cos(ang)
        s = (x + largo / 2) / largo
        yc = curva * largo * (s - 0.5) ** 2 - curva * largo * 0.125
        w = ancho * 0.5 * np.clip(1 - s, 0, 1) ** 0.6 * (0.75 + 0.25 * np.clip(s * 6, 0, 1)) + 0.05
        dy = np.abs(y - yc)
        dentro = (s >= 0) & (s <= 1) & (dy < w)
        if erguida:
            alt = alto * (0.35 + 0.65 * s)
        else:
            alt = alto * (4 * np.clip(s, 0, 1) * np.clip(1 - s, 0, 1)) ** 0.7 * (1 - 0.3 * s)
        alt = alt - pliegue * 0.001 * dy / np.maximum(w, 0.1)  # hoja plegada por el nervio
        return dentro, alt, np.clip(s, 0, 1).astype(F32)
    a, h, t = sprite_supermuestreado(lado, f)
    return sp.anadir(a, h, {"t": t})


def _sprite_trebol(sp, R, ang, alto, rng):
    lado = int(2 * R + 5) | 1
    giros = [ang + k * 2 * math.pi / 3 + rng.normal(0, 0.12) for k in range(3)]

    def f(u, v):
        cob = np.zeros(u.shape, bool)
        t = np.ones(u.shape, F32)
        for a in giros:
            x = u * math.cos(a) + v * math.sin(a)
            y = -u * math.sin(a) + v * math.cos(a)
            el = ((x - R * 0.52) / (R * 0.5)) ** 2 + (y / (R * 0.46)) ** 2 < 1
            muesca = ((x - R * 1.05) ** 2 + y ** 2) < (R * 0.2) ** 2
            d = el & ~muesca
            cob |= d
            t = np.where(d, np.minimum(t, np.clip(x / R, 0, 1)), t)
        alt = alto * (1.0 - 0.25 * t)
        return cob, alt, t
    a, h, t = sprite_supermuestreado(lado, f)
    return sp.anadir(a, h, {"t": t})


def _sprite_roseta(sp, R, hojas, ang, alto, rng):
    lado = int(2 * R + 5) | 1
    giros = [ang + k * 2 * math.pi / hojas + rng.normal(0, 0.2) for k in range(hojas)]
    largos = [R * rng.uniform(0.7, 1.0) for _ in range(hojas)]

    def f(u, v):
        cob = np.zeros(u.shape, bool)
        t = np.ones(u.shape, F32)
        for a, L in zip(giros, largos):
            x = u * math.cos(a) + v * math.sin(a)
            y = -u * math.sin(a) + v * math.cos(a)
            d = ((x - L * 0.55) / (L * 0.5)) ** 2 + (y / (L * 0.24)) ** 2 < 1
            cob |= d
            t = np.where(d, np.minimum(t, np.clip(x / L, 0, 1)), t)
        alt = alto * (0.6 + 0.4 * (1 - t))
        return cob, alt, t
    a, h, t = sprite_supermuestreado(lado, f)
    return sp.anadir(a, h, {"t": t})


def generar(ctx):
    n = ctx.n
    rb = ctx.rng("base")
    lz = Lienzo(n)
    px = ctx.px

    # ---------------- tierra ----------------
    m1 = ruido_m(ctx, rb, 0.05, 0.004, beta=1.2)
    grumos = np.maximum(ruido_m(ctx, rb, 0.008, 0.002, beta=0.4) - 0.3, 0)
    suelo_h = 0.006 * ruido_m(ctx, rb, 1.0, 0.1, beta=2.2) + 0.0008 * grumos
    lz.altura[:] = suelo_h
    lz.albedo[:] = col(70, 55, 42) * (1 + 0.12 * m1 + 0.1 * grumos)[..., None]
    lz.suavidad[:] = 0.3

    # densidad general del cesped (claros de tierra pisoteada) y rodales de trebol
    dens = suave(ruido_m(ctx, ctx.rng("densidad"), 1.0, 0.12, beta=2.0), -1.6, 0.2) * 0.75 + 0.25
    trebol = suave(ruido_m(ctx, ctx.rng("trebol"), 0.7, 0.08, beta=2.0), 0.5, 1.2)
    rosetas = suave(ruido_m(ctx, ctx.rng("rosetas"), 0.9, 0.1, beta=2.0), 0.9, 1.6)

    # ---------------- biblioteca de sprites ----------------
    rs = ctx.rng("sprites")
    sp = Sprites()
    lib = {"paja": [], "estolon": [], "tendida": [], "erguida": [], "trebol": [], "roseta": []}
    for _ in range(48):
        lib["paja"].append(_sprite_brizna(sp, px(rs.uniform(0.04, 0.12)), px(rs.uniform(0.002, 0.004)),
                                          rs.normal(0, 0.25), rs.uniform(0, 2 * math.pi), 0.002, False, 0.1))
    for _ in range(16):
        lib["estolon"].append(_sprite_brizna(sp, px(rs.uniform(0.08, 0.16)), px(rs.uniform(0.0016, 0.0026)),
                                             rs.normal(0, 0.4), rs.uniform(0, 2 * math.pi), 0.0015, False, 0.0))
    for _ in range(80):
        lib["tendida"].append(_sprite_brizna(sp, px(rs.uniform(0.035, 0.10)), px(rs.uniform(0.0045, 0.009)),
                                             rs.normal(0, 0.3), rs.uniform(0, 2 * math.pi), 0.005, False, 0.3))
    for _ in range(56):
        lib["erguida"].append(_sprite_brizna(sp, px(rs.uniform(0.012, 0.035)), px(rs.uniform(0.0045, 0.008)),
                                             rs.normal(0, 0.2), rs.uniform(0, 2 * math.pi), 0.006, True, 0.3))
    for _ in range(24):
        lib["trebol"].append(_sprite_trebol(sp, px(rs.uniform(0.007, 0.014)), rs.uniform(0, 6.3), 0.0015, rs))
    for _ in range(16):
        lib["roseta"].append(_sprite_roseta(sp, px(rs.uniform(0.015, 0.035)), int(rs.integers(4, 8)),
                                            rs.uniform(0, 6.3), 0.002, rs))
    sp.cerrar()
    te = sp.extra["t"]

    def colocar(nombre, por_m2, mapa, z0, z1, c_base, c_punta, var=0.12, suav=(0.28, 0.38), rng=None):
        rng = rng or ctx.rng(nombre)
        cant = int(por_m2 * ctx.tam ** 2)
        y = rng.random(cant) * n
        x = rng.random(cant) * n
        iy, ix = y.astype(int) % n, x.astype(int) % n
        ok = rng.random(cant) < mapa[iy, ix]
        y, x, iy, ix = y[ok], x[ok], iy[ok], ix[ok]
        k = len(y)
        sid = rng.choice(lib[nombre], size=k)
        base = suelo_h[iy, ix] + rng.uniform(z0, z1, k).astype(F32)
        # cada brizna mezcla entre varios tonos de su paleta
        ib = rng.integers(len(c_base), size=k)
        b = np.exp(rng.normal(0, var, k)).astype(F32)[:, None]
        cb = np.asarray(c_base, F32)[ib] * b
        cp = np.asarray(c_punta, F32)[ib] * b

        def color_fn(inst, g, cb=cb, cp=cp):
            t = te[g][:, None]
            return cb[inst] + (cp[inst] - cb[inst]) * t
        s = rng.uniform(*suav, k).astype(F32)
        salpicar(lz, sp, sid, y, x, base, color_fn=color_fn, suavidad=s)
        return k

    c = col
    cuenta = {}
    # paja muerta tendida (capa baja)
    cuenta["paja"] = colocar("paja", 1800, np.clip(dens + 0.35, 0, 1), 0.0, 0.002,
                             [c(128, 110, 80), c(110, 94, 70), c(94, 80, 62), c(78, 66, 52)],
                             [c(150, 132, 96), c(128, 110, 80), c(112, 98, 76), c(92, 80, 62)], suav=(0.22, 0.3))
    cuenta["estolon"] = colocar("estolon", 120, dens, 0.001, 0.003,
                                [c(92, 82, 52), c(80, 92, 48)], [c(110, 110, 64), c(96, 112, 56)])
    # briznas verdes tendidas / arqueadas (capa media, la mas abundante)
    verdes_b = [c(56, 84, 34), c(64, 94, 38), c(76, 100, 40), c(60, 90, 44), c(86, 102, 44)]
    verdes_p = [c(102, 130, 54), c(116, 140, 58), c(128, 142, 62), c(106, 134, 66), c(138, 142, 66)]
    cuenta["tendida"] = colocar("tendida", 5200, dens, 0.002, 0.008, verdes_b, verdes_p)
    # treboles y rosetas por rodales (por encima de la hierba tendida)
    cuenta["trebol"] = colocar("trebol", 4200, trebol * dens, 0.006, 0.013,
                               [c(64, 98, 42), c(56, 88, 40), c(74, 104, 46)],
                               [c(96, 132, 70), c(86, 120, 62), c(104, 136, 72)], var=0.1, suav=(0.35, 0.45))
    cuenta["roseta"] = colocar("roseta", 160, rosetas * dens, 0.003, 0.008,
                               [c(60, 88, 40), c(72, 96, 44)], [c(90, 116, 56), c(104, 124, 60)], suav=(0.35, 0.45))
    # briznas erguidas: puntas iluminadas (capa alta)
    cuenta["erguida"] = colocar("erguida", 4400, dens, 0.006, 0.014, verdes_b, verdes_p, var=0.14)
    # briznas secas amarillas sueltas por encima
    cuenta["seca"] = colocar("tendida", 160, dens, 0.006, 0.012,
                             [c(140, 124, 76), c(156, 140, 88)], [c(170, 154, 100), c(180, 164, 108)],
                             suav=(0.22, 0.3), rng=ctx.rng("secas"))

    # alguna hoja caida de los arboles del borde del claro
    tex = {"moteado": m1, "parches": ruido_m(ctx, rb, 0.07, 0.01, beta=1.8),
           "borde": ruido_m(ctx, rb, 0.05, 0.012, beta=2.0),
           "agujeros_q": np.full((n, n), 9.0, F32), "agujeros_u": np.ones((n, n), F32)}
    rh = ctx.rng("hojas")
    for _ in range(int(0.7 * ctx.tam ** 2)):
        largo = ctx.px(rh.uniform(0.06, 0.16))
        cc = col(*[(104, 82, 60), (86, 66, 50), (118, 98, 72)][rh.integers(3)])
        hoja = dict(forma="gotero", largo=largo, semiancho=largo / rh.uniform(2.2, 3.2) / 2,
                    angulo=rh.uniform(0, 6.3), curva=rh.normal(0, 0.05), peciolo=largo * 0.08,
                    perfil=perfil_hoja(rh, "gotero"), copa=rh.uniform(0, 0.3), pliegue=0.0, arco=0.0, punta=0.0,
                    nervio=-1.0, color=cc, color2=cc * 0.6, color_nervio=cc * 1.2, parche=0.6,
                    borde_oscuro=0.8, manchas=0.0, agujeros=0.0, irregular=0.04, suav=0.35,
                    rigidez=1.2, apoyo=0.6, corte=None, paralelas=False, n_venas=9, ang_venas=0.8,
                    gradiente=0.0, grosor_m=0.0003)
        estampar_hoja(lz, ctx.texel, *(rh.random(2) * n), hoja, tex)
    # algunas briznas erguidas atraviesan las hojas caidas (quedan entre la hierba)
    colocar("erguida", 500, dens, 0.010, 0.016, verdes_b, verdes_p, var=0.14, rng=ctx.rng("erguida2"))

    # ---------------- acabado ----------------
    h = lz.altura
    # lo hondo del cesped queda en penumbra de color (transmision/sombra entre briznas)
    hondo = suave(desenfocar(h, 4.0) - h, 0.0, 0.003)
    albedo = lz.albedo * (1.0 - 0.2 * hondo)[..., None]
    albedo *= tinte_macro(ctx, ctx.rng("tinte"), 0.07)
    return ResultadoCapa(
        albedo=albedo.astype(F32), altura=h.astype(F32), suavidad=lz.suavidad.astype(F32),
        fuerza_normal=0.7, radio_ao_m=0.04, intensidad_ao=0.9, oscurecer_cavidad=0.3,
        ecualizar_altura=0.4, notas=str(cuenta))
