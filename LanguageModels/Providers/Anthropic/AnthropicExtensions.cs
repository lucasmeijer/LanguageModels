using Microsoft.Extensions.DependencyInjection;

namespace LanguageModels;

static class AnthropicExtensions
{
    internal static void SetupAnthropic(this IServiceCollection serviceCollection)
    {
        serviceCollection.AddSingleton<AnthropicApiKey>();
        serviceCollection.AddHttpClient<AnthropicHttpClient>((provider, client) =>
        {
            client.BaseAddress = new("https://api.anthropic.com/v1/messages");
            client.DefaultRequestHeaders.Add("x-api-key", provider.GetRequiredService<AnthropicApiKey>().ApiKey);
            client.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");
            client.DefaultRequestHeaders.Add("anthropic-beta", "messages-2023-12-15");
            client.Timeout = TimeSpan.FromMinutes(10);
        });
        serviceCollection.AddSingleton<AnthropicLanguageModels>();
    }
}