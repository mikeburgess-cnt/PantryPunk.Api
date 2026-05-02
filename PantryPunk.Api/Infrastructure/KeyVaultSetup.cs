using Azure.Identity;

namespace PantryPunk.Api.Infrastructure;

public static class KeyVaultSetup
{
    public static void AddKeyVaultSecrets(this WebApplicationBuilder builder)
    {
        var uri = builder.Configuration["KeyVault:Uri"];
        if (string.IsNullOrWhiteSpace(uri))
        {
            // Local development: secrets come from user-secrets / appsettings.Development.json
            return;
        }

        builder.Configuration.AddAzureKeyVault(new Uri(uri), new DefaultAzureCredential());
    }
}
