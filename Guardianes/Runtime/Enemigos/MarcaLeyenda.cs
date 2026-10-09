using System.Collections.Generic;
using UnityEngine;

namespace Guardianes.Enemigos
{
    /// <summary>
    /// Una marca de la leyenda de la criatura: la camisa que muda una serpiente, una estatua de
    /// piedra, una huella enorme en el barro. Los humanos que pasan cerca la ven una vez y se asustan
    /// (solo con Humano.ConMiedo). Una zona con bastantes marcas acaba vaciandose sola.
    /// </summary>
    public class MarcaLeyenda : MonoBehaviour
    {
        public static readonly List<MarcaLeyenda> Todas = new List<MarcaLeyenda>();

        [Tooltip("A cuantos metros la ven")]
        public float radio = 6f;
        [Tooltip("Miedo que da al verla (una vez por humano)")]
        public float miedo = 20f;

        void OnEnable() => Todas.Add(this);
        void OnDisable() => Todas.Remove(this);
    }
}
