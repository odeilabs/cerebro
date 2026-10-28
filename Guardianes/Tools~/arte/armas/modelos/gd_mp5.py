"""gd_mp5: Heckler & Koch MP5A3 (9 x 19 mm, culata retractil de dos varillas), el subfusil de los traficantes de
Basiliskos (junto a la AK-47 y la Glock 17) y de cualquier juego de la saga que necesite un subfusil moderno. Se empuna
con los clips `subfusil_*` de la saga, que son de un MP5.

SIN LAMINA. AssetsCreator no tiene lamina del MP5 (sus 30 armas de fuego no lo incluyen), asi que este fichero no se
lanza con construir_arma.py (que la necesita para los contornos y la comparacion) sino solo, con el python del .venv de
AssetsCreator; usa los pasos del kit (modelo, texturas, agarre) y hace su propia comparacion:

    python modelos/gd_mp5.py [--res 1024] [--solo modelo,texturas,agarre,comparar] [--revision <carpeta>]
                             [--foto <png>] [--origen agarre|linea] [--blender <blender.exe>]

(la hoja de contactos, como siempre: construir_arma.py --hoja gd_mp5,... solo lee el _pbr.obj).

La referencia de proporciones es una foto real de lado: la del MP5A3 de la hoja de identificacion de la Small Arms Survey
(https://www.smallarmssurvey.org/sites/default/files/SAS-weapons-sub-machine-guns-Heckler-Koch-MP5.pdf, pagina 1,
«Model illustrated: H&K MP5A3»: la primera imagen de la pagina, 733 x 383 px, lado derecho con la ventana de expulsion,
culata metida, la boca a la derecha). No se versiona: la comparacion la busca en --foto o en
proyectos/gd_mp5/trabajo/gd_mp5_referencia_lateral.png, y sin ella compara solo las medidas. Las piezas que el MP5
comparte con la G3 (el alza de tambor, el punto de mira en tunel, el tubo de la palanca de montar, el grupo del
disparador) se han mirado tambien en la lamina 024-hk-g3, que es de la misma familia; sale en la comparacion.

Medidas reales: linea de mira 340 mm y canon 225 mm (HK); ancho 50 mm (HK USA); largo 660/490 mm con la culata
sacada/metida (HK en los 80 y las licencias de Noruega y Pakistan), 700/550 mm (HK USA hoy) o 680 mm (Small Arms Survey);
alto con el cargador curvo de 30, unos 260 mm (HK USA da 230). Los pixeles de abajo son de esa foto (x a la derecha, y
hacia abajo). La escala sale de la linea de mira: del poste del punto de mira (x = 653) al tambor del alza (x = 185),
468 px = 340 mm, 0,7265 mm/px; con ella el canon de 225 mm acaba justo delante del cargador (donde esta la recamara) y
el cargador mide 32 mm de fondo (un cartucho de 9 x 19 y sus chapas), asi que la foto no esta estirada. Da 511 mm de
largo con la culata metida (entre los 490 y los 550 de HK) y 270 de alto. La culata sale 170 mm (la carrera de los
datos de HK de 660/490): 681 mm sacada, la que se entrega. Con GD_MP5_CULATA=metida en el entorno sale metida.

EN UNITY el paquete lee el _agarre.json (ConstructorHumanos.TraerArma lo copia y LeerFicha lo lee): la mano va en el
agarre (el centro del pistolete; mas ConstructorHumanos.DesfaseAgarre(clase), vacio de serie) y la clase, Subfusil, sale de
la ficha o de su prefijo en Arma.PrefijosSerie (sin el, Arma.ClaseDe("gd_mp5") daria FusilAsalto, calibre 8 en vez del 1
del subfusil: en Basiliskos sus balas no rebotarian en el basilisco). Sin la ficha, la regla del 28 % de EjesFusil cogeria
el arma por las varillas de la culata, 9 cm detras y 9 cm encima del pistolete (con la culata sacada el pistolete esta al
41 % del largo). El bloque "unity" del _agarre.json es de consulta: la mano y la boca en el espacio de Unity (con la x
cambiada de signo), si el cerebro ya tiene el prefijo y la lectura de la ficha, y lo que haria la regla sin ficha; la
comparacion pinta los dos puntos. El humano que la lleve, ademas, con Def.familia = "subfusil" (los clips del MP5).
"""
import json
import math
import os
import re
import subprocess
import sys

FICHA = {
    "nombre": "gd_mp5",
    "clase": "Subfusil",
    "lamina": None,               # AssetsCreator no la tiene: ver el docstring (foto real de la Small Arms Survey)
    "para": "traficantes de Basiliskos (junto a la AK-47 y la Glock 17); cualquier subfusil moderno de la saga",
    "medidas_reales": {"linea_mira": 0.340, "canon": 0.225, "ancho": 0.050, "largo_sacada": [0.660, 0.700],
                       "largo_metida": [0.490, 0.550], "alto_cargador_30": [0.230, 0.260]},
    "texturas": {"res": 1024, "escala": 0.4},   # 0,4 m por tesela: la densidad de todas las gd_
    "vista_34": {"azimut": 30.0, "elevacion": 12.0, "focal": 100.0},
    "semilla": 5,
    "culata": "sacada",           # sacada (681 mm, la de serie) o metida (511 mm); GD_MP5_CULATA la cambia
}

# ------------------------------------------------------------------ la foto de referencia y lo que sale de ella
S_FOTO = 0.340 / (653 - 185)        # metros por pixel: la linea de mira real (340 mm) del poste al alza
PX_BOCA, PY_EJE = 715, 96           # la cara de la boca y el eje del canon en la foto
PX_POSTE, PX_ALZA = 653, 185        # el poste del punto de mira y el tambor del alza
AGARRE_PX = (163, 247)              # centro de la empunadura a la altura del dedo corazon (bajo el guardamonte)
GUARDAMANOS_PX = (541.5, 96)        # centro del guardamanos (de 443 a 640), en el eje
CARRERA = 0.170                     # lo que sale la culata de metida a sacada
VARILLA = 0.240                     # largo de cada varilla de la culata
MEDIO_CAJON = 0.0175                # medio ancho del cajon de mecanismos (35 mm)
PX_M_COMPARAR = 1750.0              # pixeles por metro de los paneles de la comparacion de lado
# El punto de la mano respecto al agarre de la ficha: ConstructorHumanos.DesfaseAgarre(clase), vacio de serie (con el
# agarre en el centro del pistolete la mano lo coge bien; el del AK, 6 cm hacia la culata y 4 cm abajo, la dejaba debajo).
DESFASE_MANO = (0.0, 0.0)           # en Unity: +x es hacia la culata

AQUI = os.path.dirname(os.path.abspath(__file__))
KIT = os.path.dirname(AQUI)
PAQUETE = os.path.normpath(os.path.join(KIT, "..", "..", ".."))   # cerebro/Guardianes: solo se lee


def _culata():
    return (os.environ.get("GD_MP5_CULATA") or FICHA.get("culata") or "sacada").strip().lower()


def _barra(c, angulo, largo, ancho, atras=0.0):
    """Rectangulo 2D de una palanca que sale de `c` hacia `angulo` (grados), con `atras` por detras del centro."""
    ux, uy = math.cos(math.radians(angulo)), math.sin(math.radians(angulo))
    nx, ny = -uy, ux
    a0 = (c[0] - ux * atras, c[1] - uy * atras)
    a1 = (c[0] + ux * largo, c[1] + uy * largo)
    h = ancho * 0.5
    return [(a0[0] + nx * h, a0[1] + ny * h), (a1[0] + nx * h, a1[1] + ny * h),
            (a1[0] - nx * h, a1[1] - ny * h), (a0[0] - nx * h, a0[1] - ny * h)]


def _interpolar(puntos, py):
    """La x de una polilinea [(px, py)...] (de arriba abajo) a la altura py."""
    for (x0, y0), (x1, y1) in zip(puntos[:-1], puntos[1:]):
        if min(y0, y1) <= py <= max(y0, y1) and y1 != y0:
            return x0 + (x1 - x0) * (py - y0) / (y1 - y0)
    return puntos[-1][0]


# ------------------------------------------------------------------ el modelo (dentro de Blender, con el kit)
def modelar(k):
    mm = k.mm
    cal = k.Calibracion(px_boca=PX_BOCA, px_cola=PX_ALZA, largo=S_FOTO * (PX_BOCA - PX_ALZA), py_eje=PY_EJE,
                        boca_izquierda=False)
    sacada = _culata() != "metida"
    dx_culata = -CARRERA if sacada else 0.0

    # Pavonado negro mate del cajon (la receta de pintura: 'pavonado'/'acero' salen gris acero claro), polimero del
    # grupo del disparador, la empunadura y el guardamanos, acero del canon y del cierre, goma de la cantonera.
    negro = k.material("pintura_cajon", "#2a2c2f", rugosidad=0.7)
    # (el polimero, a ~50 sRGB: con el Kd mas negro el texturizador avisa de un albedo "mas oscuro que el carbon")
    polimero = k.material("plastico_armazon", "#323438", rugosidad=0.6)
    acero = k.material("acero_canon", "#3a3b3e", rugosidad=0.35, metal=1.0)
    goma = k.material("goma_cantonera", "#1b1b1c", rugosidad=0.9)

    # ---------------------------------------------------------------- cajon de mecanismos
    # Chapa estampada: arriba redonda (radio = medio ancho), los lados rectos y abajo chaflanes; lo alto del cajon
    # sigue en lo alto del tubo de montar.
    ya, yb = cal.y(49.5), cal.y(125)
    R = MEDIO_CAJON

    def seccion_cajon(r):
        yc = ya - R
        return ([(-r + mm(2), yb), (r - mm(2), yb), (r, yb + mm(2))] + k.arco((0.0, yc), r, 0, 180, 14)
                + [(-r, yb + mm(2))])

    cajon = k.perfil(cal.puntos([(100, 49.5), (443, 49.5), (443, 125), (100, 125)]), ancho=mm(40), material=negro,
                     nombre="cajon")
    k.intersecar(cajon, k.perfil(seccion_cajon(R), plano="zy", desde=cal.x(97), hasta=cal.x(446)))
    k.biselar(cajon, mm(1.0), 2, 30)
    # La tapa de atras (con la caida de arriba) donde entran las varillas de la culata.
    tapa = k.perfil(cal.puntos([(102, 52), (96, 52), (88, 64), (80, 84), (78, 90), (78, 123), (102, 123)]),
                    ancho=mm(40), material=negro, nombre="tapa")
    k.intersecar(tapa, k.perfil(seccion_cajon(R - mm(0.4)), plano="zy", desde=cal.x(76), hasta=cal.x(104)))
    k.biselar(tapa, mm(0.9), 2, 30)

    # Las guias de las varillas a los dos lados (la culata metida las llena), la nervadura de la chapa, la ventana de
    # expulsion (lado derecho, +Z) y las dos marcas de las garras del montaje de visor, arriba.
    fin_guia = cal.x(62) + VARILLA + mm(3)
    for pieza, x_atras in ((cajon, cal.x(102)), (tapa, cal.x(76))):
        guia = k.caja((x_atras, cal.y(118.5), R - mm(2.3)), (fin_guia, cal.y(97.5), R + mm(3)))
        k.restar(pieza, k.unir(guia, k.espejo(guia)))
    nervio = k.caja((cal.x(440), cal.y(78), R - mm(0.8)), (cal.x(105), cal.y(75.5), R + mm(2)))
    ventana = k.caja((cal.x(365), cal.y(76), mm(6.5)), (cal.x(317), cal.y(60), mm(21)))
    k.restar(cajon, k.unir(nervio, k.espejo(nervio)), ventana)
    for px in (280, 303):
        k.caja((cal.x(px + 2), cal.y(51), -mm(4)), (cal.x(px - 2), cal.y(46.5), mm(4)), material=negro,
               bisel=mm(0.4), nombre="garra")
    # El cierre de acero, visto por la ventana.
    k.cilindro((cal.x(370), cal.y(68), mm(7)), (cal.x(312), cal.y(68), mm(7)), mm(7), 16, material=acero,
               nombre="cierre")

    # ---------------------------------------------------------------- alza de tambor
    k.perfil(cal.puntos([(149, 56), (149, 49), (152, 47), (206, 47), (209, 49), (209, 56)]), ancho=mm(20),
             material=negro, bisel=mm(0.5), nombre="base_alza")
    caseta = k.perfil(cal.puntos([(155, 52), (155, 41), (157, 36), (160, 32.5), (165, 30), (172, 28.5), (180, 29),
                                  (186, 33), (186, 52)]), ancho=mm(24), material=negro, nombre="alza")
    sec_alza = k.redondear([(-mm(11), cal.y(52)), (mm(11), cal.y(52)), (mm(11), cal.y(28)), (-mm(11), cal.y(28))],
                           [0, 0, mm(7), mm(7)], 4)
    k.intersecar(caseta, k.perfil(sec_alza, plano="zy", desde=cal.x(150), hasta=cal.x(190)))
    k.biselar(caseta, mm(0.8), 2, 30)
    # El tambor, con su eje inclinado 17 grados hacia delante (la cara de arriba cae hacia la boca), y sus agujeros.
    inc = math.radians(17)
    eje = (math.sin(inc), math.cos(inc))
    cx, cy = cal.p(192.5, 36.5)
    h = mm(7.25)
    tambor = k.cilindro((cx - eje[0] * h, cy - eje[1] * h, 0), (cx + eje[0] * h, cy + eje[1] * h, 0), mm(10), 20,
                        material=negro, bisel=mm(0.8), nombre="tambor")
    k.restar(tambor, k.cilindro((cx, cy + mm(2.5), -mm(12)), (cx, cy + mm(2.5), mm(12)), mm(1.6), 10),
             k.cilindro((cx - mm(12), cy + mm(2.5), 0), (cx + mm(12), cy + mm(2.5), 0), mm(1.3), 10))
    for s in (1, -1):
        k.cilindro((cal.x(163), cal.y(45), s * mm(10.4)), (cal.x(163), cal.y(45), s * mm(11.6)), mm(2.4), 12,
                   material=negro, nombre="tornillo_alza")

    # ---------------------------------------------------------------- tubo de la palanca de montar
    y_tubo, r_tubo = cal.y(64), mm(10.5)
    tubo = k.cilindro((cal.x(436), y_tubo, 0), (cal.x(645), y_tubo, 0), r_tubo, 24, material=negro, bisel=mm(0.6),
                      nombre="tubo_montar")
    # La ranura de la palanca (lado izquierdo, -Z) con la muesca de atras, donde se deja montada.
    ranura = k.caja((cal.x(470), y_tubo - mm(2.6), -mm(13)), (cal.x(545), y_tubo + mm(2.6), -mm(6)))
    muesca = k.caja((cal.x(478), y_tubo, -mm(13)), (cal.x(470), y_tubo + mm(8), -mm(5)))
    k.restar(tubo, ranura, muesca)                 # se tocan: por separado (una herramienta que se corta a si misma
                                                   # deja vacia la booleana EXACT)
    k.cilindro((cal.x(616), y_tubo, 0), (cal.x(641), y_tubo, 0), mm(11.3), 24, material=negro, bisel=mm(0.6),
               nombre="anillo_tubo")
    # La palanca, delante en la ranura (cierre cerrado), hacia arriba y a la izquierda, con su pomo.
    xm = cal.x(538)
    a0 = (xm, y_tubo + mm(0.5), -mm(8.5))
    a1 = (xm, y_tubo + mm(8.5), -mm(20.5))
    palanca = k.barrido([a0, a1], mm(2.6), 10, material=negro, nombre="palanca_montar")
    k.escalar(palanca, (0.75, 1.0, 1.0), centro=a0)      # una pletina, no una varilla
    dl = math.hypot(a1[1] - a0[1], a1[2] - a0[2])
    d = (0.0, (a1[1] - a0[1]) / dl, (a1[2] - a0[2]) / dl)
    k.cilindro(a1, (a1[0], a1[1] + d[1] * mm(8), a1[2] + d[2] * mm(8)), mm(5.6), 18, material=negro, bisel=mm(1.2),
               nombre="pomo_montar")

    # ---------------------------------------------------------------- punto de mira en tunel
    # El bloque, visto desde la boca: un anillo alrededor del tubo y otro alrededor del canon unidos por un cuello.
    bloque = k.perfil(cal.puntos([(641, 46), (666, 46), (667, 58), (667, 104), (664, 110), (658, 113), (648, 113),
                                  (642, 110), (640, 104), (640, 52)]), ancho=mm(30), material=negro, nombre="bloque_mira")
    ab = math.degrees(math.acos(mm(9) / mm(11.5)))
    at = math.degrees(math.asin(-math.sqrt(mm(12.3) ** 2 - mm(9) ** 2) / mm(12.3)))
    sec_bloque = (k.arco((0.0, y_tubo), mm(12.3), at, 180 - at, 16)
                  + k.arco((0.0, 0.0), mm(11.5), 180 - ab, 360 + ab, 20))
    k.intersecar(bloque, k.perfil(sec_bloque, plano="zy", desde=cal.x(638), hasta=cal.x(669)))
    k.biselar(bloque, mm(1.2), 2, 30)
    xp, yt = cal.x(PX_POSTE), cal.y(27.5)          # el poste y el centro del tunel (la linea de mira)
    k.torno([(-mm(6), mm(12.6)), (mm(6), mm(12.6)), (mm(6), mm(14.6)), (mm(5.2), mm(15.6)), (mm(4), mm(16)),
             (-mm(4), mm(16)), (-mm(5.2), mm(15.6)), (-mm(6), mm(14.6))], eje="x", origen=(xp, yt, 0.0),
            segmentos=24, cerrado=True, material=negro, nombre="tunel_mira")
    k.caja((xp - mm(1.0), yt - mm(14), -mm(0.9)), (xp + mm(1.0), yt + mm(0.3), mm(0.9)), material=negro,
           bisel=mm(0.25), nombre="poste_mira")
    k.cilindro((cal.x(653), cal.y(82), -mm(10.4)), (cal.x(653), cal.y(82), mm(10.4)), mm(3.2), 14, material=negro,
               bisel=mm(0.4), nombre="pasador_mira")

    # ---------------------------------------------------------------- canon (lo que asoma) con sus tres tetones
    k.torno([(-mm(60), 0), (-mm(60), mm(6.2)), (-mm(31.5), mm(6.2)), (-mm(31), mm(7.6)), (-mm(29.8), mm(8.4)),
             (-mm(21), mm(8.4)), (-mm(19.6), mm(7.0)), (-mm(19), mm(6.5)), (-mm(0.8), mm(6.5)), (0, mm(5.8)),
             (0, mm(4.7)), (-mm(0.6), mm(4.5)), (-mm(14), mm(4.5)), (-mm(14), 0)],
            eje="x", segmentos=24, material=acero, nombre="canon")
    teton = k.caja((-mm(29), mm(7.8), -mm(2.1)), (-mm(22.5), mm(10.0), mm(2.1)), material=acero, bisel=mm(0.4),
                   nombre="teton")
    for g in (120, 240):
        k.girar(k.copiar(teton), g, "x")

    # ---------------------------------------------------------------- guardamanos delgado (polimero)
    gm = k.perfil(cal.puntos([(442, 67.5), (615, 67.5), (626, 69), (633, 73), (638, 82), (640, 96), (639, 110),
                              (636, 118), (630, 123), (622, 125.5), (452, 125.5), (446, 124), (442, 121)]),
                  ancho=mm(42), material=polimero, nombre="guardamanos")
    # Visto desde la boca: los lados rectos, abajo media elipse y arriba se cierra sobre el tubo de montar.
    lado = ([(mm(19.2) * math.cos(math.radians(a)), -mm(12.8) + mm(9.0) * math.sin(math.radians(a)))
             for a in range(-90, 1, 15)]
            + [(mm(19.2), mm(11)), (mm(18.8), mm(15.5)), (mm(16), mm(19)), (mm(12.5), mm(21.2))])
    sec_gm = lado + [(-z, y) for z, y in reversed(lado)]
    k.intersecar(gm, k.perfil(sec_gm, plano="zy", desde=cal.x(439), hasta=cal.x(645)))
    k.biselar(gm, mm(1.2), 2, 30)
    # El panel rebajado de los lados (en la foto, el del picado) y la estria larga de en medio.
    panel = k.redondear(cal.puntos([(455, 74), (612, 74), (612, 118), (455, 118)]), mm(3), 3)
    estria = k.caja((cal.x(612), cal.y(103), mm(17.4)), (cal.x(456), cal.y(100), mm(21)))
    k.restar(gm, k.unir(k.perfil(panel, desde=mm(18.6), hasta=mm(22)), k.perfil(panel, desde=-mm(22), hasta=-mm(18.6))),
             k.unir(estria, k.espejo(estria)))
    k.cilindro((cal.x(628), cal.y(79), -mm(19.8)), (cal.x(628), cal.y(79), mm(19.8)), mm(2.2), 12, material=negro,
               nombre="pasador_guardamanos")

    # ---------------------------------------------------------------- grupo del disparador (polimero)
    caja_disp = k.perfil(cal.puntos([(98, 122), (346, 122), (346, 186), (318, 188.5), (226, 188.5), (140, 152),
                                     (98, 153)]), ancho=mm(30), material=polimero, nombre="caja_disparador")
    k.biselar(caja_disp, mm(1.2), 2, 30)
    # La placa en relieve alrededor del selector, a los dos lados (grupo ambidiestro, como el de la foto).
    placa = k.perfil(cal.puntos([(208, 150), (212, 138), (222, 128.5), (232, 126), (328, 126), (335, 133), (334, 150),
                                 (318, 172), (300, 180), (260, 184), (228, 182), (214, 174), (209, 163)]),
                     desde=mm(14.6), hasta=mm(15.8), material=polimero, bisel=mm(0.4), nombre="placa_selector")
    k.espejo(placa)
    cs = cal.p(235, 154)
    for s in (1, -1):
        k.cilindro((cs[0], cs[1], s * mm(15.4)), (cs[0], cs[1], s * mm(16.9)), mm(5.0), 18, material=negro,
                   bisel=mm(0.4), nombre="selector")
        k.perfil(k.redondear(_barra(cs, 130, mm(12), mm(3.6), mm(2)), [0, mm(1.6), mm(1.6), 0], 3),
                 desde=s * mm(16.5), hasta=s * mm(18.0), material=negro, bisel=mm(0.4), nombre="palanca_selector")
    for px, py, r in ((125, 138, 2.6), (335, 154, 3.2), (335, 176, 2.3)):
        k.cilindro((cal.x(px), cal.y(py), -mm(16.4)), (cal.x(px), cal.y(py), mm(16.4)), mm(r), 14, material=negro,
                   bisel=mm(0.4), nombre="pasador")
    # La paleta del retén del cargador, detras del brocal.
    k.perfil(cal.puntos([(334, 186), (339.5, 186), (338, 196), (334, 207), (330.5, 206.5), (332.5, 196)]),
             ancho=mm(10), material=negro, bisel=mm(0.4), nombre="reten_cargador")

    guardamonte = k.perfil(cal.puntos([(211, 186), (318, 186), (317.5, 192), (314, 205), (311, 220), (309, 232),
                                       (304, 238), (296, 241), (240, 241), (229, 238.5), (221, 233), (215, 226),
                                       (211, 216)]), ancho=mm(14), material=polimero, nombre="guardamonte")
    k.restar(guardamonte, k.perfil(cal.puntos([(224.5, 184), (310, 184), (306, 205), (303, 218), (301, 224.5),
                                               (296, 228), (238, 228), (230, 225), (226, 219), (224.5, 206)]),
                                   ancho=mm(30)))
    k.biselar(guardamonte, mm(1.6), 2, 30)
    k.perfil(cal.puntos([(247, 184), (266, 184), (263, 195), (259, 203), (258, 212), (261, 221), (268, 229),
                         (266, 232.5), (260, 230.5), (253, 224), (249, 215), (247, 205), (246, 195)]),
             ancho=mm(6), material=negro, bisel=mm(0.6), nombre="gatillo")

    # La empunadura (pistolete), mas ancha que la caja; su parte de arriba queda dentro de la caja.
    pistolete = [(137, 150), (143, 158), (150, 164), (156, 172), (159, 181), (160, 190), (158, 199), (153, 209),
                 (145, 220), (136, 231), (128, 241), (120, 252), (113, 263), (109, 272), (110, 279), (118, 285),
                 (140, 295), (160, 305), (168, 310), (173, 312), (177, 308), (180, 298), (185, 288), (189, 280),
                 (191, 270), (194, 263), (198, 258), (201, 250), (202, 240), (205, 232), (212, 226), (219, 220),
                 (223, 212), (224.5, 200), (224.5, 190), (215, 180), (190, 166), (165, 152)]
    k.perfil(cal.puntos(pistolete), ancho=mm(33), material=polimero, bisel=mm(4.5), segmentos=3,
             nombre="pistolete")

    # La retenida de la culata (la palanca triangular de debajo de la tapa).
    gancho = k.perfil(cal.puntos([(82, 122), (98, 122), (97, 130), (94, 142), (92, 152), (88, 159), (78, 159),
                                  (74, 155), (74, 148), (78, 134)]), ancho=mm(10), material=polimero,
                      nombre="retenida_culata")
    k.restar(gancho, k.perfil(cal.puntos([(85, 139), (89.5, 139), (88.5, 152), (80, 152), (79, 149)]), ancho=mm(14)))
    k.biselar(gancho, mm(0.8), 2, 30)

    # ---------------------------------------------------------------- brocal y cargador curvo de 30
    brocal = k.perfil(cal.puntos([(345, 122), (402, 122), (402, 167), (398, 170), (352, 190), (345, 190)]),
                      ancho=mm(31), material=negro, nombre="brocal")
    k.biselar(brocal, mm(1.0), 2, 30)
    rebaje = k.redondear(cal.puntos([(350, 131), (387, 131), (387, 157), (350, 157)]), mm(1.5), 2)
    k.restar(brocal, k.unir(k.perfil(rebaje, desde=mm(14.8), hasta=mm(16.5)),
                            k.perfil(rebaje, desde=-mm(16.5), hasta=-mm(14.8))))
    cargador = [(352, 128), (401, 128), (401, 165), (402, 185), (405, 205), (410, 225), (418, 245), (430, 270),
                (440, 290), (452, 315), (462, 340), (468, 352), (430, 377), (420, 362), (410, 345), (397, 322),
                (390, 305), (381, 280), (376, 268), (368, 245), (360, 215), (352, 185)]
    k.perfil(cal.puntos(cargador), ancho=mm(23), material=negro, bisel=mm(1.5), nombre="cargador")
    nervio_c = k.perfil(cal.puntos([(375, 192), (384, 230), (401, 270), (417, 310), (430, 338), (436, 338), (423, 310),
                                    (407, 270), (390, 230), (381, 192)]), desde=mm(11.2), hasta=mm(12.2),
                        material=negro, bisel=mm(0.3), nombre="nervio_cargador")
    k.espejo(nervio_c)
    k.perfil(cal.puntos([(429, 378.5), (469.5, 352.5), (465.5, 346.5), (425.5, 372)]), ancho=mm(25), material=negro,
             bisel=mm(1.0), nombre="tapa_cargador")

    # ---------------------------------------------------------------- culata retractil: dos varillas y la cantonera
    x0 = cal.x(62) + dx_culata
    sec_var = k.redondear([(mm(15.5), cal.y(117)), (mm(19.5), cal.y(117)), (mm(19.5), cal.y(99)),
                           (mm(15.5), cal.y(99))], mm(1.6), 3)
    varilla = k.perfil(sec_var, plano="zy", desde=x0, hasta=x0 + VARILLA, material=negro, nombre="varilla")
    k.espejo(varilla)
    # El yugo de chapa del frente de la cantonera, donde se sueldan las varillas (metida, abraza la tapa).
    yugo = k.perfil(cal.puntos([(60, 80), (70, 79), (77, 84), (79, 92), (79, 124), (66, 128), (60, 126)]),
                    ancho=mm(42), material=negro, bisel=mm(1.2), nombre="yugo_culata")
    k.mover(yugo, (dx_culata, 0.0, 0.0))
    trasera = [(25, 75), (22, 100), (21, 125), (20, 150), (19.5, 175), (17, 195), (12.5, 220)]
    contorno = trasera + [(14, 228), (20, 233.5), (26, 234.5), (33, 233), (38, 229), (41, 220), (47.5, 200),
                          (54, 175), (58, 155), (60, 140), (62, 128), (65, 110), (67, 95), (69, 85), (67, 80),
                          (58, 75.5), (45, 72), (35, 71.5), (28, 72.5)]
    cantonera = k.perfil(cal.puntos(contorno), ancho=mm(40), material=goma, bisel=mm(3), segmentos=3,
                         nombre="cantonera")
    # Las estrias de goma de la cara de atras.
    rayas = []
    for py in range(100, 222, 9):
        xr = cal.x(_interpolar(trasera, py))
        rayas.append(k.caja((xr - mm(2), cal.y(py) - mm(0.75), -mm(16)), (xr + mm(1.3), cal.y(py) + mm(0.75), mm(16))))
    k.restar(cantonera, k.unir(*rayas))
    k.mover(cantonera, (dx_culata, 0.0, 0.0))

    return {"agarre": cal.p(*AGARRE_PX), "boca": (cal.x(PX_BOCA), 0.0, 0.0),
            "guardamanos": (cal.x(GUARDAMANOS_PX[0]), cal.y(GUARDAMANOS_PX[1]), 0.0), "calibracion": cal,
            "notas": [f"culata {'sacada' if sacada else 'metida'}"]}


# ------------------------------------------------------------------ comparacion sin lamina (dentro de Blender)
def _render_derecho(ba, mn, mx, ruta, px_m):
    """Ortografica desde +Z (el lado derecho, como la foto): la boca sale a la derecha."""
    import bpy
    sc = bpy.context.scene
    margen = 0.03 * (mx.x - mn.x)
    W = int(math.ceil((mx.x - mn.x + 2 * margen) * px_m))
    H = int(math.ceil((mx.y - mn.y + 2 * margen) * px_m))
    W += W % 2
    H += H % 2
    sc.render.resolution_x, sc.render.resolution_y = W, H
    sc.render.resolution_percentage = 100
    c = (mn + mx) * 0.5
    ob = ba._camara("derecho")
    ob.data.type = "ORTHO"
    ob.data.ortho_scale = max(W, H) / px_m
    ob.data.clip_start, ob.data.clip_end = 0.01, 10.0
    ob.location = (c.x, c.y, mx.z + 1.0)
    ob.rotation_euler = (0.0, 0.0, 0.0)
    sc.render.filepath = ruta
    bpy.ops.render.render(write_still=True)
    bpy.data.objects.remove(ob)
    return {"ruta": ruta, "px_m": px_m, "centro": [c.x, c.y], "ancho": W, "alto": H}


def _render_detalle(ba, objetivo, azimut, elevacion, distancia, ruta, focal=50.0, ancho=1000, alto=700):
    """Perspectiva de cerca (azimut desde -Z hacia +X), para ponerla junto a la lamina de detalle de la G3."""
    import bpy
    from mathutils import Vector
    sc = bpy.context.scene
    sc.render.resolution_x, sc.render.resolution_y = ancho, alto
    ob = ba._camara("detalle")
    ob.data.lens = focal
    ob.data.sensor_width = 36.0
    az, el = math.radians(azimut), math.radians(elevacion)
    d = Vector((math.sin(az) * math.cos(el), math.sin(el), -math.cos(az) * math.cos(el)))
    ob.location = objetivo + d * distancia
    ob.rotation_euler = ba.mirar(ob.location, objetivo).to_euler()
    ob.data.clip_start, ob.data.clip_end = 0.005, 10.0
    sc.render.filepath = ruta
    bpy.ops.render.render(write_still=True)
    bpy.data.objects.remove(ob)
    return {"ruta": ruta}


def _blender_comparar():
    """Dentro de Blender: el modelo con la culata metida (como la foto), su lado derecho a PX_M y un detalle de la
    boca. Lo deja en --comparar (una carpeta de revision, nunca en el proyecto)."""
    import argparse
    from mathutils import Vector
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    p = argparse.ArgumentParser()
    p.add_argument("--comparar", required=True)
    p.add_argument("--px-m", type=float, default=PX_M_COMPARAR)
    a = p.parse_args(argv)
    sys.path.insert(0, KIT)
    import blender_arma as ba
    os.makedirs(a.comparar, exist_ok=True)
    # la comparacion va siempre con el origen en el agarre (_punto y las cotas lo suponen)
    ns = argparse.Namespace(modelo=os.path.abspath(__file__), salida=a.comparar, trabajo=a.comparar, origen="agarre",
                            sin_render=True, hoja=None, px_m=0.0)
    ba.modelar(ns)
    nombre = FICHA["nombre"]
    ruta_m = os.path.join(a.comparar, nombre + "_medidas.json")
    with open(ruta_m, encoding="utf-8") as f:
        m = json.load(f)
    mn, mx = Vector(m["min"]), Vector(m["max"])
    ba.preparar_render(True)
    m["render_derecho"] = _render_derecho(ba, mn, mx, os.path.join(a.comparar, nombre + "_derecho.png"), a.px_m)
    objetivo = Vector(m["boca"]) + Vector((-0.055, 0.015, 0.0))     # el bloque del punto de mira
    # desde delante a la derecha (+Z), como la lamina de detalle de la boca de la G3
    m["render_boca"] = _render_detalle(ba, objetivo, 118.0, 16.0, 0.26, os.path.join(a.comparar, nombre + "_boca.png"))
    with open(ruta_m, "w", encoding="utf-8") as f:
        json.dump(m, f, indent=1)
    print(f"[armas] comparacion: culata metida {m['max'][0] - m['min'][0]:.4f} m de largo")


# ------------------------------------------------------------------ comparacion sin lamina (fuera de Blender)
def _mascara_foto(ruta, umbral=26.0):
    """La silueta de la foto (fondo blanco): lo que se aparta del blanco, sin motas y con los brillos rellenos."""
    import numpy as np
    from PIL import Image
    from scipy import ndimage
    rgb = np.asarray(Image.open(ruta).convert("RGB"), np.float32)
    m = (255.0 - rgb).max(-1) > umbral
    m = ndimage.binary_closing(m, np.ones((3, 3), bool), iterations=2)
    lab, n = ndimage.label(m)
    if n:
        m = lab == 1 + int(np.argmax(ndimage.sum(m, lab, range(1, n + 1))))
    hueco = ~m
    lab, n = ndimage.label(hueco)
    borde = set(np.unique(np.concatenate([lab[0], lab[-1], lab[:, 0], lab[:, -1]])))
    for i in range(1, n + 1):
        if i not in borde and (lab == i).sum() < 0.0008 * m.size:
            m[lab == i] = True
    return m


def _px(medidas_render, x, y, izquierda=False):
    """De metros del modelo (ya con el origen en el agarre) a pixeles de un render ortografico del kit."""
    r = medidas_render
    s = -1.0 if izquierda else 1.0
    return (r["ancho"] / 2 + s * (x - r["centro"][0]) * r["px_m"], r["alto"] / 2 - (y - r["centro"][1]) * r["px_m"])


def _punto(px, py):
    """Un pixel de la foto en metros del modelo final (origen en el agarre)."""
    return ((px - PX_BOCA) * S_FOTO - (AGARRE_PX[0] - PX_BOCA) * S_FOTO, (PY_EJE - py) * S_FOTO - (PY_EJE - AGARRE_PX[1]) * S_FOTO)


def comparar(nombre, salida, trabajo, revision, foto, blender, ancho_panel=1000):
    import numpy as np
    from PIL import Image, ImageDraw
    import construir_arma as ca
    import laminas

    tmp = os.path.join(revision, f"_{nombre}_metida")
    env = dict(os.environ, GD_MP5_CULATA="metida")
    cmd = [blender, "-b", "--factory-startup", "--python", os.path.abspath(__file__), "--", "--comparar", tmp,
           "--px-m", str(PX_M_COMPARAR)]
    r = subprocess.run(cmd, capture_output=True, text=True, encoding="utf-8", errors="replace", env=env)
    with open(os.path.join(trabajo, "blender_comparar.log"), "w", encoding="utf-8") as f:
        f.write(r.stdout + "\n" + r.stderr)
    if r.returncode != 0 or "Traceback" in r.stdout + r.stderr:
        raise SystemExit(f"Blender ha fallado en la comparacion (ver {os.path.join(trabajo, 'blender_comparar.log')})")
    mm_ = ca.leer_json(os.path.join(tmp, nombre + "_medidas.json"))          # culata metida
    m = ca.leer_json(os.path.join(trabajo, nombre + "_medidas.json"))        # el entregado (culata sacada)
    FONDO = ca.FONDO
    fuente = laminas.fuente

    # --- fila 1: la foto y el modelo metido, los dos de lado derecho a PX_M_COMPARAR y alineados por la boca y el eje
    rd = mm_["render_derecho"]
    mod = Image.open(rd["ruta"]).convert("RGBA")
    mod_mask = np.asarray(mod)[..., 3] > 127
    ub, vb = _px(rd, mm_["boca"][0], mm_["boca"][1])
    hay_foto = bool(foto and os.path.isfile(foto))
    iou_real = iou_caja = None
    if hay_foto:
        f = PX_M_COMPARAR * S_FOTO
        im_f = Image.open(foto).convert("RGB")
        fm = _mascara_foto(foto)
        im_f = im_f.resize((round(im_f.width * f), round(im_f.height * f)), Image.LANCZOS)
        fm = np.asarray(Image.fromarray(fm).resize(im_f.size, Image.NEAREST))
        dx, dy = round(ub - PX_BOCA * f), round(vb - PY_EJE * f)
        x0, y0 = min(0, dx), min(0, dy)
        x1, y1 = max(mod.width, dx + im_f.width), max(mod.height, dy + im_f.height)
        Wc, Hc = x1 - x0, y1 - y0
        A = np.zeros((Hc, Wc), bool)
        B = np.zeros((Hc, Wc), bool)
        A[-y0:-y0 + mod.height, -x0:-x0 + mod.width] = mod_mask
        B[dy - y0:dy - y0 + im_f.height, dx - x0:dx - x0 + im_f.width] = fm
        iou_real = ca.iou(A, B)
        # ajustada a la caja: la silueta de la foto estirada a la caja del modelo
        ys, xs = np.nonzero(A)
        ya_, xa_ = np.nonzero(B)
        a_rec = A[ys.min():ys.max() + 1, xs.min():xs.max() + 1]
        b_rec = B[ya_.min():ya_.max() + 1, xa_.min():xa_.max() + 1]
        b_aj = np.asarray(Image.fromarray(b_rec).resize((a_rec.shape[1], a_rec.shape[0]), Image.NEAREST))
        iou_caja = ca.iou(a_rec, b_aj)
        p_foto = Image.new("RGB", (Wc, Hc), FONDO)
        p_foto.paste(im_f, (dx - x0, dy - y0))
        p_mod = Image.new("RGB", (Wc, Hc), FONDO)
        p_mod.paste(ca._sobre_fondo(mod), (-x0, -y0))
        sup = np.full((Hc, Wc, 3), FONDO, np.uint8)
        sup[B & ~A] = (215, 60, 50)
        sup[A & ~B] = (40, 170, 210)
        sup[A & B] = (90, 90, 90)
        p_sup = Image.fromarray(sup)
    else:
        p_foto = Image.new("RGB", mod.size, FONDO)
        ImageDraw.Draw(p_foto).text((30, 30), "sin foto de referencia (--foto)", fill=(30, 30, 30), font=fuente(24))
        p_mod = ca._sobre_fondo(mod)
        p_sup = Image.new("RGB", mod.size, FONDO)

    def encajar(im, w=ancho_panel, h=None):
        h = h or int(im.height * w / im.width)
        e = min(w / im.width, h / im.height)
        im2 = im.resize((max(1, round(im.width * e)), max(1, round(im.height * e))), Image.LANCZOS)
        p = Image.new("RGB", (w, h), FONDO)
        ca._pegar_centrado(p, im2)
        return p, e

    p_foto, e1 = encajar(p_foto)
    p_mod, _ = encajar(p_mod)
    p_sup, _ = encajar(p_sup)

    # --- fila 3: el entregado (culata sacada), lado izquierdo, con las cotas reales
    rl = m["render_lado"]
    lado = Image.open(rl["ruta"]).convert("RGBA")
    k_px = 0.80 * ancho_panel / (m["max"][0] - m["min"][0])
    e = k_px / rl["px_m"]
    lado_s = lado.resize((round(lado.width * e), round(lado.height * e)), Image.LANCZOS)
    alto_c = int(lado_s.height + 170)
    p_cot = Image.new("RGB", (ancho_panel, alto_c), FONDO)
    ox, oy = (ancho_panel - lado_s.width) // 2, 95
    p_cot.paste(ca._sobre_fondo(lado_s), (ox, oy))
    dd = ImageDraw.Draw(p_cot)

    def pp(x, y):
        u, v = _px(rl, x, y, izquierda=True)
        return ox + u * e, oy + v * e

    def cota(xa, ya, xb, yb, texto, abajo=False):
        dd.line((xa, ya, xb, yb), fill=(20, 20, 20), width=2)
        for x, y in ((xa, ya), (xb, yb)):
            if abs(yb - ya) < 1:
                dd.line((x, y - 7, x, y + 7), fill=(20, 20, 20), width=2)
            else:
                dd.line((x - 7, y, x + 7, y), fill=(20, 20, 20), width=2)
        fx = fuente(17)
        if abs(yb - ya) < 1:
            dd.text(((xa + xb) / 2 - 90, ya + (8 if abajo else -26)), texto, fill=(20, 20, 20), font=fx)
        else:
            dd.text((xa + 10, (ya + yb) / 2 - 10), texto, fill=(20, 20, 20), font=fx)

    mn, mx = m["min"], m["max"]
    xb_, yb_ = m["boca"][0], m["boca"][1]
    (u0, v0), (u1, _v1) = pp(mx[0], mx[1]), pp(mn[0], mn[1])
    cota(u0, oy - 50, u1, oy - 50, f"largo {1000 * (mx[0] - mn[0]):.0f} mm (real 660-700)")
    xp, yp = _punto(PX_POSTE, 27.5)
    xa, _ya = _punto(PX_ALZA, 32)
    cota(pp(xp, 0)[0], oy - 18, pp(xa, 0)[0], oy - 18, "linea de mira 340 mm (real 340)")
    yb_cota = pp(0, mn[1])[1] + 30
    cota(pp(xb_, 0)[0], yb_cota, pp(xb_ - 0.225, 0)[0], yb_cota, "canon 225 mm (real 225)", abajo=True)
    dd.line((pp(xb_ - 0.225, yb_)[0], pp(0, yb_)[1], pp(xb_ - 0.225, 0)[0], yb_cota), fill=(120, 120, 120), width=1)
    ux = pp(mx[0], 0)[0] - 30
    cota(ux, pp(0, mx[1])[1], ux, pp(0, mn[1])[1], f"alto {1000 * (mx[1] - mn[1]):.0f} mm\n(real ~260)")
    # Como lo cogeria Unity: la mano con la ficha (morado) y la de la regla del 28 % sin ficha (naranja); los dos, de
    # vuelta del espacio de Unity al del OBJ (la x cambiada de signo).
    un = unity(nombre, FICHA["clase"], m["agarre"], m["boca"], _obj_unity(salida, nombre))
    puntos = [((0.0, 0.0), (200, 40, 40)), ((m["guardamanos"][0], m["guardamanos"][1]), (40, 120, 220)),
              ((xb_, yb_), (40, 160, 60)), ((-un["empunadura"][0], un["empunadura"][1]), (150, 50, 190))]
    if "regla_28" in un:
        puntos.append(((-un["regla_28"]["empunadura"][0], un["regla_28"]["empunadura"][1]), (235, 120, 20)))
    for (x, y), color in puntos:
        u, v = pp(x, y)
        dd.ellipse((u - 6, v - 6, u + 6, v + 6), outline=color, width=3)
    dd.text((20, alto_c - 50), "rojo: agarre (origen)   azul: guardamanos   verde: boca", fill=(20, 20, 20),
            font=fuente(16))
    dd.text((20, alto_c - 28), "Unity, la mano: morado con la ficha, naranja con la regla del 28 % (sin ficha)",
            fill=(20, 20, 20), font=fuente(16))
    ca._barra(dd, ancho_panel - 160, alto_c - 52, k_px, 0.10)

    # --- tres cuartos y detalle de la boca (con la lamina de detalle de la G3, de la misma familia)
    t34 = ca._recorte_alfa(Image.open(m["render_tres_cuartos"]["ruta"]).convert("RGBA"))
    p_34, _ = encajar(ca._sobre_fondo(t34), ancho_panel, alto_c)
    boca = Image.open(mm_["render_boca"]["ruta"]).convert("RGBA")
    p_boca, _ = encajar(ca._sobre_fondo(boca), ancho_panel, 700)
    try:
        g3 = Image.open(laminas.ruta_lamina("024-hk-g3", "detalle_boca")).convert("RGB")
        p_g3, _ = encajar(g3, ancho_panel, 700)
    except SystemExit:
        p_g3 = Image.new("RGB", (ancho_panel, 700), FONDO)

    # --- texto
    p_txt = Image.new("RGB", p_sup.size, FONDO)
    d = ImageDraw.Draw(p_txt)
    largo_s, largo_m = mx[0] - mn[0], mm_["max"][0] - mm_["min"][0]
    lineas = [
        f"{nombre}  (Subfusil, HK MP5A3; sin lamina en AssetsCreator)",
        f"culata sacada (la entregada): largo {largo_s * 1000:.0f} mm   real 660 / 680 / 700",
        f"culata metida: largo {largo_m * 1000:.0f} mm   real 490 / 550",
        f"alto {1000 * (mx[1] - mn[1]):.0f} mm (real ~260 con el cargador de 30)   ancho {1000 * (mx[2] - mn[2]):.1f} mm (real 50)",
        "linea de mira 340 mm y canon 225 mm (las medidas de HK: dan la escala)",
        f"triangulos {m['triangulos']}   vertices {m['vertices']}",
        *[f"    {k_}: {v}" for k_, v in sorted(m.get("triangulos_material", {}).items())],
        f"boca {[round(v * 1000, 1) for v in m['boca']]} mm desde el agarre",
        f"guardamanos {[round(v * 1000, 1) for v in m['guardamanos']]} mm",
    ]
    if hay_foto:
        lineas.append(f"IoU con la foto: {iou_real:.3f} a escala real (por la boca y el eje)   {iou_caja:.3f} ajustada a la caja")
    ec = un["en_el_cerebro"]
    lineas += [f"Unity (en el cerebro): Arma.PrefijosSerie {ec['Arma.PrefijosSerie']}, "
               f"LeerFicha {ec['ConstructorHumanos.LeerFicha']}", f"    {un['prefijo']}"]
    if "regla_28" in un:
        lineas.append(f"    sin ficha, la regla del 28 % coge a {un['regla_28']['desde_el_pistolete_mm'][:2]} mm "
                      "del pistolete (x hacia la culata, y)")
    for i, t in enumerate(lineas):
        d.text((24, 16 + i * 27), t, fill=(20, 20, 20), font=fuente(19))

    # --- montaje
    sep, cab = 12, 34
    filas = [(p_foto, p_mod, "Foto real MP5A3 (Small Arms Survey), lado derecho, culata metida; escala: linea de mira 340 mm",
              "Modelo con la culata metida, lado derecho (ortografica, misma escala, alineado por la boca)"),
             (p_sup, p_txt, "Siluetas: gris las dos, rojo solo foto, azul solo modelo", "Medidas"),
             (p_cot, p_34, "Modelo entregado (culata sacada), lado izquierdo, con las cotas reales",
              "Modelo, tres cuartos (lado izquierdo: la palanca de montar)"),
             (p_g3, p_boca, "Lamina 024-hk-g3 detalle_boca (misma familia: tunel, tubo de montar)",
              "Modelo, detalle de la boca")]
    W = 2 * ancho_panel + 3 * sep
    Ht = sum(max(a.height, b.height) + cab + sep for a, b, _c, _d in filas) + sep
    hoja = Image.new("RGB", (W, Ht), (235, 235, 235))
    dh = ImageDraw.Draw(hoja)
    y = sep
    for a, b, ta, tb in filas:
        dh.text((sep + 6, y + 6), ta, fill=(20, 20, 20), font=fuente(18))
        dh.text((2 * sep + ancho_panel + 6, y + 6), tb, fill=(20, 20, 20), font=fuente(18))
        hoja.paste(a, (sep, y + cab))
        hoja.paste(b, (2 * sep + ancho_panel, y + cab))
        y += max(a.height, b.height) + cab + sep
    if hay_foto:
        ca._barra(dh, sep + 20, sep + cab + p_foto.height - 40, PX_M_COMPARAR * e1, 0.05)
    os.makedirs(revision, exist_ok=True)
    ruta = os.path.join(revision, nombre + "_comparar.png")
    hoja.save(ruta)
    return ruta, iou_real, iou_caja, largo_m


# ------------------------------------------------------------------ como lo coge Unity (ConstructorHumanos.PonerArma)
def _obj_unity(salida, nombre):
    """El OBJ que copiara TraerArma (el _pbr); si es de un modelo anterior, el base (mismas posiciones)."""
    pbr, base = os.path.join(salida, nombre + "_pbr.obj"), os.path.join(salida, nombre + ".obj")
    if os.path.isfile(pbr) and (not os.path.isfile(base) or os.path.getmtime(pbr) >= os.path.getmtime(base)):
        return pbr
    return base


def _vertices_unity(ruta_obj):
    """Los vertices como los deja el importador de OBJ de Unity: uno por esquina distinta (v/vt/vn), la x cambiada de signo."""
    import numpy as np
    v, esquinas = [], {}
    with open(ruta_obj, encoding="utf-8", errors="replace") as f:
        for linea in f:
            if linea.startswith("v "):
                v.append([float(t) for t in linea.split()[1:4]])
            elif linea.startswith("f "):
                for t in linea.split()[1:]:
                    esquinas[t] = int(t.split("/")[0]) - 1
    p = np.array(v)[list(esquinas.values())]
    p[:, 0] *= -1.0
    return p


def regla_fusil(p):
    """ConstructorHumanos.EjesFusil (la regla del 28 %, la de un arma sin ficha ni entrada en Agarres), sobre los vertices de
    Unity: el canon es el eje mas largo, la boca el extremo con mas vertices, y la empunadura, a 28 % del largo desde la
    culata, al 55 % entre la linea del canon y lo mas bajo de una franja de +-5 % del largo."""
    import numpy as np
    lo_b, hi_b = p.min(0), p.max(0)
    s = hi_b - lo_b
    e = 0 if s[0] >= s[1] and s[0] >= s[2] else (1 if s[1] >= s[2] else 2)
    o1, o2 = (e + 1) % 3, (e + 2) % 3
    v = o1 if s[o1] >= s[o2] else o2
    w = o2 if v == o1 else o1
    lo, hi = float(lo_b[e]), float(hi_b[e])
    largo = hi - lo
    signo = 1.0 if (p[:, e] > hi - largo * 0.15).sum() >= (p[:, e] < lo + largo * 0.15).sum() else -1.0
    extremo = hi if signo > 0 else lo
    cerca = np.abs(p[:, e] - extremo) < largo * 0.15
    linea = float(p[cerca, v].mean()) if cerca.any() else 0.0
    arriba = 1.0 if (hi_b[v] - linea) < (linea - lo_b[v]) else -1.0
    xg = lo + largo * 0.28 if signo > 0 else hi - largo * 0.28
    franja = [float(q) for q in p[np.abs(p[:, e] - xg) < largo * 0.05, v]]
    bajo = min([linea] + franja) if arriba > 0 else max([linea] + franja)
    zc = float(lo_b[w] + hi_b[w]) * 0.5
    emp, boca, canon, sube = [0.0] * 3, [0.0] * 3, [0.0] * 3, [0.0] * 3
    emp[e], emp[v], emp[w] = xg, linea + (bajo - linea) * 0.55, zc
    boca[e], boca[v], boca[w] = extremo, linea, zc
    canon[e], sube[v] = signo, arriba
    return {"canon": canon, "arriba": sube, "empunadura": emp, "boca": boca}


def unity(nombre, clase, agarre, boca, ruta_obj):
    """Lo que necesita el paquete para coger bien el arma (en el espacio de Unity: la x del OBJ con el signo cambiado),
    si el cerebro ya lo tiene y lo que haria la regla del 28 % sin ello. `agarre` y `boca`, en metros del OBJ."""
    r4 = lambda c: [round(float(x), 4) + 0.0 for x in c]  # noqa: E731
    emp = r4((-agarre[0] + DESFASE_MANO[0], agarre[1] + DESFASE_MANO[1], agarre[2]))
    bu = r4((-boca[0], boca[1], boca[2]))
    u = {"nota": "De consulta: ConstructorHumanos.LeerFicha lee este fichero y pone la mano en el agarre (el centro "
                 "del pistolete, mas ConstructorHumanos.DesfaseAgarre(clase), vacio de serie) y la boca en la boca, con la "
                 "x cambiada de signo (empunadura y boca, aqui ya en el espacio de Unity). La clase sale de la ficha o del "
                 "prefijo en Arma.PrefijosSerie. Sin la ficha, Unity lo cogeria con la regla del 28 % (regla_28). El "
                 "humano, con Def.familia = \"subfusil\".",
         "prefijo": f'("{nombre}", ClaseArma.{clase})',
         "empunadura": emp, "boca": bu, "canon": [-1.0, 0.0, 0.0], "arriba": [0.0, 1.0, 0.0]}
    cerebro = {}
    for clave, partes, busca in (("Arma.PrefijosSerie", ("Runtime", "Enemigos", "Arma.cs"), f'"{nombre}"'),
                                 ("ConstructorHumanos.LeerFicha", ("Editor", "Enemigos", "ConstructorHumanos.cs"),
                                  "FichaArma LeerFicha(")):
        try:
            with open(os.path.join(PAQUETE, *partes), encoding="utf-8") as f:
                cerebro[clave] = "esta" if busca in f.read() else "falta"
        except OSError:
            cerebro[clave] = "no encuentro el fichero"
    u["en_el_cerebro"] = cerebro
    if os.path.isfile(ruta_obj):
        r = regla_fusil(_vertices_unity(ruta_obj))
        mano = (-agarre[0], agarre[1], agarre[2])
        u["regla_28"] = {"canon": r["canon"], "arriba": r["arriba"], "empunadura": r4(r["empunadura"]),
                         "boca": r4(r["boca"]),
                         "desde_el_pistolete_mm": [round(1000 * (a - b), 1) + 0.0 for a, b in zip(r["empunadura"], mano)]}
    return u


def _avisar_unity(u):
    for clave, estado in u["en_el_cerebro"].items():
        if estado != "esta":
            que = u["prefijo"] if clave == "Arma.PrefijosSerie" else "la lectura del _agarre.json"
            print(f"   AVISO: {clave} del cerebro: {estado}. Hace falta {que}")
    if "regla_28" in u:
        print(f"   sin ficha, la regla del 28 % lo coge a {u['regla_28']['desde_el_pistolete_mm']} mm del pistolete "
              f"(en Unity, +x hacia la culata)")


def _retocar_agarre(ac, salida, nombre, res, origen="agarre"):
    """El _agarre.json del kit apunta a construir_arma.py; este modelo se lanza con su propio fichero. Le anade lo que
    hace falta en Unity (el bloque "unity")."""
    ruta = os.path.join(salida, nombre + "_agarre.json")
    with open(ruta, encoding="utf-8") as f:
        d = json.load(f)
    py = os.path.join(ac, ".venv", "Scripts", "python.exe")
    d["receta"] = (f'"{py}" "{os.path.abspath(__file__)}" --res {res or FICHA["texturas"]["res"]}'
                   + (f" --origen {origen}" if origen != "agarre" else ""))
    d["referencia"] = ("sin lamina en AssetsCreator: medidas reales de HK (linea de mira 340 mm, canon 225 mm) y la "
                       "foto del MP5A3 de la hoja de identificacion de la Small Arms Survey")
    d["culata"] = _culata()
    d["unity"] = unity(nombre, d["clase"], d["agarre"], d["boca"], _obj_unity(salida, nombre))
    texto = json.dumps(d, indent=1, ensure_ascii=False)
    texto = re.sub(r"\[\s+([-\d.e]+),\s+([-\d.e]+),\s+([-\d.e]+)\s+\]", r"[\1, \2, \3]", texto)
    with open(ruta, "w", encoding="utf-8") as f:
        f.write(texto + "\n")
    return d


def main():
    import argparse
    import shutil
    p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    p.add_argument("--solo", default="modelo,texturas,agarre,comparar")
    p.add_argument("--res", type=int, default=0)
    p.add_argument("--revision", default=None, help="carpeta del PNG de comparacion (por defecto trabajo/)")
    p.add_argument("--foto", default=None, help="la foto de referencia (por defecto trabajo/gd_mp5_referencia_lateral.png)")
    p.add_argument("--origen", choices=("agarre", "linea"), default="agarre",
                   help="agarre: el origen en el punto de la mano; linea: x del agarre pero y = linea de tiro")
    p.add_argument("--blender", default=None)
    a = p.parse_args()
    sys.path.insert(0, KIT)
    import construir_arma as ca
    nombre = FICHA["nombre"]
    ac, salida, trabajo, revision = ca.carpetas(nombre, a.revision)
    foto = a.foto or os.path.join(trabajo, nombre + "_referencia_lateral.png")
    if a.foto and os.path.isfile(a.foto):
        destino = os.path.join(trabajo, nombre + "_referencia_lateral.png")
        if os.path.abspath(a.foto) != os.path.abspath(destino):
            shutil.copyfile(a.foto, destino)      # para que la proxima comparacion la encuentre sola
    pasos = set(a.solo.split(","))
    blender = ca.blender_exe(a.blender)
    print(f"== {nombre} ({FICHA['clase']}, sin lamina) -> {salida}")
    if "modelo" in pasos:
        print("-- modelo (Blender)")
        ca.paso_modelo(os.path.abspath(__file__), salida, trabajo, a.origen, blender)
    if "texturas" in pasos:
        print("-- texturas (AssetsCreator ultra/texturizar_obj.py)")
        ca.paso_texturas(ac, salida, nombre, FICHA, a.res)
    if "agarre" in pasos or "texturas" in pasos:
        ca.paso_agarre(ac, salida, trabajo, nombre, FICHA, a.res, a.origen)
        d = _retocar_agarre(ac, salida, nombre, a.res, a.origen)
        print(f"-- agarre: boca {d['boca']}, guardamanos {d['guardamanos']}, {d['triangulos']} triangulos, "
              f"recetas {d['materiales']}")
        _avisar_unity(d["unity"])
    if "comparar" in pasos and a.origen != "agarre":
        print("-- comparacion: solo con el origen en el agarre (lanzala sin --origen)")
    elif "comparar" in pasos:
        ruta, i_r, i_c, largo_m = comparar(nombre, salida, trabajo, revision, foto, blender)
        extra = f" (IoU con la foto {i_r:.3f} a escala real, {i_c:.3f} ajustada)" if i_r is not None else " (sin foto)"
        print(f"-- comparacion: {ruta}{extra}; culata metida {largo_m * 1000:.0f} mm")
    return 0


if __name__ == "__main__":
    try:
        import bpy  # noqa: F401
        _DENTRO = True
    except ImportError:
        _DENTRO = False
    if _DENTRO:
        _blender_comparar()
    else:
        sys.exit(main())
