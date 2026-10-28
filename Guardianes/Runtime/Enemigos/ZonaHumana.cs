using System.Collections.Generic;
using Guardianes.Combate;
using Guardianes.Criaturas;
using Guardianes.Mundo;
using UnityEngine;

namespace Guardianes.Enemigos
{
    /// <summary>
    /// Una zona donde los humanos estan danando la selva. Cuando la criatura se acerca, despiertan
    /// sus guardias (prefabs de humanos repartidos por el puesto), las instalaciones quedan activas
    /// y salen sus helicopteros (del helipuerto de la base o llegando desde fuera del mapa).
    /// La zona se libera al destruir todas sus instalaciones principales: los humanos que quedan
    /// huyen, la criatura gana su premio y la selva recupera el suelo. La compania puede volver a una
    /// zona liberada (Reocupar: cada juego decide cuando).
    /// </summary>
    public class ZonaHumana : MonoBehaviour
    {
        public string nombre;
        public TipoZona tipo;
        public float radio = 180f;
        public float distanciaActivacion = 520f;
        public GameObject[] prefabsGuardia = new GameObject[0];
        public int guardias = 14;
        [Tooltip("Parte de la guardia que son trabajadores desarmados (la campana pide no matarlos)")]
        [Range(0f, 1f)] public float proporcionCiviles;
        [Tooltip("Instalaciones que hay que destruir para liberar la zona (las levanta ConstructorInstalaciones al empezar)")]
        public List<Salud> instalaciones = new List<Salud>();
        public MaterialesInstalaciones materiales;
        public int semilla = 1;
        [Header("Helicopteros")]
        public GameObject prefabHelicoptero;
        [Tooltip("El primero sale del helipuerto si la zona tiene; los demas llegan volando desde el mar")]
        public int helicopteros;

        public bool Liberada { get; private set; }
        public bool Activa { get; private set; }
        public float Progreso => instalaciones.Count == 0 ? 0f : (float)_destruidas / instalaciones.Count;
        public int Destruidas => _destruidas;
        /// <summary>Las veces que la compania ha vuelto a ocuparla despues de liberada.</summary>
        public int Reocupaciones { get; private set; }
        /// <summary>Instalaciones principales que siguen en pie.</summary>
        public IEnumerable<Salud> EnPie()
        {
            foreach (var s in instalaciones)
                if (s != null && !s.Muerto) yield return s;
        }

        public static event System.Action<ZonaHumana> AlLiberar;
        public static event System.Action<ZonaHumana> AlDescubrir;
        /// <summary>Despierta su guardia (la criatura se acerca): cada juego le puede dar puestos y rondas.</summary>
        public static event System.Action<ZonaHumana> AlActivar;
        /// <summary>La compania vuelve a una zona liberada (Reocupar).</summary>
        public static event System.Action<ZonaHumana> AlReocupar;
        /// <summary>
        /// El prefab de cada uno de la guardia al despertar (la zona, su puesto en la guardia —los primeros son los
        /// civiles—, cuantos civiles hay y el que ha salido al azar de prefabsGuardia): cada juego puede repartir sus
        /// armas (de cada diez, tantos con escopeta...). Sin el, o si devuelve null, el del azar. El azar se tira igual.
        /// </summary>
        public static System.Func<ZonaHumana, int, int, GameObject, GameObject> ElegirGuardia;
        public static readonly List<ZonaHumana> Todas = new List<ZonaHumana>();

        readonly List<Humano> _vivos = new List<Humano>();
        readonly List<Helicoptero> _helicopteros = new List<Helicoptero>();
        Criatura _criatura;
        Transform _kong => _criatura != null ? _criatura.transform : null;
        public IReadOnlyList<Humano> Guardia => _vivos;
        /// <summary>Los helicopteros que ha mandado (los que ya no estan, null): cada juego puede retirarlos.</summary>
        public IReadOnlyList<Helicoptero> Helicopteros => _helicopteros;
        int _destruidas;
        bool _descubierta, _helicopterosEnviados;

        void OnEnable() => Todas.Add(this);
        void OnDisable() => Todas.Remove(this);

        void Start()
        {
            _criatura = Criatura.Buscar();
            if (materiales != null && instalaciones.Count == 0)
                instalaciones = ConstructorInstalaciones.Construir(this, materiales, semilla);
            foreach (var s in instalaciones)
                if (s != null) s.AlMorir += _ => InstalacionDestruida();
        }

        void Update()
        {
            if (_criatura == null) _criatura = Criatura.Jugador;
            if (_kong == null || Liberada) return;
            float d = Vector2.Distance(new Vector2(_kong.position.x, _kong.position.z), new Vector2(transform.position.x, transform.position.z));
            if (!_descubierta && d < radio + 120f)
            {
                _descubierta = true;
                AlDescubrir?.Invoke(this);
            }
            if (!Activa && d < distanciaActivacion) Activar();
            else if (Activa && d > distanciaActivacion * 1.4f) Dormir();
        }

        void Activar()
        {
            Activa = true;
            EnviarHelicopteros();
            if (prefabsGuardia.Length == 0) return;
            _vivos.RemoveAll(h => h == null);
            int faltan = guardias - _vivos.Count;
            int civiles = Mathf.RoundToInt(faltan * proporcionCiviles);
            for (int i = 0; i < faltan; i++)
            {
                var r = Random.insideUnitCircle * radio * 0.75f;
                var p = transform.position + new Vector3(r.x, 0f, r.y);
                p.y = Mapa.Altura(p.x, p.z);
                if (!Mapa.EsTierra(p)) continue;
                var prefab = prefabsGuardia[Random.Range(0, prefabsGuardia.Length)];
                if (ElegirGuardia != null)
                {
                    var elegido = ElegirGuardia(this, i, civiles, prefab);
                    if (elegido != null) prefab = elegido;
                }
                var go = Instantiate(prefab, p, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f), transform);
                var h = go.GetComponent<Humano>();
                if (h == null) continue;
                // Los primeros de la guardia son los civiles (reparto fijo, no al azar).
                if (i < civiles) h.HacerCivil();
                _vivos.Add(h);
            }
            AlActivar?.Invoke(this);
        }

        /// <summary>
        /// La primera vez que la criatura se acerca: el helicoptero del helipuerto arranca el rotor y
        /// despega; los demas llegan desde fuera, por el lado de la zona que da al borde del mapa.
        /// </summary>
        void EnviarHelicopteros()
        {
            if (_helicopterosEnviados || prefabHelicoptero == null || helicopteros <= 0) return;
            _helicopterosEnviados = true;
            var helipuerto = transform.Find("Helipuerto");
            Vector3 fuera = transform.position;
            fuera.y = 0f;
            fuera = fuera.sqrMagnitude > 1f ? fuera.normalized : Vector3.forward;
            for (int i = 0; i < helicopteros; i++)
            {
                Helicoptero h;
                if (i == 0 && helipuerto != null)
                {
                    Vector3 p = helipuerto.position + Vector3.up * 0.42f;
                    h = Instantiate(prefabHelicoptero, p, helipuerto.rotation).GetComponent<Helicoptero>();
                    h.Colocar(p, helipuerto.rotation);
                    h.Despegar();
                }
                else
                {
                    Vector3 p = transform.position + Quaternion.Euler(0f, (i - 1) * 35f - 15f, 0f) * fuera * 650f;
                    p.y = Mapa.Superficie(p.x, p.z) + 80f;
                    var rot = Quaternion.LookRotation(new Vector3(transform.position.x - p.x, 0f, transform.position.z - p.z));
                    h = Instantiate(prefabHelicoptero, p, rot).GetComponent<Helicoptero>();
                    h.Colocar(p, rot, true);
                }
                if (h == null) continue;
                h.Patrullar(transform.position);
                _helicopteros.Add(h);
            }
        }

        void Dormir()
        {
            Activa = false;
            // Los que siguen vivos se quedan (no reaparecen mas que hasta completar la guardia).
        }

        void InstalacionDestruida()
        {
            _destruidas++;
            if (_criatura != null) _criatura.Premiar(new Premio(MotivoPremio.InstalacionDestruida, 12f, transform.position));
            if (_destruidas >= instalaciones.Count && !Liberada) Liberar();
        }

        /// <summary>
        /// Ya liberada en una partida guardada (o en un objetivo saltado al retomarla): sin instalaciones,
        /// sin guardia y con el suelo limpio, sin avisos ni furia.
        /// </summary>
        public void DarPorLiberada()
        {
            if (Liberada) return;
            Liberada = true;
            foreach (var s in instalaciones)
                if (s != null) Destroy(s.gameObject);
            foreach (var h in _vivos)
                if (h != null) Destroy(h.gameObject);
            if (TerrenoMundo.Instancia != null) TerrenoMundo.Instancia.Purificar(transform.position, radio * 1.25f, 0.5f);
        }

        /// <summary>
        /// La compania vuelve a una zona liberada: levanta otra vez sus instalaciones (otras, con otra semilla; las
        /// que quedaran de antes se quitan) y, cuando la criatura se acerque, despierta una guardia nueva y vuelve a
        /// mandar sus helicopteros. Liberarla otra vez da otra vez su premio.
        /// </summary>
        public void Reocupar()
        {
            if (!Liberada) return;
            Liberada = false;
            Activa = false;
            Reocupaciones++;
            _destruidas = 0;
            _helicopterosEnviados = false;
            _vivos.RemoveAll(h => h == null);
            // Lo que quedara del campamento de antes (lonas, bidones, troncos) se va: el nuevo se monta en otro orden.
            foreach (var e in GetComponentsInChildren<Estructura>())
                if (e != null) { e.name += " (vieja)"; Destroy(e.gameObject); }
            instalaciones = materiales != null ? ConstructorInstalaciones.Construir(this, materiales, semilla + 7919 * Reocupaciones) : new List<Salud>();
            foreach (var s in instalaciones)
                if (s != null) s.AlMorir += _ => InstalacionDestruida();
            AlReocupar?.Invoke(this);
        }

        /// <summary>Quita (sin explosiones ni avisos) 'cuantas' instalaciones que ya estaban rotas en la partida guardada.</summary>
        public void Retirar(int cuantas)
        {
            for (int i = instalaciones.Count - 1; i >= 0 && cuantas > 0; i--)
            {
                var s = instalaciones[i];
                instalaciones.RemoveAt(i);
                if (s == null) continue;
                Destroy(s.gameObject);
                cuantas--;
            }
        }

        void Liberar()
        {
            Liberada = true;
            AlLiberar?.Invoke(this);
            // Los supervivientes huyen de la zona.
            foreach (var h in _vivos)
                if (h != null) h.Asustar(30f);
            if (_criatura != null) _criatura.Premiar(new Premio(MotivoPremio.ZonaLiberada, 30f, transform.position));
            // La selva recupera el suelo que contaminaron.
            if (TerrenoMundo.Instancia != null) TerrenoMundo.Instancia.Purificar(transform.position, radio * 1.25f, 10f);
        }
    }
}
