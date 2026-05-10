using McpApis.BuildingBlocks.Http.DelegatingHandlers;
using Microsoft.Extensions.DependencyInjection;

namespace McpApis.BuildingBlocks.Http;

public static class HttpClientFactoryExtensions
{
    /// <summary>
    /// Adds a typed HttpClient with the CorrelationHandler already wired in.
    /// </summary>
    public static IHttpClientBuilder AddHttpClientWithCorrelation<TClient, TImplementation>(
        this IServiceCollection services,
        string baseAddress)
        where TClient : class
        where TImplementation : class, TClient
    {
        services.AddHttpContextAccessor();
        services.AddTransient<CorrelationHandler>();

        return services
            .AddHttpClient<TClient, TImplementation>(client =>
            {
                client.BaseAddress = new Uri(baseAddress);
            })
            .AddHttpMessageHandler<CorrelationHandler>();
    }
}
