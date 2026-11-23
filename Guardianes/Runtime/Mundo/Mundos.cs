using System;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Guardianes.Mundo
{
    /// <summary>
    /// Varios mundos en un juego: cada mundo es su escena (su relieve, su agua, su vegetacion, sus lugares), y se viaja de uno a
    /// otro con un fundido a negro (Fenrir Hunt, por las raices de Yggdrasil; Primal Legends, entre los mundos de la saga).
    /// Lo que tiene que cruzar (la partida, lo decidido) va en estaticos del juego, que no se pierden al cambiar de escena; lo que
    /// el mundo nuevo tiene que hacer al llegar lo dice `Pendiente` (el juego lo lee en su arranque y lo borra).
    /// Sin escenas de mas no cambia nada: un juego de un solo mundo no lo usa.
    /// </summary>
    public static class Mundos
    {
        /// <summary>Lo que hay que hacer al llegar al mundo nuevo (el capitulo que empieza): lo pone quien viaja, lo lee el juego.</summary>
        public static string Pendiente;
        /// <summary>Se esta viajando: el fundido o la carga.</summary>
        public static bool Viajando { get; private set; }
        /// <summary>El mundo de ahora (el nombre de su escena).</summary>
        public static string Actual => SceneManager.GetActiveScene().name;
        /// <summary>Al llegar, con la escena ya cargada y antes de quitar el negro.</summary>
        public static event Action<string> AlLlegar;

        static Ayudante _ayudante;

        /// <summary>Viaja al mundo de esa escena (que tiene que estar en la compilacion), con lo pendiente para cuando llegue.</summary>
        public static void Ir(string escena, string pendiente = null, float fundido = 0.8f)
        {
            if (Viajando) return;
            Pendiente = pendiente;
            Asegurar().StartCoroutine(Viaje(escena, fundido));
        }

        /// <summary>Lo pendiente, una vez: el juego lo lee al arrancar en el mundo nuevo.</summary>
        public static string Recoger()
        {
            var p = Pendiente;
            Pendiente = null;
            return p;
        }

        static IEnumerator Viaje(string escena, float fundido)
        {
            Viajando = true;
            var a = _ayudante;
            yield return a.Fundir(1f, fundido);
            var carga = SceneManager.LoadSceneAsync(escena);
            while (carga != null && !carga.isDone) yield return null;
            // Un fotograma para que el mundo nuevo haga su Start (el relieve, el agua, la vegetacion) bajo el negro.
            yield return null;
            yield return null;
            AlLlegar?.Invoke(escena);
            Viajando = false;
            yield return a.Fundir(0f, fundido);
        }

        static Ayudante Asegurar()
        {
            if (_ayudante != null) return _ayudante;
            var go = new GameObject("Mundos");
            UnityEngine.Object.DontDestroyOnLoad(go);
            _ayudante = go.AddComponent<Ayudante>();
            return _ayudante;
        }

        /// <summary>El negro del viaje, por encima de todo, y las corrutinas que sobreviven al cambio de escena.</summary>
        class Ayudante : MonoBehaviour
        {
            Image _negro;

            void Awake()
            {
                var lienzo = gameObject.AddComponent<Canvas>();
                lienzo.renderMode = RenderMode.ScreenSpaceOverlay;
                lienzo.sortingOrder = 32000;
                var go = new GameObject("Negro", typeof(RectTransform));
                go.transform.SetParent(transform, false);
                _negro = go.AddComponent<Image>();
                _negro.color = new Color(0f, 0f, 0f, 0f);
                _negro.raycastTarget = false;
                var rt = (RectTransform)go.transform;
                rt.anchorMin = Vector2.zero;
                rt.anchorMax = Vector2.one;
                rt.offsetMin = rt.offsetMax = Vector2.zero;
            }

            public IEnumerator Fundir(float hasta, float segundos)
            {
                float desde = _negro.color.a;
                for (float t = 0f; t < segundos; t += Time.unscaledDeltaTime)
                {
                    _negro.color = new Color(0f, 0f, 0f, Mathf.Lerp(desde, hasta, t / segundos));
                    yield return null;
                }
                _negro.color = new Color(0f, 0f, 0f, hasta);
            }
        }
    }
}
