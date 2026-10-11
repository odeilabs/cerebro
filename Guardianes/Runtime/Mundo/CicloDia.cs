using Guardianes.Enemigos;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace Guardianes.Mundo
{
    /// <summary>
    /// El dia y la noche: mueve el sol por el cielo segun la hora (con la latitud y la declinacion
    /// del sitio: en el ecuador sale a las 6 por el este y pasa casi por el cenit), enciende la luna
    /// cuando se pone, baja los limites de la exposicion para que la noche sea noche (oscura, azulada,
    /// pero jugable) y da a los humanos la luz que ven en cada punto (Humano.Luz: el cielo mas las
    /// FuenteLuz). Un dia de juego dura 'minutosPorDia' minutos; las misiones fijan la hora.
    /// 'nublado' (lo pone la Lluvia) quita sol, luna y luz para la IA, y algo de color.
    /// Sin un CicloDia en la escena todo sigue como antes (siempre de dia).
    /// </summary>
    [DefaultExecutionOrder(-50)]
    public class CicloDia : MonoBehaviour
    {
        public static CicloDia Instancia { get; private set; }
        /// <summary>Cambia de dia a noche o al reves (true = se hace de noche).</summary>
        public static event System.Action<bool> AlCambiarFase;

        [Header("Hora")]
        [Range(0f, 24f)] public float hora = 9f;
        public bool avanzar = true;
        [Tooltip("Minutos reales que dura un dia de juego")]
        public float minutosPorDia = 40f;

        [Header("Cielo (grados)")]
        [Tooltip("Latitud del sitio (negativa al sur): la Amazonia, -3")]
        public float latitud = -3f;
        [Tooltip("Declinacion del sol (la epoca del ano): principios de octubre, -6")]
        public float declinacion = -6f;

        [Header("Luces")]
        public Light sol;
        [Tooltip("Se crea si falta")]
        public Light luna;
        [Tooltip("Luz de la luna (lux): mas que la real para que la noche se juegue")]
        public float luxLuna = 8f;
        public float temperaturaLuna = 8200f;
        [Tooltip("Luz del cielo de noche (lux, desde arriba y sin sombras): lo que se ve bajo el dosel")]
        public float luxCieloNoche = 2.5f;

        [Header("Exposicion (EV100: limites del histograma)")]
        public Vector2 exposicionDia = new Vector2(7f, 15.5f);
        [Tooltip("De noche: lo mas que aclara (x) y lo mas que oscurece (y, para no quemar una hoguera de cerca)")]
        public Vector2 exposicionNoche = new Vector2(-0.6f, 8f);
        [Tooltip("Compensacion de la exposicion de noche (negativa: la noche se ve oscura aunque se aclare)")]
        public float compensacionNoche = -0.5f;
        public float compensacionDia = 0.3f;
        [Tooltip("Saturacion y temperatura de color de noche (la vista nocturna pierde color)")]
        public float saturacionNoche = -28f;
        public float temperaturaNoche = -14f;

        [Header("Luz para la IA (0 noche cerrada .. 1 dia)")]
        [Tooltip("La que queda con luna")]
        public float luzNoche = 0.12f;

        [Header("Nubes")]
        [Tooltip("Cielo cubierto (0 despejado .. 1 tormenta): menos sol, luna y luz para la IA, y menos color. Lo pone la Lluvia")]
        [Range(0f, 1f)] public float nublado;

        /// <summary>0 de noche .. 1 de dia (crepusculo civil entre medias).</summary>
        public float Dia { get; private set; } = 1f;
        public bool EsDeNoche => Dia < 0.35f;
        /// <summary>Luz del cielo para la IA (0..1).</summary>
        public float LuzCielo { get; private set; } = 1f;
        public Vector3 DireccionAlSol { get; private set; } = Vector3.up;

        float _luxSol, _adaptarDeGolpeHasta;
        Light _cieloNoche;
        Volume _volumen;
        Exposure _exposicion;
        ColorAdjustments _color;
        WhiteBalance _balance;
        bool _noche;

        public static string Reloj(float h)
        {
            h = Mathf.Repeat(h, 24f);
            int hh = Mathf.FloorToInt(h), mm = Mathf.FloorToInt((h - hh) * 60f);
            return $"{hh:00}:{mm:00}";
        }

        /// <summary>Direccion hacia el sol (o la luna, con la hora +12) para una hora, latitud y declinacion.</summary>
        public static Vector3 Direccion(float hora, float latitud, float declinacion)
        {
            float h = (hora - 12f) * 15f * Mathf.Deg2Rad;
            float fi = latitud * Mathf.Deg2Rad, de = declinacion * Mathf.Deg2Rad;
            float senEl = Mathf.Sin(fi) * Mathf.Sin(de) + Mathf.Cos(fi) * Mathf.Cos(de) * Mathf.Cos(h);
            float el = Mathf.Asin(Mathf.Clamp(senEl, -1f, 1f));
            // Acimut desde el norte, hacia el este.
            float az = Mathf.Atan2(-Mathf.Sin(h) * Mathf.Cos(de), Mathf.Cos(fi) * Mathf.Sin(de) - Mathf.Sin(fi) * Mathf.Cos(de) * Mathf.Cos(h));
            return new Vector3(Mathf.Cos(el) * Mathf.Sin(az), senEl, Mathf.Cos(el) * Mathf.Cos(az));
        }

        /// <summary>Luz en un punto para la IA: el cielo mas las fuentes (hogueras, linternas...).</summary>
        public static float Luz(Vector3 p)
        {
            float cielo = Instancia != null ? Instancia.LuzCielo : 1f;
            return Mathf.Clamp01(cielo + FuenteLuz.Total(p));
        }

        void Awake()
        {
            Instancia = this;
            if (sol == null) sol = RenderSettings.sun;
            if (sol == null)
                foreach (var l in FindObjectsByType<Light>(FindObjectsSortMode.None))
                    if (l.type == LightType.Directional && l != luna) { sol = l; break; }
            if (sol != null) _luxSol = sol.intensity;
            CrearLuna();
            CrearCieloNoche();
            CrearVolumen();
        }

        void OnEnable()
        {
            Instancia = this;
            Humano.Luz = Luz;
        }

        void OnDisable()
        {
            if (Instancia == this) Instancia = null;
            if (Humano.Luz == (System.Func<Vector3, float>)Luz) Humano.Luz = null;
        }

        void Start() => Aplicar();

        /// <summary>Pone la hora de golpe (misiones, pruebas).</summary>
        public void FijarHora(float h)
        {
            hora = Mathf.Repeat(h, 24f);
            // Un salto de hora (empieza una mision de noche): la exposicion se ajusta de golpe, sin los
            // segundos de adaptacion del ojo (si no, la escena sale negra o blanca un rato).
            _adaptarDeGolpeHasta = Time.unscaledTime + 1.5f;
            Aplicar();
        }

        void Update()
        {
            if (avanzar && minutosPorDia > 0f)
                hora = Mathf.Repeat(hora + Time.deltaTime * 24f / (minutosPorDia * 60f), 24f);
            Aplicar();
        }

        void CrearLuna()
        {
            if (luna != null) return;
            var go = new GameObject("Luna");
            go.transform.SetParent(transform, false);
            var hd = go.AddHDLight(LightType.Directional);
            luna = go.GetComponent<Light>();
            luna.lightUnit = LightUnit.Lux;
            luna.useColorTemperature = true;
            luna.colorTemperature = temperaturaLuna;
            luna.color = new Color(0.86f, 0.9f, 1f);
            luna.shadows = LightShadows.None;
            hd.angularDiameter = 0.52f;
            hd.interactsWithSky = true;
            hd.volumetricDimmer = 0.6f;
            hd.SetShadowResolutionLevel(2);
            luna.enabled = false;
        }

        /// <summary>
        /// El relleno del cielo de noche: una direccional desde arriba, sin sombra ni disco en el cielo,
        /// que hace de la luz que da la boveda (sin ella, bajo los arboles la noche seria negra).
        /// </summary>
        void CrearCieloNoche()
        {
            var go = new GameObject("CieloNocturno");
            go.transform.SetParent(transform, false);
            go.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            var hd = go.AddHDLight(LightType.Directional);
            _cieloNoche = go.GetComponent<Light>();
            _cieloNoche.lightUnit = LightUnit.Lux;
            _cieloNoche.useColorTemperature = true;
            _cieloNoche.colorTemperature = 9500f;
            _cieloNoche.color = new Color(0.8f, 0.86f, 1f);
            _cieloNoche.shadows = LightShadows.None;
            hd.interactsWithSky = false;
            hd.volumetricDimmer = 0f;
            _cieloNoche.enabled = false;
        }

        void CrearVolumen()
        {
            var go = new GameObject("VolumenCicloDia");
            go.transform.SetParent(transform, false);
            _volumen = go.AddComponent<Volume>();
            _volumen.isGlobal = true;
            _volumen.priority = 50f;
            var perfil = ScriptableObject.CreateInstance<VolumeProfile>();
            perfil.name = "CicloDia";
            _volumen.sharedProfile = perfil;
            _exposicion = perfil.Add<Exposure>(false);
            _exposicion.limitMin.Override(exposicionDia.x);
            _exposicion.limitMax.Override(exposicionDia.y);
            _exposicion.compensation.Override(compensacionDia);
            _exposicion.adaptationMode.Override(AdaptationMode.Fixed);
            _exposicion.adaptationMode.overrideState = false;
            _color = perfil.Add<ColorAdjustments>(false);
            _color.saturation.Override(0f);
            _balance = perfil.Add<WhiteBalance>(false);
            _balance.temperature.Override(0f);
        }

        void Aplicar()
        {
            var aSol = Direccion(hora, latitud, declinacion);
            var aLuna = Direccion(hora + 12f, latitud, -declinacion);
            DireccionAlSol = aSol;
            Dia = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-0.1f, 0.1f, aSol.y));
            float lunaArriba = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-0.02f, 0.12f, aLuna.y));
            LuzCielo = Mathf.Max(Dia, luzNoche * lunaArriba) * (1f - 0.45f * nublado);

            if (sol != null)
            {
                // El sol sigue encendido hasta unos 6 grados bajo el horizonte: el cielo fisico de HDRP
                // hace el crepusculo con el y la atmosfera ya le quita la luz directa.
                float f = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-0.11f, -0.06f, aSol.y));
                sol.enabled = f > 0.001f;
                sol.intensity = _luxSol * f * (1f - 0.7f * nublado);
                sol.transform.rotation = Quaternion.LookRotation(-aSol, Vector3.up);
                sol.shadows = sol.enabled ? LightShadows.Soft : LightShadows.None;
            }
            if (luna != null)
            {
                float f = lunaArriba * (1f - Dia);
                luna.enabled = f > 0.001f;
                luna.intensity = luxLuna * f * (1f - 0.6f * nublado);
                luna.transform.rotation = Quaternion.LookRotation(-aLuna, Vector3.up);
                // HDRP solo da sombra a una direccional: la luna cuando no hay sol.
                luna.shadows = luna.enabled && (sol == null || !sol.enabled) ? LightShadows.Soft : LightShadows.None;
            }
            if (_cieloNoche != null)
            {
                float f = 1f - Dia;
                _cieloNoche.enabled = f > 0.001f && luxCieloNoche > 0f;
                _cieloNoche.intensity = luxCieloNoche * f * (1f - 0.25f * nublado);
            }
            if (_exposicion != null)
            {
                _exposicion.limitMin.value = Mathf.Lerp(exposicionNoche.x, exposicionDia.x, Dia);
                _exposicion.limitMax.value = Mathf.Lerp(exposicionNoche.y, exposicionDia.y, Dia);
                _exposicion.compensation.value = Mathf.Lerp(compensacionNoche, compensacionDia, Dia);
                _exposicion.adaptationMode.overrideState = Time.unscaledTime < _adaptarDeGolpeHasta;
                _color.saturation.value = Mathf.Lerp(saturacionNoche, 0f, Dia) - 15f * nublado;
                _balance.temperature.value = Mathf.Lerp(temperaturaNoche, 0f, Dia);
            }
            bool noche = EsDeNoche;
            if (noche != _noche)
            {
                _noche = noche;
                AlCambiarFase?.Invoke(noche);
            }
        }
    }
}
