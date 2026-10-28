using System.Collections.Generic;
using Guardianes.Camara;
using Guardianes.Combate;
using Guardianes.Enemigos;
using Guardianes.Criaturas;
using Guardianes.Mundo;
using Guardianes.Vegetacion;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using Random = UnityEngine.Random;

namespace Guardianes.Efectos
{
    /// <summary>
    /// Efectos visuales del combate y de la destruccion. Un emisor por tipo de particula (en espacio
    /// de mundo, sin emision propia) y todo se emite a mano donde pasa algo:
    ///   Ondas contra el suelo (machacar, saltos, coletazos): anillo de polvo, corona y escombros.
    ///   Pisadas al correr: polvo bajo manos y pies (a la escala de la criatura).
    ///   Explosiones: bola de fuego, chispas, escombros, onda, destello de luz y fuego con humo que
    ///   sigue ardiendo un rato.
    ///   Instalaciones que se rompen: nube de polvo y escombros.
    ///   Disparos: fogonazo, trazadora (no todas las balas) y polvillo en el impacto (nada si da en un blanco
    ///   sin colisionadores o rebota: la sangre la pone cada juego). Rebotes (Balas): chispas, una trazadora
    ///   desviada y un destello.
    ///   Arboles derribados: hojas al caer y polvo a lo largo del tronco al tocar el suelo.
    ///   Purificacion (la selva recupera el suelo, zona liberada): motas de luz y hojas en remolino.
    /// Las particulas usan Guardianes/Particula (iluminadas por el sol y el cielo, o emisivas en nits).
    /// </summary>
    public class EfectosCombate : MonoBehaviour
    {
        [Header("Materiales (Guardianes/Particula; los crea ConstructorEfectos)")]
        public Material polvo;
        public Material humo;
        public Material bolaFuego;
        public Material llamas;
        public Material chispas;
        public Material trazadoras;
        public Material fogonazos;
        public Material escombros;
        public Material hojas;
        public Material anillo;
        public Material motas;

        [Header("Ajustes")]
        [Range(0f, 1f)] public float proporcionTrazadoras = 0.45f;
        public int maximoFuegos = 24;
        public int lucesFuego = 4;
        public int lucesDestello = 4;
        [Tooltip("Particulas que se pueden emitir por fotograma (las rafagas grandes se recortan)")]
        public int presupuesto = 600;
        public float distanciaMaxima = 900f;

        public static EfectosCombate Instancia { get; private set; }
        /// <summary>Rebotes de bala que han llegado a los efectos (las pruebas; los lejanos cuentan aunque no se pinten).</summary>
        public int Rebotes { get; private set; }
        /// <summary>Polvillos de bala puestos donde pega el tiro, y tiros que no lo ponen por dar en un blanco o rebotar (las pruebas).</summary>
        public int Polvillos { get; private set; }
        public int SinPolvillo { get; private set; }
        /// <summary>Cada polvillo de bala (donde): las pruebas miran que no salga detras de un cuerpo.</summary>
        public event System.Action<Vector3> AlPolvillo;

        ParticleSystem _polvo, _humo, _bola, _llamas, _chispas, _trazas, _fogonazos, _escombros, _hojas, _anillo, _motas;
        CamaraCriatura _camara;
        int _gastado;
        int _ultimoFotograma = -1;

        class Fuego
        {
            public Vector3 pos;
            public float radio, inicio, fin, relojLlama, relojHumo, semilla;
            public Luz luz;
        }

        class Luz
        {
            public Light luz;
            public float inicio, duracion, candelas;
            public Fuego fuego;
            public bool Libre => fuego == null && Time.time > inicio + duracion;
        }

        readonly List<Fuego> _fuegos = new List<Fuego>();
        readonly List<Luz> _destellos = new List<Luz>();
        readonly List<Luz> _lucesFuego = new List<Luz>();

        static readonly Color ColorTierra = new Color(0.46f, 0.38f, 0.28f, 0.9f);
        static readonly Color ColorArena = new Color(0.72f, 0.66f, 0.52f, 0.85f);
        static readonly Color ColorHumo = new Color(0.16f, 0.15f, 0.14f, 0.85f);
        static readonly Color ColorPolvoObra = new Color(0.58f, 0.55f, 0.5f, 0.85f);

        // ------------------------------------------------------------------ Montaje
        void Awake()
        {
            Instancia = this;
            _polvo = Emisor("Polvo", polvo, 3000, (ps, r) =>
            {
                var main = ps.main;
                main.gravityModifier = -0.012f;
                Tamano(ps, 0f, 0.35f, 0.25f, 0.75f, 1f, 1f);
                Alfa(ps, 0f, 0f, 0.06f, 1f, 0.45f, 0.75f, 1f, 0f);
                Arrastre(ps, 0.9f);
                Giro(ps, 0.35f);
                Lamina(ps, 4, 4, false);
            });
            _humo = Emisor("Humo", humo, 2500, (ps, r) =>
            {
                var main = ps.main;
                main.gravityModifier = -0.01f;
                Tamano(ps, 0f, 0.3f, 0.3f, 0.7f, 1f, 1f);
                Alfa(ps, 0f, 0f, 0.08f, 1f, 0.55f, 0.6f, 1f, 0f);
                Arrastre(ps, 0.35f);
                Giro(ps, 0.2f);
                Lamina(ps, 4, 4, false);
                var n = ps.noise;
                n.enabled = true;
                n.strength = 1.4f;
                n.frequency = 0.05f;
                n.scrollSpeed = 0.12f;
                n.damping = true;
                n.octaveCount = 1;
                n.quality = ParticleSystemNoiseQuality.Low;
                var f = ps.forceOverLifetime;
                f.enabled = true;
                f.space = ParticleSystemSimulationSpace.World;
            });
            _bola = Emisor("BolaFuego", bolaFuego, 300, (ps, r) =>
            {
                var main = ps.main;
                main.gravityModifier = -0.04f;
                Tamano(ps, 0f, 0.55f, 0.3f, 0.92f, 1f, 1.15f);
                Arrastre(ps, 1.6f);
                Giro(ps, 0.5f);
                Lamina(ps, 8, 8, true);
            });
            _llamas = Emisor("Llamas", llamas, 1500, (ps, r) =>
            {
                var main = ps.main;
                main.gravityModifier = -0.25f;
                Tamano(ps, 0f, 0.6f, 0.35f, 1f, 1f, 0.55f);
                Alfa(ps, 0f, 0f, 0.12f, 1f, 0.7f, 1f, 1f, 0f);
                Arrastre(ps, 1f);
                Lamina(ps, 8, 8, true);
            });
            _chispas = Emisor("Chispas", chispas, 1500, (ps, r) =>
            {
                var main = ps.main;
                main.gravityModifier = 1f;
                Tamano(ps, 0f, 1f, 1f, 0.3f);
                Alfa(ps, 0f, 1f, 0.7f, 1f, 1f, 0f);
                Arrastre(ps, 0.5f);
                r.renderMode = ParticleSystemRenderMode.Stretch;
                r.velocityScale = 0.05f;
                r.lengthScale = 1f;
            });
            _trazas = Emisor("Trazadoras", trazadoras, 600, (ps, r) =>
            {
                // La raya mide su tamano mas la velocidad por velocityScale: a 420 m/s, unos 2,2 m, menos que la salida de
                // Disparar (2,5 m), asi que no cruza la boca ni al tirador (con 0,022 era de 9 m y le atravesaba el pecho).
                r.renderMode = ParticleSystemRenderMode.Stretch;
                r.velocityScale = 0.005f;
                r.lengthScale = 1f;
            });
            _fogonazos = Emisor("Fogonazos", fogonazos, 300, (ps, r) =>
            {
                Tamano(ps, 0f, 0.7f, 1f, 1.25f);
                Lamina(ps, 2, 2, false);
            });
            _escombros = Emisor("Escombros", escombros, 1500, (ps, r) =>
            {
                var main = ps.main;
                main.gravityModifier = 1.4f;
                Alfa(ps, 0f, 1f, 0.85f, 1f, 1f, 0f);
                Arrastre(ps, 0.12f);
                Giro(ps, 7f);
                Lamina(ps, 2, 2, false);
                var c = ps.collision;
                c.enabled = true;
                c.type = ParticleSystemCollisionType.World;
                c.mode = ParticleSystemCollisionMode.Collision3D;
                c.quality = ParticleSystemCollisionQuality.Medium;
                c.bounce = 0.25f;
                c.dampen = 0.45f;
                c.lifetimeLoss = 0f;
                c.radiusScale = 0.4f;
                c.collidesWith = ~0;
            });
            _hojas = Emisor("Hojas", hojas, 2500, (ps, r) =>
            {
                var main = ps.main;
                main.gravityModifier = 0.07f;
                Alfa(ps, 0f, 1f, 0.85f, 1f, 1f, 0f);
                Arrastre(ps, 0.8f);
                Giro(ps, 3f);
                Lamina(ps, 2, 2, false);
                var n = ps.noise;
                n.enabled = true;
                n.strength = 2.2f;
                n.frequency = 0.35f;
                n.scrollSpeed = 0.4f;
                n.damping = false;
                n.octaveCount = 1;
                n.quality = ParticleSystemNoiseQuality.Low;
            });
            _anillo = Emisor("Anillo", anillo, 60, (ps, r) =>
            {
                Tamano(ps, 0f, 0.2f, 0.25f, 0.75f, 1f, 1f);
                Alfa(ps, 0f, 0f, 0.05f, 1f, 0.5f, 0.6f, 1f, 0f);
                r.renderMode = ParticleSystemRenderMode.HorizontalBillboard;
                r.sortMode = ParticleSystemSortMode.None;
            });
            _motas = Emisor("Motas", motas, 1500, (ps, r) =>
            {
                var main = ps.main;
                main.gravityModifier = -0.04f;
                Tamano(ps, 0f, 0.2f, 0.2f, 1f, 1f, 0f);
                Alfa(ps, 0f, 0f, 0.15f, 1f, 0.7f, 1f, 1f, 0f);
                Arrastre(ps, 0.6f);
                var n = ps.noise;
                n.enabled = true;
                n.strength = 1.5f;
                n.frequency = 0.25f;
                n.scrollSpeed = 0.3f;
                n.octaveCount = 1;
                n.quality = ParticleSystemNoiseQuality.Low;
            });

            for (int i = 0; i < lucesDestello; i++) _destellos.Add(CrearLuz("Destello"));
            for (int i = 0; i < lucesFuego; i++) _lucesFuego.Add(CrearLuz("LuzFuego"));
        }

        ParticleSystem Emisor(string nombre, Material mat, int maximo, System.Action<ParticleSystem, ParticleSystemRenderer> configurar)
        {
            var go = new GameObject(nombre);
            go.transform.SetParent(transform, false);
            go.SetActive(false);
            var ps = go.AddComponent<ParticleSystem>();
            var r = go.GetComponent<ParticleSystemRenderer>();
            var main = ps.main;
            main.playOnAwake = false;
            main.loop = true;
            main.duration = 1f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = maximo;
            main.startSpeed = 0f;
            main.startLifetime = 1f;
            main.startSize = 1f;
            main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
            var em = ps.emission;
            em.enabled = false;
            var sh = ps.shape;
            sh.enabled = false;
            r.sharedMaterial = mat;
            r.renderMode = ParticleSystemRenderMode.Billboard;
            r.sortMode = ParticleSystemSortMode.Distance;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.minParticleSize = 0f;
            r.maxParticleSize = 4f;
            configurar(ps, r);
            go.SetActive(true);
            ps.Play();
            return ps;
        }

        static AnimationCurve Curva(params float[] tv)
        {
            var c = new AnimationCurve();
            for (int i = 0; i + 1 < tv.Length; i += 2) c.AddKey(tv[i], tv[i + 1]);
            return c;
        }

        static void Tamano(ParticleSystem ps, params float[] tv)
        {
            var s = ps.sizeOverLifetime;
            s.enabled = true;
            s.size = new ParticleSystem.MinMaxCurve(1f, Curva(tv));
        }

        static void Alfa(ParticleSystem ps, params float[] tv)
        {
            var g = new Gradient();
            var ak = new GradientAlphaKey[tv.Length / 2];
            for (int i = 0; i < ak.Length; i++) ak[i] = new GradientAlphaKey(tv[i * 2 + 1], tv[i * 2]);
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) }, ak);
            var c = ps.colorOverLifetime;
            c.enabled = true;
            c.color = new ParticleSystem.MinMaxGradient(g);
        }

        static void Arrastre(ParticleSystem ps, float arrastre)
        {
            var l = ps.limitVelocityOverLifetime;
            l.enabled = true;
            l.limit = 10000f;
            l.drag = arrastre;
            l.multiplyDragByParticleSize = false;
            l.multiplyDragByParticleVelocity = false;
        }

        static void Giro(ParticleSystem ps, float radianes)
        {
            var g = ps.rotationOverLifetime;
            g.enabled = true;
            g.z = new ParticleSystem.MinMaxCurve(-radianes, radianes);
        }

        /// <summary>Lamina de NxM: animada a lo largo de la vida o un fotograma al azar por particula.</summary>
        static void Lamina(ParticleSystem ps, int x, int y, bool animada)
        {
            var t = ps.textureSheetAnimation;
            t.enabled = true;
            t.mode = ParticleSystemAnimationMode.Grid;
            t.numTilesX = x;
            t.numTilesY = y;
            t.animation = ParticleSystemAnimationType.WholeSheet;
            t.cycleCount = 1;
            t.frameOverTime = animada ? new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0f, 1f, 1f)) : new ParticleSystem.MinMaxCurve(0f, 0.999f);
        }

        Luz CrearLuz(string nombre)
        {
            var go = new GameObject(nombre);
            go.transform.SetParent(transform, false);
            go.AddHDLight(LightType.Point);
            var l = go.GetComponent<Light>();
            l.color = new Color(1f, 0.62f, 0.32f);
            l.shadows = LightShadows.None;
            l.enabled = false;
            return new Luz { luz = l, inicio = -100f };
        }

        void OnEnable()
        {
            Impacto.AlImpactar += AlImpactar;
            Estructura.AlRomperse += Rotura;
            Estructura.AlExplotar += Explosion;
            Humano.AlDisparar += Disparo;
            Derribos.AlDerribar += Derribo;
            Derribos.AlTocarSuelo += ArbolEnSuelo;
            Criatura.AlPurificar += Purificar;
            Criatura.AlPisar += Pisada;
            ZonaHumana.AlLiberar += ZonaLiberada;
            Helicoptero.AlDisparar += DisparoCanon;
            Helicoptero.AlExplotar += HelicopteroExplota;
            Cohete.AlLanzar += CoheteSale;
            Cohete.AlExplotar += CoheteExplota;
            Balas.AlImpactar += Bala;
        }

        void OnDisable()
        {
            Impacto.AlImpactar -= AlImpactar;
            Estructura.AlRomperse -= Rotura;
            Estructura.AlExplotar -= Explosion;
            Humano.AlDisparar -= Disparo;
            Derribos.AlDerribar -= Derribo;
            Derribos.AlTocarSuelo -= ArbolEnSuelo;
            Criatura.AlPurificar -= Purificar;
            Criatura.AlPisar -= Pisada;
            ZonaHumana.AlLiberar -= ZonaLiberada;
            Helicoptero.AlDisparar -= DisparoCanon;
            Helicoptero.AlExplotar -= HelicopteroExplota;
            Cohete.AlLanzar -= CoheteSale;
            Cohete.AlExplotar -= CoheteExplota;
            Balas.AlImpactar -= Bala;
            if (Instancia == this) Instancia = null;
        }

        void Start()
        {
            _camara = FindAnyObjectByType<CamaraCriatura>();
        }

        // ------------------------------------------------------------------ Emision
        bool Cerca(Vector3 p, float margen = 0f)
        {
            var cam = Camera.main;
            return cam == null || (cam.transform.position - p).sqrMagnitude < (distanciaMaxima + margen) * (distanciaMaxima + margen);
        }

        // pintar falso: gasta lo mismo que una particula (su hueco del presupuesto y el Random del giro) sin emitirla, para
        // que quitar una no cambie el resto de efectos ni la secuencia de Random.
        bool Emitir(ParticleSystem ps, Vector3 pos, Vector3 vel, float tam, float vida, Color color, bool obligado = false, bool pintar = true)
        {
            if (Time.frameCount != _ultimoFotograma)
            {
                _ultimoFotograma = Time.frameCount;
                _gastado = 0;
            }
            if (!obligado && _gastado >= presupuesto) return false;
            _gastado++;
            float giro = Random.Range(0f, 360f);
            if (!pintar) return false;
            var e = new ParticleSystem.EmitParams
            {
                position = pos,
                velocity = vel,
                startSize = tam,
                startLifetime = vida,
                startColor = color,
                rotation = giro,
                applyShapeToPosition = false,
            };
            ps.Emit(e, 1);
            return true;
        }

        static float Altura(Vector3 p) => TerrenoMundo.Instancia != null ? Mapa.Altura(p.x, p.z) : p.y;

        static Vector3 AlSuelo(Vector3 p)
        {
            p.y = Altura(p);
            return p;
        }

        static Color ColorSuelo(Vector3 p)
        {
            var me = Mapa.Medidas;
            if (me.hayMar && Altura(p) < me.nivelMar + 2.5f) return ColorArena;
            var d = Mapa.Datos;
            return d != null && d.MascaraExtra("playa", new Unity.Mathematics.float2(p.x, p.z)) > 0.4f ? ColorArena : ColorTierra;
        }

        static Color Variar(Color c, float v)
        {
            float k = Random.Range(1f - v, 1f + v);
            return new Color(c.r * k, c.g * k, c.b * k, c.a);
        }

        void Sacudir(Vector3 donde, float fuerza, float alcance)
        {
            if (_camara == null) return;
            float d = Vector3.Distance(_camara.transform.position, donde);
            float k = Mathf.Clamp01(1f - d / alcance);
            if (k > 0f) _camara.Sacudir(fuerza * k * k);
        }

        void Destello(Vector3 pos, float lumenes, float rango, float duracion)
        {
            Luz mejor = null;
            foreach (var l in _destellos)
                if (mejor == null || l.inicio + l.duracion < mejor.inicio + mejor.duracion) mejor = l;
            if (mejor == null) return;
            mejor.inicio = Time.time;
            mejor.duracion = duracion;
            mejor.candelas = lumenes / (4f * Mathf.PI);
            mejor.luz.transform.position = pos;
            mejor.luz.range = rango;
            mejor.luz.intensity = mejor.candelas;
            mejor.luz.enabled = true;
        }

        // ------------------------------------------------------------------ Efectos
        /// <summary>Onda contra el suelo (machacar, aterrizar, explosiones, la ira de la selva).</summary>
        public void OndaSuelo(Vector3 centro, float radio)
        {
            if (!Cerca(centro, radio)) return;
            Vector3 suelo = AlSuelo(centro);
            Color c = ColorSuelo(suelo);
            Emitir(_anillo, suelo + Vector3.up * 0.6f, Vector3.zero, radio * 2.6f, Mathf.Lerp(0.9f, 1.6f, radio / 70f), c, true);
            int n = Mathf.RoundToInt(Mathf.Lerp(16f, 48f, Mathf.Clamp01(radio / 40f)));
            for (int i = 0; i < n; i++)
            {
                float a = (i + Random.value * 0.7f) / n * Mathf.PI * 2f;
                var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                Vector3 p = AlSuelo(suelo + dir * radio * Random.Range(0.12f, 0.35f)) + Vector3.up * Random.Range(0.5f, 2f);
                Vector3 v = dir * radio * Random.Range(0.7f, 1.3f) + Vector3.up * Random.Range(0.5f, 3f);
                Emitir(_polvo, p, v, radio * Random.Range(0.28f, 0.45f), Random.Range(2.5f, 5f), Variar(c, 0.12f));
            }
            // Columna de polvo en el centro y escombros (tierra y piedras).
            for (int i = 0; i < n / 3; i++)
                Emitir(_polvo, suelo + Random.insideUnitSphere * radio * 0.2f + Vector3.up * 2f, Vector3.up * Random.Range(2f, 6f) + Random.insideUnitSphere * 2f,
                       radio * Random.Range(0.3f, 0.5f), Random.Range(3f, 5f), Variar(c, 0.1f));
            Escombros(suelo + Vector3.up, radio * 0.5f, n, radio * 0.9f, false);
        }

        void Escombros(Vector3 centro, float dispersion, int cuantos, float velocidad, bool obra)
        {
            for (int i = 0; i < cuantos; i++)
            {
                Vector3 dir = Random.insideUnitSphere;
                dir.y = Mathf.Abs(dir.y) + 0.6f;
                Color c = obra ? Variar(new Color(0.75f, 0.72f, 0.68f, 1f), 0.2f) : Variar(Color.white, 0.15f);
                Emitir(_escombros, centro + Random.insideUnitSphere * dispersion, dir.normalized * velocidad * Random.Range(0.4f, 1f),
                       Random.Range(0.3f, 1.4f) * Mathf.Clamp(dispersion / 6f, 0.6f, 2.2f), Random.Range(2.5f, 5f), c);
            }
        }

        void AlImpactar(Vector3 centro, float radio, TipoDanio tipo)
        {
            switch (tipo)
            {
                case TipoDanio.Onda:
                case TipoDanio.Pisoton:
                    OndaSuelo(centro, radio);
                    break;
                case TipoDanio.Explosion:
                    // (Un cohete que revienta contra el pecho de Kong no levanta el suelo.)
                    if (centro.y - Altura(centro) < radio) OndaSuelo(centro, radio * 0.8f);
                    break;
                case TipoDanio.Golpe:
                case TipoDanio.Lanzado:
                    // Polvo solo si el golpe da cerca del suelo.
                    if (centro.y - Altura(centro) < 5f && Cerca(centro))
                    {
                        Color c = ColorSuelo(centro);
                        for (int i = 0; i < 8; i++)
                            Emitir(_polvo, AlSuelo(centro + Random.insideUnitSphere * radio * 0.4f) + Vector3.up, Random.insideUnitSphere * 4f + Vector3.up * 2f,
                                   radio * Random.Range(0.3f, 0.5f), Random.Range(1.8f, 3f), Variar(c, 0.1f));
                        Escombros(centro, radio * 0.3f, 10, radio * 1.2f, false);
                    }
                    break;
            }
        }

        void Rotura(Estructura e, Vector3 centro, float radio)
        {
            if (!Cerca(centro, radio)) return;
            int n = Mathf.Clamp(Mathf.RoundToInt(radio * 2.2f), 10, 45);
            for (int i = 0; i < n; i++)
            {
                Vector3 p = centro + Vector3.Scale(Random.insideUnitSphere, new Vector3(1f, 0.6f, 1f)) * radio * 0.7f;
                Emitir(_polvo, p, Random.insideUnitSphere * 3f + Vector3.up * 1.5f, radio * Random.Range(0.4f, 0.8f), Random.Range(3f, 6f), Variar(ColorPolvoObra, 0.12f));
            }
            Escombros(centro, radio * 0.4f, n * 2, 10f + radio * 0.6f, true);
            Sacudir(centro, 0.5f, 140f);
        }

        void Explosion(Vector3 centro, float radio) => Explosion(centro, radio, true);

        /// <summary>Bola de fuego, chispas, humo negro, escombros, destello y (si fuego) fuego que sigue ardiendo.</summary>
        public void Explosion(Vector3 centro, float radio, bool fuego)
        {
            if (!Cerca(centro, radio * 4f)) return;
            int n = 5 + Mathf.RoundToInt(radio / 5f);
            for (int i = 0; i < n; i++)
            {
                Vector3 p = centro + Random.insideUnitSphere * radio * 0.15f + Vector3.up * radio * 0.08f * i;
                Vector3 v = Random.insideUnitSphere * radio * 0.3f + Vector3.up * radio * Random.Range(0.15f, 0.4f);
                Emitir(_bola, p, v, radio * Random.Range(0.5f, 0.85f), Random.Range(1.2f, 1.9f), Color.white, true);
            }
            for (int i = 0; i < 70; i++)
            {
                Vector3 d = Random.insideUnitSphere;
                d.y = Mathf.Abs(d.y) * 1.3f + 0.2f;
                Emitir(_chispas, centro + Vector3.up, d.normalized * Random.Range(12f, 38f), Random.Range(0.15f, 0.4f), Random.Range(0.7f, 2.2f),
                       new Color(1f, Random.Range(0.55f, 0.85f), 0.35f, 1f));
            }
            // Humo negro que sube tras la bola de fuego.
            for (int i = 0; i < 10; i++)
                Emitir(_humo, centro + Random.insideUnitSphere * radio * 0.3f + Vector3.up * radio * 0.4f,
                       Vector3.up * Random.Range(3f, 8f) + Random.insideUnitSphere * 2f, radio * Random.Range(0.6f, 1f), Random.Range(7f, 12f), Variar(ColorHumo, 0.15f));
            Escombros(centro + Vector3.up, radio * 0.25f, 35, 16f + radio * 0.8f, true);
            Destello(centro + Vector3.up * 3f, 3e7f * Mathf.Clamp(radio / 18f, 0.5f, 2f), radio * 6f, 0.9f);
            if (fuego) AnadirFuego(AlSuelo(centro), radio * 0.35f, Random.Range(18f, 32f));
            Sacudir(centro, 1.8f, radio * 14f);
        }

        // ------------------------------------------------------------------ Helicopteros y cohetes
        void DisparoCanon(Vector3 origen, Vector3 impacto) => Disparar(origen, impacto, 1.8f);

        void HelicopteroExplota(Helicoptero h)
        {
            Vector3 c = h.transform.position + h.transform.rotation * h.centroMasas;
            Explosion(c, 16f, false);
            // Chapa y metralla, y los restos arden un buen rato.
            Escombros(c, 2.5f, 40, 22f, true);
            AnadirFuego(c, 3.5f, Random.Range(45f, 70f));
        }

        void CoheteSale(Vector3 pos, Vector3 dir)
        {
            if (!Cerca(pos)) return;
            Emitir(_fogonazos, pos, dir * 2f, 1.6f, 0.07f, Color.white);
            for (int i = 0; i < 3; i++)
                Emitir(_humo, pos - dir * (1f + i), -dir * Random.Range(3f, 8f) + Random.insideUnitSphere, Random.Range(1.5f, 2.5f), Random.Range(2f, 3.5f),
                       new Color(0.6f, 0.58f, 0.55f, 0.6f));
        }

        void CoheteExplota(Vector3 centro, float radio, bool enSuelo) => Explosion(centro, radio, enSuelo && Random.value < 0.35f);

        /// <summary>Lo que los vehiculos dejan cada fotograma: chorro del rotor, humo de los danos, estelas.</summary>
        void Vehiculos(float dt)
        {
            foreach (var h in Helicoptero.Todos)
            {
                if (h == null) continue;
                // Chorro del rotor: unas pocas particulas por segundo segun la fuerza.
                if (h.IntensidadChorro > 0.02f && Random.value < dt * 30f)
                    ChorroRotor(h.PuntoChorro, h.radioRotor, h.IntensidadChorro, h.ChorroSobreAgua ? 1 : h.ChorroSobreDosel ? 2 : 0);
                // Motores tocados: humo cada vez mas negro (y los restos echan humo hasta desaparecer).
                float dano = h.Danio;
                if (dano > 0.45f && h.toberas.Length > 0 && Random.value < dt * (6f + dano * 14f))
                {
                    var t = h.toberas[Random.Range(0, dano > 0.75f ? h.toberas.Length : 1)];
                    if (t != null) HumoDanio(t.position, h.Velocidad, Mathf.InverseLerp(0.45f, 1f, dano));
                }
            }
            foreach (var c in Cohete.Todos)
            {
                if (c == null) continue;
                Vector3 a = c.PosicionAnterior, b = c.transform.position;
                int n = Mathf.Clamp(Mathf.CeilToInt(Vector3.Distance(a, b) / 1.6f), 1, 8);
                for (int i = 0; i < n; i++) EstelaCohete(Vector3.Lerp(a, b, (i + 1f) / n), c.Velocidad);
            }
        }

        // El fogonazo, del tamano del arma (el fusil de asalto, 1).
        void Disparo(Vector3 origen, Vector3 impacto, Humano h) => Disparar(origen, impacto, h != null ? Arma.Ficha(h.arma).fogonazo : 1f);

        /// <summary>Fogonazo, trazadora y polvillo de un disparo (humanos, helicopteros).</summary>
        public void Disparar(Vector3 origen, Vector3 impacto, float escala)
        {
            if (!Cerca(origen)) return;
            Vector3 dir = impacto - origen;
            float dist = dir.magnitude;
            if (dist < 0.1f) return;
            dir /= dist;
            Emitir(_fogonazos, origen + dir * 0.35f * escala, dir * 1.5f, Random.Range(0.6f, 1.1f) * escala, 0.055f, Color.white);
            if (Random.value < proporcionTrazadoras)
            {
                // Sale a 2,5 m de la bocacha y se apaga al llegar a lo que toca (la piel de un blanco, la escama donde
                // rebota), sin vida minima: con ella, en un tiro cercano pasaba de largo y cruzaba el cuerpo. A menos de
                // 3 m no le queda recorrido y no se pinta (pero gasta lo mismo: ver Emitir). La raya mide Velocidad por el
                // velocityScale de las trazadoras (y su tamano) y tiene que caber en Salida: si no, se alarga hacia la
                // boca y atraviesa al tirador.
                const float Velocidad = 420f, Salida = 2.5f;
                Emitir(_trazas, origen + dir * Salida, dir * Velocidad, 0.11f, (dist - Salida) / Velocidad, new Color(1f, 0.82f, 0.5f, 1f),
                       pintar: dist > Salida + 0.5f);
            }
            // Si este tiro ha dado en un blanco sin colisionadores (o se ha quedado en su agua) o ha rebotado
            // (Balas.Ultimo, en este fotograma y en este punto), ni polvillo ni chispas: las pone Rebote, o la
            // sangre (o la salpicadura) del juego.
            var u = Balas.Ultimo;
            if (u.fotograma == Time.frameCount && (u.blanco != null || u.rebote) && (u.punto - impacto).sqrMagnitude < 1e-4f)
                SinPolvillo++;
            // Polvillo donde pega (si no es en el propio Kong, que no levanta tierra).
            else if (Mathf.Abs(impacto.y - Altura(impacto)) < 1.5f)
            {
                Emitir(_polvo, impacto + Vector3.up * 0.3f, Vector3.up * 1.5f + Random.insideUnitSphere, Random.Range(0.6f, 1.1f), Random.Range(0.8f, 1.4f), Variar(ColorSuelo(impacto), 0.1f));
                Polvillos++;
                AlPolvillo?.Invoke(impacto);
            }
            else if (Random.value < 0.3f)
                Emitir(_chispas, impacto, (-dir + Random.insideUnitSphere) * 6f, 0.08f, 0.25f, new Color(1f, 0.8f, 0.5f, 1f));
        }

        void Bala(Tiro t)
        {
            if (t.rebote) Rebote(t.punto, t.normal, t.direccion, EscalaRebote(t));
        }

        /// <summary>Lo grande que se pinta un rebote: con el calibre y, de lejos, mas (que se vea desde la camara de una criatura enorme).</summary>
        static float EscalaRebote(Tiro t)
        {
            var cam = Camera.main;
            float lejos = cam != null ? Mathf.Clamp(Vector3.Distance(cam.transform.position, t.punto) / 40f, 1f, 3f) : 1f;
            return Mathf.Clamp(0.5f + 0.15f * Mathf.Sqrt(Mathf.Max(0f, t.calibre)), 0.6f, 1.5f) * lejos;
        }

        /// <summary>
        /// Una bala que rebota (en una coraza, en las escamas): chispas en un cono alrededor del reflejo, a veces una
        /// trazadora desviada y un destello que de noche se ve. Sin polvillo. dir: hacia donde iba la bala.
        /// </summary>
        public void Rebote(Vector3 punto, Vector3 normal, Vector3 dir, float escala)
        {
            Rebotes++;
            if (!Cerca(punto)) return;
            Vector3 n = normal.sqrMagnitude > 1e-6f ? normal.normalized : -dir;
            Vector3 r = Vector3.Reflect(dir, n);
            if (Vector3.Dot(r, n) < 0f) r = Vector3.Reflect(r, n);
            int cuantas = Random.Range(6, 11);
            for (int i = 0; i < cuantas; i++)
            {
                // Cono de unos 35 grados alrededor del reflejo.
                Vector3 d = (r + Random.insideUnitSphere * 0.7f).normalized;
                Emitir(_chispas, punto + n * 0.03f, d * Random.Range(8f, 18f), Random.Range(0.05f, 0.09f) * escala, Random.Range(0.12f, 0.35f),
                       new Color(1f, Random.Range(0.75f, 0.95f), Random.Range(0.45f, 0.7f), 1f));
            }
            if (Random.value < 0.6f)
            {
                // La bala desviada sigue un poco (de 8 a 25 m, a 300 m/s).
                const float Velocidad = 300f;
                Vector3 d = (r + Random.insideUnitSphere * 0.35f).normalized;
                if (Vector3.Dot(d, n) < 0f) d = Vector3.Reflect(d, n);
                Emitir(_trazas, punto + d * 0.2f, d * Velocidad, 0.11f * escala, Random.Range(8f, 25f) / Velocidad, new Color(1f, 0.82f, 0.5f, 1f));
            }
            Emitir(_fogonazos, punto + n * 0.05f, Vector3.zero, Random.Range(0.15f, 0.3f) * escala, 0.04f, new Color(1f, 0.92f, 0.75f, 1f));
        }

        /// <summary>Estela de un cohete: humo que se queda y el resplandor del motor.</summary>
        public void EstelaCohete(Vector3 pos, Vector3 vel)
        {
            if (!Cerca(pos)) return;
            Emitir(_humo, pos - vel * 0.02f, Random.insideUnitSphere * 0.6f, Random.Range(1.2f, 2f), Random.Range(2.5f, 4f), new Color(0.55f, 0.53f, 0.5f, 0.55f));
            Emitir(_chispas, pos, vel * 0.1f, 0.9f, 0.05f, new Color(1f, 0.75f, 0.4f, 1f));
        }

        /// <summary>
        /// Chorro del rotor contra lo que tiene debajo: el aire baja a ~28 m/s (BEMT de Lur Aerospace)
        /// y se abre en corona. Sobre tierra levanta polvo y hojas, sobre el agua una corona de rocio y
        /// sobre el dosel arranca hojas de las copas. intensidad 0..1 (1 = muy bajo);
        /// superficie 0 tierra, 1 agua, 2 dosel (suelo.y es entonces la altura de las copas).
        /// </summary>
        public void ChorroRotor(Vector3 suelo, float radioRotor, float intensidad, int superficie = 0)
        {
            if (intensidad <= 0.02f || !Cerca(suelo)) return;
            if (superficie == 2)
            {
                var r = Random.insideUnitCircle * radioRotor;
                Emitir(_hojas, suelo + new Vector3(r.x, 0.5f, r.y), new Vector3(r.x, 0.5f, r.y).normalized * 10f * intensidad + Vector3.down * 2f,
                       Random.Range(0.3f, 0.8f), Random.Range(4f, 7f), Variar(Color.white, 0.15f));
                return;
            }
            Color c = superficie == 1 ? new Color(0.82f, 0.86f, 0.88f, 0.55f) : ColorSuelo(suelo);
            c.a *= 0.35f + 0.45f * intensidad;
            int n = Mathf.CeilToInt(3f * intensidad);
            for (int i = 0; i < n; i++)
            {
                float a = Random.Range(0f, Mathf.PI * 2f);
                var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                Vector3 p = suelo + dir * radioRotor * Random.Range(0.6f, 1.1f);
                p.y = superficie == 1 ? suelo.y : Altura(p);
                Emitir(_polvo, p + Vector3.up, dir * Random.Range(10f, 22f) * intensidad + Vector3.up * Random.Range(0.5f, 2f),
                       Random.Range(3f, 6f), Random.Range(1.5f, 2.6f), Variar(c, 0.1f));
            }
            if (superficie == 0 && Random.value < 0.35f * intensidad)
            {
                var r = Random.insideUnitCircle * radioRotor;
                Emitir(_hojas, AlSuelo(suelo + new Vector3(r.x, 0f, r.y)) + Vector3.up, new Vector3(r.x, 2f, r.y).normalized * 12f,
                       Random.Range(0.3f, 0.8f), Random.Range(3f, 6f), Variar(Color.white, 0.15f));
            }
        }

        /// <summary>Humo de un motor tocado (se va haciendo mas negro y denso con el dano).</summary>
        public void HumoDanio(Vector3 pos, Vector3 vel, float dano)
        {
            if (!Cerca(pos)) return;
            Color c = Color.Lerp(new Color(0.45f, 0.44f, 0.42f, 0.5f), new Color(0.1f, 0.09f, 0.09f, 0.85f), dano);
            Emitir(_humo, pos, -vel * 0.3f + Vector3.up * 2f + Random.insideUnitSphere, Random.Range(2f, 3.5f) * (0.7f + dano), Random.Range(3f, 6f), c);
        }

        /// <summary>Polvo de una pisada al correr (a la escala de la criatura: velocidades de Froude).</summary>
        void Pisada(Criatura quien, int extremidad, Vector3 p, float rapidez)
        {
            float e = quien != null ? Mathf.Max(quien.Escala, 0.01f) : 1f;
            float v = Mathf.Sqrt(e);
            if (rapidez < 4.5f * v || !Cerca(p)) return;
            float k = Mathf.InverseLerp(4.5f * v, 14f * v, rapidez);
            Color c = ColorSuelo(p);
            c.a *= 0.55f + 0.35f * k;
            int n = 2 + Mathf.RoundToInt(k * 4f);
            for (int i = 0; i < n; i++)
            {
                Vector3 d = Random.insideUnitSphere;
                d.y = 0f;
                Emitir(_polvo, AlSuelo(p + d * 1.5f * e) + Vector3.up * 0.6f * e, (d * Random.Range(1.5f, 4f) + Vector3.up * Random.Range(0.3f, 1.2f)) * v,
                       Random.Range(2f, 3.5f) * (0.8f + k * 0.6f) * e, Random.Range(1.6f, 2.8f) * Mathf.Max(0.4f, v), Variar(c, 0.1f));
            }
        }

        /// <summary>Polvo que levanta un cuerpo al arrastrarse (una serpiente): tamano en metros.</summary>
        public void Arrastre(Vector3 p, Vector3 velocidad, float tamano)
        {
            if (!Cerca(p)) return;
            Color c = ColorSuelo(p);
            c.a *= 0.45f;
            Vector3 d = Random.insideUnitSphere;
            d.y = 0f;
            Emitir(_polvo, AlSuelo(p + d * tamano * 0.5f) + Vector3.up * tamano * 0.15f, d * tamano * 0.6f + velocidad * 0.15f + Vector3.up * tamano * 0.2f,
                   tamano * Random.Range(0.8f, 1.5f), Random.Range(1.2f, 2.2f), Variar(c, 0.1f));
        }

        /// <summary>
        /// Una rociada de gotas de un color (la sangre o el veneno de un mordisco): sale de 'p' hacia 'dir' en
        /// cono y cae; tamano en metros (el del bocado).
        /// </summary>
        public void Gotas(Vector3 p, Vector3 dir, float tamano, Color color, int cuantas = 10)
        {
            if (!Cerca(p)) return;
            Vector3 d0 = dir.sqrMagnitude > 1e-6f ? dir.normalized : Vector3.up;
            for (int i = 0; i < cuantas; i++)
            {
                Vector3 d = (d0 + Random.insideUnitSphere * 0.6f + Vector3.up * 0.3f).normalized;
                Emitir(_polvo, p + Random.insideUnitSphere * tamano * 0.15f, d * tamano * Random.Range(2f, 5f),
                       tamano * Random.Range(0.18f, 0.45f), Random.Range(0.35f, 0.8f), Variar(color, 0.12f));
            }
        }

        /// <summary>Salpicadura en el agua (algo que entra, una criatura que nada): tamano en metros.</summary>
        public void Salpicadura(Vector3 p, float tamano, int cuantas = 12)
        {
            if (!Cerca(p)) return;
            var blanca = new Color(0.86f, 0.9f, 0.92f, 0.7f);
            for (int i = 0; i < cuantas; i++)
            {
                Vector3 d = Random.insideUnitSphere;
                d.y = Mathf.Abs(d.y) * 2.5f + 0.5f;
                Emitir(_polvo, p + Random.insideUnitSphere * tamano * 0.3f, d * tamano * Random.Range(1.5f, 3.5f),
                       tamano * Random.Range(0.4f, 0.9f), Random.Range(0.6f, 1.2f), Variar(blanca, 0.05f));
            }
        }

        void Derribo(Vector3 base_, EspecieVegetal esp, float fuerza)
        {
            if (esp == null || !Cerca(base_)) return;
            if (esp.tipo == TipoVegetal.Roca || esp.tipo == TipoVegetal.Suelo) return;
            float alto = esp.Limites.size.y;
            int n = esp.tipo == TipoVegetal.Arbol ? 22 : esp.tipo == TipoVegetal.Palmera ? 12 : 5;
            for (int i = 0; i < n; i++)
            {
                Vector3 p = base_ + Vector3.up * alto * Random.Range(0.5f, 0.95f) + Random.insideUnitSphere * alto * 0.25f;
                Emitir(_hojas, p, Random.insideUnitSphere * 2.5f + Vector3.up * 0.5f, Random.Range(0.35f, 0.9f), Random.Range(5f, 9f), Variar(Color.white, 0.15f));
            }
        }

        void ArbolEnSuelo(Vector3 base_, Vector3 punta, EspecieVegetal esp)
        {
            if (!Cerca(base_)) return;
            float largo = Vector3.Distance(base_, punta);
            int n = Mathf.Clamp(4 + Mathf.RoundToInt(largo / 5f), 4, 14);
            Color c = ColorSuelo(base_);
            for (int i = 0; i < n; i++)
            {
                Vector3 p = AlSuelo(Vector3.Lerp(base_, punta, (i + Random.value) / n)) + Vector3.up;
                Emitir(_polvo, p, Random.insideUnitSphere * 2.5f + Vector3.up * 1.5f, Random.Range(2.5f, 5f), Random.Range(2.2f, 3.8f), Variar(c, 0.1f));
            }
            for (int i = 0; i < 14; i++)
                Emitir(_hojas, punta + Random.insideUnitSphere * largo * 0.15f + Vector3.up * 2f, Random.insideUnitSphere * 3f + Vector3.up * 2f,
                       Random.Range(0.35f, 0.9f), Random.Range(5f, 9f), Variar(Color.white, 0.15f));
            if (largo > 15f) Sacudir(punta, 0.25f, 80f);
        }

        void Purificar(Vector3 centro, float radio)
        {
            OndaSuelo(centro, radio * 0.4f);
            for (int i = 0; i < 220; i++)
            {
                var r = Random.insideUnitCircle * radio;
                Vector3 p = AlSuelo(centro + new Vector3(r.x, 0f, r.y)) + Vector3.up * Random.Range(0.5f, 3f);
                Vector3 giro = Vector3.Cross(Vector3.up, p - centro).normalized * Random.Range(1f, 4f);
                Emitir(_motas, p, Vector3.up * Random.Range(1.5f, 5f) + giro, Random.Range(0.25f, 0.6f), Random.Range(2.5f, 5f),
                       Color.Lerp(new Color(0.55f, 1f, 0.35f, 1f), new Color(1f, 0.9f, 0.45f, 1f), Random.value), true);
            }
            for (int i = 0; i < 90; i++)
            {
                var r = Random.insideUnitCircle * radio * 0.6f;
                Vector3 p = AlSuelo(centro + new Vector3(r.x, 0f, r.y)) + Vector3.up * Random.Range(1f, 6f);
                Vector3 giro = Vector3.Cross(Vector3.up, p - centro).normalized * Random.Range(3f, 7f);
                Emitir(_hojas, p, giro + Vector3.up * Random.Range(1f, 4f), Random.Range(0.4f, 1f), Random.Range(5f, 9f), Variar(Color.white, 0.15f), true);
            }
        }

        void ZonaLiberada(ZonaHumana z) => Purificar(z.transform.position, Mathf.Min(z.radio, 120f));

        // ------------------------------------------------------------------ Fuegos
        /// <summary>Fuego que arde un rato (restos de explosiones, vehiculos, depositos).</summary>
        public void AnadirFuego(Vector3 pos, float radio, float duracion)
        {
            if (_fuegos.Count >= maximoFuegos) QuitarFuego(0);
            _fuegos.Add(new Fuego { pos = pos, radio = Mathf.Max(1.5f, radio), inicio = Time.time, fin = Time.time + duracion, semilla = Random.value * 100f });
        }

        void QuitarFuego(int i)
        {
            var f = _fuegos[i];
            if (f.luz != null) { f.luz.fuego = null; f.luz.luz.enabled = false; }
            _fuegos.RemoveAt(i);
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            var cam = Camera.main;
            Vector3 ojo = cam != null ? cam.transform.position : Vector3.zero;

            // El viento empuja el humo.
            if (Viento.Instancia != null)
            {
                var f = _humo.forceOverLifetime;
                Vector3 v = Viento.Instancia.Direccion * (0.6f + Viento.Instancia.fuerza * 1.5f);
                f.x = v.x;
                f.y = 0f;
                f.z = v.z;
            }

            for (int i = _fuegos.Count - 1; i >= 0; i--)
            {
                var f = _fuegos[i];
                if (Time.time > f.fin) { QuitarFuego(i); continue; }
                float vida = Mathf.InverseLerp(f.fin, f.fin - (f.fin - f.inicio) * 0.4f, Time.time); // se apaga al final
                if ((ojo - f.pos).sqrMagnitude > distanciaMaxima * distanciaMaxima) continue;
                f.relojLlama -= dt * (3f + f.radio * 2.5f) * Mathf.Max(0.25f, vida);
                while (f.relojLlama <= 0f)
                {
                    f.relojLlama += 1f;
                    var r = Random.insideUnitCircle * f.radio;
                    Vector3 p = f.pos + new Vector3(r.x, 0.2f, r.y);
                    float tam = f.radio * Random.Range(0.7f, 1.3f) * Mathf.Lerp(0.5f, 1f, vida);
                    Emitir(_llamas, p + Vector3.up * tam * 0.35f, Vector3.up * Random.Range(1f, 3f), tam, Random.Range(0.7f, 1.2f), Color.white);
                }
                f.relojHumo -= dt * (0.8f + f.radio * 0.25f);
                while (f.relojHumo <= 0f)
                {
                    f.relojHumo += 1f;
                    Emitir(_humo, f.pos + Vector3.up * (f.radio * 1.5f) + Random.insideUnitSphere * f.radio * 0.5f, Vector3.up * Random.Range(3f, 6f),
                           f.radio * Random.Range(2.5f, 4f), Random.Range(8f, 14f), Variar(ColorHumo, 0.15f));
                }
            }
            Vehiculos(dt);
            ActualizarLuces(ojo);
        }

        void ActualizarLuces(Vector3 ojo)
        {
            foreach (var l in _destellos)
            {
                if (!l.luz.enabled) continue;
                float t = (Time.time - l.inicio) / l.duracion;
                if (t >= 1f) { l.luz.enabled = false; continue; }
                l.luz.intensity = l.candelas * (1f - t) * (1f - t);
            }
            // Las luces de fuego van a los fuegos mas cercanos a la camara.
            foreach (var l in _lucesFuego)
                if (l.fuego != null && !_fuegos.Contains(l.fuego)) { l.fuego = null; l.luz.enabled = false; }
            foreach (var f in _fuegos)
            {
                if (f.luz != null) continue;
                Luz libre = null;
                foreach (var l in _lucesFuego) if (l.fuego == null) { libre = l; break; }
                if (libre == null)
                {
                    // Cambia la del fuego mas lejano si este esta mas cerca.
                    float peor = (f.pos - ojo).sqrMagnitude;
                    foreach (var l in _lucesFuego)
                    {
                        float d = (l.fuego.pos - ojo).sqrMagnitude;
                        if (d > peor) { peor = d; libre = l; }
                    }
                    if (libre == null) continue;
                    libre.fuego.luz = null;
                }
                libre.fuego = f;
                f.luz = libre;
                libre.luz.enabled = true;
                libre.luz.range = f.radio * 12f;
            }
            foreach (var l in _lucesFuego)
            {
                if (l.fuego == null) continue;
                var f = l.fuego;
                float vida = Mathf.InverseLerp(f.fin, f.fin - (f.fin - f.inicio) * 0.4f, Time.time);
                float parpadeo = 0.75f + 0.5f * Mathf.PerlinNoise(Time.time * 7f, f.semilla);
                l.luz.transform.position = f.pos + Vector3.up * f.radio * 1.2f;
                l.luz.intensity = 4e5f * f.radio / (4f * Mathf.PI) * parpadeo * vida;
            }
        }
    }
}
