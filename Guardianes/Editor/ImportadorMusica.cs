using UnityEditor;
using UnityEngine;

namespace Guardianes.EditorTools
{
    /// <summary>
    /// Los bucles de musica (Resources/Audio/musica, los de generar_musica.py): se leen del disco mientras suenan
    /// (Streaming) y comprimidos en Vorbis, para no tener un minuto de estereo descomprimido por cada uno en memoria.
    /// </summary>
    public class ImportadorMusica : AssetPostprocessor
    {
        void OnPreprocessAudio()
        {
            if (!assetPath.Replace('\\', '/').Contains("/Audio/musica/")) return;
            var imp = (AudioImporter)assetImporter;
            var s = imp.defaultSampleSettings;
            s.loadType = AudioClipLoadType.Streaming;
            s.compressionFormat = AudioCompressionFormat.Vorbis;
            s.quality = 0.6f;
            s.sampleRateSetting = AudioSampleRateSetting.PreserveSampleRate;
            imp.defaultSampleSettings = s;
            imp.loadInBackground = true;
        }
    }
}
