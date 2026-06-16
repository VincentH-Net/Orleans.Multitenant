using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Orleans.Storage;

namespace Orleans.Multitenant.Internal;

static class TenantGrainStorageFactoryFactory
{
    public static ITenantGrainStorageFactory Create<TGrainStorage, TGrainStorageOptions, TGrainStorageOptionsValidator>(
        IServiceProvider services, 
        string name, 
        Action<TGrainStorageOptions, string>? configureTenantOptions = null, 
        GrainStorageProviderParametersFactory<TGrainStorageOptions>? getProviderParameters = null
    )   where TGrainStorage : IGrainStorage
        where TGrainStorageOptions : class, new()
        where TGrainStorageOptionsValidator : class, IConfigurationValidator
    {
        List<object> parameters = [name];
        if (configureTenantOptions is not null) parameters.Add(configureTenantOptions);
        if (getProviderParameters is not null) parameters.Add(getProviderParameters);
        return ActivatorUtilities.CreateInstance<TenantGrainStorageFactory<TGrainStorage, TGrainStorageOptions, TGrainStorageOptionsValidator>>(services, parameters: [.. parameters]);
    }
}

[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "Class is instantiated through DI")]
sealed class TenantGrainStorageFactory<TGrainStorage, TGrainStorageOptions, TGrainStorageOptionsValidator> : ITenantGrainStorageFactory
    where TGrainStorage : IGrainStorage
    where TGrainStorageOptions : class, new()
    where TGrainStorageOptionsValidator : class, IConfigurationValidator
{
    readonly string name;
    readonly Action<TGrainStorageOptions, string>? configureTenantOptions;
    readonly GrainStorageProviderParametersFactory<TGrainStorageOptions>? getProviderParameters;
    readonly IServiceProvider services;
    readonly ILogger<MultitenantStorage> logger;

    public TenantGrainStorageFactory(string name, IServiceProvider services, ILogger<MultitenantStorage> logger)
    {
        this.name = name;
        this.services = services;
        this.logger = logger;
    }

    public TenantGrainStorageFactory(string name, Action<TGrainStorageOptions, string> configureTenantOptions, IServiceProvider services, ILogger<MultitenantStorage> logger)
    {
        this.name = name;
        this.configureTenantOptions = configureTenantOptions;
        this.services = services;
        this.logger = logger;
    }

    public TenantGrainStorageFactory(string name, GrainStorageProviderParametersFactory<TGrainStorageOptions> getProviderParameters, IServiceProvider services, ILogger<MultitenantStorage> logger)
    {
        this.name = name;
        this.getProviderParameters = getProviderParameters;
        this.services = services;
        this.logger = logger;
    }

    public TenantGrainStorageFactory(string name, Action<TGrainStorageOptions, string> configureTenantOptions, GrainStorageProviderParametersFactory<TGrainStorageOptions> getProviderParameters, IServiceProvider services, ILogger<MultitenantStorage> logger)
    {
        this.name = name;
        this.configureTenantOptions = configureTenantOptions;
        this.getProviderParameters = getProviderParameters;
        this.services = services;
        this.logger = logger;
    }

    // The common Orleans grain storage provider implementation pattern uses a static Create method which takes a provider name parameter,
    // e.g. AzureTableGrainStorageFactory.Create and AdoNetGrainStorageFactory.Create.
    // These Create methods both retrieve the provider options and create the provider instance with those options.
    // We need to change the provider name to include the tenant ID,
    // and we need to call configureTenantOptions after retrieving the options and before the provider instance is created.
    // In addition, we need a way to support providers that take other parameters than tenantProviderName and options
    // To do this, we use below method instead of those Create methods, and offer optional configureTenantOptions and getProviderParameters
    // parameters to allow tenant-specific and provider-specific logic.
    public IGrainStorage Create(string tenantId)
    {
        string tenantProviderName = string.IsNullOrEmpty(tenantId) ? name : $"{tenantId}_{name}";
        logger.CreatingTenantProvider(typeof(TGrainStorage), tenantId, tenantProviderName);

        var options = ActivatorUtilities.CreateInstance<TGrainStorageOptions>(services);

        configureTenantOptions?.Invoke(options, tenantId);

        if (options is IStorageProviderSerializerOptions serializerOptions && serializerOptions.GrainStorageSerializer == default)
            serializerOptions.GrainStorageSerializer = services.GetKeyedService<IGrainStorageSerializer>(name) ?? services.GetRequiredService<IGrainStorageSerializer>();

        // If getProviderParameters is supplied, the caller takes full ownership of the constructor
        // parameter list (needed for providers like RavenDbGrainStorage whose constructors do not
        // follow the conventional (string name, TOptions options) signature).
        // Otherwise, fall back to the original convention: [tenantProviderName, options].
        // This keeps backward compatibility for standard providers (Azure, AdoNet, etc.) and is
        // suitable to PR upstream to VincentH-Net/Orleans.Multitenant.
        List<object> providerParameters = getProviderParameters is not null
            ? [.. getProviderParameters.Invoke(services, name, tenantProviderName, options)]
            : [tenantProviderName, options];

        var configuredOptions = providerParameters.OfType<TGrainStorageOptions>().SingleOrDefault();
        if (configuredOptions is not null)
        { 
            var validator = ActivatorUtilities.CreateInstance<TGrainStorageOptionsValidator>(services, configuredOptions, tenantProviderName);
            validator.ValidateConfiguration();
        }

        return ActivatorUtilities.CreateInstance<TGrainStorage>(services, parameters: [.. providerParameters]);
    }
}
