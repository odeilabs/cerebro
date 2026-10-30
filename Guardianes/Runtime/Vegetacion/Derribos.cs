using System.Collections.Generic;
using Guardianes.Mundo;
using Unity.Mathematics;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Guardianes.Vegetacion
{
    /// <summary>
    /// Plantas derribadas: la instancia deja de dibujarse en el RenderizadorVegetacion y aparece un
    /// objeto con su malla que cae girando sobre la base (como un pendulo invertido que acelera),
    /// rebota y se queda tumbado. Los arbustos se aplastan. Hay un tope de restos: los mas viejos
    /// se hunden y desaparecen. Un resto quieto que deja de verse (fuera de camara o lejos) se quita.
    /// El hueco queda: la planta no vuelve sola; solo con Restaurar (cada juego decide con que: una
    /// habilidad de su criatura, una siembra).
    /// </summary>
    public class Derribos : MonoBehaviour
    {
        public int maximoRestos = 220;
        [Tooltip("Un resto quieto que lleva este tiempo sin verse (s) se quita")]
        public float ocultoParaQuitar = 3f;
        [Tooltip("Y estando al menos a esta distancia de la camara (m)")]
        public float distanciaParaQuitar = 60f;
        /// <summary>Una planta vuelve a crecer donde cayo (base, especie).</summary>
        public static event System.Action<Vector3, EspecieVegetal> AlRestaurar;
        /// <summary>Los huecos: las plantas derribadas (indice del renderizador) y donde estaban.</summary>
        static readonly Dictionary<int, Vector3> s_Huecos = new Dictionary<int, Vector3>();
        public static int Huecos => s_Huecos.Count;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reiniciar() => s_Huecos.Clear();
        /// <summary>Empieza a caer (base, especie, fuerza del golpe).</summary>
        public static event System.Action<Vector3, EspecieVegetal, float> AlDerribar;
        /// <summary>Un arbol caido golpea el suelo (base, punta, especie).</summary>
        public static event System.Action<Vector3, Vector3, EspecieVegetal> AlTocarSuelo;

        static Derribos s_Instancia;
        readonly List<Caido> _caidos = new List<Caido>();

        class Caido
        {
            public Transform t;
            public Vector3 pivote, eje;
            public Quaternion inicial;
            public float angulo, velocidad, maximo, edad, altura;
            public bool aplastar, quieto, toco;
            public float hundir, oculto;
            public EspecieVegetal especie;
            public int indice;
            public Renderer render;
        }

        static Derribos Instancia
        {
            get
            {
                if (s_Instancia == null)
                {
                    s_Instancia = FindAnyObjectByType<Derribos>();
                    if (s_Instancia == null) s_Instancia = new GameObject("Derribos").AddComponent<Derribos>();
                }
                return s_Instancia;
            }
        }

        /// <summary>Derriba la planta 'indice' del renderizador empujandola en 'direccion'.</summary>
        public static void Derribar(int indice, Vector3 direccion, float fuerza)
        {
            var veg = RenderizadorVegetacion.Instancia;
            if (veg == null || !veg.Viva(indice)) return;
            var esp = veg.EspecieDe(indice);
            var m = veg.MatrizDe(indice);
            float escala = veg.EscalaDe(indice);
            veg.Derribar(indice);
            s_Huecos[indice] = m.c3.xyz;
            Instancia.Crear(esp, veg.VarianteDe(indice), m, escala, direccion, fuerza, indice);
        }

        /// <summary>
        /// Las plantas derribadas a menos de 'radio' de 'centro' vuelven a crecer (y sus restos, si quedan, se van).
        /// Devuelve cuantas.
        /// </summary>
        public static int Restaurar(Vector3 centro, float radio)
        {
            var veg = RenderizadorVegetacion.Instancia;
            if (veg == null || s_Huecos.Count == 0) return 0;
            var vuelven = new List<int>();
            foreach (var h in s_Huecos)
            {
                float dx = h.Value.x - centro.x, dz = h.Value.z - centro.z;
                if (dx * dx + dz * dz <= radio * radio) vuelven.Add(h.Key);
            }
            foreach (int i in vuelven)
            {
                s_Huecos.Remove(i);
                veg.Restaurar(i);
                if (s_Instancia != null)
                    foreach (var c in s_Instancia._caidos)
                        if (c.indice == i && c.t != null) c.hundir = Mathf.Max(c.hundir, 4f);
                AlRestaurar?.Invoke(veg.MatrizDe(i).c3.xyz, veg.EspecieDe(i));
            }
            return vuelven.Count;
        }

        void Crear(EspecieVegetal esp, int variante, float4x4 m, float escala, Vector3 direccion, float fuerza, int indice)
        {
            var go = new GameObject(esp.name + "_caido");
            go.transform.SetParent(transform, false);
            Vector3 pos = m.c3.xyz;
            // La matriz lleva escala: la rotacion sale de los ejes normalizados.
            Quaternion rot = Quaternion.LookRotation(math.normalize(m.c2.xyz), math.normalize(m.c1.xyz));
            go.transform.SetPositionAndRotation(pos, rot);
            go.transform.localScale = Vector3.one * escala;
            go.AddComponent<MeshFilter>().sharedMesh = esp.Malla(variante, 0);
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterials = esp.Materiales;

            direccion.y = 0f;
            if (direccion.sqrMagnitude < 1e-4f) direccion = Random.insideUnitSphere;
            direccion.y = 0f;
            direccion.Normalize();
            bool pequeno = esp.tipo == TipoVegetal.Arbusto || esp.tipo == TipoVegetal.Suelo;
            float altura = esp.Limites.size.y * escala;
            _caidos.Add(new Caido
            {
                t = go.transform,
                pivote = pos,
                eje = Vector3.Cross(Vector3.up, direccion),
                inicial = rot,
                angulo = 0f,
                // Arranque segun el golpe; luego acelera como un pendulo invertido.
                velocidad = Mathf.Clamp(fuerza * 2.2f / Mathf.Max(altura, 1f), 0.15f, 3f) * Mathf.Rad2Deg,
                maximo = pequeno ? 70f : Random.Range(80f, 88f),
                altura = altura,
                aplastar = pequeno,
                especie = esp,
                indice = indice,
                render = mr,
            });
            AlDerribar?.Invoke(pos, esp, fuerza);
            // Tope de restos: los mas viejos empiezan a hundirse; si aun asi hay demasiados (un golpe
            // que tumba cientos de plantas), los mas viejos se quitan ya.
            int hundiendose = 0;
            foreach (var c in _caidos) if (c.hundir > 0f) hundiendose++;
            int sobran = _caidos.Count - hundiendose - maximoRestos;
            for (int i = 0; i < _caidos.Count && sobran > 0; i++)
                if (_caidos[i].hundir <= 0f) { _caidos[i].hundir = 0.001f; sobran--; }
            while (_caidos.Count > maximoRestos * 2)
            {
                if (_caidos[0].t != null) Destroy(_caidos[0].t.gameObject);
                _caidos.RemoveAt(0);
            }
        }

        void Update()
        {
            float dt = Time.deltaTime;
            var cam = Camera.main;
            foreach (var c in _caidos)
            {
                if (c.t == null) continue;
                c.edad += dt;
                // Quieto y sin verse (fuera de camara, o lejos), se quita: el hueco queda.
                if (c.quieto && c.hundir <= 0f && c.render != null && cam != null)
                {
                    bool lejos = (c.pivote - cam.transform.position).sqrMagnitude > distanciaParaQuitar * distanciaParaQuitar;
                    c.oculto = !c.render.isVisible && lejos ? c.oculto + dt : 0f;
                    if (c.oculto > ocultoParaQuitar) c.hundir = 7f;
                }
                if (!c.quieto)
                {
                    // Pendulo invertido: cuanto mas inclinado, mas acelera.
                    float g = 9.81f * 1.5f / Mathf.Max(c.altura, 1f) * Mathf.Rad2Deg;
                    c.velocidad += g * Mathf.Sin(Mathf.Max(c.angulo, 2f) * Mathf.Deg2Rad) * dt;
                    c.angulo += c.velocidad * dt;
                    if (c.angulo >= c.maximo)
                    {
                        c.angulo = c.maximo;
                        if (!c.toco && !c.aplastar)
                        {
                            c.toco = true;
                            c.t.rotation = Quaternion.AngleAxis(c.angulo, c.eje) * c.inicial;
                            AlTocarSuelo?.Invoke(c.pivote, c.pivote + c.t.up * c.altura, c.especie);
                        }
                        // Rebote amortiguado contra el suelo.
                        c.velocidad = -c.velocidad * 0.18f;
                        if (Mathf.Abs(c.velocidad) < 4f) { c.velocidad = 0f; c.quieto = true; }
                    }
                    c.t.rotation = Quaternion.AngleAxis(c.angulo, c.eje) * c.inicial;
                    if (c.aplastar)
                        c.t.localScale = new Vector3(c.t.localScale.x, Mathf.Lerp(c.t.localScale.y, c.t.localScale.x * 0.45f, dt * 3f), c.t.localScale.z);
                }
                if (c.hundir > 0f)
                {
                    c.hundir += dt;
                    c.t.position = c.pivote + Vector3.down * c.hundir * 1.5f;
                }
            }
            Limpiar();
        }

        void Limpiar()
        {
            for (int i = _caidos.Count - 1; i >= 0; i--)
            {
                var c = _caidos[i];
                if (c.t == null || c.hundir > 6f)
                {
                    if (c.t != null) Destroy(c.t.gameObject);
                    _caidos.RemoveAt(i);
                }
            }
        }
    }
}
