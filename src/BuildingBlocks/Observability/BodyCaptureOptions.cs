namespace McpApis.BuildingBlocks.Observability;

public interface IBodyCaptureOptions
{
    bool Enabled { get; }
    int MaxBodyBytes { get; }
    IReadOnlySet<string> AllowedContentTypes { get; }
    IReadOnlySet<string> SensitiveFields { get; }
}

public class BodyCaptureOptions : IBodyCaptureOptions
{
    public bool Enabled { get; set; }
    public int MaxBodyBytes { get; set; } = 16_384;
    public IReadOnlySet<string> AllowedContentTypes { get; set; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "application/json",
        "application/problem+json"
    };
    public IReadOnlySet<string> SensitiveFields { get; set; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "password", "secret", "token", "access_token", "refresh_token", "apiKey", "api_key",
        "authorization", "cookie", "set-cookie", "connectionString"
    };
}
