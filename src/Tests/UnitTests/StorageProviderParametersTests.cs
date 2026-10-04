using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Orleans.Runtime;
using Orleans.Storage;
using OrleansMultitenant.Tests.Examples.StorageProviderParameters;

namespace OrleansMultitenant.Tests.UnitTests;

public class StorageProviderParametersTests
{
    const string ProviderName = "TestStorage";

    [Fact]
    public async Task ReadStateAsync_ForProviderWithUnregisteredConstructorParameter_ThrowsExceptionThatExplainsGetProviderParameters()
    {
        using var siloHost = BuildSiloHost(getProviderParameters: null);
        var storage = siloHost.Services.GetRequiredKeyedService<IGrainStorage>(ProviderName);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => storage.ReadStateAsync("state", GrainIdForTenant("TenantA"), new GrainState<int>()));

        Assert.StartsWith($"Could not create storage provider {typeof(TestGrainStorage).FullName} for tenant 'TenantA': Unable to resolve service for type '{typeof(DependencyThatIsNotRegisteredAsService).FullName}'", exception.Message, StringComparison.Ordinal);
        Assert.Contains("supply these with the getProviderParameters parameter of AddMultitenantGrainStorage or AddMultitenantGrainStorageAsDefault", exception.Message, StringComparison.Ordinal);
        _ = Assert.IsType<InvalidOperationException>(exception.InnerException);
    }

    [Fact]
    public async Task ReadStateAsync_ForProviderWithConstructorParameterFromGetProviderParameters_UsesTenantProviderWithThatParameter()
    {
        DependencyThatIsNotRegisteredAsService dependency = new();
        using var siloHost = BuildSiloHost((services, providerName, tenantProviderName, options) => [options, dependency]);
        var storage = siloHost.Services.GetRequiredKeyedService<IGrainStorage>(ProviderName);
        var grainId = GrainIdForTenant("TenantB");

        GrainState<int> writtenState = new() { State = 42 }, readState = new();
        await storage.WriteStateAsync("state", grainId, writtenState);
        await storage.ReadStateAsync("state", grainId, readState);

        Assert.Equal(42, readState.State);
        var tenantProvider = Assert.Single(TestGrainStorage.ReadBy, provider => provider.Dependency == dependency);
        Assert.Equal($"TenantB_{ProviderName}", tenantProvider.Name);
        Assert.Equal("setting for TenantB", tenantProvider.Options.TenantSetting);
    }

    static GrainId GrainIdForTenant(string tenantId) => GrainId.Create("grain", $"{tenantId}|key");

    /// <summary>A silo host that is built but not started is enough to use a storage provider that needs no silo lifecycle</summary>
    static IHost BuildSiloHost(GrainStorageProviderParametersFactory<TestGrainStorageOptions>? getProviderParameters) => new HostBuilder()
        .UseOrleans(silo => silo
            .UseLocalhostClustering()
            .AddMultitenantGrainStorage<TestGrainStorage, TestGrainStorageOptions, TestGrainStorageOptionsValidator>(
                ProviderName,
                (silo, name) => silo,
                configureTenantOptions: (options, tenantId) => options.TenantSetting = $"setting for {tenantId}",
                getProviderParameters: getProviderParameters))
        .Build();
}
