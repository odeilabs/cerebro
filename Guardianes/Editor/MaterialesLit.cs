using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;

namespace Guardianes.EditorTools
{
    /// <summary>
    /// Materiales sobre HDRP/Lit y sus variantes (Guardianes/Vegetacion, Guardianes/Rio, ...): se
    /// configuran las propiedades del Lit y se recalculan palabras clave y pasadas con la misma
    /// rutina que usa el inspector de HDRP (LitAPI.ValidateMaterial), aunque el shader no se llame
    /// "HDRP/Lit".
    /// </summary>
    public static class MaterialesLit
    {
        static MethodInfo s_Validar;

        public static void Validar(Material m)
        {
            s_Validar ??= typeof(HDMaterial).Assembly.GetType("UnityEngine.Rendering.HighDefinition.LitAPI")
                ?.GetMethod("ValidateMaterial", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
            if (s_Validar != null)
            {
                s_Validar.Invoke(null, new object[] { m });
                return;
            }
            var propio = m.shader;
            m.shader = Shader.Find("HDRP/Lit");
            HDMaterial.ValidateMaterial(m);
            m.shader = propio;
        }

        public static Material Crear(string ruta, string shader, Action<Material> configurar)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ruta));
            var sh = Shader.Find(shader);
            if (sh == null)
            {
                Debug.LogWarning($"[GD] No existe el shader {shader}; uso HDRP/Lit");
                sh = Shader.Find("HDRP/Lit");
            }
            var mat = AssetDatabase.LoadAssetAtPath<Material>(ruta);
            if (mat == null)
            {
                // El asset debe existir antes de configurar: el perfil de difusion de HDRP se guarda
                // como sub-asset del material.
                mat = new Material(sh);
                AssetDatabase.CreateAsset(mat, ruta);
            }
            else mat.shader = sh;
            configurar(mat);
            Validar(mat);
            mat.enableInstancing = true;
            EditorUtility.SetDirty(mat);
            return mat;
        }

        public static Texture2D Textura(string ruta) => AssetDatabase.LoadAssetAtPath<Texture2D>(ruta);
    }
}
