using System.Collections.Generic;
using UnityEngine;

namespace Guardianes.Enemigos
{
    /// <summary>
    /// Piezas geometricas con UV en metros (la textura no se estira aunque la caja mida 12 x 3 m):
    /// cajas, cilindros y prismas (tejados, tiendas). Cada pieza es un hijo con su malla; asi, al
    /// romperse la estructura, cada pieza sale despedida por separado. Mallas cacheadas por tamano.
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
