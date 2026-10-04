using Orleans.Streams;

namespace OrleansMultitenant.Tests.Examples.AuthorizedStreaming;

static class Constants
{
    internal const string Stream1Namespace = "Namespace1";
    internal const string Stream2Namespace = "Namespace2";
    internal const string TenantAwareGrainKeySuffix = "_TENANTAWARE";
}

interface IStreamProducerGrain : IGrainWithStringKey
{
    Task ProduceEvent(string @namespace, string key, int item);
}

interface IImplicitStreamSubscriberGrain : IGrainWithStringKey
{
    Task<int?> ExtractLastValue();
}

interface IExplicitStreamSubscriberGrain : IGrainWithStringKey
{
    Task Subscribe(string @namespace, string key);
    Task<int?> ExtractLastValue();
}

static class GrainExtensions
{
    internal static bool IsTenantAware(this Grain grain) => grain.GetPrimaryKeyString().EndsWith(Constants.TenantAwareGrainKeySuffix, StringComparison.Ordinal);

    internal static TenantStream<int> GetTenantAwareStream(this Grain grain, StreamId id)
    {
        string? tenantId = grain.GetTenantId();
        return tenantId is null
            ? grain.GetTenantStreamProvider(ClusterFixture.TenantAwareStreamProviderName, null!).GetStream<int>(id) // Have a stream without tenant id that can still use a multitenant stream provider
            : grain.GetTenantStreamProvider(ClusterFixture.TenantAwareStreamProviderName).GetStream<int>(id);
    }

    internal static IAsyncStream<int> GetTenantUnawareStream(this Grain grain, StreamId id)
     => grain.GetStreamProvider(ClusterFixture.TenantUnawareStreamProviderName).GetStream<int>(id);
}

sealed class StreamProducerGrain : Grain, IStreamProducerGrain
{
    public Task ProduceEvent(string @namespace, string key, int value) => this.IsTenantAware()
        ? this.GetTenantAwareStream(StreamId.Create(@namespace, key)).OnNextAsync(value)
        : this.GetTenantUnawareStream(StreamId.Create(@namespace, key)).OnNextAsync(value);
}

[ImplicitStreamSubscription(Constants.Stream1Namespace)]
sealed class ImplicitStreamSubscriberGrain : Grain, IImplicitStreamSubscriberGrain
{
    int? lastValue;
    StreamSubscriptionHandle<int>? tenantAwaresubscription;
    StreamSubscriptionHandle<int>? tenantUnawaresubscription;

    async Task EnsureUnsubscribed()
    {
        if (tenantAwaresubscription is not null) { await tenantAwaresubscription.UnsubscribeAsync(); tenantAwaresubscription = null; }
        if (tenantUnawaresubscription is not null) { await tenantUnawaresubscription.UnsubscribeAsync(); tenantUnawaresubscription = null; }
        lastValue = null;
    }

    public override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        await EnsureUnsubscribed();
        var streamId = StreamId.Create(Constants.Stream1Namespace, this.GetPrimaryKeyString());

        if (this.IsTenantAware())
            tenantAwaresubscription = await this.GetTenantAwareStream(streamId).SubscribeAsync(OnNext);
        else
            tenantUnawaresubscription = await this.GetTenantUnawareStream(streamId).SubscribeAsync(OnNext);
    }

    Task OnNext(int value, StreamSequenceToken token)
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

sealed class ExplicitStreamSubscriberGrain : Grain, IExplicitStreamSubscriberGrain
{
    int? lastValue;
    StreamSubscriptionHandle<int>? tenantAwaresubscription;
    StreamSubscriptionHandle<int>? tenantUnawaresubscription;

    async Task EnsureUnsubscribed()
    {
        if (tenantAwaresubscription is not null) { await tenantAwaresubscription.UnsubscribeAsync(); tenantAwaresubscription = null; }
        if (tenantUnawaresubscription is not null) { await tenantUnawaresubscription.UnsubscribeAsync(); tenantUnawaresubscription = null; }
        lastValue = null;
    }

    public async Task Subscribe(string @namespace, string key)
    {
        await EnsureUnsubscribed();
        var streamId = StreamId.Create(@namespace, key);

        if (this.IsTenantAware())
            tenantAwaresubscription = await this.GetTenantAwareStream(streamId).SubscribeAsync(OnNext);
        else
            tenantUnawaresubscription = await this.GetTenantUnawareStream(streamId).SubscribeAsync(OnNext);
    }

    Task OnNext(int value, StreamSequenceToken token)
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
