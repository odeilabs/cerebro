using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Guardianes.Juego
{
    /// <summary>
    /// LAS OPCIONES del jugador, comunes a la saga (PlayerPrefs): pantalla (completa, resolucion, VSync), volumen,
    /// camara (sensibilidad e invertir el eje Y), subtitulos (tamano y fondo), daltonismo (la paleta de los avisos),
    /// dificultad (el dano que recibe la criatura y lo deprisa que la reconocen los humanos), idioma y los controles
    /// (cada juego registra sus acciones, Registrar; las teclas cambiadas se guardan). La calidad la lleva
    /// CalidadJuego. El panel de opciones esta en HudJuego. Se cargan solas al empezar.
    /// </summary>
    public static class OpcionesJuego
    {
        public enum Daltonismo { No, Deuteranopia, Protanopia, Tritanopia }
        public enum Dificultad { Facil, Normal, Dificil }

        const string P = "op_";

        public static bool PantallaCompleta = true;
        /// <summary>Indice en Resoluciones (-1: la del escritorio).</summary>
        public static int Resolucion = -1;
        public static bool VSync = true;
        public static float Volumen = 1f;
        public static float Sensibilidad = 1f;
        public static bool InvertirY;
        /// <summary>Escala del texto de los subtitulos y la narracion (1, 1,25 o 1,5).</summary>
        public static float Subtitulos = 1f;
        public static bool FondoSubtitulos = true;
        public static Daltonismo Paleta = Daltonismo.No;
        public static Dificultad Nivel = Dificultad.Normal;
        /// <summary>"es" o "en".</summary>
        public static string Idioma = "es";
        /// <summary>Los consejos de la primera vez (los controles cuando hacen falta).</summary>
        public static bool Consejos = true;

        public static event Action AlCambiar;
        static bool _cargadas;

        // ------------------------------------------------------------------ Lo que aplican los juegos
        /// <summary>Lo que se multiplica el dano que recibe la criatura del jugador.</summary>
        public static float DanioRecibido => Nivel == Dificultad.Facil ? 0.5f : Nivel == Dificultad.Dificil ? 1.5f : 1f;
        /// <summary>Lo que se multiplica lo deprisa que la reconocen los humanos.</summary>
        public static float Deteccion => Nivel == Dificultad.Facil ? 0.65f : Nivel == Dificultad.Dificil ? 1.35f : 1f;

        /// <summary>Los colores de los avisos segun la paleta: peligro (el rojo), aviso (el ambar) y bien (el verde).</summary>
        public static Color Peligro => Paleta switch
        {
            Daltonismo.Deuteranopia or Daltonismo.Protanopia => new Color(0.85f, 0.37f, 0.0f),
            Daltonismo.Tritanopia => new Color(0.9f, 0.1f, 0.35f),
            _ => new Color(1f, 0.25f, 0.2f),
        };
        public static Color Aviso => Paleta switch
        {
            Daltonismo.Tritanopia => new Color(1f, 0.6f, 0.75f),
            _ => new Color(1f, 0.72f, 0.2f),
        };
        public static Color Bien => Paleta switch
        {
            Daltonismo.Deuteranopia or Daltonismo.Protanopia => new Color(0.35f, 0.6f, 1f),
            Daltonismo.Tritanopia => new Color(0.2f, 0.85f, 0.85f),
            _ => new Color(0.4f, 1f, 0.45f),
        };

        // ------------------------------------------------------------------ Resoluciones
        static Resolution[] _resoluciones;
        /// <summary>Las resoluciones de la pantalla (sin repetir ancho y alto).</summary>
        public static Resolution[] Resoluciones
        {
            get
            {
                if (_resoluciones != null) return _resoluciones;
                var lista = new List<Resolution>();
                foreach (var r in Screen.resolutions)
                    if (!lista.Exists(x => x.width == r.width && x.height == r.height)) lista.Add(r);
                _resoluciones = lista.ToArray();
                return _resoluciones;
            }
        }

        public static string NombreResolucion =>
            Resolucion >= 0 && Resolucion < Resoluciones.Length ? $"{Resoluciones[Resolucion].width}×{Resoluciones[Resolucion].height}" : "la del escritorio";

        // ------------------------------------------------------------------ Guardar y cargar
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void AlEmpezar()
        {
            _cargadas = false;
            _resoluciones = null;
            Cargar();
        }

        public static void Cargar()
        {
            if (_cargadas) return;
            _cargadas = true;
            // Las pruebas en batch, con las de serie (no las del jugador).
            if (Application.isBatchMode) return;
            try
            {
                PantallaCompleta = PlayerPrefs.GetInt(P + "completa", 1) == 1;
                Resolucion = PlayerPrefs.GetInt(P + "resolucion", -1);
                VSync = PlayerPrefs.GetInt(P + "vsync", 1) == 1;
                Volumen = PlayerPrefs.GetFloat(P + "volumen", 1f);
                Sensibilidad = PlayerPrefs.GetFloat(P + "sensibilidad", 1f);
                InvertirY = PlayerPrefs.GetInt(P + "invertir", 0) == 1;
                Subtitulos = PlayerPrefs.GetFloat(P + "subtitulos", 1f);
                FondoSubtitulos = PlayerPrefs.GetInt(P + "fondo", 1) == 1;
                Paleta = (Daltonismo)Mathf.Clamp(PlayerPrefs.GetInt(P + "daltonismo", 0), 0, 3);
                Nivel = (Dificultad)Mathf.Clamp(PlayerPrefs.GetInt(P + "dificultad", 1), 0, 2);
                Idioma = PlayerPrefs.GetString(P + "idioma", "es");
                Consejos = PlayerPrefs.GetInt(P + "consejos", 1) == 1;
            }
            catch (Exception e) { Debug.LogWarning("[GD] Opciones no leidas: " + e.Message); }
            Aplicar(false);
        }

        public static void Guardar()
        {
            try
            {
                PlayerPrefs.SetInt(P + "completa", PantallaCompleta ? 1 : 0);
                PlayerPrefs.SetInt(P + "resolucion", Resolucion);
                PlayerPrefs.SetInt(P + "vsync", VSync ? 1 : 0);
                PlayerPrefs.SetFloat(P + "volumen", Volumen);
                PlayerPrefs.SetFloat(P + "sensibilidad", Sensibilidad);
                PlayerPrefs.SetInt(P + "invertir", InvertirY ? 1 : 0);
                PlayerPrefs.SetFloat(P + "subtitulos", Subtitulos);
                PlayerPrefs.SetInt(P + "fondo", FondoSubtitulos ? 1 : 0);
                PlayerPrefs.SetInt(P + "daltonismo", (int)Paleta);
                PlayerPrefs.SetInt(P + "dificultad", (int)Nivel);
                PlayerPrefs.SetString(P + "idioma", Idioma);
                PlayerPrefs.SetInt(P + "consejos", Consejos ? 1 : 0);
                PlayerPrefs.Save();
            }
            catch (Exception e) { Debug.LogWarning("[GD] Opciones no guardadas: " + e.Message); }
        }

        /// <summary>Aplica lo que es del motor (pantalla, VSync, volumen) y avisa a los que escuchan.</summary>
        public static void Aplicar(bool guardar = true)
        {
            AudioListener.volume = Mathf.Clamp01(Volumen);
            // En las pruebas en batch no se toca la pantalla.
            if (!Application.isBatchMode)
            {
                QualitySettings.vSyncCount = VSync ? 1 : 0;
                var modo = PantallaCompleta ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed;
                if (Resolucion >= 0 && Resolucion < Resoluciones.Length)
                    Screen.SetResolution(Resoluciones[Resolucion].width, Resoluciones[Resolucion].height, modo);
                else if (Screen.fullScreenMode != modo) Screen.fullScreenMode = modo;
            }
            if (guardar) Guardar();
            AlCambiar?.Invoke();
        }

        // ------------------------------------------------------------------ Controles
        /// <summary>Una accion que se puede cambiar de tecla: su nombre para el jugador y la accion.</summary>
        public struct AccionControl { public string nombre; public InputAction accion; }
        static readonly List<AccionControl> _acciones = new List<AccionControl>();
        public static IReadOnlyList<AccionControl> Acciones => _acciones;

        /// <summary>El juego registra las acciones que el jugador puede cambiar (y se le aplican las que guardo).</summary>
        public static void Registrar(string nombre, InputAction accion)
        {
            if (accion == null) return;
            _acciones.RemoveAll(a => a.nombre == nombre);
            _acciones.Add(new AccionControl { nombre = nombre, accion = accion });
            if (Application.isBatchMode) return;
            string json = PlayerPrefs.GetString(P + "tecla_" + accion.name, "");
            if (!string.IsNullOrEmpty(json))
            {
                try { accion.LoadBindingOverridesFromJson(json); }
                catch (Exception e) { Debug.LogWarning($"[GD] Controles de {accion.name} no leidos: {e.Message}"); }
            }
        }

        public static void GuardarControl(InputAction accion)
        {
            if (accion == null) return;
            PlayerPrefs.SetString(P + "tecla_" + accion.name, accion.SaveBindingOverridesAsJson());
            PlayerPrefs.Save();
        }

        /// <summary>Todas las teclas como venian.</summary>
        public static void RestablecerControles()
        {
            foreach (var a in _acciones)
            {
                a.accion.RemoveAllBindingOverrides();
                PlayerPrefs.DeleteKey(P + "tecla_" + a.accion.name);
            }
            PlayerPrefs.Save();
        }

        /// <summary>Las teclas de una accion, para el panel (las del teclado y el raton, y las del mando).</summary>
        public static string Teclas(InputAction a, bool mando)
        {
            if (a == null) return "";
            var partes = new List<string>();
            for (int i = 0; i < a.bindings.Count; i++)
            {
                var b = a.bindings[i];
                if (b.isComposite) continue;
                string ruta = b.effectivePath ?? "";
                bool esMando = ruta.StartsWith("<Gamepad>") || ruta.StartsWith("<XInputController>") || ruta.StartsWith("<DualShockGamepad>");
                if (esMando != mando || string.IsNullOrEmpty(ruta)) continue;
                partes.Add(a.GetBindingDisplayString(i));
            }
            return string.Join(" / ", partes);
        }
    }
}
