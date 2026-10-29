using Saylo.Modules.Translation.Contracts;
using Saylo.Modules.Translation.Pronunciation;

namespace Saylo.Modules.Translation.Tests;

public sealed class PronunciationServiceTests
{
    [Fact]
    public async Task RealSpanishEngineStartsOnlyAfterTextHasBeenConsumed()
    {
        await using var pronunciation = new LocalPronunciationEngine(PronunciationResources.CreateCatalog(Path.GetTempPath()));
        var engines = new TestEngines { Translate = (_, _, _, _) => Task.FromResult("Hola. Gracias.") };
        var service = new TranslationService(engines, engines, pronunciation, engines);
        await using var updates = service.TranslateTextAsync(new("Hello. Thank you.", Language.English, Language.Spanish)).GetAsyncEnumerator();
        Assert.True(await updates.MoveNextAsync());
        Assert.IsType<TextTranslationCompleted>(updates.Current);
        Assert.Equal(0, pronunciation.SpanishWordCount);
        var optional = new List<TranslationUpdate>();
        while (await updates.MoveNextAsync()) optional.Add(updates.Current);
        var completed = Assert.Single(optional.OfType<PronunciationCompleted>());
        Assert.Equal("ˈola. ˈɡɾasjas.", completed.Pronunciation.Text);
        Assert.Equal(PronunciationNotation.Ipa, completed.Pronunciation.Notation);
        Assert.Single(optional.OfType<SpeechSynthesisCompleted>());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RealPronunciationFailureLeavesTextAndIndependentOptionalStageAvailable(bool voice)
    {
        await using var pronunciation = new LocalPronunciationEngine(PronunciationResources.CreateCatalog(Path.GetTempPath()));
        var engines = new TestEngines { Translate = (_, _, _, _) => Task.FromResult("Jencer") };
        var service = new TranslationService(engines, engines, pronunciation, engines);
        var stream = voice ? service.TranslateVoiceAsync(new(ContractValidationTests.CreateAudio(), Language.English, Language.Spanish))
            : service.TranslateTextAsync(new("Name fixture", Language.English, Language.Spanish));
        var updates = new List<TranslationUpdate>();
        await foreach (var update in stream) updates.Add(update);
        Assert.Equal("Jencer", Assert.Single(updates.OfType<TextTranslationCompleted>()).TranslatedText);
        Assert.Empty(updates.OfType<PronunciationCompleted>());
        var failed = Assert.Single(updates.OfType<TranslationStageFailed>());
        Assert.Equal(TranslationStage.Pronunciation, failed.Stage);
        Assert.Equal(TranslationErrorCode.InvalidEngineOutput, failed.Code);
        Assert.Single(updates.OfType<SpeechSynthesisCompleted>());
        Assert.All(updates, update => Assert.Equal(updates[0].OperationId, update.OperationId));
        Assert.Equal("ˈola", (await pronunciation.GenerateAsync("hola", Language.Spanish)).Text);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task IntegrityFailureRetainsOriginalErrorAndNeverStartsTheRealEngine(bool voice)
    {
        await using var pronunciation = new LocalPronunciationEngine(PronunciationResources.CreateCatalog(Path.GetTempPath()));
        var engines = new TestEngines { Translate = (_, _, _, _) => throw new TranslationEngineException(
            TranslationStage.TextTranslation, TranslationErrorCode.IntegrityValidationFailed, "Integrity fixture.") };
        var service = new TranslationService(engines, engines, pronunciation, engines);
        var stream = voice ? service.TranslateVoiceAsync(new(ContractValidationTests.CreateAudio(), Language.English, Language.Spanish))
            : service.TranslateTextAsync(new("Fixture", Language.English, Language.Spanish));
        var updates = new List<TranslationUpdate>();
        await foreach (var update in stream) updates.Add(update);
        var failure = Assert.Single(updates.OfType<TranslationStageFailed>());
        Assert.Equal(TranslationStage.TextTranslation, failure.Stage);
        Assert.Equal(TranslationErrorCode.IntegrityValidationFailed, failure.Code);
        Assert.Empty(updates.OfType<TextTranslationCompleted>());
        Assert.Empty(updates.OfType<PronunciationCompleted>());
        Assert.DoesNotContain(engines.Calls, call => call.Stage == TranslationStage.SpeechSynthesis);
        Assert.Equal(0, pronunciation.SpanishWordCount);
        Assert.Equal(0, pronunciation.DictionaryLoadCount);
    }
}
