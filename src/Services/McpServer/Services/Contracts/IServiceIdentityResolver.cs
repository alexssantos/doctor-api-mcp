using McpApis.McpServer.Domain.Contracts;

namespace McpApis.McpServer.Services.Contracts;

public enum ServiceResolutionStatus
{
    Resolved,
    Unknown,
    Ambiguous,
    Disabled,
    NamespaceNotAllowed,
    NamespaceRequired
}

public sealed record ServiceResolution(
    ServiceResolutionStatus Status,
    ServiceIdentity? Identity,
    DiscoveredApplication? Application,
    IReadOnlyList<string> Candidates,
    string? Message)
{
    public bool IsResolved => Status == ServiceResolutionStatus.Resolved;
}

public interface IServiceIdentityResolver
{
    ServiceResolution Resolve(
        string serviceName,
        string? namespaceName = null,
        bool requireEnabled = true);
}
