using Unity.Mathematics;

namespace Guardianes.Mundo
{
    /// <summary>
    /// Los tipos de sitio donde Ferrox (u otros humanos) han entrado en la selva. Los valores son
    /// fijos: las partidas guardadas los usan como bits (1 &lt;&lt; tipo). Los nuevos van al final.
    /// </summary>
    public enum TipoZona
    {
        Maderero = 0,
        Petroleo = 1,
        Mina = 2,
        Base = 3,
        Muelle = 4,
        /// <summary>Mina de oro artesanal con balsas de mercurio (Amazonia).</summary>
        Garimpo = 5,
        /// <summary>Dragas de rio que remueven el lecho y sueltan mercurio.</summary>
        Draga = 6,
        /// <summary>Hacienda que quema la selva para pasto.</summary>
        Hacienda = 7,
        /// <summary>Obras de una presa.</summary>
        Presa = 8,
        /// <summary>Campamento de traficantes de animales.</summary>
        Traficantes = 9,
        /// <summary>Campamento de cazadores de criaturas (las Comadrejas de Basiliskos).</summary>
        Cazadores = 10,
        /// <summary>Laboratorio o estacion de investigacion de la compania.</summary>
        Laboratorio = 11,
    }

    /// <summary>Un sitio donde los humanos han entrado en la selva.</summary>
    public struct Zona
    {
        public string nombre;
        public TipoZona tipo;
        public float2 centro;
        public float radio;
        /// <summary>Cota a la que se allana (NaN = la media del terreno).</summary>
        public float cota;
        /// <summary>0..1: cuanto suelo contaminado deja alrededor.</summary>
        public float contaminacion;
    }
}
