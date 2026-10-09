using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Guardianes.Vegetacion.Generacion
{
    /// <summary>
    /// Acumula vertices y triangulos por submalla y monta el Mesh. Los colores de vertice llevan los
    /// pesos del viento que lee Shaders/Vegetacion/Viento.hlsl:
    /// a = flexion del tronco, r = flexion de rama, g = aleteo de hoja, b = fase.
    /// </summary>
    public sealed class ConstructorMalla
    {
        public readonly List<Vector3> vertices = new List<Vector3>(4096);
        public readonly List<Vector3> normales = new List<Vector3>(4096);
        public readonly List<Vector2> uvs = new List<Vector2>(4096);
        public readonly List<Color> colores = new List<Color>(4096);
        readonly List<int>[] _submallas;

        public ConstructorMalla(int submallas)
        {
            _submallas = new List<int>[submallas];
            for (int i = 0; i < submallas; i++) _submallas[i] = new List<int>(4096);
        }

        public int Cuenta => vertices.Count;

        public int V(Vector3 p, Vector3 n, Vector2 uv, Color c)
        {
            vertices.Add(p);
            normales.Add(n);
            uvs.Add(uv);
            colores.Add(c);
            return vertices.Count - 1;
        }

        public void Tri(int sub, int a, int b, int c)
        {
            var l = _submallas[sub];
            l.Add(a); l.Add(b); l.Add(c);
        }

        /// <summary>Cuadrilatero a-b-c-d en sentido antihorario visto desde la cara.</summary>
        public void Quad(int sub, int a, int b, int c, int d)
        {
            Tri(sub, a, b, c);
            Tri(sub, a, c, d);
        }

        public int Triangulos(int sub) => _submallas[sub].Count / 3;

        public Mesh Construir(string nombre)
        {
            var m = new Mesh { name = nombre };
            if (vertices.Count > 65000) m.indexFormat = IndexFormat.UInt32;
            m.SetVertices(vertices);
            m.SetNormals(normales);
            m.SetUVs(0, uvs);
            m.SetColors(colores);
            m.subMeshCount = _submallas.Length;
            for (int i = 0; i < _submallas.Length; i++) m.SetTriangles(_submallas[i], i, false);
            m.RecalculateBounds();
            m.RecalculateTangents();
            m.UploadMeshData(false);
            return m;
        }

        // ------------------------------------------------------------------ Tubos
        /// <summary>
        /// Tubo a lo largo de 'eje' con marcos de transporte paralelo (sin giros bruscos). 'radio'
        /// recibe (anillo, angulo) y devuelve el radio: asi se hacen contrafuertes y nudos. La V de la
        /// textura avanza en metros / tamTextura; la U da una vuelta cada 'vueltasU'.
        /// </summary>
        public void Tubo(int sub, IList<Vector3> eje, Func<int, float, float> radio, int lados, float tamTextura,
                         float vueltasU, Func<int, Color> pesos, float vInicio = 0f, bool tapaFinal = false)
        {
            int n = eje.Count;
            if (n < 2) return;
            Vector3 t0 = (eje[1] - eje[0]).normalized;
            Vector3 normal = Vector3.Cross(t0, Mathf.Abs(t0.y) < 0.95f ? Vector3.up : Vector3.right).normalized;
            float v = vInicio;
            int previo = -1;
            for (int i = 0; i < n; i++)
            {
                Vector3 t = (eje[Mathf.Min(i + 1, n - 1)] - eje[Mathf.Max(i - 1, 0)]).normalized;
                normal = (normal - Vector3.Dot(normal, t) * t).normalized;
                Vector3 bin = Vector3.Cross(t, normal);
                if (i > 0) v += Vector3.Distance(eje[i], eje[i - 1]) / tamTextura;
                Color c = pesos(i);
                int inicio = vertices.Count;
                for (int k = 0; k <= lados; k++)
                {
                    float a = k / (float)lados * Mathf.PI * 2f;
                    Vector3 dir = Mathf.Cos(a) * normal + Mathf.Sin(a) * bin;
                    float r = radio(i, a);
                    V(eje[i] + dir * r, dir, new Vector2(k / (float)lados * vueltasU, v), c);
                }
                if (previo >= 0)
                {
                    for (int k = 0; k < lados; k++)
                        Quad(sub, previo + k, previo + k + 1, inicio + k + 1, inicio + k);
                }
                previo = inicio;
            }
            if (tapaFinal)
            {
                Vector3 t = (eje[n - 1] - eje[n - 2]).normalized;
                int centro = V(eje[n - 1], t, new Vector2(0.5f, v), pesos(n - 1));
                for (int k = 0; k < lados; k++) Tri(sub, previo + k, previo + k + 1, centro);
            }
        }

        // ------------------------------------------------------------------ Tarjetas de ramillete
        /// <summary>
        /// Tarjeta de follaje curvada (3 tramos) que sale de 'base' en direccion 'dir'. La normal se
        /// mezcla con la direccion desde el centro de la copa: la copa se ilumina como un volumen y
        /// no como un monton de planos.
        /// </summary>
        public void Tarjeta(int sub, Vector3 base_, Vector3 dir, Vector3 normalHint, float largo, float ancho,
                            Rect uv, float caida, Color pesos, Vector3 centroCopa, float esferica = 0.65f)
        {
            dir = dir.normalized;
            Vector3 lado = Vector3.Cross(normalHint, dir);
            if (lado.sqrMagnitude < 1e-6f) lado = Vector3.Cross(Vector3.up, dir);
            if (lado.sqrMagnitude < 1e-6f) lado = Vector3.right;
            lado.Normalize();
            Vector3 cara = Vector3.Cross(dir, lado).normalized;
            const int Tramos = 3;
            int inicio = vertices.Count;
            for (int i = 0; i <= Tramos; i++)
            {
                float t = i / (float)Tramos;
                // La punta cae por su peso (y un poco se abomba hacia la cara).
                Vector3 centro = base_ + dir * (largo * t) + Vector3.down * (caida * largo * t * t);
                for (int j = 0; j < 2; j++)
                {
                    float s = j == 0 ? -0.5f : 0.5f;
                    Vector3 p = centro + lado * (s * ancho);
                    Vector3 n = Vector3.Lerp(cara, (p - centroCopa).normalized, esferica).normalized;
                    var c = pesos;
                    c.r = Mathf.Lerp(pesos.r, 1f, t);
                    c.g = Mathf.Clamp01(t * 0.9f + 0.1f);
                    V(p, n, new Vector2(Mathf.Lerp(uv.xMin, uv.xMax, j), Mathf.Lerp(uv.yMin, uv.yMax, t)), c);
                }
            }
            for (int i = 0; i < Tramos; i++)
            {
                int a = inicio + i * 2;
                Quad(sub, a, a + 2, a + 3, a + 1);
            }
        }

        // ------------------------------------------------------------------ Frondas y hojas largas
        /// <summary>
        /// Fronda (palmera, helecho, platano): un nervio que sale de 'base' con 'elevacion' (rad) en el
        /// rumbo 'azimut' y se va doblando hacia abajo 'curva' radianes en todo su largo; la lamina
        /// tiene ancho(t) a cada lado y se pliega en V 'pliegue' radianes. UV: columna de atlas.
        /// </summary>
        public void Fronda(int sub, Vector3 base_, float azimut, float elevacion, float curva, float largo,
                           Func<float, float> ancho, float pliegue, Rect uv, Color pesos, Vector3 centroCopa,
                           int tramos = 10, float giro = 0f, float esferica = 0.35f)
        {
            Vector3 horizontal = new Vector3(Mathf.Cos(azimut), 0f, Mathf.Sin(azimut));
            Vector3 lateral = Vector3.Cross(Vector3.up, horizontal).normalized;
            Vector3 p = base_;
            float paso = largo / tramos;
            int inicio = vertices.Count;
            for (int i = 0; i <= tramos; i++)
            {
                float t = i / (float)tramos;
                float ang = elevacion - curva * t * t;
                Vector3 dir = horizontal * Mathf.Cos(ang) + Vector3.up * Mathf.Sin(ang);
                if (i > 0) p += dir * paso;
                Vector3 arriba = Vector3.Cross(dir, lateral).normalized;
                // Giro de la lamina sobre el nervio (las hojas no son planas al cielo).
                Vector3 lat = Quaternion.AngleAxis(giro * Mathf.Rad2Deg * t, dir) * lateral;
                arriba = Quaternion.AngleAxis(giro * Mathf.Rad2Deg * t, dir) * arriba;
                float w = ancho(t);
                float subida = Mathf.Sin(pliegue) * w;
                float abre = Mathf.Cos(pliegue) * w;
                var c = pesos;
                c.r = Mathf.Lerp(pesos.r, 1f, t);
                var cBorde = c;
                cBorde.g = Mathf.Clamp01(0.4f + t * 0.6f);
                c.g = t * 0.25f;
                Vector3 nCentro = Vector3.Lerp(arriba, (p - centroCopa).normalized, esferica).normalized;
                Vector3 pIzq = p - lat * abre + arriba * subida;
                Vector3 pDer = p + lat * abre + arriba * subida;
                Vector3 nIzq = Vector3.Lerp((arriba + lat * Mathf.Sin(pliegue)).normalized, (pIzq - centroCopa).normalized, esferica).normalized;
                Vector3 nDer = Vector3.Lerp((arriba - lat * Mathf.Sin(pliegue)).normalized, (pDer - centroCopa).normalized, esferica).normalized;
                float v = Mathf.Lerp(uv.yMin, uv.yMax, t);
                V(pIzq, nIzq, new Vector2(uv.xMin, v), cBorde);
                V(p, nCentro, new Vector2(uv.center.x, v), c);
                V(pDer, nDer, new Vector2(uv.xMax, v), cBorde);
            }
            for (int i = 0; i < tramos; i++)
            {
                int a = inicio + i * 3;
                Quad(sub, a, a + 3, a + 4, a + 1);
                Quad(sub, a + 1, a + 4, a + 5, a + 2);
            }
        }
    }
}
