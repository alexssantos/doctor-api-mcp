using Microsoft.Extensions.Options;

namespace McpApis.McpServer.Infrastructure.Options;

public static class OptionsRegistration
{
    public static IServiceCollection AddObservabilityIntelligenceOptions(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        services.AddOptions<SecurityOptions>()
            .Bind(configuration.GetSection(SecurityOptions.SectionName))
            .ValidateDataAnnotations()
            .Validate(o => o.AllowedNamespaces.Length > 0,
                "Security:AllowedNamespaces must contain at least one namespace.")
            .Validate(o => environment.IsDevelopment() || o.Authentication.Enabled,
                "Authentication cannot be disabled outside Development.")
            .Validate(o => !o.Authentication.Enabled ||
                           (!string.IsNullOrWhiteSpace(o.Authentication.ReaderApiKey) &&
                            !string.IsNullOrWhiteSpace(o.Authentication.AdminApiKey)),
                "ReaderApiKey and AdminApiKey are required when authentication is enabled.")
            .Validate(o => !o.Authentication.Enabled ||
                           !string.Equals(o.Authentication.ReaderApiKey, o.Authentication.AdminApiKey,
                               StringComparison.Ordinal),
                "ReaderApiKey and AdminApiKey must be different.")
            .ValidateOnStart();

        services.AddOptions<ObservabilityLimitsOptions>()
            .Bind(configuration.GetSection(ObservabilityLimitsOptions.SectionName))
            .ValidateDataAnnotations()
            .Validate(o => o.MaxWindowMinutes >= o.DefaultWindowMinutes,
                "MaxWindowMinutes must be greater than or equal to DefaultWindowMinutes.")
            .ValidateOnStart();

        services.AddOptions<HealthEngineOptions>()
            .Bind(configuration.GetSection(HealthEngineOptions.SectionName))
            .ValidateDataAnnotations()
            .Validate(o => o.Weights.Count > 0 && o.Weights.Values.All(v => v > 0),
                "Health weights must be positive.")
            .Validate(o => o.HealthyScore > o.DegradedScore,
                "HealthyScore must be greater than DegradedScore.")
            .ValidateOnStart();

        services.AddOptions<MetricsTemplateOptions>()
            .Bind(configuration.GetSection(MetricsTemplateOptions.SectionName))
            .ValidateOnStart();

        services.AddOptions<ObservabilityCacheOptions>()
            .Bind(configuration.GetSection(ObservabilityCacheOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<ObservabilityFeatureOptions>()
            .Bind(configuration.GetSection(ObservabilityFeatureOptions.SectionName))
            .ValidateOnStart();

        return services;
    }
}
