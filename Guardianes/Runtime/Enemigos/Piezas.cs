using System.Collections.Generic;
using UnityEngine;

namespace Guardianes.Enemigos
{
    /// <summary>
    /// Piezas geometricas con UV en metros (la textura no se estira aunque la caja mida 12 x 3 m):
    /// cajas, cilindros y prismas (tejados, tiendas), y tubos a lo largo de una curva (catenarias: ramas, lianas,
    /// cables). Cada pieza es un hijo con su malla; asi, al romperse la estructura, cada pieza sale despedida por
    /// separado. Mallas cacheadas por tamano (las de los tubos no: cada una es suya).
    /// </summary>
    public static class Piezas
    {
        const float Teja = 2f; // metros por repeticion de textura

        static readonly Dictionary<string, Mesh> s_Cache = new Dictionary<string, Mesh>();

        public static Transform Caja(Transform padre, Vector3 centro, Vector3 tam, Material m, Quaternion? giro = null, string nombre = "Caja")
        {
            var mesh = Obtener($"c{tam.x:F2}_{tam.y:F2}_{tam.z:F2}", () => MallaCaja(tam));
            return Crear(padre, nombre, centro, giro ?? Quaternion.identity, mesh, m);
        }

        public static Transform Cilindro(Transform padre, Vector3 centro, float radio, float alto, Material m, Quaternion? giro = null, int lados = 16, string nombre = "Cilindro")
        {
            var mesh = Obtener($"y{radio:F2}_{alto:F2}_{lados}", () => MallaCilindro(radio, alto, lados));
            return Crear(padre, nombre, centro, giro ?? Quaternion.identity, mesh, m);
        }

        /// <summary>Prisma triangular (tejado a dos aguas, tienda): base ancho x largo, altura de cumbrera.</summary>
        public static Transform Prisma(Transform padre, Vector3 centro, float ancho, float alto, float largo, Material m, Quaternion? giro = null, string nombre = "Tejado")
        {
            var mesh = Obtener($"p{ancho:F2}_{alto:F2}_{largo:F2}", () => MallaPrisma(ancho, alto, largo));
            return Crear(padre, nombre, centro, giro ?? Quaternion.identity, mesh, m);
        }

        /// <summary>
        /// Una catenaria: un tubo que cuelga de 'a' a 'b' (una rama, una liana, un cable) y baja 'flecha' metros en medio
        /// respecto a la recta entre los dos (0: recto). Los puntos, en el espacio del padre (como el centro de las demas
        /// piezas). La malla es suya, no se cachea: quien quite la pieza la destruye con ella.
        /// </summary>
        public static Transform Catenaria(Transform padre, Vector3 a, Vector3 b, float flecha, float radio, Material m, int tramos = 24, int lados = 8, string nombre = "Catenaria")
            => Tubo(padre, PuntosCatenaria(a, b, flecha, tramos), radio, m, lados, nombre);

        /// <summary>
        /// Los 'tramos' + 1 puntos de una catenaria de 'a' a 'b' que baja 'flecha' metros en medio. La curva es la de la
        /// cadena colgada entre dos puntos a la misma altura (y = c cosh(x/c), con c tal que la flecha sea esa), puesta
        /// bajo la recta de a a b: con los extremos a distinta altura sigue bajando 'flecha' en el centro del vano.
        /// </summary>
        public static Vector3[] PuntosCatenaria(Vector3 a, Vector3 b, float flecha, int tramos = 24)
        {
            tramos = Mathf.Max(1, tramos);
            var puntos = new Vector3[tramos + 1];
            float vano = new Vector2(b.x - a.x, b.z - a.z).magnitude;
            if (vano < 1e-3f) vano = (b - a).magnitude;
            double c = 0.0;
            if (flecha > 1e-4f && vano > 1e-3f)
            {
                // flecha = c (cosh(vano / 2c) - 1): baja al crecer c (de una V a una recta); se busca c a medias en escala log.
                double lo = vano * 1e-3, hi = 1e7;
                for (int k = 0; k < 80; k++)
                {
                    double medio = System.Math.Sqrt(lo * hi);
                    double f = medio * (System.Math.Cosh(vano / (2.0 * medio)) - 1.0);
                    if (double.IsInfinity(f) || f > flecha) lo = medio; else hi = medio;
                }
                c = System.Math.Sqrt(lo * hi);
            }
            for (int i = 0; i <= tramos; i++)
            {
                float u = i / (float)tramos;
                var p = Vector3.Lerp(a, b, u);
                if (c > 0.0)
                {
                    double x = (u - 0.5) * vano;
                    p.y -= (float)(c * (System.Math.Cosh(vano / (2.0 * c)) - System.Math.Cosh(x / c)));
                }
                puntos[i] = p;
            }
            return puntos;
        }

        /// <summary>
        /// Un tubo de 'radio' a lo largo de unos puntos (en el espacio del padre), con sus tapas; 'radioFinal' (mayor que 0)
        /// lo afina o lo engorda hasta el ultimo punto. UV en metros (alrededor y a lo largo). La malla es suya.
        /// </summary>
        public static Transform Tubo(Transform padre, IList<Vector3> puntos, float radio, Material m, int lados = 8, string nombre = "Tubo", float radioFinal = -1f)
        {
            var mesh = MallaTubo(puntos, radio, radioFinal > 0f ? radioFinal : radio, Mathf.Max(3, lados));
            mesh.name = "Pieza_tubo";
            return Crear(padre, nombre, Vector3.zero, Quaternion.identity, mesh, m);
        }

        static Transform Crear(Transform padre, string nombre, Vector3 centro, Quaternion giro, Mesh mesh, Material m)
        {
            var go = new GameObject(nombre);
            go.transform.SetParent(padre, false);
            go.transform.localPosition = centro;
            go.transform.localRotation = giro;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = m;
            return go.transform;
        }

        static Mesh Obtener(string clave, System.Func<Mesh> crear)
        {
            if (!s_Cache.TryGetValue(clave, out var m) || m == null)
            {
                m = crear();
                m.name = "Pieza_" + clave;
                m.hideFlags = HideFlags.DontSave;
                s_Cache[clave] = m;
            }
            return m;
        }

        // ------------------------------------------------------------------ Mallas
        static Mesh MallaCaja(Vector3 t)
        {
            var v = new List<Vector3>(); var n = new List<Vector3>(); var uv = new List<Vector2>(); var tri = new List<int>();
            Vector3 h = t * 0.5f;
            void Cara(Vector3 normal, Vector3 u, Vector3 w, float lu, float lw)
            {
                int b = v.Count;
                Vector3 c = Vector3.Scale(normal, h);
                Vector3 du = u * (lu * 0.5f), dw = w * (lw * 0.5f);
                v.Add(c - du - dw); v.Add(c + du - dw); v.Add(c + du + dw); v.Add(c - du + dw);
                for (int i = 0; i < 4; i++) n.Add(normal);
                uv.Add(new Vector2(0, 0)); uv.Add(new Vector2(lu / Teja, 0)); uv.Add(new Vector2(lu / Teja, lw / Teja)); uv.Add(new Vector2(0, lw / Teja));
                tri.AddRange(new[] { b, b + 2, b + 1, b, b + 3, b + 2 });
            }
            Cara(Vector3.right, Vector3.forward, Vector3.up, t.z, t.y);
            Cara(Vector3.left, Vector3.back, Vector3.up, t.z, t.y);
            Cara(Vector3.up, Vector3.right, Vector3.forward, t.x, t.z);
            Cara(Vector3.down, Vector3.right, Vector3.back, t.x, t.z);
            Cara(Vector3.forward, Vector3.left, Vector3.up, t.x, t.y);
            Cara(Vector3.back, Vector3.right, Vector3.up, t.x, t.y);
            return Montar(v, n, uv, tri);
        }

        static Mesh MallaCilindro(float r, float alto, int lados)
        {
            var v = new List<Vector3>(); var n = new List<Vector3>(); var uv = new List<Vector2>(); var tri = new List<int>();
            float circ = 2f * Mathf.PI * r;
            for (int i = 0; i <= lados; i++)
            {
                float a = i / (float)lados * Mathf.PI * 2f;
                var d = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                v.Add(d * r + Vector3.down * alto * 0.5f); n.Add(d); uv.Add(new Vector2(i / (float)lados * circ / Teja, 0f));
                v.Add(d * r + Vector3.up * alto * 0.5f); n.Add(d); uv.Add(new Vector2(i / (float)lados * circ / Teja, alto / Teja));
            }
            for (int i = 0; i < lados; i++)
            {
                int b = i * 2;
                tri.AddRange(new[] { b, b + 1, b + 3, b, b + 3, b + 2 });
            }
            foreach (float s in new[] { -1f, 1f })
            {
                int c = v.Count;
                v.Add(Vector3.up * alto * 0.5f * s); n.Add(Vector3.up * s); uv.Add(Vector2.zero);
                for (int i = 0; i <= lados; i++)
                {
                    float a = i / (float)lados * Mathf.PI * 2f;
                    var d = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                    v.Add(d * r + Vector3.up * alto * 0.5f * s); n.Add(Vector3.up * s); uv.Add(new Vector2(d.x * r / Teja, d.z * r / Teja));
                }
                for (int i = 0; i < lados; i++)
                {
                    if (s > 0) tri.AddRange(new[] { c, c + i + 2, c + i + 1 });
                    else tri.AddRange(new[] { c, c + i + 1, c + i + 2 });
                }
            }
            return Montar(v, n, uv, tri);
        }

        static Mesh MallaPrisma(float ancho, float alto, float largo)
        {
            var v = new List<Vector3>(); var n = new List<Vector3>(); var uv = new List<Vector2>(); var tri = new List<int>();
            float a = ancho * 0.5f, l = largo * 0.5f;
            float faldon = Mathf.Sqrt(a * a + alto * alto);
            foreach (float s in new[] { -1f, 1f })
            {
                // Faldon: de la cumbrera (0, alto) al alero (s*a, 0).
                var normal = new Vector3(s * alto, a, 0f).normalized;
                int b = v.Count;
                v.Add(new Vector3(0f, alto, -l)); v.Add(new Vector3(0f, alto, l)); v.Add(new Vector3(s * a, 0f, l)); v.Add(new Vector3(s * a, 0f, -l));
                for (int i = 0; i < 4; i++) n.Add(normal);
                uv.Add(new Vector2(0, 0)); uv.Add(new Vector2(largo / Teja, 0)); uv.Add(new Vector2(largo / Teja, faldon / Teja)); uv.Add(new Vector2(0, faldon / Teja));
                if (s > 0) tri.AddRange(new[] { b, b + 1, b + 2, b, b + 2, b + 3 });
                else tri.AddRange(new[] { b, b + 2, b + 1, b, b + 3, b + 2 });
            }
            foreach (float s in new[] { -1f, 1f })
            {
                int b = v.Count;
                v.Add(new Vector3(-a, 0f, s * l)); v.Add(new Vector3(a, 0f, s * l)); v.Add(new Vector3(0f, alto, s * l));
                for (int i = 0; i < 3; i++) n.Add(new Vector3(0f, 0f, s));
                uv.Add(new Vector2(0, 0)); uv.Add(new Vector2(ancho / Teja, 0)); uv.Add(new Vector2(ancho * 0.5f / Teja, alto / Teja));
                // Sentido horario visto desde fuera (normal +Z en el hastial delantero).
                if (s > 0) tri.AddRange(new[] { b, b + 1, b + 2 });
                else tri.AddRange(new[] { b, b + 2, b + 1 });
            }
            return Montar(v, n, uv, tri);
        }

        /// <summary>
        /// El tubo: un anillo de 'lados' en cada punto, perpendicular a la curva (el marco se lleva de un punto al
        /// siguiente sin girar: no se retuerce en las curvas), y una tapa en cada punta.
        /// </summary>
        static Mesh MallaTubo(IList<Vector3> puntos, float radio0, float radio1, int lados)
        {
            var v = new List<Vector3>(); var n = new List<Vector3>(); var uv = new List<Vector2>(); var tri = new List<int>();
            int cuantos = puntos != null ? puntos.Count : 0;
            if (cuantos < 2) return Montar(v, n, uv, tri);
            // El largo hasta cada punto (la v de la textura y el radio).
            var acumulado = new float[cuantos];
            for (int i = 1; i < cuantos; i++) acumulado[i] = acumulado[i - 1] + Vector3.Distance(puntos[i - 1], puntos[i]);
            float total = Mathf.Max(1e-4f, acumulado[cuantos - 1]);
            Vector3 Tangente(int i)
            {
                Vector3 t = puntos[Mathf.Min(cuantos - 1, i + 1)] - puntos[Mathf.Max(0, i - 1)];
                return t.sqrMagnitude > 1e-10f ? t.normalized : Vector3.forward;
            }
            // El primer marco: un lado cualquiera perpendicular a la tangente (el que menos se parezca a ella).
            Vector3 t0 = Tangente(0);
            Vector3 lado = Vector3.Cross(Mathf.Abs(Vector3.Dot(t0, Vector3.up)) < 0.9f ? Vector3.up : Vector3.right, t0).normalized;
            Vector3 tAntes = t0;
            float circ0 = 2f * Mathf.PI * Mathf.Max(radio0, radio1);
            for (int i = 0; i < cuantos; i++)
            {
                Vector3 t = Tangente(i);
                // Transporte paralelo: el lado gira con la tangente lo justo.
                lado = Quaternion.FromToRotation(tAntes, t) * lado;
                lado = (lado - t * Vector3.Dot(lado, t)).normalized;
                tAntes = t;
                Vector3 otro = Vector3.Cross(t, lado);
                float r = Mathf.Lerp(radio0, radio1, acumulado[i] / total);
                for (int k = 0; k <= lados; k++)
                {
                    float a = k / (float)lados * Mathf.PI * 2f;
                    Vector3 d = lado * Mathf.Cos(a) + otro * Mathf.Sin(a);
                    v.Add(puntos[i] + d * r); n.Add(d); uv.Add(new Vector2(k / (float)lados * circ0 / Teja, acumulado[i] / Teja));
                }
            }
            int anillo = lados + 1;
            for (int i = 0; i < cuantos - 1; i++)
                for (int k = 0; k < lados; k++)
                {
                    int b = i * anillo + k;
                    tri.AddRange(new[] { b, b + 1, b + anillo, b + 1, b + anillo + 1, b + anillo });
                }
            // Las tapas (hacia fuera de cada punta).
            for (int punta = 0; punta < 2; punta++)
            {
                int i = punta == 0 ? 0 : cuantos - 1;
                Vector3 normal = punta == 0 ? -Tangente(0) : Tangente(cuantos - 1);
                int centro = v.Count;
                v.Add(puntos[i]); n.Add(normal); uv.Add(Vector2.zero);
                for (int k = 0; k <= lados; k++)
                {
                    int bk = i * anillo + k;
                    v.Add(v[bk]); n.Add(normal); uv.Add(new Vector2(0.5f, 0.5f));
                }
                for (int k = 0; k < lados; k++)
                {
                    if (punta == 0) tri.AddRange(new[] { centro, centro + k + 2, centro + k + 1 });
                    else tri.AddRange(new[] { centro, centro + k + 1, centro + k + 2 });
                }
            }
            return Montar(v, n, uv, tri);
        }

        static Mesh Montar(List<Vector3> v, List<Vector3> n, List<Vector2> uv, List<int> tri)
        {
            var m = new Mesh();
            m.SetVertices(v); m.SetNormals(n); m.SetUVs(0, uv); m.SetTriangles(tri, 0);
            m.RecalculateBounds();
            m.RecalculateTangents();
            return m;
        }
    }
}
