using System.Threading.Channels;
using Microsoft.Extensions.Options;
using McpApis.McpServer.Domain.Contracts;
using McpApis.McpServer.Domain.Models;
using McpApis.McpServer.Infrastructure.Options;
using McpApis.McpServer.Services.Contracts;

namespace McpApis.McpServer.Services;

/// <summary>
/// Correlates every discovery source into <see cref="DiscoveredApplication"/> entries
/// and installs the result atomically in the <see cref="IApplicationCatalog"/>.
///
/// Sources per Discovery:Mode:
///   Config     – "Services" config section only (legacy, feature 003 behavior)
///   Kubernetes – Services labelled mcp-apis/indexed=true only (legacy)
///   Both       – Config + labelled Services (legacy)
///   Auto       – cluster-wide Deployments + Services/Endpoints + Jaeger /api/services + Config
///
/// Each source is collected in its own try/catch: one failing source never aborts
/// the scan. Apps declared in Config or labelled indexed=true default to enabled;
/// everything else is discovered disabled (opt-in via the dashboard toggle).
/// </summary>
public class DiscoveryOrchestrator : IDiscoveryOrchestrator
{
    private static readonly string[] DefaultExcludeNamespaces =
        ["kube-system", "kube-public", "kube-node-lease"];
    private static readonly string[] DefaultExcludeApps =
        ["mcpserver", "jaeger", "prometheus", "grafana", "loki", "promtail",
         "postgres-preco", "postgres-produto", "kubernetes"];
    private static readonly string[] DefaultExcludeOtelServices = ["jaeger-query", "McpServer"];

    private readonly IConfiguration _config;
    private readonly IKubernetesCollector _k8s;
    private readonly IApplicationCatalog _catalog;
    private readonly IIndexingStateStore _stateStore;
    private readonly IDeploymentHistoryStore _deploymentHistory;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<DiscoveryOrchestrator> _logger;
    private readonly SecurityOptions _security;

    private readonly SemaphoreSlim _scanLock = new(1, 1);
    private readonly Channel<bool> _rescanSignal = Channel.CreateBounded<bool>(
        new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite });

    // Successful validations are cached per (app, baseUrl) and refreshed every
    // Discovery:RevalidateSeconds; failures are retried on every scan.
    private readonly Dictionary<string, (string BaseUrl, DateTimeOffset At, OpenApiInfo Info)> _validationCache =
        new(StringComparer.OrdinalIgnoreCase);

    public DiscoveryOrchestrator(
        IConfiguration config,
        IKubernetesCollector k8s,
        IApplicationCatalog catalog,
        IIndexingStateStore stateStore,
        IDeploymentHistoryStore deploymentHistory,
        IServiceScopeFactory scopeFactory,
        IOptions<SecurityOptions> security,
        ILogger<DiscoveryOrchestrator> logger)
    {
        _config = config;
        _k8s = k8s;
        _catalog = catalog;
        _stateStore = stateStore;
        _deploymentHistory = deploymentHistory;
        _scopeFactory = scopeFactory;
        _security = security.Value;
        _logger = logger;
    }

    public DateTimeOffset? LastScanCompletedAt { get; private set; }

    /// <summary>Read by the discovery background service to wake up on demand.</summary>
    public ChannelReader<bool> RescanSignal => _rescanSignal.Reader;

    public void RequestRescan() => _rescanSignal.Writer.TryWrite(true);

    public async Task<DiscoveryScanResult> ScanAsync(CancellationToken ct = default)
    {
        await _scanLock.WaitAsync(ct);
        try
        {
            return await RunScanAsync(ct);
        }
        finally
        {
            _scanLock.Release();
        }
    }

    private async Task<DiscoveryScanResult> RunScanAsync(CancellationToken ct)
    {
        var mode = _config["Discovery:Mode"] ?? "Auto";
        var warnings = new List<string>();
        var now = DateTimeOffset.UtcNow;

        _logger.LogInformation("Discovery scan starting (mode: {Mode})...", mode);

        var candidates = mode.Equals("Auto", StringComparison.OrdinalIgnoreCase)
            ? await CollectAutoAsync(warnings, ct)
            : await CollectLegacyAsync(mode, warnings, ct);

        await ValidateOpenApiAsync(candidates, ct);
        await ApplyIndexingStateAsync(candidates, ct);

        var apps = candidates
            .Select(c => c.ToApplication(
                now,
                GetList("Discovery:AllowedLabels",
                    ["app", "app.kubernetes.io/name", "app.kubernetes.io/version", "app.kubernetes.io/part-of"]),
                GetList("Discovery:AllowedAnnotations",
                    ["mcp-apis/owner", "mcp-apis/team", "mcp-apis/description", "mcp-apis/metrics-id",
                     "mcp-apis/metrics-enabled", "mcp-apis/logs-enabled", "mcp-apis/dependencies",
                     "app.kubernetes.io/version", "deployment.kubernetes.io/revision"])))
            .OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var forgetAfter = TimeSpan.FromMinutes(GetInt("Discovery:ForgetAfterMinutes", 60));
        _catalog.ReplaceSnapshot(apps, forgetAfter);
        await _deploymentHistory.ObserveAsync(_catalog.GetAll(), now, ct);
        LastScanCompletedAt = DateTimeOffset.UtcNow;

        foreach (var warning in warnings)
            _logger.LogWarning("Discovery: {Warning}", warning);

        var installed = _catalog.GetAll();
        foreach (var app in installed)
        {
            _logger.LogInformation(
                "{Mark} {Name} (ns: {Ns}, sources: {Sources}, enabled: {Enabled}, openapi: {OpenApi})",
                app.OpenApi.Validated ? "✓" : "✗",
                app.Name,
                app.Namespace ?? "-",
                app.Sources,
                app.Enabled,
                app.OpenApi.Validated ? app.OpenApi.Path : "not indexable");
        }

        var result = new DiscoveryScanResult(
            installed.Count,
            installed.Count(a => a.OpenApi.Validated),
            installed.Count(a => a.Sources == DiscoverySources.OpenTelemetry),
            warnings);

        _logger.LogInformation(
            "Discovery scan complete: {Discovered} app(s), {Validated} validated, {OtelOnly} OTel-only.",
            result.Discovered, result.Validated, result.OtelOnly);

        return result;
    }

    // ── Auto mode: cluster-wide correlation ────────────────────────────────────

    private async Task<List<AppCandidate>> CollectAutoAsync(
        List<string> warnings,
        CancellationToken cancellationToken)
    {
        var excludeNamespaces = GetList("Discovery:ExcludeNamespaces", DefaultExcludeNamespaces);
        var allowedNamespaces = _security.AllowedNamespaces
            .Where(ns => !excludeNamespaces.Contains(ns))
            .ToArray();
        var excludeApps = GetList("Discovery:ExcludeApps", DefaultExcludeApps)
            .Select(NameNormalizer.Normalize)
            .ToHashSet(StringComparer.Ordinal);

        var deployments = new List<DeploymentDetail>();
        var services = new List<ServiceDetail>();
        var readyEndpoints = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            deployments = (await _k8s.ListDeploymentDetailsAsync(allowedNamespaces, cancellationToken))
                .Where(d => !excludeApps.Contains(NameNormalizer.Normalize(d.Name)))
                .ToList();
            services = (await _k8s.ListServiceDetailsAsync(allowedNamespaces, cancellationToken))
                .Where(s => !excludeApps.Contains(NameNormalizer.Normalize(s.Name)))
                .ToList();
        }
        catch (Exception ex)
        {
            warnings.Add($"Kubernetes discovery failed: {ex.Message}");
        }

        try
        {
            readyEndpoints = await _k8s.ListServicesWithReadyEndpointsAsync(
                allowedNamespaces, cancellationToken);
        }
        catch (Exception ex)
        {
            warnings.Add($"Endpoints listing failed: {ex.Message}");
        }

        // 1. Group Services with the Deployments they select (structural match:
        //    service selector ⊆ deployment pod-template labels, same namespace).
        var candidates = new List<AppCandidate>();
        foreach (var deployment in deployments)
        {
            candidates.Add(new AppCandidate
            {
                CanonicalKey = NameNormalizer.Normalize(deployment.Name),
                Namespace = deployment.Namespace,
                DeploymentName = deployment.Name,
                Deployment = deployment,
                Sources = DiscoverySources.Deployment
            });
        }

        foreach (var service in services)
        {
            var owners = candidates
                .Where(c => c.DeploymentName is not null
                            && c.Namespace == service.Namespace
                            && c.Deployment is not null
                            && SelectorMatches(service.Selector, c.Deployment.TemplateLabels))
                .ToList();

            if (owners.Count == 0)
            {
                candidates.Add(new AppCandidate
                {
                    CanonicalKey = NameNormalizer.Normalize(service.Name),
                    Namespace = service.Namespace,
                    Service = service,
                    Sources = DiscoverySources.Network
                });
                continue;
            }

            if (owners.Count > 1)
                warnings.Add(
                    $"Service '{service.Namespace}/{service.Name}' selects multiple deployments " +
                    $"({string.Join(", ", owners.Select(o => o.DeploymentName))}); attaching to all.");

            foreach (var owner in owners)
            {
                if (owner.Service is null)
                {
                    owner.Service = service;
                }
                else if (owner.Service.Name != service.Name)
                {
                    warnings.Add(
                        $"Deployment '{owner.Namespace}/{owner.DeploymentName}' is selected by multiple services " +
                        $"('{owner.Service.Name}', '{service.Name}'); keeping '{owner.Service.Name}'.");
                }
                owner.Sources |= DiscoverySources.Network;
            }
        }

        // 2. Merge only within the same namespace. Cross-namespace names remain
        // distinct and are resolved as ambiguous unless the caller supplies ns.
        candidates = MergeWithinNamespace(candidates, warnings);

        // 3. OTel: attach Jaeger service names, or create OTel-only candidates.
        await AttachOtelAsync(candidates, excludeApps, warnings, cancellationToken);

        // 4. Config entries reinforce (and default-enable) matching apps or add config-only ones.
        AttachConfigEntries(candidates, warnings);

        // 5. Resolve base URLs and endpoint readiness.
        var labelKey = _config["Discovery:KubernetesLabel"] ?? "mcp-apis/indexed";
        foreach (var candidate in candidates)
        {
            if (candidate.Service is not null)
            {
                candidate.BaseUrl = candidate.Service.Annotations.GetValueOrDefault("mcp-apis/base-url")
                    ?? $"http://{candidate.Service.Name}.{candidate.Service.Namespace}.svc.cluster.local";
                candidate.HasReadyEndpoints =
                    readyEndpoints.Contains($"{candidate.Service.Namespace}/{candidate.Service.Name}");

                var label = candidate.Service.Labels.GetValueOrDefault(labelKey);
                if (string.Equals(label, "false", StringComparison.OrdinalIgnoreCase))
                    candidate.LockedDisabled = true;
                else if (string.Equals(label, "true", StringComparison.OrdinalIgnoreCase))
                    candidate.DefaultEnabled = true;
            }
            else
            {
                candidate.BaseUrl ??= candidate.ConfigUrl;
            }
        }

        return candidates;
    }

    private async Task AttachOtelAsync(
        List<AppCandidate> candidates,
        HashSet<string> excludeApps,
        List<string> warnings,
        CancellationToken cancellationToken)
    {
        var excludeOtel = GetList("Discovery:ExcludeOtelServices", DefaultExcludeOtelServices)
            .Select(NameNormalizer.Normalize)
            .ToHashSet(StringComparer.Ordinal);

        List<string> otelServices;
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var jaeger = scope.ServiceProvider.GetRequiredService<IJaegerCollector>();
            otelServices = await jaeger.GetServicesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            warnings.Add($"OTel (Jaeger) discovery failed: {ex.Message}");
            return;
        }

        foreach (var otelName in otelServices)
        {
            var normalized = NameNormalizer.Normalize(otelName);
            if (excludeOtel.Contains(normalized) || excludeApps.Contains(normalized))
                continue;

            // Annotation override wins over name normalization.
            var annotated = candidates.Where(c =>
                    c.Service?.Annotations.GetValueOrDefault("mcp-apis/otel-service-name") == otelName)
                .ToArray();
            var normalizedMatches = candidates.Where(c => c.CanonicalKey == normalized).ToArray();
            var matches = annotated.Length > 0 ? annotated : normalizedMatches;

            if (matches.Length == 1)
            {
                var match = matches[0];
                match.OtelServiceName = otelName;
                match.Sources |= DiscoverySources.OpenTelemetry;
            }
            else if (matches.Length > 1)
            {
                warnings.Add(
                    $"OTel service '{otelName}' matches multiple namespaces " +
                    $"({string.Join(", ", matches.Select(m => m.Namespace))}); add mcp-apis/otel-service-name explicitly.");
            }
            else if (_security.AllowedNamespaces.Length == 1)
            {
                candidates.Add(new AppCandidate
                {
                    CanonicalKey = normalized,
                    Namespace = _security.AllowedNamespaces[0],
                    OtelServiceName = otelName,
                    Sources = DiscoverySources.OpenTelemetry
                });
            }
            else
            {
                warnings.Add(
                    $"OTel-only service '{otelName}' has no namespace identity and was ignored in multi-namespace mode.");
            }
        }
    }

    private void AttachConfigEntries(List<AppCandidate> candidates, List<string> warnings)
    {
        foreach (var (alias, url) in ReadConfigServices(warnings))
        {
            var normalized = NameNormalizer.Normalize(alias);
            var namespaceFromUrl = TryGetNamespaceFromClusterUrl(url);
            var matching = candidates.Where(c => c.CanonicalKey == normalized)
                .Where(c => namespaceFromUrl is null ||
                            c.Namespace?.Equals(namespaceFromUrl, StringComparison.OrdinalIgnoreCase) == true)
                .ToArray();
            var match = matching.Length == 1 ? matching[0] : null;
            if (match is not null)
            {
                match.Sources |= DiscoverySources.Config;
                match.ConfigUrl = url;
                match.DefaultEnabled = true;
            }
            else
            {
                candidates.Add(new AppCandidate
                {
                    CanonicalKey = normalized,
                    Namespace = namespaceFromUrl ??
                                (_security.AllowedNamespaces.Length == 1 ? _security.AllowedNamespaces[0] : null),
                    Sources = DiscoverySources.Config,
                    ConfigUrl = url,
                    BaseUrl = url,
                    DefaultEnabled = true
                });
            }
        }
    }

    private static List<AppCandidate> MergeWithinNamespace(
        List<AppCandidate> candidates, List<string> warnings)
    {
        var result = candidates
            .GroupBy(c => $"{c.Namespace ?? "~"}/{c.CanonicalKey}", StringComparer.OrdinalIgnoreCase)
            .Select(group => group.Aggregate((a, b) => a.MergeWith(b)))
            .ToList();

        foreach (var group in result.GroupBy(c => c.CanonicalKey, StringComparer.Ordinal))
        {
            if (group.Count() > 1)
                warnings.Add(
                    $"Application name '{group.Key}' exists in multiple namespaces " +
                    $"({string.Join(", ", group.Select(c => c.Namespace))}); callers must supply namespace.");
        }
        return result;
    }

    // ── Legacy modes (feature 003 semantics) ───────────────────────────────────

    private async Task<List<AppCandidate>> CollectLegacyAsync(
        string mode,
        List<string> warnings,
        CancellationToken cancellationToken)
    {
        var merged = new Dictionary<string, AppCandidate>(StringComparer.OrdinalIgnoreCase);

        if (mode is "Config" or "Both")
        {
            foreach (var (alias, url) in ReadConfigServices(warnings))
            {
                merged[alias] = new AppCandidate
                {
                    CanonicalKey = alias,
                    Namespace = TryGetNamespaceFromClusterUrl(url) ??
                                (_security.AllowedNamespaces.Length == 1 ? _security.AllowedNamespaces[0] : null),
                    Sources = DiscoverySources.Config,
                    BaseUrl = url,
                    DefaultEnabled = true
                };
            }
        }

        if (mode is "Kubernetes" or "Both")
        {
            var labelKey = _config["Discovery:KubernetesLabel"] ?? "mcp-apis/indexed";
            try
            {
                foreach (var (name, url) in await _k8s.DiscoverIndexedServicesAsync(
                             labelKey, cancellationToken))
                {
                    merged[name] = new AppCandidate
                    {
                        CanonicalKey = name,
                        Namespace = _security.AllowedNamespaces.FirstOrDefault(),
                        KubernetesServiceNameOverride = name,
                        Sources = DiscoverySources.Network,
                        BaseUrl = url,
                        DefaultEnabled = true
                    };
                }
            }
            catch (Exception ex)
            {
                warnings.Add($"Kubernetes label discovery failed (label: {labelKey}): {ex.Message}");
            }
        }

        return [.. merged.Values];
    }

    private IEnumerable<(string Alias, string Url)> ReadConfigServices(List<string> warnings)
    {
        foreach (var child in _config.GetSection("Services").GetChildren())
        {
            if (string.IsNullOrWhiteSpace(child.Value))
                continue;

            if (Uri.TryCreate(child.Value, UriKind.Absolute, out var uri) && !uri.Host.Contains('.'))
            {
                warnings.Add(
                    $"Service '{child.Key}' uses short hostname '{uri.Host}'. " +
                    $"Use http://{uri.Host}.<namespace>.svc.cluster.local for cross-namespace access.");
            }

            yield return (child.Key, child.Value);
        }
    }

    // ── Validation and persisted state ─────────────────────────────────────────

    private async Task ValidateOpenApiAsync(List<AppCandidate> candidates, CancellationToken ct)
    {
        var revalidateAfter = TimeSpan.FromSeconds(GetInt("Discovery:RevalidateSeconds", 300));
        var throttle = new SemaphoreSlim(8, 8);
        using var scope = _scopeFactory.CreateScope();
        var validator = scope.ServiceProvider.GetRequiredService<IServiceValidator>();

        var tasks = candidates
            .Where(c => c.BaseUrl is not null)
            .Select(async candidate =>
            {
                var cacheKey = candidate.IdentityKey;
                if (_validationCache.TryGetValue(cacheKey, out var cached)
                    && cached.BaseUrl == candidate.BaseUrl
                    && cached.Info.Validated
                    && DateTimeOffset.UtcNow - cached.At < revalidateAfter)
                {
                    candidate.OpenApi = cached.Info;
                    return;
                }

                await throttle.WaitAsync(ct);
                try
                {
                    var result = await validator.ValidateAsync(
                        candidate.CanonicalKey, candidate.BaseUrl!, candidate.Namespace, ct);
                    candidate.OpenApi = new OpenApiInfo(
                        result.IsValid,
                        result.IsValid ? result.OpenApiPath : null,
                        result.Failures);

                    if (result.IsValid)
                        _validationCache[cacheKey] =
                            (candidate.BaseUrl!, DateTimeOffset.UtcNow, candidate.OpenApi);
                    else
                        _validationCache.Remove(cacheKey);
                }
                catch (Exception ex)
                {
                    candidate.OpenApi = new OpenApiInfo(false, null, [$"Validation error: {ex.Message}"]);
                }
                finally
                {
                    throttle.Release();
                }
            });

        await Task.WhenAll(tasks);
    }

    private async Task ApplyIndexingStateAsync(List<AppCandidate> candidates, CancellationToken ct)
    {
        var overrides = await _stateStore.LoadAsync(ct);
        foreach (var candidate in candidates)
        {
            var enabledByDefault = candidate.DefaultEnabled;
            var userChoice = overrides.TryGetValue(candidate.IdentityKey, out var choice) ||
                             overrides.TryGetValue(candidate.CanonicalKey, out choice)
                ? choice
                : (bool?)null;
            candidate.Enabled = !candidate.LockedDisabled && (userChoice ?? enabledByDefault);
        }
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private static bool SelectorMatches(
        Dictionary<string, string> selector, Dictionary<string, string> templateLabels) =>
        selector.Count > 0
        && selector.All(kv => templateLabels.TryGetValue(kv.Key, out var value) && value == kv.Value);

    private HashSet<string> GetList(string key, string[] defaults)
    {
        var values = _config.GetSection(key).Get<string[]>();
        return new HashSet<string>(
            values is { Length: > 0 } ? values : defaults,
            StringComparer.OrdinalIgnoreCase);
    }

    private int GetInt(string key, int fallback) =>
        int.TryParse(_config[key], out var value) ? value : fallback;

    private static string? TryGetNamespaceFromClusterUrl(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri))
            return null;
        var labels = uri.DnsSafeHost.Split('.', StringSplitOptions.RemoveEmptyEntries);
        var svcIndex = Array.FindIndex(labels, label =>
            label.Equals("svc", StringComparison.OrdinalIgnoreCase));
        return svcIndex > 0 ? labels[svcIndex - 1] : null;
    }

    /// <summary>Mutable working entry for a single application while a scan assembles its sources.</summary>
    private class AppCandidate
    {
        public required string CanonicalKey { get; set; }
        public string IdentityKey => $"{Namespace ?? "~"}/{CanonicalKey}";
        public string? Namespace { get; set; }
        public string? DeploymentName { get; set; }
        public DeploymentDetail? Deployment { get; set; }
        public ServiceDetail? Service { get; set; }
        public string? KubernetesServiceNameOverride { get; set; }
        public string? OtelServiceName { get; set; }
        public DiscoverySources Sources { get; set; }
        public string? BaseUrl { get; set; }
        public string? ConfigUrl { get; set; }
        public bool HasReadyEndpoints { get; set; }
        public bool LockedDisabled { get; set; }
        public bool DefaultEnabled { get; set; }
        public bool Enabled { get; set; }
        public OpenApiInfo OpenApi { get; set; } = OpenApiInfo.NotValidated;

        public AppCandidate MergeWith(AppCandidate other)
        {
            Sources |= other.Sources;
            DeploymentName ??= other.DeploymentName;
            Deployment ??= other.Deployment;
            Service ??= other.Service;
            OtelServiceName ??= other.OtelServiceName;
            ConfigUrl ??= other.ConfigUrl;
            DefaultEnabled |= other.DefaultEnabled;
            LockedDisabled |= other.LockedDisabled;
            return this;
        }

        public DiscoveredApplication ToApplication(
            DateTimeOffset now,
            IReadOnlySet<string> allowedLabels,
            IReadOnlySet<string> allowedAnnotations)
        {
            var labels = MergeMetadata(Deployment?.Labels, Service?.Labels)
                .Where(kv => allowedLabels.Contains(kv.Key))
                .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal);
            var annotations = MergeMetadata(Deployment?.Annotations, Service?.Annotations)
                .Where(kv => allowedAnnotations.Contains(kv.Key))
                .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal);
            var version = annotations.GetValueOrDefault("app.kubernetes.io/version") ??
                          labels.GetValueOrDefault("app.kubernetes.io/version") ??
                          ExtractImageTag(Deployment?.Image);
            var dependencies = annotations.GetValueOrDefault("mcp-apis/dependencies")?
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray() ?? [];
            var hasKubernetes = Deployment is not null || Service is not null;
            var metricsEnabled = annotations.GetValueOrDefault("mcp-apis/metrics-enabled")
                ?.Equals("true", StringComparison.OrdinalIgnoreCase) == true;
            var logsEnabled = hasKubernetes &&
                              annotations.GetValueOrDefault("mcp-apis/logs-enabled")
                                  ?.Equals("false", StringComparison.OrdinalIgnoreCase) != true;

            return new DiscoveredApplication
            {
                Name = CanonicalKey,
                Namespace = Namespace,
                Sources = Sources,
                DeploymentName = DeploymentName,
                KubernetesServiceName = Service?.Name ?? KubernetesServiceNameOverride,
                OtelServiceName = OtelServiceName,
                MetricsId = annotations.GetValueOrDefault("mcp-apis/metrics-id") ?? CanonicalKey,
                BaseUrl = BaseUrl?.TrimEnd('/'),
                Selector = Service?.Selector is { Count: > 0 }
                    ? new Dictionary<string, string>(Service.Selector)
                    : Deployment?.Selector is { Count: > 0 }
                        ? new Dictionary<string, string>(Deployment.Selector)
                        : new Dictionary<string, string>(),
                Image = Deployment?.Image,
                Version = version,
                Revision = Deployment?.Revision,
                DesiredReplicas = Deployment?.Replicas ?? 0,
                ReadyReplicas = Deployment?.ReadyReplicas ?? 0,
                Labels = labels,
                Annotations = annotations,
                Owner = annotations.GetValueOrDefault("mcp-apis/owner"),
                Team = annotations.GetValueOrDefault("mcp-apis/team"),
                Description = annotations.GetValueOrDefault("mcp-apis/description"),
                DeclaredDependencies = dependencies,
                Coverage = new SignalCoverage(
                    hasKubernetes ? SourceAvailability.Available : SourceAvailability.Unavailable,
                    metricsEnabled ? SourceAvailability.Available : SourceAvailability.Unavailable,
                    Sources.HasFlag(DiscoverySources.OpenTelemetry)
                        ? SourceAvailability.Available : SourceAvailability.Unavailable,
                    logsEnabled ? SourceAvailability.Available : SourceAvailability.Unavailable,
                    OpenApi.Validated ? SourceAvailability.Available : SourceAvailability.Unavailable,
                    hasKubernetes ? SourceAvailability.Available : SourceAvailability.Unavailable),
                HasReadyEndpoints = HasReadyEndpoints,
                OpenApi = OpenApi,
                Enabled = Enabled,
                LockedDisabled = LockedDisabled,
                FirstSeen = now,
                LastSeen = now
            };
        }

        private static Dictionary<string, string> MergeMetadata(
            IReadOnlyDictionary<string, string>? first,
            IReadOnlyDictionary<string, string>? second)
        {
            var result = first is null
                ? new Dictionary<string, string>(StringComparer.Ordinal)
                : new Dictionary<string, string>(first, StringComparer.Ordinal);
            if (second is not null)
            {
                foreach (var (key, value) in second)
                    result[key] = value;
            }
            return result;
        }

        private static string? ExtractImageTag(string? image)
        {
            if (string.IsNullOrWhiteSpace(image))
                return null;
            var digestIndex = image.IndexOf('@');
            var withoutDigest = digestIndex >= 0 ? image[..digestIndex] : image;
            var colon = withoutDigest.LastIndexOf(':');
            var slash = withoutDigest.LastIndexOf('/');
            return colon > slash ? withoutDigest[(colon + 1)..] : null;
        }
    }
}
