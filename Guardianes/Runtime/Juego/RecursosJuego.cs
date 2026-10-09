using Guardianes.Enemigos;
using Guardianes.Mundo;
using UnityEngine;

namespace Guardianes.Juego
{
    /// <summary>
    /// Lo que las campanas y los eventos sacan de la escena: materiales de las instalaciones y
    /// prefabs del mercenario y del helicoptero (los tienen las zonas humanas, aunque esten apagadas),
    /// y sitios de suelo firme y llano donde levantar cosas.
    /// </summary>
    public static class RecursosJuego
    {
        static ZonaHumana Zona()
        {
            foreach (var z in Object.FindObjectsByType<ZonaHumana>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (z.materiales != null) return z;
            return null;
        }

        public static MaterialesInstalaciones Materiales() => Zona()?.materiales;

        public static GameObject PrefabHumano()
        {
            var z = Zona();
            return z != null && z.prefabsGuardia.Length > 0 ? z.prefabsGuardia[0] : null;
        }

        public static GameObject PrefabHelicoptero()
        {
            foreach (var z in Object.FindObjectsByType<ZonaHumana>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (z.prefabHelicoptero != null) return z.prefabHelicoptero;
            return null;
        }

        public static float Altura(float x, float z) => Mapa.Altura(x, z);

        public static Vector3 AlSuelo(Vector3 p)
        {
            p.y = Altura(p.x, p.z);
            return p;
        }

        /// <summary>Tierra firme: por encima del mar y fuera de rios y lagos (con un margen de orilla).</summary>
        public static bool EsTierra(Vector3 p)
        {
            var me = Mapa.Medidas;
            if (me.hayMar && Altura(p.x, p.z) < me.nivelMar + 0.8f) return false;
            if (!float.IsNaN(Mapa.Agua(p.x, p.z))) return false;
            // La orilla tampoco: el agua a unos metros.
            for (int i = 0; i < 4; i++)
            {
                Vector3 q = p + Quaternion.Euler(0f, i * 90f + 45f, 0f) * Vector3.forward * 4f;
                if (!float.IsNaN(Mapa.Agua(q.x, q.z))) return false;
            }
            return true;
        }

        /// <summary>Desnivel del terreno en un circulo (maximo menos minimo, m).</summary>
        public static float Desnivel(Vector3 c, float radio)
        {
            float min = float.MaxValue, max = float.MinValue;
            for (int i = 0; i < 9; i++)
            {
                Vector3 p = i == 0 ? c : c + Quaternion.Euler(0f, i * 45f, 0f) * Vector3.forward * radio;
                float y = Altura(p.x, p.z);
                min = Mathf.Min(min, y);
                max = Mathf.Max(max, y);
            }
            return max - min;
        }

        /// <summary>
        /// Un sitio en tierra firme (fuera del agua) y casi llano entre rMin y rMax del centro.
        /// Deterministico con la semilla; si no encuentra uno bueno devuelve el menos malo.
        /// </summary>
        public static Vector3 SitioLlano(Vector3 centro, float rMin, float rMax, float huella, int semilla, float desnivelMax = 2.5f)
        {
            var r = new System.Random(semilla);
            Vector3? mejor = null;
            float mejorD = float.MaxValue;
            // Si alrededor solo hay agua (la costa, un lago, el rio), se busca cada vez mas lejos hasta dar con tierra.
            for (int ronda = 0; ronda < 5; ronda++)
            {
                float hasta = rMax + ronda * Mathf.Max(40f, rMax);
                for (int i = 0; i < 60; i++)
                {
                    float a = (float)r.NextDouble() * Mathf.PI * 2f;
                    float d = Mathf.Lerp(rMin, hasta, Mathf.Sqrt((float)r.NextDouble()));
                    Vector3 p = AlSuelo(centro + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * d);
                    if (!EsTierra(p)) continue;
                    float des = Desnivel(p, huella);
                    if (des < desnivelMax) return p;
                    if (des < mejorD) { mejorD = des; mejor = p; }
                }
                if (mejor.HasValue) return mejor.Value;
            }
            return AlSuelo(centro);
        }
    }
}
