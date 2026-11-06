using System.Collections.Generic;
using Guardianes.Mundo;
using Unity.Mathematics;
using UnityEngine;

namespace Guardianes.Vegetacion
{
    /// <summary>
    /// Lo solido de la vegetacion (las especies con radioTronco: troncos, rocas, troncos caidos), con la forma que miden sus
    /// mallas (EspecieVegetal.SolidoDe), en una rejilla propia sin hierba ni helechos: preguntar cuesta poco aunque lo haga
    /// cada animal en cada fotograma. Cada juego decide quien choca: Apartar saca un circulo de lo que pisa y Rayo dice a que
    /// distancia hay algo grande en una linea. Lo derribado deja de contar. Se monta la primera vez que se pide
    /// (RenderizadorVegetacion.Solidos): quien no lo usa no lo paga.
    /// </summary>
    public class SolidosVegetacion
    {
        const float Celda = 16f;

        struct Solido
        {
            /// <summary>Los extremos de su eje en planta: iguales en lo que esta de pie.</summary>
            public float2 a, b;
            public float radio, pie, cima, resistencia;
            public int planta;
        }

        readonly RenderizadorVegetacion _veg;
        readonly Solido[] _solidos;
        readonly int[] _inicio, _lista;
        readonly float2 _origen;
        readonly int _lado;

        public int Total => _solidos.Length;

        public SolidosVegetacion(RenderizadorVegetacion veg)
        {
            _veg = veg;
            var me = Mapa.Medidas;
            _origen = me.Origen;
            _lado = (int)math.ceil(me.tamano / Celda);

            var lista = new List<Solido>();
            int n = veg.TotalInstancias;
            for (int i = 0; i < n; i++)
            {
                var e = veg.EspecieDe(i);
                if (e.radioTronco <= 0f) continue;
                var f = e.SolidoDe(veg.VarianteDe(i));
                var m = veg.MatrizDe(i);
                float3 p = m.c3.xyz;
                float k = veg.EscalaDe(i);
                float2 eje = math.normalizesafe(m.c2.xz) * f.medioLargo * k;
                lista.Add(new Solido
                {
                    a = p.xz - eje,
                    b = p.xz + eje,
                    radio = f.radio * k,
                    pie = p.y,
                    cima = p.y + f.alto * k,
                    resistencia = e.destructible ? e.resistencia : float.MaxValue,
                    planta = i,
                });
            }
            _solidos = lista.ToArray();

            // Cada solido, en todas las celdas que toca: las rocas de pared miden mas que una celda.
            _inicio = new int[_lado * _lado + 1];
            for (int s = 0; s < _solidos.Length; s++) Recorrer(s, c => _inicio[c + 1]++);
            for (int c = 0; c < _lado * _lado; c++) _inicio[c + 1] += _inicio[c];
            _lista = new int[_inicio[_lado * _lado]];
            var libre = (int[])_inicio.Clone();
            for (int s = 0; s < _solidos.Length; s++)
            {
                int solido = s;
                Recorrer(s, c => _lista[libre[c]++] = solido);
            }
        }

        void Recorrer(int indice, System.Action<int> enCelda)
        {
            ref var s = ref _solidos[indice];
            int2 c0 = CeldaDe(math.min(s.a, s.b) - s.radio), c1 = CeldaDe(math.max(s.a, s.b) + s.radio);
            for (int y = c0.y; y <= c1.y; y++)
            for (int x = c0.x; x <= c1.x; x++)
                enCelda(y * _lado + x);
        }

        int2 CeldaDe(float2 p) => math.clamp((int2)math.floor((p - _origen) / Celda), 0, _lado - 1);

        static float2 MasCercano(in Solido s, float2 p)
        {
            float2 ab = s.b - s.a;
            float largo2 = math.dot(ab, ab);
            return largo2 > 1e-5f ? s.a + ab * math.saturate(math.dot(p - s.a, ab) / largo2) : s.a;
        }

        /// <summary>
        /// Donde tiene que estar un circulo (centro en 'p', a la altura de los pies) para no meterse en nada solido. Por encima
        /// de la cima de algo no choca: se puede saltar.
        /// </summary>
        /// <param name="tumba">Lo que aguanta esto o menos no le para: se lo lleva por delante.</param>
        /// <param name="pisa">Lo que mide esto de alto o menos tampoco: pasa por encima.</param>
        public Vector3 Apartar(Vector3 p, float radio, float tumba = 0f, float pisa = 0f)
        {
            var q = new float2(p.x, p.z);
            int2 c0 = CeldaDe(q - radio), c1 = CeldaDe(q + radio);
            for (int y = c0.y; y <= c1.y; y++)
            for (int x = c0.x; x <= c1.x; x++)
            {
                int celda = y * _lado + x;
                for (int k = _inicio[celda]; k < _inicio[celda + 1]; k++)
                {
                    ref var s = ref _solidos[_lista[k]];
                    if (p.y >= s.cima || s.cima - s.pie <= pisa || s.resistencia <= tumba) continue;
                    float2 c = MasCercano(s, q), d = q - c;
                    float dist = math.length(d), minimo = s.radio + radio;
                    if (dist >= minimo || !_veg.Viva(s.planta)) continue;
                    q = dist > 1e-4f ? c + d / dist * minimo : c + new float2(minimo, 0f);
                }
            }
            return new Vector3(q.x, p.y, q.y);
        }

        /// <summary>Lo que se mete un circulo en lo solido, a su altura (0: nada). Para las pruebas.</summary>
        public float Metido(Vector3 p, float radio, float tumba = 0f, float pisa = 0f)
        {
            var q = new float2(p.x, p.z);
            float mas = 0f;
            int2 c0 = CeldaDe(q - radio), c1 = CeldaDe(q + radio);
            for (int y = c0.y; y <= c1.y; y++)
            for (int x = c0.x; x <= c1.x; x++)
            {
                int celda = y * _lado + x;
                for (int k = _inicio[celda]; k < _inicio[celda + 1]; k++)
                {
                    ref var s = ref _solidos[_lista[k]];
                    if (p.y >= s.cima || s.cima - s.pie <= pisa || s.resistencia <= tumba || !_veg.Viva(s.planta)) continue;
                    mas = math.max(mas, s.radio + radio - math.distance(q, MasCercano(s, q)));
                }
            }
            return mas;
        }

        /// <summary>
        /// A que distancia de 'origen', yendo hacia 'dir' (unitario), hay algo solido de al menos 'radioMinimo'; si no hay nada, 'dist'.
        /// Sirve para que una camara no se quede detras de una roca: los troncos finos se dejan pasar con radioMinimo.
        /// </summary>
        public float Rayo(Vector3 origen, Vector3 dir, float dist, float radioMinimo)
        {
            float mejor = dist;
            var o = new float2(origen.x, origen.z);
            var f = o + new float2(dir.x, dir.z) * dist;
            var d = new float2(dir.x, dir.z);
            float plano = math.length(d);
            if (plano < 1e-4f) return dist;
            d /= plano;
            int2 c0 = CeldaDe(math.min(o, f)), c1 = CeldaDe(math.max(o, f));
            for (int y = c0.y; y <= c1.y; y++)
            for (int x = c0.x; x <= c1.x; x++)
            {
                int celda = y * _lado + x;
                for (int k = _inicio[celda]; k < _inicio[celda + 1]; k++)
                {
                    ref var s = ref _solidos[_lista[k]];
                    if (s.radio < radioMinimo || !_veg.Viva(s.planta)) continue;
                    float2 aC = (s.a + s.b) * 0.5f - o;
                    float t = math.dot(aC, d), lado2 = math.lengthsq(aC) - t * t, r = s.radio + math.distance(s.a, s.b) * 0.5f;
                    if (lado2 > r * r) continue;
                    float entra = math.max(0f, t - math.sqrt(r * r - lado2)) / plano;
                    if (entra < mejor && origen.y + dir.y * entra < s.cima) mejor = entra;
                }
            }
            return mejor;
        }
    }
}
