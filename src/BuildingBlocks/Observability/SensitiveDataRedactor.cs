using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace McpApis.BuildingBlocks.Observability;

public static partial class SensitiveDataRedactor
{
    private const string Redacted = "[REDACTED]";

    public static string Redact(string input, IReadOnlySet<string> sensitiveFields)
    {
        if (string.IsNullOrWhiteSpace(input))
            return input;

        try
        {
            var node = JsonNode.Parse(input);
            RedactNode(node, sensitiveFields);
            return node?.ToJsonString(new JsonSerializerOptions { WriteIndented = false }) ?? string.Empty;
        }
        catch (JsonException)
        {
            return RedactPatterns(input);
        }
    }

    private static void RedactNode(JsonNode? node, IReadOnlySet<string> sensitiveFields)
    {
        if (node is JsonObject obj)
        {
            foreach (var property in obj.ToList())
            {
                if (sensitiveFields.Contains(property.Key))
                    obj[property.Key] = Redacted;
                else if (property.Value is JsonValue value &&
                         value.TryGetValue<string>(out var text))
                    obj[property.Key] = RedactPatterns(text);
                else
                    RedactNode(property.Value, sensitiveFields);
            }
        }
        else if (node is JsonArray array)
        {
            for (var index = 0; index < array.Count; index++)
            {
                if (array[index] is JsonValue value &&
                    value.TryGetValue<string>(out var text))
                    array[index] = RedactPatterns(text);
                else
                    RedactNode(array[index], sensitiveFields);
            }
        }
    }

    private static string RedactPatterns(string value)
    {
        value = BearerTokenRegex().Replace(value, "$1[REDACTED]");
        value = ConnectionStringPasswordRegex().Replace(value, "$1[REDACTED]");
        return EmailRegex().Replace(value, "[REDACTED_EMAIL]");
    }

    [GeneratedRegex("(?i)(bearer\\s+)[A-Za-z0-9._~+\\-/]+=*")]
    private static partial Regex BearerTokenRegex();

    [GeneratedRegex("(?i)(password|pwd)\\s*=\\s*[^;\\s]+")]
    private static partial Regex ConnectionStringPasswordRegex();

    [GeneratedRegex("(?i)\\b[A-Z0-9._%+-]+@[A-Z0-9.-]+\\.[A-Z]{2,}\\b")]
    private static partial Regex EmailRegex();
}
