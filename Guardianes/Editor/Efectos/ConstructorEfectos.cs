using System.IO;
using Guardianes.Efectos;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Guardianes.EditorTools.Efectos
{
    /// <summary>
    /// Materiales de particulas (Guardianes/Particula) con las laminas de Tools/arte/efectos y el
    /// objeto EfectosCombate de la escena.
    /// </summary>
    public static class ConstructorEfectos
    {
        const string CarpetaTexturas = "Assets/_Game/Art/Efectos/Texturas";
        const string CarpetaMateriales = "Assets/_Game/Art/Efectos/Materiales";

        static Material Particula(string nombre, string textura, bool emisiva, bool aditiva, float nits = 1f, float suavidad = 1.5f, float dispersion = 0.5f)
        {
            Directory.CreateDirectory(CarpetaMateriales);
            string ruta = $"{CarpetaMateriales}/{nombre}.mat";
            var sh = Shader.Find("Guardianes/Particula");
            var m = AssetDatabase.LoadAssetAtPath<Material>(ruta);
            if (m == null)
            {
                m = new Material(sh);
                AssetDatabase.CreateAsset(m, ruta);
            }
            else m.shader = sh;
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>($"{CarpetaTexturas}/{textura}.png");
            if (tex == null) Debug.LogWarning($"[GD] Falta la textura de efectos {textura} (Tools/arte/efectos/generar_texturas_efectos.py)");
            m.SetTexture("_MainTex", tex);
            m.SetColor("_Color", Color.white);
            m.SetFloat("_Emisiva", emisiva ? 1f : 0f);
            m.SetFloat("_Intensidad", nits);
            m.SetFloat("_Suavidad", suavidad);
            m.SetFloat("_Dispersion", dispersion);
            m.SetFloat("_Origen", (float)BlendMode.SrcAlpha);
            m.SetFloat("_Destino", aditiva ? (float)BlendMode.One : (float)BlendMode.OneMinusSrcAlpha);
            m.renderQueue = (int)RenderQueue.Transparent;
            EditorUtility.SetDirty(m);
            return m;
        }

        /// <summary>Crea (o actualiza) los materiales y pone EfectosCombate en la escena abierta.</summary>
        public static EfectosCombate Crear(Transform padre)
        {
            var go = new GameObject("Efectos");
            if (padre != null) go.transform.SetParent(padre, false);
            go.SetActive(false);
            var e = go.AddComponent<EfectosCombate>();
            e.polvo = Particula("Polvo", "humo", false, false, suavidad: 2f, dispersion: 0.35f);
            e.humo = Particula("Humo", "humo", false, false, suavidad: 3f, dispersion: 0.6f);
            e.bolaFuego = Particula("BolaFuego", "bola_fuego", true, false, 60000f, 2f);
            e.llamas = Particula("Llamas", "llamas", true, false, 30000f, 0.8f);
            e.chispas = Particula("Chispas", "chispa", true, true, 90000f, 0.3f);
            e.trazadoras = Particula("Trazadoras", "chispa", true, true, 120000f, 0.2f);
            e.fogonazos = Particula("Fogonazos", "fogonazo", true, true, 80000f, 0.3f);
            e.escombros = Particula("Escombros", "escombro", false, false, suavidad: 0.2f, dispersion: 0f);
            e.hojas = Particula("Hojas", "hoja", false, false, suavidad: 0.2f, dispersion: 0.8f);
            e.anillo = Particula("Anillo", "anillo", false, false, suavidad: 1.5f, dispersion: 0.35f);
            e.motas = Particula("Motas", "chispa", true, true, 25000f, 0.5f);
            go.SetActive(true);
            AssetDatabase.SaveAssets();
            return e;
        }
    }
}
