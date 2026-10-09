using System;
using System.Collections.Generic;
using Guardianes.Mundo;
using Unity.Collections;
using Unity.Mathematics;

namespace Guardianes.EditorTools.Terreno
{
    /// <summary>
    /// Resultado de un generador de relieve: rejilla global de medidas.ResolucionGlobal^2 muestras
    /// con la altura en metros y las mascaras que necesitan el pintado del suelo y el reparto de
    /// vegetacion. Indice = j * res + i (j crece hacia +Z, i hacia +X).
    /// </summary>
    public sealed class Relieve : IDisposable
    {
        public readonly MedidasMundo medidas;
        public readonly int res;
        public NativeArray<float> alturas;
        /// <summary>0..1: cauce (1 = centro del rio o fondo del lago).</summary>
        public NativeArray<float> cauce;
        /// <summary>0..1: orillas y riberas encharcadas.</summary>
        public NativeArray<float> ribera;
        /// <summary>Cota del agua en las muestras con agua (NaN si no hay agua).</summary>
        public NativeArray<float> nivelAgua;
        /// <summary>0..1: humedad por escorrentia (barro y musgo).</summary>
        public NativeArray<float> humedad;
        /// <summary>0..1: suelo danado por los humanos.</summary>
        public NativeArray<float> contaminacion;
        /// <summary>0..1: claro sin arboles (zonas humanas, caminos, playa).</summary>
        public NativeArray<float> claro;
        /// <summary>Mascaras propias del juego (0..1) a la resolucion del relieve: playa, varzea...</summary>
        public readonly Dictionary<string, NativeArray<float>> extras = new Dictionary<string, NativeArray<float>>();
        /// <summary>Rasgos sueltos (x, z, radio, altura): mogotes, farallones, tepuyes.</summary>
        public float4[] torres = new float4[0];
        /// <summary>Rios con la cota del agua ya asignada.</summary>
        public Rios.Punto[][] rios = new Rios.Punto[0][];
        public Lago[] lagos = new Lago[0];

        public Relieve(MedidasMundo medidas)
        {
            this.medidas = medidas;
            res = medidas.ResolucionGlobal;
            int n = res * res;
            alturas = new NativeArray<float>(n, Allocator.Persistent);
            cauce = new NativeArray<float>(n, Allocator.Persistent);
            ribera = new NativeArray<float>(n, Allocator.Persistent);
            nivelAgua = new NativeArray<float>(n, Allocator.Persistent);
            humedad = new NativeArray<float>(n, Allocator.Persistent);
            contaminacion = new NativeArray<float>(n, Allocator.Persistent);
            claro = new NativeArray<float>(n, Allocator.Persistent);
            for (int k = 0; k < n; k++) nivelAgua[k] = float.NaN;
        }

        public NativeArray<float> Extra(string nombre)
        {
            if (!extras.TryGetValue(nombre, out var m))
            {
                m = new NativeArray<float>(res * res, Allocator.Persistent);
                extras[nombre] = m;
            }
            return m;
        }

        /// <summary>Version compacta para el juego: alturas a la resolucion del heightmap y mascaras a la mitad.</summary>
        public DatosMundo ADatos()
        {
            var d = new DatosMundo(medidas);
            d.alturas.CopyFrom(alturas);
            int rm = d.resMascaras;
            d.CrearAgua();
            var extrasDatos = new List<(NativeArray<float> origen, NativeArray<byte> destino)>();
            foreach (var kv in extras) extrasDatos.Add((kv.Value, d.Extra(kv.Key)));
            for (int j = 0; j < rm; j++)
            for (int i = 0; i < rm; i++)
            {
                // Promedio 3x3 alrededor de la muestra (evita aliasing al bajar a la mitad).
                int ci = i * 2, cj = j * 2;
                float c = 0, ri = 0, hu = 0, co = 0, cl = 0; int n = 0;
                float agua = float.NaN;
                for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int x = math.clamp(ci + dx, 0, res - 1), y = math.clamp(cj + dy, 0, res - 1);
                    int k = y * res + x;
                    c += cauce[k]; ri += ribera[k]; hu += humedad[k]; co += contaminacion[k]; cl += claro[k]; n++;
                    float na = nivelAgua[k];
                    if (!float.IsNaN(na) && (float.IsNaN(agua) || na > agua)) agua = na;
                }
                int m = j * rm + i;
                d.cauce[m] = (byte)math.round(math.saturate(c / n) * 255f);
                d.ribera[m] = (byte)math.round(math.saturate(ri / n) * 255f);
                d.humedad[m] = (byte)math.round(math.saturate(hu / n) * 255f);
                d.contaminacion[m] = (byte)math.round(math.saturate(co / n) * 255f);
                d.claro[m] = (byte)math.round(math.saturate(cl / n) * 255f);
                d.agua[m] = d.CodificarAgua(agua);
                for (int x = 0; x < extrasDatos.Count; x++)
                {
                    var origen = extrasDatos[x].origen;
                    var destino = extrasDatos[x].destino;
                    float e = 0f;
                    for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                        e += origen[math.clamp(cj + dy, 0, res - 1) * res + math.clamp(ci + dx, 0, res - 1)];
                    destino[m] = (byte)math.round(math.saturate(e / 9f) * 255f);
                }
            }
            d.rios = rios;
            d.torres = torres;
            d.lagos = lagos;
            return d;
        }

        public float Muestra(NativeArray<float> datos, float i, float j)
        {
            i = math.clamp(i, 0, res - 1.001f);
            j = math.clamp(j, 0, res - 1.001f);
            int i0 = (int)i, j0 = (int)j;
            float fi = i - i0, fj = j - j0;
            float a = datos[j0 * res + i0], b = datos[j0 * res + i0 + 1];
            float c = datos[(j0 + 1) * res + i0], d = datos[(j0 + 1) * res + i0 + 1];
            return math.lerp(math.lerp(a, b, fi), math.lerp(c, d, fi), fj);
        }

        /// <summary>Muestra bilineal en coordenadas de mundo (x, z).</summary>
        public float MuestraMundo(NativeArray<float> datos, float2 xz)
        {
            float2 g = (xz - medidas.Origen) / medidas.tamano * (res - 1);
            return Muestra(datos, g.x, g.y);
        }

        public float2 AMundo(int i, int j) => medidas.Origen + new float2(i, j) * medidas.PasoAlturas;

        public void Dispose()
        {
            if (alturas.IsCreated) alturas.Dispose();
            if (cauce.IsCreated) cauce.Dispose();
            if (ribera.IsCreated) ribera.Dispose();
            if (nivelAgua.IsCreated) nivelAgua.Dispose();
            if (humedad.IsCreated) humedad.Dispose();
            if (contaminacion.IsCreated) contaminacion.Dispose();
            if (claro.IsCreated) claro.Dispose();
            foreach (var e in extras.Values) if (e.IsCreated) e.Dispose();
            extras.Clear();
        }
    }
}
