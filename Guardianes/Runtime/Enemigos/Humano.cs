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
    /// El Animator lo monta el editor (ConstructorHumanos) con estos parametros:
    ///   velocidad (float), apuntar (bool), disparar (bool), miedo (bool), golpe (trigger), muerto (trigger)
    ///   y, si lo tiene, atacar (trigger: el machetazo).
    /// </summary>
    [RequireComponent(typeof(Salud))]
    public class Humano : MonoBehaviour
    {
        public enum Estado { Patrulla, Alerta, Disparo, Huida, Muerto, Acoso }

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
        }

        void OnEnable()
        {
            Criatura.AlIntimidar += OirIntimidacion;
            AlMorir += OtroMuere;
            AlSerAtacado += OtroAtacado;
            FuenteLuz.AlApagarse += LuzApagada;
            Todos.Add(this);
        }

        void OnDisable()
        {
            Criatura.AlIntimidar -= OirIntimidacion;
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
            float dt = Time.deltaTime;
            _relojEstado -= dt;
            if (_criatura == null) _criatura = Criatura.Jugador;
            bool hay = _criatura != null && !_criatura.Salud.Muerto;
            Vector3 aCriatura = hay ? _criatura.PuntoBlanco - Ojos : Vector3.forward * 9999f;
            float dCriatura = new Vector2(aCriatura.x, aCriatura.z).magnitude;
            Ver(hay, aCriatura, dCriatura, dt);
            if (ConMiedo)
            {
                Temer(hay, dCriatura, dt);
                if (Ahuyentado) { Escapar(aCriatura, hay, dt); return; }
            }
            bool laVe = Deteccion >= 1f;
            float panico = CriaturaPequena ? 0f : distanciaPanico * Mathf.Min(1f, TamanoCriatura / Criatura.TamanoReferencia);

            // Cambios de estado.
            if (Time.time < _miedoHasta || (hay && dCriatura < panico)) Cambiar(Estado.Huida);
            else if (Actual == Estado.Huida) Cambiar(laVe && !civil && !CriaturaPequena ? Estado.Alerta : Estado.Patrulla);
            else if (CriaturaPequena && laVe)
            {
                // A una criatura pequena: los armados y los valientes van a por ella; los demas se apartan.
                // Los protegidos (los pueblos del lugar) la miran y no le hacen nada; aterrados, huyen.
                if (protegido) { if (Actual == Estado.Acoso) Cambiar(Estado.Patrulla); Girar(Ojos + aCriatura, dt * 2f); }
                else if (ConMiedo && miedo >= Aterrado) { if (dCriatura < 6f) Asustar(Random.Range(3f, 6f)); }
                else if (!civil || valor > 0.5f) { if (Actual != Estado.Acoso) Cambiar(Estado.Acoso); }
                else if (dCriatura < 4f) Asustar(Random.Range(2f, 4f));
            }
            else if (Actual == Estado.Acoso && (!laVe || !CriaturaPequena)) Cambiar(Estado.Patrulla);
            else if (civil) { if (Actual != Estado.Patrulla) Cambiar(Estado.Patrulla); }
            else if (laVe && dCriatura < vision && Actual == Estado.Patrulla) Cambiar(Estado.Alerta);
            else if ((!laVe || dCriatura > vision * 1.3f) && Actual != Estado.Patrulla && Actual != Estado.Acoso) Cambiar(Estado.Patrulla);

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
            }

            if (_anim != null)
            {
                _anim.SetFloat(PVelocidad, velocidad, 0.15f, dt);
                _anim.SetBool(PApuntar, Actual == Estado.Disparo || Actual == Estado.Alerta || (ConMiedo && Time.time < _sombrasHasta));
                _anim.SetBool(PDisparar, _enRafaga);
                _anim.SetBool(PMiedo, Actual == Estado.Huida);
            }
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

        float Andar(Vector3 hacia, float velocidad, float dt)
        {
            Vector3 d = Plano(hacia - transform.position);
            if (d.magnitude < 0.3f) return 0f;
            Girar(transform.position + d, dt * 6f);
            Vector3 p = transform.position + transform.forward * velocidad * dt;
            if (TerrenoMundo.Instancia != null)
            {
                float y = Mapa.Altura(p.x, p.z);
                // No se mete en el agua profunda ni trepa paredes.
                if (Mapa.Profundidad(p.x, p.z) > 0.5f || Mathf.Abs(y - transform.position.y) > velocidad * dt * 2.5f + 0.4f) return 0f;
                p.y = y;
            }
            transform.position = p;
            return velocidad;
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
        }

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
