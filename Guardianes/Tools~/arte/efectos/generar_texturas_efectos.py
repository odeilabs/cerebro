"""Texturas de los efectos de combate (particulas) de la saga Guardianes.

Genera en Assets/_Game/Art/Efectos/Texturas (RGBA, PNG):
  humo.png        4x4 variantes de bocanada de humo/polvo (coliflor con autosombra horneada)
  bola_fuego.png  8x8 fotogramas: bola de fuego que se expande, se enfria y acaba en humo negro
  llamas.png      8x8 fotogramas en bucle: lenguas de fuego que suben
  chispa.png      punto incandescente (nucleo y halo)
  fogonazo.png    2x2 variantes de fogonazo de boca de fusil
  escombro.png    2x2 variantes de esquirlas de roca/madera (recorte por alfa, sombreadas)
  hoja.png        2x2 variantes de hojas de la selva
  anillo.png      anillo de polvo para las ondas expansivas (se ve desde arriba)

Solo numpy/scipy/Pillow (el .venv de AssetsCreator los tiene):
  ..\\AssetsCreator\\AssetsCreatorWin\\.venv\\Scripts\\python.exe Tools\\arte\\efectos\\generar_texturas_efectos.py
"""
import os
import sys

import numpy as np
from PIL import Image
from scipy import ndimage as ndi

AQUI = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(AQUI, "..", "vegetacion"))
import ruido  # noqa: E402

# Carpeta de salida: --proyecto <proyecto Unity> (va a Assets/_Game/Art/Efectos/Texturas) o --salida <carpeta>.
SALIDA = None


def guardar(nombre, rgba):
    os.makedirs(SALIDA, exist_ok=True)
    img = np.clip(rgba * 255.0 + 0.5, 0, 255).astype(np.uint8)
    Image.fromarray(img, "RGBA").save(os.path.join(SALIDA, nombre), optimize=True)
    print(f"  {nombre}: {img.shape[1]}x{img.shape[0]}")


def rejilla(n):
    """Coordenadas de pixel centradas en [-1, 1]."""
    y, x = np.mgrid[0:n, 0:n].astype(np.float32)
    return (x + 0.5) / n * 2 - 1, (y + 0.5) / n * 2 - 1


def suave(a, b, x):
    t = np.clip((x - a) / (b - a), 0, 1)
    return t * t * (3 - 2 * t)


def mosaico(celdas, lado):
    """Junta celdas (lista de HxWx4) en una lamina lado x lado (fila 0 arriba)."""
    n = celdas[0].shape[0]
    hoja = np.zeros((n * lado, n * lado, 4), np.float32)
    for i, c in enumerate(celdas):
        fy, fx = divmod(i, lado)
        hoja[fy * n:(fy + 1) * n, fx * n:(fx + 1) * n] = c
    return hoja


def autosombra(densidad, luz=(-0.55, -0.65), relieve=6.0):
    """Sombreado horneado de una nube: normal a partir de la densidad suavizada, luz arriba-izquierda."""
    h = ndi.gaussian_filter(densidad, densidad.shape[0] / 48)
    gy, gx = np.gradient(h)
    nx, ny, nz = -gx * relieve * densidad.shape[0] / 64, -gy * relieve * densidad.shape[0] / 64, np.ones_like(h)
    largo = np.sqrt(nx * nx + ny * ny + nz * nz)
    lx, ly = luz
    lz = np.sqrt(max(0.0, 1 - lx * lx - ly * ly))
    lambert = (nx * lx + ny * ly + nz * lz) / largo
    # Las zonas densas del centro quedan algo mas oscuras (autosombra del volumen).
    espesor = ndi.gaussian_filter(densidad, densidad.shape[0] / 12)
    return np.clip(0.62 + 0.38 * lambert - 0.25 * espesor, 0.25, 1.0)


# ---------------------------------------------------------------------- humo
def bocanada(n, rng):
    x0, y0 = rejilla(n)
    x, y = x0 * 0.8, y0 * 0.8                            # la bocanada llena la celda
    # Union suave de bolas (coliflor): una grande en el centro y lobulos alrededor.
    dens = np.zeros((n, n), np.float32)
    bolas = [(0.0, 0.04, rng.uniform(0.42, 0.5))]
    for _ in range(rng.integers(6, 10)):
        a = rng.uniform(0, 2 * np.pi)
        dist = rng.uniform(0.22, 0.45)
        bolas.append((np.cos(a) * dist, np.sin(a) * dist * 0.85, rng.uniform(0.2, 0.32)))
    for cx, cy, r in bolas:
        d2 = ((x - cx) ** 2 + (y - cy) ** 2) / (r * r)
        dens = dens + np.exp(-d2 * 1.0)                  # gaussianas: borde blando
    # Deformacion suave y lobulos de la superficie (ruido "billowy": 1 - |ruido|).
    w1 = ruido.espectral((n, n), rng, p=1.8, fmin=2, fmax=16)
    w2 = ruido.espectral((n, n), rng, p=1.8, fmin=2, fmax=16)
    yy, xx = np.mgrid[0:n, 0:n].astype(np.float32)
    dens = ruido.muestrear(dens, yy + w1 * n * 0.03, xx + w2 * n * 0.03)
    grumos = 1 - np.abs(ruido.espectral((n, n), rng, p=1.4, fmin=4, fmax=28)) * 0.7
    fino = ruido.a_01(ruido.espectral((n, n), rng, p=1.0, fmin=10, fmax=n / 3))
    dens = dens * (0.82 + 0.22 * grumos) * (0.9 + 0.14 * fino)
    dens *= suave(1.0, 0.8, np.sqrt(x0 * x0 + y0 * y0))  # nunca toca el borde de la celda
    # Umbral muy blando: solo el nucleo llega a opaco; el resto es un degradado que se funde al
    # solaparse muchas bocanadas (si no, se ven discos recortados).
    dens = dens / max(1e-6, float(np.percentile(dens, 99.7)))
    dens = suave(0.03, 0.85, dens) ** 1.1
    sombra = autosombra(dens, relieve=5.0)
    rgba = np.zeros((n, n, 4), np.float32)
    rgba[..., :3] = sombra[..., None]
    rgba[..., 3] = dens
    return rgba


def humo():
    rng = np.random.default_rng(11)
    guardar("humo.png", mosaico([bocanada(256, rng) for _ in range(16)], 4))


# ---------------------------------------------------------------------- fuego
def cuerpo_negro(t):
    """Color de un fuego segun su temperatura relativa (0 frio .. 1 muy caliente), lineal."""
    t = np.clip(t, 0, 1)
    r = np.clip(t * 3.0, 0, 1)
    g = np.clip(t * 3.0 - 0.9, 0, 1) ** 1.2
    b = np.clip(t * 3.0 - 2.0, 0, 1) ** 1.5 * 0.8
    brillo = t ** 1.6
    return np.stack([r, g * 0.85, b * 0.6], -1) * brillo[..., None]


def bola_fuego():
    rng = np.random.default_rng(21)
    n, fotos = 128, 64
    base = 512
    # Ruido periodico que se "advecta" (sube y se expande) entre fotogramas: bolsas calientes
    # (ruido billowy) y vetas de hollin.
    r1 = ruido.espectral((base, base), rng, p=1.5, fmin=3, fmax=36)
    r2 = ruido.espectral((base, base), rng, p=1.8, fmin=2, fmax=18)
    r3 = ruido.espectral((base, base), rng, p=1.2, fmin=6, fmax=70)
    x, y = rejilla(n)
    celdas = []
    for f in range(fotos):
        t = f / (fotos - 1)
        radio = 0.62 + 0.3 * np.sqrt(t)
        esc = 0.7 / radio
        yy = (y * esc * 0.5 + 0.5) * base * 0.45 + t * base * 0.2
        xx = (x * esc * 0.5 + 0.5) * base * 0.45
        a = ruido.muestrear(r1, yy, xx)
        b = ruido.muestrear(r2, yy * 0.7 + 40, xx * 0.7 + 90)
        c = ruido.muestrear(r3, yy * 1.3 + 11, xx * 1.3 + 7)
        d = np.sqrt(x * x + (y + 0.08 * t) ** 2) / radio
        forma = 1 - d ** 2 + 0.3 * b + 0.1 * a
        dens = suave(0.0, 0.3, forma) * suave(1.0, 0.86, np.sqrt(x * x + y * y))
        # Temperatura: lobulos calientes (1-|ruido|) con pliegues mas frios; el borde y el
        # tiempo enfrian, y el hollin aparece a medida que se apaga.
        bolsas = 1 - np.abs(a) * 0.7
        hollin = np.clip(c * 0.5 + 0.25, 0, 1) * suave(0.25, 0.75, t)
        calor = np.clip(forma, 0, 1) ** 0.6 * (0.55 + 0.6 * bolsas) - hollin * 0.7
        calor = np.clip(calor * (1.35 - 1.2 * t ** 0.75), 0, 1)
        color = cuerpo_negro(calor)
        # Al final queda humo negro que se desvanece.
        alfa = dens * (1 - suave(0.72, 1.0, t)) * (0.85 + 0.15 * np.clip(b + 0.5, 0, 1))
        rgba = np.zeros((n, n, 4), np.float32)
        rgba[..., :3] = color
        rgba[..., 3] = np.clip(alfa, 0, 1)
        celdas.append(rgba)
    guardar("bola_fuego.png", mosaico(celdas, 8))


def llamas():
    rng = np.random.default_rng(31)
    n, fotos = 128, 64
    lado = 256
    # Ruido periodico alargado en vertical (lenguas) y otro suave para el vaiven.
    r1 = ruido.espectral((lado, lado), rng, p=1.4, fmin=2, fmax=30, estiramiento=0.45)
    r2 = ruido.espectral((lado, lado), rng, p=1.8, fmin=1, fmax=10)
    x, y = rejilla(n)
    h = 1 - (y + 1) * 0.5       # 0 abajo .. 1 arriba
    celdas = []
    for f in range(fotos):
        t = f / fotos            # bucle: el ruido sube exactamente un periodo
        yy = ((1 - h) * 0.75 + t) * lado
        xx = (x * 0.5 + 0.5) * lado * 0.55
        # El vaiven tambien recorre un periodo entero (con yy * 0.6 no empalmaba y la llama daba
        # un tiron en cada vuelta; lo cazo el arnes de AssetsCreator).
        vaiven = ruido.muestrear(r2, yy, xx * 0.3 + 50)
        u = x + vaiven * 0.16 * h ** 1.3                         # mas vaiven arriba
        lengua = ruido.muestrear(r1, yy, (u * 0.5 + 0.5) * lado * 0.55)
        # Gota: redonda abajo, se afila hacia arriba.
        ancho = 0.88 * np.clip(1 - h, 0, 1) ** 0.7 * suave(0.0, 0.2, h) + 1e-3
        cuerpo = np.clip(1 - np.abs(u) / ancho, 0, 1)
        forma = cuerpo * 1.4 - np.clip(lengua * 0.5 + 0.5, 0, 1) * (0.25 + 0.95 * h)
        forma *= suave(0.0, 0.06, h) * suave(1.0, 0.9, h)
        dens = suave(0.0, 0.18, forma)
        calor = np.clip(forma * 1.2, 0, 1) * (1.05 - 0.55 * h)
        color = cuerpo_negro(np.clip(calor * 1.15, 0, 1))
        rgba = np.zeros((n, n, 4), np.float32)
        rgba[..., :3] = color
        rgba[..., 3] = dens
        celdas.append(rgba)
    guardar("llamas.png", mosaico(celdas, 8))


# ---------------------------------------------------------------------- chispa, fogonazo
def chispa():
    n = 64
    x, y = rejilla(n)
    d = np.sqrt(x * x + y * y)
    nucleo = np.exp(-(d / 0.18) ** 2)
    halo = np.exp(-(d / 0.5) ** 2) * 0.35
    a = np.clip(nucleo + halo, 0, 1) * suave(1.0, 0.9, d)
    rgba = np.ones((n, n, 4), np.float32)
    rgba[..., 3] = a
    guardar("chispa.png", rgba)


def fogonazo_celda(n, rng):
    x, y = rejilla(n)
    d = np.sqrt(x * x + y * y)
    ang = np.arctan2(y, x)
    puntas = rng.integers(4, 7)
    fase = rng.uniform(0, np.pi)
    estrella = np.abs(np.cos(ang * puntas * 0.5 + fase)) ** 6
    largo = 0.35 + 0.55 * estrella * rng.uniform(0.7, 1.0)
    ruido_a = ruido.a_01(ruido.espectral((n, n), rng, p=1.0, fmin=3, fmax=40))
    forma = np.clip(1 - d / (largo * (0.75 + 0.35 * ruido_a)), 0, 1)
    nucleo = np.exp(-(d / 0.22) ** 2)
    a = np.clip(forma ** 1.4 + nucleo, 0, 1) * suave(1.0, 0.88, d)
    calor = np.clip(nucleo * 1.0 + forma * 0.65, 0, 1)
    color = np.stack([np.ones_like(calor), 0.55 + 0.4 * calor, 0.2 + 0.6 * calor ** 2], -1)
    rgba = np.zeros((n, n, 4), np.float32)
    rgba[..., :3] = color
    rgba[..., 3] = a
    return rgba


def fogonazo():
    rng = np.random.default_rng(41)
    guardar("fogonazo.png", mosaico([fogonazo_celda(128, rng) for _ in range(4)], 2))


# ---------------------------------------------------------------------- escombros, hojas
def esquirla(n, rng, madera):
    """Esquirla facetada: poligono convexo (envolvente de puntos al azar) dividido en caras que
    suben hacia un vertice interior; cada cara recibe la luz segun su normal."""
    from scipy.spatial import ConvexHull
    x, y = rejilla(n)
    if madera:
        # Astilla: alargada y estrecha, con las puntas irregulares.
        pts = np.concatenate([rng.uniform([-0.85, -0.2], [-0.5, 0.2], (4, 2)), rng.uniform([0.5, -0.16], [0.88, 0.16], (4, 2)),
                              rng.uniform([-0.5, -0.24], [0.5, 0.24], (6, 2))])
        giro = rng.uniform(0, np.pi)
        c, s = np.cos(giro), np.sin(giro)
        pts = pts @ np.array([[c, s], [-s, c]])
    else:
        ang = rng.uniform(0, 2 * np.pi, 9)
        rad = rng.uniform(0.5, 0.85, 9)
        pts = np.stack([np.cos(ang) * rad, np.sin(ang) * rad * rng.uniform(0.7, 1.0)], 1)
    casco = ConvexHull(pts)
    poli = pts[casco.vertices]                       # en sentido antihorario
    k = len(poli)
    # Distancia con signo al borde (positiva dentro): minimo de los semiplanos.
    dentro = np.full(x.shape, 9.0, np.float32)
    for i in range(k):
        p0, p1 = poli[i], poli[(i + 1) % k]
        e = p1 - p0
        nrm = np.array([e[1], -e[0]]) / np.linalg.norm(e)   # normal hacia fuera (CCW)
        dentro = np.minimum(dentro, -((x - p0[0]) * nrm[0] + (y - p0[1]) * nrm[1]))
    alfa = np.clip(dentro * n * 0.5, 0, 1)
    # Caras: abanico desde un vertice interior elevado.
    pico = np.array([*(poli.mean(0) + rng.normal(0, 0.08, 2)), rng.uniform(0.25, 0.5)])
    luz_dir = np.array([-0.5, -0.6, 0.62])
    luz_dir /= np.linalg.norm(luz_dir)
    luz = np.full(x.shape, 0.7, np.float32)
    ang_p = np.arctan2(y - pico[1], x - pico[0])
    angs = np.arctan2(poli[:, 1] - pico[1], poli[:, 0] - pico[0])
    for i in range(k):
        a0, a1 = angs[i], angs[(i + 1) % k]
        dentro_cara = np.mod(ang_p - a0, 2 * np.pi) <= np.mod(a1 - a0, 2 * np.pi)
        v0 = np.array([*poli[i], 0.0]) - pico
        v1 = np.array([*poli[(i + 1) % k], 0.0]) - pico
        nrm = np.cross(v0, v1)
        nrm = nrm / np.linalg.norm(nrm) * (1 if nrm[2] > 0 else -1)
        luz = np.where(dentro_cara, 0.35 + 0.75 * max(0.0, float(nrm @ luz_dir)), luz)
    r = ruido.espectral((n, n), rng, p=1.0, fmin=6, fmax=n / 2)
    if madera:
        vetas = 0.5 + 0.5 * np.sin((x * 0.3 + y * 1.0 + 0.04 * r) * 40)
        base = np.array([0.45, 0.32, 0.19])[None, None, :] * (0.7 + 0.3 * vetas[..., None])
    else:
        tono = rng.uniform(0.8, 1.05)
        base = np.array([0.38, 0.36, 0.33])[None, None, :] * tono * (0.85 + 0.15 * r[..., None])
    luz = luz * (1 - 0.25 * suave(0.04, 0.0, dentro))    # filo algo oscuro
    rgba = np.zeros((n, n, 4), np.float32)
    rgba[..., :3] = np.clip(base * luz[..., None], 0, 1)
    rgba[..., 3] = alfa
    return rgba


def escombro():
    rng = np.random.default_rng(51)
    guardar("escombro.png", mosaico([esquirla(128, rng, i % 2 == 1) for i in range(4)], 2))


def hoja_celda(n, rng):
    x, y = rejilla(n)
    giro = rng.uniform(-0.6, 0.6)
    c, s = np.cos(giro), np.sin(giro)
    u, v = x * c - y * s, x * s + y * c
    largo = rng.uniform(0.75, 0.92)
    ancho = rng.uniform(0.28, 0.42)
    t = (v / largo + 1) * 0.5                       # 0 punta .. 1 base
    perfil = ancho * np.sin(np.clip(t, 0, 1) * np.pi) ** 0.8
    borde = perfil - np.abs(u)
    alfa = np.clip(borde * n * 0.5, 0, 1) * (np.abs(v) < largo)
    rabito = (np.abs(u) < 0.015) & (v > largo * 0.95) & (v < largo * 1.08)
    alfa = np.maximum(alfa, rabito.astype(np.float32))
    nervio = np.exp(-(u / 0.02) ** 2) * 0.25
    secund = (0.5 + 0.5 * np.cos((v * 9 + np.abs(u) * 14) * np.pi)) * 0.08
    verde = np.array([0.16, 0.3, 0.07]) * rng.uniform(0.75, 1.2)
    if rng.random() < 0.3:
        verde = np.array([0.36, 0.33, 0.1])          # alguna hoja seca
    luz = 0.8 + 0.25 * np.clip(-u / max(ancho, 1e-3), -1, 1) + nervio + secund
    rgba = np.zeros((n, n, 4), np.float32)
    rgba[..., :3] = np.clip(verde[None, None, :] * luz[..., None], 0, 1)
    rgba[..., 3] = np.clip(alfa, 0, 1)
    return rgba


def hoja():
    rng = np.random.default_rng(61)
    guardar("hoja.png", mosaico([hoja_celda(128, rng) for _ in range(4)], 2))


# ---------------------------------------------------------------------- anillo
def anillo():
    rng = np.random.default_rng(71)
    n = 512
    x, y = rejilla(n)
    d = np.sqrt(x * x + y * y)
    ang = np.arctan2(y, x)
    r = ruido.espectral((n, n), rng, p=1.2, fmin=3, fmax=60)
    # Perfil: sube rapido por dentro, cola larga por fuera; roto por el ruido.
    perfil = suave(0.45, 0.78, d + 0.05 * r) * suave(0.98, 0.8, d + 0.04 * r)
    grumos = ruido.a_01(ruido.espectral((n, n), rng, p=1.0, fmin=6, fmax=90))
    a = np.clip(perfil * (0.45 + 0.8 * grumos), 0, 1)
    luz = np.clip(0.75 + 0.2 * np.cos(ang + 2.2) + 0.1 * r, 0.4, 1)
    rgba = np.zeros((n, n, 4), np.float32)
    rgba[..., :3] = luz[..., None]
    rgba[..., 3] = a
    guardar("anillo.png", rgba)


def cascada():
    """Espuma de la cascada: chorros verticales (ruido muy estirado), periodica en los dos ejes
    para correr hacia abajo sin costura. RGB casi blanco con algo de verde agua en lo fino."""
    rng = np.random.default_rng(81)
    n = 512
    chorros = ruido.espectral((n, n), rng, p=1.3, fmin=3, fmax=90, estiramiento=8.0)
    grumos = ruido.espectral((n, n), rng, p=1.6, fmin=4, fmax=60)
    fino = ruido.espectral((n, n), rng, p=1.0, fmin=20, fmax=200, estiramiento=4.0)
    d = ruido.a_01(chorros * 0.75 + grumos * 0.35 + fino * 0.25)
    alfa = suave(0.28, 0.85, d)
    rgba = np.zeros((n, n, 4), np.float32)
    blanco = 0.82 + 0.18 * d
    rgba[..., 0] = blanco * 0.93
    rgba[..., 1] = blanco * 0.98
    rgba[..., 2] = blanco
    rgba[..., 3] = alfa
    guardar("cascada_espuma.png", rgba)


if __name__ == "__main__":
    import argparse
    ap = argparse.ArgumentParser(description="Laminas de particulas de combate")
    ap.add_argument("--proyecto", help="carpeta del proyecto Unity")
    ap.add_argument("--salida", help="carpeta de salida (manda sobre --proyecto)")
    ap.add_argument("--solo-cascada", action="store_true")
    a = ap.parse_args()
    if a.salida:
        SALIDA = a.salida
    elif a.proyecto:
        SALIDA = os.path.join(a.proyecto, "Assets", "_Game", "Art", "Efectos", "Texturas")
    else:
        sys.exit("Falta --proyecto <carpeta del proyecto Unity> o --salida <carpeta>")
    print("Texturas de efectos ->", SALIDA)
    if a.solo_cascada:
        cascada()
        sys.exit(0)
    cascada()
    humo()
    bola_fuego()
    llamas()
    chispa()
    fogonazo()
    escombro()
    hoja()
    anillo()
