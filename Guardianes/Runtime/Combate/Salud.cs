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
        public bool invulnerable;

        public event Action<DatosDanio> AlDanio;
        public event Action<DatosDanio> AlMorir;

        public bool Muerto { get; private set; }
        public float Fraccion => maxima > 0f ? actual / maxima : 0f;
        public DatosDanio UltimoDanio { get; private set; }

        void Awake() => actual = Mathf.Min(actual, maxima);

        public void Danar(DatosDanio d)
        {
            if (Muerto || invulnerable) return;
            float k = d.tipo == TipoDanio.Bala ? resistenciaBalas
                    : d.tipo == TipoDanio.Canon ? resistenciaCanon
                    : d.tipo == TipoDanio.Golpe || d.tipo == TipoDanio.Onda || d.tipo == TipoDanio.Pisoton ? resistenciaGolpes : 1f;
            d.cantidad *= k;
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
