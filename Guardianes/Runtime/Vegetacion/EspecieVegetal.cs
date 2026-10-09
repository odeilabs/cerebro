using System;
using Guardianes.Vegetacion.Generacion;
using UnityEngine;
using UnityEngine.Rendering;

namespace Guardianes.Vegetacion
{
    public enum TipoVegetal
    {
        /// <summary>Gigantes emergentes (ceiba): no se derriban, Kong trepa por ellos.</summary>
        Gigante,
        Arbol,
        Palmera,
        Bambu,
        Arbusto,
        /// <summary>Plantas de suelo (helechos, hierba...): muchas, cercanas, sin colision.</summary>
        Suelo,
        Roca,
    }

    /// <summary>
    /// Una especie de la selva. Las mallas no son assets: se generan al cargar (Plantas.Generar, con
    /// semilla fija) para cada variante y LOD y se guardan en memoria. Aqui van los parametros, los
    /// materiales, las distancias de LOD y los datos de colision y destruccion.
    /// </summary>
    [CreateAssetMenu(menuName = "Guardianes/Especie vegetal")]
    public class EspecieVegetal : ScriptableObject
    {
        public TipoVegetal tipo = TipoVegetal.Arbol;
        public Plantas.Forma forma = Plantas.Forma.Dosel;
        [Range(1, 8)] public int variantes = 3;
        public uint semilla = 1;

        [Header("LODs (cerca, media, lejos)")]
        [Tooltip("Distancia maxima de cada LOD (m) para escala 1")]
        public float[] distancias = { 90f, 350f, 2000f };
        public ShadowCastingMode[] sombras = { ShadowCastingMode.On, ShadowCastingMode.On, ShadowCastingMode.On };

        [Header("Materiales")]
        [Tooltip("Submalla 0: madera, tallos (o la roca)")]
        public Material materialMadera;
        [Tooltip("Submalla 1: follaje")]
        public Material materialHojas;

        [Header("Colision y destruccion")]
        [Tooltip("0 = sin colision")]
        public float radioTronco = 0.4f;
        public float alturaTronco = 8f;
        public bool destructible = true;
        [Tooltip("Golpe necesario para derribarla (Kong da 100 con un punetazo normal)")]
        public float resistencia = 60f;

        [NonSerialized] Mesh[] _mallas;
        [NonSerialized] Bounds _limites;

        public int Lods => distancias.Length;
        public Material[] Materiales => forma == Plantas.Forma.Roca ? new[] { materialMadera } : new[] { materialMadera, materialHojas };

        /// <summary>Malla de una variante y LOD (se genera la primera vez).</summary>
        public Mesh Malla(int variante, int lod)
        {
            Preparar();
            return _mallas[Mathf.Clamp(variante, 0, variantes - 1) * Lods + Mathf.Clamp(lod, 0, Lods - 1)];
        }

        /// <summary>Caja que envuelve todas las variantes (LOD 0) a escala 1.</summary>
        public Bounds Limites { get { Preparar(); return _limites; } }

        public void Preparar()
        {
            if (_mallas != null && _mallas.Length == variantes * Lods && _mallas[0] != null) return;
            Liberar();
            _mallas = new Mesh[variantes * Lods];
            bool primera = true;
            for (int v = 0; v < variantes; v++)
            for (int l = 0; l < Lods; l++)
            {
                var m = Plantas.Generar(forma, semilla * 92821u + (uint)v * 68917u + 1u, l, $"{name}_v{v}_lod{l}");
                m.hideFlags = HideFlags.DontSave;
                _mallas[v * Lods + l] = m;
                if (l == 0)
                {
                    if (primera) { _limites = m.bounds; primera = false; }
                    else _limites.Encapsulate(m.bounds);
                }
            }
        }

        public void Liberar()
        {
            if (_mallas == null) return;
            foreach (var m in _mallas)
            {
                if (m == null) continue;
                if (Application.isPlaying) Destroy(m); else DestroyImmediate(m);
            }
            _mallas = null;
        }

        void OnValidate() => Liberar();
        void OnDisable() => Liberar();
    }
}
