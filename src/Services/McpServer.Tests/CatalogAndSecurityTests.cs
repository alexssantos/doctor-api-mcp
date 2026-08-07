using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using McpApis.McpServer.Domain.Contracts;
using McpApis.McpServer.Domain.Models;
using McpApis.McpServer.Infrastructure.Options;
using McpApis.McpServer.Infrastructure.Security;
using McpApis.McpServer.Services;
using McpApis.McpServer.Services.Contracts;

namespace McpApis.McpServer.Tests;

public sealed class CatalogAndSecurityTests
{
    [Fact]
    public void Resolve_requires_namespace_when_alias_is_ambiguous()
    {
        var catalog = new ApplicationCatalog();
        catalog.ReplaceSnapshot(
            [App("orders", "team-a"), App("orders", "team-b")], TimeSpan.FromMinutes(5));

        var ambiguous = catalog.Resolve("orders");
        var resolved = catalog.Resolve("orders", "team-b");

        Assert.Equal(CatalogResolutionStatus.Ambiguous, ambiguous.Status);
        Assert.Equal(2, ambiguous.Candidates.Count);
        Assert.Equal(CatalogResolutionStatus.Resolved, resolved.Status);
        Assert.Equal("team-b", resolved.Application!.Namespace);
        Assert.Null(catalog.ResolveCanonicalName("orders"));
    }

    [Fact]
    public void Snapshot_preserves_toggle_per_namespace()
    {
        var catalog = new ApplicationCatalog();
        catalog.ReplaceSnapshot(
            [App("orders", "team-a"), App("orders", "team-b")], TimeSpan.FromMinutes(5));
        Assert.True(catalog.SetEnabled("orders", false, "team-a"));

        catalog.ReplaceSnapshot(
            [App("orders", "team-a"), App("orders", "team-b")], TimeSpan.FromMinutes(5));

        Assert.True(catalog.TryGet("orders", "team-a", out var teamA));
        Assert.True(catalog.TryGet("orders", "team-b", out var teamB));
        Assert.False(teamA.Enabled);
        Assert.True(teamB.Enabled);
    }

    [Fact]
    public void Identity_resolver_is_fail_closed_for_unknown_disabled_and_disallowed_services()
    {
        var catalog = new ApplicationCatalog();
        catalog.ReplaceSnapshot(
            [App("orders", "allowed") with { Enabled = false }, App("billing", "blocked")],
            TimeSpan.FromMinutes(5));
        var resolver = new ServiceIdentityResolver(
            catalog,
            Options.Create(new SecurityOptions { AllowedNamespaces = ["allowed"] }));

        Assert.Equal(ServiceResolutionStatus.Unknown, resolver.Resolve("missing").Status);
        Assert.Equal(ServiceResolutionStatus.Disabled, resolver.Resolve("orders", "allowed").Status);
        Assert.Equal(ServiceResolutionStatus.NamespaceNotAllowed,
            resolver.Resolve("billing", "blocked").Status);
    }

    [Theory]
    [InlineData("http://orders.allowed.svc.cluster.local", "allowed", true)]
    [InlineData("http://169.254.169.254/latest/meta-data", "allowed", false)]
    [InlineData("http://orders.blocked.svc.cluster.local", "allowed", false)]
    [InlineData("https://user:password@orders.allowed.svc.cluster.local", "allowed", false)]
    public void Url_policy_blocks_ssrf_targets(string value, string expectedNamespace, bool accepted)
    {
        var policy = new ServiceUrlPolicy(
            Options.Create(new SecurityOptions
            {
                AllowedNamespaces = ["allowed"],
                AllowedServiceHostSuffixes = [".svc.cluster.local"],
                AllowedServicePorts = [80, 443]
            }),
            new FakeEnvironment { EnvironmentName = Environments.Production });

        var actual = policy.TryValidate(value, expectedNamespace, out _, out _);

        Assert.Equal(accepted, actual);
    }

    private static DiscoveredApplication App(string name, string namespaceName) => new()
    {
        Name = name,
        Namespace = namespaceName,
        Sources = DiscoverySources.Deployment,
        DeploymentName = name,
        KubernetesServiceName = name,
        OtelServiceName = name,
        MetricsId = name,
        Selector = new Dictionary<string, string> { ["app"] = name },
        Coverage = new SignalCoverage(
            SourceAvailability.Available,
            SourceAvailability.Available,
            SourceAvailability.Available,
            SourceAvailability.Available,
            SourceAvailability.Available,
            SourceAvailability.Available),
        OpenApi = new OpenApiInfo(true, "/openapi/v1.json", []),
        Enabled = true,
        FirstSeen = DateTimeOffset.UtcNow,
        LastSeen = DateTimeOffset.UtcNow
    };

    private sealed class FakeEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "tests";
        public string ContentRootPath { get; set; } = Directory.GetCurrentDirectory();
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
