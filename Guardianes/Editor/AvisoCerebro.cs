using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Guardianes.EditorTools
{
    /// <summary>
    /// Al abrir el editor (una vez por sesión, nunca en batch), compara la copia del paquete común del
    /// juego (Packages/com.odeilabs.guardianes, con su .sincronizacion.json) con el cerebro de la saga, si
    /// está al lado, y avisa en la consola de lo que ha cambiado en el cerebro y aún no se ha traído, y
    /// de lo que se ha cambiado en la copia y no se ha subido. Traer y subir se hace con
    /// Tools~/traer_cerebro.py (la misma comparación). Sin cerebro al lado (otra máquina), no dice nada.
    /// </summary>
    [InitializeOnLoad]
    public static class AvisoCerebro
    {
        const string Foto = ".sincronizacion.json";
        static readonly HashSet<string> Propios = new HashSet<string> { Foto, "ORIGEN.md", "ORIGEN.md.meta" };
        static readonly HashSet<string> Binarios = new HashSet<string>
            { ".png", ".jpg", ".jpeg", ".tga", ".exr", ".psd", ".wav", ".ogg", ".mp3", ".fbx", ".obj", ".bytes", ".asset", ".dll", ".so", ".ttf", ".otf" };

        [Serializable] class Entrada { public string ruta; public string hash; }
        [Serializable] class Sincronizacion { public string commit; public string fecha; public Entrada[] ficheros; }

        static AvisoCerebro()
        {
            if (Application.isBatchMode || SessionState.GetBool("Guardianes.AvisoCerebro", false)) return;
            SessionState.SetBool("Guardianes.AvisoCerebro", true);
            EditorApplication.delayCall += () => Comprobar(false);
        }

        [MenuItem("Guardianes/Cerebro/Comprobar cambios")]
        static void ComprobarMenu() => Comprobar(true);

        /// <summary>Compara copia, foto y cerebro y lo dice en la consola. Devuelve los cambios del cerebro sin traer.</summary>
        public static int Comprobar(bool siempre)
        {
            try
            {
                string copia = Path.GetFullPath("Packages/com.odeilabs.guardianes");
                string rutaFoto = Path.Combine(copia, Foto);
                if (!Directory.Exists(copia))
                {
                    if (siempre) Debug.Log("[GD] El paquete común no está copiado en este proyecto (Packages/com.odeilabs.guardianes).");
                    return 0;
                }
                if (!File.Exists(rutaFoto))
                {
                    Debug.LogWarning("[GD] La copia del paquete común no tiene " + Foto + ": créala con python Packages/com.odeilabs.guardianes/Tools~/traer_cerebro.py iniciar --commit <commit de su ORIGEN.md>");
                    return 0;
                }
                string cerebro = BuscarCerebro(Directory.GetCurrentDirectory());
                if (cerebro == null)
                {
                    if (siempre) Debug.Log("[GD] No hay cerebro al lado del juego: nada que comparar.");
                    return 0;
                }

                var foto = JsonUtility.FromJson<Sincronizacion>(File.ReadAllText(rutaFoto));
                var base_ = new Dictionary<string, string>();
                if (foto.ficheros != null) foreach (var e in foto.ficheros) base_[e.ruta] = e.hash;
                var enCerebro = Hashes(cerebro);
                var enCopia = Hashes(copia);

                var nuevos = new List<string>();
                var locales = new List<string>();
                var todas = new HashSet<string>(base_.Keys);
                todas.UnionWith(enCerebro.Keys);
                todas.UnionWith(enCopia.Keys);
                foreach (var r in todas)
                {
                    base_.TryGetValue(r, out var b);
                    enCerebro.TryGetValue(r, out var c);
                    enCopia.TryGetValue(r, out var k);
                    if (c == k || r.EndsWith(".meta")) continue;
                    if (c != b) nuevos.Add(r);
                    if (k != b) locales.Add(r);
                }
                nuevos.Sort();
                locales.Sort();
                string script = Path.Combine(cerebro, "Tools~", "traer_cerebro.py");
                if (nuevos.Count > 0)
                    Debug.LogWarning($"[GD] El cerebro tiene {nuevos.Count} cambios que la copia de este juego no tiene (copia de {foto.commit}, {foto.fecha}): {Lista(nuevos)}.\n" +
                                     $"Para verlos y traerlos: python \"{script}\" estado (desde la carpeta del juego), y luego traer <rutas>.");
                if (locales.Count > 0)
                    Debug.LogWarning($"[GD] La copia del paquete común tiene {locales.Count} cambios que no están en el cerebro: {Lista(locales)}.\n" +
                                     $"Súbelos para que no se pierdan: python \"{script}\" subir <rutas>.");
                if (nuevos.Count == 0 && locales.Count == 0 && siempre)
                    Debug.Log($"[GD] La copia del paquete común está al día con el cerebro (copia de {foto.commit}).");
                return nuevos.Count;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[GD] No he podido comparar la copia del paquete con el cerebro: " + e.Message);
                return 0;
            }
        }

        static string Lista(List<string> l) =>
            string.Join(", ", l.GetRange(0, Math.Min(6, l.Count))) + (l.Count > 6 ? $" y {l.Count - 6} más" : "");

        static string BuscarCerebro(string desde)
        {
            for (var d = new DirectoryInfo(desde); d != null; d = d.Parent)
            {
                string c = Path.Combine(d.FullName, "cerebro", "Guardianes");
                if (File.Exists(Path.Combine(c, "package.json"))) return c;
            }
            return null;
        }

        /// <summary>{ruta relativa: sha1} con los finales de línea normalizados en el texto (como traer_cerebro.py).</summary>
        static Dictionary<string, string> Hashes(string raiz)
        {
            var res = new Dictionary<string, string>();
            using var sha = SHA1.Create();
            foreach (var f in Directory.GetFiles(raiz, "*", SearchOption.AllDirectories))
            {
                string rel = f.Substring(raiz.Length).TrimStart('\\', '/').Replace('\\', '/');
                if (Propios.Contains(rel) || rel.EndsWith(".pyc") || rel.Contains("__pycache__/") ||
                    rel.Contains("_revision/") || rel.StartsWith(".git/")) continue;
                byte[] datos = File.ReadAllBytes(f);
                if (!Binarios.Contains(Path.GetExtension(f).ToLowerInvariant())) datos = SinCR(datos);
                var sb = new StringBuilder(40);
                foreach (byte x in sha.ComputeHash(datos)) sb.Append(x.ToString("x2"));
                res[rel] = sb.ToString();
            }
            return res;
        }

        static byte[] SinCR(byte[] d)
        {
            var o = new List<byte>(d.Length);
            for (int i = 0; i < d.Length; i++)
                if (!(d[i] == '\r' && i + 1 < d.Length && d[i + 1] == '\n')) o.Add(d[i]);
            return o.ToArray();
        }
    }
}
