using System.Collections;
using System.Collections.Generic;
using Guardianes.Combate;
using Guardianes.Criaturas;
using Guardianes.Mundo;
using Guardianes.Vegetacion;
using Unity.Mathematics;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Guardianes.Enemigos
{
    /// <summary>
    /// Helicoptero de ataque de la compania. Vuela con un modelo fisico sencillo: el rotor empuja a lo
    /// largo de su eje (hasta 123 kN con 8 t al despegue, del BEMT de Lur Aerospace) y para acelerar
    /// tiene que inclinar antes el disco, que sigue al mando con retraso; el fuselaje cuelga del disco
    /// y gira alrededor de su centro de masas. La IA:
    ///   En tierra (helipuerto) · Despegue: el rotor coge vueltas y sube en vertical · Patrulla sobre
    ///   su zona · Ataque: orbita a la criatura por encima del dosel con el morro hacia ella, rafagas
    ///   del canon de la torreta y salvas de cohetes · Pasada: cada poco baja a ametrallarla en linea
    ///   recta (en un claro queda a su alcance) · Intimidar (rugido, siseo) desestabiliza al piloto ·
    ///   Agarrado por la criatura (Lanzable volador) y lanzado: explota al chocar · Derribado: cae
    ///   girando sobre si mismo (sin rotor de cola), explota y quedan los restos ardiendo.
    /// Los efectos (chorro del rotor, humo) los pinta EfectosCombate mirando Todos; las piezas, los
    /// materiales y el sonido del rotor los pone el editor (ConstructorHelicoptero).
    /// </summary>
    [RequireComponent(typeof(Salud), typeof(Rigidbody), typeof(Lanzable))]
    public class Helicoptero : MonoBehaviour, IInteractorVegetacion
    {
        public enum Estado { EnTierra, Despegue, Patrulla, Ataque, Pasada, Agarrado, Caida, Destruido }

        const float G = 9.81f;
        const float DensidadAire = 1.2f;

        [Header("Vuelo (BEMT de Lur Aerospace: 4 palas, R 7,3 m, 289 rpm, 8 t)")]
        public float masa = 8000f;
        [Tooltip("Empuje maximo del rotor (N): 123 kN con 15 grados de colectivo")]
        public float empujeMaximo = 123000f;
        public float radioRotor = 7.3f;
        public float rpm = 289f;
        public float inclinacionMaxima = 30f;
        [Tooltip("Lo que tarda el disco del rotor en seguir al mando (s)")]
        public float respuestaDisco = 0.45f;
        public float velocidadMaxima = 45f;
        [Tooltip("Guinada maxima (grados/s)")]
        public float giroMaximo = 50f;
        [Tooltip("Area de placa plana equivalente del fuselaje (m2)")]
        public float areaResistente = 2.8f;
        [Tooltip("Centro de masas (local): el aparato se inclina alrededor de el, no de las ruedas")]
        public Vector3 centroMasas = new Vector3(0f, 1.6f, -0.3f);
        [Tooltip("Por donde lo agarra Kong (local): el botalon de cola")]
        public Vector3 puntoAgarre = new Vector3(0f, 2.2f, -6.5f);

        [Header("Combate")]
        public float vision = 650f;
        [Tooltip("Altura de la orbita sobre el suelo o el dosel")]
        public float alturaCrucero = 42f;
        [Tooltip("Altura de las pasadas sobre el suelo (nunca por debajo del dosel)")]
        public float alturaPasada = 11f;
        public float radioOrbita = 120f;
        public float velocidadOrbita = 20f;
        public float velocidadPasada = 32f;
        [Tooltip("Fraccion de la velocidad de pasada al llegar a Kong")]
        public float frenoPasada = 0.45f;
        [Tooltip("Velocidad de bajada en la pasada (m/s); fuera de ella, 7")]
        public float bajadaPasada = 18f;
        public Vector2 esperaPasada = new Vector2(12f, 22f);
        public float alcanceCanon = 280f;
        public float danioBala = 6f;
        [Tooltip("Lo que penetra cada proyectil del canon (Calibres; el de 30 mm entra en todo)")]
        public float calibreCanon = Calibres.Canon;
        [Tooltip("Disparos por segundo (canon de 30 mm)")]
        public float cadencia = 10f;
        public float dispersion = 0.02f;
        public Vector2 duracionRafaga = new Vector2(1.2f, 2.4f);
        public Vector2 pausaRafagas = new Vector2(1.4f, 2.8f);
        public int cohetesPorSalva = 4;
        public float pausaCohetes = 12f;
        public GameObject prefabCohete;
        [Tooltip("Furia que gana Kong al derribarlo")]
        public float furia = 15f;

        [Header("Piezas (las pone ConstructorHelicoptero)")]
        public Transform rotorPrincipal;
        public Transform rotorCola;
        public Transform torreta;
        public Transform canon;
        public Transform bocacha;
        public Transform[] lanzacohetes = new Transform[0];
        public Transform[] toberas = new Transform[0];
        public Renderer[] discos = new Renderer[0];
        public Renderer[] cuerpo = new Renderer[0];
        public Renderer cristales;
        public Material materialQuemado;
        public AudioSource sonidoRotor;

        public Estado Actual { get; private set; }
        public Vector3 Velocidad => _vel;
        /// <summary>0 sano, 1 destrozado (humo de los motores).</summary>
        public float Danio => 1f - _salud.Fraccion;
        public float FraccionRpm => _fraccionRpm;
        /// <summary>Lo mas alto bajo el aparato y por delante (suelo, copas), la altura a la que va y el suelo (pruebas).</summary>
        public float Techo => _techo;
        public float AlturaObjetivo => _objetivo.y;
        public float SueloBajo => _terreno;
        /// <summary>Fuerza del chorro del rotor contra lo que tiene debajo (0..1).</summary>
        public float IntensidadChorro { get; private set; }
        /// <summary>Punto del suelo (o del agua) bajo el rotor.</summary>
        public Vector3 PuntoChorro { get; private set; }
        public bool ChorroSobreAgua { get; private set; }
        public bool ChorroSobreDosel { get; private set; }

        public static readonly List<Helicoptero> Todos = new List<Helicoptero>();
        /// <summary>
        /// Lo que el juego levanta por encima del relieve y no se puede atravesar (un muro, una presa: el aparato es cinematico
        /// y no choca con ningun colisionador): cada uno da lo mas alto que hay en un punto (y del mundo; float.MinValue si alli
        /// no hay nada). Cuenta como las copas: en su techo (lo de debajo y lo de delante: sube a tiempo) y en la altura de cada
        /// sitio al que va. Quien lo pone lo quita (sin ninguno, como siempre).
        /// </summary>
        public static readonly List<System.Func<Vector3, float>> Estorbos = new List<System.Func<Vector3, float>>();
        /// <summary>Disparo del canon (origen, impacto) para fogonazos, trazadoras y sonido.</summary>
        public static event System.Action<Vector3, Vector3> AlDisparar;
        public static event System.Action<Helicoptero> AlDespegar;
        /// <summary>El helicoptero explota (derribado, lanzado por Kong o destrozado en tierra).</summary>
        public static event System.Action<Helicoptero> AlExplotar;

        // IInteractorVegetacion: el chorro del rotor aparta la maleza y las copas.
        public bool Activo => IntensidadChorro > 0.05f && Actual != Estado.Destruido;
        public Vector3 PosicionEmpuje => new Vector3(_pos.x, _pos.y + 1.5f, _pos.z);
        public float RadioEmpuje => radioRotor * (0.8f + 0.7f * IntensidadChorro);

        Salud _salud;
        Rigidbody _cuerpo;
        Lanzable _lanzable;
        Criatura _criatura;
        Transform _kong => _criatura != null ? _criatura.transform : null;
        float AlturaBlancoCriatura => _criatura != null ? _criatura.AlturaBlanco : 9f;

        Vector3 _pos, _vel, _arriba = Vector3.up;
        float _empuje, _rumbo;
        float _fraccionRpm, _rpmObjetivo, _anguloRotor;
        Vector3 _objetivo, _velObjetivo, _mirarA;
        Vector3 _base, _despegue;
        float _sueloInicial;
        float _terreno, _techo; // bajo el aparato: suelo (o mar) y lo mas alto (dosel, ceibas)
        float _proximoSuelo, _proximoDosel, _doselObjetivo;
        float _anguloOrbita, _sentidoOrbita = 1f, _fase;
        float _proximaPasada;
        Vector3 _dirPasada;
        float _ladoPasada, _finPasada;
        float _aturdidoHasta;
        float _empujeAturdido = 0.8f;   // fraccion del empuje que da el rotor con el piloto aturdido
        Vector3 _bamboleo;
        float _relojRafaga, _relojBala, _proximaSalva, _alturaBlanco = 9f;
        bool _enRafaga, _salvaEnCurso, _ordenDespegue, _explotado, _lanzado;
        // La rafaga apunta a un punto del cuerpo (Criatura.PuntoDisparo), guardado respecto a la criatura.
        bool _conPunto;
        Vector3 _desfase;
        float _yawTorreta, _pitchCanon;
        float _relojCaida;
        static readonly List<int> s_Plantas = new List<int>();

        void Awake()
        {
            _salud = GetComponent<Salud>();
            _cuerpo = GetComponent<Rigidbody>();
            _lanzable = GetComponent<Lanzable>();
            _salud.AlMorir += Derribado;
            _lanzable.AlChocar += (l, c) => Explotar();
            _cuerpo.isKinematic = true;
            _cuerpo.interpolation = RigidbodyInterpolation.Interpolate;
            _cuerpo.mass = masa;
            _cuerpo.centerOfMass = centroMasas;
            _lanzable.volador = true;
            _lanzable.puntoAgarre = puntoAgarre;
            _fase = Random.value * 100f;
            _sentidoOrbita = Random.value < 0.5f ? -1f : 1f;
            Colocar(transform.position, transform.rotation);
        }

        void OnEnable()
        {
            Todos.Add(this);
            Viento.Registrar(this);
            Criatura.AlIntimidar += OirRugido;
            Criatura.AlOndaCono += OirPalmada;
        }

        void OnDisable()
        {
            Todos.Remove(this);
            Viento.Quitar(this);
            Criatura.AlIntimidar -= OirRugido;
            Criatura.AlOndaCono -= OirPalmada;
        }

        void Start()
        {
            _criatura = Criatura.Buscar();
            _proximaPasada = Time.time + Random.Range(esperaPasada.x, esperaPasada.y);
            _proximaSalva = Time.time + Random.Range(3f, pausaCohetes);
        }

        // ------------------------------------------------------------------ Puesta en escena
        /// <summary>Deja el aparato en un punto: posado (rotor parado) o ya en vuelo.</summary>
        public void Colocar(Vector3 posicion, Quaternion rotacion, bool enVuelo = false)
        {
            transform.SetPositionAndRotation(posicion, rotacion);
            _pos = posicion + rotacion * centroMasas;
            _vel = Vector3.zero;
            _arriba = Vector3.up;
            _rumbo = rotacion.eulerAngles.y;
            _base = _despegue = posicion;
            _sueloInicial = posicion.y;
            _objetivo = _pos;
            _mirarA = _pos + rotacion * Vector3.forward * 50f;
            if (enVuelo)
            {
                _fraccionRpm = _rpmObjetivo = 1f;
                _empuje = masa * G;
                Actual = Estado.Patrulla;
            }
            else
            {
                _fraccionRpm = _rpmObjetivo = 0f;
                _empuje = 0f;
                Actual = Estado.EnTierra;
            }
        }

        /// <summary>Despega (lo ordena su zona al activarse, o al ver a Kong cerca).</summary>
        public void Despegar() => _ordenDespegue = true;

        /// <summary>Se ha rendido: posado y con el rotor parado, ya no despega ni dispara.</summary>
        public bool Rendido { get; private set; }

        /// <summary>Se retira: deja de ver a Kong, no dispara y se va mar adentro (y desaparece lejos).</summary>
        public bool Retirado { get; private set; }

        /// <summary>Vuelve al mar sin atacar (Ferrox se rinde al final de la campana).</summary>
        public void Retirarse()
        {
            if (!Vivo || Rendido || Retirado) return;
            Retirado = true;
            _ordenDespegue = false;
            Vector3 fuera = new Vector3(_pos.x, 0f, _pos.z);
            fuera = fuera.sqrMagnitude > 1f ? fuera.normalized : Vector3.forward;
            _base = fuera * Mapa.Medidas.tamano * 0.635f;
            if (Actual == Estado.Ataque || Actual == Estado.Pasada) Cambiar(Estado.Patrulla);
        }

        /// <summary>La criatura lo deja en el suelo sin romperlo (el final de la campana de Kong).</summary>
        public void Rendirse(Vector3 donde, Quaternion rumbo)
        {
            if (_explotado) return;
            _lanzable.Soltar();
            Rendido = true;
            _ordenDespegue = false;
            Colocar(donde, rumbo);
        }

        /// <summary>Centro de la patrulla cuando no ve a Kong (su zona).</summary>
        public void Patrullar(Vector3 centro) => _base = centro;

        /// <summary>Empieza ya una pasada baja contra Kong (pruebas y momentos de guion).</summary>
        public void ForzarPasada()
        {
            if (_kong == null || (Actual != Estado.Ataque && Actual != Estado.Patrulla)) return;
            EmpezarPasada();
        }

        bool Vivo => Actual != Estado.Caida && Actual != Estado.Destruido;
        bool EnVueloControlado => Actual == Estado.Despegue || Actual == Estado.Patrulla || Actual == Estado.Ataque || Actual == Estado.Pasada;

        // ------------------------------------------------------------------ Bucle
        void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            if (_lanzable.Agarrado && Actual != Estado.Agarrado && !_explotado)
            {
                Cambiar(Estado.Agarrado);
                _vel = Vector3.zero;
            }
            if (Actual == Estado.Agarrado && _lanzable.EnVuelo)
            {
                Cambiar(Estado.Caida);
                _relojCaida = 0f;
                _lanzado = true;
            }
            // Retirado y ya en alta mar: se va del todo.
            if (Retirado && new Vector2(_pos.x, _pos.z).magnitude > Mapa.Medidas.tamano * 0.586f) { Destroy(gameObject); return; }
            Medir();
            Cerebro(dt);
            Armas(dt);
            Rotores(dt);
            Sonido();
            _lanzable.Vuela = EnVueloControlado && (Actual != Estado.Despegue || _pos.y - _sueloInicial > 4f);
            _lanzable.Atrapable = _lanzable.Vuela && _pos.y - _terreno < 45f;
            _lanzable.VelocidadVuelo = _vel;
        }

        void FixedUpdate()
        {
            float dt = Time.fixedDeltaTime;
            if (Actual == Estado.Caida && !_cuerpo.isKinematic) { Caer(dt); return; }
            if (!EnVueloControlado || _lanzable.Agarrado || _lanzable.EnVuelo) return;
            Volar(dt);
        }

        void Cambiar(Estado e)
        {
            if (Actual == e) return;
            Actual = e;
            _enRafaga = false;
            if (e == Estado.Ataque && _kong != null)
            {
                Vector3 d = _pos - _kong.position;
                _anguloOrbita = Mathf.Atan2(d.z, d.x);
            }
        }

        // ------------------------------------------------------------------ Lo que hay debajo
        static float Terreno(Vector3 p) => Mapa.Superficie(p.x, p.z);

        /// <summary>Lo mas alto de la vegetacion alrededor de un punto (copas, ceibas).</summary>
        static float Dosel(Vector3 p, float radio)
        {
            var veg = RenderizadorVegetacion.Instancia;
            if (veg == null) return float.MinValue;
            veg.Buscar(new float3(p.x, p.y, p.z), radio, s_Plantas);
            float techo = float.MinValue;
            foreach (int i in s_Plantas)
            {
                var esp = veg.EspecieDe(i);
                if (esp.tipo != TipoVegetal.Gigante && esp.tipo != TipoVegetal.Arbol && esp.tipo != TipoVegetal.Palmera && esp.tipo != TipoVegetal.Bambu) continue;
                techo = Mathf.Max(techo, veg.MatrizDe(i).c3.y + esp.Limites.max.y * veg.EscalaDe(i));
            }
            return techo;
        }

        /// <summary>Lo mas alto de los Estorbos en un punto (float.MinValue sin ninguno).</summary>
        static float Estorbo(Vector3 p)
        {
            float y = float.MinValue;
            for (int i = 0; i < Estorbos.Count; i++)
                if (Estorbos[i] != null) y = Mathf.Max(y, Estorbos[i](p));
            return y;
        }

        /// <summary>Lo mas alto bajo el aparato y un poco por delante (suelo, mar, copas, los Estorbos).</summary>
        void Medir()
        {
            if (Time.time < _proximoSuelo) return;
            _proximoSuelo = Time.time + 0.25f;
            Vector3 delante = _pos + Vector3.ClampMagnitude(new Vector3(_vel.x, 0f, _vel.z) * 2.5f, 80f);
            _terreno = Terreno(_pos);
            float dosel = Mathf.Max(Dosel(_pos, radioRotor + 4f), Dosel(delante, radioRotor + 4f));
            _techo = Mathf.Max(Mathf.Max(_terreno, Terreno(delante)), Mathf.Max(dosel, Mathf.Max(Estorbo(_pos), Estorbo(delante))));
            // Chorro del rotor: fuerte a menos de un diametro de lo que tiene debajo.
            float bajo = Mathf.Max(_terreno, Dosel(_pos, radioRotor));
            float h = _pos.y - bajo;
            IntensidadChorro = Vivo && Actual != Estado.EnTierra ? Mathf.Clamp01(1f - h / (3.2f * radioRotor)) * _fraccionRpm * _fraccionRpm : 0f;
            ChorroSobreDosel = bajo > _terreno + 4f;
            ChorroSobreAgua = !ChorroSobreDosel && !float.IsNaN(Mapa.Agua(_pos.x, _pos.z));
            PuntoChorro = new Vector3(_pos.x, bajo, _pos.z);
        }

        // ------------------------------------------------------------------ IA
        float _visibilidad = 1f, _relojVisibilidad;

        bool CriaturaA(float distancia)
        {
            if (_criatura == null) _criatura = Criatura.Jugador;
            if (Retirado || _criatura == null || _criatura.Salud.Muerto || _criatura.Escondida ||
                (_kong.position - _pos).sqrMagnitude >= distancia * distancia) return false;
            // Lo que hay en medio (el relieve, la selva cerrada): la misma vista que los humanos, si el juego la da.
            // Solo a una criatura pequena (como los humanos: una grande se ve desde arriba aunque este en la selva).
            if (_criatura.Tamano >= 8f) return true;
            if (Humano.Visibilidad != null && Time.time >= _relojVisibilidad)
            {
                _relojVisibilidad = Time.time + 0.3f;
                _visibilidad = Mathf.Clamp01(Humano.Visibilidad(_pos, _criatura.PuntoBlanco));
            }
            return Humano.Visibilidad == null || _visibilidad > 0.25f;
        }

        void Cerebro(float dt)
        {
            Vector3 kong = _kong != null ? _kong.position : _base;
            switch (Actual)
            {
                case Estado.EnTierra:
                    if (!Rendido && (_ordenDespegue || CriaturaA(vision * 0.7f)))
                    {
                        Actual = Estado.Despegue;
                        _rpmObjetivo = 1f;
                        AlDespegar?.Invoke(this);
                    }
                    break;

                case Estado.Despegue:
                    // Con el rotor a vueltas sube en vertical y despues se va a por Kong.
                    _rpmObjetivo = 1f;
                    _objetivo = new Vector3(_despegue.x, _sueloInicial + centroMasas.y + (_fraccionRpm > 0.85f ? 35f : 0f), _despegue.z);
                    _velObjetivo = Vector3.zero;
                    _mirarA = _kong != null ? kong : _pos + transform.forward * 50f;
                    if (_pos.y - Mathf.Max(_terreno, _sueloInicial) > 22f) Cambiar(CriaturaA(vision) ? Estado.Ataque : Estado.Patrulla);
                    break;

                case Estado.Patrulla:
                {
                    _anguloOrbita += 14f / 170f * dt * _sentidoOrbita;
                    Vector3 p = _base + new Vector3(Mathf.Cos(_anguloOrbita), 0f, Mathf.Sin(_anguloOrbita)) * 170f;
                    _objetivo = Altura(p, alturaCrucero + 15f);
                    _velObjetivo = Tangente() * 14f;
                    _mirarA = _pos + (_velObjetivo.sqrMagnitude > 1f ? _velObjetivo : transform.forward) * 10f;
                    if (CriaturaA(vision)) Cambiar(Estado.Ataque);
                    break;
                }

                case Estado.Ataque:
                {
                    // Orbita a Kong con el morro hacia el; el radio y la altura respiran un poco.
                    float r = radioOrbita * (1f + 0.15f * Mathf.Sin(Time.time * 0.13f + _fase));
                    _anguloOrbita += velocidadOrbita / r * dt * _sentidoOrbita;
                    Vector3 p = kong + new Vector3(Mathf.Cos(_anguloOrbita), 0f, Mathf.Sin(_anguloOrbita)) * r;
                    _objetivo = Altura(p, alturaCrucero + 8f * Mathf.Sin(Time.time * 0.21f + _fase * 2f));
                    _velObjetivo = Tangente() * velocidadOrbita + (_criatura != null ? _criatura.Velocidad : Vector3.zero);
                    _mirarA = kong;
                    if (Time.time > _proximaPasada) EmpezarPasada();
                    if (!CriaturaA(vision * 1.4f)) Cambiar(Estado.Patrulla);
                    break;
                }

                case Estado.Pasada:
                {
                    // Linea recta que pasa junto a Kong, baja, ametrallando; despues vuelve a subir.
                    Vector3 lado = Vector3.Cross(Vector3.up, _dirPasada);
                    Vector3 linea = kong + lado * _ladoPasada;
                    float s = Vector3.Dot(_pos - linea, _dirPasada);
                    Vector3 p = linea + _dirPasada * (s + 45f);
                    _objetivo = Altura(p, alturaPasada, true);
                    // Frena al llegar a Kong: le ametralla bajo y despacio (y ahi Kong lo puede coger). A toda
                    // velocidad cruzaba el claro antes de bajar de las copas y pasaba a 30-45 m de su cabeza.
                    float cerca = Mathf.InverseLerp(90f, 25f, Mathf.Abs(s));
                    _velObjetivo = _dirPasada * velocidadPasada * Mathf.Lerp(1f, frenoPasada, cerca);
                    _mirarA = s < 15f ? kong + Vector3.up * AlturaBlancoCriatura * 0.9f : _pos + _dirPasada * 60f;
                    if (s > 120f || Time.time > _finPasada)
                    {
                        Cambiar(Estado.Ataque);
                        _proximaPasada = Time.time + Random.Range(esperaPasada.x, esperaPasada.y);
                    }
                    break;
                }

                case Estado.Agarrado:
                    Forcejear(dt);
                    break;

                case Estado.Destruido:
                    _rpmObjetivo = 0f;
                    break;
            }
        }

        Vector3 Tangente() => new Vector3(-Mathf.Sin(_anguloOrbita), 0f, Mathf.Cos(_anguloOrbita)) * _sentidoOrbita;

        /// <summary>El punto p a cierta altura sobre el suelo, y siempre por encima del dosel (y de los Estorbos).</summary>
        Vector3 Altura(Vector3 p, float sobreSuelo, bool rasante = false)
        {
            float suelo = Terreno(p);
            // La consulta a la vegetacion es cara: el dosel del objetivo se mira cuatro veces por segundo.
            if (Time.time >= _proximoDosel)
            {
                _proximoDosel = Time.time + 0.25f;
                _doselObjetivo = Dosel(p, radioRotor + 3f);
            }
            float encima = rasante ? 5f : 12f;
            p.y = Mathf.Max(Mathf.Max(suelo + sobreSuelo, _doselObjetivo + encima), Mathf.Max(suelo + 6f, Estorbo(p) + encima));
            return p;
        }

        void EmpezarPasada()
        {
            Vector3 haciaKong = _kong.position - _pos;
            haciaKong.y = 0f;
            if (haciaKong.sqrMagnitude < 1f) haciaKong = transform.forward;
            _dirPasada = Quaternion.Euler(0f, Random.Range(-12f, 12f), 0f) * haciaKong.normalized;
            // A 5-11 m de su costado: al alcance de la mano si Kong le sale al paso.
            _ladoPasada = Random.Range(5f, 11f) * (Random.value < 0.5f ? -1f : 1f);
            _finPasada = Time.time + 16f;
            Cambiar(Estado.Pasada);
        }

        /// <summary>
        /// El piloto pierde el control un rato: el disco se va de lado, el rotor da menos empuje (el
        /// aparato cae) y no dispara. Lo provocan el rugido y la palmada de Kong.
        /// </summary>
        public void Aturdir(float segundos, float empujeRestante, float bamboleo)
        {
            if (!EnVueloControlado) return;
            bool nuevo = Time.time >= _aturdidoHasta;
            _aturdidoHasta = Mathf.Max(_aturdidoHasta, Time.time + segundos);
            _empujeAturdido = nuevo ? empujeRestante : Mathf.Min(_empujeAturdido, empujeRestante);
            _bamboleo = Random.onUnitSphere * bamboleo;
            _bamboleo.y = 0f;
        }

        public bool Aturdido => Time.time < _aturdidoHasta;

        void OirRugido(Vector3 donde, float radio)
        {
            if (!EnVueloControlado) return;
            float d = Vector3.Distance(donde, _pos);
            if (d > radio * 1.6f) return;
            // Rugido atronador: de cerca, el piloto pierde el control y el aparato se viene abajo.
            float k = 1f - d / (radio * 1.6f);
            Aturdir(1.5f + 2.5f * k, Mathf.Lerp(0.85f, 0.45f, k), 0.35f + 0.4f * k);
        }

        void OirPalmada(Vector3 centro, Vector3 direccion, float alcance, float cono)
        {
            if (!EnVueloControlado) return;
            Vector3 d = _pos - centro;
            float dist = d.magnitude;
            if (dist > alcance * 1.2f || Vector3.Angle(direccion, d) > cono) return;
            // La onda de la palmada le da de lleno: cae y se queda unos segundos sin control.
            float k = 1f - dist / (alcance * 1.2f);
            Aturdir(2f + 2f * k, Mathf.Lerp(0.65f, 0.3f, k), 0.5f + 0.4f * k);
        }

        // ------------------------------------------------------------------ Vuelo
        void Volar(float dt)
        {
            // 1) Mando: velocidad deseada hacia el objetivo y aceleracion para alcanzarla.
            Vector3 e = _objetivo - _pos;
            Vector3 vDes = Vector3.ClampMagnitude(new Vector3(e.x, 0f, e.z) * 0.45f + new Vector3(_velObjetivo.x, 0f, _velObjetivo.z), velocidadMaxima);
            Vector3 aH = (vDes - new Vector3(_vel.x, 0f, _vel.z)) / 1.4f;
            aH = Vector3.ClampMagnitude(aH, G * Mathf.Tan(inclinacionMaxima * Mathf.Deg2Rad));
            // En la pasada pica: baja casi el doble de deprisa para meterse en el claro.
            float vyDes = Mathf.Clamp(e.y * 0.7f, Actual == Estado.Pasada ? -bajadaPasada : -7f, 9f);
            // Las copas (no tienen colision) se esquivan subiendo a tiempo, sin saltos.
            if (Actual != Estado.Despegue && _pos.y < _techo + 4f) vyDes = Mathf.Max(vyDes, Mathf.Min((_techo + 4f - _pos.y) * 1.2f, 12f));
            // (y con mas aceleracion vertical: si no, la bajada llega tarde al claro)
            float limiteV = Actual == Estado.Pasada ? 9f : 6f;
            float aV = Mathf.Clamp((vyDes - _vel.y) / 0.9f, -limiteV, limiteV);
            // 2) Empuje necesario: peso, aceleracion y la resistencia del aire.
            Vector3 resistencia = 0.5f * DensidadAire * areaResistente * _vel.magnitude * _vel;
            Vector3 f = masa * (aH + Vector3.up * (aV + G)) + resistencia;
            f.y = Mathf.Max(f.y, masa * G * 0.25f);
            Vector3 n = f.normalized;
            float angulo = Vector3.Angle(Vector3.up, n);
            if (angulo > inclinacionMaxima) n = Vector3.Slerp(Vector3.up, n, inclinacionMaxima / angulo);
            // El empuje que da el rotor depende de sus vueltas (cuadrado de la velocidad de punta).
            float empuje = Mathf.Clamp(Vector3.Dot(f, n), 0f, empujeMaximo * _fraccionRpm * _fraccionRpm);
            if (Time.time < _aturdidoHasta)
            {
                n = (n + _bamboleo * Mathf.Sin(Time.time * 5f + _fase)).normalized;
                empuje *= _empujeAturdido;
            }
            // Los motores tocados no dan toda la potencia.
            empuje = Mathf.Min(empuje, empujeMaximo * Mathf.Lerp(0.72f, 1f, _salud.Fraccion));
            // 3) El disco y el colectivo siguen al mando con retraso.
            _arriba = Vector3.Slerp(_arriba, n, 1f - Mathf.Exp(-dt / respuestaDisco)).normalized;
            _empuje = Mathf.Lerp(_empuje, empuje, 1f - Mathf.Exp(-dt / 0.25f));
            // 4) Dinamica.
            Vector3 acel = _arriba * (_empuje / masa) + Vector3.down * G - resistencia / masa;
            _vel += acel * dt;
            _pos += _vel * dt;
            // El suelo (o el helipuerto al despegar) es duro.
            float apoyo = Mathf.Max(_terreno, Actual == Estado.Despegue ? _sueloInicial : float.MinValue) + centroMasas.y;
            if (_pos.y < apoyo)
            {
                _pos.y = apoyo;
                if (_vel.y < 0f) _vel.y = 0f;
                if (Actual == Estado.Despegue) _vel = new Vector3(0f, _vel.y, 0f);
            }
            // 5) Rumbo hacia donde mira; el fuselaje cuelga del disco.
            Vector3 mirar = _mirarA - _pos;
            mirar.y = 0f;
            if (mirar.sqrMagnitude > 1f && (Actual != Estado.Despegue || _pos.y - _sueloInicial > 6f))
                _rumbo = Mathf.MoveTowardsAngle(_rumbo, Mathf.Atan2(mirar.x, mirar.z) * Mathf.Rad2Deg, giroMaximo * dt);
            Vector3 delante = Vector3.ProjectOnPlane(Quaternion.Euler(0f, _rumbo, 0f) * Vector3.forward, _arriba).normalized;
            var rot = Quaternion.LookRotation(delante, _arriba);
            _cuerpo.MoveRotation(rot);
            _cuerpo.MovePosition(_pos - rot * centroMasas);
        }

        // ------------------------------------------------------------------ Armas
        void Armas(float dt)
        {
            // Aturdido no dispara: bastante tiene con no estrellarse.
            bool combate = (Actual == Estado.Ataque || Actual == Estado.Pasada) && CriaturaA(alcanceCanon * 1.5f) && _fraccionRpm > 0.9f
                           && Time.time >= _aturdidoHasta;
            Vector3 blanco = _kong == null ? _pos + transform.forward * 100f
                           : _conPunto ? _kong.position + _kong.rotation * _desfase
                           : _kong.position + Vector3.up * _alturaBlanco;
            bool enLinea = ApuntarTorreta(combate ? blanco : _pos + transform.forward * 100f - transform.up * 20f, dt);
            if (!combate || bocacha == null) { _enRafaga = false; return; }

            float distancia = Vector3.Distance(bocacha.position, blanco);
            _relojRafaga -= dt;
            if (_relojRafaga <= 0f)
            {
                _enRafaga = !_enRafaga && distancia < alcanceCanon;
                _relojRafaga = _enRafaga ? Random.Range(duracionRafaga.x, duracionRafaga.y) : Random.Range(pausaRafagas.x, pausaRafagas.y);
                _alturaBlanco = Random.Range(0.55f, 1.45f) * AlturaBlancoCriatura;
                // Si la criatura dice a donde (el cuerpo que se ve desde el canon), la rafaga va ahi.
                Vector3 pb = default;
                _conPunto = _criatura != null && _criatura.PuntoDisparo(bocacha.position, out pb);
                if (_conPunto) _desfase = Quaternion.Inverse(_kong.rotation) * (pb - _kong.position);
                // En la pasada no para de disparar.
                if (Actual == Estado.Pasada && !_enRafaga) _relojRafaga *= 0.3f;
            }
            if (_enRafaga && enLinea && distancia < alcanceCanon)
            {
                _relojBala -= dt;
                while (_relojBala <= 0f)
                {
                    _relojBala += 1f / cadencia;
                    Disparar();
                }
            }

            // Salvas de cohetes cuando el morro apunta a Kong.
            Vector3 aKong = blanco - _pos;
            aKong.y = 0f;
            Vector3 morro = transform.forward;
            morro.y = 0f;
            if (!_salvaEnCurso && Time.time > _proximaSalva && prefabCohete != null && lanzacohetes.Length > 0 &&
                distancia > 60f && distancia < 330f && Vector3.Angle(morro, aKong) < 15f)
                StartCoroutine(Salva());
        }

        /// <summary>Gira la torreta (guinada) y el canon (cabeceo) hacia el blanco; true si ya apunta.</summary>
        bool ApuntarTorreta(Vector3 blanco, float dt)
        {
            if (torreta == null || canon == null) return true;
            Vector3 d = transform.InverseTransformDirection(blanco - torreta.position);
            float yaw = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
            float pitch = -Mathf.Atan2(d.y, new Vector2(d.x, d.z).magnitude) * Mathf.Rad2Deg;
            float yawLim = Mathf.Clamp(yaw, -110f, 110f), pitchLim = Mathf.Clamp(pitch, -12f, 60f);
            _yawTorreta = Mathf.MoveTowardsAngle(_yawTorreta, yawLim, 140f * dt);
            _pitchCanon = Mathf.MoveTowards(_pitchCanon, pitchLim, 110f * dt);
            torreta.localRotation = Quaternion.Euler(0f, _yawTorreta, 0f);
            canon.localRotation = Quaternion.Euler(_pitchCanon, 0f, 0f);
            return Mathf.Abs(yaw - yawLim) < 0.1f && Mathf.Abs(pitch - pitchLim) < 0.1f &&
                   Mathf.Abs(Mathf.DeltaAngle(_yawTorreta, yaw)) < 4f && Mathf.Abs(_pitchCanon - pitch) < 4f;
        }

        void Disparar()
        {
            Vector3 origen = bocacha.position;
            Vector3 dir = (bocacha.forward + Random.insideUnitSphere * dispersion).normalized;
            // Por Balas: no se para en el propio helicoptero y ve los blancos sin colisionadores.
            Vector3 impacto = Balas.Disparar(origen, dir, alcanceCanon * 1.3f, gameObject,
                new DatosDanio { cantidad = danioBala, direccion = dir, empuje = 0.5f, tipo = TipoDanio.Canon, origen = gameObject, calibre = calibreCanon });
            AlDisparar?.Invoke(origen, impacto);
        }

        IEnumerator Salva()
        {
            _salvaEnCurso = true;
            _proximaSalva = Time.time + pausaCohetes * Random.Range(0.8f, 1.3f);
            for (int i = 0; i < cohetesPorSalva; i++)
            {
                if (!(Actual == Estado.Ataque || Actual == Estado.Pasada) || _kong == null) break;
                var lanzador = lanzacohetes[i % lanzacohetes.Length];
                // Salen por los tubos (hacia el morro) y corrigen un poco hacia Kong (o hacia el cuerpo que diga la criatura).
                Vector3 blanco = _kong.position + Vector3.up * Random.Range(0.45f, 1.35f) * AlturaBlancoCriatura;
                if (_criatura != null && _criatura.PuntoDisparo(lanzador.position, out var alCuerpo)) blanco = alCuerpo;
                float t = Vector3.Distance(lanzador.position, blanco) / 75f;
                if (_criatura != null) blanco += _criatura.Velocidad * t;
                Vector3 dir = Vector3.RotateTowards(transform.forward, (blanco - lanzador.position).normalized, 18f * Mathf.Deg2Rad, 0f);
                dir = Quaternion.Euler(Random.Range(-2.5f, 2.5f), Random.Range(-2.5f, 2.5f), 0f) * dir; // no guiados: dispersion
                var go = Instantiate(prefabCohete, lanzador.position + dir * 1.2f, Quaternion.LookRotation(dir));
                var c = go.GetComponent<Cohete>();
                if (c != null) c.Lanzar(gameObject, _kong, blanco - _kong.position, _vel + dir * 25f);
                yield return new WaitForSeconds(0.22f);
            }
            _salvaEnCurso = false;
        }

        // ------------------------------------------------------------------ Rotores y sonido
        void Rotores(float dt)
        {
            // El rotor tarda en coger vueltas (unos 5 s desde parado) y en pararse.
            float ritmo = _rpmObjetivo > _fraccionRpm ? (_fraccionRpm < 0.3f ? 1f / 8f : 1f / 4f) : 1f / 9f;
            _fraccionRpm = Mathf.MoveTowards(_fraccionRpm, _rpmObjetivo, ritmo * dt);
            _anguloRotor = (_anguloRotor + rpm / 60f * 360f * _fraccionRpm * dt) % 360f;
            // Antihorario visto desde arriba; el de cola gira 4,7 veces mas rapido.
            if (rotorPrincipal != null) rotorPrincipal.localRotation = Quaternion.Euler(0f, -_anguloRotor, 0f);
            if (rotorCola != null) rotorCola.localRotation = Quaternion.Euler(_anguloRotor * 4.7f % 360f, 0f, 0f);
            bool rapido = _fraccionRpm > 0.55f;
            foreach (var d in discos)
                if (d != null) d.enabled = rapido;
        }

        void Sonido()
        {
            if (sonidoRotor == null) return;
            float f = _fraccionRpm;
            if (f < 0.02f || _explotado)
            {
                if (sonidoRotor.isPlaying) sonidoRotor.Stop();
                return;
            }
            if (!sonidoRotor.isPlaying)
            {
                sonidoRotor.time = Random.Range(0f, sonidoRotor.clip != null ? sonidoRotor.clip.length : 0f);
                sonidoRotor.Play();
            }
            sonidoRotor.pitch = Mathf.Lerp(0.45f, 1f, f) * (Time.time < _aturdidoHasta ? 0.97f : 1f);
            sonidoRotor.volume = 0.75f * f * f;
        }

        // ------------------------------------------------------------------ Agarrado por Kong
        void Forcejear(float dt)
        {
            var mano = transform.parent;
            if (mano == null || _kong == null) return;
            // El botalon en la palma y el morro hacia fuera; el piloto da tirones intentando soltarse.
            Vector3 fuera = Vector3.ProjectOnPlane(_kong.forward + _kong.right * 0.7f, Vector3.up).normalized;
            var tiron = Quaternion.Euler(Mathf.Sin(Time.time * 7.3f) * 6f, Mathf.Sin(Time.time * 5.1f + 1f) * 9f, Mathf.Sin(Time.time * 6.2f + 2f) * 8f);
            var rot = Quaternion.LookRotation(fuera, Vector3.up) * Quaternion.Euler(-12f, 0f, 0f) * tiron;
            transform.rotation = Quaternion.Slerp(transform.rotation, rot, 1f - Mathf.Exp(-dt * 6f));
            Vector3 pos = mano.position - transform.rotation * puntoAgarre;
            transform.position = Vector3.Lerp(transform.position, pos, 1f - Mathf.Exp(-dt * 10f));
            _pos = transform.position + transform.rotation * centroMasas;
        }

        // ------------------------------------------------------------------ Derribo
        void Derribado(DatosDanio d)
        {
            if (_explotado) return;
            _lanzable.Atrapable = _lanzable.Vuela = false;
            // En tierra (o en la mano de Kong) revienta ahi mismo; en el aire cae girando.
            if (Actual == Estado.EnTierra || (Actual == Estado.Despegue && _pos.y - _sueloInicial < 4f) || _lanzable.Agarrado)
            {
                Explotar();
                return;
            }
            Cambiar(Estado.Caida);
            _relojCaida = 0f;
            _cuerpo.isKinematic = false;
            _cuerpo.useGravity = true;
            _cuerpo.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            _cuerpo.linearVelocity = _vel;
            _cuerpo.angularVelocity = transform.up * 1.2f;
            _cuerpo.linearDamping = 0.05f;
            _cuerpo.angularDamping = 0.3f;
        }

        void Caer(float dt)
        {
            // Sin rotor de cola: el par del rotor principal lo hace girar cada vez mas rapido, el
            // disco aun sostiene algo (autorrotacion torpe) y el morro se va hacia abajo.
            // (Lanzado por Kong vuela como una piedra: Lanzable lo hace explotar al chocar.)
            _relojCaida += dt;
            if (!_lanzado)
            {
                float sustento = Mathf.Lerp(0.55f, 0.15f, _relojCaida / 4f);
                _cuerpo.AddForce(transform.up * masa * G * sustento * _fraccionRpm);
                if (_cuerpo.angularVelocity.magnitude < 5f) _cuerpo.AddTorque(transform.up * masa * 9f, ForceMode.Force);
                _cuerpo.AddTorque(transform.right * masa * 1.5f, ForceMode.Force);
                _rpmObjetivo = 0.6f;
            }
            float agua = Mapa.Agua(transform.position.x, transform.position.z);
            if ((!float.IsNaN(agua) && transform.position.y < agua - 1f) || _relojCaida > 20f) Explotar();
        }

        void OnCollisionEnter(Collision c)
        {
            if (_explotado || Actual != Estado.Caida) return;
            if (c.collider.transform.IsChildOf(transform)) return;
            if (_kong != null && c.collider.transform.IsChildOf(_kong) && _lanzable.EnVuelo) return;
            Explotar();
        }

        /// <summary>Revienta: bola de fuego, dano alrededor, restos quemados ardiendo y palas sueltas.</summary>
        public void Explotar()
        {
            if (_explotado) return;
            _explotado = true;
            Vector3 centro = transform.position + transform.rotation * centroMasas;
            Actual = Estado.Destruido;
            _rpmObjetivo = 0f;
            _fraccionRpm = Mathf.Min(_fraccionRpm, 0.3f);
            _lanzable.Atrapable = _lanzable.Vuela = false;
            _lanzable.explota = false;
            _lanzable.danio = 180f;
            AlExplotar?.Invoke(this);
            Impacto.Golpear(centro, 16f, 160f, 26f, TipoDanio.Explosion, gameObject);
            if (_criatura != null) _criatura.Premiar(new Premio(MotivoPremio.Helicoptero, furia, centro));

            // Restos: chapa quemada, sin cristales, las palas salen volando.
            if (materialQuemado != null)
                foreach (var r in cuerpo)
                    if (r != null)
                    {
                        var mats = r.sharedMaterials;
                        for (int i = 0; i < mats.Length; i++) mats[i] = materialQuemado;
                        r.sharedMaterials = mats;
                    }
            if (cristales != null) cristales.enabled = false;
            foreach (var d in discos)
                if (d != null) d.gameObject.SetActive(false);
            SoltarPalas(centro);
            if (sonidoRotor != null) sonidoRotor.Stop();

            if (_cuerpo.isKinematic && !_lanzable.Agarrado)
            {
                _cuerpo.isKinematic = false;
                _cuerpo.useGravity = true;
                _cuerpo.linearVelocity = _vel * 0.3f + Vector3.up * 4f;
            }
            _cuerpo.linearDamping = 0.4f;
            _cuerpo.angularDamping = 1.5f;
            Destroy(gameObject, 90f);
        }

        void SoltarPalas(Vector3 centro)
        {
            if (rotorPrincipal == null) return;
            float omega = rpm / 60f * Mathf.PI * 2f * 0.4f;
            var palas = new List<Transform>();
            foreach (Transform t in rotorPrincipal)
                if (t.name.StartsWith("Pala")) palas.Add(t);
            foreach (var p in palas)
            {
                var mf = p.GetComponent<MeshFilter>();
                p.SetParent(null, true);
                var rb = p.gameObject.AddComponent<Rigidbody>();
                rb.mass = 120f;
                var col = p.gameObject.AddComponent<BoxCollider>();
                if (mf != null && mf.sharedMesh != null)
                {
                    col.center = mf.sharedMesh.bounds.center;
                    col.size = Vector3.Max(mf.sharedMesh.bounds.size, Vector3.one * 0.1f);
                }
                Vector3 radial = Vector3.ProjectOnPlane(p.rotation * Vector3.forward, Vector3.up).normalized;
                rb.linearVelocity = Vector3.Cross(Vector3.up, radial) * -omega * radioRotor * 0.5f + Vector3.up * Random.Range(4f, 9f) + radial * 6f;
                rb.angularVelocity = Random.insideUnitSphere * 8f + Vector3.up * omega;
                Destroy(p.gameObject, 30f);
            }
        }
    }
}
