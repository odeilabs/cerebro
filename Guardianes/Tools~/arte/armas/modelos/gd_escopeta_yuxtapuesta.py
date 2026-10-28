"""gd_escopeta_yuxtapuesta: escopeta de caza de dos canones yuxtapuestos (lado a lado) del calibre 12, de bascula
(boxlock) grabada con dos gatillos y culata de pistola de nogal con picado: la del Vigilante y la del furtivo de
Basiliskos, y la de cualquier cazador de la saga. Lamina 027-escopeta-yuxtapuesta.

Medidas reales (una yuxtapuesta del 12 de caza, tipo Merkel 147E, AYA o Beretta 471): canones de 26" (660 mm), unos
1.120 mm de largo total, longitud de culata (del gatillo delantero a la cantonera) de unos 385 mm, caida de 39 mm en
el lomo y de 62 en el talon, cantonera de 145 mm, canones de 28 mm de diametro en la recamara y 20,8 en la boca (anima
de 18,4 mm), bascula de 54 mm de ancho. La lamina cuadra con una sola escala (la del canon de 26"): las caidas, la
cantonera y la longitud de culata salen reales, asi que no esta estirada.

La lamina lateral tiene la BOCA A LA DERECHA (ensena el lado derecho, +Z): Calibracion(boca_izquierda=False), y la
comparacion del kit (que no voltea la lamina) se hace con este fichero (ver abajo). Y pinta los canones de lado como
una superpuesta (43 mm de alto delante del guardamanos): el modelo lleva los de una yuxtapuesta real (21-28 mm), que
es lo que ensenan la de tres cuartos y la de la boca.

Lo que al kit le falta y se hace aqui: un solido por secciones (_solido: la culata, la cantonera, el guardamanos y las
costillas son anillos de superelipses a lo largo de X, no extrusiones), cambiar el material de una pieza (_material:
el picado y el pomo salen de partir la madera con booleanas) y la comparacion con una lamina de boca a la derecha.

Las coordenadas de la lamina se leen con
    python laminas.py rejilla 027/lateral rejilla.png --paso 10 --recorte 440,220,760,460 --zoom 3
Construirla entera (modelo, texturas, agarre y la comparacion volteada):
    python modelos/gd_escopeta_yuxtapuesta.py [--res 1024] [--solo modelo,comparar] [--revision <carpeta>]
"""
import math
import os
import sys

FICHA = {
    "nombre": "gd_escopeta_yuxtapuesta",
    "clase": "Escopeta",
    "lamina": "027-escopeta-yuxtapuesta",
    "para": "el Vigilante y el furtivo de Basiliskos; cualquier escopeta de caza de dos canones de la saga",
    "medidas_reales": {"canon": 0.660, "largo": 1.120, "longitud_culata": 0.385, "caida_lomo": 0.039,
                       "caida_talon": 0.062, "diametro_recamara": 0.028, "diametro_boca": 0.0208, "anima": 0.0184,
                       "ancho_bascula": 0.054},
    "texturas": {"res": 1024, "escala": 0.4},   # 0,4 m por tesela: la densidad de todas las gd_
    # la lamina de tres cuartos mira desde delante y a la derecha (+Z), desde arriba y con gran angular (la boca
    # cerca: los canones se ven en diagonal y la culata casi de lado)
    "vista_34": {"azimut": 132.0, "elevacion": 26.0, "focal": 18.0},
    "semilla": 27,
}

# Pixeles de la lamina lateral (escopeta-yuxtapuesta_lateral.png, 2172 x 724, boca a la derecha)
PX_BOCA, PX_RECAMARA, PY_EJE = 2137, 890, 212      # la corona, la cara de la recamara y el eje de los canones
CANON = 0.660                                      # 26 pulgadas


# ------------------------------------------------------------------ utilidades propias (lo que le falta al kit)
def _interp(tabla, x):
    """Interpolacion lineal en una tabla [(x, valor), ...] ordenada (fuera, el extremo)."""
    if x <= tabla[0][0]:
        return tabla[0][1]
    for (x0, v0), (x1, v1) in zip(tabla[:-1], tabla[1:]):
        if x <= x1:
            return v0 + (v1 - v0) * (x - x0) / (x1 - x0)
    return tabla[-1][1]


def _cortes(poligono, px):
    """Las y donde la vertical x = px corta un poligono (pixeles), ordenadas."""
    ys = []
    n = len(poligono)
    for i in range(n):
        (x0, y0), (x1, y1) = poligono[i], poligono[(i + 1) % n]
        if (x0 <= px < x1) or (x1 <= px < x0):
            ys.append(y0 + (px - x0) * (y1 - y0) / (x1 - x0))
    return sorted(ys)


def _vertical(lam, px):
    """(arriba, abajo) del contorno exterior de la lamina en la columna px."""
    ys = _cortes(lam.exterior, px)
    return ys[0], ys[-1]


def _seccion(cal, px, ya, yb, hw, fa=1.0, fb=1.0, n=2.5, N=28, x_de_py=None):
    """Un anillo de N puntos (metros) de una seccion redondeada en la columna px de la lamina: de ya (arriba) a yb
    (abajo), en pixeles; hw la media anchura (m) a media altura, fa y fb la fraccion de hw cerca de arriba y de abajo
    (el lomo de una culata es mas estrecho que su parte de abajo), n el exponente de la superelipse (2 elipse, mas
    cuadrada cuanto mas alto). x_de_py(py) -> px: la columna de cada punto, para un corte inclinado o curvo."""
    ym, hy = (ya + yb) * 0.5, (yb - ya) * 0.5
    e = 2.0 / n
    pts = []
    for i in range(N):
        t = 2.0 * math.pi * i / N
        c, s = math.cos(t), math.sin(t)
        u = math.copysign(abs(c) ** e, c)
        v = math.copysign(abs(s) ** e, s)                       # v = 1 arriba
        f = 1.0 - (1.0 - fa) * max(v, 0.0) ** 1.6 - (1.0 - fb) * max(-v, 0.0) ** 1.6
        py = ym - v * hy
        pts.append((cal.x(x_de_py(py) if x_de_py else px), cal.y(py), u * hw * f))
    return pts


def _solido(k, anillos, material, nombre):
    """Un solido cerrado por anillos (listas de puntos 3D con el mismo numero de puntos, en orden): culatas,
    guardamanos y costillas. Los extremos se tapan con un abanico desde su centro."""
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
    las booleanas la borran) como pieza aparte con otro material: el picado, el pomo de la empunadura."""
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
    """Un poligono cerrado alrededor de una polilinea 2D con un ancho en cada punto (gatillos)."""
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


def _holgura(pieza, hueco, desde_py):
    """La holgura (px) entre el trozo de `pieza` por debajo de desde_py y el borde de `hueco` (dos poligonos 2D en
    pixeles de la lamina, y hacia abajo): la punta de un gatillo dentro del arco del guardamonte. Negativa si la
    cruza o se sale del hueco (el gatillo quedaria soldado al arco)."""
    def lados(p):
        return [(p[i], p[(i + 1) % len(p)]) for i in range(len(p))]

    def dist(p, a, b):
        dx, dy = b[0] - a[0], b[1] - a[1]
        t = max(0.0, min(1.0, ((p[0] - a[0]) * dx + (p[1] - a[1]) * dy) / (dx * dx + dy * dy or 1e-12)))
        return math.hypot(p[0] - a[0] - t * dx, p[1] - a[1] - t * dy)

    def giro(p, q, r):
        return (q[0] - p[0]) * (r[1] - p[1]) - (q[1] - p[1]) * (r[0] - p[0]) > 0

    def dentro(q):
        d = False
        for (ax, ay), (bx, by) in lados(hueco):
            if (ay > q[1]) != (by > q[1]) and q[0] < ax + (q[1] - ay) * (bx - ax) / (by - ay):
                d = not d
        return d

    trozo = [(a, b) for a, b in lados(pieza) if a[1] >= desde_py and b[1] >= desde_py]
    if not all(dentro(a) for a, _ in trozo):
        return -1.0
    minimo = float("inf")
    for a, b in trozo:
        for c, d in lados(hueco):
            if giro(a, b, c) != giro(a, b, d) and giro(c, d, a) != giro(c, d, b):
                return -1.0
            minimo = min(minimo, dist(a, c, d), dist(b, c, d), dist(c, a, b), dist(d, a, b))
    return minimo


# ------------------------------------------------------------------ modelo
def modelar(k):
    mm = k.mm
    # x: de la corona (2137) a la cara de la recamara (890) son los 660 mm del canon; y con la misma escala (la lamina
    # no esta estirada) y el cero en el eje de los canones (212).
    cal = k.Calibracion(px_boca=PX_BOCA, px_cola=PX_RECAMARA, largo=CANON, py_eje=PY_EJE, boca_izquierda=False)
    lam = k.lamina("lateral")

    def py_de(y):                                   # metros -> pixel de la lamina (y)
        return PY_EJE - y / cal.sy

    # Pavonado brillante de los canones: la receta 'pintura_coche' (laca sobre escamas de metal, rugosidad 0,38) es
    # la que da un negro azulado con brillo; 'pavonado'/'acero' van a 'metal' (gris claro) y 'pintura' a un mate.
    pavonado = k.material("pintura_coche_canones", "#15181e", rugosidad=0.35, metal=0.0)
    acero = k.material("acero_bascula", "#4c4846", rugosidad=0.35, metal=1.0)          # bascula grabada, gris plata
    nogal = k.material("madera_nogal", "#7a4020", rugosidad=0.5, veta=(1.0, 0.0, 0.0))
    picado = k.material("madera_nogal_picado", "#3e2c22", rugosidad=0.7, veta=(1.0, 0.0, 0.0))
    goma = k.material("goma_cantonera", "#1c1c1c", rugosidad=0.85)

    # ---------------------------------------------------------------- canones
    xb, xm = cal.x(PX_RECAMARA), cal.x(PX_BOCA)
    radios = [(0.000, 14.0), (0.012, 14.0), (0.070, 13.6), (0.110, 12.8), (0.180, 12.1), (0.250, 11.6),
              (0.350, 11.2), (0.450, 10.9), (0.560, 10.6), (0.660, 10.4)]          # (distancia a la recamara, mm)

    def r_canon(x):
        return mm(_interp(radios, x - xb))

    def c_canon(x):                                 # el eje de cada canon, a cada lado de z = 0 (se tocan)
        return r_canon(x) - mm(0.3)

    rm, ra = r_canon(xm), mm(9.2)                   # radio en la boca y del anima
    for lado in (1.0, -1.0):
        perfil = [(xb, 0.0)] + [(xb + d, mm(r)) for d, r in radios[:-1]] + [
            (xm - mm(0.8), rm), (xm - mm(0.8), ra), (xm - 0.13, ra), (xm - 0.13, 0.0)]
        canon = k.torno(perfil, eje="x", segmentos=28, material=pavonado, nombre="canon")
        # la corona, de acero en blanco (como en la lamina de la boca): un anillo con sus dos chaflanes
        corona = k.torno([(xm - mm(0.8), rm), (xm, rm - mm(0.45)), (xm, ra + mm(0.5)), (xm - mm(0.8), ra)],
                         eje="x", segmentos=28, material=acero, nombre="corona", cerrado=True)
        for p in (canon, corona):
            k.deformar(p, lambda x, y, z, s=lado: (x, y, z + s * c_canon(x)))

    # Costilla de arriba (plana, medio milimetro por debajo de los canones) y la de abajo delante del guardamanos.
    def costilla(x0, x1, arriba, n=9):
        anillos = []
        for i in range(n):
            x = x0 + (x1 - x0) * i / (n - 1)
            r = r_canon(x)
            w = mm(_interp([(xb, 4.6), (xm, 3.4)], x))
            if arriba:                              # el pie, dentro de la pared de los canones (no del anima)
                y1, y0 = r - mm(0.5), 0.72 * r
            else:
                y1, y0 = -0.72 * r, -(r - mm(1.0))
            b = mm(0.4)
            anillos.append([(x, y0, -w), (x, y0, w), (x, y1 - b, w), (x, y1, w - b), (x, y1, -w + b),
                            (x, y1 - b, -w)])
        return _solido(k, anillos, pavonado, "costilla")
    costilla(xb + mm(2), xm - mm(1.5), True)
    costilla(cal.x(1486), xm - mm(1.5), False, 6)
    # Punto de mira (de laton en la lamina; aqui de acero, para no gastar un material en 3 mm)
    xp = cal.x(2108)
    k.cilindro((xp, r_canon(xp) - mm(1.0), 0.0), (xp, r_canon(xp) + mm(2.6), 0.0), mm(1.0), 10, material=acero,
               nombre="punto_mira")
    k.esfera((xp, r_canon(xp) + mm(3.3), 0.0), mm(1.8), 10, 5, material=acero, nombre="punto_mira")

    # ---------------------------------------------------------------- bascula
    # De lado: la cola (838), la recamara con su tapa (890, a 10 mm sobre el eje), la barra bajo los canones y el
    # nudillo redondo de delante, donde gira el guardamanos.
    perfil_bascula = cal.puntos([(838, 206), (846, 194), (890, 193), (890, 239), (1020, 239), (1028, 250),
                                 (1034, 263), (1036, 277), (1034, 292), (1029, 305), (1022, 314), (1000, 315.5),
                                 (950, 316.5), (900, 317), (845, 318), (838, 318)])
    bascula = k.perfil(perfil_bascula, ancho=mm(54), material=acero, nombre="bascula")
    # De frente: 54 mm de ancho a la altura de los canones, 44 abajo (el cuerpo redondeado de una bascula).
    yb_b, ya_b = cal.y(318), cal.y(193)
    seccion = k.redondear([(-mm(22), yb_b), (mm(22), yb_b), (mm(26.6), yb_b + mm(26)), (mm(27), -mm(14)),
                           (mm(27), ya_b), (-mm(27), ya_b), (-mm(27), -mm(14)), (-mm(26.6), yb_b + mm(26))],
                          [mm(9), mm(9), mm(12), 0, mm(7), mm(7), 0, mm(12)], 5)
    k.intersecar(bascula, k.perfil(seccion, plano="zy", desde=cal.x(830), hasta=cal.x(1045), material=acero))
    k.biselar(bascula, mm(1.0), 2, 30)
    # El eje de la bisagra, asomando en el nudillo.
    k.cilindro((cal.x(1006), cal.y(278), -mm(27.3)), (cal.x(1006), cal.y(278), mm(27.3)), mm(4.5), 16,
               material=acero, nombre="eje_bisagra")
    # Los escudos (fences): las cupulas de la bascula alrededor de la recamara de cada canon.
    L, R = cal.dx(890 - 848), r_canon(xb) + mm(1.2)
    cupula = [(xb - L * math.cos(math.radians(a)), R * math.sin(math.radians(a))) for a in range(0, 91, 15)]
    for s in (1.0, -1.0):
        k.torno(cupula, eje="x", origen=(0.0, 0.0, s * c_canon(xb)), segmentos=28, material=acero, nombre="escudo")

    # La cola de arriba sobre la garganta y la llave de apertura encima, con la pala del pulgar hacia la derecha (+Z).
    k.perfil(cal.puntos([(745, 213), (790, 210.5), (846, 205), (846, 212), (790, 220), (745, 229)]), ancho=mm(14),
             material=acero, bisel=mm(0.6), nombre="cola_superior")
    llave = k.perfil(cal.puntos([(846, 195), (820, 197), (795, 201), (770, 205), (760, 203), (748, 199), (737, 200),
                                 (730, 207), (729, 216), (733, 224), (742, 228), (752, 227), (758, 219), (768, 213.5),
                                 (790, 210.5), (846, 205)]), ancho=mm(30), material=acero, nombre="llave")
    planta = [(cal.x(px), mm(z)) for px, z in ((848, -4.5), (790, -4.5), (765, -5.0), (755, -6.5), (740, -7.0),
                                               (730, -5.0), (727, 1.0), (730, 8.0), (740, 11.0), (755, 9.5),
                                               (765, 5.0), (790, 4.5), (848, 4.5))]
    k.intersecar(llave, k.perfil(k.redondear(planta, mm(1.5), 2), plano="xz", desde=cal.y(240), hasta=cal.y(185)))
    k.biselar(llave, mm(0.6), 1, 30)

    # ---------------------------------------------------------------- guardamonte y gatillos
    # El arco: por fuera, el contorno de la lamina (el borde de arriba se mete 4 px en la madera; abajo, 1 px por
    # fuera); por dentro, su hueco con los dos gatillos quitados. Arriba y delante, mas estrecho que en la lamina (el
    # arco de 3,5-5 mm lee mejor); abajo y atras, donde acaban los gatillos, casi el de la lamina (2,6-3,5 mm), para
    # que las puntas queden sueltas, con holgura, como en la lamina (detalle_empunadura y lateral).
    guarda = k.perfil(cal.puntos(_suave([(682, 340), (690, 345), (697, 352), (707.6, 374.6), (722.6, 385),
                                  (752, 393.8), (790, 393.5), (817, 383.5), (828, 374), (832, 366), (834.5, 358),
                                  (831, 341), (827, 333), (818, 322), (818, 316), (790, 319.5), (740, 326),
                                  (700, 331.5), (682, 334)])), ancho=mm(10), material=acero, nombre="guardamonte")
    hueco = _suave([(712, 334), (745, 331), (790, 326), (811, 327), (821, 337), (825, 352), (823.5, 365),
                    (816.5, 375.6), (803.5, 383), (785, 387.4), (760, 387.8), (740, 385.4), (725.8, 380.8),
                    (714.6, 372.2), (709.5, 356), (709, 341)])
    k.restar(guarda, k.perfil(cal.puntos(hueco), ancho=mm(30)))
    k.biselar(guarda, mm(0.8), 2, 30)
    # La cola del guardamonte por delante de la empunadura.
    k.perfil(cal.puntos([(640, 344), (668, 338), (697, 333), (699, 341), (680, 342.5), (660, 346.5), (640, 351)]),
             ancho=mm(9), material=acero, bisel=mm(0.8), nombre="cola_guardamonte")
    # Dos gatillos de hoja curva, con la punta hacia delante: el de delante (canon derecho) sigue la hoja de la lamina
    # y el de atras acaba 2 mm antes que en ella (alli la punta casi roza el arco). Las puntas, sueltas dentro del
    # arco: si alguna queda a menos de 1,5 mm de el (o lo cruza), el modelo no se construye.
    for nombre, linea in (("gatillo_delantero", [(774, 318), (767.5, 336), (763, 350), (761.5, 364), (763.5, 374),
                                                 (766.8, 381.5)]),
                          ("gatillo_trasero", [(731, 322), (723.5, 338), (719.2, 350), (717.6, 361), (718.7, 367.2),
                                               (720.8, 370.6)])):
        hoja = _cinta(linea, [5.5, 5.0, 4.6, 4.2, 3.8, 3.2])
        h = _holgura(hoja, hueco, 345)
        if h < 0 or cal.dy(h) < mm(1.5):
            raise ValueError(f"{nombre}: la punta " + ("cruza el arco del guardamonte" if h < 0 else
                                                        f"queda a {cal.dy(h) * 1000:.1f} mm del arco (minimo 1,5)"))
        print(f"[armas] {nombre}: la punta queda a {cal.dy(h) * 1000:.1f} mm del arco del guardamonte")
        k.perfil(cal.puntos(hoja), ancho=mm(6), material=acero, bisel=mm(0.7), nombre=nombre)

    # ---------------------------------------------------------------- culata (nogal) y cantonera (goma)
    def arriba_c(px):
        if px <= 725:
            return _vertical(lam, px)[0]
        return _interp([(725, 232), (775, 217), (820, 210), (838, 208)], px)

    def abajo_c(px):
        if px <= 645:
            return _vertical(lam, px)[1]
        return _interp([(645, 346.5), (680, 339), (720, 333), (760, 328), (800, 323), (838, 320)], px)

    anchos = [(23, 19.5), (60, 21.0), (150, 21.2), (300, 20.5), (400, 19.5), (450, 18.0), (500, 17.0), (545, 16.5),
              (600, 16.8), (650, 18.2), (700, 20.5), (760, 23.5), (838, 26.0)]
    f_arriba = [(23, 0.66), (300, 0.62), (490, 0.60), (530, 0.72), (560, 0.84), (700, 0.90), (838, 0.95)]
    f_abajo = [(23, 0.72), (420, 0.72), (450, 0.82), (545, 0.86), (600, 0.90), (838, 0.94)]
    exponente = [(23, 2.5), (600, 2.5), (700, 2.9), (838, 3.4)]

    def anillo_c(px, ya=None, yb=None, x_de_py=None):
        return _seccion(cal, px, arriba_c(px) if ya is None else ya, abajo_c(px) if yb is None else yb,
                        mm(_interp(anchos, px)), _interp(f_arriba, px), _interp(f_abajo, px),
                        _interp(exponente, px), 32, x_de_py)

    # La junta goma-madera va inclinada como la cantonera: de (55, 303.5) arriba a (78, 571.5) abajo.
    junta = anillo_c(66, 303.5, 571.5, lambda py: 55 + (py - 303.5) / (571.5 - 303.5) * 23)
    _solido(k, [anillo_c(px) for px in (23.5, 27, 32, 38, 45, 51)] + [junta], goma, "cantonera")
    # (mas juntas en la nariz del lomo, 512-548, y en la muesca del pomo, 438-452; sin ninguna en el teton, 226-250)
    estaciones = (90, 140, 200, 258, 320, 385, 415, 430, 438, 444, 452, 462, 474, 488, 500, 508, 514, 520, 525, 530,
                  534, 538, 542, 546, 551, 557, 566, 580, 596, 614, 634, 655, 676, 698, 720, 745, 772, 800, 820, 838)
    culata = _solido(k, [junta] + [anillo_c(px) for px in estaciones], nogal, "culata")

    # El pomo de la empunadura (acero pavonado): lo que queda por debajo de la linea (436, 423,5)-(556, 436,5).
    _partir(k, culata, lambda: k.perfil(cal.puntos([(436, 423.5), (556, 436.5), (560, 470), (430, 470)]),
                                        ancho=0.1), pavonado, "pomo")
    # El picado de la empunadura, a los dos lados, con su punta de madera lisa entrando por delante.
    picado_emp = [(472, 421), (478, 398), (498, 352), (538, 300), (588, 271), (640, 256), (686, 246), (664, 266),
                  (645, 288), (684, 318), (660, 331), (628, 341), (600, 354), (574, 377), (553, 405), (546, 421)]

    def panel(puntos):
        def hacer():
            a = k.perfil(cal.puntos(puntos), desde=mm(2), hasta=0.08)
            return k.unir(a, k.perfil(cal.puntos(puntos), desde=-0.08, hasta=-mm(2)))
        return hacer
    p = _partir(k, culata, panel(picado_emp), picado, "picado_empunadura")
    k.deformar(p, lambda x, y, z: (x, y, z - math.copysign(mm(0.3), z)))      # medio hundido: el borde se lee

    # ---------------------------------------------------------------- guardamanos (nogal)
    # Por debajo de los canones (su parte de arriba queda dentro de ellos), con la nariz redonda y la trasera
    # siguiendo el nudillo de la bascula.
    def arriba_g(px):
        oculto = py_de(-0.45 * r_canon(cal.x(px)))
        if px < 1380:
            return oculto
        return max(oculto, _interp([(1380, 222), (1430, 229), (1445, 232), (1455, 234), (1465, 238), (1473, 244),
                                    (1479, 251), (1483, 258), (1486, 264), (1488, 270), (1490, 274)], px))

    def abajo_g(px):
        if px < 1424:
            return _vertical(lam, px)[1]
        return _interp([(1424, 301), (1430, 299.5), (1445, 298), (1455, 297), (1465, 294.5), (1474, 291),
                        (1481, 287), (1487, 280), (1490, 275)], px)

    def ancho_g(px):
        base = _interp([(1036, 26.5), (1150, 26.0), (1300, 25.0), (1440, 23.0)], px)
        if px > 1440:
            base *= math.sqrt(max(0.0, 1.0 - ((px - 1440) / 51.0) ** 2))
        return mm(max(base, 2.5))

    nudillo = [(215, 1019), (239, 1021), (250, 1029), (263, 1035), (277, 1037), (292, 1035), (305, 1030),
               (314, 1023), (322, 1020)]
    anillos = [_seccion(cal, 1036, arriba_g(1036), abajo_g(1036), ancho_g(1036), 1.0, 0.86, 2.8, 24,
                        lambda py: _interp(nudillo, py))]
    for px in (1048, 1065, 1090, 1120, 1160, 1200, 1240, 1280, 1320, 1360, 1395, 1430, 1445, 1455, 1465, 1473,
               1479, 1483, 1486, 1488, 1489.5):
        anillos.append(_seccion(cal, px, arriba_g(px), abajo_g(px), ancho_g(px), 1.0, 0.86, 2.8, 24))
    guardamanos = _solido(k, anillos, nogal, "guardamanos")
    # el del guardamanos casi de arriba abajo, con una lengua de madera lisa entrando por cada punta
    picado_del = [(1087, 247), (1410, 246), (1352, 272), (1414, 295), (1140, 302), (1158, 276)]
    p = _partir(k, guardamanos, panel(picado_del), picado, "picado_guardamanos")
    k.deformar(p, lambda x, y, z: (x, y, z - math.copysign(mm(0.3), z)))

    # ---------------------------------------------------------------- anillas de la correa (los tetones)
    for (a, b), r in ((((1412, 296), (1412, 305)), 3.0), (((236, 506), (241, 520)), 3.0)):
        p0, p1 = cal.p(*a, 0.0), cal.p(*b, 0.0)
        k.cilindro(p0, p1, mm(r), 12, material=pavonado, nombre="teton")
        k.esfera(p1, mm(r + 0.6), 10, 5, material=pavonado, nombre="teton")

    # El agarre: el centro de la empunadura a la altura del dedo corazon (justo detras del guardamonte). La mano
    # izquierda, en el centro del guardamanos.
    return {"agarre": cal.p(584, 321, 0.0), "boca": (xm, 0.0, 0.0), "guardamanos": cal.p(1265, 266, 0.0),
            "calibracion": cal}


# ------------------------------------------------------------------ construirla (con la comparacion volteada)
def _comparar_volteada(revision=None):
    """La comparacion de construir_arma.comparar con la lamina lateral volteada: esta tiene la boca a la derecha y el
    kit compara siempre con la boca a la izquierda (sin voltear, la IoU sale de la lamina al reves). Escribe una
    copia volteada de la lamina en trabajo/ y se la pasa a comparar() cambiando, solo mientras dura, la ruta de la
    lamina y la caja de su contorno. La de tres cuartos no se voltea: la camara de la FICHA ya mira desde la
    derecha."""
    import construir_arma as ca
    import laminas
    from PIL import Image, ImageOps
    nombre = FICHA["nombre"]
    _ruta, ficha = ca.cargar_ficha(nombre)
    _ac, _salida, trabajo, rev = ca.carpetas(nombre, revision)
    original = laminas.ruta_lamina(FICHA["lamina"], "lateral")
    volteada = os.path.join(trabajo, nombre + "_lamina_lateral_volteada.png")
    im = Image.open(original)
    ImageOps.mirror(im.convert("RGB")).save(volteada)
    ancho = im.width
    ruta_lamina, leer_json = laminas.ruta_lamina, ca.leer_json

    def ruta_lamina_v(lamina, vista=None):
        return volteada if vista == "lateral" else ruta_lamina(lamina, vista)

    def leer_json_v(ruta):
        d = leer_json(ruta)
        if ruta.endswith("_lamina_lateral.json"):
            x0, y0, x1, y1 = d["caja"]
            d = dict(d, caja=[ancho - 1 - x1, y0, ancho - 1 - x0, y1])
        return d

    laminas.ruta_lamina, ca.leer_json = ruta_lamina_v, leer_json_v
    try:
        return ca.comparar(nombre, ficha, trabajo, rev)
    finally:
        laminas.ruta_lamina, ca.leer_json = ruta_lamina, leer_json


def main():
    import argparse
    sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
    import construir_arma as ca
    p = argparse.ArgumentParser(description="Construye gd_escopeta_yuxtapuesta con construir_arma.py y hace la "
                                            "comparacion con la lamina volteada (boca a la derecha).")
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
        ruta, i_a, i_i = _comparar_volteada(a.revision)
        print(f"-- comparacion (lamina volteada): {ruta} (IoU {i_a:.3f} ajustada, {i_i:.3f} por el largo)")
    return 0


if __name__ == "__main__":
    sys.exit(main())
