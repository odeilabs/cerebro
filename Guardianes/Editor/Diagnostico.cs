using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Guardianes.EditorTools
{
    /// <summary>
    /// Comprobaciones rapidas en batch: compilacion de shaders propios y argumentos de linea de comandos.
    /// Unity.exe -batchmode -projectPath <proyecto> -executeMethod Guardianes.EditorTools.Diagnostico.ComprobarDesdeBatch -logFile diag.log
    /// </summary>
    public static class Diagnostico
    {
        public static void ComprobarDesdeBatch()
        {
            int errores = 0;
            try
            {
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                errores += ComprobarShaders();
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                errores++;
            }
            Debug.Log($"[GD] DIAGNOSTICO: {(errores == 0 ? "OK" : errores + " errores")}");
            EditorApplication.Exit(errores == 0 ? 0 : 1);
        }

        [MenuItem("Guardianes/Diagnostico/Comprobar shaders")]
        public static int ComprobarShaders()
        {
            int errores = 0;
            var carpetas = new System.Collections.Generic.List<string> { "Packages/com.odeilabs.guardianes/Shaders" };
            if (AssetDatabase.IsValidFolder("Assets/_Game/Shaders")) carpetas.Add("Assets/_Game/Shaders");
            var guids = AssetDatabase.FindAssets("t:Shader", carpetas.ToArray());
            foreach (var g in guids)
            {
                var ruta = AssetDatabase.GUIDToAssetPath(g);
                var shader = AssetDatabase.LoadAssetAtPath<Shader>(ruta);
                if (shader == null) continue;
                // Fuerza la compilacion de la variante por defecto de cada pasada.
                var mat = new Material(shader);
                ShaderUtil.CompilePass(mat, 0, true);
                var mensajes = ShaderUtil.GetShaderMessages(shader);
                int errs = mensajes.Count(m => m.severity == UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error);
                foreach (var m in mensajes.Take(30))
                    Debug.Log($"[GD] {shader.name} {m.severity}: {m.message} ({m.file}:{m.line}) [{m.platform}]");
                Debug.Log($"[GD] Shader {shader.name}: {(errs == 0 ? "compila" : errs + " errores")} (soportado={shader.isSupported}, pasadas={shader.passCount})");
                errores += errs;
                UnityEngine.Object.DestroyImmediate(mat);
            }
            return errores;
        }

        /// <summary>
        /// Escena minima (sol, cielo fisico, suelo y unas hojas con el shader de vegetacion) y dos fotos
        /// separadas en el tiempo: valida el pipeline de capturas en batch y que el viento mueve las hojas.
        /// -capturas &lt;carpeta&gt;
        /// </summary>
        public static void PruebaRenderDesdeBatch()
        {
            int codigo = 0;
            try
            {
                var dir = CarpetaCapturas(Path.Combine(Path.GetTempPath(), "kk_prueba_render"));
                var escena = UnityEditor.SceneManagement.EditorSceneManager.NewScene(
                    UnityEditor.SceneManagement.NewSceneSetup.EmptyScene, UnityEditor.SceneManagement.NewSceneMode.Single);

                var sol = new GameObject("Sol");
                var hdSol = UnityEngine.Rendering.HighDefinition.GameObjectExtension.AddHDLight(sol, LightType.Directional);
                var luz = sol.GetComponent<Light>();
                luz.lightUnit = UnityEngine.Rendering.LightUnit.Lux;
                luz.intensity = 100000f;
                luz.shadows = LightShadows.Soft;
                sol.transform.rotation = Quaternion.Euler(40f, -35f, 0f);

                var perfil = ScriptableObject.CreateInstance<UnityEngine.Rendering.VolumeProfile>();
                var ve = perfil.Add<UnityEngine.Rendering.HighDefinition.VisualEnvironment>(true);
                ve.skyType.Override((int)UnityEngine.Rendering.HighDefinition.SkyType.PhysicallyBased);
                perfil.Add<UnityEngine.Rendering.HighDefinition.PhysicallyBasedSky>(true);
                var exp = perfil.Add<UnityEngine.Rendering.HighDefinition.Exposure>(true);
                exp.mode.Override(UnityEngine.Rendering.HighDefinition.ExposureMode.Fixed);
                exp.fixedExposure.Override(14.5f);
                var vol = new GameObject("Volumen").AddComponent<UnityEngine.Rendering.Volume>();
                vol.isGlobal = true;
                vol.sharedProfile = perfil;

                var suelo = GameObject.CreatePrimitive(PrimitiveType.Plane);
                suelo.transform.localScale = Vector3.one * 5f;
                suelo.GetComponent<Renderer>().sharedMaterial = new Material(Shader.Find("HDRP/Lit"));

                var shaderVeg = Shader.Find("Guardianes/Vegetacion");
                Debug.Log($"[GD] Shader vegetacion encontrado: {shaderVeg != null}");
                var matVeg = new Material(Shader.Find("HDRP/Lit"));
                matVeg.SetColor("_BaseColor", new Color(0.18f, 0.42f, 0.12f));
                UnityEngine.Rendering.HighDefinition.HDMaterial.ValidateMaterial(matVeg);
                matVeg.shader = shaderVeg;
                matVeg.SetShaderPassEnabled("MotionVectors", true);

                var viento = new GameObject("Viento").AddComponent<Guardianes.Vegetacion.Viento>();
                viento.fuerza = 1.2f;

                // Una "planta" de prueba: tallo vertical con hojas cuadradas a varias alturas.
                var malla = new Mesh();
                var v = new System.Collections.Generic.List<Vector3>();
                var c = new System.Collections.Generic.List<Color>();
                var t = new System.Collections.Generic.List<int>();
                for (int i = 0; i < 12; i++)
                {
                    float y = 0.5f + i * 0.45f;
                    float ang = i * 2.4f;
                    var d = new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang));
                    var lado = Vector3.Cross(d, Vector3.up) * 0.35f;
                    int b = v.Count;
                    v.Add(new Vector3(0f, y, 0f) - lado); v.Add(new Vector3(0f, y, 0f) + lado);
                    v.Add(new Vector3(0f, y, 0f) + d * 1.4f + lado + Vector3.up * 0.2f); v.Add(new Vector3(0f, y, 0f) + d * 1.4f - lado + Vector3.up * 0.2f);
                    float h = y / 6f;
                    c.Add(new Color(0f, 0f, i / 12f, h * h)); c.Add(new Color(0f, 0f, i / 12f, h * h));
                    c.Add(new Color(1f, 1f, i / 12f, h * h)); c.Add(new Color(1f, 1f, i / 12f, h * h));
                    t.AddRange(new[] { b, b + 1, b + 2, b, b + 2, b + 3, b, b + 2, b + 1, b, b + 3, b + 2 });
                }
                malla.SetVertices(v); malla.SetColors(c); malla.SetTriangles(t, 0);
                malla.RecalculateNormals(); malla.RecalculateTangents(); malla.RecalculateBounds();

                for (int k = 0; k < 2; k++)
                {
                    var planta = new GameObject(k == 0 ? "PlantaViento" : "PlantaLit");
                    planta.AddComponent<MeshFilter>().sharedMesh = malla;
                    var mat = k == 0 ? matVeg : new Material(Shader.Find("HDRP/Lit")) { color = new Color(0.18f, 0.42f, 0.12f) };
                    planta.AddComponent<MeshRenderer>().sharedMaterial = mat;
                    planta.transform.position = new Vector3(k == 0 ? -2f : 2f, 0f, 0f);
                }

                var cam = Capturador.CrearCamara("CamaraPrueba", 50f);
                cam.transform.position = new Vector3(0f, 3.5f, -11f);
                cam.transform.LookAt(new Vector3(0f, 2.8f, 0f));
                Capturador.Capturar(cam, Path.Combine(dir, "prueba_a.png"), 960, 540, 24);
                System.Threading.Thread.Sleep(700);
                Capturador.Capturar(cam, Path.Combine(dir, "prueba_b.png"), 960, 540, 4);

                foreach (var nombre in new[] { "HDRP/Lit", "Guardianes/Vegetacion" })
                {
                    var sh = Shader.Find(nombre);
                    var msgs = ShaderUtil.GetShaderMessages(sh);
                    int errs = msgs.Count(m => m.severity == UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error);
                    foreach (var m in msgs.Take(20))
                        Debug.Log($"[GD] {nombre} {m.severity}: {m.message} ({m.file}:{m.line})");
                    Debug.Log($"[GD] {nombre}: pasadas={sh.passCount} errores={errs}");
                    if (errs > 0) codigo = 1;
                }
                Debug.Log($"[GD] Pipeline activo: {UnityEngine.Rendering.RenderPipelineManager.currentPipeline?.GetType().Name ?? "ninguno"}");
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                codigo = 1;
            }
            EditorApplication.Exit(codigo);
        }

        /// <summary>Valor de un argumento "-nombre valor" de la linea de comandos (o null).</summary>
        public static string Argumento(string nombre)
        {
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (string.Equals(args[i], nombre, StringComparison.OrdinalIgnoreCase))
                    return args[i + 1];
            return null;
        }

        public static string CarpetaCapturas(string porDefecto)
        {
            var dir = Argumento("-capturas") ?? porDefecto;
            Directory.CreateDirectory(dir);
            return dir;
        }
    }
}
