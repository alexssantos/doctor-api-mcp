using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using McpApis.McpServer.Infrastructure.Options;

namespace McpApis.McpServer.Infrastructure.Security;

public static class ObservabilityPolicies
{
    public const string Scheme = "ObservabilityApiKey";
    public const string Reader = "ObservabilityReader";
    public const string Admin = "ObservabilityAdmin";
    public const string RateLimit = "ObservabilityRateLimit";
}

public static class ObservabilitySecurityRegistration
{
    public static IServiceCollection AddObservabilitySecurity(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddAuthentication(ObservabilityPolicies.Scheme)
            .AddScheme<AuthenticationSchemeOptions, ApiKeyAuthenticationHandler>(
                ObservabilityPolicies.Scheme, _ => { });

        services.AddAuthorization(options =>
        {
            options.AddPolicy(ObservabilityPolicies.Reader,
                policy => policy.RequireAuthenticatedUser().RequireRole(ObservabilityPolicies.Reader));
            options.AddPolicy(ObservabilityPolicies.Admin,
                policy => policy.RequireAuthenticatedUser().RequireRole(ObservabilityPolicies.Admin));
        });

        var limits = configuration.GetSection(ObservabilityLimitsOptions.SectionName)
            .Get<ObservabilityLimitsOptions>() ?? new ObservabilityLimitsOptions();

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddFixedWindowLimiter(ObservabilityPolicies.RateLimit, limiter =>
            {
                limiter.PermitLimit = limits.RateLimitRequestsPerMinute;
                limiter.Window = TimeSpan.FromMinutes(1);
                limiter.QueueLimit = 0;
                limiter.AutoReplenishment = true;
            });
            options.GlobalLimiter = System.Threading.RateLimiting.PartitionedRateLimiter.Create<HttpContext, string>(
                context => System.Threading.RateLimiting.RateLimitPartition.GetConcurrencyLimiter(
                    context.User.Identity?.Name ?? context.Connection.RemoteIpAddress?.ToString() ?? "anonymous",
                    _ => new System.Threading.RateLimiting.ConcurrencyLimiterOptions
                    {
                        PermitLimit = limits.ConcurrencyLimit,
                        QueueLimit = 0
                    }));
        });

        services.AddSingleton<IServiceUrlPolicy, ServiceUrlPolicy>();
        return services;
    }
}

public sealed class ApiKeyAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    private readonly IOptionsMonitor<SecurityOptions> _security;

    public ApiKeyAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        IOptionsMonitor<SecurityOptions> security)
        : base(options, logger, encoder)
    {
        _security = security;
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var settings = _security.CurrentValue.Authentication;
        if (!settings.Enabled)
            return Task.FromResult(Success("development", isAdmin: true));

        var supplied = Request.Headers[settings.HeaderName].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(supplied) &&
            Request.Headers.Authorization.FirstOrDefault() is { } authorization &&
            authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            supplied = authorization["Bearer ".Length..].Trim();
        }

        if (string.IsNullOrWhiteSpace(supplied))
            return Task.FromResult(AuthenticateResult.NoResult());

        if (FixedTimeEquals(supplied, settings.AdminApiKey))
            return Task.FromResult(Success("api-key-admin", isAdmin: true));

        if (FixedTimeEquals(supplied, settings.ReaderApiKey))
            return Task.FromResult(Success("api-key-reader", isAdmin: false));

        return Task.FromResult(AuthenticateResult.Fail("Invalid observability API key."));
    }

    private AuthenticateResult Success(string name, bool isAdmin)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.Name, name),
            new(ClaimTypes.Role, ObservabilityPolicies.Reader)
        };
        if (isAdmin)
            claims.Add(new Claim(ClaimTypes.Role, ObservabilityPolicies.Admin));

        var identity = new ClaimsIdentity(claims, Scheme.Name);
        return AuthenticateResult.Success(
            new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name));
    }

    private static bool FixedTimeEquals(string supplied, string? expected)
    {
        if (string.IsNullOrEmpty(expected))
            return false;

        var left = Encoding.UTF8.GetBytes(supplied);
        var right = Encoding.UTF8.GetBytes(expected);
        return left.Length == right.Length && CryptographicOperations.FixedTimeEquals(left, right);
    }
}

public interface IServiceUrlPolicy
{
    bool TryValidate(string value, string? expectedNamespace, out Uri uri, out string error);
}

public sealed class ServiceUrlPolicy(
    IOptions<SecurityOptions> options,
    IHostEnvironment environment) : IServiceUrlPolicy
{
    private readonly SecurityOptions _options = options.Value;

    public bool TryValidate(string value, string? expectedNamespace, out Uri uri, out string error)
    {
        uri = null!;
        error = string.Empty;

        if (!Uri.TryCreate(value, UriKind.Absolute, out var parsed) ||
            parsed.Scheme is not ("http" or "https"))
        {
            error = "Only absolute HTTP(S) service URLs are allowed.";
            return false;
        }

        if (!string.IsNullOrEmpty(parsed.UserInfo) || !string.IsNullOrEmpty(parsed.Fragment))
        {
            error = "Credentials and fragments are forbidden in service URLs.";
            return false;
        }

        var port = parsed.IsDefaultPort ? (parsed.Scheme == "https" ? 443 : 80) : parsed.Port;
        if (!_options.AllowedServicePorts.Contains(port))
        {
            error = $"Port {port} is not allowed for service discovery.";
            return false;
        }

        if (IPAddress.TryParse(parsed.DnsSafeHost, out _))
        {
            error = "Literal IP addresses are forbidden for discovered services.";
            return false;
        }

        var hostAllowed = _options.AllowedServiceHostSuffixes.Any(suffix =>
            parsed.DnsSafeHost.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));
        if (!hostAllowed && !(environment.IsDevelopment() && parsed.IsLoopback))
        {
            error = "Service host is outside the configured DNS suffix allowlist.";
            return false;
        }

        if (!string.IsNullOrWhiteSpace(expectedNamespace))
        {
            if (!_options.AllowedNamespaces.Contains(expectedNamespace, StringComparer.OrdinalIgnoreCase))
            {
                error = $"Namespace '{expectedNamespace}' is outside the allowlist.";
                return false;
            }

            var namespaceSegment = $".{expectedNamespace}.svc.";
            if (!parsed.IsLoopback &&
                !parsed.DnsSafeHost.Contains(namespaceSegment, StringComparison.OrdinalIgnoreCase))
            {
                error = "Service URL does not belong to the expected namespace.";
                return false;
            }
        }

        uri = parsed;
        return true;
    }
}
