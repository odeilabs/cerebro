using Unity.Mathematics;
using UnityEngine;

namespace Guardianes.Mundo
{
    /// <summary>
    /// Consultas del mapa para cualquier sistema (criaturas, humanos, vehiculos, efectos, sonido):
    /// medidas, altura del suelo, agua y tierra firme. Lee los datos del TerrenoMundo cargado; sin
    /// terreno responde con el suelo a y = 0 y sin agua.
    /// </summary>
    public static class Mapa
    {
        /// <summary>Las medidas del mundo cargado (las pone TerrenoMundo al leer sus datos).</summary>
        public static MedidasMundo Medidas = MedidasMundo.PorDefecto;

        public static DatosMundo Datos => TerrenoMundo.Instancia != null ? TerrenoMundo.Instancia.Datos : null;

        /// <summary>Altura del suelo (m) en x, z (el fondo, si hay agua).</summary>
        public static float Altura(float x, float z)
        {
            var d = Datos;
            return d != null ? d.Altura(new float2(x, z)) : 0f;
        }

        public static float Altura(Vector3 p) => Altura(p.x, p.z);

        public static Vector3 AlSuelo(Vector3 p)
        {
            p.y = Altura(p.x, p.z);
            return p;
        }

        /// <summary>Cota de la superficie del agua en x, z (rio, lago o mar) o NaN si alli no hay agua.</summary>
        public static float Agua(float x, float z)
        {
            var d = Datos;
            if (d == null) return Medidas.hayMar ? Medidas.nivelMar : float.NaN;
            float nivel = d.NivelAgua(new float2(x, z));
            // El agua que queda por debajo del suelo (bordes del mapa de agua) no cuenta.
            return !float.IsNaN(nivel) && nivel > d.Altura(new float2(x, z)) ? nivel : float.NaN;
        }

        /// <summary>Profundidad del agua (m) en x, z; 0 en seco.</summary>
        public static float Profundidad(float x, float z)
        {
            float a = Agua(x, z);
            return float.IsNaN(a) ? 0f : Mathf.Max(0f, a - Altura(x, z));
        }

        /// <summary>Lo que hay encima en x, z: el agua si la hay, si no el suelo.</summary>
        public static float Superficie(float x, float z)
        {
            float a = Agua(x, z);
            float s = Altura(x, z);
            return float.IsNaN(a) ? s : Mathf.Max(a, s);
        }

        /// <summary>Suelo firme: sin agua (o con menos de 'vadeo' metros de agua).</summary>
        public static bool EsTierra(Vector3 p, float vadeo = 0.15f) => Profundidad(p.x, p.z) <= vadeo;

        /// <summary>Normal del suelo en x, z (diferencias centradas a 'paso' metros).</summary>
        public static Vector3 Normal(float x, float z, float paso = 1f)
        {
            float hx = Altura(x + paso, z) - Altura(x - paso, z);
            float hz = Altura(x, z + paso) - Altura(x, z - paso);
            return new Vector3(-hx, 2f * paso, -hz).normalized;
        }

        /// <summary>Pendiente del suelo (grados) en x, z.</summary>
        public static float Pendiente(float x, float z, float paso = 1f) => Vector3.Angle(Vector3.up, Normal(x, z, paso));

        /// <summary>Dentro del mapa con un margen (m).</summary>
        public static bool Dentro(Vector3 p, float margen = 0f)
        {
            float m = Medidas.tamano * 0.5f - margen;
            return Mathf.Abs(p.x) <= m && Mathf.Abs(p.z) <= m;
        }

        /// <summary>Contaminacion del suelo (0..1).</summary>
        public static float Contaminacion(float x, float z)
        {
            var d = Datos;
            return d != null ? d.Mascara(d.contaminacion, new float2(x, z)) : 0f;
        }
    }
}
