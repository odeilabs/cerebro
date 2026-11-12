using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Guardianes.Criaturas;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Guardianes.EditorTools
{
    /// <summary>
    /// Un animal de la via de juego de AssetsCreator (ultra/animal_juego.py, exportado con exportar_unity.py --geometria fbx)
    /// montado sobre una instancia de su FBX: un material por cada uno de su ficha (`*_criatura_unity.json`), las capas del
    /// manto (Guardianes/Manto, una por material, con `MantoAnimal`), las tarjetas del pelo largo (Guardianes/Tarjeta), la
    /// sombra del pelo y el LODGroup con las mallas lod1_* y lod2_*. Portado de AssetsCreatorCriatura (12-11-2026) con lo que
    /// usa un animal; los tejidos con perfil de difusion (mucosa, trufa) van en Standard: sin perfil, HDRP los pinta magenta.
    /// Los materiales quedan en `carpeta/Materiales`. Cada juego pone despues lo suyo (emision, colores).
    /// </summary>
    public static class MantoAssetsCreator
    {
        [Serializable]
        public class FichaManto
        {
            public string _FlujoMap, _LargoMap, _FibraMap;
            public int capas;
            public float largo_max_mm, peinado, caida, ruido, ruido_tiling, oscuro_raiz, realce, anisotropia, f0_fibra, lisura_fibra, borde;
        }

        [Serializable]
        public class FichaTarjeta
        {
            public string _FibraMap;
            public float tiling_u, tiling_v, corte, color, ruido;
        }

        [Serializable]
        public class FichaMaterial
        {
            public string material, @base, _BaseColorMap, _NormalMap, _MaskMap, _EmissiveColorMap;
            public int _MaterialID = 1, _SurfaceType, _AlphaCutoffEnable, _DoubleSidedEnable, tamano_maximo;
            public float _Alpha = 1f, _AlphaCutoff = 0.4f, _SpecularF0, _CoatMask, ao_max = 1f, color_mult = 1f, emision = 1f, emision_nits;
            public FichaManto manto;
            public FichaTarjeta tarjeta;
        }

        [Serializable] public class Ficha { public FichaMaterial[] materiales; }

        // Las alturas en pantalla de cada LOD (ultra/animal_juego.LOD_ALTURAS) y lo que se empuja la sombra del pelo, en
        // veces el tamano del animal: los de AssetsCreatorCriatura.
        public static readonly float[] AlturaLod = { 0.30f, 0.10f, 0.012f };
        const float EmpujeSombra = 0.06f;
        static readonly Regex SufijoBlender = new Regex(@"\.\d{3}$");
        enum Uso { Color, Normal, Lineal }

        /// <summary>La ficha de la carpeta, o null.</summary>
        public static Ficha LeerFicha(string carpeta)
        {
            var f = Directory.Exists(carpeta) ? Directory.GetFiles(carpeta, "*_criatura_unity.json").FirstOrDefault() : null;
            return f == null ? null : JsonUtility.FromJson<Ficha>(File.ReadAllText(f));
        }

        /// <summary>Si la carpeta tiene un animal de la via de juego (su ficha trae el manto).</summary>
        public static bool EsDeVia(string carpeta) => LeerFicha(carpeta)?.materiales?.Any(m => m.manto != null && m.manto.capas > 0) ?? false;

        /// <summary>
        /// Monta la instancia: materiales, capas, tarjetas, sombra y LODGroup. Devuelve el MantoAnimal (null si no hay capas).
        /// `sombrasPelo`: la sombra hinchada del pelo (cuesta una malla mas en cada luz con sombra).
        /// </summary>
        public static MantoAnimal Montar(GameObject inst, string carpeta, bool sombrasPelo = true)
        {
            var ficha = LeerFicha(carpeta);
            if (ficha?.materiales == null) { Debug.LogWarning($"[GD] {carpeta}: sin ficha de criatura"); return null; }
            var mats = new Dictionary<string, Material>();
            foreach (var fm in ficha.materiales)
                if (fm != null && !string.IsNullOrEmpty(fm.material)) mats[fm.material] = MaterialDe(carpeta, fm);

            SkinnedMeshRenderer smrCapas = null;
            foreach (var r in inst.GetComponentsInChildren<Renderer>(true))
            {
                if (r.name.Contains("manto_capas")) { smrCapas = r as SkinnedMeshRenderer; continue; }
                var lista = r.sharedMaterials;
                for (int i = 0; i < lista.Length; i++)
                    if (lista[i] != null && mats.TryGetValue(SufijoBlender.Replace(lista[i].name, "").Trim(), out var m)) lista[i] = m;
                r.sharedMaterials = lista;
                r.shadowCastingMode = r.name.Contains("tarjetas") ? ShadowCastingMode.Off : ShadowCastingMode.On;
            }

            MantoAnimal manto = null;
            var fManto = ficha.materiales.FirstOrDefault(m => m.manto != null && m.manto.capas > 0);
            if (smrCapas != null && fManto != null)
            {
                smrCapas.sharedMaterials = Capas(carpeta, fManto, mats[fManto.material]);
                smrCapas.shadowCastingMode = ShadowCastingMode.Off;
                manto = inst.GetComponent<MantoAnimal>();
                if (manto == null) manto = inst.AddComponent<MantoAnimal>();
                manto.capas = smrCapas;
                manto.todas = smrCapas.sharedMaterials;
            }
            else if (fManto != null) Debug.LogWarning($"[GD] {carpeta}: el FBX no trae la malla manto_capas");
            Lods(inst, carpeta, sombrasPelo);
            return manto;
        }

        static Material MaterialDe(string carpeta, FichaMaterial fm)
        {
            bool tarjeta = fm.tarjeta != null && !string.IsNullOrEmpty(fm.tarjeta._FibraMap);
            var color = Tex(carpeta, fm._BaseColorMap, Uso.Color, fm.tamano_maximo);
            var normal = Tex(carpeta, fm._NormalMap, Uso.Normal, fm.tamano_maximo);
            var mascara = Tex(carpeta, fm._MaskMap, Uso.Lineal, fm.tamano_maximo, true);
            var emision = Tex(carpeta, fm._EmissiveColorMap, Uso.Color);
            if (tarjeta && color != null) PrepararAlfa(AssetDatabase.GetAssetPath(color), fm._AlphaCutoff);
            return MaterialesLit.Crear($"{carpeta}/Materiales/{fm.material}.mat", tarjeta ? "Guardianes/Tarjeta" : "HDRP/Lit", mat =>
            {
                // 0 SSS y 5 Translucent piden perfil de difusion: sin el, Standard.
                int id = fm._MaterialID == 0 || fm._MaterialID == 5 ? 1 : fm._MaterialID;
                Poner(mat, "_MaterialID", id);
                mat.SetColor("_BaseColor", Color.white * fm.color_mult);
                if (color != null) mat.SetTexture("_BaseColorMap", color);
                if (normal != null) { mat.SetTexture("_NormalMap", normal); Poner(mat, "_NormalScale", 1f); }
                if (mascara != null) mat.SetTexture("_MaskMap", mascara);
                Poner(mat, "_Metallic", 0f);
                Poner(mat, "_MetallicRemapMax", 1f);
                Poner(mat, "_SmoothnessRemapMin", 0f);
                Poner(mat, "_SmoothnessRemapMax", 1f);
                Poner(mat, "_AORemapMin", 0f);
                Poner(mat, "_AORemapMax", fm.ao_max);
                Poner(mat, "_CoatMask", Mathf.Clamp01(fm._CoatMask));
                if (id == 4 && mat.HasProperty("_SpecularColor"))
                {
                    mat.SetColor("_SpecularColor", new Color(fm._SpecularF0, fm._SpecularF0, fm._SpecularF0).gamma);
                    Poner(mat, "_EnergyConservingSpecularColor", 1f);
                }
                Poner(mat, "_AlphaCutoffEnable", fm._AlphaCutoffEnable != 0 ? 1f : 0f);
                if (fm._AlphaCutoffEnable != 0) { Poner(mat, "_AlphaCutoff", fm._AlphaCutoff); Poner(mat, "_AlphaCutoffShadow", fm._AlphaCutoff); }
                Poner(mat, "_DoubleSidedEnable", fm._DoubleSidedEnable != 0 ? 1f : 0f);
                // La cornea: transparente, negra y con el especular conservado; asi se ve el iris y el brillo del sol.
                bool transparente = fm._SurfaceType == 1;
                Poner(mat, "_SurfaceType", transparente ? 1f : 0f);
                if (transparente)
                {
                    Poner(mat, "_BlendMode", 0f);
                    Poner(mat, "_EnableBlendModePreserveSpecularLighting", 1f);
                    Poner(mat, "_ZWrite", 0f);
                    Poner(mat, "_TransparentZWrite", 0f);
                    Poner(mat, "_EnableFogOnTransparent", 1f);
                    mat.SetColor("_BaseColor", new Color(0f, 0f, 0f, Mathf.Clamp01(fm._Alpha)));
                    Poner(mat, "_AORemapMax", 0.4f);
                }
                if (tarjeta)
                {
                    var fibra = Tex(carpeta, fm.tarjeta._FibraMap, Uso.Lineal);
                    if (fibra != null) mat.SetTexture("_IridescenceMaskMap", fibra);
                    mat.SetVector("_ThicknessRemap", new Vector4(fm.tarjeta.tiling_u, fm.tarjeta.tiling_v, fm.tarjeta.corte, fm.tarjeta.color));
                    mat.SetVector("_IridescenceThicknessRemap", new Vector4(fm.tarjeta.ruido, 0f, 0f, 0f));
                    Poner(mat, "_SubsurfaceMask", 0f);
                }
                if (emision != null)
                {
                    mat.SetTexture("_EmissiveColorMap", emision);
                    Poner(mat, "_UseEmissiveIntensity", 0f);
                    mat.SetColor("_EmissiveColor", Color.white * (fm.emision_nits > 0f ? fm.emision_nits : Mathf.Max(0f, fm.emision)));
                }
            });
        }

        /// <summary>Un material Guardianes/Manto por capa, de la raiz a la punta, con todo lo suyo (dos especies no comparten nada).</summary>
        static Material[] Capas(string carpeta, FichaMaterial fm, Material piel)
        {
            var m = fm.manto;
            var flujo = Tex(carpeta, m._FlujoMap, Uso.Lineal, fm.tamano_maximo, true);
            var largo = Tex(carpeta, m._LargoMap, Uso.Lineal, fm.tamano_maximo, true);
            var fibra = Tex(carpeta, m._FibraMap, Uso.Lineal);
            var capas = new Material[m.capas];
            for (int i = 0; i < m.capas; i++)
            {
                int k = i;
                capas[i] = MaterialesLit.Crear($"{carpeta}/Materiales/{fm.material}_capa{i:00}.mat", "Guardianes/Manto", mat =>
                {
                    mat.CopyPropertiesFromMaterial(piel);
                    Poner(mat, "_TexWorldScale", (k + 1f) / m.capas);
                    Poner(mat, "_SubsurfaceMask", m.borde);
                    mat.SetVector("_ThicknessRemap", new Vector4(m.largo_max_mm * 0.001f, m.peinado, m.caida, m.ruido));
                    mat.SetVector("_IridescenceThicknessRemap", new Vector4(m.ruido_tiling, m.oscuro_raiz, m.realce, m.f0_fibra));
                    if (flujo != null) mat.SetTexture("_SubsurfaceMaskMap", flujo);
                    if (largo != null) mat.SetTexture("_ThicknessMap", largo);
                    if (fibra != null) mat.SetTexture("_IridescenceMaskMap", fibra);
                    // el brillo de fibra: 2 = Anisotropy
                    if (m.anisotropia != 0f)
                    {
                        Poner(mat, "_MaterialID", 2f);
                        Poner(mat, "_Anisotropy", m.anisotropia);
                        Poner(mat, "_Thickness", m.lisura_fibra);
                    }
                    Poner(mat, "_AlphaCutoffEnable", 1f);
                    Poner(mat, "_AlphaCutoff", 0.5f);
                    Poner(mat, "_UseShadowThreshold", 0f);
                });
            }
            return capas;
        }

        /// <summary>
        /// El LODGroup: lod1_* y lod2_* son sus mallas; el resto, la LOD0. La sombra del pelo es la malla de LOD2, solo en la
        /// pasada de sombra y empujada a lo largo del rayo (Tarjeta.hlsl): las tarjetas no echan sombra.
        /// </summary>
        static void Lods(GameObject inst, string carpeta, bool sombrasPelo)
        {
            var niveles = new[] { new List<Renderer>(), new List<Renderer>(), new List<Renderer>() };
            foreach (var s in inst.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                niveles[s.name.StartsWith("lod1_") ? 1 : s.name.StartsWith("lod2_") ? 2 : 0].Add(s);
            if (niveles[1].Count == 0 || niveles[2].Count == 0) return;
            var lod2 = niveles[2][0] as SkinnedMeshRenderer;
            if (sombrasPelo && lod2 != null && Shader.Find("Guardianes/Tarjeta") != null && inst.transform.Find("manto_sombra") == null)
            {
                var copia = UnityEngine.Object.Instantiate(lod2.gameObject, lod2.transform.parent);
                copia.name = "manto_sombra";
                var s = copia.GetComponent<SkinnedMeshRenderer>();
                s.shadowCastingMode = ShadowCastingMode.ShadowsOnly;
                float tam = inst.GetComponentsInChildren<SkinnedMeshRenderer>().First().bounds.size.magnitude;
                s.sharedMaterial = MaterialesLit.Crear($"{carpeta}/Materiales/sombra_pelo.mat", "Guardianes/Tarjeta", m => Poner(m, "_SubsurfaceMask", EmpujeSombra * tam));
                niveles[0].Add(s);
            }
            var grupo = inst.GetComponent<LODGroup>();
            if (grupo == null) grupo = inst.AddComponent<LODGroup>();
            grupo.SetLODs(niveles.Select((l, i) => new LOD(AlturaLod[i], l.ToArray())).ToArray());
            grupo.RecalculateBounds();
        }

        /// <summary>
        /// La textura importada para lo que es: color en sRGB, el resto lineal. Con `tamanoMaximo` (los atlas del pelo), ese
        /// lado, filtrado anisotropo y BC7: BC1 parte en bloques las vetas de un texel. Las mascaras, siempre en BC7 (alfa).
        /// </summary>
        static Texture2D Tex(string carpeta, string nombre, Uso uso, int tamanoMaximo = 0, bool alta = false)
        {
            if (string.IsNullOrEmpty(nombre)) return null;
            string ruta = $"{carpeta}/{nombre}";
            if (!(AssetImporter.GetAtPath(ruta) is TextureImporter ti)) { Debug.LogWarning($"[GD] Falta {ruta}"); return null; }
            bool cambia = false;
            void Si(bool c, Action a) { if (c) { a(); cambia = true; } }
            Si(tamanoMaximo > 0 && ti.maxTextureSize != tamanoMaximo, () => ti.maxTextureSize = tamanoMaximo);
            Si(tamanoMaximo > 0 && ti.anisoLevel != 8, () => ti.anisoLevel = 8);
            var tipo = uso == Uso.Normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            Si(ti.textureType != tipo, () => ti.textureType = tipo);
            Si(uso != Uso.Normal && ti.sRGBTexture != (uso == Uso.Color), () => ti.sRGBTexture = uso == Uso.Color);
            Si((alta || tamanoMaximo > 0) && ti.textureCompression != TextureImporterCompression.CompressedHQ, () => ti.textureCompression = TextureImporterCompression.CompressedHQ);
            if (cambia) ti.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(ruta);
        }

        /// <summary>Los mips conservan la cobertura del corte: si no, el mechon se va comiendo con la distancia.</summary>
        static void PrepararAlfa(string ruta, float corte)
        {
            if (!(AssetImporter.GetAtPath(ruta) is TextureImporter ti)) return;
            if (ti.alphaIsTransparency && ti.mipMapsPreserveCoverage && Mathf.Approximately(ti.alphaTestReferenceValue, corte)) return;
            ti.alphaIsTransparency = true;
            ti.mipMapsPreserveCoverage = true;
            ti.alphaTestReferenceValue = corte;
            ti.SaveAndReimport();
        }

        static void Poner(Material m, string prop, float v)
        {
            if (m.HasProperty(prop)) m.SetFloat(prop, v);
        }
    }
}
