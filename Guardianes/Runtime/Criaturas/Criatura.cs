using System;
using System.Collections.Generic;
using Guardianes.Combate;
using Guardianes.Mundo;
using UnityEngine;

namespace Guardianes.Criaturas
{
    /// <summary>Por que gana algo la criatura (cada juego lo traduce: furia, crecimiento, afinidad...).</summary>
    public enum MotivoPremio
    {
        InstalacionDestruida,
        ZonaLiberada,
        Helicoptero,
        LugarDescubierto,
        HumanoAbatido,
        HumanoAhuyentado,
        Purificacion,
        Otro,
    }

    public struct Premio
    {
        public MotivoPremio motivo;
        public float cantidad;
        public Vector3 donde;

        public Premio(MotivoPremio motivo, float cantidad, Vector3 donde = default)
        {
            this.motivo = motivo;
            this.cantidad = cantidad;
            this.donde = donde;
        }
    }

    /// <summary>
    /// La criatura que controla el jugador (Kong, el basilisco y las que vengan). Los humanos, los
    /// helicopteros, la camara, los efectos, el sonido y el HUD la buscan aqui en vez de conocer a
    /// cada criatura. Todo lo que depende del tamano (a que altura apuntan las armas, cuanto asusta,
    /// lo lejos que va la camara) sale de Tamano y AlturaBlanco.
    /// </summary>
    [RequireComponent(typeof(Salud))]
    public abstract class Criatura : MonoBehaviour
    {
        /// <summary>Tamano de referencia de la saga (Kong de pie, m): con el las escalas valen 1.</summary>
        public const float TamanoReferencia = 17.5f;

        [Tooltip("La controla el jugador (la buscan los enemigos, la camara y el HUD)")]
        public bool esJugador = true;

        public static Criatura Jugador { get; private set; }
        public static readonly List<Criatura> Todas = new List<Criatura>();

        Salud _salud;
        public Salud Salud => _salud != null ? _salud : (_salud = GetComponent<Salud>());

        // ------------------------------------------------------------------ Lo que cada criatura dice de si
        /// <summary>Velocidad (m/s).</summary>
        public abstract Vector3 Velocidad { get; }
        /// <summary>Tamano caracteristico (m): el alto de Kong, el largo de la serpiente.</summary>
        public abstract float Tamano { get; }
        /// <summary>Altura (m sobre su posicion) a la que apuntan las armas: el pecho, la cabeza.</summary>
        public abstract float AlturaBlanco { get; }
        /// <summary>Alto del cuerpo (m) por el que se reparten los disparos.</summary>
        public virtual float AltoCuerpo => AlturaBlanco * 1.1f;
        /// <summary>Punto al que miran y disparan los humanos.</summary>
        public virtual Vector3 PuntoBlanco => transform.position + Vector3.up * AlturaBlanco;
        /// <summary>
        /// A donde apunta un tirador desde 'desde' (el cuerpo que ve: un humano por disparo, el canon por rafaga,
        /// cada cohete). Falso: cada tirador apunta como siempre (a la altura de AlturaBlanco, con su reparto).
        /// </summary>
        public virtual bool PuntoDisparo(Vector3 desde, out Vector3 punto)
        {
            punto = default;
            return false;
        }
        /// <summary>Radio del cuerpo (m) en planta.</summary>
        public virtual float Radio => Tamano * 0.35f;
        public virtual bool Corriendo => false;
        /// <summary>No se la ve (escondida en la hojarasca, bajo el agua, bajo tierra).</summary>
        public virtual bool Escondida => false;
        /// <summary>Escala respecto a Kong (1 = 17,5 m): la usan efectos y sonido.</summary>
        public float Escala => Tamano / TamanoReferencia;

        /// <summary>Gana algo (instalacion destruida, zona liberada...). Cada juego decide que da.</summary>
        public virtual void Premiar(Premio p) { }

        /// <summary>Celebra (al descubrir un lugar): Kong ruge, la serpiente se alza y sisea.</summary>
        public virtual void Celebrar() { }

        protected virtual void OnEnable()
        {
            if (!Todas.Contains(this)) Todas.Add(this);
            if (esJugador) Jugador = this;
        }

        protected virtual void OnDisable()
        {
            Todas.Remove(this);
            if (Jugador == this) Jugador = null;
        }

        /// <summary>La criatura del jugador aunque aun no se haya activado (Start de otros sistemas).</summary>
        public static Criatura Buscar()
        {
            if (Jugador != null) return Jugador;
            foreach (var c in FindObjectsByType<Criatura>(FindObjectsSortMode.None))
                if (c.esJugador) return c;
            return null;
        }

        // ------------------------------------------------------------------ Eventos para los demas
        /// <summary>Intimida (rugido, siseo): los humanos del radio huyen y los helicopteros cercanos
        /// pierden el control (donde, radio).</summary>
        public static event Action<Vector3, float> AlIntimidar;
        /// <summary>Onda en cono (la palmada de Kong): centro, direccion, alcance, medio angulo (grados).</summary>
        public static event Action<Vector3, Vector3, float, float> AlOndaCono;
        /// <summary>Purifica la tierra envenenada (centro, radio): efectos y avisos.</summary>
        public static event Action<Vector3, float> AlPurificar;
        /// <summary>Un apoyo en el suelo (quien, extremidad, punto, rapidez m/s): polvo y sonido.</summary>
        public static event Action<Criatura, int, Vector3, float> AlPisar;

        public static void Intimidar(Vector3 donde, float radio) => AlIntimidar?.Invoke(donde, radio);

        public static void OndaCono(Vector3 centro, Vector3 direccion, float alcance, float medioAngulo) =>
            AlOndaCono?.Invoke(centro, direccion, alcance, medioAngulo);

        /// <summary>Limpia el suelo del radio (poco a poco, en 'duracion' s) y avisa a los efectos.</summary>
        public static void Purificar(Vector3 centro, float radio, float duracion = 4f)
        {
            if (TerrenoMundo.Instancia != null) TerrenoMundo.Instancia.Purificar(centro, radio, duracion);
            AlPurificar?.Invoke(centro, radio);
        }

        public void Pisar(int extremidad, Vector3 punto, float rapidez) => AlPisar?.Invoke(this, extremidad, punto, rapidez);
    }

    /// <summary>
    /// Lo que la camara de la criatura lee de los controles (cada juego tiene los suyos).
    /// </summary>
    public interface IEntradaCamara
    {
        /// <summary>Grados de camara este fotograma (raton ya escalado + stick).</summary>
        Vector2 Mirar(float sensibilidadRaton, float sensibilidadMando);
        /// <summary>Rueda o cruceta: positivo acerca.</summary>
        float Zoom { get; }
    }
}
