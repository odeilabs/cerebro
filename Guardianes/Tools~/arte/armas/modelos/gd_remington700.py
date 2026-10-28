"""gd_remington700: Remington 700 BDL de cerrojo (accion larga, .30-06) con visor de 50 mm, culata de nogal con
picado y cantonera de goma: el fusil de caza de los vigias, las Comadrejas y Brandao en Basiliskos, y el de cualquier
cazador o tirador de la saga. Tiene que leerse de lejos como fusil y no como escopeta: canon largo y fino, cajon
redondo, la bola del cerrojo a la derecha y, sobre todo, el visor. Lamina 028-remington-700.

Medidas reales (Remington 700 BDL .30-06, ficha del fabricante): 1.080 mm de largo (42,5"), canon de 22" (559 mm, de
la cara del cerrojo a la boca; se ven unos 548 delante del tope de retroceso), longitud de culata de 13 3/8" (340 mm,
del gatillo a la cantonera), cajon de 1,355" (34,4 mm) y 216 mm de largo con la cola, cuerpo del cerrojo de 0,697"
(17,7 mm). El visor (tipo Leupold VX-3 4,5-14x50) tiene tubo de 1" (25,4 mm), campana de 54 mm y 378 mm de largo. La
lamina cuadra con UNA sola escala (la del largo total): con ella la longitud de culata sale de 339 mm, la cantonera de
133 mm de alto y el tubo del visor de 27 mm, asi que no esta estirada. Lo que la lamina dibuja mas fino que el arma
real (el cajon, de 28 mm) se hace a la medida real; el resto, a la de la lamina. El LARGO del cajon es el de la
lamina: el cuerpo redondo va de justo detras de la base trasera (px 735,5: delante quedan a la vista el collar y la
raiz de la palanca) al tope de retroceso (px 1060,5-1067,5, la raya vertical bajo el final de la base delantera),
166 mm; con la cola sobre la garganta, 221.

La lamina lateral tiene la BOCA A LA DERECHA (ensena el lado derecho, +Z: la bola del cerrojo, la ventana de
expulsion, el tambor de deriva del visor): Calibracion(boca_izquierda=False). Y su silueta automatica coge la sombra del
suelo bajo la culata. Por eso la comparacion se hace con este fichero (ver abajo): quita la sombra de la silueta (lo que
queda por debajo de SUELO) y voltea la lamina. El modelo no usa los contornos automaticos (todo va dibujado en pixeles
de la lamina, leidos con la rejilla), asi que sale igual lanzado por construir_arma.py o por este fichero; solo la
comparacion de construir_arma.py sale mal (la lamina sin voltear y con sombra).

Lo que al kit le falta y se hace aqui: solidos por secciones (_solido/_anillo: la culata, la cantonera y el separador
son anillos de superelipses a lo largo de X), cambiar el material de una pieza (_material/_partir: la tapa del
pistolete, su separador y el picado salen de partir la madera con booleanas) y la comparacion con la lamina limpia y
volteada.

Las coordenadas de la lamina se leen con
    python laminas.py rejilla 028/lateral rejilla.png --paso 10 --recorte 420,280,700,500 --zoom 3 --umbral 40
Construirlo entero (modelo, texturas, agarre y la comparacion con la lamina limpia y volteada):
    python modelos/gd_remington700.py [--res 1024] [--solo modelo,comparar] [--revision <carpeta>]
"""
import math
import os
import sys

FICHA = {
    "nombre": "gd_remington700",
    "clase": "FusilCaza",
    "lamina": "028-remington-700",
    "para": "vigias, Comadrejas y Brandao de Basiliskos; cualquier fusil de caza de cerrojo con visor de la saga",
    "medidas_reales": {"largo": 1.0795, "canon": 0.5588, "longitud_culata": 0.3397, "diametro_cajon": 0.0344,
                       "largo_cajon": 0.216, "diametro_cerrojo": 0.0177, "tubo_visor": 0.0254, "campana_visor": 0.054},
    "texturas": {"res": 1024, "escala": 0.4},   # 0,4 m por tesela: la densidad de todas las gd_
    # la lamina de tres cuartos mira desde la derecha (+Z), por detras de la culata y desde arriba
    "vista_34": {"azimut": 212.0, "elevacion": 13.0, "focal": 70.0},
    "semilla": 700,
}

# Pixeles de la lamina lateral (remington-700_lateral.png, 2172 x 724, boca a la derecha)
PX_BOCA, PX_CANTONERA, PY_EJE = 2141, 27, 260.5   # la corona, la trasera de la cantonera y el eje del canon
LARGO = 1.0795                                     # 42,5 pulgadas
PY_VISOR = 175.0                                   # el eje del visor
R_CAJON = 17.2                                     # mm: 1,355" (la lamina lo dibuja de 28 mm)
PX_CAJON0, PX_CAJON1 = 735.5, 1060.5               # el cuerpo del cajon: de detras de la raiz de la palanca al tope
PX_TOPE1 = 1067.5                                  # el tope de retroceso (la raya vertical), y despues el canon
# El canon (px, radio en mm): el cuello de 1,06" junto al tope y el cono hasta la boca, del borde de arriba de la lamina
PERFIL_CANON = [(1064, 13.8), (1122, 13.6), (1160, 12.5), (1200, 11.5), (1250, 10.3), (1320, 9.7), (1500, 9.35),
                (1800, 8.9)]

# La sombra del suelo, que la silueta automatica pega a la culata: todo lo que queda por debajo de esta linea (justo
# por debajo del arma, rodeando la anilla de atras, y a la izquierda de la cantonera) se quita de la silueta de la
# lamina antes de comparar.
SUELO = [(0, 290), (25, 290), (25, 566), (36, 565.5), (60, 563.5), (80, 556), (86, 550), (100, 546), (120, 540.5),
         (140, 534.5), (160, 529), (165, 528), (167, 540), (197, 540), (199, 517), (240, 503.5), (300, 483.5),
         (360, 463.5), (400, 450.5), (410, 448.5), (420, 455.5), (430, 467.5), (470, 481.5), (512, 493), (522, 492),
         (535, 462), (546, 440), (2172, 440)]
# La caja del arma en la lamina de tres cuartos: la automatica coge la sombra y unas manchas del fondo.
CAJA_34 = (12, 163, 1743, 746)


# ------------------------------------------------------------------ la culata, en pixeles de la lamina
# Lo alto de la madera (el lomo, la nariz del lomo, la garganta y, bajo el cajon y el canon, el borde de la madera)
TOPE = [(77, 303.5), (90, 303.5), (100, 302), (110, 301), (120, 298), (130, 295), (140, 291), (150, 286), (160, 280.5),
        (170, 276.5), (180, 275.2), (190, 275.2), (200, 276.5), (250, 277.5), (300, 279.5), (350, 281.5), (400, 282.5),
        (440, 284.5), (460, 285.5), (470, 288), (475, 291.5), (480, 296), (485, 301.5), (490, 306.5), (495, 310.5),
        (500, 312.8), (510, 315), (520, 315), (530, 312.5), (540, 310), (560, 304), (580, 297), (600, 290),
        (620, 282), (640, 277), (655, 274), (680, 272.5), (720, 272), (745, 269), (800, 268), (900, 267), (1000, 266),
        (1100, 265), (1300, 264), (1550, 264)]
# Lo bajo: la quilla (sin la anilla), el pistolete con su tapa, el frente del pistolete, el guardamonte y el guardamanos
FONDO = [(77, 550.5), (86, 548), (100, 543.5), (120, 537.5), (140, 531.5), (160, 525.5), (180, 519.5), (200, 513.5),
         (220, 506.5), (240, 500), (260, 493), (280, 488), (300, 480), (320, 474), (340, 467), (360, 460),
         (380, 454), (400, 447.5), (410, 445.5), (415, 448), (420, 452), (425, 458), (430, 463.5), (440, 467),
         (450, 470.8), (460, 474), (470, 477.6), (480, 481), (490, 484.6), (500, 487.6), (510, 489.4), (516, 489.8),
         (520, 484), (525, 471), (530, 457), (535, 446), (540, 437.5), (550, 421.5), (560, 409.5), (570, 399.5),
         (580, 392.8), (590, 386.5), (600, 381.5), (610, 378), (620, 375.5), (630, 373.8), (640, 373), (660, 372.2),
         (700, 371), (720, 369.6), (760, 368.6), (800, 367), (850, 365), (900, 362), (950, 359), (975, 356),
         (1000, 352.5), (1020, 351), (1060, 349.2), (1100, 348), (1200, 344), (1300, 339), (1400, 334), (1500, 329),
         (1540, 326.5)]
# La punta del guardamanos, redonda de lado y desde arriba
PUNTA_ARRIBA = [(1550, 264), (1560, 264.5), (1566, 265.5), (1572, 267.5), (1577, 270.5), (1581, 275), (1584, 281),
                (1586, 288), (1587.5, 295.5)]
PUNTA_ABAJO = [(1540, 326.5), (1550, 326), (1560, 325), (1567, 324), (1573, 321.5), (1578, 318.5), (1582, 314),
               (1585, 308), (1586.6, 301.5), (1587.5, 295.5)]
PX_PUNTA0, PX_PUNTA1 = 1551.0, 1587.8
# Media anchura (mm), la fraccion de ella arriba y abajo y el exponente de la superelipse (2 elipse, mas cuadrada
# cuanto mas alto): culata de 41 mm, garganta de 33, 44 a la altura del cajon y 40 en la punta.
MEDIO_ANCHO = [(77, 20.5), (200, 20.5), (350, 20.0), (440, 19.0), (480, 17.6), (520, 16.6), (560, 16.6), (600, 17.6),
               (640, 19.6), (680, 21.4), (720, 22.0), (1000, 22.0), (1150, 21.6), (1400, 20.6), (1550, 20.0)]
F_ARRIBA = [(77, 0.62), (440, 0.62), (480, 0.74), (520, 0.84), (600, 0.92), (650, 0.96), (1080, 0.96), (1160, 0.88),
            (1550, 0.86)]
F_ABAJO = [(77, 0.70), (400, 0.70), (430, 0.80), (520, 0.84), (640, 0.86), (1100, 0.86), (1550, 0.78)]
EXPONENTE = [(77, 2.6), (440, 2.6), (520, 2.4), (640, 3.0), (720, 3.4), (1080, 3.4), (1160, 3.0), (1550, 2.8)]
ESTACIONES = (100, 120, 140, 160, 180, 205, 250, 300, 350, 395, 405, 412, 419, 427, 440, 458, 470, 478, 486, 494,
              502, 510, 516, 520, 525, 530, 536, 544, 555, 568, 584, 602, 622, 645, 670, 700, 740, 800, 870, 940, 975,
              1005, 1040, 1100, 1180, 1270, 1370, 1460, 1520, 1551, 1563, 1572, 1579, 1583.5, 1586.2, 1587.4)
# La junta de la cantonera (el borde de atras del separador blanco) y la cara de atras de la cantonera: (py, px)
JUNTA = [(300, 75.5), (320, 72.5), (350, 71.5), (400, 72.5), (450, 74.5), (500, 77.0), (556, 80.0)]
TRASERA = [(300, 29.0), (350, 27.5), (400, 30.0), (450, 32.0), (500, 35.0), (566, 37.0)]
SEPARADOR = 3.0                                    # px (1,5 mm)


# ------------------------------------------------------------------ utilidades propias (lo que le falta al kit)
def _interp(tabla, x):
    """Interpolacion lineal en una tabla [(x, valor), ...] ordenada (fuera, el extremo)."""
    if x <= tabla[0][0]:
        return tabla[0][1]
    for (x0, v0), (x1, v1) in zip(tabla[:-1], tabla[1:]):
        if x <= x1:
            return v0 + (v1 - v0) * (x - x0) / (x1 - x0)
    return tabla[-1][1]


def _anillo(cal, px, ya, yb, hw, fa=1.0, fb=1.0, n=2.5, N=22, x_de_py=None):
    """Un anillo de N puntos (metros) de una seccion redondeada en la columna px de la lamina: de ya (arriba) a yb
    (abajo) en pixeles, hw la media anchura (m) a media altura, fa y fb la fraccion de hw arriba y abajo, n el
    exponente de la superelipse. x_de_py(py) -> px: la columna de cada punto, para una junta inclinada o curva."""
    ym, hy = (ya + yb) * 0.5, (yb - ya) * 0.5
    e = 2.0 / n
    vmax = max(abs(math.sin(2.0 * math.pi * i / N)) for i in range(N)) ** e    # con N impar de cuartos no hay
    pts = []                                                                     # punto en lo alto: se estira
    for i in range(N):
        t = 2.0 * math.pi * i / N
        c, s = math.cos(t), math.sin(t)
        u = math.copysign(abs(c) ** e, c)
        v = math.copysign(abs(s) ** e, s) / vmax                # v = 1 arriba
        f = 1.0 - (1.0 - fa) * max(v, 0.0) ** 1.6 - (1.0 - fb) * max(-v, 0.0) ** 1.6
        py = ym - v * hy
        pts.append((cal.x(x_de_py(py) if x_de_py else px), cal.y(py), u * hw * f))
    return pts


def _solido(k, anillos, material, nombre):
    """Un solido cerrado por anillos (listas de puntos 3D con el mismo numero de puntos, en orden). Los extremos se
    tapan con un abanico desde su centro."""
    import bmesh
    from mathutils import Vector
    bm = bmesh.new()
    V = [[bm.verts.new(p) for p in a] for a in anillos]
    N = len(anillos[0])
    for A, B in zip(V[:-1], V[1:]):
        for i in range(N):
            j = (i + 1) % N
            bm.faces.new((A[i], A[j], B[j], B[i]))
    for anillo in (V[0], V[-1]):
        centro = bm.verts.new(sum((v.co for v in anillo), Vector()) / N)
        for i in range(N):
            bm.faces.new((anillo[i], anillo[(i + 1) % N], centro))
    return k._objeto(bm, nombre, material)


def _material(ob, nombre):
    """Cambia el material de una pieza (las booleanas del kit dejan el de la pieza de la que sale)."""
    import bpy
    ob.data.materials.clear()
    ob.data.materials.append(bpy.data.materials[nombre])
    for p in ob.data.polygons:
        p.material_index = 0
    return ob


def _partir(k, pieza, herramienta, material, nombre):
    """Separa de `pieza` lo que cae dentro de `herramienta()` (una funcion que crea la herramienta: hacen falta dos,
    las booleanas la borran) como pieza aparte con otro material."""
    trozo = k.copiar(pieza)
    trozo.name = nombre
    k.intersecar(trozo, herramienta())
    k.restar(pieza, herramienta())
    return _material(trozo, material)


def _suave(puntos, n=2):
    """Catmull-Rom cerrado: n puntos por tramo (redondea un contorno dibujado a mano con pocos puntos)."""
    m = len(puntos)
    salida = []
    for i in range(m):
        p0, p1, p2, p3 = puntos[i - 1], puntos[i], puntos[(i + 1) % m], puntos[(i + 2) % m]
        for j in range(n):
            t = j / n
            t2, t3 = t * t, t * t * t
            salida.append(tuple(0.5 * (2 * p1[c] + (p2[c] - p0[c]) * t
                                       + (2 * p0[c] - 5 * p1[c] + 4 * p2[c] - p3[c]) * t2
                                       + (3 * p1[c] - p0[c] - 3 * p2[c] + p3[c]) * t3) for c in (0, 1)))
    return salida


def _cinta(puntos, anchos):
    """Un poligono cerrado alrededor de una polilinea 2D con un ancho en cada punto (el gatillo)."""
    izq, der = [], []
    n = len(puntos)
    for i, (p, w) in enumerate(zip(puntos, anchos)):
        a, b = puntos[max(i - 1, 0)], puntos[min(i + 1, n - 1)]
        tx, ty = b[0] - a[0], b[1] - a[1]
        L = math.hypot(tx, ty) or 1.0
        nx, ny = -ty / L, tx / L
        izq.append((p[0] + nx * w * 0.5, p[1] + ny * w * 0.5))
        der.append((p[0] - nx * w * 0.5, p[1] - ny * w * 0.5))
    return izq + der[::-1]


def _paneles(k, cal, puntos, z0, z1=0.08):
    """Una funcion que hace los dos prismas de un panel (a cada lado, de |z| = z0 hacia fuera): para _partir."""
    def hacer():
        a = k.perfil(cal.puntos(puntos), desde=z0, hasta=z1)
        return k.unir(a, k.perfil(cal.puntos(puntos), desde=-z1, hasta=-z0))
    return hacer


# ------------------------------------------------------------------ modelo
def modelar(k):
    mm = k.mm
    # x: de la corona (2141) a la trasera de la cantonera (27) son los 1.079,5 mm del largo; y con la misma escala (la
    # lamina no esta estirada) y el cero en el eje del canon (260,5).
    cal = k.Calibracion(px_boca=PX_BOCA, px_cola=PX_CANTONERA, largo=LARGO, py_eje=PY_EJE, boca_izquierda=False)
    yv = cal.y(PY_VISOR)                              # el eje del visor, 43,7 mm sobre el del canon

    # El pavonado brillante (canon, cajon, guardamonte): 'pintura_coche' (laca sobre escamas de metal, rugosidad 0,38)
    # da el negro azulado con brillo; 'pavonado'/'acero' van a 'metal' (gris claro). El visor, anodizado mate.
    pavonado = k.material("pintura_coche_pavonado", "#16191e", rugosidad=0.35, metal=0.0)
    visor = k.material("pintura_visor", "#1d1e21", rugosidad=0.6, metal=0.0)
    acero = k.material("acero_cerrojo", "#9a9ca0", rugosidad=0.25, metal=1.0)       # el cerrojo pulido
    nogal = k.material("madera_nogal", "#6a3a22", rugosidad=0.45, veta=(1.0, 0.0, 0.0))
    picado = k.material("madera_nogal_picado", "#3f2a1e", rugosidad=0.75, veta=(1.0, 0.0, 0.0))
    goma = k.material("goma_cantonera", "#1b1b1c", rugosidad=0.9)
    filete = k.material("plastico_filete", "#e8d6b0", rugosidad=0.4)        # separadores blancos y el anillo dorado

    # ---------------------------------------------------------------- culata (nogal), cantonera y separador
    def seccion(px):
        return (mm(_interp(MEDIO_ANCHO, px)), _interp(F_ARRIBA, px), _interp(F_ABAJO, px), _interp(EXPONENTE, px))

    def anillo_madera(px):
        hw, fa, fb, n = seccion(px)
        if px <= PX_PUNTA0:
            ya, yb = _interp(TOPE, px), _interp(FONDO, px)
        else:                                       # la punta: redonda de lado y desde arriba
            ya, yb = _interp(PUNTA_ARRIBA, px), _interp(PUNTA_ABAJO, px)
            hw *= math.sqrt(max(0.0, 1.0 - ((px - PX_PUNTA0) / (PX_PUNTA1 - PX_PUNTA0)) ** 2))
            hw = max(hw, mm(1.5))
        return _anillo(cal, px, ya, yb, hw, fa, fb, n)

    def junta(py, d=0.0):
        return _interp(JUNTA, py) + d

    hw0, fa0, fb0, n0 = seccion(77)
    ya0, yb0 = 304.0, 555.0                        # la cantonera baja un poco mas que la quilla de la madera
    madera0 = _anillo(cal, 77, _interp(TOPE, 77), _interp(FONDO, 77), hw0, fa0, fb0, n0,
                      x_de_py=lambda py: junta(py, SEPARADOR))
    separador0 = _anillo(cal, 77, ya0, yb0, hw0, fa0, fb0, n0, x_de_py=junta)
    _solido(k, [separador0, madera0], filete, "separador_cantonera")
    # La cantonera: de la junta a su cara de atras, con los cantos redondeados (radio de 7 px, 3,6 mm).
    R = 7.0
    anillos = [separador0]
    for dd in (30.0, 18.0, 9.0, 7.0, 4.5, 2.5, 1.0, 0.0):          # pixeles desde la cara de atras
        hundido = R - math.sqrt(max(0.0, R * R - (R - dd) ** 2)) if dd < R else 0.0

        def x_de_py(py, dd=dd):
            return _interp(TRASERA, py) + dd
        t = dd / 43.0
        anillos.append(_anillo(cal, 50, ya0 + hundido, 565.0 - 9.0 * t - hundido, hw0 - cal.dx(hundido), fa0, fb0,
                               n0, x_de_py=x_de_py))
    _solido(k, anillos, goma, "cantonera")
    culata = _solido(k, [madera0] + [anillo_madera(px) for px in ESTACIONES], nogal, "culata")

    # La carrillera (Monte Carlo) del lado izquierdo (-Z), que no ensena ninguna lamina: 4 mm en un ovalo del lomo.
    def carrillera(x, y, z):
        px, py = PX_BOCA + x / cal.sx, PY_EJE - y / cal.sy
        r2 = ((px - 290.0) / 125.0) ** 2 + ((py - 330.0) / 52.0) ** 2
        if z >= 0.0 or r2 >= 1.0:
            return (x, y, z)
        lado = min(1.0, -z / (0.6 * mm(_interp(MEDIO_ANCHO, px))))
        return (x, y, z - mm(4.0) * min(1.0, (1.0 - r2) * 3.0) * lado * lado)
    k.deformar(culata, carrillera)

    # El canal del canon en el guardamanos, desde el tope de retroceso (0,8 mm de holgura: la raya oscura entre canon
    # y madera).
    k.restar(culata, k.torno([(cal.x(1066), 0.0), (cal.x(1066), mm(14.6))]
                             + [(cal.x(px), mm(r + 0.8)) for px, r in PERFIL_CANON if px in (1122, 1160, 1200, 1250)]
                             + [(cal.x(1400), mm(10.3)), (cal.x(1600), mm(10.0)), (cal.x(1600), 0.0)],
                             eje="x", segmentos=24))

    # La tapa del pistolete (goma negra) y su separador blanco: lo que queda por debajo de dos rectas paralelas a su
    # fondo (de (430, 463,5) a (510, 489,4)), 8 y 10,5 px por encima.
    def bajo_recta(d):
        def hacer():
            p = [(405, 455.4 - d), (535, 497.5 - d), (535, 520), (405, 520)]
            return k.perfil(cal.puntos(p), ancho=0.1)
        return hacer
    _partir(k, culata, bajo_recta(8.0), goma, "tapa_pistolete")
    _partir(k, culata, bajo_recta(10.5), filete, "separador_pistolete")

    # El picado: en el pistolete (con sus dos puntas por delante) y en el guardamanos (con las dos puntas de cada
    # lado), medio milimetro hundido: el borde se lee.
    picado_emp = [(437, 447), (447, 420), (460, 400), (483, 373), (507, 350), (533, 330), (570, 315), (607, 305),
                  (650, 293), (597, 342), (640, 360), (607, 363), (583, 370), (563, 383), (547, 403), (530, 427),
                  (517, 453), (505, 468)]
    picado_del = [(1067, 274), (1450, 272.5), (1380, 301.5), (1436, 321), (1070, 339), (1133, 305)]
    for nombre_p, pts in (("picado_pistolete", picado_emp), ("picado_guardamanos", picado_del)):
        p = _partir(k, culata, _paneles(k, cal, pts, mm(2.0)), picado, nombre_p)
        k.deformar(p, lambda x, y, z: (x, y, z - math.copysign(mm(0.35), z)))

    # ---------------------------------------------------------------- canon (pavonado)
    # Del tope de retroceso a la boca; la corona y el anima de 7,8 mm.
    xb = cal.x(PX_BOCA)
    k.torno([(cal.x(PERFIL_CANON[0][0]), 0.0)] + [(cal.x(px), mm(r)) for px, r in PERFIL_CANON]
            + [(xb - mm(0.8), mm(8.4)), (xb, mm(7.8)), (xb, mm(4.8)), (xb - mm(0.6), mm(4.0)), (xb - mm(12), mm(3.9)),
               (xb - mm(12), 0.0)], eje="x", segmentos=24, material=pavonado, nombre="canon")

    # ---------------------------------------------------------------- cajon, cerrojo y mandos
    # El cajon redondo, hueco (el cerrojo va dentro), con la ventana de expulsion a la derecha (+Z): del puente de atras
    # (justo detras de la base trasera; delante quedan a la vista el collar y la raiz de la palanca) al tope de
    # retroceso, bajo el final de la base delantera. La ventana ocupa la mitad, como en las laminas.
    xr0, xr1 = cal.x(PX_CAJON0), cal.x(PX_CAJON1)
    # (con un anillo de mas en el agujero de gases: sin el, la booleana deja triangulos de 46 mm que sombrean mal)
    cajon = k.torno([(xr0, mm(9.6)), (xr0, mm(R_CAJON - 0.8)), (xr0 + mm(0.8), mm(R_CAJON)), (cal.x(1015), mm(R_CAJON)),
                     (xr1 - mm(1.0), mm(R_CAJON)), (xr1, mm(R_CAJON - 1.0)), (xr1, mm(9.6))], eje="x", segmentos=24,
                    material=pavonado, nombre="cajon", cerrado=True)
    # (y el agujero de gases de la anilla delantera, bajo la base, donde lo pone la lamina)
    k.restar(cajon, k.unir(k.caja((cal.x(806), -mm(3.5), 0.0), (cal.x(968), mm(12.5), mm(25))),
                           k.cilindro((cal.x(1015), cal.y(259), mm(10)), (cal.x(1015), cal.y(259), mm(20)), mm(1.8),
                                      10)))
    k.biselar(cajon, mm(0.6), 1, 50)
    # El tope de retroceso entre el cajon y el canon: un disco algo mas estrecho que el cajon (la raya de la junta).
    xt0, xt1 = cal.x(PX_CAJON1) - mm(0.3), cal.x(PX_TOPE1)
    k.torno([(xt0, 0.0), (xt0, mm(R_CAJON - 0.8)), (xt1, mm(R_CAJON - 0.8)), (xt1, 0.0)], eje="x", segmentos=24,
            material=pavonado, nombre="tope_retroceso")
    # La cola del cajon sobre la garganta, hasta el cajon (bajo el collar y la raiz de la palanca no queda hueco).
    k.perfil(cal.puntos([(628, 279.5), (636, 274), (650, 270), (738, 268.5), (738, 283), (628, 284)]), ancho=mm(15),
             material=pavonado, bisel=mm(1.2), nombre="cola_cajon")
    # El cerrojo: el cuerpo pulido (se ve por la ventana), la trasera con el collar y la raiz de la palanca, y la caja
    # de atras (pavonados). La trasera llega hasta el cajon: el collar (r 12,2 mm) y, desde la junta (px 711), la raiz
    # de la palanca, algo mas gorda (r 12,8), como en la lamina.
    k.cilindro((cal.x(730), 0.0, 0.0), (cal.x(1052), 0.0, 0.0), mm(8.85), 16, material=acero, nombre="cerrojo")
    k.torno([(cal.x(692), 0.0), (cal.x(692), mm(11.0)), (cal.x(694), mm(12.2)), (cal.x(711), mm(12.2)),
             (cal.x(711), mm(12.8)), (cal.x(PX_CAJON0 + 1.5), mm(12.8)), (cal.x(PX_CAJON0 + 1.5), 0.0)], eje="x",
            segmentos=16, material=pavonado, nombre="collar")
    k.torno([(cal.x(646), 0.0), (cal.x(646), mm(5.5)), (cal.x(648), mm(7.6)), (cal.x(652), mm(8.9)),
             (cal.x(660), mm(9.3)), (cal.x(694), mm(9.3)), (cal.x(694), 0.0)], eje="x", segmentos=16,
            material=pavonado, nombre="caja_cerrojo")
    # La palanca: sale de la trasera del cerrojo entre el collar y el cajon (px 711-736, 12 mm de ancho en la raiz) y
    # baja por fuera de la madera, afinandose, hasta la bola (a 31 mm del eje).
    palanca = [(723.5, 250, 3.0, 6.0), (723.5, 258, 9.0, 6.0), (723, 268, 16.0, 5.6), (722.5, 281, 22.5, 5.0),
               (721, 294, 25.8, 4.5), (714, 314, 28.9, 3.5), (709.5, 323, 30.0, 3.2), (705, 333, 30.8, 3.1)]
    k.barrido([cal.p(px, py, mm(z)) for px, py, z, _r in palanca], mm(4.8), 12, material=pavonado,
              radios=[mm(r) for *_p, r in palanca], nombre="palanca")
    k.esfera(cal.p(702, 346.5, mm(31.0)), mm(9.6), 14, 8, material=pavonado, nombre="bola_cerrojo")
    # El seguro, a la derecha de la cola, detras de la palanca.
    k.caja((cal.x(664), cal.y(281), mm(6.5)), (cal.x(680), cal.y(272), mm(10.0)), material=pavonado, bisel=mm(0.6),
           nombre="seguro")

    # ---------------------------------------------------------------- guardamonte, gatillo y chapa del cargador
    guarda_fuera = _suave([(638, 362), (645, 372), (652, 378.5), (657, 388), (660, 400), (664, 411), (671, 420),
                           (681, 427), (695, 430.5), (716, 431.5), (736, 428), (750, 422), (760, 414), (766, 404),
                           (769, 393), (771, 382), (775, 373.5), (781, 369.5), (790, 368), (792, 362)])
    guarda_hueco = _suave([(667, 358), (667, 380), (668, 392), (670, 402), (675, 411), (683, 418), (694, 423),
                           (710, 425), (725, 424), (740, 420), (750, 413), (755, 404), (757, 393), (755, 383),
                           (751, 375), (745, 358)])
    guarda = k.perfil(cal.puntos(guarda_fuera), ancho=mm(11), material=pavonado, nombre="guardamonte")
    k.restar(guarda, k.perfil(cal.puntos(guarda_hueco), ancho=mm(40)))
    k.biselar(guarda, mm(1.2), 1, 30)
    gatillo = _cinta([(693, 360), (690, 372), (685, 385), (682, 397), (683, 406), (687, 413), (692, 418)],
                     [9.0, 8.5, 8.0, 7.5, 7.0, 6.0, 4.5])
    k.perfil(cal.puntos(gatillo), ancho=mm(6.0), material=pavonado, bisel=mm(0.8), nombre="gatillo")
    # La chapa del cargador (abatible), por debajo de la madera del guardamonte a la boca del cargador.
    fondo_chapa = [(px, _interp(FONDO, px) + 0.6) for px in (776, 800, 850, 900, 950, 975, 1000, 1020, 1060, 1082)]
    chapa = [(px, py - 7.0) for px, py in fondo_chapa] + list(reversed(fondo_chapa))
    k.perfil(cal.puntos(chapa), ancho=mm(25), material=pavonado, bisel=mm(1.2), nombre="chapa_cargador")

    # ---------------------------------------------------------------- monturas (mate, como el visor)
    for x0, x1, xa0, xa1 in ((740, 806, 762, 797), (965, 1059, 1021, 1053)):
        k.perfil([(cal.x(x0), mm(12.0)), (cal.x(x0 + 6), mm(22.5)), (cal.x(x1 - 6), mm(22.5)), (cal.x(x1), mm(12.0))],
                 ancho=mm(19), material=visor, bisel=mm(1.0), nombre="base")
        xc = (xa0 + xa1) * 0.5
        k.cilindro((cal.x(xc), mm(21.0), -mm(10.3)), (cal.x(xc), mm(21.0), mm(10.3)), mm(6.0), 14, material=visor,
                   nombre="cola_milano")
        k.caja((cal.x(xa0 + 1), mm(21.0), -mm(7.5)), (cal.x(xa1 - 1), yv - mm(11.0), mm(7.5)), material=visor,
               bisel=mm(0.8), nombre="pie_anilla")
        k.torno([(cal.x(xa0), mm(12.6)), (cal.x(xa0), mm(14.6)), (cal.x(xa0) + mm(0.7), mm(15.3)),
                 (cal.x(xa1) - mm(0.7), mm(15.3)), (cal.x(xa1), mm(14.6)), (cal.x(xa1), mm(12.6))],
                eje="x", origen=(0.0, yv, 0.0), segmentos=24, material=visor, nombre="anilla", cerrado=True)
        for dx in (-8.5, 8.5):                      # los tornillos de la tapa, dos a cada lado
            for s in (1.0, -1.0):
                k.cilindro((cal.x(xc + dx), yv + mm(6.0), s * mm(12.5)), (cal.x(xc + dx), yv + mm(6.0), s * mm(15.4)),
                           mm(1.7), 6, material=visor, nombre="tornillo_anilla")

    # ---------------------------------------------------------------- visor (tipo 4,5-14x50, tubo de 1")
    # (px, radio en mm): el ocular, el anillo de aumentos, el selector estriado, el tubo, la silla de las torretas, el
    # cono y la campana del objetivo de 50 mm
    perfil_visor = [(561, 0.0), (561, 17.2), (556, 18.6), (556.5, 20.8), (558.5, 21.4), (646, 21.4), (652, 20.9),
                    (690, 17.3), (692, 15.8), (712, 15.8), (714, 18.0), (743, 18.0), (745.5, 16.0), (752, 12.7),
                    (880, 12.7), (884, 15.4), (888, 16.4), (938, 16.4), (942, 15.4), (946, 12.7), (1096, 12.7),
                    (1220, 26.6), (1224, 26.9), (1292, 26.9), (1296, 26.0), (1296.5, 24.2), (1290, 23.4), (1290, 0.0)]
    k.torno([(cal.x(px), mm(r)) for px, r in perfil_visor], eje="x", origen=(0.0, yv, 0.0), segmentos=24,
            material=visor, nombre="visor")
    # Las lentes (el pavonado brillante lee como vidrio tratado) y el anillo dorado de la campana.
    k.cilindro((cal.x(1290) - mm(0.2), yv, 0.0), (cal.x(1290) + mm(0.25), yv, 0.0), mm(23.4), 24, material=pavonado,
               nombre="objetivo")
    k.cilindro((cal.x(561) + mm(0.2), yv, 0.0), (cal.x(561) - mm(0.25), yv, 0.0), mm(17.2), 20, material=pavonado,
               nombre="ocular")
    k.torno([(cal.x(1257), mm(26.8)), (cal.x(1257), mm(27.1)), (cal.x(1262), mm(27.1)), (cal.x(1262), mm(26.8))],
            eje="x", origen=(0.0, yv, 0.0), segmentos=24, material=filete, nombre="anillo_dorado", cerrado=True)
    # Torretas: la de altura arriba y la de deriva a la derecha (+Z), con su tapa algo mas ancha.
    xt = cal.x(913)
    k.torno([(yv + mm(14.0), mm(12.6)), (cal.y(130), mm(12.6)), (cal.y(129), mm(14.2)), (cal.y(116), mm(14.2)),
             (cal.y(114.5), mm(13.4)), (cal.y(114.5), 0.0)], eje="y", origen=(xt, 0.0, 0.0), segmentos=20,
            material=visor, nombre="torreta_altura")
    k.torno([(mm(14.0), mm(12.6)), (mm(25.0), mm(12.6)), (mm(25.6), mm(13.8)), (mm(31.4), mm(13.8)),
             (mm(32.0), mm(13.0)), (mm(32.0), 0.0)], eje="z", origen=(xt, yv, 0.0), segmentos=20, material=visor,
            nombre="torreta_deriva")

    # ---------------------------------------------------------------- anillas de la correa
    for (a, b), c in ((((183, 516), (180, 532)), (180, 535.5)), (((1462, 334), (1462, 342)), (1462, 345.5))):
        k.cilindro(cal.p(*a, 0.0), cal.p(*b, 0.0), mm(3.3), 12, material=pavonado, nombre="teton")
        k.torno([(-mm(1.6), mm(2.0)), (-mm(1.6), mm(4.6)), (mm(1.6), mm(4.6)), (mm(1.6), mm(2.0))], eje="z",
                origen=cal.p(*c, 0.0), segmentos=12, material=pavonado, nombre="ojo_anilla", cerrado=True)

    if os.environ.get("ARMAS_PIEZAS"):              # para cuadrar el presupuesto: los triangulos de cada pieza
        cuenta = {}
        for ob in k._piezas:
            ob.data.calc_loop_triangles()
            nombre = ob.name.split(".")[0]
            cuenta[nombre] = cuenta.get(nombre, 0) + len(ob.data.loop_triangles)
        print("[armas] piezas: " + ", ".join(f"{n} {t}" for n, t in sorted(cuenta.items(), key=lambda c: -c[1])))

    # El agarre: el centro del pistolete a la altura del dedo corazon (donde cierra el puno). La mano izquierda, en el
    # centro del guardamanos (el centro del picado).
    return {"agarre": cal.p(510, 410, 0.0), "boca": (xb, 0.0, 0.0), "guardamanos": cal.p(1260, 303, 0.0),
            "calibracion": cal}


# ------------------------------------------------------------------ construirlo (con la comparacion limpia y volteada)
def _comparar_limpia(revision=None, umbral=22.0, margen=60):
    """La comparacion de construir_arma.comparar con la lamina lateral VOLTEADA (la boca a la izquierda, como compara
    siempre el kit) y su silueta LIMPIA: sin lo que queda por debajo de SUELO (la sombra del suelo, que la silueta
    automatica pega a la culata). Escribe en trabajo/ la lateral volteada y la de tres cuartos, las dos con `margen`
    pixeles de gris alrededor (el arma llega casi al borde y el recorte de comparar() pintaria franjas negras), y se las
    pasa a comparar() cambiando, solo mientras dura, la ruta de las laminas, la silueta de la lateral y la caja de sus
    contornos (la de tres cuartos, CAJA_34)."""
    import numpy as np
    from PIL import Image, ImageDraw
    from scipy import ndimage
    import construir_arma as ca
    import laminas
    nombre = FICHA["nombre"]
    _ruta, ficha = ca.cargar_ficha(nombre)
    _ac, _salida, trabajo, rev = ca.carpetas(nombre, revision)
    silueta, caja = laminas.silueta, laminas.caja

    def con_margen(im):
        a = np.asarray(im.convert("RGB"))
        gris = tuple(int(v) for v in np.median(np.concatenate([a[:8].reshape(-1, 3), a[-8:].reshape(-1, 3)]), 0))
        lienzo = Image.new("RGB", (im.width + 2 * margen, im.height + 2 * margen), gris)
        lienzo.paste(im.convert("RGB"), (margen, margen))
        return lienzo

    original = laminas.ruta_lamina(FICHA["lamina"], "lateral")
    im = Image.open(original).convert("RGB")
    w, h = im.size
    sombra = Image.new("L", (w, h), 0)
    ImageDraw.Draw(sombra).polygon([tuple(p) for p in SUELO] + [(w, h), (0, h)], fill=255)
    m = silueta(original, umbral) & ~(np.asarray(sombra) > 0)
    etiquetas, n = ndimage.label(m)                          # y solo el arma: la mancha mas grande
    if n > 1:
        m = etiquetas == 1 + int(np.argmax(ndimage.sum(m, etiquetas, range(1, n + 1))))
    mascara = np.zeros((h + 2 * margen, w + 2 * margen), bool)
    mascara[margen:margen + h, margen:margen + w] = m[:, ::-1]
    volteada = os.path.join(trabajo, nombre + "_lamina_lateral_volteada.png")
    con_margen(im.transpose(Image.FLIP_LEFT_RIGHT)).save(volteada)
    tres_cuartos = os.path.join(trabajo, nombre + "_lamina_tres_cuartos_margen.png")
    con_margen(Image.open(laminas.ruta_lamina(FICHA["lamina"], "tres_cuartos"))).save(tres_cuartos)
    caja_lat = caja(mascara)
    caja_34 = [v + margen for v in CAJA_34]
    ruta_lamina, leer_json = laminas.ruta_lamina, ca.leer_json

    def ruta_lamina_v(lamina, vista=None):
        return {"lateral": volteada, "tres_cuartos": tres_cuartos}.get(vista) or ruta_lamina(lamina, vista)

    def silueta_v(ruta, *a, **kw):
        return mascara.copy() if ruta == volteada else silueta(ruta, *a, **kw)

    def leer_json_v(ruta):
        d = leer_json(ruta)
        if ruta.endswith("_lamina_lateral.json"):
            d = dict(d, caja=list(caja_lat), umbral=umbral)
        elif ruta.endswith("_lamina_tres_cuartos.json"):
            d = dict(d, caja=caja_34)
        return d

    laminas.ruta_lamina, laminas.silueta, ca.leer_json = ruta_lamina_v, silueta_v, leer_json_v
    try:
        return ca.comparar(nombre, ficha, trabajo, rev)
    finally:
        laminas.ruta_lamina, laminas.silueta, ca.leer_json = ruta_lamina, silueta, leer_json


def main():
    import argparse
    sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
    import construir_arma as ca
    p = argparse.ArgumentParser(description="Construye gd_remington700 con construir_arma.py y hace la comparacion "
                                            "con la lamina limpia (sin la sombra) y volteada (boca a la derecha).")
    p.add_argument("--res", type=int, default=0)
    p.add_argument("--solo", default="laminas,modelo,texturas,agarre,comparar")
    p.add_argument("--revision", default=None)
    p.add_argument("--blender", default=None)
    a = p.parse_args()
    pasos = [s for s in a.solo.split(",") if s]
    resto = [s for s in pasos if s != "comparar"]
    if resto:
        argv = ["construir_arma.py", FICHA["nombre"], "--solo", ",".join(resto)]
        if a.res:
            argv += ["--res", str(a.res)]
        if a.revision:
            argv += ["--revision", a.revision]
        if a.blender:
            argv += ["--blender", a.blender]
        sys.argv = argv
        ca.main()
    if "comparar" in pasos:
        ruta, i_a, i_i = _comparar_limpia(a.revision)
        print(f"-- comparacion (lamina limpia y volteada): {ruta} (IoU {i_a:.3f} ajustada, {i_i:.3f} por el largo)")
    return 0


if __name__ == "__main__":
    sys.exit(main())
