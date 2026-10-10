using System;
using System.Collections;
using System.Collections.Generic;
using Guardianes.Combate;
using Guardianes.Criaturas;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Guardianes.Sensacion
{
    /// <summary>Cómo se para la acción en un golpe.</summary>
    public enum ModoParada
    {
        /// <summary>Solo se paran el atacante y el golpeado (sus Animator, y lo que pregunte EstaParado).</summary>
        Local,
        /// <summary>Se para todo el juego (Time.timeScale): remates y jefes.</summary>
        Global,
        /// <summary>Sin parada (golpes continuos, ataques en área con muchos blancos).</summary>
        Ninguna,
    }

    /// <summary>
    /// Un impacto que el jugador tiene que notar. 'fuerza' es lo importante que es (0 roce, 1 golpe
    /// fuerte, más de 1 remate o jefe): lo decide cada juego según sus mecánicas (el daño frente a la
    /// vida, el tamaño de lo golpeado, si era una emboscada...). Desde() lo calcula a partir del daño.
    /// </summary>
    public struct Golpe
    {
        public float fuerza;
        public Vector3 punto;
        public Vector3 direccion;
        public GameObject atacante;
        public GameObject golpeado;
        /// <summary>Lo que tiembla del golpeado durante la parada (su modelo, no la raíz con física).</summary>
        public Transform visualGolpeado;
        public ModoParada parada;
        /// <summary>Etiqueta libre del juego ("mordisco", "palmada"...) para sus sonidos y efectos.</summary>
        public string tipo;

        /// <summary>Un golpe con la fuerza sacada del daño: la fracción de la vida que quita (con 'fuerte' = la que cuenta como 1).</summary>
        public static Golpe Desde(DatosDanio d, Salud golpeado, float fuerte = 0.35f, string tipo = null)
        {
            float fraccion = golpeado != null && golpeado.maxima > 0f ? d.cantidad / golpeado.maxima : 0.5f;
            return new Golpe
            {
                fuerza = Mathf.Clamp(fraccion / Mathf.Max(0.01f, fuerte), 0f, 2f),
                punto = d.punto,
                direccion = d.direccion,
                atacante = d.origen,
                golpeado = golpeado != null ? golpeado.gameObject : null,
                tipo = tipo,
            };
        }
    }

    /// <summary>
    /// La sensación común de la saga: una llamada por impacto (Impacto) y el cerebro hace la parada
    /// (hitstop), el trauma de las cámaras (TraumaCamara), el destello del golpeado y la vibración del
    /// mando según el perfil activo y la escala de la criatura; los sonidos y partículas propios los
    /// pone cada juego escuchando AlImpacto. Temblor() para lo que no es un golpe (pisadas de un
    /// gigante, explosiones). Nada de esto ocurre si un juego no lo llama: es opcional y por juego.
    /// </summary>
    public static class Sensaciones
    {
        /// <summary>Opciones del jugador (accesibilidad): se aplican encima del perfil.</summary>
        public static class Opciones
        {
            /// <summary>0..1: cuánto tiembla la cámara.</summary>
            public static float sacudida = 1f;
            public static bool paradas = true;
            public static bool vibracion = true;
            public static bool destellos = true;
        }

        static PerfilSensacion _perfil;
        /// <summary>El perfil del juego (si nadie lo pone, uno Creciente de serie).</summary>
        public static PerfilSensacion Perfil
        {
            get { if (_perfil == null) _perfil = PerfilSensacion.Crear(ArquetipoSensacion.Creciente); return _perfil; }
            set => _perfil = value;
        }

        /// <summary>La escala de tiempo del juego sin paradas (los juegos con cámara lenta propia la dan aquí).</summary>
        public static Func<float> EscalaTiempoBase = () => 1f;

        /// <summary>Cada impacto (para los sonidos, partículas y efectos propios del juego).</summary>
        public static event Action<Golpe> AlImpacto;
        /// <summary>Empieza una parada (golpe, segundos): para lo que el juego anime a mano.</summary>
        public static event Action<Golpe, float> AlParar;

        static readonly Dictionary<GameObject, float> _parados = new Dictionary<GameObject, float>();

        /// <summary>
        /// Este objeto está en la parada de un golpe: las animaciones hechas a mano (la espina de una
        /// serpiente, el IK de un gorila) tienen que quedarse quietas mientras tanto.
        /// </summary>
        public static bool EstaParado(GameObject go) =>
            go != null && _parados.TryGetValue(go, out var hasta) && Time.unscaledTime < hasta;

        /// <summary>Escala de los efectos con el tamaño de la criatura del jugador (Kong = 1).</summary>
        public static float EscalaCriatura => Criatura.Jugador != null ? Mathf.Max(0.02f, Criatura.Jugador.Escala) : 1f;

        // ------------------------------------------------------------------ Impactos
        /// <summary>Un impacto: parada, cámara, destello y vibración según su fuerza.</summary>
        public static void Impacto(Golpe g)
        {
            var p = Perfil;
            float f = Mathf.Max(0f, g.fuerza);
            Temblor(g.punto, f * p.traumaPorFuerza);
            float parada = g.parada == ModoParada.Ninguna || !Opciones.paradas ? 0f : p.Parada(f);
            if (parada > 0f) Ejecutor.Instancia.Parar(g, parada);
            if (p.destello && Opciones.destellos && g.golpeado != null)
                Ejecutor.Instancia.Destellar(g.golpeado, p.destelloNits * Mathf.Clamp01(f), p.destelloDuracion + parada);
            Vibrar(p.vibracion * Mathf.Clamp01(f), p.vibracionDuracion + parada * 0.5f);
            Medidor.Impacto(f);
            AlImpacto?.Invoke(g);
        }

        /// <summary>
        /// Trauma para las cámaras por algo que pasa en 'punto' (una pisada, una explosión): menos
        /// cuanto más lejos, medido en tamaños de la criatura del jugador.
        /// </summary>
        public static void Temblor(Vector3 punto, float trauma)
        {
            if (trauma <= 0f) return;
            var p = Perfil;
            float mitad = Mathf.Max(0.5f, p.traumaMitadA * Mathf.Max(1f, (Criatura.Jugador != null ? Criatura.Jugador.Tamano : 17.5f)));
            foreach (var c in TraumaCamara.Todas)
            {
                if (c == null) continue;
                float d = Vector3.Distance(c.transform.position, punto);
                c.Sumar(trauma / (1f + (d / mitad) * (d / mitad)));
            }
        }

        /// <summary>Vibración del mando (grave, agudo) durante unos segundos.</summary>
        public static void Vibrar(Vector2 motores, float segundos)
        {
            if (!Opciones.vibracion || Gamepad.current == null || segundos <= 0f || motores.sqrMagnitude < 1e-4f) return;
            Ejecutor.Instancia.Vibrar(motores, segundos);
        }

        // ------------------------------------------------------------------ Lo que hace falta en el tiempo
        /// <summary>Corrutinas de la sensación (objeto oculto que se crea solo).</summary>
        sealed class Ejecutor : MonoBehaviour
        {
            static Ejecutor _instancia;
            public static Ejecutor Instancia
            {
                get
                {
                    if (_instancia == null)
                    {
                        var go = new GameObject("Sensacion") { hideFlags = HideFlags.HideAndDontSave };
                        if (Application.isPlaying) DontDestroyOnLoad(go);
                        _instancia = go.AddComponent<Ejecutor>();
                    }
                    return _instancia;
                }
            }

            float _globalHasta;
            Coroutine _global, _vibracion;

            public void Parar(Golpe g, float segundos)
            {
                AlParar?.Invoke(g, segundos);
                if (g.parada == ModoParada.Global)
                {
                    _globalHasta = Mathf.Max(_globalHasta, Time.unscaledTime + segundos);
                    if (_global == null) _global = StartCoroutine(ParadaGlobal());
                    return;
                }
                float hasta = Time.unscaledTime + segundos;
                foreach (var go in new[] { g.atacante, g.golpeado })
                {
                    if (go == null) continue;
                    _parados[go] = Mathf.Max(hasta, _parados.TryGetValue(go, out var h) ? h : 0f);
                    StartCoroutine(PararAnimaciones(go, segundos));
                }
                if (g.visualGolpeado != null) StartCoroutine(Temblar(g.visualGolpeado, segundos, g.direccion));
            }

            IEnumerator ParadaGlobal()
            {
                while (Time.unscaledTime < _globalHasta)
                {
                    // Respeta la pausa (0) y la cámara lenta del juego (la base).
                    float baseEscala = EscalaTiempoBase != null ? EscalaTiempoBase() : 1f;
                    if (baseEscala > 0f) Time.timeScale = baseEscala * 0.02f;
                    yield return null;
                }
                float b = EscalaTiempoBase != null ? EscalaTiempoBase() : 1f;
                if (Time.timeScale > 0f) Time.timeScale = b;
                _global = null;
            }

            IEnumerator PararAnimaciones(GameObject go, float segundos)
            {
                var animadores = go.GetComponentsInChildren<Animator>();
                var velocidades = new float[animadores.Length];
                for (int i = 0; i < animadores.Length; i++) { velocidades[i] = animadores[i].speed; animadores[i].speed = 0f; }
                float fin = Time.unscaledTime + segundos;
                while (Time.unscaledTime < fin) yield return null;
                for (int i = 0; i < animadores.Length; i++)
                    if (animadores[i] != null && animadores[i].speed == 0f) animadores[i].speed = velocidades[i];
                if (go != null && _parados.TryGetValue(go, out var h) && Time.unscaledTime >= h) _parados.Remove(go);
            }

            /// <summary>El golpeado tiembla en la dirección del golpe y el temblor se apaga (técnica de Sakurai).</summary>
            IEnumerator Temblar(Transform t, float segundos, Vector3 direccion)
            {
                Vector3 inicio = t.localPosition;
                float tamano = 1f;
                var r = t.GetComponentInChildren<Renderer>();
                if (r != null) tamano = Mathf.Max(0.05f, r.bounds.size.magnitude * 0.5f);
                Vector3 eje = direccion.sqrMagnitude > 1e-4f ? t.parent != null ? t.parent.InverseTransformDirection(direccion.normalized) : direccion.normalized : Vector3.right;
                float amp = Perfil.temblorGolpeado * tamano;
                for (float e = 0f; e < segundos && t != null; e += Time.unscaledDeltaTime)
                {
                    float apaga = 1f - e / segundos;
                    t.localPosition = inicio + eje * (Mathf.Sin(e * 90f) * amp * apaga);
                    yield return null;
                }
                if (t != null) t.localPosition = inicio;
            }

            static readonly int PEmision = Shader.PropertyToID("_EmissiveColor");

            public void Destellar(GameObject go, float nits, float segundos) => StartCoroutine(Destello(go, nits, segundos));

            IEnumerator Destello(GameObject go, float nits, float segundos)
            {
                if (nits <= 0f) yield break;
                var renderers = go.GetComponentsInChildren<Renderer>();
                var guardados = new MaterialPropertyBlock[renderers.Length];
                for (int i = 0; i < renderers.Length; i++)
                {
                    var mpb = new MaterialPropertyBlock();
                    renderers[i].GetPropertyBlock(mpb);
                    guardados[i] = mpb;
                    var nuevo = new MaterialPropertyBlock();
                    renderers[i].GetPropertyBlock(nuevo);
                    nuevo.SetColor(PEmision, Color.white * nits);
                    renderers[i].SetPropertyBlock(nuevo);
                }
                float fin = Time.unscaledTime + segundos;
                while (Time.unscaledTime < fin) yield return null;
                for (int i = 0; i < renderers.Length; i++)
                    if (renderers[i] != null) renderers[i].SetPropertyBlock(guardados[i].isEmpty ? null : guardados[i]);
            }

            public void Vibrar(Vector2 motores, float segundos)
            {
                if (_vibracion != null) StopCoroutine(_vibracion);
                _vibracion = StartCoroutine(Vibracion(motores, segundos));
            }

            IEnumerator Vibracion(Vector2 motores, float segundos)
            {
                var mando = Gamepad.current;
                if (mando == null) yield break;
                mando.SetMotorSpeeds(Mathf.Clamp01(motores.x), Mathf.Clamp01(motores.y));
                float fin = Time.unscaledTime + segundos;
                while (Time.unscaledTime < fin) yield return null;
                if (mando != null) mando.SetMotorSpeeds(0f, 0f);
                _vibracion = null;
            }

            void OnDisable()
            {
                if (Gamepad.current != null) Gamepad.current.SetMotorSpeeds(0f, 0f);
                if (_global != null && Time.timeScale > 0f) Time.timeScale = EscalaTiempoBase != null ? EscalaTiempoBase() : 1f;
            }
        }
    }
}
