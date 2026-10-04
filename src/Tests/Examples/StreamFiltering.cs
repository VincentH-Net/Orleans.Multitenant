using System.Collections.Concurrent;
using Orleans.Runtime;
using Orleans.Streams.Filtering;

namespace OrleansMultitenant.Tests.Examples.StreamFiltering;

/// <summary>A regular (tenant unaware) Orleans stream filter, which only delivers even numbers</summary>
sealed class EvenNumbersStreamFilter : IStreamFilter
{
    /// <remarks>static can be used to access the same object instances in silo's and tests, because <see cref="Orleans.TestingHost.TestCluster"/> uses in-process silo's</remarks>
    internal static ConcurrentQueue<(StreamId StreamId, object Item, string FilterData)> Calls { get; } = new();

    public bool ShouldDeliver(StreamId streamId, object item, string filterData)
    {
        Calls.Enqueue((streamId, item, filterData));
        return item is int number && number % 2 == 0;
    }
}
