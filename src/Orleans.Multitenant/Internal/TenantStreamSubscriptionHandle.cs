using Newtonsoft.Json;
using Orleans.Streams;

namespace Orleans.Multitenant.Internal;

/// <summary>
/// A regular Orleans <see cref="StreamSubscriptionHandle{T}"/> for a subscription to a tenant stream:
/// lets observers receive events of type <typeparamref name="T"/>, while the underlying stream transports <see cref="TenantEvent{T}"/>s
/// </summary>
/// <remarks>
/// The underlying handle is never exposed, to ensure that tenant streams can only be consumed with the tenant aware API.<br />
/// Like the Orleans stream subscription handle, this handle supports both the Orleans serializer and the default (JSON) grain storage serializer, so it can be stored in grain state
/// </remarks>
[GenerateSerializer, Alias("Orleans.Multitenant.TenantStreamSubscriptionHandle`1")]
[JsonObject(MemberSerialization.OptIn)]
[method: JsonConstructor]
sealed class TenantStreamSubscriptionHandle<T>(StreamSubscriptionHandle<TenantEvent<T>> handle) : StreamSubscriptionHandle<T>
{
    [Id(0), JsonProperty] readonly StreamSubscriptionHandle<TenantEvent<T>> handle = handle;

    public override StreamId StreamId => handle.StreamId;

    public override string ProviderName => handle.ProviderName;

    public override Guid HandleId => handle.HandleId;

    public override Task UnsubscribeAsync() => handle.UnsubscribeAsync();

    public override Task<StreamSubscriptionHandle<T>> ResumeAsync(IAsyncObserver<T> observer, StreamSequenceToken? token = null)
    => handle.ResumeAsync(new TenantStreamObserver<T>(observer), token).AsTenantStreamSubscriptionHandle();

    public override Task<StreamSubscriptionHandle<T>> ResumeAsync(IAsyncBatchObserver<T> observer, StreamSequenceToken? token = null)
    => handle.ResumeAsync(new TenantStreamBatchObserver<T>(observer), token).AsTenantStreamSubscriptionHandle();

    public override bool Equals(StreamSubscriptionHandle<T>? other) => other is TenantStreamSubscriptionHandle<T> o && handle.Equals(o.handle);

    public override bool Equals(object? obj) => Equals(obj as StreamSubscriptionHandle<T>);

    public override int GetHashCode() => handle.GetHashCode();

    public override string ToString() => handle.ToString() ?? "";
}

static class TenantStreamSubscriptionHandleExtensions
{
    internal static async Task<StreamSubscriptionHandle<T>> AsTenantStreamSubscriptionHandle<T>(this Task<StreamSubscriptionHandle<TenantEvent<T>>> handle)
    => new TenantStreamSubscriptionHandle<T>(await handle.ConfigureAwait(false));
}
