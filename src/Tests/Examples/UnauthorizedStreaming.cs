using Orleans.Streams;

namespace OrleansMultitenant.Tests.Examples.UnauthorizedStreaming;

static class Constants
{
    internal const string Stream1Namespace = "Namespace1";
    internal const string Stream2Namespace = "Namespace2";
    internal const string TenantAwareGrainKeySuffix = "_TENANTAWARE";
}

interface ICrossTenantStreamProducerGrain : IGrainWithStringKey
{
    Task ProduceEvent(string provider, string @namespace, string? tenantId, string key, int item);
}

interface ICrossTenantExplicitStreamSubscriberGrain : IGrainWithStringKey
{
    Task Subscribe(string provider, string @namespace, string? tenantId, string key);
    Task<int?> ExtractLastValue();
}

static class GrainExtensions
{
    internal static bool IsTenantAware(this Grain grain) => grain.GetPrimaryKeyString().EndsWith(Constants.TenantAwareGrainKeySuffix, StringComparison.Ordinal);

    internal static TenantStream<int> GetTenantAwareStream(this Grain grain, string provider, string? tenantId, StreamId id)
     => grain.GetTenantStreamProvider(provider, tenantId!).GetStream<int>(id); // Note that we allow to pass in null tenantId by design - the internals must support null tenants, even though the public API does not allow it

    internal static IAsyncStream<int> GetTenantUnawareStream(this Grain grain, string provider, StreamId id)
     => grain.GetStreamProvider(provider).GetStream<int>(id);
}

sealed class CrossTenantStreamProducerGrain : Grain, ICrossTenantStreamProducerGrain
{
    public Task ProduceEvent(string provider, string @namespace, string? tenantId, string key, int value) => this.IsTenantAware()
        ? this.GetTenantAwareStream(provider, tenantId, StreamId.Create(@namespace, key)).OnNextAsync(value)
        : this.GetTenantUnawareStream(provider, StreamId.Create(@namespace, key)).OnNextAsync(value);
}

sealed class CrossTenantExplicitStreamSubscriberGrain : Grain, ICrossTenantExplicitStreamSubscriberGrain
{
    int? lastValue;
    StreamSubscriptionHandle<int>? tenantAwaresubscription;
    StreamSubscriptionHandle<int>? tenantUnawaresubscription;

    async Task EnsureUnsubscribed()
    {
        if (tenantAwaresubscription   is not null) { await tenantAwaresubscription  .UnsubscribeAsync(); tenantAwaresubscription   = null; }
        if (tenantUnawaresubscription is not null) { await tenantUnawaresubscription.UnsubscribeAsync(); tenantUnawaresubscription = null; }
        lastValue = null;
    }

    public async Task Subscribe(string provider, string @namespace, string? tenantId, string key)
    {
        await EnsureUnsubscribed();
        var streamId = StreamId.Create(@namespace, key);

        if (this.IsTenantAware())
            tenantAwaresubscription = await this.GetTenantAwareStream(provider, tenantId, streamId).SubscribeAsync(OnNext);
        else
            tenantUnawaresubscription = await this.GetTenantUnawareStream(provider, streamId).SubscribeAsync(OnNext);
    }

    Task OnNext(int value, StreamSequenceToken? token)
    {
        if (lastValue is not null) throw new InvalidOperationException("Received new value before previous value was extracted");
        lastValue = value;
        return Task.CompletedTask;
    }

    public Task<int?> ExtractLastValue()
    {
        int? r = lastValue;
        lastValue = null;
        return Task.FromResult(r);
    }
}
