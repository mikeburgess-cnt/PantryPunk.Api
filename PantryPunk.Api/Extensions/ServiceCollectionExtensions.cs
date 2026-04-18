using Azure.Identity;
using Azure.Storage.Blobs;
using Microsoft.Azure.Cosmos;
using PantryPunk.Api.Infrastructure;
using PantryPunk.Api.Repositories;
using PantryPunk.Api.Services;

namespace PantryPunk.Api.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddCosmosDb(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton(sp =>
        {
            var endpoint = configuration["CosmosDb:AccountEndpoint"]
                ?? throw new InvalidOperationException("CosmosDb:AccountEndpoint is not configured.");

            var connectionString = configuration["CosmosDb:ConnectionString"];
            if (!string.IsNullOrEmpty(connectionString))
            {
                // Local development with emulator using connection string
                return new CosmosClient(connectionString, new CosmosClientOptions
                {
                    SerializerOptions = new CosmosSerializationOptions
                    {
                        PropertyNamingPolicy = CosmosPropertyNamingPolicy.CamelCase
                    }
                });
            }

            // Production: use managed identity
            return new CosmosClient(endpoint, new DefaultAzureCredential(), new CosmosClientOptions
            {
                SerializerOptions = new CosmosSerializationOptions
                {
                    PropertyNamingPolicy = CosmosPropertyNamingPolicy.CamelCase
                }
            });
        });

        services.AddSingleton<CosmosDbContext>();
        return services;
    }

    public static IServiceCollection AddBlobStorage(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton(_ =>
        {
            var connectionString = configuration["BlobStorage:ConnectionString"];
            if (!string.IsNullOrEmpty(connectionString))
            {
                // Local development with Azurite
                return new BlobServiceClient(connectionString);
            }

            var accountName = configuration["BlobStorage:AccountName"]
                ?? throw new InvalidOperationException("BlobStorage:AccountName is not configured.");

            return new BlobServiceClient(
                new Uri($"https://{accountName}.blob.core.windows.net"),
                new DefaultAzureCredential());
        });

        return services;
    }

    public static IServiceCollection AddRepositories(this IServiceCollection services)
    {
        services.AddScoped<UserRepository>();
        services.AddScoped<ListRepository>();
        services.AddScoped<ShareRepository>();
        return services;
    }

    public static IServiceCollection AddAppServices(this IServiceCollection services)
    {
        services.AddScoped<UserService>();
        services.AddScoped<ListService>();
        services.AddScoped<ShareService>();
        services.AddScoped<BlobStorageService>();
        return services;
    }
}
