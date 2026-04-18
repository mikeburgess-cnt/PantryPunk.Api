using Microsoft.Azure.Cosmos;
using PantryPunk.Api.Infrastructure;
using PantryPunk.Api.Models.Documents;

namespace PantryPunk.Api.Repositories;

public class UserRepository
{
    private readonly Container _container;

    public UserRepository(CosmosDbContext context)
    {
        _container = context.Users;
    }

    public async Task<UserDocument?> GetByIdAsync(string userId)
    {
        try
        {
            var response = await _container.ReadItemAsync<UserDocument>(userId, new PartitionKey(userId));
            return response.Resource;
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task<UserDocument> UpsertAsync(UserDocument document)
    {
        var response = await _container.UpsertItemAsync(document, new PartitionKey(document.UserId));
        return response.Resource;
    }
}
