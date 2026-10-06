using Microsoft.Extensions.DependencyInjection;
using Orleans.Configuration;
using Orleans.Storage;
using Orleans.TestingHost;

namespace OrleansMultitenant.Tests.UnitTests;

public interface ITestGrain : IGrainWithStringKey
{
    Task UseStorage();
}

public class TestGrain([PersistentState("state")] IPersistentState<TestState> state) : Grain, ITestGrain
{
    public Task UseStorage() => state.ReadStateAsync();
}

[GenerateSerializer]
public class TestState { }

public sealed class GrainStorageProviderParametersTests(GrainStorageProviderParametersTests.ClusterFixture fixture) : IClassFixture<GrainStorageProviderParametersTests.ClusterFixture>
{
    readonly TestCluster cluster = fixture.Cluster;

    [Fact]
    public async Task CustomStorageProvider_IsInstantiatedWithCorrectParameters()
    {
        string tenantId = "TenantA";
        var grain = cluster.Client.ForTenant(tenantId).GetGrain<ITestGrain>(Guid.NewGuid().ToString());

        await grain.UseStorage();

        object[]? parameters = CustomGrainStorage.GetLastParameters();
        Assert.NotNull(parameters);
        Assert.Equal(3, parameters.Length);
        Assert.StartsWith("TenantA_", (string)parameters[0], StringComparison.Ordinal);
        _ = Assert.IsType<CustomOptions>(parameters[1]);
        Assert.Equal("ExtraValue", (string)parameters[2]);
    }

    public class CustomGrainStorage : IGrainStorage
    {
        static object[]? lastParameters;
        public static object[]? GetLastParameters() => lastParameters;

        public CustomGrainStorage(string name, CustomOptions options, string extraParameter)
            => lastParameters = [name, options, extraParameter];

        public Task ClearStateAsync<T>(string stateName, GrainId grainId, IGrainState<T> grainState) => Task.CompletedTask;
        public Task ReadStateAsync<T>(string stateName, GrainId grainId, IGrainState<T> grainState) => Task.CompletedTask;
        public Task WriteStateAsync<T>(string stateName, GrainId grainId, IGrainState<T> grainState) => Task.CompletedTask;
    }

    public class CustomOptions
    {
        public string SomeValue { get; set; } = string.Empty;
    }

    public class CustomOptionsValidator(CustomOptions options, string name) : IConfigurationValidator
    {
        public void ValidateConfiguration()
        {
            _ = options;
            _ = name;
        }
    }

    public sealed class ClusterFixture : IDisposable
    {
        public ClusterFixture()
        {
            var builder = new TestClusterBuilder()
                .AddSiloBuilderConfigurator<SiloConfigurator>();

            Cluster = builder.Build();
            Cluster.Deploy();
        }

        public void Dispose() => Cluster.StopAllSilos();

        public TestCluster Cluster { get; }

        sealed class SiloConfigurator : ISiloConfigurator
        {
            public void Configure(ISiloBuilder siloBuilder) => siloBuilder
                .AddMultitenantGrainStorageAsDefault<CustomGrainStorage, CustomOptions, CustomOptionsValidator>(
                    (siloBuilder, name) => siloBuilder.ConfigureServices(services =>
                        services.AddKeyedSingleton<IGrainStorage>(name, (sp, key) => new CustomGrainStorage((string)key!, new CustomOptions(), "default"))),
                    getProviderParameters: (services, name, tenantProviderName, options) => [tenantProviderName, options, "ExtraValue"]
                );
        }
    }
}
