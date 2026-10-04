using Orleans.Streams;
using Orleans.Streams.Core;

namespace OrleansMultitenant.Tests.Examples.StreamSubscriptions;

static class Constants
{
    internal const string ExplicitNamespace = "ExplicitSubscriptionNamespace";
    internal const string StoredNamespace = "StoredSubscriptionNamespace";
    internal const string ImplicitNamespace = "ImplicitSubscriptionNamespace";
}

/// <summary>The ways in which the Orleans streaming API lets a subscriber observe stream events</summary>
enum ObserverKind { Observer, BatchObserver, Delegate, BatchDelegate }

interface ISubscriberGrain : IGrainWithStringKey
{
    Task<List<int>> GetReceived();
    Task<Guid> GetActivationId();
    Task Deactivate();
}

interface IResumingSubscriberGrain : ISubscriberGrain
{
    Task Subscribe();
    Task<int> GetResumedSubscriptionCount();
    Task<int> UnsubscribeAll();
}

interface IStoringSubscriberGrain : ISubscriberGrain
{
    Task Subscribe();
    Task<bool> GetIsResumedFromStoredHandle();
    Task Unsubscribe();
    Task<int> GetSubscriptionCount();
}

interface IObservingImplicitSubscriberGrain : ISubscriberGrain
{
    Task<int> GetOnSubscribedCount();
}

/// <remarks>
/// Apart from how the stream and the implicit subscription handle are obtained, the subscriber grains use the regular Orleans streaming API:
/// <see cref="StreamSubscriptionHandle{T}"/> and <see cref="StreamSubscriptionHandleExtensions"/> in terms of the stream element type
/// </remarks>
abstract class SubscriberGrain : Grain, ISubscriberGrain, IAsyncObserver<int>, IAsyncBatchObserver<int>
{
    readonly Guid activationId = Guid.NewGuid();
    readonly List<int> received = [];

    /// <remarks>The key within the tenant starts with the <see cref="ObserverKind"/>, followed by an underscore</remarks>
    protected ObserverKind ObserverKind => Enum.Parse<ObserverKind>(this.GetKeyWithinTenant().Split('_')[0]);

    protected Task<StreamSubscriptionHandle<int>> Resume(StreamSubscriptionHandle<int> handle) => ObserverKind switch
    {
        ObserverKind.Observer      => handle.ResumeAsync((IAsyncObserver<int>)this),
        ObserverKind.BatchObserver => handle.ResumeAsync((IAsyncBatchObserver<int>)this),
        ObserverKind.Delegate      => handle.ResumeAsync(OnNext),
        ObserverKind.BatchDelegate => handle.ResumeAsync(OnNextBatch),
        _ => throw new NotSupportedException(ObserverKind.ToString())
    };

    protected Task OnNext(int item, StreamSequenceToken? _)
    {
        received.Add(item);
        return Task.CompletedTask;
    }

    protected Task OnNextBatch(IList<SequentialItem<int>> items)
    {
        received.AddRange(items.Select(item => item.Item));
        return Task.CompletedTask;
    }

    public Task OnNextAsync(int item, StreamSequenceToken? token = null) => OnNext(item, token);

    public Task OnNextAsync(IList<SequentialItem<int>> items) => OnNextBatch(items);

    public Task OnErrorAsync(Exception ex) => Task.CompletedTask;

    public Task OnCompletedAsync() => Task.CompletedTask;

    public Task<List<int>> GetReceived() => Task.FromResult(received.ToList());

    public Task<Guid> GetActivationId() => Task.FromResult(activationId);

    public Task Deactivate()
    {
        DeactivateOnIdle();
        return Task.CompletedTask;
    }
}

/// <summary>Explicit subscriber that resumes its subscriptions when it is activated, as prescribed for Orleans explicit stream subscriptions</summary>
sealed class ResumingSubscriberGrain : SubscriberGrain, IResumingSubscriberGrain
{
    int resumedSubscriptionCount;

    TenantStream<int> Stream => this.GetTenantStreamProvider(ClusterFixture.TenantAwareStreamProviderName).GetStream<int>(Constants.ExplicitNamespace, this.GetKeyWithinTenant());

    public override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        foreach (var handle in await Stream.GetAllSubscriptionHandles())
        {
            _ = await Resume(handle);
            resumedSubscriptionCount++;
        }
    }

    public async Task Subscribe() => _ = await (ObserverKind switch
    {
        ObserverKind.Observer      => Stream.SubscribeAsync((IAsyncObserver<int>)this),
        ObserverKind.BatchObserver => Stream.SubscribeAsync((IAsyncBatchObserver<int>)this),
        ObserverKind.Delegate      => Stream.SubscribeAsync(OnNext),
        ObserverKind.BatchDelegate => Stream.SubscribeAsync(OnNextBatch),
        _ => throw new NotSupportedException(ObserverKind.ToString())
    });

    public Task<int> GetResumedSubscriptionCount() => Task.FromResult(resumedSubscriptionCount);

    public async Task<int> UnsubscribeAll()
    {
        var handles = await Stream.GetAllSubscriptionHandles();
        foreach (var handle in handles)
            await handle.UnsubscribeAsync();
        return handles.Count;
    }
}

[GenerateSerializer]
sealed class StoredSubscription
{
    [Id(0)] public StreamSubscriptionHandle<int>? Handle { get; set; }
}

/// <summary>Explicit subscriber that stores its subscription handle in grain state, and resumes that handle when it is activated</summary>
sealed class StoringSubscriberGrain([PersistentState("subscription")] IPersistentState<StoredSubscription> subscription) : SubscriberGrain, IStoringSubscriberGrain
{
    bool isResumedFromStoredHandle;

    TenantStream<int> Stream => this.GetTenantStreamProvider(ClusterFixture.TenantAwareStreamProviderName).GetStream<int>(Constants.StoredNamespace, this.GetKeyWithinTenant());

    public override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        if (subscription.State.Handle is { } storedHandle)
        {
            _ = await Resume(storedHandle);
            isResumedFromStoredHandle = true;
        }
    }

    public async Task Subscribe()
    {
        subscription.State.Handle = await Stream.SubscribeAsync(OnNext);
        await subscription.WriteStateAsync();
    }

    public Task<bool> GetIsResumedFromStoredHandle() => Task.FromResult(isResumedFromStoredHandle);

    public async Task Unsubscribe()
    {
        await subscription.ReadStateAsync();
        await subscription.State.Handle!.UnsubscribeAsync();
        await subscription.ClearStateAsync();
    }

    public async Task<int> GetSubscriptionCount() => (await Stream.GetAllSubscriptionHandles()).Count;
}

/// <summary>Implicit subscriber that attaches its processing logic in <see cref="OnSubscribed"/>, as prescribed for Orleans implicit stream subscriptions</summary>
[ImplicitStreamSubscription(Constants.ImplicitNamespace)]
sealed class ObservingImplicitSubscriberGrain : SubscriberGrain, IObservingImplicitSubscriberGrain, IStreamSubscriptionObserver
{
    int onSubscribedCount;

    public async Task OnSubscribed(IStreamSubscriptionHandleFactory handleFactory)
    {
        onSubscribedCount++;
        _ = await Resume(handleFactory.CreateTenantHandle<int>());
    }

    public Task<int> GetOnSubscribedCount() => Task.FromResult(onSubscribedCount);
}
