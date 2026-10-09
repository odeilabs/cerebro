using System.Collections.Generic;
using UnityEngine;

namespace Guardianes.Combate
{
    /// <summary>
    /// Algo que Kong puede agarrar y lanzar (rocas, troncos, vehiculos, bidones, humanos). Mientras
    /// lo lleva es cinematico; al lanzarlo vuela con fisica y al chocar hace un impacto en area
    /// proporcional a su masa y a la velocidad.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class Lanzable : MonoBehaviour
    {
        [Tooltip("Dano del impacto a 40 m/s")]
        public float danio = 160f;
        public float radioImpacto = 9f;
        [Tooltip("Se rompe al chocar (rocas, troncos podridos): desaparece tras el impacto")]
        public bool seRompe;
        public bool explota;
        [Tooltip("Vuela por su cuenta (helicopteros): Kong lo atrapa al vuelo si pasa a su alcance y le apunta al lanzar")]
        public bool volador;
        [Tooltip("Por donde lo agarra Kong (local); en un helicoptero, el botalon de cola")]
        public Vector3 puntoAgarre;

        public Rigidbody Cuerpo { get; private set; }
        public bool Agarrado { get; private set; }
        public bool EnVuelo { get; private set; }
        public GameObject Lanzador { get; private set; }
        /// <summary>Un volador en el aire y entero (blanco para lo que lanza Kong). Lo pone quien lo pilota.</summary>
        public bool Vuela { get; set; }
        /// <summary>Un volador lo bastante bajo para atraparlo. Lo pone quien lo pilota.</summary>
        public bool Atrapable { get; set; }
        /// <summary>Velocidad de un volador (los cinematicos no la tienen en el Rigidbody).</summary>
        public Vector3 VelocidadVuelo { get; set; }
        public Vector3 PuntoAgarreMundo => transform.TransformPoint(puntoAgarre);

        public event System.Action<Lanzable, Collision> AlChocar;
        public static readonly List<Lanzable> Voladores = new List<Lanzable>();

        float _vueloDesde;

        void Awake() => Cuerpo = GetComponent<Rigidbody>();

        void OnEnable()
        {
            if (volador) Voladores.Add(this);
        }

        void OnDisable() => Voladores.Remove(this);

        public void Agarrar(Transform mano)
        {
            Agarrado = true;
            EnVuelo = false;
            Cuerpo.isKinematic = true;
            Cuerpo.interpolation = RigidbodyInterpolation.None;
            transform.SetParent(mano, true);
            foreach (var c in GetComponentsInChildren<Collider>()) c.enabled = false;
        }

        /// <summary>Lo deja donde esta, sin lanzarlo (sigue cinematico: quien lo tenga lo coloca).</summary>
        public void Soltar()
        {
            Agarrado = false;
            EnVuelo = false;
            transform.SetParent(null, true);
            foreach (var c in GetComponentsInChildren<Collider>()) c.enabled = true;
        }

        public void Lanzar(Vector3 velocidad, Vector3 giro, GameObject lanzador)
        {
            Agarrado = false;
            EnVuelo = true;
            Lanzador = lanzador;
            transform.SetParent(null, true);
            foreach (var c in GetComponentsInChildren<Collider>()) c.enabled = true;
            Cuerpo.isKinematic = false;
            Cuerpo.interpolation = RigidbodyInterpolation.Interpolate;
            Cuerpo.linearVelocity = velocidad;
            Cuerpo.angularVelocity = giro;
            Cuerpo.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            _vueloDesde = Time.time;
        }

        void OnCollisionEnter(Collision c)
        {
            if (!EnVuelo || Time.time - _vueloDesde < 0.08f) return;
            if (Lanzador != null && c.transform.IsChildOf(Lanzador.transform)) return;
            float v = c.relativeVelocity.magnitude;
            if (v < 6f) { EnVuelo = false; return; }
            float k = Mathf.Clamp(v / 40f, 0.3f, 1.6f);
            Impacto.Golpear(c.GetContact(0).point, radioImpacto * Mathf.Sqrt(k), danio * k, 22f * k,
                explota ? TipoDanio.Explosion : TipoDanio.Lanzado, Lanzador);
            AlChocar?.Invoke(this, c);
            EnVuelo = false;
            Cuerpo.collisionDetectionMode = CollisionDetectionMode.Discrete;
            if (seRompe) Destroy(gameObject, 0.05f);
        }
    }
}
