using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Configuration;
using Orleans.Providers;
using Orleans.Serialization;
using Orleans.Storage;
using Orleans.Streams;
using Orleans.TestingHost;

namespace OrleansMultitenant.Tests.UnitTests;

/// <summary>
/// Verifies that events in tenant streams keep their content when the stream provider serializes events with the Orleans JSON serializer instead of with the Orleans serializer;
/// an example of such a stream provider is Azure Queue streams with the JSON data adapter
/// </summary>
public sealed class StreamEventSerializationTests(StreamEventSerializationTests.ClusterFixture fixture) : IClassFixture<StreamEventSerializationTests.ClusterFixture>
{
    const string Namespace = "JsonSerializedNamespace";

    readonly TestCluster cluster = fixture.Cluster;

    [Fact]
    public Task TenantStream_OfProviderThatSerializesEventsAsJson_DeliversValueTypeEvents()
     => AssertEventsAreDeliveredAsSentAsync(42, 0, -1);

    [Fact]
    public Task TenantStream_OfProviderThatSerializesEventsAsJson_DeliversStringEvents()
     => AssertEventsAreDeliveredAsSentAsync("first", "", "third");

    [Fact]
    public Task TenantStream_OfProviderThatSerializesEventsAsJson_DeliversObjectEvents()
     => AssertEventsAreDeliveredAsSentAsync(new JsonSerializedEvent("A1", 3), new JsonSerializedEvent("B2", 0), new JsonSerializedEvent("C3", 5));

    async Task AssertEventsAreDeliveredAsSentAsync<T>(T first, T second, T third, [CallerMemberName] string testMethod = "")
    {
        var stream = cluster.Client.GetTenantStreamProvider(ClusterFixture.StreamProviderName, "TenantA").GetStream<T>(Namespace, testMethod);
        ConcurrentQueue<T> received = new();
        var handle = await stream.SubscribeAsync((item, _) =>
        {
            received.Enqueue(item);
            return Task.CompletedTask;
        });

        await stream.OnNextAsync(first);
        await stream.OnNextBatchAsync([second, third]);

        await WaitUntilAsync(_ => Task.FromResult(received.Count >= 3), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(0.05));
        Assert.Equal([first, second, third], received);
        Assert.Contains(JsonMemoryMessageBodySerializer.SerializedBodies, body => body.Contains(typeof(T).FullName!, StringComparison.Ordinal)); // The events were serialized as JSON
        await handle.UnsubscribeAsync();
    }

    public sealed class ClusterFixture : IDisposable
    {
        internal const string StreamProviderName = "JsonSerializingStreamProvider";

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
                .AddMultitenantGrainStorageAsDefault<
                    MemoryGrainStorage,
                    MemoryGrainStorageOptions,
                    MemoryGrainStorageOptionsValidator>((siloBuilder, name) => siloBuilder.AddMemoryGrainStorage(name))
                .AddMultitenantStreams(
                    StreamProviderName, (silo, name) => silo
                    .AddMemoryStreams<JsonMemoryMessageBodySerializer>(name)
                    .AddMemoryGrainStorage(name));
        }

        sealed class ClientConfigurator : IClientBuilderConfigurator
        {
            public void Configure(IConfiguration configuration, IClientBuilder clientBuilder) => clientBuilder
                .AddMemoryStreams<JsonMemoryMessageBodySerializer>(StreamProviderName);
        }
    }
}

/// <summary>An event type that has no Orleans serializer; it can only be serialized as JSON</summary>
sealed record JsonSerializedEvent(string Id, int Quantity);

/// <summary>Serializes the events of a memory stream provider with the Orleans JSON serializer, like e.g. the JSON data adapter of Azure Queue streams does</summary>
[GenerateSerializer]
sealed class JsonMemoryMessageBodySerializer(IServiceProvider services) : IMemoryMessageBodySerializer
{
    [NonSerialized]
    readonly OrleansJsonSerializer serializer = ActivatorUtilities.GetServiceOrCreateInstance<OrleansJsonSerializer>(services);

    /// <remarks>static can be used to access the same object instances in silo's and tests, because <see cref="TestCluster"/> uses in-process silo's</remarks>
    internal static ConcurrentQueue<string> SerializedBodies { get; } = new();

    public ArraySegment<byte> Serialize(MemoryMessageBody body)
    {
        string json = serializer.Serialize(body, typeof(MemoryMessageBody));
        SerializedBodies.Enqueue(json);
        return new(Encoding.UTF8.GetBytes(json));
    }

    public MemoryMessageBody Deserialize(ArraySegment<byte> bodyBytes)
     => (MemoryMessageBody)serializer.Deserialize(typeof(MemoryMessageBody), Encoding.UTF8.GetString(bodyBytes))!;
}
