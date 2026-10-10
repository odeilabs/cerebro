using UnityEngine;

namespace Guardianes.Sensacion
{
    /// <summary>Puntos de partida para las criaturas de la saga (cada juego ajusta el suyo).</summary>
    public enum ArquetipoSensacion
    {
        /// <summary>Gigante pesado de cuerpo a cuerpo: King Kong, Behemot, Magmákor, Cerbero.</summary>
        Gigante,
        /// <summary>Pequeño y ágil, de sigilo y emboscada: la cría de Basiliskos, Noctarion, Kitsune.</summary>
        Agil,
        /// <summary>Crece de pequeño a enorme: Basiliskos entero (el perfil escala con el tamaño).</summary>
        Creciente,
        /// <summary>Vuela: Fénix, Grifo, Pájaro del Trueno, Roc. Menos sacudida (marea en el aire).</summary>
        Volador,
        /// <summary>Nada: Leviatán, Jörmungandr, Kraken, Megalodón. Sacudida lenta y amortiguada.</summary>
        Acuatico,
        /// <summary>Arcade de plataformas: Quimera Flip. Paradas cortas y nítidas, sacudida mínima.</summary>
        Arcade,
    }

    /// <summary>
    /// Cómo responde un juego a los impactos: cuánto se para la acción (hitstop), cuánto tiembla la
    /// cámara (trauma), el destello y la vibración, según la fuerza del golpe (0 roce .. 1 golpe
    /// fuerte .. más de 1 remate). Cada juego crea el suyo (o parte de un arquetipo con Crear) y lo
    /// ajusta a sus mecánicas; Sensaciones.Perfil es el activo. Valores en segundos, grados y metros.
    /// Ver Investigacion/Fluidez-y-sensacion.md del cerebro.
    /// </summary>
    [CreateAssetMenu(menuName = "Guardianes/Perfil de sensación")]
    public class PerfilSensacion : ScriptableObject
    {
        [Header("Parada (hitstop)")]
        [Tooltip("Por debajo de esta fuerza no hay parada (roces, golpes continuos)")]
        public float paradaUmbral = 0.15f;
        [Tooltip("Parada con fuerza 1 (s)")]
        public float paradaFuerte = 0.12f;
        [Tooltip("Parada mínima cuando se supera el umbral (s)")]
        public float paradaMinima = 0.03f;
        [Tooltip("Tope de la parada (remates, jefes: la 'boss stop')")]
        public float paradaMaxima = 0.3f;
        [Tooltip("Curva: 1 lineal, más de 1 deja los golpes medianos cortos y los fuertes largos")]
        public float paradaExponente = 1.4f;
        [Tooltip("Lo que tiembla el golpeado durante la parada (m, por cada metro de su tamaño), y se apaga")]
        public float temblorGolpeado = 0.04f;

        [Header("Cámara (trauma)")]
        [Tooltip("Trauma que suma un golpe de fuerza 1 justo al lado de la cámara")]
        public float traumaPorFuerza = 0.45f;
        [Tooltip("Trauma que se pierde por segundo")]
        public float traumaCaida = 1.4f;
        [Tooltip("Giro máximo de la cámara con trauma 1 (grados): cabeceo, guiñada, alabeo")]
        public Vector3 sacudidaGrados = new Vector3(4f, 4f, 6f);
        [Tooltip("Desplazamiento máximo con trauma 1 (m por cada unidad de Criatura.Escala)")]
        public float sacudidaMetros = 0.25f;
        [Tooltip("Frecuencia del ruido de la sacudida (Hz)")]
        public float sacudidaFrecuencia = 18f;
        [Tooltip("Distancia (en tamaños de la criatura) a la que un impacto da la mitad de trauma")]
        public float traumaMitadA = 3f;

        [Header("Destello y vibración")]
        public bool destello = true;
        [Tooltip("Emisión del destello del golpeado (nits) con fuerza 1")]
        public float destelloNits = 30f;
        public float destelloDuracion = 0.06f;
        [Tooltip("Motores del mando con fuerza 1 (grave, agudo)")]
        public Vector2 vibracion = new Vector2(0.6f, 0.4f);
        public float vibracionDuracion = 0.12f;

        /// <summary>Segundos de parada para una fuerza (0 si no llega al umbral).</summary>
        public float Parada(float fuerza)
        {
            if (fuerza < paradaUmbral) return 0f;
            float t = Mathf.Pow(Mathf.Max(0f, fuerza), paradaExponente);
            return Mathf.Clamp(Mathf.Max(paradaMinima, paradaFuerte * t), 0f, paradaMaxima);
        }

        /// <summary>Un perfil nuevo (en memoria) con los valores de partida de un arquetipo.</summary>
        public static PerfilSensacion Crear(ArquetipoSensacion a)
        {
            var p = CreateInstance<PerfilSensacion>();
            p.name = "Sensacion_" + a;
            switch (a)
            {
                case ArquetipoSensacion.Gigante:
                    p.paradaFuerte = 0.14f; p.paradaMaxima = 0.35f; p.traumaPorFuerza = 0.55f; p.traumaCaida = 1.1f;
                    p.sacudidaGrados = new Vector3(5f, 4f, 7f); p.sacudidaFrecuencia = 14f; p.vibracion = new Vector2(0.8f, 0.4f);
                    break;
                case ArquetipoSensacion.Agil:
                    p.paradaFuerte = 0.08f; p.paradaMinima = 0.025f; p.traumaPorFuerza = 0.3f; p.traumaCaida = 2f;
                    p.sacudidaGrados = new Vector3(2.5f, 2.5f, 3f); p.sacudidaFrecuencia = 24f; p.vibracion = new Vector2(0.3f, 0.6f);
                    break;
                case ArquetipoSensacion.Creciente:
                    // Lo que cambia con el tamaño lo pone Sensaciones (escala de la criatura).
                    p.paradaFuerte = 0.1f; p.traumaPorFuerza = 0.4f; p.traumaCaida = 1.5f;
                    break;
                case ArquetipoSensacion.Volador:
                    p.paradaFuerte = 0.09f; p.traumaPorFuerza = 0.3f; p.traumaCaida = 1.8f;
                    p.sacudidaGrados = new Vector3(2.5f, 2f, 5f); p.sacudidaMetros = 0.1f;
                    break;
                case ArquetipoSensacion.Acuatico:
                    p.paradaFuerte = 0.11f; p.traumaPorFuerza = 0.35f; p.traumaCaida = 0.9f;
                    p.sacudidaGrados = new Vector3(3f, 3f, 4f); p.sacudidaFrecuencia = 7f; p.vibracion = new Vector2(0.7f, 0.15f);
                    break;
                case ArquetipoSensacion.Arcade:
                    p.paradaFuerte = 0.07f; p.paradaMinima = 0.04f; p.paradaExponente = 1f; p.traumaPorFuerza = 0.2f;
                    p.traumaCaida = 2.5f; p.sacudidaGrados = new Vector3(1.5f, 1.5f, 2f); p.sacudidaMetros = 0.08f;
                    break;
            }
            return p;
        }
    }
}
