using System;
using System.Collections.Generic;
using System.IO;
using Guardianes.Enemigos;
using Guardianes.Mundo;
using UnityEditor;
using UnityEngine;

namespace Guardianes.EditorTools.Enemigos
{
    /// <summary>
    /// Materiales de las instalaciones (texturas PBR de AssetsCreator empaquetadas por
    /// Tools~/arte/empaquetar_materiales.py) y las zonas humanas de la escena con su guardia.
    /// </summary>
    public static class ConfiguradorZonas
    {
        const string CarpetaTexturas = "Assets/_Game/Art/Instalaciones/Materiales";
        const string CarpetaMateriales = "Assets/_Game/Art/Instalaciones/MaterialesHDRP";
        public const string RutaCatalogo = "Assets/_Game/Art/Instalaciones/MaterialesInstalaciones.asset";

        /// <summary>Lo que lleva una zona: guardias, cuantos son civiles y helicopteros.</summary>
        public struct Plan
        {
            public int guardias;
            [Range(0f, 1f)] public float proporcionCiviles;
            public int helicopteros;
            public GameObject[] prefabs;
        }

        static Material Lit(string nombre, string textura, Color color, float metal = 0f, float suavidadMax = 1f, Color? emision = null)
        {
            Texture2D T(string mapa) => textura == null ? null : AssetDatabase.LoadAssetAtPath<Texture2D>($"{CarpetaTexturas}/{textura}/{textura}_{mapa}.png");
            return MaterialesLit.Crear($"{CarpetaMateriales}/{nombre}.mat", "HDRP/Lit", m =>
            {
                m.SetTexture("_BaseColorMap", color == Color.white ? T("albedo") : null);
                m.SetTexture("_NormalMap", T("normal"));
                m.SetTexture("_MaskMap", T("mascara"));
                m.SetColor("_BaseColor", color);
                m.SetFloat("_Metallic", metal);
                m.SetFloat("_Smoothness", 0.45f);
                m.SetFloat("_SmoothnessRemapMin", 0f);
                m.SetFloat("_SmoothnessRemapMax", suavidadMax);
                m.SetFloat("_MetallicRemapMin", 0f);
                m.SetFloat("_MetallicRemapMax", 1f);
                m.SetFloat("_MaterialID", 1f);
                m.SetFloat("_NormalScale", 1f);
                if (emision.HasValue)
                {
                    m.SetColor("_EmissiveColor", emision.Value * 8000f);
                    m.SetFloat("_UseEmissiveIntensity", 0f);
                }
            });
        }

        public static MaterialesInstalaciones CrearMateriales()
        {
            Directory.CreateDirectory(CarpetaMateriales);
            var c = AssetDatabase.LoadAssetAtPath<MaterialesInstalaciones>(RutaCatalogo);
            bool nuevo = c == null;
            if (nuevo) c = ScriptableObject.CreateInstance<MaterialesInstalaciones>();
            c.madera = Lit("Madera", "madera", Color.white);
            c.oxido = Lit("Oxido", "oxido", Color.white, 0f);
            c.metal = Lit("Metal", "metal", Color.white, 1f);
            c.hormigon = Lit("Hormigon", "hormigon", Color.white);
            c.lona = Lit("Lona", "arpillera", Color.white);
            c.plastico = Lit("Plastico", "plastico", Color.white);
            c.goma = Lit("Goma", "goma", Color.white);
            // Los troncos cortados llevan la corteza de la selva.
            c.troncos = AssetDatabase.LoadAssetAtPath<Material>("Assets/_Game/Art/Vegetacion/Materiales/Corteza_corteza_tropical.mat");
            c.pinturaAmarilla = Lit("PinturaAmarilla", "pintura_mate", new Color(0.78f, 0.55f, 0.06f));
            c.pinturaVerde = Lit("PinturaVerde", "pintura_mate", new Color(0.2f, 0.25f, 0.14f));
            c.pinturaRoja = Lit("PinturaRoja", "pintura_mate", new Color(0.5f, 0.07f, 0.05f));
            c.pinturaAzul = Lit("PinturaAzul", "pintura_mate", new Color(0.08f, 0.18f, 0.4f));
            c.pinturaBlanca = Lit("PinturaBlanca", "pintura_mate", new Color(0.72f, 0.72f, 0.7f));
            c.pinturaNaranja = Lit("PinturaNaranja", "pintura_mate", new Color(0.8f, 0.32f, 0.05f));
            c.cristal = Lit("Cristal", null, new Color(0.04f, 0.05f, 0.06f), 0.3f, 1f);
            c.cristal.SetFloat("_Smoothness", 0.95f);
            c.luz = Lit("Luz", null, new Color(1f, 0.95f, 0.8f), 0f, 1f, new Color(1f, 0.9f, 0.7f));
            if (nuevo) AssetDatabase.CreateAsset(c, RutaCatalogo);
            else EditorUtility.SetDirty(c);
            AssetDatabase.SaveAssets();
            return c;
        }

        /// <summary>
        /// Crea las zonas humanas en la escena abierta, cada una con su plan (guardias, civiles,
        /// helicopteros). Devuelve la raiz.
        /// </summary>
        public static Transform CrearZonas(DatosMundo datos, IEnumerable<Zona> zonas, Func<Zona, Plan> plan, int semillaBase = 1000)
        {
            var materiales = CrearMateriales();
            var raiz = new GameObject("ZonasHumanas").transform;
            GameObject helicoptero = null;
            bool helicopteroIntentado = false;
            int i = 0;
            foreach (var z in zonas)
            {
                var p = plan(z);
                var go = new GameObject("Zona_" + z.nombre);
                go.transform.SetParent(raiz, false);
                go.transform.position = MedidasMundo.AMundo(z.centro, datos.Altura(z.centro));
                var zona = go.AddComponent<ZonaHumana>();
                zona.nombre = z.nombre;
                zona.tipo = z.tipo;
                zona.radio = z.radio;
                zona.materiales = materiales;
                zona.semilla = semillaBase + i++;
                zona.guardias = p.guardias;
                zona.proporcionCiviles = p.proporcionCiviles;
                zona.prefabsGuardia = p.prefabs ?? new GameObject[0];
                if (p.helicopteros > 0 && !helicopteroIntentado)
                {
                    // El helicoptero se monta la primera vez que una zona lo pide (sus mallas salen de
                    // Tools~/vehiculos/sdf_helicoptero.py).
                    helicopteroIntentado = true;
                    try { helicoptero = ConstructorHelicoptero.Construir(); }
                    catch (Exception e) { Debug.LogWarning("[GD] Sin helicopteros: " + e.Message); }
                }
                zona.prefabHelicoptero = p.helicopteros > 0 ? helicoptero : null;
                zona.helicopteros = zona.prefabHelicoptero != null ? p.helicopteros : 0;
            }
            return raiz;
        }
    }
}
