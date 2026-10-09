using System;
using Guardianes.Criaturas;
using Guardianes.Juego;
using UnityEngine;

namespace Guardianes.Misiones
{
    /// <summary>
    /// Un paso de una mision. El director lo empieza, le pregunta cada fotograma si se ha cumplido (o
    /// si ha fallado) y pinta su texto, su progreso y el marcador del HUD. Las frases de 'dialogo'
    /// salen al empezar y las de 'alCumplir' al acabar (subtitulos, sin parar el juego).
    /// Cada juego anade los suyos (comer, morder, mudar...) heredando de aqui.
    /// </summary>
    public abstract class Objetivo
    {
        public string texto = "";
        public string[] dialogo = new string[0];
        public string[] alCumplir = new string[0];
        /// <summary>Una pista que sale en el HUD si tarda (segundos, texto).</summary>
        public float esperaPista = 45f;
        public string pista;

        protected DirectorMisiones D;
        protected float Inicio;
        bool _pistaDada;

        public virtual void Empezar(DirectorMisiones d)
        {
            D = d;
            Inicio = Time.time;
            _pistaDada = false;
        }

        public abstract bool Cumplido { get; }
        public virtual string Progreso => "";
        /// <summary>Por que se ha perdido la mision (null si va bien).</summary>
        public virtual string Fallo => null;
        public virtual Vector3? Marcador => null;
        /// <summary>Etiqueta del marcador en el HUD.</summary>
        public virtual string EtiquetaMarcador => "";
        public virtual void Actualizar()
        {
            if (!_pistaDada && !string.IsNullOrEmpty(pista) && Time.time - Inicio > esperaPista)
            {
                _pistaDada = true;
                D.Aviso(pista);
            }
        }
        public virtual void Terminar() { }
        public float Tiempo => Time.time - Inicio;

        public Objetivo Dice(params string[] lineas) { dialogo = lineas; return this; }
        public Objetivo AlAcabar(params string[] lineas) { alCumplir = lineas; return this; }
        public Objetivo ConPista(string texto, float segundos = 45f) { pista = texto; esperaPista = segundos; return this; }

        protected Criatura Criatura => D != null ? D.Criatura : Criatura.Jugador;
        protected Vector3 Posicion => Criatura != null ? Criatura.transform.position : Vector3.zero;

        protected static float Plano(Vector3 a, Vector3 b) => Vector2.Distance(new Vector2(a.x, a.z), new Vector2(b.x, b.z));
    }

    /// <summary>Solo dialogo: se cumple cuando acaban de decirse sus frases.</summary>
    public class Narracion : Objetivo
    {
        public Narracion(params string[] lineas) { dialogo = lineas; }
        public override bool Cumplido => D.DialogoTerminado && Tiempo > 1f;
    }

    /// <summary>Esperar unos segundos (que pase algo en el mundo).</summary>
    public class Esperar : Objetivo
    {
        readonly float _segundos;
        public Esperar(string texto, float segundos) { this.texto = texto; _segundos = segundos; }
        public override bool Cumplido => Tiempo >= _segundos;
        public override string Progreso => $"{Mathf.Max(0f, _segundos - Tiempo):F0} s";
    }

    /// <summary>Aguantar un tiempo sin que falle nada (las reglas siguen vigiladas).</summary>
    public class Sobrevivir : Esperar
    {
        public Sobrevivir(string texto, float segundos) : base(texto, segundos) { }
    }

    /// <summary>Llegar a un punto (en el plano, a menos de 'radio'). El punto se calcula al empezar.</summary>
    public class Llegar : Objetivo
    {
        readonly Func<Vector3> _donde;
        readonly float _radio;
        public Vector3 Punto { get; private set; }
        public float Radio => _radio;

        public Llegar(string texto, Func<Vector3> donde, float radio = 3f) { this.texto = texto; _donde = donde; _radio = radio; }
        public override void Empezar(DirectorMisiones d)
        {
            base.Empezar(d);
            Punto = RecursosJuego.AlSuelo(_donde());
        }
        public override bool Cumplido => Plano(Posicion, Punto) < _radio;
        public override string Progreso => $"{Plano(Posicion, Punto):F0} m";
        public override Vector3? Marcador => Punto;
    }

    /// <summary>
    /// Un objetivo hecho con funciones (para lo que solo sirve una vez): se cumple cuando 'hecho'
    /// devuelve true; progreso, marcador y fallo opcionales.
    /// </summary>
    public class Condicion : Objetivo
    {
        readonly Func<bool> _hecho;
        readonly Func<string> _progreso, _fallo;
        readonly Func<Vector3?> _marcador;
        readonly Action<DirectorMisiones> _alEmpezar;

        public Condicion(string texto, Func<bool> hecho, Func<string> progreso = null, Func<Vector3?> marcador = null,
                         Func<string> fallo = null, Action<DirectorMisiones> alEmpezar = null)
        {
            this.texto = texto;
            _hecho = hecho;
            _progreso = progreso;
            _marcador = marcador;
            _fallo = fallo;
            _alEmpezar = alEmpezar;
        }

        public override void Empezar(DirectorMisiones d)
        {
            base.Empezar(d);
            _alEmpezar?.Invoke(d);
        }
        public override bool Cumplido => _hecho();
        public override string Progreso => _progreso != null ? _progreso() : "";
        public override Vector3? Marcador => _marcador?.Invoke();
        public override string Fallo => _fallo?.Invoke();
    }

    /// <summary>Llegar a un lugar por descubrir (Descubrimiento con ese tipo).</summary>
    public class Descubrir : Objetivo
    {
        readonly int _tipo;
        Descubrimiento _d;
        public Descubrir(string texto, int tipo) { this.texto = texto; _tipo = tipo; }
        public override void Empezar(DirectorMisiones d)
        {
            base.Empezar(d);
            foreach (var x in Descubrimiento.Todos) if (x.tipo == _tipo) _d = x;
        }
        public override bool Cumplido => _d == null || (_d.Descubierto && Plano(Posicion, _d.transform.position) < _d.radio * 1.2f);
        public override Vector3? Marcador => _d != null ? _d.transform.position : (Vector3?)null;
        public override string Progreso => _d != null ? $"{Plano(Posicion, _d.transform.position):F0} m" : "";
    }
}
