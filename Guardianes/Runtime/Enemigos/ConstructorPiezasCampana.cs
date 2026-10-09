using UnityEngine;

namespace Guardianes.Enemigos
{
    /// <summary>Lo que la campana levanta suelto, fuera de los planos de las zonas humanas.</summary>
    public enum TipoPieza
    {
        Barracon, Torre, Deposito, Contenedor, Generador,
        /// <summary>Estacion de bombeo del oleoducto (el crudo que gotea al rio).</summary>
        Valvula,
        /// <summary>Jaula de los traficantes de animales.</summary>
        Jaula,
        /// <summary>Choza de los islenos (palafito de madera con tejado de hojas).</summary>
        Choza,
        /// <summary>Lancha de desembarco de la compania, varada en la orilla.</summary>
        Lancha,
        /// <summary>Tramo de la presa que desvia el rio de la meseta.</summary>
        Presa,
        /// <summary>Carga de dinamita en el volcan (explota al romperla).</summary>
        Carga,
        /// <summary>Estacion de investigacion de la doctora Varga.</summary>
        Estacion,
    }

    /// <summary>
    /// Piezas sueltas para la campana (oleoducto, jaulas, chozas, presa...), con las mismas reglas
    /// que las instalaciones de las zonas: Salud + Estructura, piezas que saltan con fisica al
    /// romperse y una caja de colision que las envuelve.
    /// </summary>
    public static partial class ConstructorInstalaciones
    {
        public static GameObject Levantar(TipoPieza tipo, Transform padre, Vector3 p, float rumbo, MaterialesInstalaciones materiales, int semilla)
        {
            M = materiales;
            R = new System.Random(semilla);
            switch (tipo)
            {
                case TipoPieza.Barracon: return Barracon(padre, p, rumbo);
                case TipoPieza.Torre: return Torre(padre, p, rumbo);
                case TipoPieza.Deposito: return Deposito(padre, p, rumbo);
                case TipoPieza.Contenedor: return Contenedor(padre, p, rumbo);
                case TipoPieza.Generador: return Generador(padre, p, rumbo);
                case TipoPieza.Valvula: return Valvula(padre, p, rumbo);
                case TipoPieza.Jaula: return Jaula(padre, p, rumbo);
                case TipoPieza.Choza: return Choza(padre, p, rumbo);
                case TipoPieza.Lancha: return Lancha(padre, p, rumbo);
                case TipoPieza.Presa: return Presa(padre, p, rumbo);
                case TipoPieza.Carga: return Carga(padre, p, rumbo);
                default: return Estacion(padre, p, rumbo);
            }
        }

        static GameObject Valvula(Transform padre, Vector3 p, float rumbo)
        {
            var go = Nueva(padre, "Valvula", p, rumbo, 200f, true);
            var t = go.transform;
            go.GetComponent<Estructura>().radioExplosion = 16f;
            var tumbado = Quaternion.Euler(90f, 0f, 0f);
            // El tubo entra y sale de la estacion; gotea crudo en un charco negro.
            Piezas.Cilindro(t, new Vector3(0f, 1.6f, 0f), 0.75f, 22f, M.oxido, tumbado, 16, "Tubo");
            for (int i = -1; i <= 1; i++)
                Piezas.Caja(t, new Vector3(0f, 0.5f, i * 8f), new Vector3(2f, 1f, 0.6f), M.hormigon, null, "Apoyo");
            Piezas.Caja(t, new Vector3(2.4f, 1.6f, 0f), new Vector3(3f, 3.2f, 4f), M.pinturaAmarilla, null, "Bomba");
            Piezas.Cilindro(t, new Vector3(0f, 3.1f, 0f), 0.25f, 1.6f, M.metal, null, 8, "Vastago");
            Piezas.Cilindro(t, new Vector3(0f, 3.95f, 0f), 1.1f, 0.18f, M.pinturaRoja, null, 16, "Volante");
            Piezas.Cilindro(t, new Vector3(-2.5f, 0.04f, 1f), 3.2f, 0.06f, M.goma, null, 20, "Charco");
            return Cerrar(go);
        }

        static GameObject Jaula(Transform padre, Vector3 p, float rumbo)
        {
            var go = Nueva(padre, "Jaula", p, rumbo, 90f);
            var t = go.transform;
            float lado = 4.5f, alto = 3.6f;
            Piezas.Caja(t, new Vector3(0f, 0.15f, 0f), new Vector3(lado, 0.3f, lado), M.madera, null, "Suelo");
            Piezas.Caja(t, new Vector3(0f, alto, 0f), new Vector3(lado, 0.2f, lado), M.oxido, null, "Techo");
            float v = (lado - 0.2f) * 0.5f;
            for (int cara = 0; cara < 4; cara++)
                for (int i = 0; i < 6; i++)
                {
                    float u = (i / 5f - 0.5f) * (lado - 0.2f);
                    var c = cara == 0 ? new Vector3(u, alto * 0.5f, v) : cara == 1 ? new Vector3(u, alto * 0.5f, -v)
                          : cara == 2 ? new Vector3(v, alto * 0.5f, u) : new Vector3(-v, alto * 0.5f, u);
                    Piezas.Cilindro(t, c, 0.06f, alto, M.metal, null, 6, "Barrote");
                }
            // El animal atrapado, hecho un ovillo de pelo oscuro.
            Piezas.Cilindro(t, new Vector3(0f, 0.75f, 0f), 1f, 0.9f, M.goma, null, 12, "Animal");
            return Cerrar(go);
        }

        static GameObject Choza(Transform padre, Vector3 p, float rumbo)
        {
            var go = Nueva(padre, "Choza", p, rumbo, 150f);
            var t = go.transform;
            float ancho = 5f + (float)R.NextDouble() * 1.5f, largo = 6f + (float)R.NextDouble() * 2f, suelo = 1.4f;
            for (int i = 0; i < 4; i++)
            {
                float x = (i & 1) == 0 ? -ancho * 0.45f : ancho * 0.45f, z = (i & 2) == 0 ? -largo * 0.45f : largo * 0.45f;
                Piezas.Cilindro(t, new Vector3(x, suelo * 0.5f, z), 0.16f, suelo, M.troncos, null, 8, "Pilote");
            }
            Piezas.Caja(t, new Vector3(0f, suelo, 0f), new Vector3(ancho, 0.2f, largo), M.madera, null, "Suelo");
            Piezas.Caja(t, new Vector3(-ancho * 0.5f + 0.1f, suelo + 1.2f, 0f), new Vector3(0.15f, 2.2f, largo - 0.4f), M.madera, null, "Pared");
            Piezas.Caja(t, new Vector3(ancho * 0.5f - 0.1f, suelo + 1.2f, 0f), new Vector3(0.15f, 2.2f, largo - 0.4f), M.madera, null, "Pared");
            Piezas.Caja(t, new Vector3(0f, suelo + 1.2f, -largo * 0.5f + 0.2f), new Vector3(ancho, 2.2f, 0.15f), M.madera, null, "Pared");
            // Tejado de fibra de palma (la corteza de la selva) que vuela mas alla de las paredes.
            Piezas.Prisma(t, new Vector3(0f, suelo + 2.3f, 0f), ancho + 1.6f, 2.4f, largo + 1.4f, M.troncos != null ? M.troncos : M.lona, null, "Tejado");
            return Cerrar(go);
        }

        static GameObject Lancha(Transform padre, Vector3 p, float rumbo)
        {
            var go = Nueva(padre, "Lancha", p, rumbo, 160f, true);
            var t = go.transform;
            go.GetComponent<Estructura>().radioExplosion = 12f;
            Piezas.Caja(t, new Vector3(0f, 0.9f, -1f), new Vector3(4.2f, 1.8f, 12f), M.pinturaVerde, null, "Casco");
            Piezas.Prisma(t, new Vector3(0f, 0f, 5.8f), 4.2f, 1.8f, 2.6f, M.pinturaVerde, Quaternion.Euler(0f, 90f, 0f), "Proa");
            Piezas.Caja(t, new Vector3(0f, 2.4f, -5f), new Vector3(2.4f, 1.6f, 2f), M.pinturaVerde, null, "Puente");
            Piezas.Caja(t, new Vector3(0f, 1.3f, -7.4f), new Vector3(1f, 1.6f, 0.9f), M.metal, null, "Motor");
            return Cerrar(go);
        }

        static GameObject Presa(Transform padre, Vector3 p, float rumbo)
        {
            var go = Nueva(padre, "Presa", p, rumbo, 520f);
            var t = go.transform;
            Piezas.Caja(t, new Vector3(0f, 4f, 0f), new Vector3(22f, 8f, 3f), M.hormigon, null, "Muro");
            Piezas.Caja(t, new Vector3(0f, 8.4f, 0f), new Vector3(23f, 0.8f, 3.6f), M.hormigon, null, "Coronacion");
            for (int i = -1; i <= 1; i++)
                Piezas.Caja(t, new Vector3(i * 6f, 3f, 1.6f), new Vector3(3f, 4f, 0.3f), M.oxido, null, "Compuerta");
            Piezas.Caja(t, new Vector3(9f, 10f, 0f), new Vector3(3f, 2.6f, 3f), M.pinturaBlanca, null, "Caseta");
            return Cerrar(go);
        }

        static GameObject Carga(Transform padre, Vector3 p, float rumbo)
        {
            var go = Nueva(padre, "Carga", p, rumbo, 60f, true);
            var t = go.transform;
            var e = go.GetComponent<Estructura>();
            e.radioExplosion = 22f;
            e.danioExplosion = 120f;
            for (int i = 0; i < 6; i++)
                Piezas.Caja(t, new Vector3((i % 3 - 1) * 0.85f, 0.4f + (i / 3) * 0.8f, 0f), new Vector3(0.8f, 0.75f, 1.2f), M.pinturaRoja, null, "Caja");
            Piezas.Caja(t, new Vector3(0f, 2f, 0f), new Vector3(0.5f, 0.4f, 0.5f), M.luz, null, "Detonador");
            Piezas.Cilindro(t, new Vector3(0f, 1.2f, 2.5f), 0.05f, 5f, M.goma, Quaternion.Euler(90f, 0f, 0f), 6, "Cable");
            return Cerrar(go);
        }

        static GameObject Estacion(Transform padre, Vector3 p, float rumbo)
        {
            var go = Nueva(padre, "Estacion", p, rumbo, 400f);
            var t = go.transform;
            Piezas.Caja(t, new Vector3(0f, 0.25f, 0f), new Vector3(8f, 0.5f, 10f), M.hormigon, null, "Losa");
            Piezas.Caja(t, new Vector3(0f, 2f, 0f), new Vector3(6.5f, 3f, 8.5f), M.pinturaBlanca, null, "Modulo");
            Piezas.Prisma(t, new Vector3(0f, 3.5f, 0f), 7f, 1f, 9f, M.pinturaVerde, null, "Tejado");
            Piezas.Caja(t, new Vector3(3.3f, 2.2f, 0f), new Vector3(0.05f, 1f, 4f), M.cristal, null, "Ventana");
            Piezas.Caja(t, new Vector3(-2f, 6.5f, 3f), new Vector3(0.25f, 6f, 0.25f), M.metal, null, "Mastil");
            Piezas.Cilindro(t, new Vector3(-2f, 9.4f, 3f), 0.9f, 0.15f, M.pinturaBlanca, Quaternion.Euler(60f, 0f, 0f), 16, "Plato");
            Piezas.Caja(t, new Vector3(2f, 0.9f, -5.6f), new Vector3(2.2f, 1.3f, 1.6f), M.pinturaVerde, null, "Generador");
            return Cerrar(go);
        }
    }
}
