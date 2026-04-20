using Azure.Identity;

namespace PantryPunk.Api.Infrastructure;

public static class KeyVaultConfiguration
{
    public static void AddKeyVault(this ConfigurationManager configuration)
    {
        var keyVaultUri = configuration["KeyVault:Uri"];
        if (string.IsNullOrEmpty(keyVaultUri))
            return;

        configuration.AddAzureKeyVault(
            new Uri(keyVaultUri),
            new DefaultAzureCredential());
    }
}
