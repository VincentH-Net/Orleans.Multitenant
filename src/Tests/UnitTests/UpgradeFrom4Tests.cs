using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using Orleans.Serialization;
using Orleans.Storage;
using Orleans.Streams;
using Orleans.TestingHost;

namespace OrleansMultitenant.Tests.UnitTests;

/// <summary>
/// Verifies the upgrade instructions in the readme for a subscription handle that an application stored in grain state with Orleans.Multitenant 4.x.<br />
/// The grain state in these tests was stored by an application that used Orleans.Multitenant 4.0.0, in a state class with these members:<br />
/// <c>[Id(0)] public string Note { get; set; }</c><br />
/// <c>[Id(1)] public StreamSubscriptionHandle&lt;TenantEvent&lt;int&gt;&gt;? Handle { get; set; }</c>
/// </summary>
[Collection(MultiPurposeCluster.Name)]
public class UpgradeFrom4Tests(ClusterFixture fixture)
{
    const string Namespace = "UpgradeNamespace";
    const string JsonStorageSerializer = "Json";
    const string OrleansStorageSerializer = "Orleans";

    /// <summary>The grain state as 4.0.0 stored it with the default (JSON) grain storage serializer; {StateClass} is the name of the state class</summary>
    const string JsonStateStoredBy4x = """{"$id":"1","$type":"OrleansMultitenant.Tests.UnitTests.{StateClass}, Orleans.Multitenant.Tests","Note":"written by 4.0","Handle":{"$id":"2","$type":"Orleans.Streams.StreamSubscriptionHandleImpl`1[[Orleans.Multitenant.TenantEvent`1[[System.Int32, System.Private.CoreLib]], Orleans.Multitenant]], Orleans.Streaming","streamImpl":{"streamId":{"$id":"3","$type":"Orleans.Runtime.StreamId, Orleans.Streaming","fk":{"$type":"System.Byte[], System.Private.CoreLib","$value":"VXBncmFkZU5hbWVzcGFjZVRlbmFudEF8a2V5"},"ki":16,"fh":725946607},"providerName":"TenantAwareStreamProvider","isRewindable":true},"subscriptionId":{"$id":"4","$type":"Orleans.Runtime.GuidId, Orleans.Core.Abstractions","Guid":"198bc848-32fd-4069-bc04-227aa68ee938"},"ProviderName":"TenantAwareStreamProvider","StreamId":{"$type":"Orleans.Runtime.StreamId, Orleans.Streaming","fk":{"$type":"System.Byte[], System.Private.CoreLib","$value":"VXBncmFkZU5hbWVzcGFjZVRlbmFudEF8a2V5"},"ki":16,"fh":725946607},"HandleId":"198bc848-32fd-4069-bc04-227aa68ee938"}}""";

    /// <summary>The grain state as 4.0.0 stored it with the Orleans grain storage serializer, in base64</summary>
    const string OrleansStateStoredBy4x = "IEAdd3JpdHRlbiBieSA0LjAxAVeOrOYCAk9ybGVhbnMuU3RyZWFtcy5TdHJlYW1TdWJzY3JpcHRpb25IYW5kbGVJbXBsYDFbW09ybGVhbnMuTXVsdGl0ZW5hbnQuVGVuYW50RXZlbnRgMVtbaW50XV0sT3JsZWFucy5NdWx0aXRlbmFudF1dLE9ybGVhbnMuU3RyZWFtaW5n6CAgIEA3VXBncmFkZU5hbWVzcGFjZVRlbmFudEF8a2V5ASFh7xBFK+BBM1RlbmFudEF3YXJlU3RyZWFtUHJvdmlkZXLgAQPgwQEhQCFIyIsZ/TJpQLwEInqmjuk44AED4OA=";

    readonly TestCluster cluster = fixture.Cluster;

    public static TheoryData<string /*grainStorageSerializer*/> GrainStorageSerializers() => new(JsonStorageSerializer, OrleansStorageSerializer);

    public static TheoryData<string /*grainStorageSerializer*/, Type /*exceptionType*/> GrainStorageSerializersAndReadExceptions() => new()
    {
        { JsonStorageSerializer   , typeof(JsonSerializationException) },
        { OrleansStorageSerializer, typeof(InvalidCastException)       },
    };

    [Theory]
    [MemberData(nameof(GrainStorageSerializers))]
    public async Task GrainState_WithHandleStoredBy4x_IsReadWhenHandleMemberHasNewNameAndId(string grainStorageSerializer)
    {
        var serializer = GetGrainStorageSerializer(grainStorageSerializer);

        var state = serializer.Deserialize<SubscriberStateWithRenamedHandle>(GetStateStoredBy4x<SubscriberStateWithRenamedHandle>(grainStorageSerializer));

        Assert.NotNull(state);
        Assert.Equal("written by 4.0", state.Note);
        Assert.Null(state.Subscription);

        // The upgraded state class can store the handle, which the grain gets from GetAllSubscriptionHandles when it is activated
        var stream = cluster.Client.GetTenantStreamProvider(ClusterFixture.TenantAwareStreamProviderName, "TenantA").GetStream<int>(Namespace, ThisTestMethodId(grainStorageSerializer));
        _ = await stream.SubscribeAsync(new StreamReceiver().Observer);
        state.Subscription = Assert.Single(await stream.GetAllSubscriptionHandles());

        var storedState = serializer.Deserialize<SubscriberStateWithRenamedHandle>(serializer.Serialize(state));

        Assert.NotNull(storedState);
        Assert.Equal("written by 4.0", storedState.Note);
        Assert.NotNull(storedState.Subscription);
        Assert.Equal(state.Subscription, storedState.Subscription);
        await storedState.Subscription.UnsubscribeAsync();
        Assert.Empty(await stream.GetAllSubscriptionHandles());
    }

    [Theory]
    [MemberData(nameof(GrainStorageSerializersAndReadExceptions))]
    public void GrainState_WithHandleStoredBy4x_IsNotReadWhenHandleMemberIsOnlyRetyped(string grainStorageSerializer, Type exceptionType)
    {
        var serializer = GetGrainStorageSerializer(grainStorageSerializer);
        var stateStoredBy4x = GetStateStoredBy4x<SubscriberStateWithRetypedHandle>(grainStorageSerializer);

        var exception = Assert.Throws(exceptionType, () => serializer.Deserialize<SubscriberStateWithRetypedHandle>(stateStoredBy4x));

        Assert.Contains("TenantEvent", exception.Message, StringComparison.Ordinal);
    }

    IGrainStorageSerializer GetGrainStorageSerializer(string grainStorageSerializer)
    {
        var services = Assert.IsType<InProcessSiloHandle>(cluster.Primary).SiloHost.Services;
        return grainStorageSerializer == JsonStorageSerializer
            ? services.GetRequiredService<IGrainStorageSerializer>() // The default serializer for grain state
            : new OrleansGrainStorageSerializer(services.GetRequiredService<Serializer>());
    }

    static BinaryData GetStateStoredBy4x<TState>(string grainStorageSerializer) => grainStorageSerializer == JsonStorageSerializer
        ? new(JsonStateStoredBy4x.Replace("{StateClass}", typeof(TState).Name, StringComparison.Ordinal))
        : new(Convert.FromBase64String(OrleansStateStoredBy4x));
}

/// <summary>The state class after the upgrade to 5.0 as the readme instructs: the handle member has a new name and a new id</summary>
[GenerateSerializer]
sealed class SubscriberStateWithRenamedHandle
{
    [Id(0)] public string Note { get; set; } = "";
    [Id(2)] public StreamSubscriptionHandle<int>? Subscription { get; set; }
}

/// <summary>The state class after only the type of the handle member was changed for 5.0; this cannot read the state that 4.x stored</summary>
[GenerateSerializer]
sealed class SubscriberStateWithRetypedHandle
{
    [Id(0)] public string Note { get; set; } = "";
    [Id(1)] public StreamSubscriptionHandle<int>? Handle { get; set; }
}
