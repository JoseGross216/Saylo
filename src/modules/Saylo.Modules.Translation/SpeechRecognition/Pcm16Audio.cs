using System.Buffers.Binary;
using Saylo.Modules.Translation.Contracts;

namespace Saylo.Modules.Translation.SpeechRecognition;

internal static class Pcm16Audio
{
    internal static void Validate(AudioData audio)
    {
        ArgumentNullException.ThrowIfNull(audio);
        if (audio.Format != AudioFormat.Pcm16 || audio.SampleRate != 16000 || audio.Channels != 1)
            throw Failure("Whisper currently requires raw PCM16 mono audio at 16000 Hz. Resampling is not implemented.");
        var bytes = audio.Data.Span;
        if (bytes.IsEmpty || bytes.Length % 2 != 0)
            throw Failure("Audio must contain complete PCM16 samples.");
        if (bytes.Length >= 12 && bytes[..4].SequenceEqual("RIFF"u8) && bytes.Slice(8, 4).SequenceEqual("WAVE"u8))
            throw Failure("A WAV container is not raw PCM16. Decode it before calling this engine.");
    }

    internal static float[] Convert(AudioData audio, CancellationToken token)
    {
        Validate(audio);
        var data = audio.Data.Span;
        var samples = new float[data.Length / 2];
        for (var i = 0; i < samples.Length; i++)
        {
            if ((i & 4095) == 0)
                token.ThrowIfCancellationRequested();
            samples[i] = BinaryPrimitives.ReadInt16LittleEndian(data.Slice(i * 2, 2)) / 32768f;
        }
        return samples;
    }

    private static TranslationEngineException Failure(string message) =>
        new(TranslationStage.SpeechRecognition, TranslationErrorCode.UnsupportedAudioFormat, message);
}
