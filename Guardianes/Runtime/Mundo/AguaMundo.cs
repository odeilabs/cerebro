using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;

namespace Guardianes.Mundo
{
    /// <summary>
    /// Agua del mundo construida a partir de los datos del terreno: los lagos (superficie de agua
    /// HDRP con malla propia que sigue la orilla real), los rios (cintas que bajan con la cota del
    /// agua de cada tramo, con material de rio) y las laminas de las cascadas. El mar, si lo hay, es
    /// una WaterSurface infinita aparte (la crea el constructor de la escena).
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(TerrenoMundo))]
    public class AguaMundo : MonoBehaviour
    {
        public Material materialRio;
        public Material materialLago;
        [Tooltip("Guardianes/Cascada: la lamina de agua de los saltos")]
        public Material materialCascada;
        [Tooltip("Particulas de niebla al pie de las cascadas (Guardianes/Particula iluminada)")]
        public Material materialNiebla;
        [Tooltip("Metros entre secciones de la cinta del rio")]
        public float paso = 4f;
        [Tooltip("La cinta del rio sobresale de la orilla del cauce para no dejar huecos")]
        public float margen = 4f;

        [Header("Lagos (WaterSurface)")]
        public Color refraccionLago = new Color(0.32f, 0.62f, 0.52f);
        public Color dispersionLago = new Color(0.1f, 0.34f, 0.26f);
        public float absorcionLago = 5f;

        readonly List<GameObject> _creados = new List<GameObject>();

        void OnEnable()
        {
            TerrenoMundo.Listo += Construir;
            var t = GetComponent<TerrenoMundo>();
            if (t != null && t.Datos != null) Construir();
        }

        void OnDisable()
        {
            TerrenoMundo.Listo -= Construir;
            Destruir();
        }

        public void Construir()
        {
            Destruir();
            var terreno = GetComponent<TerrenoMundo>();
            var datos = terreno != null ? terreno.Datos : null;
            if (datos == null) return;
            foreach (var lago in datos.lagos)
                if (lago.Valido) ConstruirLago(lago);
            for (int r = 0; r < datos.rios.Length; r++)
            {
                ConstruirRio(datos, datos.rios[r], r);
                ConstruirCascadas(datos.rios[r], r, terreno);
            }
        }

        // ------------------------------------------------------------------ Cascadas
        /// <summary>
        /// Lamina de agua por cada salto (tramos marcados como cascada): baja en linea recta del
        /// borde al pie sin meterse en la roca, con los bordes desvanecidos; y niebla al pie.
        /// </summary>
        void ConstruirCascadas(Rios.Punto[] rio, int indice, TerrenoMundo terreno)
        {
            if (materialCascada == null) return;
            for (int i = 0; i < rio.Length; i++)
            {
                if (!rio[i].cascada) continue;
                int ini = math.max(0, i - 1);
                int fin = i;
                while (fin + 1 < rio.Length && rio[fin + 1].cascada) fin++;
                fin = math.min(rio.Length - 1, fin + 1);
                ConstruirLamina(rio, ini, fin, terreno, indice);
                i = fin;
            }
        }

        void ConstruirLamina(Rios.Punto[] rio, int ini, int fin, TerrenoMundo terreno, int indice)
        {
            const int Columnas = 9;
            float hArriba = rio[ini].nivel, hAbajo = rio[fin].nivel;
            // Recorrido en planta.
            float largo = 0f;
            for (int k = ini + 1; k <= fin; k++) largo += math.distance(rio[k - 1].xz, rio[k].xz);
            if (largo < 1f) return;
            int filas = math.max(8, (int)math.ceil((hArriba - hAbajo) / 2f));
            var v = new List<Vector3>();
            var uv = new List<Vector2>();
            var col = new List<Color>();
            var t = new List<int>();
            float caida = hArriba - hAbajo;
            for (int f = 0; f <= filas; f++)
            {
                float u = f / (float)filas;            // 0 arriba .. 1 abajo
                // Punto del trazado a la fraccion u del recorrido.
                float objetivo = u * largo, acum = 0f;
                int k = ini;
                while (k < fin && acum + math.distance(rio[k].xz, rio[k + 1].xz) < objetivo)
                {
                    acum += math.distance(rio[k].xz, rio[k + 1].xz);
                    k++;
                }
                int k2 = math.min(k + 1, fin);
                float tramo = math.max(1e-3f, math.distance(rio[k].xz, rio[k2].xz));
                float2 xz = math.lerp(rio[k].xz, rio[k2].xz, math.saturate((objetivo - acum) / tramo));
                float2 tang = math.normalizesafe(rio[k2].xz - rio[k].xz, new float2(1f, 0f));
                float2 nrm = new float2(-tang.y, tang.x);
                float semi = math.lerp(rio[ini].semiancho, rio[fin].semiancho, u) * 0.85f + 2f;
                // Recta del borde al pie; donde la roca sobresale, la lamina la sigue por encima.
                float h = math.lerp(hArriba, hAbajo, u);
                for (int c = 0; c < Columnas; c++)
                {
                    float a = c / (Columnas - 1f);
                    float2 p = xz + nrm * semi * (a * 2f - 1f);
                    float suelo = terreno != null ? terreno.Altura(p.x, p.y) : h;
                    float y = math.max(h, suelo + 2.2f);
                    v.Add(new Vector3(p.x, y, p.y));
                    // v crece hacia arriba (la espuma corre hacia abajo en el shader).
                    uv.Add(new Vector2(a * semi * 2f / 12f, (1f - u) * caida / 18f));
                    float borde = 1f - math.pow(math.abs(a * 2f - 1f), 3f);
                    float extremos = math.saturate(u / 0.05f) * math.saturate((1f - u) / 0.08f);
                    col.Add(new Color(math.saturate(u * 1.3f), 0f, 0f, borde * math.lerp(0.35f, 1f, extremos)));
                }
                if (f > 0)
                {
                    int f0 = v.Count - Columnas * 2, f1 = v.Count - Columnas;
                    for (int c = 0; c < Columnas - 1; c++)
                    {
                        t.Add(f0 + c); t.Add(f0 + c + 1); t.Add(f1 + c);
                        t.Add(f0 + c + 1); t.Add(f1 + c + 1); t.Add(f1 + c);
                    }
                }
            }
            var malla = new Mesh { name = $"Cascada_{indice}" };
            if (!Application.isPlaying) malla.hideFlags = HideFlags.DontSave;
            malla.SetVertices(v);
            malla.SetUVs(0, uv);
            malla.SetColors(col);
            malla.SetTriangles(t, 0);
            malla.RecalculateNormals();
            malla.RecalculateBounds();
            var go = Crear($"Cascada_{indice}");
            go.AddComponent<MeshFilter>().sharedMesh = malla;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = materialCascada;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            // Niebla de agua pulverizada al pie (solo jugando).
            if (Application.isPlaying && materialNiebla != null)
            {
                var pie = rio[fin];
                var niebla = Crear("NieblaCascada");
                niebla.transform.position = new Vector3(pie.xz.x, pie.nivel + 4f, pie.xz.y);
                var ps = niebla.AddComponent<ParticleSystem>();
                ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                var main = ps.main;
                main.loop = true;
                main.startLifetime = new ParticleSystem.MinMaxCurve(6f, 10f);
                main.startSpeed = new ParticleSystem.MinMaxCurve(1.5f, 4f);
                main.startSize = new ParticleSystem.MinMaxCurve(12f, 26f);
                main.startRotation = new ParticleSystem.MinMaxCurve(0f, 6.28f);
                main.startColor = new Color(0.92f, 0.95f, 0.97f, 0.22f);
                main.gravityModifier = -0.015f;
                main.maxParticles = 160;
                main.simulationSpace = ParticleSystemSimulationSpace.World;
                var em = ps.emission;
                em.rateOverTime = 9f;
                var sh = ps.shape;
                sh.shapeType = ParticleSystemShapeType.Hemisphere;
                sh.radius = math.max(6f, pie.semiancho);
                var sol = ps.sizeOverLifetime;
                sol.enabled = true;
                sol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 0.5f, 1f, 1.3f));
                var colv = ps.colorOverLifetime;
                colv.enabled = true;
                var g = new Gradient();
                g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                          new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.2f), new GradientAlphaKey(0.6f, 0.6f), new GradientAlphaKey(0f, 1f) });
                colv.color = g;
                var rot = ps.rotationOverLifetime;
                rot.enabled = true;
                rot.z = new ParticleSystem.MinMaxCurve(-0.2f, 0.2f);
                var tsa = ps.textureSheetAnimation;
                tsa.enabled = true;
                tsa.numTilesX = 4;
                tsa.numTilesY = 4;
                tsa.frameOverTime = new ParticleSystem.MinMaxCurve(0f, 0.999f);
                var r = niebla.GetComponent<ParticleSystemRenderer>();
                r.sharedMaterial = materialNiebla;
                r.maxParticleSize = 4f;
                r.sortMode = ParticleSystemSortMode.Distance;
                ps.Play();
            }
        }

        void Destruir()
        {
            foreach (var go in _creados)
            {
                if (go == null) continue;
                var mf = go.GetComponent<MeshFilter>();
                if (mf != null && mf.sharedMesh != null)
                {
                    if (Application.isPlaying) Destroy(mf.sharedMesh); else DestroyImmediate(mf.sharedMesh);
                }
                if (Application.isPlaying) Destroy(go); else DestroyImmediate(go);
            }
            _creados.Clear();
        }

        GameObject Crear(string nombre)
        {
            var go = new GameObject(nombre);
            if (!Application.isPlaying) go.hideFlags = HideFlags.DontSave;
            go.transform.SetParent(transform, false);
            _creados.Add(go);
            return go;
        }

        // ------------------------------------------------------------------ Lagos
        void ConstruirLago(Lago lago)
        {
            const int Lados = 160;
            var v = new List<Vector3> { Vector3.zero };
            var t = new List<int>();
            for (int i = 0; i < Lados; i++)
            {
                float a = i / (float)Lados * math.PI * 2f;
                float2 dir = new float2(math.cos(a), math.sin(a));
                // Algo mas grande que la orilla: el terreno tapa lo que sobra.
                float radio = lago.RadioEn(dir) * 1.08f + margen;
                v.Add(new Vector3(dir.x * radio, 0f, dir.y * radio));
                t.Add(0); t.Add(1 + (i + 1) % Lados); t.Add(1 + i);
            }
            var malla = new Mesh { name = "Lago_" + lago.nombre };
            if (!Application.isPlaying) malla.hideFlags = HideFlags.DontSave;
            malla.SetVertices(v);
            malla.SetTriangles(t, 0);
            malla.RecalculateNormals();
            malla.RecalculateBounds();

            var go = Crear("Lago_" + lago.nombre);
            go.transform.position = new Vector3(lago.centro.x, lago.nivel, lago.centro.y);
            var forma = Crear("Lago_Forma");
            forma.transform.SetParent(go.transform, false);
            forma.AddComponent<MeshFilter>().sharedMesh = malla;
            var mr = forma.AddComponent<MeshRenderer>();
            mr.enabled = false; // solo da la forma; pinta la WaterSurface
            if (materialLago != null) mr.sharedMaterial = materialLago;

            var ws = go.AddComponent<WaterSurface>();
            ws.surfaceType = WaterSurfaceType.Pool;
            ws.geometryType = WaterGeometryType.Custom;
            ws.meshRenderers = new List<MeshRenderer> { mr };
            ws.ripplesWindSpeed = 5f;
            // Lago de selva: verde turquesa con algo de turbidez (con poca dispersion solo se veia el
            // reflejo del cenit y quedaba casi negro).
            ws.refractionColor = refraccionLago;
            ws.scatteringColor = dispersionLago;
            ws.absorptionDistance = absorcionLago;
            ws.maxRefractionDistance = 1.5f;
            ws.caustics = true;
        }

        // ------------------------------------------------------------------ Rios
        void ConstruirRio(DatosMundo datos, Rios.Punto[] rio, int indice)
        {
            if (rio.Length < 2 || materialRio == null) return;
            var me = datos.medidas;
            var v = new List<Vector3>();
            var uv = new List<Vector2>();
            var col = new List<Color>();
            var t = new List<int>();
            float s = 0f;
            float2 previo = rio[0].xz;
            int filas = 0;
            const int Columnas = 6;
            for (int i = 0; i < rio.Length; i++)
            {
                var q = rio[i];
                // Dentro de un lago no hace falta cinta (y tampoco por debajo del mar).
                bool enLago = false;
                foreach (var l in datos.lagos)
                    if (l.Valido && l.Relativa(q.xz) < 0.9f) { enLago = true; break; }
                // En los saltos tampoco: ahi va la lamina de la cascada.
                if (enLago || q.cascada || (me.hayMar && q.nivel < me.nivelMar - 0.5f))
                {
                    if (filas > 1) CerrarTramo();
                    filas = 0;
                    continue;
                }
                s += math.distance(previo, q.xz);
                previo = q.xz;

                float2 a = rio[math.max(i - 1, 0)].xz, b = rio[math.min(i + 1, rio.Length - 1)].xz;
                float2 tang = math.normalizesafe(b - a, new float2(1f, 0f));
                float2 nrm = new float2(-tang.y, tang.x);
                float semi = q.semiancho + margen;
                // Velocidad del agua por la pendiente: el material la lee del color (rojo).
                float caida = math.abs(rio[math.min(i + 1, rio.Length - 1)].nivel - rio[math.max(i - 1, 0)].nivel) / math.max(1f, math.distance(a, b));
                float velocidad = math.saturate(0.25f + caida * 12f);
                for (int c = 0; c < Columnas; c++)
                {
                    float u = c / (Columnas - 1f);
                    float2 xz = q.xz + nrm * semi * (u * 2f - 1f);
                    v.Add(new Vector3(xz.x, q.nivel, xz.y));
                    uv.Add(new Vector2(u * semi * 2f / 8f, s / 8f));
                    // g = espuma en los bordes y en los rapidos
                    float borde = 1f - math.abs(u * 2f - 1f);
                    col.Add(new Color(velocidad, math.saturate((1f - borde) * 0.6f + caida * 6f), 0f, 1f));
                }
                if (filas > 0)
                {
                    int f0 = v.Count - Columnas * 2, f1 = v.Count - Columnas;
                    // Cara hacia arriba (convencion de Unity: cross(b - a, c - a) es la normal). Al
                    // reves, el material (cara trasera descartada) dejaba el rio invisible desde arriba.
                    for (int c = 0; c < Columnas - 1; c++)
                    {
                        t.Add(f0 + c); t.Add(f0 + c + 1); t.Add(f1 + c);
                        t.Add(f0 + c + 1); t.Add(f1 + c + 1); t.Add(f1 + c);
                    }
                }
                filas++;
            }
            if (filas > 1) CerrarTramo();

            void CerrarTramo()
            {
                if (t.Count == 0) { v.Clear(); uv.Clear(); col.Clear(); return; }
                var malla = new Mesh { name = $"Rio_{indice}" };
                if (!Application.isPlaying) malla.hideFlags = HideFlags.DontSave;
                malla.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                malla.SetVertices(v);
                malla.SetUVs(0, uv);
                malla.SetColors(col);
                malla.SetTriangles(t, 0);
                malla.RecalculateNormals();
                malla.RecalculateTangents();
                malla.RecalculateBounds();
                var go = Crear($"Rio_{indice}_{_creados.Count}");
                go.AddComponent<MeshFilter>().sharedMesh = malla;
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = materialRio;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                v.Clear(); uv.Clear(); col.Clear(); t.Clear();
            }
        }
    }
}
