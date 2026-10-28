"""gd_ak47: fusil de asalto Kalashnikov AK-47 (7,62 x 39 mm, culata fija de madera, cargador curvo de 30), el de los
mercenarios de Ferrox y de las Comadrejas. Sustituye al kk_ak47 de cajas en Basiliskos; Gorilla Revenge puede traerlo.
Lamina 021-ak-47.

Medidas reales: 870 mm de largo (con la tuerca de la boca), canon de 415 mm (de la recamara a la boca: asoma ~390 por
delante del cajon), unos 30 mm de ancho de cajon, culata de ~42 mm en la cantonera. La lamina cuadra con una sola
escala (870 mm de la cantonera a la tuerca de la boca = 0,414 mm/px): los remaches salen redondos, el canon asoma
392 mm y sale de 15-16 mm de grueso, el cajon 66 mm de alto y la mira delantera 55 mm sobre el eje, asi que no esta
estirada. Alto sin cargador: 190 mm (de lo alto de la mira delantera al pie del pistolete).

LO QUE NO ESTA EN LA LAMINA: la lamina ensena el fusil SIN CARGADOR (el brocal vacio). El modelo lo lleva: el cargador
de chapa de 30 es la pieza que mas identifica a un AK y el kk_ak47 al que sustituye lo llevaba. Es un arco de 320 mm de
radio en la cara de atras y 67 mm de fondo que baja 185 mm por debajo del cajon y avanza 70 (alto total con cargador:
~275 mm). Con GD_AK47_CARGADOR=no en el entorno sale sin el: es lo que hace la segunda comparacion
(<revision>/gd_ak47_comparar_sin_cargador.png, modelada aparte en trabajo/sin_cargador sin tocar salida/), la que mide
la IoU de siluetas con la lamina; en la normal (gd_ak47_comparar.png, con cargador) la IoU «ajustada a la caja» no vale,
porque estira la lamina hasta el pie del cargador.

La lamina lateral tiene la BOCA A LA DERECHA (ensena el lado derecho, +Z, con el selector y la palanca de montar):
Calibracion(boca_izquierda=False), y la comparacion del kit (que no voltea la lamina) se hace desde este fichero con la
lamina volteada (ver abajo). La de tres cuartos tambien es del lado derecho y por delante: azimut de mas de 90.

Lo que al kit le falta y se hace aqui: un solido por secciones (_seccion, _solido: la culata y el guardamanos de abajo
son anillos de superelipses a lo largo de X, no extrusiones), el cargador curvo (_cargador) y la comparacion con una
lamina de boca a la derecha (_comparar_volteada, la misma idea que gd_escopeta_yuxtapuesta).

Las coordenadas de la lamina se leen con
    python laminas.py rejilla 021-ak-47/lateral rejilla.png --paso 10 --recorte 940,120,1320,360 --zoom 3
Construirla entera (modelo, texturas, agarre y las dos comparaciones volteadas, con y sin cargador):
    python modelos/gd_ak47.py [--res 1024] [--solo modelo,comparar] [--revision <carpeta>]
(construir_arma.py gd_ak47 deja el mismo modelo y las mismas texturas; solo su comparacion sale con la lamina al reves).
GD_AK47_DEPURAR=1 escribe en el log los triangulos de cada pieza (el presupuesto: 2.000-12.000; sale en ~11.500).
"""
import math
import os
import sys

FICHA = {
    "nombre": "gd_ak47",
    "clase": "FusilAsalto",
    "lamina": "021-ak-47",
    "para": "mercenarios de Ferrox y Comadrejas (sustituye a kk_ak47 en Basiliskos; Gorilla Revenge puede traerlo)",
    "medidas_reales": {"largo": 0.870, "canon": 0.415, "ancho_cajon": 0.030, "ancho_culata": 0.042,
                       "alto_sin_cargador": 0.190, "alto_con_cargador": 0.275},
    "texturas": {"res": 1024, "escala": 0.4},   # 0,4 m por tesela: la densidad de todas las gd_
    # la lamina de tres cuartos mira desde delante y a la derecha (+Z), casi a la altura del arma (la caida de la
    # boca en la imagen sale igual con 6 grados de elevacion)
    "vista_34": {"azimut": 125.0, "elevacion": 6.0, "focal": 70.0},
    "semilla": 47,
    # la lamina de tres cuartos tiene sombra en el suelo: con 40 el contorno se queda en el arma (la lateral sale igual)
    "umbral": 40.0,
}

# Pixeles de la lamina lateral (ak-47_lateral.png, 2172 x 724, boca a la derecha)
PX_BOCA, PX_CULATA, PY_EJE = 2137, 36, 255      # la cara de la tuerca de la boca, la cantonera y el eje del canon
LARGO = 0.870


# ------------------------------------------------------------------ utilidades propias (lo que le falta al kit)
def _cortes(poligono, px):
    """Las y donde la vertical x = px corta un poligono (pixeles), ordenadas."""
    ys = []
    n = len(poligono)
    for i in range(n):
        (x0, y0), (x1, y1) = poligono[i], poligono[(i + 1) % n]
        if (x0 <= px < x1) or (x1 <= px < x0):
            ys.append(y0 + (px - x0) * (y1 - y0) / (x1 - x0))
    return sorted(ys)


def _vertical(lam, px, primero=False):
    """(arriba, abajo) del contorno exterior de la lamina en la columna px. Con `primero`, el abajo del primer tramo
    (la culata, sin el pistolete que la misma columna corta mas abajo)."""
    ys = _cortes(lam.exterior, px)
    return ys[0], ys[1] if primero else ys[-1]


def _cil(k, p0, p1, r, segmentos=16, chaflan=0.0, material=None, nombre="cilindro", bola=False):
    """Un cilindro de p0 a p1 con sus dos cantos achaflanados en el propio perfil (un torno de 6 puntos: la mitad de
    triangulos que un cilindro con bisel). Con `bola`, el extremo p1 redondeado (la bola de la palanca de montar)."""
    from mathutils import Vector
    d = Vector(p1) - Vector(p0)
    L = d.length
    c = min(chaflan, 0.45 * L, 0.45 * r)
    if bola:
        perfil = [(0.0, 0.0), (0.0, r - c), (c, r), (L - 0.55 * r, r), (L - 0.22 * r, 0.8 * r), (L, 0.42 * r),
                  (L, 0.0)]
    elif c > 0:
        perfil = [(0.0, 0.0), (0.0, r - c), (c, r), (L - c, r), (L, r - c), (L, 0.0)]
    else:
        perfil = [(0.0, 0.0), (0.0, r), (L, r), (L, 0.0)]
    return k.torno(perfil, eje=tuple(d), origen=tuple(p0), segmentos=segmentos, material=material, nombre=nombre)


def _seccion(cal, px, ya, yb, hw, fa=1.0, fb=1.0, n=2.5, N=28, x_de_py=None, dy=0.0):
    """Un anillo de N puntos (metros) de una seccion redondeada en la columna px de la lamina: de ya (arriba) a yb
    (abajo), en pixeles; hw la media anchura (m) a media altura, fa y fb la fraccion de hw cerca de arriba y de abajo
    (el lomo de una culata es mas estrecho que su parte de abajo), n el exponente de la superelipse (2 elipse, mas
    cuadrada cuanto mas alto). x_de_py(py) -> px: la columna de cada punto, para un corte inclinado (la cantonera).
    dy: metros que se le suman arriba y abajo (una pieza que envuelve a otra)."""
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
        pts.append((cal.x(x_de_py(py) if x_de_py else px), cal.y(py) + v * dy, u * hw * f))
    return pts


def _solido(k, anillos, material, nombre):
    """Un solido cerrado por anillos (listas de puntos 3D con el mismo numero de puntos, en orden): la culata, el
    guardamanos de abajo y su abrazadera. Los extremos se tapan con un abanico desde su centro."""
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


def _remache(k, centro, r, lado, material, alto=None, segmentos=8):
    """Una cabeza de remache abombada sobre la cara z = centro[2], hacia +Z (lado 1) o -Z (lado -1)."""
    alto = alto or r * 0.55
    x, y, z = centro
    perfil = [(-alto, r), (alto * 0.3, r * 0.94), (alto, r * 0.42)]
    return k.torno(perfil, eje=(0.0, 0.0, lado), origen=(x, y, z), segmentos=segmentos, material=material,
                   nombre="remache")


def _cargador(k, cal, mm, material):
    """El cargador curvo de chapa de 30 (7,62 x 39): la cara de atras es un arco de 320 mm de radio que empieza dentro
    del brocal (contra la retenida, x = 919 px) y gira 38,5 grados hacia delante; 67 mm de fondo y 26,5 de grueso, dos
    nervios estampados a cada lado y la tapa del fondo. Devuelve el centro de la tapa (para las notas)."""
    xr, yt = cal.x(919), cal.y(333) + mm(16)         # arriba y atras, 16 mm dentro del cajon
    R, D, th = 0.320, mm(67), math.radians(38.5)
    cx, cy = xr + R, yt
    n = 11

    def P(r, t, s=0.0):                               # radio r al angulo t, y s metros a lo largo (hacia abajo)
        return (cx - r * math.cos(t) + s * math.sin(t), cy - r * math.sin(t) - s * math.cos(t))

    ts = [th * i / n for i in range(n + 1)]
    cuerpo = k.perfil([P(R, t) for t in ts] + [P(R - D, t) for t in reversed(ts)], ancho=mm(26.5), material=material,
                      bisel=mm(1.3), nombre="cargador")
    # Nervios a lo largo de la curva (a un tercio y a dos tercios del fondo), 0,8 mm por fuera de la chapa.
    nervios = []
    for f in (0.30, 0.72):
        rc = R - D * f
        tn = [th * (0.10 + 0.82 * i / 6) for i in range(7)]
        banda = [P(rc + mm(1.6), t) for t in tn] + [P(rc - mm(1.6), t) for t in reversed(tn)]
        nervio = k.perfil(banda, desde=mm(12.6), hasta=mm(14.05), material=material, bisel=mm(0.5), segmentos=1,
                          nombre="nervio")
        nervios += [nervio, k.espejo(nervio)]
    k.unir(cuerpo, *nervios)
    # La tapa del fondo: sobresale 3 mm por delante y por detras y 1 mm por los lados.
    tapa = [P(R + mm(3), th, -mm(2)), P(R + mm(3), th, mm(4.5)), P(R - D - mm(3), th, mm(4.5)),
            P(R - D - mm(3), th, -mm(2))]
    k.perfil(tapa, ancho=mm(28.5), material=material, bisel=mm(1.2), nombre="tapa_cargador")
    return P(R - D * 0.5, th, mm(4.5))


# ------------------------------------------------------------------ modelo
def modelar(k):
    mm = k.mm
    # x: de la cantonera (36) a la cara de la tuerca de la boca (2137) son los 870 mm reales; y con la misma escala
    # (la lamina no esta estirada) y el cero en el eje del canon (255).
    cal = k.Calibracion(px_boca=PX_BOCA, px_cola=PX_CULATA, largo=LARGO, py_eje=PY_EJE, boca_izquierda=False)
    lam = k.lamina("lateral")
    notas = []

    # Chapa y canon negros mate (pintura: 'pavonado'/'acero' van a la receta de metal, que no baja de un gris claro);
    # el acero en blanco solo en el portacerrojo y la palanca de montar. La madera, roja de laca como en la lamina:
    # 'madera_nogal_*' tine al 0,85 (con 'madera' a secas saldria pino claro). El pistolete, mas oscuro.
    negro = k.material("pintura_cajon", "#303135", rugosidad=0.6, metal=0.0)
    acero = k.material("acero_cerrojo", "#5a5c60", rugosidad=0.35, metal=1.0)
    madera = k.material("madera_nogal_culata", "#9a3412", rugosidad=0.5, veta=(1.0, 0.0, 0.0))
    pistola = k.material("madera_nogal_pistolete", "#4a2620", rugosidad=0.55, veta=(0.27, 0.96, 0.0))

    # ---------------------------------------------------------------- culata (anillos de superelipse)
    def cantonera_px(py, extra=0.0):                 # la cara de la cantonera, algo inclinada (37 arriba, 48 abajo)
        return 37.0 + (py - 272.0) * (48.0 - 37.0) / (507.0 - 272.0) + extra

    def hw_culata(px):                                # 21 mm de media anchura en la cantonera, 13,8 en la garganta
        t = min(1.0, max(0.0, (px - 54.0) / (566.0 - 54.0)))
        return mm(21.0 + (13.8 - 21.0) * t)

    ya0, yb0 = _vertical(lam, 56, True)
    anillos = [_seccion(cal, 0, ya0 + 1, yb0 - 1, hw_culata(54), 0.72, 0.95, 2.5, N=24,
                        x_de_py=lambda py: cantonera_px(py, 15.0))]
    columnas = [62, 72, 90, 150, 220, 290, 350, 372, 385, 396, 408, 420, 432, 446, 466, 490, 515, 540, 556, 566]
    for px in columnas:
        ya, yb = _vertical(lam, min(px, 561), True)
        anillos.append(_seccion(cal, px, ya, yb, hw_culata(px), 0.72, 0.95, 2.5, N=24))
    _solido(k, anillos, madera, "culata")
    # La cantonera de chapa: el mismo corte que la culata, medio milimetro mas grande, con la cara de atras redondeada.
    canto = [_seccion(cal, 0, ya0 + 4, yb0 - 4, hw_culata(54) - mm(1.2), 0.72, 0.95, 2.5, N=24,
                      x_de_py=lambda py: cantonera_px(py)),
             _seccion(cal, 0, ya0 + 0.5, yb0 - 0.5, hw_culata(54) + mm(0.5), 0.72, 0.95, 2.5, N=24,
                      x_de_py=lambda py: cantonera_px(py, 4.0)),
             _seccion(cal, 0, ya0 + 0.5, yb0 - 0.5, hw_culata(54) + mm(0.5), 0.72, 0.95, 2.5, N=24,
                      x_de_py=lambda py: cantonera_px(py, 17.0))]
    _solido(k, canto, negro, "cantonera")
    # La anilla de la correa, en el lado izquierdo y abajo (la ensenan la de tres cuartos y la del detalle).
    xa, ya_ = cal.x(108), cal.y(500)
    _cil(k, (xa, cal.y(484), -mm(18.0)), (xa, cal.y(484), -mm(21.6)), mm(3.6), 10, material=negro,
         nombre="pie_anilla")
    anilla = [(xa + mm(7.5) * math.sin(2 * math.pi * i / 12), ya_ + mm(7.5) * math.cos(2 * math.pi * i / 12),
               -mm(21.0)) for i in range(13)]
    k.barrido(anilla, mm(1.4), 6, material=negro, nombre="anilla_correa")

    # ---------------------------------------------------------------- cajon (chapa estampada)
    cajon = k.perfil(cal.puntos([(563, 248), (575, 222), (1192, 222), (1192, 326), (940, 334), (900, 340),
                                 (563, 346)]), ancho=mm(30.0), material=negro, bisel=mm(1.2), nombre="cajon")
    # Lado derecho: la ventana de expulsion y la ranura de la palanca de montar, por las que se ve el portacerrojo.
    k.restar(cajon, k.caja((cal.x(980), cal.y(248), -mm(3.0)), (cal.x(1123), cal.y(226), mm(16.0))))
    k.restar(cajon, k.caja((cal.x(790), cal.y(246), mm(8.0)), (cal.x(981), cal.y(236), mm(16.0))))
    # El hundido ovalado sobre el brocal, a los dos lados.
    ovalo = k.redondear(cal.puntos([(970, 290), (1028, 290), (1028, 317), (970, 317)]), mm(5.0), 4)
    hundido = k.perfil(ovalo, desde=mm(14.3), hasta=mm(16.0))
    k.restar(cajon, k.unir(hundido, k.espejo(hundido)))
    k.caja((cal.x(790), cal.y(247), -mm(2.5)), (cal.x(1126), cal.y(227), mm(12.5)), material=acero, bisel=mm(0.5),
           nombre="portacerrojo")
    # La palanca de montar: el vastago y la bola, 33 mm por fuera del eje (lado derecho).
    xp, yp = cal.x(1129), cal.y(236)
    _cil(k, (xp, yp, mm(10.0)), (xp, yp, mm(27.0)), mm(3.2), 10, material=acero, nombre="palanca_montar")
    _cil(k, (xp, yp, mm(25.0)), (xp, yp, mm(33.5)), mm(5.4), 14, mm(1.0), material=acero, nombre="bola_palanca",
         bola=True)
    # Remaches y pasadores (a los dos lados): las mesas de los muelles, los del guardamonte y los del cierre de
    # delante.
    remaches = [(605, 258, 7, 2), (668, 288, 7, 1), (770, 321, 6, 1), (850, 317, 6, 1), (893, 293, 5, 1),
                (937, 294, 5, 1), (947, 303, 6, 2), (1050, 302, 5, 1), (1153, 263, 6, 2), (1168, 263, 6, 2),
                (1147, 233, 4, 1), (1160, 233, 4, 1)]
    for px, py, r, lados in remaches:                # lados: 2 a los dos lados, 1 solo a la derecha
        for lado in (1.0, -1.0)[:lados]:
            _remache(k, (cal.x(px), cal.y(py), lado * mm(14.8)), cal.dx(r) * 0.85, lado, negro)

    # El selector (lado derecho): la palanca larga en "seguro", que tapa la ranura de la palanca de montar.
    c = cal.p(712, 275)
    rs = cal.dx(19)
    barra = cal.puntos([(712, 256), (752, 254), (775, 240), (792, 232), (942, 231), (949, 236), (949, 247),
                        (942, 252), (880, 258), (820, 268), (770, 283), (712, 294)])
    selector = barra + k.arco(c, rs, 270.0, 90.0, 10)[1:-1]
    k.perfil(selector, desde=mm(14.9), hasta=mm(16.9), material=negro, bisel=mm(0.5), nombre="selector")
    _cil(k, (c[0], c[1], mm(16.0)), (c[0], c[1], mm(18.0)), mm(4.6), 14, mm(0.6), material=negro,
         nombre="eje_selector")

    # ---------------------------------------------------------------- tapa del cajon
    # Llega hasta la base del alza (px 1140, su cara de atras): las dos caras se tocan y los biseles dejan solo la
    # junta, como en la lamina. Acabada antes (en 1128) quedaba una ranura de 5 mm que atravesaba el arma.
    lado_tapa = cal.puntos([(575, 247), (575, 232), (579, 222), (583, 212), (588, 206), (596, 203), (603, 198),
                            (612, 187), (622, 177), (632, 170), (642, 166), (655, 165), (1140, 168), (1140, 226),
                            (790, 226), (752, 247)])
    tapa = k.perfil(lado_tapa, ancho=mm(31.6), material=negro, nombre="tapa")
    yt, yb = cal.y(163), cal.y(250)
    seccion = k.redondear([(-mm(15.8), yb), (mm(15.8), yb), (mm(15.8), yt), (-mm(15.8), yt)],
                          {2: mm(10.0), 3: mm(10.0)}, 5)
    k.intersecar(tapa, k.perfil(seccion, plano="zy", desde=cal.x(560), hasta=cal.x(1150)))
    k.biselar(tapa, mm(0.8), 2, 30)
    # La acanaladura estampada de atras, a los dos lados.
    canal = k.perfil(k.redondear(cal.puntos([(628, 224), (742, 224), (742, 234), (628, 234)]), mm(2.0), 3),
                     desde=mm(15.0), hasta=mm(17.0))
    k.restar(tapa, k.unir(canal, k.espejo(canal)))
    # El nervio de la tapa a cada lado y el boton de la guia del muelle recuperador, que asoma por detras.
    nervio = k.caja((cal.x(645), cal.y(213), mm(14.8)), (cal.x(1122), cal.y(207), mm(16.3)), material=negro,
                    bisel=mm(0.5))
    k.espejo(nervio)
    k.perfil(cal.puntos([(578, 206), (598, 203), (600, 227), (580, 229)]), ancho=mm(12.0), material=negro,
             bisel=mm(2.4), segmentos=2, nombre="boton_guia")

    # ---------------------------------------------------------------- alza
    k.perfil(k.redondear(cal.puntos([(1140, 168), (1290, 168), (1302, 178), (1302, 224), (1140, 224)]),
                         {1: mm(3.0), 2: mm(3.0)}, 3), ancho=mm(28.0), material=negro, bisel=mm(1.0),
             nombre="base_alza")
    _cil(k, (cal.x(1292), cal.y(187), 0.0), (cal.x(1306), cal.y(187), 0.0), mm(14.6), 20, mm(1.0), material=negro,
         nombre="anillo_alza")
    hoja = k.perfil(cal.puntos([(1127, 151), (1133, 145), (1240, 142), (1280, 146), (1292, 155), (1290, 165),
                                (1140, 164), (1127, 160)]), ancho=mm(14.0), material=negro, bisel=mm(0.5),
                    nombre="hoja_alza")
    k.restar(hoja, k.cilindro((cal.x(1124), cal.y(144), 0.0), (cal.x(1138), cal.y(144), 0.0), mm(1.4), 8))
    k.caja((cal.x(1157), cal.y(166), -mm(10.0)), (cal.x(1184), cal.y(141), mm(10.0)), material=negro, bisel=mm(0.6),
           nombre="corredera_alza")
    for (px, py, r, z) in ((1170, 143, 3.6, 11.5), (1282, 156, 4.2, 10.0), (1248, 146, 3.3, 7.5)):
        _cil(k, (cal.x(px), cal.y(py), -mm(z)), (cal.x(px), cal.y(py), mm(z)), mm(r), 12, mm(0.5), material=negro,
             nombre="eje_alza")
    # La palanca del cierre del tubo de gases (lado derecho), con su eje.
    k.perfil(cal.puntos([(1218, 196), (1226, 206), (1293, 176), (1291, 163), (1281, 160)]), desde=mm(13.8),
             hasta=mm(15.6), material=negro, bisel=mm(0.4), nombre="cierre_tubo")
    _cil(k, (cal.x(1231), cal.y(190), mm(14.0)), (cal.x(1231), cal.y(190), mm(16.6)), mm(3.0), 10, mm(0.4),
         material=negro, nombre="eje_cierre")

    # ---------------------------------------------------------------- canon, bloque de gases, mira y tuerca
    xr = cal.x(1185)
    k.torno([(xr, 0.0), (xr, mm(10.5)), (cal.x(1300), mm(10.5)), (cal.x(1312), mm(8.2)), (cal.x(1770), mm(8.2)),
             (cal.x(1775), mm(7.6)), (cal.x(2100), mm(7.6)), (cal.x(2100), mm(3.9)), (cal.x(2070), mm(3.9)),
             (cal.x(2070), 0.0)], eje="x", segmentos=20, material=negro, nombre="canon")
    xb = cal.x(PX_BOCA)
    k.torno([(cal.x(2093), mm(5.0)), (cal.x(2093), mm(9.0)), (cal.x(2118), mm(9.0)), (xb - mm(1.2), mm(7.9)),
             (xb, mm(7.4)), (xb, mm(5.0))], eje="x", segmentos=20, material=negro, cerrado=True, nombre="tuerca_boca")
    # Bloque de gases: el anillo del canon, la camara de arriba y el cuello a 45 grados que los une.
    yg = cal.y(196)
    _cil(k, (cal.x(1763), 0.0, 0.0), (cal.x(1857), 0.0, 0.0), mm(11.3), 20, mm(0.9), material=negro,
         nombre="anillo_gases")
    k.torno([(cal.x(1712), 0.0), (cal.x(1712), mm(10.6)), (cal.x(1782), mm(10.6)), (cal.x(1792), mm(9.0)),
             (cal.x(1797), mm(5.0)), (cal.x(1798), 0.0)], eje="x", origen=(0.0, yg, 0.0), segmentos=20,
            material=negro, nombre="camara_gases")
    k.perfil(cal.puntos([(1745, 182), (1772, 172), (1786, 176), (1798, 190), (1813, 206), (1832, 221),
                         (1857, 229), (1857, 262), (1768, 262), (1760, 215)]), ancho=mm(19.0),
             material=negro, bisel=mm(2.0), segmentos=3, nombre="cuello_gases")
    k.perfil(cal.puntos([(1800, 278), (1831, 278), (1831, 300), (1826, 305), (1805, 305), (1800, 300)]),
             ancho=mm(10.0), material=negro, bisel=mm(0.8), nombre="tope_baqueta")
    # Tubo de gases (bajo el guardamanos de arriba) con su ventana de escape, y la tapa de delante.
    tubo = k.cilindro((cal.x(1300), yg, 0.0), (cal.x(1716), yg, 0.0), mm(9.9), 20, material=negro, nombre="tubo_gases")
    ventana = k.redondear(cal.puntos([(1602, 185), (1700, 185), (1700, 206), (1602, 206)]), mm(4.2), 4)
    escape = k.perfil(ventana, desde=mm(8.0), hasta=mm(12.0))
    k.restar(tubo, k.unir(escape, k.espejo(escape)))
    _cil(k, (cal.x(1566), cal.y(193), 0.0), (cal.x(1592), cal.y(193), 0.0), mm(12.4), 20, mm(1.3), material=negro,
         nombre="tapa_guardamanos")
    # Mira delantera: la torre con sus orejas (el hueco de arriba, entre las dos), la ventana, el tambor del poste,
    # el anillo del canon y el taco de la baqueta.
    torre = k.perfil(cal.puntos([(2011, 240), (2019, 224), (2029, 200), (2039, 170), (2046, 140), (2050, 128),
                                 (2057, 122), (2088, 122), (2094, 128), (2094, 245)]), ancho=mm(16.0), material=negro,
                     bisel=mm(1.0), nombre="mira")
    k.restar(torre, k.caja((cal.x(2030), cal.y(165), -mm(4.6)), (cal.x(2110), cal.y(110), mm(4.6))))
    k.restar(torre, k.perfil(k.redondear(cal.puntos([(2041, 192), (2077, 192), (2079, 218), (2031, 218)]), mm(1.6), 3),
                             ancho=mm(30.0)))
    _cil(k, (cal.x(2071), cal.y(167), -mm(9.0)), (cal.x(2071), cal.y(167), mm(9.0)), mm(4.6), 12, mm(0.5),
         material=negro, nombre="tambor_mira")
    k.cilindro((cal.x(2071), cal.y(190), 0.0), (cal.x(2071), cal.y(142), 0.0), mm(1.4), 8, material=negro,
               nombre="poste_mira")
    _cil(k, (cal.x(2015), 0.0, 0.0), (cal.x(2094), 0.0, 0.0), mm(11.0), 20, mm(0.9), material=negro,
         nombre="anillo_mira")
    k.perfil(cal.puntos([(2022, 276), (2094, 276), (2094, 297), (2030, 297)]), ancho=mm(12.0), material=negro,
             bisel=mm(1.0), nombre="taco_mira")
    # La baqueta, bajo el canon, de la abrazadera del guardamanos a la mira.
    yv = cal.y(289)
    k.cilindro((cal.x(1580), yv, 0.0), (cal.x(2097), yv, 0.0), mm(2.5), 10, material=negro, nombre="baqueta")
    _cil(k, (cal.x(2089), yv, 0.0), (cal.x(2101), yv, 0.0), mm(3.3), 10, mm(0.6), material=negro,
         nombre="cabeza_baqueta")

    # ---------------------------------------------------------------- guardamanos
    # El de abajo: anillos (arriba plano, abajo redondo) siguiendo el bulto de atras de la lamina.
    anillos = []
    for px in (1193, 1196, 1200, 1206, 1214, 1226, 1242, 1262, 1290, 1330, 1450, 1540, 1560, 1576):
        _ya, yb = _vertical(lam, min(px, 1566))
        anillos.append(_seccion(cal, px, 221, yb, mm(21.0), 1.0, 0.84, 3.0, N=24))
    inferior = _solido(k, anillos, madera, "guardamanos_inferior")
    # Las dos ventanas del canto de arriba.
    for x0, x1 in ((1336, 1404), (1462, 1518)):
        ventana = k.redondear(cal.puntos([(x0, 216), (x1, 216), (x1, 229), (x0, 229)]), mm(1.5), 3)
        rebaje = k.perfil(ventana, desde=mm(15.0), hasta=mm(30.0))
        k.restar(inferior, k.unir(rebaje, k.espejo(rebaje)))
    # La abrazadera de chapa delante (un anillo un poco mayor) con su palanquita a la derecha.
    abrazadera = []
    for px in (1567, 1600):
        _ya, yb = _vertical(lam, 1566)
        abrazadera.append(_seccion(cal, px, 219, yb + 1.5, mm(22.4), 1.0, 0.86, 3.0, N=24))
    k.biselar(_solido(k, abrazadera, negro, "abrazadera"), mm(1.0), 2, 30)
    _cil(k, (cal.x(1593), cal.y(240), mm(20.0)), (cal.x(1593), cal.y(240), mm(23.6)), mm(3.4), 10, mm(0.5),
         material=negro, nombre="palanca_abrazadera")
    # El de arriba: media cana sobre el tubo de gases (el lateral redondeado por la seccion).
    R = mm(16.2)
    ysup = cal.y(222)
    superior = k.perfil(k.redondear(cal.puntos([(1305, 157), (1568, 157), (1568, 222), (1305, 222)]),
                                    {0: mm(6.0), 1: mm(6.0)}, 4), ancho=2 * R + mm(4), material=madera,
                        nombre="guardamanos_superior")
    seccion = [(R, ysup)] + k.arco((0.0, yg), R, 0.0, 180.0, 14) + [(-R, ysup)]
    k.intersecar(superior, k.perfil(seccion, plano="zy", desde=cal.x(1300), hasta=cal.x(1575)))
    k.biselar(superior, mm(1.5), 2, 30)

    # ---------------------------------------------------------------- pistolete, guardamonte y mandos
    # El pistolete: la silueta de la lamina (29,4 mm: queda dentro del cajon, de 30, por arriba) con su picado
    # hundido medio milimetro a cada lado.
    pistolete = k.silueta(cal, region=[(597, 332), (741, 332), (741, 398), (720, 412), (700, 440), (684, 500),
                                       (672, 560), (662, 600), (530, 600), (560, 400), (597, 352)],
                          ancho=mm(29.4), material=pistola, nombre="pistolete")
    k.biselar(pistolete, mm(5.0), 2, 30)
    picado = k.redondear(cal.puntos([(616, 383), (686, 388), (646, 542), (568, 538)]), mm(2.0), 3)
    k.restar(pistolete, k.unir(k.perfil(picado, desde=mm(14.1), hasta=mm(16.0)),
                               k.perfil(picado, desde=-mm(16.0), hasta=-mm(14.1))))
    # Guardamonte de chapa, gatillo y retenida del cargador.
    guarda = k.perfil(cal.puntos([(741, 334), (871, 334), (873, 404), (866, 417), (752, 419), (742, 410)]),
                      ancho=mm(10.0), material=negro, nombre="guardamonte")
    k.restar(guarda, k.perfil(cal.puntos([(752, 320), (865, 320), (865, 403), (756, 405), (752, 398)]),
                              ancho=mm(20.0)))
    k.biselar(guarda, mm(0.8), 2, 30)
    k.perfil(cal.puntos([(754, 336), (767, 336), (772, 358), (781, 374), (792, 388), (803, 397), (800, 401),
                         (789, 397), (774, 386), (762, 372), (755, 358)]), ancho=mm(6.0), material=negro,
             bisel=mm(0.6), nombre="gatillo")
    k.perfil(cal.puntos([(866, 332), (919, 332), (919, 396), (906, 401), (901, 432), (895, 437), (889, 432),
                         (887, 401), (870, 398)]), ancho=mm(16.0), material=negro, bisel=mm(0.8),
             nombre="retenida_cargador")

    # ---------------------------------------------------------------- cargador
    if os.environ.get("GD_AK47_CARGADOR", "si").lower() not in ("no", "0", "sin"):
        fondo = _cargador(k, cal, mm, negro)
        notas.append(f"cargador: tapa a ({fondo[0] * 1000:.0f}, {fondo[1] * 1000:.0f}) mm del eje de la boca")
    else:
        notas.append("sin cargador (GD_AK47_CARGADOR=no)")
    if os.environ.get("GD_AK47_DEPURAR"):          # triangulos por pieza, para cuadrar el presupuesto
        cuenta = {}
        for ob in k._piezas:
            ob.data.calc_loop_triangles()
            nombre = ob.name.split(".")[0]
            cuenta[nombre] = cuenta.get(nombre, 0) + len(ob.data.loop_triangles)
        for nombre, n in sorted(cuenta.items(), key=lambda t: -t[1]):
            print(f"[armas]   {nombre}: {n}")

    # El agarre: el centro del pistolete a la altura del dedo corazon (justo bajo el guardamonte). La mano izquierda,
    # en el centro del guardamanos de abajo.
    return {"agarre": cal.p(641, 435, 0.0), "boca": (cal.x(PX_BOCA), 0.0, 0.0), "guardamanos": cal.p(1385, 268, 0.0),
            "calibracion": cal, "notas": notas}


# ------------------------------------------------------------------ construirla (con la comparacion volteada)
def _comparar_volteada(revision=None, trabajo=None):
    """La comparacion de construir_arma.comparar con la lamina lateral volteada: esta tiene la boca a la derecha y el
    kit compara siempre con la boca a la izquierda (sin voltear, la IoU sale de la lamina al reves). Escribe una
    copia volteada de la lamina en trabajo/ y se la pasa a comparar() cambiando, solo mientras dura, la ruta de la
    lamina y la caja de su contorno. La de tres cuartos no se voltea: la camara de la FICHA ya mira desde la
    derecha. `trabajo`: otra carpeta con los renders y las medidas (la del modelo sin cargador)."""
    import construir_arma as ca
    import laminas
    from PIL import Image, ImageOps
    nombre = FICHA["nombre"]
    _ruta, ficha = ca.cargar_ficha(nombre)
    _ac, _salida, trabajo_, rev = ca.carpetas(nombre, revision)
    trabajo = trabajo or trabajo_
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
        return ca.comparar(nombre, ficha, trabajo, rev if trabajo == trabajo_ else trabajo)
    finally:
        laminas.ruta_lamina, ca.leer_json = ruta_lamina, leer_json


def _comparar_sin_cargador(revision=None, blender=None):
    """La lamina no tiene cargador y el modelo si: la caja del modelo crece 85 mm hacia abajo y la IoU «ajustada a la
    caja» de la comparacion normal estira la lamina hasta ahi (no mide nada). Esta modela el arma otra vez SIN
    cargador (GD_AK47_CARGADOR=no) en trabajo/sin_cargador (sin tocar salida/), la compara igual y deja
    <revision>/gd_ak47_comparar_sin_cargador.png: esa es la IoU que dice si la silueta casa con la lamina."""
    import shutil
    import subprocess
    import construir_arma as ca
    nombre = FICHA["nombre"]
    ruta, _ficha = ca.cargar_ficha(nombre)
    _ac, _salida, trabajo, rev = ca.carpetas(nombre, revision)
    sub = os.path.join(trabajo, "sin_cargador")
    os.makedirs(sub, exist_ok=True)
    for vista in ("lateral", "tres_cuartos"):
        shutil.copy(os.path.join(trabajo, f"{nombre}_lamina_{vista}.json"), sub)
    cmd = [ca.blender_exe(blender), "-b", "--factory-startup", "--python", os.path.join(ca.AQUI, "blender_arma.py"),
           "--", "--modelo", ruta, "--salida", sub, "--trabajo", sub, "--origen", "agarre"]
    r = subprocess.run(cmd, capture_output=True, text=True, encoding="utf-8", errors="replace",
                       env=dict(os.environ, GD_AK47_CARGADOR="no"))
    with open(os.path.join(sub, "blender.log"), "w", encoding="utf-8") as f:
        f.write(r.stdout + "\n" + r.stderr)
    if r.returncode != 0 or "Traceback" in r.stdout + r.stderr:
        raise SystemExit(f"Blender ha fallado sin cargador (ver {sub}\\blender.log)")
    hecho, i_a, i_i = _comparar_volteada(revision, sub)
    destino = os.path.join(rev, nombre + "_comparar_sin_cargador.png")
    os.replace(hecho, destino)
    return destino, i_a, i_i


def main():
    import argparse
    sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
    import construir_arma as ca
    p = argparse.ArgumentParser(description="Construye gd_ak47 con construir_arma.py y hace la comparacion con la "
                                            "lamina volteada (boca a la derecha), con y sin cargador.")
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
        print(f"-- comparacion (lamina volteada): {ruta} (IoU {i_a:.3f} ajustada, {i_i:.3f} por el largo; "
              "con el cargador, que la lamina no tiene)")
        ruta, i_a, i_i = _comparar_sin_cargador(a.revision, a.blender)
        print(f"-- comparacion sin cargador: {ruta} (IoU {i_a:.3f} ajustada, {i_i:.3f} por el largo)")
    return 0


if __name__ == "__main__":
    sys.exit(main())
