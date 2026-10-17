using System.Collections.Generic;
using UnityEngine;

namespace Guardianes.Juego
{
    /// <summary>
    /// LOS IDIOMAS: el juego se escribe en espanol y cada texto que se ensena pasa por T, que lo traduce al idioma de
    /// las opciones (OpcionesJuego.Idioma) si esta en las tablas; si no, sale en espanol. Las tablas son ficheros de
    /// texto en Resources/Textos (los del paquete y los de cada juego): una linea por texto, "espanol" TAB "traduccion"
    /// (las lineas que empiezan por # no cuentan; \n es un salto de linea), con el idioma en el nombre del fichero
    /// (guardianes.en.txt). Los textos con huecos se traducen con F (string.Format sobre la plantilla traducida).
    /// </summary>
    public static class Textos
    {
        static readonly Dictionary<string, Dictionary<string, string>> _tablas = new Dictionary<string, Dictionary<string, string>>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reiniciar() => _tablas.Clear();

        /// <summary>El texto en el idioma del jugador (o el espanol si no esta traducido).</summary>
        public static string T(string es)
        {
            if (string.IsNullOrEmpty(es)) return es;
            string idioma = OpcionesJuego.Idioma;
            if (string.IsNullOrEmpty(idioma) || idioma == "es") return es;
            var tabla = Tabla(idioma);
            return tabla.TryGetValue(es, out var t) ? t : es;
        }

        /// <summary>Una plantilla con huecos ({0}, {1}...) traducida y rellena.</summary>
        public static string F(string es, params object[] huecos) => string.Format(T(es), huecos);

        /// <summary>Esta traducido (las pruebas, para contar lo que falta).</summary>
        public static bool Traducido(string es, string idioma) => Tabla(idioma).ContainsKey(es);

        static Dictionary<string, string> Tabla(string idioma)
        {
            if (_tablas.TryGetValue(idioma, out var t)) return t;
            t = new Dictionary<string, string>();
            _tablas[idioma] = t;
            foreach (var f in Resources.LoadAll<TextAsset>("Textos"))
            {
                if (!f.name.EndsWith("." + idioma)) continue;
                foreach (var linea in f.text.Split('\n'))
                {
                    var l = linea.TrimEnd('\r');
                    if (l.Length == 0 || l[0] == '#') continue;
                    int tab = l.IndexOf('\t');
                    if (tab <= 0) continue;
                    t[l.Substring(0, tab).Replace("\\n", "\n")] = l.Substring(tab + 1).Replace("\\n", "\n");
                }
            }
            return t;
        }
    }
}
