using System.Collections.Concurrent;
using Orleans.Runtime;
using Orleans.Storage;

namespace OrleansMultitenant.Tests.Examples.StorageProviderParameters;

sealed class DependencyThatIsNotRegisteredAsService;

sealed class TestGrainStorageOptions
{
    public string? TenantSetting { get; set; }
}

sealed class TestGrainStorageOptionsValidator(TestGrainStorageOptions options, string name) : IConfigurationValidator
{
    public void ValidateConfiguration()
    {
        if (options.TenantSetting is null)
            throw new OrleansConfigurationException($"{nameof(TestGrainStorageOptions.TenantSetting)} is not set for storage provider {name}");
    }
}

/// <summary>A storage provider with a constructor parameter that is not registered as a service - like e.g. the Orleans Azure Blob storage provider has</summary>
sealed class TestGrainStorage(string name, TestGrainStorageOptions options, DependencyThatIsNotRegisteredAsService dependency) : IGrainStorage
{
    readonly ConcurrentDictionary<(string StateName, GrainId GrainId), object?> states = new();

    internal string Name => name;

    internal TestGrainStorageOptions Options => options;

    internal DependencyThatIsNotRegisteredAsService Dependency => dependency;

    /// <remarks>static can be used to access the provider instances in tests, because the tests run the storage provider in-process</remarks>
    internal static ConcurrentQueue<TestGrainStorage> ReadBy { get; } = new();

    public Task ReadStateAsync<T>(string stateName, GrainId grainId, IGrainState<T> grainState)
    {
        ReadBy.Enqueue(this);
        if (states.TryGetValue((stateName, grainId), out object? state))
            grainState.State = (T)state!;
        return Task.CompletedTask;
    }

    public Task WriteStateAsync<T>(string stateName, GrainId grainId, IGrainState<T> grainState)
    {
        states[(stateName, grainId)] = grainState.State;
        return Task.CompletedTask;
    }

    public Task ClearStateAsync<T>(string stateName, GrainId grainId, IGrainState<T> grainState)
    {
        _ = states.TryRemove((stateName, grainId), out _);
        return Task.CompletedTask;
    }
}
