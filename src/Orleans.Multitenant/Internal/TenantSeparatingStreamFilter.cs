using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Orleans.Streams.Filtering;

namespace Orleans.Multitenant.Internal;

interface ITenantEvent
{
    object? Event { get; }
}

[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "Class is instantiated through DI")]
sealed class TenantSeparatingStreamFilter : IStreamFilter
{
    readonly ILogger logger;
    readonly Lazy<IStreamFilter?> tenantUnawareFilter;

    public TenantSeparatingStreamFilter(ILoggerFactory loggerFactory, IServiceProvider services, [ServiceKey] string streamProviderName)
    {
        logger = loggerFactory.CreateLogger(nameof(TenantSeparatingStreamFilter));

        // Orleans uses one stream filter per stream provider: the last one that was registered for the provider, which is this filter.
        // A stream filter that was registered for the provider before this filter (in addStreamProvider) is invoked by this filter.
        // It is resolved on first use, because this filter is itself one of the keyed services that are resolved here
        tenantUnawareFilter = new(() => services.GetKeyedServices<IStreamFilter>(streamProviderName).LastOrDefault(filter => filter is not TenantSeparatingStreamFilter));

        logger.LogInformation("created");
    }

    public bool ShouldDeliver(StreamId streamId, object item, string? filterData)
    {
        if (item is ITenantEvent tenantEvent) // This forces the tenant aware API to be used
            return tenantUnawareFilter.Value?.ShouldDeliver(streamId, tenantEvent.Event!, filterData) ?? true; // A null event is passed on as is, like Orleans does

        logger.TenantUnawareStreamApiUsed(streamId, item);
        return false;
    }
}

/// <summary>
/// Guards against a stream filter that is registered for a multitenant stream provider after the <see cref="TenantSeparatingStreamFilter"/>:
/// Orleans would use that filter instead, which would silently disable tenant separation for the stream provider
/// </summary>
sealed class TenantSeparatingStreamFilterValidator(IServiceProvider services, string streamProviderName) : IConfigurationValidator
{
    public void ValidateConfiguration()
    {
        var filter = services.GetKeyedService<IStreamFilter>(streamProviderName);
        if (filter is not TenantSeparatingStreamFilter)
        {
            throw new OrleansConfigurationException(
                $"Stream provider '{streamProviderName}' was added with {nameof(Multitenant.SiloBuilderExtensions.AddMultitenantStreams)}, but stream filter {filter?.GetType().FullName ?? "NULL"} was registered for it afterwards. " +
                "This disables tenant separation for the stream provider, because Orleans only uses the stream filter that was registered last for a stream provider. " +
                $"Register the stream filter in the addStreamProvider parameter of {nameof(Multitenant.SiloBuilderExtensions.AddMultitenantStreams)} instead.");
        }
    }
}
