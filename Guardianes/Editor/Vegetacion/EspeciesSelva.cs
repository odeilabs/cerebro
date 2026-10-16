using System.IO;
using Guardianes.Vegetacion;
using Guardianes.Vegetacion.Generacion;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace Guardianes.EditorTools.Vegetacion
{
    /// <summary>
    /// Materiales de la vegetacion (sobre Guardianes/Vegetacion: viento en todas las pasadas) y los
    /// assets de las especies de la selva. Las texturas las genera Tools/arte/vegetacion.
    /// </summary>
    public static class EspeciesSelva
    {
        public const string CarpetaTexturas = "Assets/_Game/Art/Vegetacion/Texturas";
        public const string CarpetaMateriales = "Assets/_Game/Art/Vegetacion/Materiales";
        public const string CarpetaEspecies = "Assets/_Game/Art/Vegetacion/Especies";
        const string RutaFoliage = "Packages/com.unity.render-pipelines.high-definition/Runtime/RenderPipelineResources/FoliageDiffusionProfile.asset";

        static Texture2D Tex(string conjunto, string mapa) =>
            AssetDatabase.LoadAssetAtPath<Texture2D>($"{CarpetaTexturas}/{conjunto}/{conjunto}_{mapa}.png");

        // ------------------------------------------------------------------ Materiales
        public static Material Hojas(string conjunto, float corte = 0.5f, float suavidadMax = 1f, float grosor = 1f, float grosorMin = 0f)
        {
            var perfil = AssetDatabase.LoadAssetAtPath<DiffusionProfileSettings>(RutaFoliage);
            return MaterialesLit.Crear($"{CarpetaMateriales}/Hojas_{conjunto}.mat", "Guardianes/Vegetacion", m =>
            {
                m.SetTexture("_BaseColorMap", Tex(conjunto, "albedo"));
                m.SetTexture("_NormalMap", Tex(conjunto, "normal"));
                m.SetTexture("_MaskMap", Tex(conjunto, "mascara"));
                m.SetTexture("_ThicknessMap", Tex(conjunto, "grosor"));
                m.SetColor("_BaseColor", Color.white);
                m.SetFloat("_NormalScale", 1f);
                m.SetFloat("_Metallic", 0f);
                m.SetFloat("_SmoothnessRemapMin", 0f);
                m.SetFloat("_SmoothnessRemapMax", suavidadMax);
                m.SetFloat("_AORemapMin", 0f);
                m.SetFloat("_AORemapMax", 1f);
                m.SetFloat("_AlphaCutoffEnable", 1f);
                m.SetFloat("_AlphaCutoff", corte);
                m.SetFloat("_AlphaCutoffShadow", corte);
                m.SetFloat("_UseShadowThreshold", 1f);
                m.SetFloat("_DoubleSidedEnable", 1f);
                m.SetFloat("_DoubleSidedNormalMode", 1f); // espejo
                m.SetFloat("_MaterialID", 5f);            // translucido
                m.SetFloat("_Thickness", grosor);
                m.SetVector("_ThicknessRemap", new Vector4(grosorMin, grosor, 0f, 0f));
                m.SetFloat("_EnableGeometricSpecularAA", 1f);
                m.SetFloat("_SpecularAAScreenSpaceVariance", 0.12f);
                m.SetFloat("_SpecularAAThreshold", 0.25f);
                m.SetFloat("_SupportDecals", 0f);
                m.SetFloat("_ReceivesSSR", 0f);
                if (perfil != null) HDMaterial.SetDiffusionProfile(m, perfil);
            }).ConVientoYMovimiento();
        }

        public static Material Corteza(string conjunto, string nombre = null)
        {
            return MaterialesLit.Crear($"{CarpetaMateriales}/Corteza_{nombre ?? conjunto}.mat", "Guardianes/Vegetacion", m =>
            {
                m.SetTexture("_BaseColorMap", Tex(conjunto, "albedo"));
                m.SetTexture("_NormalMap", Tex(conjunto, "normal"));
                m.SetTexture("_MaskMap", Tex(conjunto, "mascara"));
                m.SetColor("_BaseColor", Color.white);
                m.SetFloat("_NormalScale", 1.2f);
                m.SetFloat("_Metallic", 0f);
                m.SetFloat("_SmoothnessRemapMin", 0f);
                m.SetFloat("_SmoothnessRemapMax", 1f);
                m.SetFloat("_MaterialID", 1f);
                m.SetFloat("_DoubleSidedEnable", 0f);
                // Recorte activo (alfa 1 siempre) para que el tronco tambien se disuelva entre la
                // camara y Kong (GD_AjustarAlfa en Viento.hlsl).
                m.SetFloat("_AlphaCutoffEnable", 1f);
                m.SetFloat("_AlphaCutoff", 0.5f);
            }).ConVientoYMovimiento();
        }

        public static Material Roca()
        {
            string capa = "Assets/_Game/Art/Terreno/Capas/roca/roca";
            return MaterialesLit.Crear($"{CarpetaMateriales}/Roca.mat", "HDRP/Lit", m =>
            {
                m.SetTexture("_BaseColorMap", AssetDatabase.LoadAssetAtPath<Texture2D>(capa + "_albedo.png"));
                m.SetTexture("_NormalMap", AssetDatabase.LoadAssetAtPath<Texture2D>(capa + "_normal.png"));
                m.SetColor("_BaseColor", new Color(0.92f, 0.92f, 0.9f));
                m.SetFloat("_NormalScale", 1.3f);
                m.SetFloat("_Metallic", 0f);
                m.SetFloat("_Smoothness", 0.25f);
                m.SetFloat("_UVBase", 5f);          // triplanar: sin costuras en rocas sin UV
                m.SetFloat("_TexWorldScale", 1f / 6f);
                m.SetFloat("_MaterialID", 1f);
            });
        }

        static Material ConVientoYMovimiento(this Material m)
        {
            // Las pasadas de motion vectors deben correr aunque la planta no se mueva (el viento si).
            m.SetShaderPassEnabled("MotionVectors", true);
            EditorUtility.SetDirty(m);
            return m;
        }

        // ------------------------------------------------------------------ Especies
        public static EspecieVegetal[] CrearTodas()
        {
            Directory.CreateDirectory(CarpetaEspecies);
            var cortezaTropical = Corteza("corteza_tropical");
            var cortezaPalmera = Corteza("corteza_palmera");
            var bambu = Corteza("bambu");
            var hojasDoselB = Hojas("hojas_dosel_b");
            var frondaHelecho = Hojas("fronda_helecho");
            var sombrasCerca = new[] { ShadowCastingMode.On, ShadowCastingMode.On, ShadowCastingMode.Off };
            var sinSombraLejos = new[] { ShadowCastingMode.On, ShadowCastingMode.Off };

            return new[]
            {
                Especie("Ceiba", TipoVegetal.Gigante, Plantas.Forma.Ceiba, 3, 11, new[] { 170f, 650f, 4500f }, null,
                    Corteza("corteza_ceiba"), Hojas("hojas_ceiba"), 2.2f, 40f, false, 9999f),
                Especie("DoselA", TipoVegetal.Arbol, Plantas.Forma.Dosel, 4, 21, new[] { 120f, 480f, 3200f }, null,
                    cortezaTropical, Hojas("hojas_dosel_a"), 0.65f, 18f, true, 140f),
                Especie("DoselB", TipoVegetal.Arbol, Plantas.Forma.Dosel, 4, 37, new[] { 120f, 480f, 3200f }, null,
                    cortezaTropical, hojasDoselB, 0.6f, 16f, true, 120f),
                Especie("Palmera", TipoVegetal.Palmera, Plantas.Forma.Palmera, 4, 53, new[] { 100f, 380f, 2200f }, null,
                    cortezaPalmera, Hojas("fronda_palmera"), 0.26f, 12f, true, 55f),
                Especie("HelechoArboreo", TipoVegetal.Arbol, Plantas.Forma.HelechoArboreo, 3, 71, new[] { 60f, 200f, 700f }, sombrasCerca,
                    Corteza("tronco_helecho"), frondaHelecho, 0.22f, 5f, true, 18f),
                Especie("Platanera", TipoVegetal.Arbusto, Plantas.Forma.Platanera, 3, 83, new[] { 60f, 200f, 650f }, sombrasCerca,
                    bambu, Hojas("hoja_platano"), 0.18f, 3.5f, true, 8f),
                Especie("Bambu", TipoVegetal.Bambu, Plantas.Forma.Bambu, 3, 97, new[] { 80f, 280f, 1100f }, null,
                    bambu, Hojas("hojas_bambu"), 1.4f, 10f, true, 35f),
                Especie("Arbusto", TipoVegetal.Arbusto, Plantas.Forma.Arbusto, 4, 109, new[] { 45f, 140f, 380f }, sombrasCerca,
                    cortezaTropical, hojasDoselB, 0f, 2f, true, 4f),
                Especie("Helecho", TipoVegetal.Suelo, Plantas.Forma.Helecho, 4, 127, new[] { 38f, 95f }, sinSombraLejos,
                    bambu, frondaHelecho, 0f, 1f, true, 1f),
                Especie("HojasGrandes", TipoVegetal.Suelo, Plantas.Forma.HojasGrandes, 4, 139, new[] { 40f, 100f }, sinSombraLejos,
                    bambu, Hojas("hojas_grandes"), 0f, 1f, true, 1f),
                Especie("Hierba", TipoVegetal.Suelo, Plantas.Forma.Hierba, 3, 151, new[] { 32f, 75f }, new[] { ShadowCastingMode.Off, ShadowCastingMode.Off },
                    bambu, Hojas("hierba_alta"), 0f, 1f, true, 1f),
                Especie("Flores", TipoVegetal.Suelo, Plantas.Forma.Flores, 4, 163, new[] { 32f, 75f }, new[] { ShadowCastingMode.Off, ShadowCastingMode.Off },
                    bambu, Hojas("flores", grosorMin: 0.2f), 0f, 1f, true, 1f),
                Especie("Roca", TipoVegetal.Roca, Plantas.Forma.Roca, 5, 179, new[] { 160f, 650f, 3000f }, null,
                    Roca(), null, 1.5f, 2f, false, 9999f),
                Especie("TroncoCaido", TipoVegetal.Roca, Plantas.Forma.TroncoCaido, 3, 191, new[] { 80f, 260f }, null,
                    cortezaTropical, frondaHelecho, 0f, 1f, true, 30f),
                // El herbazal cerrado (las manchas de hierba alta donde se esconde lo pequeno): sin sombras, es mucha.
                Especie("Herbazal", TipoVegetal.Suelo, Plantas.Forma.Herbazal, 3, 211, new[] { 40f, 90f }, new[] { ShadowCastingMode.Off, ShadowCastingMode.Off },
                    bambu, Hojas("hierba_alta"), 0f, 1f, true, 1f),
            };
        }

        static EspecieVegetal Especie(string nombre, TipoVegetal tipo, Plantas.Forma forma, int variantes, uint semilla, float[] distancias,
                                      ShadowCastingMode[] sombras, Material madera, Material hojas, float radioTronco, float alturaTronco,
                                      bool destructible, float resistencia)
        {
            string ruta = $"{CarpetaEspecies}/{nombre}.asset";
            var e = AssetDatabase.LoadAssetAtPath<EspecieVegetal>(ruta);
            bool nueva = e == null;
            if (nueva) e = ScriptableObject.CreateInstance<EspecieVegetal>();
            e.tipo = tipo;
            e.forma = forma;
            e.variantes = variantes;
            e.semilla = semilla;
            e.distancias = distancias;
            e.sombras = sombras ?? new[] { ShadowCastingMode.On, ShadowCastingMode.On, ShadowCastingMode.On };
            e.materialMadera = madera;
            e.materialHojas = hojas;
            e.radioTronco = radioTronco;
            e.alturaTronco = alturaTronco;
            e.destructible = destructible;
            e.resistencia = resistencia;
            if (nueva) AssetDatabase.CreateAsset(e, ruta);
            else EditorUtility.SetDirty(e);
            e.Liberar();
            return e;
        }
    }
}
