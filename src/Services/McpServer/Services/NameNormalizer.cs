namespace McpApis.McpServer.Services;

/// <summary>
/// Normalizes application identifiers so the same app can be correlated across
/// naming conventions: K8s objects use kebab-case ("catalog-api"), OTel service
/// names often use PascalCase ("CatalogAPI").
/// </summary>
public static class NameNormalizer
{
    /// <summary>Lowercases and strips every character outside [a-z0-9]: "CatalogAPI" → "catalogapi".</summary>
    public static string Normalize(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "";

        Span<char> buffer = stackalloc char[name.Length];
        var length = 0;
        foreach (var c in name)
        {
            var lower = char.ToLowerInvariant(c);
            if (lower is >= 'a' and <= 'z' or >= '0' and <= '9')
                buffer[length++] = lower;
        }
        return new string(buffer[..length]);
    }
}
