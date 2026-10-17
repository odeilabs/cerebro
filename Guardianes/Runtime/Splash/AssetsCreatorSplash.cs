// ============================================================================
// AssetsCreatorSplash.cs — el SPLASH de un estudio al arrancar el juego: el
// vídeo de `creador.py splash`, a pantalla completa, saltable, con la escena
// siguiente cargándose MIENTRAS se ve y un plan B si el vídeo no se puede
// reproducir.
//
// USO: una escena de arranque (la primera del Build) con un GameObject vacío y
// este componente. `clip` = el <nombre>_unity.mp4 (Windows, Mac) o el
// <nombre>_unity.webm (VP8 + Vorbis, el único WebM que IMPORTA el editor de
// Unity: un VP9 + Opus da error). `poster` = el <nombre>_poster.png.
// `siguienteEscena` = la del menú (vacío: la siguiente del Build).
//
// Lo que resuelve y por qué:
//   - PREPARA antes de enseñar: un VideoPlayer que arranca sin Prepare() pinta
//     uno o dos fotogramas de basura o de blanco antes del primero; aquí la
//     cámara limpia a negro hasta que el vídeo está listo (con un tope: si
//     tarda más de `esperaPreparar`, plan B).
//   - CARGA EN PARALELO: la escena siguiente se carga con
//     allowSceneActivation = false mientras dura el splash, y se activa al
//     acabar. El splash no alarga el arranque: lo tapa.
//   - SALTABLE pasado `minimoAntesDeSaltar` (cualquier tecla, botón o clic con
//     el Input Manager clásico; con el Input System nuevo el juego llama a
//     Saltar()). Al saltar funde a negro en `fundidoSalida` y entra la escena.
//   - PLAN B: si el vídeo da error (formato que la plataforma no lee) o no se
//     prepara a tiempo, enseña el póster con un fundido y sigue. Un splash que
//     falla no puede dejar el juego en negro.
//   - TOPE: si por lo que sea el vídeo no termina, a los `esperaMaxima` s sigue.
//
// INSTALACIÓN: FUERA de Editor/. Necesita el módulo de vídeo de Unity (viene
// por defecto). El splash nativo de Unity ("Made with Unity") es aparte: en
// Unity 6 Personal es opcional (Player Settings > Splash Image).
// ============================================================================
using System.Collections;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using UnityEngine.Video;

namespace AssetsCreator.Splash
{
    public class AssetsCreatorSplash : MonoBehaviour
    {
        [Tooltip("El vídeo importado: <nombre>_unity.mp4 o <nombre>_unity.webm (VP8)")]
        public VideoClip clip;
        [Tooltip("Alternativa a 'clip': ruta dentro de StreamingAssets (se reproduce por URL)")]
        public string archivoStreamingAssets = "";
        [Tooltip("Plan B si el vídeo no se puede reproducir: <nombre>_poster.png")]
        public Texture2D poster;
        [Tooltip("Escena que carga detrás; vacío = la siguiente del Build")]
        public string siguienteEscena = "";
        public bool saltable = true;
        [Tooltip("Segundos antes de dejar saltar (que se vea al menos el golpe)")]
        public float minimoAntesDeSaltar = 0.6f;
        [Tooltip("Fundido a negro al saltar")]
        public float fundidoSalida = 0.35f;
        [Tooltip("Si el vídeo no está listo en este tiempo, plan B")]
        public float esperaPreparar = 3f;
        [Tooltip("Tope por si el vídeo no termina nunca")]
        public float esperaMaxima = 12f;
        [Tooltip("Segundos que se enseña el póster en el plan B")]
        public float duracionPoster = 2.5f;
        [Range(0f, 1f)] public float volumen = 1f;
        [Tooltip("Al acabar (antes de activar la escena siguiente)")]
        public UnityEvent alTerminar = new UnityEvent();

        VideoPlayer reproductor;
        AudioSource fuente;
        Camera camara;
        AsyncOperation carga;
        float inicio;
        bool terminado, saltar, fallo;
        float opacidadPoster;
        Texture2D negro;

        /// <summary>Saltar el splash desde el juego (p. ej. con el Input System nuevo).</summary>
        public void Saltar()
        {
            if (saltable && Time.unscaledTime - inicio >= minimoAntesDeSaltar) saltar = true;
        }

        void Awake()
        {
            camara = gameObject.AddComponent<Camera>();
            camara.clearFlags = CameraClearFlags.SolidColor;
            camara.backgroundColor = Color.black;
            camara.cullingMask = 0;
            camara.depth = 100;
            negro = new Texture2D(1, 1);
            negro.SetPixel(0, 0, Color.black);
            negro.Apply();
        }

        IEnumerator Start()
        {
            inicio = Time.unscaledTime;
            EmpezarCarga();
            if (clip == null && string.IsNullOrEmpty(archivoStreamingAssets))
            {
                yield return PlanB();
                yield return Salir();
                yield break;
            }
            fuente = gameObject.AddComponent<AudioSource>();
            fuente.playOnAwake = false;
            fuente.volume = volumen;
            reproductor = gameObject.AddComponent<VideoPlayer>();
            reproductor.playOnAwake = false;
            reproductor.isLooping = false;
            reproductor.renderMode = VideoRenderMode.CameraNearPlane;
            reproductor.targetCamera = camara;
            reproductor.aspectRatio = VideoAspectRatio.FitInside;
            reproductor.audioOutputMode = VideoAudioOutputMode.AudioSource;
            reproductor.SetTargetAudioSource(0, fuente);
            if (clip != null)
            {
                reproductor.source = VideoSource.VideoClip;
                reproductor.clip = clip;
            }
            else
            {
                reproductor.source = VideoSource.Url;
                reproductor.url = System.IO.Path.Combine(Application.streamingAssetsPath, archivoStreamingAssets);
            }
            reproductor.errorReceived += (vp, msg) => { Debug.LogWarning("[Splash] " + msg); fallo = true; };
            reproductor.loopPointReached += vp => terminado = true;
            reproductor.Prepare();
            float t = 0f;
            while (!reproductor.isPrepared && !fallo && t < esperaPreparar)
            {
                t += Time.unscaledDeltaTime;
                yield return null;
            }
            if (fallo || !reproductor.isPrepared)
            {
                if (!fallo) Debug.LogWarning("[Splash] el vídeo no se preparó en " + esperaPreparar + " s");
                reproductor.Stop();
                yield return PlanB();
                yield return Salir();
                yield break;
            }
            reproductor.Play();
            float tope = Time.unscaledTime + esperaMaxima;
            while (!terminado && !fallo && Time.unscaledTime < tope)
            {
                LeerEntrada();
                if (saltar) break;
                yield return null;
            }
            if (saltar || fallo)
            {
                // fundido a negro desde donde esté (el vídeo ya acaba en negro
                // si se deja terminar)
                float a = reproductor.targetCameraAlpha;
                for (float k = 0f; k < 1f; k += Time.unscaledDeltaTime / Mathf.Max(0.01f, fundidoSalida))
                {
                    reproductor.targetCameraAlpha = a * (1f - k);
                    fuente.volume = volumen * (1f - k);
                    yield return null;
                }
            }
            reproductor.Stop();
            yield return Salir();
        }

        void LeerEntrada()
        {
#if ENABLE_LEGACY_INPUT_MANAGER
            if (Input.anyKeyDown || Input.GetMouseButtonDown(0)) Saltar();
#endif
        }

        IEnumerator PlanB()
        {
            if (poster == null) yield break;
            float d = Mathf.Max(0.5f, duracionPoster);
            for (float t = 0f; t < d; t += Time.unscaledDeltaTime)
            {
                // entra en 0,3 s, se queda y sale en 0,4 s
                opacidadPoster = Mathf.Clamp01(t / 0.3f) * Mathf.Clamp01((d - t) / 0.4f);
                LeerEntrada();
                if (saltar) break;
                yield return null;
            }
            opacidadPoster = 0f;
        }

        void EmpezarCarga()
        {
            if (!string.IsNullOrEmpty(siguienteEscena))
                carga = SceneManager.LoadSceneAsync(siguienteEscena);
            else if (SceneManager.GetActiveScene().buildIndex + 1 < SceneManager.sceneCountInBuildSettings)
                carga = SceneManager.LoadSceneAsync(SceneManager.GetActiveScene().buildIndex + 1);
            if (carga != null) carga.allowSceneActivation = false;
        }

        IEnumerator Salir()
        {
            alTerminar.Invoke();
            if (carga != null)
            {
                carga.allowSceneActivation = true;
                while (!carga.isDone) yield return null;
            }
            else
            {
                enabled = false;
            }
        }

        void OnGUI()
        {
            if (opacidadPoster <= 0f || poster == null) return;
            var r = new Rect(0, 0, Screen.width, Screen.height);
            GUI.DrawTexture(r, negro);
            var c = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, opacidadPoster);
            GUI.DrawTexture(r, poster, ScaleMode.ScaleToFit);
            GUI.color = c;
        }

        void OnDestroy()
        {
            if (negro != null) Destroy(negro);
        }
    }
}
