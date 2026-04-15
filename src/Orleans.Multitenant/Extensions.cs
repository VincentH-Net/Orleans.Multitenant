using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Orleans.Multitenant.Internal;
using Orleans.Providers;
using Orleans.Storage;

namespace Orleans.Multitenant;

public static class SiloBuilderExtensions
{
    /// <summary>Configure silo to use tenant separation for grain communication: grain calls and streams (the latter if used together with <see cref="AddMultitenantStreams(ISiloBuilder, string, Func{ISiloBuilder, string, ISiloBuilder})"/>)</summary>
    /// <param name="builder">The silo builder</param>
    /// <param name="crossTenantAuthorizerFactory">A factory to instantiate a custom <see cref="ICrossTenantAuthorizer"/> implementation (optional; default is no cross-tenant communication allowed)</param>
    /// <param name="grainCallTenantSeparatorFactory">A factory to instantiate a custom <see cref="IGrainCallTenantSeparator"/> implementation (optional; default is to consider calls to interfaces outside the Orleans namespace as tenant separated)</param>
    /// <returns>The same instance of the <see cref="ISiloBuilder"/> for chaining</returns>
    /// <remarks>If this method is not used, there will be no separation of grain communication between tenants - grains and streams of all tenants will be able to communicate unrestricted</remarks>
    public static ISiloBuilder AddMultitenantCommunicationSeparation(
        this ISiloBuilder builder,
        Func<IServiceProvider, ICrossTenantAuthorizer>? crossTenantAuthorizerFactory = null,
        Func<IServiceProvider, IGrainCallTenantSeparator>? grainCallTenantSeparatorFactory = null)
    => builder
        .ConfigureServices(services => services
            .AddSingleton(crossTenantAuthorizerFactory ?? (_ => new DefaultCrossTenantAccessAuthorizer()))
            .AddSingleton(grainCallTenantSeparatorFactory ?? (_ => new DefaultGrainCallTenantSeparator())))
        .AddIncomingGrainCallFilter<TenantSeparatingCallFilter>();

    /// <summary>
    /// Configure silo to use a specific grain storage provider as the default grain storage, with tenant separation<br />
    /// Allows any storage provider type to have one instance per tenant, separately configured (e.g. to have a separate table name based in the tenant ID)
    /// </summary>
    /// <typeparam name="TGrainStorage">The provider-specific grain storage type, e.g. Orleans.Storage.MemoryGrainStorage or Orleans.Storage.AzureTableGrainStorage</typeparam>
    /// <typeparam name="TGrainStorageOptions">The provider-specific grain storage options type, e.g. Orleans.Storage.MemoryGrainStorageOptions or Orleans.Storage.AzureTableStorageOptions</typeparam>
    /// <typeparam name="TGrainStorageOptionsValidator">The provider-specific grain storage options validator type, e.g. Orleans.Storage.MemoryGrainStorageOptionsValidator or Orleans.Storage.AzureTableGrainStorageOptionsValidator</typeparam>
    /// <param name="builder">The silo builder</param>
    /// <param name="addStorageProvider">
    /// Function to register a regular (tenant unaware) storage provider, using the provider name parameter that is passed into the function<br />
    /// You can place the same statement(s) in this function that you would normally use to register the storage provider without multi tenancy<br />
    /// This storage provider instance will not be used to store state - it is only called at silo initialization, to ensure that any shared dependencies needed by the tenant-specific storage provider instances are initialized
    /// </param>
    /// <param name="configureTenantOptions">Action to configure the supplied <typeparamref name="TGrainStorageOptions"/> based on the supplied tenant ID (e.g. use the tenant ID in a storage table name to realize separate storage per tenant)</param>
    /// <param name="getProviderParameters">Optional factory to transform or add constructor parameters for tenant grain storage providers; for details see <see cref="GrainStorageProviderParametersFactory{TGrainStorageOptions}"/><br />When omitted, only <typeparamref name="TGrainStorageOptions"/> is passed in</param>
    /// <param name="configureOptions">Action to configure the <see cref="MultitenantStorageOptions"/></param>
    /// <returns>The same instance of the <see cref="ISiloBuilder"/> for chaining</returns>
    public static ISiloBuilder AddMultitenantGrainStorageAsDefault<TGrainStorage, TGrainStorageOptions, TGrainStorageOptionsValidator>(
        this ISiloBuilder builder,
        Func<ISiloBuilder, string, ISiloBuilder> addStorageProvider,
        Action<TGrainStorageOptions, string>? configureTenantOptions = null,
        GrainStorageProviderParametersFactory<TGrainStorageOptions>? getProviderParameters = null,
        Action<OptionsBuilder<MultitenantStorageOptions>>? configureOptions = null)
        where TGrainStorage : IGrainStorage
        where TGrainStorageOptions : class, new()
        where TGrainStorageOptionsValidator : class, IConfigurationValidator
    => builder.AddMultitenantGrainStorage<TGrainStorage, TGrainStorageOptions, TGrainStorageOptionsValidator>(
        ProviderConstants.DEFAULT_STORAGE_PROVIDER_NAME,
        (services, name) => { _ = addStorageProvider(builder, name); return services; },
        configureTenantOptions,
        getProviderParameters,
        configureOptions);

    /// <summary>
    /// Configure silo to use a specific grain storage provider type as a named grain storage provider, with tenant separation<br />
    /// Allows any storage provider type to have one instance per tenant, separately configured (e.g. to have a separate table name based in the tenant ID)
    /// </summary>
    /// <typeparam name="TGrainStorage">The provider-specific grain storage type, e.g. Orleans.Storage.MemoryGrainStorage or Orleans.Storage.AzureTableGrainStorage</typeparam>
    /// <typeparam name="TGrainStorageOptions">The provider-specific grain storage options type, e.g. Orleans.Storage.MemoryGrainStorageOptions or Orleans.Storage.AzureTableStorageOptions</typeparam>
    /// <typeparam name="TGrainStorageOptionsValidator">The provider-specific grain storage options validator type, e.g. Orleans.Storage.MemoryGrainStorageOptionsValidator or Orleans.Storage.AzureTableGrainStorageOptionsValidator</typeparam>
    /// <param name="builder">The silo builder</param>
    /// <param name="name">The storage provider name</param>
    /// <param name="addStorageProvider">
    /// Function to register a regular (tenant unaware) storage provider, using the provider name parameter that is passed into the function<br />
    /// You can place the same statement(s) in this function that you would normally use to register the storage provider without multi tenancy
    /// This storage provider instance will not be used to store state - it is only called at silo initialization, to ensure that any shared dependencies needed by the tenant-specific storage provider instances are initialized
    /// </param>
    /// <param name="configureTenantOptions">Action to configure the supplied <typeparamref name="TGrainStorageOptions"/> based on the supplied tenant ID (e.g. use the tenant ID in a storage table name to realize separate storage per tenant)</param>
    /// <param name="getProviderParameters">Optional factory to transform or add constructor parameters for tenant grain storage providers; for details see <see cref="GrainStorageProviderParametersFactory{TGrainStorageOptions}"/><br />When omitted, only <typeparamref name="TGrainStorageOptions"/> is passed in</param>
    /// <param name="configureOptions">Action to configure the <see cref="MultitenantStorageOptions"/></param>
    /// <returns>The same instance of the <see cref="ISiloBuilder"/> for chaining</returns>
    public static ISiloBuilder AddMultitenantGrainStorage<TGrainStorage, TGrainStorageOptions, TGrainStorageOptionsValidator>(
        this ISiloBuilder builder,
        string name,
        Func<ISiloBuilder, string, ISiloBuilder> addStorageProvider,
        Action<TGrainStorageOptions, string>? configureTenantOptions = null,
        GrainStorageProviderParametersFactory<TGrainStorageOptions>? getProviderParameters = null,
        Action<OptionsBuilder<MultitenantStorageOptions>>? configureOptions = null)
        where TGrainStorage : IGrainStorage
        where TGrainStorageOptions : class, new()
        where TGrainStorageOptionsValidator : class, IConfigurationValidator
    => builder.ConfigureServices(services => services.AddMultitenantGrainStorage<TGrainStorage, TGrainStorageOptions, TGrainStorageOptionsValidator>(
        name,
        (sevices, name) => { _ = addStorageProvider(builder, name); return sevices; },
        configureTenantOptions,
        getProviderParameters,
        configureOptions));

    /// <summary>Configure silo to use a specific stream provider type as a named stream provider, with tenant separation</summary>
    /// <param name="builder">The silo builder</param>
    /// <param name="name">The stream provider name</param>
    /// <param name="addStreamProvider">
    /// Function to register a regular (tenant unaware) stream provider, using the provider name parameter that is passed into the function<br />
    /// You can place the same statement(s) in this function that you would normally use to register the stream provider without multi tenancy
    /// </param>
    /// <returns>The same instance of the <see cref="ISiloBuilder"/> for chaining</returns>
    public static ISiloBuilder AddMultitenantStreams(
        this ISiloBuilder builder,
        string name,
        Func<ISiloBuilder, string, ISiloBuilder> addStreamProvider)
    {
        ArgumentNullException.ThrowIfNull(addStreamProvider);
        return addStreamProvider(builder, name).AddStreamFilter<TenantSeparatingStreamFilter>(name);
    }
}

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Configure silo services to use a specific grain storage provider as the default grain storage, with tenant separation<br />
    /// Allows any storage provider type to have one instance per tenant, separately configured (e.g. to have a separate table name based in the tenant ID)
    /// </summary>
    /// <typeparam name="TGrainStorage">The provider-specific grain storage type, e.g. Orleans.Storage.MemoryGrainStorage or Orleans.Storage.AzureTableGrainStorage</typeparam>
    /// <typeparam name="TGrainStorageOptions">The provider-specific grain storage options type, e.g. Orleans.Storage.MemoryGrainStorageOptions or Orleans.Storage.AzureTableStorageOptions</typeparam>
    /// <typeparam name="TGrainStorageOptionsValidator">The provider-specific grain storage options validator type, e.g. Orleans.Storage.MemoryGrainStorageOptionsValidator or Orleans.Storage.AzureTableGrainStorageOptionsValidator</typeparam>
    /// <param name="services">The silo services</param>
    /// <param name="addStorageProvider">
    /// Function to register a regular (tenant unaware) storage provider, using the provider name parameter that is passed into the function<br />
    /// You can place the same statement(s) in this function that you would normally use to register the storage provider without multi tenancy<br />
    /// This storage provider instance will not be used to store state - it is only called at silo initialization, to ensure that any shared dependencies needed by the tenant-specific storage provider instances are initialized
    /// </param>
    /// <param name="configureTenantOptions">Action to configure the supplied <typeparamref name="TGrainStorageOptions"/> based on the supplied tenant ID (e.g. use the tenant ID in a storage table name to realize separate storage per tenant)</param>
    /// <param name="getProviderParameters">Optional factory to transform or add constructor parameters for tenant grain storage providers; for details see <see cref="GrainStorageProviderParametersFactory{TGrainStorageOptions}"/><br />When omitted, only <typeparamref name="TGrainStorageOptions"/> is passed in</param>
    /// <param name="configureOptions">Action to configure the <see cref="MultitenantStorageOptions"/></param>
    /// <returns>The same instance of the <see cref="IServiceCollection"/> for chaining</returns>
    public static IServiceCollection AddMultitenantGrainStorageAsDefault<TGrainStorage, TGrainStorageOptions, TGrainStorageOptionsValidator>(
        this IServiceCollection services,
        Func<IServiceCollection, string, IServiceCollection> addStorageProvider,
        Action<TGrainStorageOptions, string>? configureTenantOptions = null,
        GrainStorageProviderParametersFactory<TGrainStorageOptions>? getProviderParameters = null,
        Action<OptionsBuilder<MultitenantStorageOptions>>? configureOptions = null)
        where TGrainStorage : IGrainStorage
        where TGrainStorageOptions : class, new()
        where TGrainStorageOptionsValidator : class, IConfigurationValidator
    => services.AddMultitenantGrainStorage<TGrainStorage, TGrainStorageOptions, TGrainStorageOptionsValidator>(
        ProviderConstants.DEFAULT_STORAGE_PROVIDER_NAME,
        addStorageProvider,
        configureTenantOptions,
        getProviderParameters,
        configureOptions);

    /// <summary>
    /// Configure silo services to use a specific grain storage provider as named grain storage, with tenant separation<br />
    /// Allows any storage provider type to have one instance per tenant, separately configured (e.g. to have a separate table name based in the tenant ID)
    /// </summary>
    /// <typeparam name="TGrainStorage">The provider-specific grain storage type, e.g. Orleans.Storage.MemoryGrainStorage or Orleans.Storage.AzureTableGrainStorage</typeparam>
    /// <typeparam name="TGrainStorageOptions">The provider-specific grain storage options type, e.g. Orleans.Storage.MemoryGrainStorageOptions or Orleans.Storage.AzureTableStorageOptions</typeparam>
    /// <typeparam name="TGrainStorageOptionsValidator">The provider-specific grain storage options validator type, e.g. Orleans.Storage.MemoryGrainStorageOptionsValidator or Orleans.Storage.AzureTableGrainStorageOptionsValidator</typeparam>
    /// <param name="services">The silo services</param>
    /// <param name="name">The storage prvider name</param>
    /// <param name="addStorageProvider">
    /// Function to register a regular (tenant unaware) storage provider, using the provider name parameter that is passed into the function<br />
    /// You can place the same statement(s) in this function that you would normally use to register the storage provider without multi tenancy<br />
    /// This storage provider instance will not be used to store state - it is only called at silo initialization, to ensure that any shared dependencies needed by the tenant-specific storage provider instances are initialized
    /// </param>
    /// <param name="configureTenantOptions">Action to configure the supplied <typeparamref name="TGrainStorageOptions"/> based on the supplied tenant ID (e.g. use the tenant ID in a storage table name to realize separate storage per tenant)</param>
    /// <param name="getProviderParameters">Optional factory to transform or add constructor parameters for tenant grain storage providers; for details see <see cref="GrainStorageProviderParametersFactory{TGrainStorageOptions}"/><br />When omitted, only <typeparamref name="TGrainStorageOptions"/> is passed in</param>
    /// <param name="configureOptions">Action to configure the <see cref="MultitenantStorageOptions"/></param>
    /// <returns>The same instance of the <see cref="IServiceCollection"/> for chaining</returns>
    public static IServiceCollection AddMultitenantGrainStorage<TGrainStorage, TGrainStorageOptions, TGrainStorageOptionsValidator>(
        this IServiceCollection services,
        string name,
        Func<IServiceCollection, string, IServiceCollection> addStorageProvider,
        Action<TGrainStorageOptions, string>? configureTenantOptions = null,
        GrainStorageProviderParametersFactory<TGrainStorageOptions>? getProviderParameters = null,
        Action<OptionsBuilder<MultitenantStorageOptions>>? configureOptions = null)
        where TGrainStorage : IGrainStorage
        where TGrainStorageOptions : class, new()
        where TGrainStorageOptionsValidator : class, IConfigurationValidator
    {
        ArgumentNullException.ThrowIfNull(addStorageProvider);
        return addStorageProvider(services, name).AddMultitenantGrainStorage(
                name,
                (services, name) => TenantGrainStorageFactoryFactory.Create<TGrainStorage, TGrainStorageOptions, TGrainStorageOptionsValidator>(services, name, configureTenantOptions, getProviderParameters),
                configureOptions);
    }
}

/// <summary>
/// Factory delegate, used to create parameters for a tenant grain storage provider constructor.<br /> 
/// Allows to e.g. transform the options if the provider expects a different type than <typeparamref name="TGrainStorageOptions"/>,<br />
/// or to retrieve an add extra parameters like <see cref="Configuration.ClusterOptions" />
/// </summary>
/// <typeparam name="TGrainStorageOptions">The provider-specific grain storage options type, e.g. Orleans.Storage.MemoryGrainStorageOptions or Orleans.Storage.AzureTableStorageOptions</typeparam>
/// <param name="services">The silo services</param>
/// <param name="providerName">The name - without the tenant id - of the provider; can be used to access named provider services that are not tenant specific</param>
/// <param name="tenantProviderName">The name - including the tenant id - of the tenant provider; can be used to access named provider services that are tenant specific</param>
/// <param name="options">The options to pass to the provider. Note that configureTenantOptions and options validation have already been executed on this</param>
/// <returns>
/// The complete set of tenant storage provider construction parameters to pass to DI.<br />
/// When this factory is provided, it has full ownership of the parameter list — <paramref name="tenantProviderName"/> is NOT prepended automatically.<br />
/// This allows providers with non-standard constructor signatures (e.g. RavenDbGrainStorage, which does not accept a string name as its first parameter) to work correctly.<br />
/// For standard providers that DO follow the <c>(string name, TOptions options)</c> convention, omit this factory and rely on the default behavior.
/// </returns>
public delegate object[] GrainStorageProviderParametersFactory<in TGrainStorageOptions>(
    IServiceProvider services,
    string providerName,
    string tenantProviderName,
    TGrainStorageOptions options
);

public static class GrainExtensions
{
    /// <summary>Get a tenant stream provider from within a <see cref="Grain"/>, for the tenant that this grain belongs to</summary>
    /// <param name="grain">The tenant specific grain</param>
    /// <param name="name">The name of the stream provider</param>
    /// <returns>The <see cref="TenantStreamProvider"/></returns>
    /// <remarks>Use this overload to access a stream within the same tenant that <paramref name="grain"/> belongs to</remarks>
    public static TenantStreamProvider GetTenantStreamProvider(this Grain grain, string name)
    => new(grain.GetGrainId().TryGetTenantId(), grain.GetStreamProvider(name));

    /// <summary>Get a tenant stream provider from within a <see cref="Grain"/>, for a specified tenant</summary>
    /// <param name="grain">This tenant specific grain</param>
    /// <param name="name">The name of the stream provider</param>
    /// <param name="tenantId">The stream tenant</param>
    /// <returns>The <see cref="TenantStreamProvider"/></returns>
    /// <remarks>Note that a <see cref="ICrossTenantAuthorizer"/> service must be registered that allows access from the tenant to which <paramref name="grain"/> belongs to the <paramref name="tenantId"/> tenant</remarks>
    public static TenantStreamProvider GetTenantStreamProvider(this Grain grain, string name, string tenantId)
    {
        var targetTenantId = tenantId.AsTenantId();
        grain.ThrowIfAccessIsUnauthorized(targetTenantId);

        return new(targetTenantId, grain.GetStreamProvider(name));
    }

    /// <summary>Get a tenant-specific grain factory from within a <see cref="Grain"/>, for the tenant that this grain belongs to</summary>
    /// <param name="grain">This tenant specific grain</param>
    /// <returns>The <see cref="TenantGrainFactory"/></returns>
    public static TenantGrainFactory GetTenantGrainFactory(this Grain grain) => new(grain.GetGrainFactory(), grain);

    /// <summary>Get a tenant-specific grain factory from within a <see cref="Grain"/>, for a specified tenant</summary>
    /// <param name="grain">This tenant specific grain</param>
    /// <param name="tenantId">The factory tenant</param>
    /// <returns>The <see cref="TenantGrainFactory"/></returns>
    /// <remarks>Note that a <see cref="ICrossTenantAuthorizer"/> service must be registered that allows access from the tenant to which <paramref name="grain"/> belongs to the <paramref name="tenantId"/> tenant</remarks>
    public static TenantGrainFactory GetTenantGrainFactory(this Grain grain, string tenantId)
    {
        var targetTenantId = tenantId.AsTenantId();
        grain.ThrowIfAccessIsUnauthorized(targetTenantId);

        return new(grain.GetGrainFactory(), tenantId);
    }
}

public static class ClusterClientExtensions
{
    /// <summary>Get a tenant stream provider from within a <see cref="IClusterClient"/>, for a specified tenant</summary>
    /// <param name="client">This client</param>
    /// <param name="name">The name of the stream provider</param>
    /// <param name="tenantId">The stream tenant</param>
    /// <returns>The <see cref="TenantStreamProvider"/></returns>
    public static TenantStreamProvider GetTenantStreamProvider(this IClusterClient client, string name, string tenantId)
    => new(tenantId.AsTenantId(), client.GetStreamProvider(name));
}

public static class GrainFactoryExtensions
{
    /// <summary>Get a tenant-specific grain factory from an <see cref="IAddressable"/> (i.e. a grain), for the tenant that this grain belongs to</summary>
    /// <param name="factory">A regular (tenant-unaware) grain factory</param>
    /// <param name="grain">This tenant specific grain</param>
    /// <returns>The <see cref="TenantGrainFactory"/></returns>
    public static TenantGrainFactory ForTenantOf(this IGrainFactory factory, IAddressable grain) => new(factory, grain);

    /// <summary>Get a tenant-specific grain factory without an <see cref="IAddressable"/> (e.g. in a cluster client, a stateless worker grain or a grain service), for a specified tenant</summary>
    /// <param name="factory">A regular (tenant-unaware) grain factory</param>
    /// <param name="tenantId">The factory tenant</param>
    /// <returns>The <see cref="TenantGrainFactory"/></returns>
    public static TenantGrainFactory ForTenant(this IGrainFactory factory, string tenantId) => new(factory, tenantId);
}

public static class AddressableExtensions
{
    /// <summary>Use e.g. in a grain to pass the grain's tenant ID as a parameter in a call to a stateless worker grain or an external service</summary>
    /// <returns>the tenant id of <paramref name="grain"/></returns>
    /// <remarks>
    /// Note that if the tenant id is null, the grain was not created with a <see cref="TenantGrainFactory"/>.
    /// Such grains are supported in a multitenant context, but only if their grain key has a value for which <see cref="GetTenantId(IAddressable)"/> returns null.
    /// </remarks>
    public static string? GetTenantId(this IAddressable grain)
    => grain.GetGrainId().TryGetTenantId().TenantIdString();

    /// <summary>Get the part of the grain key that identifies it within it's tenant</summary>
    /// <param name="grain">This <see cref="IAddressable"/>, i.e. grain</param>
    /// <returns>The key within the tenant. This corresponds to the keyWithinTenant parameter of <see cref="TenantGrainFactory.GetGrain(Type, string)"/></returns>
    public static string GetKeyWithinTenant(this IAddressable grain)
    => grain.GetGrainId().Key.Value.Span.GetKey();
}

public static class GrainIdExtensions
{
    /// <summary>Get the tenant id part of the <see cref="GrainId"/> key</summary>
    /// <param name="grainId">This grain id</param>
    /// <returns>The tenant id</returns>
    public static string? GetTenantId(this GrainId grainId)
    => grainId.TryGetTenantId().TenantIdString();

    /// <summary>Get the part of the <see cref="GrainId"/> key that identifies it within it's tenant</summary>
    /// <param name="grainId">This grain id</param>
    /// <returns>The key within the tenant. This corresponds to the keyWithinTenant parameter of <see cref="TenantGrainFactory.GetGrain(Type, string)"/></returns>
    public static string GetKeyWithinTenant(this GrainId grainId)
    => grainId.Key.Value.Span.GetKey();
}

public static class StreamIdExtensions
{
    /// <summary>Get the tenant id part of the <see cref="StreamId"/> key</summary>
    /// <param name="streamId">This stream id</param>
    /// <returns>The tenant id</returns>
    public static string? GetTenantId(this StreamId streamId)
    => streamId.Key.Span.TenantIdString();

    /// <summary>Get the part of the <see cref="StreamId"/> key that identifies it within it's tenant</summary>
    /// <param name="streamId">This stream id</param>
    /// <returns>The key within the tenant. This corresponds to the keyWithinTenant parameter of <see cref="TenantStreamProvider.GetStream{T}(string, string)"/></returns>
    public static string GetKeyWithinTenant(this StreamId streamId)
    => streamId.Key.Span.GetKey();
}
