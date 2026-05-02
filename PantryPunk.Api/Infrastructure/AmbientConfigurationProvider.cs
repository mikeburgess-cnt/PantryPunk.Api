namespace PantryPunk.Api.Infrastructure;

// Per-request configuration overlay backed by AsyncLocal. Middleware sets Current.Value with the
// settings dict loaded from Cosmos; this provider exposes those values to every IConfiguration
// consumer (including Microsoft.FeatureManagement) by sitting last in the IConfigurationRoot chain.
public sealed class AmbientConfigurationProvider : ConfigurationProvider, IConfigurationSource
{
    private static readonly AsyncLocal<IReadOnlyDictionary<string, string?>?> CurrentValue = new();

    public static IReadOnlyDictionary<string, string?>? Current
    {
        get => CurrentValue.Value;
        set => CurrentValue.Value = value;
    }

    public IConfigurationProvider Build(IConfigurationBuilder builder) => this;

    public override bool TryGet(string key, out string? value)
    {
        var current = CurrentValue.Value;
        if (current is not null && current.TryGetValue(key, out value))
        {
            return true;
        }

        value = null;
        return false;
    }

    public override IEnumerable<string> GetChildKeys(IEnumerable<string> earlierKeys, string? parentPath)
    {
        var current = CurrentValue.Value;
        if (current is null)
        {
            return earlierKeys;
        }

        var prefix = string.IsNullOrEmpty(parentPath) ? string.Empty : parentPath + ConfigurationPath.KeyDelimiter;
        var keys = new HashSet<string>(earlierKeys, StringComparer.OrdinalIgnoreCase);

        foreach (var key in current.Keys)
        {
            if (prefix.Length == 0)
            {
                var head = Head(key);
                if (head is not null) keys.Add(head);
            }
            else if (key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                var head = Head(key.Substring(prefix.Length));
                if (head is not null) keys.Add(head);
            }
        }

        return keys;
    }

    private static string? Head(string remainder)
    {
        if (remainder.Length == 0) return null;
        var idx = remainder.IndexOf(ConfigurationPath.KeyDelimiter, StringComparison.Ordinal);
        return idx < 0 ? remainder : remainder.Substring(0, idx);
    }
}
