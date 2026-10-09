"""Arena de playa/rio tropical: grano fino beige grisaceo, ondulaciones (ripples) suaves con
bifurcaciones naturales, fragmentos de concha, coral y granos volcanicos oscuros.

Las ondulaciones salen de resolver Swift-Hohenberg en un dominio periodico (espectral): da
crestas sinuosas con dislocaciones en 'Y' como las reales. Luego se les da perfil asimetrico
(barlovento suave, sotavento empinado) y los minerales pesados oscuros se acumulan en los senos.
"""
import math

import numpy as np
import scipy.fft as sfft

from elementos import estampar_piedra, estampar_ramita
from exportar import ResultadoCapa
from nucleo import (F32, Lienzo, col, desenfocar, gradiente, mezclar, ruido_m, suave, tinte_macro,
                    ventana)

TAM_M = 6.0


def swift_hohenberg(m, longitud_px, rng, r=0.3, anis=0.15, ang=0.0, pasos=260, dt=0.6,
                    sinuosidad=9.0, ruido_ini=0.15):
    """Patron de franjas periodico con defectos. longitud_px: longitud de onda en px (malla m).

    Se parte de una onda plana (frecuencia entera => periodica) con la fase deformada por un
    ruido suave y algo de ruido blanco; la dinamica SH relaja la curvatura y crea las
    dislocaciones ('Y') donde la fase se aleja demasiado del numero de onda preferido.
    """
    k0 = 2 * math.pi / longitud_px
    fy = (np.fft.fftfreq(m) * 2 * math.pi / k0)[:, None]
    fx = (np.fft.rfftfreq(m) * 2 * math.pi / k0)[None, :]
    c, s = math.cos(ang), math.sin(ang)
    kpar = fx * c + fy * s
    kper = -fx * s + fy * c
    K2 = kpar ** 2 + kper ** 2
    L = r - (1.0 - K2) ** 2 - anis * kper ** 2
    den = (1.0 - dt * L).astype(np.float64)
    ciclos = m / longitud_px
    cx, cy = round(ciclos * c), round(ciclos * s)
    Y, X = np.mgrid[0:m, 0:m] / m
    from nucleo import ruido
    phi = sinuosidad * ruido(m, rng, 2.0, 7.0, beta=3.0)
    u = 0.8 * np.cos(2 * math.pi * (cx * X + cy * Y) + phi) + ruido_ini * rng.standard_normal((m, m))
    U = sfft.rfft2(u, workers=-1)
    for _ in range(pasos):
        u = sfft.irfft2(U, s=(m, m), workers=-1)
        U = (U + dt * sfft.rfft2(-u ** 3, workers=-1)) / den
    return sfft.irfft2(U, s=(m, m), workers=-1)


def escalar_fft(u, n):
    """Remuestreo periodico exacto de m x m a n x n: relleno de ceros en frecuencia si n > m y,
    si n < m, se quedan las frecuencias que caben (un paso bajo ideal)."""
    m = u.shape[0]
    U = sfft.fft2(u, workers=-1)
    V = np.zeros((n, n), complex)
    h = min(m, n) // 2
    V[:h, :h] = U[:h, :h]
    V[:h, -h:] = U[:h, -h:]
    V[-h:, :h] = U[-h:, :h]
    V[-h:, -h:] = U[-h:, -h:]
    return (sfft.ifft2(V, workers=-1).real * (n / m) ** 2).astype(F32)


def _concha(lz, ctx, rng, cy, cx, tex):
    """Fragmento de concha bivalva: abanico con costillas radiales, roto por una linea."""
    n = lz.n
    R = ctx.px(math.exp(rng.uniform(math.log(0.003), math.log(0.022))))
    m = int(R + 3)
    idx = ventana(n, int(cy) - m, int(cx) - m, 2 * m + 1, 2 * m + 1)
    yy, xx = np.mgrid[-m:m + 1, -m:m + 1].astype(F32)
    ang = rng.uniform(0, 2 * math.pi)
    u = xx * math.cos(ang) + yy * math.sin(ang) + R * 0.5
    v = -xx * math.sin(ang) + yy * math.cos(ang)
    rr = np.hypot(u, v)
    phi = np.arctan2(v, u)
    apertura = rng.uniform(0.6, 1.2)
    Rphi = R * (1.0 - 0.12 * (phi / apertura) ** 2)
    dentro = np.minimum(Rphi - rr, (apertura - np.abs(phi)) * rr)
    # rotura: corte recto irregular
    ca = rng.uniform(0, 2 * math.pi)
    corte = (xx * math.cos(ca) + yy * math.sin(ca)) - rng.uniform(-0.3, 0.5) * R + 0.8 * tex["fino"][idx]
    dentro = np.minimum(dentro, corte)
    alfa = np.clip(dentro + 0.5, 0, 1)
    if alfa.max() < 0.05:
        return
    q = np.clip(rr / R, 0, 1)
    costillas = 0.5 + 0.5 * np.cos(phi * rng.uniform(10, 22))
    hz = R * ctx.texel * rng.uniform(0.15, 0.35) * (1 - q * q) + 0.00025 * costillas * (rng.random() < 0.7)
    tipo = rng.random()
    if tipo < 0.55:
        c = col(214, 206, 190)
    elif tipo < 0.75:
        c = col(196, 170, 150)
    elif tipo < 0.9:
        c = col(170, 164, 156)
    else:
        c = col(120, 104, 96)
    c = c * math.exp(rng.normal(0, 0.06))
    colr = c[None, None, :] * (1 - 0.12 * costillas * q)[..., None] * (1 + 0.05 * tex["fino"][idx])[..., None]
    h0 = lz.altura[idx]
    base = float(np.mean(h0)) - R * ctx.texel * rng.uniform(0.05, 0.3)
    lz.componer_max(idx, alfa.astype(F32), (base + hz).astype(F32), colr.astype(F32),
                    np.full(alfa.shape, 0.42, F32), suav_borde=0.0002)


def generar(ctx):
    n = ctx.n
    rb = ctx.rng("base")
    lz = Lienzo(n)

    # ---------------- ripples (Swift-Hohenberg) ----------------
    m = 512 if n >= 1024 else n // 2
    lam_m = 0.115  # longitud de onda media de los ripples
    # La dinamica necesita unos 4 px por longitud de onda: por debajo de 2 (Nyquist) el termino
    # cubico revienta (NaN en todo el tile a 128 px). Con tiles pequenos se resuelve en una malla
    # mas fina y se reduce despues; a 1024 px o mas no cambia nada.
    while m / ctx.ciclos(lam_m) < 4.0:
        m *= 2
    ang_viento = 0.35
    u = swift_hohenberg(m, m / ctx.ciclos(lam_m), ctx.rng("ripples"), ang=ang_viento, pasos=300)
    u = escalar_fft(u, n)
    k0 = 2 * math.pi / ctx.px(lam_m)
    gx, gy = gradiente(u)  # por px
    wx, wy = math.cos(ang_viento), math.sin(ang_viento)
    gpar = gx * wx + gy * wy
    gmag = np.sqrt(gx * gx + gy * gy)
    A = np.sqrt(u * u + (gmag / k0) ** 2) + 1e-6
    cos_t = u / A
    sin_t = -np.sign(gpar) * gmag / k0 / A
    theta = np.arctan2(sin_t, cos_t)
    perfil = np.cos(theta - 0.55 * np.cos(theta))  # sotavento mas empinado
    An = np.clip(A / np.percentile(A, 70), 0, 1.2)
    # zonas mas lisas (pisoteo, secado) y zonas con ripples marcados
    modul = suave(ruido_m(ctx, ctx.rng("modul"), 1.5, 0.3, beta=2.0), -1.3, 0.9)
    amp = 0.0036 * (0.4 + 0.6 * modul)
    ripple = amp * An * perfil

    # ondulacion de fondo muy suave: el relieve grande lo pone la malla del terreno, y aqui
    # solo delataria la repeticion del tile
    undul = 0.003 * ruido_m(ctx, rb, 2.0, 0.8, beta=2.2) + 0.0005 * ruido_m(ctx, rb, 0.25, 0.05, beta=2.0)
    fino = ruido_m(ctx, rb, 0.006, 0.0029, beta=0.0)
    tex = {"fino": fino, "moteado": ruido_m(ctx, rb, 0.02, 0.004, beta=1.2),
           "parches": ruido_m(ctx, rb, 0.08, 0.01, beta=1.8)}
    lz.altura[:] = undul + ripple

    # ---------------- color del grano ----------------
    c_base = col(176, 164, 142)
    c_clara = col(196, 186, 166)
    c_osc = col(84, 80, 76)       # granos volcanicos
    c_rojiza = col(170, 146, 120)
    v_macro = ruido_m(ctx, rb, 1.5, 0.3, beta=2.0)
    v_med = ruido_m(ctx, rb, 0.3, 0.05, beta=1.8)
    alb = mezclar(c_base, c_clara, (suave(v_macro, -0.5, 1.8) * 0.35)[..., None])
    alb = mezclar(alb, c_rojiza, (suave(v_med, 1.0, 2.4) * 0.15)[..., None])
    # minerales pesados en los senos de los ripples y en vetas
    seno = suave(-perfil, 0.2, 0.9) * An * (0.25 + 0.75 * modul)
    vetas_osc = suave(ruido_m(ctx, rb, 0.6, 0.05, beta=1.8, anis=(3.0, ang_viento + math.pi / 2)), 0.8, 2.0)
    oscuro = np.clip(0.3 * seno + 0.25 * vetas_osc, 0, 0.6)
    alb = mezclar(alb, mezclar(alb, c_osc, 0.5), oscuro[..., None])
    # grano: sal y pimienta a nivel de pixel (cada pixel = decenas de granos)
    g2 = ruido_m(ctx, rb, 0.004, 0.0029, beta=0.0)
    alb = alb * (1.0 + 0.07 * fino)[..., None]
    alb = mezclar(alb, c_osc * 0.8, (suave(g2, 1.7, 2.6) * (0.5 + 0.8 * oscuro))[..., None])
    alb = mezclar(alb, c_clara * 1.1, (suave(-g2, 2.0, 2.8) * 0.6)[..., None])
    lz.albedo[:] = alb
    hum = suave(ruido_m(ctx, ctx.rng("humedad"), 2.0, 0.4, beta=2.2), 0.6, 1.9) * 0.45
    lz.suavidad[:] = 0.16 + 0.04 * v_med + 0.06 * seno

    # ---------------- conchas, coral y granos gruesos ----------------
    rc = ctx.rng("conchas")
    for _ in range(int(55 * ctx.tam ** 2)):
        y, x = rc.random(2) * n
        _concha(lz, ctx, rc, y, x, tex)
    for _ in range(int(1.2 * ctx.tam ** 2)):
        c = col(220, 214, 200) * rc.uniform(0.85, 1.0)
        r = dict(largo=ctx.px(rc.uniform(0.015, 0.05)), radio=ctx.px(rc.uniform(0.0015, 0.0035)),
                 angulo=rc.uniform(0, 6.3), curva=rc.normal(0, 0.1), color=c, color2=c * 0.9,
                 manchas2=0.3, suav=0.3, conicidad=0.3, enterrado=rc.uniform(0.3, 0.6), rigidez=3)
        estampar_ramita(lz, ctx.texel, *(rc.random(2) * n), r, tex, modo="enterrado")
    # gravilla volcanica dispersa (1-4 mm)
    for _ in range(int(160 * ctx.tam ** 2)):
        y, x = rc.random(2) * n
        R = ctx.px(rc.uniform(0.0012, 0.0035))
        c = col(*[(62, 60, 60), (90, 84, 80), (120, 100, 84)][rc.integers(3)])
        p = dict(radio=R, elong=rc.uniform(0.6, 1), angulo=rc.uniform(0, 3.14), armonicos=[(2, 0.1, rc.uniform(0, 6))],
                 alto_rel=0.6, exp=2.0, hundido=R * ctx.texel * 0.25, color=c, suav=0.3, base="local")
        estampar_piedra(lz, ctx.texel, y, x, p, tex, modo="max")
    # restos organicos minimos (algas/hojitas secas)
    for _ in range(int(0.6 * ctx.tam ** 2)):
        c = col(*[(70, 60, 48), (52, 48, 40)][rc.integers(2)])
        r = dict(largo=ctx.px(rc.uniform(0.02, 0.08)), radio=ctx.px(rc.uniform(0.0008, 0.002)),
                 angulo=rc.uniform(0, 6.3), curva=rc.normal(0, 0.2), ondulacion=0.05, ondas=3, fase=0,
                 color=c, color2=c, manchas2=0.0, suav=0.4, conicidad=0.2, enterrado=0.5, rigidez=3)
        estampar_ramita(lz, ctx.texel, *(rc.random(2) * n), r, tex, modo="enterrado")

    # ---------------- humedad (zonas mas oscuras y algo menos rugosas) ----------------
    albedo = lz.albedo * (1.0 - 0.18 * hum)[..., None]
    suav = lz.suavidad + 0.14 * hum
    albedo *= tinte_macro(ctx, ctx.rng("tinte"), 0.04)
    micro = 0.00012 * fino  # rugosidad de grano solo para la normal

    return ResultadoCapa(
        albedo=albedo.astype(F32), altura=lz.altura.astype(F32), suavidad=suav.astype(F32),
        fuerza_normal=1.0, radio_ao_m=0.05, intensidad_ao=0.8, oscurecer_cavidad=0.2,
        micro_normal=micro.astype(F32), ecualizar_altura=0.3)
