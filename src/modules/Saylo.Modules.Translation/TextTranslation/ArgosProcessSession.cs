using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace Saylo.Modules.Translation.TextTranslation;

internal interface IArgosSession : IAsyncDisposable
{
    int ProcessId { get; }
    long WorkingSetBytes { get; }
    Task<string> TranslateAsync(string id, string text, string source, string target, CancellationToken token);
}

internal interface IArgosCandidateSession : IArgosSession
{
    bool SupportsRankedCandidates { get; }
    Task<IReadOnlyList<string>> TranslateCandidatesAsync(string id, string text, string source, string target, CancellationToken token);
}

internal sealed class ArgosProcessSession : IArgosCandidateSession
{
    private readonly Process _process;
    private readonly Task _stderrDrain;
    private int _workerProcessId;
    public bool SupportsRankedCandidates { get; private set; }

    private ArgosProcessSession(Process process)
    {
        _process = process;
        _stderrDrain = process.StandardError.BaseStream.CopyToAsync(Stream.Null);
    }

    public int ProcessId => _workerProcessId;
    public long WorkingSetBytes
    {
        get
        {
            using var worker = Process.GetProcessById(_workerProcessId);
            return worker.HasExited ? 0 : worker.WorkingSet64;
        }
    }

    internal static async Task<IArgosSession> StartAsync(ArgosTranslationOptions options, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!File.Exists(options.PythonExecutablePath) || !File.Exists(options.WorkerScriptPath))
            throw new FileNotFoundException("The configured bundled Python executable or worker script is missing.");
        var start = new ProcessStartInfo(options.PythonExecutablePath)
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
            StandardInputEncoding = new UTF8Encoding(false), StandardOutputEncoding = Encoding.UTF8,
            WorkingDirectory = Path.GetDirectoryName(options.WorkerScriptPath)!
        };
        foreach (var argument in new[] { "-I", "-B", "-u", options.WorkerScriptPath, "--models", options.ModelDirectory,
            "--data", options.RuntimeDataDirectory, "--threads", options.Threads.ToString(System.Globalization.CultureInfo.InvariantCulture) })
            start.ArgumentList.Add(argument);
        var process = new Process { StartInfo = start };
        try
        {
            if (!process.Start())
                throw new InvalidOperationException("The local Python process could not start.");
        }
        catch { process.Dispose(); throw; }
        var session = new ArgosProcessSession(process);
        try
        {
            var ready = await process.StandardOutput.ReadLineAsync(token).ConfigureAwait(false);
            ArgosProtocol.ParseResponse(ready, "ready", ready: true);
            using var document = JsonDocument.Parse(ready!);
            session._workerProcessId = document.RootElement.GetProperty("pid").GetInt32();
            session.SupportsRankedCandidates = document.RootElement.TryGetProperty("capabilities", out var capabilities) &&
                capabilities.ValueKind == JsonValueKind.Array && capabilities.EnumerateArray().Any(value =>
                    value.ValueKind == JsonValueKind.String && value.GetString() == "ranked_candidates_v1");
            if (session._workerProcessId <= 0)
                throw new InvalidDataException("Invalid worker process ID.");
            return session;
        }
        catch { await session.DisposeAsync().ConfigureAwait(false); throw; }
    }

    public async Task<string> TranslateAsync(string id, string text, string source, string target, CancellationToken token)
    {
        var request = JsonSerializer.Serialize(new { protocol = 1, id, text, source, target });
        await _process.StandardInput.WriteLineAsync(request.AsMemory(), token).ConfigureAwait(false);
        await _process.StandardInput.FlushAsync(token).ConfigureAwait(false);
        return ArgosProtocol.ParseResponse(await _process.StandardOutput.ReadLineAsync(token).ConfigureAwait(false), id);
    }

    public async Task<IReadOnlyList<string>> TranslateCandidatesAsync(string id, string text, string source, string target, CancellationToken token)
    {
        if (!SupportsRankedCandidates)
            throw new InvalidOperationException("The worker does not support ranked candidates.");
        var request = JsonSerializer.Serialize(new { protocol = 1, id, text, source, target, candidates = 4 });
        await _process.StandardInput.WriteLineAsync(request.AsMemory(), token).ConfigureAwait(false);
        await _process.StandardInput.FlushAsync(token).ConfigureAwait(false);
        return ArgosProtocol.ParseCandidates(await _process.StandardOutput.ReadLineAsync(token).ConfigureAwait(false), id);
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            try { _process.StandardInput.Close(); }
            catch (IOException) { }
            try { await _process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(3)).ConfigureAwait(false); }
            catch (TimeoutException)
            {
                try { _process.Kill(entireProcessTree: true); }
                catch (InvalidOperationException) when (_process.HasExited) { }
                await _process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            }
            await _stderrDrain.ConfigureAwait(false);
        }
        finally { _process.Dispose(); }
    }
}
