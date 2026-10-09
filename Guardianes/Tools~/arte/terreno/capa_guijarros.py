"""Lecho de rio: cantos rodados de tamanos mezclados semienterrados en arena/limo humedo.

Orden: arena base -> cantos y piedras grandes (sin solaparse demasiado) -> guijarros -> relleno
de arena entre piedras (las 'entierra') -> grava fina -> restos vegetales -> humedad.
Litologia de isla volcanica: basaltos y andesitas dominan, con algo de arenisca, piedras
ferruginosas, verdosas, granito y cuarzo. Cada canto lleva su textura (motas, vesiculas, vetas,
bandas) y la parte baja mojada (mas oscura y brillante).
"""
import math

import numpy as np

from elementos import estampar_hoja, estampar_piedra, estampar_ramita, perfil_hoja
from exportar import ResultadoCapa
from nucleo import (F32, Lienzo, col, desenfocar, mezclar, ruido_m, suave, tinte_macro, voronoi)

TAM_M = 3.0

# litologia: color sRGB, peso, motas (0-1), vesiculas, vetas, bandas, suavidad seca
ROCAS = {
    "basalto":      ((66, 66, 68), 0.30, 0.10, 0.35, 0.06, 0.0, 0.40),
    "andesita":     ((106, 104, 100), 0.20, 0.55, 0.05, 0.05, 0.0, 0.36),
    "pardo_oscuro": ((86, 74, 64), 0.12, 0.15, 0.0, 0.05, 0.2, 0.36),
    "arenisca":     ((128, 114, 96), 0.08, 0.05, 0.0, 0.0, 0.7, 0.28),
    "granito":      ((140, 134, 128), 0.04, 1.0, 0.0, 0.05, 0.0, 0.38),
    "ferruginosa":  ((110, 92, 78), 0.05, 0.1, 0.0, 0.05, 0.3, 0.30),
    "verdosa":      ((98, 102, 94), 0.03, 0.25, 0.0, 0.15, 0.1, 0.40),
    "gris_claro":   ((132, 130, 126), 0.07, 0.3, 0.0, 0.12, 0.0, 0.38),
    "cuarzo":       ((180, 174, 162), 0.02, 0.0, 0.0, 0.0, 0.0, 0.45),
}


def _texturas(ctx):
    r = ctx.rng("texturas")
    n = ctx.n
    # poros/vesiculas: celdas pequenas
    cant = int(4000 * ctx.tam ** 2)
    pts = r.random((cant, 2)) * n
    radios = np.exp(r.uniform(math.log(ctx.px(0.0005)), math.log(ctx.px(0.0022)), cant))
    v = voronoi(n, pts, k=1)
    return {
        "grano": ruido_m(ctx, r, 0.004, 0.0012, beta=0.0),
        "grano2": ruido_m(ctx, r, 0.006, 0.002, beta=0.5),
        "moteado": ruido_m(ctx, r, 0.03, 0.004, beta=1.5),
        "manchas": ruido_m(ctx, r, 0.05, 0.006, beta=1.8),
        "suave": ruido_m(ctx, r, 0.10, 0.025, beta=2.5),   # abolladuras y deformacion de vetas
        "poros": (v["f1"] / radios[v["id1"]].astype(F32)).astype(F32),
        "borde": ruido_m(ctx, r, 0.05, 0.012, beta=2.0),
        "parches": ruido_m(ctx, r, 0.07, 0.01, beta=1.8),
        "agujeros_q": np.full((n, n), 9.0, F32), "agujeros_u": np.ones((n, n), F32),
    }


def _elegir_roca(rng):
    nombres = list(ROCAS)
    pesos = np.array([ROCAS[k][1] for k in nombres])
    return nombres[rng.choice(len(nombres), p=pesos / pesos.sum())]


def _textura(tex, rng, tipo, c_base, H):
    _, _, motas, vesic, vetas, bandas, s_seca = ROCAS[tipo]
    vesic = vesic * (rng.random() < 0.6)
    tiene_veta = rng.random() < vetas * 2
    ang_v = rng.uniform(0, math.pi)
    frec_v = rng.uniform(1.0, 3.0)
    fase_v = rng.uniform(0, 6.3)
    ancho_v = rng.uniform(0.035, 0.08)
    ang_b = rng.uniform(0, math.pi)
    frec_b = rng.uniform(2.5, 6)
    c_osc = c_base * 0.5
    c_cla = np.minimum(c_base * 1.3 + 0.02, 0.6)
    c_veta = col(196, 190, 178) * rng.uniform(0.85, 1.05)
    # segundo tono del canto (patina, oxido, alteracion) que aparece por zonas
    alt = c_base * np.array([rng.uniform(0.85, 1.2), rng.uniform(0.85, 1.05), rng.uniform(0.7, 1.0)], F32)
    alt = alt * rng.uniform(0.75, 1.15)
    mancha_int = rng.uniform(0.25, 0.7)
    pulido = rng.uniform(0.0, 0.08)
    abollado = rng.uniform(0.02, 0.05) * H
    desp = rng.uniform(0, 400, 2)  # cada canto lee el ruido en otra zona (no se repiten manchas)

    def f(idx, rho, perfil, c, u, v):
        g = tex["grano"][idx]
        m = tex["moteado"][idx]
        ms0 = tex["manchas"][idx]
        out = c * (1.0 + 0.10 * m)[..., None]
        out = out + (alt - out) * (mancha_int * suave(ms0, -0.4, 1.4))[..., None]
        if motas > 0:
            g2 = tex["grano2"][idx]
            osc = suave(g2, 1.5 - 0.5 * motas, 2.2 - 0.5 * motas) * motas
            cla = suave(-g2, 1.9 - 0.4 * motas, 2.6 - 0.4 * motas) * motas
            out = out + (c_osc - out) * (0.8 * osc)[..., None] + (c_cla - out) * (0.6 * cla)[..., None]
        ms = tex["suave"][idx]  # ruido suave (escala de canto) para deformar bandas y vetas
        if bandas > 0:
            b = np.sin(frec_b * (u * math.cos(ang_b) + v * math.sin(ang_b)) * math.pi + 0.5 * ms)
            out = out * (1.0 + 0.07 * bandas * b)[..., None]
        # superficie no perfecta: abolladuras suaves y grano
        dz = 0.00004 * g + abollado * ms * np.clip(perfil * 2, 0, 1)
        if tiene_veta:
            # una veta de cuarzo que cruza el canto (ancho en unidades de radio)
            d = u * math.cos(ang_v) + v * math.sin(ang_v) - (fase_v / 6.3 - 0.5) * 1.2 + 0.06 * ms
            veta = 1.0 - suave(np.abs(d), ancho_v * 0.5, ancho_v)
            out = out + (c_veta - out) * (0.85 * veta)[..., None]
            dz = dz + 0.00012 * veta
        if vesic > 0:
            poro = (1.0 - suave(tex["poros"][idx], 0.7, 1.0)) * (perfil > 0.15)
            poro = poro * suave(m, 0.6 - vesic * 2, 1.2 - vesic * 2)
            out = out * (1.0 - 0.45 * poro)[..., None]
            dz = dz - 0.0006 * poro
        # base mojada: la parte baja del canto (junto a la arena) humeda: oscura y brillante
        moj = 1.0 - suave(perfil, 0.05, 0.5)
        out = out * (1.0 - 0.32 * moj)[..., None]
        sv = s_seca + pulido + (0.74 - s_seca) * moj
        return out, dz, sv.astype(F32)

    return f


class Ocupacion:
    """Mapa de ocupacion periodico (a media resolucion) para empaquetar piedras sin que se
    monten demasiado: acepta una posicion si la fraccion ya ocupada de su huella es baja."""

    def __init__(self, n, escala=2):
        self.e = escala
        self.m = n // escala
        self.occ = np.zeros((self.m, self.m), np.uint8)

    def _huella(self, y, x, r):
        e = self.e
        rr = max(1.0, r / e)
        k = int(math.ceil(rr))
        yy, xx = np.mgrid[-k:k + 1, -k:k + 1]
        dentro = (yy * yy + xx * xx) <= rr * rr
        fy = (int(y / e) + yy[dentro]) % self.m
        fx = (int(x / e) + xx[dentro]) % self.m
        return fy, fx

    def fraccion(self, y, x, r):
        fy, fx = self._huella(y, x, r)
        return self.occ[fy, fx].mean()

    def marcar(self, y, x, r):
        fy, fx = self._huella(y, x, r)
        self.occ[fy, fx] = 1


def _empaquetar(rng, n, ocup, radios, densidad, max_frac, intentos=40):
    """Coloca discos (de mayor a menor) donde la huella esta mayormente libre."""
    pos, rad = [], []
    dmax = densidad.max()
    for r in sorted(radios, reverse=True):
        for _ in range(intentos):
            y, x = rng.random(2) * n
            if rng.random() * dmax > densidad[int(y), int(x)]:
                continue
            if ocup.fraccion(y, x, r * 0.9) <= max_frac:
                ocup.marcar(y, x, r * 0.85)
                pos.append((y, x))
                rad.append(r)
                break
    return np.array(pos), np.array(rad)


def _parametros_canto(ctx, rng, tex, radio_px, aplanado):
    tipo = _elegir_roca(rng)
    while tipo in ("cuarzo", "granito") and radio_px > ctx.px(0.02):
        tipo = _elegir_roca(rng)  # los claros llamativos solo en piedras pequenas
    c = col(*ROCAS[tipo][0]) * math.exp(rng.normal(0, 0.13))
    lum = float(c @ np.array([0.2126, 0.7152, 0.0722], F32))
    c = (lum + (c - lum) * rng.uniform(0.7, 1.15)).astype(F32)
    p = dict(
        radio=radio_px,
        elong=rng.uniform(0.55, 0.95),
        angulo=rng.uniform(0, math.pi),
        armonicos=[(1, rng.uniform(0, 0.07), rng.uniform(0, 6.3)),
                   (2, rng.uniform(0, 0.10), rng.uniform(0, 6.3)),
                   (3, rng.uniform(0, 0.06), rng.uniform(0, 6.3)),
                   (4, rng.uniform(0, 0.035), rng.uniform(0, 6.3)),
                   (5, rng.uniform(0, 0.02), rng.uniform(0, 6.3))],
        alto_rel=rng.uniform(0.35, 0.65) * aplanado,
        exp=rng.uniform(2.0, 2.8),
        color=c, suav=0.38, base=0.0, extra={"piedra": 1.0},
    )
    H = p["alto_rel"] * radio_px * p["elong"] * ctx.texel
    p["textura"] = _textura(tex, rng, tipo, c, H)
    p["hundido"] = H * rng.uniform(0.08, 0.45)
    return p


def generar(ctx):
    n = ctx.n
    tex = _texturas(ctx)
    lz = Lienzo(n, extras=("piedra",))
    rb = ctx.rng("base")

    # ---------------- arena / limo base ----------------
    v1 = ruido_m(ctx, rb, 0.5, 0.02, beta=2.0)
    v2 = ruido_m(ctx, rb, 0.006, 0.0015, beta=0.2)
    lz.altura[:] = 0.002 * ruido_m(ctx, rb, 0.3, 0.01, beta=2.0) + 0.00012 * v2
    c_arena = col(112, 102, 88)
    c_limo = col(126, 118, 104)
    base_col = mezclar(c_arena, c_limo, suave(v1, -0.2, 1.2)[..., None])
    base_col = base_col * (1.0 + 0.10 * v2)[..., None]
    # granos oscuros y claros sueltos
    gr = tex["grano"]
    base_col = mezclar(base_col, base_col * 0.45, (suave(gr, 1.8, 2.4) * 0.8)[..., None])
    base_col = mezclar(base_col, base_col * 1.5, (suave(-gr, 2.0, 2.6) * 0.6)[..., None])
    lz.albedo[:] = base_col
    lz.suavidad[:] = 0.5

    # mapas de clasificacion: zonas de cantos gordos y zonas de grava/arena
    clase = ruido_m(ctx, ctx.rng("clase"), 1.0, 0.15, beta=2.0)
    dens_gruesa = np.clip(1.0 + 0.8 * clase, 0.1, 2.5)
    dens_fina = np.clip(1.0 - 0.6 * clase, 0.2, 2.5)

    # ---------------- cantos, piedras y guijarros empaquetados ----------------
    area = ctx.tam ** 2
    ocup = Ocupacion(n)
    clases = [
        # (diametro min, max m), cobertura objetivo, fraccion max ya ocupada, aplanado, densidad
        ((0.12, 0.22), 0.10, 0.05, 1.0, dens_gruesa),
        ((0.05, 0.12), 0.50, 0.12, 1.0, dens_gruesa * 0.6 + 0.6),
        ((0.02, 0.05), 0.40, 0.25, 0.9, dens_fina * 0.5 + 0.7),
    ]
    rg = ctx.rng("cantos")
    total = 0
    for (d0, d1), cob, max_frac, apl, dens in clases:
        radios, acum = [], 0.0
        while acum < cob * area:
            d = math.exp(rg.uniform(math.log(d0), math.log(d1)))
            radios.append(ctx.px(d / 2))
            acum += math.pi * (d / 2) ** 2 * 0.8
        pos, rad = _empaquetar(rg, n, ocup, radios, dens, max_frac)
        total += len(pos)
        for (y, x), r in zip(pos, rad):
            estampar_piedra(lz, ctx.texel, y, x, _parametros_canto(ctx, rg, tex, r, apl), tex, modo="max")

    # ---------------- relleno de arena entre piedras (las entierra) ----------------
    h = lz.altura
    nivel = np.minimum(desenfocar(h, ctx.px(0.01)) * 0.6, 0.006)
    tapa = suave(nivel - h, 0.0, 0.0012)
    lz.altura[:] = np.maximum(h, nivel)
    lz.albedo[:] = mezclar(lz.albedo, base_col * 0.92, tapa[..., None])
    lz.suavidad[:] = mezclar(lz.suavidad, 0.5, tapa)
    lz.extra["piedra"][:] *= 1.0 - tapa

    # ---------------- grava fina ----------------
    rf = ctx.rng("grava")
    k = int(1500 * area)
    dmax = dens_fina.max()
    for _ in range(k):
        y, x = rf.random(2) * n
        if rf.random() * dmax > dens_fina[int(y), int(x)]:
            continue
        r = ctx.px(math.exp(rf.uniform(math.log(0.005), math.log(0.018))) / 2)
        p = _parametros_canto(ctx, rf, tex, r, 0.9)
        p["base"] = float(lz.altura[int(y), int(x)]) * 0.5
        estampar_piedra(lz, ctx.texel, y, x, p, tex, modo="max")

    # ---------------- restos vegetales arrastrados ----------------
    rv = ctx.rng("restos")
    tex_h = dict(tex, borde=tex["borde"], moteado=tex["moteado"], parches=tex["parches"])
    for _ in range(int(3 * area)):
        largo = ctx.px(rv.uniform(0.04, 0.12))
        c = col(*[(70, 54, 40), (52, 42, 34), (96, 74, 52)][rv.integers(3)])
        hoja = dict(forma="gotero", largo=largo, semiancho=largo / rv.uniform(2.2, 3.2) / 2,
                    angulo=rv.uniform(0, 6.3), curva=rv.normal(0, 0.05), peciolo=largo * 0.08,
                    perfil=perfil_hoja(rv, "gotero"), copa=0.0, pliegue=0.0, arco=0.0, punta=0.0,
                    nervio=-1.0, color=c, color2=c * 0.6, color_nervio=c * 1.3, parche=1.0,
                    borde_oscuro=0.5, manchas=0.0, agujeros=0.0, irregular=0.06, suav=0.6,
                    rigidez=0.6, apoyo=0.1, corte=None, paralelas=False, n_venas=9,
                    ang_venas=0.8, gradiente=0.0, grosor_m=0.0003)
        estampar_hoja(lz, ctx.texel, *(rv.random(2) * n), hoja, tex_h)
    for _ in range(int(4 * area)):
        c = col(*[(84, 70, 56), (60, 50, 42), (120, 106, 88)][rv.integers(3)])
        r = dict(largo=ctx.px(rv.uniform(0.05, 0.2)), radio=ctx.px(rv.uniform(0.0015, 0.004)),
                 angulo=rv.uniform(0, 6.3), curva=rv.normal(0, 0.06), color=c, color2=c * 1.4,
                 manchas2=0.4, suav=0.5, conicidad=0.3, enterrado=0.3, rigidez=ctx.px(0.02))
        estampar_ramita(lz, ctx.texel, *(rv.random(2) * n), r, tex_h, modo="enterrado")

    # ---------------- humedad ----------------
    h = lz.altura
    piedra = lz.extra["piedra"]
    hueco = 1.0 - piedra
    # cerca de las piedras la arena esta mas mojada (capilaridad) y en las zonas bajas
    cerca = desenfocar(piedra, ctx.px(0.006))
    bajo = suave(desenfocar(h, ctx.px(0.1)) - h, -0.001, 0.004)
    zona = suave(ruido_m(ctx, ctx.rng("humedad"), 0.8, 0.1, beta=2.0), -0.6, 0.8)
    moj_arena = np.clip((0.45 + 0.4 * cerca + 0.3 * bajo) * (0.5 + 0.5 * zona), 0, 1) * hueco
    albedo = lz.albedo * (1.0 - 0.35 * moj_arena)[..., None]
    suav = lz.suavidad + (0.68 - lz.suavidad) * moj_arena * 0.85
    # piedras: zonas humedas tambien oscurecen algo los cantos
    albedo = albedo * (1.0 - 0.18 * zona * piedra)[..., None]
    suav = suav + (0.7 - suav) * 0.35 * zona * piedra
    albedo *= tinte_macro(ctx, ctx.rng("tinte"), 0.05)

    return ResultadoCapa(
        albedo=albedo.astype(F32), altura=h.astype(F32), suavidad=suav.astype(F32),
        fuerza_normal=1.0, radio_ao_m=0.04, intensidad_ao=1.3, oscurecer_cavidad=0.35,
        ecualizar_altura=0.35)
