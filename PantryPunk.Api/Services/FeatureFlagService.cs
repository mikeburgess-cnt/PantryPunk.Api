using Microsoft.FeatureManagement;
using Microsoft.FeatureManagement.FeatureFilters;

namespace PantryPunk.Api.Services;

public class FeatureFlagService
{
    private readonly IFeatureManager _featureManager;

    private static readonly string[] KnownFlags = ["RealtimeSync", "AnnualSubscription", "AppAttest"];

    public FeatureFlagService(IFeatureManager featureManager)
    {
        _featureManager = featureManager;
    }

    public async Task<Dictionary<string, bool>> GetFlagsAsync(string userId, bool isSubscriber)
    {
        var context = new TargetingContext
        {
            UserId = userId,
            Groups = isSubscriber ? ["subscribers"] : ["free"]
        };

        var flags = new Dictionary<string, bool>();
        foreach (var flag in KnownFlags)
        {
            flags[ToCamelCase(flag)] = await _featureManager.IsEnabledAsync(flag, context);
        }

        return flags;
    }

    private static string ToCamelCase(string name)
    {
        if (string.IsNullOrEmpty(name)) return name;
        return char.ToLowerInvariant(name[0]) + name[1..];
    }
}
