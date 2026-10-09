"""Sonidos de la saga Guardianes sintetizados (numpy + scipy), sin muestras de terceros (nacieron en King Kong).

    python Tools/arte/audio/generar_sonidos.py [--solo rugido,disparo,...]

Escribe WAV de 48 kHz y 16 bits en Assets/_Game/Art/Audio/<grupo>/<nombre>_<n>.wav:
  kong/      rugido (voz de fuente-filtro: pulsos glotales graves con subarmonico y "fry",
             formantes de un tracto vocal enorme, aliento, saturacion), golpes de pecho, pisadas,
             golpe (punetazo que impacta), aspaviento (el brazo cortando el aire)
  serpiente/ siseo (aire por la glotis: ruido con formantes y el silbido de la boca), reptar (las
             escamas sobre la hojarasca), mordisco (el golpe del ataque y la mandibula que se cierra)
             (con --criatura serpiente --solo siseo,reptar,mordisco)
  armas/     disparo de AK (chasquido, cuerpo y eco del bosque)
  destruccion/ explosion, madera (crujidos y astillas), metal (chapas que resuenan), arbol (crujido,
             hojas y golpe contra el suelo)
  ambiente/  selva (bucle estereo de 60 s: lecho de hojas, cigarras, grillos, pajaros, aullidos
             lejanos), rio (bucle), fuego (bucle)
Todo es determinista (semillas fijas). Cada sonido sale en varias variantes para no repetir.
"""
import argparse
import sys
import os
import wave

import numpy as np
from scipy import signal

SR = 48000
AQUI = os.path.dirname(os.path.abspath(__file__))
# Carpeta de salida: --proyecto <proyecto Unity> (va a Assets/_Game/Art/Audio) o --salida <carpeta>.
SALIDA = None


# ---------------------------------------------------------------------------- Utilidades
def t_(seg):
    return np.arange(int(seg * SR)) / SR


def blanco(rng, n):
    return rng.standard_normal(n)


def color(rng, n, pendiente):
    """Ruido 1/f^pendiente (1 rosa, 2 marron) por FFT."""
    x = rng.standard_normal(n)
    X = np.fft.rfft(x)
    f = np.fft.rfftfreq(n, 1 / SR)
    f[0] = f[1]
    X *= f ** (-pendiente / 2)
    y = np.fft.irfft(X, n)
    return y / (np.std(y) + 1e-12)


def banda(x, f1, f2, orden=4):
    nyq = SR / 2
    f1 = max(1.0, f1)
    f2 = min(nyq * 0.99, f2)
    sos = signal.butter(orden, [f1 / nyq, f2 / nyq], btype="band", output="sos")
    return signal.sosfilt(sos, x)


def paso_bajo(x, fc, orden=4):
    sos = signal.butter(orden, min(fc / (SR / 2), 0.99), btype="low", output="sos")
    return signal.sosfilt(sos, x)


def paso_alto(x, fc, orden=4):
    sos = signal.butter(orden, max(fc / (SR / 2), 1e-4), btype="high", output="sos")
    return signal.sosfilt(sos, x)


def resonador(x, f, q):
    """Filtro resonante (pico) de segundo orden."""
    b, a = signal.iirpeak(f / (SR / 2), q)
    return signal.lfilter(b, a, x)


def decae(n, tau):
    return np.exp(-np.arange(n) / (tau * SR))


def envolvente(n, ataque, caida, sostener=0.0):
    """Ataque lineal, meseta y caida exponencial (segundos)."""
    e = np.ones(n)
    a = max(1, int(ataque * SR))
    e[:a] = np.linspace(0, 1, a) ** 1.5
    s = a + int(sostener * SR)
    if s < n:
        e[s:] = np.exp(-np.arange(n - s) / (caida * SR))
    return e


def saturar(x, k):
    return np.tanh(k * x) / np.tanh(k)


def reverb(x, rng, t60=1.6, mezcla=0.3, estereo=False, brillo=4000.0):
    """Reverberacion por convolucion con ruido que decae (y reflexiones tempranas)."""
    n = int(t60 * SR)
    canales = []
    for c in range(2 if estereo else 1):
        ir = rng.standard_normal(n) * np.exp(-6.9 * np.arange(n) / n)
        ir = paso_bajo(ir, brillo, 2)
        for _ in range(6):
            d = rng.integers(int(0.008 * SR), int(0.07 * SR))
            ir[d] += rng.uniform(0.3, 0.7) * (1 if rng.random() < 0.5 else -1) * 8
        ir /= np.sqrt(np.sum(ir ** 2)) + 1e-12
        h = signal.fftconvolve(x, ir)
        h = np.concatenate([h, np.zeros(len(x) + n - len(h))])[: len(x) + n]
        seco = np.concatenate([x, np.zeros(n)])
        canales.append(seco * (1 - mezcla) + h * mezcla * 1.6)
    return np.stack(canales, -1) if estereo else canales[0]


def normalizar(x, pico_db=-1.0):
    m = np.max(np.abs(x)) + 1e-12
    return x / m * 10 ** (pico_db / 20)


def recortar_cola(x, umbral_db=-60.0):
    """Quita el silencio del final (deja 50 ms)."""
    a = np.abs(x if x.ndim == 1 else x.max(-1))
    u = np.max(a) * 10 ** (umbral_db / 20)
    i = np.nonzero(a > u)[0]
    fin = (i[-1] if len(i) else len(x) - 1) + int(0.05 * SR)
    y = x[:fin]
    rampa = min(len(y), int(0.03 * SR))
    y[-rampa:] *= np.linspace(1, 0, rampa)[:, None] if y.ndim == 2 else np.linspace(1, 0, rampa)
    return y


def guardar(grupo, nombre, x):
    carpeta = os.path.join(SALIDA, grupo)
    os.makedirs(carpeta, exist_ok=True)
    x = np.clip(x, -1, 1)
    datos = (x * 32767).astype("<i2")
    with wave.open(os.path.join(carpeta, nombre + ".wav"), "wb") as w:
        w.setnchannels(1 if x.ndim == 1 else x.shape[1])
        w.setsampwidth(2)
        w.setframerate(SR)
        w.writeframes(datos.tobytes())
    print(f"  {grupo}/{nombre}.wav  {len(x) / SR:.2f} s")


# ---------------------------------------------------------------------------- Kong
def rugido(rng, dur):
    n = int(dur * SR)
    t = np.arange(n) / SR
    u = t / dur
    # Tono: sube al empezar, se mantiene y cae al final; con temblor (jitter) y vibrato lento.
    f0 = 58 + 34 * np.sin(np.pi * np.clip(u * 1.25, 0, 1)) ** 0.7 - 14 * np.clip((u - 0.7) / 0.3, 0, 1)
    jitter = np.cumsum(rng.standard_normal(n)) / np.sqrt(SR) * 0.9
    jitter = paso_bajo(jitter, 12, 2)
    f0 = f0 * (1 + 0.035 * jitter / (np.std(jitter) + 1e-9)) * (1 + 0.012 * np.sin(2 * np.pi * 5.5 * t))
    fase = 2 * np.pi * np.cumsum(f0) / SR
    # Pulsos glotales (suma de armonicos con caida espectral) + subarmonico y "fry" que rasca.
    fuente = np.zeros(n)
    for k in range(1, 60):
        amp = k ** -1.15
        fuente += amp * np.sin(k * fase) * (k * f0 < 6000)
    fuente += 0.55 * np.sin(fase / 2) + 0.25 * np.sin(fase / 2 * 3)
    fry = 0.55 + 0.45 * np.sign(np.sin(2 * np.pi * np.cumsum(34 + 8 * jitter) / SR))
    fuente *= paso_bajo(fry, 300, 2) * 0.6 + 0.4
    aliento = color(rng, n, 1.0) * 0.45
    voz = fuente / np.std(fuente) + aliento
    # Formantes de un tracto vocal gigante ("aaa" grave) que se abren con el rugido.
    salida = np.zeros(n)
    for f, q, g in ((330, 3.5, 1.0), (620, 4.0, 0.8), (1150, 5.0, 0.5), (2300, 6.0, 0.28), (3400, 7.0, 0.14)):
        salida += g * resonador(voz, f * (0.92 + 0.12 * rng.random()), q)
    salida += 0.25 * paso_bajo(voz, 180, 2)
    env = envolvente(n, 0.28, 0.35, sostener=dur * 0.55) * (0.85 + 0.15 * np.sin(2 * np.pi * 3.1 * t + 1.3))
    salida = saturar(salida / (np.std(salida) + 1e-9) * 0.45 * env, 2.2)
    # Retumbo del pecho.
    salida += 0.5 * np.sin(2 * np.pi * np.cumsum(f0 * 0.5) / SR) * env
    return recortar_cola(normalizar(reverb(salida, rng, t60=2.2, mezcla=0.3), -1.0))


def golpes_pecho(rng):
    golpes = rng.integers(5, 8)
    n = int(2.4 * SR)
    x = np.zeros(n)
    pos = 0.05
    for i in range(golpes):
        i0 = int(pos * SR)
        m = int(0.5 * SR)
        tt = np.arange(m) / SR
        cuerpo = sum(a * np.sin(2 * np.pi * f * tt) for f, a in ((72, 1.0), (115, 0.6), (190, 0.3)))
        cuerpo *= np.exp(-tt / 0.13)
        palmada = banda(blanco(rng, m), 300, 1800) * np.exp(-tt / 0.018) * 0.9
        g = (cuerpo + palmada) * (0.7 + 0.3 * (i % 2)) * rng.uniform(0.85, 1.1)
        fin = min(n, i0 + m)
        x[i0:fin] += g[: fin - i0]
        pos += rng.uniform(0.12, 0.17)
    x = saturar(x / np.max(np.abs(x)) * 1.2, 1.5)
    return recortar_cola(normalizar(reverb(x, rng, t60=1.4, mezcla=0.25), -1.0))


def pisada(rng, hojas):
    m = int(1.2 * SR)
    tt = np.arange(m) / SR
    f = 55 * np.exp(-tt / 0.25) + 26
    golpe = np.sin(2 * np.pi * np.cumsum(f) / SR) * np.exp(-tt / 0.32)
    tierra = paso_bajo(color(rng, m, 1.5), 900) * np.exp(-tt / 0.12) * 0.8
    x = golpe + tierra
    if hojas:
        cruje = banda(blanco(rng, m), 1500, 7000) * np.exp(-tt / 0.2) * 0.25
        chasquidos = np.zeros(m)
        for _ in range(rng.integers(6, 14)):
            i = rng.integers(0, int(0.25 * SR))
            chasquidos[i:i + 200] += rng.uniform(-1, 1) * np.exp(-np.arange(min(200, m - i)) / 40)
        x += cruje + banda(chasquidos, 800, 5000) * 0.6
    return recortar_cola(normalizar(reverb(x, rng, t60=1.0, mezcla=0.18), -2.0))


def punetazo(rng):
    m = int(1.0 * SR)
    tt = np.arange(m) / SR
    golpe = np.sin(2 * np.pi * np.cumsum(90 * np.exp(-tt / 0.1) + 40) / SR) * np.exp(-tt / 0.2)
    cruje = banda(blanco(rng, m), 200, 2500) * np.exp(-tt / 0.06) * 0.9
    x = saturar(golpe + cruje, 1.8)
    return recortar_cola(normalizar(reverb(x, rng, t60=1.1, mezcla=0.2), -1.0))


def aspaviento(rng):
    dur = 0.55
    m = int(dur * SR)
    u = np.linspace(0, 1, m)
    x = blanco(rng, m)
    # Barrido de banda (el brazo pasa) con envolvente de campana.
    salida = np.zeros(m)
    trozos = 24
    for i in range(trozos):
        a, b = i * m // trozos, (i + 1) * m // trozos
        c = 250 + 900 * np.sin(np.pi * (i + 0.5) / trozos)
        salida[a:b] = banda(x, c * 0.6, c * 1.6, 2)[a:b]
    salida *= np.sin(np.pi * u) ** 2
    return normalizar(salida, -4.0)


# ---------------------------------------------------------------------------- Serpiente
def siseo(rng, dur):
    """El siseo: aire forzado por la glotis. Ruido con dos formantes que se mueven, un silbido agudo
    de la boca y la entrada brusca; el juego lo baja de tono con el tamano (el del basilisco es un
    rugido de aire)."""
    n = int(dur * SR)
    t = np.arange(n) / SR
    u = t / dur
    aire = color(rng, n, 0.4)
    salida = np.zeros(n)
    for f, q, g in ((2600, 2.2, 1.0), (5200, 3.0, 0.7), (8200, 4.0, 0.35)):
        deriva = 1 + 0.06 * np.sin(2 * np.pi * (0.7 + rng.random()) * t + rng.random() * 6)
        trozos = 32
        y = np.zeros(n)
        for i in range(trozos):
            a, b = i * n // trozos, (i + 1) * n // trozos
            c = f * deriva[(a + b) // 2]
            y[a:b] = banda(aire, c / (1 + 0.5 / q), c * (1 + 0.5 / q), 2)[a:b]
        salida += g * y
    # Golpe de aire al empezar y un temblor (la serpiente infla el cuerpo a golpes).
    env = np.minimum(1, t / 0.025) * (1 - np.clip((u - 0.72) / 0.28, 0, 1)) ** 1.5
    env *= 0.85 + 0.15 * np.sin(2 * np.pi * 7.5 * t)
    salida = salida / (np.std(salida) + 1e-9) * env
    return recortar_cola(normalizar(reverb(salida, rng, t60=0.8, mezcla=0.12), -2.0))


def reptar(rng):
    """Las escamas del vientre sobre la hojarasca: roce continuo con crujidos de hojas secas."""
    dur = 0.9
    m = int(dur * SR)
    tt = np.arange(m) / SR
    roce = banda(color(rng, m, 0.8), 900, 6500) * (0.6 + 0.4 * np.sin(np.pi * tt / dur)) * 0.35
    cruje = np.zeros(m)
    for _ in range(rng.integers(10, 22)):
        i = rng.integers(0, m - 400)
        k = rng.integers(60, 300)
        cruje[i:i + k] += rng.uniform(-1, 1) * np.exp(-np.arange(k) / (k / 4))
    x = roce + banda(cruje, 1200, 7000) * 0.7
    x *= np.sin(np.pi * tt / dur) ** 0.5
    return normalizar(x, -6.0)


def mordisco(rng):
    """El ataque: el cuerpo que sale disparado (un soplo corto) y el golpe seco de la boca."""
    m = int(0.6 * SR)
    tt = np.arange(m) / SR
    soplo = banda(blanco(rng, m), 400, 3000, 2) * np.exp(-((tt - 0.05) / 0.035) ** 2) * 0.5
    i0 = int(0.11 * SR)
    golpe = np.zeros(m)
    k = m - i0
    tk = np.arange(k) / SR
    golpe[i0:] = (np.sin(2 * np.pi * np.cumsum(260 * np.exp(-tk / 0.02) + 120) / SR) * np.exp(-tk / 0.04)
                  + banda(blanco(rng, k), 1500, 8000) * np.exp(-tk / 0.008) * 0.8)
    x = saturar(soplo + golpe, 1.4)
    return recortar_cola(normalizar(reverb(x, rng, t60=0.6, mezcla=0.1), -1.0))


# ---------------------------------------------------------------------------- Armas
def disparo(rng):
    m = int(1.6 * SR)
    tt = np.arange(m) / SR
    chasquido = blanco(rng, m) * np.exp(-tt / 0.002)
    cuerpo = banda(blanco(rng, m), 350, 5000) * np.exp(-tt / 0.035)
    grave = banda(blanco(rng, m), 90, 400) * np.exp(-tt / 0.06) * 1.2
    x = saturar(chasquido * 1.5 + cuerpo + grave, 2.5)
    # Ecos del bosque: dos o tres reflexiones apagadas y una cola.
    eco = np.zeros(m)
    for _ in range(3):
        d = int(rng.uniform(0.12, 0.5) * SR)
        eco[d:] += paso_bajo(x, 1800, 2)[: m - d] * rng.uniform(0.12, 0.25)
    x = x + eco
    return recortar_cola(normalizar(reverb(x, rng, t60=1.3, mezcla=0.22, brillo=3000), -1.0))


# ---------------------------------------------------------------------------- Destruccion
def explosion(rng):
    dur = 5.0
    m = int(dur * SR)
    tt = np.arange(m) / SR
    chasquido = blanco(rng, m) * np.exp(-tt / 0.004)
    estruendo = color(rng, m, 2.0)
    # El filtro se cierra: primero desgarro, luego retumbo.
    trozos = 40
    salida = np.zeros(m)
    for i in range(trozos):
        a, b = i * m // trozos, (i + 1) * m // trozos
        fc = 150 + 3500 * np.exp(-((a + b) / 2 / SR) / 0.35)
        salida[a:b] = paso_bajo(estruendo, fc, 2)[a:b]
    salida *= np.exp(-tt / 1.2)
    sub = np.sin(2 * np.pi * np.cumsum(48 * np.exp(-tt / 0.6) + 22) / SR) * np.exp(-tt / 1.4) * 1.3
    escombros = np.zeros(m)
    for _ in range(rng.integers(60, 110)):
        i = int(rng.uniform(0.3, 3.5) ** 1.3 / 3.5 ** 0.3 * SR)
        if i >= m - 400:
            continue
        escombros[i:i + 400] += rng.uniform(-1, 1) * np.exp(-np.arange(400) / rng.uniform(30, 120))
    escombros = banda(escombros, 600, 5000) * 0.5
    x = saturar(chasquido * 1.2 + salida * 1.4 + sub + escombros, 2.0)
    return recortar_cola(normalizar(reverb(x, rng, t60=3.0, mezcla=0.35), -0.5))


def madera(rng):
    dur = 2.4
    m = int(dur * SR)
    x = np.zeros(m)
    for _ in range(rng.integers(5, 10)):
        i = int(rng.uniform(0.0, 0.9) * SR)
        k = int(0.12 * SR)
        tt = np.arange(k) / SR
        crujido = resonador(blanco(rng, k), rng.uniform(250, 900), 6) * np.exp(-tt / rng.uniform(0.015, 0.05))
        x[i:i + k] += crujido[: m - i] * rng.uniform(0.5, 1.0)
    for _ in range(rng.integers(20, 40)):
        i = int(rng.uniform(0.0, 1.6) * SR)
        k = int(0.03 * SR)
        x[i:i + k] += banda(blanco(rng, k), 2000, 7000)[: m - i] * np.exp(-np.arange(min(k, m - i)) / 300) * 0.4
    tt = np.arange(m) / SR
    derrumbe = paso_bajo(color(rng, m, 2), 350) * np.exp(-((tt - 0.6) ** 2) / 0.12) * 0.8
    x = saturar(x + derrumbe, 1.6)
    return recortar_cola(normalizar(reverb(x, rng, t60=1.4, mezcla=0.25), -1.0))


def metal(rng):
    dur = 3.0
    m = int(dur * SR)
    tt = np.arange(m) / SR
    x = np.zeros(m)
    excitacion = blanco(rng, m) * np.exp(-tt / 0.01)
    base = rng.uniform(140, 260)
    for r, tau in ((1.0, 1.2), (2.43, 0.9), (4.17, 0.7), (6.73, 0.45), (9.4, 0.3), (12.9, 0.2)):
        f = base * r * rng.uniform(0.97, 1.03)
        x += resonador(excitacion, f, 60) * np.exp(-tt / tau) * rng.uniform(0.5, 1.0)
    roce = banda(blanco(rng, m), 1500, 6000) * np.exp(-((tt - 0.5) ** 2) / 0.08) * 0.15
    golpe = paso_bajo(blanco(rng, m), 600) * np.exp(-tt / 0.05)
    x = saturar(x / (np.max(np.abs(x)) + 1e-9) + roce + golpe, 1.4)
    return recortar_cola(normalizar(reverb(x, rng, t60=1.6, mezcla=0.25), -1.5))


def arbol(rng):
    dur = 4.5
    m = int(dur * SR)
    tt = np.arange(m) / SR
    # Crujido del tronco al ceder: friccion (sierra irregular) en banda media.
    f = 45 + 25 * np.sin(2 * np.pi * 0.7 * tt) + 8 * paso_bajo(blanco(rng, m), 20, 2)
    sierra = 2 * ((np.cumsum(f) / SR) % 1) - 1
    crujido = banda(sierra * (0.6 + 0.4 * blanco(rng, m) ** 2), 200, 1200) * np.exp(-((tt - 0.6) ** 2) / 0.25)
    # Hojas que barren el aire mientras cae.
    hojas = banda(blanco(rng, m), 1200, 7000) * np.clip((tt - 0.6) / 1.4, 0, 1) ** 2 * (tt < 2.25) * 0.6
    # Golpe contra el suelo y ramas que se parten.
    i = int(2.25 * SR)
    golpe = np.zeros(m)
    k = m - i
    tk = np.arange(k) / SR
    golpe[i:] = (np.sin(2 * np.pi * np.cumsum(60 * np.exp(-tk / 0.2) + 28) / SR) * np.exp(-tk / 0.4)
                 + paso_bajo(color(rng, k, 1.5), 1200) * np.exp(-tk / 0.25) * 0.9)
    ramas = np.zeros(m)
    for _ in range(rng.integers(8, 16)):
        j = i + int(rng.uniform(0, 0.6) * SR)
        r = int(0.08 * SR)
        if j + r < m:
            ramas[j:j + r] += resonador(blanco(rng, r), rng.uniform(400, 1500), 5) * np.exp(-np.arange(r) / (0.02 * SR))
    hojas_suelo = np.zeros(m)
    hojas_suelo[i:] = banda(blanco(rng, k), 1500, 8000) * np.exp(-tk / 0.5) * 0.5
    x = saturar(crujido * 0.5 + hojas + golpe * 1.3 + ramas * 0.7 + hojas_suelo, 1.5)
    return recortar_cola(normalizar(reverb(x, rng, t60=1.8, mezcla=0.28), -1.0))


# ---------------------------------------------------------------------------- Ambientes
def bucle(x, cruce):
    """Funde la cola sobre el principio para que el bucle no tenga costura."""
    c = int(cruce * SR)
    rampa = np.linspace(0, 1, c)[:, None] if x.ndim == 2 else np.linspace(0, 1, c)
    inicio = x[:c] * rampa + x[-c:] * (1 - rampa)
    return np.concatenate([inicio, x[c:-c]])


def selva(rng):
    dur = 64.0
    m = int(dur * SR)
    tt = np.arange(m) / SR
    L = np.zeros(m)
    R = np.zeros(m)

    def pan(x, p):
        nonlocal L, R
        L += x * np.cos((p + 1) * np.pi / 4)
        R += x * np.sin((p + 1) * np.pi / 4)

    # Lecho: hojas mecidas por el aire (rafagas lentas) y rumor grave.
    rafagas = 0.5 + 0.5 * paso_bajo(blanco(rng, m), 0.25, 2) / 0.02
    rafagas = np.clip(rafagas, 0.15, 1.6)
    for p in (-0.7, 0.7):
        pan(banda(blanco(rng, m), 1800, 9000) * rafagas * 0.018, p)
    pan(paso_bajo(color(rng, m, 1.5), 300) * 0.05, 0)
    # Cigarras: ruido filtrado en pulsos (no tonos puros, que taladran), lejanas; crecen y se
    # apagan durante unos segundos y luego callan un buen rato.
    for _ in range(2):
        fc = rng.uniform(3200, 4600)
        mod = 0.5 + 0.5 * np.sin(2 * np.pi * rng.uniform(18, 30) * tt)
        fase = rng.uniform(0, 2 * np.pi)
        lenta = np.clip(np.sin(2 * np.pi * tt / rng.uniform(26, 40) + fase) * 2.2 - 1.2, 0, 1) ** 2
        silbido = banda(blanco(rng, m), fc * 0.85, fc * 1.18) * mod ** 2 * lenta
        pan(paso_bajo(silbido, 7000, 2) * 0.0035, rng.uniform(-0.8, 0.8))
    # Grillos: chirridos cortos, suaves y espaciados.
    for _ in range(2):
        fc = rng.uniform(3300, 4100)
        periodo = rng.uniform(0.9, 1.6)
        pulso = (np.sin(2 * np.pi * tt / periodo) > 0.94).astype(float)
        tri = 0.5 + 0.5 * np.sign(np.sin(2 * np.pi * 24 * tt))
        chirrido = banda(blanco(rng, m), fc * 0.95, fc * 1.05) * paso_bajo(pulso * tri, 150, 2)
        pan(chirrido * 0.006, rng.uniform(-1, 1))
    # Pajaros: reclamos con barrido de frecuencia, trinos y silbidos de dos notas, al azar.
    for _ in range(70):
        i = int(rng.uniform(0, dur - 3) * SR)
        tipo = rng.integers(0, 3)
        if tipo == 0:   # reclamo que baja
            d = rng.uniform(0.15, 0.45)
            tk = np.arange(int(d * SR)) / SR
            f = rng.uniform(2500, 4500) * np.exp(-tk / d * rng.uniform(0.3, 1.0))
            x = np.sin(2 * np.pi * np.cumsum(f) / SR) * np.sin(np.pi * tk / d) ** 2
            reps = rng.integers(1, 4)
            x = np.concatenate([np.concatenate([x, np.zeros(int(rng.uniform(0.05, 0.2) * SR))]) for _ in range(reps)])
        elif tipo == 1:  # trino
            d = rng.uniform(0.5, 1.4)
            tk = np.arange(int(d * SR)) / SR
            f = rng.uniform(3000, 5500) + 600 * np.sin(2 * np.pi * rng.uniform(14, 24) * tk)
            x = np.sin(2 * np.pi * np.cumsum(f) / SR) * np.sin(np.pi * tk / d) ** 0.5 * (0.5 + 0.5 * np.sin(2 * np.pi * 18 * tk))
        else:            # silbido de dos notas
            d = rng.uniform(0.25, 0.4)
            tk = np.arange(int(d * SR)) / SR
            f1, f2 = rng.uniform(1800, 2600), rng.uniform(1300, 2200)
            f = np.where(tk < d * 0.45, f1, f2)
            x = np.sin(2 * np.pi * np.cumsum(f) / SR) * np.sin(np.pi * tk / d) ** 1.2
        lejos = rng.uniform(0.15, 1.0)
        x = paso_bajo(x, 9000 - 5000 * (1 - lejos), 2) * 0.05 * lejos
        k = min(len(x), m - i)
        pan(np.concatenate([np.zeros(i), x[:k], np.zeros(m - i - k)]), rng.uniform(-1, 1))
    # Aullidos lejanos (monos aulladores): rugido grave y apagado de vez en cuando.
    for _ in range(4):
        i = int(rng.uniform(0, dur - 5) * SR)
        d = rng.uniform(2.0, 3.5)
        k = int(d * SR)
        tk = np.arange(k) / SR
        f = 160 + 60 * np.sin(np.pi * tk / d) + 10 * np.sin(2 * np.pi * 6 * tk)
        x = sum(np.sin(h * 2 * np.pi * np.cumsum(f) / SR) / h for h in range(1, 8))
        x = paso_bajo(resonador(x, 500, 3) + 0.3 * x, 1200) * np.sin(np.pi * tk / d) ** 1.5 * 0.02
        pan(np.concatenate([np.zeros(i), x, np.zeros(m - i - k)]), rng.uniform(-1, 1))
    st = np.stack([L, R], -1)
    st = reverb(st[:, 0], rng, t60=1.8, mezcla=0.3)[:m, None] * np.array([1.0, 0.0]) + \
        reverb(st[:, 1], rng, t60=1.8, mezcla=0.3)[:m, None] * np.array([0.0, 1.0])
    return normalizar(bucle(st, 3.0), -8.0)


def rio(rng):
    dur = 24.0
    m = int(dur * SR)
    tt = np.arange(m) / SR
    # Rumor grave del agua (no siseo): ruido marron filtrado, con vaiven lento.
    base = banda(color(rng, m, 1.8), 90, 1400) * (0.8 + 0.2 * np.sin(2 * np.pi * 0.13 * tt))
    burbujas = np.zeros(m)
    for _ in range(900):
        i = rng.integers(0, m - 3000)
        k = rng.integers(600, 2400)
        tk = np.arange(k) / SR
        f = rng.uniform(300, 1400) * (1 + 1.5 * tk / (k / SR))
        burbujas[i:i + k] += np.sin(2 * np.pi * np.cumsum(f) / SR) * np.exp(-tk / (k / SR / 3)) * rng.uniform(0.2, 1)
    x = base * 0.6 + paso_bajo(burbujas, 2500, 2) * 0.06
    st = np.stack([x, np.roll(x, int(0.013 * SR))], -1)
    return normalizar(bucle(st, 2.0), -9.0)


def fuego(rng):
    dur = 12.0
    m = int(dur * SR)
    rumor = paso_bajo(color(rng, m, 2.0), 500) * (0.8 + 0.2 * paso_bajo(blanco(rng, m), 1.5, 2) / 0.01)
    chasquidos = np.zeros(m)
    for _ in range(int(dur * 22)):
        i = rng.integers(0, m - 600)
        k = rng.integers(60, 500)
        chasquidos[i:i + k] += rng.uniform(-1, 1) * np.exp(-np.arange(k) / rng.uniform(10, 80))
    x = rumor * 0.5 + banda(chasquidos, 800, 5000) * 0.45
    st = np.stack([x, np.roll(x, int(0.009 * SR))], -1)
    return normalizar(bucle(st, 1.5), -8.0)


# ---------------------------------------------------------------------------- Helicoptero
# Rotor principal calculado con el BEMT de Lur Aerospace: 4 palas a 289 rpm => 19,3 Hz de paso de
# pala. El bucle dura exactamente 40 pasos, asi no tiene costura.
PASO_PALA_HZ = 4 * 289.0 / 60.0


def circular(fn, x):
    """Filtra una senal periodica sin romper su periodo (se filtra repetida tres veces y se queda la
    del medio)."""
    n = len(x)
    return fn(np.concatenate([x, x, x]))[n:2 * n]


def rotor(rng):
    # Todo periodico en el bucle (golpes que dan la vuelta, ruido filtrado en circular y senos con un
    # numero entero de ciclos): con IIR normales el bucle hacia clic cada 2 s (lo cazo el arnes de
    # AssetsCreator, ultra/audio_accion.py).
    pasos = 40
    n = int(round(SR * pasos / PASO_PALA_HZ))
    dur = n / SR

    def entera(f):
        return max(1, round(f * dur)) / dur

    x = np.zeros(n)
    periodo = n / pasos
    golpe_n = int(0.09 * SR)
    tg = np.arange(golpe_n) / SR
    for k in range(pasos):
        i = int(k * periodo)
        # Golpe de pala: pulso de presion grave y algo de "palmada" filtrada.
        amp = 0.85 + 0.15 * rng.random()
        golpe = (np.sin(2 * np.pi * 55 * tg) * np.exp(-tg / 0.028) * 0.9
                 + banda(blanco(rng, golpe_n), 120, 900) * np.exp(-tg / 0.014) * 0.6) * amp
        fin = min(n, i + golpe_n)
        x[i:fin] += golpe[: fin - i]
        if fin - i < golpe_n:   # lo que sobra entra por el principio (bucle)
            x[: golpe_n - (fin - i)] += golpe[fin - i:]
    # Rotor de cola: pulsos mas rapidos y suaves.
    cola_hz = entera(PASO_PALA_HZ * 2.45)
    tt = np.arange(n) / SR
    cola = circular(lambda s: banda(s, 200, 1300), blanco(rng, n)) * (0.5 + 0.5 * np.sin(2 * np.pi * cola_hz * tt)) ** 4 * 0.18
    # Rumor del aire que mueve el rotor y una turbina muy discreta (nada de silbidos agudos).
    rumor = circular(lambda s: paso_bajo(s, 450), color(rng, n, 1.8)) * 0.35
    vaiven = np.sin(2 * np.pi * entera(0.5) * tt)
    turbina = sum(np.sin(2 * np.pi * np.cumsum(entera(f) * (1 + 0.002 * vaiven)) / SR) / i
                  for i, f in enumerate((310.0, 620.0, 930.0), 1)) * 0.025
    y = circular(lambda s: paso_bajo(s, 2200, 2), x + cola + rumor + turbina)
    return normalizar(y, -6.0)


def cohete(rng):
    dur = 1.6
    m = int(dur * SR)
    tt = np.arange(m) / SR
    encendido = blanco(rng, m) * np.exp(-tt / 0.01) * 0.6
    chorro = color(rng, m, 1.2)
    trozos = 24
    salida = np.zeros(m)
    for i in range(trozos):
        a, b = i * m // trozos, (i + 1) * m // trozos
        fc = 600 + 1800 * np.exp(-((a + b) / 2 / SR) / 0.5)
        salida[a:b] = paso_bajo(chorro, fc, 2)[a:b]
    salida *= np.minimum(1.0, tt / 0.04) * np.exp(-tt / 0.6)
    x = saturar(encendido + salida, 1.5)
    return recortar_cola(normalizar(reverb(x, rng, t60=1.2, mezcla=0.25), -2.0))


# ---------------------------------------------------------------------------- Lote
def main():
    global SALIDA
    ap = argparse.ArgumentParser()
    ap.add_argument("--solo", default="", help="lista separada por comas")
    ap.add_argument("--proyecto", help="carpeta del proyecto Unity")
    ap.add_argument("--salida", help="carpeta de salida (manda sobre --proyecto)")
    ap.add_argument("--criatura", default="kong", help="carpeta de la voz de la criatura (rugidos, pisadas...)")
    a = ap.parse_args()
    if a.salida:
        SALIDA = a.salida
    elif a.proyecto:
        SALIDA = os.path.join(a.proyecto, "Assets", "_Game", "Art", "Audio")
    else:
        sys.exit("Falta --proyecto <carpeta del proyecto Unity> o --salida <carpeta>")
    criatura = a.criatura
    solo = set(s for s in a.solo.split(",") if s)

    def quiero(n):
        return not solo or n in solo

    print("Sonidos ->", SALIDA)
    if quiero("rugido"):
        for i in range(3):
            guardar(criatura, f"rugido_{i}", rugido(np.random.default_rng(100 + i), dur=2.6 + 0.5 * i))
    if quiero("pecho"):
        for i in range(2):
            guardar(criatura, f"pecho_{i}", golpes_pecho(np.random.default_rng(200 + i)))
    if quiero("pisada"):
        for i in range(6):
            guardar(criatura, f"pisada_{i}", pisada(np.random.default_rng(300 + i), hojas=i % 2 == 1))
    if quiero("golpe"):
        for i in range(4):
            guardar(criatura, f"golpe_{i}", punetazo(np.random.default_rng(400 + i)))
    if quiero("aspaviento"):
        for i in range(3):
            guardar(criatura, f"aspaviento_{i}", aspaviento(np.random.default_rng(450 + i)))
    if quiero("siseo"):
        for i in range(4):
            guardar(criatura, f"siseo_{i}", siseo(np.random.default_rng(1500 + i), dur=0.9 + 0.35 * i))
    if quiero("reptar"):
        for i in range(6):
            guardar(criatura, f"reptar_{i}", reptar(np.random.default_rng(1600 + i)))
    if quiero("mordisco"):
        for i in range(4):
            guardar(criatura, f"mordisco_{i}", mordisco(np.random.default_rng(1700 + i)))
    if quiero("disparo"):
        for i in range(5):
            guardar("armas", f"disparo_{i}", disparo(np.random.default_rng(500 + i)))
    if quiero("explosion"):
        for i in range(3):
            guardar("destruccion", f"explosion_{i}", explosion(np.random.default_rng(600 + i)))
    if quiero("madera"):
        for i in range(4):
            guardar("destruccion", f"madera_{i}", madera(np.random.default_rng(700 + i)))
    if quiero("metal"):
        for i in range(3):
            guardar("destruccion", f"metal_{i}", metal(np.random.default_rng(800 + i)))
    if quiero("arbol"):
        for i in range(3):
            guardar("destruccion", f"arbol_{i}", arbol(np.random.default_rng(900 + i)))
    if quiero("rotor"):
        guardar("vehiculos", "rotor_0", rotor(np.random.default_rng(1300)))
    if quiero("cohete"):
        for i in range(2):
            guardar("armas", f"cohete_{i}", cohete(np.random.default_rng(1400 + i)))
    if quiero("selva"):
        guardar("ambiente", "selva_0", selva(np.random.default_rng(1000)))
    if quiero("rio"):
        guardar("ambiente", "rio_0", rio(np.random.default_rng(1100)))
    if quiero("fuego"):
        guardar("ambiente", "fuego_0", fuego(np.random.default_rng(1200)))


if __name__ == "__main__":
    main()
