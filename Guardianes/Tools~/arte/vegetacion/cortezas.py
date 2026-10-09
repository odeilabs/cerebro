"""Cortezas tileables (U rodea el tronco, V sube por el) y corte transversal de tronco.

Todo el ruido es periodico (espectral / Voronoi en toro / sellos y trazos con envoltura) y las
derivadas para la normal y la oclusion se calculan con envoltura, asi que el mosaico 2x2 no tiene
costuras. Alturas en pixeles (en una tesela de 2048, 1 cm ~ 20 px en las cortezas de ~1 m) para
que las pendientes sean razonables; el PNG de altura se normaliza a 0..1 (16 bits).
"""
import numpy as np
from PIL import Image, ImageDraw
from scipy import ndimage as ndi

import ruido as R
from comun import hexl, mezclar, normal_desde_altura, push_pull, suave, satura, normalizar_v


# ----------------------------------------------------------------------------------------------
# Utilidades periodicas
# ----------------------------------------------------------------------------------------------
def desenfocar(x, r):
    return ndi.gaussian_filter(x, r, mode="wrap")


def _indices(campo, cy, cx, h, w):
    H, W = campo.shape[:2]
    filas = (np.arange(h) + int(round(cy)) - h // 2) % H
    cols = (np.arange(w) + int(round(cx)) - w // 2) % W
    return np.ix_(filas, cols)


def estampar(campo, cy, cx, parche, modo="suma"):
    """Suma / maximo / minimo de un parche centrado en (cy, cx) con envoltura en ambos ejes."""
    idx = _indices(campo, cy, cx, *parche.shape)
    if modo == "suma":
        campo[idx] = campo[idx] + parche
    elif modo == "max":
        campo[idx] = np.maximum(campo[idx], parche)
    else:
        campo[idx] = np.minimum(campo[idx], parche)


def estampar_mezcla(campo3, cy, cx, color, m):
    """Pinta 'color' (h,w,3) sobre campo3 con opacidad m (h,w), con envoltura."""
    idx = _indices(campo3[..., 0], cy, cx, *m.shape)
    for k in range(3):
        sub = campo3[..., k][idx]
        campo3[..., k][idx] = sub * (1 - m) + color[..., k] * m


def mascara_manchas(forma, rng, cobertura, p=1.2, fmin=2.0, borde=0.08, est=1.0):
    """Mascara 0..1 de manchas irregulares con la fraccion de area pedida (y el ruido base)."""
    n = R.espectral(forma, rng, p=p, fmin=fmin, estiramiento=est)
    umbral = np.quantile(n, 1.0 - cobertura)
    return suave(umbral - borde, umbral + borde, n), n


def cavidad(alt, radios):
    ocl = np.zeros_like(alt)
    for r, k in radios:
        ocl += k * np.clip(desenfocar(alt, r) - alt, 0, None)
    return ocl


def finalizar(alt, color, suav, esc, intensidad=1.0, radios_ao=((4, 0.03), (16, 0.015), (48, 0.006)), ao_albedo=0.35,
              ao_extra=None):
    alt = alt.astype(np.float32)
    normal = normal_desde_altura(alt, intensidad, envolver=True)
    ao = np.clip(1.0 - cavidad(alt, [(r * esc, k) for r, k in radios_ao]), 0.15, 1.0)
    if ao_extra is not None:
        ao = ao * ao_extra
    color = np.clip(color * (1 - ao_albedo * (1 - ao))[..., None], 0, 1)
    mn, mx = float(alt.min()), float(alt.max())
    return dict(color=color.astype(np.float32), normal=normal, ao=ao.astype(np.float32),
                suav=np.clip(suav, 0, 1).astype(np.float32), altura=((alt - mn) / max(mx - mn, 1e-6)).astype(np.float32))


def _rnd_por_id(rng, ids, n):
    return rng.random(n + 1).astype(np.float32)[np.clip(ids, 0, n)]


# ----------------------------------------------------------------------------------------------
# Liquenes (colonias redondeadas de borde neto) y musgo
# ----------------------------------------------------------------------------------------------
PALETA_LIQUEN = [hexl("#c9cbc1"), hexl("#b4bd9f"), hexl("#bcc08c"), hexl("#a9b0aa"), hexl("#d3d2c6")]


def _texturas_liquen(rng):
    g = R.espectral((256, 256), rng, p=0.35, fmin=24)
    f1, f2, _ = R.celular((256, 256), 900, rng)
    areola = 1 - suave(0.0, 1.0, (f2 - f1) / 1.6)
    return g, areola


def colonias_liquen(forma, rng, n, r_rango, esc, paleta=PALETA_LIQUEN, permitido=None):
    """Colonias de liquen crustaceo/folioso (borde neto lobulado, textura granular y areolada).

    Tamanos con distribucion potencial (muchas pequenas, pocas grandes). Devuelve mascara, color y relieve."""
    H, W = forma
    masc = np.zeros(forma, np.float32)
    col = np.zeros(forma + (3,), np.float32)
    alt = np.zeros(forma, np.float32)
    g, areola = _texturas_liquen(rng)
    intentos = 0
    hechas = 0
    rmin, rmax = r_rango
    while hechas < n and intentos < n * 8:
        intentos += 1
        cy, cx = rng.uniform(0, H), rng.uniform(0, W)
        if permitido is not None and permitido[int(cy) % H, int(cx) % W] < rng.random():
            continue
        hechas += 1
        r = rmin * (rmax / rmin) ** (rng.random() ** 2.2) * esc
        hw = int(r * 1.45) + 4
        yy, xx = np.mgrid[-hw:hw + 1, -hw:hw + 1].astype(np.float32)
        th = np.arctan2(yy, xx)
        d = np.sqrt(xx ** 2 + yy ** 2)
        a = rng.uniform(0, 6.3, 6)
        rr = r * (1 + 0.14 * np.sin(2 * th + a[0]) + 0.09 * np.sin(3 * th + a[1]) + 0.05 * np.sin(5 * th + a[2])
                  + 0.04 * np.sin(9 * th + a[3]) + 0.025 * np.sin(17 * th + a[4]) + 0.015 * np.sin(31 * th + a[5]))
        q = d / rr
        m = 1 - suave(0.955, 1.0, q)
        borde = np.exp(-((q - 0.95) / 0.04) ** 2) * m
        zona = 0.5 + 0.5 * np.cos(q * np.pi * rng.uniform(2.5, 6.0) + rng.uniform(0, 6))
        oy, ox = rng.integers(0, 256, 2)
        sy = (yy.astype(np.int64) + oy) % 256
        sx = (xx.astype(np.int64) + ox) % 256
        gi = g[sy, sx]
        ai = areola[sy, sx] * (rng.random() < 0.6)
        base = paleta[int(rng.integers(len(paleta)))] * rng.uniform(0.82, 1.05)
        f_borde = rng.choice([-0.4, 0.25])  # protalo oscuro o margen blanquecino
        c = base[None, None, :] * (0.9 + 0.12 * zona + 0.13 * gi - 0.2 * ai)[..., None]
        c = c * (1 + f_borde * borde)[..., None]
        if rng.random() < 0.3:  # apotecios: discos oscuros
            for _ in range(int(rng.integers(3, 16))):
                ra = rng.uniform(1.0, 3.0) * esc
                py, px = rng.uniform(-0.6, 0.6, 2) * r
                da = np.sqrt((yy - py) ** 2 + (xx - px) ** 2) / ra
                ma = (1 - suave(0.7, 1.0, da)) * m
                c = c * (1 - ma[..., None]) + (hexl("#3a3328") * rng.uniform(0.8, 1.3))[None, None, :] * ma[..., None]
        opac = m * rng.uniform(0.8, 0.97) * (1 - 0.25 * ai)
        estampar_mezcla(col, cy, cx, c, opac)
        estampar(masc, cy, cx, opac, "max")
        estampar(alt, cy, cx, m * (0.45 + 0.35 * gi + 0.2 * zona) * esc * rng.uniform(0.8, 1.5), "max")
    return masc, col, alt


def capa_musgo(forma, rng, cobertura, esc, preferencia=None):
    """Musgo en cojines: mascara cohesionada con borde algodonoso, color y relieve."""
    base, n = mascara_manchas(forma, rng, cobertura, p=1.45, fmin=2, borde=0.12)
    if preferencia is not None:
        base = satura(base * (0.35 + 0.9 * preferencia))
    medio = R.espectral(forma, rng, p=0.6, fmin=50)
    fino = R.espectral(forma, rng, p=0.3, fmin=180)
    m = satura(base * 1.35 + 0.16 * medio - 0.12) * satura(0.88 + 0.2 * fino)
    tono = satura(0.45 + 0.25 * medio + 0.22 * fino)
    col = mezclar(np.broadcast_to(hexl("#2b3f17"), forma + (3,)).astype(np.float32), hexl("#6a7a2a"), tono)
    alt = m * (2.4 + 0.9 * medio + 0.5 * fino) * esc
    return m, col, alt


# ----------------------------------------------------------------------------------------------
# Ceiba (kapok): gris palido a gris verdoso, lisa, vetas verticales, lenticelas, espinas conicas
# ----------------------------------------------------------------------------------------------
def corteza_ceiba(res, rng, banco):
    N = res
    f = (N, N)
    esc = N / 2048.0
    grande = R.espectral(f, rng, p=1.8, fmin=1, fmax=9, estiramiento=1.6)
    medio = R.espectral(f, rng, p=1.15, fmin=4, estiramiento=8.0)
    fino = R.espectral(f, rng, p=0.75, fmin=40, estiramiento=3.0)
    micro = R.espectral(f, rng, p=0.3, fmin=200)
    alt = (12 * grande + 1.2 * medio + 0.14 * fino + 0.06 * micro) * esc
    # lineas longitudinales muy someras (corteza lisa)
    f1, f2, ids = R.celular(f, 110, rng, estiramiento=9.0)
    linea = np.exp(-((f2 - f1) / (3.0 * esc)) ** 2) * satura(_rnd_por_id(rng, ids, 110) * 1.5 - 0.3)
    alt -= 2.2 * esc * linea
    # lenticelas horizontales en forma de lente, algo elevadas, interior pardo-anaranjado
    lent_alt = np.zeros(f, np.float32)
    lent_m = np.zeros(f, np.float32)
    for _ in range(int(430 * (N / 2048) ** 2)):
        lx = rng.uniform(10, 34) * esc
        ly = rng.uniform(2.2, 4.8) * esc
        hw = int(lx * 1.3) + 3
        hh = int(ly * 2.5) + 3
        yy, xx = np.mgrid[-hh:hh + 1, -hw:hw + 1].astype(np.float32)
        yy = yy - xx * rng.uniform(-0.05, 0.05)
        e = (xx / lx) ** 2 + (yy / (ly * np.sqrt(np.clip(1 - (xx / lx) ** 2, 0.05, 1)))) ** 2
        dentro = 1 - suave(0.7, 1.0, e)
        labio = np.exp(-((np.sqrt(e) - 1.0) / 0.35) ** 2)
        cy, cx = rng.uniform(0, N), rng.uniform(0, N)
        estampar(lent_alt, cy, cx, (1.0 * labio + 0.6 * dentro - 0.9 * dentro ** 4) * esc, "suma")
        estampar(lent_m, cy, cx, dentro, "max")
    alt += lent_alt
    # espinas conicas (algunas agrupadas)
    esp = np.zeros(f, np.float32)
    esp_m = np.zeros(f, np.float32)
    esp_b = np.zeros(f, np.float32)
    centros = []
    for _ in range(int(rng.integers(26, 40) * (N / 2048) ** 2)):
        if centros and rng.random() < 0.4:
            cy, cx = centros[int(rng.integers(len(centros)))]
            cy += rng.normal(0, 70 * esc)
            cx += rng.normal(0, 55 * esc)
        else:
            cy, cx = rng.uniform(0, N), rng.uniform(0, N)
        centros.append((cy, cx))
        Rr = rng.uniform(14, 34) * esc
        Rb = Rr * 1.8
        hw = int(Rb) + 3
        yy, xx = np.mgrid[-hw:hw + 1, -hw:hw + 1].astype(np.float32)
        yy = yy / rng.uniform(0.8, 1.0)
        r = np.sqrt(xx ** 2 + yy ** 2)
        q = r / Rr
        cono = np.clip(1 - q, 0, 1) ** 1.15 * Rr * 1.3
        base = np.clip(1 - r / Rb, 0, 1) ** 2 * Rr * 0.3
        estampar(esp, cy, cx, cono + base, "max")
        estampar(esp_m, cy, cx, np.clip(1 - q, 0, 1), "max")
        estampar(esp_b, cy, cx, np.clip(1 - r / Rb, 0, 1), "max")
    alt += esp
    # liquenes en colonias y musgo escaso
    liq_m, liq_c, liq_a = colonias_liquen(f, rng, int(60 * (N / 2048) ** 2), (8, 110), esc,
                                          permitido=1 - satura(esp_b * 2))
    mus_m, mus_c, mus_a = capa_musgo(f, rng, 0.05, esc, preferencia=satura(0.5 - 0.5 * grande))
    alt += liq_a + mus_a

    # ---- color ----
    c = np.broadcast_to(hexl("#8d8e85"), f + (3,)).astype(np.float32)
    c = mezclar(c, hexl("#7f8672"), satura(0.4 + 0.35 * grande))          # zonas verdosas (algas)
    c = c * (1 + 0.06 * medio + 0.03 * fino + 0.02 * micro)[..., None]
    chorro = satura(R.espectral(f, rng, p=1.0, fmin=3, estiramiento=14.0) * 0.55 - 0.15)
    c = mezclar(c, hexl("#626458"), chorro * 0.5)                           # chorreones de lluvia
    c = mezclar(c, hexl("#6c6d63"), linea * 0.4)
    c = mezclar(c, hexl("#a89a84"), lent_m * 0.75)                          # lenticelas (beige claro)
    c = mezclar(c, hexl("#6e6252"), lent_m ** 4 * 0.45)
    c = mezclar(c, hexl("#a5a598"), satura(lent_alt / (1.0 * esc)) * 0.35)
    c = mezclar(c, hexl("#77736a"), esp_b * 0.35)
    c = mezclar(c, hexl("#4a4038"), suave(0.45, 0.95, esp_m))              # punta parda de la espina
    c = c * (1 - liq_m[..., None]) + liq_c * liq_m[..., None]
    c = mezclar(c, mus_c, mus_m)
    suav = 0.33 + 0.03 * fino - 0.1 * liq_m - 0.2 * mus_m + 0.06 * esp_m - 0.05 * lent_m
    return finalizar(alt, c, suav, esc, intensidad=1.0, radios_ao=((3, 0.05), (12, 0.02), (40, 0.006)))


# ----------------------------------------------------------------------------------------------
# Corteza tropical generica: placas y fisuras verticales irregulares, liquen, musgo, humedad
# ----------------------------------------------------------------------------------------------
def corteza_tropical(res, rng, banco):
    N = res
    f = (N, N)
    esc = N / 2048.0
    n_c = 100
    est = 6.0
    pts = rng.random((n_c, 2)) * np.array([N / est, N])
    f1, f2, ids = R.celular(f, n_c, rng, estiramiento=est, puntos=pts)
    dy = (R.espectral(f, rng, p=1.4, fmin=3, fmax=22) * 16 + R.espectral(f, rng, p=0.8, fmin=40, fmax=220) * 2.5) * esc
    dx = (R.espectral(f, rng, p=1.4, fmin=3, fmax=22) * 16 + R.espectral(f, rng, p=0.8, fmin=40, fmax=220) * 2.5) * esc
    borde = R.deformar(f2 - f1, dy, dx)
    borde = borde + R.espectral(f, rng, p=0.6, fmin=60, estiramiento=3.0) * 3.0 * esc   # paredes rugosas
    idw = R.deformar(ids.astype(np.float32), dy, dx, orden=0).astype(np.int64)
    alto_placa = _rnd_por_id(rng, idw, n_c)
    tono_placa = _rnd_por_id(rng, idw, n_c)
    w_f = (22 + 36 * R.a_01(R.espectral(f, rng, p=1.2, fmin=2, estiramiento=3.0))) * esc
    rel = borde / w_f
    fisura = 1 - suave(0.12, 0.8, rel)
    labio_p = np.exp(-((rel - 0.95) / 0.22) ** 2)
    meseta = suave(0.4, 1.6, rel)
    medio = R.espectral(f, rng, p=1.0, fmin=6, estiramiento=6.0)
    fino = R.espectral(f, rng, p=0.65, fmin=50, estiramiento=6.0)
    micro = R.espectral(f, rng, p=0.3, fmin=220)
    # escamas: laminas irregulares sobre las placas, cada una a su altura (terrazas) con borde oscuro
    n_e = 1600
    g1, g2, gid = R.celular(f, n_e, rng, estiramiento=0.7)
    gdy = R.espectral(f, rng, p=1.2, fmin=8, fmax=60) * 5 * esc
    gdx = R.espectral(f, rng, p=1.2, fmin=8, fmax=60) * 5 * esc
    gb = R.deformar(g2 - g1, gdy, gdx)
    gid = R.deformar(gid.astype(np.float32), gdy, gdx, orden=0).astype(np.int64)
    escama = (1 - suave(0.0, 1.0, gb / (2.2 * esc))) * satura(_rnd_por_id(rng, gid, n_e) * 2.2 - 0.5)
    terraza = _rnd_por_id(rng, gid, n_e)
    tono_esc = _rnd_por_id(rng, gid, n_e)
    alt = (-42 * fisura + 4 * labio_p + 10 * meseta + 9 * alto_placa * meseta + 5 * medio + 1.4 * fino + 0.35 * micro
           + (5.0 * terraza - 4.0 * escama) * meseta) * esc
    estria = R.espectral(f, rng, p=0.7, fmin=30, estiramiento=10.0)
    alt += 1.6 * esc * estria * meseta
    # liquen sobre placas, musgo en fisuras y manchas
    liq_m, liq_c, liq_a = colonias_liquen(f, rng, int(40 * (N / 2048) ** 2), (6, 80), esc, permitido=meseta)
    mus_m, mus_c, mus_a = capa_musgo(f, rng, 0.22, esc, preferencia=satura(fisura * 2.0) ** 0.7)
    alt += liq_a * meseta + mus_a
    humedo = suave(0.15, 0.85, R.espectral(f, rng, p=1.7, fmin=1, fmax=8, estiramiento=3.0) * 0.5 + 0.25)

    # ---- color ----
    c = mezclar(np.broadcast_to(hexl("#5e5045"), f + (3,)).astype(np.float32), hexl("#6e6255"), tono_placa)
    c = mezclar(c, hexl("#8e877d"), satura(meseta * alto_placa * (0.6 + 0.4 * medio)) * 0.55)  # cimas grises
    c = c * (1 + 0.07 * medio + 0.06 * fino + 0.04 * micro + 0.16 * (tono_esc - 0.5) * meseta
             + 0.06 * estria * meseta)[..., None]
    c = mezclar(c, hexl("#968f84"), satura(terraza * meseta - 0.55) * 0.6)      # escamas grises sueltas
    pared = satura(fisura * 2.2) * (1 - satura(fisura * 1.6 - 0.6))
    c = mezclar(c, hexl("#6a4630"), pared * 0.6)                           # corteza interna rojiza en las paredes
    c = mezclar(c, hexl("#24190f"), satura(fisura * 1.4 - 0.35))           # fondo de la fisura
    c = mezclar(c, hexl("#3a2c22"), escama * meseta * 0.6)
    c = c * (1 - liq_m[..., None]) + liq_c * liq_m[..., None]
    c = mezclar(c, mus_c, mus_m)
    c = c * (1 - 0.3 * humedo)[..., None]
    suav = 0.27 + 0.03 * fino - 0.08 * liq_m - 0.15 * mus_m + 0.2 * humedo * (1 - mus_m)
    return finalizar(alt, c, suav, esc, intensidad=1.0)


# ----------------------------------------------------------------------------------------------
# Tronco de palmera: cicatrices anulares cada ~10 cm (10 por tesela de ~1 m), fibra vertical
# ----------------------------------------------------------------------------------------------
def corteza_palmera(res, rng, banco):
    N = res
    f = (N, N)
    esc = N / 2048.0
    n_an = 10
    yy, xx = np.mgrid[0:N, 0:N].astype(np.float32)
    base = (np.arange(n_an) + 0.5 + rng.uniform(-0.15, 0.15, n_an)) * N / n_an
    dist = np.full(f, 1e9, np.float32)
    idx = np.zeros(f, np.int32)
    for k, y0 in enumerate(base):
        m1, m2 = int(rng.integers(1, 3)), int(rng.integers(2, 5))
        ond = (rng.uniform(6, 16) * np.sin(2 * np.pi * m1 * xx / N + rng.uniform(0, 6.3)) +
               rng.uniform(2, 7) * np.sin(2 * np.pi * m2 * xx / N + rng.uniform(0, 6.3))) * esc
        ruido_x = R.ruido1d(N, rng, suavidad=N / 50, periodico=True)[None, :] * 4 * esc
        d = yy - (y0 + ond + ruido_x)
        d = (d + N / 2) % N - N / 2
        mejor = np.abs(d) < np.abs(dist)
        dist = np.where(mejor, d, dist)
        idx = np.where(mejor, k, idx)
    b = (rng.uniform(16, 28, n_an) * esc).astype(np.float32)[idx]
    b = b * (1 + 0.18 * R.espectral(f, rng, p=1.2, fmin=4, estiramiento=0.3))
    q = dist / b                                   # <0 por encima del anillo (fila menor = mas alto)
    surco = np.exp(-(q / 0.6) ** 2)                # cicatriz de la hoja
    labio = np.where(q > -1.0, np.exp(-((q + 1.0) / 0.22) ** 2), np.exp(-((q + 1.0) / 0.5) ** 2))  # labio superior
    hombro = np.exp(-((q - 1.15) / 0.5) ** 2)
    banda = 1 - suave(0.9, 1.4, np.abs(q))
    entre = np.cos(np.pi * np.clip(dist / (N / n_an), -0.5, 0.5)) ** 2
    fibra_v = R.espectral(f, rng, p=0.75, fmin=25, estiramiento=14.0)
    fibra_v2 = R.espectral(f, rng, p=0.5, fmin=100, estiramiento=20.0)
    fibra_h = R.espectral(f, rng, p=0.6, fmin=40, estiramiento=0.08)
    g1, g2, gid = R.celular(f, 220, rng, estiramiento=10.0)
    grieta = (1 - suave(0.0, 1.0, (g2 - g1) / (2.4 * esc))) * satura(_rnd_por_id(rng, gid, 220) * 1.8 - 0.5)
    grieta = grieta * (1 - banda)
    grande = R.espectral(f, rng, p=1.6, fmin=1, fmax=8)
    alt = (-9 * surco + 7 * labio + 3 * hombro + 7 * entre + 1.8 * fibra_v * (1 - banda) + 0.6 * fibra_v2
           + 2.2 * fibra_h * banda - 6 * grieta + 5 * grande) * esc
    liq_m, liq_c, liq_a = colonias_liquen(f, rng, int(28 * (N / 2048) ** 2), (8, 40), esc, permitido=1 - banda)
    alt += liq_a
    # ---- color ----
    c = mezclar(np.broadcast_to(hexl("#7c766d"), f + (3,)).astype(np.float32), hexl("#6b5d4e"),
                satura(0.4 + 0.4 * grande))
    c = c * (1 + 0.07 * fibra_v + 0.05 * fibra_v2)[..., None]
    c = mezclar(c, hexl("#9a948a"), satura(0.5 * fibra_v - 0.2) * (1 - banda) * 0.5)   # vetas grises claras
    c = mezclar(c, hexl("#6d655b"), banda * 0.45)
    c = c * (1 + 0.1 * fibra_h * banda)[..., None]
    c = mezclar(c, hexl("#3a3029"), np.exp(-((q + 0.72) / 0.12) ** 2) * 0.75)   # junta bajo el labio
    c = mezclar(c, hexl("#4a4037"), surco * 0.35)
    c = mezclar(c, hexl("#aaa498"), labio * 0.5)
    c = mezclar(c, hexl("#2b231c"), grieta * 0.7)
    algas = suave(0.3, 0.9, R.espectral(f, rng, p=1.3, fmin=2) * 0.5 + 0.3) * satura(surco + labio * 0.5)
    c = mezclar(c, hexl("#48532e"), algas * 0.45)
    c = c * (1 - liq_m[..., None]) + liq_c * liq_m[..., None]
    suav = 0.28 + 0.04 * fibra_v - 0.06 * liq_m + 0.05 * labio
    return finalizar(alt, c, suav, esc, intensidad=1.0)


# ----------------------------------------------------------------------------------------------
# Tronco de helecho arboreo: manto de raices aereas con cicatrices romboidales de frondas viejas
# ----------------------------------------------------------------------------------------------
def _trazos_periodicos(N, rng, n, largo, ancho, ang_medio, desv, ondula, z_rango=(0.0, 1.0)):
    """Dibuja n trazos (raicillas) con PIL en un lienzo 3x3 y lo pliega: altura periodica en px."""
    M = 3 * N
    im = Image.new("F", (M, M), 0.0)
    d = ImageDraw.Draw(im)
    for _ in range(n):
        x, y = N + rng.uniform(0, N), N + rng.uniform(0, N)
        L = rng.uniform(*largo)
        w = rng.uniform(*ancho)
        ang = ang_medio + rng.normal(0, desv)
        z = rng.uniform(*z_rango)
        pasos = max(int(L / 6), 2)
        pts = [(x, y)]
        for _ in range(pasos):
            ang += rng.normal(0, ondula)
            x += np.cos(ang) * 6
            y += np.sin(ang) * 6
            pts.append((x, y))
        d.line(pts, fill=float(z + w * 0.5), width=max(int(round(w)), 1), joint="curve")
        if w >= 2.5:
            d.line(pts, fill=float(z + w * 0.85), width=max(int(round(w * 0.45)), 1))
    a = np.asarray(im, np.float32).reshape(3, N, 3, N)
    return a.max(axis=(0, 2))


def tronco_helecho(res, rng, banco):
    N = res
    f = (N, N)
    esc = N / 2048.0
    # cicatrices de frondas en reticula romboidal (periodica), con jitter
    n_c, n_f = 5, 4
    cic = np.zeros(f, np.float32)
    cic_m = np.zeros(f, np.float32)
    cic_borde = np.zeros(f, np.float32)
    haces = np.zeros(f, np.float32)
    arrugas = R.espectral((256, 256), rng, p=0.8, fmin=10)
    cw, ch = N / n_c, N / n_f
    for i in range(n_f):
        for j in range(n_c):
            for off in (0.0, 0.5):
                cy = (i + off + rng.uniform(-0.08, 0.08)) * ch
                cx = (j + off + rng.uniform(-0.08, 0.08)) * cw
                a = cw * rng.uniform(0.22, 0.3)
                b = ch * rng.uniform(0.2, 0.28)
                rot = rng.uniform(-0.15, 0.15)
                hw, hh = int(max(a, b) * 1.3) + 6, int(max(a, b) * 1.3) + 6
                py, px = np.mgrid[-hh:hh + 1, -hw:hw + 1].astype(np.float32)
                ux = px * np.cos(rot) + py * np.sin(rot)
                uy = -px * np.sin(rot) + py * np.cos(rot)
                th = np.arctan2(uy, ux)
                irr = 1 + 0.05 * np.sin(3 * th + rng.uniform(0, 6)) + 0.03 * np.sin(7 * th + rng.uniform(0, 6))
                q = (np.abs(ux / a) ** 1.3 + np.abs(uy / b) ** 1.3) / irr
                dentro = 1 - suave(0.88, 1.0, q)
                borde = np.exp(-((q - 0.97) / 0.07) ** 2)
                arr = arrugas[(np.abs(py).astype(int) * 3) % 256, (np.abs(px).astype(int) * 3) % 256]
                hueco = (-7 - 1.5 * arr) * dentro * esc
                estampar(cic, cy, cx, hueco + 5 * esc * borde, "suma")
                estampar(cic_m, cy, cx, dentro, "max")
                estampar(cic_borde, cy, cx, borde, "max")
                # haces vasculares: puntos en herradura
                hz = np.zeros_like(px)
                for t in np.linspace(-2.3, 2.3, int(rng.integers(11, 17))):
                    hx = 0.55 * a * np.sin(t)
                    hy = -0.42 * b * np.cos(t) + 0.12 * b
                    rr = rng.uniform(2.0, 3.6) * esc
                    hz = np.maximum(hz, np.exp(-((ux - hx) ** 2 + (uy - hy) ** 2) / (2 * rr ** 2)))
                estampar(haces, cy, cx, hz * dentro, "max")
    # manto de raicillas: miles de trazos finos (sobre todo verticales), menos encima de las cicatrices
    fib = _trazos_periodicos(N, rng, int(30000 * (N / 2048) ** 2), (30 * esc, 260 * esc), (0.8 * esc, 2.6 * esc),
                             np.pi / 2, 0.6, 0.14, (0.0, 5.0 * esc))
    fib = desenfocar(fib, 0.6 * esc)
    gruesas = _trazos_periodicos(N, rng, int(260 * (N / 2048) ** 2), (150 * esc, 700 * esc), (2.5 * esc, 5.0 * esc),
                                 np.pi / 2, 0.3, 0.07, (4.0 * esc, 8.0 * esc))
    gruesas = desenfocar(gruesas, 0.9 * esc)
    cubre = satura(1 - cic_m * 0.55)
    manto = np.maximum(fib, gruesas * 0.9) * cubre
    fondo = R.espectral(f, rng, p=0.9, fmin=10) * 2 * esc
    grande = R.espectral(f, rng, p=1.5, fmin=1, fmax=10) * 6 * esc
    alt = manto + fondo + grande + cic
    alt = alt + 0.4 * esc * R.espectral(f, rng, p=0.3, fmin=200)
    # ---- color ----
    hebra = satura(manto / (6 * esc))
    var = R.espectral(f, rng, p=1.0, fmin=6)
    c = np.broadcast_to(hexl("#1d140d"), f + (3,)).astype(np.float32)
    c = mezclar(c, hexl("#3b2919"), satura(hebra * 1.3))
    c = mezclar(c, hexl("#674326"), satura(hebra * 2.2 - 1.3) * satura(0.5 + 0.5 * var))   # brillos rojizos
    c = mezclar(c, hexl("#4d3421"), satura(gruesas / (8 * esc)) * cubre * 0.5)
    tex_c = R.espectral(f, rng, p=0.8, fmin=20)
    cic_col = mezclar(hexl("#3e3024"), hexl("#5e4a36"), satura(0.5 + 0.45 * tex_c))
    c = mezclar(c, cic_col, cic_m * (1 - satura(hebra * 1.8)) * 0.92)
    c = mezclar(c, hexl("#2a1f17"), cic_borde * 0.7)
    c = mezclar(c, hexl("#231910"), haces * 0.85)
    mus_m, mus_c, mus_a = capa_musgo(f, rng, 0.06, esc)
    c = mezclar(c, mus_c, mus_m * 0.8)
    alt += mus_a * 0.6
    suav = 0.2 + 0.08 * hebra - 0.08 * mus_m + 0.04 * cic_m
    return finalizar(alt, c, suav, esc, intensidad=1.0, radios_ao=((2, 0.06), (8, 0.025), (30, 0.008)))


# ----------------------------------------------------------------------------------------------
# Bambu: un nudo por tesela en v=0/1, entrenudo brillante verde-amarillo con estrias finas
# ----------------------------------------------------------------------------------------------
def corteza_bambu(res, rng, banco):
    N = res
    f = (N, N)
    esc = N / 2048.0
    yy, xx = np.mgrid[0:N, 0:N].astype(np.float32)
    ond = (5 * np.sin(2 * np.pi * xx / N + rng.uniform(0, 6.3)) + 2 * np.sin(4 * np.pi * xx / N + 1.0)) * esc
    dn = yy - ond
    dn = (dn + N / 2) % N - N / 2      # px por debajo del nudo (fila 0); negativo = por encima (v~1)
    # nudo: cresta ancha + cicatriz de la vaina justo por encima
    cresta = np.exp(-((dn - 8 * esc) / (26 * esc)) ** 2)
    cicatriz = np.exp(-((dn + 30 * esc) / (6 * esc)) ** 2)
    hinchado = np.exp(-((dn + 10 * esc) / (90 * esc)) ** 2)
    estrias = R.espectral(f, rng, p=0.45, fmin=90, estiramiento=50.0)
    estrias2 = R.espectral(f, rng, p=0.85, fmin=12, estiramiento=35.0)
    grande = R.espectral(f, rng, p=1.8, fmin=1, fmax=5, estiramiento=4.0)
    alt = (16 * cresta - 6 * cicatriz + 12 * hinchado + 0.02 * estrias + 0.12 * estrias2 + 3 * grande) * esc
    # banda pulverulenta (cera blanquecina) bajo el nudo
    polvo_n = R.espectral(f, rng, p=1.3, fmin=3)
    banda = suave(25 * esc, 50 * esc, dn) * (1 - suave(60 * esc, 170 * esc, dn + 25 * esc * polvo_n))
    polvo = satura(banda * (0.85 + 0.12 * polvo_n))
    # manchas pardas y algun liquen
    manchas = np.zeros(f, np.float32)
    for _ in range(int(rng.integers(12, 22) * (N / 2048) ** 2)):
        r = rng.uniform(4, 16) * esc
        hw = int(r * 2.5) + 3
        py, px = np.mgrid[-hw:hw + 1, -hw:hw + 1].astype(np.float32)
        d = np.sqrt((px / rng.uniform(0.5, 0.9)) ** 2 + py ** 2) / r
        estampar(manchas, rng.uniform(0, N), rng.uniform(0, N), 1 - suave(0.6, 1.0, d), "max")
    liq_m, liq_c, liq_a = colonias_liquen(f, rng, int(10 * (N / 2048) ** 2), (6, 22), esc)
    alt += liq_a * 0.5
    # ---- color ----
    c = np.broadcast_to(hexl("#62782e"), f + (3,)).astype(np.float32)
    c = mezclar(c, hexl("#86903e"), satura(0.3 + 0.25 * grande + 0.15 * estrias2))
    c = c * (1 + 0.015 * estrias + 0.03 * estrias2)[..., None]
    c = mezclar(c, hexl("#9a9447"), cresta * 0.55)
    c = mezclar(c, hexl("#5a4e2e"), cicatriz * 0.85)
    c = mezclar(c, hexl("#b3b8ab"), polvo * 0.62)
    c = mezclar(c, hexl("#6d5034"), manchas * 0.85)
    c = c * (1 - liq_m[..., None]) + liq_c * liq_m[..., None]
    suav = 0.72 - 0.38 * polvo - 0.3 * manchas - 0.2 * cicatriz - 0.3 * liq_m
    return finalizar(alt, c, suav, esc, intensidad=1.0, radios_ao=((4, 0.03), (16, 0.012)), ao_albedo=0.25)


# ----------------------------------------------------------------------------------------------
# Madera astillada (interior de troncos rotos)
# ----------------------------------------------------------------------------------------------
def madera_astillada(res, rng, banco):
    N = res
    f = (N, N)
    esc = N / 2048.0
    n_a = 120
    est = 26.0
    pts = rng.random((n_a, 2)) * np.array([N / est, N])
    f1, f2, ids = R.celular(f, n_a, rng, estiramiento=est, puntos=pts)
    dy = R.espectral(f, rng, p=1.4, fmin=2, fmax=20, estiramiento=4.0) * 30 * esc
    dx = (R.espectral(f, rng, p=1.4, fmin=2, fmax=20, estiramiento=6.0) * 8 +
          R.espectral(f, rng, p=0.8, fmin=30, fmax=200, estiramiento=10.0) * 1.5) * esc
    borde = R.deformar(f2 - f1, dy, dx)
    idw = R.deformar(ids.astype(np.float32), dy, dx, orden=0).astype(np.int64)
    alto = _rnd_por_id(rng, idw, n_a)
    tono = _rnd_por_id(rng, idw, n_a)
    fase = _rnd_por_id(rng, idw, n_a) * 6.28
    yy, xx = np.mgrid[0:N, 0:N].astype(np.float32)
    ola = np.sin(2 * np.pi * yy / N * 1 + fase) * 0.5 + 0.5      # cada astilla sube/baja a lo largo de V
    lomo = suave(0.0, 1.0, borde / (10 * esc)) ** 0.7
    grieta = 1 - suave(0.0, 1.0, borde / (2.6 * esc))
    fibra = R.espectral(f, rng, p=0.55, fmin=50, estiramiento=40.0)
    fibra2 = R.espectral(f, rng, p=0.9, fmin=8, estiramiento=30.0)
    w_v = R.espectral(f, rng, p=1.3, fmin=1, fmax=12, estiramiento=3.0) * 40 * esc
    veta = suave(0.8, 0.98, np.sin(2 * np.pi * (xx + w_v) / (N / 48)) * 0.5 + 0.5) * satura(0.6 + 0.4 * fibra2)
    alt = ((14 * alto + 10 * ola * alto) * lomo + 8 * lomo - 18 * grieta + 1.8 * fibra + 2.5 * fibra2 - 0.8 * veta) * esc
    # ---- color ----
    c = mezclar(np.broadcast_to(hexl("#d0b48a"), f + (3,)).astype(np.float32), hexl("#a9845a"),
                satura(tono * 0.85 + 0.15 * fibra2))
    c = mezclar(c, hexl("#e2cda6"), satura(alto * ola * lomo - 0.4) * 0.6)   # astillas frescas mas claras
    c = c * (1 + 0.07 * fibra + 0.06 * fibra2)[..., None]
    c = mezclar(c, hexl("#7d5a3a"), veta * 0.5)
    c = mezclar(c, hexl("#3a281a"), satura(grieta * 1.3))
    sucio = suave(0.5, 1.5, R.espectral(f, rng, p=1.3, fmin=2))
    c = mezclar(c, hexl("#7c7466"), sucio * 0.35)
    suav = 0.3 + 0.04 * fibra - 0.08 * grieta
    return finalizar(alt, c, suav, esc, intensidad=1.0, radios_ao=((3, 0.05), (12, 0.02), (40, 0.006)))


# ----------------------------------------------------------------------------------------------
# Corte transversal de tronco (no tileable, RGBA)
# ----------------------------------------------------------------------------------------------
def anillos_tronco(res, rng, banco):
    N = res
    esc = N / 1024.0
    yy, xx = np.mgrid[0:N, 0:N].astype(np.float32) + 0.5
    cx, cy = N / 2 + rng.uniform(-4, 4) * esc, N / 2 + rng.uniform(-4, 4) * esc
    Rr = 0.475 * N
    th = np.arctan2(yy - cy, xx - cx)
    irr = 1 + 0.012 * np.sin(3 * th + rng.uniform(0, 6)) + 0.008 * np.sin(5 * th + rng.uniform(0, 6))
    dc = np.sqrt((xx - cx) ** 2 + (yy - cy) ** 2)
    alfa = satura(0.5 - (dc - Rr * irr))
    px, py = cx + rng.uniform(-0.06, 0.06) * Rr, cy + rng.uniform(-0.06, 0.06) * Rr
    dp = np.sqrt((xx - px) ** 2 + (yy - py) ** 2)
    tp = np.arctan2(yy - py, xx - px)
    ox, oy = px - cx, py - cy
    dxu, dyu = np.cos(tp), np.sin(tp)
    b = ox * dxu + oy * dyu
    cc = ox * ox + oy * oy - (Rr * irr) ** 2
    rb = -b + np.sqrt(np.maximum(b * b - cc, 1e-6))
    rho = np.clip(dp / rb, 0, 1.2)
    n_an = int(rng.integers(38, 56))
    anchos = rng.gamma(4.0, 1.0, n_an) * np.linspace(1.4, 0.7, n_an)
    bordes = np.concatenate([[0], np.cumsum(anchos)])
    bordes /= bordes[-1]
    ang_ruido = np.zeros_like(rho)
    for k, a in ((3, 0.012), (7, 0.006), (13, 0.003)):
        ang_ruido += a * np.sin(k * tp + rng.uniform(0, 6.3))
    rho_w = np.clip(rho * 0.94 + ang_ruido * np.clip(rho * 3, 0, 1), 0, 1)
    fase = np.interp(rho_w / 0.94, bordes, np.arange(n_an + 1)).astype(np.float32)
    fr = fase - np.floor(fase)
    tardia = suave(0.72, 0.93, fr) * (1 - suave(0.95, 1.0, fr))
    # fendas de secado: radiales casi rectas, en cuna (mas abiertas hacia fuera), borde dentado
    grietas = np.zeros_like(rho)
    dentado = R.espectral((N, N), rng, p=0.7, fmin=30) * 0.6 * esc
    for _ in range(int(rng.integers(4, 8))):
        a0 = rng.uniform(-np.pi, np.pi)
        r0, r1 = rng.uniform(0.02, 0.4), rng.uniform(0.55, 1.0)
        curva = rng.uniform(-0.15, 0.15)
        dth = np.angle(np.exp(1j * (tp - a0 - curva * rho)))
        dist = np.abs(dth) * dp + dentado
        t = np.clip((rho - r0) / max(r1 - r0, 1e-3), 0, 1)
        anchura = (0.5 + 7.0 * t ** 1.8) * esc
        dentro = (rho > r0) & (rho < min(r1, 0.96))
        grietas = np.maximum(grietas, (1 - suave(anchura * 0.5, anchura, dist)) * dentro)
    for _ in range(3):  # estrella central
        a0 = rng.uniform(-np.pi, np.pi)
        dth = np.angle(np.exp(1j * (tp - a0)))
        grietas = np.maximum(grietas, (1 - suave(0.4 * esc, 1.4 * esc, np.abs(dth) * dp)) * (rho < 0.1))
    corteza = suave(0.935, 0.95, rho)
    cambium = np.exp(-((rho - 0.935) / 0.006) ** 2)
    rug = R.espectral((N, N), rng, p=0.8, fmin=20)
    rug2 = R.espectral((N, N), rng, p=0.4, fmin=120)
    ang_s = rng.uniform(0, np.pi)
    sierra = np.sin(2 * np.pi * ((xx * np.cos(ang_s) + yy * np.sin(ang_s)) / (11 * esc) +
                                0.6 * R.espectral((N, N), rng, p=1.5, fmin=1, fmax=6)))
    alt = (0.8 * tardia - 9 * grietas + corteza * (6 + 4 * rug) + 0.5 * sierra * (1 - corteza) + 0.25 * rug2) * esc
    duramen = hexl("#9c6a45")
    albura = hexl("#d0b48c")
    c = mezclar(np.broadcast_to(duramen, (N, N, 3)).astype(np.float32), albura, suave(0.5, 0.72, rho))
    c = c * (1 - 0.15 * suave(0.0, 0.3, 0.3 - rho))[..., None]
    c = mezclar(c, c * 0.72, tardia * 0.75)
    manchas = suave(0.4, 1.6, R.espectral((N, N), rng, p=1.3, fmin=2))
    c = mezclar(c, hexl("#8a8076"), manchas * 0.14 * suave(0.6, 0.9, rho))      # algo de gris de intemperie
    c = c * (1 + 0.05 * rug2 + 0.03 * sierra * (1 - corteza))[..., None]
    c = mezclar(c, hexl("#2a1d14"), satura(grietas * 1.1))
    c = mezclar(c, hexl("#8a6a48"), cambium * 0.7)
    c = mezclar(c, mezclar(hexl("#3b2f26"), hexl("#5e5246"), satura(0.5 + 0.5 * rug)), corteza)
    suav = 0.32 - 0.12 * corteza - 0.08 * grietas
    normal = normal_desde_altura(alt, 1.0)
    ocl = np.zeros_like(alt)
    for r, k in ((3, 0.05), (12, 0.02)):
        ocl += k * np.clip(ndi.gaussian_filter(alt, r * esc) - alt, 0, None)
    ao = np.clip(1 - ocl, 0.2, 1)
    c = c * (1 - 0.3 * (1 - ao))[..., None]
    peso = np.clip(alfa * 8, 0, 1)
    pila = push_pull(np.concatenate([c, normal, ao[..., None], suav[..., None]], -1), peso)
    mn, mx = float(alt.min()), float(alt.max())
    return dict(color=np.clip(pila[..., :3], 0, 1), alfa=alfa, normal=normalizar_v(pila[..., 3:6]),
                ao=np.clip(pila[..., 6], 0, 1), suav=np.clip(pila[..., 7], 0, 1), altura=(alt - mn) / max(mx - mn, 1e-6))
