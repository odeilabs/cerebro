"""Elementos estampables con envoltura periodica: hojas, ramitas, semillas y piedras.

Cada elemento se evalua analiticamente sobre una ventana pequena (su caja girada), con bordes
antialiasados por distancia con signo, y se compone sobre el Lienzo. Las hojas 'se posan'
sobre lo que hay debajo (envolvente superior suavizada segun su rigidez), asi el apilado
tiene alturas coherentes: las hojas de arriba quedan mas altas y se arquean sobre las ramitas.
"""
import math

import numpy as np
from scipy import ndimage
from scipy.interpolate import PchipInterpolator

from nucleo import F32, suave, ventana

# ---------------------------------------------------------------------------------------------
# Hojas
# ---------------------------------------------------------------------------------------------
# Perfiles de semiancho relativo a lo largo del limbo (t: 0 = base/peciolo, 1 = punta).
FORMAS_HOJA = {
    # eliptica con punta de goteo (la mas tipica del bosque lluvioso)
    "gotero": ([0, .02, .08, .20, .35, .50, .62, .74, .83, .90, .95, 1.0],
               [0, .18, .50, .82, .97, 1.0, .93, .72, .42, .16, .07, 0]),
    "ovada": ([0, .02, .08, .18, .32, .50, .68, .82, .92, 1.0],
              [0, .35, .70, .93, 1.0, .90, .65, .38, .14, 0]),
    "obovada": ([0, .10, .25, .45, .62, .75, .86, .94, .985, 1.0],
                [0, .18, .45, .78, .97, 1.0, .88, .62, .30, 0]),
    "lanceolada": ([0, .04, .15, .30, .45, .60, .75, .88, .96, 1.0],
                   [0, .30, .75, .97, 1.0, .90, .65, .33, .10, 0]),
    # redondeada de base acorazonada (tipo Macaranga / Piper); t<0 = lobulos basales
    "acorazonada": ([-.14, -.10, -.04, .04, .15, .30, .48, .64, .78, .89, .96, 1.0],
                    [0, .45, .78, .93, .99, 1.0, .95, .80, .58, .32, .12, 0]),
    # foliolo de palma roto: tira larga, base truncada
    "palma": ([0, .02, .15, .50, .80, .93, 1.0],
              [.70, .88, 1.0, 1.0, .82, .45, 0]),
}


def perfil_hoja(rng, forma, muestras=256):
    """Perfil de semiancho (0-1) muestreado en t con variacion por hoja."""
    t, w = FORMAS_HOJA[forma]
    t = np.asarray(t, np.float64)
    w = np.asarray(w, np.float64) * (1.0 + rng.normal(0.0, 0.05, len(w)))
    w[-1] = 0.0
    if forma not in ("palma",):
        w[0] = 0.0
    w = np.clip(w, 0.0, None)
    tt = np.linspace(t[0], 1.0, muestras)
    gamma = rng.uniform(0.85, 1.18)
    tn = (tt - t[0]) / (1.0 - t[0])
    tq = t[0] + (1.0 - t[0]) * tn ** gamma
    p = PchipInterpolator(t, w)(tq)
    p = np.clip(p, 0.0, None)
    return tt.astype(F32), (p / max(p.max(), 1e-6)).astype(F32)


def estampar_hoja(lz, texel, cy, cx, h, tex):
    """Estampa una hoja con parametros h (dict) centrada en (cy, cx) px.

    tex: ruidos globales n x n (std~1): 'borde', 'moteado', 'parches', 'agujeros', 'manchas'.
    Claves de h: largo, semiancho (px), angulo, curva, peciolo, perfil (tt, p), forma,
    copa, pliegue, arco, punta, nervio (+1 relieve / -1 surco), color, color2, color_nervio,
    parche, borde_oscuro, manchas, agujeros, irregular, suav, rigidez, corte (o None),
    paralelas (bool, nervadura paralela de palma), n_venas, ang_venas, grosor_m.
    """
    n = lz.n
    L = float(h["largo"])
    W = float(h["semiancho"])
    ang = h["angulo"]
    curva = h["curva"]
    pet = float(h["peciolo"])
    tt, prof = h["perfil"]
    t_min = float(tt[0])
    ca, sa = math.cos(ang), math.sin(ang)

    u_lo, u_hi = min(-pet, t_min * L) - 2.0, L + 2.0
    vmax = W * 1.2 + abs(curva) * L + 2.0
    esq_u = np.array([u_lo, u_lo, u_hi, u_hi])
    esq_v = np.array([-vmax, vmax, -vmax, vmax])
    ex = esq_u * ca - esq_v * sa
    ey = esq_u * sa + esq_v * ca
    uc, vc = (u_lo + u_hi) * 0.5, curva * L * 0.3
    bx = cx - (uc * ca - vc * sa)
    by = cy - (uc * sa + vc * ca)
    x0 = int(math.floor(bx + ex.min()))
    y0 = int(math.floor(by + ey.min()))
    wv = int(math.ceil(bx + ex.max())) - x0 + 1
    hv = int(math.ceil(by + ey.max())) - y0 + 1
    if wv >= n or hv >= n:
        return
    idx = ventana(n, y0, x0, hv, wv)
    yy, xx = np.mgrid[0:hv, 0:wv].astype(F32)
    dx = xx + (x0 - bx)
    dy = yy + (y0 - by)
    u = dx * ca + dy * sa
    v = -dx * sa + dy * ca
    t = u / L
    vv = v - curva * L * np.clip(t, 0.0, 1.0) ** 2
    av = np.abs(vv)

    prof_t = np.interp(t, tt, prof, left=0.0, right=0.0).astype(F32)
    irr = 1.0 + h["irregular"] * tex["borde"][idx]
    hw = W * prof_t * irr
    if h.get("dientes", 0) > 0:
        # margen aserrado regular: dientes que apuntan hacia la punta
        fase = np.mod(u / h["dientes_esp"] + (vv > 0) * 0.5, 1.0)
        hw = hw - h["dientes"] * (fase ** 1.6) * np.clip(prof_t * 4, 0, 1)
    dentro = hw - av
    # mordiscos en el margen (insectos / rotura): circulos restados sobre el borde
    for (tb, lado_b, rb) in h.get("mordiscos", ()):
        pb_u = tb * L
        pb_v = lado_b * W * float(np.interp(tb, tt, prof))
        db = np.hypot(u - pb_u, vv - pb_v) - rb
        dentro = np.minimum(dentro, db)
    a = np.clip(dentro + 0.5, 0.0, 1.0)
    if h["forma"] == "acorazonada":
        # seno basal: hendidura estrecha entre los lobulos donde entra el peciolo
        seno = W * 0.22 * np.clip((0.03 - t) / 0.17, 0.0, 1.0)
        a *= np.clip(av - seno + 0.5, 0.0, 1.0) * (t < 0.03) + (t >= 0.03)
    # peciolo
    r_pet = max(0.55, W * 0.045)
    a_pet = np.clip(r_pet - av + 0.5, 0.0, 1.0) * np.clip(u + pet + 0.5, 0.0, 1.0) * (t < 0.08)
    if h["forma"] == "palma":
        a_pet *= 0.0
        a *= np.clip(u + 0.5, 0.0, 1.0)
    # agujeros (descomposicion) y fragmentacion
    w_rib = max(0.6, W * 0.05)
    if h["agujeros"] > 0:
        # agujeros discretos (mapa de celdas): q = distancia/radio, u = azar por agujero
        q = tex["agujeros_q"][idx]
        activo = tex["agujeros_u"][idx] < h["agujeros"]
        hueco = (1.0 - suave(q, 0.8, 1.05)) * activo * suave(av, w_rib * 1.3, w_rib * 2.6)
        a *= 1.0 - hueco
    if h["corte"] is not None:
        tc, angc, lado = h["corte"]
        s = (u - tc * L) * math.cos(angc) + vv * math.sin(angc) + 2.5 * tex["borde"][idx]
        a *= np.clip(lado * s + 0.5, 0.0, 1.0)
        a_pet *= np.clip(lado * s + 0.5, 0.0, 1.0)
    alfa = np.maximum(a, a_pet)
    if alfa.max() <= 0.01:
        return

    # ------------------------- altura (m) -------------------------
    tc_ = np.clip(t, 0.0, 1.0)
    hw_s = np.maximum(hw, 1.0)
    z = np.full(alfa.shape, h["grosor_m"], F32)
    z += h["copa"] * (av * av / hw_s) * texel
    z += h["pliegue"] * np.minimum(av, hw_s) * texel
    z += h["arco"] * L * texel * 4.0 * (tc_ - 0.5) ** 2
    z += h["punta"] * L * texel * suave(tc_, 0.62, 1.0) ** 2
    nerv = np.exp(-(vv / w_rib) ** 2) * (1.0 - 0.65 * tc_) * (t > -0.02)
    z += h["nervio"] * 0.0007 * nerv
    lam = suave(av, w_rib, 2.2 * w_rib) * np.clip(1.0 - (av / hw_s) ** 3, 0, 1)  # zona de limbo
    if h["paralelas"]:
        esp = max(1.6, W * 0.22)
        venas = 0.5 + 0.5 * np.cos(2 * math.pi * vv / esp)
        z += 0.00022 * venas
        mvenas = venas
    else:
        esp = L / h["n_venas"]
        k = 1.0 / math.tan(h["ang_venas"])
        s = u - k * av - 0.35 * k * av * av / max(W, 1.0)
        fr = s / esp
        dfr = fr - np.round(fr)
        d = np.abs(dfr) * esp * math.sin(h["ang_venas"])
        mvenas = np.exp(-(d / 0.7) ** 2) * lam
        z += h["nervio"] * 0.0004 * mvenas
        # abombado del limbo entre nervios secundarios (lo que da el aspecto de hoja seca)
        bulto = (0.5 - 0.5 * np.cos(2 * math.pi * dfr)) * lam
        z += -h["nervio"] * 0.00035 * h.get("arrugas", 0.5) * bulto
    # arrugas finas y ondulacion a escala de hoja
    if "arrugas" in tex:
        z += h.get("arrugas", 0.5) * 0.00012 * tex["arrugas"][idx]
    if "ondas" in tex:
        z += h.get("ondas", 0.0) * 0.0009 * tex["ondas"][idx] * suave(dentro, 0.0, W * 0.5)
    z += 0.00045 * a_pet * (a_pet > a)

    # posado sobre lo de debajo: parte se apoya en los salientes (envolvente) segun rigidez y
    # parte cede y se amolda (desenfoque), como una lamina blanda.
    h0 = lz.altura[idx]
    k_rig = int(max(3, h["rigidez"] * W))
    if k_rig < min(hv, wv):
        amold = ndimage.gaussian_filter(h0, max(1.0, k_rig * 0.3), mode="nearest")
        env = ndimage.gaussian_filter(ndimage.maximum_filter(h0, size=k_rig, mode="nearest"),
                                      k_rig * 0.45, mode="nearest")
        # el borde de la hoja toca lo de debajo (salvo lo que levante la copa)
        apoyo = h.get("apoyo", 0.4) * suave(dentro, 0.0, max(2.0, W * 0.35))
        base = amold + apoyo * (env - amold)
    else:
        base = np.full_like(h0, np.percentile(h0[alfa > 0.5], 70) if (alfa > 0.5).any() else h0.mean())
    altura = base + z

    # ------------------------- color -------------------------
    c = np.asarray(h["color"], F32)
    c2 = np.asarray(h["color2"], F32)
    c3 = np.asarray(h.get("color3", c), F32)
    mot = tex["moteado"][idx]
    par = tex["parches"][idx]
    borde = np.exp(-np.maximum(dentro, 0.0) / (0.10 * W + 1.0))
    # deterioro desde el margen y en manchas difusas
    mezcla = suave(par + 1.8 * borde * h["borde_oscuro"], h["parche"], h["parche"] + 2.2)
    col = c[None, None, :] * (1.0 + 0.06 * mot)[..., None]
    # variacion de tono a escala de hoja (zonas mas rojizas / mas amarillentas)
    tono = tex["tono"][idx] if "tono" in tex else mot
    col = col + (c3[None, None, :] - col) * (suave(tono, -0.5, 1.5) * h.get("var_tono", 0.5))[..., None]
    col = col + (c2[None, None, :] - col) * mezcla[..., None]
    if h["manchas"] > 0 and "manchas" in tex:
        mk = tex["manchas"][idx]  # 0 fondo, ~0.5 halo, 1 nucleo
        nucleo = suave(mk, 0.75, 0.9) * h["manchas"]
        halo = suave(mk, 0.35, 0.55) * (1 - suave(mk, 0.75, 0.9)) * h["manchas"]
        col = col * (1.0 - 0.55 * nucleo)[..., None] + (col * 1.18 * np.array([1.05, 1.0, 0.8], F32) - col) * (0.6 * halo)[..., None]
    cn = np.asarray(h["color_nervio"], F32)
    m_nerv = np.clip(np.exp(-(vv / (w_rib * 1.1)) ** 2) * (tc_ < 0.97) * (t > -0.02) + 0.85 * (a_pet > a), 0, 1)
    col = col + (cn[None, None, :] - col) * (m_nerv * 0.7)[..., None]
    col = col + (cn[None, None, :] - col) * (0.6 * h.get("venas_col", 0.5) * mvenas)[..., None]
    col *= (1.0 + h["gradiente"] * (tc_ - 0.5))[..., None]

    s = h["suav"] + 0.04 * mot - 0.10 * mezcla
    lz.componer(idx, alfa.astype(F32), altura.astype(F32), col.astype(F32), s.astype(F32),
                extra={"hoja": np.ones_like(alfa)} if "hoja" in lz.extra else None)


# ---------------------------------------------------------------------------------------------
# Ramitas / raices (cilindros curvos)
# ---------------------------------------------------------------------------------------------
def curva_bezier(p0, p1, p2, k=24):
    t = np.linspace(0, 1, k)[:, None]
    return ((1 - t) ** 2) * p0 + 2 * (1 - t) * t * p1 + (t ** 2) * p2


def distancia_polilinea(py, px, pts):
    """Distancia minima de cada pixel a una polilinea y parametro (0-1) a lo largo de ella."""
    mejor = np.full(py.shape, np.inf, F32)
    param = np.zeros(py.shape, F32)
    segs = len(pts) - 1
    for i in range(segs):
        ay, ax = pts[i]
        by_, bx_ = pts[i + 1]
        vy, vx = by_ - ay, bx_ - ax
        l2 = vy * vy + vx * vx + 1e-9
        tt = np.clip(((py - ay) * vy + (px - ax) * vx) / l2, 0.0, 1.0)
        d = np.hypot(py - (ay + tt * vy), px - (ax + tt * vx))
        m = d < mejor
        mejor = np.where(m, d, mejor)
        param = np.where(m, (i + tt) / segs, param)
    return mejor.astype(F32), param.astype(F32)


def estampar_ramita(lz, texel, cy, cx, r, tex, modo="sobre"):
    """Cilindro curvo (ramita o raiz). r: dict con largo, radio (px), angulo, curva, color,
    color2 (puntas/corteza clara), suav, conicidad, enterrado (0-1 fraccion del radio bajo la
    superficie), rigidez, aplanado (0 cilindro .. 1 cinta)."""
    n = lz.n
    L = r["largo"]
    R = r["radio"]
    ang = r["angulo"]
    ca, sa = math.cos(ang), math.sin(ang)
    p0 = np.array([-L / 2 * sa, -L / 2 * ca])
    p2 = np.array([L / 2 * sa, L / 2 * ca])
    perp = np.array([ca, -sa])
    p1 = perp * r["curva"] * L
    pts = curva_bezier(p0, p1, p2, 20)
    if r.get("ondulacion", 0) > 0:
        tt = np.linspace(0, 1, len(pts))
        pts = pts + perp[None, :] * (r["ondulacion"] * L * np.sin(tt * math.pi * r.get("ondas", 3) + r.get("fase", 0)))[:, None]
    m = R + 3
    y_lo, y_hi = pts[:, 0].min() - m, pts[:, 0].max() + m
    x_lo, x_hi = pts[:, 1].min() - m, pts[:, 1].max() + m
    y0 = int(math.floor(cy + y_lo))
    x0 = int(math.floor(cx + x_lo))
    hv = int(math.ceil(y_hi - y_lo)) + 2
    wv = int(math.ceil(x_hi - x_lo)) + 2
    if hv >= n or wv >= n:
        return
    idx = ventana(n, y0, x0, hv, wv)
    yy, xx = np.mgrid[0:hv, 0:wv].astype(F32)
    py = yy + y0 - cy
    px = xx + x0 - cx
    d, s = distancia_polilinea(py, px, pts)
    rad = R * (1.0 - r["conicidad"] * s)
    alfa = np.clip(rad - d + 0.5, 0.0, 1.0)
    if alfa.max() <= 0.01:
        return
    q = np.clip(d / np.maximum(rad, 0.3), 0.0, 1.0)
    perfil = np.sqrt(np.maximum(1.0 - q * q, 0.0))
    perfil = perfil * (1 - r.get("aplanado", 0.0)) + r.get("aplanado", 0.0) * suave(1 - q, 0, 0.5)
    rad_m = rad * texel
    z = rad_m * (1.0 + perfil) - 2 * rad_m * r["enterrado"]
    # corteza: estrias longitudinales y nudos
    estr = tex["moteado"][idx]
    z += 0.08 * rad_m * estr * perfil
    h0 = lz.altura[idx]
    k_rig = int(max(3, r["rigidez"]))
    if k_rig < min(hv, wv):
        env = ndimage.maximum_filter(h0, size=k_rig, mode="nearest")
        base = ndimage.gaussian_filter(env, k_rig * 0.5, mode="nearest")
    else:
        base = np.full_like(h0, h0.mean())
    if modo == "enterrado":
        base = ndimage.gaussian_filter(h0, max(1.0, R), mode="nearest")
    altura = base + z
    c = np.asarray(r["color"], F32)
    c2 = np.asarray(r["color2"], F32)
    lat = 1.0 - perfil  # cerca de los flancos
    col = c[None, None, :] * (1.0 + 0.12 * estr)[..., None]
    col = col + (c2[None, None, :] - col) * (suave(tex["parches"][idx], 0.6, 1.6) * r.get("manchas2", 0.6))[..., None]
    col *= (1.0 - 0.18 * lat)[..., None]
    sv = np.full(alfa.shape, r["suav"], F32) + 0.03 * estr
    if modo == "enterrado":
        lz.componer_max(idx, alfa, altura, col, sv, suav_borde=0.4 * rad_m.mean() + 1e-5)
    else:
        lz.componer(idx, alfa, altura, col, sv)


# ---------------------------------------------------------------------------------------------
# Piedras / semillas (domos redondeados)
# ---------------------------------------------------------------------------------------------
def estampar_piedra(lz, texel, cy, cx, p, tex, modo="max"):
    """Piedra redondeada: contorno con armonicos bajos, perfil superelipsoide.

    p: radio (px), elong (b/a <= 1), angulo, armonicos [(k, amp, fase)], alto_rel (altura /
    radio), exp (2 = elipsoide, >2 aplanada arriba), hundido (m bajo la base), color, textura
    (callable(idx, rho, alfa) -> (col, dz) opcional), suav, base ('local' o valor m).
    Devuelve (idx, alfa) por si el llamador quiere anadir canales.
    """
    n = lz.n
    R = p["radio"]
    e = p["elong"]
    ang = p["angulo"]
    ca, sa = math.cos(ang), math.sin(ang)
    amp_tot = sum(abs(a) for _, a, _ in p["armonicos"])
    m = int(math.ceil(R * (1 + amp_tot) + 3))
    y0 = int(math.floor(cy)) - m
    x0 = int(math.floor(cx)) - m
    hv = wv = 2 * m + 1
    if hv >= n:
        return None, None
    idx = ventana(n, y0, x0, hv, wv)
    yy, xx = np.mgrid[0:hv, 0:wv].astype(F32)
    dy = yy + y0 - cy
    dx = xx + x0 - cx
    u = dx * ca + dy * sa
    v = (-dx * sa + dy * ca) / e
    rr = np.hypot(u, v)
    phi = np.arctan2(v, u)
    Rphi = np.full_like(rr, R)
    for k, a, f in p["armonicos"]:
        Rphi = Rphi * (1.0 + a * np.cos(k * phi + f))
    rho = rr / Rphi
    # antialias: distancia aproximada al contorno en px (en la direccion menor)
    dist = (1.0 - rho) * Rphi * e
    alfa = np.clip(dist + 0.5, 0.0, 1.0)
    if alfa.max() <= 0.01:
        return None, None
    ex = p["exp"]
    perfil = np.power(np.clip(1.0 - np.power(np.clip(rho, 0, 1), ex), 0.0, 1.0), 1.0 / ex)
    H = p["alto_rel"] * R * e * texel
    z = H * perfil - p["hundido"]
    col = np.broadcast_to(np.asarray(p["color"], F32), alfa.shape + (3,)).copy()
    sv = np.full(alfa.shape, p["suav"], F32)
    if p.get("textura") is not None:
        res = p["textura"](idx, rho, perfil, col, u / R, v / R)
        col, dz = res[0], res[1]
        if len(res) > 2:
            sv = res[2]
        z = z + dz
    h0 = lz.altura[idx]
    if p.get("base", "local") == "local":
        base = ndimage.gaussian_filter(h0, max(1.0, R * 0.6), mode="nearest")
        base = float(np.mean(base[alfa > 0.5])) if (alfa > 0.5).any() else float(h0.mean())
    else:
        base = float(p["base"])
    altura = base + z
    extra = {k: np.full(alfa.shape, v, F32) for k, v in p.get("extra", {}).items()} or None
    if modo == "max":
        a = lz.componer_max(idx, alfa, altura.astype(F32), col.astype(F32), sv, extra=extra,
                            suav_borde=max(H * 0.06, 1e-5))
    else:
        lz.componer(idx, alfa, altura.astype(F32), col.astype(F32), sv, extra=extra)
        a = alfa
    return idx, a
