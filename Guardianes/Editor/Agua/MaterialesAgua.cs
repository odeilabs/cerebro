using System.IO;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;

namespace Guardianes.EditorTools.Agua
{
    /// <summary>
    /// Texturas de ondas (normales periodicas: suma de ondas con frecuencias enteras en el toro + ruido
    /// periodico) y el material de los rios (Guardianes/Rio: transparente, refraccion y dos capas de
    /// ondas que corren rio abajo).
    /// </summary>
    public static class MaterialesAgua
    {
        public const string Carpeta = "Assets/_Game/Art/Agua";
        public const string RutaNormal = Carpeta + "/agua_normal.png";
        public const string RutaDetalle = Carpeta + "/agua_detalle_mascara.png";
        public const string RutaRio = Carpeta + "/Rio.mat";

        public static Material CrearRio()
        {
            CrearTexturas();
            return MaterialesLit.Crear(RutaRio, "Guardianes/Rio", m =>
            {
                m.SetFloat("_SurfaceType", 1f);
                m.SetFloat("_BlendMode", 0f);
                // Rio de selva turbio: verde pardo y poco transparente (si no, desde arriba solo se
                // ve el lecho de grava y el cauce parece seco).
                m.SetColor("_BaseColor", new Color(0.1f, 0.17f, 0.12f, 0.9f));
                m.SetFloat("_Metallic", 0f);
                m.SetFloat("_Smoothness", 0.95f);
                m.SetTexture("_NormalMap", MaterialesLit.Textura(RutaNormal));
                m.SetFloat("_NormalScale", 0.55f);
                m.SetTexture("_DetailMap", MaterialesLit.Textura(RutaDetalle));
                m.SetFloat("_UVDetail", 1f);
                m.SetFloat("_DetailAlbedoScale", 0f);
                m.SetFloat("_DetailNormalScale", 0.9f);
                m.SetFloat("_DetailSmoothnessScale", 0f);
                m.SetFloat("_RefractionModel", 1f); // plano
                m.SetFloat("_Ior", 1.33f);
                m.SetFloat("_ATDistance", 0.6f);
                m.SetColor("_TransmittanceColor", new Color(0.32f, 0.45f, 0.34f));
                m.SetFloat("_EnableFogOnTransparent", 1f);
                m.SetFloat("_CullMode", 2f);
            });
        }

        /// <summary>Lamina de las cascadas (Guardianes/Cascada) con la espuma de Tools/arte/efectos.</summary>
        public static Material CrearCascada()
        {
            string ruta = Carpeta + "/Cascada.mat";
            var sh = Shader.Find("Guardianes/Cascada");
            var m = AssetDatabase.LoadAssetAtPath<Material>(ruta);
            if (m == null) { m = new Material(sh); AssetDatabase.CreateAsset(m, ruta); }
            else m.shader = sh;
            m.SetTexture("_MainTex", MaterialesLit.Textura("Assets/_Game/Art/Efectos/Texturas/cascada_espuma.png"));
            m.SetColor("_ColorAgua", new Color(0.45f, 0.64f, 0.58f, 0.72f));
            m.SetFloat("_Velocidad", 1.1f);
            m.SetFloat("_Suavidad", 0.5f);
            m.SetFloat("_Brillo", 1.25f);
            m.renderQueue = 3000;
            EditorUtility.SetDirty(m);
            return m;
        }

        /// <summary>Niebla de agua pulverizada al pie de las cascadas (Guardianes/Particula iluminada).</summary>
        public static Material CrearNiebla()
        {
            string ruta = Carpeta + "/NieblaCascada.mat";
            var sh = Shader.Find("Guardianes/Particula");
            var m = AssetDatabase.LoadAssetAtPath<Material>(ruta);
            if (m == null) { m = new Material(sh); AssetDatabase.CreateAsset(m, ruta); }
            else m.shader = sh;
            m.SetTexture("_MainTex", MaterialesLit.Textura("Assets/_Game/Art/Efectos/Texturas/humo.png"));
            m.SetColor("_Color", Color.white);
            m.SetFloat("_Emisiva", 0f);
            m.SetFloat("_Suavidad", 4f);
            m.SetFloat("_Dispersion", 0.9f);
            m.SetFloat("_Origen", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetFloat("_Destino", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            m.renderQueue = 3000;
            EditorUtility.SetDirty(m);
            return m;
        }

        static void CrearTexturas()
        {
            if (File.Exists(RutaNormal) && File.Exists(RutaDetalle)) return;
            Directory.CreateDirectory(Carpeta);
            const int Res = 1024;
            var h = Alturas(Res, 7, 1.0f);
            var h2 = Alturas(Res, 23, 0.8f);
            var normal = new Color32[Res * Res];
            var detalle = new Color32[Res * Res];
            for (int y = 0; y < Res; y++)
            for (int x = 0; x < Res; x++)
            {
                float3 n = Normal(h, Res, x, y, 6f);
                float3 n2 = Normal(h2, Res, x, y, 4f);
                normal[y * Res + x] = new Color32((byte)((n.x * 0.5f + 0.5f) * 255), (byte)((n.y * 0.5f + 0.5f) * 255), (byte)((n.z * 0.5f + 0.5f) * 255), 255);
                // Mapa de detalle HDRP: R albedo, G normal Y, B suavidad, A normal X.
                detalle[y * Res + x] = new Color32(128, (byte)((n2.y * 0.5f + 0.5f) * 255), 128, (byte)((n2.x * 0.5f + 0.5f) * 255));
            }
            Guardar(RutaNormal, normal, Res);
            Guardar(RutaDetalle, detalle, Res);
            AssetDatabase.ImportAsset(RutaNormal, ImportAssetOptions.ForceSynchronousImport);
            AssetDatabase.ImportAsset(RutaDetalle, ImportAssetOptions.ForceSynchronousImport);
        }

        static float[] Alturas(int res, int semilla, float escala)
        {
            var h = new float[res * res];
            var rnd = new Unity.Mathematics.Random((uint)semilla);
            // Ondas con frecuencias enteras: periodicas en la textura (sin costuras).
            const int Ondas = 28;
            var k = new int2[Ondas];
            var fase = new float[Ondas];
            var amp = new float[Ondas];
            for (int i = 0; i < Ondas; i++)
            {
                int2 f;
                do { f = new int2(rnd.NextInt(-14, 15), rnd.NextInt(2, 22)); } while (math.all(f == 0));
                k[i] = f;
                fase[i] = rnd.NextFloat(0f, 6.2831853f);
                amp[i] = 1f / math.pow(math.length(f), 1.3f);
            }
            for (int y = 0; y < res; y++)
            for (int x = 0; x < res; x++)
            {
                float2 uv = new float2(x, y) / res;
                float v = 0f;
                for (int i = 0; i < Ondas; i++)
                    v += amp[i] * math.sin(6.2831853f * math.dot(k[i], uv) + fase[i]);
                // Ruido periodico fino.
                v += 0.08f * noise.pnoise(uv * 32f * escala, new float2(32f * escala)) + 0.04f * noise.pnoise(uv * 64f, new float2(64f));
                h[y * res + x] = v;
            }
            return h;
        }

        static float3 Normal(float[] h, int res, int x, int y, float fuerza)
        {
            float l = h[y * res + (x + res - 1) % res], r = h[y * res + (x + 1) % res];
            float d = h[((y + res - 1) % res) * res + x], u = h[((y + 1) % res) * res + x];
            return math.normalize(new float3((l - r) * fuerza, (d - u) * fuerza, 1f));
        }

        static void Guardar(string ruta, Color32[] px, int res)
        {
            var tex = new Texture2D(res, res, TextureFormat.RGBA32, false, true);
            tex.SetPixels32(px);
            tex.Apply();
            File.WriteAllBytes(ruta, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
        }
    }
}
