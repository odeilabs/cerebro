using System.IO;
using Unity.Mathematics;
using UnityEngine;

namespace Guardianes.EditorTools.Terreno
{
    /// <summary>
    /// Mapa del mundo para revisar el relieve sin abrir Unity: sombreado con luz del noroeste,
    /// tintas hipsometricas, agua, zonas humanas (rojo = contaminacion) y claros.
    /// </summary>
    public static class VistaRelieve
    {
        public static void Guardar(Relieve r, string ruta, int tam = 1024)
        {
            var tex = new Texture2D(tam, tam, TextureFormat.RGB24, false);
            var px = new Color32[tam * tam];
            float paso = (r.res - 1f) / (tam - 1f);
            float metros = r.medidas.PasoAlturas;
            float3 luz = math.normalize(new float3(-1f, 1.4f, 1f));
            var me = r.medidas;
            for (int y = 0; y < tam; y++)
            for (int x = 0; x < tam; x++)
            {
                float gi = x * paso, gj = y * paso;
                float h = r.Muestra(r.alturas, gi, gj);
                float hx = r.Muestra(r.alturas, math.min(gi + paso, r.res - 1), gj) - r.Muestra(r.alturas, math.max(gi - paso, 0), gj);
                float hz = r.Muestra(r.alturas, gi, math.min(gj + paso, r.res - 1)) - r.Muestra(r.alturas, gi, math.max(gj - paso, 0));
                float3 n = math.normalize(new float3(-hx, 2f * paso * metros, -hz));
                float sombra = math.saturate(math.dot(n, luz)) * 0.85f + 0.25f;

                float3 c = Tinta(h - (me.hayMar ? me.nivelMar : 0f));
                float agua = r.Muestra(r.nivelAgua, gi, gj);
                if (me.hayMar && h < me.nivelMar) c = math.lerp(new float3(0.05f, 0.22f, 0.35f), new float3(0.1f, 0.45f, 0.55f), math.saturate(1f + (h - me.nivelMar) / 40f));
                else if (!math.isnan(agua) && agua > h) c = math.lerp(new float3(0.16f, 0.42f, 0.5f), new float3(0.06f, 0.22f, 0.32f), math.saturate((agua - h) / 8f));
                float sucio = r.Muestra(r.contaminacion, gi, gj);
                c = math.lerp(c, new float3(0.45f, 0.12f, 0.08f), sucio * 0.7f);
                float claro = r.Muestra(r.claro, gi, gj);
                c = math.lerp(c, new float3(0.75f, 0.68f, 0.5f), claro * 0.35f);
                float hum = r.Muestra(r.humedad, gi, gj);
                c *= 1f - hum * 0.25f;

                c *= sombra;
                // Curvas de nivel cada 25 m.
                if (h > me.alturaMinima && math.frac(h / 25f) < 0.04f) c *= 0.8f;
                px[y * tam + x] = new Color32((byte)(math.saturate(c.x) * 255), (byte)(math.saturate(c.y) * 255), (byte)(math.saturate(c.z) * 255), 255);
            }
            tex.SetPixels32(px);
            tex.Apply();
            Directory.CreateDirectory(Path.GetDirectoryName(ruta));
            File.WriteAllBytes(ruta, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            Debug.Log($"[GD] Mapa del relieve: {ruta}");
        }

        static float3 Tinta(float h)
        {
            if (h < 3f) return new float3(0.86f, 0.80f, 0.62f);
            if (h < 60f) return math.lerp(new float3(0.22f, 0.45f, 0.18f), new float3(0.33f, 0.52f, 0.22f), h / 60f);
            if (h < 180f) return math.lerp(new float3(0.33f, 0.52f, 0.22f), new float3(0.55f, 0.55f, 0.3f), (h - 60f) / 120f);
            if (h < 380f) return math.lerp(new float3(0.55f, 0.55f, 0.3f), new float3(0.5f, 0.42f, 0.35f), (h - 180f) / 200f);
            return math.lerp(new float3(0.5f, 0.42f, 0.35f), new float3(0.85f, 0.85f, 0.85f), math.saturate((h - 380f) / 200f));
        }

        /// <summary>Alturas minima y maxima, la superficie con agua y unas muestras de control.</summary>
        public static string Estadisticas(Relieve r, params float2[] control)
        {
            float min = float.MaxValue, max = float.MinValue;
            int agua = 0;
            for (int k = 0; k < r.alturas.Length; k++)
            {
                float h = r.alturas[k];
                min = math.min(min, h);
                max = math.max(max, h);
                if (!float.IsNaN(r.nivelAgua[k])) agua++;
            }
            float km2 = r.medidas.tamano * r.medidas.tamano / 1e6f;
            string texto = "";
            foreach (var q in control)
                texto += $" ({q.x:F0},{q.y:F0})={r.MuestraMundo(r.alturas, q):F2}";
            return $"alturas {min:F1}..{max:F1} m, agua {agua / (float)r.alturas.Length * km2:F2} de {km2:F1} km2;{texto}";
        }
    }
}
