"""flores: 2x2 celdas, insercion abajo-centro (salvo la bromelia, centrada y vista desde arriba).

  celda 0 (arriba izq.):  Heliconia erguida: bracteas en barca rojas con borde amarillo en raquis zigzag.
  celda 1 (arriba der.):  Bromelia (Neoregelia) en roseta vista desde arriba, centro rojo.
  celda 2 (abajo izq.):   Vara de orquideas pequenas blancas con labelo purpura y capullos.
  celda 3 (abajo der.):   Baston de emperador (Etlingera elatior): cono de bracteas rojas y corona abierta.
"""
import numpy as np

import hojas as H
from comun import Lienzo, hexl, mezclar, variar
from ramilletes import Celda

COMUN = dict(color_amarillo=hexl("#b8a23a"), color_seco=hexl("#7a5a38"), color_seco_oscuro=hexl("#3a2818"),
             color_halo=hexl("#a8a040"), color_liquen=hexl("#a3aa94"))


def _estilo(color, vena=None, nervio=None, **kw):
    e = dict(COMUN)
    e.update(color=color, color_vena=vena if vena is not None else color * 1.15,
             color_nervio=nervio if nervio is not None else color * 1.2)
    e.update(kw)
    return e


def _lamina(lienzo, x, y, ang, L, W, perfil, est, rng, banco, z, esc, k2=0.0, asim=0.0, danos=None, recorte=None):
    m = H.Marco(x, y, ang, L, k2, 0.0)
    Wl, Wr = W * (1 + asim), W * (1 - asim)
    capa = H.render_hoja(m, lambda s: perfil(s) * Wl, lambda s: perfil(s) * Wr, est, rng, banco, z=z, danos=danos,
                         esc=esc, recorte=recorte)
    lienzo.pintar(capa)


# ----------------------------------------------------------------------------------------------
# Heliconia
# ----------------------------------------------------------------------------------------------
def heliconia(lienzo, celda, rng, banco):
    """Heliconia bihai erguida: bracteas en forma de barca (pico largo), rojas con el borde superior
    amarillo y la punta verdosa, alternas sobre un raquis en zigzag."""
    esc = celda.esc
    C = celda.ancho
    rojo = hexl("#b3141c")
    rojo_osc = hexl("#7c0c14")
    amarillo = hexl("#e4bc2c")
    verde = hexl("#6b8a2c")
    n_br = int(rng.integers(7, 9))
    base_v = 0.16
    paso = (0.8 - base_v) / n_br
    lado = rng.choice([-1, 1])
    pts = [celda.img(0.5, 0.03), celda.img(0.5 + rng.uniform(-0.01, 0.01), base_v)]
    nodos = []
    for i in range(n_br):
        v = base_v + (i + 1) * paso
        u = 0.5 + lado * 0.03 * (1 - 0.6 * i / n_br)
        pts.append(celda.img(u, v))
        nodos.append((pts[-1][0], pts[-1][1], lado, i))
        lado = -lado
    pts = np.array(pts)
    radios = np.linspace(10, 4.5, len(pts)) * esc
    capa, _ = H.render_tubo(pts, radios, banco, rng, verde * 0.9, mezclar(rojo, amarillo, 0.35), z=5, suav=0.6,
                            grosor=0.9, estrias=0.4)
    lienzo.pintar(capa)
    perfil = H.Perfil([0.0, 0.04, 0.15, 0.35, 0.6, 0.82, 0.94, 1.0], [0.45, 0.8, 1.0, 0.95, 0.72, 0.42, 0.16, 0.0])
    for (xn, yn, lado, i) in nodos:
        r = np.random.default_rng(rng.integers(1 << 30))
        frac = i / max(n_br - 1, 1)
        L = (0.42 - 0.2 * frac) * C * r.uniform(0.94, 1.04)
        ang = np.pi / 2 - lado * np.radians(r.uniform(52, 64) - 14 * frac)
        W = L * r.uniform(0.14, 0.17)

        def recorte(ctx, lado=lado):
            arriba = H.satura(ctx["t"] * lado / np.maximum(ctx["wpx"], 1))       # 0 en el eje, 1 en el borde superior
            borde_sup = H.suave(0.45, 0.85, arriba) * H.suave(0.04, 0.25, ctx["s"])
            ctx["c"] = mezclar(ctx["c"], amarillo * (0.9 + 0.15 * H.satura(0.5 + 0.5 * ctx["mm"]))[..., None], borde_sup)
            filo = np.exp(-ctx["d_borde"] / (1.6 * esc + 0.4)) * H.suave(0.3, 0.9, arriba)
            ctx["c"] = mezclar(ctx["c"], verde, filo * 0.75)
            ctx["c"] = mezclar(ctx["c"], verde * 1.1, H.suave(0.84, 0.97, ctx["s"]) * 0.8)   # punta verde
            abajo = H.satura(-ctx["t"] * lado / np.maximum(ctx["wpx"], 1))
            ctx["c"] = mezclar(ctx["c"], rojo_osc, H.suave(0.3, 1.0, abajo) * 0.35)        # quilla mas oscura
        est = _estilo(variar(rojo, r, 0.008, 0.06, 0.08), vena=rojo * 1.08, nervio=rojo, venacion="paralela",
                      n_long=10, cada_long=3, ancho_vena=1.0, ancho_nervio=0.0, contraste_nervio=0.0,
                      contraste_vena=0.1, var_valor=0.06, var_tono=0.0, combado=0.6, plegado=0.0,
                      arqueo=0.03, abollonado=0.0, alto_vena=0.25, suavidad=0.74, grosor=0.55, estirar_s=0.3,
                      incl_t=-lado * 0.15)
        _lamina(lienzo, xn, yn, ang, L, W, perfil, est, r, banco, 10 + i * 3, esc, k2=lado * r.uniform(0.06, 0.12),
                asim=0.0, recorte=recorte)


# ----------------------------------------------------------------------------------------------
# Bromelia en roseta (vista cenital)
# ----------------------------------------------------------------------------------------------
def bromelia(lienzo, celda, rng, banco):
    """Neoregelia: roseta en espiral (137.5 grados), hojas acintadas brillantes; el centro se
    enrojece (las hojas interiores enteras, las intermedias solo en la base) y hay un tanque central."""
    esc = celda.esc
    C = celda.ancho
    cx, cy = celda.img(0.5, 0.5)
    verde = hexl("#40632a")
    verde_c = hexl("#5f7f30")
    bronce = hexl("#6a6a34")
    rojo = hexl("#b0122e")
    granate = hexl("#5c0c22")
    n = int(rng.integers(34, 40))
    perfil = H.Perfil([0.0, 0.06, 0.25, 0.6, 0.82, 0.93, 1.0], [0.62, 0.85, 1.0, 1.0, 0.86, 0.55, 0.0])
    hojas = []
    a = rng.uniform(0, 2 * np.pi)
    for i in range(n):
        q = i / (n - 1)                         # 0 = exterior .. 1 = interior
        a += np.radians(137.5) + rng.uniform(-0.05, 0.05)
        L = C * (0.47 - 0.3 * q ** 1.2) * rng.uniform(0.93, 1.02)
        W = C * (0.045 - 0.012 * q) * rng.uniform(0.9, 1.1)
        hojas.append((q, a, L, W, int(rng.integers(1 << 30))))
    for (q, ang, L, W, sem) in hojas:
        r = np.random.default_rng(sem)
        x0 = cx + np.cos(ang) * C * 0.03
        y0 = cy - np.sin(ang) * C * 0.03
        lim = float(np.clip(0.05 + 1.1 * q ** 1.4, 0.0, 1.05)) + r.uniform(-0.04, 0.04)
        base = mezclar(mezclar(verde, verde_c, r.uniform(0, 0.7)), bronce, (1 - q) * r.uniform(0, 0.35))

        def recorte(ctx, lim=lim, q=q):
            n = ctx["e"]["_banco"].leer("fbm", ctx["nsL"], ctx["t"], 1.4 * ctx["esc"], (13.0 + 31 * q, 71.0))
            m = 1 - H.suave(lim - 0.18, lim + 0.06, ctx["s"] + 0.05 * n)
            tono = mezclar(granate, rojo, H.satura(0.35 + 0.65 * ctx["s"] / max(lim, 0.05)))
            ctx["c"] = mezclar(ctx["c"], tono, m)
            ctx["suav"] = ctx["suav"] + 0.06 * m
        est = _estilo(variar(base, r, 0.01, 0.08, 0.08), vena=verde_c, nervio=verde_c, venacion="paralela",
                      n_long=11, cada_long=3, ancho_vena=0.9, ancho_nervio=0.0, contraste_nervio=0.0,
                      contraste_vena=0.1, var_valor=0.07, var_tono=0.04, combado=0.0, plegado=-0.3,
                      arqueo=0.06, abollonado=0.0, alto_vena=0.2, suavidad=0.64, grosor=0.42, estirar_s=0.25,
                      _banco=banco)
        _lamina(lienzo, x0, y0, ang, L, W, perfil, est, r, banco, 10 + 40 * q + r.uniform(0, 3), esc,
                k2=r.uniform(-0.06, 0.06), recorte=recorte)
    # tanque central con agua oscura y florecillas violetas
    lado = int(C * 0.14)
    oy, ox = int(cy - lado / 2), int(cx - lado / 2)
    yy, xx = np.mgrid[0:lado, 0:lado].astype(np.float32)
    d = np.sqrt((yy + oy + 0.5 - cy) ** 2 + (xx + ox + 0.5 - cx) ** 2)
    rad = C * 0.05
    alfa = H.cobertura(d - rad)
    agua = mezclar(granate * 0.45, hexl("#26122a"), H.satura(1 - d / rad))
    nn = banco.leer("fbm_fino", yy * 1.5, xx * 1.5, 1.0, (5, 9))
    flores = H.suave(1.0, 1.4, nn) * H.satura(1 - d / (rad * 0.85))
    col = mezclar(agua, hexl("#7050b0"), flores)
    alt = -2.0 * esc * H.satura(1 - d / rad) + 1.2 * esc * flores
    lienzo.pintar(H.Capa(oy, ox, alfa, col, H.normal_desde_altura(alt), 0.85 - 0.35 * flores, 0.9, 5 + alt,
                         0.55 + 0.45 * H.satura(d / rad)))


# ----------------------------------------------------------------------------------------------
# Orquideas
# ----------------------------------------------------------------------------------------------
def orquideas(lienzo, celda, rng, banco):
    esc = celda.esc
    C = celda.ancho
    tallo_col = hexl("#5d6e30")
    x, y = celda.img(0.5, 0.03)
    giro = rng.choice([-1, 1])
    pts = H.curva_suave((x, y), np.pi / 2 + giro * 0.05, 0.92 * C, curvatura=0, n=50, giro_final=-giro * 1.1,
                        ruido_amp=0.02, rng=rng)
    # que no se salga
    pts[:, 0] = np.clip(pts[:, 0], celda.x0 + 0.06 * C, celda.x0 + 0.94 * C)
    pts[:, 1] = np.clip(pts[:, 1], celda.y0 + 0.06 * C, None)
    capa, _ = H.render_tubo(pts, np.linspace(4.5, 2.0, len(pts)) * esc, banco, rng, tallo_col * 0.8, tallo_col, z=5,
                            suav=0.4, grosor=0.9, estrias=0.4)
    lienzo.pintar(capa)
    seg = np.sqrt((np.diff(pts, axis=0) ** 2).sum(1))
    acum = np.concatenate([[0], np.cumsum(seg)])
    tot = acum[-1]
    blanco = hexl("#e6e2e4")
    rosa = hexl("#d9b8d0")
    purpura = hexl("#7a1f5c")
    amarillo = hexl("#e2b83a")
    n_fl = int(rng.integers(5, 8))
    for i in range(n_fl + 3):
        r = np.random.default_rng(rng.integers(1 << 30))
        q = 0.38 + 0.6 * i / (n_fl + 2)
        a = q * tot
        px = np.interp(a, acum, pts[:, 0])
        py = np.interp(a, acum, pts[:, 1])
        lado = 1 if i % 2 == 0 else -1
        # pedicelo
        k = min(int(np.searchsorted(acum, a)), len(pts) - 1)
        tang = np.arctan2(-(pts[k][1] - pts[max(k - 1, 0)][1]), pts[k][0] - pts[max(k - 1, 0)][0] + 1e-9)
        pa = tang + lado * np.radians(r.uniform(35, 70))
        lp = r.uniform(0.04, 0.07) * C
        fx = px + np.cos(pa) * lp
        fy = py - np.sin(pa) * lp
        capa, _ = H.render_tubo(np.array([[px, py], [fx, fy]]), np.array([2.0, 1.6]) * esc, banco, r, tallo_col,
                                tallo_col * 1.1, z=6, suav=0.4, grosor=0.9)
        lienzo.pintar(capa)
        if i >= n_fl:  # capullos
            Lb = r.uniform(0.035, 0.05) * C
            eb = _estilo(mezclar(hexl("#a8b070"), rosa, r.uniform(0, 0.5)), venacion="paralela", n_long=3,
                         ancho_nervio=0.0, contraste_nervio=0, combado=0.5, plegado=0, abollonado=0, suavidad=0.5,
                         grosor=0.6)
            _lamina(lienzo, fx, fy, pa, Lb, Lb * 0.3, H.Perfil([0, 0.3, 0.7, 1.0], [0.5, 1.0, 0.8, 0.0]), eb, r, banco,
                    12, esc)
            continue
        tam = r.uniform(0.06, 0.075) * C
        rot = r.uniform(-0.3, 0.3) + (tang - np.pi / 2) * 0.3
        tono = mezclar(blanco, rosa, r.uniform(0, 0.4))
        ep = _estilo(tono, vena=mezclar(tono, purpura, 0.25), nervio=mezclar(tono, purpura, 0.15), n_venas=5,
                     angulo_vena=25, ancho_nervio=1.0, ancho_vena=0.8, contraste_vena=0.2, contraste_nervio=0.25,
                     terciaria=False, dist_intramarginal=0, var_valor=0.05, var_tono=0.0, combado=0.25,
                     plegado=0.0, abollonado=0.1, alto_vena=0.2, suavidad=0.45, grosor=0.3, contraste_borde=0.0)
        per_sepalo = H.Perfil([0, 0.15, 0.5, 0.85, 1.0], [0.35, 0.8, 1.0, 0.7, 0.0])
        per_petalo = H.Perfil([0, 0.12, 0.45, 0.8, 1.0], [0.25, 0.7, 1.0, 0.85, 0.0])
        # sepalo dorsal y laterales, petalos
        for (da, Lr, Wr, per) in ((90, 1.0, 0.2, per_sepalo), (220, 0.95, 0.18, per_sepalo), (-40, 0.95, 0.18, per_sepalo),
                                  (160, 0.9, 0.32, per_petalo), (20, 0.9, 0.32, per_petalo)):
            a2 = np.radians(da) + rot + r.uniform(-0.08, 0.08)
            _lamina(lienzo, fx, fy, a2, tam * Lr, tam * Wr, per, ep, r, banco, 14 + r.uniform(0, 1), esc,
                    k2=r.uniform(-0.08, 0.08))
        # labelo purpura con garganta amarilla
        el = _estilo(purpura, vena=purpura * 0.7, nervio=amarillo, n_venas=6, angulo_vena=30, ancho_nervio=2.0,
                     ancho_vena=0.8, contraste_vena=0.35, contraste_nervio=0.5, terciaria=False, dist_intramarginal=0,
                     var_valor=0.08, var_tono=0, combado=0.35, plegado=0.1, abollonado=0.2, alto_vena=0.3,
                     suavidad=0.5, grosor=0.35, aclarar_base=0.0, contraste_borde=0.0)
        per_lab = H.Perfil([0, 0.2, 0.55, 0.85, 1.0], [0.3, 0.6, 1.0, 0.9, 0.0])
        _lamina(lienzo, fx, fy, np.radians(-90) + rot, tam * 0.75, tam * 0.3, per_lab, el, r, banco, 16, esc)
        # columna
        ec = _estilo(hexl("#f0eadc"), nervio=amarillo, n_venas=2, terciaria=False, ancho_nervio=0.8,
                     contraste_nervio=0.3, combado=0.5, abollonado=0, suavidad=0.5, grosor=0.5)
        _lamina(lienzo, fx, fy - 0.0 * tam, np.radians(-90) + rot, tam * 0.28, tam * 0.08,
                H.Perfil([0, 0.5, 1.0], [0.8, 1.0, 0.0]), ec, r, banco, 17, esc)


# ----------------------------------------------------------------------------------------------
# Baston de emperador (Etlingera elatior)
# ----------------------------------------------------------------------------------------------
def jengibre(lienzo, celda, rng, banco):
    """Cono de bracteas puntiagudas imbricadas (rojo ceroso con margen blanco) sobre una corona de
    bracteas exteriores grandes y abiertas; tallo grueso desde la insercion."""
    esc = celda.esc
    C = celda.ancho
    rojo = hexl("#c3163a")
    rosa = hexl("#dc4a6c")
    oscuro = hexl("#7a0d26")
    blanco = hexl("#efdcd2")
    verde = hexl("#56702c")
    x, y = celda.img(0.5, 0.03)
    xh, yh = celda.img(0.5 + rng.uniform(-0.015, 0.015), 0.4)
    pts = np.stack([np.linspace(x, xh, 20), np.linspace(y, yh, 20)], 1)
    capa, _ = H.render_tubo(pts, np.linspace(12, 10, 20) * esc, banco, rng, verde * 0.8, mezclar(verde, oscuro, 0.6),
                            z=5, suav=0.45, grosor=0.95, estrias=0.6)
    lienzo.pintar(capa)
    # corona exterior
    n_ext = int(rng.integers(10, 14))
    per_ext = H.Perfil([0, 0.1, 0.35, 0.65, 0.88, 1.0], [0.4, 0.78, 1.0, 0.92, 0.55, 0.0])
    angs = np.linspace(np.radians(200), np.radians(-20), n_ext) + rng.uniform(-0.05, 0.05, n_ext)
    for i, a in enumerate(angs):
        r = np.random.default_rng(rng.integers(1 << 30))
        cent = np.sin(np.pi * i / (n_ext - 1))
        L = C * r.uniform(0.27, 0.34) * (0.75 + 0.25 * cent)
        e = _estilo(variar(mezclar(rojo, rosa, r.uniform(0.1, 0.7)), r, 0.008, 0.05, 0.07), vena=rosa, nervio=rosa,
                    venacion="paralela", n_long=12, cada_long=2, ancho_vena=1.0, ancho_nervio=0, contraste_nervio=0,
                    contraste_vena=0.1, var_valor=0.05, var_tono=0, combado=0.4, plegado=0, arqueo=0.05,
                    abollonado=0, alto_vena=0.25, suavidad=0.8, grosor=0.45, estirar_s=0.4)

        def recorte(ctx):
            borde = np.exp(-ctx["d_borde"] / (3.0 * esc + 0.5))
            ctx["c"] = mezclar(ctx["c"], blanco * 0.92, borde * 0.45)
        z = 10 + 4 * cent + r.uniform(0, 2)
        _lamina(lienzo, xh, yh, a, L, L * r.uniform(0.21, 0.27), per_ext, e, r, banco, z, esc,
                k2=-np.sign(np.cos(a)) * r.uniform(0.06, 0.16), recorte=recorte)
    # cono: escamas puntiagudas dispuestas en espiral sobre la superficie de un cono (vista lateral)
    alto_cono = C * rng.uniform(0.34, 0.4)
    radio_base = C * rng.uniform(0.11, 0.13)
    n_b = 70
    per_b = H.Perfil([0, 0.12, 0.45, 0.78, 0.93, 1.0], [0.5, 0.85, 1.0, 0.75, 0.35, 0.0])
    phi = rng.uniform(0, 2 * np.pi)
    escamas = []
    for k in range(n_b):
        q = k / (n_b - 1)                              # 0 = base del cono .. 1 = punta
        phi += np.radians(137.5)
        h = alto_cono * (0.02 + 0.9 * q)
        rad = radio_base * (1 - q) ** 0.85
        lat = np.cos(phi)                               # posicion lateral (-1..1)
        prof = np.sin(phi)                              # >0 delante
        if prof < -0.35:
            continue                                    # oculta detras del cono
        xx_ = xh + rad * lat
        yy_ = yh - h
        Lb = C * (0.085 - 0.04 * q) * (0.7 + 0.3 * max(prof, 0))
        ang = np.pi / 2 - lat * 0.6 * (1 - q)
        escamas.append((prof, xx_, yy_, ang, Lb, q, int(rng.integers(1 << 30))))
    escamas.sort(key=lambda e: (e[5], e[0]))
    for (prof, xx_, yy_, ang, Lb, q, sem) in escamas:
        r = np.random.default_rng(sem)
        e = _estilo(variar(mezclar(oscuro, rojo, 0.45 + 0.45 * q + 0.1 * prof), r, 0.008, 0.05, 0.08), vena=rojo,
                    nervio=rojo, venacion="paralela", n_long=5, ancho_nervio=0, contraste_nervio=0, contraste_vena=0.1,
                    var_valor=0.05, var_tono=0, combado=0.55, plegado=0, abollonado=0, alto_vena=0.2, suavidad=0.8,
                    grosor=0.6)

        def recorte(ctx):
            borde = np.exp(-ctx["d_borde"] / (3.2 * esc + 0.8)) * H.suave(0.25, 0.75, ctx["s"])
            ctx["c"] = mezclar(ctx["c"], blanco, H.satura(borde * 1.3))
        _lamina(lienzo, xx_, yy_ + Lb * 0.55, ang, Lb, Lb * 0.36, per_b, e, r, banco, 30 + 20 * q + 5 * prof, esc,
                recorte=recorte)


def atlas_flores(res, rng, banco):
    lienzo = Lienzo(res, res)
    lado = res // 2
    funciones = [heliconia, bromelia, orquideas, jengibre]
    for i, f in enumerate(funciones):
        celda = Celda((i % 2) * lado, (i // 2) * lado, lado)
        f(lienzo, celda, np.random.default_rng(rng.integers(1 << 30)), banco)
    return lienzo, dict(recortes=[(res // 4, res // 4), (3 * res // 4, 3 * res // 4)])
