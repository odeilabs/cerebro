using System.Collections.Generic;
using Guardianes.Vegetacion;
using Unity.Mathematics;
using UnityEngine;

namespace Guardianes.Combate
{
    /// <summary>
    /// Golpes de area: dana todo lo que tenga Salud en la esfera (con caida por distancia), empuja
    /// los cuerpos fisicos y tumba la vegetacion que no resista el golpe.
    /// </summary>
    public static class Impacto
    {
        static readonly Collider[] s_Colliders = new Collider[256];
        static readonly HashSet<Salud> s_Tocados = new HashSet<Salud>();
        static readonly List<int> s_Plantas = new List<int>();

        public static event System.Action<Vector3, float, TipoDanio> AlImpactar;

        /// <param name="centro">Centro de la esfera de dano.</param>
        /// <param name="radio">Radio (m).</param>
        /// <param name="danio">Dano en el centro; en el borde queda el (1 - caida).</param>
        /// <param name="ignorar">Raiz que no se dana (normalmente quien golpea).</param>
        /// <param name="perdonar">Lo que este golpe no dana (la embestida aparta a la gente desarmada).</param>
        public static int Golpear(Vector3 centro, float radio, float danio, float empuje, TipoDanio tipo, GameObject ignorar,
                                  float caida = 0.6f, bool vegetacion = true, System.Predicate<Salud> perdonar = null)
        {
            s_Tocados.Clear();
            int n = Physics.OverlapSphereNonAlloc(centro, radio, s_Colliders, ~0, QueryTriggerInteraction.Ignore);
            int tocados = 0;
            for (int i = 0; i < n; i++)
            {
                var col = s_Colliders[i];
                if (col is TerrainCollider) continue;
                if (ignorar != null && col.transform.IsChildOf(ignorar.transform)) continue;
                // ClosestPoint solo vale para primitivas y mallas convexas.
                Vector3 punto = col is MeshCollider mc && !mc.convex ? col.bounds.ClosestPoint(centro) : col.ClosestPoint(centro);
                float d = Vector3.Distance(punto, centro);
                float k = 1f - caida * Mathf.Clamp01(d / radio);
                Vector3 dir = punto - centro;
                dir = dir.sqrMagnitude > 1e-4f ? dir.normalized : Vector3.up;

                var salud = col.GetComponentInParent<Salud>();
                if (salud != null && perdonar != null && perdonar(salud)) continue;
                if (salud != null && s_Tocados.Add(salud))
                {
                    salud.Danar(new DatosDanio
                    {
                        cantidad = danio * k,
                        punto = punto,
                        direccion = (dir + Vector3.up * 0.35f).normalized,
                        empuje = empuje * k,
                        tipo = tipo,
                        origen = ignorar,
                    });
                    tocados++;
                }
                var rb = col.attachedRigidbody;
                if (rb != null && !rb.isKinematic)
                    rb.AddExplosionForce(empuje * k * rb.mass * 0.6f, centro, radio * 1.2f, 0.6f, ForceMode.Impulse);
            }

            if (vegetacion) TumbarVegetacion(centro, radio, danio, empuje);
            AlImpactar?.Invoke(centro, radio, tipo);
            return tocados;
        }

        /// <summary>Tumba las plantas cuya resistencia no aguanta el golpe (las ceibas y las rocas grandes resisten).</summary>
        public static void TumbarVegetacion(Vector3 centro, float radio, float danio, float empuje)
        {
            var veg = RenderizadorVegetacion.Instancia;
            if (veg == null) return;
            veg.Buscar(new float3(centro.x, centro.y, centro.z), radio, s_Plantas);
            foreach (int i in s_Plantas)
            {
                var esp = veg.EspecieDe(i);
                if (!esp.destructible) continue;
                Vector3 p = veg.MatrizDe(i).c3.xyz;
                float d = Vector3.Distance(new Vector3(p.x, centro.y, p.z), centro);
                float k = 1f - 0.6f * Mathf.Clamp01(d / radio);
                if (danio * k < esp.resistencia * veg.EscalaDe(i)) continue;
                Vector3 dir = new Vector3(p.x - centro.x, 0f, p.z - centro.z);
                Derribos.Derribar(i, dir.sqrMagnitude > 1e-3f ? dir : Vector3.forward, Mathf.Max(4f, empuje * k));
            }
        }
    }
}
