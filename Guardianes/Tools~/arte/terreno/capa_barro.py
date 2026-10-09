"""Barro tropical: suelo oscuro y empapado con charcos, zonas altas mas secas con costra
agrietada, pisadas/huellas difusas (terreno pisoteado), piedrecitas y raices semienterradas y
algun resto de hoja hundido en el lodo.

Los charcos se calculan al final: todo lo que queda por debajo de un nivel de agua (con el
relieve ya hecho) se inunda con una lamina plana, muy lisa y oscura; lo que sobresale (piedras,
raices, crestas) queda fuera del agua de forma natural. Alrededor, una orla de barro empapado.
"""
import math

import numpy as np

from elementos import estampar_hoja, estampar_piedra, estampar_ramita, perfil_hoja
from exportar import ResultadoCapa
from nucleo import (F32, Lienzo, col, desenfocar, mezclar, ruido_m, suave, tinte_macro, ventana,
                    voronoi)

TAM_M = 4.0


def _pisadas(ctx, lz, rng, cantidad):
    """Depresiones someras con reborde (barro desplazado): terreno pisoteado sin huellas
    reconocibles que delaten la repeticion."""
    n = ctx.n
    for _ in range(cantidad):
        L = ctx.px(rng.uniform(0.08, 0.26))
        W = L * rng.uniform(0.4, 0.75)
        ang = rng.uniform(0, math.pi)
        prof = rng.uniform(0.003, 0.008)
        m = int(L * 1.6) + 2
        cy, cx = rng.random(2) * n
        idx = ventana(n, int(cy) - m, int(cx) - m, 2 * m + 1, 2 * m + 1)
        yy, xx = np.mgrid[-m:m + 1, -m:m + 1].astype(F32)
        u = xx * math.cos(ang) + yy * math.sin(ang)
        v = -xx * math.sin(ang) + yy * math.cos(ang)
        r = np.sqrt((u / L) ** 2 + (v / W) ** 2)
        hueco = -prof * np.clip(1 - r ** 2, 0, 1) ** 0.7
        reborde = prof * 0.25 * np.exp(-((r - 1.2) / 0.3) ** 2)
        lz.altura[idx] += (hueco + reborde).astype(F32)


def generar(ctx):
    n = ctx.n
    rb = ctx.rng("base")
    lz = Lienzo(n)

    # ---------------- relieve del lodo: viscoso, liso, con pliegues suaves ----------------
    # poco relieve de gran escala (charcos pequenos y numerosos, sin un motivo que se repita)
    h = (0.007 * ruido_m(ctx, rb, 1.6, 0.3, beta=2.4)
         + 0.004 * ruido_m(ctx, rb, 0.5, 0.12, beta=2.4)
         + 0.00008 * ruido_m(ctx, rb, 0.03, 0.006, beta=1.4))
    # pliegues de barro removido (crestas redondeadas y anchas)
    h += 0.0012 * (1.0 - np.abs(ruido_m(ctx, rb, 0.35, 0.1, beta=2.4))) ** 2
    lz.altura[:] = h
    _pisadas(ctx, lz, ctx.rng("pisadas"), int(38 * (ctx.tam / 4) ** 2))
    h = desenfocar(lz.altura, 1.0)  # lodo viscoso: sin aristas vivas
    lz.altura[:] = h

    # ---------------- zonas algo mas secas (crestas) con costra agrietada ----------------
    rel = h - desenfocar(h, ctx.px(0.35))
    seco = suave(0.8 * ruido_m(ctx, ctx.rng("seco"), 1.0, 0.25, beta=2.2) + 25 * rel, 0.6, 1.4)
    rg = ctx.rng("grietas")
    pts = rg.random((int(260 * ctx.tam ** 2), 2)) * n
    w = ctx.px(0.006)
    warp = (w * ruido_m(ctx, rg, 0.2, 0.04, beta=2.2), w * ruido_m(ctx, rg, 0.2, 0.04, beta=2.2))
    v = voronoi(n, pts, warp=warp, k=2)
    borde = v["borde"]
    ancho = 0.4 + 2.0 * seco
    grieta = (1.0 - suave(borde, ancho * 0.5, ancho * 0.5 + 1.0)) * suave(seco, 0.2, 0.55)
    # placas algo abarquilladas (bordes levantados)
    placa = suave(seco, 0.3, 0.8) * (1 - suave(borde, 1.0, ctx.px(0.025))) * 0.0010
    lz.altura[:] = h - grieta * 0.004 * suave(seco, 0.2, 0.7) + placa

    # ---------------- color base ----------------
    rc = ctx.rng("color")
    c_humedo = col(86, 67, 52)
    c_rojizo = col(102, 70, 51)
    c_seco = col(120, 102, 84)
    c_grieta = col(48, 38, 31)
    m1 = ruido_m(ctx, rc, 0.6, 0.08, beta=2.0)
    m2 = ruido_m(ctx, rc, 0.05, 0.004, beta=1.2)
    fino = ruido_m(ctx, rc, 0.006, 0.002, beta=0.3)
    alb = mezclar(c_humedo, c_rojizo, suave(m1, -0.4, 1.6)[..., None] * 0.6)
    alb = mezclar(alb, c_seco * (1 + 0.08 * m1[..., None]), (seco * 0.8)[..., None])
    alb = alb * (1.0 + 0.06 * m2 + 0.035 * fino)[..., None]
    alb = mezclar(alb, c_grieta, (grieta * 0.85)[..., None])
    lz.albedo[:] = alb
    lz.suavidad[:] = mezclar(0.54 + 0.06 * m2 + 0.04 * m1, 0.26 + 0.04 * m2, seco)

    # ---------------- piedrecitas, raices y restos ----------------
    tex = {"moteado": m2, "parches": ruido_m(ctx, rc, 0.07, 0.01, beta=1.8),
           "borde": ruido_m(ctx, rc, 0.05, 0.012, beta=2.0),
           "agujeros_q": np.full((n, n), 9.0, F32), "agujeros_u": np.ones((n, n), F32)}
    rp = ctx.rng("piedras")
    for _ in range(int(70 * ctx.tam ** 2)):
        R = ctx.px(math.exp(rp.uniform(math.log(0.003), math.log(0.018))))
        c = col(*[(86, 80, 74), (110, 100, 88), (70, 62, 56), (120, 92, 70)][rp.integers(4)])
        c = c * math.exp(rp.normal(0, 0.12))
        p = dict(radio=R, elong=rp.uniform(0.55, 0.95), angulo=rp.uniform(0, 3.14),
                 armonicos=[(2, rp.uniform(0, 0.12), rp.uniform(0, 6)), (3, rp.uniform(0, 0.08), rp.uniform(0, 6))],
                 alto_rel=rp.uniform(0.5, 0.8), exp=2.2, hundido=R * ctx.texel * rp.uniform(0.2, 0.6),
                 color=c, suav=0.45, base="local")
        p["textura"] = lambda idx, rho, perfil, cc, u, v: (
            mezclar(cc, c_humedo * 0.9, (1 - suave(perfil, 0.0, 0.5))[..., None] * 0.7), 0.0,
            (0.45 + 0.3 * (1 - suave(perfil, 0.0, 0.5))).astype(F32))
        estampar_piedra(lz, ctx.texel, *(rp.random(2) * n), p, tex, modo="max")
    rr = ctx.rng("raices")
    for _ in range(int(7 * ctx.tam ** 2)):
        c = col(*[(78, 56, 42), (64, 48, 38), (92, 72, 56)][rr.integers(3)]) * rr.uniform(0.85, 1.1)
        r = dict(largo=ctx.px(rr.uniform(0.12, 0.6)), radio=ctx.px(rr.uniform(0.002, 0.008)),
                 angulo=rr.uniform(0, 6.3), curva=rr.normal(0, 0.12), ondulacion=rr.uniform(0.01, 0.04),
                 ondas=rr.uniform(1, 3), fase=rr.uniform(0, 6.3), color=c, color2=c * 0.75, manchas2=0.7,
                 suav=0.5, conicidad=rr.uniform(0.2, 0.7), enterrado=rr.uniform(0.45, 0.8), rigidez=3)
        estampar_ramita(lz, ctx.texel, *(rr.random(2) * n), r, tex, modo="enterrado")
    rh = ctx.rng("hojas")
    for _ in range(int(2.5 * ctx.tam ** 2)):
        largo = ctx.px(rh.uniform(0.05, 0.16))
        c = col(*[(60, 46, 36), (76, 58, 42), (48, 40, 34)][rh.integers(3)])
        hoja = dict(forma="gotero", largo=largo, semiancho=largo / rh.uniform(2.2, 3.2) / 2,
                    angulo=rh.uniform(0, 6.3), curva=rh.normal(0, 0.05), peciolo=largo * 0.08,
                    perfil=perfil_hoja(rh, "gotero"), copa=0.0, pliegue=0.0, arco=0.0, punta=0.0,
                    nervio=-1.0, color=c, color2=c * 0.7, color_nervio=c * 1.25, parche=1.0,
                    borde_oscuro=0.6, manchas=0.0, agujeros=0.0, irregular=0.06, suav=0.55,
                    rigidez=0.5, apoyo=0.0, corte=(rh.uniform(0.3, 0.8), rh.normal(0, 0.5), 1.0) if rh.random() < 0.5 else None,
                    paralelas=False, n_venas=9, ang_venas=0.8, gradiente=0.0, grosor_m=0.0003)
        estampar_hoja(lz, ctx.texel, *(rh.random(2) * n), hoja, tex)

    # ---------------- charcos ----------------
    h = lz.altura
    hs = desenfocar(h, ctx.px(0.006))
    # nivel que inunda ~16% del tile, pero solo en las zonas humedas (no secas)
    cand = hs + 0.02 * seco
    nivel = np.percentile(cand, 16)
    prof = nivel - cand
    agua = suave(prof, 0.0, 0.0012)
    orilla = suave(prof, -0.006, 0.0) * (1 - agua)  # barro empapado alrededor
    # cerco de limo fino depositado justo en la orilla (algo mas claro)
    cerco = np.exp(-((prof + 0.0018) / 0.0009) ** 2) * (1 - agua)
    c_agua = col(50, 41, 32)
    profundidad = suave(prof, 0.0, 0.01)
    alb_agua = mezclar(lz.albedo * 0.6, c_agua, profundidad[..., None])
    albedo = mezclar(lz.albedo, alb_agua, agua[..., None])
    albedo = albedo * (1.0 - 0.3 * orilla)[..., None]
    albedo = mezclar(albedo, col(98, 84, 68), (cerco * 0.35)[..., None])
    altura = np.where(agua > 0, h + (nivel - 0.02 * seco - h) * agua, h)
    suav = mezclar(lz.suavidad, 0.8, orilla)
    suav = mezclar(suav, 0.96, agua)
    # pelicula de agua en zonas humedas bajas (brillo sin charco)
    pel = suave(prof, -0.012, -0.004) * (1 - seco) * (1 - agua)
    suav = suav + (0.75 - suav) * pel * 0.6

    albedo *= tinte_macro(ctx, ctx.rng("tinte"), 0.05)
    return ResultadoCapa(
        albedo=albedo.astype(F32), altura=altura.astype(F32), suavidad=suav.astype(F32),
        fuerza_normal=1.0, radio_ao_m=0.05, intensidad_ao=1.0, oscurecer_cavidad=0.3,
        ecualizar_altura=0.3)
