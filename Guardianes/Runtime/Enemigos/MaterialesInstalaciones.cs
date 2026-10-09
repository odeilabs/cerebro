using UnityEngine;

namespace Guardianes.Enemigos
{
    /// <summary>Materiales PBR de las instalaciones humanas (los crea el editor desde AssetsCreator).</summary>
    [CreateAssetMenu(menuName = "Guardianes/Materiales de instalaciones")]
    public class MaterialesInstalaciones : ScriptableObject
    {
        public Material madera, oxido, metal, hormigon, lona, plastico, goma, troncos;
        public Material pinturaAmarilla, pinturaVerde, pinturaRoja, pinturaAzul, pinturaBlanca, pinturaNaranja;
        public Material cristal, luz;
    }
}
