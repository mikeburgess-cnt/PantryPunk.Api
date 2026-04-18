using Microsoft.Azure.Cosmos;
using PantryPunk.Api.Infrastructure;
using PantryPunk.Api.Models.Documents;

namespace PantryPunk.Api.Repositories;

public class ShareRepository
{
    private readonly Container _container;

    public ShareRepository(CosmosDbContext context)
    {
        _container = context.ShareCodes;
    }

    public async Task<ShareCodeDocument?> GetByCodeAsync(string code)
    {
        // Partition key is /code but id is a GUID — query within the partition
        var query = new QueryDefinition("SELECT * FROM c WHERE c.code = @code")
            .WithParameter("@code", code);

        using var iterator = _container.GetItemQueryIterator<ShareCodeDocument>(query, requestOptions: new QueryRequestOptions
        {
            PartitionKey = new PartitionKey(code)
        });

        if (iterator.HasMoreResults)
        {
            var response = await iterator.ReadNextAsync();
            return response.FirstOrDefault();
        }

        return null;
    }

    public async Task<ShareCodeDocument> CreateAsync(ShareCodeDocument document)
    {
        var response = await _container.CreateItemAsync(document, new PartitionKey(document.Code));
        return response.Resource;
    }

    public async Task<ShareCodeDocument> ReplaceAsync(ShareCodeDocument document)
    {
        var response = await _container.ReplaceItemAsync(document, document.Id, new PartitionKey(document.Code));
        return response.Resource;
    }

    public async Task<List<ShareCodeDocument>> GetByOwnerUserIdAsync(string ownerUserId, bool excludeRevoked = true)
    {
        var queryText = excludeRevoked
            ? "SELECT * FROM c WHERE c.ownerUserId = @ownerUserId AND (NOT IS_DEFINED(c.revokedAt) OR c.revokedAt = null) ORDER BY c.createdAt ASC"
            : "SELECT * FROM c WHERE c.ownerUserId = @ownerUserId ORDER BY c.createdAt ASC";

        var query = new QueryDefinition(queryText)
            .WithParameter("@ownerUserId", ownerUserId);

        var results = new List<ShareCodeDocument>();
        using var iterator = _container.GetItemQueryIterator<ShareCodeDocument>(query);

        while (iterator.HasMoreResults)
        {
            var response = await iterator.ReadNextAsync();
            results.AddRange(response);
        }

        return results;
    }

    public async Task<ShareCodeDocument?> GetByIdAndOwnerAsync(string shareId, string ownerUserId)
    {
        var query = new QueryDefinition("SELECT * FROM c WHERE c.id = @shareId AND c.ownerUserId = @ownerUserId")
            .WithParameter("@shareId", shareId)
            .WithParameter("@ownerUserId", ownerUserId);

        using var iterator = _container.GetItemQueryIterator<ShareCodeDocument>(query);
        if (iterator.HasMoreResults)
        {
            var response = await iterator.ReadNextAsync();
            return response.FirstOrDefault();
        }

        return null;
    }

    public async Task<bool> ActiveCodeExistsAsync(string code)
    {
        var query = new QueryDefinition(
            "SELECT VALUE COUNT(1) FROM c WHERE c.code = @code AND (NOT IS_DEFINED(c.revokedAt) OR c.revokedAt = null) AND c.expiresAt > @now")
            .WithParameter("@code", code)
            .WithParameter("@now", DateTime.UtcNow);

        using var iterator = _container.GetItemQueryIterator<int>(query, requestOptions: new QueryRequestOptions
        {
            PartitionKey = new PartitionKey(code)
        });

        if (iterator.HasMoreResults)
        {
            var response = await iterator.ReadNextAsync();
            return response.FirstOrDefault() > 0;
        }

        return false;
    }
}
