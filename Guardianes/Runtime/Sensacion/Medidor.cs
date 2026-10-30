using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace Guardianes.Sensacion
{
    /// <summary>
    /// Métricas de sensación para las pruebas en batch (y para afinar jugando): la latencia de las
    /// entradas (de Pulsar a la respuesta), el tiempo entre recompensas, el tiempo sin control y los
    /// impactos. Se alimenta solo desde BufferEntrada y Sensaciones.Impacto; cada juego añade Pedir /
    /// Responder en sus entradas propias, Recompensa donde premia y Control donde quita el control.
    /// Tiempo sin escalar. Informe() da una línea para el log ([GD] SENSACION ...).
    /// </summary>
    public static class Medidor
    {
        public static System.Func<float> reloj = () => Time.unscaledTime;

        static readonly Dictionary<int, float> _pedidas = new Dictionary<int, float>();
        static readonly List<float> _latencias = new List<float>();
        static readonly List<float> _entreRecompensas = new List<float>();
        static float _ultimaRecompensa = float.NaN, _sinControlDesde = float.NaN;
        static float _sinControl, _peorSinControl;
        static int _impactos, _recompensas;
        static float _fuerzaImpactos;

        public static void Reiniciar()
        {
            _pedidas.Clear(); _latencias.Clear(); _entreRecompensas.Clear();
            _ultimaRecompensa = _sinControlDesde = float.NaN;
            _sinControl = _peorSinControl = 0f;
            _impactos = _recompensas = 0; _fuerzaImpactos = 0f;
        }

        /// <summary>
        /// Una petición que lleva más que esto sin respuesta no se hizo (el buffer la dejó caducar, estaba en
        /// recarga): no cuenta, y la siguiente de esa acción se mide desde la suya.
        /// </summary>
        public const float PeticionPerdida = 1f;

        /// <summary>El jugador pide una acción.</summary>
        public static void Pedir(int accion)
        {
            float t = reloj();
            if (!_pedidas.TryGetValue(accion, out var antes) || t - antes > PeticionPerdida) _pedidas[accion] = t;
        }

        /// <summary>La acción ya se ve (empieza la animación, sale el golpe).</summary>
        public static void Responder(int accion)
        {
            if (!_pedidas.TryGetValue(accion, out var t)) return;
            float latencia = reloj() - t;
            if (latencia <= PeticionPerdida) _latencias.Add(latencia);
            _pedidas.Remove(accion);
        }

        /// <summary>Algo que premia (XP, una presa, un objetivo, un descubrimiento).</summary>
        public static void Recompensa()
        {
            float t = reloj();
            if (!float.IsNaN(_ultimaRecompensa)) _entreRecompensas.Add(t - _ultimaRecompensa);
            _ultimaRecompensa = t;
            _recompensas++;
        }

        /// <summary>El jugador tiene (o pierde) el control: aturdido, cinemática, derribado.</summary>
        public static void Control(bool tiene)
        {
            float t = reloj();
            if (!tiene && float.IsNaN(_sinControlDesde)) _sinControlDesde = t;
            else if (tiene && !float.IsNaN(_sinControlDesde))
            {
                float d = t - _sinControlDesde;
                _sinControl += d;
                _peorSinControl = Mathf.Max(_peorSinControl, d);
                _sinControlDesde = float.NaN;
            }
        }

        public static void Impacto(float fuerza) { _impactos++; _fuerzaImpactos += fuerza; }

        public static int Impactos => _impactos;
        public static int Recompensas => _recompensas;
        public static float LatenciaMedia => Media(_latencias);
        public static float LatenciaMaxima => Maximo(_latencias);
        public static float EntreRecompensasMedio => Media(_entreRecompensas);
        public static float EntreRecompensasMaximo => Maximo(_entreRecompensas);
        public static float SinControlTotal => _sinControl;
        public static float PeorSinControl => _peorSinControl;

        public static string Informe()
        {
            var sb = new StringBuilder("[GD] SENSACION");
            sb.Append($" latencia {LatenciaMedia * 1000f:0}ms (max {LatenciaMaxima * 1000f:0}, n {_latencias.Count})");
            sb.Append($" | recompensas {_recompensas}, cada {EntreRecompensasMedio:0.0}s (max {EntreRecompensasMaximo:0.0})");
            sb.Append($" | sin control {_sinControl:0.0}s (peor {_peorSinControl:0.0})");
            sb.Append($" | impactos {_impactos} (fuerza media {(_impactos > 0 ? _fuerzaImpactos / _impactos : 0f):0.00})");
            return sb.ToString();
        }

        static float Media(List<float> l) { if (l.Count == 0) return 0f; float s = 0f; foreach (var v in l) s += v; return s / l.Count; }
        static float Maximo(List<float> l) { float m = 0f; foreach (var v in l) m = Mathf.Max(m, v); return m; }
    }
}
