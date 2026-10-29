using Saylo.Desktop.Models;
using Saylo.Desktop.ViewModels;
using Saylo.Modules.Translation.Contracts;

namespace Saylo.Desktop.Tests;

public sealed class WorkspaceViewModelTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void SelectingTheOtherLanguageKeepsAValidPairDuringEveryNotification(bool changeSource)
    {
        var model = new LanguageSelectionViewModel();
        var changes = new List<string?>();
        model.PropertyChanged += (_, args) =>
        {
            Assert.NotEqual(model.SourceLanguage.Language, model.TargetLanguage.Language);
            changes.Add(args.PropertyName);
        };

        if (changeSource)
            model.SourceLanguage = LanguageOption.English;
        else
            model.TargetLanguage = LanguageOption.Spanish;

        Assert.Equal(LanguageOption.English, model.SourceLanguage);
        Assert.Equal(LanguageOption.Spanish, model.TargetLanguage);
        Assert.Contains(nameof(model.SourceLanguage), changes);
        Assert.Contains(nameof(model.TargetLanguage), changes);
        Assert.Contains(nameof(model.DirectionLabel), changes);
    }

    [Fact]
    public void SwappingLanguagesPreservesTheDraftAndCanBeReversed()
    {
        var workspace = new MainViewModel();
        workspace.TextInput.Text = "  Buenos días, ¿en qué puedo ayudarle?\nPedido 123.  ";
        var draft = workspace.TextInput.Text;

        workspace.Languages.SwapCommand.Execute(null);

        Assert.Equal("Inglés → Español", workspace.Languages.DirectionLabel);
        Assert.Equal(draft, workspace.TextInput.Text);
        workspace.Languages.SwapCommand.Execute(null);
        Assert.Equal("Español → Inglés", workspace.Languages.DirectionLabel);
        Assert.Equal(draft, workspace.TextInput.Text);
    }

    [Fact]
    public void TransientNullOrUnsupportedSelectionsCannotInvalidateThePair()
    {
        var model = new LanguageSelectionViewModel();

        model.SourceLanguage = null!;
        model.TargetLanguage = new LanguageOption(Language.English, "Unsupported label", "XX");

        Assert.Equal(LanguageOption.Spanish, model.SourceLanguage);
        Assert.Equal(LanguageOption.English, model.TargetLanguage);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\r\n\t")]
    public void EmptyOrWhitespaceDraftsCannotBecomeTranslationRequests(string? text)
    {
        var model = new TextInputViewModel { Text = text };

        Assert.False(model.HasText);
        Assert.ThrowsAny<ArgumentException>(() => model.CreateRequest(Language.Spanish, Language.English));
    }

    [Fact]
    public void RequestUsesTheSelectedDirectionAndPreservesOriginalText()
    {
        var workspace = new MainViewModel();
        workspace.Languages.SwapCommand.Execute(null);
        workspace.TextInput.Text = "  Good morning.\nOrder #123.  ";

        var request = workspace.TextInput.CreateRequest(
            workspace.Languages.SourceLanguage.Language, workspace.Languages.TargetLanguage.Language);

        Assert.Equal(workspace.TextInput.Text, request.Text);
        Assert.Equal(Language.English, request.SourceLanguage);
        Assert.Equal(Language.Spanish, request.TargetLanguage);
    }

    [Fact]
    public void ClearingWhitespaceIsAllowedAndUpdatesTheCommandAndCounter()
    {
        var model = new TextInputViewModel();
        var commandChanges = 0;
        model.ClearCommand.CanExecuteChanged += (_, _) => commandChanges++;
        Assert.False(model.ClearCommand.CanExecute(null));

        model.Text = "   ";
        Assert.False(model.HasText);
        Assert.True(model.ClearCommand.CanExecute(null));
        Assert.Equal(3, model.CharacterCount);
        model.ClearCommand.Execute(null);

        Assert.Equal(string.Empty, model.Text);
        Assert.Equal("0 caracteres", model.CharacterCountLabel);
        Assert.False(model.ClearCommand.CanExecute(null));
        Assert.True(commandChanges >= 2);
    }

    [Fact]
    public void TheShortTextBenchmarkDoesNotBecomeAnInputLengthLimit()
    {
        var model = new TextInputViewModel { Text = new string('a', 500) };

        Assert.True(model.HasText);
        Assert.Equal(500, model.CreateRequest(Language.Spanish, Language.English).Text.Length);
    }
}
