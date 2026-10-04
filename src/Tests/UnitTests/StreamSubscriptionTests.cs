using Microsoft.Extensions.DependencyInjection;
using Orleans.Serialization;
using Orleans.Storage;
using Orleans.Streams;
using Orleans.TestingHost;
using OrleansMultitenant.Tests.Examples.StreamSubscriptions;

namespace OrleansMultitenant.Tests.UnitTests;

[Collection(MultiPurposeCluster.Name)]
public class StreamSubscriptionTests(ClusterFixture fixture)
{
    const string ClientNamespace = "ClientSubscriptionNamespace";
    static readonly StreamSequenceToken NoToken = null!;

    static readonly Dictionary<string, Func<TenantStream<int>, StreamReceiver, Task<StreamSubscriptionHandle<int>>>> SubscribeOverloads = new()
    {
        ["observer"                            ] = (stream, receiver) => stream.SubscribeAsync(receiver.Observer),
        ["observer, token, filterData"         ] = (stream, receiver) => stream.SubscribeAsync(receiver.Observer, null, "filterData"),
        ["batchObserver"                       ] = (stream, receiver) => stream.SubscribeAsync(receiver.BatchObserver),
        ["batchObserver, token"                ] = (stream, receiver) => stream.SubscribeAsync(receiver.BatchObserver, null),
        ["observer, startPosition, filterData" ] = (stream, receiver) => stream.SubscribeAsync(receiver.Observer, StreamSubscriptionStartPosition.Latest, "filterData"),
        ["batchObserver, startPosition"        ] = (stream, receiver) => stream.SubscribeAsync(receiver.BatchObserver, StreamSubscriptionStartPosition.Latest),
        ["onNext, onError, onCompleted"        ] = (stream, receiver) => stream.SubscribeAsync(receiver.OnNext, StreamReceiver.OnError, StreamReceiver.OnCompleted),
        ["onNext, onError"                     ] = (stream, receiver) => stream.SubscribeAsync(receiver.OnNext, StreamReceiver.OnError),
        ["onNext, onCompleted"                 ] = (stream, receiver) => stream.SubscribeAsync(receiver.OnNext, StreamReceiver.OnCompleted),
        ["onNext"                              ] = (stream, receiver) => stream.SubscribeAsync(receiver.OnNext),
        ["onNext, onError, onCompleted, token" ] = (stream, receiver) => stream.SubscribeAsync(receiver.OnNext, StreamReceiver.OnError, StreamReceiver.OnCompleted, NoToken),
        ["onNext, onError, token"              ] = (stream, receiver) => stream.SubscribeAsync(receiver.OnNext, StreamReceiver.OnError, NoToken),
        ["onNext, onCompleted, token"          ] = (stream, receiver) => stream.SubscribeAsync(receiver.OnNext, StreamReceiver.OnCompleted, NoToken),
        ["onNext, token"                       ] = (stream, receiver) => stream.SubscribeAsync(receiver.OnNext, NoToken),
        ["onNextBatch, onError, onCompleted"   ] = (stream, receiver) => stream.SubscribeAsync(receiver.OnNextBatch, StreamReceiver.OnError, StreamReceiver.OnCompleted),
        ["onNextBatch, onError"                ] = (stream, receiver) => stream.SubscribeAsync(receiver.OnNextBatch, StreamReceiver.OnError),
        ["onNextBatch, onCompleted"            ] = (stream, receiver) => stream.SubscribeAsync(receiver.OnNextBatch, StreamReceiver.OnCompleted),
        ["onNextBatch"                         ] = (stream, receiver) => stream.SubscribeAsync(receiver.OnNextBatch),
    };

    static readonly Dictionary<string, Func<TenantStream<int>, StreamReceiver, StreamSubscriptionStartPosition, Task<StreamSubscriptionHandle<int>>>> StartPositionOverloads = new()
    {
        ["observer, startPosition"             ] = (stream, receiver, startPosition) => stream.SubscribeAsync(receiver.Observer, startPosition),
        ["batchObserver, startPosition"        ] = (stream, receiver, startPosition) => stream.SubscribeAsync(receiver.BatchObserver, startPosition),
    };

    /// <remarks>These are the regular Orleans <see cref="StreamSubscriptionHandle{T}"/> methods and <see cref="StreamSubscriptionHandleExtensions"/></remarks>
    static readonly Dictionary<string, Func<StreamSubscriptionHandle<int>, StreamReceiver, Task<StreamSubscriptionHandle<int>>>> ResumeOverloads = new()
    {
        ["observer"                            ] = (handle, receiver) => handle.ResumeAsync(receiver.Observer),
        ["batchObserver"                       ] = (handle, receiver) => handle.ResumeAsync(receiver.BatchObserver),
        ["onNext, onError, onCompleted"        ] = (handle, receiver) => handle.ResumeAsync(receiver.OnNext, StreamReceiver.OnError, StreamReceiver.OnCompleted),
        ["onNext, onError"                     ] = (handle, receiver) => handle.ResumeAsync(receiver.OnNext, StreamReceiver.OnError),
        ["onNext, onCompleted"                 ] = (handle, receiver) => handle.ResumeAsync(receiver.OnNext, StreamReceiver.OnCompleted),
        ["onNext"                              ] = (handle, receiver) => handle.ResumeAsync(receiver.OnNext),
        ["onNextBatch, onError, onCompleted"   ] = (handle, receiver) => handle.ResumeAsync(receiver.OnNextBatch, StreamReceiver.OnError, StreamReceiver.OnCompleted),
        ["onNextBatch, onError"                ] = (handle, receiver) => handle.ResumeAsync(receiver.OnNextBatch, StreamReceiver.OnError),
        ["onNextBatch, onCompleted"            ] = (handle, receiver) => handle.ResumeAsync(receiver.OnNextBatch, StreamReceiver.OnCompleted),
        ["onNextBatch"                         ] = (handle, receiver) => handle.ResumeAsync(receiver.OnNextBatch),
    };

    readonly Orleans.TestingHost.TestCluster cluster = fixture.Cluster;

    public static TheoryData<string /*overload*/> SubscribeOverloadNames() => new([.. SubscribeOverloads.Keys]);

    public static TheoryData<string /*overload*/> ResumeOverloadNames() => new([.. ResumeOverloads.Keys]);

    public static TheoryData<string /*overload*/> StartPositionOverloadNames() => new([.. StartPositionOverloads.Keys]);

    public static TheoryData<string /*observerKind*/, string /*tenantId*/> ObserverKindsAndTenants()
    {
        TheoryData<string, string> data = [];
        foreach (string observerKind in Enum.GetNames<ObserverKind>())
        {
            data.Add(observerKind, "TenantA");
            data.Add(observerKind, ""); // Verify that subscriptions work with a tenant that has an empty tenant ID
        }
        return data;
    }

    /// <remarks>Tenant ids and keys within a tenant that contain the characters which separate and escape the parts of a tenant specific key</remarks>
    public static TheoryData<string /*tenantId*/, string /*keySuffix*/> TenantsAndKeySuffixesWithSeparators() => new()
    {
        { "TenantA", "|K|e||y|" },
        { "Te|antB", "~Key"     },
        { "|"      , "|~Key"    },
        { "A|~|"   , "||"       },
    };

    [Theory]
    [MemberData(nameof(SubscribeOverloadNames))]
    public async Task SubscribeAsync_WithEachOverload_ReceivesEventsAndBatches(string overload)
    {
        var stream = GetTenantStream("TenantA", ClientNamespace, ThisTestMethodId(overload));
        StreamReceiver receiver = new();

        var handle = await SubscribeOverloads[overload](stream, receiver);
        await stream.OnNextAsync(1);
        await stream.OnNextBatchAsync([2, 3]);

        Assert.Equal([1, 2, 3], await receiver.ReceiveAsync(3));
        await handle.UnsubscribeAsync();
    }

    [Theory]
    [MemberData(nameof(StartPositionOverloadNames))]
    public async Task SubscribeAsync_WithStartPosition_ReceivesEventsFromThatPosition(string overload)
    {
        var stream = GetTenantStream("TenantA", ClientNamespace, ThisTestMethodId(overload));
        StreamReceiver firstReceiver = new(), latestReceiver = new(), earliestReceiver = new();
        var firstHandle = await stream.SubscribeAsync(firstReceiver.Observer);
        await stream.OnNextAsync(1);
        await stream.OnNextAsync(2);
        Assert.Equal([1, 2], await firstReceiver.ReceiveAsync(2)); // These events are now retained by the stream provider

        var latestHandle = await StartPositionOverloads[overload](stream, latestReceiver, StreamSubscriptionStartPosition.Latest);
        var earliestHandle = await StartPositionOverloads[overload](stream, earliestReceiver, StreamSubscriptionStartPosition.EarliestAvailable);
        await stream.OnNextAsync(3);

        Assert.Equal([1, 2, 3], await earliestReceiver.ReceiveAsync(3));
        Assert.Equal([3], await latestReceiver.ReceiveAsync(1));
        Assert.Equal([1, 2, 3], await firstReceiver.ReceiveAsync(3));
        await firstHandle.UnsubscribeAsync();
        await latestHandle.UnsubscribeAsync();
        await earliestHandle.UnsubscribeAsync();
    }

    [Fact]
    public async Task UnsubscribeAsync_OnSubscribeHandle_StopsEventDelivery()
    {
        var stream = GetTenantStream("TenantA", ClientNamespace, ThisTestMethodId());
        StreamReceiver receiver = new(), otherReceiver = new();
        var handle = await stream.SubscribeAsync(receiver.Observer);
        var otherHandle = await stream.SubscribeAsync(otherReceiver.Observer);

        await handle.UnsubscribeAsync();
        await stream.OnNextAsync(1);

        Assert.Equal([1], await otherReceiver.ReceiveAsync(1));
        Assert.Empty(receiver.Received);
        Assert.Equal(otherHandle, Assert.Single(await stream.GetAllSubscriptionHandles()));
        await otherHandle.UnsubscribeAsync();
    }

    [Fact]
    public async Task SubscribeAsync_ForTenantStream_ReturnsHandleOfThatStream()
    {
        var stream = GetTenantStream("TenantA", ClientNamespace, ThisTestMethodId());

        var handle = await stream.SubscribeAsync(new StreamReceiver().Observer);

        Assert.Equal(stream.StreamId, handle.StreamId);
        Assert.Equal("TenantA", handle.StreamId.GetTenantId());
        Assert.Equal(ClusterFixture.TenantAwareStreamProviderName, handle.ProviderName);
        Assert.NotEqual(Guid.Empty, handle.HandleId);
        Assert.Contains(handle.HandleId.ToString(), handle.ToString(), StringComparison.Ordinal);
        await handle.UnsubscribeAsync();
    }

    [Fact]
    public async Task GetAllSubscriptionHandles_ForStreamWithSubscriptions_ReturnsHandlesEqualToSubscribeHandles()
    {
        var stream = GetTenantStream("TenantA", ClientNamespace, ThisTestMethodId());
        StreamReceiver receiver = new();
        var handle1 = await stream.SubscribeAsync(receiver.Observer);
        var handle2 = await stream.SubscribeAsync(receiver.BatchObserver);

        var handles = await stream.GetAllSubscriptionHandles();

        Assert.Equal(2, handles.Count);
        var handle1FromGetAll = Assert.Single(handles, handle => handle.HandleId == handle1.HandleId);
        var handle2FromGetAll = Assert.Single(handles, handle => handle.HandleId == handle2.HandleId);
        Assert.True(handle1.Equals(handle1FromGetAll));
        Assert.True(handle1.Equals((object)handle1FromGetAll));
        Assert.Equal(handle1.GetHashCode(), handle1FromGetAll.GetHashCode());
        Assert.True(handle2FromGetAll.Equals(handle2));
        Assert.False(handle1.Equals(handle2));
        Assert.False(handle1.Equals((object)handle2));
        Assert.False(handle1.Equals(null));
        Assert.False(handle1.Equals((object?)null));

        await handle1FromGetAll.UnsubscribeAsync();
        await handle2FromGetAll.UnsubscribeAsync();
        Assert.Empty(await stream.GetAllSubscriptionHandles());
    }

    [Fact]
    public async Task GetAllSubscriptionHandles_ForStreamWithoutSubscriptions_ReturnsNoHandles()
     => Assert.Empty(await GetTenantStream("TenantA", ClientNamespace, ThisTestMethodId()).GetAllSubscriptionHandles());

    [Fact]
    public async Task Equals_ForHandleOfTenantUnawareStream_ReturnsFalse()
    {
        var streamId = StreamId.Create(ClientNamespace, ThisTestMethodId());
        StreamReceiver receiver = new();
        var handle = await GetTenantStream("TenantA", streamId).SubscribeAsync(receiver.Observer);
        var tenantUnawareHandle = await cluster.Client.GetStreamProvider(ClusterFixture.TenantUnawareStreamProviderName).GetStream<int>(streamId).SubscribeAsync(receiver.Observer);

        Assert.False(handle.Equals(tenantUnawareHandle));
        Assert.False(tenantUnawareHandle.Equals(handle));

        await handle.UnsubscribeAsync();
        await tenantUnawareHandle.UnsubscribeAsync();
    }

    [Theory]
    [MemberData(nameof(ResumeOverloadNames))]
    public async Task ResumeAsync_WithEachOrleansOverload_ReplacesObserverOfSubscription(string overload)
    {
        var stream = GetTenantStream("TenantA", ClientNamespace, ThisTestMethodId(overload));
        StreamReceiver originalReceiver = new(), resumedReceiver = new();
        var subscribeHandle = await stream.SubscribeAsync(originalReceiver.Observer);
        await stream.OnNextAsync(1);
        Assert.Equal([1], await originalReceiver.ReceiveAsync(1));
        var handle = Assert.Single(await stream.GetAllSubscriptionHandles());

        var resumedHandle = await ResumeOverloads[overload](handle, resumedReceiver);
        await stream.OnNextAsync(2);
        await stream.OnNextBatchAsync([3, 4]);

        Assert.Equal([2, 3, 4], await resumedReceiver.ReceiveAsync(3));
        Assert.Equal([1], originalReceiver.Received);
        Assert.Equal(subscribeHandle, resumedHandle);
        Assert.Equal(resumedHandle, Assert.Single(await stream.GetAllSubscriptionHandles()));
        await resumedHandle.UnsubscribeAsync();
    }

    [Fact]
    public async Task ResumeAsync_OnHandleThatWasUsedToResume_ThrowsLikeOrleansHandle()
    {
        var stream = GetTenantStream("TenantA", ClientNamespace, ThisTestMethodId());
        StreamReceiver receiver = new();
        var handle = await stream.SubscribeAsync(receiver.Observer);
        var resumedHandle = await handle.ResumeAsync(receiver.OnNext);

        _ = await Assert.ThrowsAsync<InvalidOperationException>(() => handle.ResumeAsync(receiver.OnNext));
        _ = await Assert.ThrowsAsync<InvalidOperationException>(handle.UnsubscribeAsync);

        await resumedHandle.UnsubscribeAsync();
    }

    [Fact]
    public async Task StreamSubscriptionHandle_WhenSerializedOrCopied_RepresentsSameSubscription()
    {
        var stream = GetTenantStream("TenantA", ClientNamespace, ThisTestMethodId());
        var handle = await stream.SubscribeAsync(new StreamReceiver().Observer);
        var serializer = cluster.Client.ServiceProvider.GetRequiredService<Serializer>();
        var copier = cluster.Client.ServiceProvider.GetRequiredService<DeepCopier>();

        var grainStorageSerializer = Assert.IsType<InProcessSiloHandle>(cluster.Primary).SiloHost.Services.GetRequiredService<IGrainStorageSerializer>(); // The default serializer for grain state

        var deserializedHandle = serializer.Deserialize<StreamSubscriptionHandle<int>>(serializer.SerializeToArray(handle));
        var copiedHandle = copier.Copy(handle);
        var storedHandle = grainStorageSerializer.Deserialize<StreamSubscriptionHandle<int>>(grainStorageSerializer.Serialize(handle));

        Assert.NotNull(deserializedHandle);
        foreach (var roundtrippedHandle in (StreamSubscriptionHandle<int>?[])[deserializedHandle, copiedHandle, storedHandle])
        {
            Assert.NotNull(roundtrippedHandle);
            Assert.Equal(handle, roundtrippedHandle);
            Assert.Equal(handle.HandleId, roundtrippedHandle.HandleId);
            Assert.Equal(handle.StreamId, roundtrippedHandle.StreamId);
            Assert.Equal(handle.ProviderName, roundtrippedHandle.ProviderName);
        }

        await deserializedHandle.UnsubscribeAsync(); // Verify that a deserialized handle can be used
        Assert.Empty(await stream.GetAllSubscriptionHandles());
    }

    [Theory]
    [MemberData(nameof(ObserverKindsAndTenants))]
    public async Task ResumeAsync_InActivationOfExplicitSubscriberGrain_ReceivesEventsAfterReactivation(string observerKind, string tenantId)
    {
        string key = $"{observerKind}_{ThisTestMethodId(tenantId)}";
        var subscriber = Factory.ForTenant(tenantId).GetGrain<IResumingSubscriberGrain>(key);
        var stream = GetTenantStream(tenantId, Constants.ExplicitNamespace, key);
        await subscriber.Subscribe();
        await stream.OnNextAsync(1);
        Assert.Equal([1], await ReceivedAsync(subscriber, 1));
        Assert.Equal(0, await subscriber.GetResumedSubscriptionCount());

        await ReactivateAsync(subscriber);
        await stream.OnNextAsync(2);
        await stream.OnNextBatchAsync([3, 4]);

        Assert.Equal(1, await subscriber.GetResumedSubscriptionCount());
        Assert.Equal([2, 3, 4], await ReceivedAsync(subscriber, 3));
        Assert.Equal(1, await subscriber.UnsubscribeAll());
        Assert.Equal(0, await subscriber.UnsubscribeAll());
    }

    [Theory]
    [MemberData(nameof(ObserverKindsAndTenants))]
    public async Task ResumeAsync_OnHandleStoredInGrainState_ReceivesEventsAfterReactivation(string observerKind, string tenantId)
    {
        string key = $"{observerKind}_{ThisTestMethodId(tenantId)}";
        var subscriber = Factory.ForTenant(tenantId).GetGrain<IStoringSubscriberGrain>(key);
        var stream = GetTenantStream(tenantId, Constants.StoredNamespace, key);
        await subscriber.Subscribe();
        await stream.OnNextAsync(1);
        Assert.Equal([1], await ReceivedAsync(subscriber, 1));
        Assert.False(await subscriber.GetIsResumedFromStoredHandle());

        await ReactivateAsync(subscriber);
        await stream.OnNextAsync(2);

        Assert.True(await subscriber.GetIsResumedFromStoredHandle());
        Assert.Equal([2], await ReceivedAsync(subscriber, 1));
        Assert.Equal(1, await subscriber.GetSubscriptionCount());
        await subscriber.Unsubscribe();
        Assert.Equal(0, await subscriber.GetSubscriptionCount());
    }

    [Theory]
    [MemberData(nameof(ObserverKindsAndTenants))]
    public async Task CreateTenantHandle_InOnSubscribedOfImplicitSubscriberGrain_ReceivesEventsInEachActivation(string observerKind, string tenantId)
    {
        string key = $"{observerKind}_{ThisTestMethodId(tenantId)}";
        var subscriber = Factory.ForTenant(tenantId).GetGrain<IObservingImplicitSubscriberGrain>(key);
        var stream = GetTenantStream(tenantId, Constants.ImplicitNamespace, key);

        await stream.OnNextAsync(1);
        await stream.OnNextBatchAsync([2, 3]);

        Assert.Equal([1, 2, 3], await ReceivedAsync(subscriber, 3));
        Assert.Equal(1, await subscriber.GetOnSubscribedCount());

        await ReactivateAsync(subscriber);
        await stream.OnNextAsync(4);

        Assert.Equal([4], await ReceivedAsync(subscriber, 1));
        Assert.Equal(1, await subscriber.GetOnSubscribedCount());
    }

    [Theory]
    [MemberData(nameof(TenantsAndKeySuffixesWithSeparators))]
    public async Task ResumeAsync_ForTenantIdAndKeyWithSeparators_ReceivesEventsAfterReactivation(string tenantId, string keySuffix)
    {
        string key = $"{nameof(ObserverKind.Delegate)}_{ThisTestMethodId()}{keySuffix}";
        var subscriber = Factory.ForTenant(tenantId).GetGrain<IResumingSubscriberGrain>(key);
        var stream = GetTenantStream(tenantId, Constants.ExplicitNamespace, key);
        await subscriber.Subscribe();

        await ReactivateAsync(subscriber);
        await stream.OnNextAsync(1);

        Assert.Equal([1], await ReceivedAsync(subscriber, 1));
        Assert.Equal(1, await subscriber.UnsubscribeAll());
    }

    [Theory]
    [MemberData(nameof(TenantsAndKeySuffixesWithSeparators))]
    public async Task ImplicitSubscription_ForTenantIdAndKeyWithSeparators_ActivatesGrainWithSameTenantIdAndKey(string tenantId, string keySuffix)
    {
        string key = $"{nameof(ObserverKind.Observer)}_{ThisTestMethodId()}{keySuffix}";
        var subscriber = Factory.ForTenant(tenantId).GetGrain<IObservingImplicitSubscriberGrain>(key);
        var stream = GetTenantStream(tenantId, Constants.ImplicitNamespace, key);

        await stream.OnNextAsync(1);

        Assert.Equal(subscriber.GetPrimaryKeyString(), stream.StreamId.GetKeyAsString());
        Assert.Equal([1], await ReceivedAsync(subscriber, 1));
        Assert.Equal(1, await subscriber.GetOnSubscribedCount());
    }

    [Fact]
    public void CreateTenantHandle_ForNullHandleFactory_ThrowsArgumentNullException()
     => Assert.Throws<ArgumentNullException>("handleFactory", () => StreamSubscriptionHandleFactoryExtensions.CreateTenantHandle<int>(null!));

    static async Task<List<int>> ReceivedAsync(ISubscriberGrain subscriber, int count)
    {
        List<int> received = [];
        await WaitUntilAsync(async _ =>
        {
            received = await subscriber.GetReceived();
            return received.Count >= count;
        }, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(0.1));
        return received;
    }

    /// <summary>Deactivate the grain and wait until it has a new activation</summary>
    static async Task ReactivateAsync(ISubscriberGrain subscriber)
    {
        var activationId = await subscriber.GetActivationId();
        await subscriber.Deactivate();
        await WaitUntilAsync(async _ => await subscriber.GetActivationId() != activationId, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(0.1));
        Assert.NotEqual(activationId, await subscriber.GetActivationId());
    }

    TenantStream<int> GetTenantStream(string tenantId, string @namespace, string keyWithinTenant)
     => cluster.Client.GetTenantStreamProvider(ClusterFixture.TenantAwareStreamProviderName, tenantId).GetStream<int>(@namespace, keyWithinTenant);

    TenantStream<int> GetTenantStream(string tenantId, StreamId streamId)
     => cluster.Client.GetTenantStreamProvider(ClusterFixture.TenantAwareStreamProviderName, tenantId).GetStream<int>(streamId);

    IGrainFactory Factory => cluster.GrainFactory;
}
