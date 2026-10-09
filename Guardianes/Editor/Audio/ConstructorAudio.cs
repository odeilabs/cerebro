using System.Linq;
using Guardianes.Audio;
using UnityEditor;
using UnityEngine;

namespace Guardianes.EditorTools.Audio
{
    /// <summary>
    /// Pone el sonido en la escena: SonidoJuego con los clips de Assets/_Game/Art/Audio (los genera
    /// Tools~/arte/audio/generar_sonidos.py, y los de cada criatura su propio juego) y ajusta su
    /// importacion. La voz y el cuerpo de la criatura salen de su carpeta (kong, serpiente...).
    /// </summary>
    public static class ConstructorAudio
    {
        public const string Carpeta = "Assets/_Game/Art/Audio";

        public static AudioClip[] Clips(string grupo, string prefijo) =>
            AssetDatabase.FindAssets("t:AudioClip", new[] { $"{Carpeta}/{grupo}" })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(r => System.IO.Path.GetFileNameWithoutExtension(r).StartsWith(prefijo + "_"))
                .OrderBy(r => r)
                .Select(AssetDatabase.LoadAssetAtPath<AudioClip>)
                .ToArray();

        /// <summary>
        /// Crea el SonidoJuego de la escena. criatura: carpeta de sus sonidos; voz, percusion,
        /// pisada, golpe y aspaviento: prefijos de sus clips (los de Kong por defecto).
        /// </summary>
        public static SonidoJuego Crear(string criatura = "kong", string voz = "rugido", string percusion = "pecho", string pisada = "pisada",
                                        string golpe = "golpe", string aspaviento = "aspaviento")
        {
            var go = new GameObject("Sonido");
            go.SetActive(false);
            var s = go.AddComponent<SonidoJuego>();
            s.voces = Clips(criatura, voz);
            s.percusion = Clips(criatura, percusion);
            s.pisadas = Clips(criatura, pisada);
            s.golpes = Clips(criatura, golpe);
            s.aspavientos = Clips(criatura, aspaviento);
            s.disparos = Clips("armas", "disparo");
            s.cohetes = Clips("armas", "cohete");
            s.explosiones = Clips("destruccion", "explosion");
            s.maderas = Clips("destruccion", "madera");
            s.metales = Clips("destruccion", "metal");
            s.arboles = Clips("destruccion", "arbol");
            s.selva = Clips("ambiente", "selva").FirstOrDefault();
            s.rio = Clips("ambiente", "rio").FirstOrDefault();
            s.fuego = Clips("ambiente", "fuego").FirstOrDefault();
            int total = s.voces.Length + s.percusion.Length + s.pisadas.Length + s.golpes.Length + s.disparos.Length + s.explosiones.Length;
            if (total == 0) Debug.LogWarning("[GD] Sin sonidos: genera Tools~/arte/audio/generar_sonidos.py --proyecto <proyecto>");
            go.SetActive(true);
            return s;
        }
    }

    /// <summary>Importacion: ambientes en streaming (largos), efectos descomprimidos al cargar.</summary>
    public class ImportadorAudio : AssetPostprocessor
    {
        void OnPreprocessAudio()
        {
            if (!assetPath.StartsWith(ConstructorAudio.Carpeta)) return;
            var ai = (AudioImporter)assetImporter;
            bool ambiente = assetPath.Contains("/ambiente/");
            var s = ai.defaultSampleSettings;
            s.loadType = ambiente ? AudioClipLoadType.Streaming : AudioClipLoadType.DecompressOnLoad;
            s.compressionFormat = AudioCompressionFormat.Vorbis;
            s.quality = ambiente ? 0.6f : 0.75f;
            s.sampleRateSetting = AudioSampleRateSetting.PreserveSampleRate;
            ai.defaultSampleSettings = s;
            ai.forceToMono = !ambiente;
            ai.loadInBackground = ambiente;
        }
    }
}
