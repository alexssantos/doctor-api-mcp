using Microsoft.Extensions.Options;
using McpApis.McpServer.Domain.Contracts;
using McpApis.McpServer.Infrastructure.Options;
using McpApis.McpServer.Services.Contracts;

namespace McpApis.McpServer.Services;

public sealed class ServiceIdentityResolver(
    IApplicationCatalog catalog,
    IOptions<SecurityOptions> security) : IServiceIdentityResolver
{
    public ServiceResolution Resolve(
        string serviceName,
        string? namespaceName = null,
        bool requireEnabled = true)
    {
        var resolution = catalog.Resolve(serviceName, namespaceName);
        if (resolution.Status == CatalogResolutionStatus.Unknown)
            return new ServiceResolution(
                ServiceResolutionStatus.Unknown, null, null, [],
                $"Unknown service '{serviceName}'.");

        if (resolution.Status == CatalogResolutionStatus.Ambiguous)
        {
            var candidates = resolution.Candidates.Select(FormatCandidate).ToArray();
            return new ServiceResolution(
                ServiceResolutionStatus.Ambiguous, null, null, candidates,
                $"Service '{serviceName}' exists in multiple namespaces; namespace is required.");
        }

        var app = resolution.Application!;
        if (string.IsNullOrWhiteSpace(app.Namespace))
            return new ServiceResolution(
                ServiceResolutionStatus.NamespaceRequired, null, app, [],
                $"Service '{app.Name}' has no namespace identity and is not queryable by vNext tools.");

        if (!security.Value.AllowedNamespaces.Contains(app.Namespace, StringComparer.OrdinalIgnoreCase))
            return new ServiceResolution(
                ServiceResolutionStatus.NamespaceNotAllowed, null, app, [],
                $"Namespace '{app.Namespace}' is outside the configured allowlist.");

        if (requireEnabled && !app.Enabled)
            return new ServiceResolution(
                ServiceResolutionStatus.Disabled, null, app, [],
                $"Service '{FormatCandidate(app)}' is disabled for observability indexing.");

        var aliases = new[]
            {
                app.Name, app.DeploymentName, app.KubernetesServiceName,
                app.OtelServiceName, app.MetricsId
            }
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var identity = new ServiceIdentity(
            app.Name,
            app.Namespace,
            app.DeploymentName,
            app.KubernetesServiceName,
            app.OtelServiceName,
            app.MetricsId ?? app.Name,
            aliases);
        return new ServiceResolution(
            ServiceResolutionStatus.Resolved, identity, app, [FormatCandidate(app)], null);
    }

    private static string FormatCandidate(DiscoveredApplication app) =>
        $"{app.Namespace ?? "unknown"}/{app.Name}";
}
