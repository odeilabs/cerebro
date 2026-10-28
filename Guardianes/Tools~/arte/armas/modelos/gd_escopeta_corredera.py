"""gd_escopeta_corredera: escopeta de corredera del calibre 12 (de caza, con culata y guardamanos de nogal, tipo
Remington 870 Wingmaster), la de los garimpeiros, los cazadores y los incendiarios de Basiliskos (y la de cualquier
juego de la saga que necesite una escopeta de caza). Lamina 026-escopeta-corredera.

Medidas reales (870 Wingmaster con canon de 26"): 1.181 mm de largo (46,5"), canon de 660 mm desde la cara del
cerrojo, del que asoman 636,5 por delante del cajon, longitud de culata (del gatillo al centro de la cantonera) de 14"
(355 mm), cajon de 219 mm, 49 mm de alto y 38 de ancho donde casa la culata (1 15/16" x 1 1/2"), 218 mm de alto de la
punta de la mira a la punta de la cantonera. La lamina NO esta estirada (a la escala del largo, el alto de la culata en
el cajon da 50 mm; una sola escala, 0,5584 mm/px), pero REPARTE MAL EL LARGO: su canon mide 621 mm desde el cerrojo
(24,5") y su culata unos 39 mm de mas (393 mm de longitud de culata). El largo total cuadra porque se compensan. El
modelo lleva el reparto de la 870 real: el canon se alarga por delante del deposito (la anilla, el tapon y el
guardamanos se quedan donde estan respecto al cajon, como en una 870 de 26") y la culata se acorta en el tramo recto
entre la cantonera y la nariz de la quijera (ver PX_CULATA). Por eso la IoU contra la lamina tal cual baja: la
comparacion de este fichero deja ademas <n>_comparar_proporciones.png, con la lamina llevada a esas proporciones,
que es la que mide la forma (IoU 0,956; con la lamina tal cual, 0,850). Lo que se aparta de la lamina a proposito,
ademas: el canon (24,6 mm en la lamina; aqui 24,4 en la recamara y 23,2 en la boca) y el tubo del deposito (28 mm,
con el tapon de 32,4 y la anilla de 31).

La culata y la cantonera son un solido "hinchado" (_hinchar): el contorno lateral de la lamina, triangulado por
dentro, con un grueso que sube del canto (0) al centro con un redondeo de 4-12 mm y un abombado de 2 mm en los
flancos; los cantos de la quijera, del puno y de la cantonera salen redondos, y el picado es un panel hundido 1 mm en
esa misma superficie, con un nogal mas oscuro y mate (madera_nogal_picado, como en la yuxtapuesta y el Remington).
Se parte en madera y goma por la junta de la cantonera, asi que las dos casan a ras.

El pavonado va con la receta `pintura_coche` del texturizador (laca lisa sobre un negro con escamas de metal finas):
es la unica de las suyas que da un negro liso y brillante. `pavonado`/`acero` van a `metal`, que no baja de un gris
claro (el tinte de la receta se queda en la mitad); `pintura`, a un mate; `plastico` pone un punteado de celdas que de
cerca parece pintura martilleada; `film_metalizado`, el otro conductor, lleva arrugas. Es dielectrico con capa de laca
(F0 0,04, `coat` 1), y asi lo declara el modelo (metal 0).

La lamina lateral tiene la boca a la DERECHA (ensena el lado derecho del tirador, +Z, el de la ventana de expulsion):
la calibracion va con boca_izquierda=False y la ventana se abre en +Z. El paso `comparar` del kit compara la lamina tal
cual con el modelo visto desde -Z (boca a la izquierda); para que la comparacion valga, este fichero tambien se lanza
solo y voltea la lamina en ese paso (ver el final):
    python modelos/gd_escopeta_corredera.py [--solo modelo,comparar] [--revision <carpeta>]   (los mismos argumentos
    que construir_arma.py, sin el nombre)

Las coordenadas de la lamina se leen con
    python laminas.py rejilla 026-escopeta-corredera/lateral rejilla.png --paso 10 --recorte 600,170,1120,420 --zoom 2
    --umbral 50   (el umbral de serie, 22, se lleva la sombra de debajo de la culata)
La camara de tres cuartos (azimut 129, elevacion 19, focal 60) es la que mejor casa la silueta de la lamina de tres
cuartos entre unas 180 probadas (desde la derecha y por delante: la lamina ensena el lado de la ventana).
"""
import math

FICHA = {
    "nombre": "gd_escopeta_corredera",
    "clase": "Escopeta",
    "lamina": "026-escopeta-corredera",
    "para": "garimpeiros, cazadores e incendiarios de Basiliskos; cualquier escopeta de caza de corredera de la saga",
    "medidas_reales": {"largo": 1.181, "canon": 0.660, "canon_visible": 0.6365, "longitud_culata": 0.355,
                       "alto": 0.218, "cajon": 0.219, "ancho_cajon": 0.038, "alto_culata_en_cajon": 0.049,
                       "ancho_guardamanos": 0.046, "ancho_cantonera": 0.043, "calibre_anima": 0.0185},
    "texturas": {"res": 1024, "escala": 0.4},   # 0,4 m por tesela: la veta del nogal fina
    "vista_34": {"azimut": 129.0, "elevacion": 19.0, "focal": 60.0},
    "semilla": 26,
    "umbral": 50.0,
}

# Pixeles de la lamina lateral (escopeta-corredera_lateral.png, 2172 x 724, boca a la derecha)
PX_BOCA, PX_COLA, PY_EJE = 2141, 26, 214      # la corona, la cola de la cantonera y el eje del canon
PX_CERROJO = 1028                             # la cara del cerrojo (la recamara): de aqui se mide el canon
PX_CULATA = (90.0, 460.0)                     # el tramo recto de la culata que se acorta (de la junta de la cantonera
                                              # a la nariz de la quijera): lo de detras se adelanta entero
PX_CANON = (1960.0, 2080.0)                   # el tramo liso del canon que se alarga en la lamina de la comparacion
                                              # (del tapon a la base de la mira; el modelo alarga el canon entero)
# El grueso de la culata y la cantonera (semiancho, mm) y el radio del canto (mm), por pixel de la lamina: sale de
# 43 mm en la cantonera y el talon, 42 en la culata, 40 en la quijera, 37 en el puno y junto al cajon (que mide 38);
# el canto de la goma, cerrado (4 mm), el de la madera, 12, y junto al cajon 6 (lo que el chaflan del cajon).
SEMIANCHOS = [(0, 23.0), (60, 22.8), (110, 21.8), (300, 21.0), (440, 20.2), (520, 18.6), (600, 18.2), (650, 18.8),
              (700, 19.9)]
RADIOS = [(0, 4.0), (45, 4.0), (110, 9.0), (200, 12.0), (620, 12.0), (690, 6.0)]


def _interpolar(tabla, v):
    """Interpolacion lineal en una tabla [(clave, valor), ...] ordenada por clave (sujeta a los extremos)."""
    if v <= tabla[0][0]:
        return tabla[0][1]
    for (a, fa), (b, fb) in zip(tabla[:-1], tabla[1:]):
        if v <= b:
            return fa + (fb - fa) * (v - a) / (b - a)
    return tabla[-1][1]


def _suave(tabla, v):
    """Como _interpolar pero con tangentes continuas (Hermite con las pendientes de los vecinos): una tabla de
    gruesos interpolada a trozos rectos deja un pliegue en cada nudo."""
    xs, ys = [a for a, _ in tabla], [b for _, b in tabla]
    if v <= xs[0]:
        return ys[0]
    if v >= xs[-1]:
        return ys[-1]
    i = max(j for j in range(len(xs) - 1) if xs[j] <= v)
    h = xs[i + 1] - xs[i]
    t = (v - xs[i]) / h

    def pendiente(j):
        if j == 0 or j == len(xs) - 1:
            return 0.0
        return (ys[j + 1] - ys[j - 1]) / (xs[j + 1] - xs[j - 1])
    m0, m1 = pendiente(i) * h, pendiente(i + 1) * h
    t2, t3 = t * t, t * t * t
    return (2 * t3 - 3 * t2 + 1) * ys[i] + (t3 - 2 * t2 + t) * m0 + (3 * t2 - 2 * t3) * ys[i + 1] + (t3 - t2) * m1


def _sujetar(v, a=0.0, b=1.0):
    return max(a, min(b, v))


def _tramos(v, tramos):
    """Un desplazamiento a trozos: [(v0, v1, d0, d1), ...]; dentro de cada tramo, d va de d0 a d1 en linea recta
    (fuera de todos, el del extremo mas cercano)."""
    for v0, v1, d0, d1 in tramos:
        if v0 <= v <= v1:
            return d0 + (d1 - d0) * (v - v0) / (v1 - v0)
    if v < tramos[0][0]:
        return tramos[0][2]
    return tramos[-1][3]


def _loft_x(k, estaciones, material, nombre):
    """Lo que al kit le falta: una pieza de secciones que cambian a lo largo del canon (un 'loft'). `estaciones` es
    [(x, [(z, y), ...]), ...], todas las secciones con el mismo numero de puntos y en el mismo orden; las dos de los
    extremos se tapan. Sirve para un cajon cuyo lomo baja hacia la culata sin perder los chaflanes."""
    import bmesh
    bm = bmesh.new()
    anillos = [[bm.verts.new((x, y, z)) for z, y in seccion] for x, seccion in estaciones]
    n = len(anillos[0])
    for A, B in zip(anillos[:-1], anillos[1:]):
        for i in range(n):
            j = (i + 1) % n
            bm.faces.new((A[i], A[j], B[j], B[i]))
    bm.faces.new(anillos[0])
    bm.faces.new(list(reversed(anillos[-1])))
    return k._objeto(bm, nombre, material)


def _encoger(poligono, d):
    """Encoge un poligono tangencial (triangulo o rombo) una distancia d desde cada lado (respecto a su incentro)."""
    n = len(poligono)
    lados = [math.dist(poligono[i], poligono[(i + 1) % n]) for i in range(n)]
    area = 0.5 * abs(sum(poligono[i][0] * poligono[(i + 1) % n][1] - poligono[(i + 1) % n][0] * poligono[i][1]
                         for i in range(n)))
    r = 2.0 * area / sum(lados)
    if n == 3:          # incentro: los vertices pesados por el lado opuesto
        pesos = [lados[1], lados[2], lados[0]]
        cx = sum(p[0] * w for p, w in zip(poligono, pesos)) / sum(pesos)
        cy = sum(p[1] * w for p, w in zip(poligono, pesos)) / sum(pesos)
    else:
        cx = sum(p[0] for p in poligono) / n
        cy = sum(p[1] for p in poligono) / n
    f = max(0.05, (r - d) / r)
    return [(cx + (x - cx) * f, cy + (y - cy) * f) for x, y in poligono]


# ------------------------------------------------------------------ el solido hinchado (culata y cantonera)
def _area(p):
    return 0.5 * sum(p[i][0] * p[(i + 1) % len(p)][1] - p[(i + 1) % len(p)][0] * p[i][1] for i in range(len(p)))


def _remuestrear(poligono, paso, fijos=None):
    """Un poligono cerrado con un punto cada `paso` como mucho (los suyos se conservan) y sin puntos a menos de un
    cuarto de paso. `fijos` (una lista de bool, uno por punto) se arrastra a los puntos nuevos de cada lado."""
    salida, marcas = [], []
    n = len(poligono)
    for i in range(n):
        a, b = poligono[i], poligono[(i + 1) % n]
        m = max(1, int(math.ceil(math.dist(a, b) / paso)))
        for j in range(m):
            t = j / m
            salida.append((a[0] + (b[0] - a[0]) * t, a[1] + (b[1] - a[1]) * t))
            marcas.append(bool(fijos[i] and (j == 0 or fijos[(i + 1) % n])) if fijos else False)
    limpio, limpias = [salida[0]], [marcas[0]]
    for p, f in zip(salida[1:], marcas[1:]):
        if math.dist(p, limpio[-1]) > 0.25 * paso:
            limpio.append(p)
            limpias.append(f)
    if math.dist(limpio[0], limpio[-1]) <= 0.25 * paso:
        limpio.pop()
        limpias.pop()
    return limpio, limpias


def _alisar(poligono, fijos, pasadas=4, lam=0.5, mu=-0.53):
    """Taubin sobre un poligono cerrado (redondea las esquinas del contorno simplificado sin encogerlo); los puntos
    `fijos` no se mueven."""
    p = [list(q) for q in poligono]
    n = len(p)
    for _ in range(pasadas):
        for f in (lam, mu):
            q = [[(p[i - 1][c] + p[(i + 1) % n][c]) * 0.5 for c in (0, 1)] for i in range(n)]
            for i in range(n):
                if not fijos[i]:
                    p[i] = [p[i][c] + f * (q[i][c] - p[i][c]) for c in (0, 1)]
    return [tuple(q) for q in p]


def _simplificar(poligono, tol, largo):
    """Quita los puntos de un poligono cerrado donde es recto: cada tramo que queda no se aparta mas de `tol` de su
    cuerda ni mide mas de `largo` (denso en las curvas, ralo en las rectas). `largo` puede ser una funcion del punto
    (el tramo no pasa del menor de sus dos extremos)."""
    tope = largo if callable(largo) else (lambda p: largo)
    def a_cuerda(p, a, b):
        ab = (b[0] - a[0], b[1] - a[1])
        l2 = ab[0] * ab[0] + ab[1] * ab[1]
        t = _sujetar(((p[0] - a[0]) * ab[0] + (p[1] - a[1]) * ab[1]) / l2) if l2 > 0 else 0.0
        return math.dist(p, (a[0] + ab[0] * t, a[1] + ab[1] * t))
    n = len(poligono)
    P = list(poligono) + [poligono[0]]
    quedan, i = [0], 0
    while i < n:
        mejor, j = i + 1, i + 1
        while j <= n and math.dist(P[i], P[j]) <= min(tope(P[i]), tope(P[j])) and \
                all(a_cuerda(P[m], P[i], P[j]) <= tol for m in range(i + 1, j)):
            mejor, j = j, j + 1
        quedan.append(mejor)
        i = mejor
    return [P[m] for m in quedan[:-1]]


def _dentro(P, poligono):
    """Que puntos (array (m, 2)) caen dentro de un poligono (array (n, 2)) (par-impar)."""
    import numpy as np
    x, y = P[:, :1], P[:, 1:2]
    a, b = poligono, np.roll(poligono, -1, 0)
    ya, yb, xa, xb = a[None, :, 1], b[None, :, 1], a[None, :, 0], b[None, :, 0]
    cruza = ((ya > y) != (yb > y)) & (x < (xb - xa) * (y - ya) / np.where(yb == ya, 1e-30, yb - ya) + xa)
    return cruza.sum(1) % 2 == 1


def _pintar_dentro(ob, poligono, nombre):
    """Pone el material `nombre` a las caras de `ob` cuyo centro cae (en xy) dentro de `poligono` (en metros): un panel
    de otro material sin booleanas (el picado de la culata, que _hinchar hunde en las dos caras con su contorno como
    aristas: ninguna cara lo cruza). Devuelve cuantas caras."""
    import bpy
    import numpy as np
    me = ob.data
    if nombre not in [m.name for m in me.materials if m is not None]:
        me.materials.append(bpy.data.materials[nombre])
    indice = [m.name if m is not None else None for m in me.materials].index(nombre)
    centros = np.array([p.center[:2] for p in me.polygons], dtype=float)
    dentro = _dentro(centros, np.asarray(poligono, dtype=float))
    for p, d in zip(me.polygons, dentro):
        if d:
            p.material_index = indice
    me.update()
    return int(dentro.sum())


def _distancia(P, sa, sb):
    """Distancia de cada punto (m, 2) al segmento mas cercano de los (sa[i], sb[i])."""
    import numpy as np
    ab = sb - sa
    l2 = np.maximum((ab * ab).sum(1), 1e-18)
    salida = np.empty(len(P))
    for i in range(0, len(P), 400):
        ap = P[i:i + 400, None, :] - sa[None]
        t = np.clip((ap * ab[None]).sum(2) / l2[None], 0.0, 1.0)
        d = ap - t[..., None] * ab[None]
        salida[i:i + 400] = np.sqrt((d * d).sum(2)).min(1)
    return salida


def _hinchar(contorno, libres, semiancho, radio, picados=(), malla=0.026, abombado=0.10, alcance=0.030,
             hondo=0.001, pared=0.0007, paso_picado=0.004):
    """Un solido redondo a partir de un contorno lateral (x, y) antihorario: la cara de cada lado es el contorno
    triangulado por dentro (Delaunay con restricciones de Blender), con z = semiancho(x) * perfil(d), d la distancia
    al canto: un cuarto de elipse de radio(x) (el canto redondeado) mas un abombado que sigue subiendo hasta
    `alcance` (los flancos no quedan planos). Los lados del contorno con `libres` en falso no redondean (por ahi se
    corta luego la pieza: una junta). `picados`: poligonos donde la superficie se hunde `hondo` con una pared de
    `pared` (el picado de la empunadura, a los dos lados). Devuelve un bmesh cerrado (las dos caras comparten el
    contorno)."""
    import bmesh
    import numpy as np
    from mathutils import Vector
    from mathutils.geometry import delaunay_2d_cdt

    C = np.array(contorno, float)
    n = len(C)
    A, B = C, np.roll(C, -1, 0)
    libres = np.array(libres, bool)
    SA, SB = A[libres], B[libres]                       # los cantos que redondean
    r_c = np.array([radio(x) for x, _ in C])
    t = B - A
    t /= np.maximum(np.linalg.norm(t, axis=1, keepdims=True), 1e-12)
    normal = np.stack([-t[:, 1], t[:, 0]], 1)           # hacia dentro (antihorario)
    nv = normal + np.roll(normal, 1, 0)
    nv /= np.maximum(np.linalg.norm(nv, axis=1, keepdims=True), 1e-12)

    puntos, celdas = [], {}
    tam = 0.002

    def libre_de(p, minimo):
        cx, cy = int(p[0] // tam), int(p[1] // tam)
        k = int(minimo // tam) + 1
        for i in range(cx - k, cx + k + 1):
            for j in range(cy - k, cy + k + 1):
                for q in celdas.get((i, j), ()):
                    if (q[0] - p[0]) ** 2 + (q[1] - p[1]) ** 2 < minimo * minimo:
                        return False
        return True

    def poner(p):
        puntos.append(p)
        celdas.setdefault((int(p[0] // tam), int(p[1] // tam)), []).append(p)

    for p in C:
        celdas.setdefault((int(p[0] // tam), int(p[1] // tam)), []).append(tuple(p))

    # el picado: su contorno (restriccion), una fila de puntos `pared` hacia dentro de cada lado (sin las esquinas:
    # los dientes de arriba son muy agudos) y unos pocos en el fondo, para que siga la curva de la empunadura
    lazos = []
    for poli in picados:
        poli = poli if _area(poli) > 0 else poli[::-1]
        Q, _ = _remuestrear(poli, paso_picado)
        Q = np.array(Q)
        lazos.append(Q)
        for p in Q:
            celdas.setdefault((int(p[0] // tam), int(p[1] // tam)), []).append(tuple(p))
        L = np.array(poli)
        cand = []
        for a, b in zip(L, np.roll(L, -1, 0)):
            largo_ = float(np.linalg.norm(b - a))
            m = max(1, int(math.ceil(largo_ / paso_picado)))
            nl = np.array([-(b - a)[1], (b - a)[0]]) / max(largo_, 1e-12)
            cand += [a + (b - a) * ((j + 0.5) / m) + nl * pared for j in range(m)]
        cand = np.array(cand)
        ok = _dentro(cand, L) & (_distancia(cand, L, np.roll(L, -1, 0)) > 0.6 * pared)
        for p in cand[ok]:
            if libre_de(p, 0.8 * pared):
                poner(tuple(p))
        x0, y0 = L.min(0)
        x1, y1 = L.max(0)
        fondo = np.array([(x, y) for x in np.arange(x0, x1, 0.011) for y in np.arange(y0, y1, 0.011)])
        ok = _dentro(fondo, L) & (_distancia(fondo, L, np.roll(L, -1, 0)) > 0.003)
        for p in fondo[ok]:
            if libre_de(p, 0.006):
                poner(tuple(p))
    if lazos:
        lados_picado = (np.concatenate(lazos), np.concatenate([np.roll(L, -1, 0) for L in lazos]))

    def lejos_del_picado(P, minimo):
        if not lazos:
            return np.ones(len(P), bool)
        return _distancia(P, *lados_picado) > minimo

    # anillos junto al canto (solo en los cantos que redondean) y una malla al tresbolillo por dentro
    anillos = (0.06, 0.24, 0.60, 1.15)
    canto_libre = libres | np.roll(libres, 1)
    for k_, f in enumerate(anillos):
        cand = C + nv * (r_c * f)[:, None]
        d_todos = _distancia(cand, A, B)
        ok = _dentro(cand, C) & (d_todos > 0.6 * r_c * f) & canto_libre & lejos_del_picado(cand, 0.0012)
        separacion = r_c * max(0.025, 0.5 * (f - (anillos[k_ - 1] if k_ else 0.0)))
        for p, s in zip(cand[ok], separacion[ok]):
            if libre_de(p, s):
                poner(tuple(p))
    x0, y0 = C.min(0)
    x1, y1 = C.max(0)
    rej = []
    fila = 0
    y = y0
    while y < y1:
        x = x0 + (0.5 * malla if fila % 2 else 0.0)
        while x < x1:
            rej.append((x, y))
            x += malla
        y += malla * 0.866
        fila += 1
    rej = np.array(rej)
    ok = _dentro(rej, C) & lejos_del_picado(rej, 0.0015)
    rej = rej[ok]
    d_rej = _distancia(rej, A, B)
    r_rej = np.array([radio(x) for x, _ in rej])
    for p, d, r in zip(rej, d_rej, r_rej):
        if d > anillos[-1] * r + 0.35 * malla and libre_de(p, 0.45 * malla):
            poner(tuple(p))

    # Delaunay con restricciones: el contorno (cara) y los lazos del picado (aristas)
    verts = [Vector((float(p[0]), float(p[1]))) for p in C]
    aristas = [(i, (i + 1) % n) for i in range(n)]
    for L in lazos:
        b = len(verts)
        verts += [Vector((float(p[0]), float(p[1]))) for p in L]
        aristas += [(b + i, b + (i + 1) % len(L)) for i in range(len(L))]
    verts += [Vector((float(p[0]), float(p[1]))) for p in puntos]
    salida = delaunay_2d_cdt(verts, aristas, [list(range(n))], 1, 1e-9, True)
    V = np.array([tuple(v) for v in salida[0]])
    caras = [tuple(c) for c in salida[2]]
    canto = np.zeros(len(V), bool)                       # los vertices del contorno: z = 0, compartidos
    for i, origen in enumerate(salida[3]):
        if any(o < n for o in origen):
            canto[i] = True

    # el grueso: d a los cantos que redondean
    d = _distancia(V, SA, SB)
    r = np.array([radio(x) for x, _ in V])
    w = np.array([semiancho(x) for x, _ in V])
    q = np.clip(d / r, 0.0, 1.0)
    z = w * ((1.0 - abombado) * np.sqrt(1.0 - (1.0 - q) ** 2) + abombado * np.sin(0.5 * math.pi *
                                                                                     np.clip(d / alcance, 0, 1)))
    z[canto] = 0.0
    # un alisado de z lejos del canto: quita el pliegue del abombado sobre el eje medio de las partes estrechas
    vecinos = [set() for _ in range(len(V))]
    for c in caras:
        for i in range(3):
            vecinos[c[i]].update((c[(i + 1) % 3], c[(i + 2) % 3]))
    mover = [(i, [j for j in vecinos[i] if not canto[j]]) for i in range(len(V)) if not canto[i] and d[i] > 0.7 * r[i]]
    for _ in range(4):
        z2 = z.copy()
        for i, js in mover:
            if js:
                z2[i] = 0.5 * z[i] + 0.5 * sum(z[j] for j in js) / len(js)
        z = z2
    # el picado: hundido `hondo`, con la pared de `pared`
    if lazos:
        for L in lazos:
            dentro_ = _dentro(V, L)
            dq = _distancia(V, L, np.roll(L, -1, 0))
            z = np.where(dentro_ & ~canto, z - hondo * np.clip(dq / pared, 0.0, 1.0), z)

    # el bmesh: dos caras (z y -z) que comparten el contorno. Un triangulo con los tres vertices en el contorno
    # (una esquina donde no cupo ningun anillo) se salta en las dos caras: el contorno pasa por su otro lado.
    bm = bmesh.new()
    arriba = [bm.verts.new((V[i, 0], V[i, 1], z[i])) for i in range(len(V))]
    abajo = [arriba[i] if canto[i] else bm.verts.new((V[i, 0], V[i, 1], -z[i])) for i in range(len(V))]
    for a, b, c in caras:
        if canto[a] and canto[b] and canto[c]:
            continue
        for tri in ((arriba[a], arriba[b], arriba[c]), (abajo[c], abajo[b], abajo[a])):
            try:
                bm.faces.new(tri)
            except ValueError:
                pass
    sueltos = [v for v in bm.verts if not v.link_faces]
    bmesh.ops.delete(bm, geom=sueltos, context="VERTS")
    return bm


def _contorno_culata(k):
    """El contorno de la culata y la cantonera en pixeles de la lamina: el tramo del contorno exterior que queda
    detras del cajon (x < 700: del lomo junto al cajon, por la cantonera y el vientre, al puno y al guardamonte), sin
    la anilla portacorrea (que es una pieza aparte), y cerrado por delante pasada la junta con el cajon."""
    ext = k.lamina("lateral").exterior
    n = len(ext)
    dentro = [p[0] < 700 for p in ext]
    inicio = next(i for i in range(n) if dentro[i] and not dentro[i - 1])
    tramo = []
    i = inicio
    while dentro[i % n] and len(tramo) < n:
        tramo.append(ext[i % n])
        i += 1
    tramo = [tuple(p) for p in tramo if not (140 < p[0] < 192 and p[1] > 505)]  # la anilla portacorrea
    # cerrado por fuera de la junta (x = 715): del extremo en que acaba (el lomo o el guardamonte) al otro
    if tramo[-1][1] < tramo[0][1]:
        return tramo + [(715.0, 205.0), (715.0, 322.0)]
    return tramo + [(715.0, 322.0), (715.0, 205.0)]


def modelar(k):
    mm = k.mm
    # Pixeles de escopeta-corredera_lateral.png -> metros. La boca (2141) a la cola de la cantonera (26) = 1.181 mm; la
    # lamina no esta estirada (una sola escala). El cero de y, en el eje del canon (214).
    cal = k.Calibracion(px_boca=PX_BOCA, px_cola=PX_COLA, largo=1.181, py_eje=PY_EJE, boca_izquierda=False)
    X, Y, P = cal.x, cal.y, cal.p

    def px_de(x):
        return cal.px_boca + x / cal.sx          # inversa de cal.x (boca a la derecha)

    def py_de(y):
        return cal.py_eje - y / cal.sy

    # El reparto de la 870 real (ver arriba): el canon, 660 mm desde la cara del cerrojo, y el largo total de 46,5".
    # Lo que crece el canon (39 mm) es lo que se acorta la culata.
    d_boca = FICHA["medidas_reales"]["canon"] - (X(PX_BOCA) - X(PX_CERROJO))
    xb = X(PX_BOCA) + d_boca

    def adelante(x):
        """Lo que se adelanta un punto de la culata (de la lamina a la 870 real), en metros."""
        return _tramos(px_de(x), [(PX_CULATA[0], PX_CULATA[1], d_boca, 0.0)])

    # El pavonado: la receta `pintura_coche` (ver arriba); el acero desnudo, solo en el cerrojo, la corona y el punto
    # de mira.
    pavon = k.material("pintura_coche_pavonado", "#24262c", rugosidad=0.38, metal=0.0)
    nogal = k.material("madera_nogal_culata", "#7a4222", rugosidad=0.5, veta=(1, 0, 0))
    # El picado del puno, en un nogal mas oscuro y mate, como el de la yuxtapuesta y el Remington (revision de
    # conjunto: hundido 1 mm con la misma madera, de cerca se leia como una grieta que cruzaba el puno).
    nogal_picado = k.material("madera_nogal_picado", "#3f2a1e", rugosidad=0.75, veta=(1, 0, 0))
    goma = k.material("goma_cantonera", "#1c1c1e", rugosidad=0.85)
    acero = k.material("acero_cerrojo", "#8a8c90", rugosidad=0.3, metal=1.0)

    # ---------------------------------------------------------------- cajon (receiver)
    # Lados planos, el lomo con dos chaflanes, y el lomo bajando hacia la culata (una seccion por estacion).
    lomo = [(660, 222.5), (672, 218), (680, 215), (690, 212), (700, 209), (720, 203.5), (750, 197), (775, 193),
            (800, 190.5), (850, 189), (900, 188.5), (950, 188), (1000, 188), (1076, 187.5)]
    semi, ch, cv = mm(19.0), mm(5.5), mm(6.0)
    yb = Y(320)
    estaciones = []
    for px in (660, 672, 682, 692, 704, 718, 734, 752, 772, 795, 820, 850, 900, 1076):
        yt = Y(_interpolar(lomo, px))
        estaciones.append((X(px), [(-semi, yb), (semi, yb), (semi, yt - cv), (semi - ch, yt), (-(semi - ch), yt),
                                   (-semi, yt - cv)]))
    cajon = _loft_x(k, estaciones, pavon, "cajon")
    # De lado: la cara de atras inclinada (donde casa la culata), el morro de abajo y la cara de delante.
    k.intersecar(cajon, k.perfil(cal.puntos([(669, 165), (1070, 165), (1070, 314), (1031, 314), (1027, 306),
                                             (692, 306)]), ancho=mm(60)))
    k.biselar(cajon, mm(1.0), 2, 30)
    # Ventana de expulsion (lado derecho, +Z) y la ventana de carga por debajo.
    ventana = k.redondear(cal.puntos([(887, 201), (1030, 201), (1030, 249), (887, 249)]), mm(5.0), 4)
    k.restar(cajon, k.perfil(ventana, desde=mm(3.0), hasta=mm(25)),
             k.caja((X(872), Y(312), -mm(14)), (X(1020), Y(306) + mm(7), mm(14))))
    # El cerrojo, de acero, visto por la ventana (con la ranura del extractor delante); su cara, en PX_CERROJO.
    cerrojo = k.cilindro((X(880), Y(225), 0), (X(PX_CERROJO), Y(225), 0), mm(10.5), 24, material=acero,
                         nombre="cerrojo")
    k.restar(cerrojo, k.caja((X(1008), Y(229.5), mm(5)), (X(1031), Y(221.5), mm(12))))
    # Pasadores del cajon (atraviesan; asoman medio milimetro).
    for px, py, r in ((742.5, 235, 2.4), (729, 276.5, 2.8), (817.5, 277.5, 2.8)):
        k.cilindro((X(px), Y(py), -mm(19.5)), (X(px), Y(py), mm(19.5)), mm(r), 10, material=pavon, nombre="pasador")

    # ---------------------------------------------------------------- placa del disparador, guardamonte, gatillo
    k.perfil(cal.puntos([(690, 299), (866, 299), (869, 304), (866, 311), (692, 311)]), ancho=mm(24), material=pavon,
             bisel=mm(0.8), nombre="placa_disparador")
    hueco = [(715, 304), (797, 304), (802, 315), (805.6, 322), (807, 332), (806, 346), (803, 358), (794, 365),
             (776, 366.4), (740, 367), (726, 364), (718, 356), (714, 344), (713, 330), (715, 316)]
    guardamonte = k.silueta(cal, region=[(694, 308), (880, 308), (880, 390), (694, 390)], ancho=mm(9.0),
                            material=pavon, nombre="guardamonte")
    k.restar(guardamonte, k.perfil(cal.puntos(hueco), ancho=mm(30)))
    k.biselar(guardamonte, mm(1.6), 2, 30)
    k.perfil(cal.puntos([(734, 305), (752, 305), (752, 313), (746, 316), (740, 322), (735, 330), (733.6, 340),
                         (735, 350), (740, 359), (738, 361), (731, 356), (727, 348), (725.6, 336), (727, 324),
                         (734, 314)]), ancho=mm(5.5), material=pavon, bisel=mm(0.6), nombre="gatillo")
    k.cilindro((X(709), Y(317), -mm(6.5)), (X(709), Y(317), mm(6.5)), mm(3.0), 12, material=pavon, nombre="seguro")

    # ---------------------------------------------------------------- canon (26": la boca, d_boca por delante)
    k.torno([(X(1040), 0), (X(1040), mm(12.2)), (X(1068), mm(12.2)), (X(1069), mm(12.8)), (X(1070.5), mm(13.6)),
             (X(1091), mm(13.6)), (X(1094), mm(12.2)), (xb - mm(1.2), mm(11.6)), (xb - mm(0.6), mm(11.6)),
             (xb - mm(0.6), mm(9.25)), (xb - mm(90), mm(9.25)), (xb - mm(90), 0)],
            eje="x", segmentos=32, material=pavon, nombre="canon")
    # La corona de la boca, de acero (el anillo claro de la lamina de detalle).
    k.torno([(xb - mm(0.6), mm(9.25)), (xb - mm(0.6), mm(11.6)), (xb, mm(11.1)), (xb, mm(9.25))], eje="x",
            segmentos=32, material=acero, cerrado=True, nombre="corona")
    # Mira: la base (rampa) y el punto, junto a la boca (se adelantan con ella).
    base_mira = [(x + d_boca, y) for x, y in cal.puntos([(2092, 196), (2092, 191), (2097, 185.5), (2102, 184),
                                                         (2121, 184), (2126, 187), (2127, 196)])]
    k.perfil(base_mira, ancho=mm(3.6), material=pavon, bisel=mm(0.5), nombre="base_mira")
    punto = k.esfera(P(2109.5, 181.5, 0.0), mm(1.75), 12, 6, material=acero, nombre="punto_mira")
    k.mover(punto, (d_boca, 0.0, 0.0))
    # La barra de debajo del canon, del guardamanos a la anilla.
    k.perfil(k.redondear(cal.puntos([(1636, 228), (1948, 228), (1948, 238.5), (1636, 238.5)]),
                         [0, mm(2.5), mm(2.5), 0], 3), ancho=mm(6.0), material=pavon, bisel=mm(0.6), nombre="barra")

    # ---------------------------------------------------------------- deposito (tubo), anilla y tapon
    yd = Y(276)                                        # eje del tubo del deposito
    k.torno([(X(1040), 0), (X(1040), mm(14)), (X(1914), mm(14)), (X(1914), 0)], eje="x", origen=(0, yd, 0),
            segmentos=28, material=pavon, nombre="deposito")
    tapon = k.torno([(X(1910), 0), (X(1910), mm(15.0)), (X(1912), mm(15.6)), (X(1920), mm(15.6)),
                     (X(1921.5), mm(16.2)), (X(1953), mm(16.2)), (X(1956.3), mm(15.3)), (X(1957), mm(14.0)),
                     (X(1957), 0)], eje="x", origen=(0, yd, 0), segmentos=40, material=pavon, nombre="tapon")
    # Moleteado: costillas a lo largo (una de cada dos aristas sale 0,7 mm y la otra entra: 48 grados entre caras, mas
    # que el angulo de suavizado de 40, asi que las costillas quedan con arista; con 0,35 se suavizaban y no se veian).
    x0m, x1m = X(1921.5) - mm(0.1), X(1953) + mm(0.1)        # los dos anillos de la banda moleteada

    def moleteado(x, y, z):
        r = math.hypot(y - yd, z)
        if x0m <= x <= x1m and r > mm(15.9):
            i = int(round(math.atan2(z, y - yd) / (2 * math.pi / 40)))
            r2 = r + (mm(0.7) if i % 2 == 0 else -mm(0.7))
            return x, yd + (y - yd) * r2 / r, z * r2 / r
        return x, y, z
    k.deformar(tapon, moleteado)
    # Anilla del canon: abrazadera al tubo, con su taco hasta el canon, el tornillo pasante y el de sujecion.
    k.torno([(X(1841), mm(14)), (X(1841), mm(15.5)), (X(1874), mm(15.5)), (X(1874), mm(14))], eje="x",
            origen=(0, yd, 0), segmentos=28, material=pavon, nombre="anilla")
    k.caja((X(1839), -mm(22), -mm(6.5)), (X(1871), Y(229), mm(6.5)), material=pavon, bisel=mm(0.8), nombre="taco")
    k.cilindro((X(1826), Y(243.5), 0), (X(1882), Y(243.5), 0), mm(3.2), 12, material=pavon, nombre="tornillo_anilla")
    k.cilindro((X(1856), Y(237), mm(6.0)), (X(1856), Y(237), mm(7.2)), mm(2.3), 12, material=pavon,
               nombre="tornillo_taco")
    # Entre el cajon y el guardamanos: el puente entre el canon y el tubo (la sombra oscura de la lamina entre los dos),
    # el tubo de la corredera, que abraza al deposito (la lamina lo ensena mas grueso que el deposito de delante: baja
    # hasta 307), y las dos barras de accion a los lados.
    k.caja((X(1068), Y(250), -mm(3)), (X(1212), Y(233), mm(3)), material=pavon, nombre="puente")
    k.torno([(X(1066), mm(14)), (X(1066), mm(16.0)), (X(1214), mm(16.0)), (X(1214), mm(14))], eje="x",
            origen=(0, yd, 0), segmentos=28, material=pavon, nombre="tubo_corredera")
    for s in (1, -1):
        k.caja((X(1062), Y(262.5), s * mm(9.5)), (X(1215), Y(245), s * mm(12.5)), material=pavon, bisel=mm(0.5),
               nombre="barra_accion")

    # ---------------------------------------------------------------- guardamanos (nogal, con 18 estrias)
    gx0, gx1 = 1209, 1643
    g_arriba, g_abajo = Y(236.5), Y(331)
    g_centro = 0.5 * (g_arriba + g_abajo)

    def seccion_guardamanos(d):
        return k.redondear([(-mm(23) + d, g_abajo + d), (mm(23) - d, g_abajo + d), (mm(23) - d, g_arriba - d),
                            (-mm(23) + d, g_arriba - d)],
                           [mm(21) - d, mm(21) - d, mm(13) - d, mm(13) - d], 4)

    def subida(x, y, z):
        # el vientre del guardamanos sube 9 px (5 mm) de atras adelante (331 -> 322)
        t = _sujetar((px_de(x) - gx0) / (gx1 - gx0)) * cal.dy(9)
        s = _sujetar((g_centro - y) / (g_centro - g_abajo))
        return x, y + t * s, z
    guardamanos = k.perfil(seccion_guardamanos(0.0), plano="zy", desde=X(gx0), hasta=X(gx1), material=nogal,
                           nombre="guardamanos")
    k.biselar(guardamanos, mm(4.5), 3, 30)
    k.deformar(guardamanos, subida)
    estrias = []
    for i in range(18):
        xg = X(1310) + i * cal.dx(13.18)
        e = k.caja((xg - mm(1.1), -mm(90), -mm(30)), (xg + mm(1.1), Y(255), mm(30)), material=nogal)
        dentro = k.perfil(seccion_guardamanos(mm(2.4)), plano="zy", desde=xg - mm(3), hasta=xg + mm(3))
        k.deformar(dentro, subida)
        k.restar(e, dentro)
        estrias.append(e)
    k.restar(guardamanos, k.unir(*estrias))

    # ---------------------------------------------------------------- culata (nogal) y cantonera (goma)
    # Un solo solido hinchado desde el contorno de la lamina (ver _hinchar), partido por la junta de la cantonera
    # (madera y goma casan a ras) y recortado por la junta con el cajon (las dos caras de las juntas quedan vivas).
    junta_cola = [(65.8, 240), (86.5, 600)]           # (69, 296) - (84, 556)
    junta_cajon = [(668.2, 160), (695.9, 330)]        # (669, 165) - (692, 306)
    ja, jb = cal.puntos(junta_cajon)
    lado_ref = (jb[0] - ja[0]) * (Y(350) - ja[1]) - (jb[1] - ja[1]) * (X(400) - ja[0])

    def de_la_culata(x, y):
        """Del lado de la culata de la junta con el cajon (lo de mas alla se corta: el contorno de alli no se alisa)."""
        return ((jb[0] - ja[0]) * (y - ja[1]) - (jb[1] - ja[1]) * (x - ja[0])) * lado_ref > 0

    bruto = cal.puntos(_contorno_culata(k))
    if _area(bruto) < 0:
        bruto = bruto[::-1]
    fijos = [not de_la_culata(*p) for p in bruto]
    contorno, fijos = _remuestrear(bruto, mm(3.5), fijos)
    # ralo en las rectas de la culata (20 mm), denso en el lomo del puno (7 mm: alli llega el picado) y medio en el
    # resto del puno (10 mm)
    contorno = _simplificar(_alisar(contorno, fijos, 6), mm(0.2),
                            lambda p: mm(_interpolar([(430, 20.0), (470, 7.0 if py_de(p[1]) < 300 else 10.0)],
                                                     px_de(p[0]))))
    # Redondean todos los cantos menos el cierre de x = 715 (tambien el lomo del cajon pasada la junta: asi el
    # redondeo del lomo de la culata llega entero a la junta).
    n = len(contorno)
    libres = [px_de(contorno[i][0]) < 712 and px_de(contorno[(i + 1) % n][0]) < 712 for i in range(n)]
    # El picado del puno (los dos lados): un panel hundido 1 mm con su pared.
    picado = cal.puntos([(660, 242), (632.5, 276), (650, 275.5), (622.5, 287.5), (675, 295), (635, 307.5),
                         (605, 322.5), (580, 342.5), (560, 367.5), (547.5, 395), (542.5, 413.75), (481, 395),
                         (490, 380), (505, 360), (525, 330), (547.5, 305), (572.5, 285), (597.5, 267.5),
                         (635, 251.25)])
    bm = _hinchar(contorno, libres, lambda x: mm(_suave(SEMIANCHOS, px_de(x))),
                  lambda x: mm(_suave(RADIOS, px_de(x))), picados=[picado])
    culata = k._objeto(bm.copy(), "culata", nogal)
    cantonera = k._objeto(bm, "cantonera", goma)
    lado_cola = [(0, 240), junta_cola[0], junta_cola[1], (0, 600)]
    k.restar(culata, k.perfil(cal.puntos([junta_cajon[0], (800, 160), (800, 330), junta_cajon[1]]), ancho=mm(80)),
             k.perfil(cal.puntos(lado_cola), ancho=mm(80)))
    k.intersecar(cantonera, k.perfil(cal.puntos(lado_cola), ancho=mm(80)))
    # El panel del picado (las caras de dentro de su contorno, a los dos lados), con el nogal oscuro.
    _pintar_dentro(culata, picado, nogal_picado)
    # La reja de la cantonera (los dos lados): rombos en columna y medios rombos (triangulos) contra los bordes.
    n_r = 10
    izq0, izq1, der0, der1, v0, v1 = (35, 316), (45, 528), (52, 316), (64, 528), 316, 528

    def reja(u, v):
        t = v / n_r
        a = (izq0[0] + (izq1[0] - izq0[0]) * t, v0 + (v1 - v0) * t)
        b = (der0[0] + (der1[0] - der0[0]) * t, v0 + (v1 - v0) * t)
        return (a[0] + (b[0] - a[0]) * u, a[1] + (b[1] - a[1]) * u)
    huecos = []
    for i in range(n_r):
        huecos.append([reja(0.5, i), reja(1.0, i + 0.5), reja(0.5, i + 1), reja(0.0, i + 0.5)])
    for i in range(1, n_r):
        huecos.append([reja(0.0, i - 0.5), reja(0.5, i), reja(0.0, i + 0.5)])
        huecos.append([reja(1.0, i - 0.5), reja(1.0, i + 0.5), reja(0.5, i)])
    prismas = []
    for h in huecos:
        poligono = cal.puntos(_encoger(h, 1.6))
        prismas.append(k.perfil(poligono, desde=mm(17.5), hasta=mm(26)))
        prismas.append(k.perfil(poligono, desde=-mm(26), hasta=-mm(17.5)))
    k.restar(cantonera, k.unir(*prismas))
    # La 870 real: la culata se acorta en su tramo recto (PX_CULATA) y lo de detras se adelanta entero.
    for pieza in (culata, cantonera):
        k.deformar(pieza, lambda x, y, z: (x + adelante(x), y, z))

    # Anilla portacorrea, debajo de la culata cerca de la cantonera (se adelanta con su sitio de la culata).
    pivote = k.cilindro((X(163.4), Y(518.3), 0), (X(167.7), Y(528.4), 0), mm(3.4), 12, material=pavon,
                        nombre="pivote_correa")
    cx, cy, rr = X(172.5), Y(534.5), mm(3.0)
    anilla = k.barrido([(cx + rr * math.sin(2 * math.pi * i / 12), cy + rr * math.cos(2 * math.pi * i / 12), 0.0)
                        for i in range(13)], mm(0.9), 6, material=pavon, nombre="anilla_correa")
    for pieza in (pivote, anilla):
        k.mover(pieza, (adelante(X(165.5)), 0.0, 0.0))

    # El agarre: el centro del cuello de la culata a la altura del dedo corazon (donde cierra el puno, detras del
    # guardamonte). La mano izquierda, en el centro del guardamanos.
    return {"agarre": P(552, 344, 0.0), "boca": (xb, 0.0, 0.0), "guardamanos": P(1425, 283, 0.0),
            "calibracion": cal}


# ------------------------------------------------------------------ la comparacion (lanzado como script)
def lamina_proporciones(ruta, salida, d_px):
    """La lamina lateral (boca a la derecha) llevada al reparto de la 870 real, para comparar la forma: la culata se
    acorta en PX_CULATA, el canon se alarga en PX_CANON (d_px pixeles cada uno) y la imagen crece d_px a la derecha.
    Columna a columna, con interpolacion lineal (los dos tramos son rectos: la culata y un tubo liso)."""
    import numpy as np
    from PIL import Image
    im = np.asarray(Image.open(ruta).convert("RGB")).astype(np.float32)
    W = im.shape[1]
    u = np.arange(W + int(math.ceil(d_px)), dtype=np.float64)
    a0, a1 = PX_CULATA
    c0, c1 = PX_CANON
    v = np.where(u <= a0 + d_px, u - d_px,
        np.where(u <= a1, a0 + (u - (a0 + d_px)) * (a1 - a0) / (a1 - a0 - d_px),
        np.where(u <= c0, u,
        np.where(u <= c1 + d_px, c0 + (u - c0) * (c1 - c0) / (c1 + d_px - c0), u - d_px))))
    v = np.clip(v, 0, W - 1)
    i0 = np.floor(v).astype(int)
    i1 = np.minimum(i0 + 1, W - 1)
    f = (v - i0)[None, :, None]
    nueva = im[:, i0] * (1 - f) + im[:, i1] * f
    Image.fromarray(np.clip(nueva + 0.5, 0, 255).astype(np.uint8)).save(salida)

    def ida(px):
        """Pixel de la lamina -> pixel de la nueva."""
        if px <= a0:
            return px + d_px
        if px <= a1:
            return px + d_px * (a1 - px) / (a1 - a0)
        if px <= c0:
            return px
        if px <= c1:
            return px + d_px * (px - c0) / (c1 - c0)
        return px + d_px
    return len(u), ida


if __name__ == "__main__":
    # El paso `comparar` de construir_arma.py pone la lamina tal cual junto al modelo visto desde -Z (boca a la
    # izquierda). Esta lamina tiene la boca a la derecha: aqui se voltea en horizontal SOLO en ese paso (los contornos
    # con los que se modela siguen siendo los de la lamina original). Y como el modelo lleva el reparto de la 870 real
    # (ver arriba), deja dos: <n>_comparar.png con la lamina tal cual (volteada) y <n>_comparar_proporciones.png con
    # la lamina llevada a ese reparto (lamina_proporciones), que es la que mide la forma. Todo lo demas es
    # construir_arma.py sin cambios.
    import os
    import sys

    from PIL import Image, ImageOps

    sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
    import construir_arma as ca

    comparar_kit = ca.comparar

    def comparar_con(nombre, ficha, trabajo, revision, ancho_panel, proporciones):
        ruta = ca.laminas.ruta_lamina(ficha["lamina"], "lateral")
        base = os.path.join(trabajo, nombre + "_lamina_lateral_" + ("proporciones" if proporciones else "volteada"))
        ancho, ida = Image.open(ruta).width, (lambda px: px)
        if proporciones:
            cal_sx = 1.181 / (PX_BOCA - PX_COLA)
            d_px = (ficha["medidas_reales"]["canon"] - (PX_BOCA - PX_CERROJO) * cal_sx) / cal_sx
            ancho, ida = lamina_proporciones(ruta, base + ".png", d_px)
            ruta = base + ".png"
        volteada = base + ".png"
        ImageOps.mirror(Image.open(ruta).convert("RGB")).save(volteada)
        json_lateral = os.path.normcase(os.path.abspath(os.path.join(trabajo, f"{nombre}_lamina_lateral.json")))
        leer, ruta_lamina = ca.leer_json, ca.laminas.ruta_lamina

        def leer_volteado(r):
            d = leer(r)
            if os.path.normcase(os.path.abspath(r)) == json_lateral:
                x0, y0, x1, y1 = d["caja"]
                x0, x1 = int(round(ida(x0))), int(round(ida(x1)))
                d = dict(d, ancho=ancho, caja=[ancho - 1 - x1, y0, ancho - 1 - x0, y1])
            return d

        def ruta_volteada(lamina, vista=None):
            return volteada if vista == "lateral" else ruta_lamina(lamina, vista)

        ca.leer_json, ca.laminas.ruta_lamina = leer_volteado, ruta_volteada
        try:
            return comparar_kit(nombre, ficha, trabajo, revision, ancho_panel)
        finally:
            ca.leer_json, ca.laminas.ruta_lamina = leer, ruta_lamina

    def comparar_volteada(nombre, ficha, trabajo, revision, ancho_panel=1000):
        ruta, i_a, i_i = comparar_con(nombre, ficha, trabajo, revision, ancho_panel, True)
        otra = os.path.join(os.path.dirname(ruta), nombre + "_comparar_proporciones.png")
        os.replace(ruta, otra)
        print(f"-- comparacion con la lamina en las proporciones de la 870 real: {otra} "
              f"(IoU {i_a:.3f} ajustada, {i_i:.3f} por el largo)")
        return comparar_con(nombre, ficha, trabajo, revision, ancho_panel, False)

    ca.comparar = comparar_volteada
    sys.argv = [sys.argv[0], FICHA["nombre"]] + sys.argv[1:]
    sys.exit(ca.main())
