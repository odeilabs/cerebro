"""gd_m2_browning: ametralladora pesada Browning M2HB (12,7 x 99 mm) en la cuna y la cabeza de su pedestal (el pie, del
alto que haga falta, lo pone el juego; sin tripode): la de la torreta del blindado de Brandao (Basiliskos, la 34) y
cualquier ametralladora pesada montada de la saga.
Lamina 029-m2-browning (la lateral y la de tres cuartos son las del pedestal; el detalle de la empunadura la ensena en
un tripode M3, que no se modela).

Medidas reales (hoja del M2HB de General Dynamics y de US Ordnance): 1.654 mm de largo (de las cachas a la bocacha),
canon de 1.143 mm (45"), 230 mm de ancho (de cacha a cacha) y 193 mm de alto (el arma sola). Con una sola escala, la del
largo (0,790 mm/px), la lamina da un canon de 43 mm de diametro junto a la camisa y 39 en la boca, una bocacha de
55 mm, una camisa de 74 mm y un cajon de 131 mm de alto (135 con la tapa) y 571 de largo (600 con la placa trasera y el
manguito): las medidas de un M2HB de verdad (un canon de acero de 11,8 kg y 45" sale de 43 mm de media), asi que la
lamina no esta estirada y la calibracion lleva una sola escala. El canon real empieza 170 mm por detras de la cara del
cajon (la recamara); lo de dentro del cajon no se modela. El ancho del cajon (112 mm) y la separacion de las cachas
(190 mm entre centros, 229 de fuera a fuera) salen de los 230 mm de ancho y de la vista de tres cuartos. Del pie de las
cachas a lo alto de la tapa mide 140 mm, y 208 hasta la perilla del alza (alzada en la lamina).

La lamina lateral tiene la BOCA A LA DERECHA (ensena el lado derecho, +Z: la corredera de montar con su asa): la
calibracion va con boca_izquierda=False y la comparacion del kit (que no voltea la lamina) se hace con este fichero, con
la lamina volteada. La de tres cuartos mira desde atras y a la derecha (por eso el azimut negativo de vista_34).

El soporte es el de la lamina hasta la cabeza del pedestal: la cuna (dos brazos a los lados del cajon, el bloque
delantero con el munon y el eje de atras con su palanca), el collarin de arriba de la columna y una espiga de 40 mm de
la columna: lo que gira con el arma. El PIE de la lamina (la columna hasta el suelo, cuatro cartabones y la placa
redonda con cuatro tornillos) tiene un alto fijo (la placa 0,322 m por debajo del agarre) y no va en la malla: en la
torreta de un blindado o en un emplazamiento, el juego pone el poste del alto que le haga falta, un cilindro de
`radio_columna` desde el `pivote` (la cara de abajo de la espiga, sobre el eje de giro) hacia -Y. Los dos van en el
bloque `montaje` del _agarre.json (con GD_M2_BASE=1 en el entorno, la malla lleva tambien el pie entero de la lamina y
el pivote es la cara de abajo de la placa).

Los materiales, medidos en la lamina lateral (medianas sRGB de las caras verticales, con la misma luz): el costado del
cajon 81 y la cuna oliva 84-90, o sea el parkerizado casi tan claro como el oliva; el canon, del mismo gris, con brillo
(la banda de arriba, 131). El Kd de cada uno sale de ahi. La rugosidad de la FICHA (la de cada `k.material`) no la lee el
texturizador del .mtl (cada receta trae la suya: pintura_mate 0,73, goma 0,815): se le pasa como el acabado `rugosidad`
(lo que se suma a la del tile) del anexo <n>.veta.json, que este fichero escribe entre el modelo y las texturas y
comprueba despues en los mapas. Por eso la receta es este fichero y no construir_arma.py a secas (el paso del modelo del
kit borra el anexo si no hay maderas).

El agarre es el de las dos cachas de atras, entre ellas (z = 0), a media altura de la cacha; `guardamanos` es la cacha
izquierda (-Z), donde va la mano izquierda.

Lo que al kit le falta y se hace aqui: remaches y cabezas de tornillo (_remaches, _tornillos: troncos de 6 lados en una
sola pieza), la comparacion con la lamina volteada (y sin el pie, si la malla no lo lleva), el acabado de la rugosidad
y el bloque `montaje` del _agarre.json.

Las coordenadas de la lamina se leen con
    python laminas.py rejilla 029-m2-browning/lateral rejilla.png --paso 10 --recorte 880,120,1420,330 --zoom 2
Construirla entera (modelo, acabado, texturas, agarre y la comparacion volteada), con el python del .venv de
AssetsCreator:
    python modelos/gd_m2_browning.py [--res 1024] [--solo modelo,comparar] [--revision <carpeta>]
"""
import json
import math
import os
import re
import sys

FICHA = {
    "nombre": "gd_m2_browning",
    "clase": "Ametralladora",
    "lamina": "029-m2-browning",
    "para": "la ametralladora de 12,7 de la torreta del blindado de Brandao (Basiliskos, la 34); cualquier "
            "ametralladora pesada montada de la saga",
    "medidas_reales": {"largo": 1.654, "canon": 1.143, "ancho": 0.230, "alto_arma": 0.193, "cajon": 0.571,
                       "alto_cajon": 0.131, "ancho_cajon": 0.112, "camisa": 0.074, "bocacha": 0.055},
    "texturas": {"res": 1024, "escala": 0.4},   # 0,4 m por tesela: la densidad de todas las gd_
    # la lamina de tres cuartos mira desde atras y a la derecha (+Z), un poco desde arriba y con angular normal
    "vista_34": {"azimut": -132.0, "elevacion": 13.0, "focal": 45.0},
    "semilla": 29,
    # el fondo de estudio de esta lamina tiene un degradado fuerte y la sombra del suelo: con 22 la silueta se los
    # come (la de tres cuartos sale media imagen); con 45 sale limpia y el oliva claro del soporte sigue dentro
    "umbral": 45.0,
}

# Pixeles de la lamina lateral (m2-browning_lateral.png, 2172 x 724, boca a la derecha)
# (el eje: la bocacha esta en la y 237,5 y la camisa en la 242; la lamina tiene el canon girado 0,2 grados: el medio)
PX_BOCA, PX_COLA, PY_EJE = 2145, 52, 239.5   # la cara de la bocacha, la trasera de las cachas y el eje del canon
LARGO = 1.654                                # de las cachas a la bocacha (General Dynamics)
PX_PIVOTE = 871.5                            # el eje de la columna del pedestal
FILA_A = (1025, 1065, 1105, 1145, 1185, 1225)  # agujeros de la camisa a 0, 90, 180 y 270 grados
FILA_B = (1045, 1085, 1125, 1165, 1205, 1245)  # y a 45, 135, 225 y 315 (al tresbolillo)
PY_COLLARIN = 447                            # la cara de abajo del collarin del pedestal
ESPIGA = 0.040                               # lo que baja la columna por debajo del collarin (m)
RADIO_COLUMNA = 0.0407
# el pie de la lamina (columna hasta el suelo, cartabones y placa) solo con GD_M2_BASE=1: alto fijo, ver arriba
CON_BASE = os.environ.get("GD_M2_BASE", "") not in ("", "0")

# La rugosidad media de los mapas de cada receta del texturizador (medida en sus mapas, 27-10-2026: la de cada receta no
# depende del Kd); lo que la FICHA pide de mas o de menos se suma con el acabado del anexo de veta.
RUGOSIDAD_RECETA = {"pintura_mate": 0.730, "goma": 0.815, "metal": 0.484, "plastico": 0.300, "pintura_coche": 0.380}


# ------------------------------------------------------------------ lo que le falta al kit
def _remaches(k, cal, puntos, z, signo, material, r=0.0042, h=0.0019):
    """Cabezas de remache (un cono bajo de 6 lados, 10 triangulos) en la cara z del cajon, hacia `signo` (+1 la
    derecha, -1 la izquierda). Una sola pieza."""
    piezas = [k.torno([(0.0, r), (h, 0.0)], eje=(0.0, 0.0, signo), origen=(cal.x(px), cal.y(py), z),
                      segmentos=6, material=material, nombre="remache")
              for px, py in puntos]
    return k.unir(*piezas)


def _tornillos(k, centros, eje, r, h, material, arandela=0.0):
    """Cabezas hexagonales (con su arandela si se da) apoyadas en `centros` y saliendo hacia `eje`."""
    piezas = []
    for c in centros:
        piezas.append(k.torno([(0.0, r), (h, r * 0.85), (h, 0.0)], eje=eje, origen=c, segmentos=6, material=material,
                              fase=30.0, nombre="tornillo"))
        if arandela:
            piezas.append(k.torno([(-0.0015, arandela), (0.0006, arandela), (0.0006, 0.0)], eje=eje, origen=c,
                                  segmentos=10, material=material, nombre="arandela"))
    return k.unir(*piezas)


def _rodillo(k, a0, a1, r, chaflan, eje, origen, segmentos, material, nombre):
    """Un cilindro macizo con los dos cantos achaflanados en el propio perfil (sin modificador de bisel: la mitad de
    triangulos). a0 y a1 a lo largo de `eje` desde `origen`."""
    c = min(chaflan, abs(a1 - a0) * 0.45, r * 0.45)
    sg = 1.0 if a1 >= a0 else -1.0
    return k.torno([(a0, 0.0), (a0, r - c), (a0 + sg * c, r), (a1 - sg * c, r), (a1, r - c), (a1, 0.0)], eje=eje,
                   origen=origen, segmentos=segmentos, material=material, nombre=nombre)


def _contar(k):
    """Con GD_ARMAS_DEPURAR en el entorno: los triangulos de cada pieza (por su nombre), para el presupuesto."""
    cuenta = {}
    for ob in k._piezas:
        me = ob.data
        me.calc_loop_triangles()
        base = ob.name.split(".")[0]
        cuenta[base] = cuenta.get(base, 0) + len(me.loop_triangles)
    print(f"[armas]   total: {sum(cuenta.values())}")
    for n, t in sorted(cuenta.items(), key=lambda kv: -kv[1]):
        print(f"[armas]   {n}: {t}")


# ------------------------------------------------------------------ el modelo
def modelar(k):
    mm = k.mm
    cal = k.Calibracion(px_boca=PX_BOCA, px_cola=PX_COLA, largo=LARGO, py_eje=PY_EJE, boca_izquierda=False)
    X, Y = cal.x, cal.y

    # El parkerizado con la receta de pintura mate (dielectrico; la de metal sale acero inoxidable, 140 sRGB con
    # cualquier Kd, y con su anisotropia el canon salia a rayas): el cajon con el gris de la lamina (81 sRGB, casi tan
    # claro como el oliva); el canon (y la bocacha) un punto mas claro y con brillo, como en la lamina; el soporte,
    # verde oliva; las cachas, de goma (no 'empunadura': iria a cuero). La rugosidad llega a los mapas por el acabado
    # (ver _escribir_acabado).
    pavon = k.material("pintura_receptor", "#4f5051", rugosidad=0.62)
    acero = k.material("pintura_canon", "#56575a", rugosidad=0.5)
    oliva = k.material("pintura_soporte", "#57553f", rugosidad=0.7)
    goma = k.material("goma_cachas", "#1c1c1e", rugosidad=0.85)

    hw = mm(56.0)          # medio ancho del cajon (las chapas laterales)

    # ================================================================ cajon (receiver)
    cajon = k.perfil(cal.puntos([(182, 171), (905, 171), (905, 318), (610, 319), (592, 322), (580, 328),
                                 (520, 333), (500, 337), (182, 337)]),
                     ancho=2 * hw, material=pavon, nombre="cajon")
    k.biselar(cajon, mm(1.6), 2, 30)
    # La ventana de alimentacion (la cinta entra por un lado y sale por el otro) y la ranura de la corredera de montar.
    vent = cal.puntos([(756, 178), (830, 178), (830, 213), (756, 213)])
    ranura = k.redondear(cal.puntos([(300, 229), (548, 229), (548, 247), (300, 247)]), mm(6.5), 3)
    k.restar(cajon, k.unir(k.perfil(vent, desde=hw - mm(16), hasta=hw + mm(5)),
                           k.perfil(vent, desde=-hw - mm(5), hasta=-hw + mm(16)),
                           k.perfil(ranura, desde=hw - mm(7), hasta=hw + mm(5))))
    # El trinquete de la cinta, dentro de la ventana de cada lado.
    for s in (1, -1):
        k.caja((X(768), Y(200), s * (hw - mm(13))), (X(824), Y(189), s * (hw - mm(6))), material=pavon,
               bisel=mm(0.6), segmentos=1, nombre="trinquete")

    # Tapa (la de alimentacion, encima) y la placa de atras.
    k.perfil(cal.puntos([(184, 173), (184, 167), (880, 166), (887, 161), (905, 160), (905, 173)]), ancho=mm(104),
             material=pavon, bisel=mm(1.2), nombre="tapa")
    k.perfil(cal.puntos([(170, 178), (183, 173), (183, 337), (170, 335)]), ancho=2 * hw + mm(3), material=pavon,
             bisel=mm(1.5), nombre="placa_trasera")
    # La orejeta de delante de la tapa, sobre el bloque del munon (lo de arriba del manguito, de la tapa a la camisa).
    k.perfil(k.redondear(cal.puntos([(906, 168), (906, 121), (937, 121), (937, 168)]), {1: mm(4), 2: mm(4)}, 3),
             ancho=mm(14), material=pavon, bisel=mm(0.8), segmentos=1, nombre="orejeta")
    k.perfil(cal.puntos([(903, 166), (936, 166), (941, 172), (941, 196), (903, 196)]), ancho=mm(70), material=pavon,
             bisel=mm(1.2), segmentos=1, nombre="bloque_munon")

    # Las regletas de arriba y de abajo de las chapas laterales (resaltan 1,8 mm), a los dos lados: una sola pieza
    # en L por lado (arriba, atras y abajo).
    regleta = cal.puntos([(184, 173), (750, 173), (750, 200), (200, 200), (200, 307), (510, 307), (510, 335),
                          (184, 335)])
    for s in (1, -1):
        z0, z1 = sorted((s * (hw - mm(0.5)), s * (hw + mm(1.8))))
        k.perfil(regleta, desde=z0, hasta=z1, material=pavon, bisel=mm(0.6), segmentos=1, nombre="regleta")

    # La corredera de montar (lado derecho): su placa con la ranura, la palanca y el asa que sale de lado.
    placa = k.perfil(k.redondear(cal.puntos([(288, 205), (560, 205), (560, 262), (288, 262)]), mm(3), 2),
                     desde=hw - mm(1.0), hasta=hw + mm(3.0), material=pavon, bisel=mm(0.6), segmentos=1,
                     nombre="corredera")
    k.restar(placa, k.perfil(ranura, desde=hw - mm(3), hasta=hw + mm(5)))
    k.perfil(cal.puntos([(500, 218), (545, 213), (578, 222), (580, 262), (562, 270), (540, 270), (505, 242)]),
             desde=hw + mm(2.5), hasta=hw + mm(6.5), material=pavon, bisel=mm(0.6), segmentos=1,
             nombre="palanca_corredera")
    asa = k.torno([(0.0, mm(9.5)), (mm(10), mm(9.5)), (mm(12), mm(11.5)), (mm(54), mm(11.5)), (mm(57), mm(10.5)),
                   (mm(57), 0.0)], eje="z", origen=(X(531), Y(199), hw + mm(2)), segmentos=14, material=pavon,
                  nombre="asa_corredera")
    k.restar(asa, k.cilindro((X(531), Y(199), hw + mm(47)), (X(531), Y(199), hw + mm(62)), mm(6.5), 10))
    k.perfil(cal.puntos([(518, 188), (545, 188), (545, 212), (518, 212)]), desde=hw, hasta=hw + mm(5),
             material=pavon, bisel=mm(0.6), segmentos=1, nombre="pie_asa")

    # Remaches (los de la lamina en el lado derecho; el izquierdo, igual): en las regletas y en la chapa.
    en_regleta = ([(x, 187) for x in (196, 248, 300, 352, 404, 456, 508, 600, 652, 704)]
                  + [(x, 321) for x in (248, 300, 352, 404, 452, 498)] + [(192, 230), (192, 268), (192, 321)])
    en_chapa = [(560, 312), (640, 300), (700, 300), (760, 300), (820, 300), (888, 222), (888, 262)]
    for s in (1, -1):
        _remaches(k, cal, en_regleta, s * (hw + mm(1.3)), s, pavon)
        _remaches(k, cal, en_chapa, s * (hw - mm(0.3)), s, pavon)

    # ================================================================ alza (atras, encima de la tapa)
    k.perfil(cal.puntos([(243, 168), (246, 152), (252, 150), (274, 150), (277, 146), (432, 143), (437, 148),
                         (437, 168)]), ancho=mm(44), material=pavon, bisel=mm(1.0), segmentos=1, nombre="base_alza")
    k.perfil(cal.puntos([(276, 148), (276, 113), (282, 108), (327, 108), (332, 113), (332, 148)]), ancho=mm(30),
             material=pavon, bisel=mm(0.8), segmentos=1, nombre="alza")
    k.perfil(cal.puntos([(332, 148), (335, 111), (345, 110), (349, 148)]), ancho=mm(10), material=pavon,
             bisel=mm(0.5), segmentos=1, nombre="poste_alza")
    k.torno([(0.0, mm(3.6)), (Y(97) - Y(110), mm(3.6)), (Y(97) - Y(110), mm(10)), (Y(82) - Y(110), mm(10.5)),
             (Y(80) - Y(110), mm(8)), (Y(80) - Y(110), 0.0)], eje="y", origen=(X(300), Y(110), 0.0), segmentos=12,
            material=pavon, nombre="perilla_alza")
    _rodillo(k, mm(14), mm(24), mm(7.0), mm(1.0), "z", (X(297), Y(150), 0.0), 10, pavon, "perilla_deriva")

    # ================================================================ cachas, brazos, mariposa y amortiguador
    xc = X(75.5)
    for s in (1, -1):
        zc = s * mm(95)
        alto = Y(186) - Y(332)
        k.torno([(0.0, mm(14.5)), (mm(4), mm(17.0)), (alto * 0.5, mm(18.3)), (alto - mm(4), mm(17.0)),
                 (alto, mm(14.5))], eje="y", origen=(xc, Y(332), zc), segmentos=16, material=goma, nombre="cacha")
        for y0, y1 in ((343, 330), (188, 177)):
            _rodillo(k, 0.0, Y(y1) - Y(y0), mm(19.5), mm(1.5), "y", (xc, Y(y0), zc), 14, pavon, "casquillo_cacha")
        # los brazos de arriba y de abajo, de la cacha a la placa trasera (planta: x, z)
        brazo = [(xc - mm(14), zc - mm(9)), (xc + mm(8), zc - mm(9)), (X(172), s * mm(56) - mm(9)),
                 (X(172), s * mm(56) + mm(9)), (xc + mm(8), zc + mm(9)), (xc - mm(14), zc + mm(9))]
        for y0, y1 in ((194, 185), (336, 327)):
            k.perfil(brazo, plano="xz", desde=Y(y0), hasta=Y(y1), material=pavon, bisel=mm(1.0), segmentos=1,
                     nombre="brazo_cacha")
    # El gatillo de mariposa (entre las cachas, arriba) y el amortiguador con su tapa.
    k.perfil(cal.puntos([(104, 194), (122, 197), (142, 208), (157, 222), (165, 234), (158, 236), (149, 225),
                         (136, 213), (118, 203), (104, 200)]), ancho=mm(56), material=pavon, bisel=mm(0.8),
             segmentos=1, nombre="mariposa")
    k.torno([(X(100), 0.0), (X(100), mm(15)), (X(103), mm(17.5)), (X(145), mm(17.5)), (X(145), mm(22.5)),
             (X(151), mm(22.5)), (X(151), mm(27)), (X(156), mm(30)), (X(178), mm(30)), (X(178), 0.0)], eje="x",
            origen=(0.0, Y(250), 0.0), segmentos=20, material=pavon, nombre="amortiguador")

    # ================================================================ gatillo de la chapa (debajo, atras)
    # El guardamonte es una J: abierto por delante y arriba (la punta no llega a la chapa), como en la lamina.
    k.perfil(cal.puntos([(272, 334), (282, 352), (296, 372), (307, 382), (390, 382), (398, 378), (401, 369),
                         (399, 362), (392, 363), (388, 371), (348, 371), (339, 365), (335, 352), (334, 334)]),
             ancho=mm(8), material=pavon, bisel=mm(1.0), segmentos=1, nombre="guardamonte")
    k.perfil(cal.puntos([(272, 334), (336, 334), (336, 349), (280, 349)]), ancho=mm(26), material=pavon,
             bisel=mm(1.0), segmentos=1, nombre="caja_gatillo")
    k.perfil(cal.puntos([(332, 346), (343, 346), (349, 357), (351, 368), (346, 370), (340, 360), (333, 352)]),
             ancho=mm(6), material=pavon, bisel=mm(0.5), segmentos=1, nombre="gatillo")

    # ================================================================ manguito, camisa perforada y canon
    k.torno([(X(902), 0.0), (X(902), mm(42.5)), (X(904), mm(44)), (X(944), mm(44)), (X(950), mm(40)),
             (X(950), 0.0)], eje="x", segmentos=24, material=pavon, nombre="manguito")
    r_ext, r_int = mm(37.0), mm(34.0)
    camisa = k.torno([(X(948), r_int), (X(948), r_ext - mm(0.8)), (X(949), r_ext), (X(1284), r_ext),
                      (X(1285), r_ext - mm(0.8)), (X(1285), r_int)], eje="x", segmentos=24, material=pavon,
                     cerrado=True, nombre="camisa")
    agujeros = []
    for fila in range(8):
        a = math.radians(45.0 * fila)
        d = (0.0, math.sin(a), math.cos(a))
        for px in (FILA_A if fila % 2 == 0 else FILA_B):
            agujeros.append(k.cilindro((X(px), d[1] * mm(31), d[2] * mm(31)), (X(px), d[1] * mm(41), d[2] * mm(41)),
                                       mm(9.9), 8, fase=22.5))
    k.restar(camisa, k.unir(*agujeros))
    # El collarin de delante de la camisa (con la abrazadera del punto de mira) y la tuerca del canon.
    k.torno([(X(1283), 0.0), (X(1283), mm(30.5)), (X(1289), mm(32)), (X(1359), mm(32)), (X(1366), mm(29)),
             (X(1366), 0.0)], eje="x", segmentos=24, material=pavon, nombre="collarin")
    k.cilindro((X(1298), Y(231), mm(30)), (X(1384), Y(231), mm(30)), mm(4.0), 8, material=pavon, nombre="abrazadera")
    k.torno([(X(1364), 0.0), (X(1364), mm(22)), (X(1366), mm(23)), (X(1388), mm(23)), (X(1392), mm(21.5)),
             (X(1392), 0.0)], eje="x", segmentos=20, material=acero, nombre="tuerca_canon")
    mira = k.perfil(cal.puntos([(1322, 205), (1324, 168), (1329, 152), (1335, 147), (1352, 147), (1358, 152),
                                (1361, 168), (1362, 205)]), ancho=mm(13), material=pavon, bisel=mm(0.6), segmentos=1,
                    nombre="punto_mira")
    k.restar(mira, k.caja((X(1330), Y(162), -mm(3.2)), (X(1366), Y(140), mm(3.2))))
    k.perfil(cal.puntos([(1336, 165), (1340, 151), (1348, 151), (1351, 165)]), ancho=mm(2.4), material=pavon,
             nombre="hoja_mira")
    # El canon (pesado, algo conico), dentro de la camisa y hasta la bocacha. Dentro de la camisa es mas grueso (el
    # refuerzo de la recamara): por los agujeros se ve el canon, como en la lamina, y casi no se ve a traves.
    k.torno([(X(930), 0.0), (X(930), mm(26)), (X(1290), mm(26)), (X(1300), mm(22.4)), (X(1390), mm(21.3)),
             (X(1925), mm(19.4)), (X(1925), 0.0)], eje="x", segmentos=20, material=acero, nombre="canon")
    k.torno([(X(1918), 0.0), (X(1918), mm(20.5)), (X(1922), mm(21.5)), (X(1938), mm(21.5)), (X(1940), mm(20.5)),
             (X(1940), 0.0)], eje="x", segmentos=20, material=acero, nombre="anillo_canon")

    # ================================================================ bocacha (apagallamas ranurado)
    boc = k.torno([(X(1938), 0.0), (X(1938), mm(21)), (X(1975), mm(27.5)), (X(2124), mm(27.5)), (X(2145), mm(22.5)),
                   (X(2145), 0.0)], eje="x", segmentos=24, material=acero, nombre="bocacha")
    huecos = [k.cilindro((X(2160), 0.0, 0.0), (X(2028), 0.0, 0.0), mm(15.5), 20),
              k.cilindro((X(2040), 0.0, 0.0), (X(1960), 0.0, 0.0), mm(7.0), 10)]
    ranura_b = k.redondear([(X(2033), -mm(4.2)), (X(2128), -mm(4.2)), (X(2128), mm(4.2)), (X(2033), mm(4.2))],
                           mm(4.1), 2)
    for g in (30.0, 150.0, 210.0, 330.0):
        huecos.append(k.girar(k.perfil(ranura_b, desde=mm(10), hasta=mm(34)), g, "x"))
    k.restar(boc, k.unir(*huecos))

    # ================================================================ soporte: la cuna
    # Un brazo a cada lado del cajon (de la orejeta de atras al bloque de delante, con el soporte del munon que sube
    # por el costado), el bloque de delante entre los dos (apoya en el collarin) y el eje de atras con su palanca.
    lado = [(400, 352), (406, 343), (420, 341), (520, 342), (610, 343), (700, 337), (790, 331), (838, 333),
            (838, 287), (848, 277), (928, 277), (938, 287), (938, 335), (985, 335), (995, 345), (995, 415), (985, 425),
            (820, 425), (800, 422), (750, 420), (650, 417), (560, 414), (532, 409), (527, 390), (420, 390),
            (406, 386), (400, 375)]
    for s in (1, -1):
        z0, z1 = sorted((s * mm(40), s * mm(68)))
        brazo = k.perfil(cal.puntos(lado), desde=z0, hasta=z1, material=oliva, nombre="cuna")
        k.biselar(brazo, mm(2.2), 1, 30)
        # el agujero grande pasa; el de atras es un rebaje con el pasador dentro (en la lamina no se ve a traves)
        k.restar(brazo, k.unir(k.cilindro((X(725), Y(375), -mm(80)), (X(725), Y(375), mm(80)), cal.dx(17), 14),
                               k.cilindro((X(630), Y(377), s * mm(56)), (X(630), Y(377), s * mm(80)), cal.dx(15), 14)))
        k.cilindro((X(630), Y(377), s * mm(55)), (X(630), Y(377), s * mm(64)), cal.dx(8), 10, material=oliva,
                   nombre="pasador_cuna")
        _tornillos(k, [(X(502), Y(372), s * mm(68)), (X(547), Y(372), s * mm(68)), (X(875), Y(375), s * mm(68)),
                       (X(977), Y(362), s * mm(68)), (X(977), Y(412), s * mm(68))], (0.0, 0.0, s), mm(8.5), mm(6.0),
                   oliva)
        # el munon (el perno grueso que sujeta el cajon) con su arandela
        _tornillos(k, [(X(875), Y(300), s * mm(68))], (0.0, 0.0, s), mm(13), mm(9), pavon, arandela=mm(18.5))
        _rodillo(k, s * mm(66), s * mm(76), cal.dx(20), mm(1.5), "z", (X(445), Y(365), 0.0), 14, oliva, "cabeza_eje")
    k.caja((X(820), Y(425), -mm(40.5)), (X(995), Y(338), mm(40.5)), material=oliva, bisel=mm(2.0), segmentos=1,
           nombre="bloque")
    k.cilindro((X(445), Y(365), -mm(70)), (X(445), Y(365), mm(70)), mm(10), 12, material=oliva, nombre="eje_cuna")
    k.perfil(cal.puntos([(438, 362), (452, 362), (455, 392), (441, 392)]), desde=mm(76), hasta=mm(82),
             material=oliva, bisel=mm(1.0), segmentos=1, nombre="palanca_freno")
    k.torno([(0.0, mm(5)), (mm(4), mm(5)), (mm(6), mm(10)), (Y(392) - Y(404), mm(11)), (Y(392) - Y(405), mm(9)),
             (Y(392) - Y(405), 0.0)], eje=(0.0, -1.0, 0.0), origen=(X(448), Y(392), mm(79)), segmentos=12,
            material=oliva, nombre="pomo_freno")

    # ================================================================ soporte: la cabeza del pedestal (y el pie)
    # Lo que gira con el arma: el collarin de arriba de la columna (con la cadena del pasador en la lamina, que no se
    # modela) y una espiga de la columna de 40 mm, achaflanada abajo. La cara de abajo de la espiga es el pivote: de ahi
    # baja el poste que pone el juego, del radio de la columna y del alto que haga falta.
    xp = X(PX_PIVOTE)
    alto_c = Y(427) - Y(PY_COLLARIN)
    k.torno([(0.0, mm(46)), (mm(2), mm(48)), (alto_c - mm(2), mm(48)), (alto_c, mm(46)), (alto_c, 0.0)], eje="y",
            origen=(xp, Y(PY_COLLARIN), 0.0), segmentos=24, material=oliva, nombre="collarin_pedestal")
    if not CON_BASE:
        y_pie = Y(PY_COLLARIN) - ESPIGA
        k.torno([(0.0, RADIO_COLUMNA - mm(1.5)), (mm(1.5), RADIO_COLUMNA), (ESPIGA + mm(2), RADIO_COLUMNA),
                 (ESPIGA + mm(2), 0.0)], eje="y", origen=(xp, y_pie, 0.0), segmentos=24, material=oliva,
                nombre="espiga")
    else:
        # El pie de la lamina: la columna, cuatro cartabones y la placa redonda con sus cuatro tornillos. En la lamina
        # la placa se ve desde un poco arriba (una elipse): de lado, su canto va de la y 652 a la 669 (lo mas bajo).
        y_pie = Y(669)
        k.torno([(0.0, RADIO_COLUMNA), (Y(PY_COLLARIN) - Y(653), RADIO_COLUMNA), (Y(PY_COLLARIN) - Y(653), 0.0)],
                eje="y", origen=(xp, Y(653), 0.0), segmentos=24, material=oliva, nombre="columna")
        # el canto del cartabon de la lamina: a 57 px del eje en la y 528 y 0,95 px mas por cada px que baja
        u0, u1 = mm(38), cal.dx(162)
        cartabon = [(u0, Y(522)), (u0, Y(653)), (u1, Y(653)), (u1, Y(641))]
        for a in (0.0, 90.0, 180.0, 270.0):
            c, s_ = math.cos(math.radians(a)), math.sin(math.radians(a))
            if abs(c) > 0.5:          # en el plano xy (los que se ven de lado como triangulos)
                k.perfil([(xp + c * u, y) for u, y in cartabon], ancho=mm(10), material=oliva, bisel=mm(1.0),
                         segmentos=1, nombre="cartabon")
            else:                     # en el plano zy (de lado se ven de canto)
                k.perfil([(s_ * u, y) for u, y in cartabon], plano="zy", desde=xp - mm(5), hasta=xp + mm(5),
                         material=oliva, bisel=mm(1.0), segmentos=1, nombre="cartabon")
        R, t, ch = cal.dx(211), Y(652) - Y(669), mm(2.0)
        k.torno([(0.0, 0.0), (0.0, R - ch), (ch, R), (t - ch, R), (t, R - ch), (t, 0.0)], eje="y",
                origen=(xp, Y(669), 0.0), segmentos=40, material=oliva, nombre="placa_base")
        r_t = cal.dx(180)
        _tornillos(k, [(xp + r_t * math.cos(math.radians(a)), Y(652), r_t * math.sin(math.radians(a)))
                       for a in (45.0, 135.0, 225.0, 315.0)], "y", mm(10), mm(8), oliva, arandela=mm(13.5))

    if os.environ.get("GD_ARMAS_DEPURAR"):
        _contar(k)

    # El montaje para el juego, respecto al agarre (main() lo pasa al origen y lo pone en el _agarre.json): el pivote
    # (la cara de abajo de la espiga o de la placa, sobre el eje vertical de la columna) y el radio del poste.
    agarre = cal.p(75.5, 261, 0.0)
    pivote = [round(xp - agarre[0], 5), round(y_pie - agarre[1], 5), 0.0]
    montaje = {"pivote_desde_agarre": pivote, "eje_giro": "+Y", "radio_columna": RADIO_COLUMNA, "base": CON_BASE}
    notas = [f"pivote del montaje ({'cara de abajo de la placa' if CON_BASE else 'cara de abajo de la espiga'}, "
             f"eje vertical de la columna) respecto al agarre: [{pivote[0]:.4f}, {pivote[1]:.4f}, {pivote[2]:.4f}] m",
             "guardamanos = la cacha izquierda (-Z)", {"montaje": montaje}]
    return {"agarre": agarre, "boca": (X(PX_BOCA), 0.0, 0.0), "guardamanos": (xc, Y(261), -mm(95)),
            "calibracion": cal, "notas": notas}


# ------------------------------------------------------------------ construirla (con la comparacion volteada)
def _comparar_volteada(revision=None):
    """La comparacion de construir_arma.comparar con la lamina lateral volteada: esta tiene la boca a la derecha y el
    kit compara siempre con la boca a la izquierda. Escribe una copia volteada de la lamina en trabajo/ y se la pasa a
    comparar() cambiando, solo mientras dura, la ruta de la lamina y la caja de su contorno. Si la malla no lleva el pie
    (lo de serie), la copia tampoco: por debajo de la espiga se pinta el fondo de estudio que estima laminas.py (el mismo
    que resta la silueta, asi que alli no queda nada) y la caja sale de la silueta de la copia. La de tres cuartos no se
    voltea (la camara de la FICHA ya mira desde la derecha) y se queda con su pie."""
    import construir_arma as ca
    import laminas
    import numpy as np
    from PIL import Image, ImageOps
    nombre = FICHA["nombre"]
    _ruta, ficha = ca.cargar_ficha(nombre)
    _ac, _salida, trabajo, rev = ca.carpetas(nombre, revision)
    original = laminas.ruta_lamina(FICHA["lamina"], "lateral")
    im = Image.open(original).convert("RGB")
    ancho = im.width
    caja = None
    if CON_BASE:
        volteada = os.path.join(trabajo, nombre + "_lamina_lateral_volteada.png")
    else:
        volteada = os.path.join(trabajo, nombre + "_lamina_lateral_volteada_sin_pie.png")
        rgb = np.asarray(im, np.float32)
        fondo = laminas.fondo_estimado(rgb)
        corte = int(math.ceil(PY_COLLARIN + ESPIGA * abs(PX_BOCA - PX_COLA) / LARGO)) + 1
        b = 24                       # el marco del que laminas.fondo_estimado saca el fondo: no se toca
        rgb[corte:-b, b:-b] = fondo[corte:-b, b:-b]
        im = Image.fromarray(np.clip(rgb + 0.5, 0, 255).astype(np.uint8))
    ImageOps.mirror(im).save(volteada)
    if not CON_BASE:
        caja = list(laminas.caja(laminas.silueta(volteada, FICHA.get("umbral", 22.0))))
    ruta_lamina, leer_json = laminas.ruta_lamina, ca.leer_json

    def ruta_lamina_v(lamina, vista=None):
        return volteada if vista == "lateral" else ruta_lamina(lamina, vista)

    def leer_json_v(ruta):
        d = leer_json(ruta)
        if ruta.endswith("_lamina_lateral.json"):
            x0, y0, x1, y1 = d["caja"]
            d = dict(d, caja=caja or [ancho - 1 - x1, y0, ancho - 1 - x0, y1])
        return d

    laminas.ruta_lamina, ca.leer_json = ruta_lamina_v, leer_json_v
    try:
        return ca.comparar(nombre, ficha, trabajo, rev)
    finally:
        laminas.ruta_lamina, ca.leer_json = ruta_lamina, leer_json


def _rugosidad_pedida(ca, nombre):
    """La `rugosidad` de cada k.material del modelo (la apunta el _medidas.json). -> {material: rugosidad}."""
    _ac, _salida, trabajo, _rev = ca.carpetas(nombre)
    m = ca.leer_json(os.path.join(trabajo, nombre + "_medidas.json"))
    return {mt: d["rugosidad"] for mt, d in m.get("materiales", {}).items()}


def _escribir_acabado(ca, nombre):
    """El anexo <n>.veta.json con el acabado `rugosidad` de cada material: lo que la FICHA pide (la `rugosidad` de cada
    k.material) menos la media de su receta. El texturizador la suma a su mapa (texturizar_obj.aplicar_acabado_veta);
    un material sin `dir` ni `vuelta` no cambia de UV. Respeta lo que el modelo haya escrito en el anexo (la veta de una
    madera). Va despues del paso del modelo, que lo borra si no hay maderas."""
    ac, salida, _trabajo, _rev = ca.carpetas(nombre)
    pedidas = _rugosidad_pedida(ca, nombre)
    recetas = ca.recetas_de(ac, sorted(pedidas))
    ruta = os.path.join(salida, nombre + ".veta.json")
    anexo = ca.leer_json(ruta) if os.path.isfile(ruta) else {}
    mats = anexo.setdefault("materiales", {})
    for mt, r in sorted(pedidas.items()):
        base = RUGOSIDAD_RECETA.get(recetas.get(mt))
        if base is None:
            print(f"   AVISO: {mt} ({recetas.get(mt)}): no se la rugosidad de su receta, se queda la suya")
            continue
        mats.setdefault(mt, {})["rugosidad"] = round(r - base, 3)
    with open(ruta, "w", encoding="utf-8") as f:
        json.dump(anexo, f, indent=1)
    print("-- acabado (anexo de veta): " + ", ".join(f"{mt} {d['rugosidad']:+.3f}" for mt, d in sorted(mats.items())
                                                    if "rugosidad" in d))


def _comprobar_rugosidad(ca, nombre):
    """La rugosidad media de cada <n>_pbr_<material>_roughness.png frente a la pedida. -> {material: media}."""
    import numpy as np
    from PIL import Image
    _ac, salida, _trabajo, _rev = ca.carpetas(nombre)
    medidas = {}
    for mt, r in sorted(_rugosidad_pedida(ca, nombre).items()):
        ruta = os.path.join(salida, f"{nombre}_pbr_{mt}_roughness.png")
        if not os.path.isfile(ruta):
            continue
        medidas[mt] = round(float(np.asarray(Image.open(ruta).convert("L"), np.float32).mean() / 255.0), 3)
        aviso = "" if abs(medidas[mt] - r) <= 0.03 else ("   AVISO: no casa (el texturizador de ahora no aplica el "
                                                         "acabado del anexo de veta?)")
        print(f"   rugosidad {mt}: pedida {r:.2f}, en el mapa {medidas[mt]:.3f}{aviso}")
    return medidas


def _completar_agarre(ca, nombre, res, rugosidad):
    """Anade al _agarre.json del kit el bloque `montaje` (el pivote respecto al origen del OBJ) y la rugosidad medida
    de los mapas, y cambia la receta por este fichero (construir_arma.py a secas no escribe el acabado)."""
    ac, salida, trabajo, _rev = ca.carpetas(nombre)
    ruta = os.path.join(salida, nombre + "_agarre.json")
    d = ca.leer_json(ruta)
    m = ca.leer_json(os.path.join(trabajo, nombre + "_medidas.json"))
    mont = next((n["montaje"] for n in m.get("notas", []) if isinstance(n, dict) and "montaje" in n), None)
    if mont is not None:
        piv = [round(p + a, 5) for p, a in zip(mont["pivote_desde_agarre"], m["agarre"])]
        d["montaje"] = {
            "pivote": piv,
            "eje_giro": mont["eje_giro"],
            "radio_columna": mont["radio_columna"],
            "base": mont["base"],
            "nota": ("La malla lleva el pie entero de la lamina (la placa a {:.3f} m del agarre): va apoyada en un suelo "
                     "plano en el pivote.".format(-piv[1]) if mont["base"] else
                     "La malla acaba en la espiga de la columna, bajo el collarin: el poste hasta el techo o el suelo "
                     "lo pone el juego (un cilindro de radio_columna desde el pivote hacia -Y, del alto que haga "
                     "falta). El arma gira en direccion alrededor del eje vertical que pasa por el pivote. En Unity "
                     "la x cambia de signo: el pivote queda en [{:.5f}, {:.5f}, {:.5f}].".format(-piv[0], piv[1],
                                                                                              piv[2])),
        }
    if rugosidad:
        d["rugosidad"] = rugosidad
    python = os.path.join(ac, ".venv", "Scripts", "python.exe")
    d["receta"] = (("set GD_M2_BASE=1 && " if CON_BASE else "")
                   + f'"{python}" -B "{os.path.abspath(__file__)}" --res {res or FICHA["texturas"]["res"]}')
    texto = json.dumps(d, indent=1, ensure_ascii=False)
    texto = re.sub(r"\[\s+([-\d.e]+),\s+([-\d.e]+),\s+([-\d.e]+)\s+\]", r"[\1, \2, \3]", texto)
    with open(ruta, "w", encoding="utf-8") as f:
        f.write(texto + "\n")
    return d


def main():
    import argparse
    sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
    import construir_arma as ca
    p = argparse.ArgumentParser(description="Construye gd_m2_browning con construir_arma.py, con el acabado de la "
                                            "rugosidad, el montaje en el _agarre.json y la comparacion con la lamina "
                                            "volteada (boca a la derecha).")
    p.add_argument("--res", type=int, default=0)
    p.add_argument("--solo", default="laminas,modelo,texturas,agarre,comparar")
    p.add_argument("--revision", default=None)
    p.add_argument("--blender", default=None)
    a = p.parse_args()
    nombre = FICHA["nombre"]
    pasos = [s for s in a.solo.split(",") if s]

    def construir(lista):
        argv = ["construir_arma.py", nombre, "--solo", ",".join(lista)]
        if a.res:
            argv += ["--res", str(a.res)]
        if a.revision:
            argv += ["--revision", a.revision]
        if a.blender:
            argv += ["--blender", a.blender]
        sys.argv = argv
        ca.main()

    if CON_BASE:
        print("-- GD_M2_BASE: la malla lleva el pie entero de la lamina")
    antes = [s for s in pasos if s in ("laminas", "modelo")]
    despues = [s for s in pasos if s in ("texturas", "agarre")]
    if antes:
        construir(antes)
    if "texturas" in pasos:
        _escribir_acabado(ca, nombre)
    if despues:
        construir(despues)
        d = _completar_agarre(ca, nombre, a.res, _comprobar_rugosidad(ca, nombre))
        print(f"-- montaje en el _agarre.json: {d.get('montaje', {}).get('pivote')}")
    if "comparar" in pasos:
        ruta, i_a, i_i = _comparar_volteada(a.revision)
        print(f"-- comparacion (lamina volteada{'' if CON_BASE else ', sin el pie'}): {ruta} "
              f"(IoU {i_a:.3f} ajustada, {i_i:.3f} por el largo)")
    return 0


if __name__ == "__main__":
    sys.exit(main())
