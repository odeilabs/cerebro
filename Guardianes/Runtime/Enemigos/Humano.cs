using Guardianes.Combate;
using Guardianes.Criaturas;
using Guardianes.Mundo;
using UnityEngine;

namespace Guardianes.Enemigos
{
    /// <summary>
    /// Un humano de la compania (mercenario, maderero, garimpeiro) o un civil. Su reaccion depende del
    /// tamano de la criatura del jugador:
    ///   Gigante (Kong, el basilisco): patrulla, la ve, apunta y dispara rafagas (rayo con dispersion
    ///   y trazadora); huye si se le echa encima o si la criatura intimida; los civiles huyen.
    ///   Pequena (una serpiente de un metro): no asusta. Hay que verla (a pocos metros, y nada si esta
    ///   escondida); los armados y los civiles valientes van a por ella a machetazos y pisotones, los
    ///   demas se apartan.
    ///   Muere aplastado, de un golpe o volando por los aires (sale despedido con fisica).
    /// Recuerda donde la vio por ultima vez y, al perderla, va alli (Investigar) y busca alrededor (Buscar);
    /// lo mismo con lo que oye (Ruidos: la criatura al moverse o romper, los disparos) y con lo que le
    /// mandan (Investigar). Si lleva radio y la ve, avisa (unos segundos: si lo callan antes, no llega;
    /// AlAvisar). Al andar rodea lo que le corta el paso (agua honda, cuestas, y lo que este en
    /// CapasObstaculo). Lejos de la criatura piensa menos a menudo.
    /// El Animator lo monta el editor (ConstructorHumanos) con estos parametros:
    ///   velocidad (float), apuntar (bool), disparar (bool), miedo (bool), golpe (trigger), muerto (trigger)
    ///   y, si lo tiene, atacar (trigger: el machetazo).
    /// </summary>
    [RequireComponent(typeof(Salud))]
    public class Humano : MonoBehaviour
    {
        public enum Estado { Patrulla, Alerta, Disparo, Huida, Muerto, Acoso, Investigar, Buscar }

        [Header("Comportamiento")]
        public float vision = 160f;
        public float distanciaDisparo = 120f;
        [Tooltip("Distancia a la que huye de una criatura del tamano de Kong (escala con el tamano)")]
        public float distanciaPanico = 28f;
        public float velocidadAndar = 1.6f;
        public float velocidadCorrer = 5.5f;
        public float radioPatrulla = 40f;
        [Tooltip("Se queda en su puesto mirando al frente (un centinela, alguien sentado a la mesa)")]
        public bool quieto;
        [Tooltip("Ronda: va y vuelve por estos puntos en vez de pasear al azar")]
        public Vector3[] ruta;
        [Tooltip("Segundos que se para en los extremos de la ronda (trabajando)")]
        public float pausaRuta;

        [Header("Arma")]
        public float danioBala = 3.5f;
        public float cadencia = 9f;          // balas por segundo en rafaga
        public float dispersion = 0.035f;    // radianes
        public Vector2 duracionRafaga = new Vector2(0.8f, 1.8f);
        public Vector2 pausaRafagas = new Vector2(0.9f, 2.2f);
        public Transform bocacha;

        [Header("Civil")]
        [Tooltip("Trabajador o aldeano desarmado: no dispara, sigue con lo suyo y huye de las criaturas grandes")]
        public bool civil;
        [Tooltip("Si muere siendo civil, cuenta como inocente (un villano desarmado, no)")]
        public bool inocente = true;
        [Tooltip("No se le puede matar (el juego no lo permite): los aldeanos protegidos")]
        public bool protegido;

        [Header("Criaturas pequenas")]
        [Tooltip("Por debajo de este tamano (m) la criatura no da miedo: van a por ella")]
        public float tamanoAmenaza = 2.5f;
        [Tooltip("A cuantas veces el tamano de la criatura la ven (con luz y sin esconderse)")]
        public float visionPorTamano = 12f;
        [Tooltip("Vision termica (una camara de calor): la oscuridad le quita poca vista, pero es un visor: solo ve de frente")]
        public bool termica;
        [Range(0f, 1f)] public float valor = 0.5f;
        public float alcanceGolpe = 1.3f;
        public float danioGolpe = 30f;
        public float pausaGolpes = 1.1f;

        [Header("Oido, memoria y radio")]
        [Tooltip("Multiplica lo lejos que oye (1: lo que dice el ruido)")]
        public float oido = 1f;
        [Tooltip("Segundos que busca alrededor de la pista antes de volver a lo suyo")]
        public float segundosBusqueda = 14f;
        [Tooltip("Lleva radio: si la ve, avisa a los demas (los civiles no)")]
        public bool radio = true;
        [Tooltip("Lo que tarda en dar el aviso (s): si lo callan antes, no llega")]
        public float segundosLlamada = 3.5f;

        [Header("Miedo (solo si el juego activa Humano.ConMiedo)")]
        [Tooltip("0..100: sube con siseos y gritos, mordiscos, cuerpos, la oscuridad, lo que se ve de reojo y las marcas de leyenda; se contagia")]
        [Range(0f, 100f)] public float miedo;
        [Tooltip("Lleva luz propia (linterna): de noche no se asusta tanto por la oscuridad")]
        public FuenteLuz linterna;

        /// <summary>El juego activa el miedo (Basiliskos). Sin el, los humanos se portan como siempre (King Kong).</summary>
        public static bool ConMiedo;
        public const float Nervioso = 35f, Aterrado = 70f, Roto = 95f;
        /// <summary>Huye para no volver (tira el arma y se va): cuenta como ahuyentado.</summary>
        public static event System.Action<Humano> AlAhuyentar;
        /// <summary>Pasa de Aterrado por primera vez.</summary>
        public static event System.Action<Humano> AlAterrarse;
        /// <summary>La criatura le hace dano (mordisco, golpe): los de alrededor se asustan.</summary>
        public static event System.Action<Humano> AlSerAtacado;
        public bool Ahuyentado { get; private set; }
        public bool HaEstadoAterrado { get; private set; }

        public Estado Actual { get; private set; }
        public static event System.Action<Humano> AlMorir;
        /// <summary>Empieza a huir de la criatura (la primera vez): cuenta para ahuyentar sin matar.</summary>
        public static event System.Action<Humano> AlAsustarse;
        /// <summary>Empieza a huir (cada vez).</summary>
        public static event System.Action<Humano> AlHuir;
        /// <summary>Ve a la criatura por primera vez (sigilo).</summary>
        public static event System.Action<Humano> AlVer;
        public static readonly System.Collections.Generic.List<Humano> Todos = new System.Collections.Generic.List<Humano>();
        /// <summary>Luz en un punto (0 noche cerrada .. 1 dia). La pone el juego; sin ella, siempre de dia.</summary>
        public static System.Func<Vector3, float> Luz;
        /// <summary>
        /// Lo que deja ver lo que hay en medio entre los ojos de un humano y la criatura (el relieve, las
        /// paredes, la selva cerrada): 1 nada, 0 no se ve. Multiplica lo lejos que la ve. La pone el juego;
        /// sin ella, se ve a traves de todo. Se pregunta unas cinco veces por segundo por humano y solo si
        /// estaria a tiro.
        /// </summary>
        public static System.Func<Vector3, Vector3, float> Visibilidad;
        /// <summary>Ya ha huido alguna vez de la criatura.</summary>
        public bool Asustado { get; private set; }
        public bool Muerto => Actual == Estado.Muerto;
        /// <summary>0..1: lo que lleva visto de la criatura (1 = la ha visto y da la voz).</summary>
        public float Deteccion { get; private set; }
        /// <summary>La pista que sigue (Investigar, Buscar): donde la vio u oyo por ultima vez, o a donde lo mandaron.</summary>
        public Vector3? Pista => _pista;
        /// <summary>0..1: lo que lleva del aviso por radio (1 = dado).</summary>
        public float Llamada { get; private set; }
        /// <summary>Ha dado el aviso por radio (vale hasta que vuelve a lo suyo).</summary>
        public bool Avisado { get; private set; }
        /// <summary>Da el aviso por radio: quien y donde la vio. Cada juego decide que pasa (refuerzos, alarma).</summary>
        public static event System.Action<Humano, Vector3> AlAvisar;
        /// <summary>Capas de lo que corta el paso al andar (rocas, paredes): las rodea. La pone el juego; 0, nada.</summary>
        public static int CapasObstaculo;
        /// <summary>Disparo (origen, impacto) para trazadoras, fogonazos y sonido.</summary>
        public static event System.Action<Vector3, Vector3, Humano> AlDisparar;
        /// <summary>Machetazo o pisoton contra una criatura pequena (donde).</summary>
        public static event System.Action<Vector3, Humano> AlGolpear;

        Animator _anim;
        bool _animAtacar;
        float _visibilidad = 1f, _relojVisibilidad;
        Salud _salud;
        Criatura _criatura;
        Vector3 _puesto;
        Vector3 _destino;
        float _relojEstado, _relojRafaga, _relojBala, _miedoHasta, _relojGolpe;
        bool _enRafaga, _vista;
        Rigidbody _cuerpo;
        static readonly int PVelocidad = Animator.StringToHash("velocidad");
        static readonly int PApuntar = Animator.StringToHash("apuntar");
        static readonly int PDisparar = Animator.StringToHash("disparar");
        static readonly int PMiedo = Animator.StringToHash("miedo");
        static readonly int PGolpe = Animator.StringToHash("golpe");
        static readonly int PMuerto = Animator.StringToHash("muerto");
        static readonly int PAtacar = Animator.StringToHash("atacar");

        void Awake()
        {
            _anim = GetComponentInChildren<Animator>();
            if (_anim != null && _anim.runtimeAnimatorController != null)
                foreach (var p in _anim.parameters) if (p.nameHash == PAtacar) _animAtacar = true;
            _salud = GetComponent<Salud>();
            _salud.AlDanio += RecibirDanio;
            _salud.AlMorir += Morir;
            _puesto = transform.position;
            _destino = _puesto;
            _desfase = s_humanos++ & 3;
        }

        void OnEnable()
        {
            Criatura.AlIntimidar += OirIntimidacion;
            Ruidos.AlOir += Oir;
            AlMorir += OtroMuere;
            AlSerAtacado += OtroAtacado;
            FuenteLuz.AlApagarse += LuzApagada;
            Todos.Add(this);
        }

        void OnDisable()
        {
            Criatura.AlIntimidar -= OirIntimidacion;
            Ruidos.AlOir -= Oir;
            AlMorir -= OtroMuere;
            AlSerAtacado -= OtroAtacado;
            FuenteLuz.AlApagarse -= LuzApagada;
            Todos.Remove(this);
        }

        /// <summary>Lo convierte en civil: guarda el arma y no vuelve a disparar.</summary>
        public void HacerCivil()
        {
            civil = true;
            var mano = BuscarHijo(transform, "AC_arma_r");
            if (mano != null)
                foreach (var r in mano.GetComponentsInChildren<Renderer>()) r.enabled = false;
        }

        /// <summary>Cambia el puesto que patrulla (las oleadas van a por la aldea).</summary>
        public void Dirigir(Vector3 puesto, float radio)
        {
            _puesto = puesto;
            _destino = puesto;
            radioPatrulla = radio;
            _relojEstado = 0f;
        }

        /// <summary>
        /// Que vaya a mirar a un sitio (un ruido, un aviso por radio, donde la vio): va (corriendo si es
        /// urgente), busca alrededor un rato y vuelve a lo suyo. Los civiles, los protegidos y los quietos
        /// (centinelas) no se mueven de su sitio: solo miran hacia alli.
        /// </summary>
        public void Investigar(Vector3 punto, bool urgente)
        {
            if (Muerto || Ahuyentado || protegido) return;
            // Una pista menos urgente no pisa a una urgente reciente.
            if (_pista.HasValue && _pistaUrgente && !urgente && Time.time < _pistaHasta - 40f) return;
            _sombras = punto;
            if (civil || quieto) { _quietoHasta = Time.time + 2.5f; return; }
            _pista = punto;
            _pistaUrgente = urgente;
            _pistaHasta = Time.time + 60f;
            _atascado = 0f;
            if (Actual == Estado.Buscar) Cambiar(Estado.Investigar);
        }

        void OlvidarPista()
        {
            _pista = null;
            _pistaUrgente = false;
            Avisado = false;
            Llamada = 0f;
        }

        void Oir(Sonido r)
        {
            if (Muerto || Ahuyentado || protegido || r.origen == gameObject) return;
            float d = Vector3.Distance(r.donde, transform.position);
            float alcance = r.radio * oido;
            if (d > alcance) return;
            float cerca = 1f - d / alcance;
            if (r.deCriatura)
            {
                // Lo que hace la criatura: de noche asusta; los que buscan van a ver (lejos, con error).
                if (ConMiedo) SumarMiedo((EsDeNoche ? 8f : 3f) * cerca + (r.tipo >= TipoRuido.Golpe ? 6f : 0f));
                if (Deteccion >= 1f) return;
                Vector3 error = Random.insideUnitSphere * d * 0.15f;
                Investigar(r.donde + new Vector3(error.x, 0f, error.z), r.tipo >= TipoRuido.Rotura);
            }
            else if (r.tipo >= TipoRuido.Disparo && !civil && Deteccion < 1f)
            {
                // Tiros o explosiones de los suyos: van a ver que pasa.
                Investigar(r.donde, true);
            }
        }

        static Transform BuscarHijo(Transform t, string nombre)
        {
            if (t.name == nombre) return t;
            foreach (Transform h in t)
            {
                var r = BuscarHijo(h, nombre);
                if (r != null) return r;
            }
            return null;
        }

        void Start()
        {
            _criatura = Criatura.Buscar();
            _relojEstado = Random.Range(0f, 3f);
            valor = Mathf.Clamp01(valor + Random.Range(-0.35f, 0.35f));
        }

        float TamanoCriatura => _criatura != null ? _criatura.Tamano : Criatura.TamanoReferencia;
        bool CriaturaPequena => _criatura != null && TamanoCriatura < tamanoAmenaza;

        void Update()
        {
            if (Actual == Estado.Muerto) return;
            if (_criatura == null) _criatura = Criatura.Jugador;
            bool hay = _criatura != null && !_criatura.Salud.Muerto;
            Vector3 aCriatura = hay ? _criatura.PuntoBlanco - Ojos : Vector3.forward * 9999f;
            float dCriatura = new Vector2(aCriatura.x, aCriatura.z).magnitude;
            // Lejos de la criatura (fuera de lo que podria verla u oirla) piensa uno de cada cuatro fotogramas.
            _acumulado += Time.deltaTime;
            if (dCriatura > Mathf.Max(vision * 1.5f, 300f) && Actual != Estado.Huida && !Ahuyentado && (++_fotograma + _desfase) % 4 != 0) return;
            float dt = _acumulado;
            _acumulado = 0f;
            _relojEstado -= dt;
            Ver(hay, aCriatura, dCriatura, dt);
            if (Deteccion >= 1f && hay) _ultimaVista = _criatura.transform.position;
            Avisar(hay, dt);
            if (ConMiedo)
            {
                Temer(hay, dCriatura, dt);
                if (Ahuyentado) { Escapar(aCriatura, hay, dt); return; }
            }
            bool laVe = Deteccion >= 1f;
            // Un animal que se les mete en el campamento (Enfrentar): mientras no vean a la criatura, le disparan.
            if (Enfrentando && !laVe && Actual != Estado.Huida && Time.time >= _miedoHasta)
            {
                PelearCon(_amenaza.position + Vector3.up * 0.4f, dt);
                return;
            }
            float panico = CriaturaPequena ? 0f : distanciaPanico * Mathf.Min(1f, TamanoCriatura / Criatura.TamanoReferencia);

            // Cambios de estado.
            if (_pista.HasValue && Time.time > _pistaHasta) { OlvidarPista(); if (Actual == Estado.Investigar || Actual == Estado.Buscar) Cambiar(Estado.Patrulla); }
            if (Time.time < _miedoHasta || (hay && dCriatura < panico)) Cambiar(Estado.Huida);
            else if (Actual == Estado.Huida)
            {
                // Pasado el susto, si ya no la ve, va (con cuidado) a donde la vio.
                if (!laVe && !civil && _ultimaVista != Vector3.zero) Investigar(_ultimaVista, false);
                Cambiar(laVe && !civil && !CriaturaPequena ? Estado.Alerta : Estado.Patrulla);
            }
            else if (CriaturaPequena && laVe)
            {
                // A una criatura pequena: los armados y los valientes van a por ella; los demas se apartan.
                // Los protegidos (los pueblos del lugar) la miran y no le hacen nada; aterrados, huyen.
                if (protegido) { if (Actual == Estado.Acoso) Cambiar(Estado.Patrulla); Girar(Ojos + aCriatura, dt * 2f); }
                else if (ConMiedo && miedo >= Aterrado) { if (dCriatura < 6f) Asustar(Random.Range(3f, 6f)); }
                else if (!civil || valor > 0.5f) { if (Actual != Estado.Acoso) Cambiar(Estado.Acoso); }
                else if (dCriatura < 4f) Asustar(Random.Range(2f, 4f));
            }
            else if (Actual == Estado.Acoso && (!laVe || !CriaturaPequena))
            {
                // La ha perdido: a donde la vio por ultima vez.
                if (!laVe && _ultimaVista != Vector3.zero) Investigar(_ultimaVista, true);
                Cambiar(Estado.Patrulla);
            }
            else if (civil) { if (Actual != Estado.Patrulla) Cambiar(Estado.Patrulla); }
            else if (laVe && dCriatura < vision && (Actual == Estado.Patrulla || Actual == Estado.Investigar || Actual == Estado.Buscar)) Cambiar(Estado.Alerta);
            else if ((!laVe || dCriatura > vision * 1.3f) && (Actual == Estado.Alerta || Actual == Estado.Disparo))
            {
                if (!laVe && _ultimaVista != Vector3.zero) Investigar(_ultimaVista, true);
                Cambiar(Estado.Patrulla);
            }
            else if (_pista.HasValue && Actual == Estado.Patrulla && !laVe && !(ConMiedo && miedo >= Aterrado)) Cambiar(Estado.Investigar);

            float velocidad = 0f;
            switch (Actual)
            {
                case Estado.Patrulla:
                    if (ruta != null && ruta.Length > 1)
                    {
                        // La ronda: de punto en punto, ida y vuelta.
                        if (Plano(ruta[_iRuta] - transform.position).magnitude < 1f)
                        {
                            if (_iRuta + _sentidoRuta < 0 || _iRuta + _sentidoRuta >= ruta.Length)
                            {
                                _sentidoRuta = -_sentidoRuta;
                                // En los extremos se para un rato (lava la batea, vacia el saco).
                                if (pausaRuta > 0f) _pausaHasta = Time.time + pausaRuta * Random.Range(0.6f, 1.4f);
                            }
                            _iRuta += _sentidoRuta;
                        }
                        _destino = ruta[_iRuta];
                        _relojEstado = 5f;
                    }
                    else if (_relojEstado <= 0f || Plano(_destino - transform.position).magnitude < 1.5f)
                    {
                        var r = Random.insideUnitCircle * radioPatrulla;
                        _destino = _puesto + new Vector3(r.x, 0f, r.y);
                        _relojEstado = Random.Range(4f, 9f);
                    }
                    if (ConMiedo && Time.time < _sombrasHasta)
                    {
                        // Aterrado: dispara a las sombras (a donde cree que esta).
                        Girar(_sombras, dt * 6f);
                        Disparar(dt, _sombras);
                    }
                    else if (ConMiedo && Time.time < _quietoHasta) Girar(_sombras, dt * 2f);
                    else if (Time.time < _pausaHasta) { }
                    else if (!quieto || (ruta != null && ruta.Length > 1)) velocidad = Andar(_destino, velocidadAndar * (ConMiedo && miedo >= Nervioso ? 0.75f : 1f), dt);
                    break;
                case Estado.Alerta:
                case Estado.Disparo:
                    Girar(Ojos + aCriatura, dt * 3f);
                    // Se acerca hasta su distancia de tiro, luego dispara a rafagas.
                    if (dCriatura > distanciaDisparo)
                    {
                        velocidad = Andar(transform.position + Plano(aCriatura).normalized * 5f, velocidadCorrer * 0.7f, dt);
                        _enRafaga = false;
                    }
                    else Disparar(dt);
                    Actual = dCriatura <= distanciaDisparo ? Estado.Disparo : Estado.Alerta;
                    break;
                case Estado.Huida:
                    velocidad = Andar(transform.position - Plano(aCriatura).normalized * 8f, velocidadCorrer, dt);
                    _enRafaga = false;
                    break;
                case Estado.Acoso:
                    velocidad = Acosar(aCriatura, dCriatura, dt);
                    break;
                case Estado.Investigar:
                    velocidad = IrAPista(dt);
                    break;
                case Estado.Buscar:
                    velocidad = BuscarAlrededor(dt);
                    break;
            }

            if (_anim != null)
            {
                _anim.SetFloat(PVelocidad, velocidad, 0.15f, dt);
                _anim.SetBool(PApuntar, Actual == Estado.Disparo || Actual == Estado.Alerta || (ConMiedo && Time.time < _sombrasHasta));
                _anim.SetBool(PDisparar, _enRafaga);
                _anim.SetBool(PMiedo, Actual == Estado.Huida);
            }
        }

        // ------------------------------------------------------------------ Pistas, busqueda y radio
        Vector3? _pista;
        bool _pistaUrgente;
        float _pistaHasta, _buscarHasta, _atascado, _acumulado;
        int _fotograma;
        int _desfase;
        static int s_humanos;
        Vector3 _ultimaVista;

        /// <summary>
        /// Hasta donde se acerca a la pista: a una criatura pequena, encima (la busca entre la hierba); a una
        /// grande, a un par de largos (se acercan con cuidado y miran desde ahi).
        /// </summary>
        float LlegadaPista => CriaturaPequena ? 2.5f : Mathf.Clamp(TamanoCriatura * 2f, 2.5f, 40f);

        /// <summary>Va hacia la pista; al llegar (o si no puede seguir) se pone a buscar alrededor.</summary>
        float IrAPista(float dt)
        {
            if (!_pista.HasValue) { Cambiar(Estado.Patrulla); return 0f; }
            Vector3 p = _pista.Value;
            float v = Andar(p, _pistaUrgente ? velocidadCorrer * 0.8f : velocidadAndar * 1.25f, dt);
            _atascado = v <= 0f ? _atascado + dt : 0f;
            if (Plano(p - transform.position).magnitude < LlegadaPista || _atascado > 2.5f)
            {
                _buscarHasta = Time.time + segundosBusqueda * Random.Range(0.8f, 1.2f);
                _destino = p;
                _relojEstado = 0f;
                Cambiar(Estado.Buscar);
            }
            return v;
        }

        /// <summary>Busca alrededor de la pista: va de un punto a otro mirando; al rato, vuelve a lo suyo.</summary>
        float BuscarAlrededor(float dt)
        {
            if (!_pista.HasValue || Time.time > _buscarHasta)
            {
                OlvidarPista();
                Cambiar(Estado.Patrulla);
                return 0f;
            }
            if (_relojEstado <= 0f || Plano(_destino - transform.position).magnitude < 1.2f)
            {
                // A una pequena, por encima de donde estaba; a una grande, en un anillo a su distancia.
                var r = CriaturaPequena ? Random.insideUnitCircle * Random.Range(3f, 9f)
                                        : Random.insideUnitCircle.normalized * (LlegadaPista + Random.Range(0.5f, 6f));
                _destino = _pista.Value + new Vector3(r.x, 0f, r.y);
                _relojEstado = Random.Range(2f, 4f);
            }
            return Andar(_destino, velocidadAndar, dt);
        }

        /// <summary>Si la ve y lleva radio, avisa: unos segundos al habla; si lo callan antes (muerto, ahuyentado, ciego), no llega.</summary>
        void Avisar(bool hay, float dt)
        {
            // Por una criatura pequena no se llama a nadie: se la busca a machetazos.
            if (civil || !radio || Ahuyentado || Avisado || CriaturaPequena) return;
            if (hay && Deteccion >= 1f)
            {
                Llamada = Mathf.Min(1f, Llamada + dt / Mathf.Max(0.1f, segundosLlamada));
                if (Llamada >= 1f)
                {
                    Avisado = true;
                    AlAvisar?.Invoke(this, _ultimaVista != Vector3.zero ? _ultimaVista : _criatura.transform.position);
                }
            }
            else Llamada = Mathf.MoveTowards(Llamada, 0f, dt * 0.2f);
        }

        // ------------------------------------------------------------------ Miedo
        int _iRuta, _sentidoRuta = 1;
        float _pausaHasta;
        float _ultimoSusto = -100f, _relojContagio, _sombrasHasta, _quietoHasta, _relojSombras, _ahuyentadoDesde;
        Vector3 _sombras, _huida;
        System.Collections.Generic.HashSet<MarcaLeyenda> _marcasVistas;

        /// <summary>Suma miedo (los valientes lo notan menos).</summary>
        public void SumarMiedo(float cantidad)
        {
            if (Muerto || Ahuyentado || cantidad <= 0f) return;
            miedo = Mathf.Min(100f, miedo + cantidad * Mathf.Lerp(1.5f, 0.6f, valor));
            _ultimoSusto = Time.time;
        }

        bool EsDeNoche => CicloDia.Instancia != null && CicloDia.Instancia.EsDeNoche;

        /// <summary>Lo que le da miedo cada fotograma, el contagio y lo que hace con mucho miedo.</summary>
        void Temer(bool hay, float d, float dt)
        {
            if (protegido) return;
            float luz = Luz != null ? Mathf.Clamp01(Luz(Ojos)) : 1f;
            bool conLinterna = linterna != null && linterna.Encendida;
            // La oscuridad: de noche, sin luz, se pone nervioso poco a poco.
            if (luz < 0.3f && miedo < Nervioso * (conLinterna ? 0.5f : 0.9f)) miedo += (conLinterna ? 0.3f : 1f) * dt;
            if (hay && Deteccion > 0.15f)
            {
                _sombras = _criatura.transform.position;
                if (TamanoCriatura >= tamanoAmenaza) SumarMiedo(25f * Deteccion * dt);
                // Lo que se ve de reojo da mas miedo que lo que se ve entero, sobre todo a oscuras.
                else if (Deteccion < 1f || luz < 0.35f) SumarMiedo((EsDeNoche ? 9f : 2.5f) * Deteccion * dt);
                // De noche, los ojos de la criatura brillan cuando les da la luz.
                if (EsDeNoche && d < 25f && FuenteLuz.Total(_criatura.PuntoBlanco) > 0.25f) SumarMiedo(14f * dt);
            }
            // Las marcas de leyenda (camisas mudadas, estatuas, rastros): una vez cada una.
            foreach (var m in MarcaLeyenda.Todas)
            {
                if (m == null || (m.transform.position - transform.position).sqrMagnitude > m.radio * m.radio) continue;
                if (_marcasVistas == null) _marcasVistas = new System.Collections.Generic.HashSet<MarcaLeyenda>();
                if (_marcasVistas.Add(m)) SumarMiedo(m.miedo);
            }
            // El miedo se contagia a los de alrededor.
            _relojContagio -= dt;
            if (_relojContagio <= 0f)
            {
                _relojContagio = 0.5f;
                if (miedo > Nervioso)
                    foreach (var o in Todos)
                    {
                        if (o == this || o.Muerto || o.protegido || o.miedo >= miedo - 10f) continue;
                        if ((o.transform.position - transform.position).sqrMagnitude < 100f) o.SumarMiedo((miedo - Nervioso) * 0.025f);
                    }
            }
            // Se calma con luz y sin sustos.
            if (Deteccion < 0.05f && luz > 0.5f && Time.time - _ultimoSusto > 12f) miedo = Mathf.Max(0f, miedo - 1.5f * dt);

            if (!HaEstadoAterrado && miedo >= Aterrado)
            {
                HaEstadoAterrado = true;
                AlAterrarse?.Invoke(this);
            }
            if (miedo >= Roto) { Ahuyentar(); return; }
            // Aterrado: los armados disparan a las sombras; los demas se quedan helados.
            _relojSombras -= dt;
            if (miedo >= Aterrado && _relojSombras <= 0f && Actual == Estado.Patrulla)
            {
                _relojSombras = Random.Range(4f, 9f);
                if (_sombras == Vector3.zero) _sombras = transform.position + transform.forward * 10f;
                Vector3 azar = Random.insideUnitSphere * 4f;
                _sombras += new Vector3(azar.x, 0f, azar.z);
                if (!civil) _sombrasHasta = Time.time + Random.Range(0.5f, 1.2f);
                else _quietoHasta = Time.time + Random.Range(1.5f, 3.5f);
            }
        }

        /// <summary>Tira el arma y huye para no volver (cuenta como ahuyentado).</summary>
        public void Ahuyentar()
        {
            if (Ahuyentado || Muerto || protegido) return;
            Ahuyentado = true;
            miedo = 100f;
            if (!civil) HacerCivil();
            var c = _criatura != null ? _criatura : Criatura.Jugador;
            Vector3 lejos = c != null ? Plano(transform.position - c.transform.position) : transform.forward;
            if (lejos.sqrMagnitude < 0.01f) lejos = transform.forward;
            _huida = lejos.normalized;
            _ahuyentadoDesde = Time.time;
            _enRafaga = false;
            Cambiar(Estado.Huida);
            AlAhuyentar?.Invoke(this);
        }

        void Escapar(Vector3 aCriatura, bool hay, float dt)
        {
            float v = Andar(transform.position + _huida * 8f, velocidadCorrer, dt);
            // Si algo le corta el paso, prueba otro rumbo.
            if (v <= 0f) _huida = Quaternion.Euler(0f, Random.Range(-70f, 70f), 0f) * _huida;
            if (_anim != null)
            {
                _anim.SetFloat(PVelocidad, v, 0.15f, dt);
                _anim.SetBool(PApuntar, false);
                _anim.SetBool(PDisparar, false);
                _anim.SetBool(PMiedo, true);
            }
            // Se va de la zona: desaparece lejos de la criatura.
            float lejos = hay ? Plano(aCriatura).magnitude : 999f;
            if (Time.time - _ahuyentadoDesde > 40f && lejos > 60f) Destroy(gameObject);
        }

        void OtroMuere(Humano h)
        {
            if (!ConMiedo || h == this) return;
            float d = Vector3.Distance(h.transform.position, transform.position);
            if (d < 25f) SumarMiedo(40f * (1f - d / 25f) + 5f);
        }

        void OtroAtacado(Humano h)
        {
            if (!ConMiedo || h == this) return;
            // Lo oye gritar.
            float d = Vector3.Distance(h.transform.position, transform.position);
            if (d < 18f) SumarMiedo(20f * (1f - d / 18f) + 4f);
        }

        void LuzApagada(FuenteLuz f)
        {
            if (!ConMiedo || f == null) return;
            if ((f.transform.position - transform.position).sqrMagnitude < f.alcance * f.alcance * 4f) SumarMiedo(18f);
        }

        /// <summary>
        /// Lo que ve: una criatura grande se ve siempre y en todas direcciones; una pequena solo de
        /// frente, cerca, con luz y si no esta escondida, y tarda en reconocerla.
        /// </summary>
        void Ver(bool hay, Vector3 aCriatura, float d, float dt)
        {
            if (!hay) { Deteccion = Mathf.MoveTowards(Deteccion, 0f, dt * 0.5f); return; }
            float tamano = TamanoCriatura;
            float luz = Luz != null ? Mathf.Clamp01(Luz(_criatura.transform.position)) : 1f;
            // La termica: de noche ve casi como de dia (el calor), pero no del todo.
            if (termica) luz = Mathf.Max(luz, 0.6f);
            float alcance = Mathf.Min(vision, visionPorTamano * tamano * Mathf.Lerp(0.25f, 1f, luz));
            if (_criatura.Escondida) alcance = Mathf.Min(alcance, tamano * 1.2f);
            // Lo que hay en medio (relieve, paredes, selva), si el juego lo mira: solo si estaria a tiro.
            if (Visibilidad != null && d < alcance)
            {
                _relojVisibilidad -= dt;
                if (_relojVisibilidad <= 0f)
                {
                    _relojVisibilidad = Random.Range(0.15f, 0.25f);
                    _visibilidad = Mathf.Clamp01(Visibilidad(Ojos, _criatura.PuntoBlanco));
                }
                alcance *= _visibilidad;
            }
            bool grande = tamano >= 8f;
            float hacia = Vector3.Dot(transform.forward, Plano(aCriatura).normalized);
            // A una criatura grande se la ve en cualquier direccion; por el visor de la termica, solo de frente.
            float frente = termica ? Mathf.Clamp01((hacia - 0.6f) / 0.3f) : grande ? 1f : Mathf.Clamp01((hacia + 0.35f) / 0.6f);
            float ve = d < alcance ? frente * (1f - d / Mathf.Max(alcance, 0.01f)) : 0f;
            if (ve > 0f)
            {
                // Grandes: al instante. Pequenas: mas deprisa cuanto mas cerca, mas grande y mas rapida.
                float rapidez = grande ? 100f : (0.6f + 2.5f * ve) * Mathf.Lerp(0.6f, 1.8f, Mathf.Clamp01(_criatura.Velocidad.magnitude / Mathf.Max(tamano, 0.3f)));
                Deteccion = Mathf.Min(1f, Deteccion + rapidez * dt);
            }
            else Deteccion = Mathf.MoveTowards(Deteccion, 0f, dt * (Actual == Estado.Acoso ? 0.15f : 0.35f));
            if (Deteccion >= 1f && !_vista)
            {
                _vista = true;
                AlVer?.Invoke(this);
            }
        }

        Vector3 Ojos => transform.position + Vector3.up * 1.6f;

        static Vector3 Plano(Vector3 v) => new Vector3(v.x, 0f, v.z);

        void Cambiar(Estado e)
        {
            if (Actual == e) return;
            Actual = e;
            if (e == Estado.Huida)
            {
                AlHuir?.Invoke(this);
                if (!Asustado)
                {
                    Asustado = true;
                    AlAsustarse?.Invoke(this);
                }
            }
            _enRafaga = false;
        }

        float _rodeo, _rodeoHasta;
        static readonly float[] Rodeos = { 35f, -35f, 70f, -70f, 105f, -105f, 150f, -150f };

        float Andar(Vector3 hacia, float velocidad, float dt)
        {
            Vector3 d = Plano(hacia - transform.position);
            if (d.magnitude < 0.3f) return 0f;
            Vector3 dir = d.normalized;
            float paso = velocidad * dt;
            // Rodeando: sigue el rumbo desviado un rato; si el directo ya esta libre, lo deja.
            if (Time.time < _rodeoHasta)
            {
                if (Libre(dir, paso)) _rodeoHasta = 0f;
                else dir = Quaternion.Euler(0f, _rodeo, 0f) * dir;
            }
            if (!Libre(dir, paso))
            {
                // Algo corta el paso (agua honda, una cuesta, una roca): el primer rumbo libre a un lado o al otro.
                bool hay = false;
                foreach (float a in Rodeos)
                {
                    var alt = Quaternion.Euler(0f, a, 0f) * d.normalized;
                    if (!Libre(alt, paso)) continue;
                    _rodeo = a;
                    _rodeoHasta = Time.time + Random.Range(0.8f, 1.6f);
                    dir = alt;
                    hay = true;
                    break;
                }
                if (!hay) return 0f;
            }
            Girar(transform.position + dir, dt * 6f);
            Vector3 p = transform.position + dir * paso;
            if (TerrenoMundo.Instancia != null) p.y = Mapa.Altura(p.x, p.z);
            transform.position = p;
            return velocidad;
        }

        /// <summary>Se puede dar el paso en esa direccion: sin agua honda, sin pared y sin obstaculo delante.</summary>
        bool Libre(Vector3 dir, float paso)
        {
            Vector3 p = transform.position + dir * paso;
            if (TerrenoMundo.Instancia != null)
            {
                float y = Mapa.Altura(p.x, p.z);
                // No se mete en el agua profunda ni trepa paredes.
                if (Mapa.Profundidad(p.x, p.z) > 0.5f || Mathf.Abs(y - transform.position.y) > paso * 2.5f + 0.4f) return false;
                // Un poco mas adelante: el agua honda, antes de llegar a la orilla.
                Vector3 q = transform.position + dir * 1.2f;
                if (Mapa.Profundidad(q.x, q.z) > 0.8f) return false;
            }
            if (CapasObstaculo != 0 &&
                Physics.Raycast(transform.position + Vector3.up * 0.6f, dir, 0.9f, CapasObstaculo, QueryTriggerInteraction.Ignore))
                return false;
            return true;
        }

        void Girar(Vector3 hacia, float rapidez)
        {
            Vector3 d = Plano(hacia - transform.position);
            if (d.sqrMagnitude < 0.01f) return;
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(d), Mathf.Clamp01(rapidez));
        }

        /// <summary>Rafagas contra la criatura (o, aterrado, contra un punto: las sombras).</summary>
        void Disparar(float dt, Vector3? punto = null)
        {
            _relojRafaga -= dt;
            if (_relojRafaga <= 0f)
            {
                _enRafaga = !_enRafaga;
                _relojRafaga = _enRafaga ? Random.Range(duracionRafaga.x, duracionRafaga.y) : Random.Range(pausaRafagas.x, pausaRafagas.y);
            }
            if (!_enRafaga || (_criatura == null && !punto.HasValue)) return;
            _relojBala -= dt;
            while (_relojBala <= 0f)
            {
                _relojBala += 1f / cadencia;
                Vector3 origen = bocacha != null ? bocacha.position : Ojos;
                Vector3 objetivo;
                if (punto.HasValue) objetivo = punto.Value + Vector3.up * Random.Range(0f, 1.2f);
                else
                {
                    float medio = _criatura.AltoCuerpo * 0.5f;
                    objetivo = _criatura.transform.position + Vector3.up * Random.Range(_criatura.AlturaBlanco - medio, _criatura.AlturaBlanco + medio);
                }
                Vector3 dir = (objetivo - origen).normalized;
                dir = (dir + Random.insideUnitSphere * dispersion).normalized;
                Vector3 impacto = origen + dir * distanciaDisparo * 1.5f;
                if (Physics.Raycast(origen, dir, out var hit, distanciaDisparo * 1.5f, ~0, QueryTriggerInteraction.Ignore))
                {
                    impacto = hit.point;
                    var s = hit.collider.GetComponentInParent<Salud>();
                    if (s != null && s != _salud)
                        s.Danar(new DatosDanio { cantidad = danioBala, punto = hit.point, direccion = dir, tipo = TipoDanio.Bala, origen = gameObject });
                }
                AlDisparar?.Invoke(origen, impacto, this);
            }
            // La rafaga se oye (los suyos van a ver que pasa; los animales huyen).
            if (Time.time - _ultimoRuido > 0.8f)
            {
                _ultimoRuido = Time.time;
                Ruidos.Emitir(transform.position, 180f, TipoRuido.Disparo, false, gameObject);
            }
        }

        float _ultimoRuido = -100f;

        /// <summary>Va a por una criatura pequena y le da machetazos o pisotones cuando la alcanza.</summary>
        float Acosar(Vector3 aCriatura, float d, float dt)
        {
            _relojGolpe -= dt;
            float alcance = alcanceGolpe + _criatura.Radio;
            if (d > alcance * 0.85f)
                return Andar(_criatura.transform.position, valor > 0.75f ? velocidadCorrer : velocidadCorrer * 0.7f, dt);
            Girar(_criatura.transform.position, dt * 8f);
            if (_relojGolpe <= 0f)
            {
                _relojGolpe = pausaGolpes * Random.Range(0.85f, 1.2f);
                if (_anim != null && _animAtacar) _anim.SetTrigger(PAtacar);
                // El golpe llega donde estaba: si la criatura se aparta a tiempo, falla.
                Vector3 donde = transform.position + transform.forward * alcanceGolpe * 0.8f;
                AlGolpear?.Invoke(donde, this);
                Vector3 c = _criatura.transform.position;
                if (Plano(c - donde).magnitude < _criatura.Radio + 0.45f)
                    _criatura.Salud.Danar(new DatosDanio
                    {
                        cantidad = danioGolpe, punto = c + Vector3.up * _criatura.AlturaBlanco, direccion = transform.forward,
                        empuje = 1f, tipo = TipoDanio.Golpe, origen = gameObject,
                    });
            }
            return 0f;
        }

        Transform _amenaza;
        float _amenazaHasta;

        /// <summary>Esta disparando a un animal (Enfrentar).</summary>
        public bool Enfrentando => _amenaza != null && _amenaza.gameObject.activeInHierarchy && Time.time < _amenazaHasta;

        /// <summary>
        /// Un animal peligroso cerca (un jaguar en el campamento, una piara): los armados se vuelven y le disparan
        /// a rafagas unos segundos (los tiros se oyen: los demas vienen a ver); los civiles huyen. Si ven a la
        /// criatura, lo primero es ella.
        /// </summary>
        public void Enfrentar(Transform amenaza, float segundos)
        {
            if (Muerto || protegido || amenaza == null) return;
            if (civil) { Asustar(segundos * 0.6f); return; }
            if (_amenaza != amenaza) _enRafaga = false;
            _amenaza = amenaza;
            _amenazaHasta = Mathf.Max(_amenazaHasta, Time.time + segundos);
        }

        void PelearCon(Vector3 p, float dt)
        {
            Girar(p, dt * 8f);
            if (_anim != null)
            {
                _anim.SetFloat(PVelocidad, 0f, 0.15f, dt);
                _anim.SetBool(PApuntar, true);
                _anim.SetBool(PDisparar, _enRafaga);
                _anim.SetBool(PMiedo, false);
            }
            Disparar(dt, p);
        }

        /// <summary>Huye durante unos segundos.</summary>
        public void Asustar(float segundos)
        {
            if (Actual != Estado.Muerto) _miedoHasta = Mathf.Max(_miedoHasta, Time.time + segundos);
        }

        void OirIntimidacion(Vector3 donde, float radio)
        {
            if (Actual == Estado.Muerto) return;
            float d = Vector3.Distance(donde, transform.position);
            if (d < radio) _miedoHasta = Time.time + Random.Range(5f, 9f);
            // El siseo o el rugido se oyen mas lejos de lo que espantan.
            if (ConMiedo && d < radio * 4f)
            {
                SumarMiedo(30f * (1f - d / (radio * 4f)) + (d < radio ? 20f : 0f));
                _sombras = donde;
            }
        }

        void RecibirDanio(DatosDanio d)
        {
            if (_anim != null) _anim.SetTrigger(PGolpe);
            if (d.origen != null && d.origen.GetComponentInParent<Criatura>() != null)
            {
                if (ConMiedo) SumarMiedo(45f);
                AlSerAtacado?.Invoke(this);
            }
        }

        void Morir(DatosDanio d)
        {
            Actual = Estado.Muerto;
            Llamada = 0f;
            if (_anim != null) _anim.SetTrigger(PMuerto);
            AlMorir?.Invoke(this);
            // Un golpe fuerte los lanza por los aires: cuerpo rigido que vuela y rueda.
            if (d.empuje > 4f)
            {
                _cuerpo = gameObject.AddComponent<Rigidbody>();
                _cuerpo.mass = 80f;
                _cuerpo.interpolation = RigidbodyInterpolation.Interpolate;
                _cuerpo.linearVelocity = d.direccion * d.empuje * 1.6f + Vector3.up * d.empuje * 0.5f;
                _cuerpo.angularVelocity = Random.insideUnitSphere * 8f;
            }
            else if (d.tipo == TipoDanio.Pisoton || d.tipo == TipoDanio.Aplastado)
            {
                transform.localScale = new Vector3(1.2f, 0.25f, 1.2f);
            }
            Destroy(gameObject, 25f);
        }
    }
}
