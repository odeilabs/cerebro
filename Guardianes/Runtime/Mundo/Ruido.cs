using Unity.Mathematics;

namespace Guardianes.Mundo
{
    /// <summary>
    /// Ruido para Burst (sin estado ni managed): fBm, crestas (ridged multifractal), deformacion de
    /// dominio y hash. Lo usan el generador de relieve (editor) y el reparto de vegetacion.
    /// </summary>
    public static class Ruido
    {
        /// <summary>fBm de simplex en [-1, 1] aproximadamente.</summary>
        public static float Fbm(float2 p, int octavas, float lacunaridad = 2.03f, float ganancia = 0.5f)
        {
            float suma = 0f, amp = 0.5f, norm = 0f;
            for (int i = 0; i < octavas; i++)
            {
                suma += amp * noise.snoise(p);
                norm += amp;
                p = p * lacunaridad + new float2(17.13f, -9.71f);
                amp *= ganancia;
            }
            return suma / norm;
        }

        /// <summary>Ridged multifractal de Musgrave en [0, 1]: crestas afiladas y valles anchos.</summary>
        public static float Crestas(float2 p, int octavas, float lacunaridad = 2.1f, float ganancia = 2.0f, float desfase = 1.0f)
        {
            float senal = desfase - math.abs(noise.snoise(p));
            senal *= senal;
            float resultado = senal, peso = 1f, frecuenciaPeso = 1f, norm = 1f;
            for (int i = 1; i < octavas; i++)
            {
                p = p * lacunaridad + new float2(-3.17f, 5.29f);
                peso = math.saturate(senal * ganancia);
                senal = desfase - math.abs(noise.snoise(p));
                senal *= senal * peso;
                frecuenciaPeso *= 0.5f;
                resultado += senal * frecuenciaPeso;
                norm += frecuenciaPeso;
            }
            return math.saturate(resultado / norm);
        }

        /// <summary>Desplazamiento de dominio (dos fBm independientes).</summary>
        public static float2 Deformar(float2 p, float escala, float amplitud, int octavas = 4)
        {
            float2 q = p * escala;
            return amplitud * new float2(Fbm(q + new float2(11.3f, 2.9f), octavas), Fbm(q + new float2(-5.7f, 41.1f), octavas));
        }

        /// <summary>Hash entero -> [0,1).</summary>
        public static float Hash01(uint x)
        {
            x ^= x >> 16; x *= 0x7feb352dU; x ^= x >> 15; x *= 0x846ca68bU; x ^= x >> 16;
            return (x & 0x00FFFFFFu) / 16777216f;
        }

        public static uint Hash(int a, int b, int c = 0)
        {
            uint h = (uint)a * 0x8da6b343u ^ (uint)b * 0xd8163841u ^ (uint)c * 0xcb1ab31fu;
            h ^= h >> 13; h *= 0x5bd1e995u; h ^= h >> 15;
            return h;
        }

        public static float SmoothStep(float a, float b, float x)
        {
            float t = math.saturate((x - a) / (b - a));
            return t * t * (3f - 2f * t);
        }

        /// <summary>Distancia de p al segmento ab y parametro t (0..1) del punto mas cercano.</summary>
        public static float DistanciaSegmento(float2 p, float2 a, float2 b, out float t)
        {
            float2 ab = b - a;
            float l2 = math.max(math.lengthsq(ab), 1e-6f);
            t = math.saturate(math.dot(p - a, ab) / l2);
            return math.distance(p, a + ab * t);
        }
    }
}
