using System;
using Guardianes.Mundo;
using Guardianes.Vegetacion;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;

namespace Guardianes.Camara
{
    /// <summary>
    /// Un plano de una cinematica: cuanto dura, por donde va la camara, a que mira y lo que pasa o se dice en el. Las posiciones
    /// se calculan al empezar el plano o en cada fotograma, porque dependen de lo que este pasando en el mundo. Cada juego
    /// escribe los suyos; quien los rueda es DirectorMisiones.Rodar.
    /// </summary>
    public class Plano
    {
        public float segundos = 5f, fovInicio = 40f, fovFin = 40f;
        /// <summary>Donde esta la camara y a que mira, de 0 a 1 a lo largo del plano.</summary>
        public Func<float, Vector3> posicion, mira;
        /// <summary>Lo que cuenta el narrador y lo que dicen los personajes, como subtitulos.</summary>
        public string[] dice = new string[0];
        /// <summary>Lo que pasa al empezar (mover a alguien, un clip, encender algo). La pantalla ya esta en este plano.</summary>
        public Action alEmpezar;
        /// <summary>Se funde a negro al final de este plano.</summary>
        public bool aNegro;
        internal Action preparar;

        public Plano Dice(params string[] lineas) { dice = lineas; return this; }
        public Plano Al(Action accion) { alEmpezar = accion; return this; }
        public Plano Fov(float inicio, float fin) { fovInicio = inicio; fovFin = fin; return this; }
        public Plano Fov(float fov) => Fov(fov, fov);
        public Plano Negro() { aNegro = true; return this; }

        /// <summary>La camara quieta en un sitio, mirando a algo que puede moverse.</summary>
        public static Plano Fijo(float segundos, Func<Vector3> donde, Func<Vector3> mira)
        {
            var p0 = Vector3.zero;
            return new Plano { segundos = segundos, preparar = () => p0 = donde(), posicion = _ => p0, mira = _ => mira() };
        }

        /// <summary>De un sitio a otro, los dos calculados al empezar.</summary>
        public static Plano Viaje(float segundos, Func<Vector3> de, Func<Vector3> a, Func<Vector3> mira)
        {
            Vector3 p0 = Vector3.zero, p1 = Vector3.zero;
            return new Plano
            {
                segundos = segundos,
                preparar = () => { p0 = de(); p1 = a(); },
                posicion = t => Vector3.Lerp(p0, p1, t),
                mira = _ => mira(),
            };
        }

        /// <summary>Gira alrededor de algo (grados: 0, al norte) a una distancia y una altura.</summary>
        public static Plano Orbita(float segundos, Func<Vector3> centro, float gradosDe, float gradosA, float distancia, float alto, float altoMira = 0f)
        {
            return new Plano
            {
                segundos = segundos,
                posicion = t => centro() + Quaternion.Euler(0f, Mathf.Lerp(gradosDe, gradosA, t), 0f) * Vector3.forward * distancia + Vector3.up * alto,
                mira = _ => centro() + Vector3.up * altoMira,
            };
        }

        internal void Preparar() => preparar?.Invoke();
    }

    /// <summary>
    /// La camara de las cinematicas: mientras hay un plano, CamaraCriatura se aparta y esta mueve la camara por el plano, sin
    /// bajar del suelo ni meterse en lo solido. Se pone sola en la camara principal la primera vez.
    /// </summary>
    [DefaultExecutionOrder(200)]
    public class CamaraCine : MonoBehaviour
    {
        static readonly int IdVision = Shader.PropertyToID("_GD_Vision"), IdVisionFin = Shader.PropertyToID("_GD_VisionFin");

        Plano _plano;
        float _desde;
        Camera _cam;
        CamaraCriatura _juego;
        float _fovJuego;

        public static CamaraCine Asegurar()
        {
            var cam = Camera.main;
            if (cam == null) return null;
            return cam.TryGetComponent(out CamaraCine c) ? c : cam.gameObject.AddComponent<CamaraCine>();
        }

        void Awake()
        {
            _cam = GetComponent<Camera>();
            _juego = GetComponent<CamaraCriatura>();
        }

        /// <summary>Corta a este plano.</summary>
        public void Poner(Plano p)
        {
            if (_plano == null)
            {
                _fovJuego = _cam.fieldOfView;
                if (_juego != null) _juego.enabled = false;
            }
            p.Preparar();
            _plano = p;
            _desde = Time.time;
            Colocar(0f);
            // Un corte: sin esto, el primer fotograma saca los vectores de movimiento del salto.
            HDCamera.GetOrCreate(_cam).Reset();
        }

        /// <summary>Se acaban los planos: la camara vuelve detras de la criatura.</summary>
        public void Soltar()
        {
            if (_plano == null) return;
            _plano = null;
            _cam.fieldOfView = _fovJuego;
            if (_juego == null) return;
            _juego.enabled = true;
            _juego.Recolocar();
        }

        void LateUpdate()
        {
            if (_plano != null) Colocar(Mathf.Clamp01((Time.time - _desde) / Mathf.Max(0.01f, _plano.segundos)));
        }

        void Colocar(float t)
        {
            // Sin arrancar ni parar de golpe.
            float s = t * t * (3f - 2f * t);
            Vector3 p = _plano.posicion(s), m = _plano.mira(s);
            float suelo = Mapa.Superficie(p.x, p.z);
            p.y = Mathf.Max(p.y, suelo + 0.35f);
            var solidos = RenderizadorVegetacion.Instancia != null ? RenderizadorVegetacion.Instancia.Solidos : null;
            if (solidos != null) p = solidos.Apartar(p, 0.3f);
            var dir = m - p;
            transform.SetPositionAndRotation(p, dir.sqrMagnitude > 1e-4f ? Quaternion.LookRotation(dir, Vector3.up) : transform.rotation);
            _cam.fieldOfView = Mathf.Lerp(_plano.fovInicio, _plano.fovFin, s);
            _cam.nearClipPlane = 0.08f;
            // El follaje entre la camara y lo que se mira se disuelve, como en el juego.
            Shader.SetGlobalVector(IdVision, new Vector4(p.x, p.y, p.z, 1.6f));
            Shader.SetGlobalVector(IdVisionFin, new Vector4(m.x, m.y, m.z, 1f));
        }
    }
}
