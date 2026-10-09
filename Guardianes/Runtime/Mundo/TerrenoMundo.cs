using System.Diagnostics;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Guardianes.Mundo
{
    /// <summary>Umbrales del pintado del suelo (cada mundo los ajusta a su relieve).</summary>
    [System.Serializable]
    public struct ReglasSuelo
    {
        [Tooltip("Pendiente (grados) en la que aparece la roca y en la que manda")]
        public Vector2 rocaPendiente;
        [Tooltip("Altura (m) a partir de la que asoma la roca en las cumbres")]
        public Vector2 rocaAltura;
        [Tooltip("Metros sobre el mar donde acaba la arena de la playa (solo con mar)")]
        public Vector2 arenaCota;
        [Tooltip("Altura (m) de los prados de montana")]
        public Vector2 hierbaAltura;

        public static ReglasSuelo PorDefecto => new ReglasSuelo
        {
            rocaPendiente = new Vector2(31f, 43f),
            rocaAltura = new Vector2(380f, 470f),
            arenaCota = new Vector2(1.2f, 3.6f),
            hierbaAltura = new Vector2(140f, 220f),
        };
    }

    /// <summary>
    /// Levanta los Terrain del mundo al activarse (en el editor y en el juego) a partir de los datos
    /// compactos (DatosMundo): alturas y suelo pintado con 8 capas PBR segun pendiente, humedad,
    /// rios, costa o playas, claros y contaminacion. Los terrenos no se guardan en la escena (se
    /// regeneran en ~2 s), asi el repo solo lleva los datos compactos.
    /// </summary>
    [ExecuteAlways]
    [DefaultExecutionOrder(-100)]
    public class TerrenoMundo : MonoBehaviour
    {
        public enum Capa { Hojarasca, Barro, Musgo, Roca, Guijarros, Arena, HierbaSuelo, Contaminado }

        [Tooltip("Datos del mundo (.bytes) que genera el constructor de la escena")]
        public TextAsset datos;
        [Tooltip("Las 8 capas en el orden de Capa")]
        public TerrainLayer[] capas = new TerrainLayer[8];
        public Material material;
        [Range(1f, 8f)] public float errorPixel = 2f;
        public float distanciaBasemap = 900f;
        [Tooltip("Medidas de unos datos de version 1, que no las llevan (la isla de Kong)")]
        public MedidasMundo medidasDatosAntiguos = MedidasMundo.PorDefecto;
        public ReglasSuelo reglas = ReglasSuelo.PorDefecto;

        public static TerrenoMundo Instancia { get; private set; }
        public DatosMundo Datos { get; private set; }
        public Terrain[] Terrenos { get; private set; } = new Terrain[0];
        public MedidasMundo Medidas => Datos != null ? Datos.medidas : medidasDatosAntiguos;

        public static event System.Action Listo;

        void OnEnable()
        {
            Instancia = this;
            Construir();
        }

        void OnDisable()
        {
            Destruir();
            Datos?.Dispose();
            Datos = null;
            if (Instancia == this) Instancia = null;
        }

        void Reset() => reglas = ReglasSuelo.PorDefecto;

        /// <summary>Altura del suelo (m) en x, z; 0 si aun no hay datos.</summary>
        public float Altura(float x, float z) => Datos != null ? Datos.Altura(new float2(x, z)) : 0f;

        /// <summary>Lagos de unos datos antiguos que no los llevan (los pone cada juego).</summary>
        protected virtual Lago[] LagosAntiguos() => null;

        public void Construir()
        {
            Destruir();
            if (datos == null) return;
            var reloj = Stopwatch.StartNew();
            Datos?.Dispose();
            Datos = DatosMundo.Leer(datos.bytes, medidasDatosAntiguos);
            if (Datos.lagos.Length == 0)
            {
                var lagos = LagosAntiguos();
                if (lagos != null) Datos.lagos = lagos;
            }
            var me = Datos.medidas;
            Mapa.Medidas = me;

            int n = me.teselas;
            int resA = me.resolucionAlturas, resC = me.resolucionCapas;
            Terrenos = new Terrain[n * n];
            var alturas = new float[resA, resA];
            var mapa = new float[resC, resC, 8];
            var pesos = new NativeArray<float>(resC * resC * 8, Allocator.TempJob);
            var playa = Datos.TieneExtra("playa") ? Datos.extras["playa"] : new NativeArray<byte>(1, Allocator.TempJob);
            bool editor = !Application.isPlaying;

            for (int tz = 0; tz < n; tz++)
            for (int tx = 0; tx < n; tx++)
            {
                var td = new TerrainData { name = $"Tesela_{tx}_{tz}" };
                if (editor) td.hideFlags = HideFlags.DontSave;
                td.heightmapResolution = resA;
                td.alphamapResolution = resC;
                td.baseMapResolution = 1024;
                td.SetDetailResolution(32, 16);
                td.size = new Vector3(me.TamanoTesela, me.rangoAlturas, me.TamanoTesela);
                td.terrainLayers = capas;

                int i0 = tx * (resA - 1), j0 = tz * (resA - 1);
                int resG = Datos.resAlturas;
                for (int j = 0; j < resA; j++)
                for (int i = 0; i < resA; i++)
                    alturas[j, i] = math.saturate((Datos.alturas[(j0 + j) * resG + i0 + i] - me.alturaMinima) / me.rangoAlturas);
                td.SetHeights(0, 0, alturas);

                float2 origen = me.Origen + new float2(tx, tz) * me.TamanoTesela;
                new JobSuelo
                {
                    resAlturas = Datos.resAlturas,
                    resMascaras = Datos.resMascaras,
                    resCapas = resC,
                    origen = origen,
                    tamano = me.TamanoTesela,
                    origenMundo = me.Origen,
                    tamanoMundo = me.tamano,
                    nivelMar = me.nivelMar,
                    hayMar = me.hayMar,
                    hayPlaya = Datos.TieneExtra("playa"),
                    rocaPendiente = reglas.rocaPendiente,
                    rocaAltura = reglas.rocaAltura,
                    arenaCota = reglas.arenaCota,
                    hierbaAltura = reglas.hierbaAltura,
                    alturas = Datos.alturas,
                    cauce = Datos.cauce,
                    ribera = Datos.ribera,
                    humedad = Datos.humedad,
                    contaminacion = Datos.contaminacion,
                    claro = Datos.claro,
                    playa = playa,
                    pesos = pesos,
                }.Schedule(resC * resC, 256).Complete();
                for (int y = 0; y < resC; y++)
                for (int x = 0; x < resC; x++)
                {
                    int b = (y * resC + x) * 8;
                    for (int c = 0; c < 8; c++) mapa[y, x, c] = pesos[b + c];
                }
                td.SetAlphamaps(0, 0, mapa);

                var go = Terrain.CreateTerrainGameObject(td);
                go.name = td.name;
                if (editor) go.hideFlags = HideFlags.DontSave;
                go.transform.SetParent(transform, false);
                go.transform.position = new Vector3(origen.x, me.alturaMinima, origen.y);
                go.layer = gameObject.layer;
                var t = go.GetComponent<Terrain>();
                t.materialTemplate = material;
                t.drawInstanced = true;
                t.heightmapPixelError = errorPixel;
                t.basemapDistance = distanciaBasemap;
                t.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                t.drawTreesAndFoliage = false;
                t.allowAutoConnect = false;
                Terrenos[tz * n + tx] = t;
            }
            pesos.Dispose();
            if (!Datos.TieneExtra("playa")) playa.Dispose();

            for (int tz = 0; tz < n; tz++)
            for (int tx = 0; tx < n; tx++)
            {
                Terrain Vecino(int x, int z) => (x < 0 || z < 0 || x >= n || z >= n) ? null : Terrenos[z * n + x];
                Terrenos[tz * n + tx].SetNeighbors(Vecino(tx - 1, tz), Vecino(tx, tz + 1), Vecino(tx + 1, tz), Vecino(tx, tz - 1));
            }
            Debug.Log($"[GD] Terreno listo en {reloj.Elapsed.TotalSeconds:F1} s ({me})");
            Listo?.Invoke();
        }

        // ------------------------------------------------------------------ Purificacion
        /// <summary>
        /// La selva recupera el suelo: dentro del radio (con el borde suave) la capa contaminada se
        /// convierte poco a poco en hierba, musgo y hojarasca, y se limpia la mascara de contaminacion.
        /// </summary>
        public void Purificar(Vector3 centro, float radio, float duracion = 6f)
        {
            if (Terrenos.Length == 0 || radio <= 0f) return;
            StartCoroutine(RutinaPurificar(new float2(centro.x, centro.z), radio, Mathf.Max(0.1f, duracion)));
        }

        System.Collections.IEnumerator RutinaPurificar(float2 c, float radio, float duracion)
        {
            const int Pasos = 10;
            float hecho = 0f;
            for (int paso = 1; paso <= Pasos; paso++)
            {
                float objetivo = paso / (float)Pasos;
                // Fraccion de lo que aun queda sucio que se limpia en este paso.
                float k = (objetivo - hecho) / (1f - hecho);
                hecho = objetivo;
                AplicarPurificacion(c, radio, k);
                yield return new WaitForSeconds(duracion / Pasos);
            }
        }

        void AplicarPurificacion(float2 c, float radio, float k)
        {
            var me = Medidas;
            int res = me.resolucionCapas;
            float texel = me.TamanoTesela / res;
            const int Sucio = (int)Capa.Contaminado;
            foreach (var t in Terrenos)
            {
                if (t == null) continue;
                Vector3 o = t.transform.position;
                int x0 = Mathf.Clamp(Mathf.FloorToInt((c.x - radio - o.x) / texel), 0, res);
                int x1 = Mathf.Clamp(Mathf.CeilToInt((c.x + radio - o.x) / texel), 0, res);
                int z0 = Mathf.Clamp(Mathf.FloorToInt((c.y - radio - o.z) / texel), 0, res);
                int z1 = Mathf.Clamp(Mathf.CeilToInt((c.y + radio - o.z) / texel), 0, res);
                if (x1 <= x0 || z1 <= z0) continue;
                var td = t.terrainData;
                var m = td.GetAlphamaps(x0, z0, x1 - x0, z1 - z0);
                bool cambio = false;
                for (int j = 0; j < z1 - z0; j++)
                for (int i = 0; i < x1 - x0; i++)
                {
                    float sucio = m[j, i, Sucio];
                    if (sucio <= 1e-4f) continue;
                    var p = new float2(o.x + (x0 + i + 0.5f) * texel, o.z + (z0 + j + 0.5f) * texel);
                    float borde = 1f - Ruido.SmoothStep(radio * 0.7f, radio, math.distance(p, c));
                    if (borde <= 0f) continue;
                    float quita = sucio * k * borde;
                    m[j, i, Sucio] = sucio - quita;
                    m[j, i, (int)Capa.HierbaSuelo] += quita * 0.45f;
                    m[j, i, (int)Capa.Musgo] += quita * 0.3f;
                    m[j, i, (int)Capa.Hojarasca] += quita * 0.25f;
                    cambio = true;
                }
                if (cambio) td.SetAlphamaps(x0, z0, m);
            }

            // Mascara de contaminacion de los datos (la consultan otros sistemas).
            if (Datos == null) return;
            int rm = Datos.resMascaras;
            float paso = me.tamano / (rm - 1);
            int a0 = Mathf.Clamp(Mathf.FloorToInt((c.x - radio - me.Origen.x) / paso), 0, rm - 1);
            int a1 = Mathf.Clamp(Mathf.CeilToInt((c.x + radio - me.Origen.x) / paso), 0, rm - 1);
            int b0 = Mathf.Clamp(Mathf.FloorToInt((c.y - radio - me.Origen.y) / paso), 0, rm - 1);
            int b1 = Mathf.Clamp(Mathf.CeilToInt((c.y + radio - me.Origen.y) / paso), 0, rm - 1);
            var mascara = Datos.contaminacion;
            for (int y = b0; y <= b1; y++)
            for (int x = a0; x <= a1; x++)
            {
                var p = me.Origen + new float2(x, y) * paso;
                float borde = 1f - Ruido.SmoothStep(radio * 0.7f, radio, math.distance(p, c));
                if (borde <= 0f) continue;
                int idx = y * rm + x;
                mascara[idx] = (byte)Mathf.RoundToInt(mascara[idx] * (1f - k * borde));
            }
        }

        /// <summary>Contaminacion del suelo (0..1) en x, z (la que queda tras purificar).</summary>
        public float Contaminacion(float x, float z) => Datos != null ? Datos.Mascara(Datos.contaminacion, new float2(x, z)) : 0f;

        void Destruir()
        {
            foreach (var t in Terrenos)
            {
                if (t == null) continue;
                var td = t.terrainData;
                if (Application.isPlaying) { Destroy(t.gameObject); Destroy(td); }
                else { DestroyImmediate(t.gameObject); DestroyImmediate(td); }
            }
            Terrenos = new Terrain[0];
            // Restos de una construccion anterior (p. ej. tras recargar scripts).
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                var hijo = transform.GetChild(i);
                if (hijo.GetComponent<Terrain>() != null)
                {
                    if (Application.isPlaying) Destroy(hijo.gameObject); else DestroyImmediate(hijo.gameObject);
                }
            }
        }

        // ------------------------------------------------------------------ Pintado del suelo
        [BurstCompile(CompileSynchronously = true, FloatMode = FloatMode.Fast)]
        struct JobSuelo : IJobParallelFor
        {
            public int resAlturas, resMascaras, resCapas;
            public float2 origen;
            public float tamano;
            public float2 origenMundo;
            public float tamanoMundo;
            public float nivelMar;
            public bool hayMar, hayPlaya;
            public float2 rocaPendiente, rocaAltura, arenaCota, hierbaAltura;
            [ReadOnly] public NativeArray<float> alturas;
            [ReadOnly] public NativeArray<byte> cauce, ribera, humedad, contaminacion, claro, playa;
            [NativeDisableParallelForRestriction][WriteOnly] public NativeArray<float> pesos;

            float Altura(float2 xz)
            {
                float2 g = math.clamp((xz - origenMundo) / tamanoMundo * (resAlturas - 1), 0f, resAlturas - 1.001f);
                int2 c = (int2)g;
                float2 f = g - c;
                int k = c.y * resAlturas + c.x;
                return math.lerp(math.lerp(alturas[k], alturas[k + 1], f.x), math.lerp(alturas[k + resAlturas], alturas[k + resAlturas + 1], f.x), f.y);
            }

            float Mascara(NativeArray<byte> m, float2 xz)
            {
                float2 g = math.clamp((xz - origenMundo) / tamanoMundo * (resMascaras - 1), 0f, resMascaras - 1.001f);
                int2 c = (int2)g;
                float2 f = g - c;
                int k = c.y * resMascaras + c.x;
                return math.lerp(math.lerp(m[k], m[k + 1], f.x), math.lerp(m[k + resMascaras], m[k + resMascaras + 1], f.x), f.y) / 255f;
            }

            public void Execute(int indice)
            {
                int x = indice % resCapas, y = indice / resCapas;
                float2 p = origen + (new float2(x, y) + 0.5f) * (tamano / resCapas);

                float h = Altura(p);
                float hx = Altura(p + new float2(1f, 0f)) - Altura(p - new float2(1f, 0f));
                float hz = Altura(p + new float2(0f, 1f)) - Altura(p - new float2(0f, 1f));
                float pendiente = math.degrees(math.atan(math.length(new float2(hx, hz)) * 0.5f));
                // Concavidad: positiva en vaguadas (se acumulan barro y hojas), negativa en lomas.
                float media = (Altura(p + new float2(3f, 0f)) + Altura(p - new float2(3f, 0f)) +
                               Altura(p + new float2(0f, 3f)) + Altura(p - new float2(0f, 3f))) * 0.25f;
                float concavidad = math.clamp((media - h) * 0.6f, -1f, 1f);

                float cau = Mascara(cauce, p), rib = Mascara(ribera, p), hum = Mascara(humedad, p);
                float sucio = Mascara(contaminacion, p), abierto = Mascara(claro, p);
                float n1 = Ruido.Fbm(p * 0.018f, 4), n2 = Ruido.Fbm(p * 0.09f + 7.3f, 3), n3 = Ruido.Fbm(p * 0.35f + 1.7f, 2);

                float roca = Ruido.SmoothStep(rocaPendiente.x, rocaPendiente.y, pendiente + n1 * 7f + n3 * 3f)
                             + Ruido.SmoothStep(rocaAltura.x, rocaAltura.y, h + n1 * 40f) * 0.7f;
                float arena = 0f;
                if (hayMar)
                    arena = (1f - Ruido.SmoothStep(nivelMar + arenaCota.x, nivelMar + arenaCota.y, h + n2 * 0.9f)) * Ruido.SmoothStep(nivelMar - 30f, nivelMar - 2f, h);
                if (hayPlaya)
                    arena = math.max(arena, Ruido.SmoothStep(0.15f, 0.6f, Mascara(playa, p) + n2 * 0.15f));
                float guijarros = math.saturate(cau * 1.4f - 0.15f) + math.saturate(rib * 1.3f - 0.75f + n2 * 0.4f) * 0.8f;
                float barro = math.saturate(rib * 0.9f + hum * 0.55f + concavidad * 0.35f + n2 * 0.25f - 0.25f) + abierto * 0.25f * (0.5f + n3);
                float musgo = Ruido.SmoothStep(16f, 27f, pendiente + n2 * 6f) * (1f - Ruido.SmoothStep(36f, 46f, pendiente)) * (0.55f + 0.45f * hum)
                              + Ruido.SmoothStep(0.35f, 0.75f, n1 + hum * 0.4f) * 0.35f;
                float hierba = abierto * (0.7f + 0.3f * n3) + Ruido.SmoothStep(0.3f, 0.65f, n1 - hum * 0.4f) * 0.45f * (1f - Ruido.SmoothStep(20f, 30f, pendiente))
                               + Ruido.SmoothStep(hierbaAltura.x, hierbaAltura.y, h) * 0.35f * (1f - roca);
                float hojarasca = 0.75f + 0.25f * n2 - abierto * 0.5f;
                float contaminado = sucio * (1.6f + n2 * 0.6f);

                // La roca y el agua mandan sobre todo lo demas; luego realce y normalizacion.
                float noRoca = 1f - math.saturate(roca);
                float seco = 1f - math.saturate(cau * 1.5f);
                float4 w0 = new float4(
                    math.max(0f, hojarasca) * noRoca * seco * (1f - arena),
                    math.max(0f, barro) * noRoca * (1f - arena * 0.7f),
                    math.max(0f, musgo) * noRoca * seco,
                    math.saturate(roca) * (1f - math.saturate(cau)));
                float4 w1 = new float4(
                    math.max(0f, guijarros),
                    math.max(0f, arena) * noRoca,
                    math.max(0f, hierba) * noRoca * seco * (1f - arena),
                    math.max(0f, contaminado) * seco);
                // Realce: las transiciones quedan cortas y la mezcla por altura hace el resto.
                w0 = math.pow(w0, 2.2f);
                w1 = math.pow(w1, 2.2f);
                float suma = math.csum(w0) + math.csum(w1);
                if (suma < 1e-5f) { w0 = new float4(1f, 0f, 0f, 0f); w1 = 0f; suma = 1f; }
                w0 /= suma;
                w1 /= suma;
                int b = indice * 8;
                pesos[b] = w0.x; pesos[b + 1] = w0.y; pesos[b + 2] = w0.z; pesos[b + 3] = w0.w;
                pesos[b + 4] = w1.x; pesos[b + 5] = w1.y; pesos[b + 6] = w1.z; pesos[b + 7] = w1.w;
            }
        }
    }
}
