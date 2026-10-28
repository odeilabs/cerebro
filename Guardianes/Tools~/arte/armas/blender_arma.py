"""Lo que corre dentro de Blender: modela un arma con el kit, la exporta y hace sus renders. Lo lanza
construir_arma.py; a mano:

    blender -b --factory-startup --python blender_arma.py -- --modelo modelos/gd_glock17.py
            --salida <proyectos/gd_glock17/salida> --trabajo <proyectos/gd_glock17/trabajo> [--origen agarre|linea]
    blender -b --factory-startup --python blender_arma.py -- --hoja a_pbr.obj,b_pbr.obj --trabajo <carpeta>

Modelo: <salida>/<n>.obj/.mtl (+ .veta.json) y <n>_preview.png; <trabajo>/<n>_medidas.json, <n>_lado.png
(ortografica desde el lado izquierdo, -Z, con la boca a la izquierda como las laminas; px_m pixeles por metro) y
<n>_tres_cuartos.png (perspectiva, la camara de FICHA["vista_34"]). Los PNG de trabajo llevan fondo transparente.
Hoja: <trabajo>/<obj>_hoja.png de cada OBJ texturizado, todos a los mismos pixeles por metro, y hoja_medidas.json.
"""
import importlib.util
import json
import math
import os
import sys

import bpy
from mathutils import Matrix, Vector

AQUI = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, AQUI)
import kit_armas as k  # noqa: E402

GRIS_FONDO = (0.42, 0.42, 0.42)


def argumentos():
    import argparse
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    p = argparse.ArgumentParser()
    p.add_argument("--modelo")
    p.add_argument("--salida")
    p.add_argument("--trabajo", required=True)
    p.add_argument("--origen", choices=("agarre", "linea"), default="agarre")
    p.add_argument("--sin-render", action="store_true")
    p.add_argument("--hoja", default=None, help="OBJ texturizados separados por comas")
    p.add_argument("--px-m", type=float, default=0.0)
    return p.parse_args(argv)


def cargar_modelo(ruta):
    spec = importlib.util.spec_from_file_location("modelo_arma", ruta)
    mod = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(mod)
    return mod


def _v3(p):
    if p is None:
        return None
    p = tuple(float(v) for v in p)
    return Vector(p if len(p) == 3 else (p[0], p[1], 0.0))


# ------------------------------------------------------------------ renders
def preparar_render(transparente=True, color="MATERIAL"):
    sc = bpy.context.scene
    sc.render.engine = "BLENDER_WORKBENCH"
    sh = sc.display.shading
    sh.light = "STUDIO"
    sh.color_type = color
    sh.show_specular_highlight = True
    sh.show_cavity = True
    sh.cavity_type = "BOTH"
    sh.cavity_ridge_factor = 1.0
    sh.cavity_valley_factor = 1.0
    sh.show_object_outline = False
    sc.display.render_aa = "16"
    sc.render.film_transparent = transparente
    sc.render.image_settings.file_format = "PNG"
    sc.render.image_settings.color_mode = "RGBA" if transparente else "RGB"
    sc.view_settings.view_transform = "Standard"
    if sc.world is None:
        sc.world = bpy.data.worlds.new("fondo")
    sc.world.color = GRIS_FONDO
    return sc


def _camara(nombre):
    cam = bpy.data.cameras.new(nombre)
    ob = bpy.data.objects.new(nombre, cam)
    bpy.context.scene.collection.objects.link(ob)
    bpy.context.scene.camera = ob
    return ob


def mirar(desde, hacia, arriba=Vector((0.0, 1.0, 0.0))):
    """Giro de una camara en `desde` que mira a `hacia` con +Y arriba (to_track_quat usaria la Z de Blender)."""
    f = (hacia - desde).normalized()
    derecha = f.cross(arriba).normalized()
    sube = derecha.cross(f).normalized()
    return Matrix((derecha, sube, -f)).transposed().to_quaternion()


def render_lado(caja_min, caja_max, ruta, px_m, margen=0.06):
    """Ortografica desde -Z (el lado izquierdo del tirador): la boca (+X) sale a la izquierda, como en las laminas.
    Devuelve el mapeo de pixeles: el centro del PNG es `centro` (x, y) y hay px_m pixeles por metro."""
    sc = bpy.context.scene
    lx = caja_max.x - caja_min.x + 2 * margen * (caja_max.x - caja_min.x)
    ly = caja_max.y - caja_min.y + 2 * margen * (caja_max.x - caja_min.x)
    W, H = int(math.ceil(lx * px_m)), int(math.ceil(ly * px_m))
    W += W % 2
    H += H % 2
    sc.render.resolution_x, sc.render.resolution_y = W, H
    sc.render.resolution_percentage = 100
    c = (caja_min + caja_max) * 0.5
    ob = _camara("lado")
    ob.data.type = "ORTHO"
    ob.data.ortho_scale = max(W, H) / px_m
    ob.data.clip_start, ob.data.clip_end = 0.01, 10.0
    ob.location = (c.x, c.y, caja_min.z - 1.0)
    ob.rotation_euler = (0.0, math.pi, 0.0)
    sc.render.filepath = ruta
    bpy.ops.render.render(write_still=True)
    bpy.data.objects.remove(ob)
    return {"ruta": ruta, "px_m": px_m, "centro": [c.x, c.y], "ancho": W, "alto": H}


def render_tres_cuartos(caja_min, caja_max, ruta, azimut=28.0, elevacion=12.0, focal=100.0, ancho=1200, alto=800):
    """Perspectiva desde el lado izquierdo y por delante (azimut desde -Z hacia +X, grados), como la lamina de tres
    cuartos."""
    sc = bpy.context.scene
    sc.render.resolution_x, sc.render.resolution_y = ancho, alto
    c = (caja_min + caja_max) * 0.5
    radio = (caja_max - caja_min).length * 0.5
    ob = _camara("tres_cuartos")
    ob.data.lens = focal
    ob.data.sensor_width = 36.0
    fov = 2 * math.atan(18.0 / focal)
    d = radio / math.sin(fov * 0.5) * 1.02
    az, el = math.radians(azimut), math.radians(elevacion)
    direccion = Vector((math.sin(az) * math.cos(el), math.sin(el), -math.cos(az) * math.cos(el)))
    ob.location = c + direccion * d
    ob.rotation_euler = mirar(ob.location, c).to_euler()
    ob.data.clip_start, ob.data.clip_end = 0.01, d * 4
    sc.render.filepath = ruta
    bpy.ops.render.render(write_still=True)
    bpy.data.objects.remove(ob)
    return {"ruta": ruta, "azimut": azimut, "elevacion": elevacion, "focal": focal}


# ------------------------------------------------------------------ modelo
def modelar(a):
    mod = cargar_modelo(a.modelo)
    ficha = dict(getattr(mod, "FICHA", {}))
    nombre = ficha.get("nombre") or os.path.splitext(os.path.basename(a.modelo))[0]
    k.nuevo(int(ficha.get("semilla", 0)))
    k.cargar_laminas(a.trabajo, nombre)
    res = mod.modelar(k) or {}
    agarre = _v3(res.get("agarre", (0, 0, 0)))
    boca = _v3(res["boca"])
    guarda = _v3(res.get("guardamanos"))
    origen = agarre.copy() if a.origen == "agarre" else Vector((agarre.x, boca.y, 0.0))
    ob = k.terminar(nombre, origen, angulo_suave=float(ficha.get("angulo_suave", 40.0)))
    os.makedirs(a.salida, exist_ok=True)
    os.makedirs(a.trabajo, exist_ok=True)
    m = k.exportar_obj(ob, os.path.join(a.salida, nombre + ".obj"))
    m["nombre"] = nombre
    m["origen"] = a.origen
    m["agarre"] = list(agarre - origen)
    m["boca"] = list(boca - origen)
    m["guardamanos"] = list(guarda - origen) if guarda is not None else None
    cal = res.get("calibracion")
    if cal is not None:
        m["calibracion"] = {"sx_mm_px": cal.sx * 1000, "sy_mm_px": cal.sy * 1000, "deformacion": cal.deformacion}
    m["notas"] = res.get("notas", [])
    if not a.sin_render:
        mn, mx = Vector(m["min"]), Vector(m["max"])
        largo = mx.x - mn.x
        px_m = a.px_m or min(6000.0, 1800.0 / max(largo, 1e-3))
        preparar_render(True)
        m["render_lado"] = render_lado(mn, mx, os.path.join(a.trabajo, nombre + "_lado.png"), px_m)
        v34 = ficha.get("vista_34", {})
        m["render_tres_cuartos"] = render_tres_cuartos(
            mn, mx, os.path.join(a.trabajo, nombre + "_tres_cuartos.png"), v34.get("azimut", 28.0),
            v34.get("elevacion", 12.0), v34.get("focal", 100.0))
        preparar_render(False)
        bpy.context.scene.world.color = (0.30, 0.30, 0.30)
        render_tres_cuartos(mn, mx, os.path.join(a.salida, nombre + "_preview.png"), v34.get("azimut", 28.0),
                            v34.get("elevacion", 12.0), v34.get("focal", 100.0), 512, 512)
    with open(os.path.join(a.trabajo, nombre + "_medidas.json"), "w", encoding="utf-8") as f:
        json.dump(m, f, indent=1)
    print(f"[armas] {nombre}: {m['triangulos']} triangulos, caja {[round(v, 4) for v in m['min']]} .. "
          f"{[round(v, 4) for v in m['max']]}")


# ------------------------------------------------------------------ hoja de contactos
def hoja(a):
    """Cada OBJ texturizado de lado, con sus texturas (basecolor), a los mismos pixeles por metro."""
    bpy.ops.wm.read_factory_settings(use_empty=True)
    preparar_render(True, color="TEXTURE")
    px_m = a.px_m or 900.0
    salida = {}
    os.makedirs(a.trabajo, exist_ok=True)
    for ruta in [r for r in a.hoja.split(",") if r]:
        antes = set(bpy.data.objects)
        # forward Y / up Z: sin conversion, los ejes del OBJ tal cual (Y arriba, como el kit)
        bpy.ops.wm.obj_import(filepath=ruta, forward_axis="Y", up_axis="Z")
        nuevos = [o for o in bpy.data.objects if o not in antes and o.type == "MESH"]
        pts = [o.matrix_world @ v.co for o in nuevos for v in o.data.vertices]
        mn = Vector((min(p.x for p in pts), min(p.y for p in pts), min(p.z for p in pts)))
        mx = Vector((max(p.x for p in pts), max(p.y for p in pts), max(p.z for p in pts)))
        nombre = os.path.splitext(os.path.basename(ruta))[0]
        r = render_lado(mn, mx, os.path.join(a.trabajo, nombre + "_hoja.png"), px_m, margen=0.02)
        tris = 0
        for o in nuevos:
            o.data.calc_loop_triangles()
            tris += len(o.data.loop_triangles)
        r.update({"min": list(mn), "max": list(mx), "triangulos": tris})
        salida[nombre] = r
        for o in nuevos:
            bpy.data.objects.remove(o)
    with open(os.path.join(a.trabajo, "hoja_medidas.json"), "w", encoding="utf-8") as f:
        json.dump(salida, f, indent=1)


def main():
    a = argumentos()
    if a.hoja:
        hoja(a)
    else:
        modelar(a)


if __name__ == "__main__":
    main()
