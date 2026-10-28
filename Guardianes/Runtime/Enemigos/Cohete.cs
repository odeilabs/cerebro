using System.Collections.Generic;
using Guardianes.Combate;
using Guardianes.Mundo;
using UnityEngine;

namespace Guardianes.Enemigos
{
    /// <summary>
    /// Cohete de 70 mm de los helicopteros. Sale del tubo, el motor lo lleva a ~80 m/s y corrige un
    /// poco hacia el blanco (una salva amenaza a la criatura sin ser infalible). Avanza con un rayo por paso
    /// (sin colision fisica) y revienta al tocar algo, el suelo o el agua, o al acabarse el motor.
    /// La estela la pinta EfectosCombate mirando Todos.
    /// </summary>
    public class Cohete : MonoBehaviour
    {
        public float velocidad = 80f;
        public float aceleracion = 60f;
        [Tooltip("Correccion de rumbo hacia el blanco (grados/s)")]
        public float giro = 6f;
        public float danio = 40f;
        public float radio = 8f;
        public float vidaMaxima = 6f;

        public static readonly List<Cohete> Todos = new List<Cohete>();
        /// <summary>Sale un cohete (posicion, direccion).</summary>
        public static event System.Action<Vector3, Vector3> AlLanzar;
        /// <summary>Revienta (centro, radio, contra el suelo).</summary>
        public static event System.Action<Vector3, float, bool> AlExplotar;

        public Vector3 Velocidad => _vel;
        /// <summary>Donde estaba el fotograma anterior (la estela rellena el tramo).</summary>
        public Vector3 PosicionAnterior { get; private set; }

        GameObject _origen;
        Transform _objetivo;
        Vector3 _desplazamiento, _vel;
        float _vida;

        void OnEnable() => Todos.Add(this);
        void OnDisable() => Todos.Remove(this);

        /// <param name="origen">Quien dispara (no se dana con su propio cohete).</param>
        /// <param name="objetivo">Hacia quien corrige (puede ser null).</param>
        /// <param name="desplazamiento">Punto del blanco respecto a su origen (pecho, cabeza...).</param>
        public void Lanzar(GameObject origen, Transform objetivo, Vector3 desplazamiento, Vector3 velocidadInicial)
        {
            _origen = origen;
            _objetivo = objetivo;
            _desplazamiento = desplazamiento;
            _vel = velocidadInicial;
            PosicionAnterior = transform.position;
            AlLanzar?.Invoke(transform.position, _vel.normalized);
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            _vida += dt;
            PosicionAnterior = transform.position;
            float v = _vel.magnitude;
            Vector3 dir = v > 0.1f ? _vel / v : transform.forward;
            if (_objetivo != null && _vida > 0.2f)
                dir = Vector3.RotateTowards(dir, (_objetivo.position + _desplazamiento - transform.position).normalized, giro * Mathf.Deg2Rad * dt, 0f);
            v = Mathf.MoveTowards(v, velocidad, aceleracion * dt);
            _vel = dir * v + Physics.gravity * (0.25f * dt);

            Vector3 paso = _vel * dt;
            // Por Balas: no se para en quien lo lanza y revienta tambien contra un blanco sin colisionadores.
            float largo = paso.magnitude;
            if (largo >= 1e-4f && Balas.Rayo(transform.position, paso / largo, largo, _origen, out var golpe))
            {
                Explotar(golpe.punto, golpe.collider is TerrainCollider);
                return;
            }
            transform.position += paso;
            if (_vel.sqrMagnitude > 1f) transform.rotation = Quaternion.LookRotation(_vel);
            Vector3 p = transform.position;
            float suelo = TerrenoMundo.Instancia != null ? Mapa.Altura(p.x, p.z) : float.MinValue;
            float agua = Mapa.Agua(p.x, p.z);
            if (p.y < suelo) Explotar(new Vector3(p.x, suelo, p.z), true);
            else if (!float.IsNaN(agua) && p.y < agua) Explotar(new Vector3(p.x, agua, p.z), true);
            else if (_vida > vidaMaxima) Explotar(p, false);
        }

        void Explotar(Vector3 punto, bool enSuelo)
        {
            Impacto.Golpear(punto, radio, danio, 14f, TipoDanio.Explosion, _origen);
            AlExplotar?.Invoke(punto, radio, enSuelo);
            Destroy(gameObject);
        }
    }
}
