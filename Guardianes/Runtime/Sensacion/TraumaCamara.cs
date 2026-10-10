using System.Collections.Generic;
using UnityEngine;

namespace Guardianes.Sensacion
{
    /// <summary>
    /// Sacudida de cámara por trauma (Squirrel Eiserloh, GDC 2016): los golpes y temblores suman
    /// trauma (0..1), que cae solo con el tiempo, y la cámara tiembla con trauma² por ruido de Perlin
    /// (suave y continuo, nunca saltos al azar). Se pone en cualquier cámara (la de la criatura, la de
    /// vuelo, la de buceo...) y se ejecuta después de su controlador: tiembla encima de lo que este
    /// haya puesto y lo quita en el fotograma siguiente si el controlador no ha movido la cámara.
    /// Los valores salen de Sensaciones.Perfil y Sensaciones.Opciones.sacudida.
    /// </summary>
    [DefaultExecutionOrder(10000)]
    public class TraumaCamara : MonoBehaviour
    {
        public static readonly List<TraumaCamara> Todas = new List<TraumaCamara>();

        [Range(0f, 1f)] public float trauma;
        [Tooltip("Multiplicador propio de esta cámara (0 = no tiembla: cámaras de cinemática, mapas)")]
        public float intensidad = 1f;
        [Tooltip("El desplazamiento escala con el tamaño de la criatura (la rotación no)")]
        public bool escalarConCriatura = true;

        float _semilla;
        Vector3 _posPuesta, _posAplicada;
        Quaternion _rotPuesta, _rotAplicada;
        bool _aplicado;

        /// <summary>Lo que tiembla ahora (trauma²·intensidad·opciones), 0..1.</summary>
        public float Sacudida => trauma * trauma * Mathf.Max(0f, intensidad) * Mathf.Clamp01(Sensaciones.Opciones.sacudida);

        public void Sumar(float t) => trauma = Mathf.Clamp01(trauma + Mathf.Max(0f, t));

        /// <summary>Suma trauma a todas las cámaras (sin distancia).</summary>
        public static void SumarTodas(float t) { foreach (var c in Todas) if (c != null) c.Sumar(t); }

        void OnEnable() { Todas.Add(this); _semilla = Random.value * 100f; _aplicado = false; }

        void OnDisable()
        {
            Todas.Remove(this);
            Quitar();
        }

        void Quitar()
        {
            // Solo si nadie ha movido la cámara desde la sacudida: si el controlador la ha puesto, ya está limpia.
            if (_aplicado && transform.localPosition == _posAplicada && transform.localRotation == _rotAplicada)
            {
                transform.localPosition = _posPuesta;
                transform.localRotation = _rotPuesta;
            }
            _aplicado = false;
        }

        void LateUpdate()
        {
            Quitar();
            var p = Sensaciones.Perfil;
            trauma = Mathf.Max(0f, trauma - p.traumaCaida * Time.unscaledDeltaTime);
            float s = Sacudida;
            if (s <= 1e-4f) return;

            float t = Time.unscaledTime * p.sacudidaFrecuencia;
            Vector3 giro = new Vector3(
                p.sacudidaGrados.x * Ruido(t, 0),
                p.sacudidaGrados.y * Ruido(t, 1),
                p.sacudidaGrados.z * Ruido(t, 2)) * s;
            float metros = p.sacudidaMetros * s * (escalarConCriatura ? Sensaciones.EscalaCriatura : 1f);
            Vector3 mover = new Vector3(Ruido(t, 3), Ruido(t, 4), Ruido(t, 5) * 0.5f) * metros;

            _posPuesta = transform.localPosition;
            _rotPuesta = transform.localRotation;
            transform.localRotation = _rotPuesta * Quaternion.Euler(giro);
            transform.localPosition = _posPuesta + _rotPuesta * mover;
            _posAplicada = transform.localPosition;
            _rotAplicada = transform.localRotation;
            _aplicado = true;
        }

        /// <summary>Ruido de Perlin centrado (-1..1) en un canal.</summary>
        float Ruido(float t, int canal) => Mathf.PerlinNoise(_semilla + canal * 31.7f, t) * 2f - 1f;
    }
}
