using System.Collections.Generic;
using Guardianes.Mundo;
using UnityEngine;

namespace Guardianes.Enemigos
{
    /// <summary>
    /// CAMINOS para los humanos (y lo que quiera usarlo): un A* en una rejilla (2 m) sobre el rectangulo que cubre
    /// el origen y el destino con un margen alrededor. No se pisa el agua honda (mas de medio metro), las cuestas
    /// que no se suben de un paso ni lo que este en las capas de obstaculo (una esfera en cada celda). El camino
    /// se recorta con lineas de vision (de un punto se salta al mas lejano que se ve libre). Barato: se pide solo
    /// cuando el que anda se atasca, con un tope de celdas (128 x 128, si el rectangulo es mayor las celdas
    /// crecen) y de calculos por fotograma.
    /// </summary>
    public static class Navegacion
    {
        /// <summary>Calculos que se permiten por fotograma (los demas esperan al siguiente).</summary>
        public static int PorFotograma = 1;
        /// <summary>Apunta en Ultimo con que choca cada paso rechazado (caro: solo para depurar).</summary>
        public static bool Depurar;
        /// <summary>Caminos calculados y encontrados (las pruebas).</summary>
        public static int Calculados { get; private set; }
        public static int Encontrados { get; private set; }
        /// <summary>Del ultimo calculo: celdas miradas, bloqueadas y puntos antes de recortar (para depurar).</summary>
        public static string Ultimo { get; private set; } = "";

        static int _fotograma = -1, _gastados;
        const int Max = 128;
        static readonly float[] _coste = new float[Max * Max];
        static readonly int[] _padre = new int[Max * Max];
        static readonly sbyte[] _libre = new sbyte[Max * Max];
        static readonly bool[] _cerrado = new bool[Max * Max];
        static readonly List<int> _abiertos = new List<int>();
        static readonly List<Vector3> _bruto = new List<Vector3>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reiniciar() { Calculados = 0; Encontrados = 0; _fotograma = -1; }

        /// <summary>Queda presupuesto en este fotograma.</summary>
        public static bool Puede
        {
            get
            {
                if (Time.frameCount != _fotograma) { _fotograma = Time.frameCount; _gastados = 0; }
                return _gastados < PorFotograma;
            }
        }

        /// <summary>
        /// Busca un camino de 'desde' a 'hasta'. Deja en 'salida' los puntos a seguir (sin el de salida) y
        /// devuelve true si llega (o se acerca a menos de dos celdas). 'capas': lo que corta el paso.
        /// </summary>
        public static bool Camino(Vector3 desde, Vector3 hasta, int capas, List<Vector3> salida, float celda = 2f, float margen = 30f)
        {
            salida.Clear();
            if (!Puede) { Ultimo = "sin presupuesto en este fotograma"; return false; }
            _gastados++;
            Calculados++;
            float minX = Mathf.Min(desde.x, hasta.x) - margen, minZ = Mathf.Min(desde.z, hasta.z) - margen;
            float maxX = Mathf.Max(desde.x, hasta.x) + margen, maxZ = Mathf.Max(desde.z, hasta.z) + margen;
            celda = Mathf.Max(celda, Mathf.Max(maxX - minX, maxZ - minZ) / Max);
            int nx = Mathf.Clamp(Mathf.CeilToInt((maxX - minX) / celda), 2, Max), nz = Mathf.Clamp(Mathf.CeilToInt((maxZ - minZ) / celda), 2, Max);
            int n = nx * nz;
            for (int i = 0; i < n; i++) { _coste[i] = float.MaxValue; _padre[i] = -1; _libre[i] = -1; _cerrado[i] = false; }
            int Indice(Vector3 p) => Mathf.Clamp(Mathf.FloorToInt((p.z - minZ) / celda), 0, nz - 1) * nx + Mathf.Clamp(Mathf.FloorToInt((p.x - minX) / celda), 0, nx - 1);
            Vector3 Centro(int k) { var c = new Vector3(minX + (k % nx + 0.5f) * celda, 0f, minZ + (k / nx + 0.5f) * celda); c.y = Altura(c); return c; }
            bool Transitable(int k)
            {
                if (_libre[k] >= 0) return _libre[k] == 1;
                var c = Centro(k);
                bool ok = Mapa.Profundidad(c.x, c.z) <= 0.5f
                          && (capas == 0 || !Physics.CheckSphere(c + Vector3.up * 0.9f, celda * 0.42f, capas, QueryTriggerInteraction.Ignore));
                _libre[k] = (sbyte)(ok ? 1 : 0);
                return ok;
            }
            int ini = Indice(desde), fin = Indice(hasta);
            // Pegado a una pared (el que va hacia su meta y se queda contra ella): desde ahi, la capsula ya la toca.
            Vector3 salidaReal = new Vector3(desde.x, Altura(desde) + 0.9f, desde.z);
            bool pegado = capas != 0 && Physics.CheckSphere(salidaReal, 0.3f, capas, QueryTriggerInteraction.Ignore);
            _libre[ini] = 1;
            _coste[ini] = 0f;
            _abiertos.Clear();
            _abiertos.Add(ini);
            int mejor = ini;
            float mejorH = Heuristica(ini, fin, nx);
            int expandidos = 0, porCuesta = 0, porPaso = 0, porEsquina = 0;
            string choques = "";
            while (_abiertos.Count > 0 && expandidos < 9000)
            {
                // El abierto con menos coste + heuristica (lista: la rejilla es pequena).
                int ia = 0;
                float fa = float.MaxValue;
                for (int i = 0; i < _abiertos.Count; i++)
                {
                    int k = _abiertos[i];
                    float f = _coste[k] + Heuristica(k, fin, nx);
                    if (f < fa) { fa = f; ia = i; }
                }
                int act = _abiertos[ia];
                _abiertos[ia] = _abiertos[_abiertos.Count - 1];
                _abiertos.RemoveAt(_abiertos.Count - 1);
                if (_cerrado[act]) continue;
                _cerrado[act] = true;
                expandidos++;
                float h = Heuristica(act, fin, nx);
                if (h < mejorH) { mejorH = h; mejor = act; }
                if (act == fin) break;
                int ax = act % nx, az = act / nx;
                // Desde la salida, el sitio de verdad (su celda puede caer encima de una pared).
                Vector3 pAct = act == ini ? desde : Centro(act);
                float yAct = Centro(act).y;
                for (int dz = -1; dz <= 1; dz++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        if (dx == 0 && dz == 0) continue;
                        int bx = ax + dx, bz = az + dz;
                        if (bx < 0 || bz < 0 || bx >= nx || bz >= nz) continue;
                        int b = bz * nx + bx;
                        if (_cerrado[b] || !Transitable(b)) continue;
                        // En diagonal, sin cortar esquinas.
                        if (dx != 0 && dz != 0 && (!Transitable(az * nx + bx) || !Transitable(bz * nx + ax))) { porEsquina++; continue; }
                        float paso = (dx != 0 && dz != 0 ? 1.4142f : 1f) * celda;
                        // Una cuesta que no se sube de un paso.
                        float subida = Mathf.Abs(Centro(b).y - yAct);
                        if (subida > paso * 0.9f + 0.4f) { porCuesta++; continue; }
                        // El paso entre las dos celdas, libre (una capsula: ve lo que ya toca al salir).
                        if (capas != 0)
                        {
                            Vector3 pB = Centro(b);
                            var a0 = new Vector3(pAct.x, Altura(pAct) + 0.9f, pAct.z);
                            bool choca;
                            if (act == ini && pegado)
                            {
                                // Lo que ya toca no cuenta (se aparta de ello); lo que tiene delante, si.
                                Vector3 haciaB = pB + Vector3.up * 0.9f - a0;
                                float lB = haciaB.magnitude;
                                choca = Physics.Raycast(a0, haciaB / lB, lB, capas, QueryTriggerInteraction.Ignore)
                                     || Physics.SphereCast(a0, 0.2f, haciaB / lB, out _, lB, capas, QueryTriggerInteraction.Ignore);
                            }
                            else choca = Physics.CheckCapsule(a0, pB + Vector3.up * 0.9f, 0.3f, capas, QueryTriggerInteraction.Ignore);
                            if (choca)
                            {
                                porPaso++;
                                if (Depurar && choques.Length < 300)
                                    foreach (var col in Physics.OverlapCapsule(a0, pB + Vector3.up * 0.9f, 0.3f, capas, QueryTriggerInteraction.Ignore))
                                        if (!choques.Contains(col.name)) choques += col.name + " (capa " + col.gameObject.layer + "), ";
                                continue;
                            }
                        }
                        float c = _coste[act] + paso + subida * 2f;
                        if (c >= _coste[b]) continue;
                        _coste[b] = c;
                        _padre[b] = act;
                        _abiertos.Add(b);
                    }
            }
            int miradas = 0, bloqueadas = 0;
            for (int i = 0; i < n; i++) { if (_libre[i] >= 0) miradas++; if (_libre[i] == 0) bloqueadas++; }
            // Hasta el destino, o hasta lo mas cerca a lo que se llega.
            int destino = _padre[fin] >= 0 || fin == ini ? fin : mejor;
            if (destino == ini)
            {
                Ultimo = $"no sale de la primera celda: rejilla {nx}x{nz} de {celda:F1} m, {miradas} celdas miradas, {bloqueadas} bloqueadas; rechazos: cuesta {porCuesta}, paso {porPaso}, esquina {porEsquina}" + (Depurar ? "; choca con " + choques : "");
                return false;
            }
            _bruto.Clear();
            for (int k = destino; k >= 0 && k != ini; k = _padre[k]) _bruto.Add(Centro(k));
            _bruto.Reverse();
            if (destino == fin) _bruto[_bruto.Count - 1] = hasta;
            Recortar(desde, capas, salida);
            Ultimo = $"rejilla {nx}x{nz} de {celda:F1} m, {miradas} celdas miradas, {bloqueadas} bloqueadas, {expandidos} abiertas, {_bruto.Count} puntos sin recortar; rechazos: cuesta {porCuesta}, paso {porPaso}, esquina {porEsquina}" + (Depurar ? "; choca con " + choques : "");
            bool llega = destino == fin || Heuristica(destino, fin, nx) <= 2f;
            if (llega) Encontrados++;
            return llega;
        }

        static float Heuristica(int a, int b, int nx)
        {
            int dx = Mathf.Abs(a % nx - b % nx), dz = Mathf.Abs(a / nx - b / nx);
            return Mathf.Max(dx, dz) + 0.4142f * Mathf.Min(dx, dz);
        }

        static float Altura(Vector3 p) => TerrenoMundo.Instancia != null ? Mapa.Altura(p.x, p.z) : p.y;

        /// <summary>De cada punto, al mas lejano que se ve libre (sin agua honda ni obstaculo en medio).</summary>
        static void Recortar(Vector3 desde, int capas, List<Vector3> salida)
        {
            Vector3 actual = desde;
            int i = 0;
            while (i < _bruto.Count)
            {
                int lejos = i;
                for (int j = _bruto.Count - 1; j > i; j--)
                    if (Visible(actual, _bruto[j], capas)) { lejos = j; break; }
                actual = _bruto[lejos];
                salida.Add(actual);
                i = lejos + 1;
            }
        }

        const float Holgura = 0.65f;

        static bool Visible(Vector3 a, Vector3 b, int capas)
        {
            Vector3 d = b - a;
            d.y = 0f;
            float largo = d.magnitude;
            if (largo < 0.1f) return true;
            Vector3 dir = d / largo;
            for (float t = 1f; t < largo; t += 1f)
            {
                var p = a + dir * t;
                if (Mapa.Profundidad(p.x, p.z) > 0.5f) return false;
                // Una esfera en cada metro: el SphereCast no ve lo que ya toca al salir (el que esta pegado a una pared).
                // Con holgura: el que anda se desvia un poco al rodear y no debe acabar con la esquina delante.
                if (capas != 0 && Physics.CheckSphere(new Vector3(p.x, Altura(p) + 0.9f, p.z), Holgura, capas, QueryTriggerInteraction.Ignore)) return false;
            }
            if (capas == 0) return true;
            Vector3 o = a + Vector3.up * 0.9f;
            return !Physics.Raycast(o, dir, largo, capas, QueryTriggerInteraction.Ignore)
                && !Physics.SphereCast(o, Holgura - 0.05f, dir, out _, largo, capas, QueryTriggerInteraction.Ignore);
        }
    }
}
