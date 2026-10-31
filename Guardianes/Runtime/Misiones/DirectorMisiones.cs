using System;
using System.Collections;
using System.Collections.Generic;
using Guardianes.Criaturas;
using Guardianes.Enemigos;
using Guardianes.Mundo;
using Guardianes.UI;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Guardianes.Misiones
{
    /// <summary>
    /// Juega una mision en la escena: monta su escenario (Preparar), pone la hora, cuenta la
    /// introduccion con la criatura quieta, lleva los objetivos uno tras otro con sus dialogos, vigila
    /// las reglas y al acabar cuenta el desenlace y hace balance. Si falla, lo dice y la repite.
    /// Cada juego hereda para colocar a su criatura, bloquear sus controles, encadenar sus misiones y
    /// anadir sus filas al balance; los objetivos propios heredan de Objetivo. Para retomar una mision a medias
    /// (una partida guardada en un objetivo), PrimerObjetivo: los de antes se saltan (Objetivo.Saltar) y no se
    /// cuenta la introduccion; AlEmpezarObjetivo, para guardar al empezar cada uno.
    /// </summary>
    public class DirectorMisiones : MonoBehaviour
    {
        public static DirectorMisiones Instancia { get; private set; }
        public static event Action<DirectorMisiones> AlEmpezarMision, AlCompletarMision, AlFallarMision;

        [Header("Flujo")]
        [Tooltip("Al completar una mision, pasa a la siguiente (lo decide el juego en AlTerminar)")]
        public bool encadenar = true;
        [Tooltip("Al fallar, repite la mision")]
        public bool reintentar = true;
        [Tooltip("Segundos de las pantallas de mision completada y fallida (Intro las pasa)")]
        public float esperaPantalla = 20f;

        [Header("Textos (sin tildes si la fuente del juego no las tiene)")]
        public string textoMision = "Mision";
        public string textoObjetivo = "Objetivo";
        public string textoCompletada = "MISION COMPLETADA";
        public string textoFallida = "MISION FALLIDA";
        public string textoReintentar = "Intro: reintentar";
        public string textoContinuar = "Intro: continuar";
        public string textoHecho = "Hecho: ";

        public Mision Mision { get; private set; }
        public Criatura Criatura { get; protected set; }
        public HudJuego Hud { get; protected set; }
        /// <summary>Donde cuelga el escenario de la mision (se borra al empezar otra o al repetirla: al acabar, sigue en el mundo).</summary>
        public Transform Raiz { get; private set; }
        public Objetivo Actual { get; private set; }
        public int NumeroObjetivo { get; private set; }
        public bool EnMision => _rutina != null;
        public bool EnCinematica { get; private set; }
        public bool Completado { get; private set; }
        public bool Fallido { get; private set; }
        public string MotivoFallo { get; private set; }
        public int Intentos { get; private set; }
        public float Duracion => (_fin > 0f ? _fin : Time.time) - _inicio;

        // El balance (lo que ha pasado en la mision).
        public int VistaPor { get; private set; }
        public int Bajas { get; private set; }
        public int CivilesMuertos { get; private set; }
        public int Ahuyentados { get; private set; }
        public int Aterrados { get; private set; }

        /// <summary>Las pruebas en batch: cinematicas y pantallas cortas.</summary>
        protected bool Rapido => Application.isBatchMode;

        readonly Queue<string> _dialogos = new Queue<string>();
        float _finDialogo, _inicio, _fin;
        Coroutine _rutina;
        Mision _anterior;

        public bool DialogoTerminado => _dialogos.Count == 0 && Time.unscaledTime >= _finDialogo;

        protected virtual void Awake() => Instancia = this;

        protected virtual void OnDestroy()
        {
            if (Instancia == this) Instancia = null;
        }

        protected virtual void OnEnable()
        {
            Humano.AlVer += Visto;
            Humano.AlMorir += Muerto;
            Humano.AlAhuyentar += Ahuyentado;
            Humano.AlAterrarse += Aterrado;
        }

        protected virtual void OnDisable()
        {
            Humano.AlVer -= Visto;
            Humano.AlMorir -= Muerto;
            Humano.AlAhuyentar -= Ahuyentado;
            Humano.AlAterrarse -= Aterrado;
        }

        void Visto(Humano h) { if (EnMision && !h.protegido) VistaPor++; }
        void Ahuyentado(Humano h) { if (EnMision) Ahuyentados++; }
        void Aterrado(Humano h) { if (EnMision) Aterrados++; }

        void Muerto(Humano h)
        {
            if (!EnMision || Criatura == null) return;
            var s = h.GetComponent<Combate.Salud>();
            if (s == null || !Regla.CulpaDeLaCriatura(s.UltimoDanio, Criatura)) return;
            if ((h.civil && h.inocente) || h.protegido) CivilesMuertos++;
            else Bajas++;
        }

        protected virtual void Buscar()
        {
            if (Criatura == null) Criatura = Criatura.Buscar();
            if (Hud == null) Hud = FindAnyObjectByType<HudJuego>();
        }

        // ------------------------------------------------------------------ Jugar
        /// <summary>Empieza (o repite) una mision.</summary>
        public void Jugar(Mision m)
        {
            Detener();
            _rutina = StartCoroutine(Ejecutar(m));
        }

        /// <summary>Deja la mision a medias (sin pantallas) y borra su escenario.</summary>
        public void Detener()
        {
            if (_rutina != null) StopCoroutine(_rutina);
            _rutina = null;
            if (Actual != null) Actual.Terminar();
            if (Mision != null) foreach (var r in Mision.reglas) r.Terminar();
            Actual = null;
            if (EnCinematica) Bloquear(false);
            EnCinematica = false;
            if (Hud != null) { Hud.Objetivo(""); Hud.Marcador(null); }
        }

        IEnumerator Ejecutar(Mision m)
        {
            Buscar();
            Intentos = m == _anterior ? Intentos + 1 : 1;
            _anterior = m;
            Mision = m;
            Completado = Fallido = false;
            MotivoFallo = null;
            VistaPor = Bajas = CivilesMuertos = Ahuyentados = Aterrados = 0;
            _fin = 0f;
            _dialogos.Clear();
            LimpiarEscenario();
            Raiz = new GameObject($"Mision {m.numero}").transform;

            EnCinematica = true;
            Bloquear(true);
            if (Hud != null) Hud.Fundido(1f, 0f);
            yield return null;
            if (m.hora >= 0f && CicloDia.Instancia != null)
            {
                CicloDia.Instancia.FijarHora(m.hora);
                CicloDia.Instancia.avanzar = m.horaAvanza;
            }
            yield return Preparar(m);
            m.preparar?.Invoke(this);
            foreach (var r in m.reglas) r.Empezar(this);
            AlEmpezarMision?.Invoke(this);
            Debug.Log($"[GD] Mision {m.numero} '{m.titulo}' (intento {Intentos}): {m.objetivos.Count} objetivos, {m.reglas.Count} reglas" +
                      (m.hora >= 0f ? $", a las {CicloDia.Reloj(m.hora)}" : ""));

            // Retomada a medias: los objetivos ya hechos se saltan y la introduccion no se vuelve a contar.
            int primero = Mathf.Clamp(PrimerObjetivo(m), 0, Mathf.Max(0, m.objetivos.Count - 1));
            for (int i = 0; i < primero; i++) m.objetivos[i].Saltar(this);
            if (primero == 0) yield return Cinematica(m.introduccion, true);
            else yield return Cinematica(null, true);
            if (Hud != null)
            {
                string titulo = Guardianes.Juego.Textos.T(m.titulo), acto = Guardianes.Juego.Textos.T(m.acto), rotulo = Rotulo(m);
                Hud.Nivel($"{acto}\n{rotulo}: {titulo}");
                Hud.Titulo(titulo.ToUpperInvariant(), $"{acto}  ·  {rotulo}  {m.Etiquetas}", 4f);
            }
            _inicio = Time.time;

            for (NumeroObjetivo = primero; NumeroObjetivo < m.objetivos.Count; NumeroObjetivo++)
            {
                var o = m.objetivos[NumeroObjetivo];
                Actual = o;
                AlEmpezarObjetivo(NumeroObjetivo, o, primero);
                o.Empezar(this);
                Decir(o.dialogo);
                Debug.Log($"[GD] Mision {m.numero}: objetivo {NumeroObjetivo + 1}/{m.objetivos.Count}: {o.GetType().Name} '{o.texto}'");
                while (!o.Cumplido)
                {
                    o.Actualizar();
                    string fallo = o.Fallo ?? Reglas(m);
                    if (fallo != null) { o.Terminar(); yield return Fallar(fallo); yield break; }
                    Pintar(m, o);
                    yield return null;
                }
                o.Terminar();
                Debug.Log($"[GD] Mision {m.numero}: objetivo {NumeroObjetivo + 1} cumplido en {o.Tiempo:F0} s");
                Decir(o.alCumplir);
                if (Hud != null && !string.IsNullOrEmpty(o.texto)) Hud.Avisar(Guardianes.Juego.Textos.T(textoHecho) + Guardianes.Juego.Textos.T(o.texto));
                // Que se lea lo que se dice al acabar antes de seguir (el juego no se para).
                float espera = Time.time + 25f;
                while (o.alCumplir.Length > 0 && !DialogoTerminado && Time.time < espera)
                {
                    string fallo = Reglas(m);
                    if (fallo != null) { yield return Fallar(fallo); yield break; }
                    yield return null;
                }
            }
            Actual = null;
            _fin = Time.time;
            foreach (var r in m.reglas) r.Terminar();
            if (Hud != null) { Hud.Objetivo(""); Hud.Marcador(null); }
            yield return new WaitForSeconds(Rapido ? 0.5f : 1.5f);
            yield return Cinematica(m.desenlace, false);
            yield return Balance(m);
        }

        string Reglas(Mision m)
        {
            foreach (var r in m.reglas)
            {
                var f = r.Fallo;
                if (f != null) return f;
            }
            return null;
        }

        void Pintar(Mision m, Objetivo o)
        {
            if (Hud == null) return;
            var sb = new System.Text.StringBuilder();
            sb.Append($"{Guardianes.Juego.Textos.T(textoObjetivo)} {NumeroObjetivo + 1}/{m.objetivos.Count}\n");
            if (!string.IsNullOrEmpty(o.texto)) sb.Append(Guardianes.Juego.Textos.T(o.texto));
            var p = o.Progreso;
            if (!string.IsNullOrEmpty(p)) sb.Append($"  ({p})");
            foreach (var r in m.reglas)
            {
                var e = r.Estado;
                if (!string.IsNullOrEmpty(e)) sb.Append('\n').Append(e);
            }
            Hud.Objetivo(sb.ToString());
            Hud.Marcador(o.Marcador, o.EtiquetaMarcador);
        }

        // ------------------------------------------------------------------ Lo que hace cada juego
        /// <summary>Coloca a la criatura y monta lo del juego antes de la introduccion (la pantalla esta en negro).</summary>
        protected virtual IEnumerator Preparar(Mision m) { yield break; }

        /// <summary>Bloquea o devuelve los controles de la criatura (cinematicas, pantallas).</summary>
        protected virtual void Bloquear(bool si) { }

        /// <summary>Al acabar (completada, o fallida sin reintentar): el juego encadena la siguiente.</summary>
        protected virtual void AlTerminar(bool completada) { }

        /// <summary>El objetivo por el que empieza (0; una partida retomada a medias, el que se guardo).</summary>
        protected virtual int PrimerObjetivo(Mision m) => 0;

        /// <summary>Justo antes de empezar cada objetivo (el autoguardado); 'primero' es por el que se empezo.</summary>
        protected virtual void AlEmpezarObjetivo(int numero, Objetivo o, int primero) { }

        /// <summary>Completada, antes de las filas del balance (desbloquear, guardar, las medallas...).</summary>
        protected virtual void AntesDelBalance(Mision m) { }

        /// <summary>El titulo de la pantalla de mision completada (la ultima de la campana, FIN).</summary>
        protected virtual string TituloCompletada(Mision m) => textoCompletada;

        /// <summary>Lo que dice abajo la pantalla de mision fallida.</summary>
        protected virtual string PieFallida() => reintentar ? textoReintentar : "";

        /// <summary>Lo que hace perder la mision desde fuera de sus objetivos y reglas (la criatura capturada...).</summary>
        public void FallarDesdeFuera(string motivo)
        {
            if (!EnMision || Fallido || Completado || Mision == null) return;
            if (_rutina != null) StopCoroutine(_rutina);
            if (Actual != null) Actual.Terminar();
            _rutina = StartCoroutine(Fallar(motivo));
        }

        /// <summary>Filas del balance.</summary>
        protected virtual List<(string, string)> FilasBalance()
        {
            string t = $"{(int)(Duracion / 60)}:{(int)(Duracion % 60):00}";
            return new List<(string, string)>
            {
                ("Tiempo", t),
                ("Te han visto", VistaPor.ToString()),
                ("Ahuyentados", Ahuyentados.ToString()),
                ("Bajas", Bajas.ToString()),
                ("Inocentes muertos", CivilesMuertos.ToString()),
            };
        }

        protected virtual string Valoracion() => "";
        protected virtual string PieCompletada() => textoContinuar;

        /// <summary>Lo que va delante del titulo (traducido): el rotulo de la mision o, si no tiene, «Mision N».</summary>
        protected string Rotulo(Mision m) =>
            string.IsNullOrEmpty(m.rotulo) ? $"{Guardianes.Juego.Textos.T(textoMision)} {m.numero}" : Guardianes.Juego.Textos.T(m.rotulo);

        // ------------------------------------------------------------------ Dialogos y cinematicas
        /// <summary>Frases de los personajes como subtitulos, una tras otra (el juego sigue).</summary>
        public void Decir(params string[] lineas)
        {
            if (lineas == null) return;
            foreach (var l in lineas) if (!string.IsNullOrEmpty(l)) _dialogos.Enqueue(l);
        }

        public void Aviso(string texto)
        {
            if (Hud != null) Hud.Avisar(texto);
        }

        static float Lectura(string s) => Mathf.Clamp(2.2f + s.Length * 0.055f, 3f, 9f);

        protected virtual void Update()
        {
            if (Hud == null || _dialogos.Count == 0 || Time.unscaledTime < _finDialogo) return;
            var l = _dialogos.Dequeue();
            float s = Rapido ? Mathf.Min(2.5f, Lectura(l)) : Lectura(l);
            Hud.Dialogo(l, s);
            Debug.Log("[GD] Dice: " + l);
            _finDialogo = Time.unscaledTime + s + 0.3f;
        }

        protected static bool Saltar() =>
            (Keyboard.current != null && (Keyboard.current.enterKey.wasPressedThisFrame || Keyboard.current.spaceKey.wasPressedThisFrame)) ||
            (Gamepad.current != null && (Gamepad.current.startButton.wasPressedThisFrame || Gamepad.current.buttonSouth.wasPressedThisFrame));

        /// <summary>Lineas en el centro de la pantalla con la criatura quieta (Intro o Espacio las pasan).</summary>
        /// <summary>Lo que se oscurece la pantalla en las cinematicas (cada juego: con plano de cine, menos).</summary>
        protected virtual float OscuridadCinematica => 0.6f;

        protected IEnumerator Cinematica(string[] lineas, bool desdeNegro)
        {
            EnCinematica = true;
            Bloquear(true);
            if (lineas != null && lineas.Length > 0 && Hud != null)
            {
                if (desdeNegro) Hud.Fundido(OscuridadCinematica, 2f);
                else Hud.Fundido(OscuridadCinematica, 1.5f);
                foreach (var l in lineas)
                {
                    float s = Rapido ? 1.2f : Lectura(l) + 0.6f;
                    Hud.Narrar(l, s);
                    Debug.Log("[GD] Narra: " + l);
                    float fin = Time.unscaledTime + s;
                    yield return null;
                    while (Time.unscaledTime < fin && !Saltar()) yield return null;
                }
                Hud.Narrar("", 0f);
            }
            if (Hud != null) Hud.Fundido(0f, 1.2f);
            Bloquear(false);
            EnCinematica = false;
        }

        // ------------------------------------------------------------------ Final y fallo
        IEnumerator Balance(Mision m)
        {
            Completado = true;
            EnCinematica = true;
            Bloquear(true);
            AntesDelBalance(m);
            var filas = FilasBalance();
            var sb = new System.Text.StringBuilder();
            foreach (var f in filas) sb.Append($"{f.Item1} {f.Item2} | ");
            string valoracion = Valoracion();
            Debug.Log($"[GD] Mision {m.numero} '{m.titulo}' COMPLETADA. {sb}{valoracion}");
            AlCompletarMision?.Invoke(this);
            if (Hud != null)
                Hud.PantallaMision(true, TituloCompletada(m), $"{Guardianes.Juego.Textos.T(m.acto)}  ·  {Rotulo(m)}: {Guardianes.Juego.Textos.T(m.titulo)}", filas.ToArray(), valoracion, PieCompletada());
            yield return Esperar();
            if (Hud != null) Hud.PantallaMision(false);
            Bloquear(false);
            EnCinematica = false;
            _rutina = null;
            AlTerminar(true);
        }

        IEnumerator Fallar(string motivo)
        {
            Fallido = true;
            MotivoFallo = motivo;
            _fin = Time.time;
            foreach (var r in Mision.reglas) r.Terminar();
            EnCinematica = true;
            Bloquear(true);
            Debug.LogWarning($"[GD] Mision {Mision.numero} '{Mision.titulo}' FALLADA en el objetivo {NumeroObjetivo + 1}: {motivo}");
            AlFallarMision?.Invoke(this);
            if (Hud != null)
            {
                Hud.Marcador(null);
                Hud.Objetivo("");
                Hud.PantallaCaida(true, textoFallida, motivo, PieFallida());
            }
            yield return Esperar();
            if (Hud != null) Hud.PantallaCaida(false);
            Actual = null;
            _rutina = null;
            if (reintentar) Jugar(Mision);
            else
            {
                Bloquear(false);
                EnCinematica = false;
                AlTerminar(false);
            }
        }

        IEnumerator Esperar()
        {
            yield return new WaitForSecondsRealtime(Rapido ? 1.5f : 2.5f);
            if (Rapido) yield break;
            float hasta = Time.unscaledTime + esperaPantalla;
            while (Time.unscaledTime < hasta && !Saltar()) yield return null;
        }

        void LimpiarEscenario()
        {
            if (Raiz != null) Destroy(Raiz.gameObject);
            Raiz = null;
        }
    }
}
