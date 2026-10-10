using System.Collections.Generic;
using UnityEngine;

namespace Guardianes.Sensacion
{
    /// <summary>
    /// Buffer de entrada: una pulsación que llega un poco antes de poder hacerse (en la recuperación
    /// de otro ataque, en el aire justo antes de tocar el suelo) no se pierde, se hace en cuanto se
    /// pueda dentro de la ventana. Cada criatura lo usa con sus propias acciones (enteros o un enum
    /// suyo convertido): Pulsar al leer la entrada y Consumir donde la acción ya es posible.
    /// Tiempo sin escalar: funciona igual durante las paradas de los golpes.
    /// </summary>
    public class BufferEntrada
    {
        /// <summary>Ventana por defecto (s): entre 0,1 y 0,2 según el ritmo del juego.</summary>
        public float ventana = 0.15f;

        readonly Dictionary<int, float> _pulsada = new Dictionary<int, float>();

        /// <summary>Reloj (sobrescribible en las pruebas).</summary>
        public System.Func<float> reloj = () => Time.unscaledTime;

        public void Pulsar(int accion)
        {
            _pulsada[accion] = reloj();
            Medidor.Pedir(accion);
        }

        /// <summary>Hay una pulsación de esta acción dentro de la ventana (sin gastarla).</summary>
        public bool Pendiente(int accion, float ventanaPropia = -1f) =>
            _pulsada.TryGetValue(accion, out var t) && reloj() - t <= (ventanaPropia >= 0f ? ventanaPropia : ventana);

        /// <summary>Si hay una pulsación pendiente la gasta y devuelve verdad: la acción se hace ya.</summary>
        public bool Consumir(int accion, float ventanaPropia = -1f)
        {
            if (!Pendiente(accion, ventanaPropia)) return false;
            _pulsada.Remove(accion);
            Medidor.Responder(accion);
            return true;
        }

        public void Olvidar(int accion) => _pulsada.Remove(accion);
        public void Limpiar() => _pulsada.Clear();
    }

    /// <summary>
    /// Margen de apoyo (coyote time): durante un instante después de perder el apoyo (salir de un
    /// borde, de una rama, del agua) todavía se puede saltar, lanzarse o agarrarse como si no se
    /// hubiera perdido. Vale para cualquier apoyo: suelo, tronco, pared, superficie del agua, el aire
    /// de una corriente térmica.
    /// </summary>
    public class MargenApoyo
    {
        /// <summary>Margen por defecto (s): entre 0,08 y 0,15.</summary>
        public float ventana = 0.12f;

        public System.Func<float> reloj = () => Time.unscaledTime;

        float _ultimoApoyo = float.NegativeInfinity;
        bool _gastado;

        /// <summary>Cada fotograma, con si ahora hay apoyo.</summary>
        public void Actualizar(bool apoyado)
        {
            if (apoyado) { _ultimoApoyo = reloj(); _gastado = false; }
        }

        /// <summary>Hay apoyo o se perdió hace menos que la ventana (y no se ha gastado ya el margen).</summary>
        public bool Puede(float ventanaPropia = -1f) =>
            !_gastado && reloj() - _ultimoApoyo <= (ventanaPropia >= 0f ? ventanaPropia : ventana);

        /// <summary>Se ha usado el margen (un salto): no vale otra vez hasta volver a apoyarse.</summary>
        public void Gastar() => _gastado = true;
    }
}
