"""Ramilletes: una ramita que entra por abajo-centro de la celda y se ramifica, con hojas que se
reparten para llenar la celda sin tocar el borde. Lo usan hojas_dosel_a/b, hojas_ceiba y bambu.

La colocacion es voraz sobre una rejilla gruesa de ocupacion: en cada nodo de la ramita se prueban
varias orientaciones/tamanos de hoja y se elige la que mas superficie nueva cubre (con algo de azar
y penalizando el solape excesivo). Cada hoja se encoge hasta caber dentro del margen.
"""
import numpy as np
from skimage.draw import polygon as poligono

import hojas as H
from comun import hexl, mezclar, variar, suave as suave_np


class Celda:
    def __init__(self, x0, y0, ancho, alto=None):
        self.x0, self.y0 = float(x0), float(y0)
        self.ancho = float(ancho)
        self.alto = float(alto if alto is not None else ancho)
        self.esc = self.ancho / 1024.0  # escala respecto a una celda de 1024 px

    def img(self, u, v):
        return self.x0 + u * self.ancho, self.y0 + (1.0 - v) * self.alto

    def dentro(self, xs, ys, margen=0.02):
        mx = margen * self.ancho
        my = margen * self.alto
        return (xs.min() >= self.x0 + mx and xs.max() <= self.x0 + self.ancho - mx and
                ys.min() >= self.y0 + my and ys.max() <= self.y0 + self.alto - my)


class Ocupacion:
    """Rejilla gruesa para medir cuanta superficie nueva cubre una hoja candidata."""

    def __init__(self, celda, n=128):
        self.c = celda
        self.n = n
        self.g = np.zeros((n, n), np.float32)

    def mascara(self, xs, ys):
        f = self.n / self.c.ancho
        fy = self.n / self.c.alto
        rr, cc = poligono((ys - self.c.y0) * fy, (xs - self.c.x0) * f, (self.n, self.n))
        m = np.zeros((self.n, self.n), bool)
        m[rr, cc] = True
        return m

    def puntuar(self, m):
        area = m.sum()
        if area == 0:
            return -1e9, 0
        nuevo = (m & (self.g == 0)).sum()
        doble = (m & (self.g >= 2)).sum()
        return nuevo - 0.35 * (area - nuevo) - 0.8 * doble, area

    def marcar(self, m):
        self.g[m] += 1

    def cobertura(self):
        return float((self.g > 0).mean())


# ----------------------------------------------------------------------------------------------
# Esqueleto de la ramita
# ----------------------------------------------------------------------------------------------
def esqueleto(rng, celda, p):
    """Eje principal desde (0.5, v0) hacia arriba y laterales. Devuelve lista de ramas."""
    esc = celda.esc
    C = celda.ancho
    ramas = []
    x0, y0 = celda.img(0.5, p.get("v0", 0.03))
    largo = rng.uniform(*p["largo_eje"]) * C
    ang0 = np.pi / 2 + rng.uniform(-0.12, 0.12)
    pts = H.curva_suave((x0, y0), ang0, largo, curvatura=rng.uniform(-0.25, 0.25), n=28,
                        ruido_amp=0.05, rng=rng)
    r0, r1 = p["radio_eje"]
    ramas.append(dict(pts=pts, radios=np.linspace(r0, r1, len(pts)) * esc, nivel=0))
    n_lat = int(rng.integers(p["laterales"][0], p["laterales"][1] + 1))
    s0 = rng.choice([-1, 1])
    lados = [s0 * (1 if i % 2 == 0 else -1) for i in range(n_lat)]
    fr = np.sort(rng.uniform(p.get("lateral_desde", 0.18), p.get("lateral_hasta", 0.6), n_lat))
    for i in range(n_lat):
        k = int(fr[i] * (len(pts) - 1))
        base = pts[k]
        tang = np.arctan2(-(pts[k + 1][1] - pts[k][1]), pts[k + 1][0] - pts[k][0])
        lado = lados[i]
        ang = tang + lado * np.radians(rng.uniform(*p["angulo_lateral"]))
        lar = rng.uniform(*p["largo_lateral"]) * C
        ptl = H.curva_suave(base, ang, lar, curvatura=-lado * rng.uniform(0.1, 0.5), n=18,
                            ruido_amp=0.05, rng=rng)
        rb = np.interp(k, [0, len(pts) - 1], [r0, r1]) * 0.75
        ramas.append(dict(pts=ptl, radios=np.linspace(rb, r1 * 0.8, len(ptl)) * esc, nivel=1))
    return ramas


def nodos(rng, ramas, celda, p):
    """Nodos alternos a lo largo de cada rama: (x, y, angulo_rama, lado, frac, rama)."""
    res = []
    for ir, r in enumerate(ramas):
        pts = r["pts"]
        seg = np.sqrt((np.diff(pts, axis=0) ** 2).sum(1))
        acum = np.concatenate([[0], np.cumsum(seg)])
        tot = acum[-1]
        paso = p["paso_nodos"] * celda.ancho * rng.uniform(0.85, 1.15)
        pos = tot * p.get("nodo_desde", 0.12) + rng.uniform(0, paso * 0.5)
        lado = rng.choice([-1, 1])
        while pos < tot * 0.97:
            x = np.interp(pos, acum, pts[:, 0])
            y = np.interp(pos, acum, pts[:, 1])
            k = min(int(np.searchsorted(acum, pos)), len(pts) - 1)
            k0 = max(k - 1, 0)
            ang = np.arctan2(-(pts[k][1] - pts[k0][1]), pts[k][0] - pts[k0][0] + 1e-9)
            res.append(dict(x=x, y=y, ang=ang, lado=lado, frac=pos / tot, rama=ir, terminal=False))
            lado = -lado
            pos += paso * rng.uniform(0.8, 1.2)
        # hoja terminal
        k = len(pts) - 1
        ang = np.arctan2(-(pts[k][1] - pts[k - 1][1]), pts[k][0] - pts[k - 1][0])
        res.append(dict(x=pts[k][0], y=pts[k][1], ang=ang, lado=0, frac=1.0, rama=ir, terminal=True))
    return res


def distancia_al_borde(x, y, ang, celda, margen=0.02):
    """Distancia (px) desde (x, y) hasta el margen de la celda en la direccion ang."""
    dx, dy = np.cos(ang), -np.sin(ang)
    mx, my = margen * celda.ancho, margen * celda.alto
    ts = []
    if dx > 1e-6:
        ts.append((celda.x0 + celda.ancho - mx - x) / dx)
    elif dx < -1e-6:
        ts.append((celda.x0 + mx - x) / dx)
    if dy > 1e-6:
        ts.append((celda.y0 + celda.alto - my - y) / dy)
    elif dy < -1e-6:
        ts.append((celda.y0 + my - y) / dy)
    return max(0.0, min(ts)) if ts else 0.0


def _mejor_candidato(rng, celda, tipo, nd, occ, p):
    mejor = None
    for _ in range(p.get("intentos", 10)):
        geo = tipo.proponer(rng, nd, celda)
        geo = ajustar(geo, tipo, celda, p.get("margen", 0.02))
        if geo is None:
            continue
        xs, ys = tipo.contorno(geo)
        m = occ.mascara(xs, ys)
        pun, area = occ.puntuar(m)
        pun += rng.normal(0, 0.08) * area
        if mejor is None or pun > mejor[0]:
            mejor = (pun, geo, m, area)
    return mejor


def colocar(rng, celda, tipo, nds, p):
    """Eleccion voraz de una hoja por nodo (y extras) maximizando la cobertura."""
    occ = Ocupacion(celda)
    elegidas = []
    orden = list(range(len(nds)))
    rng.shuffle(orden)
    # primero los nodos de abajo (hojas grandes/viejas), luego el resto
    orden.sort(key=lambda i: (nds[i]["frac"] + 0.25 * rng.random()) * (1.0 + 0.3 * nds[i]["rama"]))
    max_hojas = p["n_hojas"][1]
    for i in orden:
        if len(elegidas) >= max_hojas:
            break
        mejor = _mejor_candidato(rng, celda, tipo, nds[i], occ, p)
        if mejor is None:
            continue
        pun, geo, m, area = mejor
        if pun < p.get("umbral", 0.15) * area and len(elegidas) >= p["n_hojas"][0]:
            continue
        occ.marcar(m)
        geo["orden"] = len(elegidas)
        elegidas.append(geo)
    # hojas extra (brotes agrupados) mientras falte cobertura
    intentos = 0
    while len(elegidas) < max_hojas and occ.cobertura() < p.get("cobertura_obj", 0.62) and intentos < 40:
        intentos += 1
        nd = dict(nds[int(rng.integers(len(nds)))])
        nd["lado"] = rng.choice([-1, 1]) if nd["lado"] == 0 else -nd["lado"]
        nd["terminal"] = False
        mejor = _mejor_candidato(rng, celda, tipo, nd, occ, p)
        if mejor is None:
            continue
        pun, geo, m, area = mejor
        if pun < 0.35 * area:
            continue
        occ.marcar(m)
        geo["orden"] = len(elegidas)
        elegidas.append(geo)
    return elegidas, occ


def ajustar(geo, tipo, celda, margen):
    """Encoge la hoja hasta que quepa en la celda (con margen). None si queda demasiado pequena."""
    if geo is None:
        return None
    for _ in range(14):
        xs, ys = tipo.contorno(geo)
        if celda.dentro(xs, ys, margen):
            return geo
        geo = tipo.escalar(geo, 0.9)
        if geo.get("escala_rel", 1.0) < geo.get("escala_min", 0.45):
            return None
    return None


# ----------------------------------------------------------------------------------------------
# Tipo de hoja: simple eliptica-acuminada (dosel A)
# ----------------------------------------------------------------------------------------------
class HojaSimple:
    """Hoja simple de arbol de dosel: eliptica/ovada, punta de goteo, brillante, nervadura pinnada."""

    def __init__(self, p, paleta):
        self.p = p
        self.paleta = paleta

    def proponer(self, rng, nd, celda):
        p = self.p
        C = celda.ancho
        juventud = nd["frac"] if nd["rama"] == 0 else 0.3 + 0.7 * nd["frac"]
        if nd["terminal"]:
            ang = nd["ang"] + rng.uniform(-0.35, 0.35)
        else:
            a0, a1 = p["angulo_hoja"]
            # en los nodos bajos la hoja puede abrirse hacia atras (vista cenital de una rama horizontal)
            a1 = a1 + p.get("abrir_base", 45.0) * (1 - suave_np(0.15, 0.55, juventud))
            ang = nd["ang"] + nd["lado"] * np.radians(rng.uniform(a0, a1))
        tam = 1.0 - p.get("reduccion_punta", 0.3) * juventud ** 1.5
        lp = rng.uniform(*p["peciolo"]) * C * (0.7 + 0.3 * tam)
        bx = nd["x"] + lp * np.cos(ang)
        by = nd["y"] - lp * np.sin(ang)
        # la hoja "busca" el borde: largo = fraccion de la distancia libre hasta el margen
        alcance = distancia_al_borde(bx, by, ang, celda, p.get("margen", 0.02))
        lmin, lmax = p["largo_hoja"]
        L = float(np.clip(rng.uniform(*p.get("alcance", (0.75, 1.0))) * alcance, lmin * C * 0.8, lmax * C)) * tam
        # vista desde arriba algunas hojas estan inclinadas: se ven mas estrechas y asimetricas
        giro = rng.normal(0, 0.18)
        prop = rng.uniform(*p["proporcion"]) * (1 - min(abs(giro), 0.45) * 0.6)
        geo = dict(nx=nd["x"], ny=nd["y"], ang=ang, lp=lp, L=L, bx=bx, by=by,
                   W=L * prop * 0.5, asim=float(np.clip(rng.uniform(-0.06, 0.06) + giro * 0.4, -0.25, 0.25)),
                   k2=rng.uniform(-0.1, 0.1), k3=rng.uniform(-0.05, 0.05),
                   incl_t=giro * 0.5, incl_s=rng.normal(0, 0.12),
                   edad=float(np.clip(1.0 - juventud + rng.normal(0, 0.15), 0, 1)),
                   semilla=int(rng.integers(1 << 30)), escala_rel=1.0, escala_min=0.45,
                   curva_peciolo=rng.uniform(-0.4, 0.4))
        return geo

    def escalar(self, geo, f):
        g = dict(geo)
        g["L"] *= f
        g["W"] *= f
        g["lp"] *= (0.5 + 0.5 * f)
        g["bx"] = g["nx"] + g["lp"] * np.cos(g["ang"])
        g["by"] = g["ny"] - g["lp"] * np.sin(g["ang"])
        g["escala_rel"] = geo.get("escala_rel", 1.0) * f
        return g

    def escalar_desde(self, geo, k, ox, oy):
        g = dict(geo)
        for cx, cy in (("nx", "ny"), ("bx", "by")):
            g[cx] = ox + (geo[cx] - ox) * k
            g[cy] = oy + (geo[cy] - oy) * k
        g["L"] *= k
        g["W"] *= k
        g["lp"] *= k
        return g

    def _perfil(self, geo):
        rng = np.random.default_rng(geo["semilla"])
        p = self.p
        return H.perfil_eliptico(rng, pos_max=p.get("pos_max", 0.45), base_redonda=rng.uniform(0.3, 0.8),
                                 largo_punta=rng.uniform(*p["punta"]), ancho_punta=rng.uniform(0.04, 0.07),
                                 ancho_en_punta=rng.uniform(0.24, 0.34))

    def _marco(self, geo):
        return H.Marco(geo["bx"], geo["by"], geo["ang"], geo["L"], geo["k2"], geo["k3"])

    def contorno(self, geo):
        P = self._perfil(geo)
        m = self._marco(geo)
        s = np.linspace(0, 1, 40)
        wl = P(s) * geo["W"] * (1 + geo["asim"])
        wr = P(s) * geo["W"] * (1 - geo["asim"])
        xl, yl = m.a_imagen(s, wl)
        xr, yr = m.a_imagen(s[::-1], -wr[::-1])
        return np.concatenate([xl, xr, [geo["nx"]]]), np.concatenate([yl, yr, [geo["ny"]]])

    def pintar(self, geo, lienzo, rng, banco, z, esc):
        rng = np.random.default_rng(geo["semilla"] + 7)
        pal = self.paleta
        edad = geo["edad"]
        # ---- peciolo ----
        pts = H.curva_suave((geo["nx"], geo["ny"]), geo["ang"] - geo["curva_peciolo"] * 0.5, geo["lp"] * 1.04,
                            curvatura=geo["curva_peciolo"], n=10)
        rp = self.p["radio_peciolo"] * esc * (0.75 + 0.35 * geo["escala_rel"])
        radios = np.linspace(rp * 0.9, rp * 1.15, len(pts))
        radios[-2:] *= 1.18  # pulvinulo
        col_p = variar(pal["peciolo"], rng, 0.02, 0.15, 0.15)
        capa, _ = H.render_tubo(pts, radios, banco, rng, col_p * 0.85, col_p, z=z - 2, suav=0.35, grosor=0.85,
                                estrias=0.4, nudos=[(1.0, 0.25)])
        lienzo.pintar(capa)
        # ---- limbo ----
        P = self._perfil(geo)
        m = self._marco(geo)
        Wl = geo["W"] * (1 + geo["asim"])
        Wr = geo["W"] * (1 - geo["asim"])
        est = self.estilo(rng, edad, esc, geo)
        danos = H.danos_aleatorios(rng, wmax=geo["W"], esc=esc * self.p.get("escala_danos", 1.0), edad=edad,
                                   prob=self.p.get("prob_danos", 1.0), liquen=self.p.get("liquen", 1.0))
        for d in danos:
            if d["tipo"] == "mordisco":
                d["w"] = float(P(np.array([d["s"]]))[0]) * (Wl if d["lado"] > 0 else Wr)
        capa = H.render_hoja(m, lambda s: P(s) * Wl, lambda s: P(s) * Wr, est, rng, banco, z=z, danos=danos, esc=esc)
        lienzo.pintar(capa)

    def estilo(self, rng, edad, esc, geo):
        pal = self.paleta
        joven = 1 - edad
        base = mezclar(pal["viejo"], pal["joven"], joven ** 1.5 * 0.8)
        base = variar(base, rng, dh=0.015, ds=0.12, dv=0.14)
        p = self.p
        L = geo["L"]
        return dict(
            color=base,
            color_vena=variar(pal["vena"], rng, 0.01, 0.1, 0.1),
            color_nervio=variar(pal["nervio"], rng, 0.01, 0.1, 0.1),
            color_amarillo=pal["amarillo"], color_seco=pal["seco"], color_seco_oscuro=pal["seco_oscuro"],
            color_halo=pal["halo"], color_liquen=pal["liquen"],
            var_valor=rng.uniform(0.08, 0.14), var_tono=rng.uniform(0.03, 0.12),
            n_venas=int(rng.integers(*p["n_venas"])), angulo_vena=rng.uniform(*p["angulo_vena"]),
            curva_vena=rng.uniform(0.35, 0.8),
            ancho_nervio=p.get("ancho_nervio", 4.5) * (L / (0.36 * 1024 * esc)) ** 0.5,
            ancho_vena=p.get("ancho_vena", 1.6),
            contraste_vena=p.get("contraste_vena", 0.32) * rng.uniform(0.8, 1.2),
            contraste_nervio=p.get("contraste_nervio", 0.7),
            suavidad=p.get("suavidad", 0.6) + 0.05 * joven - 0.06 * edad,
            combado=rng.uniform(0.12, 0.3), plegado=rng.uniform(0.0, 0.12), arqueo=rng.uniform(0.0, 0.08),
            grosor=p.get("grosor", 0.2), incl_t=geo.get("incl_t", 0.0), incl_s=geo.get("incl_s", 0.0),
        )


def ajustar_conjunto(ramas, elegidas, tipo, celda, p):
    """Escala todo el ramillete respecto al punto de insercion para que llene la celda."""
    ox, oy = celda.img(0.5, p.get("v0", 0.03))
    margen = p.get("margen", 0.02)

    def cabe(k):
        for g in elegidas:
            xs, ys = tipo.contorno(tipo.escalar_desde(g, k, ox, oy))
            if not celda.dentro(xs, ys, margen):
                return False
        for r in ramas:
            pts = r["pts"]
            xs = ox + (pts[:, 0] - ox) * k
            ys = oy + (pts[:, 1] - oy) * k
            if not celda.dentro(xs, ys, margen):
                return False
        return True

    k = 1.0
    paso = 0.02
    while k + paso <= p.get("escala_max", 1.4) and cabe(k + paso):
        k += paso
    if k <= 1.0:
        return ramas, elegidas
    nuevas = []
    for r in ramas:
        r = dict(r)
        pts = r["pts"].copy()
        pts[:, 0] = ox + (pts[:, 0] - ox) * k
        pts[:, 1] = oy + (pts[:, 1] - oy) * k
        r["pts"] = pts
        nuevas.append(r)
    return nuevas, [dict(tipo.escalar_desde(g, k, ox, oy), z=g.get("z", 0)) for g in elegidas]


# ----------------------------------------------------------------------------------------------
# Construccion de un ramillete completo en una celda
# ----------------------------------------------------------------------------------------------
def ramillete(lienzo, celda, rng, banco, tipo, p, paleta_rama, estilo_rama=None):
    esc = celda.esc
    er = dict(lenticelas=0.25, estrias=0.8, paso_nudos=0.0, fuerza_nudos=0.3)
    er.update(estilo_rama or {})
    ramas = esqueleto(rng, celda, p)
    nds = nodos(rng, ramas, celda, p)
    elegidas, occ = colocar(rng, celda, tipo, nds, p)
    ramas, elegidas = ajustar_conjunto(ramas, elegidas, tipo, celda, p)
    # orden de dibujo: hojas viejas (abajo de la rama) debajo; jovenes encima; con azar
    for g in elegidas:
        g["z"] = 30.0 * (1.0 - g.get("edad", 0.5)) * 0 + rng.uniform(0, 40) + 25 * (1 - g.get("edad", 0.5))
    elegidas.sort(key=lambda g: g["z"])
    n_debajo = int(len(elegidas) * p.get("frac_debajo_rama", 0.3))
    for g in elegidas[:n_debajo]:
        tipo.pintar(g, lienzo, rng, banco, g["z"], esc)
    # ramas (de la mas fina a la principal, para que la principal quede encima en los cruces)
    for r in sorted(ramas, key=lambda r: -r["nivel"]):
        col_a = paleta_rama["lenoso"]
        col_b = paleta_rama["verde"] if r["nivel"] > 0 else mezclar(paleta_rama["lenoso"], paleta_rama["verde"], 0.6)
        nudos = ()
        if er["paso_nudos"] > 0:
            lon = float(np.sqrt((np.diff(r["pts"], axis=0) ** 2).sum(1)).sum())
            paso = er["paso_nudos"] * celda.ancho
            nudos = [(x / lon, er["fuerza_nudos"]) for x in np.arange(paso * rng.uniform(0.3, 1.0), lon, paso)]
        capa, _ = H.render_tubo(r["pts"], r["radios"], banco, rng, col_a, col_b, z=30.0, suav=er.get("suav", 0.2),
                                grosor=0.95, lenticelas=er["lenticelas"], estrias=er["estrias"], nudos=nudos)
        lienzo.pintar(capa)
    for g in elegidas[n_debajo:]:
        tipo.pintar(g, lienzo, rng, banco, g["z"] + 30, esc)
    return dict(n_hojas=len(elegidas), cobertura=occ.cobertura())


# ----------------------------------------------------------------------------------------------
# Atlas: hojas_dosel_a
# ----------------------------------------------------------------------------------------------
PALETA_DOSEL_A = dict(
    viejo=hexl("#243a17"), joven=hexl("#4a6a22"), vena=hexl("#577530"), nervio=hexl("#859a4a"),
    peciolo=hexl("#526227"), amarillo=hexl("#9c9634"), seco=hexl("#7d6446"), seco_oscuro=hexl("#3a2a1a"),
    halo=hexl("#8f9433"), liquen=hexl("#9aa38c"),
)
PALETA_RAMA = dict(lenoso=hexl("#4f4030"), verde=hexl("#56632b"))

VARIANTES_DOSEL_A = [
    # pocas hojas grandes y anchas
    dict(n_hojas=(12, 15), largo_hoja=(0.30, 0.5), proporcion=(0.40, 0.48), angulo_hoja=(35, 95),
         largo_eje=(0.45, 0.58), laterales=(2, 3), angulo_lateral=(40, 70), largo_lateral=(0.28, 0.4),
         paso_nodos=0.06, n_venas=(9, 12), angulo_vena=(48, 60), punta=(0.09, 0.13), pos_max=0.42,
         cobertura_obj=0.6),
    # numero medio
    dict(n_hojas=(17, 20), largo_hoja=(0.26, 0.42), proporcion=(0.36, 0.44), angulo_hoja=(30, 100),
         largo_eje=(0.5, 0.62), laterales=(2, 4), angulo_lateral=(35, 70), largo_lateral=(0.28, 0.4),
         paso_nodos=0.05, n_venas=(9, 12), angulo_vena=(45, 58), punta=(0.10, 0.15), pos_max=0.46,
         cobertura_obj=0.62),
    # muchas hojas pequenas
    dict(n_hojas=(22, 25), largo_hoja=(0.2, 0.34), proporcion=(0.40, 0.50), angulo_hoja=(30, 105),
         largo_eje=(0.52, 0.64), laterales=(3, 4), angulo_lateral=(40, 72), largo_lateral=(0.3, 0.42),
         paso_nodos=0.04, n_venas=(8, 11), angulo_vena=(45, 58), punta=(0.08, 0.12), pos_max=0.5,
         cobertura_obj=0.64),
    # hojas mas erguidas y alargadas (lanceoladas)
    dict(n_hojas=(15, 18), largo_hoja=(0.28, 0.48), proporcion=(0.30, 0.37), angulo_hoja=(18, 60),
         largo_eje=(0.5, 0.62), laterales=(2, 3), angulo_lateral=(30, 55), largo_lateral=(0.28, 0.38),
         paso_nodos=0.05, n_venas=(11, 15), angulo_vena=(50, 62), punta=(0.12, 0.17), pos_max=0.4,
         cobertura_obj=0.58),
]
COMUN_DOSEL_A = dict(radio_eje=(6.0, 2.6), peciolo=(0.03, 0.055), radio_peciolo=2.6, intentos=22,
                     lateral_desde=0.15, lateral_hasta=0.7,
                     nodo_desde=0.2, abrir_base=50.0,
                     reduccion_punta=0.3, umbral=0.12, margen=0.02, frac_debajo_rama=0.3, alcance=(0.72, 1.0))


def atlas_dosel_a(res, rng, banco):
    from comun import Lienzo
    lienzo = Lienzo(res, res)
    lado = res // 2
    info = []
    for i, var in enumerate(VARIANTES_DOSEL_A):
        celda = Celda((i % 2) * lado, (i // 2) * lado, lado)
        p = dict(COMUN_DOSEL_A)
        p.update(var)
        tipo = HojaSimple(p, PALETA_DOSEL_A)
        info.append(ramillete(lienzo, celda, np.random.default_rng(rng.integers(1 << 30)), banco, tipo, p, PALETA_RAMA))
    return lienzo, info


# ----------------------------------------------------------------------------------------------
# Utilidades compartidas por los tipos compuestos
# ----------------------------------------------------------------------------------------------
def estilo_foliolo(pal, rng, edad, esc, L, **extra):
    """Estilo de un foliolo/hoja pequena a partir de una paleta."""
    joven = 1 - edad
    base = mezclar(pal["viejo"], pal["joven"], joven ** 1.3 * 0.85)
    base = variar(base, rng, dh=0.012, ds=0.1, dv=0.12)
    e = dict(
        color=base, color_vena=variar(pal["vena"], rng, 0.01, 0.1, 0.1),
        color_nervio=variar(pal["nervio"], rng, 0.01, 0.1, 0.1),
        color_amarillo=pal["amarillo"], color_seco=pal["seco"], color_seco_oscuro=pal["seco_oscuro"],
        color_halo=pal["halo"], color_liquen=pal["liquen"],
        var_valor=rng.uniform(0.06, 0.12), var_tono=rng.uniform(0.02, 0.08),
        ancho_nervio=2.4 * (L / (0.1 * 1024 * esc)) ** 0.5, ancho_vena=1.0,
        n_venas=int(rng.integers(6, 10)), angulo_vena=rng.uniform(45, 60), curva_vena=rng.uniform(0.4, 0.8),
        dist_intramarginal=2.5, escala_terciaria=0.8, contraste_vena=0.28, contraste_nervio=0.6,
        alto_nervio=1.0, surco_nervio=0.25, alto_vena=0.5, abollonado=0.25, alto_terciaria=0.15,
        enrollado_borde=0.8, combado=rng.uniform(0.1, 0.25), plegado=rng.uniform(0.0, 0.15),
        arqueo=rng.uniform(0.0, 0.06), suavidad=0.5 + 0.05 * joven - 0.05 * edad, grosor=0.18,
        incl_t=rng.normal(0, 0.12), incl_s=rng.normal(0, 0.08),
    )
    e.update(extra)
    return e


def _casco(xs, ys):
    from scipy.spatial import ConvexHull
    pts = np.stack([xs, ys], axis=1)
    try:
        h = ConvexHull(pts)
        v = h.vertices
        return pts[v, 0], pts[v, 1]
    except Exception:
        return xs, ys


def _contorno_lamina(x, y, ang, L, W, perfil, k2=0.0, n=16):
    m = H.Marco(x, y, ang, L, k2, 0.0)
    s = np.linspace(0, 1, n)
    xl, yl = m.a_imagen(s, perfil(s) * W)
    xr, yr = m.a_imagen(s[::-1], -perfil(s[::-1]) * W)
    return np.concatenate([xl, xr]), np.concatenate([yl, yr])


# ----------------------------------------------------------------------------------------------
# Hoja compuesta pinnada (leguminosas, lianas): hojas_dosel_b
# ----------------------------------------------------------------------------------------------
class HojaCompuesta:
    def __init__(self, p, paleta):
        self.p = p
        self.paleta = paleta

    def proponer(self, rng, nd, celda):
        p = self.p
        C = celda.ancho
        juventud = nd["frac"] if nd["rama"] == 0 else 0.3 + 0.7 * nd["frac"]
        if nd["terminal"]:
            ang = nd["ang"] + rng.uniform(-0.3, 0.3)
        else:
            a0, a1 = p["angulo_hoja"]
            a1 = a1 + p.get("abrir_base", 40.0) * (1 - suave_np(0.15, 0.55, juventud))
            ang = nd["ang"] + nd["lado"] * np.radians(rng.uniform(a0, a1))
        tam = 1.0 - p.get("reduccion_punta", 0.25) * juventud ** 1.5
        lp = rng.uniform(*p["peciolo"]) * C
        alcance = distancia_al_borde(nd["x"], nd["y"], ang, celda, p.get("margen", 0.02))
        lmin, lmax = p["largo_hoja"]
        Ltot = float(np.clip(rng.uniform(*p.get("alcance", (0.75, 1.0))) * alcance, lmin * C, lmax * C)) * tam
        Lf = rng.uniform(*p["largo_foliolo"]) * C * (0.8 + 0.2 * tam)
        Lr = max(Ltot - lp - Lf * 0.5, 0.08 * C)
        return dict(nx=nd["x"], ny=nd["y"], ang=ang, lp=lp, Lr=Lr, Lf=Lf,
                    prop=rng.uniform(*p["proporcion"]), ang_f=np.radians(rng.uniform(*p["angulo_foliolo"])),
                    curv=rng.uniform(-0.5, 0.5), sep=rng.uniform(*p["separacion"]),
                    impar=rng.random() < p.get("prob_impar", 0.3),
                    edad=float(np.clip(1.0 - juventud + rng.normal(0, 0.15), 0, 1)),
                    semilla=int(rng.integers(1 << 30)), escala_rel=1.0, escala_min=0.45)

    def escalar(self, geo, f):
        g = dict(geo)
        g["Lr"] *= f
        g["Lf"] *= (0.4 + 0.6 * f)
        g["lp"] *= (0.5 + 0.5 * f)
        g["escala_rel"] = geo.get("escala_rel", 1.0) * f
        return g

    def escalar_desde(self, geo, k, ox, oy):
        g = dict(geo)
        g["nx"] = ox + (geo["nx"] - ox) * k
        g["ny"] = oy + (geo["ny"] - oy) * k
        for c in ("Lr", "Lf", "lp"):
            g[c] *= k
        return g

    def _raquis(self, geo):
        pts = H.curva_suave((geo["nx"], geo["ny"]), geo["ang"], geo["lp"] + geo["Lr"], curvatura=geo["curv"], n=24)
        seg = np.sqrt((np.diff(pts, axis=0) ** 2).sum(1))
        return pts, np.concatenate([[0], np.cumsum(seg)])

    def _foliolos(self, geo):
        pts, acum = self._raquis(geo)
        rng = np.random.default_rng(geo["semilla"])
        Lf, prop, af = geo["Lf"], geo["prop"], geo["ang_f"]
        paso = max(Lf * prop * geo["sep"] / max(np.sin(af), 0.3), 3.0)
        a0 = geo["lp"] + paso * 0.4
        a1 = acum[-1] - (paso * 0.9 if geo["impar"] else paso * 0.12)
        npar = max(int(round((a1 - a0) / paso)) + 1, 2)
        pos = np.linspace(a0, a1, npar)
        res = []
        n = len(pos)
        for i, a in enumerate(pos):
            x = (a - geo["lp"]) / max(geo["Lr"], 1.0)
            f = 0.62 + 0.38 * np.sin(np.pi * np.clip(0.12 + 0.85 * x, 0, 1))
            k = min(int(np.searchsorted(acum, a)), len(pts) - 1)
            k0 = max(k - 1, 0)
            tang = np.arctan2(-(pts[k][1] - pts[k0][1]), pts[k][0] - pts[k0][0] + 1e-9)
            px = np.interp(a, acum, pts[:, 0])
            py = np.interp(a, acum, pts[:, 1])
            for lado in (1, -1):
                off = rng.uniform(-0.15, 0.15) * paso  # subopuestos
                pxx = px + off * np.cos(tang)
                pyy = py - off * np.sin(tang)
                Li = Lf * f * rng.uniform(0.9, 1.08)
                res.append(dict(x=pxx, y=pyy, ang=tang + lado * (af + rng.uniform(-0.12, 0.12)), L=Li,
                                W=Li * prop * 0.5, k2=rng.uniform(-0.12, 0.12) - 0.08 * lado,
                                asim=rng.uniform(0.0, 0.15) * lado, semilla=int(rng.integers(1 << 30))))
        if geo["impar"]:
            tang = np.arctan2(-(pts[-1][1] - pts[-2][1]), pts[-1][0] - pts[-2][0])
            Li = Lf * 0.95
            res.append(dict(x=pts[-1][0], y=pts[-1][1], ang=tang + rng.uniform(-0.15, 0.15), L=Li, W=Li * prop * 0.5,
                            k2=rng.uniform(-0.08, 0.08), asim=0.0, semilla=int(rng.integers(1 << 30))))
        return res, pts

    def _perfil(self, semilla):
        r = np.random.default_rng(semilla)
        if self.p.get("forma", "oblonga") == "oblonga":
            return H.perfil_oblongo(r, base_redonda=r.uniform(0.5, 0.9), punta_redonda=r.uniform(0.3, 0.8))
        return H.perfil_eliptico(r, pos_max=0.45, base_redonda=r.uniform(0.3, 0.7), largo_punta=r.uniform(0.08, 0.14),
                                 ancho_punta=0.06, ancho_en_punta=0.32)

    def contorno(self, geo):
        fol, pts = self._foliolos(geo)
        xs, ys = [pts[:, 0]], [pts[:, 1]]
        for f in fol:
            m = H.Marco(f["x"], f["y"], f["ang"], f["L"], f["k2"], 0.0)
            x, y = m.a_imagen(np.array([1.0, 0.5, 0.5]), np.array([0.0, f["W"], -f["W"]]))
            xs.append(x)
            ys.append(y)
        return _casco(np.concatenate(xs), np.concatenate(ys))

    def pintar(self, geo, lienzo, rng, banco, z, esc):
        rng = np.random.default_rng(geo["semilla"] + 3)
        pal = self.paleta
        fol, pts = self._foliolos(geo)
        edad = geo["edad"]
        for f in fol:
            r = np.random.default_rng(f["semilla"])
            P = self._perfil(f["semilla"])
            m = H.Marco(f["x"], f["y"], f["ang"], f["L"], f["k2"], 0.0)
            Wl = f["W"] * (1 + f["asim"])
            Wr = f["W"] * (1 - f["asim"])
            tam = float(np.clip(f["L"] / (0.1 * 1024 * esc), 0.35, 1.3))
            e = estilo_foliolo(pal, r, float(np.clip(edad + r.normal(0, 0.1), 0, 1)), esc, f["L"],
                               abollonado=0.25 * tam, alto_vena=0.5 * tam, alto_terciaria=0.15 * tam,
                               **self.p.get("estilo", {}))
            danos = H.danos_aleatorios(r, wmax=f["W"], esc=esc * 0.6 * tam ** 0.5, edad=edad,
                                       prob=self.p.get("prob_danos", 0.5) * min(tam, 1.0))
            danos = [d for d in danos if d["tipo"] not in ("mordisco", "liquen")]
            capa = H.render_hoja(m, lambda s, P=P, Wl=Wl: P(s) * Wl, lambda s, P=P, Wr=Wr: P(s) * Wr, e, r, banco,
                                 z=z + r.uniform(0, 3), danos=danos, esc=esc)
            lienzo.pintar(capa)
        # raquis encima de los foliolos (con pulvino en la base)
        col = variar(pal["peciolo"], rng, 0.02, 0.12, 0.12)
        rad = self.p.get("radio_raquis", 2.2) * esc
        radios = np.linspace(rad, rad * 0.45, len(pts))
        radios[:2] *= 1.35
        capa, _ = H.render_tubo(pts, radios, banco, rng, col * 0.85, col * 1.05, z=z + 4, suav=0.3, grosor=0.85,
                                estrias=0.3)
        lienzo.pintar(capa)


# ----------------------------------------------------------------------------------------------
# Hoja palmaticompuesta (ceiba): 5-8 foliolos lanceolados que salen de la punta del peciolo
# ----------------------------------------------------------------------------------------------
class HojaPalmada:
    def __init__(self, p, paleta):
        self.p = p
        self.paleta = paleta

    def proponer(self, rng, nd, celda):
        p = self.p
        C = celda.ancho
        juventud = nd["frac"] if nd["rama"] == 0 else 0.3 + 0.7 * nd["frac"]
        if nd["terminal"]:
            ang = nd["ang"] + rng.uniform(-0.3, 0.3)
        else:
            a0, a1 = p["angulo_hoja"]
            a1 = a1 + p.get("abrir_base", 40.0) * (1 - suave_np(0.15, 0.55, juventud))
            ang = nd["ang"] + nd["lado"] * np.radians(rng.uniform(a0, a1))
        tam = 1.0 - p.get("reduccion_punta", 0.2) * juventud ** 1.5
        alcance = distancia_al_borde(nd["x"], nd["y"], ang, celda, p.get("margen", 0.02))
        lp = float(np.clip(rng.uniform(0.3, 0.5) * alcance, p["peciolo"][0] * C, p["peciolo"][1] * C))
        Lf = float(np.clip(rng.uniform(0.75, 1.0) * (alcance - lp), p["largo_foliolo"][0] * C, p["largo_foliolo"][1] * C)) * tam
        Lf = max(Lf, 0.8 * p["largo_foliolo"][0] * C)   # sin manos en miniatura: si no cabe, se descarta
        n = int(rng.integers(p["n_foliolos"][0], p["n_foliolos"][1] + 1))
        return dict(nx=nd["x"], ny=nd["y"], ang=ang, lp=lp, Lf=Lf, n=n,
                    abertura=np.radians(rng.uniform(*p["abertura"])), prop=rng.uniform(*p["proporcion"]),
                    curv=rng.uniform(-0.3, 0.3), giro=rng.uniform(-0.25, 0.25),
                    edad=float(np.clip(1.0 - juventud + rng.normal(0, 0.15), 0, 1)),
                    semilla=int(rng.integers(1 << 30)), escala_rel=1.0, escala_min=0.62)

    def escalar(self, geo, f):
        g = dict(geo)
        g["Lf"] *= f
        g["lp"] *= (0.4 + 0.6 * f)
        g["escala_rel"] = geo.get("escala_rel", 1.0) * f
        return g

    def escalar_desde(self, geo, k, ox, oy):
        g = dict(geo)
        g["nx"] = ox + (geo["nx"] - ox) * k
        g["ny"] = oy + (geo["ny"] - oy) * k
        g["Lf"] *= k
        g["lp"] *= k
        return g

    def _peciolo(self, geo):
        return H.curva_suave((geo["nx"], geo["ny"]), geo["ang"] - geo["curv"] * 0.4, geo["lp"], curvatura=geo["curv"], n=16)

    def _foliolos(self, geo):
        pts = self._peciolo(geo)
        cx, cy = pts[-1]
        tang = np.arctan2(-(pts[-1][1] - pts[-2][1]), pts[-1][0] - pts[-2][0])
        rng = np.random.default_rng(geo["semilla"])
        n = geo["n"]
        res = []
        for i in range(n):
            x = (i / (n - 1) - 0.5) if n > 1 else 0.0
            a = tang + geo["giro"] * 0.3 + geo["abertura"] * x + rng.uniform(-0.08, 0.08)
            Li = geo["Lf"] * (1 - 0.5 * (abs(x) * 2) ** 1.5) * rng.uniform(0.9, 1.05)
            r0 = geo["Lf"] * 0.05
            res.append(dict(x=cx + r0 * np.cos(a), y=cy - r0 * np.sin(a), ang=a, L=Li, W=Li * geo["prop"] * 0.5,
                            k2=rng.uniform(-0.12, 0.12), asim=rng.uniform(-0.08, 0.08),
                            semilla=int(rng.integers(1 << 30)), r0=r0))
        return res, pts

    def _perfil(self, semilla):
        r = np.random.default_rng(semilla)
        return H.perfil_eliptico(r, pos_max=0.5, base_redonda=r.uniform(0.0, 0.25), largo_punta=r.uniform(0.1, 0.16),
                                 ancho_punta=0.05, ancho_en_punta=0.3)

    def contorno(self, geo):
        fol, pts = self._foliolos(geo)
        xs, ys = [pts[:, 0]], [pts[:, 1]]
        for f in fol:
            m = H.Marco(f["x"], f["y"], f["ang"], f["L"], f["k2"], 0.0)
            x, y = m.a_imagen(np.array([1.0, 0.5, 0.5, 0.25, 0.25]),
                              np.array([0.0, f["W"], -f["W"], f["W"] * 0.8, -f["W"] * 0.8]))
            xs.append(x)
            ys.append(y)
        return _casco(np.concatenate(xs), np.concatenate(ys))

    def pintar(self, geo, lienzo, rng, banco, z, esc):
        rng = np.random.default_rng(geo["semilla"] + 3)
        pal = self.paleta
        fol, pts = self._foliolos(geo)
        edad = geo["edad"]
        col = variar(pal["peciolo"], rng, 0.02, 0.12, 0.12)
        rad = self.p.get("radio_peciolo", 2.4) * esc
        radios = np.linspace(rad * 1.2, rad * 0.8, len(pts))
        radios[-1] *= 1.4
        capa, _ = H.render_tubo(pts, radios, banco, rng, col * 0.85, col, z=z - 3, suav=0.3, grosor=0.85, estrias=0.4,
                                nudos=[(1.0, 0.2)])
        lienzo.pintar(capa)
        for f in fol:
            r = np.random.default_rng(f["semilla"])
            # peciolulo corto
            cx, cy = pts[-1]
            capa, _ = H.render_tubo(np.array([[cx, cy], [f["x"], f["y"]]]), np.array([rad * 0.6, rad * 0.5]), banco, r,
                                    col * 0.9, col, z=z - 1, suav=0.3, grosor=0.85)
            lienzo.pintar(capa)
            P = self._perfil(f["semilla"])
            m = H.Marco(f["x"], f["y"], f["ang"], f["L"], f["k2"], 0.0)
            Wl = f["W"] * (1 + f["asim"])
            Wr = f["W"] * (1 - f["asim"])
            e = estilo_foliolo(pal, r, float(np.clip(edad + r.normal(0, 0.1), 0, 1)), esc, f["L"],
                               n_venas=int(r.integers(10, 15)), angulo_vena=r.uniform(50, 62), dist_intramarginal=3.5,
                               ancho_nervio=3.0 * (f["L"] / (0.18 * 1024 * esc)) ** 0.5, ancho_vena=1.2,
                               contraste_vena=0.3, contraste_nervio=0.65, suavidad=0.52 - 0.05 * edad,
                               abollonado=0.35, alto_vena=0.7)
            danos = H.danos_aleatorios(r, wmax=f["W"], esc=esc * 0.8, edad=edad, prob=0.6)
            for d in danos:
                if d["tipo"] == "mordisco":
                    d["w"] = float(P(np.array([d["s"]]))[0]) * (Wl if d["lado"] > 0 else Wr)
            capa = H.render_hoja(m, lambda s, P=P, Wl=Wl: P(s) * Wl, lambda s, P=P, Wr=Wr: P(s) * Wr, e, r, banco,
                                 z=z + r.uniform(0, 6), danos=danos, esc=esc)
            lienzo.pintar(capa)


# ----------------------------------------------------------------------------------------------
# Hoja de bambu: lanceolada, venacion paralela, pseudopeciolo corto
# ----------------------------------------------------------------------------------------------
class HojaBambu(HojaSimple):
    def _perfil(self, geo):
        rng = np.random.default_rng(geo["semilla"])
        S = [0.0, 0.015, 0.05, 0.14, 0.26, 0.45, 0.65, 0.82, 0.93, 1.0]
        b = rng.uniform(0.4, 0.8)
        W = [0.0, 0.25 + 0.25 * b, 0.62 + 0.2 * b, 0.93, 1.0, 0.9, 0.66, 0.38, 0.14, 0.0]
        W = [w * (1 + rng.uniform(-0.04, 0.04)) if 0 < i < len(W) - 1 else w for i, w in enumerate(W)]
        p = H.Perfil(S, W)
        p.modular(1.0 + 0.008 * H.ruido.ruido1d(p.s.size, rng, suavidad=p.s.size / 25.0))
        return p

    def estilo(self, rng, edad, esc, geo):
        e = super().estilo(rng, edad, esc, geo)
        e.update(venacion="paralela", n_long=int(rng.integers(5, 8)), cada_long=int(rng.integers(2, 4)),
                 fuerza_menor=0.45, ancho_vena=1.6, esp_transversal=rng.uniform(4.5, 7.0),
                 ancho_nervio=2.6 * (geo["L"] / (0.3 * 1024 * esc)) ** 0.5, contraste_nervio=0.55,
                 contraste_vena=0.22, contraste_terciaria=0.06, contraste_areola=0.03,
                 combado=rng.uniform(0.05, 0.18), plegado=rng.uniform(0.05, 0.2), abollonado=0.0,
                 alto_vena=0.35, alto_terciaria=0.12, alto_nervio=1.0, surco_nervio=0.4,
                 suavidad=0.46 + 0.04 * (1 - edad), grosor=0.16, var_valor=rng.uniform(0.05, 0.1),
                 alto_terciaria_t=0.06)
        return e


# ----------------------------------------------------------------------------------------------
# Atlas: hojas_dosel_b (compuestas pinnadas)
# ----------------------------------------------------------------------------------------------
PALETA_DOSEL_B = dict(
    viejo=hexl("#30501b"), joven=hexl("#5a7f27"), vena=hexl("#68893a"), nervio=hexl("#8fa452"),
    peciolo=hexl("#5b6b2b"), amarillo=hexl("#a7a23a"), seco=hexl("#7f6847"), seco_oscuro=hexl("#3c2c1b"),
    halo=hexl("#949a3c"), liquen=hexl("#a2aa92"),
)
VARIANTES_DOSEL_B = [
    # tipo Inga/Pterocarpus: pocos pares de foliolos elipticos
    dict(n_hojas=(10, 13), largo_hoja=(0.34, 0.55), largo_foliolo=(0.085, 0.115), proporcion=(0.36, 0.44),
         angulo_foliolo=(50, 68), separacion=(1.05, 1.3), forma="eliptica", prob_impar=0.6, peciolo=(0.03, 0.05),
         angulo_hoja=(30, 95), largo_eje=(0.42, 0.55), laterales=(2, 4), angulo_lateral=(35, 70),
         largo_lateral=(0.25, 0.36), paso_nodos=0.06),
    # tipo tamarindo: muchos foliolos pequenos oblongos (textura fina)
    dict(n_hojas=(9, 12), largo_hoja=(0.3, 0.46), largo_foliolo=(0.045, 0.06), proporcion=(0.32, 0.4),
         angulo_foliolo=(60, 78), separacion=(1.0, 1.15), forma="oblonga", prob_impar=0.0, peciolo=(0.02, 0.035),
         angulo_hoja=(25, 90), largo_eje=(0.45, 0.58), laterales=(2, 4), angulo_lateral=(35, 70),
         largo_lateral=(0.25, 0.35), paso_nodos=0.055),
    # foliolos grandes, imparipinnada (tipo Spondias/caoba)
    dict(n_hojas=(9, 11), largo_hoja=(0.38, 0.56), largo_foliolo=(0.11, 0.15), proporcion=(0.34, 0.42),
         angulo_foliolo=(45, 62), separacion=(1.1, 1.4), forma="eliptica", prob_impar=1.0, peciolo=(0.04, 0.06),
         angulo_hoja=(30, 95), largo_eje=(0.42, 0.52), laterales=(2, 3), angulo_lateral=(35, 70),
         largo_lateral=(0.24, 0.34), paso_nodos=0.07),
    # intermedia (tipo Cedrela/Albizia de foliolo medio)
    dict(n_hojas=(8, 11), largo_hoja=(0.3, 0.48), largo_foliolo=(0.06, 0.08), proporcion=(0.34, 0.42),
         angulo_foliolo=(55, 72), separacion=(1.0, 1.2), forma="oblonga", prob_impar=0.3, peciolo=(0.025, 0.045),
         angulo_hoja=(25, 90), largo_eje=(0.45, 0.56), laterales=(2, 3), angulo_lateral=(35, 65),
         largo_lateral=(0.24, 0.34), paso_nodos=0.06),
]
COMUN_DOSEL_B = dict(radio_eje=(5.0, 2.2), radio_raquis=2.0, intentos=16, nodo_desde=0.2, abrir_base=45.0,
                     lateral_desde=0.15, lateral_hasta=0.7, reduccion_punta=0.25, umbral=0.1, margen=0.02,
                     frac_debajo_rama=0.3, alcance=(0.75, 1.0), cobertura_obj=0.6, prob_danos=0.5)


def atlas_dosel_b(res, rng, banco):
    from comun import Lienzo
    lienzo = Lienzo(res, res)
    lado = res // 2
    info = []
    for i, var in enumerate(VARIANTES_DOSEL_B):
        celda = Celda((i % 2) * lado, (i // 2) * lado, lado)
        p = dict(COMUN_DOSEL_B)
        p.update(var)
        tipo = HojaCompuesta(p, PALETA_DOSEL_B)
        info.append(ramillete(lienzo, celda, np.random.default_rng(rng.integers(1 << 30)), banco, tipo, p, PALETA_RAMA))
    lienzo.calibrar(0.92)
    return lienzo, info


# ----------------------------------------------------------------------------------------------
# Atlas: hojas_ceiba (palmaticompuestas)
# ----------------------------------------------------------------------------------------------
PALETA_CEIBA = dict(
    viejo=hexl("#2f4d1c"), joven=hexl("#557a28"), vena=hexl("#62843a"), nervio=hexl("#8ea052"),
    peciolo=hexl("#6a7432"), amarillo=hexl("#a8a33b"), seco=hexl("#80694a"), seco_oscuro=hexl("#3c2c1b"),
    halo=hexl("#969c3e"), liquen=hexl("#a0a890"),
)
VARIANTES_CEIBA = [
    dict(n_hojas=(6, 8), n_foliolos=(5, 7), largo_foliolo=(0.17, 0.26), proporcion=(0.22, 0.28), abertura=(200, 270),
         peciolo=(0.1, 0.18), angulo_hoja=(30, 80), largo_eje=(0.35, 0.45), laterales=(1, 2), angulo_lateral=(35, 60),
         largo_lateral=(0.15, 0.25), paso_nodos=0.07),
    dict(n_hojas=(9, 12), n_foliolos=(6, 8), largo_foliolo=(0.13, 0.2), proporcion=(0.2, 0.26), abertura=(230, 300),
         peciolo=(0.08, 0.15), angulo_hoja=(25, 90), largo_eje=(0.4, 0.5), laterales=(2, 3), angulo_lateral=(35, 65),
         largo_lateral=(0.18, 0.28), paso_nodos=0.055),
    dict(n_hojas=(5, 7), n_foliolos=(7, 8), largo_foliolo=(0.2, 0.3), proporcion=(0.2, 0.25), abertura=(220, 290),
         peciolo=(0.12, 0.2), angulo_hoja=(30, 75), largo_eje=(0.3, 0.42), laterales=(1, 2), angulo_lateral=(40, 60),
         largo_lateral=(0.15, 0.22), paso_nodos=0.08),
    dict(n_hojas=(7, 10), n_foliolos=(5, 7), largo_foliolo=(0.15, 0.22), proporcion=(0.24, 0.3), abertura=(180, 250),
         peciolo=(0.1, 0.17), angulo_hoja=(25, 85), largo_eje=(0.38, 0.5), laterales=(2, 3), angulo_lateral=(35, 65),
         largo_lateral=(0.18, 0.28), paso_nodos=0.06),
]
COMUN_CEIBA = dict(radio_eje=(5.5, 2.6), radio_peciolo=2.4, intentos=20, nodo_desde=0.25, abrir_base=50.0,
                   lateral_desde=0.2, lateral_hasta=0.7, reduccion_punta=0.2, umbral=0.1, margen=0.02,
                   frac_debajo_rama=0.25, cobertura_obj=0.6)
PALETA_RAMA_CEIBA = dict(lenoso=hexl("#5d6440"), verde=hexl("#68733a"))


def atlas_ceiba(res, rng, banco):
    from comun import Lienzo
    lienzo = Lienzo(res, res)
    lado = res // 2
    info = []
    for i, var in enumerate(VARIANTES_CEIBA):
        celda = Celda((i % 2) * lado, (i // 2) * lado, lado)
        p = dict(COMUN_CEIBA)
        p.update(var)
        tipo = HojaPalmada(p, PALETA_CEIBA)
        info.append(ramillete(lienzo, celda, np.random.default_rng(rng.integers(1 << 30)), banco, tipo, p,
                              PALETA_RAMA_CEIBA, dict(lenticelas=0.1, estrias=0.5)))
    lienzo.calibrar(0.84)
    return lienzo, info


# ----------------------------------------------------------------------------------------------
# Atlas: hojas_bambu
# ----------------------------------------------------------------------------------------------
PALETA_BAMBU = dict(
    viejo=hexl("#3c5a1e"), joven=hexl("#6a8a2c"), vena=hexl("#6e8c38"), nervio=hexl("#9aac5c"),
    peciolo=hexl("#7a8a3a"), amarillo=hexl("#aea445"), seco=hexl("#9a8358"), seco_oscuro=hexl("#54402a"),
    halo=hexl("#a3a347"), liquen=hexl("#a6ad96"),
)
PALETA_RAMA_BAMBU = dict(lenoso=hexl("#6f7a3a"), verde=hexl("#7d8a40"))
VARIANTES_BAMBU = [
    dict(n_hojas=(18, 22), largo_hoja=(0.24, 0.4), proporcion=(0.12, 0.16), angulo_hoja=(15, 55),
         largo_eje=(0.55, 0.7), laterales=(2, 3), angulo_lateral=(30, 55), largo_lateral=(0.25, 0.38), paso_nodos=0.045),
    dict(n_hojas=(24, 30), largo_hoja=(0.2, 0.32), proporcion=(0.13, 0.17), angulo_hoja=(15, 60),
         largo_eje=(0.55, 0.7), laterales=(3, 4), angulo_lateral=(30, 60), largo_lateral=(0.25, 0.38), paso_nodos=0.04),
    dict(n_hojas=(14, 18), largo_hoja=(0.3, 0.46), proporcion=(0.11, 0.14), angulo_hoja=(12, 45),
         largo_eje=(0.5, 0.65), laterales=(2, 3), angulo_lateral=(25, 50), largo_lateral=(0.25, 0.35), paso_nodos=0.05),
    dict(n_hojas=(20, 26), largo_hoja=(0.22, 0.36), proporcion=(0.12, 0.16), angulo_hoja=(15, 65),
         largo_eje=(0.55, 0.7), laterales=(2, 4), angulo_lateral=(30, 60), largo_lateral=(0.22, 0.36), paso_nodos=0.042),
]
COMUN_BAMBU = dict(radio_eje=(4.2, 2.0), peciolo=(0.008, 0.018), radio_peciolo=1.6, intentos=16, nodo_desde=0.3,
                   abrir_base=35.0, lateral_desde=0.2, lateral_hasta=0.75, reduccion_punta=0.25, umbral=0.08,
                   margen=0.02, frac_debajo_rama=0.25, alcance=(0.7, 1.0), cobertura_obj=0.5, n_venas=(5, 6),
                   angulo_vena=(10, 12), punta=(0.2, 0.3), suavidad=0.48, prob_danos=1.3, escala_danos=0.8,
                   liquen=0.0)


def atlas_bambu(res, rng, banco):
    from comun import Lienzo
    lienzo = Lienzo(res, res)
    lado = res // 2
    info = []
    for i, var in enumerate(VARIANTES_BAMBU):
        celda = Celda((i % 2) * lado, (i // 2) * lado, lado)
        p = dict(COMUN_BAMBU)
        p.update(var)
        tipo = HojaBambu(p, PALETA_BAMBU)
        info.append(ramillete(lienzo, celda, np.random.default_rng(rng.integers(1 << 30)), banco, tipo, p,
                              PALETA_RAMA_BAMBU, dict(lenticelas=0.0, estrias=0.5, paso_nudos=0.09, fuerza_nudos=0.35,
                                                      suav=0.45)))
    lienzo.calibrar(0.76)
    return lienzo, info
