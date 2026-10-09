"""hierba_alta: atlas de ancho x ancho/2 con 4 celdas (ancho/4 x ancho/2) de matas de hierba alta.

La base de la mata esta en el borde inferior (insercion); las hojas suben abriendose en abanico,
algunas se arquean, unas pocas espigas/panojas; verdes con puntas secas amarillo-pardas.
"""
import numpy as np

import hojas as H
from comun import Lienzo, hexl, mezclar, variar
from ramilletes import Celda

PAL_HIERBA = dict(
    verde=hexl("#4a6b22"), verde_claro=hexl("#6f8c2e"), oscuro=hexl("#35521b"), vena=hexl("#7d983e"),
    nervio=hexl("#b7bd84"), base=hexl("#9a9a5a"), paja=hexl("#b19a5e"), seco=hexl("#9c7e4e"),
    seco_oscuro=hexl("#5a4428"), amarillo=hexl("#b3a645"), halo=hexl("#a8a048"), liquen=hexl("#a3aa94"),
    espiga=hexl("#a88f68"), espiga_b=hexl("#7a5c4c"), tallo=hexl("#7d8a44"),
)


def _perfil_hoja_hierba(rng):
    S = [0.0, 0.04, 0.15, 0.4, 0.65, 0.85, 0.95, 1.0]
    W = [0.62, 0.85, 1.0, 0.95, 0.75, 0.42, 0.16, 0.0]
    W = [w * (1 + rng.uniform(-0.05, 0.05)) if 0 < i < len(W) - 1 else w for i, w in enumerate(W)]
    return H.Perfil(S, W)


def _dentro(pts, celda, margen=0.015):
    mx = margen * celda.ancho
    my = margen * celda.alto
    return (pts[:, 0].min() >= celda.x0 + mx and pts[:, 0].max() <= celda.x0 + celda.ancho - mx and
            pts[:, 1].min() >= celda.y0 + my)


def _hoja(rng, celda, cx, esc, prm):
    """Geometria de una hoja de hierba que cabe en la celda."""
    W, A = celda.ancho, celda.alto
    yb = celda.y0 + celda.alto + 2
    xb = cx + rng.normal(0, prm["dispersion"] * W)
    rel = (xb - cx) / (0.5 * W)
    lado = np.sign(rel + rng.normal(0, 0.3)) or 1.0
    incl = lado * (abs(rng.normal(0, prm["apertura"])) + 0.6 * abs(rel) * prm["apertura"])
    largo = rng.uniform(*prm["largo"]) * A
    arco = rng.random() < prm["p_arco"]
    giro = lado * (rng.uniform(0.9, 2.0) if arco else rng.uniform(0.0, 0.6))
    for _ in range(25):
        pts = H.curva_suave((xb, yb), np.pi / 2 - incl, largo, curvatura=-giro * 0.25, n=40, giro_final=-giro * 0.75,
                            ruido_amp=0.03, rng=rng)
        if _dentro(pts, celda):
            return pts
        largo *= 0.93
        giro *= 0.93
        incl *= 0.95
    return None


def mata(lienzo, celda, rng, banco, prm):
    esc = celda.ancho / 512.0 * 0.5 * 2  # celdas de 512 de ancho a res 2048 -> esc 1
    esc = celda.alto / 1024.0
    pal = PAL_HIERBA
    cx = celda.x0 + celda.ancho * (0.5 + rng.uniform(-0.03, 0.03))
    hojas = []
    for _ in range(prm["n"]):
        pts = _hoja(rng, celda, cx, esc, prm)
        if pts is not None:
            hojas.append((rng.uniform(0, 1), pts, int(rng.integers(1 << 30))))
    # espigas (tallo + panoja) mezcladas con las hojas
    espigas = []
    for _ in range(prm["espigas"]):
        xb = cx + rng.normal(0, 0.04 * celda.ancho)
        largo = rng.uniform(0.82, 0.97) * celda.alto
        incl = rng.normal(0, 0.12)
        giro = rng.uniform(-0.35, 0.35)
        for _ in range(20):
            pts = H.curva_suave((xb, celda.y0 + celda.alto + 2), np.pi / 2 - incl, largo, curvatura=0, n=40,
                                giro_final=giro, ruido_amp=0.02, rng=rng)
            if _dentro(pts, celda, 0.02):
                break
            largo *= 0.95
            giro *= 0.9
        espigas.append((rng.uniform(0.3, 1.0), pts, int(rng.integers(1 << 30))))
    elementos = [("hoja",) + h for h in hojas] + [("espiga",) + e for e in espigas]
    elementos.sort(key=lambda a: a[1])
    for tipo, zrel, pts, semilla in elementos:
        r = np.random.default_rng(semilla)
        if tipo == "hoja":
            _pintar_hoja(lienzo, pts, r, banco, esc, pal, prm, 10 + 30 * zrel)
        else:
            _pintar_espiga(lienzo, pts, r, banco, esc, pal, prm, 10 + 30 * zrel)


def _pintar_hoja(lienzo, pts, r, banco, esc, pal, prm, z):
    P = _perfil_hoja_hierba(r)
    ancho = r.uniform(*prm["ancho"]) * esc
    seca_total = r.random() < prm["p_seca"]
    if seca_total:
        base = mezclar(pal["paja"], pal["seco"], r.uniform(0, 0.6))
    else:
        base = mezclar(pal["oscuro"], pal["verde_claro"], r.uniform(0.1, 0.9))
    base = variar(base, r, 0.015, 0.1, 0.1)
    e = dict(color=base, color_vena=pal["vena"] if not seca_total else pal["paja"], color_nervio=pal["nervio"],
             color_amarillo=pal["amarillo"], color_seco=pal["seco"], color_seco_oscuro=pal["seco_oscuro"],
             color_halo=pal["halo"], color_liquen=pal["liquen"], venacion="paralela",
             n_long=int(r.integers(3, 5)), cada_long=2, fuerza_menor=0.5, ancho_vena=1.2, esp_transversal=9.0,
             ancho_nervio=1.4 * (ancho / (8 * esc)), contraste_nervio=r.uniform(0.35, 0.7), contraste_vena=0.18,
             contraste_terciaria=0.02, contraste_areola=0.02, var_valor=r.uniform(0.06, 0.12), var_tono=0.08,
             escala_manchas=160.0, estirar_s=0.12, combado=0.0, plegado=r.uniform(0.15, 0.35), arqueo=0.0,
             alto_nervio=0.3, surco_nervio=0.5, alto_vena=0.25, alto_terciaria_t=0.03, abollonado=0.0,
             enrollado_borde=0.4, suavidad=(0.42 if not seca_total else 0.3), grosor=0.24, aclarar_base=0.25)
    danos = []
    if not seca_total:
        if r.random() < prm["p_punta"]:
            danos.append(dict(tipo="punta_seca", desde=r.uniform(0.55, 0.93)))
        if r.random() < 0.3:
            danos.append(dict(tipo="clorosis", modo="punta", desde=r.uniform(0.4, 0.7), fuerza=r.uniform(0.3, 0.7)))
        if r.random() < 0.25:
            for _ in range(r.integers(1, 4)):
                danos.append(dict(tipo="mancha", s=r.uniform(0.2, 0.9), t=r.uniform(-0.4, 0.4) * ancho,
                                  r=r.uniform(1.0, 2.5) * esc, halo=1.6, fuerza_halo=0.3))
    # base de la hoja (vaina) mas clara/amarillenta
    def recorte(ctx):
        b = 1 - H.suave(0.0, 0.12, ctx["s"])
        ctx["c"] = mezclar(ctx["c"], pal["base"], b * 0.6)
        ctx["grosor"] = ctx["grosor"] + 0.3 * b
    capa = H.render_lamina_curva(pts, P, ancho, e, r, banco, z=z, danos=danos, esc=esc, recorte=recorte)
    lienzo.pintar(capa)


def _pintar_espiga(lienzo, pts, r, banco, esc, pal, prm, z):
    """Tallo floral con panoja de espiguillas en el tercio superior."""
    radios = np.linspace(2.6, 1.2, len(pts)) * esc
    capa, _ = H.render_tubo(pts, radios, banco, r, pal["tallo"] * 0.9, mezclar(pal["tallo"], pal["paja"], 0.5),
                            z=z, suav=0.35, grosor=0.9, estrias=0.6, nudos=[(0.35, 0.3), (0.62, 0.3)])
    lienzo.pintar(capa)
    seg = np.sqrt((np.diff(pts, axis=0) ** 2).sum(1))
    acum = np.concatenate([[0], np.cumsum(seg)])
    tot = acum[-1]
    inicio = r.uniform(0.62, 0.75)
    n = int(r.integers(70, 120))
    col_a = variar(pal["espiga"], r, 0.02, 0.15, 0.1)
    col_b = pal["espiga_b"]
    for i in range(n):
        q = inicio + (1 - inicio) * (i / n) ** 0.9 + r.uniform(-0.01, 0.01)
        a = np.clip(q, 0, 0.995) * tot
        x = np.interp(a, acum, pts[:, 0])
        y = np.interp(a, acum, pts[:, 1])
        k = min(int(np.searchsorted(acum, a)), len(pts) - 1)
        k0 = max(k - 1, 0)
        tang = np.arctan2(-(pts[k][1] - pts[k0][1]), pts[k][0] - pts[k0][0] + 1e-9)
        lado = 1 if i % 2 == 0 else -1
        ang = tang + lado * np.radians(r.uniform(8, 32))
        Ls = r.uniform(14, 30) * esc * (1 - 0.5 * (q - inicio) / (1 - inicio))
        m = H.Marco(x, y, ang, Ls, r.uniform(-0.1, 0.1), 0.0)
        Pe = H.Perfil([0, 0.1, 0.4, 0.75, 1.0], [0.3, 0.8, 1.0, 0.7, 0.0])
        ancho = r.uniform(1.4, 2.4) * esc
        e = dict(color=mezclar(col_a, col_b, r.uniform(0, 0.6)), color_vena=col_a, color_nervio=col_b,
                 color_amarillo=pal["amarillo"], color_seco=pal["seco"], color_seco_oscuro=pal["seco_oscuro"],
                 color_halo=pal["halo"], color_liquen=pal["liquen"], venacion="paralela", n_long=1,
                 ancho_nervio=0.6, contraste_nervio=0.3, contraste_vena=0.1, var_valor=0.1, var_tono=0.0,
                 combado=0.3, plegado=0.0, arqueo=0.0, abollonado=0.0, alto_nervio=0.3, alto_vena=0.0,
                 suavidad=0.3, grosor=0.45, enrollado_borde=0.3)
        capa = H.render_hoja(m, lambda s, P=Pe, a=ancho: P(s) * a, lambda s, P=Pe, a=ancho: P(s) * a, e, r, banco,
                             z=z + 2, esc=esc)
        lienzo.pintar(capa)


VARIANTES = [
    dict(n=46, largo=(0.45, 0.95), ancho=(5.0, 8.0), apertura=0.22, dispersion=0.05, p_arco=0.15, p_seca=0.06,
         p_punta=0.6, espigas=0),
    dict(n=38, largo=(0.5, 0.97), ancho=(5.5, 9.0), apertura=0.18, dispersion=0.05, p_arco=0.1, p_seca=0.1,
         p_punta=0.65, espigas=3),
    dict(n=44, largo=(0.4, 0.9), ancho=(4.5, 7.5), apertura=0.3, dispersion=0.06, p_arco=0.35, p_seca=0.18,
         p_punta=0.75, espigas=1),
    dict(n=30, largo=(0.45, 0.95), ancho=(8.0, 12.0), apertura=0.25, dispersion=0.06, p_arco=0.25, p_seca=0.08,
         p_punta=0.55, espigas=2),
]


def atlas_hierba(res, rng, banco):
    alto = res // 2
    lienzo = Lienzo(alto, res)
    ancho_celda = res // 4
    for i, prm in enumerate(VARIANTES):
        celda = Celda(i * ancho_celda, 0, ancho_celda, alto)
        mata(lienzo, celda, np.random.default_rng(rng.integers(1 << 30)), banco, prm)
    lienzo.calibrar(0.7)
    return lienzo, dict(recortes=[(alto // 3, ancho_celda // 2), (alto // 3, ancho_celda + ancho_celda // 2)])
