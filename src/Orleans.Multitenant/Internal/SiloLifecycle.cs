using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using Microsoft.Extensions.Logging;

namespace Orleans.Multitenant.Internal;

// Orleans does not allow to add subscribers for a lifecycle stage after that stage has been started,
// so we need to simulate the lifecycle start for the wrapped provider: record the start events and replay the recorded start events later,
// directy after we call Participate (since in Participate the subscriptions are registered by the wrapped provider)
// Stop events are immediately forwarded to the wrapped providers.

interface IRepeatedSiloLifecycleObserver
{
    Task OnStop(int lifecycleIndex, CancellationToken ct);
}

interface IRepeatedSiloLifecycleObservable
{
    LifecycleStartupRecording GetStartupRecording();
    void SubscribeStopEvents(IRepeatedSiloLifecycleObserver observer);
}

sealed record LifecycleStartupRecording(int HighestCompletedStageOnParticipate, int LowestStoppedStageOnParticipate, ReadOnlyCollection<LifecycleStartEventRecord> Events, ISiloLifecycle Lifecycle);

readonly record struct LifecycleStartEventRecord(int LifecycleIndex, int HighestCompletedStage, int LowestStoppedStage);

sealed class SiloLifecycleRepeater : IRepeatedSiloLifecycleObservable
{
    internal static int[] AllServiceLifecycleStages => field ??=
        [.. typeof(ServiceLifecycleStage).GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static).Where(fi => fi.FieldType == typeof(int))
        .Select(fi => (int)(fi.GetValue(null) ?? throw new InvalidCastException("static int field cannot have value null"))).OrderBy(value => value)];

    readonly ISiloLifecycle realSiloLifecycle;
    readonly ILogger<MultitenantStorage> logger;
    readonly int highestCompletedStageOnParticipate, lowestStoppedStageOnParticipate;
    readonly List<LifecycleStartEventRecord> startupRecording = [];

    internal SiloLifecycleRepeater(ISiloLifecycle realSiloLifecycleOnParticipate, ILogger<MultitenantStorage> logger)
    {
        realSiloLifecycle = realSiloLifecycleOnParticipate;
        this.logger = logger;
        highestCompletedStageOnParticipate = realSiloLifecycleOnParticipate.HighestCompletedStage;
        lowestStoppedStageOnParticipate = realSiloLifecycleOnParticipate.LowestStoppedStage;
        SubscribeToAllServiceLifeCycleStages();
    }

    public LifecycleStartupRecording GetStartupRecording() => new(highestCompletedStageOnParticipate, lowestStoppedStageOnParticipate, startupRecording.AsReadOnly(), realSiloLifecycle);

    public void SubscribeStopEvents(IRepeatedSiloLifecycleObserver observer) => onStopObservers.Add(observer);

    readonly ConcurrentBag<IRepeatedSiloLifecycleObserver> onStopObservers = [];

    void SubscribeToAllServiceLifeCycleStages()
    {
        for (int i = 0; i < AllServiceLifecycleStages.Length - 1; i++)
        {
            int lifecycleIndex = i;
            int firstStage = AllServiceLifecycleStages[lifecycleIndex];
            int lastStage = AllServiceLifecycleStages[lifecycleIndex + 1] - 1;

            _ = realSiloLifecycle.Subscribe(
                $"{GetType().FullName} observer for stages {firstStage}..{lastStage - 1}",
                firstStage,
                ct =>
                {
                    logger.RecordingSiloLifecycleStart(realSiloLifecycle.HighestCompletedStage, firstStage, lastStage);
                    startupRecording.Add(new(lifecycleIndex, realSiloLifecycle.HighestCompletedStage, realSiloLifecycle.LowestStoppedStage));
                    return Task.CompletedTask;
                },
                ct =>
                {
                    logger.ForwardingSiloLifecycleStop(realSiloLifecycle.LowestStoppedStage, onStopObservers.Count, firstStage, lastStage);
                    return Task.WhenAll(onStopObservers.Select(observer => observer.OnStop(lifecycleIndex, ct)));
                }
            );
        }
    }
}

sealed class SiloLifecycleSimulator : ISiloLifecycle, IRepeatedSiloLifecycleObserver
{
    readonly ILogger<MultitenantStorage> logger;
    readonly LifecycleStartupRecording startHistory;
    readonly List<Subscription> subscriptions = [];
    bool stopping;

    internal SiloLifecycleSimulator(IRepeatedSiloLifecycleObservable lifecycle, ILogger<MultitenantStorage> logger)
    {
        this.logger = logger;
        startHistory = lifecycle.GetStartupRecording();
        HighestCompletedStage = startHistory.HighestCompletedStageOnParticipate;
        LowestStoppedStage = startHistory.LowestStoppedStageOnParticipate;
        lifecycle.SubscribeStopEvents(this);
    }

    public int HighestCompletedStage { get; private set; }

    public int LowestStoppedStage { get; private set; }

    public async Task ReplayOnStartHistory(CancellationToken ct)
    {
        foreach (var onStartRecord in startHistory.Events)
        {
            if (stopping) return;
            HighestCompletedStage = onStartRecord.HighestCompletedStage;
            LowestStoppedStage = onStartRecord.LowestStoppedStage;
            await OnStart(onStartRecord.LifecycleIndex, ct).ConfigureAwait(false);
        }
    }

    public IDisposable Subscribe(string observerName, int stage, ILifecycleObserver observer)
    {
        Subscription subscription = new(stage, observer);
        subscriptions.Add(subscription);
        return subscription;
    }

    async Task OnStart(int lifecycleIndex, CancellationToken ct)
    {
        int firstStage = SiloLifecycleRepeater.AllServiceLifecycleStages[lifecycleIndex];
        int lastStage = SiloLifecycleRepeater.AllServiceLifecycleStages[lifecycleIndex + 1] - 1;

        foreach (var subscriptionsForStage in subscriptions
            .Where(s => firstStage <= s.Stage && s.Stage <= lastStage)
            .GroupBy(s => s.Stage)
            .OrderBy(g => g.Key))
        {
            int stage = subscriptionsForStage.Key;
            logger.ReplayingSiloLifecycleStartForTenant(subscriptionsForStage.Count(), stage);
            await Task.WhenAll([.. subscriptionsForStage.Select(s => s.Observer.OnStart(ct))]).ConfigureAwait(false);
            HighestCompletedStage = stage;
        }
    }

    public async Task OnStop(int lifecycleIndex, CancellationToken ct)
    {
        stopping = true;
        int firstStage = SiloLifecycleRepeater.AllServiceLifecycleStages[lifecycleIndex];
        int lastStage = SiloLifecycleRepeater.AllServiceLifecycleStages[lifecycleIndex + 1] - 1;

        foreach (var subscriptionsForStage in subscriptions
            .Where(s => firstStage <= s.Stage && s.Stage <= lastStage)
            .GroupBy(s => s.Stage)
            .OrderByDescending(g => g.Key))
        {
            int stage = subscriptionsForStage.Key;
            logger.ForwardingSiloLifecycleStopForTenant(subscriptionsForStage.Count(), stage);
            await Task.WhenAll([.. subscriptionsForStage.Select(s => s.Observer.OnStop(ct))]).ConfigureAwait(false);
        }
    }

    sealed class Subscription(int stage, ILifecycleObserver observer) : IDisposable
    {
        public int Stage { get; } = stage;
        public ILifecycleObserver Observer { get; } = observer;

        public void Dispose() { }
    }
}
