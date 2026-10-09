using Unity.Mathematics;
using UnityEngine;

namespace Guardianes.Mundo
{
    /// <summary>
    /// Medidas del mapa de un juego: lado, teselas de terreno, resoluciones y rango de alturas. Van
    /// en la cabecera de los datos del mundo (DatosMundo) y los jobs Burst las reciben como campo
    /// (no pueden leer estaticos que cambian). 1 unidad = 1 m; el mapa esta centrado en el origen.
    /// </summary>
    [System.Serializable]
    public struct MedidasMundo
    {
        [Tooltip("Lado del mundo (cuadrado) en metros")]
        public float tamano;
        [Tooltip("Teselas de terreno por lado (cada una un Terrain de Unity)")]
        public int teselas;
        [Tooltip("Muestras del heightmap por tesela (2^n + 1)")]
        public int resolucionAlturas;
        [Tooltip("Resolucion de las capas del suelo por tesela")]
        public int resolucionCapas;
        [Tooltip("Cota minima del terreno (m)")]
        public float alturaMinima;
        [Tooltip("Rango total de alturas (m)")]
        public float rangoAlturas;
        [Tooltip("Nivel del mar (m). Sin mar (hayMar = false) es solo la cota de referencia")]
        public float nivelMar;
        [Tooltip("El mapa tiene mar alrededor (la isla de Kong) o es tierra adentro (la Amazonia)")]
        public bool hayMar;

        public float TamanoTesela => tamano / teselas;
        /// <summary>Muestras del heightmap global (las teselas comparten bordes).</summary>
        public int ResolucionGlobal => (resolucionAlturas - 1) * teselas + 1;
        public float2 Origen => new float2(-tamano * 0.5f, -tamano * 0.5f);
        public float PasoAlturas => tamano / (ResolucionGlobal - 1);
        public bool Valida => tamano > 0f && teselas > 0 && resolucionAlturas > 1 && rangoAlturas > 0f;

        /// <summary>Las de la isla de Kong (4 km, 4x4 teselas a 1 m, de -60 a 660 m, con mar).</summary>
        public static MedidasMundo PorDefecto => new MedidasMundo
        {
            tamano = 4096f,
            teselas = 4,
            resolucionAlturas = 1025,
            resolucionCapas = 1024,
            alturaMinima = -60f,
            rangoAlturas = 720f,
            nivelMar = 0f,
            hayMar = true,
        };

        public float2 MuestraAMundo(int i, int j) => Origen + new float2(i, j) * PasoAlturas;

        public static Vector3 AMundo(float2 xz, float y) => new Vector3(xz.x, y, xz.y);

        public override string ToString() =>
            $"{tamano:F0} m, {teselas}x{teselas} teselas de {resolucionAlturas}, alturas {alturaMinima:F0}..{alturaMinima + rangoAlturas:F0} m{(hayMar ? ", con mar" : "")}";
    }
}
