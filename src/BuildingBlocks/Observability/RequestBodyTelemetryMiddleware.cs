using System.Diagnostics;
using Microsoft.AspNetCore.Http;

namespace McpApis.BuildingBlocks.Observability;

public class RequestBodyTelemetryMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IBodyCaptureOptions _options;

    public RequestBodyTelemetryMiddleware(RequestDelegate next, IBodyCaptureOptions options)
    {
        _next = next;
        _options = options;
    }

    public async Task Invoke(HttpContext context)
    {
        if (!CanCapture(context.Request.ContentType))
        {
            await _next(context);
            return;
        }

        if (context.Request.ContentLength > _options.MaxBodyBytes)
        {
            Activity.Current?.SetTag("http.request.body.capture", "skipped_too_large");
            await _next(context);
            return;
        }

        context.Request.EnableBuffering(bufferThreshold: _options.MaxBodyBytes, bufferLimit: _options.MaxBodyBytes);

        var body = await new StreamReader(context.Request.Body, leaveOpen: true)
            .ReadToEndAsync(context.RequestAborted);
        context.Request.Body.Position = 0;

        var activity = Activity.Current;
        if (activity != null && !string.IsNullOrEmpty(body))
        {
            activity.SetTag("http.request.body", SensitiveDataRedactor.Redact(body, _options.SensitiveFields));
        }

        await _next(context);
    }

    private bool CanCapture(string? contentType)
    {
        var mediaType = contentType?.Split(';', 2)[0].Trim();
        return mediaType is not null && _options.AllowedContentTypes.Contains(mediaType);
    }
}
