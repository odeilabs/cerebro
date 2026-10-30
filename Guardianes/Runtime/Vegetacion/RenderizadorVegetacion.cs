using System.Collections.Generic;
using Guardianes.Mundo;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace Guardianes.Vegetacion
{
    /// <summary>
    /// Dibuja toda la vegetacion del mundo con instancing de GPU: las plantas se agrupan en celdas
    /// de 64 m; por cada camara (juego, escena o capturas) un job Burst descarta celdas fuera del
    /// frustum (ampliado para no perder sombras), elige el LOD de cada planta por distancia y
    /// agrupa las matrices por (especie, LOD). Se envian con Graphics.RenderMeshInstanced en
    /// beginCameraRendering, antes del culling de HDRP, asi que sirve tambien fuera del modo juego.
    /// Las plantas derribadas se marcan y dejan de dibujarse (Derribar).
    /// </summary>
    [ExecuteAlways]
    [DefaultExecutionOrder(-40)]
    public class RenderizadorVegetacion : MonoBehaviour
    {
        public const int MaxLods = 4;
        const float Celda = 64f;

        public EspecieVegetal[] especies = new EspecieVegetal[0];
        [Tooltip("Vegetacion (.bytes) que genera el constructor de la escena")]
        public TextAsset datos;
        [Range(0.25f, 2.5f)] public float multiplicadorDistancia = 1f;
        [Tooltip("Las celdas fuera de camara a menos de esto aun se dibujan (sombras que entran en plano)")]
        public float margenSombras = 50f;

        public static RenderizadorVegetacion Instancia { get; private set; }
        public int TotalInstancias => _especieDe.IsCreated ? _especieDe.Length : 0;

        // Instancias ordenadas por celda.
        NativeArray<float4x4> _matrices;
        NativeArray<float4> _esferas;    // centro (mundo) y radio
        NativeArray<int> _especieDe;     // tipo (especie, variante) de cada instancia
        NativeArray<float> _escalaDe;
        NativeArray<byte> _vivo;
        NativeArray<int2> _celdas;       // inicio, cuenta
        NativeArray<float3> _celdaMin, _celdaMax;
        NativeArray<float> _distLod2;    // (especie * MaxLods + lod) -> distancia^2 (escala 1)
        NativeArray<int> _numLods;
        int _lado;
        float2 _origen;

        class PorCamara
        {
            public NativeArray<float4x4> salida;
            public NativeArray<int> inicio, cuenta;
        }
        readonly Dictionary<Camera, PorCamara> _camaras = new Dictionary<Camera, PorCamara>();
        RenderParams[][][] _params; // [tipo][lod][submalla]
        Mesh[][] _mallas;           // [tipo][lod]
        readonly Plane[] _planos = new Plane[6];

        void OnEnable()
        {
            Instancia = this;
            Cargar();
            RenderPipelineManager.beginCameraRendering += AlEmpezarCamara;
        }

        void OnDisable()
        {
            RenderPipelineManager.beginCameraRendering -= AlEmpezarCamara;
            Liberar();
            if (Instancia == this) Instancia = null;
        }

        void OnValidate()
        {
            if (isActiveAndEnabled && _especieDe.IsCreated) PrepararDistancias();
        }

        // ------------------------------------------------------------------ Carga
        // Tipos de dibujo: cada (especie, variante) tiene sus propias mallas.
        struct Tipo { public int especie; public int variante; }
        readonly List<Tipo> _tipos = new List<Tipo>();

        public void Cargar()
        {
            Liberar();
            if (datos == null || especies == null || especies.Length == 0) return;
            var porNombre = DatosVegetacion.Leer(datos.bytes);

            _tipos.Clear();
            var primerTipo = new int[especies.Length];
            for (int e = 0; e < especies.Length; e++)
            {
                primerTipo[e] = _tipos.Count;
                if (especies[e] == null) continue;
                especies[e].Preparar();
                for (int v = 0; v < especies[e].variantes; v++) _tipos.Add(new Tipo { especie = e, variante = v });
            }

            var todas = new List<(int celda, int tipo, InstanciaVegetal i)>();
            var me = Mapa.Medidas;
            _origen = me.Origen;
            _lado = (int)math.ceil(me.tamano / Celda);
            for (int e = 0; e < especies.Length; e++)
            {
                if (especies[e] == null || !porNombre.TryGetValue(especies[e].name, out var lista)) continue;
                foreach (var i in lista)
                {
                    int2 c = math.clamp((int2)math.floor((i.posicion.xz - _origen) / Celda), 0, _lado - 1);
                    int tipo = primerTipo[e] + math.min((int)i.variante, especies[e].variantes - 1);
                    todas.Add((c.y * _lado + c.x, tipo, i));
                }
            }
            todas.Sort((a, b) => a.celda != b.celda ? a.celda.CompareTo(b.celda) : a.tipo.CompareTo(b.tipo));

            int n = todas.Count;
            _matrices = new NativeArray<float4x4>(n, Allocator.Persistent);
            _esferas = new NativeArray<float4>(n, Allocator.Persistent);
            _especieDe = new NativeArray<int>(n, Allocator.Persistent);
            _escalaDe = new NativeArray<float>(n, Allocator.Persistent);
            _vivo = new NativeArray<byte>(n, Allocator.Persistent);
            _celdas = new NativeArray<int2>(_lado * _lado, Allocator.Persistent);
            _celdaMin = new NativeArray<float3>(_lado * _lado, Allocator.Persistent);
            _celdaMax = new NativeArray<float3>(_lado * _lado, Allocator.Persistent);
            for (int c = 0; c < _celdaMin.Length; c++) { _celdaMin[c] = new float3(float.MaxValue); _celdaMax[c] = new float3(float.MinValue); }

            for (int k = 0; k < n; k++)
            {
                var (celda, tipo, i) = todas[k];
                var esp = especies[_tipos[tipo].especie];
                var lim = esp.Limites;
                _matrices[k] = DatosVegetacion.Matriz(i);
                float3 centro = i.posicion + new float3(0f, lim.center.y * i.escala, 0f);
                float radio = lim.extents.magnitude * i.escala;
                _esferas[k] = new float4(centro, radio);
                _especieDe[k] = tipo;
                _escalaDe[k] = i.escala;
                _vivo[k] = 1;
                var cel = _celdas[celda];
                if (cel.y == 0) cel.x = k;
                cel.y++;
                _celdas[celda] = cel;
                _celdaMin[celda] = math.min(_celdaMin[celda], centro - radio);
                _celdaMax[celda] = math.max(_celdaMax[celda], centro + radio);
            }

            _distLod2 = new NativeArray<float>(_tipos.Count * MaxLods, Allocator.Persistent);
            _numLods = new NativeArray<int>(_tipos.Count, Allocator.Persistent);
            PrepararDistancias();
            PrepararParametros();
        }

        void PrepararDistancias()
        {
            for (int t = 0; t < _tipos.Count; t++)
            {
                var esp = especies[_tipos[t].especie];
                int nl = math.min(esp.Lods, MaxLods);
                _numLods[t] = nl;
                for (int l = 0; l < MaxLods; l++)
                {
                    float d = l < nl ? esp.distancias[l] * multiplicadorDistancia : 0f;
                    _distLod2[t * MaxLods + l] = d * d;
                }
            }
        }

        void PrepararParametros()
        {
            var limites = new Bounds(Vector3.zero, new Vector3(Mapa.Medidas.tamano * 1.5f, 3000f, Mapa.Medidas.tamano * 1.5f));
            _params = new RenderParams[_tipos.Count][][];
            _mallas = new Mesh[_tipos.Count][];
            for (int t = 0; t < _tipos.Count; t++)
            {
                var esp = especies[_tipos[t].especie];
                int nl = math.min(esp.Lods, MaxLods);
                var mats = esp.Materiales;
                _params[t] = new RenderParams[nl][];
                _mallas[t] = new Mesh[nl];
                for (int l = 0; l < nl; l++)
                {
                    var malla = esp.Malla(_tipos[t].variante, l);
                    _mallas[t][l] = malla;
                    var sombra = l < esp.sombras.Length ? esp.sombras[l] : ShadowCastingMode.On;
                    _params[t][l] = new RenderParams[malla.subMeshCount];
                    for (int s = 0; s < malla.subMeshCount; s++)
                    {
                        _params[t][l][s] = new RenderParams(s < mats.Length ? mats[s] : null)
                        {
                            worldBounds = limites,
                            shadowCastingMode = sombra,
                            receiveShadows = true,
                            motionVectorMode = MotionVectorGenerationMode.Object,
                            layer = gameObject.layer,
                            lightProbeUsage = LightProbeUsage.BlendProbes,
                        };
                    }
                }
            }
        }

        void Liberar()
        {
            foreach (var pc in _camaras.Values)
            {
                if (pc.salida.IsCreated) pc.salida.Dispose();
                if (pc.inicio.IsCreated) pc.inicio.Dispose();
                if (pc.cuenta.IsCreated) pc.cuenta.Dispose();
            }
            _camaras.Clear();
            if (_matrices.IsCreated) _matrices.Dispose();
            if (_esferas.IsCreated) _esferas.Dispose();
            if (_especieDe.IsCreated) _especieDe.Dispose();
            if (_escalaDe.IsCreated) _escalaDe.Dispose();
            if (_vivo.IsCreated) _vivo.Dispose();
            if (_celdas.IsCreated) _celdas.Dispose();
            if (_celdaMin.IsCreated) _celdaMin.Dispose();
            if (_celdaMax.IsCreated) _celdaMax.Dispose();
            if (_distLod2.IsCreated) _distLod2.Dispose();
            if (_numLods.IsCreated) _numLods.Dispose();
        }

        // ------------------------------------------------------------------ Dibujo por camara
        void AlEmpezarCamara(ScriptableRenderContext ctx, Camera cam)
        {
            if (!_matrices.IsCreated || _matrices.Length == 0) return;
            if (cam.cameraType == CameraType.Preview || cam.cameraType == CameraType.Reflection) return;

            if (!_camaras.TryGetValue(cam, out var pc))
            {
                pc = new PorCamara
                {
                    salida = new NativeArray<float4x4>(_matrices.Length, Allocator.Persistent),
                    inicio = new NativeArray<int>(_tipos.Count * MaxLods, Allocator.Persistent),
                    cuenta = new NativeArray<int>(_tipos.Count * MaxLods, Allocator.Persistent),
                };
                _camaras[cam] = pc;
            }

            GeometryUtility.CalculateFrustumPlanes(cam, _planos);
            var planos = new NativeArray<float4>(6, Allocator.TempJob);
            for (int i = 0; i < 6; i++)
            {
                // El plano cercano no se amplia (lo de detras de la camara no se ve ni sombrea casi).
                float margen = i == 4 ? 0f : margenSombras;
                planos[i] = new float4(_planos[i].normal, _planos[i].distance + margen);
            }

            new JobCulling
            {
                planos = planos,
                camara = cam.transform.position,
                celdas = _celdas,
                celdaMin = _celdaMin,
                celdaMax = _celdaMax,
                matrices = _matrices,
                esferas = _esferas,
                especieDe = _especieDe,
                escalaDe = _escalaDe,
                vivo = _vivo,
                distLod2 = _distLod2,
                numLods = _numLods,
                salida = pc.salida,
                inicio = pc.inicio,
                cuenta = pc.cuenta,
            }.Run();
            planos.Dispose();

            for (int t = 0; t < _tipos.Count; t++)
            {
                var porLod = _params[t];
                for (int l = 0; l < porLod.Length; l++)
                {
                    int b = t * MaxLods + l;
                    int n = pc.cuenta[b];
                    if (n == 0) continue;
                    var malla = _mallas[t][l];
                    for (int s = 0; s < porLod[l].Length; s++)
                    {
                        var rp = porLod[l][s];
                        if (rp.material == null) continue;
                        rp.camera = cam;
                        Graphics.RenderMeshInstanced(rp, malla, s, pc.salida.Reinterpret<Matrix4x4>(), n, pc.inicio[b]);
                    }
                }
            }
        }

        // ------------------------------------------------------------------ Consultas y destruccion
        /// <summary>Indices de las plantas vivas cuyo tronco esta a menos de 'radio' de 'centro' (XZ).</summary>
        public void Buscar(float3 centro, float radio, List<int> resultado)
        {
            resultado.Clear();
            if (!_celdas.IsCreated) return;
            int2 c0 = math.clamp((int2)math.floor((centro.xz - radio - _origen) / Celda), 0, _lado - 1);
            int2 c1 = math.clamp((int2)math.floor((centro.xz + radio - _origen) / Celda), 0, _lado - 1);
            for (int y = c0.y; y <= c1.y; y++)
            for (int x = c0.x; x <= c1.x; x++)
            {
                var cel = _celdas[y * _lado + x];
                for (int k = cel.x; k < cel.x + cel.y; k++)
                {
                    if (_vivo[k] == 0) continue;
                    float3 p = _matrices[k].c3.xyz;
                    if (math.distancesq(p.xz, centro.xz) <= radio * radio) resultado.Add(k);
                }
            }
        }

        public EspecieVegetal EspecieDe(int indice) => especies[_tipos[_especieDe[indice]].especie];
        public int VarianteDe(int indice) => _tipos[_especieDe[indice]].variante;
        public float4x4 MatrizDe(int indice) => _matrices[indice];
        public float EscalaDe(int indice) => _escalaDe[indice];
        public bool Viva(int indice) => _vivo[indice] != 0;

        /// <summary>Quita la planta del dibujo (la sustituye quien la derriba por su version fisica).</summary>
        public void Derribar(int indice)
        {
            if (_vivo.IsCreated && indice >= 0 && indice < _vivo.Length) _vivo[indice] = 0;
        }

        /// <summary>La vuelve a dibujar (una planta que rebrota; Derribos.Restaurar).</summary>
        public void Restaurar(int indice)
        {
            if (_vivo.IsCreated && indice >= 0 && indice < _vivo.Length) _vivo[indice] = 1;
        }

        // ------------------------------------------------------------------ Job
        [BurstCompile(CompileSynchronously = true, FloatMode = FloatMode.Fast)]
        struct JobCulling : IJob
        {
            [ReadOnly] public NativeArray<float4> planos;
            public float3 camara;
            [ReadOnly] public NativeArray<int2> celdas;
            [ReadOnly] public NativeArray<float3> celdaMin, celdaMax;
            [ReadOnly] public NativeArray<float4x4> matrices;
            [ReadOnly] public NativeArray<float4> esferas;
            [ReadOnly] public NativeArray<int> especieDe;
            [ReadOnly] public NativeArray<float> escalaDe;
            [ReadOnly] public NativeArray<byte> vivo;
            [ReadOnly] public NativeArray<float> distLod2;
            [ReadOnly] public NativeArray<int> numLods;
            public NativeArray<float4x4> salida;
            public NativeArray<int> inicio, cuenta;

            public void Execute()
            {
                int cubetas = cuenta.Length;
                for (int b = 0; b < cubetas; b++) cuenta[b] = 0;

                // Distancia maxima de cualquier LOD (para descartar celdas enteras).
                float maxD2 = 0f;
                for (int b = 0; b < cubetas; b++) maxD2 = math.max(maxD2, distLod2[b]);
                float maxEscala2 = 9f; // plantas de hasta 3x su tamano base

                var eleccion = new NativeArray<int>(matrices.Length, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
                var cubetaDe = new NativeArray<short>(matrices.Length, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
                int elegidas = 0;

                for (int c = 0; c < celdas.Length; c++)
                {
                    int2 cel = celdas[c];
                    if (cel.y == 0) continue;
                    float3 mn = celdaMin[c], mx = celdaMax[c];
                    float3 cerca = math.clamp(camara, mn, mx);
                    if (math.distancesq(cerca, camara) > maxD2 * maxEscala2) continue;
                    if (!CajaVisible(mn, mx)) continue;

                    for (int k = cel.x; k < cel.x + cel.y; k++)
                    {
                        if (vivo[k] == 0) continue;
                        float4 esf = esferas[k];
                        if (!EsferaVisible(esf)) continue;
                        int e = especieDe[k];
                        float s = escalaDe[k];
                        float d2 = math.distancesq(esf.xyz, camara) / (s * s);
                        int nl = numLods[e];
                        int lod = -1;
                        for (int l = 0; l < nl; l++)
                        {
                            if (d2 <= distLod2[e * 4 + l]) { lod = l; break; }
                        }
                        if (lod < 0) continue;
                        int b = e * 4 + lod;
                        eleccion[elegidas] = k;
                        cubetaDe[elegidas] = (short)b;
                        elegidas++;
                        cuenta[b]++;
                    }
                }

                int acumulado = 0;
                for (int b = 0; b < cubetas; b++)
                {
                    inicio[b] = acumulado;
                    acumulado += cuenta[b];
                }
                var escrito = new NativeArray<int>(cubetas, Allocator.Temp);
                for (int i = 0; i < elegidas; i++)
                {
                    int b = cubetaDe[i];
                    salida[inicio[b] + escrito[b]] = matrices[eleccion[i]];
                    escrito[b]++;
                }
                escrito.Dispose();
                eleccion.Dispose();
                cubetaDe.Dispose();
            }

            bool EsferaVisible(float4 esf)
            {
                for (int p = 0; p < 6; p++)
                {
                    float4 pl = planos[p];
                    if (math.dot(pl.xyz, esf.xyz) + pl.w < -esf.w) return false;
                }
                return true;
            }

            bool CajaVisible(float3 mn, float3 mx)
            {
                for (int p = 0; p < 6; p++)
                {
                    float4 pl = planos[p];
                    float3 v = math.select(mn, mx, pl.xyz >= 0f);
                    if (math.dot(pl.xyz, v) + pl.w < 0f) return false;
                }
                return true;
            }
        }
    }
}
