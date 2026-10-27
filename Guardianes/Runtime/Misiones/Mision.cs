using System;
using System.Collections.Generic;

namespace Guardianes.Misiones
{
    /// <summary>
    /// Una mision: numero, acto, titulo, etiquetas (sigilo, terror...), la hora a la que pasa, lo que
    /// se cuenta antes y despues (pantalla en negro, la criatura quieta), los objetivos en orden y
    /// las reglas que valen para toda ella. 'preparar' monta lo que necesita en el mundo (la llama
    /// el director antes de la introduccion, con la raiz donde colgarlo). 'rotulo' es lo que se ve
    /// delante del titulo en la HUD y en la pantalla de mision (vacio: «Mision N», con su numero).
    /// </summary>
    public class Mision
    {
        public int numero;
        public string acto = "";
        public string titulo = "";
        /// <summary>Lo que se ve en vez de «Mision N» (un encargo, una mision fuera de la cuenta); vacio, el numero.</summary>
        public string rotulo;
        public string[] etiquetas = new string[0];
        /// <summary>Hora del dia (0-24) a la que empieza; negativa = no la toca.</summary>
        public float hora = -1f;
        /// <summary>El reloj avanza durante la mision (si no, la hora se queda fija).</summary>
        public bool horaAvanza;
        public string[] introduccion = new string[0];
        public string[] desenlace = new string[0];
        public readonly List<Objetivo> objetivos = new List<Objetivo>();
        public readonly List<Regla> reglas = new List<Regla>();
        /// <summary>Monta el escenario (el director pasa su raiz: lo que cuelga de ella se borra al empezar otra mision o al repetirla).</summary>
        public Action<DirectorMisiones> preparar;

        public Mision Objetivo(Objetivo o) { objetivos.Add(o); return this; }
        public Mision Regla(Regla r) { reglas.Add(r); return this; }

        public string Etiquetas => etiquetas.Length == 0 ? "" : "[" + string.Join("] [", etiquetas) + "]";
        public override string ToString() => $"{numero}. {titulo}";
    }
}
