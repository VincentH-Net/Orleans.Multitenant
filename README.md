# <img src="img/CSharp-Toolkit-Icon.png" alt="Backend Toolkit" width="64px" />Orleans.Multitenant
Secure, flexible tenant separation for Microsoft Orleans 10 on .NET 10

> [![Nuget (with prereleases)](https://img.shields.io/nuget/vpre/Orleans.Multitenant?color=gold&label=NuGet:%20Orleans.Multitenant&style=plastic)](https://www.nuget.org/packages/Orleans.Multitenant)<br />
> (install in silo client and grain implementation projects)

_(Note: this repo was transferred from Applicita to VincentH-Net on March 17, 2025 to reflect who actively maintains it)_

## Summary
[Microsoft Orleans 10](https://github.com/dotnet/orleans/releases) is a great technology for building distributed, cloud-native applications. It was designed to reduce the complexity of building this type of applications for C# developers.

However, creating multi tenant applications with Orleans out of the box requires careful design, complex coding and significant testing to prevent unintentional leakage of communication or stored data across tenants. Orleans.Multitenant adds this capability to Orleans for free, as an uncomplicated, flexible and extensible API that lets developers:

- **Separate storage** per tenant in any Orleans storage provider by configuring the storage provider options per tenant:<br />
  ![Example Azure Table Storage](img/example-azure-table-storage.png)
- **Separate communication** across tenants for grain calls and streams, or allow specific access between tenants:<br /> 
  ![Example Access Authorizer](img/example-access-authorizer.png)<br />

- **Choose where to use** - for part or all of an application; combine regular stream/storage providers with multitenant ones, use tenant-specific grains/streams and tenant unaware ones. Want to add multitenant storage to an existing application? You can bring along existing grain state in the null tenant. Or add a multitenant storage provider and keep the existing non-multitenant provider as well

- **Secure** against development mistakes: unauthorized access to a tenant specific grain or stream throws an `UnauthorizedException`, and using a non-tenant aware API on a tenant aware stream is blocked and logged.

## Requirements
- .NET 10 SDK for building and testing this repository
- Microsoft Orleans 10 packages in consuming applications

## Installation
Install the package in silo, client and grain implementation projects:

```bash
dotnet add package Orleans.Multitenant
```

## Scope and limitations
- Tenant id's are part of the key for a `GrainId` or `StreamId` and can be any string; the same goes for keys within a tenant. The creation and lifecycle management of tenant id's is the responsibility of the application developer; as far as Orleans.Multitenant is concerned, tenants are **virtual** just like grains and streams - so conceptually all possible tenant id's always exist

- Orleans.Multitenant guards against unauthorized access from grains that have a GrainId, since only there a tenant-specific context exists (the grain key contains the tenant id). Guarding against unauthorized tenant access that is not initiated from a tenant grain (e.g. when using a cluster client in an ASP.NET controller, or in a stateless worker grain or a grain service) is the responsibility of the application developer, since what constitutes a tenant context there is application specific

- Only `IGrainWithStringKey` grains can be tenant specific

## Usage
All multitenant features can be independently enabled and configured at silo startup, with the `ISiloBuilder` `AddMultitenant*` extension methods.
See the inline documentation for more details on how to use the APIs that are mentioned in this readme.

### Add multitenant storage
To add tenant storage separation to any Orleans storage provider, use `AddMultitenantGrainStorage` and `AddMultitenantGrainStorageAsDefault` on an `ISiloBuilder` or `IServiceCollection`:

```csharp 
siloBuilder
.AddMultitenantGrainStorageAsDefault<AzureTableGrainStorage, AzureTableStorageOptions, AzureTableGrainStorageOptionsValidator>(
    (silo, name) => silo.AddAzureTableGrainStorage(name, options =>
        options.ConfigureTableServiceClient(tableStorageConnectionString)),
        // Called during silo startup, to ensure that any common dependencies
        // needed for tenant-specific provider instances are initialized

    configureTenantOptions: (options, tenantId) => {
        options.ConfigureTableServiceClient(tableStorageConnectionString);
        options.TableName = $"OrleansGrainState{tenantId}";
    }   // Called on the first grain state access for a tenant in a silo,
        // to initialize the options for the tenant-specific provider instance
        // just before it is instantiated
 )
```

#### Customize storage provider constructor parameters
By default, the parameters passed into the storage provider instance for a tenant are the tenant provider name (which contains the tenant Id) and the tenant options. Some storage providers may expect a different (wrapper) type for the options, or you may want to pass in additional parameters (e.g. `ClusterOptions`).

To do this, you can pass in an optional `GrainStorageProviderParametersFactory<TGrainStorageOptions>? getProviderParameters` parameter.

##### Example: .NET Aspire with Azure Blob Storage for grain state
If you are using the [.NET Aspire Orleans Integration](https://learn.microsoft.com/en-us/dotnet/aspire/frameworks/orleans) to configure the default grain storage for the silo like this:
```csharp
// Add the resources which you will use for Orleans clustering and
// grain state storage.
var storage = builder.AddAzureStorage("orleans").RunAsEmulator();
var clusteringTable = storage.AddTables("clustering");
var grainStorage = storage.AddBlobs("grain-state");

// Add the Orleans resource to the Aspire DistributedApplication
// builder, then configure it with Azure Table Storage for clustering.
var orleans = builder.AddOrleans("default")
                     .WithClustering(clusteringTable);
// Note that we don't call WithGrainStorage here, since Multitenant grain storage is added in the silo code
// where we use the blob client that Aspire configures from above grainStorage resource, which is passed to apis using WithReference below

var yourProject = builder
    .AddProject<Projects. ...>("...")
    .WithReference(orleans)
    .WithReference(grainStorage) // We use this instead of .WithGrainStorage(grainStorage) to pass only the blob client to the Orleans silo
    .WaitFor(clusteringTable)
    .WaitFor(grainStorage);
```
then you can use the following code to configure the tenant-specific storage provider instances for Azure Blob Storage:

```csharp
// Use Aspire to configure the clients for clustering and grain state
builder.AddKeyedAzureTableClient("clustering");
builder.AddKeyedAzureBlobClient("grain-state");

builder.UseOrleans(
    silo => silo
    .AddMultitenantGrainStorageAsDefault<AzureBlobGrainStorage, AzureBlobStorageOptions, AzureBlobStorageOptionsValidator>(
        // Called during silo startup, to ensure that any common dependencies
        // needed for tenant-specific provider instances are initialized
        (silo, name) => silo.AddAzureBlobGrainStorage(name, (OptionsBuilder<AzureBlobStorageOptions> options) =>
            options.Configure<IServiceProvider>((options, services) => 
                options.BlobServiceClient = services.GetRequiredKeyedService<BlobServiceClient>("grain-state")) // Use the BlobServiceClient registered by AddKeyedAzureBlobClient above
        ),

        // Called on the first grain state access for a tenant in a silo,
        // to initialize the options for the tenant-specific provider instance just before it is instantiated
        configureTenantOptions: (options, tenantId) =>
        {
            #pragma warning disable CA1308 // Normalize strings to uppercase
            options.ContainerName += "-" + tenantId.ToLowerInvariant();
            #pragma warning restore CA1308 // Normalize strings to uppercase
        },

        getProviderParameters: (services, providerName, tenantProviderName, options) =>
        {
            options.BlobServiceClient = services.GetRequiredKeyedService<BlobServiceClient>("grain-state"); // Use the BlobServiceClient registered by AddKeyedAzureBlobClient above
            return [options, options.BuildContainerFactory(services, options)];
        }
    )
);
```
Note that you do not need to include the `tenantProviderName` in the returned provider parameters; it is added automatically.

The parameters passed to `getProviderParameters` allow to access relevant services from DI to retrieve additional provider parameters, if needed.

##### Example: ADO.NET for grain state
E.g. the Orleans ADO.NET storage provider constructor expects an `IOptions<AdoNetGrainStorageOptions>` instead of an `AdoNetGrainStorageOptions`. You can use `getProviderParameters` to wrap the `AdoNetGrainStorageOptions` in an `IOptions<AdoNetGrainStorageOptions>`:

```csharp
.AddMultitenantGrainStorageAsDefault<AdoNetGrainStorage, AdoNetGrainStorageOptions, AdoNetGrainStorageOptionsValidator>(
    (silo, name) => silo.AddAdoNetGrainStorage(name, options => options.ConnectionString = sqlConnectionString),

    configureTenantOptions: (options, tenantId) => options.ConnectionString = sqlConnectionString.Replace("[DatabaseName]", tenantId, StringComparison.Ordinal),

    getProviderParameters: (services, providerName, tenantProviderName, options) => [Options.Create(options)]
)
```

### Add multitenant streams
To configure a silo to use a specific stream provider type as a named stream provider with tenant separation, use `AddMultitenantStreams`. Any Orleans stream provider can be used:
```csharp
.AddMultitenantStreams(
    "provider_name", (silo, name) => silo
    .AddMemoryStreams<DefaultMemoryMessageBodySerializer>(name)
    .AddMemoryGrainStorage(name)
 )
```
Both implicit and explicit stream subscriptions are supported.

### Add multitenant communication separation
To configure a silo to use tenant separation for grain communication, use `AddMultitenantCommunicationSeparation` . Separation will be enforced for both grain calls and streams (the latter if used together with `AddMultitenantStreams`)

Optionally pass in an `ICrossTenantAuthorizer` factory and/or an `IGrainCallTenantSeparator` factory, to control which tenants are authorized to communicate, and which grain calls require authorization:
```csharp
.AddMultitenantCommunicationSeparation(_ => new ExtendedCrossTenantAccessAuthorizer())
```
```csharp
class ExtendedCrossTenantAccessAuthorizer : ICrossTenantAuthorizer
{
    internal const string RootTenantId = "RootTenant";

    public bool IsAccessAuthorized(string? sourceTenantId, string? targetTenantId)
    => string.Equals(sourceTenantId, RootTenantId, StringComparison.Ordinal);
    // Allow access from the root tenant to any tenant
}
```

By default different tenants are not authorized to communicate, and only calls to `Orleans.*` grain interfaces are exempted from authorization

- An attempt to make an unauthorized grain call causes an `UnauthorizedAccessException` to be thrown. The call does not reach the target grain
- An unauthorized attempt to access a stream provider using `GetTenantStreamProvider` causes an `UnauthorizedAccessException` to be thrown
- An attempt to publish an event to a multitenant stream without using `GetTenantStreamProvider` (i.e. using the Orleans built-in `GetStreamProvider` API) causes the event to be blocked by the stream filter; an error with event Id `TenantUnawareStreamApiUsed` is logged in the silo log (also see `AddMultitenantStreams`)

### Access tenant grains and streams from a tenant grain
Where a tenant grain is available,

- To access grains within the same tenant from within a `Grain`, use the `Grain` extension method `this.GetTenantGrainFactory()`:
  ```csharp
  var sameTenantGrain = this.GetTenantGrainFactory().GetGrain<IMyGrain>("key_within_tenant");
  ```

- To access grains that belong to another tenant from within a `Grain`, use the `Grain` extension method `this.GetTenantGrainFactory("tenant_id")`:
  ```csharp
  var otherTenantGrain = this.GetTenantGrainFactory("tenant_id").GetGrain<IMyGrain>("key_within_tenant");
  ```

- To access grains within the same tenant that an `IAddressable` (i.e. a grain) belongs to, use the `IGrainFactory` extension method `factory.ForTenantOf(grain)`:
  ```csharp
  var sameTenantGrain = factory.ForTenantOf(grain).GetGrain<IMyGrain>("key_within_tenant");
  ```
  A tenant grain factory is a very lightweight, allocation-free factory wrapper; it can be stored/cached as desired, but it's overhead is extremely low even without that.

- To access streams within the same tenant, use the `Grain` extension method `this.GetTenantStreamProvider("provider_name")`:
  ```csharp
  var sameTenantStream = this.GetTenantStreamProvider("provider_name").GetStream<int>("stream_namespace", "stream_key_within_tenant");
  ```
  A tenant stream provider is a very lightweight, allocation-free stream provider wrapper; it can be stored/cached as desired, but it's overhead is extremely low even without that.

- To access streams that belong to another tenant, use the `Grain` extension method `this.GetTenantStreamProvider("provider_name", "tenant_id"):
  ```csharp
  var otherTenantStream = this.GetTenantStreamProvider("provider_name", "tenant_id").GetStream<int>("stream_namespace", "stream_key_within_tenant");
  ```

When `AddMultitenantCommunicationSeparation` is used, all of the above methods are guarded against unauthorized access.

### Access tenant grains and streams without a tenant grain
Where no tenant grain is available (e.g. in a cluster client, a stateless worker grain or a grain service),
- To access tenant grains, use the `IGrainFactory` extension method `factory.ForTenant("tenant_id")`:<br />
  ```csharp
  var tenantGrain = factory.ForTenant("tenant_id").GetGrain<IMyGrain>("key_within_tenant");
  ```

- To access tenant streams, use the `IClusterClient` extension method `client.GetTenantStreamProvider("provider name", "tenant id"):<br />
  ```csharp
  var tenantStream = client.GetTenantStreamProvider("provider_name", "tenant_id").GetStream<int>("stream_namespace", "stream_key_within_tenant");
  ```

**Note** that guarding against unauthorized tenant access that is not initiated from a tenant grain (e.g. when using a cluster client in an ASP.NET controller, or in a stateless worker grain or a grain service) is the responsibility of the application developer, since what constitutes a tenant context there is application specific

### Grain/stream key and tenant id
Tenant id's are stored in the key of a tenant specific `GrainId` / `StreamId`. Use these methods to access the individual parts of the key:
```csharp
string? GetTenantId(this IAddressable grain);
string  GetKeyWithinTenant(this IAddressable grain);

string? GetTenantId(this GrainId grainId);
string GetKeyWithinTenant(this GrainId grainId);

string? GetTenantId(this StreamId streamId);
string  GetKeyWithinTenant(this StreamId streamId);
```

### The null tenant
Note that a tenant id with value `null` means that a grain was not created with the tenant aware API's as described in this readme. This could e.g. be the case when 3rd party code is responsible for creating the grain keys.

Even though the null tenant cannot be specified in the tenant aware API's, it is a valid tenant Id value in the parameters of the `ICrossTenantAuthorizer.IsAccessAuthorized` callback. This enables support for scenario's like above.

To access null tenant grains, use the Orleans built-in `IGrainFactory`, and register an `ICrossTenantAuthorizer` that allows access between the null tenant and other tenants. You can also exclude specific interface namespaces from the need to be authorized by registering an `IGrainCallTenantSeparator` (see [Add multitenant communication separation](#add-multitenant-communication-separation)).

The `MultitenantStorageOptions.TenantIdForNullTenant` setting specifies the non-null string value representing the null tenant. This value can be specified in `appsettings.json` in the `MultitenantStorage` section, and is passed as the `tenantId` parameter of the `configureTenantOptions` action, which can be specified in `AddMultitenantGrainStorage` methods. This setting allows developers to choose a name for the null tenant in storage that does not conflict with other valid tenant names in the application.

### Tenant unaware streams
To access tenant unaware streams (e.g. streams whose keys are defined by 3rd party code), use the Orleans built-in `IStreamProvider`. There is no need for an `ICrossTenantAuthorizer` to enable this access, because an `IStreamProvider` does not have the `TenantSeparatingStreamFilter` attached.

