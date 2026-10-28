using System.Collections;
using System.Collections.Generic;
using Guardianes.Combate;
using Guardianes.Enemigos;
using Guardianes.Criaturas;
using Guardianes.Mundo;
using Guardianes.Vegetacion;
using UnityEngine;
using UnityEngine.Serialization;
using Random = UnityEngine.Random;

namespace Guardianes.Audio
{
    /// <summary>
    /// Sonido del juego: un grupo de fuentes 3D reutilizables que suenan donde pasan las cosas
    /// (escucha los mismos eventos que los efectos visuales) y los ambientes en bucle (la selva,
    /// el rio mas cercano y los fuegos). Las distancias van a escala de criatura gigante: un rugido se
    /// oye a kilometros y un disparo a cientos de metros. La voz y las acciones de cada criatura las
    /// pone su juego (SonidoKong, SonidoSerpiente) con Sonar y los bancos de la criatura de aqui.
    /// Los clips los asigna el editor (ConstructorAudio).
    /// </summary>
    public class SonidoJuego : MonoBehaviour
    {
        [Header("Criatura")]
        [FormerlySerializedAs("rugidos")] public AudioClip[] voces;
        [FormerlySerializedAs("pechos")] public AudioClip[] percusion;
        public AudioClip[] pisadas;
        public AudioClip[] golpes;
        public AudioClip[] aspavientos;
        [Header("Armas y destruccion")]
        public AudioClip[] disparos;
        public AudioClip[] cohetes;
        public AudioClip[] explosiones;
        public AudioClip[] maderas;
        public AudioClip[] metales;
        public AudioClip[] arboles;
        [Tooltip("Balas que rebotan en una coraza o en las escamas (el piiu); sin ellos, una chapa muy aguda")]
        public AudioClip[] rebotes;
        [Header("Ambientes (bucles)")]
        public AudioClip selva;
        public AudioClip rio;
        public AudioClip fuego;

        [Header("Mezcla")]
        [Range(0f, 1f)] public float volumenGeneral = 1f;
        [Range(0f, 1f)] public float volumenAmbiente = 0.3f;
        [Range(0f, 1f)] public float volumenRio = 0.3f;
        public int fuentes = 40;
        [Tooltip("Disparos que pueden sonar por segundo (con un campamento entero disparando)")]
        public int disparosPorSegundo = 14;
        [Tooltip("Rebotes que pueden sonar por segundo (aparte de los disparos; los perdigones de un tiro cuentan uno)")]
        public int rebotesPorSegundo = 8;

        public static SonidoJuego Instancia { get; private set; }
        /// <summary>Rebotes que han sonado (las pruebas).</summary>
        public int RebotesSonados { get; private set; }

        readonly List<AudioSource> _libres = new List<AudioSource>();
        readonly List<AudioSource> _todas = new List<AudioSource>();
        AudioSource _selva, _rio;
        readonly List<(AudioSource fuente, float fin)> _fuegos = new List<(AudioSource, float)>();
        Transform _oyente;
        float _ventanaDisparos, _ventanaRebotes;
        int _disparosEnVentana, _rebotesEnVentana;
        // El ultimo tiro cuyo rebote ha sonado (los demas perdigones de ese tiro no suenan otra vez) y su fuente.
        int _fotogramaRebote = -1, _perdigonesRebote;
        GameObject _tiradorRebote;
        Vector3 _puntoRebote;
        AudioSource _fuenteRebote;
        AudioClip _clipRebote;
        float _volumenRebote;
        float _ultimoArbol;
        float _proximoRio;

        void Awake()
        {
            Instancia = this;
            for (int i = 0; i < fuentes; i++)
            {
                var go = new GameObject("Fuente" + i);
                go.transform.SetParent(transform, false);
                var s = go.AddComponent<AudioSource>();
                s.playOnAwake = false;
                s.spatialBlend = 1f;
                s.dopplerLevel = 0f;
                s.rolloffMode = AudioRolloffMode.Logarithmic;
                _todas.Add(s);
                _libres.Add(s);
            }
            _selva = Bucle("Selva", selva, false);
            _rio = Bucle("Rio", rio, true);
            if (_rio != null)
            {
                _rio.minDistance = 25f;
                _rio.maxDistance = 400f;
                _rio.volume = 0f;
            }
        }

        AudioSource Bucle(string nombre, AudioClip clip, bool espacial)
        {
            if (clip == null) return null;
            var go = new GameObject(nombre);
            go.transform.SetParent(transform, false);
            var s = go.AddComponent<AudioSource>();
            s.clip = clip;
            s.loop = true;
            s.playOnAwake = false;
            s.spatialBlend = espacial ? 1f : 0f;
            s.dopplerLevel = 0f;
            s.volume = volumenAmbiente * volumenGeneral;
            s.Play();
            return s;
        }

        void OnEnable()
        {
            Criatura.AlPisar += Pisada;
            Impacto.AlImpactar += AlImpactar;
            Estructura.AlRomperse += Rotura;
            Estructura.AlExplotar += Explosion;
            Humano.AlDisparar += Disparo;
            Derribos.AlDerribar += Derribo;
            Helicoptero.AlDisparar += DisparoCanon;
            Helicoptero.AlExplotar += HelicopteroExplota;
            Cohete.AlLanzar += CoheteSale;
            Cohete.AlExplotar += CoheteExplota;
            Balas.AlImpactar += Bala;
        }

        void OnDisable()
        {
            Criatura.AlPisar -= Pisada;
            Impacto.AlImpactar -= AlImpactar;
            Estructura.AlRomperse -= Rotura;
            Estructura.AlExplotar -= Explosion;
            Humano.AlDisparar -= Disparo;
            Derribos.AlDerribar -= Derribo;
            Helicoptero.AlDisparar -= DisparoCanon;
            Helicoptero.AlExplotar -= HelicopteroExplota;
            Cohete.AlLanzar -= CoheteSale;
            Cohete.AlExplotar -= CoheteExplota;
            Balas.AlImpactar -= Bala;
            if (Instancia == this) Instancia = null;
        }

        void Start()
        {
            if (FindAnyObjectByType<AudioListener>() == null && Camera.main != null)
                Camera.main.gameObject.AddComponent<AudioListener>();
            var oyente = FindAnyObjectByType<AudioListener>();
            _oyente = oyente != null ? oyente.transform : null;
        }

        // ------------------------------------------------------------------ Reproducir
        /// <summary>Suena un clip en un punto. minimo: distancia hasta la que suena a todo volumen.</summary>
        public AudioSource Sonar(AudioClip[] banco, Vector3 donde, float volumen = 1f, float minimo = 20f, float maximo = 1500f,
                                 float tono = 1f, float variacion = 0.06f, int prioridad = 128)
        {
            if (banco == null || banco.Length == 0) return null;
            return Sonar(banco[Random.Range(0, banco.Length)], donde, volumen, minimo, maximo, tono, variacion, prioridad);
        }

        public AudioSource Sonar(AudioClip clip, Vector3 donde, float volumen, float minimo, float maximo, float tono, float variacion, int prioridad)
        {
            if (clip == null) return null;
            if (_oyente != null && (donde - _oyente.position).sqrMagnitude > maximo * maximo) return null;
            AudioSource s = null;
            for (int i = _libres.Count - 1; i >= 0; i--)
            {
                if (!_libres[i].isPlaying) { s = _libres[i]; break; }
            }
            if (s == null)
            {
                // Sin fuentes libres: se reutiliza la menos prioritaria que ya va mas avanzada.
                float mejor = -1f;
                foreach (var f in _libres)
                {
                    float avance = f.clip != null ? f.time / f.clip.length : 1f;
                    if (f.priority >= prioridad && avance > mejor) { mejor = avance; s = f; }
                }
                if (s == null) return null;
            }
            s.Stop();
            s.loop = false;
            s.clip = clip;
            s.transform.position = donde;
            s.volume = volumen * volumenGeneral;
            s.pitch = tono * Random.Range(1f - variacion, 1f + variacion);
            s.minDistance = minimo;
            s.maxDistance = maximo;
            s.priority = prioridad;
            s.spread = minimo > 40f ? 90f : 0f;
            s.Play();
            return s;
        }

        /// <summary>Hace algo dentro de unos segundos (para encadenar sonidos de una accion).</summary>
        public void Despues(float segundos, System.Action accion) => StartCoroutine(RutinaDespues(segundos, accion));

        IEnumerator RutinaDespues(float segundos, System.Action accion)
        {
            yield return new WaitForSeconds(segundos);
            accion();
        }

        // ------------------------------------------------------------------ Eventos

        void AlImpactar(Vector3 centro, float radio, TipoDanio tipo)
        {
            switch (tipo)
            {
                case TipoDanio.Golpe:
                    Sonar(golpes, centro, 0.9f, 30f, 900f, 1f, 0.08f, 40);
                    break;
                case TipoDanio.Onda:
                case TipoDanio.Pisoton:
                    // Golpe contra el suelo: pisada muy grave y fuerte con un golpe encima.
                    Sonar(pisadas, centro, 1f, Mathf.Clamp(radio * 3f, 40f, 150f), 2500f, 0.62f, 0.05f, 10);
                    Sonar(golpes, centro, 0.8f, 40f, 1500f, 0.75f, 0.06f, 30);
                    break;
                case TipoDanio.Lanzado:
                    Sonar(golpes, centro, 0.9f, 30f, 1200f, 0.85f, 0.08f, 40);
                    Sonar(maderas, centro, 0.6f, 25f, 900f, 1f, 0.1f, 60);
                    break;
            }
        }

        void Pisada(Criatura quien, int extremidad, Vector3 punto, float rapidez)
        {
            float e = quien != null ? Mathf.Max(quien.Escala, 0.01f) : 1f;
            float vel = Mathf.Sqrt(e);
            if (rapidez < 0.5f * vel) return;
            bool mano = extremidad >= 2;
            float v = Mathf.Lerp(0.35f, 0.85f, Mathf.InverseLerp(1f * vel, 14f * vel, rapidez)) * (mano ? 0.7f : 1f) * Mathf.Clamp01(0.3f + e);
            // Una criatura pequena pisa flojo, cerca y mas agudo.
            Sonar(pisadas, punto, v, 25f * e, 700f * Mathf.Max(e, 0.05f), (mano ? 1.08f : 0.95f) / Mathf.Max(0.6f, Mathf.Pow(e, 0.15f)), 0.07f, 90);
        }

        // El tono, el del arma (el fusil de asalto, 1).
        void Disparo(Vector3 origen, Vector3 impacto, Humano h)
        {
            if (!HayHueco()) return;
            Sonar(disparos, origen, 0.55f, 10f, 800f, h != null ? Arma.Ficha(h.arma).tono : 1f, 0.07f, 150);
        }

        // Una bala que rebota: el piiu en el punto (de la pistola al fusil, mas fuerte), con su propio tope por segundo.
        // Los perdigones de un escopetazo rebotan en el mismo fotograma: suena uno por tiro (mismo tirador y fotograma, a
        // menos de 5 m), que gasta un hueco del tope, y cada perdigon mas que rebota lo sube un 10 % (hasta 1,8 veces).
        // Mientras no esten los clips, una chapa muy aguda.
        void Bala(Tiro t)
        {
            if (!t.rebote) return;
            if (t.fotograma == _fotogramaRebote && t.tirador == _tiradorRebote && (t.punto - _puntoRebote).sqrMagnitude < 25f)
            {
                _perdigonesRebote++;
                // La fuente sigue siendo la suya si no se la ha quitado otro sonido (que habria puesto su clip).
                if (_fuenteRebote != null && _fuenteRebote.isPlaying && _fuenteRebote.clip == _clipRebote)
                    _fuenteRebote.volume = Mathf.Min(1f, _volumenRebote * Mathf.Min(1.8f, 1f + 0.1f * (_perdigonesRebote - 1))) * volumenGeneral;
                return;
            }
            _fotogramaRebote = t.fotograma;
            _tiradorRebote = t.tirador;
            _puntoRebote = t.punto;
            _perdigonesRebote = 1;
            _fuenteRebote = null;
            if (Time.time > _ventanaRebotes + 1f) { _ventanaRebotes = Time.time; _rebotesEnVentana = 0; }
            if (_rebotesEnVentana >= rebotesPorSegundo) return;
            _rebotesEnVentana++;
            bool clips = rebotes != null && rebotes.Length > 0;
            float v = clips ? Mathf.Lerp(0.22f, 0.4f, Mathf.InverseLerp(Calibres.Pistola, Calibres.FusilAsalto, t.calibre)) : 0.22f;
            var s = clips
                ? Sonar(rebotes, t.punto, v, 4f, 220f, 1f, 0.15f, 170)
                : Sonar(metales, t.punto, v, 4f, 220f, 2.9f, 0.1f, 170);
            if (s == null) return;
            RebotesSonados++;
            _fuenteRebote = s;
            _clipRebote = s.clip;
            _volumenRebote = v;
        }

        bool HayHueco()
        {
            if (Time.time > _ventanaDisparos + 1f) { _ventanaDisparos = Time.time; _disparosEnVentana = 0; }
            if (_disparosEnVentana >= disparosPorSegundo) return false;
            _disparosEnVentana++;
            return true;
        }

        // El canon de 30 mm del helicoptero: el mismo disparo, mas grave y se oye mas lejos.
        void DisparoCanon(Vector3 origen, Vector3 impacto)
        {
            if (!HayHueco()) return;
            Sonar(disparos, origen, 0.7f, 25f, 1500f, 0.62f, 0.05f, 140);
        }

        void CoheteSale(Vector3 pos, Vector3 dir) => Sonar(cohetes, pos, 0.75f, 30f, 1800f, 1f, 0.08f, 60);

        void CoheteExplota(Vector3 centro, float radio, bool enSuelo) =>
            Sonar(explosiones, centro, 0.8f, 40f, 3000f, 1.15f, 0.1f, 20);

        void HelicopteroExplota(Helicoptero h)
        {
            Explosion(h.transform.position, 16f);
            Sonar(metales, h.transform.position, 1f, 40f, 2000f, 0.7f, 0.06f, 15);
        }

        void Rotura(Estructura e, Vector3 centro, float radio)
        {
            float v = Mathf.Clamp01(0.5f + radio / 20f);
            Sonar(maderas, centro, v, Mathf.Clamp(radio * 2f, 20f, 60f), 1500f, Mathf.Lerp(1.1f, 0.8f, radio / 20f), 0.08f, 30);
            if (Random.value < 0.6f)
                Sonar(metales, centro, v * 0.8f, 20f, 1200f, Random.Range(0.8f, 1.15f), 0.05f, 50);
        }

        void Explosion(Vector3 centro, float radio)
        {
            Sonar(explosiones, centro, 1f, Mathf.Clamp(radio * 4f, 50f, 150f), 5000f, 1f, 0.08f, 5);
            if (fuego == null) return;
            // El fuego que queda crepita un rato.
            var s = Sonar(fuego, centro, 0.35f, 10f, 200f, 1f, 0.05f, 120);
            if (s != null)
            {
                s.loop = true;
                s.time = Random.Range(0f, fuego.length);
                _fuegos.Add((s, Time.time + Random.Range(18f, 30f)));
            }
        }

        void Derribo(Vector3 base_, EspecieVegetal esp, float fuerza)
        {
            if (esp == null || (esp.tipo != TipoVegetal.Arbol && esp.tipo != TipoVegetal.Palmera)) return;
            // Kong a la carrera tumba muchos: no mas de un crujido de arbol cada poco.
            if (Time.time - _ultimoArbol < 0.35f) return;
            _ultimoArbol = Time.time;
            Sonar(arboles, base_ + Vector3.up * 6f, 0.85f, 30f, 1500f, Random.Range(0.85f, 1.1f), 0.05f, 60);
        }

        void Update()
        {
            if (_selva != null) _selva.volume = volumenAmbiente * volumenGeneral;
            for (int i = _fuegos.Count - 1; i >= 0; i--)
            {
                var (s, fin) = _fuegos[i];
                if (s == null) { _fuegos.RemoveAt(i); continue; }
                float queda = fin - Time.time;
                if (queda < 3f) s.volume = Mathf.Max(0f, queda / 3f) * 0.35f * volumenGeneral;
                if (queda <= 0f || s.clip != fuego) { if (s.clip == fuego) s.Stop(); s.loop = false; _fuegos.RemoveAt(i); }
            }
            ColocarRio();
        }

        /// <summary>El rumor del agua sale del punto de rio mas cercano al oyente.</summary>
        void ColocarRio()
        {
            if (_rio == null || _oyente == null || Time.time < _proximoRio) return;
            _proximoRio = Time.time + 0.5f;
            var datos = TerrenoMundo.Instancia != null ? TerrenoMundo.Instancia.Datos : null;
            if (datos == null) return;
            Vector3 o = _oyente.position;
            float mejor = 350f * 350f;
            Vector3 punto = o;
            bool hay = false, cascada = false;
            // El punto de rio (trazado denso de Rios) mas cercano al oyente.
            foreach (var rioPuntos in datos.rios)
                for (int i = 0; i < rioPuntos.Length; i += 2)
                {
                    var p = rioPuntos[i];
                    float dx = p.xz.x - o.x, dz = p.xz.y - o.z;
                    float d = dx * dx + dz * dz;
                    if (d < mejor) { mejor = d; punto = new Vector3(p.xz.x, p.nivel, p.xz.y); hay = true; cascada = p.cascada; }
                }
            if (hay) _rio.transform.position = Vector3.Lerp(_rio.transform.position, punto, 0.5f);
            _rio.maxDistance = cascada ? 500f : 250f;
            _rio.minDistance = cascada ? 35f : 12f;
            _rio.volume = Mathf.MoveTowards(_rio.volume, hay ? volumenRio * (cascada ? 1.3f : 1f) * volumenGeneral : 0f, 0.1f);
        }
    }
}
