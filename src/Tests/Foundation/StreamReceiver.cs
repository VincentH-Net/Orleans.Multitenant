using System.Collections.Concurrent;
using Orleans.Streams;

namespace OrleansMultitenant.Tests;

/// <summary>Receives stream events in a test, in any of the ways that the Orleans streaming API offers</summary>
sealed class StreamReceiver
{
    readonly ConcurrentQueue<int> received = new();

    internal IAsyncObserver<int> Observer => new ObserverAdapter(this);

    internal IAsyncBatchObserver<int> BatchObserver => new BatchObserverAdapter(this);

    internal List<int> Received => [.. received];

    internal Task OnNext(int item, StreamSequenceToken? _)
    {
        received.Enqueue(item);
        return Task.CompletedTask;
    }

    internal Task OnNextBatch(IList<SequentialItem<int>> items)
    {
        foreach (var item in items)
            received.Enqueue(item.Item);
        return Task.CompletedTask;
    }

    internal static Task OnError(Exception _) => Task.CompletedTask;

    internal static Task OnCompleted() => Task.CompletedTask;

    /// <summary>Wait until at least <paramref name="count"/> events are received</summary>
    /// <returns>All received events</returns>
    internal async Task<List<int>> ReceiveAsync(int count)
    {
        await WaitUntilAsync(_ => Task.FromResult(received.Count >= count), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(0.05));
        return Received;
    }

    sealed class ObserverAdapter(StreamReceiver receiver) : IAsyncObserver<int>
    {
        public Task OnNextAsync(int item, StreamSequenceToken? token = null) => receiver.OnNext(item, token);
        public Task OnErrorAsync(Exception ex) => OnError(ex);
        public Task OnCompletedAsync() => OnCompleted();
    }

    sealed class BatchObserverAdapter(StreamReceiver receiver) : IAsyncBatchObserver<int>
    {
        public Task OnNextAsync(IList<SequentialItem<int>> items) => receiver.OnNextBatch(items);
        public Task OnErrorAsync(Exception ex) => OnError(ex);
        public Task OnCompletedAsync() => OnCompleted();
    }
}
