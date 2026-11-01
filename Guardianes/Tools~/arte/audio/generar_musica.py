"""Musica de la saga Guardianes sintetizada (numpy + scipy), sin muestras de terceros.

    python generar_musica.py --proyecto "<proyecto Unity>" [--solo exploracion,combate,jefe] [--tono D]

Escribe en Assets/_Game/Art/Audio/musica/ tres bucles estereo de 48 kHz y 16 bits que se repiten sin costura (la cola
de cada uno, la reverberacion incluida, se suma al principio):
  exploracion_0  72 ppm, 16 compases: bordon grave y acordes lentos (i - VII - IV - i, modo dorico), marimba en
                 arpegios sueltos, una flauta que respira cada pocos compases y un tambor de tronco suave.
  combate_0      112 ppm, 32 compases: tambores grandes de piel (un taiko), palos y maraca, un bajo en ostinato,
                 golpes de metales cada dos compases y una llamada de trompa en la segunda mitad de cada frase.
  jefe_0         132 ppm, 32 compases: los tambores al doble, metales disonantes (frigio: la segunda menor), un
                 coro en "a" y el bajo empujando en corcheas.
Lo usa MusicaJuego (Runtime/Audio), que funde de uno a otro segun lo que pase en el juego. Nada de tonos puros
agudos sostenidos: la flauta lleva aliento y vibrato, y lo agudo es percusivo. Todo es determinista (semillas fijas).
"""
import argparse
import os
import sys

import numpy as np

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import generar_sonidos as gs  # noqa: E402  (las utilidades: filtros, reverb, normalizar, guardar)
from generar_sonidos import SR, banda, paso_bajo, paso_alto, resonador, saturar, reverb, normalizar  # noqa: E402

NOTAS = {"C": -9, "C#": -8, "D": -7, "D#": -6, "E": -5, "F": -4, "F#": -3, "G": -2, "G#": -1, "A": 0, "A#": 1, "B": 2}


def hz(semitonos_desde_la4):
    return 440.0 * 2 ** (semitonos_desde_la4 / 12.0)


class Pista:
    """Una mezcla estereo de 'dur' segundos con margen para las colas; poner() suma un sonido en un instante y paneo."""

    def __init__(self, dur, cola=4.0):
        self.n = int(dur * SR)
        self.L = np.zeros(self.n + int(cola * SR))
        self.R = np.zeros_like(self.L)

    def poner(self, x, t, pan=0.0, vol=1.0):
        i = int(t * SR)
        if i >= len(self.L):
            return
        k = min(len(x), len(self.L) - i)
        a = (pan + 1) * np.pi / 4
        self.L[i:i + k] += x[:k] * np.cos(a) * vol
        self.R[i:i + k] += x[:k] * np.sin(a) * vol

    def bucle(self, rng, t60=2.0, mezcla=0.25):
        """Con reverberacion y la cola sumada al principio: un bucle sin costura de exactamente dur segundos."""
        canales = []
        for c in (self.L, self.R):
            r = reverb(c, rng, t60=t60, mezcla=mezcla)
            y = r[:self.n].copy()
            resto = r[self.n:]
            k = min(len(resto), self.n)
            y[:k] += resto[:k]
            canales.append(y)
        return np.stack(canales, -1)


# ---------------------------------------------------------------------------- Instrumentos
def tambor(rng, f0, dur=0.9, piel=1.0):
    """Tambor grande: el tono que cae al golpear, el cuerpo y el chasquido de la piel."""
    n = int(dur * SR)
    t = np.arange(n) / SR
    f = f0 * (1 + 1.4 * np.exp(-t / 0.025))
    x = np.sin(2 * np.pi * np.cumsum(f) / SR) * np.exp(-t / (dur * 0.35))
    x += 0.5 * np.sin(2 * np.pi * np.cumsum(f * 1.58) / SR) * np.exp(-t / (dur * 0.15))
    golpe = banda(rng.standard_normal(n), 200, 2500) * np.exp(-t / 0.012) * 0.6 * piel
    return saturar(x + golpe, 1.3) * 0.9


def palo(rng, f=750.0):
    """Tambor de tronco o claves: un resonador de madera, corto."""
    n = int(0.25 * SR)
    t = np.arange(n) / SR
    x = resonador(rng.standard_normal(n) * np.exp(-t / 0.004), f, 18) * np.exp(-t / 0.07)
    return x / (np.max(np.abs(x)) + 1e-9) * 0.6


def maraca(rng):
    n = int(0.09 * SR)
    t = np.arange(n) / SR
    return banda(rng.standard_normal(n), 4500, 11000) * np.exp(-t / 0.025) * 0.25


def marimba(f, dur=1.2):
    """Barra de madera golpeada: la fundamental, el cuarto armonico y el decimo, que decaen deprisa."""
    n = int(dur * SR)
    t = np.arange(n) / SR
    x = np.sin(2 * np.pi * f * t) * np.exp(-t / 0.45)
    x += 0.35 * np.sin(2 * np.pi * 3.93 * f * t) * np.exp(-t / 0.12)
    x += 0.08 * np.sin(2 * np.pi * 9.8 * f * t) * np.exp(-t / 0.03)
    ataque = np.minimum(1, t / 0.002)
    return x * ataque * 0.5


def flauta(rng, f, dur):
    """Flauta de madera: el tono con un poco de segundo armonico, vibrato que entra tarde y aliento filtrado."""
    n = int(dur * SR)
    t = np.arange(n) / SR
    vib = 1 + 0.006 * np.sin(2 * np.pi * 5.2 * t) * np.clip((t - 0.35) / 0.4, 0, 1)
    fase = 2 * np.pi * np.cumsum(f * vib) / SR
    x = np.sin(fase) + 0.18 * np.sin(2 * fase) + 0.05 * np.sin(3 * fase)
    aliento = banda(rng.standard_normal(n), f * 0.9, f * 3.5) * 0.12
    env = np.minimum(1, t / 0.18) * np.minimum(1, (dur - t) / 0.35)
    return paso_bajo((x + aliento) * np.clip(env, 0, 1), 4500, 2) * 0.35


def sierra(f, n, desafinar=0.0, fase0=0.0):
    t = np.arange(n) / SR
    ph = (f * (1 + desafinar) * t + fase0) % 1.0
    return 2 * ph - 1


def pad(fs, dur, corte=900.0, ataque=1.2):
    """Acorde lento de sierras desafinadas, oscuro (paso bajo)."""
    n = int(dur * SR)
    t = np.arange(n) / SR
    x = np.zeros(n)
    for i, f in enumerate(fs):
        for d in (-0.004, 0.0, 0.005):
            x += sierra(f, n, d, 0.13 * i + d * 10)
    x = paso_bajo(x / (len(fs) * 3), corte, 2)
    env = np.minimum(1, t / ataque) * np.minimum(1, (dur - t) / 1.0)
    return x * np.clip(env, 0, 1) * 0.5


def coro(fs, dur):
    """Voces en "a": sierras por los formantes de la vocal (700, 1150, 2600 Hz), entrada lenta."""
    n = int(dur * SR)
    t = np.arange(n) / SR
    x = np.zeros(n)
    for f in fs:
        for d in (-0.006, 0.0, 0.007):
            x += sierra(f * (1 + 0.003 * np.sin(2 * np.pi * 4.8 * t[0])), n, d)
    x /= len(fs) * 3
    v = resonador(x, 700, 6) + 0.6 * resonador(x, 1150, 7) + 0.25 * resonador(x, 2600, 8)
    env = np.minimum(1, t / 1.5) * np.minimum(1, (dur - t) / 1.2)
    return paso_bajo(v, 3500, 2) * np.clip(env, 0, 1) * 0.6


def metal(fs, dur, brillo=2600.0):
    """Metales: sierras con el filtro que se abre al soplar (el "bwaa"), un poco saturadas."""
    n = int(dur * SR)
    t = np.arange(n) / SR
    x = np.zeros(n)
    for f in fs:
        x += sierra(f, n, 0.002) + sierra(f, n, -0.003)
    x /= len(fs) * 2
    corte = 300 + brillo * np.minimum(1, t / 0.12) * (0.7 + 0.3 * np.exp(-t / 0.6))
    # Filtro que se mueve: por tramos de 10 ms.
    y = np.zeros(n)
    paso = int(0.01 * SR)
    for i in range(0, n, paso):
        y[i:i + paso] = paso_bajo(x[max(0, i - 4 * paso):i + paso], float(corte[i]), 2)[-len(x[i:i + paso]):]
    env = np.minimum(1, t / 0.05) * np.minimum(1, (dur - t) / 0.25)
    return saturar(y * np.clip(env, 0, 1) * 1.2, 1.5) * 0.45


def bajo(f, dur):
    n = int(dur * SR)
    t = np.arange(n) / SR
    x = np.sin(2 * np.pi * f * t) + 0.4 * paso_bajo(sierra(f, n), 400, 2)
    env = np.minimum(1, t / 0.008) * np.exp(-t / max(0.15, dur * 0.8))
    return saturar(x * env, 1.4) * 0.5


# ---------------------------------------------------------------------------- Piezas
def escala_dorica(tonica):
    pasos = [0, 2, 3, 5, 7, 9, 10]
    return [tonica + p for p in pasos]


def exploracion(rng, tonica):
    ppm, compases = 72, 16
    negra = 60.0 / ppm
    dur = compases * 4 * negra
    p = Pista(dur)
    # i - VII - IV - i (dorico: el IV mayor), cuatro compases cada uno.
    acordes = [(0, 3, 7, 10), (-2, 2, 5, 9), (5, 9, 12, 15), (0, 3, 7, 10)]
    for k, ac in enumerate(acordes):
        t0 = k * 4 * 4 * negra
        fs = [hz(tonica - 24 + s) for s in ac]
        p.poner(pad(fs, 4 * 4 * negra + 1.5, corte=700, ataque=2.0), t0, 0.0, 0.55)
        p.poner(pad([hz(tonica - 36 + ac[0])], 4 * 4 * negra + 1.5, corte=250, ataque=1.5), t0, 0.0, 0.5)
        # Marimba: arpegios en corcheas, sueltos.
        for c in range(4):
            for e in range(8):
                if rng.random() < 0.55:
                    s = ac[rng.integers(0, len(ac))] + 12 * rng.integers(0, 2)
                    p.poner(marimba(hz(tonica - 12 + s)), t0 + (c * 4 + e * 0.5) * negra, rng.uniform(-0.5, 0.5), 0.5)
        # Tambor de tronco: en el 1 y el "3 y".
        for c in range(4):
            p.poner(palo(rng, 480), t0 + c * 4 * negra, -0.3, 0.45)
            p.poner(palo(rng, 620), t0 + (c * 4 + 2.5) * negra, 0.3, 0.3)
    # La flauta: frases de notas largas cada ocho compases.
    escala = escala_dorica(tonica)
    for frase in range(2):
        t = (frase * 8 + 1) * 4 * negra
        for _ in range(6):
            d = negra * rng.choice([1.5, 2.0, 3.0])
            p.poner(flauta(rng, hz(escala[rng.integers(0, 7)] + (12 if rng.random() < 0.3 else 0)), d * 1.1), t, 0.2, 0.55)
            t += d
    return normalizar(p.bucle(rng, t60=2.6, mezcla=0.35), -9.0)


def combate(rng, tonica, ppm=112, jefe=False):
    compases = 32
    negra = 60.0 / ppm
    dur = compases * 4 * negra
    p = Pista(dur)
    # Armonia: i - VI - VII - V (menor) o, el jefe, i - bII - i - V (frigio: la segunda menor, que inquieta).
    acordes = [(0, 3, 7), (1, 5, 8), (0, 3, 7), (-5, -1, 2)] if jefe else [(0, 3, 7), (-4, 0, 3), (-2, 2, 5), (-5, -1, 2)]
    ostinato = [0, 0, 3, 0, -2, 0, -5, -2]
    for c in range(compases):
        t0 = c * 4 * negra
        ac = acordes[(c // 2) % 4]
        # Tambores grandes: 1, "1 y", 3, "4 y" (el jefe, ademas, el 2 y el 4) y redoble cada cuatro compases.
        golpes = [0, 0.5, 2, 3.5] + ([1, 3] if jefe else [])
        for g in golpes:
            p.poner(tambor(rng, rng.uniform(58, 66), 0.9), t0 + g * negra, rng.uniform(-0.2, 0.2), 0.9)
        if c % 4 == 3:
            for i in range(8 if jefe else 6):
                p.poner(tambor(rng, 95 + 12 * (i % 3), 0.35, 0.7), t0 + (2 + i * 0.25) * negra, -0.4 + 0.1 * i, 0.55)
        # Palos en corcheas, maraca en semicorcheas.
        for e in range(8):
            p.poner(palo(rng, 820 if e % 2 else 700), t0 + e * 0.5 * negra, 0.45, 0.35)
        for s in range(16):
            p.poner(maraca(rng), t0 + s * 0.25 * negra, -0.5, 0.5 if s % 4 == 0 else 0.3)
        # Bajo en ostinato (corcheas).
        for e, s in enumerate(ostinato):
            p.poner(bajo(hz(tonica - 36 + ac[0] + s * (1 if e % 2 else 0) + (s if e % 4 == 2 else 0)), negra * 0.45), t0 + e * 0.5 * negra, 0.0, 0.8)
        # Metales: un golpe al empezar cada par de compases.
        if c % 2 == 0:
            fs = [hz(tonica - 24 + s) for s in ac] + ([hz(tonica - 24 + ac[0] + 1)] if jefe else [])
            p.poner(metal(fs, negra * 1.6), t0, 0.1, 0.8)
        # Fondo: el acorde (el jefe, con coro).
        if c % 2 == 0:
            fs = [hz(tonica - 12 + s) for s in ac]
            p.poner(coro(fs, 8 * negra + 1.0) if jefe else pad(fs, 8 * negra + 1.0, corte=1100, ataque=0.6), t0, -0.1, 0.45)
    # La llamada de la trompa en la segunda mitad de cada frase de ocho compases.
    escala = [tonica + s for s in ([0, 1, 3, 5, 7, 8, 10] if jefe else [0, 2, 3, 5, 7, 8, 10])]
    for frase in range(compases // 8):
        t = (frase * 8 + 4) * 4 * negra
        for _ in range(5):
            d = negra * rng.choice([1.0, 1.5, 2.0])
            f = hz(escala[rng.integers(0, 5)] - 12)
            p.poner(metal([f, f * 1.5], d * 0.95, brillo=3200), t, -0.25, 0.7)
            t += d
    return normalizar(p.bucle(rng, t60=1.8, mezcla=0.22), -6.5)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--solo", default="", help="exploracion,combate,jefe")
    ap.add_argument("--proyecto", help="carpeta del proyecto Unity")
    ap.add_argument("--salida", help="carpeta de salida (manda sobre --proyecto)")
    ap.add_argument("--tono", default="D", help="la tonica (D de serie)")
    a = ap.parse_args()
    if a.salida:
        gs.SALIDA = a.salida
    elif a.proyecto:
        gs.SALIDA = os.path.join(a.proyecto, "Assets", "_Game", "Art", "Audio")
    else:
        sys.exit("Falta --proyecto <carpeta del proyecto Unity> o --salida <carpeta>")
    tonica = NOTAS[a.tono]
    solo = set(s for s in a.solo.split(",") if s)

    def quiero(n):
        return not solo or n in solo

    print("Musica ->", gs.SALIDA)
    if quiero("exploracion"):
        gs.guardar("musica", "exploracion_0", exploracion(np.random.default_rng(3000), tonica))
    if quiero("combate"):
        gs.guardar("musica", "combate_0", combate(np.random.default_rng(3100), tonica, 112, False))
    if quiero("jefe"):
        gs.guardar("musica", "jefe_0", combate(np.random.default_rng(3200), tonica, 132, True))


if __name__ == "__main__":
    main()
