using System.Collections.Generic;
using Guardianes.Criaturas;
using Guardianes.UI;
using UnityEngine;

namespace Guardianes.Juego
{
    /// <summary>
    /// Un lugar del mundo por descubrir. Cuando la criatura llega: su recuerdo en pantalla, la
    /// criatura lo celebra (Kong ruge), la tierra le da fuerza (mas vida maxima, vida llena y su
    /// premio) y cuenta para el HUD.
    /// </summary>
    public class Descubrimiento : MonoBehaviour
    {
        public string nombre;
        [TextArea] public string recuerdo;
        [Tooltip("Tipo de lugar del juego (su enum, como entero)")]
        public int tipo;
        public float radio = 100f;
        [Tooltip("Vida maxima que gana la criatura (fraccion)")]
        public float premioVida = 0.08f;

        public bool Descubierto { get; private set; }
        public static readonly List<Descubrimiento> Todos = new List<Descubrimiento>();
        public static event System.Action<Descubrimiento> AlDescubrir;

        Criatura _criatura;

        void OnEnable() => Todos.Add(this);
        void OnDisable() => Todos.Remove(this);

        void Start() => _criatura = Criatura.Buscar();

        void Update()
        {
            if (_criatura == null) _criatura = Criatura.Jugador;
            if (Descubierto || _criatura == null || _criatura.Salud.Muerto) return;
            Vector3 d = _criatura.transform.position - transform.position;
            d.y = 0f;
            if (d.sqrMagnitude < radio * radio) Descubrir();
        }

        public void Descubrir()
        {
            if (Descubierto) return;
            Descubierto = true;
            if (_criatura != null)
            {
                _criatura.Salud.maxima *= 1f + premioVida;
                _criatura.Salud.Curar(_criatura.Salud.maxima);
                _criatura.Premiar(new Premio(MotivoPremio.LugarDescubierto, 40f, transform.position));
                _criatura.Celebrar();
            }
            var hud = FindAnyObjectByType<HudJuego>();
            if (hud != null) hud.Titulo(nombre.ToUpperInvariant(), recuerdo, 8f);
            AlDescubrir?.Invoke(this);
        }

        /// <summary>Ya descubierto en una mision anterior: cuenta y da la fuerza, sin aviso.</summary>
        public void Marcar()
        {
            if (Descubierto) return;
            Descubierto = true;
            if (_criatura == null) _criatura = Criatura.Buscar();
            if (_criatura != null)
            {
                _criatura.Salud.maxima *= 1f + premioVida;
                _criatura.Salud.Curar(_criatura.Salud.maxima);
            }
        }

        public static int Descubiertos()
        {
            int n = 0;
            foreach (var d in Todos) if (d.Descubierto) n++;
            return n;
        }
    }
}
