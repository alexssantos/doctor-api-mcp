using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace McpApis.BuildingBlocks.Observability;

public static class ObservabilityExtensions
{
    public static IServiceCollection AddObservability(
        this IServiceCollection services,
        string serviceName,
        IConfiguration configuration)
    {
        var otlpEndpoint = configuration["Otel:Endpoint"] ?? "http://localhost:4317";
        var captureBody = configuration.GetValue<bool>("Otel:CaptureBody");

        services.AddOpenApiTelemetry(serviceName, otlpEndpoint, captureBody);

        services.AddMetricsTelemetry(serviceName);

        services.AddSingleton<IBodyCaptureOptions>(new BodyCaptureOptions
        {
            Enabled = captureBody,
            MaxBodyBytes = Math.Clamp(configuration.GetValue("Otel:MaxCapturedBodyBytes", 16_384), 1024, 1_048_576),
            AllowedContentTypes = new HashSet<string>(
                configuration.GetSection("Otel:AllowedBodyContentTypes").Get<string[]>() ??
                ["application/json", "application/problem+json"],
                StringComparer.OrdinalIgnoreCase),
            SensitiveFields = new HashSet<string>(
                configuration.GetSection("Otel:SensitiveFields").Get<string[]>() ??
                ["password", "secret", "token", "access_token", "refresh_token", "apiKey", "api_key",
                 "authorization", "cookie", "set-cookie", "connectionString"],
                StringComparer.OrdinalIgnoreCase)
        });

        return services;
    }

    private static IServiceCollection AddOpenApiTelemetry(
        this IServiceCollection services,
        string serviceName,
        string otlpEndpoint,
        bool captureSensitiveData)
    {
        services.AddOpenTelemetry()
            .WithTracing(tracing =>
            {
                tracing
                    .SetResourceBuilder(ResourceBuilder.CreateDefault().AddService(serviceName))
                    .AddSource("McpApis.ObservabilityIntelligence")
                    .AddAspNetCoreInstrumentation(opts =>
                    {
                        opts.RecordException = true;
                    })
                    .AddHttpClientInstrumentation(opts =>
                    {
                        opts.RecordException = true;
                    })
                    .AddEntityFrameworkCoreInstrumentation(opts =>
                    {
                        // SQL statement text (and its parameter values) is only recorded
                        // when explicit body/sensitive-data capture is enabled (Otel:CaptureBody),
                        // to avoid leaking query data (potential PII) into Jaeger by default.
                        opts.SetDbStatementForText = captureSensitiveData;
                    })
                    .AddOtlpExporter(opts =>
                    {
                        opts.Endpoint = new Uri(otlpEndpoint);
                    });
            });

        return services;
    }

    private static IServiceCollection AddMetricsTelemetry(
        this IServiceCollection services,
        string serviceName)
    {
        services.AddOpenTelemetry()
            .WithMetrics(metrics =>
            {
                metrics
                    .SetResourceBuilder(ResourceBuilder.CreateDefault().AddService(serviceName))
                    .AddMeter("McpApis.ObservabilityIntelligence")
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddRuntimeInstrumentation()
                    .AddPrometheusExporter();
            });

        return services;
    }

    public static IApplicationBuilder UseBodyCaptureTelemetry(this IApplicationBuilder app)
    {
        var options = app.ApplicationServices.GetRequiredService<IBodyCaptureOptions>();
        if (options.Enabled)
        {
            app.UseMiddleware<RequestBodyTelemetryMiddleware>();
            app.UseMiddleware<ResponseBodyTelemetryMiddleware>();
        }
        return app;
    }
}
