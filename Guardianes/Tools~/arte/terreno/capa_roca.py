"""Roca volcanica (basalto) para cortados y pendientes fuertes.

Estructura (como se rompe un macizo de basalto real):
  * dos familias de diaclasas casi rectas (frecuencias enteras => periodicas) que cortan el
    macizo en bloques romboidales irregulares; cada bloque tiene su desnivel y su inclinacion
    (repisas y escalones) y las juntas se abren en grietas de anchura variable;
  * caras de fractura planas a dos escalas (facetado Voronoi: cada celda es el plano tangente
    de una superficie suave, con un giro aleatorio) -> aristas y escalones angulosos;
  * fracturas secundarias, aristas redondeadas por la meteorizacion, vesiculas.
Color: basalto gris oscuro, patina parda/gris en caras expuestas, oxido saliendo de juntas,
tierra y musgo en grietas, regueros de humedad y liquenes crustaceos en caras expuestas.
"""
import math

import numpy as np

from exportar import ResultadoCapa
from nucleo import (F32, cavidad, col, desenfocar, gradiente, mezclar, puntos_poisson, ruido_m,
                    suave, tinte_macro, voronoi)

TAM_M = 8.0


def _hash01(a, b):
    """Hash entero -> [0,1) determinista para pares de enteros (arrays)."""
    h = (a.astype(np.int64) * 73856093) ^ (b.astype(np.int64) * 19349663)
    h = (h ^ (h >> 13)) * 1274126177
    h = h ^ (h >> 16)
    return ((h & 0xFFFFFF).astype(np.float64) / float(0x1000000)).astype(F32)


def _diaclasas(ctx, rng, kA, kB, warp_amp):
    """Dos familias de juntas periodicas. kA, kB: vectores enteros de frecuencia (ciclos/tile).

    Devuelve distancia (px) a la junta mas cercana de cada familia y numeros aleatorios por
    bloque (bloque = paralelogramo entre juntas), coherentes con la periodicidad del tile.
    El alabeo es suave y de baja frecuencia para que las juntas no se plieguen en bucles.
    """
    n = ctx.n
    yy, xx = np.mgrid[0:n, 0:n].astype(np.float64)
    wa = warp_amp * ruido_m(ctx, rng, ctx.tam / 2, ctx.tam / 10, beta=3.0).astype(np.float64)
    wb = warp_amp * ruido_m(ctx, rng, ctx.tam / 2, ctx.tam / 10, beta=3.0).astype(np.float64)
    wa += 0.006 * ruido_m(ctx, rng, 0.5, 0.08, beta=2.0)  # irregularidad fina del trazo
    wb += 0.006 * ruido_m(ctx, rng, 0.5, 0.08, beta=2.0)
    fa = (kA[0] * xx + kA[1] * yy) / n + wa
    fb = (kB[0] * xx + kB[1] * yy) / n + wb
    sa = n / math.hypot(*kA)
    sb = n / math.hypot(*kB)
    da = (np.abs(fa - np.round(fa)) * sa).astype(F32)
    db = (np.abs(fb - np.round(fb)) * sb).astype(F32)
    ia, ib = np.floor(fa), np.floor(fb)
    # centro del bloque en el plano (sin warp) -> envuelto en el toro -> hash
    M = np.array([[kA[0], kA[1]], [kB[0], kB[1]]], np.float64) / n
    Mi = np.linalg.inv(M)
    cx = Mi[0, 0] * (ia + 0.5) + Mi[0, 1] * (ib + 0.5)
    cy = Mi[1, 0] * (ia + 0.5) + Mi[1, 1] * (ib + 0.5)
    kx = np.round(np.mod(cx, n) * 16).astype(np.int64) % (n * 16)
    ky = np.round(np.mod(cy, n) * 16).astype(np.int64) % (n * 16)
    r1 = _hash01(kx, ky)
    r2 = _hash01(ky + 7919, kx + 104729)
    r3 = _hash01(kx + 31337, ky + 4242)
    # coordenadas locales (m) respecto al centro del bloque, para inclinarlo
    dxm = ((xx - cx + n / 2) % n - n / 2) * ctx.texel
    dym = ((yy - cy + n / 2) % n - n / 2) * ctx.texel
    return da, db, r1, r2, r3, dxm.astype(F32), dym.astype(F32)


def _facetas(ctx, rng, sep_m, giro, salto_m, sup=None):
    """Caras de fractura: cada celda Voronoi es un plano con inclinacion y desnivel aleatorios
    (relativos a la superficie suave 'sup', si se da: plano tangente en la semilla). Los bordes
    quedan como aristas y escalones. Alabeo de baja frecuencia => poligonos de lados casi rectos."""
    n = ctx.n
    pts = puntos_poisson(n, rng, ctx.px(sep_m))
    w = ctx.px(sep_m * 0.12)
    warp = (w * ruido_m(ctx, rng, sep_m * 3, sep_m * 0.8, beta=2.5),
            w * ruido_m(ctx, rng, sep_m * 3, sep_m * 0.8, beta=2.5))
    v = voronoi(n, pts, warp=warp, k=2)
    m = len(pts)
    sx = rng.normal(0, giro, m)
    sy = rng.normal(0, giro, m)
    off = rng.normal(0, salto_m, m)
    if sup is not None:
        gx, gy = gradiente(sup)
        iy = pts[:, 0].astype(int) % n
        ix = pts[:, 1].astype(int) % n
        sx = sx + gx[iy, ix] / ctx.texel
        sy = sy + gy[iy, ix] / ctx.texel
        off = off + sup[iy, ix]
    idc = v["id1"]
    yy, xx = np.mgrid[0:n, 0:n].astype(F32)
    dy = (yy - pts[idc, 0] + n / 2) % n - n / 2
    dx = (xx - pts[idc, 1] + n / 2) % n - n / 2
    h = off[idc] + (sx[idc] * dx + sy[idc] * dy) * ctx.texel
    return h.astype(F32), v["borde"] * ctx.texel, idc


def generar(ctx):
    n = ctx.n
    rb = ctx.rng("base")

    # ---------------- superficie de fondo ----------------
    macro = 0.08 * ruido_m(ctx, rb, 4.0, 0.8, beta=2.6)

    # ---------------- diaclasas y bloques ----------------
    rj = ctx.rng("juntas")
    da, db, r1, r2, r3, dxm, dym = _diaclasas(ctx, ctx.rng("diaclasas"), (7, 3), (-2, 6), 0.12)
    desnivel = 0.13 * (r1 - 0.5)
    inclin = 0.14 * (r2 - 0.5) * dxm + 0.14 * (r3 - 0.5) * dym
    bloques = desnivel + inclin
    # abertura de las juntas: varia a lo largo del trazo, muchas se cierran (solo queda el
    # escalon entre bloques) => no se lee como una cuadricula
    abre_a = np.clip(0.55 + 0.6 * ruido_m(ctx, rj, 1.2, 0.2, beta=2.2) + 0.4 * (r2 - 0.5), 0, 1.3)
    abre_b = np.clip(0.45 + 0.6 * ruido_m(ctx, rj, 1.2, 0.2, beta=2.2) + 0.4 * (r3 - 0.5), 0, 1.3)
    ancho_a = ctx.px(0.013) * abre_a
    ancho_b = ctx.px(0.013) * abre_b
    red = ctx.px(0.035)
    grieta_a = (1.0 - suave(da, ancho_a, ancho_a + red * (0.4 + abre_a))) * 0.10 * abre_a
    grieta_b = (1.0 - suave(db, ancho_b, ancho_b + red * (0.4 + abre_b))) * 0.10 * abre_b
    juntas = np.maximum(grieta_a, grieta_b)

    # ---------------- facetas de fractura a dos escalas ----------------
    f1, borde_f1, id_f1 = _facetas(ctx, ctx.rng("facetas1"), 0.32, 0.10, 0.008, sup=macro)
    f2, borde_f2, _ = _facetas(ctx, ctx.rng("facetas2"), 0.10, 0.08, 0.002)
    # aristas de las facetas algo redondeadas (meteorizacion)
    h = desenfocar(f1 + f2, 1.0) + bloques - juntas
    # fracturas secundarias finas dentro de algunos bloques
    _, borde_s, id_s = _facetas(ctx, ctx.rng("secundarias"), 0.22, 0.0, 0.0)
    activa = (r1 > 0.6).astype(F32) * suave(ruido_m(ctx, rj, 0.6, 0.1, beta=2.0), 0.0, 0.8)
    fr_sec = (1.0 - suave(borde_s, 0.002, 0.006)) * 0.01 * activa
    h = h - fr_sec
    # rugosidad fina (grano) sin abollar
    h = h + 0.0012 * ruido_m(ctx, rb, 0.03, 0.006, beta=1.2) + 0.0004 * ruido_m(ctx, rb, 0.008, 0.004, beta=0.5)

    # vesiculas por zonas (coladas mas porosas)
    rv = ctx.rng("vesiculas")
    cant = int(700 * ctx.tam ** 2)
    pv = rv.random((cant, 2)) * n
    radios = np.exp(rv.uniform(math.log(ctx.px(0.0016)), math.log(ctx.px(0.006)), cant)).astype(F32)
    vv = voronoi(n, pv, k=1)
    q = vv["f1"] / radios[vv["id1"]]
    zona_ves = suave(ruido_m(ctx, rv, 1.2, 0.15, beta=2.0), 0.0, 1.0) * (r3 > 0.3)
    poro = (1.0 - suave(q, 0.7, 1.0)) * zona_ves
    h = h - 0.004 * poro

    # ---------------- color ----------------
    rc = ctx.rng("color")
    c_fresco = col(58, 58, 60)
    c_meteor = col(96, 93, 88)
    c_pardo = col(102, 88, 74)
    c_oxido = col(114, 76, 54)
    c_tierra = col(54, 45, 37)
    c_musgo = col(62, 72, 36)
    # exposicion a partir de la altura suavizada (el grano fino no debe salpicar las mascaras)
    h_suave = desenfocar(h, ctx.px(0.012))
    loc = h_suave - desenfocar(h, ctx.px(0.2))
    expuesto = suave(loc, -0.004, 0.025)
    arista = suave(loc, 0.01, 0.04)
    cav_g = cavidad(h_suave, ctx.texel, 0.12)
    m1 = ruido_m(ctx, rc, 0.7, 0.05, beta=1.8)
    m2 = ruido_m(ctx, rc, 0.06, 0.006, beta=1.2)
    tono_f = rc.random(int(id_f1.max()) + 1).astype(F32)[id_f1]  # cada cara de fractura distinta
    # cada bloque de diaclasa se meteoriza distinto: es lo que lee la estructura a distancia
    meteo = np.clip(0.15 + 0.5 * r1 + 0.15 * tono_f + 0.18 * m1 + 0.28 * expuesto, 0, 1)
    alb = mezclar(c_fresco, c_meteor, meteo[..., None])
    alb = mezclar(alb, c_pardo, (suave(m1 + 1.4 * r2, 1.1, 2.1) * 0.6)[..., None])
    alb = alb * (1.0 + 0.08 * m2 + 0.06 * (tono_f - 0.5))[..., None]
    # aristas vivas algo mas claras (polvo/meteorizacion)
    alb = alb * (1.0 + 0.18 * arista)[..., None]
    # oxido saliendo de juntas
    d_junta = np.minimum(da, db)
    cerca = 1.0 - suave(d_junta, 0.0, ctx.px(0.12))
    oxido = suave(ruido_m(ctx, rc, 0.25, 0.02, beta=1.6) + 1.6 * cerca, 1.6, 2.6)
    alb = mezclar(alb, c_oxido, (oxido * 0.5)[..., None])
    # granos de feldespato y poros
    gr = ruido_m(ctx, rc, 0.006, 0.0039, beta=0.0)
    alb = mezclar(alb, alb * 1.45, (suave(gr, 2.1, 2.8) * 0.45)[..., None])
    alb = alb * (1.0 - 0.45 * poro)[..., None]
    # tierra y musgo en juntas abiertas y huecos
    tierra = np.clip(juntas / 0.04 + 0.7 * suave(cav_g, 0.35, 0.85), 0, 1)
    tierra = tierra * suave(ruido_m(ctx, rc, 0.4, 0.05, beta=2.0), -1.8, -0.2)
    alb = mezclar(alb, c_tierra * (1 + 0.15 * m2[..., None]), (tierra * 0.85)[..., None])
    musgo = tierra * suave(ruido_m(ctx, rc, 0.8, 0.08, beta=2.0), 0.0, 1.0)
    alb = mezclar(alb, c_musgo * (1 + 0.25 * m2[..., None]), (musgo * 0.8)[..., None])
    h = h + 0.003 * musgo * (1 + 0.3 * m2)

    # ---------------- liquenes crustaceos en caras expuestas ----------------
    rl = ctx.rng("liquenes")
    cant = int(10 * ctx.tam ** 2)
    pl = rl.random((cant, 2)) * n
    rad = np.exp(rl.uniform(math.log(ctx.px(0.015)), math.log(ctx.px(0.09)), cant)).astype(F32)
    tipo = rl.random(cant)
    # alabeo en dos escalas: lobulos (suave) + borde fractal fino de amplitud < 1 px de onda
    w1, w2 = ctx.px(0.015), ctx.px(0.003)
    warp = (w1 * ruido_m(ctx, rl, 0.15, 0.04, beta=2.2) + w2 * ruido_m(ctx, rl, 0.03, 0.012, beta=1.5),
            w1 * ruido_m(ctx, rl, 0.15, 0.04, beta=2.2) + w2 * ruido_m(ctx, rl, 0.03, 0.012, beta=1.5))
    vl = voronoi(n, pl, warp=warp, k=1)
    ql = vl["f1"] / rad[vl["id1"]]
    t_l = tipo[vl["id1"]]
    # costra continua con borde lobulado nitido; algunas con el centro viejo mas apagado
    opac = 0.65 + 0.35 * suave(ruido_m(ctx, rl, 0.06, 0.02, beta=1.8), -1.0, 1.0)
    cubre = (1.0 - suave(ql, 0.93, 1.0)) * opac * suave(expuesto, 0.05, 0.5) * (1 - tierra)
    anillo = 1.0 - 0.45 * (1 - suave(ql, 0.25, 0.6)) * (t_l < 0.35)
    cubre = cubre * anillo
    c_l1, c_l2, c_l3, c_l4 = col(150, 152, 140), col(178, 176, 166), col(142, 146, 96), col(166, 110, 58)
    c_lq = np.where((t_l < 0.45)[..., None], c_l1, np.where((t_l < 0.78)[..., None], c_l2,
                                                           np.where((t_l < 0.97)[..., None], c_l3, c_l4)))
    alb = mezclar(alb, c_lq * (1.0 + 0.1 * m2)[..., None], (cubre * 0.8)[..., None])
    h = h + 0.0006 * cubre

    # ---------------- suavidad y regueros de humedad ----------------
    suav = 0.30 + 0.04 * m2 - 0.08 * meteo
    suav = suav + (0.45 - suav) * tierra * 0.5
    suav = suav + (0.12 - suav) * cubre
    reguero = suave(ruido_m(ctx, rc, 0.6, 0.04, beta=1.8, anis=(5.0, math.pi / 2)), 1.1, 2.1) * (1 - cubre)
    suav = suav + (0.6 - suav) * reguero * 0.7
    alb = alb * (1.0 - 0.2 * reguero)[..., None]

    alb *= tinte_macro(ctx, ctx.rng("tinte"), 0.05)
    return ResultadoCapa(
        albedo=alb.astype(F32), altura=h.astype(F32), suavidad=suav.astype(F32),
        fuerza_normal=0.75, radio_ao_m=0.25, intensidad_ao=1.0, oscurecer_cavidad=0.3,
        ecualizar_altura=0.3)
