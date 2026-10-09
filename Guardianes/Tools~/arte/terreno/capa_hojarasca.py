"""Hojarasca de selva tropical: hojas caidas superpuestas sobre humus oscuro y humedo.

Pasadas (de abajo arriba, como se forma el mantillo real):
  1. humus base con grumos y raicillas;
  2. restos viejos: hojas oscuras, agujereadas y rotas, blandas (se amoldan); compactado;
  3. ramitas;
  4. hojarasca principal de varias especies (gotero, ovada, lanceolada, acorazonada, palma);
  5. mas ramitas y semillas;
  6. hojas recien caidas encima: algo mas claras (rara vez amarillas/verdes), mas rigidas.
Despues, humedad en los huecos (mas oscuro y brillante) y tinte macro anti-repeticion.
"""
import math

import numpy as np

from elementos import estampar_hoja, estampar_piedra, estampar_ramita, perfil_hoja
from exportar import ResultadoCapa
from nucleo import (F32, Lienzo, col, desenfocar, mezclar, ruido, ruido_m, suave, tinte_macro,
                    voronoi)

TAM_M = 4.0

# Paleta de hoja muerta de selva (sRGB) y pesos por pasada (viejas, principal, nuevas).
# Pardos poco saturados: la hojarasca tropical es mas parda/grisacea que la otonal templada.
PALETA = {
    "canela":     ((150, 124, 94), (0.06, 0.12, 0.17)),
    "ocre":       ((138, 104, 70), (0.06, 0.12, 0.16)),
    "rojizo":     ((112, 70, 50), (0.08, 0.13, 0.12)),
    "cobre":      ((126, 82, 56), (0.04, 0.06, 0.09)),
    "chocolate":  ((86, 60, 44), (0.22, 0.20, 0.10)),
    "oscuro":     ((62, 46, 37), (0.26, 0.14, 0.04)),
    "gris_pardo": ((112, 100, 88), (0.12, 0.12, 0.09)),
    "gris_claro": ((136, 126, 112), (0.02, 0.02, 0.03)),
    "negruzco":   ((42, 35, 30), (0.13, 0.04, 0.0)),
    "amarillo":   ((156, 132, 78), (0.0, 0.008, 0.045)),
    "naranja":    ((148, 92, 54), (0.0, 0.01, 0.045)),
    "oliva":      ((110, 102, 64), (0.0, 0.01, 0.03)),
    "verde":      ((84, 92, 52), (0.0, 0.0, 0.012)),
}
LLAMATIVOS = ("amarillo", "naranja", "oliva", "verde", "gris_claro")  # solo en hojas pequenas/medianas

# Especies: forma, largo (m), relacion largo/ancho, peso por pasada (viejas, principal, nuevas)
ESPECIES = [
    ("gotero", (0.10, 0.22), (2.3, 3.2), (0.30, 0.32, 0.32)),
    ("gotero", (0.20, 0.34), (2.2, 3.0), (0.04, 0.08, 0.10)),
    ("ovada", (0.08, 0.16), (1.6, 2.2), (0.12, 0.13, 0.13)),
    ("obovada", (0.08, 0.15), (2.0, 2.8), (0.08, 0.08, 0.08)),
    ("lanceolada", (0.09, 0.20), (3.5, 5.5), (0.12, 0.14, 0.14)),
    ("acorazonada", (0.15, 0.30), (1.1, 1.4), (0.02, 0.03, 0.04)),
    ("gotero", (0.03, 0.07), (2.0, 3.0), (0.30, 0.16, 0.06)),
    ("palma", (0.15, 0.45), (9.0, 16.0), (0.02, 0.06, 0.05)),
]


def _mapa_manchas(ctx, rng, por_m2, r_min_m, r_max_m):
    """Manchas de hongo: 1 en el nucleo, ~0.5 en el halo, 0 fuera. Formas irregulares."""
    n = ctx.n
    cant = int(por_m2 * ctx.tam ** 2)
    pts = rng.random((cant, 2)) * n
    radios = np.exp(rng.uniform(math.log(ctx.px(r_min_m)), math.log(ctx.px(r_max_m)), cant))
    w = ctx.px(r_max_m) * 0.35
    warp = (w * ruido_m(ctx, rng, 0.02, 0.006, beta=1.2), w * ruido_m(ctx, rng, 0.02, 0.006, beta=1.2))
    v = voronoi(n, pts, warp=warp, k=1)
    q = v["f1"] / radios[v["id1"]].astype(F32)
    return (1.0 - 0.5 * suave(q, 0.85, 1.05) - 0.5 * suave(q, 1.5, 1.9)).astype(F32)


def _mapa_agujeros(ctx, rng, por_m2, r_min_m, r_max_m):
    """Agujeros discretos: q = distancia/radio (q<1 dentro) y u = azar por agujero, para que
    cada hoja active una fraccion distinta de ellos."""
    n = ctx.n
    cant = int(por_m2 * ctx.tam ** 2)
    pts = rng.random((cant, 2)) * n
    radios = np.exp(rng.uniform(math.log(ctx.px(r_min_m)), math.log(ctx.px(r_max_m)), cant))
    azar = rng.random(cant).astype(F32)
    w = ctx.px(r_max_m) * 0.3
    warp = (w * ruido_m(ctx, rng, 0.015, 0.005, beta=1.2), w * ruido_m(ctx, rng, 0.015, 0.005, beta=1.2))
    v = voronoi(n, pts, warp=warp, k=1)
    return (v["f1"] / radios[v["id1"]].astype(F32)).astype(F32), azar[v["id1"]]


def _texturas(ctx):
    r = ctx.rng("texturas")
    q, u = _mapa_agujeros(ctx, r, 2500, 0.0012, 0.007)
    return {
        "borde": ruido_m(ctx, r, 0.05, 0.012, beta=2.0),
        "moteado": ruido_m(ctx, r, 0.02, 0.004, beta=1.2),
        "parches": ruido_m(ctx, r, 0.07, 0.01, beta=1.8),
        "tono": ruido_m(ctx, r, 0.10, 0.015, beta=2.0),
        "arrugas": ruido_m(ctx, r, 0.014, 0.003, beta=1.3),
        "ondas": ruido_m(ctx, r, 0.08, 0.02, beta=2.0),
        "agujeros_q": q,
        "agujeros_u": u,
        "manchas": _mapa_manchas(ctx, r, 700, 0.0015, 0.006),
    }


def _elegir_color(rng, pasada, grande=False):
    nombres = list(PALETA)
    pesos = np.array([PALETA[k][1][pasada] * (0.0 if grande and k in LLAMATIVOS else 1.0) for k in nombres])
    pesos = pesos / pesos.sum()
    k = nombres[rng.choice(len(nombres), p=pesos)]
    c = np.array(PALETA[k][0], np.float64)
    k2 = nombres[rng.choice(len(nombres), p=pesos)]
    c = c + (np.array(PALETA[k2][0]) - c) * rng.uniform(0, 0.3)
    # las hojas grandes llaman mas la atencion al repetir el tile: menos dispersion de brillo
    c = col(*np.clip(c, 0, 255)) * math.exp(rng.normal(0, 0.07 if grande else 0.11))
    lum = float(c @ np.array([0.2126, 0.7152, 0.0722], F32))
    c = lum + (c - lum) * 0.85  # algo menos saturado
    if grande:
        c = c * min(1.0, 0.22 / max(lum, 1e-4)) ** 0.5  # sin hojas grandes muy claras
    return c.astype(F32), k


def _parametros_hoja(ctx, rng, pasada, especie):
    forma, (l0, l1), (a0, a1), _ = especie
    largo_m = math.exp(rng.uniform(math.log(l0), math.log(l1)))
    rel = rng.uniform(a0, a1)
    largo = ctx.px(largo_m)
    semi = largo / rel / 2.0
    nuevo = pasada == 2
    viejo = pasada == 0
    c, _ = _elegir_color(rng, pasada, grande=largo_m > 0.15)
    if viejo:
        c = c * 0.88
    # color de deterioro: mas oscuro y algo gris, o decolorado
    if rng.random() < 0.8:
        c2 = c * rng.uniform(0.55, 0.78) * np.array([0.96, 0.98, 1.04], F32)
    else:
        c2 = (c * 0.55 + c.mean() * 0.45) * rng.uniform(1.08, 1.25)
    # tono alternativo dentro de la hoja (rojizo o amarillento)
    c3 = c * (np.array([1.12, 0.95, 0.85], F32) if rng.random() < 0.5 else np.array([1.04, 1.06, 0.9], F32))
    c3 = c3 * rng.uniform(0.9, 1.12)
    if rng.random() < 0.7:
        cn = c * rng.uniform(1.12, 1.4) * np.array([1.04, 1.0, 0.88], F32)
    else:
        cn = c * rng.uniform(0.6, 0.82)
    cara_arriba = rng.random() < 0.6
    if not cara_arriba:  # enves: mas claro y grisaceo, nervios en relieve
        c = c * 1.08 + c.mean() * 0.05
    mordiscos = []
    if rng.random() < (0.45 if viejo else 0.25):
        for _ in range(rng.integers(1, 4)):
            mordiscos.append((rng.uniform(0.15, 0.9), rng.choice([-1.0, 1.0]), semi * rng.uniform(0.15, 0.5)))
    p = dict(
        forma=forma, largo=largo, semiancho=semi,
        angulo=rng.uniform(0, 2 * math.pi),
        curva=rng.normal(0, 0.05 if forma != "palma" else 0.03),
        peciolo=largo * rng.uniform(0.05, 0.15) if forma != "palma" else 0,
        perfil=perfil_hoja(rng, forma),
        copa=rng.uniform(-0.04, 0.08) if viejo else (rng.uniform(0.0, 0.4) if nuevo else rng.uniform(-0.1, 0.25)),
        pliegue=(rng.uniform(0.3, 0.6) if forma == "palma" else
                 (rng.uniform(0.04, 0.22) if rng.random() < 0.3 else 0.0)) * (0.3 if viejo else 1.0),
        arco=rng.uniform(-0.005, 0.01) if viejo else rng.uniform(-0.015, 0.05 if nuevo else 0.03),
        punta=0.0 if viejo else rng.uniform(0.0, 0.1 if nuevo else 0.05) * (rng.random() < 0.45),
        nervio=-1.0 if cara_arriba else 1.0,
        color=c.astype(F32), color2=c2.astype(F32), color3=c3.astype(F32), color_nervio=cn.astype(F32),
        var_tono=rng.uniform(0.2, 0.8),
        venas_col=rng.uniform(0.2, 0.9),
        parche=rng.uniform(0.3, 1.8) if not nuevo else rng.uniform(1.2, 2.6),
        borde_oscuro=rng.uniform(0.2, 1.0) if not nuevo else rng.uniform(0.0, 0.5),
        manchas=rng.uniform(0.4, 1.0) * (rng.random() < (0.4 if not nuevo else 0.15)),
        agujeros=(rng.uniform(0.1, 0.6) if viejo else rng.uniform(0.04, 0.3)) * (rng.random() < (0.6 if viejo else 0.18)),
        irregular=rng.uniform(0.015, 0.05) if not viejo else rng.uniform(0.04, 0.1),
        dientes=rng.uniform(0.5, 1.1) * (rng.random() < 0.14 and forma in ("gotero", "ovada", "obovada")),
        dientes_esp=ctx.px(rng.uniform(0.003, 0.007)),
        arrugas=rng.uniform(0.8, 1.5) if viejo else (rng.uniform(0.2, 0.7) if nuevo else rng.uniform(0.4, 1.1)),
        ondas=rng.uniform(0.0, 1.0) * (0.4 if viejo else 1.0),
        mordiscos=mordiscos,
        suav=(rng.uniform(0.40, 0.52) if nuevo else rng.uniform(0.28, 0.40)) + (0.05 if cara_arriba else -0.03),
        rigidez=rng.uniform(0.5, 1.0) if viejo else (rng.uniform(1.2, 2.2) if nuevo else rng.uniform(0.8, 1.5)),
        apoyo=rng.uniform(0.1, 0.3) if viejo else (rng.uniform(0.45, 0.8) if nuevo else rng.uniform(0.3, 0.6)),
        corte=None,
        paralelas=forma == "palma",
        n_venas=rng.uniform(7, 13),
        ang_venas=math.radians(rng.uniform(38, 62)),
        gradiente=rng.normal(0, 0.12),
        grosor_m=rng.uniform(0.00025, 0.0005),
    )
    p_corte = 0.55 if viejo else (0.22 if not nuevo else 0.06)
    if forma == "palma":
        p_corte = 0.7
    if rng.random() < p_corte:
        p["corte"] = (rng.uniform(0.25, 0.85), rng.normal(0, 0.5), 1.0 if rng.random() < 0.5 else -1.0)
    return p


def _posiciones(rng, n, densidad, cantidad):
    """Posiciones (px) con probabilidad proporcional al mapa de densidad (rechazo)."""
    out = []
    dmax = densidad.max()
    while len(out) < cantidad:
        c = rng.random((cantidad * 2 + 16, 2)) * n
        d = densidad[c[:, 0].astype(int) % n, c[:, 1].astype(int) % n]
        ok = rng.random(len(c)) * dmax < d
        out.extend(c[ok].tolist())
    return np.array(out[:cantidad])


def _pasada_hojas(ctx, lz, tex, rng, pasada, cobertura, densidad):
    n = ctx.n
    pesos = np.array([e[3][pasada] for e in ESPECIES])
    pesos = pesos / pesos.sum()
    hojas, area = [], 0.0
    objetivo = cobertura * n * n
    while area < objetivo:
        esp = ESPECIES[rng.choice(len(ESPECIES), p=pesos)]
        p = _parametros_hoja(ctx, rng, pasada, esp)
        area += 0.62 * p["largo"] * 2 * p["semiancho"] * (0.6 if p["corte"] else 1.0)
        hojas.append(p)
    pos = _posiciones(rng, n, densidad, len(hojas))
    for p, (y, x) in zip(hojas, pos):
        estampar_hoja(lz, ctx.texel, y, x, p, tex)
    return len(hojas)


def _ramitas(ctx, lz, tex, rng, cantidad, gruesas=0):
    colores = [(98, 84, 70), (80, 66, 54), (60, 49, 41), (132, 120, 104), (104, 74, 54), (138, 122, 98)]
    for i in range(cantidad + gruesas):
        gruesa = i >= cantidad
        c = col(*colores[rng.integers(len(colores))]) * math.exp(rng.normal(0, 0.12))
        r = dict(
            largo=ctx.px(rng.uniform(0.3, 0.7) if gruesa else math.exp(rng.uniform(math.log(0.05), math.log(0.38)))),
            radio=ctx.px(rng.uniform(0.007, 0.014) if gruesa else math.exp(rng.uniform(math.log(0.0015), math.log(0.005)))),
            angulo=rng.uniform(0, 2 * math.pi),
            curva=rng.normal(0, 0.07),
            ondulacion=rng.uniform(0, 0.02), ondas=rng.uniform(1, 4), fase=rng.uniform(0, 6.3),
            color=c.astype(F32), color2=(c * rng.uniform(1.25, 1.6)).astype(F32), manchas2=rng.uniform(0.2, 0.8),
            suav=rng.uniform(0.22, 0.36), conicidad=rng.uniform(0.0, 0.5), enterrado=rng.uniform(0.0, 0.2),
            rigidez=ctx.px(0.03),
        )
        y, x = rng.random(2) * ctx.n
        estampar_ramita(lz, ctx.texel, y, x, r, tex)
        if not gruesa and rng.random() < 0.4 and r["largo"] > ctx.px(0.08):
            for _ in range(rng.integers(1, 3)):
                f = rng.uniform(-0.35, 0.35)
                yb = y + math.sin(r["angulo"]) * f * r["largo"]
                xb = x + math.cos(r["angulo"]) * f * r["largo"]
                rb = dict(r)
                rb.update(largo=r["largo"] * rng.uniform(0.2, 0.45), radio=r["radio"] * rng.uniform(0.5, 0.8),
                          angulo=r["angulo"] + rng.choice([-1, 1]) * rng.uniform(0.4, 0.9), curva=rng.normal(0, 0.1))
                desp = rb["largo"] * 0.5
                estampar_ramita(lz, ctx.texel, yb + math.sin(rb["angulo"]) * desp,
                                xb + math.cos(rb["angulo"]) * desp, rb, tex)


def _semillas(ctx, lz, tex, rng, cantidad):
    colores = [(62, 42, 30), (110, 52, 36), (36, 31, 28), (140, 112, 76), (88, 68, 46)]
    for _ in range(cantidad):
        c = col(*colores[rng.integers(len(colores))]) * math.exp(rng.normal(0, 0.15))
        radio = ctx.px(math.exp(rng.uniform(math.log(0.004), math.log(0.015))))
        p = dict(radio=radio, elong=rng.uniform(0.55, 1.0), angulo=rng.uniform(0, 6.3),
                 armonicos=[(2, rng.uniform(0, 0.06), rng.uniform(0, 6.3)), (3, rng.uniform(0, 0.04), rng.uniform(0, 6.3))],
                 alto_rel=rng.uniform(0.6, 0.9), exp=2.0, hundido=0.0, color=c.astype(F32),
                 suav=rng.uniform(0.42, 0.6), base="local")
        p["textura"] = lambda idx, rho, perfil, cc, u, v: (cc * (1.0 - 0.25 * rho ** 3)[..., None]
                                                            * (1 + 0.06 * tex["moteado"][idx])[..., None], 0.0)
        y, x = rng.random(2) * ctx.n
        estampar_piedra(lz, ctx.texel, y, x, p, tex, modo="sobre")


def _compactar(lz, factor):
    """Asentamiento: las capas inferiores se aplastan con el tiempo y la lluvia."""
    m = lz.altura.mean()
    lz.altura[:] = m + (lz.altura - m) * factor


def generar(ctx):
    n = ctx.n
    tex = _texturas(ctx)
    lz = Lienzo(n)
    rb = ctx.rng("base")

    # ---------------- humus base: grumoso, con terrones y fibras ----------------
    hb = (0.003 * ruido_m(ctx, rb, 0.4, 0.02, beta=2.2)
          + 0.001 * ruido_m(ctx, rb, 0.05, 0.004, beta=1.6))
    terrones = np.maximum(ruido_m(ctx, rb, 0.02, 0.005, beta=1.0) + 0.2, 0) ** 1.5 * 0.0011
    grumos = np.maximum(ruido_m(ctx, rb, 0.006, 0.002, beta=0.3) - 0.3, 0) * 0.0008
    lz.altura[:] = hb + terrones + grumos
    v1 = ruido_m(ctx, rb, 0.3, 0.01, beta=1.8)
    v2 = ruido_m(ctx, rb, 0.02, 0.002, beta=0.8)
    c_a, c_b, c_c = col(68, 51, 40), col(48, 38, 31), col(92, 74, 58)
    base_col = mezclar(c_b, c_a, suave(v1, -0.8, 0.9)[..., None])
    base_col = mezclar(base_col, c_c, (suave(v2, 1.0, 2.2) * 0.6)[..., None])
    base_col *= (1.0 + 0.10 * v2 + 0.5 * (grumos / 0.0008) * 0.15)[..., None]
    lz.albedo[:] = base_col
    lz.suavidad[:] = 0.34 + 0.05 * v1 - 0.08 * np.clip(grumos / 0.0008, 0, 1)

    rr = ctx.rng("raicillas")
    for _ in range(int(90 * (TAM_M / 4) ** 2)):
        c = col(*[(70, 50, 38), (96, 70, 50), (50, 40, 34)][rr.integers(3)])
        r = dict(largo=ctx.px(rr.uniform(0.05, 0.25)), radio=ctx.px(rr.uniform(0.0007, 0.0016)),
                 angulo=rr.uniform(0, 6.3), curva=rr.normal(0, 0.15), ondulacion=rr.uniform(0.01, 0.05),
                 ondas=rr.uniform(2, 6), fase=rr.uniform(0, 6.3), color=c, color2=c * 1.3, manchas2=0.3,
                 suav=0.35, conicidad=0.5, enterrado=rr.uniform(0.2, 0.6), rigidez=3)
        estampar_ramita(lz, ctx.texel, *(rr.random(2) * n), r, tex, modo="enterrado")

    # picadillo: fragmentos diminutos de hoja ya casi humus
    rp = ctx.rng("picadillo")
    for _ in range(int(2600 * (TAM_M / 4) ** 2)):
        esp = ("gotero", (0.012, 0.035), (1.2, 2.5), None)
        p = _parametros_hoja(ctx, rp, 0, esp)
        p.update(corte=(rp.uniform(0.3, 0.7), rp.normal(0, 0.7), rp.choice([-1.0, 1.0])),
                 copa=0.0, pliegue=0.0, arco=0.0, punta=0.0, rigidez=0.4, apoyo=0.0,
                 irregular=rp.uniform(0.08, 0.2), peciolo=0.0)
        p["color"] = p["color"] * 0.75
        estampar_hoja(lz, ctx.texel, *(rp.random(2) * n), p, tex)

    # densidad de hojarasca a gran escala (zonas mas pobladas y claros de humus)
    dens = ruido_m(ctx, ctx.rng("densidad"), 0.9, 0.12, beta=2.0)
    densidad = np.clip(1.0 + 0.6 * dens, 0.15, 2.2)

    # ---------------- pasadas de hojas ----------------
    cuenta = {}
    cuenta["viejas"] = _pasada_hojas(ctx, lz, tex, ctx.rng("hojas_viejas"), 0, 0.95, densidad)
    _compactar(lz, 0.55)
    _ramitas(ctx, lz, tex, ctx.rng("ramitas1"), int(55 * (TAM_M / 4) ** 2), gruesas=2)
    cuenta["principal"] = _pasada_hojas(ctx, lz, tex, ctx.rng("hojas_principal"), 1, 1.1, densidad)
    _compactar(lz, 0.75)
    _ramitas(ctx, lz, tex, ctx.rng("ramitas2"), int(28 * (TAM_M / 4) ** 2), gruesas=1)
    _semillas(ctx, lz, tex, ctx.rng("semillas1"), 25)
    cuenta["nuevas"] = _pasada_hojas(ctx, lz, tex, ctx.rng("hojas_nuevas"), 2, 0.28, densidad)
    _semillas(ctx, lz, tex, ctx.rng("semillas2"), 20)

    # ---------------- humedad en los huecos ----------------
    h = lz.altura
    local = h - desenfocar(h, ctx.px(0.05))
    bajo = suave(-local, 0.0005, 0.005)
    hum_macro = suave(ruido_m(ctx, ctx.rng("humedad"), 0.8, 0.1), -0.3, 1.2)
    humedad = np.clip(bajo * (0.55 + 0.45 * hum_macro), 0, 1)
    albedo = lz.albedo * (1.0 - 0.2 * humedad)[..., None]
    suav = lz.suavidad + 0.18 * humedad * (1 - lz.suavidad)
    albedo *= tinte_macro(ctx, ctx.rng("tinte"), 0.06)

    return ResultadoCapa(
        albedo=albedo.astype(F32), altura=h.astype(F32), suavidad=suav.astype(F32),
        fuerza_normal=1.0, radio_ao_m=0.06, intensidad_ao=1.3, oscurecer_cavidad=0.3,
        ecualizar_altura=0.5, notas=f"hojas: {cuenta}")
