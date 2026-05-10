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

        services.AddOpenApiTelemetry(serviceName, otlpEndpoint);

        services.AddMetricsTelemetry(serviceName);

        if (captureBody)
        {
            services.AddSingleton<IBodyCaptureOptions>(new BodyCaptureOptions { Enabled = true });
        }
        else
        {
            services.AddSingleton<IBodyCaptureOptions>(new BodyCaptureOptions { Enabled = false });
        }

        return services;
    }

    private static IServiceCollection AddOpenApiTelemetry(
        this IServiceCollection services,
        string serviceName,
        string otlpEndpoint)
    {
        services.AddOpenTelemetry()
            .WithTracing(tracing =>
            {
                tracing
                    .SetResourceBuilder(ResourceBuilder.CreateDefault().AddService(serviceName))
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
                        opts.SetDbStatementForText = true;
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
