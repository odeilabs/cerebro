using Guardianes.Combate;
using UnityEngine;

namespace Guardianes.Enemigos
{
    /// <summary>
    /// Instalacion destructible (barracon, deposito, balancin de petroleo, excavadora...). Esta hecha
    /// de piezas (cada una con su malla); al romperse, cada pieza se suelta con fisica y sale
    /// despedida desde el punto del golpe. Las explosivas detonan (dano en area que encadena otras
    /// explosiones) y avisan para el fuego y el humo. Las piezas sueltas se pueden agarrar y lanzar.
    /// </summary>
    [RequireComponent(typeof(Salud))]
    public class Estructura : MonoBehaviour
    {
        public bool explosiva;
        public float radioExplosion = 18f;
        public float danioExplosion = 150f;
        [Tooltip("Densidad para la masa de las piezas (kg/m3 de su caja)")]
        public float densidad = 180f;
        public float vidaRestos = 60f;
        /// <summary>Se la traga una criatura: al romperse no suelta piezas ni explota, desaparece entera.</summary>
        [System.NonSerialized] public bool engullida;

        /// <summary>Se rompe (posicion, radio aproximado). Lo usan los efectos y el sonido.</summary>
        public static event System.Action<Estructura, Vector3, float> AlRomperse;
        public static event System.Action<Vector3, float> AlExplotar;

        Salud _salud;
        bool _rota;

        void Awake()
        {
            _salud = GetComponent<Salud>();
            _salud.AlMorir += Romper;
        }

        public Bounds Limites()
        {
            var b = new Bounds(transform.position, Vector3.zero);
            bool primero = true;
            foreach (var r in GetComponentsInChildren<Renderer>())
            {
                if (primero) { b = r.bounds; primero = false; }
                else b.Encapsulate(r.bounds);
            }
            return b;
        }

        void Romper(DatosDanio d)
        {
            if (_rota) return;
            _rota = true;
            if (engullida)
            {
                AlRomperse?.Invoke(this, transform.position, 1f);
                Destroy(gameObject);
                return;
            }
            var limites = Limites();
            Vector3 centro = limites.center;
            Vector3 origen = d.punto == Vector3.zero ? centro - Vector3.up : d.punto;
            float fuerza = Mathf.Max(8f, d.empuje) + (explosiva ? 18f : 0f);
            // Las maquinas dejan de moverse (balancines, antenas).
            foreach (var c in GetComponentsInChildren<MonoBehaviour>())
                if (c != this && !(c is Salud)) Destroy(c);

            foreach (var mf in GetComponentsInChildren<MeshFilter>())
            {
                var t = mf.transform;
                if (t == transform) continue;
                t.SetParent(null, true);
                var b = mf.sharedMesh != null ? mf.sharedMesh.bounds : new Bounds(Vector3.zero, Vector3.one);
                Vector3 tam = Vector3.Scale(b.size, t.lossyScale);
                var col = t.gameObject.AddComponent<BoxCollider>();
                col.center = b.center;
                col.size = b.size;
                var rb = t.gameObject.AddComponent<Rigidbody>();
                rb.mass = Mathf.Clamp(tam.x * tam.y * tam.z * densidad, 20f, 60000f);
                rb.linearDamping = 0.05f;
                rb.angularDamping = 0.2f;
                rb.interpolation = RigidbodyInterpolation.Interpolate;
                Vector3 dir = (t.position - origen);
                dir = (dir.sqrMagnitude > 0.01f ? dir.normalized : Random.onUnitSphere) + Vector3.up * 0.6f + d.direccion * 0.8f;
                rb.linearVelocity = dir.normalized * fuerza * Random.Range(0.5f, 1.1f);
                rb.angularVelocity = Random.insideUnitSphere * 4f;
                // Las piezas grandes se pueden agarrar y lanzar (chapas, troncos, depositos...).
                if (rb.mass > 300f)
                {
                    var l = t.gameObject.AddComponent<Lanzable>();
                    l.danio = Mathf.Clamp(rb.mass / 40f, 40f, 200f);
                    l.radioImpacto = Mathf.Clamp(tam.magnitude * 0.6f, 4f, 12f);
                }
                Destroy(t.gameObject, vidaRestos * Random.Range(0.8f, 1.3f));
            }

            AlRomperse?.Invoke(this, centro, limites.extents.magnitude);
            if (explosiva)
            {
                AlExplotar?.Invoke(centro, radioExplosion);
                // Otras explosiones en cadena: el dano llega en el siguiente fotograma (evita recursion).
                StartCoroutineDetonar(centro);
            }
            else Destroy(gameObject);
        }

        void StartCoroutineDetonar(Vector3 centro)
        {
            var go = new GameObject("Explosion");
            go.transform.position = centro;
            go.AddComponent<Detonacion>().Configurar(radioExplosion, danioExplosion);
            Destroy(gameObject);
        }

        class Detonacion : MonoBehaviour
        {
            float _radio, _danio;
            public void Configurar(float radio, float danio) { _radio = radio; _danio = danio; }
            void Start()
            {
                Impacto.Golpear(transform.position, _radio, _danio, 30f, TipoDanio.Explosion, gameObject, 0.7f);
                Destroy(gameObject, 0.1f);
            }
        }
    }
}
