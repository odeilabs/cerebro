"""Kit de modelado de armas de fuego realistas de la saga (Blender 5.1: bpy + bmesh).

Lo importa blender_arma.py dentro de Blender y se lo pasa a cada modelo (modelos/<n>.py) como `k`:

    def modelar(k):
        cal = k.Calibracion(px_boca=83, px_cola=1400, largo=0.186, py_arriba=87, py_abajo=925, alto=0.138, py_eje=192)
        metal = k.material("pavonado_corredera", "#1c1d1f", rugosidad=0.45, metal=1.0)
        corredera = k.perfil(cal.puntos([(83, 120), (1400, 120), (1400, 270), (83, 270)]), ancho=k.mm(25.5), material=metal)
        k.biselar(corredera, k.mm(0.8))
        k.restar(corredera, k.cilindro((0.01, 0, 0), (-0.003, 0, 0), k.mm(7.5)))
        return {"agarre": cal.p(1183, 640), "boca": (k.mm(2), 0, 0)}

TODO EN METROS Y EN LOS EJES DEL OBJ DE LA SAGA (los de kk_ak47): el canon hacia +X, arriba +Y, el grosor en Z
(+Z es el lado derecho del tirador, -Z el izquierdo, el que enseñan las laminas laterales). Dentro de Blender no se
usa su Z arriba: la geometria se escribe tal cual en el OBJ (exportar_obj), y las camaras de los renders miran con
+Y arriba. Cada funcion que crea una pieza devuelve un objeto de Blender con la malla ya en su sitio (sin
transformaciones de objeto) y con un solo material; las operaciones (restar, biselar...) se aplican en el acto.

Piezas:   perfil, torno, cilindro, caja, esfera, barrido, silueta
2D:       redondear, arco, bezier, Calibracion (pixeles de la lamina -> metros), mm
Booleanas: restar, intersecar, unir (sin booleana: junta mallas)
Retoques: biselar, deformar, mover, girar, escalar, copiar, espejo, repetir, quitar
Final:    terminar (une todo, pone el origen en el agarre, normales y UV), exportar_obj
"""
import json
import math
import os
import random

import bmesh
import bpy
from mathutils import Matrix, Vector

# ------------------------------------------------------------------ estado
_piezas = []            # objetos vivos del modelo, en orden de creacion
_materiales = {}        # nombre -> {"kd": (r,g,b) sRGB, "rugosidad", "metal", "veta"}
_laminas = {}           # vista -> datos de laminas.datos_lamina (pixeles)
SOLVER = "EXACT"        # el de las booleanas: EXACT aguanta caras coplanarias; MANIFOLD es mas rapido
azar = random.Random(0)  # el azar del modelo (desgaste, variaciones): siempre con la semilla de FICHA


def mm(v):
    """Milimetros a metros."""
    return v * 0.001


def nuevo(semilla=0):
    """Vacia la escena y siembra `azar` (lo llama blender_arma.py antes de cada modelo con FICHA["semilla"])."""
    bpy.ops.wm.read_factory_settings(use_empty=True)
    _piezas.clear()
    _materiales.clear()
    azar.seed(semilla)


def cargar_laminas(carpeta, nombre):
    """Lee los <nombre>_lamina_<vista>.json que deja construir_arma.py (contornos en pixeles)."""
    _laminas.clear()
    for vista in ("lateral", "tres_cuartos", "detalle_empunadura", "detalle_boca"):
        ruta = os.path.join(carpeta, f"{nombre}_lamina_{vista}.json")
        if os.path.isfile(ruta):
            with open(ruta, encoding="utf-8") as f:
                _laminas[vista] = json.load(f)


class Lamina:
    """Una vista de la lamina: caja ([x0, y0, x1, y1]), exterior ([[x, y], ...]) y huecos, en pixeles."""

    def __init__(self, datos):
        self.datos = datos
        self.caja = datos["caja"]
        self.exterior = [tuple(p) for p in datos["exterior"]]
        self.huecos = [[tuple(p) for p in h] for h in datos["huecos"]]
        self.ancho, self.alto = datos["ancho"], datos["alto"]


def lamina(vista="lateral"):
    if vista not in _laminas:
        raise RuntimeError(f"No hay contornos de la vista '{vista}': lanza el modelo con construir_arma.py")
    return Lamina(_laminas[vista])


# ------------------------------------------------------------------ calibracion
class Calibracion:
    """De pixeles de una lamina lateral (la boca a la izquierda) a metros del modelo.

    X: px_boca y px_cola son dos referencias a lo largo (p. ej. la cara de la corredera y su trasera) y `largo` los
    metros reales entre ellas. Y: py_arriba/py_abajo y `alto` hacen lo mismo en vertical; sin ellos, la misma escala
    que en X. Las laminas son imagenes generadas y pueden venir estiradas: con las dos escalas el modelo tiene las
    medidas reales y la forma de la lamina. El cero queda en x = px_boca y en y = py_eje (la linea de tiro).
    `boca_izquierda` = False si la lamina tiene la boca a la derecha."""

    def __init__(self, px_boca, px_cola, largo, py_arriba=None, py_abajo=None, alto=None, py_eje=0.0,
                 boca_izquierda=True):
        self.px_boca, self.py_eje = float(px_boca), float(py_eje)
        self.sx = largo / abs(px_cola - px_boca)
        self.sy = alto / abs(py_abajo - py_arriba) if alto else self.sx
        self.signo = 1.0 if boca_izquierda else -1.0

    def x(self, px):
        return (self.px_boca - px) * self.sx * self.signo

    def y(self, py):
        return (self.py_eje - py) * self.sy

    def p(self, px, py, z=None):
        """Un punto: (x, y) o (x, y, z) si se da z (en metros)."""
        return (self.x(px), self.y(py)) if z is None else (self.x(px), self.y(py), z)

    def puntos(self, lista):
        return [self.p(a, b) for a, b in lista]

    def dx(self, n_px):
        """Una longitud en pixeles a lo largo, en metros."""
        return abs(n_px) * self.sx

    def dy(self, n_px):
        return abs(n_px) * self.sy

    @property
    def deformacion(self):
        """sy / sx: 1 si la lamina no esta estirada."""
        return self.sy / self.sx


# ------------------------------------------------------------------ 2D
def _area2(p):
    return sum(p[i][0] * p[(i + 1) % len(p)][1] - p[(i + 1) % len(p)][0] * p[i][1] for i in range(len(p))) * 0.5


def _limpiar(p, tol=1e-7):
    """Quita puntos repetidos seguidos (y el ultimo si repite el primero)."""
    q = []
    for a in p:
        if not q or abs(a[0] - q[-1][0]) > tol or abs(a[1] - q[-1][1]) > tol:
            q.append((float(a[0]), float(a[1])))
    if len(q) > 1 and abs(q[0][0] - q[-1][0]) <= tol and abs(q[0][1] - q[-1][1]) <= tol:
        q.pop()
    return q


def arco(centro, radio, a0, a1, n=8):
    """Puntos de un arco (grados, de a0 a a1, ambos incluidos)."""
    return [(centro[0] + radio * math.cos(math.radians(a0 + (a1 - a0) * i / n)),
             centro[1] + radio * math.sin(math.radians(a0 + (a1 - a0) * i / n))) for i in range(n + 1)]


def bezier(p0, p1, p2, p3, n=8):
    """Bezier cubica de p0 a p3 (n+1 puntos)."""
    r = []
    for i in range(n + 1):
        t = i / n
        a, b, c, d = (1 - t) ** 3, 3 * t * (1 - t) ** 2, 3 * t * t * (1 - t), t ** 3
        r.append((a * p0[0] + b * p1[0] + c * p2[0] + d * p3[0], a * p0[1] + b * p1[1] + c * p2[1] + d * p3[1]))
    return r


def redondear(puntos, radios, segmentos=4):
    """Redondea las esquinas de un poligono cerrado con arcos. `radios`: un numero (todas), una lista (una por
    esquina) o un dict {indice: radio}. Un radio mayor de lo que cabe se recorta a la mitad del lado mas corto."""
    p = _limpiar(puntos)
    n = len(p)
    if isinstance(radios, dict):
        rs = [radios.get(i, 0.0) for i in range(n)]
    elif isinstance(radios, (list, tuple)):
        rs = list(radios) + [0.0] * (n - len(radios))
    else:
        rs = [float(radios)] * n
    salida = []
    for i in range(n):
        a, b, c = Vector(p[i - 1]), Vector(p[i]), Vector(p[(i + 1) % n])
        r = rs[i]
        u, v = (a - b), (c - b)
        if r <= 0 or u.length < 1e-9 or v.length < 1e-9:
            salida.append(tuple(b))
            continue
        ang = u.angle(v)
        if ang < 1e-3 or abs(ang - math.pi) < 1e-3:
            salida.append(tuple(b))
            continue
        t = r / math.tan(ang * 0.5)
        t = min(t, u.length * 0.5, v.length * 0.5)
        r = t * math.tan(ang * 0.5)
        u.normalize()
        v.normalize()
        p1, p2 = b + u * t, b + v * t
        bis = (u + v).normalized()
        cen = b + bis * (r / math.sin(ang * 0.5))
        a1 = math.atan2(p1.y - cen.y, p1.x - cen.x)
        a2 = math.atan2(p2.y - cen.y, p2.x - cen.x)
        d = (a2 - a1 + math.pi) % (2 * math.pi) - math.pi
        for k in range(segmentos + 1):
            ak = a1 + d * k / segmentos
            salida.append((cen.x + r * math.cos(ak), cen.y + r * math.sin(ak)))
    return _limpiar(salida)


# ------------------------------------------------------------------ materiales
def _hex(c):
    if isinstance(c, str):
        c = c.lstrip("#")
        return tuple(int(c[i:i + 2], 16) / 255.0 for i in (0, 2, 4))
    return tuple(float(v) for v in c[:3])


def _lineal(c):
    return tuple(v / 12.92 if v <= 0.04045 else ((v + 0.055) / 1.055) ** 2.4 for v in c)


def material(nombre, color="#202022", rugosidad=0.5, metal=0.0, veta=None):
    """Un material con nombre para las recetas del texturizador de AssetsCreator (ultra/recetas_nombre.py: gana la
    primera regla cuya palabra este en el nombre): pavonado/acero/metal -> metal; plastico/detalle -> plastico;
    goma -> goma; madera/culata/nogal -> madera; cuero/correa -> cuero. OJO: 'empunadura' va a cuero. El color
    (sRGB) es el Kd con el que se tine la receta. `veta`: la direccion de la fibra de una madera ((1, 0, 0) a lo
    largo del canon); va al anexo <n>.veta.json que lee el texturizador. Devuelve el nombre."""
    if nombre not in _materiales:
        kd = _hex(color)
        _materiales[nombre] = {"kd": kd, "rugosidad": float(rugosidad), "metal": float(metal),
                               "veta": list(veta) if veta else None}
        m = bpy.data.materials.new(nombre)
        # el color de los renders de revision (Workbench): el Kd aclarado y sin metal, para leer la forma
        lin = _lineal(tuple(min(1.0, c * 2.6 + 0.04) for c in kd))
        m.diffuse_color = (lin[0], lin[1], lin[2], 1.0)
        m.roughness = 0.5
        m.metallic = 0.0
    return nombre


def _poner_material(ob, nombre):
    if nombre is None:
        nombre = material("plastico_defecto", "#303032")
    me = ob.data
    me.materials.clear()
    me.materials.append(bpy.data.materials[nombre])
    for p in me.polygons:
        p.material_index = 0


# ------------------------------------------------------------------ piezas (bmesh -> objeto)
def _objeto(bm, nombre, material_):
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
    me = bpy.data.meshes.new(nombre or "pieza")
    bm.to_mesh(me)
    bm.free()
    ob = bpy.data.objects.new(nombre or "pieza", me)
    bpy.context.scene.collection.objects.link(ob)
    _poner_material(ob, material_)
    _piezas.append(ob)
    return ob


def _plano(plano, u, v, w):
    """(u, v) del dibujo y w (la profundidad) -> (x, y, z)."""
    if plano == "xy":      # lateral: el dibujo de la lamina; se extruye en Z
        return (u, v, w)
    if plano == "zy":      # seccion: mirando desde la boca (u = z, v = y); se extruye en X
        return (w, v, u)
    if plano == "xz":      # planta: desde arriba (u = x, v = z); se extruye en Y
        return (u, w, v)
    raise ValueError(plano)


def _extension(ancho, centro, desde, hasta):
    if desde is not None and hasta is not None:
        return float(min(desde, hasta)), float(max(desde, hasta))
    if ancho is None:
        raise ValueError("perfil: da 'ancho' (y 'centro') o 'desde' y 'hasta'")
    return centro - ancho * 0.5, centro + ancho * 0.5


def perfil(puntos, ancho=None, plano="xy", centro=0.0, desde=None, hasta=None, material=None, bisel=0.0,
           segmentos=2, angulo=30.0, nombre=None):
    """Extruye un poligono cerrado 2D. plano 'xy' (el perfil lateral, se extruye en Z: culatas, cajones, armazones,
    guardamontes), 'zy' (la seccion vista desde la boca, se extruye en X: correderas, guardamanos) o 'xz' (la
    planta, se extruye en Y). El grosor: `ancho` centrado en `centro` o de `desde` a `hasta`. Con `bisel` > 0
    bisela las aristas de mas de `angulo` grados."""
    p = _limpiar(puntos)
    if len(p) < 3:
        raise ValueError("perfil: hacen falta 3 puntos")
    w0, w1 = _extension(ancho, centro, desde, hasta)
    bm = bmesh.new()
    vs = [bm.verts.new(_plano(plano, a, b, w0)) for a, b in p]
    cara = bm.faces.new(vs)
    ext = bmesh.ops.extrude_face_region(bm, geom=[cara])
    nuevos = [g for g in ext["geom"] if isinstance(g, bmesh.types.BMVert)]
    d = Vector(_plano(plano, 0.0, 0.0, w1 - w0))
    bmesh.ops.translate(bm, verts=nuevos, vec=d)
    ob = _objeto(bm, nombre or "perfil", material)
    if bisel > 0:
        biselar(ob, bisel, segmentos, angulo)
    return ob


def _base_eje(eje):
    """Matriz que lleva el eje local X al eje pedido ('x', 'y', 'z' o un vector)."""
    if isinstance(eje, str):
        eje = {"x": (1, 0, 0), "y": (0, 1, 0), "z": (0, 0, 1)}[eje]
    a = Vector(eje).normalized()
    ref = Vector((0, 1, 0)) if abs(a.y) < 0.9 else Vector((0, 0, 1))
    b = ref.cross(a).normalized()          # "z" local
    c = a.cross(b).normalized()            # "y" local
    return Matrix(((a.x, c.x, b.x), (a.y, c.y, b.y), (a.z, c.z, b.z)))


def torno(perfil_ar, eje="x", origen=(0.0, 0.0, 0.0), segmentos=24, material=None, bisel=0.0, angulo=30.0,
          fase=0.0, nombre=None, cerrado=False):
    """Revoluciona un perfil [(a, r), ...] (a a lo largo del eje, r el radio) alrededor de un eje que pasa por
    `origen`. Los puntos con r = 0 cierran en un polo; si los extremos no estan en el eje se tapan con una cara.
    Con `cerrado`, el perfil es un lazo (un tubo con su anima). Canones, bocachas, cilindros de revolver, visores,
    cargadores tubulares, pomos. `fase` gira los segmentos (grados)."""
    p = [(float(a), max(0.0, float(r))) for a, r in perfil_ar]
    M = _base_eje(eje)
    o = Vector(origen)
    bm = bmesh.new()
    anillos = []
    for a, r in p:
        if r < 1e-9:
            anillos.append([bm.verts.new(o + M @ Vector((a, 0, 0)))])
            continue
        anillo = []
        for i in range(segmentos):
            t = math.radians(fase) + 2 * math.pi * i / segmentos
            anillo.append(bm.verts.new(o + M @ Vector((a, r * math.cos(t), r * math.sin(t)))))
        anillos.append(anillo)
    pares = list(zip(anillos[:-1], anillos[1:]))
    if cerrado:
        pares.append((anillos[-1], anillos[0]))
    for A, B in pares:
        if len(A) == 1 and len(B) == 1:
            continue
        for i in range(segmentos):
            j = (i + 1) % segmentos
            if len(A) == 1:
                bm.faces.new((A[0], B[j], B[i]))
            elif len(B) == 1:
                bm.faces.new((A[i], A[j], B[0]))
            else:
                bm.faces.new((A[i], A[j], B[j], B[i]))
    if not cerrado:
        for anillo in (anillos[0], anillos[-1]):
            if len(anillo) > 2:
                bm.faces.new(anillo)
    bmesh.ops.remove_doubles(bm, verts=bm.verts[:], dist=1e-7)
    ob = _objeto(bm, nombre or "torno", material)
    if bisel > 0:
        biselar(ob, bisel, 2, angulo)
    return ob


def cilindro(p0, p1, radio, segmentos=16, material=None, radio1=None, bisel=0.0, fase=0.0, nombre=None):
    """Cilindro (o tronco de cono con radio1) de p0 a p1, tapado. Pernos, pasadores, canones sencillos, y la
    herramienta de los agujeros."""
    p0, p1 = Vector(p0), Vector(p1)
    L = (p1 - p0).length
    r1 = radio if radio1 is None else radio1
    return torno([(0.0, 0.0), (0.0, radio), (L, r1), (L, 0.0)], eje=tuple(p1 - p0), origen=tuple(p0),
                 segmentos=segmentos, material=material, bisel=bisel, fase=fase, nombre=nombre or "cilindro")


def caja(minimo, maximo, material=None, bisel=0.0, segmentos=2, angulo=30.0, nombre=None):
    """Caja alineada con los ejes entre dos esquinas."""
    a, b = Vector(minimo), Vector(maximo)
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=1.0)
    for v in bm.verts:
        v.co = Vector(((a.x + b.x) * 0.5 + v.co.x * abs(b.x - a.x), (a.y + b.y) * 0.5 + v.co.y * abs(b.y - a.y),
                       (a.z + b.z) * 0.5 + v.co.z * abs(b.z - a.z)))
    ob = _objeto(bm, nombre or "caja", material)
    if bisel > 0:
        biselar(ob, bisel, segmentos, angulo)
    return ob


def esfera(centro, radio, segmentos=16, anillos=8, material=None, nombre=None):
    """Esfera (pomos de cerrojo, cabezas de remache)."""
    bm = bmesh.new()
    bmesh.ops.create_uvsphere(bm, u_segments=segmentos, v_segments=anillos, radius=radio)
    bmesh.ops.translate(bm, verts=bm.verts[:], vec=Vector(centro))
    return _objeto(bm, nombre or "esfera", material)


def barrido(puntos, radio, segmentos=12, material=None, radios=None, nombre=None):
    """Un tubo a lo largo de una polilinea 3D (marcos de transporte paralelo), tapado en los extremos. `radios`,
    uno por punto, para ahusarlo. Palancas de cerrojo, guardamontes de alambre, anillas, muelles."""
    P = [Vector(p) for p in puntos]
    rs = radios or [radio] * len(P)
    T = []
    for i in range(len(P)):
        a = P[min(i + 1, len(P) - 1)] - P[max(i - 1, 0)]
        T.append(a.normalized())
    ref = Vector((0, 1, 0)) if abs(T[0].y) < 0.9 else Vector((1, 0, 0))
    n = ref.cross(T[0]).normalized()
    bm = bmesh.new()
    anillos = []
    for i, (p, t) in enumerate(zip(P, T)):
        if i > 0:
            ax = T[i - 1].cross(t)
            if ax.length > 1e-9:
                n.rotate(Matrix.Rotation(T[i - 1].angle(t), 3, ax.normalized()))
        b = t.cross(n).normalized()
        anillos.append([bm.verts.new(p + rs[i] * (math.cos(2 * math.pi * k / segmentos) * n +
                                                   math.sin(2 * math.pi * k / segmentos) * b))
                        for k in range(segmentos)])
    for A, B in zip(anillos[:-1], anillos[1:]):
        for k in range(segmentos):
            j = (k + 1) % segmentos
            bm.faces.new((A[k], A[j], B[j], B[k]))
    bm.faces.new(anillos[0])
    bm.faces.new(anillos[-1])
    return _objeto(bm, nombre or "barrido", material)


def silueta(cal, vista="lateral", region=None, ancho=None, centro=0.0, desde=None, hasta=None, material=None,
            bisel=0.0, segmentos=2, angulo=30.0, nombre=None):
    """El contorno exterior de la lamina (en pixeles, de laminas.py) extruido en Z, recortado por `region` (un
    poligono en pixeles de la lamina): la forma exacta de un trozo del arma (armazon, empunadura, culata) sin
    dibujarla a mano. El bisel se da despues del recorte."""
    lam = lamina(vista)
    ob = perfil(cal.puntos(lam.exterior), ancho=ancho, centro=centro, desde=desde, hasta=hasta, material=material,
                nombre=nombre or "silueta")
    if region:
        w0, w1 = _extension(ancho, centro, desde, hasta)
        herramienta = perfil(cal.puntos(region), desde=w0 - 0.01, hasta=w1 + 0.01, material=material)
        intersecar(ob, herramienta)
    if bisel > 0:
        biselar(ob, bisel, segmentos, angulo)
    return ob


# ------------------------------------------------------------------ booleanas y retoques
def _aplicar(ob):
    """Aplica todos los modificadores del objeto en su malla (sin operadores)."""
    dg = bpy.context.evaluated_depsgraph_get()
    dg.update()
    ev = ob.evaluated_get(dg)
    me = bpy.data.meshes.new_from_object(ev, preserve_all_data_layers=True, depsgraph=dg)
    viejo = ob.data
    ob.modifiers.clear()
    ob.data = me
    if viejo.users == 0:
        bpy.data.meshes.remove(viejo)
    return ob


def quitar(*obs):
    """Borra piezas (las herramientas despues de una booleana)."""
    for ob in obs:
        if ob in _piezas:
            _piezas.remove(ob)
        me = ob.data
        bpy.data.objects.remove(ob)
        if me.users == 0:
            bpy.data.meshes.remove(me)


def _booleana(ob, otra, operacion):
    # las caras del corte, con el material de la pieza (no el de la herramienta)
    if ob.data.materials:
        otra.data.materials.clear()
        otra.data.materials.append(ob.data.materials[0])
    mod = ob.modifiers.new("booleana", "BOOLEAN")
    mod.operation = operacion
    mod.object = otra
    mod.solver = SOLVER
    mod.material_mode = "INDEX"
    otra.hide_render = True
    _aplicar(ob)
    quitar(otra)
    if not ob.data.polygons:
        print(f"[armas] AVISO: la booleana {operacion} ha dejado vacia la pieza {ob.name}")
    return ob


def restar(ob, *herramientas):
    """Le quita a `ob` cada herramienta (agujeros, rebajes, estrias, ventanas) y borra las herramientas. Las caras
    del corte llevan el material de `ob`. Varias herramientas que no se tocan van mejor juntas: unir(...)."""
    for h in herramientas:
        if isinstance(h, (list, tuple)):
            restar(ob, *h)
        else:
            _booleana(ob, h, "DIFFERENCE")
    return ob


def intersecar(ob, otra):
    """Deja de `ob` solo lo que esta dentro de `otra` (p. ej. el perfil lateral por la seccion: una corredera con
    los cantos de arriba achaflanados y la nariz redondeada)."""
    return _booleana(ob, otra, "INTERSECT")


def unir(*obs):
    """Junta mallas en la primera, sin booleana (las piezas pueden solaparse), conservando cada material."""
    obs = [o for o in obs if o is not None]
    base = obs[0]
    mats = list(base.data.materials)
    bm = bmesh.new()
    bm.from_mesh(base.data)
    for o in obs[1:]:
        remap = []
        for m in o.data.materials:
            if m not in mats:
                mats.append(m)
            remap.append(mats.index(m))
        bo = bmesh.new()
        bo.from_mesh(o.data)
        for f in bo.faces:
            f.material_index = remap[f.material_index] if remap else 0
        tmp = bpy.data.meshes.new("tmp")
        bo.to_mesh(tmp)
        bo.free()
        bm.from_mesh(tmp)
        bpy.data.meshes.remove(tmp)
    base.data.materials.clear()
    for m in mats:
        base.data.materials.append(m)
    bm.to_mesh(base.data)
    bm.free()
    quitar(*obs[1:])
    return base


def biselar(ob, ancho, segmentos=2, angulo=30.0, perfil_=0.5):
    """Bisela (redondea) las aristas de mas de `angulo` grados: lo que hace que un arma lea a distancia y no
    parezca de cajas. Se recorta solo donde no cabe (clamp)."""
    mod = ob.modifiers.new("bisel", "BEVEL")
    mod.width = ancho
    mod.segments = segmentos
    mod.limit_method = "ANGLE"
    mod.angle_limit = math.radians(angulo)
    mod.use_clamp_overlap = True
    mod.profile = perfil_
    mod.miter_outer = "MITER_ARC"
    return _aplicar(ob)


def deformar(ob, f):
    """Mueve cada vertice: f(x, y, z) -> (x, y, z). Para ahusar, curvar o abombar una pieza."""
    for v in ob.data.vertices:
        v.co = Vector(f(v.co.x, v.co.y, v.co.z))
    ob.data.update()
    return ob


def mover(ob, d):
    ob.data.transform(Matrix.Translation(Vector(d)))
    ob.data.update()
    return ob


def girar(ob, grados, eje="z", centro=(0.0, 0.0, 0.0)):
    c = Vector(centro)
    R = Matrix.Rotation(math.radians(grados), 4, eje.upper() if isinstance(eje, str) else Vector(eje))
    ob.data.transform(Matrix.Translation(c) @ R @ Matrix.Translation(-c))
    ob.data.update()
    return ob


def escalar(ob, s, centro=(0.0, 0.0, 0.0)):
    s = (s, s, s) if isinstance(s, (int, float)) else s
    c = Vector(centro)
    S = Matrix.Diagonal((s[0], s[1], s[2], 1.0))
    ob.data.transform(Matrix.Translation(c) @ S @ Matrix.Translation(-c))
    if s[0] * s[1] * s[2] < 0:
        ob.data.flip_normals()
    ob.data.update()
    return ob


def copiar(ob):
    nuevo_ = bpy.data.objects.new(ob.name, ob.data.copy())
    bpy.context.scene.collection.objects.link(nuevo_)
    _piezas.append(nuevo_)
    return nuevo_


def espejo(ob, eje="z"):
    """Una copia reflejada (por el plano z = 0 de serie): la pieza del otro lado."""
    s = {"x": (-1, 1, 1), "y": (1, -1, 1), "z": (1, 1, -1)}[eje]
    return escalar(copiar(ob), s)


def repetir(ob, n, paso, juntas=True):
    """n copias de `ob` (contando la original) desplazadas `paso` cada una: estrias, remaches, aletas de
    refrigeracion, ranuras del guardamanos. Con `juntas` devuelve una sola pieza (lista para restar de una vez)."""
    copias = [ob]
    for i in range(1, n):
        c = copiar(ob)
        mover(c, Vector(paso) * i)
        copias.append(c)
    return unir(*copias) if juntas else copias


# ------------------------------------------------------------------ final
def terminar(nombre, origen=(0.0, 0.0, 0.0), angulo_suave=40.0, uv=True):
    """Une todas las piezas en un objeto `nombre`, lleva `origen` (el agarre) al (0, 0, 0), suaviza las normales
    (aristas de mas de `angulo_suave` grados duras; normales ponderadas por area para que las caras planas sigan
    planas junto a los biseles) y desenvuelve UV sin solapes (smart project). Devuelve el objeto."""
    vivas = [o for o in _piezas if o.data.polygons]
    for o in [o for o in _piezas if not o.data.polygons]:
        quitar(o)
    ob = unir(*vivas)
    ob.name = nombre
    ob.data.name = nombre
    mover(ob, -Vector(origen))
    me = ob.data
    bm = bmesh.new()
    bm.from_mesh(me)
    bmesh.ops.dissolve_degenerate(bm, edges=bm.edges[:], dist=1e-7)
    bm.to_mesh(me)
    bm.free()
    me.shade_smooth()
    me.set_sharp_from_angle(angle=math.radians(angulo_suave))
    mod = ob.modifiers.new("normales", "WEIGHTED_NORMAL")
    mod.mode = "FACE_AREA"
    mod.weight = 50
    mod.keep_sharp = True
    _aplicar(ob)
    if uv:
        _desenvolver(ob)
    return ob


def _desenvolver(ob):
    for o in bpy.context.scene.objects:
        o.select_set(False)
    bpy.context.view_layer.objects.active = ob
    ob.select_set(True)
    try:
        bpy.ops.object.mode_set(mode="EDIT")
        bpy.ops.mesh.select_all(action="SELECT")
        bpy.ops.uv.smart_project(angle_limit=math.radians(55), island_margin=0.004, area_weight=0.0,
                                 correct_aspect=True, scale_to_bounds=False)
        bpy.ops.object.mode_set(mode="OBJECT")
    except Exception as e:  # noqa: BLE001 - sin UV el texturizador hace las suyas igualmente
        print(f"[armas] AVISO: smart_project ha fallado ({e}); el OBJ base sale sin UV")
        if bpy.context.object and bpy.context.object.mode != "OBJECT":
            bpy.ops.object.mode_set(mode="OBJECT")


def medidas(ob):
    me = ob.data
    me.calc_loop_triangles()
    xs = [v.co.x for v in me.vertices]
    ys = [v.co.y for v in me.vertices]
    zs = [v.co.z for v in me.vertices]
    por_mat = {}
    for t in me.loop_triangles:
        n = me.materials[t.material_index].name if me.materials else "default"
        por_mat[n] = por_mat.get(n, 0) + 1
    return {"min": [min(xs), min(ys), min(zs)], "max": [max(xs), max(ys), max(zs)],
            "triangulos": len(me.loop_triangles), "vertices": len(me.vertices), "triangulos_material": por_mat}


def exportar_obj(ob, ruta_obj):
    """Escribe <n>.obj + <n>.mtl (+ <n>.veta.json si hay maderas con veta) en los ejes de la saga, tal cual:
    un solo objeto, triangulos, normales por esquina (las suaves y las duras), UV, y un usemtl por material con
    su Kd (el tinte de la receta). Devuelve las medidas."""
    me = ob.data
    me.calc_loop_triangles()
    nombre = os.path.splitext(os.path.basename(ruta_obj))[0]
    ruta_mtl = os.path.splitext(ruta_obj)[0] + ".mtl"
    normales = me.corner_normals
    capa_uv = me.uv_layers.active.data if me.uv_layers.active else None
    vt, vn = {}, {}
    lista_vt, lista_vn = [], []
    por_mat = {}

    def indice(d, lista, clave):
        i = d.get(clave)
        if i is None:
            i = d[clave] = len(lista)
            lista.append(clave)
        return i

    for t in me.loop_triangles:
        esquinas = []
        for li, vi in zip(t.loops, t.vertices):
            n = normales[li].vector
            i_n = indice(vn, lista_vn, (round(n.x, 4), round(n.y, 4), round(n.z, 4)))
            i_t = -1
            if capa_uv is not None:
                u = capa_uv[li].uv
                i_t = indice(vt, lista_vt, (round(u.x, 5), round(u.y, 5)))
            esquinas.append((vi, i_t, i_n))
        mat = me.materials[t.material_index].name if me.materials else "default"
        por_mat.setdefault(mat, []).append(esquinas)
    os.makedirs(os.path.dirname(ruta_obj) or ".", exist_ok=True)
    with open(ruta_obj, "w", encoding="utf-8", newline="\n") as f:
        f.write(f"# {nombre} - kit de armas de la saga (cerebro/Guardianes/Tools~/arte/armas)\n")
        f.write("# metros; canon +X, arriba +Y, grosor Z; origen en el agarre de la mano derecha\n")
        f.write(f"mtllib {os.path.basename(ruta_mtl)}\no {nombre}\n")
        for v in me.vertices:
            f.write(f"v {v.co.x:.6f} {v.co.y:.6f} {v.co.z:.6f}\n")
        for u, v in lista_vt:
            f.write(f"vt {u:.5f} {v:.5f}\n")
        for x, y, z in lista_vn:
            f.write(f"vn {x:.4f} {y:.4f} {z:.4f}\n")
        for mat in sorted(por_mat):
            f.write(f"usemtl {mat}\n")
            for esq in por_mat[mat]:
                if capa_uv is not None:
                    f.write("f " + " ".join(f"{a + 1}/{b + 1}/{c + 1}" for a, b, c in esq) + "\n")
                else:
                    f.write("f " + " ".join(f"{a + 1}//{c + 1}" for a, _b, c in esq) + "\n")
    with open(ruta_mtl, "w", encoding="utf-8", newline="\n") as f:
        for mat in sorted(por_mat):
            d = _materiales.get(mat, {"kd": (0.5, 0.5, 0.5), "rugosidad": 0.5, "metal": 0.0})
            kd = d["kd"]
            ks = 0.5 if d["metal"] > 0.5 else 0.2
            f.write(f"newmtl {mat}\nKa 0.0 0.0 0.0\nKd {kd[0]:.4f} {kd[1]:.4f} {kd[2]:.4f}\n"
                    f"Ks {ks:.3f} {ks:.3f} {ks:.3f}\nNs {max(2, int(round((1 - d['rugosidad']) * 100)))}\n"
                    f"Pr {d['rugosidad']:.3f}\nPm {d['metal']:.3f}\nillum 2\n\n")
    vetas = {m: {"dir": d["veta"]} for m, d in _materiales.items() if d.get("veta") and m in por_mat}
    ruta_veta = os.path.splitext(ruta_obj)[0] + ".veta.json"
    if vetas:
        with open(ruta_veta, "w", encoding="utf-8") as f:
            json.dump({"materiales": vetas}, f, indent=1)
    elif os.path.isfile(ruta_veta):
        os.remove(ruta_veta)
    m = medidas(ob)
    m["materiales"] = {k: {"kd": list(v["kd"]), "rugosidad": v["rugosidad"], "metal": v["metal"]}
                       for k, v in _materiales.items() if k in por_mat}
    return m
