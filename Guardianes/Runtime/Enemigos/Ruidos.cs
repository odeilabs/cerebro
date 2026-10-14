using System;
using UnityEngine;

namespace Guardianes.Enemigos
{
    /// <summary>Lo que suena (de menos a mas alarmante).</summary>
    public enum TipoRuido { Movimiento, Agua, Rotura, Golpe, Grito, Disparo, Explosion }

    /// <summary>Un sonido: donde, hasta donde se oye, que es y si lo hace la criatura del jugador.</summary>
    public struct Sonido
    {
        public Vector3 donde;
        public float radio;
        public TipoRuido tipo;
        public bool deCriatura;
        public GameObject origen;
    }

    /// <summary>
    /// EL OIDO del mundo: lo que hace ruido lo emite aqui (la criatura al moverse o romper, un disparo, una
    /// explosion) y lo oye quien quiera (los humanos: una pista que investigar; los animales de cada juego:
    /// huir). Cada juego decide que ruido hace su criatura y cuanto; el paquete emite los disparos de los
    /// humanos. Sin oyentes no cuesta nada.
    /// </summary>
    public static class Ruidos
    {
        public static event Action<Sonido> AlOir;

        /// <summary>El ultimo ruido (las pruebas y la depuracion).</summary>
        public static Sonido Ultimo { get; private set; }

        public static void Emitir(Vector3 donde, float radio, TipoRuido tipo, bool deCriatura, GameObject origen = null)
        {
            if (radio <= 0.05f) return;
            var r = new Sonido { donde = donde, radio = radio, tipo = tipo, deCriatura = deCriatura, origen = origen };
            Ultimo = r;
            AlOir?.Invoke(r);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reiniciar() => Ultimo = default;
    }
}
