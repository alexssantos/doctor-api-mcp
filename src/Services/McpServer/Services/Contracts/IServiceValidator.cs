namespace McpApis.McpServer.Services.Contracts;

public interface IServiceValidator
{
    Task<ServiceValidationResult> ValidateAsync(
        string serviceName,
        string baseUrl,
        string? namespaceName = null,
        CancellationToken cancellationToken = default);
}

public record ServiceValidationResult(
    string ServiceName,
    string BaseUrl,
    string OpenApiPath,
    bool IsValid,
    IReadOnlyList<string> Failures);
