using Saylo.Modules.Translation.Contracts;

namespace Saylo.Modules.Translation.Tests;

public sealed class ResourceFailureTests
{
    [Theory]
    [InlineData("memory", TranslationErrorCode.InsufficientMemory)]
    [InlineData("file", TranslationErrorCode.ResourceUnavailable)]
    [InlineData("access", TranslationErrorCode.ResourceUnavailable)]
    public async Task ResourceFailuresHaveSafeTypedCodes(string kind, TranslationErrorCode expected)
    {
        var engines = new TestEngines
        {
            Translate = (_, _, _, _) => throw (kind switch
            {
                "memory" => new OutOfMemoryException("private content"),
                "file" => new IOException("private content"),
                _ => new UnauthorizedAccessException("private content")
            })
        };
        var updates = new List<TranslationUpdate>();

        await foreach (var update in engines.CreateService().TranslateTextAsync(new("Original", Language.Spanish, Language.English)))
            updates.Add(update);

        var error = Assert.IsType<TranslationStageFailed>(Assert.Single(updates));
        Assert.Equal(TranslationStage.TextTranslation, error.Stage);
        Assert.Equal(expected, error.Code);
        Assert.DoesNotContain("private content", error.ToString());
    }
}
