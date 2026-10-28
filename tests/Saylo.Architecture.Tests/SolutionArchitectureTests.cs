using System.Xml.Linq;

namespace Saylo.Architecture.Tests;

public sealed class SolutionArchitectureTests
{
    private static readonly StringComparer PathComparer = StringComparer.OrdinalIgnoreCase;
    private static readonly string Root = FindSolutionRoot();

    [Fact]
    public void SolutionRegistersAllSourceAndTestProjectsExactlyOnceInTheirFolders()
    {
        var projects = ReadProjects();
        var solution = XDocument.Load(Path.Combine(Root, "Saylo.slnx"));
        var entries = solution.Descendants("Project").ToArray();
        var registered = entries.Select(entry => Resolve(Root, entry.Attribute("Path")!.Value)).ToArray();

        Assert.Equal(registered.Length, registered.Distinct(PathComparer).Count());
        Assert.True(projects.Keys.ToHashSet(PathComparer).SetEquals(registered),
            "The solution must register every C# project under src and tests, with no extra projects.");

        foreach (var entry in entries)
        {
            var relative = entry.Attribute("Path")!.Value.Replace('\\', '/');
            var folder = relative.StartsWith("src/modules/", StringComparison.Ordinal)
                ? "/src/modules/"
                : relative.StartsWith("src/", StringComparison.Ordinal) ? "/src/" : "/tests/";
            Assert.Equal(folder, entry.Parent!.Attribute("Name")?.Value);
        }

        Assert.Contains(Resolve(Root, "src/Saylo.Desktop/Saylo.Desktop.csproj"), registered, PathComparer);
        Assert.Contains(Resolve(Root, "src/modules/Saylo.Modules.ModelManagement/Saylo.Modules.ModelManagement.csproj"),
            registered, PathComparer);
    }

    [Fact]
    public void AllProjectsTargetNet8()
    {
        foreach (var project in ReadProjects())
        {
            Assert.Equal("net8.0", project.Value.Descendants("TargetFramework").Single().Value);
            Assert.Empty(project.Value.Descendants("TargetFrameworks"));
        }
    }

    [Fact]
    public void ProjectReferencesResolveAndHaveNoCycles()
    {
        var projects = ReadProjects();
        var completed = new HashSet<string>(PathComparer);
        var visiting = new HashSet<string>(PathComparer);

        void Visit(string path)
        {
            Assert.True(projects.ContainsKey(path), $"Unregistered project reference: {path}");
            if (completed.Contains(path))
                return;

            Assert.True(visiting.Add(path), $"Circular project dependency: {path}");
            foreach (var reference in References(path, projects[path]))
                Visit(reference);

            visiting.Remove(path);
            completed.Add(path);
        }

        foreach (var path in projects.Keys)
            Visit(path);
    }

    [Fact]
    public void ModulesDoNotDependOnDesktopApiOrUiPackages()
    {
        var projects = ReadProjects();
        foreach (var path in projects.Keys.Where(path => IsModule(path)))
        {
            foreach (var dependency in DependencyClosure(path, projects))
            {
                Assert.True(IsModule(dependency), $"Module depends on a host or test project: {dependency}");
                foreach (var package in projects[dependency].Descendants("PackageReference"))
                {
                    var name = package.Attribute("Include")?.Value ?? package.Attribute("Update")?.Value ?? "";
                    Assert.False(name.StartsWith("Avalonia", StringComparison.OrdinalIgnoreCase)
                        || name.StartsWith("CommunityToolkit.Mvvm", StringComparison.OrdinalIgnoreCase),
                        $"UI package in module dependency graph: {name}");
                }
            }
        }
    }

    [Theory]
    [InlineData("Saylo.Modules.Translation.Contracts")]
    [InlineData("Saylo.Modules.ModelManagement")]
    public void ContractModulesHaveNoProjectPackageOrAssemblyDependencies(string name)
    {
        var project = XDocument.Load(Path.Combine(Root, "src", "modules", name, name + ".csproj"));
        Assert.Empty(project.Descendants("ProjectReference"));
        Assert.Empty(project.Descendants("PackageReference"));
        Assert.Empty(project.Descendants("Reference"));
        Assert.Empty(project.Descendants("FrameworkReference"));
    }

    [Fact]
    public void TranslationReferencesItsContracts()
    {
        var projects = ReadProjects();
        var path = Resolve(Root, "src/modules/Saylo.Modules.Translation/Saylo.Modules.Translation.csproj");
        var contracts = Resolve(Root,
            "src/modules/Saylo.Modules.Translation.Contracts/Saylo.Modules.Translation.Contracts.csproj");
        Assert.Contains(contracts, References(path, projects[path]), PathComparer);
    }

    [Fact]
    public void ConversationsDoesNotDependOnTranslationImplementationOrModelManagement()
    {
        var projects = ReadProjects();
        var path = Resolve(Root, "src/modules/Saylo.Modules.Conversations/Saylo.Modules.Conversations.csproj");
        var allowed = new HashSet<string>(PathComparer)
        {
            path,
            Resolve(Root, "src/modules/Saylo.Modules.Translation.Contracts/Saylo.Modules.Translation.Contracts.csproj")
        };

        Assert.All(DependencyClosure(path, projects), dependency =>
            Assert.True(allowed.Contains(dependency), $"Conversations has an implementation dependency: {dependency}"));
    }

    private static Dictionary<string, XDocument> ReadProjects() =>
        new[] { "src", "tests" }
            .SelectMany(folder => Directory.EnumerateFiles(Path.Combine(Root, folder), "*.csproj", SearchOption.AllDirectories))
            .Where(path => !Path.GetRelativePath(Root, path).Split(Path.DirectorySeparatorChar)
                .Any(segment => PathComparer.Equals(segment, "bin") || PathComparer.Equals(segment, "obj")))
            .ToDictionary(Path.GetFullPath, path => XDocument.Load(path), PathComparer);

    private static IEnumerable<string> References(string path, XDocument project) =>
        project.Descendants("ProjectReference")
            .Select(reference => Resolve(Path.GetDirectoryName(path)!, reference.Attribute("Include")!.Value));

    private static IEnumerable<string> DependencyClosure(string start, Dictionary<string, XDocument> projects)
    {
        var pending = new Stack<string>();
        var visited = new HashSet<string>(PathComparer);
        pending.Push(start);
        while (pending.TryPop(out var path))
        {
            if (!visited.Add(path))
                continue;

            Assert.True(projects.ContainsKey(path), $"Unknown project reference: {path}");
            yield return path;
            foreach (var reference in References(path, projects[path]))
                pending.Push(reference);
        }
    }

    private static bool IsModule(string path) =>
        Path.GetRelativePath(Root, path).Replace('\\', '/').StartsWith("src/modules/", StringComparison.Ordinal);

    private static string Resolve(string directory, string path) =>
        Path.GetFullPath(Path.Combine(directory, path.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar)));

    private static string FindSolutionRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory != null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Saylo.slnx")))
                return directory.FullName;
        }

        throw new DirectoryNotFoundException("Saylo.slnx was not found above the test output directory.");
    }
}
