using Saylo.Modules.Translation.Contracts;

namespace Saylo.Modules.Translation.Tests;

public sealed class ContractValidationTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \t\r\n")]
    public void EmptyTextIsRejected(string? text) =>
        Assert.ThrowsAny<ArgumentException>(() => new TextTranslationRequest(text!, Language.Spanish, Language.English));

    [Theory]
    [InlineData("es")]
    [InlineData("en")]
    public void EqualLanguagesAreRejectedForBothInputs(string code)
    {
        var language = Language.FromCode(code);
        Assert.Throws<ArgumentException>(() => new TextTranslationRequest("fixture", language, language));
        Assert.Throws<ArgumentException>(() => new VoiceTranslationRequest(CreateAudio(), language, language));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("fr")]
    [InlineData("de")]
    [InlineData("en-US")]
    public void UnsupportedLanguageCodesAreRejected(string? code) =>
        Assert.ThrowsAny<ArgumentException>(() => Language.FromCode(code!));

    [Theory]
    [InlineData(" ES ", "es")]
    [InlineData("EN", "en")]
    public void LanguageCodesAreNormalized(string code, string expected) =>
        Assert.Equal(expected, Language.FromCode(code).Code);

    [Fact]
    public void MissingLanguagesAndAudioAreRejected()
    {
        Assert.Throws<ArgumentNullException>(() => new TextTranslationRequest("fixture", null!, Language.English));
        Assert.Throws<ArgumentNullException>(() => new TextTranslationRequest("fixture", Language.Spanish, null!));
        Assert.Throws<ArgumentNullException>(() => new VoiceTranslationRequest(CreateAudio(), null!, Language.English));
        Assert.Throws<ArgumentNullException>(() => new VoiceTranslationRequest(CreateAudio(), Language.Spanish, null!));
        Assert.Throws<ArgumentNullException>(() => new VoiceTranslationRequest(null!, Language.Spanish, Language.English));
    }

    [Fact]
    public void EmptyAudioIsRejected() =>
        Assert.Throws<ArgumentException>(() => new AudioData(ReadOnlyMemory<byte>.Empty, AudioFormat.Pcm16, 16000, 1));

    [Theory]
    [InlineData(0)]
    [InlineData(99)]
    public void UnsupportedAudioFormatsAreRejected(int format) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new AudioData(new byte[2], (AudioFormat)format, 16000, 1));

    [Theory]
    [InlineData(0, 1)]
    [InlineData(-1, 1)]
    [InlineData(16000, 0)]
    [InlineData(16000, -1)]
    public void InvalidAudioMetadataIsRejected(int sampleRate, int channels) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new AudioData(new byte[2], AudioFormat.Pcm16, sampleRate, channels));

    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    public void IncompleteAudioFramesAreRejected(int bytes, int channels) =>
        Assert.Throws<ArgumentException>(() => new AudioData(new byte[bytes], AudioFormat.Pcm16, 16000, channels));

    [Fact]
    public void AudioOwnsASnapshotAndPlaybackMetadata()
    {
        var bytes = new byte[] { 1, 2, 3, 4 };
        var audio = new AudioData(bytes, AudioFormat.Pcm16, 48000, 2);
        bytes[0] = 99;
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, audio.Data.ToArray());
        Assert.Equal(48000, audio.SampleRate);
        Assert.Equal(2, audio.Channels);
        Assert.Equal(16, audio.BitsPerSample);
        Assert.Equal(AudioFormat.Pcm16, audio.Format);
    }

    [Theory]
    [InlineData(PronunciationNotation.Approximate)]
    [InlineData(PronunciationNotation.Ipa)]
    public void PronunciationRetainsItsExplicitNotation(PronunciationNotation notation)
    {
        var pronunciation = new PronunciationResult("phonetic fixture", notation);
        Assert.Equal(notation, pronunciation.Notation);
        Assert.Equal("phonetic fixture", pronunciation.Text);
    }

    [Fact]
    public void InvalidPronunciationIsRejected()
    {
        Assert.ThrowsAny<ArgumentException>(() => new PronunciationResult(" ", PronunciationNotation.Ipa));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PronunciationResult("fixture", (PronunciationNotation)99));
    }

    internal static AudioData CreateAudio() => new(new byte[4], AudioFormat.Pcm16, 16000, 1);
}
