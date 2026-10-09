"""Helicoptero de ataque de la compania (campo de distancias y marching cubes).

    python <Guardianes>/Tools~/vehiculos/sdf_helicoptero.py --salida "<proyecto>/Assets/_Game/Art/Vehiculos/Helicoptero"

Biplaza en tandem al estilo del AH-64: fuselaje estrecho y facetado, gondolas laterales, dos
turbinas con toberas, botalon con deriva en flecha y estabilizador, alas cortas con un lanzacohetes
de 19 tubos y cuatro misiles por lado, tren de ruedas y torreta de canon bajo el morro. Las piezas
duras son poliedros, cajas y cilindros con las aristas redondeadas (maximo suave), unidos con
minimo suave pequeno: chapa, no globos.

Coordenadas de trabajo de Blender (Z arriba, -Y delante, +X izquierda); escribe OBJ en metros
con Y arriba (el morro hacia +Z en Unity), un fichero por material:
  fuselaje.obj  pintura                    metal.obj   tren, bujes y marcos de la cabina
  ruedas.obj    goma                       cabina.obj  cristales de la cabina y de los sensores
  canon.obj     torreta y canon, con el origen en el eje de la torreta (gira en Unity)
Las palas de los rotores las hace Unity (giran aparte). Puntos de anclaje para Unity (x, y, z):
cabeza del mastil (0, 3.56, -0.3), rotor de cola (-0.36, 3.85, -9.3), torreta (0, 0.6, 3.55),
boca del canon (0, 0, 1.62) desde la torreta, lanzacohetes (+-2.35, 1.08, 0.85), toberas
(+-1.3, 2.48, -2.62). Si encuentra Blender reduce las mallas (Tools/kong/decimar_obj.py).
"""
import argparse
import math
import os
import shutil
import subprocess
import sys

import numpy as np
from skimage import measure

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import sdf_kong as k  # noqa: E402
import sdf_huesos as h  # noqa: E402  (campo() y guardar())

k.ALTO = 1.0
E, C = h.E, h.C
BLENDER = r"C:\Program Files\Blender Foundation\Blender 5.1\blender.exe"
TORRETA = (0.0, -3.55, 0.6)


# ---------------------------------------------------------------------------- Primitivas duras
class Poliedro:
    """Interseccion de semiplanos n.(p - q) <= 0. Las aristas se redondean con log-suma-exp, que
    trata todos los planos a la vez (el maximo suave encadenado deja pliegues en las esquinas)."""

    def __init__(self, planos, lo, hi, k=0.1, redondeo=0.05, resta=False):
        n = np.array([p[0] for p in planos], np.float64)
        self.n = n / np.linalg.norm(n, axis=1, keepdims=True)
        self.d = np.einsum("ij,ij->i", self.n, np.array([p[1] for p in planos], np.float64))
        self.lo, self.hi = np.array(lo, np.float64), np.array(hi, np.float64)
        self.k, self.redondeo, self.resta = k, redondeo, resta

    def caja(self):
        return self.lo, self.hi

    def planos(self, p):
        return p @ self.n.T - self.d

    def distancia(self, p):
        dp = self.planos(p)
        m = dp.max(-1)
        r = self.redondeo * 0.5
        return m + r * np.log(np.exp((dp - m[..., None]) / r).sum(-1))


def Prisma(x0, x1, y0, y1, z0, z1, extra=(), k=0.1, redondeo=0.06, resta=False):
    """Caja alineada con los ejes recortada por planos extra (biseles, morros, colas)."""
    planos = [((1, 0, 0), (x1, 0, 0)), ((-1, 0, 0), (x0, 0, 0)),
              ((0, 1, 0), (0, y1, 0)), ((0, -1, 0), (0, y0, 0)),
              ((0, 0, 1), (0, 0, z1)), ((0, 0, -1), (0, 0, z0))] + list(extra)
    return Poliedro(planos, (x0, y0, z0), (x1, y1, z1), k, redondeo, resta)


class Cilindro:
    """Cilindro de a a b con los cantos redondeados."""

    def __init__(self, a, b, r, redondeo=0.02, k=0.05, resta=False):
        self.a, self.b = np.array(a, np.float64), np.array(b, np.float64)
        ba = self.b - self.a
        self.largo = float(np.linalg.norm(ba))
        self.u = ba / self.largo
        self.c = (self.a + self.b) * 0.5
        self.r = r
        self.rr = min(redondeo, r * 0.95, self.largo * 0.45)
        self.k, self.resta = k, resta

    def caja(self):
        return np.minimum(self.a, self.b) - self.r, np.maximum(self.a, self.b) + self.r

    def distancia(self, p):
        q = p - self.c
        ax = q @ self.u
        rad = np.linalg.norm(q - ax[..., None] * self.u, axis=-1)
        dx = rad - (self.r - self.rr)
        dy = np.abs(ax) - (self.largo * 0.5 - self.rr)
        return np.minimum(np.maximum(dx, dy), 0.0) + np.hypot(np.maximum(dx, 0.0), np.maximum(dy, 0.0)) - self.rr


class Caja:
    """Caja redondeada (centro, semilados), opcionalmente girada."""

    def __init__(self, c, medias, redondeo=0.03, k=0.05, giro=None, resta=False):
        self.c, self.m = np.array(c, np.float64), giro
        self.h = np.array(medias, np.float64)
        self.rr = min(redondeo, float(self.h.min()) * 0.95)
        self.k, self.resta = k, resta

    def caja(self):
        e = float(np.linalg.norm(self.h))
        return self.c - e, self.c + e

    def distancia(self, p):
        q = p - self.c
        if self.m is not None:
            q = q @ self.m
        d = np.abs(q) - (self.h - self.rr)
        return np.linalg.norm(np.maximum(d, 0.0), axis=-1) + np.minimum(d.max(-1), 0.0) - self.rr


# ---------------------------------------------------------------------------- Piezas
def fuselaje():
    P = []
    # Cabina de mando (la parte baja; la cupula va aparte): se estrecha y baja hacia el morro.
    P += [Prisma(-0.56, 0.56, -4.25, 0.1, 0.78, 1.98, [
        ((0, -0.707, 0.707), (0, -3.72, 1.98)),       # el morro cae a 45 grados
        ((0, -0.26, -0.966), (0, -3.15, 0.78)),       # barbilla
        ((0.97, -0.243, 0), (0.56, -3.6, 0)),         # planta afilada
        ((-0.97, -0.243, 0), (-0.56, -3.6, 0)),
        ((0.707, 0, -0.707), (0.56, 0, 1.02)),        # biseles de la panza
        ((-0.707, 0, -0.707), (-0.56, 0, 1.02))], k=0.15, redondeo=0.1)]
    # Cuerpo central (transmision y depositos), que se cierra hacia el botalon.
    P += [Prisma(-0.62, 0.62, -0.4, 2.7, 0.82, 2.3, [
        ((0, 0.6, 0.8), (0, 1.9, 2.3)),
        ((0, 0.5, -0.866), (0, 1.7, 0.82)),
        ((0.9, 0.436, 0), (0.62, 1.5, 0)),
        ((-0.9, 0.436, 0), (-0.62, 1.5, 0)),
        ((0.707, 0, -0.707), (0.62, 0, 1.06)),
        ((-0.707, 0, -0.707), (-0.62, 0, 1.06))], k=0.2, redondeo=0.1)]
    # Gondolas laterales (electronica), con el frente y la cola en cuna.
    for s in (1, -1):
        x0, x1 = sorted((0.3 * s, 1.02 * s))
        P += [Prisma(x0, x1, -2.95, 1.35, 0.86, 1.78, [
            ((0, -0.8, 0.6), (0, -2.6, 1.1)),
            ((0, 0.7, 0.714), (0, 0.95, 1.45)),
            ((0.6 * s, -0.8, 0), (0.85 * s, -2.4, 0)),
            ((0.707 * s, 0, -0.707), (1.02 * s, 0, 1.08)),
            ((0.707 * s, 0, 0.707), (1.02 * s, 0, 1.58))], k=0.12, redondeo=0.08)]
    # Cubierta de motores y base del rotor.
    P += [Prisma(-0.56, 0.56, -0.95, 2.1, 2.1, 2.72, [
        ((0, -0.6, 0.8), (0, -0.55, 2.72)),
        ((0, 0.5, 0.866), (0, 1.55, 2.72)),
        ((0.6, 0, 0.8), (0.56, 0, 2.55)),
        ((-0.6, 0, 0.8), (-0.56, 0, 2.55))], k=0.12, redondeo=0.08),
        Cilindro((0, 0.3, 2.55), (0, 0.3, 3.12), 0.4, 0.14, 0.1),
        Cilindro((0, 0.3, 3.0), (0, 0.3, 3.56), 0.15, 0.03, 0.05)]
    # Turbinas: gondola, pilon, toma de aire con su cono y tobera con supresor de infrarrojos.
    for s in (1, -1):
        x = 0.98 * s
        P += [Cilindro((x, -0.72, 2.3), (x, 1.95, 2.33), 0.4, 0.12, 0.12),
              Caja((0.68 * s, 0.85, 2.22), (0.3, 0.95, 0.16), 0.06, 0.15),
              Cilindro((x, -0.85, 2.3), (x, -0.45, 2.3), 0.3, 0.03, 0.03, resta=True),
              E((x, -0.5, 2.3), (0.13, 0.15, 0.13), 0.02),
              C((x, 1.85, 2.33), (1.27 * s, 2.55, 2.46), 0.33, 0.27, 0.1),
              C((1.22 * s, 2.42, 2.44), (1.36 * s, 2.8, 2.52), 0.19, 0.19, 0.03, resta=True)]
    # Botalon, deriva en flecha, caja del rotor de cola (lado izquierdo) y estabilizador.
    P += [C((0, 2.2, 1.95), (0, 8.9, 2.3), 0.5, 0.2, 0.3),
          Poliedro([((1, 0, 0), (0.085, 0, 0)), ((-1, 0, 0), (-0.085, 0, 0)),
                    ((0, 0, 1), (0, 0, 4.25)), ((0, 0, -1), (0, 0, 2.0)),
                    ((0, -0.894, 0.447), (0, 8.05, 2.1)),
                    ((0, 0.994, -0.113), (0, 9.55, 2.1))],
                   (-0.1, 7.9, 1.95), (0.1, 9.9, 4.3), k=0.2, redondeo=0.06),
          E((0.13, 9.3, 3.85), (0.13, 0.25, 0.22), 0.06),
          C((0, 7.75, 2.1), (0, 8.2, 1.35), 0.2, 0.12, 0.12),                   # carenado de la rueda de cola
          Prisma(-1.65, 1.65, 9.0, 9.7, 2.23, 2.34, [
              ((0.97, 0.243, 0), (1.65, 9.2, 0)), ((-0.97, 0.243, 0), (-1.65, 9.2, 0))], k=0.1, redondeo=0.05)]
    # Alas cortas con pilones, lanzacohetes de 19 tubos (fuera) y lanzamisiles de cuatro (dentro).
    for s in (1, -1):
        x0, x1 = sorted((0.45 * s, 2.72 * s))
        P += [Prisma(x0, x1, -0.15, 1.15, 1.62, 1.8, [((0.2 * s, -0.98, 0), (2.72 * s, -0.05, 0))], k=0.12, redondeo=0.07)]
        for xp in (1.55, 2.35):
            P += [Caja((xp * s, 0.45, 1.46), (0.07, 0.42, 0.17), 0.03, 0.06)]
        xc = 2.35 * s
        P += [Cilindro((xc, -0.78, 1.08), (xc, 1.25, 1.08), 0.31, 0.06, 0.04),
              Cilindro((xc, -0.9, 1.08), (xc, -0.7, 1.08), 0.25, 0.02, 0.02, resta=True)]
        for i in range(7):
            a = math.radians(i * 60)
            dx, dz = (0.15 * math.cos(a), 0.15 * math.sin(a)) if i < 6 else (0.0, 0.0)
            P += [Cilindro((xc + dx, -0.85, 1.08 + dz), (xc + dx, -0.35, 1.08 + dz), 0.05, 0.01, 0.01, resta=True)]
        xm = 1.55 * s
        P += [Caja((xm, 0.3, 1.31), (0.25, 0.68, 0.03), 0.015, 0.03)]
        for dx in (-0.11, 0.11):
            for zm in (1.19, 0.99):
                P += [Cilindro((xm + dx, -0.45, zm), (xm + dx, 1.02, zm), 0.09, 0.02, 0.01),
                      E((xm + dx, -0.47, zm), (0.09, 0.16, 0.09), 0.01)]
    # Sensores del morro (TADS y PNVS) y anillo de la torreta.
    P += [Cilindro((0, -4.35, 0.98), (0, -4.35, 1.52), 0.36, 0.08, 0.1),
          E((0, -4.22, 1.63), (0.17, 0.17, 0.17), 0.05),
          Cilindro((0, -3.55, 0.68), (0, -3.55, 0.9), 0.25, 0.04, 0.06)]
    return P, (-2.95, -5.0, 0.3), (2.95, 10.0, 4.4)


def metal():
    P = []
    for s in (1, -1):
        P += [C((0.5 * s, -1.55, 0.98), (1.12 * s, -1.85, 0.4), 0.09, 0.075, 0.04),      # pata
              C((0.55 * s, -0.95, 0.95), (1.1 * s, -1.82, 0.42), 0.06, 0.05, 0.04),       # tirante
              Cilindro((0.98 * s, -1.85, 0.36), (1.33 * s, -1.85, 0.36), 0.16, 0.03, 0.02)]  # buje
    P += [C((0, 8.2, 1.42), (0, 8.25, 0.3), 0.075, 0.06, 0.04),                           # horquilla de cola
          Cilindro((-0.1, 8.25, 0.25), (0.1, 8.25, 0.25), 0.1, 0.02, 0.02)]
    return P, (-1.6, -2.4, -0.1), (1.6, 8.6, 2.4)


def ruedas():
    P = [Cilindro((-0.065, 8.25, 0.25), (0.065, 8.25, 0.25), 0.25, 0.05, 0.02)]
    for s in (1, -1):
        P += [Cilindro((1.05 * s, -1.85, 0.36), (1.27 * s, -1.85, 0.36), 0.36, 0.08, 0.02)]
    return P, (-1.6, -2.4, -0.2), (1.6, 8.6, 0.8)


def lentes():
    P = [Caja((s * 0.14, -4.69, 1.25), (0.1, 0.04, 0.12), 0.02) for s in (1, -1)]
    P += [Caja((0, -4.37, 1.63), (0.08, 0.03, 0.07), 0.02)]
    return P, (-0.5, -4.9, 0.9), (0.5, -4.2, 1.9)


def canon():
    x, y, z = TORRETA
    P = [E((x, y, z), (0.3, 0.3, 0.27), 0.04),
         Cilindro((x, y - 0.2, z), (x, y - 0.8, z), 0.1, 0.02, 0.05),          # camisa
         Cilindro((x, y - 0.75, z), (x, y - 1.5, z), 0.055, 0.01, 0.02),       # canon de 30 mm
         Cilindro((x, y - 1.42, z), (x, y - 1.62, z), 0.08, 0.015, 0.02),      # bocacha
         Cilindro((x, y - 1.45, z), (x, y - 1.75, z), 0.035, 0.005, 0.01, resta=True)]
    return P, (x - 0.5, y - 1.9, z - 0.5), (x + 0.5, y + 0.5, z + 0.5)


def cupula():
    """Cupula en tandem (artillero delante y abajo, piloto detras y arriba), lados en talud."""
    lado = math.radians(12)
    sx, sz = math.cos(lado), math.sin(lado)
    comun = [((sx, 0, sz), (0.5, 0, 1.9)), ((-sx, 0, sz), (-0.5, 0, 1.9)), ((0, 0, -1), (0, 0, 1.95))]
    delante = comun + [((0, 0, 1), (0, 0, 2.6)),
                       ((0, -0.766, 0.643), (0, -3.62, 1.98)),
                       ((0, 1, 0), (0, -2.1, 0))]
    detras = comun + [((0, 0, 1), (0, 0, 2.92)),
                      ((0, -0.707, 0.707), (0, -2.35, 2.55)),
                      ((0, 0.6, 0.8), (0, -0.85, 2.62)),
                      ((0, -1, 0), (0, -2.6, 0))]
    return [Poliedro(delante, (-0.6, -3.85, 1.75), (0.6, -2.05, 2.65), k=0.04, redondeo=0.05),
            Poliedro(detras, (-0.6, -2.65, 1.75), (0.6, -0.25, 2.97), k=0.04, redondeo=0.05)]


# ---------------------------------------------------------------------------- Mallas
def malla(piezas, lo, hi, voxel):
    """Marching cubes de las piezas en orden (cada resta solo quita lo que hay antes que ella)."""
    d = h.campo(piezas, lo, hi, voxel, ordenar=False)
    return superficie(d, lo, voxel)


def superficie(d, lo, voxel):
    vs, fs, _, _ = measure.marching_cubes(d, level=0.0, spacing=(voxel, voxel, voxel))
    return vs + np.array(lo, np.float64), fs


def rejilla(lo, hi, voxel):
    lo, hi = np.array(lo, np.float64), np.array(hi, np.float64)
    forma = np.ceil((hi - lo) / voxel).astype(int) + 1
    ejes = [lo[i] + np.arange(forma[i]) * voxel for i in range(3)]
    return np.stack(np.meshgrid(*ejes, indexing="ij"), -1)


def cupula_y_marcos(voxel):
    """Cristal (la union de los dos poliedros) y marcos: una banda de chapa a lo largo de cada
    arista (donde el segundo plano mas cercano queda a menos de 'ancho') sobre la superficie."""
    lo, hi = (-0.7, -3.95, 1.7), (0.7, -0.15, 3.05)
    p = rejilla(lo, hi, voxel)
    grosor, ancho = 0.03, 0.055
    cristal = np.full(p.shape[:-1], 1e3)
    marco = np.full(p.shape[:-1], 1e3)
    for pol in cupula():
        dp = pol.planos(p)
        dd = pol.distancia(p)
        segundo = np.partition(dp, -2, axis=-1)[..., -2]
        cristal = k.smin(cristal, dd, 0.03)
        marco = np.minimum(marco, np.maximum(np.abs(dd) - grosor, -(segundo + ancho)))
    return superficie(cristal.astype(np.float32), lo, voxel), superficie(marco.astype(np.float32), lo, voxel)


def unir(*mallas):
    vs, fs, n = [], [], 0
    for v, f in mallas:
        vs.append(v)
        fs.append(f + n)
        n += len(v)
    return np.concatenate(vs), np.concatenate(fs)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--salida", required=True)
    ap.add_argument("--voxel", type=float, default=0.04)
    ap.add_argument("--sin-blender", action="store_true", help="no reducir las mallas")
    a = ap.parse_args()
    os.makedirs(a.salida, exist_ok=True)
    v = a.voxel
    cristal, marcos = cupula_y_marcos(v * 0.5)
    piezas = {
        "fuselaje": (malla(*fuselaje(), v), 0.18),
        "metal": (unir(malla(*metal(), v * 0.75), marcos), 0.3),
        "ruedas": (malla(*ruedas(), v * 0.75), 0.3),
        "cabina": (unir(cristal, malla(*lentes(), v * 0.5)), 0.12),
        "canon": (malla(*canon(), v * 0.5), 0.3),
    }
    rutas = []
    for nombre, ((vs, fs), proporcion) in piezas.items():
        if nombre == "canon":
            vs = vs - np.array(TORRETA)  # el origen en el eje de la torreta
        ruta = os.path.join(a.salida, nombre + ".obj")
        h.guardar(ruta, vs, fs)
        rutas += [str(proporcion), os.path.abspath(ruta)]
    blender = shutil.which("blender") or (BLENDER if os.path.exists(BLENDER) else None)
    if a.sin_blender or blender is None:
        print("[GD] Sin Blender: las mallas quedan sin reducir")
        return
    script = os.path.join(os.path.dirname(os.path.abspath(__file__)), "decimar_obj.py")
    r = subprocess.run([blender, "-b", "--factory-startup", "-P", script, "--"] + rutas, capture_output=True, text=True)
    for linea in r.stdout.splitlines():
        if "[GD]" in linea:
            print(linea)
    if r.returncode != 0:
        print(r.stderr[-2000:])
        raise SystemExit("Blender fallo al reducir las mallas")


if __name__ == "__main__":
    main()
