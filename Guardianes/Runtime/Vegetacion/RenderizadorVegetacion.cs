using System;
using System.Collections.Generic;
using Guardianes.Mundo;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace Guardianes.Vegetacion
{
    /// <summary>
    /// Dibuja toda la vegetacion del mundo con un BatchRendererGroup: las matrices de todas las plantas viven en un buffer de
    /// la GPU, que no se vuelve a subir, y en cada vista que pide Unity (cada camara y cada luz con sombra) unos jobs Burst
    /// descartan celdas de 64 m y plantas, eligen el LOD por distancia y agrupan lo visible por (especie, LOD, cascadas).
    /// En una luz, cada planta solo entra en las cascadas que la necesitan (lo mismo que el GPU Resident Drawer) y, lejos de
    /// la camara, con un LOD menos que en la imagen. La matriz anterior es la misma que la actual: quietas no se mueven y
    /// sus vectores de movimiento solo llevan el viento. Sirve tambien fuera del modo juego.
    /// Las plantas derribadas se marcan y dejan de dibujarse (Derribar).
    /// </summary>
    [ExecuteAlways]
    [DefaultExecutionOrder(-40)]
    public unsafe class RenderizadorVegetacion : MonoBehaviour
    {
        public const int MaxLods = 4;
        const float Celda = 64f;
        const int MaxSubmallas = 3;
        // Por debajo de esto, la sombra usa el LOD de la imagen: de cerca se le ven las hojas.
        const float SombraFina = 40f;

        public EspecieVegetal[] especies = new EspecieVegetal[0];
        [Tooltip("Vegetacion (.bytes) que genera el constructor de la escena")]
        public TextAsset datos;
        [Range(0.25f, 2.5f)] public float multiplicadorDistancia = 1f;

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
        NativeArray<float> _distLod2;    // (tipo * MaxLods + lod) -> distancia^2 (escala 1)
        NativeArray<int> _numLods;
        NativeArray<byte> _conSombra;    // (tipo * MaxLods + lod) -> proyecta sombra
        NativeArray<byte> _subMallas;    // (tipo * MaxLods + lod) -> submallas de su malla
        NativeArray<BatchMeshID> _idMalla;         // (tipo * MaxLods + lod)
        NativeArray<BatchMaterialID> _idMaterial;  // (tipo * MaxLods + lod) * MaxSubmallas + submalla
        int _lado;
        float2 _origen;

        BatchRendererGroup _grupo;
        GraphicsBuffer _instancias;
        BatchID _lote;
        readonly List<BatchMeshID> _mallasRegistradas = new List<BatchMeshID>();
        readonly List<BatchMaterialID> _materialesRegistrados = new List<BatchMaterialID>();
        JobHandle _trabajo;
        bool _dibujar = true, _sombras = true;
        int _capa;

        void OnEnable()
        {
            Instancia = this;
            Cargar();
        }

        void OnDisable()
        {
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

            int cubetas = _tipos.Count * MaxLods;
            _distLod2 = new NativeArray<float>(cubetas, Allocator.Persistent);
            _numLods = new NativeArray<int>(_tipos.Count, Allocator.Persistent);
            _conSombra = new NativeArray<byte>(cubetas, Allocator.Persistent);
            _subMallas = new NativeArray<byte>(cubetas, Allocator.Persistent);
            _idMalla = new NativeArray<BatchMeshID>(cubetas, Allocator.Persistent);
            _idMaterial = new NativeArray<BatchMaterialID>(cubetas * MaxSubmallas, Allocator.Persistent);
            PrepararDistancias();
            PrepararGrupo();
        }

        void PrepararDistancias()
        {
            _trabajo.Complete();
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

        void PrepararGrupo()
        {
            int n = _matrices.Length;
            if (n == 0) return;
            _capa = gameObject.layer;
            _grupo = new BatchRendererGroup(AlCullear, IntPtr.Zero);
            _grupo.SetEnabledViewTypes(new[] { BatchCullingViewType.Camera, BatchCullingViewType.Light });
            var min = new float3(float.MaxValue);
            var max = new float3(float.MinValue);
            for (int c = 0; c < _celdaMin.Length; c++)
                if (_celdas[c].y > 0)
                {
                    min = math.min(min, _celdaMin[c]);
                    max = math.max(max, _celdaMax[c]);
                }
            _grupo.SetGlobalBounds(new Bounds((min + max) * 0.5f, max - min));

            // Los primeros 64 bytes, a cero (las propiedades que no vienen en los metadatos leen de ahi); luego las dos matrices
            // de 3x4 de cada instancia. La anterior es la misma: quietas.
            const int Cero = 64, Matriz = 48;
            int inicioM = Cero, inicioI = Cero + Matriz * n;
            var bytes = new NativeArray<float>((Cero + 2 * Matriz * n) / 4, Allocator.TempJob);
            new JobMatrices { matrices = _matrices, salida = bytes, inicioM = inicioM / 4, inicioI = inicioI / 4 }.Schedule(n, 2048).Complete();
            _instancias = new GraphicsBuffer(GraphicsBuffer.Target.Raw, bytes.Length, 4);
            _instancias.SetData(bytes);
            bytes.Dispose();

            const uint PorInstancia = 0x80000000;
            var metadatos = new NativeArray<MetadataValue>(4, Allocator.Temp);
            metadatos[0] = new MetadataValue { NameID = Shader.PropertyToID("unity_ObjectToWorld"), Value = PorInstancia | (uint)inicioM };
            metadatos[1] = new MetadataValue { NameID = Shader.PropertyToID("unity_WorldToObject"), Value = PorInstancia | (uint)inicioI };
            metadatos[2] = new MetadataValue { NameID = Shader.PropertyToID("unity_MatrixPreviousM"), Value = PorInstancia | (uint)inicioM };
            metadatos[3] = new MetadataValue { NameID = Shader.PropertyToID("unity_MatrixPreviousMI"), Value = PorInstancia | (uint)inicioI };
            _lote = _grupo.AddBatch(metadatos, _instancias.bufferHandle);
            metadatos.Dispose();

            var mallas = new Dictionary<Mesh, BatchMeshID>();
            var materiales = new Dictionary<Material, BatchMaterialID>();
            for (int t = 0; t < _tipos.Count; t++)
            {
                var esp = especies[_tipos[t].especie];
                int nl = math.min(esp.Lods, MaxLods);
                var mats = esp.Materiales;
                for (int l = 0; l < nl; l++)
                {
                    int b = t * MaxLods + l;
                    var malla = esp.Malla(_tipos[t].variante, l);
                    if (!mallas.TryGetValue(malla, out var idMalla))
                    {
                        idMalla = _grupo.RegisterMesh(malla);
                        mallas[malla] = idMalla;
                        _mallasRegistradas.Add(idMalla);
                    }
                    _idMalla[b] = idMalla;
                    var sombra = l < esp.sombras.Length ? esp.sombras[l] : ShadowCastingMode.On;
                    _conSombra[b] = (byte)(sombra != ShadowCastingMode.Off ? 1 : 0);
                    int subs = math.min(malla.subMeshCount, MaxSubmallas);
                    int validas = 0;
                    for (int s = 0; s < subs; s++)
                    {
                        var mat = s < mats.Length ? mats[s] : null;
                        if (mat == null) continue;
                        if (!materiales.TryGetValue(mat, out var idMat))
                        {
                            idMat = _grupo.RegisterMaterial(mat);
                            materiales[mat] = idMat;
                            _materialesRegistrados.Add(idMat);
                        }
                        _idMaterial[b * MaxSubmallas + s] = idMat;
                        validas = s + 1;
                    }
                    _subMallas[b] = (byte)validas;
                }
            }
        }

        void Liberar()
        {
            _trabajo.Complete();
            _trabajo = default;
            if (_grupo != null)
            {
                foreach (var m in _mallasRegistradas) _grupo.UnregisterMesh(m);
                foreach (var m in _materialesRegistrados) _grupo.UnregisterMaterial(m);
                _grupo.Dispose();
                _grupo = null;
            }
            _mallasRegistradas.Clear();
            _materialesRegistrados.Clear();
            _instancias?.Dispose();
            _instancias = null;
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
            if (_conSombra.IsCreated) _conSombra.Dispose();
            if (_subMallas.IsCreated) _subMallas.Dispose();
            if (_idMalla.IsCreated) _idMalla.Dispose();
            if (_idMaterial.IsCreated) _idMaterial.Dispose();
        }

        // ------------------------------------------------------------------ Lo que se dibuja en cada vista
        JobHandle AlCullear(BatchRendererGroup grupo, BatchCullingContext ctx, BatchCullingOutput salida, IntPtr usuario)
        {
            if (!_dibujar || !_matrices.IsCreated || (ctx.cullingLayerMask & (1u << _capa)) == 0) return default;
            bool sombra = ctx.viewType == BatchCullingViewType.Light;
            if (sombra && !_sombras) return default;

            int splits = math.clamp(ctx.cullingSplits.Length, 1, 6);
            var tramos = new NativeArray<int2>(splits, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
            int cuantos = 0;
            if (ctx.cullingSplits.Length == 0) { tramos[0] = new int2(0, ctx.cullingPlanes.Length); cuantos = ctx.cullingPlanes.Length; }
            else
                for (int s = 0; s < splits; s++)
                {
                    var corte = ctx.cullingSplits[s];
                    tramos[s] = new int2(cuantos, corte.cullingPlaneCount);
                    cuantos += corte.cullingPlaneCount;
                }
            var planos = new NativeArray<float4>(cuantos, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
            if (ctx.cullingSplits.Length == 0)
                for (int i = 0; i < cuantos; i++) planos[i] = Plano(ctx.cullingPlanes[i]);
            else
                for (int s = 0; s < splits; s++)
                {
                    var corte = ctx.cullingSplits[s];
                    for (int i = 0; i < corte.cullingPlaneCount; i++)
                        planos[tramos[s].x + i] = Plano(ctx.cullingPlanes[corte.cullingPlaneOffset + i]);
                }

            // Cascadas de una luz direccional: las esferas que cubre cada una (ver ReceiverSphereCuller en el GPU Resident Drawer).
            bool cascadas = sombra && splits > 1;
            for (int s = 0; cascadas && s < splits; s++)
                if (!(ctx.cullingSplits[s].sphereRadius > 0f)) cascadas = false;
            var esferasCascada = new NativeArray<float4>(cascadas ? splits : 0, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
            var mezclaCascada = new NativeArray<float>(cascadas ? splits : 0, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
            var planosLuz = new NativeArray<float4>(cascadas ? ctx.receiverPlaneCount : 0, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
            int numPlanosLuz = 0;
            float3x3 aLuz = float3x3.identity;
            if (cascadas)
            {
                float4x4 luz = ctx.localToWorldMatrix;
                aLuz = math.transpose(new float3x3(luz.c0.xyz, luz.c1.xyz, luz.c2.xyz));
                for (int s = 0; s < splits; s++)
                {
                    var corte = ctx.cullingSplits[s];
                    esferasCascada[s] = new float4(math.mul(aLuz, corte.sphereCenter), corte.sphereRadius);
                    mezclaCascada[s] = corte.cascadeBlendCullingFactor;
                }
                float3 dirLuz = -luz.c2.xyz;
                for (int i = 0; i < ctx.receiverPlaneCount; i++)
                {
                    var p = ctx.cullingPlanes[ctx.receiverPlaneOffset + i];
                    if (math.dot((float3)p.normal, dirLuz) >= 0f) planosLuz[numPlanosLuz++] = Plano(p);
                }
            }

            var visibles = new NativeArray<int>(_celdas.Length, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
            var inicios = new NativeArray<int>(_celdas.Length, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
            float3 camara = ctx.lodParameters.cameraPosition;
            int celdasVisibles = 0, total = 0;
            new JobCeldas
            {
                planos = planos,
                tramos = tramos,
                camara = camara,
                celdas = _celdas,
                celdaMin = _celdaMin,
                celdaMax = _celdaMax,
                distLod2 = _distLod2,
                visibles = visibles,
                inicios = inicios,
                celdasVisibles = &celdasVisibles,
                total = &total,
            }.Run();

            var entradas = new NativeArray<ulong>(total, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
            var cuentas = new NativeArray<int>(celdasVisibles, Allocator.TempJob, NativeArrayOptions.ClearMemory);
            var h = new JobPlantas
            {
                planos = planos,
                tramos = tramos,
                esferasCascada = esferasCascada,
                mezclaCascada = mezclaCascada,
                planosLuz = planosLuz,
                numPlanosLuz = numPlanosLuz,
                aLuz = aLuz,
                exclusion = sombra ? (uint)ctx.splitExclusionMask : 0u,
                sombra = sombra,
                camara = camara,
                celdas = _celdas,
                esferas = _esferas,
                especieDe = _especieDe,
                escalaDe = _escalaDe,
                vivo = _vivo,
                distLod2 = _distLod2,
                numLods = _numLods,
                conSombra = _conSombra,
                visibles = visibles,
                inicios = inicios,
                entradas = entradas,
                cuentas = cuentas,
            }.Schedule(celdasVisibles, 2);
            var filtro = new BatchFilterSettings
            {
                renderingLayerMask = 1,
                layer = (byte)_capa,
                motionMode = MotionVectorGenerationMode.Object,
                shadowCastingMode = ShadowCastingMode.On,
                receiveShadows = true,
                staticShadowCaster = false,
                allDepthSorted = false,
            };
            h = new JobComandos
            {
                inicios = inicios,
                entradas = entradas,
                cuentas = cuentas,
                celdasVisibles = celdasVisibles,
                bits = splits,
                cubetas = _tipos.Count * MaxLods,
                sombra = sombra,
                subMallas = _subMallas,
                idMalla = _idMalla,
                idMaterial = _idMaterial,
                lote = _lote,
                filtro = filtro,
                salida = (BatchCullingOutputDrawCommands*)salida.drawCommands.GetUnsafePtr(),
            }.Schedule(h);
            planos.Dispose(h);
            tramos.Dispose(h);
            esferasCascada.Dispose(h);
            mezclaCascada.Dispose(h);
            planosLuz.Dispose(h);
            visibles.Dispose(h);
            inicios.Dispose(h);
            entradas.Dispose(h);
            cuentas.Dispose(h);
            _trabajo = JobHandle.CombineDependencies(_trabajo, h);
            return h;
        }

        static float4 Plano(Plane p) => new float4(p.normal, p.distance);

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

        /// <summary>Cambia la distancia de dibujo (cada nivel de calidad) con la vegetacion ya cargada.</summary>
        public void PonerDistancia(float multiplicador)
        {
            multiplicadorDistancia = multiplicador;
            if (_distLod2.IsCreated) PrepararDistancias();
        }

        /// <summary>Para medir lo que cuesta (`MedicionRendimiento`): dejar de dibujarla o quitarle las sombras.</summary>
        public void Ablacion(bool dibujar, bool sombras)
        {
            _dibujar = dibujar;
            _sombras = sombras;
        }

        public EspecieVegetal EspecieDe(int indice) => especies[_tipos[_especieDe[indice]].especie];
        public int VarianteDe(int indice) => _tipos[_especieDe[indice]].variante;
        public float4x4 MatrizDe(int indice) => _matrices[indice];
        public float EscalaDe(int indice) => _escalaDe[indice];
        public bool Viva(int indice) => _vivo[indice] != 0;

        /// <summary>Quita la planta del dibujo (la sustituye quien la derriba por su version fisica).</summary>
        public void Derribar(int indice)
        {
            _trabajo.Complete();
            if (_vivo.IsCreated && indice >= 0 && indice < _vivo.Length) _vivo[indice] = 0;
        }

        /// <summary>La vuelve a dibujar (una planta que rebrota; Derribos.Restaurar).</summary>
        public void Restaurar(int indice)
        {
            _trabajo.Complete();
            if (_vivo.IsCreated && indice >= 0 && indice < _vivo.Length) _vivo[indice] = 1;
        }

        // ------------------------------------------------------------------ Jobs
        [BurstCompile(FloatMode = FloatMode.Fast)]
        struct JobMatrices : IJobParallelFor
        {
            [ReadOnly] public NativeArray<float4x4> matrices;
            [NativeDisableParallelForRestriction] public NativeArray<float> salida;
            public int inicioM, inicioI;

            public void Execute(int i)
            {
                float4x4 m = matrices[i];
                Escribir(inicioM + i * 12, m);
                Escribir(inicioI + i * 12, math.inverse(m));
            }

            void Escribir(int o, in float4x4 m)
            {
                salida[o] = m.c0.x; salida[o + 1] = m.c0.y; salida[o + 2] = m.c0.z;
                salida[o + 3] = m.c1.x; salida[o + 4] = m.c1.y; salida[o + 5] = m.c1.z;
                salida[o + 6] = m.c2.x; salida[o + 7] = m.c2.y; salida[o + 8] = m.c2.z;
                salida[o + 9] = m.c3.x; salida[o + 10] = m.c3.y; salida[o + 11] = m.c3.z;
            }
        }

        // Las celdas que puede ver esta vista, y donde empieza cada una en las entradas.
        [BurstCompile(FloatMode = FloatMode.Fast)]
        struct JobCeldas : IJob
        {
            [ReadOnly] public NativeArray<float4> planos;
            [ReadOnly] public NativeArray<int2> tramos;
            public float3 camara;
            [ReadOnly] public NativeArray<int2> celdas;
            [ReadOnly] public NativeArray<float3> celdaMin, celdaMax;
            [ReadOnly] public NativeArray<float> distLod2;
            public NativeArray<int> visibles, inicios;
            [NativeDisableUnsafePtrRestriction] public int* celdasVisibles;
            [NativeDisableUnsafePtrRestriction] public int* total;

            public void Execute()
            {
                float maxD2 = 0f;
                for (int b = 0; b < distLod2.Length; b++) maxD2 = math.max(maxD2, distLod2[b]);
                float limite = maxD2 * 9f; // plantas de hasta 3x su tamano base
                int n = 0, suma = 0;
                for (int c = 0; c < celdas.Length; c++)
                {
                    int2 cel = celdas[c];
                    if (cel.y == 0) continue;
                    float3 mn = celdaMin[c], mx = celdaMax[c];
                    if (math.distancesq(math.clamp(camara, mn, mx), camara) > limite) continue;
                    if (!EnAlgunTramo(mn, mx)) continue;
                    visibles[n] = c;
                    inicios[n] = suma;
                    n++;
                    suma += cel.y;
                }
                *celdasVisibles = n;
                *total = suma;
            }

            bool EnAlgunTramo(float3 mn, float3 mx)
            {
                for (int s = 0; s < tramos.Length; s++)
                {
                    int2 t = tramos[s];
                    bool dentro = true;
                    for (int p = t.x; p < t.x + t.y && dentro; p++)
                    {
                        float4 pl = planos[p];
                        float3 v = math.select(mn, mx, pl.xyz >= 0f);
                        if (math.dot(pl.xyz, v) + pl.w < 0f) dentro = false;
                    }
                    if (dentro) return true;
                }
                return false;
            }
        }

        // Cada planta de una celda: si se ve, con que LOD y en que cascadas. Una entrada: indice | cubeta << 32 | mascara << 48.
        [BurstCompile(FloatMode = FloatMode.Fast)]
        struct JobPlantas : IJobParallelFor
        {
            [ReadOnly] public NativeArray<float4> planos;
            [ReadOnly] public NativeArray<int2> tramos;
            [ReadOnly] public NativeArray<float4> esferasCascada;
            [ReadOnly] public NativeArray<float> mezclaCascada;
            [ReadOnly] public NativeArray<float4> planosLuz;
            public int numPlanosLuz;
            public float3x3 aLuz;
            public uint exclusion;
            public bool sombra;
            public float3 camara;
            [ReadOnly] public NativeArray<int2> celdas;
            [ReadOnly] public NativeArray<float4> esferas;
            [ReadOnly] public NativeArray<int> especieDe;
            [ReadOnly] public NativeArray<float> escalaDe;
            [ReadOnly] public NativeArray<byte> vivo;
            [ReadOnly] public NativeArray<float> distLod2;
            [ReadOnly] public NativeArray<int> numLods;
            [ReadOnly] public NativeArray<byte> conSombra;
            [ReadOnly] public NativeArray<int> visibles, inicios;
            [NativeDisableParallelForRestriction] public NativeArray<ulong> entradas;
            [NativeDisableParallelForRestriction] public NativeArray<int> cuentas;

            public void Execute(int v)
            {
                int2 cel = celdas[visibles[v]];
                int o = inicios[v], n = 0;
                for (int k = cel.x; k < cel.x + cel.y; k++)
                {
                    if (vivo[k] == 0) continue;
                    float4 esf = esferas[k];
                    int t = especieDe[k];
                    float s = escalaDe[k];
                    float d2 = math.distancesq(esf.xyz, camara);
                    float d2Escala = d2 / (s * s);
                    int nl = numLods[t];
                    int lod = -1;
                    for (int l = 0; l < nl; l++)
                        if (d2Escala <= distLod2[t * MaxLods + l]) { lod = l; break; }
                    if (lod < 0) continue;
                    if (sombra)
                    {
                        if (d2 > SombraFina * SombraFina && lod + 1 < nl && conSombra[t * MaxLods + lod + 1] != 0) lod++;
                        if (conSombra[t * MaxLods + lod] == 0) continue;
                    }
                    uint mascara = Mascara(esf);
                    if (esferasCascada.Length > 0 && mascara != 0) mascara &= MascaraCascadas(esf);
                    mascara &= ~exclusion;
                    if (mascara == 0) continue;
                    entradas[o + n] = (ulong)(uint)k | ((ulong)(uint)(t * MaxLods + lod) << 32) | ((ulong)mascara << 48);
                    n++;
                }
                cuentas[v] = n;
            }

            uint Mascara(float4 esf)
            {
                uint m = 0;
                for (int s = 0; s < tramos.Length; s++)
                {
                    int2 t = tramos[s];
                    bool dentro = true;
                    for (int p = t.x; p < t.x + t.y && dentro; p++)
                    {
                        float4 pl = planos[p];
                        if (math.dot(pl.xyz, esf.xyz) + pl.w < -esf.w) dentro = false;
                    }
                    if (dentro) m |= 1u << s;
                }
                return m;
            }

            // Lo mismo que ReceiverSphereCuller del GPU Resident Drawer: la planta entra en la cascada si su sombra puede caer en
            // su esfera, y si la sombra entera cabe en el nucleo de una cascada no hace falta en las mayores.
            uint MascaraCascadas(float4 esf)
            {
                float3 centroLuz = math.mul(aLuz, esf.xyz);
                float radio = esf.w;
                float3 dirSombra = math.transpose(aLuz).c2;
                float largoSombra = float.PositiveInfinity;
                for (int i = 0; i < numPlanosLuz; i++)
                {
                    float4 pl = planosLuz[i];
                    float cos = math.max(math.abs(math.dot(pl.xyz, dirSombra)), 0.001f);
                    float sobre = (math.dot(pl.xyz, esf.xyz) + pl.w) / cos;
                    float sen = math.sqrt(math.max(1f - cos * cos, 0f));
                    largoSombra = math.min(largoSombra, sobre + radio * sen / cos);
                }
                largoSombra = math.max(largoSombra, 0f);

                uint m = 0;
                for (int s = 0; s < esferasCascada.Length; s++)
                {
                    float4 receptor = esferasCascada[s];
                    float3 aPlanta = centroLuz - receptor.xyz;
                    float maxDist = radio + receptor.w;
                    float z2 = maxDist * maxDist - math.lengthsq(aPlanta.xy);
                    if (z2 < 0f) continue;
                    if (aPlanta.z > 0f && aPlanta.z * aPlanta.z > z2) continue;
                    m |= 1u << s;
                    float nucleo = receptor.w * mezclaCascada[s] - radio;
                    float3 finSombra = aPlanta + new float3(0f, 0f, largoSombra);
                    float dist2 = math.max(math.lengthsq(aPlanta), math.lengthsq(finSombra));
                    if (nucleo > 0f && dist2 < nucleo * nucleo) break;
                }
                return m;
            }
        }

        // Agrupa lo visible por (cubeta, mascara) y escribe la lista que pide Unity.
        [BurstCompile]
        struct JobComandos : IJob
        {
            [ReadOnly] public NativeArray<int> inicios, cuentas;
            [ReadOnly] public NativeArray<ulong> entradas;
            public int celdasVisibles, bits, cubetas;
            public bool sombra;
            [ReadOnly] public NativeArray<byte> subMallas;
            [ReadOnly] public NativeArray<BatchMeshID> idMalla;
            [ReadOnly] public NativeArray<BatchMaterialID> idMaterial;
            public BatchID lote;
            public BatchFilterSettings filtro;
            [NativeDisableUnsafePtrRestriction] public BatchCullingOutputDrawCommands* salida;

            public void Execute()
            {
                int claves = cubetas << bits;
                var porClave = new NativeArray<int>(claves, Allocator.Temp, NativeArrayOptions.ClearMemory);
                int total = 0;
                for (int v = 0; v < celdasVisibles; v++)
                {
                    int o = inicios[v], n = cuentas[v];
                    for (int i = 0; i < n; i++) porClave[Clave(entradas[o + i])]++;
                    total += n;
                }
                if (total == 0) return;

                var inicio = new NativeArray<int>(claves, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
                int acumulado = 0, comandos = 0;
                for (int c = 0; c < claves; c++)
                {
                    inicio[c] = acumulado;
                    if (porClave[c] == 0) continue;
                    acumulado += porClave[c];
                    comandos += subMallas[c >> bits];
                }
                if (comandos == 0) return;

                var visibles = (int*)UnsafeUtility.Malloc(sizeof(int) * total, UnsafeUtility.AlignOf<int>(), Allocator.TempJob);
                var escrito = new NativeArray<int>(claves, Allocator.Temp, NativeArrayOptions.ClearMemory);
                for (int v = 0; v < celdasVisibles; v++)
                {
                    int o = inicios[v], n = cuentas[v];
                    for (int i = 0; i < n; i++)
                    {
                        ulong e = entradas[o + i];
                        int c = Clave(e);
                        visibles[inicio[c] + escrito[c]] = (int)(uint)e;
                        escrito[c]++;
                    }
                }

                var lista = (BatchDrawCommand*)UnsafeUtility.Malloc(UnsafeUtility.SizeOf<BatchDrawCommand>() * comandos, UnsafeUtility.AlignOf<BatchDrawCommand>(), Allocator.TempJob);
                var flags = sombra ? BatchDrawCommandFlags.None : BatchDrawCommandFlags.HasMotion;
                int d = 0;
                for (int c = 0; c < claves; c++)
                {
                    int n = porClave[c];
                    if (n == 0) continue;
                    int b = c >> bits;
                    uint mascara = (uint)(c & ((1 << bits) - 1));
                    for (int s = 0; s < subMallas[b]; s++)
                        lista[d++] = new BatchDrawCommand
                        {
                            visibleOffset = (uint)inicio[c],
                            visibleCount = (uint)n,
                            batchID = lote,
                            materialID = idMaterial[b * MaxSubmallas + s],
                            meshID = idMalla[b],
                            submeshIndex = (ushort)s,
                            splitVisibilityMask = sombra ? (ushort)mascara : (ushort)0xff,
                            flags = flags,
                            sortingPosition = 0,
                            lightmapIndex = 0xffff,
                            activeMeshLod = 0xffff,
                        };
                }

                var rango = (BatchDrawRange*)UnsafeUtility.Malloc(UnsafeUtility.SizeOf<BatchDrawRange>(), UnsafeUtility.AlignOf<BatchDrawRange>(), Allocator.TempJob);
                *rango = new BatchDrawRange { drawCommandsBegin = 0, drawCommandsCount = (uint)d, filterSettings = filtro };

                salida->visibleInstances = visibles;
                salida->visibleInstanceCount = total;
                salida->drawCommands = lista;
                salida->drawCommandCount = d;
                salida->drawRanges = rango;
                salida->drawRangeCount = 1;
                salida->instanceSortingPositions = null;
                salida->instanceSortingPositionFloatCount = 0;
            }

            int Clave(ulong e) => (int)((e >> 32) & 0xffff) << bits | (int)(e >> 48);
        }
    }
}
