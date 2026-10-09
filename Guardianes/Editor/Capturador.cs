using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace Guardianes.EditorTools
{
    /// <summary>
    /// Fotos fuera del modo juego (batch o editor): renderiza una camara HDRP varias veces para que
    /// converjan TAA, exposicion automatica, nubes volumetricas y SSGI, y guarda un PNG.
    /// </summary>
    public static class Capturador
    {
        public static Camera CrearCamara(string nombre, float fov = 55f, float lejos = 12000f)
        {
            var go = new GameObject(nombre);
            var cam = go.AddComponent<Camera>();
            cam.fieldOfView = fov;
            cam.nearClipPlane = 0.3f;
            cam.farClipPlane = lejos;
            var hd = go.AddComponent<HDAdditionalCameraData>();
            hd.antialiasing = HDAdditionalCameraData.AntialiasingMode.TemporalAntialiasing;
            hd.TAAQuality = HDAdditionalCameraData.TAAQualityLevel.High;
            return cam;
        }

        /// <summary>
        /// Fuera del modo juego la exposicion automatica no avanza (no hay deltaTime): un volumen de
        /// prioridad maxima la hace instantanea para que cada foto salga bien expuesta.
        /// </summary>
        public static void ExposicionInmediata()
        {
            var perfil = ScriptableObject.CreateInstance<VolumeProfile>();
            var exp = perfil.Add<Exposure>(false);
            exp.adaptationMode.Override(AdaptationMode.Fixed);
            var go = new GameObject("ExposicionCapturas");
            var v = go.AddComponent<Volume>();
            v.isGlobal = true;
            v.priority = 1000f;
            v.sharedProfile = perfil;
        }

        /// <summary>Renderiza 'fotogramas' veces y guarda el ultimo en 'ruta' (PNG, sRGB).</summary>
        public static void Capturar(Camera cam, string ruta, int ancho = 1920, int alto = 1080, int fotogramas = 48)
        {
            var desc = new RenderTextureDescriptor(ancho, alto, RenderTextureFormat.ARGB32, 24)
            {
                sRGB = true,
                msaaSamples = 1
            };
            var rt = new RenderTexture(desc);
            rt.Create();
            var anterior = cam.targetTexture;
            cam.targetTexture = rt;

            var peticion = new RenderPipeline.StandardRequest { destination = rt };
            for (int i = 0; i < fotogramas; i++)
            {
                if (RenderPipeline.SupportsRenderRequest(cam, peticion))
                    RenderPipeline.SubmitRenderRequest(cam, peticion);
                else
                    cam.Render();
            }

            var activa = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(ancho, alto, TextureFormat.RGB24, false, false);
            tex.ReadPixels(new Rect(0, 0, ancho, alto), 0, 0);
            tex.Apply();
            RenderTexture.active = activa;

            Directory.CreateDirectory(Path.GetDirectoryName(ruta));
            File.WriteAllBytes(ruta, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            cam.targetTexture = anterior;
            rt.Release();
            Object.DestroyImmediate(rt);
            Debug.Log($"[GD] Captura: {ruta}");
        }
    }
}
