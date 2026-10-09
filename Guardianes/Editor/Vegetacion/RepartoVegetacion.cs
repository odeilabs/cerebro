using System;
using System.Collections.Generic;
using Guardianes.Mundo;
using Guardianes.Vegetacion;
using Unity.Mathematics;
using UnityEngine;
using Random = Unity.Mathematics.Random;

namespace Guardianes.EditorTools.Vegetacion
{
    /// <summary>
    /// Reparte la vegetacion por el mundo: rejilla con jitter por especie, probabilidad segun una
    /// regla (altura, pendiente, humedad, rios, costa, claros, contaminacion, mascaras del juego y
    /// manchas de ruido para que las especies formen rodales) y distancia minima con lo ya plantado
    /// (hash espacial). Orden: el de las reglas (primero los grandes, al final el suelo). Cada juego
    /// pone sus reglas; ReglasSelvaTropical son las de la isla de Kong.
    /// </summary>
    public static class RepartoVegetacion
    {
        public struct Muestra
        {
            public float2 xz;
            public float altura, pendiente, humedad, cauce, ribera, claro, sucio;
            /// <summary>Cota del agua (NaN en seco) y profundidad (0 en seco).</summary>
            public float nivelAgua, profundidad;
            public DatosMundo datos;

            /// <summary>Mascara del juego (playa, varzea...) en este punto (0..1).</summary>
            public float Extra(string nombre) => datos != null ? datos.MascaraExtra(nombre, xz) : 0f;
        }

        public class Regla
        {
            public string especie;
            public float paso;          // lado de la rejilla (m)
            public float separacion;    // distancia minima a otras plantas grandes
            public bool ocupa = true;   // las demas respetan su separacion
            public float2 escala = new float2(0.85f, 1.2f);
            public float inclinacionMax;
            /// <summary>Se hunde en la ladera y se inclina con ella (rocas de pared).</summary>
            public bool enPared;
            /// <summary>Vive en el agua (nenufares, hierba flotante): se planta a la cota del agua.</summary>
            public bool acuatica;
            public Func<Muestra, float> probabilidad;
        }

        /// <param name="evitarAgua">Ninguna planta de tierra donde haya agua (el mapa de agua de los datos);
        /// sin el, solo se evitan el mar y el cauce (como la isla de Kong, que asi conserva su reparto).</param>
        public static Dictionary<string, List<InstanciaVegetal>> Repartir(DatosMundo d, EspecieVegetal[] especies, Regla[] reglas, int semilla,
                                                                         Action<string, float> progreso = null, bool evitarAgua = false)
        {
            var resultado = new Dictionary<string, List<InstanciaVegetal>>();
            var ocupado = new HashEspacial(8f);
            var rnd = new Random((uint)(semilla * 2654435761u + 1));
            var variantes = new Dictionary<string, int>();
            foreach (var e in especies) if (e != null) variantes[e.name] = e.variantes;
            var me = d.medidas;

            for (int r = 0; r < reglas.Length; r++)
            {
                var regla = reglas[r];
                if (!variantes.TryGetValue(regla.especie, out int nVariantes)) continue;
                progreso?.Invoke(regla.especie, r / (float)reglas.Length);
                var lista = new List<InstanciaVegetal>();
                int lado = (int)math.ceil(me.tamano / regla.paso);
                for (int j = 0; j < lado; j++)
                for (int i = 0; i < lado; i++)
                {
                    float2 xz = me.Origen + (new float2(i, j) + rnd.NextFloat2(0.1f, 0.9f)) * regla.paso;
                    var m = Muestrear(d, xz);
                    if (regla.acuatica)
                    {
                        if (m.profundidad < 0.2f) continue;
                    }
                    else if ((me.hayMar && m.altura < me.nivelMar - 0.5f) || m.cauce > 0.04f || (evitarAgua && m.profundidad > 0.05f)) continue;
                    float p = regla.probabilidad(m);
                    if (p <= 0f || rnd.NextFloat() > p) continue;
                    if (regla.separacion > 0f && ocupado.HayCerca(xz, regla.separacion)) continue;

                    float escala = rnd.NextFloat(regla.escala.x, regla.escala.y);
                    // Hundido un poco: la base nunca flota en laderas.
                    float y = m.altura - (0.15f + m.pendiente * 0.015f) * escala;
                    if (regla.acuatica) y = m.nivelAgua;
                    float incl = regla.inclinacionMax > 0f ? rnd.NextFloat(0f, regla.inclinacionMax) * math.saturate(m.pendiente / 35f + 0.3f) : 0f;
                    float rumbo = rnd.NextFloat(0f, math.PI * 2f);
                    if (regla.enPared)
                    {
                        // Inclinada como la pared y metida en ella (la cara de fuera asoma).
                        float hx = d.Altura(xz + new float2(2f, 0f)) - d.Altura(xz - new float2(2f, 0f));
                        float hz = d.Altura(xz + new float2(0f, 2f)) - d.Altura(xz - new float2(0f, 2f));
                        float2 cuestaAbajo = -math.normalizesafe(new float2(hx, hz));
                        incl = math.radians(m.pendiente) * 0.7f;
                        // Eje de inclinacion perpendicular a la cuesta: la roca se tumba hacia el valle.
                        rumbo = math.atan2(cuestaAbajo.x, -cuestaAbajo.y);
                        y = m.altura - escala * 0.9f;
                        xz -= cuestaAbajo * escala * 0.6f;
                    }
                    lista.Add(new InstanciaVegetal
                    {
                        posicion = new float3(xz.x, y, xz.y),
                        giro = rnd.NextFloat(0f, math.PI * 2f),
                        escala = escala,
                        inclinacion = incl,
                        rumboInclinacion = rumbo,
                        variante = (byte)rnd.NextInt(nVariantes),
                    });
                    if (regla.ocupa && regla.separacion > 0f) ocupado.Agregar(xz);
                }
                if (!resultado.TryGetValue(regla.especie, out var existente)) resultado[regla.especie] = lista;
                else existente.AddRange(lista);
                Debug.Log($"[GD] Vegetacion {regla.especie}: {lista.Count}");
            }
            return resultado;
        }

        public static Muestra Muestrear(DatosMundo d, float2 xz)
        {
            float h = d.Altura(xz);
            float hx = d.Altura(xz + new float2(1.5f, 0f)) - d.Altura(xz - new float2(1.5f, 0f));
            float hz = d.Altura(xz + new float2(0f, 1.5f)) - d.Altura(xz - new float2(0f, 1.5f));
            float agua = d.NivelAgua(xz);
            if (!float.IsNaN(agua) && agua <= h) agua = float.NaN;
            return new Muestra
            {
                xz = xz,
                altura = h,
                pendiente = math.degrees(math.atan(math.length(new float2(hx, hz)) / 3f)),
                humedad = d.Mascara(d.humedad, xz),
                cauce = d.Mascara(d.cauce, xz),
                ribera = d.Mascara(d.ribera, xz),
                claro = d.Mascara(d.claro, xz),
                sucio = d.Mascara(d.contaminacion, xz),
                nivelAgua = agua,
                profundidad = float.IsNaN(agua) ? 0f : agua - h,
                datos = d,
            };
        }

        // ------------------------------------------------------------------ Utilidades de reglas
        public static float Mancha(float2 xz, float escala, float desfase) => Ruido.Fbm(xz * escala + desfase, 3) * 0.5f + 0.5f;
        public static float Entre(float x, float a, float b, float suave) => Ruido.SmoothStep(a - suave, a + suave, x) * (1f - Ruido.SmoothStep(b - suave, b + suave, x));

        // ------------------------------------------------------------------ Reglas de la isla de Kong
        /// <summary>Selva cerrada: tierra firme, no muy empinada, fuera de claros y de lo contaminado.</summary>
        static float Selva(Muestra m) =>
            Entre(m.altura, 2.5f, 430f, 2f) * (1f - Ruido.SmoothStep(30f, 40f, m.pendiente)) *
            (1f - Ruido.SmoothStep(0.15f, 0.45f, m.claro)) * (1f - Ruido.SmoothStep(0.25f, 0.7f, m.sucio));

        /// <summary>La selva tropical de la isla de Kong (con mar: playas de palmeras y alturas desde la costa).</summary>
        public static Regla[] ReglasSelvaTropical() => new[]
        {
            new Regla { especie = "Ceiba", paso = 60f, separacion = 34f, escala = new float2(0.85f, 1.15f),
                probabilidad = m => Selva(m) * 0.5f * Entre(m.altura, 6f, 260f, 10f) * (0.4f + 0.6f * Mancha(m.xz, 0.004f, 3.1f)) },
            new Regla { especie = "Roca", paso = 22f, separacion = 6f, escala = new float2(0.8f, 4.5f), ocupa = false,
                probabilidad = m => Ruido.SmoothStep(22f, 45f, m.pendiente) * 0.55f + m.ribera * 0.12f + Ruido.SmoothStep(300f, 450f, m.altura) * 0.25f },
            // Paredes: rocas enormes encajadas en los acantilados y en los mogotes (rompen el
            // estiramiento de la textura del terreno en vertical).
            new Regla { especie = "Roca", paso = 9f, separacion = 0f, escala = new float2(3.5f, 9f), ocupa = false, enPared = true,
                probabilidad = m => Ruido.SmoothStep(44f, 60f, m.pendiente) * 0.85f },
            new Regla { especie = "DoselA", paso = 12f, separacion = 8.5f, escala = new float2(0.8f, 1.2f), inclinacionMax = 0.08f,
                probabilidad = m => Selva(m) * 0.82f * Mancha(m.xz, 0.006f, 11.7f) * 1.4f },
            new Regla { especie = "DoselB", paso = 12f, separacion = 8.5f, escala = new float2(0.75f, 1.15f), inclinacionMax = 0.08f,
                probabilidad = m => Selva(m) * 0.82f * (1f - Mancha(m.xz, 0.006f, 11.7f)) * 1.4f },
            new Regla { especie = "Palmera", paso = 9f, separacion = 5f, escala = new float2(0.8f, 1.2f), inclinacionMax = 0.12f,
                probabilidad = m => Entre(m.altura, 0.8f, 14f, 1.5f) * (1f - Ruido.SmoothStep(18f, 28f, m.pendiente)) * 0.55f * (1f - m.sucio)
                                    + m.ribera * Entre(m.altura, 1f, 80f, 3f) * 0.25f
                                    + m.claro * Entre(m.altura, 1f, 120f, 3f) * 0.06f },
            new Regla { especie = "Bambu", paso = 20f, separacion = 7f, escala = new float2(0.8f, 1.2f),
                probabilidad = m => m.ribera * 0.7f * Selva(m) * Mancha(m.xz, 0.01f, 5.5f) + m.humedad * 0.05f * Selva(m) },
            new Regla { especie = "HelechoArboreo", paso = 11f, separacion = 4f, escala = new float2(0.8f, 1.2f),
                probabilidad = m => Selva(m) * (0.12f + 0.4f * m.humedad) * Entre(m.altura, 20f, 380f, 15f) * Mancha(m.xz, 0.012f, 21f) * 1.6f },
            new Regla { especie = "Platanera", paso = 13f, separacion = 3.5f, escala = new float2(0.85f, 1.2f),
                probabilidad = m => Entre(m.altura, 2f, 150f, 5f) * (1f - Ruido.SmoothStep(20f, 30f, m.pendiente)) *
                                    (0.1f + 0.5f * m.humedad + 0.25f * m.claro) * Mancha(m.xz, 0.015f, 8.8f) * (1f - m.sucio) },
            new Regla { especie = "TroncoCaido", paso = 40f, separacion = 6f, ocupa = false, escala = new float2(0.8f, 1.2f),
                probabilidad = m => Selva(m) * 0.35f + m.sucio * 0.4f * Entre(m.altura, 2f, 200f, 4f) },
            new Regla { especie = "Arbusto", paso = 5.5f, separacion = 0f, ocupa = false, escala = new float2(0.7f, 1.3f),
                probabilidad = m => (Selva(m) * 0.45f + m.claro * 0.1f) * Mancha(m.xz, 0.03f, 2.2f) * 1.3f },
            new Regla { especie = "Helecho", paso = 4f, separacion = 0f, ocupa = false, escala = new float2(0.7f, 1.4f),
                probabilidad = m => Selva(m) * (0.25f + 0.45f * m.humedad) * Mancha(m.xz, 0.04f, 9.9f) * 1.4f },
            new Regla { especie = "HojasGrandes", paso = 7f, separacion = 0f, ocupa = false, escala = new float2(0.8f, 1.4f),
                probabilidad = m => Selva(m) * (0.1f + 0.5f * m.humedad + 0.4f * m.ribera) * Mancha(m.xz, 0.025f, 31f) },
            new Regla { especie = "Hierba", paso = 3.5f, separacion = 0f, ocupa = false, escala = new float2(0.8f, 1.3f),
                probabilidad = m => (m.claro * 0.7f + Ruido.SmoothStep(140f, 260f, m.altura) * 0.3f + Entre(m.altura, 1.5f, 6f, 1f) * 0.25f) *
                                    (1f - Ruido.SmoothStep(28f, 38f, m.pendiente)) * (1f - m.sucio) * Mancha(m.xz, 0.03f, 17f) * 1.3f },
            new Regla { especie = "Flores", paso = 12f, separacion = 0f, ocupa = false, escala = new float2(0.8f, 1.2f),
                probabilidad = m => (Selva(m) * 0.12f + m.claro * 0.2f + m.ribera * 0.15f) * Mancha(m.xz, 0.02f, 41f) },
        };

        // ------------------------------------------------------------------ Hash espacial
        sealed class HashEspacial
        {
            readonly float _celda;
            readonly Dictionary<long, List<float2>> _mapa = new Dictionary<long, List<float2>>();
            public HashEspacial(float celda) { _celda = celda; }

            long Clave(int x, int y) => ((long)x << 32) ^ (uint)y;

            public void Agregar(float2 p)
            {
                int2 c = (int2)math.floor(p / _celda);
                long k = Clave(c.x, c.y);
                if (!_mapa.TryGetValue(k, out var l)) _mapa[k] = l = new List<float2>();
                l.Add(p);
            }

            public bool HayCerca(float2 p, float r)
            {
                int2 c0 = (int2)math.floor((p - r) / _celda), c1 = (int2)math.floor((p + r) / _celda);
                float r2 = r * r;
                for (int y = c0.y; y <= c1.y; y++)
                for (int x = c0.x; x <= c1.x; x++)
                {
                    if (!_mapa.TryGetValue(Clave(x, y), out var l)) continue;
                    foreach (var q in l) if (math.distancesq(p, q) < r2) return true;
                }
                return false;
            }
        }
    }
}
