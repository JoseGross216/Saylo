using System.Security.Cryptography;
using Saylo.Modules.ModelManagement;
using Saylo.Modules.ModelManagement.Contracts;

namespace Saylo.Modules.Translation.Tests;

public sealed class LocalModelCatalogTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "SayloModelTests", Guid.NewGuid().ToString("N"));
    private readonly byte[] _bytes = [1, 2, 3, 4];

    public LocalModelCatalogTests() => Directory.CreateDirectory(_directory);

    private ModelDescriptor Descriptor() => new("fixture", "v1", ModelPurpose.SpeechRecognition, ["es", "en"],
        _directory, [new("fixture.bin", Convert.ToHexString(SHA256.HashData(_bytes)), _bytes.Length)]);

    [Fact]
    public async Task MissingModelIsNotListedOrLocatedAndHasAClearDiagnostic()
    {
        var descriptor = Descriptor();
        var catalog = new LocalModelCatalog([descriptor]);
        Assert.Null(await catalog.LocateAsync("fixture", "v1"));
        Assert.Empty(await catalog.GetInstalledModelsAsync());
        var result = await catalog.ValidateAsync(descriptor);
        Assert.Equal(ModelAvailability.Missing, result.Availability);
        Assert.Equal(ModelIntegrity.Unknown, result.Integrity);
        Assert.Equal("FileMissing", result.DiagnosticCode);
    }

    [Fact]
    public async Task ValidFilesHaveVerifiedIntegrityButNativeCompatibilityRemainsUnknown()
    {
        await File.WriteAllBytesAsync(Path.Combine(_directory, "fixture.bin"), _bytes);
        var descriptor = Descriptor();
        var catalog = new LocalModelCatalog([descriptor]);
        Assert.NotNull(await catalog.LocateAsync("fixture", "v1"));
        Assert.Null(await catalog.LocateAsync("fixture", "v2"));
        Assert.Single(await catalog.GetInstalledModelsAsync());
        var result = await catalog.ValidateAsync(descriptor);
        Assert.Equal(ModelIntegrity.Valid, result.Integrity);
        Assert.Equal(ModelAvailability.Available, result.Availability);
        Assert.Equal(ModelCompatibility.Unknown, result.Compatibility);
    }

    [Theory]
    [InlineData(true, "SizeMismatch")]
    [InlineData(false, "ChecksumMismatch")]
    public async Task CorruptionIsDetected(bool changeSize, string diagnostic)
    {
        await File.WriteAllBytesAsync(Path.Combine(_directory, "fixture.bin"), changeSize ? [9, 8] : [9, 8, 7, 6]);
        var result = await new LocalModelCatalog([Descriptor()]).ValidateAsync(Descriptor());
        Assert.Equal(ModelIntegrity.Invalid, result.Integrity);
        Assert.Equal(diagnostic, result.DiagnosticCode);
    }

    [Theory]
    [InlineData("../outside.bin")]
    [InlineData("nested/../../outside.bin")]
    public void PathsOutsideTheModelDirectoryAreRejected(string relativePath)
    {
        var descriptor = Descriptor() with { Files = [Descriptor().Files[0] with { RelativePath = relativePath }] };
        Assert.Throws<ArgumentException>(() => new LocalModelCatalog([descriptor]));
    }

    [Fact]
    public void DuplicateEntriesAndUnverifiableManifestsAreRejected()
    {
        Assert.Throws<ArgumentException>(() => new LocalModelCatalog([Descriptor(), Descriptor()]));
        Assert.Throws<ArgumentException>(() => new LocalModelCatalog([Descriptor() with { Files = [] }]));
        Assert.Throws<ArgumentException>(() => new LocalModelCatalog([Descriptor() with
            { Files = [new("fixture.bin", "not-a-hash", 4)] }]));
    }

    [Fact]
    public async Task CatalogOperationsHonorCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var catalog = new LocalModelCatalog([Descriptor()]);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => catalog.GetInstalledModelsAsync(cancellation.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => catalog.LocateAsync("fixture", "v1", cancellation.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => catalog.ValidateAsync(Descriptor(), cancellation.Token));
    }

    public void Dispose() => Directory.Delete(_directory, recursive: true);
}
