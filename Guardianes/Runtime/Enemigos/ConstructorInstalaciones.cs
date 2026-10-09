using System.Collections.Generic;
using Guardianes.Combate;
using Guardianes.Mundo;
using UnityEngine;

namespace Guardianes.Enemigos
{
    /// <summary>
    /// Levanta las instalaciones de una zona humana al cargar (como el terreno y la vegetacion: nada
    /// pesado en la escena). Cada tipo de zona tiene su plano: campamento maderero, pozos de
    /// petroleo, mina, base de la compania y muelle. Las piezas se hacen con Piezas (UV en metros).
    /// Devuelve las instalaciones principales (las que liberan la zona al destruirlas).
    /// </summary>
    public static partial class ConstructorInstalaciones
    {
        static MaterialesInstalaciones M;
        static System.Random R;
        static readonly List<(Vector2 p, float r)> s_Ocupado = new List<(Vector2, float)>();

        public static List<Salud> Construir(ZonaHumana zona, MaterialesInstalaciones materiales, int semilla)
        {
            M = materiales;
            R = new System.Random(semilla);
            s_Ocupado.Clear();
            var principales = new List<Salud>();
            var t = zona.transform;
            float rz = zona.radio;

            void N(int cuantos, float rMin, float rMax, float huella, System.Func<Transform, Vector3, float, GameObject> crear, bool principal)
            {
                for (int i = 0; i < cuantos; i++)
                {
                    if (!Lugar(t.position, rz * rMin, rz * rMax, huella, out var p)) continue;
                    var go = crear(t, p, (float)R.NextDouble() * 360f);
                    if (go == null) continue;
                    if (principal) principales.Add(go.GetComponent<Salud>());
                }
            }

            switch (zona.tipo)
            {
                case TipoZona.Maderero:
                    N(3, 0f, 0.35f, 9f, Barracon, true);
                    N(2, 0.1f, 0.6f, 6f, Deposito, true);
                    N(2, 0.2f, 0.7f, 7f, Camion, true);
                    N(2, 0.5f, 0.85f, 3f, Torre, true);
                    N(2, 0.1f, 0.6f, 2.5f, Generador, true);
                    N(5, 0.2f, 0.75f, 4f, Tienda, false);
                    N(7, 0.3f, 0.9f, 7f, PilaTroncos, false);
                    N(4, 0.1f, 0.8f, 2f, Bidones, false);
                    break;
                case TipoZona.Petroleo:
                    N(6, 0.15f, 0.8f, 7f, Balancin, true);
                    N(3, 0.1f, 0.5f, 8f, TanqueGrande, true);
                    N(2, 0.1f, 0.4f, 9f, Barracon, true);
                    N(2, 0.2f, 0.7f, 2.5f, Generador, false);
                    N(1, 0.6f, 0.85f, 3f, Torre, true);
                    N(7, 0.1f, 0.85f, 2f, Bidones, false);
                    N(3, 0.2f, 0.7f, 6f, Deposito, false);
                    break;
                case TipoZona.Mina:
                    N(3, 0.0f, 0.55f, 9f, Excavadora, true);
                    N(2, 0.2f, 0.75f, 7f, Camion, true);
                    N(2, 0.5f, 0.85f, 9f, Barracon, true);
                    N(2, 0.4f, 0.85f, 6f, Deposito, true);
                    N(1, 0.6f, 0.9f, 3f, Torre, false);
                    N(2, 0.4f, 0.85f, 2.5f, Generador, false);
                    N(4, 0.3f, 0.9f, 2f, Bidones, false);
                    break;
                case TipoZona.Base:
                    N(1, 0.0f, 0.15f, 12f, Helipuerto, false);
                    N(4, 0.2f, 0.6f, 9f, Barracon, true);
                    N(4, 0.6f, 0.9f, 3f, Torre, true);
                    N(1, 0.3f, 0.6f, 5f, Antena, true);
                    N(4, 0.2f, 0.8f, 7f, Contenedor, false);
                    N(2, 0.2f, 0.7f, 6f, Deposito, true);
                    N(2, 0.2f, 0.7f, 2.5f, Generador, false);
                    N(3, 0.2f, 0.8f, 2f, Bidones, false);
                    break;
                case TipoZona.Muelle:
                    N(1, 0.0f, 0.2f, 10f, Grua, true);
                    N(6, 0.1f, 0.75f, 7f, Contenedor, false);
                    N(1, 0.3f, 0.7f, 9f, Barracon, true);
                    N(2, 0.2f, 0.7f, 6f, Deposito, true);
                    N(3, 0.2f, 0.8f, 2f, Bidones, false);
                    break;
                case TipoZona.Garimpo:
                    {
                        // Lonas, motobombas y bidones de gasoil y mercurio; los grandes, con maquinas.
                        bool grande = rz > 120f;
                        N(grande ? 2 : 0, 0.1f, 0.6f, 9f, Excavadora, true);
                        N(grande ? 2 : 1, 0.2f, 0.7f, 6f, Deposito, true);
                        N(grande ? 3 : 2, 0.1f, 0.6f, 2.5f, Generador, true);
                        N(grande ? 6 : 3, 0.2f, 0.8f, 4f, Tienda, false);
                        N(grande ? 8 : 4, 0.1f, 0.85f, 2f, Bidones, false);
                        N(grande ? 2 : 0, 0.3f, 0.8f, 7f, Camion, true);
                    }
                    break;
                case TipoZona.Draga:
                    N(1, 0.1f, 0.4f, 9f, Barracon, true);
                    N(2, 0.1f, 0.6f, 6f, Deposito, true);
                    N(2, 0.1f, 0.6f, 2.5f, Generador, true);
                    N(4, 0.1f, 0.8f, 7f, Contenedor, false);
                    N(5, 0.1f, 0.85f, 2f, Bidones, false);
                    break;
                case TipoZona.Hacienda:
                    N(2, 0.0f, 0.35f, 9f, Barracon, true);
                    N(2, 0.1f, 0.5f, 6f, Deposito, true);
                    N(2, 0.2f, 0.7f, 7f, Camion, true);
                    N(1, 0.4f, 0.8f, 3f, Torre, true);
                    N(2, 0.1f, 0.6f, 2.5f, Generador, true);
                    N(6, 0.3f, 0.95f, 7f, PilaTroncos, false);
                    N(4, 0.1f, 0.8f, 2f, Bidones, false);
                    break;
                case TipoZona.Presa:
                    N(1, 0.0f, 0.3f, 10f, Grua, true);
                    N(2, 0.1f, 0.6f, 9f, Excavadora, true);
                    N(2, 0.2f, 0.7f, 7f, Camion, true);
                    N(2, 0.3f, 0.8f, 9f, Barracon, true);
                    N(4, 0.2f, 0.8f, 7f, Contenedor, false);
                    N(2, 0.2f, 0.7f, 2.5f, Generador, true);
                    N(4, 0.2f, 0.8f, 2f, Bidones, false);
                    break;
                case TipoZona.Traficantes:
                case TipoZona.Cazadores:
                case TipoZona.Laboratorio:
                    N(2, 0.0f, 0.4f, 9f, Barracon, true);
                    N(4, 0.1f, 0.7f, 4f, Tienda, false);
                    N(2, 0.2f, 0.7f, 7f, Contenedor, true);
                    N(2, 0.1f, 0.6f, 2.5f, Generador, true);
                    N(4, 0.1f, 0.8f, 2f, Bidones, false);
                    break;
            }
            return principales;
        }

        // ------------------------------------------------------------------ Colocacion
        static bool Lugar(Vector3 centro, float rMin, float rMax, float huella, out Vector3 p)
        {
            for (int intento = 0; intento < 40; intento++)
            {
                float a = (float)R.NextDouble() * Mathf.PI * 2f;
                float r = Mathf.Lerp(rMin, rMax, Mathf.Sqrt((float)R.NextDouble()));
                var xz = new Vector2(centro.x + Mathf.Cos(a) * r, centro.z + Mathf.Sin(a) * r);
                bool libre = true;
                foreach (var o in s_Ocupado)
                    if (Vector2.Distance(o.p, xz) < o.r + huella + 2f) { libre = false; break; }
                if (!libre) continue;
                float y = TerrenoMundo.Instancia != null ? Mapa.Altura(xz.x, xz.y) : centro.y;
                if (TerrenoMundo.Instancia != null && !Mapa.EsTierra(new Vector3(xz.x, y, xz.y), 0.05f)) continue;
                s_Ocupado.Add((xz, huella));
                p = new Vector3(xz.x, y, xz.y);
                return true;
            }
            p = Vector3.zero;
            return false;
        }

        static GameObject Nueva(Transform padre, string nombre, Vector3 pos, float rumbo, float vida, bool explosiva = false)
        {
            var go = new GameObject(nombre);
            go.transform.SetParent(padre, true);
            go.transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, rumbo, 0f));
            var s = go.AddComponent<Salud>();
            s.maxima = s.actual = vida;
            s.resistenciaBalas = 0f; // las balas humanas no rompen sus propias cosas
            var e = go.AddComponent<Estructura>();
            e.explosiva = explosiva;
            return go;
        }

        /// <summary>Colision de la estructura entera (caja que envuelve sus piezas) y sombras.</summary>
        static GameObject Cerrar(GameObject go)
        {
            var b = new Bounds();
            bool primero = true;
            foreach (var r in go.GetComponentsInChildren<MeshRenderer>())
            {
                var mf = r.GetComponent<MeshFilter>();
                var lb = mf.sharedMesh.bounds;
                // Esquinas de la pieza en el espacio de la estructura.
                for (int i = 0; i < 8; i++)
                {
                    var c = lb.center + Vector3.Scale(lb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    var w = go.transform.InverseTransformPoint(r.transform.TransformPoint(c));
                    if (primero) { b = new Bounds(w, Vector3.zero); primero = false; } else b.Encapsulate(w);
                }
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            }
            var col = go.AddComponent<BoxCollider>();
            col.center = b.center;
            col.size = b.size;
            return go;
        }

        static Material Pintura()
        {
            switch (R.Next(5))
            {
                case 0: return M.pinturaRoja;
                case 1: return M.pinturaAzul;
                case 2: return M.pinturaVerde;
                case 3: return M.pinturaNaranja;
                default: return M.pinturaBlanca;
            }
        }

        // ------------------------------------------------------------------ Estructuras
        static GameObject Barracon(Transform padre, Vector3 p, float rumbo)
        {
            var go = Nueva(padre, "Barracon", p, rumbo, 260f);
            var t = go.transform;
            float largo = 12f + (float)R.NextDouble() * 6f, ancho = 6.5f, alto = 3.6f;
            Piezas.Caja(t, new Vector3(0f, 0.25f, 0f), new Vector3(ancho + 1f, 0.6f, largo + 1f), M.hormigon, null, "Losa");
            Piezas.Caja(t, new Vector3(-ancho * 0.5f, alto * 0.5f + 0.5f, 0f), new Vector3(0.2f, alto, largo), M.madera, null, "Pared");
            Piezas.Caja(t, new Vector3(ancho * 0.5f, alto * 0.5f + 0.5f, 0f), new Vector3(0.2f, alto, largo), M.madera, null, "Pared");
            Piezas.Caja(t, new Vector3(0f, alto * 0.5f + 0.5f, -largo * 0.5f), new Vector3(ancho, alto, 0.2f), M.madera, null, "Pared");
            Piezas.Caja(t, new Vector3(0f, alto * 0.5f + 0.5f, largo * 0.5f), new Vector3(ancho, alto, 0.2f), M.madera, null, "Pared");
            Piezas.Prisma(t, new Vector3(0f, alto + 0.5f, 0f), ancho + 1.2f, 1.8f, largo + 1.2f, M.oxido);
            Piezas.Caja(t, new Vector3(0f, 1.6f, largo * 0.5f + 0.12f), new Vector3(1.2f, 2.2f, 0.1f), M.pinturaVerde, null, "Puerta");
            for (int i = 0; i < 3; i++)
                Piezas.Caja(t, new Vector3(ancho * 0.5f + 0.11f, 2.6f, -largo * 0.3f + i * largo * 0.3f), new Vector3(0.05f, 0.9f, 1.4f), M.cristal, null, "Ventana");
            return Cerrar(go);
        }

        static GameObject Tienda(Transform padre, Vector3 p, float rumbo)
        {
            var go = Nueva(padre, "Tienda", p, rumbo, 40f);
            float ancho = 4f + (float)R.NextDouble() * 2f, largo = 5f + (float)R.NextDouble() * 3f;
            Piezas.Prisma(go.transform, new Vector3(0f, 0.05f, 0f), ancho, 2.6f, largo, M.lona, null, "Lona");
            return Cerrar(go);
        }

        static GameObject Deposito(Transform padre, Vector3 p, float rumbo)
        {
            var go = Nueva(padre, "Deposito", p, rumbo, 120f, true);
            var t = go.transform;
            var giro = Quaternion.Euler(90f, 0f, 0f);
            Piezas.Cilindro(t, new Vector3(0f, 2.3f, 0f), 1.6f, 8f, (float)R.NextDouble() < 0.5 ? M.pinturaBlanca : M.pinturaRoja, giro, 18, "Tanque");
            for (int i = -1; i <= 1; i += 2)
                Piezas.Caja(t, new Vector3(0f, 0.6f, i * 2.6f), new Vector3(2.6f, 1.2f, 0.4f), M.oxido, null, "Soporte");
            Piezas.Cilindro(t, new Vector3(0f, 4.1f, 1.5f), 0.35f, 0.6f, M.metal, null, 8, "Boca");
            return Cerrar(go);
        }

        static GameObject TanqueGrande(Transform padre, Vector3 p, float rumbo)
        {
            var go = Nueva(padre, "TanqueCrudo", p, rumbo, 420f, true);
            var t = go.transform;
            float r = 6f + (float)R.NextDouble() * 2f, h = 9f + (float)R.NextDouble() * 3f;
            go.GetComponent<Estructura>().radioExplosion = 34f;
            go.GetComponent<Estructura>().danioExplosion = 260f;
            Piezas.Cilindro(t, new Vector3(0f, h * 0.5f, 0f), r, h, M.pinturaBlanca, null, 32, "Cuerpo");
            Piezas.Cilindro(t, new Vector3(0f, h + 0.3f, 0f), r * 1.02f, 0.6f, M.oxido, null, 32, "Techo");
            Piezas.Caja(t, new Vector3(r + 0.4f, h * 0.5f, 0f), new Vector3(0.8f, h, 0.8f), M.oxido, null, "Escalera");
            return Cerrar(go);
        }

        static GameObject Bidones(Transform padre, Vector3 p, float rumbo)
        {
            var go = Nueva(padre, "Bidones", p, rumbo, 30f, true);
            var t = go.transform;
            go.GetComponent<Estructura>().radioExplosion = 12f;
            go.GetComponent<Estructura>().danioExplosion = 90f;
            var mat = (float)R.NextDouble() < 0.5 ? M.oxido : Pintura();
            int n = 5 + R.Next(8);
            for (int i = 0; i < n; i++)
            {
                var off = new Vector3((i % 4) * 0.65f - 1f, 0.45f, (i / 4) * 0.65f - 0.5f);
                Piezas.Cilindro(t, off, 0.29f, 0.9f, mat, null, 12, "Bidon");
            }
            return Cerrar(go);
        }

        static GameObject PilaTroncos(Transform padre, Vector3 p, float rumbo)
        {
            var go = Nueva(padre, "Troncos", p, rumbo, 90f);
            var t = go.transform;
            var giro = Quaternion.Euler(0f, 0f, 90f);
            float largo = 9f + (float)R.NextDouble() * 4f;
            int base_ = 4 + R.Next(3);
            for (int fila = 0; fila < base_; fila++)
                for (int i = 0; i < base_ - fila; i++)
                {
                    float r = 0.45f + (float)R.NextDouble() * 0.2f;
                    Piezas.Cilindro(t, new Vector3(0f, 0.55f + fila * 0.95f, (i - (base_ - fila - 1) * 0.5f) * 1.05f), r, largo, M.troncos, giro * Quaternion.identity, 12, "Tronco");
                }
            // Los troncos ruedan con fisica al romperse: Estructura los suelta uno a uno.
            return Cerrar(go);
        }

        static GameObject Torre(Transform padre, Vector3 p, float rumbo)
        {
            var go = Nueva(padre, "Torre", p, rumbo, 140f);
            var t = go.transform;
            float alto = 11f + (float)R.NextDouble() * 4f;
            for (int i = 0; i < 4; i++)
            {
                float x = (i & 1) == 0 ? -1.4f : 1.4f, z = (i & 2) == 0 ? -1.4f : 1.4f;
                Piezas.Caja(t, new Vector3(x, alto * 0.5f, z), new Vector3(0.3f, alto, 0.3f), M.madera, null, "Pata");
            }
            Piezas.Caja(t, new Vector3(0f, alto, 0f), new Vector3(4f, 0.25f, 4f), M.madera, null, "Plataforma");
            Piezas.Caja(t, new Vector3(0f, alto + 0.6f, 2f), new Vector3(4f, 1f, 0.1f), M.madera, null, "Baranda");
            Piezas.Caja(t, new Vector3(0f, alto + 0.6f, -2f), new Vector3(4f, 1f, 0.1f), M.madera, null, "Baranda");
            Piezas.Prisma(t, new Vector3(0f, alto + 2.4f, 0f), 4.6f, 1.2f, 4.6f, M.oxido, null, "Techo");
            Piezas.Caja(t, new Vector3(1.6f, alto + 1.4f, 1.6f), new Vector3(0.4f, 0.4f, 0.6f), M.luz, null, "Foco");
            return Cerrar(go);
        }

        static GameObject Generador(Transform padre, Vector3 p, float rumbo)
        {
            var go = Nueva(padre, "Generador", p, rumbo, 90f, true);
            var t = go.transform;
            go.GetComponent<Estructura>().radioExplosion = 10f;
            Piezas.Caja(t, new Vector3(0f, 0.9f, 0f), new Vector3(1.8f, 1.6f, 3.2f), M.pinturaVerde, null, "Cuerpo");
            Piezas.Caja(t, new Vector3(0f, 0.1f, 0f), new Vector3(2f, 0.2f, 3.4f), M.oxido, null, "Bastidor");
            Piezas.Cilindro(t, new Vector3(0.5f, 2.2f, -1f), 0.15f, 1.2f, M.metal, null, 8, "Escape");
            return Cerrar(go);
        }

        static GameObject Balancin(Transform padre, Vector3 p, float rumbo)
        {
            var go = Nueva(padre, "Balancin", p, rumbo, 320f);
            var t = go.transform;
            Piezas.Caja(t, new Vector3(0f, 0.3f, 0f), new Vector3(3f, 0.6f, 11f), M.hormigon, null, "Base");
            Piezas.Caja(t, new Vector3(0f, 3f, 0.8f), new Vector3(0.5f, 5f, 0.5f), M.pinturaAzul, Quaternion.Euler(-12f, 0f, 0f), "Poste");
            Piezas.Caja(t, new Vector3(0f, 3f, -0.8f), new Vector3(0.5f, 5f, 0.5f), M.pinturaAzul, Quaternion.Euler(12f, 0f, 0f), "Poste");
            var balancin = Piezas.Caja(t, new Vector3(0f, 5.6f, 0f), new Vector3(0.6f, 0.8f, 9f), M.pinturaAzul, null, "Viga");
            Piezas.Caja(t, new Vector3(0f, 5.2f, 4.6f), new Vector3(0.7f, 2.2f, 1.2f), M.pinturaAzul, Quaternion.Euler(20f, 0f, 0f), "Cabeza");
            Piezas.Cilindro(t, new Vector3(0f, 2f, -3.6f), 1.2f, 0.5f, M.oxido, Quaternion.Euler(0f, 0f, 90f), 16, "Contrapeso");
            Piezas.Caja(t, new Vector3(0f, 1.2f, -4.2f), new Vector3(1.6f, 1.6f, 2f), M.pinturaVerde, null, "Motor");
            Piezas.Cilindro(t, new Vector3(0f, 1.6f, 5.2f), 0.18f, 3f, M.metal, null, 8, "Varilla");
            balancin.gameObject.AddComponent<Cabeceo>();
            return Cerrar(go);
        }

        static GameObject Excavadora(Transform padre, Vector3 p, float rumbo)
        {
            var go = Nueva(padre, "Excavadora", p, rumbo, 380f);
            var t = go.transform;
            for (int i = -1; i <= 1; i += 2)
                Piezas.Caja(t, new Vector3(i * 1.6f, 0.6f, 0f), new Vector3(0.9f, 1.2f, 5f), M.goma, null, "Oruga");
            Piezas.Caja(t, new Vector3(0f, 1.9f, -0.5f), new Vector3(3.2f, 1.6f, 4f), M.pinturaAmarilla, null, "Cuerpo");
            Piezas.Caja(t, new Vector3(-0.8f, 3.4f, 0.6f), new Vector3(1.4f, 1.6f, 1.6f), M.pinturaAmarilla, null, "Cabina");
            Piezas.Caja(t, new Vector3(-0.8f, 3.5f, 1.42f), new Vector3(1.2f, 1f, 0.05f), M.cristal, null, "Luna");
            Piezas.Caja(t, new Vector3(0.6f, 3.6f, 2.6f), new Vector3(0.6f, 0.7f, 4.5f), M.pinturaAmarilla, Quaternion.Euler(-30f, 0f, 0f), "Pluma");
            Piezas.Caja(t, new Vector3(0.6f, 3.4f, 5.4f), new Vector3(0.5f, 0.6f, 3.2f), M.pinturaAmarilla, Quaternion.Euler(50f, 0f, 0f), "Brazo");
            Piezas.Caja(t, new Vector3(0.6f, 1.5f, 6.1f), new Vector3(1.4f, 1f, 1f), M.oxido, null, "Cazo");
            return Cerrar(go);
        }

        static GameObject Camion(Transform padre, Vector3 p, float rumbo)
        {
            var go = Nueva(padre, "Camion", p, rumbo, 170f, true);
            var t = go.transform;
            go.GetComponent<Estructura>().radioExplosion = 14f;
            Piezas.Caja(t, new Vector3(0f, 1.1f, 0f), new Vector3(2.4f, 0.4f, 11f), M.oxido, null, "Chasis");
            Piezas.Caja(t, new Vector3(0f, 2.3f, 4.2f), new Vector3(2.5f, 2.2f, 2.4f), Pintura(), null, "Cabina");
            Piezas.Caja(t, new Vector3(0f, 2.6f, 5.42f), new Vector3(2.2f, 0.9f, 0.05f), M.cristal, null, "Parabrisas");
            var giro = Quaternion.Euler(0f, 0f, 90f);
            foreach (float z in new[] { 4.2f, -1f, -2.4f, -3.8f })
                for (int i = -1; i <= 1; i += 2)
                    Piezas.Cilindro(t, new Vector3(i * 1.25f, 0.6f, z), 0.6f, 0.5f, M.goma, giro, 14, "Rueda");
            // Carga de troncos.
            for (int i = 0; i < 6; i++)
                Piezas.Cilindro(t, new Vector3((i % 3 - 1) * 0.75f, 1.8f + (i / 3) * 0.7f, -1.5f), 0.36f, 7f, M.troncos, Quaternion.Euler(90f, 0f, 0f), 10, "Tronco");
            return Cerrar(go);
        }

        static GameObject Contenedor(Transform padre, Vector3 p, float rumbo)
        {
            var go = Nueva(padre, "Contenedor", p, rumbo, 160f);
            int alturas = 1 + R.Next(2);
            for (int i = 0; i < alturas; i++)
                Piezas.Caja(go.transform, new Vector3(0f, 1.3f + i * 2.6f, 0f), new Vector3(2.45f, 2.6f, 12.2f), Pintura(), null, "Contenedor");
            return Cerrar(go);
        }

        static GameObject Helipuerto(Transform padre, Vector3 p, float rumbo)
        {
            var go = Nueva(padre, "Helipuerto", p, rumbo, 600f);
            var t = go.transform;
            Piezas.Caja(t, new Vector3(0f, 0.2f, 0f), new Vector3(22f, 0.4f, 22f), M.hormigon, null, "Losa");
            Piezas.Caja(t, new Vector3(-2.5f, 0.42f, 0f), new Vector3(1f, 0.05f, 8f), M.pinturaBlanca, null, "H");
            Piezas.Caja(t, new Vector3(2.5f, 0.42f, 0f), new Vector3(1f, 0.05f, 8f), M.pinturaBlanca, null, "H");
            Piezas.Caja(t, new Vector3(0f, 0.42f, 0f), new Vector3(4f, 0.05f, 1f), M.pinturaBlanca, null, "H");
            return Cerrar(go);
        }

        static GameObject Antena(Transform padre, Vector3 p, float rumbo)
        {
            var go = Nueva(padre, "Antena", p, rumbo, 220f);
            var t = go.transform;
            Piezas.Caja(t, new Vector3(0f, 9f, 0f), new Vector3(1.2f, 18f, 1.2f), M.metal, null, "Mastil");
            var plato = Piezas.Cilindro(t, new Vector3(0f, 18.5f, 0.6f), 3.2f, 0.4f, M.pinturaBlanca, Quaternion.Euler(70f, 0f, 0f), 24, "Plato");
            plato.gameObject.AddComponent<Giro>();
            Piezas.Caja(t, new Vector3(0f, 1.5f, 3f), new Vector3(4f, 3f, 3f), M.pinturaVerde, null, "Caseta");
            return Cerrar(go);
        }

        static GameObject Grua(Transform padre, Vector3 p, float rumbo)
        {
            var go = Nueva(padre, "Grua", p, rumbo, 450f);
            var t = go.transform;
            for (int i = 0; i < 4; i++)
            {
                float x = (i & 1) == 0 ? -4f : 4f, z = (i & 2) == 0 ? -3f : 3f;
                Piezas.Caja(t, new Vector3(x, 9f, z), new Vector3(0.8f, 18f, 0.8f), M.pinturaNaranja, null, "Pata");
            }
            Piezas.Caja(t, new Vector3(0f, 18.5f, 6f), new Vector3(3f, 2f, 30f), M.pinturaNaranja, null, "Pluma");
            Piezas.Caja(t, new Vector3(0f, 20.5f, -2f), new Vector3(4f, 3f, 4f), M.pinturaNaranja, null, "Cabina");
            Piezas.Caja(t, new Vector3(0f, 12f, 16f), new Vector3(0.2f, 12f, 0.2f), M.metal, null, "Cable");
            return Cerrar(go);
        }

        // ------------------------------------------------------------------ Movimiento de maquinas
        /// <summary>La viga del balancin de petroleo cabecea (bombea crudo de la selva).</summary>
        public class Cabeceo : MonoBehaviour
        {
            Quaternion _base;
            float _fase;
            void Start() { _base = transform.localRotation; _fase = Random.value * 6.28f; }
            void Update() => transform.localRotation = _base * Quaternion.Euler(Mathf.Sin(Time.time * 1.1f + _fase) * 14f, 0f, 0f);
        }

        public class Giro : MonoBehaviour
        {
            void Update() => transform.RotateAround(transform.parent.position, Vector3.up, 25f * Time.deltaTime);
        }
    }
}
