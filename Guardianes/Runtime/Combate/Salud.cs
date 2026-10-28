using System;
using UnityEngine;

namespace Guardianes.Combate
{
    /// <summary>Bala: fusil. Canon: el de 30 mm de los helicopteros (a un gorila de 18 m le hace dano de verdad;
    /// una bala de fusil, poco).</summary>
    public enum TipoDanio { Golpe, Onda, Pisoton, Bala, Explosion, Fuego, Lanzado, Aplastado, Canon }

    /// <summary>Un impacto: cuanto, donde, hacia donde y con que fuerza empuja.</summary>
    public struct DatosDanio
    {
        public float cantidad;
        public Vector3 punto;
        public Vector3 direccion;
        public float empuje;
        public TipoDanio tipo;
        public GameObject origen;
        /// <summary>Lo que penetra el proyectil (Calibres; 0 = sin calibre: entra siempre, como un golpe).</summary>
        public float calibre;
        /// <summary>El proyectil ha rebotado en el blindaje (Balas): solo hace el restoRebote del dano.</summary>
        public bool rebote;
    }

    /// <summary>
    /// Vida de cualquier cosa que se pueda romper o matar: Kong, humanos, vehiculos, estructuras.
    /// Los demas componentes escuchan AlDanio / AlMorir.
    /// </summary>
    public class Salud : MonoBehaviour
    {
        public float maxima = 100f;
        public float actual = 100f;
        [Tooltip("Multiplicador por tipo de dano (p. ej. el blindaje de un vehiculo frente a balas)")]
        public float resistenciaBalas = 1f;
        public float resistenciaCanon = 1f;
        public float resistenciaGolpes = 1f;
        [Tooltip("Escamas o coraza frente a los proyectiles con calibre: 0, nada rebota")]
        public float blindaje;
        [Tooltip("Lo que hace un proyectil que rebota (fraccion de su dano)")]
        [Range(0f, 1f)] public float restoRebote = 0.06f;
        public bool invulnerable;

        public event Action<DatosDanio> AlDanio;
        public event Action<DatosDanio> AlMorir;

        public bool Muerto { get; private set; }
        public float Fraccion => maxima > 0f ? actual / maxima : 0f;
        public DatosDanio UltimoDanio { get; private set; }

        void Awake() => actual = Mathf.Min(actual, maxima);

        /// <summary>
        /// Probabilidad de que un proyectil de ese calibre entre (0..1): con calibre / blindaje por debajo de
        /// 0,6 rebota siempre y por encima de 1,4 entra siempre. Sin blindaje o sin calibre, 1. Los dados los
        /// tira Balas; aqui no se tira nada.
        /// </summary>
        public float Penetracion(float calibre) =>
            blindaje <= 0f || calibre <= 0f ? 1f : Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.6f, 1.4f, calibre / blindaje));

        public void Danar(DatosDanio d)
        {
            if (Muerto || invulnerable) return;
            // Un rebote solo hace un resto (y siempre avisa: los efectos y la cura lo quieren saber).
            if (d.rebote) d.cantidad *= restoRebote;
            float k = d.tipo == TipoDanio.Bala ? resistenciaBalas
                    : d.tipo == TipoDanio.Canon ? resistenciaCanon
                    : d.tipo == TipoDanio.Golpe || d.tipo == TipoDanio.Onda || d.tipo == TipoDanio.Pisoton ? resistenciaGolpes : 1f;
            d.cantidad *= k;
            // La dificultad: el dano que recibe la criatura del jugador (Opciones).
            var jugador = Guardianes.Criaturas.Criatura.Jugador;
            if (jugador != null && jugador.Salud == this) d.cantidad *= Guardianes.Juego.OpcionesJuego.DanioRecibido;
            if (d.cantidad <= 0f) return;
            actual = Mathf.Max(0f, actual - d.cantidad);
            UltimoDanio = d;
            AlDanio?.Invoke(d);
            if (actual <= 0f)
            {
                Muerto = true;
                AlMorir?.Invoke(d);
            }
        }

        public void Curar(float cantidad)
        {
            if (Muerto) return;
            actual = Mathf.Min(maxima, actual + cantidad);
        }

        /// <summary>Vuelve a la vida con la vida llena (Kong reaparece).</summary>
        public void Revivir()
        {
            Muerto = false;
            actual = maxima;
        }
    }
}
