using Microsoft.Azure.Cosmos;
using PantryPunk.Api.Infrastructure;
using PantryPunk.Api.Models.Documents;

namespace PantryPunk.Api.Repositories;

public class ListRepository
{
    private readonly Container _container;

    public ListRepository(CosmosDbContext context)
    {
        _container = context.ShoppingLists;
    }

    public async Task<ShoppingListDocument?> GetByOwnerUserIdAsync(string ownerUserId)
    {
        var query = new QueryDefinition("SELECT * FROM c WHERE c.ownerUserId = @ownerUserId")
            .WithParameter("@ownerUserId", ownerUserId);

        using var iterator = _container.GetItemQueryIterator<ShoppingListDocument>(query);
        if (iterator.HasMoreResults)
        {
            var response = await iterator.ReadNextAsync();
            return response.FirstOrDefault();
        }

        return null;
    }

    public async Task<ShoppingListDocument> CreateAsync(ShoppingListDocument document)
    {
        var response = await _container.CreateItemAsync(document, new PartitionKey(document.ListId));
        return response.Resource;
    }

    public async Task<ShoppingListDocument> ReplaceAsync(ShoppingListDocument document)
    {
        var response = await _container.ReplaceItemAsync(document, document.Id, new PartitionKey(document.ListId));
        return response.Resource;
    }
}
