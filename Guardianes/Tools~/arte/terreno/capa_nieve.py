"""Nieve vieja y venteada: el manto con sastrugi (crestas largas que talla el viento), rizos finos de
viento en las zonas sueltas, costras de hielo mas lisas y brillantes, huellas de animales que lo
cruzan, sombras azuladas en lo hondo y lo poco que asoma (agujas de pino, alguna piedra).

Para los mundos frios de la saga (Niflheim y Midgard en invierno, en Fenrir Hunt; el bioma de hielo
del roadmap del cerebro). Los rizos salen de Swift-Hohenberg, como los de la arena.
"""
import math

import numpy as np

from capa_arena import escalar_fft, swift_hohenberg
from elementos import estampar_piedra, estampar_ramita
from exportar import ResultadoCapa
from nucleo import F32, Lienzo, col, desenfocar, gradiente, mezclar, ruido_m, suave, tinte_macro, ventana

TAM_M = 6.0


def _huella(lz, ctx, cy, cx, ang, largo_m, ancho_m, prof_m):
    """Una huella: un hueco ovalado con el borde algo levantado (la nieve que aparta el pie)."""
    n = lz.n
    R = ctx.px(largo_m)
    m = int(R * 1.5 + 3)
    idx = ventana(n, int(cy) - m, int(cx) - m, 2 * m + 1, 2 * m + 1)
    yy, xx = np.mgrid[-m:m + 1, -m:m + 1].astype(F32)
    c, s = math.cos(ang), math.sin(ang)
    u = (xx * c + yy * s) / R
    v = (-xx * s + yy * c) / (ctx.px(ancho_m) + 1e-6)
    d = np.sqrt(u * u + v * v)
    hueco = prof_m * np.clip(1.0 - d * d, 0, 1) ** 0.6
    borde = prof_m * 0.25 * np.exp(-((d - 1.15) / 0.18) ** 2)
    lz.altura[idx] = lz.altura[idx] - hueco + borde


def _rastro(lz, ctx, rng):
    """Un rastro de huellas que cruza el tile: de zorro o de liebre, con su paso y su ancho."""
    n = lz.n
    zorro = rng.random() < 0.6
    paso_m = rng.uniform(0.28, 0.38) if zorro else rng.uniform(0.5, 0.9)
    largo_m, ancho_m = (0.05, 0.035) if zorro else (0.08, 0.04)
    ang = rng.uniform(0, 2 * math.pi)
    y, x = rng.random(2) * n
    giro = rng.normal(0, 0.15)
    pasos = int(ctx.tam / paso_m * rng.uniform(0.5, 1.2))
    for k in range(pasos):
        ang += giro * 0.08 + rng.normal(0, 0.05)
        lado = (1 if k % 2 else -1) * ctx.px(0.04 if zorro else 0.07)
        py = y + math.sin(ang + math.pi / 2) * lado
        px = x + math.cos(ang + math.pi / 2) * lado
        _huella(lz, ctx, py, px, ang, largo_m, ancho_m, rng.uniform(0.012, 0.025))
        if not zorro:
            # la liebre: las dos traseras juntas, delante de las delanteras
            _huella(lz, ctx, py + math.sin(ang) * ctx.px(0.12), px + math.cos(ang) * ctx.px(0.12), ang, 0.11, 0.045, 0.02)
        y += math.sin(ang) * ctx.px(paso_m)
        x += math.cos(ang) * ctx.px(paso_m)


def generar(ctx):
    n = ctx.n
    rb = ctx.rng("base")
    lz = Lienzo(n)
    ang_viento = 0.6

    # ---------------- el manto: ondulacion, sastrugi y costras ----------------
    undul = 0.012 * ruido_m(ctx, rb, 3.0, 0.6, beta=2.2)
    # Sastrugi: crestas largas a lo ancho del viento, afiladas (el valor absoluto del ruido estirado).
    cresta = ruido_m(ctx, ctx.rng("sastrugi"), 2.6, 0.18, beta=2.1, anis=(7.0, ang_viento + math.pi / 2))
    sastrugi = 0.018 * (1.0 - np.abs(np.tanh(cresta * 1.1))) ** 2 * suave(ruido_m(ctx, rb, 2.4, 0.6, beta=2.0), -0.8, 1.0)
    # Las costras: placas mas lisas, donde la nieve se derritio y volvio a helar.
    costra = suave(ruido_m(ctx, ctx.rng("costra"), 1.6, 0.25, beta=2.0), 0.5, 1.2)

    # ---------------- rizos de viento en la nieve suelta ----------------
    m = 512 if n >= 1024 else n // 2
    lam_m = 0.07
    while m / ctx.ciclos(lam_m) < 4.0:
        m *= 2
    u = swift_hohenberg(m, m / ctx.ciclos(lam_m), ctx.rng("rizos"), ang=ang_viento, pasos=240)
    u = escalar_fft(u, n)
    rizos = 0.0014 * np.clip(u / (np.percentile(np.abs(u), 90) + 1e-6), -1.2, 1.2) * (1.0 - costra)

    fino = ruido_m(ctx, rb, 0.006, 0.0029, beta=0.0)
    lz.altura[:] = undul + sastrugi + rizos

    # ---------------- huellas ----------------
    rr = ctx.rng("rastros")
    for _ in range(rr.integers(1, 4)):
        _rastro(lz, ctx, rr)

    # ---------------- lo que asoma ----------------
    tex = {"fino": fino, "moteado": ruido_m(ctx, rb, 0.02, 0.004, beta=1.2), "parches": ruido_m(ctx, rb, 0.08, 0.01, beta=1.8)}
    ra = ctx.rng("asoma")
    for _ in range(int(0.5 * ctx.tam ** 2)):
        c = col(*[(64, 70, 44), (88, 72, 50)][ra.integers(2)])
        r = dict(largo=ctx.px(ra.uniform(0.02, 0.045)), radio=ctx.px(ra.uniform(0.0007, 0.0012)),
                 angulo=ra.uniform(0, 6.3), curva=ra.normal(0, 0.05), color=c, color2=c * 0.85, manchas2=0.2,
                 suav=0.3, conicidad=0.3, enterrado=ra.uniform(0.4, 0.7), rigidez=4)
        estampar_ramita(lz, ctx.texel, *(ra.random(2) * n), r, tex, modo="enterrado")
    for _ in range(int(0.08 * ctx.tam ** 2)):
        y, x = ra.random(2) * n
        R = ctx.px(ra.uniform(0.02, 0.06))
        p = dict(radio=R, elong=ra.uniform(0.6, 1), angulo=ra.uniform(0, 3.14), armonicos=[(2, 0.12, ra.uniform(0, 6))],
                 alto_rel=0.5, exp=2.0, hundido=R * ctx.texel * 0.6, color=col(92, 92, 96), suav=0.35, base="local")
        estampar_piedra(lz, ctx.texel, y, x, p, tex, modo="max")

    # ---------------- color: blanco, azulado en lo hondo y a la sombra de las crestas ----------------
    h = lz.altura
    hondo = suave(-(h - desenfocar(h, ctx.px(0.25))) / 0.006, 0.0, 1.0)
    gx, gy = gradiente(h)
    # La cara de sotavento de las crestas (mira lejos de la luz habitual), algo mas azul.
    sombra = suave(-(gx * math.cos(ang_viento) + gy * math.sin(ang_viento)) / ctx.texel, 0.05, 0.4)
    c_nieve = col(238, 242, 247)
    c_azul = col(206, 220, 240)
    c_vieja = col(222, 222, 220)
    v_macro = ruido_m(ctx, rb, 2.0, 0.4, beta=2.0)
    alb = mezclar(np.broadcast_to(c_nieve, (n, n, 3)).astype(F32), c_vieja, (suave(v_macro, 0.4, 1.8) * 0.4)[..., None])
    alb = mezclar(alb, c_azul, np.clip(0.3 * hondo + 0.3 * sombra, 0, 0.6)[..., None])
    # Las costras, algo mas grises y transparentes (se ve el hielo).
    alb = mezclar(alb, col(224, 230, 236), (costra * 0.2)[..., None])
    # Lo asomado ya trae su color en el lienzo: donde hay algo, manda el lienzo.
    hay = np.clip(lz.albedo.sum(axis=2) * 4.0, 0, 1)[..., None]
    alb = mezclar(alb, lz.albedo, hay)
    alb = alb * (1.0 + 0.025 * fino)[..., None]
    alb = alb * tinte_macro(ctx, ctx.rng("tinte"), 0.025)

    # La nieve suelta es mate; la costra brilla (y el brillo cambia mucho de un sitio a otro: la escarcha).
    destello = suave(ruido_m(ctx, ctx.rng("destello"), 0.004, 0.0029, beta=0.0), 1.6, 2.6)
    suav = 0.22 + 0.4 * costra + 0.25 * destello
    suav = np.where(hay[..., 0] > 0.5, lz.suavidad, suav)

    return ResultadoCapa(
        albedo=alb.astype(F32), altura=lz.altura.astype(F32), suavidad=suav.astype(F32),
        fuerza_normal=0.8, radio_ao_m=0.06, intensidad_ao=0.45, oscurecer_cavidad=0.06,
        micro_normal=(0.00008 * fino).astype(F32), ecualizar_altura=0.3,
        notas="Nieve vieja venteada: sastrugi, rizos, costras y huellas")
