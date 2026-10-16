using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using Random = Unity.Mathematics.Random;

namespace Guardianes.Vegetacion.Generacion
{
    /// <summary>
    /// Generadores de las plantas de la selva. Cada uno construye el esqueleto a partir de la semilla
    /// (igual en todos los LOD) y emite geometria segun el detalle: 0 = cerca, 1 = media, 2 = lejos.
    /// Submalla 0 = madera/tallo (opaco), submalla 1 = follaje (recortado, translucido).
    /// Atlas (Tools/arte/vegetacion): celdas 2x2 con la base del ramillete abajo en el centro;
    /// frondas y hojas largas en 2 columnas con la base abajo.
    /// </summary>
    public static class Plantas
    {
        public enum Forma
        {
            Ceiba, Dosel, Palmera, HelechoArboreo, Platanera, Bambu, Arbusto, Helecho, HojasGrandes, Hierba, Flores, Roca, TroncoCaido,
            // Las nuevas, al final (los assets guardan el numero).
            Herbazal,
        }

        public const int SubMadera = 0;
        public const int SubHojas = 1;

        public static Mesh Generar(Forma forma, uint semilla, int detalle, string nombre)
        {
            var m = new ConstructorMalla(forma == Forma.Roca ? 1 : 2);
            var r = new Random(semilla * 747796405u + 2891336453u);
            switch (forma)
            {
                case Forma.Ceiba: Ceiba(m, ref r, detalle); break;
                case Forma.Dosel: Dosel(m, ref r, detalle); break;
                case Forma.Palmera: Palmera(m, ref r, detalle); break;
                case Forma.HelechoArboreo: HelechoArboreo(m, ref r, detalle); break;
                case Forma.Platanera: Platanera(m, ref r, detalle); break;
                case Forma.Bambu: Bambu(m, ref r, detalle); break;
                case Forma.Arbusto: Arbusto(m, ref r, detalle); break;
                case Forma.Helecho: Helecho(m, ref r, detalle); break;
                case Forma.HojasGrandes: HojasGrandes(m, ref r, detalle); break;
                case Forma.Hierba: Hierba(m, ref r, detalle); break;
                case Forma.Flores: Flores(m, ref r, detalle); break;
                case Forma.Roca: Roca(m, ref r, detalle); break;
                case Forma.TroncoCaido: TroncoCaido(m, ref r, detalle); break;
                case Forma.Herbazal: Herbazal(m, ref r, detalle); break;
            }
            return m.Construir(nombre);
        }

        // ================================================================== Utilidades
        static Rect Celda2x2(int i) => new Rect((i & 1) * 0.5f + 0.004f, (i >> 1) * 0.5f + 0.004f, 0.492f, 0.492f);
        static Rect Columna(int c) => new Rect(c * 0.5f + 0.002f, 0.002f, 0.496f, 0.996f);

        static Color Pesos(float tronco, float rama, float fase) => new Color(rama, 0f, fase, tronco);

        /// <summary>Peso de flexion del tronco: crece con la altura relativa al cuadrado.</summary>
        static float FlexTronco(float y, float alto) => Mathf.Clamp01(y / alto) * Mathf.Clamp01(y / alto);

        static Vector3 DirDesdeAngulos(float azimut, float elevacion) =>
            new Vector3(Mathf.Cos(azimut) * Mathf.Cos(elevacion), Mathf.Sin(elevacion), Mathf.Sin(azimut) * Mathf.Cos(elevacion));

        static Vector3 Perpendicular(Vector3 d)
        {
            var p = Vector3.Cross(d, Mathf.Abs(d.y) < 0.9f ? Vector3.up : Vector3.right);
            return p.normalized;
        }

        /// <summary>Curva de rama: sale en 'dir', se arquea hacia 'tirar' (arriba o abajo) y serpentea.</summary>
        static List<Vector3> Curva(ref Random r, Vector3 inicio, Vector3 dir, float largo, int tramos, Vector3 tirar, float fuerzaTirar, float serpenteo)
        {
            var pts = new List<Vector3>(tramos + 1) { inicio };
            Vector3 p = inicio, d = dir.normalized;
            float paso = largo / tramos;
            for (int i = 1; i <= tramos; i++)
            {
                d = (d + tirar * (fuerzaTirar / tramos) + (Vector3)r.NextFloat3Direction() * serpenteo).normalized;
                p += d * paso;
                pts.Add(p);
            }
            return pts;
        }

        struct Rama
        {
            public List<Vector3> eje;
            public float radioBase, radioPunta;
            public float flexBase;   // peso de tronco en el arranque
            public float fase;
            public int nivel;
        }

        static void EmitirRama(ConstructorMalla m, Rama rama, int lados, float alto, float tamCorteza)
        {
            int n = rama.eje.Count;
            m.Tubo(Plantas.SubMadera, rama.eje,
                (i, a) => Mathf.Lerp(rama.radioBase, rama.radioPunta, i / (float)(n - 1)) * (1f + 0.05f * Mathf.Sin(a * 3f + i)),
                lados, tamCorteza, Mathf.Max(1f, Mathf.Round(rama.radioBase * 6.283f / tamCorteza)),
                i =>
                {
                    float t = i / (float)(n - 1);
                    float tronco = Mathf.Max(rama.flexBase, FlexTronco(rama.eje[i].y, alto));
                    float flexRama = rama.nivel == 0 ? 0f : t * (rama.nivel == 1 ? 0.35f : 0.8f);
                    return Pesos(tronco, flexRama, rama.fase);
                });
        }

        // ================================================================== Ceiba (gigante emergente)
        static void Ceiba(ConstructorMalla m, ref Random r, int detalle)
        {
            float alto = r.NextFloat(44f, 56f);
            float alturaCopa = alto * r.NextFloat(0.66f, 0.74f);
            float radioBase = r.NextFloat(1.3f, 1.7f);
            int lados = detalle == 0 ? 28 : detalle == 1 ? 14 : 6;
            int anillos = detalle == 0 ? 26 : detalle == 1 ? 12 : 5;

            // Tronco recto con una leve inclinacion y contrafuertes tabulares en la base.
            var eje = new List<Vector3>();
            Vector3 incl = new Vector3(r.NextFloat(-0.02f, 0.02f), 1f, r.NextFloat(-0.02f, 0.02f)).normalized;
            for (int i = 0; i <= anillos; i++)
            {
                float t = i / (float)anillos;
                // Mas anillos abajo (donde estan los contrafuertes).
                float y = alturaCopa * 1.05f * t * t * (0.35f + 0.65f * t) / (0.35f + 0.65f) + alturaCopa * 1.05f * 0f;
                y = alturaCopa * 1.05f * Mathf.Pow(t, 1.35f);
                eje.Add(incl * y);
            }
            int contrafuertes = r.NextInt(5, 8);
            var angulos = new float[contrafuertes];
            var fuerza = new float[contrafuertes];
            for (int k = 0; k < contrafuertes; k++)
            {
                angulos[k] = k / (float)contrafuertes * Mathf.PI * 2f + r.NextFloat(-0.35f, 0.35f);
                fuerza[k] = r.NextFloat(2.2f, 4.2f);
            }
            float alturaContra = r.NextFloat(5f, 8f);
            float fase0 = r.NextFloat();
            m.Tubo(SubMadera, eje, (i, a) =>
            {
                float y = eje[i].y;
                float rad = Mathf.Lerp(radioBase, radioBase * 0.55f, Mathf.Clamp01(y / alturaCopa));
                // Contrafuertes: aletas estrechas que se abren hacia el suelo.
                float aleta = 0f;
                for (int k = 0; k < contrafuertes; k++)
                {
                    float d = Mathf.DeltaAngle(a * Mathf.Rad2Deg, angulos[k] * Mathf.Rad2Deg) * Mathf.Deg2Rad;
                    aleta += fuerza[k] * Mathf.Exp(-d * d / 0.018f);
                }
                float base_ = Mathf.Exp(-y / (alturaContra * 0.45f));
                return rad * (1f + aleta * base_ + 0.04f * Mathf.Sin(a * 5f + y * 0.7f));
            }, lados, 3f, Mathf.Round(radioBase * 6.283f / 3f), i => Pesos(FlexTronco(eje[i].y, alto) * 0.6f, 0f, fase0));

            // Grandes ramas casi horizontales que forman la copa en paraguas.
            Vector3 cima = eje[eje.Count - 1];
            int ramas = detalle == 2 ? 4 : r.NextInt(5, 8);
            var puntas = new List<(Vector3 p, Vector3 d, float fase, float flex)>();
            var centroCopa = cima + Vector3.up * 3f;
            for (int k = 0; k < ramas; k++)
            {
                float az = k / (float)ramas * Mathf.PI * 2f + r.NextFloat(-0.3f, 0.3f);
                float y0 = cima.y - r.NextFloat(0f, 7f);
                var inicio = incl * y0;
                var dir = DirDesdeAngulos(az, r.NextFloat(0.12f, 0.4f));
                float largo = r.NextFloat(12f, 18f);
                float fase = r.NextFloat();
                var curva = Curva(ref r, inicio, dir, largo, detalle == 0 ? 8 : 4, Vector3.up, 0.25f, 0.06f);
                var rama = new Rama { eje = curva, radioBase = radioBase * 0.38f, radioPunta = 0.14f, flexBase = FlexTronco(y0, alto) * 0.6f, fase = fase, nivel = 1 };
                EmitirRama(m, rama, detalle == 0 ? 10 : detalle == 1 ? 6 : 4, alto, 2f);
                // Ramas secundarias hacia fuera y arriba.
                int sec = detalle == 2 ? 2 : r.NextInt(3, 6);
                for (int s = 0; s < sec; s++)
                {
                    int idx = Mathf.Clamp((int)(curva.Count * r.NextFloat(0.35f, 0.95f)), 1, curva.Count - 1);
                    var o = curva[idx];
                    var d2 = (dir + Vector3.up * r.NextFloat(0.2f, 0.7f) + (Vector3)r.NextFloat3Direction() * 0.45f).normalized;
                    float l2 = r.NextFloat(4f, 8f);
                    var c2 = Curva(ref r, o, d2, l2, detalle == 0 ? 5 : 3, Vector3.up, 0.2f, 0.12f);
                    float flex = FlexTronco(o.y, alto);
                    if (detalle < 2)
                        EmitirRama(m, new Rama { eje = c2, radioBase = 0.12f, radioPunta = 0.035f, flexBase = flex, fase = r.NextFloat(), nivel = 2 }, 5, alto, 1.2f);
                    puntas.Add((c2[c2.Count - 1], d2, r.NextFloat(), flex));
                    puntas.Add((c2[c2.Count / 2], d2, r.NextFloat(), flex));
                }
                puntas.Add((curva[curva.Count - 1], dir, fase, FlexTronco(curva[curva.Count - 1].y, alto)));
            }

            // Follaje: ramilletes alrededor de cada punta (copa aplanada).
            int porPunta = detalle == 0 ? 9 : detalle == 1 ? 4 : 2;
            float tam = detalle == 0 ? 3.4f : detalle == 1 ? 4.6f : 7.5f;
            foreach (var pu in puntas)
            {
                for (int k = 0; k < porPunta; k++)
                {
                    var off = (Vector3)r.NextFloat3Direction() * r.NextFloat(0.5f, 2.6f);
                    off.y *= 0.45f;
                    var basep = pu.p + off;
                    var dir = (pu.d * 0.6f + Vector3.up * r.NextFloat(0.1f, 0.6f) + (Vector3)r.NextFloat3Direction() * 0.7f).normalized;
                    float t = tam * r.NextFloat(0.8f, 1.2f);
                    m.Tarjeta(SubHojas, basep, dir, (Vector3)r.NextFloat3Direction(), t, t, Celda2x2(r.NextInt(4)),
                        r.NextFloat(0.1f, 0.35f), Pesos(pu.flex, 0.7f, pu.fase), centroCopa);
                }
            }
        }

        // ================================================================== Arbol de dosel
        static void Dosel(ConstructorMalla m, ref Random r, int detalle)
        {
            float alto = r.NextFloat(22f, 33f);
            float alturaHorquilla = alto * r.NextFloat(0.5f, 0.64f);
            float radioBase = r.NextFloat(0.45f, 0.75f);
            int lados = detalle == 0 ? 14 : detalle == 1 ? 8 : 5;
            int anillos = detalle == 0 ? 12 : detalle == 1 ? 6 : 3;

            var eje = Curva(ref r, Vector3.zero, new Vector3(r.NextFloat(-0.08f, 0.08f), 1f, r.NextFloat(-0.08f, 0.08f)), alturaHorquilla, anillos, Vector3.up, 0.2f, 0.035f);
            float fase0 = r.NextFloat();
            float raices = r.NextFloat(0.4f, 1.4f);
            m.Tubo(SubMadera, eje, (i, a) =>
            {
                float y = eje[i].y;
                float rad = Mathf.Lerp(radioBase, radioBase * 0.7f, y / alturaHorquilla);
                float aletas = raices * Mathf.Pow(Mathf.Abs(Mathf.Sin(a * 2.5f)), 4f) * Mathf.Exp(-y / 1.6f);
                return rad * (1f + aletas + 0.05f * Mathf.Sin(a * 4f + y));
            }, lados, 2f, Mathf.Round(radioBase * 6.283f / 2f), i => Pesos(FlexTronco(eje[i].y, alto) * 0.7f, 0f, fase0));

            Vector3 horquilla = eje[eje.Count - 1];
            var centroCopa = horquilla + Vector3.up * (alto - alturaHorquilla) * 0.55f;
            int limbs = detalle == 2 ? 3 : r.NextInt(3, 6);
            var puntas = new List<(Vector3 p, Vector3 d, float fase, float flex)>();
            for (int k = 0; k < limbs; k++)
            {
                float az = k / (float)limbs * Mathf.PI * 2f + r.NextFloat(-0.4f, 0.4f);
                var dir = DirDesdeAngulos(az, r.NextFloat(0.75f, 1.15f));
                float largo = (alto - alturaHorquilla) * r.NextFloat(0.6f, 0.85f);
                var c1 = Curva(ref r, horquilla - Vector3.up * r.NextFloat(0f, 1.5f), dir, largo, detalle == 0 ? 6 : 3, Vector3.up, 0.15f, 0.08f);
                float fase = r.NextFloat();
                EmitirRama(m, new Rama { eje = c1, radioBase = radioBase * 0.55f, radioPunta = 0.09f, flexBase = FlexTronco(c1[0].y, alto) * 0.7f, fase = fase, nivel = 1 },
                           detalle == 0 ? 8 : 5, alto, 1.6f);
                int sec = detalle == 2 ? 1 : r.NextInt(2, 4);
                for (int s = 0; s < sec; s++)
                {
                    int idx = Mathf.Clamp((int)(c1.Count * r.NextFloat(0.4f, 0.9f)), 1, c1.Count - 1);
                    var o = c1[idx];
                    var d2 = (dir + (Vector3)r.NextFloat3Direction() * 0.6f + Vector3.up * 0.2f).normalized;
                    var c2 = Curva(ref r, o, d2, r.NextFloat(3f, 6f), detalle == 0 ? 4 : 2, Vector3.up, 0.15f, 0.12f);
                    float flex = FlexTronco(o.y, alto);
                    if (detalle < 2)
                        EmitirRama(m, new Rama { eje = c2, radioBase = 0.08f, radioPunta = 0.025f, flexBase = flex, fase = r.NextFloat(), nivel = 2 }, 4, alto, 1f);
                    puntas.Add((c2[c2.Count - 1], d2, r.NextFloat(), flex));
                }
                puntas.Add((c1[c1.Count - 1], dir, fase, FlexTronco(c1[c1.Count - 1].y, alto)));
            }

            // Copa redondeada: ramilletes en una nube alrededor de las puntas.
            int porPunta = detalle == 0 ? 14 : detalle == 1 ? 6 : 3;
            float tam = detalle == 0 ? 2.8f : detalle == 1 ? 3.8f : 6.5f;
            foreach (var pu in puntas)
            {
                for (int k = 0; k < porPunta; k++)
                {
                    var off = (Vector3)r.NextFloat3Direction() * r.NextFloat(0.3f, 3.2f);
                    var basep = pu.p + off;
                    var desdeCentro = (basep - centroCopa).normalized;
                    var dir = (desdeCentro + Vector3.up * 0.4f + (Vector3)r.NextFloat3Direction() * 0.6f).normalized;
                    float t = tam * r.NextFloat(0.8f, 1.25f);
                    m.Tarjeta(SubHojas, basep, dir, (Vector3)r.NextFloat3Direction(), t, t, Celda2x2(r.NextInt(4)),
                        r.NextFloat(0.1f, 0.4f), Pesos(pu.flex, 0.75f, pu.fase), centroCopa);
                }
            }
        }

        // ================================================================== Palmera cocotera
        static void Palmera(ConstructorMalla m, ref Random r, int detalle)
        {
            float alto = r.NextFloat(10f, 18f);
            float az = r.NextFloat(0f, Mathf.PI * 2f);
            Vector3 inclinacion = new Vector3(Mathf.Cos(az), 0f, Mathf.Sin(az)) * r.NextFloat(0.15f, 0.55f);
            int anillos = detalle == 0 ? 22 : detalle == 1 ? 10 : 4;
            var eje = new List<Vector3>();
            for (int i = 0; i <= anillos; i++)
            {
                float t = i / (float)anillos;
                // Curva de palmera: se inclina pronto y endereza arriba.
                eje.Add(Vector3.up * (alto * t) + inclinacion * alto * (t * 0.9f - t * t * 0.45f));
            }
            float fase = r.NextFloat();
            m.Tubo(SubMadera, eje, (i, a) =>
            {
                float t = i / (float)anillos;
                float rad = Mathf.Lerp(0.2f, 0.15f, t) + 0.16f * Mathf.Exp(-t * 18f) + 0.05f * Mathf.Exp(-(1f - t) * 25f);
                return rad;
            }, detalle == 0 ? 12 : detalle == 1 ? 7 : 5, 1f, 1f, i => Pesos(FlexTronco(eje[i].y, alto), 0f, fase));

            Vector3 cima = eje[eje.Count - 1];
            var centro = cima + Vector3.up * 0.5f;
            int frondas = detalle == 0 ? r.NextInt(15, 20) : detalle == 1 ? 11 : 7;
            int tramos = detalle == 0 ? 10 : detalle == 1 ? 6 : 3;
            for (int k = 0; k < frondas; k++)
            {
                float u = k / (float)frondas;
                float azF = k * 2.39996f + r.NextFloat(-0.2f, 0.2f);
                // Las de dentro apuntan arriba; las viejas cuelgan secas.
                bool vieja = u > 0.8f && detalle < 2;
                float elev = vieja ? r.NextFloat(-0.9f, -0.4f) : Mathf.Lerp(1.05f, 0.05f, u) + r.NextFloat(-0.1f, 0.1f);
                float largo = r.NextFloat(4.2f, 5.6f) * (vieja ? 0.85f : 1f);
                float curva = vieja ? 0.4f : r.NextFloat(0.8f, 1.4f);
                m.Fronda(SubHojas, cima + Vector3.down * 0.2f, azF, elev, curva, largo,
                    t => 0.72f * Mathf.Sin(Mathf.Clamp01(t * 1.05f) * Mathf.PI) * (0.35f + 0.65f * Mathf.Sqrt(t)) + 0.02f,
                    vieja ? 0.1f : r.NextFloat(0.25f, 0.5f), Columna(vieja ? 1 : 0),
                    Pesos(1f, 0.15f, r.NextFloat()), centro, tramos, r.NextFloat(-0.4f, 0.4f));
            }
        }

        // ================================================================== Helecho arboreo
        static void HelechoArboreo(ConstructorMalla m, ref Random r, int detalle)
        {
            float alto = r.NextFloat(3.5f, 7f);
            var eje = Curva(ref r, Vector3.zero, new Vector3(r.NextFloat(-0.15f, 0.15f), 1f, r.NextFloat(-0.15f, 0.15f)), alto, detalle == 0 ? 8 : 3, Vector3.up, 0.25f, 0.05f);
            float fase = r.NextFloat();
            m.Tubo(SubMadera, eje, (i, a) => 0.17f + 0.06f * Mathf.Exp(-eje[i].y * 1.5f) + 0.012f * Mathf.Sin(a * 7f + i * 2f),
                detalle == 0 ? 10 : 5, 1.2f, 1f, i => Pesos(FlexTronco(eje[i].y, alto), 0f, fase));
            Vector3 cima = eje[eje.Count - 1];
            int frondas = detalle == 0 ? r.NextInt(11, 16) : detalle == 1 ? 8 : 5;
            for (int k = 0; k < frondas; k++)
            {
                float u = k / (float)frondas;
                m.Fronda(SubHojas, cima, k * 2.39996f + r.NextFloat(-0.2f, 0.2f), Mathf.Lerp(0.95f, 0.15f, u) + r.NextFloat(-0.1f, 0.1f),
                    r.NextFloat(1.0f, 1.7f), r.NextFloat(2.4f, 3.4f),
                    t => 0.55f * Mathf.Sin(Mathf.Clamp01(t) * Mathf.PI) * (0.4f + 0.6f * t) + 0.015f,
                    r.NextFloat(0.05f, 0.25f), Columna(r.NextInt(2)), Pesos(1f, 0.2f, r.NextFloat()), cima + Vector3.up * 0.4f,
                    detalle == 0 ? 9 : 4, r.NextFloat(-0.3f, 0.3f));
            }
        }

        // ================================================================== Platanera
        static void Platanera(ConstructorMalla m, ref Random r, int detalle)
        {
            float alto = r.NextFloat(2.6f, 4.2f);
            var eje = Curva(ref r, Vector3.zero, new Vector3(r.NextFloat(-0.06f, 0.06f), 1f, r.NextFloat(-0.06f, 0.06f)), alto, detalle == 0 ? 6 : 2, Vector3.up, 0.1f, 0.02f);
            float fase = r.NextFloat();
            m.Tubo(SubMadera, eje, (i, a) => Mathf.Lerp(0.17f, 0.11f, i / (float)(eje.Count - 1)),
                detalle == 0 ? 10 : 5, 1.5f, 1f, i => Pesos(FlexTronco(eje[i].y, alto) * 0.8f, 0f, fase));
            Vector3 cima = eje[eje.Count - 1];
            int hojas = detalle == 0 ? r.NextInt(8, 12) : detalle == 1 ? 6 : 4;
            for (int k = 0; k < hojas; k++)
            {
                float u = k / (float)hojas;
                bool rota = r.NextFloat() < 0.4f;
                m.Fronda(SubHojas, cima + Vector3.up * r.NextFloat(-0.4f, 0.2f), k * 2.39996f + r.NextFloat(-0.3f, 0.3f),
                    Mathf.Lerp(1.15f, 0.35f, u) + r.NextFloat(-0.1f, 0.1f), r.NextFloat(0.9f, 1.6f), r.NextFloat(2.0f, 3.0f),
                    t => t < 0.12f ? 0.03f : 0.36f * Mathf.Pow(Mathf.Max(0f, Mathf.Sin(Mathf.Clamp01((t - 0.06f) / 0.94f) * Mathf.PI)), 0.45f) + 0.02f,
                    r.NextFloat(0.05f, 0.2f), Columna(rota ? 1 : 0), Pesos(1f, 0.35f, r.NextFloat()), cima,
                    detalle == 0 ? 10 : 4, r.NextFloat(-0.6f, 0.6f));
            }
        }

        // ================================================================== Bambu
        static void Bambu(ConstructorMalla m, ref Random r, int detalle)
        {
            int canas = detalle == 0 ? r.NextInt(18, 32) : detalle == 1 ? 12 : 6;
            float altoMax = r.NextFloat(10f, 16f);
            var centro = Vector3.up * altoMax * 0.7f;
            for (int k = 0; k < canas; k++)
            {
                var pie = (Vector3)(r.NextFloat2Direction().x0y()) * r.NextFloat(0f, 1.6f);
                float alto = altoMax * r.NextFloat(0.65f, 1f);
                var salida = (Vector3.up + new Vector3(pie.x, 0f, pie.z) * 0.25f + (Vector3)r.NextFloat3Direction() * 0.05f).normalized;
                var eje = Curva(ref r, pie, salida, alto, detalle == 0 ? 10 : 4, (new Vector3(pie.x, 0f, pie.z).normalized + Vector3.down * 0.3f).normalized, r.NextFloat(0.2f, 0.6f), 0.01f);
                float radio = r.NextFloat(0.045f, 0.075f);
                float fase = r.NextFloat();
                if (detalle < 2 || k % 2 == 0)
                    m.Tubo(SubMadera, eje, (i, a) => radio * (1f - 0.35f * i / (float)(eje.Count - 1)), detalle == 0 ? 7 : 4, 0.42f, 1f,
                        i => Pesos(FlexTronco(eje[i].y, alto), 0f, fase));
                // Ramilletes de hojas en la mitad alta.
                int ramilletes = detalle == 0 ? 9 : detalle == 1 ? 4 : 2;
                for (int s = 0; s < ramilletes; s++)
                {
                    float t = Mathf.Lerp(0.45f, 1f, s / (float)Mathf.Max(1, ramilletes - 1));
                    int idx = Mathf.Clamp((int)(t * (eje.Count - 1)), 0, eje.Count - 1);
                    var o = eje[idx];
                    var d = (DirDesdeAngulos(r.NextFloat(0f, 6.283f), r.NextFloat(-0.2f, 0.5f))).normalized;
                    float tam = detalle == 2 ? 2.4f : r.NextFloat(1.2f, 1.7f);
                    m.Tarjeta(SubHojas, o, d, Vector3.up, tam, tam, Celda2x2(r.NextInt(4)), r.NextFloat(0.2f, 0.5f),
                        Pesos(FlexTronco(o.y, alto), 0.85f, fase), centro, 0.5f);
                }
            }
        }

        // ================================================================== Arbusto
        static void Arbusto(ConstructorMalla m, ref Random r, int detalle)
        {
            float alto = r.NextFloat(1.6f, 3.4f);
            var centro = Vector3.up * alto * 0.6f;
            int tallos = detalle == 2 ? 3 : r.NextInt(4, 8);
            var puntas = new List<Vector3>();
            for (int k = 0; k < tallos; k++)
            {
                var d = (Vector3.up * 1.6f + (Vector3)r.NextFloat2Direction().x0y()).normalized;
                var eje = Curva(ref r, Vector3.zero, d, alto * r.NextFloat(0.6f, 0.95f), detalle == 0 ? 4 : 2, Vector3.up, 0.2f, 0.15f);
                if (detalle < 2)
                    m.Tubo(SubMadera, eje, (i, a) => Mathf.Lerp(0.035f, 0.01f, i / (float)(eje.Count - 1)), 4, 0.8f, 1f,
                        i => Pesos(FlexTronco(eje[i].y, alto), i / (float)(eje.Count - 1) * 0.5f, 0.3f));
                puntas.Add(eje[eje.Count - 1]);
                puntas.Add(eje[eje.Count / 2]);
            }
            int porPunta = detalle == 0 ? 6 : detalle == 1 ? 3 : 2;
            float tam = detalle == 0 ? 1.1f : detalle == 1 ? 1.5f : 2.2f;
            foreach (var p in puntas)
                for (int k = 0; k < porPunta; k++)
                {
                    var basep = p + (Vector3)r.NextFloat3Direction() * 0.35f;
                    var dir = ((basep - centro).normalized + Vector3.up * 0.5f + (Vector3)r.NextFloat3Direction() * 0.6f).normalized;
                    float t = tam * r.NextFloat(0.8f, 1.2f);
                    m.Tarjeta(SubHojas, basep, dir, (Vector3)r.NextFloat3Direction(), t, t, Celda2x2(r.NextInt(4)), r.NextFloat(0.1f, 0.4f),
                        Pesos(FlexTronco(basep.y, alto), 0.9f, r.NextFloat()), centro);
                }
        }

        // ================================================================== Helecho de suelo
        static void Helecho(ConstructorMalla m, ref Random r, int detalle)
        {
            int frondas = detalle == 0 ? r.NextInt(12, 22) : detalle == 1 ? 8 : 5;
            float largo = r.NextFloat(0.9f, 1.6f);
            var centro = Vector3.up * 0.3f;
            for (int k = 0; k < frondas; k++)
            {
                float u = k / (float)frondas;
                m.Fronda(SubHojas, Vector3.zero, k * 2.39996f + r.NextFloat(-0.2f, 0.2f), Mathf.Lerp(1.2f, 0.45f, u) + r.NextFloat(-0.1f, 0.1f),
                    r.NextFloat(1.2f, 2.0f), largo * r.NextFloat(0.7f, 1.1f),
                    t => 0.2f * Mathf.Sin(Mathf.Clamp01(t * 1.05f) * Mathf.PI) * (0.35f + 0.65f * t) + 0.01f,
                    r.NextFloat(0.05f, 0.2f), Columna(r.NextInt(2)), Pesos(0f, 0.2f, r.NextFloat()), centro,
                    detalle == 0 ? 7 : 3, r.NextFloat(-0.4f, 0.4f), 0.5f);
            }
        }

        // ================================================================== Hojas grandes (oreja de elefante / monstera)
        static void HojasGrandes(ConstructorMalla m, ref Random r, int detalle)
        {
            int hojas = detalle == 0 ? r.NextInt(5, 9) : detalle == 1 ? 4 : 3;
            bool monstera = r.NextFloat() < 0.4f;
            var centro = Vector3.up * 0.8f;
            for (int k = 0; k < hojas; k++)
            {
                float az = k * 2.39996f + r.NextFloat(-0.3f, 0.3f);
                float largoPeciolo = r.NextFloat(0.7f, 1.7f);
                var dir = DirDesdeAngulos(az, r.NextFloat(0.9f, 1.3f));
                var peciolo = Curva(ref r, Vector3.zero, dir, largoPeciolo, detalle == 0 ? 4 : 2, Vector3.down, 0.4f, 0.03f);
                float fase = r.NextFloat();
                if (detalle < 2)
                    m.Tubo(SubMadera, peciolo, (i, a) => Mathf.Lerp(0.025f, 0.012f, i / (float)(peciolo.Count - 1)), 4, 0.5f, 1f,
                        i => Pesos(0f, i / (float)(peciolo.Count - 1) * 0.6f, fase));
                var punta = peciolo[peciolo.Count - 1];
                float tam = r.NextFloat(0.7f, 1.35f);
                // La lamina cuelga del peciolo: la punta mira hacia fuera y abajo.
                var dirHoja = (new Vector3(dir.x, 0f, dir.z).normalized + Vector3.down * r.NextFloat(0.2f, 0.7f)).normalized;
                int celda = (monstera ? 2 : 0) + r.NextInt(2);
                m.Tarjeta(SubHojas, punta - dirHoja * tam * 0.1f, dirHoja, Vector3.up, tam, tam * 0.92f, Celda2x2(celda), 0.15f,
                    Pesos(0f, 0.6f, fase), centro, 0.25f);
            }
        }

        // ================================================================== Hierba alta
        static void Hierba(ConstructorMalla m, ref Random r, int detalle)
        {
            int tarjetas = detalle == 0 ? r.NextInt(5, 9) : 3;
            float alto = r.NextFloat(0.9f, 1.7f);
            var centro = Vector3.up * alto * 0.4f;
            for (int k = 0; k < tarjetas; k++)
            {
                float az = k / (float)tarjetas * Mathf.PI + r.NextFloat(-0.3f, 0.3f);
                var lado = new Vector3(Mathf.Cos(az), 0f, Mathf.Sin(az));
                var dir = (Vector3.up + (Vector3)r.NextFloat2Direction().x0y() * 0.25f).normalized;
                int c = r.NextInt(4);
                var uv = new Rect(c * 0.25f + 0.003f, 0.003f, 0.244f, 0.994f);
                m.Tarjeta(SubHojas, (Vector3)r.NextFloat2Direction().x0y() * 0.15f, dir, Vector3.Cross(lado, Vector3.up), alto * r.NextFloat(0.8f, 1.1f), alto * 0.55f,
                    uv, r.NextFloat(0.05f, 0.25f), Pesos(0f, 0.4f, r.NextFloat()), centro, 0.35f);
            }
        }

        // ================================================================== Herbazal
        /// <summary>
        /// Un trozo de herbazal cerrado (capim, canarana): varias matas de hierba alta repartidas en un circulo de algo
        /// mas de un metro, para que plantadas a unos dos metros se toquen y no se vea el suelo entre ellas.
        /// </summary>
        static void Herbazal(ConstructorMalla m, ref Random r, int detalle)
        {
            int matas = detalle == 0 ? r.NextInt(6, 9) : 3;
            float alto = r.NextFloat(1.1f, 1.6f);
            var centro = Vector3.up * alto * 0.45f;
            for (int j = 0; j < matas; j++)
            {
                // La primera en el centro; las demas, en el circulo (de lejos, menos y mas anchas).
                Vector3 donde = j == 0 ? Vector3.zero : (Vector3)(r.NextFloat2Direction() * math.sqrt(r.NextFloat(0.15f, 1f)) * 1.1f).x0y();
                int tarjetas = detalle == 0 ? r.NextInt(3, 5) : 4;
                float altoMata = alto * r.NextFloat(0.8f, 1.1f);
                for (int k = 0; k < tarjetas; k++)
                {
                    float az = k / (float)tarjetas * Mathf.PI + r.NextFloat(-0.4f, 0.4f);
                    var lado = new Vector3(Mathf.Cos(az), 0f, Mathf.Sin(az));
                    var dir = (Vector3.up + (Vector3)r.NextFloat2Direction().x0y() * 0.3f).normalized;
                    int c = r.NextInt(4);
                    var uv = new Rect(c * 0.25f + 0.003f, 0.003f, 0.244f, 0.994f);
                    m.Tarjeta(SubHojas, donde + (Vector3)r.NextFloat2Direction().x0y() * 0.12f, dir, Vector3.Cross(lado, Vector3.up),
                        altoMata * r.NextFloat(0.85f, 1.1f), altoMata * (detalle == 0 ? 0.55f : 0.8f),
                        uv, r.NextFloat(0.08f, 0.28f), Pesos(0f, 0.45f, r.NextFloat()), centro, 0.3f);
                }
            }
        }

        // ================================================================== Flores (heliconias, bromelias, jengibre)
        static void Flores(ConstructorMalla m, ref Random r, int detalle)
        {
            int n = detalle == 0 ? r.NextInt(3, 6) : 2;
            float alto = r.NextFloat(0.8f, 1.6f);
            var centro = Vector3.up * alto * 0.5f;
            int tipo = r.NextInt(4);
            for (int k = 0; k < n; k++)
            {
                var dir = (Vector3.up * 2f + (Vector3)r.NextFloat2Direction().x0y()).normalized;
                float t = alto * r.NextFloat(0.7f, 1.1f);
                m.Tarjeta(SubHojas, (Vector3)r.NextFloat2Direction().x0y() * 0.2f, dir, (Vector3)r.NextFloat2Direction().x0y(), t, t * 0.8f,
                    Celda2x2(tipo == 1 && k > 0 ? r.NextInt(4) : tipo), 0.1f, Pesos(0f, 0.5f, r.NextFloat()), centro, 0.3f);
            }
        }

        // ================================================================== Roca
        static void Roca(ConstructorMalla m, ref Random r, int detalle)
        {
            int sub = detalle == 0 ? 3 : detalle == 1 ? 2 : 1;
            var (verts, tris) = Icosfera(sub);
            float semilla = r.NextFloat(0f, 100f);
            var escala = new Vector3(r.NextFloat(1f, 1.6f), r.NextFloat(0.55f, 0.9f), r.NextFloat(0.8f, 1.3f));
            var inicio = m.Cuenta;
            var desplazados = new Vector3[verts.Count];
            for (int i = 0; i < verts.Count; i++)
            {
                var v = verts[i];
                float n = Mundo.Ruido.Fbm(new float2(v.x * 1.7f + semilla, v.z * 1.7f + v.y * 0.9f), 4) * 0.22f
                        + Mundo.Ruido.Crestas(new float2(v.x * 2.4f - semilla, v.y * 2.4f + v.z), 3) * 0.18f;
                var p = Vector3.Scale(v * (1f + n), escala);
                // Asentada: el tercio inferior se aplana y se hunde.
                if (p.y < -0.25f) p.y = Mathf.Lerp(p.y, -0.25f - (p.y + 0.25f) * 0.25f, 0.8f);
                desplazados[i] = p;
            }
            // Normales por triangulo acumuladas (facetado suave de roca).
            var normales = new Vector3[verts.Count];
            for (int t = 0; t < tris.Count; t += 3)
            {
                var a = desplazados[tris[t]]; var b = desplazados[tris[t + 1]]; var c = desplazados[tris[t + 2]];
                var n = Vector3.Cross(b - a, c - a);
                normales[tris[t]] += n; normales[tris[t + 1]] += n; normales[tris[t + 2]] += n;
            }
            for (int i = 0; i < verts.Count; i++)
                m.V(desplazados[i] + Vector3.up * 0.25f, normales[i].normalized, new Vector2(desplazados[i].x, desplazados[i].z), new Color(0f, 0f, 0f, 0f));
            for (int t = 0; t < tris.Count; t += 3)
                m.Tri(0, inicio + tris[t], inicio + tris[t + 1], inicio + tris[t + 2]);
        }

        static (List<Vector3>, List<int>) Icosfera(int subdivisiones)
        {
            float t = (1f + Mathf.Sqrt(5f)) / 2f;
            var v = new List<Vector3>
            {
                new Vector3(-1, t, 0), new Vector3(1, t, 0), new Vector3(-1, -t, 0), new Vector3(1, -t, 0),
                new Vector3(0, -1, t), new Vector3(0, 1, t), new Vector3(0, -1, -t), new Vector3(0, 1, -t),
                new Vector3(t, 0, -1), new Vector3(t, 0, 1), new Vector3(-t, 0, -1), new Vector3(-t, 0, 1),
            };
            for (int i = 0; i < v.Count; i++) v[i] = v[i].normalized;
            var f = new List<int> { 0,11,5, 0,5,1, 0,1,7, 0,7,10, 0,10,11, 1,5,9, 5,11,4, 11,10,2, 10,7,6, 7,1,8,
                                    3,9,4, 3,4,2, 3,2,6, 3,6,8, 3,8,9, 4,9,5, 2,4,11, 6,2,10, 8,6,7, 9,8,1 };
            for (int s = 0; s < subdivisiones; s++)
            {
                var cache = new Dictionary<long, int>();
                int Medio(int a, int b)
                {
                    long k = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
                    if (cache.TryGetValue(k, out int idx)) return idx;
                    v.Add(((v[a] + v[b]) * 0.5f).normalized);
                    cache[k] = v.Count - 1;
                    return v.Count - 1;
                }
                var nf = new List<int>(f.Count * 4);
                for (int i = 0; i < f.Count; i += 3)
                {
                    int a = f[i], b = f[i + 1], c = f[i + 2];
                    int ab = Medio(a, b), bc = Medio(b, c), ca = Medio(c, a);
                    nf.AddRange(new[] { a, ab, ca, b, bc, ab, c, ca, bc, ab, bc, ca });
                }
                f = nf;
            }
            // Asi, con las caras hacia fuera: Vector3.Cross(b - a, c - a) mira afuera, que es la cara
            // que pinta Unity (la misma regla que los tubos). Antes se les daba la vuelta "porque Unity
            // quiere horario" y las rocas salian del reves: se veia su cara interior.
            return (v, f);
        }

        // ================================================================== Tronco caido
        static void TroncoCaido(ConstructorMalla m, ref Random r, int detalle)
        {
            float largo = r.NextFloat(8f, 16f);
            float radio = r.NextFloat(0.35f, 0.7f);
            var eje = new List<Vector3>();
            int tramos = detalle == 0 ? 10 : 4;
            for (int i = 0; i <= tramos; i++)
            {
                float t = i / (float)tramos;
                eje.Add(new Vector3(0f, radio * 0.8f + Mathf.Sin(t * 3.1f) * 0.15f, (t - 0.5f) * largo));
            }
            m.Tubo(SubMadera, eje, (i, a) => radio * (1f - 0.25f * i / (float)tramos) * (1f + 0.06f * Mathf.Sin(a * 5f + i)),
                detalle == 0 ? 12 : 6, 2f, Mathf.Round(radio * 6.283f / 2f), i => new Color(0f, 0f, 0f, 0f), 0f, true);
            // Algun helecho y musgo en la corteza con tarjetas de helecho bajas.
            if (detalle == 0)
                for (int k = 0; k < 4; k++)
                {
                    var p = eje[r.NextInt(eje.Count)] + Vector3.up * radio * 0.8f;
                    m.Fronda(SubHojas, p, r.NextFloat(0f, 6.28f), 0.6f, 1.4f, r.NextFloat(0.5f, 0.9f),
                        t => 0.12f * Mathf.Sin(t * Mathf.PI) + 0.01f, 0.1f, Columna(0), new Color(0.2f, 0f, 0.5f, 0f), p, 4);
                }
        }
    }

    static class ExtVec
    {
        public static float3 x0y(this float2 v) => new float3(v.x, 0f, v.y);
    }
}
