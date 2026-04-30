using Azure.Identity;
using Microsoft.FeatureManagement;

namespace PantryPunk.Api.Infrastructure;

public static class AppConfigurationSetup
{
    public static void AddAppConfiguration(this WebApplicationBuilder builder)
    {
        var endpoint = builder.Configuration["AzureAppConfiguration:Endpoint"];
        if (string.IsNullOrEmpty(endpoint))
        {
            // Local development: feature flags come from appsettings.Development.json
            builder.Services.AddFeatureManagement();
            return;
        }

        builder.Configuration.AddAzureAppConfiguration(options =>
        {
            options.Connect(new Uri(endpoint), new DefaultAzureCredential())
                .ConfigureKeyVault(kv => kv.SetCredential(new DefaultAzureCredential()))
                .ConfigureRefresh(refresh =>
                {
                    refresh.Register("PantryPunk:Sentinel", refreshAll: true)
                           .SetRefreshInterval(TimeSpan.FromMinutes(5));
                })
                .UseFeatureFlags(flags =>
                {
                    flags.SetRefreshInterval(TimeSpan.FromMinutes(5));
                });
        });

        builder.Services.AddAzureAppConfiguration();
        builder.Services.AddFeatureManagement();
    }
}
