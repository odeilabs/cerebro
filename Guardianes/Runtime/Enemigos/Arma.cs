using System.Collections.Generic;
using Guardianes.Combate;
using UnityEngine;

namespace Guardianes.Enemigos
{
    /// <summary>
    /// Las clases de arma (las de los humanos y el canon de los helicopteros). Los valores nuevos van al final:
    /// los prefabs guardan el numero, y el 0 (el fusil de asalto) es el de los prefabs de antes de las armas.
    /// </summary>
    public enum ClaseArma { FusilAsalto, Pistola, Subfusil, Escopeta, Arcabuz, FusilCaza, Ametralladora, Canon }

    /// <summary>
    /// La ficha de un arma. Lo fisico: calibre (Calibres), proyectiles por disparo (los perdigones) y su abanico
    /// (radianes). Como se dispara: dano por disparo (se reparte entre los proyectiles), balas por segundo en
    /// rafaga, dispersion (radianes), rafaga y pausa (s) y alcance del rayo (m). Como se ve y se oye: la escala
    /// del fogonazo y el tono del disparo.
    /// </summary>
    [System.Serializable]
    public struct DatosArma
    {
        public float calibre, danio, cadencia, dispersion, abanico, alcance, fogonazo, tono;
        public int proyectiles;
        public Vector2 rafaga, pausa;
    }

    /// <summary>
    /// El arma que lleva un humano en la mano. Humano.Awake la reconoce con DeHumano: este componente en sus hijos
    /// o, si no lo lleva, el nombre de la malla que cuelga de AC_arma_r (kk_ak47_pbr, gd_glock17_pbr...) por sus
    /// Prefijos; asi el calibre sale del modelo sin rehacer los prefabs. De la ficha, Awake solo toma lo fisico
    /// (calibre, proyectiles y abanico): el dano, la cadencia y la dispersion siguen siendo los del prefab y los de
    /// las misiones. Humano.Armar copia la ficha entera. Cada juego retoca la tabla al arrancar (Ajustar), antes de
    /// que despierten los humanos.
    /// </summary>
    [DisallowMultipleComponent]
    public class Arma : MonoBehaviour
    {
        public ClaseArma clase;

        static DatosArma[] s_Fichas = Serie();
        static readonly List<MeshFilter> s_Mallas = new List<MeshFilter>();

        /// <summary>La tabla de serie (en el orden de ClaseArma).</summary>
        static DatosArma[] Serie() => new[]
        {
            // Fusil de asalto (el AK de 7,62x39, el M4 de 5,56): los numeros de serie de Humano.
            new DatosArma { calibre = Calibres.FusilAsalto, danio = 3.5f, proyectiles = 1, cadencia = 9f, dispersion = 0.035f,
                            rafaga = new Vector2(0.8f, 1.8f), pausa = new Vector2(0.9f, 2.2f), alcance = 180f, fogonazo = 1f, tono = 1f },
            // Pistola o revolver (9 mm, .38).
            new DatosArma { calibre = Calibres.Pistola, danio = 2.5f, proyectiles = 1, cadencia = 3f, dispersion = 0.045f,
                            rafaga = new Vector2(0.8f, 1.6f), pausa = new Vector2(1f, 2.2f), alcance = 60f, fogonazo = 0.5f, tono = 1.4f },
            // Subfusil de 9 mm.
            new DatosArma { calibre = Calibres.Subfusil, danio = 2f, proyectiles = 1, cadencia = 11f, dispersion = 0.06f,
                            rafaga = new Vector2(0.8f, 1.8f), pausa = new Vector2(0.9f, 2.2f), alcance = 80f, fogonazo = 0.6f, tono = 1.3f },
            // Escopeta del 12: nueve perdigones de 0,8. Un fogonazo ancho y un estampido grave (de lejos se parece al fusil
            // de caza: lo que la distingue es como se ve y se oye el tiro).
            new DatosArma { calibre = Calibres.Perdigon, danio = 7.2f, proyectiles = 9, abanico = 0.05f, cadencia = 1.2f, dispersion = 0.04f,
                            rafaga = new Vector2(0.6f, 1.2f), pausa = new Vector2(1.2f, 2.5f), alcance = 45f, fogonazo = 1.4f, tono = 0.72f },
            // Arcabuz de mecha (el de los conquistadores): sin rafagas, un tiro cada tres segundos y pico mientras apunta.
            new DatosArma { calibre = Calibres.Arcabuz, danio = 6f, proyectiles = 1, cadencia = 0.3f, dispersion = 0.06f,
                            rafaga = new Vector2(3f, 6f), pausa = new Vector2(0.5f, 1.5f), alcance = 60f, fogonazo = 1.3f, tono = 0.7f },
            // Fusil de caza mayor (.30-06, .375): un fogonazo estrecho y un chasquido seco.
            new DatosArma { calibre = Calibres.FusilCaza, danio = 9f, proyectiles = 1, cadencia = 0.8f, dispersion = 0.012f,
                            rafaga = new Vector2(1f, 2.5f), pausa = new Vector2(1.5f, 3f), alcance = 250f, fogonazo = 0.85f, tono = 0.95f },
            // Ametralladora de 12,7 (la torreta de un blindado).
            new DatosArma { calibre = Calibres.Ametralladora, danio = 12f, proyectiles = 1, cadencia = 8f, dispersion = 0.03f,
                            rafaga = new Vector2(1f, 2f), pausa = new Vector2(2f, 4f), alcance = 300f, fogonazo = 1.5f, tono = 0.7f },
            // Canon de 30 mm: los numeros de serie del helicoptero (que dispara con los suyos).
            new DatosArma { calibre = Calibres.Canon, danio = 6f, proyectiles = 1, cadencia = 10f, dispersion = 0.02f,
                            rafaga = new Vector2(1.2f, 2.4f), pausa = new Vector2(1.4f, 2.8f), alcance = 364f, fogonazo = 1.8f, tono = 0.62f },
        };

        static readonly (string prefijo, ClaseArma clase)[] PrefijosSerie =
        {
            ("kk_ak47", ClaseArma.FusilAsalto), ("ak47", ClaseArma.FusilAsalto), ("arma_ak47", ClaseArma.FusilAsalto),
            ("arma_fusil", ClaseArma.FusilAsalto), ("bk_fusil", ClaseArma.FusilAsalto),
            ("pistola", ClaseArma.Pistola), ("arma_pistola", ClaseArma.Pistola),
            ("arma_subfusil", ClaseArma.Subfusil), ("thompson", ClaseArma.Subfusil),
            ("arma_thompson", ClaseArma.Subfusil), ("mp40", ClaseArma.Subfusil), ("arma_mp40", ClaseArma.Subfusil),
            ("arma_escopeta", ClaseArma.Escopeta),
            ("m1_garand", ClaseArma.FusilCaza), ("arma_m1_garand", ClaseArma.FusilCaza),
            ("bk_arcabuz", ClaseArma.Arcabuz), ("arcabuz", ClaseArma.Arcabuz), ("arma_arcabuz", ClaseArma.Arcabuz),
            ("ww2_mg42", ClaseArma.Ametralladora), ("bk_ametralladora", ClaseArma.Ametralladora),
            // Las del kit de armas del cerebro (Tools~/arte/armas: gd_<arma>, su malla gd_<arma>_pbr): las dos escopetas por
            // gd_escopeta.
            ("gd_glock17", ClaseArma.Pistola), ("gd_colt_saa", ClaseArma.Pistola), ("gd_mp5", ClaseArma.Subfusil),
            ("gd_ak47", ClaseArma.FusilAsalto), ("gd_escopeta", ClaseArma.Escopeta), ("gd_remington700", ClaseArma.FusilCaza),
            ("gd_m2_browning", ClaseArma.Ametralladora),
        };

        /// <summary>
        /// Como empieza el nombre de cada modelo de arma (el proyecto de AssetsCreator o el del kit de armas del cerebro) y
        /// su clase: gana el prefijo mas largo que encaje, sin mirar mayusculas. Cada juego puede anadir los suyos.
        /// </summary>
        public static readonly List<(string prefijo, ClaseArma clase)> Prefijos = new List<(string, ClaseArma)>(PrefijosSerie);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reiniciar()
        {
            s_Fichas = Serie();
            Prefijos.Clear();
            Prefijos.AddRange(PrefijosSerie);
        }

        /// <summary>La ficha de una clase (la de serie o la que haya puesto el juego con Ajustar).</summary>
        public static DatosArma Ficha(ClaseArma c)
        {
            int i = (int)c;
            return i >= 0 && i < s_Fichas.Length ? s_Fichas[i] : s_Fichas[0];
        }

        /// <summary>Cambia la ficha de una clase (cada juego retoca su tabla al arrancar).</summary>
        public static void Ajustar(ClaseArma c, DatosArma d)
        {
            int i = (int)c;
            if (i >= 0 && i < s_Fichas.Length) s_Fichas[i] = d;
        }

        /// <summary>La clase de un modelo por su nombre (kk_ak47_pbr, gd_escopeta_corredera...); lo que no conoce, fusil de asalto.</summary>
        public static ClaseArma ClaseDe(string modelo) => Reconocer(modelo, out var c) ? c : ClaseArma.FusilAsalto;

        /// <summary>Como ClaseDe, pero dice si lo conoce.</summary>
        public static bool Reconocer(string modelo, out ClaseArma clase)
        {
            clase = ClaseArma.FusilAsalto;
            if (string.IsNullOrEmpty(modelo)) return false;
            int mejor = 0;
            foreach (var (prefijo, c) in Prefijos)
                if (prefijo.Length > mejor && modelo.StartsWith(prefijo, System.StringComparison.OrdinalIgnoreCase))
                {
                    mejor = prefijo.Length;
                    clase = c;
                }
            return mejor > 0;
        }

        /// <summary>
        /// El arma de un humano: un Arma en sus hijos o, si no, la primera malla conocida bajo AC_arma_r (por el
        /// nombre de la malla o el de su objeto). Sin nada conocido, 'porDefecto' (la que dice su prefab).
        /// </summary>
        public static ClaseArma DeHumano(Transform raiz, ClaseArma porDefecto)
        {
            if (raiz == null) return porDefecto;
            var a = raiz.GetComponentInChildren<Arma>(true);
            if (a != null) return a.clase;
            var mano = Buscar(raiz, "AC_arma_r");
            if (mano == null) return porDefecto;
            mano.GetComponentsInChildren(true, s_Mallas);
            var clase = porDefecto;
            foreach (var mf in s_Mallas)
                if ((mf.sharedMesh != null && Reconocer(mf.sharedMesh.name, out clase)) || Reconocer(mf.gameObject.name, out clase))
                {
                    s_Mallas.Clear();
                    return clase;
                }
            s_Mallas.Clear();
            return porDefecto;
        }

        static Transform Buscar(Transform t, string nombre)
        {
            if (t.name == nombre) return t;
            foreach (Transform h in t)
            {
                var r = Buscar(h, nombre);
                if (r != null) return r;
            }
            return null;
        }
    }
}
