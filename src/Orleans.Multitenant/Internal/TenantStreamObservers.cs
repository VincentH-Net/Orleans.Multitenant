using Orleans.Streams;

namespace Orleans.Multitenant.Internal;

readonly struct TenantStreamObserver<T> : IAsyncObserver<TenantEvent<T>>
{
    readonly IAsyncObserver<T> observer;
    internal TenantStreamObserver(IAsyncObserver<T> observer) => this.observer = observer;
    public Task OnCompletedAsync() => observer.OnCompletedAsync();
    public Task OnErrorAsync(Exception ex) => observer.OnErrorAsync(ex);
    public Task OnNextAsync(TenantEvent<T> item, StreamSequenceToken? token = null) => observer.OnNextAsync(item.Event, token);
}

readonly struct TenantStreamBatchObserver<T> : IAsyncBatchObserver<TenantEvent<T>>
{
    readonly IAsyncBatchObserver<T> observer;
    internal TenantStreamBatchObserver(IAsyncBatchObserver<T> observer) => this.observer = observer;
    public Task OnCompletedAsync() => observer.OnCompletedAsync();
    public Task OnErrorAsync(Exception ex) => observer.OnErrorAsync(ex);
    public Task OnNextAsync(IList<SequentialItem<TenantEvent<T>>> items) => observer.OnNextAsync(items.Events());
}

static class TenantEventExtensions
{
    internal static IList<SequentialItem<T>> Events<T>(this IList<SequentialItem<TenantEvent<T>>> items)
    => [.. items.Select(item => new SequentialItem<T>(item.Item.Event, item.Token))];
}
