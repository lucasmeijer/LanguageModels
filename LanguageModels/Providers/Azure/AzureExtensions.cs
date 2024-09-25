using Microsoft.Extensions.DependencyInjection;

namespace LanguageModels;

static class AzureExtensions
{
    internal static void SetupAzure(this IServiceCollection serviceCollection)
    {
        serviceCollection.AddSingleton<AzureCredentials>();
        serviceCollection.AddHttpClient<AzureHttpClient>((provider, client) =>
        {
            var creds = provider.GetRequiredService<AzureCredentials>();
            client.BaseAddress = new($"https://{creds.ResourceName}.openai.azure.com/openai/deployments/{creds.DeploymentName}/");
            client.DefaultRequestHeaders.Add("api-key", creds.Key);
            client.Timeout = TimeSpan.FromMinutes(10);
        });
    }
}