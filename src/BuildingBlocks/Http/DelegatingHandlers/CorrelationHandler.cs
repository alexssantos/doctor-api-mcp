using System.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace McpApis.BuildingBlocks.Http.DelegatingHandlers;

/// <summary>
/// Propagates a correlation ID (X-Correlation-Id) across outgoing HTTP requests
/// and logs the outgoing call with its status.
/// </summary>
public class CorrelationHandler : DelegatingHandler
{
    private const string CorrelationHeader = "X-Correlation-Id";
    private readonly ILogger<CorrelationHandler> _logger;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CorrelationHandler(
        ILogger<CorrelationHandler> logger,
        IHttpContextAccessor httpContextAccessor)
    {
        _logger = logger;
        _httpContextAccessor = httpContextAccessor;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var correlationId = ResolveCorrelationId();

        request.Headers.TryAddWithoutValidation(CorrelationHeader, correlationId);

        // Also propagate via OTel baggage so Jaeger shows the link
        Activity.Current?.SetBaggage(CorrelationHeader, correlationId);

        _logger.LogInformation(
            "Outgoing HTTP {Method} {Uri} [CorrelationId={CorrelationId}]",
            request.Method,
            request.RequestUri,
            correlationId);

        var response = await base.SendAsync(request, cancellationToken);

        _logger.LogInformation(
            "Incoming HTTP {StatusCode} from {Uri} [CorrelationId={CorrelationId}]",
            (int)response.StatusCode,
            request.RequestUri,
            correlationId);

        return response;
    }

    private string ResolveCorrelationId()
    {
        var httpContext = _httpContextAccessor.HttpContext;

        if (httpContext is not null &&
            httpContext.Request.Headers.TryGetValue(CorrelationHeader, out var existing) &&
            !string.IsNullOrWhiteSpace(existing))
        {
            return existing!;
        }

        return Activity.Current?.TraceId.ToString() ?? Guid.NewGuid().ToString();
    }
}
