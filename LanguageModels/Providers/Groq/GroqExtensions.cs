using Microsoft.Extensions.DependencyInjection;

namespace LanguageModels;

public static class GroqExtensions
{
    internal static void SetupGroq(this IServiceCollection serviceCollection)
    {
        serviceCollection.SetupGroqHttpClient();
        serviceCollection.AddSingleton<GroqModels>();
    }

    public static void SetupGroqHttpClient(this IServiceCollection serviceCollection)
    {
        serviceCollection.AddSingleton<GroqApiKey>();
        serviceCollection.AddHttpClient<GroqHttpClient>((provider, client) =>
        {
            client.BaseAddress = new("https://api.groq.com/openai/v1/");
            client.DefaultRequestHeaders.Authorization =
                new("Bearer", provider.GetRequiredService<GroqApiKey>().ApiKey);
            client.Timeout = TimeSpan.FromMinutes(10);
        });
    }
}