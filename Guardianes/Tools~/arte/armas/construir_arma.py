"""Construye un arma de la saga de punta a punta: modelo (Blender) -> texturas PBR (texturizador de AssetsCreator,
sin tocarlo) -> <n>_agarre.json -> render de comparacion con su lamina. Con el python del .venv de AssetsCreator:

    python construir_arma.py gd_glock17 [--res 1024] [--solo modelo,texturas,agarre,comparar]
                             [--revision <carpeta>] [--origen agarre|linea] [--blender <blender.exe>]
    python construir_arma.py --hoja gd_glock17,kk_ak47,gd_mp5 [--salida hoja.png] [--px-m 900]

El modelo es modelos/<n>.py (FICHA + modelar(k)). Deja, como un `objeto --pbr` de AssetsCreator:
    AssetsCreatorWin/proyectos/<n>/salida/  <n>.obj/.mtl, <n>_preview.png, <n>_pbr.obj/.mtl,
        <n>_pbr_<material>_{basecolor,normal,roughness,ao,height[,metallic]}.png (+ _material.json),
        <n>_pbr_preview.png y <n>_agarre.json
    AssetsCreatorWin/proyectos/<n>/trabajo/ contornos de la lamina, medidas, renders de lado y de tres cuartos
    <revision>/<n>_comparar.png (por defecto en trabajo/)
Solo escribe en proyectos/<n> si <n> empieza por gd_ y la carpeta es nueva o ya es de este kit (lleva
trabajo/kit_armas.txt): nunca pisa un proyecto de otro.
"""
import argparse
import importlib.util
import json
import os
import re
import shutil
import subprocess
import sys
import time

import numpy as np
from PIL import Image, ImageDraw

AQUI = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, AQUI)
import laminas  # noqa: E402

PREFIJO = "gd_"
MARCA = "kit_armas.txt"
FONDO = (200, 200, 200)
CLASES = ("Pistola", "Escopeta", "Subfusil", "FusilAsalto", "FusilCaza", "Ametralladora")


# ------------------------------------------------------------------ utilidades
def blender_exe(dado=None):
    for c in (dado, os.environ.get("BLENDER"), os.environ.get("ASSETSCREATOR_BLENDER")):
        if c and os.path.isfile(c):
            return c
    base = r"C:\Program Files\Blender Foundation"
    if os.path.isdir(base):
        for d in sorted(os.listdir(base), reverse=True):
            c = os.path.join(base, d, "blender.exe")
            if os.path.isfile(c):
                return c
    c = shutil.which("blender")
    if c:
        return c
    raise SystemExit("No encuentro Blender (pon --blender o %BLENDER%)")


def cargar_ficha(nombre):
    ruta = os.path.join(AQUI, "modelos", nombre + ".py")
    if not os.path.isfile(ruta):
        raise SystemExit(f"No existe el modelo {ruta}")
    spec = importlib.util.spec_from_file_location("ficha_" + nombre, ruta)
    mod = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(mod)
    ficha = dict(mod.FICHA)
    if ficha.get("clase") not in CLASES:
        raise SystemExit(f"{nombre}: FICHA['clase'] tiene que ser una de {CLASES}")
    return ruta, ficha


def carpetas(nombre, revision=None):
    ac = laminas.assetscreator()
    proyecto = os.path.join(ac, "proyectos", nombre)
    if not nombre.startswith(PREFIJO):
        raise SystemExit(f"El kit solo escribe proyectos {PREFIJO}*: {nombre}")
    marca = os.path.join(proyecto, "trabajo", MARCA)
    if os.path.isdir(proyecto) and os.listdir(proyecto) and not os.path.isfile(marca):
        raise SystemExit(f"{proyecto} ya existe y no es de este kit: no lo toco")
    salida = os.path.join(proyecto, "salida")
    trabajo = os.path.join(proyecto, "trabajo")
    os.makedirs(salida, exist_ok=True)
    os.makedirs(trabajo, exist_ok=True)
    if not os.path.isfile(marca):
        with open(marca, "w", encoding="utf-8") as f:
            f.write("Proyecto del kit de armas de la saga (cerebro/Guardianes/Tools~/arte/armas).\n"
                    "Se regenera entero con construir_arma.py; no lo edites a mano.\n")
    return ac, salida, trabajo, os.path.abspath(revision or trabajo)


def leer_json(ruta):
    with open(ruta, encoding="utf-8") as f:
        return json.load(f)


# ------------------------------------------------------------------ pasos
def paso_laminas(ficha, nombre, trabajo):
    for vista in ("lateral", "tres_cuartos"):
        ruta = laminas.ruta_lamina(ficha["lamina"], vista)
        d = laminas.datos_lamina(ruta, ficha.get("umbral", 22.0), ficha.get("tolerancia", 1.5),
                                 ficha.get("suavizado", 2.0))
        with open(os.path.join(trabajo, f"{nombre}_lamina_{vista}.json"), "w", encoding="utf-8") as f:
            json.dump(d, f)


def paso_modelo(ruta_modelo, salida, trabajo, origen, blender):
    cmd = [blender, "-b", "--factory-startup", "--python", os.path.join(AQUI, "blender_arma.py"), "--",
           "--modelo", ruta_modelo, "--salida", salida, "--trabajo", trabajo, "--origen", origen]
    t = time.time()
    r = subprocess.run(cmd, capture_output=True, text=True, encoding="utf-8", errors="replace")
    log = os.path.join(trabajo, "blender.log")
    with open(log, "w", encoding="utf-8") as f:
        f.write(r.stdout + "\n" + r.stderr)
    for linea in r.stdout.splitlines():
        if linea.startswith("[armas]") or "Error" in linea or "Traceback" in linea:
            print("   ", linea)
    if r.returncode != 0 or "Traceback" in r.stdout + r.stderr:
        raise SystemExit(f"Blender ha fallado (ver {log})")
    print(f"   modelo en {time.time() - t:.1f} s")


def recetas_de(ac, materiales):
    """La receta que el texturizador dara a cada material (ultra/recetas_nombre.py, solo se lee)."""
    antes = sys.dont_write_bytecode
    sys.dont_write_bytecode = True                # ni un .pyc en la carpeta de AssetsCreator
    try:
        spec = importlib.util.spec_from_file_location("recetas_nombre_ac", os.path.join(ac, "ultra", "recetas_nombre.py"))
        rn = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(rn)
        return {m: (rn.resolver(m) or ("plastico (por defecto)",))[0] for m in materiales}
    except Exception as e:  # noqa: BLE001 - la tabla es de otra sesion y puede estar a medio cambiar
        print(f"   AVISO: no he podido leer recetas_nombre.py ({e})")
        return {m: "?" for m in materiales}
    finally:
        sys.dont_write_bytecode = antes


def limpiar_pbr(salida, nombre):
    """Quita los mapas PBR de una pasada anterior (un material renombrado dejaria PNG viejos en la
    salida). Solo dentro del proyecto gd_ del kit."""
    for f in os.listdir(salida):
        if f.startswith(nombre + "_pbr"):
            os.remove(os.path.join(salida, f))


def paso_texturas(ac, salida, nombre, ficha, res):
    tex = ficha.get("texturas", {})
    py = os.path.join(ac, ".venv", "Scripts", "python.exe")
    if not os.path.isfile(py):
        py = sys.executable
    obj = os.path.join(salida, nombre + ".obj")
    cmd = [py, os.path.join("ultra", "texturizar_obj.py"), obj, "--res", str(res or tex.get("res", 1024)),
           "--escala", str(tex.get("escala", 1.0))]
    limpiar_pbr(salida, nombre)
    for intento in (1, 2):
        t = time.time()
        r = subprocess.run(cmd, cwd=ac, capture_output=True, text=True, encoding="utf-8", errors="replace")
        with open(os.path.join(os.path.dirname(salida), "trabajo", "texturizar.log"), "w", encoding="utf-8") as f:
            f.write(r.stdout + "\n" + r.stderr)
        if r.returncode == 0 and os.path.isfile(os.path.join(salida, nombre + "_pbr.obj")):
            for linea in r.stdout.splitlines():
                if linea.startswith(">>> mapas") or linea.startswith("OK"):
                    print("   ", linea)
            print(f"   texturas en {time.time() - t:.1f} s")
            return " ".join(['"%s"' % c if " " in c else c for c in cmd])
        print(f"   el texturizador ha fallado (intento {intento}): {r.stderr.strip().splitlines()[-1:] }")
        if intento == 1:
            print("   (su codigo puede estar a medio cambiar en otra sesion) espero 90 s y reintento una vez")
            time.sleep(90)
    raise SystemExit("El texturizador de AssetsCreator falla dos veces: ver trabajo/texturizar.log")


def paso_agarre(ac, salida, trabajo, nombre, ficha, res, origen):
    m = leer_json(os.path.join(trabajo, nombre + "_medidas.json"))
    mats = sorted(m.get("materiales", {}))
    receta = (f'"{os.path.join(ac, ".venv", "Scripts", "python.exe")}" "{os.path.join(AQUI, "construir_arma.py")}" '
              f'{nombre} --res {res or ficha.get("texturas", {}).get("res", 1024)}'
              + (f" --origen {origen}" if origen != "agarre" else ""))
    pbr = os.path.join(salida, nombre + "_pbr.obj")
    base = os.path.join(salida, nombre + ".obj")
    if not os.path.isfile(pbr) or os.path.getmtime(pbr) < os.path.getmtime(base):
        print("   AVISO: el _pbr.obj falta o es de un modelo anterior: lanza tambien el paso de texturas")
    r = lambda v: [round(float(c), 5) for c in v]  # noqa: E731
    d = {
        "nombre": nombre,
        "clase": ficha["clase"],
        "lamina": ficha.get("lamina"),
        "unidades": "m",
        "largo": round(m["max"][0] - m["min"][0], 5),
        "alto": round(m["max"][1] - m["min"][1], 5),
        "ancho": round(m["max"][2] - m["min"][2], 5),
        "agarre": r(m["agarre"]),
        "boca": r(m["boca"]),
        "guardamanos": r(m["guardamanos"]) if m.get("guardamanos") else None,
        "ejes": {"canon": "+X", "arriba": "+Y"},
        "linea_tiro": {"y": round(m["boca"][1], 5), "z": round(m["boca"][2], 5)},
        "caja": {"min": r(m["min"]), "max": r(m["max"])},
        "triangulos": m["triangulos"],
        "vertices": m["vertices"],
        "materiales": recetas_de(ac, mats),
        "receta": receta,
        "nota_unity": "El importador de OBJ de Unity cambia el signo de X: alli el canon es -X y la x de "
                      "boca y guardamanos cambia de signo (lo hace ConstructorHumanos.LeerFicha al leer este fichero).",
    }
    texto = json.dumps(d, indent=1, ensure_ascii=False)
    # las ternas [x, y, z] en una linea
    texto = re.sub(r"\[\s+([-\d.e]+),\s+([-\d.e]+),\s+([-\d.e]+)\s+\]", r"[\1, \2, \3]", texto)
    with open(os.path.join(salida, nombre + "_agarre.json"), "w", encoding="utf-8") as f:
        f.write(texto + "\n")
    return d


# ------------------------------------------------------------------ comparacion
def _sobre_fondo(im, fondo=FONDO):
    base = Image.new("RGBA", im.size, fondo + (255,))
    base.alpha_composite(im.convert("RGBA"))
    return base.convert("RGB")


def _recorte_alfa(im, umbral=8):
    a = np.asarray(im.convert("RGBA"))[..., 3]
    ys, xs = np.nonzero(a > umbral)
    return im.crop((int(xs.min()), int(ys.min()), int(xs.max()) + 1, int(ys.max()) + 1))


def _pegar_centrado(panel, im, cx=None, cy=None):
    cx = panel.width // 2 if cx is None else cx
    cy = panel.height // 2 if cy is None else cy
    x, y = int(round(cx - im.width / 2)), int(round(cy - im.height / 2))
    if im.mode == "RGBA":
        panel.paste(im, (x, y), im)
    else:
        panel.paste(im, (x, y))


def _rotulo(d, xy, texto, tam=20, color=(20, 20, 20)):
    d.text(xy, texto, fill=color, font=laminas.fuente(tam))


def _barra(d, x, y, px_m, metros=0.05, texto=None):
    L = int(round(metros * px_m))
    d.rectangle((x, y, x + L, y + 6), fill=(30, 30, 30))
    for i in range(0, 6):
        if i % 2:
            d.rectangle((x + L * i // 5, y + 1, x + L * (i + 1) // 5, y + 5), fill=(235, 235, 235))
    _rotulo(d, (x, y + 10), texto or f"{metros * 100:g} cm", 16)


def iou(a, b):
    u = np.logical_or(a, b).sum()
    return float(np.logical_and(a, b).sum() / u) if u else 0.0


def comparar(nombre, ficha, trabajo, revision, ancho_panel=1000):
    """El PNG de revision: la lamina lateral escalada por el largo real junto al modelo de lado (los dos a los mismos
    pixeles por metro), la de tres cuartos junto al modelo de tres cuartos (al mismo ancho de pantalla), y las dos
    siluetas superpuestas con su IoU."""
    m = leer_json(os.path.join(trabajo, nombre + "_medidas.json"))
    largo = m["max"][0] - m["min"][0]
    alto = m["max"][1] - m["min"][1]
    k_px = 0.86 * ancho_panel / largo                         # pixeles por metro de los paneles de lado
    alto_panel = int(max(alto * k_px / 0.80, 0.45 * ancho_panel))

    # --- modelo de lado
    lado = Image.open(m["render_lado"]["ruta"]).convert("RGBA")
    f = k_px / m["render_lado"]["px_m"]
    lado = lado.resize((max(1, round(lado.width * f)), max(1, round(lado.height * f))), Image.LANCZOS)
    lado = _recorte_alfa(lado)
    mod_mask = np.asarray(lado)[..., 3] > 127

    # --- lamina lateral escalada por el largo real (la caja de la silueta = el largo del modelo)
    ruta_lat = laminas.ruta_lamina(ficha["lamina"], "lateral")
    lam_d = leer_json(os.path.join(trabajo, f"{nombre}_lamina_lateral.json"))
    x0, y0, x1, y1 = lam_d["caja"]
    lam = Image.open(ruta_lat).convert("RGB")
    f_lam = k_px * largo / (x1 - x0 + 1)
    margen = 40
    rec = lam.crop((x0 - margen, y0 - margen, x1 + 1 + margen, y1 + 1 + margen))
    rec = rec.resize((round(rec.width * f_lam), round(rec.height * f_lam)), Image.LANCZOS)
    lam_mask_full = laminas.silueta(ruta_lat, lam_d.get("umbral", 22.0))
    lm = lam_mask_full[y0:y1 + 1, x0:x1 + 1]
    alto_lamina_m = (y1 - y0 + 1) / (x1 - x0 + 1) * largo

    def panel():
        return Image.new("RGB", (ancho_panel, alto_panel), FONDO)

    p_lam, p_mod = panel(), panel()
    _pegar_centrado(p_lam, rec)
    _pegar_centrado(p_mod, _sobre_fondo(lado))

    # --- siluetas: la lamina ajustada a la caja del modelo (sus dos escalas) y por el largo (una escala)
    h, w = mod_mask.shape
    aniso = np.asarray(Image.fromarray(lm).resize((w, h), Image.NEAREST))
    iou_aniso = iou(mod_mask, aniso)
    hi = max(1, round(lm.shape[0] * w / lm.shape[1]))
    iso_img = np.asarray(Image.fromarray(lm).resize((w, hi), Image.NEAREST))
    H = max(h, hi)
    A = np.zeros((H, w), bool)
    B = np.zeros((H, w), bool)
    A[:h] = mod_mask
    B[:hi] = iso_img                                           # alineadas por arriba (las miras)
    iou_iso = iou(A, B)
    sup = np.full((h, w, 3), FONDO, np.uint8)
    sup[aniso & ~mod_mask] = (215, 60, 50)
    sup[mod_mask & ~aniso] = (40, 170, 210)
    sup[mod_mask & aniso] = (90, 90, 90)
    p_sup = panel()
    _pegar_centrado(p_sup, Image.fromarray(sup))

    # --- tres cuartos: los dos al mismo ancho de pantalla
    t34 = _recorte_alfa(Image.open(m["render_tres_cuartos"]["ruta"]).convert("RGBA"))
    ruta_34 = laminas.ruta_lamina(ficha["lamina"], "tres_cuartos")
    d34 = leer_json(os.path.join(trabajo, f"{nombre}_lamina_tres_cuartos.json"))
    a0, b0, a1, b1 = d34["caja"]
    l34 = Image.open(ruta_34).convert("RGB").crop((a0 - margen, b0 - margen, a1 + 1 + margen, b1 + 1 + margen))
    objetivo = 0.86 * ancho_panel
    l34 = l34.resize((round(l34.width * objetivo / (a1 - a0 + 1)), round(l34.height * objetivo / (a1 - a0 + 1))),
                     Image.LANCZOS)
    t34 = t34.resize((round(objetivo), round(t34.height * objetivo / t34.width)), Image.LANCZOS)
    alto34 = int(max(l34.height, t34.height) + 60)
    p_l34 = Image.new("RGB", (ancho_panel, alto34), FONDO)
    p_m34 = Image.new("RGB", (ancho_panel, alto34), FONDO)
    _pegar_centrado(p_l34, l34)
    _pegar_centrado(p_m34, _sobre_fondo(t34))

    # --- texto
    p_txt = panel()
    d = ImageDraw.Draw(p_txt)
    cal = m.get("calibracion") or {}
    lineas = [
        f"{nombre}  ({ficha['clase']}, lamina {ficha.get('lamina')})",
        f"largo {largo * 1000:.0f} mm   alto {alto * 1000:.0f} mm   ancho {(m['max'][2] - m['min'][2]) * 1000:.1f} mm",
        f"triangulos {m['triangulos']}   vertices {m['vertices']}",
        "triangulos por material:",
        *[f"    {k}: {v}" for k, v in sorted(m.get("triangulos_material", {}).items())],
        f"boca {[round(v * 1000, 1) for v in m['boca']]} mm desde el agarre",
        f"IoU de siluetas: {iou_aniso:.3f} (lamina ajustada a la caja)  {iou_iso:.3f} (solo por el largo)",
        f"la lamina a la escala del largo mide {alto_lamina_m * 1000:.0f} mm de alto",
    ]
    if cal:
        lineas.append(f"calibracion: {cal['sx_mm_px']:.4f} x {cal['sy_mm_px']:.4f} mm/px "
                      f"(deformacion de la lamina {cal['deformacion']:.3f})")
    for i, t in enumerate(lineas):
        _rotulo(d, (24, 24 + i * 34), t, 21)

    # --- montaje
    sep, cab = 12, 34
    filas = [(p_lam, p_mod, f"Lamina lateral escalada por el largo real ({largo * 1000:.0f} mm)",
              "Modelo, lado izquierdo (ortografica, misma escala)"),
             (p_l34, p_m34, "Lamina tres cuartos (al mismo ancho de pantalla)", "Modelo, tres cuartos"),
             (p_sup, p_txt, "Siluetas: gris las dos, rojo solo lamina, azul solo modelo", "Medidas")]
    W = 2 * ancho_panel + 3 * sep
    Ht = sum(a.height + cab + sep for a, _b, _c, _d in filas) + sep
    hoja = Image.new("RGB", (W, Ht), (235, 235, 235))
    dd = ImageDraw.Draw(hoja)
    y = sep
    for a, b, ta, tb in filas:
        _rotulo(dd, (sep + 6, y + 6), ta, 20)
        _rotulo(dd, (2 * sep + ancho_panel + 6, y + 6), tb, 20)
        hoja.paste(a, (sep, y + cab))
        hoja.paste(b, (2 * sep + ancho_panel, y + cab))
        y += a.height + cab + sep
    for xb in (sep, 2 * sep + ancho_panel):
        _barra(dd, xb + 20, sep + cab + alto_panel - 40, k_px, 0.05)
    os.makedirs(revision, exist_ok=True)
    ruta = os.path.join(revision, nombre + "_comparar.png")
    hoja.save(ruta)
    return ruta, iou_aniso, iou_iso


# ------------------------------------------------------------------ hoja de contactos
def hoja_contactos(nombres, salida, px_m, blender):
    """Varias armas de lado, con sus texturas, a la misma escala en metros (con barra de 10 cm). Vale cualquier
    proyecto de AssetsCreator con <n>_pbr.obj (los gd_ del kit y los demas, que solo se leen)."""
    ac = laminas.assetscreator()
    rutas = []
    for n in nombres:
        r = os.path.join(ac, "proyectos", n, "salida", n + "_pbr.obj")
        if not os.path.isfile(r):
            raise SystemExit(f"No existe {r}")
        rutas.append(r)
    tmp = os.path.join(os.path.dirname(os.path.abspath(salida)), "_hoja_armas")
    os.makedirs(tmp, exist_ok=True)
    cmd = [blender, "-b", "--factory-startup", "--python", os.path.join(AQUI, "blender_arma.py"), "--",
           "--hoja", ",".join(rutas), "--trabajo", tmp, "--px-m", str(px_m)]
    r = subprocess.run(cmd, capture_output=True, text=True, encoding="utf-8", errors="replace")
    if r.returncode != 0 or not os.path.isfile(os.path.join(tmp, "hoja_medidas.json")):
        print(r.stdout[-3000:], r.stderr[-3000:])
        raise SystemExit("Blender ha fallado en la hoja")
    med = leer_json(os.path.join(tmp, "hoja_medidas.json"))
    imgs = []
    for n in nombres:
        clave = n + "_pbr"
        im = _recorte_alfa(Image.open(med[clave]["ruta"]).convert("RGBA"))
        imgs.append((n, im, med[clave]))
    sep, cab = 24, 30
    W = max(im.width for _n, im, _m in imgs) + 2 * sep
    W = max(W, int(0.5 * px_m) + 2 * sep)
    Ht = sum(im.height + cab + sep for _n, im, _m in imgs) + sep + 60
    hoja = Image.new("RGB", (W, Ht), FONDO)
    d = ImageDraw.Draw(hoja)
    y = sep
    for n, im, mm_ in imgs:
        largo = mm_["max"][0] - mm_["min"][0]
        _rotulo(d, (sep, y), f"{n}   {largo * 1000:.0f} mm, {mm_['triangulos']} triangulos", 20)
        hoja.paste(_sobre_fondo(im), (sep, y + cab))
        y += im.height + cab + sep
    _barra(d, sep, y + 4, px_m, 0.10)
    _barra(d, sep + int(0.15 * px_m), y + 4, px_m, 0.50)
    hoja.save(salida)
    shutil.rmtree(tmp, ignore_errors=True)
    return salida


# ------------------------------------------------------------------ main
def main():
    p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    p.add_argument("nombre", nargs="?")
    p.add_argument("--res", type=int, default=0, help="resolucion de los mapas (por defecto la de la FICHA)")
    p.add_argument("--solo", default="laminas,modelo,texturas,agarre,comparar")
    p.add_argument("--revision", default=None, help="carpeta del PNG de comparacion (por defecto trabajo/)")
    p.add_argument("--origen", choices=("agarre", "linea"), default="agarre",
                   help="agarre: el origen en el punto de la mano; linea: x del agarre pero y = linea de tiro")
    p.add_argument("--blender", default=None)
    p.add_argument("--hoja", default=None, help="nombres de proyecto separados por comas")
    p.add_argument("--salida", default=None, help="PNG de la hoja de contactos")
    p.add_argument("--px-m", type=float, default=900.0)
    a = p.parse_args()
    if a.hoja:
        salida = a.salida or os.path.join(os.getcwd(), "hoja_armas.png")
        print(hoja_contactos([n for n in a.hoja.split(",") if n], salida, a.px_m, blender_exe(a.blender)))
        return 0
    if not a.nombre:
        p.error("falta el nombre del arma (gd_...)")
    ruta_modelo, ficha = cargar_ficha(a.nombre)
    ac, salida, trabajo, revision = carpetas(a.nombre, a.revision)
    pasos = set(a.solo.split(","))
    print(f"== {a.nombre} ({ficha['clase']}) -> {salida}")
    if "laminas" in pasos or "modelo" in pasos:
        print("-- contornos de la lamina")
        paso_laminas(ficha, a.nombre, trabajo)
    if "modelo" in pasos:
        print("-- modelo (Blender)")
        paso_modelo(ruta_modelo, salida, trabajo, a.origen, blender_exe(a.blender))
    if "texturas" in pasos:
        print("-- texturas (AssetsCreator ultra/texturizar_obj.py)")
        paso_texturas(ac, salida, a.nombre, ficha, a.res)
    if "agarre" in pasos or "texturas" in pasos:
        d = paso_agarre(ac, salida, trabajo, a.nombre, ficha, a.res, a.origen)
        print(f"-- agarre: boca {d['boca']}, {d['triangulos']} triangulos, recetas {d['materiales']}")
    if "comparar" in pasos:
        ruta, i_a, i_i = comparar(a.nombre, ficha, trabajo, revision)
        print(f"-- comparacion: {ruta} (IoU {i_a:.3f} ajustada, {i_i:.3f} por el largo)")
    return 0


if __name__ == "__main__":
    sys.exit(main())
