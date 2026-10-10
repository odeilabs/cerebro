using System;
using System.Collections.Generic;
using Guardianes.Combate;
using UnityEngine;

namespace Guardianes.Sensacion
{
    /// <summary>
    /// Ayuda invisible para acertar lo que el jugador quería (soft lock): entre los candidatos dentro
    /// de un cono delante del ataque elige el mejor (más centrado y más cerca) y corrige un poco la
    /// dirección hacia él. Genérica para todas las criaturas: mordisco, palmada, picado de un ave,
    /// embestida bajo el agua, la cabeza de una hidra que elige su blanco. La dirección puede ser 3D
    /// (vuelo, buceo); para las criaturas de suelo se puede aplanar con 'plano'.
    /// </summary>
    public static class AsistenciaPunteria
    {
        /// <summary>
        /// El mejor candidato en el cono (o el valor por defecto si no hay ninguno). Puntúa con el
        /// ángulo pesando el doble que la distancia relativa, como hacen los juegos de acción con lock blando.
        /// </summary>
        public static T Elegir<T>(Vector3 origen, Vector3 direccion, float alcance, float conoGrados,
            IEnumerable<T> candidatos, Func<T, Vector3> posicion, Func<T, bool> vale = null, bool plano = false)
        {
            if (direccion.sqrMagnitude < 1e-6f || alcance <= 0f) return default;
            if (plano) direccion.y = 0f;
            direccion.Normalize();
            T mejor = default;
            float mejorPuntos = float.MaxValue;
            float medio = Mathf.Max(0.1f, conoGrados * 0.5f);
            foreach (var c in candidatos)
            {
                if (c == null || (vale != null && !vale(c))) continue;
                Vector3 hacia = posicion(c) - origen;
                if (plano) hacia.y = 0f;
                float d = hacia.magnitude;
                if (d > alcance) continue;
                float ang = d < 1e-4f ? 0f : Vector3.Angle(direccion, hacia);
                if (ang > medio) continue;
                float puntos = 2f * (ang / medio) + d / alcance;
                if (puntos < mejorPuntos) { mejorPuntos = puntos; mejor = c; }
            }
            return mejor;
        }

        /// <summary>Gira 'direccion' hacia 'haciaBlanco' como mucho 'maxGrados' (conserva el módulo).</summary>
        public static Vector3 Corregir(Vector3 direccion, Vector3 haciaBlanco, float maxGrados)
        {
            if (direccion.sqrMagnitude < 1e-8f || haciaBlanco.sqrMagnitude < 1e-8f) return direccion;
            return Vector3.RotateTowards(direccion, haciaBlanco.normalized * direccion.magnitude, maxGrados * Mathf.Deg2Rad, 0f);
        }

        static readonly Collider[] _cols = new Collider[64];
        static readonly List<Salud> _saludes = new List<Salud>();

        /// <summary>
        /// La mejor Salud viva en el cono (por colisionadores en la esfera del alcance), sin contar
        /// la del propio atacante. Para blancos que no tienen colisionador, Elegir con la lista del juego.
        /// </summary>
        public static Salud BuscarSalud(Vector3 origen, Vector3 direccion, float alcance, float conoGrados,
            GameObject atacante = null, int capas = ~0, Func<Salud, bool> vale = null, bool plano = false)
        {
            int n = Physics.OverlapSphereNonAlloc(origen, alcance, _cols, capas, QueryTriggerInteraction.Ignore);
            _saludes.Clear();
            for (int i = 0; i < n; i++)
            {
                var s = _cols[i].GetComponentInParent<Salud>();
                if (s == null || _saludes.Contains(s)) continue;
                if (atacante != null && s.transform.IsChildOf(atacante.transform)) continue;
                _saludes.Add(s);
            }
            return Elegir(origen, direccion, alcance, conoGrados, _saludes, s => s.transform.position,
                s => s != null && !s.Muerto && (vale == null || vale(s)), plano);
        }
    }
}
