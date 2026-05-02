using Microsoft.Azure.Cosmos;

namespace PantryPunk.Api.Infrastructure;

public class CosmosDbContext
{
    public Container Users { get; }
    public Container ShoppingLists { get; }
    public Container ShareCodes { get; }
    public Container AppConfig { get; }

    public CosmosDbContext(CosmosClient cosmosClient, IConfiguration configuration)
    {
        var databaseName = configuration["CosmosDb:DatabaseName"]
            ?? throw new InvalidOperationException("CosmosDb:DatabaseName is not configured.");

        var database = cosmosClient.GetDatabase(databaseName);
        Users = database.GetContainer("Users");
        ShoppingLists = database.GetContainer("ShoppingLists");
        ShareCodes = database.GetContainer("ShareCodes");
        AppConfig = database.GetContainer("AppConfig");
    }
}
