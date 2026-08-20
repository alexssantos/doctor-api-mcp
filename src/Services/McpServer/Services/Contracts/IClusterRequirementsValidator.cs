using McpApis.McpServer.Infrastructure.Options;

namespace McpApis.McpServer.Services.Contracts;

public sealed record ClusterRequirementCheck(
    string Name,
    bool Required,
    bool Satisfied,
    string Detail);

public sealed record ClusterRequirementsReport(
    string Mode,
    ClusterAccessScope Scope,
    bool ServiceDiscovery,
    ClusterStateStorage StateStorage,
    bool VolumesAllowed,
    bool MeetsMinimumRequirements,
    DateTimeOffset CheckedAt,
    IReadOnlyList<ClusterRequirementCheck> Checks)
{
    public IReadOnlyList<string> MissingRequirements => Checks
        .Where(check => check.Required && !check.Satisfied)
        .Select(check => check.Name)
        .ToArray();
}

public interface IClusterRequirementsValidator
{
    Task<ClusterRequirementsReport> ValidateAsync(
        bool forceRefresh = false,
        CancellationToken cancellationToken = default);
}
