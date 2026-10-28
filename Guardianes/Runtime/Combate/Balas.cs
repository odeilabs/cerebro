using System.Collections.Generic;
using Unity.Profiling;
using UnityEngine;

namespace Guardianes.Combate
{
    /// <summary>
    /// Lo que penetra cada proyectil (1 = una pistola de 9 mm). Frente al blindaje de una Salud dice si entra o
    /// rebota (Salud.Penetracion); cada juego pone el blindaje de sus criaturas (0 de serie: nada rebota).
    /// </summary>
    public static class Calibres
    {
        public const float Ninguno = 0f, Perdigon = 0.45f, Pistola = 1f, Subfusil = 1f, Arcabuz = 1.3f,
                           FusilAsalto = 8f, FusilCaza = 12f, Ametralladora = 25f, Canon = 60f;
    }

    /// <summary>Donde un blanco sin colisionadores recibe el rayo o la esfera. para: se queda ahi sin dano (el agua).</summary>
    public struct GolpeBlanco
    {
        /// <summary>Rayo: del origen a la entrada (m). Esfera: del centro a lo mas cercano del cuerpo.</summary>
        public float distancia;
        /// <summary>Esfera: la parte del dano que le llega (0..1; lo que hay del cuerpo dentro).</summary>
        public float fraccion;
        public Vector3 punto, normal;
        public bool para;
    }

    /// <summary>
    /// Un blanco sin colisionadores: un cuerpo que un collider no puede seguir (la cadena de capsulas de una
    /// serpiente gigante). Se registra en Balas y los tiradores le preguntan despues de su rayo de fisica, solo
    /// hasta donde llego ese rayo: lo que tiene colisionador delante (el relieve, una roca, una pared, otro
    /// humano) lo tapa solo.
    /// </summary>
    public interface IBlancoBalas
    {
        Salud Salud { get; }
        /// <summary>Su raiz: un tirador que cuelga de ella no le da, y una explosion que la ignora tampoco.</summary>
        Transform Raiz { get; }
        /// <summary>Lo primero que toca el rayo (dir normalizada) entre 0 y 'hasta'.</summary>
        bool Rayo(Vector3 origen, Vector3 dir, float hasta, out GolpeBlanco g);
        /// <summary>Una explosion (Impacto.Golpear): si le llega, a que distancia y que parte del dano.</summary>
        bool Esfera(Vector3 centro, float radio, float danio, TipoDanio tipo, GameObject ignorar, out GolpeBlanco g);
    }

    /// <summary>Un proyectil: de donde sale, hacia donde, que toca y que le hace.</summary>
    public struct Tiro
    {
        public Vector3 origen, direccion, punto, normal;
        /// <summary>distancia: hasta lo que toco (o el alcance). calibre y cantidad: los del DatosDanio (Impactar).</summary>
        public float distancia, calibre, cantidad;
        /// <summary>toco: algo (el suelo, un colisionador, un blanco). rebote: rebota en el blindaje. para: se queda en el agua de un blanco.</summary>
        public bool toco, rebote, para;
        /// <summary>El colisionador que toco (null si es un blanco o si no toco nada).</summary>
        public Collider collider;
        /// <summary>A quien dana (la del colisionador o la del blanco; null si nada).</summary>
        public Salud salud;
        public IBlancoBalas blanco;
        public GameObject tirador;
        public TipoDanio tipo;
        /// <summary>Time.frameCount del tiro.</summary>
        public int fotograma;
    }

    /// <summary>
    /// Los proyectiles de todos los tiradores (humanos, el canon y los cohetes de los helicopteros): el rayo de
    /// fisica, que se salta lo del propio tirador, y despues los blancos sin colisionadores registrados (IBlancoBalas),
    /// solo hasta donde llego ese rayo. Al tocar una Salud tira los dados del rebote con su blindaje
    /// (Salud.Penetracion; con un System.Random propio, para no mover el UnityEngine.Random de cada juego) y la dana.
    /// Sin blancos y sin blindaje es el Physics.Raycast de siempre. AlImpactar: los efectos y el sonido del rebote.
    /// </summary>
    public static class Balas
    {
        /// <summary>La semilla de los dados del rebote (se vuelven a sembrar al entrar en modo juego).</summary>
        public const int Semilla = 1542;

        static readonly List<IBlancoBalas> s_Blancos = new List<IBlancoBalas>();
        static readonly RaycastHit[] s_Golpes = new RaycastHit[32];
        static readonly ProfilerMarker s_Marca = new ProfilerMarker("Balas");
        static System.Random s_Dados = new System.Random(Semilla);

        /// <summary>Cada tiro que toca algo (el suelo, un colisionador, un blanco), despues de danar.</summary>
        public static event System.Action<Tiro> AlImpactar;
        /// <summary>El ultimo tiro que toco algo (los efectos, las pruebas).</summary>
        public static Tiro Ultimo { get; private set; }
        /// <summary>Tiros que han dado a una Salud (las pruebas).</summary>
        public static int Impactos { get; private set; }
        /// <summary>De ellos, los que han rebotado.</summary>
        public static int Rebotes { get; private set; }
        /// <summary>Dados tirados (solo cuando el rebote puede salir o no).</summary>
        public static int Tiradas { get; private set; }
        /// <summary>Blancos registrados.</summary>
        public static int Registrados => s_Blancos.Count;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reiniciar()
        {
            s_Blancos.Clear();
            ReiniciarContadores();
        }

        /// <summary>Pone a cero los contadores y el ultimo tiro, y vuelve a sembrar los dados (las pruebas).</summary>
        public static void ReiniciarContadores(int semilla = Semilla)
        {
            s_Dados = new System.Random(semilla);
            Impactos = Rebotes = Tiradas = 0;
            Ultimo = default;
        }

        public static void Registrar(IBlancoBalas b)
        {
            if (b != null && !s_Blancos.Contains(b)) s_Blancos.Add(b);
        }

        public static void Quitar(IBlancoBalas b) => s_Blancos.Remove(b);

        /// <summary>
        /// Lo que toca un proyectil (dir normalizada) hasta 'alcance': lo mas cercano con colisionador que no sea
        /// del tirador y, si hay un blanco sin colisionadores mas cerca, ese. Si no toca nada, t.punto es el final
        /// del alcance.
        /// </summary>
        public static bool Rayo(Vector3 origen, Vector3 dir, float alcance, GameObject tirador, out Tiro t)
        {
            using (s_Marca.Auto())
            {
                t = new Tiro { origen = origen, direccion = dir, tirador = tirador, distancia = alcance, punto = origen + dir * alcance, fotograma = Time.frameCount };
                Transform propio = tirador != null ? tirador.transform : null;
                // El rayo de fisica de siempre; si lo primero es del tirador (su escudo, su fuselaje), lo siguiente que no lo sea.
                bool hay = Physics.Raycast(origen, dir, out var hit, alcance, ~0, QueryTriggerInteraction.Ignore);
                if (hay && propio != null && hit.collider.transform.IsChildOf(propio)) hay = SinPropio(origen, dir, alcance, propio, out hit);
                if (hay)
                {
                    t.toco = true;
                    t.punto = hit.point;
                    t.normal = hit.normal;
                    t.distancia = hit.distance;
                    t.collider = hit.collider;
                    t.salud = hit.collider.GetComponentInParent<Salud>();
                }
                // Los blancos sin colisionadores, solo hasta donde ha llegado: gana la entrada mas cercana.
                float hasta = t.distancia;
                for (int i = 0; i < s_Blancos.Count; i++)
                {
                    var b = s_Blancos[i];
                    if (b == null || (b is UnityEngine.Object o && o == null)) continue;
                    var raiz = b.Raiz;
                    if (propio != null && raiz != null && raiz.IsChildOf(propio)) continue;
                    if (!b.Rayo(origen, dir, hasta, out var g) || g.distancia >= hasta) continue;
                    hasta = g.distancia;
                    t.toco = true;
                    t.punto = g.punto;
                    t.normal = g.normal;
                    t.distancia = g.distancia;
                    t.collider = null;
                    t.para = g.para;
                    t.salud = g.para ? null : b.Salud;
                    t.blanco = b;
                }
                return t.toco;
            }
        }

        /// <summary>Lo mas cercano que no cuelga del tirador.</summary>
        static bool SinPropio(Vector3 origen, Vector3 dir, float alcance, Transform propio, out RaycastHit golpe)
        {
            int n = Physics.RaycastNonAlloc(origen, dir, s_Golpes, alcance, ~0, QueryTriggerInteraction.Ignore);
            golpe = default;
            float mejor = float.MaxValue;
            for (int i = 0; i < n; i++)
            {
                var g = s_Golpes[i];
                if (g.distance >= mejor || g.collider.transform.IsChildOf(propio)) continue;
                mejor = g.distance;
                golpe = g;
            }
            return mejor < float.MaxValue;
        }

        /// <summary>
        /// Lo que hace el tiro al llegar: si toca una Salud (que no sea la del tirador), los dados del rebote (solo
        /// si puede entrar o no: 0 &lt; Penetracion &lt; 1) y Danar con el punto del impacto; despues, AlImpactar.
        /// </summary>
        public static void Impactar(ref Tiro t, DatosDanio d)
        {
            t.calibre = d.calibre;
            t.cantidad = d.cantidad;
            t.tipo = d.tipo;
            if (!t.toco) return;
            if (t.salud != null && !t.para && (t.tirador == null || t.salud.gameObject != t.tirador))
            {
                float p = t.salud.Penetracion(d.calibre);
                bool rebota = p <= 0f || (p < 1f && Dado() >= p);
                t.rebote = rebota;
                d.rebote = rebota;
                d.punto = t.punto;
                t.salud.Danar(d);
                Impactos++;
                if (rebota) Rebotes++;
            }
            Ultimo = t;
            AlImpactar?.Invoke(t);
        }

        static float Dado()
        {
            Tiradas++;
            return (float)s_Dados.NextDouble();
        }

        /// <summary>Rayo e Impactar de una vez; devuelve donde acaba el tiro (la trazadora, el fogonazo del impacto).</summary>
        public static Vector3 Disparar(Vector3 origen, Vector3 dir, float alcance, GameObject tirador, DatosDanio d)
        {
            Rayo(origen, dir, alcance, tirador, out var t);
            Impactar(ref t, d);
            return t.punto;
        }

        /// <summary>
        /// Los blancos sin colisionadores a los que llega una esfera de dano (Impacto.Golpear): cada uno dice a que
        /// distancia y que parte. Los que cuelgan de 'ignorar' no cuentan. 'fuera' se vacia antes.
        /// </summary>
        public static void Esfera(Vector3 c, float r, float danio, TipoDanio tipo, GameObject ignorar, List<(IBlancoBalas blanco, GolpeBlanco golpe)> fuera)
        {
            fuera.Clear();
            if (s_Blancos.Count == 0) return;
            using (s_Marca.Auto())
            {
                Transform ignorado = ignorar != null ? ignorar.transform : null;
                for (int i = 0; i < s_Blancos.Count; i++)
                {
                    var b = s_Blancos[i];
                    if (b == null || (b is UnityEngine.Object o && o == null)) continue;
                    var raiz = b.Raiz;
                    if (ignorado != null && raiz != null && raiz.IsChildOf(ignorado)) continue;
                    if (b.Esfera(c, r, danio, tipo, ignorar, out var g)) fuera.Add((b, g));
                }
            }
        }
    }
}
