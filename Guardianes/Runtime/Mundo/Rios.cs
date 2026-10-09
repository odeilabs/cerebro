using System.Collections.Generic;
using Unity.Mathematics;

namespace Guardianes.Mundo
{
    /// <summary>
    /// Trazado denso de los rios a partir de sus puntos de control (x, z, cota del agua): curva
    /// Catmull-Rom con meandros (desplazamiento lateral por ruido) salvo cerca de los anclajes
    /// (cascadas, lagos, confluencias, desembocadura), cota interpolada por tramos (los saltos se
    /// conservan) y ancho y profundidad crecientes rio abajo. Lo usan el tallado del relieve y las
    /// mallas de agua, asi que el cauce y el agua coinciden siempre. Cada juego pone sus rios.
    /// </summary>
    public static class Rios
    {
        public struct Punto
        {
            public float2 xz;
            public float nivel;
            public float semiancho;
            public float profundidad;
            /// <summary>Distancia recorrida desde el nacimiento (m).</summary>
            public float s;
            /// <summary>0 = nacimiento, 1 = final.</summary>
            public float avance;
            /// <summary>Tramo de cascada (caida casi vertical).</summary>
            public bool cascada;
        }

        /// <summary>Forma de un rio: meandros, ancho y profundidad del nacimiento al final.</summary>
        public struct Forma
        {
            /// <summary>Amplitud lateral de los meandros (m).</summary>
            public float amplitud;
            /// <summary>Longitud de onda base de los meandros (m): la frecuencia es 1/esto.</summary>
            public float ondaMeandro;
            public float2 semiancho;    // nacimiento, final
            public float2 profundidad;  // nacimiento, final
            /// <summary>Paso del trazado denso (m).</summary>
            public float paso;
            /// <summary>Distancia a un anclaje en la que se apagan los meandros (m).</summary>
            public float calmaAnclaje;

            /// <summary>El rio principal de la isla de Kong.</summary>
            public static Forma Principal => new Forma
            {
                amplitud = 58f, ondaMeandro = 238f, semiancho = new float2(12f, 34f), profundidad = new float2(3.2f, 6.5f), paso = 6f, calmaAnclaje = 160f,
            };

            /// <summary>Un afluente de la isla de Kong.</summary>
            public static Forma Afluente => new Forma
            {
                amplitud = 34f, ondaMeandro = 238f, semiancho = new float2(7f, 14f), profundidad = new float2(2.4f, 2.4f), paso = 6f, calmaAnclaje = 160f,
            };
        }

        /// <summary>
        /// Trazado denso de un rio. control: (x, z, cota). anclajes: indices de control donde el cauce
        /// no se mueve. Un salto de cota de mas de 20 m entre dos puntos de control es una cascada.
        /// </summary>
        public static Punto[] Trazar(float3[] control, Forma forma, int semilla, int[] anclajes)
        {
            int n = control.Length;
            float paso = forma.paso > 0f ? forma.paso : 6f;

            // Longitud acumulada de los puntos de control (para los anclajes).
            var sControl = new float[n];
            for (int k = 1; k < n; k++) sControl[k] = sControl[k - 1] + math.distance(control[k - 1].xy, control[k].xy);

            var puntos = new List<Punto>();
            float s = 0f;
            float2 previo = control[0].xy;
            for (int k = 0; k < n - 1; k++)
            {
                float2 p0 = control[math.max(k - 1, 0)].xy, p1 = control[k].xy, p2 = control[k + 1].xy, p3 = control[math.min(k + 2, n - 1)].xy;
                bool salto = control[k].z - control[k + 1].z > 20f;
                float largo = math.distance(p1, p2);
                int m = math.max(1, (int)math.ceil(largo / paso));
                for (int t = 0; t < m; t++)
                {
                    float u = t / (float)m;
                    float2 xz = salto ? math.lerp(p1, p2, u) : CatmullRom(p0, p1, p2, p3, u);
                    s += math.distance(previo, xz);
                    previo = xz;
                    puntos.Add(new Punto
                    {
                        xz = xz,
                        nivel = math.lerp(control[k].z, control[k + 1].z, u),
                        s = s,
                        cascada = salto,
                    });
                }
            }
            puntos.Add(new Punto { xz = control[n - 1].xy, nivel = control[n - 1].z, s = s + math.distance(previo, control[n - 1].xy) });

            float total = puntos[puntos.Count - 1].s;
            float frecuencia = 1f / math.max(forma.ondaMeandro, 1f);
            var r = new Punto[puntos.Count];
            for (int i = 0; i < r.Length; i++)
            {
                var p = puntos[i];
                float2 a = puntos[math.max(i - 1, 0)].xz, b = puntos[math.min(i + 1, r.Length - 1)].xz;
                float2 tang = math.normalizesafe(b - a, new float2(1, 0));
                float2 normal = new float2(-tang.y, tang.x);

                // Meandros: apagados cerca de los anclajes y en tramos empinados.
                float mascara = 1f;
                if (anclajes != null)
                    foreach (int ia in anclajes)
                        mascara = math.min(mascara, Ruido.SmoothStep(0f, forma.calmaAnclaje, math.abs(p.s - sControl[ia] * (total / math.max(sControl[n - 1], 1f)))));
                float pendiente = math.abs(puntos[math.min(i + 1, r.Length - 1)].nivel - puntos[math.max(i - 1, 0)].nivel) / math.max(1f, math.distance(a, b));
                mascara *= 1f - Ruido.SmoothStep(0.03f, 0.12f, pendiente);
                float onda = Ruido.Fbm(new float2(p.s * frecuencia, semilla * 0.37f), 3) + 0.3f * noise.snoise(new float2(p.s * frecuencia * 3.1f, semilla * 1.3f));
                p.xz += normal * onda * forma.amplitud * mascara;

                p.avance = p.s / math.max(total, 1f);
                p.semiancho = math.lerp(forma.semiancho.x, forma.semiancho.y, p.avance);
                p.profundidad = math.lerp(forma.profundidad.x, forma.profundidad.y, p.avance);
                if (p.cascada) p.semiancho *= 0.8f;
                r[i] = p;
            }
            return r;
        }

        /// <summary>Cota del agua del rio en el punto mas cercano a xz.</summary>
        public static float NivelEn(Punto[] rio, float2 xz)
        {
            float mejor = float.MaxValue, nivel = 0f;
            foreach (var q in rio)
            {
                float d = math.distancesq(q.xz, xz);
                if (d < mejor) { mejor = d; nivel = q.nivel; }
            }
            return nivel;
        }

        /// <summary>Distancia (m) de xz al cauce mas cercano de todos los rios (al eje).</summary>
        public static float DistanciaAlEje(Punto[][] rios, float2 xz, out Punto cercano)
        {
            float mejor = float.MaxValue;
            cercano = default;
            foreach (var rio in rios)
                for (int i = 0; i < rio.Length - 1; i++)
                {
                    float d = Ruido.DistanciaSegmento(xz, rio[i].xz, rio[i + 1].xz, out float t);
                    if (d < mejor)
                    {
                        mejor = d;
                        cercano = rio[t < 0.5f ? i : i + 1];
                    }
                }
            return mejor;
        }

        static float2 CatmullRom(float2 p0, float2 p1, float2 p2, float2 p3, float t)
        {
            float t2 = t * t, t3 = t2 * t;
            return 0.5f * (2f * p1 + (-p0 + p2) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 + (-p0 + 3f * p1 - 3f * p2 + p3) * t3);
        }
    }
}
