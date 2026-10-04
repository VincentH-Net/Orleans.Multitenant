using Orleans.Configuration;
using Microsoft.OpenApi;
using Orleans.Multitenant;
using Orleans.Storage;
using Orleans4Multitenant.Apis;

var builder = WebApplication.CreateBuilder(args);

// To store grain state in Azure Table Storage, configure a connection string, e.g. for the Azurite emulator:
//   dotnet user-secrets set "Azure:TableStorageConnectionString" "UseDevelopmentStorage=true"
// Without a connection string, grain state is kept in memory, so the example can run without Azure Table Storage
string? tableStorageConnectionString = builder.Configuration["Azure:TableStorageConnectionString"];
bool useTableStorage = !string.IsNullOrWhiteSpace(tableStorageConnectionString);

builder.Host.UseOrleans(silo =>
{
    _ = silo
    .UseLocalhostClustering()
    .AddMultitenantCommunicationSeparation();

    _ = useTableStorage
    ? silo.AddMultitenantGrainStorageAsDefault<AzureTableGrainStorage, AzureTableStorageOptions, AzureTableGrainStorageOptionsValidator>(
        (silo, name) => silo.AddAzureTableGrainStorage(name, options =>
            options.TableServiceClient = new(tableStorageConnectionString)),
        // Called during silo startup, to ensure that any common dependencies
        // needed for tenant-specific provider instances are initialized

        configureTenantOptions: (options, tenantId) =>
        {
            options.TableServiceClient = new(tableStorageConnectionString);
            options.TableName = $"OrleansGrainState{tenantId}";
        }   // Called on the first grain state access for a tenant in a silo,
            // to initialize the options for the tenant-specific provider instance
            // just before it is instantiated
    )
    : silo.AddMultitenantGrainStorageAsDefault<MemoryGrainStorage, MemoryGrainStorageOptions, MemoryGrainStorageOptionsValidator>(
        (silo, name) => silo.AddMemoryGrainStorage(name)
    );
});

// Add services to the container.
builder.Services.AddControllers();
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options => {
    options.IncludeXmlComments(Path.Combine(AppContext.BaseDirectory, $"{System.Reflection.Assembly.GetExecutingAssembly().GetName().Name}.xml"));
    options.SwaggerDoc("v1", new OpenApiInfo { Title = "Example Orleans 10 Multitenant API", Version = "v1" });
    options.OperationFilter<TenantHeader.AddAsOpenApiParameter>();
});

var app = builder.Build();

if (!useTableStorage)
    app.Logger.LogWarning("No Azure:TableStorageConnectionString is configured; grain state is kept in memory and is lost when the application stops");

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    _ = app.UseSwagger()
           .UseSwaggerUI(options => options.EnableTryItOutByDefault());
}

app.UseHttpsRedirection();

app.MapControllers();

app.Run();
sealed partial class Program { } // Fix CA1852
