"""Suelo contaminado junto a la industria humana.

Tierra compactada y polvorienta, gris parda, con grandes zonas empapadas de crudo (oscuras,
satinadas, con cerco mas negro en el frente de avance y halo donde el aceite se ha absorbido),
charcos negros muy brillantes en lo hondo, salpicaduras, ventisqueros de ceniza gris clara con
bordes suaves, hojas quemadas (carbonizadas negras, 'fantasmas' de ceniza gris que conservan la
forma de la hoja y chamuscadas con borde negro), trozos de carbon, chapas oxidadas que manchan de
oxido la tierra a su alrededor, escamas de oxido y algun plastico descolorido.
"""
import math

import numpy as np

from elementos import estampar_hoja, estampar_ramita, perfil_hoja
from exportar import ResultadoCapa
from nucleo import (F32, Lienzo, col, desenfocar, mezclar, ruido_m, suave, tinte_macro, ventana,
                    voronoi)

TAM_M = 4.0


def _fragmento(lz, ctx, rng, cy, cx, tam_m, color_fn, suav, grosor_m, combado=0.0):
    """Placa poligonal convexa irregular (chapa, escama, plastico, carbon)."""
    n = lz.n
    R = ctx.px(tam_m) * 0.5
    k = int(rng.integers(4, 8))
    angs = np.sort(rng.uniform(0, 2 * math.pi, k))
    radios = R * rng.uniform(0.55, 1.0, k)
    alarg = rng.uniform(0.4, 1.0)
    giro = rng.uniform(0, 2 * math.pi)
    px_ = radios * np.cos(angs)
    py_ = radios * np.sin(angs) * alarg
    vx = px_ * math.cos(giro) - py_ * math.sin(giro)
    vy = px_ * math.sin(giro) + py_ * math.cos(giro)
    m = int(R + 3)
    idx = ventana(n, int(cy) - m, int(cx) - m, 2 * m + 1, 2 * m + 1)
    yy, xx = np.mgrid[-m:m + 1, -m:m + 1].astype(F32)
    d = np.full(yy.shape, -1e9, F32)
    for i in range(k):
        x0, y0 = vx[i], vy[i]
        x1, y1 = vx[(i + 1) % k], vy[(i + 1) % k]
        ex, ey = x1 - x0, y1 - y0
        L = math.hypot(ex, ey) + 1e-6
        s = -((xx - x0) * ey - (yy - y0) * ex) / L   # >0 fuera (vertices en sentido antihorario)
        d = np.maximum(d, s)
    alfa = np.clip(-d + 0.5, 0, 1)
    if alfa.max() < 0.05:
        return None, None
    tx, ty = rng.normal(0, 0.12, 2)
    r2 = (xx * xx + yy * yy) / max(R * R, 1.0)
    z = grosor_m + (tx * xx + ty * yy) * ctx.texel + combado * R * ctx.texel * (1 - r2)
    h0 = lz.altura[idx]
    base = float(np.mean(h0))
    colr = color_fn(idx, alfa.shape)
    a = lz.componer_max(idx, alfa.astype(F32), (base + z).astype(F32), colr.astype(F32),
                        np.full(alfa.shape, suav, F32), suav_borde=0.0003)
    return idx, a


def generar(ctx):
    n = ctx.n
    rb = ctx.rng("base")
    lz = Lienzo(n)

    # ---------------- suelo: tierra compactada, polvorienta, con huellas de rodadas suaves ----------------
    h = (0.012 * ruido_m(ctx, rb, 1.6, 0.25, beta=2.4)
         + 0.0025 * ruido_m(ctx, rb, 0.25, 0.04, beta=2.0)
         + 0.0004 * ruido_m(ctx, rb, 0.03, 0.005, beta=1.2))
    grumos = np.maximum(ruido_m(ctx, rb, 0.008, 0.002, beta=0.4) - 0.4, 0)
    piedrecillas = np.maximum(ruido_m(ctx, rb, 0.01, 0.003, beta=0.2) - 1.8, 0)
    h += 0.0008 * grumos + 0.002 * piedrecillas
    m1 = ruido_m(ctx, rb, 0.6, 0.08, beta=2.0)
    m2 = ruido_m(ctx, rb, 0.05, 0.004, beta=1.2)
    fino = ruido_m(ctx, rb, 0.006, 0.002, beta=0.3)
    # tierra ya ennegrecida por hollin y aceite en toda la zona (el suelo 'sano' no aparece)
    c_tierra = col(80, 71, 62)
    c_tierra2 = col(62, 55, 49)
    alb = mezclar(c_tierra, c_tierra2, suave(m1, -0.8, 1.2)[..., None])
    alb = alb * (1.0 + 0.07 * m2 + 0.05 * fino + 0.1 * grumos)[..., None]
    alb = mezclar(alb, col(104, 98, 90), np.clip(piedrecillas, 0, 1)[..., None])
    lz.altura[:] = h
    lz.albedo[:] = alb
    lz.suavidad[:] = 0.24 + 0.03 * m2

    # ---------------- grado de empapado de crudo (continuo, no a islas) ----------------
    rcr = ctx.rng("crudo")
    campo = ruido_m(ctx, rcr, 1.0, 0.12, beta=2.6) + 0.06 * ruido_m(ctx, rcr, 0.12, 0.02, beta=1.6)
    umbral = 0.35
    crudo = suave(campo, umbral, umbral + 0.06)                    # empapado del todo
    halo = suave(campo, umbral - 0.9, umbral) * (1 - crudo)       # absorcion gradual alrededor
    frente = np.exp(-((campo - umbral - 0.03) / 0.022) ** 2)      # cerco mas negro en el borde
    alb = lz.albedo
    alb = alb * (1.0 - 0.32 * halo)[..., None]
    c_emp = col(28, 25, 23) * (1 + 0.15 * m2[..., None])
    alb = mezclar(alb, c_emp, (crudo * 0.92)[..., None])
    alb = alb * (1.0 - 0.3 * frente * (1 - 0.5 * crudo))[..., None]
    lz.albedo[:] = alb
    lz.suavidad[:] = mezclar(lz.suavidad, 0.42, halo)
    lz.suavidad[:] = mezclar(lz.suavidad, 0.68 + 0.06 * m2, crudo)

    tex = {"moteado": m2, "parches": ruido_m(ctx, rb, 0.07, 0.01, beta=1.8),
           "borde": ruido_m(ctx, rb, 0.05, 0.012, beta=2.0), "arrugas": ruido_m(ctx, rb, 0.014, 0.003, beta=1.3)}
    ra = ctx.rng("agujeros")
    cant = int(1800 * ctx.tam ** 2)
    pa = ra.random((cant, 2)) * n
    rad = np.exp(ra.uniform(math.log(ctx.px(0.002)), math.log(ctx.px(0.012)), cant)).astype(F32)
    w = ctx.px(0.003)
    va = voronoi(n, pa, warp=(w * ruido_m(ctx, ra, 0.04, 0.012, beta=1.6), w * ruido_m(ctx, ra, 0.04, 0.012, beta=1.6)), k=1)
    tex["agujeros_q"] = (va["f1"] / rad[va["id1"]]).astype(F32)
    tex["agujeros_u"] = ra.random(cant).astype(F32)[va["id1"]]

    # ---------------- hojas quemadas ----------------
    rh = ctx.rng("hojas")
    formas = ["gotero", "gotero", "ovada", "lanceolada", "obovada"]
    for _ in range(int(60 * ctx.tam ** 2)):
        forma = formas[rh.integers(len(formas))]
        largo = ctx.px(math.exp(rh.uniform(math.log(0.04), math.log(0.2))))
        tipo = rh.random()
        agujeros = 0.0
        if tipo < 0.45:      # carbonizada: negra, algo satinada, borde de ceniza
            c = col(*[(28, 26, 25), (36, 34, 32), (46, 43, 41)][rh.integers(3)])
            c2 = col(150, 148, 144) * rh.uniform(0.75, 1.0)
            cn = c * 1.5
            borde, parche, s = rh.uniform(0.6, 1.2), rh.uniform(1.2, 2.2), rh.uniform(0.3, 0.42)
            agujeros = rh.uniform(0.1, 0.5) * (rh.random() < 0.6)
        elif tipo < 0.7:     # fantasma de ceniza: gris claro, fragil, con nervios marcados
            c = col(*[(150, 148, 142), (128, 126, 122), (170, 168, 160)][rh.integers(3)])
            c2 = col(70, 68, 66)
            cn = col(60, 58, 56)
            borde, parche, s = rh.uniform(0.0, 0.5), rh.uniform(1.6, 2.6), 0.12
            agujeros = rh.uniform(0.2, 0.7)
        else:                # chamuscada: parda con el borde negro
            c = col(*[(84, 62, 44), (70, 54, 40), (96, 76, 56)][rh.integers(3)])
            c2 = col(26, 24, 22)
            cn = c * 1.25
            borde, parche, s = rh.uniform(1.0, 1.8), rh.uniform(0.6, 1.4), 0.26
        hoja = dict(forma=forma, largo=largo, semiancho=largo / rh.uniform(2.0, 3.6) / 2,
                    angulo=rh.uniform(0, 6.3), curva=rh.normal(0, 0.08), peciolo=largo * 0.08,
                    perfil=perfil_hoja(rh, forma), copa=rh.uniform(0.1, 0.9), pliegue=rh.uniform(0, 0.4),
                    arco=rh.uniform(0, 0.08), punta=rh.uniform(0, 0.15), nervio=rh.choice([-1.0, 1.0]),
                    color=c, color2=c2, color_nervio=cn, parche=parche, venas_col=0.9,
                    borde_oscuro=borde, manchas=0.0, agujeros=agujeros,
                    irregular=rh.uniform(0.06, 0.16), suav=s, rigidez=rh.uniform(1.0, 2.0),
                    apoyo=rh.uniform(0.3, 0.7),
                    corte=(rh.uniform(0.3, 0.8), rh.normal(0, 0.6), rh.choice([-1.0, 1.0])) if rh.random() < 0.5 else None,
                    paralelas=False, n_venas=rh.uniform(7, 12), ang_venas=math.radians(rh.uniform(40, 60)),
                    gradiente=rh.normal(0, 0.1), grosor_m=0.0003, arrugas=rh.uniform(0.8, 1.6))
        estampar_hoja(lz, ctx.texel, *(rh.random(2) * n), hoja, tex)

    # ---------------- ramitas y trozos de carbon ----------------
    rr = ctx.rng("ramas")
    for _ in range(int(3 * ctx.tam ** 2)):
        c = col(*[(26, 24, 23), (36, 33, 31)][rr.integers(2)])
        r = dict(largo=ctx.px(rr.uniform(0.06, 0.35)), radio=ctx.px(rr.uniform(0.002, 0.009)),
                 angulo=rr.uniform(0, 6.3), curva=rr.normal(0, 0.08), color=c, color2=col(110, 106, 100),
                 manchas2=rr.uniform(0.3, 0.8), suav=0.34, conicidad=0.3, enterrado=rr.uniform(0, 0.3),
                 rigidez=ctx.px(0.03))
        estampar_ramita(lz, ctx.texel, *(rr.random(2) * n), r, tex)
    grieta_c = ruido_m(ctx, rr, 0.006, 0.002, beta=0.5)

    def color_carbon(idx, forma):
        return col(30, 28, 27)[None, None, :] * (1 + 0.15 * grieta_c[idx][..., None])

    for _ in range(int(40 * ctx.tam ** 2)):
        _fragmento(lz, ctx, rr, *(rr.random(2) * n), math.exp(rr.uniform(math.log(0.005), math.log(0.025))),
                   color_carbon, 0.36, 0.002, combado=0.25)

    # ---------------- chatarra oxidada (mancha el suelo de oxido) ----------------
    rd = ctx.rng("restos")
    oxido_n = ruido_m(ctx, rd, 0.012, 0.002, beta=1.0)
    oxido_g = ruido_m(ctx, rd, 0.05, 0.01, beta=1.5)

    def color_oxido(idx, forma):
        o = oxido_n[idx] + 0.6 * oxido_g[idx]
        a = col(122, 66, 38)
        b = col(70, 42, 30)
        cc = col(150, 92, 50)
        out = mezclar(b, a, suave(o, -1.0, 0.6)[..., None])
        return mezclar(out, cc, (suave(o, 1.2, 2.2) * 0.6)[..., None])

    mancha_oxido = np.zeros((n, n), F32)
    for i in range(int(2.5 * ctx.tam ** 2)):
        y, x = rd.random(2) * n
        idx, a = _fragmento(lz, ctx, rd, y, x, rd.uniform(0.04, 0.14), color_oxido, 0.32, 0.0015,
                            combado=rd.uniform(-0.2, 0.3))
        if idx is not None:
            mancha_oxido[idx] = np.maximum(mancha_oxido[idx], a)
    for _ in range(int(140 * ctx.tam ** 2)):   # escamas de oxido
        y, x = rd.random(2) * n
        idx, a = _fragmento(lz, ctx, rd, y, x, rd.uniform(0.003, 0.012), color_oxido, 0.26, 0.0005)
        if idx is not None:
            mancha_oxido[idx] = np.maximum(mancha_oxido[idx], a * 0.4)
    plasticos = [col(170, 168, 160), col(70, 96, 128), col(140, 54, 44), col(186, 176, 140)]
    for _ in range(int(1.0 * ctx.tam ** 2)):
        cp = plasticos[rd.integers(len(plasticos))] * rd.uniform(0.75, 1.0)
        sucio = rd.uniform(0.2, 0.6)
        _fragmento(lz, ctx, rd, *(rd.random(2) * n), rd.uniform(0.015, 0.05),
                   lambda idx, forma, cp=cp, s=sucio: mezclar(cp, col(60, 54, 48), (s * suave(m2[idx], -1, 1.5))[..., None]),
                   0.5, 0.0008)
    # aureola de oxido en la tierra alrededor de la chatarra (escurrido por la lluvia)
    aureola = desenfocar(mancha_oxido, ctx.px(0.012)) * 1.6 * (0.7 + 0.3 * suave(oxido_g, -1, 1))
    aureola = np.clip(aureola, 0, 1) * (1 - mancha_oxido)
    lz.albedo[:] = mezclar(lz.albedo, lz.albedo * np.array([1.45, 0.95, 0.7], F32), (aureola * 0.7)[..., None])

    # ---------------- ceniza: ventisqueros suaves (polvo) ----------------
    hh = lz.altura
    rc = ctx.rng("ceniza")
    base_c = ruido_m(ctx, rc, 0.7, 0.1, beta=2.4) + 0.3 * ruido_m(ctx, rc, 0.15, 0.03, beta=2.0)
    hondo = suave(desenfocar(hh, ctx.px(0.03)) - hh, -0.0005, 0.003)
    # ceniza: velo fino que deja ver la tierra (poca opacidad) y alguna acumulacion espesa
    espesor = np.clip(suave(base_c, 0.6, 2.0) * (0.6 + 0.6 * hondo), 0, 1)
    espesor = espesor * (1 - suave(desenfocar(crudo + halo * 0.5, ctx.px(0.02)), 0.0, 0.7))
    espesor = espesor * (0.8 + 0.2 * suave(fino, -1, 1))    # polvo: algo granulado
    c_ceniza = mezclar(col(118, 116, 112), col(150, 148, 142), suave(m2, -0.5, 1.5)[..., None])
    alb = mezclar(lz.albedo, c_ceniza, (espesor * 0.82)[..., None])
    suav = mezclar(lz.suavidad, 0.1, espesor)
    altura = hh + 0.002 * espesor

    # ---------------- charcos de crudo en lo hondo de las manchas ----------------
    hs = desenfocar(altura, ctx.px(0.006))
    cand = hs + 0.03 * (1 - crudo)
    nivel = np.percentile(cand, 10)
    prof = nivel - cand
    charco = suave(prof, 0.0, 0.0008)
    orilla = suave(prof, -0.004, 0.0) * (1 - charco)
    c_crudo = col(12, 11, 11)
    alb = mezclar(alb, c_crudo, charco[..., None])
    alb = alb * (1.0 - 0.4 * orilla)[..., None]
    suav = mezclar(suav, 0.8, orilla)
    suav = mezclar(suav, 0.97, charco)
    altura = np.where(charco > 0, altura + (nivel - 0.03 * (1 - crudo) - altura) * charco, altura)
    # salpicaduras y goterones
    rs = ctx.rng("salpicaduras")
    cant = int(220 * ctx.tam ** 2)
    ps = rs.random((cant, 2)) * n
    rad = np.exp(rs.uniform(math.log(ctx.px(0.002)), math.log(ctx.px(0.018)), cant)).astype(F32)
    w = ctx.px(0.004)
    vs = voronoi(n, ps, warp=(w * ruido_m(ctx, rs, 0.05, 0.015, beta=1.6), w * ruido_m(ctx, rs, 0.05, 0.015, beta=1.6)), k=1)
    cerca_crudo = suave(desenfocar(crudo, ctx.px(0.1)), 0.05, 0.4)
    gota = (1.0 - suave(vs["f1"] / rad[vs["id1"]], 0.8, 1.0)) * (rs.random(cant) < 0.7)[vs["id1"]] * cerca_crudo
    alb = mezclar(alb, c_crudo * 1.5, (gota * 0.9)[..., None])
    suav = mezclar(suav, 0.85, gota)

    alb *= tinte_macro(ctx, ctx.rng("tinte"), 0.05)
    return ResultadoCapa(
        albedo=alb.astype(F32), altura=altura.astype(F32), suavidad=suav.astype(F32),
        fuerza_normal=1.0, radio_ao_m=0.05, intensidad_ao=1.0, oscurecer_cavidad=0.3,
        ecualizar_altura=0.3)
