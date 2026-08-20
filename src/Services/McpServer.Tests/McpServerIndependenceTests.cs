using System.Text.Json;
using System.Xml.Linq;

namespace McpApis.McpServer.Tests;

public sealed class McpServerIndependenceTests
{
    private static readonly string[] ExampleApiNames = ["PrecoAPI", "ProdutoAPI"];

    [Fact]
    public void Project_references_do_not_include_example_apis()
    {
        var project = XDocument.Load(RepositoryPath(
            "src", "Services", "McpServer", "McpApis.McpServer.csproj"));

        var references = project
            .Descendants("ProjectReference")
            .Select(element => element.Attribute("Include")?.Value ?? string.Empty)
            .ToArray();

        Assert.NotEmpty(references);
        Assert.All(references, reference => Assert.False(
            ContainsExampleApi(reference),
            $"McpServer must not reference an example API project: {reference}"));
    }

    [Fact]
    public void Isolated_solution_contains_only_the_mcpserver_build_and_test_graph()
    {
        var solution = File.ReadAllText(RepositoryPath("src", "McpServer.slnx"));

        Assert.Contains("McpApis.McpServer.csproj", solution);
        Assert.Contains("McpApis.McpServer.Tests.csproj", solution);
        Assert.False(ContainsExampleApi(solution),
            "The isolated McpServer solution must not include example API projects.");
    }

    [Fact]
    public void Container_build_does_not_copy_example_projects_or_the_integration_solution()
    {
        var dockerfile = File.ReadAllText(RepositoryPath(
            "src", "Services", "McpServer", "Dockerfile"));

        Assert.False(ContainsExampleApi(dockerfile),
            "The McpServer image must not depend on example API build inputs.");
        Assert.DoesNotContain("mcp-apis.slnx", dockerfile, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("BuildingBlocks/Http", dockerfile, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("COPY src/BuildingBlocks/Observability/", dockerfile);
    }

    [Fact]
    public void Default_configuration_does_not_pre_register_example_services()
    {
        using var configuration = JsonDocument.Parse(File.ReadAllText(RepositoryPath(
            "src", "Services", "McpServer", "appsettings.json")));

        var services = configuration.RootElement.GetProperty("Services");

        Assert.Equal(JsonValueKind.Object, services.ValueKind);
        Assert.Empty(services.EnumerateObject());
    }

    private static bool ContainsExampleApi(string value) =>
        ExampleApiNames.Any(name => value.Contains(name, StringComparison.OrdinalIgnoreCase));

    private static string RepositoryPath(params string[] segments) =>
        Path.Combine([FindRepositoryRoot(), .. segments]);

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "src", "mcp-apis.slnx")))
                return directory.FullName;
        }

        throw new DirectoryNotFoundException(
            $"Could not locate the repository root from '{AppContext.BaseDirectory}'.");
    }
}
