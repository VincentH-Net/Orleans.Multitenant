using Microsoft.Extensions.Configuration;
using Orleans.Configuration;
using Orleans.Providers;
using Orleans.Runtime;
using Orleans.Storage;
using Orleans.Streams;
using Orleans.TestingHost;
using OrleansMultitenant.Tests.Examples.StreamFiltering;

namespace OrleansMultitenant.Tests.UnitTests;

public sealed class StreamFilterTests(StreamFilterTests.ClusterFixture fixture) : IClassFixture<StreamFilterTests.ClusterFixture>
{
    const string Namespace = "FilteredNamespace";

    readonly TestCluster cluster = fixture.Cluster;

    [Fact]
    public async Task StreamFilter_RegisteredInAddStreamProvider_FiltersEventsSentWithTenantAwareApi()
    {
        var stream = cluster.Client.GetTenantStreamProvider(ClusterFixture.StreamProviderName, "TenantA").GetStream<int>(Namespace, ThisTestMethodId());
        StreamReceiver receiver = new();
        var handle = await stream.SubscribeAsync(receiver.Observer, null, "the filter data");

        await stream.OnNextAsync(1); // Odd number, so the filter blocks it
        await stream.OnNextAsync(2);

        Assert.Equal([2], await receiver.ReceiveAsync(1));
        var filterCalls = EvenNumbersStreamFilter.Calls.Where(call => call.StreamId == stream.StreamId).ToList();
        Assert.Equal([1, 2], filterCalls.Select(call => Assert.IsType<int>(call.Item)).Distinct()); // The filter receives the events, not tenant specific wrappers
        Assert.All(filterCalls, call => Assert.Equal("the filter data", call.FilterData));
        Assert.All(filterCalls, call => Assert.Equal("TenantA", call.StreamId.GetTenantId()));
        await handle.UnsubscribeAsync();
    }

    [Fact]
    public async Task StreamFilter_RegisteredInAddStreamProvider_IsNotInvokedForEventsSentWithTenantUnawareApi()
    {
        var stream = cluster.Client.GetStreamProvider(ClusterFixture.StreamProviderName).GetStream<int>(Namespace, ThisTestMethodId());
        StreamReceiver receiver = new();
        bool tenantAwareApiNotUsedErrorIsLogged = false;
        var processorId = ProcessingLogger.Instance.AddLogEventProcessor(ProcessLogEntry);
        try
        {
            var handle = await stream.SubscribeAsync(receiver.Observer);
            await stream.OnNextAsync(271828); // Even number, so the filter would deliver it

            await WaitUntilAsync(_ => Task.FromResult(tenantAwareApiNotUsedErrorIsLogged || receiver.Received.Count > 0), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(0.1));
            await handle.UnsubscribeAsync();
        }
        finally
        {
            _ = ProcessingLogger.Instance.RemoveLogEventProcessor(processorId);
        }

        Assert.True(tenantAwareApiNotUsedErrorIsLogged);
        Assert.Empty(receiver.Received);
        Assert.DoesNotContain(EvenNumbersStreamFilter.Calls, call => call.StreamId == stream.StreamId);

        void ProcessLogEntry(Microsoft.Extensions.Logging.LogLevel level, Exception? exception, string message)
        {
            if (level == Microsoft.Extensions.Logging.LogLevel.Error
                && exception is null
                && message.Contains(" event 271828 of type System.Int32 was not sent with the tenant aware API", StringComparison.Ordinal))
            {
                tenantAwareApiNotUsedErrorIsLogged = true;
            }
        }
    }

    [Fact]
    public void StreamFilter_RegisteredAfterAddMultitenantStreams_FailsSiloStartup()
    {
        var misconfiguredCluster = new TestClusterBuilder(1).AddSiloBuilderConfigurator<LateStreamFilterSiloConfigurator>().Build();
        try
        {
            var exception = Assert.ThrowsAny<Exception>(misconfiguredCluster.Deploy);

            var configurationException = Assert.Single(Flatten(exception).OfType<OrleansConfigurationException>());
            Assert.StartsWith(
                $"Stream provider '{ClusterFixture.StreamProviderName}' was added with AddMultitenantStreams, but stream filter {typeof(EvenNumbersStreamFilter).FullName} was registered for it afterwards.",
                configurationException.Message,
                StringComparison.Ordinal);
        }
        finally
        {
            misconfiguredCluster.Dispose();
        }

        static IEnumerable<Exception> Flatten(Exception exception)
         => exception is AggregateException aggregate ? aggregate.InnerExceptions.SelectMany(Flatten)
          : exception.InnerException is { } inner ? Flatten(inner).Prepend(exception)
          : [exception];
    }

    public sealed class ClusterFixture : IDisposable
    {
        internal const string StreamProviderName = "FilteredStreamProvider";

        public ClusterFixture()
        {
            var builder = new TestClusterBuilder()
                .AddSiloBuilderConfigurator<SiloConfigurator>()
                .AddClientBuilderConfigurator<ClientConfigurator>();

            Cluster = builder.Build();
            Cluster.Deploy();
        }

        public void Dispose() => Cluster.StopAllSilos();

        public TestCluster Cluster { get; }

        sealed class SiloConfigurator : ISiloConfigurator
        {
            public void Configure(ISiloBuilder siloBuilder) => siloBuilder
                .ConfigureLogging(l => l.AddProcessing())
                .AddMultitenantGrainStorageAsDefault<
                    MemoryGrainStorage,
                    MemoryGrainStorageOptions,
                    MemoryGrainStorageOptionsValidator>((siloBuilder, name) => siloBuilder.AddMemoryGrainStorage(name))
                .AddMultitenantStreams(
                    StreamProviderName, (silo, name) => silo
                    .AddMemoryStreams<DefaultMemoryMessageBodySerializer>(name)
                    .AddMemoryGrainStorage(name)
                    .AddStreamFilter<EvenNumbersStreamFilter>(name));
        }

        sealed class ClientConfigurator : IClientBuilderConfigurator
        {
            public void Configure(IConfiguration configuration, IClientBuilder clientBuilder) => clientBuilder
                .AddMemoryStreams<DefaultMemoryMessageBodySerializer>(StreamProviderName);
        }
    }

    sealed class LateStreamFilterSiloConfigurator : ISiloConfigurator
    {
        public void Configure(ISiloBuilder siloBuilder) => siloBuilder
            .AddMultitenantStreams(
                ClusterFixture.StreamProviderName, (silo, name) => silo
                .AddMemoryStreams<DefaultMemoryMessageBodySerializer>(name)
                .AddMemoryGrainStorage(name))
            .AddStreamFilter<EvenNumbersStreamFilter>(ClusterFixture.StreamProviderName);
    }
}
