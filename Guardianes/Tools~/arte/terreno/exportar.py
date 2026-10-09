"""Exportacion de una capa a PNG (albedo, normal, mascara HDRP) y hojas de revision.

Mascara HDRP de terreno (lineal): R = metalico (0), G = AO, B = altura (0-1), A = suavidad.
Normal en espacio tangente OpenGL (+Y arriba), como espera Unity.
"""
import math
import os
from dataclasses import dataclass
from typing import Optional

import numpy as np
from PIL import Image, ImageDraw

from nucleo import (F32, costura, lineal_a_srgb, luminancia, normal_desde_altura, normalizar,
                    oclusion)


@dataclass
class ResultadoCapa:
    albedo: np.ndarray                    # lineal, HxWx3
    altura: np.ndarray                    # metros, HxW
    suavidad: np.ndarray                  # 0-1, HxW
    fuerza_normal: float = 1.0            # multiplicador sobre la pendiente fisica
    radio_ao_m: float = 0.05              # alcance del AO en metros
    intensidad_ao: float = 1.0
    ao_extra: Optional[np.ndarray] = None       # multiplicador adicional del AO (0-1)
    oscurecer_cavidad: float = 0.25       # cuanto oscurece el AO al albedo (sutil)
    altura_mascara: Optional[np.ndarray] = None  # canal B ya preparado (0-1)
    gamma_altura: float = 1.0
    ecualizar_altura: float = 0.0          # 0 lineal .. 1 ecualizado por rango (reparte el canal B)
    micro_normal: Optional[np.ndarray] = None    # relieve extra (m) solo para la normal
    notas: str = ""


def _a8(x, rng):
    """float 0-1 -> uint8 con tramado triangular (evita bandas)."""
    d = (rng.random(x.shape, dtype=F32) - rng.random(x.shape, dtype=F32))
    return np.clip(np.round(x * 255.0 + d), 0, 255).astype(np.uint8)


def iluminar(albedo, normal, ao, suav, luz=(-0.55, 0.60, 0.42), vista=(0.0, -0.42, 0.91),
             sol=0.85, cielo=0.42):
    """Render sencillo tipo motor: Lambert + GGX con sol bajo, cielo con AO. Sin sombras
    proyectadas (el terreno de HDRP no las hace con el normal map). Devuelve sRGB 0-1.
    sol: irradiancia solar / pi (difuso = albedo * sol * N.L)."""
    L = np.array(luz, F32)
    L /= np.linalg.norm(L)
    V = np.array(vista, F32)
    V /= np.linalg.norm(V)
    Hh = L + V
    Hh /= np.linalg.norm(Hh)
    n = normal
    ndl = np.clip(n @ L, 0.0, 1.0)
    ndv = np.clip(n @ V, 1e-3, 1.0)
    ndh = np.clip(n @ Hh, 0.0, 1.0)
    vdh = float(np.clip(V @ Hh, 0.0, 1.0))
    rug = np.clip(1.0 - suav, 0.02, 1.0)
    a = rug * rug
    a2 = a * a
    D = a2 / (math.pi * (ndh * ndh * (a2 - 1.0) + 1.0) ** 2)
    k = (rug + 1.0) ** 2 / 8.0
    G = (ndl / (ndl * (1 - k) + k)) * (ndv / (ndv * (1 - k) + k))
    F = 0.04 + 0.96 * (1.0 - vdh) ** 5
    espec = D * G * F / (4.0 * np.maximum(ndl, 1e-3) * ndv) * ndl
    hemi = 0.65 + 0.35 * n[..., 2]
    col = albedo * (ndl * sol)[..., None]
    col += albedo * (cielo * ao * hemi)[..., None]
    col += (espec * math.pi * sol * (0.35 + 0.65 * ao))[..., None]
    x = col * 1.15
    tm = (x * (2.51 * x + 0.03)) / (x * (2.43 * x + 0.59) + 0.14)
    return lineal_a_srgb(np.clip(tm, 0, 1)).astype(F32)


def _img(arr):
    if arr.dtype != np.uint8:
        arr = np.clip(np.round(arr * 255), 0, 255).astype(np.uint8)
    return Image.fromarray(arr)


def _mosaico(arr, veces, lado):
    t = np.tile(arr, (veces, veces) + (1,) * (arr.ndim - 2))
    return _img(t).resize((lado, lado), Image.LANCZOS)


def _rotulo(img, texto):
    img = img.convert("RGB")
    d = ImageDraw.Draw(img)
    d.rectangle([0, 0, 8 + 7 * len(texto), 16], fill=(0, 0, 0))
    d.text((4, 2), texto, fill=(255, 255, 255))
    return img


def exportar(ctx, r: ResultadoCapa, dir_capas, dir_revision, segundos=None):
    n = ctx.n
    nombre = ctx.nombre
    os.makedirs(os.path.join(dir_capas, nombre), exist_ok=True)
    rng = np.random.default_rng(12345)

    altura = r.altura.astype(F32)
    h_normal = altura if r.micro_normal is None else altura + r.micro_normal
    normal = normal_desde_altura(h_normal, ctx.texel, r.fuerza_normal)
    ao = oclusion(altura, ctx.texel, r.radio_ao_m, intensidad=r.intensidad_ao)
    if r.ao_extra is not None:
        ao = ao * r.ao_extra
    ao = np.clip(ao, 0.0, 1.0).astype(F32)
    albedo = r.albedo * (1.0 - r.oscurecer_cavidad * (1.0 - ao))[..., None]
    albedo = np.clip(albedo, 0.008, 0.9).astype(F32)
    if r.altura_mascara is not None:
        b = np.clip(r.altura_mascara, 0, 1).astype(F32)
    else:
        b = normalizar(altura, 0.3, 99.7)
        if r.ecualizar_altura > 0:
            rango = np.empty(altura.size, F32)
            rango[np.argsort(altura, axis=None)] = np.linspace(0, 1, altura.size, dtype=F32)
            b = (1 - r.ecualizar_altura) * b + r.ecualizar_altura * rango.reshape(altura.shape)
        b = b ** r.gamma_altura
    suav = np.clip(r.suavidad, 0.0, 1.0).astype(F32)

    alb_srgb = lineal_a_srgb(albedo)
    alb8 = _a8(alb_srgb, rng)
    rgba = np.concatenate([alb8, np.full((n, n, 1), 255, np.uint8)], -1)
    base = os.path.join(dir_capas, nombre, nombre)
    Image.fromarray(rgba, "RGBA").save(base + "_albedo.png", compress_level=6)
    nrm8 = _a8(normal * 0.5 + 0.5, rng)
    Image.fromarray(nrm8, "RGB").save(base + "_normal.png", compress_level=6)
    masc = _a8(np.stack([np.zeros_like(ao), ao, b, suav], -1), rng)
    masc[..., 0] = 0  # metalico exactamente 0 (sin tramado)
    Image.fromarray(masc, "RGBA").save(base + "_mascara.png", compress_level=6)

    # ---------------- estadisticas y revision ----------------
    lum = luminancia(albedo)
    stats = {
        "lum_lineal_media": float(lum.mean()),
        "lum_p5": float(np.percentile(lum, 5)),
        "lum_p95": float(np.percentile(lum, 95)),
        "suav_media": float(suav.mean()),
        "ao_medio": float(ao.mean()),
        "altura_rango_cm": float((np.percentile(altura, 99.5) - np.percentile(altura, 0.5)) * 100),
        "b_media": float(b.mean()),
        "costura_alb": costura(alb_srgb),
        "costura_alt": costura(altura),
        "costura_nrm": costura(normal),
    }
    if dir_revision:
        os.makedirs(dir_revision, exist_ok=True)
        lit = iluminar(albedo, normal, ao, suav)
        lado = 640
        fila1 = [
            _rotulo(_mosaico(alb_srgb, 2, lado), "albedo 2x2"),
            _rotulo(_mosaico(lit, 2, lado), "iluminado 2x2 (sol bajo)"),
            _rotulo(_mosaico(lit, 4, lado), "iluminado 4x4"),
        ]
        m = 480
        fila2 = [
            _rotulo(_img(nrm8).resize((m, m), Image.LANCZOS), "normal"),
            _rotulo(_img(ao).resize((m, m), Image.LANCZOS), "AO"),
            _rotulo(_img(b).resize((m, m), Image.LANCZOS), "altura (B)"),
            _rotulo(_img(suav).resize((m, m), Image.LANCZOS), "suavidad (A)"),
        ]
        hoja = Image.new("RGB", (3 * lado, lado + m), (40, 40, 40))
        for i, im in enumerate(fila1):
            hoja.paste(im.convert("RGB"), (i * lado, 0))
        for i, im in enumerate(fila2):
            hoja.paste(im.convert("RGB"), (i * m, lado))
        t = f"{nombre}  {n}px  {ctx.tam:g} m  lum={stats['lum_lineal_media']:.3f}  " \
            f"suav={stats['suav_media']:.2f}  relieve={stats['altura_rango_cm']:.1f}cm"
        if segundos is not None:
            t += f"  {segundos:.1f}s"
        d = ImageDraw.Draw(hoja)
        d.rectangle([3 * m + 10, lado + 10, 3 * lado - 10, lado + 40], fill=(20, 20, 20))
        d.text((3 * m + 14, lado + 18), t[:80], fill=(255, 255, 255))
        hoja.save(os.path.join(dir_revision, f"{nombre}_revision.png"))

        # detalle 1:1 centrado en la esquina del mosaico 2x2 (delata cualquier costura)
        s = min(900, n)
        y0 = n - s // 2
        alb2 = np.tile(alb_srgb, (2, 2, 1))[y0:y0 + s, y0:y0 + s]
        lit2 = np.tile(lit, (2, 2, 1))[y0:y0 + s, y0:y0 + s]
        det = Image.new("RGB", (2 * s, s))
        det.paste(_img(alb2), (0, 0))
        det.paste(_img(lit2), (s, 0))
        det.save(os.path.join(dir_revision, f"{nombre}_detalle.png"))
    return stats
