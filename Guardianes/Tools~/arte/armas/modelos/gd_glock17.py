"""gd_glock17: Glock 17 (generacion 4), la pistola de los capataces y los mercenarios de Basiliskos (y de cualquier
juego de la saga que necesite una pistola moderna). Lamina 030-glock-17.

Medidas reales: corredera de 186 mm, canon de 114 mm, 138 mm de alto con el cargador y las miras, 25,5 mm de ancho
de corredera y unos 30 de empunadura. La lamina lateral (generada) esta estirada a lo largo: a la escala del largo
mide 118 mm de alto; por eso la calibracion lleva las dos escalas (x por la corredera, y por el alto).

Las coordenadas de la lamina se leen con
    python laminas.py rejilla 030-glock-17/lateral rejilla.png --paso 10 --recorte 500,240,1000,600 --zoom 2
"""
FICHA = {
    "nombre": "gd_glock17",
    "clase": "Pistola",
    "lamina": "030-glock-17",
    "para": "capataces y mercenarios de Basiliskos; cualquier pistola moderna de la saga",
    "medidas_reales": {"corredera": 0.186, "alto": 0.138, "canon": 0.114, "ancho_corredera": 0.0255,
                       "ancho_empunadura": 0.029},
    "texturas": {"res": 1024, "escala": 0.4},   # 0,4 m por tesela: la densidad de todas las gd_
    "vista_34": {"azimut": 28.0, "elevacion": 12.0, "focal": 100.0},
    "semilla": 17,
}


def modelar(k):
    mm = k.mm
    # Pixeles de glock-17_lateral.png -> metros. x: cara de la corredera (83) a su trasera (1400) = 186 mm.
    # y: de lo alto de las miras (87) al pie del cargador (925) = 138 mm; el cero, en el eje del canon (192).
    cal = k.Calibracion(px_boca=83, px_cola=1400, largo=0.186, py_arriba=87, py_abajo=925, alto=0.138, py_eje=192)

    # El acabado negro mate de la corredera (Tenifer) va con la receta de pintura mate: 'pavonado' y 'acero' van a
    # la de metal, que no baja de un gris acero claro (F0 alto) aunque el Kd sea negro. El acero desnudo, solo en la
    # corona y el bloque del canon, la guia y los pasadores.
    pavonado = k.material("pintura_corredera", "#2c2d30", rugosidad=0.7, metal=0.0)
    polimero = k.material("plastico_armazon", "#222326", rugosidad=0.55, metal=0.0)
    acero = k.material("acero_canon", "#303133", rugosidad=0.35, metal=1.0)

    # ---------------------------------------------------------------- corredera
    # El perfil lateral (esquinas redondeadas) por la seccion (cantos de arriba achaflanados a 45 grados).
    lado = k.redondear(cal.puntos([(83, 120), (1400, 120), (1400, 270), (83, 270)]),
                       [mm(1.7), mm(2.2), mm(0.6), mm(0.6)], 4)
    corredera = k.perfil(lado, ancho=mm(27), material=pavonado, nombre="corredera")
    ya, yc, yb = cal.y(120), cal.y(150), cal.y(270)        # arriba, arranque del chaflan, abajo
    seccion = [(-mm(12.75), yb), (mm(12.75), yb), (mm(12.75), yc), (mm(8.2), ya), (-mm(8.2), ya), (-mm(12.75), yc)]
    k.intersecar(corredera, k.perfil(seccion, plano="zy", desde=cal.x(1410), hasta=cal.x(70), material=pavonado))
    k.biselar(corredera, mm(0.9), 2, 30)

    # Estrias de atras: 7 ranuras de 2,8 mm cada 4,75 mm a los dos lados.
    x0, paso = cal.x(1115), cal.dx(33.67)
    ranura = k.caja((x0 - cal.dx(20), cal.y(280), mm(11.85)), (x0, cal.y(143), mm(14)))
    ranuras = k.repetir(ranura, 7, (-paso, 0, 0))
    k.restar(corredera, k.unir(ranuras, k.espejo(ranuras)))
    # Ventana de expulsion (lado derecho, +Z, y por arriba hasta pasado el centro) y el agujero del canon delante.
    k.restar(corredera, k.caja((cal.x(865), -mm(0.5), -mm(6)), (cal.x(668), mm(14), mm(14))))
    k.restar(corredera, k.cilindro((mm(1), 0, 0), (-mm(2.5), 0, 0), mm(7.6), 24))
    # Extractor, detras de la ventana.
    k.caja((cal.x(902), mm(2.2), mm(12.4)), (cal.x(866), mm(5.8), mm(13.25)), material=pavonado, bisel=mm(0.3))

    # ---------------------------------------------------------------- canon (la boca y el bloque de la recamara)
    xb = cal.x(68)                                         # la corona sale 2 mm por delante de la corredera
    k.torno([(-mm(12), 0), (-mm(12), mm(7.2)), (xb - mm(0.7), mm(7.2)), (xb, mm(6.5)), (xb, mm(5.0)),
             (xb - mm(0.6), mm(4.6)), (xb - mm(10), mm(4.6)), (xb - mm(10), 0)],
            eje="x", segmentos=24, material=acero, nombre="canon")
    k.caja((cal.x(858), -mm(4), -mm(6.8)), (cal.x(675), cal.y(124), mm(6.8)), material=acero, bisel=mm(0.5),
           nombre="recamara")

    # ---------------------------------------------------------------- miras
    k.perfil(cal.puntos([(112, 123), (132, 90), (156, 90), (171, 123)]), ancho=mm(3.4), material=polimero,
             bisel=mm(0.35), nombre="punto_mira")
    alza = k.perfil(cal.puntos([(1271, 123), (1282, 99), (1290, 88), (1322, 86), (1326, 90), (1337, 123)]),
                    ancho=mm(12), material=polimero, bisel=mm(0.4), nombre="alza")
    k.restar(alza, k.caja((cal.x(1345), cal.y(86) - mm(2.6), -mm(1.75)), (cal.x(1265), cal.y(80), mm(1.75))))

    # ---------------------------------------------------------------- armazon (polimero)
    # El contorno de la lamina, recortado por zonas de distinto grosor.
    hueco = [(828, 551), (850, 546), (868, 536), (884, 522), (892, 507), (898, 489), (900, 472), (896, 456),
             (890, 437), (878, 415), (860, 398), (830, 388), (784, 384), (685, 382), (658, 382), (630, 394),
             (612, 415), (604, 439), (604, 491), (608, 503), (614, 517), (635, 538), (661, 546)]
    # Donde se ensancha la empunadura: la curva de arriba del abultamiento de la palma (la lamina la ensena como un
    # brillo de 942,470 a 1290,432). La parte de arriba del armazon baja 15 px mas para que no quede rendija.
    hombro = [(942, 470), (990, 442), (1060, 426), (1150, 419), (1240, 423), (1290, 432), (1430, 432)]
    superior = k.silueta(cal, region=[(84, 262), (1430, 262)] + [(x, y + 15) for x, y in reversed(hombro)]
                         + [(900, 485), (900, 392), (84, 392)], ancho=mm(25.0), material=polimero, nombre="armazon")
    k.restar(superior, k.perfil(cal.puntos(hueco), ancho=mm(40)))
    # Ranura del rail a cada lado y el hueco de la guia del muelle en la cara de delante.
    k.restar(superior, k.caja((cal.x(440), cal.y(352), mm(11.3)), (cal.x(97), cal.y(338), mm(13.5))),
             k.caja((cal.x(440), cal.y(352), -mm(13.5)), (cal.x(97), cal.y(338), -mm(11.3))),
             k.cilindro((cal.x(78), cal.y(303), 0), (cal.x(98), cal.y(303), 0), mm(5.4), 20))
    k.biselar(superior, mm(1.3), 2, 30)

    guardamonte = k.silueta(cal, region=[(500, 370), (940, 370), (940, 610), (500, 610)], ancho=mm(14.5),
                            material=polimero, nombre="guardamonte")
    k.restar(guardamonte, k.perfil(cal.puntos(hueco), ancho=mm(40)))
    k.biselar(guardamonte, mm(1.6), 2, 30)

    # La empunadura empieza en la pared de atras del guardamonte (es su cara delantera, con todo su ancho).
    empunadura = k.silueta(cal, region=[(900, 590), (866, 538), (884, 522), (892, 507), (898, 489), (900, 472),
                                        (905, 466)] + hombro + [(1480, 432), (1480, 897), (925, 897)],
                           ancho=mm(29), material=polimero, nombre="empunadura_armazon")
    k.restar(empunadura, k.perfil(cal.puntos(hueco), ancho=mm(40)))
    k.biselar(empunadura, mm(4.5), 3, 30)
    # El panel del picado (generacion 4), medio milimetro hundido a cada lado: su borde lee de cerca.
    panel = k.redondear(cal.puntos([(1032, 512), (1110, 500), (1250, 503), (1263, 530), (1322, 830), (1310, 861),
                                    (1100, 862), (1088, 848)]), mm(2.0), 3)
    k.restar(empunadura, k.perfil(panel, desde=mm(14.0), hasta=mm(16)), k.perfil(panel, desde=-mm(16), hasta=-mm(14.0)))

    k.silueta(cal, region=[(1060, 893), (1480, 893), (1480, 935), (1060, 935)], ancho=mm(27), material=polimero,
              bisel=mm(1.5), nombre="base_cargador")

    # ---------------------------------------------------------------- mandos y pasadores
    # El gatillo: lo que el hueco de la silueta deja dentro del guardamonte.
    k.perfil(cal.puntos([(784, 384), (788, 417), (786, 454), (762, 486), (741, 516), (743, 525), (751, 530),
                         (753, 538), (760, 543), (768, 542), (795, 526), (826, 494), (836, 479), (841, 466),
                         (876, 454), (881, 448), (884, 436), (890, 384)]),
             ancho=mm(6.5), material=polimero, bisel=mm(0.6), nombre="gatillo")
    k.perfil(cal.puntos([(690, 298), (708, 298), (724, 354), (706, 354)]), ancho=mm(26.6), material=pavonado,
             bisel=mm(0.35), nombre="palanca_desmontaje")
    k.perfil(cal.puntos([(983, 282), (1058, 282), (1058, 300), (983, 300)]), desde=-mm(13.6), hasta=-mm(11.0),
             material=pavonado, bisel=mm(0.4), nombre="retenida_corredera")
    k.perfil(cal.puntos([(958, 487), (1012, 487), (1012, 546), (958, 546)]), desde=-mm(15.7), hasta=-mm(13.0),
             material=polimero, bisel=mm(0.45), nombre="retenida_cargador")
    for (px, py, r, z) in ((830, 300, 1.3, 12.85), (815, 345, 2.1, 12.85), (1258, 458, 1.5, 14.85)):
        k.cilindro((cal.x(px), cal.y(py), -mm(z)), (cal.x(px), cal.y(py), mm(z)), mm(r), 12, material=acero,
                   nombre="pasador")
    # Guia del muelle recuperador, asomando por delante del armazon.
    k.cilindro((cal.x(96), cal.y(303), 0), (cal.x(76), cal.y(303), 0), mm(4.4), 20, material=acero,
               bisel=mm(0.4), nombre="guia_muelle")

    # El agarre: el centro de la empunadura a la altura del dedo corazon (donde cierra el puno).
    return {"agarre": cal.p(1184, 641), "boca": (xb, 0.0, 0.0), "guardamanos": None, "calibracion": cal}
