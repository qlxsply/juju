using System.Xml.Linq;
using Xunit;

namespace Juju.Architecture.Tests;

public sealed class ProjectReferenceGraphTests
{
    private static readonly IReadOnlyDictionary<string, string[]> Baseline =
        new Dictionary<string, string[]>
        {
            ["Juju.Core"] = [],
            ["Juju.Tools.Json"] = ["Juju.Core"],
            ["Juju.Tools.Toto"] = ["Juju.Core"],
            ["Juju.Platform.Windows"] = ["Juju.Core"],
            ["Juju.App"] = ["Juju.Core", "Juju.Platform.Windows", "Juju.Tools.Json", "Juju.Tools.Toto"]
        };

    [Fact]
    public void Product_project_references_match_the_stage_zero_baseline()
    {
        var actual = ReadProductProjectGraph(FindRepositoryRoot());

        Assert.Equal(Baseline.Keys.Order(), actual.Keys.Order());
        foreach (var (project, expectedReferences) in Baseline)
            Assert.Equal(expectedReferences.Order(), actual[project].Order());
    }

    [Fact]
    public void Product_project_reference_graph_is_acyclic()
    {
        var graph = ReadProductProjectGraph(FindRepositoryRoot());
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var active = new HashSet<string>(StringComparer.Ordinal);

        bool Visit(string project)
        {
            if (active.Contains(project)) return false;
            if (!visited.Add(project)) return true;

            active.Add(project);
            foreach (var dependency in graph[project])
                if (!Visit(dependency)) return false;
            active.Remove(project);
            return true;
        }

        foreach (var project in graph.Keys)
            Assert.True(Visit(project), $"ProjectReference cycle includes {project}.");
    }

    [Fact]
    public void Architecture_test_project_does_not_reference_product_projects()
    {
        var project = Path.Combine(FindRepositoryRoot(), "tests", "Juju.Architecture.Tests",
            "Juju.Architecture.Tests.csproj");

        Assert.Empty(XDocument.Load(project).Descendants("ProjectReference"));
    }

    [Fact]
    public void Launcher_routes_card_and_global_hotkey_open_requests_through_tool_manager()
    {
        var source = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "src", "Juju.App", "Views",
            "LauncherWindow.xaml.cs"));

        Assert.Contains("_tools.Open(tool)", source, StringComparison.Ordinal);
        Assert.Contains("_tools.Open(ToolId.Json)", source, StringComparison.Ordinal);
        Assert.Contains("_tools.Open(ToolId.Toto)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ShowTool(", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Json_and_toto_tool_adapters_do_not_reference_each_other()
    {
        var toolsRoot = Path.Combine(FindRepositoryRoot(), "src", "Juju.App", "Tools");
        var json = File.ReadAllText(Path.Combine(toolsRoot, "Json", "JsonToolModule.cs"));
        var toto = File.ReadAllText(Path.Combine(toolsRoot, "Toto", "TotoToolModule.cs"));

        Assert.DoesNotContain("Juju.Tools.Toto", json, StringComparison.Ordinal);
        Assert.DoesNotContain("TotoToolModule", json, StringComparison.Ordinal);
        Assert.DoesNotContain("Juju.Tools.Json", toto, StringComparison.Ordinal);
        Assert.DoesNotContain("JsonToolModule", toto, StringComparison.Ordinal);
    }

    [Fact]
    public void App_does_not_reference_toto_repository_or_infrastructure_types()
    {
        var appRoot = Path.Combine(FindRepositoryRoot(), "src", "Juju.App");
        var source = string.Join('\n', Directory.GetFiles(appRoot, "*.cs", SearchOption.AllDirectories)
            .Select(File.ReadAllText));

        foreach (var forbidden in new[]
                 {
                     "ItemRepository", "SettingsRepository", "WorkCalendarRepository",
                     "HolidayCalendarDownloadService", "TotoPaths", "_host.Items.", "toto.Items.",
                     "_toto.Items."
                 })
            Assert.DoesNotContain(forbidden, source, StringComparison.Ordinal);
    }

    private static Dictionary<string, string[]> ReadProductProjectGraph(string repositoryRoot)
    {
        var sourceRoot = Path.Combine(repositoryRoot, "src");
        var projectFiles = Directory.GetFiles(sourceRoot, "*.csproj", SearchOption.AllDirectories);
        var knownProjects = projectFiles.ToDictionary(ProjectName,
            path => Path.GetFullPath(path), StringComparer.Ordinal);

        return projectFiles.ToDictionary(
            ProjectName,
            path => XDocument.Load(path)
                .Descendants("ProjectReference")
                .Select(reference =>
                {
                    var include = (string?)reference.Attribute("Include")
                        ?? throw new InvalidOperationException($"ProjectReference without Include in {path}.");
                    var targetPath = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(path)!, include));
                    var targetName = ProjectName(targetPath);
                    Assert.True(knownProjects.TryGetValue(targetName, out var knownPath),
                        $"{ProjectName(path)} references a non-product project: {include}");
                    Assert.Equal(knownPath, targetPath);
                    return targetName;
                })
                .Order(StringComparer.Ordinal)
                .ToArray(),
            StringComparer.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null;
             directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "Juju.sln")))
                return directory.FullName;

        throw new DirectoryNotFoundException("Could not find Juju.sln above the test output directory.");
    }

    private static string ProjectName(string path) =>
        Path.GetFileNameWithoutExtension(path)
        ?? throw new InvalidOperationException($"Could not determine project name from {path}.");
}
