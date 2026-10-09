using System.Collections.Generic;
using UnityEngine;

namespace Guardianes.Mundo
{
    /// <summary>
    /// Una luz que cuenta para el juego (no solo para la imagen): hogueras, linternas, focos,
    /// generadores, el farol de una cocina. Lo que alumbra se ve de noche (Humano.Luz la suma a la
    /// del cielo) y la criatura puede apagarla (romper el generador, tumbar la linterna).
    /// Con 'cono' > 0 solo alumbra hacia delante (linterna, foco). Si tiene una Light, la enciende,
    /// la apaga y la hace parpadear (fuego).
    /// </summary>
    public class FuenteLuz : MonoBehaviour
    {
        public static readonly List<FuenteLuz> Todas = new List<FuenteLuz>();
        /// <summary>Se apaga (donde): los humanos de alrededor se asustan.</summary>
        public static event System.Action<FuenteLuz> AlApagarse;

        [Tooltip("Hasta donde alumbra lo bastante para ver (m)")]
        public float alcance = 12f;
        [Tooltip("Cuanta luz da en el centro (0..1, 1 = como de dia)")]
        [Range(0f, 1.5f)] public float intensidad = 1f;
        [Tooltip("Medio angulo del haz (grados); 0 = alumbra en todas direcciones")]
        [Range(0f, 90f)] public float cono;
        [Tooltip("Parpadeo de la llama (0 = luz electrica)")]
        [Range(0f, 0.5f)] public float parpadeo;
        public Light luz;
        public bool encendida = true;

        float _base, _fase;

        public bool Encendida => encendida && isActiveAndEnabled;

        void Awake()
        {
            if (luz == null) luz = GetComponentInChildren<Light>();
            if (luz != null) _base = luz.intensity;
            _fase = Random.Range(0f, 100f);
        }

        void OnEnable() => Todas.Add(this);
        void OnDisable() => Todas.Remove(this);

        public void Apagar()
        {
            if (!encendida) return;
            encendida = false;
            if (luz != null) luz.enabled = false;
            AlApagarse?.Invoke(this);
        }

        public void Encender()
        {
            encendida = true;
            if (luz != null) luz.enabled = true;
        }

        void Update()
        {
            if (luz == null || !encendida || parpadeo <= 0f) return;
            // Llama: dos ruidos lentos y uno rapido.
            float t = Time.time + _fase;
            float n = Mathf.PerlinNoise(t * 2.1f, 0.3f) * 0.6f + Mathf.PerlinNoise(t * 7.3f, 1.7f) * 0.4f;
            luz.intensity = _base * (1f - parpadeo + parpadeo * 2f * n);
        }

        /// <summary>Lo que esta fuente alumbra el punto p (0..intensidad).</summary>
        public float Aporte(Vector3 p)
        {
            if (!Encendida) return 0f;
            Vector3 d = p - transform.position;
            float dist = d.magnitude;
            if (dist >= alcance) return 0f;
            float f = 1f - dist / alcance;
            f *= f;
            if (cono > 0f && dist > 0.2f)
            {
                float ang = Vector3.Angle(transform.forward, d);
                if (ang > cono) return 0f;
                f *= Mathf.SmoothStep(0f, 1f, (cono - ang) / Mathf.Max(1f, cono * 0.35f));
            }
            return f * intensidad;
        }

        /// <summary>La luz de todas las fuentes en p (sin la del cielo).</summary>
        public static float Total(Vector3 p)
        {
            float s = 0f;
            for (int i = 0; i < Todas.Count; i++) s += Todas[i].Aporte(p);
            return s;
        }
    }
}
