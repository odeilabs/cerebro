using System.IO;
using Guardianes.Mundo;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace Guardianes.EditorTools
{
    /// <summary>
    /// Ajustes "ultra" de HDRP y la atmosfera de la selva: asset de HDRP con agua, nubes
    /// volumetricas y SSR; perfil de volumen (cielo fisico, nubes, niebla humeda volumetrica,
    /// SSGI, SSAO, sombras de contacto, exposicion y grading) y el sol.
    /// </summary>
    public static class ConfiguradorHDRP
    {
        public const string RutaPerfil = "Assets/_Game/Settings/PerfilSelva.asset";
        /// <summary>Hora del sol: rumbo y altura (grados). La de la isla de Kong es la media manana.</summary>
        public static Vector2 SolMediaManana => new Vector2(34f, -58f);
        const string RutaAssetHDRP = "Assets/Settings/HDRP High Fidelity.asset";

        // ------------------------------------------------------------------ Asset de HDRP
        public static void ConfigurarProyecto()
        {
            var asset = AssetDatabase.LoadAssetAtPath<HDRenderPipelineAsset>(RutaAssetHDRP);
            if (asset == null)
            {
                Debug.LogError($"[GD] No encuentro {RutaAssetHDRP}");
                return;
            }
            var so = new SerializedObject(asset);
            void Bool(string ruta, bool v) { var p = so.FindProperty("m_RenderPipelineSettings." + ruta); if (p != null) p.boolValue = v; else Debug.LogWarning("[GD] HDRP sin " + ruta); }
            void Int(string ruta, int v) { var p = so.FindProperty("m_RenderPipelineSettings." + ruta); if (p != null) p.intValue = v; else Debug.LogWarning("[GD] HDRP sin " + ruta); }

            Bool("supportSSR", true);
            Bool("supportSSRTransparent", false);
            Bool("supportSSAO", true);
            Bool("supportSSGI", true);
            Bool("supportVolumetrics", true);
            Bool("supportVolumetricClouds", true);
            Bool("supportWater", true);
            Int("waterSimulationResolution", 256);
            Bool("supportWaterDecals", true);
            Bool("supportDecals", true);
            Bool("supportMotionVectors", true);
            Bool("supportSubsurfaceScattering", true);
            Bool("supportTransparentBackface", true);
            Bool("supportDistortion", true);
            Bool("supportDitheringCrossFade", true);
            Bool("supportCustomPass", true);
            // Sombras del sol a 4096 y 32 bits de profundidad (cascadas largas sobre la selva).
            Int("hdShadowInitParams.directionalShadowsDepthBits", 32);
            var res = so.FindProperty("m_RenderPipelineSettings.hdShadowInitParams.shadowResolutionDirectional.m_Values");
            if (res != null && res.isArray && res.arraySize >= 4)
            {
                res.GetArrayElementAtIndex(0).intValue = 1024;
                res.GetArrayElementAtIndex(1).intValue = 2048;
                res.GetArrayElementAtIndex(2).intValue = 4096;
                res.GetArrayElementAtIndex(3).intValue = 4096;
            }
            Int("hdShadowInitParams.cachedAreaLightShadowAtlas", 4096);
            // Resolucion dinamica: los niveles de calidad bajan la resolucion interna y el
            // reescalado temporal (STP si la plataforma lo admite, si no TAAU) recupera la nitidez.
            Bool("dynamicResolutionSettings.enabled", true);
            Bool("dynamicResolutionSettings.useMipBias", true);
            Bool("dynamicResolutionSettings.forceResolution", false);
            var minimo = so.FindProperty("m_RenderPipelineSettings.dynamicResolutionSettings.minPercentage");
            if (minimo != null) minimo.floatValue = 50f;
            var maximo = so.FindProperty("m_RenderPipelineSettings.dynamicResolutionSettings.maxPercentage");
            if (maximo != null) maximo.floatValue = 100f;
            Int("dynamicResolutionSettings.upsampleFilter", (int)DynamicResUpscaleFilter.TAAU);
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(asset);

            // Calidad por defecto: la mas alta.
            int indice = System.Array.IndexOf(QualitySettings.names, "High Fidelity");
            if (indice >= 0) QualitySettings.SetQualityLevel(indice, true);
            AssetDatabase.SaveAssets();
        }

        // ------------------------------------------------------------------ Perfil de volumen
        public static VolumeProfile CrearPerfil(string ruta = RutaPerfil)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ruta));
            var perfil = AssetDatabase.LoadAssetAtPath<VolumeProfile>(ruta);
            if (perfil != null)
            {
                // Se reconstruye entero: componentes como sub-assets nuevos.
                foreach (var c in perfil.components.ToArray())
                {
                    perfil.components.Remove(c);
                    Object.DestroyImmediate(c, true);
                }
            }
            else
            {
                perfil = ScriptableObject.CreateInstance<VolumeProfile>();
                AssetDatabase.CreateAsset(perfil, ruta);
            }

            var ve = Agregar<VisualEnvironment>(perfil);
            ve.skyType.Override((int)SkyType.PhysicallyBased);
            ve.skyAmbientMode.Override(SkyAmbientMode.Dynamic);

            var cielo = Agregar<PhysicallyBasedSky>(perfil);
            // Calima tropical: mas aerosoles y algo calidos (el horizonte se blanquea con la humedad).
            cielo.aerosolDensity.Override(0.06f);
            cielo.aerosolTint.Override(new Color(0.92f, 0.9f, 0.85f));
            cielo.aerosolAnisotropy.Override(0.82f);
            cielo.groundTint.Override(new Color(0.06f, 0.09f, 0.05f));

            var nubes = Agregar<VolumetricClouds>(perfil);
            nubes.enable.Override(true);
            nubes.cloudPreset = VolumetricClouds.CloudPresets.Cloudy;
            nubes.bottomAltitude.Override(1100f);
            nubes.altitudeRange.Override(1800f);
            nubes.shadows.Override(true);
            nubes.shadowOpacity.Override(0.65f);
            nubes.shadowResolution.Override(VolumetricClouds.CloudShadowResolution.High512);
            nubes.shadowDistance.Override(4000f);
            nubes.numPrimarySteps.Override(96);
            nubes.numLightSteps.Override(8);

            var niebla = Agregar<Fog>(perfil);
            niebla.enabled.Override(true);
            niebla.meanFreePath.Override(650f);
            niebla.baseHeight.Override(0f);
            niebla.maximumHeight.Override(220f);
            niebla.maxFogDistance.Override(6000f);
            niebla.enableVolumetricFog.Override(true);
            niebla.albedo.Override(new Color(0.94f, 0.95f, 0.93f));
            niebla.anisotropy.Override(0.65f);
            niebla.depthExtent.Override(400f);
            niebla.multipleScatteringIntensity.Override(0.6f);
            niebla.denoisingMode.Override(FogDenoisingMode.Both);
            niebla.sliceDistributionUniformity.Override(0.6f);
            niebla.globalLightProbeDimmer.Override(0.8f);

            var exp = Agregar<Exposure>(perfil);
            exp.mode.Override(ExposureMode.AutomaticHistogram);
            exp.meteringMode.Override(MeteringMode.CenterWeighted);
            exp.limitMin.Override(7f);
            exp.limitMax.Override(15.5f);
            exp.compensation.Override(0.3f);
            exp.histogramPercentages.Override(new Vector2(45f, 92f));
            exp.adaptationSpeedDarkToLight.Override(2.2f);
            exp.adaptationSpeedLightToDark.Override(1.2f);

            var sombras = Agregar<HDShadowSettings>(perfil);
            sombras.maxShadowDistance.Override(1600f);
            sombras.cascadeShadowSplitCount.Override(4);
            sombras.cascadeShadowSplit0.Override(0.03f);
            sombras.cascadeShadowSplit1.Override(0.10f);
            sombras.cascadeShadowSplit2.Override(0.32f);

            var contacto = Agregar<ContactShadows>(perfil);
            contacto.enable.Override(true);
            contacto.length.Override(0.35f);
            contacto.maxDistance.Override(120f);
            contacto.fadeDistance.Override(20f);
            contacto.thicknessScale.Override(0.3f);

            Agregar<MicroShadowing>(perfil).enable.Override(true);

            var ssao = Agregar<ScreenSpaceAmbientOcclusion>(perfil);
            ssao.quality.Override((int)ScalableSettingLevelParameter.Level.High);
            ssao.intensity.Override(1.4f);
            ssao.radius.Override(3.5f);
            ssao.directLightingStrength.Override(0.15f);

            var ssgi = Agregar<GlobalIllumination>(perfil);
            ssgi.enable.Override(true);
            ssgi.quality.Override((int)ScalableSettingLevelParameter.Level.High);
            ssgi.ambientProbeDimmer.Override(0.35f);
            ssgi.depthBufferThickness.Override(0.2f);

            var ssr = Agregar<ScreenSpaceReflection>(perfil);
            ssr.enabled.Override(true);
            ssr.quality.Override((int)ScalableSettingLevelParameter.Level.High);
            ssr.usedAlgorithm.Override(ScreenSpaceReflectionAlgorithm.PBRAccumulation);

            // HDRP atenua a la mitad el ambiente sobre el agua por defecto: con la calima tropical el
            // mar y el lago quedaban casi negros.
            var agua = Agregar<WaterRendering>(perfil);
            agua.enable.Override(true);
            agua.ambientProbeDimmer.Override(1f);
            agua.triangleSize.Override(18f);

            var tono = Agregar<Tonemapping>(perfil);
            tono.mode.Override(TonemappingMode.ACES);

            var bloom = Agregar<Bloom>(perfil);
            bloom.intensity.Override(0.12f);
            bloom.scatter.Override(0.65f);
            bloom.threshold.Override(1.2f);

            var color = Agregar<ColorAdjustments>(perfil);
            color.contrast.Override(8f);
            color.saturation.Override(6f);

            var balance = Agregar<WhiteBalance>(perfil);
            balance.temperature.Override(4f);
            balance.tint.Override(2f);

            var lgg = Agregar<LiftGammaGain>(perfil);
            lgg.lift.Override(new Vector4(1f, 1f, 1.02f, -0.01f));
            lgg.gain.Override(new Vector4(1.02f, 1.01f, 0.98f, 0f));

            // Desenfoque de movimiento suave (el de serie emborrona demasiado a pocos fps).
            var movimiento = Agregar<MotionBlur>(perfil);
            movimiento.intensity.Override(0.25f);
            movimiento.maximumVelocity.Override(120f);

            var vineta = Agregar<Vignette>(perfil);
            vineta.intensity.Override(0.18f);
            vineta.smoothness.Override(0.5f);

            var grano = Agregar<FilmGrain>(perfil);
            grano.type.Override(FilmGrainLookup.Thin1);
            grano.intensity.Override(0.12f);

            EditorUtility.SetDirty(perfil);
            AssetDatabase.SaveAssets();
            return perfil;
        }

        static T Agregar<T>(VolumeProfile perfil) where T : VolumeComponent
        {
            var c = perfil.Add<T>(false);
            c.name = typeof(T).Name;
            AssetDatabase.AddObjectToAsset(c, perfil);
            return c;
        }

        // ------------------------------------------------------------------ Sol y volumen
        public static Light CrearSol(Transform padre)
        {
            var go = new GameObject("Sol");
            go.transform.SetParent(padre, false);
            var hd = GameObjectExtension.AddHDLight(go, LightType.Directional);
            var luz = go.GetComponent<Light>();
            luz.lightUnit = LightUnit.Lux;
            luz.intensity = 120000f;
            luz.useColorTemperature = true;
            luz.colorTemperature = 5600f;
            luz.color = Color.white;
            luz.shadows = LightShadows.Soft;
            hd.SetShadowResolutionLevel(3);
            hd.angularDiameter = 0.53f;
            hd.interactsWithSky = true;
            hd.volumetricDimmer = 1.2f;
            hd.shadowDimmer = 1f;
            // Media manana: el sol entra rasante y largo por el este (rayos entre la bruma).
            go.transform.rotation = Quaternion.Euler(34f, -58f, 0f);
            return luz;
        }

        public static Volume CrearVolumen(Transform padre, VolumeProfile perfil)
        {
            var go = new GameObject("Atmosfera");
            go.transform.SetParent(padre, false);
            var v = go.AddComponent<Volume>();
            v.isGlobal = true;
            v.priority = 1f;
            v.sharedProfile = perfil;
            return v;
        }

        // ------------------------------------------------------------------ Agua
        public static void CrearMar(Transform padre, float nivel = 0f)
        {
            var mar = new GameObject("Mar");
            mar.transform.SetParent(padre, false);
            mar.transform.position = new Vector3(0f, nivel, 0f);
            var ws = mar.AddComponent<WaterSurface>();
            ws.surfaceType = WaterSurfaceType.OceanSeaLake;
            ws.geometryType = WaterGeometryType.Infinite;
            ws.repetitionSize = 600f;
            ws.largeWindSpeed = 18f;
            ws.largeChaos = 0.75f;
            ws.ripplesWindSpeed = 6f;
            ws.refractionColor = new Color(0.05f, 0.55f, 0.6f);
            ws.scatteringColor = new Color(0.0f, 0.3f, 0.28f);
            ws.absorptionDistance = 9f;
            ws.maxRefractionDistance = 2f;
            ws.caustics = true;
            ws.causticsIntensity = 0.6f;
            ws.foam = true;
            ws.simulationFoamAmount = 0.35f;

        }
    }
}
