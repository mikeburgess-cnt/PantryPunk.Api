using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Caching.Memory;
using PantryPunk.Api.Infrastructure;
using PantryPunk.Api.Models.Documents;

namespace PantryPunk.Api.Repositories;

public class AppConfigRepository
{
    private const string CacheKey = "AppConfig:Settings";
    private const string LastKnownGoodKey = "AppConfig:Settings:LKG";
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(30);

    private readonly Container _container = null!;
    private readonly IMemoryCache _cache = null!;
    private readonly ILogger<AppConfigRepository> _logger = null!;

    protected AppConfigRepository() { }

    public AppConfigRepository(CosmosDbContext context, IMemoryCache cache, ILogger<AppConfigRepository> logger)
    {
        _container = context.AppConfig;
        _cache = cache;
        _logger = logger;
    }

    public virtual async Task<IReadOnlyDictionary<string, string?>?> GetSettingsAsync()
    {
        if (_cache.TryGetValue<IReadOnlyDictionary<string, string?>>(CacheKey, out var cached) && cached is not null)
        {
            return cached;
        }

        try
        {
            var response = await _container.ReadItemAsync<AppConfigDocument>(
                AppConfigDocument.DocumentId,
                new PartitionKey(AppConfigDocument.DocumentId));

            var settings = (IReadOnlyDictionary<string, string?>)response.Resource.Settings;
            _cache.Set(CacheKey, settings, CacheTtl);
            _cache.Set(LastKnownGoodKey, settings);
            return settings;
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            _logger.LogWarning("AppConfig document '{Id}' not found in Cosmos.", AppConfigDocument.DocumentId);
            return _cache.Get<IReadOnlyDictionary<string, string?>>(LastKnownGoodKey);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read AppConfig document; falling back to last-known-good.");
            return _cache.Get<IReadOnlyDictionary<string, string?>>(LastKnownGoodKey);
        }
    }
}
