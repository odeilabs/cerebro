using Guardianes.Efectos;
using Guardianes.Mundo;
using Guardianes.UI;
using Guardianes.Vegetacion;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace Guardianes.Juego
{
    /// <summary>
    /// Niveles de calidad del juego. "Ultra" es el perfil de la selva tal cual; los demas bajan lo
    /// caro de HDRP con un volumen por encima (nubes volumetricas, niebla volumetrica, SSGI, SSR,
    /// SSAO, sombras de contacto, distancia de sombras, teselado del agua) y ademas la resolucion
    /// interna (reescalado temporal), la distancia de la vegetacion, el detalle del terreno, la sombra
    /// del sol y el presupuesto de particulas (y en Bajo, sin desenfoque de movimiento); lo de cada criatura (las capas del pelaje de Kong) lo
    /// aplica su juego escuchando AlAplicar.
    /// Al empezar elige segun la grafica (las integradas van en Bajo) o lo guardado; F1-F4 cambian
    /// de nivel y -calidad bajo|medio|alto|ultra lo fuerza desde la linea de comandos.
    /// </summary>
    [DefaultExecutionOrder(-50)]
    public class CalidadJuego : MonoBehaviour
    {
        public enum Nivel { Bajo, Medio, Alto, Ultra }

        [Tooltip("Clave de las preferencias donde se guarda el nivel (una por juego)")]
        public string clave = "kk_calidad";

        public Nivel Actual { get; private set; } = Nivel.Ultra;
        public static CalidadJuego Instancia { get; private set; }
        /// <summary>Se aplica un nivel (cada juego ajusta lo suyo: pelajes, mallas de su criatura...).</summary>
        public static event System.Action<Nivel> AlAplicar;
        Volume _volumen;
        VolumeProfile _perfil;
        float _resolucion = 100f;

        void Awake()
        {
            Instancia = this;
            _perfil = ScriptableObject.CreateInstance<VolumeProfile>();
            _perfil.name = "Calidad";
            _volumen = gameObject.AddComponent<Volume>();
            _volumen.isGlobal = true;
            _volumen.priority = 50f;
            _volumen.sharedProfile = _perfil;
            DynamicResolutionHandler.SetDynamicResScaler(() => _resolucion, DynamicResScalePolicyType.ReturnsPercentage);
        }

        void Start() => Aplicar(Inicial(clave), false);

        void OnDestroy()
        {
            if (Instancia == this) Instancia = null;
            if (_perfil != null) Destroy(_perfil);
        }

        void Update()
        {
            var k = Keyboard.current;
            if (k == null) return;
            if (k.f1Key.wasPressedThisFrame) Aplicar(Nivel.Bajo, true);
            if (k.f2Key.wasPressedThisFrame) Aplicar(Nivel.Medio, true);
            if (k.f3Key.wasPressedThisFrame) Aplicar(Nivel.Alto, true);
            if (k.f4Key.wasPressedThisFrame) Aplicar(Nivel.Ultra, true);
        }

        static Nivel Inicial(string Clave)
        {
            var args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == "-calidad" && System.Enum.TryParse(args[i + 1], true, out Nivel forzado))
                    return forzado;
            try
            {
                if (PlayerPrefs.HasKey(Clave)) return (Nivel)Mathf.Clamp(PlayerPrefs.GetInt(Clave), 0, 3);
            }
            catch { /* sin preferencias guardadas */ }
            return Detectar();
        }

        /// <summary>Nivel recomendado para la grafica: las integradas (memoria compartida) en Bajo.</summary>
        public static Nivel Detectar()
        {
            string g = SystemInfo.graphicsDeviceName.ToLowerInvariant();
            bool integrada = g.Contains("radeon(tm) graphics") || g.Contains("radeon graphics") || g.Contains("vega ") ||
                             g.Contains("uhd graphics") || g.Contains("iris") || (g.Contains("intel") && !g.Contains("arc"));
            if (integrada) return Nivel.Bajo;
            int vram = SystemInfo.graphicsMemorySize;
            if (vram < 5000) return Nivel.Medio;
            if (vram < 9000) return Nivel.Alto;
            return Nivel.Ultra;
        }

        public void Aplicar(Nivel nivel, bool guardar)
        {
            Actual = nivel;
            if (guardar)
            {
                try { PlayerPrefs.SetInt(clave, (int)nivel); PlayerPrefs.Save(); }
                catch { /* sin preferencias */ }
                var hud = FindAnyObjectByType<HudJuego>();
                if (hud != null) hud.Avisar("Calidad: " + Nombre(nivel));
            }
            Volumen(nivel);
            int n = (int)nivel;
            _resolucion = new[] { 58f, 72f, 87f, 100f }[n];
            foreach (var cam in FindObjectsByType<Camera>())
            {
                var hd = cam.GetComponent<HDAdditionalCameraData>();
                if (hd != null) hd.allowDynamicResolution = nivel != Nivel.Ultra;
            }
            if (RenderizadorVegetacion.Instancia != null)
                RenderizadorVegetacion.Instancia.multiplicadorDistancia = new[] { 0.5f, 0.7f, 0.9f, 1f }[n];
            AlAplicar?.Invoke(nivel);
            if (TerrenoMundo.Instancia != null)
                foreach (var t in TerrenoMundo.Instancia.Terrenos)
                    if (t != null)
                    {
                        t.heightmapPixelError = new[] { 8f, 5f, 3f, 2f }[n];
                        t.basemapDistance = new[] { 300f, 500f, 700f, 900f }[n];
                    }
            foreach (var luz in FindObjectsByType<HDAdditionalLightData>())
                if (luz.GetComponent<Light>().type == LightType.Directional)
                    luz.SetShadowResolutionLevel(n);
            if (EfectosCombate.Instancia != null)
                EfectosCombate.Instancia.presupuesto = new[] { 200, 350, 500, 600 }[n];
            QualitySettings.lodBias = new[] { 0.6f, 0.8f, 1f, 1.2f }[n];
            Debug.Log($"[GD] Calidad {Nombre(nivel)} (resolucion {_resolucion}%, grafica {SystemInfo.graphicsDeviceName})");
        }

        public static string Nombre(Nivel n) => n switch
        {
            Nivel.Bajo => "Baja",
            Nivel.Medio => "Media",
            Nivel.Alto => "Alta",
            _ => "Ultra",
        };

        /// <summary>Overrides del volumen de calidad; en Ultra no se toca el perfil de la selva.</summary>
        void Volumen(Nivel nivel)
        {
            foreach (var c in _perfil.components.ToArray())
            {
                _perfil.components.Remove(c);
                Destroy(c);
            }
            _volumen.enabled = nivel != Nivel.Ultra;
            if (nivel == Nivel.Ultra) return;
            bool bajo = nivel == Nivel.Bajo, medio = nivel == Nivel.Medio;
            var q = bajo ? ScalableSettingLevelParameter.Level.Low : medio ? ScalableSettingLevelParameter.Level.Medium : ScalableSettingLevelParameter.Level.High;

            var nubes = _perfil.Add<VolumetricClouds>();
            if (bajo) nubes.enable.Override(false);
            else
            {
                nubes.numPrimarySteps.Override(medio ? 32 : 64);
                nubes.numLightSteps.Override(medio ? 4 : 6);
            }

            var niebla = _perfil.Add<Fog>();
            niebla.enableVolumetricFog.Override(!bajo);
            niebla.quality.Override((int)q);

            // SSGI solo en Alto (y en su calidad baja); por debajo, el ambiente del cielo.
            var gi = _perfil.Add<GlobalIllumination>();
            gi.enable.Override(nivel == Nivel.Alto);
            gi.quality.Override((int)ScalableSettingLevelParameter.Level.Low);

            var ssr = _perfil.Add<ScreenSpaceReflection>();
            ssr.enabled.Override(!bajo);
            ssr.quality.Override((int)q);

            _perfil.Add<ScreenSpaceAmbientOcclusion>().quality.Override((int)q);
            _perfil.Add<ContactShadows>().enable.Override(!bajo);
            _perfil.Add<MicroShadowing>().enable.Override(!bajo);

            var sombras = _perfil.Add<HDShadowSettings>();
            sombras.maxShadowDistance.Override(bajo ? 450f : medio ? 800f : 1200f);
            sombras.cascadeShadowSplitCount.Override(bajo ? 2 : medio ? 3 : 4);

            _perfil.Add<WaterRendering>().triangleSize.Override(bajo ? 45f : medio ? 30f : 22f);

            // El desenfoque de movimiento sale de lo que se mueve entre un fotograma y otro: con los pocos
            // fotogramas de una integrada lo emborrona todo al girar la camara.
            if (bajo) _perfil.Add<MotionBlur>().intensity.Override(0f);
        }
    }
}
