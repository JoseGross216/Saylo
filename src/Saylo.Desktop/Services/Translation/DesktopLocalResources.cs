using System;
using System.IO;

namespace Saylo.Desktop.Services.Translation;

internal sealed record DesktopLocalResources(string? Python, string? ArgosModels, string? WhisperModels, string? CmuDictionary)
{
    public static DesktopLocalResources Resolve(string applicationDirectory, Func<string, string?> readEnvironment)
    {
        var root = Path.Combine(Path.GetFullPath(applicationDirectory), "resources");
        var bundled = Directory.Exists(root);
        if (!bundled)
        {
            // Resolve development assets from the checkout containing the built executable.
            for (var directory = new DirectoryInfo(applicationDirectory); directory is not null; directory = directory.Parent)
            {
                if (File.Exists(Path.Combine(directory.FullName, "Saylo.slnx")) &&
                    File.Exists(Path.Combine(directory.FullName, "src", "Saylo.Desktop", "Saylo.Desktop.csproj")))
                {
                    root = Path.Combine(directory.FullName, "artifacts");
                    break;
                }
            }
        }

        var argos = Path.Combine(root, "argos");
        var runtime = Path.Combine(argos, "runtime");
        var python = bundled ? Path.Combine(runtime, "python.exe") : OperatingSystem.IsWindows()
            ? Path.Combine(runtime, "Scripts", "python.exe") : Path.Combine(runtime, "bin", "python");
        var models = Path.Combine(argos, "models");
        var hasArgos = File.Exists(python) || Directory.Exists(models);

        return new(
            Override("SAYLO_ARGOS_PYTHON", hasArgos ? python : null),
            Override("SAYLO_ARGOS_MODELS", hasArgos ? models : null),
            Override("SAYLO_WHISPER_MODEL_DIR", InstalledDirectory("whisper", "models")),
            Override("SAYLO_CMUDICT_DIR", InstalledDirectory("pronunciation", "cmudict")));

        string? InstalledDirectory(string component, string name)
        {
            var path = Path.Combine(root, component, name);
            return Directory.Exists(path) ? path : null;
        }

        string? Override(string key, string? installedPath)
        {
            var value = readEnvironment(key);
            return string.IsNullOrWhiteSpace(value) ? installedPath : value;
        }
    }
}
