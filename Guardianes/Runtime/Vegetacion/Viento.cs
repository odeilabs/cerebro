using System.Collections.Generic;
using UnityEngine;

namespace Guardianes.Vegetacion
{
    /// <summary>
    /// Viento global de la selva y empujones a la maleza. Alimenta las globales que lee
    /// Shaders/Vegetacion/Viento.hlsl (shader Guardianes/Vegetacion) en todas sus pasadas.
    /// </summary>
    [ExecuteAlways]
    [DefaultExecutionOrder(-50)]
    public class Viento : MonoBehaviour
    {
        public const int MaxInteractores = 8;

        [Tooltip("Direccion del viento en grados (0 = hacia +Z).")]
        [Range(0f, 360f)] public float rumbo = 35f;
        [Tooltip("0 = calma, 0.4 = brisa de selva, 1 = temporal.")]
        [Range(0f, 1.5f)] public float fuerza = 0.35f;
        [Range(0f, 1f)] public float turbulencia = 0.55f;
        [Tooltip("Velocidad a la que viajan los frentes de rafaga (m/s escalados).")]
        public float velocidadRafagas = 4f;
        [Tooltip("Escala espacial de las rafagas: mas alto = rafagas mas pequenas.")]
        public float escalaRafagas = 0.08f;
        [Tooltip("Amplitud del balanceo de ramas en metros.")]
        public float amplitudRamas = 0.18f;
        [Tooltip("Amplitud del aleteo de hojas en metros.")]
        public float amplitudHojas = 0.035f;

        static readonly int IdViento = Shader.PropertyToID("_GD_Viento");
        static readonly int IdParams = Shader.PropertyToID("_GD_VientoParams");
        static readonly int IdInteractores = Shader.PropertyToID("_GD_Interactores");
        static readonly int IdNumInteractores = Shader.PropertyToID("_GD_NumInteractores");

        static readonly List<IInteractorVegetacion> s_Interactores = new List<IInteractorVegetacion>();
        readonly Vector4[] _bufer = new Vector4[MaxInteractores];

        public static Viento Instancia { get; private set; }

        public Vector3 Direccion
        {
            get
            {
                float r = rumbo * Mathf.Deg2Rad;
                return new Vector3(Mathf.Sin(r), 0f, Mathf.Cos(r));
            }
        }

        public static void Registrar(IInteractorVegetacion i)
        {
            if (!s_Interactores.Contains(i)) s_Interactores.Add(i);
        }

        public static void Quitar(IInteractorVegetacion i) => s_Interactores.Remove(i);

        void OnEnable()
        {
            Instancia = this;
            Aplicar();
        }

        void OnDisable()
        {
            if (Instancia == this) Instancia = null;
            Shader.SetGlobalFloat(IdNumInteractores, 0f);
        }

        void LateUpdate() => Aplicar();

        void Aplicar()
        {
            Vector3 d = Direccion;
            Shader.SetGlobalVector(IdViento, new Vector4(d.x, d.z, fuerza, turbulencia));
            Shader.SetGlobalVector(IdParams, new Vector4(velocidadRafagas, escalaRafagas, amplitudRamas, amplitudHojas));

            int n = 0;
            for (int i = 0; i < s_Interactores.Count && n < MaxInteractores; i++)
            {
                var it = s_Interactores[i];
                if (it == null || !it.Activo) continue;
                Vector3 p = it.PosicionEmpuje;
                _bufer[n++] = new Vector4(p.x, p.y, p.z, it.RadioEmpuje);
            }
            for (int i = n; i < MaxInteractores; i++) _bufer[i] = Vector4.zero;
            Shader.SetGlobalVectorArray(IdInteractores, _bufer);
            Shader.SetGlobalFloat(IdNumInteractores, n);
        }
    }

    /// <summary>Algo que aparta la maleza al pasar (Kong, sus manos, un vehiculo...).</summary>
    public interface IInteractorVegetacion
    {
        bool Activo { get; }
        Vector3 PosicionEmpuje { get; }
        float RadioEmpuje { get; }
    }
}
