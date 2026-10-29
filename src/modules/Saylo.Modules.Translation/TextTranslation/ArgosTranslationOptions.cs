namespace Saylo.Modules.Translation.TextTranslation;

public sealed record ArgosTranslationOptions
{
    public string PythonExecutablePath { get; }
    public string ModelDirectory { get; }
    public string WorkerScriptPath { get; }
    public string RuntimeDataDirectory { get; }
    public TimeSpan StartupTimeout { get; }
    public TimeSpan TranslationTimeout { get; }
    public int Threads { get; }
    public TranslationIntegrityOptions Integrity { get; }
    public const int MaximumTextLength = 1000;

    public ArgosTranslationOptions(string pythonExecutablePath, string modelDirectory,
        string? workerScriptPath = null, string? runtimeDataDirectory = null,
        TimeSpan? startupTimeout = null, TimeSpan? translationTimeout = null, int threads = 4,
        TranslationIntegrityOptions? integrity = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pythonExecutablePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(modelDirectory);
        if (!Path.IsPathFullyQualified(pythonExecutablePath))
            throw new ArgumentException("Configure the bundled Python executable explicitly; PATH lookup is not supported.", nameof(pythonExecutablePath));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(threads);
        PythonExecutablePath = Path.GetFullPath(pythonExecutablePath);
        ModelDirectory = Path.GetFullPath(modelDirectory);
        WorkerScriptPath = Path.GetFullPath(workerScriptPath ?? Path.Combine(AppContext.BaseDirectory, "TextTranslation", "argos_worker.py"));
        RuntimeDataDirectory = Path.GetFullPath(runtimeDataDirectory ??
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Saylo", "Argos"));
        StartupTimeout = startupTimeout ?? TimeSpan.FromSeconds(60);
        TranslationTimeout = translationTimeout ?? TimeSpan.FromSeconds(30);
        if (StartupTimeout <= TimeSpan.Zero || StartupTimeout.TotalMilliseconds > int.MaxValue
            || TranslationTimeout <= TimeSpan.Zero || TranslationTimeout.TotalMilliseconds > int.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(startupTimeout), "Timeouts must be positive and bounded.");
        Threads = threads;
        Integrity = integrity ?? new TranslationIntegrityOptions();
    }
}
