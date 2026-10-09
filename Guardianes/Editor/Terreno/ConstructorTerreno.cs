using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;

namespace Guardianes.EditorTools.Terreno
{
    /// <summary>
    /// Assets del terreno: las 8 capas PBR (TerrainLayer), el material HDRP/TerrainLit con mezcla por
    /// altura y los datos compactos del mundo (.bytes) que TerrenoMundo convierte en terrenos al
    /// cargar. Las texturas de las capas las genera Tools~/arte/terreno (en el orden de
    /// TerrenoMundo.Capa).
    /// </summary>
    public static class ConstructorTerreno
    {
        public const string CarpetaCapas = "Assets/_Game/Art/Terreno/Capas";
        public const string RutaMaterial = "Assets/_Game/Art/Terreno/TerrenoSelva.mat";

        public struct DefCapa
        {
            public string nombre;
            public float tamano;      // metros por repeticion
            public float normal;      // escala del mapa de normales
            public Color tinte;       // multiplicador del albedo (remap max)
        }

        // Las normales vienen horneadas a escala fisica para el tamano de tesela de Tools~/arte/terreno
        // (hojarasca 4 m, roca 8 m...); aqui las teselas son algo mayores (menos repeticion vista desde
        // la altura de una criatura gigante) y la escala de normales baja en proporcion (viejo / nuevo).
        public static readonly DefCapa[] CapasSelva =
        {
            new DefCapa { nombre = "hojarasca", tamano = 5f, normal = 0.85f, tinte = Color.white },
            new DefCapa { nombre = "barro", tamano = 5f, normal = 0.75f, tinte = Color.white },
            new DefCapa { nombre = "musgo", tamano = 3.5f, normal = 0.9f, tinte = Color.white },
            new DefCapa { nombre = "roca", tamano = 10f, normal = 1.05f, tinte = Color.white },
            new DefCapa { nombre = "guijarros", tamano = 3.5f, normal = 0.9f, tinte = Color.white },
            new DefCapa { nombre = "arena", tamano = 7f, normal = 0.8f, tinte = new Color(0.92f, 0.92f, 0.92f) },
            new DefCapa { nombre = "hierba_suelo", tamano = 4.5f, normal = 0.9f, tinte = Color.white },
            new DefCapa { nombre = "contaminado", tamano = 5f, normal = 0.85f, tinte = Color.white },
        };

        // ------------------------------------------------------------------ Capas y material
        public static TerrainLayer[] CrearCapas(DefCapa[] defs = null)
        {
            defs ??= CapasSelva;
            Directory.CreateDirectory(CarpetaCapas);
            var capas = new TerrainLayer[defs.Length];
            for (int i = 0; i < defs.Length; i++)
            {
                var d = defs[i];
                string ruta = $"{CarpetaCapas}/{d.nombre}.terrainlayer";
                var capa = AssetDatabase.LoadAssetAtPath<TerrainLayer>(ruta);
                bool nueva = capa == null;
                if (nueva) capa = new TerrainLayer();

                string baseTex = $"{CarpetaCapas}/{d.nombre}/{d.nombre}";
                capa.diffuseTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(baseTex + "_albedo.png");
                capa.normalMapTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(baseTex + "_normal.png");
                capa.maskMapTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(baseTex + "_mascara.png");
                capa.tileSize = new Vector2(d.tamano, d.tamano);
                capa.normalScale = d.normal;
                capa.diffuseRemapMin = Vector4.zero;
                capa.diffuseRemapMax = new Vector4(d.tinte.r, d.tinte.g, d.tinte.b, 1f);
                capa.maskMapRemapMin = Vector4.zero;
                capa.maskMapRemapMax = Vector4.one;
                capa.smoothness = 0.25f;
                capa.metallic = 0f;
                if (capa.diffuseTexture == null)
                    Debug.LogWarning($"[GD] Capa {d.nombre}: falta {baseTex}_albedo.png (se vera gris)");

                if (nueva) AssetDatabase.CreateAsset(capa, ruta);
                else EditorUtility.SetDirty(capa);
                capas[i] = capa;
            }
            return capas;
        }

        public static Material CrearMaterial()
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(RutaMaterial);
            bool nuevo = mat == null;
            if (nuevo) mat = new Material(Shader.Find("HDRP/TerrainLit"));
            mat.shader = Shader.Find("HDRP/TerrainLit");
            // Mezcla por altura: los guijarros asoman entre el barro, la hojarasca cubre la tierra...
            mat.SetFloat("_EnableHeightBlend", 1f);
            mat.SetFloat("_HeightTransition", 0.15f);
            mat.SetFloat("_EnableInstancedPerPixelNormal", 1f);
            HDMaterial.ValidateMaterial(mat);
            if (nuevo) AssetDatabase.CreateAsset(mat, RutaMaterial);
            else EditorUtility.SetDirty(mat);
            return mat;
        }

        // ------------------------------------------------------------------ Datos compactos
        /// <summary>Guarda el mundo compacto (.bytes) y lo devuelve importado como TextAsset.</summary>
        public static TextAsset GuardarDatos(Relieve r, string ruta)
        {
            using (var datos = r.ADatos())
            {
                var bytes = datos.Escribir();
                Directory.CreateDirectory(Path.GetDirectoryName(ruta));
                File.WriteAllBytes(ruta, bytes);
                Debug.Log($"[GD] {ruta}: {bytes.Length / 1048576f:F1} MB");
            }
            AssetDatabase.ImportAsset(ruta, ImportAssetOptions.ForceSynchronousImport);
            return AssetDatabase.LoadAssetAtPath<TextAsset>(ruta);
        }
    }
}
