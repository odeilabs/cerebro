"""gd_colt_saa: Colt Single Action Army de 5 1/2 pulgadas (el modelo "Artillery"), el revolver de Ze Dourado, el
capataz del garimpo de Basiliskos (y el de cualquier juego de la saga que necesite un revolver de accion simple).
Lamina 008-colt-single-action-army.

Medidas reales: 279 mm de largo (11"), canon de 139,7 mm (5 1/2", medido desde la cara del tambor), tambor de
42,2 mm de diametro y 41 mm de largo, canon de 17,8 mm de grueso, unos 128 mm de alto (de la mira al pie de la
culata). La lamina (generada) NO esta estirada: el tambor mide 235 x 240 px (41 x 42 mm) y de la cara del tambor a la
cola de la culata hay 793 px, que a esa escala son los 139,7 mm reales. Lo que no casa es el canon: en la lamina mide
unos 6,4" (entre los 5 1/2 y los 7 1/2 de serie), asi que el canon, la mira y el tubo del expulsor se modelan a mano con
el largo real y se colocan midiendo desde la boca (xb), y el resto sale de la silueta con una sola escala.

La lamina lateral tiene la boca a la DERECHA (ensena el lado derecho, +Z: el del tubo del expulsor): la calibracion
lleva boca_izquierda=False. El render de lado del kit es del lado izquierdo, asi que la comparacion se hace con la
lamina reflejada y con su canon encogido al de 5 1/2" (lo que al kit le falta; ver comparar_espejo abajo). La receta:

    python construir_arma.py gd_colt_saa --res 1024 --solo laminas,modelo,texturas,agarre
    python modelos/gd_colt_saa.py comparar [--revision <carpeta>]

Piezas: armazon (silueta de 26 mm con la correa de arriba redondeada, la ventana del tambor de lados rectos, que lo
deja ver entero, y la ranura del alza), escudo de retroceso (la cupula detras del tambor), martillo con la cresta
rayada, tambor de 6 recamaras con 6 estrias y las muescas del retenedor, canon con su corona, punto de mira de media
luna, tubo del expulsor con su ranura, la cabeza de la varilla, el tornillo y el taco, perno del tambor, tornillos del
armazon, puente y arco del guardamonte, gatillo, lomo (correas y pie de la culata), cachas de nogal de una pieza y su
tornillo con arandela. El lomo y las cachas, redondeados con `redondeada` (el bisel del kit no da para ellos).

Las coordenadas de la lamina se leen con
    python laminas.py rejilla 008/lateral rejilla.png --paso 10 --recorte 400,80,960,460 --zoom 2
"""
import math
import os
import sys

FICHA = {
    "nombre": "gd_colt_saa",
    "clase": "Pistola",
    "lamina": "008-colt-single-action-army",
    "para": "Ze Dourado, el capataz del garimpo de Basiliskos; cualquier revolver de accion simple de la saga",
    "medidas_reales": {"largo": 0.2794, "canon": 0.1397, "alto": 0.128, "tambor_diametro": 0.0422,
                       "tambor_largo": 0.041, "canon_diametro": 0.0178, "ancho_culata": 0.034},
    "texturas": {"res": 1024, "escala": 0.4},   # 0,4 m por tesela: la densidad de todas las gd_
    # la lamina de tres cuartos es del lado derecho y por delante (la boca a la derecha): azimut de mas de 90
    "vista_34": {"azimut": 158.0, "elevacion": 10.0, "focal": 100.0},
    "semilla": 1873,
}

# ------------------------------------------------------------------ medidas de la lamina lateral (pixeles)
PX_TAMBOR = 807          # cara de delante del tambor: el cero de x
PX_COLA = 14             # cola de la culata (lo mas atrasado del arma)
PX_BOCA = 1737           # boca en la lamina (el canon de la lamina es ~1" mas largo que el real)
PX_FRENTE = 912          # cara de delante del armazon, donde sale el canon
PY_EJE = 175.5           # eje del canon: el cero de y
CANON = 139.7            # mm, de la cara del tambor a la boca (5 1/2")
TAMBOR = (41.0, 21.1)    # mm: largo y radio
Y_TAMBOR = -13.2         # mm: el eje del tambor bajo el del canon (circulo de recamaras de 13,2 mm)

# El armazon (acero cementado): de la lamina, sin el martillo, hasta la linea de abajo del armazon.
R_ARMAZON = [(441, 152), (459, 149), (475, 144), (509, 119), (520, 116), (524, 90), (912, 90), (912, 404),
             (861, 402), (431, 443), (431, 292), (441, 292)]
# El martillo: la cresta y el cuerpo que asoma detras del escudo; por delante se mete en el armazon.
R_MARTILLO = [(330, 86), (520, 86), (522, 104), (515, 112), (505, 126), (490, 140), (470, 160), (470, 215),
              (446, 215), (446, 289), (412, 289), (395, 250), (330, 150)]
# El lomo (pavonado): la correa de atras, la de delante y el pie de la culata, del contorno de la lamina; las cachas lo
# tapan por los lados. Arriba, detras del martillo, el lomo se ensancha (LOMO_ALTO) hasta el ancho de las cachas.
LOMO = [(431, 289), (424, 289), (416, 294.71), (402.69, 308), (388, 331.97), (331, 338.9), (301, 346.83),
        (268, 359.77), (239, 375.88), (212, 394.87), (191.07, 412), (171.67, 433), (149.17, 461), (120.36, 509),
        (71.9, 620), (9, 787), (280, 797.05), (321, 796), (308.67, 720), (304.51, 682), (305.63, 625),
        (316.88, 577), (329.88, 551), (345.57, 529), (368, 510.53), (386, 501.56), (417, 493.69), (431, 493.72)]
LOMO_ALTO = [(431, 289), (424, 289), (416, 294.71), (402.69, 308), (388, 331.97), (331, 338.9), (301, 346.83),
             (300, 367), (431, 367)]
# El puente del guardamonte (bajo el armazon) y el arco.
R_PUENTE = [(431, 443), (861, 402), (912, 402), (912, 445), (700, 446), (600, 452), (540, 457), (500, 467),
            (470, 480), (431, 480)]
# El arco: un tubo de seccion eliptica (2,9 x 8 mm) por la linea media del arco de la lamina, y sus dos uniones (la de
# delante con el puente y la de atras con la correa de delante de la culata), de la silueta.
ARCO = [(677.6, 470), (680.3, 500), (681.5, 521), (676.3, 538), (667, 553), (650, 569.5), (631, 577.8), (600, 582),
        (575, 585.4), (549, 585.4), (533, 581.7), (516, 573), (503, 562), (494.5, 547), (487, 530), (481, 515)]
R_ARCO_DELANTE = [(668, 436), (740, 436), (740, 478), (668, 478)]
R_ARCO_DETRAS = [(431, 465), (530, 452), (530, 518), (431, 518)]
# El hueco entero del guardamonte (la silueta lo deja partido por el gatillo).
HUECO = [(616, 445.5), (634, 447), (650, 454), (661, 464), (668, 477), (672, 492), (674, 521), (672, 532),
         (664, 548), (650, 561), (631, 570), (562, 579), (539, 576), (523, 569), (512, 562), (502, 547),
         (494, 525), (492.5, 508), (496, 493), (505, 476), (515, 463), (535, 455), (556, 450), (590, 446)]
GATILLO = [(519, 474), (518.7, 512), (522.9, 527), (532, 544.2), (549, 562.3), (564, 569.2), (567.9, 567),
           (568.9, 563), (547.3, 538), (540.2, 521), (537.6, 506), (539.9, 485), (551.7, 456), (556, 446),
           (540, 438), (522, 442)]
# Las cachas de nogal (una pieza): la culata metida ~10 px por detras, delante y abajo (ahi asoma el lomo).
CACHAS = [(432, 363), (432, 482), (410, 484), (386, 491), (362, 501), (338, 521), (321, 545), (307, 573),
          (296, 625), (295, 682), (307, 788), (31, 783), (82, 623), (129, 514), (157, 467), (179, 440),
          (198, 420), (218, 403), (244, 385), (272, 369), (290, 363)]
# Punto de mira (media luna), en pixeles de la lamina; se coloca midiendo desde la boca.
MIRA = [(1604, 129), (1606, 105), (1609, 89), (1612, 81), (1618, 74.8), (1628, 72.5), (1642, 75.3), (1658, 82),
        (1670, 90.6), (1680, 105), (1687, 124), (1690, 129)]
# El borde de arriba de la cresta del martillo, donde va el rayado.
CRESTA = [(343, 97.5), (354, 95.5), (368, 97), (382, 105), (395, 120), (403, 128)]


def _suave(t):
    """smoothstep de t recortado a [0, 1]."""
    t = min(1.0, max(0.0, t))
    return t * t * (3.0 - 2.0 * t)


def redondeada(k, contorno, mitad, radio, peso, material, nombre, anillos=3, paso=0.006, suavizado=1):
    """Lo que al kit le falta: una pieza lateral (contorno en el plano xy, en metros) de grosor +-mitad(y) con los
    cantos redondeados con un cuarto de circulo de `radio`, como una cacha o un lomo. k.biselar lleva clamp (el ancho
    baja al que cabe en el lado mas corto, y un contorno sacado de la lamina tiene lados de 1 mm: el bisel se queda en
    nada) y no deja cantos vivos en una parte y redondos en otra. Aqui el redondeo de cada punto del contorno va por
    peso(x, y) (0 vivo, contra el armazon; 1 redondo). El contorno se remuestrea cada `paso` y se suaviza (Chaikin)
    para que el canto no tenga quiebros; las esquinas, redondeadas antes (k.redondear) con un radio mayor que `radio`,
    o el canto se pliega. mitad(y) tiene que ser lineal en y (o constante): asi las tapas son planas y el n-gono se
    triangula sin pliegues. Se crea con k.caja (asi entra en las piezas del kit) y se le cambia la malla."""
    import bmesh
    # remuestreo y suavizado
    p = [tuple(map(float, q)) for q in contorno]
    q = []
    for i, a in enumerate(p):
        b = p[(i + 1) % len(p)]
        n = max(1, int(math.ceil(math.hypot(b[0] - a[0], b[1] - a[1]) / paso)))
        q += [(a[0] + (b[0] - a[0]) * j / n, a[1] + (b[1] - a[1]) * j / n) for j in range(n)]
    for _ in range(suavizado):
        r = []
        for i, a in enumerate(q):
            b = q[(i + 1) % len(q)]
            r += [(0.75 * a[0] + 0.25 * b[0], 0.75 * a[1] + 0.25 * b[1]),
                  (0.25 * a[0] + 0.75 * b[0], 0.25 * a[1] + 0.75 * b[1])]
        q = r
    area = sum(q[i][0] * q[(i + 1) % len(q)][1] - q[(i + 1) % len(q)][0] * q[i][1] for i in range(len(q)))
    if area < 0:
        q.reverse()
    n = len(q)
    # hacia dentro de cada punto (inglete de las normales izquierdas de sus dos lados: el contorno va antihorario)
    dentro = []
    for i in range(n):
        a, b, c = q[i - 1], q[i], q[(i + 1) % n]
        n1 = (-(b[1] - a[1]), b[0] - a[0])
        n2 = (-(c[1] - b[1]), c[0] - b[0])
        l1, l2 = math.hypot(*n1) or 1.0, math.hypot(*n2) or 1.0
        m = (n1[0] / l1 + n2[0] / l2, n1[1] / l1 + n2[1] / l2)
        lm = math.hypot(*m) or 1.0
        m = (m[0] / lm, m[1] / lm)
        cos = max(0.4, m[0] * n1[0] / l1 + m[1] * n1[1] / l1)
        dentro.append((m[0] / cos, m[1] / cos))
    pesos = [peso(x, y) for x, y in q]
    bm = bmesh.new()
    lados = {}
    for s in (1, -1):
        lados[s] = []
        for j in range(anillos + 1):
            t = 0.5 * math.pi * j / anillos
            anillo = []
            for (x, y), (dx, dy), w in zip(q, dentro, pesos):
                d = radio * w * (1.0 - math.cos(t))
                z = mitad(y + dy * d) - radio * w * (1.0 - math.sin(t))
                anillo.append(bm.verts.new((x + dx * d, y + dy * d, s * z)))
            lados[s].append(anillo)
    for i in range(n):
        j = (i + 1) % n
        bm.faces.new((lados[1][0][i], lados[1][0][j], lados[-1][0][j], lados[-1][0][i]))
        for s in (1, -1):
            for a in range(anillos):
                A, B = lados[s][a], lados[s][a + 1]
                bm.faces.new((A[i], A[j], B[j], B[i]))
    tapas = [bm.faces.new(lados[1][-1]), bm.faces.new(list(reversed(lados[-1][-1])))]
    bmesh.ops.triangulate(bm, faces=tapas)
    bmesh.ops.remove_doubles(bm, verts=bm.verts[:], dist=1e-6)
    bmesh.ops.dissolve_degenerate(bm, edges=bm.edges[:], dist=1e-6)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
    ob = k.caja((0, 0, 0), (0.001, 0.001, 0.001), material=material, nombre=nombre)
    bm.to_mesh(ob.data)
    bm.free()
    ob.data.update()
    return ob


def modelar(k):
    mm = k.mm
    # Pixeles de colt-single-action-army_lateral.png -> metros. Una sola escala (la lamina no esta estirada): de la
    # cara del tambor (807) a la cola de la culata (14) hay 139,7 mm (11" de largo menos 5 1/2" de canon). y: el eje
    # del canon.
    cal = k.Calibracion(px_boca=PX_TAMBOR, px_cola=PX_COLA, largo=mm(CANON), py_eje=PY_EJE, boca_izquierda=False)
    xboca = mm(CANON)

    def xb(px):
        """Lo del canon se mide desde la boca: el canon de la lamina es mas largo que el real."""
        return xboca - cal.dx(PX_BOCA - px)

    yt = Y_TAMBOR * 0.001
    largo_t, radio_t = mm(TAMBOR[0]), mm(TAMBOR[1])

    # Las recetas (probadas con texturizar_obj.py en el scratchpad): 'pavonado' y 'acero' van a la de metal, que
    # tine el Kd solo al 0,5 y no baja de un gris acero de ~135 sRGB por oscuro que sea el Kd (medido: con
    # "acero_cementado" y Kd #2a2426 el armazon salia a (137, 138, 138), plateado como un niquelado al lado del canon
    # negro). En la lamina armazon y canon son oscuros y brillantes. Por eso:
    #  - el pavonado azul negro (canon, tambor, tubo del expulsor, guardamonte, lomo, gatillo) lleva "pintura_coche"
    #    -> pintura_coche: el Kd liso y con brillo (rugosidad 0,38) y una capa de laca, como el pavonado de las
    #    escopetas y del Remington (revision de conjunto: con pintura_mate salia un negro azulado mate, de pintura de
    #    imprimacion, al lado del pavonado brillante de las armas largas). La de plastico sale granulada como un
    #    polimero y la de cascarilla, con motas blancas;
    #  - el cementado (armazon, escudo, martillo, perno y tornillos), tambien pintura_coche, con el gris pardo del
    #    temple jaspeado de la lamina (su media: lateral (55, 59, 68) y (48, 54, 62), detalle de la empunadura
    #    (77, 73, 72) y (93, 85, 85)): mas claro y mas calido que el pavonado, que es azul negro (37, 42, 53), y con
    #    el mismo brillo. Sin los colores del jaspeado (las recetas no los hacen);
    #  - las cachas, "madera_nogal" -> madera tenida al 0,85, con la fibra a lo largo de la culata.
    pavonado = k.material("pintura_coche_pavonado", "#252a35", rugosidad=0.38, metal=0.0)
    cementado = k.material("pintura_coche_cementado", "#433e40", rugosidad=0.38, metal=0.0)
    nogal = k.material("madera_nogal_cachas", "#6a3820", rugosidad=0.55, metal=0.0, veta=(0.49, 0.87, 0.0))

    # ---------------------------------------------------------------- armazon
    armazon = k.silueta(cal, region=R_ARMAZON, ancho=mm(26), material=cementado, nombre="armazon")
    # La seccion: paredes de 26 mm y la correa de arriba redondeada, de 14 mm.
    ya = cal.y(97) + mm(0.5)
    seccion = k.redondear([(-mm(13), -mm(70)), (mm(13), -mm(70)), (mm(13), mm(2)), (mm(7), ya), (-mm(7), ya),
                           (-mm(13), mm(2))], {2: mm(8), 3: mm(3), 4: mm(3), 5: mm(8)}, 4)
    k.intersecar(armazon, k.perfil(seccion, plano="zy", desde=cal.x(420), hasta=cal.x(930)))
    # La ventana del tambor, de lados rectos y de lado a lado: el tambor (42,2 mm) es mas ancho que el armazon (26) y
    # de lado se ve entero, entre la correa de arriba y el armazon de abajo (en la lamina la ventana va de py 127 a
    # 367: el alto del tambor). Cortada con el volumen del tambor, las paredes le tapaban ~3,5 mm arriba y abajo y la
    # correa salia de 8,8 mm en vez de 5,3. Arriba y abajo se mete 0,1 mm en el tambor y por delante y por detras
    # deja 0,15 y 0,3 mm de holgura (con el escudo a 0,15): asi no queda una rendija por la que se vea el fondo.
    # Y la ranura del alza a lo largo de la correa.
    k.restar(armazon, k.unir(
        k.caja((-largo_t - mm(0.3), yt - radio_t + mm(0.1), -mm(20)), (mm(0.15), yt + radio_t - mm(0.1), mm(20))),
        k.caja((cal.x(526), cal.y(97) - mm(1.4), -mm(1.1)), (cal.x(690), mm(20), mm(1.1)))))
    k.biselar(armazon, mm(1.0), 2, 30)

    # El escudo de retroceso: la cupula detras del tambor, tan ancha como el.
    escudo = k.torno([(-mm(64), 0), (-mm(63.4), mm(5)), (-mm(62), mm(9.5)), (-mm(59.5), mm(13.4)),
                      (-mm(56), mm(16.8)), (-mm(52), mm(19.2)), (-mm(48), mm(20.6)), (-mm(44.5), radio_t),
                      (-largo_t - mm(0.15), radio_t), (-largo_t - mm(0.15), 0)],
                     eje="x", origen=(0, yt, 0), segmentos=40, material=cementado, nombre="escudo")
    k.biselar(escudo, mm(0.8), 2, 30)

    # ---------------------------------------------------------------- martillo (con el rayado de la cresta)
    martillo = k.silueta(cal, region=R_MARTILLO, ancho=mm(8.5), material=cementado, nombre="martillo")
    k.biselar(martillo, mm(1.0), 2, 30)
    rayas = []
    pts = cal.puntos(CRESTA)
    for i in range(1, 6):
        t = i / 6.0
        j = min(int(t * (len(pts) - 1)), len(pts) - 2)
        f = t * (len(pts) - 1) - j
        x = pts[j][0] + (pts[j + 1][0] - pts[j][0]) * f
        y = pts[j][1] + (pts[j + 1][1] - pts[j][1]) * f
        ang = math.degrees(math.atan2(pts[j + 1][1] - pts[j][1], pts[j + 1][0] - pts[j][0]))
        raya = k.caja((x - mm(0.35), y - mm(0.7), -mm(6)), (x + mm(0.35), y + mm(1.5), mm(6)))
        rayas.append(k.girar(raya, ang, "z", (x, y, 0)))
    k.restar(martillo, k.unir(*rayas))

    # ---------------------------------------------------------------- tambor
    tambor = k.torno([(-largo_t, 0), (-largo_t, radio_t - mm(0.8)), (-largo_t + mm(0.8), radio_t),
                      (-mm(0.7), radio_t), (0, radio_t - mm(0.7)), (0, 0)],
                     eje="x", origen=(0, yt, 0), segmentos=48, material=pavonado, nombre="tambor")
    # Los huecos en cuatro tandas de piezas que no se tocan (una herramienta que se corta a si misma deja vacia la
    # booleana): recamaras y eje, estrias, muescas del retenedor y sus rampas.
    recamaras, estrias, muescas, rampas = [k.cilindro((mm(0.5), yt, 0), (-mm(6), yt, 0), mm(3.4), 12)], [], [], []
    for i in range(6):
        a = math.radians(60 * i)
        cy, cz = yt + mm(13.2) * math.cos(a), mm(13.2) * math.sin(a)
        recamaras.append(k.cilindro((mm(0.5), cy, cz), (-mm(12), cy, cz), mm(5.85), 16))
        # estria entre recamaras: un canal de 10 mm con los extremos redondos
        b = math.radians(60 * i + 30)
        ey, ez = yt + (radio_t + mm(0.9)) * math.cos(b), (radio_t + mm(0.9)) * math.sin(b)
        perf = [(-mm(26.5), 0)] + [(-mm(26.5) + mm(5) - mm(5) * math.cos(math.radians(g)),
                                    mm(5) * math.sin(math.radians(g))) for g in (30, 60, 90)]
        perf += [(-mm(2.0) - mm(5) + mm(5) * math.cos(math.radians(g)), mm(5) * math.sin(math.radians(g)))
                 for g in (90, 60, 30)] + [(-mm(2.0), 0)]
        estrias.append(k.torno(perf, eje="x", origen=(0, ey, ez), segmentos=12))
        # muesca del retenedor (con su rampa) detras de las estrias, alineada con la recamara
        muesca = k.caja((-mm(33), radio_t - mm(1.3), -mm(1.9)), (-mm(28.5), radio_t + mm(1), mm(1.9)))
        rampa = k.caja((-mm(31.6), radio_t - mm(0.75), -mm(7.5)), (-mm(29.8), radio_t + mm(1), -mm(1.5)))
        for h, lista in ((muesca, muescas), (rampa, rampas)):
            k.mover(h, (0, yt, 0))
            lista.append(k.girar(h, 60 * i, "x", (0, yt, 0)))
    for tanda in (recamaras, estrias, muescas, rampas):
        k.restar(tambor, k.unir(*tanda))

    # ---------------------------------------------------------------- canon, mira y expulsor (medidos desde la boca)
    k.torno([(mm(12), 0), (mm(12), mm(8.9)), (xboca - mm(0.8), mm(8.9)), (xboca, mm(8.2)), (xboca, mm(6.5)),
             (xboca - mm(0.7), mm(5.75)), (xboca - mm(16), mm(5.75)), (xboca - mm(16), 0)],
            eje="x", segmentos=32, material=pavonado, nombre="canon")
    k.perfil([(xb(px), cal.y(py)) for px, py in MIRA], ancho=mm(3.0), material=pavonado, bisel=mm(0.4),
             nombre="punto_mira")
    # Tubo del expulsor: al lado derecho del canon (+Z), un poco por debajo; acaba ~14 mm antes de la boca.
    ye, ze, re = -mm(9.0), mm(8.5), mm(6.2)
    xf = xboca - mm(13.7)
    tubo = k.torno([(cal.x(905), 0), (cal.x(905), re), (xf - mm(2.6), re), (xf - mm(1.1), mm(5.2)),
                    (xf - mm(0.2), mm(3.4)), (xf, 0)], eje="x", origen=(0, ye, ze), segmentos=20,
                   material=pavonado, nombre="tubo_expulsor")
    k.restar(tubo, k.caja((mm(34), ye - mm(1.5), ze + mm(4.2)), (xf - mm(9.5), ye + mm(1.5), ze + mm(8))))
    k.biselar(tubo, mm(0.4), 1, 40)
    # La cabeza de la varilla (en su ranura, al final) y el tornillo del tubo, y el taco de debajo.
    k.cilindro((xf - mm(13), ye - mm(0.4), ze + mm(2)), (xf - mm(13), ye - mm(0.4), ze + mm(7.4)), mm(2.9), 12,
               material=pavonado, bisel=mm(0.4), nombre="cabeza_expulsor")
    tornillo_tubo = k.cilindro((xf - mm(5.2), ye, ze + mm(4)), (xf - mm(5.2), ye, ze + mm(6.55)), mm(2.2), 10,
                               material=pavonado, nombre="tornillo_tubo")
    k.restar(tornillo_tubo, k.caja((xf - mm(7.6), ye - mm(0.3), ze + mm(6.1)),
                                   (xf - mm(2.8), ye + mm(0.3), ze + mm(7))))
    k.caja((xf - mm(17.3), cal.y(281), ze - mm(2.6)), (xf - mm(9.7), ye - mm(4.5), ze + mm(2.6)), material=pavonado,
           bisel=mm(0.8), nombre="taco_expulsor")

    # ---------------------------------------------------------------- perno del tambor y tornillos del armazon
    k.cilindro((cal.x(905), yt, 0), (cal.x(948), yt, 0), mm(4.2), 16, material=cementado, bisel=mm(0.5),
               nombre="perno_tambor")

    def tornillo(px, py, r, z, ranura=True):
        x, y = cal.p(px, py)
        t = k.cilindro((x, y, -z), (x, y, z), r, 10, material=cementado, nombre="tornillo")
        if ranura:
            ang = k.azar.uniform(0, 180)
            cortes = []
            for s in (1, -1):
                corte = k.caja((x - r * 1.3, y - mm(0.3), s * (z - mm(0.45))),
                               (x + r * 1.3, y + mm(0.3), s * (z + mm(1))))
                cortes.append(k.girar(corte, ang, "z", (x, y, 0)))
            k.restar(t, k.unir(*cortes))
        return t

    tornillo(862, 282, mm(2.3), mm(13.5))           # el del perno del tambor
    tornillo(465, 365, mm(2.9), mm(13.5))           # el del martillo
    tornillo(550, 405, mm(2.7), mm(13.5))           # el del retenedor
    tornillo(575, 391, mm(1.6), mm(13.4), False)    # el pasador del gatillo

    # ---------------------------------------------------------------- guardamonte, gatillo y lomo (pavonados)
    puente = k.silueta(cal, region=R_PUENTE, ancho=mm(20), material=pavonado, nombre="puente_guardamonte")
    k.restar(puente, k.perfil(cal.puntos(HUECO), ancho=mm(40)))
    k.biselar(puente, mm(1.2), 2, 30)
    for region in (R_ARCO_DELANTE, R_ARCO_DETRAS):
        union = k.silueta(cal, region=region, ancho=mm(8.4), material=pavonado, nombre="guardamonte")
        k.restar(union, k.perfil(cal.puntos(HUECO), ancho=mm(40)))
        k.biselar(union, mm(1.2), 2, 30)
    arco = k.barrido([cal.p(px, py, 0.0) for px, py in ARCO], mm(1.45), 12, material=pavonado, nombre="guardamonte")
    k.escalar(arco, (1.0, 1.0, 2.75))
    k.perfil(cal.puntos(GATILLO), ancho=mm(5.5), material=pavonado, bisel=mm(0.9), nombre="gatillo")
    # El lomo y las cachas, redondeados con `redondeada` (el bisel del kit lleva clamp: en un contorno con lados de
    # 1 mm se queda en nada). Vivos donde tocan el armazon, el guardamonte o el martillo (peso 0).
    def peso_px(px0=None, py0=None, ancho=20.0):
        def f(x, y):
            px, py = PX_TAMBOR + x / cal.sx, PY_EJE - y / cal.sy
            w = 1.0
            if px0 is not None:
                w *= _suave((px0 - px) / ancho)
            if py0 is not None:
                w *= _suave((py - py0) / ancho)
            return w
        return f

    # en las dos esquinas del pie el redondeo baja a la mitad (no cabe entero: el talon marca el largo del arma)
    esquinas = (cal.p(9, 787), cal.p(321, 796))

    def peso_lomo(x, y):
        w = peso_px(430, 290, 12)(x, y)
        for ex, ey in esquinas:
            w *= 0.5 + 0.5 * _suave(math.hypot(x - ex, y - ey) / mm(8))
        return w

    redondeada(k, k.redondear(cal.puntos(LOMO), {15: mm(3), 17: mm(4)}, 4), lambda y: mm(6), mm(5), peso_lomo,
               pavonado, "lomo", anillos=2, paso=0.008)
    redondeada(k, cal.puntos(LOMO_ALTO), lambda y: mm(13.2), mm(3),
               lambda x, y: peso_px(430, 290, 12)(x, y) * _suave((cal.y(366) - y) / mm(1.5)), pavonado, "lomo_alto")

    # ---------------------------------------------------------------- cachas de nogal
    # Se abren hacia abajo: 27 mm arriba, contra el armazon, y 33 en el pie (lineal en y: tapas planas).
    def mitad_cachas(y):
        return mm(13.5) + mm(3.0) * (cal.y(363) - y) / cal.dy(785 - 363)

    contorno = k.redondear(cal.puntos(CACHAS), {1: mm(3), 10: mm(12), 11: mm(10)}, 5)
    redondeada(k, contorno, mitad_cachas, mm(7.5), peso_px(431, 364, 20), nogal, "cachas")
    # El tornillo de las cachas con su arandela, a los dos lados, en la cara de la cacha.
    x, y = cal.p(205, 566)
    h = mitad_cachas(y)
    for s in (1, -1):
        k.cilindro((x, y, s * (h - mm(0.6))), (x, y, s * (h + mm(0.35))), mm(3.6), 12, material=cementado,
                   nombre="arandela")
        cabeza = k.cilindro((x, y, s * (h - mm(0.4))), (x, y, s * (h + mm(0.6))), mm(2.5), 10, material=cementado,
                            nombre="tornillo_cachas")
        k.restar(cabeza, k.girar(k.caja((x - mm(3), y - mm(0.3), s * (h + mm(0.1))),
                                        (x + mm(3), y + mm(0.3), s * (h + mm(1.2)))),
                                 k.azar.uniform(0, 180), "z", (x, y, 0)))

    # El agarre: el centro de la culata a la altura del dedo corazon (justo bajo el arco del guardamonte).
    return {"agarre": cal.p(231, 525), "boca": (xboca, 0.0, 0.0), "guardamanos": None, "calibracion": cal}


# ------------------------------------------------------------------ comparacion con la lamina reflejada
def comparar_espejo(revision=None):
    """El _comparar.png del kit con la lamina lateral reflejada: el kit renderiza el lado izquierdo (boca a la
    izquierda) y la lamina del Colt ensena el derecho. Reutiliza construir_arma.comparar tal cual, dandole los
    contornos y la silueta de la lamina reflejada (en trabajo/espejo). La de tres cuartos no se refleja: la camara de
    la FICHA ya es la del lado derecho."""
    import json
    import shutil
    from PIL import Image
    aqui = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
    sys.path.insert(0, aqui)
    import construir_arma as ca
    import laminas
    nombre = FICHA["nombre"]
    _ac, _salida, trabajo, rev = ca.carpetas(nombre, revision)
    espejo = os.path.join(trabajo, "espejo")
    os.makedirs(espejo, exist_ok=True)
    original = laminas.ruta_lamina
    ruta = original(FICHA["lamina"], "lateral")
    reflejada = os.path.join(espejo, os.path.basename(ruta))
    # El canon de la lamina (de la cara del armazon a la boca) se encoge al de 5 1/2": asi la IoU mide la forma del
    # arma y no la diferencia de largo del canon, que es a proposito.
    im = Image.open(ruta).convert("RGB")
    sx = CANON / (PX_TAMBOR - PX_COLA)
    encoge = (CANON - (PX_FRENTE - PX_TAMBOR) * sx) / ((PX_BOCA - PX_FRENTE) * sx)
    resto = im.crop((PX_FRENTE, 0, im.width, im.height))
    resto = resto.resize((round(resto.width * encoge), im.height), Image.LANCZOS)
    nueva = Image.new("RGB", (PX_FRENTE + resto.width, im.height))
    nueva.paste(im.crop((0, 0, PX_FRENTE, im.height)), (0, 0))
    nueva.paste(resto, (PX_FRENTE, 0))
    nueva.transpose(Image.FLIP_LEFT_RIGHT).save(reflejada)
    print(f"   lamina reflejada y con el canon encogido x{encoge:.3f} "
          f"(de {(PX_BOCA - PX_TAMBOR) * sx:.1f} a {CANON} mm)")
    d = laminas.datos_lamina(reflejada, FICHA.get("umbral", 22.0), FICHA.get("tolerancia", 1.5),
                             FICHA.get("suavizado", 2.0))
    with open(os.path.join(espejo, f"{nombre}_lamina_lateral.json"), "w", encoding="utf-8") as f:
        json.dump(d, f)
    for n in (f"{nombre}_lamina_tres_cuartos.json", f"{nombre}_medidas.json"):
        shutil.copy(os.path.join(trabajo, n), os.path.join(espejo, n))
    laminas.ruta_lamina = lambda lamina, vista=None: reflejada if vista == "lateral" else original(lamina, vista)
    try:
        return ca.comparar(nombre, dict(FICHA), espejo, rev)
    finally:
        laminas.ruta_lamina = original


if __name__ == "__main__":
    import argparse
    p = argparse.ArgumentParser(description="Comparacion del Colt con su lamina reflejada")
    p.add_argument("accion", choices=("comparar",))
    p.add_argument("--revision", default=None)
    a = p.parse_args()
    r, i_a, i_i = comparar_espejo(a.revision)
    print(f"-- comparacion (lamina reflejada): {r} (IoU {i_a:.3f} ajustada, {i_i:.3f} por el largo)")
