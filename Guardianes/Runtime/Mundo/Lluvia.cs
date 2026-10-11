using UnityEngine;

namespace Guardianes.Mundo
{
    /// <summary>
    /// La lluvia (opcional: la pone cada juego o cada mision con Poner). Gotas alrededor de la camara, el
    /// cielo cubierto (CicloDia.nublado: menos sol, menos luna y menos luz para la IA de los humanos) y,
    /// para lo que cada juego quiera hacer con ella (que tape el olor, que ciegue un sentido), la
    /// Intensidad de ahora (0..1), que sube y baja despacio hacia la que se pide.
    /// </summary>
    public class Lluvia : MonoBehaviour
    {
        public static Lluvia Instancia { get; private set; }
        /// <summary>Lo que llueve ahora (0 nada .. 1 tormenta). Sin lluvia en la escena, 0.</summary>
        public static float Intensidad => Instancia != null ? Instancia._intensidad : 0f;

        [Tooltip("Hacia donde va la intensidad (0..1)")]
        public float objetivo;
        [Tooltip("Lo que tarda en ir de nada a tormenta (s)")]
        public float segundosCambio = 6f;
        [Tooltip("Gotas por segundo con tormenta")]
        public float gotasTormenta = 6000f;
        [Tooltip("Lado de la caja de gotas alrededor de la camara (m)")]
        public float lado = 36f;
        [Tooltip("Material de las gotas (Guardianes/Particula iluminada). Si falta, se hace uno con el sombreador de la niebla del agua")]
        public Material material;

        float _intensidad;
        ParticleSystem _gotas;
        Transform _camara;
        static Material _hecho;

        /// <summary>Pide lluvia: 'intensidad' (0..1) en 'segundos' (0, de golpe). La crea si no la hay.</summary>
        public static Lluvia Poner(float intensidad, float segundos = 6f)
        {
            if (Instancia == null)
            {
                if (intensidad <= 0f) return null;
                new GameObject("Lluvia").AddComponent<Lluvia>();
            }
            var l = Instancia;
            l.objetivo = Mathf.Clamp01(intensidad);
            l.segundosCambio = Mathf.Max(0.01f, segundos);
            if (segundos <= 0f) l._intensidad = l.objetivo;
            return l;
        }

        void Awake()
        {
            Instancia = this;
            CrearGotas();
        }

        void OnDestroy()
        {
            if (Instancia == this) Instancia = null;
            if (CicloDia.Instancia != null) CicloDia.Instancia.nublado = 0f;
        }

        void LateUpdate()
        {
            _intensidad = Mathf.MoveTowards(_intensidad, objetivo, Time.deltaTime / Mathf.Max(0.01f, segundosCambio));
            if (CicloDia.Instancia != null) CicloDia.Instancia.nublado = _intensidad;
            if (_camara == null && Camera.main != null) _camara = Camera.main.transform;
            bool bajoAgua = false;
            if (_camara != null)
            {
                var c = _camara.position;
                transform.position = c + Vector3.up * 12f;
                float agua = Mapa.Agua(c.x, c.z);
                bajoAgua = !float.IsNaN(agua) && c.y < agua;
            }
            if (_gotas != null)
            {
                // Bajo el agua no llueve (las gotas que ya caen se acaban solas).
                var emision = _gotas.emission;
                emision.rateOverTime = bajoAgua ? 0f : gotasTormenta * _intensidad;
            }
        }

        void CrearGotas()
        {
            var go = new GameObject("Gotas");
            go.transform.SetParent(transform, false);
            _gotas = go.AddComponent<ParticleSystem>();
            _gotas.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var principal = _gotas.main;
            principal.loop = true;
            principal.startLifetime = 1.6f;
            principal.startSpeed = 0f;
            principal.startSize = 0.025f;
            principal.startColor = new Color(0.78f, 0.82f, 0.88f, 0.35f);
            principal.gravityModifier = 0f;
            principal.maxParticles = 12000;
            principal.simulationSpace = ParticleSystemSimulationSpace.World;
            var forma = _gotas.shape;
            forma.shapeType = ParticleSystemShapeType.Box;
            forma.scale = new Vector3(lado, 1f, lado);
            var velocidad = _gotas.velocityOverLifetime;
            velocidad.enabled = true;
            velocidad.space = ParticleSystemSimulationSpace.World;
            velocidad.x = new ParticleSystem.MinMaxCurve(-0.6f, 0.6f);
            velocidad.y = new ParticleSystem.MinMaxCurve(-13f, -10f);
            velocidad.z = new ParticleSystem.MinMaxCurve(-0.6f, 0.6f);
            var emision = _gotas.emission;
            emision.rateOverTime = 0f;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Stretch;
            r.velocityScale = 0.045f;
            r.lengthScale = 1f;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.sharedMaterial = material != null ? material : Hecho();
            _gotas.Play();
        }

        /// <summary>
        /// Una gota: un trazo blanco con los bordes difuminados, iluminado por el sol y el cielo. El
        /// sombreador sale del material de la niebla del agua (asi va en el build) o, si no, de buscarlo.
        /// </summary>
        static Material Hecho()
        {
            if (_hecho != null) return _hecho;
            Shader sombreador = null;
            var agua = FindAnyObjectByType<AguaMundo>();
            if (agua != null && agua.materialNiebla != null) sombreador = agua.materialNiebla.shader;
            if (sombreador == null) sombreador = Shader.Find("Guardianes/Particula");
            if (sombreador == null) return null;
            var tex = new Texture2D(8, 32, TextureFormat.RGBA32, false) { name = "Gota", wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < 32; y++)
                for (int x = 0; x < 8; x++)
                {
                    float u = Mathf.Abs(x - 3.5f) / 3.5f, v = Mathf.Abs(y - 15.5f) / 15.5f;
                    float a = Mathf.Clamp01(1f - u * u) * Mathf.Clamp01(1f - v * v);
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
            tex.Apply();
            _hecho = new Material(sombreador) { name = "Lluvia" };
            _hecho.SetTexture("_MainTex", tex);
            _hecho.SetColor("_Color", Color.white);
            _hecho.SetFloat("_Emisiva", 0f);
            _hecho.SetFloat("_Suavidad", 0.2f);
            return _hecho;
        }
    }
}
