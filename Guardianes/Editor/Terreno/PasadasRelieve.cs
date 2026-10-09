using System;
using System.Collections.Generic;
using Guardianes.Mundo;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace Guardianes.EditorTools.Terreno
{
    /// <summary>
    /// Las pasadas comunes de los generadores de relieve (todo Burst). Cada juego hace su relieve
    /// base (la isla de Kong, la cuenca del basilisco) en una rejilla gruesa y luego encadena:
    ///   Erosion (gotas con inercia y termica) · Subir a la resolucion final con detalle segun la
    ///   pendiente y los rasgos sueltos (mogotes, tepuyes) · Niveles del agua de los rios sacados del
    ///   terreno · Tallar rios y lagos (cauce, riberas, diques) · Allanar zonas humanas y lugares ·
    ///   Humedad por escorrentia.
    /// </summary>
    public static class PasadasRelieve
    {
        /// <summary>La rejilla gruesa va a 4 veces el paso final (4 m con heightmaps de 1 m).</summary>
        public const int FactorGrueso = 4;

        public static int ResGruesa(MedidasMundo m) => (m.ResolucionGlobal - 1) / FactorGrueso + 1;

        // ================================================================== 2. Erosion
        /// <summary>Erosion hidraulica por gotas y termica sobre la rejilla gruesa (en el sitio).</summary>
        public static void Erosionar(NativeArray<float> gruesa, int resGruesa, MedidasMundo m, int gotas, uint semilla, float tanMax = 5.2f, int iteracionesTermica = 4)
        {
            new JobErosionGotas
            {
                res = resGruesa,
                alturas = gruesa,
                gotas = gotas,
                semilla = semilla,
                escalaAltura = 1f / m.rangoAlturas,
                fondo = (m.hayMar ? m.nivelMar - 1.5f : m.alturaMinima),
            }.Run();
            new JobTermica { res = resGruesa, alturas = gruesa, tanMax = tanMax, iteraciones = iteracionesTermica }.Run();
        }

        // Algoritmo de gotas con inercia (Hans Theobald Beyer, 2015), alturas normalizadas.
        [BurstCompile(CompileSynchronously = true, FloatMode = FloatMode.Fast)]
        struct JobErosionGotas : IJob
        {
            public int res;
            public NativeArray<float> alturas;
            public int gotas;
            public uint semilla;
            public float escalaAltura;
            /// <summary>Por debajo de esto no se erosiona (el mar) y la gota se para.</summary>
            public float fondo;

            const int Radio = 3;
            const float Inercia = 0.06f;
            const float Capacidad = 4.5f;
            const float CapacidadMinima = 0.0006f;
            const float VelErosion = 0.32f;
            const float VelDeposito = 0.28f;
            const float Evaporacion = 0.012f;
            const float Gravedad = 4f;
            const int Vida = 56;

            public void Execute()
            {
                int n = res * res;
                for (int k = 0; k < n; k++) alturas[k] *= escalaAltura;

                // Pincel de erosion precalculado (pesos normalizados en un disco).
                int lado = Radio * 2 + 1;
                var pesos = new NativeArray<float>(lado * lado, Allocator.Temp);
                float suma = 0f;
                for (int y = -Radio; y <= Radio; y++)
                for (int x = -Radio; x <= Radio; x++)
                {
                    float d = math.sqrt(x * x + y * y);
                    float w = math.max(0f, Radio - d);
                    pesos[(y + Radio) * lado + x + Radio] = w;
                    suma += w;
                }
                for (int k = 0; k < pesos.Length; k++) pesos[k] /= suma;

                var rnd = new Unity.Mathematics.Random(semilla | 1u);
                float fondoNorm = fondo * escalaAltura;

                for (int g = 0; g < gotas; g++)
                {
                    float2 pos = rnd.NextFloat2(new float2(Radio + 1), new float2(res - Radio - 2));
                    float2 dir = 0f;
                    float vel = 1f, agua = 1f, sedimento = 0f;

                    for (int paso = 0; paso < Vida; paso++)
                    {
                        int2 nodo = (int2)pos;
                        float2 off = pos - nodo;
                        float h = AlturaYGradiente(pos, out float2 grad);
                        if (h < fondoNorm)
                        {
                            Depositar(nodo, off, sedimento);
                            break;
                        }

                        dir = dir * Inercia - grad * (1f - Inercia);
                        float len = math.length(dir);
                        if (len < 1e-7f)
                        {
                            float a = rnd.NextFloat(0f, 6.2831853f);
                            dir = new float2(math.cos(a), math.sin(a));
                        }
                        else dir /= len;

                        pos += dir;
                        if (pos.x < Radio + 1 || pos.y < Radio + 1 || pos.x >= res - Radio - 2 || pos.y >= res - Radio - 2)
                            break;

                        float hNueva = AlturaYGradiente(pos, out _);
                        float dh = hNueva - h;
                        float capacidad = math.max(-dh * vel * agua * Capacidad, CapacidadMinima);

                        if (sedimento > capacidad || dh > 0f)
                        {
                            float deposito = dh > 0f ? math.min(dh, sedimento) : (sedimento - capacidad) * VelDeposito;
                            sedimento -= deposito;
                            Depositar(nodo, off, deposito);
                        }
                        else
                        {
                            float erosion = math.min((capacidad - sedimento) * VelErosion, -dh);
                            for (int y = -Radio; y <= Radio; y++)
                            for (int x = -Radio; x <= Radio; x++)
                            {
                                float w = pesos[(y + Radio) * lado + x + Radio];
                                if (w <= 0f) continue;
                                int idx = (nodo.y + y) * res + nodo.x + x;
                                // Solo se lleva material por encima del fondo (el algoritmo original
                                // supone alturas >= 0 y con fondo marino negativo lo pondria a 0).
                                float quita = math.min(erosion * w, math.max(0f, alturas[idx] - fondoNorm));
                                alturas[idx] -= quita;
                                sedimento += quita;
                            }
                        }

                        vel = math.sqrt(math.max(0f, vel * vel - dh * Gravedad));
                        agua *= 1f - Evaporacion;
                    }
                }

                pesos.Dispose();
                float inv = 1f / escalaAltura;
                for (int k = 0; k < n; k++) alturas[k] *= inv;
            }

            void Depositar(int2 nodo, float2 off, float cantidad)
            {
                int idx = nodo.y * res + nodo.x;
                alturas[idx] += cantidad * (1 - off.x) * (1 - off.y);
                alturas[idx + 1] += cantidad * off.x * (1 - off.y);
                alturas[idx + res] += cantidad * (1 - off.x) * off.y;
                alturas[idx + res + 1] += cantidad * off.x * off.y;
            }

            float AlturaYGradiente(float2 pos, out float2 grad)
            {
                int2 c = (int2)pos;
                float2 f = pos - c;
                int idx = c.y * res + c.x;
                float nw = alturas[idx], ne = alturas[idx + 1], sw = alturas[idx + res], se = alturas[idx + res + 1];
                grad = new float2((ne - nw) * (1 - f.y) + (se - sw) * f.y, (sw - nw) * (1 - f.x) + (se - ne) * f.x);
                return nw * (1 - f.x) * (1 - f.y) + ne * f.x * (1 - f.y) + sw * (1 - f.x) * f.y + se * f.x * f.y;
            }
        }

        // Erosion termica: reparte el exceso de pendiente (talud) y suaviza crestas imposibles.
        [BurstCompile(CompileSynchronously = true, FloatMode = FloatMode.Fast)]
        struct JobTermica : IJob
        {
            public int res;
            public NativeArray<float> alturas;
            public float tanMax; // diferencia maxima de altura entre vecinos (m) antes de desmoronarse
            public int iteraciones;

            public void Execute()
            {
                for (int it = 0; it < iteraciones; it++)
                for (int j = 1; j < res - 1; j++)
                for (int i = 1; i < res - 1; i++)
                {
                    int idx = j * res + i;
                    float h = alturas[idx];
                    int mejor = -1;
                    float maxDif = tanMax;
                    int4 vec = new int4(idx - 1, idx + 1, idx - res, idx + res);
                    for (int k = 0; k < 4; k++)
                    {
                        float d = h - alturas[vec[k]];
                        if (d > maxDif) { maxDif = d; mejor = vec[k]; }
                    }
                    if (mejor >= 0)
                    {
                        float mover = (maxDif - tanMax) * 0.25f;
                        alturas[idx] -= mover;
                        alturas[mejor] += mover;
                    }
                }
            }
        }

        // ================================================================== 3. Subida + detalle
        /// <summary>
        /// Sube la rejilla gruesa a la del relieve (Catmull-Rom) con detalle: suave en llano (raices,
        /// lomitas) y rocoso en pendiente; y levanta los rasgos sueltos de r.torres (paredes casi
        /// verticales, cima rugosa y talud de derrubios).
        /// </summary>
        public static void SubirYDetallar(NativeArray<float> gruesa, int resGruesa, Relieve r, int semilla, float detalle = 1f)
        {
            var torres = new NativeArray<float4>(r.torres, Allocator.TempJob);
            new JobSubirYDetallar
            {
                resGruesa = resGruesa,
                gruesa = gruesa,
                res = r.res,
                origen = r.medidas.Origen,
                paso = r.medidas.PasoAlturas,
                fondo = r.medidas.hayMar ? r.medidas.nivelMar : r.medidas.alturaMinima - 100f,
                semilla = semilla,
                detalle = detalle,
                torres = torres,
                alturas = r.alturas,
            }.Schedule(r.alturas.Length, 256).Complete();
            torres.Dispose();
        }

        [BurstCompile(CompileSynchronously = true, FloatMode = FloatMode.Fast)]
        struct JobSubirYDetallar : IJobParallelFor
        {
            public int resGruesa;
            [ReadOnly] public NativeArray<float> gruesa;
            public int res;
            public float2 origen;
            public float paso;
            public float fondo;
            public int semilla;
            public float detalle;
            [ReadOnly] public NativeArray<float4> torres;
            [WriteOnly] public NativeArray<float> alturas;

            public void Execute(int indice)
            {
                int i = indice % res, j = indice / res;
                float gx = i * (resGruesa - 1f) / (res - 1f);
                float gy = j * (resGruesa - 1f) / (res - 1f);
                float h = CatmullRom(gx, gy);

                // Pendiente aproximada de la rejilla gruesa (m por m).
                float hx = CatmullRom(math.min(gx + 0.5f, resGruesa - 1), gy) - CatmullRom(math.max(gx - 0.5f, 0), gy);
                float hy = CatmullRom(gx, math.min(gy + 0.5f, resGruesa - 1)) - CatmullRom(gx, math.max(gy - 0.5f, 0));
                float pendiente = math.length(new float2(hx, hy)) / (paso * FactorGrueso);

                float2 p = origen + new float2(i, j) * paso;
                float2 s = new float2(semilla * 0.071f, -semilla * 0.037f);
                // Detalle: suave en llano (raices, lomitas), rocoso y mas fuerte en pendiente.
                float suave = Ruido.Fbm(p * 0.045f + s, 4) * 0.55f + Ruido.Fbm(p * 0.13f + s, 2) * 0.12f;
                float roca = Ruido.Crestas(p * 0.025f + s + 4.4f, 5) * 4.5f - 2.2f;
                float t = Ruido.SmoothStep(0.35f, 0.9f, pendiente);
                float sobreFondo = Ruido.SmoothStep(fondo - 1f, fondo + 3f, h);
                h += (suave * (1f - t) + roca * t) * sobreFondo * detalle;
                alturas[indice] = Torres(p, h);
            }

            // Rasgos sueltos: paredes casi verticales, cima rugosa y un talud de derrubios.
            float Torres(float2 p, float h)
            {
                float resultado = h;
                for (int t = 0; t < torres.Length; t++)
                {
                    float4 to = torres[t];
                    float2 d = p - to.xy;
                    float dist = math.length(d);
                    if (dist > to.z * 1.6f) continue;
                    float ang = math.atan2(d.y, d.x);
                    float2 dir = new float2(math.cos(ang), math.sin(ang));
                    float radio = to.z * (1f + 0.2f * Ruido.Fbm(dir * 2.2f + to.xy * 0.013f, 3) + 0.05f * noise.snoise(p * 0.06f));
                    float u = dist / radio;
                    float pared = u < 1f ? math.pow(math.saturate(1f - math.pow(u, 7f)), 0.3f) : 0f;
                    float talud = (1f - Ruido.SmoothStep(0.8f, 1.45f, u)) * 0.16f;
                    float cima = Ruido.Crestas(p * 0.028f + to.xy * 0.1f, 4) * 0.22f - 0.08f;
                    float alto = to.w * (pared * (0.88f + cima) + talud * (1f - pared));
                    resultado = math.max(resultado, h + alto);
                }
                return resultado;
            }

            float Valor(int x, int y)
            {
                x = math.clamp(x, 0, resGruesa - 1);
                y = math.clamp(y, 0, resGruesa - 1);
                return gruesa[y * resGruesa + x];
            }

            static float Cubica(float a, float b, float c, float d, float t)
            {
                return b + 0.5f * t * (c - a + t * (2f * a - 5f * b + 4f * c - d + t * (3f * (b - c) + d - a)));
            }

            float CatmullRom(float x, float y)
            {
                int x0 = (int)math.floor(x), y0 = (int)math.floor(y);
                float fx = x - x0, fy = y - y0;
                float4 filas;
                filas.x = Cubica(Valor(x0 - 1, y0 - 1), Valor(x0, y0 - 1), Valor(x0 + 1, y0 - 1), Valor(x0 + 2, y0 - 1), fx);
                filas.y = Cubica(Valor(x0 - 1, y0), Valor(x0, y0), Valor(x0 + 1, y0), Valor(x0 + 2, y0), fx);
                filas.z = Cubica(Valor(x0 - 1, y0 + 1), Valor(x0, y0 + 1), Valor(x0 + 1, y0 + 1), Valor(x0 + 2, y0 + 1), fx);
                filas.w = Cubica(Valor(x0 - 1, y0 + 2), Valor(x0, y0 + 2), Valor(x0 + 1, y0 + 2), Valor(x0 + 2, y0 + 2), fx);
                return Cubica(filas.x, filas.y, filas.z, filas.w, fy);
            }
        }

        // ================================================================== 4. Niveles del agua
        /// <summary>
        /// Cota del agua de un rio sacada del propio terreno: un poco por debajo del suelo mas bajo
        /// de cada seccion y siempre descendente aguas abajo (si hay una loma, el rio la corta en
        /// garganta). En un lago la cota es la del lago; un afluente desemboca a la cota del rio
        /// principal en la confluencia; con mar, el principal acaba a su nivel. Las cascadas salen
        /// solas donde el terreno cae de golpe.
        /// </summary>
        public static void AsignarNiveles(Relieve r, Rios.Punto[] rio, Rios.Punto[] principal, Lago[] lagos, float bajoSuelo = 2.2f)
        {
            int n = rio.Length;
            var objetivo = new float[n];
            for (int i = 0; i < n; i++)
            {
                float2 a = rio[math.max(i - 1, 0)].xz, b = rio[math.min(i + 1, n - 1)].xz;
                float2 t = math.normalizesafe(b - a, new float2(1, 0));
                float2 nrm = new float2(-t.y, t.x);
                float alcance = rio[i].semiancho + 18f;
                float minimo = float.MaxValue;
                for (int k = -4; k <= 4; k++)
                    minimo = math.min(minimo, r.MuestraMundo(r.alturas, rio[i].xz + nrm * (alcance * k / 4f)));
                objetivo[i] = minimo - bajoSuelo;
            }
            // Suavizado a lo largo del cauce (ventana de ~40 m) para que no copie cada bache.
            var suave = new float[n];
            for (int i = 0; i < n; i++)
            {
                float suma = 0f; int c = 0;
                for (int k = -3; k <= 3; k++) { int j = math.clamp(i + k, 0, n - 1); suma += objetivo[j]; c++; }
                suave[i] = math.min(objetivo[i] + 0.5f, suma / c);
            }

            var me = r.medidas;
            float cotaFinal = principal != null ? Rios.NivelEn(principal, rio[n - 1].xz) : (me.hayMar ? me.nivelMar - 1.2f : float.NegativeInfinity);
            float previo = float.MaxValue;
            for (int i = 0; i < n; i++)
            {
                var q = rio[i];
                float nivel = float.NaN;
                if (lagos != null)
                    foreach (var l in lagos)
                        if (l.Valido && l.Relativa(q.xz) < 0.95f) { nivel = l.nivel; break; }
                if (float.IsNaN(nivel)) nivel = math.min(suave[i], previo - 0.0004f * Paso(rio, i));
                // Junto a un lago, el tallado rebaja la orilla despues: el agua baja con ella (si no,
                // la cinta del rio queda flotando sobre el talud).
                if (lagos != null)
                    foreach (var l in lagos)
                    {
                        if (!l.Valido) continue;
                        float ul = l.Relativa(q.xz);
                        if (ul < 1.45f)
                        {
                            float v = math.saturate((ul - 1f) / 0.45f);
                            nivel = math.min(nivel, l.nivel + 0.5f + v * v * 18f - 1.5f);
                        }
                    }
                // En el mar el rio acaba a nivel del mar.
                if (me.hayMar && q.avance > 0.97f && principal == null) nivel = math.min(nivel, me.nivelMar - 0.6f);
                q.nivel = nivel;
                rio[i] = q;
                previo = nivel;
            }
            // Cascadas: donde la cota cae de golpe (mas de 4 m con pendiente > 0,8), no donde lo
            // dijeran los puntos de control (el terreno manda).
            for (int i = 0; i < n; i++)
            {
                var q = rio[i];
                q.cascada = false;
                rio[i] = q;
            }
            for (int i = 1; i < n; i++)
            {
                float caida = rio[i - 1].nivel - rio[i].nivel;
                float dist = math.max(1f, math.distance(rio[i - 1].xz, rio[i].xz));
                if (caida > 4f && caida / dist > 0.8f)
                {
                    var a = rio[i - 1]; a.cascada = true; rio[i - 1] = a;
                    var b = rio[i]; b.cascada = true; rio[i] = b;
                }
            }

            // El tramo final de un afluente baja a la cota del principal en la confluencia.
            if (principal != null)
            {
                for (int i = n - 1; i >= 0; i--)
                {
                    float d = rio[n - 1].s - rio[i].s;
                    if (d > 120f) break;
                    var q = rio[i];
                    float w = 1f - d / 120f;
                    q.nivel = math.lerp(q.nivel, math.max(cotaFinal, q.nivel - 4f), w);
                    q.nivel = math.max(q.nivel, cotaFinal);
                    rio[i] = q;
                }
            }
        }

        static float Paso(Rios.Punto[] rio, int i) => i == 0 ? 0f : math.max(0f, rio[i].s - rio[i - 1].s);

        // ================================================================== 5. Tallar rios y lagos
        /// <summary>
        /// Segmentos de los cauces densos en una rejilla de 64 m, para que cada muestra solo mire los
        /// segmentos cercanos. puntos = (x, z, nivel, semiancho); datos = (profundidad, avance, rio,
        /// fin) con fin = 1 en el ultimo punto de un rio (el segmento k une los puntos k y k+1 salvo
        /// que k sea un final).
        /// </summary>
        sealed class CaucesRio : IDisposable
        {
            public const float Celda = 64f;
            public readonly float alcance;
            public readonly int lado;
            public NativeArray<float4> puntos;
            public NativeArray<float4> datos;
            public NativeArray<int2> celdas;
            public NativeArray<int> indices;

            public CaucesRio(MedidasMundo m, float alcance, Rios.Punto[][] rios)
            {
                this.alcance = alcance;
                var lp = new List<float4>();
                var ld = new List<float4>();
                for (int r = 0; r < rios.Length; r++)
                    for (int k = 0; k < rios[r].Length; k++)
                    {
                        var q = rios[r][k];
                        lp.Add(new float4(q.xz, q.nivel, q.semiancho));
                        ld.Add(new float4(q.profundidad, q.avance, r, k == rios[r].Length - 1 ? 1 : 0));
                    }
                if (lp.Count == 0) { lp.Add(new float4(1e9f)); ld.Add(new float4(0f, 0f, -1f, 1f)); }
                puntos = new NativeArray<float4>(lp.ToArray(), Allocator.TempJob);
                datos = new NativeArray<float4>(ld.ToArray(), Allocator.TempJob);

                lado = (int)math.ceil(m.tamano / Celda);
                var listas = new List<int>[lado * lado];
                for (int k = 0; k < lp.Count - 1; k++)
                {
                    if (ld[k].w > 0.5f) continue;
                    float2 a = lp[k].xy, b = lp[k + 1].xy;
                    float2 mn = math.min(a, b) - alcance - m.Origen, mx = math.max(a, b) + alcance - m.Origen;
                    int2 c0 = math.clamp((int2)math.floor(mn / Celda), 0, lado - 1), c1 = math.clamp((int2)math.floor(mx / Celda), 0, lado - 1);
                    for (int y = c0.y; y <= c1.y; y++)
                        for (int x = c0.x; x <= c1.x; x++)
                            (listas[y * lado + x] ??= new List<int>()).Add(k);
                }
                var plano = new List<int>();
                celdas = new NativeArray<int2>(lado * lado, Allocator.TempJob);
                for (int c = 0; c < listas.Length; c++)
                {
                    int ini = plano.Count;
                    if (listas[c] != null) plano.AddRange(listas[c]);
                    celdas[c] = new int2(ini, plano.Count - ini);
                }
                indices = new NativeArray<int>(math.max(1, plano.Count), Allocator.TempJob);
                for (int i = 0; i < plano.Count; i++) indices[i] = plano[i];
            }

            public void Dispose()
            {
                puntos.Dispose();
                datos.Dispose();
                celdas.Dispose();
                indices.Dispose();
            }
        }

        /// <summary>
        /// Talla los lagos (lecho, orilla y dique) y los rios (cauce y talud de la orilla) en el
        /// relieve, y escribe cauce, ribera y la cota del agua. anchoRibera: metros de ribera de cada
        /// rio (si falta, 45). alcance: distancia maxima al eje que se mira (0 = la que piden los
        /// rios). Donde se cruzan, se queda lo mas hondo.
        /// </summary>
        public static void TallarRiosYLagos(Relieve r, int semilla, Rios.Punto[][] rios, Lago[] lagos, float[] anchoRibera = null, float alcance = 0f)
        {
            r.rios = rios;
            r.lagos = lagos ?? new Lago[0];
            float maxRibera = 45f, maxSemi = 0f;
            var anchos = new NativeArray<float>(math.max(1, rios.Length), Allocator.TempJob);
            for (int i = 0; i < rios.Length; i++)
            {
                anchos[i] = anchoRibera != null && i < anchoRibera.Length ? anchoRibera[i] : 45f;
                maxRibera = math.max(maxRibera, anchos[i]);
                foreach (var p in rios[i]) maxSemi = math.max(maxSemi, p.semiancho);
            }
            // Lagos aplanados para Burst: centro, nivel, profundidad y los radios seguidos.
            int nl = r.lagos.Length;
            var lagoDatos = new NativeArray<float4>(math.max(1, nl), Allocator.TempJob);
            var lagoRadios = new NativeArray<float>(math.max(1, nl * 64), Allocator.TempJob);
            for (int l = 0; l < nl; l++)
            {
                var lago = r.lagos[l];
                lagoDatos[l] = new float4(lago.centro, lago.nivel, lago.profundidad > 0f ? lago.profundidad : 11.6f);
                for (int k = 0; k < 64; k++)
                {
                    float a = k / 64f * math.PI * 2f;
                    lagoRadios[l * 64 + k] = lago.RadioEn(new float2(math.cos(a), math.sin(a)));
                }
            }
            using (var cauces = new CaucesRio(r.medidas, alcance > 0f ? alcance : maxSemi * 1.3f + maxRibera + 10f, rios))
            {
                new JobRios
                {
                    res = r.res,
                    origen = r.medidas.Origen,
                    paso = r.medidas.PasoAlturas,
                    semilla = semilla,
                    rios = rios.Length,
                    lado = cauces.lado,
                    alcance = cauces.alcance,
                    puntos = cauces.puntos,
                    datos = cauces.datos,
                    celdas = cauces.celdas,
                    indices = cauces.indices,
                    anchoRibera = anchos,
                    lagos = nl,
                    lagoDatos = lagoDatos,
                    lagoRadios = lagoRadios,
                    alturas = r.alturas,
                    cauce = r.cauce,
                    ribera = r.ribera,
                    nivelAgua = r.nivelAgua,
                }.Schedule(r.alturas.Length, 256).Complete();
            }
            anchos.Dispose();
            lagoDatos.Dispose();
            lagoRadios.Dispose();
        }

        [BurstCompile(CompileSynchronously = true, FloatMode = FloatMode.Fast)]
        struct JobRios : IJobParallelFor
        {
            public int res;
            public float2 origen;
            public float paso;
            public int semilla;
            public int rios;
            public int lado;
            public float alcance;
            [ReadOnly] public NativeArray<float4> puntos;
            [ReadOnly] public NativeArray<float4> datos;
            [ReadOnly] public NativeArray<int2> celdas;
            [ReadOnly] public NativeArray<int> indices;
            [ReadOnly] public NativeArray<float> anchoRibera;
            public int lagos;
            [ReadOnly] public NativeArray<float4> lagoDatos;
            [ReadOnly] public NativeArray<float> lagoRadios;
            public NativeArray<float> alturas;
            [WriteOnly] public NativeArray<float> cauce;
            [WriteOnly] public NativeArray<float> ribera;
            [WriteOnly] public NativeArray<float> nivelAgua;

            public void Execute(int indice)
            {
                int i = indice % res, j = indice / res;
                float2 p = origen + new float2(i, j) * paso;
                float h = alturas[indice];
                float cauceV = 0f, riberaV = 0f, agua = float.NaN;

                // Primero los lagos (con su orilla); luego cada rio por separado, quedandose con lo
                // mas hondo: en las confluencias los valles se funden sin escalones.
                for (int l = 0; l < lagos; l++) TallarLago(p, l, ref h, ref cauceV, ref riberaV, ref agua);
                float tallada = h;
                for (int rio = 0; rio < rios; rio++)
                {
                    float hr = h;
                    Tallar(p, rio, ref hr, ref cauceV, ref riberaV, ref agua);
                    tallada = math.min(tallada, hr);
                }

                alturas[indice] = tallada;
                cauce[indice] = cauceV;
                ribera[indice] = riberaV;
                nivelAgua[indice] = agua;
            }

            void Tallar(float2 p, int rio, ref float h, ref float cauceV, ref float riberaV, ref float agua)
            {
                int2 c = math.clamp((int2)math.floor((p - origen) / CaucesRio.Celda), 0, lado - 1);
                int2 lista = celdas[c.y * lado + c.x];
                if (lista.y == 0) return;

                float mejorD = float.MaxValue;
                float4 pa = 0f, da = 0f;
                for (int q = 0; q < lista.y; q++)
                {
                    int k = indices[lista.x + q];
                    if ((int)(datos[k].z + 0.5f) != rio) continue;
                    float d = Ruido.DistanciaSegmento(p, puntos[k].xy, puntos[k + 1].xy, out float t);
                    if (d < mejorD)
                    {
                        mejorD = d;
                        pa = math.lerp(puntos[k], puntos[k + 1], t);
                        da = math.lerp(datos[k], datos[k + 1], t);
                    }
                }
                if (mejorD > alcance) return;

                float nivel = pa.z, semiancho = pa.w, profundidad = da.x;
                float ancho = anchoRibera[rio];
                // Orilla irregular: el borde del agua entra y sale unos metros.
                float d2 = math.max(0f, mejorD + Ruido.Fbm(p * 0.03f + semilla * 0.1f + rio * 7f, 3) * semiancho * 0.25f);

                if (d2 < semiancho)
                {
                    float u = d2 / semiancho;
                    float lecho = nivel - profundidad * (1f - u * u) - 0.4f;
                    h = math.min(h, lecho);
                    cauceV = math.max(cauceV, 1f - u * u * 0.6f);
                    agua = math.isnan(agua) ? nivel : math.max(agua, nivel);
                }
                else if (d2 < semiancho + ancho)
                {
                    float u = (d2 - semiancho) / ancho;
                    // Talud de la orilla: del agua sube a la ribera y se funde con el terreno.
                    float orilla = nivel + 0.5f + u * u * 16f;
                    float mezcla = Ruido.SmoothStep(0f, 1f, u);
                    if (h > orilla) h = math.lerp(orilla, h, mezcla * mezcla);
                    riberaV = math.max(riberaV, 1f - u);
                }
            }

            float RadioLago(int l, float2 dir)
            {
                float a = math.atan2(dir.y, dir.x);
                if (a < 0f) a += math.PI * 2f;
                float f = a / (math.PI * 2f) * 64f;
                int i0 = (int)math.floor(f) % 64;
                int i1 = (i0 + 1) % 64;
                return math.lerp(lagoRadios[l * 64 + i0], lagoRadios[l * 64 + i1], f - math.floor(f));
            }

            void TallarLago(float2 p, int l, ref float h, ref float cauceV, ref float riberaV, ref float agua)
            {
                float4 ld = lagoDatos[l];
                float2 d = p - ld.xy;
                float dist = math.length(d);
                float radio = RadioLago(l, dist > 1e-3f ? d / dist : new float2(1f, 0f));
                if (dist > radio * 1.5f) return;
                float u = dist / radio;
                float nivel = ld.z, profundidad = ld.w;
                if (u < 1f)
                {
                    float lecho = nivel - 0.6f - (profundidad - 0.6f) * (1f - u * u);
                    h = lecho;
                    cauceV = math.max(cauceV, 1f);
                    agua = math.isnan(agua) ? nivel : math.max(agua, nivel);
                }
                else if (u < 1.45f)
                {
                    float v = (u - 1f) / 0.45f;
                    float orilla = nivel + 0.5f + v * v * 18f;
                    float mezcla = Ruido.SmoothStep(0f, 1f, v);
                    if (h > orilla) h = math.lerp(orilla, h, mezcla * mezcla);
                    float dique = nivel + 0.5f + v * 2f;
                    if (h < dique) h = math.lerp(dique, h, mezcla);
                    riberaV = math.max(riberaV, 1f - v);
                }
            }
        }

        // ================================================================== 6. Zonas y lugares
        /// <summary>
        /// Allana las zonas humanas a la cota media (o a la suya), marca el claro y la contaminacion.
        /// Las minas (Mina, Garimpo) son un hoyo en terrazas de 6 m.
        /// </summary>
        public static void AllanarZonas(Relieve r, IEnumerable<Zona> zonas)
        {
            int res = r.res;
            var me = r.medidas;
            foreach (var z in zonas)
            {
                // Cota de allanado: la media del terreno en el circulo interior.
                float cota = z.cota;
                if (float.IsNaN(cota))
                {
                    double suma = 0; int cuenta = 0;
                    for (int k = 0; k < 400; k++)
                    {
                        float a = k * 2.39996f, rr = z.radio * 0.7f * math.sqrt((k + 0.5f) / 400f);
                        float2 q = z.centro + rr * new float2(math.cos(a), math.sin(a));
                        suma += r.MuestraMundo(r.alturas, q);
                        cuenta++;
                    }
                    cota = (float)(suma / cuenta);
                    if (me.hayMar) cota = math.max(cota, me.nivelMar + 3f);
                }

                int2 min = math.max(0, (int2)math.floor((z.centro - z.radio * 1.6f - me.Origen) / me.PasoAlturas));
                int2 max = math.min(res - 1, (int2)math.ceil((z.centro + z.radio * 1.6f - me.Origen) / me.PasoAlturas));
                for (int j = min.y; j <= max.y; j++)
                for (int i = min.x; i <= max.x; i++)
                {
                    int idx = j * res + i;
                    float2 p = r.AMundo(i, j);
                    float d = math.distance(p, z.centro) + Ruido.Fbm(p * 0.02f + z.centro, 3) * z.radio * 0.12f;
                    float u = d / z.radio;
                    float m = 1f - Ruido.SmoothStep(0.75f, 1.35f, u);
                    if (m <= 0f) continue;
                    float objetivo = cota + Ruido.Fbm(p * 0.03f, 3) * 0.6f;
                    if (z.tipo == TipoZona.Mina || z.tipo == TipoZona.Garimpo)
                    {
                        // Hoyo en terrazas de 6 m; el fondo esta 36 m (mina) o 18 m (garimpo) por debajo.
                        float hondo = z.tipo == TipoZona.Mina ? 36f : 18f;
                        float prof = hondo * math.saturate(1f - u / 0.8f);
                        float escalon = math.floor(prof / 6f) * 6f + Ruido.SmoothStep(0.75f, 1f, math.frac(prof / 6f)) * 6f;
                        objetivo = cota - escalon;
                    }
                    if (math.isnan(r.nivelAgua[idx]))
                        r.alturas[idx] = math.lerp(r.alturas[idx], objetivo, m);
                    r.claro[idx] = math.max(r.claro[idx], Ruido.SmoothStep(1.05f, 0.7f, u));
                    float sucio = z.contaminacion * (1f - Ruido.SmoothStep(0.5f, 1.5f, u)) * (0.65f + 0.35f * Ruido.Fbm(p * 0.015f + 3.3f, 4));
                    r.contaminacion[idx] = math.max(r.contaminacion[idx], math.saturate(sucio));
                }
            }
        }

        /// <summary>
        /// Explanada a la cota media (con borde suave) y claro: templos, cementerios, aldeas.
        /// alturaLocal(p) da la forma tallada (gradas de un templo) sobre la cota, si la hay.
        /// </summary>
        public static void Allanar(Relieve r, float2 centro, float radio, float fuerza, Func<float2, float> alturaLocal = null, float cotaMinima = 4f)
        {
            if (fuerza <= 0f) return;
            int res = r.res;
            var me = r.medidas;
            double suma = 0;
            for (int k = 0; k < 400; k++)
            {
                float a = k * 2.39996f, rr = radio * 0.8f * math.sqrt((k + 0.5f) / 400f);
                suma += r.MuestraMundo(r.alturas, centro + rr * new float2(math.cos(a), math.sin(a)));
            }
            float cota = math.max((float)(suma / 400), cotaMinima);
            int2 min = math.max(0, (int2)math.floor((centro - radio * 1.8f - me.Origen) / me.PasoAlturas));
            int2 max = math.min(res - 1, (int2)math.ceil((centro + radio * 1.8f - me.Origen) / me.PasoAlturas));
            for (int j = min.y; j <= max.y; j++)
            for (int i = min.x; i <= max.x; i++)
            {
                int idx = j * res + i;
                float2 p = r.AMundo(i, j);
                float u = (math.distance(p, centro) + Ruido.Fbm(p * 0.02f + 5f, 3) * radio * 0.1f) / radio;
                float m = (1f - Ruido.SmoothStep(0.85f, 1.6f, u)) * fuerza;
                if (m <= 0f) continue;
                float objetivo = cota + Ruido.Fbm(p * 0.05f, 3) * 0.4f;
                if (alturaLocal != null) objetivo = cota + alturaLocal(p);
                if (math.isnan(r.nivelAgua[idx]))
                    r.alturas[idx] = math.lerp(r.alturas[idx], objetivo, m);
                r.claro[idx] = math.max(r.claro[idx], Ruido.SmoothStep(1.1f, 0.75f, u));
            }
        }

        // ================================================================== 7. Humedad
        /// <summary>Acumulacion de flujo D8 a la resolucion gruesa (ordenando por altura) + cercania al agua.</summary>
        public static void CalcularHumedad(Relieve r)
        {
            int rg = ResGruesa(r.medidas);
            var h = new NativeArray<float>(rg * rg, Allocator.TempJob);
            for (int j = 0; j < rg; j++)
            for (int i = 0; i < rg; i++)
                h[j * rg + i] = r.alturas[(j * FactorGrueso) * r.res + i * FactorGrueso];
            var acum = new NativeArray<float>(rg * rg, Allocator.TempJob);
            new JobFlujo { res = rg, alturas = h, acumulado = acum }.Run();
            new JobHumedad { res = r.res, resGruesa = rg, acumulado = acum, ribera = r.ribera, cauce = r.cauce, humedad = r.humedad }
                .Schedule(r.humedad.Length, 512).Complete();
            h.Dispose();
            acum.Dispose();
        }

        [BurstCompile(CompileSynchronously = true)]
        struct JobFlujo : IJob
        {
            public int res;
            [ReadOnly] public NativeArray<float> alturas;
            public NativeArray<float> acumulado;

            struct PorAltura : IComparer<int2>
            {
                public int Compare(int2 a, int2 b) => b.x.CompareTo(a.x);
            }

            public void Execute()
            {
                int n = res * res;
                var orden = new NativeArray<int2>(n, Allocator.Temp);
                for (int k = 0; k < n; k++)
                {
                    acumulado[k] = 1f;
                    // Altura en milimetros como clave entera para ordenar de mas alto a mas bajo.
                    orden[k] = new int2((int)(alturas[k] * 1000f), k);
                }
                orden.Sort(new PorAltura());
                for (int o = 0; o < n; o++)
                {
                    int k = orden[o].y;
                    int i = k % res, j = k / res;
                    if (i == 0 || j == 0 || i == res - 1 || j == res - 1) continue;
                    float h = alturas[k];
                    int destino = -1;
                    float mejor = 0f;
                    for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        if (dx == 0 && dy == 0) continue;
                        int v = k + dy * res + dx;
                        float caida = (h - alturas[v]) / ((dx != 0 && dy != 0) ? 1.4142f : 1f);
                        if (caida > mejor) { mejor = caida; destino = v; }
                    }
                    if (destino >= 0) acumulado[destino] += acumulado[k];
                }
                orden.Dispose();
            }
        }

        [BurstCompile(CompileSynchronously = true, FloatMode = FloatMode.Fast)]
        struct JobHumedad : IJobParallelFor
        {
            public int res, resGruesa;
            [ReadOnly] public NativeArray<float> acumulado;
            [ReadOnly] public NativeArray<float> ribera;
            [ReadOnly] public NativeArray<float> cauce;
            [WriteOnly] public NativeArray<float> humedad;

            public void Execute(int indice)
            {
                int i = indice % res, j = indice / res;
                float gx = i / (float)FactorGrueso, gy = j / (float)FactorGrueso;
                int x0 = math.min((int)gx, resGruesa - 2), y0 = math.min((int)gy, resGruesa - 2);
                float fx = gx - x0, fy = gy - y0;
                float a = acumulado[y0 * resGruesa + x0], b = acumulado[y0 * resGruesa + x0 + 1];
                float c = acumulado[(y0 + 1) * resGruesa + x0], d = acumulado[(y0 + 1) * resGruesa + x0 + 1];
                float acum = math.lerp(math.lerp(a, b, fx), math.lerp(c, d, fx), fy);
                // log10 de celdas drenadas: 1 celda = seco, ~10^3.5 = vaguada empapada.
                float escorrentia = math.saturate((math.log10(acum) - 0.6f) / 2.9f);
                humedad[indice] = math.saturate(math.max(escorrentia, ribera[indice] * 0.95f) + cauce[indice]);
            }
        }
    }
}
