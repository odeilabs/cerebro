using UnityEngine;

namespace Guardianes.Criaturas
{
    /// <summary>
    /// El pelo por capas de un animal de la via de juego de AssetsCreator (ultra/animal_juego.py): la malla de capas
    /// se pinta una vez por material de capa (Guardianes/Manto), de la raiz a la punta. Todo lo del pelo vive en esos
    /// materiales; aqui solo se decide cuantas capas se pintan. Capas(n) reparte n entre todas, siempre con la ultima,
    /// asi que el pelo mide lo mismo y solo es menos denso; con 0 se apaga y queda la piel con su atlas.
    /// Lo monta Guardianes.EditorTools.MantoAssetsCreator. Portado de AssetsCreatorManto.
    /// </summary>
    public class MantoAnimal : MonoBehaviour
    {
        [Tooltip("El SkinnedMeshRenderer de la malla de capas")]
        public SkinnedMeshRenderer capas;
        [Tooltip("Los materiales de todas las capas, de la raiz a la punta")]
        public Material[] todas;

        public int Pintadas => capas == null || !capas.enabled ? 0 : capas.sharedMaterials.Length;

        /// <summary>Pinta `cuantas` capas repartidas entre todas; 0 las apaga.</summary>
        public void Capas(int cuantas)
        {
            if (capas == null || todas == null || todas.Length == 0) return;
            capas.enabled = cuantas > 0;
            if (cuantas <= 0) return;
            cuantas = Mathf.Min(cuantas, todas.Length);
            var elegidas = new Material[cuantas];
            for (int i = 0; i < cuantas; i++)
                elegidas[i] = todas[Mathf.CeilToInt((i + 1) * todas.Length / (float)cuantas) - 1];
            capas.sharedMaterials = elegidas;
        }
    }
}
