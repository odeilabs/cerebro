using Unity.Mathematics;

namespace Guardianes.Mundo
{
    /// <summary>
    /// Un lago (o una cocha, o una balsa): centro, cota del agua y la orilla como radios en N
    /// direcciones equiespaciadas (la primera hacia +X, en sentido antihorario visto desde arriba).
    /// La orilla no es redonda: el generador del relieve la talla y el agua la sigue.
    /// </summary>
    [System.Serializable]
    public struct Lago
    {
        public string nombre;
        public float2 centro;
        public float nivel;
        public float[] radios;
        /// <summary>Profundidad maxima en el centro (m).</summary>
        public float profundidad;

        public bool Valido => radios != null && radios.Length >= 3;

        /// <summary>Radio de la orilla en la direccion dir (no hace falta normalizarla).</summary>
        public float RadioEn(float2 dir)
        {
            if (!Valido) return 0f;
            float a = math.atan2(dir.y, dir.x);
            if (a < 0f) a += math.PI * 2f;
            float f = a / (math.PI * 2f) * radios.Length;
            int i0 = (int)math.floor(f) % radios.Length;
            int i1 = (i0 + 1) % radios.Length;
            return math.lerp(radios[i0], radios[i1], f - math.floor(f));
        }

        public float RadioMaximo
        {
            get
            {
                float m = 0f;
                if (radios != null) foreach (var r in radios) m = math.max(m, r);
                return m;
            }
        }

        /// <summary>0 en el centro, 1 en la orilla, mas de 1 fuera.</summary>
        public float Relativa(float2 xz)
        {
            float2 d = xz - centro;
            float dist = math.length(d);
            float r = RadioEn(dist > 1e-3f ? d : new float2(1f, 0f));
            return r > 0f ? dist / r : float.MaxValue;
        }

        public bool Contiene(float2 xz, float margen = 1f) => Relativa(xz) < margen;

        /// <summary>Lago con la orilla que da una funcion del angulo (radianes) en n direcciones.</summary>
        public static Lago DesdeFuncion(string nombre, float2 centro, float nivel, float profundidad, System.Func<float2, float> radioEn, int n = 96)
        {
            var radios = new float[n];
            for (int i = 0; i < n; i++)
            {
                float a = i / (float)n * math.PI * 2f;
                radios[i] = radioEn(new float2(math.cos(a), math.sin(a)));
            }
            return new Lago { nombre = nombre, centro = centro, nivel = nivel, profundidad = profundidad, radios = radios };
        }
    }
}
