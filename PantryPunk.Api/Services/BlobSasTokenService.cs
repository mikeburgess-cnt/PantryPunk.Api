using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Sas;

namespace PantryPunk.Api.Services;

public class BlobSasTokenService
{
    private readonly BlobServiceClient _blobServiceClient;
    private readonly string _containerName;
    private readonly ILogger<BlobSasTokenService> _logger;

    private UserDelegationKey? _cachedKey;
    private DateTimeOffset _cacheExpiry = DateTimeOffset.MinValue;
    private readonly SemaphoreSlim _keyLock = new(1, 1);

    public BlobSasTokenService(
        BlobServiceClient blobServiceClient,
        IConfiguration configuration,
        ILogger<BlobSasTokenService> logger)
    {
        _blobServiceClient = blobServiceClient;
        _containerName = configuration["BlobStorage:PhotosContainer"] ?? "photos";
        _logger = logger;
    }

    public async Task<(string Url, DateTimeOffset ExpiresAt)?> GetReadSasUrlAsync(
        string blobUrl,
        TimeSpan ttl,
        CancellationToken ct = default)
    {
        try
        {
            var blobUri = new Uri(blobUrl);
            var blobUriBuilder = new BlobUriBuilder(blobUri);
            var blobClient = _blobServiceClient
                .GetBlobContainerClient(blobUriBuilder.BlobContainerName)
                .GetBlobClient(blobUriBuilder.BlobName);

            var expiresOn = DateTimeOffset.UtcNow.Add(ttl);

            if (blobClient.CanGenerateSasUri)
            {
                // Local Azurite / connection-string path — shared key available
                var sasUri = blobClient.GenerateSasUri(BlobSasPermissions.Read, expiresOn);
                return (sasUri.ToString(), expiresOn);
            }

            // Production path — user delegation key via managed identity
            var key = await GetOrRefreshDelegationKeyAsync(expiresOn, ct);
            var startsOn = DateTimeOffset.UtcNow.AddMinutes(-5);

            var sasBuilder = new BlobSasBuilder
            {
                BlobContainerName = blobUriBuilder.BlobContainerName,
                BlobName = blobUriBuilder.BlobName,
                Resource = "b",
                StartsOn = startsOn,
                ExpiresOn = expiresOn
            };
            sasBuilder.SetPermissions(BlobSasPermissions.Read);

            var sasParams = sasBuilder.ToSasQueryParameters(key, _blobServiceClient.AccountName);
            var signedUri = new UriBuilder(blobUri) { Query = sasParams.ToString() }.Uri;
            return (signedUri.ToString(), expiresOn);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate SAS token for blob URL {BlobUrl}", blobUrl);
            return null;
        }
    }

    private async Task<UserDelegationKey> GetOrRefreshDelegationKeyAsync(
        DateTimeOffset expiresOn,
        CancellationToken ct)
    {
        if (_cachedKey != null && DateTimeOffset.UtcNow < _cacheExpiry)
            return _cachedKey;

        await _keyLock.WaitAsync(ct);
        try
        {
            // Re-check under lock
            if (_cachedKey != null && DateTimeOffset.UtcNow < _cacheExpiry)
                return _cachedKey;

            var keyValidEnd = DateTimeOffset.UtcNow.AddMinutes(55);
            _cachedKey = await _blobServiceClient.GetUserDelegationKeyAsync(
                DateTimeOffset.UtcNow.AddMinutes(-5),
                keyValidEnd,
                ct);
            _cacheExpiry = DateTimeOffset.UtcNow.AddMinutes(50);
            return _cachedKey;
        }
        finally
        {
            _keyLock.Release();
        }
    }
}
