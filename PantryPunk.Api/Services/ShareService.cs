using System.Security.Cryptography;
using PantryPunk.Api.Models.Documents;
using PantryPunk.Api.Models.Requests;
using PantryPunk.Api.Models.Responses;
using PantryPunk.Api.Repositories;

namespace PantryPunk.Api.Services;

public class ShareService
{
    private readonly ShareRepository _shareRepository;
    private readonly ListRepository _listRepository;
    private readonly UserService _userService;
    private readonly IConfiguration _configuration;

    private const string CodeCharacters = "ABCDEFGHJKMNPQRSTUVWXYZ23456789"; // Excludes ambiguous: 0/O, 1/I/L
    private const int CodeLength = 6;
    private const int MaxRetries = 5;

    public ShareService(
        ShareRepository shareRepository,
        ListRepository listRepository,
        UserService userService,
        IConfiguration configuration)
    {
        _shareRepository = shareRepository;
        _listRepository = listRepository;
        _userService = userService;
        _configuration = configuration;
    }

    public async Task<GenerateShareCodeResponse> GenerateCodeAsync(string userId, GenerateShareCodeRequest request)
    {
        await _userService.RequireSubscriberAsync(userId);

        var list = await _listRepository.GetByOwnerUserIdAsync(userId)
            ?? throw new InvalidOperationException("Shopping list not found for user.");

        var expiryHours = _configuration.GetValue("PantryPunk:ShareCode:ExpiryHours", 24);

        string code = null!;
        for (int i = 0; i < MaxRetries; i++)
        {
            code = GenerateRandomCode();
            if (!await _shareRepository.ActiveCodeExistsAsync(code))
                break;

            if (i == MaxRetries - 1)
                throw new ConflictException("Could not generate a unique code. Please try again.");
        }

        var now = DateTime.UtcNow;
        var document = new ShareCodeDocument
        {
            Id = Guid.NewGuid().ToString(),
            Code = code,
            ListId = list.ListId,
            OwnerUserId = userId,
            Confirmed = false,
            ExpiresAt = now.AddHours(expiryHours),
            CreatedAt = now
        };

        await _shareRepository.CreateAsync(document);

        return MapToGenerateResponse(document);
    }

    public async Task<(bool success, string? recipientName, string? error)> ConfirmCodeAsync(ConfirmShareCodeRequest request)
    {
        var code = request.Code.Trim().ToUpperInvariant();
        var document = await _shareRepository.GetByCodeAsync(code);

        if (document == null)
            return (false, null, "Invalid, expired, or revoked code");

        if (document.RevokedAt.HasValue)
            return (false, null, "Invalid, expired, or revoked code");

        if (!document.Confirmed && document.ExpiresAt < DateTime.UtcNow)
            return (false, null, "Invalid, expired, or revoked code");

        if (!document.Confirmed)
        {
            document.RecipientName = request.RecipientName.Trim();
            document.Confirmed = true;
            document.ConfirmedAt = DateTime.UtcNow;
            await _shareRepository.ReplaceAsync(document);
        }

        return (true, document.RecipientName, null);
    }

    public async Task<List<ShareCodeResponse>> GetShareCodesAsync(string userId)
    {
        var documents = await _shareRepository.GetByOwnerUserIdAsync(userId);
        return documents.Select(MapToListResponse).ToList();
    }

    public async Task<bool> RevokeAsync(string shareId, string userId)
    {
        var document = await _shareRepository.GetByIdAndOwnerAsync(shareId, userId);
        if (document == null) return false;

        document.RevokedAt = DateTime.UtcNow;
        await _shareRepository.ReplaceAsync(document);
        return true;
    }

    private static string GenerateRandomCode()
    {
        return string.Create(CodeLength, (object?)null, (span, _) =>
        {
            Span<byte> randomBytes = stackalloc byte[CodeLength];
            RandomNumberGenerator.Fill(randomBytes);
            for (int i = 0; i < CodeLength; i++)
                span[i] = CodeCharacters[randomBytes[i] % CodeCharacters.Length];
        });
    }

    private static GenerateShareCodeResponse MapToGenerateResponse(ShareCodeDocument document)
    {
        return new GenerateShareCodeResponse
        {
            ShareId = document.Id,
            Code = document.Code,
            RecipientName = document.RecipientName,
            Confirmed = document.Confirmed,
            ExpiresAt = document.ExpiresAt
        };
    }

    private static ShareCodeResponse MapToListResponse(ShareCodeDocument document)
    {
        return new ShareCodeResponse
        {
            ShareId = document.Id,
            Code = document.Code,
            RecipientName = document.RecipientName,
            Confirmed = document.Confirmed,
            ConfirmedAt = document.ConfirmedAt,
            ExpiresAt = document.ExpiresAt,
            CreatedAt = document.CreatedAt
        };
    }
}

public class ConflictException : Exception
{
    public ConflictException(string message) : base(message) { }
}
