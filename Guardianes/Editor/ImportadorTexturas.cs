using UnityEditor;
using UnityEngine;

namespace Guardianes.EditorTools
{
    /// <summary>
    /// Ajustes de importacion de las texturas generadas (Tools/arte) segun el sufijo del archivo:
    ///   _albedo   sRGB; en vegetacion con alfa recortado y mips que conservan la cobertura
    ///   _normal   mapa de normales (BC5)
    ///   _mascara  lineal (mascara HDRP: R metal, G AO, B altura/detalle, A suavidad)
    ///   _grosor   lineal, un canal (translucidez de las hojas)
    ///   _altura   lineal 16 bits sin comprimir
    /// </summary>
    public class ImportadorTexturas : AssetPostprocessor
    {
        const string Raiz = "Assets/_Game/Art/";

        void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith(Raiz)) return;
            var ti = (TextureImporter)assetImporter;
            string nombre = System.IO.Path.GetFileNameWithoutExtension(assetPath).ToLowerInvariant();
            bool vegetacion = assetPath.Contains("/Vegetacion/");

            ti.mipmapEnabled = true;
            ti.anisoLevel = 8;
            ti.filterMode = FilterMode.Trilinear;
            ti.wrapMode = TextureWrapMode.Repeat;
            ti.maxTextureSize = 4096;
            ti.textureCompression = TextureImporterCompression.CompressedHQ;
            ti.streamingMipmaps = true;

            // Laminas de particulas (Tools/arte/efectos): color sRGB con alfa, sin repetir.
            if (assetPath.Contains("/Efectos/"))
            {
                ti.textureType = TextureImporterType.Default;
                ti.sRGBTexture = true;
                ti.alphaSource = TextureImporterAlphaSource.FromInput;
                ti.alphaIsTransparency = true;
                // La espuma de la cascada corre por la lamina: se repite.
                ti.wrapMode = nombre.StartsWith("cascada") ? TextureWrapMode.Repeat : TextureWrapMode.Clamp;
                ti.anisoLevel = nombre.StartsWith("cascada") ? 4 : 1;
                ti.streamingMipmaps = false;
                return;
            }

            if (nombre.EndsWith("_normal"))
            {
                ti.textureType = TextureImporterType.NormalMap;
                ti.sRGBTexture = false;
            }
            else if (nombre.EndsWith("_albedo"))
            {
                ti.textureType = TextureImporterType.Default;
                ti.sRGBTexture = true;
                ti.alphaSource = TextureImporterAlphaSource.FromInput;
                if (vegetacion)
                {
                    ti.alphaIsTransparency = true;
                    ti.mipMapsPreserveCoverage = true;
                    ti.alphaTestReferenceValue = 0.5f;
                    ti.mipmapFilter = TextureImporterMipFilter.KaiserFilter;
                    if (!nombre.Contains("corteza") && !nombre.Contains("tronco") && !nombre.Contains("bambu") && !nombre.Contains("madera"))
                        ti.wrapMode = TextureWrapMode.Clamp;
                }
            }
            else if (nombre.EndsWith("_mascara") || nombre.EndsWith("_grosor"))
            {
                ti.textureType = TextureImporterType.Default;
                ti.sRGBTexture = false;
                ti.alphaSource = nombre.EndsWith("_mascara") ? TextureImporterAlphaSource.FromInput : TextureImporterAlphaSource.None;
                if (vegetacion && !nombre.Contains("corteza") && !nombre.Contains("tronco") && !nombre.Contains("bambu") && !nombre.Contains("madera"))
                    ti.wrapMode = TextureWrapMode.Clamp;
            }
            else if (nombre.EndsWith("_altura"))
            {
                ti.textureType = TextureImporterType.SingleChannel;
                ti.sRGBTexture = false;
                ti.textureCompression = TextureImporterCompression.Uncompressed;
            }
        }
    }
}
