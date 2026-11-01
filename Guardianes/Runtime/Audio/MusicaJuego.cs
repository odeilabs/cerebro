using System;
using Guardianes.Juego;
using UnityEngine;

namespace Guardianes.Audio
{
    /// <summary>
    /// La musica que cambia con lo que pasa: tres bucles (exploracion, combate y jefe, los de
    /// Tools~/arte/audio/generar_musica.py en Resources/Audio/musica) que se funden de uno a otro. Cada juego dice en
    /// que estado esta con Decidir (cada medio segundo); el combate se queda un rato despues de la ultima senal
    /// (sostenerCombate) para no ir y venir. El volumen es OpcionesJuego.VolumenMusica (y el general, el del oyente).
    /// Sin Asegurar, no hay musica: es opcional y por juego.
    /// </summary>
    public class MusicaJuego : MonoBehaviour
    {
        public enum Estado { Exploracion, Combate, Jefe }

        public static MusicaJuego Instancia { get; private set; }
        /// <summary>Lo que dice el juego (null: siempre exploracion).</summary>
        public Func<Estado> Decidir;
        public float volumen = 0.45f;
        public float fundido = 2.5f;
        public float sostenerCombate = 8f;

        public Estado Actual { get; private set; }
        public int Cambios { get; private set; }
        public static event Action<Estado> AlCambiar;

        readonly AudioSource[] _fuentes = new AudioSource[3];
        float _reloj, _ultimoCombate = -100f;

        /// <summary>La crea (con sus bucles de Resources/Audio/musica) si no esta.</summary>
        public static MusicaJuego Asegurar()
        {
            if (Instancia == null)
            {
                var go = new GameObject("Musica");
                DontDestroyOnLoad(go);
                Instancia = go.AddComponent<MusicaJuego>();
            }
            return Instancia;
        }

        void Awake()
        {
            Instancia = this;
            string[] nombres = { "exploracion_0", "combate_0", "jefe_0" };
            for (int i = 0; i < 3; i++)
            {
                var f = gameObject.AddComponent<AudioSource>();
                f.clip = Resources.Load<AudioClip>("Audio/musica/" + nombres[i]);
                f.loop = true;
                f.playOnAwake = false;
                f.spatialBlend = 0f;
                f.volume = 0f;
                f.priority = 0;
                f.ignoreListenerPause = false;
                if (f.clip != null) f.Play();
                _fuentes[i] = f;
            }
            if (_fuentes[0].clip == null) Debug.LogWarning("[GD] Musica: no estan los bucles (generar_musica.py en Resources/Audio/musica)");
        }

        void OnDestroy()
        {
            if (Instancia == this) Instancia = null;
        }

        void Update()
        {
            _reloj -= Time.unscaledDeltaTime;
            if (_reloj <= 0f)
            {
                _reloj = 0.5f;
                var e = Decidir != null ? Decidir() : Estado.Exploracion;
                if (e != Estado.Exploracion) _ultimoCombate = Time.unscaledTime;
                // Pasado el combate, se queda un rato antes de volver a explorar.
                if (e == Estado.Exploracion && Actual != Estado.Exploracion && Time.unscaledTime - _ultimoCombate < sostenerCombate) e = Actual == Estado.Jefe ? Estado.Combate : Actual;
                if (e != Actual)
                {
                    Debug.Log($"[GD] Musica: {Actual} -> {e}");
                    Actual = e;
                    Cambios++;
                    // El bucle que entra empieza desde el principio (una frase entera).
                    var f = _fuentes[(int)e];
                    if (f.clip != null && f.volume < 0.01f) f.time = 0f;
                    AlCambiar?.Invoke(e);
                }
            }
            float objetivo = volumen * Mathf.Clamp01(OpcionesJuego.VolumenMusica);
            float paso = Time.unscaledDeltaTime / Mathf.Max(0.05f, fundido);
            for (int i = 0; i < 3; i++)
            {
                var f = _fuentes[i];
                if (f == null || f.clip == null) continue;
                f.volume = Mathf.MoveTowards(f.volume, i == (int)Actual ? objetivo : 0f, paso * Mathf.Max(objetivo, 0.05f));
            }
        }
    }
}
