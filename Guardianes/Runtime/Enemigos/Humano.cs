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
        [Range(0f, 1f)] public float valor = 0.5f;
        public float alcanceGolpe = 1.3f;
        public float danioGolpe = 30f;
        public float pausaGolpes = 1.1f;

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
            Todos.Add(this);
        }

        void OnDisable()
        {
            Criatura.AlIntimidar -= OirIntimidacion;
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
            bool laVe = Deteccion >= 1f;
            float panico = CriaturaPequena ? 0f : distanciaPanico * Mathf.Min(1f, TamanoCriatura / Criatura.TamanoReferencia);

            // Cambios de estado.
            if (Time.time < _miedoHasta || (hay && dCriatura < panico)) Cambiar(Estado.Huida);
            else if (Actual == Estado.Huida) Cambiar(laVe && !civil && !CriaturaPequena ? Estado.Alerta : Estado.Patrulla);
            else if (CriaturaPequena && laVe)
            {
                // A una criatura pequena: los armados y los valientes van a por ella; los demas se apartan.
                if (!civil || valor > 0.5f) { if (Actual != Estado.Acoso) Cambiar(Estado.Acoso); }
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
                    if (_relojEstado <= 0f || Plano(_destino - transform.position).magnitude < 1.5f)
                    {
                        var r = Random.insideUnitCircle * radioPatrulla;
                        _destino = _puesto + new Vector3(r.x, 0f, r.y);
                        _relojEstado = Random.Range(4f, 9f);
                    }
                    velocidad = Andar(_destino, velocidadAndar, dt);
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
                _anim.SetBool(PApuntar, Actual == Estado.Disparo || Actual == Estado.Alerta);
                _anim.SetBool(PDisparar, _enRafaga);
                _anim.SetBool(PMiedo, Actual == Estado.Huida);
            }
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
            float alcance = Mathf.Min(vision, visionPorTamano * tamano * Mathf.Lerp(0.25f, 1f, luz));
            if (_criatura.Escondida) alcance = Mathf.Min(alcance, tamano * 1.2f);
            bool grande = tamano >= 8f;
            float frente = grande ? 1f : Mathf.Clamp01((Vector3.Dot(transform.forward, Plano(aCriatura).normalized) + 0.35f) / 0.6f);
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

        void Disparar(float dt)
        {
            _relojRafaga -= dt;
            if (_relojRafaga <= 0f)
            {
                _enRafaga = !_enRafaga;
                _relojRafaga = _enRafaga ? Random.Range(duracionRafaga.x, duracionRafaga.y) : Random.Range(pausaRafagas.x, pausaRafagas.y);
            }
            if (!_enRafaga || _criatura == null) return;
            _relojBala -= dt;
            while (_relojBala <= 0f)
            {
                _relojBala += 1f / cadencia;
                Vector3 origen = bocacha != null ? bocacha.position : Ojos;
                float medio = _criatura.AltoCuerpo * 0.5f;
                Vector3 objetivo = _criatura.transform.position + Vector3.up * Random.Range(_criatura.AlturaBlanco - medio, _criatura.AlturaBlanco + medio);
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
            if ((donde - transform.position).sqrMagnitude < radio * radio)
                _miedoHasta = Time.time + Random.Range(5f, 9f);
        }

        void RecibirDanio(DatosDanio d)
        {
            if (_anim != null) _anim.SetTrigger(PGolpe);
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
